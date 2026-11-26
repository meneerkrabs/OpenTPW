using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenTPW.Mods;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>A park for the award checker with settable cells, staff and attractions.</summary>
internal sealed class TestAwardPark : IThemeParkIncAwardPark
{
	public byte[,] Types { get; } = new byte[128, 128];
	public bool[,] Litter { get; } = new bool[128, 128];
	public bool[,] Tracks { get; } = new bool[128, 128];
	public short[,] Securities { get; } = new short[128, 128];
	public List<ThemeParkIncAwardStaff> StaffList { get; } = new();
	public List<ThemeParkIncAwardAttraction> AttractionList { get; } = new();

	public byte CellType( int x, int y ) => Types[x, y];
	public bool HasLitter( int x, int y ) => Litter[x, y];
	public bool HasTrackRecord( int x, int y ) => Tracks[x, y];
	public short Security( int x, int y ) => Securities[x, y];
	public IReadOnlyList<ThemeParkIncAwardStaff> Staff => StaffList;
	public IReadOnlyList<ThemeParkIncAwardAttraction> Attractions => AttractionList;

	/// <summary>Sets the type of the first <paramref name="count"/> cells in row order from <paramref name="start"/>.</summary>
	public TestAwardPark Fill( byte type, int count, int start = 0 )
	{
		for ( var index = start; index < start + count; index++ )
			Types[index % 128, index / 128] = type;
		return this;
	}

	public static ThemeParkIncAwardStaff Member( ThemeParkIncStaffRole role, int ability = 0 ) => new( role, ability, (0, 0, 127, 127) );

	public static ThemeParkIncAwardAttraction Ride( int trackType, int run, int sections, bool open = true ) => new( 0, trackType, open, 1, 1, run, sections );

	public static ThemeParkIncAwardAttraction Feature( int width, int height ) => new( 3, 0, true, width, height, 0, 0 );
}

/// <summary>Theme Park Inc's park awards and shares game (docs/reverse/TPI-AWARDS-SHARES.md).</summary>
[TestClass]
public class ThemeParkIncAwardsSharesTests
{
	/// <summary>The retail thresholds (<c>levels/Standard.sam</c> lines 1045 to 1054).</summary>
	private static readonly ThemeParkIncAwardSettings Retail = new( 110, 100, 90, 1000, 25, 15, 50, 10, 5, 50 );

	/// <summary>The retail share words (<c>ShareInfo</c>).</summary>
	private static readonly ThemeParkIncShareSettings Shares = new( 30000, 1000, 100 );

	private static TestCareerFiles Files() => new TestCareerFiles()
		.With( "/missions/Miss00.sam", "Info.Number 0\n" )
		.With( "/levels/arabian/global.sam", "ParkInfo.AvailableFromStart 0\n" )
		.With( "/levels/science/global.sam", "ParkInfo.AvailableFromStart 1\n" )
		.With( "/levels/water/global.sam", "ParkInfo.AvailableFromStart 0\n" );

	private static ThemeParkIncCareer Career( string zone = "science" )
	{
		var career = new ThemeParkIncCareer( Files(), TestCareerFiles.ShippedFiringLines );
		career.ChangeTheme( zone );
		return career;
	}

	/// <summary>A park whose hygiene, decoration, security, training and track rides are all below the retail thresholds.</summary>
	private static TestAwardPark Plain() => new TestAwardPark().Fill( 1, 2000 );

	// ---- the metrics, each at its boundary --------------------------------------------------

	[TestMethod]
	public void TheHygieneAwardNeedsNinetyPercentUnlitteredAndAThousandCoveredCells()
	{
		// 100 counted cells (paths, queues and queue ends), 10 of them littered: 90 % clean
		var park = new TestAwardPark().Fill( 1, 40 ).Fill( 3, 30, 40 ).Fill( 9, 30, 70 ).Fill( 4, 969, 100 );
		for ( var index = 0; index < 10; index++ )
			park.Litter[index, 0] = true;
		park.Tracks[0, 100] = true;
		Assert.AreEqual( (10, 1000), (ThemeParkIncParkAwards.LitterPercent( park ), ThemeParkIncParkAwards.CoveredCells( park )), "the queue ends and a track record count as covered too: 30 + 969 + 1" );
		var career = Career();
		Assert.AreEqual( new ThemeParkIncAwardNotice( ThemeParkIncAward.Hygiene, false, 90 ), ThemeParkIncParkAwards.Check( career, park, Retail ) );

		var dirtier = new TestAwardPark().Fill( 1, 100 ).Fill( 4, 1000, 100 );
		for ( var index = 0; index < 11; index++ )
			dirtier.Litter[index, 0] = true;
		Assert.AreEqual( 11, ThemeParkIncParkAwards.LitterPercent( dirtier ) );
		Assert.IsNull( ThemeParkIncParkAwards.Check( Career(), dirtier, Retail ), "89 % clean misses the award" );

		var small = new TestAwardPark().Fill( 1, 100 ).Fill( 4, 999, 100 );
		Assert.IsNull( ThemeParkIncParkAwards.Check( Career(), small, Retail ), "999 covered cells are below HygieneMinParkSize" );
		var litterOnGrass = new TestAwardPark();
		litterOnGrass.Litter[5, 5] = true;
		Assert.AreEqual( 100, ThemeParkIncParkAwards.LitterPercent( litterOnGrass ), "a littered cell counts whatever its type" );
		Assert.AreEqual( 0, ThemeParkIncParkAwards.LitterPercent( new TestAwardPark() ), "no counted cell measures 0" );
	}

	[TestMethod]
	public void TheDecorationAwardIsTheFeaturesFootprintOverTheCoveredCells()
	{
		var park = Plain().Fill( 4, 200, 2000 );
		park.AttractionList.Add( TestAwardPark.Feature( 5, 6 ) );
		park.AttractionList.Add( new ThemeParkIncAwardAttraction( 0, 0, true, 10, 10, 0, 0 ) );
		Assert.AreEqual( 15, ThemeParkIncParkAwards.DecorationPercent( park ), "30 feature cells over 200 covered: only Info.WhichUIType 3 counts" );
		Assert.AreEqual( new ThemeParkIncAwardNotice( ThemeParkIncAward.Decoration, false, 15 ), ThemeParkIncParkAwards.Check( Career(), park, Retail ) );
		var less = Plain().Fill( 4, 201, 2000 );
		less.AttractionList.Add( TestAwardPark.Feature( 5, 6 ) );
		Assert.AreEqual( 14, ThemeParkIncParkAwards.DecorationPercent( less ) );
		Assert.IsNull( ThemeParkIncParkAwards.Check( Career(), less, Retail ) );
		var bare = new TestAwardPark();
		bare.AttractionList.Add( TestAwardPark.Feature( 1, 1 ) );
		Assert.ThrowsException<InvalidOperationException>( () => ThemeParkIncParkAwards.DecorationPercent( bare ), "the original divides by the covered cells" );
		Assert.AreEqual( 0, ThemeParkIncParkAwards.DecorationPercent( new TestAwardPark() ), "no feature measures 0" );
	}

	[TestMethod]
	public void TheSecurityAwardAddsTheGuardsPatrolsToTheCamerasCover()
	{
		var park = new TestAwardPark();
		// 16384 land cells; water, no-terrain and fixed walkway cells are not land
		park.Fill( ParkCellTypes.Water, 4096 ).Fill( ParkCellTypes.NoTerrain, 2048, 4096 ).Fill( ParkCellTypes.FixedWalkway, 2048, 6144 );
		// 8192 land cells remain (rows 64 to 127): a quarter of them under a camera's security word, 2048 cells
		for ( var y = 64; y < 80; y++ )
			for ( var x = 0; x < 128; x++ )
				park.Securities[x, y] = 1;
		park.Securities[0, 0] = 5;
		Assert.AreEqual( 25, ThemeParkIncParkAwards.CameraPercent( park ), "2048 of 8192; the water cell's word does not count" );
		Assert.AreEqual( 0, ThemeParkIncParkAwards.PatrolPercent( park ), "no guard" );
		Assert.AreEqual( new ThemeParkIncAwardNotice( ThemeParkIncAward.Security, false, 25 ), ThemeParkIncParkAwards.Check( Career(), park, Retail ) );

		park.Securities[0, 64] = 0;
		Assert.AreEqual( 24, ThemeParkIncParkAwards.CameraPercent( park ) );
		Assert.IsNull( ThemeParkIncParkAwards.Check( Career(), park, Retail ), "24 % misses the award" );
		park.StaffList.Add( TestAwardPark.Member( ThemeParkIncStaffRole.Guard ) );
		Assert.AreEqual( 0, ThemeParkIncParkAwards.PatrolPercent( park ), "a guard with the whole map (the corner words 1 and 0x4000) marks nothing" );
		park.StaffList.Add( new ThemeParkIncAwardStaff( ThemeParkIncStaffRole.Handyman, 0, (0, 100, 127, 127) ) );
		Assert.AreEqual( 0, ThemeParkIncParkAwards.PatrolPercent( park ), "only guards (kind 7) mark their patrol" );
		park.StaffList.Add( new ThemeParkIncAwardStaff( ThemeParkIncStaffRole.Guard, 0, (0, 64, 127, 65) ) );
		Assert.AreEqual( 3, ThemeParkIncParkAwards.PatrolPercent( park ), "256 of 8192 land cells, inclusive corners" );
		Assert.AreEqual( new ThemeParkIncAwardNotice( ThemeParkIncAward.Security, false, 27 ), ThemeParkIncParkAwards.Check( Career(), park, Retail ) );
	}

	[TestMethod]
	public void TheTrainingAwardNeedsHalfTheStaffTrainedAndTenStaff()
	{
		var park = Plain();
		for ( var index = 0; index < 10; index++ )
			park.StaffList.Add( TestAwardPark.Member( ThemeParkIncStaffRole.Handyman, index < 5 ? 3 : 0 ) );
		Assert.AreEqual( 50, ThemeParkIncParkAwards.TrainedPercent( park ) );
		Assert.AreEqual( new ThemeParkIncAwardNotice( ThemeParkIncAward.Training, false, 50 ), ThemeParkIncParkAwards.Check( Career(), park, Retail ) );
		park.StaffList.RemoveAt( 0 );
		Assert.AreEqual( 44, ThemeParkIncParkAwards.TrainedPercent( park ) );
		Assert.IsNull( ThemeParkIncParkAwards.Check( Career(), park, Retail ) );
		var few = Plain();
		for ( var index = 0; index < 9; index++ )
			few.StaffList.Add( TestAwardPark.Member( ThemeParkIncStaffRole.Mechanic, 1 ) );
		Assert.AreEqual( 100, ThemeParkIncParkAwards.TrainedPercent( few ) );
		Assert.IsNull( ThemeParkIncParkAwards.Check( Career(), few, Retail ), "nine staff are below TrainingMinStaff" );
		Assert.AreEqual( 0, ThemeParkIncParkAwards.TrainedPercent( Plain() ), "no staff measures 0" );
	}

	[TestMethod]
	public void TheKartAwardReadsTheLongestStraightAndTheWaterAwardTheSections()
	{
		var park = Plain();
		park.AttractionList.Add( TestAwardPark.Ride( 1, 4, 90 ) );
		park.AttractionList.Add( TestAwardPark.Ride( 1, 9, 90, open: false ) );
		park.AttractionList.Add( TestAwardPark.Ride( 2, 30, 49 ) );
		park.AttractionList.Add( new ThemeParkIncAwardAttraction( 1, 1, true, 1, 1, 20, 90 ) );
		Assert.AreEqual( (4, 49), (ThemeParkIncParkAwards.LongestTrackRide( park, 1, false ), ThemeParkIncParkAwards.LongestTrackRide( park, 2, true )),
			"a closed ride and a type that is not a ride (Info.WhichUIType 1) are not measured" );
		Assert.IsNull( ThemeParkIncParkAwards.Check( Career(), park, Retail ), "a straight of 4 and 49 sections miss both" );
		park.AttractionList.Add( TestAwardPark.Ride( 1, 5, 10 ) );
		Assert.AreEqual( new ThemeParkIncAwardNotice( ThemeParkIncAward.GokartSpeed, false, 5 ), ThemeParkIncParkAwards.Check( Career(), park, Retail ) );
		var water = Plain();
		water.AttractionList.Add( TestAwardPark.Ride( 2, 1, 50 ) );
		Assert.AreEqual( new ThemeParkIncAwardNotice( ThemeParkIncAward.WaterLength, false, 50 ), ThemeParkIncParkAwards.Check( Career(), water, Retail ) );
	}

	[TestMethod]
	public void TheCoasterAwardsAreNeverDecided()
	{
		// the coaster metrics need the thing bytes +0x3F and +0x40 and the coaster track record, which OpenTPW does not keep ([EXT:tpi-capability-gates])
		var zero = Retail with { CoasterHeight = 0, CoasterSpeed = 0 };
		var park = Plain();
		park.AttractionList.Add( new ThemeParkIncAwardAttraction( 0, 3, true, 4, 4, 0, 0 ) );
		Assert.IsNull( ThemeParkIncParkAwards.Check( Career(), park, zero ) );
	}

	// ---- the check, the storage and the gate -----------------------------------------------

	[TestMethod]
	public void ACheckDecidesOneAwardAndTheNextCheckTheNext()
	{
		var park = new TestAwardPark().Fill( 1, 100 ).Fill( 4, 1000, 100 );
		park.AttractionList.Add( TestAwardPark.Feature( 20, 10 ) );
		var career = Career();
		Assert.AreEqual( ThemeParkIncAward.Hygiene, ThemeParkIncParkAwards.Check( career, park, Retail )!.Value.Award );
		Assert.AreEqual( (1, 0), (career.Awards[2], career.Awards[3]), "the decoration award waits for the next check" );
		Assert.AreEqual( ThemeParkIncAward.Decoration, ThemeParkIncParkAwards.Check( career, park, Retail )!.Value.Award, "a held award changes nothing and the scan goes on" );
		Assert.IsNull( ThemeParkIncParkAwards.Check( career, park, Retail ) );
		Assert.AreEqual( 2, career.AwardsHeld );
	}

	[TestMethod]
	public void AnAwardMovesToTheZoneThatBeatsItsHoldersValue()
	{
		var career = Career( "arabian" );
		Assert.AreEqual( 1, career.StoreAward( 6, 7 ), "the first win anywhere" );
		Assert.AreEqual( (1, (byte)1, 7), (career.Awards[6], career.CurrentZone!.Data.Awards[6].Flag, career.CurrentZone.Data.Awards[6].Value) );
		Assert.AreEqual( 0, career.StoreAward( 6, 99 ), "the holder's own higher value is not stored" );
		Assert.AreEqual( 7, career.CurrentZone.Data.Awards[6].Value );
		career.ChangeTheme( "science" );
		Assert.AreEqual( 0, career.StoreAward( 6, 7 ), "an equal value does not move it" );
		Assert.AreEqual( 2, career.StoreAward( 6, 8 ) );
		Assert.AreEqual( ((byte)0, (byte)1, 8), (career.ZoneRecord( 0 )!.Data.Awards[6].Flag, career.ZoneRecord( 1 )!.Data.Awards[6].Flag, career.ZoneRecord( 1 )!.Data.Awards[6].Value) );
		Assert.AreEqual( 1, career.AwardsHeld, "the career's byte is set once" );
		Assert.AreEqual( new ThemeParkIncAwardNotice( ThemeParkIncAward.GokartSpeed, true, 8 ).AdviceId, 0xC4 );
		Assert.AreEqual( new ThemeParkIncAwardNotice( ThemeParkIncAward.GokartSpeed, false, 8 ).AdviceId, 0xC0 );
	}

	[TestMethod]
	public void TheCheckRunsOnEveryHundredthWorldTick()
	{
		Assert.IsTrue( ThemeParkIncParkAwards.IsCheckTick( 0 ) && ThemeParkIncParkAwards.IsCheckTick( 100 ) && ThemeParkIncParkAwards.IsCheckTick( 4_000_000_000 ) );
		Assert.IsFalse( ThemeParkIncParkAwards.IsCheckTick( 99 ) || ThemeParkIncParkAwards.IsCheckTick( 101 ) );
		Assert.IsTrue( ThemeParkIncParkAwards.IsCheckTick( 4_294_967_396 ), "the tick is the 32-bit word (divl): 2^32 + 100 reads as 100" );

		var economy = ThemeParkIncChallengeTests.Economy();
		economy.AttachThemeParkIncCareer( Career() );
		var park = new TestAwardPark().Fill( 1, 100 ).Fill( 4, 1000, 100 );
		economy.TpiAwardPark = park;
		while ( economy.Turn < 99 )
			economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );
		Assert.AreEqual( 0, economy.TpiAwardNotices.Count, "no check before turn 100" );
		economy.Advance( ParkCalendar.TickOfTurn( 100 ) - economy.Tick );
		Assert.AreEqual( new ThemeParkIncAwardNotice( ThemeParkIncAward.Hygiene, false, 100 ), economy.TpiAwardNotices.Single() );
		economy.Advance( ParkCalendar.TickOfTurn( 199 ) - economy.Tick );
		Assert.AreEqual( 1, economy.TpiAwardNotices.Count );
	}

	[TestMethod]
	public void TheThresholdsComeFromStandardSam()
	{
		var standard = new SamSettings( new[] { SamDocument.Parse( "ParkAward.CoasterHeight 110\nParkAward.Hygiene 90\nParkAward.HygieneMinParkSize 1000\nParkAward.Security 25\n"
			+ "ParkAward.Decoration 15\nParkAward.Training 50\nParkAward.TrainingMinStaff 10\nParkAward.GokartSpeed 5\nParkAward.WaterLength 50\nShareInfo.CompanySharesIssued 30000\n"
			+ "ShareInfo.InitialSharesOwned 1000\nShareInfo.InitialSharePrice 100\n", "Standard.sam" ) } );
		Assert.AreEqual( Retail with { CoasterSpeed = 0 }, ThemeParkIncAwardSettings.Read( standard ), "a key the files lack reads 0" );
		Assert.AreEqual( Shares, ThemeParkIncShareSettings.Read( standard ) );
	}

	/// <summary>
	/// <c>OPENTPW_TPI_PATH</c>: the Standard object bound from the shipped files holds the thresholds at the addresses the checker reads (<c>0xA028BC</c>..<c>0xA028E0</c>) and the share words at
	/// <c>0xA02168</c>..<c>0xA02170</c>, as the keyed reader gives them; the thirteen challenge share prizes sum to 14300, which with the 1000 shares of the start-up is exactly 51 % of 30000.
	/// </summary>
	[TestMethod]
	public void TheShippedWordsAreTheKeyedThresholdsAndSharePrizesReachExactlyFiftyOnePercent()
	{
		var files = TestCareerFiles.Shipped();
		var standard = ThemeParkIncStandard.Load( files.Read, "science" )!;
		int Word( uint address ) => standard[(int)(address - ThemeParkIncStandard.ObjectAddress)];
		var bound = new ThemeParkIncAwardSettings( Word( 0xA028BC ), Word( 0xA028C0 ), Word( 0xA028C4 ), Word( 0xA028C8 ), Word( 0xA028CC ), Word( 0xA028D0 ), Word( 0xA028D4 ), Word( 0xA028D8 ),
			Word( 0xA028DC ), Word( 0xA028E0 ) );
		var keyed = new SamSettings( new[] { SamDocument.Parse( Encoding.Latin1.GetString( files.Read( "/levels/Standard.sam" )! ), "Standard.sam" ) } );
		Assert.AreEqual( Retail, bound );
		Assert.AreEqual( bound, ThemeParkIncAwardSettings.Read( keyed ) );
		Assert.AreEqual( Shares, new ThemeParkIncShareSettings( Word( 0xA02168 ), Word( 0xA0216C ), Word( 0xA02170 ) ) );
		Assert.AreEqual( Shares, ThemeParkIncShareSettings.Read( keyed ) );
		var challenges = SamDocument.Parse( Encoding.Latin1.GetString( files.Read( "/Challenges.sam" )! ), "Challenges.sam" );
		var prizes = new SamSettings( new[] { challenges } );
		var sharePrizes = Enumerable.Range( 0, ThemeParkIncChallengeSettings.RowCount ).Select( row => prizes.GetInt( $"Challenges[{row}].SharePrize", 0, optional: true ) ).Where( prize => prize > 0 ).ToArray();
		Assert.AreEqual( (13, 14300), (sharePrizes.Length, sharePrizes.Sum()) );
		var shares = new ThemeParkIncSharesGame();
		shares.Initialize( Shares );
		foreach ( var prize in sharePrizes )
			shares.AddShares( prize, Shares );
		Assert.AreEqual( (15300, 51), (shares.Owned, shares.PercentOwned) );
	}

	// ---- the shares game -------------------------------------------------------------------

	[TestMethod]
	public void TheStartUpGivesTheInitialHoldingAndPriceAndThePercentageIsWholePercent()
	{
		var shares = new ThemeParkIncSharesGame();
		Assert.AreEqual( (-1, -1, -1, -1), (shares.Owned, shares.PercentOwned, shares.Price, shares.HoldingValue), "the reset" );
		shares.Initialize( Shares );
		Assert.AreEqual( (1000, 3, 100, 100000), (shares.Owned, shares.PercentOwned, shares.Price, shares.HoldingValue) );
		shares.AddShares( 14299, Shares );
		Assert.AreEqual( (15299, 50), (shares.Owned, shares.PercentOwned) );
		shares.AddShares( 1, Shares );
		Assert.AreEqual( (15300, 51), (shares.Owned, shares.PercentOwned), "1000 + the thirteen share prizes (14300) are exactly 51 %" );
		shares.AddShares( 0, Shares );
		shares.AddShares( -5, Shares );
		Assert.AreEqual( 15300, shares.Owned, "a prize of 0 or less adds nothing" );
		shares.AddShares( 30000, Shares );
		Assert.AreEqual( (45300, 151), (shares.Owned, shares.PercentOwned), "the holding is not capped" );
		var kept = new ThemeParkIncSharesGame();
		kept.Load( shares.Capture() );
		kept.Initialize( Shares with { InitialSharesOwned = 7, InitialSharePrice = 9 } );
		Assert.AreEqual( (45300, 151, 100), (kept.Owned, kept.PercentOwned, kept.Price), "a start-up keeps what is set" );
		Assert.AreEqual( "Theme Park Inc.", kept.CompanyName( "Theme Park Inc." ), "an empty name reads as the language's" );
	}

	[TestMethod]
	public void ThePriceMovesByAWinALossAndTheAnnualReport()
	{
		var shares = new ThemeParkIncSharesGame();
		shares.Initialize( Shares );
		shares.MovePrice( 4 );
		Assert.AreEqual( 104, shares.Price, "a challenge won: +4 %" );
		shares.MovePrice( 4 );
		Assert.AreEqual( 108, shares.Price, "104 × 4 / 100 = 4 (toward zero)" );
		shares.MovePrice( -2 );
		Assert.AreEqual( 106, shares.Price, "a first loss: -2 % (108 × -2 / 100 = -2)" );
		shares.MovePrice( 0 );
		Assert.AreEqual( 106, shares.Price );
		var falling = new ThemeParkIncSharesGame();
		falling.Initialize( Shares with { InitialSharePrice = 1 } );
		falling.MovePrice( -100 );
		Assert.AreEqual( 1, falling.Price, "a price that would reach 0 is not stored" );

		Assert.AreEqual( 3, ThemeParkIncSharesGame.AnnualScore( 5, 1, 2, 7, 1 ), "five rises are clamped to 3" );
		Assert.AreEqual( -3, ThemeParkIncSharesGame.AnnualScore( -1, -1, -0.5, -1, 0 ) );
		Assert.AreEqual( 1, ThemeParkIncSharesGame.AnnualScore( 1, 0, -1, 0, 1 ) );
		Assert.AreEqual( -1, ThemeParkIncSharesGame.AnnualScore( double.NaN, 0, 0, 0, 0 ), "an unordered change counts as a fall (fcomp's C0)" );
		var report = new ThemeParkIncSharesGame();
		report.Initialize( Shares );
		report.ApplyAnnualScore( 3 );
		Assert.AreEqual( 103, report.Price );
		report.ApplyAnnualScore( -3 );
		Assert.AreEqual( 100, report.Price, "103 × -3 / 100 = -3" );
	}

	private const string ShareStandard =
		"BankAccountInfo.InitialCash\t50000\nBankAccountInfo.InitialAdmissionFee\t25\n"
		+ "LoanInfo[0].LoanAmount\t10000\nLoanInfo[0].APRInPercent\t20\nLoanInfo[0].RepaymentPeriodInMonths\t36\nLoanInfo[0].Lendername\t0\n"
		+ "Costs.QueueCell 25\nCosts.PathCell 10\nCosts.KartTrackCell 40\nCosts.WaterTrackCell 50\nCosts.MapCell 75\n"
		+ "PeepInfo.ExcitementToCostDivisor 6\nPeepInfo.MinimumEntryFee 20\nPeepInfo.CheapPriceMultiplier 0.75\nPeepInfo.AveragePriceMultiplier 1.25\nPeepInfo.ExpensivePriceMultiplier 2.0\n"
		+ "Challenges.DaysBetweenOffers 20\nChallenges.MissionNumberForFirstChallenge 0\nChallengesInThisLevel[0].ChallengeType 2\n"
		+ "ShareInfo.CompanySharesIssued 30000\nShareInfo.InitialSharesOwned 1000\nShareInfo.InitialSharePrice 100\n";

	private const string ShareChallenges =
		"Challenges[2].Type 2\nChallenges[2].TargetTime 60\nChallenges[2].TargetVal 3\nChallenges[2].CashPrize 5000\nChallenges[2].GTPrize 2\nChallenges[2].ScriptIndex 2\n"
		+ "Challenges[2].SharePrize 600\nChallenges[2].SharePriceEffectWon 4\nChallenges[2].SharePriceEffectLost 2\nChallenges[2].Sender 2\n";

	private static readonly EconomyObjectInfo HotDogs = new( 1207, "Hot dogs", ParkObjectKind.Shop, ResearchCategory.Shop, 0,
		new[] { new UpgradeLevelInfo( 0, 500, 0, 0, 0, new[] { 50, 30, 20, 10 } ) }, 10, 2, null, 2, 1, 0, 0, 0, 0, "/x" );

	private static ParkEconomy ShareEconomy()
	{
		var settings = BalanceSettings.ForThemeParkInc( new SamSettings( new[] { SamDocument.Parse( ShareStandard, "Standard.sam" ) } ), "science",
			new SamSettings( new[] { SamDocument.Parse( ShareChallenges, "Challenges.sam" ) } ) );
		return new ParkEconomy( settings, new EconomyObjectCatalog( new[] { HotDogs } ), ParkGameMode.FullSimulation, 7, EconomyRules.ThemeParkIncMoney );
	}

	private static void NextTurn( ParkEconomy economy ) => economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );

	[TestMethod]
	public void AChallengeWinPaysItsSharePrizeAndRaisesThePriceAndAFirstLossLowersIt()
	{
		var economy = ShareEconomy();
		var shares = economy.TpiCareer!.Shares;
		Assert.AreEqual( (1000, 3, 100), (shares.Owned, shares.PercentOwned, shares.Price), "the park's build runs the start-up" );
		economy.OpenPark();
		NextTurn( economy );
		Assert.AreEqual( 0, economy.DebugOfferThemeParkIncChallenge( 0 ) );
		economy.AcceptThemeParkIncChallenge( 0 );
		economy.ClosePark();
		NextTurn( economy );
		Assert.AreEqual( (1000, 98), (shares.Owned, shares.Price), "the first loss: SharePriceEffectLost 2 % off, no shares" );
		economy.OpenPark();
		economy.AcceptThemeParkIncChallenge( 0 );
		economy.ClosePark();
		NextTurn( economy );
		Assert.AreEqual( 98, shares.Price, "a second loss of the same challenge moves nothing" );
		economy.OpenPark();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1207, out var shop ) );
		economy.AcceptThemeParkIncChallenge( 0 );
		for ( var sale = 0; sale < 3; sale++ )
			economy.SettleVisit( shop.Id, 10, null );
		NextTurn( economy );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Won, economy.TpiChallenges!.Slots[0].Status );
		Assert.AreEqual( (1600, 5, 101), (shares.Owned, shares.PercentOwned, shares.Price), "SharePrize 600; 98 + 98 × 4 / 100 = 101" );
	}

	[TestMethod]
	public void WinTheGameGivesFiftyOnePercentOfTheIssuedShares()
	{
		using var extensions = OpenTpwExtensions.Use( CheatsTests.On() );
		var economy = ShareEconomy();
		Assert.IsTrue( economy.QueueCheat( new CheatAction( CheatKind.WinTheGame ) ) );
		Assert.IsFalse( economy.QueueCheat( new CheatAction( CheatKind.WinTheGame, 1 ) ), "no argument" );
		NextTurn( economy );
		Assert.AreEqual( (16300, 54), (economy.TpiCareer!.Shares.Owned, economy.TpiCareer.Shares.PercentOwned), "1000 + 30000 × 51 / 100" );
		Assert.AreEqual( CheatKind.WinTheGame, economy.Cheats.Log.Single().Kind );
		var world = new ParkEconomy( EconomyTestData.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		Assert.IsFalse( world.QueueCheat( new CheatAction( CheatKind.WinTheGame ) ), "a Theme Park World park has no shares game" );
	}

	// ---- saves ------------------------------------------------------------------------------

	[TestMethod]
	public void TheCareerCarriesTheSharesThroughTheGlobalSaveAndTheParkSave()
	{
		var career = Career();
		career.Shares.Initialize( Shares );
		career.Shares.AddShares( 2000, Shares );
		career.Shares.MovePrice( 4 );
		career.StoreAward( 3, 17 );
		var bytes = career.ToGlobalSave( ThemeParkIncSession.New( _ => null, "Theme Park Inc." ), 1, false ).Write();
		var read = ThemeParkIncGlobalSave.Read( bytes );
		Assert.AreEqual( (3000, 10, "Theme Park Inc.", 104), (read.Shares!.NumberOwned, read.Shares.PercentCompanySharesOwned, read.Shares.CompanyName, read.Shares.CurrentValue),
			"the writer formats the language's name into an empty one" );
		var loaded = new ThemeParkIncCareer( Files(), TestCareerFiles.ShippedFiringLines );
		loaded.Load( read, "science" );
		Assert.AreEqual( (3000, 10, 104, 1), (loaded.Shares.Owned, loaded.Shares.PercentOwned, loaded.Shares.Price, loaded.AwardsHeld) );
		Assert.AreEqual( (byte)1, loaded.CurrentZone!.Data.Awards[3].Flag );
		Assert.AreEqual( 17, loaded.CurrentZone.Data.Awards[3].Value );

		var reset = new ThemeParkIncCareer( Files(), TestCareerFiles.ShippedFiringLines );
		Assert.AreEqual( -1, reset.Shares.Owned, "the career reset resets the shares (0x621210)" );

		var economy = ShareEconomy();
		economy.TpiCareer!.Shares.AddShares( 500, economy.Settings.ThemeParkIncShares );
		economy.TpiCareer.Shares.MovePrice( -2 );
		var json = ParkSaveFile.Serialize( economy );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( (1500, 5, 98), (restored.TpiCareer!.Shares.Owned, restored.TpiCareer.Shares.PercentOwned, restored.TpiCareer.Shares.Price) );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
	}

	[TestMethod]
	public void AParkSaveFromBeforeTheSharesGameIsRefused()
	{
		var economy = ShareEconomy();
		var json = ParkSaveFile.Serialize( economy );
		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		Assert.IsTrue( node["TpiCareer"]!.AsObject().Remove( "Shares" ) );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ) );
		Assert.AreEqual( ParkSaveFile.SharesMissing, older.Message, "refused by the validation, before any asset comparison" );
		var shortShares = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		shortShares["TpiCareer"]!["Shares"] = Convert.ToBase64String( new byte[181] );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( shortShares.ToJsonString() ) ) );
	}
}
