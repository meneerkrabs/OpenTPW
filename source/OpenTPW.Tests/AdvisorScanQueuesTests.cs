using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the price, shop value and research callbacks of the advisor scan (messages 15-17, 61, 63, 64, 72, 76, 80, 91, 92 and 238-257: <see cref="AdvisorQueuesCallbacks"/>), the statistics they read
/// (<see cref="EconomyParkState"/> as <see cref="IAdvisorQueuesState"/>) and the booth's opinion of the entrance fee that fills the ring behind 91 and 92. GPU-free; the thresholds are synthetic, not the shipped values.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorScanQueuesTests
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
		MessageGroups[3].MinTimeSameMessage		0
		MessageGroups[3].SayOnlyOnce			0
		MessageGroups[3].DiscardAfterSlaps		0
		MessageGroups[4].MinTimeSameMessage		0
		MessageGroups[4].SayOnlyOnce			0
		MessageGroups[4].DiscardAfterSlaps		0
		Welcome.Score 9000
		Bankrupted.Score	8000
		ParkNowOpen.Score	20
		ParkNowClosed.Score	20
		PrebuiltPark.Score	700
		HelpMessage.Score	1000
		HelpMessage.ContextHelpScore	1500
		StaffHireResearchers1.Most							40
		StaffHireResearchers1.PointsPerExtraPct				3
		StaffHireResearchers2.Score							61
		StaffHireResearchers3.MonthsIntoGameForConsideration	6
		StaffHireResearchers3.SlowerThan					33
		StaffHireResearchers3.Score							47
		RidesResearchMore.Score								71
		ShopsResearchMore.Score								72
		SideshowsResearchMore.Score							73
		FeaturesResearchMore.Score							74
		RidesNoUpgrades.MonthsIntoGameForConsideration		4
		RidesNoUpgrades.Score								65
		RidesMoreUpgrades.LessAvailableThan					2
		RidesMoreUpgrades.Score								66
		PricingTicketTooExpensive.TicketSatisfactionUnder	35
		PricingTicketTooExpensive.Score						111
		PricingTicketTooCheap.TicketSatisfactionOver		85
		PricingTicketTooCheap.Score							112
		FoodPoorValue.ValueWorseThan						40
		FoodPoorValue.ScorePerPoint							-7
		ShopsPoorValue.ValueWorseThan						41
		ShopsPoorValue.ScorePerPoint						-6
		RestaurantPoorValue.ValueWorseThan					42
		RestaurantPoorValue.ScorePerPoint					-5
		DrinksPoorValue.ValueWorseThan						43
		DrinksPoorValue.ScorePerPoint						-4
		SideshowsPoorValue.ValueWorseThan					44
		SideshowsPoorValue.ScorePerPoint					-3
		FoodPoorSatisfaction.SatisfactionWorseThan			30
		FoodPoorSatisfaction.ScorePerPoint					-9
		ShopsPoorSatisfaction.SatisfactionWorseThan			31
		ShopsPoorSatisfaction.ScorePerPoint					-8
		RestaurantPoorSatisfaction.SatisfactionWorseThan	32
		RestaurantPoorSatisfaction.ScorePerPoint			-7
		DrinksPoorSatisfaction.SatisfactionWorseThan		33
		DrinksPoorSatisfaction.ScorePerPoint				-6
		SideshowsPoorSatisfaction.SatisfactionWorseThan		34
		SideshowsPoorSatisfaction.ScorePerPoint				-5
		FoodGreatValue.ValueBetterThan						70
		FoodGreatValue.ScorePerPoint						2
		ShopsGreatValue.ValueBetterThan						71
		ShopsGreatValue.ScorePerPoint						3
		RestaurantGreatValue.ValueBetterThan				72
		RestaurantGreatValue.ScorePerPoint					4
		DrinksGreatValue.ValueBetterThan					73
		DrinksGreatValue.ScorePerPoint						5
		SideshowsGreatValue.ValueBetterThan					74
		SideshowsGreatValue.ScorePerPoint					6
		FoodGreatSatisfaction.SatisfactionBetterThan		60
		FoodGreatSatisfaction.ScorePerPoint					2
		ShopsGreatSatisfaction.SatisfactionBetterThan		61
		ShopsGreatSatisfaction.ScorePerPoint				3
		RestaurantGreatSatisfaction.SatisfactionBetterThan	62
		RestaurantGreatSatisfaction.ScorePerPoint			4
		DrinksGreatSatisfaction.SatisfactionBetterThan		63
		DrinksGreatSatisfaction.ScorePerPoint				5
		SideshowsGreatSatisfaction.SatisfactionBetterThan	64
		SideshowsGreatSatisfaction.ScorePerPoint			6
		""";

	/// <summary>The messages this batch wires, in the order of the descriptor table.</summary>
	private static readonly int[] Messages = new[] { 15, 16, 17, 61, 63, 64, 72, 76, 80, 91, 92 }.Concat( Enumerable.Range( 238, 20 ) ).ToArray();

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static AdvisorBalance Balance( string? sam = null ) => AdvisorBalance.Parse( Encoding.ASCII.GetBytes( sam ?? Sam ), "test.sam" );

	private sealed class FakeMoney : IAdvisorMoneyState
	{
		public int Months { get; set; } = 12;
		public int Balance => 0;
		public IReadOnlyList<AdvisorLoanSlot> LoanSlots { get; } = new AdvisorLoanSlot[AdvisorLoanSlot.Count];
		public bool LoanAvailable => false;
		public int MonthsInRed => 0;
		public int ParkMonths => Months;
		public IReadOnlyList<int> WageMonths { get; } = Array.Empty<int>();
		public IReadOnlyList<int> IncomeMonths { get; } = Array.Empty<int>();
	}

	/// <summary>A hand-built park view: the statistics are fields; kinds 0 rides, 1 shops, 2 sideshows, 3 features; research categories 0 rides, 1 shops, 2 sideshows, 3 features, 4 upgrades.</summary>
	private sealed class FakeState : IAdvisorParkState, IAdvisorQueuesState
	{
		public bool Open { get; set; } = true;
		public int Researchers { get; set; }
		public int[] Available { get; } = new int[4];
		public int[] BuiltTypeCount { get; } = new int[4];
		public int[] Outstanding { get; } = new int[5];
		public int[] Rates { get; } = new int[5];
		public int Candidates { get; set; }
		public int Done { get; set; }
		public int Capacity { get; set; }
		public int UpgradedTypes { get; set; }
		public bool RingFull { get; set; }
		public int RingMean { get; set; }
		public int LastRingCategory { get; private set; } = -1;
		public Dictionary<(string Helper, AdvisorShopGroup Group), AdvisorShopPick?> Picks { get; } = new();
		public List<string> Calls { get; } = new();
		public bool? LastTicketFlag { get; private set; }

		public bool ParkOpen => Open;
		public int StaffCount( StaffType type ) => type == StaffType.Researcher ? Researchers : 0;
		public int DaysSinceObjectStamp => 0;
		public int DaysSinceHireStamp => 0;
		public int DaysSinceStart => 0;
		public int AvailableTypes( int kind, bool withTicketItems ) { LastTicketFlag = withTicketItems; return Available[kind]; }
		public int BuiltTypes( int kind, bool withTicketItems ) => BuiltTypeCount[kind];
		public int ObjectsOfKind( int kind, int minimumLevel ) => 0;
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

		public bool PriceRingHoldsFifty( int category ) { LastRingCategory = category; return RingFull; }
		public int PriceRingMean( int category ) { LastRingCategory = category; return RingMean; }
		public int ResearchOutstanding( ResearchCategory category ) => Outstanding[(int)category];
		public int ResearchRate( ResearchCategory category ) => Rates[(int)category];
		public int ResearcherCandidates => Candidates;
		public int ResearchDone => Done;
		public int ResearchCapacity => Capacity;
		public int ResearchedUpgradeTypes => UpgradedTypes;

		private AdvisorShopPick? Pick( string helper, AdvisorShopGroup group )
		{
			Calls.Add( $"{helper}:{group}" );
			return Picks.TryGetValue( (helper, group), out var pick ) ? pick : null;
		}

		public AdvisorShopPick? WorstValue( AdvisorShopGroup group ) => Pick( "WorstValue", group );
		public AdvisorShopPick? BestValue( AdvisorShopGroup group ) => Pick( "BestValue", group );
		public AdvisorShopPick? WorstSatisfaction( AdvisorShopGroup group ) => Pick( "WorstSatisfaction", group );
		public AdvisorShopPick? BestSatisfaction( AdvisorShopGroup group ) => Pick( "BestSatisfaction", group );
	}

	/// <summary>A park state that has the original's park statistics but not this batch's view.</summary>
	private sealed class ParkOnly : IAdvisorParkState
	{
		public bool ParkOpen => true;
		public int StaffCount( StaffType type ) => 0;
		public int DaysSinceObjectStamp => 0;
		public int DaysSinceHireStamp => 0;
		public int DaysSinceStart => 0;
		public int AvailableTypes( int kind, bool withTicketItems ) => 10;
		public int BuiltTypes( int kind, bool withTicketItems ) => 0;
		public int ObjectsOfKind( int kind, int minimumLevel ) => 0;
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

	private static AdvisorScanContext Context( IAdvisorParkState? park, int months = 12 ) => new( Balance(), new FakeMoney { Months = months }, park );

	private static int Score( int message, FakeState? park, int months = 12, int gameType = 0 ) =>
		new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented ).ScoreOf( AdvisorDescriptorTable.Rows[message], Context( park, months ), gameType );

	/// <summary>A park in which the researcher lines have something to say: a researcher is hired, five of ten ride types are built.</summary>
	private static FakeState Researching()
	{
		// three ride types with an upgrade are above RidesMoreUpgrades' 2, so neither upgrade line speaks
		var park = new FakeState { Researchers = 1, UpgradedTypes = 3 };
		park.Available[0] = 10;
		park.BuiltTypeCount[0] = 10;
		park.Outstanding[0] = 2;
		return park;
	}

	// ------------------------------------------------------------------ the table

	[TestMethod]
	public void TheBatchPortsThirtyOneCallbacksOfTheScannedRows()
	{
		var rows = AdvisorDescriptorTable.Rows.Where( row => AdvisorQueuesCallbacks.Callbacks.ContainsKey( row.Callback ) ).ToArray();
		CollectionAssert.AreEqual( Messages, rows.Select( row => row.MessageId ).ToArray(), "one descriptor row per ported callback, the 31 messages of the queues_prices and research batches" );
		Assert.AreEqual( 31, AdvisorQueuesCallbacks.Callbacks.Count );
		Assert.IsTrue( rows.All( row => row.Scan ), "the scan reaches each of them" );
		foreach ( var row in rows )
			Assert.IsTrue( AdvisorScanCallbacks.Implemented.ContainsKey( row.Callback ), $"message {row.MessageId} is in the scan's callback table" );
		Assert.IsTrue( Messages.All( message => AdvisorTables.ScanMessages.Any( definition => definition.MessageId == message ) ), "each has its descriptor in AdvisorTables" );
	}

	[TestMethod]
	public void EveryQueuesCallbackScoresZeroWithoutAParkItsViewOrItsThresholds()
	{
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );
		var bare = Balance( Sam.Split( "StaffHireResearchers1.Most" )[0] );
		foreach ( var message in Messages )
		{
			var row = AdvisorDescriptorTable.Rows[message];
			Assert.AreEqual( 0, scan.ScoreOf( row, Context( null ), 0 ), $"message {message} without a park" );
			Assert.AreEqual( 0, scan.ScoreOf( row, Context( new ParkOnly() ), 0 ), $"message {message} with a park state that has no queues view" );
			var ready = new FakeState { Researchers = 1, RingFull = true, RingMean = 0, Candidates = 1, Done = 1, Capacity = 100 };
			ready.Available[0] = ready.Available[1] = ready.Available[2] = ready.Available[3] = 10;
			ready.Outstanding[0] = ready.Outstanding[1] = ready.Outstanding[2] = ready.Outstanding[3] = 1;
			foreach ( var group in Enum.GetValues<AdvisorShopGroup>() )
			{
				ready.Picks[("WorstValue", group)] = new AdvisorShopPick( 1, 0 );
				ready.Picks[("WorstSatisfaction", group)] = new AdvisorShopPick( 1, 0 );
				ready.Picks[("BestValue", group)] = new AdvisorShopPick( 1, 100 );
				ready.Picks[("BestSatisfaction", group)] = new AdvisorShopPick( 1, 100 );
			}
			Assert.AreEqual( 0, scan.ScoreOf( row, new AdvisorScanContext( bare, new FakeMoney(), ready ), 0 ), $"message {message} with a settings file without its thresholds" );
		}
	}

	[TestMethod]
	public void TheKeysTheCallbacksReadAreParametersOfTheScan()
	{
		foreach ( var key in AdvisorQueuesCallbacks.IntegerKeys )
			CollectionAssert.Contains( AdvisorScan.IntegerParameterKeys.ToArray(), key );
		Assert.AreEqual( 6 + 2 + 40, AdvisorQueuesCallbacks.IntegerKeys.Count );
		var balance = Balance();
		Assert.AreEqual( -7, balance.ScanParameters["FoodPoorValue.ScorePerPoint"], "a signed word" );
		Assert.AreEqual( 40, balance.ScanParameters["StaffHireResearchers1.Most"] );
	}

	// ------------------------------------------------------------------ the researchers

	[TestMethod]
	public void FewRideTypesBuiltScoreTheExcessOverMostPerPoint()
	{
		var park = Researching();
		park.Researchers = 0;
		park.BuiltTypeCount[0] = 4;
		Assert.AreEqual( 0, Score( 15, park ), "4 x 100 / 10 = 40 is not above Most 40" );
		park.BuiltTypeCount[0] = 5;
		Assert.AreEqual( (50 - 40) * 3, Score( 15, park ), "50 percent: 10 points over, x PointsPerExtraPct" );
		park.BuiltTypeCount[0] = 9;
		Assert.AreEqual( (90 - 40) * 3, Score( 15, park ) );
		park.BuiltTypeCount[0] = 10;
		Assert.AreEqual( 0, Score( 15, park ), "every type built: the line leaves it to 16" );
		Assert.AreEqual( false, park.LastTicketFlag, "the available ride types are counted without the golden ticket ones (0x4CA450 of 0)" );
	}

	[TestMethod]
	public void TheResearcherLinesNeedAnOpenParkNoResearcherAndRideResearchLeft()
	{
		foreach ( var message in new[] { 15, 16 } )
		{
			var park = Researching();
			park.Researchers = 0;
			park.BuiltTypeCount[0] = message == 15 ? 7 : 10;
			Assert.AreNotEqual( 0, Score( message, park ), $"message {message}: no researcher is what it asks for" );
			park.Researchers = 1;
			Assert.AreEqual( 0, Score( message, park ), $"message {message}: a researcher is hired" );
			park.Researchers = 0;
			park.Open = false;
			Assert.AreEqual( 0, Score( message, park ), $"message {message}: the park is closed" );
			park.Open = true;
			park.Outstanding[0] = 0;
			Assert.AreEqual( 0, Score( message, park ), $"message {message}: no ride research outstanding" );
			park.Outstanding[0] = 1;
			park.Outstanding[1] = 0;
			Assert.AreNotEqual( 0, Score( message, park ), $"message {message}: only the rides' research counts" );
		}
	}

	[TestMethod]
	public void EveryRideTypeBuiltScoresTheFlatLineAndNothingAvailableCountsAsBuilt()
	{
		var park = Researching();
		park.Researchers = 0;
		Assert.AreEqual( 61, Score( 16, park ) );
		park.BuiltTypeCount[0] = 9;
		Assert.AreEqual( 0, Score( 16, park ), "a ride type is still to build" );
		// 0x59CF50 has no test for 0 available types: 0 built of 0 available is equal
		park.Available[0] = 0;
		park.BuiltTypeCount[0] = 0;
		Assert.AreEqual( 61, Score( 16, park ) );
		Assert.AreEqual( 0, Score( 15, park ), "0x59CEA0 does stop at 0 available types" );
	}

	[TestMethod]
	public void SlowResearchNeedsMonthsAResearcherAPooledCandidateAndTheStaffingBelowSlowerThan()
	{
		var park = new FakeState { Researchers = 1, Candidates = 1, Done = 32, Capacity = 100 };
		Assert.AreEqual( 47, Score( 17, park ), "32 x 100 / 100 = 32 is below SlowerThan 33" );
		park.Done = 33;
		Assert.AreEqual( 0, Score( 17, park ), "33 is not below 33" );
		park.Done = 0;
		Assert.AreEqual( 47, Score( 17, park ), "no research done at all is below it" );
		Assert.AreEqual( 47, Score( 17, park, months: 6 ), "MonthsIntoGameForConsideration is inclusive" );
		Assert.AreEqual( 0, Score( 17, park, months: 5 ) );
		park.Candidates = 0;
		Assert.AreEqual( 0, Score( 17, park ), "nobody waits in the hiring pool" );
		park.Candidates = 1;
		park.Researchers = 0;
		Assert.AreEqual( 0, Score( 17, park ), "a researcher must be hired" );
		park.Researchers = 1;
		park.Capacity = 0;
		Assert.AreEqual( 0, Score( 17, park ), "no research total: no division" );
		park.Capacity = 100;
		park.Open = false;
		Assert.AreEqual( 0, Score( 17, park ), "the park is closed" );
		var wrap = new FakeState { Researchers = 1, Candidates = 1, Done = 50_000_000, Capacity = 7 };
		Assert.AreEqual( unchecked( 50_000_000u * 100u ) / 7 < 33 ? 47 : 0, Score( 17, wrap ), "x 100 is a 32-bit unsigned product, divided unsigned" );
	}

	[TestMethod]
	public void ResearchMoreLinesAskForAResearcherABuiltTypeOutstandingResearchAndNoRate()
	{
		// message, kind, research category: the rides, shops, sideshows and features
		foreach ( var (message, kind, category, score, rateTested) in new[] { (61, 0, 0, 71, true), (72, 1, 1, 72, true), (76, 2, 2, 73, true), (80, 3, 3, 74, false) } )
		{
			FakeState Park()
			{
				var park = new FakeState { Researchers = 1 };
				park.Available[kind] = park.BuiltTypeCount[kind] = 4;
				park.Outstanding[category] = 3;
				return park;
			}
			var park = Park();
			Assert.AreEqual( score, Score( message, park ), $"message {message}" );
			Assert.AreEqual( kind != 0, park.LastTicketFlag, $"message {message}: only the rides' available types skip the golden ticket ones" );
			park = Park();
			park.Open = false;
			Assert.AreEqual( score, Score( message, park ), $"message {message} tests no park-open state" );
			park = Park();
			park.Researchers = 0;
			Assert.AreEqual( 0, Score( message, park ), $"message {message}: no researcher" );
			park = Park();
			park.BuiltTypeCount[kind] = 0;
			park.Available[kind] = 0;
			Assert.AreEqual( 0, Score( message, park ), $"message {message}: no type of the kind built" );
			park = Park();
			park.Outstanding[category] = 0;
			Assert.AreEqual( 0, Score( message, park ), $"message {message}: nothing left to research" );
			park = Park();
			park.BuiltTypeCount[kind] = 3;
			Assert.AreEqual( 0, Score( message, park ), $"message {message}: a type is still to build" );
			park = Park();
			park.Rates[category] = 1;
			Assert.AreEqual( rateTested ? 0 : score, Score( message, park ), $"message {message}: the research rate" );
			park = Park();
			park.Outstanding[(category + 1) % 4] = 5;
			park.Outstanding[category] = 0;
			Assert.AreEqual( 0, Score( message, park ), $"message {message}: another category's research does not count" );
		}
	}

	[TestMethod]
	public void ResearchMoreMessagesAreForTheFullSimulationOnly()
	{
		var park = new FakeState { Researchers = 1 };
		park.Available[1] = park.BuiltTypeCount[1] = 2;
		park.Outstanding[1] = 1;
		Assert.AreEqual( 72, Score( 72, park, gameType: 0 ) );
		Assert.AreEqual( -1, Score( 72, park, gameType: 2 ), "the descriptor's game-mode selector 0 excludes Instant Action" );
		foreach ( var message in new[] { 61, 63, 64, 76, 80 } )
			Assert.AreEqual( 0, AdvisorDescriptorTable.Rows[message].GameMode, $"message {message}" );
		Assert.AreEqual( 2, AdvisorDescriptorTable.Rows[15].GameMode );
		Assert.AreEqual( 2, AdvisorDescriptorTable.Rows[17].GameMode );
	}

	[TestMethod]
	public void UpgradeLinesCompareTheUpgradedRideTypesAndAskForNoUpgradeResearch()
	{
		var park = new FakeState { Researchers = 1 };
		Assert.AreEqual( 65, Score( 63, park ), "no ride type has an upgrade" );
		Assert.AreEqual( 0, Score( 64, park ), "and nothing is less available than 2" );
		Assert.AreEqual( 0, Score( 63, park, months: 3 ), "before RidesNoUpgrades' months" );
		Assert.AreEqual( 65, Score( 63, park, months: 4 ) );
		park.UpgradedTypes = 1;
		Assert.AreEqual( 0, Score( 63, park ) );
		Assert.AreEqual( 66, Score( 64, park ) );
		park.UpgradedTypes = 2;
		Assert.AreEqual( 66, Score( 64, park ), "at LessAvailableThan" );
		park.UpgradedTypes = 3;
		Assert.AreEqual( 0, Score( 64, park ), "above it" );
		park.UpgradedTypes = 1;
		park.Rates[4] = 1;
		Assert.AreEqual( 0, Score( 64, park ), "upgrade research is under way" );
		park.UpgradedTypes = 0;
		Assert.AreEqual( 0, Score( 63, park ) );
		park.Rates[4] = 0;
		park.Researchers = 0;
		Assert.AreEqual( 0, Score( 63, park ), "no researcher" );
		Assert.AreEqual( 0, Score( 64, new FakeState { UpgradedTypes = 1 } ), "no researcher" );
		park.Researchers = 1;
		park.Rates[0] = 9;
		Assert.AreEqual( 65, Score( 63, park ), "only the upgrade category's rate counts" );
	}

	// ------------------------------------------------------------------ the ticket price

	[TestMethod]
	public void TicketLinesReadTheMeanOfTheFullTicketRingAgainstTheirThreshold()
	{
		var park = new FakeState { RingFull = true, RingMean = 34 };
		Assert.AreEqual( 111, Score( 91, park ), "34 is below TicketSatisfactionUnder 35" );
		Assert.AreEqual( 0, Score( 92, park ) );
		Assert.AreEqual( 0, park.LastRingCategory, "the entrance ticket's ring is category 0" );
		park.RingMean = 35;
		Assert.AreEqual( 0, Score( 91, park ), "35 is not below 35" );
		park.RingMean = 85;
		Assert.AreEqual( 0, Score( 92, park ), "85 is not above 85" );
		park.RingMean = 86;
		Assert.AreEqual( 112, Score( 92, park ) );
		park.RingFull = false;
		Assert.AreEqual( 0, Score( 92, park ), "the ring must hold its 50 values" );
		park.RingMean = 0;
		Assert.AreEqual( 0, Score( 91, park ) );
		park.RingFull = true;
		park.RingMean = 256 + 34;
		Assert.AreEqual( 111, Score( 91, park ), "the mean is the byte al, compared as a word" );
		Assert.AreEqual( 0, Score( 92, park ) );
	}

	[TestMethod]
	public void TheEconomyStateReadsTheTicketRingLikeTheOriginalsMeanAndFullTests()
	{
		var park = EconomyTestData.Park();
		var state = new EconomyParkState( park );
		Assert.IsFalse( state.PriceRingHoldsFifty( 0 ) );
		Assert.AreEqual( 0, state.PriceRingMean( 0 ), "an empty ring gives a clear byte" );
		for ( var index = 0; index < 49; index++ )
			park.PriceStatistics.Record( 0, 20 );
		Assert.IsFalse( state.PriceRingHoldsFifty( 0 ), "49 values" );
		Assert.AreEqual( 20, state.PriceRingMean( 0 ) );
		park.PriceStatistics.Record( 0, 70 );
		Assert.IsTrue( state.PriceRingHoldsFifty( 0 ) );
		Assert.AreEqual( (49 * 20 + 70) / 50, state.PriceRingMean( 0 ), "the sum over the 50 values divided and truncated: 21.0 is 21" );
		park.PriceStatistics.Record( 0, 99 );
		Assert.AreEqual( (48 * 20 + 70 + 99) / 50, state.PriceRingMean( 0 ), "the oldest value left the ring" );
		Assert.IsFalse( state.PriceRingHoldsFifty( 1 ), "another category's ring" );
		park.PriceStatistics.Record( 1, 99 );
		Assert.AreEqual( 99, state.PriceRingMean( 1 ) );
		Assert.AreEqual( (48 * 20 + 70 + 99) / 50, state.PriceRingMean( 0 ), "the rings are independent" );
	}

	[TestMethod]
	public void TheBoothRecordsItsOpinionOfTheFeeInRingZeroThroughTheOriginalsBands()
	{
		var settings = new GuestSettings { MinimumEntryFee = 20, OpinionForMuchTooHigh = 1, OpinionForTooHigh = 2, OpinionForTooLow = 3, OpinionForAboutRight = 4 };
		Assert.AreEqual( 1, BoothVerdict.Opinion( BoothVerdictKind.FarTooExpensive, settings ) );
		Assert.AreEqual( 2, BoothVerdict.Opinion( BoothVerdictKind.Expensive, settings ) );
		Assert.AreEqual( 3, BoothVerdict.Opinion( BoothVerdictKind.Cheap, settings ) );
		Assert.AreEqual( 4, BoothVerdict.Opinion( BoothVerdictKind.Fair, settings ) );
		Assert.IsNull( BoothVerdict.Opinion( BoothVerdictKind.Fair, new GuestSettings() ), "a settings file without the keys records nothing" );
		Assert.AreEqual( AdvisorQueuesCallbacks.TicketCategory, BoothVerdict.OpinionCategory, "the booth writes the ring the advisor reads" );
		// a fee and a park value both at or below the minimum entry fee return the fair verdict before the bands, without a record
		Assert.IsFalse( BoothVerdict.RecordsOpinion( 20, 20, settings ) );
		Assert.IsFalse( BoothVerdict.RecordsOpinion( 5, 0, settings ) );
		Assert.IsTrue( BoothVerdict.RecordsOpinion( 21, 20, settings ), "the park value is above the minimum" );
		Assert.IsTrue( BoothVerdict.RecordsOpinion( 20, 21, settings ), "the fee is above the minimum" );
	}

	private sealed class RecordingGate : IGuestPayments
	{
		public int Fee;
		public readonly List<(int Category, int Value)> Recorded = new();
		public int AdmissionFee => Fee;
		public bool ParkClosed => false;
		public bool TryPayAdmission( out int feePaid ) { feePaid = Fee; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
		public void PriceTested( int category, int value ) => Recorded.Add( (category, value) );
	}

	/// <summary>Runs a simulation until a guest has been weighed at the booth, and returns what the booth recorded.</summary>
	private static IReadOnlyList<(int Category, int Value)> BoothOpinions( int fee, int points, bool opinions = true )
	{
		var gate = new RecordingGate { Fee = fee };
		var settings = GuestTests.CreateSettings();
		if ( opinions )
			(settings.OpinionForMuchTooHigh, settings.OpinionForTooHigh, settings.OpinionForTooLow, settings.OpinionForAboutRight) = (11, 22, 33, 44);
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 3 ) { Payments = gate, ParkPoints = () => points };
		for ( var tick = 0; tick < 120 * 60 && gate.Recorded.Count == 0 && simulation.Admissions == 0; tick++ )
			simulation.Tick( GuestSnapshotTests.Tick );
		return gate.Recorded.Distinct().ToArray();
	}

	[TestMethod]
	public void TheBoothVerdictsRecordTheirBandsOpinionInRingZero()
	{
		var minimum = new GuestSettings().MinimumEntryFee;
		// no park points: the park value is the minimum entry fee, so the fee decides the band (the defaults' multipliers are 0.75, 1.25 and 2)
		CollectionAssert.AreEqual( new[] { (0, 11) }, BoothOpinions( fee: minimum * 3, points: 0 ).ToArray(), "far too expensive" );
		CollectionAssert.AreEqual( new[] { (0, 22) }, BoothOpinions( fee: minimum * 3 / 2, points: 0 ).ToArray(), "expensive: the guest asks again" );
		CollectionAssert.AreEqual( new[] { (0, 33) }, BoothOpinions( fee: 1, points: 100_000 ).ToArray(), "cheap for a valuable park" );
		CollectionAssert.AreEqual( new[] { (0, 44) }, BoothOpinions( fee: minimum + 1, points: 0 ).ToArray(), "about right: above the minimum entry fee, below 1.25 times the park value" );
	}

	[TestMethod]
	public void TheBoothRecordsNothingForTheEarlyFairVerdictOrWithoutTheOpinionKeys()
	{
		var minimum = new GuestSettings().MinimumEntryFee;
		Assert.AreEqual( 0, BoothOpinions( fee: minimum, points: 0 ).Count, "a park value and a fee both at or below the minimum entry fee: fair without a record (0x4FF71A)" );
		Assert.AreEqual( 0, BoothOpinions( fee: minimum * 3, points: 0, opinions: false ).Count, "no PeepInfo.OpinionFor* keys" );
	}

	[TestMethod]
	public void TheGuestSettingsReadTheOpinionKeysAndLeaveThemOutWhenTheFileHasNone()
	{
		var values = new Dictionary<string, string[]>
		{
			["PeepInfo.OpinionForMuchTooHigh"] = new[] { "3" }, ["PeepInfo.OpinionForTooHigh"] = new[] { "30" },
			["PeepInfo.OpinionForTooLow"] = new[] { "90" }, ["PeepInfo.OpinionForAboutRight"] = new[] { "60" },
		};
		var settings = GuestSettings.FromValues( values );
		Assert.AreEqual( (3, 30, 90, 60), (settings.OpinionForMuchTooHigh, settings.OpinionForTooHigh, settings.OpinionForTooLow, settings.OpinionForAboutRight) );
		var bare = GuestSettings.FromValues( new Dictionary<string, string[]>() );
		Assert.IsNull( bare.OpinionForTooHigh );
	}

	// ------------------------------------------------------------------ the shops' figures

	private static readonly string[] Prefixes = { "Food", "Shops", "Restaurant", "Drinks", "Sideshows" };

	[TestMethod]
	public void PoorLinesScoreTheFigureLessTheThresholdTimesThePointsSoAPositiveMultiplierNeverPasses()
	{
		// the callbacks compute (figure - threshold) x points with the figure first: below the threshold that is negative for a positive multiplier
		for ( var index = 0; index < 5; index++ )
		{
			var group = (AdvisorShopGroup)index;
			foreach ( var (first, helper, thresholdFirst) in new[] { (238, "WorstValue", 40), (243, "WorstSatisfaction", 30) } )
			{
				var message = first + index;
				var threshold = thresholdFirst + index;
				var points = (helper == "WorstValue" ? -7 : -9) + index;
				var park = new FakeState();
				park.Picks[(group == AdvisorShopGroup.Sideshows ? "WorstValue" : helper, group)] = new AdvisorShopPick( 12, threshold - 1 );
				Assert.AreEqual( -1 * points, Score( message, park ), $"message {message}: (threshold - 1 - threshold) x {points}" );
				park.Picks[(group == AdvisorShopGroup.Sideshows ? "WorstValue" : helper, group)] = new AdvisorShopPick( 12, threshold - 6 );
				Assert.AreEqual( -6 * points, Score( message, park ), $"message {message}" );
				park.Picks[(group == AdvisorShopGroup.Sideshows ? "WorstValue" : helper, group)] = new AdvisorShopPick( 12, threshold );
				Assert.AreEqual( 0, Score( message, park ), $"message {message} at the threshold" );
				park.Picks[(group == AdvisorShopGroup.Sideshows ? "WorstValue" : helper, group)] = new AdvisorShopPick( 12, threshold + 1 );
				Assert.AreEqual( 0, Score( message, park ), $"message {message} above the threshold" );
			}
		}
		// with the shipped sign of the multiplier the line is silent: the score of a figure below the threshold is not above 0
		var positive = Balance( System.Text.RegularExpressions.Regex.Replace( Sam, @"FoodPoorValue\.ScorePerPoint\s+-7", "FoodPoorValue.ScorePerPoint 7" ) );
		var state = new FakeState();
		state.Picks[("WorstValue", AdvisorShopGroup.Food)] = new AdvisorShopPick( 3, 10 );
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );
		var score = scan.ScoreOf( AdvisorDescriptorTable.Rows[238], new AdvisorScanContext( positive, new FakeMoney(), state ), 0 );
		Assert.AreEqual( (10 - 40) * 7, score );
		Assert.IsTrue( score <= 0, "never above the scan's best of 0" );
	}

	[TestMethod]
	public void GreatLinesScoreTheFigureOverTheThresholdTimesThePoints()
	{
		for ( var index = 0; index < 5; index++ )
		{
			var group = (AdvisorShopGroup)index;
			foreach ( var (first, helper, thresholdFirst, pointsFirst) in new[] { (248, "BestValue", 70, 2), (253, "BestSatisfaction", 60, 2) } )
			{
				var message = first + index;
				var threshold = thresholdFirst + index;
				var points = pointsFirst + index;
				var park = new FakeState();
				park.Picks[(helper, group)] = new AdvisorShopPick( 5, threshold + 1 );
				Assert.AreEqual( points, Score( message, park ), $"message {message}: one over" );
				park.Picks[(helper, group)] = new AdvisorShopPick( 5, threshold + 10 );
				Assert.AreEqual( 10 * points, Score( message, park ) );
				park.Picks[(helper, group)] = new AdvisorShopPick( 5, threshold );
				Assert.AreEqual( 0, Score( message, park ), $"message {message} at the threshold" );
				park.Picks[(helper, group)] = new AdvisorShopPick( 5, threshold - 1 );
				Assert.AreEqual( 0, Score( message, park ) );
				park.Picks[(helper, group)] = new AdvisorShopPick( 0, threshold + 10 );
				Assert.AreEqual( 0, Score( message, park ), $"message {message}: a handle of 0 is no shop" );
				park.Picks[(helper, group)] = null;
				Assert.AreEqual( 0, Score( message, park ), $"message {message}: no shop picked" );
			}
		}
	}

	[TestMethod]
	public void TheShopLinesAskTheirOwnGroupsHelperAndTheSidewaysPoorSatisfactionAsksForTheValue()
	{
		var park = new FakeState();
		var expectations = new List<(int Message, string Call)>();
		for ( var index = 0; index < 5; index++ )
		{
			var group = (AdvisorShopGroup)index;
			expectations.Add( (238 + index, $"WorstValue:{group}") );
			expectations.Add( (243 + index, group == AdvisorShopGroup.Sideshows ? $"WorstValue:{group}" : $"WorstSatisfaction:{group}") );
			expectations.Add( (248 + index, $"BestValue:{group}") );
			expectations.Add( (253 + index, $"BestSatisfaction:{group}") );
		}
		foreach ( var (message, call) in expectations )
		{
			park.Calls.Clear();
			Score( message, park );
			CollectionAssert.AreEqual( new[] { call }, park.Calls, $"message {message}" );
		}
		// 0x59FF50 calls 0x4CC090, the helper of 0x59FD20
		Assert.AreEqual( AdvisorDescriptorTable.Rows[247].Callback, AdvisorQueuesCallbacks.PoorSatisfaction[4] );
		Assert.AreEqual( 0x59FF50u, AdvisorQueuesCallbacks.PoorSatisfaction[4] );
	}

	[TestMethod]
	public void TheShopLinesTestNeitherTheParkOpenStateNorAnyStaff()
	{
		var park = new FakeState { Open = false };
		park.Picks[("BestValue", AdvisorShopGroup.Food)] = new AdvisorShopPick( 9, 80 );
		Assert.AreEqual( 10 * 2, Score( 248, park ) );
		Assert.AreEqual( 0, Score( 248, null ) );
	}

	// ------------------------------------------------------------------ through the real queue

	private static IReadOnlyList<(int Message, int Score)> Pass( FakeState park, int months = 12, int gameType = 0, string? sam = null )
	{
		var controller = new AdvisorController( Balance( sam ) );
		var money = new FakeMoney { Months = months };
		var posted = new List<(int, int)>();
		for ( var step = 0; step < 155; step++ )
			if ( controller.ScanOnce( money, 100, gameType, true, park ).Posted is { } advice )
				posted.Add( (advice.MessageId, advice.Score) );
		return posted;
	}

	[TestMethod]
	public void AWholeScanPassPostsAResearchLineExactlyWhenTheOriginalDoes()
	{
		var park = Researching();
		park.Researchers = 0;
		park.BuiltTypeCount[0] = 8;
		CollectionAssert.AreEqual( new[] { (15, (80 - 40) * 3) }, Pass( park ).ToArray(), "no researcher, 80 percent of the ride types built" );
		park.BuiltTypeCount[0] = 10;
		CollectionAssert.AreEqual( new[] { (16, 61) }, Pass( park ).ToArray(), "every type built" );
		park.Researchers = 1;
		CollectionAssert.AreEqual( new[] { (61, 71) }, Pass( park ).ToArray(), "with a researcher hired nothing asks for one; every ride type is built and no ride research runs" );
		park.BuiltTypeCount[1] = park.Available[1] = 2;
		park.Outstanding[1] = 1;
		CollectionAssert.AreEquivalent( new[] { (61, 71), (72, 72) }, Pass( park ).ToArray(), "a shop built, shop research outstanding, no rate, every shop type built" );
		Assert.AreEqual( 0, Pass( park, gameType: 2 ).Count, "not in Instant Action" );
		park.Rates[1] = 1;
		CollectionAssert.AreEqual( new[] { (61, 71) }, Pass( park ).ToArray(), "the shop research is under way" );
		park.Rates[0] = 1;
		Assert.AreEqual( 0, Pass( park ).Count );
	}

	[TestMethod]
	public void AWholeScanPassPostsTheTicketAndTheGreatValueLines()
	{
		var park = new FakeState { RingFull = true, RingMean = 20 };
		CollectionAssert.AreEqual( new[] { (91, 111) }, Pass( park ).ToArray() );
		park.RingMean = 90;
		CollectionAssert.AreEqual( new[] { (92, 112) }, Pass( park ).ToArray() );
		park.RingMean = 50;
		park.Picks[("BestValue", AdvisorShopGroup.Drinks)] = new AdvisorShopPick( 4, 83 );
		CollectionAssert.AreEqual( new[] { (251, (83 - 73) * 5) }, Pass( park ).ToArray(), "a great value drinks shop is praised" );
		// the shipped sign of the poor lines' multipliers is positive: a poor value shop then scores below 0 and is never said
		var positive = System.Text.RegularExpressions.Regex.Replace( Sam, @"(Poor(?:Value|Satisfaction)\.ScorePerPoint\s+)-", "$1" );
		park.Picks[("WorstValue", AdvisorShopGroup.Drinks)] = new AdvisorShopPick( 4, 3 );
		CollectionAssert.AreEqual( new[] { (251, 50) }, Pass( park, sam: positive ).ToArray() );
		CollectionAssert.AreEquivalent( new[] { (251, 50), (241, (3 - 43) * -4) }, Pass( park ).ToArray(), "with a negative multiplier the same shop is spoken of" );
	}

	[TestMethod]
	public void ANotWiredMessageNeverProducesGenericSpeech()
	{
		// the batch's rows score only through the callbacks above: a park without the queues view says none of them and a row of another batch is unaffected
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );
		var context = Context( new ParkOnly() );
		foreach ( var message in Messages )
			Assert.AreEqual( 0, scan.ScoreOf( AdvisorDescriptorTable.Rows[message], context, 0 ), $"message {message}" );
		var controller = new AdvisorController( Balance() );
		var posted = new List<int>();
		for ( var step = 0; step < 155; step++ )
			if ( controller.ScanOnce( new FakeMoney(), 100, 0, true, new ParkOnly() ).Posted is { } advice )
				posted.Add( advice.MessageId );
		CollectionAssert.AreEqual( Array.Empty<int>(), posted.Intersect( Messages ).ToArray() );
	}

	[TestMethod]
	public void TheScansRowOrderAndPacingAreTheOriginals()
	{
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );
		var context = Context( Researching() );
		var visited = new List<int>();
		for ( var step = 0; step < 155; step++ )
			visited.Add( scan.Step( context, 0, ( _, _ ) => true ).ScoredRow );
		CollectionAssert.AreEqual( AdvisorDescriptorTable.Rows.Where( row => row.Scan ).Select( row => row.MessageId ).ToArray(), visited.ToArray(), "one scanned descriptor per call, in table order" );
		Assert.AreEqual( visited[0], scan.Step( context, 0, ( _, _ ) => true ).ScoredRow, "the 156th call wraps to the first scanned row" );
	}

	// ------------------------------------------------------------------ the statistics over a real park

	private static ParkEconomy Park() => EconomyTestData.Park( initialCash: 500000 );

	private static void Age( ParkEconomy park, int days ) => EconomyTestData.AdvanceToTurn( park, (long)days * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn );

	private static DailyRing Ring( params int[] entriesOldestFirst ) => DailyRing.FromState( entriesOldestFirst.Length - 1, false, 0, entriesOldestFirst );

	[TestMethod]
	public void TheEconomyValueIsTheWalkAwayShareOfTheCustomersOfShopsOlderThanThirtyDays()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		var shop = park.RegisterExisting( 1203 );
		// a day's end moves each ring on, so the rings are set after the days have passed; a zero day added to a sum changes nothing
		shop.CustomersRing = Ring( 10, 20, 30 );
		shop.WalkAwayDays.AddRange( new long[] { 6, 0, 3 } );
		Assert.IsNull( state.WorstValue( AdvisorShopGroup.Drinks ), "a new shop is not counted" );
		Age( park, 30 );
		Assert.IsTrue( park.ObjectAgeDays( shop ) <= 30 );
		shop.CustomersRing = Ring( 10, 20, 30 );
		shop.WalkAwayDays.Clear();
		shop.WalkAwayDays.AddRange( new long[] { 6, 0, 3 } );
		Assert.IsNull( state.WorstValue( AdvisorShopGroup.Drinks ), "30 days is not above 30" );
		Age( park, 32 );
		Assert.IsTrue( park.ObjectAgeDays( shop ) > 30 );
		Assert.AreEqual( new AdvisorShopPick( shop.Id, 9 * 100 / 60 ), state.WorstValue( AdvisorShopGroup.Drinks ), "9 walk-aways x 100 / 60 customers, truncated: 15" );
		Assert.AreEqual( new AdvisorShopPick( shop.Id, 15 ), state.BestValue( AdvisorShopGroup.Drinks ) );
		Assert.IsNull( state.WorstValue( AdvisorShopGroup.Food ), "the drinks shop has ShopType 4" );
		Assert.IsNull( state.WorstValue( AdvisorShopGroup.Restaurant ) );
		Assert.IsNull( state.WorstValue( AdvisorShopGroup.Shops ) );
		Assert.IsNull( state.WorstValue( AdvisorShopGroup.Sideshows ) );
		shop.CustomersRing = Ring();
		Assert.IsNull( state.WorstValue( AdvisorShopGroup.Drinks ), "a shop without customers is skipped" );
		shop.CustomersRing = Ring( 0, 0 );
		Assert.IsNull( state.BestValue( AdvisorShopGroup.Drinks ), "customers that sum to 0 as well" );
	}

	[TestMethod]
	public void TheEconomyPicksTheFirstOfTheStrictlyLowestAndHighestFigures()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		var shops = Enumerable.Range( 0, 3 ).Select( _ => park.RegisterExisting( 1203 ) ).ToArray();
		var walkAways = new long[] { 40, 10, 10 };
		Age( park, 40 );
		for ( var index = 0; index < 3; index++ )
		{
			shops[index].CustomersRing = Ring( 100 );
			shops[index].WalkAwayDays.Clear();
			shops[index].WalkAwayDays.Add( walkAways[index] );
		}
		Assert.AreEqual( new AdvisorShopPick( shops[1].Id, 10 ), state.WorstValue( AdvisorShopGroup.Drinks ), "the first of two equal lowest figures" );
		Assert.AreEqual( new AdvisorShopPick( shops[0].Id, 40 ), state.BestValue( AdvisorShopGroup.Drinks ) );
		// the walk-aways of a value are summed as unsigned words
		shops[0].WalkAwayDays.Clear();
		shops[0].WalkAwayDays.Add( 50 );
		shops[0].WalkAwayDays.Add( 100 );
		shops[0].CustomersRing = Ring( 60, 40 );
		Assert.AreEqual( new AdvisorShopPick( shops[0].Id, 150 ), state.BestValue( AdvisorShopGroup.Drinks ), "more walk-aways than customers is above 100" );
	}

	[TestMethod]
	public void TheEconomySatisfactionIsFiftyPlusTheMeanOfTheSatisfactionRing()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		var shop = park.RegisterExisting( 1203 );
		var other = park.RegisterExisting( 1203 );
		Age( park, 40 );
		shop.SatisfactionRing = Ring( 10, -4, 30 );
		other.SatisfactionRing = Ring( -20 );
		Assert.AreEqual( new AdvisorShopPick( shop.Id, 50 + 36 / 3 ), state.BestSatisfaction( AdvisorShopGroup.Drinks ) );
		Assert.AreEqual( new AdvisorShopPick( other.Id, 50 - 20 ), state.WorstSatisfaction( AdvisorShopGroup.Drinks ) );
		Assert.AreEqual( 62, state.BestSatisfaction( AdvisorShopGroup.Drinks )!.Value.Value );
		// the shops are counted whether or not they have customers, except by the sideshows' best
		Assert.IsNotNull( state.WorstSatisfaction( AdvisorShopGroup.Drinks ) );
		Assert.IsNull( state.BestSatisfaction( AdvisorShopGroup.Sideshows ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => state.WorstSatisfaction( AdvisorShopGroup.Sideshows ), "0x4CC090 is the sideshows' value helper, so there is none for the satisfaction" );
	}

	[TestMethod]
	public void TheEconomySideshowsAreTheAttractionsOfKindTwoAndTheirBestSatisfactionNeedsCustomers()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		var sideshow = park.RegisterExisting( 1303 );
		var shop = park.RegisterExisting( 1203 );
		Age( park, 40 );
		sideshow.SatisfactionRing = Ring( 40 );
		sideshow.CustomersRing = Ring();
		shop.SatisfactionRing = Ring( 40 );
		Assert.IsNull( state.BestSatisfaction( AdvisorShopGroup.Sideshows ), "no customers" );
		Assert.AreEqual( new AdvisorShopPick( shop.Id, 90 ), state.BestSatisfaction( AdvisorShopGroup.Drinks ), "a shop without customers still counts" );
		sideshow.CustomersRing = Ring( 5 );
		Assert.AreEqual( new AdvisorShopPick( sideshow.Id, 90 ), state.BestSatisfaction( AdvisorShopGroup.Sideshows ) );
		sideshow.WalkAwayDays.Add( 2 );
		Assert.AreEqual( new AdvisorShopPick( sideshow.Id, 40 ), state.WorstValue( AdvisorShopGroup.Sideshows ) );
	}

	[TestMethod]
	public void TheEconomyKeepsTheStockTypesAndTheRetailShopsApart()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		var catalog = park.Catalog;
		Assert.IsTrue( catalog.TryGet( 1203, out var drinks ) );
		Assert.AreEqual( 4, drinks!.ShopType );
		var shop = park.RegisterExisting( 1203 );
		Age( park, 40 );
		shop.SatisfactionRing = Ring( 50 );
		Assert.IsNotNull( state.BestSatisfaction( AdvisorShopGroup.Drinks ) );
		foreach ( var group in new[] { AdvisorShopGroup.Food, AdvisorShopGroup.Restaurant, AdvisorShopGroup.Shops, AdvisorShopGroup.Sideshows } )
			Assert.IsNull( state.BestSatisfaction( group ), $"{group}" );
	}

	[TestMethod]
	public void TheEconomyResearchStateCountsOutstandingProjectsTheRateAndTheStaffing()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		var ridesLeft = park.Research.Items.Count( item => item.Category == ResearchCategory.Ride && !park.Research.Completed.Contains( (item.InfoId, item.Level) ) );
		Assert.IsTrue( ridesLeft > 0, "the test catalog has ride projects" );
		Assert.AreEqual( ridesLeft, state.ResearchOutstanding( ResearchCategory.Ride ) );
		Assert.AreEqual( 0, state.ResearchDone );
		Assert.AreEqual( 0, state.ResearchRate( ResearchCategory.Ride ), "no researcher: no staffing" );
		var researcher = EconomyTestData.HireBest( park, StaffType.Researcher );
		var ability = park.Settings.ResearchAbility[researcher.Grade];
		Assert.AreEqual( ability, state.ResearchDone );
		var capacity = park.Settings[StaffType.Researcher].MaximumInPark * park.Settings.ResearchAbility[BalanceSettings.GradeCount - 1];
		Assert.AreEqual( capacity, state.ResearchCapacity );
		foreach ( var category in Enum.GetValues<ResearchCategory>() )
			park.Research.SetEffort( category, 0 );
		Assert.AreEqual( 0, state.ResearchRate( ResearchCategory.Ride ), "no effort at all: no share" );
		park.Research.SetEffort( ResearchCategory.Ride, 100 );
		park.Research.SetWorkload( 100 );
		// the share is 100 x 100 / 100 x workload / 100 = 100; the staffing percentage is the researchers' ability x 100 / the capacity (unsigned); the rate is their product / 100
		var staffing = (uint)ability * 100u / (uint)capacity;
		Assert.AreEqual( (int)(100u * staffing / 100u), state.ResearchRate( ResearchCategory.Ride ) );
		Assert.AreEqual( 0, state.ResearchRate( ResearchCategory.Shop ), "a category without effort" );
		park.Research.SetWorkload( 50 );
		Assert.AreEqual( (int)(50u * staffing / 100u), state.ResearchRate( ResearchCategory.Ride ), "the workload scales the share" );
	}

	[TestMethod]
	public void TheEconomyCountsTheResearchersOfTheHiringPoolAndTheUpgradedRideTypes()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		var pooled = park.Staff.Candidates.Count( candidate => candidate.Type == StaffType.Researcher );
		Assert.AreEqual( pooled, state.ResearcherCandidates );
		// the ride type without upgrade rows (1112) is researched at the start, which writes its free levels 1 and 2 into the control (0x504580): its level is above 0
		Assert.AreEqual( 2, park.Research.ResearchedLevel( 1112 ) );
		Assert.AreEqual( 0, park.Research.ResearchedLevel( 1100 ), "1100's first upgrade needs research" );
		Assert.AreEqual( 1, state.ResearchedUpgradeTypes );
		park.Research.SetEffort( ResearchCategory.Upgrade, 100 );
		park.Research.SetWorkload( 100 );
		for ( var round = 0; round < 20 && state.ResearchedUpgradeTypes == 1; round++ )
			park.Research.AddResearcherPoints( 100_000 );
		Assert.IsTrue( park.Research.ResearchedLevel( 1100 ) is > 0, "the upgrade project finished" );
		Assert.IsTrue( state.ResearchedUpgradeTypes >= 2 );
		var upgraded = park.Catalog.Objects.Count( info => info.RatingType == 0 && park.Research.ResearchedLevel( info.InfoId ) is > 0 );
		Assert.AreEqual( upgraded, state.ResearchedUpgradeTypes );
	}

	[TestMethod]
	public void TheEconomyStateDoesNotChangeTheParkWhenItReadsTheQueuesView()
	{
		var park = Park();
		park.RegisterExisting( 1203 ).CustomersRing = Ring( 10 );
		park.RegisterExisting( 1303 );
		EconomyTestData.HireBest( park, StaffType.Researcher );
		Age( park, 40 );
		var before = ParkSaveFile.Serialize( park );
		var state = new EconomyParkState( park );
		foreach ( var group in Enum.GetValues<AdvisorShopGroup>() )
			_ = (state.WorstValue( group ), state.BestValue( group ), group == AdvisorShopGroup.Sideshows ? null : state.WorstSatisfaction( group ), state.BestSatisfaction( group ));
		foreach ( var category in Enum.GetValues<ResearchCategory>() )
			_ = (state.ResearchOutstanding( category ), state.ResearchRate( category ));
		_ = (state.PriceRingHoldsFifty( 0 ), state.PriceRingMean( 0 ), state.ResearcherCandidates, state.ResearchDone, state.ResearchCapacity, state.ResearchedUpgradeTypes);
		Assert.AreEqual( before, ParkSaveFile.Serialize( park ), "reading the statistics wrote nothing" );
	}

	// ------------------------------------------------------------------ the shipped settings

	[TestMethod]
	public void EveryQueuesThresholdExistsInTheShippedSettings()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !System.IO.Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Advisor.sam." );
		var file = System.IO.Directory.EnumerateFiles( gamePath!, "Advisor.sam", System.IO.SearchOption.AllDirectories ).FirstOrDefault();
		if ( file == null )
			Assert.Inconclusive( "The original Advisor/Advisor.sam is missing." );
		var balance = AdvisorBalance.Parse( System.IO.File.ReadAllBytes( file ), file );
		foreach ( var key in AdvisorQueuesCallbacks.IntegerKeys )
			Assert.IsTrue( balance.ScanParameters.ContainsKey( key ), $"{key} is read from Advisor.sam" );
		foreach ( var message in Messages )
		{
			var definition = AdvisorTables.ScanMessages.Single( item => item.MessageId == message );
			Assert.IsTrue( balance.Scores.ContainsKey( definition.ScoreKey ), $"message {message}: {definition.ScoreKey}" );
			Assert.IsTrue( balance.Groups.ContainsKey( definition.Group ), $"message {message}: group {definition.Group}" );
		}
		// the shipped multipliers of the poor lines are positive, so those lines never pass
		foreach ( var prefix in Prefixes )
		{
			Assert.IsTrue( balance.ScanParameters[$"{prefix}PoorValue.ScorePerPoint"] > 0, prefix );
			Assert.IsTrue( balance.ScanParameters[$"{prefix}PoorSatisfaction.ScorePerPoint"] > 0, prefix );
		}
	}
}
