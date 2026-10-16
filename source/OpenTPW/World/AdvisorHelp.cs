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
/// The shipped help-message table (<c>content/data/advisor-help-messages.toml</c>): the 82 context-help descriptors of the Windows
/// executable's table at 0x76E300, copied for interoperability by <c>tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py</c>.
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
	// [BIN:TPW-EXE:0x00493530 all visitors window] columns 0x71-0x76 (Visitor Number .. Happiness): posts 0x11E (286) at 0x4937CA, withdraws it at 0x4933D2
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
		["visitors"] = 286,
		["entryPrice"] = 303,
		[Hud.ParkHud.StaffPlacementHelp] = 309,
	};

	/// <summary>
	/// Whether F1 in the HUD window named <paramref name="window"/> repeats the last tutorial advice: the procedures of the eight help
	/// windows pass the key messages they do not handle to the default window procedure, whose F1 row replays it.
	/// </summary>
	// [BIN:TPW-EXE:0x00488BA0 default window procedure] called with the window message from the procedures of the buy (0x4AC80B), hire staff (0x49B768), all attractions (0x495331), loans (0x49F81B), finance (0x49A100, 0x49A149, 0x49A1B7), park information (0x4A4A9B), all visitors (0x49337C) and ticket price (0x498D53, in 0x498C60 with its withdrawal 0x498CC7) windows, each beside its window's withdrawal call; seven more callers (0x48D27E, 0x495EE5, 0x4A8111, 0x4A9B3A, 0x4B26F7, 0x4B7ADA and one of the finance sites) belong to windows not identified
	public static bool RepeatsTutorialOnHelpKey( string? window ) => window != null && Windows.ContainsKey( window );

	/// <summary>The help message of the HUD window named <paramref name="window"/>, or null when it has none.</summary>
	public static int? MessageFor( string window ) =>
		Windows.TryGetValue( window, out var message ) ? message : ObjectInfoHelp.TryParse( window, out var posted, out _ ) ? posted : null;

	/// <summary>The messages a closing window withdraws: its own, and for a ride window the one it did not post as well.</summary>
	public static IReadOnlyList<int> WithdrawnWith( string window ) =>
		ObjectInfoHelp.TryParse( window, out _, out var withdrawn ) ? withdrawn : MessageFor( window ) is { } message ? new[] { message } : Array.Empty<int>();

	/// <summary>Every message some window posts (the ones a park that ends withdraws).</summary>
	public static IEnumerable<int> AllWindowMessages => Windows.Values.Concat( ObjectInfoHelp.Messages ).Distinct();

	/// <summary>
	/// The ten help messages the original withdraws whenever the interface mode changes (0x52F200, 0x52F580): the help of the tools
	/// and modes rather than of windows. Their producers are not bound (ADVISOR-009).
	/// </summary>
	// [BIN:TPW-EXE:0x0052F200 interface mode change] when the new mode differs from the old one, 0x59AA70 is called for 0xB8, 0xCB, 0xD6, 0xCD, 0xC9, 0xCE, 0xC1, 0x159, 0x15A and 0x151 (184, 203, 214, 205, 201, 206, 193, 345, 346, 337); the new mode 3 then posts 0xCB (203)
	public static readonly IReadOnlyList<int> ModeHelpMessages = new[] { 184, 203, 214, 205, 201, 206, 193, 345, 346, 337 };
}

/// <summary>The kinds of object window the original opens for a placed object (one layout table each).</summary>
internal enum ObjectInfoKind { Ride, Shop, Sideshow }

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
internal static class ObjectInfoHelp
{
	public const string Prefix = "objectInfo/";
	public const int RideMessage = 207, RideRepairMessage = 297, ShopMessage = 209, SideshowMessage = 208;

	/// <summary>The messages any object window posts.</summary>
	public static readonly IReadOnlyList<int> Messages = new[] { RideMessage, RideRepairMessage, ShopMessage, SideshowMessage };

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
		$"{Prefix}{kind}{(kind == ObjectInfoKind.Ride && IsLowRepair( repair ) ? "/repair" : "")}/{opening.ToString( CultureInfo.InvariantCulture )}";

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
			default:
				posted = SideshowMessage;
				withdrawn = new[] { SideshowMessage };
				return true;
		}
	}
}
