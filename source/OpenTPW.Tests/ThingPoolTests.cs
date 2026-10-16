using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The thing pool: tp.exe's one table of 10239 thing ids (allocator 0x516270, free 0x516200, deferred destroy 0x516310, load 0x518D43; WALK-010).</summary>
[TestClass]
public class ThingPoolTests
{
	[TestMethod]
	public void AFreshPoolGivesIdsFromOneUpInAscendingOrder()
	{
		var pool = new ThingPool();
		Assert.AreEqual( 0, pool.Count );
		Assert.AreEqual( 1, pool.NextId );
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, new[] { pool.Allocate( SaveThingModel.Guest ), pool.Allocate( SaveThingModel.Attraction ), pool.Allocate( SaveThingModel.Guest ) } );
		Assert.AreEqual( 4, pool.NextId );
		Assert.AreEqual( SaveThingModel.Attraction, pool.ModelOf( 2 ) );
	}

	[TestMethod]
	public void ThePoolHoldsTenThousandTwoHundredAndThirtyNineThingsAndThenGivesIdZero()
	{
		var pool = new ThingPool();
		for ( var expected = 1; expected <= ThingPool.Capacity; expected++ )
			Assert.AreEqual( expected, pool.Allocate( SaveThingModel.Guest ) );
		Assert.AreEqual( 10239, ThingPool.Capacity );
		Assert.AreEqual( ThingPool.Capacity, pool.Count );
		Assert.AreEqual( 0, pool.NextId );
		Assert.AreEqual( 0, pool.Allocate( SaveThingModel.Guest ), "the original logs a fatal error and carries on with id 0" );
		Assert.IsTrue( pool.Release( 77 ) );
		Assert.AreEqual( 77, pool.Allocate( SaveThingModel.Guest ), "a full pool takes the freed slot again" );
	}

	[TestMethod]
	public void AFreedIdIsTakenAgainByTheNextThingLastInFirstOut()
	{
		var pool = new ThingPool();
		for ( var count = 0; count < 6; count++ )
			pool.Allocate( SaveThingModel.Guest );
		Assert.IsTrue( pool.Release( 2 ) );
		Assert.IsTrue( pool.Release( 5 ) );
		Assert.IsTrue( pool.Release( 3 ) );
		CollectionAssert.AreEqual( new[] { 3, 5, 2, 7 }, pool.FreeInOrder().Take( 4 ).ToArray(), "the slot freed last is first in line, then the earlier ones, then the never used ids" );
		CollectionAssert.AreEqual( new[] { 3, 5, 2, 7, 8 }, Enumerable.Range( 0, 5 ).Select( _ => pool.Allocate( SaveThingModel.Guest ) ).ToArray() );
		Assert.IsFalse( pool.Release( 99 ), "a slot that is not in use frees nothing" );
		Assert.IsFalse( pool.Release( 0 ) );
	}

	[TestMethod]
	public void ADestroyRequestWaitsForTheTurnsEndAndThenFreesLastRequestFirst()
	{
		var pool = new ThingPool();
		for ( var count = 0; count < 5; count++ )
			pool.Allocate( SaveThingModel.Guest );
		pool.QueueDestroy( 4 );
		pool.QueueDestroy( 2 );
		pool.QueueDestroy( 4 );
		pool.QueueDestroy( 9 );
		Assert.AreEqual( 2, pool.QueuedCount, "a repeated request and a free slot change nothing" );
		Assert.IsTrue( pool.IsUsed( 4 ) && pool.IsUsed( 2 ), "the things stay in use until the turn's end" );
		Assert.AreEqual( 6, pool.NextId, "so a thing built in the same turn takes a new slot" );
		CollectionAssert.AreEqual( new[] { 2, 4 }, pool.DestroyQueued().ToArray(), "the queue's head, the request made last, is freed first" );
		Assert.AreEqual( 0, pool.QueuedCount );
		CollectionAssert.AreEqual( new[] { 4, 2, 6 }, pool.FreeInOrder().Take( 3 ).ToArray(), "so the request made first is the next thing's id" );
		Assert.AreEqual( 4, pool.Allocate( SaveThingModel.Attraction ) );
	}

	[TestMethod]
	public void TheUsedListRunsNewestFirstAndKeepsItsOrderAcrossFrees()
	{
		var pool = new ThingPool();
		for ( var count = 0; count < 4; count++ )
			pool.Allocate( SaveThingModel.Guest );
		CollectionAssert.AreEqual( new[] { 4, 3, 2, 1 }, pool.UsedNewestFirst().Select( slot => slot.Id ).ToArray() );
		pool.Release( 3 );
		CollectionAssert.AreEqual( new[] { 4, 2, 1 }, pool.UsedNewestFirst().Select( slot => slot.Id ).ToArray() );
		Assert.AreEqual( 3, pool.Allocate( SaveThingModel.Handyman ) );
		CollectionAssert.AreEqual( new[] { 3, 4, 2, 1 }, pool.UsedNewestFirst().Select( slot => slot.Id ).ToArray(), "the reused slot is the newest thing" );
		pool.Release( 3 );
		pool.Release( 1 );
		Assert.AreEqual( 4, pool.Newest );
		CollectionAssert.AreEqual( new[] { 4, 2 }, pool.UsedNewestFirst().Select( slot => slot.Id ).ToArray() );
	}

	[TestMethod]
	public void TheWorldSetupBuildsTenSingletonsInTheConstructorOrder()
	{
		var pool = new ThingPool();
		CollectionAssert.AreEqual( Enumerable.Range( 1, 10 ).ToArray(), pool.AllocateWorldSetup().ToArray() );
		// the jungle level's shipped save lists thing ids 1 to 10 with these models (the chain's tail)
		CollectionAssert.AreEqual( new[] { 9, 10, 11, 12, 13, 14, 15, 16, 17, 19 }, Enumerable.Range( 1, 10 ).Select( id => (int)pool.ModelOf( id ) ).ToArray() );
		Assert.AreEqual( 11, pool.NextId, "the gates and the lights are the park's first objects" );
	}

	[TestMethod]
	public void ALoadKeepsTheSavedSlotsAndRebuildsTheFreeListInAscendingOrder()
	{
		var pool = new ThingPool();
		for ( var count = 0; count < 20; count++ )
			pool.Allocate( SaveThingModel.Guest );
		pool.Release( 7 );
		pool.Release( 3 );
		CollectionAssert.AreEqual( new[] { 3, 7, 21 }, pool.FreeInOrder().Take( 3 ).ToArray(), "the live pool's free list is last in, first out" );
		var saved = pool.Capture();
		var loaded = new ThingPool();
		loaded.Restore( saved );
		CollectionAssert.AreEqual( new[] { 3, 7, 21 }, loaded.FreeInOrder().Take( 3 ).ToArray() );
		pool.Release( 12 );
		Assert.AreEqual( 12, pool.NextId, "a live pool takes the slot freed last" );
		var reloaded = new ThingPool();
		reloaded.Restore( pool.Capture() );
		CollectionAssert.AreEqual( new[] { 3, 7, 12, 21 }, reloaded.FreeInOrder().Take( 4 ).ToArray(), "a load puts the free ids in ascending order, whatever the freeing order was" );
		CollectionAssert.AreEqual( pool.Capture(), reloaded.Capture(), "the used list keeps its order" );
	}

	[TestMethod]
	public void ALoadOfTheShippedJungleChainTakesTheNextIdAfterItsHighestHole()
	{
		var chain = new[] { (42, 1), (41, 1), (31, 1), (30, 8), (29, 1), (15, 3), (28, 7), (27, 6), (11, 3), (10, 19), (9, 17), (1, 9) };
		var pool = new ThingPool();
		pool.Restore( chain.Select( entry => new ThingSlot( entry.Item1, (SaveThingModel)entry.Item2 ) ) );
		Assert.AreEqual( chain.Length, pool.Count );
		Assert.AreEqual( 2, pool.NextId );
		CollectionAssert.AreEqual( new[] { 2, 3, 4 }, Enumerable.Range( 0, 3 ).Select( _ => pool.Allocate( SaveThingModel.Guest ) ).ToArray() );
		Assert.AreEqual( 42, pool.UsedNewestFirst().ElementAt( 3 ).Id, "the saved head is the newest only until a thing is built" );
	}

	[TestMethod]
	public void ALoadRefusesIdsOutsideThePoolRepeatedIdsAndUnknownModels()
	{
		var pool = new ThingPool();
		Assert.ThrowsException<InvalidDataException>( () => pool.Restore( new[] { new ThingSlot( 0, SaveThingModel.Guest ) } ) );
		Assert.ThrowsException<InvalidDataException>( () => pool.Restore( new[] { new ThingSlot( ThingPool.Capacity + 1, SaveThingModel.Guest ) } ) );
		Assert.ThrowsException<InvalidDataException>( () => pool.Restore( new[] { new ThingSlot( 5, SaveThingModel.Guest ), new ThingSlot( 5, SaveThingModel.Attraction ) } ) );
		Assert.ThrowsException<InvalidDataException>( () => pool.Restore( new[] { new ThingSlot( 5, (SaveThingModel)0 ) } ) );
		Assert.ThrowsException<InvalidDataException>( () => pool.Restore( new[] { new ThingSlot( 5, (SaveThingModel)20 ) } ) );
		Assert.AreEqual( 0, pool.Count, "a refused load leaves the pool as it was" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => pool.Allocate( (SaveThingModel)0 ) );
	}

	[TestMethod]
	public void OccupyTakesOneSlotAsTheNewestAndKeepsTheRestOfTheFreeList()
	{
		var pool = new ThingPool();
		Assert.IsTrue( pool.Occupy( 3, SaveThingModel.Guest ) );
		Assert.IsFalse( pool.Occupy( 3, SaveThingModel.Guest ), "a slot in use cannot be taken again" );
		Assert.IsFalse( pool.Occupy( 0, SaveThingModel.Guest ) );
		Assert.IsFalse( pool.Occupy( ThingPool.Capacity + 1, SaveThingModel.Guest ) );
		CollectionAssert.AreEqual( new[] { 1, 2, 4, 5 }, pool.FreeInOrder().Take( 4 ).ToArray() );
		Assert.AreEqual( 3, pool.Newest );
		Assert.IsTrue( pool.Release( 3 ) );
		CollectionAssert.AreEqual( new[] { 3, 1, 2, 4 }, pool.FreeInOrder().Take( 4 ).ToArray() );
	}

	[TestMethod]
	public void ReleasingAQueuedThingDropsItsRequest()
	{
		var pool = new ThingPool();
		pool.Allocate( SaveThingModel.Guest );
		pool.QueueDestroy( 1 );
		Assert.IsTrue( pool.Release( 1 ) );
		Assert.AreEqual( 0, pool.QueuedCount );
		Assert.AreEqual( 0, pool.DestroyQueued().Count );
		Assert.AreEqual( 1, pool.Allocate( SaveThingModel.Guest ), "a slot is not freed twice" );
		Assert.AreEqual( 2, pool.NextId );
	}
}
