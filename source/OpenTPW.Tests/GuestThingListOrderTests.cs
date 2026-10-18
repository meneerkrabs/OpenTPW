using System.Collections.Generic;
using System.Linq;
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
}
