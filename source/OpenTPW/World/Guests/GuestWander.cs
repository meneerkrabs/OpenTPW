namespace OpenTPW;

/// <summary>
/// The wander destination (<c>0x004F9490</c>, docs/reverse/WANDER-plan.md §3): from a path cell the guest walks
/// <c>rand % 5 + 1</c> random steps over the cell links (one more each time the last step comes back to the start), and the
/// destination is the last cell at a random point in its first quarter (sub-cell bytes <c>rand &amp; 0x7F</c>, clamped to 5..123 of 256).
/// The steps only pick the destination; the guest then walks the shortest route there.
/// </summary>
public static class GuestWander
{
	/// <summary>The candidate order of a step, as <see cref="GuestPathGrid.Directions"/> indices: +Y (link 16), +X (4), −Y (1), −X (64).</summary>
	public static ReadOnlySpan<int> CandidateOrder => [2, 1, 0, 3];

	/// <summary>The smallest and largest sub-cell byte of a destination.</summary>
	public const int MinimumByte = 5;
	public const int MaximumByte = 123;
	/// <summary>The sub-cell byte of a cell's centre (0x80).</summary>
	public const int CentreByte = 0x80;
	/// <summary>The four link bits of a cell (1, 4, 16, 64); <c>0x00522810</c> counts them to tell a linked path cell.</summary>
	private const byte AllLinks = 0x55;

	/// <summary>A picked destination: the final cell and the sub-cell bytes (1/256 of a cell) of the point in it.</summary>
	public readonly record struct Destination( (int X, int Y) Cell, int ByteX, int ByteY );

	/// <summary>
	/// The links of <paramref name="cell"/> the original keeps as candidates of a step (<c>0x004F9490</c>): every cardinal link except, on a path cell, those toward
	/// queue cells (types 3 and 9), on a queue cell (not the ride's entrance cell) its own queue link's direction, and on any cell those toward type-10 cells. A guest
	/// whose count is 0 is stranded.
	/// </summary>
	// [BIN:TPW-EXE:0x004F97E4 candidate removals] a type-1 cell loses the links to queue cells (0x00536310, 0x00536320); a queue cell (0x00536320 and not 0x00536340) loses the direction of its own queue link (0x00522850); every cell loses the links to type-10 cells (0x00536350); a count below 2 takes the first one left, none left is stranded
	public static int OriginalCandidates( GuestPathGrid grid, (int X, int Y) cell )
	{
		var links = (byte)(grid.LinksAt( cell.X, cell.Y ) & AllLinks);
		var type = grid.Cells.RawTypeAt( cell.X, cell.Y );
		var ownQueueLink = type == (byte)ParkCellType.Queue ? grid.Cells.QueueLinkAt( cell.X, cell.Y ) : (byte)0;
		var count = 0;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var bit = GuestPathGrid.LinkValue( direction );
			if ( (links & bit) == 0 || bit == ownQueueLink )
				continue;
			var (dx, dy) = GuestPathGrid.Directions[direction];
			if ( !BridgeEdgeAllowed( grid, cell, direction ) )
				continue;
			var target = grid.Cells.RawTypeAt( cell.X + dx, cell.Y + dy );
			if ( target == 10 || (grid.IsWalkable( cell.X, cell.Y ) && target is (byte)ParkCellType.Queue or 9) )
				continue;
			count++;
		}
		return count;
	}

	/// <summary>Keep original candidate behavior away from bridges, including asymmetric ground links.</summary>
	private static bool BridgeEdgeAllowed( GuestPathGrid grid, (int X, int Y) cell, int direction )
	{
		if ( grid.Cells.PoolBridges is not { } bridges )
			return true;
		var (dx, dy) = GuestPathGrid.Directions[direction];
		return !bridges.Covers( cell.X, cell.Y ) && !bridges.Covers( cell.X + dx, cell.Y + dy )
			|| bridges.Connects( cell.X, cell.Y, direction );
	}

	/// <summary>
	/// The destination search of a start cell without links (<c>0x004F9490</c>, <c>0x522810</c> below 1): for each of eight direction patterns at the distances 0 to 3 the
	/// first path cell (type 1) a route reaches, then up to five random cells within five of the start that a route reaches. <paramref name="route"/> sets the walk to a
	/// cell's centre and tells whether the search found one. The wander's first draw (the step count) has been taken by the caller.
	/// </summary>
	// [BIN:TPW-EXE:0x004F99F8 off-path candidates] 8 patterns (0, 0), (t, t), (t, 0), (t, −t), (0, −t), (−t, −t), (−t, 0), (−t, t) for t = 0..3, outer loop the pattern, inner the distance; the cell id is the start id plus (dy << 7) + dx in 16 bits, its cell is (id − 1) & 0x7F, (id − 1) >> 7; kept when inside 0..127 (0x004D8300) and of type 1 (0x00536310), then a SetDest (0x00510100) to the cell centre (0x80, 0x80): the first that finds a route wins
	// [BIN:TPW-EXE:0x004F9BF2 random candidates] five tries of two draws each, x + (R1 % 11 − 5) and y + (R2 % 11 − 5), kept when inside 0..127 (no type test) and routed to the cell centre; none found returns 0 without a stranded mark
	public static bool OffPath( GuestPathGrid grid, (int X, int Y) start, Func<uint> draw, Func<(int X, int Y), bool> route )
	{
		var startId = (ushort)(start.Y * 128 + start.X + 1);
		for ( var pattern = 0; pattern < 8; pattern++ )
		{
			for ( var t = 0; t < 4; t++ )
			{
				var (dx, dy) = pattern switch
				{
					0 => (0, 0),
					1 => (t, t),
					2 => (t, 0),
					3 => (t, -t),
					4 => (0, -t),
					5 => (-t, -t),
					6 => (-t, 0),
					_ => (-t, t)
				};
				var id = (ushort)((dy << 7) + dx + startId);
				var offset = (uint)id - 1;
				var cell = ((int)(offset & 0x7F), (int)(offset >> 7));
				if ( cell.Item1 < 128 && cell.Item2 < 128 && grid.InBounds( cell.Item1, cell.Item2 ) && grid.IsWalkable( cell.Item1, cell.Item2 ) && route( cell ) )
					return true;
			}
		}
		for ( var attempt = 0; attempt < 5; attempt++ )
		{
			var first = draw();
			var second = draw();
			var x = (int)(first % 11) - 5 + start.X;
			var y = (int)(second % 11) - 5 + start.Y;
			// [EXT:pools-cell-type] the draws stay as they are; a candidate in the water is skipped (docs/POOLS.md)
			if ( x >= 0 && y >= 0 && x < 128 && y < 128 && grid.InBounds( x, y ) && (grid.Cells.RawTypeAt( x, y ) != ParkCellTypes.PoolWater || grid.IsWalkable( x, y )) && route( (x, y) ) )
				return true;
		}
		return false;
	}

	/// <summary>
	/// Picks the destination from <paramref name="start"/>, drawing from <paramref name="draw"/> in the original's order: the step
	/// count, a start index for every step with two or more candidates, then the two sub-cell bytes. A path cell without links gives
	/// its own centre; linked entrance/exit cells can walk out to paths. Other nonpath starts or a step with no supported candidates give null.
	/// </summary>
	// [BIN:TPW-EXE:0x004F9490 wander destination] rand % 5 at entry (0x004F9534); a do-while over the steps whose counter becomes 1 again when a step ends on the start cell with the counter at 0 (0x004F98F5); per step the current cell's links (0x00522770, & 0x55) in the order 16, 4, 1, 64, the count from 0x00522810; fewer than two left: the first in order (no draw); else the first from rand & 3 (0x004F9874) that is neither empty nor the way back ((i − 2) & 3 of the last step, 9999 before the first); then two draws & 0x7F, each clamped to 5..123 (0x004F9922)
	public static Destination? Pick( GuestPathGrid grid, (int X, int Y) start, Func<uint> draw, Func<int, int, bool>? inArea = null ) =>
		Pick( grid, start, draw, inArea, out _ );

	/// <summary>
	/// <see cref="Pick(GuestPathGrid, (int X, int Y), Func{uint}, Func{int, int, bool}?)"/>; <paramref name="areaBlocked"/> is true when a step had
	/// candidates but <paramref name="inArea"/> removed them all (a staff member at its patrol rectangle's edge; the original then looks for a cell inside it).
	/// </summary>
	public static Destination? Pick( GuestPathGrid grid, (int X, int Y) start, Func<uint> draw, Func<int, int, bool>? inArea, out bool areaBlocked ) =>
		Pick( grid, start, draw, inArea, out areaBlocked, default, out _ );

	/// <summary>The most cells of a walk <see cref="Pick(GuestPathGrid, (int X, int Y), Func{uint}, Func{int, int, bool}?, out bool, Span{ValueTuple{int, int}}, out int)"/> reports; a longer walk reports its last ones.</summary>
	public const int TrailCapacity = 8;

	/// <summary>
	/// <see cref="Pick(GuestPathGrid, (int X, int Y), Func{uint}, Func{int, int, bool}?, out bool)"/> that also reports the cells the random walk stood on after each step, in order, the destination cell last
	/// (<paramref name="trail"/> receives at most its length of them; a walk longer than that reports its last ones). The walk, its draws and its result are the original's, whether the caller wants the trail or not:
	/// the trail is only a copy of cells the search already visited (docs/SMARTER-GUESTS.md, phase 6). <paramref name="trailCount"/> is 0 for a start without links, which has no walk.
	/// </summary>
	public static Destination? Pick( GuestPathGrid grid, (int X, int Y) start, Func<uint> draw, Func<int, int, bool>? inArea, out bool areaBlocked, Span<(int X, int Y)> trail, out int trailCount )
	{
		areaBlocked = false;
		trailCount = 0;
		var remaining = draw() % 5;
		// [APPROX:GUEST-010] linked entrance/exit starts use the existing path-candidate walk; other nonpath start types and generalized candidate types remain unsupported (the original walks links of any cell type, 0x4F9490). GuestNavigation can route from access cells; the path-grid route is only bookkeeping.
		// [BIN:TPW-EXE:0x004F9490 queue start] the link walk starts from any cell that has links, a queue cell (type 3) included: a guest the queue let go stands on its queue cell and wanders from there (QUEUE-010)
		var linkedAccess = grid.Cells.RawTypeAt( start.X, start.Y ) is ParkCellTypes.Entrance or ParkCellTypes.Exit
			&& (grid.LinksAt( start.X, start.Y ) & AllLinks) != 0;
		if ( !grid.IsWalkable( start.X, start.Y ) && !grid.IsQueue( start.X, start.Y ) && !linkedAccess )
			return null;
		if ( (grid.LinksAt( start.X, start.Y ) & AllLinks) == 0 )
			return new Destination( start, CentreByte, CentreByte );
		var cell = start;
		var back = -1;
		Span<int> candidates = stackalloc int[4];
		while ( true )
		{
			var count = 0;
			var outside = 0;
			var links = grid.LinksAt( cell.X, cell.Y );
			// [BIN:TPW-EXE:0x004F9490 candidates of a queue cell] on a queue cell (type 3, not the ride's entrance cell 9) the direction of its own queue link (0x00522850, toward the front) is removed (0x004F9718..0x004F97E0); a path cell removes its links to queue cells (0x00536320), a queue cell keeps them
			var onQueue = grid.IsQueue( cell.X, cell.Y );
			var queueLink = onQueue ? grid.Cells.QueueLinkAt( cell.X, cell.Y ) : 0;
			for ( var index = 0; index < 4; index++ )
			{
				var direction = CandidateOrder[index];
				var (dx, dy) = GuestPathGrid.Directions[direction];
				// [APPROX:GUEST-010] a candidate is a link of the current cell to a path cell; the original keeps every link except to queue cells (removed on a path cell, 0x00536310 / 0x00536320), its own queue link's direction on a queue cell (0x00522850), cell type 10 (0x00536350) and, for staff, cells outside the patrol rectangle (0x00506ED0); this bounded adapter retains path targets (and queue targets from queue starts), including for linked access starts; generalized target types remain gated
				// staff keep to their patrol rectangle (0x00506ED0 removes the candidates outside it)
				var linked = (links & GuestPathGrid.LinkValue( direction )) != 0 && (grid.IsWalkable( cell.X + dx, cell.Y + dy ) || onQueue && grid.IsQueue( cell.X + dx, cell.Y + dy ))
					&& !(onQueue && queueLink == GuestPathGrid.LinkValue( direction ))
					&& BridgeEdgeAllowed( grid, cell, direction );
				candidates[index] = linked && (inArea == null || inArea( cell.X + dx, cell.Y + dy )) ? direction : -1;
				count += candidates[index] >= 0 ? 1 : 0;
				outside += linked && candidates[index] < 0 ? 1 : 0;
			}
			int chosen;
			if ( count < 2 )
			{
				// Fewer than two: the first in order, even the way back. None: stranded, as a path cell whose links lead only to queue cells.
				chosen = candidates.IndexOfAnyExcept( -1 );
				if ( chosen < 0 )
				{
					areaBlocked = outside > 0;
					return null;
				}
			}
			else
			{
				var first = (int)(draw() & 3);
				chosen = first;
				for ( var offset = 0; offset < 4; offset++ )
				{
					chosen = (first + offset) & 3;
					if ( candidates[chosen] >= 0 && chosen != back )
						break;
				}
			}
			var (stepX, stepY) = GuestPathGrid.Directions[candidates[chosen]];
			cell = (cell.X + stepX, cell.Y + stepY);
			if ( !trail.IsEmpty )
			{
				if ( trailCount == trail.Length )
				{
					trail[1..].CopyTo( trail );
					trailCount--;
				}
				trail[trailCount++] = cell;
			}
			back = (chosen + 2) & 3;
			if ( cell == start && remaining == 0 )
				remaining = 1;
			if ( remaining-- == 0 )
				break;
		}
		var byteX = Math.Clamp( (int)(draw() & 0x7F), MinimumByte, MaximumByte );
		var byteY = Math.Clamp( (int)(draw() & 0x7F), MinimumByte, MaximumByte );
		return new Destination( cell, byteX, byteY );
	}
}
