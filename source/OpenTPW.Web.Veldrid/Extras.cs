// The parts of Veldrid.SPIRV, Veldrid.StartupUtilities and Veldrid.ImGui that the game touches.
// Shaders arrive precompiled (content/shaders/web), and the developer panels are not in the browser.

namespace Veldrid.SPIRV
{
	public class SpirvReflection
	{
		public VertexElementDescription[] VertexElements { get; }
		public ResourceLayoutDescription[] ResourceLayouts { get; }

		public SpirvReflection( VertexElementDescription[] vertexElements, ResourceLayoutDescription[] resourceLayouts )
		{
			VertexElements = vertexElements;
			ResourceLayouts = resourceLayouts;
		}
	}
}

namespace Veldrid.StartupUtilities
{
	using Veldrid.Sdl2;

	public static class VeldridStartup
	{
		public static SwapchainSource GetSwapchainSource( Sdl2Window window ) => new CanvasSwapchainSource();
	}
}

namespace Veldrid
{
	/// <summary>ImGui draws the developer panels, which the browser build leaves out.</summary>
	public class ImGuiRenderer : IDisposable
	{
		public ImGuiRenderer( GraphicsDevice graphicsDevice, OutputDescription outputDescription, int width, int height ) { }
		public void Update( float deltaSeconds, InputSnapshot snapshot ) { }
		public void Render( GraphicsDevice graphicsDevice, CommandList commandList ) { }
		public void WindowResized( int width, int height ) { }
		public void Dispose() { }
	}
}
