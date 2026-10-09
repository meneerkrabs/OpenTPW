using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW.UI.Original;

/// <summary>
/// Draws <see cref="UiBatch"/> triangles: original images (linear filtered, straight alpha) and BF4
/// atlases (uploaded as white RGBA with coverage alpha, point sampled) over the scene. Several
/// batches may be drawn per frame; each uses its own vertex buffer from a per-frame ring.
/// </summary>
internal sealed class UiRenderer : IDisposable
{
	[StructLayout( LayoutKind.Sequential )]
	private readonly record struct GpuVertex( System.Numerics.Vector2 Position, System.Numerics.Vector2 TexCoords, System.Numerics.Vector4 Color );

	private static readonly VertexLayoutDescription VertexLayout = new(
		new VertexElementDescription( "vPosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2 ),
		new VertexElementDescription( "vTexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2 ),
		new VertexElementDescription( "vColor", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4 ) );

	private readonly ShaderInfo shader;
	private readonly ResourceLayout resourceLayout;
	private readonly Pipeline pipeline;
	private readonly Dictionary<object, (Veldrid.Texture Texture, ResourceSet Set)> textures = new();
	private readonly List<DeviceBuffer> buffers = new();
	private int frameBuffers;
	private GpuVertex[] vertices = Array.Empty<GpuVertex>();
	private readonly HashSet<string> failedImages = new( StringComparer.OrdinalIgnoreCase );

	public UiRenderer( OutputDescription outputs )
	{
		shader = ShaderCompiler.CompileShader( Path.GetFullPath( "content/shaders/ui-batch.shader", AppContext.BaseDirectory ) );
		resourceLayout = Device.ResourceFactory.CreateResourceLayout( shader.Reflection.ResourceLayouts.Single() );
		pipeline = Device.ResourceFactory.CreateGraphicsPipeline( new GraphicsPipelineDescription(
			BlendStateDescription.SingleAlphaBlend,
			DepthStencilStateDescription.Disabled,
			RasterizerStateDescription.CullNone,
			PrimitiveTopology.TriangleList,
			new ShaderSetDescription( [VertexLayout], shader.ShaderProgram ),
			[resourceLayout],
			outputs,
			ResourceBindingModel.Improved ) );
	}

	/// <summary>Call once per frame before the first <see cref="Draw"/>.</summary>
	public void BeginFrame() => frameBuffers = 0;

	public void Draw( CommandList commands, UiBatch batch, uint framebufferWidth, uint framebufferHeight )
	{
		var count = batch.Draws.Sum( draw => draw.Vertices.Count );
		if ( count == 0 )
			return;
		if ( vertices.Length < count )
			vertices = new GpuVertex[count];
		var offset = 0;
		foreach ( var draw in batch.Draws )
		{
			foreach ( var vertex in draw.Vertices )
			{
				var position = new System.Numerics.Vector2( vertex.Position.X * 2f / framebufferWidth - 1, 1 - vertex.Position.Y * 2f / framebufferHeight );
				var color = new System.Numerics.Vector4( vertex.Color.R, vertex.Color.G, vertex.Color.B, vertex.Color.A ) / 255f;
				vertices[offset++] = new GpuVertex( position, vertex.TexCoords, color );
			}
		}
		var bytes = (uint)(count * Marshal.SizeOf<GpuVertex>());
		if ( frameBuffers == buffers.Count )
			buffers.Add( CreateBuffer( bytes ) );
		else if ( buffers[frameBuffers].SizeInBytes < bytes )
		{
			var old = buffers[frameBuffers];
			Render.ScheduleDelete( old.Dispose );
			buffers[frameBuffers] = CreateBuffer( bytes );
		}
		var buffer = buffers[frameBuffers++];
		commands.UpdateBuffer( buffer, 0, ref vertices[0], bytes );
		commands.SetPipeline( pipeline );
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

	private ResourceSet? GetResourceSet( UiTexture texture )
	{
		object key = (object?)texture.Atlas ?? texture.ImagePath ?? "solid";
		if ( textures.TryGetValue( key, out var entry ) )
			return entry.Set;
		if ( texture.Atlas != null )
		{
			var atlas = texture.Atlas;
			var rgba = new byte[atlas.Width * atlas.Height * 4];
			for ( var index = 0; index < atlas.Alpha.Length; index++ )
			{
				rgba[index * 4] = 255;
				rgba[index * 4 + 1] = 255;
				rgba[index * 4 + 2] = 255;
				rgba[index * 4 + 3] = atlas.Alpha[index];
			}
			entry = Create( rgba, atlas.Width, atlas.Height, Device.PointSampler );
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

	public void Dispose()
	{
		foreach ( var (texture, set) in textures.Values )
		{
			set.Dispose();
			texture.Dispose();
		}
		textures.Clear();
		foreach ( var buffer in buffers )
			buffer.Dispose();
		buffers.Clear();
		pipeline.Dispose();
		resourceLayout.Dispose();
		shader.VertexShader.Dispose();
		shader.FragmentShader.Dispose();
	}
}
