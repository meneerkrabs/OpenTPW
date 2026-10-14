namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private Func<int, int, float, System.Numerics.Vector3>? bumpToEngine;
	private Func<System.Numerics.Vector3, Vector3?>? engineToSound;
	private readonly Dictionary<int, AudioVoice> engineVoices = new();

	/// <summary>Counts of the kart effects the host played, for the smoke checks and the tests (sounds submitted, particles requested).</summary>
	public (int Sounds, int Particles, int Engines) BumpEffectStats { get; private set; }

	/// <summary>
	/// Gives the runtime the placement of the ride's vehicles: the engine position of a vehicle point (section units and a model-unit height) and the sound-space
	/// position of an engine position. Presentation only; headless runtimes have none and play nothing positioned.
	/// </summary>
	internal void SetBumpPlacement( Func<int, int, float, System.Numerics.Vector3> toEngine, Func<System.Numerics.Vector3, Vector3?> toSound )
	{
		bumpToEngine = toEngine;
		engineToSound = toSound;
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
			case BumpEffectKind.GlobalSound:
				// [APPROX:TRACK-025] the global cat_rides sounds 12 and 24 are submitted through the global-rides route (EVENT type 5); which sound each id is was not read from the catalogue
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
				if ( EventMapEvent( BumpEffects.SlotEngine ) is { } engineEvent && GameAudio.PlayRseEvent( 3, (uint)engineEvent, sound ) is { } voice )
				{
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
			case BumpEffectKind.EngineParameter:
				// [APPROX:TRACK-025] the engine's pitch parameter (EventMap VAR_PAR0 = speed / 3) is raised every tick, but the voice is a plain event voice: no parameter reaches it
				break;
		}
	}

	/// <summary>
	/// After a restore: starts the engine voice of every live kart again (the voices start at the launch, which a restored race has passed). No launch particle is
	/// raised and no generator is drawn, so the simulation state is untouched.
	/// </summary>
	internal void ResumeBumpEffects()
	{
		if ( stopped || Bump is not { Template.IsKart: true } bump )
			return;
		if ( EventMapEvent( BumpEffects.SlotEngine ) is not { } engineEvent )
			return;
		foreach ( var vehicle in bump.Vehicles )
		{
			if ( engineVoices.ContainsKey( vehicle.Slot ) )
				continue;
			var engine = bumpToEngine?.Invoke( vehicle.X, vehicle.Y, vehicle.Height );
			var sound = engine is { } point ? engineToSound?.Invoke( point ) : null;
			if ( GameAudio.PlayRseEvent( 3, (uint)engineEvent, sound ) is { } voice )
			{
				BumpEffectStats = BumpEffectStats with { Engines = BumpEffectStats.Engines + 1 };
				engineVoices[vehicle.Slot] = voice;
			}
		}
	}

	private void StopBumpEffects()
	{
		foreach ( var voice in engineVoices.Values )
			voice.Stop();
		engineVoices.Clear();
	}
}
