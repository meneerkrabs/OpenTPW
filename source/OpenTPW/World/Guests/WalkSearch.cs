namespace OpenTPW;

/// <summary>
/// The original walk's cell rules over the <see cref="ParkCellMap"/> (tp.exe): the edge test <c>0x4D8750</c> that tells
/// whether a walker with mask <c>m</c> (walk <c>+0xB4</c>, guest <c>+0x188</c>) may cross from a cell to one of its four
/// neighbours, and the step commit <c>0x510510</c> built on it. Directions are <see cref="GuestPathGrid.Directions"/>
/// (0 −Y, 1 +X, 2 +Y, 3 −X; the original's table <c>0x7622B0</c>). The original map is 128 × 128 cells; a step off a smaller
/// <see cref="ParkCellMap"/> is blocked like a step off the original's.
/// </summary>
// [BIN:TPW-EXE:0x004D8750 edge test] blocked(from, d, mask): map edge (x 0 with d 3, y 0 with d 0, x 127 with d 1, y 127 with d 2); a path target from a non-queue cell is open; type 4 to type 4 open; type 4 target blocked; a 0x1E target is open unless mask 2; mask 2 (or a 0x1E target) reads the target's flag 0x400 (open) and a type-9 target's ride thing; targets of type 2, 5 or 7 blocked; a queue (3, 9) target is open from a path or queue cell whose neighbour link (+0x0C) points back; a path target from a queue cell likewise; an object cell (0x5363F0, 0x53AD20) may block; from a cell that is neither path nor queue every step is open; mask 1 opens path to any non-path, non-queue, non-4 cell; mask 2 path to type 0; a type 9 or 10 source opens into a type-0 cell along its queue link (+0x0D); everything else is blocked
// [BIN:TPW-EXE:0x00536300 cell type tests] 0x536300 type 2, 0x536310 type 1 (path), 0x536320 type 3 or 9 (queue), 0x536340 9, 0x536350 10, 0x536380 4, 0x536390 0, 0x5363A0 0x1E, 0x5363B0 flag 0x400, 0x5363C0 type 5, 7 or 2; 0x522770 the link byte +0x0C (+0x22 underground, not modelled), 0x522850 the queue link +0x0D
public static class WalkEdges
{
	/// <summary>The original map's last cell coordinate (the edge test's hard-coded 0x7F).</summary>
	public const int LastCell = 127;

	/// <summary>The link bit of a neighbour that points back at a cell left in direction <paramref name="direction"/>: 16, 64, 1, 4.</summary>
	private static int BackLink( int direction ) => direction switch { 0 => 0x10, 1 => 0x40, 2 => 0x01, _ => 0x04 };

	private static bool IsQueue( byte type ) => type is 3 or 9;

	private static bool GroundTrackBlocks( ParkCellMap map, int x, int y )
	{
		var track = map.Tracks.Get( x, y );
		if ( track.IsExtension )
		{
			// Zero-parent compatibility and malformed references remain outside this slice;
			// preserve their host fallback rather than interpreting an unrelated record.
			if ( track.Parent <= 0 || track.Parent >= (long)map.Tracks.Width * map.Tracks.Height )
				return false;
			var parent = map.Tracks.CellOf( track.Parent );
			track = map.Tracks.Get( parent.X, parent.Y );
		}
		return track.IsOrigin && (track.Flags & TrackCell.BridgeMask) == 0;
	}

	/// <summary>
	/// <c>0x4D8750</c>: true when a walker with <paramref name="mask"/> may not step from (<paramref name="x"/>,
	/// <paramref name="y"/>) to its neighbour in <paramref name="direction"/>. Coordinates are bytes, as in the original.
	/// </summary>
	// [APPROX:WALK-005] supported track origins and nonzero type-12 parents now read the low flag nibble from ParkCellMap.Tracks. Type-17 and zero/invalid-parent records retain the old fallthrough; a type-9 target owner still does not decide mask-2 steps, and underground links are not modelled. The host track layer retains TRACK-010 bridge/underlay limitations.
	public static bool Blocked( ParkCellMap map, int x, int y, int direction, int mask )
	{
		x &= 0xFF;
		y &= 0xFF;
		if ( (x == 0 && direction == 3) || (y == 0 && direction == 0) || (x == LastCell && direction == 1) || (y == LastCell && direction == 2) )
			return true;
		var (dx, dy) = GuestPathGrid.Directions[direction];
		int tx = x + dx, ty = y + dy;
		// OpenTPW maps smaller than the original's 128 × 128 end at their own edge (the same test for the original's size).
		if ( !map.InBounds( tx, ty ) )
			return true;
		// Elevated decks have their own edges. Neither off-path movement nor walk masks may bypass their sides.
		if ( map.PoolBridges is { Count: > 0 } bridges && (bridges.Covers( x, y ) || bridges.Covers( tx, ty )) )
			return !bridges.Connects( x, y, direction );
		var from = map.RawTypeAt( x, y );
		var to = map.RawTypeAt( tx, ty );
		// [EXT:pools-cell-type] a pool cell blocks every step onto it, for every walk mask: a walker set down off the paths, an escorted guest or a striking employee never wades in (docs/POOLS.md)
		if ( to == ParkCellTypes.PoolWater )
			return true;
		if ( to == 1 && !IsQueue( from ) )
			return false;
		if ( from == 4 && to == 4 )
			return false;
		if ( to == 4 )
			return true;
		if ( mask == 2 )
		{
			// 0x4D8871: the target's flag 0x400 opens it; a type-9 target with a ride thing on its owner cell decides by
			// the ride (0x42A440, 0x4DD4E0 +0x118). OpenTPW keeps no thing references in its cell map (WALK-005).
			if ( ((ushort)map.FlagsAt( tx, ty ) & 0x400) != 0 )
				return false;
		}
		else if ( to == 0x1E )
			return false;
		if ( to is 5 or 7 or 2 )
			return true;
		if ( IsQueue( to ) )
		{
			if ( from == 1 || IsQueue( from ) )
				return (map.LinksAt( tx, ty ) & BackLink( direction )) == 0;
			return true;
		}
		if ( IsQueue( from ) && to == 1 )
			return (map.LinksAt( tx, ty ) & BackLink( direction )) == 0;
		// 0x4D89C3: a target cell whose track record is of a track kind (0x5363F0) blocks unless the record's flag nibble is set (0x53AD20);
		// Supported track origins and type-12 parents now come from the shared track layer.
		if ( GroundTrackBlocks( map, tx, ty ) )
			return true;
		if ( from != 1 && !IsQueue( from ) )
			return false;
		if ( mask == 1 && from == 1 && to != 1 && !IsQueue( to ) && to != 4 )
			return false;
		if ( mask == 2 && from == 1 && to == 0 )
			return false;
		if ( from != 10 && from != 9 )
			return true;
		if ( to != 0 )
			return true;
		var queueLink = map.QueueLinkAt( x, y );
		return !((direction == 3 && queueLink == 4) || (direction == 1 && queueLink == 0x40) || (direction == 0 && queueLink == 0x10)
			|| (direction == 2 && queueLink == 1));
	}

	/// <summary>
	/// The step commit <c>0x510510</c> (the walk's callback <c>0x762190</c>): true when a walker may move its position from cell
	/// <paramref name="from"/> into cell <paramref name="to"/> (null: off the map, refused). Cells more than one apart are refused;
	/// a diagonal step needs one of its two L-shaped routes open (<see cref="Diagonal"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x00510510 step commit] a step of more than one cell in x or y is refused ("walking so fast"); an orthogonal step tests its edge; a diagonal step (up-right, right-down, down-left, up-left: first edge −Y, +X, +Y, −Y) is accepted when the first edge and then the second from that neighbour are open, else refused when the second edge from the start is blocked, else decided by the first edge from the second neighbour; the caller 0x50F3B0 passes the cells of the old and new positions (0x510870: −1 outside 0..127) and the mask +0xB4
	public static bool Commit( ParkCellMap map, (int X, int Y) from, (int X, int Y)? to, int mask )
	{
		if ( to is not { } target )
			return false;
		var (fx, fy) = from;
		var (tx, ty) = target;
		if ( Math.Abs( fx - tx ) > 1 || Math.Abs( fy - ty ) > 1 )
			return false;
		var up = fy - ty == 1;
		var right = tx - fx == 1;
		var down = ty - fy == 1;
		var left = fx - tx == 1;
		var count = (up ? 1 : 0) + (right ? 1 : 0) + (down ? 1 : 0) + (left ? 1 : 0);
		if ( count != 2 )
		{
			if ( up && Blocked( map, fx, fy, 0, mask ) )
				return false;
			if ( right && Blocked( map, fx, fy, 1, mask ) )
				return false;
			if ( down && Blocked( map, fx, fy, 2, mask ) )
				return false;
			if ( left && Blocked( map, fx, fy, 3, mask ) )
				return false;
			return true;
		}
		// The four diagonals (0x5105D2..0x510800), each with the edge it tries first.
		if ( up && right )
			return Diagonal( map, fx, fy, 0, 1, mask );
		if ( right && down )
			return Diagonal( map, fx, fy, 1, 2, mask );
		if ( down && left )
			return Diagonal( map, fx, fy, 2, 3, mask );
		return Diagonal( map, fx, fy, 0, 3, mask );
	}

	/// <summary>
	/// One diagonal of <see cref="Commit"/>: open via the neighbour in <paramref name="first"/>; else refused when the edge in
	/// <paramref name="second"/> is blocked; else decided by the edge in <paramref name="first"/> from that neighbour.
	/// </summary>
	private static bool Diagonal( ParkCellMap map, int x, int y, int first, int second, int mask )
	{
		var (ax, ay) = GuestPathGrid.Directions[first];
		if ( !Blocked( map, x, y, first, mask ) && !Blocked( map, x + ax, y + ay, second, mask ) )
			return true;
		if ( Blocked( map, x, y, second, mask ) )
			return false;
		var (bx, by) = GuestPathGrid.Directions[second];
		return !Blocked( map, x + bx, y + by, first, mask );
	}
}

/// <summary>
/// The original walk's route search (tp.exe <c>0x511420</c>): a line-and-boundary search from the start cell to the goal cell
/// (<c>0x511470</c>: a four-connected line, and at a wall two boundary tracers, one turning each way, until one may leave
/// the wall towards the goal), the corner shortcuts (<c>0x5110D0</c>) and the refinement (<c>0x5108A0</c>). The result is a list of
/// waypoint cells after the start, the goal last; every move between them is decided by <see cref="WalkEdges.Blocked"/>.
/// </summary>
// [BIN:TPW-EXE:0x00511420 route search] clears the result count and the step counter 0x7CF3A4, stores the mask in 0x7CF3C0, runs 0x511470 with the limit 60000 and the refinement 0x5108A0; the result is the step count or 0x70000000 (failed)
public static class WalkSearch
{
	/// <summary>The step limit of every search (<c>0xEA60</c>).</summary>
	public const int StepLimit = 60000;
	/// <summary>The search's failure value <c>0x70000000</c>.</summary>
	public const int Failed = 0x70000000;
	/// <summary>Turns a tracer still in progress to "left the wall" (<c>0x32</c>) or "overflowed" (<c>0x33</c>).</summary>
	private const int Left = 0x32;
	private const int Overflow = 0x33;

	private sealed class Line
	{
		public int Adx, Ady, XDirection, YDirection, Error;

		/// <summary><c>0x511350</c>: a line from <paramref name="start"/> to <paramref name="end"/>.</summary>
		// [BIN:TPW-EXE:0x00511350 line setup] |dx|, |dy|; the y step is +Y when dy ≥ 1 else −Y, the x step +X when dx ≥ 1 else −X; a zero dx takes the y step for both, a zero dy the x step; the error starts at |dx|/2 + |dy|/2
		public void Setup( (int X, int Y) start, (int X, int Y) end )
		{
			var dx = end.X - start.X;
			var dy = end.Y - start.Y;
			Adx = Math.Abs( dx );
			Ady = Math.Abs( dy );
			YDirection = dy >= 1 ? 2 : 0;
			XDirection = dx >= 1 ? 1 : 3;
			if ( start.X == end.X )
				XDirection = YDirection;
			if ( start.Y == end.Y )
				YDirection = XDirection;
			Error = Adx / 2 + Ady / 2;
		}

		/// <summary>The next step: the y step when the error has reached |dx| (then error −= |dx|), else the x step (error += |dy|).</summary>
		public int Next()
		{
			if ( Error >= Adx )
			{
				Error -= Adx;
				return YDirection;
			}
			Error += Ady;
			return XDirection;
		}
	}

	private sealed class Tracer
	{
		public (int X, int Y) Position, Hit, Goal;
		public int Direction, Turn;
		public readonly List<(int X, int Y)> Path = new();
	}

	private static (int X, int Y) Add( (int X, int Y) cell, int direction )
	{
		var (dx, dy) = GuestPathGrid.Directions[direction];
		return ((cell.X + dx) & 0xFF, (cell.Y + dy) & 0xFF);
	}

	/// <summary>
	/// <c>0x511420</c>: the waypoints from <paramref name="start"/> to <paramref name="goal"/> for <paramref name="mask"/>, appended
	/// to <paramref name="path"/> (cleared first). False when the search fails (the original's 0x70000000); the path is then not
	/// meaningful.
	/// </summary>
	public static bool Find( ParkCellMap map, (int X, int Y) start, (int X, int Y) goal, int mask, List<(int X, int Y)> path )
	{
		path.Clear();
		start = (start.X & 0xFF, start.Y & 0xFF);
		goal = (goal.X & 0xFF, goal.Y & 0xFF);
		var steps = 0;
		if ( !Search( map, mask, start, goal, path, StepLimit, ref steps ) )
			return false;
		Refine( map, mask, start, path );
		return true;
	}

	/// <summary>
	/// <c>0x511470</c>: the line search from <paramref name="start"/> towards <paramref name="goal"/>, appending its waypoints to
	/// <paramref name="path"/>; <paramref name="steps"/> is the shared step counter <c>0x7CF3A4</c>.
	/// </summary>
	// [BIN:TPW-EXE:0x00511470 line search] the line steps while its edge is open; a change of step direction after the first step appends the new cell when the corner it cuts is blocked (0x511AEB..0x511BC7); at a blocked edge the cell is appended and two tracers start there, one turning right (0x511616, direction + 1, turn −1) and one left (0x511C90, direction − 1, turn +1); they step alternately until one may leave the wall (0x32), both overflow (0x33) or they meet head on, which fails; the winner's cells and position are appended, the shortcuts 0x5110D0 run from 0 and the line starts again from the last waypoint; the goal is appended on arrival (fewer than 0xFA waypoints), and reaching the step limit fails
	private static bool Search( ParkCellMap map, int mask, (int X, int Y) start, (int X, int Y) goal, List<(int X, int Y)> path, int limit, ref int steps )
	{
		var line = new Line();
		line.Setup( start, goal );
		var current = start;
		var first = true;
		var direction = 0;
		bool End( (int X, int Y) at )
		{
			if ( path.Count < 0xFA && at == goal )
			{
				path.Add( at );
				return true;
			}
			return false;
		}
		bool Fail( (int X, int Y) at )
		{
			path.Add( at );
			return false;
		}
		if ( steps >= limit )
			return End( current );
		while ( true )
		{
			if ( current == goal )
				return End( current );
			var previous = direction;
			direction = line.Next();
			if ( WalkEdges.Blocked( map, current.X, current.Y, direction, mask ) )
			{
				path.Add( current );
				var a = new Tracer { Position = current, Hit = current, Goal = goal, Direction = (direction + 1) & 3, Turn = -1 };
				var b = new Tracer { Position = current, Hit = current, Goal = goal, Direction = (direction - 1) & 3, Turn = 1 };
				Tracer? winner = null;
				if ( steps < limit )
				{
					while ( true )
					{
						Trace( map, mask, a );
						if ( HeadOn( a, b ) )
							return Fail( current );
						Trace( map, mask, b );
						if ( HeadOn( a, b ) )
							return Fail( current );
						if ( a.Direction == Left )
						{
							winner = a;
							break;
						}
						if ( b.Direction == Left )
						{
							winner = b;
							break;
						}
						if ( a.Direction == Overflow && b.Direction == Overflow )
							return Fail( current );
						if ( ++steps >= limit )
							break;
					}
					if ( winner != null )
					{
						if ( path.Count + winner.Path.Count + 2 > 0xFC )
							return Fail( current );
						path.AddRange( winner.Path );
						path.Add( winner.Position );
					}
				}
				if ( steps == limit )
					return Fail( current );
				Simplify( map, mask, start, path, 0 );
				current = path.Count > 0 ? path[^1] : start;
				line.Setup( current, goal );
				first = true;
			}
			else
			{
				var saved = current;
				current = Add( current, direction );
				if ( first )
					first = false;
				else if ( direction != previous )
				{
					bool corner;
					if ( !WalkEdges.Blocked( map, current.X, current.Y, previous, mask ) )
					{
						var beyond = Add( current, previous );
						corner = WalkEdges.Blocked( map, beyond.X, beyond.Y, direction, mask );
					}
					else
					{
						var ahead = Add( saved, direction );
						corner = !WalkEdges.Blocked( map, saved.X, saved.Y, direction, mask ) && WalkEdges.Blocked( map, ahead.X, ahead.Y, previous, mask );
					}
					if ( corner )
					{
						path.Add( current );
						if ( path.Count > 0xFA )
							return Fail( current );
					}
				}
			}
			if ( ++steps >= limit )
				return End( current );
		}
	}

	/// <summary>The tracers meet head on (<c>0x511859</c>, <c>0x5118D0</c>): a's next cell is b's and they face each other.</summary>
	private static bool HeadOn( Tracer a, Tracer b ) =>
		a.Direction is not (Left or Overflow) && Add( a.Position, a.Direction ) == b.Position && ((a.Direction - 2) & 3) == b.Direction;

	/// <summary>
	/// One step of a boundary tracer (<c>0x511C90</c>, inlined for the first tracer at <c>0x511616</c>): turn away from a blocked edge
	/// and record, or move and, when the turn back is open, turn and record; then leave the wall (<c>0x32</c>) once the position
	/// lies between the hit point and the goal, facing and turned towards it.
	/// </summary>
	// [BIN:TPW-EXE:0x00511C90 boundary tracer] a blocked edge turns the tracer by −turn and records its cell; else it moves and turns by +turn when that edge is open, recording the cell; more than 0xFB recorded cells end it (0x33); it leaves (0x32) unless the goal lies behind its turned or its own heading, or its cell is outside the box of the hit point and the goal, or it stands on the hit point
	private static void Trace( ParkCellMap map, int mask, Tracer tracer )
	{
		if ( tracer.Direction == Overflow )
			return;
		bool record;
		if ( WalkEdges.Blocked( map, tracer.Position.X, tracer.Position.Y, tracer.Direction, mask ) )
		{
			tracer.Direction = (tracer.Direction - tracer.Turn) & 3;
			record = true;
		}
		else
		{
			tracer.Position = Add( tracer.Position, tracer.Direction );
			var turned = (tracer.Direction + tracer.Turn) & 3;
			record = !WalkEdges.Blocked( map, tracer.Position.X, tracer.Position.Y, turned, mask );
			if ( record )
				tracer.Direction = turned;
		}
		if ( record )
		{
			tracer.Path.Add( tracer.Position );
			if ( (tracer.Path.Count & 0xFF) > 0xFB )
			{
				tracer.Direction = Overflow;
				return;
			}
		}
		var (gx, gy) = tracer.Goal;
		var (x, y) = tracer.Position;
		switch ( (tracer.Direction + tracer.Turn) & 3 )
		{
			case 0 when gy < y:
			case 1 when gx > x:
			case 2 when gy > y:
			case 3 when gx < x:
				return;
		}
		switch ( tracer.Direction )
		{
			case 0 when gy > y:
			case 1 when gx < x:
			case 2 when gy < y:
			case 3 when gx > x:
				return;
		}
		var (hx, hy) = tracer.Hit;
		if ( x < Math.Min( hx, gx ) || x > Math.Max( hx, gx ) || y < Math.Min( hy, gy ) || y > Math.Max( hy, gy ) )
			return;
		if ( (hx, hy) != (x, y) )
			tracer.Direction = Left;
	}

	/// <summary>
	/// <c>0x5110D0</c>: from waypoint <paramref name="from"/> on, each waypoint is skipped when the line from the previous kept one
	/// reaches one of the next four (farthest first) without a blocked edge or a blocked cut corner; repeated until nothing changes.
	/// </summary>
	// [BIN:TPW-EXE:0x005110D0 shortcuts] the anchor is the start or waypoint from − 1; for k = 4 down to 1 the line 0x511350 to waypoint read + k is tried: a blocked edge fails it, and after the first step a change of direction fails it when the corner on the new cell or on the previous cell is cut off (0x5111F4..0x51128F); a success skips to read + k, else waypoint read is kept as the new anchor; the pass repeats while it changed anything
	private static bool Simplify( ParkCellMap map, int mask, (int X, int Y) start, List<(int X, int Y)> path, int from )
	{
		var changedAny = false;
		var line = new Line();
		while ( true )
		{
			var read = from;
			var write = from;
			var anchor = from == 0 ? start : path[from - 1];
			var changed = false;
			var count = path.Count;
			while ( read < count )
			{
				var shortcut = false;
				for ( var k = 4; k > 0; k-- )
				{
					if ( k + read >= count )
						continue;
					var target = path[read + k];
					if ( !LineIsOpen( map, mask, line, anchor, target ) )
						continue;
					read += k;
					changed = true;
					changedAny = true;
					shortcut = true;
					break;
				}
				if ( !shortcut )
				{
					anchor = path[read];
					path[write++] = anchor;
					read++;
				}
			}
			path.RemoveRange( write, path.Count - write );
			if ( !changed )
				return changedAny;
		}
	}

	/// <summary>The straight walk of <see cref="Simplify"/> from <paramref name="anchor"/> to <paramref name="target"/>.</summary>
	private static bool LineIsOpen( ParkCellMap map, int mask, Line line, (int X, int Y) anchor, (int X, int Y) target )
	{
		line.Setup( anchor, target );
		var position = anchor;
		var saved = anchor;
		var previous = -1;
		var first = true;
		while ( position != target )
		{
			var previousDirection = previous;
			var direction = line.Next();
			previous = direction;
			if ( WalkEdges.Blocked( map, position.X, position.Y, direction, mask ) )
				return false;
			if ( first )
				first = false;
			else if ( direction != previousDirection )
			{
				if ( !WalkEdges.Blocked( map, position.X, position.Y, previousDirection, mask ) )
				{
					var beyond = Add( position, previousDirection );
					if ( WalkEdges.Blocked( map, beyond.X, beyond.Y, direction, mask ) )
						return false;
				}
				if ( !WalkEdges.Blocked( map, saved.X, saved.Y, direction, mask ) )
				{
					var ahead = Add( saved, direction );
					if ( WalkEdges.Blocked( map, ahead.X, ahead.Y, previousDirection, mask ) )
						return false;
				}
			}
			saved = position;
			position = Add( position, direction );
		}
		return true;
	}

	/// <summary>
	/// <c>0x5108A0</c>: the shortcuts from 0, then up to nine replacements of a waypoint pair by a fresh search from the waypoint
	/// before them (each restarting the scan), then a detour waypoint before every diagonal step whose corner is cut on one side.
	/// </summary>
	// [BIN:TPW-EXE:0x005108A0 refinement] for i while i + 1 < count: a search 0x511470 (limit 60000, counter reset) from the start or waypoint i − 1 to waypoint i + 1 replaces waypoints i and i + 1 when it succeeds, does not pass waypoint i before its last cell and keeps at most 0xFC waypoints; then the shortcuts from max(0, i − 4) and the scan restarts; the ninth replacement returns; the final pass (0x510A1E) inserts, before a waypoint one diagonal step from the previous one, the neighbour (−sx, 0) or (0, −sy) of it whose L-shaped route is open when the other side's corner is blocked (fewer than 0xFC waypoints)
	private static void Refine( ParkCellMap map, int mask, (int X, int Y) start, List<(int X, int Y)> path )
	{
		Simplify( map, mask, start, path, 0 );
		var local = new List<(int X, int Y)>();
		for ( var budget = 9; ; )
		{
			var replaced = false;
			for ( var i = 0; i + 1 < path.Count; i++ )
			{
				var from = i == 0 ? start : path[i - 1];
				var to = path[i + 1];
				local.Clear();
				var steps = 0;
				if ( !Search( map, mask, from, to, local, StepLimit, ref steps ) )
					continue;
				var middle = path[i];
				var passes = false;
				for ( var cell = 0; cell + 1 < local.Count; cell++ )
				{
					if ( local[cell] == middle )
					{
						passes = true;
						break;
					}
				}
				if ( passes || path.Count + local.Count - 2 > 0xFC )
					continue;
				path.RemoveRange( i, 2 );
				path.InsertRange( i, local );
				Simplify( map, mask, start, path, Math.Max( 0, i - 4 ) );
				if ( --budget == 0 )
					return;
				replaced = true;
				break;
			}
			if ( !replaced )
				break;
		}
		for ( var j = 0; j < path.Count; j++ )
		{
			var a = j == 0 ? start : path[j - 1];
			var b = path[j];
			int sx = b.X - a.X, sy = b.Y - a.Y;
			if ( Math.Abs( sx ) != 1 || Math.Abs( sy ) != 1 )
				continue;
			// The corner cells of the step: Q = (a.X + sx, a.Y) and P = (a.X, a.Y + sy); the detour goes through P (offset (−sx, 0) from b) or Q ((0, −sy)).
			var toQFromB = sy > 0 ? 0 : 2;
			var toPFromB = sx > 0 ? 3 : 1;
			var toPFromA = sy > 0 ? 2 : 0;
			var toQFromA = sx > 0 ? 1 : 3;
			(int X, int Y)? offset = null;
			if ( WalkEdges.Blocked( map, b.X, b.Y, toQFromB, mask ) && !WalkEdges.Blocked( map, b.X, b.Y, toPFromB, mask ) )
				offset = (-sx, 0);
			else if ( WalkEdges.Blocked( map, b.X, b.Y, toPFromB, mask ) && !WalkEdges.Blocked( map, b.X, b.Y, toQFromB, mask ) )
				offset = (0, -sy);
			else if ( WalkEdges.Blocked( map, a.X, a.Y, toPFromA, mask ) && !WalkEdges.Blocked( map, a.X, a.Y, toQFromA, mask ) )
				offset = (0, -sy);
			else if ( WalkEdges.Blocked( map, a.X, a.Y, toQFromA, mask ) && !WalkEdges.Blocked( map, a.X, a.Y, toPFromA, mask ) )
				offset = (-sx, 0);
			if ( offset is not { } detour || path.Count + 1 >= 0xFC )
				continue;
			path.Insert( j, ((b.X + detour.X) & 0xFF, (b.Y + detour.Y) & 0xFF) );
			j++;
		}
	}
}
