namespace OpenTPW;

/// <summary>
/// One flying car of a tour ride (the original's 0xE4-byte car record inside the 0x122C-byte controller; the controller holds 20). Positions are
/// in the module's units: 3000 per map cell, 300 per model unit, the second horizontal axis called Z as in the original, Y up. Angles are 4096 per turn.
/// </summary>
// [BIN:TPW-EXE:0x0055A720 car creation] the record's dword fields: +4 x, +8 height, +0xC z, +0x10 heading, +0x14 pitch, +0x18 roll, +0x4C/+0x50/+0x54 target, +0x58/+0x5C/+0x60 previous target, +0x70 turn rate, +0x74 vertical velocity, +0x78 speed, +0x7C thrust, +0x80 state, +0x84 timer, +0x88 patrol corner, +0x8C rider count, +0x90.. riders, +0xB4 leaving flag, +0xB8 broken flag, +0xBC approach heading, +0xD8 random, +0xDC head-node count
public sealed class TourCar
{
	/// <summary>Riders a car carries: the head nodes of the car mesh are numbered from 1 and the script's INCAR is 8 or 9.</summary>
	public const int MaximumRiders = 9;

	// States (+0x80), numbered as in the original's switch in 0x0055ABF0.
	public const int StateFree = 0;
	/// <summary>Standing at the station: boarding, or unloading when the rider count is negative.</summary>
	public const int StateDocked = 1;
	/// <summary>Rising from the station towards the first target.</summary>
	public const int StateTakeoff = 2;
	/// <summary>The ride: flying lattice targets until the timer runs out.</summary>
	public const int StateTour = 3;
	/// <summary>Flying home or idling: heading for the next patrol target.</summary>
	public const int StateReturn = 4;
	/// <summary>Staging far out on the station's approach line (a fixed approach, <c>TOUR 12 1</c>).</summary>
	public const int StateStaging = 5;
	/// <summary>On the approach line, one approach radius out.</summary>
	public const int StateApproach = 6;
	/// <summary>Descending onto the station.</summary>
	public const int StateLanding = 7;
	/// <summary>Circling the station's four corners until the landing slot is free.</summary>
	public const int StatePatrol = 8;
	/// <summary>Leaving the park: an extra car with nobody aboard flies off the edge and is removed.</summary>
	public const int StateLeaving = 9;

	public int State { get; internal set; }
	public int X { get; internal set; }
	public int Y { get; internal set; }
	public int Z { get; internal set; }
	public int Heading { get; internal set; }
	public int Pitch { get; internal set; }
	public int Roll { get; internal set; }
	/// <summary>The pose before the last tick (the original keeps it for the renderer's interpolation).</summary>
	public int PreviousX { get; internal set; }
	public int PreviousY { get; internal set; }
	public int PreviousZ { get; internal set; }
	public int PreviousHeading { get; internal set; }
	public int PreviousPitch { get; internal set; }
	public int PreviousRoll { get; internal set; }
	public int TargetX { get; internal set; }
	public int TargetY { get; internal set; }
	public int TargetZ { get; internal set; }
	public int PreviousTargetX { get; internal set; }
	public int PreviousTargetY { get; internal set; }
	public int PreviousTargetZ { get; internal set; }
	/// <summary>The velocity components of the last tick (+0x64, +0x6C).</summary>
	public int VelocityX { get; internal set; }
	public int VelocityZ { get; internal set; }
	public int Turn { get; internal set; }
	public int VerticalVelocity { get; internal set; }
	public int Speed { get; internal set; }
	public int Thrust { get; internal set; }
	/// <summary>The flight timer in ticks: set when the car takes off, counted down every tick.</summary>
	public int Timer { get; internal set; }
	public int Corner { get; internal set; }
	/// <summary>Riders aboard: positive while loading or flying, negative (minus the riders left to hand out) while unloading.</summary>
	public int RiderCount { get; internal set; }
	public int[] Riders { get; } = new int[MaximumRiders];
	public bool Leaving { get; internal set; }
	public bool Broken { get; internal set; }
	public int ApproachHeading { get; internal set; }
	public int Random { get; internal set; }
	/// <summary>The animation the car's mesh plays (0 idle, 3 docked, 4 take-off, 5 flying, 6 landing, 9 flying broken, 10 repaired) and whether it loops.</summary>
	public int Animation { get; internal set; }
	public bool AnimationLoops { get; internal set; }
	/// <summary>Clip triggers so far (each change of <see cref="Animation"/> or <see cref="AnimationLoops"/> restarts the clip); presentation state, not saved, not hashed.</summary>
	public long AnimationSerial { get; internal set; }
	/// <summary>
	/// The particle id of the emitter the car holds (+0xC4): 0 none, 39 FlySmoke (worn), 40 FlyFire (broken). Presentation state: saved with the car so a resumed park
	/// restarts it, not hashed.
	/// </summary>
	public int Emitter { get; internal set; }

	/// <summary>
	/// Cars created in this slot so far: presentation state only (not saved, not hashed). The renderer snaps a car to its pose on its first draw, as the original does through
	/// the car's first-draw word (+0xC8, set at creation).
	/// </summary>
	public int Generation { get; internal set; }
	/// <summary>The pose last drawn (the original keeps it in car +0x34..+0x48) and the <see cref="Generation"/> it was drawn for; presentation state, not saved.</summary>
	internal TourDrawPose Drawn;
	internal int DrawnGeneration = -1;

	/// <summary>Whether the slot holds a car.</summary>
	public bool IsLive => State != StateFree;
}
