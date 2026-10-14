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
	/// <summary>The vehicle was bumped this tick.</summary>
	public const int Bumped = 0x40000;
	/// <summary>The damping is cut to 70 % (kart finish slowdown).</summary>
	public const int Slowed = 0x80000;
	/// <summary>The vehicle was just created: it is launched for the first time.</summary>
	public const int FirstLaunch = 0x100000;
	/// <summary>A crash sound already played this tick.</summary>
	public const int Crunched = 0x200000;
	/// <summary>The controller's first vehicle (the one whose model the controller tracks).</summary>
	public const int First = 0x400000;
	/// <summary>The vehicle finished launching.</summary>
	public const int Launched = 0x4000000;

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
	/// <summary>The guests aboard, in boarding order.</summary>
	public List<int> Riders { get; } = new();
	/// <summary>Spray particles the boat made (cosmetic counter).</summary>
	public int Sprays { get; set; }

	public bool IsLive => (Flags & Live) != 0;
}
