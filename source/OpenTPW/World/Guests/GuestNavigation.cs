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
}

/// <summary>
/// One guest's navigation object (original guest <c>+212</c>) and its steering step <c>0xfec9c</c>, run once per park
/// turn in the walking states (docs/reverse/WALK-plan.md §3, §5–§7, §11). Positions are 16.16 cells. The step sums
/// avoid_walls (0.9), follow_path (0.5) and separation (0.1), each truncated to the max force <c>+24</c>; the sum is
/// truncated again and added to the velocity, which is truncated to the max speed <c>+28</c>. A new destination does
/// not reset the velocity.
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
	/// <summary>Walking off the path grid (the arrival lanes): no walls and every position is accepted.</summary>
	// [APPROX:WALK-008] guests on the arrival lanes walk off the path grid, with no wall force and every position committed; the original walks them like every guest (route search 0x50f8e0, commit 0x510510 and edge test 0x4d8750 with the walk mask), the lane cells being type 0x1e or path from map init 0x536490, so only map-edge, entrance-building, water, ride and queue cells could push or refuse them — still open: lane guests are not walked through the edge test
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
	// [APPROX:WALK-004] routes are OpenTPW's cell sequences (one Grid.TryStep cell per destination, the queue cells in order, Grid.FindPath for the bin and wander walks) that start at the guest's own cell and are refilled five waypoints at a time from the stored route; the stuck reroute prepends the current cell to the rest; the original searches from the guest's cell at every SetDest (0x510100, setup 0x50f8e0, search 0x511420 = line-and-boundary search 0x511470 plus refinement 0x5108a0), again at each refill (0x50efce) and at the stuck reroute (0x50ee79, mode 1, current cell prepended), and ends the walk when a search fails (+0x60 = +0xb8 = 1, 0x50f945) — still open: the search 0x511470 and refinement 0x5108a0 are not ported
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
	/// One steering step. <paramref name="isOpen"/> tells whether a cell is walkable for the guest; it decides the
	/// walls and the position commit (ignored <see cref="OffGrid"/>). <paramref name="separation"/> returns the summed
	/// neighbour push (<see cref="Separation"/>) when the traced on/off rule asks for it; null means no neighbours.
	/// Returns the walk step's result: true when the guest has arrived, tested on the position at the start of the step
	/// (the step still moves).
	/// </summary>
	public bool Step( int maxSpeed, int maxForce, Func<int, int, bool> isOpen, Func<GuestNavigation, (int X, int Y)>? separation = null )
	{
		// avoid_walls (0xfdc70): probe = position + velocity; a blocked edge of the probe cell in the direction of motion
		// (zero counts as positive) within the radius pushes back with 2 x the penetration, perpendicular to the edge. The
		// edges are tried in the order +x, -x, +y, -y and the first that pushes is the whole force (each branch ends with
		// b 0xfe5f0: 0xfde78, 0xfdee4, 0xfdf64, 0xfdfe0).
		// [APPROX:WALK-002] avoid_walls (tp.exe 0x50e4a0, Mac 0xfdc70) models only its edge branch (the first pushing edge of +x, -x, +y, -y, 2 x the penetration) and takes the cell of the probe; the original takes the cell of the position (0x5104b0 on walk +8/+0xc, 0x50e4eb) and, when no edge pushes, runs one corner block for that cell's corner in the motion quadrant (0x50e75e, 0x50e8b3, 0x50ea07, 0x50eb5e) with the ray test 0x50cc20, which OpenTPW leaves out — still open: the corner branch and the position cell are not ported
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
		// [APPROX:WALK-007] no map-edit reroute: the original stamps the 16 x 16-cell block of a changed cell with a global counter (0x4d8c50/0x4d8c60, from 0x5346d0, 0x538fc0, 0x5348d0 and 0x4de1f0), and every walk step follow_path (0x50ed10) compares the stamp of the guest's block (0x4d8cd0) with the walk's +72 (0x50ed6c) and searches again (0x50f8e0 mode 1); a failed search sets +184 and ends the walk as arrived; OpenTPW reroutes only on the stuck rule — still open: no block stamps in OpenTPW
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
		// [APPROX:WALK-009] the progress history compares raw remaining distances inside the five-waypoint window and skips the first step after route setup; the original's progress is ONE - trunc(remaining x ONE / +160) (0x50fd40; ONE when arrived or +160 is 0), where +160 is the whole route's octile length from the position at setup or refill, compared (<=) with +172 (0x50f5c7), which setup and every refill reset to 0 together with the history +176 (0x50f8e0) — still open: the full-route length +160 and the resets are not ported (refill timing: WALK-004)
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
	/// <c>0xe6b28</c>: +200 = (3.0 × +200 + (+192 + +194 + +196) / 100) × 0.25 in single precision. The multiply-add is
	/// one <c>fmadds</c> (<c>0xe6b9c</c>: exact product and sum, rounded once), so a plain <c>3f * s + q</c> differs in
	/// the last bit for about one input in five (PPC oracle finding F6, docs/reverse/ORACLE.md).
	/// </summary>
	// [BIN:STP-PPC:0x100E6B28 speed smoothing] float data 0x550c 3.0, 0x5508 0.25, divisor 100 (data 0x41062); 0xe6b9c fmadds
	public static float Smooth( float previous, int baseSpeed, int bonus, int boost ) =>
		MathF.FusedMultiplyAdd( 3f, previous, (baseSpeed + bonus + boost) / (float)Divisor ) * 0.25f;

	/// <summary>
	/// <c>0xffe38</c>: (+28 max speed, +24 max force) = max(655, trunc(min(s, 2) × 0.2 / 0.4 × 65536)). NaN and −∞ give
	/// the floor (the conversion saturates below it), +∞ the cap, as the oracle found for the original.
	/// </summary>
	// [BIN:STP-PPC:0x100FFE38 speed setter] input capped at 2.0 (data 0x5668), factors 0.2 / 0.4 (data 0x5650 / 0x5658), floor 655 on both
	public static (int MaxSpeed, int MaxForce) Caps( float speed )
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
	/// One speed update <c>0xe6b28</c> as the guest update runs it: smooth s, set the caps with the speed setter
	/// <c>0xffe38</c>, then decay the boost. The seam the PPC oracle bridge compares (<c>guest_speed_smoothing</c>;
	/// <see cref="Caps"/> is <c>guest_speed_limits</c>).
	/// </summary>
	public static (float Speed, ushort Boost, int MaxSpeed, int MaxForce) Update( float speed, int baseSpeed, int bonus, ushort boost )
	{
		var smoothed = Smooth( speed, baseSpeed, bonus, boost );
		var (maxSpeed, maxForce) = Caps( smoothed );
		return (smoothed, Decay( boost ), maxSpeed, maxForce);
	}
}
