using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>Path-tool check of the native smoke test: the park HUD lays a path from synthetic clicks, as a player would.</summary>
internal sealed partial class SandboxSmokeTest
{
	private const int SmokePathLength = 5;
	private readonly OpenTPW.Hud.ParkHud? hud;
	private readonly UiContext? hudContext;
	private bool pathToolVerified;

	/// <summary>
	/// Original levels with the HUD: a press on an empty owned cell starts the path tool, a hover shows the ghost,
	/// a press five cells along lays and charges the line, and a press on its end ends the tool. The clicks go
	/// through <see cref="OpenTPW.Hud.ParkHud.Update"/> at the projected cell centres.
	/// </summary>
	private void VerifyHudPathTool()
	{
		pathToolVerified = true;
		if ( hud == null || hudContext == null || level.Paths == null || level.Guests == null )
			return;
		var canvas = hudContext.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		bool Clickable( (int X, int Y) cell, out System.Numerics.Vector2 point ) =>
			level.TryProjectCell( cell.X, cell.Y, viewport, out point )
			&& point.X > canvas.Width * 0.3f && point.X < canvas.Width * 0.7f && point.Y > canvas.Height * 0.2f && point.Y < canvas.Height * 0.6f
			&& !hud.Stack.Covers( canvas, point )
			&& level.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell;
		var grid = level.Guests.Grid;
		(int X, int Y) start = (-1, -1), end = (-1, -1);
		for ( var index = 0; index < grid.CountX * grid.CountY && start.X < 0; index++ )
		{
			var cell = (X: index % grid.CountX, Y: index / grid.CountX);
			foreach ( var (dx, dy) in GuestPathGrid.Directions )
			{
				var last = (cell.X + dx * SmokePathLength, cell.Y + dy * SmokePathLength);
				if ( Enumerable.Range( 0, SmokePathLength + 1 ).All( step => level.Paths.CheckCell( cell.X + dx * step, cell.Y + dy * step ) == CellBuildResult.Ok )
					&& Clickable( cell, out _ ) && Clickable( last, out _ ) )
				{
					(start, end) = (cell, last);
					break;
				}
			}
		}
		Require( start.X >= 0, "find an empty owned on-screen line for the HUD path tool" );
		Clickable( start, out var startPoint );
		Clickable( end, out var endPoint );
		var economy = level.Park?.Economy;
		var balance = economy?.Balance ?? 0;
		var walkable = grid.WalkableCount;

		hud.Update( hudContext, new UiInput( startPoint, true, true, false, false, UiKeys.None ) );
		Require( level.CellTool.Mode == CellToolMode.Path && level.CellTool.Start == start, "a HUD click on an empty owned cell starts the path tool" );
		Require( hudContext.HoverHelp != null, "the path tool shows its help line (UIHELPTEXT 443)" );
		hud.Update( hudContext, new UiInput( startPoint, false, false, true, false, UiKeys.None ) );
		hud.Update( hudContext, UiInput.Idle( endPoint ) );
		Require( level.CellTool.Ghost is { Built.Count: SmokePathLength + 1 } && grid.WalkableCount == walkable, "the HUD hover previews the line without building it" );
		hud.Update( hudContext, new UiInput( endPoint, true, true, false, false, UiKeys.None ) );
		hud.Update( hudContext, new UiInput( endPoint, false, false, true, false, UiKeys.None ) );
		var built = level.CellTool.LastCommit;
		Require( built is { Completed: true, Built.Count: SmokePathLength + 1 } && grid.WalkableCount == walkable + SmokePathLength + 1, "a second HUD click lays the snapped line" );
		Require( grid.Distance( start.X, start.Y, end.X, end.Y ) == SmokePathLength, "the built cells are linked for guests" );
		if ( economy != null )
			Require( balance - economy.Balance == built!.Charged && built.Charged == (SmokePathLength + 1) * economy.CellCost( CellPurchase.Path ), "each built cell is charged Costs.PathCell" );
		Require( level.CellTool.IsActive && level.CellTool.Start == end, "the tool continues from the line's end" );
		hud.Update( hudContext, new UiInput( endPoint, true, true, false, false, UiKeys.None ) );
		hud.Update( hudContext, new UiInput( endPoint, false, false, true, false, UiKeys.None ) );
		Require( !level.CellTool.IsActive, "clicking the start again ends the path tool" );
		Log.Trace( $"HUD path tool: laid {built!.Built.Count} cells {start} -> {end} for ${built.Charged} through ParkHud clicks at ({startPoint.X:F0}, {startPoint.Y:F0}) -> ({endPoint.X:F0}, {endPoint.Y:F0})." );
	}
}
