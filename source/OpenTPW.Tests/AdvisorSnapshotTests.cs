using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The advisor controller's own-format snapshot: a restored controller continues the same pending advice,
/// history and action reservation (the original wrapper's span) instead of re-raising them, the automatic
/// advisor's clock continues from its saved milliseconds, and invalid or uncapturable state is rejected
/// explicitly.
/// </summary>
[TestClass]
public class AdvisorSnapshotTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	// Synthetic settings in the Advisor.sam layout (values chosen for the tests, not copied from the original).
	private const string Settings = """
		GeneralAdvisor.MinTimeAnyMessage	7
		GeneralAdvisor.MinTimeSameMessage	 99		free text after the value
		GeneralAdvisor.MinScoreForConsideration	50
		MessageGroups[0].MinTimeSameMessage		10
		MessageGroups[0].SayOnlyOnce			0
		MessageGroups[0].DiscardAfterSlaps		0
		MessageGroups[1].MinTimeSameMessage		0
		MessageGroups[1].SayOnlyOnce			1
		MessageGroups[1].DiscardAfterSlaps		2
		Welcome.Score 9000
		Bankrupted.Score	8000
		ParkNowOpen.Score	50
		ParkNowClosed.Score	51
		PrebuiltPark.Score	700
		""";

	internal static AdvisorBalance Balance() => AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Settings ), "test.sam" );

	[TestMethod]
	public void RestoredControllerContinuesPendingAdviceHistoryAndReservation()
	{
		var controller = new AdvisorController( Balance() );
		controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, liveGameTick: 100, gameType: 0, tutorialEnabled: true );
		controller.HandleGameEvent( AdvisorGameEvent.ParkNowClosed, liveGameTick: 200, gameType: 0, tutorialEnabled: true );
		var played = new List<int>();
		controller.Update( () => 1000u, () => 250u, response => { played.Add( response ); return 4000u; } );
		Assert.AreEqual( 1, played.Count, "the welcome advice plays first" );

		var snapshot = controller.CaptureSnapshot();
		Assert.AreEqual( 1, snapshot.Queue.Slots.Count( item => item != null ), "the closed-park advice is still pending" );
		Assert.AreEqual( 1000u, snapshot.Queue.LastActionStarted );
		Assert.AreEqual( 4000u + 1000u, snapshot.Queue.LastActionDuration );

		// A fresh controller has re-raised its own level-start advice; the restore replaces it wholesale.
		var restored = new AdvisorController( Balance() );
		restored.HandleGameEvent( AdvisorGameEvent.LevelStarted, liveGameTick: 250, gameType: 0, tutorialEnabled: true );
		restored.RestoreSnapshot( snapshot );
		Assert.IsTrue( restored.Queue.GetHistory( 0 ).HasBeenPlayed, "the saved played-history replaces the fresh controller's" );
		var resumed = restored.CaptureSnapshot();
		var original = controller.CaptureSnapshot();
		Assert.AreEqual( original.Queue.Slots.Count( item => item != null ), resumed.Queue.Slots.Count( item => item != null ) );
		CollectionAssert.AreEqual( original.Queue.Slots.Select( item => item?.MessageId ).ToArray(),
			resumed.Queue.Slots.Select( item => item?.MessageId ).ToArray(), "the pending slots resume in place" );
		Assert.AreEqual( original.Queue.LastActionStarted, resumed.Queue.LastActionStarted );
		Assert.AreEqual( original.Queue.LastActionDuration, resumed.Queue.LastActionDuration );
		Assert.AreEqual( original.Queue.History[0].HasBeenPlayed, resumed.Queue.History[0].HasBeenPlayed );

		// Both controllers now see the same clock and pick the same next advice at the same moment.
		uint clock = 1000;
		for ( var step = 0; step < 8; step++ )
		{
			clock += 700;
			Assert.AreEqual( Pick( controller, clock ), Pick( restored, clock ), $"step {step}" );
		}
	}

	private static int Pick( AdvisorController controller, uint clock ) =>
		controller.Update( () => clock, () => 300u, _ => 500u ) is { } selection ? selection.ResponseId : -1;

	[TestMethod]
	public void ControllerSnapshotsRejectInvalidStateWithoutMutating()
	{
		var controller = new AdvisorController( Balance() );
		controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, liveGameTick: 0, gameType: 0, tutorialEnabled: true );
		var good = controller.CaptureSnapshot();
		foreach ( var bad in new[]
		{
			good with { Queue = new AdvisorQueueSnapshot(
				good.Queue.Slots.Take( AdvisorScoreQueue.Capacity - 1 ).ToArray(), good.Queue.History, 0, 0 ) },
			good with { Queue = new AdvisorQueueSnapshot(
				null!, good.Queue.History, 0, 0 ) },
			good with { Queue = new AdvisorQueueSnapshot(
				Replace( good.Queue.Slots.ToArray(), 0, new AdvisorPendingAdviceSnapshot( 42, 100, -1, false ) ), good.Queue.History, 0, 0 ) },
			good with { Queue = new AdvisorQueueSnapshot(
				good.Queue.Slots, good.Queue.History.Concat( new[] { KeyValuePair.Create( 42, new AdvisorHistorySnapshot( 0, -1, false, 0 ) ) } ).ToDictionary(), 0, 0 ) },
			good with { Queue = new AdvisorQueueSnapshot(
				good.Queue.Slots, new Dictionary<int, AdvisorHistorySnapshot> { [0] = null! }, 0, 0 ) },
			good with { Queue = new AdvisorQueueSnapshot(
				Replace( good.Queue.Slots.ToArray(), 0, new AdvisorPendingAdviceSnapshot( 0, 100, -2, false ) ), good.Queue.History, 0, 0 ) }
		} )
		{
			var fresh = new AdvisorController( Balance() );
			fresh.HandleGameEvent( AdvisorGameEvent.LevelStarted, liveGameTick: 0, gameType: 0, tutorialEnabled: true );
			Assert.ThrowsException<InvalidDataException>( () => fresh.ValidateSnapshot( bad ) );
			Assert.ThrowsException<InvalidDataException>( () => fresh.RestoreSnapshot( bad ) );
			Assert.AreEqual( 1, fresh.Queue.Count, "a rejected restore does not touch the fresh controller" );
		}
		Assert.ThrowsException<InvalidDataException>( () => controller.ValidateSnapshot(
			good with { Queue = new AdvisorQueueSnapshot(
				good.Queue.Slots, new Dictionary<int, AdvisorHistorySnapshot> { [0] = new( 0, -2, false, 0 ) }, 0, 0 ) } ),
			"a stored variant below none is rejected" );
		Assert.ThrowsException<ArgumentNullException>( () => controller.ValidateSnapshot( null! ) );
	}

	private static T[] Replace<T>( T[] items, int index, T value )
	{
		items[index] = value;
		return items;
	}

	[TestMethod]
	public void CapturingWhileAPlaybackAttemptIsOutstandingFailsExplicitly()
	{
		var controller = new AdvisorController( Balance() );
		controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, liveGameTick: 0, gameType: 0, tutorialEnabled: true );
		var selected = controller.Queue.SelectNext( 0 );
		Assert.IsNotNull( selected );
		var selection = selected.Value;
		controller.Queue.BeginPlaybackAttempt( selection );
		Assert.IsTrue( controller.Queue.IsAttemptOutstanding );
		Assert.ThrowsException<InvalidOperationException>( () => controller.CaptureSnapshot() );
		controller.Queue.CompletePlaybackAttempt( selection, playbackSucceeded: true, 10u, 20u, 3000u );
		var snapshot = controller.CaptureSnapshot();
		Assert.AreEqual( 10u, snapshot.Queue.LastActionStarted, "capture works once the attempt completes" );
	}

	[TestMethod]
	public void AutomaticAdvisorClockContinuesFromItsSavedMilliseconds()
	{
		long milliseconds = 400_000;
		var advisor = new AutomaticAdvisor( new AdvisorController( Balance() ), () => milliseconds );
		Assert.AreEqual( 400_000u, advisor.CaptureSnapshot().AdvisorClockMilliseconds );
		var snapshot = advisor.CaptureSnapshot();

		long restoredMilliseconds = 0;
		var restored = new AutomaticAdvisor( new AdvisorController( Balance() ), () => restoredMilliseconds );
		Assert.AreEqual( 0u, restored.CaptureSnapshot().AdvisorClockMilliseconds, "a fresh advisor's clock starts at 0" );
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( 400_000u, restored.CaptureSnapshot().AdvisorClockMilliseconds );
		restoredMilliseconds = 50;
		Assert.AreEqual( 400_050u, restored.CaptureSnapshot().AdvisorClockMilliseconds, "the restored clock keeps running from the saved value" );

		// The original's 32-bit milliseconds wrap; the continuation wraps with them.
		long wrappingMilliseconds = 0;
		var wrapping = new AutomaticAdvisor( new AdvisorController( Balance() ), () => wrappingMilliseconds );
		wrapping.RestoreSnapshot( snapshot with { AdvisorClockMilliseconds = uint.MaxValue - 40 } );
		wrappingMilliseconds = 100;
		Assert.AreEqual( 59u, wrapping.CaptureSnapshot().AdvisorClockMilliseconds, "the clock wraps as an unchecked 32-bit value" );
	}

	[TestMethod]
	public void RestoredClockKeepsTheSavedValueWhilePausedAndWrapsOnResume()
	{
		long milliseconds = 3_000;
		var destination = new AutomaticAdvisor( new AdvisorController( Balance() ), () => milliseconds );
		destination.Update( gamePaused: true ); // freezes the destination clock at its own 3000
		var saved = new AutomaticAdvisor( new AdvisorController( Balance() ), () => 900_000L ).CaptureSnapshot()
			with { AdvisorClockMilliseconds = uint.MaxValue - 40 };

		destination.RestoreSnapshot( saved );
		Assert.AreEqual( saved.AdvisorClockMilliseconds, destination.CaptureSnapshot().AdvisorClockMilliseconds,
			"the saved value replaces the frozen destination value" );
		milliseconds = 3_500;
		Assert.AreEqual( saved.AdvisorClockMilliseconds, destination.CaptureSnapshot().AdvisorClockMilliseconds,
			"still paused: the restored clock stands still" );
		destination.Update( gamePaused: false );
		Assert.AreEqual( saved.AdvisorClockMilliseconds, destination.CaptureSnapshot().AdvisorClockMilliseconds,
			"the paused time before the resume does not count" );
		milliseconds = 3_623;
		Assert.AreEqual( unchecked(saved.AdvisorClockMilliseconds + 123u), destination.CaptureSnapshot().AdvisorClockMilliseconds,
			"the resumed clock continues and wraps as an unchecked 32-bit value" );
	}

	[TestMethod]
	public void ActiveSpeechIsRefusedInsteadOfFlagged()
	{
		// The presentation's speech has no snapshot seam, so the tests substitute a controllable speech state.
		var speaking = new AutomaticAdvisor( new AdvisorController( Balance() ), () => 0L, speechActive: () => true );
		Assert.IsTrue( speaking.IsSpeechActive );
		Assert.ThrowsException<InvalidOperationException>( () => speaking.CaptureSnapshot(),
			"a save is refused while a response is being spoken, not flagged and silently silenced" );

		var idle = new AutomaticAdvisor( new AdvisorController( Balance() ), () => 0L, speechActive: () => false );
		Assert.IsFalse( idle.IsSpeechActive );
		var snapshot = idle.CaptureSnapshot();
		Assert.IsFalse( snapshot.SpeechWasActive );
		var restored = new AutomaticAdvisor( new AdvisorController( Balance() ), () => 0L );
		restored.RestoreSnapshot( snapshot );
		Assert.IsFalse( restored.IsSpeechActive );
	}
}
