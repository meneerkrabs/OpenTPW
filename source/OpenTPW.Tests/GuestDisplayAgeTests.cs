using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Synthetic fixed-clock cases for native integer age, not original executable runtime fixtures.</summary>
[TestClass]
public class GuestDisplayAgeTests
{
	private static GuestSimulation Park()
	{
		var grid = new GuestPathGrid( 20, 10 );
		for ( var x = 0; x < 20; x++ ) grid.SetPath( x, 5, true );
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 100000;
		settings.ExitLevelVarSeconds = 0;
		return new GuestSimulation( grid, settings, 7 ) { ArrivalsEnabled = false };
	}

	private static void Steps( GuestSimulation park, int count )
	{
		for ( var tick = 0; tick < count; tick++ ) park.Tick( GuestSnapshotTests.Tick );
	}

	[TestMethod]
	public void BirthAtTick75AndReadAt134RetainsFourTurnsRatherThanThree()
	{
		var park = Park();
		Steps( park, 75 );
		var guest = park.SpawnInPark( 5, 5 );
		Steps( park, 59 );
		Assert.AreEqual( 4L, guest.DisplayTimeInParkTurns );
		Assert.AreEqual( 3L, (long)Math.Floor( guest.TimeInParkSeconds!.Value * 1000 / ParkCalendar.TurnMilliseconds ) );
		Assert.AreEqual( 4L, ParkVisitorRoster.ValuesOf( guest, refreshed: true ).Hours );
		Assert.AreEqual( 1L, ParkVisitorRoster.ValuesOf( guest, refreshed: false ).Hours );
	}

	[TestMethod]
	public void EveryBirthPhaseAcrossACompleteFixedClockCycleUsesIntegerSubtraction()
	{
		for ( var birth = 0; birth < GuestSimulation.DisplayTurnPhasePeriodTicks; birth++ )
		{
			var park = Park();
			Steps( park, birth );
			var guest = park.SpawnInPark( 5, 5 );
			for ( var gap = 0; gap <= 75; gap++ )
			{
				Assert.AreEqual( ParkCalendar.Turn( birth + gap ) - ParkCalendar.Turn( birth ), guest.DisplayTimeInParkTurns, $"birth {birth}, gap {gap}" );
				Steps( park, 1 );
			}
		}
	}

	[TestMethod]
	public void ExistingGuestsAdvanceOnceAndATurnCallbackBirthStartsAtZero()
	{
		var park = Park();
		var first = park.SpawnInPark( 5, 5 );
		Steps( park, 14 );
		Assert.IsTrue( park.NextTickStartsTurn );
		park.BeginSweepTick( GuestSnapshotTests.Tick );
		var added = park.SpawnInPark( 6, 5 );
		park.FinishSweep();
		Assert.AreEqual( 1L, first.DisplayTimeInParkTurns );
		Assert.AreEqual( 0L, added.DisplayTimeInParkTurns );
		Assert.AreEqual( 1L, ParkCalendar.Turn( park.TickCount ) );
	}

	[TestMethod]
	public void ZeroDeltaStillTakesTheExistingFixedTickButPauseTakesNoTick()
	{
		var park = Park();
		var guest = park.SpawnInPark( 5, 5 );
		for ( var tick = 0; tick < 15; tick++ ) park.Tick( 0 );
		Assert.AreEqual( 0d, guest.TimeInParkSeconds );
		Assert.AreEqual( 1L, guest.DisplayTimeInParkTurns, "Tick(0) already advances TickCount and gameplay park turns" );
		var clock = new FixedStepClock();
		Assert.AreEqual( 0, clock.Advance( 0, park.Tick ) );
		Assert.AreEqual( 1L, guest.DisplayTimeInParkTurns, "a paused frame supplies no tick" );
	}

	[TestMethod]
	public void SavedNonzeroPhaseIsImmediatelyExactAndSurvivesBothSidesOfATurnBoundary()
	{
		foreach ( var elapsed in new[] { 13, 14, 15, 58, 59, 60, 296, 297 } )
		{
			const long baseTick = 14955; // First tick of original world turn 1005; phase 75, not zero.
			var original = Park();
			original.AlignTurns( baseTick );
			original.SpawnInPark( 5, 5 );
			Steps( original, elapsed );
			var saved = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( original.CaptureSnapshot() ) );
			Assert.AreEqual( 75, saved.DisplayTurnPhaseTicks );
			foreach ( var usedTarget in new[] { false, true } )
			{
				var target = Park();
				if ( usedTarget ) { Steps( target, 23 ); target.AlignTurns( 888 ); }
				target.RestoreSnapshot( saved );
				Assert.AreEqual( saved.Guests[0].DisplayTimeInParkTurns, target.Guests.Single().DisplayTimeInParkTurns, "HUD before any reattachment" );
				Assert.AreEqual( ParkVisitorRoster.ValuesOf( original.Guests.Single(), true ).Hours, ParkVisitorRoster.ValuesOf( target.Guests.Single(), true ).Hours );
				// Saved display phase works even before the host's first AlignTurns call.
				Steps( target, 1 );
				Assert.AreEqual( ParkCalendar.Turn( baseTick + elapsed + 1 ) - ParkCalendar.Turn( baseTick ), target.Guests.Single().DisplayTimeInParkTurns );
				target.RestoreSnapshot( saved );
				target.AlignTurns( baseTick + elapsed );
				for ( var step = 1; step <= 35; step++ )
				{
					Steps( target, 1 );
					Assert.AreEqual( ParkCalendar.Turn( baseTick + elapsed + step ) - ParkCalendar.Turn( baseTick ), target.Guests.Single().DisplayTimeInParkTurns, $"saved at {elapsed}, step {step}" );
				}
			}
		}
	}

	[TestMethod]
	public void PhaseWrapAndUnsignedAgeWrapDoNotThrowOrAlterSeconds()
	{
		var park = Park();
		park.AlignTurns( 371 );
		var guest = park.SpawnInPark( 5, 5 );
		guest.DisplayTimeInParkTurns = uint.MaxValue;
		park.Tick( GuestSnapshotTests.Tick );
		Assert.AreEqual( 0L, guest.DisplayTimeInParkTurns, "uint subtraction wraps at 2^32, not at long.MaxValue" );
		Assert.AreEqual( (double)GuestSnapshotTests.Tick, guest.TimeInParkSeconds );
		Steps( park, 14 );
		Assert.AreEqual( 0L, guest.DisplayTimeInParkTurns );
		Steps( park, 1 );
		Assert.AreEqual( 1L, guest.DisplayTimeInParkTurns );
	}

	[TestMethod]
	public void LegacyKnownDurationsKeepTheirFallbackAndUnknownNeverGainsExactAge()
	{
		var park = Park();
		park.SpawnInPark( 5, 5 );
		var snapshot = park.CaptureSnapshot();
		var legacy = snapshot with { DisplayTurnPhaseTicks = null, Guests = new[] { snapshot.Guests[0] with { DisplayTimeInParkTurns = null, TimeInParkSeconds = 59.99 } } };
		park.RestoreSnapshot( legacy );
		Assert.IsNull( park.Guests.Single().DisplayTimeInParkTurns );
		Assert.AreEqual( 250L, ParkVisitorRoster.ValuesOf( park.Guests.Single(), true ).Hours );
		Steps( park, 60 );
		Assert.IsNull( park.CaptureSnapshot().Guests.Single().DisplayTimeInParkTurns );
		park.RestoreSnapshot( snapshot );
		park.Guests.Single().TimeInParkSeconds = null;
		Assert.IsNull( ParkVisitorRoster.ValuesOf( park.Guests.Single(), true ).Hours );
		var unknown = park.CaptureSnapshot();
		Assert.IsNull( unknown.Guests.Single().DisplayTimeInParkTurns, "a stale runtime cache is not persisted when duration is unknown" );
		park.RestoreSnapshot( unknown );
		Steps( park, 60 );
		Assert.IsNull( park.Guests.Single().TimeInParkSeconds );
		Assert.IsNull( park.Guests.Single().DisplayTimeInParkTurns );
	}

	[TestMethod]
	public void BothSnapshotValidatorsRejectMalformedDisplayMetadataBeforeReplacingLiveState()
	{
		var park = Park();
		park.SpawnInPark( 5, 5 );
		var snapshot = park.CaptureSnapshot();
		var before = ParkResumeJson.Serialize( snapshot );
		var versionField = typeof( GuestSimulation ).GetField( "poolActivityVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!;
		var version = versionField.GetValue( park );
		var envelope = ParkResumeSaveFileTests.Envelope();
		GuestSimulationSnapshot[] Invalid( GuestSimulationSnapshot source ) => new[]
		{
			source with { DisplayTurnPhaseTicks = -1 }, source with { DisplayTurnPhaseTicks = 372 },
			source with { DisplayTurnPhaseTicks = null, Guests = source.Guests.Select( g => g with { DisplayTimeInParkTurns = 0 } ).ToArray() },
			source with { Guests = source.Guests.Select( g => g with { DisplayTimeInParkTurns = -1 } ).ToArray() },
			source with { Guests = source.Guests.Select( g => g with { DisplayTimeInParkTurns = (long)uint.MaxValue + 1 } ).ToArray() },
			source with { Guests = source.Guests.Select( g => g with { DisplayTimeInParkTurns = 0, TimeInParkSeconds = null } ).ToArray() },
		};
		foreach ( var invalid in Invalid( snapshot ) )
		{
			Assert.IsFalse( park.ValidateSnapshot( invalid ).IsValid );
			Assert.ThrowsException<InvalidDataException>( () => park.RestoreSnapshot( invalid ) );
			CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( park.CaptureSnapshot() ) );
			Assert.AreEqual( version, versionField.GetValue( park ), "invalid metadata must not invalidate live pool-activity offers" );
		}
		park.RestoreSnapshot( snapshot );
		Assert.AreEqual( (int)version! + 1, (int)versionField.GetValue( park )!, "a valid restore still invalidates offers exactly once" );
		foreach ( var invalid in Invalid( envelope.Guests ) )
			Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Serialize( envelope with { Guests = invalid } ) );
	}

	[TestMethod]
	public void DisplayAgeMetadataLeavesGameplayRngSecondsAndStatisticsIdentical()
	{
		var exact = new GuestSnapshotTests.Rig();
		var legacy = new GuestSnapshotTests.Rig();
		exact.Simulation.ArrivalsEnabled = legacy.Simulation.ArrivalsEnabled = false;
		exact.Simulation.SpawnInPark( 5, 0 );
		legacy.Simulation.SpawnInPark( 5, 0 ).DisplayTimeInParkTurns = null;
		for ( var tick = 0; tick < 600; tick++ )
		{
			exact.Step(); legacy.Step();
			GuestSnapshotTests.Drive( exact, tick + 1 ); GuestSnapshotTests.Drive( legacy, tick + 1 );
			Assert.AreEqual( GuestSnapshotTests.CanonicalHash( exact ), GuestSnapshotTests.CanonicalHash( legacy ) );
			Assert.AreEqual( exact.Simulation.ComputeStateHash(), legacy.Simulation.ComputeStateHash() );
			Assert.AreEqual( exact.Simulation.GetStatistics(), legacy.Simulation.GetStatistics() );
			var a = exact.Simulation.CaptureSnapshot(); var b = legacy.Simulation.CaptureSnapshot();
			Assert.AreEqual( a.WorldGenerator, b.WorldGenerator );
			CollectionAssert.AreEqual( a.Guests.Select( g => g.TimeInParkSeconds ).ToArray(), b.Guests.Select( g => g.TimeInParkSeconds ).ToArray() );
			CollectionAssert.AreEqual( a.Guests.Select( g => (g.UmbrellaState, g.UmbrellaDueTurn, g.RainMoodAgeStartTurn) ).ToArray(), b.Guests.Select( g => (g.UmbrellaState, g.UmbrellaDueTurn, g.RainMoodAgeStartTurn) ).ToArray() );
		}
	}

	[TestMethod]
	public void ActiveUmbrellaDayGateStillReadsSecondsRatherThanExactDisplayAge()
	{
		var first = Park(); var second = Park();
		first.UmbrellaWeatherState = second.UmbrellaWeatherState = () => UmbrellaRules.WeatherRain;
		var a = first.SpawnInPark( 5, 5 ); var b = second.SpawnInPark( 5, 5 );
		a.TimeInParkSeconds = b.TimeInParkSeconds = 96.5 * ParkCalendar.TurnMilliseconds / 1000.0;
		a.DisplayTimeInParkTurns = 0; b.DisplayTimeInParkTurns = uint.MaxValue;
		first.SetUmbrellaStateForTest( a, UmbrellaRules.StateClosed );
		second.SetUmbrellaStateForTest( b, UmbrellaRules.StateClosed );
		var opened = false;
		for ( var tick = 0; tick < 1000; tick++ )
		{
			Steps( first, 1 ); Steps( second, 1 );
			Assert.AreEqual( (a.UmbrellaState, a.UmbrellaDueTurn), (b.UmbrellaState, b.UmbrellaDueTurn) );
			Assert.AreEqual( GuestSimulation.AgeTurnsOf( a ), GuestSimulation.AgeTurnsOf( b ) );
			Assert.AreEqual( first.ComputeStateHash(), second.ComputeStateHash() );
			Assert.AreEqual( first.CaptureSnapshot().WorldGenerator, second.CaptureSnapshot().WorldGenerator );
			opened |= a.UmbrellaState == UmbrellaRules.StateOpen;
		}
		Assert.IsTrue( opened, "the active weather fixture must reach an open umbrella" );
	}

	private sealed class StayPayments : IGuestPayments
	{
		public int AdmissionFee => 0;
		public int? Days;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
		public void StayEnded( int days ) => Days = days;
	}

	[TestMethod]
	public void LeavingStillReportsSecondsBasedStayEvenWithDifferentExactDisplayAge()
	{
		var park = Park();
		var payments = new StayPayments();
		park.Payments = payments;
		// the guests' update runs on the tick that starts a park turn (DET-014): the guest is made just before it
		while ( !park.NextTickStartsTurn )
			park.Tick( GuestSnapshotTests.Tick );
		var guest = park.SpawnInPark( 5, 5 );
		guest.TimeInParkSeconds = 100 * ParkCalendar.TurnMilliseconds / 1000.0;
		guest.DisplayTimeInParkTurns = 4000;
		guest.Paid = true;
		guest.State = GuestState.WalkingToVehicle;
		guest.HasWaypoint = false;
		Steps( park, 1 );
		Assert.AreEqual( GuestState.WaitingToGoHome, guest.State );
		Assert.AreEqual( ParkStatisticsHistory.StayDays( 100 ), payments.Days );
		Assert.AreNotEqual( ParkStatisticsHistory.StayDays( 4000 ), payments.Days );
	}

	[TestMethod]
	public void ExactAgeKeepsTheExisting24And48HourFormattingThresholds()
	{
		var guest = new Guest( 1, 0 ) { TimeInParkSeconds = 0 };
		foreach ( var (turns, refreshed, hours) in new[] { (23L, true, 20L), (24L, true, 25L), (47L, true, 45L), (48L, true, 50L), (95L, false, 23L), (96L, false, 25L), (187L, false, 47L), (188L, false, 48L), (192L, false, 50L) } )
		{
			guest.DisplayTimeInParkTurns = turns;
			Assert.AreEqual( hours, ParkVisitorRoster.ValuesOf( guest, refreshed ).Hours );
			Assert.AreEqual( hours > 48 ? UiTextRecord.Days : hours > 24 ? UiTextRecord.Day : UiTextRecord.Hours, ParkVisitorRoster.DurationRecord( hours ) );
			Assert.AreEqual( hours > 24 ? hours / 24 : hours, ParkVisitorRoster.DurationValue( hours ) );
		}
	}
}
