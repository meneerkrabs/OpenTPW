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
	/// <summary>Most cells one queue may have: 4 × 25 = 100 positions, the HasQueue limit.</summary>
	// [APPROX:QUEUE-013] maximum queue length 25 cells (more cells add no room beyond the 100-guest HasQueue limit); no length limit was found in the traced build code besides the 1000-step walk guard — evidence needed: the queue tool in 0x10070B98..0x1008C7C0
	public const int MaximumCells = 25;

	/// <summary>Checks whether (x, y) can be the next cell of <paramref name="ride"/>'s queue (no writes).</summary>
	// [APPROX:QUEUE-014] a queue is laid cell by cell from the entrance's outside cell; each cell must touch the current back cell, must not be a path or queue cell, and is linked toward that back cell — evidence needed: the queue tool's placement rules (UI-031, 0x10070B98..0x1008C7C0)
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
		// CanChangeCellType allows a queue over an empty cell (and over a path only for a line's last cell, which this tool does not lay).
		if ( grid.Cells.TypeAt( x, y ) != ParkCellType.Empty || !ParkCellMap.CanChangeCellType( grid.Cells.RawTypeAt( x, y ), (byte)ParkCellType.Queue, lastCell: false )
			|| grid.IsWalkable( x, y ) || isBlocked?.Invoke( x, y ) == true || pending.Contains( (x, y) ) )
			return QueueBuildResult.Blocked;
		if ( !hasCells )
			return (x, y) == front ? QueueBuildResult.Ok : QueueBuildResult.NotAtQueueEnd;
		var back = pending.Count > 0 ? pending[^1] : ride.QueueBackCell;
		return GuestPathGrid.DirectionBetween( x, y, back.X, back.Y ) >= 0 ? QueueBuildResult.Ok : QueueBuildResult.NotAtQueueEnd;
	}

	/// <summary>Lays (x, y) as the next queue cell of <paramref name="ride"/> and recomputes the queue.</summary>
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
	// [APPROX:QUEUE-015] removing a queue cell also removes every cell behind it (each one is refunded by the caller); the player's bulldozer route into ClearCell 0x859b4 is not traced — evidence needed: the remove tool's call into 0x100859B4
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
