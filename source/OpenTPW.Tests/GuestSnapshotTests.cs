using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// Save/restore tests of the guest lane (park-resume milestone): typed capture of the whole guest system,
/// pure validation, two-pass restore into fresh objects, deterministic continuation of an uninterrupted and a
/// restored park over hundreds of ticks, and rejection of corrupt snapshots without touching the target.
/// </summary>
[TestClass]
public class GuestSnapshotTests
{
	internal const float Tick = 1f / FixedStepClock.TicksPerSecond;

	/// <summary>A script that only yields; the tests drive the visitor protocol through its variables, which stands
	/// in for the script slice until its own snapshot restores them.</summary>
	internal static RideVM IdleScript( IRideScriptEffects effects ) => new( new RideScriptFile( new MemoryStream(
		new Asm().Label( "loop" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" ).Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Effects = effects, Seed = 1 } );

	/// <summary>
	/// A path row with two rides whose queues run up from it, and two arrival lanes that cross the row's park edge:
	/// guests arrive from the bus, pay, wander, queue, are called, board, ride and leave. Both rides use the idle
	/// script; the visitor protocol is driven deterministically by <see cref="Drive"/> so a restored park receives
	/// exactly the inputs of the uninterrupted one. The arrival timing, admission fee and guest cap are accelerated
	/// test-harness settings (root's R1 note): they shape only this fixture, not the production rules.
	/// </summary>
	internal sealed class Rig
	{
		public readonly GuestPathGrid Grid = new( 20, 10 );
		public readonly List<(RideVisitorBridge Bridge, RideVM Script)> Rides = new();
		public readonly GuestSimulation Simulation;

		/// <param name="rideScript">The script every ride runs; the idle script drives the visitor protocol
		/// through its variables. A limbo/bounce protocol rides the same harness (park-resume tests).</param>
		public Rig( Func<IRideScriptEffects, RideVM>? rideScript = null )
		{
			for ( var x = 0; x < 20; x++ )
				Grid.SetPath( x, 0, true );
			var settings = GuestTests.CreateSettings();
			// the lanes run along row 8, clear of the rides' queue columns (x = 2 and x = 17) that the cell map's edge test would not let a walker cross
			settings.ArrivalLaneA = new[] { (0, 8), (4, 8), (6, 8), (8, 8), (10, 0) };
			settings.ArrivalLaneB = new[] { (19, 8), (15, 8), (13, 8), (12, 8), (9, 0) };
			// Accelerated harness: a group about every dozen park turns (the default points rule gives four guests),
			// short visits so guests cycle out again, and a cap the park never reaches.
			settings.ArrivalTimeBetween = 2;
			settings.AdmissionFee = 5;
			settings.ExitLevelSeconds = 45;
			settings.ExitLevelVarSeconds = 15;
			settings.MaximumGuests = 150;
			Simulation = new GuestSimulation( Grid, settings, 7 );
			AddRide( rideScript ?? IdleScript, 1, "Snake ride", (2, 5), 5, (2, 0) );
			AddRide( rideScript ?? IdleScript, 2, "Wasp ride", (17, 5), 5, (17, 0) );
		}

		private void AddRide( Func<IRideScriptEffects, RideVM> scriptFactory, int attractionId, string name, (int X, int Y) front, int queueCells, (int X, int Y) entrance )
		{
			var bridge = new RideVisitorBridge( attractionId, name, RideVisitorKind.Ride, 5, 55, 30 )
			{
				EntranceCell = entrance,
				ExitCell = entrance,
				QueueFrontCell = front,
				QueueEntranceDirection = 2, // +Y, toward the ride beyond the front cell
				Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
			};
			var script = scriptFactory( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ) );
			bridge.Attach( script, () => true );
			Assert.AreEqual( queueCells, QueuePaths.TryExtend( Grid, bridge, Enumerable.Range( 0, queueCells ).Select( index => (front.X, front.Y - index) ) ) );
			QueuePaths.TryConnect( Grid, bridge );
			// the entrance cell as a placement lays it (type 9, the link byte and the queue link toward the front queue cell): the called guest's search walks into it
			Grid.Cells.SetType( front.X, front.Y + 1, ParkCellTypes.Entrance );
			Grid.Cells.SetLinks( front.X, front.Y + 1, GuestPathGrid.LinkValue( 0 ) );
			Grid.Cells.SetQueueLink( front.X, front.Y + 1, GuestPathGrid.LinkValue( 0 ) );
			Simulation.Register( bridge );
			Rides.Add( (bridge, script) );
			script.Advance( Tick );
		}

		public void Step()
		{
			Simulation.Tick( Tick );
			foreach ( var (bridge, script) in Rides )
			{
				bridge.HostStep();
				script.Advance( Tick );
			}
		}
	}

	/// <summary>
	/// The "script behaviour" every world receives each tick: take the presented guest every 90th tick, release one
	/// rider every 150th, so riders accumulate and the rides hold guests most of the time. It reads only state a
	/// restored park shares, so both worlds get identical inputs.
	/// </summary>
	internal static void Drive( Rig rig, long tick )
	{
		foreach ( var (bridge, script) in rig.Rides )
		{
			if ( tick % 90 == 0 && script[RideVariables.VAR_LETMEON] != 0 )
				script[RideVariables.VAR_LETMEON] = 0; // the script takes the guest; HostStep boards it
			if ( tick % 150 == 75 )
			{
				var rider = bridge.Riders.FirstOrDefault();
				if ( rider != 0 )
					script[RideVariables.VAR_LETMEOFF] = rider; // the script releases one rider
			}
		}
	}

	/// <summary>The canonical guest-system hash (guests, RNG-adjacent counters, queues and bridges).</summary>
	internal static ulong CanonicalHash( Rig rig )
	{
		var hash = new StateHasher();
		rig.Simulation.AddCanonicalState( hash );
		return hash.Value;
	}

	/// <summary>True when the park holds guests in every situation a snapshot must carry: standing and riding,
	/// arriving along a lane and heading home along one.</summary>
	internal static bool RichMoment( Rig park )
	{
		var states = park.Simulation.Guests.Select( guest => guest.State ).ToHashSet();
		return states.Contains( GuestState.Queueing )
			&& states.Contains( GuestState.Using )
			&& states.Contains( GuestState.WaitingToBoard )
			&& states.Any( state => state is GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth or GuestState.AtTicketBooth or GuestState.EnteringPark )
			&& states.Any( state => state is GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome );
	}

	/// <summary>Runs the park with its driver until <paramref name="until"/> holds or the tick budget is spent.</summary>
	internal static long RunUntil( Rig park, Func<Rig, bool> until, long tick, long maximumTick )
	{
		while ( tick < maximumTick && !until( park ) )
		{
			park.Step();
			Drive( park, ++tick );
		}
		return tick;
	}

	/// <summary>Two worlds continue identically for hundreds of ticks after one is restored from a snapshot.</summary>
	[TestMethod]
	public void RestoredParkContinuesIdenticallyForHundredsOfTicks()
	{
		var park = new Rig();
		var tick = RunUntil( park, rig => rig.Simulation.TickCount >= 4200, 0, 4201 );
		// A few guests have had enough of the park, so the snapshot also holds the walk home along a lane.
		foreach ( var guest in park.Simulation.Guests.Where( guest => guest.State is GuestState.WalkingAround or GuestState.GoingToRide or GuestState.ExitingRide ).Take( 3 ) )
			guest.ExitLevel = 1;
		tick = RunUntil( park, RichMoment, tick, 9000 );
		Assert.IsTrue( RichMoment( park ), $"the accelerated harness reached a rich snapshot moment (tick {tick})" );

		var snapshot = park.Simulation.CaptureSnapshot();
		var states = snapshot.Guests.Select( guest => guest.State ).ToList();
		CollectionAssert.AllItemsAreUnique( snapshot.Guests.Select( guest => guest.Id ).ToList(), "guest ids are unique" );
		Assert.IsTrue( states.Contains( GuestState.Queueing ), "guests are queueing at the snapshot moment" );
		Assert.IsTrue( states.Contains( GuestState.Using ), "guests are riding at the snapshot moment" );
		Assert.IsTrue( states.Contains( GuestState.WaitingToBoard ), "a guest is waiting to be taken" );
		Assert.IsTrue( states.Any( state => state is GuestState.GoingToRide or GuestState.MovingUpQueue or GuestState.Boarding ), "guests are moving towards or inside a queue" );
		Assert.IsTrue( states.Any( state => state is GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth or GuestState.AtTicketBooth or GuestState.EnteringPark ), "guests are arriving along a lane" );
		Assert.IsTrue( states.Any( state => state is GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome ), "guests are heading home" );

		// A fresh park — new grid, bridges, scripts and simulation — restored without replaying any history.
		var resumed = new Rig();
		resumed.Simulation.RestoreSnapshot( snapshot );
		// The script variable the script slice's snapshot restores: VAR_LETMEON still holds a presented guest.
		foreach ( var attraction in snapshot.Attractions )
		{
			var script = resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script;
			script[RideVariables.VAR_LETMEON] = attraction.LetMeOnWritten ? attraction.CalledGuest : 0;
		}
		Assert.AreEqual( CanonicalHash( park ), CanonicalHash( resumed ), "the restored park hashes the same as the saved one" );
		Assert.AreEqual( park.Simulation.RandomState, resumed.Simulation.RandomState, "the explicit RNG state carries over" );
		var lastTick = tick + 1000;
		for ( ; tick <= lastTick; tick++ )
		{
			park.Step();
			Drive( park, tick );
			resumed.Step();
			Drive( resumed, tick );
			Assert.AreEqual( CanonicalHash( park ), CanonicalHash( resumed ), $"the parks diverged at tick {tick}" );
		}
		Assert.AreEqual( park.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash() );
	}

	/// <summary>Restore rebuilds fresh guest objects and an equivalent snapshot: re-capturing the restored park gives the same DTO.</summary>
	[TestMethod]
	public void RestoreRebuildsFreshObjectsAndAnIdenticalSnapshot()
	{
		var park = new Rig();
		RunUntil( park, rig => rig.Simulation.TickCount >= 4200, 0, 4201 );
		var snapshot = park.Simulation.CaptureSnapshot();

		// A lived-in park with different state: the restore must replace it wholesale.
		var resumed = new Rig();
		for ( var tick = 1; tick <= 60; tick++ )
			resumed.Step();
		Assert.AreNotEqual( CanonicalHash( park ), CanonicalHash( resumed ), "the worlds differ before the restore" );
		resumed.Simulation.RestoreSnapshot( snapshot );

		Assert.AreEqual( snapshot.Guests.Length, resumed.Simulation.Guests.Count );
		for ( var index = 0; index < snapshot.Guests.Length; index++ )
		{
			var guest = snapshot.Guests[index];
			var restored = resumed.Simulation.Guests[index];
			Assert.AreEqual( guest.Id, restored.Id, $"guest {index} keeps its id and list position" );
			Assert.AreNotSame( park.Simulation.Guests[index], restored, "the restore builds a fresh object" );
			Assert.AreEqual( guest.State, restored.State );
			Assert.AreEqual( guest.Navigation.X, restored.Navigation.X );
			Assert.AreEqual( guest.Navigation.VelocityY, restored.Navigation.VelocityY );
			Assert.AreEqual( guest.Navigation.History, restored.Navigation.History );
			CollectionAssert.AreEqual( guest.Navigation.Route, restored.Navigation.Route, "the route deep-copies" );
			Assert.AreEqual( guest.Lane?.Length ?? -1, restored.Lane?.Length ?? -1 );
			Assert.AreEqual( guest.QueuePosition, restored.QueuePosition );
			Assert.AreEqual( guest.LastQueueUpdateTurn, restored.LastQueueUpdateTurn );
			Assert.IsNotNull( restored.OccupiedCell, "the occupant index is rebuilt" );
		}
		foreach ( var attraction in snapshot.Attractions )
		{
			var bridge = resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Bridge;
			CollectionAssert.AreEqual( attraction.Queue, bridge.Queue.ToArray(), "the queue keeps its order" );
			CollectionAssert.AreEqual( attraction.OnRide, bridge.Riders.ToArray(), "the riders keep their order" );
			Assert.AreEqual( attraction.CalledGuest, bridge.CalledGuest );
			Assert.AreEqual( attraction.QueueEditCount, bridge.QueueEditCount );
		}
		Assert.AreEqual( JsonSerializer.Serialize( snapshot ), JsonSerializer.Serialize( resumed.Simulation.CaptureSnapshot() ), "re-capturing the restored park gives the same snapshot" );
	}

	/// <summary>RideWalkCount and BoothWait are optional save fields: a snapshot without them (older saves read the defaults) restores, and set values round-trip.</summary>
	[TestMethod]
	public void RideWalkCountAndBoothWaitRoundTripAndDefaultForOlderSaves()
	{
		var park = new Rig();
		RunUntil( park, rig => rig.Simulation.TickCount >= 600, 0, 601 );
		var guest = park.Simulation.Guests.First();
		guest.RideWalkCount = 7;
		guest.BoothWait = 63;
		var snapshot = park.Simulation.CaptureSnapshot();
		var restored = new Rig();
		restored.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( 7, restored.Simulation.Guests.First( g => g.Id == guest.Id ).RideWalkCount );
		Assert.AreEqual( 63, restored.Simulation.Guests.First( g => g.Id == guest.Id ).BoothWait );
		// an older save has neither property: the guest record deserializes with the defaults and the snapshot validates
		var json = JsonSerializer.Serialize( snapshot.Guests.First( g => g.Id == guest.Id ) );
		var old = System.Text.RegularExpressions.Regex.Replace( json, "\"(RideWalkCount|BoothWait)\":\\d+,?", "" ).Replace( ",}", "}" );
		Assert.IsFalse( old.Contains( "RideWalkCount" ) || old.Contains( "BoothWait" ) );
		var olderGuest = JsonSerializer.Deserialize<GuestSnapshot>( old )!;
		Assert.AreEqual( 0, olderGuest.RideWalkCount );
		Assert.AreEqual( 0, olderGuest.BoothWait );
		var older = snapshot with { Guests = snapshot.Guests.Select( g => g.Id == guest.Id ? olderGuest : g ).ToArray() };
		Assert.IsTrue( park.Simulation.ValidateSnapshot( older ).IsValid );
		var target = new Rig();
		target.Simulation.RestoreSnapshot( older );
		Assert.AreEqual( 0, target.Simulation.Guests.First( g => g.Id == guest.Id ).RideWalkCount );
		Assert.AreEqual( 0, target.Simulation.Guests.First( g => g.Id == guest.Id ).BoothWait );
	}

	/// <summary>Snapshots with duplicate or missing references, bad bounds or inconsistent state are rejected, and a
	/// rejected restore leaves the lived-in target untouched.</summary>
	[TestMethod]
	public void CorruptSnapshotsAreRejectedAndLeaveTheTargetIntact()
	{
		var park = new Rig();
		RunUntil( park, rig => rig.Simulation.Guests.Any( guest => guest.State == GuestState.Queueing ), 0, 9000 );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.IsTrue( park.Simulation.ValidateSnapshot( snapshot ).IsValid, "the unmodified snapshot validates" );

		var target = new Rig();
		for ( var tick = 1; tick <= 40; tick++ )
			target.Step();
		var targetHash = CanonicalHash( target );
		var targetQueue = target.Rides.Select( ride => ride.Bridge.Queue.ToArray() ).ToList();

		GuestSimulationSnapshot WithGuest( int index, Func<GuestSnapshot, GuestSnapshot> change ) => snapshot with
		{
			Guests = snapshot.Guests.Select( ( guest, at ) => at == index ? change( guest ) : guest ).ToArray()
		};
		GuestSimulationSnapshot WithAttraction( int attractionId, Func<RideVisitorBridgeSnapshot, RideVisitorBridgeSnapshot> change ) => snapshot with
		{
			Attractions = snapshot.Attractions.Select( attraction => attraction.AttractionId == attractionId ? change( attraction ) : attraction ).ToArray()
		};
		var queuedIndex = Array.FindIndex( snapshot.Guests, guest => guest.State == GuestState.Queueing );
		var boundIndex = Array.FindIndex( snapshot.Guests, guest => guest.State is GuestState.GoingToRide or GuestState.WalkingAround );
		Assert.IsTrue( queuedIndex >= 0 && boundIndex >= 0, "the snapshot moment holds queueing and grid-walking guests" );

		var corrupt = new List<(string Name, GuestSimulationSnapshot Snapshot)>
		{
			( "duplicate guest id", snapshot with { Guests = snapshot.Guests.Append( snapshot.Guests[0] ).ToArray() } ),
			( "guest id beyond the thing pool", WithGuest( 0, guest => guest with { Id = ThingPool.Capacity + 1 } ) ),
			( "guest ids out of order in a save from before the thing pool", snapshot with { SchemaVersion = 7, Guests = new[] { snapshot.Guests[1], snapshot.Guests[0] }.Concat( snapshot.Guests.Skip( 2 ) ).ToArray() } ),
			( "already gone", WithGuest( 0, guest => guest with { State = GuestState.Gone } ) ),
			( "ride-walk count past its reset", WithGuest( 0, guest => guest with { RideWalkCount = 12 } ) ),
			( "negative booth wait", WithGuest( 0, guest => guest with { BoothWait = -1 } ) ),
			( "booth wait beyond the longest gate wait", WithGuest( 0, guest => guest with { BoothWait = GuestSimulation.GateWaitMaximum + 1 } ) ),
			( "route cell outside the map", WithGuest( 0, guest => guest with { Navigation = guest.Navigation with { Route = new[] { (99, 99) }.Concat( guest.Navigation.Route.Skip( 1 ) ).ToArray() } } ) ),
			( "queueing guest no longer held", WithGuest( queuedIndex, guest => guest with { State = GuestState.WalkingAround } ) ),
			( "attraction reference not registered", WithGuest( boundIndex, guest => guest with { State = GuestState.GoingToRide, AttractionId = 42 } ) ),
			( "queue references a missing guest", WithAttraction( snapshot.Attractions[0].AttractionId, attraction => attraction with { Queue = attraction.Queue.Append( 999 ).ToArray() } ) ),
			( "called guest not in the queue", WithAttraction( snapshot.Attractions[0].AttractionId, attraction => attraction with { CalledGuest = snapshot.Guests[boundIndex].Id } ) ),
			( "entrance cell outside the map", WithAttraction( snapshot.Attractions[0].AttractionId, attraction => attraction with { EntranceCell = (99, 99), ExitCell = (2, 0) } ) ),
			( "exit cell outside the map", WithAttraction( snapshot.Attractions[0].AttractionId, attraction => attraction with { EntranceCell = (2, 0), ExitCell = (2, 99) } ) ),
			( "pending arrivals below the lowest cut remainder", snapshot with { PendingArrivals = 500 - 10239 - 1 } ),
			( "attraction missing from the snapshot", snapshot with
			{
				Attractions = snapshot.Attractions.Take( 1 ).ToArray(),
				SeenQueueEdits = snapshot.SeenQueueEdits.Take( 1 ).ToArray()
			} )
		};
		var laneIndex = Array.FindIndex( snapshot.Guests, guest => guest.Lane != null );
		if ( laneIndex >= 0 )
			corrupt.Add( ( "lane index out of range", WithGuest( laneIndex, guest => guest with { LaneIndex = 9 } ) ) );
		var riding = snapshot.Guests.FirstOrDefault( guest => guest.State == GuestState.Using );
		if ( riding != null )
			corrupt.Add( ( "riding guest dropped from the riders", WithAttraction( snapshot.Attractions.First( a => a.OnRide.Contains( riding.Id ) ).AttractionId, attraction => attraction with { OnRide = attraction.OnRide.Where( id => id != riding.Id ).ToArray() } ) ) );

		foreach ( var (name, corrupted) in corrupt )
		{
			var validation = target.Simulation.ValidateSnapshot( corrupted );
			Assert.IsFalse( validation.IsValid, $"{name} must not validate" );
			Assert.ThrowsException<InvalidDataException>( () => target.Simulation.RestoreSnapshot( corrupted ), $"{name} must not restore" );
			Assert.AreEqual( targetHash, CanonicalHash( target ), $"{name} left the target unchanged" );
			CollectionAssert.AreEqual( targetQueue[0], target.Rides[0].Bridge.Queue.ToArray(), $"{name} left the target's queue unchanged" );
		}
		Assert.ThrowsException<InvalidDataException>( () => target.Simulation.RestoreSnapshot( null! ) );
	}

	/// <summary>An attraction that is not a RideVisitorBridge is an explicit save failure, not a silent omission.</summary>
	[TestMethod]
	public void UnsupportedAttractionsFailCaptureExplicitly()
	{
		var rig = new Rig();
		var cleanSnapshot = rig.Simulation.CaptureSnapshot();
		rig.Simulation.Register( new FakeBridge() );
		Assert.ThrowsException<NotSupportedException>( () => rig.Simulation.CaptureSnapshot() );
		var validation = rig.Simulation.ValidateSnapshot( cleanSnapshot );
		Assert.IsFalse( validation.IsValid, "the park no longer validates against its own snapshot" );
		Assert.IsTrue( validation.Errors.Any( problem => problem.Contains( "99" ) && problem.Contains( "queue snapshot" ) ),
			"the unsupported attraction is named: " + string.Join( "; ", validation.Errors ) );
	}

	/// <summary>The minimal member set of <see cref="IRideVisitorBridge"/>: a stand-in for a future non-script attraction.</summary>
	private sealed class FakeBridge : IRideVisitorBridge
	{
		public IRideVisitorHost? Host { get; set; }
		public int AttractionId => 99;
		public string Name => "Unsupported";
		public RideVisitorKind Kind => RideVisitorKind.Shop;
		public bool IsOpen => false;
		public int Capacity => 1;
		public int ExcitementLevel => 0;
		public int AttractionValue => 0;
		public GuestNeeds Satisfies => GuestNeeds.None;
		public int Price => 0;
		public (int X, int Y) EntranceCell => (0, 0);
		public (int X, int Y) ExitCell => (0, 0);
		public int QueueLength => 0;
		public int MaximumQueueLength => 0;
		public uint QueueLimit => 0;
		public bool HasQueue => false;
		public bool ProvidesRelief => false;
		public bool IsBroken => false;
		public bool IsBrokenDown => false;
		public bool ReliefWornOut => false;
		public bool IsRunning => false;
		public int RidersOnBoard => 0;
		public int Speed => 0;
		public IReadOnlyList<(int X, int Y)> QueueCells => Array.Empty<(int X, int Y)>();
		public int QueueSizeInCells => 1;
		public (int X, int Y)? JoinCell => null;
		public int QueueEditCount => 0;
		public int CalledGuest => 0;
		public bool TryJoinQueue( int guestId ) => false;
		public QueueJoinResult JoinQueue( int guestId, int? preferredExcitement = null ) => QueueJoinResult.Closed;
		public QueueJoinResult CheckJoinRoom( int guestId ) => QueueJoinResult.Closed;
		public void AppendToQueue( int guestId ) { }
		public int JoinDifference( int preferredExcitement ) => 0;
		public int GetQueuePosition( int guestId ) => -1;
		public bool LeaveQueue( int guestId ) => false;
		public bool RecomputeQueue( GuestPathGrid grid ) => false;
		public int DirectionTowardsFront( int index ) => 0;
		public bool PresentForBoarding( int guestId ) => false;
	}
}
