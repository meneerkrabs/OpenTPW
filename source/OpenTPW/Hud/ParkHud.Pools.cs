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

	/// <summary>Whether <paramref name="item"/> is one of the pool rows.</summary>
	public static bool IsPoolRow( BuildItem item ) => item.InfoId is -3 or -4 && item.Entry == null && item.Category == BuildCategory.Features;

	private string PoolRowName( BuildItem item ) => strings.Extra( item.InfoId == PoolPaintRow.InfoId ? OpenTpwText.PoolRowPaint : OpenTpwText.PoolRowErase );

	private long PoolRowPrice( BuildItem item ) => item.InfoId == PoolPaintRow.InfoId ? (level.Park?.Economy.CellCost( CellPurchase.Pool ) ?? 0) : 0;

	private bool IsPoolRowAvailable() => FeatureGates.Pools && !level.IsReadOnlyVisit && level.PoolTool != null;

	/// <summary>Starts the pool tool in the paint or erase mode after ending the other park tools; closes the window.</summary>
	private void BeginPoolTool( BuildItem item )
	{
		if ( !IsPoolRowAvailable() || level.PoolTool is not { } tool )
		{
			PostMessage( strings.Extra( OpenTpwText.NotAvailable ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return;
		}
		level.EndBuildTools();
		level.StaffPlacement?.Cancel();
		LeaveBuildTool();
		var erase = item.InfoId == PoolEraseRow.InfoId;
		if ( !tool.Begin( erase ) )
			return;
		CloseBuyWindow();
		PostMessage( strings.Extra( erase ? OpenTpwText.PoolEraseHint : OpenTpwText.PoolPaintHint ) );
	}

	/// <summary>
	/// The pool tool's HUD side: the hovered cell and its help, press and drag to write cells, right click leaves. Clicks inside the tool are captured here so
	/// <see cref="Level"/>'s park click skips them.
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
			tool.HoverNone();
			if ( !input.LeftDown )
				tool.Release();
			return false;
		}
		tool.Hover( cursorX, cursorY );
		context.HoverHelp = strings.Extra( tool.IsErasing ? OpenTpwText.PoolEraseHelp : OpenTpwText.PoolPaintHelp );
		if ( input.LeftPressed )
		{
			var result = tool.Press( cursorX, cursorY );
			ReportPoolResult( result, tool );
			return toolPress = true;
		}
		if ( tool.IsDragging )
		{
			if ( input.LeftDown )
				return toolPress = true;
			var stroke = tool.Release();
			if ( stroke != null )
				GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
			if ( tool.LastRefusal is { } refusal )
				ReportPoolResult( refusal.Reason, tool );
			return toolPress = true;
		}
		return false;
	}

	private void ReportPoolResult( PoolBuildResult? result, PoolTool tool )
	{
		if ( result is null or PoolBuildResult.Ok or PoolBuildResult.Existing )
			return;
		refusalClick.Press();
		if ( tool.HoverCell is { } cell )
			refusalClick.Refused( cell.X, cell.Y );
		PostMessage( strings.Extra( result switch
		{
			PoolBuildResult.NotEnoughMoney => OpenTpwText.NotEnoughMoney,
			PoolBuildResult.TooSteep => OpenTpwText.PoolTooSteep,
			_ => OpenTpwText.CannotBuildHere
		} ) );
	}
}
