using Veldrid;

namespace OpenTPW;

/// <summary>
/// Where the final composed frame goes: the window's swapchain, or (offscreen runs) a texture nobody presents.
/// The renderer, overlay pipelines and readbacks all go through this one abstraction instead of the swapchain.
/// </summary>
public abstract class PresentTarget
{
	/// <summary>The framebuffer the compose pass draws into.</summary>
	public abstract Framebuffer Framebuffer { get; }
	/// <summary>True when frames never reach a window.</summary>
	public abstract bool IsOffscreen { get; }
	/// <summary>Output description that pipelines drawing into <see cref="Framebuffer"/> must use.</summary>
	public OutputDescription OutputDescription => Framebuffer.OutputDescription;
	public PixelFormat ColorFormat => Framebuffer.ColorTargets[0].Target.Format;
	public uint Width => Framebuffer.Width;
	public uint Height => Framebuffer.Height;

	/// <summary>Resizes to the drawable size. The caller has already waited for the GPU to go idle.</summary>
	public abstract void Resize( uint width, uint height );
	/// <summary>Called once per submitted frame; the swapchain flips, the offscreen target does nothing.</summary>
	public abstract void Present();
}

/// <summary>The window's swapchain.</summary>
public sealed class SwapchainPresentTarget( GraphicsDevice device ) : PresentTarget
{
	public override Framebuffer Framebuffer => device.MainSwapchain.Framebuffer;
	public override bool IsOffscreen => false;
	public override void Resize( uint width, uint height ) => device.MainSwapchain.Resize( width, height );
	public override void Present() => device.SwapBuffers();
}

/// <summary>
/// A colour texture of the size the window would have had (no depth, like the swapchain, so every pipeline built for the swapchain's
/// output description stays compatible). macOS throttles Metal drawables of hidden or
/// occluded windows, so offscreen runs never touch the swapchain. The colour format matches the swapchain, so readbacks that
/// expect BGRA keep working.
/// </summary>
public sealed class OffscreenPresentTarget : PresentTarget
{
	private readonly GraphicsDevice device;
	private readonly PixelFormat colorFormat;
	private Veldrid.Texture color = null!;
	private Framebuffer framebuffer = null!;

	public OffscreenPresentTarget( GraphicsDevice device, uint width, uint height )
	{
		this.device = device;
		colorFormat = device.MainSwapchain.Framebuffer.ColorTargets[0].Target.Format;
		Create( width, height );
	}

	public override Framebuffer Framebuffer => framebuffer;
	public override bool IsOffscreen => true;
	/// <summary>The colour texture frames are drawn into.</summary>
	public Veldrid.Texture Color => color;

	public override void Resize( uint width, uint height )
	{
		if ( width == framebuffer.Width && height == framebuffer.Height )
			return;
		Destroy();
		Create( width, height );
	}

	public override void Present() { }

	private void Create( uint width, uint height )
	{
		width = Math.Max( 1, width );
		height = Math.Max( 1, height );
		var factory = device.ResourceFactory;
		color = factory.CreateTexture( TextureDescription.Texture2D( width, height, 1, 1, colorFormat, TextureUsage.RenderTarget | TextureUsage.Sampled ) );
		framebuffer = factory.CreateFramebuffer( new FramebufferDescription( null, color ) );
	}

	private void Destroy()
	{
		framebuffer.Dispose();
		color.Dispose();
	}
}
