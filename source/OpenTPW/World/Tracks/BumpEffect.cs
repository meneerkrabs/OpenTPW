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
	/// <summary>The engine voice of a kart starts (EventMap slot 0).</summary>
	EngineStart,
	/// <summary>The engine voice's pitch parameter (EventMap VAR_PAR0): <see cref="BumpEffect.Value"/> is speed / 3.</summary>
	EngineParameter,
	/// <summary>The engine voice stops (the unload plays EventMap slot 1 over it).</summary>
	EngineStop,
	/// <summary>The engine voice stops without the stop sound (the bumper cars: the retire routine only stops the voice).</summary>
	EngineHalt,
	/// <summary>The kart's animation clip changed (<see cref="BumpEffect.Id"/> 5 or 12).</summary>
	Animation
}

/// <summary>One cosmetic effect of a vehicle: raised by <see cref="BumpController"/> while it ticks, played by the host (never part of the simulation state).</summary>
/// <param name="Slot">The vehicle's slot in the controller.</param>
/// <param name="X">Vehicle position in section units.</param>
/// <param name="Height">The vehicle's presentation height in model units (the pose probes' last result; 0 in a headless host).</param>
public readonly record struct BumpEffect( BumpEffectKind Kind, int Slot, int Id, int Value, int X, int Y, float Height = 0f );

/// <summary>The effect ids of the kart code (traced from the executable; the cat_rides ids are not mapped to sounds, TRACK-025).</summary>
public static class BumpEffects
{
	/// <summary>Particle library ids (Data/Particle/par_lib.h): 1 Sparks, 2 Smoke, 10 ExhaustPuff, 15 BeamUpCar.</summary>
	public const int ParticleSparks = 1;
	public const int ParticleSmoke = 2;
	public const int ParticleExhaustPuff = 10;
	public const int ParticleBeamUpCar = 15;
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
}
