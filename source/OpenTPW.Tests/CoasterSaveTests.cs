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
		CoasterTests.PlaceStation( builder, Depart );
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
		CoasterTests.PlaceStation( builder, Depart );
		return ride;
	}

	[TestMethod]
	public void ARunningCoasterWithRidersRoundTripsAndContinuesDeterministically()
	{
		var (builder, ride, _) = NewCoaster();
		BuildRunningCoaster( builder, ride );
		Assert.IsTrue( ride.Trains.Count >= 1, "completion placed trains" );
		// The train seats the two waiting guests as its cars roll through the station window and runs into the circuit with them.
		for ( var tick = 0; tick < 30 * 60 && !(ride.Trains[0].Position > 3 && ride.Trains[0].Position < ride.Samples.Count / 2 && ride.RidersOnBoard == 2 && !ride.Trains[0].Flags.HasFlag( CoasterTrainFlags.InStation )); tick++ )
			ride.Advance( Tick );
		Assert.IsFalse( ride.Trains[0].Flags.HasFlag( CoasterTrainFlags.InStation ), "the first train is out on the circuit" );
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
	public void GuestsWaitingOnTheAdmissionRingAndBoardingSeatsRoundTrip()
	{
		var (builder, ride, _) = NewCoaster();
		BuildRunningCoaster( builder, ride );
		Assert.AreEqual( (2, 0), (ride.AdmissionCount, ride.RidersOnBoard), "COAST 1 put the guests on the admission ring" );
		var captured = CoasterSave.CaptureRide( ride, attractionId: 7 );
		CollectionAssert.AreEqual( new[] { 101, 102 }, captured.Admission );
		var resumed = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
		resumed.RestoreSnapshot( ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( captured ) ) );
		Assert.AreEqual( Hash( ride ), Hash( resumed ), "the ring is part of the canonical state" );
		// Save again while the first train is seating them: the boarding lists and flags cross the codec too.
		for ( var tick = 0; tick < 240 * 60 && !(ride.Trains.Any( train => train.BoardingCars.Any( car => car.Count > 0 ) )); tick++ )
		{
			ride.Advance( Tick );
			resumed.Advance( Tick );
		}
		Assert.IsTrue( ride.Trains.Any( train => train.BoardingCars.Any( car => car.Count > 0 ) ), "a train seated a guest" );
		var again = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( CoasterSave.CaptureRide( ride, attractionId: 7 ) ) );
		var second = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
		second.RestoreSnapshot( again );
		Assert.AreEqual( Hash( ride ), Hash( second ) );
		for ( var tick = 0; tick < 120 * 60; tick++ )
		{
			ride.Advance( Tick );
			second.Advance( Tick );
		}
		Assert.AreEqual( Hash( ride ), Hash( second ), "the continuation is deterministic" );
	}

	/// <summary>
	/// The motion clock (controller flag 0x10, <see cref="CoasterRide.MotionClockStarted"/>) crosses the resume save: a coaster saved before its first motion tick
	/// still takes that tick without time after the resume, one saved after it does not, and a save from before the flag counts it set on a track that runs.
	/// </summary>
	[TestMethod]
	public void TheMotionClockCrossesTheResumeSave()
	{
		var (builder, ride, _) = NewCoaster();
		BuildRunningCoaster( builder, ride );
		var fresh = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( CoasterSave.CaptureRide( ride, attractionId: 7 ) ) );
		Assert.AreEqual( false, fresh.MotionClockStarted );
		var resumed = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
		resumed.RestoreSnapshot( fresh );
		Assert.IsFalse( resumed.MotionClockStarted );
		ride.Advance( Tick );
		resumed.Advance( Tick );
		Assert.AreEqual( Hash( ride ), Hash( resumed ), "both took the first tick without time" );
		var started = CoasterSave.CaptureRide( ride, attractionId: 7 );
		Assert.AreEqual( true, started.MotionClockStarted );
		var older = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
		older.RestoreSnapshot( started with { MotionClockStarted = null } );
		Assert.IsTrue( older.MotionClockStarted, "a save from before the flag: the track runs, so the clock counts as started" );
		Assert.AreEqual( Hash( ride ), Hash( older ) );
	}

	/// <summary>
	/// The motion clock's lag (<see cref="CoasterRide.MotionClockLagSeconds"/>) crosses the resume save: a coaster saved while its track stands cut open catches the
	/// same stretch up after the resume as the one that kept running; a save from before the lag holds none.
	/// </summary>
	[TestMethod]
	public void TheMotionClockLagCrossesTheResumeSave()
	{
		var (builder, ride, _) = NewCoaster();
		BuildRunningCoaster( builder, ride );
		for ( var tick = 0; tick < 60; tick++ )
			ride.Advance( Tick );
		Assert.IsTrue( builder.Reopen() );
		for ( var tick = 0; tick < 90; tick++ )
			ride.Advance( Tick );
		var captured = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( CoasterSave.CaptureRide( ride, attractionId: 7 ) ) );
		Assert.AreEqual( ride.MotionClockLagSeconds, captured.MotionClockLagSeconds );
		var resumed = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Test Coaster" );
		var other = new CoasterBuilder( resumed, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( other, Depart );
		resumed.RestoreSnapshot( captured );
		Assert.AreEqual( Hash( ride ), Hash( resumed ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( other ) );
		ride.Advance( Tick );
		resumed.Advance( Tick );
		Assert.AreEqual( ride.LastMotionFrameSeconds, resumed.LastMotionFrameSeconds );
		Assert.IsTrue( ride.LastMotionFrameSeconds > 90 * Tick, "the catch-up crossed the save" );
		Assert.AreEqual( Hash( ride ), Hash( resumed ) );

		var older = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Test Coaster" );
		CoasterTests.PlaceStation( new CoasterBuilder( older, new CoasterSite( 64, 64 ) ), Depart );
		older.RestoreSnapshot( captured with { MotionClockLagSeconds = null } );
		Assert.AreEqual( 0d, older.MotionClockLagSeconds, "a save from before the lag holds none" );

		foreach ( var bad in new[] { double.NaN, double.PositiveInfinity, -1d } )
		{
			var corrupt = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Test Coaster" );
			CoasterTests.PlaceStation( new CoasterBuilder( corrupt, new CoasterSite( 64, 64 ) ), Depart );
			Assert.ThrowsException<InvalidDataException>( () => corrupt.RestoreSnapshot( captured with { MotionClockLagSeconds = bad } ), $"a lag of {bad} is refused" );
		}
	}

	/// <summary>
	/// A save from before the original's station rule (trains with a state and a boarding clock, no flags, no admission ring) still
	/// loads: a boarding train keeps its guests at the station and they ride when it leaves; an unloading train hands its riders to the
	/// leaving ring; a running or waiting train keeps them on the lap.
	/// </summary>
	[TestMethod]
	public void AnOldSaveWithDispatchStatesLoadsIntoTheStationRule()
	{
		var (builder, ride, _) = NewCoaster();
		BuildRunningCoaster( builder, ride );
		ride.Command( 6, 6 );
		var captured = CoasterSave.CaptureRide( ride, attractionId: 7 );
		// The JSON of an old save has none of the new members.
		var json = System.Text.Json.Nodes.JsonNode.Parse( ParkResumeJson.Serialize( captured ) )!.AsObject();
		json.Remove( "Admission" );
		foreach ( var train in json["Trains"]!.AsArray() )
		{
			var node = train!.AsObject();
			foreach ( var member in new[] { "Flags", "FirstBoardingCar", "LastBoardingCar", "BoardingCars" } )
				node.Remove( member );
		}
		var old = ParkResumeJson.Deserialize<CoasterRideSaveData>( System.Text.Encoding.UTF8.GetBytes( json.ToJsonString() ) );
		Assert.IsNull( old.Admission );
		Assert.IsTrue( old.Trains.All( train => train.Flags is null && train.BoardingCars is null ) );

		CoasterRide Load( int state, int[] riders )
		{
			var cars = Enumerable.Range( 0, old.Trains[0].Cars.Count ).Select( _ => new List<int>() ).ToList();
			cars[0].AddRange( riders );
			var trains = old.Trains.Select( ( train, index ) => index == 0 ? train with { State = state, Cars = cars } : train ).ToList();
			var resumed = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
			resumed.RestoreSnapshot( old with { Trains = trains, Leaving = new List<int>() } );
			return resumed;
		}

		var boarding = Load( CoasterRide.LegacyBoardingState, new[] { 501, 502 } );
		Assert.AreEqual( 2, boarding.Trains[0].BoardingCars[0].Count, "a boarding train's guests are seated at the station" );
		Assert.IsTrue( boarding.Trains[0].Flags.HasFlag( CoasterTrainFlags.InStation ) );
		for ( var tick = 0; tick < 120 * 60 && boarding.Trains[0].Flags.HasFlag( CoasterTrainFlags.InStation ); tick++ )
			boarding.Advance( Tick );
		Assert.AreEqual( (2, 0), (boarding.Trains[0].Cars[0].Count, boarding.LeavingCount), "they ride the lap when the train leaves the station" );

		var unloading = Load( CoasterRide.LegacyUnloadingState, new[] { 601, 602 } );
		Assert.AreEqual( (0, 2), (unloading.RidersOnBoard, unloading.LeavingCount), "an unloading train's riders wait for COAST 3" );
		Assert.AreEqual( 601, unloading.Command( 3, 0 ) );

		var running = Load( CoasterRide.LegacyRunningState, new[] { 701 } );
		Assert.AreEqual( (1, 0), (running.Trains[0].Cars[0].Count, running.LeavingCount) );
		Assert.AreEqual( 0, running.AdmissionCount );
	}

	/// <summary>
	/// A save of the older shape was rated with the earlier pylon heights, so its train and car counts can differ from the re-derived ones:
	/// it still loads, the new placement stands and every saved rider (lap and seated at the station) waits to leave. A save of the current
	/// shape with a wrong count is still refused.
	/// </summary>
	[TestMethod]
	public void AnOldSaveWhoseRatingPlacesAnotherTrainCountStillLoads()
	{
		var (builder, ride, _) = NewCoaster();
		BuildRunningCoaster( builder, ride );
		var captured = CoasterSave.CaptureRide( ride, attractionId: 7 );
		var json = System.Text.Json.Nodes.JsonNode.Parse( ParkResumeJson.Serialize( captured ) )!.AsObject();
		json.Remove( "Admission" );
		foreach ( var train in json["Trains"]!.AsArray() )
		{
			foreach ( var member in new[] { "Flags", "FirstBoardingCar", "LastBoardingCar", "BoardingCars" } )
				train!.AsObject().Remove( member );
		}
		var old = ParkResumeJson.Deserialize<CoasterRideSaveData>( System.Text.Encoding.UTF8.GetBytes( json.ToJsonString() ) );
		var guest = 800;
		CoasterTrainSaveData WithRiders( CoasterTrainSaveData train, int state ) => train with
		{
			State = state,
			Cars = train.Cars.Select( _ => new List<int> { ++guest } ).ToList()
		};
		// One more train than the re-derived rating places, one of them boarding, one running.
		var trains = old.Trains.Select( ( train, index ) => WithRiders( train, index == 0 ? CoasterRide.LegacyBoardingState : CoasterRide.LegacyRunningState ) ).ToList();
		trains.Add( WithRiders( old.Trains[0], CoasterRide.LegacyRunningState ) );
		var expected = trains.Sum( train => train.Cars.Sum( car => car.Count ) );
		var resumed = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
		resumed.RestoreSnapshot( old with { Trains = trains, Leaving = new List<int>() } );
		Assert.AreEqual( ride.Trains.Count, resumed.Trains.Count, "the re-derived placement stands" );
		Assert.AreEqual( 0, resumed.RidersOnBoard );
		Assert.AreEqual( expected, resumed.LeavingCount, "no saved rider is lost" );

		// Fewer cars in the saved train than the definition gives is the same case.
		var short_ = old.Trains.Select( train => train with { Cars = train.Cars.Skip( 1 ).ToList() } ).ToList();
		var resumedShort = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
		resumedShort.RestoreSnapshot( old with { Trains = short_, Leaving = new List<int>() } );
		Assert.AreEqual( ride.Trains.Count, resumedShort.Trains.Count );

		// The current shape is not forgiven.
		var current = captured with { Trains = captured.Trains.Concat( captured.Trains.Take( 1 ) ).ToList() };
		Assert.ThrowsException<InvalidDataException>( () => FreshRide( ride.Definition, new CoasterWorld( 1 ) ).RestoreSnapshot( current ) );
	}

	/// <summary>
	/// A speed change on a completed track (COAST 8 with the script's bias, 0x43b2b0 → 0x43c3d0) re-rates it at the new
	/// cap, so a park resume, which re-derives the rating from the saved cap, holds the same controller.
	/// </summary>
	[TestMethod]
	public void ASpeedChangeOnACompletedTrackResumesToTheSameState()
	{
		var (builder, ride, _) = NewCoaster();
		BuildRunningCoaster( builder, ride );
		for ( var tick = 0; tick < 10 * 60; tick++ )
			ride.Advance( Tick );
		ride.Command( 8, 0, 20 );
		Assert.AreEqual( CoasterRide.SpeedCapFor( ride.Definition, 20 ), ride.SpeedCap );
		var captured = CoasterSave.CaptureRide( ride, attractionId: 3 );
		var restored = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( captured ) );
		var resumed = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
		resumed.RestoreSnapshot( restored );
		Assert.AreEqual( ride.Rating, resumed.Rating, "the re-rated pass is what the resume derives" );
		Assert.AreEqual( Hash( ride ), Hash( resumed ) );
	}

	/// <summary>
	/// The lead car coordinate a reopened track keeps (train 0's last car when the trains were taken off) survives the save, joins the hash, and the
	/// resumed ride places its trains from it when the track is finished again, as the uninterrupted ride does.
	/// </summary>
	[TestMethod]
	public void TheLeadCarCoordinateOfAReopenedTrackRoundTripsAndPlacesTheTrainsAgain()
	{
		var (builder, ride, _) = NewCoaster();
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		ride.Command( 8, 0 );
		for ( var tick = 0; tick < 20 * 60; tick++ )
			ride.Advance( Tick );
		var last = ride.Definition.CarCount - 1;
		var lead = ride.CarCoordinate( ride.Trains[0], last );
		Assert.IsTrue( lead > 1f, $"train 0's last car left the platform start ({lead})" );
		Assert.IsTrue( builder.Reopen() );
		Assert.AreEqual( lead, ride.LeadCarCoordinate, 1e-4f, "the trains were taken off and the idle slot keeps the coordinate" );

		var captured = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( CoasterSave.CaptureRide( ride, attractionId: 7 ) ) );
		Assert.AreEqual( ride.LeadCarCoordinate, captured.LeadCarCoordinate, "the codec keeps the coordinate" );
		var resumed = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Test Coaster" );
		var other = new CoasterBuilder( resumed, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( other, Depart );
		resumed.RestoreSnapshot( captured );
		Assert.AreEqual( ride.LeadCarCoordinate, resumed.LeadCarCoordinate );
		Assert.AreEqual( Hash( ride ), Hash( resumed ), "the restored controller hashes as the saved one" );
		var moved = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Test Coaster" );
		CoasterTests.PlaceStation( new CoasterBuilder( moved, new CoasterSite( 64, 64 ) ), Depart );
		moved.RestoreSnapshot( captured with { LeadCarCoordinate = captured.LeadCarCoordinate + 1f } );
		Assert.AreNotEqual( Hash( ride ), Hash( moved ), "the coordinate is part of the canonical state" );

		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( other ) );
		Assert.AreEqual( lead, resumed.CarCoordinate( resumed.Trains[0], last ), 1e-3f, "the next placement starts train 0's last car on the kept coordinate (0x43c1f0)" );
		Assert.AreEqual( Hash( ride ), Hash( resumed ), "both rides place the same trains" );
	}

	[TestMethod]
	public void ALeadCarCoordinateSurvivesTheShapeCheckAndTheCodecAndANegativeOrNonFiniteOneIsRefused()
	{
		var snapshot = WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { CoasterEntry() with { LeadCarCoordinate = 17.25f } } } );
		ParkResumeSaveFile.ValidateShape( snapshot );
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		ParkResumeSaveFile.ValidateShape( restored );
		Assert.AreEqual( 17.25f, restored.Coasters!.Rides[0].LeadCarCoordinate );
		Assert.IsNull( ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { CoasterEntry() } } ) ) ).Coasters!.Rides[0].LeadCarCoordinate,
			"a save without it (an older one) stays without it" );
		foreach ( var bad in new[] { -0.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>(
				() => ParkResumeSaveFile.ValidateShape( WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { CoasterEntry() with { LeadCarCoordinate = bad } } } ) ) ).Message,
				"lead car coordinate", $"{bad}" );
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

	/// <summary>
	/// A save made with the two-node station (one station node at the departing end, the end node at the return end of a completed track) migrates on load
	/// (COAST-029): the station placement's three nodes stay, the saved pylons follow them, the end node returns onto S0, the whole circuit is sampled again, and the
	/// riders of the old placement wait to leave because the train placement of the new sampling is another one.
	/// </summary>
	[TestMethod]
	public void ASaveOfTheTwoNodeStationMigratesToTheThreeNodeStationAndItsRidersLeave()
	{
		var (builder, ride, _) = NewCoaster();
		BuildRunningCoaster( builder, ride );
		for ( var tick = 0; tick < 240 * 60 && ride.RidersOnBoard == 0; tick++ )
			ride.Advance( Tick );
		Assert.IsTrue( ride.RidersOnBoard > 0, "a train seated the boarding guests" );
		ride.Board( 103 );
		var captured = CoasterSave.CaptureRide( ride, 7 );
		var riders = captured.Trains.SelectMany( train => train.Cars.Concat( train.BoardingCars ?? new List<List<int>>() ) ).SelectMany( car => car ).Concat( captured.Leaving ).ToList();
		Assert.IsTrue( riders.Count > 0 && captured.Admission!.Count > 0, "riders on board and a guest waiting for a seat" );

		// The old shape: the station node on the depart end, the pylons as they were placed, the end node on the return end (it carried 0x144 as flags).
		var depart = ride.Track.DepartCell!.Value;
		var back = ride.Track.ReturnCell!.Value;
		var oldNodes = new List<CoasterNodeSaveData> { new( depart.X, depart.Y, 10f, 0f, false, 0, true, false, 0, 0f ) };
		oldNodes.AddRange( captured.Track.Nodes.Where( node => !node.IsStation && !node.IsStationEnd ) );
		oldNodes.Add( new CoasterNodeSaveData( back.X, back.Y, 10f, 0f, false, 0, false, true, 0x144, 0f ) );
		var old = captured with { Track = captured.Track with { Nodes = oldNodes }, State = null };

		var restored = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
		restored.RestoreSnapshot( old );
		var nodes = restored.Track.Nodes;
		Assert.AreEqual( 3, nodes.Count( node => node.IsStation ), "the station placement's three nodes" );
		Assert.AreEqual( ride.Track.Nodes.Count, nodes.Count );
		CollectionAssert.AreEqual( ride.Track.Nodes.Select( node => (node.X, node.Y, node.IsStation, node.IsStationEnd, node.Flags & ~CoasterNodeFlags.Winch) ).ToArray(),
			nodes.Select( node => (node.X, node.Y, node.IsStation, node.IsStationEnd, node.Flags & ~CoasterNodeFlags.Winch) ).ToArray(), "the same track as the freshly built one: the end node stands on S0" );
		Assert.IsTrue( restored.IsOperational && restored.Track.IsRunnable );
		CollectionAssert.AreEqual( ride.Samples.Select( sample => sample.Position ).ToArray(), restored.Samples.Select( sample => sample.Position ).ToArray(), "the circuit is sampled again from the new station" );
		Assert.AreEqual( CoasterControllerState.Open, restored.State, "an open ride stays open" );

		// The train placement is the new one; every rider of the old one waits to leave, the admission ring is kept.
		Assert.AreEqual( 0, restored.RidersOnBoard );
		Assert.AreEqual( 0f, restored.CarCoordinate( restored.Trains[0], restored.Trains[0].Cars.Count - 1 ), 1e-4f, "a new train's last car starts on the platform start (0x43c1f0)" );
		var waiting = new List<int>();
		while ( restored.TakeLeavingRider() is var guest and not 0 )
			waiting.Add( guest );
		CollectionAssert.AreEquivalent( riders, waiting, "nobody is lost" );
		CollectionAssert.AreEqual( captured.Admission, restored.AdmissionGuests.ToList() );

		// A save of the current shape still must match exactly.
		var current = FreshRide( ride.Definition, new CoasterWorld( 1 ) );
		current.RestoreSnapshot( captured );
		Assert.AreEqual( Hash( ride ), Hash( current ), "the current shape restores verbatim" );
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
		var train = new CoasterTrainSaveData( 12.5f, 30f, CoasterRide.LegacyRunningState, 5.0, new List<List<int>> { new(), new(), new(), new() } );
		return new CoasterRideSaveData( 1, track, true, false, 0, 60, 16, 105f, 1234, 45.5, 40.0, new List<CoasterTrainSaveData> { train }, new List<int>() );
	}

	/// <summary>A structurally valid coaster entry with the three-node station (COAST-029): S0, S1 on its exact position, S2, two pylons and the end node on S0.</summary>
	private static CoasterRideSaveData ThreeNodeCoasterEntry()
	{
		var nodes = new List<CoasterNodeSaveData>
		{
			new( 1, 6, 10f, 0f, false, 0, true, false, (uint)(CoasterNodeFlags.StationHead | CoasterNodeFlags.Winch | CoasterNodeFlags.NoPreviousLink), 0f ),
			new( 3, 6, 0f, 0f, false, 0, true, false, (uint)(CoasterNodeFlags.StationHead | CoasterNodeFlags.StationTail | CoasterNodeFlags.Winch), 0f, null, 36.5f, 12f, 65f ),
			new( 5, 6, 10f, 0f, false, 0, true, false, (uint)(CoasterNodeFlags.StationTail | CoasterNodeFlags.NoNextLink), 0f ),
			new( 8, 6, 40f, 0.3f, false, 0, false, false, (uint)CoasterNodeFlags.Winch, 0.1f ),
			new( 3, 9, 30f, 0.2f, false, 0, false, false, 0, 0.2f ),
			new( 1, 6, 10f, 0f, false, 0, false, true, (uint)CoasterTrack.StationEndFlags, 0f )
		};
		var track = new CoasterTrackSaveData( (4, 6), (2, 6), true, true, 8, nodes );
		var train = new CoasterTrainSaveData( 12.5f, 30f, CoasterRide.LegacyRunningState, 5.0, new List<List<int>> { new(), new(), new(), new() } );
		return new CoasterRideSaveData( 1, track, true, false, 0, 60, 16, 105f, 1234, 45.5, 40.0, new List<CoasterTrainSaveData> { train }, new List<int>(), State: 4 );
	}

	[TestMethod]
	public void AHandTiltedLoopRotationAndAHeightAboveTheModelsTopStillLoad()
	{
		var entry = ThreeNodeCoasterEntry();
		var nodes = entry.Track.Nodes.ToList();
		// the loop-tilt drag keeps the first pylon of a pair in [-0.35, 0.35]; the height drag may pass t = 1 up to what fMaxHeight leaves
		nodes[3] = nodes[3] with { LoopRotate = -0.35f, T = 1.05f };
		nodes[4] = nodes[4] with { LoopRotate = 0.85f };
		var snapshot = WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { entry with { Track = entry.Track with { Nodes = nodes } } } } );
		ParkResumeSaveFile.ValidateShape( ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) ) );
		nodes[3] = nodes[3] with { LoopRotate = -0.5f };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { entry with { Track = entry.Track with { Nodes = nodes } } } } ) ) ).Message, "loop rotate" );
	}

	[TestMethod]
	public void TheThreeNodeStationShapeAndItsControllerStatePassTheShapeCheckAndTheCodec()
	{
		var snapshot = WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { ThreeNodeCoasterEntry() } } );
		ParkResumeSaveFile.ValidateShape( snapshot );
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		ParkResumeSaveFile.ValidateShape( restored );
		var middle = restored.Coasters!.Rides[0].Track.Nodes[1];
		Assert.AreEqual( (36.5f, 12f, 65f), (middle.ExactX, middle.ExactY, middle.ExactZ), "the middle node's exact position survives the codec" );
		Assert.AreEqual( 4, restored.Coasters.Rides[0].State );
		void Rejects( CoasterRideSaveData entry, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>(
				() => ParkResumeSaveFile.ValidateShape( WithCoasters( new CoasterWorldSaveData { Rides = new List<CoasterRideSaveData> { entry } } ) ) ).Message, message );
		var entry = ThreeNodeCoasterEntry();
		Rejects( entry with { State = 3 }, "controller state 3" );
		Rejects( entry with { State = 2, Track = entry.Track with { IsComplete = false, IsClosed = false, Nodes = entry.Track.Nodes.Take( 5 ).ToList() } }, "needs a finished track" );
		var partial = entry.Track.Nodes.ToList();
		partial[1] = partial[1] with { ExactZ = null };
		Rejects( entry with { Track = entry.Track with { Nodes = partial } }, "partial exact position" );
		var misplaced = entry.Track.Nodes.ToList();
		misplaced[3] = misplaced[3] with { ExactX = 1f, ExactY = 2f, ExactZ = 3f };
		Rejects( entry with { Track = entry.Track with { Nodes = misplaced } }, "not a station node" );
		var sunk = entry.Track.Nodes.ToList();
		sunk[0] = sunk[0] with { IsStation = false };
		sunk[1] = sunk[1] with { ExactX = null, ExactY = null, ExactZ = null };
		Rejects( entry with { Track = entry.Track with { Nodes = sunk } }, "away from the head of the track" );
		var wrongEnd = entry.Track.Nodes.ToList();
		wrongEnd[^1] = wrongEnd[^1] with { X = 2 };
		Rejects( entry with { Track = entry.Track with { Nodes = wrongEnd } }, "head of its station" );
		var middleOnPylon = entry.Track.Nodes.ToList();
		middleOnPylon[1] = middleOnPylon[1] with { ExactX = null, ExactY = null, ExactZ = null };
		Rejects( entry with { Track = entry.Track with { Nodes = middleOnPylon } }, "middle node on a pylon" );
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
		Rejects( CoasterEntry() with { Track = Track( CoasterEntry() ) with { Nodes = NewNodes( Node( 1 ) with { T = 9f } ) } }, "outside 0..8" );
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
