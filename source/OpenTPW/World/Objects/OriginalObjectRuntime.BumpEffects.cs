namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private Func<int, int, float, System.Numerics.Vector3>? bumpToEngine;
	private Func<System.Numerics.Vector3, Vector3?>? engineToSound;
	private Func<int, int, Vector3?>? vehicleToSound;
	private readonly Dictionary<int, EngineVoice> engineVoices = new();
	/// <summary>The break-smoke emitter handle of each vehicle slot (presentation only).</summary>
	private readonly Dictionary<int, ParticleRendererModule.EmitterRef> smokeEmitters = new();

	/// <summary>Counts of the kart effects the host played, for the smoke checks and the tests (sounds submitted, particles requested).</summary>
	public (int Sounds, int Particles, int Engines) BumpEffectStats { get; private set; }

	/// <summary>
	/// Gives the runtime the placement of the ride's vehicles: the engine position of a vehicle point (section units and a model-unit height) and the sound-space
	/// position of an engine position. Presentation only; headless runtimes have none and play nothing positioned.
	/// </summary>
	/// <param name="toVehicleSound">The sound-space place of a vehicle's engine voice at a vehicle point (<see cref="BumpEffects.VoicePlace"/> on the park's sound origin), or null to use <paramref name="toSound"/>.</param>
	internal void SetBumpPlacement( Func<int, int, float, System.Numerics.Vector3> toEngine, Func<System.Numerics.Vector3, Vector3?> toSound, Func<int, int, Vector3?>? toVehicleSound = null )
	{
		bumpToEngine = toEngine;
		engineToSound = toSound;
		vehicleToSound = toVehicleSound;
	}

	/// <summary>A vehicle's engine voice: a sentence with a parameter channel, or a plain voice when the engine event is not a repeating sentence.</summary>
	private sealed record EngineVoice( SoundSentence? Sentence, AudioVoice? Voice )
	{
		public void Stop()
		{
			Sentence?.Stop();
			Voice?.Stop();
		}

		public void MoveTo( Vector3 place )
		{
			Sentence?.MoveTo( place );
			if ( Voice != null )
				Voice.Position = place;
		}
	}

	/// <summary>The engine voices held per vehicle slot (tests).</summary>
	internal IReadOnlyDictionary<int, SoundSentence?> EngineSentences => engineVoices.ToDictionary( pair => pair.Key, pair => pair.Value.Sentence );

	/// <summary>The value of the ride's EventMap child's variable <paramref name="index"/> (by position), or null while the ride has no EventMap script or the index is out of range.</summary>
	// [BIN:TPW-EXE:0x0055A3E0 sound variable] the child script of the ride's script (+0x14) is found and variable index i is read from its array (+0x1C) when 0 ≤ i < the count (+0x8C), else 0 with a log line
	internal int? EventMapVariable( int index )
	{
		if ( Script?.SoundChild is not { } child || index < 0 || index >= child.Variables.Length )
			return null;
		return child.Variables[index];
	}

	/// <summary>Starts a vehicle's engine voice at its place: a sentence with a parameter channel, or a plain voice for a non-sentence event.</summary>
	private EngineVoice? StartEngineVoice( int engineEvent, int x, int y, Vector3? fallback )
	{
		var place = vehicleToSound?.Invoke( x, y ) ?? fallback;
		if ( place is { } at && GameAudio.StartRseSentence( 3, (uint)engineEvent, at ) is { } sentence )
			return new EngineVoice( sentence, null );
		return GameAudio.PlayRseEvent( 3, (uint)engineEvent, place ) is { } voice ? new EngineVoice( null, voice ) : null;
	}

	/// <summary>The EventMap child's event id of a slot (<c>VAR_EVT0…9</c>), or null while the ride has no EventMap script.</summary>
	// [DATA:EventMap.RSE VAR_EVT0…9] the ride's sound child fills the slot table (jungle karts 194 engine, 195 stop, 199 horn; the other themes' ids are in docs/TRACKS.md)
	internal int? EventMapEvent( int slot )
	{
		if ( Script?.SoundChild is not { } child || slot is < 0 or > 9 )
			return null;
		var index = child.GetVariableIndex( $"VAR_EVT{slot}" );
		return index < 0 ? null : child.Variables[index];
	}

	private void OnBumpEffect( BumpEffect effect )
	{
		if ( stopped )
			return;
		var engine = bumpToEngine?.Invoke( effect.X, effect.Y, effect.Height );
		var sound = engine is { } point ? engineToSound?.Invoke( point ) : null;
		switch ( effect.Kind )
		{
			case BumpEffectKind.Particle:
				if ( engine is { } at )
				{
					BumpEffectStats = BumpEffectStats with { Particles = BumpEffectStats.Particles + 1 };
					ParticleRequested?.Invoke( new ParticleEventRequest( AttractionId, 0, 1, 0, effect.Id, 1000, at, null, -1, -1 ) );
				}
				break;
			case BumpEffectKind.EmitterStart:
				// [BIN:TPW-EXE:0x00544D4D smoke start] BUMP 8 creates the emitter at the sparks node's world place (the node at the car's +0x24 in the instance's node table +0x28, its matrix +0x30..+0x38 × 1024); the node position comes from the last draw, the car's point without one
				if ( (effect.Node ?? engine) is { } start && !smokeEmitters.ContainsKey( effect.Slot ) )
				{
					BumpEffectStats = BumpEffectStats with { Particles = BumpEffectStats.Particles + 1 };
					if ( ParticleRendererModule.StartEmitter( effect.Id, start ) is { } started )
						smokeEmitters[effect.Slot] = started;
				}
				break;
			case BumpEffectKind.EmitterMove:
				// [BIN:TPW-EXE:0x00548593 smoke follow] every tick the emitter (+0x2C) moves to the sparks node's world place (0x51FE30), as at its creation
				if ( (effect.Node ?? engine) is { } moved && smokeEmitters.TryGetValue( effect.Slot, out var following ) )
					ParticleRendererModule.MoveEmitter( following, moved );
				break;
			case BumpEffectKind.EmitterStop:
				if ( smokeEmitters.Remove( effect.Slot, out var ended ) )
					ParticleRendererModule.KillEmitter( ended );
				break;
			case BumpEffectKind.GlobalSound:
				// [BIN:TPW-EXE:0x00547170 effect player] the global cat_rides sounds 12 and 24 are submitted through the global-rides route (EVENT type 5), the category the original's DAT_00803A28 names
				BumpEffectStats = BumpEffectStats with { Sounds = BumpEffectStats.Sounds + 1 };
				GameAudio.PlayRseEvent( 5, (uint)effect.Id, sound );
				break;
			case BumpEffectKind.EventSound:
				if ( EventMapEvent( effect.Id ) is { } eventId )
				{
					BumpEffectStats = BumpEffectStats with { Sounds = BumpEffectStats.Sounds + 1 };
					GameAudio.PlayRseEvent( 3, (uint)eventId, sound );
				}
				break;
			case BumpEffectKind.EngineStart:
				// [BIN:TPW-EXE:0x0054A366 engine start] the voice is submitted at (0, 0, 0) (0x51EEB0) and moved to the vehicle's voice place at once (0x51C270, 0x54A39A)
				if ( EventMapEvent( BumpEffects.SlotEngine ) is { } engineEvent && StartEngineVoice( engineEvent, effect.X, effect.Y, sound ) is { } voice )
				{
					// a voice already held by the slot (a restored boat, a reused slot) ends first, so none is left running unreachable
					if ( engineVoices.Remove( effect.Slot, out var previous ) )
						previous.Stop();
					BumpEffectStats = BumpEffectStats with { Engines = BumpEffectStats.Engines + 1 };
					engineVoices[effect.Slot] = voice;
				}
				break;
			case BumpEffectKind.EngineStop:
				if ( engineVoices.Remove( effect.Slot, out var running ) )
					running.Stop();
				if ( EventMapEvent( BumpEffects.SlotStop ) is { } stopEvent )
				{
					BumpEffectStats = BumpEffectStats with { Sounds = BumpEffectStats.Sounds + 1 };
					GameAudio.PlayRseEvent( 3, (uint)stopEvent, sound );
				}
				break;
			case BumpEffectKind.EngineHalt:
				if ( engineVoices.Remove( effect.Slot, out var halted ) )
					halted.Stop();
				break;
			case BumpEffectKind.EngineParameter:
				// [BIN:TPW-EXE:0x00548104 engine voice tick] while the vehicle holds a voice (+0x20) the integration moves it to the vehicle's voice place (0x51C270: (int)(x × 0.0033036), 0, (int)(y × 0.0033036)) and then sets parameter code = EventMap variable 10 (0x55A3E0) to speed / 3 (0x51BC40 -> 0x6B5B80 -> the placeholder's SetParameter 0x6BBB80); the water, kart and bumper branches do the same (0x548132/0x5481E7, 0x5484C7/0x54857C, 0x548A81/0x548B63)
				if ( engineVoices.TryGetValue( effect.Slot, out var engineVoice ) )
				{
					if ( (vehicleToSound?.Invoke( effect.X, effect.Y ) ?? sound) is { } place )
						engineVoice.MoveTo( place );
					if ( EventMapVariable( BumpEffects.EngineParameterVariable ) is { } code )
						engineVoice.Sentence?.SetParameter( code, effect.Value );
				}
				break;
		}
	}

	/// <summary>
	/// After a restore: starts the engine voice of every live kart and boat and the break-smoke emitter of every smoking car again (the voices start at the launch, which a restored race has passed). No launch particle is
	/// raised and no generator is drawn, so the simulation state is untouched.
	/// </summary>
	internal void ResumeBumpEffects()
	{
		if ( stopped || Bump is not { } bump )
			return;
		foreach ( var vehicle in bump.Vehicles )
		{
			if ( vehicle.Smoke && !smokeEmitters.ContainsKey( vehicle.Slot ) && (vehicle.SparksNode ?? bumpToEngine?.Invoke( vehicle.X, vehicle.Y, vehicle.Height )) is { } smokeAt )
			{
				if ( ParticleRendererModule.StartEmitter( BumpEffects.ParticleSmoke, smokeAt ) is { } started )
					smokeEmitters[vehicle.Slot] = started;
			}
		}
		if ( EventMapEvent( BumpEffects.SlotEngine ) is not { } engineEvent )
			return;
		foreach ( var vehicle in bump.Vehicles )
		{
			// a kart's or boat's voice runs for its whole life; a bumper car holds one only while it is flagged (the first carrying car of a running ride)
			if ( engineVoices.ContainsKey( vehicle.Slot ) || bump.Template.IsBumper && !vehicle.Engine )
				continue;
			var engine = bumpToEngine?.Invoke( vehicle.X, vehicle.Y, vehicle.Height );
			var sound = engine is { } point ? engineToSound?.Invoke( point ) : null;
			if ( StartEngineVoice( engineEvent, vehicle.X, vehicle.Y, sound ) is { } voice )
			{
				BumpEffectStats = BumpEffectStats with { Engines = BumpEffectStats.Engines + 1 };
				engineVoices[vehicle.Slot] = voice;
			}
		}
	}

	private void StopBumpEffects()
	{
		foreach ( var emitter in smokeEmitters.Values )
			ParticleRendererModule.KillEmitter( emitter );
		smokeEmitters.Clear();
		foreach ( var voice in engineVoices.Values )
			voice.Stop();
		engineVoices.Clear();
	}
}
