using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>The five series of the park information window's graph, as bits of the series mask (tp.exe's <c>0x753C48</c>).</summary>
[Flags]
public enum ParkInfoSeries
{
	None = 0,
	/// <summary>The guests the bus dropped off in a month (toggle 0x4A68BC, UITEXT 0x98).</summary>
	Arrivals = 1,
	/// <summary>The mean happiness (toggle 0x4A68BD, UITEXT 0x99).</summary>
	Happiness = 2,
	/// <summary>The mean stay in days (toggle 0x4A68BE, UITEXT 0x9A).</summary>
	Stay = 4,
	/// <summary>The guests in the park (toggle 0x4A68BF, UITEXT 0x97).</summary>
	People = 8,
	/// <summary>The park rating (toggle 0x4A68C0, UITEXT 0x9B).</summary>
	Rating = 0x10
}

/// <summary>The park information window's graph: what it reads from the history and where it draws it (tp.exe <c>0x4A4B90</c>, <c>0x4A4750</c>, <c>0x480810</c>).</summary>
public static class ParkInfoGraph
{
	/// <summary>The series mask the window starts with: the guests in the park (<c>0x753C48</c> = 8).</summary>
	// [BIN:TPW-EXE:0x753C48 series mask] the mask is initialised to 8 (only the people-in-park toggle, 0x4A68BF) and kept by the window procedure across openings (for the process; OpenTPW keeps it within a park, UI-914); the builder presses a toggle for each set bit (0x4A52A0)
	public const ParkInfoSeries InitialSeries = ParkInfoSeries.People;

	/// <summary>The period the window starts with: 1 year (<c>0x7CB39C</c> = 0).</summary>
	public const int InitialPeriod = 0;

	/// <summary>The line colour of each series.</summary>
	// [BIN:TPW-EXE:0x4A45E0 series colours] the five colour records the series' line drawer reads (0x7CB368 arrivals, 0x7CB380 happiness, 0x7CB308 stay, 0x7CB390 people, 0x7CB370 rating) are set as the finance graph's: magenta (0xFF, 0, 0xFF), red (0xFF, 0, 0), (0, 0xBD, 0xEC), yellow (0xFF, 0xFF, 0) and (0xFF, 0x90, 0)
	public static RgbaByte Colour( ParkInfoSeries series ) => series switch
	{
		ParkInfoSeries.Arrivals => new( 255, 0, 255, 255 ),
		ParkInfoSeries.Happiness => new( 255, 0, 0, 255 ),
		ParkInfoSeries.Stay => new( 0, 189, 236, 255 ),
		ParkInfoSeries.People => new( 255, 255, 0, 255 ),
		_ => new( 255, 144, 0, 255 )
	};

	/// <summary>The series in the order the line drawer draws them (the later over the earlier).</summary>
	// [BIN:TPW-EXE:0x4A4750 series order] arrivals (bit 1), happiness (bit 2, the single ring 0x480570), stay (bit 4), people (bit 8) and rating (bit 0x10), each through 0x480460 with its colour record
	public static readonly ParkInfoSeries[] DrawOrder = { ParkInfoSeries.Arrivals, ParkInfoSeries.Happiness, ParkInfoSeries.Stay, ParkInfoSeries.People, ParkInfoSeries.Rating };

	/// <summary>The dedicated statistics ring a series reads, including rating independently of financial history.</summary>
	public static HistoryRing Ring( ParkInfoSeries series, ParkStatisticsHistory history ) => series switch
	{
		ParkInfoSeries.Arrivals => history.Arrivals,
		ParkInfoSeries.Happiness => history.Happiness,
		ParkInfoSeries.Stay => history.Stay,
		ParkInfoSeries.People => history.People,
		_ => history.Rating
	};

	/// <summary>The scale maximum of the selected series: the highest of their rings' maxima, never below 0 (the graph's scale runs 0 to this in ten divisions).</summary>
	// [BIN:TPW-EXE:0x4A4B90 scale] after a toggle or a period change the window proc folds 0x4801D0 / 0x4802A0 over the selected rings into one maximum that starts at 0 and sets the graph's scale with 0x47FB30(0, maximum, 10)
	public static int ScaleMaximum( ParkInfoSeries selected, int period, Func<ParkInfoSeries, HistoryRing> rings )
	{
		var maximum = 0;
		foreach ( var series in DrawOrder )
			if ( selected.HasFlag( series ) )
				maximum = Math.Max( maximum, ParkStatisticsHistory.SeriesMaximum( rings( series ), period, series == ParkInfoSeries.Happiness ) );
		return maximum;
	}

	// ---- geometry, in the graph bitmap's pixels (the control's inner rectangle at the display mode's size) ------------------------------

	/// <summary>The plot's rectangle in the bitmap: 4 pixels from the left and top, 20 from the right and bottom.</summary>
	// [BIN:TPW-EXE:0x4806A0 graph setup] 0x47FB50(4, 4, width - 20, height - 20) sets the plot's left, top, right and bottom (width and height of the control's rectangle at the display mode's pixel size)
	public static (int Left, int Top, int Right, int Bottom) Plot( int widthPixels, int heightPixels ) => GraphBackdrop.PlotBounds( widthPixels, heightPixels );

	/// <summary>The row of a value: the bottom less (value x height / maximum), cut to the plot, the bottom for a flat range.</summary>
	// [BIN:TPW-EXE:0x47FE90 graph drawer] y = bottom - ((value - minimum) * height) / (maximum - minimum) in integers, bottom for an empty range, then cut to top..bottom
	public static int Row( int value, int maximum, (int Left, int Top, int Right, int Bottom) plot )
	{
		var height = plot.Bottom - plot.Top;
		var y = maximum == 0 ? plot.Bottom : plot.Bottom - (int)((long)value * height / maximum);
		return Math.Clamp( y, plot.Top, plot.Bottom );
	}

	/// <summary>The column of sample <paramref name="index"/>: the left plus index steps of width / samples.</summary>
	// [BIN:TPW-EXE:0x47FE90 graph drawer] the line starts at the plot's left and advances width / samples (a float, 0xC, 0x24 or 0x90) per sample, truncated for each segment end
	public static int Column( int index, int samples, (int Left, int Top, int Right, int Bottom) plot ) =>
		(int)(plot.Left + (float)(plot.Right - plot.Left) / samples * index);

	/// <summary>The vertical gridlines: the sample steps that get a line and the number written under it (every step for a year, every third for 3 years, every twelfth for 12).</summary>
	// [BIN:TPW-EXE:0x47FCD0 graph grid] a line and its label (the step number, from 1) at the end of every step whose count passes 0, 2 or 11 (12, 36 and 144 samples); the horizontal lines are the ten divisions of the height
	public static IEnumerable<int> GridSteps( int samples ) => GraphBackdrop.GridSteps( samples );
}

public sealed partial class ParkHud
{
	/// <summary>The graph control: the black plot, its green axes and dark green grid, and the selected series.</summary>
	private sealed class ParkInfoGraphElement( ParkHud owner ) : UiElement
	{
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var rect = ScreenRect( context.Canvas );
			var pixel = GraphBackdropRenderer.Pixel( context.Canvas );
			var samples = ParkStatisticsHistory.PeriodSamples( owner.parkInfoPeriod );
			var backdrop = new GraphBackdrop( (int)(rect.Width / pixel), (int)(rect.Height / pixel), samples );
			var canDrawSeries = GraphBackdropRenderer.Draw( context, rect, pixel, backdrop );
			var plot = backdrop.Plot;
			var economy = owner.Status.EconomySource as ParkEconomy;
			if ( economy == null || !canDrawSeries )
				return;
			var font = context.Fonts.Small;
			HistoryRing Ring( ParkInfoSeries series ) => ParkInfoGraph.Ring( series, economy.StatisticsHistory );
			var maximum = ParkInfoGraph.ScaleMaximum( owner.parkInfoSeries, owner.parkInfoPeriod, Ring );
			foreach ( var series in ParkInfoGraph.DrawOrder )
			{
				if ( !owner.parkInfoSeries.HasFlag( series ) )
					continue;
				DrawSeries( context, ParkStatisticsHistory.SeriesPoints( Ring( series ), owner.parkInfoPeriod, series == ParkInfoSeries.Happiness ), samples, maximum, plot, ParkInfoGraph.Colour( series ), (rect, pixel), font );
			}
		}

		private static void DrawSeries( UiContext context, int[] points, int samples, int maximum, (int Left, int Top, int Right, int Bottom) plot, RgbaByte colour, (UiRect Rect, float Pixel) frame, FontAtlas font )
		{
			var (rect, pixel) = frame;
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
			var n = points.Length - 1;
			var lastColumn = plot.Left;
			var lastRow = samples;
			for ( var index = 0; index < n; index++ )
			{
				var fromColumn = ParkInfoGraph.Column( index, samples, plot );
				var toColumn = ParkInfoGraph.Column( index + 1, samples, plot );
				var fromRow = ParkInfoGraph.Row( points[index], maximum, plot );
				var toRow = ParkInfoGraph.Row( points[index + 1], maximum, plot );
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
			context.DrawText( font, label, new UiRect( rect.X + (right - width - 1) * pixel, rect.Y + top * pixel, (width + 2) * pixel, 12 * pixel ), colour, UiAlign.Right, false, false );
		}
	}
}
