using System.Runtime.InteropServices;
using Veldrid.Sdl2;

namespace OpenTPW;

/// <summary>
/// SDL2 display queries that Veldrid.SDL2 does not bind: drawable size in pixels (HiDPI) and the
/// display-mode list. Functions are loaded lazily; older SDL2 builds without them fall back to the
/// logical window size / the desktop mode, so callers always get a usable answer.
/// </summary>
public static unsafe class SdlDisplay
{
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )]
	private delegate void GetSize_t( IntPtr window, int* width, int* height );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )]
	private delegate int GetNumDisplayModes_t( int displayIndex );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )]
	private delegate int GetDisplayMode_t( int displayIndex, int modeIndex, SDL_DisplayMode* mode );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )]
	private delegate int GetWindowDisplayIndex_t( IntPtr window );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )]
	private delegate int SetWindowDisplayMode_t( IntPtr window, SDL_DisplayMode* mode );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )]
	private delegate void SetWindowMinimumSize_t( IntPtr window, int width, int height );

	private static readonly Lazy<GetSize_t?> getWindowSizeInPixels = new( () => TryLoad<GetSize_t>( "SDL_GetWindowSizeInPixels" ) );
	private static readonly Lazy<GetSize_t?> getMetalDrawableSize = new( () => TryLoad<GetSize_t>( "SDL_Metal_GetDrawableSize" ) );
	private static readonly Lazy<GetSize_t?> getVulkanDrawableSize = new( () => TryLoad<GetSize_t>( "SDL_Vulkan_GetDrawableSize" ) );
	private static readonly Lazy<GetNumDisplayModes_t?> getNumDisplayModes = new( () => TryLoad<GetNumDisplayModes_t>( "SDL_GetNumDisplayModes" ) );
	private static readonly Lazy<GetDisplayMode_t?> getDisplayMode = new( () => TryLoad<GetDisplayMode_t>( "SDL_GetDisplayMode" ) );
	private static readonly Lazy<GetWindowDisplayIndex_t?> getWindowDisplayIndex = new( () => TryLoad<GetWindowDisplayIndex_t>( "SDL_GetWindowDisplayIndex" ) );
	private static readonly Lazy<SetWindowDisplayMode_t?> setWindowDisplayMode = new( () => TryLoad<SetWindowDisplayMode_t>( "SDL_SetWindowDisplayMode" ) );
	private static readonly Lazy<SetWindowMinimumSize_t?> setWindowMinimumSize = new( () => TryLoad<SetWindowMinimumSize_t>( "SDL_SetWindowMinimumSize" ) );

	/// <summary>Which SDL query produced the last pixel size (diagnostics).</summary>
	public static string PixelSizeSource { get; private set; } = "window size";

	private static T? TryLoad<T>( string name ) where T : Delegate
	{
		try
		{
			return Sdl2Native.LoadFunction<T>( name );
		}
		catch ( Exception )
		{
			return null;
		}
	}

	/// <summary>
	/// Drawable size in pixels: SDL_GetWindowSizeInPixels (SDL ≥ 2.26), else the Metal/Vulkan drawable
	/// size, else the logical size. Results that disagree with the logical aspect are rejected.
	/// </summary>
	public static Point2 GetPixelSize( Sdl2Window window, Point2 logicalSize )
	{
		foreach ( var (name, function) in new[] {
			("SDL_GetWindowSizeInPixels", getWindowSizeInPixels.Value),
			("SDL_Metal_GetDrawableSize", OperatingSystem.IsMacOS() ? getMetalDrawableSize.Value : null),
			("SDL_Vulkan_GetDrawableSize", OperatingSystem.IsLinux() ? getVulkanDrawableSize.Value : null) } )
		{
			if ( function == null )
				continue;
			int width = 0, height = 0;
			function( window.SdlWindowHandle, &width, &height );
			if ( width > 0 && height > 0 && width >= logicalSize.X && height >= logicalSize.Y )
			{
				PixelSizeSource = name;
				return new Point2( width, height );
			}
		}
		PixelSizeSource = "window size";
		return logicalSize;
	}

	public static int GetWindowDisplayIndex( Sdl2Window window ) =>
		Math.Max( 0, getWindowDisplayIndex.Value?.Invoke( window.SdlWindowHandle ) ?? 0 );

	public static Point2 GetDesktopSize( int displayIndex )
	{
		SDL_DisplayMode mode;
		return Sdl2Native.SDL_GetDesktopDisplayMode( displayIndex, &mode ) == 0 ? new Point2( mode.w, mode.h ) : new Point2( 0, 0 );
	}

	/// <summary>Distinct sizes of the display's fullscreen modes (empty if SDL cannot list them).</summary>
	public static IReadOnlyList<Point2> GetDisplayModeSizes( int displayIndex )
	{
		var count = getNumDisplayModes.Value?.Invoke( displayIndex ) ?? 0;
		var sizes = new List<Point2>();
		for ( var index = 0; index < count && getDisplayMode.Value != null; index++ )
		{
			SDL_DisplayMode mode;
			if ( getDisplayMode.Value( displayIndex, index, &mode ) == 0 )
				sizes.Add( new Point2( mode.w, mode.h ) );
		}
		return sizes.Distinct().ToArray();
	}

	/// <summary>
	/// Selects the fullscreen mode SDL uses for exclusive fullscreen. Returns false (with a reason) when
	/// the display does not list the requested size.
	/// </summary>
	public static bool TrySetExclusiveMode( Sdl2Window window, Point2 size, out string reason )
	{
		reason = "";
		if ( getNumDisplayModes.Value == null || getDisplayMode.Value == null || setWindowDisplayMode.Value == null )
		{
			reason = "this SDL2 build cannot list or set display modes";
			return false;
		}
		var display = GetWindowDisplayIndex( window );
		var count = getNumDisplayModes.Value( display );
		for ( var index = 0; index < count; index++ )
		{
			SDL_DisplayMode mode;
			if ( getDisplayMode.Value( display, index, &mode ) != 0 || mode.w != size.X || mode.h != size.Y )
				continue;
			if ( setWindowDisplayMode.Value( window.SdlWindowHandle, &mode ) == 0 )
				return true;
			reason = "SDL_SetWindowDisplayMode failed";
			return false;
		}
		reason = $"display {display} has no {size.X}x{size.Y} mode";
		return false;
	}

	public static void SetMinimumSize( Sdl2Window window, int width, int height ) =>
		setWindowMinimumSize.Value?.Invoke( window.SdlWindowHandle, width, height );
}
