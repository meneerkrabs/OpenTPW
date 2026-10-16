namespace OpenTPW;

/// <summary>Outcome of laying one queue cell.</summary>
public enum QueueBuildResult
{
	Ok,
	OutOfBounds,
	/// <summary>The attraction has no <c>Info.HasQueue</c> (shops, toilets and sideshows use a one-cell virtual queue).</summary>
	NoQueue,
	/// <summary>The entrance's outside cell is unknown or is a walkable path (remove the path first).</summary>
	NoFrontCell,
	/// <summary>The cell is not the next cell of the queue: the front cell first, then a neighbour of the back cell.</summary>
	NotAtQueueEnd,
	/// <summary>A path cell, a queue cell, an object or terrain the build rule refuses.</summary>
	Blocked,
	TooLong,
	/// <summary>Refused by the level (read-only visit, no money).</summary>
	Refused,
	/// <summary>The cell is a path cell beside the back of the queue: the queue was linked to it (a queue line ending on a path, code 8) and nothing was laid.</summary>
	Connected
}

/// <summary>
/// Builds queue paths on <see cref="GuestPathGrid"/> (headless; <see cref="Level"/>'s queue tool and scripted parks
/// use it). Each new cell links back toward its predecessor, so the first cell links toward the ride
/// entrance (docs/reverse/QUEUE-plan.md §3.3), and the ride's queue is recomputed after every edit.
/// </summary>
public static class QueuePaths
{
	/// <summary>Most cells one queue may have: the bound of the original's back-of-queue walk.</summary>
	// [BIN:TPW-EXE:0x004DE130 back of queue] the original compares no cell count anywhere in the queue tool (the click 0x524960, the setter 0x5346D0, the validator 0x535670 and the line layer 0x536100 test none); only the back-of-queue walk stops after 1,000 cells and logs "GetBackOfQueue() crashed!", so a queue can be that long and no longer
	public const int MaximumCells = 1000;

	/// <summary>Checks whether (x, y) can be the next cell of <paramref name="ride"/>'s queue (no writes).</summary>
	// [APPROX:QUEUE-014] Queue lines start on the entrance's outside cell and lay empty cells that each touch the current back cell, where tp.exe's queue tool lays straight segments along the dominant axis (0x536100, ported) from a start the player clicks (SetMode 0x52f200 clears it), each cell validated by 0x535670: an empty cell is accepted, a path cell is replaced unless its flag 0x20 (NoModify) is set, a path cell on the line's last cell is code 8, the connection (ported), an object cell is refused, a queue cell of another ride (owner word +0x10 against the tool's ride, 0x818698) is refused and the tool's own passed; the commit 0x532fc0 then types the cell (0x5346D0, which clears a path first), links it (pass 0x80, the link writer in mode 3), gives it the queue link toward the previous cell (pass 0x82) and the tool's ride as owner word (pass 0x83); OpenTPW keeps no owner word (a queue cell belongs to the ride whose chain reaches it) and lays no queue over a path cell, and a queue line must start on the front cell and touch the back cell — evidence needed: the validator's object-cell branch (0x818C10 and 0x818C14), the rest of the commit 0x532fc0 and the refund of the cell clear 0x5367A0, which reads the owner word
	public static QueueBuildResult CheckExtend( GuestPathGrid grid, RideVisitorBridge ride, int x, int y, Func<int, int, bool>? isBlocked = null, Func<int, int, bool>? givesWay = null ) =>
		CheckExtend( grid, ride, x, y, Array.Empty<(int X, int Y)>(), isBlocked, givesWay );

	/// <summary>
	/// Checks (x, y) as the next queue cell after <paramref name="pending"/>, the cells a ghost line has already
	/// accepted in order, as if they were laid (no writes): they count toward the length, the last one is the back
	/// cell, and none of them can be laid again. The queue tool's preview validates every cell this way.
	/// </summary>
	/// <param name="givesWay">An object at the cell the queue may cover (tp.exe's overwrite priority below 3); the cell counts as empty and <see cref="TryExtend(GuestPathGrid, RideVisitorBridge, int, int, Func{int, int, bool}?, Func{int, int, bool}?, Action{int, int}?)"/> demolishes the object.</param>
	public static QueueBuildResult CheckExtend( GuestPathGrid grid, RideVisitorBridge ride, int x, int y, IReadOnlyList<(int X, int Y)> pending, Func<int, int, bool>? isBlocked = null, Func<int, int, bool>? givesWay = null )
	{
		if ( !grid.InBounds( x, y ) )
			return QueueBuildResult.OutOfBounds;
		if ( !ride.HasQueue )
			return QueueBuildResult.NoQueue;
		if ( ride.QueueFrontCell is not { } front || ride.QueueEntranceDirection < 0 || grid.IsWalkable( front.X, front.Y ) )
			return QueueBuildResult.NoFrontCell;
		var frontIsQueue = grid.IsQueue( front.X, front.Y );
		var hasCells = frontIsQueue || pending.Count > 0;
		if ( hasCells && (frontIsQueue ? ride.QueueSizeInCells : 0) + pending.Count >= MaximumCells )
			return QueueBuildResult.TooLong;
		// CanChangeCellType allows a queue over an empty cell (and over a path except on a line's last cell); this tool also refuses path cells, where 0x535670 replaces a path cell unless it is NoModify (QUEUE-014).
		// [BIN:TPW-EXE:0x535D66 overwrite priority] the queue tool's 0x818C10 is 3: an object cell whose owner's priority is below it is allowed (features of priority 0 or 1)
		var covers = givesWay?.Invoke( x, y ) == true;
		if ( (!covers && (grid.Cells.TypeAt( x, y ) != ParkCellType.Empty || !ParkCellMap.CanChangeCellType( grid.Cells.RawTypeAt( x, y ), (byte)ParkCellType.Queue, lastCell: false )))
			|| grid.IsWalkable( x, y ) || isBlocked?.Invoke( x, y ) == true || pending.Contains( (x, y) ) )
			return QueueBuildResult.Blocked;
		if ( !hasCells )
			return (x, y) == front ? QueueBuildResult.Ok : QueueBuildResult.NotAtQueueEnd;
		var back = pending.Count > 0 ? pending[^1] : ride.QueueBackCell;
		return GuestPathGrid.DirectionBetween( x, y, back.X, back.Y ) >= 0 ? QueueBuildResult.Ok : QueueBuildResult.NotAtQueueEnd;
	}

	/// <summary>
	/// Lays (x, y) as the next queue cell of <paramref name="ride"/>, links it (the line's link pass) and recomputes the queue; a path
	/// cell beside the back of an existing queue is linked to the back instead (<see cref="QueueBuildResult.Connected"/>, nothing laid).
	/// </summary>
	/// <param name="givesWay">An object the queue may cover (<see cref="CheckExtend(GuestPathGrid, RideVisitorBridge, int, int, IReadOnlyList{ValueTuple{int, int}}, Func{int, int, bool}?, Func{int, int, bool}?)"/>).</param>
	/// <param name="demolish">Demolishes that object before the cell is laid (tp.exe's cell op 0x87).</param>
	public static QueueBuildResult TryExtend( GuestPathGrid grid, RideVisitorBridge ride, int x, int y, Func<int, int, bool>? isBlocked = null, Func<int, int, bool>? givesWay = null, Action<int, int>? demolish = null )
	{
		ride.RecomputeQueue( grid );
		if ( grid.IsWalkable( x, y ) && HasQueueCells( grid, ride ) && GuestPathGrid.DirectionBetween( ride.QueueBackCell.X, ride.QueueBackCell.Y, x, y ) >= 0 )
			return Connect( grid, ride, ride.QueueBackCell, (x, y) ) ? QueueBuildResult.Connected : QueueBuildResult.Blocked;
		var result = CheckExtend( grid, ride, x, y, isBlocked, givesWay );
		if ( result != QueueBuildResult.Ok )
			return result;
		// [BIN:TPW-EXE:0x5344E5 cell op 0x87] the commit's first LayLine pass demolishes the cell's lower-priority owner (0x527EE0) before the queue type is written
		if ( givesWay?.Invoke( x, y ) == true )
		{
			demolish?.Invoke( x, y );
			if ( grid.Cells.TypeAt( x, y ) != ParkCellType.Empty || isBlocked?.Invoke( x, y ) == true )
				return QueueBuildResult.Blocked;
		}
		var first = !HasQueueCells( grid, ride );
		var back = ride.QueueBackCell;
		var towards = first ? ride.QueueEntranceDirection : GuestPathGrid.DirectionBetween( x, y, back.X, back.Y );
		grid.SetQueue( x, y, towards );
		if ( first )
			LinkFront( grid.Cells, x, y, ride.QueueEntranceDirection );
		else
			PathLinkWriter.LinkQueueStep( grid.Cells, x, y, x - back.X, y - back.Y, (byte)ParkCellType.Queue, OwnedBy( ride ) );
		ride.RecomputeQueue( grid );
		return QueueBuildResult.Ok;
	}

	/// <summary>Whether the ride's queue has real queue cells (its front is a queue cell), not only the one-cell fallback.</summary>
	private static bool HasQueueCells( GuestPathGrid grid, RideVisitorBridge ride ) =>
		ride.QueueFrontCell is { } front && grid.IsQueue( front.X, front.Y );

	/// <summary>
	/// The owner test of the link writer's mode 3 (the cell's owner word <c>+0x10</c> is the tool's ride or 0): OpenTPW keeps no owner
	/// word; a queue cell belongs to the ride whose queue walk reaches it, and the cells a line meets are the ride's own (the validator
	/// refuses another ride's queue cell, <c>0x5359C6</c>).
	/// </summary>
	private static Func<int, int, bool> OwnedBy( RideVisitorBridge ride )
	{
		var cells = ride.QueueCells;
		return ( x, y ) => cells.Contains( (x, y) );
	}

	/// <summary>
	/// The first cell of a ride's queue links toward the ride's entrance: the link writer's first-cell scan, which links the line's
	/// first cell to a neighbour that is the ride's entrance cell and whose queue link points at it.
	/// </summary>
	// [BIN:TPW-EXE:0x0053522D first queue cell] for the line's first cell (0x820AA4) each cardinal neighbour that is the ride's entrance cell (ride +0x36) and whose queue link +0x0D points back is linked both ways (0x53525A..0x535366); the ride finds its front cell through the entrance cell's +0x0C bit (0x4DE040, order 1, 16, 64, 4)
	// OpenTPW's ride cells are not typed map cells (WALK-005): only the front cell's bit toward the entrance is written, and the ride keeps its front in QueueFrontCell rather than reading the entrance cell's bit (QUEUE-003)
	private static void LinkFront( ParkCellMap cells, int x, int y, int entranceDirection )
	{
		if ( entranceDirection is >= 0 and < 4 )
			cells.SetLinks( x, y, (byte)(cells.LinksAt( x, y ) | GuestPathGrid.LinkValue( entranceDirection )) );
	}

	/// <summary>
	/// A queue line of <paramref name="ride"/> ending on the path cell <paramref name="path"/> after the queue cell <paramref name="before"/>
	/// (the validator's code 8): the link writer's mode 3 links the two when the queue cell has room, and clears the path cell's diagonals beside
	/// the queue. False when <paramref name="path"/> is not a path cell beside <paramref name="before"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x0052744E queue line ending on a path] the type pass refuses the last cell (queue over path is not allowed there, 0x535600), but the link pass 0x80 still runs on it with the mode of the last type written (3): mode 3 links the path cell to the queue cell before it (0x53538C); the pass 0x82 then gives the path cell a queue link +0x0D toward it, which no reader of a type-1 cell uses (0x4D8750 and 0x5348D0 read it for types 9 and 10, 0x535DD0 and 0x4DE670 for type 3), and pass 0x83 its owner word
	// [BIN:TPW-EXE:0x004DE1F0 queue re-evaluation] once the queue is connected (0x4DE4A0) a closed ride whose state +0x19C is not 1, 2 or 4 is opened (+0x68 = 1, message 0x4E0E60)
	// [APPROX:QUEUE-023] connecting a queue does not open the ride; OpenTPW leaves the open state to the player and the economy — the callers of 0x4DE1F0 are the queue tool's cell click 0x524960, the placement 0x528A70, 0x52FE50, the cell type setter 0x5346D0 and the cell clear 0x5367A0, so the original re-evaluates (and may open) the ride after every edit of its cells — evidence needed: none for the rule; OpenTPW has no queue-edit hook on cell writes that could open a ride
	public static bool Connect( GuestPathGrid grid, RideVisitorBridge ride, (int X, int Y) before, (int X, int Y) path )
	{
		if ( !grid.IsWalkable( path.X, path.Y ) || GuestPathGrid.DirectionBetween( before.X, before.Y, path.X, path.Y ) < 0 )
			return false;
		PathLinkWriter.LinkQueueStep( grid.Cells, path.X, path.Y, path.X - before.X, path.Y - before.Y, (byte)ParkCellType.Queue, OwnedBy( ride ) );
		ride.RecomputeQueue( grid );
		return true;
	}

	/// <summary>
	/// Links the back of <paramref name="ride"/>'s queue to its first adjacent path cell in the order −Y, +X, +Y, −X, as the queue tool does
	/// when the player ends a queue line on that cell (OpenTPW's headless builders and the rebuild of older saves). False when there is none.
	/// </summary>
	public static bool TryConnect( GuestPathGrid grid, RideVisitorBridge ride )
	{
		ride.RecomputeQueue( grid );
		if ( !HasQueueCells( grid, ride ) )
			return false;
		var back = ride.QueueBackCell;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			if ( grid.IsWalkable( back.X + dx, back.Y + dy ) )
				return Connect( grid, ride, back, (back.X + dx, back.Y + dy) );
		}
		return false;
	}

	/// <summary>
	/// A queue from a save made before queue cells were linked (every one of its cells has an empty link byte) gets the links the queue
	/// tool would have written: the front toward the entrance, each cell to the one before it, and the back to the cell OpenTPW used to
	/// join from (<see cref="TryConnect"/>), so the loaded queue is joined as it was. Returns true when it wrote links.
	/// </summary>
	public static bool RebuildMissingLinks( GuestPathGrid grid, RideVisitorBridge ride )
	{
		ride.RecomputeQueue( grid );
		if ( !HasQueueCells( grid, ride ) )
			return false;
		var cells = ride.QueueCells;
		if ( cells.Any( cell => grid.Cells.LinksAt( cell.X, cell.Y ) != 0 ) )
			return false;
		LinkFront( grid.Cells, cells[0].X, cells[0].Y, ride.QueueEntranceDirection );
		var owned = OwnedBy( ride );
		for ( var index = 1; index < cells.Count; index++ )
			PathLinkWriter.LinkQueueStep( grid.Cells, cells[index].X, cells[index].Y, cells[index].X - cells[index - 1].X, cells[index].Y - cells[index - 1].Y,
				(byte)ParkCellType.Queue, owned );
		TryConnect( grid, ride );
		return true;
	}

	/// <summary>Lays a run of cells in order; stops at the first refusal or at a path cell the queue is connected to. Returns the number laid.</summary>
	public static int TryExtend( GuestPathGrid grid, RideVisitorBridge ride, IEnumerable<(int X, int Y)> cells, Func<int, int, bool>? isBlocked = null )
	{
		var count = 0;
		foreach ( var (x, y) in cells )
		{
			if ( TryExtend( grid, ride, x, y, isBlocked ) != QueueBuildResult.Ok )
				break;
			count++;
		}
		return count;
	}

	/// <summary>Removes queue cell (x, y) of <paramref name="ride"/> and every cell behind it; returns the number removed.</summary>
	// [APPROX:QUEUE-015] removing a queue cell also removes every cell behind it (each one is refunded by the caller); the original's cell clear (tp.exe 0x005367A0, queue branch) refunds and clears only that cell, and the bulldozer's rectangle pass 0x0052BDD0 clears only path cells (type 1 test 0x00536310 at 0x0052C116 before 0x005367A0) and hands ride cells (types 4, 9, 10) to the demolition 0x00527EE0 — evidence needed: the route that clears a queue cell (0x00527EE0's own clears, and the edit-list clear 0x00452CA0 from 0x0043EAF0 / 0x00442EA0)
	public static int RemoveFrom( GuestPathGrid grid, RideVisitorBridge ride, int x, int y )
	{
		ride.RecomputeQueue( grid );
		var cells = ride.QueueCells;
		var index = -1;
		for ( var i = 0; i < cells.Count; i++ )
		{
			if ( cells[i] == (x, y) )
				index = i;
		}
		if ( index < 0 || !grid.IsQueue( x, y ) )
			return 0;
		var removed = cells.Skip( index ).ToArray();
		foreach ( var cell in removed )
			grid.ClearQueue( cell.X, cell.Y );
		ride.RecomputeQueue( grid );
		return removed.Length;
	}
}
