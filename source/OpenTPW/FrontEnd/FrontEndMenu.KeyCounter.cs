using System.Globalization;
using OpenTPW.UI.Original;

namespace OpenTPW.FrontEnd;

public sealed partial class FrontEndMenu
{
	private void AddKeyCounter( UiScreen screen )
	{
		// [BIN:TPW-EXE:0x4b96be..0x4b9807 lobby count] EC is visible for positive keys outside Instant Action;
		// EE receives "%d x". This is only EC's two children, not EA/EB affordability art or F0/F1 navigation.
		// [EXT:no-player-profiles] OpenTPW has no player profiles, so the count is the one player's saved Full Simulation
		// ledger, read when the menu is built (returning from a park rebuilds it); the advisor timer's live refresh of the
		// panel is not made ([APPROX:ADVISOR-025], AdvisorSequence.cs).
		var keys = actions.PlayerKeySnapshot;
		var visible = keys > 0;
		var table = OriginalLayoutTables.Lobby;
		screen.Add( new UiModelImage { Id = "lobbyGoldenKey", Model = table.Control( 0x1E0ED ).Model!,
			Bounds = table.Control( 0x1E0ED ).Rect, Anchor = UiAnchor.TopRight, Visible = visible } );
		screen.Add( new UiLabel { Id = "lobbyGoldenKeys", Text = () => keys?.ToString( CultureInfo.InvariantCulture ) + " x",
			Font = fonts => fonts.Balance, Align = UiAlign.Right, Fit = true,
			Bounds = table.Control( 0x1E0EE ).Rect, Anchor = UiAnchor.TopRight, Visible = visible } );
	}
}
