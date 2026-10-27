using Veldrid;

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
	// [APPROX:UI-060] one bitmap pixel remains two canvas units, at least one framebuffer pixel. Original display-mode calibration,
	// finance sample/month correspondence, palette, fonts and composited raster fidelity remain unqualified.
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
}
