using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the advisor's background scan (tp.exe <c>0x59C680</c>, called by the advisor update <c>0x59A550</c>) and the money batch it scores (messages 100-105 and 107),
/// plus the golden-ticket messages 234-237. GPU-free; the thresholds are synthetic, one test reads the shipped <c>Advisor.sam</c> when OPENTPW_GAME_PATH is set.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorScanTests
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
		Welcome.Score 9000
		Bankrupted.Score	8000
		ParkNowOpen.Score	20
		ParkNowClosed.Score	20
		PrebuiltPark.Score	700
		HelpMessage.Score	1000
		HelpMessage.ContextHelpScore	1500
		WealthHigh.MoreMoneyThan	150000
		WealthHigh.Score	41
		WealthHighRepayLoan.Margin	1.5
		WealthHighRepayLoan.Score	91
		TakeLoan.Score	71
		InTheRedSixMonths.Score	76
		InTheRedThreeMonths.Score	51
		InTheRedMonthLeft.Score	111
		WagesHigh.WagesTimesHigherThanIncome	3
		WagesHigh.AverageOverMonths	4
		WagesHigh.Score	92
		AwardMoved.Score	501
		""";

	private const string GroupTwo = "\nMessageGroups[2].MinTimeSameMessage	0\nMessageGroups[2].SayOnlyOnce	0\nMessageGroups[2].DiscardAfterSlaps	0\n";

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static AdvisorBalance Balance( string text = Sam ) => AdvisorBalance.Parse( Encoding.ASCII.GetBytes( text ), "test.sam" );
	private static AdvisorController Controller( string text = Sam + GroupTwo, IReadOnlyDictionary<uint, AdvisorScoreCallback>? callbacks = null ) => new( Balance( text ), callbacks );
	private static IReadOnlyList<AdvisorDescriptorRow> Rows => AdvisorDescriptorTable.Rows;
	private static AdvisorDescriptorRow Row( int message ) => Rows[message];
	private static AdvisorScanContext Context( IAdvisorMoneyState? money = null, string text = Sam ) => new( Balance( text ), money );

	private sealed class FakeMoney : IAdvisorMoneyState
	{
		public int Balance { get; set; }
		public AdvisorLoanSlot[] Slots { get; set; } = new AdvisorLoanSlot[AdvisorLoanSlot.Count];
		public IReadOnlyList<AdvisorLoanSlot> LoanSlots => Slots;
		public bool LoanAvailable { get; set; }
		public int MonthsInRed { get; set; }
		public int ParkMonths { get; set; } = 12;
		public int[] Wages { get; set; } = Array.Empty<int>();
		public int[] Income { get; set; } = Array.Empty<int>();
		public IReadOnlyList<int> WageMonths => Wages;
		public IReadOnlyList<int> IncomeMonths => Income;
	}

	private static int Score( int message, IAdvisorMoneyState? money, int gameType = 0 ) =>
		new AdvisorScan( Rows, AdvisorScanCallbacks.Implemented ).ScoreOf( Row( message ), Context( money ), gameType );

	// ------------------------------------------------------------------ the driver

	[TestMethod]
	public void TheCursorAdvancesOneScoredDescriptorPerStepAndSkipsTheRowsGameCodePosts()
	{
		var scanned = Enumerable.Range( 0, Rows.Count ).Where( index => Rows[index].Scan ).ToArray();
		Assert.AreEqual( AdvisorScan.DescriptorCount, Rows.Count );
		Assert.AreEqual( 155, scanned.Length, "134 computed callbacks and 21 constant-zero stubs; the other 196 rows are posted by game code" );
		Assert.AreNotEqual( 0, scanned[0], "row 0 (Welcome) is posted by game code, so the first call skips it without counting it" );
		var scan = new AdvisorScan( Rows, new Dictionary<uint, AdvisorScoreCallback>() );
		var context = Context();
		for ( var step = 0; step < 2 * scanned.Length + 7; step++ )
		{
			var result = scan.Step( context, 0, ( _, _ ) => true );
			Assert.AreEqual( scanned[step % scanned.Length], result.ScoredRow, $"step {step} scores the next scanned row" );
			Assert.AreEqual( result.ScoredRow + 1, scan.Cursor, $"step {step} leaves the cursor after the row it scored" );
			Assert.AreEqual( AdvisorScan.NoMessage, result.MessageId, "an unported callback never wins" );
		}
	}

	[TestMethod]
	public void TheCursorWrapsAt351()
	{
		var scan = new AdvisorScan( Rows, new Dictionary<uint, AdvisorScoreCallback>() );
		var first = Enumerable.Range( 0, Rows.Count ).First( index => Rows[index].Scan );
		var last = Enumerable.Range( 0, Rows.Count ).Last( index => Rows[index].Scan );
		Assert.AreEqual( 350, last, "the last descriptor is scanned" );
		scan.RestoreCursor( last );
		Assert.AreEqual( last, scan.Step( Context(), 0, ( _, _ ) => true ).ScoredRow );
		Assert.AreEqual( AdvisorScan.DescriptorCount, scan.Cursor, "the cursor reaches 351 and wraps when the next call starts" );
		Assert.AreEqual( first, scan.Step( Context(), 0, ( _, _ ) => true ).ScoredRow );
		Assert.AreEqual( first + 1, scan.Cursor );
		scan.RestoreCursor( AdvisorScan.DescriptorCount );
		Assert.AreEqual( first, scan.Step( Context(), 0, ( _, _ ) => true ).ScoredRow, "a cursor saved at 351 wraps too" );
		Assert.ThrowsException<InvalidDataException>( () => scan.RestoreCursor( 352 ) );
		Assert.ThrowsException<InvalidDataException>( () => scan.RestoreCursor( -1 ) );
	}

	[TestMethod]
	public void AnUnportedCallbackScoresZeroLikeTheOriginalsStub()
	{
		var scan = new AdvisorScan( Rows, AdvisorScanCallbacks.Implemented );
		var money = new FakeMoney { Balance = -10, LoanAvailable = true };
		foreach ( var row in Rows.Where( row => row.Scan && !AdvisorScanCallbacks.Implemented.ContainsKey( row.Callback ) && row.GameMode == 2 ) )
			Assert.AreEqual( 0, scan.ScoreOf( row, Context( money ), 0 ), $"message {row.MessageId} (callback 0x{row.Callback:X}) is not ported" );
		// a whole pass over a park in the red posts the money line and nothing else
		var controller = Controller();
		var posted = new List<int>();
		for ( var step = 0; step < 155; step++ )
			if ( controller.ScanOnce( money, 100, 0, true ).Posted is { } advice )
				posted.Add( advice.MessageId );
		CollectionAssert.AreEqual( new[] { 102 }, posted, "only the ported TakeLoan callback produces anything" );
	}

	[TestMethod]
	public void TheGameModeSelectorExcludesTheRowsOfTheOtherMode()
	{
		var called = new HashSet<uint>();
		var callbacks = Rows.Where( row => row.Scan ).Select( row => row.Callback ).Distinct().ToDictionary( address => address, address => (AdvisorScoreCallback)(_ => { called.Add( address ); return 1; }) );
		var scan = new AdvisorScan( Rows, callbacks );
		var context = Context();
		foreach ( var row in Rows.Where( row => row.Scan ) )
		{
			called.Clear();
			var offline = scan.ScoreOf( row, context, 0 );
			Assert.AreEqual( row.GameMode != 1, called.Contains( row.Callback ), $"message {row.MessageId} offline" );
			Assert.AreEqual( row.GameMode == 1 ? -1 : 1, offline );
			called.Clear();
			var instant = scan.ScoreOf( row, context, 2 );
			Assert.AreEqual( row.GameMode != 0, called.Contains( row.Callback ), $"message {row.MessageId} in Instant Action" );
			Assert.AreEqual( row.GameMode == 0 ? -1 : 1, instant );
		}
		Assert.IsTrue( Rows.Any( row => row.Scan && row.GameMode == 0 ), "some rows are full-simulation only" );
	}

	[TestMethod]
	public void OnlyAStrictlyHigherEligibleScoreWinsAndOnlyAboveTheMinimumIsQueued()
	{
		var address = Row( 100 ).Callback;
		var score = 25;
		var controller = Controller( callbacks: new Dictionary<uint, AdvisorScoreCallback> { [address] = _ => score } );
		var money = new FakeMoney();
		AdvisorScanOutcome Pass()
		{
			AdvisorScanOutcome last = default;
			for ( var step = 0; step < 155; step++ )
				if ( controller.ScanOnce( money, 100, 0, true ) is { Posted: not null } outcome )
					last = outcome;
			return last;
		}
		Assert.IsNull( Pass().Posted, "a score equal to MinScoreForConsideration is not queued (0x59A5F5: JLE)" );
		Assert.AreEqual( 0, controller.Queue.Count );
		score = 26;
		var posted = Pass();
		Assert.AreEqual( 100, posted.Posted!.MessageId );
		Assert.AreEqual( 26, posted.Posted.Score, "the record carries the callback's score" );
		Assert.IsTrue( posted.Admission!.Value.Stored );
		Assert.AreEqual( 1, controller.Queue.Count );
		Pass();
		Assert.AreEqual( 1, controller.Queue.Count, "one pending copy is the descriptor's limit: the eligibility check refuses a second" );
	}

	[TestMethod]
	public void ATutorialGroupScanMessageIsRefusedWhileTheTutorialIsOff()
	{
		var tutorial = Rows.First( row => row.Scan && row.Group == 1 );
		var controller = Controller( callbacks: new Dictionary<uint, AdvisorScoreCallback> { [tutorial.Callback] = _ => 500 } );
		var posted = new List<int>();
		for ( var step = 0; step < 155; step++ )
			if ( controller.ScanOnce( null, 100, 0, tutorialEnabled: false ).Posted is { } advice )
				posted.Add( advice.MessageId );
		Assert.AreEqual( 0, posted.Count( id => Rows[id].Group == 1 ), "the eligibility check 0x59ABC0 refuses tutorial messages with TutorialOn clear" );
		Assert.AreEqual( 0, controller.Queue.Count );
		for ( var step = 0; step < 155; step++ )
			controller.ScanOnce( null, 100, 0, tutorialEnabled: true );
		Assert.IsTrue( Enumerable.Range( 0, 8 ).Any( slot => controller.Queue.GetSlot( slot ) is { } advice && Rows[advice.MessageId].Group == 1 ), "with it set the same message queues" );
	}

	[TestMethod]
	public void TheCursorIsSavedWithTheControllerAndAnOldSnapshotRestoresItAtZero()
	{
		var controller = Controller();
		for ( var step = 0; step < 40; step++ )
			controller.ScanOnce( null, 100, 0, true );
		var cursor = controller.Scan.Cursor;
		Assert.IsTrue( cursor > 40 );
		var snapshot = controller.CaptureSnapshot();
		Assert.AreEqual( cursor, snapshot.ScanCursor );
		var json = Encoding.UTF8.GetString( ParkResumeJson.Serialize( snapshot ) );
		var restored = Controller();
		restored.RestoreSnapshot( ParkResumeJson.Deserialize<AdvisorController.AdvisorControllerSnapshot>( Encoding.UTF8.GetBytes( json ) ) );
		Assert.AreEqual( cursor, restored.Scan.Cursor );
		// a snapshot written before the scan existed has no cursor
		var old = System.Text.RegularExpressions.Regex.Replace( json, ",\\s*\"ScanCursor\"\\s*:\\s*\\d+", "" );
		Assert.IsFalse( old.Contains( "ScanCursor", StringComparison.Ordinal ) );
		var fresh = Controller();
		fresh.RestoreSnapshot( ParkResumeJson.Deserialize<AdvisorController.AdvisorControllerSnapshot>( Encoding.UTF8.GetBytes( old ) ) );
		Assert.AreEqual( 0, fresh.Scan.Cursor );
		Assert.ThrowsException<InvalidDataException>( () => fresh.ValidateSnapshot( snapshot with { ScanCursor = 352 } ) );
		Assert.ThrowsException<InvalidDataException>( () => fresh.ValidateSnapshot( snapshot with { ScanCursor = -1 } ) );
	}

	[TestMethod]
	public void TheAdvisorScansOncePerParkTurnAndNotWhilePaused()
	{
		var controller = Controller();
		var advisor = new AutomaticAdvisor( controller, () => 0 );
		Assert.AreEqual( 0, advisor.ScanTurns( null, 1000, 0, true, halted: false ), "the first update only takes the turn as its start" );
		Assert.AreEqual( 0, controller.Scan.Cursor );
		Assert.AreEqual( 0, advisor.ScanTurns( null, 1000, 0, true, halted: false ), "no turn passed" );
		Assert.AreEqual( 1, advisor.ScanTurns( null, 1001, 0, true, halted: false ) );
		var cursor = controller.Scan.Cursor;
		Assert.IsTrue( cursor > 0 );
		Assert.AreEqual( 0, advisor.ScanTurns( null, 1005, 0, true, halted: true ), "a paused game scans nothing" );
		Assert.AreEqual( cursor, controller.Scan.Cursor );
		Assert.AreEqual( 1, advisor.ScanTurns( null, 1006, 0, true, halted: false ), "the halt took turn 1005 as the new start: the turns 1001-1005 are not caught up (tp.exe never scans them)" );
		Assert.AreEqual( 1, advisor.ScanTurns( null, 1007, 0, true, halted: false ) );
		Assert.AreEqual( (int)AutomaticAdvisor.MaximumScansPerUpdate, advisor.ScanTurns( null, 1007 + 500, 0, true, halted: false ), "a long jump is capped" );
		Assert.AreEqual( 0, advisor.ScanTurns( null, 10, 0, true, halted: false ), "a turn counter that went backwards re-synchronises without scanning" );
	}

	[TestMethod]
	public void TheUpdatePathScansAParkOncePerTurnAndNeverWhilePausedOrLost()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.OpenPark();
		var controller = Controller();
		var advisor = new AutomaticAdvisor( controller, () => 0 );
		Assert.AreEqual( 0, advisor.ScanPark( park, gamePaused: false ), "the first update only takes the turn" );
		park.Advance( 30 );
		var turns = (int)park.Turn - 0;
		Assert.IsTrue( turns >= 2 );
		Assert.AreEqual( turns, advisor.ScanPark( park, gamePaused: false ), "one scan per turn that passed" );
		Assert.AreEqual( 0, advisor.ScanPark( park, gamePaused: false ), "none when no turn passed" );
		var cursor = controller.Scan.Cursor;
		park.Advance( 30 );
		Assert.AreEqual( 0, advisor.ScanPark( park, gamePaused: true ) );
		Assert.AreEqual( cursor, controller.Scan.Cursor );
		park.Advance( 30 );
		Assert.AreEqual( 1, Math.Min( 1, advisor.ScanPark( park, gamePaused: false ) ), "the turns of the pause are not caught up" );
		// a lost park: the thing loop is skipped (0x516395), so no scan, and no burst when it is over
		park.AdvanceMonths( 1 );
		while ( !park.IsBankrupt )
			park.AdvanceMonths( 1 );
		advisor.ScanPark( park, gamePaused: false );
		var lostCursor = controller.Scan.Cursor;
		park.AdvanceMonths( 1 );
		Assert.AreEqual( 0, advisor.ScanPark( park, gamePaused: false ) );
		Assert.AreEqual( lostCursor, controller.Scan.Cursor );
	}

	[TestMethod]
	public void TheLoanCheckReadsTheNetWorthThroughTheCreditTestsCacheWithoutWritingIt()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		park.Litter.Cells = new ParkCellMap( 128, 128 );
		Assert.AreEqual( 32768, park.NetWorth( 20 ), "the credit test's read caches the value" );
		var cached = park.CachedNetWorth;
		park.Litter.Cells = null;
		Assert.AreEqual( 32768, park.PeekNetWorth( 20 ), "the worth dropped inside the window: the cached value answers" );
		Assert.IsTrue( park.CanTakeAnyLoan(), "so a loan still passes" );
		Assert.AreEqual( cached, park.CachedNetWorth, "the peek does not write the cache" );
		park.Advance( 3000 );
		Assert.AreEqual( 0, park.PeekNetWorth( 20 ), "past the window it counts fresh" );
		Assert.IsFalse( park.CanTakeAnyLoan() );
		Assert.AreEqual( cached, park.CachedNetWorth, "and still does not stamp it" );
	}

	// ------------------------------------------------------------------ the money batch

	[TestMethod]
	public void TakeLoanNeedsAnOverdraftAndALoanToTake()
	{
		Assert.AreEqual( 71, Score( 102, new FakeMoney { Balance = -1, LoanAvailable = true } ), "TakeLoan.Score" );
		Assert.AreEqual( 0, Score( 102, new FakeMoney { Balance = 0, LoanAvailable = true } ), "balance 0 is not below 0 (0x59E9C6: JGE)" );
		Assert.AreEqual( 0, Score( 102, new FakeMoney { Balance = 5000, LoanAvailable = true } ) );
		Assert.AreEqual( 0, Score( 102, new FakeMoney { Balance = -1, LoanAvailable = false } ), "no loan can be taken" );
		Assert.AreEqual( 0, Score( 102, null ), "without a park" );
		Assert.AreEqual( -1, Score( 102, new FakeMoney { Balance = -1, LoanAvailable = true }, gameType: 2 ), "102 is for the full simulation only" );
	}

	[TestMethod]
	public void TheSixThreeAndFiveMonthWarningsCoverTheMonthsInTheRedWithoutALoan()
	{
		foreach (var (months, expected) in new[] { (0, 103), (1, 103), (2, 103), (3, 104), (4, 104), (5, 105), (6, 0), (7, 0) })
		{
			var money = new FakeMoney { Balance = -1, LoanAvailable = false, MonthsInRed = months };
			var scores = new[] { 103, 104, 105 }.ToDictionary( id => id, id => Score( id, money ) );
			var expectedScore = expected switch { 103 => 76, 104 => 51, 105 => 111, _ => 0 };
			Assert.AreEqual( expectedScore, scores.Values.Sum(), $"{months} months in the red: only {expected} scores" );
			if ( expected != 0 )
				Assert.AreEqual( expectedScore, scores[expected] );
		}
		var withLoan = new FakeMoney { Balance = -1, LoanAvailable = true, MonthsInRed = 4 };
		Assert.AreEqual( 0, new[] { 103, 104, 105 }.Sum( id => Score( id, withLoan ) ), "the three warnings need no loan to be available" );
		var black = new FakeMoney { Balance = 0, LoanAvailable = false, MonthsInRed = 4 };
		Assert.AreEqual( 0, new[] { 103, 104, 105 }.Sum( id => Score( id, black ) ), "and a balance below 0" );
		Assert.AreEqual( 51, Score( 104, new FakeMoney { Balance = -1, MonthsInRed = 4 }, gameType: 2 ), "103-105 are said in Instant Action too (mode 2)" );
	}

	[TestMethod]
	public void WealthHighNeedsMoreMoneyThanTheThresholdAndNoLoanTaken()
	{
		Assert.AreEqual( 41, Score( 100, new FakeMoney { Balance = 150001 } ), "WealthHigh.Score" );
		Assert.AreEqual( 0, Score( 100, new FakeMoney { Balance = 150000 } ), "the balance must exceed MoreMoneyThan (JLE)" );
		var loaned = new FakeMoney { Balance = 999999, Slots = Slots( (3, 0, 100) ) };
		Assert.AreEqual( 0, Score( 100, loaned ), "any loan taken silences it" );
		Assert.AreEqual( 0, Score( 100, null ) );
	}

	[TestMethod]
	public void WealthHighRepayLoanComparesTheCheapestLoanWithTheCashLeftAfterAllDebts()
	{
		// two loans: 10 months at 1000 owes 10000, 4 months at 1000 owes 4000; the Margin is 1.5, so the cheapest (4000) × 1.5 = 6000 must be below balance − 14000
		var slots = Slots( (10, 0, 1000), (4, 0, 1000) );
		Assert.AreEqual( 14000, AdvisorScanCallbacks.TotalDebt( new FakeMoney { Slots = slots } ) );
		Assert.AreEqual( 1, AdvisorScanCallbacks.CheapestLoan( new FakeMoney { Slots = slots } ) );
		Assert.AreEqual( 0, Score( 101, new FakeMoney { Balance = 20000, Slots = slots } ), "6000 is not below 20000 − 14000" );
		Assert.AreEqual( 91, Score( 101, new FakeMoney { Balance = 20001, Slots = slots } ), "WealthHighRepayLoan.Score" );
		Assert.AreEqual( 0, Score( 101, new FakeMoney { Balance = 1_000_000 } ), "no loan to repay: the cheapest index is −1 (JL)" );
		Assert.AreEqual( -1, Score( 101, new FakeMoney { Balance = 1_000_000, Slots = slots }, gameType: 2 ), "101 is for the full simulation only" );
		// a repaid record (nothing owed) is skipped; equal debts keep the first; the amounts compare as unsigned words
		var repaid = Slots( (4, 4, 1000), (5, 0, 100), (5, 0, 100), (2, 5, 100) );
		Assert.AreEqual( 1, AdvisorScanCallbacks.CheapestLoan( new FakeMoney { Slots = repaid } ) );
		Assert.AreEqual( 3, AdvisorScanCallbacks.LoansTaken( new FakeMoney { Slots = repaid.Take( 3 ).ToArray().Concat( new AdvisorLoanSlot[5] ).ToArray() } ) );
		Assert.AreEqual( -1, AdvisorScanCallbacks.CheapestLoan( new FakeMoney { Slots = Slots( (4, 4, 1000) ) } ) );
		// the difference is rounded to a single before the compare (fild dword, fstp dword): 11 184 811 × 1.5 = 16 777 216.5
		var one = Slots( (1, 0, 11_184_811) );
		Assert.AreEqual( 0, Score( 101, new FakeMoney { Slots = one, Balance = 11_184_811 + 16_777_217 } ), "16 777 217 is held as the single 16 777 216, which 16 777 216.5 does not undercut" );
		Assert.AreEqual( 91, Score( 101, new FakeMoney { Slots = one, Balance = 11_184_811 + 16_777_218 } ) );
	}

	[TestMethod]
	public void WagesHighAveragesTheNewestMonthsOfTheWageAndIncomeRings()
	{
		// the average is over 4 months; wages must exceed income × 3 (WagesTimesHigherThanIncome)
		var money = new FakeMoney { ParkMonths = 12, Wages = new[] { 901, 901, 901, 901 }, Income = new[] { 300, 300, 300, 300 } };
		Assert.AreEqual( 92, Score( 107, money ), "WagesHigh.Score" );
		money.Wages = new[] { 900, 900, 900, 900 };
		Assert.AreEqual( 0, Score( 107, money ), "900 is not above 300 × 3" );
		money.Wages = new[] { 5000, 5000, 5000, 5000, 1, 1, 1 };
		money.Income = new[] { 100, 100, 100, 100, 100_000, 100_000, 100_000 };
		Assert.AreEqual( 92, Score( 107, money ), "only the 4 newest months count" );
		money.Wages = new[] { 1000, 1000 };
		money.Income = new[] { 100, 100 };
		Assert.AreEqual( 92, Score( 107, money ), "a ring with fewer months than asked averages what it holds" );
		money.ParkMonths = 2;
		Assert.AreEqual( 0, Score( 107, money ), "the park clock must have run 3 months" );
		money.ParkMonths = 3;
		Assert.AreEqual( 92, Score( 107, money ) );
		money.Wages = Array.Empty<int>();
		money.Income = Array.Empty<int>();
		Assert.AreEqual( 0, Score( 107, money ), "an empty ring averages nothing" );
		money.Wages = new[] { 1000, 1000 };
		Assert.AreEqual( 0, Score( 107, money ), "an empty income ring scores 0 too (0x59ED8D)" );
		Assert.AreEqual( (10, 3), AdvisorScanCallbacks.RingAverage( new[] { 10, 10, 11, 99 }, 3 ), "the quotient is truncated: 31 / 3" );
		Assert.AreEqual( (0, 0), AdvisorScanCallbacks.RingAverage( new[] { 1 }, 0 ) );
		Assert.AreEqual( (0, -2), AdvisorScanCallbacks.RingAverage( new[] { 1 }, -2 ) );
		Assert.AreEqual( (-3, 2), AdvisorScanCallbacks.RingAverage( new[] { -7, 1 }, 5 ), "toward zero" );
	}

	[TestMethod]
	public void ASettingsFileWithoutAThresholdLeavesItsCallbackSilent()
	{
		var text = string.Join( '\n', Sam.Split( '\n' ).Where( line => !line.StartsWith( "WealthHigh.MoreMoneyThan", StringComparison.Ordinal ) && !line.StartsWith( "TakeLoan.Score", StringComparison.Ordinal ) ) );
		var context = Context( new FakeMoney { Balance = 500_000, Slots = Slots( (3, 0, 1) ), LoanAvailable = true }, text );
		Assert.AreEqual( 0, AdvisorScanCallbacks.Implemented[AdvisorScanCallbacks.WealthHigh]( context ) );
		Assert.AreEqual( 0, AdvisorScanCallbacks.Implemented[AdvisorScanCallbacks.TakeLoan]( Context( new FakeMoney { Balance = -5, LoanAvailable = true }, text ) ), "a missing score key scores 0" );
	}

	[TestMethod]
	public void TheShippedAdvisorSamGivesTheMoneyLinesTheirBands()
	{
		var balance = ShippedBalance();
		int Run( int message, FakeMoney money, int gameType = 0 ) =>
			new AdvisorScan( Rows, AdvisorScanCallbacks.Implemented ).ScoreOf( Row( message ), new AdvisorScanContext( balance, money ), gameType );
		int Key( string key ) => balance.Scores[key];
		var threshold = (int)balance.ScanParameters["WealthHigh.MoreMoneyThan"];
		Assert.AreEqual( Key( "WealthHigh.Score" ), Run( 100, new FakeMoney { Balance = threshold + 1 } ) );
		Assert.AreEqual( 0, Run( 100, new FakeMoney { Balance = threshold } ) );
		Assert.AreEqual( Key( "TakeLoan.Score" ), Run( 102, new FakeMoney { Balance = -1, LoanAvailable = true } ) );
		var red = new FakeMoney { Balance = -1, LoanAvailable = false };
		CollectionAssert.AreEqual( new[] { Key( "InTheRedSixMonths.Score" ), Key( "InTheRedThreeMonths.Score" ), Key( "InTheRedMonthLeft.Score" ) },
			new[] { 0, 3, 5 }.Select( months => { red.MonthsInRed = months; return new[] { 103, 104, 105 }.Sum( id => Run( id, red ) ); } ).ToArray() );
		Assert.IsTrue( Key( "InTheRedMonthLeft.Score" ) > balance.MinimumScore && Key( "InTheRedThreeMonths.Score" ) > balance.MinimumScore && Key( "InTheRedSixMonths.Score" ) > balance.MinimumScore, "every red warning can pass the minimum" );
		// WealthHighRepayLoan.Margin is a single: 1.2 is read as 1.2f
		Assert.AreEqual( (double)1.2f, balance.ScanParameters["WealthHighRepayLoan.Margin"], 0 );
		// the shipped file reads, and the group of the golden-ticket messages is present
		Assert.IsTrue( balance.Groups.ContainsKey( Row( 234 ).Group ) );
		Assert.AreEqual( Key( "AwardMoved.Score" ), balance.ScoreOf( 234 ) );
	}

	private static AdvisorBalance ShippedBalance()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Advisor.sam." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var file = Directory.EnumerateDirectories( dataPath ).Where( directory => string.Equals( Path.GetFileName( directory ), "Advisor", StringComparison.OrdinalIgnoreCase ) )
			.SelectMany( directory => Directory.EnumerateFiles( directory ) ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "Advisor.sam", StringComparison.OrdinalIgnoreCase ) );
		if ( file == null )
			Assert.Inconclusive( "The original Advisor/Advisor.sam is missing." );
		return AdvisorBalance.Parse( File.ReadAllBytes( file! ), file! );
	}

	private static AdvisorLoanSlot[] Slots( params (int Months, int Repaid, int Monthly)[] loans )
	{
		var slots = new AdvisorLoanSlot[AdvisorLoanSlot.Count];
		for ( var index = 0; index < loans.Length; index++ )
			slots[index] = new AdvisorLoanSlot( true, loans[index].Months, loans[index].Repaid, loans[index].Monthly );
		return slots;
	}

	// ------------------------------------------------------------------ the economy behind the callbacks

	[TestMethod]
	public void TheEconomyStateReadsTheBankLikeTheOriginalsHelpers()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		var state = new EconomyMoneyState( park );
		Assert.AreEqual( 100, state.Balance );
		Assert.AreEqual( 0, AdvisorScanCallbacks.LoansTaken( state ) );
		Assert.AreEqual( -1, AdvisorScanCallbacks.CheapestLoan( state ) );
		Assert.IsFalse( state.LoanAvailable, "the net worth of a park without land is 0, below every offer" );
		park.Litter.Cells = new ParkCellMap( 128, 128 );
		var stored = string.Join( ",", park.AvailableOfferIndices );
		Assert.IsTrue( state.LoanAvailable, "32768 covers the smallest test offer" );
		var loan = park.TakeLoan( 2 );
		Assert.AreEqual( 100 + loan.OriginalAmount, state.Balance );
		Assert.AreEqual( 1, AdvisorScanCallbacks.LoansTaken( state ) );
		Assert.AreEqual( loan.Months * (int)loan.MonthlyRepayment, AdvisorScanCallbacks.TotalDebt( state ) );
		Assert.AreEqual( 2, AdvisorScanCallbacks.CheapestLoan( state ) );
		Assert.AreEqual( park.Settings.Loans.Any( offer => offer.Index != 2 && offer.Amount <= 32768 - loan.Months * (int)loan.MonthlyRepayment ), state.LoanAvailable, "the owed instalments come off the net worth" );
		Assert.AreEqual( stored, string.Join( ",", park.AvailableOfferIndices ), "reading the availability left the stored credit tests alone" );
	}

	[TestMethod]
	public void TheEconomyStateCountsMonthsAndKeepsTheNewestRingMonthsFirst()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.OpenPark();
		var state = new EconomyMoneyState( park );
		Assert.AreEqual( 0, state.ParkMonths );
		park.AdvanceMonths( 1 );
		Assert.AreEqual( 1, state.ParkMonths, "January 2000 has 31 days: one whole 30-day month" );
		Assert.IsTrue( state.Balance < 0 );
		Assert.AreEqual( 0, state.MonthsInRed );
		park.AdvanceMonths( 4 );
		Assert.AreEqual( ParkCalendar.Seconds( park.Tick ) / (30 * ParkCalendar.SecondsPerDay), state.ParkMonths );
		Assert.AreEqual( park.MonthsSinceEnteringRed(), state.MonthsInRed );
		Assert.IsTrue( state.MonthsInRed is >= 3 and <= 5 );
		Assert.AreEqual( park.Ledger.History.Count, state.WageMonths.Count );
		Assert.AreEqual( (int)park.Ledger.History[^1][LedgerCategory.StaffCosts], state.WageMonths[0], "the newest closed month first" );
		Assert.AreEqual( (int)park.Ledger.History[0].MoneyIn, state.IncomeMonths[^1] );
	}

	[TestMethod]
	public void AParkInTheRedProducesTheRightWarningThroughTheRealQueueAndSelection()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.OpenPark();
		park.AdvanceMonths( 1 );
		Assert.IsTrue( park.Balance < 0 );
		// the wages line would outrank the early warnings once the park is three months old; it has its own test
		var controller = Controller( (Sam + GroupTwo).Replace( "WagesHigh.Score\t92", "WagesHigh.Score\t0", StringComparison.Ordinal ) );
		var state = new EconomyMoneyState( park );
		uint clock = 1_000_000;
		var seen = new List<(int Months, int Message, int Response)>();
		while ( !park.IsBankrupt )
		{
			var months = park.MonthsSinceEnteringRed();
			var tick = (uint)park.Turn;
			var queued = new HashSet<int>();
			for ( var step = 0; step < 155; step++ )
				if ( controller.ScanOnce( state, tick, 0, true ).Posted is { } advice )
					queued.Add( advice.MessageId );
			var expected = months < 3 ? 103 : months < 5 ? 104 : 105;
			CollectionAssert.AreEqual( new[] { expected }, queued.ToArray(), $"{months} months in the red: one warning is queued" );
			clock += 100_000;
			var selection = controller.Update( () => clock, () => tick, _ => 0u );
			Assert.IsNotNull( selection, "the queue serves the warning" );
			seen.Add( (months, selection.Value.Advice.MessageId, selection.Value.ResponseId) );
			park.AdvanceMonths( 1 );
		}
		Assert.IsTrue( seen.All( item => item.Message == (item.Months < 3 ? 103 : item.Months < 5 ? 104 : 105) ), "the selection is the warning of the month count" );
		// a calendar month is 28 to 31 days, so the 30-day count may skip a value from one month end to the next; every band is still reached
		CollectionAssert.AreEqual( new[] { 103, 104, 105 }, seen.Select( item => item.Message ).Distinct().ToArray(), "months seen: " + string.Join( ",", seen.Select( item => item.Months ) ) );
		Assert.AreEqual( 268, seen[0].Response, "103's first response" );
		Assert.IsTrue( seen.Where( item => item.Message == 103 ).All( item => item.Response is 268 or 269 ), "103 has two responses and cycles" );
		Assert.IsTrue( seen.Where( item => item.Message == 104 ).All( item => item.Response is 270 or 271 ) );
		Assert.IsTrue( seen.Where( item => item.Message == 105 ).All( item => item.Response is 272 or 273 ) );
		// at 6 months in the red the park is lost: none of 103-105 matches any more
		var after = 0;
		for ( var step = 0; step < 155; step++ )
			if ( controller.ScanOnce( state, (uint)park.Turn, 0, true ).Posted is not null )
				after++;
		Assert.AreEqual( 0, after );
	}

	[TestMethod]
	public void TheWagesLineOutranksTheEarlyWarningsOnceThePositionHasHeldForThreeMonths()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.OpenPark();
		park.AdvanceMonths( 4 );
		var state = new EconomyMoneyState( park );
		Assert.IsTrue( state.WageMonths.Count >= 3 && state.ParkMonths >= 3 && state.Balance < 0 );
		Assert.IsTrue( state.WageMonths.Take( 4 ).Average() > 3 * state.IncomeMonths.Take( 4 ).Average(), "the wages are far above an income of nothing" );
		var controller = Controller();
		var tick = (uint)park.Turn;
		var queued = new List<int>();
		for ( var step = 0; step < 155; step++ )
			if ( controller.ScanOnce( state, tick, 0, true ).Posted is { } advice )
				queued.Add( advice.MessageId );
		CollectionAssert.Contains( queued, 107 );
		Assert.AreEqual( 107, controller.Update( () => 5_000_000u, () => tick, _ => 0u )!.Value.Advice.MessageId, "the strongest pending line is served first" );
	}

	[TestMethod]
	public void AParkWithALoanToTakeIsToldToTakeIt()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		park.Litter.Cells = new ParkCellMap( 128, 128 );
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.OpenPark();
		park.AdvanceMonths( 1 );
		Assert.IsTrue( park.Balance < 0 );
		var controller = Controller();
		var state = new EconomyMoneyState( park );
		var posted = new List<int>();
		for ( var step = 0; step < 155; step++ )
			if ( controller.ScanOnce( state, (uint)park.Turn, 0, true ).Posted is { } advice )
				posted.Add( advice.MessageId );
		CollectionAssert.AreEqual( new[] { 102 }, posted, "a loan can be taken: 102, and none of the in-the-red warnings" );
		var selection = controller.Update( () => 2_000_000u, () => (uint)park.Turn, _ => 0u );
		Assert.AreEqual( (102, 266), (selection!.Value.Advice.MessageId, selection.Value.ResponseId) );
	}

	// ------------------------------------------------------------------ the golden-ticket messages

	[TestMethod]
	public void AGlobalGoldenTicketMovingHereIsNowSaid()
	{
		var controller = Controller();
		foreach ( var kind in new[] { GoldenTicketKind.CoasterHeight, GoldenTicketKind.GokartExcitement, GoldenTicketKind.WaterLength, GoldenTicketKind.BigPark } )
		{
			var message = AutomaticAdvisor.GlobalTicketMovedMessage( kind )!.Value;
			var admission = controller.Advise( message, 100, 0, true );
			Assert.IsNotNull( admission, $"message {message} has a definition" );
			Assert.IsTrue( admission!.Value.Stored );
			Assert.AreEqual( 501, controller.Balance.ScoreOf( message ), "AwardMoved.Score" );
			Assert.AreEqual( 474 + (message - 234), AdvisorTables.EventMessages.Single( item => item.MessageId == message ).FirstResponseId );
		}
		var selection = controller.Update( () => 1_000_000u, () => 100u, _ => 0u );
		Assert.AreEqual( 234, selection!.Value.Advice.MessageId );
		Assert.AreEqual( 474, selection.Value.ResponseId );
		Assert.IsNull( Controller().Advise( 234, 100, 2, true ), "the four messages are for the full simulation only (mode 0)" );
		Assert.IsNull( Controller( Sam ).Advise( 234, 100, 0, true ), "a settings file without the group's controls leaves them silent instead of failing" );
	}
}
