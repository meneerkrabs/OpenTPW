using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// Where the original game files are, chosen in the setup wizard or found automatically, and the optional
/// official bonus content (docs/OBJECTS.md). Stored as <c>setup.json</c> beside <c>display.json</c> in the
/// user configuration directory, so it survives moving or updating a release (docs/SETUP.md). A missing
/// <c>bonusPath</c> means not chosen; an empty one means the player removed the bonus content.
/// </summary>
public sealed record SetupSettings( string? GamePath, string? CdPath, string? BonusPath = null )
{
	public const string FileName = "setup.json";

	private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	public static string GetDefaultPath() => Path.Combine( Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )!, FileName );

	/// <summary>Missing or unreadable files give empty settings; the wizard then asks again.</summary>
	public static SetupSettings Load( string path )
	{
		try
		{
			return File.Exists( path ) ? JsonSerializer.Deserialize<SetupSettings>( File.ReadAllText( path ), Options ) ?? new( null, null ) : new( null, null );
		}
		catch ( Exception exception ) when ( exception is JsonException or IOException or UnauthorizedAccessException )
		{
			Log?.Warning( $"Setup settings '{path}' are unreadable ({exception.Message}); asking for the game folder again." );
			return new( null, null );
		}
	}

	public void Save( string path )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		var temporary = path + ".tmp";
		File.WriteAllText( temporary, JsonSerializer.Serialize( this, Options ) );
		File.Move( temporary, path, overwrite: true );
	}
}

/// <summary>Where the game path came from, for the startup log.</summary>
public enum GamePathSource { CommandLine, Environment, Saved, Legacy, Detected, Wizard }

/// <summary>
/// Chooses the game folder: <c>--game-path</c>, then <c>OPENTPW_GAME_PATH</c>, then the saved setup,
/// then the old application setting, then automatic detection. Only the first two are trusted
/// without inspection, so developer overrides behave as before; saved and detected folders must
/// pass <see cref="GameInstallation.Inspect"/>. Null means the setup wizard has to ask.
/// </summary>
public static class GamePathResolution
{
	public static (string Path, GamePathSource Source)? Resolve( string? commandLine, string? environment, SetupSettings saved, string? legacy, Func<IEnumerable<string>> detect )
	{
		if ( !string.IsNullOrWhiteSpace( commandLine ) )
			return (Path.GetFullPath( commandLine ), GamePathSource.CommandLine);
		if ( !string.IsNullOrWhiteSpace( environment ) )
			return (Path.GetFullPath( environment ), GamePathSource.Environment);
		if ( Usable( saved.GamePath ) is { } savedPath )
			return (savedPath, GamePathSource.Saved);
		if ( Usable( legacy ) is { } legacyPath )
			return (legacyPath, GamePathSource.Legacy);
		foreach ( var candidate in detect() )
			if ( Usable( candidate ) is { } detected )
				return (detected, GamePathSource.Detected);
		return null;
	}

	private static string? Usable( string? path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;
		var report = GameInstallation.Inspect( path );
		return report.IsUsable ? report.Path : null;
	}

	/// <summary>
	/// Command-line modes that must never open a window to ask (tests, tools, CI); they keep failing
	/// with the old message when no game folder is known. <c>OPENTPW_NO_SETUP=1</c> does the same.
	/// </summary>
	public static bool IsInteractive( string[] args ) =>
		Environment.GetEnvironmentVariable( "OPENTPW_NO_SETUP" ) != "1"
		&& !args.Any( argument => argument is "--smoke-test" or "--validate-assets" or "--inspect-model" or "--inspect-rides" or "--headless" or "--build-texture-pack" or "--capture-world" or "--export-park" or "--import-park" );
}
