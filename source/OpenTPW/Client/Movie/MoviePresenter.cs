using Veldrid;

namespace OpenTPW;

/// <summary>
/// Uploads decoded movie frames to a GPU texture (the original player's colour tables on the CPU, <see cref="TqiColour"/>, at full 8-bit colour) and draws them
/// at the original's display aspect (letter/pillarboxed) with the fullscreen-triangle blit shader.
/// </summary>
internal sealed class MoviePresenter : IDisposable
{
	private readonly Veldrid.Texture texture;
	private readonly TextureView view;
	private readonly ResourceLayout layout;
	private readonly ResourceSet resourceSet;
	private readonly Pipeline pipeline;
	private readonly byte[] rgba;

	public MoviePresenter( int width, int height, OutputDescription output )
	{
		Width = width;
		Height = height;
		rgba = new byte[width * height * 4];
		texture = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D(
			(uint)width, (uint)height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled ) );
		view = Device.ResourceFactory.CreateTextureView( texture );
		layout = Device.ResourceFactory.CreateResourceLayout( new ResourceLayoutDescription(
			new ResourceLayoutElementDescription( "g_tInput", ResourceKind.TextureReadOnly, ShaderStages.Fragment ),
			new ResourceLayoutElementDescription( "g_sSampler", ResourceKind.Sampler, ShaderStages.Fragment ) ) );
		resourceSet = Device.ResourceFactory.CreateResourceSet( new ResourceSetDescription( layout, view, Device.LinearSampler ) );
		var shader = new Shader( "content/shaders/blit.shader" );
		pipeline = Device.ResourceFactory.CreateGraphicsPipeline( new GraphicsPipelineDescription(
			BlendStateDescription.SingleOverrideBlend,
			DepthStencilStateDescription.Disabled,
			RasterizerStateDescription.CullNone,
			PrimitiveTopology.TriangleList,
			new ShaderSetDescription( Array.Empty<VertexLayoutDescription>(), shader.ShaderProgram ),
			[layout],
			output ) );
	}

	/// <summary>
	/// Width : height of the displayed picture. The original draws every movie into a rectangle of the screen width
	/// by 352/480 of its height, which is 640 x 352 on a 640 x 480 screen; the 320 x 352 TGQ frames are therefore
	/// shown at twice their width, with non-square pixels ([BIN:TPW-EXE:0x51b010 movie box], docs/TGQ-MOVIES.md; the Mac twin is 0x1009B1DC).
	/// </summary>
	// [EXT:movie-letterbox] the original ran fixed 4:3 modes; OpenTPW runs any window size and letterboxes the 640:352 picture inside it
	// instead of stretching it to the window width (design decision).
	public const double OriginalDisplayAspect = 640d / 352;

	/// <summary>
	/// The rectangle the PC player puts a movie into on a screen of the given size: the full width, a height of 352/480 of the
	/// screen and a top edge at 64/480 of it (integer divisions), so 640 x 352 at y 64 on a 640 x 480 screen.
	/// </summary>
	// [BIN:TPW-EXE:0x54df20 movie box] the logo and trailer callers pass y = screen height * 64 / 480 (0x88888889 multiply, shift 8) and x = 0 to the movie start 0x51b010, which sizes the picture as the screen width (0x7a1968) by height * 0x160 / 0x1E0 (0x51b0ed, 0x51b0fc); the screen sizes are the eight modes of 0x449f38
	public static (int X, int Y, int Width, int Height) OriginalBox( int screenWidth, int screenHeight ) =>
		(0, (int)((uint)screenHeight * 64 / 480), screenWidth, (int)((uint)screenHeight * 0x160 / 0x1E0));

	public int Width { get; }
	public int Height { get; }
	/// <summary>Displayed width : height; 0 keeps the movie's own (square-pixel) aspect.</summary>
	public double DisplayAspect { get; init; } = OriginalDisplayAspect;
	/// <summary>RGBA of the last uploaded frame (CPU copy, used for readback checks).</summary>
	public ReadOnlySpan<byte> Pixels => rgba;
	public bool HasFrame { get; private set; }
	/// <summary>
	/// The display packing the frames go through before they are shown. Null (the default) presents the colour tables' full 8-bit
	/// values; a format reproduces the original player's 16-bit packing (<see cref="OriginalDisplayFormat"/>) for comparison and tests.
	/// </summary>
	// [EXT:movie-full-color] the original packs every movie pixel to its 16-bit display surface (tables 0x670350, row writer 0x675D90); OpenTPW presents full colour, which also suits upscaled movies (user-approved 2026-10-17).
	public TqiDisplayFormat? DisplayFormat { get; init; }

	/// <summary>
	/// The packing the original applies for the RENDER32 option of the detail settings: 0 (the Low, Medium and High presets) is a
	/// 16-bit display, 1 a 32-bit display whose writers (0x675767 and 0x67538B, the same tables with 8-bit fields) keep every channel value.
	/// </summary>
	// [BIN:TPW-EXE:0x44E080 display depth] the window setup passes 16 + 16 * (start-up flag 0x200000) as the DirectDraw display depth (0x44DF20 to SetDisplayMode, 0x563460); the option routine 0x44D8F0 sets that flag from RENDER32 (0x7858A8, between MIPMAP and TEXTURE32 in the key table at 0x7445F0); the colour mode of 0x670C20 picks the 16-bit writers (0x671800, 0x671940) for a surface of at most 16 bits and the 32-bit writers (0x6718A0) otherwise; with 8-bit fields the table builder 0x670350 stores the value unchanged in the low word and builds no high word
	public static TqiDisplayFormat? OriginalDisplayFormat( int render32 ) => render32 == 0 ? TqiDisplayFormat.Rgb565 : null;

	/// <summary>The RGBA the presenter uploads for a frame: full colour for a null format, else the packed display values.</summary>
	internal static void ToRgba( TqiFrame frame, TqiDisplayFormat? format, Span<byte> rgba )
	{
		if ( format is { } packing )
			frame.WriteDisplayRgba32( rgba, packing );
		else
			frame.WriteRgba32( rgba );
	}

	public void Upload( TqiFrame frame )
	{
		if ( frame.Width != Width || frame.Height != Height )
			throw new ArgumentException( "Movie frame size changed.", nameof( frame ) );
		ToRgba( frame, DisplayFormat, rgba );
		Device.UpdateTexture( texture, rgba, 0, 0, 0, (uint)Width, (uint)Height, 1, 0, 0 );
		HasFrame = true;
	}

	/// <summary>
	/// Largest centred rectangle with the given display aspect (width : height; 0 = the movie's own) inside a
	/// target of the given size.
	/// </summary>
	public static (int X, int Y, int Width, int Height) Fit( int movieWidth, int movieHeight, int targetWidth, int targetHeight, double displayAspect = 0 )
	{
		var aspect = displayAspect > 0 ? displayAspect : movieWidth / (double)movieHeight;
		var scale = Math.Min( targetWidth / aspect, targetHeight );
		var width = Math.Max( 1, (int)Math.Round( scale * aspect ) );
		var height = Math.Max( 1, (int)Math.Round( scale ) );
		return ((targetWidth - width) / 2, (targetHeight - height) / 2, width, height);
	}

	public void Draw( CommandList commands, uint targetWidth, uint targetHeight )
	{
		if ( !HasFrame )
			return;
		var (x, y, width, height) = Fit( Width, Height, (int)targetWidth, (int)targetHeight, DisplayAspect );
		commands.SetViewport( 0, new Viewport( x, y, width, height, 0, 1 ) );
		commands.SetPipeline( pipeline );
		commands.SetGraphicsResourceSet( 0, resourceSet );
		commands.Draw( 3, 1, 0, 0 );
		commands.SetViewport( 0, new Viewport( 0, 0, targetWidth, targetHeight, 0, 1 ) );
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
