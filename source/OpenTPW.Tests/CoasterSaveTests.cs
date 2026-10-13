using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The coaster part of the own-format park save without game data (docs/PARK-SAVES.md): a running coaster with
/// riders on board round-trips through the JSON codec into a fresh controller and continues deterministically (the
/// canonical state hash is equal at the boundary and after further simulation), an unfinished track round-trips
/// without trains, a save with no coaster part stays a save with no coaster part (pre-coaster saves load as they
/// were), and the envelope's shape validation refuses corrupted track data with a reason — never a half-applied park.
/// </summary>
[TestClass]
public class CoasterSaveTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private static readonly (int X, int Y) Depart = (20, 20), Return = (17, 20);

	[TestInitialize]
	public void Init() => Log = new();

	/// <summary>A coaster on a flat site, headless like <see cref="CoasterTests"/>' fixtures.</summary>
	private static (CoasterBuilder Builder, CoasterRide Ride, CoasterWorld World) NewCoaster( int size = 64 )
	{
		var world = new CoasterWorld( 1 );
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), world, "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( size, size ) );
		builder.SetStation( Depart, Return );
		return (builder, ride, world);
	}

	/// <summary>Builds the shared circuit, closes and completes it, opens the ride and boards two riders.</summary>
	private static void BuildRunningCoaster( CoasterBuilder builder, CoasterRide ride )
	{
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		ride.Command( 6, 6 );
		ride.Board( 101 );
		ride.Board( 102 );
	}

	private static ulong Hash( CoasterRide ride )
	{
		var hash = new StateHasher();
		ride.AddCanonicalState( hash );
		return hash.Value;
	}

	private static CoasterRide FreshRide( CoasterDefinition definition, CoasterWorld world )
	{
		// The restore path of a candidate: the station was placed (which built a fresh controller and reset the
		// track to its station node), and the saved state now goes onto it.
		var ride = new CoasterRide( definition, world, "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		builder.SetStation( Depart, Return );
		return ride;
	}

	[TestMethod]
	public void ARunningCoasterWithRidersRoundTripsAndContinuesDeterministically()
	{
		var (builder, ride, _) = NewCoaster();
		BuildRunningCoaster( builder, ride );
		Assert.IsTrue( ride.Trains.Count >= 1, "completion placed trains" );
		// Let the boarding train dispatch (full or after five seconds) and run into the circuit with its riders.
		for ( var tick = 0; tick < 30 * 60 && !(ride.Trains[0].State is CoasterTrainState.Running or CoasterTrainState.Holding && ride.Trains[0].Position > 3); tick++ )
			ride.Advance( Tick );
		Assert.IsTrue( ride.Trains[0].State is CoasterTrainState.Running or CoasterTrainState.Holding, "the first train is out on the circuit" );
		Assert.AreEqual( 2, ride.RidersOnBoard, "both riders are aboard the moving train" );

		var captured = CoasterSave.CaptureRide( ride, attractionId: 7 );
		var restored = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( captured ) );
		var boundary = Hash( ride );

		var world = new CoasterWorld( 1 );
		var resumed = FreshRide( ride.Definition, world );
		resumed.RestoreSnapshot( restored );
		Assert.AreEqual( boundary, Hash( resumed ), "the reconstructed controller holds the persisted state" );
		Assert.AreEqual( 2, resumed.RidersOnBoard, "the riders are still on board" );
		Assert.AreEqual( ride.Trains[0].Position, resumed.Trains[0].Position, "the train stands where it was saved" );
		Assert.AreEqual( ride.Rating, resumed.Rating, "the rebuilt rating is the saved pass's rating" );

		// Deterministic continuation: the same ticks move both parks alike, riders included.
		for ( var tick = 0; tick < 45 * 60; tick++ )
		{
			ride.Advance( Tick );
			resumed.Advance( Tick );
		}
		Assert.AreEqual( Hash( ride ), Hash( resumed ), "the continuation is deterministic" );
		Assert.AreEqual( ride.RidersOnBoard, resumed.RidersOnBoard );
	}

	[TestMethod]
	public void AnUnfinishedTrackRoundTripsWithoutTrains()
	{
		var (builder, ride, _) = NewCoaster();
		builder.Height = 25;
		foreach ( var (dx, dy) in CoasterTests.Circuit.Take( 3 ) )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		Assert.IsFalse( ride.Track.IsClosed );
		var captured = CoasterSave.CaptureRide( ride, attractionId: 1 );
		var restored = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( captured ) );

		var resumed = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
		resumed.RestoreSnapshot( restored );
		Assert.AreEqual( Hash( ride ), Hash( resumed ) );
		Assert.AreEqual( 0, resumed.Trains.Count, "an open chain holds no trains" );
		Assert.AreEqual( 3, resumed.Track.PylonCount );
		// The build continues seamlessly after the load: the next pylon validates against the restored chain.
		Assert.AreEqual( CoasterPlaceResult.Ok, new CoasterBuilder( resumed, new CoasterSite( 64, 64 ) ).Place( Depart.X + CoasterTests.Circuit[3].Item1, Depart.Y + CoasterTests.Circuit[3].Item2 ) );
	}

	/// <summary>
	/// The restore's own defensive refusals (the envelope validated the part's shape and catalog fit already; these
	/// guard a doctored part whose counts disagree with what the deterministic rebuild re-derives): a train count off
	/// the rating's and a car count off the definition's are refused with a reason.
	/// </summary>
	[TestMethod]
	public void ARestoreWithMismatchedTrainOrCarCountsIsRefused()
	{
		var (builder, ride, _) = NewCoaster();
		BuildRunningCoaster( builder, ride );
		var captured = CoasterSave.CaptureRide( ride, attractionId: 1 );

		var wrongTrains = Assert.ThrowsException<InvalidDataException>( () =>
			FreshRide( ride.Definition, new CoasterWorld( 1 ) ).RestoreSnapshot( captured with { Trains = new List<CoasterTrainSaveData>() } ) );
		StringAssert.Contains( wrongTrains.Message, "trains in the save" );

		var fewerCars = captured.Trains.Select( train => train with { Cars = train.Cars.Take( train.Cars.Count - 1 ).ToList() } ).ToList();
		var wrongCars = Assert.ThrowsException<InvalidDataException>( () =>
			FreshRide( ride.Definition, new CoasterWorld( 1 ) ).RestoreSnapshot( captured with { Trains = fewerCars } ) );
		StringAssert.Contains( wrongCars.Message, "cars in the save" );
	}

	// ---- Envelope shape validation -----------------------------------------------------------------------

	/// <summary>A structurally valid coaster entry for the envelope fixture's placed attraction 1.</summary>
	private static CoasterRideSaveData CoasterEntry()
	{
		var nodes = new List<CoasterNodeSaveData>
		{
			new( 2, 6, 10f, 0f, false, 0, true, false, 0, 0f ),
			new( 5, 6, 40f, 0.3f, false, 0, false, false, (uint)CoasterNodeFlags.Winch, 0.1f ),
			new( 3, 8, 30f, 0.2f, false, 0, false, false, 0, 0.2f ),
			new( 0, 6, 10f, 0f, false, 0, false, true, (uint)CoasterTrack.StationEndFlags, 0f )
		};
		var track = new CoasterTrackSaveData( (2, 6), (0, 6), true, true, 8, nodes );
		var train = new CoasterTrainSaveData( 12.5f, 30f, (int)CoasterTrainState.Running, 5.0, new List<List<int>> { new(), new(), new(), new() } );
		return new CoasterRideSaveData( 1, track, true, false, 0, 60, 16, 105f, 1234, 45.5, 40.0, new List<CoasterTrainSaveData> { train }, new List<int>() );
	}

	private static ParkResumeSnapshot WithCoasters( CoasterWorldSaveData coasters ) =>
		ParkResumeSaveFileTests.Envelope() with { Coasters = coasters };

	[TestMethod]
	public void AWellFormedCoasterPartPassesTheShapeCheckAndTheCodec()
	{
		var snapshot = WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { CoasterEntry() } } );
		ParkResumeSaveFile.ValidateShape( snapshot );
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		ParkResumeSaveFile.ValidateShape( restored );
		Assert.AreEqual( (2, 6), restored.Coasters!.Rides[0].Track.DepartCell, "the station end cells survive the codec" );
		Assert.AreEqual( 40.0, restored.Coasters.Rides[0].LastDispatch, "the dispatch clock survives the codec" );
	}

	[TestMethod]
	public void ASaveWithoutCoastersStaysWithoutCoasters()
	{
		// The pre-coaster format: no coaster member at all. It loads exactly as it was, with no coaster part.
		var snapshot = ParkResumeSaveFileTests.Envelope();
		Assert.IsNull( snapshot.Coasters );
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		Assert.IsNull( restored.Coasters );
		ParkResumeSaveFile.ValidateShape( restored );
	}

	[TestMethod]
	public void NeverDispatchedSavesAsANullDispatchClock()
	{
		var entry = CoasterEntry() with { LastDispatch = null };
		var restored = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( entry ) );
		Assert.IsNull( restored.LastDispatch, "the live -∞ initial clock is saved as null and comes back null" );
		ParkResumeSaveFile.ValidateShape( WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { entry } } ) );
	}

	[TestMethod]
	public void CorruptedTrackDataIsRefusedWithAReason()
	{
		ParkResumeSaveFile.ValidateShape( WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { CoasterEntry() } } ) );
		void Rejects( CoasterRideSaveData entry, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>(
				() => ParkResumeSaveFile.ValidateShape( WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { entry } } ) ) ).Message, message );
		CoasterNodeSaveData Node( int index ) => CoasterEntry().Track.Nodes[index];
		CoasterTrackSaveData Track( CoasterRideSaveData ride ) => ride.Track;
		// Cells and flags.
		Rejects( CoasterEntry() with { Track = Track( CoasterEntry() ) with { Nodes = NewNodes( Node( 1 ) with { X = -1 } ) } }, "outside the cell map" );
		Rejects( CoasterEntry() with { Track = Track( CoasterEntry() ) with { Nodes = NewNodes( Node( 1 ) with { Flags = (uint)CoasterNodeFlags.Winch | 0x8 } ) } }, "does not know" );
		Rejects( CoasterEntry() with { Track = Track( CoasterEntry() ) with { Nodes = NewNodes( Node( 1 ) with { T = 1.5f } ) } }, "outside 0..1" );
		// Circuit rules.
		Rejects( CoasterEntry() with { Track = Track( CoasterEntry() ) with { IsClosed = false } }, "completed without closing" );
		Rejects( CoasterEntry() with { Track = Track( CoasterEntry() ) with { Nodes = new List<CoasterNodeSaveData> { Node( 0 ), Node( 1 ), Node( 3 ) } } }, "closes over 1 pylons" );
		Rejects( CoasterEntry() with { Track = Track( CoasterEntry() ) with { Nodes = new List<CoasterNodeSaveData> { Node( 0 ), Node( 1 ), Node( 2 ) } } }, "no station end node" );
		Rejects( CoasterEntry() with { Track = Track( CoasterEntry() ) with { IsComplete = false, Nodes = NewNodes( Node( 1 ) ) } }, "open track holds a station end node" );
		Rejects( CoasterEntry() with { Track = Track( CoasterEntry() ) with { DepartCell = null, ReturnCell = null } }, "nodes without station ends" );
		Rejects( CoasterEntry() with { Track = TrackWithSecondStationNode() }, "second station node" );
		// Identity: an entry the park does not place, and entries out of creation order.
		Rejects( CoasterEntry() with { AttractionId = 99 }, "does not place" );
		var duplicated = new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { CoasterEntry(), CoasterEntry() } };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( WithCoasters( duplicated ) ) ).Message, "does not follow" );
		// Controller scalars.
		Rejects( CoasterEntry() with { BreakdownState = 3 }, "not 0, 1 or 2" );
		Rejects( CoasterEntry() with { PaidCost = -1 }, "paid" );
		Rejects( CoasterEntry() with { LastDispatch = double.NaN }, "not finite" );
		// Trains: states, positions and riders.
		Rejects( CoasterEntry() with { Trains = new List<CoasterTrainSaveData> { CoasterEntry().Trains[0] with { State = 99 } } }, "not a train state" );
		Rejects( CoasterEntry() with { Trains = new List<CoasterTrainSaveData> { CoasterEntry().Trains[0] with { Position = -1 } } }, "negative or not finite" );
		// The part's own bounds, before any catalog speaks: trains and cars above the format's caps.
		Rejects( CoasterEntry() with { Trains = Enumerable.Range( 0, CoasterSave.MaximumTrains + 1 ).Select( _ => CoasterEntry().Trains[0] ).ToList() }, $"trains, above {CoasterSave.MaximumTrains}" );
		Rejects( CoasterEntry() with { Trains = new List<CoasterTrainSaveData> { CoasterEntry().Trains[0] with { Cars = Enumerable.Range( 0, CoasterSave.MaximumCarsPerTrain + 1 ).Select( _ => new List<int>() ).ToList() } } }, $"cars, above {CoasterSave.MaximumCarsPerTrain}" );
		Rejects( CoasterEntry() with { Track = Track( CoasterEntry() ) with { IsComplete = false, IsClosed = false, Nodes = new List<CoasterNodeSaveData> { Node( 0 ), Node( 1 ), Node( 2 ) } } }, "never completed" );
		var rider = CoasterEntry() with { Trains = new List<CoasterTrainSaveData> { CoasterEntry().Trains[0] with { Cars = new List<List<int>> { new() { 999999 }, new(), new(), new() } } } };
		Rejects( rider, "the guest snapshot does not hold" );
		Rejects( CoasterEntry() with { Leaving = new List<int> { 999999 } }, "the guest snapshot does not hold" );
	}

	/// <summary>
	/// The boarding hand-over: a rider the script has already handed to a car with <c>COAST 1</c> while the bridge has
	/// not marked it aboard yet (it does one host step later, when the script consumes VAR_LETMEON). The save holds
	/// that guest still waiting at the stand point — WaitingToBoard at this coaster, called, VAR_LETMEON written — and
	/// the shape check accepts it; anything looser (a merely queued guest, a called guest whose VAR_LETMEON the save
	/// does not show written) is refused.
	/// </summary>
	[TestMethod]
	public void ABoardingHandOverRiderIsValidAndAnythingLooserIsRefused()
	{
		// A longer fixture run settles guests inside the park, where the mutation below is coherent.
		var snapshot = ParkResumeSaveFileTests.Envelope( 5400 );
		var guests = snapshot.Guests!;
		var bridgeIndex = Array.IndexOf( guests.Attractions!, guests.Attractions!.Single( attraction => attraction.AttractionId == 1 ) );
		var bridge = guests.Attractions![bridgeIndex]!;
		// The hand-over guest: queued at attraction 1 (already, or placed there), WaitingToBoard, called.
		var guestIndex = Array.IndexOf( guests.Guests!, guests.Guests!.FirstOrDefault( guest => guest!.State == GuestState.Queueing && guest.AttractionId == 1 )
			?? guests.Guests!.First( guest => guest!.State is GuestState.WalkingAround or GuestState.DoingNothing ) );
		var rider = guests.Guests[guestIndex]!.Id;
		ParkResumeSnapshot WithGuest( GuestState state, bool letMeOnWritten )
		{
			guests.Guests![guestIndex] = guests.Guests[guestIndex]! with { State = state, AttractionId = 1 };
			var withQueue = bridge.Queue.Contains( rider ) ? bridge : bridge with { Queue = bridge.Queue.Append( rider ).ToArray() };
			guests.Attractions![bridgeIndex] = withQueue with { CalledGuest = rider, LetMeOnWritten = letMeOnWritten };
			return snapshot with { Coasters = CoasterPart( inLeavingQueue: false ) };
		}
		CoasterWorldSaveData CoasterPart( bool inLeavingQueue ) => new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { CoasterEntry() with
		{
			Trains = new List<CoasterTrainSaveData> { CoasterEntry().Trains[0] with { Cars = new List<List<int>> { inLeavingQueue ? new() : new() { rider }, new(), new(), new() } } },
			Leaving = inLeavingQueue ? new List<int> { rider } : new List<int>()
		} } };

		ParkResumeSaveFile.ValidateShape( WithGuest( GuestState.WaitingToBoard, letMeOnWritten: true ) );
		// The leaving queue follows the same rule (a guest COAST 1 could not seat waits to leave).
		ParkResumeSaveFile.ValidateShape( WithGuest( GuestState.WaitingToBoard, letMeOnWritten: true ) with { Coasters = CoasterPart( inLeavingQueue: true ) } );
		void Rejects( ParkResumeSnapshot corrupted, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( corrupted ) ).Message, message );
		// A merely queued guest (not the called hand-over) is not a rider.
		Rejects( WithGuest( GuestState.Queueing, letMeOnWritten: true ), $"walks the park in state {GuestState.Queueing}" );
		// A called guest whose VAR_LETMEON the save does not show written is not in the hand-over.
		Rejects( WithGuest( GuestState.WaitingToBoard, letMeOnWritten: false ), $"walks the park in state {GuestState.WaitingToBoard}" );
	}

	private static List<CoasterNodeSaveData> NewNodes( CoasterNodeSaveData changed )
	{
		var nodes = CoasterEntry().Track.Nodes;
		nodes[1] = changed;
		return nodes;
	}

	private static CoasterTrackSaveData TrackWithSecondStationNode()
	{
		var nodes = CoasterEntry().Track.Nodes;
		nodes[2] = nodes[2] with { IsStation = true };
		return CoasterEntry().Track with { Nodes = nodes };
	}
}
