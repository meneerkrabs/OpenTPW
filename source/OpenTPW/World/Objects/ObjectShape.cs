namespace OpenTPW;

/// <summary>Meaning of one character of an <c>Info.Shape</c> grid (docs/OBJECTS.md).</summary>
public enum ObjectCellKind
{
	/// <summary><c>.</c>: inside the bounding box but not occupied.</summary>
	Free,
	/// <summary><c>*</c>: occupied.</summary>
	Occupied,
	/// <summary>
	/// <c>2</c> (and <c>8</c> <c>6</c> <c>4</c> <c>O</c>): the entrance cell. Verified in Easymode: the Belly Bounce queue ends at it, and the Drinks
	/// Shop, Jungle Spray and Staff Room paths connect to it (the only special cell of shops and toilets).
	/// </summary>
	Entrance,
	/// <summary>
	/// <c>S</c>, <c>N</c>, <c>E</c>, <c>W</c>, <c>X</c>: the exit cell. <c>S</c> verified (Belly Bounce exit joins the path);
	/// the others follow the executable's symbol table.
	/// </summary>
	Exit,
	/// <summary><c>&lt;</c>, <c>&gt;</c>, <c>D</c>: track-end cells (type 23); the coaster station ends where the track leaves/returns.</summary>
	TrackConnection,
	/// <summary><c>+</c>: cell type 11, the track-upgrade cells laid over the parent ride's track (use inferred).</summary>
	Overlay
}

/// <summary>An entrance or exit cell of a placed object and the adjacent outside cell (queue end or path).</summary>
/// <summary>An entrance or exit cell of a placed shape: its grid cell, the outside cell in front of it and its place in the shape (<paramref name="U"/> the column, <paramref name="V"/> the row counted from the last text row), which the scan orders of tp.exe read.</summary>
public readonly record struct ObjectAccessPoint( ObjectCellKind Kind, int X, int Y, int OutsideX, int OutsideY, int U = 0, int V = 0 );

/// <summary>One footprint cell: local shape coordinates and its kind.</summary>
public readonly record struct ObjectShapeCell( int U, int V, ObjectCellKind Kind, char Symbol )
{
	/// <summary>The executable's cell type of the symbol (<see cref="ObjectShapeSymbols"/>); 0 for a space.</summary>
	public int CellType => ObjectShapeSymbols.TryGet( Symbol, out var row ) ? row.CellType : 0;

	/// <summary>The compass bit of the symbol (0x01 N, 0x04 E, 0x10 S, 0x40 W); 0 when the symbol has none.</summary>
	public byte Direction => ObjectShapeSymbols.TryGet( Symbol, out var row ) ? row.Direction : (byte)0;
}

/// <summary>
/// The shape-symbol table of <c>Info.Shape</c>: one row per character with the cell type the shape parser stores and a direction bit.
/// </summary>
public static class ObjectShapeSymbols
{
	/// <summary>Cell types of the table (the executable's numbering).</summary>
	public const int TypeFree = 0, TypeAt = 1, TypeQ = 3, TypeOccupied = 4, TypeEntrance = 9, TypeExit = 10, TypeUpgrade = 11, TypeHash = 16, TypeTrackEnd = 23;

	/// <summary>Compass bits.</summary>
	public const byte North = 0x01, East = 0x04, South = 0x10, West = 0x40;

	// [BIN:TPW-EXE:0x007396C8 shape symbol table] 19 rows of 12 bytes (character, cell type, direction byte) passed to the shape parser 0x402720 for Info.Shape; the parser looks every non-space character up in it (0x402840..0x40287B) and fails with "Illegal map character" when it is missing
	private static readonly (char Symbol, int CellType, byte Direction)[] Rows =
	{
		('.', TypeFree, 0), ('*', TypeOccupied, 0), ('Q', TypeQ, 0), ('@', TypeAt, 0), ('+', TypeUpgrade, 0), ('#', TypeHash, 0),
		('8', TypeEntrance, North), ('6', TypeEntrance, East), ('2', TypeEntrance, South), ('4', TypeEntrance, West),
		('N', TypeExit, North), ('E', TypeExit, East), ('S', TypeExit, South), ('W', TypeExit, West),
		('D', TypeTrackEnd, 0), ('>', TypeTrackEnd, East), ('<', TypeTrackEnd, West),
		('O', TypeEntrance, North), ('X', TypeExit, North)
	};

	public static bool TryGet( char symbol, out (int CellType, byte Direction) row )
	{
		foreach ( var candidate in Rows )
		{
			if ( candidate.Symbol == symbol )
			{
				row = (candidate.CellType, candidate.Direction);
				return true;
			}
		}
		row = default;
		return false;
	}
}

/// <summary>
/// An object's <c>Info.Shape</c> grid. Local coordinates: <c>U</c> is the column (model +X), <c>V</c> counts
/// rows from the <b>last</b> text row (model +Z). Evidence: 240 of 248 non-fixed main models span exactly
/// [0, 10·width] × [0, 10·height] in X/Z; seat dummies of the Belly Bounce (15, 0, 5)/(15, 0, 35) and the
/// Staff Room <c>position01</c> (15, 0, 3) sit in the <c>2</c>/<c>S</c> cells only with V counted from
/// the last row; and in Easymode the saved footprints, queue and path connections match that orientation.
/// </summary>
public sealed class ObjectShape
{
	private readonly ObjectShapeCell[] cells;

	public ObjectShape( IReadOnlyList<string> rows )
	{
		ArgumentNullException.ThrowIfNull( rows );
		var trimmed = rows.Select( row => row.TrimEnd() ).ToList();
		while ( trimmed.Count > 0 && trimmed[^1].Length == 0 )
			trimmed.RemoveAt( trimmed.Count - 1 );
		if ( trimmed.Count == 0 || trimmed.Any( row => row.Length == 0 ) )
			throw new InvalidDataException( "An object shape needs at least one non-empty row." );
		Rows = trimmed.AsReadOnly();
		Width = trimmed.Max( row => row.Length );
		Height = trimmed.Count;
		var list = new List<ObjectShapeCell>();
		for ( var row = 0; row < Height; row++ )
		{
			for ( var column = 0; column < trimmed[row].Length; column++ )
			{
				var symbol = trimmed[row][column];
				list.Add( new ObjectShapeCell( column, Height - 1 - row, Classify( symbol ), symbol ) );
			}
		}
		cells = list.ToArray();
	}

	public IReadOnlyList<string> Rows { get; }
	public int Width { get; }
	public int Height { get; }
	public IReadOnlyList<ObjectShapeCell> Cells => cells;
	public IEnumerable<ObjectShapeCell> OccupiedCells => cells.Where( cell => cell.Kind != ObjectCellKind.Free );
	public IEnumerable<ObjectShapeCell> Entrances => cells.Where( cell => cell.Kind == ObjectCellKind.Entrance );
	public IEnumerable<ObjectShapeCell> Exits => cells.Where( cell => cell.Kind == ObjectCellKind.Exit );

	/// <summary>The kind of a symbol from its cell type in <see cref="ObjectShapeSymbols"/>; a space is free.</summary>
	// [BIN:TPW-EXE:0x007396C8 shape symbol kinds] '.' type 0, '*' 4, 'Q' 3, '@' 1, '+' 11, '#' 16; entrances (type 9) 8 6 2 4 O; exits (type 10) N E S W X; track ends (type 23) D > <
	// [APPROX:RIDES-011] The add-on cells retain their native double-layer flags. Rotated track endpoints use the mode4 beyond-cell test; CoasterCellMarkers integrates type21 reservations, single-cell node preview and stack lifetime. Residual: independent host previews omit native sticky failure globals, and defensive owner/generation leases do not reproduce unconditional native foreign-cell commits (docs/COASTER-CELL-MARKERS.md).
	// [BIN:TPW-EXE:0x0052950D '+' and '#' stamp] '+' and '#' are stored as occupied type 4 (0x5290C7–0x5290EE) and on commit both set the flag word of the cell and of its second-layer cell to 0x10 (0x52950D–0x52951C); every shipped add-on is a block of '+' (Dino Karts Jump "++/++", the tunnels, the Teleport) and no shipped shape has a '#'
	// [BIN:TPW-EXE:0x00528A70 '@' and 'Q' stamp] '@' (1) and 'Q' (3) are stamped by the placement as a type-1 and a type-3 cell (0x529119, 0x529173–0x529208); no Theme Park World shape has either (only the Theme Park Inc road signs "*@*" and "*@@*" do)
	public static ObjectCellKind Classify( char symbol )
	{
		if ( symbol == ' ' )
			return ObjectCellKind.Free;
		if ( !ObjectShapeSymbols.TryGet( symbol, out var row ) )
			throw new InvalidDataException( $"Unknown object shape symbol '{symbol}'." );
		return row.CellType switch
		{
			ObjectShapeSymbols.TypeFree => ObjectCellKind.Free,
			ObjectShapeSymbols.TypeEntrance => ObjectCellKind.Entrance,
			ObjectShapeSymbols.TypeExit => ObjectCellKind.Exit,
			ObjectShapeSymbols.TypeTrackEnd => ObjectCellKind.TrackConnection,
			ObjectShapeSymbols.TypeUpgrade => ObjectCellKind.Overlay,
			_ => ObjectCellKind.Occupied
		};
	}

	/// <summary>A 1×1 occupied shape, used for objects without an Info.Shape.</summary>
	public static ObjectShape Single { get; } = new( new[] { "*" } );
}

/// <summary>
/// Placement of a shape on the park grid. The anchor (save record X/Y) is the cell of local (0, 0), and a
/// rotation turns the local grid about it. Verified in Easymode: rotation 0 maps (u, v) → (X+u, Y+v);
/// rotation 90 maps (u, v) → (X+v, Y−u) (Staff Room and Round Fountain footprints, the Staff Room entrance
/// opening towards −X); rotation 270 turns the Small Toilet entrances towards +X, the inverse rotation.
/// Rotation 180 and non-square shapes follow the same rigid rotation in the original placement (tp.exe 0x528A70).
/// </summary>
public static class ObjectFootprint
{
	public static bool IsValidRotation( int degrees ) => degrees is 0 or 90 or 180 or 270;

	/// <summary>Grid cell of local cell (u, v) for an object anchored at (x, y).</summary>
	// [BIN:TPW-EXE:0x00528F62 footprint stamping] the placement stamps shape cell (column u, row v; the parser 0x402720 reverses the rows, so row 0 is the bottom text row) at the anchor plus (u, v), (v, −u), (−u, −v) and (−v, u) for 0, 90, 180 and 270 (0x528F7C, 0x529003, 0x528FD2, 0x528FA2 with the window offsets of 0x528B31–0x528BE4), for any width and height; the anchor is the cell of (0, 0) stored at 0x528DFF–0x528E12, and the demolition 0x527EE0 walks the same mapping
	public static (int X, int Y) ToGrid( int anchorX, int anchorY, int rotation, int u, int v ) => rotation switch
	{
		0 => (anchorX + u, anchorY + v),
		90 => (anchorX + v, anchorY - u),
		180 => (anchorX - u, anchorY - v),
		270 => (anchorX - v, anchorY + u),
		_ => throw new ArgumentOutOfRangeException( nameof( rotation ), "Rotations are multiples of 90 degrees." )
	};

	/// <summary>Continuous local position (in cells, model X/Z ÷ 10) to grid position, consistent with <see cref="ToGrid"/>.</summary>
	public static System.Numerics.Vector2 ToGrid( int anchorX, int anchorY, int rotation, System.Numerics.Vector2 local ) => rotation switch
	{
		0 => new( anchorX + local.X, anchorY + local.Y ),
		90 => new( anchorX + local.Y, anchorY + 1 - local.X ),
		180 => new( anchorX + 1 - local.X, anchorY + 1 - local.Y ),
		270 => new( anchorX + 1 - local.Y, anchorY + local.X ),
		_ => throw new ArgumentOutOfRangeException( nameof( rotation ), "Rotations are multiples of 90 degrees." )
	};

	/// <summary>
	/// A shape symbol's direction byte (0x01, 0x04, 0x10 or 0x40) turned by the build rotation, the byte the placement stores in an entrance or exit cell's queue link:
	/// shifted left by 0, 6, 4 or 2 bits (rotation 0, 90, 180, 270); a result above 0x80 is taken as its overflow, the value shifted right by 8.
	/// </summary>
	// [BIN:TPW-EXE:0x004D8C20 rotate direction byte] the byte is shifted left by log2 of the step byte (1, 0x40, 0x10, 4 for 0, 90, 180, 270); a result above 0x80 is shifted right by 8 (0x4D8C45), so the bit that left the byte comes back at the bottom: 0x10 turned by 90 is 0x04, by 180 0x01, by 270 0x40; Easymode's saved entrance and exit cells carry these bytes (Staff Room at 90: 0x04, Small Toilets at 270: 0x40)
	public static byte RotateDirectionByte( byte direction, int rotation )
	{
		var shift = rotation switch
		{
			0 => 0,
			90 => 6,
			180 => 4,
			270 => 2,
			_ => throw new ArgumentOutOfRangeException( nameof( rotation ), "Rotations are multiples of 90 degrees." )
		};
		var value = direction << shift;
		return (byte)(value > 0x80 ? value >> 8 : value);
	}

	/// <summary>Turns a local direction (du, dv) like <see cref="ToGrid(int,int,int,int,int)"/> turns offsets.</summary>
	public static (int X, int Y) RotateDirection( int rotation, int du, int dv ) => rotation switch
	{
		0 => (du, dv),
		90 => (dv, -du),
		180 => (-du, -dv),
		270 => (-dv, du),
		_ => throw new ArgumentOutOfRangeException( nameof( rotation ) )
	};

	/// <summary>
	/// Local direction from an access cell to the outside, from its symbol: an exit opens towards its compass bit
	/// (N −V, S +V, E +U, W −U), an entrance opposite to it (the guest walks in along the bit), so <c>2</c> opens towards −V.
	/// Cells without a direction bit open towards −V.
	/// </summary>
	// [BIN:TPW-EXE:0x00413410 shape access cells] the first entrance cell (type 9) is copied with its row, column and direction byte to the object record (def+0x494..0x49C, default direction 1) and the first exit cell (type 10) to def+0x4A0..0x4A8 (default 0x10); the direction therefore belongs to the symbol, not to the edge the cell lies on
	// [BIN:TPW-EXE:0x00529323 entrance outside cell] the placement rotates the entrance symbol's direction byte by the build rotation (0x4D8C20: shifted left by log2 of 1, 0x40, 0x10, 4 for 0, 90, 180, 270, wrapping) and takes the outside cell 1 → (x, y+1), 4 → (x−1, y), 0x10 → (x, y−1), 0x40 → (x+1, y) (table 0x529D90)
	// [BIN:TPW-EXE:0x00529239 exit outside cell] the exit's rotated direction gives 1 → (x, y−1), 4 → (x+1, y), 0x10 → (x, y+1), 0x40 → (x−1, y) (table 0x529D3C): with grid +y = local +V this is N −V, E +U, S +V, W −U, the exit opening along its bit and the entrance opposite; every access symbol has a bit ('O' and 'X' are N), so the −V default below has no case
	public static (int U, int V) OutwardDirection( ObjectShape shape, ObjectShapeCell cell )
	{
		var (du, dv) = cell.Direction switch
		{
			ObjectShapeSymbols.North => (0, -1),
			ObjectShapeSymbols.South => (0, 1),
			ObjectShapeSymbols.East => (1, 0),
			ObjectShapeSymbols.West => (-1, 0),
			_ => (0, 0)
		};
		if ( du == 0 && dv == 0 )
			return (0, -1);
		return cell.Kind == ObjectCellKind.Entrance ? (-du, -dv) : (du, dv);
	}

	/// <summary>Entrance and exit cells on the grid with the outside cell a queue or path joins.</summary>
	public static IEnumerable<ObjectAccessPoint> GetAccessPoints( ObjectShape shape, int anchorX, int anchorY, int rotation )
	{
		foreach ( var cell in shape.Cells.Where( cell => cell.Kind is ObjectCellKind.Entrance or ObjectCellKind.Exit ) )
		{
			var (x, y) = ToGrid( anchorX, anchorY, rotation, cell.U, cell.V );
			var (du, dv) = OutwardDirection( shape, cell );
			var (dx, dy) = RotateDirection( rotation, du, dv );
			yield return new ObjectAccessPoint( cell.Kind, x, y, x + dx, y + dy, cell.U, cell.V );
		}
	}

	public static IEnumerable<(int X, int Y, ObjectShapeCell Cell)> GetCells( ObjectShape shape, int anchorX, int anchorY, int rotation )
	{
		foreach ( var cell in shape.OccupiedCells )
		{
			var (x, y) = ToGrid( anchorX, anchorY, rotation, cell.U, cell.V );
			yield return (x, y, cell);
		}
	}

	/// <summary>Inclusive bounding box of the whole shape grid (free cells included).</summary>
	public static (int MinX, int MinY, int MaxX, int MaxY) GetBounds( ObjectShape shape, int anchorX, int anchorY, int rotation )
	{
		var a = ToGrid( anchorX, anchorY, rotation, 0, 0 );
		var b = ToGrid( anchorX, anchorY, rotation, shape.Width - 1, shape.Height - 1 );
		return (Math.Min( a.X, b.X ), Math.Min( a.Y, b.Y ), Math.Max( a.X, b.X ), Math.Max( a.Y, b.Y ));
	}
}
