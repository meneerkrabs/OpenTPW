using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic host invariants for guest-only cell chains, not mixed guest/staff native parity (WALK-003).</summary>
[TestClass]
public class GuestCellChainLifecycleTests
{
	private static GuestSimulation Create() => new( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };

	private static Guest[] Spawn( GuestSimulation simulation ) => Enumerable.Range( 1, 6 ).Select( index =>
	{
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Happiness = index * 10;
		return guest;
	} ).ToArray();

	private static void AssertChain( GuestSimulation simulation, int x, params float[] expected ) =>
		CollectionAssert.AreEqual( expected, simulation.CellListHappiness().Where( item => item.X == x && item.Y == 5 ).Select( item => item.Happiness ).ToArray() );

	[TestMethod]
	public void MovingInteriorAndHeadMembersRemovesEveryOldLink()
	{
		var simulation = Create();
		var guests = Spawn( simulation );
		simulation.PlaceForTest( guests[1], 3, 5 );
		simulation.PlaceForTest( guests[4], 3, 5 );
		simulation.PlaceForTest( guests[1], 2, 5 );
		AssertChain( simulation, 2, 20, 60, 40, 30, 10 );
		AssertChain( simulation, 3, 50 );
		CollectionAssert.AreEquivalent( guests.Select( guest => guest.Happiness ).ToArray(), simulation.CellListHappiness().Select( item => item.Happiness ).ToArray(), "every guest remains in exactly one list" );
		simulation.PlaceForTest( guests[4], 2, 5 );
		AssertChain( simulation, 2, 50, 20, 60, 40, 30, 10 );
		AssertChain( simulation, 3 );
		Assert.IsNull( simulation.HeadOfCell( 3, 5 ), "the emptied source cell has no stale head" );
	}

	[TestMethod]
	public void SameCellPlacementNeitherReordersNorConsumesEntrySerials()
	{
		var simulation = Create();
		var guests = Spawn( simulation );
		var before = simulation.CaptureSnapshot().Guests.ToDictionary( guest => guest.Id, guest => guest.CellSerial );
		for ( var repeat = 0; repeat < 3; repeat++ )
			foreach ( var guest in guests )
				simulation.PlaceForTest( guest, 2, 5 );
		AssertChain( simulation, 2, 60, 50, 40, 30, 20, 10 );
		foreach ( var saved in simulation.CaptureSnapshot().Guests )
			Assert.AreEqual( before[saved.Id], saved.CellSerial );
		simulation.PlaceForTest( guests[0], 3, 5 );
		Assert.AreEqual( before.Values.Max() + 1, simulation.CaptureSnapshot().Guests.Single( guest => guest.Id == guests[0].Id ).CellSerial, "same-cell placements must not secretly advance the next serial" );
	}

	[TestMethod]
	public void RestorePreservesWholeChainsAndFutureEntryOrder()
	{
		var original = Create();
		var guests = Spawn( original );
		original.PlaceForTest( guests[1], 3, 5 );
		original.PlaceForTest( guests[4], 3, 5 );
		original.PlaceForTest( guests[1], 2, 5 );
		var snapshot = original.CaptureSnapshot();
		var restored = Create();
		restored.RestoreSnapshot( snapshot );
		foreach ( var simulation in new[] { original, restored } )
		{
			AssertChain( simulation, 2, 20, 60, 40, 30, 10 );
			AssertChain( simulation, 3, 50 );
			var moved = simulation.Guests.Single( guest => guest.Id == guests[0].Id );
			simulation.PlaceForTest( moved, 3, 5 );
			AssertChain( simulation, 2, 20, 60, 40, 30 );
			AssertChain( simulation, 3, 10, 50 );
			Assert.AreEqual( snapshot.Guests.Max( guest => guest.CellSerial ) + 1, simulation.CaptureSnapshot().Guests.Single( guest => guest.Id == moved.Id ).CellSerial );
			Assert.AreEqual( 6, simulation.CellListHappiness().Count() );
		}
		CollectionAssert.AreEqual( original.CaptureSnapshot().Guests.Select( guest => guest.CellSerial ).ToArray(), restored.CaptureSnapshot().Guests.Select( guest => guest.CellSerial ).ToArray() );
	}
}
