using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>Immutable graph geometry in bitmap pixels, shared by the finance and park-information backdrops.</summary>
internal readonly record struct GraphBackdrop( int Width, int Height, int Samples, long Minimum = 0, long Maximum = 0, int Divisions = 10 )
{
	internal static readonly RgbaByte Background = new( 0, 0, 0, 255 );
	internal static readonly RgbaByte Grid = new( 0, 100, 0, 255 );
	internal static readonly RgbaByte Axis = new( 0, 200, 0, 255 );

	// [BIN:TPW-EXE:0x4806A0 graph setup] both windows use the same bitmap inset and opaque colour records.
	internal static (int Left, int Top, int Right, int Bottom) PlotBounds( int width, int height ) => (4, 4, width - 20, height - 20);
	internal (int Left, int Top, int Right, int Bottom) Plot => PlotBounds( Width, Height );
	internal UiRect PlotFill => new( Plot.Left, Plot.Top, Plot.Right - Plot.Left, Plot.Bottom - Plot.Top );
	internal static int GridInterval( int samples ) => samples switch { 12 => 1, 36 => 3, 144 => 12, _ => 0 };

	// Host guards: the native invalid-size/unsupported-period paths are not qualified. Draw only the background in those cases.
	internal bool CanPlot => Width > 24 && Height > 24 && GridInterval( Samples ) != 0;

	internal static IEnumerable<int> GridSteps( int samples )
	{
		var every = GridInterval( samples );
		if ( every == 0 ) yield break;
		for ( var step = every; step <= samples; step += every ) yield return step;
	}

	internal IEnumerable<(UiRect Span, int Step, UiRect Label)> VerticalLines()
	{
		if ( !CanPlot || Divisions <= 0 ) yield break;
		var plot = Plot;
		var every = GridInterval( Samples );
		var dx = (float)((double)(plot.Right - plot.Left) / Samples);
		var x = plot.Left + dx;
		// [BIN:TPW-EXE:0x47FCD0 grid columns] store each sample advance as a float, including unlabelled steps; truncate only the emitted column.
		for ( var step = 1; step <= Samples; step++ )
		{
			if ( step % every == 0 )
			{
				var column = (int)x;
				// [BIN:TPW-EXE:0x5F98D0 vertical span] the third argument is a pixel count, not a bottom endpoint.
				yield return (new UiRect( column, plot.Top, 1, plot.Bottom ), step,
					new UiRect( Math.Max( 2, column ), Math.Max( 2, plot.Bottom + 4 ), 40, 12 ));
			}
			x = (float)(x + dx);
		}
	}

	internal IEnumerable<UiRect> HorizontalLines()
	{
		if ( !CanPlot || Divisions <= 0 ) yield break;
		var plot = Plot;
		var dy = (float)((double)(plot.Bottom - plot.Top) / Divisions);
		// The initial expression has one final float store; an intermediate float subtraction can move a row.
		var y = (float)(plot.Bottom - (double)dy + 1d);
		for ( var division = 0; division < Divisions; division++ )
		{
			// [BIN:TPW-EXE:0x5F9860 horizontal span] likewise, Right is the count (intentionally extending past the plot).
			yield return new UiRect( plot.Left, (int)y, plot.Right, 1 );
			y = (float)(y - dy);
		}
	}

	internal int ZeroAxisRow
	{
		get
		{
			var plot = Plot;
			// [BIN:TPW-EXE:0x47FC50 zero axis] negative minima omit the nonnegative branch's extra bottom pixel.
			// Host scales include zero. Signed 128-bit arithmetic preserves their full long domain without native 32-bit wrap or floating-point cancellation.
			// A negative-only maximum is not a supported host scale; retain the flat/bottom fallback rather than overflow a coordinate.
			return Minimum < 0 && Maximum >= 0
				? plot.Bottom + (int)((Int128)(plot.Bottom - plot.Top) * Minimum / ((Int128)Maximum - Minimum))
				: plot.Bottom + 1;
		}
	}

	internal UiRect HorizontalAxis => new( Plot.Left - 1, ZeroAxisRow, Plot.Right + 1, 1 );
	internal UiRect VerticalAxis => new( Plot.Left - 1, Plot.Top - 1, 1, Plot.Bottom + 1 );
}

/// <summary>Adapts the immutable bitmap backdrop to the host UI batch; it never reads graph histories or changes the park.</summary>
internal static class GraphBackdropRenderer
{
	// [EXT:graph-pixel-adapter] one bitmap pixel is two canvas units, at least one framebuffer pixel: the original draws at its display mode's pixel size, which
	// follows the host's resolution, and this is the adapter (user-approved 2026-10-30).
	internal static float Pixel( UiCanvas canvas ) => Math.Max( 1f, canvas.Scale * 2f );

	internal static UiRect ToScreen( UiRect bitmap, UiRect frame, float pixel ) =>
		new( frame.X + bitmap.X * pixel, frame.Y + bitmap.Y * pixel, bitmap.Width * pixel, bitmap.Height * pixel );

	/// <summary>Returns whether the caller may draw series; invalid frames and background-only fallbacks return false.</summary>
	internal static bool Draw( UiContext context, UiRect frame, float pixel, GraphBackdrop backdrop )
	{
		if ( !float.IsFinite( frame.X ) || !float.IsFinite( frame.Y ) || !float.IsFinite( frame.Width ) || !float.IsFinite( frame.Height )
			|| !float.IsFinite( frame.Right ) || !float.IsFinite( frame.Bottom )
			|| frame.Width <= 0 || frame.Height <= 0 || !float.IsFinite( pixel ) || pixel <= 0 )
			return false;
		void Fill( UiRect bitmap, RgbaByte colour ) => context.Batch.AddRectangle( ToScreen( bitmap, frame, pixel ), colour );
		// [BIN:TPW-EXE:0x480810 backdrop order] bitmap clear, plot fill, grid and its labels, axes, then the caller draws its series.
		// Clear the full host control, including a possible fractional-pixel remainder after bitmap-size truncation.
		context.Batch.AddRectangle( frame, GraphBackdrop.Background );
		if ( !backdrop.CanPlot ) return false;
		Fill( backdrop.PlotFill, GraphBackdrop.Background );
		foreach ( var (span, step, label) in backdrop.VerticalLines() )
		{
			Fill( span, GraphBackdrop.Grid );
			// Existing Small-font/centred text-box adapter; these glyphs do not establish native text raster equivalence.
			context.DrawText( context.Fonts.Small, step.ToString( System.Globalization.CultureInfo.InvariantCulture ),
				ToScreen( label, frame, pixel ), GraphBackdrop.Axis, UiAlign.Left, false );
		}
		foreach ( var span in backdrop.HorizontalLines() ) Fill( span, GraphBackdrop.Grid );
		Fill( backdrop.HorizontalAxis, GraphBackdrop.Axis );
		Fill( backdrop.VerticalAxis, GraphBackdrop.Axis );
		return true;
	}

	/// <summary>The row of a value: bottom less (value - minimum) x height / (maximum - minimum), cut to the plot; the bottom for an empty range.</summary>
	// [BIN:TPW-EXE:0x47FE90 graph drawer] with a zero range ([+0x24] - [+0x1C]) both ends of a segment sit on the bottom row ([+0x14]); otherwise y = bottom - ((v - min) * [+4]) / range in integers, then cut to top ([+0xC]) .. bottom
	// [APPROX:UI-060] the original multiplies in 32 bits (imul/idiv at 0x47FEEE, 0x47FF03), so a financial span above about 8.9 million wraps there; OpenTPW keeps 128-bit arithmetic for the long ledger values (a host safety adaptation)
	internal static int Row( long value, long minimum, long maximum, (int Left, int Top, int Right, int Bottom) plot )
	{
		var range = (Int128)maximum - minimum;
		if ( range == 0 )
			return plot.Bottom;
		var y = plot.Bottom - (int)((Int128)(plot.Bottom - plot.Top) * ((Int128)value - minimum) / range);
		return Math.Clamp( y, plot.Top, plot.Bottom );
	}

	/// <summary>The column of the segment end <paramref name="index"/>: the left plus index steps of (width / samples) as a float, summed as the drawer does.</summary>
	// [BIN:TPW-EXE:0x47FE90 graph drawer] x starts at the left ([+8]) as a float and each segment adds the step (width / samples stored as a float, 0x47FEAF) to the x before it was cut to the plot, both ends cut to left .. right and truncated (0x67A830)
	internal static int Column( int index, int samples, (int Left, int Top, int Right, int Bottom) plot )
	{
		var step = (float)((double)(plot.Right - plot.Left) / samples);
		var x = (float)plot.Left;
		for ( var k = 0; k < index; k++ )
			x = (float)(step + Math.Clamp( x, plot.Left, plot.Right ));
		return (int)Math.Clamp( x, plot.Left, plot.Right );
	}

	/// <summary>
	/// Draws one series of <paramref name="points"/> (oldest first, the segment count plus one) as the graph drawer 0x47FE90 does: a line per segment between whole
	/// bitmap pixels, a hollow box round every segment end when the period has fewer than 144 samples, and the last value written at the line's end.
	/// </summary>
	internal static void DrawSeries( UiContext context, ReadOnlySpan<long> points, int samples, long minimum, long maximum,
		(int Left, int Top, int Right, int Bottom) plot, RgbaByte colour, UiRect rect, float pixel, FontAtlas font )
	{
		NVector2 At( int column, int row ) => new( rect.X + column * pixel, rect.Y + row * pixel );
		void Line( NVector2 from, NVector2 to )
		{
			var delta = to - from;
			if ( delta.LengthSquared() < 0.01f )
			{
				context.Batch.AddRectangle( new UiRect( from.X, from.Y, pixel, pixel ), colour );
				return;
			}
			var normal = NVector2.Normalize( new NVector2( -delta.Y, delta.X ) ) * pixel / 2;
			var a = new UiVertex( from + normal, NVector2.Zero, colour );
			var b = new UiVertex( from - normal, NVector2.Zero, colour );
			var c = new UiVertex( to + normal, NVector2.Zero, colour );
			var d = new UiVertex( to - normal, NVector2.Zero, colour );
			context.Batch.AddTriangle( UiTexture.Solid, a, b, c );
			context.Batch.AddTriangle( UiTexture.Solid, c, b, d );
		}
		var step = (float)((double)(plot.Right - plot.Left) / samples);
		var x = (float)plot.Left;
		var lastColumn = plot.Left;
		var lastRow = samples;
		for ( var index = 0; index < points.Length - 1; index++ )
		{
			// [BIN:TPW-EXE:0x47FF1C segment ends] the far end is the x plus the step (a float), the next x the cut near end plus the step
			var near = Math.Clamp( x, plot.Left, plot.Right );
			var far = Math.Clamp( (float)(step + x), plot.Left, plot.Right );
			x = (float)(step + near);
			var fromColumn = (int)near;
			var toColumn = (int)far;
			var fromRow = Row( points[index], minimum, maximum, plot );
			var toRow = Row( points[index + 1], minimum, maximum, plot );
			Line( At( fromColumn, fromRow ), At( toColumn, toRow ) );
			// [BIN:TPW-EXE:0x47FFE0 sample marks] with fewer than 0x90 samples each segment end gets a hollow box of four one-pixel lines round it
			if ( samples < 0x90 )
			{
				Line( At( toColumn - 1, toRow - 1 ), At( toColumn + 1, toRow - 1 ) );
				Line( At( toColumn + 1, toRow - 1 ), At( toColumn + 1, toRow + 1 ) );
				Line( At( toColumn + 1, toRow + 1 ), At( toColumn - 1, toRow + 1 ) );
				Line( At( toColumn - 1, toRow + 1 ), At( toColumn - 1, toRow - 1 ) );
			}
			lastColumn = toColumn;
			lastRow = toRow;
		}
		// [BIN:TPW-EXE:0x47FE90 last value] the series' last value is written ("%d", 0x74F9D8) right-aligned against the end of the line, from 6 pixels above its row (2 at the least); a series with no segment writes it at the plot's left, 6 less than the sample count down
		var label = points[^1].ToString( System.Globalization.CultureInfo.InvariantCulture );
		var top = Math.Max( 2, lastRow - 6 );
		var width = Math.Max( context.Measure( font, label ).Width, 1f ) / pixel;
		var right = Math.Max( lastColumn, plot.Left + width );
		context.DrawText( font, label, new UiRect( rect.X + (right - width - 1) * pixel, rect.Y + top * pixel, (width + 2) * pixel, 12 * pixel ), colour, UiAlign.Right, false );
	}
}
