using System;
using System.Collections.Generic;
using ImGuiNET;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>
/// The ImGui overlay (the editor panels and the setup wizard) draws through <see cref="ImGuiOverlayRenderer"/>, which uploads every draw list in one write
/// through <see cref="DynamicBufferUpload"/> (issue #829). These draws run on a real device: Direct3D 11 on Windows, Metal on macOS with
/// OPENTPW_NATIVE_SHADER_TESTS=1, as <see cref="NativeWorldShaderRenderTests"/> does.
/// </summary>
[TestClass, DoNotParallelize]
public class ImGuiOverlayRendererTests
{
	private const uint Size = 64;

	private sealed class NoInput : InputSnapshot
	{
		public IReadOnlyList<KeyEvent> KeyEvents { get; } = Array.Empty<KeyEvent>();
		public IReadOnlyList<MouseEvent> MouseEvents { get; } = Array.Empty<MouseEvent>();
		public IReadOnlyList<char> KeyCharPresses { get; } = Array.Empty<char>();
		public System.Numerics.Vector2 MousePosition => new( -1, -1 );
		public float WheelDelta => 0;
		public bool IsMouseDown( MouseButton button ) => false;
	}

	[TestMethod]
	public void TwoDrawListsReachTheScreenFrameAfterFrame()
	{
		using var device = CreateDevice();
		var previousBackend = DynamicBufferUpload.Backend;
		var previousContext = ImGui.GetCurrentContext();
		ImGuiOverlayRenderer? renderer = null;
		try
		{
			DynamicBufferUpload.Backend = device.BackendType;
			var factory = device.ResourceFactory;
			using var colour = factory.CreateTexture( TextureDescription.Texture2D( Size, Size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget ) );
			using var staging = factory.CreateTexture( TextureDescription.Texture2D( Size, Size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging ) );
			using var framebuffer = factory.CreateFramebuffer( new FramebufferDescription( null, colour ) );
			using var commands = factory.CreateCommandList();
			renderer = new ImGuiOverlayRenderer( device, framebuffer.OutputDescription, (int)Size, (int)Size );
			// colours are ABGR: 0xFF0000FF red, 0xFF00FF00 green, 0xFFFF0000 blue
			var first = Frame( device, renderer, commands, framebuffer, colour, staging, centre: 0xFF0000FFu, band: 0xFF00FF00u );
			Assert.AreEqual( (255, 0, 0), first.Centre, "the foreground list's rectangle" );
			Assert.AreEqual( (0, 255, 0), first.Band, "the background list's band, the second list in the same upload" );
			var second = Frame( device, renderer, commands, framebuffer, colour, staging, centre: 0xFFFF0000u, band: 0xFF0000FFu );
			Assert.AreEqual( (0, 0, 255), second.Centre, "the next frame's upload replaces the first" );
			Assert.AreEqual( (255, 0, 0), second.Band );
		}
		finally
		{
			if ( renderer != null )
			{
				renderer.Dispose();
				ImGui.DestroyContext( ImGui.GetCurrentContext() );
			}
			if ( previousContext != IntPtr.Zero )
				ImGui.SetCurrentContext( previousContext );
			DynamicBufferUpload.Backend = previousBackend;
		}
	}

	private static ((int R, int G, int B) Centre, (int R, int G, int B) Band) Frame( GraphicsDevice device, ImGuiOverlayRenderer renderer, CommandList commands,
		Framebuffer framebuffer, Veldrid.Texture colour, Veldrid.Texture staging, uint centre, uint band )
	{
		renderer.Update( 1f / 60f, new NoInput() );
		ImGui.GetBackgroundDrawList().AddRectFilled( new System.Numerics.Vector2( 0, 0 ), new System.Numerics.Vector2( Size, 8 ), band );
		ImGui.GetForegroundDrawList().AddRectFilled( new System.Numerics.Vector2( 16, 16 ), new System.Numerics.Vector2( 48, 48 ), centre );
		commands.Begin();
		commands.SetFramebuffer( framebuffer );
		commands.ClearColorTarget( 0, RgbaFloat.Black );
		renderer.Render( device, commands );
		commands.CopyTexture( colour, staging );
		commands.End();
		device.SubmitCommands( commands );
		device.WaitForIdle();
		var mapped = device.Map( staging, MapMode.Read );
		try
		{
			(int, int, int) Pixel( int x, int y )
			{
				var offset = (int)(y * mapped.RowPitch) + x * 4;
				int Read( int at ) => System.Runtime.InteropServices.Marshal.ReadByte( mapped.Data, offset + at );
				return (Read( 0 ), Read( 1 ), Read( 2 ));
			}
			return (Pixel( 32, 32 ), Pixel( 32, 4 ));
		}
		finally
		{
			device.Unmap( staging );
		}
	}

	private static GraphicsDevice CreateDevice()
	{
		// the engine's options (Renderer.CreateDevice) without its swapchain, as NativeWorldShaderRenderTests creates its device
		var options = new GraphicsDeviceOptions
		{
			PreferStandardClipSpaceYDirection = true,
			PreferDepthRangeZeroToOne = true,
			SyncToVerticalBlank = false,
			HasMainSwapchain = false
		};
		if ( OperatingSystem.IsWindows() )
			return GraphicsDevice.CreateD3D11( options );
		if ( OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) == "1" )
			return GraphicsDevice.CreateMetal( options );
		Assert.Inconclusive( "Direct3D 11 on Windows; Metal on macOS with OPENTPW_NATIVE_SHADER_TESTS=1." );
		return null!;
	}
}
