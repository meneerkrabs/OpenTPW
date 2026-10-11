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
	private static readonly HashSet<string> loadedLevels = new( StringComparer.OrdinalIgnoreCase );
	private static readonly HashSet<string> requestedLevels = new( StringComparer.OrdinalIgnoreCase );
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
	}

	/// <summary>
	/// Mounts the copied files like <c>Game.Run</c> does and shows the front end in the canvas.
	/// <paramref name="origin"/> is the page's own address, offered as the online server.
	/// </summary>
	[JSExport]
	public static void Start( string origin )
	{
		try
		{
			OnlineSession.SuggestedServerUrl = origin;
			StartGame();
		}
		catch ( Exception exception )
		{
			// The page only sees the message; the console gets the whole trace.
			Console.Error.WriteLine( exception );
			throw;
		}
	}

	private static void StartGame()
	{
		WriteContent();
		var dataDirectory = GameLanguage.FindEntry( GameRoot, "data", true )
			?? throw new DirectoryNotFoundException( "No Data folder among the chosen files." );
		FileSystem = new BaseFileSystem( dataDirectory );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		FileSystem.RegisterArchiveHandler<SdtArchive>( ".sdt" );
		try
		{
			GameLanguage.Current = GameLanguage.Resolve( dataDirectory, null, null );
		}
		catch ( DirectoryNotFoundException exception )
		{
			Log.Warning( exception.Message );
		}
		Directory.CreateDirectory( "/save" );
		Directory.CreateDirectory( "/cache" );
		SaveFileSystem = new BaseFileSystem( "/save" );
		CacheFileSystem = new BaseFileSystem( "/cache" );

		// Use the same persisted graphics choice as the desktop, including an explicit Original choice.
		TexturePack.RequestMissing = Page.RequestTexture;
		CompatibilityStartup.Initialize( Array.Empty<string>(), dataDirectory );

		// Sound plays through WebAudio (Browser/WebAudioOutput.cs); movies are not in the browser yet.
		GameAudio.Enabled = true;
		renderer = new Renderer( DisplaySettings.Default, null );
		Render = renderer;
		flow = new GameFlow { OnlineFolders = new OnlineFolders( "/online" ) };
		// Level folders are copied when a park is first started; the lobby keeps running meanwhile.
		GameFlow.LevelDataReady = level =>
		{
			if ( loadedLevels.Contains( level ) )
				return true;
			if ( requestedLevels.Add( level ) )
				Page.RequestLevel( level );
			return false;
		};
		renderer.OnUpdate += flow.Update;
		renderer.OnRender += flow.Render;
		flow.ShowFrontEnd();
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
