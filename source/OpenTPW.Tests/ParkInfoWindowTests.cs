using System;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The park information window's graph, counts frame, happiness levels and buttons (tp.exe 0x4A52A0, 0x4A4830, 0x4A4B90, 0x4C7720, 0x4C9750).</summary>
[TestClass, DoNotParallelize]
public class ParkInfoWindowTests
{
	[TestMethod]
	public void ARingKeepsTheNewestEntriesAndWrapsOnceFull()
	{
		var ring = new HistoryRing( 3 );
		Assert.AreEqual( (0, -1), (ring.Count, ring.Head) );
		Assert.IsFalse( ring.TryGet( 0, out _ ) );
		for ( var value = 1; value <= 4; value++ )
			ring.Add( value );
		Assert.IsTrue( ring.Wrapped );
		Assert.AreEqual( 3, ring.Count );
		CollectionAssert.AreEqual( new double[] { 2, 3, 4 }, ring.Oldest().ToArray() );
		Assert.IsTrue( ring.TryGet( 0, out var newest ) && newest == 4 );
		Assert.IsFalse( ring.TryGet( 3, out _ ), "past the slots" );
	}

	[TestMethod]
	public void ThePeriodsAreTwelveThirtySixAndOneHundredFortyFourSamples()
	{
		CollectionAssert.AreEqual( new[] { 12, 36, 144, 0 }, new[] { 0, 1, 2, 3 }.Select( ParkStatisticsHistory.PeriodSamples ).ToArray() );
	}

	[TestMethod]
	public void TheScaleReadsOneSampleMoreThanTheGraphDrawsAndIgnoresNonPositiveValues()
	{
		var ring = new HistoryRing( 144 );
		ring.Add( 500 );              // 13 months back: read by the scale (samples 0..12), not drawn
		for ( var month = 0; month < 12; month++ )
			ring.Add( 10 + month );
		Assert.AreEqual( 500, ParkStatisticsHistory.SeriesMaximum( ring, 0, false ), "0x4801D0 scans up to and including the period's sample count" );
		var points = ParkStatisticsHistory.SeriesPoints( ring, 0, false );
		Assert.AreEqual( 13, points.Length );
		Assert.AreEqual( (500, 10, 21), (points[0], points[1], points[^1]), "the points are the newest twelve plus the one before, oldest first" );
		var negative = new HistoryRing( 144 );
		negative.Add( -5 );
		Assert.AreEqual( 0, ParkStatisticsHistory.SeriesMaximum( negative, 0, false ) );
		Assert.AreEqual( 7, ParkStatisticsHistory.SeriesMaximum( HistoryRing.Of( 144, new[] { 7.9 } ), 0, true ), "a single ring's maximum is truncated" );
	}

	[TestMethod]
	public void AShortRingDrawsAZeroPointBeforeItsFirstEntry()
	{
		var ring = HistoryRing.Of( 144, new double[] { 5, 9 } );
		CollectionAssert.AreEqual( new[] { 0, 5, 9 }, ParkStatisticsHistory.SeriesPoints( ring, 0, false ), "0x480460: n = entries held, n + 1 points, the oldest unreadable one 0" );
		CollectionAssert.AreEqual( new[] { 0 }, ParkStatisticsHistory.SeriesPoints( new HistoryRing( 144 ), 1, false ) );
	}

	[TestMethod]
	public void TheMonthStepStoresTheRingsAndClearsTheArrivals()
	{
		var history = new ParkStatisticsHistory();
		for ( var arrival = 0; arrival < 7; arrival++ )
			history.RecordArrival();
		history.RecordStay( 2 );
		history.RecordStay( 5 );
		history.CloseMonth( 61.5f, 40, 23 );
		Assert.AreEqual( 0, history.ArrivalsThisMonth );
		Assert.IsTrue( history.Arrivals.TryGet( 0, out var arrivals ) && arrivals == 7 );
		Assert.IsTrue( history.People.TryGet( 0, out var people ) && people == 40 );
		Assert.IsTrue( history.Happiness.TryGet( 0, out var happiness ) && happiness == 61.5 );
		Assert.IsTrue( history.Stay.TryGet( 0, out var stay ) && stay == 3, "(2 + 5) / 2 truncated" );
		history.CloseMonth( 0, 0, 0 );
		Assert.IsTrue( history.Arrivals.TryGet( 0, out arrivals ) && arrivals == 0 );
	}

	[TestMethod]
	public void WithoutAnyStayTheMeanStayIsZeroAndAGuestsStayIsWholeFourTurnStepsAsDays()
	{
		var history = new ParkStatisticsHistory();
		history.CloseMonth( 0, 0, 0 );
		Assert.IsTrue( history.Stay.TryGet( 0, out var stay ) && stay == 0 );
		Assert.AreEqual( 0, ParkStatisticsHistory.StayDays( 22 ) );
		// 23 four-turn steps of 3750 s are 345000 s = 3 days and a bit; 92 turns are 23 steps
		Assert.AreEqual( 3, ParkStatisticsHistory.StayDays( 95 ) );
		Assert.AreEqual( ParkStatisticsHistory.StayDays( 92 ), ParkStatisticsHistory.StayDays( 95 ), "a turn short of a step does not count" );
	}

	[TestMethod]
	public void TheStayRingMeansOnlyItsNewestFifty()
	{
		var history = new ParkStatisticsHistory();
		for ( var guest = 0; guest < 50; guest++ )
			history.RecordStay( 100 );
		for ( var guest = 0; guest < 50; guest++ )
			history.RecordStay( 2 );
		history.CloseMonth( 0, 0, 0 );
		Assert.IsTrue( history.Stay.TryGet( 0, out var stay ) && stay == 2 );
	}

	[TestMethod]
	public void TheHistoryRoundTripsThroughASaveAndOldSavesLoadEmpty()
	{
		var history = new ParkStatisticsHistory();
		history.RecordArrival();
		history.RecordStay( 4 );
		history.CloseMonth( 55.25f, 12, 18 );
		history.RecordArrival();
		var json = JsonSerializer.Serialize( history.Capture() );
		var data = JsonSerializer.Deserialize<ParkStatisticsData>( json )!;
		Assert.IsTrue( data.IsValid );
		var restored = new ParkStatisticsHistory();
		restored.Restore( data );
		Assert.AreEqual( json, JsonSerializer.Serialize( restored.Capture() ) );
		Assert.AreEqual( 1, restored.ArrivalsThisMonth );
		Assert.IsTrue( restored.Happiness.TryGet( 0, out var happiness ) && happiness == 55.25 );
		Assert.IsTrue( new ParkStatisticsHistory().IsEmpty );
		Assert.IsFalse( new ParkStatisticsData { Arrivals = data.Arrivals, Happiness = data.Happiness, Stay = data.Stay, People = data.People }.IsValid, "a missing ring is rejected" );
		Assert.IsFalse( new ParkStatisticsData { ArrivalsThisMonth = 0, Arrivals = new RingData { Slots = new() { 1 }, Head = 0, Wrapped = false }, Happiness = data.Happiness, Stay = data.Stay, People = data.People, RecentStays = data.RecentStays }.IsValid, "a wrong size" );

		// a ring that has gone round keeps its head and wrapped flag, so save -> load -> save writes the loaded data again
		for ( var guest = 0; guest < 60; guest++ )
			history.RecordStay( guest );
		Assert.AreEqual( (10, true), (history.RecentStays.Head, history.RecentStays.Wrapped), "61 stays (the first one above and 60 more) into the 50-entry ring" );
		var wrappedJson = JsonSerializer.Serialize( history.Capture() );
		var wrapped = new ParkStatisticsHistory();
		wrapped.Restore( JsonSerializer.Deserialize<ParkStatisticsData>( wrappedJson )! );
		Assert.AreEqual( (10, true), (wrapped.RecentStays.Head, wrapped.RecentStays.Wrapped) );
		Assert.AreEqual( wrappedJson, JsonSerializer.Serialize( wrapped.Capture() ) );
		CollectionAssert.AreEqual( Enumerable.Range( 10, 50 ).Select( stay => (double)stay ).ToArray(), wrapped.RecentStays.Oldest().ToArray() );

		// a ring saved with its entries replayed in order (head at the last slot, flag clear) still loads with the same entries
		var replayed = new RingData { Slots = Enumerable.Range( 10, 50 ).Select( stay => (double)stay ).ToList(), Head = 49, Wrapped = false };
		var old = new ParkStatisticsData { Arrivals = data.Arrivals, Happiness = data.Happiness, Stay = data.Stay, People = data.People, RecentStays = replayed };
		Assert.IsTrue( old.IsValid );
		var loaded = new ParkStatisticsHistory();
		loaded.Restore( old );
		CollectionAssert.AreEqual( wrapped.RecentStays.Oldest().ToArray(), loaded.RecentStays.Oldest().ToArray() );
		Assert.AreEqual( wrapped.RecentStays.SumNewest( 50 ), loaded.RecentStays.SumNewest( 50 ) );
	}

	[TestMethod]
	public void TheHappinessLevelsAreThePercentsBelow33AndAbove66()
	{
		// 0x4C9750 / 0x4C9670: strict compares on the truncated byte, count x 100 / guests truncated
		Assert.AreEqual( (0, 0), GuestSimulation.HappinessLevels( Array.Empty<float>() ) );
		Assert.AreEqual( (33, 33), GuestSimulation.HappinessLevels( new[] { 32.9f, 33f, 67f } ), "32 is sad, 33 is not; 66 is not happy, 67 is" );
		Assert.AreEqual( (50, 0), GuestSimulation.HappinessLevels( new[] { 0f, 66f } ) );
		Assert.AreEqual( 1024, ParkHud.ParkInfoHappyBarValue( 0 ) );
		Assert.AreEqual( 614, ParkHud.ParkInfoHappyBarValue( 40 ) );
		Assert.AreEqual( 102, ParkHud.ParkInfoSadBarValue( 10 ) );
	}

	[TestMethod]
	public void TheGraphGeometryFollowsTheOriginalsPlotAndGrid()
	{
		var plot = ParkInfoGraph.Plot( 445, 263 );
		Assert.AreEqual( (4, 4, 425, 243), plot );
		Assert.AreEqual( 243, ParkInfoGraph.Row( 0, 100, plot ) );
		Assert.AreEqual( 4, ParkInfoGraph.Row( 100, 100, plot ) );
		Assert.AreEqual( 243, ParkInfoGraph.Row( 50, 0, plot ), "a flat range sits on the bottom" );
		Assert.AreEqual( 4, ParkInfoGraph.Row( 999, 100, plot ), "cut to the plot" );
		Assert.AreEqual( 4, ParkInfoGraph.Column( 0, 12, plot ) );
		Assert.AreEqual( 425, ParkInfoGraph.Column( 12, 12, plot ) );
		CollectionAssert.AreEqual( Enumerable.Range( 1, 12 ).ToArray(), ParkInfoGraph.GridSteps( 12 ).ToArray() );
		CollectionAssert.AreEqual( new[] { 3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36 }, ParkInfoGraph.GridSteps( 36 ).ToArray() );
		CollectionAssert.AreEqual( new[] { 12, 24, 36, 48, 60, 72, 84, 96, 108, 120, 132, 144 }, ParkInfoGraph.GridSteps( 144 ).ToArray() );
	}

	[TestMethod]
	public void TheSeriesColoursAndOrderAreTheFinanceGraphsFirstFive()
	{
		Assert.AreEqual( new Veldrid.RgbaByte( 255, 255, 0, 255 ), ParkInfoGraph.Colour( ParkInfoSeries.People ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 255, 0, 255, 255 ), ParkInfoGraph.Colour( ParkInfoSeries.Arrivals ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 255, 0, 0, 255 ), ParkInfoGraph.Colour( ParkInfoSeries.Happiness ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 0, 189, 236, 255 ), ParkInfoGraph.Colour( ParkInfoSeries.Stay ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 255, 144, 0, 255 ), ParkInfoGraph.Colour( ParkInfoSeries.Rating ) );
		Assert.AreEqual( ParkInfoSeries.People, ParkInfoGraph.InitialSeries, "0x753C48 starts at 8" );
	}

	[TestMethod]
	public void TheScaleTakesTheHighestOfTheSelectedSeries()
	{
		var rings = new System.Collections.Generic.Dictionary<ParkInfoSeries, HistoryRing>
		{
			[ParkInfoSeries.People] = HistoryRing.Of( 144, new double[] { 30 } ),
			[ParkInfoSeries.Arrivals] = HistoryRing.Of( 144, new double[] { 90 } ),
			[ParkInfoSeries.Happiness] = HistoryRing.Of( 144, new double[] { 60.9 } ),
			[ParkInfoSeries.Stay] = new HistoryRing( 144 ), [ParkInfoSeries.Rating] = new HistoryRing( 144 )
		};
		Assert.AreEqual( 30, ParkInfoGraph.ScaleMaximum( ParkInfoSeries.People, 0, series => rings[series] ) );
		Assert.AreEqual( 90, ParkInfoGraph.ScaleMaximum( ParkInfoSeries.People | ParkInfoSeries.Arrivals | ParkInfoSeries.Happiness, 0, series => rings[series] ) );
		Assert.AreEqual( 0, ParkInfoGraph.ScaleMaximum( ParkInfoSeries.None, 0, series => rings[series] ) );
	}

	[TestMethod]
	public void TheWindowHasTheTogglesPeriodButtonsAndNavigation()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		hud.OpenParkInfoWindow();
		var screen = hud.Stack.Top!;
		var table = OriginalLayoutTables.ParkInfoWindow;
		Assert.AreEqual( ParkInfoSeries.People, hud.ParkInfoSelectedSeries );
		Assert.AreEqual( 0, hud.ParkInfoPeriod );
		foreach ( var (id, control) in new[] { ("allVisitors", 0x4A68C6u), ("allStaff", 0x4A68C7u), ("allThings", 0x4A68C8u), ("period0", 0x4A68C3u), ("period1", 0x4A68C4u), ("period2", 0x4A68C2u),
			("seriesPeople", 0x4A68BFu), ("seriesArrivals", 0x4A68BCu), ("seriesHappiness", 0x4A68BDu), ("seriesStay", 0x4A68BEu), ("seriesRating", 0x4A68C0u) } )
			Assert.AreEqual( table.Control( control ).Rect, screen.Find( id )!.Bounds, id );
		Assert.IsNotNull( screen.Find( "graph" ) );
		// the toggles are independent checkboxes, the periods a radio group
		hud.SetParkInfoSeries( ParkInfoSeries.Arrivals, true );
		hud.SetParkInfoSeries( ParkInfoSeries.People, false );
		Assert.AreEqual( ParkInfoSeries.Arrivals, hud.ParkInfoSelectedSeries );
		hud.SetParkInfoPeriod( 2 );
		hud.SetParkInfoPeriod( 7 );
		Assert.AreEqual( 2, hud.ParkInfoPeriod );
		// the choice stays while the window is closed and opened again within the park
		hud.CloseParkInfoWindow();
		hud.OpenParkInfoWindow();
		Assert.AreEqual( (ParkInfoSeries.Arrivals, 2), (hud.ParkInfoSelectedSeries, hud.ParkInfoPeriod) );
	}

	[TestMethod]
	public void TheNavigationButtonsReplaceTheWindow()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		hud.OpenParkInfoWindow();
		((UiButton)hud.Stack.Top!.Find( "allStaff" )!).Clicked!();
		Assert.IsFalse( hud.ParkInfoWindowOpen );
		Assert.IsTrue( hud.AllStaffWindowOpen && !hud.StaffWindowOpen, "b_allstaff opens the All Staff window (0x496620 with -1)" );
	}
}
