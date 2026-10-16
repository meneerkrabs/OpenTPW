namespace OpenTPW;

/// <summary>What a bump-ride effect asks the host for (docs/TRACKS.md, "Kart effects").</summary>
public enum BumpEffectKind
{
	/// <summary>A particle from the particle library (<see cref="BumpEffect.Id"/> is the library id) at the vehicle.</summary>
	Particle,
	/// <summary>A sound of the global <c>cat_rides</c> catalogue (<see cref="BumpEffect.Id"/> is the event id) at the vehicle.</summary>
	GlobalSound,
	/// <summary>A sound of the ride's EventMap: <see cref="BumpEffect.Id"/> is the slot (0 engine, 1 stop, 3 horn).</summary>
	EventSound,
	/// <summary>The engine voice of a kart or boat starts (EventMap slot 0).</summary>
	EngineStart,
	/// <summary>The engine voice's tick: it moves to the vehicle and takes its parameter (code = EventMap variable 10): <see cref="BumpEffect.Value"/> is speed / 3.</summary>
	EngineParameter,
	/// <summary>The engine voice stops (the unload plays EventMap slot 1 over it).</summary>
	EngineStop,
	/// <summary>The engine voice stops without the stop sound (the bumper cars: the retire routine only stops the voice).</summary>
	EngineHalt,
	/// <summary>A persistent particle emitter (<see cref="BumpEffect.Id"/> is the library id) starts on the vehicle: the break smoke.</summary>
	EmitterStart,
	/// <summary>The vehicle's emitter moves to the vehicle's position (every tick while it lives).</summary>
	EmitterMove,
	/// <summary>The vehicle's emitter is destroyed (the fix or the unload).</summary>
	EmitterStop,
	/// <summary>The kart's animation clip changed (<see cref="BumpEffect.Id"/> 5 or 12).</summary>
	Animation
}

/// <summary>One cosmetic effect of a vehicle: raised by <see cref="BumpController"/> while it ticks, played by the host (never part of the simulation state).</summary>
/// <param name="Slot">The vehicle's slot in the controller.</param>
/// <param name="X">Vehicle position in section units.</param>
/// <param name="Height">The vehicle's presentation height in model units (the pose probes' last result; 0 in a headless host).</param>
/// <param name="Node">The engine-space place of the vehicle's sparks node as last drawn (<see cref="BumpVehicle.SparksNode"/>), null in a headless host.</param>
public readonly record struct BumpEffect( BumpEffectKind Kind, int Slot, int Id, int Value, int X, int Y, float Height = 0f, System.Numerics.Vector3? Node = null );

/// <summary>The effect ids of the kart code (traced from the executable; the cat_rides ids are not mapped to sounds).</summary>
public static class BumpEffects
{
	/// <summary>Particle library ids (Data/Particle/par_lib.h): 1 Sparks, 2 Smoke, 10 ExhaustPuff, 15 BeamUpCar, 96 SmallSplash.</summary>
	public const int ParticleSparks = 1;
	public const int ParticleSmoke = 2;
	public const int ParticleExhaustPuff = 10;
	public const int ParticleBeamUpCar = 15;
	/// <summary>The boats' spray: library id 96, P_EFFECT_SmallSplash.</summary>
	public const int ParticleSmallSplash = 96;
	/// <summary>Global <c>cat_rides</c> sounds the effect player submits: 12 for a crash, 24 for the exhaust puff.</summary>
	public const int SoundCrunch = 12;
	public const int SoundExhaust = 24;
	/// <summary>EventMap slots: 0 engine, 1 engine stop, 3 horn.</summary>
	public const int SlotEngine = 0;
	public const int SlotStop = 1;
	public const int SlotToot = 3;
	/// <summary>Effect request codes (vehicle +0x14/+0x18): 1 exhaust, 2 horn, 3 crash.</summary>
	public const int RequestExhaust = 1;
	public const int RequestToot = 2;
	public const int RequestCrash = 3;
	/// <summary>The bumper cars' crash request: the same CRUNCH sound as code 3 (global cat_rides 12), without the log line.</summary>
	public const int RequestBumperCrash = 5;
	/// <summary>A crash is loud above this impulse (0x82).</summary>
	public const int CrashImpulse = 130;
	/// <summary>The horn needs a speed above this (0xE6); the exhaust puff needs one above 0x50; the animation switches at 0x29.</summary>
	public const int TootSpeed = 230;
	public const int ExhaustSpeed = 80;
	public const int AnimationSpeed = 40;
	/// <summary>The EventMap variable (by index) whose value is the engine voice's parameter code: 10.</summary>
	public const int EngineParameterVariable = 10;
	/// <summary>Sound units per section unit of a vehicle's engine voice place (the float at 0x700F20).</summary>
	public const float VoiceUnitsPerSectionUnit = 0.0033036007f;

	/// <summary>
	/// The place of a vehicle's engine voice relative to the park's sound origin: (int)(x × 0.0033036) and (int)(y × 0.0033036) on the ground plane, height 0
	/// (the sound's X and Z; the original's voice is not at the vehicle's height).
	/// </summary>
	// [BIN:TPW-EXE:0x0054810F engine voice place] FILD +0x34, FMUL [0x700F20], ftol; FILD +0x38, FMUL [0x700F20], ftol; pushed as (x, 0, y) to 0x51C270, which converts the three ints to floats
	public static (int X, int Z) VoicePlace( int x, int y ) => ((int)(x * (double)VoiceUnitsPerSectionUnit), (int)(y * (double)VoiceUnitsPerSectionUnit));
}
