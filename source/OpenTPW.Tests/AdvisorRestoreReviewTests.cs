using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public sealed class AdvisorRestoreReviewTests
{
	[TestInitialize]
	public void Initialize() => Log ??= new();

	private static AdvisorController Controller() => new( AdvisorBalance.Parse( Encoding.ASCII.GetBytes( """
		GeneralAdvisor.MinTimeAnyMessage 7
		GeneralAdvisor.MinTimeSameMessage 99
		GeneralAdvisor.MinScoreForConsideration 50
		MessageGroups[0].MinTimeSameMessage 10
		MessageGroups[0].SayOnlyOnce 0
		MessageGroups[0].DiscardAfterSlaps 0
		MessageGroups[1].MinTimeSameMessage 0
		MessageGroups[1].SayOnlyOnce 1
		MessageGroups[1].DiscardAfterSlaps 2
		Welcome.Score 9000
		Bankrupted.Score 8000
		ParkNowOpen.Score 50
		ParkNowClosed.Score 51
		PrebuiltPark.Score 700
		""" ), "resume-review.sam" ) );

	[TestMethod]
	public void RestoreRebasesANonzeroDestinationClockExactly()
	{
		var source = new AutomaticAdvisor( Controller(), () => 20_000L );
		var saved = source.CaptureSnapshot();
		long destinationTime = 1_739;
		var destination = new AutomaticAdvisor( Controller(), () => destinationTime );
		destination.RestoreSnapshot( saved );
		Assert.AreEqual( saved.AdvisorClockMilliseconds, destination.CaptureSnapshot().AdvisorClockMilliseconds,
			"constructing and attaching a new advisor takes time; that time must not shift saved deadlines" );
		destinationTime += 123;
		Assert.AreEqual( saved.AdvisorClockMilliseconds + 123u, destination.CaptureSnapshot().AdvisorClockMilliseconds );
		destination.RestoreSnapshot( saved );
		Assert.AreEqual( saved.AdvisorClockMilliseconds, destination.CaptureSnapshot().AdvisorClockMilliseconds,
			"restoring twice must not retain the destination's previous elapsed time" );
	}

	[TestMethod]
	public void RestoreWhilePausedKeepsTheSavedClockUntilResume()
	{
		var saved = new AutomaticAdvisor( Controller(), () => 20_000L ).CaptureSnapshot();
		long destinationTime = 5_000;
		var destination = new AutomaticAdvisor( Controller(), () => destinationTime );
		destination.Update( gamePaused: true );
		destination.RestoreSnapshot( saved );
		Assert.AreEqual( saved.AdvisorClockMilliseconds, destination.CaptureSnapshot().AdvisorClockMilliseconds );
		destinationTime += 1_000;
		Assert.AreEqual( saved.AdvisorClockMilliseconds, destination.CaptureSnapshot().AdvisorClockMilliseconds,
			"loading under a paused UI must not advance the reservation clock" );
		destination.Update( gamePaused: false );
		destinationTime += 123;
		Assert.AreEqual( saved.AdvisorClockMilliseconds + 123u, destination.CaptureSnapshot().AdvisorClockMilliseconds );
	}

	[TestMethod]
	public void UnsupportedActiveSpeechIsRejectedBeforeQueueOrClockChanges()
	{
		var destination = new AutomaticAdvisor( Controller(), () => 700L );
		var before = destination.CaptureSnapshot();
		var unsupported = before with { AdvisorClockMilliseconds = 20_000, SpeechWasActive = true };
		Assert.ThrowsException<InvalidDataException>( () => destination.ValidateSnapshot( unsupported ),
			"a speech flag does not preserve playback; unsupported state must be rejected explicitly" );
		Assert.ThrowsException<InvalidDataException>( () => destination.RestoreSnapshot( unsupported ) );
		Assert.AreEqual( before.AdvisorClockMilliseconds, destination.CaptureSnapshot().AdvisorClockMilliseconds );
	}
}
