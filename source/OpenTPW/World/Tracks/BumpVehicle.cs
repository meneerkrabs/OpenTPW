namespace OpenTPW;

/// <summary>
/// One vehicle of a bump-ride controller (original: 172 bytes, 43 words, in a pool of 256): its flags, position and velocity in section
/// units, heading, the aimed buoy and the guests it carries. A water boat uses a subset of the fields; the rest is kept for the kart and
/// bumper-car modes this slice does not run. Everything is integer state, saved verbatim (docs/TRACKS.md).
/// </summary>
public sealed class BumpVehicle
{
	/// <summary>The vehicle exists (+0 bit 0).</summary>
	public const int Live = 0x1;
	/// <summary>Lap mode: the vehicle follows the controller's buoys in order.</summary>
	public const int LapMode = 0x8;
	/// <summary>Random-buoy mode (bumper cars).</summary>
	public const int Wander = 0x10;
	/// <summary>The vehicle heads for the unload point.</summary>
	public const int ToUnload = 0x20;
	/// <summary>The vehicle can steer and thrust.</summary>
	public const int Steerable = 0x4000;
	/// <summary>The aim must be recomputed at the next steering step.</summary>
	public const int Retarget = 0x10000;
	/// <summary>The vehicle aims at an intermediate waypoint toward its target buoy.</summary>
	public const int Waypoint = 0x20000;
	/// <summary>The bumper car chases another car (+0x80) for the countdown in +0x8C.</summary>
	public const int Chasing = 0x4;
	/// <summary>The vehicle was bumped this tick.</summary>
	public const int Bumped = 0x40000;
	/// <summary>The damping is cut to 70 % (kart finish slowdown).</summary>
	public const int Slowed = 0x80000;
	/// <summary>The vehicle was just created: it is launched for the first time.</summary>
	public const int FirstLaunch = 0x100000;
	/// <summary>The jungle bumper car's extra presentation object (+0xC, the wake mesh) exists (flags 0x3000000 at the first launch).</summary>
	public const int Wake = 0x2000000;
	/// <summary>A crash sound already played this tick.</summary>
	public const int Crunched = 0x200000;
	/// <summary>The controller's first vehicle (the one whose model the controller tracks).</summary>
	public const int First = 0x400000;
	/// <summary>Kart flags of the hovering space and fantasy karts (types −9 and −10, set at the first launch): hover with a bob, front and back height only, bank into turns, a shadow instance.</summary>
	public const int HoverKartFlags = 0x3C00;
	public const int Hover = 0x400;
	public const int FrontBackHeight = 0x800;
	public const int Bank = 0x1000;
	public const int Shadow = 0x2000;
	/// <summary>Kart grid slot mode: the vehicle aims at its grid buoy and parks there.</summary>
	public const int GridMode = 0x40;
	/// <summary>The vehicle finished launching.</summary>
	public const int Launched = 0x4000000;
	/// <summary>The vehicle is hidden (0x00548E80): neither it, its shadow nor its riders' heads are drawn, and it takes no part in the vehicle collisions. Only the Space Racers Teleport hides one.</summary>
	public const int Hidden = 0x200;
	/// <summary>The hidden vehicle came into the teleport from its negative side (it shows again past the far side of the second tile).</summary>
	public const int FromNegative = 0x800000;

	/// <summary>Slot in the controller's pool (0..255); vehicles run in slot order.</summary>
	public int Slot { get; internal set; }
	public int Flags { get; set; }
	/// <summary>The model variant: vehicle count modulo the controller's variant count plus its base.</summary>
	public int Variant { get; set; }
	public int X { get; set; }
	public int Y { get; set; }
	public int VX { get; set; }
	public int VY { get; set; }
	/// <summary>The velocity after damping, before the wall and vehicle collisions of the tick (+0x44/+0x48).</summary>
	public int VX0 { get; set; }
	public int VY0 { get; set; }
	public int Speed { get; set; }
	/// <summary>Heading in 512ths of a turn clockwise from +Y.</summary>
	public int Heading { get; set; }
	/// <summary>Wall-hit spin (water only, drives the boat's roll).</summary>
	public int Spin { get; set; }
	public int Roll { get; set; }
	/// <summary>The kart's drawn heading (+0x54): it follows the direction of the velocity by (angle difference × speed / 1000) per tick. Karts only.</summary>
	public int VisualHeading { get; set; }
	/// <summary>The kart's yaw change of the last tick (+0x5C, angle difference × speed / 1000) and its change since the tick before (+0x60); the bank of hovering karts reads them.</summary>
	public int YawRate { get; set; }
	public int YawDelta { get; set; }
	/// <summary>The kart's animation clip (+0x10): 12 standing, 5 driving (karts only).</summary>
	public int Animation { get; set; }
	/// <summary>The effect asked for this tick (+0x14) and the effect latched for playing (+0x18); 1 exhaust puff, 2 horn, 3 crash (karts).</summary>
	public int EffectRequest { get; set; }
	public int EffectLatch { get; set; }
	/// <summary>The height the pose probes last found (+0xA0, model units); presentation state, kept so a probe without a surface reuses it. Not part of the digest.</summary>
	public float Height { get; set; }
	/// <summary>
	/// The engine-space place of the vehicle's sparks node (class 0x100, number 1 on a kart, 2 on a bumper car) as last drawn, or null before a draw or without the node;
	/// presentation state set by the renderer (the break smoke sits there). Not saved, not part of the digest.
	/// </summary>
	public System.Numerics.Vector3? SparksNode { get; set; }
	/// <summary>Which seats of the vehicle's mesh have a head dummy (seat k - 1 holds rider k), set by the renderer from the model; null before the first draw (the controller's SeatHeadSource then answers, and without one no rider is seated).</summary>
	public bool[]? SeatHeads { get; set; }
	/// <summary>The controller the vehicle belongs to (+0x9C).</summary>
	public BumpController? Controller { get; internal set; }
	/// <summary>
	/// The ride whose collision object <see cref="ObjectIndex"/> names when it is not the vehicle's own (the object searches walk the module's shared pool, so a vehicle
	/// can pass into a neighbouring ride's object); null for its own ride.
	/// </summary>
	public BumpController? ObjectOwner { get; internal set; }
	/// <summary>A restored vehicle's saved object owner (attraction id, −1 for its own ride) until the park restore resolves it.</summary>
	internal int RestoredObjectAttraction { get; set; } = -1;
	/// <summary>The vehicle radius in section units.</summary>
	public int Radius { get; set; }
	/// <summary>Per-vehicle thrust added to the controller's (+0x68).</summary>
	public int AccelBonus { get; set; }
	public int TargetX { get; set; }
	public int TargetY { get; set; }
	public int JitterX { get; set; }
	public int JitterY { get; set; }
	/// <summary>Index of the aimed buoy in the controller's list, −1 for none.</summary>
	public int TargetBuoy { get; set; } = -1;
	/// <summary>Laps counter (+0x88): starts at the controller's laps (negative: laps to go), the run ends when it reaches 0.</summary>
	public int Laps { get; set; }
	/// <summary>Buoy countdown (+0x8C).</summary>
	public int BuoyCountdown { get; set; }
	public int Age { get; set; }
	/// <summary>The collision object the vehicle is inside, or −1.</summary>
	public int ObjectIndex { get; set; } = -1;
	/// <summary>Consecutive wall contacts in the current 32-tick window (+0xA4) and the window's tick count (+0xA8).</summary>
	public int WallHits { get; set; }
	public int WallWindow { get; set; }
	/// <summary>The slot of the car this bumper car chases (+0x80, −1 for none); the chase flag 4 is set while it does.</summary>
	public int ChaseTarget { get; set; } = -1;
	/// <summary>Whether the car holds the engine voice (+0x20 non-zero): only the controller's first carrying car starts one, at the first launch of a running ride.</summary>
	public bool Engine { get; set; }
	/// <summary>Whether the car holds a break-smoke emitter (+0x2C): created by BUMP 8 for a car that carries guests, removed by the fix and the unload. Saved, not hashed.</summary>
	public bool Smoke { get; set; }
	/// <summary>The guests aboard, in boarding order.</summary>
	public List<int> Riders { get; } = new();
	/// <summary>
	/// The seat number (the head dummy number, from 1) each rider of <see cref="Riders"/> holds, in the same order; null while the riders sit in their list order (the vehicle hung them itself, 0x549C60, which numbers the
	/// list from its head). A vehicle an original save brought keeps the numbers of its nodes while the loader reverses their order (0x543F24). Presentation: not hashed.
	/// </summary>
	public int[]? SeatNumbers { get; set; }

	/// <summary>The seat (index from 0 into the model's head dummies) of the rider at <paramref name="index"/> of <see cref="Riders"/>.</summary>
	public int SeatOf( int index ) => SeatNumbers is { } numbers && numbers.Length == Riders.Count ? numbers[index] - 1 : index;
	/// <summary>Spray particles the boat made (cosmetic counter).</summary>
	public int Sprays { get; set; }

	public bool IsLive => (Flags & Live) != 0;
}
