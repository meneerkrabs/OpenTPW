using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Guest-owner null-guard probes (root's corrective round): every reference-typed part of the guest DTOs a
/// corrupt file can null out — the snapshot's core lists, a bridge's tables, a guest's navigation buffers — is
/// named as an explicit validation error through the real codec path, never a crash, and the refused restore
/// leaves the lived-in target untouched (whole simulation and single bridge alike).
/// </summary>
[TestClass]
public class GuestSnapshotNullGuardTests
{
	/// <summary>A lived-in park and its hash/queues, so every refused restore can prove it changed nothing.</summary>
	private sealed class Target
	{
		public readonly GuestSnapshotTests.Rig Rig = new();
		public readonly ulong Hash;
		public readonly List<int[]> Queues;

		public Target()
		{
			GuestSnapshotTests.RunUntil( Rig, rig => rig.Simulation.Guests.Any( guest => guest.State == GuestState.Queueing ), 0, 9000 );
			Hash = GuestSnapshotTests.CanonicalHash( Rig );
			Queues = Rig.Rides.Select( ride => ride.Bridge.Queue.ToArray() ).ToList();
		}

		public void AssertIntact( string name )
		{
			Assert.AreEqual( Hash, GuestSnapshotTests.CanonicalHash( Rig ), $"{name} left the target unchanged" );
			for ( var index = 0; index < Rig.Rides.Count; index++ )
				CollectionAssert.AreEqual( Queues[index], Rig.Rides[index].Bridge.Queue.ToArray(), $"{name} left the target's ride {index} queue unchanged" );
		}
	}

	private static void Rejects( Target target, GuestSimulationSnapshot corrupted, string name )
	{
		var validation = target.Rig.Simulation.ValidateSnapshot( corrupted );
		Assert.IsFalse( validation.IsValid, $"{name} must not validate" );
		Assert.ThrowsException<InvalidDataException>( () => target.Rig.Simulation.RestoreSnapshot( corrupted ), $"{name} must not restore" );
		target.AssertIntact( name );
	}

	/// <summary>Nulls one member through the snapshot's JSON form and reads it back through the actual codec, so
	/// every probe exercises the real file-load path (bytes → DTO → validation), not just in-memory DTOs.</summary>
	private static GuestSimulationSnapshot WithJsonNull( GuestSimulationSnapshot snapshot, Action<JsonObject> nullOut )
	{
		var root = JsonNode.Parse( ParkResumeJson.Serialize( snapshot ) )!.AsObject();
		nullOut( root );
		return ParkResumeJson.Deserialize<GuestSimulationSnapshot>( Encoding.UTF8.GetBytes( root.ToJsonString() ) );
	}

	// ---- The snapshot's core lists ----------------------------------------------------------------

	/// <summary>Files whose Attractions or SeenQueueEdits array is a JSON null are rejected explicitly with the
	/// missing list named — like a wholly missing snapshot — and the restore refuses them.</summary>
	[TestMethod]
	public void NullCoreListsFromTheFileAreRejectedExplicitly()
	{
		var target = new Target();
		var snapshot = target.Rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( target.Rig.Simulation.ValidateSnapshot( snapshot ).IsValid, "the unmodified snapshot validates" );

		var withoutAttractions = WithJsonNull( snapshot, root => root[nameof( GuestSimulationSnapshot.Attractions )] = null );
		var attractions = target.Rig.Simulation.ValidateSnapshot( withoutAttractions );
		Assert.IsFalse( attractions.IsValid, "a null attraction list must not validate" );
		Assert.IsTrue( attractions.Errors.Any( problem => problem.Contains( "Attractions is missing" ) ),
			"the missing attraction list is named: " + string.Join( "; ", attractions.Errors ) );
		Rejects( target, withoutAttractions, "null attraction list" );

		var withoutSeenEdits = WithJsonNull( snapshot, root => root[nameof( GuestSimulationSnapshot.SeenQueueEdits )] = null );
		var seenEdits = target.Rig.Simulation.ValidateSnapshot( withoutSeenEdits );
		Assert.IsFalse( seenEdits.IsValid, "a null seen-edit list must not validate" );
		Assert.IsTrue( seenEdits.Errors.Any( problem => problem.Contains( "SeenQueueEdits is missing" ) ),
			"the missing seen-edit list is named: " + string.Join( "; ", seenEdits.Errors ) );
		Rejects( target, withoutSeenEdits, "null seen-edit list" );
	}

	// ---- A guest's navigation buffers --------------------------------------------------------------

	/// <summary>Guests whose route or extra-open-cell list is a JSON null are named as validation errors instead
	/// of crashing the per-guest structure check, and the restore refuses the file.</summary>
	[TestMethod]
	public void NullNavigationBuffersFromTheFileAreRejectedExplicitly()
	{
		var target = new Target();
		var snapshot = target.Rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( target.Rig.Simulation.ValidateSnapshot( snapshot ).IsValid, "the unmodified snapshot validates" );

		var withoutRoute = WithJsonNull( snapshot, root => root["Guests"]![0]![nameof( GuestSnapshot.Navigation )]![nameof( GuestNavigationSnapshot.Route )] = null );
		var route = target.Rig.Simulation.ValidateSnapshot( withoutRoute );
		Assert.IsFalse( route.IsValid, "a guest without a route must not validate" );
		Assert.IsTrue( route.Errors.Any( problem => problem.Contains( "has no route" ) ),
			"the missing route is named: " + string.Join( "; ", route.Errors ) );
		Rejects( target, withoutRoute, "null route" );

		var withoutAlsoOpen = WithJsonNull( snapshot, root => root["Guests"]![0]![nameof( GuestSnapshot.Navigation )]![nameof( GuestNavigationSnapshot.AlsoOpen )] = null );
		var alsoOpen = target.Rig.Simulation.ValidateSnapshot( withoutAlsoOpen );
		Assert.IsFalse( alsoOpen.IsValid, "a guest without its extra-open-cell list must not validate" );
		Assert.IsTrue( alsoOpen.Errors.Any( problem => problem.Contains( "has no extra-open-cell list" ) ),
			"the missing extra-open-cell list is named: " + string.Join( "; ", alsoOpen.Errors ) );
		Rejects( target, withoutAlsoOpen, "null extra-open-cell list" );
	}

	// ---- A bridge's tables --------------------------------------------------------------------------

	/// <summary>Every bridge table a file can null out — the queue, rider, walking-off, hopped, limbo, bounce and
	/// queue-cell lists — is named by the whole-simulation validation and refused by the restore.</summary>
	[TestMethod]
	public void NullBridgeTablesFromTheFileAreRejectedExplicitly()
	{
		var target = new Target();
		var snapshot = target.Rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( target.Rig.Simulation.ValidateSnapshot( snapshot ).IsValid, "the unmodified snapshot validates" );
		var tables = new (string Member, string Named)[]
		{
			( nameof( RideVisitorBridgeSnapshot.Queue ), "the queue list is missing" ),
			( nameof( RideVisitorBridgeSnapshot.OnRide ), "the rider list is missing" ),
			( nameof( RideVisitorBridgeSnapshot.WalkingOff ), "the walking-off list is missing" ),
			( nameof( RideVisitorBridgeSnapshot.Hopped ), "the hopped set is missing" ),
			( nameof( RideVisitorBridgeSnapshot.Limbo ), "the limbo table is missing" ),
			( nameof( RideVisitorBridgeSnapshot.Bounce ), "the bounce table is missing" ),
			( nameof( RideVisitorBridgeSnapshot.QueueCells ), "the queue cell list is missing" )
		};
		foreach ( var (member, named) in tables )
		{
			var withoutTable = WithJsonNull( snapshot, root => root["Attractions"]![0]![member] = null );
			var validation = target.Rig.Simulation.ValidateSnapshot( withoutTable );
			Assert.IsFalse( validation.IsValid, $"a null {member} must not validate" );
			Assert.IsTrue( validation.Errors.Any( problem => problem.Contains( named ) ),
				$"the missing {member} is named: " + string.Join( "; ", validation.Errors ) );
			Rejects( target, withoutTable, $"null {member}" );
		}
	}

	/// <summary>The bridge's own restore refuses a snapshot with a null table before touching the live bridge:
	/// validation names the missing table, <see cref="RideVisitorBridge.RestoreSnapshot"/> throws, and a bridge
	/// with riders and a queue keeps both.</summary>
	[TestMethod]
	public void BridgeRestoreRefusesNullTablesBeforeTouchingTheBridge()
	{
		var rig = new GuestSnapshotTests.Rig();
		GuestSnapshotTests.RunUntil( rig, park => park.Rides.Any( ride => ride.Bridge.QueueLength > 0 && ride.Bridge.Riders.Count > 0 ), 0, 9000 );
		var bridge = rig.Rides.Select( ride => ride.Bridge ).First( candidate => candidate.QueueLength > 0 && candidate.Riders.Count > 0 );
		Assert.IsTrue( bridge.QueueLength > 0, "the target bridge holds a queue" );
		Assert.IsTrue( bridge.Riders.Count > 0, "the target bridge holds riders" );
		var queueBefore = bridge.Queue.ToArray();
		var ridersBefore = bridge.Riders.ToArray();
		var captured = bridge.CaptureSnapshot();
		Assert.AreEqual( 0, RideVisitorBridge.ValidateSnapshot( captured ).Count, "the captured bridge snapshot validates" );

		var tables = new (string Name, Func<RideVisitorBridgeSnapshot, RideVisitorBridgeSnapshot> NullOut)[]
		{
			( nameof( RideVisitorBridgeSnapshot.Queue ), snapshot => snapshot with { Queue = null! } ),
			( nameof( RideVisitorBridgeSnapshot.OnRide ), snapshot => snapshot with { OnRide = null! } ),
			( nameof( RideVisitorBridgeSnapshot.WalkingOff ), snapshot => snapshot with { WalkingOff = null! } ),
			( nameof( RideVisitorBridgeSnapshot.Hopped ), snapshot => snapshot with { Hopped = null! } ),
			( nameof( RideVisitorBridgeSnapshot.Limbo ), snapshot => snapshot with { Limbo = null! } ),
			( nameof( RideVisitorBridgeSnapshot.Bounce ), snapshot => snapshot with { Bounce = null! } ),
			( nameof( RideVisitorBridgeSnapshot.QueueCells ), snapshot => snapshot with { QueueCells = null! } )
		};
		foreach ( var (name, nullOut) in tables )
		{
			var corrupted = nullOut( captured );
			var problems = RideVisitorBridge.ValidateSnapshot( corrupted );
			Assert.IsTrue( problems.Count > 0, $"a null {name} must not validate" );
			Assert.IsTrue( problems.Any( problem => problem.Contains( "is missing" ) ),
				$"the missing {name} is named: " + string.Join( "; ", problems ) );
			Assert.ThrowsException<InvalidDataException>( () => bridge.RestoreSnapshot( corrupted ), $"a null {name} must not restore" );
		}
		CollectionAssert.AreEqual( queueBefore, bridge.Queue.ToArray(), "every refused restore left the queue unchanged" );
		CollectionAssert.AreEqual( ridersBefore, bridge.Riders.ToArray(), "every refused restore left the riders unchanged" );
	}
}
