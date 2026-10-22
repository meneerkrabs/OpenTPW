using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The player's record list of the parks whose research is complete (tp.exe's per-theme mAllResearchCompleted records, 0x5B0CA0 and 0x5B0C40), kept in the own progress file.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PlayerResearchRecordTests
{
	private string root = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Initialize() { root = Directory.CreateTempSubdirectory( "opentpw-research-records-" ).FullName; previousLog = Log; Log = new(); }

	[TestCleanup]
	public void Cleanup() { Log = previousLog; Directory.Delete( root, true ); }

	private string PathName => Path.Combine( root, PlayerTicketFile.FileName );

	[TestMethod]
	public void EveryParkMustBeRecordedAndTheCurrentOneCountsAsItsCompletionIsRecorded()
	{
		var ledger = new PlayerTicketLedger();
		Assert.IsFalse( ledger.ResearchCompletedEverywhereWith( "jungle" ) );
		Assert.IsTrue( ledger.RecordResearchCompleted( "Jungle" ), "theme names are matched without case and stored canonically" );
		Assert.IsFalse( ledger.RecordResearchCompleted( "jungle" ), "a record set once stays set" );
		CollectionAssert.AreEqual( new[] { "jungle" }, ledger.ResearchCompleted.ToArray() );
		Assert.IsTrue( ledger.RecordResearchCompleted( "hallow" ) && ledger.RecordResearchCompleted( "fantasy" ) );
		Assert.IsFalse( ledger.ResearchCompletedEverywhereWith( "jungle" ), "the space record is still clear" );
		Assert.IsTrue( ledger.ResearchCompletedEverywhereWith( "space" ), "the park whose research just completed counts before its record is written" );
		Assert.ThrowsException<InvalidDataException>( () => ledger.RecordResearchCompleted( "lobby" ) );
	}

	[TestMethod]
	public void TheRecordsRoundTripThroughTheOwnFileInThemeOrder()
	{
		var ledger = new PlayerTicketLedger();
		ledger.RecordResearchCompleted( "space" ); ledger.RecordResearchCompleted( "jungle" );
		PlayerTicketFile.Save( PathName, ledger.Capture() );
		var restored = PlayerTicketFile.LoadIfPresent( PathName )!;
		CollectionAssert.AreEqual( new[] { "jungle", "space" }, restored.Capture().ResearchCompleted!.ToArray() );
		StringAssert.Contains( File.ReadAllText( PathName ), "\"ResearchCompleted\":[\"jungle\",\"space\"]" );
	}

	[TestMethod]
	public void OlderFilesWithoutRecordsLoadWithNoneAndAFileWithoutRecordsDoesNotWriteTheField()
	{
		var older = "{\"Format\":\"opentpw-player-progress\",\"Version\":1,\"Mode\":\"FullSimulation\",\"ExtraKeys\":1,\"SpentTickets\":0,\"Earned\":[]}";
		var ledger = PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( older ) );
		Assert.AreEqual( 0, ledger.ResearchCompleted.Count );
		Assert.IsNull( ledger.Capture().ResearchCompleted );
		Assert.IsFalse( Encoding.UTF8.GetString( PlayerTicketFile.Serialize( ledger.Capture() ) ).Contains( "ResearchCompleted" ) );
	}

	[DataTestMethod]
	[DataRow( "[\"lobby\"]" )]
	[DataRow( "[\"Jungle\"]" )]
	[DataRow( "[\"jungle\",\"jungle\"]" )]
	[DataRow( "[null]" )]
	[DataRow( "[\"jungle\",\"hallow\",\"fantasy\",\"space\",\"jungle\"]" )]
	public void InvalidRecordsAreRefused( string records )
	{
		var text = "{\"Format\":\"opentpw-player-progress\",\"Version\":1,\"Mode\":\"FullSimulation\",\"ExtraKeys\":1,\"SpentTickets\":0,\"Earned\":[],\"ResearchCompleted\":" + records + "}";
		Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( text ) ) );
	}

	[TestMethod]
	public void InstantActionMarksAnExistingFileOnlyAndNeverCreatesOne()
	{
		var active = true;
		var records = new GameFlow.InstantActionResearchRecords( PathName, () => active );
		records.RecordCompleted( "space" );
		Assert.IsFalse( File.Exists( PathName ), "without a progress file there is no player to record against" );
		Assert.IsFalse( records.CompletedEverywhereWith( "space" ), "an Instant Action park never reads the records" );

		var start = new PlayerTicketLedger(); start.RecordEarned( "jungle", GoldenTicketKind.Visitors );
		PlayerTicketFile.Save( PathName, start.Capture() );
		records.RecordCompleted( "Space" );
		var saved = PlayerTicketFile.LoadIfPresent( PathName )!;
		CollectionAssert.AreEqual( new[] { "space" }, saved.ResearchCompleted.ToArray() );
		Assert.AreEqual( 1, saved.EarnedTickets, "the rest of the file is kept" );

		active = false; // a park in a co-op session
		var before = File.ReadAllBytes( PathName );
		records.RecordCompleted( "jungle" );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( PathName ), "a co-op park leaves the file untouched" );
	}

	[TestMethod]
	public void ACorruptFileOnlyWarnsAndStaysAsItWas()
	{
		var messages = new System.Collections.Generic.List<string>();
		var previous = Logger.OnLog;
		Logger.OnLog += ( severity, text ) => messages.Add( text );
		try
		{
			File.WriteAllText( PathName, "{ not json" );
			new GameFlow.InstantActionResearchRecords( PathName, () => true ).RecordCompleted( "hallow" );
			Assert.AreEqual( "{ not json", File.ReadAllText( PathName ) );
			Assert.IsTrue( messages.Any( text => text.Contains( "research record of hallow was not saved" ) ), string.Join( " | ", messages ) );
		}
		finally { Logger.OnLog = previous; }
	}

	[TestMethod]
	public void ATicketlessThemeStillSavesItsResearchRecord()
	{
		PlayerTicketFile.Save( PathName, new PlayerTicketLedger().Capture() );
		var settings = new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( EconomyTestData.StandardText( 50000 ), "Standard.sam" ) } ),
			new SamSettings( new[] { SamDocument.Parse( EconomyTestData.ChallengesText, "Challenges.sam" ) } ),
			new SamSettings( new[] { SamDocument.Parse( "Tickets.CanEarnTicketsInTheme 0\nTickets.CanSpendTicketsInTheme 1\nKeys.CostToEnter 1\n", "global.sam" ) } ), "jungle", false );
		Assert.IsFalse( settings.CanEarnTickets );
		var runtime = ParkEconomyRuntime.ForResume( new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 ), ParkStartKind.FullSimulation );
		using var owner = new PlayerProgressRuntimeOwner( PathName, "jungle", PlayerTicketFile.ReadIfPresent( PathName )!, runtime, () => true, _ => Assert.Fail() );
		var before = File.ReadAllBytes( PathName );
		Assert.IsTrue( owner.TryFlush() ); CollectionAssert.AreEqual( before, File.ReadAllBytes( PathName ), "the ticket owner leaves a ticketless theme's file alone" );
		owner.RecordResearchCompleted();
		CollectionAssert.AreEqual( new[] { "jungle" }, PlayerTicketFile.LoadIfPresent( PathName )!.ResearchCompleted.ToArray(), "0x5B0CA0 checks no ticket setting" );
		Assert.IsFalse( owner.Dirty ); Assert.IsNull( owner.Error );
	}

	[TestMethod]
	public void TheFullSimulationOwnerRecordsAndSavesItsParkAndAnswersForEveryPark()
	{
		var start = new PlayerTicketLedger();
		foreach ( var theme in new[] { "hallow", "fantasy", "space" } ) start.RecordResearchCompleted( theme );
		PlayerTicketFile.Save( PathName, start.Capture() );
		var runtime = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
		var shared = false;
		using var owner = new PlayerProgressRuntimeOwner( PathName, "jungle", PlayerTicketFile.ReadIfPresent( PathName )!, runtime, () => true, _ => Assert.Fail(), () => shared );
		Assert.IsTrue( owner.ResearchCompletedEverywhere );
		shared = true;
		Assert.IsFalse( owner.ResearchCompletedEverywhere, "a park in a co-op session leaves the player's records alone" );
		owner.RecordResearchCompleted();
		Assert.AreEqual( 3, PlayerTicketFile.LoadIfPresent( PathName )!.ResearchCompleted.Count );
		shared = false;
		owner.Pump(); // the reclaim after the session
		owner.RecordResearchCompleted();
		CollectionAssert.AreEquivalent( new[] { "jungle", "hallow", "fantasy", "space" }, PlayerTicketFile.LoadIfPresent( PathName )!.ResearchCompleted.ToArray() );
		Assert.IsFalse( owner.Dirty );
	}
}
