namespace OpenTPW;

/// <summary>
/// Decides whether the CD's autorun screen opens before the front end and builds it (docs/AUTORUN.md).
/// [EXT:autorun] It needs the original <c>Autorun</c> folder (<c>general.tre</c> and the language archives) in the game
/// folder or the CD folder and is skipped by <c>--no-autorun</c>, <c>OPENTPW_NO_AUTORUN=1</c> or <c>"showAutorun": false</c> in setup.json.
/// </summary>
internal static class AutorunLauncher
{
	public const string DisableOption = "--no-autorun";
	public const string DisableVariable = "OPENTPW_NO_AUTORUN";

	public static bool IsDisabled( string[] args, SetupSettings settings ) =>
		args.Contains( DisableOption ) || Environment.GetEnvironmentVariable( DisableVariable ) == "1" || !settings.ShowAutorun;

	/// <summary>The autorun assets for the game's language, or null when disabled or the CD's launcher files are not there.</summary>
	public static AutorunAssets? FindAssets( string[] args, SetupSettings settings, string gameFolder, string? cdFolder, string languageName )
	{
		if ( IsDisabled( args, settings ) )
			return null;
		var folder = AutorunAssets.FindFolder( [gameFolder, cdFolder] );
		if ( folder == null )
			return null;
		var assets = AutorunAssets.Load( folder, languageName, out var problem );
		if ( assets == null )
			Log.Warning( $"Autorun: '{folder}' cannot be shown ({problem})." );
		return assets;
	}

	public static AutorunScreen Create( AutorunAssets assets, string gameFolder )
	{
		var readme = assets.FindReadme( gameFolder );
		Log.Trace( $"Autorun: CD launcher in {assets.Language} from {assets.Folder} (read-me {(readme == null ? "not found" : readme)})." );
		return new AutorunScreen( new AutorunView( assets, readme != null ), readme );
	}
}
