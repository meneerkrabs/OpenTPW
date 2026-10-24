using System.Runtime.InteropServices.JavaScript;

namespace OpenTPW;

/// <summary>
/// The game in the browser (docs/WEB.md). The page copies the player's own game files into the
/// runtime's in-memory file system, then starts the front end; the page calls <see cref="Frame"/>
/// from requestAnimationFrame. Nothing from the game is shipped.
/// </summary>
public static partial class Program
{
	private const string GameRoot = "/game";
	private static Renderer? renderer;
	private static GameFlow? flow;
	private static string[] sourceThemeDirectories = Array.Empty<string>();
	private static readonly HashSet<string> loadedLevels = new( StringComparer.OrdinalIgnoreCase );
	private static readonly HashSet<string> requestedLevels = new( StringComparer.OrdinalIgnoreCase );
	private static readonly HashSet<string> loadedLanguages = new( StringComparer.OrdinalIgnoreCase );
	private static readonly HashSet<string> requestedLanguages = new( StringComparer.OrdinalIgnoreCase );
	private static readonly HashSet<string> failedLanguages = new( StringComparer.OrdinalIgnoreCase );
	private static string[] installedLanguages = Array.Empty<string>();

	/// <summary>Folders the page keeps in the browser's storage between visits (main.js): saves and options, online files, settings.</summary>
	private static readonly string[] PersistentFolders = { "/save", "/online", "/config" };

	public static void Main()
	{
		// Display and graphics settings go to /config, which the page keeps between visits.
		Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", "/config" );
		Log = new Logger();
		// The Extensions and mods screen asks the page for a folder of the player's own game files (docs/WEB.md).
		OpenTPW.Mods.BrowserModInputs.Choose = Page.ChooseInput;
		Console.WriteLine( "OpenTPW: runtime ready." );
	}

	/// <summary>Empties the copy of an original game's files that mods read (before the page copies a chosen folder again).</summary>
	[JSExport]
	public static void BeginInput( string edition ) => OpenTPW.Mods.BrowserModInputs.Begin( edition );

	/// <summary>Stores one file of an original game's files for mods, as <c>Data/...</c> below the edition's folder.</summary>
	[JSExport]
	public static void AddInputFile( string edition, string relativePath, byte[] contents )
		=> OpenTPW.Mods.BrowserModInputs.AddFile( edition, relativePath, contents );

	/// <summary>Finishes the import: checks it is complete and Theme Park Inc data, then makes it the live copy mods read.</summary>
	[JSExport]
	public static void PublishInput( string edition, int fileCount ) => OpenTPW.Mods.BrowserModInputs.Publish( edition, fileCount );

	/// <summary>Gives up an import: nothing half-copied stays behind.</summary>
	[JSExport]
	public static void AbortInput( string edition ) => OpenTPW.Mods.BrowserModInputs.Abort( edition );

	/// <summary>The page finished a mod input import or restore (an empty message), or gave up (why). The Extensions and mods screen refreshes.</summary>
	[JSExport]
	public static void InputChanged( string message ) => OpenTPW.Mods.BrowserModInputs.Changed( message );

	/// <summary>Stores one game file at its path below the game folder (e.g. Data/levels/jungle/terrain.wad).</summary>
	[JSExport]
	public static void AddFile( string relativePath, byte[] contents )
	{
		var path = Path.GetFullPath( Path.Combine( GameRoot, relativePath.TrimStart( '/' ) ) );
		if ( !path.StartsWith( GameRoot + "/", StringComparison.Ordinal ) )
			throw new ArgumentException( $"'{relativePath}' is outside the game folder." );
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllBytes( path, contents );
		// A theme folder copied while the game runs: the game data's remembered lookups may name it missing.
		FileSystem?.InvalidateLookups();
	}

	/// <summary>
	/// Mounts the copied files like <c>Game.Run</c> does and shows the front end in the canvas.
	/// <paramref name="origin"/> is the page's own address, offered as the online server.
	/// </summary>
	[JSExport]
	public static void Start( string origin )
		=> StartSelected( origin, "" );

	/// <summary>Starts the existing experimental slice, with an explicitly selected Theme Park Inc theme.</summary>
	[JSExport]
	public static void StartExperimental( string origin, string theme )
		=> StartSelected( origin, theme );

	/// <summary>Clears only this runtime's game-file staging before a new setup attempt.</summary>
	[JSExport]
	public static void BeginImport( string[] themeDirectories )
	{
		sourceThemeDirectories = BrowserStartup.BeginImport( GameRoot, renderer != null || flow != null, themeDirectories );
		loadedLevels.Clear();
		requestedLevels.Clear();
		loadedLanguages.Clear();
		requestedLanguages.Clear();
		failedLanguages.Clear();
		installedLanguages = Array.Empty<string>();
		FileSystem = null!;
	}

	private static void StartSelected( string origin, string theme )
	{
		try
		{
			OnlineSession.SuggestedServerUrl = origin;
			StartGame( theme );
		}
		catch ( Exception exception )
		{
			// The page only sees the message; the console gets the whole trace.
			Console.Error.WriteLine( exception );
			throw;
		}
	}

	private static void StartGame( string theme )
	{
		if ( renderer != null || flow != null )
			throw new InvalidOperationException( "The game has already started loading. Reload the page before starting again." );
		WriteContent();
		// [EXT:pools-render] the pools sprite sets are not embedded: the page fetches them after the start (PoolSpriteDelivery) and the level setup uses a set only when it is complete
		PoolSpriteDelivery.Delivered = true;
		var dataDirectory = GameLanguage.FindEntry( GameRoot, "data", true )
			?? throw new DirectoryNotFoundException( "No Data folder among the chosen files." );
		// [EXT:tpi-viewer] explicit Theme Park Inc starts reuse the experimental slice and retain all capability refusals.
		BrowserStartup.PreserveThemeDirectories( dataDirectory, sourceThemeDirectories );
		var edition = BrowserStartup.Validate( dataDirectory, theme );
		GameEditions.Select( edition.Edition );
		// The game data changes only through AddFile, which invalidates the remembered lookups.
		FileSystem = GameDataFileSystem.Create( dataDirectory );
		// Like the desktop: the saved choice (/config/language.json, kept by the page) and the CD's other languages
		// beside Data (the page copies their text, docs/WEB.md). A saved language the folder lacks falls back to the default.
		var languageDiagnostics = new List<string>();
		var saved = LanguageSettings.Load( LanguageSettings.GetDefaultPath(), languageDiagnostics );
		foreach ( var diagnostic in languageDiagnostics )
			Log.Warning( diagnostic );
		// The page copies a saved language's speech before it starts; when that failed the default is used (text and speech together).
		var savedLanguage = saved.Language;
		if ( savedLanguage != null && failedLanguages.Contains( savedLanguage ) )
		{
			Log.Warning( $"The speech files of the saved language {savedLanguage} could not be copied; using the default language. Reload to try again." );
			savedLanguage = null;
		}
		LanguageStartup.Apply( dataDirectory, savedLanguage, null, GameRoot, true, Log.Warning );
		installedLanguages = GameLanguage.FindLanguages( dataDirectory, null ).ToArray();
		Directory.CreateDirectory( "/save" );
		Directory.CreateDirectory( "/cache" );
		SaveFileSystem = new BaseFileSystem( "/save" );
		CacheFileSystem = new BaseFileSystem( "/cache" );

		// Use the same persisted graphics choice as the desktop, including an explicit Original choice.
		CompatibilityStartup.Initialize( Array.Empty<string>(), dataDirectory );
		// [EXT:mods] The browser has the data mods shipped with the page (content/mods) and the official extensions, with the choices in /config/mods.json (docs/MODS.md).
		OpenTPW.Mods.ModStartup.InitializeBrowser( Log.Trace, Log.Warning );
		// [EXT:extensions-apply-at-load] a park load or the Extensions and mods screen switched the pools extension on after the start: the page fetches its sprite sets now (the start's fetch skipped them)
		OpenTPW.Mods.ModStartup.ExtensionsSwitched += _ =>
		{
			if ( !FeatureGates.Pools )
				return;
			PoolSpriteDelivery.Retry();
			Page.RequestPoolSprites();
		};

		// Sound plays through WebAudio (Browser/WebAudioOutput.cs); movies are not in the browser yet.
		GameAudio.Enabled = true;
		// The display choices the browser honours (render scale, interface scale, frame cap) are kept in /config like the desktop's;
		// the canvas sets the size, so a stored window size or mode has no effect here.
		var displayPath = DisplaySettings.GetDefaultPath();
		var displayDiagnostics = new List<string>();
		var displaySettings = DisplaySettings.Load( displayPath, displayDiagnostics );
		foreach ( var diagnostic in displayDiagnostics )
			Log.Warning( diagnostic );
		renderer = new Renderer( displaySettings, displayPath );
		Render = renderer;
		// [EXT:extension-online-gate] no online folder while the opentpw.online extension is off (docs/MODS.md)
		// [EXT:online-coop-session] the live acceptance's page runs without automatic advice, as the native run does (--no-advisor)
		flow = new GameFlow { OnlineFolders = FeatureGates.Online ? new OnlineFolders( "/online" ) : null, AutomaticAdvisorEnabled = !coopLiveMode };
		// Level folders are copied when a park is first started; the lobby keeps running meanwhile.
		// A language other than the installed one needs its speech files (about 27 MB) beside the text; they are copied when the
		// language is chosen, at once for a saved choice, and a park or a switch waits for them so the advisor never mixes voices.
		GameFlow.LanguageDataReady = LanguageDataReady;
		GameFlow.LanguageDataFailed = language => failedLanguages.Contains( language );
		// Choosing a language again after a failed copy tries again.
		GameFlow.LanguageChosen = language =>
		{
			failedLanguages.Remove( language );
			LanguageDataReady( language );
		};
		GameFlow.LevelDataReady = level =>
		{
			if ( !LanguageDataReady( GameLanguage.Current.Name ) )
				return false;
			if ( loadedLevels.Contains( level ) )
				return true;
			if ( requestedLevels.Add( level ) )
			{
				// [EXT:online-coop-session] the live acceptance's page keeps when a park's files were asked for and arrived (Program.CoopLive.cs)
				CoopLiveLevelRequested( level );
				Page.RequestLevel( level );
			}
			return false;
		};
		LanguageDataReady( GameLanguage.Current.Name );
		renderer.OnUpdate += flow.Update;
		renderer.OnRender += flow.Render;
		if ( theme.Length == 0 )
			flow.ShowFrontEnd();
		else
			flow.StartLevel( theme, original: true, developerPanels: false );
		renderer.Start();
	}

	/// <summary>Puts back a file the page kept in the browser's storage (before <see cref="Start"/>).</summary>
	[JSExport]
	public static void RestoreFile( string path, byte[] contents )
	{
		if ( !PersistentFolders.Any( folder => path.StartsWith( folder + "/", StringComparison.Ordinal ) ) )
			return;
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllBytes( path, contents );
	}

	/// <summary>The files to keep, one per entry as <c>path</c>, a tab and a change stamp (write time and length).</summary>
	[JSExport]
	public static string[] PersistentFiles() => PersistentFolders
		.Where( Directory.Exists )
		.SelectMany( folder => Directory.EnumerateFiles( folder, "*", SearchOption.AllDirectories ) )
		.Select( path => $"{path}\t{File.GetLastWriteTimeUtc( path ).Ticks}:{new FileInfo( path ).Length}" )
		.ToArray();

	[JSExport]
	public static byte[] ReadFile( string path ) => File.ReadAllBytes( path );

	/// <summary>The pinned list (path, size, SHA-256) of the pools extension's sprite files, which the page may fetch; empty when the build has none (docs/POOLS.md, "Browser delivery").</summary>
	[JSExport]
	public static string PoolSpriteManifest() => PoolSpriteDelivery.ManifestText() ?? "";

	/// <summary>Whether the pools extension is on in this run (known once the game has started): the swim sets are then needed.</summary>
	[JSExport]
	public static bool PoolsEnabled() => FeatureGates.Pools;

	/// <summary>Whether the pool guest art extension is on as well: the land guest sets are then needed.</summary>
	[JSExport]
	public static bool PoolGuestArtEnabled() => FeatureGates.Pools && FeatureGates.PoolGuestArt;

	/// <summary>Keeps one fetched sprite file when its path, size and SHA-256 are the pinned ones; anything else is refused (returns false) and stored nowhere.</summary>
	[JSExport]
	public static bool AddPoolSpriteFile( string relativePath, byte[] contents )
		=> FeatureGates.Pools && PoolSpriteDelivery.Store( relativePath, contents );

	/// <summary>[EXT:extensions-apply-at-load] Whether a pinned sprite file is already stored, so a fetch started again after an extension switch skips it.</summary>
	[JSExport]
	public static bool PoolSpriteStored( string relativePath ) => PoolSpriteDelivery.IsStored( relativePath );

	/// <summary>The page gave up fetching the sprite files (offline, not found, a wrong file): the swimmers and the land guests keep their fallbacks.</summary>
	[JSExport]
	public static void PoolSpritesFailed()
	{
		PoolSpriteDelivery.MarkFailed( PoolSpriteDelivery.SwimGroup );
		PoolSpriteDelivery.MarkFailed( PoolSpriteDelivery.LandGroup );
	}

	/// <summary>Whether the speech files of <paramref name="language"/> are in memory; asks the page for them the first time.</summary>
	private static bool LanguageDataReady( string language )
	{
		if ( installedLanguages.Contains( language, StringComparer.OrdinalIgnoreCase ) || loadedLanguages.Contains( language ) )
			return true;
		if ( !failedLanguages.Contains( language ) && requestedLanguages.Add( language ) )
			Page.RequestLanguage( language );
		return false;
	}

	/// <summary>The page has copied all of a language's speech files.</summary>
	[JSExport]
	public static void LanguageLoaded( string language )
	{
		failedLanguages.Remove( language );
		loadedLanguages.Add( language );
	}

	/// <summary>
	/// The page could not copy a language's speech files completely. The language stays unready, so GameFlow drops a pending choice
	/// of it with a warning (<see cref="PendingLanguage"/>); choosing it again copies again (the page skips files already in).
	/// </summary>
	[JSExport]
	public static void LanguageFailed( string language )
	{
		failedLanguages.Add( language );
		requestedLanguages.Remove( language );
	}

	/// <summary>The page has copied a level's folder (or given up, after <see cref="LevelCopyFailed"/>; the level then reports what is missing).</summary>
	[JSExport]
	public static void LevelLoaded( string level )
	{
		loadedLevels.Add( level );
		CoopLiveLevelLoaded( level );
	}

	/// <summary>A level's folder could not all be copied: <see cref="LevelLoaded"/> follows, and the level reports what is missing; the live acceptance fails on it.</summary>
	[JSExport]
	public static void LevelCopyFailed( string level, string message )
	{
		Log.Warning( $"Copying the level {level} failed: {message}" );
		CoopLiveLevelCopyFailed( level, message );
	}

	/// <summary>Writes the embedded content files where the game looks for them (next to the program).</summary>
	private static void WriteContent()
	{
		var assembly = typeof( Program ).Assembly;
		foreach ( var name in assembly.GetManifestResourceNames().Where( name => name.StartsWith( "content/" ) ) )
		{
			var path = Path.GetFullPath( name, AppContext.BaseDirectory );
			Directory.CreateDirectory( Path.GetDirectoryName( path )! );
			using var source = assembly.GetManifestResourceStream( name )!;
			using var target = File.Create( path );
			source.CopyTo( target );
		}
	}

	/// <summary>One frame; false once the game has quit.</summary>
	[JSExport]
	public static bool Frame()
	{
		if ( renderer == null )
			return false;
		FrameMarks.BeginFrame();
		// [EXT:online-coop-snapshot-join] the co-op join smoke's frame while it runs (Program.CoopJoinSmokeTest.cs); [EXT:online-coop-session] the live acceptance's,
		// whose page counts its frames and keeps a frame's failure also after the run (Program.CoopLive.cs)
		if ( coopLiveMode )
			return CoopLivePageFrame( renderer );
		return coopSmoke is { } smoke ? CoopSmokeFrame( renderer, smoke ) : renderer.Frame();
	}
}
