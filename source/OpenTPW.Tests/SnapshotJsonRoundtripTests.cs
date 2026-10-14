using System;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The runtime lane's snapshot DTOs are the state root's save envelope serializes with System.Text.Json:
/// every DTO survives a serialize/deserialize round trip and restores into a fresh object that continues
/// identically — including the nulls (free advice slots, a missing history record's slot value, no pending
/// forecast, unpositioned actors), which must survive as JSON nulls rather than being dropped.
/// </summary>
[TestClass]
public class SnapshotJsonRoundtripTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static string Json<T>( T value ) => JsonSerializer.Serialize( value );

	private static T RoundTrip<T>( T value ) =>
		JsonSerializer.Deserialize<T>( JsonSerializer.Serialize( value ) )!;

	[TestMethod]
	public void ClockAndGateSnapshotsRoundTrip()
	{
		var clock = new FixedStepClock();
		clock.Advance( FixedStepClock.TickDuration * 2.5, _ => { } );
		var clockSnapshot = clock.CaptureSnapshot();
		var restoredClock = new FixedStepClock();
		restoredClock.RestoreSnapshot( RoundTrip( clockSnapshot ) );
		Assert.AreEqual( Json( clockSnapshot ), Json( restoredClock.CaptureSnapshot() ) );

		var gate = new ParkGate( EconomyTestData.Park(), () => null, () => Array.Empty<OriginalObjectRuntime>() );
		gate.RestoreSnapshot( new ParkGate.ParkGateSnapshot( 12345 ) );
		var gateSnapshot = gate.CaptureSnapshot();
		var restoredGate = new ParkGate( EconomyTestData.Park(), () => null, () => Array.Empty<OriginalObjectRuntime>() );
		restoredGate.RestoreSnapshot( RoundTrip( gateSnapshot ) );
		Assert.AreEqual( Json( gateSnapshot ), Json( restoredGate.CaptureSnapshot() ) );
	}

	[TestMethod]
	public void WeatherSnapshotRoundTripsAndContinuesIdentically()
	{
		var original = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 4, 10, 10 );
		original.AdvanceTo( ParkCalendar.TickAtDay( 6 ) );
		original.ApplyQuality( 8 ); // a lightning-band quality, so the lightning clocks are live
		original.Step( 0 );
		// the first update rolled a forecast (tp.exe 0x511f70: lastChanged 0); the snapshot under test is the one with none pending
		var snapshot = original.CaptureSnapshot() with
		{
			PendingQuality = null,
			ApplyDay = 0,
			PendingStrikes = new[] { new ParkWeather.ThunderStrikeSnapshot( 12f, 300f, 34f ) }
		};
		var json = Json( snapshot );
		Assert.IsTrue( json.Contains( "\"PendingQuality\":null", StringComparison.Ordinal ),
			"no pending forecast survives as a JSON null, not a dropped property" );

		// The JSON path must land exactly where a direct restore of the same DTO lands.
		var reference = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 4, 10, 10 );
		reference.RestoreSnapshot( snapshot );
		var restored = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 4, 10, 10 );
		restored.RestoreSnapshot( RoundTrip( snapshot ) );
		reference.AdvanceTo( ParkCalendar.TickAtDay( 13 ) );
		restored.AdvanceTo( ParkCalendar.TickAtDay( 13 ) );
		Assert.AreEqual( Json( reference.CaptureSnapshot() ), Json( restored.CaptureSnapshot() ),
			"a week of continued weather through the JSON round trip matches the direct restore" );
	}

	[TestMethod]
	public void StaffSnapshotRoundTripsPositionsNullsAndTravel()
	{
		// A travelling mechanic mid-cell-step, plus an unpositioned entertainer on a pathless grid.
		var travelling = EconomyTestData.Park();
		var mechanic = EconomyTestData.HireBest( travelling, StaffType.Mechanic );
		mechanic.AssignedInstanceId = 100;
		var original = RuntimeSnapshotTests.World( travelling, RuntimeSnapshotTests.Paths() );
		original.Tick( 0.5f );
		original.Tick( 2.75f );

		var unpositioned = EconomyTestData.Park();
		EconomyTestData.HireBest( unpositioned, StaffType.Entertainer );
		var empty = new StaffWorldSimulation( new GuestPathGrid( 2, 2 ), () => unpositioned.Staff.Members, _ => null );

		var snapshot = original.CaptureSnapshot();
		var unpositionedSnapshot = empty.CaptureSnapshot();
		Assert.IsNull( unpositionedSnapshot.Actors.Single().CellX );
		Assert.AreEqual( Json( snapshot ), Json( RoundTrip( snapshot ) ), "the travelling actor's DTO is JSON-stable" );
		Assert.AreEqual( Json( unpositionedSnapshot ), Json( RoundTrip( unpositionedSnapshot ) ), "the null position survives as a JSON null" );

		var restoredUnpositioned = EconomyTestData.Park();
		EconomyTestData.HireBest( restoredUnpositioned, StaffType.Entertainer );
		var restoredEmpty = new StaffWorldSimulation( new GuestPathGrid( 2, 2 ), () => restoredUnpositioned.Staff.Members, _ => null );
		restoredEmpty.RestoreSnapshot( RoundTrip( unpositionedSnapshot ) );
		Assert.IsNull( restoredUnpositioned.Staff.Members.Single().CellX, "the JSON null restores as unpositioned" );

		var restoredPark = EconomyTestData.Park();
		var restoredMechanic = EconomyTestData.HireBest( restoredPark, StaffType.Mechanic );
		restoredMechanic.AssignedInstanceId = 100;
		var restored = RuntimeSnapshotTests.World( restoredPark, RuntimeSnapshotTests.Paths() );
		restored.RestoreSnapshot( RoundTrip( snapshot ) );
		Assert.AreEqual( (mechanic.CellX!.Value, mechanic.CellY!.Value), (restoredMechanic.CellX!.Value, restoredMechanic.CellY!.Value) );
		Assert.AreEqual( mechanic.TravelSeconds, restoredMechanic.TravelSeconds );
		for ( var step = 0; step < 3; step++ )
		{
			original.Tick( 0.5f );
			restored.Tick( 0.5f );
			Assert.AreEqual( (mechanic.CellX!.Value, mechanic.CellY!.Value), (restoredMechanic.CellX!.Value, restoredMechanic.CellY!.Value), $"step {step}" );
		}
	}

	[TestMethod]
	public void AdvisorSnapshotsRoundTripQueueHistoryAndReservation()
	{
		var controller = new AdvisorController( AdvisorSnapshotTests.Balance() );
		controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, liveGameTick: 100, gameType: 0, tutorialEnabled: true );
		controller.Update( () => 1000u, () => 250u, _ => 4000u ); // the welcome advice plays: history and reservation
		controller.HandleGameEvent( AdvisorGameEvent.ParkNowClosed, liveGameTick: 200, gameType: 0, tutorialEnabled: true );
		var snapshot = controller.CaptureSnapshot();
		Assert.AreEqual( 1, snapshot.Queue.Slots.Count( item => item != null ), "one advice is pending" );
		Assert.AreEqual( AdvisorScoreQueue.Capacity - 1, snapshot.Queue.Slots.Count( item => item is null ),
			"free slots are nulls the JSON must keep" );

		var roundTripped = RoundTrip( snapshot );
		Assert.AreEqual( Json( snapshot ), Json( roundTripped ) );
		var fresh = new AdvisorController( AdvisorSnapshotTests.Balance() );
		fresh.HandleGameEvent( AdvisorGameEvent.LevelStarted, liveGameTick: 0, gameType: 0, tutorialEnabled: true );
		fresh.RestoreSnapshot( roundTripped );
		Assert.AreEqual( Json( snapshot ), Json( fresh.CaptureSnapshot() ), "the restored controller carries the same queue" );

		// Both controllers pick the same next advice at the same moments after the round trip; the saved
		// reservation holds until advisor-clock 6000, so the picks resume after it.
		uint clock = 1000;
		for ( var step = 0; step < 8; step++ )
		{
			clock += 700;
			Assert.AreEqual( Pick( controller, clock ), Pick( fresh, clock ), $"step {step}" );
		}
	}

	private static int Pick( AdvisorController controller, uint clock ) =>
		controller.Update( () => clock, () => 300u, _ => 500u ) is { } selection ? selection.ResponseId : -1;

	[TestMethod]
	public void AutomaticAdvisorSnapshotRoundTrips()
	{
		long milliseconds = 20_000;
		var advisor = new AutomaticAdvisor( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => milliseconds );
		var snapshot = advisor.CaptureSnapshot();
		var json = Json( snapshot );
		Assert.IsTrue( json.Contains( "\"SpeechWasActive\":false", StringComparison.Ordinal ) );

		long restoredTime = 500;
		var restored = new AutomaticAdvisor( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => restoredTime );
		restored.RestoreSnapshot( RoundTrip( snapshot ) );
		Assert.AreEqual( 20_000u, restored.CaptureSnapshot().AdvisorClockMilliseconds,
			"the nested controller validates, and the clock rebases to the saved value" );
		milliseconds = 20_100;
		restoredTime = 600;
		Assert.AreEqual( 20_100u, restored.CaptureSnapshot().AdvisorClockMilliseconds );
	}
}
