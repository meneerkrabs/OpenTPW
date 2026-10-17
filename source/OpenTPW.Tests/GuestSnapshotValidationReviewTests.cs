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
/// Independent validation probes of the guest save lane: malformed file bytes (null entries, type confusion,
/// non-finite numbers) and extreme out-of-bounds DTO values must be rejected explicitly — never crash — and a
/// rejected restore must leave the lived-in target park untouched. Real admission (TryJoinQueue at the live room)
/// shows the queue-room validation matches current join policy for rides, shops and toilets alike, so nothing
/// legitimate is falsely rejected. One documented seam: the guest restore trusts the caller's grid and recomputes
/// queue geometry on the next tick when the grid version moved.
/// </summary>
[TestClass]
public class GuestSnapshotValidationReviewTests
{
	/// <summary>A lived-in park and its hash/queues, so every rejected restore can prove it changed nothing.</summary>
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

	/// <summary>The corrupt snapshot must fail validation, throw on restore, and leave the target intact.</summary>
	private static void Rejects( Target target, GuestSimulationSnapshot corrupted, string name )
	{
		var validation = target.Rig.Simulation.ValidateSnapshot( corrupted );
		Assert.IsFalse( validation.IsValid, $"{name} must not validate" );
		Assert.ThrowsException<InvalidDataException>( () => target.Rig.Simulation.RestoreSnapshot( corrupted ), $"{name} must not restore" );
		target.AssertIntact( name );
	}

	private static GuestSimulationSnapshot ValidSnapshot( Target target )
	{
		var snapshot = target.Rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( target.Rig.Simulation.ValidateSnapshot( snapshot ).IsValid, "the unmodified snapshot validates" );
		return snapshot;
	}

	private static GuestSimulationSnapshot RoundTrip( GuestSimulationSnapshot snapshot ) =>
		ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) );

	/// <summary>Edits the snapshot's JSON form with <see cref="JsonNode"/> and reads it back through the actual
	/// codec, so the probes exercise the real file-load path (bytes → DTO → validation), not just in-memory DTOs.</summary>
	private static GuestSimulationSnapshot WithJsonEdit( GuestSimulationSnapshot snapshot, Action<JsonObject> edit )
	{
		var root = JsonNode.Parse( ParkResumeJson.Serialize( snapshot ) )!.AsObject();
		edit( root );
		return ParkResumeJson.Deserialize<GuestSimulationSnapshot>( Encoding.UTF8.GetBytes( root.ToJsonString() ) );
	}

	private static GuestSimulationSnapshot WithGuest( GuestSimulationSnapshot snapshot, int index, Func<GuestSnapshot, GuestSnapshot> change ) => snapshot with
	{
		Guests = snapshot.Guests.Select( ( guest, at ) => at == index ? change( guest ) : guest ).ToArray()
	};

	// ---- Real admission against current join policy (no fabricated over-room state) -------------

	/// <summary>A five-cell ride queue filled to its live room through real <see cref="RideVisitorBridge.TryJoinQueue"/>
	/// calls validates through the codec: the snapshot's 4-guests-per-cell cap is exactly the join policy
	/// (0x100DC988 count &lt; 4 × cells), and the guest past the room is refused by admission, not by validation.</summary>
	[TestMethod]
	public void RideQueueFilledToItsLiveRoomByRealAdmissionValidatesThroughJson()
	{
		var grid = new GuestPathGrid( 8, 8 );
		for ( var x = 0; x < 8; x++ )
			grid.SetPath( x, 0, true );
		var bridge = new RideVisitorBridge( 3, "Room ride", RideVisitorKind.Ride, 5, 55, 30 )
		{
			EntranceCell = (2, 0),
			ExitCell = (2, 0),
			QueueFrontCell = (2, 5),
			QueueEntranceDirection = 2,
			Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
		};
		var script = GuestSnapshotTests.IdleScript( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ) );
		bridge.Attach( script, () => true );
		Assert.AreEqual( 5, QueuePaths.TryExtend( grid, bridge, Enumerable.Range( 0, 5 ).Select( index => (2, 5 - index) ) ) );
		QueuePaths.TryConnect( grid, bridge );
		Assert.AreEqual( 20, bridge.MaximumQueueLength, "the live room of five cells is twenty guests" );
		foreach ( var guest in Enumerable.Range( 1, 20 ) )
			Assert.IsTrue( bridge.TryJoinQueue( guest ), $"guest {guest} joined while there was room" );
		Assert.AreEqual( QueueJoinResult.NoRoom, bridge.JoinQueue( 21, 0 ), "the room refuses the twenty-first guest (current policy)" );

		var captured = bridge.CaptureSnapshot();
		Assert.AreEqual( 0, RideVisitorBridge.ValidateSnapshot( captured ).Count, "a queue at its live room is not over-strict" );
		var loaded = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( captured ) );
		var restored = new RideVisitorBridge( 3, "Room ride", RideVisitorKind.Ride, 5, 55, 30 );
		restored.Attach( GuestSnapshotTests.IdleScript( new VisitorRideScriptEffects( restored, UnimplementedRideScriptEffects.Instance ) ), () => true );
		restored.RestoreSnapshot( loaded );
		CollectionAssert.AreEqual( Enumerable.Range( 1, 20 ).ToArray(), restored.Queue.ToArray(), "the full queue restored front-first through the codec" );
	}

	/// <summary>Shops and toilets (no <c>Info.HasQueue</c>): the data limit is <see cref="RideVisitorBridge.NoQueueLimit"/>
	/// (100), the physical room of their one front cell is four, and real admission joins exactly those four — a
	/// snapshot of that state validates, so the virtual queue is not falsely rejected.</summary>
	[TestMethod]
	public void ShopAndToiletVirtualQueuesValidateAtTheirLiveAdmissionRoomThroughJson()
	{
		foreach ( var (satisfies, name) in new[] { (GuestNeeds.Thirst, "drink shop"), (GuestNeeds.Toilet, "toilet") } )
		{
			var grid = new GuestPathGrid( 4, 4 );
			grid.SetPath( 0, 0, true );
			var bridge = new RideVisitorBridge( 5, name, RideVisitorKind.Shop, 1, 70, 30, satisfies );
			bridge.EntranceCell = (0, 0);
			bridge.ExitCell = (0, 0);
			bridge.Attach( GuestSnapshotTests.IdleScript( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ) ), () => true );
			bridge.RecomputeQueue( grid );
			Assert.AreEqual( RideVisitorBridge.NoQueueLimit, bridge.QueueLimit, $"the {name} keeps the no-queue data limit" );
			Assert.AreEqual( 4, bridge.MaximumQueueLength, $"the {name}'s one-cell room admits four (room binds before the limit)" );
			foreach ( var guest in new[] { 31, 32, 33, 34 } )
				Assert.IsTrue( bridge.TryJoinQueue( guest ), $"{name}: guest {guest} joined at the live room" );
			Assert.AreEqual( QueueJoinResult.NoRoom, bridge.JoinQueue( 35, 0 ), $"{name}: the room refuses the fifth guest (current policy)" );

			var captured = bridge.CaptureSnapshot();
			Assert.AreEqual( 0, RideVisitorBridge.ValidateSnapshot( captured ).Count, $"the {name}'s virtual queue is not falsely rejected" );
			var loaded = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( captured ) );
			var restored = new RideVisitorBridge( 5, name, RideVisitorKind.Shop, 1, 70, 30, satisfies );
			restored.RestoreSnapshot( loaded );
			CollectionAssert.AreEqual( new[] { 31, 32, 33, 34 }, restored.Queue.ToArray(), $"the {name} queue restored through the codec" );
		}
	}

	/// <summary>A shop registered in a whole simulation captures and validates as part of the guest snapshot
	/// (its bridge-local queue checks included), so the supported slice can save parks with shops.</summary>
	[TestMethod]
	public void RegisteredShopValidatesInsideTheWholeSimulationSnapshot()
	{
		var target = new Target();
		var grid = new GuestPathGrid( 4, 4 );
		grid.SetPath( 0, 0, true );
		var shop = new RideVisitorBridge( 7, "Snack bar", RideVisitorKind.Shop, 1, 70, 30, GuestNeeds.Hunger ) { EntranceCell = (0, 0), ExitCell = (0, 0) };
		shop.Attach( GuestSnapshotTests.IdleScript( new VisitorRideScriptEffects( shop, UnimplementedRideScriptEffects.Instance ) ), () => true );
		target.Rig.Simulation.Register( shop );
		var snapshot = target.Rig.Simulation.CaptureSnapshot();
		var validation = target.Rig.Simulation.ValidateSnapshot( snapshot );
		Assert.IsTrue( validation.IsValid, "the park with a registered shop validates: " + string.Join( "; ", validation.Errors ) );
		Assert.IsTrue( RoundTrip( snapshot ).Attractions.Any( attraction => attraction.AttractionId == 7 ), "the shop attraction survives the codec" );
	}

	// ---- Malformed file bytes: explicit rejection, target preserved ------------------------------

	/// <summary>A null guest entry in the file is an explicit validation error, and the restore refuses it.</summary>
	[TestMethod]
	public void NullGuestEntryFromTheFileIsRejectedExplicitly()
	{
		var target = new Target();
		var snapshot = ValidSnapshot( target );
		var withNullGuest = WithJsonEdit( snapshot, root => root["Guests"]![0] = null );
		var validation = target.Rig.Simulation.ValidateSnapshot( withNullGuest );
		Assert.IsFalse( validation.IsValid, "a null guest entry must not validate" );
		Assert.IsTrue( validation.Errors.Any( problem => problem.Contains( "Guests[0] is missing" ) ), "the missing guest is named: " + string.Join( "; ", validation.Errors ) );
		Rejects( target, withNullGuest, "null guest entry" );
	}

	/// <summary>A null element inside an int array (the queue) cannot become a guest id: the codec rejects it.</summary>
	[TestMethod]
	public void NullQueueEntryFromTheFileIsRejectedByTheCodec()
	{
		var target = new Target();
		var snapshot = ValidSnapshot( target );
		var queueName = nameof( RideVisitorBridgeSnapshot.Queue );
		Assert.ThrowsException<JsonException>( () => WithJsonEdit( snapshot, root => root["Attractions"]![0]![queueName] = new JsonArray( 41, null ) ),
			"a null queue entry is not a guest id" );
		target.AssertIntact( "null queue entry" );
	}

	/// <summary>Schema mismatch, unknown members, wrong value types and non-finite numbers are refused before any
	/// restore; nothing touches the target.</summary>
	[TestMethod]
	public void SchemaTypeAndNumberAbuseIsRejectedBeforeAnyRestore()
	{
		var target = new Target();
		var snapshot = ValidSnapshot( target );

		Rejects( target, WithJsonEdit( snapshot, root => root[nameof( GuestSimulationSnapshot.SchemaVersion )] = 99 ), "future schema version" );
		Assert.ThrowsException<JsonException>( () => WithJsonEdit( snapshot, root => root["unknownMember"] = 1 ),
			"the codec disallows unmapped members" );
		Assert.ThrowsException<JsonException>( () => WithJsonEdit( snapshot, root => root["Guests"]![0]![nameof( GuestSnapshot.Hunger )] = "starving" ),
			"a string is not a hunger float" );
		Assert.ThrowsException<JsonException>( () => WithJsonEdit( snapshot, root => root["Guests"]![0]![nameof( GuestSnapshot.Hunger )] = "NaN" ),
			"NaN is not a JSON number" );
		// A float field overflow either fails the codec parse or lands as a non-finite value the validation rejects.
		var overflow = false;
		GuestSimulationSnapshot? huge = null;
		try
		{
			huge = WithJsonEdit( snapshot, root => root["Guests"]![0]![nameof( GuestSnapshot.Hunger )] = 1e300 );
			overflow = true;
		}
		catch ( JsonException )
		{
		}
		if ( overflow )
			Rejects( target, huge!, "hunger overflow" );
		target.AssertIntact( "schema, type and number abuse" );
	}

	/// <summary>Out-of-bounds DTO numbers — non-finite needs, negative money and counters, speeds outside the walk
	/// ranges, navigation buffers past their route — are named as validation errors and refused by the restore.</summary>
	[TestMethod]
	public void ExtremeDtoBoundsAreRejectedExplicitly()
	{
		var target = new Target();
		var snapshot = ValidSnapshot( target );
		var first = 0;

		Rejects( target, WithGuest( snapshot, first, guest => guest with { Hunger = float.NaN } ), "NaN hunger" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { Happiness = float.PositiveInfinity } ), "infinite happiness" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { OffsetX = float.NegativeInfinity } ), "infinite offset" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { StateTimer = float.NaN } ), "NaN state timer" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { Money = -1 } ), "negative money" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { RidesTaken = -1 } ), "negative rides taken" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { QueueMoveDelay = -1 } ), "negative queue move delay" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { QueueCellIndex = -1 } ), "negative queue cell index" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { BaseSpeed = 59 } ), "walk speed below the table" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { BaseSpeed = 141 } ), "walk speed above the table" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { MaxSpeed = GuestSpeed.VelocityBound + 1 } ), "max speed above the cap" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { MaxForce = GuestSpeed.Floor - 1 } ), "max force below the floor" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with
		{
			Navigation = guest.Navigation with { VelocityX = GuestSpeed.VelocityBound + 1 }
		} ), "navigation velocity above the cap" );
		Rejects( target, WithJsonEdit( snapshot, root => root["Guests"]![0]!["Navigation"]!["VelocityX"] = int.MinValue ), "minimum navigation velocity from JSON" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with
		{
			Navigation = guest.Navigation with { Cursor = guest.Navigation.Route.Length + 1 }
		} ), "navigation cursor past the route" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with
		{
			Navigation = guest.Navigation with { Count = 0, Index = 1 }
		} ), "waypoint index without waypoints" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with
		{
			Navigation = guest.Navigation with { History = 0x8000 }
		} ), "progress history past its 15-bit window" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with
		{
			Navigation = guest.Navigation with { PreviousRemaining = -2 }
		} ), "previous remaining below minus one" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with
		{
			Navigation = guest.Navigation with { Reroutes = -1 }
		} ), "negative reroute count" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { Thought = unchecked( (GuestThought)999 ) } ), "undefined thought" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { State = (GuestState)25 } ), "undefined guest state" );
		Rejects( target, WithGuest( snapshot, first, guest => guest with { Type = 99 } ), "guest type beyond the park's types" );
		Rejects( target, snapshot with { SeenQueueEdits = snapshot.SeenQueueEdits.Append( (snapshot.Attractions[0].AttractionId, 0) ).ToArray() }, "seen-edit counter repeated" );
		Rejects( target, snapshot with { SeenQueueEdits = snapshot.SeenQueueEdits.Select( ( edit, index ) => index == 0 ? (edit.AttractionId, -2) : edit ).ToArray() }, "seen-edit counter below minus one" );
		Rejects( target, snapshot with
		{
			Attractions = snapshot.Attractions.Select( ( attraction, index ) => index == 0
				? attraction with { QueueCells = new[] { (999, 999) }.Concat( attraction.QueueCells.Skip( 1 ) ).ToArray() }
				: attraction ).ToArray()
		}, "queue cell outside the map" );
		Rejects( target, snapshot with
		{
			Attractions = snapshot.Attractions.Select( ( attraction, index ) => index == 0 ? attraction with { JoinCell = (999, 999) } : attraction ).ToArray()
		}, "join cell outside the map" );
		Rejects( target, snapshot with { Attractions = snapshot.Attractions.Select( ( attraction, index ) => index == 0 ? attraction with { ReleasedTotal = -1 } : attraction ).ToArray() },
			"negative released total" );
		Rejects( target, snapshot with { SchemaVersion = 7, NextGuestId = 1 }, "next guest id below every saved guest of a save from before the thing pool" );
		var laneIndex = Array.FindIndex( snapshot.Guests, guest => guest.Lane != null );
		if ( laneIndex >= 0 )
			Rejects( target, WithGuest( snapshot, laneIndex, guest => guest with { LaneIndex = guest.Lane!.Length } ), "lane index past the lane" );
		target.AssertIntact( "extreme bounds" );
	}

	// ---- Null file entries in reference-typed snapshot parts: explicit rejection, target untouched -------
	// The three probes below pin the guest owner's null guards (root's R2 fix): a JSON null that deserialises
	// into a reference-typed snapshot part is named as a validation error — never a NullReferenceException —
	// and the refused restore leaves the lived-in target bit-identical.

	/// <summary>A null attraction entry in the file is an explicit validation error naming the broken attraction,
	/// and the restore refuses it.</summary>
	[TestMethod]
	public void NullAttractionEntryFromTheFileIsRejectedExplicitly()
	{
		var target = new Target();
		var snapshot = ValidSnapshot( target );
		var withNullAttraction = WithJsonEdit( snapshot, root => root["Attractions"]![0] = null );
		var validation = target.Rig.Simulation.ValidateSnapshot( withNullAttraction );
		Assert.IsFalse( validation.IsValid, "a null attraction entry must not validate" );
		Rejects( target, withNullAttraction, "null attraction entry" );
	}

	/// <summary>A file whose Guests array is null is an explicit validation error naming the missing guest list,
	/// and the restore refuses it.</summary>
	[TestMethod]
	public void NullGuestsArrayFromTheFileIsRejectedExplicitly()
	{
		var target = new Target();
		var snapshot = ValidSnapshot( target );
		var withNullGuests = WithJsonEdit( snapshot, root => root[nameof( GuestSimulationSnapshot.Guests )] = null );
		var validation = target.Rig.Simulation.ValidateSnapshot( withNullGuests );
		Assert.IsFalse( validation.IsValid, "a null guest list must not validate" );
		Rejects( target, withNullGuests, "null guest list" );
	}

	/// <summary>A guest whose Navigation object is null is an explicit validation error naming the guest, and the
	/// restore refuses it.</summary>
	[TestMethod]
	public void NullNavigationFromTheFileIsRejectedExplicitly()
	{
		var target = new Target();
		var snapshot = ValidSnapshot( target );
		var withNullNavigation = WithJsonEdit( snapshot, root => root["Guests"]![0]![nameof( GuestSnapshot.Navigation )] = null );
		var validation = target.Rig.Simulation.ValidateSnapshot( withNullNavigation );
		Assert.IsFalse( validation.IsValid, "a guest without navigation must not validate" );
		Rejects( target, withNullNavigation, "null navigation" );
	}

	// ---- Documented seam: the guest restore trusts the caller's grid ------------------------------

	/// <summary>
	/// The guest snapshot records <see cref="GuestSimulationSnapshot.SeenGridVersion"/> and per-bridge queue cells,
	/// but validation never compares them with the target grid: a restore into a grid whose queue geometry differs
	/// (a stale or diverged layout restore) is accepted, and the next simulation tick recomputes the geometry from
	/// the caller's grid and re-evaluates the queued guests. The envelope — not the guest lane — must therefore pin
	/// the grid the snapshot was saved with.
	/// </summary>
	[TestMethod]
	public void RestoreTrustsTheCallerGridAndRecomputesQueueGeometryOnTheNextTick()
	{
		var park = new GuestSnapshotTests.Rig();
		GuestSnapshotTests.RunUntil( park, rig => rig.Rides[0].Bridge.QueueLength >= 3, 0, 9000 );
		var snapshot = park.Simulation.CaptureSnapshot();
		var loaded = RoundTrip( snapshot );
		Assert.AreEqual( park.Simulation.Grid.Version, loaded.SeenGridVersion, "the snapshot carries the grid version the queues synced with" );
		Assert.AreEqual( 5, loaded.Attractions[0].QueueCells.Length, "the ride saved its five-cell queue" );

		// A caller grid whose first ride lost its whole queue (five cells removed): a legitimately different layout.
		var resumed = new GuestSnapshotTests.Rig();
		Assert.AreEqual( 5, QueuePaths.RemoveFrom( resumed.Grid, resumed.Rides[0].Bridge, 2, 5 ) );
		resumed.Simulation.RestoreSnapshot( loaded );
		foreach ( var attraction in loaded.Attractions )
			resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script[RideVariables.VAR_LETMEON] =
				attraction.LetMeOnWritten ? attraction.CalledGuest : 0;
		CollectionAssert.AreEqual( snapshot.Attractions[0].Queue, resumed.Rides[0].Bridge.Queue.ToArray(),
			"the restore commits the saved queue although the caller grid disagrees (documented seam)" );

		resumed.Step();
		var bridge = resumed.Rides[0].Bridge;
		Assert.IsTrue( bridge.QueueCells.Count < 5, "the next tick recomputed the queue from the caller's grid" );
		Assert.IsTrue( bridge.QueueLength <= RideVisitorBridge.PositionsPerCell * bridge.QueueSizeInCells,
			$"the re-evaluation left at most the new room ({bridge.QueueLength} guests in {bridge.QueueCells.Count} cells)" );
		Assert.AreEqual( 5, park.Rides[0].Bridge.QueueCells.Count, "the saving park keeps its own geometry" );
	}
}
