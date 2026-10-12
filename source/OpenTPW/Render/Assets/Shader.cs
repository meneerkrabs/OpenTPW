using Veldrid;

namespace OpenTPW;

public class Shader : Asset
{
	private ShaderInfo shaderInfo;
	private bool released;

	public VertexElementDescription[] VertexElements => shaderInfo.Reflection.VertexElements;
	public ResourceLayoutDescription[] ResourceLayouts => shaderInfo.Reflection.ResourceLayouts;
	public Veldrid.Shader[] ShaderProgram => shaderInfo.ShaderProgram;
	public bool IsDirty { get; private set; }
	public Action OnRecompile { get; set; }

	// One hot-reload watcher per shader file, shared by every Shader compiled from it. Each
	// Material creates its own Shader, and on Linux every FileSystemWatcher holds an inotify
	// instance (128 per user by default), so per-Shader watchers crashed the lobby scene.
	private static readonly Dictionary<string, FileSystemWatcher?> watchers = new();

	/// <summary>
	/// Recompile shaders when their files change: a development aid, off unless asked for with
	/// <c>--shader-hot-reload</c> or <c>OPENTPW_SHADER_HOT_RELOAD=1</c>. On macOS starting a file watcher calls
	/// <c>sync()</c>, which waits for every mounted file system, so one unresponsive network share kept the game from
	/// starting.
	/// </summary>
	public static bool HotReload { get; set; } = Environment.GetEnvironmentVariable( "OPENTPW_SHADER_HOT_RELOAD" ) == "1";

	internal Shader( string path )
	{
		Path = System.IO.Path.GetFullPath( path, AppContext.BaseDirectory );
		All.Add( this );

		Recompile();
		Watch();
	}

	private void Watch()
	{
		// The browser has no file watching, and its shaders are precompiled (docs/WEB.md).
		if ( !HotReload || OperatingSystem.IsBrowser() )
			return;
		lock ( watchers )
		{
			if ( !watchers.TryGetValue( Path, out var watcher ) )
			{
				watcher = new FileSystemWatcher( System.IO.Path.GetDirectoryName( Path )!, System.IO.Path.GetFileName( Path ) );
				watcher.NotifyFilter = NotifyFilters.Attributes
									 | NotifyFilters.CreationTime
									 | NotifyFilters.DirectoryName
									 | NotifyFilters.FileName
									 | NotifyFilters.LastAccess
									 | NotifyFilters.LastWrite
									 | NotifyFilters.Security
									 | NotifyFilters.Size;
				try
				{
					watcher.EnableRaisingEvents = true;
				}
				catch ( IOException exception )
				{
					// Hot reload is a development aid; running out of watchers must not stop the game.
					Log.Warning( $"Shader hot reload disabled for {Path}: {exception.Message}" );
					watcher.Dispose();
					watcher = null;
				}
				watchers[Path] = watcher;
			}
			if ( watcher != null )
				watcher.Changed += OnWatcherChanged;
		}
	}

	/// <summary>Stops this shader's watcher subscription and releases its independently owned native shader objects.</summary>
	internal void ReleaseOwnedResources()
	{
		if ( released )
			return;
		released = true;
		Asset.All.Remove( this );
		lock ( watchers )
			if ( watchers.TryGetValue( Path, out var watcher ) && watcher != null )
				watcher.Changed -= OnWatcherChanged;
		var program = ShaderProgram;
		// The constructor registers before compiling, so a shader whose file was missing or whose
		// compilation threw owns no native objects to retire.
		if ( program[0] == null && program[1] == null )
			return;
		// Without a renderer the shader was never submitted to a frame, so its objects are freed at
		// once instead of after the last drawn frame.
		if ( Render is { } renderer )
			renderer.ScheduleDelete( () =>
			{
				foreach ( var shader in program )
					shader.Dispose();
			} );
		else
			foreach ( var shader in program )
				shader.Dispose();
	}

	public static bool IsFileReady( string path )
	{
		try
		{
			using ( FileStream inputStream = File.OpenRead( path ) )
				return inputStream.Length > 0;
		}
		catch ( Exception )
		{
			return false;
		}
	}

	private void OnWatcherChanged( object sender, FileSystemEventArgs e )
	{
		if ( !released )
			IsDirty = true;
	}

	public void Recompile()
	{
		if ( released || !IsFileReady( Path ) )
			return;

		shaderInfo = ShaderCompiler.CompileShader( Path );
		OnRecompile?.Invoke();
		IsDirty = false;
	}
}
