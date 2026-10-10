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
	private static readonly HashSet<string> loadedLevels = new( StringComparer.OrdinalIgnoreCase );
	private static readonly HashSet<string> requestedLevels = new( StringComparer.OrdinalIgnoreCase );

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

		// The desktop picks the texture choice from graphics.json; the browser has no settings file and uses
		// the default Enhanced choice, which without a locally built pack means the shipped interface art.
		var diagnostics = new List<string>();
		TexturePack.Activate( TexturePack.DefaultName, diagnostics );
		foreach ( var diagnostic in diagnostics )
			Log.Warning( diagnostic );

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
	public static bool Frame() => renderer?.Frame() ?? false;
}
