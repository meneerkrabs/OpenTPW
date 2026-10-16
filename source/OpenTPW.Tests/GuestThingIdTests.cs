using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>A guest's id is its thing's slot in the park's one pool (tp.exe 0x004F8940 → 0x0050AFE0 → 0x00516270; WALK-010).</summary>
[TestClass]
public class GuestThingIdTests
{
	private static GuestSimulation Simulation() => new( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };

	[TestMethod]
	public void GuestsTakeTheSlotsOfThePoolInOrder()
	{
		var simulation = Simulation();
		var ids = Enumerable.Range( 0, 3 ).Select( _ => simulation.SpawnInPark( 10, 0 ).Id ).ToArray();
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, ids );
		Assert.AreEqual( 4, simulation.Things.NextId );
		Assert.AreEqual( SaveThingModel.Guest, simulation.Things.ModelOf( 2 ) );
	}

	/// <summary>A guest that leaves asks for its destruction; its slot is freed when the turn ends, and the request made first is the next guest's id (the queue frees last request first).</summary>
	[TestMethod]
	public void ALeavingGuestsSlotIsTakenAgainByTheNextGuest()
	{
		var simulation = Simulation();
		var guests = Enumerable.Range( 0, 4 ).Select( _ => simulation.SpawnInPark( 10, 0 ) ).ToArray();
		guests[0].State = GuestState.Gone;
		guests[2].State = GuestState.Gone;
		simulation.Tick( 1f / 60 );
		CollectionAssert.AreEqual( new[] { 2, 4 }, simulation.Guests.Select( guest => guest.Id ).ToArray() );
		Assert.AreEqual( 0, simulation.Things.QueuedCount, "a simulation of its own ends the turn's destroys itself" );
		// the removal pass asks newest first (guest 3, then guest 1); the queue frees the last request first, so slot 1 is freed first and slot 3 last
		CollectionAssert.AreEqual( new[] { 3, 1, 5 }, simulation.Things.FreeInOrder().Take( 3 ).ToArray() );
		CollectionAssert.AreEqual( new[] { 3, 1, 5 }, new[] { simulation.SpawnInPark( 10, 0 ).Id, simulation.SpawnInPark( 10, 0 ).Id, simulation.SpawnInPark( 10, 0 ).Id } );
	}

	[TestMethod]
	public void AParksPoolIsSharedAndTheHostEndsTheTurn()
	{
		var simulation = Simulation();
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		simulation.AttachThings( pool );
		var guest = simulation.SpawnInPark( 10, 0 );
		Assert.AreEqual( 11, guest.Id, "ids 1 to 10 belong to the world's singletons" );
		guest.State = GuestState.Gone;
		simulation.Tick( 1f / 60 );
		Assert.AreEqual( 1, pool.QueuedCount, "the park ends the turn, after every kind of thing has been updated" );
		Assert.IsTrue( pool.IsUsed( 11 ) );
		pool.DestroyQueued();
		Assert.AreEqual( 11, simulation.SpawnInPark( 10, 0 ).Id );
		Assert.ThrowsException<InvalidOperationException>( () => simulation.AttachThings( new ThingPool() ), "a pool is attached before the first guest" );
	}

	[TestMethod]
	public void AFullPoolBuildsNoGuest()
	{
		var simulation = Simulation();
		var pool = new ThingPool();
		for ( var count = 0; count < ThingPool.Capacity; count++ )
			pool.Allocate( SaveThingModel.Attraction );
		simulation.AttachThings( pool );
		Assert.ThrowsException<InvalidOperationException>( () => simulation.SpawnInPark( 10, 0 ) );
		Assert.AreEqual( 0, simulation.Guests.Count );
	}

	/// <summary>A snapshot of a simulation with its own pool keeps the free list's order, so a restored park hands out the ids a continuing one does.</summary>
	[TestMethod]
	public void ASnapshotKeepsTheFreeListOrderOfTheStandalonePool()
	{
		var live = Simulation();
		var guests = Enumerable.Range( 0, 4 ).Select( _ => live.SpawnInPark( 10, 0 ) ).ToArray();
		guests[0].State = GuestState.Gone;
		guests[2].State = GuestState.Gone;
		live.Tick( 1f / 60 );
		var snapshot = live.CaptureSnapshot();
		Assert.IsNotNull( snapshot.Things );
		CollectionAssert.AreEqual( new[] { 3 }, snapshot.Things!.FreeFirst.ToArray(), "the head of the free list that is out of order; the ascending run after it (1, 5, 6, ...) is rebuilt" );
		Assert.AreEqual( GuestSimulationSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion );
		var restored = Simulation();
		restored.RestoreSnapshot( snapshot );
		CollectionAssert.AreEqual( new[] { 3, 1, 5, 6 }, restored.Things.FreeInOrder().Take( 4 ).ToArray() );
		Assert.AreEqual( live.ComputeStateHash(), restored.ComputeStateHash() );
		Assert.AreEqual( live.SpawnInPark( 10, 0 ).Id, restored.SpawnInPark( 10, 0 ).Id );
		Assert.AreEqual( live.ComputeStateHash(), restored.ComputeStateHash(), "the two continue alike" );
	}

	/// <summary>A save from before the pool counts its guests: they keep those ids as slots, and the next guest takes the lowest free one.</summary>
	[TestMethod]
	public void ASaveFromBeforeThePoolKeepsItsGuestIds()
	{
		var source = Simulation();
		for ( var count = 0; count < 3; count++ )
			source.SpawnInPark( 10, 0 );
		var legacy = source.CaptureSnapshot() with { SchemaVersion = 7, NextGuestId = 4, Things = null };
		var target = Simulation();
		target.RestoreSnapshot( legacy );
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, target.Guests.Select( guest => guest.Id ).ToArray() );
		Assert.AreEqual( 4, target.SpawnInPark( 10, 0 ).Id );
		Assert.IsTrue( target.Things.IsUsed( 2 ) && target.Things.ModelOf( 2 ) == SaveThingModel.Guest );
	}

	[TestMethod]
	public void AParkRefusesAGuestWhoseSlotIsNotAGuestSlot()
	{
		var source = Simulation();
		source.SpawnInPark( 10, 0 );
		var snapshot = source.CaptureSnapshot() with { Things = null };
		var target = Simulation();
		var pool = new ThingPool();
		pool.Allocate( SaveThingModel.Attraction );
		target.AttachThings( pool );
		var validation = target.ValidateSnapshot( snapshot );
		Assert.IsFalse( validation.IsValid );
		StringAssert.Contains( string.Join( " ", validation.Errors ), "not a guest slot" );
		Assert.ThrowsException<InvalidDataException>( () => target.RestoreSnapshot( snapshot ) );
		var good = new ThingPool();
		good.Allocate( SaveThingModel.Guest );
		var shared = Simulation();
		shared.AttachThings( good );
		shared.RestoreSnapshot( snapshot );
		Assert.AreEqual( 1, shared.Guests.Single().Id );
	}

	[TestMethod]
	public void ACorruptPoolInASnapshotDoesNotValidate()
	{
		var source = Simulation();
		source.SpawnInPark( 10, 0 );
		var snapshot = source.CaptureSnapshot();
		var target = Simulation();
		Assert.IsTrue( target.ValidateSnapshot( snapshot ).IsValid );
		Assert.IsFalse( target.ValidateSnapshot( snapshot with { Things = new ThingPoolSnapshot { Used = new[] { new ThingSlotData( 1, 1 ), new ThingSlotData( 1, 3 ) } } } ).IsValid );
		Assert.IsFalse( target.ValidateSnapshot( snapshot with { Things = new ThingPoolSnapshot { Used = new[] { new ThingSlotData( 1, 1 ) }, FreeFirst = new[] { 1 } } } ).IsValid );
		Assert.IsFalse( target.ValidateSnapshot( snapshot with { Things = new ThingPoolSnapshot { Used = new[] { new ThingSlotData( 0, 1 ) } } } ).IsValid );
	}
}
