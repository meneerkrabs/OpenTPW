namespace OpenTPW;

/// <summary>What a click of the area clear tool did.</summary>
public enum AreaClearClick
{
	/// <summary>The tool is off.</summary>
	Ignored,
	/// <summary>The first click stored the rectangle's first corner.</summary>
	CornerStored,
	/// <summary>The second click cleared the rectangle and ended the tool.</summary>
	Cleared
}

/// <summary>
/// The area clear tool (tp.exe tool mode 0x3A, the bulldozer: the buy window's Clear Land row and the Clear key): two clicks span a
/// rectangle; the second removes every object wholly inside it and every path cell in it (<see cref="Clear"/>), then the tool ends.
/// Queue cells and ride cells that are not wholly inside stay.
/// </summary>
// [BIN:TPW-EXE:0x0040C5E0 Clear key] the key builds the tool object 0x46C580(0x3A); the buy window's row −2 builds the same (0x4AC851)
// [BIN:TPW-EXE:0x00525DA3 area clear click] in mode 0x3A the first click (start −1) stores the cell as the corner; the second runs the check 0x424EC0 (it returns 1) and the clear 0x52BDD0 from the corner to the clicked cell, sets the start to −1 and ends the tool (SetMode(0, 0) 0x525E33)
public sealed class AreaClearTool
{
	private readonly Func<int, int, object?> objectAt;
	private readonly Func<int, int, bool> removeObject;
	private readonly Func<ParkCellMap?> cells;
	private readonly Func<int, int, CellBuildResult> removePath;

	/// <param name="objectAt">The object standing on a cell (its identity), or null.</param>
	/// <param name="removeObject">Demolishes the object standing on a cell (the demolition 0x527EE0); false when it cannot.</param>
	/// <param name="cells">The park's cell map.</param>
	/// <param name="removePath">The forced path clear of a cell (<see cref="ParkPathBuilder.Remove"/>).</param>
	public AreaClearTool( Func<int, int, object?> objectAt, Func<int, int, bool> removeObject, Func<ParkCellMap?> cells, Func<int, int, CellBuildResult> removePath )
	{
		this.objectAt = objectAt ?? throw new ArgumentNullException( nameof( objectAt ) );
		this.removeObject = removeObject ?? throw new ArgumentNullException( nameof( removeObject ) );
		this.cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
		this.removePath = removePath ?? throw new ArgumentNullException( nameof( removePath ) );
	}

	public bool IsActive { get; private set; }
	/// <summary>The cell the first click stored, null before it.</summary>
	public (int X, int Y)? Corner { get; private set; }
	/// <summary>The cell under the pointer, null off the park.</summary>
	public (int X, int Y)? HoverCell { get; private set; }
	/// <summary>Objects and path cells the last clear removed.</summary>
	public (int Objects, int Paths) LastCleared { get; private set; }

	/// <summary>Starts the tool (SetMode 0x3A: no corner yet).</summary>
	public void Begin()
	{
		IsActive = true;
		Corner = null;
		HoverCell = null;
	}

	/// <summary>Ends the tool without clearing (right click, Escape, another tool).</summary>
	public void Cancel()
	{
		IsActive = false;
		Corner = null;
		HoverCell = null;
	}

	/// <summary>The pointer is over (<paramref name="x"/>, <paramref name="y"/>), or off the park when null.</summary>
	public void Hover( int? x, int? y ) => HoverCell = IsActive && x is { } cx && y is { } cy ? (cx, cy) : null;

	/// <summary>A click on (<paramref name="x"/>, <paramref name="y"/>): the first stores the corner, the second clears the rectangle and ends the tool.</summary>
	public AreaClearClick Click( int x, int y )
	{
		if ( !IsActive )
			return AreaClearClick.Ignored;
		if ( Corner is not { } corner )
		{
			Corner = (x, y);
			HoverCell = (x, y);
			return AreaClearClick.CornerStored;
		}
		LastCleared = Clear( corner, (x, y) );
		Cancel();
		return AreaClearClick.Cleared;
	}

	/// <summary>
	/// The ghost: the rectangle's edge cells from <see cref="Corner"/> to <see cref="HoverCell"/>, or the hovered cell before the first click.
	/// </summary>
	// [BIN:TPW-EXE:0x005234D0 area clear ghost] with a corner stored the preview lays the four edges of the rectangle with LayLine(0x13A) (the validator answers 0xD for mode 0x3A)
	public IEnumerable<(int X, int Y)> PreviewEdge()
	{
		if ( HoverCell is not { } b )
			yield break;
		if ( Corner is not { } a )
		{
			yield return b;
			yield break;
		}
		var (x0, x1) = (Math.Min( a.X, b.X ), Math.Max( a.X, b.X ));
		var (y0, y1) = (Math.Min( a.Y, b.Y ), Math.Max( a.Y, b.Y ));
		for ( var y = y0; y <= y1; y++ )
			for ( var x = x0; x <= x1; x++ )
				if ( x == x0 || x == x1 || y == y0 || y == y1 )
					yield return (x, y);
	}

	/// <summary>
	/// The clear of the rectangle from <paramref name="a"/> to <paramref name="b"/> (both included): row by row, each object met whose
	/// run of cells along the row and along the column through the cell it is met at lies inside the rectangle, with none of those cells
	/// carrying flag 0x10, is demolished; then every path cell in the rectangle is cleared (forced, a NoModify cell with neighbours stays).
	/// </summary>
	// [BIN:TPW-EXE:0x0052BDD0 area clear] the rectangle is normalised; rows from the top, cells from the left: a ride cell (type 4, 9 or 10) starts four scans along the cells of the same owner (+0x10) and of a ride type, to the right, the left, down and up, each refused when a scanned cell has flag 0x10 (0x535DB0(0x10)) or the run passes the rectangle's edge (0x52BEC6..0x52C06A), and when all four pass the object is demolished (0x527EE0(x, y, 0)); then every type-1 cell of the rectangle (0x536310) is cleared with 0x5367A0(0, 0) (0x52C0D8..0x52C11F), queue cells and ride cells staying
	// [APPROX:PATH-005] OpenTPW's cell map keeps no owner word (+0x10): an object's cells are its footprint cells (ParkObjects) rather than the type-4/9/10 cells of one owner, its owner the object; the writer of the cell flag 0x10 that keeps an object from the clear is not traced, and OpenTPW's map never carries it — evidence needed: the writer of the cell flag word +0x0E bit 0x10
	public (int Objects, int Paths) Clear( (int X, int Y) a, (int X, int Y) b )
	{
		var (x0, x1) = (Math.Min( a.X, b.X ), Math.Max( a.X, b.X ));
		var (y0, y1) = (Math.Min( a.Y, b.Y ), Math.Max( a.Y, b.Y ));
		var map = cells();
		bool Flagged( int x, int y ) => map != null && map.InBounds( x, y ) && ((ushort)map.FlagsAt( x, y ) & 0x10) != 0;
		var objects = 0;
		for ( var y = y0; y <= y1; y++ )
		{
			for ( var x = x0; x <= x1; x++ )
			{
				if ( objectAt( x, y ) is not { } owner )
					continue;
				if ( RunInside( map, x, y, 1, 0, owner, x1, Flagged ) && RunInside( map, x, y, -1, 0, owner, x0, Flagged )
					&& RunInside( map, x, y, 0, 1, owner, y1, Flagged ) && RunInside( map, x, y, 0, -1, owner, y0, Flagged ) && removeObject( x, y ) )
					objects++;
			}
		}
		var paths = 0;
		if ( map != null )
		{
			for ( var y = y0; y <= y1; y++ )
				for ( var x = x0; x <= x1; x++ )
					if ( map.TypeAt( x, y ) == ParkCellType.Path && removePath( x, y ) == CellBuildResult.Ok )
						paths++;
		}
		return (objects, paths);
	}

	/// <summary>
	/// One of the four scans: from (<paramref name="x"/>, <paramref name="y"/>) along (<paramref name="dx"/>, <paramref name="dy"/>) over the cells of
	/// <paramref name="owner"/> and the first cell beyond them; false when a scanned cell is flagged or lies more than one cell past
	/// <paramref name="edge"/> (the rectangle's edge on that side), so the object's run must end inside the rectangle.
	/// </summary>
	private bool RunInside( ParkCellMap? map, int x, int y, int dx, int dy, object owner, int edge, Func<int, int, bool> flagged )
	{
		var inside = true;
		var (cx, cy) = (x, y);
		while ( true )
		{
			if ( flagged( cx, cy ) )
				inside = false;
			var along = dx != 0 ? cx : cy;
			if ( dx + dy > 0 ? along > edge + 1 : along < edge - 1 )
				inside = false;
			var owned = ReferenceEquals( objectAt( cx, cy ), owner );
			(cx, cy) = (cx + dx, cy + dy);
			// The scan goes on past a cell of the object while the next cell is on the map (0x4D8300).
			if ( !owned || map == null || !map.InBounds( cx, cy ) )
				break;
		}
		return inside;
	}
}
