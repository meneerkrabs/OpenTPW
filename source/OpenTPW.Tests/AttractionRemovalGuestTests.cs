using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The removal message (tp.exe <c>0x004DD0A0</c> → <c>0x004FB360</c>): every guest whose target is the removed attraction loses the small happiness step,
/// its target and its used-history entry and decides again; a queued guest is dismissed first (the medium step).
/// </summary>
[TestClass]
public class AttractionRemovalGuestTests
{
	private const int RideId = 1;

	[TestMethod]
	public void QueuedGuestsAreDismissedAndEveryTargetingGuestLosesTheSmallStep()
	{
		var rig = new QueueTests.Rig();
		var queued = Enumerable.Range( 0, 3 ).Select( _ => rig.Send( 80 ) ).ToList();
		rig.RunTurns( 120 );   // each guest first walks the whole way to the back cell (WALK-004), in its own time, so the join order is not the send order
		Assert.AreEqual( GuestState.WaitingToBoard, queued.Single( guest => guest.QueuePosition == 0 ).State );
		Assert.IsTrue( queued.Where( guest => guest.QueuePosition != 0 ).All( guest => guest.State == GuestState.Queueing ) );
		var walking = rig.Send( 80 );
		var other = rig.Simulation.SpawnInPark( 5, 0 );
		other.Happiness = 80;
		other.UsedHistory[1] = RideId;
		other.TemporaryHistory[1] = 9;
		other.TemporaryHistory[3] = RideId;
		queued[1].UsedHistory[2] = RideId;
		queued[1].TemporaryHistory[2] = 7;
		queued[1].TemporaryHistory[0] = RideId;
		var settings = rig.Simulation.Settings;
		var before = queued.Select( guest => guest.Happiness ).ToList();
		var walkingBefore = walking.Happiness;

		rig.Simulation.Unregister( rig.Ride );

		for ( var index = 0; index < queued.Count; index++ )
		{
			var guest = queued[index];
			Assert.AreEqual( MeterMath.Add( MeterMath.Add( before[index], -settings.MediumHappinessChange ), -settings.SmallHappinessChange ), guest.Happiness,
				$"guest {guest.Id}: the dismissal 0x5012f0 (medium) and the removal (small)" );
			Assert.AreEqual( (GuestState.DoingNothing, 0, -1), (guest.State, guest.AttractionId, guest.QueuePosition) );
		}
		Assert.AreEqual( MeterMath.Add( walkingBefore, -settings.SmallHappinessChange ), walking.Happiness, "a guest walking to it is not dismissed" );
		Assert.AreEqual( (GuestState.DoingNothing, 0), (walking.State, walking.AttractionId) );
		Assert.AreEqual( (80f, 0), (other.Happiness, other.AttractionId), "a guest with another target keeps its happiness" );
		Assert.AreEqual( (0, 0, RideId), (other.UsedHistory[1], other.TemporaryHistory[1], other.TemporaryHistory[3]),
			"the history pass 0x4fb4ba runs for every guest the message reaches, not only the targeting ones" );
		Assert.AreEqual( 0, queued[1].UsedHistory[2], "the used-history slot naming the attraction is cleared" );
		Assert.AreEqual( 0, queued[1].TemporaryHistory[2], "with the temporary-history slot of the same index" );
		Assert.AreEqual( RideId, queued[1].TemporaryHistory[0], "a temporary-history slot is cleared only beside a used-history match" );
	}

	[TestMethod]
	public void AGuestWalkingToARemovedBinKeepsItsLitterAndDecidesAgain()
	{
		var rig = new QueueTests.Rig();
		Guest Walker( int bin )
		{
			var guest = rig.Simulation.SpawnInPark( 4, 0 );
			guest.Happiness = 80;
			guest.Litter = 60;
			guest.BinId = bin;
			guest.State = GuestState.ThrowingAwayLitter;
			return guest;
		}
		var walker = Walker( 5 );
		var other = Walker( 6 );
		rig.Simulation.BinRemoved( 5 );
		Assert.AreEqual( MeterMath.Add( 80, -rig.Simulation.Settings.SmallHappinessChange ), walker.Happiness );
		Assert.AreEqual( (GuestState.DoingNothing, 0, 60f, false), (walker.State, walker.BinId, walker.Litter, walker.HasWaypoint), "the litter meter is kept (0x4fb360)" );
		Assert.AreEqual( (GuestState.ThrowingAwayLitter, 6, 80f), (other.State, other.BinId, other.Happiness) );
	}

	[TestMethod]
	public void TheStepsAreClampedAtZero()
	{
		var rig = new QueueTests.Rig();
		var guest = rig.Send( 80 );
		guest.Happiness = 2;
		rig.Simulation.Unregister( rig.Ride );
		Assert.AreEqual( 0f, guest.Happiness );
	}

	[TestMethod]
	public void AQueuedGuestWhoseAttractionVanishedWithoutTheMessageStaysWhereItStands()
	{
		// [BIN:TPW-EXE:0x004FB360] the original has no poll for a vanished attraction; the safety net does what the removal message does: state 6, no position written
		var rig = new QueueTests.Rig();
		var queued = Enumerable.Range( 0, 3 ).Select( _ => rig.Send( 80 ) ).ToList();
		rig.RunTurns( 120 );
		var guest = queued.First( candidate => candidate.State == GuestState.Queueing );
		var cell = guest.Navigation.Cell;
		guest.AttractionId = 999;
		rig.RunTurns( 1 );
		Assert.AreEqual( (GuestState.DoingNothing, 0, -1), (guest.State, guest.AttractionId, guest.QueuePosition) );
		Assert.AreEqual( cell, guest.Navigation.Cell, "no position is written" );
	}
}
