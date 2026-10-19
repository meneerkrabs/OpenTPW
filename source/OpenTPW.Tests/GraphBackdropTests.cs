using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Synthetic bitmap arithmetic fixtures; these do not establish original display-mode or rendered parity.</summary>
[TestClass]
public class GraphBackdropTests
{
	[DataTestMethod]
	[DataRow( 448, 268, 428, 248 )]
	[DataRow( 445, 263, 425, 243 )]
	public void BothGraphsShareTheBitmapInsetWithoutChangingSeriesCoordinates( int width, int height, int right, int bottom )
	{
		var backdrop = new GraphBackdrop( width, height, 12 );
		Assert.AreEqual( (4, 4, right, bottom), backdrop.Plot );
		Assert.AreEqual( backdrop.Plot, ParkInfoGraph.Plot( width, height ) );
		Assert.AreEqual( new UiRect( 4, 4, right - 4, bottom - 4 ), backdrop.PlotFill );
		Assert.AreEqual( bottom, ParkInfoGraph.Row( 0, 100, backdrop.Plot ) );
		Assert.AreEqual( bottom, ParkInfoGraph.Row( 7, 0, backdrop.Plot ) );
		Assert.AreEqual( 4, ParkInfoGraph.Row( 999, 100, backdrop.Plot ) );
		Assert.AreEqual( 4, ParkInfoGraph.Column( 0, 12, backdrop.Plot ) );
		Assert.AreEqual( right, ParkInfoGraph.Column( 12, 12, backdrop.Plot ) );
	}

	[TestMethod]
	public void BackdropColorsAreOpaqueAndIndependentOfSeriesColours()
	{
		Assert.AreEqual( new Veldrid.RgbaByte( 0, 0, 0, 255 ), GraphBackdrop.Background );
		Assert.AreEqual( new Veldrid.RgbaByte( 0, 100, 0, 255 ), GraphBackdrop.Grid );
		Assert.AreEqual( new Veldrid.RgbaByte( 0, 200, 0, 255 ), GraphBackdrop.Axis );
	}

	[DataTestMethod]
	[DataRow( 12 )]
	[DataRow( 36 )]
	[DataRow( 144 )]
	public void EverySampleAdvancesTheStoredFloatBeforeTruncatingLabeledColumns( int samples )
	{
		var expectedColumns = samples switch
		{
			12 => new[] { 39, 74, 110, 145, 180, 215, 251, 286, 322, 357, 392, 428 },
			36 => new[] { 39, 74, 110, 145, 180, 215, 251, 286, 321, 357, 392, 427 },
			_ => new[] { 39, 74, 109, 145, 180, 215, 251, 286, 322, 357, 392, 428 }
		};
		var backdrop = new GraphBackdrop( 448, 268, samples );
		var lines = backdrop.VerticalLines().ToArray();
		CollectionAssert.AreEqual( expectedColumns, lines.Select( line => (int)line.Span.X ).ToArray() );
		var steps = Enumerable.Range( 1, 12 ).Select( n => n * (samples / 12) ).ToArray();
		CollectionAssert.AreEqual( steps, lines.Select( line => line.Step ).ToArray() );
		CollectionAssert.AreEqual( steps, ParkInfoGraph.GridSteps( samples ).ToArray() );
		for ( var index = 0; index < 12; index++ )
		{
			Assert.AreEqual( new UiRect( expectedColumns[index], 4, 1, 248 ), lines[index].Span );
			Assert.AreEqual( new UiRect( expectedColumns[index], 252, 40, 12 ), lines[index].Label );
		}
	}

	[TestMethod]
	public void HorizontalRowsAccumulateAndDoNotSnapTheLastDivision()
	{
		var backdrop = new GraphBackdrop( 448, 268, 12 );
		CollectionAssert.AreEqual( new[] { 224, 200, 175, 151, 127, 102, 78, 53, 29, 5 },
			backdrop.HorizontalLines().Select( line => (int)line.Y ).ToArray() );
		CollectionAssert.AreEqual( new[] { 28, 25, 23, 20, 17, 15, 12, 10, 7, 4 },
			new GraphBackdrop( 100, 50, 12 ).HorizontalLines().Select( line => (int)line.Y ).ToArray() );
		var unsnapped = new GraphBackdrop( 449, 269, 12 );
		Assert.AreEqual( 429, unsnapped.Plot.Right );
		Assert.AreEqual( 428f, unsnapped.VerticalLines().Last().Span.X );
		Assert.AreEqual( 429, ParkInfoGraph.Column( 12, 12, unsnapped.Plot ), "series columns retain their separate existing formula" );
	}

	[TestMethod]
	public void SpansUseCountsRatherThanPlotEndpoints()
	{
		var backdrop = new GraphBackdrop( 448, 268, 12 );
		foreach ( var line in backdrop.VerticalLines() )
		{
			Assert.AreEqual( 4f, line.Span.Y ); Assert.AreEqual( 248f, line.Span.Height );
			Assert.AreEqual( 252f, line.Span.Bottom ); Assert.AreEqual( 1f, line.Span.Width );
		}
		foreach ( var span in backdrop.HorizontalLines() )
		{
			Assert.AreEqual( 4f, span.X ); Assert.AreEqual( 428f, span.Width );
			Assert.AreEqual( 432f, span.Right ); Assert.AreEqual( 1f, span.Height );
		}
		Assert.AreEqual( new UiRect( 3, 249, 429, 1 ), backdrop.HorizontalAxis );
		Assert.AreEqual( new UiRect( 3, 3, 1, 249 ), backdrop.VerticalAxis );
	}

	[DataTestMethod]
	[DataRow( 0L, 100L, 249 )]
	[DataRow( 0L, 0L, 249 )]
	[DataRow( -50L, 50L, 126 )]
	[DataRow( -1L, 2L, 167 )]
	[DataRow( -100L, 0L, 4 )]
	[DataRow( -5L, -5L, 249 )]
	[DataRow( long.MinValue, long.MaxValue, 126 )]
	[DataRow( long.MinValue, 0L, 4 )]
	[DataRow( -1L, long.MaxValue, 248 )]
	public void ZeroAxisUsesASignedTruncatedOffsetWithoutMovingSeries( long minimum, long maximum, int row )
	{
		Assert.AreEqual( row, new GraphBackdrop( 448, 268, 12, minimum, maximum ).ZeroAxisRow );
		Assert.AreEqual( 248, ParkInfoGraph.Row( 0, 0, (4, 4, 428, 248) ), "flat series keep the baseline, one pixel above the flat axis" );
	}

	[TestMethod]
	public void WidenedHostAxisDoesNotOverflowForLongBalancesAndLargeBitmapHeights()
	{
		var extreme = new GraphBackdrop( 448, int.MaxValue, 12, long.MinValue, 0 );
		Assert.AreEqual( 4, extreme.ZeroAxisRow );
		Assert.AreEqual( 248, new GraphBackdrop( 448, 268, 12, -1, long.MaxValue ).ZeroAxisRow,
			"a very small negative fraction truncates to zero before adding the bottom, without the nonnegative branch's +1" );
	}

	[DataTestMethod]
	[DataRow( 24, 268, 12 )]
	[DataRow( 448, 24, 12 )]
	[DataRow( 0, 0, 12 )]
	[DataRow( -5, 268, 12 )]
	[DataRow( 448, 268, 0 )]
	[DataRow( 448, 268, 24 )]
	[DataRow( 448, 268, -12 )]
	public void InvalidHostBoundsAndUnsupportedPeriodsHaveNoPlotOrGrid( int width, int height, int samples )
	{
		var backdrop = new GraphBackdrop( width, height, samples );
		Assert.IsFalse( backdrop.CanPlot );
		Assert.AreEqual( 0, backdrop.VerticalLines().Count() );
		Assert.AreEqual( 0, backdrop.HorizontalLines().Count() );
	}

	[TestMethod]
	public void ZeroDivisionsSuppressBothGridDirectionsButNotTheAxes()
	{
		var backdrop = new GraphBackdrop( 448, 268, 12, Divisions: 0 );
		Assert.IsTrue( backdrop.CanPlot );
		Assert.AreEqual( 0, backdrop.VerticalLines().Count() );
		Assert.AreEqual( 0, backdrop.HorizontalLines().Count() );
		Assert.AreEqual( new UiRect( 3, 249, 429, 1 ), backdrop.HorizontalAxis );
	}
	[TestMethod]
	public void InvalidHostFramesAndPixelsCannotAuthorizeAnySeriesOrEmitNonfiniteVertices()
	{
		// Guard paths must not even ask for fonts; no original assets are required.
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, null! );
		var geometry = new GraphBackdrop( 448, 268, 12 );
		var frame = new UiRect( 10, 20, 448, 268 );
		foreach ( var invalid in new[]
		{
			new UiRect( float.NaN, 20, 448, 268 ), new UiRect( 10, float.PositiveInfinity, 448, 268 ),
			new UiRect( 10, 20, float.NegativeInfinity, 268 ), new UiRect( 10, 20, 448, float.NaN ),
			new UiRect( 10, 20, 0, 268 ), new UiRect( 10, 20, 448, -1 ),
			new UiRect( float.MaxValue, 20, float.MaxValue, 268 ), new UiRect( 10, float.MaxValue, 448, float.MaxValue )
		} )
		{
			Assert.IsFalse( GraphBackdropRenderer.Draw( context, invalid, 1, geometry ) );
			Assert.AreEqual( 0, context.Batch.Draws.Count );
		}
		foreach ( var pixel in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
		{
			Assert.IsFalse( GraphBackdropRenderer.Draw( context, frame, pixel, geometry ) );
			Assert.AreEqual( 0, context.Batch.Draws.Count );
		}
		Assert.AreEqual( 0, context.Batch.Texts.Count );
		Assert.AreEqual( 0, context.Batch.Glyphs.Count );
	}

	[TestMethod]
	public void BackgroundOnlyFallbackDoesNotAuthorizeSeriesOrAskForFonts()
	{
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, null! );
		foreach ( var geometry in new[] { new GraphBackdrop( 24, 24, 12 ), new GraphBackdrop( 448, 268, 0 ), new GraphBackdrop( 448, 268, 24 ) } )
		{
			context.Batch.Clear();
			Assert.IsFalse( GraphBackdropRenderer.Draw( context, new UiRect( 10, 20, geometry.Width, geometry.Height ), 1, geometry ) );
			Assert.AreEqual( 1, context.Batch.Draws.Count );
			Assert.AreEqual( 6, context.Batch.Draws[0].Vertices.Count );
			Assert.IsTrue( context.Batch.Draws[0].Vertices.All( vertex => vertex.Color.Equals( GraphBackdrop.Background ) ) );
			Assert.AreEqual( 0, context.Batch.Texts.Count );
		}
	}

}
