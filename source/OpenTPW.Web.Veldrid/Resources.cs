using System.Runtime.InteropServices;

namespace Veldrid;

public class VeldridException : Exception
{
	public VeldridException() { }
	public VeldridException( string message ) : base( message ) { }
	public VeldridException( string message, Exception innerException ) : base( message, innerException ) { }
}

public interface BindableResource { }
public interface MappableResource { }

/// <summary>A WebGL object behind an integer handle.</summary>
public abstract class DeviceResource : IDisposable
{
	internal int Handle { get; set; }
	public string? Name { get; set; }
	public bool IsDisposed { get; private set; }

	public virtual void Dispose()
	{
		if ( IsDisposed )
			return;
		IsDisposed = true;
		if ( Handle != 0 )
			Gl.Destroy( Handle );
		Handle = 0;
	}
}

public class Texture : DeviceResource, BindableResource, MappableResource
{
	public PixelFormat Format { get; }
	public uint Width { get; }
	public uint Height { get; }
	public uint Depth => 1;
	public uint MipLevels { get; }
	public uint ArrayLayers => 1;
	public TextureUsage Usage { get; }
	public TextureType Type => TextureType.Texture2D;
	public TextureSampleCount SampleCount { get; }

	internal Texture( in TextureDescription description )
	{
		if ( description.Type != TextureType.Texture2D || description.Depth > 1 || description.ArrayLayers > 1 )
			throw new VeldridException( "The browser renderer only supports single 2D textures." );
		Format = description.Format;
		Width = description.Width;
		Height = description.Height;
		MipLevels = Math.Max( 1, description.MipLevels );
		Usage = description.Usage;
		SampleCount = description.SampleCount;
		// Staging textures are only read back on the desktop (smoke-test captures).
		if ( (Usage & TextureUsage.Staging) != 0 )
			return;
		var samples = SampleCount switch
		{
			TextureSampleCount.Count2 => 2,
			TextureSampleCount.Count4 => 4,
			TextureSampleCount.Count8 => 8,
			TextureSampleCount.Count1 => 1,
			_ => 8
		};
		Handle = Gl.CreateTexture( Formats.Texture( Format ).Internal, (int)Width, (int)Height, (int)MipLevels, samples );
	}
}

public class TextureView : DeviceResource, BindableResource
{
	public Texture Target { get; }
	public uint BaseMipLevel => 0;
	public uint MipLevels => Target.MipLevels;
	public uint BaseArrayLayer => 0;
	public uint ArrayLayers => 1;
	public PixelFormat Format => Target.Format;

	internal TextureView( Texture target ) => Target = target;

	// The view shares the texture's WebGL object.
	public override void Dispose() { }
}

public class DeviceBuffer : DeviceResource, BindableResource, MappableResource
{
	public uint SizeInBytes { get; }
	public BufferUsage Usage { get; }

	internal DeviceBuffer( in BufferDescription description )
	{
		SizeInBytes = description.SizeInBytes;
		Usage = description.Usage;
		// WebGL2 never lets an index buffer be bound to another target, so the target is fixed here.
		var target = (Usage & BufferUsage.IndexBuffer) != 0 ? Gl.ElementArrayBuffer
			: (Usage & BufferUsage.UniformBuffer) != 0 ? Gl.UniformBuffer
			: Gl.ArrayBuffer;
		Handle = Gl.CreateBuffer( target, (int)SizeInBytes );
	}
}

public class Sampler : DeviceResource, BindableResource
{
	internal Sampler( in SamplerDescription description )
	{
		var (min, mag) = Formats.Filter( description.Filter );
		var anisotropy = description.Filter == SamplerFilter.Anisotropic ? (int)Math.Max( 1, description.MaximumAnisotropy ) : 1;
		Handle = Gl.CreateSampler( min, mag, Formats.Address( description.AddressModeU ), Formats.Address( description.AddressModeV ), Formats.Address( description.AddressModeW ),
			description.MinimumLod, description.MaximumLod == uint.MaxValue ? 1000 : description.MaximumLod, anisotropy );
	}
}

/// <summary>GLSL ES source; the browser's ShaderCompiler supplies the reflected resource names.</summary>
public class Shader : DeviceResource
{
	public ShaderStages Stage { get; }
	public string EntryPoint { get; }
	internal string Source { get; }

	/// <summary>
	/// Shader-side resource names per set, in layout order (from content/shaders/web). Pipelines bind
	/// by position like Metal and Direct3D, using these names to find the WebGL uniform or block.
	/// </summary>
	public string[][]? ResourceNames { get; set; }

	internal Shader( in ShaderDescription description )
	{
		Stage = description.Stage;
		EntryPoint = description.EntryPoint;
		Source = System.Text.Encoding.UTF8.GetString( description.ShaderBytes );
	}

	public override void Dispose() { }
}

public class ResourceLayout : DeviceResource
{
	internal ResourceLayoutElementDescription[] Elements { get; }
	internal ResourceLayout( in ResourceLayoutDescription description ) => Elements = description.Elements ?? [];
	public override void Dispose() { }
}

public class ResourceSet : DeviceResource
{
	internal ResourceSet( in ResourceSetDescription description )
	{
		var resources = description.BoundResources;
		var entries = new int[1 + resources.Length * 3];
		entries[0] = resources.Length;
		for ( var index = 0; index < resources.Length; index++ )
		{
			var (kind, handle, size) = resources[index] switch
			{
				DeviceBuffer buffer => (0, buffer.Handle, (int)buffer.SizeInBytes),
				Texture texture => (1, texture.Handle, 0),
				TextureView view => (1, view.Target.Handle, 0),
				Sampler sampler => (2, sampler.Handle, 0),
				var other => throw new VeldridException( $"The browser renderer cannot bind {other?.GetType().Name}." )
			};
			entries[1 + index * 3] = kind;
			entries[2 + index * 3] = handle;
			entries[3 + index * 3] = size;
		}
		Handle = Gl.CreateResourceSet( entries );
	}
}

public class Framebuffer : DeviceResource
{
	public FramebufferAttachment? DepthTarget { get; }
	public IReadOnlyList<FramebufferAttachment> ColorTargets { get; }
	public OutputDescription OutputDescription { get; }
	public uint Width { get; }
	public uint Height { get; }

	internal Framebuffer( in FramebufferDescription description )
	{
		ColorTargets = description.ColorTargets.Select( target => new FramebufferAttachment( target.Target, target.ArrayLayer, target.MipLevel ) ).ToArray();
		DepthTarget = description.DepthTarget is { } depth ? new FramebufferAttachment( depth.Target, depth.ArrayLayer, depth.MipLevel ) : null;
		var first = ColorTargets.Count > 0 ? ColorTargets[0].Target : DepthTarget?.Target;
		Width = first?.Width ?? 0;
		Height = first?.Height ?? 0;
		OutputDescription = new OutputDescription(
			DepthTarget is { } d ? new OutputAttachmentDescription( d.Target.Format ) : null,
			ColorTargets.Select( target => new OutputAttachmentDescription( target.Target.Format ) ).ToArray(),
			first?.SampleCount ?? TextureSampleCount.Count1 );
		Span<int> colors = stackalloc int[ColorTargets.Count];
		for ( var index = 0; index < colors.Length; index++ )
			colors[index] = ColorTargets[index].Target.Handle;
		Handle = Gl.CreateFramebuffer( colors, DepthTarget?.Target.Handle ?? 0 );
	}
}

public abstract class SwapchainSource { }

internal sealed class CanvasSwapchainSource : SwapchainSource { }

/// <summary>
/// The canvas. Everything renders into an offscreen framebuffer with row 0 at the top (docs/WEB.md);
/// <see cref="GraphicsDevice.SwapBuffers"/> copies it to the canvas flipped.
/// </summary>
public class Swapchain : DeviceResource
{
	private Texture? color;
	public Framebuffer Framebuffer { get; private set; } = null!;
	public bool SyncToVerticalBlank { get; set; } = true;

	internal Swapchain( uint width, uint height ) => Resize( width, height );

	public void Resize( uint width, uint height )
	{
		width = Math.Max( 1, width );
		height = Math.Max( 1, height );
		Framebuffer?.Dispose();
		color?.Dispose();
		color = new Texture( TextureDescription.Texture2D( width, height, 1, 1, PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled ) );
		Framebuffer = new Framebuffer( new FramebufferDescription( (Texture?)null, color ) );
		Gl.ResizeCanvas( (int)width, (int)height );
	}

	public override void Dispose()
	{
		Framebuffer?.Dispose();
		color?.Dispose();
	}
}

public class Pipeline : DeviceResource
{
	private static readonly Dictionary<string, int> programs = new();

	public bool IsComputePipeline => false;

	internal Pipeline( in GraphicsPipelineDescription description )
	{
		var shaders = description.ShaderSet.Shaders;
		var vertex = shaders.First( shader => shader.Stage == ShaderStages.Vertex );
		var fragment = shaders.First( shader => shader.Stage == ShaderStages.Fragment );
		var names = vertex.ResourceNames ?? fragment.ResourceNames;
		var layouts = description.ResourceLayouts ?? [];

		// Uniform blocks and texture units are numbered in layout order, like Veldrid's OpenGL backend.
		var uniformBlock = 0;
		var textureUnit = 0;
		var bindings = new List<(string Name, bool Block, int Slot)>();
		var sets = new List<int>();
		sets.Add( layouts.Length );
		for ( var set = 0; set < layouts.Length; set++ )
		{
			var elements = layouts[set].Elements;
			sets.Add( elements.Length );
			for ( var index = 0; index < elements.Length; index++ )
			{
				var name = names != null && set < names.Length && index < names[set].Length ? names[set][index] : elements[index].Name;
				switch ( elements[index].Kind )
				{
					case ResourceKind.UniformBuffer:
						bindings.Add( (name, true, uniformBlock) );
						sets.Add( 0 );
						sets.Add( uniformBlock++ );
						break;
					case ResourceKind.TextureReadOnly:
						bindings.Add( (name, false, textureUnit) );
						sets.Add( 1 );
						sets.Add( textureUnit++ );
						break;
					case ResourceKind.Sampler:
						// A sampler applies to every texture of its set (SPIR-V-Cross combines them by texture name).
						sets.Add( 2 );
						sets.Add( -1 );
						break;
					default:
						throw new VeldridException( $"The browser renderer does not support {elements[index].Kind} resources." );
				}
			}
		}

		var key = string.Join( '\u0001', vertex.Source, fragment.Source, string.Join( ',', bindings ) );
		if ( !programs.TryGetValue( key, out var program ) )
		{
			program = Gl.CreateProgram( vertex.Source, fragment.Source );
			foreach ( var binding in bindings )
				Gl.BindProgramResource( program, binding.Name, binding.Block, binding.Slot );
			programs[key] = program;
		}

		var blend = description.BlendState.AttachmentStates is { Length: > 0 } states ? states[0] : BlendAttachmentDescription.Disabled;
		var raster = description.RasterizerState;
		var depth = description.DepthStencilState;
		var factor = description.BlendState.BlendFactor;
		var data = new List<int>
		{
			program,
			Formats.Topology( description.PrimitiveTopology ),
			raster.CullMode switch { FaceCullMode.Back => 1, FaceCullMode.Front => 2, _ => 0 },
			// Negating Y in the shaders mirrors the winding (docs/WEB.md).
			raster.FrontFace == FrontFace.Clockwise ? 0x0901 : 0x0900,
			depth.DepthTestEnabled ? 1 : 0,
			depth.DepthWriteEnabled ? 1 : 0,
			Formats.Comparison( depth.DepthComparison ),
			raster.ScissorTestEnabled ? 1 : 0,
			blend.BlendEnabled ? 1 : 0,
			Formats.Blend( blend.SourceColorFactor ),
			Formats.Blend( blend.DestinationColorFactor ),
			Formats.Blend( blend.ColorFunction ),
			Formats.Blend( blend.SourceAlphaFactor ),
			Formats.Blend( blend.DestinationAlphaFactor ),
			Formats.Blend( blend.AlphaFunction ),
			(int)(blend.ColorWriteMask ?? ColorWriteMask.All),
			BitConverter.SingleToInt32Bits( factor.R ),
			BitConverter.SingleToInt32Bits( factor.G ),
			BitConverter.SingleToInt32Bits( factor.B ),
			BitConverter.SingleToInt32Bits( factor.A ),
		};

		var vertexLayouts = description.ShaderSet.VertexLayouts ?? [];
		data.Add( vertexLayouts.Length );
		var location = 0;
		foreach ( var layout in vertexLayouts )
		{
			data.Add( (int)layout.Stride );
			data.Add( (int)layout.InstanceStepRate );
			data.Add( layout.Elements.Length );
			uint offset = 0;
			foreach ( var element in layout.Elements )
			{
				if ( element.Offset != 0 )
					offset = element.Offset;
				var (size, type, normalized, integer) = Formats.Attribute( element.Format );
				data.Add( location++ );
				data.Add( size );
				data.Add( type );
				data.Add( normalized ? 1 : 0 );
				data.Add( integer ? 1 : 0 );
				data.Add( (int)offset );
				offset += Formats.SizeInBytes( element.Format );
			}
		}
		data.AddRange( sets );
		Handle = Gl.CreatePipeline( CollectionsMarshal.AsSpan( data ) );
	}
}
