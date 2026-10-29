using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the last nine computed scan callbacks (12, 19, 21, 22, 27, 98, 113, 141 and 264: <see cref="AdvisorInputsCallbacks"/>) and the inputs they read (the guards' chase counters, the patrol
/// coverage of a class of cells, the mean ride excitement and the first unconnected queue, <see cref="EconomyParkState"/> as <see cref="IAdvisorInputsState"/>).
/// GPU-free; the thresholds and scores of the test settings are invented, each one different so that a swapped key shows.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorScanInputsTests
{
	private const string Header = """
		GeneralAdvisor.MinTimeAnyMessage	5
		GeneralAdvisor.MinTimeSameMessage	120
		GeneralAdvisor.MinScoreForConsideration	25
		MessageGroups[0].MinTimeSameMessage		10
		MessageGroups[0].SayOnlyOnce			0
		MessageGroups[0].DiscardAfterSlaps		0
		MessageGroups[1].MinTimeSameMessage		0
		MessageGroups[1].SayOnlyOnce			1
		MessageGroups[1].DiscardAfterSlaps		2
		MessageGroups[3].MinTimeSameMessage		0
		MessageGroups[3].SayOnlyOnce			0
		MessageGroups[3].DiscardAfterSlaps		0
		MessageGroups[9].MinTimeSameMessage		0
		MessageGroups[9].SayOnlyOnce			0
		MessageGroups[9].DiscardAfterSlaps		0
		Welcome.Score 9000
		Bankrupted.Score	8000
		ParkNowOpen.Score	20
		ParkNowClosed.Score	20
		PrebuiltPark.Score	700
		HelpMessage.Score	1000
		HelpMessage.ContextHelpScore	1500
		RidesBreakdownImminent.Score	100
		RidesBreakdownNoMechanics.Score	105
		RidesBreakdownBusyMechanics.Score	107
		RidesBreakdownMechanicOnIt.Score	110
		RideCondemned.Score	100
		StaffHireGuards2.Score	10
		StaffHireGuards2.AdditionalPerMisbehavingKid	5
		StaffSetMechanicArea.MonthsIntoGameForConsideration	13
		StaffSetMechanicArea.AtLeastThisMany	3
		StaffMechanicCoverage.Score	300
		StaffMechanicCoverage.ScorePerNonCoveredPercent	5
		StaffSetHandymanArea.MonthsIntoGameForConsideration	15
		StaffHandymanCoverage.Score	100
		StaffHandymanCoverage.ScorePerNonCoveredPercent	4
		StaffGuardBuildCamera.AcceptableTrouble	4
		StaffGuardBuildCamera.Score	45
		StaffSetEntertainerArea.MonthsIntoGameForConsideration	18
		StaffEntertainerCoverage.Score	400
		StaffEntertainerCoverage.ScorePerNonCoveredPercent	6
		GeneralLaxSecurity.MoreThanThisGotAway	1
		GeneralLaxSecurity.ScorePerGetAway	20
		QueueUnconnected.Score	87
		VisitorsTooScared.AverageScarierThan	70
		VisitorsTooScared.MinimumRides	5
		VisitorsTooScared.Score	50
		LotsOfPranks.MorePrankstersThan	2
		LotsOfPranks.Score	100
		""";

	private static AdvisorBalance Balance( string? without = null ) =>
		AdvisorBalance.Parse( Encoding.ASCII.GetBytes( without == null ? Header : string.Join( "\n", Header.Split( '\n' ).Where( line => !line.TrimStart().StartsWith( without, StringComparison.Ordinal ) ) ) ), "test.sam" );

	private static AdvisorScanContext Context( IAdvisorParkState? park, AdvisorBalance? balance = null ) => new( balance ?? Balance(), new FakeMoney(), park );

	private static int Score( int message, IAdvisorParkState? park, AdvisorBalance? balance = null ) =>
		new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented ).ScoreOf( AdvisorDescriptorTable.Rows[message], Context( park, balance ), 0 );

	private sealed class FakeMoney : IAdvisorMoneyState
	{
		public int Balance => 0;
		public IReadOnlyList<AdvisorLoanSlot> LoanSlots { get; } = new AdvisorLoanSlot[AdvisorLoanSlot.Count];
		public bool LoanAvailable => false;
		public int MonthsInRed => 0;
		public int ParkMonths => 12;
		public IReadOnlyList<int> WageMonths { get; } = Array.Empty<int>();
		public IReadOnlyList<int> IncomeMonths { get; } = Array.Empty<int>();
	}

	/// <summary>A park state without the inputs view: only what <see cref="IAdvisorParkState"/> asks.</summary>
	private class PlainPark : IAdvisorParkState
	{
		public virtual bool ParkOpen => true;
		public virtual int StaffCount( StaffType type ) => 0;
		public int DaysSinceObjectStamp => 0;
		public int DaysSinceHireStamp => 0;
		public virtual int DaysSinceStart => 0;
		public int AvailableTypes( int kind, bool withTicketItems ) => 0;
		public int BuiltTypes( int kind, bool withTicketItems ) => 0;
		public virtual int ObjectsOfKind( int kind, int minimumLevel ) => 0;
		public int ClosedObjectsOfKind( int kind ) => 0;
		public int RidesBrokenDown => 0;
		public int RidesBelowCondition( int percent ) => 0;
		public int LitterPoints => 0;
		public int DevelopedCells => 0;
		public int Toilets => 0;
		public int ClosedToilets => 0;
		public int MeanToiletFilth => 0;
		public int GuestsInPark => 0;
		public int GuestsNeeding( GuestNeedMeter meter, int threshold ) => 0;
		public int GuestsWithBalloons => 0;
		public int GuestsInCostume => 0;
		public float AverageHappiness => 0;
	}

	/// <summary>A hand-built park state with the inputs view and the staff view the coverage callbacks also read.</summary>
	private sealed class InputsPark : PlainPark, IAdvisorInputsState, IAdvisorStaffState
	{
		public bool Open { get; set; } = true;
		public int Days { get; set; } = 30 * 20;
		public int Rides { get; set; } = 6;
		public Dictionary<StaffType, int> Counts { get; } = new();
		public Dictionary<StaffType, int> Patrolled { get; } = new();
		public Dictionary<(StaffType, int), int?> Coverage { get; } = new();
		public int Month { get; set; }
		public int GotAway { get; set; }
		public int? Excitement { get; set; } = 0;
		public uint LastAge { get; private set; }
		public bool? Unconnected { get; set; } = false;

		public override bool ParkOpen => Open;
		public override int DaysSinceStart => Days;
		public override int StaffCount( StaffType type ) => Counts.GetValueOrDefault( type );
		public override int ObjectsOfKind( int kind, int minimumLevel ) => kind == 0 ? Rides : 0;
		public int MisbehavingKids( bool gotAway ) => gotAway ? GotAway : Month;
		public int? PatrolCoveragePercent( StaffType role, int cellClass ) => Coverage.GetValueOrDefault( (role, cellClass) );
		public int? AverageRideExcitement( uint maximumAge ) { LastAge = maximumAge; return Excitement; }
		public bool? AnyQueueUnconnected => Unconnected;

		public int StaffTotal => Counts.Values.Sum();
		public int StaffWithPatrolArea( StaffType? role ) => role is { } one ? Patrolled.GetValueOrDefault( one ) : Patrolled.Values.Sum();
		public int AverageTraining( StaffType role ) => 0;
		public float AverageStaffHappiness => 0;
		public int TiredStaff => 0;
		public int TrainingBudget( StaffType role ) => 0;
		public bool HaveEverTrained => false;
		public int VisitorsEver => 0;
		public int GuestPercentBelowHappiness( int happiness ) => 0;
		public int StaffRooms => 0;
		public int ClosedStaffRooms => 0;
		public int StaffLimit( StaffType role ) => 10;
	}

	private static readonly int[] Messages = { 12, 19, 21, 22, 27, 98, 113, 141, 264 };

	// ------------------------------------------------------------------ the table

	[TestMethod]
	public void EveryInputsCallbackIsTheCallbackOfItsDescriptorRow()
	{
		CollectionAssert.AreEquivalent( Messages, AdvisorInputsCallbacks.ScoreKeys.Select( entry => entry.MessageId ).ToArray() );
		Assert.AreEqual( 9, AdvisorInputsCallbacks.Callbacks.Count );
		foreach ( var message in Messages )
		{
			var row = AdvisorDescriptorTable.Rows[message];
			Assert.IsTrue( row.Scan, $"message {message} is scored by the scan" );
			Assert.IsTrue( AdvisorInputsCallbacks.Callbacks.ContainsKey( row.Callback ), $"message {message}: callback 0x{row.Callback:X} is ported" );
			Assert.IsTrue( AdvisorScanCallbacks.Implemented.ContainsKey( row.Callback ), $"message {message} is in the scan's callbacks" );
		}
		Assert.IsTrue( AdvisorInputsCallbacks.Callbacks.Keys.All( address => Messages.Any( message => AdvisorDescriptorTable.Rows[message].Callback == address ) ), "every callback has a row" );
	}

	[TestMethod]
	public void EveryInputsCallbackScoresZeroWithoutAParkAnInputsViewOrItsThresholds()
	{
		var full = new InputsPark { Month = 50, GotAway = 50, Excitement = 99, Unconnected = true, Days = 30 * 100 };
		foreach ( var role in Enum.GetValues<StaffType>() )
			(full.Counts[role], full.Patrolled[role]) = (5, 5);
		foreach ( var role in new[] { StaffType.Mechanic, StaffType.Handyman, StaffType.Entertainer } )
			for ( var cellClass = 0; cellClass < 4; cellClass++ )
				full.Coverage[(role, cellClass)] = 0;
		var bare = AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Header.Split( "StaffHireGuards2.Score" )[0] ), "bare.sam" );
		foreach ( var message in Messages )
		{
			Assert.AreEqual( 0, Score( message, null ), $"message {message} without a park" );
			Assert.AreEqual( 0, Score( message, new PlainPark() ), $"message {message} with a park that has no inputs view" );
			Assert.AreEqual( 0, Score( message, full, bare ), $"message {message} with a settings file without its thresholds" );
			if ( message == 12 )
				full.Counts[StaffType.Guard] = 0;
			Assert.IsTrue( Score( message, full ) > 0, $"message {message} scores with everything in place" );
			full.Counts[StaffType.Guard] = 5;
		}
	}

	[TestMethod]
	public void EveryInputsThresholdExistsInTheShippedSettings()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !System.IO.Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Advisor.sam." );
		var file = System.IO.Directory.EnumerateFiles( gamePath!, "Advisor.sam", System.IO.SearchOption.AllDirectories ).FirstOrDefault();
		if ( file == null )
			Assert.Inconclusive( "The original Advisor/Advisor.sam is missing." );
		var balance = AdvisorBalance.Parse( System.IO.File.ReadAllBytes( file! ), file! );
		foreach ( var key in AdvisorInputsCallbacks.IntegerKeys )
			Assert.IsTrue( balance.ScanParameters.ContainsKey( key ), $"{key} is read from Advisor.sam" );
		foreach ( var key in AdvisorInputsCallbacks.ScoreKeys.Select( entry => entry.ScoreKey ) )
			Assert.IsTrue( balance.Scores.ContainsKey( key ), key );
		foreach ( var message in Messages )
			Assert.IsTrue( balance.Groups.ContainsKey( AdvisorDescriptorTable.Rows[message].Group ), $"the group of message {message} has its controls" );
	}

	// ------------------------------------------------------------------ the guards' counters

	[TestMethod]
	public void TheHireGuardsLineCountsTheKidsTheGuardsWentAfter()
	{
		var park = new InputsPark { Month = 3 };
		Assert.AreEqual( 3 * 5 + 10, Score( 12, park ), "kids x AdditionalPerMisbehavingKid + Score" );
		park.Month = 0;
		Assert.AreEqual( 10, Score( 12, park ), "no kid: the flat word" );
		park.Month = -1;
		Assert.AreEqual( 5, Score( 12, park ), "the words are signed and not clamped" );
		park.Month = 3;
		park.GotAway = 40;
		Assert.AreEqual( 25, Score( 12, park ), "the got-away word is not read" );
		park.Counts[StaffType.Guard] = 1;
		Assert.AreEqual( 0, Score( 12, park ), "a guard is hired" );
		park.Counts.Clear();
		park.Counts[StaffType.Handyman] = 2;
		Assert.AreEqual( 25, Score( 12, park ), "only a guard counts" );
		park.Open = false;
		Assert.AreEqual( 0, Score( 12, park ), "the park is closed" );
	}

	[TestMethod]
	public void TheCameraAndLaxSecurityLinesCountTheKidsThatGotAway()
	{
		var park = new InputsPark { GotAway = 5, Month = 90 };
		park.Counts[StaffType.Guard] = 2;
		Assert.AreEqual( 45, Score( 22, park ), "a guard and more than AcceptableTrouble (4) got away: the flat score" );
		park.GotAway = 4;
		Assert.AreEqual( 0, Score( 22, park ), "exactly the threshold does not pass" );
		park.GotAway = 5;
		park.Counts.Clear();
		Assert.AreEqual( 0, Score( 22, park ), "no guard: build a camera is no advice, hiring one is" );
		Assert.AreEqual( 100, Score( 98, park ), "5 x ScorePerGetAway (20): no guard needed, no open test" );
		park.GotAway = 1;
		Assert.AreEqual( 0, Score( 98, park ), "exactly MoreThanThisGotAway (1) does not pass" );
		park.GotAway = 2;
		park.Open = false;
		Assert.AreEqual( 40, Score( 98, park ) );
	}

	[TestMethod]
	public void TheManyPrankstersLineCountsTheKidsThatWereChased()
	{
		var park = new InputsPark { Month = 3, GotAway = 0, Open = false };
		Assert.AreEqual( 100, Score( 264, park ), "more than MorePrankstersThan (2) in the month; no park-open test" );
		park.Month = 2;
		Assert.AreEqual( 0, Score( 264, park ) );
		park.Month = 2;
		park.GotAway = 99;
		Assert.AreEqual( 0, Score( 264, park ), "the month word, not the got-away word" );
	}

	// ------------------------------------------------------------------ the coverage lines

	[TestMethod]
	public void TheMechanicCoverageLineScoresTheUncoveredEntrancesAfterTheGates()
	{
		var park = new InputsPark();
		park.Counts[StaffType.Mechanic] = 3;
		park.Patrolled[StaffType.Mechanic] = 1;
		park.Coverage[(StaffType.Mechanic, 2)] = 40;
		Assert.AreEqual( (100 - 40) * 5, Score( 19, park ), "(100 - coverage) x ScorePerNonCoveredPercent, the entrance class" );
		park.Coverage[(StaffType.Mechanic, 2)] = 100;
		Assert.AreEqual( 0, Score( 19, park ), "fully covered: 0" );
		park.Coverage[(StaffType.Mechanic, 2)] = 45;
		Assert.AreEqual( 0, Score( 19, park ), "275 is below the floor Score (300)" );
		park.Coverage[(StaffType.Mechanic, 2)] = 40;
		Assert.AreEqual( 300, Score( 19, park ), "exactly the floor passes (a signed less-than)" );
		park.Counts[StaffType.Mechanic] = 2;
		Assert.AreEqual( 0, Score( 19, park ), "fewer than AtLeastThisMany (3)" );
		park.Counts[StaffType.Mechanic] = 3;
		park.Patrolled[StaffType.Mechanic] = 0;
		Assert.AreEqual( 0, Score( 19, park ), "no mechanic with a patrol area of his own" );
		park.Patrolled[StaffType.Mechanic] = 1;
		park.Days = 30 * 13 - 1;
		Assert.AreEqual( 0, Score( 19, park ), "the park is not 13 months old" );
		park.Days = 30 * 13;
		Assert.AreEqual( 300, Score( 19, park ) );
		park.Coverage.Remove( (StaffType.Mechanic, 2) );
		Assert.AreEqual( 0, Score( 19, park ), "no cell map: nothing to cover" );
	}

	[TestMethod]
	public void TheHandymanAndEntertainerCoverageLinesNeedOnlyOneOfTheRole()
	{
		var park = new InputsPark();
		park.Counts[StaffType.Handyman] = 1;
		park.Coverage[(StaffType.Handyman, 3)] = 50;
		Assert.AreEqual( 50 * 4, Score( 21, park ), "the path and queue class, ScorePerNonCoveredPercent 4: no minimum count and no area test" );
		park.Coverage[(StaffType.Handyman, 3)] = 75;
		Assert.AreEqual( 100, Score( 21, park ), "25 x 4 = 100: exactly the floor passes" );
		park.Coverage[(StaffType.Handyman, 3)] = 76;
		Assert.AreEqual( 0, Score( 21, park ), "96 is below the floor" );
		park.Coverage[(StaffType.Handyman, 3)] = 0;
		park.Days = 30 * 15 - 1;
		Assert.AreEqual( 0, Score( 21, park ), "the handymen's own months threshold (15)" );
		park.Days = 30 * 15;
		Assert.AreEqual( 400, Score( 21, park ), "an area-less handyman still scores: every cell uncovered" );
		park.Counts.Clear();
		Assert.AreEqual( 0, Score( 21, park ), "no handyman" );

		park.Days = 30 * 20;
		park.Counts[StaffType.Entertainer] = 1;
		park.Coverage[(StaffType.Entertainer, 1)] = 90;
		Assert.AreEqual( 0, Score( 27, park ), "10 x 6 = 60 is below the floor 400" );
		park.Coverage[(StaffType.Entertainer, 1)] = 20;
		Assert.AreEqual( 480, Score( 27, park ), "the built-cells class, 80 x 6" );
		park.Days = 30 * 18 - 1;
		Assert.AreEqual( 0, Score( 27, park ), "the entertainers' months threshold (18)" );
	}

	// ------------------------------------------------------------------ the queue and the excitement

	[TestMethod]
	public void TheUnconnectedQueueLineScoresAnyQueueThatJoinsNoPath()
	{
		var park = new InputsPark { Unconnected = true, Open = false };
		Assert.AreEqual( 87, Score( 113, park ), "no park-open test, the flat score" );
		park.Unconnected = false;
		Assert.AreEqual( 0, Score( 113, park ) );
		park.Unconnected = null;
		Assert.AreEqual( 0, Score( 113, park ), "no world that answers" );
	}

	[TestMethod]
	public void TheTooScaredLineReadsTheMeanExcitementWithTheAgeTen()
	{
		var park = new InputsPark { Excitement = 71 };
		Assert.AreEqual( 50, Score( 141, park ) );
		Assert.AreEqual( 10u, park.LastAge, "the cached reader is asked with the age 10" );
		park.Excitement = 70;
		Assert.AreEqual( 0, Score( 141, park ), "exactly AverageScarierThan does not pass" );
		park.Excitement = 99;
		park.Rides = 4;
		Assert.AreEqual( 0, Score( 141, park ), "fewer rides than MinimumRides (5)" );
		park.Rides = 5;
		Assert.AreEqual( 50, Score( 141, park ) );
		park.Open = false;
		Assert.AreEqual( 0, Score( 141, park ), "the park is closed" );
		park.Open = true;
		park.Excitement = null;
		Assert.AreEqual( 0, Score( 141, park ), "no world that reads the rides" );
	}

	// ------------------------------------------------------------------ the economy's inputs

	private static ParkEconomy Park() => EconomyTestData.Park( initialCash: 500000 );

	[TestMethod]
	public void TheChaseCountersFollowTheOriginalsThreeWordsAndTheMonthCloseZeroesTwo()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		Assert.AreEqual( (0, 0), (state.MisbehavingKids( false ), state.MisbehavingKids( true )) );
		park.RecordGuardChase( caught: true );
		park.RecordGuardChase( caught: true );
		park.RecordGuardChase( caught: false );
		Assert.AreEqual( (3, 1), (state.MisbehavingKids( false ), state.MisbehavingKids( true )), "every chase counts, only an abandoned one got away" );
		Assert.AreEqual( 3L, park.Counters[ParkCounters.MisbehavingKidsEver] );
		// the month close of the analyser
		park.AdvanceMonths( 1 );
		Assert.IsTrue( park.Ledger.History.Count >= 1, "a month closed" );
		Assert.AreEqual( (0, 0), (state.MisbehavingKids( false ), state.MisbehavingKids( true )), "the month close zeroes the month and got-away words" );
		Assert.AreEqual( 3L, park.Counters[ParkCounters.MisbehavingKidsEver], "the lifetime word stays" );
	}

	[TestMethod]
	public void TheChaseCountersSurviveAnOpenTpwSave()
	{
		var park = Park();
		park.RecordGuardChase( caught: false );
		park.RecordGuardChase( caught: true );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), EconomyTestData.Settings( 500000 ), EconomyTestData.Catalog() );
		var state = new EconomyParkState( restored );
		Assert.AreEqual( (2, 1), (state.MisbehavingKids( false ), state.MisbehavingKids( true )) );
		Assert.AreEqual( 2L, restored.Counters[ParkCounters.MisbehavingKidsEver] );
		// a save written before the counters has none: the words start at 0
		Assert.AreEqual( (0, 0), (new EconomyParkState( Park() ).MisbehavingKids( false ), new EconomyParkState( Park() ).MisbehavingKids( true )) );
	}

	/// <summary>A 10 x 10 map: a row of path cells, one entrance cell and one queue cell, a water cell and a cell without terrain.</summary>
	private static (ParkEconomy Park, GuestPathGrid Grid) MapPark()
	{
		var park = Park();
		var grid = new GuestPathGrid( 10, 10 );
		for ( var x = 0; x < 4; x++ )
			grid.SetPath( x, 0, true );
		grid.Cells.SetType( 4, 0, ParkCellType.Queue );
		grid.Cells.SetType( 5, 0, ParkCellTypes.Entrance );
		grid.Cells.SetType( 6, 0, ParkCellTypes.Water );
		grid.Cells.SetType( 7, 0, ParkCellTypes.NoTerrain );
		park.Litter.Cells = grid.Cells;
		return (park, grid);
	}

	[TestMethod]
	public void ThePatrolCoverageCountsTheMarkedCellsOfEachClass()
	{
		var (park, _) = MapPark();
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var state = new EconomyParkState( park );
		Assert.AreEqual( 0, state.PatrolCoveragePercent( StaffType.Handyman, 3 ), "no area of his own marks nothing" );
		// the path and queue class: four path cells, the queue cell and the entrance cell (types 1, 3 and 9)
		handyman.SetPatrolArea( (0, 0), (1, 5) );
		Assert.AreEqual( 2 * 100 / 6, state.PatrolCoveragePercent( StaffType.Handyman, 3 ), "two of six cells (a signed integer division)" );
		handyman.SetPatrolArea( (0, 0), (5, 0) );
		Assert.AreEqual( 100, state.PatrolCoveragePercent( StaffType.Handyman, 3 ) );
		// the entrance class: the one entrance cell
		Assert.AreEqual( 100, state.PatrolCoveragePercent( StaffType.Handyman, 2 ) );
		handyman.SetPatrolArea( (0, 0), (4, 9) );
		Assert.AreEqual( 0, state.PatrolCoveragePercent( StaffType.Handyman, 2 ) );
		// the built class holds the same six cells (types 1, 3 and 9) and no other
		Assert.AreEqual( 5 * 100 / 6, state.PatrolCoveragePercent( StaffType.Handyman, 1 ) );
		// the land class leaves out the water and the cell without terrain: 100 cells - 2
		handyman.SetPatrolArea( (0, 0), (9, 0) );
		Assert.AreEqual( 8 * 100 / 98, state.PatrolCoveragePercent( StaffType.Handyman, 0 ) );
		// only the rectangles of the role mark
		Assert.AreEqual( 0, state.PatrolCoveragePercent( StaffType.Mechanic, 3 ) );
		mechanic.SetPatrolArea( (0, 0), (0, 0) );
		Assert.AreEqual( 100 / 6, state.PatrolCoveragePercent( StaffType.Mechanic, 3 ) );
		// without a cell map nothing is known
		park.Litter.Cells = null;
		Assert.IsNull( state.PatrolCoveragePercent( StaffType.Handyman, 3 ) );
	}

	[TestMethod]
	public void ThePatrolCoverageIsZeroWhenNoCellHasTheClass()
	{
		var park = Park();
		park.Litter.Cells = new GuestPathGrid( 4, 4 ).Cells;
		var state = new EconomyParkState( park );
		Assert.AreEqual( 0, state.PatrolCoveragePercent( StaffType.Mechanic, 2 ), "no entrance cell: 0 counted cells give 0 (0x4CD6E3)" );
	}

	[TestMethod]
	public void TheMeanExcitementIsCachedForTheAgeInTurnsDividedByFour()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		Assert.IsNull( state.AverageRideExcitement( 10 ), "no world that reads the rides" );
		var excitement = new Dictionary<int, int>();
		park.RideLiveExcitement = instance => excitement.GetValueOrDefault( instance );
		Assert.AreEqual( 0, state.AverageRideExcitement( 10 ), "no ride: 0" );
		var first = park.RegisterExisting( 1100 );
		var second = park.RegisterExisting( 1100 );
		var shop = park.RegisterExisting( 1203 );
		excitement[first.Id] = 60;
		excitement[second.Id] = 31;
		excitement[shop.Id] = 255;
		Assert.AreEqual( 0, state.AverageRideExcitement( 10 ), "still the value stamped at the first read" );
		Assert.AreEqual( 0, state.AverageRideExcitement( 0 ), "age 0 of at most 0: the cache holds (an unsigned compare of the age with the reader's)" );
		park.Advance( ParkCalendar.TickOfTurn( park.Turn + 4 ) - park.Tick );
		Assert.AreEqual( 45, state.AverageRideExcitement( 0 ), "age 1 is above 0: counted again, (60 + 31) / 2 truncated, the shop is no ride" );
		excitement[first.Id] = 100;
		Assert.AreEqual( 45, state.AverageRideExcitement( 10 ) );
		park.Advance( ParkCalendar.TickOfTurn( park.Turn + 43 ) - park.Tick );
		Assert.AreEqual( 45, state.AverageRideExcitement( 10 ), "the age (turn >> 2) - (stamp >> 2) is 10 at turn 47 (the stamp is turn 4)" );
		park.Advance( ParkCalendar.TickOfTurn( park.Turn + 1 ) - park.Tick );
		Assert.AreEqual( 65, state.AverageRideExcitement( 10 ), "past the age: counted again, (100 + 31) / 2" );
		excitement[second.Id] = 0x1FF;
		park.Advance( ParkCalendar.TickOfTurn( park.Turn + 4 ) - park.Tick );
		Assert.AreEqual( 177, state.AverageRideExcitement( 0 ), "only the low byte of each ride's answer counts: (100 + 255) / 2" );
	}

	// ------------------------------------------------------------------ the researchers' flag (0x5041F0)

	[TestMethod]
	public void TheLabFlagIsEdgeTriggeredAndPostsMessageFiveOrSixOnlyWhenItChanges()
	{
		var park = Park();
		var messages = new List<int>();
		park.Staff.LabMessage += messages.Add;
		Assert.IsFalse( park.Staff.LabActive );
		var first = EconomyTestData.HireBest( park, StaffType.Researcher );
		CollectionAssert.AreEqual( new[] { 5 }, messages, "the first researcher sets the byte and posts 5" );
		Assert.IsTrue( park.Staff.LabActive );
		var second = park.Hire( park.Staff.Candidates.Where( candidate => candidate.Type == StaffType.Researcher ).OrderBy( candidate => candidate.Id ).First().Id );
		EconomyTestData.HireBest( park, StaffType.Handyman );
		CollectionAssert.AreEqual( new[] { 5 }, messages, "a second researcher and another role change nothing" );
		park.Fire( first.Id );
		CollectionAssert.AreEqual( new[] { 5 }, messages, "one is left: the byte stays set" );
		park.Fire( second.Id );
		CollectionAssert.AreEqual( new[] { 5, 6 }, messages, "none left: the byte is cleared and 6 posted" );
		Assert.IsFalse( park.Staff.LabActive );
		EconomyTestData.HireBest( park, StaffType.Researcher );
		CollectionAssert.AreEqual( new[] { 5, 6, 5 }, messages );
	}

	[TestMethod]
	public void TheLabFlagTakesNoDrawAndFollowsARestoredPark()
	{
		var park = Park();
		var before = park.World.State;
		EconomyTestData.HireBest( park, StaffType.Researcher );
		Assert.AreEqual( before, park.World.State, "the flag update takes no draw (0x5041F0)" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), EconomyTestData.Settings( 500000 ), EconomyTestData.Catalog() );
		Assert.IsTrue( restored.Staff.LabActive, "the flag is what the researchers make of it after a load, with no message" );
		var messages = new List<int>();
		restored.Staff.LabMessage += messages.Add;
		restored.Fire( restored.Staff.OfType( StaffType.Researcher ).Single().Id );
		CollectionAssert.AreEqual( new[] { 6 }, messages );
		Assert.IsFalse( Park().Staff.LabActive, "a new park has no active lab" );
	}
}
