using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic host consistency cases, not original-game identity parity evidence (WALK-010).</summary>
[TestClass]
public class ThingPoolIdentityLifecycleTests
{
	[TestMethod]
	public void HoldingAQueuedSlotIsRejectedWithoutChangingItsSnapshotOrDestroyOrder()
	{
		var pool = new ThingPool();
		pool.Allocate( SaveThingModel.Guest );
		pool.Allocate( SaveThingModel.Attraction );
		pool.QueueDestroy( 1 );
		pool.QueueDestroy( 2 );
		var before = JsonSerializer.Serialize( pool.CaptureSnapshot() );
		Assert.IsFalse( pool.Hold( 1 ), "holding cannot cancel or contradict an accepted destroy request" );
		Assert.IsFalse( pool.IsHeld( 1 ) );
		Assert.IsTrue( pool.IsQueued( 1 ) );
		Assert.AreEqual( before, JsonSerializer.Serialize( pool.CaptureSnapshot() ) );
		Assert.AreEqual( 0, ThingPool.Problems( pool.CaptureSnapshot() ).Count );
		var resumed = new ThingPool();
		resumed.RestoreSnapshot( pool.CaptureSnapshot() );
		CollectionAssert.AreEqual( new[] { 2, 1 }, pool.DestroyQueued().ToArray() );
		CollectionAssert.AreEqual( new[] { 2, 1 }, resumed.DestroyQueued().ToArray() );
		CollectionAssert.AreEqual( pool.FreeInOrder().ToArray(), resumed.FreeInOrder().ToArray() );
	}

	[TestMethod]
	public void ReusingAnImmediatelyReleasedQueuedSlotDoesNotDestroyItsNewOwner()
	{
		var pool = new ThingPool();
		pool.Allocate( SaveThingModel.Guest );
		pool.Allocate( SaveThingModel.Attraction );
		pool.QueueDestroy( 1 );
		pool.QueueDestroy( 2 );
		Assert.IsTrue( pool.Release( 1 ) );
		Assert.AreEqual( 1, pool.Allocate( SaveThingModel.Handyman ) );
		Assert.IsFalse( pool.IsQueued( 1 ) );
		Assert.IsTrue( pool.Hold( 1 ), "the reused slot does not inherit the previous owner's destroy flag" );
		CollectionAssert.AreEqual( new[] { 2 }, pool.DestroyQueued().ToArray() );
		Assert.IsTrue( pool.IsUsed( 1 ) && pool.IsHeld( 1 ) );
		Assert.AreEqual( SaveThingModel.Handyman, pool.ModelOf( 1 ) );
		Assert.AreEqual( 0, ThingPool.Problems( pool.CaptureSnapshot() ).Count );
	}

	[TestMethod]
	public void MixedModelsHeldSlotsAndPendingDestroysResumeWithTheSameAllocationOrder()
	{
		var pool = new ThingPool();
		foreach ( var model in new[] { SaveThingModel.Guest, SaveThingModel.Attraction, SaveThingModel.Handyman,
			SaveThingModel.Guest, SaveThingModel.Attraction, SaveThingModel.Guest } )
			pool.Allocate( model );
		pool.Release( 2 );
		pool.Release( 5 );
		Assert.IsTrue( pool.Hold( 3 ) );
		pool.QueueDestroy( 4 );
		pool.QueueDestroy( 1 );
		pool.QueueDestroy( 3 );
		var snapshot = pool.CaptureSnapshot();
		CollectionAssert.AreEqual( new[] { 5 }, snapshot.FreeFirst.ToArray() );
		var resumed = new ThingPool();
		resumed.RestoreSnapshot( JsonSerializer.Deserialize<ThingPoolSnapshot>( JsonSerializer.Serialize( snapshot ) )! );
		foreach ( var candidate in new[] { pool, resumed } )
		{
			CollectionAssert.AreEqual( new[] { 1, 4 }, candidate.DestroyQueued().ToArray() );
			CollectionAssert.AreEqual( new[] { 4, 1, 5, 2, 7 }, Enumerable.Range( 0, 5 ).Select( _ => candidate.Allocate( SaveThingModel.Attraction ) ).ToArray() );
			Assert.IsTrue( candidate.IsHeld( 3 ) );
			Assert.AreEqual( SaveThingModel.Handyman, candidate.ModelOf( 3 ) );
			CollectionAssert.AreEqual( Enumerable.Range( 1, ThingPool.Capacity ).ToArray(),
				candidate.Capture().Select( slot => slot.Id ).Concat( candidate.FreeInOrder() ).OrderBy( id => id ).ToArray(), "used and free slots partition the pool without duplicates" );
		}
		Assert.AreEqual( JsonSerializer.Serialize( pool.CaptureSnapshot() ), JsonSerializer.Serialize( resumed.CaptureSnapshot() ) );
	}
}
