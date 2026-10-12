using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Own-format state snapshots of the runtime lane's fixed-step clock, park gate retry bookkeeping and
/// staff world actors (docs of the modules): a restored object continues exactly where the saved one
/// stopped, and invalid snapshot fields are rejected before any mutation.
/// </summary>
[TestClass]
public class RuntimeSnapshotTests
{
	// ---------------------------------------------------------------- fixed-step clock

	[TestMethod]
	public void RestoredClockContinuesTheTickRemainderExactly()
	{
		var clock = new FixedStepClock();
		var ticks = new List<long>();
		clock.Advance( FixedStepClock.TickDuration / 2, _ => ticks.Add( clock.TickCount ) );
		var snapshot = clock.CaptureSnapshot();
		Assert.AreEqual( 0L, snapshot.TickCount );
		Assert.AreEqual( FixedStepClock.TickDuration / 2, snapshot.PendingSeconds, 1e-12 );

		var restored = new FixedStepClock();
		restored.RestoreSnapshot( snapshot );
		var originalTicks = new List<long>();
		var restoredTicks = new List<long>();
		clock.Advance( FixedStepClock.TickDuration * 0.6, _ => originalTicks.Add( clock.TickCount ) );
		restored.Advance( FixedStepClock.TickDuration * 0.6, _ => restoredTicks.Add( restored.TickCount ) );
		CollectionAssert.AreEqual( originalTicks, restoredTicks, "the restored clock fires the same ticks" );
		Assert.AreEqual( clock.TickCount, restored.TickCount );
		Assert.AreEqual( clock.PendingSeconds, restored.PendingSeconds, 1e-12 );
		Assert.AreEqual( clock.DroppedSeconds, restored.DroppedSeconds, 1e-12 );
	}

	[TestMethod]
	public void RestoredClockKeepsDroppedTimeDiagnostics()
	{
		var clock = new FixedStepClock();
		clock.Advance( 10, _ => { } );
		var snapshot = clock.CaptureSnapshot();
		Assert.AreEqual( 10 - FixedStepClock.MaximumCatchUpTicks * FixedStepClock.TickDuration, snapshot.DroppedSeconds, 1e-12 );

		var restored = new FixedStepClock();
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( snapshot.TickCount, restored.TickCount );
		Assert.AreEqual( snapshot.DroppedSeconds, restored.DroppedSeconds, 1e-12 );
		restored.Advance( 0, _ => { } );
		Assert.AreEqual( snapshot.DroppedSeconds, restored.DroppedSeconds, 1e-12, "a zero frame drops nothing more" );
	}

	[TestMethod]
	public void ClockSnapshotsRejectInvalidFieldsWithoutMutating()
	{
		var clock = new FixedStepClock();
		clock.Advance( FixedStepClock.TickDuration, _ => { } );
		var good = clock.CaptureSnapshot();
		foreach ( var bad in new[]
		{
			good with { TickCount = -1 },
			good with { PendingSeconds = -0.01 },
			good with { PendingSeconds = FixedStepClock.TickDuration },
			good with { PendingSeconds = double.NaN },
			good with { PendingSeconds = double.PositiveInfinity },
			good with { DroppedSeconds = -1 },
			good with { DroppedSeconds = double.PositiveInfinity }
		} )
		{
			var fresh = new FixedStepClock();
			Assert.ThrowsException<InvalidDataException>( () => fresh.ValidateSnapshot( bad ) );
			Assert.ThrowsException<InvalidDataException>( () => fresh.RestoreSnapshot( bad ) );
			Assert.AreEqual( 0L, fresh.TickCount, "a rejected restore does not mutate" );
		}
		Assert.ThrowsException<ArgumentNullException>( () => clock.ValidateSnapshot( null! ) );
	}

	// ---------------------------------------------------------------- park gate

	[TestMethod]
	public void GateSnapshotRoundTripsTheRetryBookkeeping()
	{
		var economy = EconomyTestData.Park();
		var gate = new ParkGate( economy, () => null, () => Array.Empty<OriginalObjectRuntime>() );
		Assert.AreEqual( 0L, gate.CaptureSnapshot().LastObservedTurn, "a fresh gate observes the park's turn" );

		gate.RestoreSnapshot( new ParkGate.ParkGateSnapshot( ParkCalendar.TickOfTurn( ParkGate.RetryPeriodTurns ) - 1 ) );
		Assert.AreEqual( ParkCalendar.TickOfTurn( ParkGate.RetryPeriodTurns ) - 1, gate.CaptureSnapshot().LastObservedTurn );

		// The gate reads the economy's turn live: advancing the economy past the restored observation does
		// not move the restored bookkeeping (the next Tick() consumes the crossing).
		economy.Advance( 10 );
		Assert.AreEqual( ParkCalendar.TickOfTurn( ParkGate.RetryPeriodTurns ) - 1, gate.CaptureSnapshot().LastObservedTurn );
		gate.Tick();
		Assert.AreEqual( economy.Turn, gate.CaptureSnapshot().LastObservedTurn, "the tick observes the economy's turn" );
	}

	[TestMethod]
	public void GateSnapshotsRejectNegativeTurns()
	{
		var gate = new ParkGate( EconomyTestData.Park(), () => null, () => Array.Empty<OriginalObjectRuntime>() );
		Assert.ThrowsException<InvalidDataException>( () => gate.ValidateSnapshot( new ParkGate.ParkGateSnapshot( -1 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => gate.RestoreSnapshot( new ParkGate.ParkGateSnapshot( -1 ) ) );
		Assert.AreEqual( 0L, gate.CaptureSnapshot().LastObservedTurn );
	}

	// ---------------------------------------------------------------- staff world actors

	private static GuestPathGrid Paths()
	{
		var grid = new GuestPathGrid( 5, 3 );
		for ( var x = 0; x < 5; x++ )
			grid.SetPath( x, 1, true );
		return grid;
	}

	private static StaffWorldSimulation World( ParkEconomy park, GuestPathGrid grid ) =>
		new( grid, () => park.Staff.Members, id => id == 100 ? (4, 1) : null, (0, 1) );

	[TestMethod]
	public void RestoredStaffActorsResumeMidMovementExactly()
	{
		var travelling = EconomyTestData.Park();
		var travellingMember = EconomyTestData.HireBest( travelling, StaffType.Mechanic );
		travellingMember.AssignedInstanceId = 100;
		var original = World( travelling, Paths() );
		original.Tick( 0.5f );
		original.Tick( 2.75f );
		Assert.IsTrue( original.Snapshots.Single().Travelling, "the actor is en route" );
		var snapshot = original.CaptureSnapshot();
		var actor = snapshot.Actors.Single();
		Assert.AreEqual( travellingMember.Id, actor.Id );
		Assert.AreEqual( 0.25, actor.TravelSeconds, 1e-9, "the fraction of the next cell step survives" );

		// A fresh park hires the same candidate (the identical seeded pool), so the restored ids line up.
		var restoredPark = EconomyTestData.Park();
		var restoredMember = EconomyTestData.HireBest( restoredPark, StaffType.Mechanic );
		restoredMember.AssignedInstanceId = 100;
		Assert.AreNotSame( travellingMember, restoredMember );
		Assert.AreEqual( travellingMember.Id, restoredMember.Id );
		var restored = World( restoredPark, Paths() );
		Assert.AreEqual( (0.5f, 1.5f), (restoredMember.CellX!.Value, restoredMember.CellY!.Value), "the fresh world spawned its actor at the gate" );
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( (travellingMember.CellX!.Value, travellingMember.CellY!.Value), (restoredMember.CellX!.Value, restoredMember.CellY!.Value) );
		Assert.AreEqual( travellingMember.TravelSeconds, restoredMember.TravelSeconds, 1e-9 );

		// Both worlds now step identically: the restored actor reaches the job at the same moment.
		for ( var step = 0; step < 8; step++ )
		{
			original.Tick( 0.5f );
			restored.Tick( 0.5f );
			Assert.AreEqual( (travellingMember.CellX!.Value, travellingMember.CellY!.Value), (restoredMember.CellX!.Value, restoredMember.CellY!.Value), $"step {step}" );
			Assert.AreEqual( travellingMember.TravelSeconds, restoredMember.TravelSeconds, 1e-9, $"step {step}" );
		}
		Assert.IsFalse( original.Snapshots.Single().Travelling );
		Assert.AreEqual( original.Snapshots.Single().Travelling, restored.Snapshots.Single().Travelling );
	}

	[TestMethod]
	public void StaffSnapshotsKeepUnpositionedActorsUnpositioned()
	{
		var park = EconomyTestData.Park();
		EconomyTestData.HireBest( park, StaffType.Entertainer );
		var empty = new GuestPathGrid( 2, 2 ); // no walkable cell: no spawn, the actor stays unpositioned
		var world = new StaffWorldSimulation( empty, () => park.Staff.Members, _ => null );
		world.Synchronize();
		var snapshot = world.CaptureSnapshot();
		Assert.IsNull( snapshot.Actors.Single().CellX );

		var restoredPark = EconomyTestData.Park();
		EconomyTestData.HireBest( restoredPark, StaffType.Entertainer );
		var restored = new StaffWorldSimulation( new GuestPathGrid( 2, 2 ), () => restoredPark.Staff.Members, _ => null );
		restored.RestoreSnapshot( snapshot );
		Assert.IsNull( restoredPark.Staff.Members.Single().CellX );
	}

	[TestMethod]
	public void StaffSnapshotsRejectInvalidRecordsWithoutMutating()
	{
		var park = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var world = World( park, Paths() );
		world.Tick( 0.5f );
		var good = world.CaptureSnapshot();
		var id = member.Id;
		foreach ( var bad in new[]
		{
			good with { Actors = new StaffWorldSimulation.StaffActorSnapshot[] { new( id + 100, 1.5f, 1.5f, 0 ) } },
			good with { Actors = new StaffWorldSimulation.StaffActorSnapshot[] { new( id, 1.5f, 1.5f, 0 ), new( id, 2.5f, 1.5f, 0 ) } },
			good with { Actors = Array.Empty<StaffWorldSimulation.StaffActorSnapshot>() },
			good with { Actors = new StaffWorldSimulation.StaffActorSnapshot[] { new( id, 5f, 1.5f, 0 ) } },
			good with { Actors = new StaffWorldSimulation.StaffActorSnapshot[] { new( id, -0.5f, 1.5f, 0 ) } },
			good with { Actors = new StaffWorldSimulation.StaffActorSnapshot[] { new( id, 1.5f, 1.5f, StaffWorldSimulation.SecondsPerCell ) } },
			good with { Actors = new StaffWorldSimulation.StaffActorSnapshot[] { new( id, 1.5f, 1.5f, -0.25 ) } },
			good with { Actors = new StaffWorldSimulation.StaffActorSnapshot[] { new( id, 1.5f, 1.5f, double.NaN ) } },
			good with { Actors = new StaffWorldSimulation.StaffActorSnapshot[] { new( id, float.NaN, 1.5f, 0 ) } }
		} )
		{
			var freshPark = EconomyTestData.Park();
			var freshMember = EconomyTestData.HireBest( freshPark, StaffType.Mechanic );
			var fresh = World( freshPark, Paths() );
			var position = (freshMember.CellX!.Value, freshMember.CellY!.Value);
			Assert.ThrowsException<InvalidDataException>( () => fresh.ValidateSnapshot( bad ), string.Join( ',', bad.Actors!.Select( item => item?.Id.ToString() ?? "null" ) ) );
			Assert.ThrowsException<InvalidDataException>( () => fresh.RestoreSnapshot( bad ) );
			Assert.AreEqual( position, (freshMember.CellX!.Value, freshMember.CellY!.Value), "a rejected restore does not move the actor" );
		}
		var unpaired = good with { Actors = new StaffWorldSimulation.StaffActorSnapshot[] { new( id, null, 1.5f, 0 ) } };
		Assert.ThrowsException<InvalidDataException>( () => world.ValidateSnapshot( unpaired ) );
		Assert.ThrowsException<InvalidDataException>( () => world.ValidateSnapshot( good with { Actors = null! } ) );
		Assert.ThrowsException<ArgumentNullException>( () => world.ValidateSnapshot( null! ) );
	}
}
