using ImGuiNET;
using OpenTPW.ModKit;
using System.Reflection;
using Veldrid;

namespace OpenTPW;
public partial class Editor
{
	public static Editor Instance { get; private set; }

	public ImGuiOverlayRenderer imguiRenderer { get; private set; }
	public GraphicsDevice graphicsDevice { get; private set; }

	private List<BaseTab> tabs = new();

	public bool shouldRender = false;

	public static ImFontPtr MonospaceFont { get; private set; }
	public static ImFontPtr SansSerifFont { get; private set; }

	public Editor( ImGuiOverlayRenderer imguiRenderer, GraphicsDevice graphicsDevice )
	{
		Instance ??= this;

		this.imguiRenderer = imguiRenderer;
		this.graphicsDevice = graphicsDevice;

		InitIO();

		tabs.AddRange( new BaseTab[] {
			new DemoWindow(),
			new FileBrowser()
		} );

		tabs.ForEach( x => x.ImGuiRenderer = this.imguiRenderer );
	}

	private void InitIO()
	{
		var io = ImGui.GetIO();

		io.ConfigFlags |= ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.IsSRGB;
		ImGui.LoadIniSettingsFromDisk( ImGui.GetIO().IniFilename ); // https://github.com/mellinoe/veldrid/issues/410

		// io.Fonts.Clear();
		// io.Fonts.AddFontFromFileTTF( "C:\\Windows\\Fonts\\segoeui.ttf", 16f );
		// io.Fonts.Build();
		// imguiRenderer.RecreateFontDeviceTexture( graphicsDevice );

		// the key map is the overlay renderer's (ImGuiOverlayRenderer.SetOpenTKKeyMappings, filled when it made this ImGui context)
	}

	private void DrawMenuBar()
	{
		ImGui.BeginMainMenuBar();

		foreach ( var tab in tabs )
		{
			var editorMenuAttribute = tab.GetType().GetCustomAttribute<EditorMenuAttribute>();
			if ( editorMenuAttribute == null )
				continue;

			var splitPath = editorMenuAttribute.Path.Split( '/' );

			if ( ImGui.BeginMenu( splitPath[0] ) )
			{
				for ( int i = 1; i < splitPath.Length; i++ )
				{
					string? item = splitPath[i];
					bool active = ImGui.MenuItem( item );

					if ( i == splitPath.Length - 1 && active )
						tab.visible = !tab.visible;
				}

				ImGui.EndMenu();
			}
		}

		ImGui.EndMainMenuBar();
	}

	public void UpdateFrom( InputSnapshot inputSnapshot )
	{
		imguiRenderer.Update( 1 / 60f, inputSnapshot );

		DrawMenuBar();

		ImGui.DockSpaceOverViewport( ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode );

		tabs.ForEach( tab =>
		{
			if ( tab.visible )
				tab.Draw();
		} );
	}

	public async Task<string> CacheThumbnailAsync( string path )
	{
		if ( !path.EndsWith( ".wct" ) )
			return "";

		var thumbnailPath = FileSystem.GetRelativePath( path );
		thumbnailPath = Path.ChangeExtension( thumbnailPath, ".png" );
		thumbnailPath = "/thumbcache" + thumbnailPath;

		await Task.Run( () =>
		{
			var textureFile = new TextureFile( path );
			var t = textureFile.Data;

			var thumbnail = RgbaResampler.Resize( t.Data.AsSpan( 0, t.Width * t.Height * 4 ), t.Width, t.Height, 16, 16 );
			using var writeStream = CacheFileSystem.OpenWrite( thumbnailPath );
			PngWriter.Write( writeStream, thumbnail, 16, 16 );

			Log.Info( $"Cached to {thumbnailPath}" );
		} );

		return thumbnailPath;
	}

	public void Render( CommandList commandList ) 
	{
		imguiRenderer.Render( graphicsDevice, commandList );
	}
}

