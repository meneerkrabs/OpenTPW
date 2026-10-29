using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.QueueTests;

namespace OpenTPW.Tests;

/// <summary>
/// The queues of an original save (tp.exe attraction serializer <c>0x004DB7D0</c>, guest serializer <c>0x004FB530</c>): the head <c>mFirstInQ</c>, the guests' <c>mQNext</c>/<c>mQPrev</c> links and the
/// called guest <c>mPersonBeingLoaded</c> are the loader's whole queue (nothing relinks them), so a saved queue restores its guests in states 11 to 14, and an interlude of state 11, in the saved order.
/// </summary>
[TestClass]
public class OriginalQueueImportTests
{
	private const int RideThing = 30;
	private const int RideId = 1;
	private const int Turn = 755;

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static SaveGuest Decode( SaveTestPayloads.GuestFields fields, int id )
	{
		var body = SaveTestPayloads.Guest( fields );
		var payload = new byte[8 + body.Length];
		body.CopyTo( payload, 8 );
		return SaveGuest.Read( payload, new SaveThing( id, SaveThingModel.Guest, 0, 0, 8, body.Length ) );
	}

	private static ushort At( int cell, int sub = 128 ) => (ushort)(cell * 256 + sub);

	private static SaveAttraction Ride( int id = RideThing, ushort first = 0, ushort loading = 0 ) =>
		SaveAttraction.Read( SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields() ), 0, id ) with { FirstInQueue = first, PersonBeingLoaded = loading };

	/// <summary>A guest of the queue column x = 2 (cells (2, 5) front to (2, 1) back): <paramref name="slot"/> is its cell in that column.</summary>
	private static SaveGuest Queued( int id, int slot, uint state, ushort next, ushort previous, byte position, uint admitted = 0, uint savedState = 6, uint moveDelay = 0, uint spotStamp = 0, ushort major = RideThing, byte personType = 1, int cellX = 2 )
	{
		var cell = 5 - slot;
		return Decode( new SaveTestPayloads.GuestFields
		{
			PersonType = personType, Position = (At( cellX ), At( cell )), Destination = (At( 2, 130 ), At( cell, 64 )), State = state, MajorDest = major, QNext = next, QPrev = previous, QueuePos = position,
			BeenAdmitted = admitted, SavedState = savedState, QueueMoveDelay = moveDelay, TimeOfLastSpotAnim = spotStamp, TimeStartedIdling = 700
		}, id );
	}

	private static Rig NewRig( bool takesGuests = false ) => new( queueCells: 5, takesGuests: takesGuests, kind: takesGuests ? RideVisitorKind.Shop : null, satisfies: takesGuests ? GuestNeeds.Thirst : GuestNeeds.None );

	private static IReadOnlyList<string> Import( Rig rig, SaveGuest[] guests, SaveAttraction[]? rides = null ) =>
		rig.Simulation.ImportOriginal( guests, Turn, thing => thing == RideThing ? RideId : 0, _ => 0, 9, rides ?? new[] { Ride( first: (ushort)guests[0].Id ) } );

	private static Guest Of( Rig rig, int id ) => rig.Simulation.Guests.Single( guest => guest.Id == id );

	[TestMethod]
	public void AStandingQueueRestoresItsChainFrontFirstWithTheSavedPlaces()
	{
		var rig = NewRig();
		var report = Import( rig, new[]
		{
			Queued( 41, 0, 11, 42, 0, 0, moveDelay: 0 ),
			Queued( 42, 1, 11, 43, 41, 1, moveDelay: 2 ),
			Queued( 43, 1, 11, 0, 42, 2, moveDelay: 4 ),
		} );
		CollectionAssert.AreEqual( new[] { 41, 42, 43 }, rig.Ride.Queue.ToArray(), "the saved links, front first" );
		Assert.AreEqual( 0, rig.Ride.GetQueuePosition( 41 ) );
		Assert.AreEqual( 2, rig.Ride.GetQueuePosition( 43 ) );
		foreach ( var id in new[] { 41, 42, 43 } )
		{
			var guest = Of( rig, id );
			Assert.AreEqual( (GuestState.Queueing, RideId), (guest.State, guest.AttractionId), $"guest {id}" );
			Assert.IsFalse( guest.InQueueInterlude );
		}
		Assert.AreEqual( (0, 1, 2), (Of( rig, 41 ).QueuePosition, Of( rig, 42 ).QueuePosition, Of( rig, 43 ).QueuePosition), "mQueuePos (+0x1F1) as saved" );
		Assert.AreEqual( (0, 2, 4), (Of( rig, 41 ).QueueMoveDelay, Of( rig, 42 ).QueueMoveDelay, Of( rig, 43 ).QueueMoveDelay), "mQueueMoveDelay (+0x1F4) as saved" );
		Assert.AreEqual( 0, rig.Ride.CalledGuest );
		StringAssert.Contains( report.Single( line => line.StartsWith( "Attraction queues imported:", StringComparison.Ordinal ) ), "1 saved queues (3 guests" );
		Assert.IsFalse( report.Any( line => line.Contains( "Guests not imported" ) ), "no guest is dropped" );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).Errors ) );
		// the guests stay queued through the turns that follow (the ride only yields)
		rig.HoldAdmission();
		rig.RunTurns( 5 );
		CollectionAssert.AreEqual( new[] { 41, 42, 43 }, rig.Ride.Queue.ToArray() );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid );
	}

	[TestMethod]
	public void AGuestMovingUpWalksOnToItsSavedPlaceAndStandsThere()
	{
		var rig = NewRig();
		Import( rig, new[]
		{
			Queued( 41, 0, 11, 42, 0, 0 ),
			Queued( 42, 3, 12, 0, 41, 1 ),
		} );
		var walker = Of( rig, 42 );
		Assert.AreEqual( GuestState.MovingUpQueue, walker.State );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).Errors ) );
		Assert.IsTrue( walker.HasWaypointForTest(), "the loader routes to the saved accurate destination (0x004F9407)" );
		rig.HoldAdmission();
		for ( var tick = 0; tick < 3000 && walker.State == GuestState.MovingUpQueue; tick++ )
			rig.Step();
		Assert.AreEqual(GuestState.Queueing, walker.State, "the walk ended in state 11" );
		CollectionAssert.AreEqual( new[] { 41, 42 }, rig.Ride.Queue.ToArray() );
	}

	[TestMethod]
	public void ACalledHeadKeepsItsCallAndWalksToTheStandPoint()
	{
		var rig = NewRig( takesGuests: true );
		Import( rig, new[]
		{
			Queued( 41, 0, 11, 42, 0, 0, admitted: 1 ),
			Queued( 42, 1, 11, 0, 41, 1 ),
		}, new[] { Ride( first: 41, loading: 41 ) } );
		Assert.AreEqual( 41, rig.Ride.CalledGuest, "mPersonBeingLoaded (+0x6C) is the called guest" );
		Assert.IsTrue( Of( rig, 41 ).QueueCalled, "the flag +0x1F8 is the call its state-11 update answers" );
		Assert.IsFalse( Of( rig, 42 ).QueueCalled );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).Errors ) );
		rig.RunTurns( 3 );
		Assert.AreEqual( GuestState.Boarding, Of( rig, 41 ).State, "state 13: the walk to the stand point (0x004FFFF0)" );
	}

	[TestMethod]
	public void AHeadHandedToTheScriptWaitsUntilTheScriptHasTakenIt()
	{
		var rig = NewRig( takesGuests: true );
		rig.Script[RideVariables.VAR_LETMEON] = 41;
		Import( rig, new[]
		{
			Queued( 41, 0, 14, 42, 0, 0, admitted: 1 ),
			Queued( 42, 1, 11, 0, 41, 1 ),
		} );
		Assert.AreEqual( (GuestState.WaitingToBoard, 41), (Of( rig, 41 ).State, rig.Ride.CalledGuest), "the pending visitor is the head the script holds" );
		CollectionAssert.AreEqual( new[] { 41, 42 }, rig.Ride.Queue.ToArray(), "the guest stays in the list until the script took it (0x004E0A70)" );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).Errors ) );
		// the script's variable 0 still equals mFirstInQ: the guest waits
		rig.Ride.HostStep();
		Assert.AreEqual( GuestState.WaitingToBoard, Of( rig, 41 ).State );
		// the script consumes it (VAR_LETMEON 0): the guest boards
		rig.Script[RideVariables.VAR_LETMEON] = 0;
		rig.Ride.HostStep();
		rig.RunTurns( 2 );
		Assert.AreEqual( GuestState.Using, Of( rig, 41 ).State, "state 16 once the variable differs from mFirstInQ" );
	}

	[TestMethod]
	public void AnIdleInterludeOfAQueuedGuestKeepsItsPlaceInTheQueue()
	{
		var rig = NewRig();
		Import( rig, new[]
		{
			Queued( 41, 0, 8, 42, 0, 0, savedState: 11, spotStamp: Turn - 3 ),
			Queued( 42, 1, 11, 0, 41, 1 ),
		} );
		var guest = Of( rig, 41 );
		Assert.AreEqual( (GuestState.Queueing, true), (guest.State, guest.InQueueInterlude), "state 8 with mSavedState 11 (0x004FC800) is the interlude of the queue" );
		Assert.AreEqual( 0, guest.InterludeTurn, "a stamp before the save counts from the import (SAVE-002): the interlude has its ten turns left" );
		CollectionAssert.AreEqual( new[] { 41, 42 }, rig.Ride.Queue.ToArray() );
		rig.HoldAdmission();
		rig.RunTurns( 8 );
		Assert.IsTrue( guest.InQueueInterlude, "still in the interlude after eight of its ten turns" );
		rig.RunTurns( 12 );
		Assert.IsFalse( guest.InQueueInterlude, "the interlude ends after its ten turns" );
		Assert.AreEqual( GuestState.Queueing, guest.State );
	}

	[TestMethod]
	public void AQueueThatIsNotTheLoadersChainIsRefusedWithItsReason()
	{
		(string Case, SaveGuest[] Guests, SaveAttraction[] Rides, string Reason)[] cases =
		{
			("back link", new[] { Queued( 41, 0, 11, 42, 0, 0 ), Queued( 42, 1, 11, 0, 77, 1 ) }, new[] { Ride( first: 41 ) }, "guest 42 links back to 77, not 41"),
			("loop", new[] { Queued( 41, 0, 11, 42, 0, 0 ), Queued( 42, 1, 11, 41, 41, 1 ) }, new[] { Ride( first: 41 ) }, "the links loop at guest 41"),
			("missing guest", new[] { Queued( 41, 0, 11, 99, 0, 0 ) }, new[] { Ride( first: 41 ) }, "guest 99 is not a saved guest"),
			("other state", new[] { Queued( 41, 0, 11, 42, 0, 0 ), Queued( 42, 1, 6, 0, 41, 1 ) }, new[] { Ride( first: 41 ) }, "guest 42 is in state 6"),
			("other ride", new[] { Queued( 41, 0, 11, 42, 0, 0 ), Queued( 42, 1, 11, 0, 41, 1, major: 31 ) }, new[] { Ride( first: 41 ) }, "guest 42 names attraction thing 31"),
			("called not head", new[] { Queued( 41, 0, 11, 42, 0, 0 ), Queued( 42, 1, 11, 0, 41, 1 ) }, new[] { Ride( first: 41, loading: 42 ) }, "the called guest 42 is not the head 41"),
			("called behind", new[] { Queued( 41, 0, 11, 42, 0, 0 ), Queued( 42, 1, 13, 0, 41, 1 ) }, new[] { Ride( first: 41 ) }, "guest 42 is called forward (state 13) behind the head"),
			("unknown ride", new[] { Queued( 41, 0, 11, 0, 0, 0 ) }, new[] { Ride( id: 31, first: 41 ) }, "the park does not place the attraction"),
			("person type", new[] { Queued( 41, 0, 11, 0, 0, 0, personType: 200 ) }, new[] { Ride( first: 41 ) }, "guest 41 has type 200"),
			("outside the map", new[] { Queued( 41, 0, 11, 0, 0, 0, cellX: 200 ) }, new[] { Ride( first: 41 ) }, "guest 41 stands outside the map"),
			("called without queue", new[] { Queued( 41, 0, 11, 0, 0, 0 ) }, new[] { Ride( loading: 41 ) }, "the called guest 41 has no queue"),
		};
		foreach ( var (name, guests, rides, reason) in cases )
		{
			var rig = NewRig();
			var report = rig.Simulation.ImportOriginal( guests, Turn, thing => thing == RideThing ? RideId : 0, _ => 0, 9, rides );
			if ( name is "person type" or "outside the map" )
			{
				Assert.AreEqual( 0, rig.Ride.QueueLength, name );
				StringAssert.Contains( string.Join( "\n", report ), reason, name );
				continue;
			}
			// the guests of a refused queue are dismissed to deciding at their cells, as the original dismisses a guest whose ride holds no place for it (0x00500136)
			Assert.AreEqual( guests.Length, rig.Simulation.Guests.Count( guest => guest.State == GuestState.DoingNothing || guest.State == GuestState.WalkingAround ), $"{name}: every guest of a refused queue is imported deciding" );
			Assert.AreEqual( 0, rig.Ride.QueueLength, name );
			StringAssert.Contains( string.Join( "\n", report ), reason, name );
		}
	}

	[TestMethod]
	public void GuestsOfNoSavedChainAreDismissedToDeciding()
	{
		var rig = NewRig();
		// the saved attraction holds no head (mFirstInQ 0): the guests that name it as queued have no chain
		var report = Import( rig, new[] { Queued( 41, 0, 11, 0, 0, 0 ), Queued( 42, 1, 8, 0, 0, 0, savedState: 11, spotStamp: Turn ) }, new[] { Ride() } );
		StringAssert.Contains( string.Join( "\n", report ), "guest 41 (state 11) is in no saved queue this import accepts" );
		foreach ( var id in new[] { 41, 42 } )
			Assert.AreEqual( GuestState.DoingNothing, Of( rig, id ).State, $"guest {id} is dismissed like QUEUE-010 (the interlude returns to 11 at 0x004FC890)" );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid );
	}

	[TestMethod]
	public void AnInterludeOfASaveWithoutAttractionsStillImportsAsDeciding()
	{
		var rig = NewRig();
		rig.Simulation.ImportOriginal( new[] { Queued( 41, 0, 8, 0, 0, 0, savedState: 11, spotStamp: Turn ) }, Turn, _ => RideId, _ => 0 );
		Assert.AreEqual( GuestState.DoingNothing, Of( rig, 41 ).State );
	}

	[TestMethod]
	public void AHeadWhoseScriptDoesNotHoldItIsCalledNotAdmitted()
	{
		var rig = NewRig( takesGuests: true );
		// the script import was refused: VAR_LETMEON stays 0
		Import( rig, new[]
		{
			Queued( 41, 0, 14, 42, 0, 0, admitted: 1 ),
			Queued( 42, 1, 11, 0, 41, 1 ),
		} );
		var notes = rig.Simulation.ReconcileOriginalAdmissions();
		StringAssert.Contains( string.Join( "\n", notes ), "guest 41 (queue state 14) is called but not admitted" );
		Assert.AreEqual( (GuestState.Boarding, 41), (Of( rig, 41 ).State, rig.Ride.CalledGuest) );
		rig.Ride.HostStep();
		Assert.AreNotEqual( GuestState.Using, Of( rig, 41 ).State, "the host step does not board a head no script took" );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).Errors ) );
	}

	[TestMethod]
	public void TheAdmissionReconcileRunsForEveryOriginalParkWhetherOrNotItsRsseBlockReads()
	{
		var root = Directory.GetCurrentDirectory();
		while ( root != null && !File.Exists( Path.Combine( root, "source/OpenTPW/World/Level.cs" ) ) )
			root = Path.GetDirectoryName( root );
		Assert.IsNotNull( root, "the repository root" );
		var level = File.ReadAllText( Path.Combine( root, "source/OpenTPW/World/Level.cs" ) );
		StringAssert.Matches( level, new System.Text.RegularExpressions.Regex( @"ImportOriginalScripts\(\);\s*ReconcileOriginalAdmissions\(\);" ) );
		var objects = File.ReadAllText( Path.Combine( root, "source/OpenTPW/World/Level.Objects.cs" ) );
		var scripts = objects.Split( "private void ImportOriginalScripts()" )[1].Split( "\n\t}\n" )[0];
		Assert.IsFalse( scripts.Contains( "ReconcileOriginalAdmissions" ), "the Rsse == null early return of the script import must not skip it" );
	}

	[TestMethod]
	public void AHeadTheScriptHoldsStaysHandedToIt()
	{
		var rig = NewRig( takesGuests: true );
		rig.Script[RideVariables.VAR_LETMEON] = 41;
		Import( rig, new[] { Queued( 41, 0, 14, 0, 0, 0, admitted: 1 ) } );
		Assert.AreEqual( 0, rig.Simulation.ReconcileOriginalAdmissions().Count );
		Assert.AreEqual( GuestState.WaitingToBoard, Of( rig, 41 ).State );
	}

	[TestMethod]
	public void QueuedGuestsOfASaveWithoutAttractionsAreStillReportedNotImported()
	{
		var rig = NewRig();
		var report = rig.Simulation.ImportOriginal( new[] { Queued( 41, 0, 11, 0, 0, 0 ) }, Turn, _ => RideId, _ => 0 );
		Assert.AreEqual( 0, rig.Simulation.Guests.Count );
		StringAssert.Contains( string.Join( "\n", report ), "guest 41 (state 11)" );
	}

	[TestMethod]
	public void AGuestLeavingARideWalksOnToItsSavedDestinationAndKeepsTheRideItLeft()
	{
		var rig = NewRig();
		var leaving = Decode( new SaveTestPayloads.GuestFields
		{
			Position = (At( 2, 200 ), At( 0, 20 )), Destination = (At( 5, 128 ), At( 0, 128 )), State = 15, MajorDest = RideThing, QueuePos = 0, TimeStartedIdling = 700
		}, 44 );
		var report = rig.Simulation.ImportOriginal( new[] { leaving }, Turn, thing => thing == RideThing ? RideId : 0, _ => 0, 9, Array.Empty<SaveAttraction>() );
		var guest = Of( rig, 44 );
		Assert.AreEqual( (GuestState.ExitingRide, RideId, true), (guest.State, guest.AttractionId, guest.HasWaypointForTest()), "state 15 keeps the ride it left (+0x1DC) and walks to the saved accurate destination" );
		Assert.IsFalse( report.Any( line => line.Contains( "imported off the paths" ) ), "a ride's exit cell is no path, and no recovery is reported for it" );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).Errors ) );
		rig.HoldAdmission();
		for ( var tick = 0; tick < 3000 && guest.State == GuestState.ExitingRide; tick++ )
			rig.Step();
		Assert.AreEqual( GuestState.DoingNothing, guest.State, "the walk ended and the guest decides again" );
		Assert.AreEqual( 5, guest.CellX );
	}
}
