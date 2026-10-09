namespace OpenTPW;

/// <summary>Builds the renderer from the user display settings and the display command-line options.</summary>
internal static class DisplayStartup
{
	/// <summary>
	/// Smoke tests ignore and never write the user settings file, so they only see defaults plus the
	/// command line. <c>--save-display-settings</c> persists the command-line values.
	/// </summary>
	public static Renderer CreateRenderer( string[] args )
	{
		var diagnostics = new List<string>();
		var smokeTest = args.Contains( "--smoke-test" );
		var path = DisplaySettings.GetDefaultPath();
		var settings = smokeTest ? DisplaySettings.Default : DisplaySettings.Load( path, diagnostics );
		settings = settings.ApplyCommandLine( args, diagnostics );
		foreach ( var diagnostic in diagnostics )
			Log.Warning( $"Display settings: {diagnostic}" );
		if ( args.Contains( "--save-display-settings" ) && !smokeTest )
		{
			settings.Save( path );
			Log.Trace( $"Display settings saved to {path}." );
		}
		Log.Trace( $"Display settings: {settings.Describe()}." );
		var renderer = new Renderer( settings, smokeTest ? null : path );
		renderer.DisplayDiagnostics.InsertRange( 0, diagnostics );
		return renderer;
	}
}
