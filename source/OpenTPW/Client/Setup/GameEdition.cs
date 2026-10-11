using System.Security.Cryptography;

namespace OpenTPW;

/// <summary>Which game a data folder belongs to (docs/THEME-PARK-INC.md, "Theme Park Inc level viewer").</summary>
public enum GameEdition
{
	/// <summary>Theme Park World (Sim Theme Park): the only edition OpenTPW plays.</summary>
	ThemeParkWorld,
	/// <summary>Theme Park Inc (Sim Coaster): viewable through <c>--load-original-level</c> only.</summary>
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
/// Recognises Theme Park Inc data. The known retail <c>levels/Standard.sam</c> is an exact identity; structure
/// (<c>Ver.txt</c>, the Theme Park Inc themes, none of the Theme Park World themes) also identifies edited copies.
/// <see cref="Current"/> is the edition of the data OpenTPW started with.
/// </summary>
// [EXT:tpi-viewer] Theme Park Inc is an experimental level viewer, not a supported game.
public static class GameEditions
{
	private const long StandardSamLength = 45695;
	private const string StandardSamSha256 = "8966C8647104FEB1878FF3C3D43F3B2AC6DC89856570F9AA5A5A0BF040E6749E";
	private static readonly string[] ThemeParkInc = { "arabian", "science", "water" };
	private static readonly string[] ThemeParkWorld = { "jungle", "fantasy", "hallow", "space" };

	/// <summary>The edition of the data the game started with (Theme Park World until a startup check says otherwise).</summary>
	public static GameEdition Current { get; private set; } = GameEdition.ThemeParkWorld;

	public static bool IsThemeParkInc => Current == GameEdition.ThemeParkInc;

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

	private static readonly string[] ViewerIncompatibleOptions = { "--sandbox", "--front-end", "--smoke-test", "--advisor-say", "--advisor-response", "--setup" };

	/// <summary>
	/// Why a start on Theme Park Inc data is refused, or null when it is the viewer (<c>--load-original-level</c> with a Theme Park Inc
	/// world and none of the options that need a playable game) or a read-only inspection.
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
		return "The game folder holds Theme Park Inc (Sim Coaster) data, which OpenTPW does not support. "
			+ "Only the experimental level viewer can open it: add --load-original-level arabian, science or water"
			+ (unsupported != null ? $" and drop {unsupported}" : "") + " (docs/THEME-PARK-INC.md). For playing, choose Theme Park World files.";
	}
}
