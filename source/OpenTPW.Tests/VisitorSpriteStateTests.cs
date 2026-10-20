using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Injected-clock body program and wrapper rules. Synthetic only: no game assets, original execution or graphics device.</summary>
[TestClass]
public class VisitorSpriteStateTests
{
	[TestMethod]
	public void ANewPlayerIsInvisibleUntilStrictlyAfterItsFirstDueTimestamp()
	{
		var player = new VisitorSpritePlayer( VisitorSpriteTemplate.Run, 1000 );
		Assert.AreEqual( 62, player.Interval );
		Assert.AreEqual( 1062, player.NextDue );
		Assert.AreEqual( 0, player.NextFrame );
		Assert.IsNull( player.Output );
		Assert.IsFalse( player.Tick( 1061 ) );
		Assert.IsFalse( player.Tick( 1062 ) );
		Assert.AreEqual( 1062, player.NextDue );
		Assert.IsTrue( player.Tick( 1063 ) );
		Assert.AreEqual( new VisitorSpriteFrame( 2, 0 ), player.Output!.Value );
		Assert.AreEqual( 1, player.NextFrame );
		Assert.AreEqual( 1125, player.NextDue );
		Assert.IsFalse( player.Tick( 1125 ) );
		Assert.IsTrue( player.Tick( 1126 ) );
		Assert.AreEqual( new VisitorSpriteFrame( 2, 1 ), player.Output!.Value );
		Assert.AreEqual( 1188, player.NextDue );
	}

	[TestMethod]
	public void ALateSampleEmitsOnceAndRebasesDueWithoutCatchingUp()
	{
		var player = new VisitorSpritePlayer( VisitorSpriteTemplate.Run, 1000 );
		player.Tick( 1063 );
		player.Tick( 1126 );
		Assert.IsTrue( player.Tick( 5000 ) );
		Assert.AreEqual( new VisitorSpriteFrame( 2, 2 ), player.Output!.Value );
		Assert.AreEqual( 5062, player.NextDue );
		var state = player.CaptureSnapshot();
		Assert.IsFalse( player.Tick( 5000 ) );
		Assert.AreEqual( state, player.CaptureSnapshot() );
	}

	[TestMethod]
	public void RunEmitsAllEightFramesAndLoopsWithoutABlankStep()
	{
		var player = new VisitorSpritePlayer( VisitorSpriteTemplate.Run, 0 );
		var frames = new List<int>();
		for ( var emission = 1; emission <= 9; emission++ )
		{
			Assert.IsTrue( player.Tick( emission * 63 ) );
			frames.Add( player.Output!.Value.Frame );
		}
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5, 6, 7, 0 }, frames );
	}

	[TestMethod]
	public void SamplesExactlyOneDefaultIntervalApartSkipEquality()
	{
		var player = new VisitorSpritePlayer( VisitorSpriteTemplate.Run, 0 );
		Assert.IsFalse( player.Tick( 62 ) );
		Assert.IsTrue( player.Tick( 124 ) );
		Assert.IsFalse( player.Tick( 186 ) );
		Assert.IsTrue( player.Tick( 248 ) );
		Assert.AreEqual( 1, player.Output!.Value.Frame );
	}

	[TestMethod]
	public void PendingAndAppliedTemplatesBothRetainLastOutputUntilTheNextEmission()
	{
		var state = new VisitorSpriteState();
		var player = state.CreatePlayer( VisitorSpriteTemplate.Run, 0 );
		for ( var emission = 1; emission <= 5; emission++ )
			state.Tick( emission * 63 );
		var before = player.CaptureSnapshot();
		state.RequestBody( VisitorSpriteTemplate.Stand, 320 );
		Assert.AreEqual( before, player.CaptureSnapshot() );
		Assert.AreEqual( VisitorSpriteTemplate.Stand, state.PendingTemplate!.Value );
		state.ApplyPending();
		Assert.IsNull( state.PendingTemplate );
		Assert.AreEqual( VisitorSpriteTemplate.Stand, player.ActiveTemplate );
		Assert.AreEqual( 0, player.NextFrame );
		Assert.AreEqual( before.NextDue, player.NextDue );
		Assert.AreEqual( before.Interval, player.Interval );
		Assert.AreEqual( new VisitorSpriteFrame( 2, 4 ), player.Output!.Value );
		Assert.IsFalse( state.Tick( before.NextDue ) );
		Assert.IsTrue( state.Tick( before.NextDue + 1 ) );
		Assert.AreEqual( new VisitorSpriteFrame( 0, 0 ), player.Output!.Value );
		state.Tick( player.NextDue + 1 );
		Assert.AreEqual( new VisitorSpriteFrame( 0, 0 ), player.Output!.Value );
		state.RequestBody( VisitorSpriteTemplate.Run, 9999 );
		state.ApplyPending();
		Assert.AreEqual( new VisitorSpriteFrame( 0, 0 ), player.Output!.Value );
		state.Tick( player.NextDue + 1 );
		Assert.AreEqual( new VisitorSpriteFrame( 2, 0 ), player.Output!.Value );
	}

	[TestMethod]
	public void ActiveBodyMatchDoesNotCancelADifferentPendingRequest()
	{
		var state = new VisitorSpriteState();
		state.CreatePlayer( VisitorSpriteTemplate.Run, 0 );
		state.RequestBody( VisitorSpriteTemplate.Stand, 1 );
		state.RequestBody( VisitorSpriteTemplate.Run, 2 );
		Assert.AreEqual( VisitorSpriteTemplate.Stand, state.PendingTemplate!.Value );
		state.ApplyPending();
		Assert.AreEqual( VisitorSpriteTemplate.Stand, state.Player!.ActiveTemplate );
	}

	[TestMethod]
	public void MatchingActiveOrPendingBodyRequestsNeverRestartThePlayer()
	{
		var state = new VisitorSpriteState();
		state.CreatePlayer( VisitorSpriteTemplate.Run, 0 );
		state.Tick( 63 );
		var before = state.CaptureSnapshot();
		state.RequestBody( VisitorSpriteTemplate.Run, 64 );
		Assert.AreEqual( before, state.CaptureSnapshot() );
		state.RequestBody( VisitorSpriteTemplate.Stand, 65 );
		var queued = state.CaptureSnapshot();
		state.RequestBody( VisitorSpriteTemplate.Stand, 66 );
		Assert.AreEqual( queued, state.CaptureSnapshot() );
	}

	[TestMethod]
	public void DirectStateEntryCanRestartTheActiveProgramWhileWalkTestsActiveOnly()
	{
		var state = new VisitorSpriteState();
		state.CreatePlayer( VisitorSpriteTemplate.Run, 0 );
		state.Tick( 63 );
		state.QueueTemplate( VisitorSpriteTemplate.Stand );
		state.RequestWalkTemplate( VisitorSpriteTemplate.Run );
		Assert.AreEqual( VisitorSpriteTemplate.Stand, state.PendingTemplate!.Value );
		state.QueueTemplate( VisitorSpriteTemplate.Run );
		state.ApplyPending();
		Assert.AreEqual( 0, state.Player!.NextFrame, "direct state entry restarts even when active already matches" );
		state.RequestWalkTemplate( VisitorSpriteTemplate.Walk );
		Assert.AreEqual( VisitorSpriteTemplate.Walk, state.PendingTemplate!.Value );
		state.ApplyPending();
		state.Tick( state.Player.NextDue + 1 );
		Assert.AreEqual( new VisitorSpriteFrame( 1, 0 ), state.Player.Output!.Value );
	}

	[TestMethod]
	public void IntervalApplicationPreservesOldDueAndZeroIsNoChange()
	{
		var state = new VisitorSpriteState();
		state.CreatePlayer( VisitorSpriteTemplate.Walk, 1000 );
		state.QueueInterval( 137 );
		Assert.AreEqual( 62, state.Player!.Interval );
		state.ApplyPending();
		Assert.AreEqual( 137, state.Player.Interval );
		Assert.AreEqual( 1062, state.Player.NextDue );
		state.QueueInterval( 0 );
		state.ApplyPending();
		Assert.AreEqual( 137, state.Player.Interval );
		state.RequestBody( VisitorSpriteTemplate.Run, 9999 );
		state.ApplyPending();
		Assert.AreEqual( 1062, state.Player.NextDue );
		state.Tick( 1063 );
		Assert.AreEqual( 1200, state.Player.NextDue );
		Assert.AreEqual( 137, state.Player.Interval, "a body-pose request cannot replace pre-boarding walk history" );
	}

	[TestMethod]
	public void DeletionPreservesWrapperRequestsButRecreationStartsANewInvisiblePlayer()
	{
		var state = new VisitorSpriteState();
		state.CreatePlayer( VisitorSpriteTemplate.Walk, 0 );
		state.QueueTemplate( VisitorSpriteTemplate.Stand );
		state.QueueInterval( 137 );
		state.DeletePlayer();
		Assert.IsNull( state.Player );
		Assert.AreEqual( VisitorSpriteTemplate.Stand, state.PendingTemplate!.Value );
		Assert.AreEqual( 137, state.PendingInterval );
		state.RequestBody( VisitorSpriteTemplate.Run, 2000 );
		Assert.AreEqual( VisitorSpriteTemplate.Run, state.Player!.ActiveTemplate );
		Assert.AreEqual( 2062, state.Player.NextDue );
		Assert.AreEqual( 62, state.Player.Interval );
		Assert.IsNull( state.Player.Output );
		state.ApplyPending();
		Assert.AreEqual( VisitorSpriteTemplate.Stand, state.Player.ActiveTemplate );
		Assert.AreEqual( 137, state.Player.Interval );
		Assert.AreEqual( 2062, state.Player.NextDue );
	}

	[TestMethod]
	public void BeginUpdateWithNoPlayerStillClearsPendingRequests()
	{
		var state = new VisitorSpriteState();
		state.QueueTemplate( VisitorSpriteTemplate.Stand );
		state.QueueInterval( 137 );
		state.ApplyPending();
		Assert.AreEqual( new VisitorSpriteStateSnapshot( null, null, 0 ), state.CaptureSnapshot() );
		state.CreatePlayer( VisitorSpriteTemplate.Run, 2000 );
		Assert.AreEqual( 62, state.Player!.Interval );
	}

	[TestMethod]
	public void NewPersonResetCannotInheritARecycledPersonsPendingWords()
	{
		var state = new VisitorSpriteState();
		state.CreatePlayer( VisitorSpriteTemplate.Run, 0 );
		state.Tick( 63 );
		state.QueueTemplate( VisitorSpriteTemplate.Stand );
		state.QueueInterval( 137 );
		state.ResetForNewPerson();
		Assert.AreEqual( new VisitorSpriteStateSnapshot( null, null, 0 ), state.CaptureSnapshot() );
		state.CreatePlayer( VisitorSpriteTemplate.Walk, 3000 );
		Assert.AreEqual( 3062, state.Player!.NextDue );
		Assert.IsNull( state.Player.Output );
	}

	[TestMethod]
	public void SameBodyRequestTimeDoesNotPhaseLockDifferentPriorHistories()
	{
		var first = new VisitorSpriteState();
		var second = new VisitorSpriteState();
		first.CreatePlayer( VisitorSpriteTemplate.Run, 0 );
		second.CreatePlayer( VisitorSpriteTemplate.Run, 100 );
		first.Tick( 63 );
		first.Tick( 126 );
		first.Tick( 189 );
		second.Tick( 189 );
		first.RequestBody( VisitorSpriteTemplate.Run, 200 );
		second.RequestBody( VisitorSpriteTemplate.Run, 200 );
		Assert.AreEqual( 2, first.Player!.Output!.Value.Frame );
		Assert.AreEqual( 0, second.Player!.Output!.Value.Frame );
	}

	[TestMethod]
	public void ReadingOutputAnyNumberOfTimesDoesNotAdvanceThePlayer()
	{
		var state = new VisitorSpriteState();
		state.CreatePlayer( VisitorSpriteTemplate.Run, 0 );
		var before = state.CaptureSnapshot();
		for ( var render = 0; render < 10; render++ )
			Assert.IsNull( state.Player!.Output );
		Assert.AreEqual( before, state.CaptureSnapshot() );
		state.Tick( 10000 );
		Assert.AreEqual( new VisitorSpriteFrame( 2, 0 ), state.Player!.Output!.Value );
		Assert.AreEqual( 10062, state.Player.NextDue );
	}

	[TestMethod]
	public void SchedulerBeforeGuestBeginUpdateCannotRetroactivelyReplaceTheEmittedFrame()
	{
		var state = new VisitorSpriteState();
		state.CreatePlayer( VisitorSpriteTemplate.Run, 0 );
		state.Tick( 63 );
		state.RequestBody( VisitorSpriteTemplate.Stand, 100 );
		state.Tick( 248 );
		state.ApplyPending();
		Assert.AreEqual( VisitorSpriteTemplate.Stand, state.Player!.ActiveTemplate );
		Assert.AreEqual( new VisitorSpriteFrame( 2, 1 ), state.Player.Output!.Value );
		state.Tick( 311 );
		Assert.AreEqual( new VisitorSpriteFrame( 0, 0 ), state.Player.Output!.Value );
	}

	[TestMethod]
	public void SignedComparisonAndWrappedAdditionRemainLiteralAtInt32Boundary()
	{
		var player = new VisitorSpritePlayer( VisitorSpriteTemplate.Run, int.MaxValue - 31 );
		Assert.AreEqual( int.MinValue + 30, player.NextDue );
		Assert.IsTrue( player.Tick( int.MaxValue - 30 ), "signed positive now is greater than the wrapped negative due" );
		Assert.AreEqual( int.MinValue + 31, player.NextDue );
		Assert.IsFalse( player.Tick( int.MinValue + 31 ) );
		Assert.IsTrue( player.Tick( int.MinValue + 32 ) );
		var unwrapped = new VisitorSpritePlayer( VisitorSpriteTemplate.Run, int.MaxValue - 100 );
		Assert.IsFalse( unwrapped.Tick( int.MinValue ), "a negative now is not greater than an old positive due; no modular-distance substitution" );
	}

	[TestMethod]
	public void SnapshotRoundTripKeepsCursorOutputPendingAndTheExactClockEpoch()
	{
		var state = new VisitorSpriteState();
		state.CreatePlayer( VisitorSpriteTemplate.Run, 1000 );
		state.Tick( 1063 );
		state.QueueTemplate( VisitorSpriteTemplate.Stand );
		state.QueueInterval( 137 );
		var snapshot = state.CaptureSnapshot();
		var saved = JsonSerializer.Deserialize<VisitorSpriteStateSnapshot>( JsonSerializer.Serialize( snapshot ) )!;
		var restored = new VisitorSpriteState();
		restored.RestoreSnapshot( saved );
		Assert.AreEqual( snapshot, restored.CaptureSnapshot() );
		state.ApplyPending();
		restored.ApplyPending();
		foreach ( var now in new[] { 1125, 1126, 1263, 1264, 5000 } )
		{
			Assert.AreEqual( state.Tick( now ), restored.Tick( now ) );
			Assert.AreEqual( state.CaptureSnapshot(), restored.CaptureSnapshot() );
		}
	}

	[TestMethod]
	public void ATransitionSnapshotAllowsOldOutputFromADifferentActiveProgram()
	{
		var player = new VisitorSpritePlayer( VisitorSpriteTemplate.Run, 0 );
		player.Tick( 63 );
		player.ApplyTemplate( VisitorSpriteTemplate.Stand );
		var snapshot = player.CaptureSnapshot();
		Assert.AreEqual( snapshot, VisitorSpritePlayer.RestoreSnapshot( snapshot ).CaptureSnapshot() );
		var invisible = new VisitorSpritePlayer( VisitorSpriteTemplate.Walk, 100 );
		Assert.IsNull( VisitorSpritePlayer.RestoreSnapshot( invisible.CaptureSnapshot() ).Output );
	}

	[TestMethod]
	public void InvalidSnapshotDoesNotPartiallyReplaceTheWrapper()
	{
		var state = new VisitorSpriteState();
		state.CreatePlayer( VisitorSpriteTemplate.Run, 0 );
		var before = state.CaptureSnapshot();
		var invalid = before with { PendingInterval = 251 };
		Assert.ThrowsException<InvalidDataException>( () => state.RestoreSnapshot( invalid ) );
		Assert.AreEqual( before, state.CaptureSnapshot() );
		foreach ( var player in new[]
		{
			before.Player! with { ActiveTemplate = (VisitorSpriteTemplate)9 },
			before.Player! with { CurrentProgram = (VisitorSpriteTemplate)0 },
			before.Player! with { CurrentProgram = VisitorSpriteTemplate.Stand },
			before.Player! with { NextFrame = -1 },
			before.Player! with { NextFrame = 8 },
			before.Player! with { NextFrame = 1 },
			before.Player! with { Interval = 0 },
			before.Player! with { Output = new VisitorSpriteFrame( 2, 8 ) },
			before.Player! with { Output = new VisitorSpriteFrame( 0, 1 ) }
		} )
			Assert.ThrowsException<InvalidDataException>( () => state.RestoreSnapshot( before with { Player = player } ) );
		Assert.AreEqual( before, state.CaptureSnapshot() );
	}

	[TestMethod]
	public void UnsupportedProgramsAndAccidentalLivePlayerReplacementAreRejected()
	{
		var state = new VisitorSpriteState();
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => state.CreatePlayer( (VisitorSpriteTemplate)9, 0 ) );
		Assert.IsNull( state.Player );
		state.CreatePlayer( VisitorSpriteTemplate.Run, 0 );
		Assert.ThrowsException<InvalidOperationException>( () => state.CreatePlayer( VisitorSpriteTemplate.Run, 100 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => state.QueueInterval( -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => state.QueueTemplate( (VisitorSpriteTemplate)0 ) );
	}

	[TestMethod]
	public void ADeletedPlayerSnapshotRetainsRequestsUntilARealGuestUpdate()
	{
		var state = new VisitorSpriteState();
		state.QueueTemplate( VisitorSpriteTemplate.Run );
		state.QueueInterval( 137 );
		var restored = new VisitorSpriteState();
		restored.RestoreSnapshot( state.CaptureSnapshot() );
		Assert.IsNull( restored.Player );
		Assert.AreEqual( VisitorSpriteTemplate.Run, restored.PendingTemplate!.Value );
		Assert.AreEqual( 137, restored.PendingInterval );
		Assert.IsFalse( restored.Tick( 5000 ) );
		Assert.AreEqual( state.CaptureSnapshot(), restored.CaptureSnapshot(), "scheduler opportunities do not apply person-side requests" );
		restored.ApplyPending();
		Assert.AreEqual( new VisitorSpriteStateSnapshot( null, null, 0 ), restored.CaptureSnapshot() );
	}

	[TestMethod]
	public void FiniteProgramsJumpToStandWithoutChangingTheirActiveIdentity()
	{
		foreach ( var (template, slot) in new[] { (VisitorSpriteTemplate.Cheer, 12), (VisitorSpriteTemplate.Program6, 11), (VisitorSpriteTemplate.Vomit, 6), (VisitorSpriteTemplate.Program8, 4) } )
		{
			var state = new VisitorSpriteState();
			state.CreatePlayer( template, 0 );
			for ( var step = 0; step < 4; step++ )
			{
				state.Tick( (step + 1) * 63 );
				Assert.AreEqual( new VisitorSpriteFrame( slot, step ), state.Player!.Output!.Value );
			}
			Assert.AreEqual( VisitorSpriteTemplate.Stand, state.Player!.CurrentProgram );
			Assert.AreEqual( new VisitorSpriteFrame( slot, 3 ), VisitorSpritePlayer.RestoreSnapshot( state.Player.CaptureSnapshot() ).Output!.Value, "normalized tail cursor accepts the finite program's previous output" );
			state.Tick( 315 );
			Assert.AreEqual( new VisitorSpriteFrame( 0, 0 ), state.Player!.Output!.Value );
			Assert.AreEqual( template, state.Player.ActiveTemplate );
			Assert.AreEqual( VisitorSpriteTemplate.Stand, state.Player.CurrentProgram );
			var snapshot = state.CaptureSnapshot();
			state.RequestBody( template, 316 );
			Assert.AreEqual( snapshot, state.CaptureSnapshot(), "active matching does not replay a finished prefix" );
			var restored = new VisitorSpriteState();
			restored.RestoreSnapshot( snapshot );
			restored.Tick( 378 );
			Assert.AreEqual( new VisitorSpriteFrame( 0, 0 ), restored.Player!.Output!.Value );
		}
	}

	[TestMethod]
	public void IndecisionEmitsItsSixteenFramePrefixThenStands()
	{
		var player = new VisitorSpritePlayer( VisitorSpriteTemplate.Indecision, 0 );
		for ( var step = 0; step < 16; step++ )
		{
			player.Tick( (step + 1) * 63 );
			Assert.AreEqual( new VisitorSpriteFrame( 14, step % 4 / 2 ), player.Output!.Value );
		}
		player.Tick( 17 * 63 );
		Assert.AreEqual( new VisitorSpriteFrame( 0, 0 ), player.Output!.Value );
		Assert.AreEqual( VisitorSpriteTemplate.Indecision, player.ActiveTemplate );
	}
}
