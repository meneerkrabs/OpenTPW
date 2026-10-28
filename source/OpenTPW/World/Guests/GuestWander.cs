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
	/// The links of <paramref name="cell"/> the original keeps as candidates of a step (<c>0x004F9490</c>): every link bit of the cell (the four cardinal ones, to a cell of any type) except the
	/// four removals, in this order: for a staff member the links to cells outside his patrol rectangle (<paramref name="inArea"/>), on a path cell those toward queue cells (types 3 and 9), on a queue
	/// cell (type 3) the direction of its own queue link, and on any cell those toward type-10 (exit) cells. A step whose count is 0 strands the walker.
	/// </summary>
	// [BIN:TPW-EXE:0x004F95F7 candidate links] the cell's link byte (0x00522770, & 0x55) names the candidates in the order 16, 4, 1, 64 (+Y, +X, −Y, −X) whatever the type of the cell it leads to, and 0x00522810 counts them
	// [BIN:TPW-EXE:0x004F96FB removal 1] a staff thing (kinds 4 to 8) with the area test on (the flag [esp+0x30] clear) drops each candidate whose cell fails 0x00506ED0 (the member's rectangle); the last one dropped ends the step at 0x004F9D60
	// [BIN:TPW-EXE:0x004F9733 removal 2] a type-1 cell (0x00536310) drops the candidates whose cell is a queue cell (0x00536320: type 3 or 9)
	// [BIN:TPW-EXE:0x004F977B removal 3] a queue cell (0x00536320) that is not an entrance (0x00536340: type 9) drops the candidate in the direction of its own queue link (0x00522850, the byte +0x0D: 1, 0x10, 4 or 0x40)
	// [BIN:TPW-EXE:0x004F97E4 removal 4] every cell drops the candidates whose cell is of type 10 (0x00536350); fewer than two left take the first in order with no draw, none left is 0x004F9D64
	public static int OriginalCandidates( GuestPathGrid grid, (int X, int Y) cell, Func<int, int, bool>? inArea = null )
	{
		Span<int> candidates = stackalloc int[4];
		return Candidates( grid, cell, inArea, candidates );
	}

	/// <summary>
	/// Fills <paramref name="candidates"/> (by <see cref="CandidateOrder"/> position: the direction, or −1 for a removed or absent link) and returns the original's own counter of them
	/// (<c>ebp</c>): the link count less one for every removal, which for the own queue link is taken whether or not that direction is among the cell's links (0x004F97E3), so the counter
	/// can be one lower than the candidates left; it decides whether a draw picks the step.
	/// </summary>
	private static int Candidates( GuestPathGrid grid, (int X, int Y) cell, Func<int, int, bool>? inArea, Span<int> candidates )
	{
		var links = (byte)(grid.LinksAt( cell.X, cell.Y ) & AllLinks);
		var type = grid.Cells.RawTypeAt( cell.X, cell.Y );
		var pathCell = grid.IsWalkable( cell.X, cell.Y );
		// removal 3: a queue cell that is no entrance loses the way of its own queue link
		var ownQueueLink = type == (byte)ParkCellType.Queue ? grid.Cells.QueueLinkAt( cell.X, cell.Y ) : (byte)0;
		var counter = System.Numerics.BitOperations.PopCount( (uint)links );
		for ( var index = 0; index < 4; index++ )
		{
			var direction = CandidateOrder[index];
			candidates[index] = -1;
			var bit = GuestPathGrid.LinkValue( direction );
			if ( (links & bit) == 0 )
				continue;
			var (dx, dy) = GuestPathGrid.Directions[direction];
			var (x, y) = (cell.X + dx, cell.Y + dy);
			if ( !grid.InBounds( x, y ) || !BridgeEdgeAllowed( grid, cell, direction ) )
			{
				counter--;
				continue;
			}
			var target = grid.Cells.RawTypeAt( x, y );
			if ( inArea != null && !inArea( x, y ) )
			{
				counter--;
				continue;
			}
			if ( pathCell && target is (byte)ParkCellType.Queue or ParkCellTypes.Entrance )
			{
				counter--;
				continue;
			}
			if ( bit == ownQueueLink )
				continue;
			if ( target == ParkCellTypes.Exit )
			{
				counter--;
				continue;
			}
			candidates[index] = direction;
		}
		// the own queue link's removal takes its direction out of the counter even when no link of the cell leads that way (0x004F9795..0x004F97E3: the dec at 0x004F97E3 follows each of the four compares, not a test of the slot)
		if ( ownQueueLink is 1 or 4 or 16 or 64 )
			counter--;
		return counter;
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
	/// count, a start index for every step with two or more candidates, then the two sub-cell bytes. A start cell without links gives null (the caller's off-path search,
	/// <see cref="OffPath"/>), as does a step with no candidate left (<paramref name="stranded"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x004F9490 wander destination] rand % 5 at entry (0x004F9534); a do-while over the steps whose counter becomes 1 again when a step ends on the start cell with the counter at 0 (0x004F98F5); per step the current cell's links (0x00522770, & 0x55) in the order 16, 4, 1, 64, the count from 0x00522810, whatever the cell's type; fewer than two left after the four removals: the first in order (no draw); else the first from rand & 3 (0x004F9874) that is neither empty nor the way back ((i − 2) & 3 of the last step, 9999 before the first); then two draws & 0x7F, each clamped to 5..123 (0x004F9922)
	public static Destination? Pick( GuestPathGrid grid, (int X, int Y) start, Func<uint> draw, Func<int, int, bool>? inArea = null ) =>
		Pick( grid, start, draw, inArea, out _ );

	/// <summary>
	/// <see cref="Pick(GuestPathGrid, (int X, int Y), Func{uint}, Func{int, int, bool}?)"/>; <paramref name="stranded"/> is true when a step had no candidate left after the four removals (the original's
	/// 0x004F9D64: a guest is marked stranded, a staff member looks for a cell inside his rectangle instead).
	/// </summary>
	public static Destination? Pick( GuestPathGrid grid, (int X, int Y) start, Func<uint> draw, Func<int, int, bool>? inArea, out bool stranded ) =>
		Pick( grid, start, draw, inArea, out stranded, default, out _ );

	/// <summary>The most cells of a walk <see cref="Pick(GuestPathGrid, (int X, int Y), Func{uint}, Func{int, int, bool}?, out bool, Span{ValueTuple{int, int}}, out int)"/> reports; a longer walk reports its last ones.</summary>
	public const int TrailCapacity = 8;

	/// <summary>
	/// <see cref="Pick(GuestPathGrid, (int X, int Y), Func{uint}, Func{int, int, bool}?, out bool)"/> that also reports the cells the random walk stood on after each step, in order, the destination cell last
	/// (<paramref name="trail"/> receives at most its length of them; a walk longer than that reports its last ones). The walk, its draws and its result are the original's, whether the caller wants the trail or not:
	/// the trail is only a copy of cells the search already visited (docs/SMARTER-GUESTS.md, phase 6). <paramref name="trailCount"/> is 0 for a start without links, which has no walk.
	/// </summary>
	public static Destination? Pick( GuestPathGrid grid, (int X, int Y) start, Func<uint> draw, Func<int, int, bool>? inArea, out bool stranded, Span<(int X, int Y)> trail, out int trailCount )
	{
		stranded = false;
		trailCount = 0;
		var remaining = draw() % 5;
		// [BIN:TPW-EXE:0x004F95B9 start without links] the link count of the start cell (0x00522810) below 1 takes the off-path search (0x004F9A05), whatever the cell's type
		if ( (grid.LinksAt( start.X, start.Y ) & AllLinks) == 0 )
			return null;
		var cell = start;
		var back = -1;
		Span<int> candidates = stackalloc int[4];
		while ( true )
		{
			// [BIN:TPW-EXE:0x004F9490 candidates of a step] the link walk starts from any cell that has links (a queue cell, an entrance or an exit included: a guest the queue let go stands on its queue cell and wanders from there, QUEUE-010) and takes every link the four removals leave
			var count = Candidates( grid, cell, inArea, candidates );
			int chosen;
			if ( count < 2 )
			{
				// Fewer than two: the first in order, even the way back. None: stranded, as a path cell whose links lead only to queue cells.
				chosen = candidates.IndexOfAnyExcept( -1 );
				if ( chosen < 0 )
				{
					stranded = true;
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
