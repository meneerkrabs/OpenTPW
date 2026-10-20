namespace OpenTPW;

/// <summary>
/// The tour-ride module state of one park (the original's module globals of the flying-car module: the 100-slot controller table, the sine table and the
/// four flight bounds). Owned by the park's <see cref="RideScriptWorld"/>, so every ride runtime reaches it; it holds nothing for a park without a tour ride.
/// </summary>
// [BIN:TPW-EXE:0x0055A5B0 module init] the table of 99 controller pointers at 0x8791F8 (slot 0 unused), a 4096-entry sine table at 0x8791C8 and the four bounds at 0x7660B8..0x7660C4
public sealed class TourWorld
{
	/// <summary>Controllers the module holds (slots 1..99 of the original's table).</summary>
	public const int MaximumControllers = 99;
	/// <summary>The flight area, in module units: x from <see cref="MinimumX"/> up to (excluding) <see cref="MaximumX"/>, z likewise.</summary>
	// [BIN:TPW-EXE:0x007660B8 flight bounds] the four words are 190000, 70000, 140000 and 30000 (x maximum, x minimum, z maximum, z minimum); no code writes them, the save stores them verbatim
	public const int MaximumX = 190000;
	public const int MinimumX = 70000;
	public const int MaximumZ = 140000;
	public const int MinimumZ = 30000;
	/// <summary>One map cell in module units.</summary>
	public const int CellUnits = 3000;

	private readonly List<TourController> controllers = new();
	private static readonly int[] SineTable = BuildSineTable();

	public IReadOnlyList<TourController> Controllers => controllers;

	/// <summary>Map width and height in cells (the original's 0x7A0314 and 0x7A0374): ground lookups clamp to them.</summary>
	public int MapWidth { get; set; } = 128;
	public int MapHeight { get; set; } = 128;

	/// <summary>
	/// The terrain's height at a cell corner in model units, or null for a flat world at 0. The original asks the cell lookup 0x00452AE0, which answers with the height of the
	/// object on the cell when it has one (<see cref="ObjectHeight"/>) and the terrain otherwise.
	/// </summary>
	// [BIN:TPW-EXE:0x00452AE0 cell ground] no owner object: the terrain at (10 × cell) through 0x004527F0
	public Func<int, int, float>? CellHeight { get; set; }

	/// <summary>The ground an object on a cell gives in model units (its height-map byte × 100/255 plus its base height, <see cref="ObjectHeightMaps"/>), or null for a cell no object owns.</summary>
	// [BIN:TPW-EXE:0x00452AE0 cell ground] the cell's owner object (0x0053BF30) with a height map answers before the terrain does; the tour code truncates the result and multiplies by 300 (0x0055C350)
	public Func<int, int, float?>? ObjectHeight { get; set; }

	/// <summary>The sine table value for an angle in 4096ths of a turn (any integer; the original masks with 0xFFF): 0.5 + 1024 × sin, truncated.</summary>
	// [BIN:TPW-EXE:0x0055A5B0 sine table] entry k is the truncated 0.5 − (−1024 × sin(k × 2π / 4096)), i.e. 1024 × sin + 0.5 towards zero
	public static int Sin( int angle ) => SineTable[angle & 0xFFF];

	/// <summary>The cosine: the table read a quarter turn on.</summary>
	public static int Cos( int angle ) => SineTable[(angle + 0x400) & 0xFFF];

	private static int[] BuildSineTable()
	{
		var table = new int[4096];
		for ( var index = 0; index < table.Length; index++ )
			table[index] = (int)(0.5 - DetMath.Sin( index * 2 * 0.00076698974609375 ) * -1024.0);
		return table;
	}

	internal void Add( TourController controller )
	{
		if ( !controllers.Contains( controller ) )
			controllers.Add( controller );
	}

	internal void Remove( TourController controller ) => controllers.Remove( controller );

	/// <summary>Controller part of <see cref="WorldStateHash"/>; called only while a controller exists, so every park without a tour ride keeps its pins.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( controllers.Count );
		foreach ( var controller in controllers )
			controller.AddCanonicalState( hash );
	}

	/// <summary>
	/// The ground height at a module-unit point, as the original's cell lookups give it: inside the bounds the cell's height truncated to whole model units × 300,
	/// outside them 0 (the cell indices clamp to the map).
	/// </summary>
	// [BIN:TPW-EXE:0x0055CC00 ground probe] inside the bounds: cell = x / 3000, z / 3000, clamped to 0..map−1, height = ftol(0x00452AE0(cell)) × 300
	internal int GroundClamped( int x, int z )
	{
		if ( x < MaximumX && z < MaximumZ && MinimumX <= x && MinimumZ <= z )
			return CellGround( x / CellUnits, z / CellUnits );
		return 0;
	}

	internal int CellGround( int cellX, int cellZ )
	{
		cellX = Math.Clamp( cellX, 0, MapWidth - 1 );
		cellZ = Math.Clamp( cellZ, 0, MapHeight - 1 );
		var height = ObjectHeight?.Invoke( cellX, cellZ ) ?? CellHeight?.Invoke( cellX, cellZ ) ?? 0f;
		return (int)height * 300;
	}

	/// <summary>
	/// The height a car must clear at a point (the original's 0x0055CC00): 100000 beyond the bounds plus a margin of 20000, 30000 within about 10000 units of
	/// any controller's station, else the ground.
	/// </summary>
	// [BIN:TPW-EXE:0x0055CC00 clearance] 100000 outside the margin; 30000 when the horizontal distance (taken in 32nds, truncated, × 32) to any controller's station is under 10000; else the ground
	internal int Clearance( int x, int z )
	{
		if ( x >= MaximumX + 20000 || z >= MaximumZ + 20000 || x < MinimumX - 20000 || z < MinimumZ - 20000 )
			return 100000;
		foreach ( var controller in controllers )
		{
			if ( TourMath.Distance32( x - controller.StationX, z - controller.StationZ ) * 32 < 10000 )
				return 30000;
		}
		return GroundClamped( x, z );
	}
}

/// <summary>The integer helpers of the flying-car module: the compiler's truncating divisions and the distance in 32nds.</summary>
internal static class TourMath
{
	/// <summary>The absolute value as the original writes it, (v ^ s) − s.</summary>
	public static int Abs( int value ) => unchecked((value ^ (value >> 31)) - (value >> 31));

	/// <summary>Division by 1024 truncating towards zero ((v + (v &gt;&gt; 31 &amp; 0x3FF)) &gt;&gt; 10).</summary>
	public static int Div1024( int value ) => unchecked(value + ((value >> 31) & 0x3FF)) >> 10;

	/// <summary>Division by 32 truncating towards zero.</summary>
	public static int Div32( int value ) => unchecked(value + ((value >> 31) & 0x1F)) >> 5;

	/// <summary>Division by 8 truncating towards zero.</summary>
	public static int Div8( int value ) => unchecked(value + ((value >> 31) & 7)) >> 3;

	/// <summary>Division by 16 truncating towards zero.</summary>
	public static int Div16( int value ) => unchecked(value + ((value >> 31) & 0xF)) >> 4;

	/// <summary>Division by 4 truncating towards zero.</summary>
	public static int Div4( int value ) => unchecked(value + ((value >> 31) & 3)) >> 2;

	/// <summary>Division by 2 truncating towards zero.</summary>
	public static int Div2( int value ) => unchecked(value + ((value >> 31) & 1)) >> 1;

	/// <summary>The horizontal distance of the original's tests: sqrt((dx / 32)² + (dz / 32)²), truncated (the caller multiplies by 32).</summary>
	public static int Distance32( int dx, int dz )
	{
		long a = Div32( dx );
		long b = Div32( dz );
		return (int)Math.Sqrt( a * a + b * b );
	}

	/// <summary>The three-axis distance of the collision test, in 32nds likewise.</summary>
	public static int Distance32( int dx, int dy, int dz )
	{
		long a = Div32( dx );
		long b = Div32( dy );
		long c = Div32( dz );
		return (int)Math.Sqrt( a * a + b * b + c * c );
	}

	/// <summary>Floors a coordinate to the 20000-unit lattice as the original does: towards zero, one lattice step lower for negatives.</summary>
	public static int Lattice( int value ) => value < 0 ? value / 20000 * 20000 - 20000 : value / 20000 * 20000;

	/// <summary>The sign as the original's expression gives it for a non-zero value: +1 for positive, −1 otherwise.</summary>
	public static int Sign( int value ) => value > 0 ? 1 : -1;
}
