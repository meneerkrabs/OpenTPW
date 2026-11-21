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
