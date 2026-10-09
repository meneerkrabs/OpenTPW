namespace OpenTPW.UI.Original;

/// <summary>
/// Register of every UI value or rule that is not taken from original data or hard evidence
/// (docs/UI.md "Approximation register"). Each site carries a matching
/// <c>// [APPROX:UI-NNN]</c> comment; <see cref="LogOnce"/> reports them at startup.
/// </summary>
public static class UiApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("UI-001", "non-4:3 outputs keep each element at its authored distance from the nearest edge (anchors)", "original widescreen behaviour (none in 1999) / design decision"),
		("UI-002", "BF4 font size tier (SMALL/MED/BIG) chosen by logical scale thresholds 0.36 and 0.6", "binary: font selection per screen mode"),
		("UI-003", "button/arm state frames drawn at the root node pose; child translations ignored", "binary: UI model drawing code"),
		("UI-004", "UI model triangles drawn back to front by Z, grouped per texture", "binary or capture of overlapping UI parts"),
		("UI-005", "pink (255,0,255) key with neighbour colour bleed; linear filtering of UI images", "capture of edges at non-native resolutions"),
		("UI-006", "text colours (white, yellow highlight/title, green values, grey disabled), shadow and backdrop colours", "captures of original screens"),
		("UI-007", "one-pixel (×UI scale) drop shadow under UI text", "captures of original screens"),
		("UI-008", "text buttons on purple_button art: top half normal, bottom half focused/pressed", "capture of the original front-end buttons"),
		("UI-009", "option rows: label to 58%, value between arrows at 58.5%..97.5% of the f_optpanel2 frame", "capture of the original options screen"),
		("UI-010", "popup help box at the top centre with a dark blue backdrop", "capture of original popup help (helpbg art exists)"),
		("UI-011", "modal screens dim what is below", "captures of original dialogs"),
		("UI-012", "hover focuses, release activates, arrows/Enter/Escape navigate, P pauses, right click backs out of modal screens", "binary: input handling; KEYBOARD.str meaning"),
		("UI-013", "window sizes and inner layout of options, game mode, load, pause and message dialogs", "captures of original dialogs"),
		("UI-014", "positions inside the lobby panel (island name, prev/enter/next), logo/title placement, right-hand Load/Options/Quit column", "capture of the original lobby screen"),
		("UI-015", "front-end flow without player profiles; Game Mode is asked when entering a park instead of once per player", "player profiles: the original stores the mode per player (STP-PPC 0x1015D220/0x1013741C)"),
		("UI-016", "lobby ISLAND angle = island yaw in degrees, height = camera target height", "binary: lobby script interpretation or capture"),
		("UI-017", "lobby camera: SPINSPEED read as radians per 0.1 s, vertical field of view 60, 3/s glide between islands, ISLANDFOV unused", "binary or capture of the lobby camera"),
		("UI-018", "lobby sky drawn as a flat SKYCOLOUR backdrop; flying meshes, rain, lightning, animations not drawn", "binary/capture of the lobby"),
		("UI-019", "fallback island position (400 + index × 200, 400) when lobby.txt has none", "none needed if lobby.txt is complete"),
		("UI-020", "positions of buy/info/finance/research/map buttons on the main panel (shared authored centre)", "capture of the original HUD"),
		("UI-021", "positions and fonts of the date and bank balance text; money grouped with ',' digits", "capture of the original HUD; locale number format"),
		("UI-022", "speed control (pause, ×1, ×2, ×4) bottom-right; faster speeds only speed up the economy clock, not rides/guests", "binary: original game speed options (pause only is known)"),
		("UI-023", "test-only stub calendar (2 s/day); the game shows the economy clock (see ECON tags)", "none for the game path"),
		("UI-024", "layout inside the build and info arms (category buttons, title, three-slot pages/arrows sorted by Info.Id, adaptive preview size to fit translated names/prices, stat rows, door/erase buttons)", "captures of the original arms"),
		("UI-025", "message area keeps up to 3 messages for 8 s in the f_tag frame", "binary/capture of the original message system"),
		("UI-026", "build icons: CPU orthographic projection of P<name>.MD2 with 30° tilt, 0.8 rad/s turn, painter sorting", "capture of the original build menu"),
		("UI-027", "a park click selects the original object occupying its grid cell", "binary: original picking"),
		("UI-028", "excitement shown as '<ExcitementLevel>%'; reliability, repair and life shown as not simulated", "capture of the original ride info; simulation"),
		("UI-029", "b_door 'down' frames mean the ride is closed; b_erase used as the delete button", "capture of the original ride panel"),
		("UI-030", "volumes in 0..10 steps, default 8; popup help default on", "capture/registry defaults of the original options"),
		("UI-031", "one placement per menu selection; Level.PlaceObject owns purchase/guest linkage and its removal handler owns scrap credits", "original build-tool continuation"),
		("UI-032", "longer labels fall back to the small font; catalogue names greedily wrap in their slots", "captures of translated original screens"),
		("UI-033", "autorun launcher focus rectangle: dotted frame inverting the pixels with even x + y, 2 pixels inside the button (GDI DrawFocusRect brush phase unknown)", "capture of the original launcher with a focused button"),
	};

	private static bool logged;

	public static void LogOnce()
	{
		if ( logged )
			return;
		logged = true;
		foreach ( var (id, assumption, evidence) in All )
			Log?.Warning( $"[APPROX:{id}] {assumption} — evidence needed: {evidence}" );
	}
}
