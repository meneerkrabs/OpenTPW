using System.Runtime.InteropServices.JavaScript;
using System.Buffers.Binary;
using System.Text.Json;

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
	private const string ClarityPackRoot = "/config/texture-packs/clarity-enhanced";
	private const int MaximumTextureBytes = 25 * 1024 * 1024;
	private const long TextureCacheBytes = 128 * 1024 * 1024;
	private readonly record struct DownloadedTexture( string RelativePath, string GamePath, int Size );
	private static readonly Queue<DownloadedTexture> textureDownloads = new();
	private static readonly Queue<(string Path, int Size)> textureCache = new();
	private static long textureCacheSize;
	private static long pendingTextureBytes;

	/// <summary>Folders the page keeps in the browser's storage between visits (main.js): saves and options, online files, settings.</summary>
	private static readonly string[] PersistentFolders = { "/save", "/online", "/config" };

	public static void Main()
	{
		// Display and graphics settings go to /config, which the page keeps between visits.
		Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", "/config" );
		Log = new Logger();
		Console.WriteLine( "OpenTPW: runtime ready." );
	}

	/// <summary>Stores one game file at its path below the game folder (e.g. Data/levels/jungle/terrain.wad).</summary>
	[JSExport]
	public static void AddFile( string relativePath, byte[] contents )
	{
		var path = Path.Combine( GameRoot, relativePath.TrimStart( '/' ) );
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
		var dataDirectory = GameLanguage.FindEntry( GameRoot, "data", true )
			?? throw new DirectoryNotFoundException( "No Data folder among the chosen files." );
		// [EXT:tpi-viewer] explicit Theme Park Inc starts reuse the experimental slice and retain all capability refusals.
		BrowserStartup.PreserveThemeDirectories( dataDirectory, sourceThemeDirectories );
		var edition = BrowserStartup.Validate( dataDirectory, theme );
		GameEditions.Select( edition.Edition );
		// The game data changes only through AddFile, which invalidates the remembered lookups.
		FileSystem = new BaseFileSystem( dataDirectory ) { CacheLookups = true };
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		FileSystem.RegisterArchiveHandler<SdtArchive>( ".sdt" );
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
		TexturePack.RequestMissing = Page.RequestTexture;
		CompatibilityStartup.Initialize( Array.Empty<string>(), dataDirectory );
		// [EXT:mods] The browser has the official extensions only (no files-based mods yet), with the choices in /config/mods.json (docs/MODS.md).
		OpenTPW.Mods.ModStartup.InitializeOfficial( Log.Trace, Log.Warning );

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
		flow = new GameFlow { OnlineFolders = FeatureGates.Online ? new OnlineFolders( "/online" ) : null };
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
				Page.RequestLevel( level );
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
		if ( IsTextureCache( path ) || !PersistentFolders.Any( folder => path.StartsWith( folder + "/", StringComparison.Ordinal ) ) )
			return;
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllBytes( path, contents );
	}

	/// <summary>The files to keep, one per entry as <c>path</c>, a tab and a change stamp (write time and length).</summary>
	[JSExport]
	public static string[] PersistentFiles() => PersistentFolders
		.Where( Directory.Exists )
		.SelectMany( folder => Directory.EnumerateFiles( folder, "*", SearchOption.AllDirectories ) )
		.Where( path => !IsTextureCache( path ) )
		.Select( path => $"{path}\t{File.GetLastWriteTimeUtc( path ).Ticks}:{new FileInfo( path ).Length}" )
		.ToArray();

	[JSExport]
	public static byte[] ReadFile( string path ) => File.ReadAllBytes( path );

	private static bool IsTextureCache( string path ) => path.StartsWith( ClarityPackRoot + "/textures/", StringComparison.Ordinal );

	/// <summary>The small Clarity index shipped with this build; the browser never trusts a mutable remote index.</summary>
	[JSExport]
	public static string ClarityDistribution()
	{
		using var stream = typeof( Program ).Assembly.GetManifestResourceStream( "content/texture-packs/clarity-enhanced/distribution.json" );
		return stream == null ? "" : new StreamReader( stream ).ReadToEnd();
	}

	/// <summary>Installs the packaged manifest before startup; PNGs are fetched on demand.</summary>
	[JSExport]
	public static void InstallClarity( string manifestJson )
	{
		var manifest = JsonSerializer.Deserialize<TexturePackManifest>( manifestJson );
		if ( manifest?.Format != TexturePack.FormatVersion )
			throw new InvalidDataException( "Unsupported Clarity texture pack." );
		Directory.CreateDirectory( ClarityPackRoot + "/textures" );
		File.WriteAllText( ClarityPackRoot + "/pack.json", manifestJson );
	}

	[JSExport]
	public static bool ClarityEnabled() => TexturePack.IsDefaultActive;

	/// <summary>Back pressure for downloads awaiting their render frame, in addition to the page's concurrency bound.</summary>
	[JSExport]
	public static bool TextureDownloadCapacity( int bytes ) => bytes > 0 && bytes <= MaximumTextureBytes && pendingTextureBytes + bytes <= 32 * 1024 * 1024;

	/// <summary>Stores one verified PNG and queues its GPU refresh; downloaded PNGs never enter save/options storage.</summary>
	[JSExport]
	public static bool AddTextureFile( string relativePath, byte[] contents )
	{
		if ( !TexturePack.IsDefaultActive || !TextureDownloadCapacity( contents.Length ) || !relativePath.StartsWith( "textures/", StringComparison.Ordinal )
			|| !relativePath.EndsWith( ".png", StringComparison.Ordinal ) || relativePath.Length > 1024
			|| !relativePath.All( character => char.IsAsciiLetterOrDigit( character ) || character is '/' or '-' or '_' or '.' or '!' )
			|| relativePath.Split( '/' ).Any( part => part.Length == 0 || part is "." or ".." ) )
			return false;
		if ( contents.Length < 33 || !contents.AsSpan( 0, 8 ).SequenceEqual( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } ) )
			return false;
		var width = BinaryPrimitives.ReadUInt32BigEndian( contents.AsSpan( 16, 4 ) );
		var height = BinaryPrimitives.ReadUInt32BigEndian( contents.AsSpan( 20, 4 ) );
		if ( width == 0 || height == 0 || width > 8192 || height > 8192 || (long)width * height > 16 * 1024 * 1024 )
			return false;
		var path = ClarityPackRoot + "/" + relativePath;
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllBytes( path, contents );
		textureDownloads.Enqueue( new DownloadedTexture( relativePath, relativePath["textures/".Length..^".png".Length], contents.Length ) );
		pendingTextureBytes += contents.Length;
		return true;
	}

	/// <summary>One PNG refresh per frame. The temporary file working set stays bounded across visits to different parks.</summary>
	private static void RefreshDownloadedTexture()
	{
		if ( !textureDownloads.TryDequeue( out var downloaded ) )
			return;
		pendingTextureBytes -= downloaded.Size;
		var path = ClarityPackRoot + "/" + downloaded.RelativePath;
		try
		{
			if ( TexturePack.IsDefaultActive )
			{
				var key = TexturePack.RelativeFileName( downloaded.GamePath );
				foreach ( var target in Texture.ReloadTargets().Where( target => TexturePack.RelativeFileName( target.Path ) == key ) )
				{
					var (data, width, height) = Texture.DecodeWct( target.Path );
					Texture.Reload( target.Path, target.Flags, data, width, height );
				}
				TexturePackSwitch.NotifyFilesAvailable( new[] { downloaded.GamePath } );
			}
		}
		catch ( Exception exception ) when ( exception is not OutOfMemoryException )
		{
			Log.Warning( $"Clarity replacement {downloaded.GamePath} could not be refreshed: {exception.Message}" );
		}
		textureCache.Enqueue( (path, downloaded.Size) );
		textureCacheSize += downloaded.Size;
		while ( textureCacheSize > TextureCacheBytes && textureCache.TryDequeue( out var old ) )
		{
			File.Delete( old.Path );
			textureCacheSize -= old.Size;
		}
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

	/// <summary>The page has copied a level's folder (or given up; the level then reports what is missing).</summary>
	[JSExport]
	public static void LevelLoaded( string level ) => loadedLevels.Add( level );

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
		RefreshDownloadedTexture();
		return renderer.Frame();
	}
}
