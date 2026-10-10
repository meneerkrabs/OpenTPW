using Veldrid;

namespace OpenTPW;

public class Shader : Asset
{
	private ShaderInfo shaderInfo;

	public VertexElementDescription[] VertexElements => shaderInfo.Reflection.VertexElements;
	public ResourceLayoutDescription[] ResourceLayouts => shaderInfo.Reflection.ResourceLayouts;
	public Veldrid.Shader[] ShaderProgram => shaderInfo.ShaderProgram;
	public bool IsDirty { get; private set; }
	public Action OnRecompile { get; set; }

	// One hot-reload watcher per shader file, shared by every Shader compiled from it. Each
	// Material creates its own Shader, and on Linux every FileSystemWatcher holds an inotify
	// instance (128 per user by default), so per-Shader watchers crashed the lobby scene.
	private static readonly Dictionary<string, FileSystemWatcher?> watchers = new();

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
		if ( OperatingSystem.IsBrowser() )
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
		IsDirty = true;
	}

	public void Recompile()
	{
		if ( !IsFileReady( Path ) )
			return;

		shaderInfo = ShaderCompiler.CompileShader( Path );
		OnRecompile?.Invoke();
		IsDirty = false;
	}
}
