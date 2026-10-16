namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private readonly Dictionary<int, EngineVoice> tourVoices = new();
	private readonly Dictionary<int, ParticleRendererModule.EmitterRef> tourEmitters = new();

	/// <summary>Counts of the tour car effects the host played (voices started, sounds, emitters, clip triggers), for the tests and the smoke checks.</summary>
	public (int Engines, int Sounds, int Emitters, int Animations) TourEffectStats { get; private set; }

	/// <summary>The engine voices held per tour car slot (tests).</summary>
	internal IReadOnlyDictionary<int, SoundSentence?> TourEngineSentences => tourVoices.ToDictionary( pair => pair.Key, pair => pair.Value.Sentence );

	/// <summary>The sound-space place of a tour car point (module units: 300 per sound unit, Y the height) from the grid's sound origin, or null headless.</summary>
	// [BIN:TPW-EXE:0x0055C1C7 tour voice place] x, height and z are divided by 300 (multiply by 0x1B4E81B5, sar 5, rounded toward zero) before 0x51C270 and 0x51BFC0
	private Vector3? TourSoundPlace( int x, int y, int z ) => gridSoundOrigin?.Invoke() is { } origin
		? new Vector3( origin.X + x / TourEffects.UnitsPerSoundUnit, origin.Y + y / TourEffects.UnitsPerSoundUnit, origin.Z + z / TourEffects.UnitsPerSoundUnit )
		: null;

	/// <summary>The engine place of a particle-unit point (1024 per model unit) in the tour module's frame (the grid's origin, Y the height), or null headless.</summary>
	private System.Numerics.Vector3? TourParticlePlace( int x, int y, int z )
	{
		if ( gridEngineOrigin?.Invoke() is not { } origin )
			return null;
		const float scale = ObjectPlacement.ModelScale / TourEffects.ParticleUnitsPerModelUnit;
		return new System.Numerics.Vector3( origin.X + x * scale, origin.Y + z * scale, y * scale );
	}

	private void OnTourEffect( TourEffect effect )
	{
		if ( stopped )
			return;
		switch ( effect.Kind )
		{
			case TourEffectKind.EngineStart:
				if ( EventMapVariable( TourEffects.EngineEventVariable ) is { } engineEvent && TourSoundPlace( effect.X, effect.Y, effect.Z ) is { } start
					&& StartEngineVoice( engineEvent, start ) is { } voice )
				{
					if ( tourVoices.Remove( effect.Slot, out var previous ) )
						previous.Stop();
					tourVoices[effect.Slot] = voice;
					TourEffectStats = TourEffectStats with { Engines = TourEffectStats.Engines + 1 };
				}
				break;
			case TourEffectKind.EngineParameter:
				if ( tourVoices.TryGetValue( effect.Slot, out var held ) && EventMapVariable( TourEffects.EngineParameterVariable ) is { } code )
					held.Sentence?.SetParameter( code, effect.Value );
				break;
			case TourEffectKind.EngineMove:
				if ( tourVoices.TryGetValue( effect.Slot, out var moving ) && TourSoundPlace( effect.X, effect.Y, effect.Z ) is { } place )
					moving.MoveTo( place );
				break;
			case TourEffectKind.EngineStop:
				if ( tourVoices.Remove( effect.Slot, out var ended ) )
					ended.Stop();
				break;
			case TourEffectKind.RepairSound:
				TourEffectStats = TourEffectStats with { Sounds = TourEffectStats.Sounds + 1 };
				GameAudio.PlayRseEvent( 5, (uint)effect.Id, TourSoundPlace( effect.X, effect.Y, effect.Z ), SoundSource );
				break;
			case TourEffectKind.EmitterStart:
				if ( tourEmitters.Remove( effect.Slot, out var replaced ) )
					ParticleRendererModule.KillEmitter( replaced );
				TourEffectStats = TourEffectStats with { Emitters = TourEffectStats.Emitters + 1 };
				if ( TourParticlePlace( effect.X, effect.Y, effect.Z ) is { } at && ParticleRendererModule.StartEmitter( effect.Id, at ) is { } emitter )
					tourEmitters[effect.Slot] = emitter;
				break;
			case TourEffectKind.EmitterMove:
				if ( effect.Node is { } node && tourEmitters.TryGetValue( effect.Slot, out var following ) )
					ParticleRendererModule.MoveEmitter( following, node );
				break;
			case TourEffectKind.EmitterStop:
				if ( tourEmitters.Remove( effect.Slot, out var stoppedEmitter ) )
					ParticleRendererModule.KillEmitter( stoppedEmitter );
				break;
			case TourEffectKind.Animation:
				// the clip itself is drawn by the tour renderer (TourCar.AnimationSerial)
				TourEffectStats = TourEffectStats with { Animations = TourEffectStats.Animations + 1 };
				break;
		}
	}

	/// <summary>
	/// After a restore: starts the engine voice of every live car and the emitter of every car that holds one again, at the car's place (the next tick sets the parameter and
	/// the next draw moves the emitter to its node). Nothing of the simulation is touched.
	/// </summary>
	internal void ResumeTourEffects()
	{
		if ( stopped || Tour is not { } tour )
			return;
		for ( var slot = 0; slot < tour.Cars.Count; slot++ )
		{
			var car = tour.Cars[slot];
			if ( !car.IsLive )
				continue;
			if ( !tourVoices.ContainsKey( slot ) )
				OnTourEffect( new TourEffect( TourEffectKind.EngineStart, slot, 0, 0, car.X, car.Y, car.Z ) );
			if ( car.Emitter != 0 && !tourEmitters.ContainsKey( slot ) )
				OnTourEffect( new TourEffect( TourEffectKind.EmitterStart, slot, car.Emitter, 0, unchecked(car.X << 10) / 300, unchecked(car.Y << 10) / 300, unchecked(car.Z << 10) / 300 ) );
		}
	}

	/// <summary>Ends every tour car voice and emitter (the controller's destruction, TOUR 2 0x0055D3D0, or the ride's removal).</summary>
	private void StopTourEffects()
	{
		foreach ( var voice in tourVoices.Values )
			voice.Stop();
		tourVoices.Clear();
		foreach ( var emitter in tourEmitters.Values )
			ParticleRendererModule.KillEmitter( emitter );
		tourEmitters.Clear();
	}
}
