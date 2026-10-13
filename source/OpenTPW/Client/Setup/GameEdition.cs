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

/// <summary>The edition of a data folder and the evidence for it.</summary>
public sealed record GameEditionReport( GameEdition Edition, GameEditionEvidence Evidence );

/// <summary>
/// What OpenTPW runs for an edition's park, as one explicit policy instead of scattered edition checks: Theme Park
/// World is the reference game; Theme Park Inc runs the experimental slice (docs/THEME-PARK-INC.md). Every consumer
/// asks a capability here rather than testing <see cref="GameEditions.IsThemeParkInc"/> itself, so what the slice
/// supports stays written down in one place.
/// </summary>
public sealed record EditionCapabilities
{
	/// <summary>The park economy (money, calendar, research, staff wages): needs the Theme Park World balance keys.</summary>
	public required bool Economy { get; init; }
	/// <summary>The original Theme Park World HUD (its <c>ui.wad</c> panels, buy window, advisor arm).</summary>
	public required bool OriginalHud { get; init; }
	/// <summary>The edition-appropriate basic HUD of the experimental slice (OpenTPW text UI, no original panel art).</summary>
	public required bool ExperimentalHud { get; init; }
	/// <summary>Building, rotating and removing objects, paths and queues (free while <see cref="Economy"/> is off).</summary>
	public required bool Building { get; init; }
	/// <summary>Guests walk, queue and ride under the ported Theme Park World guest rules.</summary>
	public required bool Guests { get; init; }
	/// <summary>Staff world and staffing (wages, rest, training): needs the economy.</summary>
	public required bool Staff { get; init; }
	/// <summary>The automatic advisor (its <c>Advisor.sam</c> differs in Theme Park Inc).</summary>
	public required bool Advisor { get; init; }
	/// <summary>The original-style front end (lobby and menus).</summary>
	public required bool FrontEnd { get; init; }
	/// <summary>Writing and resuming OpenTPW park saves of this edition's parks.</summary>
	public required bool ParkSaves { get; init; }
	/// <summary>Sharing: exporting, importing and visiting parks of this edition online.</summary>
	public required bool Sharing { get; init; }
	/// <summary>What a start of this edition is called in refusals and logs, with its limits (null: the full game).</summary>
	public required string? ExperimentalNote { get; init; }

	/// <summary>The reference capabilities: everything OpenTPW implements, for Theme Park World.</summary>
	public static EditionCapabilities ForThemeParkWorld() => new()
	{
		Economy = true,
		OriginalHud = true,
		ExperimentalHud = false,
		Building = true,
		Guests = true,
		Staff = true,
		Advisor = true,
		FrontEnd = true,
		ParkSaves = true,
		Sharing = true,
		ExperimentalNote = null
	};

	/// <summary>
	/// The experimental Theme Park Inc slice: worlds load and can be built in and walked by guests, but no system whose
	/// Theme Park Inc rules or data OpenTPW has not traced runs. The economy is off because Theme Park Inc's
	/// <c>Standard.sam</c> lacks 89 of the balance keys the Theme Park World economy reads (measured October 13, 2026);
	/// the original HUD, front end, advisor and every save, progress and sharing format stay refused.
	/// </summary>
	// [APPROX:TPI-903] the slice's capability gates are OpenTPW's honest limits, not traced Theme Park Inc behaviour
	public static EditionCapabilities ForThemeParkIncSlice() => new()
	{
		Economy = false,
		OriginalHud = false,
		ExperimentalHud = true,
		Building = true,
		Guests = true,
		Staff = false,
		Advisor = false,
		FrontEnd = false,
		ParkSaves = false,
		Sharing = false,
		ExperimentalNote = "experimental Theme Park Inc slice: no economy, staff, advisor, campaign (missions/GMS), "
			+ "park saves, sharing or Theme Park Inc UI; building is free; none of it is checked against the original game"
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

	/// <summary>Classifies a game <c>Data</c> folder. IO errors count as "not Theme Park Inc".</summary>
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
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
		}
		return new( GameEdition.ThemeParkWorld, GameEditionEvidence.None );
	}

	/// <summary>The themes the Theme Park Inc viewer may load.</summary>
	public static bool IsViewableLevel( string level ) => ThemeParkInc.Contains( level, StringComparer.OrdinalIgnoreCase );

	/// <summary>Options that need a playable Theme Park World game or a save/sharing format the slice refuses.</summary>
	private static readonly string[] ViewerIncompatibleOptions =
	{
		"--sandbox", "--front-end", "--smoke-test", "--advisor-say", "--advisor-response", "--setup",
		"--park-resume-smoke", "--export-park", "--import-park", "--visit-park"
	};

	/// <summary>
	/// Why a start on Theme Park Inc data is refused, or null when it is the experimental slice (<c>--load-original-level</c> with a
	/// Theme Park Inc world and none of the options that need a playable Theme Park World game or a refused save or
	/// sharing format) or a read-only inspection.
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
		return "The game folder holds Theme Park Inc (Sim Coaster) data, which OpenTPW does not play. "
			+ "The experimental slice opens it: add --load-original-level arabian, science or water"
			+ (unsupported != null ? $" and drop {unsupported} (it needs a Theme Park World park)" : "")
			+ " (docs/THEME-PARK-INC.md). For the supported game, choose Theme Park World files.";
	}

	/// <summary>One line for the start-up log: what the slice is and that it is not checked against the original game.</summary>
	// [APPROX:TPI-903] the slice's stated limits are OpenTPW's honest capability gates, not traced Theme Park Inc behaviour
	public static string SliceNotice => "experimental Theme Park Inc slice: world, building and guests only; "
		+ "no economy, staff, advisor, missions, Global Master Save, COS or Theme Park Inc interface; original behaviour is not verified.";
}
