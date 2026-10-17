using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// The HUD side of the pool tool of the <c>opentpw.pools</c> extension (docs/POOLS.md). The original HUD has no pool entry, so the extension adds two rows to the end of
/// the buy window's Miscellaneous list (beside Buy Land and Clear Land) while it is on: "Pool water" arms the paint mode and "Remove pool water" the erase mode. Press and drag in the
/// park to paint or erase; right click or Escape leaves.
/// </summary>
// [EXT:pools-ui] OpenTPW's own rows and texts; the original game has no pools
public sealed partial class ParkHud
{
	/// <summary>The Miscellaneous list's pool paint row (row id -3).</summary>
	public static readonly BuildItem PoolPaintRow = new( "pool:paint", -3, BuildCategory.Features, 0, ParkEconomy.PoolCellCost, null, Array.Empty<string>() );
	/// <summary>The Miscellaneous list's pool erase row (row id -4).</summary>
	public static readonly BuildItem PoolEraseRow = new( "pool:erase", -4, BuildCategory.Features, 0, 0, null, Array.Empty<string>() );

	/// <summary>[EXT:pools-slides] The Miscellaneous list's slide track row (row id -5): continues the first unfinished slide.</summary>
	public static readonly BuildItem PoolSlideRow = new( "pool:slide", -5, BuildCategory.Features, 0, ParkEconomy.SlidePieceCost, null, Array.Empty<string>() );

	/// <summary>Whether <paramref name="item"/> is one of the pool rows.</summary>
	public static bool IsPoolRow( BuildItem item ) => item.InfoId is -3 or -4 or -5 && item.Entry == null && item.Category == BuildCategory.Features;

	private string PoolRowName( BuildItem item ) => strings.Extra( item.InfoId == PoolPaintRow.InfoId ? OpenTpwText.PoolRowPaint : item.InfoId == PoolSlideRow.InfoId ? OpenTpwText.PoolRowSlide : OpenTpwText.PoolRowErase );

	private long PoolRowPrice( BuildItem item ) => item.InfoId == PoolPaintRow.InfoId ? (level.Park?.Economy.CellCost( CellPurchase.Pool ) ?? 0)
		: item.InfoId == PoolSlideRow.InfoId ? (level.Park?.Economy.CellCost( CellPurchase.SlidePiece ) ?? 0) : 0;

	private bool IsPoolRowAvailable() => FeatureGates.Pools && !level.IsReadOnlyVisit && level.PoolTool != null;

	/// <summary>
	/// Starts the pool tool in the paint or erase mode after ending the other park tools; closes the window. Choosing the other row while the tool is on switches the mode and keeps
	/// the strokes that can be undone.
	/// </summary>
	private void BeginPoolTool( BuildItem item )
	{
		if ( !IsPoolRowAvailable() || level.PoolTool is not { } tool )
		{
			PostMessage( strings.Extra( OpenTpwText.NotAvailable ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return;
		}
		// [EXT:pools-slides] the slide row continues the first unfinished slide
		if ( item.InfoId == PoolSlideRow.InfoId )
		{
			if ( level.NextUnfinishedSlideTower() is not { } tower )
			{
				PostMessage( strings.Extra( OpenTpwText.PoolSlideNoTower ) );
				GameAudio.PlayUi( GameAudio.ErrorEvent );
				return;
			}
			if ( level.EnterSlideTool( tower ) )
			{
				CloseBuyWindow();
				PostMessage( strings.Extra( OpenTpwText.PoolSlideHint ) );
			}
			return;
		}
		var erase = item.InfoId == PoolEraseRow.InfoId;
		if ( !tool.IsActive )
		{
			level.EndBuildTools();
			level.StaffPlacement?.Cancel();
			LeaveBuildTool();
		}
		if ( !tool.Begin( erase ) )
			return;
		CloseBuyWindow();
		PostMessage( strings.Extra( erase ? OpenTpwText.PoolEraseHint : OpenTpwText.PoolPaintHint ) );
	}

	/// <summary>
	/// The pool tool's HUD side: the hovered cell and its help, press and drag to write cells, right click leaves. Clicks inside the tool are captured here so
	/// <see cref="Level"/>'s park click skips them. A refused stroke says so once, when it ends.
	/// </summary>
	private bool UpdatePoolTool( UiContext context, UiInput input, bool inPark )
	{
		if ( level.PoolTool is not { IsActive: true } tool )
			return false;
		if ( input.RightPressed && inPark && GameOptions.Current.RmbCancel )
		{
			tool.Cancel();
			return toolPress = true;
		}
		// Backspace takes back the last stroke
		if ( input.Backspaces > 0 && !tool.IsDragging )
			tool.Undo();
		if ( !inPark )
		{
			// a drag that leaves the park view ends there
			var ended = tool.IsDragging;
			tool.HoverNone();
			if ( ended )
				ReportPoolStroke( tool );
			return false;
		}
		tool.Hover( cursorX, cursorY );
		context.HoverHelp = strings.Extra( tool.IsSlide ? OpenTpwText.PoolSlideHelp : tool.IsErasing ? OpenTpwText.PoolEraseHelp : OpenTpwText.PoolPaintHelp );
		if ( input.LeftPressed )
		{
			refusalClick.Press();
			tool.Press( cursorX, cursorY );
			return toolPress = true;
		}
		if ( tool.IsDragging )
		{
			if ( input.LeftDown )
				return toolPress = true;
			ReportPoolStroke( tool );
			return toolPress = true;
		}
		return false;
	}

	/// <summary>Ends the stroke: the lay sound if it wrote cells, and one refusal sound and message if a cell was refused.</summary>
	private void ReportPoolStroke( PoolTool tool )
	{
		var stroke = tool.Release();
		if ( stroke is { Cells.Count: > 0 } )
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
		if ( tool.SlideCompleted && stroke != null )
			PostMessage( strings.Extra( OpenTpwText.PoolSlideDone ) );
		if ( tool.LastRefusal is not { } refusal )
			return;
		refusalClick.Refused( refusal.Cell.X, refusal.Cell.Y );
		PostMessage( strings.Extra( refusal.Reason switch
		{
			PoolBuildResult.SlideRefused => tool.LastSlideRefusal switch
			{
				PoolSlideResult.TooSteep => OpenTpwText.PoolSlideTooSteep,
				PoolSlideResult.TooGentle => OpenTpwText.PoolSlideTooGentle,
				PoolSlideResult.TooLow => OpenTpwText.PoolSlideTooLow,
				PoolSlideResult.NotNext => OpenTpwText.PoolSlideNotNext,
				PoolSlideResult.NotOverPool => OpenTpwText.PoolSlideNotPool,
				PoolSlideResult.BadStart => OpenTpwText.PoolSlideBadStart,
				_ => OpenTpwText.CannotBuildHere
			},
			PoolBuildResult.NotEnoughMoney => OpenTpwText.NotEnoughMoney,
			PoolBuildResult.TooSteep => OpenTpwText.PoolTooSteep,
			PoolBuildResult.Swimmers => OpenTpwText.PoolSwimmersInWater,
			PoolBuildResult.ChangingRoomAccess => OpenTpwText.PoolChangingRoomNeedsWater,
			PoolBuildResult.SlideRiders => OpenTpwText.PoolSlideRiders,
			PoolBuildResult.SlideWater => OpenTpwText.PoolSlideWaterNeeded,
			PoolBuildResult.ExtraNeedsWater => OpenTpwText.PoolExtraNeedsWater,
			_ => OpenTpwText.CannotBuildHere
		} ) );
	}
}
