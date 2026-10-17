namespace OpenTPW;

/// <summary>
/// The cells outside a placed object's entrance and exit, which tp.exe's placement <c>0x528A70</c> lays itself (RIDES-028): for a type with
/// <c>Info.HasQueue</c> (the option 0x81B0CC, which the build tool sets from the type's +0x40, 0x52FA90) the entrance's outside cell becomes a queue cell linked to the
/// entrance and the exit's outside cell a path cell; for any other type the entrance's outside cell becomes a path cell and the exit gets none. All of them carry the
/// NoModify flag, and the placement lays them free of charge (the commit runs with the free flag 0x8186D4 set, so the cell setter 0x5346D0 charges nothing).
/// </summary>
public static class ObjectAccessCells
{
	/// <summary>Whether (x, y) is the cell outside one of the entrances or exits among <paramref name="points"/>: a cell the placement lays (<see cref="Lay"/>).</summary>
	public static bool IsOutsideCell( IEnumerable<ObjectAccessPoint> points, int x, int y ) => points.Any( point => (point.OutsideX, point.OutsideY) == (x, y) );

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
				var toward = LinkEntrance( cells, enter );
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
	/// For a harness that registers a ride without a placement (the M3 gate, test rigs): lays the cell a called guest's search walks into (state 13) the way a placement lays an
	/// entrance, type 9 beside the ride's queue cell or its path cell with the link and queue link <see cref="Lay"/> writes. A cell that holds a type already is left as it is.
	/// </summary>
	public static void LayStandCell( ParkCellMap cells, int x, int y )
	{
		ArgumentNullException.ThrowIfNull( cells );
		if ( !cells.InBounds( x, y ) || cells.RawTypeAt( x, y ) != 0 )
			return;
		int? queue = null, path = null;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (nx, ny) = (x + GuestPathGrid.Directions[direction].DX, y + GuestPathGrid.Directions[direction].DY);
			if ( !cells.InBounds( nx, ny ) )
				continue;
			if ( cells.RawTypeAt( nx, ny ) == (byte)ParkCellType.Queue )
				queue ??= direction;
			else if ( cells.RawTypeAt( nx, ny ) == (byte)ParkCellType.Path )
				path ??= direction;
		}
		if ( queue is null && path is null )
			return;
		cells.SetType( x, y, ParkCellTypes.Entrance );
		if ( queue is { } toQueue )
		{
			cells.SetLinks( x, y, GuestPathGrid.LinkValue( toQueue ) );
			cells.SetQueueLink( x, y, GuestPathGrid.LinkValue( toQueue ) );
		}
		else if ( path is { } toPath )
		{
			cells.SetQueueLink( x, y, PathLinkWriter.Opposite( GuestPathGrid.LinkValue( toPath ) ) );
			PathLinkWriter.LinkPath( cells, x + GuestPathGrid.Directions[toPath].DX, y + GuestPathGrid.Directions[toPath].DY );
		}
	}

	/// <summary>The entrance cell takes the link bit toward its outside cell and the same queue link (0x522700, 0x5227E0); returns that bit.</summary>
	private static byte LinkEntrance( ParkCellMap cells, ObjectAccessPoint enter )
	{
		var toward = LinkBit( enter.OutsideX - enter.X, enter.OutsideY - enter.Y );
		cells.SetLinks( enter.X, enter.Y, (byte)(cells.LinksAt( enter.X, enter.Y ) | toward) );
		cells.SetQueueLink( enter.X, enter.Y, toward );
		return toward;
	}

	/// <summary>
	/// Gives an object's entrance and exit cell the link byte they lack, through the writers the placement uses (<see cref="Lay"/>): a cell that already holds any link bit is left alone,
	/// and so is one whose outside cell is no longer the one the placement laid (a queue cell linked back to the entrance, a path cell). Returns the number of cells repaired.
	/// </summary>
	// [EXT:legacy-save-access-links] a park resumed from an OpenTPW save made before an imported park's entrance and exit cells took their saved link byte holds none on them, so the edge test 0x4D8750 refuses the called guest's last walk into the stand point; tp.exe's saves always hold the byte (0x4D0B30 reads each cell record whole), so the original needs no repair, and OpenTPW lays the byte at load the way the placement would (0x522700/0x5227E0 along the queue link, the path writer 0x5348D0 in mode 1 from the outside path cell: 0x5297BD, 0x52989A, 0x529A58), user-approved 2026-10-17
	public static int RepairMissingLinks( ParkCellMap cells, IEnumerable<ObjectAccessPoint> points, bool hasQueue, LinkEffects? effects = null )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var (entrance, exit) = Find( points );
		var repaired = 0;
		if ( entrance is { } enter && IsLinkless( cells, enter.X, enter.Y, ParkCellTypes.Entrance ) && cells.InBounds( enter.OutsideX, enter.OutsideY ) )
		{
			var type = cells.RawTypeAt( enter.OutsideX, enter.OutsideY );
			var toward = LinkBit( enter.OutsideX - enter.X, enter.OutsideY - enter.Y );
			if ( hasQueue && type == (byte)ParkCellType.Queue && (cells.LinksAt( enter.OutsideX, enter.OutsideY ) & PathLinkWriter.Opposite( toward )) != 0 )
			{
				LinkEntrance( cells, enter );
				effects?.Refresh?.Invoke( enter.OutsideX, enter.OutsideY );
				repaired++;
			}
			else if ( !hasQueue && type == (byte)ParkCellType.Path && RelinkOutside( cells, enter.X, enter.Y, enter.OutsideX, enter.OutsideY, effects ) )
				repaired++;
		}
		if ( exit is { } leave && IsLinkless( cells, leave.X, leave.Y, ParkCellTypes.Exit ) && cells.InBounds( leave.OutsideX, leave.OutsideY )
			&& cells.RawTypeAt( leave.OutsideX, leave.OutsideY ) == (byte)ParkCellType.Path && RelinkOutside( cells, leave.X, leave.Y, leave.OutsideX, leave.OutsideY, effects ) )
			repaired++;
		return repaired;
	}

	private static bool IsLinkless( ParkCellMap cells, int x, int y, byte type ) => cells.InBounds( x, y ) && cells.RawTypeAt( x, y ) == type && cells.LinksAt( x, y ) == 0;

	/// <summary>The path writer from the outside path cell, which reaches the entrance or exit cell along its queue link; true when the cell took a bit.</summary>
	private static bool RelinkOutside( ParkCellMap cells, int x, int y, int outsideX, int outsideY, LinkEffects? effects )
	{
		PathLinkWriter.LinkPath( cells, outsideX, outsideY, effects );
		effects?.Refresh?.Invoke( outsideX, outsideY );
		return cells.LinksAt( x, y ) != 0;
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
