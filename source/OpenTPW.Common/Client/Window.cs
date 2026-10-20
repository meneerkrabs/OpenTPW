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

	/// <summary>
	/// Set before the renderer is created (<c>--offscreen</c>, <c>OPENTPW_OFFSCREEN=1</c>, or implied by an automated mode such as a smoke, capture or perf run):
	/// the renderer then keeps its window hidden and draws into an offscreen target. See <see cref="IsOffscreen"/>.
	/// </summary>
	public static bool OffscreenRequested { get; set; }

	/// <summary>
	/// True for a window that is never shown. It has no OS-level size or fullscreen state: <see cref="Size"/> is the
	/// virtual size the window would have had, and <see cref="PixelSize"/> follows it at pixel density 1 times
	/// <see cref="TestPixelScale"/>.
	/// </summary>
	public bool IsOffscreen { get; }

	private Point2 virtualSize;

	/// <summary>Offscreen windows only: the logical size the window would have (windowed size, or the desktop size in fullscreen).</summary>
	public void SetVirtualSize( Point2 size )
	{
		if ( !IsOffscreen || size.Equals( virtualSize ) )
			return;
		virtualSize = size;
		SdlWindow_Resized();
	}

	/// <summary>
	/// Whether the window has input focus. An offscreen window is never focused, like the unfocused window of a visible smoke: its
	/// pointer rests in the window corner, and a focused window would edge-scroll the park camera there (pushscroll) and drift every screenshot.
	/// </summary>
	public bool Focused => !IsOffscreen && SdlWindow.Focused;

	/// <summary>Logical size (desktop units; SDL mouse coordinates use these).</summary>
	public unsafe Point2 Size
	{
		get
		{
			if ( IsOffscreen )
				return virtualSize;
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
			if ( IsOffscreen )
				return new Point2( logical.X * TestPixelScale, logical.Y * TestPixelScale );
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
		set
		{
			if ( IsOffscreen && value )
				return; // an offscreen window is never shown
			SdlWindow.Visible = value;
		}
	}

	/// <summary>SDL_GetWindowFlags: lets a log line prove an offscreen window stayed hidden.</summary>
	public SDL_WindowFlags SdlFlags => Sdl2Native.SDL_GetWindowFlags( SdlWindow.SdlWindowHandle );

	public Window( int width, int height, string title, bool startHidden = false, bool offscreen = false )
	{
		Current ??= this;
		IsOffscreen = offscreen;
		startHidden |= offscreen;
		virtualSize = new Point2( width, height );

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
