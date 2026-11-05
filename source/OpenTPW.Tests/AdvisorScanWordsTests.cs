using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009 / UI-025: the scan callbacks that name a thing or a cell leave it in the scan object's words +4 (a thing id) and +8 (a cell word or a figure), the lookup clears both before every
/// callback (0x59C10F), and the speech copies the nonzero ones into the advice record (0x59B6CF-0x59B6E4) for the message tube's locate button. The scan's own enqueue builds its record with
/// both words 0 (0x59A5FD). GPU-free.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorScanWordsTests
{
	private static readonly string Groups = string.Concat( Enumerable.Range( 0, 16 ).Select( group => $"MessageGroups[{group}].MinTimeSameMessage\t0\nMessageGroups[{group}].SayOnlyOnce\t0\nMessageGroups[{group}].DiscardAfterSlaps\t0\n" ) );

	private static readonly string Sam = "GeneralAdvisor.MinTimeAnyMessage\t5\nGeneralAdvisor.MinTimeSameMessage\t120\nGeneralAdvisor.MinScoreForConsideration\t25\n" + Groups + Thresholds;

	private const string Thresholds = """
		Welcome.Score	1
		Bankrupted.Score	1
		ParkNowOpen.Score	1
		ParkNowClosed.Score	1
		PrebuiltPark.Score	1
		FeaturesDirtyToilets1.FilthierThan	60
		FeaturesDirtyToilets1.PointsPerFilthPct	1
		FeaturesDirtyToilets2.FilthierThan	60
		FeaturesDirtyToilets2.PointsPerFilthPct	1
		FeaturesDirtyToilets3.FilthierThan	90
		FeaturesDirtyToilets3.PointsPerFilthPct	2
		FeaturesDirtyToilets4.FilthierThan	90
		FeaturesDirtyToilets4.PointsPerFilthPct	2
		QueueUnconnected.Score	87
		VisitorsTooScared.AverageScarierThan	70
		VisitorsTooScared.MinimumRides	2
		VisitorsTooScared.Score	50
		BigBusloadArrived.HowBigIsBig	80
		BigBusloadArrived.Score	44
		DrinksPoorValue.ValueWorseThan	50
		DrinksPoorValue.ScorePerPoint	0
		DrinksPoorSatisfaction.SatisfactionWorseThan	40
		DrinksPoorSatisfaction.ScorePerPoint	3
		DrinksGreatValue.ValueBetterThan	20
		DrinksGreatValue.ScorePerPoint	2
		DrinksGreatSatisfaction.SatisfactionBetterThan	63
		DrinksGreatSatisfaction.ScorePerPoint	5
		""";

	private static AdvisorBalance Balance() => AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Sam ), "test.sam" );

	private static AdvisorScan Scan() => new( AdvisorDescriptorTable.Rows, AdvisorScanCallbacks.Implemented );

	private static AdvisorDescriptorRow RowOf( uint callback ) => AdvisorDescriptorTable.Rows.First( row => row.Scan && row.Callback == callback );

	private sealed class Money : IAdvisorMoneyState
	{
		public int Balance => 0;
		public IReadOnlyList<AdvisorLoanSlot> LoanSlots { get; } = new AdvisorLoanSlot[AdvisorLoanSlot.Count];
		public bool LoanAvailable => false;
		public int MonthsInRed => 0;
		public int ParkMonths => 12;
		public IReadOnlyList<int> WageMonths { get; } = Array.Empty<int>();
		public IReadOnlyList<int> IncomeMonths { get; } = Array.Empty<int>();
	}

	/// <summary>An economy park whose world names every object by ten times its economy id.</summary>
	private static (ParkEconomy Park, EconomyParkState State) World( int arrivals = 0 )
	{
		var park = EconomyTestData.Park( initialCash: 500000 );
		park.ObjectThingId = instance => instance * 10;
		park.OpenPark();
		var state = new EconomyParkState( park ) { ArrivalWaves = arrivals == 0 ? Array.Empty<int>() : new[] { arrivals } };
		return (park, state);
	}

	private static AdvisorSpeechCheck Check( uint callback, EconomyParkState? state, int gameType = 0 ) =>
		Scan().CheckOf( RowOf( callback ), new AdvisorScanContext( Balance(), new Money(), state ), gameType );

	// ------------------------------------------------------------------ the scan object's words

	[TestMethod]
	public void EveryLookupClearsTheWordsBeforeTheModeSelectorAndTheCallback()
	{
		var outside = AdvisorDescriptorTable.Rows.First( row => row.Scan && !AdvisorTables.GameModeAccepts( row.GameMode, 2 ) && AdvisorTables.GameModeAccepts( row.GameMode, 0 ) );
		var plain = AdvisorDescriptorTable.Rows.First( row => row.Scan && row.MessageId != outside.MessageId && AdvisorTables.GameModeAccepts( row.GameMode, 2 ) );
		var callbacks = new Dictionary<uint, AdvisorScoreCallback>
		{
			[outside.Callback] = context => { context.RememberThing( 9 ); context.RememberSecondary( 5 ); return 40; },
			[plain.Callback] = context => { context.RememberThing( 7 ); context.RememberSecondary( 6 ); return 30; },
		};
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, callbacks );
		var context = new AdvisorScanContext( Balance(), new Money() );
		Assert.AreEqual( new AdvisorSpeechCheck( 30, 7, 6 ), scan.CheckOf( plain, context, 2 ), "a callback's words reach the check" );
		// [BIN:TPW-EXE:0x0059C10F scan words reset] the next lookup zeroes them before the mode selector: a row the mode excludes scores -1 with both words 0
		Assert.AreEqual( new AdvisorSpeechCheck( -1, 0, 0 ), scan.CheckOf( outside, context, 2 ), "the mode excludes the row: no callback, no stale words" );
		Assert.AreEqual( new AdvisorSpeechCheck( 40, 9, 5 ), scan.CheckOf( outside, context, 0 ) );
		var unported = AdvisorDescriptorTable.Rows.First( row => row.Scan && !callbacks.ContainsKey( row.Callback ) && !AdvisorScanCallbacks.Implemented.ContainsKey( row.Callback ) && AdvisorTables.GameModeAccepts( row.GameMode, 0 ) );
		Assert.AreEqual( new AdvisorSpeechCheck( 0, 0, 0 ), scan.CheckOf( unported, context, 0 ), "a callback that is not ported scores 0 and leaves nothing behind" );
		// a callback that stores nothing leaves nothing of the earlier row's words
		scan.CheckOf( plain, context, 2 );
		var silent = new AdvisorScan( AdvisorDescriptorTable.Rows, new Dictionary<uint, AdvisorScoreCallback> { [unported.Callback] = _ => 55 } );
		Assert.AreEqual( new AdvisorSpeechCheck( 55, 0, 0 ), silent.CheckOf( unported, context, 0 ) );
	}

	[TestMethod]
	public void TheRestoredAdviceBypassKeepsTheWordsZero()
	{
		var stub = AdvisorDescriptorTable.Rows.First( row => row.Scan && row.Callback == RestoredAdviceScan.ConstantZeroCallback );
		var scan = new AdvisorScan( AdvisorDescriptorTable.Rows, new Dictionary<uint, AdvisorScoreCallback>(), _ => context => 42 );
		var context = new AdvisorScanContext( Balance(), new Money() );
		context.RememberThing( 3 );
		Assert.AreEqual( new AdvisorSpeechCheck( 42, 0, 0 ), scan.CheckOf( stub, context, 0 ), "[EXT:restored-advice] the extension's callback names no thing" );
	}

	// ------------------------------------------------------------------ the dirty toilets

	[TestMethod]
	public void TheDirtyToiletLinesNameTheDirtiestToiletNewestFirstAndKeepItWhenTheFilthIsLow()
	{
		var (park, state) = World();
		var oldest = park.RegisterExisting( 1402 );
		var middle = park.RegisterExisting( 1402 );
		var newest = park.RegisterExisting( 1402 );
		oldest.Repair = 20;   // filth 80
		middle.Repair = 20;   // filth 80: a tie, so the newer one stays (0x4CA220 replaces only a greater filth)
		newest.Repair = 60.5; // filth 40
		// 86 is the line without a handyman, filth band 60 to 90 (DirtyToilets1); the mean is (80 + 80 + 40) / 3 = 66
		var check = Check( AdvisorParkCallbacks.DirtyToilets1, state );
		Assert.AreEqual( 66, check.Score );
		Assert.AreEqual( (ushort)(middle.Id * 10), check.ThingId, "the first met of equal filth, newest first, is the middle one" );
		Assert.AreEqual( 0u, check.Secondary, "the toilet callbacks store no second word" );
		newest.Repair = 0;   // filth 100: strictly greater, so the newest takes over
		Assert.AreEqual( (ushort)(newest.Id * 10), Check( AdvisorParkCallbacks.DirtyToilets1, state ).ThingId );
	}

	[TestMethod]
	public void ALowFilthScoresNothingButStillLeavesTheToiletHandle()
	{
		var (park, state) = World();
		var toilet = park.RegisterExisting( 1402 );
		toilet.Repair = 70;   // filth 30, below every band
		var check = Check( AdvisorParkCallbacks.DirtyToilets1, state );
		Assert.AreEqual( 0, check.Score );
		Assert.AreEqual( (ushort)(toilet.Id * 10), check.ThingId, "0x59E4B2 stores the handle before the thresholds" );
		var high = Check( AdvisorParkCallbacks.DirtyToilets3, state );
		Assert.AreEqual( (0, (ushort)(toilet.Id * 10)), (high.Score, high.ThingId), "the line that needs 90 filth leaves it too" );
	}

	[TestMethod]
	public void TheToiletLinesLeaveNoWordWhenAnEarlierGateStopsThem()
	{
		var (park, state) = World();
		var toilet = park.RegisterExisting( 1402 );
		toilet.Repair = 0;
		Assert.AreEqual( (ushort)(toilet.Id * 10), Check( AdvisorParkCallbacks.DirtyToilets1, state ).ThingId );
		// a handyman in the park stops 86 and 88 (they are the lines for parks without one) before the dirtiest toilet is asked
		EconomyTestData.HireBest( park, StaffType.Handyman );
		var stopped = Check( AdvisorParkCallbacks.DirtyToilets1, state );
		Assert.AreEqual( (0, (ushort)0), (stopped.Score, stopped.ThingId) );
		Assert.AreEqual( (ushort)(toilet.Id * 10), Check( AdvisorParkCallbacks.DirtyToilets2, state ).ThingId, "87 is the line for parks with a handyman" );
		park.ClosePark();
		Assert.AreEqual( (0, (ushort)0), (Check( AdvisorParkCallbacks.DirtyToilets2, state ).Score, Check( AdvisorParkCallbacks.DirtyToilets2, state ).ThingId), "a closed park stops it first (0x59E462)" );
		park.OpenPark();
		toilet.Repair = 100;
		Assert.AreEqual( (ushort)(toilet.Id * 10), Check( AdvisorParkCallbacks.DirtyToilets2, state ).ThingId );
	}

	[TestMethod]
	public void TheDirtiestToiletHasNoThingWithoutAWorldAndNoneWithoutToilets()
	{
		var park = EconomyTestData.Park();
		park.OpenPark();
		var state = new EconomyParkState( park );
		Assert.IsNull( park.DirtiestToilet() );
		Assert.AreEqual( (ushort)0, state.DirtiestToiletThing );
		var toilet = park.RegisterExisting( 1402 );
		toilet.Repair = 0;
		Assert.AreSame( toilet, park.DirtiestToilet() );
		Assert.AreEqual( (ushort)0, state.DirtiestToiletThing, "no world names the object: the word stays 0" );
		var check = Check( AdvisorParkCallbacks.DirtyToilets3, state );
		Assert.AreEqual( (200, (ushort)0), (check.Score, check.ThingId), "the score does not depend on the world" );
	}

	// ------------------------------------------------------------------ the shops

	private static ParkObjectState Shop( ParkEconomy park, int days, long customers, params long[] walkAways )
	{
		var shop = park.RegisterExisting( 1203 );
		EconomyTestData.AdvanceToTurn( park, (long)days * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn );
		shop.CustomersRing = DailyRing.FromState( 0, false, 0, new[] { (int)customers } );
		shop.WalkAwayDays.Clear();
		shop.WalkAwayDays.AddRange( walkAways );
		return shop;
	}

	[TestMethod]
	public void AGreatShopLeavesItsHandleAndItsFigureEvenWhenTheScoreIsZero()
	{
		var (park, state) = World();
		var shop = park.RegisterExisting( 1203 );
		EconomyTestData.AdvanceToTurn( park, 40L * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn );
		shop.SatisfactionRing = DailyRing.FromState( 0, false, 0, new[] { 30 } );   // 50 + 30 = 80
		// [BIN:TPW-EXE:0x005A0150 great satisfaction] a figure above the threshold (63 for the drinks) stores the handle in +4 and the figure in +8, and returns (figure - threshold) x multiplier
		var check = Check( 0x5A0240, state );
		Assert.AreEqual( (5 * (80 - 63), (ushort)(shop.Id * 10), 80u), (check.Score, check.ThingId, check.Secondary) );
		// a figure at the threshold stores nothing (jle skips the stores)
		shop.SatisfactionRing = DailyRing.FromState( 0, false, 0, new[] { 13 } );
		Assert.AreEqual( new AdvisorSpeechCheck( 0, 0, 0 ), Check( 0x5A0240, state ) );
		// the multiplier may be 0 or negative: the words are written all the same
		var zero = new AdvisorScanContext( AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Sam.Replace( "DrinksGreatValue.ScorePerPoint\t2", "DrinksGreatValue.ScorePerPoint\t0" ) ), "test.sam" ), new Money(), state );
		shop.CustomersRing = DailyRing.FromState( 0, false, 0, new[] { 100 } );
		shop.WalkAwayDays.Clear();
		shop.WalkAwayDays.Add( 60 );   // value 60 > 20
		var great = Scan().CheckOf( RowOf( 0x5A00B0 ), zero, 0 );
		Assert.AreEqual( (0, (ushort)(shop.Id * 10), 60u), (great.Score, great.ThingId, great.Secondary), "a multiplier of 0 scores 0, and the handle and the figure are stored all the same" );
	}

	[TestMethod]
	public void APoorShopStoresTheSignedFigureAsAnUncheckedWord()
	{
		var (park, state) = World();
		var shop = park.RegisterExisting( 1203 );
		EconomyTestData.AdvanceToTurn( park, 40L * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn );
		shop.SatisfactionRing = DailyRing.FromState( 0, false, 0, new[] { -80 } );   // 50 - 80 = -30, below 40
		// [BIN:TPW-EXE:0x0059FD90 poor satisfaction] below the threshold the figure goes to +8 as it is, a negative one as a 32-bit word
		var check = Check( 0x59FEE0, state );
		Assert.AreEqual( (3 * (-30 - 40), (ushort)(shop.Id * 10), unchecked( (uint)-30 )), (check.Score, check.ThingId, check.Secondary) );
		Assert.AreEqual( 0xFFFFFFE2u, check.Secondary );
		shop.SatisfactionRing = DailyRing.FromState( 0, false, 0, new[] { -10 } );   // 40: at the threshold, jge skips
		Assert.AreEqual( new AdvisorSpeechCheck( 0, 0, 0 ), Check( 0x59FEE0, state ) );
	}

	[TestMethod]
	public void TheShopPicksTakeTheNewestOfEqualFiguresAndCarryTheThingId()
	{
		var (park, state) = World();
		var first = park.RegisterExisting( 1203 );
		var second = park.RegisterExisting( 1203 );
		var third = park.RegisterExisting( 1203 );
		EconomyTestData.AdvanceToTurn( park, 40L * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn );
		foreach ( var shop in new[] { first, second, third } )
			shop.SatisfactionRing = DailyRing.FromState( 0, false, 0, new[] { 20 } );
		third.SatisfactionRing = DailyRing.FromState( 0, false, 0, new[] { 10 } );
		Assert.AreEqual( new AdvisorShopPick( second.Id, 70, (ushort)(second.Id * 10) ), state.BestSatisfaction( AdvisorShopGroup.Drinks ), "of the two highest the newer one" );
		Assert.AreEqual( new AdvisorShopPick( third.Id, 60, (ushort)(third.Id * 10) ), state.WorstSatisfaction( AdvisorShopGroup.Drinks ) );
		// the callback asks the helper once: the word and the figure come from the one pick
		var check = Check( 0x5A0240, state );
		Assert.AreEqual( ((ushort)(second.Id * 10), 70u), (check.ThingId, check.Secondary) );
	}

	[TestMethod]
	public void AShopWithoutAWorldKeepsItsScoreAndLeavesOnlyTheFigure()
	{
		var park = EconomyTestData.Park();
		park.OpenPark();
		var state = new EconomyParkState( park );
		var shop = park.RegisterExisting( 1203 );
		EconomyTestData.AdvanceToTurn( park, 40L * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn );
		shop.SatisfactionRing = DailyRing.FromState( 0, false, 0, new[] { 30 } );
		var check = Check( 0x5A0240, state );
		Assert.AreEqual( (85, (ushort)0, 80u), (check.Score, check.ThingId, check.Secondary), "no world names the shop: the id word stays 0" );
	}

	// ------------------------------------------------------------------ the queue, the scariest ride and the bus

	[TestMethod]
	public void TheUnconnectedQueueLineStoresTheReturnedHandleBeforeItTestsIt()
	{
		var (park, state) = World();
		var ride = park.RegisterExisting( 1100 );
		park.FirstUnconnectedQueueRide = () => ride.Id;
		var check = Check( AdvisorInputsCallbacks.QueueUnconnected, state );
		Assert.AreEqual( (87, (ushort)(ride.Id * 10), 0u), (check.Score, check.ThingId, check.Secondary) );
		park.FirstUnconnectedQueueRide = () => 0;
		Assert.AreEqual( new AdvisorSpeechCheck( 0, 0, 0 ), Check( AdvisorInputsCallbacks.QueueUnconnected, state ), "none found: the handle is 0 and so is the score" );
		park.FirstUnconnectedQueueRide = null;
		Assert.AreEqual( new AdvisorSpeechCheck( 0, 0, 0 ), Check( AdvisorInputsCallbacks.QueueUnconnected, state ), "no world answers" );
		Assert.IsNull( state.UnconnectedQueue );
	}

	[TestMethod]
	public void TheScariestRideIsTheNewestOfTheStrictlyHighestExcitementBytes()
	{
		var (park, state) = World();
		var rides = new[] { park.RegisterExisting( 1100 ), park.RegisterExisting( 1100 ), park.RegisterExisting( 1100 ) };
		var excitement = new Dictionary<int, int> { [rides[0].Id] = 90, [rides[1].Id] = 90, [rides[2].Id] = 60 };
		park.RideLiveExcitement = instance => excitement.GetValueOrDefault( instance );
		// mean 80 > 70 and three rides >= 2
		var check = Check( AdvisorInputsCallbacks.VisitorsTooScared, state );
		Assert.AreEqual( (50, (ushort)(rides[1].Id * 10), 0u), (check.Score, check.ThingId, check.Secondary), "of two rides at 90 the newer one, 0x4CCD10 replaces only a greater byte" );
		excitement[rides[2].Id] = 91;
		park.RegisterExisting( 1100 );   // a fourth ride without excitement
		excitement[rides[0].Id] = 0x159;   // only the low byte counts: 0x59 = 89
		Assert.AreEqual( (ushort)(rides[2].Id * 10), Check( AdvisorInputsCallbacks.VisitorsTooScared, state ).ThingId );
		// the first ride is taken whatever its excitement
		var lone = EconomyTestData.Park();
		lone.OpenPark();
		var only = lone.RegisterExisting( 1100 );
		lone.ObjectThingId = instance => instance * 10;
		lone.RideLiveExcitement = _ => 0;
		Assert.AreEqual( only.Id, lone.ScariestRide(), "a handle of 0 means none yet, so the first ride is picked even at 0" );
	}

	[TestMethod]
	public void TheScariestRideIsAskedOnlyWhenTheAverageClearsItsThreshold()
	{
		AdvisorSpeechCheck Scary( int each )
		{
			var (park, state) = World();
			park.RegisterExisting( 1100 );
			park.RegisterExisting( 1100 );
			park.RideLiveExcitement = _ => each;
			return Check( AdvisorInputsCallbacks.VisitorsTooScared, state );
		}
		Assert.AreEqual( new AdvisorSpeechCheck( 0, 0, 0 ), Scary( 70 ), "an average of 70 is not above 70: nothing is stored" );
		Assert.AreNotEqual( (ushort)0, Scary( 71 ).ThingId );
		var few = EconomyTestData.Park();
		few.OpenPark();
		few.RegisterExisting( 1100 );
		few.RideLiveExcitement = _ => 99;
		Assert.AreEqual( new AdvisorSpeechCheck( 0, 0, 0 ), Check( AdvisorInputsCallbacks.VisitorsTooScared, new EconomyParkState( few ) ), "fewer rides than the minimum" );
	}

	[TestMethod]
	public void TheBigBusloadLineNamesTheSecondBusStopsCellWordZeroExtended()
	{
		var (park, state) = World( arrivals: 81 );
		park.ArrivalBusStopCell = () => 12 * 128 + 34 + 1;
		var check = Check( 0x59F440, state );
		Assert.AreEqual( (44, (ushort)0, (uint)(12 * 128 + 34 + 1)), (check.Score, check.ThingId, check.Secondary) );
		park.ArrivalBusStopCell = () => 0x12345;
		Assert.AreEqual( 0x2345u, Check( 0x59F440, state ).Secondary, "0x59F4D4 reads the word the helper stored (mov dx, word ptr)" );
		park.ArrivalBusStopCell = null;
		Assert.AreEqual( (44, 0u), (Check( 0x59F440, state ).Score, Check( 0x59F440, state ).Secondary), "a level without the stop stores no cell" );
		var exact = World( arrivals: 80 );
		exact.Park.ArrivalBusStopCell = () => 5;
		Assert.AreEqual( new AdvisorSpeechCheck( 0, 0, 0 ), Check( 0x59F440, exact.State ), "80 is not above 80: nothing is stored" );
		var none = World();
		none.Park.ArrivalBusStopCell = () => 5;
		Assert.AreEqual( new AdvisorSpeechCheck( 0, 0, 0 ), Check( 0x59F440, none.State ), "no arrival group yet" );
	}

	// ------------------------------------------------------------------ the speech and the tube

	[TestMethod]
	public void TheCaptionTagsOfTheNamingLinesHaveTheLocateActionTheirWordFeeds()
	{
		// the dirty toilets (86-89), the unconnected queue (113) and the scary rides (141) caption with a tag whose first action is the locate-thing button (3, word +0x1C = the id word);
		// the big busload (140) with the locate-cell button (4, dword +0x14 = the cell word)
		foreach ( var (callback, action) in new[] { (AdvisorParkCallbacks.DirtyToilets1, 3), (AdvisorParkCallbacks.DirtyToilets2, 3), (AdvisorParkCallbacks.DirtyToilets3, 3), (AdvisorParkCallbacks.DirtyToilets4, 3),
			(AdvisorInputsCallbacks.QueueUnconnected, 3), (AdvisorInputsCallbacks.VisitorsTooScared, 3), (0x59F440u, 4) } )
		{
			var row = RowOf( callback );
			for ( var response = row.FirstResponseId; response < row.FirstResponseId + row.ResponseCount; response++ )
				Assert.AreEqual( action, MessageTagTable.For( AdvisorCaptionTable.For( response )!.Value.Tag )!.Value.FirstAction, $"message {row.MessageId} response {response}" );
		}
	}

	/// <summary>A scan whose row 86's callback stores words, with the scanned message's own score check.</summary>
	private static (AdvisorController Controller, int Message) ControllerWithWords( Func<AdvisorScanContext, int> score )
	{
		var row = RowOf( AdvisorParkCallbacks.DirtyToilets1 );
		var controller = new AdvisorController( Balance(), new Dictionary<uint, AdvisorScoreCallback> { [row.Callback] = context => score( context ) } );
		return (controller, row.MessageId);
	}

	private static AdvisorPendingAdvice? PendingOf( AdvisorController controller, int message ) =>
		Enumerable.Range( 0, AdvisorScoreQueue.Capacity ).Select( controller.Queue.GetSlot ).FirstOrDefault( slot => slot?.MessageId == message );

	[TestMethod]
	public void TheScansOwnEnqueueBuildsTheRecordWithBothWordsZero()
	{
		var row = RowOf( AdvisorParkCallbacks.DirtyToilets1 );
		var (controller, message) = ControllerWithWords( context => { context.RememberThing( 9 ); context.RememberSecondary( 77 ); return 100; } );
		var seen = 0;
		for ( var call = 0; call < AdvisorScan.DescriptorCount * 2 && PendingOf( controller, message ) == null; call++, seen++ )
			controller.ScanOnce( new Money(), 100, 0, true );
		// [BIN:TPW-EXE:0x0059A5FD scan enqueue] the record is built with the arguments -1, 0, -1, 0 and the message: both words are 0, whatever the callback left in the scan object
		Assert.AreEqual( new AdvisorPendingAdvice( row.MessageId, 100 ), PendingOf( controller, message ), "queued by the scan with no id and no cell word" );
	}

	[TestMethod]
	public void TheSpeechCopiesTheCallbacksNonzeroWordsOverTheRecordsAndKeepsTheOthers()
	{
		var thing = (ushort)9;
		var cell = 0u;
		var (controller, message) = ControllerWithWords( context => { if ( thing != 0 ) context.RememberThing( thing ); if ( cell != 0 ) context.RememberSecondary( cell ); return 100; } );
		for ( var call = 0; call < AdvisorScan.DescriptorCount * 2 && controller.LastScan == null; call++ )
			controller.ScanOnce( new Money(), 100, 0, true );
		Assert.IsNotNull( controller.LastScan );
		var said = new List<AdvisorResponseSelection>();
		AdvisorResponseSelection? Speak( AdvisorPendingAdvice record, uint clock )
		{
			controller.Queue.Restore( new AdvisorPendingAdvice?[AdvisorScoreQueue.Capacity], new Dictionary<int, AdvisorMessageHistory>() );
			controller.Queue.Enqueue( record, 0, true );
			return controller.UpdateSpeaking( () => clock, () => clock / 4, selection => { said.Add( selection ); return 500u; }, controller.SpeechCheck );
		}
		// the record carries a cell word of its own; the callback gives only the thing: that replaces the id word and leaves the cell word
		Speak( new AdvisorPendingAdvice( message, 100, ThingId: 3, Secondary: 4242 ), 1000 );
		Assert.AreEqual( (9, 4242u), (said[^1].Advice.ThingId, said[^1].Advice.Secondary) );
		// the callback gives only a cell: it replaces the second word and leaves the id word
		thing = 0;
		cell = 0xFFFFFFFEu;
		Speak( new AdvisorPendingAdvice( message, 100, ThingId: 3, Secondary: 4242 ), 100_000 );
		Assert.AreEqual( (3, 0xFFFFFFFEu), (said[^1].Advice.ThingId, said[^1].Advice.Secondary) );
		// nothing stored: the record's words are said as they were
		cell = 0;
		Speak( new AdvisorPendingAdvice( message, 100, ThingId: 3, Secondary: 4242 ), 200_000 );
		Assert.AreEqual( (3, 4242u), (said[^1].Advice.ThingId, said[^1].Advice.Secondary) );
		// a score below the minimum drops the record unspoken even when the words are set
		thing = 9;
		cell = 5;
		var before = said.Count;
		var (low, lowMessage) = ControllerWithWords( context => { context.RememberThing( 9 ); return 24; } );
		low.ScanOnce( new Money(), 100, 0, true );
		for ( var call = 0; call < AdvisorScan.DescriptorCount * 2 && low.LastScan == null; call++ )
			low.ScanOnce( new Money(), 100, 0, true );
		low.Queue.Enqueue( new AdvisorPendingAdvice( lowMessage, 100 ), 0, true );
		low.UpdateSpeaking( () => 1000u, () => 250u, selection => { said.Add( selection ); return 1u; }, low.SpeechCheck );
		Assert.AreEqual( before, said.Count, "score 24 is below the minimum 25 whatever the callback stored" );
		Assert.AreEqual( 24, low.SpeechCheck( new AdvisorPendingAdvice( lowMessage, 100 ) )!.Value.Score );
		Assert.AreEqual( (ushort)9, low.SpeechCheck( new AdvisorPendingAdvice( lowMessage, 100 ) )!.Value.ThingId, "the words are there for a low score too" );
	}

	[TestMethod]
	public void ARecordsWordsReachTheCaptionSinkAndSurviveASave()
	{
		var (controller, message) = ControllerWithWords( context => { context.RememberThing( 9 ); context.RememberSecondary( 0x30002 ); return 100; } );
		var advisor = new AutomaticAdvisor( controller, () => 0L );
		var sunk = new List<(int Tag, ushort Thing, uint Secondary)>();
		advisor.CaptionSink = ( tag, _, _, thing, secondary ) => sunk.Add( (tag, thing, secondary) );
		for ( var call = 0; call < AdvisorScan.DescriptorCount * 2 && controller.LastScan == null; call++ )
			controller.ScanOnce( new Money(), 100, 0, true );
		controller.Queue.Enqueue( new AdvisorPendingAdvice( message, 100 ), 0, true );
		var spoken = controller.UpdateSpeaking( () => 1000u, () => 250u, selection => { advisor.PostCaption( selection.ResponseId, selection.Advice ); return 1u; }, controller.SpeechCheck );
		Assert.IsNotNull( spoken );
		// message 86's responses 235 and 236 caption with tag 88 (no arguments, the locate-thing action 3)
		Assert.AreEqual( new AdvisorCaption( 88, AdvisorCaptionTable.For( spoken.Value.ResponseId )!.Value.TextEntry ), AdvisorCaptionTable.For( spoken.Value.ResponseId ) );
		CollectionAssert.AreEqual( new[] { (88, (ushort)9, 0x30002u) }, sunk, "the sink gets the record's words (the formatter posts them with the tag, 0x59B7C2-0x59B7D4)" );
		// the record the sink saw is the one a park save keeps
		controller.Queue.Enqueue( new AdvisorPendingAdvice( message, 100, ThingId: 9, Secondary: 0x30002 ), 100_000, true );
		var restored = new AdvisorController( Balance() );
		restored.RestoreSnapshot( controller.CaptureSnapshot() );
		Assert.AreEqual( new AdvisorPendingAdvice( message, 100, -1, false, 9, 0x30002 ), PendingOf( restored, message ) );
	}
}
