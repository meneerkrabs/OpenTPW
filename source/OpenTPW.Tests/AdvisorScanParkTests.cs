using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the park callbacks of the advisor scan (rides and breakdowns, cleanliness and guest mood: messages 3-10, 60, 62, 71, 73-75, 77-79, 81, 82, 85-89, 145, 158, 159 and 259-262),
/// the ride events 66, 68, 69 and 258, and the statistics they read (<see cref="EconomyParkState"/>). GPU-free; the thresholds are the shipped values of <c>Advisor.sam</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorScanParkTests
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
		Welcome.Score 9000
		Bankrupted.Score	8000
		ParkNowOpen.Score	20
		ParkNowClosed.Score	20
		PrebuiltPark.Score	700
		HelpMessage.Score	1000
		HelpMessage.ContextHelpScore	1500
		VisitorsThirsty.ScorePerThirstyPerson	1
		VisitorsThirsty.ThirstierThan	99
		VisitorsHungry.ScorePerHungryPerson	1
		VisitorsHungry.HungrierThan	99
		StaffHireMechanics1.ScorePerWornRide	5
		StaffHireMechanics1.PoorerStateThan	25
		StaffHireMechanics2.ScorePerBrokenRide	10
		StaffHireMechanics3.IdealNumberOfRidesPerMechanic	4
		StaffHireMechanics3.ScorePerExtraRide	10
		StaffHireHandymen1.ScorePerLitterPoint	3
		StaffHireHandymen1.LitterPointCutoffLow	3
		StaffHireHandymen1.LitterPointCutoffHigh	25
		StaffHireHandymen2.ScorePerLitterPoint	3
		StaffHireHandymen2.LitterPointCutoffLow	26
		StaffHireHandymen3.IdealDevelopedCellsPerHandyman	400
		StaffHireHandymen3.ScorePerExtra10Cells	2
		StaffHireHandymen3.MinCellsForConsideration	300
		RidesPoorSelection.PoorerThan	30
		RidesPoorSelection.MoreRidesThan	3
		RidesPoorSelection.Score	30
		RidesBuildRides.Score	-50
		RidesBuildRides.AdditionalPerDay	3
		RidesBreakdownImminent.Score	100
		RidesBreakdownNoMechanics.Score	105
		RidesBreakdownBusyMechanics.Score	107
		RidesBreakdownMechanicOnIt.Score	110
		ShopsPoorSelection.PoorerThan	25
		ShopsPoorSelection.MoreShopsThan	2
		ShopsPoorSelection.Score	50
		ShopsBuildShops.Score	50
		ShopsBuildShops.AdditionalPerDay	2
		ShopsBuildMoreShops.ShopsPerRideRatio	0.15
		ShopsBuildMoreShops.WealthMoreThan	20000
		ShopsBuildMoreShops.Score	70
		SideshowsPoorSelection.PoorerThan	15
		SideshowsPoorSelection.MoreSideshowsThan	2
		SideshowsPoorSelection.Score	45
		SideshowsBuildSideshows.Score	-70
		SideshowsBuildSideshows.AdditionalPerDay	2
		SideshowsBuildMoreSideshows.SideshowsPerRideRatio	0.2
		SideshowsBuildMoreSideshows.WealthMoreThan	20000
		SideshowsBuildMoreSideshows.Score	80
		FeaturesPoorSelection.PoorerThan	20
		FeaturesPoorSelection.MoreFeaturesThan	5
		FeaturesPoorSelection.Score	20
		FeaturesBuildFeatures.Score	-40
		FeaturesBuildFeatures.AdditionalPerDay	1
		FeaturesNoToilets.Score	90
		FeaturesNoToilets.NeedingMoreThan	60
		FeaturesNoToilets.MorePeopleThanThisNeeding	25
		FeaturesDirtyToilets1.FilthierThan	60
		FeaturesDirtyToilets1.PointsPerFilthPct	1
		FeaturesDirtyToilets2.FilthierThan	60
		FeaturesDirtyToilets2.PointsPerFilthPct	1
		FeaturesDirtyToilets3.FilthierThan	90
		FeaturesDirtyToilets3.PointsPerFilthPct	2
		FeaturesDirtyToilets4.FilthierThan	90
		FeaturesDirtyToilets4.PointsPerFilthPct	2
		CongratVisitorsHappy.MinimumVisitors	40
		CongratVisitorsHappy.HappierThan	80
		CongratVisitorsHappy.Score	90
		CongratBalloons.MoreThan	20
		CongratBalloons.Score	48
		CongratCostumes.MoreThan	30
		CongratCostumes.Score	48
		RideCondemned.Score	100
		RidesClosed.AtLeastThisManyRides	3
		RidesClosed.MoreThanThisPercentClosed	50
		RidesClosed.Score	80
		ShopsClosed.MoreThanThisPercentClosed	25
		ShopsClosed.Score	75
		SideshowsClosed.MoreThanThisPercentClosed	25
		SideshowsClosed.Score	70
		ToiletsClosed.MoreThanThisPercentClosed	20
		ToiletsClosed.Score	75
		WaitingTimes.DaysForStaff	30
		WaitingTimes.DaysForObjects	30
		""";

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static AdvisorBalance Balance() => AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Sam ), "test.sam" );
	private static AdvisorScanContext Context( FakePark? park, int balance = 0 ) => new( Balance(), new FakeMoney { Balance = balance }, park );
	private static int Score( int message, FakePark? park, int balance = 0 ) =>
		new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented ).ScoreOf( AdvisorDescriptorTable.Rows[message], Context( park, balance ), 0 );

	private sealed class FakeMoney : IAdvisorMoneyState
	{
		public int Balance { get; set; }
		public IReadOnlyList<AdvisorLoanSlot> LoanSlots { get; } = new AdvisorLoanSlot[AdvisorLoanSlot.Count];
		public bool LoanAvailable => false;
		public int MonthsInRed => 0;
		public int ParkMonths => 12;
		public IReadOnlyList<int> WageMonths { get; } = Array.Empty<int>();
		public IReadOnlyList<int> IncomeMonths { get; } = Array.Empty<int>();
	}

	/// <summary>A hand-built park state: every statistic is a field, the kinds are 0 rides, 1 shops, 2 sideshows, 3 features.</summary>
	private sealed class FakePark : IAdvisorParkState
	{
		public bool Open { get; set; } = true;
		public int Mechanics { get; set; }
		public int Handymen { get; set; }
		public int ObjectDays { get; set; }
		public int HireDays { get; set; }
		public int StartDays { get; set; }
		public int[] Available { get; } = new int[4];
		public int[] BuiltTypeCount { get; } = new int[4];
		public int[] Standing { get; } = new int[4];
		public int[] Closed { get; } = new int[4];
		public int Broken { get; set; }
		public int Worn { get; set; }
		public int Litter { get; set; }
		public int Cells { get; set; }
		public int ToiletCount { get; set; }
		public int ToiletsShut { get; set; }
		public int Filth { get; set; }
		public int Census { get; set; }
		public int Thirsty { get; set; }
		public int Hungry { get; set; }
		public int NeedingToilet { get; set; }
		public int Balloons { get; set; }
		public int Costumes { get; set; }
		public float Happiness { get; set; }
		public int LastThreshold { get; private set; } = -1;
		public int LastWornPercent { get; private set; } = -1;

		public bool ParkOpen => Open;
		public int StaffCount( StaffType type ) => type switch { StaffType.Mechanic => Mechanics, StaffType.Handyman => Handymen, _ => 0 };
		public int DaysSinceObjectStamp => ObjectDays;
		public int DaysSinceHireStamp => HireDays;
		public int DaysSinceStart => StartDays;
		public int AvailableTypes( int kind, bool withTicketItems ) => Available[kind];
		public int BuiltTypes( int kind, bool withTicketItems ) => BuiltTypeCount[kind];
		public int ObjectsOfKind( int kind, int minimumLevel ) => Standing[kind];
		public int ClosedObjectsOfKind( int kind ) => Closed[kind];
		public int RidesBrokenDown => Broken;
		public int RidesBelowCondition( int percent ) { LastWornPercent = percent; return Worn; }
		public int LitterPoints => Litter;
		public int DevelopedCells => Cells;
		public int Toilets => ToiletCount;
		public int ClosedToilets => ToiletsShut;
		public int MeanToiletFilth => Filth;
		public int GuestsInPark => Census;
		public int GuestsNeeding( GuestNeedMeter meter, int threshold )
		{
			LastThreshold = threshold;
			return meter switch { GuestNeedMeter.Thirst => Thirsty, GuestNeedMeter.Hunger => Hungry, _ => NeedingToilet };
		}
		public int GuestsWithBalloons => Balloons;
		public int GuestsInCostume => Costumes;
		public float AverageHappiness => Happiness;
	}

	// ------------------------------------------------------------------ the guests' needs and the rides

	[TestMethod]
	public void EveryParkCallbackScoresZeroWithoutAParkOrItsThresholds()
	{
		var rows = AdvisorDescriptorTable.Rows.Where( row => AdvisorParkCallbacks.Callbacks.ContainsKey( row.Callback ) ).ToArray();
		Assert.AreEqual( AdvisorParkCallbacks.Callbacks.Count, rows.Length, "one descriptor row per ported park callback" );
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );
		foreach ( var row in rows )
		{
			Assert.AreEqual( 0, scan.ScoreOf( row, Context( null ), 0 ), $"message {row.MessageId} without a park" );
			var bare = new AdvisorScanContext( AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Sam.Split( "VisitorsThirsty" )[0] ), "bare.sam" ), new FakeMoney(), new FakePark { Census = 100, Thirsty = 100, Litter = 90, Standing = { [0] = 5 }, Closed = { [0] = 5 } } );
			Assert.AreEqual( 0, scan.ScoreOf( row, bare, 0 ), $"message {row.MessageId} with a settings file without its thresholds" );
		}
	}

	[TestMethod]
	public void ThirstAndHungerCountTheGuestsAboveTheByteThreshold()
	{
		var park = new FakePark { Thirsty = 12, Hungry = 7 };
		Assert.AreEqual( 12, Score( 3, park ), "12 guests x ScorePerThirstyPerson 1" );
		Assert.AreEqual( 99, park.LastThreshold, "the callback passes the byte of ThirstierThan" );
		Assert.AreEqual( 7, Score( 4, park ) );
		Assert.AreEqual( 0, Score( 3, new FakePark() ), "nobody thirsty" );
		Assert.AreEqual( 12, Score( 3, new FakePark { Open = false, Thirsty = 12 } ), "no park-open test: a closed park is counted as well" );
	}

	[TestMethod]
	public void WornAndBrokenRidesNeedNoMechanic()
	{
		var park = new FakePark { Worn = 4, Broken = 3 };
		Assert.AreEqual( 20, Score( 5, park ), "4 worn rides x ScorePerWornRide 5" );
		Assert.AreEqual( 25, park.LastWornPercent, "PoorerStateThan" );
		Assert.AreEqual( 30, Score( 6, park ), "3 broken rides x 10" );
		park.Mechanics = 1;
		Assert.AreEqual( 0, Score( 5, park ), "a mechanic is hired" );
		Assert.AreEqual( 0, Score( 6, park ) );
	}

	[TestMethod]
	public void TooFewMechanicsCountsTheRidesBeyondWhatTheyCanKeep()
	{
		var park = new FakePark { HireDays = 30 };
		park.Standing[0] = 9;
		Assert.AreEqual( 90, Score( 7, park ), "no mechanic: 9 rides above 0 x 10" );
		park.Mechanics = 2;
		Assert.AreEqual( 10, Score( 7, park ), "9 - 2 x 4 = 1 extra ride x 10" );
		park.Standing[0] = 8;
		Assert.AreEqual( 0, Score( 7, park ), "8 - 8 = 0 is not above 0" );
		park.Standing[0] = 20;
		park.HireDays = 29;
		Assert.AreEqual( 0, Score( 7, park ), "the days since the hire stamp must reach DaysForStaff" );
	}

	// ------------------------------------------------------------------ cleanliness

	[TestMethod]
	public void LitterWithoutAHandymanScoresInTwoBands()
	{
		var park = new FakePark { Litter = 25 };
		Assert.AreEqual( 3 * 25 - 3, Score( 8, park ), "at the high cutoff: ScorePerLitterPoint x litter - the low cutoff" );
		Assert.AreEqual( 0, Score( 9, park ), "below the second band's cutoff of 26" );
		park.Litter = 26;
		Assert.AreEqual( 0, Score( 8, park ), "above the high cutoff of 25" );
		Assert.AreEqual( 3 * 26 - 26, Score( 9, park ), "at the cutoff: 3 x 26 - 26" );
		park.Litter = 40;
		Assert.AreEqual( 3 * 40 - 26, Score( 9, park ) );
		park.Handymen = 1;
		Assert.AreEqual( 0, Score( 9, park ), "a handyman is hired" );
		park.Handymen = 0;
		park.Open = false;
		Assert.AreEqual( 0, Score( 9, park ), "the park is closed" );
	}

	[TestMethod]
	public void CellsPerHandymanNeedsAtLeastOne()
	{
		var park = new FakePark { Cells = 1000, Handymen = 0 };
		Assert.AreEqual( 0, Score( 10, park ), "no handyman" );
		park.Handymen = 1;
		Assert.AreEqual( 2 * (1000 - 400), Score( 10, park ), "(cells - handymen x 400) x ScorePerExtra10Cells" );
		park.Cells = 299;
		Assert.AreEqual( 0, Score( 10, park ), "below MinCellsForConsideration" );
		park.Cells = 300;
		Assert.AreEqual( 0, Score( 10, park ), "300 - 400 is not above 0" );
		park.Cells = 401;
		park.Open = false;
		Assert.AreEqual( 0, Score( 10, park ) );
	}

	[TestMethod]
	public void DirtyToiletsFollowTheHandymenAndTheFilthBands()
	{
		var park = new FakePark { ToiletCount = 3, Filth = 60 };
		Assert.AreEqual( 60, Score( 86, park ), "no handyman, 60 <= filth <= 90: DirtyToilets1 points x filth" );
		Assert.AreEqual( 0, Score( 88, park ), "88 needs 90 filth" );
		park.Filth = 91;
		Assert.AreEqual( 0, Score( 86, park ), "above the upper threshold" );
		Assert.AreEqual( 2 * 91, Score( 88, park ), "DirtyToilets3 points 2 x 91" );
		park.Filth = 59;
		Assert.AreEqual( 0, Score( 86, park ) );
		park.Handymen = 2;
		park.Filth = 75;
		Assert.AreEqual( 0, Score( 86, park ), "86 is for parks without a handyman" );
		Assert.AreEqual( 75, Score( 87, park ), "with handymen the same band scores with the first band's points" );
		park.Filth = 90;
		Assert.AreEqual( 2 * 90, Score( 89, park ), "89: DirtyToilets4 points 2 x 90" );
		Assert.AreEqual( 90, Score( 87, park ) );
		park.ToiletCount = 0;
		Assert.AreEqual( 0, Score( 87, park ), "no toilet, no line" );
		park.ToiletCount = 1;
		park.Open = false;
		Assert.AreEqual( 0, Score( 89, park ), "the park is closed" );
	}

	[TestMethod]
	public void ClosedShareLinesCompareThePercentSignedAndStrictly()
	{
		var park = new FakePark();
		park.Standing[0] = 4;
		park.Closed[0] = 2;
		Assert.AreEqual( 0, Score( 259, park ), "50 percent is not above 50" );
		park.Closed[0] = 3;
		Assert.AreEqual( 80, Score( 259, park ), "75 percent of 4 rides" );
		park.Standing[0] = 2;
		park.Closed[0] = 2;
		Assert.AreEqual( 0, Score( 259, park ), "fewer than AtLeastThisManyRides" );
		park.Standing[0] = 4;
		park.Open = false;
		Assert.AreEqual( 0, Score( 259, park ), "rides closed is a line of the open park" );
		park.Standing[1] = 8;
		park.Closed[1] = 3;
		Assert.AreEqual( 75, Score( 260, park ), "37 percent of 8 shops, no park-open test" );
		park.Closed[1] = 2;
		Assert.AreEqual( 0, Score( 260, park ), "25 percent is not above 25" );
		park.Standing[2] = 1;
		park.Closed[2] = 1;
		Assert.AreEqual( 70, Score( 261, park ) );
		park.Standing[2] = 0;
		Assert.AreEqual( 0, Score( 261, park ), "no sideshows" );
		park.ToiletCount = 5;
		park.ToiletsShut = 1;
		Assert.AreEqual( 0, Score( 262, park ), "20 percent is not above 20" );
		park.ToiletsShut = 2;
		Assert.AreEqual( 75, Score( 262, park ), "40 percent" );
	}

	// ------------------------------------------------------------------ the object lines

	[TestMethod]
	public void PoorSelectionLinesCompareTheBuiltPercentUnsigned()
	{
		var park = new FakePark { ObjectDays = 30 };
		park.Available[0] = 10;
		park.BuiltTypeCount[0] = 2;
		park.Standing[0] = 3;
		Assert.AreEqual( 30, Score( 60, park ), "2 x 100 / 10 = 20 percent is below 30; 3 rides are enough" );
		park.BuiltTypeCount[0] = 3;
		Assert.AreEqual( 0, Score( 60, park ), "30 percent is not below 30" );
		park.BuiltTypeCount[0] = 2;
		park.Standing[0] = 2;
		Assert.AreEqual( 0, Score( 60, park ), "fewer than MoreRidesThan rides" );
		park.Standing[0] = 3;
		park.ObjectDays = 29;
		Assert.AreEqual( 0, Score( 60, park ), "before DaysForObjects" );
		park.ObjectDays = 30;
		park.Available[0] = 0;
		Assert.AreEqual( 0, Score( 60, park ), "no ride type available" );
		// the shops (25 percent, 2 shops), sideshows (15, 2) and features (20, 5) share the shape
		foreach (var (message, kind, poorer, more, score) in new[] { (71, 1, 25, 2, 50), (75, 2, 15, 2, 45), (79, 3, 20, 5, 20) })
		{
			var other = new FakePark { ObjectDays = 30 };
			other.Available[kind] = 100;
			other.Standing[kind] = more;
			other.BuiltTypeCount[kind] = poorer - 1;
			Assert.AreEqual( score, Score( message, other ), $"message {message}" );
			other.BuiltTypeCount[kind] = poorer;
			Assert.AreEqual( 0, Score( message, other ), $"message {message} at the threshold" );
			other.BuiltTypeCount[kind] = 0;
			other.Standing[kind] = more - 1;
			Assert.AreEqual( 0, Score( message, other ), $"message {message} below its minimum" );
		}
	}

	[TestMethod]
	public void BuildTheFirstScoresAskForTheKindBeforeIt()
	{
		var park = new FakePark { ObjectDays = 30, StartDays = 20 };
		Assert.AreEqual( 3 * 20 - 50, Score( 62, park ), "no ride type ever built: -50 + 20 days x 3" );
		park.BuiltTypeCount[0] = 1;
		Assert.AreEqual( 0, Score( 62, park ) );
		// 73 asks for built ride types and no shop type
		Assert.AreEqual( 50 + 2 * 20, Score( 73, park ), "ShopsBuildShops.Score 50 + 20 days x 2" );
		park.BuiltTypeCount[1] = 1;
		Assert.AreEqual( 0, Score( 73, park ), "a shop type was built" );
		// 77 asks for built shop types (not rides) and no sideshow type
		Assert.AreEqual( -70 + 2 * 20, Score( 77, park ) );
		park.BuiltTypeCount[1] = 0;
		Assert.AreEqual( 0, Score( 77, park ), "rides alone do not unlock it" );
		park.BuiltTypeCount[1] = 1;
		park.BuiltTypeCount[2] = 1;
		Assert.AreEqual( 0, Score( 77, park ), "a sideshow type was built" );
		// 81 asks for built sideshow types and no feature type
		Assert.AreEqual( -40 + 20, Score( 81, park ) );
		park.ObjectDays = 29;
		Assert.AreEqual( 0, Score( 81, park ), "before DaysForObjects" );
		Assert.AreEqual( 0, Score( 73, new FakePark { ObjectDays = 30, StartDays = 20 } ), "no ride type built" );
	}

	[TestMethod]
	public void MoreShopsCountsTheShopsTwiceLikeTheOriginalAndMoreSideshowsComparesWithTheRides()
	{
		// 0x59DEC9 pushes kind 1 for the second count where 0x59E159 pushes kind 0: shops x 0.15 can never exceed the shops
		var park = new FakePark { ObjectDays = 30 };
		park.BuiltTypeCount[1] = 5;
		park.BuiltTypeCount[0] = 100;
		Assert.AreEqual( 0, Score( 74, park, balance: 50000 ), "5 x 0.15 is not above 5, however many rides there are" );
		// 78: sideshows built (4) below rides built x 0.2
		park.BuiltTypeCount[2] = 4;
		park.BuiltTypeCount[0] = 21;
		Assert.AreEqual( 80, Score( 78, park, balance: 20001 ), "21 x 0.2 = 4.2 is above 4" );
		park.BuiltTypeCount[0] = 20;
		Assert.AreEqual( 0, Score( 78, park, balance: 20001 ), "20 x 0.2f rounds to 4.0 in a single: not above 4" );
		park.BuiltTypeCount[0] = 21;
		Assert.AreEqual( 0, Score( 78, park, balance: 20000 ), "the balance must exceed WealthMoreThan" );
		park.BuiltTypeCount[2] = 0;
		Assert.AreEqual( 0, Score( 78, park, balance: 20001 ), "no sideshow built" );
	}

	[TestMethod]
	public void NoToiletsCountsTheNeedyShareOfTheGuests()
	{
		var park = new FakePark { Census = 100, NeedingToilet = 26 };
		Assert.AreEqual( 90, Score( 82, park ), "26 percent is above 25" );
		Assert.AreEqual( 60, park.LastThreshold, "NeedingMoreThan" );
		park.NeedingToilet = 25;
		Assert.AreEqual( 0, Score( 82, park ) );
		park.NeedingToilet = 30;
		park.ToiletCount = 1;
		Assert.AreEqual( 0, Score( 82, park ), "a toilet exists" );
		park.ToiletCount = 0;
		park.Open = false;
		Assert.AreEqual( 0, Score( 82, park ), "the park is closed" );
		park.Open = true;
		park.Census = 0;
		Assert.AreEqual( 0, Score( 82, park ), "no guests: no division" );
	}

	[TestMethod]
	public void TheToiletFigureLineNeverPasses()
	{
		Assert.AreEqual( 0, Score( 85, new FakePark { ToiletCount = 4, Filth = 100 } ) );
		Assert.AreEqual( 0, AdvisorScanCallbacks.Implemented[0x59E420]( Context( new FakePark() ) ) );
	}

	// ------------------------------------------------------------------ guest mood

	[TestMethod]
	public void HappyVisitorsPassOnEqualityBalloonsAndCostumesNeedMoreThanTheThreshold()
	{
		var park = new FakePark { Census = 40, Happiness = 80f };
		Assert.AreEqual( 90, Score( 145, park ), "the compare lets a mean equal to HappierThan through" );
		park.Happiness = 79.99f;
		Assert.AreEqual( 0, Score( 145, park ) );
		park.Happiness = 90;
		park.Census = 39;
		Assert.AreEqual( 0, Score( 145, park ), "fewer than MinimumVisitors" );
		var kids = new FakePark { Balloons = 21, Costumes = 30 };
		Assert.AreEqual( 48, Score( 158, kids ) );
		Assert.AreEqual( 0, Score( 159, kids ), "30 costumes are not more than 30" );
		kids.Costumes = 31;
		Assert.AreEqual( 48, Score( 159, kids ) );
		kids.Balloons = 20;
		Assert.AreEqual( 0, Score( 158, kids ) );
	}

	// ------------------------------------------------------------------ the guest statistics

	[TestMethod]
	public void TheGuestBridgeCountsTheCensusGuestsByTheirMeterBytes()
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		var simulation = new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false };
		var economy = EconomyTestData.Park();
		var bridge = new GuestEconomyBridge( () => economy, simulation );
		Assert.AreEqual( (0, 0, 0, 0f), (bridge.CountGuestsNeeding( GuestNeedMeter.Thirst, 0 ), bridge.GuestsWithBalloons, bridge.GuestsInCostume, bridge.CensusHappiness), "no guests" );
		var guests = Enumerable.Range( 0, 4 ).Select( _ => simulation.SpawnInPark( 6, 5 ) ).ToArray();
		(guests[0].Thirst, guests[1].Thirst, guests[2].Thirst, guests[3].Thirst) = (100f, 99.9f, 50f, 0f);
		(guests[0].Hunger, guests[1].Hunger) = (100f, 100f);
		(guests[2].Toilet, guests[3].Toilet) = (60.5f, 61f);
		Assert.AreEqual( 1, bridge.CountGuestsNeeding( GuestNeedMeter.Thirst, 99 ), "the byte of 99.9 is 99, not above 99" );
		Assert.AreEqual( 2, bridge.CountGuestsNeeding( GuestNeedMeter.Thirst, 98 ) );
		Assert.AreEqual( 1, bridge.CountGuestsNeeding( GuestNeedMeter.Thirst, 99 + 256 ), "only the low byte of the threshold is compared (0x4C93F0 reads a byte argument)" );
		Assert.AreEqual( 2, bridge.CountGuestsNeeding( GuestNeedMeter.Hunger, 99 ) );
		Assert.AreEqual( 1, bridge.CountGuestsNeeding( GuestNeedMeter.Toilet, 60 ), "60.5 truncates to 60, 61 is above" );
		guests[0].HasBalloon = true;
		guests[1].HasBalloon = true;
		guests[2].InCostume = true;
		Assert.AreEqual( (2, 1), (bridge.GuestsWithBalloons, bridge.GuestsInCostume) );
		(guests[0].Happiness, guests[1].Happiness, guests[2].Happiness, guests[3].Happiness) = (80f, 80.9f, 70f, 100f);
		Assert.AreEqual( 0f, bridge.CensusHappiness, "0 while the park is closed" );
		economy.OpenPark();
		Assert.IsTrue( economy.IsParkOpen );
		Assert.AreEqual( 82.5f, bridge.CensusHappiness, "(80 + 80 + 70 + 100) / 4 over the truncated bytes" );
		guests[3].State = GuestState.Gone;
		Assert.AreEqual( 76.666664f, bridge.CensusHappiness, 1e-5f, "a guest who has left is no census guest: 230 / 3 as a single" );
		Assert.AreEqual( 3, bridge.GuestCensus );
	}

	[TestMethod]
	public void EveryParkThresholdExistsInTheShippedSettings()
	{
		var balance = ShippedBalance();
		foreach ( var key in AdvisorParkCallbacks.IntegerKeys.Concat( AdvisorParkCallbacks.FloatKeys ) )
			Assert.IsTrue( balance.ScanParameters.ContainsKey( key ), $"{key} is read from Advisor.sam" );
		Assert.AreEqual( 0.15f, (float)balance.ScanParameters["ShopsBuildMoreShops.ShopsPerRideRatio"], "the ratios are singles" );
		foreach ( var (message, key, fixedScore) in AdvisorTables.ScanScoreKeys.Where( entry => entry.FixedScore == null ) )
			Assert.IsTrue( balance.Scores.ContainsKey( key ), $"message {message}: {key}" );
		Assert.IsTrue( balance.Groups.ContainsKey( 3 ), "the guest mood lines are group 3" );
		Assert.AreEqual( 100, balance.ScoreOf( 258 ) );
	}

	private static AdvisorBalance ShippedBalance()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !System.IO.Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Advisor.sam." );
		var file = System.IO.Directory.EnumerateFiles( gamePath!, "Advisor.sam", System.IO.SearchOption.AllDirectories ).FirstOrDefault();
		if ( file == null )
			Assert.Inconclusive( "The original Advisor/Advisor.sam is missing." );
		return AdvisorBalance.Parse( System.IO.File.ReadAllBytes( file! ), file! );
	}

	// ------------------------------------------------------------------ through the real queue

	private static IReadOnlyList<(int Message, int Score)> Pass( FakePark park, int balance = 0 )
	{
		var controller = new AdvisorController( Balance() );
		var money = new FakeMoney { Balance = balance };
		var posted = new List<(int, int)>();
		for ( var step = 0; step < 155; step++ )
			if ( controller.ScanOnce( money, 100, 0, true, park ).Posted is { } advice )
				posted.Add( (advice.MessageId, advice.Score) );
		return posted;
	}

	[TestMethod]
	public void HeavyLitterWithoutHandymenQueuesTheLitterAdvice()
	{
		var posted = Pass( new FakePark { Litter = 40 } );
		CollectionAssert.AreEqual( new[] { (9, 3 * 40 - 26) }, posted.ToArray(), "a whole scan pass posts the litter line of the band above 25" );
		var light = Pass( new FakePark { Litter = 12 } );
		CollectionAssert.AreEqual( new[] { (8, 3 * 12 - 3) }, light.ToArray(), "a lighter litter share posts the low band's line" );
		Assert.AreEqual( 0, Pass( new FakePark { Litter = 40, Handymen = 1 } ).Count, "with a handyman there is nothing to say" );
	}

	[TestMethod]
	public void BrokenRidesWithoutAMechanicQueueTheMechanicAdvice()
	{
		var posted = Pass( new FakePark { Broken = 3, Worn = 6 } );
		CollectionAssert.AreEquivalent( new[] { (5, 30), (6, 30) }, posted.ToArray(), "worn rides (6 x 5) and broken rides (3 x 10) both ask for a mechanic" );
		var hired = new FakePark { Broken = 3, Worn = 5, Mechanics = 1, HireDays = 40 };
		hired.Standing[0] = 8;
		CollectionAssert.AreEqual( new[] { (7, 10 * (8 - 4)) }, Pass( hired ).ToArray(), "one mechanic keeps four rides: four rides over" );
	}

	[TestMethod]
	public void ClosedRidesAndGuestsInNeedQueueTheirLines()
	{
		var park = new FakePark { Census = 200, Thirsty = 30, Hungry = 0 };
		park.Standing[0] = 4;
		park.Closed[0] = 3;
		CollectionAssert.AreEquivalent( new[] { (3, 30), (259, 80) }, Pass( park ).ToArray() );
	}

	// ------------------------------------------------------------------ the statistics over a real park

	private static ParkEconomy Park() => EconomyTestData.Park( initialCash: 500000 );

	[TestMethod]
	public void TheEconomyStateCountsObjectsStatesAndTypes()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		Assert.AreEqual( 0, state.ObjectsOfKind( 0, 0 ) );
		var first = park.RegisterExisting( 1100 );
		var second = park.RegisterExisting( 1180 );
		var shop = park.RegisterExisting( 1203 );
		var toilet = park.RegisterExisting( 1402 );
		park.RegisterExisting( 1601 );
		Assert.AreEqual( 2, state.ObjectsOfKind( 0, 0 ), "two rides; the gates are a fixed item" );
		Assert.AreEqual( 1, state.ObjectsOfKind( 1, 0 ) );
		Assert.AreEqual( 1, state.ObjectsOfKind( 3, 0 ), "the toilet is a feature" );
		Assert.AreEqual( 1, state.Toilets );
		first.Repair = 24.9;
		second.Repair = 80;
		Assert.AreEqual( 1, state.RidesBelowCondition( 25 ), "the byte of 24.9 is 24, below 25" );
		Assert.AreEqual( 0, state.RidesBelowCondition( 24 ) );
		Assert.AreEqual( 0, state.RidesBrokenDown );
		second.IsBrokenDown = true;
		Assert.AreEqual( 1, state.RidesBrokenDown );
		Assert.AreEqual( 2, state.RidesBelowCondition( 25 ), "a broken ride counts whatever its condition" );
		second.LifeGauge = 0;
		Assert.AreEqual( 0, state.RidesBrokenDown, "a condemned ride (state 4) is not state 1" );
		Assert.AreEqual( 1, state.RidesBelowCondition( 25 ) );
		Assert.AreEqual( 1, state.ClosedObjectsOfKind( 0 ), "a condemned ride is closed" );
		first.IsOpen = false;
		Assert.AreEqual( 2, state.ClosedObjectsOfKind( 0 ) );
		Assert.AreEqual( 0, state.ClosedObjectsOfKind( 1 ) );
		shop.IsOpen = false;
		Assert.AreEqual( 1, state.ClosedObjectsOfKind( 1 ) );
		Assert.AreEqual( 0, state.ClosedToilets );
		toilet.IsOpen = false;
		Assert.AreEqual( 1, state.ClosedToilets );
	}

	[TestMethod]
	public void TheEconomyStateReadsToiletFilthAsTheIntegerMean()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		Assert.AreEqual( 0, state.MeanToiletFilth, "no toilet" );
		park.RegisterExisting( 1402 ).Repair = 100;
		park.RegisterExisting( 1402 ).Repair = 49.9;
		park.RegisterExisting( 1402 ).Repair = 0;
		// (0 + 100 - 49 + 100) / 3 = 50 (the integer mean of 100 minus the truncated condition bytes)
		Assert.AreEqual( (0 + (100 - 49) + 100) / 3, state.MeanToiletFilth );
	}

	[TestMethod]
	public void TheEconomyStateTellsTypesAvailableAndBuilt()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		// the ride types are 1100, 1180, 1106, 1112 and the add-on 1500 (which counts as a ride: WhichUIType 0); 1601 is a fixed item and no ride type
		var rides = new[] { 1100, 1180, 1106, 1112, 1500 }.Where( id => park.Research.IsAvailable( id ) ).ToArray();
		Assert.IsTrue( park.Research.IsAvailable( 1100 ), "a type outside the research tables is available" );
		Assert.AreEqual( rides.Length, state.AvailableTypes( 0, withTicketItems: true ) );
		Assert.AreEqual( rides.Length - (rides.Contains( 1112 ) ? 1 : 0), state.AvailableTypes( 0, withTicketItems: false ), "the golden ticket type 1112 counts only with the flag" );
		Assert.AreEqual( 1, state.AvailableTypes( 1, withTicketItems: false ), "the drinks shop" );
		Assert.AreEqual( 1, state.AvailableTypes( 2, withTicketItems: false ), "the sideshow" );
		Assert.AreEqual( 1, state.AvailableTypes( 3, withTicketItems: false ), "the toilet" );
		Assert.AreEqual( 0, state.BuiltTypes( 0, withTicketItems: false ) );
		park.RegisterExisting( 1100 );
		park.RegisterExisting( 1100 );
		park.RegisterExisting( 1112 );
		Assert.AreEqual( 1, state.BuiltTypes( 0, withTicketItems: false ), "two objects of 1100 are one type; 1112 has a golden ticket cost" );
		Assert.AreEqual( 2, state.BuiltTypes( 0, withTicketItems: true ) );
		park.Sell( park.Objects.First( item => item.InfoId == 1112 ).Id );
		Assert.AreEqual( 2, state.BuiltTypes( 0, withTicketItems: true ), "the control's built count is never lowered" );
	}

	[TestMethod]
	public void TheEconomyStateCountsDaysFromTheNewestStampAndTheStart()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		EconomyTestData.AdvanceToTurn( park, 100 * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn );
		Assert.AreEqual( 100, state.DaysSinceStart );
		Assert.AreEqual( 100, state.DaysSinceObjectStamp, "no object: the stamp is 0, so the days since the start" );
		Assert.AreEqual( 100, state.DaysSinceHireStamp );
		park.RegisterExisting( 1100 );
		Assert.AreEqual( 0, state.DaysSinceObjectStamp, "an object built now" );
		EconomyTestData.HireBest( park, StaffType.Mechanic );
		Assert.AreEqual( 0, state.DaysSinceHireStamp );
		EconomyTestData.AdvanceToTurn( park, 125 * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn );
		Assert.AreEqual( 25, state.DaysSinceObjectStamp );
		Assert.AreEqual( 25, state.DaysSinceHireStamp );
		Assert.AreEqual( 125, state.DaysSinceStart );
		Assert.AreEqual( 1, state.StaffCount( StaffType.Mechanic ) );
		Assert.AreEqual( 0, state.StaffCount( StaffType.Handyman ) );
	}

	[TestMethod]
	public void TheEconomyStateCountsLitterAndDevelopedCellsOverTheMap()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		Assert.AreEqual( 0, state.LitterPoints, "no cells at all" );
		Assert.AreEqual( 0, state.DevelopedCells );
		var map = new ParkCellMap( 128, 128 );
		park.Litter.Cells = map;
		for ( var x = 0; x < 20; x++ )
			map.SetType( x, 5, ParkCellTypes.Entrance );
		for ( var x = 20; x < 40; x++ )
			map.SetType( x, 5, 1 );
		map.SetType( 40, 5, 4 );
		map.SetType( 41, 5, ParkCellTypes.Exit );
		map.SetType( 42, 5, 3 );
		Assert.AreEqual( 20 + 20 + 1 + 1 + 1, state.DevelopedCells, "the types 1, 3, 4, 9 and 10" );
		Assert.AreEqual( 0, state.LitterPoints, "no litter" );
		// 43 walkable cells (1, 3, 9; the exit and the object cell are not) and 2 littered ones, one of them on a walkable cell and one on the ground
		for ( var x = 0; x < 20; x++ )
			Assert.IsTrue( map.RawTypeAt( x, 5 ) == ParkCellTypes.Entrance );
		Assert.IsTrue( park.Litter.Drop( 3, 5, LitterType.Dodgy ) );
		Assert.IsTrue( park.Litter.Drop( 60, 60, LitterType.Dodgy ) );
		var walkable = 20 + 20 + 1;
		Assert.AreEqual( 2 * 100 / (walkable + 1), state.LitterPoints, "littered cells x 100 / (walkable cells + littered cells off the paths)" );
	}

	[TestMethod]
	public void TheEconomyStateDoesNotChangeThePark()
	{
		var park = Park();
		park.Litter.Cells = new ParkCellMap( 128, 128 );
		park.RegisterExisting( 1100 );
		park.RegisterExisting( 1402 );
		EconomyTestData.HireBest( park, StaffType.Handyman );
		var before = ParkSaveFile.Serialize( park );
		var state = new EconomyParkState( park );
		_ = (state.ParkOpen, state.StaffCount( StaffType.Mechanic ), state.DaysSinceObjectStamp, state.DaysSinceHireStamp, state.DaysSinceStart, state.AvailableTypes( 0, true ), state.BuiltTypes( 0, true ),
			state.ObjectsOfKind( 0, 0 ), state.ClosedObjectsOfKind( 0 ), state.RidesBrokenDown, state.RidesBelowCondition( 50 ), state.LitterPoints, state.DevelopedCells, state.Toilets,
			state.ClosedToilets, state.MeanToiletFilth, state.GuestsInPark, state.GuestsNeeding( GuestNeedMeter.Thirst, 0 ), state.GuestsWithBalloons, state.GuestsInCostume, state.AverageHappiness);
		Assert.AreEqual( before, ParkSaveFile.Serialize( park ), "reading the statistics wrote nothing" );
	}

	// ------------------------------------------------------------------ the ride events

	[TestMethod]
	public void TheImminentBreakdownLineIsCrossedOnceBelowTen()
	{
		Assert.IsTrue( AdvisorRideEvents.CrossesImminentLine( 9.5f, 1f ), "condition 9, wear 1: 10 is not below 10" );
		Assert.IsFalse( AdvisorRideEvents.CrossesImminentLine( 9.5f, 0.9f ), "9 + 0.9 is below 10: the ride was already below the line" );
		Assert.IsFalse( AdvisorRideEvents.CrossesImminentLine( 10f, 5f ), "the byte 10 is not below 10" );
		Assert.IsFalse( AdvisorRideEvents.CrossesImminentLine( 5f, 0f ), "no wear: nothing crossed" );
		Assert.IsTrue( AdvisorRideEvents.CrossesImminentLine( 0f, 10f ) );
	}

	[TestMethod]
	public void TheWearStepRaisesTheImminentBreakdownEventAndTheAdvisorQueuesIt()
	{
		var park = EconomyTestData.Park();
		var rides = new EconomyTestData.RideOperations();
		park.RideOperations = rides;
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		park.TryBuild( 1100, out var ride );
		// five riders at speed 50: a wear of 3.875 per step from 100, so the 24th step takes it from 10.875 to 7.0
		rides.Set( ride!.Id, new RideOperation( true, 5, 50 ) );
		EconomyTestData.AdvanceToTurn( park, 23 * ParkEconomy.WearInterval );
		Assert.AreEqual( 10, ride.StateOfRepair );
		Assert.AreEqual( 0, events.Count( item => item.Kind == ParkEventKind.RideBreakdownImminent ), "the byte 10 is not below 10" );
		EconomyTestData.AdvanceToTurn( park, 24 * ParkEconomy.WearInterval );
		var imminent = events.Where( item => item.Kind == ParkEventKind.RideBreakdownImminent ).ToArray();
		Assert.AreEqual( 1, imminent.Length, "the 24th wear step crossed the line" );
		Assert.AreEqual( (ride.Id, 7), (imminent[0].InstanceId, (int)imminent[0].Amount) );
		EconomyTestData.AdvanceToTurn( park, 25 * ParkEconomy.WearInterval );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.RideBreakdownImminent ), "once: the next step starts below the line" );
		Assert.AreEqual( 100, new AdvisorController( Balance() ).Balance.ScoreOf( 66 ), "with RidesBreakdownImminent.Score" );
	}

	[TestMethod]
	public void ABreakdownPostsTheMechanicLineOrTheCondemnedLine()
	{
		var park = Park();
		var ride = park.RegisterExisting( 1100 );
		Assert.AreEqual( 68, AdvisorRideEvents.ForBreakdown( park, ride ), "no mechanic hired" );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		Assert.IsNull( AdvisorRideEvents.ForBreakdown( park, ride ), "a mechanic is free: he is sent, nothing to say here" );
		mechanic.State = StaffState.Resting;
		Assert.AreEqual( 69, AdvisorRideEvents.ForBreakdown( park, ride ), "a resting mechanic cannot come" );
		mechanic.State = StaffState.OnStrike;
		Assert.AreEqual( 69, AdvisorRideEvents.ForBreakdown( park, ride ) );
		mechanic.State = StaffState.Patrolling;
		mechanic.AssignedInstanceId = ride.Id + 1000;
		Assert.AreEqual( 69, AdvisorRideEvents.ForBreakdown( park, ride ), "busy with another ride" );
		mechanic.AssignedInstanceId = ride.Id;
		Assert.IsNull( AdvisorRideEvents.ForBreakdown( park, ride ), "on the way to this ride: advice 70 is the dispatch's" );
		var second = EconomyTestData.HireBest( park, StaffType.Mechanic );
		mechanic.AssignedInstanceId = ride.Id + 1000;
		Assert.IsNull( AdvisorRideEvents.ForBreakdown( park, ride ), "the second mechanic is free" );
		second.AssignedInstanceId = ride.Id + 1000;
		Assert.AreEqual( 69, AdvisorRideEvents.ForBreakdown( park, ride ) );
		ride.LifeGauge = 0.5;
		Assert.AreEqual( 258, AdvisorRideEvents.ForBreakdown( park, ride ), "a life gauge byte of 0 is a condemned ride" );
		Assert.AreEqual( 100, new AdvisorController( Balance() ).Balance.ScoreOf( 258 ) );
	}

	// ------------------------------------------------------------------ the event mapping of AutomaticAdvisor

	private static IReadOnlyList<int> Pending( AutomaticAdvisor advisor ) =>
		advisor.Controller.CaptureSnapshot().Queue.Slots.Where( slot => slot != null ).Select( slot => slot!.MessageId ).ToArray();

	private static (ParkEconomy Park, AutomaticAdvisor Advisor) Attached()
	{
		var park = EconomyTestData.Park();
		var advisor = new AutomaticAdvisor( new AdvisorController( Balance() ), () => 0 );
		advisor.AttachEconomy( park );
		return (park, advisor);
	}

	/// <summary>A script-hosted ride worn down to its breakdown request (the wear of 3.875 per step breaks it at the 26th step), not yet in its broken state.</summary>
	private static ParkObjectState WornToTheRequest( ParkEconomy park, List<ParkEvent> events )
	{
		var rides = new EconomyTestData.RideOperations();
		park.RideOperations = rides;
		park.EventRaised += events.Add;
		var ride = park.RegisterExisting( 1100 );
		ride.ScriptHosted = true;
		park.ObserveScriptState( ride, false );
		rides.Set( ride.Id, new RideOperation( true, 5, 50 ) );
		EconomyTestData.AdvanceToTurn( park, 26 * ParkEconomy.WearInterval );
		return ride;
	}

	[TestMethod]
	public void TheAdvisorAnswersTheImminentBreakdownEventWithAdvice66()
	{
		var (park, advisor) = Attached();
		var events = new List<ParkEvent>();
		WornToTheRequest( park, events );
		Assert.IsTrue( events.Any( item => item.Kind == ParkEventKind.RideBreakdownImminent ) );
		CollectionAssert.Contains( Pending( advisor ).ToList(), 66, "the event reaches the controller's queue through the advisor's own mapping" );
	}

	[TestMethod]
	public void TheBreakdownAdviceWaitsForTheBrokenStateAndReadsTheMechanicsThen()
	{
		// tp.exe answers the type 7 message of the state change (0x4E0FA0), not the request (0x4E0B90): a mechanic who rests at the request and is free when the script reports VAR_BROKEN says nothing
		var (park, advisor) = Attached();
		var events = new List<ParkEvent>();
		var ride = WornToTheRequest( park, events );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		mechanic.State = StaffState.Resting;
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.RideBrokeDown ), "the request was raised" );
		Assert.AreEqual( 0, events.Count( item => item.Kind == ParkEventKind.RideEnteredBrokenState ), "the ride has not entered its broken state yet" );
		Assert.IsFalse( Pending( advisor ).Contains( 69 ) || Pending( advisor ).Contains( 68 ), "nothing is said at the request" );
		mechanic.State = StaffState.Patrolling;
		park.ObserveScriptState( ride, true );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.RideEnteredBrokenState ) );
		Assert.IsFalse( Pending( advisor ).Contains( 69 ), "the mechanic is free when the ride breaks: tp.exe says nothing" );
	}

	[TestMethod]
	public void ABusyMechanicAtTheBrokenStateGivesAdvice69AndNoMechanicAdvice68()
	{
		var (park, advisor) = Attached();
		var events = new List<ParkEvent>();
		var ride = WornToTheRequest( park, events );
		park.ObserveScriptState( ride, true );
		CollectionAssert.Contains( Pending( advisor ).ToList(), 68, "no mechanic is hired" );
		var (busyPark, busyAdvisor) = Attached();
		var busyRide = WornToTheRequest( busyPark, new List<ParkEvent>() );
		EconomyTestData.HireBest( busyPark, StaffType.Mechanic ).State = StaffState.Resting;
		busyPark.ObserveScriptState( busyRide, true );
		CollectionAssert.Contains( Pending( busyAdvisor ).ToList(), 69, "the only mechanic rests" );
		Assert.IsFalse( Pending( busyAdvisor ).Contains( 68 ) );
	}

	[TestMethod]
	public void ACondemnedRideGivesAdvice258AndAHeadlessBreakdownEntersItsStateAtOnce()
	{
		var (park, advisor) = Attached();
		var events = new List<ParkEvent>();
		var ride = WornToTheRequest( park, events );
		ride.LifeGauge = 0.5;
		park.ObserveScriptState( ride, true );
		CollectionAssert.Contains( Pending( advisor ).ToList(), 258 );
		Assert.IsFalse( Pending( advisor ).Contains( 68 ), "a condemned ride sends the other message" );
		// an economy-only ride (no script) enters the broken state in the step that requests it
		var headless = EconomyTestData.Park();
		var raised = new List<ParkEvent>();
		var operations = new EconomyTestData.RideOperations();
		headless.RideOperations = operations;
		headless.EventRaised += raised.Add;
		var plain = headless.RegisterExisting( 1100 );
		operations.Set( plain.Id, new RideOperation( true, 5, 50 ) );
		EconomyTestData.AdvanceToTurn( headless, 26 * ParkEconomy.WearInterval );
		Assert.IsTrue( plain.IsBrokenDown );
		Assert.AreEqual( 1, raised.Count( item => item.Kind == ParkEventKind.RideEnteredBrokenState ) );
	}

	[TestMethod]
	public void TheStampsFollowBuildsAndHiresAndRideThroughTheSnapshot()
	{
		var (park, advisor) = Attached();
		EconomyTestData.AdvanceToTurn( park, 2304 );
		park.TryBuild( 1100, out _ );
		EconomyTestData.AdvanceToTurn( park, 2880 );
		var state = new EconomyParkState( park, 2304, 0 );
		Assert.AreEqual( 25, state.DaysSinceObjectStamp );
		var snapshot = advisor.CaptureSnapshot();
		Assert.AreEqual( 2304, snapshot.ObjectStampTurn, "the build event moved the advisor's stamp" );
		var restored = new AutomaticAdvisor( new AdvisorController( Balance() ), () => 0 );
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( snapshot.ObjectStampTurn, restored.CaptureSnapshot().ObjectStampTurn );
	}

	[TestMethod]
	public void TheStampFollowsTheFixedItemsTheGameMakesButNoRegistration()
	{
		var (park, advisor) = Attached();
		var raised = new List<ParkEvent>();
		park.EventRaised += raised.Add;
		EconomyTestData.AdvanceToTurn( park, 2304 );
		park.TryBuild( 1100, out _ );
		EconomyTestData.AdvanceToTurn( park, 2880 );
		// an object registered as it stands (an original save's, a park's own fixed items at its start) was not constructed now: the loader reads the saved stamp back (0x4C7289)
		park.RegisterExisting( 1601 );
		Assert.AreEqual( 2304, advisor.CaptureSnapshot().ObjectStampTurn, "a registration moves no stamp" );
		// an arrival vehicle at its first trip or the finale's End: the common constructor stamps the park (0x4DB7A3) as it does for a purchase
		var made = park.RegisterFixedItemMade( 1601 );
		Assert.AreEqual( 2880, advisor.CaptureSnapshot().ObjectStampTurn, "a fixed item made in play moves the stamp" );
		var item = raised.Single( candidate => candidate.Kind == ParkEventKind.FixedItemMade );
		Assert.AreEqual( (made.Id, 1601, 0L, "Gates"), (item.InstanceId, item.InfoId, item.Amount, item.Detail) );
		Assert.IsTrue( made.Imported, "nothing was bought" );
		EconomyTestData.AdvanceToTurn( park, 3456 );
		Assert.AreEqual( 25, new EconomyParkState( park, advisor.CaptureSnapshot().ObjectStampTurn ).DaysSinceObjectStamp, "the days count from the fixed item" );
	}

	[TestMethod]
	public void AFixedItemMadeInPlayLeavesTheEconomyAndItsHashAsARegistrationDoes()
	{
		// the stamp is the advisor's own record (tp.exe's mLastObjectBuilt is read by the advisor only, 0x4C9070): the event adds nothing the park save or the world hash reads
		var made = EconomyTestData.Park();
		var registered = EconomyTestData.Park();
		EconomyTestData.AdvanceToTurn( made, 2880 );
		EconomyTestData.AdvanceToTurn( registered, 2880 );
		made.RegisterFixedItemMade( 1601 );
		registered.RegisterExisting( 1601 );
		Assert.AreEqual( ParkSaveFile.Serialize( registered ), ParkSaveFile.Serialize( made ) );
		Assert.AreEqual( WorldStateHash.Compute( new WorldStateSources { Economy = registered } ), WorldStateHash.Compute( new WorldStateSources { Economy = made } ) );
	}
}
