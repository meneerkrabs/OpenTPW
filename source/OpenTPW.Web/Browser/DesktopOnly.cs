// Desktop-only pieces the shared game code refers to, answered for the browser (docs/WEB.md):
// ImGui (native library) for the developer editor and panels, and SDL audio. The game already
// copes with a missing audio device and with ImGui not wanting the mouse or keyboard.
using Veldrid;

namespace ImGuiNET
{
	public sealed class ImGuiIOPtr
	{
		public bool WantCaptureMouse => false;
		public bool WantCaptureKeyboard => false;
		public System.Numerics.Vector2 DisplayFramebufferScale { get; set; }
	}

	public static class ImGui
	{
		private static readonly ImGuiIOPtr io = new();
		public static ImGuiIOPtr GetIO() => io;
	}
}

namespace OpenTPW.ModKit
{
	public static class GlobalNamespace
	{
		public static ImGuiRenderer? ImGuiManager { get; set; }
	}
}

namespace OpenTPW
{
	/// <summary>The developer editor (OpenTPW.ModKit) draws with ImGui; in the browser it stays closed.</summary>
	public class Editor
	{
		public static Editor Instance { get; private set; } = null!;
		public bool shouldRender = false;

		public Editor( ImGuiRenderer imguiRenderer, GraphicsDevice graphicsDevice ) => Instance = this;
		public void UpdateFrom( InputSnapshot inputSnapshot ) { }
		public void Render( CommandList commandList ) { }
	}

	/// <summary>No audio device in the browser yet; callers fall back to silence.</summary>
	internal sealed class SdlMovieAudioOutput : IMovieAudioOutput
	{
		public static SdlMovieAudioOutput? TryOpen( int sampleRate, out string? failure )
		{
			failure = "the browser build has no audio output yet";
			return null;
		}

		public int SampleRate => 0;
		public long PlayedFrames => 0;
		public long QueuedFrames => 0;
		public double LatencySeconds => 0;
		public void Queue( ReadOnlySpan<short> interleavedStereo ) { }
		public void Play() { }
		public void Stop() { }
		public void Dispose() { }
	}
}
