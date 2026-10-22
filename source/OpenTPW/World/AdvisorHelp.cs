using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// One contextual-help message of the original: a scored message of the tutorial group whose score callback is the context-help
/// getter (0x59E8C0). The window code posts it when the window opens (0x486B00) and withdraws it when the window closes (0x486B40).
/// </summary>
/// <param name="GameMode">The descriptor's game-mode selector: 0 full simulation only, 1 Instant Action only, 2 always (0x59C0A0).</param>
internal readonly record struct AdvisorHelpMessage( int MessageId, int FirstResponseId, int ResponseCount, byte Group, byte MaximumPendingDuplicates, byte GameMode );

/// <summary>
/// The shipped help-message table (<c>content/data/advisor-help-messages.toml</c>): the 81 context-help descriptors (of the 82 that share the
/// score callback 0x59E8C0; 291 names the absent response 551) of the Windows executable's table at 0x76E300, copied for interoperability by <c>tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py</c>.
/// </summary>
internal static class AdvisorHelpTable
{
	public const string RelativePath = "content/data/advisor-help-messages.toml";
	private static readonly Regex Row = new( @"^\s*\{ message = (\d+), response = (\d+), count = (\d+), group = (\d+), duplicates = (\d+), mode = (\d+) \},?\s*$", RegexOptions.CultureInvariant );
	private static IReadOnlyList<AdvisorHelpMessage>? loaded;

	/// <summary>The table shipped with OpenTPW (loaded on first use; empty when the file is missing or damaged).</summary>
	public static IReadOnlyList<AdvisorHelpMessage> Messages => loaded ??= LoadShipped();

	private static IReadOnlyList<AdvisorHelpMessage> LoadShipped()
	{
		var path = Path.Combine( AppContext.BaseDirectory, RelativePath );
		try
		{
			return Parse( File.ReadAllText( path ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Advisor help messages unavailable ({path}): {exception.Message}" );
			return Array.Empty<AdvisorHelpMessage>();
		}
	}

	public static IReadOnlyList<AdvisorHelpMessage> Parse( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		var rows = new List<AdvisorHelpMessage>();
		var seen = new HashSet<int>();
		var inArray = false;
		var closed = false;
		var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( !inArray && !closed && line.Trim() == "messages = [" )
			{
				inArray = true;
				continue;
			}
			if ( inArray && line.Trim() == "]" )
			{
				inArray = false;
				closed = true;
				continue;
			}
			var match = Row.Match( line );
			if ( !inArray || !match.Success )
				throw new InvalidDataException( $"Advisor help messages line {lineNumber} is not a message row." );
			int Value( int group ) => int.Parse( match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture );
			if ( Value( 3 ) <= 0 || Value( 4 ) > byte.MaxValue || Value( 5 ) > byte.MaxValue || Value( 6 ) > 2 )
				throw new InvalidDataException( $"Advisor help message {Value( 1 )} has a value out of range." );
			if ( !seen.Add( Value( 1 ) ) )
				throw new InvalidDataException( $"Advisor help message {Value( 1 )} appears twice." );
			rows.Add( new AdvisorHelpMessage( Value( 1 ), Value( 2 ), Value( 3 ), (byte)Value( 4 ), (byte)Value( 5 ), (byte)Value( 6 ) ) );
		}
		if ( !closed )
			throw new InvalidDataException( "Advisor help messages have no closed 'messages' array." );
		return rows;
	}
}

/// <summary>
/// The contextual help the HUD's windows ask for. Opening a window posts its help message and closing it withdraws that message:
/// pending help is dropped, and help being spoken is cut off (see <see cref="AdvisorScoreQueue.Withdraw"/>). A window's message is the
/// one its builder posts through 0x486B00; the windows are identified by the title strings their builders load.
/// </summary>
internal static class AdvisorUiHelp
{
	/// <summary>
	/// The help message of each OpenTPW HUD window (by <see cref="UI.Original.UiScreen.Name"/>) whose original window has a traced message.
	/// </summary>
	// [BIN:TPW-EXE:0x004ACC70 buy window] title texts 0x77-0x7A (Buy Ride/Shop/Sideshow/Misc) and posts message 0xBC (188) through 0x486B00 at 0x4AD1E7; the close handler withdraws it through 0x486B40 at 0x4AC421
	// [BIN:TPW-EXE:0x0049BDD0 hire staff window] hire titles 0x8B-0x92: posts 0xBD (189) at 0x49C805, withdraws it at 0x49B7E6
	// [BIN:TPW-EXE:0x00495AA0 all attractions window] titles 0x4E-0x51 (All Rides/Shops/Sideshows/Misc Items): posts 0xBF (191) at 0x495BEC, withdraws it at 0x49531D
	// [BIN:TPW-EXE:0x0049FB30 loans window] lender columns 0xAA-0xB0: posts 0xC3 (195) at 0x4A0286, withdraws it at 0x49F53E
	// [BIN:TPW-EXE:0x0049AC60 financial information window] titles 0xA2-0xA9 (Bank Balance .. Staff Costs): posts 0xC5 (197) at 0x49B503, withdraws it at 0x49A139
	// [BIN:TPW-EXE:0x004A52A0 park information window] titles 0x97-0x9E (People In Park .. Top 3 Thoughts): posts 0xC7 (199) at 0x4A5DF3, withdraws it at 0x4A49F0
	// [BIN:TPW-EXE:0x004B6080 employee window] posts 0xC0 (192) at 0x4B60B9, withdraws it at 0x4B5CF8 (message 0x14, with 0xD4 at 0x4B5D05)
	// [BIN:TPW-EXE:0x004B79B0 visitor window] posts 0x12E (302) as it opens (0x486B00 at 0x4B79C9), withdraws it on message 0x14 (0x4B7720, 0x486B40)
	// [BIN:TPW-EXE:0x00493530 all visitors window] columns 0x71-0x76 (Visitor Number .. Happiness): posts 0x11E (286) at 0x4937CA, withdraws it at 0x4933D2
	// [BIN:TPW-EXE:0x004AA480 research window] controls 0xD2D0B-0xD2D10 (the effort sliders and fluid panels of the research window): its builder posts 0xC8 (200) as its last act (0x4AAE9D), the window procedure 0x4A96B0 withdraws it when it closes (0x4A97C7) and passes the keys it does not handle to the default window procedure (0x4A9B3A), so F1 repeats the advice there too
	// [BIN:TPW-EXE:0x00498D80 ticket price window] title text 0xA0 (Ticket Price): posts 0x12F (303) at 0x499038, withdraws it at 0x498CC7
	// [BIN:TPW-EXE:0x0049B9DD hire help] the hire posts 0x135 (309, place your new staff member) at 0x49B9DD once the hire window is closed; the Place tool's end (0x46C890) withdraws it at 0x46C8D4
	public static readonly IReadOnlyDictionary<string, int> Windows = new Dictionary<string, int>( StringComparer.Ordinal )
	{
		["buy"] = 188,
		["staff"] = 189,
		["attractions"] = 191,
		["employee"] = 192,
		["loans"] = 195,
		["finance"] = 197,
		["parkInfo"] = 199,
		["research"] = 200,
		["visitors"] = 286,
		["visitorInfo"] = 302,
		["entryPrice"] = 303,
		[Hud.ParkHud.StaffPlacementHelp] = 309,
		// [BIN:TPW-EXE:0x005F0B40 map window] the builder posts 0x130 (304) as it opens (0x486B00 at 0x5F100D) and the close path withdraws it (0x486B40 at 0x5F123C)
		[Hud.ParkHud.MapScreenName] = 304,
	};

	/// <summary>
	/// The windows whose procedure hands the keys it does not handle to the default window procedure, whose F1 row repeats the last tutorial advice: the windows
	/// of <see cref="Windows"/> the callers of 0x488BA0 identified. The map window's procedure is not among those callers.
	/// </summary>
	private static readonly HashSet<string> HelpKeyWindows = new( StringComparer.Ordinal )
	{
		"buy", "staff", "attractions", "employee", "loans", "finance", "parkInfo", "research", "visitors", "visitorInfo", "entryPrice", Hud.ParkHud.StaffPlacementHelp
	};

	/// <summary>
	/// Whether F1 in the HUD window named <paramref name="window"/> repeats the last tutorial advice: the procedures of the eight help
	/// windows pass the key messages they do not handle to the default window procedure, whose F1 row replays it.
	/// </summary>
	// [BIN:TPW-EXE:0x00488BA0 default window procedure] called with the window message from the procedures of the buy (0x4AC80B), hire staff (0x49B768), all attractions (0x495331), loans (0x49F81B), finance (0x49A100, 0x49A149, 0x49A1B7), park information (0x4A4A9B), all visitors (0x49337C) and ticket price (0x498D53, in 0x498C60 with its withdrawal 0x498CC7) windows, each beside its window's withdrawal call; seven more callers (0x48D27E, 0x495EE5, 0x4A8111, 0x4A9B3A, 0x4B26F7, 0x4B7ADA and one of the finance sites) belong to windows not identified
	public static bool RepeatsTutorialOnHelpKey( string? window ) => window != null && HelpKeyWindows.Contains( window );

	/// <summary>The help message of the HUD window named <paramref name="window"/>, or null when it has none.</summary>
	public static int? MessageFor( string window ) =>
		Windows.TryGetValue( window, out var message ) ? message : ObjectInfoHelp.TryParse( window, out var posted, out _ ) ? posted : null;

	/// <summary>The messages a closing window withdraws: its own, and for a ride window the one it did not post as well.</summary>
	public static IReadOnlyList<int> WithdrawnWith( string window ) =>
		ObjectInfoHelp.TryParse( window, out _, out var withdrawn ) ? withdrawn
			: MessageFor( window ) is { } message ? (ExtraWithdrawals.TryGetValue( window, out var extra ) ? new[] { message, extra } : new[] { message }) : Array.Empty<int>();

	/// <summary>The second message a closing window withdraws: the employee window's close handler withdraws 0xD4 next to its own 0xC0.</summary>
	// [BIN:TPW-EXE:0x004B5CF8 employee close] withdraws 0xC0 (192) and then 0xD4 (212, the patrol area help its refresh posts), 0x4B5D05
	private static readonly Dictionary<string, int> ExtraWithdrawals = new( StringComparer.Ordinal ) { ["employee"] = AdvisorToolHelp.PatrolAreaSet };

	/// <summary>Every message some window posts (the ones a park that ends withdraws).</summary>
	public static IEnumerable<int> AllWindowMessages => Windows.Values.Concat( ObjectInfoHelp.Messages ).Concat( AdvisorToolHelp.All ).Append( Hud.ParkHud.CameraPanelHelp ).Append( Hud.ParkHud.MessagePanelHelp ).Distinct();

	/// <summary>
	/// The ten help messages the original withdraws whenever the interface mode changes (0x52F200, 0x52F580): the help of the tools
	/// and modes rather than of windows. <see cref="AdvisorToolHelp"/> binds the post of each.
	/// </summary>
	// [BIN:TPW-EXE:0x0052F200 interface mode change] when the new mode differs from the old one, 0x59AA70 is called for 0xB8, 0xCB, 0xD6, 0xCD, 0xC9, 0xCE, 0xC1, 0x159, 0x15A and 0x151 (184, 203, 214, 205, 201, 206, 193, 345, 346, 337); the new mode 3 then posts 0xCB (203)
	public static readonly IReadOnlyList<int> ModeHelpMessages = new[] { 184, 203, 214, 205, 201, 206, 193, 345, 346, 337 };
}

/// <summary>
/// The kinds of object window the original opens for a placed object (one layout table each): <see cref="Relief"/> is the window of a feature that provides relief
/// (a toilet), <see cref="Rest"/> of one that chills you out and <see cref="Feature"/> of any other feature; <see cref="AddOn"/> is the window of a ride-type object
/// that is an add-on of kind 2 (<c>AddOn.UpgradeType</c>, not a track add-on).
/// </summary>
internal enum ObjectInfoKind { Ride, Shop, Sideshow, Relief, Rest, Feature, AddOn }

/// <summary>
/// The help of the object windows. The original opens one of three windows for a placed object, by its layout table, and each builder posts its
/// message as it opens: the ride window the repair message 297 when the truncated state of repair is 1 to 20 and the ride message 207 otherwise,
/// the shop window 209 and the sideshow window 208; the close handlers withdraw them (a ride window both of its two). The HUD reports such a
/// window under a name that carries the kind, that choice and the opening, so every object shown anew is a new window.
/// </summary>
// [BIN:TPW-EXE:0x004AF980 ride window builder] after building the window of layout table 0x755150 it takes the truncated float at the object's +0x44 (the state of repair); a low byte of 1 to 0x14 posts 0x129 (297) at 0x4AF9C8, anything else 0xCF (207) at 0x4AF9DA; the close handler withdraws 0x129 and then 0xCF (0x4AF6B7, 0x4AF6C4)
// [BIN:TPW-EXE:0x004B0E30 shop window builder] the window of layout table 0x755908 posts 0xD1 (209) at 0x4B0E5C; its close handler withdraws it (0x4B0BE3)
// [BIN:TPW-EXE:0x004B2430 sideshow window builder] the window of layout table 0x755E80 posts 0xD0 (208) at 0x4B245C; its close handler withdraws it (0x4B2203)
// [BIN:TPW-EXE:0x00486900 thing window dispatcher] the object's window is chosen by its kind: 0x4AF980 (calls at 0x4869CB and 0x4869F1), 0x4B0E30 (0x486A01) or 0x4B2430 (0x486A11)
// [BIN:TPW-EXE:0x00486920 thing window dispatcher] a placed object (thing type 3) opens its window by its kind word +0x4AC (Info.WhichUIType): 0 the ride window (0x4AF980), 1 the shop window (0x4B0E30), 2 the sideshow window (0x4B2430), 3 a feature's window by the thing flags +0x32, and no window for a larger value (the jump table 0x486A90 ends at 3); a feature with bit 0 (UsageInfo.ProvidesRelief) opens 0x497990, with bit 1 (UsageInfo.ChillsYouOut) 0x4B5290, any other 0x499EB0
// [BIN:TPW-EXE:0x00486958 ride kind dispatch] a ride-type object whose type record +0x288 (AddOn.UpgradeType, +0x284 is AddOn.UpgradesId) is 2 opens the window of table 0x7573C8 (0x4B6AE0) instead of the ride window: it posts 0x12D (301) as it opens (0x4B6B09) and its close handler withdraws it (0x4B6850)
// [BIN:TPW-EXE:0x00497990 relief window] after building the window of layout table 0x751190 it truncates the float at the object's +0x44 (the state, as the ride window's repair) and posts 0xD5 (213) at 0x4979CA when its low byte is below 0x19, else 0x12B (299) at 0x4979DB; the close handler withdraws 0x12B and then 0xD5 (0x4976A0, 0x4976AD)
// [BIN:TPW-EXE:0x004B5290 rest window] the window posts 0x12C (300) at 0x4B52B9 and its close handler withdraws it (0x4B50D4)
// [BIN:TPW-EXE:0x00499EB0 feature window] the window posts 0x12A (298) at 0x499ED9 and its close handler withdraws it (0x499C10)
internal static class ObjectInfoHelp
{
	public const string Prefix = "objectInfo/";
	public const int RideMessage = 207, RideRepairMessage = 297, ShopMessage = 209, SideshowMessage = 208;
	public const int ReliefDirtyMessage = 213, ReliefMessage = 299, RestMessage = 300, FeatureMessage = 298, AddOnMessage = 301;

	/// <summary>The messages any object window posts.</summary>
	public static readonly IReadOnlyList<int> Messages = new[] { RideMessage, RideRepairMessage, ShopMessage, SideshowMessage, ReliefDirtyMessage, ReliefMessage, RestMessage, FeatureMessage, AddOnMessage };

	/// <summary>Whether the truncated state of a relief feature posts the first message: the low byte of the integer is below 25 (a missing state counts as clean).</summary>
	public static bool IsDirty( double? state )
	{
		if ( state is not { } value || !double.IsFinite( value ) )
			return false;
		return (byte)(int)Math.Truncate( value ) < 25;
	}

	/// <summary>Whether the truncated state of repair posts the repair message: the low byte of the integer is 1 to 20.</summary>
	public static bool IsLowRepair( double? repair )
	{
		if ( repair is not { } value || !double.IsFinite( value ) )
			return false;
		var low = (byte)(int)Math.Truncate( value );
		return low is >= 1 and <= 20;
	}

	/// <summary>The window name for the <paramref name="opening"/>th object window shown; <paramref name="repair"/> matters for a ride only.</summary>
	public static string WindowName( ObjectInfoKind kind, double? repair, int opening ) =>
		$"{Prefix}{kind}{(kind == ObjectInfoKind.Ride && IsLowRepair( repair ) ? "/repair" : kind == ObjectInfoKind.Relief && IsDirty( repair ) ? "/dirty" : "")}/{opening.ToString( CultureInfo.InvariantCulture )}";

	/// <summary>Reads a window name made by <see cref="WindowName"/>: the message it posts and the messages its close withdraws.</summary>
	public static bool TryParse( string window, out int posted, out IReadOnlyList<int> withdrawn )
	{
		posted = 0;
		withdrawn = Array.Empty<int>();
		if ( !window.StartsWith( Prefix, StringComparison.Ordinal ) )
			return false;
		var parts = window[Prefix.Length..].Split( '/' );
		if ( parts.Length is < 2 or > 3 || !Enum.TryParse<ObjectInfoKind>( parts[0], out var kind ) )
			return false;
		switch ( kind )
		{
			case ObjectInfoKind.Ride:
				posted = parts.Length == 3 && parts[1] == "repair" ? RideRepairMessage : RideMessage;
				withdrawn = new[] { RideRepairMessage, RideMessage };
				return true;
			case ObjectInfoKind.Shop:
				posted = ShopMessage;
				withdrawn = new[] { ShopMessage };
				return true;
			case ObjectInfoKind.Relief:
				posted = parts.Length == 3 && parts[1] == "dirty" ? ReliefDirtyMessage : ReliefMessage;
				withdrawn = new[] { ReliefMessage, ReliefDirtyMessage };
				return true;
			case ObjectInfoKind.Rest:
				posted = RestMessage;
				withdrawn = new[] { RestMessage };
				return true;
			case ObjectInfoKind.Feature:
				posted = FeatureMessage;
				withdrawn = new[] { FeatureMessage };
				return true;
			case ObjectInfoKind.AddOn:
				posted = AddOnMessage;
				withdrawn = new[] { AddOnMessage };
				return true;
			default:
				posted = SideshowMessage;
				withdrawn = new[] { SideshowMessage };
				return true;
		}
	}
}

/// <summary>
/// The help the tools and panels post through 0x486B00: ids of the descriptor table (<c>advisor-help-messages.toml</c>) with the places that post them (see
/// <c>docs/LIPS.md</c> "Contextual help").
/// </summary>
internal static class AdvisorToolHelp
{
	// [BIN:TPW-EXE:0x004982A0 coaster panel] the press of its buttons posts 0x121 (move, control 0x1B), 0x125 (build, 0x1F), 0x126 (loop, 0x20: 0x127 when the coaster is a flume, mask bit 0x200) and 0x124 (demolish, 0x21); the close message 0x1001D8 withdraws all five
	public const int CoasterMove = 289, CoasterDemolish = 292, CoasterBuild = 293, CoasterLoop = 294, CoasterTrough = 295;
	/// <summary>The messages the coaster panel's close withdraws.</summary>
	public static readonly IReadOnlyList<int> CoasterPanelMessages = new[] { CoasterBuild, CoasterTrough, CoasterLoop, CoasterMove, CoasterDemolish };
	/// <summary>Posted after every click of the coaster tool (the click handler's mode 0x15, 0x52644C).</summary>
	public const int CoasterToolClick = 205;

	// [BIN:TPW-EXE:0x00527200 path and queue tool click] the first click of the path tool posts 0xB8 (184), the third later click of either tool 0xD6 (214), the click that ends the queue tool without a path join 0x151 (337)
	public const int PathStart = 184, PathLaterClick = 214, QueueEnd = 337;
	/// <summary>Posted when the queue tool starts, by every way into it (SetMode(3), 0x52F4B0).</summary>
	public const int QueueTool = 203;

	/// <summary>
	/// Posted by the kart and water line tool for a click that sets the line start it had none of: the tool is left without a start only when a line closed its circuit
	/// with exactly Ctrl held and no queue to follow (<see cref="Level.ContinueTrackTool"/>). No interface mode change withdraws it.
	/// </summary>
	// [BIN:TPW-EXE:0x00526D41 line start help] in the line modes 0xB and 0x10 (0x525A1C → 0x526CB3) a click with the start 0x81EDE4/0x81EDE8 at −1 stores the clicked cell as the start (0x526D0F) and the first corner (0x52FBD0, 0x52FBF0), pushes 0xCC (204) and jumps into the queue tool's post (0x527629: 0x59B590, 0x5194D0 on [0x7CF83C], 0x59A940, the body of 0x486B00); the placement presets the start at the station's exit (0x52A1A0 at 0x525472, then 0x52F580(0xB or 0x10), which keeps it) and the edit tool's switch to the line tool at the open end (0x530830, then 0x52F580), so only the start the exactly-Ctrl end clears (0x526F97, 0x527146) is reached; neither SetMode withdrawal list names 0xCC (0x52F245..0x52F326, 0x52F5BF..0x52F6A0), and no call of 0x486B40 or 0x59AA70 does
	public const int TrackLineStart = 204;

	// [BIN:TPW-EXE:0x004AC5D0 buy list click] choosing an item builds the placement tool and posts 0xC9 (201) when the item is a ride and no ride is placed yet, else 0xCE (206); the land rows build tool 0x3A (0x159, 345) and 0x39 (0x15A, 346)
	public const int FirstRide = 201, Placement = 206, ClearLandTool = 345, BuyLandTool = 346;

	/// <summary>The help a chosen buy item posts: 201 for a ride (<c>Info.WhichUIType</c> 0) while the park has no ride, else 206.</summary>
	public static int BuildStart( int whichUiType, bool anyRidePlaced ) => whichUiType == 0 && !anyRidePlaced ? FirstRide : Placement;

	// [BIN:TPW-EXE:0x004B3F60 locator open] the staff locator posts 0xC6 (198), the visitor locator (0x4B3F60 with a visitor, 0x4B4280) 0x11F (287); the panel's close message 0x1001D8 withdraws both
	public const int StaffLocator = 198, VisitorLocator = 287;

	// [BIN:TPW-EXE:0x004B5B50 patrol toggle] setting an area closes the employee window and posts 0xC1 (193); [BIN:TPW-EXE:0x004B5AB0 employee refresh] a refresh with an own area posts 0xD4 (212)
	public const int PatrolTool = 193, PatrolAreaSet = 212;

	/// <summary>Every message a tool or panel posts, for the withdrawal when a park ends.</summary>
	public static IEnumerable<int> All => CoasterPanelMessages.Concat( new[] { CoasterToolClick, PathStart, PathLaterClick, QueueEnd, QueueTool, TrackLineStart, FirstRide, Placement, ClearLandTool, BuyLandTool, PatrolTool, PatrolAreaSet, StaffLocator, VisitorLocator } );
}
