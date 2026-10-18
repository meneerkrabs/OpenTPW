namespace OpenTPW;

/// <summary>
/// The litter-aware route of the smarter guests (docs/SMARTER-GUESTS.md, phase 6b). The original's route search (<see cref="WalkSearch"/>, tp.exe <c>0x511420</c>) is a line-and-boundary tracer, not a
/// weighted shortest-path search, so a cost per cell cannot be added to it without replacing it. The host therefore runs the original search first, untouched, and then, only when the guest's next
/// waypoints cross litter, replaces such a segment by the cheapest four-connected walk over path cells whose cost is its length plus the policy's cost for each littered cell on it, bounded to a few
/// cells longer than the segment. A route over clean cells, and every route of a park without litter, is the original's, byte for byte.
/// </summary>
// [EXT:smarter-guests-litter] OpenTPW's own refinement; the original route ignores litter
public static class RouteDetour
{
	/// <summary>Only the waypoints the walk buffers (<see cref="GuestNavigation.BufferedWaypoints"/>) are looked at: the walk searches again when it has used them, and so looks ahead again from where it stands.</summary>
	public const int Segments = GuestNavigation.BufferedWaypoints;

	/// <summary>How far, in cells, around a segment's bounding box the detour may look.</summary>
	public const int Margin = 3;

	/// <summary>
	/// Replaces the segments of <paramref name="path"/> (the original's waypoints after <paramref name="start"/>, the goal last) among the first <see cref="Segments"/> that cross litter by a cheaper walk of at most
	/// <paramref name="maxExtraCells"/> more cells (than the Manhattan distance of the segment). <paramref name="cost"/> is the policy's extra cost of entering a cell (0 for a clean one). Returns whether anything changed.
	/// </summary>
	public static long DbgCalls, DbgCosted, DbgDetours, DbgEndpoint, DbgNoRoute, DbgNotCheaper, DbgBound;
	public static bool Apply( ParkCellMap map, int mask, (int X, int Y) start, List<(int X, int Y)> path, Func<int, int, int> cost, int maxExtraCells )
	{
		DbgCalls++;
		var changed = false;
		var from = start;
		var index = 0;
		var examined = 0;
		while ( index < path.Count && examined < Segments )
		{
			var to = path[index];
			examined++;
			if ( SegmentCost( from, to, cost ) is > 0 and var dc && ++DbgCosted > 0 && Detour( map, mask, from, to, cost, maxExtraCells ) is { } replacement && path.Count - 1 + replacement.Count <= 0xFC )
			{
				path.RemoveAt( index );
				path.InsertRange( index, replacement );
				index += replacement.Count;
				changed = true; DbgDetours++;
			}
			else
				index++;
			from = path[index - 1];
		}
		return changed;
	}

	/// <summary>The litter cost of the cells the straight walk from the centre of <paramref name="from"/> to the centre of <paramref name="to"/> enters (the cell it starts in not counted).</summary>
	private static int SegmentCost( (int X, int Y) from, (int X, int Y) to, Func<int, int, int> cost )
	{
		var total = 0;
		var steps = Math.Max( Math.Abs( to.X - from.X ), Math.Abs( to.Y - from.Y ) ) * 4;
		var (lastX, lastY) = from;
		for ( var step = 1; step <= steps; step++ )
		{
			// the cell of the point `step / steps` of the way, at the cells' centres (integer arithmetic: the centre of a cell is at x + 0.5, so the cell is the rounded-down interpolation of the centres)
			var x = FloorDiv( (from.X * 2 + 1) * steps + (to.X - from.X) * 2 * step, 2 * steps );
			var y = FloorDiv( (from.Y * 2 + 1) * steps + (to.Y - from.Y) * 2 * step, 2 * steps );
			if ( x == lastX && y == lastY )
				continue;
			(lastX, lastY) = (x, y);
			total += cost( x, y );
		}
		return total;
	}

	private static int FloorDiv( int a, int b ) => (int)Math.Floor( a / (double)b );

	/// <summary>The cheapest four-connected walk from <paramref name="from"/> to <paramref name="to"/>, as corner waypoints after <paramref name="from"/>, the goal last; null when none is cheaper than the straight walk within the bound.</summary>
	public static int DumpLeft = 6;
	private static void Dump( ParkCellMap map, (int X, int Y) from, (int X, int Y) to, Func<int,int,int> cost )
	{
		if ( DumpLeft-- <= 0 ) return;
		var sb = new System.Text.StringBuilder( $"DUMP {from} -> {to}\n" );
		for ( var y = Math.Min( from.Y, to.Y ) - 4; y <= Math.Max( from.Y, to.Y ) + 4; y++ )
		{
			for ( var x = Math.Min( from.X, to.X ) - 4; x <= Math.Max( from.X, to.X ) + 4; x++ )
			{
				var t = map.RawTypeAt( x, y );
				sb.Append( (x, y) == from ? 'F' : (x, y) == to ? 'T' : cost( x, y ) > 0 ? 'L' : t == 1 ? '#' : t == 0 ? '.' : (char)('0' + t % 10) );
			}
			sb.AppendLine();
		}
		Console.Error.Write( sb.ToString() );
	}

	private static List<(int X, int Y)>? Detour( ParkCellMap map, int mask, (int X, int Y) from, (int X, int Y) to, Func<int, int, int> cost, int maxExtraCells )
	{
		if ( from == to || map.TypeAt( from.X, from.Y ) != ParkCellType.Path || map.TypeAt( to.X, to.Y ) != ParkCellType.Path )
		{ DbgEndpoint++;
			return null; }
		Dump( map, from, to, cost );
		var manhattan = Math.Abs( to.X - from.X ) + Math.Abs( to.Y - from.Y );
		var limit = manhattan + maxExtraCells;
		var straight = manhattan + SegmentCost( from, to, cost );
		int minX = Math.Max( 0, Math.Min( from.X, to.X ) - Margin ), maxX = Math.Min( map.Width - 1, Math.Max( from.X, to.X ) + Margin );
		int minY = Math.Max( 0, Math.Min( from.Y, to.Y ) - Margin ), maxY = Math.Min( map.Height - 1, Math.Max( from.Y, to.Y ) + Margin );
		var width = maxX - minX + 1;
		var size = width * (maxY - minY + 1);
		var best = new int[size];
		var steps = new int[size];
		var parent = new int[size];
		Array.Fill( best, int.MaxValue );
		var queue = new PriorityQueue<int, long>();
		var counter = 0L;
		int Id( int x, int y ) => (y - minY) * width + (x - minX);
		var startId = Id( from.X, from.Y );
		best[startId] = 0;
		parent[startId] = -1;
		queue.Enqueue( startId, 0 );
		var goalId = Id( to.X, to.Y );
		while ( queue.TryDequeue( out var id, out var key ) )
		{
			if ( (int)(key >> 20) != best[id] )
				continue;
			if ( id == goalId )
				break;
			int x = minX + id % width, y = minY + id / width;
			for ( var direction = 0; direction < 4; direction++ )
			{
				var (dx, dy) = GuestPathGrid.Directions[direction];
				int nx = x + dx, ny = y + dy;
				if ( nx < minX || nx > maxX || ny < minY || ny > maxY || map.TypeAt( nx, ny ) != ParkCellType.Path || WalkEdges.Blocked( map, x, y, direction, mask ) )
					continue;
				var next = Id( nx, ny );
				var length = steps[id] + 1;
				if ( length > limit )
					continue;
				var total = best[id] + 1 + cost( nx, ny );
				if ( total >= best[next] )
					continue;
				best[next] = total;
				steps[next] = length;
				parent[next] = id;
				queue.Enqueue( next, ((long)total << 20) | (counter++ & 0xFFFFF) );
			}
		}
		if ( best[goalId] == int.MaxValue ) { DbgNoRoute++; return null; }
		if ( best[goalId] >= straight ) { DbgNotCheaper++; return null; }
		var cells = new List<(int X, int Y)>();
		for ( var id = goalId; id != startId; id = parent[id] )
			cells.Add( (minX + id % width, minY + id / width) );
		cells.Reverse();
		// corner waypoints only: a cell between two steps in the same direction is on the line the walker steers along anyway
		var corners = new List<(int X, int Y)>();
		var previous = from;
		for ( var i = 0; i < cells.Count; i++ )
		{
			if ( i + 1 < cells.Count && cells[i].X - previous.X == cells[i + 1].X - cells[i].X && cells[i].Y - previous.Y == cells[i + 1].Y - cells[i].Y )
			{
				previous = cells[i];
				continue;
			}
			corners.Add( cells[i] );
			previous = cells[i];
		}
		return corners;
	}
}
