using System.Security.Cryptography;

namespace OpenTPW;

/// <summary>Which game a data folder belongs to (docs/THEME-PARK-INC.md, "Theme Park Inc level viewer").</summary>
public enum GameEdition
{
	/// <summary>Theme Park World (Sim Theme Park): the only edition OpenTPW plays.</summary>
	ThemeParkWorld,
	/// <summary>Theme Park Inc (Sim Coaster): its worlds run in the experimental slice started by <c>--load-original-level</c>.</summary>
	ThemeParkInc
}

/// <summary>How a folder was recognised as <see cref="GameEdition.ThemeParkInc"/>.</summary>
public enum GameEditionEvidence
{
	/// <summary>Not Theme Park Inc.</summary>
	None,
	/// <summary><c>levels/Standard.sam</c> is the supplied retail file (size and SHA-256).</summary>
	StandardSamHash,
	/// <summary>Structure only: <c>Ver.txt</c> beside the Theme Park Inc theme folders and no Theme Park World theme folder.</summary>
	Structure
}

/// <summary>How the language a CD was made for was recognised (<see cref="GameEditions.DetectCdLanguage"/>).</summary>
public enum GameLanguageEvidence
{
	/// <summary>No language recognised (an installed game, or a CD with several or no language marks).</summary>
	None,
	/// <summary>The only <c>Autorun/&lt;language&gt;.tre</c> besides <c>autorun.tre</c> and <c>general.tre</c> (<c>spanish.tre</c>, <c>Korean.tre</c>) names a language that has a folder in <c>Data/Language</c>.</summary>
	AutorunTre,
	/// <summary>A <c>&lt;Language&gt;</c> folder beside <c>Data</c> (the installer, banner meshes and word filter) with its own <c>Data/Language/&lt;language&gt;</c>.</summary>
	LanguageFolder,
	/// <summary>The only non-English Windows locale file (<c>0000040c.016</c>, named by locale identifier) beside <c>Data</c>.</summary>
	LocaleFile
}

/// <summary>The edition of a data folder and the evidence for it.</summary>
public sealed record GameEditionReport( GameEdition Edition, GameEditionEvidence Evidence )
{
	/// <summary>The language the CD this folder belongs to was made for (Spanish, Korean, Polish, ...), or null for an installed game and for CDs that carry several languages.</summary>
	public string? CdLanguage { get; init; }

	/// <summary>What named <see cref="CdLanguage"/>.</summary>
	public GameLanguageEvidence CdLanguageEvidence { get; init; }

	/// <summary>Every language the folder's locale files (<c>0000040c.016</c>, named by Windows locale identifier) name, in identifier order.</summary>
	public IReadOnlyList<string> LocaleLanguages { get; init; } = Array.Empty<string>();
}

/// <summary>
/// What OpenTPW runs for an edition's park, as one explicit policy instead of scattered edition checks: Theme Park
/// World is the reference game; Theme Park Inc runs the experimental slice (docs/THEME-PARK-INC.md). Every consumer
/// asks a capability here rather than testing <see cref="GameEditions.IsThemeParkInc"/> itself, so what the slice
/// supports stays written down in one place.
/// </summary>
public sealed record EditionCapabilities
{
	/// <summary>
	/// The park's money: cash, bank and loans, the calendar, build, path, queue and track charges, scrap refunds, running costs and
	/// bankruptcy. Needs only the balance keys Theme Park Inc's own data carries (<see cref="BalanceSettings.ForThemeParkInc"/>).
	/// </summary>
	public required bool Money { get; init; }
	/// <summary>Research (the lab, researchers and the research table): needs the Theme Park World research keys.</summary>
	public required bool Research { get; init; }
	/// <summary>Challenges, golden tickets and the Theme Park World objective system.</summary>
	public required bool Challenges { get; init; }
	/// <summary>The whole Theme Park World park economy: money, staff, research and challenges together.</summary>
	public bool Economy => Money && Staff && Research && Challenges;
	/// <summary>The original Theme Park World HUD (its <c>ui.wad</c> panels, buy window, advisor arm).</summary>
	public required bool OriginalHud { get; init; }
	/// <summary>The edition-appropriate basic HUD of the experimental slice (OpenTPW text UI, no original panel art).</summary>
	public required bool ExperimentalHud { get; init; }
	/// <summary>Building, rotating and removing objects, paths and queues (free while <see cref="Economy"/> is off).</summary>
	public required bool Building { get; init; }
	/// <summary>Guests walk, queue and ride under the ported Theme Park World guest rules.</summary>
	public required bool Guests { get; init; }
	/// <summary>Staff world and staffing (wages, rest, training, maintenance): needs the Theme Park World staff keys.</summary>
	public required bool Staff { get; init; }
	/// <summary>The automatic advisor (its <c>Advisor.sam</c> differs in Theme Park Inc).</summary>
	public required bool Advisor { get; init; }
	/// <summary>The original-style front end (lobby and menus).</summary>
	public required bool FrontEnd { get; init; }
	/// <summary>
	/// The Theme Park Inc Personnel File start screen (<c>ThemeParkIncFrontEnd</c>): drawn from Theme Park Inc's own <c>ui.wad</c> models and strings,
	/// with one OpenTPW player; it replaces the up-front zone choice. Independent of <see cref="FrontEnd"/>, which is Theme Park World's lobby.
	/// </summary>
	public required bool PersonnelFileFrontEnd { get; init; }
	/// <summary>Writing and resuming OpenTPW park saves of this edition's parks.</summary>
	public required bool ParkSaves { get; init; }
	/// <summary>Sharing: exporting, importing and visiting parks of this edition online.</summary>
	public required bool Sharing { get; init; }
	/// <summary>What a start of this edition is called in refusals and logs, with its limits (null: the full game).</summary>
	public required string? ExperimentalNote { get; init; }

	/// <summary>The reference capabilities: everything OpenTPW implements, for Theme Park World.</summary>
	public static EditionCapabilities ForThemeParkWorld() => new()
	{
		Money = true,
		Research = true,
		Challenges = true,
		OriginalHud = true,
		ExperimentalHud = false,
		Building = true,
		Guests = true,
		Staff = true,
		Advisor = true,
		FrontEnd = true,
		PersonnelFileFrontEnd = false,
		ParkSaves = true,
		Sharing = true,
		ExperimentalNote = null
	};

	/// <summary>
	/// The experimental Theme Park Inc slice: worlds load and can be built in and walked by guests, but no system whose
	/// Theme Park Inc rules or data OpenTPW has not traced runs. Only the money runs (docs/THEME-PARK-INC.md, "Economy"):
	/// Theme Park Inc's <c>Standard.sam</c> lacks 86 of the balance keys the Theme Park World economy reads (measured October 17, 2026),
	/// so staff, research and challenges stay refused, as do the original HUD, front end, advisor and every save, progress and sharing format.
	/// </summary>
	// [EXT:tpi-capability-gates] the slice's capability gates are OpenTPW's honest limits (a scope decision), not traced Theme Park Inc behaviour
	public static EditionCapabilities ForThemeParkIncSlice() => new()
	{
		Money = true,
		Research = false,
		Challenges = false,
		OriginalHud = false,
		ExperimentalHud = true,
		Building = true,
		Guests = true,
		Staff = false,
		Advisor = false,
		FrontEnd = false,
		// [EXT:tpi-front-end-phase1] the slice starts at an OpenTPW Personnel File screen (docs/THEME-PARK-INC.md); Theme Park World's lobby front end stays refused
		PersonnelFileFrontEnd = true,
		ParkSaves = false,
		Sharing = false,
		ExperimentalNote = "experimental Theme Park Inc slice: money only (cash, loans, build and running costs, calendar); "
			+ "no staff, research, challenges, advisor, missions, Global Master Save, "
			+ "COS files, park saves or sharing, and no Theme Park Inc in-game interface (only its Personnel File start screen); "
			+ "none of it is checked against the original game"
	};
}

/// <summary>
/// Recognises Theme Park Inc data. The known retail <c>levels/Standard.sam</c> is an exact identity; structure
/// (<c>Ver.txt</c>, the Theme Park Inc themes, none of the Theme Park World themes) also identifies edited copies.
/// <see cref="Current"/> is the edition of the data OpenTPW started with, and <see cref="Capabilities"/> is its
/// capability policy.
/// </summary>
// [EXT:tpi-viewer] Theme Park Inc runs an experimental slice (docs/THEME-PARK-INC.md), not the supported game
public static class GameEditions
{
	private const long StandardSamLength = 45695;
	private const string StandardSamSha256 = "8966C8647104FEB1878FF3C3D43F3B2AC6DC89856570F9AA5A5A0BF040E6749E";
	private static readonly string[] ThemeParkInc = { "arabian", "science", "water" };
	private static readonly string[] ThemeParkWorld = { "jungle", "fantasy", "hallow", "space" };

	/// <summary>The Theme Park Inc worlds the viewer slice can load (setup, edition inspection).</summary>
	public static IReadOnlyList<string> ThemeParkIncWorlds => ThemeParkInc;

	/// <summary>The Theme Park World themes; any of them in a Theme Park Inc folder means a mixed folder.</summary>
	public static IReadOnlyList<string> ThemeParkWorldThemes => ThemeParkWorld;

	/// <summary>The edition of the data the game started with (Theme Park World until a startup check says otherwise).</summary>
	public static GameEdition Current { get; private set; } = GameEdition.ThemeParkWorld;

	public static bool IsThemeParkInc => Current == GameEdition.ThemeParkInc;

	/// <summary>The capability policy of the edition OpenTPW started with (Theme Park World's until selected otherwise).</summary>
	public static EditionCapabilities Capabilities => IsThemeParkInc ? EditionCapabilities.ForThemeParkIncSlice() : EditionCapabilities.ForThemeParkWorld();

	public static void Select( GameEdition edition ) => Current = edition;

	/// <summary>True when <paramref name="dataDirectory"/>'s <c>levels/Standard.sam</c> is the supplied Theme Park Inc retail file.</summary>
	public static bool HasKnownStandardSam( string dataDirectory )
	{
		var standard = GameLanguage.FindEntry( dataDirectory, Path.Combine( "levels", "Standard.sam" ), false );
		if ( standard == null || new FileInfo( standard ).Length != StandardSamLength )
			return false;
		using var input = File.OpenRead( standard );
		return Convert.ToHexString( SHA256.HashData( input ) ).Equals( StandardSamSha256, StringComparison.Ordinal );
	}

	/// <summary>
	/// Classifies a game <c>Data</c> folder. IO errors count as "not Theme Park Inc". For Theme Park World the report also
	/// names the language of a single-language CD (<see cref="DetectCdLanguage"/>: the Spanish CD, the Korean Slim Pack CD, the Polish CD).
	/// </summary>
	public static GameEditionReport Detect( string dataDirectory )
	{
		try
		{
			if ( HasKnownStandardSam( dataDirectory ) )
				return new( GameEdition.ThemeParkInc, GameEditionEvidence.StandardSamHash );
			var levels = GameLanguage.FindEntry( dataDirectory, "levels", true );
			if ( levels != null && GameLanguage.FindEntry( dataDirectory, "Ver.txt", false ) != null
				&& ThemeParkInc.Any( theme => GameLanguage.FindEntry( levels, theme, true ) != null )
				&& !ThemeParkWorld.Any( theme => GameLanguage.FindEntry( levels, theme, true ) != null ) )
				return new( GameEdition.ThemeParkInc, GameEditionEvidence.Structure );
			var (language, evidence, locales) = DetectCdLanguage( dataDirectory );
			return new( GameEdition.ThemeParkWorld, GameEditionEvidence.None ) { CdLanguage = language, CdLanguageEvidence = evidence, LocaleLanguages = locales };
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
		}
		return new( GameEdition.ThemeParkWorld, GameEditionEvidence.None );
	}

	/// <summary>
	/// Windows locale identifiers of the <c>0000XXXX.016</c>/<c>.256</c> locale files on the CDs (the installer's language marks). The
	/// Korean Slim Pack CD carries 0404 and 1004 (Chinese) next to 0412 (Korean) and five European locales, so these files only name which
	/// languages the installer knows, not the CD's own.
	/// </summary>
	private static readonly Dictionary<int, string> LocaleIdentifiers = new()
	{
		[0x0404] = "Chinese (Traditional)", [0x1004] = "Chinese (Simplified)", [0x0406] = "Danish", [0x0407] = "German", [0x0409] = "English",
		[0x0809] = "English", [0x040A] = "Spanish", [0x0C0A] = "Spanish", [0x040C] = "French", [0x0410] = "Italian", [0x0412] = "Korean",
		[0x0413] = "Dutch", [0x0415] = "Polish", [0x041D] = "Swedish"
	};

	/// <summary>
	/// The language a single-language CD (or a game folder copied from one) was made for, with what named it. The Spanish and
	/// Korean CDs have a complete <c>Data</c> folder whose <c>Language</c> holds English next to their own language, so the
	/// language comes from beside <c>Data</c>: first the one <c>Autorun/&lt;language&gt;.tre</c> besides <c>autorun.tre</c> and
	/// <c>general.tre</c>, then a <c>&lt;Language&gt;</c> folder (<c>Spanish</c>, <c>Korean</c>) that matches a <c>Data/Language</c> folder, then
	/// a single non-English locale file. Also returns the languages the locale files name. An installed game, the European CD
	/// (its languages are <c>&lt;Lang&gt;/data</c> overlays) and a folder without such marks return none.
	/// </summary>
	public static (string? Language, GameLanguageEvidence Evidence, IReadOnlyList<string> Locales) DetectCdLanguage( string dataDirectory )
	{
		var root = Path.GetDirectoryName( Path.TrimEndingDirectorySeparator( Path.GetFullPath( dataDirectory ) ) );
		if ( root == null || !Directory.Exists( root ) )
			return (null, GameLanguageEvidence.None, Array.Empty<string>());
		var installed = GameLanguage.FindEntry( dataDirectory, "Language", true ) is { } languageRoot
			? Directory.EnumerateDirectories( languageRoot ).Select( directory => GameLanguage.CanonicalName( Path.GetFileName( directory ) ) )
				.Where( name => !string.Equals( name, GameLanguage.DefaultLanguage, StringComparison.OrdinalIgnoreCase ) ).ToArray()
			: Array.Empty<string>();

		var locales = Directory.EnumerateFiles( root )
			.Select( path => Path.GetFileName( path ) )
			.Select( name => name.Length == 12 && name.StartsWith( "0000", StringComparison.Ordinal ) && name[8] == '.' && name[9..] is "016" or "256" && int.TryParse( name.AsSpan( 4, 4 ), System.Globalization.NumberStyles.HexNumber, null, out var id ) ? id : -1 )
			.Where( LocaleIdentifiers.ContainsKey ).Distinct().Order().Select( id => LocaleIdentifiers[id] ).Distinct().ToArray();

		if ( GameLanguage.FindEntry( root, "Autorun", true ) is { } autorun )
		{
			var tre = Directory.EnumerateFiles( autorun, "*.tre" ).Select( path => Path.GetFileNameWithoutExtension( path ) )
				.Where( name => !name.Equals( "autorun", StringComparison.OrdinalIgnoreCase ) && !name.Equals( "general", StringComparison.OrdinalIgnoreCase ) )
				.Select( GameLanguage.CanonicalName ).Where( name => installed.Contains( name, StringComparer.OrdinalIgnoreCase ) ).Distinct().ToArray();
			if ( tre.Length == 1 )
				return (tre[0], GameLanguageEvidence.AutorunTre, locales);
		}
		var folders = installed.Where( name => GameLanguage.FindEntry( root, name, true ) != null ).ToArray();
		if ( folders.Length == 1 )
			return (folders[0], GameLanguageEvidence.LanguageFolder, locales);
		var single = locales.Where( name => installed.Contains( name, StringComparer.OrdinalIgnoreCase ) ).ToArray();
		if ( single.Length == 1 && locales.Count( name => name != "English" ) == 1 )
			return (single[0], GameLanguageEvidence.LocaleFile, locales);
		return (null, GameLanguageEvidence.None, locales);
	}

	/// <summary>The Theme Park Inc worlds the experimental slice may load.</summary>
	public static bool IsViewableLevel( string level ) => ThemeParkInc.Contains( level, StringComparer.OrdinalIgnoreCase );

	/// <summary>Options that need a playable Theme Park World game or a save/sharing format the slice refuses.</summary>
	private static readonly string[] ViewerIncompatibleOptions =
	{
		"--sandbox", "--front-end", "--smoke-test", "--advisor-say", "--advisor-response", "--setup",
		"--park-resume-smoke", "--export-park", "--import-park", "--visit-park"
	};

	/// <summary>
	/// The only options a Theme Park Inc front-end start accepts: the front-end switch and the options that pick the files, the language, the display
	/// and the sound. Any other option (a tool mode, a capture, an export, a smoke test) keeps its refusal on Theme Park Inc data.
	/// </summary>
	private static readonly string[] FrontEndStartOptions =
	{
		"--front-end", "--game-path", "--language", "--language-data", "--cd-data", "--bonus-data", "--mute", "--silent", "--audible", "--no-intro", "--no-advisor",
		"--no-autorun", "--no-vsync", "--offscreen", "--no-offscreen", "--visible", "--frame-stats", "--no-determinism",
		// the display options of DisplaySettings.ApplyCommandLine
		"--resolution", "--windowed", "--fullscreen", "--fullscreen-exclusive", "--upscale", "--render-scale", "--ui-scale"
	};

	/// <summary>True when <paramref name="args"/> start the Personnel File: no world and no option outside <see cref="FrontEndStartOptions"/>.</summary>
	private static bool IsFrontEndStart( string[] args ) =>
		!args.Contains( "--load-original-level" ) && args.Where( argument => argument.StartsWith( "--" ) ).All( argument => FrontEndStartOptions.Contains( argument ) );

	/// <summary>
	/// Why a start on Theme Park Inc data is refused, or null when it is the experimental slice (<c>--load-original-level</c> with a
	/// Theme Park Inc world and none of the options that need a playable Theme Park World game or a refused save or
	/// sharing format), the slice's Personnel File front end (no world, only the options of <see cref="FrontEndStartOptions"/>; while
	/// <see cref="EditionCapabilities.PersonnelFileFrontEnd"/> is on) or a read-only inspection.
	/// </summary>
	public static string? ViewerRefusal( string[] args )
	{
		// Read-only inspection reads assets without starting a game: --inspect-model, and a headless movie decode.
		if ( args.Contains( "--inspect-model" ) || (args.Contains( "--play-movie" ) && args.Contains( "--headless" )) )
			return null;
		var index = Array.IndexOf( args, "--load-original-level" );
		var level = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith( "--" ) ? args[index + 1] : null;
		var unsupported = ViewerIncompatibleOptions.FirstOrDefault( args.Contains );
		if ( level != null && IsViewableLevel( level ) && unsupported == null )
			return null;
		if ( level == null && Capabilities.PersonnelFileFrontEnd && IsFrontEndStart( args ) )
			return null;
		return "The game folder holds Theme Park Inc (Sim Coaster) data, which OpenTPW does not play. "
			+ "The experimental slice opens it: start without options or with --front-end for the Personnel File, or add --load-original-level arabian, science or water"
			+ (unsupported != null ? $" and drop {unsupported} (it needs a Theme Park World park)" : "")
			+ " (docs/THEME-PARK-INC.md). For the supported game, choose Theme Park World files.";
	}

	/// <summary>One line for the start-up log: what the slice is and that it is not checked against the original game.</summary>
	// [EXT:tpi-capability-gates] the slice's stated limits are OpenTPW's honest capability gates (a scope decision), not traced Theme Park Inc behaviour
	public static string SliceNotice => "experimental Theme Park Inc slice: world, building, guests and money only; "
		+ "no staff, research, challenges, advisor, missions, Global Master Save, COS or Theme Park Inc in-game interface; original behaviour is not verified.";
}
