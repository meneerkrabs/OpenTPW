namespace OpenTPW;

/// <summary>
/// The numbers of a swim session (docs/POOLS.md, "Swimming"). All of them are OpenTPW's own: the original game has no pools, so there is nothing to measure them against.
/// </summary>
// [EXT:pools-swim] OpenTPW's own swim session; the original game has no pools
public static class PoolSwimSettings
{
	/// <summary>The shortest swim, in park turns (a turn is 0.248 s): 160 turns, about 40 seconds.</summary>
	public const int MinimumTurns = 160;
	/// <summary>A swim lasts <see cref="MinimumTurns"/> plus a draw below this: up to 120 more turns, so 40 to 70 seconds.</summary>
	public const int ExtraTurns = 120;
	/// <summary>The time a guest spends hidden in the changing room after climbing out, in park turns (12 turns, about 3 seconds).</summary>
	public const int ChangeOutTurns = 12;
	/// <summary>The happiness a finished swim adds (meter 0..100), on the turn the guest steps into the changing room.</summary>
	public const float HappinessGain = 12f;
	/// <summary>The swim speed is the guest's base speed (60, 80, 100, 120 or 140) times this, in 16.16 cells per park turn: 0.08 to 0.19 cells a turn, 0.33 to 0.78 cells a second.</summary>
	public const int SpeedPerBaseSpeed = 90;
	/// <summary>How far from the rim a swimmer's centre stays: the rim is a quarter of a cell and the body takes 0.12 more (16.16).</summary>
	public const int Margin = (int)(0.37 * Fixed16.One);
	/// <summary>Bodies a pool takes per water cell of its region (every body in the water counts: swimmers, floaters, guests under a jet); a guest released from a changing room while the water is full leaves the room on foot, as from a ride. One a cell, because a body keeps <see cref="MinimumDistance"/> from the next.</summary>
	public const int SwimmersPerWaterCell = 1;
	/// <summary>The capacity of the smallest pools: a region of one or two cells still takes this many swimmers (a strip of two cells has 1.26 cells of free length: two bodies at <see cref="MinimumDistance"/>).</summary>
	public const int MinimumCapacity = 2;
	/// <summary>Bodies closer than this steer away from each other (16.16 cells). A 2x swim sprite is about a cell long and 0.4 wide, so the push starts a cell and a fifth out.</summary>
	public const int AvoidRadius = (int)(1.2 * Fixed16.One);
	/// <summary>Bodies never move closer to each other than this (16.16 cells): 0.7 cell keeps two bodies side by side, 0.4 wide, clear on screen and leaves two in a row overlapping only at nose and tail; a move that increases the distance is always allowed.</summary>
	public const int MinimumDistance = (int)(0.7 * Fixed16.One);
	/// <summary>A wander goal is reached within this distance (16.16 cells).</summary>
	public const int GoalRadius = (int)(0.3 * Fixed16.One);
	/// <summary>Within this distance of the changing room's pool-side cell a swimmer on its way out ignores the others, so a crowd at the steps cannot block itself (16.16 cells).</summary>
	public const int CrowdedRadius = (int)(0.7 * Fixed16.One);
	/// <summary>The most heading steps (of 64) a swimmer turns in one park turn: 11.25 degrees.</summary>
	public const int MaximumTurn = 2;
	/// <summary>Headings of a full circle.</summary>
	public const int Headings = 64;
	/// <summary>The stroke of a swimmer: front crawl (sprite slot 8), breaststroke (9), treading water (10).</summary>
	public const int Strokes = 3;
	/// <summary>First sprite slot of the swim strokes in a swim sprite set (the guest sprite generator's slots 8, 9 and 10).</summary>
	public const int FirstSpriteSlot = 8;
}

/// <summary>
/// The water of one pool complex as a swimmer sees it: the 4-connected pool cells that touch a changing room's pool-side cell, and which points of them are free water. A cell's rim, a
/// quarter of a cell on every side that has no pool neighbour (and the corner square where two pool neighbours leave the diagonal empty), is the deck's coping, as the mesh builder
/// tiles it (<see cref="PoolEdges"/>); swimmers keep <see cref="PoolSwimSettings.Margin"/> from it.
/// </summary>
// [EXT:pools-swim] OpenTPW's own swim region; the original game has no pools
public sealed class PoolSwimRegion
{
	private readonly HashSet<(int X, int Y)> set;
	private readonly (int X, int Y)[] cells;

	private PoolSwimRegion( (int X, int Y) access, (int X, int Y)[] cells )
	{
		Access = access;
		this.cells = cells;
		set = cells.ToHashSet();
	}

	/// <summary>The changing room's pool-side cell, where swimmers enter and leave the water.</summary>
	public (int X, int Y) Access { get; }
	/// <summary>The cells of the region, row by row.</summary>
	public IReadOnlyList<(int X, int Y)> Cells => cells;
	/// <summary>The most swimmers the water holds at once: <see cref="PoolSwimSettings.SwimmersPerWaterCell"/> a cell, at least <see cref="PoolSwimSettings.MinimumCapacity"/>.</summary>
	public int Capacity => Math.Max( PoolSwimSettings.MinimumCapacity, cells.Length * PoolSwimSettings.SwimmersPerWaterCell );

	/// <summary>The region of the pool cells connected to <paramref name="access"/>, or null when it is not a pool cell.</summary>
	public static PoolSwimRegion? Build( ParkCellMap map, (int X, int Y) access )
	{
		ArgumentNullException.ThrowIfNull( map );
		if ( !map.InBounds( access.X, access.Y ) || map.RawTypeAt( access.X, access.Y ) != ParkCellTypes.PoolWater )
			return null;
		var seen = new HashSet<(int X, int Y)> { access };
		var pending = new Stack<(int X, int Y)>();
		pending.Push( access );
		while ( pending.Count > 0 )
		{
			var (x, y) = pending.Pop();
			foreach ( var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) } )
			{
				var next = (X: x + dx, Y: y + dy);
				if ( map.InBounds( next.X, next.Y ) && map.RawTypeAt( next.X, next.Y ) == ParkCellTypes.PoolWater && seen.Add( next ) )
					pending.Push( next );
			}
		}
		return new PoolSwimRegion( access, seen.OrderBy( cell => cell.Y ).ThenBy( cell => cell.X ).ToArray() );
	}

	public bool Contains( int x, int y ) => set.Contains( (x, y) );

	/// <summary>Whether a point (16.16 cells) is free water: in a region cell, at least <see cref="PoolSwimSettings.Margin"/> from a side without a pool neighbour and outside an inner corner's square.</summary>
	public bool IsWater( int x, int y )
	{
		int cx = x >> 16, cy = y >> 16;
		if ( !set.Contains( (cx, cy) ) )
			return false;
		int u = x & 0xFFFF, v = y & 0xFFFF, margin = PoolSwimSettings.Margin;
		bool north = set.Contains( (cx, cy - 1) ), south = set.Contains( (cx, cy + 1) ), west = set.Contains( (cx - 1, cy) ), east = set.Contains( (cx + 1, cy) );
		if ( (!north && v < margin) || (!south && v > Fixed16.One - margin) || (!west && u < margin) || (!east && u > Fixed16.One - margin) )
			return false;
		// an inner corner: both neighbours at the corner are pool but the diagonal between them is not, which leaves the corner square coping
		if ( north && west && !set.Contains( (cx - 1, cy - 1) ) && u < margin && v < margin )
			return false;
		if ( north && east && !set.Contains( (cx + 1, cy - 1) ) && u > Fixed16.One - margin && v < margin )
			return false;
		if ( south && west && !set.Contains( (cx - 1, cy + 1) ) && u < margin && v > Fixed16.One - margin )
			return false;
		if ( south && east && !set.Contains( (cx + 1, cy + 1) ) && u > Fixed16.One - margin && v > Fixed16.One - margin )
			return false;
		return true;
	}

	/// <summary>The centre of a cell as a 16.16 point.</summary>
	public static (int X, int Y) Centre( (int X, int Y) cell ) => (cell.X * Fixed16.One + Fixed16.One / 2, cell.Y * Fixed16.One + Fixed16.One / 2);
}

/// <summary>One swimmer's steering state: position (16.16 cells), heading (of <see cref="PoolSwimSettings.Headings"/>), the goal and the swimmer's own random stream.</summary>
public struct PoolSwimBody
{
	public int X, Y;
	public int Heading;
	public int GoalX, GoalY;
	public bool HasGoal;
	public uint Random;
}

/// <summary>
/// The steering of a swimmer, in integers so every machine agrees (docs/DETERMINISM.md): headings are 64 steps of a circle with a fixed table, positions are 16.16 cells, and the only
/// random numbers come from the swimmer's own xorshift stream. A swimmer swims to a goal point in the water, picks another when it gets there, steers away from nearby swimmers, turns
/// when its next point would leave the water, and never closes in on another swimmer below <see cref="PoolSwimSettings.MinimumDistance"/>.
/// </summary>
// [EXT:pools-swim] OpenTPW's own steering; the original game has no pools
public static class PoolSwimming
{
	// cos of 64 headings, 16.16; sin(i) = cos(i - 16)
	private static readonly int[] Cosines =
	{
		65536, 65220, 64277, 62714, 60547, 57798, 54491, 50660, 46341, 41576, 36410, 30893, 25080, 19024, 12785, 6424,
		0, -6424, -12785, -19024, -25080, -30893, -36410, -41576, -46341, -50660, -54491, -57798, -60547, -62714, -64277, -65220,
		-65536, -65220, -64277, -62714, -60547, -57798, -54491, -50660, -46341, -41576, -36410, -30893, -25080, -19024, -12785, -6424,
		0, 6424, 12785, 19024, 25080, 30893, 36410, 41576, 46341, 50660, 54491, 57798, 60547, 62714, 64277, 65220
	};

	/// <summary>The unit vector (16.16) of a heading: 0 is +X (east), 16 is +Y (south, the cell map's growing y).</summary>
	public static (int X, int Y) Direction( int heading ) => (Cosines[heading & 63], Cosines[(heading - 16) & 63]);

	/// <summary>The next value of a swimmer's random stream (xorshift32; never 0).</summary>
	public static uint Next( ref uint state )
	{
		var x = state == 0 ? 0x9E3779B9u : state;
		x ^= x << 13;
		x ^= x >> 17;
		x ^= x << 5;
		state = x;
		return x;
	}

	/// <summary>The heading closest to the vector (<paramref name="x"/>, <paramref name="y"/>); 0 for the zero vector.</summary>
	public static int Closest( int x, int y )
	{
		var best = 0;
		var bestDot = long.MinValue;
		for ( var heading = 0; heading < PoolSwimSettings.Headings; heading++ )
		{
			var (dx, dy) = Direction( heading );
			var dot = (long)dx * x + (long)dy * y;
			if ( dot > bestDot )
			{
				bestDot = dot;
				best = heading;
			}
		}
		return best;
	}

	/// <summary>The signed number of heading steps (-32..31) from <paramref name="from"/> to <paramref name="to"/>.</summary>
	public static int Difference( int from, int to ) => ((to - from + 32) & 63) - 32;

	/// <summary>A goal point in the water: a random cell of the region and a random point in it, the cell's centre when that point is not water.</summary>
	public static (int X, int Y) PickGoal( PoolSwimRegion region, ref uint random )
	{
		var cell = region.Cells[(int)(Next( ref random ) % (uint)region.Cells.Count)];
		var span = Fixed16.One - 2 * PoolSwimSettings.Margin;
		var x = cell.X * Fixed16.One + PoolSwimSettings.Margin + (int)(Next( ref random ) % (uint)span);
		var y = cell.Y * Fixed16.One + PoolSwimSettings.Margin + (int)(Next( ref random ) % (uint)span);
		return region.IsWater( x, y ) ? (x, y) : PoolSwimRegion.Centre( cell );
	}

	/// <summary>
	/// One park turn of a swimmer. It moves towards its goal (picked when it has none or has reached it), steers away from the swimmers in <paramref name="others"/> within
	/// <see cref="PoolSwimSettings.AvoidRadius"/> unless <paramref name="ignoreOthers"/>, turns at most <see cref="PoolSwimSettings.MaximumTurn"/> steps, and keeps to the water: when the point
	/// ahead is not free water it tries the headings either side in turn and, when none is free, stays and picks a new goal. Returns whether it moved.
	/// </summary>
	public static bool Step( PoolSwimRegion region, ref PoolSwimBody body, ReadOnlySpan<(int X, int Y)> others, int speed, bool ignoreOthers )
	{
		if ( !body.HasGoal || Fixed16.Octile( body.GoalX - body.X, body.GoalY - body.Y ) < PoolSwimSettings.GoalRadius )
		{
			var goal = PickGoal( region, ref body.Random );
			(body.GoalX, body.GoalY, body.HasGoal) = (goal.X, goal.Y, true);
		}
		// the wish: a unit step towards the goal plus a push away from every swimmer too close
		var (toX, toY) = (body.GoalX - body.X, body.GoalY - body.Y);
		var length = Fixed16.Length( toX, toY );
		(int X, int Y) wish = length == 0 ? (0, 0) : (Fixed16.Div( toX, length ), Fixed16.Div( toY, length ));
		var avoiding = false;
		if ( !ignoreOthers )
		{
			foreach ( var other in others )
			{
				var (awayX, awayY) = (body.X - other.X, body.Y - other.Y);
				var distance = Fixed16.Length( awayX, awayY );
				if ( distance >= PoolSwimSettings.AvoidRadius )
					continue;
				avoiding = true;
				if ( distance == 0 )
				{
					// on top of each other: each leaves along its own heading's side, so the two do not take the same way
					(awayX, awayY) = Direction( body.Heading + 16 );
					distance = Fixed16.One;
				}
				var weight = Fixed16.Div( PoolSwimSettings.AvoidRadius - distance, PoolSwimSettings.AvoidRadius ) * 2;
				wish.X += Fixed16.Mul( Fixed16.Div( awayX, distance ), weight );
				wish.Y += Fixed16.Mul( Fixed16.Div( awayY, distance ), weight );
			}
		}
		if ( wish != (0, 0) )
			body.Heading = (body.Heading + Math.Clamp( Difference( body.Heading, Closest( wish.X, wish.Y ) ), -PoolSwimSettings.MaximumTurn, PoolSwimSettings.MaximumTurn )) & 63;
		var random = Next( ref body.Random );
		// a little drift, so the stroke does not follow a ruler
		if ( !avoiding && (random & 7) == 0 )
			body.Heading = (body.Heading + ((random & 8) == 0 ? 1 : -1)) & 63;
		var order = (random & 16) == 0 ? 1 : -1;
		for ( var attempt = 0; attempt <= 2 * 16; attempt++ )
		{
			// attempt 0 keeps the heading, then 1, -1, 2, -2 ... steps to either side (the side drawn per turn)
			var offset = attempt == 0 ? 0 : ((attempt + 1) / 2) * ((attempt & 1) == 1 ? order : -order);
			var heading = (body.Heading + offset) & 63;
			var (dx, dy) = Direction( heading );
			int nextX = body.X + Fixed16.Mul( dx, speed ), nextY = body.Y + Fixed16.Mul( dy, speed );
			if ( !region.IsWater( nextX, nextY ) || (!ignoreOthers && TooClose( body.X, body.Y, nextX, nextY, others )) )
				continue;
			(body.X, body.Y, body.Heading) = (nextX, nextY, heading);
			return true;
		}
		// boxed in: stand still and look for another way
		body.HasGoal = false;
		return false;
	}

	/// <summary>
	/// [EXT:pools-waves] Carries a swimmer by (<paramref name="dx"/>, <paramref name="dy"/>) (16.16 cells; the push of the waves) when the point it lands on is free water and not within
	/// <see cref="PoolSwimSettings.MinimumDistance"/> of another body it was not already inside; otherwise it stays (a swimmer pressed against the wall or a crowd is not pushed through it). Returns whether it moved.
	/// </summary>
	public static bool Drift( PoolSwimRegion region, ref PoolSwimBody body, int dx, int dy, ReadOnlySpan<(int X, int Y)> others )
	{
		if ( dx == 0 && dy == 0 )
			return false;
		int nextX = body.X + dx, nextY = body.Y + dy;
		if ( !region.IsWater( nextX, nextY ) || TooClose( body.X, body.Y, nextX, nextY, others ) )
			return false;
		(body.X, body.Y) = (nextX, nextY);
		return true;
	}

	/// <summary>Whether moving from (<paramref name="x"/>, <paramref name="y"/>) to the next point brings the swimmer within <see cref="PoolSwimSettings.MinimumDistance"/> of another it was not already inside.</summary>
	private static bool TooClose( int x, int y, int nextX, int nextY, ReadOnlySpan<(int X, int Y)> others )
	{
		foreach ( var other in others )
		{
			var after = Fixed16.Length( nextX - other.X, nextY - other.Y );
			if ( after < PoolSwimSettings.MinimumDistance && after < Fixed16.Length( x - other.X, y - other.Y ) )
				return true;
		}
		return false;
	}

	/// <summary>The sprite slot of a stroke (0 front crawl, 1 breaststroke, 2 treading water): 8, 9 or 10.</summary>
	public static int SpriteSlot( int stroke ) => PoolSwimSettings.FirstSpriteSlot + Math.Clamp( stroke, 0, PoolSwimSettings.Strokes - 1 );
}
