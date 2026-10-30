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
	// [BIN:TPW-EXE:0x0053402E queue link writer] the segment commit 0x527403..0x527517 runs LayLine once per pass: the types, the links (0x5348D0 in the mode of the last type written), 0x82, 0x85, 0x86, 0x83 and the refresh 0x81; the pass 0x82 writes the queue link +0xD of a cell that has none from the line's step, towards the previous cell of the line (dx −1 gives 0x04, dx 1 0x40, dy −1 0x10, dy 1 0x01: 0x53402E..0x53407B), and the pass 0x83 the ride's owner word (0x5340E9, RideVisitorBridge.OwnerWord)
	// [BIN:TPW-EXE:0x00536100 queue line] the queue tool lays straight segments along the dominant axis (0x536100, ported) from the line's start: mode 3 always has one, the cell outside the entrance after a placement (0x52A050 stores it in 0x81EDE4/0x81EDE8) or the queue's back after the edit click (0x530120, 0x526103), so no first click without a start reaches the queue tool and the tool starting at the front or back cell is the original's; a line ending on a path cell connects the queue (code 8 on the last cell); each cell is validated as 0x535670 does (an empty cell is accepted, a path cell is replaced unless NoModify and not on a line's last cell, an object cell of a lower overwrite priority than the queue tool's 3 is demolished by the commit's first pass 0x87, another ride's queue cell is refused by the owner word +0x10), then typed (0x5346D0), linked, given its queue link toward the previous cell (pass 0x82) and the tool's ride as owner word (pass 0x83); a cell that touches no queue is laid free-standing (no queue link, the ride's owner word only) because the validator never asks a cell to touch the back cell (the remove tool takes it, the ride's chain ignores it). The ordered passes run in QueueSegmentReducer for the placed rides' cells; this per-cell route serves the bridges without an owner word (the gates, the test rigs, the Theme Park Inc slice)
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
		// CanChangeCellType allows a queue over an empty cell (and over a path except on a line's last cell); this per-cell route also refuses path cells, where 0x535670 replaces a path cell unless it is NoModify; the placed rides' lines run through QueueSegmentReducer, which replaces them.
		// [BIN:TPW-EXE:0x535D66 overwrite priority] the queue tool's 0x818C10 is 3: an object cell whose owner's priority is below it is allowed (features of priority 0 or 1)
		var covers = givesWay?.Invoke( x, y ) == true;
		if ( (!covers && ((ParkCellType)grid.Cells.PrimaryTypeAt( x, y ) != ParkCellType.Empty || !ParkCellMap.CanChangeCellType( grid.Cells.PrimaryTypeAt( x, y ), (byte)ParkCellType.Queue, lastCell: false )))
			|| grid.IsWalkable( x, y ) || isBlocked?.Invoke( x, y ) == true || pending.Contains( (x, y) ) )
			return QueueBuildResult.Blocked;
		// [BIN:TPW-EXE:0x00535670 queue cell validator] the validator tests the cell alone: it asks that the next cell of a line touches the previous one only in the line walk (0x536100), never against the ride's back cell, so a queue cell may stand free (an orphan, owned by the ride through its owner word but in no chain)
		return QueueBuildResult.Ok;
	}

	/// <summary>
	/// Whether (x, y) would continue the ride's queue: it is the front cell of a ride that has no queue cells yet, or it touches the back cell (or the last of <paramref name="pending"/>).
	/// Any other cell is laid as a free-standing queue cell.
	/// </summary>
	public static bool ContinuesQueue( GuestPathGrid grid, RideVisitorBridge ride, int x, int y, IReadOnlyList<(int X, int Y)>? pending = null )
	{
		pending ??= Array.Empty<(int X, int Y)>();
		if ( ride.QueueFrontCell is not { } front )
			return false;
		var hasCells = grid.IsQueue( front.X, front.Y ) || pending.Count > 0;
		if ( !hasCells )
			return (x, y) == front;
		var back = pending.Count > 0 ? pending[^1] : ride.QueueBackCell;
		return GuestPathGrid.DirectionBetween( x, y, back.X, back.Y ) >= 0;
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
		if ( grid.IsWalkable( x, y ) && grid.Cells.PoolBridges?.Covers( x, y ) != true && HasQueueCells( grid, ride ) && GuestPathGrid.DirectionBetween( ride.QueueBackCell.X, ride.QueueBackCell.Y, x, y ) >= 0 )
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
		var continues = ContinuesQueue( grid, ride, x, y );
		var first = !HasQueueCells( grid, ride );
		var back = ride.QueueBackCell;
		// a free-standing cell has no predecessor: no queue link and no links, only the ride's owner word
		var towards = !continues ? -1 : first ? ride.QueueEntranceDirection : GuestPathGrid.DirectionBetween( x, y, back.X, back.Y );
		grid.SetQueue( x, y, towards );
		if ( ride.OwnerWord != 0 )
			grid.Cells.SetOwner( x, y, ride.OwnerWord );
		if ( !continues )
		{
			ride.RecomputeQueue( grid );
			return QueueBuildResult.Ok;
		}
		if ( first )
			LinkFront( grid.Cells, x, y, ride.QueueEntranceDirection, ride );
		else
			PathLinkWriter.LinkQueueStep( grid.Cells, x, y, x - back.X, y - back.Y, (byte)ParkCellType.Queue, OwnedBy( ride ) );
		ride.RecomputeQueue( grid );
		ride.NotifyQueueEdited( grid );
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
	private static void LinkFront( ParkCellMap cells, int x, int y, int entranceDirection, RideVisitorBridge? ride = null )
	{
		if ( entranceDirection is >= 0 and < 4 )
			cells.SetLinks( x, y, (byte)(cells.LinksAt( x, y ) | GuestPathGrid.LinkValue( entranceDirection )) );
		// [BIN:TPW-EXE:0x0053525A both ways] the neighbour that is the ride's entrance cell (+0x36, the cell of the footprint) is linked to the first queue cell too, which is how the ride finds its front again (0x4DE040 reads the entrance's bit): a ride whose entrance lost its link (the queue was removed) has a front once a queue is laid again
		if ( ride?.AccessCell is { } entrance && cells.InBounds( entrance.X, entrance.Y ) && cells.RawTypeAt( entrance.X, entrance.Y ) == ParkCellTypes.Entrance
			&& GuestPathGrid.DirectionBetween( entrance.X, entrance.Y, x, y ) is >= 0 and < 4 and var toward )
			cells.SetLinks( entrance.X, entrance.Y, (byte)(cells.LinksAt( entrance.X, entrance.Y ) | GuestPathGrid.LinkValue( toward )) );
	}

	/// <summary>
	/// A queue line of <paramref name="ride"/> ending on the path cell <paramref name="path"/> after the queue cell <paramref name="before"/>
	/// (the validator's code 8): the link writer's mode 3 links the two when the queue cell has room, and clears the path cell's diagonals beside
	/// the queue. False when <paramref name="path"/> is not a path cell beside <paramref name="before"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x0052744E queue line ending on a path] the type pass refuses the last cell (queue over path is not allowed there, 0x535600), but the link pass 0x80 still runs on it with the mode of the last type written (3): mode 3 links the path cell to the queue cell before it (0x53538C); the pass 0x82 then gives the path cell a queue link +0x0D toward it, which no reader of a type-1 cell uses (0x4D8750 and 0x5348D0 read it for types 9 and 10, 0x535DD0 and 0x4DE670 for type 3), and pass 0x83 its owner word
	// [BIN:TPW-EXE:0x004DE1F0 queue re-evaluation] once the queue is connected (0x4DE4A0) a closed ride whose state +0x19C is not 1, 2 or 4 is opened (+0x68 = 1, message 0x4E0E60)
	// [BIN:TPW-EXE:0x004DE1F0 re-evaluation callers] 0x4DE1F0 runs after the queue tool's cell click 0x524960, Back 0x52FE50 (0x52FFEC), the cell type setter's path over a queue cell (0x5346D0 at 0x534858), a path cell's clear beside an entrance cell (0x5367A0 at 0x53694B) and a ride's placement (0x529890, 0x52537A, 0x5259AE): every one raises RideVisitorBridge.QueueEdited, and the level opens a closed ride that passes the original's tests (Level.ReevaluateQueuedRide); the clear of a queue cell calls it nowhere
	public static bool Connect( GuestPathGrid grid, RideVisitorBridge ride, (int X, int Y) before, (int X, int Y) path )
	{
		if ( !grid.IsWalkable( path.X, path.Y ) || grid.Cells.PoolBridges?.Covers( path.X, path.Y ) == true || GuestPathGrid.DirectionBetween( before.X, before.Y, path.X, path.Y ) < 0 )
			return false;
		PathLinkWriter.LinkQueueStep( grid.Cells, path.X, path.Y, path.X - before.X, path.Y - before.Y, (byte)ParkCellType.Queue, OwnedBy( ride ) );
		if ( ride.OwnerWord != 0 )
			grid.Cells.SetOwner( path.X, path.Y, ride.OwnerWord );
		ride.RecomputeQueue( grid );
		ride.NotifyQueueEdited( grid );
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
			if ( grid.IsWalkable( back.X + dx, back.Y + dy ) && grid.Cells.PoolBridges?.Covers( back.X + dx, back.Y + dy ) != true )
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
		LinkFront( grid.Cells, cells[0].X, cells[0].Y, ride.QueueEntranceDirection, ride );
		var owned = OwnedBy( ride );
		for ( var index = 1; index < cells.Count; index++ )
			PathLinkWriter.LinkQueueStep( grid.Cells, cells[index].X, cells[index].Y, cells[index].X - cells[index - 1].X, cells[index].Y - cells[index - 1].Y,
				(byte)ParkCellType.Queue, owned );
		TryConnect( grid, ride );
		return true;
	}

	/// <summary>Most points the queue tool's stack of corner points keeps: the original stores a point while the count is at most 0x400, one past its 0x400-point array.</summary>
	public const int CornerStackCapacity = 0x401;

	/// <summary>
	/// The queue tool's stack of corner points for <paramref name="ride"/>'s queue, as the walk <c>0x530120</c> fills it: the front cell first, then
	/// every corner cell of the queue in order, then the last cell the walk reaches (the back cell; a corner back cell comes up twice, and a queue
	/// with no cells yet gives the front cell twice). The queue tool's undo pops this stack one point at a time (<see cref="CellBuildTool.Undo"/>),
	/// and a ride's demolition undoes every point (<c>0x527EE0</c>). Call <see cref="RideVisitorBridge.RecomputeQueue"/> first.
	/// </summary>
	// [BIN:TPW-EXE:0x00530120 corner walk] the walk starts at the ride's entrance cell (+0x36), whose link byte gives the first step: a neighbour that is not a queue cell (types 3 and 9, 0x536320) or is one of this ride (owner word +0x10 equal to the entrance cell's) is pushed first (0x5301FC, 0x5304C0), then the walk steps off the entrance (type 9 takes no corner test, 0x5302E1) along the cells: a cell with exactly two cardinal links that are not opposite (0x53AE00) turns the walk to the link that is not the one it came by (link byte xor the reverse of the step, 0x530291..0x53029A) and is pushed (0x5302AA), a cell with one link ends the walk (0x5302F2..0x5302FC), and a cell that is not a queue cell ends it too (0x5302D4); the last cell is pushed (0x530494) and becomes the line start (0x81EDE4, 0x81EDE8); at most 0x401 points are stored (0x5301F5, 0x5302A3, 0x530492)
	// [BIN:TPW-EXE:0x0053036F corner walk ends on a path] a walk that steps onto a path cell (type 1, 0x536310) ends on the cell before it, the queue's back cell (0x530342..0x53036F); the walk then clears the link between the two (QueuePaths.DetachFromPath)
	public static IReadOnlyList<(int X, int Y)> CornerStack( GuestPathGrid grid, RideVisitorBridge ride ) => WalkCorners( grid, ride, out _ );

	/// <summary>
	/// The corner walk's counter writes: every queue cell it visits takes the placement counter <c>+0x20</c> 1 when it is a corner (it is also pushed) and 0 otherwise, the back cell included, so
	/// the undo that follows counts each cell down from what the walk left. A cell that is no queue cell, and a queue with no cells yet, are left alone.
	/// </summary>
	// [BIN:TPW-EXE:0x005302C2 walk counters] the walk 0x530120 stores the counter through 0x5228A0 after each cell's corner test (0x53AE00): 1 for a corner (0x5302BE..0x5302C2, with the push), 0 for any other cell it visits (0x5302C9..0x5302CD); the entrance cell, the first it visits, takes 0 as well; the queue edit click (0x526103) and the demolition (0x527F9A) run the walk, the clear 0x5367A0 counts down from these values and clears a bend whatever its counter
	public static void WriteWalkCounts( GuestPathGrid grid, RideVisitorBridge ride )
	{
		ride.RecomputeQueue( grid );
		if ( ride.QueueFrontCell is not { } front || !grid.IsQueue( front.X, front.Y ) )
			return;
		var visited = new List<((int X, int Y) Cell, bool Corner)>();
		WalkCorners( grid, ride, out _, visited );
		// [BIN:TPW-EXE:0x005302C9 entrance counter] the entrance cell (type 9) passes the queue test 0x536320 and takes 0 like any non-corner cell, the first the walk visits
		if ( ride.QueueEntranceDirection is >= 0 and <= 3 )
		{
			var (dx, dy) = GuestPathGrid.Directions[ride.QueueEntranceDirection];
			var entrance = (X: front.X + dx, Y: front.Y + dy);
			if ( grid.Cells.InBounds( entrance.X, entrance.Y ) && grid.Cells.RawTypeAt( entrance.X, entrance.Y ) == ParkCellTypes.Entrance )
				grid.Cells.SetPlacementCount( entrance.X, entrance.Y, 0 );
		}
		foreach ( var (cell, corner) in visited )
			grid.Cells.SetPlacementCount( cell.X, cell.Y, corner ? (short)1 : (short)0 );
	}

	/// <summary>Where a corner walk ended on a path cell: the queue's back cell, the path cell and the step (link bit) from the one to the other.</summary>
	private readonly record struct PathEnd( (int X, int Y) Back, (int X, int Y) Path, byte Step );

	private static IReadOnlyList<(int X, int Y)> WalkCorners( GuestPathGrid grid, RideVisitorBridge ride, out PathEnd? pathEnd, List<((int X, int Y) Cell, bool Corner)>? visited = null )
	{
		pathEnd = null;
		var stack = new List<(int X, int Y)>();
		if ( ride.QueueFrontCell is not { } front || ride.QueueEntranceDirection is < 0 or > 3 )
			return stack;
		Add( front );
		if ( !grid.IsQueue( front.X, front.Y ) )
		{
			// the entrance's outside cell holds no queue yet: the walk steps onto it and ends there
			Add( front );
			return stack;
		}
		// the walk comes from the entrance cell: its step is the one from the entrance to the front cell
		var step = GuestPathGrid.LinkValue( (ride.QueueEntranceDirection + 2) & 3 );
		var position = front;
		var previous = front;
		for ( var guard = 0; guard < 4096; guard++ )
		{
			var type = grid.Cells.TypeAt( position.X, position.Y );
			if ( type != ParkCellType.Queue )
			{
				if ( type == ParkCellType.Path )
					pathEnd = new PathEnd( previous, position, step );
				Add( type == ParkCellType.Path ? previous : position );
				return stack;
			}
			var links = grid.Cells.LinksAt( position.X, position.Y );
			visited?.Add( (position, IsCornerLinks( links )) );
			if ( IsCornerLinks( links ) )
			{
				step = (byte)(links ^ ReverseLink( step ));
				Add( position );
			}
			if ( System.Numerics.BitOperations.PopCount( links ) == 1 || GuestPathGrid.LinkDirection( step ) < 0 )
			{
				Add( position );
				return stack;
			}
			previous = position;
			var (dx, dy) = GuestPathGrid.Directions[GuestPathGrid.LinkDirection( step )];
			position = (position.X + dx, position.Y + dy);
		}
		Add( position );
		return stack;

		void Add( (int X, int Y) cell )
		{
			if ( stack.Count < CornerStackCapacity )
				stack.Add( cell );
		}
	}

	/// <summary>
	/// The corner walk's other effect: a walk that ends on a path cell takes the link between the queue's back cell and that path cell off both of them (the step
	/// bit on the back cell, its reverse on the path cell), so a queue entered for editing stays unjoined from the path until a line ends on a path cell again
	/// (the mode-3 link writer, code 8). Returns true when a link was cleared; the queue is recomputed and the edit reported, as the tool's entry does after the walk.
	/// </summary>
	// [BIN:TPW-EXE:0x00530379 corner walk link clear] at the path cell the walk (0x530300) takes the step back to the cell before it (0x530338..0x530341) and, when that cell is not the entrance (type 9, 0x536340 at 0x530366), clears the step bit on it (0x522730 at 0x530380) and refreshes its tile (0x5365D0); it then clears the reverse bit (0x4D8C00) on the path cell and refreshes it (0x53039C..0x5303A3) and ends with the queue's back cell as its last point; nothing restores the link on a cancel: the mode-3 undo (0x52FE50) and the tool's exit (0x52F200, 0x52F580) only pop points and reset the tool state, and the relinking pass 0x52FC80 runs for the track tool modes 0xB, 0x10 and 0x35 only; the walk's return value (0x81B14C, 0x48D00E) is never read
	public static bool DetachFromPath( GuestPathGrid grid, RideVisitorBridge ride )
	{
		ride.RecomputeQueue( grid );
		if ( ride.QueueFrontCell is not { } front || !grid.IsQueue( front.X, front.Y ) )
			return false;
		WalkCorners( grid, ride, out var end );
		if ( end is not { } found )
			return false;
		var cells = grid.Cells;
		cells.SetLinks( found.Back.X, found.Back.Y, (byte)(cells.LinksAt( found.Back.X, found.Back.Y ) & ~found.Step) );
		cells.SetLinks( found.Path.X, found.Path.Y, (byte)(cells.LinksAt( found.Path.X, found.Path.Y ) & ~PathLinkWriter.Opposite( found.Step )) );
		grid.Invalidate();
		ride.RecomputeQueue( grid );
		ride.NotifyQueueEdited( grid );
		return true;
	}

	/// <summary>The link bit opposite <paramref name="link"/> (1 and 16, 4 and 64: <c>0x4D8C00</c>); 0 stays 0.</summary>
	private static byte ReverseLink( byte link ) => link == 0 ? (byte)0 : link < 0x10 ? (byte)(link << 4) : (byte)(link >> 4);

	/// <summary>A cell with exactly two cardinal links that are not opposite (<c>0x53AE00</c>).</summary>
	private static bool IsCornerLinks( byte links ) =>
		(((links & 1) != 0 ? 1 : 0) + ((links & 4) != 0 ? 1 : 0) + ((links & 0x10) != 0 ? 1 : 0) + ((links & 0x40) != 0 ? 1 : 0)) == 2
		&& (links & (1 | 0x10)) != 0 && (links & (4 | 0x40)) != 0;

	/// <summary>
	/// The segments of the queue the tool's undo takes back, one per point of <paramref name="stack"/> after the first: the queue cells after the
	/// previous point up to this one (empty when the two points are the same cell, a pop that changes nothing).
	/// </summary>
	public static IReadOnlyList<SegmentResult> StackSegments( RideVisitorBridge ride, IReadOnlyList<(int X, int Y)> stack )
	{
		var cells = ride.QueueCells;
		var segments = new List<SegmentResult>();
		var index = 0;
		for ( var point = 1; point < stack.Count; point++ )
		{
			var built = new List<(int X, int Y)>();
			var next = stack[point] == stack[point - 1] ? index : IndexOfCell( cells, stack[point], index + 1 );
			for ( var cell = index + 1; next > index && cell <= next; cell++ )
				built.Add( cells[cell] );
			if ( next >= 0 )
				index = next;
			segments.Add( new SegmentResult( stack[point], built, 0, CellBuildResult.Ok, false )
			{
				Start = stack[point - 1],
				Line = ParkPathBuilder.LayLine( stack[point - 1], stack[point] )
			} );
		}
		return segments;
	}

	private static int IndexOfCell( IReadOnlyList<(int X, int Y)> cells, (int X, int Y) cell, int from )
	{
		for ( var index = from; index < cells.Count; index++ )
		{
			if ( cells[index] == cell )
				return index;
		}
		return -1;
	}

	/// <summary>
	/// Takes every cell of <paramref name="ride"/>'s queue off the map, as a ride's demolition undoes every point of the corner stack under the forced clear
	/// (<c>0x527EE0</c>): the NoModify front cell goes too, and the links of each cell are cleared on both sides, a path joined at the back included.
	/// Returns the cells taken. Money and guests are the caller's.
	/// </summary>
	// [BIN:TPW-EXE:0x00527F9A ride demolition queue] the demolition fills the corner stack (0x530120), sets the forced-clear flag 0x81D7A8 (0x527FAA) and calls Back 0x52FE50 until it returns 0 (0x527FBE..0x527FDA); each undo clears the line from the popped point to the new last point through ClearCell 0x5367A0, which under the flag does not stop at a NoModify cell (0x5367D4) or a placement counter and unlinks the four cardinal bits of the cell on both sides in its queue case
	public static IReadOnlyList<(int X, int Y)> ClearAll( GuestPathGrid grid, RideVisitorBridge ride, Action? afterPop = null )
	{
		ride.RecomputeQueue( grid );
		if ( !HasQueueCells( grid, ride ) )
			return Array.Empty<(int X, int Y)>();
		var removed = ride.QueueCells.Where( cell => grid.IsQueue( cell.X, cell.Y ) ).ToArray();
		// the walk the demolition makes first (0x530120) fills the stack and writes the counters
		WriteWalkCounts( grid, ride );
		var stack = CornerStack( grid, ride );
		// Back (0x52FE50) pops every point but the first: the line from the popped point to the new last one is cleared, forced (the NoModify front goes too), then 0x4DE1F0 runs on the ride (0x52FFEC)
		for ( var point = stack.Count - 1; point >= 1; point-- )
		{
			foreach ( var cell in ParkPathBuilder.LayLine( stack[point], stack[point - 1] ) )
			{
				if ( !grid.IsQueue( cell.X, cell.Y ) )
					continue;
				grid.ClearQueue( cell.X, cell.Y );
				grid.Cells.SetFlags( cell.X, cell.Y, grid.Cells.FlagsAt( cell.X, cell.Y ) & ~ParkCellFlags.NoModify );
			}
			ride.RecomputeQueue( grid );
			ride.NotifyQueueEdited( grid );
			afterPop?.Invoke();
		}
		// a cell the walk did not reach (a stray branch of the owner's cells) goes with the rest
		foreach ( var cell in removed )
		{
			if ( !grid.IsQueue( cell.X, cell.Y ) )
				continue;
			grid.ClearQueue( cell.X, cell.Y );
			grid.Cells.SetFlags( cell.X, cell.Y, grid.Cells.FlagsAt( cell.X, cell.Y ) & ~ParkCellFlags.NoModify );
		}
		return removed;
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

	/// <summary>
	/// Removes queue cell (x, y) of <paramref name="ride"/> and every cell behind it; returns the number removed. A free-standing cell (one of the ride's by its owner word, in
	/// no chain) is removed alone.
	/// </summary>
	/// <param name="removeFixed">Whether a NoModify cell (the queue front the placement laid) may go too; the original's cell clear keeps it unless forced (the level opts in, EXT:removable-access-cells).</param>
	// [BIN:TPW-EXE:0x005367A0 cell clear] the original removes a queue cell only through its cell clear: the queue tool's undo (Back 0x52FE50, whole segments of the corner stack, CellBuildTool.Undo) and the ride's demolition (QueuePaths.ClearAll) reach it, the bulldozer's rectangle clears only path cells (type 1 test 0x536310 at 0x52C116 before 0x5367A0); this removal from a clicked cell on, with the cells behind it so the queue stays one line from the ride, serves the undo of a segment and the labelled extensions EXT:dev-one-click-remove and EXT:queue-backtrack
	// [APPROX:QUEUE-015] the queue tool's undo (Back 0x52FE50) takes cells and refunds as 0x5367A0 does and writes no guest: the re-evaluation that follows (0x4DE1F0 at 0x52FFEC, 0x501390 per guest) dismisses by place alone; the corner walk's counters (+0x20: 1 on a corner, 0 elsewhere, 0x5302C2) are written by the queue edit click, whose tool takes the segments back through the native undo (the cells go from the segment's end, so a corner keeps its counter 1 - 1 and stays); a ride whose front holds no queue cell is demolished with one cell's debit (0x527FB4). The tool entries that run no walk here (a ride's placement, the ride window's button, a track's or coaster's completion) take the chain from the segment's first cell on without counters, and the removal from a clicked cell on stays only as the labelled extensions EXT:dev-one-click-remove and EXT:queue-backtrack, which keep the original's guest policy (the clear writes no guest and the re-evaluation dismisses by place)
	public static int RemoveFrom( GuestPathGrid grid, RideVisitorBridge ride, int x, int y, bool removeFixed = false )
	{
		ride.RecomputeQueue( grid );
		var cells = ride.QueueCells;
		var index = -1;
		for ( var i = 0; i < cells.Count; i++ )
		{
			if ( cells[i] == (x, y) )
				index = i;
		}
		if ( !grid.IsQueue( x, y ) )
			return 0;
		// [BIN:TPW-EXE:0x005367D4 NoModify keeps the cell] the placement lays the cell outside the entrance with the flag 0x20, and the cell clear leaves such a cell alone unless it is forced (0x5367D4..0x5367E0)
		if ( grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.NoModify ) && !removeFixed )
			return 0;
		// a free-standing cell is the ride's by its owner word alone
		if ( index < 0 && (ride.OwnerWord == 0 || grid.Cells.OwnerAt( x, y ) != ride.OwnerWord) )
			return 0;
		var removed = index < 0 ? new[] { (X: x, Y: y) } : cells.Skip( index ).ToArray();
		foreach ( var cell in removed )
		{
			grid.ClearQueue( cell.X, cell.Y );
			grid.Cells.SetFlags( cell.X, cell.Y, grid.Cells.FlagsAt( cell.X, cell.Y ) & ~ParkCellFlags.NoModify );
		}
		ride.RecomputeQueue( grid );
		ride.NotifyQueueEdited( grid );
		return removed.Length;
	}
}
