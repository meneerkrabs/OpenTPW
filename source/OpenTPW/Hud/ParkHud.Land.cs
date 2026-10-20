using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>The buy window's land rows (Buy Land, Clear Land) and the HUD side of the Buy Land tool (tp.exe mouse mode 0x39).</summary>
public sealed partial class ParkHud
{
	/// <summary>The Miscellaneous list's Buy Land row (row id −1, UITEXT 134).</summary>
	public static readonly BuildItem BuyLandRow = new( "land:buy", -1, BuildCategory.Features, 0, 0, null, Array.Empty<string>() );
	/// <summary>The Miscellaneous list's Clear Land row (row id −2, UITEXT 135).</summary>
	public static readonly BuildItem ClearLandRow = new( "land:clear", -2, BuildCategory.Features, 0, 0, null, Array.Empty<string>() );

	/// <summary>Whether <paramref name="item"/> is one of the land rows rather than a catalogue object.</summary>
	public static bool IsLandRow( BuildItem item ) => item.InfoId < 0 && item.Entry == null && item.Category == BuildCategory.Features;

	/// <summary>The rows a category lists besides its catalogue items.</summary>
	// [BIN:TPW-EXE:0x4ab12f buy list land rows] the build list of category 3 always appends a row with UITEXT 0x86 'Buy Land', id -1 and the value of the word 0x78560c (Costs.MapCell), then one with UITEXT 0x87 'Clear Land', id -2 and the value 0
	private static IEnumerable<BuildItem> LandRows( BuildCategory category ) =>
		category != BuildCategory.Features ? Enumerable.Empty<BuildItem>()
			// [EXT:pools-ui] the pool rows follow the land rows while the opentpw.pools extension is on (ParkHud.Pools.cs)
			: FeatureGates.Pools ? new[] { BuyLandRow, ClearLandRow, PoolPaintRow, PoolEraseRow, PoolRiverRow, PoolSlideRow, PoolBridgeEastRow, PoolBridgeSouthRow, PoolBridgeEraseRow } : new[] { BuyLandRow, ClearLandRow };

	private string? LandRowName( BuildItem item ) => !IsLandRow( item ) ? null
		: IsPoolRow( item ) ? PoolRowName( item )
		: item.InfoId == BuyLandRow.InfoId ? strings[UIStrings.BuyLand] : strings[UIStrings.ClearLand];

	/// <summary>The value a buy list row shows and sorts by: a land row's own value, else the object's price.</summary>
	public long BuyPrice( BuildItem item ) => !IsLandRow( item ) ? (Status.TicketCostOf( item ) is { } tickets ? -tickets : Status.PriceOf( item ) ?? item.Cost)
		: IsPoolRow( item ) ? PoolRowPrice( item )
		: item.InfoId == BuyLandRow.InfoId ? (level.Park?.Economy.CellCost( CellPurchase.Land ) ?? 0) : 0;

	/// <summary>Whether a buy list row can be used now: Buy Land with a land model, Clear Land with the area clear tool, else the catalogue's availability.</summary>
	public bool IsBuyAvailable( BuildItem item ) => !IsLandRow( item ) ? Status.IsAvailable( item )
		: IsPoolRow( item ) ? IsPoolRowAvailable()
		: !level.IsReadOnlyVisit && (item.InfoId == BuyLandRow.InfoId ? level.BuyLandTool != null : level.AreaClearTool != null);

	/// <summary>Starts a land row's tool: Buy Land arms the land tool, Clear Land the area clear tool; either closes the window.</summary>
	// [BIN:TPW-EXE:0x4ac851 buy list land tool] the buy button on row -1 builds the tool object 0x46c580(0x39), the Buy Land mouse mode; row -2 builds 0x46c580(0x3a), the area clear
	private void BeginLandTool( BuildItem item )
	{
		if ( RejectReadOnlyAction() )
			return;
		if ( IsPoolRow( item ) )
		{
			BeginPoolTool( item );
			return;
		}
		if ( item.InfoId == ClearLandRow.InfoId && IsBuyAvailable( item ) )
		{
			BeginAreaClear();
			CloseBuyWindow();
			// [BIN:TPW-EXE:0x4ac8e9 clear land row] the row -2 builds tool 0x3A and posts help 0x159 (345) through 0x486b00 (0x4ac996)
			PostToolHelp( AdvisorToolHelp.ClearLandTool );
			return;
		}
		if ( !IsBuyAvailable( item ) || level.BuyLandTool is not { } tool )
		{
			PostMessage( strings.Extra( OpenTpwText.NotAvailable ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return;
		}
		level.EndBuildTools();
		level.StaffPlacement?.Cancel();
		LeaveBuildTool();
		if ( !tool.Begin() )
			return;
		CloseBuyWindow();
		// [BIN:TPW-EXE:0x4ac82f buy land row] the row -1 builds tool 0x39 and posts help 0x15A (346) through 0x486b00 (0x4ac996)
		PostToolHelp( AdvisorToolHelp.BuyLandTool );
		// [EXT:land-feedback] OpenTPW's hint for the two corner clicks
		PostMessage( strings.Extra( OpenTpwText.BuyLandHint ) );
	}

	/// <summary>The Buy Land tool's HUD side: the hovered post, right click leaves, a left click stores a corner or buys the rectangle.</summary>
	private bool UpdateBuyLandTool( UiInput input, bool inPark )
	{
		if ( level.BuyLandTool is not { IsActive: true } tool )
			return false;
		if ( input.RightPressed && inPark && GameOptions.Current.RmbCancel )
		{
			tool.Cancel();
			return toolPress = true;
		}
		if ( inPark )
			tool.Hover( cursorX, cursorY );
		else
			tool.Hover( null, null );
		if ( !inPark || !input.LeftPressed )
			return false;
		// [EXT:land-feedback] OpenTPW says when the land cannot be bought and plays the buy window's error sound; the original's feedback on a refused purchase beyond the red ghost and the cash pointer is not traced
		if ( tool.Click( cursorX, cursorY ) == BuyLandClick.Refused )
		{
			PostMessage( strings.Extra( OpenTpwText.CannotBuyLand ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
		}
		return toolPress = true;
	}

	/// <summary>Starts the area clear tool (the Clear Land row and the Clear key) after ending the other park tools.</summary>
	private void BeginAreaClear()
	{
		if ( level.AreaClearTool is not { } tool || level.IsReadOnlyVisit )
			return;
		level.EndBuildTools();
		level.StaffPlacement?.Cancel();
		LeaveBuildTool();
		tool.Begin();
	}

	/// <summary>The area clear tool's HUD side: the hovered cell, right click leaves, a left click stores a corner or clears the rectangle.</summary>
	private bool UpdateAreaClearTool( UiInput input, bool inPark )
	{
		if ( level.AreaClearTool is not { IsActive: true } tool )
			return false;
		if ( input.RightPressed && inPark && GameOptions.Current.RmbCancel )
		{
			tool.Cancel();
			return toolPress = true;
		}
		if ( inPark )
			tool.Hover( cursorX, cursorY );
		else
			tool.Hover( null, null );
		if ( !inPark || !input.LeftPressed )
			return false;
		tool.Click( cursorX, cursorY );
		return toolPress = true;
	}
}
