using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
public class GuestTimeInParkReviewTests
{
	[TestMethod]
	public void RealQueueRideAndExitLaneBoundariesAllAdvance()
	{
		var rig = new GuestSnapshotTests.Rig();
		var seen = new HashSet<GuestState>();
		var sawHomeLane = false;
		for ( var tick = 0; tick < 9000; tick++ )
		{
			var before = rig.Simulation.Guests.Select( guest => (Guest: guest, guest.State, guest.LaneReversed, guest.TimeInParkSeconds) ).ToArray();
			rig.Step();
			GuestSnapshotTests.Drive( rig, tick + 1 );
			foreach ( var (guest, state, reversed, elapsed) in before )
			{
				if ( elapsed == null ) continue;
				// the stamp +0x1D4 is never rewritten (0x4FAFDB): the time runs on the road home too and ends with the guest
				sawHomeLane |= reversed || state is GuestState.CrossingRoadHome or GuestState.WalkingToVehicle or GuestState.WaitingToGoHome;
				if ( state == GuestState.Gone )
				{
					Assert.AreEqual( elapsed, guest.TimeInParkSeconds );
					continue;
				}
				Assert.AreEqual( elapsed.Value + (double)GuestSnapshotTests.Tick, guest.TimeInParkSeconds, $"{state} -> {guest.State}" );
				seen.Add( state );
			}
		}
		foreach ( var state in new[] { GuestState.Queueing, GuestState.Using, GuestState.LeavingPark } )
			Assert.IsTrue( seen.Contains( state ), $"real fixture must reach {state}" );
		Assert.IsTrue( sawHomeLane, "real guests must reach a reversed lane after their inside exit walk" );
	}

	[TestMethod]
	public void OverflowOnTheSecondGuestRejectsTheWholeTickWithoutAnySnapshotChange()
	{
		var rig = new GuestSnapshotTests.Rig();
		rig.Simulation.ArrivalsEnabled = false;
		rig.Simulation.SpawnInPark( 5, 0 );
		var overflow = rig.Simulation.SpawnInPark( 6, 0 );
		overflow.TimeInParkSeconds = Guest.MaximumTimeInParkSeconds;
		var before = ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() );
		Assert.ThrowsException<OverflowException>( () => rig.Simulation.Tick( GuestSnapshotTests.Tick ) );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ),
			"no guest, RNG, queue or clock changes before overflow refusal" );
	}

	[TestMethod]
	public void DifferentDisplayMetadataKeepsSuccessfulGameplayContinuationIdentical()
	{
		var known = new GuestSnapshotTests.Rig();
		var unknown = new GuestSnapshotTests.Rig();
		known.Simulation.ArrivalsEnabled = unknown.Simulation.ArrivalsEnabled = false;
		known.Simulation.SpawnInPark( 5, 0 ).TimeInParkSeconds = 90061;
		unknown.Simulation.SpawnInPark( 5, 0 ).TimeInParkSeconds = null;
		for ( var tick = 0; tick < 600; tick++ )
		{
			known.Step(); unknown.Step();
			GuestSnapshotTests.Drive( known, tick + 1 ); GuestSnapshotTests.Drive( unknown, tick + 1 );
			Assert.AreEqual( GuestSnapshotTests.CanonicalHash( known ), GuestSnapshotTests.CanonicalHash( unknown ) );
			Assert.AreEqual( known.Simulation.ComputeStateHash(), unknown.Simulation.ComputeStateHash() );
		}
		Assert.IsNull( unknown.Simulation.Guests.Single().TimeInParkSeconds );
		Assert.IsTrue( known.Simulation.Guests.Single().TimeInParkSeconds > 90061 );
	}

	[TestMethod]
	public void OldWorldJsonWithoutDurationStaysUnknownAndFormattingIgnoresCulture()
	{
		var json = JsonNode.Parse( ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() ) )!;
		foreach ( var guest in json["Guests"]!["Guests"]!.AsArray() )
		{
			guest!.AsObject().Remove( "TimeInParkSeconds" );
			guest.AsObject().Remove( "DisplayTimeInParkTurns" );
		}
		json["Guests"]!.AsObject().Remove( "DisplayTurnPhaseTicks" );
		var restored = ParkResumeSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) );
		Assert.IsTrue( restored.Guests.Guests.All( guest => guest.TimeInParkSeconds == null ) );
		var rig = new GuestSnapshotTests.Rig();
		var visitor = rig.Simulation.SpawnInPark( 5, 0 );
		visitor.DisplayTimeInParkTurns = null; // Explicit legacy seconds-only display fixture.
		var culture = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo( "ar-SA" );
			visitor.TimeInParkSeconds = Guest.MaximumTimeInParkSeconds;
			Assert.IsTrue( ParkVisitorRoster.ValuesOf( visitor, refreshed: true ).Hours > 0, "the longest duration does not overflow" );
			visitor.TimeInParkSeconds = 59.99;
			Assert.AreEqual( 240L * 3750 / 3600, ParkVisitorRoster.ValuesOf( visitor, refreshed: true ).Hours );
			Assert.AreEqual( "10", ParkVisitorRoster.DurationValue( 250 ).ToString( CultureInfo.InvariantCulture ), "the cell's number ignores the culture" );
		}
		finally { CultureInfo.CurrentCulture = culture; }
	}

	[TestMethod]
	public void ClosedEconomyRefusalLeavesTheCounterRunning()
	{
		var rig = new GuestSnapshotTests.Rig();
		rig.Simulation.ArrivalsEnabled = false;
		var economy = EconomyTestData.Park();
		economy.ClosePark();
		rig.Simulation.Payments = new GuestEconomyBridge( () => economy, rig.Simulation );
		var guest = rig.Simulation.SpawnArrival()!;
		// the closed park holds the guest at the booth in state 3 for 200 to 349 turns (0x004FF7F0), then sends it home (state 18)
		for ( var tick = 0; tick < 40000 && guest.State != GuestState.LeavingPark; tick++ ) rig.Step();
		Assert.AreEqual( GuestState.LeavingPark, guest.State );
		Assert.AreEqual( 0L, rig.Simulation.Admissions );
		Assert.IsTrue( guest.TimeInParkSeconds > 0, "the counter is the creation stamp's, not the admission's" );
	}
	[TestMethod]
	public void FailedExitRouteCountsTheLastInsideTickBeforeRemoval()
	{
		var rig = new GuestSnapshotTests.Rig();
		rig.Simulation.ArrivalsEnabled = false;
		var guest = rig.Simulation.SpawnInPark( 5, 0 );
		// a level without an arrival lane has no gate to walk to: its leaving guests vanish (a level with a gate whose route fails keeps its guest in state 6, WALK-004)
		rig.Simulation.Settings.ArrivalLaneA = null;
		rig.Simulation.Settings.ArrivalLaneB = null;
		guest.State = GuestState.LeavingPark;
		guest.ExitLevel = 0;
		guest.Happiness = 0;
		rig.Simulation.Tick( GuestSnapshotTests.Tick );
		Assert.AreEqual( GuestState.Gone, guest.State, "a level without an arrival lane has no gate" );
		Assert.AreEqual( (double)GuestSnapshotTests.Tick, guest.TimeInParkSeconds );
		Assert.AreEqual( 0, rig.Simulation.Guests.Count );
	}

}
