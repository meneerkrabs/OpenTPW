namespace OpenTPW;

/// <summary>Meaning of one character of an <c>Info.Shape</c> grid (docs/OBJECTS.md).</summary>
public enum ObjectCellKind
{
	/// <summary><c>.</c>: inside the bounding box but not occupied.</summary>
	Free,
	/// <summary><c>*</c>: occupied.</summary>
	Occupied,
	/// <summary>
	/// <c>2</c>: the entrance cell. Verified in Easymode: the Belly Bounce queue ends at it, and the Drinks
	/// Shop, Jungle Spray and Staff Room paths connect to it (the only special cell of shops and toilets).
	/// </summary>
	Entrance,
	/// <summary>
	/// <c>S</c>, <c>N</c>, <c>E</c>: the exit cell. <c>S</c> verified (Belly Bounce exit joins the path);
	/// <c>N</c> (next to <c>2</c> on track rides) and <c>E</c> (two dark rides) are inferred by analogy.
	/// </summary>
	Exit,
	/// <summary><c>&lt;</c>, <c>&gt;</c>: coaster station ends where the track leaves/returns (inferred).</summary>
	TrackConnection,
	/// <summary><c>+</c>: track-upgrade cells laid over the parent ride's track (inferred).</summary>
	Overlay
}

/// <summary>One footprint cell: local shape coordinates and its kind.</summary>
public readonly record struct ObjectShapeCell( int U, int V, ObjectCellKind Kind, char Symbol );

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

	public static ObjectCellKind Classify( char symbol ) => symbol switch
	{
		'.' or ' ' => ObjectCellKind.Free,
		'*' => ObjectCellKind.Occupied,
		'2' => ObjectCellKind.Entrance,
		'S' or 'N' or 'E' => ObjectCellKind.Exit,
		'<' or '>' => ObjectCellKind.TrackConnection,
		'+' => ObjectCellKind.Overlay,
		_ => throw new InvalidDataException( $"Unknown object shape symbol '{symbol}'." )
	};

	/// <summary>A 1×1 occupied shape, used for objects without an Info.Shape.</summary>
	public static ObjectShape Single { get; } = new( new[] { "*" } );
}

/// <summary>
/// Placement of a shape on the park grid. The anchor (save record X/Y) is the cell of local (0, 0), and a
/// rotation turns the local grid about it. Verified in Easymode: rotation 0 maps (u, v) → (X+u, Y+v);
/// rotation 90 maps (u, v) → (X+v, Y−u) (Staff Room and Round Fountain footprints, the Staff Room entrance
/// opening towards −X); rotation 270 turns the Small Toilet entrances towards +X, the inverse rotation.
/// Rotation 180 and non-square shapes follow from the same rigid rotation but are not observed in a save.
/// </summary>
public static class ObjectFootprint
{
	public static bool IsValidRotation( int degrees ) => degrees is 0 or 90 or 180 or 270;

	/// <summary>Grid cell of local cell (u, v) for an object anchored at (x, y).</summary>
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

	/// <summary>Grid direction an entrance at rotation 0 opens to (−Y), turned by <paramref name="rotation"/>.</summary>
	public static (int X, int Y) FrontDirection( int rotation ) => rotation switch
	{
		0 => (0, -1),
		90 => (-1, 0),
		180 => (0, 1),
		270 => (1, 0),
		_ => throw new ArgumentOutOfRangeException( nameof( rotation ) )
	};

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

	/// <summary>Anchor that puts the shape's bounding box at (minX, minY) for a rotation (used when building).</summary>
	public static (int X, int Y) AnchorForBounds( ObjectShape shape, int minX, int minY, int rotation )
	{
		var (boxX, boxY, _, _) = GetBounds( shape, 0, 0, rotation );
		return (minX - boxX, minY - boxY);
	}
}
