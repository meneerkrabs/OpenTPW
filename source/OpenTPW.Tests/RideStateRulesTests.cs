using System.Linq;
using System.Collections.Generic;
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The ride's state rules of docs/reverse/ORACLE.md F27: the reopening guard 0xde654, the breakdown part of the ride update
/// 0xe077c and what a repair 0xdef2c restores. The oracle suite executes the routines; these tests pin how OpenTPW applies them.
/// </summary>
[TestClass]
public class RideStateRulesTests
{
	[TestMethod]
	public void TheStatusBannerTakesTheFirstStatusThatApplies()
	{
		var open = new RideStatusFacts( RideObjectStates.Idle, false, false, false, false, RideServiceProgress.Waiting, false, false, IsOpen: true );
		Assert.IsNull( RideWindowStatus.Of( open ), "an open ride with nothing to report shows no banner" );
		Assert.AreEqual( UIStrings.Closed, RideWindowStatus.Of( open with { IsOpen = false } ) );
		// 0x485f60's order: condemned, the track, an upgrade, a called service, a breakdown, the queue, the exit, closed
		var everything = open with { State = RideObjectStates.Condemned, TrackIncomplete = true, TrackInvalid = true, ServiceCalled = true, QueueDetached = true, ExitDetached = true, IsOpen = false };
		Assert.AreEqual( UIStrings.Condemned, RideWindowStatus.Of( everything ) );
		Assert.AreEqual( UIStrings.TrackIncomplete, RideWindowStatus.Of( everything with { State = RideObjectStates.Upgrading } ) );
		Assert.AreEqual( UIStrings.TrackInvalid, RideWindowStatus.Of( everything with { State = RideObjectStates.Upgrading, TrackIncomplete = false } ) );
		Assert.AreEqual( UIStrings.UpgradeWaitingForMechanic, RideWindowStatus.Of( everything with { State = RideObjectStates.Upgrading, TrackIncomplete = false, TrackInvalid = false } ), "an upgrade before a call" );
		Assert.AreEqual( UIStrings.RepairWaitingForMechanic, RideWindowStatus.Of( everything with { State = RideObjectStates.BrokenDown, TrackIncomplete = false, TrackInvalid = false } ), "a call before a breakdown" );
		var services = Enum.GetValues<RideServiceProgress>();
		var upgrading = open with { State = RideObjectStates.Upgrading };
		CollectionAssert.AreEqual( new UIStrings?[] { UIStrings.UpgradeNoMechanics, UIStrings.UpgradeMechanicsStrike, UIStrings.UpgradeMechanicOnWay, UIStrings.UpgradeInProgress, UIStrings.UpgradeWaitingForMechanic },
			services.Select( service => RideWindowStatus.Of( upgrading with { Service = service } ) ).ToArray() );
		var called = open with { ServiceCalled = true, IsOpen = false };
		CollectionAssert.AreEqual( new UIStrings?[] { UIStrings.RepairNoMechanics, UIStrings.RepairMechanicsStrike, UIStrings.RepairMechanicOnWay, UIStrings.RepairsInProgress, UIStrings.RepairWaitingForMechanic },
			services.Select( service => RideWindowStatus.Of( called with { Service = service } ) ).ToArray() );
		CollectionAssert.AreEqual( new UIStrings?[] { UIStrings.CleaningNoCleaners, UIStrings.CleaningCleanersStrike, UIStrings.CleaningCleanerOnWay, UIStrings.CleaningInProgress, UIStrings.CleaningWaitingForCleaner },
			services.Select( service => RideWindowStatus.Of( called with { Relief = true, Service = service } ) ).ToArray(), "a toilet's call is a cleaning" );
		var broken = open with { State = RideObjectStates.BrokenDown };
		Assert.AreEqual( UIStrings.BrokenDown, RideWindowStatus.Of( broken ) );
		Assert.AreEqual( UIStrings.RepairsInProgress, RideWindowStatus.Of( broken with { Service = RideServiceProgress.AtWork } ), "the mechanic at work on the broken ride" );
		Assert.AreEqual( UIStrings.QueueNotConnected, RideWindowStatus.Of( open with { QueueDetached = true } ) );
		Assert.AreEqual( UIStrings.ClosedQueueNotConnected, RideWindowStatus.Of( open with { QueueDetached = true, IsOpen = false } ) );
		Assert.AreEqual( UIStrings.ExitNotConnected, RideWindowStatus.Of( open with { ExitDetached = true, IsOpen = false } ) );
		Assert.IsTrue( RideWindowStatus.LocksDoor( UIStrings.TrackIncomplete ) && RideWindowStatus.LocksDoor( UIStrings.TrackInvalid ), "0x4ad4e0 greys the door for the track statuses" );
		Assert.IsFalse( RideWindowStatus.LocksDoor( UIStrings.Closed ) || RideWindowStatus.LocksDoor( null ) );
	}

	[TestMethod]
	public void TheReopeningGuardReadsTheRideStateNotVarBroken()
	{
		Assert.IsTrue( RideOpening.MayOpen( RideObjectStates.Idle, false, true, 0, false ) );
		Assert.IsTrue( RideOpening.MayOpen( RideObjectStates.NotAnAttraction, false, true, 0, false ), "state 3 does not block" );
		foreach ( var state in new[] { RideObjectStates.BrokenDown, RideObjectStates.Upgrading, RideObjectStates.Condemned } )
			Assert.IsFalse( RideOpening.MayOpen( state, false, true, 0, true ), $"state {state}" );
		Assert.IsFalse( RideOpening.MayOpen( RideObjectStates.Idle, true, true, 0, true ), "the maintenance flag blocks" );
		Assert.IsFalse( RideOpening.MayOpen( RideObjectStates.Idle, false, false, 0, true ), "a queue that no longer joins a path blocks" );
		Assert.IsFalse( RideOpening.MayOpen( RideObjectStates.Idle, false, true, RideOpening.CoasterTrackType, false ), "a coaster needs a complete circuit" );
		Assert.IsTrue( RideOpening.MayOpen( RideObjectStates.Idle, false, true, RideOpening.CoasterTrackType, true ) );
		Assert.IsTrue( RideOpening.MayOpen( RideObjectStates.Idle, false, true, 1, false ), "other track types need no circuit" );
	}

	[TestMethod]
	public void TheRideUpdateSkipsBrokenClosedAndInertRidesAndRunsOnEveryEighthTurn()
	{
		Assert.IsTrue( RideBreakdownRules.RunsWear( RideObjectStates.Idle, true, 64 ) );
		Assert.IsTrue( RideBreakdownRules.RunsWear( RideObjectStates.Idle, true, 8 ) );
		Assert.IsFalse( RideBreakdownRules.RunsWear( RideObjectStates.Idle, true, 12 ), "the world turn must be a multiple of 8" );
		Assert.IsFalse( RideBreakdownRules.RunsWear( RideObjectStates.Idle, false, 64 ), "a closed ride (open word 0) neither wears nor breaks down" );
		foreach ( var state in new[] { RideObjectStates.BrokenDown, RideObjectStates.NotAnAttraction, RideObjectStates.Condemned } )
			Assert.IsFalse( RideBreakdownRules.RunsWear( state, true, 64 ), $"state {state}" );
		Assert.IsTrue( RideBreakdownRules.SkipsUpdate( RideObjectStates.Condemned ) );
		Assert.IsFalse( RideBreakdownRules.SkipsUpdate( RideObjectStates.BrokenDown ), "state 1 still runs the relief test, state 3 and 4 do not" );
	}

	[TestMethod]
	public void ARideBreaksDownWhenALifeOrWearGaugeByteIsZero()
	{
		Assert.IsFalse( RideBreakdownRules.BreaksDown( false, 0, 100f, 100f ) );
		Assert.IsFalse( RideBreakdownRules.BreaksDown( false, 0, 1f, 1f ) );
		Assert.IsTrue( RideBreakdownRules.BreaksDown( false, 0, 100f, 0.99f ), "0.99 truncates to 0" );
		Assert.IsTrue( RideBreakdownRules.BreaksDown( false, 0, 0f, 100f ) );
		Assert.IsTrue( RideBreakdownRules.BreaksDown( false, 0, 100f, 256f ), "a gauge of 256 is the byte 0" );
		Assert.IsTrue( RideBreakdownRules.BreaksDown( false, 0, 100f, float.NaN ), "NaN truncates to 0x80000000, byte 0" );
		Assert.IsFalse( RideBreakdownRules.BreaksDown( true, 0, 0f, 0f ), "a relief object never breaks down" );
		Assert.IsFalse( RideBreakdownRules.BreaksDown( false, 1, 0f, 0f ), "VAR_BREAKSTAT already announced it" );
	}

	[TestMethod]
	public void TheLifeGaugeLosesFiveWhenTheWearGaugeRanOutFirst()
	{
		Assert.AreEqual( 95f, RideBreakdownRules.LifeAfterBreakdown( 100f ) );
		Assert.AreEqual( 0f, RideBreakdownRules.LifeAfterBreakdown( 3f ), "3 - 5 clamps to 0" );
		Assert.AreEqual( 0f, RideBreakdownRules.LifeAfterBreakdown( 0f ), "an empty gauge is announced as it is" );
		Assert.AreEqual( 0.5f, RideBreakdownRules.LifeAfterBreakdown( 0.5f ), "a gauge byte of 0 does not pay" );
		Assert.AreEqual( 100f, RideBreakdownRules.LifeAfterBreakdown( 300f ), "above the range it clamps to 100" );
		Assert.IsTrue( float.IsNaN( RideBreakdownRules.LifeAfterBreakdown( float.NaN ) ) || RideBreakdownRules.GaugeByte( float.NaN ) == 0 );
	}

	[TestMethod]
	public void ARepairWritesTheFullGaugeAndOpensTheRideExceptAnIncompleteCoaster()
	{
		Assert.AreEqual( 100f, RideRepair.FullGauge );
		Assert.IsTrue( RideRepair.OpensAfterRepair( 0, false ) );
		Assert.IsTrue( RideRepair.OpensAfterRepair( RideOpening.CoasterTrackType, true ) );
		Assert.IsFalse( RideRepair.OpensAfterRepair( RideOpening.CoasterTrackType, false ), "the repair closes a coaster whose circuit is not complete again" );
	}

	[TestMethod]
	public void AClosedRideWithAnEmptyLifeGaugeBreaksDownOnlyOnceItIsOpen()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var ride );
		ride!.LifeGauge = 0.5;
		ride.IsOpen = false;
		EconomyTestData.AdvanceToTurn( park, 5 * ParkEconomy.RideUpdateInterval );
		Assert.IsFalse( ride.IsBrokenDown, "a closed ride is not updated" );
		ride.IsOpen = true;
		EconomyTestData.AdvanceToTurn( park, 6 * ParkEconomy.RideUpdateInterval );
		Assert.IsTrue( ride.IsBrokenDown );
	}

	[TestMethod]
	public void AScriptHostedObjectBreaksDownThroughItsScriptAndReportsWear()
	{
		var park = EconomyTestData.Park();
		var writes = new List<ParkEvent>();
		park.EventRaised += item => { if ( item.Kind == ParkEventKind.RideScriptVariable ) writes.Add( item ); };
		park.TryBuild( 1100, out var ride );
		ride!.ScriptHosted = true;
		ride.Repair = 0;
		EconomyTestData.AdvanceToTurn( park, ParkEconomy.RideUpdateInterval );
		// [BIN:TPW-EXE:0x4e0b90 breakdown check] VAR_BREAKSTAT = 1 and state 0 until the script's VAR_BROKEN is read (0x4e14e0)
		Assert.IsFalse( ride.IsBrokenDown );
		Assert.AreEqual( 1, ride.ScriptBreakStat );
		Assert.AreEqual( 1, writes.Count( item => item.InstanceId == ride.Id && item.InfoId == (int)RideVariables.VAR_BREAKSTAT && item.Amount == 1 ) );
		EconomyTestData.AdvanceToTurn( park, 2 * ParkEconomy.RideUpdateInterval );
		Assert.AreEqual( 1, writes.Count( item => item.InfoId == (int)RideVariables.VAR_BREAKSTAT ), "VAR_BREAKSTAT set: no second request" );
		park.PublishRideState( ride );
		EconomyTestData.RideUpdateEntersBrokenState( park, ride );
		Assert.IsTrue( ride.IsBrokenDown, "the ride update's state 1" );
		ride.IsBrokenDown = false;
		park.PublishRideState( ride );
		Assert.IsFalse( ride.IsBrokenDown, "the one state word stays as SetState(0) left it: no second breakdown" );

		// [BIN:TPW-EXE:0x4e0d16 relief worn] a relief object below 25 writes VAR_WORN = 1 on every update
		park.TryBuild( 1402, out var toilet );
		toilet!.ScriptHosted = true;
		toilet.Repair = 24.9;
		var turn = park.Turn;
		EconomyTestData.AdvanceToTurn( park, turn + 1 );
		Assert.IsTrue( writes.Any( item => item.InstanceId == toilet.Id && item.InfoId == (int)RideVariables.VAR_WORN && item.Amount == 1 ) );
		Assert.IsFalse( toilet.IsBrokenDown, "relief objects never break down" );
	}

	[TestMethod]
	public void TheStateWordIsSavedSoALoadKeepsItsStateAndCountsNoSecondBreakdown()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var ride );
		ride!.ScriptHosted = true;
		EconomyTestData.RideUpdateEntersBrokenState( park, ride );
		Assert.IsTrue( ride.IsBrokenDown );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Capture( park ), park.Settings, park.Catalog );
		var loaded = restored.Objects.Single( item => item.Id == ride.Id );
		Assert.AreEqual( RideObjectStates.BrokenDown, loaded.StateWord.Value, "the saved state word" );
		var events = 0;
		restored.EventRaised += _ => events++;
		restored.PublishRideState( loaded );
		Assert.AreEqual( 0, events, "a load publishes no second breakdown: no second mechanic" );

		// the repair finished, the level has not reopened the ride yet: the word is 0 and stays 0
		ride.IsBrokenDown = false;
		var repaired = ParkSaveFile.Restore( ParkSaveFile.Capture( park ), park.Settings, park.Catalog );
		Assert.IsFalse( repaired.Objects.Single( item => item.Id == ride.Id ).IsBrokenDown );

		// a condemned ride keeps state 4 through a save
		ride.LifeGauge = 0.5;
		ride.IsBrokenDown = true;
		Assert.AreEqual( RideObjectStates.Condemned, ride.StateWord.Value );
		var condemned = ParkSaveFile.Restore( ParkSaveFile.Capture( park ), park.Settings, park.Catalog );
		Assert.AreEqual( RideObjectStates.Condemned, condemned.Objects.Single( item => item.Id == ride.Id ).StateWord.Value );

		// an older save holds no state word: the broken-down flag and the life gauge give 1 or 4 as the ride update would
		var json = System.Text.Json.Nodes.JsonNode.Parse( ParkResumeJson.Serialize( ParkSaveFile.Capture( park ) ) )!;
		var removed = 0;
		void Strip( System.Text.Json.Nodes.JsonNode? node )
		{
			if ( node is System.Text.Json.Nodes.JsonObject obj )
			{
				foreach ( var key in obj.Select( pair => pair.Key ).ToList() )
				{
					if ( string.Equals( key, "rideState", StringComparison.OrdinalIgnoreCase ) ) { obj.Remove( key ); removed++; }
					else Strip( obj[key] );
				}
			}
			else if ( node is System.Text.Json.Nodes.JsonArray array )
				foreach ( var item in array )
					Strip( item );
		}
		Strip( json );
		Assert.IsTrue( removed > 0, "the saved state word was in the JSON" );
		var older = ParkSaveFile.Restore( ParkResumeJson.Deserialize<ParkSaveFile.Data>( System.Text.Encoding.UTF8.GetBytes( json.ToJsonString() ) ), park.Settings, park.Catalog );
		Assert.AreEqual( RideObjectStates.Condemned, older.Objects.Single( item => item.Id == ride.Id ).StateWord.Value, "the life gauge's byte 0 gives state 4" );
	}

	[TestMethod]
	public void AHeadlessMechanicWaitsForTheFixAnimationOfAScriptHostedRide()
	{
		// [BIN:TPW-EXE:0x4da830 repairing] the economy's own mechanic dispatch: the job timer ends, then the repair waits while the script reports VAR_BROKEN
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var ride );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		ride!.ScriptHosted = true;
		ride.ScriptBroken = true;
		ride.IsBrokenDown = true;
		ride.Repair = 40;
		var cleared = false;
		park.EventRaised += item => cleared |= item.Kind == ParkEventKind.RideScriptVariable && item.InstanceId == ride.Id && item.InfoId == (int)RideVariables.VAR_BREAKSTAT && item.Amount == 0;
		for ( var turn = 0; turn < 2000 && !cleared; turn++ )
			EconomyTestData.AdvanceToTurn( park, park.Turn + 1 );
		Assert.IsTrue( cleared, "the timer ran out: VAR_BREAKSTAT = 0" );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 20 );
		Assert.IsTrue( ride.IsBrokenDown, "still waiting for the script" );
		Assert.AreEqual( mechanic.Id, ride.MechanicId, "he keeps the job" );
		Assert.AreEqual( 1L, mechanic.BusyTicks, "the next staff update looks again" );
		ride.ScriptBroken = false;
		EconomyTestData.AdvanceToTurn( park, park.Turn + 2 );
		Assert.IsFalse( ride.IsBrokenDown, "the repair completes once VAR_BROKEN is clear" );
		Assert.AreEqual( 0, ride.MechanicId );
	}

	[TestMethod]
	public void ABrokenDownRideDoesNotWearAnyFurther()
	{
		var park = EconomyTestData.Park();
		var rides = new EconomyTestData.RideOperations();
		park.RideOperations = rides;
		park.TryBuild( 1100, out var ride );
		ride!.Repair = 40;
		ride.IsBrokenDown = true;
		rides.Set( ride.Id, new RideOperation( true, 5, 50 ) );
		EconomyTestData.AdvanceToTurn( park, 3 * ParkEconomy.WearInterval );
		Assert.AreEqual( 40, ride.Repair, "state 1 skips the wear step" );
	}

	[TestMethod]
	public void AMechanicAtWorkOnAnOpenRideDoesNotStopItsUpdate()
	{
		var park = EconomyTestData.Park();
		var rides = new EconomyTestData.RideOperations();
		park.RideOperations = rides;
		park.TryBuild( 1100, out var ride );
		ride!.MechanicId = 99; // a repair of a worn ride, not a breakdown: the ride is in state 0
		rides.Set( ride.Id, new RideOperation( true, 5, 50 ) );
		EconomyTestData.AdvanceToTurn( park, ParkEconomy.WearInterval );
		Assert.IsTrue( ride.Repair < 100, "the ride still wears while the mechanic is on it" );
	}

	[TestMethod]
	public void ARideWithAPurchasedUpgradeIsNotUpdatedUntilItIsInstalled()
	{
		var park = EconomyTestData.Park();
		var rides = new EconomyTestData.RideOperations();
		park.RideOperations = rides;
		park.TryBuild( 1100, out var ride );
		ride!.PendingLevel = 1;
		rides.Set( ride.Id, new RideOperation( true, 5, 50 ) );
		EconomyTestData.AdvanceToTurn( park, 2 * ParkEconomy.WearInterval );
		Assert.AreEqual( 100, ride.Repair, "state 2 clears the open word, so there is no wear" );
		Assert.AreEqual( RideObjectStates.Upgrading, ParkEconomy.ObjectState( ride ) );
		ride.PendingLevel = 0;
		ride.IsBrokenDown = true;
		Assert.AreEqual( RideObjectStates.BrokenDown, ParkEconomy.ObjectState( ride ) );
		ride.PendingLevel = 1;
		Assert.AreEqual( RideObjectStates.Upgrading, ParkEconomy.ObjectState( ride ), "a requested upgrade is stored over the breakdown (0x4E0086)" );
		ride.PendingLevel = 0;
		ride.LifeGauge = 0.4;
		ride.StateWord.Set( RideObjectStates.Condemned );
		Assert.AreEqual( RideObjectStates.Condemned, ParkEconomy.ObjectState( ride ), "the ride update enters state 4 for a life gauge byte of 0" );
	}

	[TestMethod]
	public void StatesTwoAndThreeAreStoredNotDerived()
	{
		// [BIN:TPW-EXE:0x004DB4FB object state] [BIN:TPW-EXE:0x004E0086 upgrade request] the constructor stores 3 for a type that is not choosable and the request stores 2 over any state
		var fixedItem = new ParkObjectState { Id = 1, InfoId = 1, Kind = ParkObjectKind.FixedItem, StoredState = RideObjectStates.NotAnAttraction };
		Assert.AreEqual( RideObjectStates.NotAnAttraction, ParkEconomy.ObjectState( fixedItem ) );
		fixedItem.PendingLevel = 1;
		Assert.AreEqual( RideObjectStates.NotAnAttraction, ParkEconomy.ObjectState( fixedItem ), "a request never turns a fixed item's 3 into 2" );
		var ride = new ParkObjectState { Id = 1, InfoId = 1, Kind = ParkObjectKind.Ride };
		ride.IsBrokenDown = true;
		Assert.AreEqual( RideObjectStates.BrokenDown, ParkEconomy.ObjectState( ride ) );
		ride.StoredState = RideObjectStates.Upgrading;
		Assert.AreEqual( RideObjectStates.Upgrading, ParkEconomy.ObjectState( ride ), "the stored 2 overwrites the breakdown" );
		ride.StoredState = RideObjectStates.Idle;
		Assert.AreEqual( RideObjectStates.Idle, ParkEconomy.ObjectState( ride ), "SetState(2) wrote the one word, so SetState(0) leaves state 0: the ride update finds a breakdown again" );
	}
}
