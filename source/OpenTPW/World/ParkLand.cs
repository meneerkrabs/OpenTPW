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
	/// <summary>
	/// The original's validator accepts the rectangle, but the fence routines it then runs fault on it: the walk from post A never ends or the fill reads the cell beyond the map's edge (tp.exe hangs or crashes there).
	/// OpenTPW refuses it instead.
	/// </summary>
	Fence,
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
/// <param name="Clicks">The offsets from post A the fence tool is clicked at, without the 512 terminator: the <c>HoardingClicks</c> of <c>Hoardings.sam</c>, or the six clicks of the short fence.</param>
public sealed record ParkLandSetup( (int X, int Y) PostA, (int X, int Y) PostB, IReadOnlyList<(int X, int Y)> Clicks )
{
	/// <summary>The clicks of the fence set-up when bit 0x400 of the level word 0x79D86C is set: a small enclosure in front of the gate, from post A east two cells, south four, west twelve, north four and east two.</summary>
	// [BIN:TPW-EXE:0x00532489 six-click fence] with bit 0x400 of 0x79D86C the set-up clicks the fence tool at A, A + (2, 0), A + (2, 4), A - (10, -4), A - (10, 0) and A - (8, 0) instead of reading the Hoardings.sam table; the bit is copied from the options word 0x78D954 by 0x449E78 and cleared by the new game set-up 0x44DE2B
	public static readonly IReadOnlyList<(int X, int Y)> SixClicks = new[] { (0, 0), (2, 0), (2, 4), (-10, 4), (-10, 0), (-8, 0) };

	/// <summary>The setup of a level from its entrance cells and its <c>Hoardings.sam</c> text (or, with <paramref name="sixClickFence"/>, the six-click fence); null when either entrance cell or the clicks are missing.</summary>
	// [BIN:TPW-EXE:0x5323f0 park fence] post B is the entrance's first cell (0x7855bc/0x7855c0, FixedItemInfo.EntranceA) moved 4 cells west, post A its second cell (0x7855c4/0x7855c8, EntranceB) moved 3 cells east (0x4d8690 gives both cells)
	public static ParkLandSetup? FromLevel( (int X, int Y)? entranceA, (int X, int Y)? entranceB, string? hoardingsText, bool sixClickFence = false )
	{
		if ( entranceA is not { } a || entranceB is not { } b )
			return null;
		if ( sixClickFence )
			return new ParkLandSetup( (b.X + 3, b.Y), (a.X - 4, a.Y), SixClicks );
		if ( hoardingsText == null )
			return null;
		var clicks = ParkLand.ParseHoardingClicks( hoardingsText );
		return clicks.Count == 0 ? null : new ParkLandSetup( (b.X + 3, b.Y), (a.X - 4, a.Y), clicks );
	}
}

/// <summary>
/// The park's land (tp.exe's hoardings, docs/reverse/PATH-plan.md "PATH-LAND"): the fence of posts on the second map layer (<see cref="FenceLayer"/>) from post A, east of the gate, round the park to post B, west of
/// it, and the rules that turn it into owned cells (cell flag 0x40 clear) and that buy more land. The fence is laid by the original's own routines (<see cref="FenceEngine"/>: the line tool, the walk and the fill),
/// so the cells it owns are the ones the original leaves; a purchase runs them again on the live fence. Fence posts stand on a lattice of every second cell anchored on post A.
/// </summary>
// [EXT:land-purchase-fault-refusal] a purchase on which the original's fill reads the cell beyond the map's edge (a null block, a crash) or whose ring walk never ends (a cell that is no post, a hang) is refused and charges nothing, where the original crashes or hangs; refusing is OpenTPW's choice, approved by the user on 2026-10-29 (docs/PATHS.md)
// [BIN:TPW-EXE:0x0044DE2B six-click bit] the six-click fence (bit 0x400 of the level word 0x79D86C) is traced never set: the writers 0x44D993, 0x44DA92, 0x44DDF3, 0x44DE34, 0x44DEFD, 0x44E306 and 0x44E322 set other bits or clear, only a whole-dword copy of the settings blob read from dialog.tcf (0x424A60 to 0x78D8D8, then 0x449E78 copies +0x7C) could, and 0x44DE2B clears it when no settings load, so a stock install never lays it; the engine lays it when asked and OpenTPW reads no dialog.tcf
// Host storage (not an approximation): a track tile's fence links +0x22 stay in the fence layer instead of the track layer's alternate mask, and a save without its structure records (written before the fence port) derives the layer from its ring (LayerFromRing)
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
	private (int X, int Y)[] ring;
	private HashSet<(int X, int Y)> posts;
	private FenceLayer? layer;
	private readonly ParkLandSetup? setup;

	/// <summary>A land from its ring: every post from <paramref name="postA"/> to <paramref name="postB"/>, two cells apart along one axis. Its fence records are made from the ring when a map needs them.</summary>
	public ParkLand( (int X, int Y) postA, (int X, int Y) postB, IReadOnlyList<(int X, int Y)> ring )
		: this( postA, postB, ring, null )
	{
	}

	private ParkLand( (int X, int Y) postA, (int X, int Y) postB, IReadOnlyList<(int X, int Y)> ring, ParkLandSetup? setup )
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
		this.setup = setup;
	}

	private ParkLand( (int X, int Y) postA, (int X, int Y) postB, FenceLayer layer, (int X, int Y)[] ring )
	{
		PostA = postA;
		PostB = postB;
		this.layer = layer;
		this.ring = ring;
		posts = ring.ToHashSet();
	}

	/// <summary>The fence's first post (the ring's start, east of the gate; the walks start here heading east).</summary>
	public (int X, int Y) PostA { get; }
	/// <summary>The fence's last post (west of the gate).</summary>
	public (int X, int Y) PostB { get; }
	/// <summary>Every post from <see cref="PostA"/> to <see cref="PostB"/> in walking order; a post the fence passes twice is listed twice.</summary>
	public IReadOnlyList<(int X, int Y)> Ring => ring;

	/// <summary>The fence records of the land on a map of <paramref name="width"/> × <paramref name="height"/> cells: the live ones, or those a ring without a map implies.</summary>
	public FenceLayer LayerFor( int width, int height )
	{
		if ( layer == null || layer.Width != width || layer.Height != height )
			layer = LayerFromRing( ring, width, height );
		return layer;
	}

	/// <summary>Whether a fence post (a hoarding, cell type 0x19) stands on cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
	public bool IsPost( int x, int y ) => layer != null ? layer.IsPost( x, y ) : posts.Contains( (x, y) );

	/// <summary>Whether the fence's record on the cell counts 50 for the net worth and the big park ticket (<see cref="FenceLayer.CountsAsStructure"/>), on a map of <paramref name="width"/> × <paramref name="height"/> cells.</summary>
	public bool CountsAsStructure( int x, int y, int width, int height ) => LayerFor( width, height ).CountsAsStructure( x, y );

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
	/// The fence a new park starts with, as a ring: a straight run of posts from each click to the next, the first click on post A and
	/// the last on post B. Throws <see cref="InvalidDataException"/> when the clicks do not draw such a fence. <see cref="ApplyToNewPark"/> lays the fence itself with the click list kept here.
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
		return new ParkLand( setup.PostA, setup.PostB, ring, setup );
	}

	/// <summary>
	/// The land of a fence that stands in <paramref name="layer"/>: the walk from post A (0x52BA50's choice of link) up to post B gives its ring. Null when the walk does not reach post B.
	/// </summary>
	public static ParkLand? FromLayer( (int X, int Y) postA, (int X, int Y) postB, FenceLayer layer )
	{
		ArgumentNullException.ThrowIfNull( layer );
		return WalkRing( layer, postA, postB ) is { } walked ? new ParkLand( postA, postB, layer, walked ) : null;
	}

	/// <summary>
	/// The land of an imported save's fence from its hoardings' links (<c>mHoardingNeighbours</c>; 1 north, 4 east, 16 south, 64 west), walked from post A to post B. Null when the walk does not reach post B.
	/// </summary>
	public static ParkLand? FromFenceLinks( (int X, int Y) postA, (int X, int Y) postB, Func<int, int, byte?> links, int width = MapSize, int height = MapSize )
	{
		ArgumentNullException.ThrowIfNull( links );
		var fence = new FenceLayer( width, height );
		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				if ( links( x, y ) is { } bits )
				{
					fence.Type[y * width + x] = FenceLayer.PostType;
					fence.Links[y * width + x] = bits;
				}
			}
		}
		return FromLayer( postA, postB, fence );
	}

	/// <summary>
	/// The ring of a fence in a layer: the walk of 0x52BA50 from post A heading east without its writes. A corner turns to the link it did not come by; at a junction the walk takes the first link counting from
	/// its left (left, ahead, right) and drops the others for the rest of the walk. Null when the walk leaves the posts, leaves the map or does not reach post B.
	/// </summary>
	// [BIN:TPW-EXE:0x52ba50 ring walk] the walk starts on post A heading east (bit 4); a corner turns to its other link (0x53ae00), a junction (0x53ae90) takes the first link counting from the left of the heading (0x4d8c20 with 0x40, then 4) and keeps only that link and the one it came by (or, ahead, the line); it ends on post B
	public static (int X, int Y)[]? WalkRing( FenceLayer layer, (int X, int Y) postA, (int X, int Y) postB )
	{
		ArgumentNullException.ThrowIfNull( layer );
		var links = (byte[])layer.Links.Clone();
		var ring = new List<(int X, int Y)>();
		var (x, y) = postA;
		var heading = East;
		for ( var steps = 0; steps <= MaximumPosts; steps++ )
		{
			if ( (uint)x >= (uint)layer.Width || (uint)y >= (uint)layer.Height )
				return null;
			var index = y * layer.Width + x;
			ring.Add( (x, y) );
			var bits = (int)links[index];
			if ( layer.Type[index] != FenceLayer.PostType && System.Numerics.BitOperations.PopCount( (uint)bits ) == 0 )
				return null;
			var cardinal = ((bits & 1) != 0 ? 1 : 0) + ((bits & 4) != 0 ? 1 : 0) + ((bits & 0x10) != 0 ? 1 : 0) + ((bits & 0x40) != 0 ? 1 : 0);
			if ( cardinal == 2 && (((bits & 1) != 0 && ((bits & 4) != 0 || (bits & 0x40) != 0)) || ((bits & 0x10) != 0 && ((bits & 4) != 0 || (bits & 0x40) != 0))) )
			{
				var back = Opposite( heading );
				if ( (back & bits) == 0 )
					back = heading;
				heading = bits ^ back;
			}
			if ( cardinal >= 3 )
			{
				var found = 0;
				var candidate = Rotate( heading, 0x40 );
				do
				{
					if ( (candidate & bits) != 0 )
						found = candidate;
					candidate = Rotate( candidate, 4 );
				}
				while ( found == 0 );
				links[index] &= (byte)(heading == found ? (Opposite( heading ) | heading) : ~(heading | Opposite( found )));
				heading = found;
			}
			if ( x == postB.X && y == postB.Y )
				return ring.ToArray();
			switch ( heading )
			{
				case North:
					y -= 2;
					break;
				case East:
					x += 2;
					break;
				case South:
					y += 2;
					break;
				case West:
					x -= 2;
					break;
				default:
					return null;
			}
		}
		return null;
	}

	private static int Opposite( int direction ) => direction == 0 ? 0 : direction < 0x10 ? (direction & 0xFF) << 4 : (direction & 0xFF) >> 4;

	private static int Rotate( int direction, int value )
	{
		var count = 0;
		var bits = (byte)direction;
		while ( bits > 1 )
		{
			bits >>= 1;
			count++;
		}
		var shifted = (value & 0xFF) << count;
		return shifted > 0x80 ? shifted >> 8 : (byte)(value << count);
	}

	/// <summary>The fence records a ring implies: a post on every ring cell with the links toward its ring neighbours, the walk's direction (toward the next post), and the three cells beside it typed as its extension.</summary>
	public static FenceLayer LayerFromRing( IReadOnlyList<(int X, int Y)> ring, int width, int height )
	{
		var layer = new FenceLayer( width, height );
		for ( var index = 0; index < ring.Count; index++ )
		{
			var (x, y) = ring[index];
			if ( (uint)x >= (uint)width || (uint)y >= (uint)height )
				continue;
			var cell = y * width + x;
			layer.Type[cell] = FenceLayer.PostType;
			if ( index + 1 < ring.Count )
			{
				var (nx, ny) = ring[index + 1];
				var forward = nx > x ? East : nx < x ? West : ny > y ? South : North;
				layer.Links[cell] |= (byte)forward;
				layer.Direction[cell] = (byte)forward;
				if ( (uint)nx < (uint)width && (uint)ny < (uint)height )
					layer.Links[ny * width + nx] |= (byte)Opposite( forward );
			}
			else if ( index > 0 )
				layer.Direction[cell] = layer.Direction[ring[index - 1].Y * width + ring[index - 1].X];
		}
		for ( var index = 0; index < ring.Count; index++ )
		{
			var (x, y) = ring[index];
			if ( (uint)x >= (uint)width || (uint)y >= (uint)height )
				continue;
			foreach ( var (dx, dy) in new[] { (0, 1), (1, 0), (1, 1) } )
			{
				var (ex, ey) = (x + dx, y + dy);
				if ( (uint)ex >= (uint)width || (uint)ey >= (uint)height )
					continue;
				var cell = ey * width + ex;
				if ( layer.Type[cell] == 0 )
				{
					layer.Type[cell] = FenceLayer.ExtensionType;
					layer.Owner[cell] = (ushort)(y * width + x + 1);
				}
			}
		}
		return layer;
	}

	/// <summary>The click list that lays a ring: post A, then every post where the fence turns, then the last post, as offsets from post A.</summary>
	private static IEnumerable<(int X, int Y)> ClicksOfRing( (int X, int Y) postA, IReadOnlyList<(int X, int Y)> ring )
	{
		yield return (0, 0);
		for ( var index = 1; index < ring.Count; index++ )
		{
			var last = index == ring.Count - 1;
			if ( last || ring[index + 1].X - ring[index].X != ring[index].X - ring[index - 1].X || ring[index + 1].Y - ring[index].Y != ring[index].Y - ring[index - 1].Y )
				yield return (ring[index].X - postA.X, ring[index].Y - postA.Y);
		}
	}

	/// <summary>
	/// A new park's land on <paramref name="cells"/>: the world init's unowned flag on every cell without NoModify, then the fence the level's clicks lay and the land the original's fill and walk give it (<see cref="FenceEngine"/>).
	/// Null when the fence cannot be laid (the walk never reaches post B); the cells are then left as they were.
	/// </summary>
	// [BIN:TPW-EXE:0x536490 map init] flag 0x40 is set on every cell whose flags lack 0x20 (0x53654b, 0x5365ad); the level start 0x52f050 runs it on every cell before it lays the fence (0x5323f0)
	public static ParkLand? NewPark( ParkCellMap cells, ParkLandSetup setup )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( setup );
		var fence = new FenceLayer( cells.Width, cells.Height );
		var engine = new FenceEngine( cells, fence, setup.PostA, setup.PostB );
		try
		{
			engine.InitialiseOwnership();
			engine.LayInitialFence( setup.Clicks );
		}
		catch ( FenceFaultException )
		{
			return null;
		}
		var walked = WalkRing( fence, setup.PostA, setup.PostB );
		if ( walked == null )
			return null;
		engine.Commit();
		return new ParkLand( setup.PostA, setup.PostB, fence, walked );
	}

	/// <summary>
	/// A new park's ownership on <paramref name="cells"/> with this land's gate posts and clicks: every cell but the fixed (<see cref="ParkCellFlags.NoModify"/>) ones is unowned, then the fence is laid and its land is owned.
	/// A land made from a ring lays the clicks that draw the ring.
	/// </summary>
	public void ApplyToNewPark( ParkCellMap cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var fence = new FenceLayer( cells.Width, cells.Height );
		var engine = new FenceEngine( cells, fence, PostA, PostB );
		engine.InitialiseOwnership();
		engine.LayInitialFence( setup?.Clicks ?? ClicksOfRing( PostA, ring ).ToArray() );
		var walked = WalkRing( fence, PostA, PostB ) ?? throw new InvalidDataException( "The park fence cannot be laid: its walk does not reach post B." );
		engine.Commit();
		layer = fence;
		ring = walked;
		posts = ring.ToHashSet();
	}

	/// <summary>
	/// Buys the rectangle between the fence posts <paramref name="first"/> and <paramref name="second"/> on <paramref name="cells"/> as the Buy Land tool does (0x5327E0 after the validator): the fence tool lays the
	/// rectangle, the walk from post A rewrites the fence and the fill owns the land it encloses. False, with the park as it was, when the original's walk would never end.
	/// </summary>
	public bool Purchase( ParkCellMap cells, (int X, int Y) first, (int X, int Y) second )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var fence = LayerFor( cells.Width, cells.Height ).Clone();
		var engine = new FenceEngine( cells, fence, PostA, PostB );
		try
		{
			engine.LayPurchase( first.X, first.Y, second.X, second.Y );
		}
		catch ( FenceFaultException )
		{
			return false;
		}
		var walked = WalkRing( fence, PostA, PostB );
		engine.Commit();
		layer = fence;
		if ( walked != null )
		{
			ring = walked;
			posts = ring.ToHashSet();
		}
		return true;
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
		var (count, total) = Price( cells, xmin, ymin, xmax, ymax, cellCost, balance, out var tooDear );
		if ( tooDear )
			return new LandPurchaseCheck( LandPurchaseRefusal.Money, count, total );
		// the validator has no more refusals: the purchase runs the fence routines, which fault (hang or crash) on some accepted rectangles
		// [BIN:TPW-EXE:0x5311c0 fill] a scan towards the map's edge reads the block beyond it through 0x4d96f0's null (the fill never tests it), and the walk 0x52ba50 loops on a cell that is no post: a rectangle that leaves the old park outside the walk's ring does the first
		return new LandPurchaseCheck( PurchaseFaults( cells, first, second ) ? LandPurchaseRefusal.Fence : LandPurchaseRefusal.None, count, total );

		static LandPurchaseCheck Refused( LandPurchaseRefusal refusal ) => new( refusal, 0, 0 );
	}

	/// <summary>Whether the purchase of the rectangle would fault in the original (it hangs or crashes): the routines are run on a copy of the fence and the cells' flags are not written.</summary>
	private bool PurchaseFaults( ParkCellMap cells, (int X, int Y) first, (int X, int Y) second )
	{
		var scratch = LayerFor( cells.Width, cells.Height ).Clone();
		var engine = new FenceEngine( cells, scratch, PostA, PostB );
		try
		{
			engine.LayPurchase( first.X, first.Y, second.X, second.Y );
			return false;
		}
		catch ( FenceFaultException )
		{
			return true;
		}
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
				// [BIN:TPW-EXE:0x5329D9 pocket grid extension] 0x532960 follows an extension record (0x53AD90: type 0xC or 0x11, a track tile's too) to the record its word +0x10 names (the tile's origin) before it tests the hoarding type (0x53AD60) and the flag 0x40 of that record's cell
				if ( cells.Tracks.Get( x, y ) is { IsExtension: true, Parent: >= 0 } extension && extension.Parent < cells.Width * cells.Height )
					(x, y) = cells.Tracks.CellOf( extension.Parent );
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
