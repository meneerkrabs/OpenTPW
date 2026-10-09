using Veldrid;

namespace OpenTPW;

/// <summary>
/// Uploads decoded movie frames to a GPU texture (CPU BT.601 full-range conversion to RGBA) and draws them
/// aspect-correct (square pixels, letter/pillarboxed) with the fullscreen-triangle blit shader.
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

	public int Width { get; }
	public int Height { get; }
	/// <summary>RGBA of the last uploaded frame (CPU copy, used for readback checks).</summary>
	public ReadOnlySpan<byte> Pixels => rgba;
	public bool HasFrame { get; private set; }

	public void Upload( TqiFrame frame )
	{
		if ( frame.Width != Width || frame.Height != Height )
			throw new ArgumentException( "Movie frame size changed.", nameof( frame ) );
		frame.WriteRgba32( rgba );
		Device.UpdateTexture( texture, rgba, 0, 0, 0, (uint)Width, (uint)Height, 1, 0, 0 );
		HasFrame = true;
	}

	/// <summary>Largest centred rectangle with the movie's aspect ratio inside a target of the given size.</summary>
	public static (int X, int Y, int Width, int Height) Fit( int movieWidth, int movieHeight, int targetWidth, int targetHeight )
	{
		var scale = Math.Min( targetWidth / (double)movieWidth, targetHeight / (double)movieHeight );
		var width = Math.Max( 1, (int)Math.Round( movieWidth * scale ) );
		var height = Math.Max( 1, (int)Math.Round( movieHeight * scale ) );
		return ((targetWidth - width) / 2, (targetHeight - height) / 2, width, height);
	}

	public void Draw( CommandList commands, uint targetWidth, uint targetHeight )
	{
		if ( !HasFrame )
			return;
		var (x, y, width, height) = Fit( Width, Height, (int)targetWidth, (int)targetHeight );
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
