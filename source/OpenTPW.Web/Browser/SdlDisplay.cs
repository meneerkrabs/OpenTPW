using Veldrid.Sdl2;

namespace OpenTPW;

/// <summary>
/// The browser's answer to the desktop <c>SdlDisplay</c> (OpenTPW.Common): the canvas size times the
/// device pixel ratio, one display, and no exclusive fullscreen.
/// </summary>
public static class SdlDisplay
{
	public static string PixelSizeSource { get; private set; } = "canvas";

	public static Point2 GetPixelSize( Sdl2Window window, Point2 logicalSize )
	{
		var (width, height) = window.PixelSize;
		return width > 0 && height > 0 ? new Point2( width, height ) : logicalSize;
	}

	public static int GetWindowDisplayIndex( Sdl2Window window ) => 0;

	public static Point2 GetDesktopSize( int displayIndex )
	{
		var (width, height) = Window.Current?.SdlWindow.ScreenSize ?? (0, 0);
		return new Point2( width, height );
	}

	public static IReadOnlyList<Point2> GetDisplayModeSizes( int displayIndex ) => [];

	public static bool TrySetExclusiveMode( Sdl2Window window, Point2 size, out string reason )
	{
		reason = "the browser has no exclusive fullscreen";
		return false;
	}

	public static void SetMinimumSize( Sdl2Window window, int width, int height ) { }
}
