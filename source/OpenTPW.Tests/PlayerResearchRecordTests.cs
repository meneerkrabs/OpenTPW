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
