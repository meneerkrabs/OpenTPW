using Veldrid;

namespace OpenTPW;

/// <summary>
/// The image an advisor model is drawn into for the interface to show over the park (<see cref="Advisor.RenderImage"/>, <see cref="ThemeParkIncAdvisorView"/>): a colour and depth target in
/// the world target's formats and multisampling, resolved into a texture the interface batch samples. Made again only when its size changes.
/// </summary>
internal sealed class AdvisorImageTarget : IDisposable
{
	private Framebuffer? target;
	private Veldrid.Texture? color;
	private Veldrid.Texture? depth;

	/// <summary>The image of the last <see cref="End"/>, owned by this target.</summary>
	public Veldrid.Texture? Image { get; private set; }

	/// <summary>Sets the command list to draw into a cleared image of <paramref name="width"/> by <paramref name="height"/> pixels. Call it in the world pass; <see cref="End"/> restores the world target.</summary>
	public void Begin( int width, int height )
	{
		Ensure( width, height, global::Global.Render.MultisampledFramebuffer );
		var commandList = global::Global.Render.CommandList;
		commandList.SetFramebuffer( target! );
		commandList.SetFullViewports();
		commandList.SetFullScissorRects();
		commandList.ClearColorTarget( 0, RgbaFloat.Clear );
		commandList.ClearDepthStencil( 1 );
	}

	/// <summary>Resolves the drawn image and sets the world target again.</summary>
	public Veldrid.Texture? End()
	{
		var commandList = global::Global.Render.CommandList;
		commandList.ResolveTexture( color!, Image! );
		commandList.SetFramebuffer( global::Global.Render.MultisampledFramebuffer );
		commandList.SetFullViewports();
		commandList.SetFullScissorRects();
		return Image;
	}

	/// <summary>Same formats and multisampling as the world target, so the advisor's pipelines draw into it unchanged.</summary>
	private void Ensure( int width, int height, Framebuffer world )
	{
		if ( Image != null && Image.Width == (uint)width && Image.Height == (uint)height )
			return;
		Dispose();
		var worldColor = world.ColorTargets[0].Target;
		var worldDepth = world.DepthTarget!.Value.Target;
		var factory = global::Global.Device.ResourceFactory;
		color = factory.CreateTexture( TextureDescription.Texture2D( (uint)width, (uint)height, 1, 1, worldColor.Format, TextureUsage.RenderTarget, worldColor.SampleCount ) );
		depth = factory.CreateTexture( TextureDescription.Texture2D( (uint)width, (uint)height, 1, 1, worldDepth.Format, TextureUsage.DepthStencil, worldDepth.SampleCount ) );
		Image = factory.CreateTexture( TextureDescription.Texture2D( (uint)width, (uint)height, 1, 1, worldColor.Format, TextureUsage.Sampled ) );
		target = factory.CreateFramebuffer( new FramebufferDescription( depth, color ) );
	}

	/// <summary>Releases the image and its targets after the frames that still use them (a later <see cref="Begin"/> makes new ones).</summary>
	public void Dispose()
	{
		var oldTarget = target;
		var oldColor = color;
		var oldDepth = depth;
		var image = Image;
		target = null;
		color = depth = Image = null;
		if ( image == null )
			return;
		global::Global.Render.ScheduleDelete( () =>
		{
			oldTarget?.Dispose();
			oldColor?.Dispose();
			oldDepth?.Dispose();
			image.Dispose();
		} );
	}
}
