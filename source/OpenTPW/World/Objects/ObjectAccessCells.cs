namespace OpenTPW;

/// <summary>
/// The cells outside a placed object's entrance and exit, which tp.exe's placement <c>0x528A70</c> lays itself (RIDES-028): for a type with
/// <c>Info.HasQueue</c> (the option 0x81B0CC, which the build tool sets from the type's +0x40, 0x52FA90) the entrance's outside cell becomes a queue cell linked to the
/// entrance and the exit's outside cell a path cell; for any other type the entrance's outside cell becomes a path cell and the exit gets none. All of them carry the
/// NoModify flag, and the placement lays them free of charge (the commit runs with the free flag 0x8186D4 set, so the cell setter 0x5346D0 charges nothing).
/// </summary>
public static class ObjectAccessCells
{
	/// <summary>The cells the build check has to validate beyond the footprint: <c>Object</c> cells by the object rule (type 4, 0x204), <c>Path</c> cells by the path rule (type 1, 0x201).</summary>
	// [BIN:TPW-EXE:0x0052977F access cell validation] with the queue option set the entrance's outside cell is validated as an object cell (op 0x204, 0x5297A9: empty ground, a path cell without NoModify and an object of lower overwrite priority pass) and the exit's outside cell as a path cell (op 0x201, 0x529A2F: empty ground and any path cell pass); without it the entrance's outside cell is validated as a path cell (0x5298B9) and the exit has none; a cell off the map refuses the placement (0x529744, 0x5299E2)
	public static (IReadOnlyList<(int X, int Y)> Object, IReadOnlyList<(int X, int Y)> Path) CellsToCheck( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation )
	{
		var objectRule = new List<(int, int)>();
		var pathRule = new List<(int, int)>();
		var (entrance, exit) = Find( ObjectFootprint.GetAccessPoints( entry.Shape, anchorX, anchorY, rotation ) );
		if ( entrance is { } enter )
			( entry.HasQueue ? objectRule : pathRule ).Add( (enter.OutsideX, enter.OutsideY) );
		if ( exit is { } leave && entry.HasQueue )
			pathRule.Add( (leave.OutsideX, leave.OutsideY) );
		return (objectRule, pathRule);
	}

	/// <summary>The last entrance and the last exit of a shape's access points: the placement's loop over the footprint keeps the last it meets.</summary>
	private static (ObjectAccessPoint? Entrance, ObjectAccessPoint? Exit) Find( IEnumerable<ObjectAccessPoint> points )
	{
		ObjectAccessPoint? entrance = null, exit = null;
		foreach ( var point in points )
		{
			if ( point.Kind == ObjectCellKind.Entrance )
				entrance = point;
			else if ( point.Kind == ObjectCellKind.Exit )
				exit = point;
		}
		return (entrance, exit);
	}

	/// <summary>
	/// Lays the entrance's and exit's outside cells of an object just placed (its footprint already written), as the commit does after its footprint loop.
	/// </summary>
	/// <param name="owner">The object's owner word, which the entrance's queue cell takes from the entrance cell (0x539210).</param>
	/// <param name="effects">Tile refreshes the link writer asks for.</param>
	// [BIN:TPW-EXE:0x005297BD entrance queue cell] on commit the entrance cell's link byte takes the bit toward the outside cell (0x522700) and its queue link +0xD the same bit (0x5227E0); the outside cell is cleared by cell op 0x87 and typed 3, takes the opposite bit in its link byte and its queue link (0x522700, 0x5227E0), the entrance cell's owner word (0x539210) and the flag word 0x20 (0x53A510), then the refresh ops 0x81, 0x85 and 0x86 run and the ride re-evaluates its queue (0x4DE1F0); Easymode's Belly Bounce has exactly this: entrance links 0x01 and queue link 0x01, queue cell with flags 0x20, queue link 0x10 and the ride's owner word
	// [BIN:TPW-EXE:0x00529A58 exit path cell] on commit the exit's outside cell is cleared (0x87) unless it is a path cell, typed 1 (the cell setter counts a path over a path as one more placement), linked by the path link writer in mode 1 (0x5348D0), refreshed (0x81, 0x85, 0x86), set to the flag word 0x20 and every path neighbour it links to is linked again by the same writer (0x529ABF..0x529B18)
	// [BIN:TPW-EXE:0x0052989A entrance path cell] without the queue option the entrance's outside cell gets the same treatment as the exit's (0x5298D5..0x5299AC); Easymode's Drinks Shop, Jungle Spray, Litter Bin, Staff Room and Small Toilets have a NoModify path cell outside their entrance
	public static void Lay( ParkCellMap cells, IEnumerable<ObjectAccessPoint> points, ushort owner, bool hasQueue, LinkEffects? effects = null )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var (entrance, exit) = Find( points );
		if ( entrance is { } enter )
		{
			var outside = (X: enter.OutsideX, Y: enter.OutsideY);
			if ( hasQueue && cells.InBounds( outside.X, outside.Y ) && cells.InBounds( enter.X, enter.Y ) )
			{
				var toward = LinkBit( outside.X - enter.X, outside.Y - enter.Y );
				cells.SetLinks( enter.X, enter.Y, (byte)(cells.LinksAt( enter.X, enter.Y ) | toward) );
				cells.SetQueueLink( enter.X, enter.Y, toward );
				LayQueue( cells, outside.X, outside.Y, PathLinkWriter.Opposite( toward ), owner );
				effects?.Refresh?.Invoke( outside.X, outside.Y );
			}
			else
				LayPath( cells, outside.X, outside.Y, effects );
		}
		if ( exit is { } leave )
		{
			if ( hasQueue )
				LayPath( cells, leave.OutsideX, leave.OutsideY, effects );
			// [BIN:TPW-EXE:0x005292A7 exit cell link] the footprint loop links a path cell already in front of an exit (0x4D97E0 along its queue link, then 0x5348D0 in mode 1: 0x5292BE..0x529208's twin at 0x5293D8..0x5293FE), which is all an exit gets when the queue option is off
			else if ( cells.InBounds( leave.OutsideX, leave.OutsideY ) && cells.RawTypeAt( leave.OutsideX, leave.OutsideY ) == (byte)ParkCellType.Path )
			{
				PathLinkWriter.LinkPath( cells, leave.OutsideX, leave.OutsideY, effects );
				effects?.Refresh?.Invoke( leave.OutsideX, leave.OutsideY );
			}
		}
	}

	/// <summary>
	/// Clears the queue of a ride that has been demolished: every queue cell carrying its owner word, the cell laid outside the entrance and the cells the queue tool laid after it,
	/// each unlinked from its neighbours. The path stubs laid outside an exit or a shop's entrance stay, as paths do.
	/// </summary>
	// [BIN:STP-PPC:0x100748A0 ride demolition] the demolition replays the queue tool from the ride's entrance to clear its queue (the PC twin 0x527EE0 runs the replays with 0x81B46C set); a queue cell laid by the placement is NoModify, which a rebuild at the same site could not stand on, so the clear takes it as well — the cell writes of the original's demolition are otherwise not traced (ECON-050)
	public static void ClearQueue( ParkCellMap cells, ushort owner )
	{
		ArgumentNullException.ThrowIfNull( cells );
		if ( owner == 0 )
			return;
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				if ( cells.RawTypeAt( x, y ) != (byte)ParkCellType.Queue || cells.OwnerAt( x, y ) != owner )
					continue;
				PathLinkWriter.Unlink( cells, x, y, cardinalOnly: true );
				cells.SetFlags( x, y, cells.FlagsAt( x, y ) & ~ParkCellFlags.NoModify );
				cells.SetType( x, y, ParkCellType.Empty );
			}
		}
	}

	/// <summary>The link bit of the cell one step away: 1 (−Y), 4 (+X), 16 (+Y), 64 (−X).</summary>
	private static byte LinkBit( int dx, int dy ) => (dx, dy) switch
	{
		(0, -1) => 0x01,
		(1, 0) => 0x04,
		(0, 1) => 0x10,
		(-1, 0) => 0x40,
		_ => throw new ArgumentOutOfRangeException( nameof( dx ), "An access cell lies one cell from its entrance or exit." )
	};

	private static void LayQueue( ParkCellMap cells, int x, int y, byte towardEntrance, ushort owner )
	{
		if ( !cells.InBounds( x, y ) )
			return;
		var type = cells.RawTypeAt( x, y );
		if ( type == (byte)ParkCellType.Queue )
			cells.SetPlacementCount( x, y, (short)Math.Min( short.MaxValue, cells.PlacementCountAt( x, y ) + 1 ) );
		else
		{
			// Setting a queue type over a path clears the path first, every link of it undone on both sides (0x5346D0 → 0x5367A0(0, 0)).
			if ( type == (byte)ParkCellType.Path )
				PathLinkWriter.Unlink( cells, x, y, cardinalOnly: false );
			cells.SetType( x, y, ParkCellType.Queue );
		}
		cells.SetLinks( x, y, (byte)(cells.LinksAt( x, y ) | towardEntrance ) );
		cells.SetQueueLink( x, y, towardEntrance );
		if ( owner != 0 )
			cells.SetOwner( x, y, owner );
		cells.SetFlags( x, y, ParkCellFlags.NoModify );
	}

	private static void LayPath( ParkCellMap cells, int x, int y, LinkEffects? effects )
	{
		if ( !cells.InBounds( x, y ) )
			return;
		if ( cells.RawTypeAt( x, y ) == (byte)ParkCellType.Path )
			cells.SetPlacementCount( x, y, (short)Math.Min( short.MaxValue, cells.PlacementCountAt( x, y ) + 1 ) );
		else
			cells.SetType( x, y, ParkCellType.Path );
		PathLinkWriter.LinkPath( cells, x, y, effects );
		cells.SetFlags( x, y, ParkCellFlags.NoModify );
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (nx, ny) = (x + GuestPathGrid.Directions[direction].DX, y + GuestPathGrid.Directions[direction].DY);
			if ( (cells.LinksAt( x, y ) & GuestPathGrid.LinkValue( direction )) != 0 && cells.InBounds( nx, ny ) && cells.RawTypeAt( nx, ny ) == (byte)ParkCellType.Path )
				PathLinkWriter.LinkPath( cells, nx, ny, effects );
		}
		effects?.Refresh?.Invoke( x, y );
	}
}
