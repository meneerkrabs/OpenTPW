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
	Refused
}

/// <summary>
/// Builds queue paths on <see cref="GuestPathGrid"/> (headless; <see cref="Level"/>'s queue tool and scripted parks
/// use it). Each new cell links back toward its predecessor, so the first cell links toward the ride
/// entrance (docs/reverse/QUEUE-plan.md §3.3), and the ride's queue is recomputed after every edit.
/// </summary>
public static class QueuePaths
{
	/// <summary>Most cells one queue may have: 4 × 25 = 100 positions.</summary>
	// [APPROX:QUEUE-013] maximum queue length 25 cells (100 positions); tp.exe's queue-cell setter 0x005346D0, the cell validator 0x00535670 and the line layer 0x00536100 compare no cell count, and the back-of-queue walk 0x004DE130 stops only after 1,000 cells ("GetBackOfQueue() crashed!"); a HasQueue ride's limit is its QueueWaitTimeConstant formula (Belly Bounce 21), so the cap only shortens queues of rides whose limit exceeds 100 — evidence needed: the rest of the tool's cell handler (0x00524960, 0x00532FC0)
	public const int MaximumCells = 25;

	/// <summary>Checks whether (x, y) can be the next cell of <paramref name="ride"/>'s queue (no writes).</summary>
	// [APPROX:QUEUE-014] a queue is laid cell by cell from the entrance's outside cell; each cell must touch the current back cell, must not be a path or queue cell, and is linked toward that back cell. tp.exe's tool lays straight segments along the dominant axis (0x00536100), each from the previous end, the first from the cell the player clicks (SetMode 0x0052F200 sets the start to −1 at 0x0052F340); the validator 0x00535670 lets a queue cell replace a path cell that is not NoModify (0x005346D0 clears the path first), and the last cell may end on a path cell (code 8, the connection) — evidence needed: the writer of the queue link +0x0D and the segment commit in 0x00524960 (UI-031)
	public static QueueBuildResult CheckExtend( GuestPathGrid grid, RideVisitorBridge ride, int x, int y, Func<int, int, bool>? isBlocked = null ) =>
		CheckExtend( grid, ride, x, y, Array.Empty<(int X, int Y)>(), isBlocked );

	/// <summary>
	/// Checks (x, y) as the next queue cell after <paramref name="pending"/>, the cells a ghost line has already
	/// accepted in order, as if they were laid (no writes): they count toward the length, the last one is the back
	/// cell, and none of them can be laid again. The queue tool's preview validates every cell this way.
	/// </summary>
	public static QueueBuildResult CheckExtend( GuestPathGrid grid, RideVisitorBridge ride, int x, int y, IReadOnlyList<(int X, int Y)> pending, Func<int, int, bool>? isBlocked = null )
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
		// CanChangeCellType allows a queue over an empty cell (and over a path except on a line's last cell); this tool also refuses path cells (QUEUE-014).
		if ( grid.Cells.TypeAt( x, y ) != ParkCellType.Empty || !ParkCellMap.CanChangeCellType( grid.Cells.RawTypeAt( x, y ), (byte)ParkCellType.Queue, lastCell: false )
			|| grid.IsWalkable( x, y ) || isBlocked?.Invoke( x, y ) == true || pending.Contains( (x, y) ) )
			return QueueBuildResult.Blocked;
		if ( !hasCells )
			return (x, y) == front ? QueueBuildResult.Ok : QueueBuildResult.NotAtQueueEnd;
		var back = pending.Count > 0 ? pending[^1] : ride.QueueBackCell;
		return GuestPathGrid.DirectionBetween( x, y, back.X, back.Y ) >= 0 ? QueueBuildResult.Ok : QueueBuildResult.NotAtQueueEnd;
	}

	/// <summary>Lays (x, y) as the next queue cell of <paramref name="ride"/> and recomputes the queue.</summary>
	// [APPROX:PATH-004] PATH-CONNECT: the link writer tp.exe 0x5348d0 is ported (PathLinkWriter) and the path commit runs it as its link pass; the queue tool does not run its mode 3 (0x53538c), so queue cells carry no +0x0c links (QUEUE-003) — still open: the queue line's mode-3 link pass
	public static QueueBuildResult TryExtend( GuestPathGrid grid, RideVisitorBridge ride, int x, int y, Func<int, int, bool>? isBlocked = null )
	{
		ride.RecomputeQueue( grid );
		var result = CheckExtend( grid, ride, x, y, isBlocked );
		if ( result != QueueBuildResult.Ok )
			return result;
		var front = ride.QueueFrontCell!.Value;
		var towards = grid.IsQueue( front.X, front.Y )
			? GuestPathGrid.DirectionBetween( x, y, ride.QueueBackCell.X, ride.QueueBackCell.Y )
			: ride.QueueEntranceDirection;
		grid.SetQueue( x, y, towards );
		ride.RecomputeQueue( grid );
		return QueueBuildResult.Ok;
	}

	/// <summary>Lays a run of cells in order; stops at the first refusal. Returns the number laid.</summary>
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
