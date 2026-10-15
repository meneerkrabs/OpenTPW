using System.Globalization;

namespace OpenTPW;

/// <summary>Why the Buy Land validator refused a rectangle (<see cref="LandPurchaseRefusal.None"/>: allowed).</summary>
public enum LandPurchaseRefusal
{
	None,
	/// <summary>A corner lies outside the map.</summary>
	OutsideMap,
	/// <summary>The rectangle spans two cells or less along an axis.</summary>
	TooSmall,
	/// <summary>The new fence would run through a gate post (its top or bottom edge on the post's row, or its left or right edge on the post's column).</summary>
	GatePost,
	/// <summary>Two consecutive fence posts of an edge stand on cells of type 7 or 0x1e.</summary>
	Barrier,
	/// <summary>The rectangle meets the park's fence at one corner only.</summary>
	TouchesAtOnePoint,
	/// <summary>The rectangle holds no owned or no unowned land and no side runs along the fence.</summary>
	NoNewLand,
	/// <summary>The merged park would enclose land it does not own.</summary>
	Pocket,
	/// <summary>The rectangle covers the open side of the park gate (not modelled).</summary>
	Gate,
	/// <summary>The merged fence cannot be walked from post A to post B (not modelled).</summary>
	Ring,
	/// <summary>The land costs more than the bank balance.</summary>
	Money
}

/// <summary>The Buy Land validator's verdict: the refusal (if any), the cells it charges for and their price.</summary>
public readonly record struct LandPurchaseCheck( LandPurchaseRefusal Refusal, int Cells, long Cost )
{
	public bool Allowed => Refusal == LandPurchaseRefusal.None;
}

/// <summary>Where a level's park fence starts and how the shipped <c>Hoardings.sam</c> lays it.</summary>
/// <param name="PostA">The fence's first post, three cells east of the entrance's B cell.</param>
/// <param name="PostB">The fence's last post, four cells west of the entrance's A cell.</param>
/// <param name="Clicks">The <c>HoardingClicks</c> offsets from post A, without the 512 terminator.</param>
public sealed record ParkLandSetup( (int X, int Y) PostA, (int X, int Y) PostB, IReadOnlyList<(int X, int Y)> Clicks )
{
	/// <summary>The setup of a level from its entrance cells and its <c>Hoardings.sam</c> text; null when either is missing or unusable.</summary>
	// [BIN:TPW-EXE:0x5323f0 park fence] post B is the entrance's first cell (0x7855bc/0x7855c0, FixedItemInfo.EntranceA) moved 4 cells west, post A its second cell (0x7855c4/0x7855c8, EntranceB) moved 3 cells east (0x4d8690 gives both cells)
	public static ParkLandSetup? FromLevel( (int X, int Y)? entranceA, (int X, int Y)? entranceB, string? hoardingsText )
	{
		if ( entranceA is not { } a || entranceB is not { } b || hoardingsText == null )
			return null;
		var clicks = ParkLand.ParseHoardingClicks( hoardingsText );
		return clicks.Count == 0 ? null : new ParkLandSetup( (b.X + 3, b.Y), (a.X - 4, a.Y), clicks );
	}
}

/// <summary>
/// The park's land (tp.exe's hoardings, docs/reverse/PATH-plan.md "PATH-LAND"): the fence ring of posts from post A, east
/// of the gate, round the park to post B, west of it, the lattice squares it encloses (closed along the gate), and the
/// rules that turn the ring into owned cells (cell flag 0x40 clear) and that buy more land. Immutable: a purchase
/// returns a new land. Fence posts stand on a lattice of every second cell anchored on post A; a square is the 2 × 2
/// cells between four posts, named by its smallest corner post.
/// </summary>
public sealed class ParkLand
{
	/// <summary>The <c>HoardingClicks</c> X value that ends the list.</summary>
	public const int ClicksEnd = 512;
	/// <summary>The click table's capacity: 64 (X, Y) pairs from 0x784e3c up to 0x78503b.</summary>
	public const int MaximumClicks = 64;
	/// <summary>Upper bound of posts in a ring (a fence round the whole 128 × 128 map has 256).</summary>
	public const int MaximumPosts = 4096;
	/// <summary>The size of the map the half-resolution land grid of the validator covers (64 × 64 blocks of 2 × 2 cells).</summary>
	public const int MapSize = 128;

	private const int North = 1, East = 4, South = 0x10, West = 0x40;
	private readonly (int X, int Y)[] ring;
	private readonly HashSet<(int X, int Y)> posts;
	private readonly HashSet<(int X, int Y)> squares;

	/// <summary>A land from its ring: every post from <paramref name="postA"/> to <paramref name="postB"/>, two cells apart along one axis.</summary>
	public ParkLand( (int X, int Y) postA, (int X, int Y) postB, IReadOnlyList<(int X, int Y)> ring )
	{
		ArgumentNullException.ThrowIfNull( ring );
		if ( ring.Count < 2 || ring.Count > MaximumPosts )
			throw new InvalidDataException( $"A park fence has {ring.Count} posts." );
		if ( ring[0] != postA || ring[^1] != postB )
			throw new InvalidDataException( "A park fence must run from post A to post B." );
		if ( postA == postB || ((postA.X - postB.X) & 1) != 0 || ((postA.Y - postB.Y) & 1) != 0 )
			throw new InvalidDataException( "The park fence's gate posts are not on one post lattice." );
		for ( var index = 0; index < ring.Count; index++ )
		{
			var (x, y) = ring[index];
			if ( x < 0 || y < 0 || x >= 1024 || y >= 1024 )
				throw new InvalidDataException( $"Fence post ({x}, {y}) is outside the map." );
			if ( index > 0 )
			{
				var (px, py) = ring[index - 1];
				if ( Math.Abs( x - px ) + Math.Abs( y - py ) != 2 || (x != px && y != py) )
					throw new InvalidDataException( $"Fence posts ({px}, {py}) and ({x}, {y}) are not neighbours." );
			}
		}
		PostA = postA;
		PostB = postB;
		this.ring = ring.ToArray();
		posts = this.ring.ToHashSet();
		squares = Enclosed( this.ring );
		// the walks and the fill keep the land on the right of the heading, as every shipped fence does (east from post A, then round)
		var (dx, dy) = (ring[1].X - postA.X, ring[1].Y - postA.Y);
		var first = dx > 0 ? East : dx < 0 ? West : dy > 0 ? South : North;
		if ( !IsBoundary( squares, postA, first ) )
			throw new InvalidDataException( "The park fence does not keep the park on its right from post A." );
	}

	/// <summary>The fence's first post (the ring's start, east of the gate; the walks start here heading east).</summary>
	public (int X, int Y) PostA { get; }
	/// <summary>The fence's last post (west of the gate).</summary>
	public (int X, int Y) PostB { get; }
	/// <summary>Every post from <see cref="PostA"/> to <see cref="PostB"/> in walking order; a post the fence passes twice is listed twice.</summary>
	public IReadOnlyList<(int X, int Y)> Ring => ring;
	/// <summary>The enclosed squares by their smallest corner post.</summary>
	public IReadOnlyCollection<(int X, int Y)> Squares => squares;

	/// <summary>Whether a fence post (a hoarding, cell type 0x19) stands on cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
	public bool IsPost( int x, int y ) => posts.Contains( (x, y) );

	/// <summary>The <c>HoardingClicks[n].X/Y</c> pairs of a <c>Hoardings.sam</c>, up to the 512 terminator, a missing pair or 64 pairs.</summary>
	// [BIN:TPW-EXE:0x407f20 Hoardings loader] the theme's hoardings.sam (name 0x74782c, string object 0x786bd8) fills the click table 0x784e30, whose pairs start at 0x784e3c; endhoardings.sam fills a second table 0x784c20 the park fence does not read
	// [BIN:TPW-EXE:0x532506 fence clicks] the fence tool (mode 0x19) gets one click at post A + each pair until a pair's X is 512 or after the 64th pair (bound 0x78503c)
	public static IReadOnlyList<(int X, int Y)> ParseHoardingClicks( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		var values = new Dictionary<string, string[]>( StringComparer.OrdinalIgnoreCase );
		GuestSettings.ParseSam( text, values );
		var clicks = new List<(int X, int Y)>();
		for ( var index = 0; index < MaximumClicks; index++ )
		{
			if ( !values.TryGetValue( $"HoardingClicks[{index}].X", out var xs ) || !values.TryGetValue( $"HoardingClicks[{index}].Y", out var ys )
				|| !int.TryParse( xs[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x ) || !int.TryParse( ys[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y ) )
				break;
			if ( x == ClicksEnd )
				break;
			clicks.Add( (x, y) );
		}
		return clicks;
	}

	/// <summary>
	/// The fence a new park starts with: a straight run of posts from each click to the next, the first click on post A and
	/// the last on post B. Throws <see cref="InvalidDataException"/> when the clicks do not draw such a fence.
	/// </summary>
	public static ParkLand FromClicks( ParkLandSetup setup )
	{
		ArgumentNullException.ThrowIfNull( setup );
		var points = setup.Clicks.Select( click => (X: setup.PostA.X + click.X, Y: setup.PostA.Y + click.Y) ).ToArray();
		if ( points.Length < 2 || points[0] != setup.PostA )
			throw new InvalidDataException( "The fence's first click is not on post A." );
		var ring = new List<(int X, int Y)> { points[0] };
		for ( var index = 1; index < points.Length; index++ )
		{
			var (from, to) = (points[index - 1], points[index]);
			if ( from == to )
				continue;
			if ( (from.X != to.X && from.Y != to.Y) || ((to.X - from.X) & 1) != 0 || ((to.Y - from.Y) & 1) != 0 )
				throw new InvalidDataException( $"The fence click ({to.X}, {to.Y}) is not on a straight post line from ({from.X}, {from.Y})." );
			var (dx, dy) = (Math.Sign( to.X - from.X ) * 2, Math.Sign( to.Y - from.Y ) * 2);
			for ( var post = (X: from.X + dx, Y: from.Y + dy); ; post = (post.X + dx, post.Y + dy) )
			{
				ring.Add( post );
				if ( post == to || ring.Count > MaximumPosts )
					break;
			}
		}
		if ( ring[^1] != setup.PostB )
			throw new InvalidDataException( "The fence's last click is not on post B." );
		return new ParkLand( setup.PostA, setup.PostB, ring );
	}

	/// <summary>
	/// The fence of an imported save: its hoarding posts walked from post A by their neighbour bits
	/// (<c>mHoardingNeighbours</c>; 1 north, 4 east, 16 south, 64 west), the leftmost branch first, up to post B.
	/// Null when the walk does not reach post B.
	/// </summary>
	// [BIN:TPW-EXE:0x52ba50 ring walk] the walk starts on post A heading east (bit 4); a corner turns to its other link, a junction (0x53ae90) takes the first link counting from the left of the heading (0x4d8c20 with 0x40) and turning right (0x4d8c20 with 4); it ends on post B
	public static ParkLand? FromFenceLinks( (int X, int Y) postA, (int X, int Y) postB, Func<int, int, byte?> links )
	{
		ArgumentNullException.ThrowIfNull( links );
		var ring = new List<(int X, int Y)> { postA };
		var (position, heading) = (postA, East);
		while ( ring.Count <= MaximumPosts )
		{
			if ( position == postB && ring.Count > 1 )
				return new ParkLand( postA, postB, ring );
			if ( links( position.X, position.Y ) is not { } bits )
				return null;
			var next = Turns( heading ).FirstOrDefault( turn => (bits & turn) != 0 );
			if ( next == 0 )
				return null;
			heading = next;
			position = Step( position, heading );
			ring.Add( position );
		}
		return null;
	}

	/// <summary>
	/// A new park's ownership: every cell but the fixed (<see cref="ParkCellFlags.NoModify"/>) ones is unowned, then the fence's
	/// land and the strip in front of the gate are owned.
	/// </summary>
	// [BIN:TPW-EXE:0x536490 map init] flag 0x40 is set on every cell whose flags lack 0x20 (0x53654b, 0x5365ad); the level start 0x52f050 runs it on every cell before it lays the fence (0x5323f0)
	// [BIN:TPW-EXE:0x5325a4 gate strip] after the fill, 0x40 is cleared on the two cells (x, A.y) and (x, A.y + 1) for every x from post B + 2 up to post A − 1
	public void ApplyToNewPark( ParkCellMap cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				var flags = cells.FlagsAt( x, y );
				if ( (flags & (ParkCellFlags.NoModify | ParkCellFlags.Unowned)) == 0 )
					cells.SetFlags( x, y, flags | ParkCellFlags.Unowned );
			}
		}
		ClaimEnclosedCells( cells );
		for ( var x = PostB.X + 2; x < PostA.X; x++ )
		{
			Claim( cells, x, PostA.Y );
			Claim( cells, x, PostA.Y + 1 );
		}
	}

	/// <summary>Clears the unowned flag on every cell the fence encloses (cells are never made unowned here).</summary>
	// [BIN:TPW-EXE:0x5311c0 fence fill] the fill walks the ring from post A (posts marked with thing flag 0x20 by 0x531040) and from every post scans to the right of the heading two cells at a time, clearing 0x40 on 2 × 2 cells (offsets (0,1), (1,1), (1,0) from 0x7cdba8, 0x7cdbb8, 0x7cdbc8) until it meets a marked post; it never sets 0x40
	// [APPROX:PATH-007] the fill's cell outcome is modelled, not its scan: a cell (x, y) is owned when the point (x − ½, y − ½) lies inside the ring closed along the gate, and so are the four cells round every ring corner that turns away from the land (three of its four squares enclosed); this reproduces the Jungle Easymode save's 0x40 flags on all 16,384 cells from that save's own fence — still open: the scan's 2 × 2 stepping (0x5311c0) is not reconciled with that outcome, and the six-click fence the fence setup lays instead when bit 0x400 of the level word 0x79d86c is set (meaning not traced) is not modelled
	public void ClaimEnclosedCells( ParkCellMap cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		foreach ( var (sx, sy) in squares )
		{
			for ( var dy = 1; dy <= 2; dy++ )
				for ( var dx = 1; dx <= 2; dx++ )
					Claim( cells, sx + dx, sy + dy );
		}
		var corners = new HashSet<(int X, int Y)>();
		foreach ( var (sx, sy) in squares )
			for ( var dy = 0; dy <= 2; dy += 2 )
				for ( var dx = 0; dx <= 2; dx += 2 )
					corners.Add( (sx + dx, sy + dy) );
		foreach ( var (vx, vy) in corners )
		{
			var enclosed = (squares.Contains( (vx - 2, vy - 2) ) ? 1 : 0) + (squares.Contains( (vx, vy - 2) ) ? 1 : 0)
				+ (squares.Contains( (vx - 2, vy) ) ? 1 : 0) + (squares.Contains( (vx, vy) ) ? 1 : 0);
			if ( enclosed != 3 )
				continue;
			for ( var dy = 0; dy <= 1; dy++ )
				for ( var dx = 0; dx <= 1; dx++ )
					Claim( cells, vx + dx, vy + dy );
		}
	}

	private static void Claim( ParkCellMap cells, int x, int y )
	{
		if ( !cells.InBounds( x, y ) )
			return;
		var flags = cells.FlagsAt( x, y );
		if ( flags.HasFlag( ParkCellFlags.Unowned ) )
			cells.SetFlags( x, y, flags & ~ParkCellFlags.Unowned );
	}

	/// <summary>
	/// The Buy Land validator for the rectangle between the fence posts <paramref name="first"/> (the stored corner) and
	/// <paramref name="second"/> (the pointer): the original's refusals in its order, then the price of the land it adds.
	/// </summary>
	/// <param name="barrier">Whether a cell is of the original types 7 or 0x1e (no terrain, or a fixed walkway).</param>
	/// <param name="cellCost"><c>Costs.MapCell</c>.</param>
	/// <param name="balance">The bank balance for the money test; null skips it.</param>
	// [BIN:TPW-EXE:0x531a50 Buy Land validator] the corners must be on the map (0x4d8300) and more than two cells apart on both axes; the top and bottom edges, then the left and right edges, are walked a post at a time: an edge post on gate post A (0x7639f0/f4) or B (0x7639f8/fc) refuses it, two consecutive posts of an edge on cell type 7 or 0x1e (0x536360, not type 2) refuse it, and every hoarding (thing type 0x19, 0x53ad60) on an edge is counted
	// [BIN:TPW-EXE:0x531cf2 straight hoardings] a hoarding on an edge counts as crossing it only when it is a straight piece across the edge (0x53ae50 with links 0x11 or 0x44), read from the link byte +0xc (0x522770 while 0x81b4cc is clear, as it is outside the fence setup and the purchase); the hoardings keep their links in +0x22 (mHoardingNeighbours), so every hoarding on an edge counts as touching it, and these counts stay 0 here
	// [BIN:TPW-EXE:0x53202a touch test] more than one touching hoarding on the top (left) edge counts one crossing there, or, when the first corner holds a hoarding, one less on the bottom (right) edge; more than one on the bottom (right) edge counts one there, or one less on the top (left) edge when the second corner holds a hoarding; exactly one corner on a hoarding and no crossing refuses the rectangle, and so does the type 7/0x1e run
	// [BIN:TPW-EXE:0x5320f8 new land] every cell of the rectangle counts as unowned when it has flag 0x40 and no hoarding, else as owned; a rectangle with no owned or no unowned cell is refused unless an edge holds two or more hoardings
	public LandPurchaseCheck CheckPurchase( ParkCellMap cells, (int X, int Y) first, (int X, int Y) second, Func<int, int, bool> barrier, long cellCost, long? balance )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( barrier );
		var (xmin, xmax) = (Math.Min( first.X, second.X ), Math.Max( first.X, second.X ));
		var (ymin, ymax) = (Math.Min( first.Y, second.Y ), Math.Max( first.Y, second.Y ));
		if ( !cells.InBounds( xmin, ymin ) || !cells.InBounds( xmax, ymax ) )
			return Refused( LandPurchaseRefusal.OutsideMap );
		if ( xmax - xmin <= 2 || ymax - ymin <= 2 )
			return Refused( LandPurchaseRefusal.TooSmall );
		var barrierRun = false;
		var corners = 0;
		var (topFences, bottomFences, leftFences, rightFences) = (0, 0, 0, 0);
		var (topTouch, bottomTouch, leftTouch, rightTouch) = (0, 0, 0, 0);
		void Edge( int x, int y, ref int run, ref int fences, ref int touch, bool corner )
		{
			if ( barrier( x, y ) )
			{
				run++;
				if ( run > 1 )
				{
					barrierRun = true;
					run = 0;
				}
			}
			else
				run = 0;
			if ( !IsPost( x, y ) )
				return;
			fences++;
			if ( corner )
				corners++;
			touch++;
		}
		var (runA, runB) = (0, 0);
		for ( var x = xmin; x <= xmax; x += 2 )
		{
			if ( x == PostA.X && (ymin == PostA.Y || ymax == PostA.Y) || x == PostB.X && (ymin == PostB.Y || ymax == PostB.Y) )
				return Refused( LandPurchaseRefusal.GatePost );
			Edge( x, ymin, ref runA, ref topFences, ref topTouch, x == xmin || x == xmax );
			Edge( x, ymax, ref runB, ref bottomFences, ref bottomTouch, x == xmin || x == xmax );
		}
		(runA, runB) = (0, 0);
		for ( var y = ymin; y <= ymax; y += 2 )
		{
			if ( (xmin == PostA.X || xmax == PostA.X) && y == PostA.Y || (xmin == PostB.X || xmax == PostB.X) && y == PostB.Y )
				return Refused( LandPurchaseRefusal.GatePost );
			Edge( xmin, y, ref runA, ref leftFences, ref leftTouch, y == ymin || y == ymax );
			Edge( xmax, y, ref runB, ref rightFences, ref rightTouch, y == ymin || y == ymax );
		}
		// The crossing counts start at 0 (see above) and only the touch adjustments move them.
		var (top, bottom, left, right) = (0, 0, 0, 0);
		var firstFence = IsPost( first.X, first.Y );
		var secondFence = IsPost( second.X, second.Y );
		if ( topTouch > 1 )
		{
			if ( firstFence )
				bottom--;
			else
				top++;
		}
		if ( leftTouch > 1 )
		{
			if ( firstFence )
				right--;
			else
				left++;
		}
		if ( bottomTouch > 1 )
		{
			if ( secondFence )
				top--;
			else
				bottom++;
		}
		if ( rightTouch > 1 )
		{
			if ( secondFence )
				left--;
			else
				right++;
		}
		if ( corners == 2 && top == 0 && bottom == 0 && left == 0 && right == 0 )
			return Refused( LandPurchaseRefusal.TouchesAtOnePoint );
		if ( barrierRun )
			return Refused( LandPurchaseRefusal.Barrier );
		var (owned, unowned) = (0, 0);
		for ( var y = ymin; y <= ymax; y++ )
		{
			for ( var x = xmin; x <= xmax; x++ )
			{
				if ( cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) && !IsPost( x, y ) )
					unowned++;
				else
					owned++;
			}
		}
		if ( !(owned != 0 && unowned != 0) && topFences < 2 && bottomFences < 2 && leftFences < 2 && rightFences < 2 )
			return Refused( LandPurchaseRefusal.NoNewLand );
		if ( LeavesPocket( cells, xmin, ymin, xmax, ymax ) )
			return Refused( LandPurchaseRefusal.Pocket );
		// [APPROX:PATH-007] a rectangle that covers a square on the open side of the gate (between posts B and A) is refused, and so is one whose merged fence cannot be walked from post A to post B; the original's validator accepts some rectangles in front of the gate (e.g. Jungle (45, 11)-(49, 17)) and its purchase lays the fence across that ground (0x5327e0); reading the code, the ring walk 0x52ba50 then likely keeps the old ring and the refill drops the new hoardings (the land is charged but none is gained), or the walk may not terminate — still open: that outcome is not confirmed at run time
		if ( CoversGate( xmin, ymin, xmax, ymax ) )
			return Refused( LandPurchaseRefusal.Gate );
		if ( Merge( first, second ) == null )
			return Refused( LandPurchaseRefusal.Ring );
		var (count, total) = Price( cells, xmin, ymin, xmax, ymax, cellCost, balance, out var tooDear );
		return new LandPurchaseCheck( tooDear ? LandPurchaseRefusal.Money : LandPurchaseRefusal.None, count, total );

		static LandPurchaseCheck Refused( LandPurchaseRefusal refusal ) => new( refusal, 0, 0 );
	}

	/// <summary>
	/// The price of a rectangle: <c>Costs.MapCell</c> for every unowned cell with x in [xmin + 2, xmax) and y in
	/// [ymin + 2, ymax), summed in 32 bits; <paramref name="tooDear"/> when a running total ever exceeds the balance.
	/// </summary>
	// [BIN:TPW-EXE:0x532303 land price] for y from ymin + 2 while below ymax and x from xmin + 2 while below xmax, each cell with flag 0x40 adds the word 0x78560c (Costs.MapCell, the word after Costs.WaterTrackCell 0x785608) to 0x81af4c; unless a layout replays (0x785a2c), the balance (bank +0xc, 0x6ad810) minus that running total below 0 refuses the purchase and sets the cash pointer flag 0x816d5c (0x5323c1); equal is allowed
	// The price counts fewer cells than the merge makes owned: the cells next to the rectangle's west and north fences (x = xmin + 1, y = ymin + 1) and under its east and south fences (x = xmax, y = ymax) become owned without charge, as in the original.
	public static (int Cells, long Cost) Price( ParkCellMap cells, int xmin, int ymin, int xmax, int ymax, long cellCost, long? balance, out bool tooDear )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var count = 0;
		var total = 0;
		for ( var y = ymin + 2; y < ymax; y++ )
		{
			for ( var x = xmin + 2; x < xmax; x++ )
			{
				if ( !cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) )
					continue;
				count++;
				total = unchecked( total + (int)cellCost );
			}
		}
		tooDear = balance is { } bank && TooDear( count, cellCost, bank );
		return (count, total);
	}

	/// <summary>
	/// The money test of <see cref="Price"/> on its own: whether any running total of <paramref name="cells"/> charges of
	/// <paramref name="cellCost"/>, summed in 32 bits, exceeds <paramref name="balance"/>.
	/// </summary>
	public static bool TooDear( int cells, long cellCost, long balance )
	{
		var total = 0;
		for ( var cell = 0; cell < cells; cell++ )
		{
			total = unchecked( total + (int)cellCost );
			if ( unchecked( (int)balance - total ) < 0 )
				return true;
		}
		return false;
	}

	/// <summary>
	/// The half-resolution land test: 64 × 64 blocks of 2 × 2 cells, each taking the state of its (odd, odd) cell (a hoarding,
	/// owned or unowned land), the rectangle's posts marked owned, then everything reachable from outside the map through
	/// unowned land or hoardings filled; a block of unowned land or a hoarding left over is a pocket.
	/// </summary>
	// [BIN:TPW-EXE:0x532960 land grid] block (x >> 1, y >> 1) of 0x81eeb0 (67 bytes per column) is rewritten by each of its cells in row order, so the cell (2i + 1, 2j + 1) decides it: '@' for a hoarding (following object links, 0x53ad90), '#' for a cell without 0x40, ' ' otherwise
	// [BIN:TPW-EXE:0x5321dc pocket test] the grid is copied inside a border of ' ' (0x81d7c8; most of its last row is a wall, see the pocket border below), the blocks (x >> 1, y >> 1) for x from xmin + 1 to xmax + 1 and y from ymin + 1 to ymax + 1 in steps of 2 become '#', the flood 0x5328e0 turns every ' ' and '@' it reaches from the border corner (65, 0) into '#', and any ' ' or '@' left inside refuses the rectangle
	private bool LeavesPocket( ParkCellMap cells, int xmin, int ymin, int xmax, int ymax )
	{
		const int Size = MapSize / 2 + 2;
		const byte Unowned = (byte)' ', Owned = (byte)'#', Hoarding = (byte)'@';
		var grid = new byte[Size, Size];
		for ( var i = 0; i < Size; i++ )
			for ( var j = 0; j < Size; j++ )
				grid[i, j] = Unowned;
		// [BIN:TPW-EXE:0x5321dc pocket border] the copy loop (0x5321dc..0x53221b) writes the border row y = 65 at iterations 0..63, then iteration 64 copies source row 64 into bordered row 65 over columns 1..65; the source 0x81eeb0 never has rows 64 or 65 written (0x532960 fills blocks 0..63 only), so columns 1..63 of that row hold neither ' ' nor '@' and wall the flood off; only (0, 65), (64, 65) and (65, 65) stay open
		for ( var i = 1; i <= Size - 3; i++ )
			grid[i, Size - 1] = Owned;
		for ( var i = 0; i < Size - 2; i++ )
		{
			for ( var j = 0; j < Size - 2; j++ )
			{
				var (x, y) = (2 * i + 1, 2 * j + 1);
				if ( !cells.InBounds( x, y ) )
					continue;
				grid[i + 1, j + 1] = IsPost( x, y ) ? Hoarding : cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) ? Unowned : Owned;
			}
		}
		for ( var y = ymin + 1; y <= ymax + 1; y += 2 )
			for ( var x = xmin + 1; x <= xmax + 1; x += 2 )
				if ( (uint)(x >> 1) < Size && (uint)(y >> 1) < Size )
					grid[x >> 1, y >> 1] = Owned;
		var open = new Stack<(int X, int Y)>();
		if ( grid[Size - 1, 0] is Unowned or Hoarding )
		{
			grid[Size - 1, 0] = Owned;
			open.Push( (Size - 2, 0) );
			open.Push( (Size - 1, 1) );
		}
		while ( open.Count > 0 )
		{
			var (x, y) = open.Pop();
			if ( (uint)x >= Size || (uint)y >= Size || grid[x, y] is not (Unowned or Hoarding) )
				continue;
			grid[x, y] = Owned;
			open.Push( (x - 1, y) );
			open.Push( (x + 1, y) );
			open.Push( (x, y - 1) );
			open.Push( (x, y + 1) );
		}
		for ( var i = 1; i < Size - 1; i++ )
			for ( var j = 1; j < Size - 1; j++ )
				if ( grid[i, j] is Unowned or Hoarding )
					return true;
		return false;
	}

	private bool CoversGate( int xmin, int ymin, int xmax, int ymax )
	{
		if ( PostA.Y != PostB.Y )
			return false;
		for ( var x = Math.Min( PostA.X, PostB.X ); x < Math.Max( PostA.X, PostB.X ); x += 2 )
		{
			var square = (X: x, Y: PostA.Y - 2);
			if ( square.X >= xmin && square.X + 2 <= xmax && square.Y >= ymin && square.Y + 2 <= ymax && !squares.Contains( square ) )
				return true;
		}
		return false;
	}

	/// <summary>
	/// The land after buying the rectangle between two posts: the enclosed squares and the rectangle's, with the fence
	/// walked round them from post A to post B keeping the outer branches. Null when that walk does not reach post B.
	/// </summary>
	// [BIN:TPW-EXE:0x5327e0 land purchase] the purchase lays hoardings round the rectangle by five clicks of the fence tool (mode 0x19), then walks the ring from post A to post B (0x52ba50, which drops the links of the branches it does not take) and refills the land (0x531040, 0x5311c0, whose last pass removes every hoarding the walk did not mark)
	public ParkLand? Merge( (int X, int Y) first, (int X, int Y) second )
	{
		var (xmin, xmax) = (Math.Min( first.X, second.X ), Math.Max( first.X, second.X ));
		var (ymin, ymax) = (Math.Min( first.Y, second.Y ), Math.Max( first.Y, second.Y ));
		var region = new HashSet<(int X, int Y)>( squares );
		for ( var y = ymin; y + 2 <= ymax; y += 2 )
			for ( var x = xmin; x + 2 <= xmax; x += 2 )
				region.Add( (x, y) );
		var ring = new List<(int X, int Y)> { PostA };
		var (position, heading) = (PostA, East);
		while ( ring.Count <= MaximumPosts )
		{
			if ( position == PostB && ring.Count > 1 )
			{
				var merged = new ParkLand( PostA, PostB, ring );
				return merged.squares.SetEquals( region ) ? merged : null;
			}
			var next = Turns( heading ).FirstOrDefault( turn => IsBoundary( region, position, turn ) );
			if ( next == 0 )
				return null;
			heading = next;
			position = Step( position, heading );
			ring.Add( position );
		}
		return null;
	}

	/// <summary>Whether the side from <paramref name="post"/> toward <paramref name="direction"/> has an enclosed square on its right and none on its left.</summary>
	private static bool IsBoundary( HashSet<(int X, int Y)> region, (int X, int Y) post, int direction )
	{
		var (x, y) = post;
		var (right, left) = direction switch
		{
			East => ((x, y), (x, y - 2)),
			South => ((x - 2, y), (x, y)),
			West => ((x - 2, y - 2), (x - 2, y)),
			_ => ((x, y - 2), (x - 2, y - 2)),
		};
		return region.Contains( right ) && !region.Contains( left );
	}

	/// <summary>The headings a walk tries at a post: left of <paramref name="heading"/>, straight on, then right.</summary>
	private static int[] Turns( int heading ) => new[] { Rotate( heading, 3 ), heading, Rotate( heading, 1 ) };

	private static int Rotate( int heading, int quarterTurns )
	{
		var bit = heading switch { North => 0, East => 1, South => 2, _ => 3 };
		return ((bit + quarterTurns) % 4) switch { 0 => North, 1 => East, 2 => South, _ => West };
	}

	private static (int X, int Y) Step( (int X, int Y) post, int heading ) => heading switch
	{
		North => (post.X, post.Y - 2),
		East => (post.X + 2, post.Y),
		South => (post.X, post.Y + 2),
		_ => (post.X - 2, post.Y),
	};

	/// <summary>The squares inside the ring closed from post B back to post A (each square's centre tested against the polygon).</summary>
	private static HashSet<(int X, int Y)> Enclosed( (int X, int Y)[] ring )
	{
		var (minX, maxX) = (ring.Min( post => post.X ), ring.Max( post => post.X ));
		var (minY, maxY) = (ring.Min( post => post.Y ), ring.Max( post => post.Y ));
		var enclosed = new HashSet<(int X, int Y)>();
		for ( var sy = minY; sy + 2 <= maxY; sy += 2 )
		{
			for ( var sx = minX; sx + 2 <= maxX; sx += 2 )
			{
				// even-odd ray cast eastward from the square's centre; centres never lie on a post row, so no vertex is hit
				var (cx, cy) = (sx + 1.0, sy + 1.0);
				var inside = false;
				for ( var index = 0; index < ring.Length; index++ )
				{
					var (x1, y1) = ring[index];
					var (x2, y2) = ring[(index + 1) % ring.Length];
					if ( (y1 > cy) == (y2 > cy) )
						continue;
					var crossing = x1 + (cy - y1) * (x2 - x1) / (y2 - y1);
					if ( crossing > cx )
						inside = !inside;
				}
				if ( inside )
					enclosed.Add( (sx, sy) );
			}
		}
		return enclosed;
	}

	/// <summary>
	/// The type the original's map start gives a cell from its MAP attribute bits and whether its terrain has a surface:
	/// with a surface 1 (initial path) or 0; without one 2 (water), 0x1e (fixed walkway) or 7.
	/// </summary>
	// [BIN:TPW-EXE:0x536490 map init] with 0x450420 true (the terrain vertex word's bit 0 clear) the types 5, 2 and 0x1e that MAP bits 0x01, 0x02 and 0x80 pick are overwritten by 1 for bit 0x08 (with flag 0x20) or else 0 (0x536524..0x536548); with it false bit 0x02 makes type 2, bit 0x80 type 0x1e, anything else type 7
	public static byte OriginalCellType( MapCellFlags flags, bool surface )
	{
		if ( !surface )
			return flags.HasFlag( MapCellFlags.Water ) ? (byte)2 : flags.HasFlag( MapCellFlags.FixedWalkway ) ? (byte)0x1e : (byte)7;
		return flags.HasFlag( MapCellFlags.InitialPath ) ? (byte)1 : (byte)0;
	}

	/// <summary>Whether a cell of an original level is of type 7 or 0x1e, the ground a run of new fence may not cross (<see cref="CheckPurchase"/>).</summary>
	// [BIN:TPW-EXE:0x450420 terrain test] true without a loaded terrain; false outside the terrain's cell grid (+0x18 × +0x1c); else true when bit 0 of the cell's word (+0x2c, 4 bytes a cell) is clear — the level's heightfield (base.md2) cell words, which give the Jungle Easymode save's types 2, 7 and 0x1e on all 9,383 such cells
	public static bool IsBarrier( MapFile map, ModelHeightfield field, int x, int y )
	{
		ArgumentNullException.ThrowIfNull( map );
		ArgumentNullException.ThrowIfNull( field );
		if ( x < 0 || y < 0 || x >= map.CellCountX || y >= map.CellCountY )
			return false;
		var surface = x < field.CellCountX && y < field.CellCountZ && (field.GetCellWord( x, y ) & 1) == 0;
		return OriginalCellType( map.GetFlagsAt( x, y ), surface ) is 7 or 0x1e;
	}

	/// <summary>The land's part of <see cref="WorldStateHash"/>: the gate posts and the ring in walking order.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( PostA.X );
		hash.Add( PostA.Y );
		hash.Add( PostB.X );
		hash.Add( PostB.Y );
		hash.Add( ring.Length );
		foreach ( var (x, y) in ring )
			hash.Add( (long)x << 16 | (uint)y );
	}
}
