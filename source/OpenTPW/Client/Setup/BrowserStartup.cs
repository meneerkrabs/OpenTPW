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
			throw new ArgumentException( "Theme Park Inc data needs an explicit experimental theme: arabian, science or water; its front end is unsupported." );
		if ( edition.Edition != GameEdition.ThemeParkInc && theme.Length != 0 )
			throw new ArgumentException( "The chosen files are not identified as Theme Park Inc. Choose Theme Park World or provide the Theme Park Inc installation." );
		if ( theme.Length != 0 && GameLanguage.FindEntry( dataDirectory, Path.Combine( "levels", theme ), true ) == null )
			throw new DirectoryNotFoundException( $"No {theme} theme folder among the chosen files." );
		return edition;
	}

	/// <summary>Starts a fresh import in the runtime-owned staging folder, before any renderer or world exists.</summary>
	public static void BeginImport( string gameRoot, bool rendererCreated )
	{
		if ( rendererCreated )
			throw new InvalidOperationException( "The game has already started loading. Reload the page before choosing another installation." );
		if ( Directory.Exists( gameRoot ) )
			Directory.Delete( gameRoot, recursive: true );
	}
}
