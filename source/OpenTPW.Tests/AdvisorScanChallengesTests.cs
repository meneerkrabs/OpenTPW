using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the advisor's park-opening, gate, tutorial, congratulation, challenge and golden ticket group: the scan callbacks of messages 1, 2, 124, 140, 156, 157, 185, 274-276, 278-282, 314, 316 and 317,
/// the advice game code posts for a ticket win (215-217, 220-222, 225-227, 311, 315) and for the challenge system (270, 272, 273, and 344 at the first offer), and the statistics they read
/// (<see cref="EconomyParkState"/> as <see cref="IAdvisorChallengeState"/>). GPU-free; the thresholds are synthetic, not the original's.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorScanChallengesTests
{
	private const string Sam = """
		GeneralAdvisor.MinTimeAnyMessage	5
		GeneralAdvisor.MinTimeSameMessage	120
		GeneralAdvisor.MinScoreForConsideration	25
		MessageGroups[0].MinTimeSameMessage		10
		MessageGroups[0].SayOnlyOnce			0
		MessageGroups[0].DiscardAfterSlaps		0
		MessageGroups[1].MinTimeSameMessage		0
		MessageGroups[1].SayOnlyOnce			1
		MessageGroups[1].DiscardAfterSlaps		2
		MessageGroups[2].MinTimeSameMessage		0
		MessageGroups[2].SayOnlyOnce			0
		MessageGroups[2].DiscardAfterSlaps		0
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
		OpenPark.ScorePerWaitingPerson	7
		ClosePark.Score	33
		HypeNewpark.MaxMonths	6
		HypeNewpark.DroppedPercent	40
		HypeNewpark.Score	41
		BigBusloadArrived.HowBigIsBig	80
		BigBusloadArrived.Score	44
		CongratPublishPark.ValueBetterThan	1000
		CongratPublishPark.HappinessGreaterThan	60
		CongratPublishPark.VisitorsMoreThan	20
		CongratPublishPark.Score	51
		CongratSendPostcard.ValueBetterThan	2000
		CongratSendPostcard.HappinessGreaterThan	70
		CongratSendPostcard.VisitorsMoreThan	30
		CongratSendPostcard.Score	52
		TutorialUIPanel.AfterTime	10
		TutorialUIPanel.CutoffTime	20
		TutorialUIPanel.Score	61
		TutorialExplainTooltips.AfterTime	5
		TutorialExplainTooltips.CutoffTime	15
		TutorialExplainTooltips.Score	62
		GoldTicketExplanation.Score	63
		GTAmountVisitorsA.HigherPctThanThis	25
		GTAmountVisitorsA.Score	71
		GTAmountVisitorsB.HigherPctThanThis	50
		GTAmountVisitorsB.Score	72
		GTAmountVisitorsC.HigherPctThanThis	75
		GTAmountVisitorsC.Score	73
		GTAllResearchedA.HigherPctThanThis	20
		GTAllResearchedA.Score	81
		GTAllResearchedB.HigherPctThanThis	50
		GTAllResearchedB.Score	82
		GTAllResearchedC.HigherPctThanThis	80
		GTAllResearchedC.Score	83
		GoldTicketNearToFirstOne.PeopleMultiplier	0.75
		GoldTicketNearToFirstOne.Score	91
		GoldTicketNearToXPeeps.PeopleMultiplier	0.5
		GoldTicketNearToXPeeps.Score	92
		GoldTicketNearToXHappiness.HappinessMultiplier	0.9
		GoldTicketNearToXHappiness.Score	93
		GeneralWonGoldenTicket.Score	301
		WonFirstGoldenTicket.Score	302
		GoldTicketCluesForRemainder.Score	303
		Challenge.Score	304
		AwardMoved.Score	305
		""";

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static AdvisorBalance Balance( string text = Sam ) => AdvisorBalance.Parse( Encoding.ASCII.GetBytes( text ), "test.sam" );

	private sealed class FakeMoney : IAdvisorMoneyState
	{
		public int Balance => 0;
		public IReadOnlyList<AdvisorLoanSlot> LoanSlots { get; } = new AdvisorLoanSlot[AdvisorLoanSlot.Count];
		public bool LoanAvailable => false;
		public int MonthsInRed => 0;
		public int ParkMonths { get; set; } = 3;
		public IReadOnlyList<int> WageMonths { get; } = Array.Empty<int>();
		public IReadOnlyList<int> IncomeMonths { get; } = Array.Empty<int>();
	}

	/// <summary>A hand-built park: every statistic is a field, the kinds are 0 rides, 1 shops, 2 sideshows, 3 features.</summary>
	private sealed class FakePark : IAdvisorParkState, IAdvisorChallengeState
	{
		public bool Open { get; set; } = true;
		public int StartDays { get; set; }
		public int[] Standing { get; } = new int[4];
		public int Census { get; set; }
		public float Happiness { get; set; }
		public int Gate { get; set; }
		public int Value { get; set; }
		public int LastValueAge { get; private set; } = -1;
		public int BuiltPercent { get; set; }
		public int Visitors { get; set; }
		public int VisitorsThreshold { get; set; }
		public int PeopleThreshold { get; set; }
		public int HappinessThreshold { get; set; }
		public int HappyPeopleThreshold { get; set; }
		public HashSet<GoldenTicketKind> Won { get; } = new();
		public int Held { get; set; }
		public List<int> Waves { get; } = new();

		public bool ParkOpen => Open;
		public int StaffCount( StaffType type ) => 0;
		public int DaysSinceObjectStamp => 0;
		public int DaysSinceHireStamp => 0;
		public int DaysSinceStart => StartDays;
		public int AvailableTypes( int kind, bool withTicketItems ) => 0;
		public int BuiltTypes( int kind, bool withTicketItems ) => 0;
		public int ObjectsOfKind( int kind, int minimumLevel ) => Standing[kind];
		public int ClosedObjectsOfKind( int kind ) => 0;
		public int RidesBrokenDown => 0;
		public int RidesBelowCondition( int percent ) => 0;
		public int LitterPoints => 0;
		public int DevelopedCells => 0;
		public int Toilets => 0;
		public int ClosedToilets => 0;
		public int MeanToiletFilth => 0;
		public int GuestsInPark => Census;
		public int GuestsNeeding( GuestNeedMeter meter, int threshold ) => 0;
		public int GuestsWithBalloons => 0;
		public int GuestsInCostume => 0;
		public float AverageHappiness => Happiness;

		public int GuestsAtGate => Gate;
		public int ParkValue( uint maximumAge ) { LastValueAge = (int)maximumAge; return Value; }
		public int TicketItemBuiltPercent => BuiltPercent;
		public int VisitorsEver => Visitors;
		public int VisitorsTarget => VisitorsThreshold;
		public int PeopleTarget => PeopleThreshold;
		public int HappinessTarget => HappinessThreshold;
		public int HappyPeopleTarget => HappyPeopleThreshold;
		public bool TicketWon( GoldenTicketKind kind ) => Won.Contains( kind );
		public int TicketsHeld => Held;
		public IReadOnlyList<int> ArrivalWaves => Waves;
	}

	private static int Score( int message, FakePark? park, int months = 3 ) =>
		new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented ).ScoreOf( AdvisorDescriptorTable.Rows[message], new AdvisorScanContext( Balance(), new FakeMoney { ParkMonths = months }, park ), 0 );

	// ------------------------------------------------------------------ the callbacks

	[TestMethod]
	public void EveryCallbackOfTheGroupScoresZeroWithoutAParkOrItsThresholds()
	{
		var rows = AdvisorDescriptorTable.Rows.Where( row => AdvisorChallengeCallbacks.Callbacks.ContainsKey( row.Callback ) ).ToArray();
		Assert.AreEqual( AdvisorChallengeCallbacks.Callbacks.Count, rows.Length, "one descriptor row per ported callback" );
		CollectionAssert.AreEqual( AdvisorChallengeMessages.ScanScoreKeys.Select( entry => entry.MessageId ).ToArray(), rows.Select( row => row.MessageId ).ToArray(), "the scan rows the controller defines are the callbacks' rows, in table order" );
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );
		var bare = Balance( Sam.Split( "OpenPark.ScorePerWaitingPerson" )[0] );
		foreach ( var row in rows )
		{
			Assert.AreEqual( 0, scan.ScoreOf( row, new AdvisorScanContext( Balance(), new FakeMoney(), null ), 0 ), $"message {row.MessageId} without a park" );
			var park = new FakePark { Census = 1000, Gate = 9, Value = 99999, Happiness = 99, BuiltPercent = 99, Visitors = 99999, VisitorsThreshold = 10, PeopleThreshold = 10, HappinessThreshold = 1, StartDays = 12, Waves = { 90, 10 } };
			park.Standing[0] = 0;
			Assert.AreEqual( 0, scan.ScoreOf( row, new AdvisorScanContext( bare, new FakeMoney(), park ), 0 ), $"message {row.MessageId} with a settings file without its thresholds" );
		}
	}

	[TestMethod]
	public void OpenTheParkCountsTheGuestsAtTheGateOfAClosedParkWithARideOrASideshow()
	{
		var park = new FakePark { Open = false, Gate = 5 };
		Assert.AreEqual( 0, Score( 1, park ), "no ride and no sideshow built" );
		park.Standing[1] = 3;
		park.Standing[3] = 3;
		Assert.AreEqual( 0, Score( 1, park ), "shops and features do not count" );
		park.Standing[0] = 1;
		Assert.AreEqual( 5 * 7, Score( 1, park ), "5 guests x OpenPark.ScorePerWaitingPerson" );
		park.Standing[0] = 0;
		park.Standing[2] = 1;
		Assert.AreEqual( 5 * 7, Score( 1, park ), "a sideshow alone is enough" );
		park.Gate = 0;
		Assert.AreEqual( 0, Score( 1, park ), "nobody waits" );
		park.Gate = 5;
		park.Open = true;
		Assert.AreEqual( 0, Score( 1, park ), "the park is open" );
	}

	[TestMethod]
	public void CloseTheParkIsForAnOpenParkWithoutARideOrASideshow()
	{
		var park = new FakePark { Open = true };
		park.Standing[1] = 5;
		Assert.AreEqual( 33, Score( 2, park ), "open, only shops built" );
		park.Standing[0] = 1;
		Assert.AreEqual( 0, Score( 2, park ), "a ride is built" );
		park.Standing[0] = 0;
		park.Standing[2] = 1;
		Assert.AreEqual( 0, Score( 2, park ), "a sideshow is built" );
		park.Standing[2] = 0;
		park.Open = false;
		Assert.AreEqual( 0, Score( 2, park ), "the park is closed" );
	}

	[TestMethod]
	public void HypeNewParkComparesTheNewestArrivalGroupWithThePreviousOne()
	{
		var park = new FakePark { Waves = { 100, 59 } };
		Assert.AreEqual( 41, Score( 124, park, months: 5 ), "100 - 59 x 100 / 100 = 41, above DroppedPercent 40" );
		park.Waves[1] = 60;
		Assert.AreEqual( 0, Score( 124, park, months: 5 ), "a drop of exactly 40 is not above 40" );
		park.Waves.Clear();
		park.Waves.AddRange( new[] { 7, 4 } );
		Assert.AreEqual( 41, Score( 124, park, months: 5 ), "100 - 400 / 7 = 100 - 57 = 43 (the quotient is truncated)" );
		park.Waves.Clear();
		park.Waves.AddRange( new[] { 3, 2 } );
		Assert.AreEqual( 0, Score( 124, park, months: 5 ), "100 - 200 / 3 = 34" );
		park.Waves.Clear();
		park.Waves.AddRange( new[] { 10, 100, 10, 100, 59 } );
		Assert.AreEqual( 41, Score( 124, park, months: 5 ), "only the newest two groups count" );
		park.Waves.Clear();
		park.Waves.Add( 100 );
		Assert.AreEqual( 0, Score( 124, park, months: 5 ), "one group only: at least two are needed" );
		park.Waves.Clear();
		park.Waves.AddRange( new[] { 100, 59 } );
		Assert.AreEqual( 41, Score( 124, park, months: 0 ) );
		Assert.AreEqual( 0, Score( 124, park, months: 6 ), "the park is MaxMonths old" );
		foreach ( var waves in new[] { new[] { 0, 0 }, new[] { 100, 100 }, new[] { 100, 150 }, new[] { 0, 50 } } )
		{
			park.Waves.Clear();
			park.Waves.AddRange( waves );
			Assert.AreEqual( 0, Score( 124, park, months: 1 ), $"{waves[0]} then {waves[1]} is no drop (or the previous is 0)" );
		}
	}

	[TestMethod]
	public void HypeNewParkKeepsTheSignedDropInsteadOfClampingTheNewestGroup()
	{
		var balance = Balance( Sam.Replace( "DroppedPercent\t40", "DroppedPercent\t150" ) );
		var park = new FakePark { Waves = { 100, -100 } };
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );
		int Hype() => scan.ScoreOf( AdvisorDescriptorTable.Rows[124], new AdvisorScanContext( balance, new FakeMoney { ParkMonths = 1 }, park ), 0 );
		Assert.AreEqual( 41, Hype(), "the signed drop is 200 percent, above the synthetic threshold 150" );
		park.Waves[1] = 0;
		Assert.AreEqual( 0, Hype(), "clamping the newest group would give 100 percent and lose this advice" );
	}

	[TestMethod]
	public void BigBusloadIsTheNewestGroupAboveTheThreshold()
	{
		var park = new FakePark();
		Assert.AreEqual( 0, Score( 140, park ), "no group yet" );
		park.Waves.Add( 80 );
		Assert.AreEqual( 0, Score( 140, park ), "80 is not above 80" );
		park.Waves[0] = 81;
		Assert.AreEqual( 44, Score( 140, park ) );
		park.Waves.Clear();
		park.Waves.AddRange( new[] { 200, 50 } );
		Assert.AreEqual( 0, Score( 140, park ), "the newest group is the one that counts" );
		park.Waves.Clear();
		park.Waves.AddRange( new[] { 50, 81 } );
		Assert.AreEqual( 44, Score( 140, park ) );
	}

	[TestMethod]
	public void TheCongratulationsNeedValueGuestsAndHappinessStrictlyAbove()
	{
		var park = new FakePark { Value = 1001, Census = 21, Happiness = 60.5f };
		Assert.AreEqual( 51, Score( 156, park ) );
		Assert.AreEqual( 10, park.LastValueAge, "the park value is read with age 10" );
		Assert.AreEqual( 0, Score( 157, park ), "the postcard line asks for more of everything" );
		park.Value = 1000;
		Assert.AreEqual( 0, Score( 156, park ), "the value must be above 1000" );
		park.Value = 1001;
		park.Census = 20;
		Assert.AreEqual( 0, Score( 156, park ), "the guests must be above 20" );
		park.Census = 21;
		park.Happiness = 60f;
		Assert.AreEqual( 0, Score( 156, park ), "the happiness must be above 60: the integer threshold is compared as the x87 does, strictly" );
		park.Value = 2001;
		park.Census = 31;
		park.Happiness = 70.5f;
		Assert.AreEqual( 52, Score( 157, park ) );
		Assert.AreEqual( 51, Score( 156, park ), "both lines hold" );
		park.Happiness = 70f;
		Assert.AreEqual( 0, Score( 157, park ) );
		Assert.AreEqual( 51, Score( 156, park ) );
		park.Open = false;
		Assert.AreEqual( 51, Score( 156, park ), "no park-open test of its own: a closed park has happiness 0, which the statistic gives" );
	}

	[TestMethod]
	public void TheTutorialLinesRunBetweenTheirTwoDaysExclusive()
	{
		var park = new FakePark();
		foreach (var (days, first, second) in new[] { (10, 0, 62), (11, 61, 62), (14, 61, 62), (15, 61, 0), (19, 61, 0), (20, 0, 0), (5, 0, 0), (6, 0, 62), (4, 0, 0) } )
		{
			park.StartDays = days;
			Assert.AreEqual( first, Score( 185, park ), $"TutorialUIPanel at day {days}" );
			Assert.AreEqual( second, Score( 282, park ), $"TutorialExplainTooltips at day {days}" );
		}
	}

	[TestMethod]
	public void TheVisitorTargetLinesCompareTheGuestSharePercent()
	{
		var park = new FakePark { PeopleThreshold = 200 };
		foreach (var (census, a, b, c) in new[] { (50, 0, 0, 0), (51, 0, 0, 0), (52, 71, 0, 0), (100, 71, 0, 0), (101, 71, 0, 0), (102, 71, 72, 0), (151, 71, 72, 0), (152, 71, 72, 73), (1000, 71, 72, 73) } )
		{
			park.Census = census;
			Assert.AreEqual( a, Score( 274, park ), $"{census} guests: A" );
			Assert.AreEqual( b, Score( 275, park ), $"{census} guests: B" );
			Assert.AreEqual( c, Score( 276, park ), $"{census} guests: C" );
		}
		park.Census = 1000;
		park.PeopleThreshold = 0;
		Assert.AreEqual( 0, Score( 274, park ), "no target, no line" );
		park.PeopleThreshold = 200;
		park.Open = false;
		Assert.AreEqual( 0, Score( 276, park ), "the park is closed" );
	}

	[TestMethod]
	public void TheBuiltTicketItemLinesAreBandsOfThePercent()
	{
		var park = new FakePark();
		foreach (var (percent, a, b, c) in new[] { (0, 0, 0, 0), (20, 0, 0, 0), (21, 81, 0, 0), (50, 81, 0, 0), (51, 0, 82, 0), (80, 0, 82, 0), (81, 0, 0, 83), (100, 0, 0, 83) } )
		{
			park.BuiltPercent = percent;
			Assert.AreEqual( a, Score( 278, park ), $"{percent} percent: A" );
			Assert.AreEqual( b, Score( 279, park ), $"{percent} percent: B" );
			Assert.AreEqual( c, Score( 280, park ), $"{percent} percent: C" );
		}
		park.Open = false;
		Assert.AreEqual( 0, Score( 280, park ), "the park is closed" );
	}

	[TestMethod]
	public void TheGoldTicketExplanationIsSaidWhileExactlyOneArrivalGroupHasCome()
	{
		var park = new FakePark();
		Assert.AreEqual( 0, Score( 281, park ) );
		park.Waves.Add( 10 );
		Assert.AreEqual( 63, Score( 281, park ) );
		park.Waves.Add( 20 );
		Assert.AreEqual( 0, Score( 281, park ), "two groups" );
	}

	[TestMethod]
	public void TheFirstTicketLineNeedsNoTicketAndEnoughVisitors()
	{
		var park = new FakePark { VisitorsThreshold = 1000, Visitors = 750 };
		Assert.AreEqual( 0, Score( 314, park ), "750 is not above 1000 x 0.75" );
		park.Visitors = 751;
		Assert.AreEqual( 91, Score( 314, park ) );
		park.Held = 1;
		Assert.AreEqual( 0, Score( 314, park ), "the player holds a ticket" );
		park.Held = 0;
		park.VisitorsThreshold = 1001;
		park.Visitors = 750;
		Assert.AreEqual( 0, Score( 314, park ), "1001 x 0.75 = 750.75 is truncated to 750, which 750 does not pass" );
		park.Visitors = 751;
		Assert.AreEqual( 91, Score( 314, park ) );
		park.Won.Add( GoldenTicketKind.Visitors );
		Assert.AreEqual( 91, Score( 314, park ), "the park's own visitors ticket is not read: the player's count is (0x4D4AC0)" );
	}

	[TestMethod]
	public void TheVisitorsTicketHintNeedsTheTicketNotWon()
	{
		var park = new FakePark { VisitorsThreshold = 1000, Visitors = 500 };
		Assert.AreEqual( 0, Score( 316, park ), "500 is not above 1000 x 0.5" );
		park.Visitors = 501;
		Assert.AreEqual( 92, Score( 316, park ) );
		park.Won.Add( GoldenTicketKind.PeopleInPark );
		Assert.AreEqual( 92, Score( 316, park ), "only the visitors ticket (index 0) is read" );
		park.Won.Add( GoldenTicketKind.Visitors );
		Assert.AreEqual( 0, Score( 316, park ), "the visitors ticket is won" );
		Assert.AreEqual( 92, Score( 316, new FakePark { VisitorsThreshold = 1000, Visitors = 501, Open = false, Held = 5 } ), "no park-open test and no look at the tickets the player holds" );
	}

	[TestMethod]
	public void TheHappinessTicketHintNeedsHappinessGuestsAndTheTicketNotWon()
	{
		var park = new FakePark { HappinessThreshold = 75, HappyPeopleThreshold = 150, Happiness = 67.5f, Census = 151 };
		Assert.AreEqual( 93, Score( 317, park ), "75 x 0.9 = 67.5 is truncated to 67; 67.5 is above 67" );
		park.Happiness = 67f;
		Assert.AreEqual( 0, Score( 317, park ), "67 is not above 67" );
		park.Happiness = 67.5f;
		park.Census = 150;
		Assert.AreEqual( 0, Score( 317, park ), "150 guests are not above 150" );
		park.Census = 151;
		park.Won.Add( GoldenTicketKind.Visitors );
		Assert.AreEqual( 93, Score( 317, park ), "only the happiness ticket (index 2) is read" );
		park.Won.Add( GoldenTicketKind.Happiness );
		Assert.AreEqual( 0, Score( 317, park ) );
	}

	[TestMethod]
	public void NoRowOfTheGroupChangesTheScansRowOrderOrPace()
	{
		// the scan still scores the 155 rows the original scores, in table order, one per call
		var scanned = AdvisorDescriptorTable.Rows.Where( row => row.Scan ).Select( row => row.MessageId ).ToArray();
		Assert.AreEqual( 155, scanned.Length );
		var controller = new AdvisorController( Balance() );
		var park = new FakePark { Census = 300, PeopleThreshold = 200 };
		park.Standing[0] = 1;
		var visited = new List<int>();
		for ( var step = 0; step < 155; step++ )
			visited.Add( controller.ScanOnce( new FakeMoney(), 100, 0, true, park ).ScoredRow );
		CollectionAssert.AreEqual( scanned, visited.ToArray() );
	}

	// ------------------------------------------------------------------ through the real queue

	private static IReadOnlyList<(int Message, int Score)> Pass( FakePark park, bool tutorial = true, int months = 3 )
	{
		var controller = new AdvisorController( Balance() );
		var money = new FakeMoney { ParkMonths = months };
		var posted = new List<(int, int)>();
		for ( var step = 0; step < 155; step++ )
			if ( controller.ScanOnce( money, 100, 0, tutorial, park ).Posted is { } advice )
				posted.Add( (advice.MessageId, advice.Score) );
		return posted;
	}

	[TestMethod]
	public void AWholeScanPassPostsTheLinesOfAParkThatHoldsThem()
	{
		var crowd = new FakePark { Census = 152, PeopleThreshold = 200 };
		crowd.Standing[0] = 2;
		CollectionAssert.AreEqual( new[] { (274, 71), (275, 72), (276, 73) }, Pass( crowd ).ToArray(), "76 percent of the people ticket's target: all three bands" );
		var waiting = new FakePark { Open = false, Gate = 4 };
		waiting.Standing[0] = 1;
		CollectionAssert.AreEqual( new[] { (1, 28) }, Pass( waiting ).ToArray(), "four guests wait at the gate of the closed park" );
		var empty = new FakePark();
		CollectionAssert.AreEqual( new[] { (2, 33) }, Pass( empty ).ToArray(), "an open park without a ride or a sideshow" );
		var young = new FakePark { Waves = { 100, 50 } };
		young.Standing[0] = 1;
		CollectionAssert.AreEqual( new[] { (124, 41) }, Pass( young ).ToArray(), "the second arrival group is half the first: a drop of 50 percent" );
		var first = new FakePark { Waves = { 100 } };
		first.Standing[0] = 1;
		CollectionAssert.AreEqual( new[] { (140, 44), (281, 63) }, Pass( first ).ToArray(), "one group of 100: a big busload and the first-group explanation" );
	}

	[TestMethod]
	public void TheTutorialLinesAreSaidOnlyWithTheTutorialOption()
	{
		var park = new FakePark { StartDays = 12 };
		park.Standing[0] = 1;
		CollectionAssert.AreEqual( new[] { (185, 61), (282, 62) }, Pass( park ).ToArray() );
		Assert.AreEqual( 0, Pass( park, tutorial: false ).Count, "group 1 is the tutorial group" );
	}

	[TestMethod]
	public void TheCongratulationsAndTheTicketHintsGoThroughTheQueue()
	{
		var park = new FakePark { Value = 2001, Census = 31, Happiness = 71f, Visitors = 760, VisitorsThreshold = 1000, PeopleThreshold = 1000, HappinessThreshold = 100 };
		park.Standing[0] = 1;
		CollectionAssert.AreEqual( new[] { (156, 51), (157, 52), (314, 91), (316, 92) }, Pass( park ).ToArray(), "no ticket yet and 760 of 1000 visitors: both hints" );
		park.Held = 2;
		park.Won.Add( GoldenTicketKind.Visitors );
		CollectionAssert.AreEqual( new[] { (156, 51), (157, 52) }, Pass( park ).ToArray(), "a ticket held and the visitors ticket won silence both hints" );
	}

	[TestMethod]
	public void ARowNobodyPostsNeverSpeaksGenericAdvice()
	{
		var controller = new AdvisorController( Balance() );
		// 218, 219, 223, 224, 228, 229 (the extension's ticket lines), 230-233 (awards), 277, 312, 313, 318-320 have no producer in tp.exe v2.0: Advise finds no definition
		foreach ( var message in new[] { 218, 219, 223, 224, 228, 229, 230, 231, 232, 233, 277, 312, 313, 318, 319, 320 } )
			Assert.IsNull( controller.Advise( message, 100, 0, true ), $"message {message} has no producer" );
		Assert.AreEqual( 0, controller.Queue.Count );
		foreach ( var message in AdvisorChallengeMessages.EventScoreKeys.Select( entry => entry.MessageId ) )
			Assert.IsNotNull( controller.Advise( message, 100, 0, true ), $"message {message} is defined" );
	}

	// ------------------------------------------------------------------ the statistics over a real park

	private static ParkEconomy Park() => EconomyTestData.Park( initialCash: 500000 );

	[TestMethod]
	public void TheEconomyStateCountsTicketItemTypesBuilt()
	{
		var ticket = EconomyTestData.Catalog().Objects.Single( info => info.InfoId == 1112 );
		var catalog = new EconomyObjectCatalog( EconomyTestData.Catalog().Objects.Concat( new[] { ticket with { InfoId = 2001 }, ticket with { InfoId = 2002 } } ).ToArray() );
		var park = new ParkEconomy( EconomyTestData.Settings( 500000 ), catalog, ParkGameMode.FullSimulation, 7 );
		var state = new EconomyParkState( park );
		Assert.AreEqual( 0, state.TicketItemBuiltPercent, "none built" );
		park.RegisterExisting( 1100 );
		Assert.AreEqual( 0, state.TicketItemBuiltPercent, "an object without a golden ticket cost is no ticket item" );
		park.RegisterExisting( 1112 );
		park.RegisterExisting( 1112 );
		Assert.AreEqual( 33, state.TicketItemBuiltPercent, "one of the three types, 100 / 3 truncated" );
		park.RegisterExisting( 2001 );
		Assert.AreEqual( 66, state.TicketItemBuiltPercent );
		park.RegisterExisting( 2002 );
		park.Sell( park.Objects.First( item => item.InfoId == 2002 ).Id );
		Assert.AreEqual( 100, state.TicketItemBuiltPercent, "the built count is never lowered" );
		Assert.AreEqual( 0, new EconomyParkState( new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( EconomyTestData.Catalog().Objects.Where( info => info.GoldenTicketCost == 0 ).ToArray() ), ParkGameMode.FullSimulation, 7 ) ).TicketItemBuiltPercent, "no ticket item types at all" );
	}

	[TestMethod]
	public void TheEconomyStateReadsTheVisitorsTheTargetsAndTheTickets()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		Assert.AreEqual( 0, state.VisitorsEver );
		park.Counters.Add( ParkCounters.Admissions, 12 );
		Assert.AreEqual( 12, state.VisitorsEver, "the lifetime visitor counter" );
		Assert.AreEqual( (100, 200, 75, 150), (state.VisitorsTarget, state.PeopleTarget, state.HappinessTarget, state.HappyPeopleTarget), "GoldenTicketLocal.Visitors, PeopleInPark, Happiness, AtLeastThisManyHappyPeople" );
		Assert.IsFalse( state.TicketWon( GoldenTicketKind.Happiness ) );
		park.Objectives.Restore( 100, 0, null, new Dictionary<int, int>(), Array.Empty<int>(), new[] { GoldenTicketKind.Happiness, GoldenTicketKind.CamerasEverywhere, GoldenTicketKind.CoasterHeight } );
		Assert.IsTrue( state.TicketWon( GoldenTicketKind.Happiness ) );
		Assert.IsFalse( state.TicketWon( GoldenTicketKind.Visitors ) );
		Assert.AreEqual( 2, state.TicketsHeld, "without a ledger the park's own tickets stand for the player's, the secret one left out" );
		var asked = new List<IReadOnlyCollection<GoldenTicketKind>>();
		var withLedger = new EconomyParkState( park ) { PlayerTickets = tickets => { asked.Add( tickets ); return new AdvisorPlayerTickets( 9, 7, 1, Array.Empty<int>() ); } };
		Assert.AreEqual( 7, withLedger.TicketsHeld, "the ledger's sum without the secret tickets" );
		Assert.AreEqual( 3, asked.Single().Count, "the ledger is asked with the park's tickets" );
		Assert.AreEqual( 2, new EconomyParkState( park ) { PlayerTickets = _ => null }.TicketsHeld, "a ledger that does not answer leaves the park's own" );
	}

	[TestMethod]
	public void TheEconomyStateReadsTheParkValueWithoutWritingTheCache()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		park.RegisterExisting( 1100 );
		Assert.IsNull( park.CachedNetWorth );
		var fresh = state.ParkValue( 10 );
		Assert.IsTrue( fresh > 0, "the ride's cost counts" );
		Assert.IsNull( park.CachedNetWorth, "reading wrote no cache" );
		Assert.AreEqual( fresh, park.NetWorth( 0 ), "the first reader that stamps the cache" );
		park.RegisterExisting( 1180 );
		Assert.AreEqual( fresh, state.ParkValue( 10 ), "inside its window the cached count answers, as the original's does" );
		EconomyTestData.AdvanceToTurn( park, 400 );
		Assert.IsTrue( state.ParkValue( 10 ) > fresh, "past the window the park is counted again" );
		Assert.AreEqual( fresh, park.CachedNetWorth!.Value.Value, "and still nothing was written" );
	}

	[TestMethod]
	public void TheGuestStatisticsCountTheGuestsWaitingAtTheTwoBooths()
	{
		var grid = new GuestPathGrid( 30, 24 );
		for ( var x = 5; x <= 25; x++ )
			grid.SetPath( x, 17, true );
		var settings = GuestTests.CreateSettings();
		settings.ArrivalLaneA = new[] { (4, 5), (9, 5), (9, 9), (9, 13), (9, 17) };
		settings.ArrivalLaneB = new[] { (20, 5), (10, 5), (10, 9), (10, 13), (10, 17) };
		var simulation = new GuestSimulation( grid, settings, 3 ) { ArrivalsEnabled = false };
		var economy = EconomyTestData.Park();
		var bridge = new GuestEconomyBridge( () => economy, simulation );
		Assert.AreEqual( 0, bridge.GuestsAtGate );
		Guest At( (int X, int Y) cell, GuestState state )
		{
			var guest = simulation.SpawnInPark( 9, 17 );
			(guest.Navigation.X, guest.Navigation.Y) = GuestNavigation.Centre( cell );
			(guest.CellX, guest.CellY) = cell;
			guest.State = state;
			return guest;
		}
		At( (9, 13), GuestState.WaitingAtBooth );
		At( (9, 13), GuestState.WaitingAtBooth );
		At( (10, 13), GuestState.WaitingAtBooth );
		At( (9, 13), GuestState.AtTicketBooth );
		At( (12, 13), GuestState.WaitingAtBooth );
		At( (9, 17), GuestState.WaitingAtBooth );
		Assert.AreEqual( 3, bridge.GuestsAtGate, "state 3 on the cell of booth A (9, 13) or booth B (10, 13) only" );
		Assert.AreEqual( 3, simulation.GuestsWaitingAtBooths() );
	}

	[TestMethod]
	public void TheArrivalUpdateReportsEveryGroupItStarts()
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		var settings = GuestTests.CreateSettings();
		var simulation = new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = true };
		var expected = simulation.ArrivalGroupSize();
		var sizes = new List<int>();
		simulation.ArrivalGroupStarted += sizes.Add;
		for ( var turn = 0; turn < 2000 && sizes.Count < 2; turn++ )
			simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
		Assert.IsTrue( sizes.Count >= 1, "a group started" );
		Assert.AreEqual( expected, sizes[0], "the size the group starts with, 0x004CF5B0's" );
		Assert.IsTrue( sizes[0] > 0 );
	}

	// ------------------------------------------------------------------ the arrival ring and the advisor's record of it

	[TestMethod]
	public void TheArrivalRingKeepsTheNewestFiveSizes()
	{
		var ring = new AdvisorArrivalRing();
		Assert.AreEqual( 0, ring.Waves.Count );
		for ( var size = 1; size <= 3; size++ )
			ring.Push( size * 10 );
		CollectionAssert.AreEqual( new[] { 10, 20, 30 }, ring.Waves.ToArray() );
		for ( var size = 4; size <= 7; size++ )
			ring.Push( size * 10 );
		CollectionAssert.AreEqual( new[] { 30, 40, 50, 60, 70 }, ring.Waves.ToArray(), "the sixth push overwrites the oldest" );
		ring.Restore( new[] { 1, 2, -3, 4, -5, 6, -7 } );
		CollectionAssert.AreEqual( new[] { -3, 4, -5, 6, -7 }, ring.Waves.ToArray(), "signed sizes are preserved through restore and eviction" );
		ring.Clear();
		Assert.AreEqual( 0, ring.Waves.Count );
	}

	private static IReadOnlyList<int> Pending( AutomaticAdvisor advisor ) =>
		advisor.Controller.CaptureSnapshot().Queue.Slots.Where( slot => slot != null ).Select( slot => slot!.MessageId ).ToArray();

	private static IReadOnlyList<(int Message, int Variant)> PendingVariants( AutomaticAdvisor advisor ) =>
		advisor.Controller.CaptureSnapshot().Queue.Slots.Where( slot => slot != null ).Select( slot => (slot!.MessageId, slot.ResponseVariantOverride) ).ToArray();

	private static (ParkEconomy Park, AutomaticAdvisor Advisor) Attached( ParkEconomy? park = null )
	{
		park ??= EconomyTestData.Park();
		var advisor = new AutomaticAdvisor( new AdvisorController( Balance() ), () => 0 );
		advisor.AttachEconomy( park );
		return (park, advisor);
	}

	[TestMethod]
	public void TheAdvisorKeepsTheArrivalGroupsAndTheScanSaysTheBusloadHypeAndFirstGroupLines()
	{
		var (park, advisor) = Attached();
		var money = new EconomyMoneyState( park );
		IReadOnlyList<(int, int)> Run()
		{
			var posted = new List<(int, int)>();
			for ( var step = 0; step < 155; step++ )
				if ( advisor.Controller.ScanOnce( money, 100, 0, true, new EconomyParkState( park ) { ArrivalWaves = advisor.ArrivalWaves } ).Posted is { } advice )
					posted.Add( (advice.MessageId, advice.Score) );
			return posted;
		}
		Assert.AreEqual( 0, Run().Count, "no group has come" );
		advisor.OnArrivalGroupStarted( 100 );
		CollectionAssert.AreEqual( new[] { (140, 44), (281, 63) }, Run().ToArray(), "exactly one group, and 100 is above the busload threshold of 80" );
		advisor.OnArrivalGroupStarted( 50 );
		CollectionAssert.AreEqual( new[] { (124, 41) }, Run().ToArray(), "the second group is half the first, in a park of 0 months" );
	}

	[TestMethod]
	public void TheSnapshotKeepsSignedArrivalGroupsThroughJsonAndContinuesTheRing()
	{
		var (_, advisor) = Attached();
		foreach ( var size in new[] { 40, 25, -1, 0, -100 } )
			advisor.OnArrivalGroupStarted( size );
		var snapshot = advisor.CaptureSnapshot();
		advisor.ValidateSnapshot( snapshot );
		var envelope = ParkResumeSaveFileTests.Envelope() with { Advisor = snapshot };
		var bytes = ParkResumeSaveFile.Serialize( envelope );
		var saved = ParkResumeSaveFile.Deserialize( bytes ).Advisor!;
		CollectionAssert.AreEqual( new[] { 40, 25, -1, 0, -100 }, saved.ArrivalWaves!.ToArray() );
		using var restored = new AutomaticAdvisor( new AdvisorController( Balance() ), () => 0 );
		restored.RestoreSnapshot( saved );
		CollectionAssert.AreEqual( snapshot.ArrivalWaves!.ToArray(), restored.ArrivalWaves.ToArray() );
		foreach ( var size in new[] { -2, 80, -3 } )
		{
			advisor.OnArrivalGroupStarted( size );
			restored.OnArrivalGroupStarted( size );
			CollectionAssert.AreEqual( advisor.ArrivalWaves.ToArray(), restored.ArrivalWaves.ToArray(), "the resumed ring evicts the same oldest entry" );
		}
		CollectionAssert.AreEqual( new[] { 0, -100, -2, 80, -3 }, restored.ArrivalWaves.ToArray() );

		var legacy = JsonNode.Parse( bytes )!;
		Assert.IsTrue( legacy["Advisor"]!.AsObject().Remove( "ArrivalWaves" ) );
		var older = ParkResumeSaveFile.Deserialize( Encoding.UTF8.GetBytes( legacy.ToJsonString() ) ).Advisor!;
		Assert.IsNull( older.ArrivalWaves );
		restored.RestoreSnapshot( older );
		Assert.AreEqual( 0, restored.ArrivalWaves.Count, "a legacy JSON save without the field clears an already populated ring" );
	}

	[TestMethod]
	public void InvalidArrivalHistorySpeechAndControllerSnapshotsLeaveTheAdvisorUnchanged()
	{
		using var advisor = new AutomaticAdvisor( new AdvisorController( Balance() ), () => 700 );
		advisor.Controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, liveGameTick: 20, gameType: 0, tutorialEnabled: true );
		advisor.OnArrivalGroupStarted( -7 );
		var before = JsonSerializer.Serialize( advisor.CaptureSnapshot() );
		using var source = new AutomaticAdvisor( new AdvisorController( Balance() ), () => 9000 );
		source.OnArrivalGroupStarted( -10 );
		var snapshot = source.CaptureSnapshot() with { ObjectStampTurn = 55, HireStampTurn = 66 };
		foreach ( var invalid in new[]
		{
			snapshot with { ArrivalWaves = new[] { -1, 2, 3, 4, 5, 6 } },
			snapshot with { SpeechWasActive = true },
			snapshot with { Controller = snapshot.Controller with { Queue = snapshot.Controller.Queue with { Slots = Array.Empty<AdvisorPendingAdviceSnapshot?>() } } }
		} )
		{
			Assert.ThrowsException<InvalidDataException>( () => advisor.ValidateSnapshot( invalid ) );
			Assert.AreEqual( before, JsonSerializer.Serialize( advisor.CaptureSnapshot() ), "validation is read-only" );
			Assert.ThrowsException<InvalidDataException>( () => advisor.RestoreSnapshot( invalid ) );
			Assert.AreEqual( before, JsonSerializer.Serialize( advisor.CaptureSnapshot() ), "a rejected restore preserves the ring, clock, stamps and queued advice" );
		}
	}

	// ------------------------------------------------------------------ the key rule and the ticket lines

	[TestMethod]
	public void TheKeyRuleGivesOutcomeOneTwoOrThree()
	{
		var costs = new[] { 1, 1, 3, 5 };
		foreach (var (earned, extra, expected) in new[] { (1, 1, 1), (2, 1, 1), (3, 1, 2), (4, 1, 1), (6, 1, 3), (9, 1, 2), (12, 1, 3), (3, 0, 3), (0, 0, 1), (0, 1, 3) } )
			Assert.AreEqual( expected, AdvisorTicketAdvice.Outcome( new AdvisorPlayerTickets( earned, earned, extra, costs ) ), $"{earned} tickets, {extra} extra keys: {extra + earned / 3} keys" );
		Assert.AreEqual( 2, AdvisorTicketAdvice.Outcome( new AdvisorPlayerTickets( 6, 6, 1, Array.Empty<int>() ) ), "no known park cost: a key earned is outcome 2" );
	}

	[TestMethod]
	public void EachTicketKindHasItsMessageForTheOutcomeAndItsTypeAsResponse()
	{
		var locals = new[] { GoldenTicketKind.Visitors, GoldenTicketKind.PeopleInPark, GoldenTicketKind.Happiness, GoldenTicketKind.AllResearchedAndBuilt, GoldenTicketKind.ProfitYear, GoldenTicketKind.RecentVisitors };
		for ( var index = 0; index < locals.Length; index++ )
			for ( var outcome = 1; outcome <= 3; outcome++ )
				Assert.AreEqual( (214 + outcome, index), AdvisorTicketAdvice.For( locals[index], outcome ), $"{locals[index]} outcome {outcome}" );
		var globals = new[] { GoldenTicketKind.CoasterHeight, GoldenTicketKind.GokartExcitement, GoldenTicketKind.WaterLength, GoldenTicketKind.BigPark };
		for ( var index = 0; index < globals.Length; index++ )
			for ( var outcome = 1; outcome <= 3; outcome++ )
				Assert.AreEqual( (219 + outcome, index), AdvisorTicketAdvice.For( globals[index], outcome ), $"{globals[index]} outcome {outcome}" );
		for ( var outcome = 1; outcome <= 3; outcome++ )
			Assert.AreEqual( (224 + outcome, 0), AdvisorTicketAdvice.For( GoldenTicketKind.CamerasEverywhere, outcome ) );
		Assert.IsNull( AdvisorTicketAdvice.For( GoldenTicketKind.AllLandOwned, 1 ), "tp.exe never awards an all-land ticket" );
		// the descriptors give each message as many responses as the kind has types: 6 local, 4 global, 2 secret
		var rows = AdvisorDescriptorTable.Rows;
		Assert.IsTrue( new[] { 215, 216, 217 }.All( id => rows[id].ResponseCount == 6 ) );
		Assert.IsTrue( new[] { 220, 221, 222 }.All( id => rows[id].ResponseCount == 4 ) );
		Assert.IsTrue( new[] { 225, 226, 227 }.All( id => rows[id].ResponseCount == 2 ) );
	}

	[TestMethod]
	public void TheLedgerViewCountsTheLedgerAndTheParkTicketsOnce()
	{
		var ledger = PlayerTicketLedger.Restore( new PlayerTicketLedger().Capture() with { ExtraKeys = 1 } );
		ledger.RecordEarned( "jungle", GoldenTicketKind.Visitors );
		ledger.RecordEarned( "hallow", GoldenTicketKind.Visitors );
		ledger.RecordGlobal( "hallow", GoldenTicketKind.CoasterHeight, 120 );
		var view = ledger.AdvisorView( "jungle", new[] { GoldenTicketKind.Visitors, GoldenTicketKind.PeopleInPark, GoldenTicketKind.CamerasEverywhere }, new[] { 1, 3 } );
		Assert.AreEqual( 5, view.Earned, "jungle visitors (once), hallow visitors, the coaster ticket, jungle people and the camera ticket" );
		Assert.AreEqual( 4, view.EarnedWithoutSecret );
		Assert.AreEqual( 1 + 5 / 3, view.Keys );
		CollectionAssert.AreEqual( new[] { 1, 3 }, view.ThemeKeyCosts.ToArray() );
		Assert.AreEqual( 3, ledger.EarnedTickets, "the ledger itself recorded nothing" );
		var own = AdvisorPlayerTickets.ForPark( new[] { GoldenTicketKind.Visitors, GoldenTicketKind.CoasterHeight, GoldenTicketKind.CamerasEverywhere } );
		Assert.AreEqual( (3, 2), (own.Earned, own.EarnedWithoutSecret) );
		Assert.AreEqual( 1, AdvisorPlayerTickets.ForPark( Array.Empty<GoldenTicketKind>() ).Keys, "the player starts with one key" );
	}

	private sealed class Crowd : IParkGuestStatistics
	{
		public int PeopleInPark => 300;
		public float AverageHappiness => 0;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	[TestMethod]
	public void ALaterWinOfTheBatchThatTheLedgerHoldsAlreadyStillCountsForTheEarlierWin()
	{
		(ParkEconomy Park, AutomaticAdvisor Advisor, GoldenTicketKind First, GoldenTicketKind Second) Batch()
		{
			var park = EconomyTestData.Park();
			var (_, advisor) = Attached( park );
			var counters = new ParkCounters();
			counters.Add( ParkCounters.Admissions, 100_000 );
			var won = park.Objectives.CheckGoldenTickets( counters, new Crowd(), park.Research, 0, new long[7], parkSeconds: 0 );
			Assert.AreEqual( 2, won.Count, "one check wins two tickets" );
			return (park, advisor, park.Objectives.LastWonTickets[0], park.Objectives.LastWonTickets[1]);
		}
		// the ledger already holds the later win (a replayed theme): the player has two tickets when the first is told, so there is no first-ticket line
		var (_, replayed, first, second) = Batch();
		var ledger = new PlayerTicketLedger();
		ledger.RecordEarned( "jungle", second );
		replayed.PlayerTickets = held => ledger.AdvisorView( "jungle", held, new[] { 1, 3 } );
		replayed.OnParkEvent( Won( first ) );
		CollectionAssert.DoesNotContain( Pending( replayed ).ToArray(), AdvisorTicketAdvice.FirstTicketMessage, "the ledger's own ticket is not subtracted again" );
		CollectionAssert.DoesNotContain( Pending( replayed ).ToArray(), AdvisorTicketAdvice.CluesMessage );
		// without it the player's first ticket is the first of the batch
		var (_, fresh, firstFresh, _) = Batch();
		fresh.PlayerTickets = held => new PlayerTicketLedger().AdvisorView( "jungle", held, new[] { 1, 3 } );
		fresh.OnParkEvent( Won( firstFresh ) );
		CollectionAssert.IsSubsetOf( new[] { AdvisorTicketAdvice.FirstTicketMessage, AdvisorTicketAdvice.CluesMessage }, Pending( fresh ).ToArray() );
		// and a park without a ledger counts the same way
		var (_, own, firstOwn, _) = Batch();
		own.OnParkEvent( Won( firstOwn ) );
		CollectionAssert.IsSubsetOf( new[] { AdvisorTicketAdvice.FirstTicketMessage, AdvisorTicketAdvice.CluesMessage }, Pending( own ).ToArray() );
	}

	[TestMethod]
	public void AnEstablishedParkWithoutARecordedArrivalRingTakesTheRingAsFullSoTheFirstGroupLineStaysSilent()
	{
		var ring = new AdvisorArrivalRing();
		ring.AssumeFilled();
		CollectionAssert.AreEqual( new[] { 0, 0, 0, 0, 0 }, ring.Waves.ToArray() );
		ring.Restore( new[] { 7, 9 } );
		ring.AssumeFilled();
		CollectionAssert.AreEqual( new[] { 0, 0, 0, 7, 9 }, ring.Waves.ToArray(), "the known sizes stay the newest" );
		ring.Push( 11 );
		CollectionAssert.AreEqual( new[] { 0, 0, 7, 9, 11 }, ring.Waves.ToArray() );

		// a park with admissions: the advisor's ring reads five groups after an older save, and 281 (exactly one group) is not scored
		var established = EconomyTestData.Park();
		established.Counters.Add( ParkCounters.Admissions, 12 );
		var (_, loaded) = Attached( established );
		loaded.RestoreSnapshot( loaded.CaptureSnapshot() with { ArrivalWaves = null } );
		Assert.AreEqual( AdvisorArrivalRing.Capacity, loaded.ArrivalWaves.Count );
		var money = new EconomyMoneyState( established );
		var posted = new List<int>();
		for ( var step = 0; step < 155; step++ )
			if ( loaded.Controller.ScanOnce( money, 100, 0, true, new EconomyParkState( established ) { ArrivalWaves = loaded.ArrivalWaves } ).Posted is { } advice )
				posted.Add( advice.MessageId );
		CollectionAssert.DoesNotContain( posted, 281, "the first-group explanation fires only while the ring holds exactly one group" );
		// the snapshot keeps it, and a snapshot that has a ring (even a short one) is taken as it is
		Assert.AreEqual( AdvisorArrivalRing.Capacity, loaded.CaptureSnapshot().ArrivalWaves!.Count );
		loaded.RestoreSnapshot( loaded.CaptureSnapshot() with { ArrivalWaves = new[] { 100 } } );
		CollectionAssert.AreEqual( new[] { 100 }, loaded.ArrivalWaves.ToArray() );
		// a park without admissions keeps its empty ring
		var (_, fresh) = Attached( EconomyTestData.Park() );
		fresh.RestoreSnapshot( fresh.CaptureSnapshot() with { ArrivalWaves = null } );
		Assert.AreEqual( 0, fresh.ArrivalWaves.Count );
	}

	private static ParkEvent Won( GoldenTicketKind kind ) => new( 0, ParkEventKind.GoldenTicketWon, 0, 0, (long)kind, kind.ToString() );

	[TestMethod]
	public void TheFirstGoldenTicketOfAParkSaysItsLineAndTheFirstTicketLines()
	{
		var (park, advisor) = Attached();
		park.Counters.Add( ParkCounters.Admissions, 101 );
		EconomyTestData.AdvanceToTurn( park, 100 );
		CollectionAssert.AreEqual( new[] { (215, 0), (311, -1), (315, -1) }, PendingVariants( advisor ).ToArray(), "the visitors ticket, no key earned: 215 with the ticket's type as the response, then the first-ticket lines" );
		Assert.AreEqual( 301, advisor.Controller.Balance.ScoreOf( 215 ), "GeneralWonGoldenTicket.Score" );
		Assert.AreEqual( 302, advisor.Controller.Balance.ScoreOf( 311 ) );
		Assert.AreEqual( 303, advisor.Controller.Balance.ScoreOf( 315 ) );
	}

	[TestMethod]
	public void ASecondTicketOfTheSameCheckCountsOverTheOnesBeforeIt()
	{
		var (park, advisor) = Attached();
		park.Counters.Add( ParkCounters.Admissions, 101 );
		park.GuestStatistics = new TwoHundredGuests();
		EconomyTestData.AdvanceToTurn( park, 100 );
		Assert.AreEqual( 2, park.Objectives.GoldenTickets.Count );
		CollectionAssert.AreEqual( new[] { (215, 0), (311, -1), (315, -1), (215, 1) }, PendingVariants( advisor ).ToArray(), "the first win is the player's only ticket; the second is the second" );
	}

	private sealed class TwoHundredGuests : IParkGuestStatistics
	{
		public int PeopleInPark => 201;
		public float AverageHappiness => 0;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	[TestMethod]
	public void TheKeyRulePicksTheMessageOfTheOutcomeFromThePlayersLedger()
	{
		foreach (var (earned, extra, message) in new[] { (5, 1, 215), (6, 1, 217), (9, 1, 216), (12, 1, 217) } )
		{
			var (park, advisor) = Attached();
			advisor.PlayerTickets = _ => new AdvisorPlayerTickets( earned, earned, extra, new[] { 1, 1, 3, 5 } );
			advisor.OnParkEvent( Won( GoldenTicketKind.Happiness ) );
			CollectionAssert.AreEqual( new[] { (message, 2) }, PendingVariants( advisor ).ToArray(), $"{earned} tickets: Happiness is type 2, and the player has {earned} tickets so no first-ticket line" );
			Assert.AreEqual( 0, park.Objectives.GoldenTickets.Count, "the event alone changed nothing" );
		}
	}

	[TestMethod]
	public void GlobalAndSecretTicketsHaveTheirOwnMessages()
	{
		var (_, advisor) = Attached();
		advisor.PlayerTickets = _ => new AdvisorPlayerTickets( 4, 3, 1, new[] { 1, 1, 3, 5 } );
		advisor.OnParkEvent( Won( GoldenTicketKind.WaterLength ) );
		advisor.OnParkEvent( Won( GoldenTicketKind.CamerasEverywhere ) );
		advisor.OnParkEvent( Won( GoldenTicketKind.AllLandOwned ) );
		CollectionAssert.AreEqual( new[] { (220, 2), (225, 0) }, PendingVariants( advisor ).ToArray(), "water length is global type 2; the camera ticket is the secret type 0; the all-land ticket is never awarded" );
		var (_, keyed) = Attached();
		keyed.PlayerTickets = _ => new AdvisorPlayerTickets( 6, 5, 1, new[] { 1, 1, 3, 5 } );
		keyed.OnParkEvent( Won( GoldenTicketKind.BigPark ) );
		keyed.OnParkEvent( Won( GoldenTicketKind.CamerasEverywhere ) );
		CollectionAssert.AreEqual( new[] { (222, 3), (227, 0) }, PendingVariants( keyed ).ToArray(), "the third key opens a park: outcome 3" );
	}

	[TestMethod]
	public void AMovedGlobalTicketAlsoRunsTheFirstTicketLines()
	{
		var (_, advisor) = Attached();
		advisor.PlayerTickets = _ => new AdvisorPlayerTickets( 1, 1, 1, Array.Empty<int>() );
		advisor.OnParkEvent( new ParkEvent( 0, ParkEventKind.GlobalTicketMoved, 0, 0, (long)GoldenTicketKind.CoasterHeight, "CoasterHeight" ) );
		CollectionAssert.AreEqual( new[] { 234, 311, 315 }, Pending( advisor ).ToArray(), "0x4D5270 posts 234 + type and ends in 0x4D5B30 like every outcome" );
		var (_, many) = Attached();
		many.PlayerTickets = _ => new AdvisorPlayerTickets( 4, 4, 1, Array.Empty<int>() );
		many.OnParkEvent( new ParkEvent( 0, ParkEventKind.GlobalTicketMoved, 0, 0, (long)GoldenTicketKind.BigPark, "BigPark" ) );
		CollectionAssert.AreEqual( new[] { 237 }, Pending( many ).ToArray() );
	}

	[TestMethod]
	public void TicketAdviceIsFullSimulationAdviceOnly()
	{
		var controller = new AdvisorController( Balance() );
		foreach ( var message in new[] { 215, 220, 225, 270, 272, 273, 311, 315 } )
		{
			Assert.IsNull( controller.Advise( message, 100, 2, true ), $"Instant Action excludes {message}" );
			Assert.IsTrue( controller.Advise( message, 100, 0, true ) is { Stored: true }, $"the full simulation posts {message}" );
		}
		Assert.AreEqual( 8, controller.Queue.Count );
	}

	[TestMethod]
	public void ATicketLineWithoutTheGroupInASmallerSettingsFileIsLeftOutNotFatal()
	{
		var noGroupTwo = string.Join( '\n', Sam.Split( '\n' ).Where( line => !line.Contains( "MessageGroups[2]" ) ) );
		var controller = new AdvisorController( Balance( noGroupTwo ) );
		Assert.IsNull( controller.Advise( 215, 100, 0, true ), "no controls for group 2: no descriptor" );
		Assert.IsTrue( controller.Advise( 311, 100, 0, true ) is { Stored: true }, "311 is group 0" );
	}

	// ------------------------------------------------------------------ the challenge system

	private const long Day = ParkCalendar.SecondsPerDay;
	private const long Start = 540 * Day;

	private static ChallengeDefinition Definition( int type, int followup = 0 ) => new( 0, type, followup, 10, 1000, 0, 1100, 0, 700, false, true );

	private static ParkEconomy ChallengePark( params ChallengeDefinition[] definitions )
	{
		var settings = EconomyTestData.Settings( 500000 );
		settings.AdoptChallenges( definitions );
		return new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
	}

	/// <summary>A park whose first challenge is offered and accepted (before the advisor is attached), then attached.</summary>
	private static (ParkEconomy Park, AutomaticAdvisor Advisor) Accepted( params ChallengeDefinition[] definitions )
	{
		var park = ChallengePark( definitions );
		park.Objectives.Update( Start, 1, _ => 0 ).ToList();
		park.Objectives.AcceptAt( Start, 0 );
		return Attached( park );
	}

	private static long CheckedTurn( long seconds )
	{
		var turn = seconds / ParkCalendar.SecondsPerTurn + 1;
		while ( (turn & 3) == 0 )
			turn++;
		return turn;
	}

	[TestMethod]
	public void TheFirstChallengeOfferSaysTheFirstOfferLineAndTheOthersTheOfferLine()
	{
		var (park, advisor) = Attached();
		park.OpenPark();
		park.UpdateChallenges( Start / ParkCalendar.SecondsPerTurn + 1 );
		Assert.IsNotNull( park.Objectives.Current );
		Assert.IsTrue( park.Objectives.LastOfferSwitchedOn );
		CollectionAssert.AreEqual( new[] { 344 }, Pending( advisor ).ToArray(), "the offer that switches the system on posts 344 (0x158), not 270" );
		park.DeclineChallenge();
		park.UpdateChallenges( (Start + 271 * Day) / ParkCalendar.SecondsPerTurn + 1 );
		Assert.IsNotNull( park.Objectives.Current );
		Assert.IsFalse( park.Objectives.LastOfferSwitchedOn );
		CollectionAssert.AreEqual( new[] { 344, 270 }, Pending( advisor ).ToArray(), "the later offer posts 270 (0x10E)" );
		Assert.AreEqual( 304, advisor.Controller.Balance.ScoreOf( 270 ), "Challenge.Score" );
	}

	[TestMethod]
	public void AWonChallengeWithoutAFollowUpSaysTheWinLine()
	{
		var (park, advisor) = Accepted( Definition( 11 ) );
		park.Counters.Add( ParkCounters.Admissions, 1000 );
		park.UpdateChallenges( CheckedTurn( Start + Day ) );
		Assert.IsNull( park.Objectives.Current );
		CollectionAssert.AreEqual( new[] { 272 }, Pending( advisor ).ToArray() );
	}

	[TestMethod]
	public void AFollowUpOfferSaysTheOfferLineThenTheWinLineOfTheLastPartOfTheChain()
	{
		foreach ( var nextFollowup in new[] { 0, 13 } )
		{
			var (park, advisor) = Accepted( Definition( 11, followup: 12 ), Definition( 12, followup: nextFollowup ) );
			park.Counters.Add( ParkCounters.Admissions, 1000 );
			park.UpdateChallenges( CheckedTurn( Start + Day ) );
			Assert.IsTrue( park.Objectives.Current is { Accepted: false }, "the follow-up is offered" );
			Assert.IsFalse( park.Objectives.LastOfferSwitchedOn );
			CollectionAssert.AreEqual( nextFollowup == 0 ? new[] { 270, 272 } : new[] { 270 }, Pending( advisor ).ToArray(),
				nextFollowup == 0 ? "the follow-up has no follow-up of its own: its offer, then the win line (0x4D2B60 before 0x4D1A00)" : "the follow-up leads on: only its offer" );
		}
	}

	[TestMethod]
	public void AWonChallengeThatHasNoFollowUpToOfferSaysNothing()
	{
		var (park, advisor) = Accepted( Definition( 11, followup: 12 ) );
		park.Counters.Add( ParkCounters.Admissions, 1000 );
		park.UpdateChallenges( CheckedTurn( Start + Day ) );
		Assert.IsNull( park.Objectives.Current );
		Assert.AreEqual( 0, Pending( advisor ).Count, "the slot still names a follow-up type, so 0x4D1A00 posts no win line" );
	}

	[TestMethod]
	public void ALostChallengeSaysTheLossLine()
	{
		var (park, advisor) = Accepted( Definition( 11 ) );
		var deadline = park.Objectives.Current!.EndSeconds;
		park.UpdateChallenges( CheckedTurn( deadline ) );
		Assert.IsNull( park.Objectives.Current );
		CollectionAssert.AreEqual( new[] { 273 }, Pending( advisor ).ToArray() );
	}

	[TestMethod]
	public void TheChallengeLinesAreFullSimulationAdviceOfTheMainGroup()
	{
		var rows = AdvisorDescriptorTable.Rows;
		foreach ( var message in new[] { 270, 272, 273 } )
			Assert.AreEqual( ((byte)0, (byte)0), (rows[message].GameMode, rows[message].Group), $"message {message}" );
		Assert.AreEqual( (byte)1, rows[344].Group, "the first offer's line is a tutorial group help message" );
	}

	// ------------------------------------------------------------------ the inventory and the shipped settings

	[TestMethod]
	public void TheInventoryCallsEveryRowOfTheGroupWired()
	{
		var text = File.ReadAllText( Shipped( "content/data/advisor-producers.toml" ) );
		var ids = AdvisorChallengeMessages.ScanScoreKeys.Concat( AdvisorChallengeMessages.EventScoreKeys ).Select( entry => entry.MessageId ).ToArray();
		Assert.AreEqual( 18 + 14, ids.Length );
		foreach ( var id in ids )
		{
			var row = Regex.Match( text, $@"^\s*\{{ message = {id}, kind = ""(\w+)"", batch = ""(\w+)"", status = ""(\w+)""", RegexOptions.Multiline );
			Assert.IsTrue( row.Success, $"message {id} has a row" );
			Assert.AreEqual( "wired", row.Groups[3].Value, $"message {id}" );
		}
		Assert.IsFalse( Regex.IsMatch( text, @"batch = ""(challenges|other|tutorial)"", status = ""ready""" ), "no ready row is left in the batches" );
	}

	private static string Shipped( string relative )
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var path = System.IO.Path.Combine( directory.FullName, relative );
			if ( File.Exists( path ) )
				return path;
		}
		Assert.Fail( $"{relative} not found above the test directory." );
		return "";
	}

	[TestMethod]
	public void EveryThresholdOfTheGroupExistsInTheShippedSettings()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Advisor.sam." );
		var file = Directory.EnumerateFiles( gamePath!, "Advisor.sam", SearchOption.AllDirectories ).FirstOrDefault();
		if ( file == null )
			Assert.Inconclusive( "The original Advisor/Advisor.sam is missing." );
		var balance = AdvisorBalance.Parse( File.ReadAllBytes( file! ), file! );
		foreach ( var key in AdvisorChallengeCallbacks.IntegerKeys.Concat( AdvisorChallengeCallbacks.FloatKeys ) )
			Assert.IsTrue( balance.ScanParameters.ContainsKey( key ), $"{key} is read from Advisor.sam" );
		foreach ( var key in AdvisorChallengeCallbacks.FloatKeys )
			Assert.AreEqual( (float)balance.ScanParameters[key], (float)(double)(float)balance.ScanParameters[key], $"{key} is a single" );
		foreach ( var (message, key, fixedScore) in AdvisorChallengeMessages.ScanScoreKeys.Concat( AdvisorChallengeMessages.EventScoreKeys ).Where( entry => entry.FixedScore == null ) )
			Assert.IsTrue( balance.Scores.ContainsKey( key ), $"message {message}: {key}" );
		foreach ( var group in new[] { 0, 1, 2, 3, 9 } )
			Assert.IsTrue( balance.Groups.ContainsKey( group ), $"group {group}" );
		// every message of the group has a descriptor in the settings' controls
		var controller = new AdvisorController( balance );
		foreach ( var message in AdvisorChallengeMessages.ScanScoreKeys.Concat( AdvisorChallengeMessages.EventScoreKeys ).Select( entry => entry.MessageId ) )
			Assert.IsTrue( controller.Queue.HasDescriptor( message ), $"message {message}" );
	}
}
