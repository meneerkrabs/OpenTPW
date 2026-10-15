namespace OpenTPW;

/// <summary>What a click of the Buy Land tool did.</summary>
public enum BuyLandClick { Ignored, CornerStored, Bought, Refused }

/// <summary>
/// The Buy Land tool (tp.exe mouse mode 0x39, the click handler <c>0x00524960</c>): started from the buy window's Buy Land row,
/// the first click stores a fence post, the second validates the rectangle between the two posts (<see cref="ParkLand.CheckPurchase"/>),
/// buys it when allowed and ends the tool either way, so each activation buys at most one rectangle. CPU-only: the level owns it
/// and the HUD feeds it the hovered cell.
/// </summary>
public sealed class BuyLandTool
{
	private readonly Func<ParkCellMap?> cells;
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<int, int, bool> barrier;

	/// <param name="barrier">Whether a cell is of the original types 7 or 0x1e (<see cref="ParkLand.CheckPurchase"/>).</param>
	public BuyLandTool( Func<ParkCellMap?> cells, Func<ParkEconomy?> economy, Func<int, int, bool> barrier )
	{
		this.cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
		this.economy = economy ?? throw new ArgumentNullException( nameof( economy ) );
		this.barrier = barrier ?? throw new ArgumentNullException( nameof( barrier ) );
	}

	public bool IsActive { get; private set; }
	/// <summary>The post the first click stored, null before it.</summary>
	public (int X, int Y)? Corner { get; private set; }
	/// <summary>The post under the pointer, null off the park.</summary>
	public (int X, int Y)? HoverPost { get; private set; }
	/// <summary>The validator's verdict on the rectangle from <see cref="Corner"/> to <see cref="HoverPost"/>, null before the first click.</summary>
	public LandPurchaseCheck? HoverCheck { get; private set; }
	/// <summary>Whether the hovered rectangle is refused (the ghost is drawn red).</summary>
	// [BIN:TPW-EXE:0x523afe Buy Land hover] with a stored corner the hover runs the validator on the pointer's post and stores its refusal in 0x816d50, then draws the four ghost edges (0x53aac0 with the tool mode | 0x100)
	public bool HoverRefused => HoverCheck is { Allowed: false };
	/// <summary>Whether the hovered rectangle costs more than the balance (the cash pointer, flag 0x816d5c).</summary>
	public bool HoverTooDear => HoverCheck is { Refusal: LandPurchaseRefusal.Money };
	/// <summary>The last purchase or refusal, for the HUD's message.</summary>
	public LandPurchaseCheck? LastResult { get; private set; }

	/// <summary>Starts the tool (the buy window's Buy Land row); false when the park has no land model.</summary>
	public bool Begin()
	{
		if ( cells()?.Land == null )
			return false;
		IsActive = true;
		Corner = null;
		HoverPost = null;
		HoverCheck = null;
		return true;
	}

	/// <summary>Ends the tool without a purchase (right click, Escape, another tool).</summary>
	public void Cancel()
	{
		IsActive = false;
		Corner = null;
		HoverPost = null;
		HoverCheck = null;
	}

	/// <summary>The pointer is over (<paramref name="x"/>, <paramref name="y"/>), or off the park when null.</summary>
	public void Hover( int? x, int? y )
	{
		if ( !IsActive || x is not { } cx || y is not { } cy || cells() is not { Land: { } land } map )
		{
			HoverPost = null;
			HoverCheck = null;
			return;
		}
		HoverPost = Snap( cx, cy, land.PostA, map.Width, map.Height );
		if ( Corner is not { } corner )
		{
			HoverCheck = null;
			return;
		}
		// the validator walks the whole fence, so it runs again only when the rectangle, the map or the money changed
		var key = (corner, HoverPost.Value, map.Version, economy()?.Balance);
		if ( HoverCheck != null && key == hoverKey )
			return;
		hoverKey = key;
		HoverCheck = Check( map, land, corner, HoverPost.Value );
	}

	private ((int X, int Y) Corner, (int X, int Y) Post, int Version, long? Balance) hoverKey;

	/// <summary>A click on (<paramref name="x"/>, <paramref name="y"/>): the first stores a corner, the second buys or refuses and ends the tool.</summary>
	// [BIN:TPW-EXE:0x525d29 Buy Land click] without a stored corner (0x81ede4/0x81ede8 at -1) the click stores the post and rebuilds the land grid (0x532960); otherwise it runs the validator from the stored post to the clicked one with the money test, and when it passes buys the rectangle (0x5327e0) and debits its total (0x4d01f0); either way the corner is reset and the tool ends (0x52f200(0, 0))
	public BuyLandClick Click( int x, int y )
	{
		if ( !IsActive || cells() is not { Land: { } land } map )
			return BuyLandClick.Ignored;
		var post = Snap( x, y, land.PostA, map.Width, map.Height );
		if ( Corner is not { } corner )
		{
			Corner = post;
			HoverPost = post;
			HoverCheck = null;
			return BuyLandClick.CornerStored;
		}
		var check = Check( map, land, corner, post );
		LastResult = check;
		var result = BuyLandClick.Refused;
		if ( check.Allowed && land.Merge( corner, post ) is { } merged )
		{
			map.SetLand( merged );
			merged.ClaimEnclosedCells( map );
			economy()?.ChargeLand( check.Cost, check.Cells );
			result = BuyLandClick.Bought;
		}
		Cancel();
		return result;
	}

	private LandPurchaseCheck Check( ParkCellMap map, ParkLand land, (int X, int Y) corner, (int X, int Y) post )
	{
		var park = economy();
		return land.CheckPurchase( map, corner, post, barrier, park?.CellCost( CellPurchase.Land ) ?? 0, park?.Balance );
	}

	/// <summary>
	/// The post a pointer cell picks: the cell clamped to x in [1, width − 2] and y in [1, height − 3], then moved down to the
	/// post lattice anchored on post A (every second cell, rounding toward negative infinity).
	/// </summary>
	// [BIN:TPW-EXE:0x525c60 Buy Land snap] the clicked cell is clamped to 1..W-2 and 1..H-3 (0x81d740, 0x81b1b0), its offsets from post A (0x7639f0/f4) are made even by taking one off a negative odd offset and halving toward zero, and the post is post A plus twice the halves; the hover (0x523a6a) snaps the same way
	public static (int X, int Y) Snap( int x, int y, (int X, int Y) postA, int width, int height )
	{
		x = Math.Min( Math.Max( x, 1 ), width - 2 );
		y = Math.Min( Math.Max( y, 1 ), height - 3 );
		return (postA.X + FloorEven( x - postA.X ), postA.Y + FloorEven( y - postA.Y ));

		static int FloorEven( int offset ) => (offset < 0 && (offset & 1) != 0 ? offset - 1 : offset) / 2 * 2;
	}

	/// <summary>The cells on the edges of the rectangle the next click would buy, or the hovered post alone before the first click.</summary>
	public IEnumerable<(int X, int Y)> PreviewEdge()
	{
		if ( HoverPost is not { } b )
			yield break;
		if ( Corner is not { } a )
		{
			yield return b;
			yield break;
		}
		var (x0, x1) = (Math.Min( a.X, b.X ), Math.Max( a.X, b.X ));
		var (y0, y1) = (Math.Min( a.Y, b.Y ), Math.Max( a.Y, b.Y ));
		for ( var y = y0; y <= y1; y++ )
			for ( var x = x0; x <= x1; x++ )
				if ( x == x0 || x == x1 || y == y0 || y == y1 )
					yield return (x, y);
	}
}
