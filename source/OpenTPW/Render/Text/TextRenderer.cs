using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// Draws <see cref="TextBatch"/> quads with BF4 atlases uploaded as R8 textures, point sampled
/// one texel per (integer-scaled) block of framebuffer pixels and alpha blended. Solid rectangles sample a 1×1 opaque texture.
/// </summary>
internal sealed class TextRenderer : IDisposable
{
	[StructLayout( LayoutKind.Sequential )]
	private readonly record struct TextVertex( System.Numerics.Vector2 Position, System.Numerics.Vector2 TexCoords, System.Numerics.Vector4 Color );

	private static readonly VertexLayoutDescription VertexLayout = new(
		new VertexElementDescription( "vPosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2 ),
		new VertexElementDescription( "vTexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2 ),
		new VertexElementDescription( "vColor", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4 ) );

	private readonly ShaderInfo shader;
	private readonly ResourceLayout resourceLayout;
	private readonly Pipeline pipeline;
	private readonly Dictionary<FontAtlas, (Veldrid.Texture Texture, ResourceSet Set)> atlases = new();
	private readonly (Veldrid.Texture Texture, ResourceSet Set) solid;
	private DeviceBuffer? vertexBuffer;
	private TextVertex[] vertices = Array.Empty<TextVertex>();

	public TextRenderer( OutputDescription outputs )
	{
		shader = ShaderCompiler.CompileShader( Path.GetFullPath( "content/shaders/text.shader", AppContext.BaseDirectory ) );
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
		solid = CreateTexture( [255], 1, 1 );
	}

	/// <summary>
	/// Records draws into <paramref name="commands"/> for a framebuffer of the given pixel size.
	/// </summary>
	public void Draw( CommandList commands, TextBatch batch, uint framebufferWidth, uint framebufferHeight )
	{
		var quads = batch.Quads;
		if ( quads.Count == 0 )
			return;
		var vertexCount = quads.Count * 6;
		if ( vertices.Length < vertexCount )
			vertices = new TextVertex[vertexCount];
		for ( var index = 0; index < quads.Count; index++ )
			WriteQuad( quads[index], framebufferWidth, framebufferHeight, index * 6 );

		var bytes = (uint)(vertexCount * Marshal.SizeOf<TextVertex>());
		if ( vertexBuffer == null || vertexBuffer.SizeInBytes < bytes )
		{
			vertexBuffer?.Dispose();
			vertexBuffer = Device.ResourceFactory.CreateBuffer( new BufferDescription( Math.Max( bytes * 2, 4096u ), BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		}
		commands.UpdateBuffer( vertexBuffer, 0, ref vertices[0], bytes );
		commands.SetPipeline( pipeline );
		commands.SetVertexBuffer( 0, vertexBuffer );

		var start = 0;
		while ( start < quads.Count )
		{
			var end = start + 1;
			while ( end < quads.Count && quads[end].Atlas == quads[start].Atlas )
				end++;
			commands.SetGraphicsResourceSet( 0, GetResourceSet( quads[start].Atlas ) );
			commands.Draw( (uint)((end - start) * 6), 1, (uint)(start * 6), 0 );
			start = end;
		}
	}

	private void WriteQuad( TextQuad quad, uint framebufferWidth, uint framebufferHeight, int offset )
	{
		float textureWidth = quad.Atlas?.Width ?? 1;
		float textureHeight = quad.Atlas?.Height ?? 1;
		var left = quad.X * 2f / framebufferWidth - 1;
		var right = (quad.X + quad.PixelWidth) * 2f / framebufferWidth - 1;
		var top = 1 - quad.Y * 2f / framebufferHeight;
		var bottom = 1 - (quad.Y + quad.PixelHeight) * 2f / framebufferHeight;
		var u0 = quad.AtlasX / textureWidth;
		var v0 = quad.AtlasY / textureHeight;
		var u1 = quad.Atlas == null ? 1 : (quad.AtlasX + quad.Width) / textureWidth;
		var v1 = quad.Atlas == null ? 1 : (quad.AtlasY + quad.Height) / textureHeight;
		var color = new System.Numerics.Vector4( quad.Color.R, quad.Color.G, quad.Color.B, quad.Color.A ) / 255f;
		var topLeft = new TextVertex( new( left, top ), new( u0, v0 ), color );
		var topRight = new TextVertex( new( right, top ), new( u1, v0 ), color );
		var bottomLeft = new TextVertex( new( left, bottom ), new( u0, v1 ), color );
		var bottomRight = new TextVertex( new( right, bottom ), new( u1, v1 ), color );
		vertices[offset] = topLeft;
		vertices[offset + 1] = bottomLeft;
		vertices[offset + 2] = topRight;
		vertices[offset + 3] = topRight;
		vertices[offset + 4] = bottomLeft;
		vertices[offset + 5] = bottomRight;
	}

	private ResourceSet GetResourceSet( FontAtlas? atlas )
	{
		if ( atlas == null )
			return solid.Set;
		if ( !atlases.TryGetValue( atlas, out var entry ) )
		{
			entry = CreateTexture( atlas.Alpha, atlas.Width, atlas.Height );
			atlases[atlas] = entry;
		}
		return entry.Set;
	}

	private (Veldrid.Texture Texture, ResourceSet Set) CreateTexture( byte[] alpha, int width, int height )
	{
		var texture = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D(
			(uint)width, (uint)height, 1, 1, PixelFormat.R8_UNorm, TextureUsage.Sampled ) );
		Device.UpdateTexture( texture, alpha, 0, 0, 0, (uint)width, (uint)height, 1, 0, 0 );
		var resources = shader.Reflection.ResourceLayouts.Single().Elements.Select( element => element.Name switch
		{
			"Atlas" => (BindableResource)texture,
			"s_Atlas" => Device.PointSampler,
			_ => throw new InvalidOperationException( $"Unexpected text shader resource {element.Name}." )
		} ).ToArray();
		return (texture, Device.ResourceFactory.CreateResourceSet( new ResourceSetDescription( resourceLayout, resources ) ));
	}

	public void Dispose()
	{
		foreach ( var (texture, set) in atlases.Values.Append( solid ) )
		{
			set.Dispose();
			texture.Dispose();
		}
		atlases.Clear();
		vertexBuffer?.Dispose();
		pipeline.Dispose();
		resourceLayout.Dispose();
		shader.VertexShader.Dispose();
		shader.FragmentShader.Dispose();
	}
}
