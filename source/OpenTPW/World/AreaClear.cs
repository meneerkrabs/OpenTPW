namespace OpenTPW;

/// <summary>
/// The area clear's rectangle (tp.exe <c>0x52BDD0</c>): the tool's second click runs it inside one <see cref="ClearAreaCommand"/>, so a rectangle is one edit on every peer.
/// [EXT:online-coop-commands] The scans live here, outside the tool layer: the command hands them the level's own removals, and the tool keeps its clicks (<see cref="AreaClearTool"/>).
/// </summary>
public static class AreaClear
{
	/// <summary>
	/// The clear of the rectangle from <paramref name="a"/> to <paramref name="b"/> (both included): row by row, each object cell met (type 4, 9 or 10) starts four scans along
	/// the cells of the same owner word and of an object type; the object is demolished when none of the cells scanned carries flag 0x10 (<see cref="ParkCellFlags.AddOn"/>) and
	/// every scan ends no more than one cell past the rectangle; then every path cell in the rectangle is cleared (forced, a NoModify cell with neighbours stays).
	/// </summary>
	/// <param name="removeObject">Demolishes the object standing on a cell (the demolition 0x527EE0); false when it cannot, or may not.</param>
	/// <param name="removePath">The forced path clear of a cell (<see cref="ParkPathBuilder.Remove"/>).</param>
	/// <param name="objectAt">
	/// The object standing on a cell, only for a park whose objects do not type their cells (a Theme Park Inc layout, whose placement is not traced): the scans then follow the object
	/// instead of the cells' owner words. Null: the cell map's types and owner words decide, as in tp.exe.
	/// </param>
	// [BIN:TPW-EXE:0x0052BDD0 area clear] the rectangle is normalised; rows from the top, cells from the left: a ride cell (type 4, 9 or 10) starts four scans along the cells of the same owner (+0x10) and of a ride type, to the right, the left, down and up, each refused when a scanned cell has flag 0x10 (0x535DB0(0x10)) or the run passes the rectangle's edge (0x52BEC6..0x52C06A), and when all four pass the object is demolished (0x527EE0(x, y, 0)); then every type-1 cell of the rectangle (0x536310) is cleared with 0x5367A0(0, 0) (0x52C0D8..0x52C11F), queue cells and ride cells staying
	// [BIN:TPW-EXE:0x00528F26 owner word and add-on flag] the owner word is the cell word of the object's anchor, written on every typed footprint cell by the placement (0x539210); the flag word 0x10 is written as a constant only by the placement's '+' and '#' footprint cells (0x53A510(0x10), 0x52950D, 0x52951C: the other callers of 0x53A510 pass the bridge bits 2, 4 and 8, the unowned bit 0x40, 0x20 or a computed word), and the validator reads it to refuse a second add-on on a tile (0x535961); Easymode's saved cells carry the owner words and never the flag
	public static (int Objects, int Paths) Run( ParkCellMap? map, (int X, int Y) a, (int X, int Y) b, Func<int, int, bool> removeObject, Func<int, int, CellBuildResult> removePath,
		Func<int, int, object?>? objectAt = null )
	{
		ArgumentNullException.ThrowIfNull( removeObject );
		ArgumentNullException.ThrowIfNull( removePath );
		if ( map == null )
			return (0, 0);
		var (x0, x1) = (Math.Min( a.X, b.X ), Math.Max( a.X, b.X ));
		var (y0, y1) = (Math.Min( a.Y, b.Y ), Math.Max( a.Y, b.Y ));
		// [EXT:online-coop-commands] the loops visit the map's cells only (any cell off the map is skipped, as before), so a rectangle of a request costs no more than the map; the
		// scans keep the rectangle's own edges
		var (fromX, toX) = (Math.Max( x0, 0 ), Math.Min( x1, map.Width - 1 ));
		var (fromY, toY) = (Math.Max( y0, 0 ), Math.Min( y1, map.Height - 1 ));
		var objects = 0;
		for ( var y = fromY; y <= toY; y++ )
		{
			for ( var x = fromX; x <= toX; x++ )
			{
				if ( OwnerOf( map, x, y, objectAt ) is not { } owner )
					continue;
				if ( RunInside( map, x, y, 1, 0, owner, x1, objectAt ) && RunInside( map, x, y, -1, 0, owner, x0, objectAt )
					&& RunInside( map, x, y, 0, 1, owner, y1, objectAt ) && RunInside( map, x, y, 0, -1, owner, y0, objectAt ) && removeObject( x, y ) )
					objects++;
			}
		}
		var paths = 0;
		for ( var y = fromY; y <= toY; y++ )
			for ( var x = fromX; x <= toX; x++ )
				if ( map.TypeAt( x, y ) == ParkCellType.Path && removePath( x, y ) == CellBuildResult.Ok )
					paths++;
		return (objects, paths);
	}

	/// <summary>
	/// The owner of the object cell (x, y): its owner word when the cell is of an object type (4, 9 or 10), else null; for a park whose objects do not type their cells, the object
	/// standing on it.
	/// </summary>
	private static object? OwnerOf( ParkCellMap map, int x, int y, Func<int, int, object?>? objectAt )
	{
		if ( objectAt != null )
			return objectAt( x, y );
		return map.InBounds( x, y ) && ParkCellTypes.IsObjectCell( map.RawTypeAt( x, y ) ) ? map.OwnerAt( x, y ) : null;
	}

	/// <summary>
	/// One of the four scans: from (<paramref name="x"/>, <paramref name="y"/>) along (<paramref name="dx"/>, <paramref name="dy"/>) over the cells of an object type with the
	/// owner word <paramref name="owner"/> and the first cell beyond them; false when a scanned cell carries flag 0x10 or lies more than one cell past
	/// <paramref name="edge"/> (the rectangle's edge on that side), so the object's run must end inside the rectangle.
	/// </summary>
	private static bool RunInside( ParkCellMap map, int x, int y, int dx, int dy, object owner, int edge, Func<int, int, object?>? objectAt )
	{
		var inside = true;
		var (cx, cy) = (x, y);
		while ( true )
		{
			if ( ((ushort)map.FlagsAt( cx, cy ) & (ushort)ParkCellFlags.AddOn) != 0 )
				inside = false;
			var along = dx != 0 ? cx : cy;
			if ( dx + dy > 0 ? along > edge + 1 : along < edge - 1 )
				inside = false;
			var owned = Equals( OwnerOf( map, cx, cy, objectAt ), owner );
			(cx, cy) = (cx + dx, cy + dy);
			// The scan goes on past a cell of the object while the next cell is on the map (0x4D8300).
			if ( !owned || !map.InBounds( cx, cy ) )
				break;
		}
		return inside;
	}
}
