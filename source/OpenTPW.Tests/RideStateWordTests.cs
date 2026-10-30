using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>ECON-023: the one ride state word (tp.exe +0x19C, <c>SetState</c> 0x4E0E60) the economy record and the visitor bridge share.</summary>
[TestClass]
public class RideStateWordTests
{
	[TestMethod]
	public void TheWordHoldsIdleBrokenAndCondemnedAndRaisesOnlyForASet()
	{
		var word = new RideStateWord();
		var changes = new List<(int Before, int After)>();
		word.Changed += ( before, after ) => changes.Add( (before, after) );
		word.Set( RideObjectStates.BrokenDown );
		word.Set( RideObjectStates.BrokenDown );
		word.Set( RideObjectStates.Condemned );
		word.Restore( RideObjectStates.Idle );
		CollectionAssert.AreEqual( new[] { (0, 1), (1, 4) }, changes, "a repeat and a restore raise nothing" );
		Assert.IsFalse( word.IsBroken );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => word.Set( RideObjectStates.Upgrading ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => word.Restore( RideObjectStates.NotAnAttraction ) );
		Assert.AreEqual( RideObjectStates.Condemned, RideStateWord.BrokenStateFor( 0.5 ), "a life gauge byte of 0 is state 4 (0x4E14E0)" );
		Assert.AreEqual( RideObjectStates.BrokenDown, RideStateWord.BrokenStateFor( 1 ) );
	}

	[TestMethod]
	public void TheBridgeReadsTheWordItSharesWithTheEconomyRecord()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var record );
		var ride = new RideVisitorBridge( 1, "Synthetic", RideVisitorKind.Ride, 5, 55, 30 ) { StateWord = record!.StateWord };
		Assert.AreSame( record.StateWord, ride.StateWord );
		record.IsBrokenDown = true;
		Assert.IsTrue( ride.IsBrokenDown && ride.HoldsQueuedGuests && !ride.IsCondemned );
		record.LifeGauge = 0;
		record.StateWord.Set( RideObjectStates.Condemned );
		Assert.IsTrue( ride.IsCondemned && !ride.HoldsQueuedGuests );
		ride.StateWord.Set( RideObjectStates.Idle );
		Assert.IsFalse( record.IsBrokenDown, "SetState(0) from the bridge's side is the economy's too" );
		Assert.AreEqual( RideObjectStates.Idle, ParkEconomy.ObjectState( record ) );
	}

	[TestMethod]
	public void ALinkMovesABrokenStateTheBridgeHeldToTheSharedWord()
	{
		var ride = new RideVisitorBridge( 1, "Synthetic", RideVisitorKind.Ride, 5, 55, 30 );
		ride.RestoreOriginalBrokenState( isCondemned: true, lifeGauge: 0 );
		var shared = new RideStateWord();
		ride.StateWord = shared;
		Assert.AreEqual( RideObjectStates.Condemned, shared.Value );
		var broken = new RideStateWord();
		broken.Set( RideObjectStates.BrokenDown );
		ride.StateWord = broken;
		Assert.AreEqual( RideObjectStates.BrokenDown, broken.Value, "a word that is broken already is not overwritten" );
	}

	[TestMethod]
	public void ALinkKeepsTheUpgradeRequestOverABreakdownTheBridgeHeld()
	{
		// a save written before the upgrade request cleared a breakdown can hold a ride that is both broken and upgrading
		var ride = new RideVisitorBridge( 1, "Synthetic", RideVisitorKind.Ride, 5, 55, 30 );
		ride.RestoreOriginalBrokenState( isCondemned: false, lifeGauge: 40 );
		var shared = new RideStateWord();
		shared.Store( RideObjectStates.Upgrading );
		ride.StateWord = shared;
		Assert.AreEqual( RideObjectStates.Idle, shared.Value, "the breakdown is not copied back under the upgrade request (0x4E0086)" );
		Assert.AreEqual( RideObjectStates.Upgrading, shared.Current );
	}

	[TestMethod]
	public void TheEntryIntoStateOneIsPublishedOnceToTheEconomy()
	{
		var park = EconomyTestData.Park();
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		park.TryBuild( 1100, out var record );
		record!.ScriptHosted = true;
		record.StateWord.Set( RideObjectStates.BrokenDown );
		Assert.AreEqual( 0, events.Count( item => item.Kind == ParkEventKind.RideEnteredBrokenState ), "the word's change is noted, not raised, until the level publishes it" );
		park.PublishRideState( record );
		park.PublishRideState( record );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.RideEnteredBrokenState ) );
		// state 1 to state 4 is no new entry
		record.StateWord.Set( RideObjectStates.Condemned );
		park.PublishRideState( record );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.RideEnteredBrokenState ) );
		record.StateWord.Set( RideObjectStates.Idle );
		record.StateWord.Set( RideObjectStates.BrokenDown );
		park.PublishRideState( record );
		Assert.AreEqual( 2, events.Count( item => item.Kind == ParkEventKind.RideEnteredBrokenState ), "a new entry after SetState(0)" );
	}

	[TestMethod]
	public void ARestoredStateRaisesNothing()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var record );
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		record!.StateWord.Restore( RideObjectStates.BrokenDown );
		park.PublishRideState( record );
		Assert.AreEqual( 0, events.Count( item => item.Kind == ParkEventKind.RideEnteredBrokenState ) );
	}

	[TestMethod]
	public void AGuestWindowsProjectionCopiesTheWordsValueAndNeverWritesTheRecordsWord()
	{
		// [EXT:online-coop-commands] a guest's window projects a waiting open-state action onto a copy of the record (C4b-2): the copy reads the same state, and nothing written to it
		// reaches the shared word or its listeners
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var record );
		var changes = 0;
		record!.StateWord.Changed += ( _, _ ) => changes++;
		record.IsBrokenDown = true;
		var copy = record.Projection();
		Assert.AreNotSame( record.StateWord, copy.StateWord );
		Assert.AreEqual( (RideObjectStates.BrokenDown, RideObjectStates.BrokenDown), (ParkEconomy.ObjectState( record ), ParkEconomy.ObjectState( copy )) );
		copy.IsBrokenDown = false;
		Assert.AreEqual( (true, false, 1), (record.IsBrokenDown, copy.IsBrokenDown, changes), "the copy's repair leaves the record's word alone and raises nothing" );
	}

	[TestMethod]
	public void TheStoredPartRidesInTheSharedWordAndTheDispatchReadsItWhole()
	{
		var word = new RideStateWord();
		word.Set( RideObjectStates.BrokenDown );
		Assert.AreEqual( RideObjectStates.BrokenDown, word.Current );
		word.Store( RideObjectStates.Upgrading );
		Assert.AreEqual( RideObjectStates.Upgrading, word.Current, "an upgrade request overwrites a breakdown (0x4E0086)" );
		Assert.AreEqual( RideObjectStates.Idle, word.Value, "SetState(2) writes the one word: the breakdown is gone" );
		Assert.IsFalse( word.IsBroken );
		word.Store( RideObjectStates.Idle );
		Assert.AreEqual( RideObjectStates.Idle, word.Current );
		word.Store( RideObjectStates.NotAnAttraction );
		Assert.AreEqual( RideObjectStates.NotAnAttraction, word.Current );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => word.Store( RideObjectStates.Condemned ) );
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var record );
		record!.StoredState = RideObjectStates.Upgrading;
		Assert.AreEqual( RideObjectStates.Upgrading, record.StateWord.Current );
		Assert.AreEqual( RideObjectStates.Upgrading, ParkEconomy.ObjectState( record ) );
		Assert.AreEqual( RideObjectStates.Upgrading, record.Projection().StateWord.Current, "a projection keeps the stored part" );
	}

	[DataTestMethod]
	[DataRow( RideObjectStates.Idle, true )]
	[DataRow( RideObjectStates.Upgrading, false )]
	[DataRow( RideObjectStates.NotAnAttraction, false )]
	public void OnlyStateZeroReadsTheScriptsBrokenVariable( int stored, bool entersBrokenState )
	{
		// tp.exe 0x4E0E00: state 0 runs 0x4E14E0 (VAR_BROKEN, SetState(1) or (4)), states 1, 2 and 4 the head step and the script dismiss, state 3 nothing
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		rig.Ride.StateWord.Store( stored );
		rig.Script[RideVariables.VAR_BROKEN] = 1;
		rig.RunTurns( 40 );
		Assert.AreEqual( entersBrokenState, rig.Ride.IsBrokenDown );
	}

	[TestMethod]
	public void AnUpgradingRideDismissesItsQueueHeadLikeABrokenOne()
	{
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 2 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		var head = QueueTests.Rig.InQueueOrder( guests )[0];
		Assert.IsTrue( head.IsInQueue );
		rig.Ride.StateWord.Store( RideObjectStates.Upgrading );
		var turn = rig.Simulation.ParkTurn;
		while ( head.IsInQueue && rig.Simulation.ParkTurn < turn + 8 )
			rig.Step();
		Assert.IsFalse( head.IsInQueue, "state 2 runs the head step (0x4E0450) every update" );
		Assert.IsFalse( rig.Ride.IsBrokenDown, "and never the breakdown's read of VAR_BROKEN" );
	}

	[TestMethod]
	public void AnUpgradeBoughtOnABrokenRideClearsTheBreakdownTheBridgeAndTheEconomySee()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var record );
		var ride = new RideVisitorBridge( 1, "Synthetic", RideVisitorKind.Ride, 5, 55, 30 ) { StateWord = record!.StateWord };
		record.IsBrokenDown = true;
		Assert.IsTrue( ride.HoldsQueuedGuests && ride.IsBrokenDown );
		record.StoredState = RideObjectStates.Upgrading;   // 0x4E0086: SetState(2) over the breakdown
		Assert.IsFalse( ride.HoldsQueuedGuests, "tp.exe 0x4E0370 tests the word == 1: queued guests walk on during an upgrade" );
		Assert.IsFalse( ride.IsBrokenDown || ride.IsCondemned || record.IsBrokenDown );
		Assert.AreEqual( RideObjectStates.Upgrading, ParkEconomy.ObjectState( record ) );
		record.StoredState = RideObjectStates.Idle;
		Assert.AreEqual( RideObjectStates.Idle, ParkEconomy.ObjectState( record ), "the cancel leaves state 0; the ride update finds a breakdown again" );
	}
}
