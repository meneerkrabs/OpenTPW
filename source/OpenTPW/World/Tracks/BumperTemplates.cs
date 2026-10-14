namespace OpenTPW;

/// <summary>
/// One bump-ride controller template (original: 14 records of 0xD0 bytes at 0x764178, indexed by −type − 1): the values a
/// controller starts from. Water types are −5, −8, −12 and −13; kart types −4, −7, −9 and −10; the remaining ones are the
/// bumper-car and other types this slice does not run.
/// </summary>
/// <param name="Type">The controller type (negative), <c>Bumper.BumperType</c> of the ride's .sam.</param>
/// <param name="Laps">+4: a negative value is a lap count (−1 water, −3 karts), a positive one a duration in ticks.</param>
/// <param name="VehicleRadius">+8, in section units (one cell is 0xC00).</param>
/// <param name="HalfWidth">+0xC, the lateral half width of the track; the slack of a boat is the difference to the radius.</param>
/// <param name="ModelBase">+0x14: first vehicle model variant.</param>
/// <param name="ModelCount">+0x18: number of vehicle model variants.</param>
/// <param name="DefaultPerformance">+0x1C: performance at creation (50).</param>
/// <param name="Accel">+0x30 / +0x40: thrust per tick at performance 0 and 100.</param>
/// <param name="Damping">+0x34 / +0x44: velocity factor per 1024 per tick.</param>
/// <param name="Turn">+0x38 / +0x48: heading change per tick in 512ths of a turn.</param>
/// <param name="Restitution">+0x3C / +0x4C: wall and vehicle bounce per 1024.</param>
/// <param name="MaxVehicles">+0x64.</param>
/// <param name="BuoyRadius">+0x68: capture radius of a buoy.</param>
public sealed record BumperTemplate( int Type, int Laps, int VehicleRadius, int HalfWidth, int ModelBase, int ModelCount, int DefaultPerformance,
	(int Min, int Max) Accel, (int Min, int Max) Damping, (int Min, int Max) Turn, (int Min, int Max) Restitution, int MaxVehicles, int BuoyRadius )
{
	/// <summary>Types −5, −8, −12, −13.</summary>
	public bool IsWater => Type is -5 or -8 or -12 or -13;
	/// <summary>Types −4, −7, −9, −10.</summary>
	public bool IsKart => Type is -4 or -7 or -9 or -10;
	public TrackFamily Family => IsWater ? TrackFamily.Water : IsKart ? TrackFamily.Kart : TrackFamily.None;
	/// <summary>The lateral slack inside a track section: half width minus the vehicle radius (257 for the water rides).</summary>
	public int Slack => HalfWidth - VehicleRadius;
}

/// <summary>The controller templates, read from the executable's table (docs/TRACKS.md).</summary>
public static class BumperTemplates
{
	// [BIN:TPW-EXE:0x00764178 controller templates] the 14 records of 0xD0 bytes (type, laps or duration, radii, performance, the min and max pairs, vehicle and buoy limits); the four water types hold identical values, as do the four kart types except the model range
	private static readonly BumperTemplate[] Templates =
	{
		new( -1, 4350, 768, 0, 1, 1, 50, (8, 12), (960, 1010), (8, 14), (1000, 1100), 8, 1024 ),
		new( -2, 4350, 768, 0, 0, 4, 50, (42, 42), (850, 850), (14, 14), (1300, 1300), 6, 1024 ),
		new( -3, 4350, 768, 0, 0, 1, 50, (10, 16), (940, 1000), (6, 12), (1000, 1100), 8, 1024 ),
		new( -4, -3, 900, 2048, 0, 4, 50, (16, 30), (960, 950), (18, 34), (900, 900), 8, 2048 ),
		new( -5, -1, 1024, 1281, 0, 1, 50, (4, 9), (970, 1000), (7, 14), (980, 980), 64, 512 ),
		new( -6, 150, 896, 0, 0, 1, 50, (10, 16), (940, 1000), (6, 12), (1000, 1100), 8, 1024 ),
		new( -7, -3, 900, 2048, 0, 4, 50, (16, 30), (960, 950), (18, 34), (900, 900), 8, 2048 ),
		new( -8, -1, 1024, 1281, 0, 1, 50, (4, 9), (970, 1000), (7, 14), (980, 980), 64, 512 ),
		new( -9, -3, 900, 2048, 1, 4, 50, (16, 30), (960, 950), (18, 34), (900, 900), 8, 2048 ),
		new( -10, -3, 900, 2048, 1, 1, 50, (16, 30), (960, 950), (18, 34), (900, 900), 8, 2048 ),
		new( -11, 150, 896, 0, 0, 1, 50, (10, 16), (940, 1000), (6, 12), (1000, 1100), 8, 1024 ),
		new( -12, -1, 1024, 1281, 0, 1, 50, (4, 9), (970, 1000), (7, 14), (980, 980), 64, 512 ),
		new( -13, -1, 1024, 1281, 0, 1, 50, (4, 9), (970, 1000), (7, 14), (980, 980), 64, 512 ),
		new( -14, 150, 896, 0, 0, 1, 50, (10, 16), (940, 1000), (6, 12), (1000, 1100), 8, 1024 )
	};

	public static IReadOnlyList<BumperTemplate> All => Templates;

	/// <summary>The template of a controller type, or null for a type outside −1..−14.</summary>
	public static BumperTemplate? TryGet( int type ) => type is <= -1 and >= -14 ? Templates[-type - 1] : null;

	public static BumperTemplate Get( int type ) => TryGet( type ) ?? throw new ArgumentOutOfRangeException( nameof( type ), $"Bumper type {type} is not one of −1..−14." );

	/// <summary>
	/// The template of a ride's <c>Bumper.WhichTrackType</c> and <c>Bumper.BumperType</c> (water rides use −5 in the jungle and the
	/// matching type of the other themes), or null when the ride has no kart or water track.
	/// </summary>
	public static BumperTemplate? ForRide( int whichTrackType, int bumperType ) =>
		whichTrackType is 1 or 2 && TryGet( bumperType ) is { } template && template.Family == (whichTrackType == 2 ? TrackFamily.Water : TrackFamily.Kart) ? template : null;
}
