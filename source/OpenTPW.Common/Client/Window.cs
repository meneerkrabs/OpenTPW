using System.Globalization;
using Veldrid.Sdl2;

namespace OpenTPW;

/// <summary>
/// Contains code for the instantiation and management of a window, the game editor,
/// ImGUI, inputs, the renderer, and the world itself.
/// </summary>
public class Window
{
	public static Window Current { get; set; }
	public Sdl2Window SdlWindow { get; private set; }

	/// <summary>Logical size (desktop units; SDL mouse coordinates use these).</summary>
	public unsafe Point2 Size
	{
		get
		{
			// Ask SDL directly: Sdl2Window caches the size until it processes the resize event.
			int width = 0, height = 0;
			Sdl2Native.SDL_GetWindowSize( SdlWindow.SdlWindowHandle, &width, &height );
			return width > 0 && height > 0 ? new Point2( width, height ) : new Point2( SdlWindow.Width, SdlWindow.Height );
		}
	}

	/// <summary>
	/// Drawable size in pixels (larger than <see cref="Size"/> on HiDPI displays). The test-only
	/// <c>OPENTPW_TEST_PIXEL_SCALE</c> (integer 1-4) multiplies it to exercise HiDPI paths on 1x displays.
	/// </summary>
	public Point2 PixelSize
	{
		get
		{
			var logical = Size;
			if ( logical.X <= 0 || logical.Y <= 0 || SdlWindow.WindowState == Veldrid.WindowState.Minimized )
				return new Point2( 0, 0 );
			var pixels = SdlDisplay.GetPixelSize( SdlWindow, logical );
			return new Point2( pixels.X * TestPixelScale, pixels.Y * TestPixelScale );
		}
	}

	public static int TestPixelScale { get; } = ReadTestPixelScale();

	private static int ReadTestPixelScale()
	{
		var text = Environment.GetEnvironmentVariable( "OPENTPW_TEST_PIXEL_SCALE" );
		return int.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out var scale ) && scale >= 1 && scale <= 4 ? scale : 1;
	}

	public Action<Point2>? OnResized { get; set; }

	public bool Visible
	{
		get => SdlWindow.Visible;
		set => SdlWindow.Visible = value;
	}

	public Window( int width, int height, string title, bool startHidden = false )
	{
		Current ??= this;

		// Same flags as VeldridStartup.CreateWindow plus AllowHighDpi, so Retina drawables are full resolution.
		var flags = SDL_WindowFlags.OpenGL | SDL_WindowFlags.Resizable | SDL_WindowFlags.AllowHighDpi
			| (startHidden ? SDL_WindowFlags.Hidden : SDL_WindowFlags.Shown);
		SdlWindow = new Sdl2Window( title, 32, 32, width, height, flags, false );
		SdlDisplay.SetMinimumSize( SdlWindow, 320, 200 );
		SdlWindow.Resized += SdlWindow_Resized;
		Screen.UpdateFrom( Size, PixelSize );
	}

	private void SdlWindow_Resized()
	{
		Screen.UpdateFrom( Size, PixelSize );
		OnResized?.Invoke( Size );
	}
}
