namespace OpenTPW;

/// <summary>The browser's explicit lobby or experimental-theme choice, checked before creating a renderer.</summary>
public static class BrowserStartup
{
	public static GameEditionReport Validate( string dataDirectory, string theme )
	{
		if ( theme.Length != 0 && !GameEditions.IsViewableLevel( theme ) )
			throw new ArgumentException( "Choose an experimental Theme Park Inc theme: arabian, science or water." );
		var edition = GameEditions.Detect( dataDirectory );
		if ( edition.Edition == GameEdition.ThemeParkInc && theme.Length == 0 )
			throw new ArgumentException( "Theme Park Inc data needs an explicit experimental theme: arabian, science or water; the Personnel File start screen is not offered in the browser yet." );
		if ( edition.Edition != GameEdition.ThemeParkInc && theme.Length != 0 )
			throw new ArgumentException( "The chosen files are not identified as Theme Park Inc. Choose Theme Park World or provide the Theme Park Inc installation." );
		if ( theme.Length != 0 && GameLanguage.FindEntry( dataDirectory, Path.Combine( "levels", theme ), true ) == null )
			throw new DirectoryNotFoundException( $"No {theme} theme folder among the chosen files." );
		return edition;
	}

	/// <summary>Starts a fresh import in the runtime-owned staging folder, before any renderer or world exists.</summary>
	public static string[] BeginImport( string gameRoot, bool rendererCreated, IEnumerable<string>? themeDirectories = null )
	{
		if ( rendererCreated )
			throw new InvalidOperationException( "The game has already started loading. Reload the page before choosing another installation." );
		var directories = ValidatedDirectories( themeDirectories ?? Array.Empty<string>() );
		if ( Directory.Exists( gameRoot ) )
			Directory.Delete( gameRoot, recursive: true );
		return directories;
	}

	/// <summary>Preserves observed source directories before edition detection; never marks their payload loaded.</summary>
	public static void PreserveThemeDirectories( string dataDirectory, IEnumerable<string> themeDirectories )
	{
		foreach ( var directory in ValidatedDirectories( themeDirectories ) )
		{
			var parts = directory.Split( '/' );
			// Anchor to the actual copied Data/levels spelling rather than creating a differently cased root.
			var levels = GameLanguage.FindEntry( dataDirectory, parts[1], true ) ?? Path.Combine( dataDirectory, parts[1] );
			if ( GameLanguage.FindEntry( levels, parts[2], true ) == null )
				Directory.CreateDirectory( Path.Combine( levels, parts[2] ) );
		}
	}

	private static string[] ValidatedDirectories( IEnumerable<string> directories ) => directories.Select( directory =>
	{
		var parts = directory?.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries );
		if ( parts == null || parts.Length != 3 || !parts[0].Equals( "data", StringComparison.OrdinalIgnoreCase )
			|| !parts[1].Equals( "levels", StringComparison.OrdinalIgnoreCase )
			|| !(GameEditions.IsViewableLevel( parts[2] ) || new[] { "jungle", "fantasy", "hallow", "space" }.Contains( parts[2], StringComparer.OrdinalIgnoreCase )) )
			throw new ArgumentException( "The game folder information has an unsupported theme path. Choose the installation again." );
		return string.Join( '/', parts );
	} ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
}
