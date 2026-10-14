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
		("UI-002", "BF4 font size tier (SMALL/MED/BIG) chosen by logical scale thresholds 0.36 and 0.6", "binary: font selection per screen mode"),
		("UI-003", "button/arm state frames drawn at the root node pose; child translations ignored", "binary: UI model drawing code"),
		("UI-004", "UI model triangles drawn back to front by Z, grouped per texture", "binary or capture of overlapping UI parts"),
		("UI-005", "pink (255,0,255) key with neighbour colour bleed; linear filtering of UI images", "capture of edges at non-native resolutions"),
		("UI-006", "text colours (white, yellow highlight/title, green values, grey disabled), shadow and backdrop colours", "captures of original screens"),
		("UI-007", "one-pixel (×UI scale) drop shadow under UI text", "captures of original screens"),
		("UI-008", "text buttons on purple_button art: each half is one end cap, drawn with its mirror image; upper half normal, lower half focused/pressed", "capture of the original front-end buttons"),
		("UI-010", "popup help box at the top centre with a dark blue backdrop", "capture of original popup help (helpbg art exists)"),
		("UI-011", "modal screens dim what is below", "captures of original dialogs"),
		("UI-012", "the arrows/Enter/Escape/P key map, hover focus, release activation and right-click back-out are OpenTPW's mapping; the original reads six named key groups (0x747f38-0x748378, initialised in 0x40cb80, table 0x748028) with KEYBOARD.str table overrides (0x3fc) and the RMBCancelOn option (+0x39, default 1)", "tracing those key groups and the KEYBOARD.str overrides to OpenTPW's UI key map"),
		("UI-013", "window sizes and inner layout of game mode, load, pause and message dialogs (the options page now follows the original table)", "captures of original dialogs"),
		("UI-014", "positions inside the lobby panel (island name, prev/enter/next), logo/title placement, right-hand Load/Options/Quit column", "capture of the original lobby screen"),
		("UI-015", "front-end flow without player profiles: Game Mode is asked on every park entry instead of fixed at player creation (the original stores it per player, STP-PPC 0x1015D220/0x1013741C), and every entry starts a new park instead of resuming the player's per-theme autosave", "PC confirmation of the Mac player and autosave flow (docs/reverse/PPC-scenarios.md, \"Park entry\")"),
		("UI-016", "lobby ISLAND angle = island yaw in degrees, height = camera target height", "binary: lobby script interpretation or capture"),
		("UI-017", "lobby camera: SPINSPEED read as radians per 0.1 s, vertical field of view 60, 3/s glide between islands, ISLANDFOV unused", "binary or capture of the lobby camera"),
		("UI-018", "lobby sky eases toward the nearest island's SKYCOLOUR without the original's red-toward-blue quirk and its settings gate; flying meshes and island/gate animations not drawn; the lightning flash's blend is not decoded (an opaque white frame)", "capture of the lobby sky; the flying-mesh and animation systems"),
		("UI-021", "the HUD cash text (control 47) sits at the Mac table rectangle as is; the original repositions control 47", "control 47's repositioning (the Mac placement 0x156ef4 and its PC twin)"),
		("UI-024", "layout inside the build and info arms (category buttons, title, three-slot pages/arrows, unresearched items listed and sorted under their own names where the original uses UITEXT 137, adaptive preview size to fit translated names/prices, stat rows, door/erase buttons)", "captures of the original arms"),
		("UI-025", "message area keeps up to 3 messages for 8 s in the f_tag frame", "binary/capture of the original message system"),
		("UI-026", "build icons: CPU orthographic projection of the preview model (P<name>.MD2, else the main model) with 30° tilt, 0.8 rad/s turn, painter sorting", "capture of the original build menu"),
		("UI-027", "a park click selects the original object occupying its grid cell", "binary: original picking"),
		("UI-028", "excitement shown as '<ExcitementLevel>%'; reliability, repair and life shown as not simulated", "capture of the original ride info; simulation"),
		("UI-029", "b_door 'down' frames mean the ride is closed; b_erase used as the delete button", "capture of the original ride panel"),
		("UI-030", "the volume sliders step 1 % within 0..100", "the original slider's step granularity within 0..100"),
		("UI-031", "one placement per menu selection; Level.PlaceObject owns purchase/guest linkage and its removal handler owns scrap credits; a ride with Info.HasQueue then switches the park click to laying its queue until Back", "original build-tool continuation"),
		("UI-032", "longer labels fall back to the small font; catalogue names greedily wrap in their slots", "captures of translated original screens"),
		("UI-034", "a fully opaque texture on a transparent (flag 0x2) model slot keys out black with a soft edge: transparent up to channel 6, opaque from 48, alpha ramped and colour un-premultiplied between (only ipan in the lobby f_lobbutbg panel)", "the original's render state for flagged texture slots"),
		("UI-036", "options slider: ball centre moves linearly over the track for value index 0..steps-1; click/drag sets the nearest step", "capture of the original slider ends or binary slider code"),
		("UI-037", "option bar label colour (16,16,48), no drop shadow", "exact label colour from a capture or the font palette"),
		("UI-039", "option label size: letter box about 58 % of the label rectangle height, shared per page; a label whose widest value does not fit drops alone to the largest size that does", "capture of the original option labels in several languages"),
		("UI-041", "Load Park opens a shipped park as the reference start (its own balance, Full Simulation rules) whatever Game Mode was last chosen; the original's GameType is not saved with a park but copied from the loading player's profile (mEasyModeUser)", "player profiles and what the Mac park loader 0x11acfc reads from a park file"),
		("UI-042", "the camera button cycles host Standard/Close/Overview views ([EXT:camera-view-cycle]); the original opens slide-out panel 33 with camcorder mode (control 99, help 479) and send a postcard (control 100, help 480) and starts sound 0x131, which is not built", "the two-button slide-out panel's actions (HUD case 0x27, 0x498bb0/0x498bd0)"),
		("UI-044", "buy window list rows: ten equal rows fill the table's content region (44.4 units each); the wheel scrolls one row", "capture of the original buy list"),
		("UI-045", "a click on the Name or Price column header sorts the buy list by it (again: descending); the initial name order is the original's", "the original column header handler"),
		("UI-046", "buy window stats: excitement gauge = UsageInfo.ExcitementLevel/100, reliability gauge = 1 - Upgrades[0].WearRate/10, safe capacity = Upgrades[0].InitCapacity, working life empty; type-9 value cells drawn as plain bar gauges", "capture of the original buy window and its gauge art"),
		("UI-047", "the HUD cash change text stays 4 s after the last balance change and sums the changes within that time", "capture of the original cash trend display"),
		("UI-048", "buy window control 512 (beside the title) is left empty", "capture of the original buy window"),
		("UI-049", "buy window scroll ball travel: its top runs from the track's top (first row) to a ball height above the track's bottom (last row)", "capture of the original scroll bar ends"),
		("UI-050", "buy list third column (1673-1724), its header button 18 and control 491 have no known content and stay empty", "capture of the original buy window"),
		("UI-051", "i_dollar frame 1 for a negative balance, green_up frame 1 for a decrease; golden key frame 0 and ticket count from the economy (keys are not tracked, always 0)", "capture of the original cash display and key counter"),
		("UI-060", "finance graph line colours, scaling and default selected series use the tracked monthly ledger values", "original financial graph capture and its sample/render handler"),
		("UI-061", "entry and staff-cost screens use host layouts and unit adjustment buttons over actual economy values", "original entry and staff-cost screen tables and handlers"),
		("UI-063", "loan list has seven equal rows and inferred scrollbar travel; its summary uses the bounded monthly ledger rather than the original lifetime counters", "loan-list capture, scroll handler and original lifetime finance counters"),
		("UI-064", "research workload is adjustable with linear 0..100 mouse/keyboard travel; native slider quantization remains unqualified", "original workload slider handler and control mapping"),
		("UI-065", "research effort sliders use linear 0..100 travel across their authored tracks; the original range is SetRange(0,100) (0x4aa6bd)", "the generic slider's travel and end stops"),
		("UI-066", "research progress is a simple fill inside the fluid model cell; the original draws no fill but animates the fluid model (0x4a95b0: (p+20)/100 in 16.16, state 3, frame limit 0x30, winding down to 0x20)", "porting the fluid model animation"),
		("UI-080", "staff list has ten equal rows in the table content area and the wheel moves one row", "capture of the original staff list"),
		("UI-081", "applicant details show the expected monthly wage and grade-derived skill as text; the original shows a skill gauge (9346, type 9: skill byte × 1024/5) and a mini balance (9349: staff costs + wage, balance − wage − two globals) and no grade or wage text", "porting the skill gauge and the mini balance"),
		("UI-082", "selected staff role and grade are text in the portrait frame; the original draws a 3D candidate preview in control 9348's rectangle (0x49ba00)", "the candidate preview renderer (0x49ba00)"),
		("UI-083", "Enter or a second click on the selected applicant hires them; the original's list message 0x400 hires (0x49b988 → 0x507bf0), closes the window and posts 0x135", "the gesture that produces list message 0x400 (input 0x11006/0x11007)"),
		("UI-084", "the initial sort column state of OpenTPW's lists (the generic sort 0x665a44 toggles the same column and starts a new column ascending; implemented with column 0 ascending initially)", "the initial column state of each original list"),
		("UI-085", "the 512 pixel theme map places five-pixel terrain cells centrally; live overlays use that mapping", "original map overlay alignment capture"),
		("UI-086", "paths, queues and placed objects use plain coloured cells over the shipped theme map image; the layer toggles are the original's independent bits (the layer word starts as 1 in the original) but their colours and the control-to-category assignment are not traced, so all four category layers start on", "the original map layer colours and which category button owns which layer bit"),
		("UI-091", "staff and visitor locators cycle actual actor positions and focus the host park camera; the original's 0x402 path (centre the camera on the cell, 0x4867b0, and close) is verified", "the HUD locate-button handlers"),
		("UI-092", "an explicit dismiss button below the portrait confirms with the localized prompt before calling economy Fire; the original dismisses with b_fire (1292) on the per-employee panel (0x4b6080, table 0x756f48, jump 0x4b5e5d) and confirms (UITEXT 0x18d) only when [0x78d90f] is set", "porting the per-employee panel and its confirmation switch"),
		("UI-093", "the ride view is entered with the E key on the selected ride, not the ride window's \"Ride it!\" button; the heading is the park camera's forward", "the ride window's Ride it button art and layout, and the Ride it handler FUN_004e15b0"),
		("UI-094", "the ride view is a first-person eye on the first train's front car (coaster) or the carriage's height (prototype), looking along the track or the entry heading; the walk, sway and field of view are not reproduced", "CMMFirstPersonTarget (Mac 0x1006CF44) and CMMCoaster Update bodies"),
		("UI-095", "leaving the ride view restores its prior camera instance, including the host terrain walk; native walking camera and exit behavior remain unverified", "the first-person walk's camera state (gui_CameraFlags 0x400) and the exit handler FUN_0042a190"),
		("UI-096", "only the pause menu (and the screens opened from it) stops the park simulation; the original's other game-pause callers (the full-screen map, the message dialog, the Load dialog and 0x1015F67C) do not stop it here", "which OpenTPW windows those screens correspond to and whether the pause gate +0x3C is set when they open in normal play"),
		("UI-900", "a second press of a window key closes that window (B/I/F/R/Space/L/T/G/H), and the All Staff (S) and All Items (A) keys stay unbound because the host camera pans with them; the original closes the window with Esc and re-opens it on a repeat key (0x488ba0, key group \"shortcuts\" 0x748378) and A and S are real keys (allitems, allstaff)", "none: toggle-close and leaving S/A unbound are OpenTPW choices"),
		("UI-901", "the happiness gauge shows the visitors' mean happiness as a rising red-yellow-green bar in the gauge model's empty dish", "capture of the original gauge and its value drawing"),
		("UI-903", "the blueprint-turn keys (comma/full stop) rotate through Level.RotateBuild's 90-degree steps; the original sets the build angle to (angle + 90) mod 360 and (angle + 270) mod 360 (0x40c120/0x40c150, global 0x81d7a4)", "the sign mapping of the two keys onto Level.RotateBuild"),
		("UI-904", "the staff-locator key (Ctrl+S) cycles staff roles in the staff window's role order; the original opens the locator panel (0x481790 → 0x4b3f60, role radios 0x95e..0x964) and cycles through staff with buttons 1293/1295 of the per-employee panel", "porting the locator panel and the per-employee panel's cycling buttons"),
		("UI-905", "the All Attractions window: staff/buy window geometry with the shipped list_all frame, four category tabs and name/status list columns; the decoded original tables are not ported (attractions 0x7508e0, per-category lists 0x750ab8/0x750ba0/0x750ca0, list id 400 at [266,318,1825,876])", "porting the decoded window tables"),
		("UI-907", "the All Visitors window: staff/buy window geometry and number/status/happiness list columns; the decoded original table is not ported (visitors 0x7506c8, six columns)", "porting the decoded window table"),
		("UI-909", "entry points: the original opens its overview windows with the A, S and V keys and the cross-navigation buttons (buy window 511 opens hire, hire 9365 opens buy); OpenTPW adds text buttons ([EXT:overview-entry-buttons]), b_svisit opens the visitors window from the staff window, the plain V key toggles the visitors window and A/S stay with the WASD pan; the shipped b_allthinginfo art cannot be used, its texture b_allthginfo is absent from ui.wad", "the original entry buttons' rectangles and handlers"),
		("UI-910", "row and button semantics: an attractions row activation or the select button selects the object in the park (info arm) and closes, a visitors row activation or b_gopeep locates the visitor's live position and closes, b_erase deletes through the existing removal and confirmation; the original's message 0x402 centres the camera on the cell (0x4867b0) and closes, 0x400 opens the object's info panel and keeps the list", "the gestures that produce messages 0x400 and 0x402 (b_goto's art also has no texture in ui.wad)"),
		("UI-920", "Ctrl+H toggles the existing top-edge contextual popup through a HUD-local override; until toggled it follows the global popup-help option", "original help-bar geometry and toggle state lifetime"),
		("UI-930", "world visitor presses hit the current sprite rectangle, selecting nearest projected depth then lowest id; transparent pixels, terrain/object occlusion and partially clipped sprites are not reproduced", "original visitor picking geometry, occlusion and input precedence"),

		("UI-931", "theme entry offers an explicit New/Continue choice for an application-owned saved park; saved mode/start kind remain authoritative and no native profile or autosave flow is reproduced", "original profile and theme-entry UI behavior"),
		("UI-932", "plain C arms a terrain click for a host first-person inspection camera; terrain height, eye height, movement/look tuning and flat-plane entry picking are approximations, without object collision, photo or recording", "native camcorder entry, walk movement, collision, eye height, look controls and camera persistence"),
		("UI-933", "an editable selected shop or sideshow exposes a host decimal price editor in the information arm; it changes only the mapped instance Price using the existing nonnegative int API, without native slider/layout or quality/prize policy reconstruction", "original shop/sideshow control placement, native slider bounds and mode gating"),
		("UI-934", "shared Load chooser uses an eight-row scroll viewport with explicit selection and activation; every supplied own, sandbox and original-reference entry remains reachable", "original Load chooser navigation, selection and viewport behavior"),
		("UI-935", "selected factory-owned ordinary TPW rides expose a host next-tier cost confirmation calling the existing upgrade purchase API; live catalogue defaults remain separate from authored CAP/DUR", "original upgrade window geometry, confirmation and live reset/performance behavior"),
		("UI-940", "the lobby effects advance at a fixed 30 ticks a second; the original steps them once per rendered frame", "the original's frame rate"),
		("UI-941", "only the lobby islands' own draws advance the lobby generator; its flying meshes and camera wander also draw from it in the original, so strikes and one-shots come in a different rhythm", "the flying-mesh and camera-wander systems"),
		("UI-942", "the globe's music and ambience (cat_globallobbysfx 2 and 3) replace the lobby's while the globe is up, and the enter-park sound (event 4) plays whatever game mode follows; the original keeps or stops the lobby's own sentences untraced and skips event 4 for game type 2", "the lobby music handles while the globe component runs; the game-type check at 0x10183CC0"),
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
