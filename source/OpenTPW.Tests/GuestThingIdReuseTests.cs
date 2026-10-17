using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Thing ids are slots that the next thing takes again (WALK-010). Nothing may keep naming a guest after it has left: a queue, a ride's riders and a saved
/// reference must never point at the newcomer that took the slot. The park runs with arrivals and short visits so that guests leave and slots are reused all the time.
/// </summary>
[TestClass]
public class GuestThingIdReuseTests
{
	private static readonly GuestState[] ArrivalStates =
		{ GuestState.CrossingRoad, GuestState.CrossingRoadToPark, GuestState.GoingToTicketBooth, GuestState.AtTicketBooth, GuestState.EnteringPark };

	private static (GuestSnapshotTests.Rig Rig, ThingPool Pool) SharedPoolPark()
	{
		var rig = new GuestSnapshotTests.Rig();
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		rig.Simulation.AttachThings( pool );
		return (rig, pool);
	}

	/// <summary>
	/// While guests come and go on the park's shared pool, every id in a queue or a ride's riders names a living guest, the pool's guest slots are exactly the living guests, and a guest that
	/// takes a slot over starts as a newcomer (no attraction, an arrival state).
	/// </summary>
	[TestMethod]
	public void ASlotTakenOverByANewGuestIsNeverNamedByTheOldGuestsQueueOrRide()
	{
		var (rig, pool) = SharedPoolPark();
		var seen = new Dictionary<int, HashSet<Guest>>();
		var reused = 0;
		for ( long tick = 1; tick <= 30000; tick++ )
		{
			rig.Step();
			GuestSnapshotTests.Drive( rig, tick );
			pool.DestroyQueued();
			var living = rig.Simulation.Guests.ToDictionary( guest => guest.Id );
			foreach ( var guest in living.Values )
			{
				if ( !seen.TryGetValue( guest.Id, out var owners ) )
					seen[guest.Id] = owners = new HashSet<Guest>();
				if ( owners.Add( guest ) )
				{
					if ( owners.Count > 1 )
						reused++;
					Assert.AreEqual( 0, guest.AttractionId, $"tick {tick}: the newcomer in slot {guest.Id} has no attraction yet" );
					Assert.AreEqual( 0, guest.LastAttractionId, $"tick {tick}: the newcomer in slot {guest.Id} has no history of the old guest" );
					CollectionAssert.Contains( ArrivalStates, guest.State, $"tick {tick}: the newcomer in slot {guest.Id} is arriving" );
				}
			}
			foreach ( var (bridge, _) in rig.Rides )
				foreach ( var id in bridge.Queue.Concat( bridge.Riders ) )
				{
					Assert.IsTrue( living.ContainsKey( id ), $"tick {tick}: ride {bridge.AttractionId} names guest {id}, who has left" );
					Assert.AreEqual( SaveThingModel.Guest, pool.ModelOf( id ) );
				}
			CollectionAssert.AreEquivalent( living.Keys.ToArray(), pool.UsedNewestFirst().Where( slot => slot.Model == SaveThingModel.Guest ).Select( slot => slot.Id ).ToArray(),
				$"tick {tick}: the pool's guest slots are the living guests" );
			if ( tick % 300 == 0 )
				Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid, $"tick {tick}: the snapshot validates" );
		}
		Assert.IsTrue( reused >= 3, $"slots were taken over {reused} times; the run must exercise reuse" );
	}

	/// <summary>A park saved after slots have been reused (its free list no longer ascending) continues exactly as the live one, through the snapshot's own pool.</summary>
	[TestMethod]
	public void AParkSavedAfterSlotReuseContinuesIdentically()
	{
		var park = new GuestSnapshotTests.Rig();
		long tick = 0;
		while ( tick < 30000 && park.Simulation.Things.CaptureSnapshot().FreeFirst.Count == 0 )
		{
			park.Step();
			GuestSnapshotTests.Drive( park, ++tick );
		}
		Assert.IsTrue( park.Simulation.Things.CaptureSnapshot().FreeFirst.Count > 0, "slots have been freed and not yet taken again" );
		tick = GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.Guests.Count >= 4, tick, tick + 3000 );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.IsTrue( park.Simulation.ValidateSnapshot( snapshot ).IsValid );
		var resumed = new GuestSnapshotTests.Rig();
		resumed.Simulation.RestoreSnapshot( snapshot );
		// The script variable the script slice's snapshot restores: VAR_LETMEON still holds a presented guest.
		foreach ( var attraction in snapshot.Attractions )
			resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script[RideVariables.VAR_LETMEON] = attraction.LetMeOnWritten ? attraction.CalledGuest : 0;
		var resumedTick = tick;
		for ( var step = 0; step < 600; step++ )
		{
			park.Step();
			GuestSnapshotTests.Drive( park, ++tick );
			resumed.Step();
			GuestSnapshotTests.Drive( resumed, ++resumedTick );
			Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ), $"the parks diverged {step} ticks after the restore" );
			Assert.AreEqual( park.Simulation.Things.NextId, resumed.Simulation.Things.NextId, $"the next slot differs {step} ticks after the restore" );
		}
	}
}
