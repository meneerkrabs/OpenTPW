using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW.UI.Original;

/// <summary>
/// Draws <see cref="UiBatch"/> triangles: original images (linear filtered, straight alpha) and BF4
/// atlases (uploaded as white RGBA with coverage alpha, smoothed per text scale by
/// <see cref="FontSmoothing"/>, point sampled) over the scene. Several
/// batches may be drawn per frame; each uses its own vertex buffer of the frame slot
/// (<see cref="Renderer.FramesInFlight"/>), written directly rather than through the command list.
/// </summary>
internal sealed class UiRenderer : IDisposable
{
	/// <summary>The vertex size in bytes, read once: <c>Marshal.SizeOf</c> reflects over the type on every call.</summary>
	private static readonly int GpuVertexSize = Marshal.SizeOf<GpuVertex>();

	[StructLayout( LayoutKind.Sequential )]
	private readonly record struct GpuVertex( System.Numerics.Vector2 Position, System.Numerics.Vector2 TexCoords, System.Numerics.Vector4 Color );

	private static readonly VertexLayoutDescription VertexLayout = new(
		new VertexElementDescription( "vPosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2 ),
		new VertexElementDescription( "vTexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2 ),
		new VertexElementDescription( "vColor", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4 ) );

	private readonly ShaderInfo shader;
	private readonly ResourceLayout resourceLayout;
	private readonly Pipeline pipeline;
	private readonly Pipeline invertPipeline;
	private readonly Dictionary<object, (Veldrid.Texture Texture, ResourceSet Set)> textures = new();
	// Sets for textures rendered by their owners; the textures themselves are not the UI's to dispose.
	private readonly Dictionary<Veldrid.Texture, ResourceSet> renderedSets = new();
	private readonly List<DeviceBuffer>[] buffers = Enumerable.Range( 0, Renderer.FramesInFlight ).Select( _ => new List<DeviceBuffer>() ).ToArray();
	private int frameBuffers;
	private GpuVertex[] vertices = Array.Empty<GpuVertex>();
	private readonly HashSet<string> failedImages = new( StringComparer.OrdinalIgnoreCase );

	// Depth-tested images (UiDepthImage): their own shader and pipeline over the same resource layout, targets and vertex buffers per frame slot.
	[StructLayout( LayoutKind.Sequential )]
	private readonly record struct GpuDepthVertex( System.Numerics.Vector3 Position, System.Numerics.Vector2 TexCoords );
	private static readonly int GpuDepthVertexSize = Marshal.SizeOf<GpuDepthVertex>();
	private static readonly VertexLayoutDescription DepthVertexLayout = new(
		new VertexElementDescription( "vPosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3 ),
		new VertexElementDescription( "vTexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2 ) );
	private const PixelFormat DepthImageColorFormat = PixelFormat.R8_G8_B8_A8_UNorm;
	private const PixelFormat DepthImageDepthFormat = PixelFormat.D32_Float_S8_UInt;
	private ShaderInfo? depthShader;
	private Pipeline? depthPipeline;
	// A target is at least as large as the images drawn into it, in steps of DepthTargetStep pixels, and grows only: a turning preview's image changes size
	// by a few pixels a frame, so it is drawn into the target's top-left corner with a matching viewport and shown with the matching texture coordinates.
	private const int DepthTargetStep = 64;
	private sealed class DepthTarget( Veldrid.Texture color, Veldrid.Texture depth, Framebuffer framebuffer, ResourceSet set ) : IDisposable
	{
		public Veldrid.Texture Color { get; } = color;
		public bool Fits( int width, int height ) => Color.Width >= (uint)width && Color.Height >= (uint)height;
		public Framebuffer Framebuffer { get; } = framebuffer;
		public ResourceSet Set { get; } = set;
		public void Dispose()
		{
			Set.Dispose();
			Framebuffer.Dispose();
			depth.Dispose();
			Color.Dispose();
		}
	}
	private readonly List<DepthTarget>[] depthTargets = Enumerable.Range( 0, Renderer.FramesInFlight ).Select( _ => new List<DepthTarget>() ).ToArray();
	private readonly List<DeviceBuffer>[] depthBuffers = Enumerable.Range( 0, Renderer.FramesInFlight ).Select( _ => new List<DeviceBuffer>() ).ToArray();
	private readonly Dictionary<UiDepthImage, DepthTarget> preparedImages = new( ReferenceEqualityComparer.Instance );
	private GpuDepthVertex[] depthVertices = Array.Empty<GpuDepthVertex>();

	public UiRenderer( OutputDescription outputs )
	{
		shader = ShaderCompiler.CompileShader( Path.GetFullPath( "content/shaders/ui-batch.shader", AppContext.BaseDirectory ) );
		resourceLayout = Device.ResourceFactory.CreateResourceLayout( shader.Reflection.ResourceLayouts.Single() );
		TexturePackSwitch.PackChanged += ReloadImages;
		LanguageInterfaceArchive.Changed += ReloadImages;
		pipeline = Device.ResourceFactory.CreateGraphicsPipeline( new GraphicsPipelineDescription(
			BlendStateDescription.SingleAlphaBlend,
			DepthStencilStateDescription.Disabled,
			RasterizerStateDescription.CullNone,
			PrimitiveTopology.TriangleList,
			new ShaderSetDescription( [VertexLayout], shader.ShaderProgram ),
			[resourceLayout],
			outputs,
			ResourceBindingModel.Improved ) );
		// [BIN:TPW-EXE:0x57ed75 lightning flash blend] the flash quad's state 0x9A1 sets the source blend to INVDESTCOLOR (0xA, 0x5679B0 high nibble) and the destination blend to ZERO (low nibble 1), so a white quad turns the screen into its negative
		invertPipeline = Device.ResourceFactory.CreateGraphicsPipeline( new GraphicsPipelineDescription(
			new BlendStateDescription( RgbaFloat.Clear, new BlendAttachmentDescription( true, BlendFactor.InverseDestinationColor, BlendFactor.Zero, BlendFunction.Add, BlendFactor.One, BlendFactor.Zero, BlendFunction.Add ) ),
			DepthStencilStateDescription.Disabled,
			RasterizerStateDescription.CullNone,
			PrimitiveTopology.TriangleList,
			new ShaderSetDescription( [VertexLayout], shader.ShaderProgram ),
			[resourceLayout],
			outputs,
			ResourceBindingModel.Improved ) );
	}

	/// <summary>Call once per frame before the first <see cref="Draw"/>.</summary>
	public void BeginFrame()
	{
		frameBuffers = 0;
		preparedImages.Clear();
	}

	/// <summary>
	/// Draws the batch's depth-tested images (<see cref="UiBatch.DepthImages"/>) into their targets, for <see cref="Draw"/> to show; then sets
	/// <paramref name="restore"/> again with full viewports. Call it after the batch is built and before it is drawn, outside the batch's own pass.
	/// </summary>
	public void PrepareDepthImages( CommandList commands, UiBatch batch, Framebuffer restore )
	{
		if ( batch.DepthImages.Count == 0 )
			return;
		var slot = Renderer.SlotOf( Render.FrameNumber );
		var targets = depthTargets[slot];
		var buffers = depthBuffers[slot];
		var used = 0;
		var bufferIndex = 0;
		foreach ( var image in batch.DepthImages )
		{
			if ( preparedImages.ContainsKey( image ) )
				continue;
			if ( used == targets.Count )
				targets.Add( CreateDepthTarget( Step( image.Width ), Step( image.Height ) ) );
			else if ( !targets[used].Fits( image.Width, image.Height ) )
			{
				var old = targets[used];
				Render.ScheduleDelete( old.Dispose );
				targets[used] = CreateDepthTarget( Math.Max( (int)old.Color.Width, Step( image.Width ) ), Math.Max( (int)old.Color.Height, Step( image.Height ) ) );
			}
			var target = targets[used++];
			preparedImages[image] = target;
			commands.SetFramebuffer( target.Framebuffer );
			commands.ClearColorTarget( 0, RgbaFloat.Clear );
			commands.ClearDepthStencil( 1 );
			// the image's pixels are the target's top-left corner
			commands.SetViewport( 0, new Viewport( 0, 0, image.Width, image.Height, 0, 1 ) );
			commands.SetFullScissorRects();
			var count = 0;
			for ( var index = 0; index < image.Draws.Count; index++ )
				count += image.Draws[index].Vertices.Count;
			if ( count == 0 )
				continue;
			if ( depthVertices.Length < count )
				depthVertices = new GpuDepthVertex[count];
			var offset = 0;
			for ( var drawIndex = 0; drawIndex < image.Draws.Count; drawIndex++ )
			{
				var drawVertices = image.Draws[drawIndex].Vertices;
				for ( var index = 0; index < drawVertices.Count; index++ )
				{
					var vertex = drawVertices[index];
					var position = new System.Numerics.Vector3( vertex.Position.X * 2f / image.Width - 1, 1 - vertex.Position.Y * 2f / image.Height, vertex.Position.Z );
					depthVertices[offset++] = new GpuDepthVertex( position, vertex.TexCoords );
				}
			}
			var bytes = (uint)(count * GpuDepthVertexSize);
			if ( bufferIndex == buffers.Count )
				buffers.Add( CreateBuffer( bytes ) );
			else if ( buffers[bufferIndex].SizeInBytes < bytes )
			{
				var old = buffers[bufferIndex];
				Render.ScheduleDelete( old.Dispose );
				buffers[bufferIndex] = CreateBuffer( bytes );
			}
			var buffer = buffers[bufferIndex++];
			Device.UpdateBuffer( buffer, 0, ref depthVertices[0], bytes );
			commands.SetPipeline( DepthPipeline( target.Framebuffer.OutputDescription ) );
			commands.SetVertexBuffer( 0, buffer );
			var start = 0u;
			for ( var drawIndex = 0; drawIndex < image.Draws.Count; drawIndex++ )
			{
				var draw = image.Draws[drawIndex];
				var set = GetResourceSet( draw.Texture );
				if ( set != null )
				{
					commands.SetGraphicsResourceSet( 0, set );
					commands.Draw( (uint)draw.Vertices.Count, 1, start, 0 );
				}
				start += (uint)draw.Vertices.Count;
			}
		}
		commands.SetFramebuffer( restore );
		commands.SetFullViewports();
		commands.SetFullScissorRects();
	}

	private static int Step( int pixels ) => Math.Max( 1, (pixels + DepthTargetStep - 1) / DepthTargetStep ) * DepthTargetStep;

	private DepthTarget CreateDepthTarget( int width, int height )
	{
		var factory = Device.ResourceFactory;
		var color = factory.CreateTexture( TextureDescription.Texture2D( (uint)width, (uint)height, 1, 1, DepthImageColorFormat, TextureUsage.RenderTarget | TextureUsage.Sampled ) );
		var depth = factory.CreateTexture( TextureDescription.Texture2D( (uint)width, (uint)height, 1, 1, DepthImageDepthFormat, TextureUsage.DepthStencil ) );
		var framebuffer = factory.CreateFramebuffer( new FramebufferDescription( depth, color ) );
		// one target pixel per output pixel: point sampled
		var set = factory.CreateResourceSet( new ResourceSetDescription( resourceLayout, color, Device.PointSampler ) );
		return new DepthTarget( color, depth, framebuffer, set );
	}

	private Pipeline DepthPipeline( OutputDescription outputs )
	{
		if ( depthPipeline != null )
			return depthPipeline;
		var shaderInfo = ShaderCompiler.CompileShader( Path.GetFullPath( "content/shaders/ui-depth.shader", AppContext.BaseDirectory ) );
		depthShader = shaderInfo;
		// [BIN:TPW-EXE:0x567620 state word] the draw's state word turns the depth test (ZENABLE, bit 0x8000 clear) and depth writes (ZWRITEENABLE, bit 0x800
		// clear) on; the device init 0x573080 sets ZFUNC to D3DCMP_LESSEQUAL (4) with ZENABLE (0x57360f), and the cull mode is D3DCULL_NONE (0x566957)
		depthPipeline = Device.ResourceFactory.CreateGraphicsPipeline( new GraphicsPipelineDescription(
			BlendStateDescription.SingleDisabled,
			new DepthStencilStateDescription( true, true, ComparisonKind.LessEqual ),
			RasterizerStateDescription.CullNone,
			PrimitiveTopology.TriangleList,
			new ShaderSetDescription( [DepthVertexLayout], shaderInfo.ShaderProgram ),
			[resourceLayout],
			outputs,
			ResourceBindingModel.Improved ) );
		return depthPipeline;
	}

	/// <param name="invert">Draws with the lightning flash's blend: the destination colour inverted by the batch's (white) coverage.</param>
	public void Draw( CommandList commands, UiBatch batch, uint framebufferWidth, uint framebufferHeight, bool invert = false )
	{
		var count = 0;
		foreach ( var draw in batch.Draws )
			count += draw.Vertices.Count;
		if ( count == 0 )
			return;
		if ( vertices.Length < count )
			vertices = new GpuVertex[count];
		var offset = 0;
		foreach ( var draw in batch.Draws )
		{
			// a depth-tested image fills the top-left corner of its target
			var uvScale = draw.Texture.DepthImage is { } depthImage && preparedImages.TryGetValue( depthImage, out var depthTarget )
				? new System.Numerics.Vector2( (float)depthImage.Width / depthTarget.Color.Width, (float)depthImage.Height / depthTarget.Color.Height )
				: System.Numerics.Vector2.One;
			foreach ( var vertex in draw.Vertices )
			{
				var position = new System.Numerics.Vector2( vertex.Position.X * 2f / framebufferWidth - 1, 1 - vertex.Position.Y * 2f / framebufferHeight );
				var color = new System.Numerics.Vector4( vertex.Color.R, vertex.Color.G, vertex.Color.B, vertex.Color.A ) / 255f;
				vertices[offset++] = new GpuVertex( position, vertex.TexCoords * uvScale, color );
			}
		}
		var bytes = (uint)(count * GpuVertexSize);
		// Written straight into GPU memory: an update recorded into the command list would end the render pass on Metal.
		var slot = buffers[Renderer.SlotOf( Render.FrameNumber )];
		if ( frameBuffers == slot.Count )
			slot.Add( CreateBuffer( bytes ) );
		else if ( slot[frameBuffers].SizeInBytes < bytes )
		{
			var old = slot[frameBuffers];
			Render.ScheduleDelete( old.Dispose );
			slot[frameBuffers] = CreateBuffer( bytes );
		}
		var buffer = slot[frameBuffers++];
		Device.UpdateBuffer( buffer, 0, ref vertices[0], bytes );
		commands.SetPipeline( invert ? invertPipeline : pipeline );
		commands.SetVertexBuffer( 0, buffer );
		var start = 0u;
		foreach ( var draw in batch.Draws )
		{
			var set = GetResourceSet( draw.Texture );
			if ( set != null )
			{
				commands.SetGraphicsResourceSet( 0, set );
				commands.Draw( (uint)draw.Vertices.Count, 1, start, 0 );
			}
			start += (uint)draw.Vertices.Count;
		}
	}

	private static DeviceBuffer CreateBuffer( uint bytes ) =>
		Device.ResourceFactory.CreateBuffer( new BufferDescription( Math.Max( bytes * 2, 16384u ), BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );

	internal void PrepareImage( string path ) => GetResourceSet( UiTexture.Image( path ) );

	/// <summary>
	/// Makes the GPU copy of an interface image or font atlas now, as its first draw would (a decoded, keyed image or a smoothed atlas,
	/// uploaded once and kept): a park built over frames prepares its HUD's textures this way before it is entered. Other textures
	/// (solid, rendered, depth images) are left alone.
	/// </summary>
	internal void Prepare( UiTexture texture )
	{
		if ( texture.ImagePath != null || texture.Atlas != null )
			GetResourceSet( texture );
	}

	private ResourceSet? GetResourceSet( UiTexture texture )
	{
		if ( texture.DepthImage is { } depthImage )
			return preparedImages.TryGetValue( depthImage, out var prepared ) ? prepared.Set : null;
		if ( texture.Rendered is { } rendered )
		{
			if ( renderedSets.TryGetValue( rendered, out var renderedSet ) )
				return renderedSet;
			// A new texture usually replaces one its owner has just scheduled for disposal.
			foreach ( var stale in renderedSets.Where( pair => pair.Key.IsDisposed ).ToList() )
			{
				renderedSets.Remove( stale.Key );
				stale.Value.Dispose();
			}
			using ( FrameMarks.Measure( "ui.renderedSet" ) )
				renderedSet = Device.ResourceFactory.CreateResourceSet( new ResourceSetDescription( resourceLayout, rendered, Device.LinearSampler ) );
			renderedSets[rendered] = renderedSet;
			return renderedSet;
		}
		var factor = texture.Atlas != null ? FontSmoothing.Factor( texture.Atlas, texture.FontScale ) : 1;
		object key = texture.Atlas != null ? (texture.Atlas, factor) : texture.ImagePath ?? "solid";
		if ( textures.TryGetValue( key, out var entry ) )
			return entry.Set;
		using var mark = !FrameMarks.Enabled ? default
			: FrameMarks.Measure( texture.Atlas != null ? "ui.atlas" : texture.ImagePath != null ? "ui.image " + texture.ImagePath : "ui.solid" );
		if ( texture.Atlas != null )
		{
			var atlas = texture.Atlas;
			var coverage = FontSmoothing.Coverage( atlas, factor );
			var rgba = new byte[coverage.Length * 4];
			for ( var index = 0; index < coverage.Length; index++ )
			{
				rgba[index * 4] = 255;
				rgba[index * 4 + 1] = 255;
				rgba[index * 4 + 2] = 255;
				rgba[index * 4 + 3] = coverage[index];
			}
			// One texel per output pixel when the factor matches the scale; a capped factor is filtered.
			entry = Create( rgba, atlas.Width * factor, atlas.Height * factor, factor == texture.FontScale ? Device.PointSampler : Device.LinearSampler );
		}
		else if ( texture.ImagePath != null )
		{
			if ( failedImages.Contains( texture.ImagePath ) )
				return null;
			try
			{
				var (width, height, rgba) = UiImages.Load( texture.ImagePath );
				entry = Create( rgba, width, height, Device.LinearSampler );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or ArgumentException or InvalidOperationException )
			{
				failedImages.Add( texture.ImagePath );
				Log?.Warning( $"UI image {texture.ImagePath} could not be loaded: {exception.Message}" );
				return null;
			}
		}
		else
			entry = Create( [255, 255, 255, 255], 1, 1, Device.PointSampler );
		textures[key] = entry;
		return entry.Set;
	}

	private (Veldrid.Texture, ResourceSet) Create( byte[] rgba, int width, int height, Sampler sampler )
	{
		var texture = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D( (uint)width, (uint)height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled ) );
		Device.UpdateTexture( texture, rgba, 0, 0, 0, (uint)width, (uint)height, 1, 0, 0 );
		var resources = shader.Reflection.ResourceLayouts.Single().Elements.Select( element => element.Name switch
		{
			"Image" => (BindableResource)texture,
			"s_Image" => sampler,
			_ => throw new InvalidOperationException( $"Unexpected UI shader resource {element.Name}." )
		} ).ToArray();
		return (texture, Device.ResourceFactory.CreateResourceSet( new ResourceSetDescription( resourceLayout, resources ) ));
	}

	/// <summary>
	/// [EXT:texture-pack] The texture pack changed: forgets the cached interface images (they are decoded again on first use,
	/// from the new pack) and deletes their GPU copies after the current frame.
	/// </summary>
	private void ReloadImages()
	{
		ReloadImages( null );
	}

	/// <summary>Invalidates only UI images in a newly available download batch.</summary>
	private void ReloadImages( IReadOnlyList<string>? paths )
	{
		var keys = paths?.Select( TexturePack.RelativeFileName ).ToHashSet( StringComparer.Ordinal );
		bool Matches( string path ) => keys == null || keys.Contains( TexturePack.RelativeFileName( path ) );
		var old = textures.Where( entry => entry.Key is string path && Matches( path ) ).ToList();
		foreach ( var entry in old )
			textures.Remove( entry.Key );
		failedImages.RemoveWhere( Matches );
		Render.ScheduleDelete( () =>
		{
			foreach ( var (_, (texture, set)) in old )
			{
				set.Dispose();
				texture.Dispose();
			}
		} );
	}

	private bool disposed;

	public void Dispose()
	{
		// the shader references are given back once (ShaderCompiler.Release)
		if ( disposed )
			return;
		disposed = true;
		TexturePackSwitch.PackChanged -= ReloadImages;
		LanguageInterfaceArchive.Changed -= ReloadImages;
		foreach ( var set in renderedSets.Values )
			set.Dispose();
		renderedSets.Clear();
		foreach ( var (texture, set) in textures.Values )
		{
			set.Dispose();
			texture.Dispose();
		}
		textures.Clear();
		foreach ( var slot in buffers.Concat( depthBuffers ) )
		{
			foreach ( var buffer in slot )
				buffer.Dispose();
			slot.Clear();
		}
		foreach ( var slot in depthTargets )
		{
			foreach ( var target in slot )
				target.Dispose();
			slot.Clear();
		}
		preparedImages.Clear();
		depthPipeline?.Dispose();
		// the native shaders are shared (ShaderCompiler.CompileShader): this renderer gives its references back
		if ( depthShader is { } depthShaderInfo )
			ShaderCompiler.Release( depthShaderInfo );
		pipeline.Dispose();
		resourceLayout.Dispose();
		ShaderCompiler.Release( shader );
	}
}
