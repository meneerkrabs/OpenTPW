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
	// [APPROX:QUEUE-014] the queue tool lays straight segments along the dominant axis from the ride's back cell (CellBuildTool, QueueLineWriter), ending on a path cell to connect the queue (code 8 on the last cell), as tp.exe's does (0x536100, ported); tp.exe starts a segment at a cell the player clicks (SetMode 0x52F200 clears the start) and lays anywhere, where the tool starts at the front or back cell; each cell is validated as 0x535670 does (an empty cell is accepted, a path cell is replaced unless NoModify and not on a line's last cell, an object cell of a lower overwrite priority than the queue tool's 3 is demolished and covered by the commit's op 0x87, another ride's queue cell is refused by the owner word +0x10), then typed (0x5346D0), linked, given its queue link toward the previous cell (pass 0x82) and the tool's ride as owner word (pass 0x83, ported); a cell that touches no queue is laid free-standing (no queue link, the ride's owner word only) because the validator never asks a cell to touch the back cell (the remove tool takes it, the ride's chain ignores it); OpenTPW lays no queue over a path cell — evidence needed: the segment commit's first-click handling in 0x524960 (UI-031), the rest of the commit 0x532FC0 and the refund of the cell clear 0x5367A0, which reads the owner word
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
			LinkFront( grid.Cells, x, y, ride.QueueEntranceDirection );
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
		LinkFront( grid.Cells, cells[0].X, cells[0].Y, ride.QueueEntranceDirection );
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

	/// <summary>Where a corner walk ended on a path cell: the queue's back cell, the path cell and the step (link bit) from the one to the other.</summary>
	private readonly record struct PathEnd( (int X, int Y) Back, (int X, int Y) Path, byte Step );

	private static IReadOnlyList<(int X, int Y)> WalkCorners( GuestPathGrid grid, RideVisitorBridge ride, out PathEnd? pathEnd )
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
	public static IReadOnlyList<(int X, int Y)> ClearAll( GuestPathGrid grid, RideVisitorBridge ride )
	{
		ride.RecomputeQueue( grid );
		if ( !HasQueueCells( grid, ride ) )
			return Array.Empty<(int X, int Y)>();
		var removed = ride.QueueCells.ToArray();
		foreach ( var cell in removed )
		{
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
	// [APPROX:QUEUE-015] the placement counters of the queue cells (+0x20, which the walk sets to 1 on a corner and 0 elsewhere and the clear counts down) are not kept, as the undo takes the cells of the segment outright; the queued guests standing on the cells an undo or a removal takes are set down on the nearest path and dismissed from the queue (GuestSimulation.SetDownOffQueueCells, the exit of the queue-edited re-evaluation 0x501390), where the original's clear writes no guest and the re-evaluation dismisses by place alone (0x501390: place at least four per cell, never a guest in state 14, never the called guest, which 0x4DE1F0 skips): the guests the removal leaves on a path cell keep their places and walk on (a called guest to the stand point, which falls back to the path cell the queue now starts on, GuestSimulation.StandCellOf), as the original has no removal of a single cell to read; the corner stack, the corner walk's clearing of the link between the back cell and the path (QueuePaths.DetachFromPath), the undo by whole segments (Back 0x52fe50) and the demolition's forced clears (0x527ee0) follow the original, and the removal of a queue from a clicked cell on is kept only as the labelled extensions EXT:dev-one-click-remove and EXT:queue-backtrack
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
