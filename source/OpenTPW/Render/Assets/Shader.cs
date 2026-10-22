using Veldrid;

namespace OpenTPW;

public class Shader : Asset
{
	private ShaderInfo shaderInfo;
	private bool released;

	/// <summary>True for the shader a process-lifetime shared material owns; a rejected candidate's
	/// rollback skips it so the shared material keeps its shader.</summary>
	internal bool IsProcessLifetime { get; private set; }

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

	/// <summary>Marks this shader as owned by a process-lifetime shared material (see <see cref="Material.UI"/>).</summary>
	internal void MarkAsProcessLifetime() => IsProcessLifetime = true;

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
		// The constructor registers before compiling, so a shader whose file was missing or whose compilation threw holds no native program.
		// The native shaders are shared by every Shader of the same program (ShaderCompiler.CompileShader); the last reference given back
		// disposes them after the last frame that may draw with them, or at once without a renderer.
		ShaderCompiler.Release( shaderInfo );
		shaderInfo = shaderInfo with { Owner = null };
	}

	/// <summary>Whether this shader still holds a reference to its native program (false once released); for the smoke tests.</summary>
	internal bool HoldsNativeProgram => shaderInfo.Owner != null;

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

		var previous = shaderInfo;
		shaderInfo = ShaderCompiler.CompileShader( Path );
		OnRecompile?.Invoke();
		// the materials have made their pipelines from the new program; the old one is given back (the same program when the file did not change)
		ShaderCompiler.Release( previous );
		IsDirty = false;
	}
}
