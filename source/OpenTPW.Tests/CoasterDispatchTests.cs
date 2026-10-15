using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The original's dispatch rule (tp.exe <c>0x435a30</c>, <c>0x43ce20</c>, <c>0x43b8b0</c>, <c>0x43b600</c>, <c>0x43cd60</c>) without
/// game data: trains never stop; an arriving train unloads in any state, guests board only while the ride is open as the
/// cars roll through the station window, the winch floor halves for a train with the headway flag, and a train that catches
/// up with the train ahead exchanges speed with it.
/// </summary>
[TestClass]
public class CoasterDispatchTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private static readonly (int X, int Y) Depart = (20, 20), Return = (17, 20);

	[TestInitialize]
	public void Init() => Log = new();

	private static CoasterRide NewRide( Action<Dictionary<string, string>>? configure = null )
	{
		var settings = CoasterTests.PcSettings();
		configure?.Invoke( settings );
		var ride = new CoasterRide( CoasterDefinition.FromValues( settings ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, Depart );
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		ride.Command( 8, 0 );
		// A finished ride is built (state 2: no station step); the station script opens it and, as the tests need, closes it again.
		ride.Command( 5, 0 );
		ride.Command( 5, 1 );
		return ride;
	}

	private static void Run( CoasterRide ride, double seconds )
	{
		for ( var tick = 0; tick < seconds * 60; tick++ )
			ride.Advance( Tick );
	}

	/// <summary>
	/// A speed change re-rates the track and places the trains again (0x43c450): the trains below the new count keep their riders, the trains past it
	/// give theirs up to the leaving ring (0x43ba20), and nobody vanishes.
	/// </summary>
	[TestMethod]
	public void ARerateKeepsTheRidersOfTheTrainsThatStayAndTheTrainsPastTheNewCountGiveTheirsUp()
	{
		var fewer = false;
		var more = false;
		foreach ( var release in new[] { 1500, 2500, 4000, 6000, 9000, 14000, 20000 } )
		{
			foreach ( var (from, bias) in new[] { 0, 10, 20, 35, 65, 80, 100 }.SelectMany( other => new[] { (50, other), (other, 50) } ) )
			{
				var ride = NewRide( settings => { settings["sCoasterType.uiMaxTrains"] = "8"; settings["sCoasterType.fMaxSpeedAtMinSetting"] = "3"; settings["sCoasterType.fMaxSpeedAtMaxSetting"] = "40"; settings["sCoasterType.uiTrainReleaseRate"] = release.ToString(); } );
				ride.ApplyScriptSpeed( from );
				var guest = 1000;
				foreach ( var train in ride.Trains )
				{
					train.CarLists[0].Add( ++guest );
					train.BoardingLists[^1].Add( ++guest );
				}
				var seated = guest - 1000;
				var before = ride.Trains.Count;
				ride.ApplyScriptSpeed( bias );
				if ( ride.Trains.Count == before )
					continue;
				var kept = Math.Min( before, ride.Trains.Count );
				fewer |= ride.Trains.Count < before;
				more |= ride.Trains.Count > before;
				Assert.AreEqual( 2 * kept, ride.RidersOnBoard, $"release {release}, bias {from} → {bias}: the {kept} trains that stay keep their riders" );
				Assert.AreEqual( seated - 2 * kept, ride.LeavingCount, $"release {release}, bias {from} → {bias}: the trains past the new count give up theirs" );
				for ( var index = 0; index < kept; index++ )
					Assert.AreEqual( (1, 1), (ride.Trains[index].Cars[0].Count, ride.Trains[index].BoardingCars[^1].Count), "the riders stay in their cars" );
			}
		}
		Assert.IsTrue( fewer && more, $"some script speed changes the train count of this circuit both ways ({fewer}, {more})" );
	}

	[TestMethod]
	public void TheStationWindowSpansThePlatformFromTheFirstStationNodeToTheThirdOne()
	{
		var ride = NewRide();
		var count = ride.Samples.Count;
		var coordinates = ride.KnotCoordinates;
		Assert.AreEqual( 0f, coordinates[0], "the sample list starts on the first station node (S0)" );
		Assert.IsTrue( coordinates[1] > 0 && coordinates[2] > coordinates[1] && coordinates[2] < count / 2f, $"S1 at {coordinates[1]}, S2 at {coordinates[2]} of {count} samples: the platform is the head of the list" );
		Assert.AreEqual( (0f, coordinates[2]), ride.StationWindow, "fEntranceSizeInterval 0: the whole span between the first and the third station node (0x43b750)" );
		Assert.AreEqual( count - 1f, ride.BrakingStart + ride.Samples[CoasterRide.BrakingSpeedSample( ride.Samples.ToArray(), ride.Track.Nodes.Count - 1 )].Speed / ride.Spacing, 1e-3f, "ctl+0x134 = (n − 1) − v/spacing" );
		Assert.IsTrue( ride.BrakingStart > ride.StationWindow.End, "the brake zone is the stretch before the end of the list, which is the start of the platform" );

		var narrowed = NewRide( settings => settings["sCoasterType.fEntranceSizeInterval"] = "0.25" );
		var end = narrowed.KnotCoordinates[2];
		Assert.AreEqual( (0.25f * end / 2, 0.75f * end + 0.25f * end / 2), narrowed.StationWindow, "f8 = (1 − k)·A + k·mid, fc = (1 − k)·B + k·mid" );
	}

	[TestMethod]
	public void ATrainInTheStationRunsAtTheWinchSpeedAndNeverStops()
	{
		var ride = NewRide();
		ride.Command( 5, 0 );
		var winch = ride.Definition.WinchSpeed;
		Run( ride, 20 );
		var inStation = 0;
		for ( var tick = 0; tick < 240 * 60; tick++ )
		{
			ride.Advance( Tick );
			foreach ( var train in ride.Trains )
			{
				Assert.IsTrue( train.Speed > 0, "no zero-speed path exists in the motion tick (0x43ce20)" );
				if ( !train.Flags.HasFlag( CoasterTrainFlags.InStation ) )
					continue;
				inStation++;
				Assert.IsTrue( train.Speed <= winch * 1.5f, $"train {train.Index} in the station at {train.Speed}: the braking blend ends at the winch floor {winch}" );
			}
		}
		Assert.IsTrue( inStation > 60, "the trains spent time in the station window" );
	}

	[TestMethod]
	public void GuestsBoardOnlyWhileTheRideIsOpen()
	{
		var ride = NewRide();
		ride.Command( 6, 6 );
		ride.Board( 201 );
		ride.Board( 202 );
		Run( ride, 240 );
		Assert.AreEqual( (2, 0), (ride.AdmissionCount, ride.RidersOnBoard), "closed: trains circulate, nobody is seated (0x43b8b0 seats in state 4 only)" );
		ride.Command( 5, 0 );
		var seated = false;
		for ( var tick = 0; tick < 240 * 60 && !seated; tick++ )
		{
			ride.Advance( Tick );
			seated = ride.AdmissionCount == 0;
		}
		Assert.IsTrue( seated, "open: the next train that rolls through the window seats both guests" );
		Assert.AreEqual( 2, ride.RidersOnBoard );
	}

	[TestMethod]
	public void AnArrivingTrainUnloadsWhetherTheRideIsOpenOrClosed()
	{
		var ride = NewRide();
		ride.Command( 6, 6 );
		ride.Command( 5, 0 );
		ride.Board( 301 );
		ride.Board( 302 );
		for ( var tick = 0; tick < 240 * 60 && ride.RidersOnBoard < 2; tick++ )
			ride.Advance( Tick );
		Assert.AreEqual( 2, ride.RidersOnBoard );
		ride.Board( 303 );
		Assert.AreEqual( 1, ride.AdmissionCount );
		ride.Command( 5, 1 );
		Assert.AreEqual( (0, 1), (ride.AdmissionCount, ride.LeavingCount), "closing moves the waiting guest to the leaving ring (0x43b390)" );
		var unloaded = false;
		for ( var tick = 0; tick < 240 * 60 && !unloaded; tick++ )
		{
			ride.Advance( Tick );
			unloaded = ride.RidersOnBoard == 0;
		}
		Assert.IsTrue( unloaded, "the riders get off at the next arrival although the ride is closed" );
		Assert.AreEqual( 3, ride.LeavingCount, "both riders and the guest the closing moved off the admission ring" );
		Assert.AreEqual( 0, ride.AdmissionCount );
		CollectionAssert.AreEquivalent( new[] { 301, 302, 303 }, new[] { ride.Command( 3, 0 ), ride.Command( 3, 0 ), ride.Command( 3, 0 ) } );
	}

	[TestMethod]
	public void SeatsSplitTheCapacityByTheOriginalsRecurrenceAndBoardingRespectsThem()
	{
		var ride = NewRide();
		ride.Command( 6, 6 );
		CollectionAssert.AreEqual( new[] { 1, 1, 2, 2 }, ride.Trains[0].Seats, "6 seats over 4 cars: 6/4, 5/3, 4/2, 2/1 (0x43bb7b)" );
		ride.Command( 6, 7 );
		CollectionAssert.AreEqual( new[] { 1, 2, 2, 2 }, ride.Trains[0].Seats );
		ride.Command( 6, 6 );
		ride.Command( 5, 0 );
		for ( var guest = 401; guest < 407; guest++ )
			ride.Board( guest );
		Assert.AreEqual( 0, ride.Command( 2, 0 ), "six guests wait: no room left" );
		ride.Board( 407 );
		Assert.AreEqual( 1, ride.LeavingCount, "a guest handed over without room goes back through COAST 3" );
		var most = 0;
		for ( var tick = 0; tick < 240 * 60 && ride.AdmissionCount > 0; tick++ )
		{
			ride.Advance( Tick );
			foreach ( var train in ride.Trains )
			{
				for ( var car = 0; car < train.Seats.Length; car++ )
					Assert.IsTrue( train.BoardingCars[car].Count <= train.Seats[car], $"car {car} holds {train.BoardingCars[car].Count} of {train.Seats[car]} seats" );
				most = Math.Max( most, train.BoardingCars.Sum( car => car.Count ) );
			}
		}
		Assert.IsTrue( most > 0, "a train seated guests from the ring" );
		Assert.AreEqual( 0, ride.AdmissionCount, "the trains that passed the station seated all six guests" );
	}

	[TestMethod]
	public void TheWinchFloorHalvesForATrainWithTheHeadwayFlagInTheStation()
	{
		// A release rate no gap can reach keeps flag 0x80 on until the test says so.
		var ride = NewRide( settings => settings["sCoasterType.uiTrainReleaseRate"] = "2000000000" );
		Assert.AreEqual( 1, ride.Trains.Count, "a lap shorter than the release rate gets one train" );
		ride.Command( 4, 1 );
		Run( ride, 5 );
		ride.Command( 4, 0 );
		Assert.IsTrue( ride.Trains.All( train => train.Flags.HasFlag( CoasterTrainFlags.Headway ) ), "leaving mode 64 sets 0x80 on every train" );
		var winch = ride.Definition.WinchSpeed;
		var halved = false;
		for ( var tick = 0; tick < 300 * 60 && !halved; tick++ )
		{
			ride.Advance( Tick );
			foreach ( var train in ride.Trains.Where( train => train.Flags.HasFlag( CoasterTrainFlags.InStation ) && train.Flags.HasFlag( CoasterTrainFlags.Headway ) ) )
				halved |= train.Speed <= winch * 0.5f + 0.01f && train.Speed > 0;
		}
		Assert.IsTrue( halved, $"a flagged train in the station settles at the halved winch floor {winch * 0.5f}" );
	}

	[TestMethod]
	public void AFlaggedTrainLosesItsHeadwayFlagOnceTheGapReachesTheReleaseRate()
	{
		var ride = NewRide( settings => settings["sCoasterType.uiTrainReleaseRate"] = "1" );
		ride.Command( 4, 1 );
		Run( ride, 2 );
		ride.Command( 4, 0 );
		Assert.IsTrue( ride.Trains.All( train => train.Flags.HasFlag( CoasterTrainFlags.Headway ) ) );
		ride.Advance( Tick );
		Assert.IsTrue( ride.Trains.All( train => !train.Flags.HasFlag( CoasterTrainFlags.Headway ) ), "a 1 ms release rate is reached by any gap (0x43d8a5)" );
	}

	[TestMethod]
	public void AFasterTrainThatCatchesUpWithTheOneAheadExchangesSpeedAndBumpsIt()
	{
		(CoasterTrain Behind, CoasterTrain Ahead) Setup( float gap )
		{
			var ride = NewRide( settings =>
			{
				settings["sCoasterType.uiMaxTrains"] = "2";
				settings["sCoasterType.uiTrainReleaseRate"] = "1000";
			} );
			Assert.AreEqual( 2, ride.Trains.Count );
			var (behind, ahead) = (ride.Trains[0], ride.Trains[1]);
			// In the brake zone: the bumped flag clears again only past the window and before the braking start (0x43d578), which is the rest of the track.
			behind.Position = ride.BrakingStart + 1f;
			ahead.Position = (behind.Position + gap) % ride.Samples.Count;
			behind.Speed = 30f;
			ahead.Speed = 10f;
			// The extents of the placed trains are those of their even placement; one step brings them in line with the new positions.
			ride.Advance( Tick );
			ride.Advance( Tick );
			return (behind, ahead);
		}
		// Right in front of the first train: the rear of the train ahead is behind the first train's front.
		var (behind, ahead) = Setup( 2f );
		Assert.IsTrue( ahead.Flags.HasFlag( CoasterTrainFlags.Bumped ), "the train ahead is flagged 0x40" );
		Assert.IsFalse( behind.Flags.HasFlag( CoasterTrainFlags.Bumped ) );
		// A control run with the train ahead far away shows what the hops alone do to the two speeds.
		var (controlBehind, controlAhead) = Setup( 200f );
		Assert.IsFalse( controlAhead.Flags.HasFlag( CoasterTrainFlags.Bumped ) );
		Assert.IsTrue( behind.Speed < controlBehind.Speed && ahead.Speed > controlAhead.Speed, $"speeds {behind.Speed}, {ahead.Speed} against {controlBehind.Speed}, {controlAhead.Speed}: (T + 2·O)/3 and (O + 2·T)/3" );
	}
}
