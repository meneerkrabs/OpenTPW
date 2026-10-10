using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Veldrid;

/// <summary>
/// WebGL2 behind Veldrid's GraphicsDevice API. The browser build answers whichever factory
/// <c>Renderer.CreateDevice</c> picks (it reaches <see cref="CreateVulkan"/>, as on Linux), so the
/// desktop code needs no browser branch.
/// </summary>
public class GraphicsDevice : IDisposable
{
	public GraphicsBackend BackendType => GraphicsBackend.OpenGLES;
	public ResourceFactory ResourceFactory { get; }
	public Swapchain MainSwapchain { get; }
	public Framebuffer SwapchainFramebuffer => MainSwapchain.Framebuffer;
	public Sampler PointSampler { get; }
	public Sampler LinearSampler { get; }
	public bool IsUvOriginTopLeft => true;
	public bool IsDepthRangeZeroToOne => true;
	public bool IsClipSpaceYInverted => false;
	public bool SyncToVerticalBlank { get; set; } = true;

	private GraphicsDevice( in SwapchainDescription swapchain )
	{
		if ( !Gl.Init( "canvas" ) )
			throw new VeldridException( "This browser does not offer WebGL2." );
		ResourceFactory = new ResourceFactory();
		MainSwapchain = new Swapchain( swapchain.Width, swapchain.Height );
		PointSampler = ResourceFactory.CreateSampler( SamplerDescription.Point );
		LinearSampler = ResourceFactory.CreateSampler( SamplerDescription.Linear );
	}

	public static GraphicsDevice CreateVulkan( GraphicsDeviceOptions options, SwapchainDescription swapchainDescription ) => new( swapchainDescription );
	public static GraphicsDevice CreateMetal( GraphicsDeviceOptions options, SwapchainDescription swapchainDescription ) => new( swapchainDescription );
	public static GraphicsDevice CreateD3D11( GraphicsDeviceOptions options, SwapchainDescription swapchainDescription ) => new( swapchainDescription );

	public bool GetPixelFormatSupport( PixelFormat format, TextureType type, TextureUsage usage, out PixelFormatProperties properties )
	{
		var size = (uint)Gl.MaxTextureSize();
		properties = new PixelFormatProperties { MaxWidth = size, MaxHeight = size, MaxDepth = 1, MaxMipLevels = 1 + (uint)Math.Log2( size ), MaxArrayLayers = 1 };
		return type == TextureType.Texture2D;
	}

	public void SubmitCommands( CommandList commandList ) => commandList.Execute();

	public void SwapBuffers()
	{
		var framebuffer = MainSwapchain.Framebuffer;
		Gl.Present( framebuffer.Handle, (int)framebuffer.Width, (int)framebuffer.Height );
	}

	public void SwapBuffers( Swapchain swapchain ) => SwapBuffers();

	/// <summary>WebGL runs in order on the page's only thread; there is nothing to wait for.</summary>
	public void WaitForIdle() { }

	public void UpdateBuffer( DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes )
	{
		unsafe
		{
			Gl.UploadBuffer( buffer.Handle, (int)bufferOffsetInBytes, new Span<byte>( (void*)source, (int)sizeInBytes ) );
		}
	}

	public void UpdateBuffer<T>( DeviceBuffer buffer, uint bufferOffsetInBytes, T source ) where T : unmanaged
		=> Gl.UploadBuffer( buffer.Handle, (int)bufferOffsetInBytes, MemoryMarshal.AsBytes( new Span<T>( ref source ) ) );

	public void UpdateBuffer<T>( DeviceBuffer buffer, uint bufferOffsetInBytes, ref T source ) where T : unmanaged
		=> Gl.UploadBuffer( buffer.Handle, (int)bufferOffsetInBytes, MemoryMarshal.AsBytes( new Span<T>( ref source ) ) );

	public void UpdateBuffer<T>( DeviceBuffer buffer, uint bufferOffsetInBytes, ref T source, uint sizeInBytes ) where T : unmanaged
		=> Gl.UploadBuffer( buffer.Handle, (int)bufferOffsetInBytes, MemoryMarshal.CreateSpan( ref Unsafe.As<T, byte>( ref source ), (int)sizeInBytes ) );

	public void UpdateBuffer<T>( DeviceBuffer buffer, uint bufferOffsetInBytes, T[] source ) where T : unmanaged
		=> Gl.UploadBuffer( buffer.Handle, (int)bufferOffsetInBytes, MemoryMarshal.AsBytes( source.AsSpan() ) );

	public void UpdateBuffer<T>( DeviceBuffer buffer, uint bufferOffsetInBytes, ReadOnlySpan<T> source ) where T : unmanaged
		=> Gl.UploadBuffer( buffer.Handle, (int)bufferOffsetInBytes, MemoryMarshal.AsBytes( MemoryMarshal.CreateSpan( ref MemoryMarshal.GetReference( source ), source.Length ) ) );

	public void UpdateTexture( Texture texture, IntPtr source, uint sizeInBytes, uint x, uint y, uint z, uint width, uint height, uint depth, uint mipLevel, uint arrayLayer )
	{
		unsafe
		{
			UploadTexture( texture, new Span<byte>( (void*)source, (int)sizeInBytes ), x, y, width, height, mipLevel );
		}
	}

	public void UpdateTexture<T>( Texture texture, T[] source, uint x, uint y, uint z, uint width, uint height, uint depth, uint mipLevel, uint arrayLayer ) where T : unmanaged
		=> UploadTexture( texture, MemoryMarshal.AsBytes( source.AsSpan() ), x, y, width, height, mipLevel );

	public void UpdateTexture<T>( Texture texture, ReadOnlySpan<T> source, uint x, uint y, uint z, uint width, uint height, uint depth, uint mipLevel, uint arrayLayer ) where T : unmanaged
		=> UploadTexture( texture, MemoryMarshal.AsBytes( MemoryMarshal.CreateSpan( ref MemoryMarshal.GetReference( source ), source.Length ) ), x, y, width, height, mipLevel );

	private static void UploadTexture( Texture texture, Span<byte> pixels, uint x, uint y, uint width, uint height, uint mipLevel )
	{
		var (_, format, type, bytesPerPixel) = Formats.Texture( texture.Format );
		var length = (int)(width * height) * bytesPerPixel;
		if ( pixels.Length < length )
			throw new VeldridException( $"Texture upload of {pixels.Length} bytes is smaller than {width}x{height} {texture.Format}." );
		pixels = pixels[..length];
		if ( texture.Format == PixelFormat.B8_G8_R8_A8_UNorm )
		{
			// Stored as RGBA (no BGRA8 in WebGL2): swap red and blue on a copy.
			var swizzled = pixels.ToArray();
			for ( var index = 0; index < swizzled.Length; index += 4 )
				(swizzled[index], swizzled[index + 2]) = (swizzled[index + 2], swizzled[index]);
			pixels = swizzled;
		}
		Gl.UploadTexture( texture.Handle, (int)mipLevel, (int)x, (int)y, (int)width, (int)height, format, type, pixels );
	}

	/// <summary>Reading GPU memory back is a desktop smoke-test feature.</summary>
	public MappedResource Map( MappableResource resource, MapMode mode ) => throw new NotSupportedException( "The browser renderer cannot map GPU resources." );
	public MappedResource Map( MappableResource resource, MapMode mode, uint subresource ) => Map( resource, mode );
	public void Unmap( MappableResource resource ) { }
	public void Unmap( MappableResource resource, uint subresource ) { }

	public void Dispose()
	{
		MainSwapchain.Dispose();
		PointSampler.Dispose();
		LinearSampler.Dispose();
	}
}

public class ResourceFactory
{
	public GraphicsBackend BackendType => GraphicsBackend.OpenGLES;

	public Texture CreateTexture( TextureDescription description ) => new( description );
	public Texture CreateTexture( ref TextureDescription description ) => new( description );
	public TextureView CreateTextureView( Texture target ) => new( target );
	public DeviceBuffer CreateBuffer( BufferDescription description ) => new( description );
	public DeviceBuffer CreateBuffer( ref BufferDescription description ) => new( description );
	public Sampler CreateSampler( SamplerDescription description ) => new( description );
	public Sampler CreateSampler( ref SamplerDescription description ) => new( description );
	public Shader CreateShader( ShaderDescription description ) => new( description );
	public Shader CreateShader( ref ShaderDescription description ) => new( description );
	public ResourceLayout CreateResourceLayout( ResourceLayoutDescription description ) => new( description );
	public ResourceLayout CreateResourceLayout( ref ResourceLayoutDescription description ) => new( description );
	public ResourceSet CreateResourceSet( ResourceSetDescription description ) => new( description );
	public ResourceSet CreateResourceSet( ref ResourceSetDescription description ) => new( description );
	public Framebuffer CreateFramebuffer( FramebufferDescription description ) => new( description );
	public Framebuffer CreateFramebuffer( ref FramebufferDescription description ) => new( description );
	public Pipeline CreateGraphicsPipeline( GraphicsPipelineDescription description ) => new( description );
	public Pipeline CreateGraphicsPipeline( ref GraphicsPipelineDescription description ) => new( description );
	public CommandList CreateCommandList() => new();
}
