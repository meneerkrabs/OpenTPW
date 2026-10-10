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
		("UI-008", "text buttons on purple_button art: each half is one end cap, drawn with its mirror image; upper half normal, lower half focused/pressed", "capture of the original front-end buttons"),
		("UI-010", "popup help box at the top centre with a dark blue backdrop", "capture of original popup help (helpbg art exists)"),
		("UI-011", "modal screens dim what is below", "captures of original dialogs"),
		("UI-012", "hover focuses, release activates, arrows/Enter/Escape navigate, P pauses, right click backs out of modal screens", "binary: input handling; KEYBOARD.str meaning"),
		("UI-013", "window sizes and inner layout of game mode, load, pause and message dialogs (the options page now follows the original table)", "captures of original dialogs"),
		("UI-014", "positions inside the lobby panel (island name, prev/enter/next), logo/title placement, right-hand Load/Options/Quit column", "capture of the original lobby screen"),
		("UI-015", "front-end flow without player profiles: Game Mode is asked on every park entry instead of fixed at player creation (the original stores it per player, STP-PPC 0x1015D220/0x1013741C), and every entry starts a new park instead of resuming the player's per-theme autosave", "PC confirmation of the Mac player and autosave flow (docs/reverse/PPC-scenarios.md, \"Park entry\")"),
		("UI-016", "lobby ISLAND angle = island yaw in degrees, height = camera target height", "binary: lobby script interpretation or capture"),
		("UI-017", "lobby camera: SPINSPEED read as radians per 0.1 s, vertical field of view 60, 3/s glide between islands, ISLANDFOV unused", "binary or capture of the lobby camera"),
		("UI-018", "lobby sky drawn as a flat SKYCOLOUR backdrop; flying meshes, rain, lightning, animations not drawn", "binary/capture of the lobby"),
		("UI-019", "fallback island position (400 + index × 200, 400) when lobby.txt has none", "none needed if lobby.txt is complete"),
		("UI-021", "bank balance text at the Mac table rectangle as is (the original repositions it from font extents and drawable size); money grouped with ',' digits", "binary 0x156ef4 placement; locale number format"),
		("UI-022", "speed control (pause, ×1, ×2, ×4) bottom-right; faster speeds only speed up the economy clock, not rides/guests", "binary: original game speed options (pause only is known)"),
		("UI-023", "test-only stub calendar (2 s/day); the game shows the economy clock (see ECON tags)", "none for the game path"),
		("UI-024", "layout inside the build and info arms (category buttons, title, three-slot pages/arrows, unresearched items listed and sorted under their own names where the original uses UITEXT 137, adaptive preview size to fit translated names/prices, stat rows, door/erase buttons)", "captures of the original arms"),
		("UI-025", "message area keeps up to 3 messages for 8 s in the f_tag frame", "binary/capture of the original message system"),
		("UI-026", "build icons: CPU orthographic projection of the preview model (P<name>.MD2, else the main model) with 30° tilt, 0.8 rad/s turn, painter sorting", "capture of the original build menu"),
		("UI-027", "a park click selects the original object occupying its grid cell", "binary: original picking"),
		("UI-028", "excitement shown as '<ExcitementLevel>%'; reliability, repair and life shown as not simulated", "capture of the original ride info; simulation"),
		("UI-029", "b_door 'down' frames mean the ride is closed; b_erase used as the delete button", "capture of the original ride panel"),
		("UI-030", "volumes in 0..10 steps (10 % each; the capture shows 75 %, so the original has finer steps), default 8, copied in the original from fields +0x38..+0x44 of the object at 0x101EC828; rotation 90 degs (the defaults set IsometricOn to 1, not proven to be this option)", "the initializer of the object at 0x101EC828 and the IsometricOn consumer"),
		("UI-031", "one placement per menu selection; Level.PlaceObject owns purchase/guest linkage and its removal handler owns scrap credits; a ride with Info.HasQueue then switches the park click to laying its queue until Back", "original build-tool continuation"),
		("UI-032", "longer labels fall back to the small font; catalogue names greedily wrap in their slots", "captures of translated original screens"),
		("UI-034", "a fully opaque texture on a transparent (flag 0x2) model slot keys out black with a soft edge: transparent up to channel 6, opaque from 48, alpha ramped and colour un-premultiplied between (only ipan in the lobby f_lobbutbg panel)", "the original's render state for flagged texture slots"),
		("UI-035", "start-up movies: the Mac build's order (bf, then a day-of-month trailer) is assumed for the PC .tgq files; input held at launch is ignored until released; movies are letterboxed to their aspect instead of stretched to the window width", "PC executable analysis or captures of the PC start-up sequence"),
		("UI-036", "options slider: ball centre moves linearly over the track for value index 0..steps-1; click/drag sets the nearest step", "capture of the original slider ends or binary slider code"),
		("UI-037", "option bar label colour (16,16,48), no drop shadow", "exact label colour from a capture or the font palette"),
		("UI-038", "3D card rendering, videocard and audio quality are drawn fixed and disabled (OpenTPW has no software renderer, card choice or audio quality)", "none for the game path; the original lets the player change them"),
		("UI-039", "option label size: letter box about 58 % of the label rectangle height, shared per page; a label whose widest value does not fit drops alone to the largest size that does", "capture of the original option labels in several languages"),
		("UI-040", "autorun launcher focus rectangle: dotted frame inverting the pixels with even x + y, 2 pixels inside the button (GDI DrawFocusRect brush phase unknown)", "capture of the original launcher with a focused button"),
		("UI-041", "Load Park opens a shipped park as the reference start (its own balance, Full Simulation rules) whatever Game Mode was last chosen; the original's GameType is not saved with a park but copied from the loading player's profile (mEasyModeUser)", "player profiles and what the Mac park loader 0x11acfc reads from a park file"),
		("UI-042", "HUD camera button shown disabled; its camera-view action is not implemented", "binary camera button handler"),
		("UI-043", "BF4 text drawn above 1× (HiDPI, large outputs) is magnified per glyph with Catmull-Rom and a contrast curve min(2, 0.8 × scale) instead of doubled pixels", "none: the original only drew its fonts at 1×; design decision"),
		("UI-044", "buy window list rows: ten equal rows fill the table's content region (44.4 units each); the wheel scrolls one row", "capture of the original buy list"),
		("UI-045", "a click on the Name or Price column header sorts the buy list by it (again: descending); the initial name order is the original's", "the original column header handler"),
		("UI-046", "buy window stats: excitement gauge = UsageInfo.ExcitementLevel/100, reliability gauge = 1 - Upgrades[0].WearRate/10, safe capacity = Upgrades[0].InitCapacity, working life empty; type-9 value cells drawn as plain bar gauges", "capture of the original buy window and its gauge art"),
		("UI-047", "the HUD cash change text stays 4 s after the last balance change and sums the changes within that time", "capture of the original cash trend display"),
		("UI-048", "buy window control 512 (beside the title) is left empty", "capture of the original buy window"),
		("UI-049", "buy window scroll ball travel: its top runs from the track's top (first row) to a ball height above the track's bottom (last row)", "capture of the original scroll bar ends"),
		("UI-050", "buy list third column (1673-1724), its header button 18 and control 491 have no known content and stay empty", "capture of the original buy window"),
		("UI-051", "i_dollar frame 1 for a negative balance, green_up frame 1 for a decrease; golden key frame 0 and ticket count from the economy (keys are not tracked, always 0)", "capture of the original cash display and key counter"),
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
