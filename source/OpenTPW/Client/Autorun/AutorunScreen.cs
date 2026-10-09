using System.Diagnostics;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW;

/// <summary>How the launcher ended.</summary>
internal enum AutorunResult { None, Play, Exit }

/// <summary>
/// The original CD launcher in the game window: <see cref="AutorunView"/>'s 640x480 image drawn point-sampled at the
/// largest integer scale that fits, centred on black. Play hands over to the game, View Read-me opens the CD's text file
/// with the operating system's viewer and Exit quits. [EXT:autorun] (docs/AUTORUN.md)
/// </summary>
internal sealed class AutorunScreen : IDisposable
{
	private readonly UiInputSource inputSource = new();
	private readonly Func<string, bool> openFile;
	private readonly Veldrid.Texture texture;
	private readonly TextureView view;
	private readonly ResourceLayout layout;
	private readonly ResourceSet resourceSet;
	private readonly Pipeline pipeline;
	private bool uploaded;

	/// <param name="openFile">Opens a file with the system's default application; false when that failed.</param>
	public AutorunScreen( AutorunView launcher, string? readmePath, Func<string, bool>? openFile = null )
	{
		Launcher = launcher;
		ReadmePath = readmePath;
		this.openFile = openFile ?? OpenWithSystem;
		texture = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D( AutorunView.Width, AutorunView.Height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled ) );
		this.view = Device.ResourceFactory.CreateTextureView( texture );
		layout = Device.ResourceFactory.CreateResourceLayout( new ResourceLayoutDescription(
			new ResourceLayoutElementDescription( "g_tInput", ResourceKind.TextureReadOnly, ShaderStages.Fragment ),
			new ResourceLayoutElementDescription( "g_sSampler", ResourceKind.Sampler, ShaderStages.Fragment ) ) );
		// Point sampling keeps every original pixel an exact block at integer scales.
		resourceSet = Device.ResourceFactory.CreateResourceSet( new ResourceSetDescription( layout, view, Device.PointSampler ) );
		var shader = new Shader( "content/shaders/blit.shader" );
		pipeline = Device.ResourceFactory.CreateGraphicsPipeline( new GraphicsPipelineDescription(
			BlendStateDescription.SingleOverrideBlend,
			DepthStencilStateDescription.Disabled,
			RasterizerStateDescription.CullNone,
			PrimitiveTopology.TriangleList,
			new ShaderSetDescription( Array.Empty<VertexLayoutDescription>(), shader.ShaderProgram ),
			[layout],
			global::Global.Render.MultisampledFramebuffer.OutputDescription ) );
	}

	public AutorunView Launcher { get; }
	public string? ReadmePath { get; }
	public AutorunResult Result { get; private set; }
	/// <summary>Replaces the next frame's polled input (tests and the smoke test).</summary>
	public UiInput? InjectedInput { get; set; }

	/// <summary>Where the 640x480 image is drawn in drawable pixels.</summary>
	public static (int X, int Y, int Width, int Height) Placement() => AutorunView.Fit( Screen.PixelSize.X, Screen.PixelSize.Y );

	public void Update()
	{
		if ( Result != AutorunResult.None )
			return;
		var logical = Screen.Size;
		var pixels = Screen.PixelSize;
		var input = InjectedInput ?? inputSource.Poll( pixels.X / (float)Math.Max( 1, logical.X ), pixels.Y / (float)Math.Max( 1, logical.Y ) );
		InjectedInput = null;
		var (x, y, width, height) = Placement();
		NVector2? client = null;
		if ( width > 0 && height > 0 && input.Mouse.X >= x && input.Mouse.Y >= y && input.Mouse.X < x + width && input.Mouse.Y < y + height )
			client = new NVector2( (input.Mouse.X - x) * AutorunView.Width / width, (input.Mouse.Y - y) * AutorunView.Height / height );
		switch ( Launcher.Handle( client, input.LeftPressed, input.LeftReleased, input.Pressed ) )
		{
			case AutorunAction.Play:
				Log.Trace( "Autorun: Play." );
				Result = AutorunResult.Play;
				break;
			case AutorunAction.Exit:
				Log.Trace( "Autorun: Exit." );
				Result = AutorunResult.Exit;
				GameFlow.Quit();
				break;
			case AutorunAction.Readme:
				if ( ReadmePath == null || !openFile( ReadmePath ) )
					Log.Warning( $"Autorun: the read-me {(ReadmePath == null ? "was not found" : $"'{ReadmePath}' could not be opened")}." );
				break;
		}
	}

	public void Render()
	{
		if ( Launcher.Dirty || !uploaded )
		{
			Device.UpdateTexture( texture, Launcher.Compose(), 0, 0, 0, AutorunView.Width, AutorunView.Height, 1, 0, 0 );
			uploaded = true;
		}
		var framebuffer = global::Global.Render.MultisampledFramebuffer;
		var (x, y, width, height) = AutorunView.Fit( (int)framebuffer.Width, (int)framebuffer.Height );
		var commands = global::Global.Render.CommandList;
		commands.SetViewport( 0, new Viewport( x, y, width, height, 0, 1 ) );
		commands.SetPipeline( pipeline );
		commands.SetGraphicsResourceSet( 0, resourceSet );
		commands.Draw( 3, 1, 0, 0 );
		commands.SetViewport( 0, new Viewport( 0, 0, framebuffer.Width, framebuffer.Height, 0, 1 ) );
	}

	/// <summary>The platform's default application for the file (ShellExecute, open, xdg-open).</summary>
	private static bool OpenWithSystem( string path )
	{
		try
		{
			using var process = Process.Start( new ProcessStartInfo( path ) { UseShellExecute = true } );
			return true;
		}
		catch ( Exception exception ) when ( exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException or FileNotFoundException )
		{
			Log.Warning( $"Autorun: {exception.Message}" );
			return false;
		}
	}

	public void Dispose()
	{
		pipeline.Dispose();
		resourceSet.Dispose();
		layout.Dispose();
		view.Dispose();
		texture.Dispose();
	}
}
