using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenTPW.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The order the renderers walk the guests in: the original's live thing list, the newest thing first (tp.exe 0x00518F90).</summary>
[TestClass]
public class GuestThingListOrderTests
{
	[TestMethod]
	public void TheGuestsComeNewestFirst()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var first = simulation.SpawnInPark( 2, 5 );
		var second = simulation.SpawnInPark( 2, 5 );
		var third = simulation.SpawnInPark( 2, 5 );
		var order = new List<Guest>();
		simulation.FillInThingListOrder( order );
		CollectionAssert.AreEqual( new[] { third, second, first }, order );
	}

	[TestMethod]
	public void TheCellListsKeepTheirOrderAcrossTheTurnsAndASave()
	{
		// tp.exe 0x004D91F0 inserts a thing at the head of its new cell's chain and nothing rebuilds the chains (WALK-003): the guest that entered a cell last stays its head
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guests = new[] { simulation.SpawnInPark( 2, 5 ), simulation.SpawnInPark( 2, 5 ), simulation.SpawnInPark( 2, 5 ) };
		var head = simulation.HeadOfCell( 2, 5 );
		Assert.AreSame( guests[2], head, "the last to enter heads the cell" );
		// the middle guest steps out and back in: it is the newest entrant now
		simulation.PlaceForTest( guests[1], 3, 5 );
		simulation.PlaceForTest( guests[1], 2, 5 );
		Assert.AreSame( guests[1], simulation.HeadOfCell( 2, 5 ) );
		var snapshot = simulation.CaptureSnapshot();
		var saved = snapshot.Guests.ToDictionary( item => item.Id, item => item.CellSerial );
		Assert.IsTrue( saved[guests[1].Id] > saved[guests[2].Id] && saved[guests[2].Id] > saved[guests[0].Id], "the entry numbers order the list" );
		var restored = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( guests[1].Id, restored.HeadOfCell( 2, 5 )!.Id, "the restored list has the same head" );
		// an older save holds no entry numbers: the lists start in guest order
		var older = snapshot with { Guests = snapshot.Guests.Select( item => item with { CellSerial = 0 } ).ToArray() };
		var legacy = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		legacy.RestoreSnapshot( older );
		Assert.AreEqual( guests[2].Id, legacy.HeadOfCell( 2, 5 )!.Id );
	}
	[TestMethod]
	public void HappinessCountsActualMembersRegardlessOfStateOrCellType()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var states = new[] { GuestState.Queueing, GuestState.Using, GuestState.HeldByGuard };
		for ( var index = 0; index < states.Length; index++ )
		{
			var guest = simulation.SpawnInPark( 2, 5 );
			guest.State = states[index];
			guest.Happiness = 20 + 30 * index;
		}
		simulation.Grid.Cells.SetType( 2, 5, (byte)0x1E );
		Assert.IsTrue( simulation.Guests.All( guest => !simulation.IsOnParkCell( guest ) ) );
		Assert.AreEqual( 3, simulation.CellListHappiness().Count() );
		Assert.AreEqual( 50, ParkHud.LocalHappiness( simulation.CellListHappiness(), 2, 5 ) );
	}

	[TestMethod]
	public void HappinessReadsListKeysWithoutSynchronizingStalePositions()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Happiness = 75;
		guest.CellX = 9;
		guest.CellY = 9;
		(guest.Navigation.X, guest.Navigation.Y) = GuestNavigation.Centre( (9, 9) );
		Assert.AreEqual( 75, ParkHud.LocalHappiness( simulation.CellListHappiness(), 2, 5 ) );
		Assert.AreEqual( 0, ParkHud.LocalHappiness( simulation.CellListHappiness(), 9, 9 ) );
		Assert.AreSame( guest, simulation.HeadOfCell( 2, 5 ) );
		Assert.IsNull( simulation.HeadOfCell( 9, 9 ) );
	}

	[TestMethod]
	public void HappinessPreservesExistingSwimMembershipTiming()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Happiness = 60;
		guest.State = GuestState.Swimming;
		Assert.AreEqual( 60, ParkHud.LocalHappiness( simulation.CellListHappiness(), 2, 5 ), "a read does not apply a pending membership change" );
		simulation.PlaceForTest( guest, 2, 5 );
		Assert.AreEqual( 0, simulation.CellListHappiness().Count(), "existing Occupy excludes the swim session" );
		Assert.AreEqual( 0, ParkHud.LocalHappiness( simulation.CellListHappiness(), 2, 5 ) );
	}

	[TestMethod]
	public void HappinessKeepsOffGridListKeysAndTheExistingRadiusBounds()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Happiness = 90;
		simulation.PlaceForTest( guest, -1, 5 );
		Assert.AreEqual( (-1, 5, 90f), simulation.CellListHappiness().Single() );
		Assert.AreEqual( 90, ParkHud.LocalHappiness( simulation.CellListHappiness(), 0, 5 ) );
		Assert.AreEqual( 0, ParkHud.LocalHappiness( simulation.CellListHappiness(), 1, 5 ) );
		simulation.PlaceForTest( guest, 128, 5 );
		Assert.AreEqual( (128, 5, 90f), simulation.CellListHappiness().Single() );
		Assert.AreEqual( 0, ParkHud.LocalHappiness( simulation.CellListHappiness(), 127, 5 ), "the existing upper bound remains 127" );
	}

	[TestMethod]
	public void RepeatedHappinessReadsPreserveStateRandomAndCellOrderAcrossRestore()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var first = simulation.SpawnInPark( 2, 5 );
		var second = simulation.SpawnInPark( 2, 5 );
		first.Happiness = 20;
		second.Happiness = 80;
		simulation.PlaceForTest( first, 3, 5 );
		simulation.PlaceForTest( first, 2, 5 );
		var saved = simulation.CaptureSnapshot();
		var restored = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		restored.RestoreSnapshot( saved );
		foreach ( var candidate in new[] { simulation, restored } )
		{
			var before = JsonSerializer.Serialize( candidate.CaptureSnapshot() );
			var hash = candidate.ComputeStateHash();
			var random = candidate.RandomState;
			var head = candidate.HeadOfCell( 2, 5 );
			var order = candidate.CellListHappiness().ToArray();
			CollectionAssert.AreEqual( new[] { (2, 5, 20f), (2, 5, 80f) }, order );
			for ( var read = 0; read < 5; read++ )
				Assert.AreEqual( 50, ParkHud.LocalHappiness( candidate.CellListHappiness(), 2, 5 ) );
			CollectionAssert.AreEqual( order, candidate.CellListHappiness().ToArray() );
			Assert.AreSame( head, candidate.HeadOfCell( 2, 5 ) );
			Assert.AreEqual( random, candidate.RandomState );
			Assert.AreEqual( hash, candidate.ComputeStateHash() );
			Assert.AreEqual( before, JsonSerializer.Serialize( candidate.CaptureSnapshot() ) );
		}
	}
}
