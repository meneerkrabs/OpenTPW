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
		simulation.TickTurn( 1f / 60 );
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
		live.TickTurn( 1f / 60 );
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

	// ---- The booth walk's speed term (tp.exe 0x004FF730) -------------------------------------------------------------------

	private sealed class OneVehicle : IArrivalVehicleHost
	{
		public int Status;
		public bool Exists( int kind ) => true;
		public bool Create( int kind ) => true;
		public int GetVariable( int kind, int index ) => index == ArrivalVehicleKind.StatusVariable ? Status : 0;
		public void SetVariable( int kind, int index, int value ) { if ( index == ArrivalVehicleKind.StatusVariable ) Status = value; }
	}

	private static int BoothTerm( GuestSimulation simulation, Guest guest ) =>
		(int)typeof( GuestSimulation ).GetMethod( "BoothWalkSpeedTerm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!.Invoke( simulation, new object[] { guest } )!;

	/// <summary>A guest on its way to the booth gets the speed term 25 when its thing id's low two bits are 0, else 0, but 50 while the bus waits at status 3.</summary>
	[TestMethod]
	public void TheBoothWalkSpeedTermFollowsTheThingIdAndTheBusStatus()
	{
		var simulation = Simulation();
		var guests = Enumerable.Range( 0, 4 ).Select( _ => simulation.SpawnInPark( 10, 0 ) ).ToArray();
		CollectionAssert.AreEqual( new[] { 0, 0, 0, GuestSpeed.ToiletBonus }, guests.Select( guest => BoothTerm( simulation, guest ) ).ToArray(), "no vehicle: ids 1, 2 and 3 take 0 and id 4 takes 25" );
		var vehicle = new OneVehicle();
		simulation.ArrivalVehicles = vehicle;
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Bus );
		vehicle.Status = 3;
		CollectionAssert.AreEqual( new[] { 50, 50, 50, 50 }, guests.Select( guest => BoothTerm( simulation, guest ) ).ToArray(), "the bus at status 3: 50 for every guest" );
		vehicle.Status = 2;
		CollectionAssert.AreEqual( new[] { 0, 0, 0, 25 }, guests.Select( guest => BoothTerm( simulation, guest ) ).ToArray(), "the bus at another status: by thing id" );
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Seaplane );
		vehicle.Status = 3;
		CollectionAssert.AreEqual( new[] { 0, 0, 0, 25 }, guests.Select( guest => BoothTerm( simulation, guest ) ).ToArray(), "a vehicle other than the bus is not tested for status 3" );
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Bus );
		vehicle.Status = 6;
		Assert.AreEqual( 25, BoothTerm( simulation, guests[3] ), "a gone vehicle is released and the thing id decides" );
		Assert.AreEqual( -1, simulation.ArrivalVehicle );
	}

	/// <summary>The term is set by the state-2 handler on every park turn of the walk to the booth: a guest of thing id 4 walks with the term 25.</summary>
	[TestMethod]
	public void AGuestWalkingToTheBoothCarriesTheTermOfItsThingId()
	{
		var grid = new GuestPathGrid( 20, 20 );
		for ( var x = 0; x < 16; x++ )
			grid.SetPath( x, 5, true );
		grid.SetPath( 10, 6, true );
		var settings = new GuestSettings { ArrivalLaneA = new[] { (10, 14), (10, 12), (10, 10), (10, 8), (10, 6) } };
		settings.Types.Add( new GuestType( 60, 600, 40 ) );
		var simulation = new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false };
		var guests = Enumerable.Range( 0, 4 ).Select( _ => simulation.SpawnArrival()! ).ToArray();
		var seen = new System.Collections.Generic.Dictionary<int, int>();
		for ( var tick = 0; tick < 4000 && guests.Any( guest => guest.State is not (GuestState.AtTicketBooth or GuestState.EnteringPark or GuestState.DoingNothing) ); tick++ )
		{
			simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			foreach ( var guest in guests.Where( guest => guest.State == GuestState.GoingToTicketBooth ) )
				seen[guest.Id] = guest.ToiletBonus;   // the last value seen on the walk is the one its handler set
		}
		Assert.AreEqual( 4, seen.Count, "every guest walked to the booth" );
		foreach ( var (id, term) in seen )
			Assert.AreEqual( (id & 3) == 0 ? GuestSpeed.ToiletBonus : 0, term, $"guest {id}" );
	}

	// ---- The dismissal's voice (tp.exe 0x005012F0, 0x0050133D) ---------------------------------------------------------------

	/// <summary>A guest dismissed from a queue says its piece for one thing id in eight (cat_kids 0x80); the guests of the other ids are silent.</summary>
	[TestMethod]
	public void ADismissedGuestVoicesForOneThingIdInEight()
	{
		var simulation = Simulation();
		var ride = new RideVisitorBridge( 1, "Ride", RideVisitorKind.Ride, 5, 55, 30 );
		simulation.Register( ride );
		var guests = Enumerable.Range( 0, 16 ).Select( _ => simulation.SpawnInPark( 10, 0 ) ).ToArray();
		var voiced = new System.Collections.Generic.List<(int Id, uint Event)>();
		simulation.Voiced += ( guest, eventId ) => voiced.Add( (guest.Id, eventId) );
		var dismiss = typeof( GuestSimulation ).GetMethod( "DismissFromQueue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!;
		foreach ( var guest in guests )
			dismiss.Invoke( simulation, new object[] { guest, ride, GuestThought.None, QueueEndReason.NoPlace } );
		CollectionAssert.AreEqual( new[] { (8, 0x80u), (16, 0x80u) }, voiced.ToArray(), "thing ids 8 and 16 have their low three bits 0" );
		Assert.AreEqual( GuestSimulation.DismissalVoiceEvent, 0x80u );
	}

	/// <summary>The thing ids are the original's pool slots, so who is silent depends on what was built before: behind the world's twelve things the guests are things 13 to 24, and 16 and 24 have their low three bits 0.</summary>
	[TestMethod]
	public void WhoVoicesDependsOnThePoolsHistory()
	{
		var simulation = Simulation();
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		pool.Allocate( SaveThingModel.Attraction );
		pool.Allocate( SaveThingModel.Attraction );
		simulation.AttachThings( pool );
		var ride = new RideVisitorBridge( 1, "Ride", RideVisitorKind.Ride, 5, 55, 30 );
		simulation.Register( ride );
		var guests = Enumerable.Range( 0, 12 ).Select( _ => simulation.SpawnInPark( 10, 0 ) ).ToArray();
		var voiced = new System.Collections.Generic.List<int>();
		simulation.Voiced += ( guest, _ ) => voiced.Add( guest.Id );
		var dismiss = typeof( GuestSimulation ).GetMethod( "DismissFromQueue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!;
		foreach ( var guest in guests )
			dismiss.Invoke( simulation, new object[] { guest, ride, GuestThought.None, QueueEndReason.NoPlace } );
		CollectionAssert.AreEqual( new[] { 16, 24 }, voiced.ToArray(), "guests 13..24 are the things 13..24" );
	}
}
