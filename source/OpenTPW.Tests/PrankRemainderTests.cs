using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The prank message's advisor lines (tp.exe 0x004FF2EA, 0x0059B060) and the guard's set-down with its drawn way out (0x004FEB50, 0x004D86D0).
/// </summary>
[TestClass]
public class PrankRemainderTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private const string Settings = """
		GeneralAdvisor.MinTimeAnyMessage	7
		GeneralAdvisor.MinTimeSameMessage	99
		GeneralAdvisor.MinScoreForConsideration	30
		MessageGroups[0].MinTimeSameMessage		10
		MessageGroups[0].SayOnlyOnce			0
		MessageGroups[0].DiscardAfterSlaps		0
		MessageGroups[1].MinTimeSameMessage		0
		MessageGroups[1].SayOnlyOnce			1
		MessageGroups[1].DiscardAfterSlaps		2
		Welcome.Score 9000
		Bankrupted.Score	8000
		PrankSbomb.Score									35
		PrankLitter.Score									35
		PrankBalloon.Score									35
		ParkNowOpen.Score	50
		ParkNowClosed.Score	51
		PrebuiltPark.Score	700
		""";

	[TestMethod]
	public void ThePrankLineFollowsThePranksterIdNotThePrank()
	{
		Assert.AreEqual( 125, AdvisorTables.PrankAdvice( 3 ) );
		Assert.AreEqual( 126, AdvisorTables.PrankAdvice( 4 ) );
		Assert.AreEqual( 127, AdvisorTables.PrankAdvice( 5 ) );
		Assert.AreEqual( 125, AdvisorTables.PrankAdvice( 0x10002 + 1 ), "the id is a 16-bit word" );
	}

	[TestMethod]
	public void ThePrankDescriptorsAreTheWindowsTablesRows()
	{
		var rows = AdvisorTables.Messages.Where( message => message.MessageId is >= 125 and <= 127 ).OrderBy( message => message.MessageId ).ToArray();
		CollectionAssert.AreEqual( new[] { (302, 2, 255), (304, 2, 1), (306, 2, 255) }, rows.Select( row => (row.FirstResponseId, row.ResponseCount, (int)row.MaximumPendingDuplicates) ).ToArray() );
		Assert.IsTrue( rows.All( row => row.Group == 0 && row.GameMode == 2 ) );
		Assert.IsTrue( Enumerable.Range( 302, 6 ).All( AdvisorResponses.Table.ContainsKey ), "the six prank responses are in the shipped table" );
	}

	[TestMethod]
	public void APrankLineIsAdmittedWithItsScore()
	{
		var balance = AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Settings ), "test.sam" );
		Assert.AreEqual( 35, balance.ScoreOf( 126 ) );
		var controller = new AdvisorController( balance );
		var admission = controller.Advise( AdvisorTables.PrankAdvice( 4 ), 1000, 0, true );
		Assert.IsNotNull( admission );
		Assert.IsTrue( admission.Value.Stored, admission.Value.Eligibility.ToString() );
	}

	[TestMethod]
	public void SettingsWithoutPrankScoresLeaveThePranksSilent()
	{
		var text = string.Join( "\n", Settings.Split( '\n' ).Where( line => !line.Contains( "Prank" ) ) );
		var balance = AdvisorBalance.Parse( Encoding.ASCII.GetBytes( text ), "test.sam" );
		Assert.AreEqual( 0, balance.ScoreOf( 125 ) );
		Assert.IsTrue( balance.ScoreOf( 125 ) <= balance.MinimumScore, "a score of 0 never beats the minimum, so the selection never says it" );
	}

	private static GuestSimulation Simulation( GuestPathGrid grid ) => new( grid, GuestTests.CreateSettings(), 5 ) { ArrivalsEnabled = false };

	[TestMethod]
	public void TheSetDownPutsTheGuestAtTheCellsCentreAndKeepsTheDrawnSide()
	{
		var simulation = Simulation( GuestTests.CreateCross() );
		var guest = simulation.SpawnInPark( 4, 5 );
		IGuardGuests guards = simulation;
		guards.Hold( guest.Id );
		Assert.IsTrue( guards.Release( guest.Id, (4, 5), 1 ) );
		Assert.AreEqual( GuestNavigation.Centre( (4, 5) ), (guest.Navigation.X, guest.Navigation.Y), "0x004FA930 with the bytes 0x80, 0x80" );
		Assert.AreEqual( (GuestState.LeavingPark, 1), (guest.State, guest.ExitSide) );
		var snapshot = simulation.CaptureSnapshot();
		var resumed = Simulation( GuestTests.CreateCross() );
		resumed.RestoreSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) ) );
		Assert.AreEqual( 1, resumed.Find( guest.Id )!.ExitSide, "the drawn side is saved" );
		// with no recall (no exit counter left) the guest walks to lane B's park entrance (19, 5), not to the nearer lane A at (0, 5)
		guest.ExitLevel = 0;
		var furthestLeft = guest.CellX;
		for ( var tick = 0; tick < 60 * 60 && guest.Lane == null && guest.State == GuestState.LeavingPark; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			furthestLeft = Math.Min( furthestLeft, guest.CellX );
		}
		Assert.IsNotNull( guest.Lane, "it reached a lane" );
		Assert.AreEqual( (19, 5), guest.Lane![^1], "lane B" );
		Assert.AreEqual( 4, furthestLeft, "it never walked towards lane A" );
		Assert.IsNull( guest.ExitSide, "the lane walk clears it" );
	}

	[TestMethod]
	public void ADrawnSideOutOfReachRemovesTheGuest()
	{
		var grid = new GuestPathGrid( 20, 10 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 5, true );
		var simulation = Simulation( grid );
		var guest = simulation.SpawnInPark( 4, 5 );
		IGuardGuests guards = simulation;
		guards.Hold( guest.Id );
		Assert.IsFalse( guards.Release( guest.Id, (4, 5), 1 ), "lane B's entrance (19, 5) is no path: the route fails and 0x0050B780 destroys the guest" );
		var other = simulation.SpawnInPark( 6, 5 );
		guards.Hold( other.Id );
		Assert.IsTrue( guards.Release( other.Id, (6, 5), 0 ), "side A is in reach" );
	}

	[TestMethod]
	public void WithoutADrawTheNearerLaneIsTaken()
	{
		var simulation = Simulation( GuestTests.CreateCross() );
		var guest = simulation.SpawnInPark( 4, 5 );
		IGuardGuests guards = simulation;
		guards.Hold( guest.Id );
		Assert.IsTrue( guards.Release( guest.Id, (4, 5) ) );
		Assert.IsNull( guest.ExitSide );
	}
}
