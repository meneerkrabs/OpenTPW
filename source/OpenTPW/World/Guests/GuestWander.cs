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
	/// Picks the destination from <paramref name="start"/>, drawing from <paramref name="draw"/> in the original's order: the step
	/// count, a start index for every step with two or more candidates, then the two sub-cell bytes. A path cell without links gives
	/// its own centre; null when the start is no path cell or its links lead to no path cell (stranded).
	/// </summary>
	// [BIN:TPW-EXE:0x004F9490 wander destination] rand % 5 at entry (0x004F9534); a do-while over the steps whose counter becomes 1 again when a step ends on the start cell with the counter at 0 (0x004F98F5); per step the current cell's links (0x00522770, & 0x55) in the order 16, 4, 1, 64, the count from 0x00522810; fewer than two left: the first in order (no draw); else the first from rand & 3 (0x004F9874) that is neither empty nor the way back ((i − 2) & 3 of the last step, 9999 before the first); then two draws & 0x7F, each clamped to 5..123 (0x004F9922)
	public static Destination? Pick( GuestPathGrid grid, (int X, int Y) start, Func<uint> draw, Func<int, int, bool>? inArea = null )
	{
		var remaining = draw() % 5;
		// [APPROX:GUEST-010] off the linked paths the original searches the eight directions at distances 0..3 for a path cell a route reaches (centre point, 0x80), then five random cells within ±5; the search meets the own cell first, so a path cell without links gives its own centre, which is all OpenTPW models: a guest on a cell that is no path cell finds no destination; a stranded guest is not marked (+0x198, animation 0x11), so it draws again on its next try — evidence needed: none (read); OpenTPW's routes start on path cells
		if ( !grid.IsWalkable( start.X, start.Y ) )
			return null;
		if ( (grid.Cells.LinksAt( start.X, start.Y ) & AllLinks) == 0 )
			return new Destination( start, CentreByte, CentreByte );
		var cell = start;
		var back = -1;
		Span<int> candidates = stackalloc int[4];
		while ( true )
		{
			var count = 0;
			var links = grid.Cells.LinksAt( cell.X, cell.Y );
			for ( var index = 0; index < 4; index++ )
			{
				var direction = CandidateOrder[index];
				var (dx, dy) = GuestPathGrid.Directions[direction];
				// [APPROX:GUEST-010] a candidate is a link of the current cell to a path cell; the original keeps every link except to queue cells (removed on a path cell, 0x00536310 / 0x00536320), its own queue link's direction on a queue cell (0x00522850), cell type 10 (0x00536350) and, for staff, cells outside the patrol rectangle (0x00506ED0); OpenTPW's maps link path cells only to path cells (ParkPathBuilder, saves), and it has no type 10 cells and no wandering staff yet (ECON-022) — evidence needed: none for guests on path cells
				// staff keep to their patrol rectangle (0x00506ED0 removes the candidates outside it)
				candidates[index] = (links & GuestPathGrid.LinkValue( direction )) != 0 && grid.IsWalkable( cell.X + dx, cell.Y + dy )
					&& (inArea == null || inArea( cell.X + dx, cell.Y + dy )) ? direction : -1;
				count += candidates[index] >= 0 ? 1 : 0;
			}
			int chosen;
			if ( count < 2 )
			{
				// Fewer than two: the first in order, even the way back. None: stranded, as a path cell whose links lead only to queue cells.
				chosen = candidates.IndexOfAnyExcept( -1 );
				if ( chosen < 0 )
					return null;
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
