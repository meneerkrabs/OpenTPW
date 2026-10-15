using System.Globalization;

namespace OpenTPW;

/// <summary>
/// Logical window size (desktop units used by SDL input) and the drawable size in pixels. On HiDPI
/// displays (Retina) the pixel size is larger; input, picking and <see cref="UI.Panel"/> layout stay
/// in logical units, rendertargets and BF4 UI use pixels.
/// </summary>
public readonly record struct DisplayMetrics( Point2 LogicalSize, Point2 PixelSize )
{
	public bool IsEmpty => LogicalSize.X <= 0 || LogicalSize.Y <= 0 || PixelSize.X <= 0 || PixelSize.Y <= 0;

	/// <summary>Pixels per logical unit per axis (1 when the size is empty).</summary>
	public System.Numerics.Vector2 PixelsPerLogical => IsEmpty
		? System.Numerics.Vector2.One
		: new( (float)PixelSize.X / LogicalSize.X, (float)PixelSize.Y / LogicalSize.Y );

	/// <summary>Whole drawable pixels per logical unit (2 on Retina, 1 without HiDPI or when empty).</summary>
	public int IntegerPixelDensity => IsEmpty ? 1 : Math.Max( 1, Math.Min( PixelSize.X / LogicalSize.X, PixelSize.Y / LogicalSize.Y ) );

	public System.Numerics.Vector2 LogicalToPixel( System.Numerics.Vector2 logical ) => logical * PixelsPerLogical;

	public System.Numerics.Vector2 PixelToLogical( System.Numerics.Vector2 pixel ) => pixel / PixelsPerLogical;

	public override string ToString() => PixelSize.Equals( LogicalSize )
		? $"{PixelSize.X}x{PixelSize.Y}"
		: $"{PixelSize.X}x{PixelSize.Y} pixels ({LogicalSize.X}x{LogicalSize.Y} logical)";
}

/// <summary>Result of <see cref="RenderScaling.Compute"/>: what the renderer actually allocates.</summary>
public readonly record struct RenderScaleResult(
	UpscaleMode RequestedMode, int RequestedPercent,
	UpscaleMode Mode, int EffectivePercent,
	Point2 InternalSize, Point2 OutputSize,
	string? FallbackReason )
{
	/// <summary>Zero-size output (minimised window): render nothing, allocate nothing.</summary>
	public bool IsPaused => OutputSize.X <= 0 || OutputSize.Y <= 0;

	public string Describe()
	{
		var text = $"Upscaling: {Mode}";
		if ( Mode != RequestedMode || EffectivePercent != RequestedPercent )
			text += $" (requested {RequestedMode} {RequestedPercent}%)";
		text += $"; world scale {EffectivePercent}%; internal {InternalSize.X}x{InternalSize.Y}; output {OutputSize.X}x{OutputSize.Y}";
		if ( FallbackReason != null )
			text += $"; fallback: {FallbackReason}";
		return text + ".";
	}
}

/// <summary>
/// Internal (3D world) target size from the output size and render scale. Percentages apply to each
/// dimension, rounded to the nearest pixel, at least one pixel, at most the device texture limit.
/// The camera keeps the output aspect, so rounding cannot stretch the image (see <see cref="RoundingErrorInInternalPixels"/>).
/// </summary>
public static class RenderScaling
{
	/// <summary>OpenTPW presets (our choice, not vendor quality labels or original values).</summary>
	public static readonly int[] Presets = [77, 67, 59, 50];
	public const int DefaultPreset = 67;

	public static RenderScaleResult Compute( Point2 output, UpscaleMode mode, int requestedPercent, uint maximumTextureSize, bool worldScalingAllowed = true )
	{
		string? reason = null;
		var effectiveMode = mode;
		var percent = mode == UpscaleMode.Native ? 100 : requestedPercent;
		if ( !Enum.IsDefined( mode ) )
		{
			reason = $"unknown upscale mode {(int)mode}";
			effectiveMode = UpscaleMode.Native;
			percent = 100;
		}
		else if ( mode != UpscaleMode.Native && (percent < DisplaySettings.MinimumRenderScale || percent > DisplaySettings.MaximumRenderScale) )
		{
			reason = $"render scale {requestedPercent}% is outside {DisplaySettings.MinimumRenderScale}-{DisplaySettings.MaximumRenderScale}%";
			effectiveMode = UpscaleMode.Native;
			percent = 100;
		}
		else if ( mode != UpscaleMode.Native && !worldScalingAllowed )
		{
			reason = "this screen has no scalable 3D world";
			effectiveMode = UpscaleMode.Native;
			percent = 100;
		}
		if ( output.X <= 0 || output.Y <= 0 )
			return new( mode, requestedPercent, effectiveMode, percent, new Point2( 0, 0 ), new Point2( Math.Max( 0, output.X ), Math.Max( 0, output.Y ) ), reason ?? "zero-size output; rendering paused" );

		var limit = (int)Math.Clamp( maximumTextureSize == 0 ? int.MaxValue : maximumTextureSize, 1u, int.MaxValue );
		var width = ScaleDimension( output.X, percent );
		var height = ScaleDimension( output.Y, percent );
		if ( width > limit || height > limit )
		{
			// Keep the aspect while fitting the device limit.
			var fit = Math.Min( (double)limit / width, (double)limit / height );
			width = Math.Clamp( (int)Math.Floor( width * fit ), 1, limit );
			height = Math.Clamp( (int)Math.Floor( height * fit ), 1, limit );
			reason ??= $"device texture limit {limit}";
			if ( effectiveMode == UpscaleMode.Native )
				effectiveMode = UpscaleMode.Linear;
		}
		return new( mode, requestedPercent, effectiveMode, percent, new Point2( width, height ), output, reason );
	}

	/// <summary>round(size × percent / 100), at least one pixel.</summary>
	public static int ScaleDimension( int size, int percent ) =>
		Math.Max( 1, (int)((size * (long)percent + 50) / 100) );

	/// <summary>
	/// Largest per-axis rounding error in internal pixels (at most 0.5). The camera keeps the output
	/// aspect and the blit maps the whole target onto the whole output, so rounding never stretches
	/// the image; it only makes the world sample grid this slightly anisotropic.
	/// </summary>
	public static double RoundingErrorInInternalPixels( Point2 output, Point2 internalSize, int percent ) =>
		Math.Max( Math.Abs( internalSize.X - output.X * percent / 100.0 ), Math.Abs( internalSize.Y - output.Y * percent / 100.0 ) );

	public static string DescribePresets() => string.Join( ", ", Presets.Select( value => value.ToString( CultureInfo.InvariantCulture ) + "%" ) );
}

/// <summary>
/// BF4 UI scale policy: integer scales only, so point-sampled original fonts stay pixel-exact. The
/// automatic scale is the largest integer at which a 1280×720 layout still fits the output pixels:
/// 1 up to 2559×1439, 2 at 2560×1440, 3 at 3840×2160 (4K). On HiDPI outputs (integer pixel density
/// d &gt; 1, e.g. Retina d = 2) it is at least d × the same fit measured in logical units, so a HiDPI
/// window gets the same physical text size as a 1× window of the same logical size (a Retina window
/// of 1216×684 points uses 2, not 1).
/// </summary>
public static class UiScaling
{
	public const int ReferenceWidth = 1280;
	public const int ReferenceHeight = 720;

	public static int Automatic( Point2 pixelSize, int pixelDensity = 1 )
	{
		var density = Math.Max( 1, pixelDensity );
		var pixelFit = Math.Min( pixelSize.X / ReferenceWidth, pixelSize.Y / ReferenceHeight );
		var logicalFit = Math.Max( 1, Math.Min( pixelSize.X / density / ReferenceWidth, pixelSize.Y / density / ReferenceHeight ) );
		return Math.Clamp( Math.Max( pixelFit, density * logicalFit ), 1, DisplaySettings.MaximumUiScale );
	}

	public static int Resolve( int configured, Point2 pixelSize, int pixelDensity = 1 ) =>
		configured >= 1 && configured <= DisplaySettings.MaximumUiScale ? configured : Automatic( pixelSize, pixelDensity );
}

/// <summary>Resolution list for the display settings.</summary>
public static class DisplayModes
{
	/// <summary>The original game's resolution options (UIStrings Resolution400x300 .. Resolution1600x1200).</summary>
	public static readonly Point2[] Original = [new( 400, 300 ), new( 512, 384 ), new( 640, 480 ), new( 800, 600 ), new( 1024, 768 ), new( 1280, 1024 ), new( 1600, 1200 )];

	/// <summary>Common windowed sizes offered when the display reports few modes (any size is still allowed).</summary>
	public static readonly Point2[] Common = [new( 1280, 720 ), new( 1366, 768 ), new( 1600, 900 ), new( 1920, 1080 ), new( 2560, 1080 ), new( 2560, 1440 ), new( 3440, 1440 ), new( 3840, 2160 )];

	/// <summary>
	/// Distinct sizes from the display's modes plus original/common sizes that fit the desktop,
	/// sorted by area then width. Sizes below the minimum window size are dropped.
	/// </summary>
	public static IReadOnlyList<Point2> Build( IEnumerable<Point2> displayModes, Point2 desktop ) =>
		Build( displayModes, desktop, [], WindowMode.Windowed );

	/// <summary>
	/// Sizes for <paramref name="mode"/>. Exclusive fullscreen: only the display's modes. Windowed and
	/// borderless: the modes plus <paramref name="current"/> (window/drawable sizes) plus the original
	/// and common sizes that fit the desktop (all of them when the desktop size is unknown).
	/// </summary>
	public static IReadOnlyList<Point2> Build( IEnumerable<Point2> displayModes, Point2 desktop, IEnumerable<Point2> current, WindowMode mode )
	{
		var fits = ( Point2 size ) => desktop.X <= 0 || desktop.Y <= 0 || (size.X <= desktop.X && size.Y <= desktop.Y);
		var sizes = mode == WindowMode.Exclusive
			? displayModes
			: displayModes.Concat( current ).Concat( Original.Where( fits ) ).Concat( Common.Where( fits ) );
		return sizes
			.Where( size => size.X >= DisplaySettings.MinimumWindowWidth && size.Y >= DisplaySettings.MinimumWindowHeight
				&& size.X <= DisplaySettings.MaximumWindowSize && size.Y <= DisplaySettings.MaximumWindowSize )
			.Distinct()
			.OrderBy( size => (long)size.X * size.Y ).ThenBy( size => size.X )
			.ToArray();
	}
}

/// <summary>World ↔ screen mapping shared by picking checks: output pixels versus logical mouse units.</summary>
public static class ScreenMapping
{
	/// <summary>Projects a world point to output-pixel coordinates (origin top-left).</summary>
	public static bool TryProjectToPixel( System.Numerics.Vector3 world, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection, Point2 pixelSize, out System.Numerics.Vector2 pixel )
	{
		pixel = default;
		var clip = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( world, 1 ), view * projection );
		if ( clip.W <= 0 || pixelSize.X <= 0 || pixelSize.Y <= 0 )
			return false;
		var x = clip.X / clip.W;
		var y = clip.Y / clip.W;
		pixel = new System.Numerics.Vector2( (x + 1) / 2 * pixelSize.X, (1 - y) / 2 * pixelSize.Y );
		return float.IsFinite( pixel.X ) && float.IsFinite( pixel.Y );
	}
}
