using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The guests' thing update runs in the park turn's sweep only (tp.exe 0x00516380 calls 0x0050B360 for each thing, once per eight 31 ms substeps, 0x0054F668), newest thing first, and the turn ends with
/// the destroys and the arrival update (DET-014): the ticks between two turns run no state handler, no walk step and no draw of a guest.
/// </summary>
[TestClass]
public class GuestTurnSweepTests
{
	private const float Dt = 1f / FixedStepClock.TicksPerSecond;

	[TestMethod]
	public void AGuestsStateHandlerRunsOnTheTickThatStartsAParkTurnAndNotBetween()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 5, 5 );
		// a leaving guest with no walk to finish: its handler would send it on at once
		guest.State = GuestState.ExitingRide;
		guest.HasWaypoint = false;
		guest.StateTimer = 0;
		var ticks = 0;
		var draws = simulation.RandomState;
		while ( !simulation.NextTickStartsTurn )
		{
			simulation.Tick( Dt );
			ticks++;
			Assert.AreEqual( GuestState.ExitingRide, guest.State, $"tick {ticks}: no state handler runs between two turns" );
		}
		Assert.IsTrue( ticks >= 10, $"{ticks} ticks stand between two park turns" );
		Assert.AreEqual( draws, simulation.RandomState, "and none draws from the world generator" );
		Assert.AreEqual( ticks * (double)Dt, guest.TimeInParkSeconds!.Value, 1e-9, "the seconds still count every tick" );
		simulation.Tick( Dt );
		Assert.AreNotEqual( GuestState.ExitingRide, guest.State, "the turn's tick runs it" );
	}

	[TestMethod]
	public void TheGuestTheArrivalMakesIsNotUpdatedInTheTurnThatMadeIt()
	{
		var settings = GuestTests.CreateSettings();
		settings.ArrivalTimeBetween = 0;
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 3 ) { ArrivalClock = () => 1000 };
		// the vehicle-less arrival update: a call starts the group, the next turn's call makes a guest
		var turns = 0;
		Guest? made = null;
		for ( var tick = 0; tick < 200 && made == null; tick++ )
		{
			simulation.Tick( Dt );
			if ( simulation.Guests.Count > 0 )
			{
				made = simulation.Guests[0];
				turns = (int)simulation.ParkTurn;
			}
		}
		Assert.IsNotNull( made, "a group arrived" );
		Assert.AreEqual( 2, turns, "the first turn's update started the group, the second made its first guest" );
		Assert.AreEqual( 0.0, made!.TimeInParkSeconds, "the arrival update closes the turn: the guest it makes is not updated in that turn" );
		simulation.Tick( Dt );
		Assert.AreEqual( (double)Dt, made.TimeInParkSeconds!.Value, 1e-9, "the next tick counts its first seconds" );
	}

	[TestMethod]
	public void ThePoolsOwnStatesAndTheHarnessWaitKeepTheirPerTickStep()
	{
		// [EXT:pools-swim] a change room's exit wait counts seconds on every tick, as before: the original has no such state
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 5, 5 );
		guest.State = GuestState.ExitingRide;
		guest.StateTimer = 2.5f * Dt;
		simulation.Tick( Dt );
		simulation.Tick( Dt );
		Assert.AreEqual( GuestState.ExitingRide, guest.State );
		simulation.Tick( Dt );
		Assert.AreEqual( GuestState.DoingNothing, guest.State, "the timer ran out on the third tick, not at the next park turn" );
	}

	private sealed class LeavingOrder : IGuestObserver
	{
		public readonly System.Collections.Generic.List<int> Ids = new();
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) { }
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) => Ids.Add( guest.Id );
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
	}

	/// <summary>
	/// [BIN:TPW-EXE:0x00516380] the sweep walks the pool's used list from its head, the thing built last (0x5163A4, each slot's older link after the update, 0x516413..0x516431): of three guests that all read an exit counter of zero in the same turn, the one made last is updated first (it goes home).
	/// </summary>
	[TestMethod]
	public void TheTurnUpdatesTheNewestGuestFirst()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var order = new LeavingOrder();
		simulation.Observer = order;
		var oldest = simulation.SpawnInPark( 5, 5 );
		var middle = simulation.SpawnInPark( 5, 5 );
		var newest = simulation.SpawnInPark( 5, 5 );
		foreach ( var guest in new[] { oldest, middle, newest } )
		{
			guest.State = GuestState.DoingNothing;
			guest.ExitLevel = 0;
			guest.HasWaypoint = false;
		}
		for ( var tick = 0; tick < 60 && order.Ids.Count < 1; tick++ )
			simulation.Tick( Dt );
		Assert.AreEqual( newest.Id, order.Ids.FirstOrDefault(), $"the newest of three guests is updated first ({oldest.State}, {middle.State}, {newest.State})" );
	}
}
