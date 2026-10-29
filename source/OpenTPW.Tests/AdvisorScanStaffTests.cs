using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the staff callbacks of the advisor scan (hiring and placing staff 11, 13, 14, 18, 20, 23, 26; training 28-32 and 146-150; too many staff 108-112; the park limits 114-123;
/// the staff happy 144; the staff room 169 and 263; the general lines 170-172), and the statistics they read (<see cref="EconomyParkState"/> as <see cref="IAdvisorStaffState"/>).
/// GPU-free; the thresholds and scores of the test settings are invented, each one different so that a swapped key shows.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorScanStaffTests
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
		WaitingTimes.DaysForStaff	10
		WaitingTimes.DaysForObjects	30
		StaffHireGuards1.AtLeastThisBusy	20
		StaffHireGuards1.ScorePerExtraBusyPoint	2
		StaffHireGuards3.IdealDevelopedCellsPerGuard	250
		StaffHireGuards3.ScorePerExtra10Cells	3
		StaffHireGuards3.MinCellsForConsideration	200
		StaffHireEntertainers1.AtLeastThisBusy	15
		StaffHireEntertainers1.ScorePerExtraBusyPoint	4
		StaffSetMechanicArea.MonthsIntoGameForConsideration	2
		StaffSetMechanicArea.AtLeastThisMany	2
		StaffSetMechanicArea.DontBotherIfPatrolledUp	50
		StaffSetMechanicArea.ScorePerUnpatrolledPct	3
		StaffSetHandymanArea.MonthsIntoGameForConsideration	3
		StaffSetHandymanArea.AtLeastThisMany	2
		StaffSetHandymanArea.DontBotherIfPatrolledUp	40
		StaffSetHandymanArea.ScorePerUnpatrolledPct	4
		StaffSetGuardArea.MonthsIntoGameForConsideration	4
		StaffSetGuardArea.AtLeastThisMany	3
		StaffSetGuardArea.DontBotherIfPatrolledUp	30
		StaffSetGuardArea.ScorePerUnpatrolledPct	5
		StaffSetEntertainerArea.MonthsIntoGameForConsideration	5
		StaffSetEntertainerArea.AtLeastThisMany	3
		StaffSetEntertainerArea.DontBotherIfPatrolledUp	60
		StaffSetEntertainerArea.ScorePerUnpatrolledPct	6
		StaffTrainMechanics.WorseThan	40
		StaffTrainMechanics.Score	31
		StaffTrainHandymen.WorseThan	41
		StaffTrainHandymen.Score	32
		StaffTrainGuards.WorseThan	42
		StaffTrainGuards.Score	33
		StaffTrainEntertainers.WorseThan	43
		StaffTrainEntertainers.Score	34
		StaffTrainResearchers.WorseThan	44
		StaffTrainResearchers.Score	35
		LotsHandyman.FudgeFactor	1
		LotsHandyman.Score	61
		LotsEntertainers.FudgeFactor	2
		LotsEntertainers.Score	62
		LotsMechanics.FudgeFactor	3
		LotsMechanics.Score	63
		LotsSecurity.FudgeFactor	4
		LotsSecurity.Score	64
		LotsResearchers.FudgeFactor	5
		LotsResearchers.Score	65
		MaxHandymen.MoreThan	10
		MaxHandymen.Score	71
		MaxHandymenV2.Score	72
		MaxEntertainers.MoreThan	10
		MaxEntertainers.Score	73
		MaxEntertainersV2.Score	74
		MaxMechanics.MoreThan	10
		MaxMechanics.Score	75
		MaxMechanicsV2.Score	76
		MaxGuards.MoreThan	10
		MaxGuards.Score	77
		MaxGuardsV2.Score	78
		MaxResearchers.MoreThan	6
		MaxResearchers.Score	79
		MaxResearchersV2.Score	80
		CongratStaffHappy.MinimumStaff	5
		CongratStaffHappy.HappierThan	70
		CongratStaffHappy.Score	81
		CongratSecurityTrained.MinimumGuards	2
		CongratSecurityTrained.BetterThan	60
		CongratSecurityTrained.Score	91
		CongratResearchersTrained.MinimumResearchers	1
		CongratResearchersTrained.BetterThan	55
		CongratResearchersTrained.Score	92
		CongratHandymenTrained.MinimumHandymen	3
		CongratHandymenTrained.BetterThan	50
		CongratHandymenTrained.Score	93
		CongratEntertainersTrained.MinimumEntertainers	2
		CongratEntertainersTrained.BetterThan	45
		CongratEntertainersTrained.Score	94
		CongratMechanicsTrained.MinimumMechanics	4
		CongratMechanicsTrained.BetterThan	65
		CongratMechanicsTrained.Score	95
		NeedStaffRoom.PctOfStaffTired	40
		NeedStaffRoom.Score	101
		HireStaffGeneral.MinVisitorsEver	50
		HireStaffGeneral.Score	102
		SetPatrolsGeneral.MinStaffForMessage	4
		SetPatrolsGeneral.Score	103
		TrainStaffGeneral.MinStaffForMessage	3
		TrainStaffGeneral.Score	104
		StaffroomClosed.MoreThanThisPercentClosed	30
		StaffroomClosed.Score	105
		""";

	/// <summary>The settings file of the tests; <paramref name="without"/> drops the lines that start with a name.</summary>
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

	/// <summary>A park state without staff statistics: only what <see cref="IAdvisorParkState"/> asks, every statistic zero.</summary>
	private class PlainPark : IAdvisorParkState
	{
		public virtual bool ParkOpen => true;
		public virtual int StaffCount( StaffType type ) => 0;
		public virtual int DaysSinceObjectStamp => 0;
		public virtual int DaysSinceHireStamp => 0;
		public virtual int DaysSinceStart => 0;
		public int AvailableTypes( int kind, bool withTicketItems ) => 0;
		public int BuiltTypes( int kind, bool withTicketItems ) => 0;
		public virtual int ObjectsOfKind( int kind, int minimumLevel ) => 0;
		public int ClosedObjectsOfKind( int kind ) => 0;
		public int RidesBrokenDown => 0;
		public int RidesBelowCondition( int percent ) => 0;
		public int LitterPoints => 0;
		public virtual int DevelopedCells => 0;
		public int Toilets => 0;
		public int ClosedToilets => 0;
		public int MeanToiletFilth => 0;
		public virtual int GuestsInPark => 0;
		public int GuestsNeeding( GuestNeedMeter meter, int threshold ) => 0;
		public int GuestsWithBalloons => 0;
		public int GuestsInCostume => 0;
		public float AverageHappiness => 0;
	}

	/// <summary>A hand-built park state with the staff statistics; a role missing from a dictionary has none.</summary>
	private sealed class StaffPark : PlainPark, IAdvisorStaffState
	{
		public bool Open { get; set; } = true;
		public int HireDays { get; set; } = 99;
		public int StartDays { get; set; } = 30 * 12;
		public int Census { get; set; }
		public int Cells { get; set; }
		public int Rides { get; set; }
		public Dictionary<StaffType, int> Counts { get; } = new();
		public Dictionary<StaffType, int> Patrolled { get; } = new();
		public Dictionary<StaffType, int> Training { get; } = new();
		public Dictionary<StaffType, int> Budgets { get; } = new();
		public Dictionary<StaffType, int> Limits { get; } = Enum.GetValues<StaffType>().ToDictionary( role => role, _ => 10 );
		public int? Total { get; set; }
		public int PatrolledAll { get; set; }
		public float StaffHappiness { get; set; }
		public int Tired { get; set; }
		public bool Trained { get; set; }
		public int Visitors { get; set; }
		public int Unhappy { get; set; }
		public int LastUnhappyLevel { get; private set; } = -1;
		public int Rooms { get; set; }
		public int ShutRooms { get; set; }

		public override bool ParkOpen => Open;
		public override int StaffCount( StaffType type ) => Counts.GetValueOrDefault( type );
		public override int DaysSinceHireStamp => HireDays;
		public override int DaysSinceStart => StartDays;
		public override int DevelopedCells => Cells;
		public override int GuestsInPark => Census;
		public override int ObjectsOfKind( int kind, int minimumLevel ) => kind == 0 ? Rides : 0;

		public int StaffTotal => Total ?? Counts.Values.Sum();
		public int StaffWithPatrolArea( StaffType? role ) => role is { } one ? Patrolled.GetValueOrDefault( one ) : PatrolledAll;
		public int AverageTraining( StaffType role ) => Training.GetValueOrDefault( role );
		public float AverageStaffHappiness => StaffHappiness;
		public int TiredStaff => Tired;
		public int TrainingBudget( StaffType role ) => Budgets.GetValueOrDefault( role );
		public bool HaveEverTrained => Trained;
		public int VisitorsEver => Visitors;
		public int GuestPercentBelowHappiness( int happiness ) { LastUnhappyLevel = happiness; return Unhappy; }
		public int StaffRooms => Rooms;
		public int ClosedStaffRooms => ShutRooms;
		public int StaffLimit( StaffType role ) => Limits[role];
	}

	// ------------------------------------------------------------------ the table

	[TestMethod]
	public void EveryStaffCallbackIsTheCallbackOfItsDescriptorRow()
	{
		var messages = AdvisorStaffCallbacks.ScoreKeys.Select( entry => entry.MessageId ).ToArray();
		Assert.AreEqual( 38, messages.Length );
		Assert.AreEqual( 38, AdvisorStaffCallbacks.Callbacks.Count );
		Assert.AreEqual( messages.Length, messages.Distinct().Count() );
		foreach ( var message in messages )
		{
			var row = AdvisorDescriptorTable.Rows[message];
			Assert.IsTrue( row.Scan, $"message {message} is scored by the scan" );
			Assert.IsTrue( AdvisorStaffCallbacks.Callbacks.ContainsKey( row.Callback ), $"message {message}: callback 0x{row.Callback:X} is ported" );
			Assert.IsTrue( AdvisorScanCallbacks.Implemented.ContainsKey( row.Callback ), $"message {message} is in the scan's callbacks" );
		}
		Assert.IsTrue( AdvisorStaffCallbacks.Callbacks.Keys.All( address => messages.Any( message => AdvisorDescriptorTable.Rows[message].Callback == address ) ), "every callback has a row" );
	}

	[TestMethod]
	public void EveryStaffCallbackScoresZeroWithoutAParkAStaffViewOrItsThresholds()
	{
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );
		var full = new StaffPark { Census = 400, Cells = 900, Rides = 1, StaffHappiness = 100, Tired = 5, Unhappy = 100, Visitors = 5000, Rooms = 2, ShutRooms = 2, StartDays = 3000 };
		foreach ( var role in Enum.GetValues<StaffType>() )
			(full.Counts[role], full.Training[role], full.Limits[role]) = (3, 10, 8);
		var bare = AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Header.Split( "WaitingTimes.DaysForStaff" )[0] ), "bare.sam" );
		foreach ( var message in AdvisorStaffCallbacks.ScoreKeys.Select( entry => entry.MessageId ) )
		{
			var row = AdvisorDescriptorTable.Rows[message];
			Assert.AreEqual( 0, scan.ScoreOf( row, Context( null ), 0 ), $"message {message} without a park" );
			Assert.AreEqual( 0, scan.ScoreOf( row, Context( new PlainPark() ), 0 ), $"message {message} with a park that has no staff view" );
			Assert.AreEqual( 0, scan.ScoreOf( row, Context( full, bare ), 0 ), $"message {message} with a settings file without its thresholds" );
		}
	}

	[TestMethod]
	public void EveryStaffThresholdExistsInTheShippedSettings()
	{
		var balance = ShippedBalance();
		foreach ( var key in AdvisorStaffCallbacks.IntegerKeys )
			Assert.IsTrue( balance.ScanParameters.ContainsKey( key ), $"{key} is read from Advisor.sam" );
		foreach ( var (message, key, fixedScore) in AdvisorStaffCallbacks.ScoreKeys.Where( entry => entry.FixedScore == null ) )
			Assert.IsTrue( balance.Scores.ContainsKey( key ), $"message {message}: {key}" );
		foreach ( var message in AdvisorStaffCallbacks.ScoreKeys.Select( entry => entry.MessageId ) )
			Assert.IsTrue( balance.Groups.ContainsKey( AdvisorDescriptorTable.Rows[message].Group ), $"the group of message {message} has its controls" );
		// every flat score is a key of its message's own callback: the file's keys read are the ones the descriptor's callback reads
		foreach ( var key in AdvisorStaffCallbacks.ScoreKeys.Select( entry => entry.ScoreKey ) )
			Assert.IsTrue( balance.Scores.ContainsKey( key ), key );
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

	// ------------------------------------------------------------------ hiring

	[TestMethod]
	public void GuardsAreWantedWhenTheParkIsBusy()
	{
		var park = new StaffPark { Census = 80 };
		Assert.AreEqual( 40 - 20, BusyPoints( park ) - 20 );
		Assert.AreEqual( 40, Score( 11, park ), "(40 busy points - 20) x 2" );
		park.Census = 41;
		Assert.AreEqual( 0, Score( 11, park ), "20 busy points: nothing above the threshold" );
		park.Census = 43;
		Assert.AreEqual( 2, Score( 11, park ), "21 busy points" );
		park.Census = 40;
		Assert.AreEqual( 0, Score( 11, park ), "exactly AtLeastThisBusy scores 0, which never passes" );
		park.Census = 39;
		Assert.AreEqual( -2, Score( 11, park ), "below it the product is negative (the original does not clamp): no advice, as it never beats 0" );
		park.Census = 1000;
		Assert.AreEqual( (100 - 20) * 2, Score( 11, park ), "the busy points stop at 100" );
		park.HireDays = 9;
		Assert.AreEqual( 0, Score( 11, park ), "the newest hire is too recent (DaysForStaff 10)" );
		park.HireDays = 10;
		Assert.AreEqual( 160, Score( 11, park ), "at DaysForStaff" );
		park.Counts[StaffType.Guard] = 1;
		Assert.AreEqual( 0, Score( 11, park ), "a guard is hired" );
		park.Counts.Clear();
		park.Counts[StaffType.Entertainer] = 4;
		Assert.AreEqual( 160, Score( 11, park ), "only a guard counts" );
		park.Open = false;
		Assert.AreEqual( 0, Score( 11, park ), "the park is closed" );
	}

	private static int BusyPoints( StaffPark park ) => AdvisorStaffCallbacks.BusyPoints( park.Census );

	[TestMethod]
	public void TheBusyPointsAreHalfTheGuestsAtMostAHundred()
	{
		Assert.AreEqual( (0, 0, 49, 50, 99, 100, 100), (AdvisorStaffCallbacks.BusyPoints( 0 ), AdvisorStaffCallbacks.BusyPoints( 1 ), AdvisorStaffCallbacks.BusyPoints( 98 ), AdvisorStaffCallbacks.BusyPoints( 100 ),
			AdvisorStaffCallbacks.BusyPoints( 199 ), AdvisorStaffCallbacks.BusyPoints( 200 ), AdvisorStaffCallbacks.BusyPoints( 5000 )) );
		Assert.AreEqual( 0, AdvisorStaffCallbacks.BusyPoints( -3 ), "below 0 gives 0" );
	}

	[TestMethod]
	public void GuardsAreWantedWhenTheParkHasManyDevelopedCells()
	{
		var park = new StaffPark { Cells = 450 };
		Assert.AreEqual( 1350, Score( 13, park ), "(450 - 0 guards x 250) x 3" );
		park.Counts[StaffType.Guard] = 1;
		Assert.AreEqual( 600, Score( 13, park ), "a guard does not stop it: (450 - 250) x 3" );
		park.Counts[StaffType.Guard] = 2;
		Assert.AreEqual( 0, Score( 13, park ), "(450 - 500) is below 0" );
		park.Cells = 501;
		Assert.AreEqual( 3, Score( 13, park ), "(501 - 500) x 3, just above" );
		park.Cells = 500;
		Assert.AreEqual( 0, Score( 13, park ), "exactly the ideal cells: nothing extra" );
		park.Counts[StaffType.Guard] = 0;
		park.Cells = 199;
		Assert.AreEqual( 0, Score( 13, park ), "below MinCellsForConsideration (200)" );
		park.Cells = 200;
		Assert.AreEqual( 600, Score( 13, park ), "at MinCellsForConsideration: (200 - 0) x 3" );
		park.HireDays = 9;
		Assert.AreEqual( 0, Score( 13, park ), "the newest hire is too recent" );
		park.HireDays = 10;
		park.Open = false;
		Assert.AreEqual( 0, Score( 13, park ), "closed" );
	}

	[TestMethod]
	public void EntertainersAreWantedWhenTheGuestsAreSad()
	{
		var park = new StaffPark { Census = 60, Unhappy = 31 };
		Assert.AreEqual( (30 - 15) * 4, Score( 14, park ), "31 percent below happiness 30: (30 busy points - 15) x 4" );
		Assert.AreEqual( 30, park.LastUnhappyLevel, "the callback asks for the guests below 30" );
		park.Unhappy = 30;
		Assert.AreEqual( 0, Score( 14, park ), "30 percent is not more than 30" );
		park.Unhappy = 31;
		park.Counts[StaffType.Entertainer] = 1;
		Assert.AreEqual( 0, Score( 14, park ), "an entertainer is hired" );
		park.Counts.Clear();
		park.Counts[StaffType.Guard] = 3;
		Assert.AreEqual( 60, Score( 14, park ) );
		park.Open = false;
		Assert.AreEqual( 0, Score( 14, park ), "closed" );
		park.Open = true;
		park.Census = 31;
		Assert.AreEqual( 0, Score( 14, park ), "15 busy points: nothing above AtLeastThisBusy" );
		park.Census = 33;
		Assert.AreEqual( 4, Score( 14, park ), "16 busy points" );
	}

	// ------------------------------------------------------------------ patrol areas

	[TestMethod]
	public void EveryRoleHasItsOwnPatrolAreaLine()
	{
		// message, role, months, at least, don't bother, per percent
		var lines = new (int Message, StaffType Role, int Months, int AtLeast, int Bother, int Scale)[]
		{
			(18, StaffType.Mechanic, 2, 2, 50, 3),
			(20, StaffType.Handyman, 3, 2, 40, 4),
			(23, StaffType.Guard, 4, 3, 30, 5),
			(26, StaffType.Entertainer, 5, 3, 60, 6),
		};
		foreach ( var line in lines )
		{
			var park = new StaffPark { StartDays = line.Months * 30 };
			park.Counts[line.Role] = line.AtLeast + 2;
			var staff = line.AtLeast + 2;
			park.Patrolled[line.Role] = 0;
			Assert.AreEqual( 100 * line.Scale, Score( line.Message, park ), $"message {line.Message}: nobody has an area: 100 x {line.Scale}" );
			// the percent is patrolled x 100 / staff, truncated; equal to DontBother passes, above stops
			var limit = line.Bother * staff / 100;
			park.Patrolled[line.Role] = limit;
			var percent = limit * 100 / staff;
			Assert.AreEqual( percent <= line.Bother ? (100 - percent) * line.Scale : 0, Score( line.Message, park ), $"message {line.Message} at {percent} percent" );
			park.Patrolled[line.Role] = limit + 1;
			var above = (limit + 1) * 100 / staff;
			Assert.AreEqual( above <= line.Bother ? (100 - above) * line.Scale : 0, Score( line.Message, park ), $"message {line.Message} at {above} percent" );
			park.StartDays = line.Months * 30 - 1;
			park.Patrolled[line.Role] = 0;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: a day short of {line.Months} months" );
			park.StartDays = line.Months * 30 + 29;
			Assert.AreEqual( 100 * line.Scale, Score( line.Message, park ), $"message {line.Message}: still {line.Months} whole months" );
			park.Counts[line.Role] = line.AtLeast - 1;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: fewer than AtLeastThisMany" );
			park.Counts[line.Role] = line.AtLeast;
			Assert.AreEqual( 100 * line.Scale, Score( line.Message, park ), $"message {line.Message}: exactly AtLeastThisMany" );
			// another role's staff and patrols do not count
			foreach ( var other in Enum.GetValues<StaffType>().Where( role => role != line.Role ) )
			{
				park.Counts[other] = 9;
				park.Patrolled[other] = 9;
			}
			Assert.AreEqual( 100 * line.Scale, Score( line.Message, park ), $"message {line.Message}: only its own role" );
		}
	}

	[TestMethod]
	public void ThePatrolPercentIsTruncatedAndThePatrolLineIsOnlyForTheTrailingPercents()
	{
		var park = new StaffPark { StartDays = 100 };
		park.Counts[StaffType.Mechanic] = 3;
		park.Patrolled[StaffType.Mechanic] = 1;
		Assert.AreEqual( (100 - 33) * 3, Score( 18, park ), "1 of 3 is 33 percent, truncated" );
		park.Patrolled[StaffType.Mechanic] = 2;
		Assert.AreEqual( 0, Score( 18, park ), "2 of 3 is 66 percent, above the 50 of DontBotherIfPatrolledUp" );
	}

	[TestMethod]
	public void AnAtLeastOfZeroWithNoStaffScoresZeroWhereTheOriginalDividesByZero()
	{
		var text = Header.Replace( "StaffSetMechanicArea.AtLeastThisMany\t2", "StaffSetMechanicArea.AtLeastThisMany\t0" );
		var balance = AdvisorBalance.Parse( Encoding.ASCII.GetBytes( text ), "zero.sam" );
		Assert.AreEqual( 0, Score( 18, new StaffPark { StartDays = 100 }, balance ) );
	}

	// ------------------------------------------------------------------ training

	[TestMethod]
	public void EveryRoleHasItsOwnTrainingAdvice()
	{
		var lines = new (int Message, StaffType Role, int Worse, int Score)[]
		{
			(28, StaffType.Mechanic, 40, 31),
			(29, StaffType.Handyman, 41, 32),
			(30, StaffType.Guard, 42, 33),
			(31, StaffType.Entertainer, 43, 34),
			(32, StaffType.Researcher, 44, 35),
		};
		foreach ( var line in lines )
		{
			var park = new StaffPark();
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: nobody of the role" );
			park.Counts[line.Role] = 2;
			park.Training[line.Role] = line.Worse - 1;
			Assert.AreEqual( line.Score, Score( line.Message, park ), $"message {line.Message}: just below WorseThan" );
			park.Training[line.Role] = line.Worse;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: at WorseThan" );
			park.Training[line.Role] = line.Worse + 1;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: above" );
			park.Training[line.Role] = 0;
			Assert.AreEqual( line.Score, Score( line.Message, park ), $"message {line.Message}: untrained" );
			park.Budgets[line.Role] = 1;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: a budget is set" );
			park.Budgets[line.Role] = 0;
			foreach ( var other in Enum.GetValues<StaffType>().Where( role => role != line.Role ) )
				park.Budgets[other] = 100;
			Assert.AreEqual( line.Score, Score( line.Message, park ), $"message {line.Message}: only its own role's budget counts" );
		}
	}

	[TestMethod]
	public void EveryRoleHasItsOwnTrainedCongratulation()
	{
		var lines = new (int Message, StaffType Role, int Minimum, int Better, int Score)[]
		{
			(146, StaffType.Guard, 2, 60, 91),
			(147, StaffType.Researcher, 1, 55, 92),
			(148, StaffType.Handyman, 3, 50, 93),
			(149, StaffType.Entertainer, 2, 45, 94),
			(150, StaffType.Mechanic, 4, 65, 95),
		};
		foreach ( var line in lines )
		{
			var park = new StaffPark();
			park.Counts[line.Role] = line.Minimum + 1;
			park.Training[line.Role] = line.Better + 1;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: no budget was ever set" );
			park.Budgets[line.Role] = 1;
			Assert.AreEqual( line.Score, Score( line.Message, park ), $"message {line.Message}: just above both" );
			park.Training[line.Role] = line.Better;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: at BetterThan (strictly above)" );
			park.Training[line.Role] = line.Better + 1;
			park.Counts[line.Role] = line.Minimum;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: at the minimum (strictly above)" );
			park.Counts[line.Role] = line.Minimum + 1;
			park.Budgets[line.Role] = 0;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: the budget is back to 0" );
			park.Budgets[line.Role] = -5;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: a negative word is not above 0" );
		}
	}

	[TestMethod]
	public void TheStaffAreCongratulatedWhenTheyAreHappy()
	{
		var park = new StaffPark { StaffHappiness = 70f };
		park.Counts[StaffType.Handyman] = 5;
		Assert.AreEqual( 81, Score( 144, park ), "5 staff, mean happiness 70: equality passes" );
		park.StaffHappiness = 69.99f;
		Assert.AreEqual( 0, Score( 144, park ), "just below HappierThan" );
		park.StaffHappiness = 70.01f;
		Assert.AreEqual( 81, Score( 144, park ) );
		park.Counts[StaffType.Handyman] = 4;
		Assert.AreEqual( 0, Score( 144, park ), "one staff member short of MinimumStaff" );
		park.Counts[StaffType.Researcher] = 1;
		Assert.AreEqual( 81, Score( 144, park ), "every role counts" );
		park.Total = 3;
		Assert.AreEqual( 0, Score( 144, park ), "the staff view's own total decides" );
		park.Open = false;
		park.Total = 5;
		Assert.AreEqual( 81, Score( 144, park ), "no park-open test" );
	}

	// ------------------------------------------------------------------ too many staff

	[TestMethod]
	public void EveryRoleHasItsOwnTooManyStaffLine()
	{
		var lines = new (int Message, StaffType Role, int Fudge, int Score)[]
		{
			(108, StaffType.Handyman, 1, 61),
			(109, StaffType.Entertainer, 2, 62),
			(110, StaffType.Mechanic, 3, 63),
			(111, StaffType.Guard, 4, 64),
			(112, StaffType.Researcher, 5, 65),
		};
		foreach ( var line in lines )
		{
			var park = new StaffPark { Rides = 6 };
			park.Counts[line.Role] = 6 + line.Fudge;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: as many as the rides plus the fudge factor" );
			park.Counts[line.Role] = 6 + line.Fudge + 1;
			Assert.AreEqual( line.Score, Score( line.Message, park ), $"message {line.Message}: one more" );
			park.Rides = 7;
			Assert.AreEqual( 0, Score( line.Message, park ), $"message {line.Message}: a ride more" );
			park.Rides = 0;
			Assert.AreEqual( line.Score, Score( line.Message, park ), $"message {line.Message}: no rides" );
			park.Open = false;
			Assert.AreEqual( line.Score, Score( line.Message, park ), $"message {line.Message}: no park-open test" );
		}
	}

	[TestMethod]
	public void TheLimitLinesNeedTheLimitToBeTheWordAndTheStaffToReachIt()
	{
		// message, V2 message, role, scores, the limit word MoreThan, and the first score
		var lines = new (int First, int Again, StaffType Role, int More, int ScoreFirst, int ScoreAgain)[]
		{
			(114, 115, StaffType.Handyman, 10, 71, 72),
			(116, 117, StaffType.Entertainer, 10, 73, 74),
			(118, 119, StaffType.Mechanic, 10, 75, 76),
			(120, 121, StaffType.Guard, 10, 77, 78),
		};
		foreach ( var line in lines )
		{
			var park = new StaffPark();
			park.Counts[line.Role] = 9;
			Assert.AreEqual( (0, 0), (Score( line.First, park ), Score( line.Again, park )), $"{line.First}: nine of ten" );
			park.Counts[line.Role] = 10;
			Assert.AreEqual( (line.ScoreFirst, 0), (Score( line.First, park ), Score( line.Again, park )), $"{line.First}: the limit of ten is reached" );
			park.Counts[line.Role] = 11;
			Assert.AreEqual( (line.ScoreFirst, 0), (Score( line.First, park ), Score( line.Again, park )), $"{line.First}: more than the limit still says the first line" );
			// a park that takes 8: the first line is for the limit the file names, the second for any other
			park.Limits[line.Role] = 8;
			park.Counts[line.Role] = 7;
			Assert.AreEqual( (0, 0), (Score( line.First, park ), Score( line.Again, park )), $"{line.Again}: seven of eight" );
			park.Counts[line.Role] = 8;
			Assert.AreEqual( (0, line.ScoreAgain), (Score( line.First, park ), Score( line.Again, park )), $"{line.Again}: eight of eight" );
			park.Counts[line.Role] = 9;
			Assert.AreEqual( (0, 0), (Score( line.First, park ), Score( line.Again, park )), $"{line.Again}: the staff must equal the limit" );
			park.Open = false;
			park.Counts[line.Role] = 8;
			Assert.AreEqual( line.ScoreAgain, Score( line.Again, park ), "no park-open test" );
			// another role's limit and staff do not matter
			foreach ( var other in Enum.GetValues<StaffType>().Where( role => role != line.Role ) )
				park.Limits[other] = 3;
			Assert.AreEqual( line.ScoreAgain, Score( line.Again, park ) );
		}
	}

	[TestMethod]
	public void TheResearchersLimitLineKeepsTheOriginalsGuardLimitSlip()
	{
		var park = new StaffPark();
		park.Counts[StaffType.Researcher] = 6;
		// 122 compares the guards' limit (10) with MaxGuards.MoreThan (10) and counts the researchers against MaxResearchers.MoreThan (6)
		Assert.AreEqual( 79, Score( 122, park ), "the guards' limit equals MaxGuards.MoreThan and six researchers reach MaxResearchers.MoreThan" );
		park.Counts[StaffType.Researcher] = 5;
		Assert.AreEqual( 0, Score( 122, park ), "five researchers" );
		park.Counts[StaffType.Researcher] = 6;
		park.Limits[StaffType.Researcher] = 6;
		Assert.AreEqual( 79, Score( 122, park ), "the researchers' own limit is not looked at" );
		park.Limits[StaffType.Guard] = 9;
		Assert.AreEqual( 0, Score( 122, park ), "the guards' limit is what decides" );
		// 123 is the researchers' V2 line: its own limit word against MaxResearchers.MoreThan (6)
		park.Limits[StaffType.Researcher] = 6;
		Assert.AreEqual( 0, Score( 123, park ), "the limit is the word: no repeat line" );
		park.Limits[StaffType.Researcher] = 5;
		park.Counts[StaffType.Researcher] = 5;
		Assert.AreEqual( 80, Score( 123, park ), "a limit of five reached exactly" );
		park.Counts[StaffType.Researcher] = 6;
		Assert.AreEqual( 0, Score( 123, park ) );
	}

	// ------------------------------------------------------------------ the staff room and the general lines

	[TestMethod]
	public void TiredStaffNeedAStaffRoomAfterThreeMonths()
	{
		var park = new StaffPark { StartDays = 90, Total = 10, Tired = 5 };
		Assert.AreEqual( 101, Score( 169, park ), "50 percent tired is above 40" );
		park.Tired = 4;
		Assert.AreEqual( 0, Score( 169, park ), "40 percent is not above 40" );
		park.Total = 9;
		Assert.AreEqual( 101, Score( 169, park ), "4 of 9 is 44 percent" );
		park.StartDays = 89;
		Assert.AreEqual( 0, Score( 169, park ), "the park is not three months old" );
		park.StartDays = 90;
		park.Total = 0;
		park.Tired = 0;
		Assert.AreEqual( 0, Score( 169, park ), "no staff" );
		park.Open = false;
		park.Total = 10;
		park.Tired = 5;
		Assert.AreEqual( 101, Score( 169, park ), "no park-open test" );
	}

	[TestMethod]
	public void AnEmptyParkWithVisitorsNeedsStaff()
	{
		var park = new StaffPark { Visitors = 50 };
		Assert.AreEqual( 102, Score( 170, park ), "at MinVisitorsEver" );
		park.Visitors = 49;
		Assert.AreEqual( 0, Score( 170, park ), "one visitor short" );
		park.Visitors = 5000;
		park.Counts[StaffType.Researcher] = 1;
		Assert.AreEqual( 0, Score( 170, park ), "a staff member of any role" );
		park.Counts.Clear();
		park.Open = false;
		Assert.AreEqual( 0, Score( 170, park ), "the park is closed" );
	}

	[TestMethod]
	public void StaffWithoutPatrolsAreToldToSetThem()
	{
		var park = new StaffPark { Total = 5 };
		Assert.AreEqual( 103, Score( 171, park ), "5 staff above 4, nobody patrols an area" );
		park.Total = 4;
		Assert.AreEqual( 0, Score( 171, park ), "4 staff are not above MinStaffForMessage" );
		park.Total = 5;
		park.PatrolledAll = 1;
		Assert.AreEqual( 0, Score( 171, park ), "one has an area" );
		park.PatrolledAll = 0;
		park.Patrolled[StaffType.Guard] = 1;
		Assert.AreEqual( 103, Score( 171, park ), "the count is over every role (the argument 0), not the roles' own" );
		park.Open = false;
		Assert.AreEqual( 103, Score( 171, park ), "no park-open test" );
	}

	[TestMethod]
	public void StaffWhoWereNeverTrainedAreToldToTrainThem()
	{
		var park = new StaffPark { Total = 4 };
		Assert.AreEqual( 104, Score( 172, park ), "4 staff above 3, never trained" );
		park.Total = 3;
		Assert.AreEqual( 0, Score( 172, park ), "3 staff are not above MinStaffForMessage" );
		park.Total = 4;
		park.Trained = true;
		Assert.AreEqual( 0, Score( 172, park ), "a budget was set once" );
		park.Trained = false;
		park.Budgets[StaffType.Mechanic] = 50;
		Assert.AreEqual( 104, Score( 172, park ), "the flag decides, not the budgets" );
	}

	[TestMethod]
	public void ClosedStaffRoomsAreMentionedAboveThePercent()
	{
		var park = new StaffPark { Rooms = 10, ShutRooms = 4 };
		Assert.AreEqual( 105, Score( 263, park ), "40 percent closed is above 30" );
		park.ShutRooms = 3;
		Assert.AreEqual( 0, Score( 263, park ), "30 percent is not above 30" );
		park.Rooms = 3;
		park.ShutRooms = 1;
		Assert.AreEqual( 105, Score( 263, park ), "1 of 3 is 33 percent" );
		park.Rooms = 0;
		park.ShutRooms = 0;
		Assert.AreEqual( 0, Score( 263, park ), "no staff room" );
		park.Open = false;
		park.Rooms = 2;
		park.ShutRooms = 2;
		Assert.AreEqual( 105, Score( 263, park ), "no park-open test" );
	}

	[TestMethod]
	public void TheCoverageAndPrankRowsScoreZeroForAParkWithoutTheInputsView()
	{
		var park = new StaffPark { Census = 400, Cells = 900, Total = 8, Unhappy = 100, StartDays = 3000 };
		foreach ( var role in Enum.GetValues<StaffType>() )
			park.Counts[role] = 4;
		// coverage (19, 21, 27) and the pranks (12, 22, 98) read the chase counters and the patrol coverage through IAdvisorInputsState (AdvisorScanInputsTests): a park state without that view scores 0
		foreach ( var message in new[] { 12, 19, 21, 22, 27, 98 } )
		{
			Assert.IsTrue( AdvisorScanCallbacks.Implemented.ContainsKey( AdvisorDescriptorTable.Rows[message].Callback ), $"message {message} is ported" );
			Assert.AreEqual( 0, Score( message, park ) );
		}
	}

	// ------------------------------------------------------------------ through the real queue

	[TestMethod]
	public void AHappyStaffIsPostedThroughTheScanAndTheQueue()
	{
		var park = new StaffPark { StaffHappiness = 80f, Rides = 10, PatrolledAll = 6, Trained = true };
		park.Counts[StaffType.Mechanic] = 6;
		(park.Patrolled[StaffType.Mechanic], park.Training[StaffType.Mechanic]) = (6, 100);
		var controller = new AdvisorController( Balance() );
		var posted = new List<(int Message, int Score)>();
		for ( var step = 0; step < AdvisorScan.DescriptorCount; step++ )
			if ( controller.ScanOnce( new FakeMoney(), 100, 0, true, park ).Posted is { } advice )
				posted.Add( (advice.MessageId, advice.Score) );
		CollectionAssert.AreEqual( new[] { (144, 81) }, posted, "a happy staff of six: the one congratulation; nothing else is true of this park" );
	}

	[TestMethod]
	public void TheScanKeepsItsRowOrderAndPacingWithTheStaffRows()
	{
		var park = new StaffPark { Total = 5 };
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );
		var scored = new List<int>();
		for ( var step = 0; step < 200; step++ )
		{
			var result = scan.Step( Context( park ), 0, ( _, _ ) => true );
			scored.Add( result.ScoredRow );
			if ( result.MessageId == 171 )
				Assert.AreEqual( 103, result.Score );
		}
		// one scanned row per step, in table order, skipping the rows game code posts
		var order = AdvisorDescriptorTable.Rows.Where( row => row.Scan ).Select( row => row.MessageId ).ToArray();
		for ( var step = 0; step < scored.Count; step++ )
			Assert.AreEqual( order[step % order.Length], scored[step], $"step {step}" );
	}

	// ------------------------------------------------------------------ the statistics over a real park

	private static ParkEconomy Park( bool staffRoom = false )
	{
		if ( !staffRoom )
			return EconomyTestData.Park( initialCash: 500000 );
		var room = EconomyTestData.Catalog().Objects.First( info => info.InfoId == 1402 ) with { InfoId = 1450, Name = "Staff Room", ProvidesRelief = false, ChillsYouOut = true, IsChoosable = false };
		return new ParkEconomy( EconomyTestData.Settings( 500000 ), new EconomyObjectCatalog( EconomyTestData.Catalog().Objects.Append( room ) ), ParkGameMode.FullSimulation, 7 );
	}

	[TestMethod]
	public void TheEconomyStateCountsTheStaffAndTheirPatrolAreas()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		Assert.AreEqual( (0, 0, 0), (state.StaffTotal, state.StaffWithPatrolArea( null ), state.StaffWithPatrolArea( StaffType.Guard )) );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		EconomyTestData.HireBest( park, StaffType.Handyman );
		Assert.AreEqual( 3, state.StaffTotal );
		Assert.AreEqual( 0, state.StaffWithPatrolArea( null ), "everyone patrols the whole map" );
		handyman.SetPatrolArea( (2, 2), (5, 5) );
		Assert.AreEqual( (1, 1, 0), (state.StaffWithPatrolArea( null ), state.StaffWithPatrolArea( StaffType.Handyman ), state.StaffWithPatrolArea( StaffType.Mechanic )) );
		mechanic.SetPatrolArea( (0, 0), (ParkLitter.MapSize - 1, ParkLitter.MapSize - 1) );
		Assert.AreEqual( 1, state.StaffWithPatrolArea( null ), "a drawn area that is the whole map is the cleared area (corner words 1 and 0x4000)" );
		mechanic.SetPatrolArea( (1, 1), (9, 9) );
		Assert.AreEqual( 2, state.StaffWithPatrolArea( null ) );
		handyman.ClearPatrolArea();
		Assert.AreEqual( (1, 0, 1), (state.StaffWithPatrolArea( null ), state.StaffWithPatrolArea( StaffType.Handyman ), state.StaffWithPatrolArea( StaffType.Mechanic )) );
	}

	[TestMethod]
	public void TheEconomyStateAveragesTheGradesAndTheHappinessOfTheStaff()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		Assert.AreEqual( 0, state.AverageTraining( StaffType.Guard ), "no guard" );
		Assert.AreEqual( 0f, state.AverageStaffHappiness, "no staff: the constant 0.0" );
		var first = EconomyTestData.HireBest( park, StaffType.Guard );
		var second = EconomyTestData.HireBest( park, StaffType.Guard );
		var other = EconomyTestData.HireBest( park, StaffType.Researcher );
		first.Grade = 2;
		second.Grade = 3;
		other.Grade = 5;
		Assert.AreEqual( 50, state.AverageTraining( StaffType.Guard ), "(2 + 3) x 100 / (2 x 5): 20 per grade, as an integer mean" );
		first.Grade = 1;
		second.Grade = 2;
		Assert.AreEqual( 30, state.AverageTraining( StaffType.Guard ), "3 x 100 / 10: the grades are added first and the quotient truncated" );
		Assert.AreEqual( 100, state.AverageTraining( StaffType.Researcher ) );
		first.TrainingPoints = 99;
		Assert.AreEqual( 30, state.AverageTraining( StaffType.Guard ), "the training points are not read" );
		(first.Happiness, second.Happiness, other.Happiness) = (80.9f, 70f, 51f);
		// (80 + 70 + 51) / 3 over the truncated bytes, as a single
		Assert.AreEqual( 201f / 3, state.AverageStaffHappiness );
	}

	[TestMethod]
	public void TheEconomyStateCountsTiredStaffAndReadsTheLimits()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		var one = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var two = EconomyTestData.HireBest( park, StaffType.Handyman );
		var rest = park.Settings.RestLevel;
		one.Energy = rest;
		two.Energy = rest - 0.5f;
		Assert.AreEqual( 1, state.TiredStaff, $"the byte of {rest - 0.5f} is {rest - 1}: below the rest level; the rest level itself is not" );
		one.Energy = rest - 1;
		Assert.AreEqual( 2, state.TiredStaff );
		(one.Energy, two.Energy) = (100f, 100f);
		Assert.AreEqual( 0, state.TiredStaff );
		foreach ( var role in Enum.GetValues<StaffType>() )
			Assert.AreEqual( park.Settings[role].MaximumInPark, state.StaffLimit( role ) );
	}

	[TestMethod]
	public void TheTrainingFlagIsSetByABudgetAboveZeroAndKeptByTheSave()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		Assert.IsFalse( state.HaveEverTrained );
		Assert.AreEqual( 0, state.TrainingBudget( StaffType.Guard ) );
		park.SetTrainingBudget( StaffType.Guard, 0 );
		Assert.IsFalse( state.HaveEverTrained, "a budget of 0 sets nothing" );
		park.SetTrainingBudget( StaffType.Guard, -40 );
		Assert.IsFalse( state.HaveEverTrained, "a negative budget is stored as 0 and sets nothing" );
		Assert.AreEqual( 0, state.TrainingBudget( StaffType.Guard ) );
		park.SetTrainingBudget( StaffType.Guard, 750 );
		Assert.IsTrue( state.HaveEverTrained );
		Assert.AreEqual( 750, state.TrainingBudget( StaffType.Guard ) );
		Assert.AreEqual( 0, state.TrainingBudget( StaffType.Mechanic ), "one word per role" );
		park.SetTrainingBudget( StaffType.Guard, 0 );
		Assert.IsTrue( state.HaveEverTrained, "the byte stays set" );
		Assert.AreEqual( 0, state.TrainingBudget( StaffType.Guard ) );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Capture( park ), EconomyTestData.Settings( 500000 ), EconomyTestData.Catalog() );
		Assert.IsTrue( new EconomyParkState( restored ).HaveEverTrained, "the park save keeps it even with every budget back at 0" );
		var untrained = ParkSaveFile.Restore( ParkSaveFile.Capture( Park() ), EconomyTestData.Settings( 500000 ), EconomyTestData.Catalog() );
		Assert.IsFalse( new EconomyParkState( untrained ).HaveEverTrained );
		Assert.IsFalse( ParkSaveFile.Serialize( Park() ).Contains( "HaveEverTrained", StringComparison.Ordinal ), "a park that never trained writes nothing new" );
		// a save written before the flag has none and derives it from the budgets
		park.SetTrainingBudget( StaffType.Guard, 200 );
		var withBudget = System.Text.RegularExpressions.Regex.Replace( ParkSaveFile.Serialize( park ), ",\\s*\"HaveEverTrained\": true", "" );
		Assert.IsFalse( withBudget.Contains( "HaveEverTrained", StringComparison.Ordinal ) );
		var legacy = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( withBudget ) ), EconomyTestData.Settings( 500000 ), EconomyTestData.Catalog() );
		Assert.IsTrue( new EconomyParkState( legacy ).HaveEverTrained, "a budget above 0 means training was set" );
		park.SetTrainingBudget( StaffType.Guard, 0 );
		var spent = System.Text.RegularExpressions.Regex.Replace( ParkSaveFile.Serialize( park ), ",\\s*\"HaveEverTrained\": true", "" );
		var legacyZero = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( spent ) ), EconomyTestData.Settings( 500000 ), EconomyTestData.Catalog() );
		Assert.IsFalse( new EconomyParkState( legacyZero ).HaveEverTrained, "no budget and no flag in the file: untrained" );
	}

	[TestMethod]
	public void TheEconomyStateCountsVisitorsAndTheStaffRooms()
	{
		var park = Park( staffRoom: true );
		var state = new EconomyParkState( park );
		Assert.AreEqual( (0, 0, 0), (state.VisitorsEver, state.StaffRooms, state.ClosedStaffRooms) );
		var first = park.RegisterExisting( 1450 );
		var second = park.RegisterExisting( 1450 );
		park.RegisterExisting( 1402 );
		park.RegisterExisting( 1100 );
		Assert.AreEqual( 2, state.StaffRooms, "only the objects with the rest-area flag" );
		// the shipped staff rooms are not choosable (Features.sam Info.IsChoosable 0), so their records hold state 3 (0x4DB4FB), which is no closed one (0x4E0440 reads the open word only)
		Assert.AreEqual( RideObjectStates.NotAnAttraction, ParkEconomy.ObjectState( first ) );
		Assert.AreEqual( 0, state.ClosedStaffRooms, "open staff rooms are no closed ones (issue #829: the advisor said message 263 with the placeholder sample for every staff room)" );
		first.IsOpen = false;
		Assert.AreEqual( 1, state.ClosedStaffRooms );
		second.IsOpen = false;
		Assert.AreEqual( 2, state.ClosedStaffRooms );
		Assert.AreEqual( 1, state.Toilets, "the toilet is no staff room and the staff room no toilet" );
		park.Counters.Add( ParkCounters.Admissions, 12 );
		Assert.AreEqual( 12, state.VisitorsEver );
	}

	[TestMethod]
	public void TheEconomyStateIsReadOnlyForTheStaffStatistics()
	{
		var park = Park( staffRoom: true );
		EconomyTestData.HireBest( park, StaffType.Guard ).SetPatrolArea( (3, 3), (8, 8) );
		park.RegisterExisting( 1450 );
		park.SetTrainingBudget( StaffType.Guard, 100 );
		var before = ParkSaveFile.Serialize( park );
		var state = new EconomyParkState( park );
		_ = (state.StaffTotal, state.StaffWithPatrolArea( null ), state.AverageTraining( StaffType.Guard ), state.AverageStaffHappiness, state.TiredStaff, state.TrainingBudget( StaffType.Guard ),
			state.HaveEverTrained, state.VisitorsEver, state.GuestPercentBelowHappiness( 30 ), state.StaffRooms, state.ClosedStaffRooms, state.StaffLimit( StaffType.Guard ));
		Assert.AreEqual( before, ParkSaveFile.Serialize( park ), "reading the statistics wrote nothing" );
	}

	[TestMethod]
	public void ARealParkScoresTheStaffLinesThroughTheEconomyState()
	{
		var park = Park();
		var state = new EconomyParkState( park );
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );
		var context = new AdvisorScanContext( Balance(), new EconomyMoneyState( park ), state );
		var guards = new[] { EconomyTestData.HireBest( park, StaffType.Guard ), EconomyTestData.HireBest( park, StaffType.Guard ), EconomyTestData.HireBest( park, StaffType.Guard ) };
		park.OpenPark();
		// four staff are not above MinStaffForMessage 4 for the patrol line; a fourth and a fifth make it
		Assert.AreEqual( 0, scan.ScoreOf( AdvisorDescriptorTable.Rows[171], context, 0 ) );
		EconomyTestData.HireBest( park, StaffType.Handyman );
		EconomyTestData.HireBest( park, StaffType.Handyman );
		Assert.AreEqual( 103, scan.ScoreOf( AdvisorDescriptorTable.Rows[171], context, 0 ), "5 staff above 4, nobody has an area" );
		guards[0].SetPatrolArea( (1, 1), (4, 4) );
		Assert.AreEqual( 0, scan.ScoreOf( AdvisorDescriptorTable.Rows[171], context, 0 ), "a patrol area is drawn" );
		Assert.AreEqual( 104, scan.ScoreOf( AdvisorDescriptorTable.Rows[172], context, 0 ), "5 staff above 3, no training budget was ever set" );
		park.SetTrainingBudget( StaffType.Guard, 500 );
		Assert.AreEqual( 0, scan.ScoreOf( AdvisorDescriptorTable.Rows[172], context, 0 ) );
		Assert.AreEqual( 0, scan.ScoreOf( AdvisorDescriptorTable.Rows[30], context, 0 ), "the guards have a budget now" );
		Assert.AreEqual( 32, scan.ScoreOf( AdvisorDescriptorTable.Rows[29], context, 0 ), "the handymen have none and their average training is 0, below 41" );
	}

	// ------------------------------------------------------------------ the guests' share

	[TestMethod]
	public void TheGuestBridgeCountsTheShareOfTheCensusBelowAHappinessLevel()
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		var simulation = new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false };
		var economy = EconomyTestData.Park();
		var bridge = new GuestEconomyBridge( () => economy, simulation );
		Assert.AreEqual( 0, bridge.CensusPercentBelowHappiness( 30 ), "no guests" );
		var guests = Enumerable.Range( 0, 4 ).Select( _ => simulation.SpawnInPark( 6, 5 ) ).ToArray();
		(guests[0].Happiness, guests[1].Happiness, guests[2].Happiness, guests[3].Happiness) = (10f, 29.9f, 30f, 90f);
		Assert.AreEqual( 50, bridge.CensusPercentBelowHappiness( 30 ), "the bytes 10 and 29 are below 30, 30 is not: 2 of 4" );
		Assert.AreEqual( 25, bridge.CensusPercentBelowHappiness( 11 ), "only the byte 10 is below 11" );
		Assert.AreEqual( 0, bridge.CensusPercentBelowHappiness( 10 ), "a byte equal to the level is not below it" );
		Assert.AreEqual( 100, bridge.CensusPercentBelowHappiness( 91 ) );
		guests[0].State = GuestState.Gone;
		Assert.AreEqual( 33, bridge.CensusPercentBelowHappiness( 30 ), "a guest who has left is no census guest: 1 of 3, truncated" );
		Assert.AreEqual( 0, NoGuestStatistics.Instance is IParkGuestStatistics none ? none.CensusPercentBelowHappiness( 30 ) : -1, "statistics without guests answer 0" );
	}
}
