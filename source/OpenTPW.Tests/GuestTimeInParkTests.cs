using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
public class GuestTimeInParkTests
{
	private static GuestSimulation Park( int admission = 20 )
	{
		var grid = new GuestPathGrid( 20, 10 );
		for ( var x = 0; x < 20; x++ ) grid.SetPath( x, 5, true );
		var settings = GuestTests.CreateSettings();
		settings.AdmissionFee = admission;
		settings.ExitLevelSeconds = 100000;
		settings.ExitLevelVarSeconds = 0;
		return new GuestSimulation( grid, settings, 7 ) { ArrivalsEnabled = false };
	}
	private static void Steps( GuestSimulation park, int count )
	{
		for ( var i = 0; i < count; i++ ) park.Tick( (float)FixedStepClock.TickDuration );
	}
	private static ulong Canonical( GuestSimulation park )
	{
		var hash = new StateHasher();
		park.AddCanonicalState( hash );
		return hash.Value;
	}

	[TestMethod]
	public void TheCounterStartsAtCreationAndRunsThroughTheBoothAndTheRoadHome()
	{
		// [BIN:TPW-EXE:0x004FAFDB] the stamp +0x1D4 is written by the person constructor only: nothing resets it at admission or stops it on the road home
		var park = Park();
		var guest = park.SpawnArrival()!;
		Assert.AreEqual( 0d, guest.TimeInParkSeconds, "created at zero" );
		var sawBooth = false;
		var last = 0d;
		for ( var i = 0; i < 20000 && guest.State != GuestState.EnteringPark; i++ )
		{
			sawBooth |= guest.State == GuestState.AtTicketBooth;
			Steps( park, 1 );
			Assert.IsTrue( guest.TimeInParkSeconds >= last, "the road crossing and the booth count" );
			last = guest.TimeInParkSeconds!.Value;
		}
		Assert.IsTrue( sawBooth, "the real arrival path must include the booth" );
		Assert.AreEqual( 1L, park.Admissions );
		Assert.IsTrue( guest.TimeInParkSeconds > 5, $"the admission did not reset it ({guest.TimeInParkSeconds})" );
		guest.State = GuestState.WaitingToGoHome;
		var atExit = guest.TimeInParkSeconds;
		Steps( park, 2 );
		Assert.IsTrue( guest.TimeInParkSeconds > atExit, "the road home counts too" );
	}

	[TestMethod]
	public void ARefusedVisitorsCounterRunsAsWell()
	{
		var park = Park( int.MaxValue );
		var guest = park.SpawnArrival()!;
		for ( var i = 0; i < 20000 && guest.State is not (GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.Gone); i++ ) Steps( park, 1 );
		Assert.AreEqual( 0L, park.Admissions );
		Assert.AreEqual( GuestState.LeavingPark, guest.State, "the booth must refuse and turn this guest home" );
		Assert.AreEqual( 0, guest.ExitLevel, "state 18 with the exit counter zeroed" );
		Assert.IsTrue( guest.TimeInParkSeconds > 0 );
	}

	[TestMethod]
	public void SeededGuestsUseSimulationClockAndPauseResumeDoesNotUseWallTime()
	{
		var park = Park();
		var guest = park.SpawnInPark( 5, 5 );
		Assert.AreEqual( 0d, guest.TimeInParkSeconds );
		var clock = new FixedStepClock();
		Assert.AreEqual( 6, clock.Advance( 0.1, park.Tick ) );
		var normal = guest.TimeInParkSeconds!.Value;
		Assert.AreEqual( 0, clock.Advance( 0, park.Tick ), "pause supplies no simulation time" );
		Assert.AreEqual( normal, guest.TimeInParkSeconds );
		Assert.AreEqual( 12, clock.Advance( 0.1 * 2, park.Tick ), "twice the simulation scale runs twice the steps" );
		Assert.AreEqual( normal * 3, guest.TimeInParkSeconds!.Value, 1e-12 );
		park.Tick( 0 );
		Assert.AreEqual( normal * 3, guest.TimeInParkSeconds!.Value, 1e-12 );
	}

	[TestMethod]
	public void CodecRetainsKnownDurationAndOldMissingDurationStaysUnknownAfterResume()
	{
		var park = Park();
		park.SpawnInPark( 5, 5 );
		Steps( park, 100 );
		var bytes = ParkResumeJson.Serialize( park.CaptureSnapshot() );
		var restored = Park();
		restored.RestoreSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( bytes ) );
		Assert.AreEqual( park.Guests.Single().TimeInParkSeconds, restored.Guests.Single().TimeInParkSeconds );
		Steps( park, 10 ); Steps( restored, 10 );
		Assert.AreEqual( park.Guests.Single().TimeInParkSeconds, restored.Guests.Single().TimeInParkSeconds );
		var json = JsonNode.Parse( bytes )!;
		json["Guests"]![0]!.AsObject().Remove( "TimeInParkSeconds" );
		json["Guests"]![0]!.AsObject().Remove( "DisplayTimeInParkTurns" );
		json.AsObject().Remove( "DisplayTurnPhaseTicks" );
		var old = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( Encoding.UTF8.GetBytes( json.ToJsonString() ) );
		restored.RestoreSnapshot( old );
		Steps( restored, 10 );
		Assert.IsNull( restored.Guests.Single().TimeInParkSeconds );
		Assert.IsNull( restored.CaptureSnapshot().Guests.Single().TimeInParkSeconds );
	}

	[TestMethod]
	public void InvalidDurationsAndCounterOverflowRejectBeforeMutation()
	{
		var park = Park();
		var guest = park.SpawnInPark( 5, 5 );
		var snapshot = park.CaptureSnapshot();
		foreach ( var bad in new[] { -1d, double.NaN, double.PositiveInfinity, double.MaxValue } )
		{
			var invalid = snapshot with { Guests = new[] { snapshot.Guests[0] with { TimeInParkSeconds = bad } } };
			Assert.IsFalse( park.ValidateSnapshot( invalid ).IsValid );
			Assert.ThrowsException<InvalidDataException>( () => park.RestoreSnapshot( invalid ) );
			Assert.AreEqual( 0d, guest.TimeInParkSeconds );
		}
		foreach ( var badDelta in new[] { -1f, float.NaN, float.PositiveInfinity, float.MaxValue } ) Assert.ThrowsException<ArgumentOutOfRangeException>( () => park.Tick( badDelta ) );
		guest.TimeInParkSeconds = Guest.MaximumTimeInParkSeconds;
		var tick = park.TickCount;
		Assert.ThrowsException<OverflowException>( () => park.Tick( 1 ) );
		Assert.AreEqual( tick, park.TickCount );
		Assert.AreEqual( 0d, park.TimeSeconds );
		Assert.AreEqual( Guest.MaximumTimeInParkSeconds, guest.TimeInParkSeconds );
	}

	[TestMethod]
	public void WorldSaveShapeRejectsInvalidDurationAndCodecAcceptsUnknown()
	{
		var envelope = ParkResumeSaveFileTests.Envelope();
		var guests = envelope.Guests.Guests;
		foreach ( var bad in new[] { -1d, double.NaN, double.PositiveInfinity, double.MaxValue } )
		{
			var changed = guests.ToArray(); changed[0] = changed[0] with { TimeInParkSeconds = bad };
			Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Serialize( envelope with { Guests = envelope.Guests with { Guests = changed } } ) );
		}
		var known = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( envelope ) );
		CollectionAssert.AreEqual( guests.Select( g => g.TimeInParkSeconds ).ToArray(), known.Guests.Guests.Select( g => g.TimeInParkSeconds ).ToArray() );
		var unknown = guests.Select( g => g with { TimeInParkSeconds = null, DisplayTimeInParkTurns = null } ).ToArray();
		var loaded = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( envelope with { Guests = envelope.Guests with { Guests = unknown } } ) );
		Assert.IsTrue( loaded.Guests.Guests.All( g => g.TimeInParkSeconds == null ) );
	}

	[TestMethod]
	public void DisplayMetadataDoesNotChangeGameplayHashesAndUiUsesWholeTurnSteps()
	{
		var park = Park(); var guest = park.SpawnInPark( 5, 5 );
		guest.DisplayTimeInParkTurns = null; // Explicit legacy seconds-only display fixture.
		var canonical = Canonical( park ); var gameplay = park.ComputeStateHash();
		// 248 ms turns, whole 4-turn steps of 15000 park seconds, whole hours (tp.exe 0x49326c)
		foreach ( var (duration, hours) in new (double?, long?)[] { (0, 0), (3661.9, 14764L * 3750 / 3600), (90061, 363148L * 3750 / 3600), (null, null) } )
		{
			guest.TimeInParkSeconds = duration;
			Assert.AreEqual( canonical, Canonical( park ) );
			Assert.AreEqual( gameplay, park.ComputeStateHash() );
			Assert.AreEqual( hours, ParkVisitorRoster.ValuesOf( guest, refreshed: true ).Hours );
		}
	}
}
