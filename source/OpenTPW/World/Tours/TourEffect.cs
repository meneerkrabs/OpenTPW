namespace OpenTPW;

/// <summary>What a tour car's cosmetic effect asks the host for (docs/TOURS.md, "Sounds, effects and animations").</summary>
public enum TourEffectKind
{
	/// <summary>The car's engine voice starts (EventMap variable 0) at the car.</summary>
	EngineStart,
	/// <summary>The engine voice's parameter (code = EventMap variable 10): <see cref="TourEffect.Value"/> is 80 docked, 15 taking off, 30 flying, 60 flying broken.</summary>
	EngineParameter,
	/// <summary>The engine voice moves to the car (the end of every car's tick).</summary>
	EngineMove,
	/// <summary>The engine voice ends (the car is removed).</summary>
	EngineStop,
	/// <summary>The repair sound: global <c>cat_rides</c> event 0xBB at the car (TOUR 9 1 on a broken first car).</summary>
	RepairSound,
	/// <summary>A persistent emitter (<see cref="TourEffect.Id"/>: 39 FlySmoke, 40 FlyFire) starts at the particle-unit point (<see cref="TourEffect.X"/>, <see cref="TourEffect.Y"/>, <see cref="TourEffect.Z"/>).</summary>
	EmitterStart,
	/// <summary>The car's emitter moves to its drawn node (<see cref="TourEffect.Node"/>, engine coordinates).</summary>
	EmitterMove,
	/// <summary>The car's emitter is destroyed.</summary>
	EmitterStop,
	/// <summary>The car's mesh clip is triggered: <see cref="TourEffect.Id"/> the animation id on channel 0, <see cref="TourEffect.Value"/> 1 when it loops.</summary>
	Animation
}

/// <summary>One cosmetic effect of a tour car: raised by <see cref="TourController"/>, played by the host (never part of the simulation state).</summary>
/// <param name="Slot">The car's slot in the controller.</param>
/// <param name="X">The car's position in module units (300 per model unit; Y is the height), or the emitter's start point in particle units for <see cref="TourEffectKind.EmitterStart"/>.</param>
/// <param name="Node">The engine-space place of the car's smoke node as drawn (<see cref="TourEffectKind.EmitterMove"/>).</param>
public readonly record struct TourEffect( TourEffectKind Kind, int Slot, int Id, int Value, int X, int Y, int Z, System.Numerics.Vector3? Node = null );

/// <summary>The tour cars' effect ids and engine parameters (traced from tp.exe 0x0055A720, 0x0055ABF0, 0x0055DA70 and 0x0055CE60).</summary>
public static class TourEffects
{
	/// <summary>The EventMap variables (by index): 0 the engine event, 10 the engine parameter's code.</summary>
	public const int EngineEventVariable = 0;
	public const int EngineParameterVariable = 10;
	/// <summary>Particle library ids (Data/Particle/par_lib.h): 39 P_EFFECT_FlySmoke (worn), 40 P_EFFECT_FlyFire (broken).</summary>
	public const int ParticleFlySmoke = 39;
	public const int ParticleFlyFire = 40;
	/// <summary>The global <c>cat_rides</c> repair sound.</summary>
	public const int SoundRepair = 0xBB;
	/// <summary>The engine parameter: docked, taking off, flying, flying broken (also landing and landed).</summary>
	public const int ParameterDocked = 80;
	public const int ParameterTakeoff = 15;
	public const int ParameterFlying = 30;
	public const int ParameterBroken = 60;
	/// <summary>Module units per sound unit (the voice place is the car's position / 300, truncated toward zero).</summary>
	public const int UnitsPerSoundUnit = 300;
	/// <summary>Particle units per model unit (the emitter calls take positions × 1024).</summary>
	public const int ParticleUnitsPerModelUnit = 1024;
}
