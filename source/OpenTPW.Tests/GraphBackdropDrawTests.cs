using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>Actual graph controls and window lifecycles, using synthetic glyphs and independent CPU draw expectations.</summary>
[TestClass, DoNotParallelize]
public class GraphBackdropDrawTests
{
	private Logger previousLog = null!;
	[TestInitialize]
	public void Setup() => previousLog = Log;
	[TestCleanup]
	public void Cleanup() => Log = previousLog;

	private static readonly RgbaByte Black = new( 0, 0, 0, 255 );
	private static readonly RgbaByte Grid = new( 0, 100, 0, 255 );
	private static readonly RgbaByte Green = new( 0, 200, 0, 255 );
	private static readonly RgbaByte[] FinanceColours =
	{
		new( 255, 255, 0, 255 ), new( 255, 0, 255, 255 ), new( 255, 0, 0, 255 ), new( 0, 189, 236, 255 ),
		new( 255, 144, 0, 255 ), new( 144, 211, 0, 255 ), new( 255, 117, 117, 255 ), new( 149, 88, 210, 255 )
	};

	private static void Set( object owner, string name, object value ) =>
		owner.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( owner, value );

	private static ParkHud Hud( ParkEconomy? park = null )
	{
		var hud = park == null ? VisitorWorldSelectionTests.Fixture( out _, out _ ) : StaffWindowTests.Hud( park );
		// The existing headless fixture uses an uninitialised HUD, so seed these normal constructor defaults explicitly.
		Set( hud, "financeYears", 1 );
		Set( hud, "financeVisible", new HashSet<FinanceSeries> { FinanceSeries.BankBalance } );
		return hud;
	}

	private static UiContext Context( int width = 1024, int height = 768, int textScale = 1 )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8,
			"-0123456789".Select( character => new FontAtlasTests.SyntheticGlyph( character, 2, 2, 0, 0, 3 ) ).ToArray() ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( width, height, textScale ) };
	}

	private static UiElement Open( ParkHud hud, bool finance )
	{
		if ( finance ) hud.OpenFinanceWindow(); else hud.OpenParkInfoWindow();
		return hud.Stack.Top!.Find( "graph" )!;
	}

	private static void Period( ParkHud hud, bool finance, int years ) =>
		hud.Stack.Top!.Find( $"period{(finance ? years : years == 1 ? 0 : years == 3 ? 1 : 2)}" )!.Activate();

	private static void Draw( UiContext context, UiElement graph )
	{
		context.Batch.Clear();
		Assert.AreEqual( 0, context.Batch.Draws.Count );
		Assert.AreEqual( 0, context.Batch.Texts.Count );
		Assert.AreEqual( 0, context.Batch.TextDraws.Count );
		Assert.AreEqual( 0, context.Batch.Glyphs.Count );
		graph.Draw( context, false, false );
	}

	// These literals are independent of GraphBackdrop, its renderer and ParkInfoGraph.Plot/GridSteps.
	// In particular, finance's 36-sample last column is 427, not the mathematically ideal 428.
	private static int[] Columns( bool finance, int years ) => !finance
		? new[] { 39, 74, 109, 144, 179, 214, 249, 284, 319, 354, 389, 425 }
		: years switch
		{
			1 => new[] { 39, 74, 110, 145, 180, 215, 251, 286, 322, 357, 392, 428 },
			3 => new[] { 39, 74, 110, 145, 180, 215, 251, 286, 321, 357, 392, 427 },
			_ => new[] { 39, 74, 109, 145, 180, 215, 251, 286, 322, 357, 392, 428 }
		};

	private static UiRect Frame( bool finance, UiCanvas canvas )
	{
		// The authored inner rectangles, mapped independently of UiElement.ScreenRect/UiCanvas.Map.
		var bounds = finance ? new UiRect( 741, 291, 897, 537 ) : new UiRect( 744, 256, 890, 527 );
		var scale = Math.Min( canvas.Width / 2048f, canvas.Height / 1536f );
		return new UiRect( canvas.Width / 2f + (bounds.X - 1024) * scale, canvas.Height / 2f + (bounds.Y - 768) * scale,
			bounds.Width * scale, bounds.Height * scale );
	}

	private static ExpectedFrame Backdrop( UiContext context, bool finance, int years, int? zeroRow = null )
	{
		var frame = Frame( finance, context.Canvas );
		var pixel = Math.Max( 1f, Math.Min( context.Canvas.Width / 1024f, context.Canvas.Height / 768f ) );
		var expected = new ExpectedFrame( context.Fonts.Small, context.Canvas.TextScale );
		var (right, bottom) = finance ? (428, 248) : (425, 243);
		UiRect At( int x, int y, int width, int height ) => new( frame.X + x * pixel, frame.Y + y * pixel, width * pixel, height * pixel );
		expected.Rectangle( frame, Black );
		expected.Rectangle( At( 4, 4, right - 4, bottom - 4 ), Black );
		var columns = Columns( finance, years );
		for ( var index = 0; index < columns.Length; index++ )
		{
			expected.Rectangle( At( columns[index], 4, 1, bottom ), Grid );
			expected.Label( ((index + 1) * years).ToString( CultureInfo.InvariantCulture ), At( columns[index], bottom + 4, 40, 12 ), Green );
		}
		foreach ( var row in finance ? new[] { 224, 200, 175, 151, 127, 102, 78, 53, 29, 5 } : new[] { 220, 196, 172, 148, 124, 100, 76, 52, 28, 5 } )
			expected.Rectangle( At( 4, row, right, 1 ), Grid );
		expected.Rectangle( At( 3, zeroRow ?? bottom + 1, right + 1, 1 ), Green );
		expected.Rectangle( At( 3, 3, 1, bottom + 1 ), Green );
		return expected;
	}

	[DataTestMethod]
	[DataRow( true, 1 )]
	[DataRow( true, 3 )]
	[DataRow( true, 12 )]
	[DataRow( false, 1 )]
	[DataRow( false, 3 )]
	[DataRow( false, 12 )]
	public void ActualControlsDrawEveryBackdropRectangleAndLabelInNativeOrderWithoutAnEconomy( bool finance, int years )
	{
		var hud = Hud();
		var graph = Open( hud, finance );
		Period( hud, finance, years );
		var context = Context();
		for ( var frame = 0; frame < 3; frame++ )
		{
			Draw( context, graph );
			Backdrop( context, finance, years ).AssertMatches( context.Batch );
			Assert.AreEqual( 25, context.Batch.Draws.Count, "each vertical line is immediately followed by its own font run; the final solid run contains horizontal lines and axes" );
			Assert.AreEqual( 12, context.Batch.Texts.Count );
			Assert.AreEqual( 18, context.Batch.Draws[0].Vertices.Count, "full clear, plot fill, first vertical line" );
			Assert.AreEqual( 72, context.Batch.Draws[^1].Vertices.Count, "ten horizontal spans and two axes after the twelfth label" );
		}
	}

	private static ParkEconomy FinancePark( params long[] balances )
	{
		var park = RecentResearchTests.Park();
		var totals = new Dictionary<LedgerCategory, long>();
		park.Ledger.Restore( balances[^1], balances[^1], balances.Length - 1, totals,
			balances.Take( balances.Length - 1 ).Select( (value, index) => new LedgerMonth( index, totals, value, value, 7 + index, 100 + index ) ) );
		return park;
	}

	private static void FinanceLines( ExpectedFrame expected, UiContext context, int years, long[] values, long minimum, long maximum, RgbaByte colour )
	{
		var frame = Frame( true, context.Canvas );
		var pixel = Math.Max( 1f, Math.Min( context.Canvas.Width / 1024f, context.Canvas.Height / 768f ) );
		var plot = new UiRect( frame.X + 4 * pixel, frame.Y + 4 * pixel, 424 * pixel, 244 * pixel );
		var upper = minimum == maximum ? minimum + 1 : maximum;
		float Y( long value ) => plot.Bottom - plot.Height * (float)((value - (double)minimum) / (upper - (double)minimum));
		for ( var index = 1; index < values.Length; index++ )
		{
			// Finance intentionally retains fractional host coordinates and marks before the connecting line.
			var x = plot.X + plot.Width * (index / (float)(values.Length - 1));
			var y = Y( values[index] );
			if ( years < 12 )
			{
				expected.Rectangle( new UiRect( x - 1.5f * pixel, y - 1.5f * pixel, 3 * pixel, pixel ), colour );
				expected.Rectangle( new UiRect( x - 1.5f * pixel, y - 1.5f * pixel + 3 * pixel - pixel, 3 * pixel, pixel ), colour );
				expected.Rectangle( new UiRect( x - 1.5f * pixel, y - 1.5f * pixel + pixel, pixel, pixel ), colour );
				expected.Rectangle( new UiRect( x - 1.5f * pixel + 3 * pixel - pixel, y - 1.5f * pixel + pixel, pixel, pixel ), colour );
			}
			expected.Line( new NVector2( plot.X + plot.Width * (index - 1) / (values.Length - 1), Y( values[index - 1] ) ), new NVector2( x, y ), pixel, colour );
		}
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 3 )]
	[DataRow( 12 )]
	public void FinanceDrawPreservesFractionalSeriesAndMarkerOrderAcrossEveryScaleSignAndLongExtrema( int years )
	{
		var scenarios = new (long[] Values, long Minimum, long Maximum, int Axis)[]
		{
			(new long[] { 10, 20, 70, 40 }, 0, 70, 249),
			(new long[] { 0, 0, 0, 0 }, 0, 0, 249),
			(new long[] { -10, -20, -70, -40 }, -70, 0, 4),
			(new long[] { -30, 20, 70, 10 }, -30, 70, 175),
			(new[] { long.MinValue, 0L, long.MaxValue, 1L }, long.MinValue, long.MaxValue, 126),
			(new[] { long.MinValue, -1L, long.MinValue + 1, -7L }, long.MinValue, 0, 4)
		};
		foreach ( var scenario in scenarios )
		{
			var park = FinancePark( scenario.Values );
			var saved = ParkSaveFile.Serialize( park );
			var random = park.World.State;
			var hash = WorldStateHash.Compute( new WorldStateSources { Economy = park } );
			var hud = Hud( park );
			var graph = Open( hud, true );
			Period( hud, true, years );
			var context = Context();
			Draw( context, graph );
			var expected = Backdrop( context, true, years, scenario.Axis );
			FinanceLines( expected, context, years, scenario.Values, scenario.Minimum, scenario.Maximum, FinanceColours[0] );
			expected.AssertMatches( context.Batch );
			Assert.IsTrue( context.Batch.Draws.SelectMany( draw => draw.Vertices ).All( vertex => float.IsFinite( vertex.Position.X ) && float.IsFinite( vertex.Position.Y ) ) );
			Assert.AreEqual( saved, ParkSaveFile.Serialize( park ) );
			Assert.AreEqual( random, park.World.State );
			Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = park } ) );
		}
	}

	[TestMethod]
	public void FinanceDrawUsesEachOfTheEightMappingsAndTheirIndependentColours()
	{
		var park = RecentResearchTests.Park();
		Dictionary<LedgerCategory, long> Totals( int multiplier ) => new()
		{
			[LedgerCategory.GateTakings] = 11 * multiplier, [LedgerCategory.ShopTakings] = 13 * multiplier,
			[LedgerCategory.SideshowTakings] = 17 * multiplier, [LedgerCategory.OtherIncome] = 19 * multiplier,
			[LedgerCategory.LoanReceived] = 23 * multiplier, [LedgerCategory.StaffCosts] = 29 * multiplier,
			[LedgerCategory.OtherCosts] = 31 * multiplier, [LedgerCategory.LoanPayments] = 37 * multiplier
		};
		park.Ledger.Restore( 303, 202, 2, Totals( 3 ), new[]
		{
			new LedgerMonth( 0, Totals( 1 ), 0, 101, 3, 401 ), new LedgerMonth( 1, Totals( 2 ), 101, 202, 5, 402 )
		} );
		var values = new[]
		{
			new long[] { 101, 202, 303 }, new long[] { 401, 402, 0 }, new long[] { 83, 166, 249 }, new long[] { 11, 22, 33 },
			new long[] { 13, 26, 39 }, new long[] { 17, 34, 51 }, new long[] { 97, 194, 291 }, new long[] { 29, 58, 87 }
		};
		var hud = Hud( park );
		var graph = Open( hud, true );
		var context = Context();
		foreach ( var series in Enum.GetValues<FinanceSeries>() )
		{
			foreach ( var selected in hud.FinanceVisibleSeries.ToArray() ) hud.ToggleFinanceSeries( selected );
			hud.Stack.Top!.Find( $"series{series}" )!.Activate();
			CollectionAssert.AreEqual( new[] { series }, hud.FinanceVisibleSeries.ToArray() );
			Draw( context, graph );
			var expected = Backdrop( context, true, 1 );
			FinanceLines( expected, context, 1, values[(int)series], 0, values[(int)series].Max(), FinanceColours[(int)series] );
			expected.AssertMatches( context.Batch );
		}
	}


	[TestMethod]
	public void FinanceButtonsPreserveThePairAndCashFlowMasksAndDrawSelectedSeriesInOrdinalOrder()
	{
		var park = FinancePark( 10, 20, 30 );
		var hud = Hud( park );
		var graph = Open( hud, true );
		var context = Context();
		void Press( FinanceSeries series ) => hud.Stack.Top!.Find( $"series{series}" )!.Activate();
		void Selected( params FinanceSeries[] series ) => CollectionAssert.AreEquivalent( series, hud.FinanceVisibleSeries.ToArray() );
		Selected( FinanceSeries.BankBalance );
		Press( FinanceSeries.ParkValue );
		Selected( FinanceSeries.BankBalance, FinanceSeries.ParkValue );
		Draw( context, graph );
		var expected = Backdrop( context, true, 1 );
		FinanceLines( expected, context, 1, new long[] { 10, 20, 30 }, 0, 101, FinanceColours[0] );
		FinanceLines( expected, context, 1, new long[] { 100, 101, 0 }, 0, 101, FinanceColours[1] );
		expected.AssertMatches( context.Batch );
		Press( FinanceSeries.StaffCosts );
		Selected( FinanceSeries.StaffCosts );
		Press( FinanceSeries.GateTakings );
		Press( FinanceSeries.MoneyIn );
		Selected( FinanceSeries.MoneyIn, FinanceSeries.GateTakings, FinanceSeries.StaffCosts );
		Draw( context, graph );
		expected = Backdrop( context, true, 1 );
		foreach ( var index in new[] { 2, 3, 7 } ) FinanceLines( expected, context, 1, new long[] { 0, 0, 0 }, 0, 0, FinanceColours[index] );
		expected.AssertMatches( context.Batch );
		Press( FinanceSeries.GateTakings );
		Selected( FinanceSeries.MoneyIn, FinanceSeries.StaffCosts );
		Press( FinanceSeries.ParkValue );
		Selected( FinanceSeries.ParkValue );
		Press( FinanceSeries.BankBalance );
		Selected( FinanceSeries.BankBalance, FinanceSeries.ParkValue );
		hud.ToggleFinanceSeries( (FinanceSeries)99 );
		Selected( FinanceSeries.BankBalance, FinanceSeries.ParkValue );
		hud.SetFinancePeriod( 2 );
		Assert.AreEqual( 1, hud.FinancePeriodYears, "an unsupported period must not replace a valid selection" );
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 3 )]
	[DataRow( 12 )]
	public void FinanceMonthsKeepTheLivePointAndSelectedWindowWithoutClosingAMonth( int years )
	{
		var values = Enumerable.Range( 1, 145 ).Select( value => (long)value ).ToArray();
		var park = FinancePark( values );
		var hud = Hud( park );
		var graph = Open( hud, true );
		Period( hud, true, years );
		var context = Context();
		var shown = values.TakeLast( years * 12 ).ToArray();
		CollectionAssert.AreEqual( shown, hud.FinanceMonths().Select( month => month.ClosingBalance ).ToArray() );
		Assert.AreEqual( 144L, hud.FinanceMonths()[^1].MonthIndex );
		Draw( context, graph );
		var expected = Backdrop( context, true, years );
		FinanceLines( expected, context, years, shown, 0, 145, FinanceColours[0] );
		expected.AssertMatches( context.Batch );
		Assert.AreEqual( 144, park.Ledger.History.Count );
		Assert.AreEqual( 144L, park.Ledger.CurrentMonthIndex );
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 3 )]
	[DataRow( 12 )]
	public void OneLiveFinancePointAndDeselectedSeriesDrawOnlyTheBackdrop( int years )
	{
		var park = FinancePark( 42 );
		var hud = Hud( park );
		var graph = Open( hud, true );
		Period( hud, true, years );
		var context = Context();
		for ( var selection = 0; selection < 3; selection++ )
		{
			Draw( context, graph );
			Backdrop( context, true, years ).AssertMatches( context.Batch );
			hud.Stack.Top!.Find( "seriesBankBalance" )!.Activate();
		}
		Assert.AreEqual( 0, park.Ledger.History.Count );
		Assert.AreEqual( 1, hud.FinanceMonths().Count );
	}

	private static void InformationLines( ExpectedFrame expected, UiContext context, int years, int[] points, int maximum, RgbaByte colour )
	{
		var frame = Frame( false, context.Canvas );
		var pixel = Math.Max( 1f, Math.Min( context.Canvas.Width / 1024f, context.Canvas.Height / 768f ) );
		var samples = years * 12;
		int Column( int index ) => (int)(4 + 421f / samples * index);
		int Row( int value ) => maximum == 0 ? 243 : Math.Clamp( 243 - (int)((long)value * 239 / maximum), 4, 243 );
		NVector2 At( int column, int row ) => new( frame.X + column * pixel, frame.Y + row * pixel );
		var lastColumn = 4;
		var lastRow = samples;
		for ( var index = 1; index < points.Length; index++ )
		{
			lastColumn = Column( index ); lastRow = Row( points[index] );
			expected.Line( At( Column( index - 1 ), Row( points[index - 1] ) ), At( lastColumn, lastRow ), pixel, colour );
			if ( years < 12 )
			{
				expected.Line( At( lastColumn - 1, lastRow - 1 ), At( lastColumn + 1, lastRow - 1 ), pixel, colour );
				expected.Line( At( lastColumn + 1, lastRow - 1 ), At( lastColumn + 1, lastRow + 1 ), pixel, colour );
				expected.Line( At( lastColumn + 1, lastRow + 1 ), At( lastColumn - 1, lastRow + 1 ), pixel, colour );
				expected.Line( At( lastColumn - 1, lastRow + 1 ), At( lastColumn - 1, lastRow - 1 ), pixel, colour );
			}
		}
		var text = points[^1].ToString( CultureInfo.InvariantCulture );
		var width = 3f * text.Length * context.Canvas.TextScale / pixel;
		var right = Math.Max( lastColumn, 4 + width );
		expected.Label( text, new UiRect( frame.X + (right - width - 1) * pixel, frame.Y + Math.Max( 2, lastRow - 6 ) * pixel,
			(width + 2) * pixel, 12 * pixel ), colour, rightAligned: true );
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 3 )]
	[DataRow( 12 )]
	public void ParkInformationDrawRetainsIntegerSeriesOrderDedicatedRatingAndLastValueLabels( int years )
	{
		var park = FinancePark( 100, 200, 300 );
		park.StatisticsHistory.Arrivals.Add( 5 ); park.StatisticsHistory.Arrivals.Add( 10 );
		park.StatisticsHistory.Happiness.Add( 20.75 ); park.StatisticsHistory.Happiness.Add( 45.99 );
		park.StatisticsHistory.Stay.Add( 2 ); park.StatisticsHistory.Stay.Add( 7 );
		park.StatisticsHistory.People.Add( 40 ); park.StatisticsHistory.People.Add( 60 );
		park.StatisticsHistory.Rating.Add( 13 ); park.StatisticsHistory.Rating.Add( 87 );
		var hud = Hud( park );
		var graph = Open( hud, false );
		Period( hud, false, years );
		foreach ( var series in new[] { ParkInfoSeries.Arrivals, ParkInfoSeries.Happiness, ParkInfoSeries.Stay, ParkInfoSeries.Rating } )
			hud.Stack.Top!.Find( $"series{series}" )!.Activate();
		var context = Context();
		Draw( context, graph );
		var expected = Backdrop( context, false, years );
		var values = new[] { new[] { 0, 5, 10 }, new[] { 0, 20, 45 }, new[] { 0, 2, 7 }, new[] { 0, 40, 60 }, new[] { 0, 13, 87 } };
		var colours = new[] { FinanceColours[1], FinanceColours[2], FinanceColours[3], FinanceColours[0], FinanceColours[4] };
		for ( var series = 0; series < values.Length; series++ ) InformationLines( expected, context, years, values[series], 87, colours[series] );
		expected.AssertMatches( context.Batch );
		CollectionAssert.AreEqual( new[] { "10", "45", "7", "60", "87" }, context.Batch.Texts.Skip( 12 ).Select( text => text.Text ).ToArray() );
		Assert.AreEqual( 8, park.Ledger.History[^1].ParkRating, "the orange line and label come from the independent rating history, not this ledger value" );
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 3 )]
	[DataRow( 12 )]
	public void EmptyParkHistoryHasOnlyItsZeroLabelAndDeselectionRemovesIt( int years )
	{
		var park = RecentResearchTests.Park();
		var hud = Hud( park );
		var graph = Open( hud, false );
		Period( hud, false, years );
		var context = Context();
		for ( var selection = 0; selection < 3; selection++ )
		{
			Draw( context, graph );
			var expected = Backdrop( context, false, years );
			if ( selection != 1 ) InformationLines( expected, context, years, new[] { 0 }, 0, FinanceColours[0] );
			expected.AssertMatches( context.Batch );
			hud.Stack.Top!.Find( "seriesPeople" )!.Activate();
		}
		Assert.IsTrue( park.StatisticsHistory.IsEmpty );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void RealCloseBackReopenPeriodAndSeriesActionsAreDeterministicAndNeverMutateThePark( bool finance )
	{
		var park = FinancePark( -30, 20, 70, 10 );
		park.StatisticsHistory.People.Add( 20 ); park.StatisticsHistory.People.Add( 80 );
		park.StatisticsHistory.Rating.Add( 11 ); park.StatisticsHistory.Rating.Add( 59 );
		var hud = Hud( park );
		var context = Context();
		var saved = ParkSaveFile.Serialize( park );
		var statistics = JsonSerializer.Serialize( park.StatisticsHistory.Capture() );
		var ledger = JsonSerializer.Serialize( ParkSaveFile.Capture( park ).Ledger );
		var random = park.World.State;
		var hash = WorldStateHash.Compute( new WorldStateSources { Economy = park } );
		var seen = new Dictionary<int, BatchSnapshot>();
		for ( var opening = 0; opening < 3; opening++ )
		{
			var graph = Open( hud, finance );
			var screen = hud.Stack.Top!;
			if ( opening > 0 )
				Assert.AreEqual( finance ? 12 : 2, finance ? hud.FinancePeriodYears : hud.ParkInfoPeriod, "reopening keeps the selected period" );
			foreach ( var years in new[] { 1, 3, 12 } )
			{
				Period( hud, finance, years );
				for ( var frame = 0; frame < 2; frame++ )
				{
					Draw( context, graph );
					var snapshot = BatchSnapshot.Capture( context.Batch );
					if ( seen.TryGetValue( years, out var first ) ) first.AssertMatches( context.Batch ); else seen.Add( years, snapshot );
				}
				var toggle = screen.Find( finance ? "seriesBankBalance" : "seriesPeople" )!;
				toggle.Activate(); Draw( context, graph );
				Backdrop( context, finance, years ).AssertMatches( context.Batch );
				toggle.Activate(); Draw( context, graph ); seen[years].AssertMatches( context.Batch );
			}
			if ( opening == 1 ) screen.Update( context, UiInput.Key( UiKeys.Back ) );
			else screen.Find( finance ? "exit" : "close" )!.Activate();
			Assert.IsFalse( finance ? hud.FinanceWindowOpen : hud.ParkInfoWindowOpen );
			Assert.AreEqual( "hud", hud.Stack.Top!.Name );
			Assert.AreEqual( saved, ParkSaveFile.Serialize( park ) );
			Assert.AreEqual( statistics, JsonSerializer.Serialize( park.StatisticsHistory.Capture() ) );
			Assert.AreEqual( ledger, JsonSerializer.Serialize( ParkSaveFile.Capture( park ).Ledger ) );
			Assert.AreEqual( random, park.World.State );
			Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = park } ) );
		}
	}

	[DataTestMethod]
	[DataRow( true, 1024, 768, 1 )]
	[DataRow( true, 2048, 1536, 1 )]
	[DataRow( true, 3072, 2304, 2 )]
	[DataRow( true, 4096, 3072, 3 )]
	[DataRow( true, 1280, 768, 1 )]
	[DataRow( false, 1024, 768, 1 )]
	[DataRow( false, 2048, 1536, 1 )]
	[DataRow( false, 3072, 2304, 2 )]
	[DataRow( false, 4096, 3072, 3 )]
	[DataRow( false, 1280, 768, 1 )]
	public void CanvasScalingAndAnchoringPreserveEveryBackdropPrimitiveIncludingFractionalClearRemainders( bool finance, int width, int height, int textScale )
	{
		var hud = Hud();
		var graph = Open( hud, finance );
		var context = Context( width, height, textScale );
		Assert.AreEqual( textScale, context.Canvas.TextScale );
		foreach ( var years in new[] { 1, 3, 12 } )
		{
			Period( hud, finance, years );
			Draw( context, graph );
			Backdrop( context, finance, years ).AssertMatches( context.Batch );
			Assert.AreEqual( Frame( finance, context.Canvas ).Bottom, context.Batch.Draws[0].Vertices[1].Position.Y,
				"the black clear covers the complete frame, including the bitmap's discarded half-pixel" );
		}
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void SmallCanvasUsesOnePixelAndTinyGraphBoundsClearWithoutDrawingInvalidPlots( bool finance )
	{
		var hud = Hud( FinancePark( 10, 20 ) );
		var graph = Open( hud, finance );
		var context = Context( 640, 480 );
		Draw( context, graph );
		var firstGrid = context.Batch.Draws[0].Vertices.Skip( 12 ).Take( 6 ).ToArray();
		Assert.AreEqual( 1f, firstGrid[2].Position.X - firstGrid[0].Position.X, "a virtual two-unit pixel must not shrink below one framebuffer pixel" );
		context.Canvas = new UiCanvas( 1024, 768 );
		foreach ( var size in new[] { 0f, 1f, 48f, 49f } )
		{
			graph.Bounds = new UiRect( 20, 40, size, size );
			Draw( context, graph );
			var expected = new ExpectedFrame( context.Fonts.Small, 1 );
			if ( size > 0 ) expected.Rectangle( new UiRect( 10, 20, size / 2, size / 2 ), Black );
			expected.AssertMatches( context.Batch );
		}
	}


	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void PopulatedControlsRejectNonfiniteCoordinatesAndNegativeSizesBeforeDrawingTheirSeries( bool finance )
	{
		var park = FinancePark( 10, 20, 30 );
		park.StatisticsHistory.People.Add( 5 ); park.StatisticsHistory.People.Add( 9 );
		var hud = Hud( park );
		var graph = Open( hud, finance );
		var context = Context();
		Draw( context, graph );
		Assert.IsTrue( context.Batch.Draws.SelectMany( draw => draw.Vertices ).Any( vertex => vertex.Color.Equals( FinanceColours[0] ) ),
			"the fixture must really draw a selected series before the bounds become invalid" );
		var bounds = graph.Bounds;
		var invalid = new List<UiRect> { bounds with { Width = -1 }, bounds with { Height = -1 }, bounds with { Width = -50, Height = -50 } };
		foreach ( var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
		{
			invalid.Add( bounds with { X = value } );
			invalid.Add( bounds with { Y = value } );
			invalid.Add( bounds with { Width = value } );
			invalid.Add( bounds with { Height = value } );
		}
		foreach ( var rect in invalid )
		{
			graph.Bounds = rect;
			Draw( context, graph );
			new ExpectedFrame( context.Fonts.Small, 1 ).AssertMatches( context.Batch );
		}
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void PopulatedControlsWithAnUnsupportedStoredPeriodDrawOnlyTheFullBackground( bool finance )
	{
		var park = FinancePark( 10, 20, 30 );
		park.StatisticsHistory.People.Add( 5 ); park.StatisticsHistory.People.Add( 9 );
		var hud = Hud( park );
		var graph = Open( hud, finance );
		var context = Context();
		Draw( context, graph );
		Assert.IsTrue( context.Batch.Draws.SelectMany( draw => draw.Vertices ).Any( vertex => vertex.Color.Equals( FinanceColours[0] ) ) );
		// Public setters reject these values; inject corrupt stored state to exercise the consumers' defensive path.
		Set( hud, finance ? "financeYears" : "parkInfoPeriod", finance ? 2 : 99 );
		Draw( context, graph );
		var expected = new ExpectedFrame( context.Fonts.Small, 1 );
		expected.Rectangle( Frame( finance, context.Canvas ), Black );
		expected.AssertMatches( context.Batch );
	}

	/// <summary>Independent command stream: does not call production graph helpers, UiBatch, DrawText or TextLayout.</summary>
	private sealed class ExpectedFrame
	{
		private readonly FontAtlas font;
		private readonly int textScale;
		private readonly List<UiTexture> textures = new();
		private readonly List<UiVertex[]> vertices = new();
		private readonly List<(UiRect Rect, string Text)> texts = new();
		private readonly List<UiTextDraw> textDraws = new();
		private readonly List<UiGlyphQuad> glyphs = new();

		public ExpectedFrame( FontAtlas font, int textScale ) { this.font = font; this.textScale = textScale; }

		private void Append( UiTexture texture, params UiVertex[] points )
		{
			if ( textures.Count > 0 && textures[^1].Equals( texture ) ) vertices[^1] = vertices[^1].Concat( points ).ToArray();
			else { textures.Add( texture ); vertices.Add( points ); }
		}

		private void Quad( UiTexture texture, UiRect rect, RgbaByte colour, NVector2 uv0, NVector2 uv1 )
		{
			var a = new UiVertex( new NVector2( rect.X, rect.Y ), uv0, colour );
			var b = new UiVertex( new NVector2( rect.X, rect.Bottom ), new NVector2( uv0.X, uv1.Y ), colour );
			var c = new UiVertex( new NVector2( rect.Right, rect.Y ), new NVector2( uv1.X, uv0.Y ), colour );
			var d = new UiVertex( new NVector2( rect.Right, rect.Bottom ), uv1, colour );
			Append( texture, a, b, c, c, b, d );
		}

		public void Rectangle( UiRect rect, RgbaByte colour ) => Quad( UiTexture.Solid, rect, colour, NVector2.Zero, NVector2.One );

		public void Line( NVector2 from, NVector2 to, float pixel, RgbaByte colour )
		{
			var delta = to - from;
			if ( delta.LengthSquared() < 0.01f ) { Rectangle( new UiRect( from.X, from.Y, pixel, pixel ), colour ); return; }
			var normal = NVector2.Normalize( new NVector2( -delta.Y, delta.X ) ) * pixel / 2;
			var a = new UiVertex( from + normal, NVector2.Zero, colour );
			var b = new UiVertex( from - normal, NVector2.Zero, colour );
			var c = new UiVertex( to + normal, NVector2.Zero, colour );
			var d = new UiVertex( to - normal, NVector2.Zero, colour );
			Append( UiTexture.Solid, a, b, c, c, b, d );
		}

		public void Label( string text, UiRect box, RgbaByte colour, bool rightAligned = false )
		{
			var width = text.Length * 3 * textScale;
			var height = 8 * textScale;
			var x = rightAligned ? (int)MathF.Round( box.Right ) - width : (int)MathF.Round( box.X );
			var top = (int)MathF.Round( box.Y );
			var y = top + ((int)MathF.Round( box.Bottom ) - top - height) / 2;
			var firstGlyph = glyphs.Count;
			for ( var index = 0; index < text.Length; index++ )
			{
				var glyph = font.Glyphs[text[index]];
				var left = x + index * 3 * textScale;
				glyphs.Add( new UiGlyphQuad( font, left, y, 2 * textScale, 2 * textScale, glyph.X, glyph.Y, textScale, colour ) );
				Quad( UiTexture.Font( font, textScale ), new UiRect( left, y, 2 * textScale, 2 * textScale ), colour,
					new NVector2( (float)glyph.X / font.Width, (float)glyph.Y / font.Height ), new NVector2( (float)(glyph.X + 2) / font.Width, (float)(glyph.Y + 2) / font.Height ) );
			}
			var rect = new UiRect( x, y, width, height );
			texts.Add( (rect, text) );
			textDraws.Add( new UiTextDraw( rect, text, firstGlyph, text.Length ) );
		}

		public void AssertMatches( UiBatch batch ) => new BatchSnapshot( textures.ToArray(), vertices.ToArray(), texts.ToArray(), textDraws.ToArray(), glyphs.ToArray() ).AssertMatches( batch );
	}

	private sealed record BatchSnapshot( UiTexture[] Textures, UiVertex[][] Vertices, (UiRect Rect, string Text)[] Texts, UiTextDraw[] TextDraws, UiGlyphQuad[] Glyphs )
	{
		public static BatchSnapshot Capture( UiBatch batch ) => new( batch.Draws.Select( draw => draw.Texture ).ToArray(), batch.Draws.Select( draw => draw.Vertices.ToArray() ).ToArray(),
			batch.Texts.ToArray(), batch.TextDraws.ToArray(), batch.Glyphs.ToArray() );

		public void AssertMatches( UiBatch batch )
		{
			CollectionAssert.AreEqual( Textures, batch.Draws.Select( draw => draw.Texture ).ToArray(), "texture runs must preserve grid/label/axis/series order" );
			for ( var index = 0; index < Vertices.Length; index++ ) CollectionAssert.AreEqual( Vertices[index], batch.Draws[index].Vertices.ToArray(), $"every vertex and colour in draw run {index}" );
			CollectionAssert.AreEqual( Texts, batch.Texts.ToArray(), "recorded text and exact host placement" );
			CollectionAssert.AreEqual( TextDraws, batch.TextDraws.ToArray(), "per-text glyph ownership" );
			CollectionAssert.AreEqual( Glyphs, batch.Glyphs.ToArray(), "every synthetic glyph, colour and scale, without drop shadows" );
		}
	}
}
