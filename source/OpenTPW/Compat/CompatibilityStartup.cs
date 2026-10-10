namespace OpenTPW;

/// <summary>Process-wide compatibility state read by the renderer, sign text and data loaders.</summary>
public static class CompatibilityRuntime
{
	/// <summary>Active fixes (Original until startup resolves the user's choice).</summary>
	public static CompatibilityFlags Flags { get; internal set; } = CompatibilityFlags.Original;
	/// <summary>Renderer quality from the graphics settings (legacy values until startup).</summary>
	public static RenderQuality RenderQuality { get; internal set; } = RenderQuality.Legacy;
	/// <summary>Install plus read-only overlays (<c>--cd-data</c>); null before startup.</summary>
	public static DataRoots? DataRoots { get; internal set; }
	/// <summary>Optional media status from startup.</summary>
	public static IReadOnlyList<OptionalMedia.MediaStatus> Media { get; internal set; } = Array.Empty<OptionalMedia.MediaStatus>();

	/// <summary>Applies enabled hash-keyed data corrections to bytes read from <paramref name="relativePath"/>.</summary>
	public static byte[] Correct( string relativePath, byte[] data ) =>
		DataCorrections.Apply( relativePath, data, Flags.IsEnabled, message => Log?.Trace( message ) );
}

/// <summary>
/// Startup for the compatibility slice (docs/COMPATIBILITY.md): read-only CD overlay and media
/// diagnostics, compatibility profile, graphics preset, robustness hooks and the startup log of
/// active fixes and approximations. Runs before the renderer exists (samplers read the settings).
/// </summary>
internal static class CompatibilityStartup
{
	public static void Initialize( string[] args, string dataDirectory )
	{
		var smokeTest = args.Contains( "--smoke-test" );
		SdtArchive.Diagnostic = message => Log.Warning( message );

		// Read-only CD overlay: --cd-data, then OPENTPW_CD_DATA.
		var roots = new DataRoots( dataDirectory );
		var cdData = GetOption( args, "--cd-data", "the extracted original CD (or its Data folder)" ) ?? Environment.GetEnvironmentVariable( "OPENTPW_CD_DATA" );
		if ( !string.IsNullOrWhiteSpace( cdData ) )
		{
			var overlay = roots.Add( "CD data", cdData, DataOverlayRole.CdFallback );
			Log.Trace( $"CD data overlay (read-only fallback for missing files): {overlay.DataDirectory}" );
		}
		Mount( roots );

		var compatibilityDiagnostics = new List<string>();
		var compatibilityPath = CompatibilitySettings.GetDefaultPath();
		var compatibility = smokeTest ? CompatibilitySettings.Default : CompatibilitySettings.Load( compatibilityPath, compatibilityDiagnostics );
		compatibility = ApplyCommandLine( compatibility, args );
		if ( args.Contains( "--save-compat-settings" ) && !smokeTest )
			compatibility.Save( compatibilityPath );
		CompatibilityRuntime.Flags = compatibility.Resolve( compatibilityDiagnostics );
		foreach ( var diagnostic in compatibilityDiagnostics )
			Log.Warning( $"Compatibility: {diagnostic}" );

		var graphicsDiagnostics = new List<string>();
		var graphicsPath = GraphicsSettings.GetDefaultPath();
		var graphics = smokeTest ? GraphicsSettings.Default : GraphicsSettings.Load( graphicsPath, graphicsDiagnostics );
		var preset = GetOption( args, "--graphics-preset", "low, medium, high or enhanced" );
		if ( preset != null )
		{
			if ( !Enum.TryParse<GraphicsPreset>( preset, true, out var parsed ) || parsed == GraphicsPreset.Custom || int.TryParse( preset, out _ ) )
				throw new ArgumentException( $"--graphics-preset expects low, medium, high or enhanced, not '{preset}'." );
			graphics = graphics with { Preset = parsed };
		}
		var service = new GraphicsSettingsService( path => FileSystem.FileExists( path ) ? FileSystem.OpenRead( path ) : null, graphics,
			smokeTest ? null : graphicsPath, () => CompatibilityRuntime.Flags );
		if ( args.Contains( "--save-graphics-settings" ) && !smokeTest )
			service.Apply( service.Current );
		IGraphicsSettings.Instance = service;
		CompatibilityRuntime.RenderQuality = service.Effective;
		service.Changed += () => CompatibilityRuntime.RenderQuality = CompatibilityRuntime.RenderQuality with { ViewDistanceScale = service.Effective.ViewDistanceScale };
		TexturePack.Activate( service.Current.TexturePackName, graphicsDiagnostics );
		foreach ( var diagnostic in graphicsDiagnostics.Concat( service.Diagnostics ) )
			Log.Warning( $"Graphics settings: {diagnostic}" );

		LogSummary( service );
	}

	/// <summary>Wires the overlays into the game file system and the movie library.</summary>
	internal static void Mount( DataRoots roots )
	{
		CompatibilityRuntime.DataRoots = roots;
		if ( roots.Overlays.Count > 0 )
		{
			var fallbacks = roots.Overlays.Select( overlay =>
			{
				var fileSystem = new BaseFileSystem( overlay.DataDirectory );
				fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
				fileSystem.RegisterArchiveHandler<SdtArchive>( ".sdt" );
				return (Overlay: overlay, FileSystem: fileSystem);
			} ).ToArray();
			FileSystem.FallbackResolver = relativePath =>
			{
				foreach ( var (overlay, fileSystem) in fallbacks )
				{
					if ( DataRoots.ResolveIn( overlay.DataDirectory, relativePath ) is { } corrected )
						return (fileSystem, "/" + corrected);
				}
				return null;
			};
			MovieLibrary.FallbackDataDirectories = roots.Overlays.Select( overlay => overlay.DataDirectory ).ToArray();
		}
		CompatibilityRuntime.Media = OptionalMedia.Check( roots );
		foreach ( var line in OptionalMedia.Describe( CompatibilityRuntime.Media, roots.Overlays.Count > 0 ) )
		{
			if ( line.StartsWith( "Optional media missing", StringComparison.Ordinal ) )
				Log?.Warning( line );
			else
				Log?.Trace( line );
		}
	}

	internal static CompatibilitySettings ApplyCommandLine( CompatibilitySettings settings, string[] args )
	{
		var profile = GetOption( args, "--compat-profile", "original, recommended or custom" );
		if ( profile != null )
		{
			if ( !Enum.TryParse<CompatibilityProfile>( profile, true, out var parsed ) || int.TryParse( profile, out _ ) )
				throw new ArgumentException( $"--compat-profile expects original, recommended or custom, not '{profile}'." );
			settings = settings with { Profile = parsed };
		}
		var fixes = new Dictionary<string, bool>( settings.Fixes, StringComparer.Ordinal );
		for ( var i = 0; i < args.Length; i++ )
		{
			if ( args[i] != "--compat-fix" )
				continue;
			if ( i + 1 >= args.Length || args[i + 1].StartsWith( "--" ) )
				throw new ArgumentException( "--compat-fix requires a fix id, optionally followed by =on or =off." );
			var parts = args[i + 1].Split( '=', 2 );
			if ( CompatibilityFixes.Find( parts[0] ) == null )
				throw new ArgumentException( $"Unknown compatibility fix '{parts[0]}'. Known: {string.Join( ", ", CompatibilityFixes.All.Select( fix => fix.Id ) )}." );
			fixes[parts[0]] = parts.Length == 1 || parts[1] is "on" or "true" or "1";
			settings = settings with { Profile = CompatibilityProfile.Custom };
		}
		return settings with { Fixes = fixes };
	}

	private static void LogSummary( GraphicsSettingsService graphics )
	{
		var flags = CompatibilityRuntime.Flags;
		Log.Trace( $"Compatibility profile: {flags.Describe()}." );
		foreach ( var id in flags.EnabledFixes )
		{
			var fix = CompatibilityFixes.Find( id )!;
			Log.Trace( $"Active fix [EXT:COMPAT-FIX {id}] ({fix.Kind}): {fix.Title}." );
		}
		if ( !flags.IsOriginalSimulation )
			Log.Warning( $"Simulation-affecting fixes active ({string.Join( ", ", flags.SimulationFixes )}); saves record them as '{flags.ToMetadata()}'." );
		var quality = CompatibilityRuntime.RenderQuality;
		Log.Trace( $"Graphics preset {graphics.Current.Preset}: {quality.Filter} filtering{(quality.Filter == TextureFilterMode.Anisotropic ? $" {quality.MaxAnisotropy}x" : "")}, mipmaps {(quality.Mipmaps ? "on" : "off")}, view distance x{quality.ViewDistanceScale:0.##}." );
		foreach ( var deviation in graphics.SimulationDeviations() )
			Log.Warning( $"Graphics preset changes the simulation: {deviation}." );
		ApproximationRegister.LogAll( message => Log.Warning( message ) );
	}

	private static string? GetOption( string[] args, string name, string description )
	{
		var index = Array.IndexOf( args, name );
		if ( index < 0 )
			return null;
		if ( index + 1 >= args.Length || args[index + 1].StartsWith( "--" ) )
			throw new ArgumentException( $"{name} requires {description}." );
		return args[index + 1];
	}
}
