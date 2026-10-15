namespace OpenTPW;

/// <summary>
/// Output metrics. <see cref="Size"/> is the logical window size used by input, picking and
/// <see cref="Point2"/>-based panel layout; <see cref="PixelSize"/> is the drawable (swapchain) size.
/// </summary>
public static class Screen
{
	public static Point2 Size { get; set; } = new( 1, 1 );

	/// <summary>Drawable size in pixels (equal to <see cref="Size"/> without HiDPI).</summary>
	public static Point2 PixelSize { get; set; } = new( 1, 1 );

	/// <summary>Whole drawable pixels per logical unit (2 on Retina, 1 without HiDPI).</summary>
	public static int PixelDensity => Size.X > 0 && Size.Y > 0 ? Math.Max( 1, Math.Min( PixelSize.X / Size.X, PixelSize.Y / Size.Y ) ) : 1;

	/// <summary>Integer scale for pixel-exact BF4 UI (see UiScaling).</summary>
	public static int UiScale { get; set; } = 1;

	public static float Width => Size.X;
	public static float Height => Size.Y;

	/// <summary>Output aspect; the camera follows it whatever the world render scale is.</summary>
	public static float Aspect => PixelSize.X > 0 && PixelSize.Y > 0
		? (float)PixelSize.X / PixelSize.Y
		: Size.Y > 0 ? (float)Size.X / Size.Y : 1;

	public static void UpdateFrom( Point2 size ) => UpdateFrom( size, size );

	public static void UpdateFrom( Point2 size, Point2 pixelSize )
	{
		Size = size;
		PixelSize = pixelSize;
	}
}
