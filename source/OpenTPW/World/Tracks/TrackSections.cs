namespace OpenTPW;

/// <summary>
/// A collision object of a track section (original: 32 bytes in a pool of 2048). The vehicle must stay inside the keep-in objects
/// (circles clipped to a quadrant, boxes) and outside the keep-out circles. All coordinates are section units: one map cell is 0xC00,
/// 10 world units × 307.2 (docs/TRACKS.md).
/// </summary>
public sealed class CollisionObject
{
	public const int Used = 0x01;
	/// <summary>Active side bits: a point with x ≤ the centre (0x02) / x &gt; the centre (0x04) and y &gt; the centre (0x08) / y ≤ the centre (0x10) lies in the object's quadrants; all four make a full object.</summary>
	public const int XAtMost = 0x02;
	public const int XAbove = 0x04;
	public const int YAbove = 0x08;
	public const int YAtMost = 0x10;
	public const int Sides = 0x1E;
	public const int Box = 0x20;
	public const int KeepOut = 0x40;
	/// <summary>Water crossing (reserved by the original, never set by the section builder this slice runs).</summary>
	public const int Crossing = 0x80;
	public const int AddOn = 0x100;

	public int Flags { get; init; }
	public int X { get; init; }
	public int Y { get; init; }
	/// <summary>Radius of a circle, or the half extent along X of a box (+0xC).</summary>
	public int Extent { get; init; }
	/// <summary>The half extent along Y of a box (+0x10); equals <see cref="Extent"/> for a circle.</summary>
	public int ExtentY { get; init; }
	/// <summary>The index in the controller's object list.</summary>
	public int Index { get; internal set; }
	public TrackSection? Section { get; internal set; }
	public Buoy? Buoy { get; internal set; }

	public bool IsBox => (Flags & Box) != 0;
	public bool IsKeepOut => (Flags & KeepOut) != 0;
	public bool IsFull => (Flags & Sides) == Sides;
}

/// <summary>A waypoint a vehicle steers to (original: 28 bytes in a pool of 1024; flag 1 used, 2 skip, 4 start/finish).</summary>
public sealed class Buoy
{
	public const int Used = 0x01;
	/// <summary>The buoy is a kart grid slot: vehicles never aim at it.</summary>
	public const int Skip = 0x02;
	public const int Finish = 0x04;

	public int Flags { get; init; }
	public int X { get; init; }
	public int Y { get; init; }
	public int Index { get; internal set; }
	public CollisionObject? Object { get; internal set; }
	public bool IsFinish => (Flags & Finish) != 0;
	public bool IsSkipped => (Flags & Skip) != 0;
}

/// <summary>One section of the circuit (original: 40 bytes in a pool of 1024): the kind, the tile corner and the objects and buoy it owns.</summary>
public sealed class TrackSection
{
	/// <summary>Bit of the kind word that suppresses the buoy (the first two straights, the station).</summary>
	public const int NoBuoyBit = 0x10000;

	public int Kind { get; init; }
	public bool NoBuoy { get; init; }
	/// <summary>Tile corner X in section units (tile origin cell × 0xC00).</summary>
	public int X { get; init; }
	public int Y { get; init; }
	public int Segment { get; init; }
	public List<CollisionObject> Objects { get; } = new();
	public Buoy? Buoy { get; internal set; }
	public int KindWord => Kind | (NoBuoy ? NoBuoyBit : 0);
}

/// <summary>
/// Q2 of the track trace: builds the sections, collision objects and buoys of a circuit in the original's fixed-point units. The lateral
/// slack <c>s</c> is the controller's half width minus its vehicle radius (257 for the water rides, 1148 for karts).
/// </summary>
public static class TrackSections
{
	public const int MaximumSections = 1024;
	public const int MaximumCollisionObjects = 2048;
	public const int MaximumBuoys = 1024;
	/// <summary>One cell in section units (10 world units × 307.2).</summary>
	public const int Cell = 0xC00;
	/// <summary>One tile in section units.</summary>
	public const int Tile = 0x1800;
	/// <summary>The straight box's half length: half a tile plus 32 units of overlap with the next section.</summary>
	public const int StraightHalfLength = 0xC20;
	/// <summary>The 45° point of the 0xC00 centreline arc, 3072 × cos 45° = 2172.</summary>
	public const int ArcOffset = 0x87C;

	/// <summary>The result of one build: the sections in walk order and the objects and buoys they own, station buoys first.</summary>
	public sealed record Result( IReadOnlyList<TrackSection> Sections, IReadOnlyList<CollisionObject> Objects, IReadOnlyList<Buoy> Buoys, bool Truncated );

	/// <summary>
	/// Builds the sections of <paramref name="steps"/> for a controller with lateral slack <paramref name="slack"/>, after the
	/// <paramref name="stationBuoys"/> (already numbered first). The first two sections carry no buoy.
	/// </summary>
	// [BIN:TPW-EXE:0x0054B2F0 section builder] the section pool (1024 × 40) refuses a duplicate tile ("Duplicate track section - IGNORED!"); a section holds its kind, tile corner, add-on values and collision objects; the first two straights of the walk carry the no-buoy bit 0x10000
	public static Result Build( IReadOnlyList<TrackStep> steps, int slack, IReadOnlyList<Buoy> stationBuoys )
	{
		var sections = new List<TrackSection>();
		var objects = new List<CollisionObject>();
		var buoys = new List<Buoy>();
		var seen = new HashSet<(int X, int Y)>();
		var truncated = false;
		foreach ( var buoy in stationBuoys )
			AddBuoy( buoys, buoy );
		var straights = 0;
		foreach ( var step in steps )
		{
			var (x0, y0) = (step.X * Cell, step.Y * Cell);
			if ( !seen.Add( (x0, y0) ) )
				continue;
			var kind = step.Kind;
			var noBuoy = false;
			if ( kind is 9 or 10 or 12 or 13 )
			{
				noBuoy = straights < 2;
				straights++;
			}
			if ( sections.Count >= MaximumSections )
			{
				truncated = true;
				break;
			}
			var section = new TrackSection { Kind = kind, NoBuoy = noBuoy, X = x0, Y = y0, Segment = step.Segment };
			sections.Add( section );
			var made = Geometry( section, slack );
			foreach ( var item in made.Objects )
			{
				if ( objects.Count >= MaximumCollisionObjects )
				{
					truncated = true;
					break;
				}
				item.Section = section;
				item.Index = objects.Count;
				objects.Add( item );
				section.Objects.Add( item );
			}
			if ( made.Buoy != null && !noBuoy && buoys.Count >= MaximumBuoys )
			{
				truncated = true;
				break;
			}
			if ( made.Buoy is { } point && !noBuoy )
			{
				point.Object = section.Objects.FirstOrDefault();
				section.Buoy = point;
				if ( point.Object != null )
					point.Object.Buoy = point;
				AddBuoy( buoys, point );
			}
		}
		return new Result( sections, objects, buoys, truncated );
	}

	private static void AddBuoy( List<Buoy> buoys, Buoy buoy )
	{
		buoy.Index = buoys.Count;
		buoys.Add( buoy );
	}

	private static (List<CollisionObject> Objects, Buoy? Buoy) Geometry( TrackSection section, int slack )
	{
		var objects = new List<CollisionObject>();
		var (x0, y0) = (section.X, section.Y);
		Buoy? buoy = null;
		switch ( section.Kind )
		{
			// corners: keep-in 0xC00 + s and keep-out 0xC00 − s around the pivot, both clipped to the tile's quadrant; the buoy sits on the 45° point of the centreline arc
			case 5:
				Corner( objects, slack, x0 + Tile, y0, CollisionObject.XAtMost | CollisionObject.YAbove, -ArcOffset, ArcOffset, out buoy );
				break;
			case 6:
				Corner( objects, slack, x0, y0, CollisionObject.XAbove | CollisionObject.YAbove, ArcOffset, ArcOffset, out buoy );
				break;
			case 7:
				Corner( objects, slack, x0, y0 + Tile, CollisionObject.XAbove | CollisionObject.YAtMost, ArcOffset, -ArcOffset, out buoy );
				break;
			case 8:
				Corner( objects, slack, x0 + Tile, y0 + Tile, CollisionObject.XAtMost | CollisionObject.YAtMost, -ArcOffset, -ArcOffset, out buoy );
				break;
			case 9:
			case 12:
				objects.Add( Straight( x0, y0, StraightHalfLength, slack, section.Kind == 12 ) );
				buoy = Centre( x0, y0 );
				break;
			case 10:
			case 13:
				objects.Add( Straight( x0, y0, slack, StraightHalfLength, section.Kind == 13 ) );
				buoy = Centre( x0, y0 );
				break;
			default:
				// a crossing: one keep-in circle through the lane corners and four keep-out circles at the tile corners
				// [APPROX:TRACK-017] the quadrant clipping of the crossing's four keep-out circles is assumed to follow the tile's interior like a corner's; the trace gives their centres and radius only — evidence needed: the section builder 0x0054B2F0's flag words for kind 11
				objects.Add( new CollisionObject
				{
					Flags = CollisionObject.Used | CollisionObject.Sides,
					X = x0 + Cell,
					Y = y0 + Cell,
					Extent = (int)Math.Sqrt( (double)slack * slack + (double)Cell * Cell ),
					ExtentY = (int)Math.Sqrt( (double)slack * slack + (double)Cell * Cell )
				} );
				KeepOut( objects, slack, x0, y0, CollisionObject.XAbove | CollisionObject.YAbove );
				KeepOut( objects, slack, x0 + Tile, y0, CollisionObject.XAtMost | CollisionObject.YAbove );
				KeepOut( objects, slack, x0, y0 + Tile, CollisionObject.XAbove | CollisionObject.YAtMost );
				KeepOut( objects, slack, x0 + Tile, y0 + Tile, CollisionObject.XAtMost | CollisionObject.YAtMost );
				buoy = Centre( x0, y0 );
				break;
		}
		return (objects, buoy);
	}

	private static Buoy Centre( int x0, int y0 ) => new() { Flags = Buoy.Used, X = x0 + Cell, Y = y0 + Cell };

	private static void Corner( List<CollisionObject> objects, int slack, int pivotX, int pivotY, int quadrant, int buoyX, int buoyY, out Buoy buoy )
	{
		objects.Add( new CollisionObject { Flags = CollisionObject.Used | quadrant, X = pivotX, Y = pivotY, Extent = Cell + slack, ExtentY = Cell + slack } );
		KeepOut( objects, slack, pivotX, pivotY, quadrant );
		buoy = new Buoy { Flags = Buoy.Used, X = pivotX + buoyX, Y = pivotY + buoyY };
	}

	private static void KeepOut( List<CollisionObject> objects, int slack, int x, int y, int quadrant ) =>
		objects.Add( new CollisionObject { Flags = CollisionObject.Used | CollisionObject.KeepOut | quadrant, X = x, Y = y, Extent = Cell - slack, ExtentY = Cell - slack } );

	private static CollisionObject Straight( int x0, int y0, int halfX, int halfY, bool addOn ) => new()
	{
		Flags = CollisionObject.Used | CollisionObject.Sides | CollisionObject.Box | (addOn ? CollisionObject.AddOn : 0),
		X = x0 + Cell,
		Y = y0 + Cell,
		Extent = halfX,
		ExtentY = halfY
	};

	/// <summary>
	/// The station buoys of a water ride: one finish buoy 0xC00 behind the station reference along the heading (the original orients it by the
	/// controller's orientation word, bit 1 the axis and bits 2 and 4 the signs), and the unload point a third of that offset from the station.
	/// </summary>
	// [BIN:TPW-EXE:0x00545890 station buoys] water: the finish buoy (flag 4) at the station ± 0xC00 along the axis the orientation selects, the unload point (+0x90/+0x94) at a third of that offset (integer division), controller +0x74/+0x78 the station
	public static (Buoy Finish, int UnloadX, int UnloadY) WaterStationBuoys( int stationX, int stationY, byte heading )
	{
		var (dx, dy) = (-Cell * TrackHeading.Dx( heading ), -Cell * TrackHeading.Dy( heading ));
		var finish = new Buoy { Flags = Buoy.Used | Buoy.Finish, X = stationX + dx, Y = stationY + dy };
		return (finish, stationX + dx / 3, stationY + dy / 3);
	}

	/// <summary>
	/// The station buoys of a kart ride: <paramref name="slots"/> grid buoys (flag 2, vehicles park on them before the start) in a staggered pair of columns,
	/// then the finish buoy (flag 4) 0xC00 behind the station. Slot i lies (slots − i − 2) × 0x400 along the flow and 0x400 across it, alternating sides
	/// (even slots on the positive side); the orientation word swaps the axes (bit 1) and flips x (bit 2) and y (bit 4). The unload point is the station.
	/// </summary>
	// [BIN:TPW-EXE:0x00545890 station buoys] kart types −4, −7, −9, −10: along = (count − i − 2) × 0x400 with count = controller +0x64, across = (i &amp; 1) × −0x800 + 0x400, swapped when the orientation word is odd, x negated for bit 2 and y for bit 4; each buoy gets flag 2; then the finish buoy at (−0xC00, 0) in the same frame with flag 4; +0x74/+0x78 and the unload point +0x90/+0x94 are the station
	public static IReadOnlyList<Buoy> KartStationBuoys( int stationX, int stationY, int orientation, int slots )
	{
		var buoys = new List<Buoy>();
		for ( var slot = 0; slot < slots; slot++ )
		{
			var along = (slots - slot - 2) * 0x400;
			var across = (slot & 1) * -0x800 + 0x400;
			var (x, y) = Oriented( orientation, along, across );
			buoys.Add( new Buoy { Flags = Buoy.Used | Buoy.Skip, X = stationX + x, Y = stationY + y } );
		}
		var (fx, fy) = Oriented( orientation, -Cell, 0 );
		buoys.Add( new Buoy { Flags = Buoy.Used | Buoy.Finish, X = stationX + fx, Y = stationY + fy } );
		return buoys;
	}

	/// <summary>A point given along the flow and across it, in map axes under an orientation word: bit 1 swaps the axes, bit 2 negates x, bit 4 negates y.</summary>
	public static (int X, int Y) Oriented( int orientation, int along, int across )
	{
		var (x, y) = (orientation & 1) == 0 ? (along, across) : (across, along);
		if ( (orientation & 2) != 0 )
			x = -x;
		if ( (orientation & 4) != 0 )
			y = -y;
		return (x, y);
	}

	/// <summary>The orientation word of a heading as the controller stores it (+0xB0): bit 1 selects the Y axis, bit 2 flips X and bit 4 flips Y.</summary>
	public static int OrientationOf( byte heading ) => heading switch
	{
		TrackHeading.PosX => 0,
		TrackHeading.NegX => 2,
		TrackHeading.PosY => 1,
		TrackHeading.NegY => 5,
		_ => throw new ArgumentOutOfRangeException( nameof( heading ) )
	};
}
