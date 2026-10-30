namespace OpenTPW;

/// <summary>
/// 16.16 fixed point as the original's steering uses it (Toolbox FixMul/FixDiv, the vector length of <c>0xff098</c>),
/// restated from <c>tools/ppc-analysis/lanes/walk/walk_evidence.py</c> (docs/reverse/WALK-plan.md §3). FixMul and
/// FixDiv round to nearest; WALK-plan §9 enumerates both roundings and the walk terms hold for either.
/// </summary>
public static class Fixed16
{
	public const int One = 0x10000;

	public static int Mul( int a, int b ) => (int)(((long)a * b + 0x8000) >> 16);

	/// <summary>FixDiv with saturation on division by zero.</summary>
	public static int Div( int a, int b )
	{
		if ( b == 0 )
			return a >= 0 ? int.MaxValue : int.MinValue;
		return (int)Math.Floor( (double)((long)a * One) / b + 0.5 );
	}

	/// <summary>Vector length as in <c>0xff098</c>/<c>0xfe958</c>: exact below 2^12, else in units of 2^8 or 2^18.</summary>
	public static int Length( int x, int y )
	{
		long ax = Math.Abs( (long)x ), ay = Math.Abs( (long)y );
		var bits = ax | ay;
		if ( (bits & ~0xfffL) == 0 )
			return (int)Math.Sqrt( ax * ax + ay * ay );
		if ( (bits & ~0x3fffffL) == 0 )
			return (int)Math.Sqrt( (ax >> 8) * (ax >> 8) + (ay >> 8) * (ay >> 8) ) << 8;
		return (int)Math.Sqrt( (ax >> 18) * (ax >> 18) + (ay >> 18) * (ay >> 18) ) << 18;
	}

	/// <summary>|dx| + |dy| − min / 2 (<c>0xfe778..0xfe7c8</c>, <c>0xff784..0xff7d8</c>).</summary>
	public static int Octile( int x, int y )
	{
		int ax = Math.Abs( x ), ay = Math.Abs( y );
		return ax + ay - (Math.Min( ax, ay ) >> 1);
	}

	/// <summary><c>0xff098</c>: unchanged when shorter than <paramref name="maximum"/>, else scaled by FixDiv(maximum, length).</summary>
	public static (int X, int Y) Truncate( int x, int y, int maximum )
	{
		var size = Length( x, y );
		if ( size < maximum )
			return (x, y);
		var scale = Div( maximum, size );
		return (Mul( x, scale ), Mul( y, scale ));
	}

	/// <summary>tp.exe's FixMul <c>0x50DF70</c>: bits 16..47 of the 64-bit product, so it rounds towards −∞.</summary>
	// [BIN:TPW-EXE:0x0050DF70 fixed multiply] imul, then (edx << 16) | (eax >> 16) for each component
	public static int PcMul( int a, int b ) => unchecked((int)(((long)a * b) >> 16));

	/// <summary>tp.exe's FixDiv <c>0x50E080</c>: (a &lt;&lt; 16) / b by idiv, rounding towards zero; division by zero saturates as <see cref="Div"/>.</summary>
	// [BIN:TPW-EXE:0x0050E080 fixed divide] edx:eax = a sign-extended and shifted left 16, idiv b
	public static int PcDiv( int a, int b )
	{
		if ( b == 0 )
			return a >= 0 ? int.MaxValue : int.MinValue;
		return unchecked((int)(((long)a << 16) / b));
	}

	/// <summary>tp.exe's truncate <c>0x50F600</c>: <see cref="Truncate"/> with <see cref="PcDiv"/> and <see cref="PcMul"/>.</summary>
	// [BIN:TPW-EXE:0x0050F600 vector truncate] the length of 0x4D49A0 (exact below 2^12, else in units of 2^8 or 2^18); unchanged when below the maximum, else each component FixMul(c, FixDiv(maximum, length))
	public static (int X, int Y) PcTruncate( int x, int y, int maximum )
	{
		var size = Length( x, y );
		if ( size < maximum )
			return (x, y);
		var scale = PcDiv( maximum, size );
		return (PcMul( x, scale ), PcMul( y, scale ));
	}
}

/// <summary>The walk step's result (tp.exe <c>0x4FA2A0</c>): 0 arrived, 1 still walking, 2 stuck (a route search failed).</summary>
public enum WalkResult
{
	Arrived = 0,
	Walking = 1,
	Stuck = 2
}

/// <summary>
/// One guest's navigation object (original guest <c>+212</c>, tp.exe <c>+0xD4</c>) and its steering step <c>0xfec9c</c>
/// (tp.exe <c>0x50F3B0</c>), run once per park turn in the walking states (docs/reverse/WALK-plan.md §3, §5–§7, §11). Positions
/// are 16.16 cells. The step sums avoid_walls (0.9), follow_path (0.5) and separation (0.1), each truncated to the max force
/// <c>+24</c>; the sum is truncated again and added to the velocity, which is truncated to the max speed <c>+28</c>. A new
/// destination does not reset the velocity.
/// <para>
/// Two kinds of walk share the object. A guest's walk on the park's cells (<see cref="Navigate"/>, <see cref="Original"/>) is the
/// tp.exe walk: the route search <see cref="WalkSearch"/> at every set-up and refill, the edge test <see cref="WalkEdges"/> with the
/// walk mask for the walls and the step commit, the block stamps of the cell map, and its progress measure. A walk along an
/// explicit cell route (<see cref="SetDestination"/>: staff, the arrival lanes, and walks restored from saves made before) keeps
/// OpenTPW's earlier model: the route's cells are the walkable ones and the Mac step's arithmetic.
/// </para>
/// </summary>
// [BIN:STP-PPC:0x100FEC9C steering step] forces avoid_walls/follow_path/separation (data 0xecd6c list), each truncated to +24 and FixMul-weighted; sum truncated to +24, FixDiv by ONE; +16 += force, truncated to +28; position committed when the map accepts the cell; progress history +176
public sealed class GuestNavigation
{
	/// <summary>Navigation <c>+4</c>: 0.2 cell, written only by the constructor (<c>0xffc68</c>).</summary>
	public const int Radius = 13107;
	/// <summary>Last segment: arrival at octile &lt; FixMul(1.6, radius) = 0.32 cell (<c>0xfe7ac..0xfe7d8</c>).</summary>
	public const int ArrivalFactor = 0x20000 - 26215;
	/// <summary>Other segments: the waypoint is passed at octile &lt; FixMul(2.0, radius) = 0.4 cell (<c>0xfe850</c>).</summary>
	public const int WaypointFactor = 0x20000;
	/// <summary>Arrival ramp: desired speed min(+28, |offset| / 2.0) (<c>0xfe644</c>, <c>0xfea08</c>).</summary>
	public const int RampDivisor = 0x20000;
	/// <summary>Reroute when at least 0.4 of the last 15 steps did not improve (<c>0xfe6c0</c>, <c>0xff978</c>).</summary>
	public const int StuckFraction = 26214;
	public const int StuckWindow = 15;
	/// <summary>At most five route waypoints are buffered (<c>0xff34c</c>).</summary>
	public const int BufferedWaypoints = 5;
	public const int AvoidWallsWeight = 0x10000 - 6554;
	public const int FollowPathWeight = 0x10000 - 0x8000;
	public const int SeparationWeight = 6553;

	/// <summary>FixMul(<see cref="ArrivalFactor"/>, <see cref="Radius"/>) = 0.32 cell.</summary>
	public const int ArrivalRadius = 20971;
	/// <summary>FixMul(<see cref="WaypointFactor"/>, <see cref="Radius"/>) = 0.4 cell.</summary>
	public const int WaypointRadius = 26214;

	/// <summary>Position <c>+8</c>.</summary>
	public int X, Y;
	/// <summary>Velocity <c>+16</c>, never reset by a new destination.</summary>
	public int VelocityX, VelocityY;
	/// <summary>Exact destination <c>+76/+80</c>.</summary>
	public int TargetX, TargetY;
	/// <summary><c>+96</c>; also true before the first destination.</summary>
	public bool Arrived = true;
	/// <summary>Index <c>+92</c> into <see cref="Buffer"/>.</summary>
	public int Index;
	/// <summary>Buffered waypoint cells (centres are the waypoints) and their count.</summary>
	public readonly (int X, int Y)[] Buffer = new (int X, int Y)[BufferedWaypoints];
	public int Count;
	/// <summary>The whole route, from the start cell to the destination cell; <see cref="Cursor"/> is the route index of <see cref="Buffer"/>[0].</summary>
	public (int X, int Y)[] Route = [];
	public int Cursor;
	/// <summary>Cell crossings of the walk as set up (route cells − 1); the stuck reroute keeps it. Bookkeeping for the M3 gate, not read by the step.</summary>
	public int WalkCrossings;
	/// <summary>Cells besides the route that are walkable for this walk (a queue walk's neighbours along the queue).</summary>
	public (int X, int Y)[] AlsoOpen = [];
	/// <summary>Progress history <c>+176</c>: one bit per step that did not improve (low 15 bits kept).</summary>
	public int History;
	/// <summary>Remaining route distance after the last step, −1 right after route setup.</summary>
	public int PreviousRemaining = -1;
	/// <summary>True for a walk set up by <see cref="Navigate"/>: the tp.exe search, edge test, commit and progress.</summary>
	public bool Original;
	/// <summary>The walk mask <c>+0xB4</c> (guest <c>+0x188</c>) the edge test reads: 0 on the park's paths, 1 while leaving.</summary>
	public int Mask;
	/// <summary><c>+0xB8</c>: a route search of this walk failed; the walk step reports stuck (2).</summary>
	public bool Stuck;
	/// <summary><c>+0x48</c>: the cell map's stamp counter when the walk was last set up (<see cref="ParkCellMap.BlockStamp"/>).</summary>
	public int Stamp;
	/// <summary><c>+0x54</c>: the waypoints of the last search; <see cref="Count"/> (<c>+0x58</c>) of them are buffered.</summary>
	public int PathCount;
	/// <summary><c>+0xA0</c>: the route's octile length from the position at set-up, through every waypoint.</summary>
	public int Total;
	/// <summary><c>+0xA4</c>: the octile lengths between the buffered waypoints from <see cref="Index"/> on.</summary>
	public int Ahead;
	/// <summary><c>+0xA8</c>: the octile lengths between the waypoints beyond the buffer.</summary>
	public int Beyond;
	/// <summary><c>+0xAC</c>: the progress after the last step (<see cref="ProgressValue"/>).</summary>
	public int Progress;

	/// <summary>Walking off the path grid (the staff's road legs, ECON-022): no walls and every position is accepted.</summary>
	// Approximation (ECON-022, registered at StaffWorldSimulation.UpdateStaff): the staff's road legs outside the park, where the paths end, are walked off the path grid (no wall force, every position committed); no guest walks off the grid, the arrival chain being the original's walk over the cell map
	public bool OffGrid;
	public int Reroutes;

	/// <summary>Cell centre (cell &lt;&lt; 16) + 32768: route waypoints (<c>0xff360..0xff384</c>).</summary>
	public static (int X, int Y) Centre( (int X, int Y) cell ) => ((cell.X << 16) + 32768, (cell.Y << 16) + 32768);

	/// <summary>A sub-cell point: destination bytes are (cell &lt;&lt; 8 | byte) &lt;&lt; 8 (<c>0xe68cc/0xe68d0</c>), byte / 256 of a cell.</summary>
	public static (int X, int Y) BytePoint( (int X, int Y) cell, int byteX, int byteY ) => ((cell.X << 16) + (byteX << 8), (cell.Y << 16) + (byteY << 8));

	public (int X, int Y) Cell => (X >> 16, Y >> 16);

	/// <summary>The point follow_path steers to: the destination on the last segment, else the current waypoint's centre.</summary>
	public (int X, int Y) Goal => Count == 0 || OnLastSegment ? (TargetX, TargetY) : Centre( Buffer[Index] );

	/// <summary>True when <paramref name="x"/>, <paramref name="y"/> is a cell of the current route or of <see cref="AlsoOpen"/>.</summary>
	public bool IsOnWalk( int x, int y )
	{
		foreach ( var cell in Route )
		{
			if ( cell.X == x && cell.Y == y )
				return true;
		}
		foreach ( var cell in AlsoOpen )
		{
			if ( cell.X == x && cell.Y == y )
				return true;
		}
		return false;
	}

	/// <summary>
	/// Route setup <c>0xff224</c>: the route from the start cell (the guest's own cell) to the destination cell. An empty
	/// route is a routing failure and counts as arrived at once (<c>0xff294..0xff2cc</c>). The velocity is kept.
	/// </summary>
	/// <param name="alsoOpen">Cells off the route that are still walkable during this walk.</param>
	public void SetDestination( int targetX, int targetY, IReadOnlyList<(int X, int Y)> route, IReadOnlyList<(int X, int Y)>? alsoOpen = null )
	{
		Original = false;
		Stuck = false;
		TargetX = targetX;
		TargetY = targetY;
		WalkCrossings = Math.Max( 0, route.Count - 1 );
		AlsoOpen = alsoOpen?.ToArray() ?? [];
		var cells = new (int X, int Y)[route.Count];
		for ( var index = 0; index < cells.Length; index++ )
			cells[index] = route[index];
		Setup( cells );
	}

	private void Setup( (int X, int Y)[] route )
	{
		Route = route;
		Cursor = 0;
		Index = 0;
		History = 0;
		PreviousRemaining = -1;
		if ( route.Length == 0 )
		{
			Count = 0;
			Arrived = true;
			return;
		}
		Arrived = false;
		Fill();
	}

	private void Fill()
	{
		Count = Math.Min( BufferedWaypoints, Route.Length - Cursor );
		for ( var index = 0; index < Count; index++ )
			Buffer[index] = Route[Cursor + index];
	}

	/// <summary>Route setup mode 1 (the stuck reroute): the current cell, then the rest of the route from the current waypoint.</summary>
	private void Reroute()
	{
		Reroutes++;
		var cell = Cell;
		var rest = Route.AsSpan( Cursor + Index );
		var start = rest.Length > 0 && rest[0] == cell ? 0 : 1;
		var route = new (int X, int Y)[rest.Length + start];
		if ( start == 1 )
			route[0] = cell;
		rest.CopyTo( route.AsSpan( start ) );
		Setup( route );
	}

	private bool OnLastSegment => Cursor + Index == Route.Length - 1;

	private int Remaining()
	{
		var (wx, wy) = Centre( Buffer[Index] );
		var remaining = Fixed16.Octile( X - wx, Y - wy );
		for ( var index = Index + 1; index < Count; index++ )
		{
			var (ax, ay) = Centre( Buffer[index - 1] );
			var (bx, by) = Centre( Buffer[index] );
			remaining += Fixed16.Octile( bx - ax, by - ay );
		}
		return remaining;
	}

	/// <summary>
	/// separation's neighbour query <c>0x10087c</c> with its callback <c>0xfd5f4</c> (data <c>0x7088</c>): the push away
	/// from the first five neighbours in <paramref name="scanOrder"/> whose 1/256-cell position lies within 2 × radius
	/// (Euclidean, squared in 1/256 cells, <c>0x100a18..0x100a3c</c>) and is not the guest's own; each adds
	/// FixDiv(self − other, octile(self − other)) when that octile distance is not 0 and below the two radii plus ONE.
	/// The caller passes the guests of the 3 × 3 cells around this guest's cell, rows from the top, each row from the left.
	/// </summary>
	// [BIN:STP-PPC:0x1010087C separation neighbours] cells (x, y) +- ceil(range / ONE) with range = 2 x radius (0xfd870), row by row (0x1008f0..0x100a90); thing types 1 (guest), 4..8, 18 (0x100978..0x100a08); skip self (+212); d^2 = (dx >> 8)^2 + (dy >> 8)^2, skip 0 and >= (range >> 8)^2; the query stops after 5 callbacks (0x100a50..0x100a58)
	// [BIN:STP-PPC:0x100FD5F4 separation callback] off = self +8 - other +8 (0xfcb8c); d = octile(off); add FixDiv(off, d) (0xfd704) to the sum at data 0xecd58 when d < self +4 + other +4 + ONE (data 0xecd84) and d != 0 (data 0xecd88)
	public static (int X, int Y) Separation( GuestNavigation self, IEnumerable<GuestNavigation> scanOrder )
	{
		const int Range = 2 * Radius;
		const int Limit = (Range >> 8) * (Range >> 8);
		int selfX = self.X >> 8, selfY = self.Y >> 8;
		int sumX = 0, sumY = 0, visited = 0;
		foreach ( var other in scanOrder )
		{
			if ( other == self )
				continue;
			var dx = (other.X >> 8) - selfX;
			var dy = (other.Y >> 8) - selfY;
			var square = dx * dx + dy * dy;
			if ( square == 0 || square >= Limit )
				continue;
			var offsetX = self.X - other.X;
			var offsetY = self.Y - other.Y;
			var distance = Fixed16.Octile( offsetX, offsetY );
			if ( distance < Radius + Radius + Fixed16.One && distance != 0 )
			{
				sumX += Fixed16.Div( offsetX, distance );
				sumY += Fixed16.Div( offsetY, distance );
			}
			if ( ++visited >= 5 )
				break;
		}
		return (sumX, sumY);
	}

	/// <summary>
	/// One steering step of a walk along an explicit route (the Mac model of WALK-plan §3–§7). <paramref name="isOpen"/> tells whether a cell is walkable for the guest; it decides the
	/// walls and the position commit (ignored <see cref="OffGrid"/>). <paramref name="separation"/> returns the summed
	/// neighbour push (<see cref="Separation"/>) when the traced on/off rule asks for it; null means no neighbours.
	/// Returns the walk step's result: true when the guest has arrived, tested on the position at the start of the step
	/// (the step still moves).
	/// </summary>
	// [EXT:legacy-save-route-walk] the route walk survives only for a guest off the grid and for a walk restored from an OpenTPW save made before the original walk: the route's cells (and the queue's next cells ahead and behind) are the walkable ones, avoid_walls has its edge branch only (with the probe's cell), refills come from the stored route, the stuck reroute prepends the current cell to the rest, progress compares raw remaining distances in the five-waypoint window, and the Mac's rounding FixMul/FixDiv apply; every walk the game starts is the search and edge-test walk of Navigate
	public bool Step( int maxSpeed, int maxForce, Func<int, int, bool> isOpen, Func<GuestNavigation, (int X, int Y)>? separation = null )
	{
		// avoid_walls (0xfdc70): probe = position + velocity; a blocked edge of the probe cell in the direction of motion
		// (zero counts as positive) within the radius pushes back with 2 x the penetration, perpendicular to the edge. The
		// edges are tried in the order +x, -x, +y, -y and the first that pushes is the whole force (each branch ends with
		// b 0xfe5f0: 0xfde78, 0xfdee4, 0xfdf64, 0xfdfe0).
		var probeX = X + VelocityX;
		var probeY = Y + VelocityY;
		int wallX = 0, wallY = 0;
		if ( !OffGrid )
		{
			var cellX = probeX >> 16;
			var cellY = probeY >> 16;
			if ( VelocityX >= 0 )
			{
				var high = ((cellX + 1) << 16) - Radius;
				if ( probeX > high && !isOpen( cellX + 1, cellY ) )
					wallX = 2 * (high - probeX);
			}
			else
			{
				var low = (cellX << 16) + Radius;
				if ( probeX < low && !isOpen( cellX - 1, cellY ) )
					wallX = 2 * (low - probeX);
			}
			if ( wallX == 0 && VelocityY >= 0 )
			{
				var high = ((cellY + 1) << 16) - Radius;
				if ( probeY > high && !isOpen( cellX, cellY + 1 ) )
					wallY = 2 * (high - probeY);
			}
			else if ( wallX == 0 )
			{
				var low = (cellY << 16) + Radius;
				if ( probeY < low && !isOpen( cellX, cellY - 1 ) )
					wallY = 2 * (low - probeY);
			}
		}

		// follow_path (0xfe628).
		if ( !Arrived && Count > 0 && PreviousRemaining != 0 && StuckBits() * Fixed16.One / StuckWindow >= StuckFraction )
			Reroute();
		int pathX, pathY, goalX = TargetX, goalY = TargetY;
		if ( Arrived || Count == 0 )
			(pathX, pathY) = (-VelocityX, -VelocityY);
		else
		{
			if ( OnLastSegment )
			{
				if ( Fixed16.Octile( TargetX - X, TargetY - Y ) < ArrivalRadius )
					Arrived = true;
			}
			else
			{
				var (wx, wy) = Centre( Buffer[Index] );
				if ( Fixed16.Octile( wx - X, wy - Y ) < WaypointRadius )
				{
					Index++;
					if ( Index == Count - 1 && !OnLastSegment )
					{
						Cursor += Index;
						Index = 0;
						Fill();
					}
				}
				// The step that passes the second-to-last waypoint still steers to the last waypoint's centre.
				(goalX, goalY) = Centre( Buffer[Index] );
			}
			var offsetX = goalX - X;
			var offsetY = goalY - Y;
			if ( !OnLastSegment )
				(pathX, pathY) = (offsetX - VelocityX, offsetY - VelocityY);
			else
			{
				var size = Fixed16.Length( offsetX, offsetY );
				if ( size == 0 )
					(pathX, pathY) = (0, 0);
				else
				{
					var half = Fixed16.Div( size, RampDivisor );
					var scale = Fixed16.Div( maxSpeed < half ? maxSpeed : half, size );
					(pathX, pathY) = (Fixed16.Mul( offsetX, scale ) - VelocityX, Fixed16.Mul( offsetY, scale ) - VelocityY);
				}
			}
		}

		// separation (0xfd778): off on the last segment (or once arrived) within ONE + radius of its waypoint, else the
		// neighbour push of the query 0x10087c over 2 x radius.
		int pushX = 0, pushY = 0;
		if ( separation != null && Count > 0 )
		{
			var (wx, wy) = Centre( Buffer[Index] );
			var near = Fixed16.Octile( X - wx, Y - wy ) <= Fixed16.One + Radius;
			if ( !((OnLastSegment || Arrived) && near) )
				(pushX, pushY) = separation( this );
		}

		var (ax, ay) = Fixed16.Truncate( wallX, wallY, maxForce );
		var (fx, fy) = Fixed16.Truncate( pathX, pathY, maxForce );
		var (sx, sy) = Fixed16.Truncate( pushX, pushY, maxForce );
		var totalX = Fixed16.Mul( ax, AvoidWallsWeight ) + Fixed16.Mul( fx, FollowPathWeight ) + Fixed16.Mul( sx, SeparationWeight );
		var totalY = Fixed16.Mul( ay, AvoidWallsWeight ) + Fixed16.Mul( fy, FollowPathWeight ) + Fixed16.Mul( sy, SeparationWeight );
		var (forceX, forceY) = Fixed16.Truncate( totalX, totalY, maxForce );
		// Mass ONE: FixDiv(force, ONE) is the identity.
		(VelocityX, VelocityY) = Fixed16.Truncate( VelocityX + forceX, VelocityY + forceY, maxSpeed );

		var newX = X + VelocityX;
		var newY = Y + VelocityY;
		if ( OffGrid || isOpen( newX >> 16, newY >> 16 ) )
			(X, Y) = (newX, newY);
		else
			History = ((History << 1) | 1) & 0x7fff;

		// Progress (0xff738) against the current waypoint: a history bit when it did not improve.
		if ( Count > 0 )
		{
			var remaining = Remaining();
			var bit = PreviousRemaining >= 0 && remaining >= PreviousRemaining ? 1 : 0;
			History = ((History << 1) | bit) & 0x7fff;
			PreviousRemaining = remaining;
		}
		return Arrived;
	}

	private int StuckBits() => System.Numerics.BitOperations.PopCount( (uint)(History & 0x7fff) );

	// ---- The tp.exe walk (Original) ---------------------------------------------------------------------------------

	[ThreadStatic] private static List<(int X, int Y)>? searchPath;

	/// <summary>The octile length between the centres of two waypoint cells (<c>0x50CD80</c>, then |dx| + |dy| − min / 2).</summary>
	private static int SegmentLength( (int X, int Y) a, (int X, int Y) b ) => Fixed16.Octile( (b.X - a.X) << 16, (b.Y - a.Y) << 16 );

	/// <summary>The cell of a 16.16 position as the original takes it (<c>0x5104B0</c>: the high halves).</summary>
	private static (int X, int Y) CellByte( int x, int y ) => ((x >> 16) & 0xFF, (y >> 16) & 0xFF);

	/// <summary><see cref="CellByte"/> for callers outside the walk (a route test that does not set the walk).</summary>
	public static (int X, int Y) CellByteOf( int x, int y ) => CellByte( x, y );

	/// <summary>
	/// SetDest (tp.exe <c>0x510100</c>): a walk to the 16.16 point (<paramref name="targetX"/>, <paramref name="targetY"/>) from the
	/// walker's cell, searched on <paramref name="map"/> with <see cref="Mask"/> (<see cref="WalkSearch"/>). False when the search
	/// fails: the walk then stands arrived and stuck. <paramref name="route"/> and <paramref name="alsoOpen"/> are the caller's own
	/// cells for the walk, kept as bookkeeping (<see cref="WalkCrossings"/> for the M3 gate) and not read by the step.
	/// </summary>
	public bool Navigate( ParkCellMap map, int targetX, int targetY, IReadOnlyList<(int X, int Y)>? route = null, IReadOnlyList<(int X, int Y)>? alsoOpen = null )
	{
		Original = true;
		Route = route?.ToArray() ?? [];
		Cursor = 0;
		WalkCrossings = Math.Max( 0, Route.Length - 1 );
		AlsoOpen = alsoOpen?.ToArray() ?? [];
		PreviousRemaining = -1;
		return Setup( map, targetX, targetY, reroute: false );
	}

	/// <summary>
	/// The walk set-up <c>0x50F8E0</c>: a search from the walker's cell to the target's; mode 1 (<paramref name="reroute"/>, the stuck
	/// and changed-ground reroutes) puts the current cell in front of the result. The first five waypoints are buffered and the
	/// lengths of the progress measure computed; the progress, its history, the waypoint index, the arrival and the stuck flag reset,
	/// and the walk takes the cell map's stamp counter. A failed search leaves the walk arrived and stuck with no waypoints.
	/// </summary>
	// [BIN:TPW-EXE:0x0050F8E0 walk set-up] start bytes = position >> 16, goal = target >> 16; 0x511420 with the mask +0xB4; on 0x70000000: +0x4C/+0x50 = target, +0x54, +0x58, +0xA0..+0xB0, +0x5C = 0, +0x60 = 1, +0xB8 = 1, return 0; mode 1 shifts the result one place and puts the position's cell first (count + 1 below 0xFC); +0x54 = count, +0x58 = min(count, 5); +0xA0 = octile(waypoint 0 − position) + every segment; +0x64.. the buffered centres (cell << 16 | 0x8000); +0x8C.. and +0xA4 the buffered segments; +0xA8 the segments beyond; +0xAC, +0xB0, +0x5C, +0x60, +0xB8 = 0; +0x48 = the stamp counter 0x4D8C50
	private bool Setup( ParkCellMap map, int targetX, int targetY, bool reroute )
	{
		var path = searchPath ??= new List<(int X, int Y)>();
		var start = CellByte( X, Y );
		var goal = CellByte( targetX, targetY );
		TargetX = targetX;
		TargetY = targetY;
		if ( !WalkSearch.Find( map, start, goal, Mask, path ) )
		{
			PathCount = 0;
			Count = 0;
			Total = Ahead = Beyond = Progress = History = 0;
			Index = 0;
			Arrived = true;
			Stuck = true;
			return false;
		}
		if ( reroute )
		{
			path.Insert( 0, start );
			if ( path.Count > 0xFC )
				path.RemoveAt( path.Count - 1 );
		}
		PathCount = path.Count;
		Count = Math.Min( PathCount, BufferedWaypoints );
		var (firstX, firstY) = Centre( path[0] );
		Total = Fixed16.Octile( firstX - X, firstY - Y );
		for ( var index = 0; index + 1 < PathCount; index++ )
			Total += SegmentLength( path[index], path[index + 1] );
		for ( var index = 0; index < Count; index++ )
			Buffer[index] = path[index];
		Ahead = 0;
		for ( var index = 0; index + 1 < Count; index++ )
			Ahead += SegmentLength( path[index], path[index + 1] );
		Beyond = 0;
		for ( var index = Count - 1; index + 1 < PathCount; index++ )
			Beyond += SegmentLength( path[index], path[index + 1] );
		Progress = 0;
		History = 0;
		Index = 0;
		Arrived = false;
		Stuck = false;
		Stamp = map.StampCounter;
		return true;
	}

	/// <summary>
	/// The progress <c>0x50FD40</c>: ONE once arrived or for a route of length 0, else ONE − trunc(remaining × ONE / <see cref="Total"/>)
	/// with the remaining length from the position to the current waypoint plus <see cref="Ahead"/> and <see cref="Beyond"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x0050FD40 progress] +0x60 == 1 gives 0x10000; remaining = octile(+0x64[+0x5C] − position) + +0xA4 + +0xA8; +0xA0 == 0 gives 0x10000; else 0x10000 − (remaining << 16) / +0xA0 (idiv)
	public int ProgressValue()
	{
		if ( Arrived )
			return Fixed16.One;
		var (wx, wy) = Centre( Buffer[Math.Clamp( Index, 0, BufferedWaypoints - 1 )] );
		var remaining = Fixed16.Octile( wx - X, wy - Y ) + Ahead + Beyond;
		if ( Total == 0 )
			return Fixed16.One;
		return unchecked(Fixed16.One - (int)(((long)remaining << 16) / Total));
	}

	/// <summary>
	/// avoid_walls <c>0x50E4A0</c> for a walker at (<paramref name="x"/>, <paramref name="y"/>) moving with (<paramref name="velocityX"/>,
	/// <paramref name="velocityY"/>): the probe is the position plus the velocity, the cell is the position's. The first edge of that
	/// cell in the direction of motion (zero counts as positive; +X or −X, then +Y or −Y) that <see cref="WalkEdges.Blocked"/> calls
	/// blocked and the probe penetrates within the radius pushes back with twice the penetration. Else the corner of the motion's
	/// quadrant pushes when its diagonal cell is cut off from one of the cell's two neighbours on that side while both connect to the
	/// cell: where the velocity's ray (direction FixDiv(v, |v|)) meets the circle of the radius around the corner within |v|, the force
	/// is (hit point − corner) / radius.
	/// </summary>
	// [BIN:TPW-EXE:0x0050E4A0 avoid_walls] probe 0x50F870 (position + FixMul(velocity, ONE)); cell 0x5104B0 of +8/+0xC; direction = velocity / length (0x50E080) unless the length is 0; edges +X (limit (cx + 1) << 16 − radius, probe above it), −X (cx << 16 + radius, probe below), +Y, −Y, the first pushing returns 2 × (limit − probe); corners by quadrant (+X+Y 0x50E75E, −X+Y 0x50E8B3, −X−Y 0x50EA07, +X−Y 0x50EB5E): the diagonal cell blocked towards the cell's row or column, both side cells open towards the cell, the ray 0x50CC20 hitting at t with 0 < t <= length gives FixDiv(position + FixMul(direction, t) − corner, radius)
	public static (int X, int Y) AvoidWallsOriginal( ParkCellMap map, int x, int y, int velocityX, int velocityY, int mask, int radius )
	{
		var probeX = unchecked(x + Fixed16.PcMul( velocityX, Fixed16.One ));
		var probeY = unchecked(y + Fixed16.PcMul( velocityY, Fixed16.One ));
		int cx = x >> 16, cy = y >> 16;
		int vx = unchecked(probeX - x), vy = unchecked(probeY - y);
		var length = Fixed16.Length( vx, vy );
		int dx = vx, dy = vy;
		if ( length != 0 )
			(dx, dy) = (Fixed16.PcDiv( vx, length ), Fixed16.PcDiv( vy, length ));
		var xPositive = dx >= 0;
		var yPositive = dy >= 0;
		bool Blocked( int cellX, int cellY, int direction ) => WalkEdges.Blocked( map, cellX, cellY, direction, mask );
		if ( xPositive && Blocked( cx, cy, 1 ) )
		{
			var limit = unchecked(((cx + 1) << 16) - radius);
			if ( probeX > limit )
				return (unchecked((limit - probeX) * 2), 0);
		}
		if ( !xPositive && Blocked( cx, cy, 3 ) )
		{
			var limit = unchecked((cx << 16) + radius);
			if ( probeX < limit )
				return (unchecked((limit - probeX) * 2), 0);
		}
		if ( yPositive && Blocked( cx, cy, 2 ) )
		{
			var limit = unchecked(((cy + 1) << 16) - radius);
			if ( probeY > limit )
				return (0, unchecked((limit - probeY) * 2));
		}
		if ( !yPositive && Blocked( cx, cy, 0 ) )
		{
			var limit = unchecked((cy << 16) + radius);
			if ( probeY < limit )
				return (0, unchecked((limit - probeY) * 2));
		}
		(int X, int Y)? Corner( (int X, int Y) diagonal, int first, int second, (int X, int Y) side1, int toCell1, (int X, int Y) side2, int toCell2, int cornerX, int cornerY )
		{
			if ( !Blocked( diagonal.X, diagonal.Y, first ) && !Blocked( diagonal.X, diagonal.Y, second ) )
				return null;
			if ( Blocked( side1.X, side1.Y, toCell1 ) || Blocked( side2.X, side2.Y, toCell2 ) )
				return null;
			var t = Ray( x, y, dx, dy, cornerX, cornerY, radius );
			if ( t <= 0 || t > length )
				return null;
			var hitX = unchecked(x + Fixed16.PcMul( dx, t ));
			var hitY = unchecked(y + Fixed16.PcMul( dy, t ));
			return (Fixed16.PcDiv( unchecked(hitX - cornerX), radius ), Fixed16.PcDiv( unchecked(hitY - cornerY), radius ));
		}
		(int X, int Y)? force = null;
		if ( yPositive && xPositive )
			force = Corner( (cx + 1, cy + 1), 0, 3, (cx, cy + 1), 0, (cx + 1, cy), 3, (cx + 1) << 16, (cy + 1) << 16 );
		if ( force == null && yPositive && !xPositive )
			force = Corner( (cx - 1, cy + 1), 0, 1, (cx, cy + 1), 0, (cx - 1, cy), 1, cx << 16, (cy + 1) << 16 );
		if ( force == null && !yPositive && !xPositive )
			force = Corner( (cx - 1, cy - 1), 2, 1, (cx - 1, cy), 1, (cx, cy - 1), 2, cx << 16, cy << 16 );
		if ( force == null && !yPositive && xPositive )
			force = Corner( (cx + 1, cy - 1), 2, 3, (cx + 1, cy), 3, (cx, cy - 1), 2, (cx + 1) << 16, cy << 16 );
		return force ?? (0, 0);
	}

	/// <summary>
	/// The ray test <c>0x50CC20</c>: where the ray from (x, y) along (dx, dy) meets the circle of <paramref name="radius"/> around the
	/// corner, as the original computes it (FixMul throughout, the square root of the 16.16 discriminant taken as an integer, the
	/// nearer root halved); <see cref="int.MinValue"/> when it misses.
	/// </summary>
	// [BIN:TPW-EXE:0x0050CC20 ray test] m = position − corner; a = d·d, b = 2 (d·m), c = m·m − r·r (FixMul each); disc = FixMul(b, b) − 4 FixMul(a, c); disc <= 0 returns 0x80000000; s = isqrt(disc) (0x4D49A0); min(s − b, −(s + b)) >> 1
	private static int Ray( int x, int y, int dx, int dy, int cornerX, int cornerY, int radius )
	{
		unchecked
		{
			int mx = x - cornerX, my = y - cornerY;
			var a = Fixed16.PcMul( dx, dx ) + Fixed16.PcMul( dy, dy );
			var b = (Fixed16.PcMul( dx, mx ) + Fixed16.PcMul( dy, my )) * 2;
			var c = Fixed16.PcMul( mx, mx ) + Fixed16.PcMul( my, my ) - Fixed16.PcMul( radius, radius );
			var discriminant = Fixed16.PcMul( b, b ) - Fixed16.PcMul( a, c ) * 4;
			if ( discriminant <= 0 )
				return int.MinValue;
			var root = (int)Math.Sqrt( (uint)discriminant );
			int near = root - b, far = -(root + b);
			return (far <= near ? far : near) >> 1;
		}
	}

	/// <summary>
	/// follow_path <c>0x50ED10</c>: the changed-ground reroute (the stamp of the walker's 16 × 16-cell block above the walk's), the stuck
	/// reroute (6 of the last 15 history bits while the progress is short of ONE), then the waypoint step: on the last waypoint arrival
	/// within 0.32 cell of the target and the arrival ramp towards it; before it, passing a waypoint within 0.4 cell, a refill by a
	/// new search once the five are used, and the pull towards the current waypoint's centre. Arrived (or no waypoints): −velocity.
	/// </summary>
	// [BIN:TPW-EXE:0x0050ED10 follow_path] stamp 0x4D8CD0(x >> 20, y >> 20) above +0x48 sets up again in mode 1 (failure: +0xB8 = 1, "The ground changed under this peep"); unless arrived, a remaining measure short of ONE with the 0x50FD40 progress short of ONE counts the history bits 0..14, (bits << 32) / 0xF0000 above 0x6665 sets up in mode 1 (failure: +0xB8 = 1); with waypoints and not arrived: index == +0x54 − 1 aims at +0x4C/+0x50 and arrives at octile < FixMul(radius, 0x19999); else a waypoint centre within octile < FixMul(radius, 0x20000) is passed (+0xA4 −= its segment), and the index reaching +0x58 sets up in mode 0; below +0x54 − 1 the force is (aim − position) − velocity, else the ramp min(FixDiv(|aim − position|, 2.0), +0x1C) along it less the velocity; otherwise −velocity (0x50F1A0)
	private (int X, int Y) FollowPathOriginal( ParkCellMap map, int maxSpeed )
	{
		if ( Stamp < map.BlockStamp( X >> 20, Y >> 20 ) )
		{
			Reroutes++;
			if ( !Setup( map, TargetX, TargetY, reroute: true ) )
				Stuck = true;
		}
		if ( !Arrived && ProgressValue() != Fixed16.One )
		{
			var ratio = (int)(((long)StuckBits() << 32) / 0xF0000);
			if ( ratio > 0x6665 )
			{
				Reroutes++;
				if ( !Setup( map, TargetX, TargetY, reroute: true ) )
					Stuck = true;
			}
		}
		if ( PathCount == 0 || Arrived )
			return (-VelocityX, -VelocityY);
		int goalX, goalY;
		if ( Index == PathCount - 1 )
		{
			(goalX, goalY) = (TargetX, TargetY);
			if ( Fixed16.Octile( goalX - X, goalY - Y ) < Fixed16.PcMul( Radius, ArrivalFactor ) )
				Arrived = true;
		}
		else
		{
			(goalX, goalY) = Centre( Buffer[Index] );
			if ( Fixed16.Octile( goalX - X, goalY - Y ) < Fixed16.PcMul( Radius, WaypointFactor ) )
			{
				Ahead -= Index + 1 < Count ? SegmentLength( Buffer[Index], Buffer[Index + 1] ) : 0;
				Index++;
				if ( Index == Count )
					Setup( map, TargetX, TargetY, reroute: false );
			}
			(goalX, goalY) = Centre( Buffer[Math.Min( Index, BufferedWaypoints - 1 )] );
		}
		var offsetX = goalX - X;
		var offsetY = goalY - Y;
		if ( Index < PathCount - 1 )
			return (Fixed16.PcMul( offsetX, Fixed16.One ) - VelocityX, Fixed16.PcMul( offsetY, Fixed16.One ) - VelocityY);
		var size = Fixed16.Length( offsetX, offsetY );
		if ( size == 0 )
			return (0, 0);
		var desired = Math.Min( Fixed16.PcDiv( size, RampDivisor ), maxSpeed );
		var scale = Fixed16.PcDiv( desired, size );
		return (Fixed16.PcMul( offsetX, scale ) - VelocityX, Fixed16.PcMul( offsetY, scale ) - VelocityY);
	}

	/// <summary>
	/// One step of an <see cref="Original"/> walk (<c>0x50F3B0</c>, through the walk step <c>0x4FA2A0</c>): the three forces with the
	/// original's arithmetic, the velocity, the position committed through <see cref="WalkEdges.Commit"/> (a refused step adds a
	/// history bit), then the progress and its history bit. Stuck when a search of the walk failed, else arrived when the progress is ONE.
	/// </summary>
	// [BIN:TPW-EXE:0x0050F3B0 steering step] behaviours in list order (avoid_walls 0xE666, follow_path 0x8000, separation), each truncated to +0x18 (0x50F600) and FixMul-weighted; the sum truncated to +0x18 and FixDiv by ONE; +0x10 = truncate(+0x10 + force, +0x1C); the commit 0x510510 from the old position's cell to the new one's with +0xB4 (refused: +0xB0 = +0xB0 << 1 | 1, the position kept); then +0xB0 = +0xB0 << 1 | (0x50FD40 <= +0xAC) and +0xAC = the progress
	// [BIN:TPW-EXE:0x004FA2A0 walk step] the steering step, then 2 when +0x18C (walk +0xB8) is set, 0 when 0x50FD40 is ONE, else 1
	private WalkResult StepOriginal( int maxSpeed, int maxForce, ParkCellMap map, Func<GuestNavigation, (int X, int Y)>? separation )
	{
		var (wallX, wallY) = AvoidWallsOriginal( map, X, Y, VelocityX, VelocityY, Mask, Radius );
		var (pathX, pathY) = FollowPathOriginal( map, maxSpeed );
		// separation (0x50E0E0): off on the last waypoint (or once arrived) within ONE + radius of the current waypoint's centre.
		int pushX = 0, pushY = 0;
		if ( separation != null )
		{
			var (wx, wy) = Centre( Buffer[Math.Clamp( Index, 0, BufferedWaypoints - 1 )] );
			var near = Fixed16.Octile( X - wx, Y - wy ) <= Fixed16.One + Radius;
			if ( !((Index == PathCount - 1 || Arrived) && near) )
				(pushX, pushY) = separation( this );
		}
		var (ax, ay) = Fixed16.PcTruncate( wallX, wallY, maxForce );
		var (fx, fy) = Fixed16.PcTruncate( pathX, pathY, maxForce );
		var (sx, sy) = Fixed16.PcTruncate( pushX, pushY, maxForce );
		var totalX = Fixed16.PcMul( ax, AvoidWallsWeight ) + Fixed16.PcMul( fx, FollowPathWeight ) + Fixed16.PcMul( sx, SeparationWeight );
		var totalY = Fixed16.PcMul( ay, AvoidWallsWeight ) + Fixed16.PcMul( fy, FollowPathWeight ) + Fixed16.PcMul( sy, SeparationWeight );
		var (forceX, forceY) = Fixed16.PcTruncate( totalX, totalY, maxForce );
		(forceX, forceY) = (Fixed16.PcDiv( forceX, Fixed16.One ), Fixed16.PcDiv( forceY, Fixed16.One ));
		(VelocityX, VelocityY) = Fixed16.PcTruncate( VelocityX + forceX, VelocityY + forceY, maxSpeed );
		var newX = unchecked(X + VelocityX);
		var newY = unchecked(Y + VelocityY);
		var from = ((int)((uint)X >> 16), (int)((uint)Y >> 16));
		var to = ((int)((uint)newX >> 16), (int)((uint)newY >> 16));
		// 0x510870: −1 for a cell outside the map (0..127; a smaller OpenTPW map ends at its own edge)
		var fromCell = map.InBounds( from.Item1, from.Item2 ) ? from : (-1, -1);
		(int X, int Y)? toCell = map.InBounds( to.Item1, to.Item2 ) ? to : null;
		if ( WalkEdges.Commit( map, fromCell, toCell, Mask ) )
			(X, Y) = (newX, newY);
		else
			History = ((History << 1) | 1) & 0x7fff;
		var progress = ProgressValue();
		History = ((History << 1) | (progress <= Progress ? 1 : 0)) & 0x7fff;
		Progress = progress;
		if ( Stuck )
			return WalkResult.Stuck;
		return progress == Fixed16.One ? WalkResult.Arrived : WalkResult.Walking;
	}

	/// <summary>
	/// The walk step for a guest: an <see cref="Original"/> walk steps on <paramref name="map"/>; any other (an explicit route) steps as
	/// <see cref="Step(int, int, Func{int, int, bool}, Func{GuestNavigation, (int X, int Y)}?)"/> with <paramref name="isOpen"/>.
	/// </summary>
	public WalkResult Step( int maxSpeed, int maxForce, ParkCellMap map, Func<int, int, bool> isOpen, Func<GuestNavigation, (int X, int Y)>? separation = null ) =>
		Original ? StepOriginal( maxSpeed, maxForce, map, separation )
			: Step( maxSpeed, maxForce, isOpen, separation ) ? WalkResult.Arrived : WalkResult.Walking;

	/// <summary>Every field the next step reads, for <see cref="WorldStateHash"/>.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( X );
		hash.Add( Y );
		hash.Add( VelocityX );
		hash.Add( VelocityY );
		hash.Add( TargetX );
		hash.Add( TargetY );
		hash.Add( Arrived );
		hash.Add( Index );
		hash.Add( Count );
		hash.Add( Cursor );
		hash.Add( History );
		hash.Add( PreviousRemaining );
		hash.Add( OffGrid );
		hash.Add( Route.Length );
		foreach ( var (x, y) in Route )
			hash.Add( x * 4096L + y );
		hash.Add( AlsoOpen.Length );
		foreach ( var (x, y) in AlsoOpen )
			hash.Add( x * 4096L + y );
		hash.Add( Mask );
		// The original walk's own fields join the digest for walks of that kind.
		if ( !Original )
			return;
		hash.Add( Stuck );
		hash.Add( Stamp );
		hash.Add( PathCount );
		hash.Add( Total );
		hash.Add( Ahead );
		hash.Add( Beyond );
		hash.Add( Progress );
		// Every slot: a failed set-up keeps the old ones and separation reads the first.
		foreach ( var (x, y) in Buffer )
			hash.Add( x * 4096L + y );
	}

	/// <summary>The original walk's fields for a snapshot, or null for a route walk.</summary>
	internal OriginalWalkSnapshot? CaptureOriginal() => !Original ? null : new OriginalWalkSnapshot
	{
		Stuck = Stuck,
		Stamp = Stamp,
		PathCount = PathCount,
		Total = Total,
		Ahead = Ahead,
		Beyond = Beyond,
		Progress = Progress
	};

	/// <summary>Restores what <see cref="CaptureOriginal"/> recorded (null: a route walk).</summary>
	internal void RestoreOriginal( OriginalWalkSnapshot? walk )
	{
		Original = walk != null;
		if ( walk == null )
		{
			Stuck = false;
			Stamp = PathCount = Total = Ahead = Beyond = Progress = 0;
			return;
		}
		Stuck = walk.Stuck;
		Stamp = walk.Stamp;
		PathCount = walk.PathCount;
		Total = walk.Total;
		Ahead = walk.Ahead;
		Beyond = walk.Beyond;
		Progress = walk.Progress;
	}
}

/// <summary>
/// The per-guest walk speed (docs/reverse/WALK-plan.md §4): base <c>+192</c> from {60, 80, 100, 120, 140}, toilet
/// term <c>+194</c> (25 while toilet &gt; 80), a decaying boost <c>+196</c>, the smoothed speed <c>+200</c> and the
/// caps the speed setter <c>0xffe38</c> derives from it. There is no energy factor.
/// </summary>
public static class GuestSpeed
{
	// [BIN:STP-PPC:0x100E48B8 base speed] +192 = data 0x4105e[rand % 5]: 60, 80, 100, 120, 140 (table data 0x41058)
	public static ReadOnlySpan<ushort> BaseSpeeds => [60, 80, 100, 120, 140];
	// [BIN:STP-PPC:0x100EF0BC toilet speed term] +194 = 25 while toilet +428 > 80, else 0
	public const int ToiletBonus = 25;
	public const int ToiletBonusLevel = 80;
	public const int Divisor = 100;
	/// <summary><c>UsageInfo.SpecialIngredient</c> 4 (Sugar, the ice cream shops): the only value that raises the speed boost.</summary>
	public const int SugarIngredient = 4;
	/// <summary>The ride build stores 50 as the amount of the special ingredient (<c>0xdab58</c>: ride +404, <c>mAmountOfSpecialIngredient</c>); the only way to change it is the original's shop screen.</summary>
	public const int DefaultIngredientAmount = 50;
	public const int Floor = 655;
	public const float InputCap = 2f;
	public const double MaxSpeedFactor = 0.2;
	public const double MaxForceFactor = 0.4;
	/// <summary>Largest speed the setter writes (trunc(<see cref="InputCap"/> × <see cref="MaxSpeedFactor"/>) in 16.16); the bound snapshot validation accepts.</summary>
	public const int VelocityBound = (int)(InputCap * MaxSpeedFactor * 65536.0);
	/// <summary>Largest force the setter writes (trunc(<see cref="InputCap"/> × <see cref="MaxForceFactor"/>) in 16.16); the bound snapshot validation accepts.</summary>
	public const int ForceBound = (int)(InputCap * MaxForceFactor * 65536.0);

	/// <summary>
	/// tp.exe <c>0x004FA870</c>: +0xC8 = ((+0xC0 + +0xC2 + +0xC4) / 100 + 3.0 × +0xC8) × 0.25, every operation rounded to single precision as the x87 runs at 24 mantissa bits (CW 0x7F): the
	/// sum is divided as an integer (fild, fidiv), the old speed is multiplied by −3.0 and subtracted (fmul, fsubp) and the result is scaled by 0.25; the multiply and the add are two roundings
	/// where the Mac's <c>fmadds</c> (<see cref="SmoothFused"/>) is one.
	/// </summary>
	// [BIN:TPW-EXE:0x004FA870 speed smoothing] the three words +0xC0 (base), +0xC2 (bonus) and +0xC4 (boost) are added as integers and divided by the word at 0x0075C7FC (100) with fidiv; +0xC8 is multiplied by 0x007006F0 (−3.0) and subtracted (fsubp), the difference multiplied by 0x007006F4 (0.25) and stored back and handed to the speed setter 0x00510190; then +0xC4 = (+0xC4 × 99 as a 16-bit product) / 100 (imul cx, cx, 0x63; the magic 0x51EB851F) and the walk position is copied to +0x190/+0x194
	public static float Smooth( float previous, int baseSpeed, int bonus, int boost )
	{
		var quotient = (baseSpeed + bonus + boost) / (float)Divisor;
		var tripled = 3f * previous;
		return (quotient + tripled) * 0.25f;
	}

	/// <summary>
	/// The Mac's <c>0xe6b28</c>: +200 = (3.0 × +200 + (+192 + +194 + +196) / 100) × 0.25 in single precision, the multiply-add one <c>fmadds</c> (<c>0xe6b9c</c>: exact product and sum, rounded
	/// once), so a plain <c>3f * s + q</c> differs in the last bit for about one input in five (PPC oracle finding F6, docs/reverse/ORACLE.md). Kept for the PPC oracle bridge; the PC walk is <see cref="Smooth"/>.
	/// </summary>
	// [BIN:STP-PPC:0x100E6B28 speed smoothing] float data 0x550c 3.0, 0x5508 0.25, divisor 100 (data 0x41062); 0xe6b9c fmadds
	public static float SmoothFused( float previous, int baseSpeed, int bonus, int boost ) =>
		MathF.FusedMultiplyAdd( 3f, previous, (baseSpeed + bonus + boost) / (float)Divisor ) * 0.25f;

	/// <summary>
	/// tp.exe <c>0x00510190</c>, the speed setter: (+0x1C max speed, +0x18 max force) = max(655, _ftol(min(s, 2) × 13107.2 / 26214.4)), the product of the single and the double constant rounded to
	/// a 24-bit mantissa by the x87 (<see cref="Single24.Product"/>) before the truncation. NaN is not capped and gives the floor, −∞ the floor, +∞ the cap.
	/// </summary>
	// [BIN:TPW-EXE:0x00510190 speed setter] fld s, fcomp 0x007009A0 (2.0), test ah, 0x41: a speed above 2.0 (not below, not equal, not unordered) becomes 2.0; +0x18 = _ftol(s × the double at 0x007009A8 = 26214.4), +0x1C = _ftol(s × the double at 0x007009B0 = 13107.2); each below 0x28F (655, signed) is raised to 655
	public static (int MaxSpeed, int MaxForce) Caps( float speed )
	{
		var s = float.IsNaN( speed ) || speed <= InputCap ? speed : InputCap;
		return (Math.Max( Floor, Single24.Truncate( Single24.Product( s, 13107.2 ) ) ), Math.Max( Floor, Single24.Truncate( Single24.Product( s, 26214.4 ) )));
	}

	/// <summary>
	/// The Mac's <c>0xffe38</c>: (+28 max speed, +24 max force) = max(655, trunc(min(s, 2) × 0.2 / 0.4 × 65536)) in double precision. NaN and −∞ give
	/// the floor (the conversion saturates below it), +∞ the cap, as the oracle found for the original. Kept for the PPC oracle bridge; the PC setter is <see cref="Caps"/>.
	/// </summary>
	// [BIN:STP-PPC:0x100FFE38 speed setter] input capped at 2.0 (data 0x5668), factors 0.2 / 0.4 (data 0x5650 / 0x5658), floor 655 on both
	public static (int MaxSpeed, int MaxForce) CapsMac( float speed )
	{
		var s = Math.Min( speed, InputCap );
		return (Math.Max( Floor, (int)(s * MaxSpeedFactor * 65536.0) ), Math.Max( Floor, (int)(s * MaxForceFactor * 65536.0) ));
	}

	/// <summary>
	/// <c>0xeb0cc..0xeb0f4</c> in the outcome of a completed use (<c>0xeaaf8</c>, called once from <c>0xe9ff8</c> when the guest leaves
	/// the object): an object whose type has special ingredient 4 (Sugar) adds trunc(6 × amount / 100) to the boost <c>+196</c>,
	/// a u16 store that wraps. The amount is the low byte of ride <c>+404</c> (<c>0xe2384</c>), 0..255, so one use adds at most 15
	/// (6 at the amount 100 the original's slider is thought to stop at). Any other ingredient leaves the boost alone (PPC oracle
	/// finding F20, <c>walk-boost</c>).
	/// </summary>
	// [BIN:STP-PPC:0x100EB0CC sugar boost] type +344 == 4: boost (+196, u16) += trunc((amount byte × 6) / 100) (multiply by the 0x51eb851f magic, shift 5)
	public static ushort RaiseBoost( ushort boost, int ingredient, int amount ) =>
		ingredient == SugarIngredient ? unchecked( (ushort)(boost + (amount & 0xFF) * 6 / Divisor) ) : boost;

	/// <summary>
	/// <c>0xe6bbc..0xe6bd8</c>: the boost decays by trunc(× 99 / 100) every update. The product is stored as a u16
	/// (<c>sth</c>) before the division, so above 661 it wraps: 662 decays to 0 (PPC oracle finding F6).
	/// </summary>
	public static ushort Decay( ushort boost ) => (ushort)((ushort)(boost * 99) / 100);

	/// <summary>
	/// One speed update as the person's turn update runs it (<c>0x004FA870</c>): smooth s, set the caps with the speed setter, then decay the boost.
	/// </summary>
	public static (float Speed, ushort Boost, int MaxSpeed, int MaxForce) Update( float speed, int baseSpeed, int bonus, ushort boost )
	{
		var smoothed = Smooth( speed, baseSpeed, bonus, boost );
		var (maxSpeed, maxForce) = Caps( smoothed );
		return (smoothed, Decay( boost ), maxSpeed, maxForce);
	}

	/// <summary>One speed update of the Mac's <c>0xe6b28</c> (<see cref="SmoothFused"/>): the seam the PPC oracle bridge compares (<c>guest_speed_smoothing</c>; <see cref="Caps"/> is <c>guest_speed_limits</c>).</summary>
	public static (float Speed, ushort Boost, int MaxSpeed, int MaxForce) UpdateFused( float speed, int baseSpeed, int bonus, ushort boost )
	{
		var smoothed = SmoothFused( speed, baseSpeed, bonus, boost );
		var (maxSpeed, maxForce) = CapsMac( smoothed );
		return (smoothed, Decay( boost ), maxSpeed, maxForce);
	}
}
