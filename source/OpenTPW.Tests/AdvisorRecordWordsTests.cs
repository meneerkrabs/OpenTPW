using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// GUEST-007 / ADVISOR-009: the two words of an advice record (id word +0x0 and cell dword +0x4 of tp.exe's 0x59B590 record) are kept through the queue, a park save and an
/// original save, the producers fill them as the original's call sites do, the speech scores a scanned message again (0x59B6BE), and the caption formatter (0x509E10) reads
/// them as the arguments of the texts with parameter slots. GPU-free.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorRecordWordsTests
{
	private const string Sam = """
		GeneralAdvisor.MinTimeAnyMessage	5
		GeneralAdvisor.MinTimeSameMessage	120
		GeneralAdvisor.MinScoreForConsideration	25
		MessageGroups[0].MinTimeSameMessage		0
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
		RidesBreakdownImminent.Score	105
		RidesBreakdownNoMechanics.Score	106
		RidesBreakdownBusyMechanics.Score	107
		RidesBreakdownMechanicOnIt.Score	108
		ResearchRideResearched.Score	55
		PrankSbomb.Score	60
		PrankLitter.Score	61
		PrankBalloon.Score	62
		""";

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static AdvisorController Controller() => new( AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Sam ), "test.sam" ) );

	private static AdvisorPendingAdvice? PendingOf( AdvisorController controller, int message ) =>
		Enumerable.Range( 0, AdvisorScoreQueue.Capacity ).Select( controller.Queue.GetSlot ).FirstOrDefault( slot => slot?.MessageId == message );

	private static AutomaticAdvisor Attached( out ParkEconomy economy )
	{
		var advisor = new AutomaticAdvisor( Controller(), () => 0L );
		economy = EconomyTestData.Park();
		advisor.AttachEconomy( economy );
		return advisor;
	}

	private static ParkEvent Event( ParkEventKind kind, long amount = 0, int instance = 0, int info = 0, string detail = "" ) => new( 0, kind, instance, info, amount, detail );

	[TestMethod]
	public void TheCaptionTableCarriesTheFormatterCaseOfEveryRow()
	{
		// [BIN:TPW-EXE:0x00509E10 caption formatter] the advisor's 353 captions use the cases 0, 2 and 5 to 8 only; the 36 texts with parameter slots are 194-206 and 248-256 (the ride and research lines, case 2), 434-463's ticket wins (cases 5 to 8) and 316/317 (case 0, whose slot nothing fills)
		var table = AdvisorCaptionTable.Table;
		CollectionAssert.IsSubsetOf( table.Values.Select( caption => caption.Kind ).Distinct().ToArray(), new[] { 0, 2, 5, 6, 7, 8 } );
		foreach ( var response in Enumerable.Range( 194, 13 ).Concat( Enumerable.Range( 248, 9 ) ) )
			Assert.AreEqual( 2, table[response].Kind, $"response {response}" );
		foreach ( var (response, kind) in new[] { (434, 5), (435, 6), (438, 7), (439, 8), (446, 5), (451, 8), (458, 5), (463, 8) } )
			Assert.AreEqual( kind, table[response].Kind, $"response {response}" );
		Assert.AreEqual( 0, table[316].Kind );
		Assert.AreEqual( 0, table[274].Kind, "the bank's plain captions" );
	}

	[TestMethod]
	public void TheCaptionTableReadsAndRefusesTheKindColumn()
	{
		Assert.AreEqual( new AdvisorCaption( 68, 71, 2 ), AdvisorCaptionTable.Parse( "captions = [\n  { response = 194, tag = 68, text = 71, kind = 2 },\n]\n" )[194] );
		Assert.AreEqual( new AdvisorCaption( 4, 8 ), AdvisorCaptionTable.Parse( "captions = [\n  { response = 5, tag = 4, text = 8 },\n]\n" )[5] );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorCaptionTable.Parse( "captions = [\n  { response = 5, tag = 4, text = 8, kind = },\n]\n" ) );
	}

	[TestMethod]
	public void TheFormatterNamesTheObjectTypeOfTheSecondWord()
	{
		string? Names( int id ) => id == 1100 ? "Belly Bounce" : null;
		var ride = new AdvisorPendingAdvice( 66, 105, ThingId: 17, Secondary: 1100 );
		Assert.AreEqual( "Belly Bounce", AdvisorCaptionArguments.For( AdvisorCaptionArguments.ObjectName, ride, Names, null )![0] );
		// only the low word is the id (0x509F09 reads ax), and an unknown id names nothing
		Assert.AreEqual( "Belly Bounce", AdvisorCaptionArguments.For( 2, ride with { Secondary = 0x10000 + 1100 }, Names, null )![0] );
		Assert.AreEqual( "", AdvisorCaptionArguments.For( 2, ride with { Secondary = 7 }, Names, null )![0] );
		Assert.IsNull( AdvisorCaptionArguments.For( 2, ride with { Secondary = 0 }, Names, null ), "an id of 0 makes the formatter write nothing (0x509F15)" );
	}

	[TestMethod]
	public void TheTicketCasesTakeTheThresholdsOfTheParksTickets()
	{
		var tickets = new GoldenTicketSettings( Visitors: 40000, PeopleInPark: 700, Happiness: 90, AtLeastThisManyHappyPeople: 50, ProfitYear: 250000, RecentVisitors: 9000, RecentVisitorMonths: 5, CoasterHeight: 0, GokartExcitement: 0, WaterLength: 0, MinCellsOwned: 0, MinCellsCovered: 0 );
		var record = new AdvisorPendingAdvice( 215, 1 );
		string? Nobody( int _ ) => null;
		Assert.AreEqual( "40000", AdvisorCaptionArguments.For( 5, record, Nobody, tickets )![1] );
		Assert.AreEqual( "700", AdvisorCaptionArguments.For( 6, record, Nobody, tickets )![1] );
		Assert.AreEqual( "250000", AdvisorCaptionArguments.For( 7, record, Nobody, tickets )![1] );
		var recent = AdvisorCaptionArguments.For( 8, record, Nobody, tickets )!;
		Assert.AreEqual( "9000", recent[1], "slot 1 of case 8 is the visitors (0x7857A4)" );
		Assert.AreEqual( "5", recent[2], "slot 2 is the months (0x7857A8)" );
		Assert.IsNull( AdvisorCaptionArguments.For( 5, record, Nobody, null ), "without a park there are no thresholds" );
		Assert.AreEqual( 0, AdvisorCaptionArguments.For( 0, record, Nobody, tickets )!.Count, "case 0 takes no arguments" );
	}

	[TestMethod]
	public void TheTextsGetTheirArgumentsByTheTypeOfEachSlot()
	{
		var template = new StringTemplate( new[]
		{
			new StringTemplatePart( "won for ", -1 ), new StringTemplatePart( "", 1 ), new StringTemplatePart( " visitors in ", -1 ), new StringTemplatePart( "", 2 ), new StringTemplatePart( " months", -1 ),
		} );
		Assert.AreEqual( "won for 9000 visitors in 5 months", template.FillByType( new Dictionary<int, string> { [2] = "5", [1] = "9000" } ) );
		Assert.AreEqual( "won for  visitors in 5 months", template.FillByType( new Dictionary<int, string> { [2] = "5" } ), "a slot without a value stays empty" );
	}

	[TestMethod]
	public void RideAndResearchEventsPostTheirWords()
	{
		var advisor = Attached( out _ );
		var controller = advisor.Controller;
		advisor.OnParkEvent( Event( ParkEventKind.RideBreakdownImminent, instance: 12, info: 1100 ) );
		// [BIN:TPW-EXE:0x004DF8BE ride breakdown imminent] the record's words are the ride's own word and its type id
		Assert.AreEqual( new AdvisorPendingAdvice( 66, 105, -1, false, 12, 1100 ), PendingOf( controller, 66 ) );
		advisor.OnMechanicOnTheWay( 12, 3 );
		Assert.IsNull( PendingOf( controller, 70 ), "without a level the mechanic message is not posted" );
	}

	[TestMethod]
	public void APrankQueuesTheLineWithThePranksterAndItsCellWord()
	{
		var advisor = Attached( out _ );
		// [BIN:TPW-EXE:0x0059B25F prank advice] the thing's id and the cell word y x 128 + x + 1 of its +5 and +7 bytes: guest 4 (4 mod 3 = 1, the litter line) in the cell (9, 3)
		Assert.IsNotNull( advisor.PostPrankAdvice( 4, 9, 3, GuestDecision.Prank.Litter ) );
		Assert.AreEqual( new AdvisorPendingAdvice( 126, 61, -1, false, 4, 3 * 128 + 9 + 1 ), PendingOf( advisor.Controller, 126 ) );
	}

	[TestMethod]
	public void ASavedQueueKeepsTheWordsAndWritesThemOnlyWhenTheyAreNotZero()
	{
		var controller = Controller();
		Assert.IsNotNull( controller.Advise( 66, 0, 0, true, thingId: 12, secondary: 1100 ) );
		Assert.IsNotNull( controller.Advise( 68, 0, 0, true ) );
		var snapshot = controller.CaptureSnapshot();
		var withWords = snapshot.Queue.Slots.Single( slot => slot?.MessageId == 66 )!;
		Assert.AreEqual( 12, withWords.ThingId );
		Assert.AreEqual( 1100u, withWords.Secondary );
		var json = JsonSerializer.Serialize( snapshot.Queue.Slots.Where( slot => slot != null ).ToArray() );
		StringAssert.Contains( json, "\"ThingId\":12" );
		StringAssert.Contains( json, "\"Secondary\":1100" );
		var plain = JsonSerializer.Serialize( snapshot.Queue.Slots.Single( slot => slot?.MessageId == 68 ) );
		Assert.IsFalse( plain.Contains( "ThingId" ) || plain.Contains( "Secondary" ) || plain.Contains( "ScoreOverridden" ), "a record without the words writes none: " + plain );
		// a save without the fields reads as before, and a restored controller keeps the words
		var old = JsonSerializer.Deserialize<AdvisorPendingAdviceSnapshot>( "{\"MessageId\":68,\"Score\":106,\"ResponseVariantOverride\":-1,\"OverrideOnlyOnce\":false}" )!;
		Assert.AreEqual( new AdvisorPendingAdviceSnapshot( 68, 106, -1, false ), old );
		var restored = Controller();
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( new AdvisorPendingAdvice( 66, 105, -1, false, 12, 1100 ), PendingOf( restored, 66 ) );
		// a thing id that is not a word is refused
		var bad = snapshot with { Queue = snapshot.Queue with { Slots = snapshot.Queue.Slots.Select( slot => slot is { MessageId: 66 } ? slot with { ThingId = 70000 } : slot ).ToArray() } };
		Assert.ThrowsException<InvalidDataException>( () => Controller().ValidateSnapshot( bad ) );
	}

	[TestMethod]
	public void TheSpeechScoresAScannedMessageAgainAndDropsItBelowTheMinimum()
	{
		var controller = Controller();
		controller.Queue.Enqueue( new AdvisorPendingAdvice( 1, 500 ), 0, true );
		var said = new List<AdvisorResponseSelection>();
		// [BIN:TPW-EXE:0x0059B6C3 speech score] a score below MinScoreForConsideration (25) says nothing; one equal to it passes (jl)
		var low = controller.UpdateSpeaking( () => 1000u, () => 300u, selection => { said.Add( selection ); return 500u; }, _ => new AdvisorSpeechCheck( 24 ) );
		Assert.IsNotNull( low );
		Assert.AreEqual( 0, said.Count, "nothing is said" );
		Assert.AreEqual( 0, controller.Queue.Count, "the record is gone" );
		Assert.AreEqual( AdvisorMessageHistory.Empty, controller.Queue.GetHistory( 1 ), "no history follows" );
		Assert.AreEqual( 0u, controller.Queue.LastActionDuration, "and no busy time" );
		controller.Queue.Enqueue( new AdvisorPendingAdvice( 1, 500 ), 0, true );
		controller.UpdateSpeaking( () => 1000u, () => 300u, selection => { said.Add( selection ); return 500u; }, _ => new AdvisorSpeechCheck( 25, 9, 4242 ) );
		Assert.AreEqual( 1, said.Count );
		Assert.AreEqual( (ushort)9, said[0].Advice.ThingId, "the callback's words replace the record's" );
		Assert.AreEqual( 4242u, said[0].Advice.Secondary );
		Assert.IsTrue( controller.Queue.GetHistory( 1 ).HasBeenPlayed );
		// a record offered again with a score of its own (the F1 replay) is not scored again
		controller.Queue.Enqueue( new AdvisorPendingAdvice( 2, 500, ScoreOverridden: true ), 0, true );
		var checks = 0;
		controller.UpdateSpeaking( () => 100_000u, () => 600u, selection => { said.Add( selection ); return 500u; }, _ => { checks++; return new AdvisorSpeechCheck( -1 ); } );
		Assert.AreEqual( 0, checks );
		Assert.AreEqual( 2, said.Count );
	}

	[TestMethod]
	public void TheControllerChecksOnlyTheMessagesTheScanScores()
	{
		var controller = Controller();
		Assert.IsNull( controller.SpeechCheck( new AdvisorPendingAdvice( 1, 500 ) ), "no scan has run yet" );
		controller.ScanOnce( null, 0, 0, true );
		// 66 is posted by game code (its row is not scanned): the record's own score stands; 1 is a scanned row
		Assert.IsNull( controller.SpeechCheck( new AdvisorPendingAdvice( 66, 105 ) ) );
		var scanned = Enumerable.Range( 0, AdvisorScan.DescriptorCount ).First( id => AdvisorDescriptorTable.Rows[id].Scan && AdvisorDescriptorTable.Rows[id].Callback != RestoredAdviceScan.ConstantZeroCallback );
		Assert.IsNotNull( controller.SpeechCheck( new AdvisorPendingAdvice( scanned, 500 ) ) );
		var zero = Enumerable.Range( 0, AdvisorScan.DescriptorCount ).First( id => AdvisorDescriptorTable.Rows[id].Scan && AdvisorDescriptorTable.Rows[id].Callback == RestoredAdviceScan.ConstantZeroCallback );
		Assert.IsNull( controller.SpeechCheck( new AdvisorPendingAdvice( zero, 500 ) ), "the restored-advice extension's lines keep their own scores" );
	}

	[TestMethod]
	public void TheF1ReplayMarksTheRecordScoreOverridden()
	{
		var controller = Controller();
		controller.Queue.Enqueue( new AdvisorPendingAdvice( 188, 1000 ), 0, true );
		var replay = controller.Queue.ReplayLastTutorial( 1500, 10, true );
		Assert.IsNotNull( replay );
		Assert.IsTrue( controller.Queue.LastTutorial!.ScoreOverridden, "byte +0x16 (0x59AB50 stores 1 at +0x16E6)" );
	}
}
