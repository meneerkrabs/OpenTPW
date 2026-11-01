using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;

namespace OpenTPW.Tests;

/// <summary>The lobby's speech queue (tp.exe 0x5D5F70, 0x5D5F80, 0x5D6110, 0x5D61A0, 0x5D6060). GPU free.</summary>
[TestClass]
public class LobbyAdvisorQueueTests
{
	private static List<int> Said( LobbyAdvisorQueue queue, uint now, bool busy = false )
	{
		var said = new List<int>();
		queue.Step( now, busy, said.Add );
		return said;
	}

	[TestMethod]
	public void IdsAreSaidInTheOrderTheyWerePushedOneAFrame()
	{
		// [BIN:TPW-EXE:0x5E2A30 list push] pushed at the head, taken from the tail: first in, first out
		var queue = new LobbyAdvisorQueue();
		queue.Push( 390, flush: false ); queue.Push( 391, flush: false );
		CollectionAssert.AreEqual( new[] { 390 }, Said( queue, 1 ) );
		CollectionAssert.AreEqual( new[] { 391 }, Said( queue, 2 ) );
		Assert.AreEqual( 0, Said( queue, 3 ).Count );
	}

	[TestMethod]
	public void AFlushingPushDropsTheWaitingIdsFirst()
	{
		// [BIN:TPW-EXE:0x5D6110 push] a nonzero second argument empties the list before the id is added
		var queue = new LobbyAdvisorQueue();
		queue.Push( 1, false ); queue.Push( 2, false );
		queue.Push( 3, flush: true );
		Assert.AreEqual( 1, queue.Count );
		CollectionAssert.AreEqual( new[] { 3 }, Said( queue, 1 ) );
	}

	[TestMethod]
	public void ARunningResponseKeepsTheQueueWaitingAndMarksTheSpeech()
	{
		// [BIN:TPW-EXE:0x5D6047 busy] a response that plays only sets the spoke word
		var queue = new LobbyAdvisorQueue();
		queue.Push( 7, false );
		Assert.AreEqual( 0, Said( queue, 1, busy: true ).Count );
		Assert.AreEqual( 1, queue.Count );
		CollectionAssert.AreEqual( new[] { 7 }, Said( queue, 2 ) );
	}

	[TestMethod]
	public void TheIdleLineComesAfterTheDelayOfSilenceAndOnlyOnceTheIdleLineIsOn()
	{
		// [BIN:TPW-EXE:0x5D6014 silence] an unsigned compare of the time with the last speech plus the delay
		var queue = new LobbyAdvisorQueue();
		Assert.AreEqual( 0, Said( queue, 500000 ).Count, "no idle line before 0x5D61A0 turns it on" );
		queue.SetIdle( LobbyAdvisorQueue.IdleLineEven, LobbyAdvisorQueue.IdleDelayMilliseconds );
		Assert.AreEqual( 90000u, LobbyAdvisorQueue.IdleDelayMilliseconds );
		var early = new LobbyAdvisorQueue(); early.SetIdle( 394, 90000 );
		Assert.AreEqual( 0, Said( early, 90000 ).Count ); Assert.AreEqual( 0, early.Count, "equal is not past the delay" );
		Assert.AreEqual( 0, Said( early, 90001 ).Count ); Assert.AreEqual( 1, early.Count, "the line is queued, not said, in that frame" );
		CollectionAssert.AreEqual( new[] { 394 }, Said( early, 90002 ), "and said by the next step" );
	}

	[TestMethod]
	public void AfterASpeechTheSilenceIsMeasuredFromTheNextStep()
	{
		// [BIN:TPW-EXE:0x5D5FFE spoke] with the idle line on, the step after a speech takes the time as the last speech and returns
		var queue = new LobbyAdvisorQueue(); queue.SetIdle( 395, 1000 );
		queue.Push( 5, false );
		CollectionAssert.AreEqual( new[] { 5 }, Said( queue, 100000 ) );
		Assert.AreEqual( 0, Said( queue, 100010 ).Count ); // the last speech is now 100010
		Assert.AreEqual( 0, Said( queue, 101010 ).Count ); Assert.AreEqual( 0, queue.Count );
		Assert.AreEqual( 0, Said( queue, 101011 ).Count ); Assert.AreEqual( 1, queue.Count );
		CollectionAssert.AreEqual( new[] { 395 }, Said( queue, 101012 ) );
	}

	[TestMethod]
	public void AResponseThatPlaysDuringTheSilenceRestartsIt()
	{
		var queue = new LobbyAdvisorQueue(); queue.SetIdle( 394, 1000 );
		Said( queue, 500 ); Said( queue, 1400, busy: true ); // the advisor speaks something else
		Assert.AreEqual( 0, Said( queue, 1500 ).Count ); // marks the end of that speech as the last one
		Assert.AreEqual( 0, Said( queue, 2400 ).Count ); Assert.AreEqual( 0, queue.Count );
		Said( queue, 2501 ); Assert.AreEqual( 1, queue.Count );
	}

	[TestMethod]
	public void TheTeardownCutsARunningResponseOffAndDropsTheQueue()
	{
		// [BIN:TPW-EXE:0x5D6060 queue teardown] the cut-off path runs only with a response active
		var queue = new LobbyAdvisorQueue(); queue.Push( 1, false ); queue.Push( 2, false );
		var cuts = 0;
		queue.Teardown( busy: true, () => cuts++ );
		Assert.AreEqual( 1, cuts ); Assert.AreEqual( 0, queue.Count );
		queue.Push( 3, false );
		queue.Teardown( busy: false, () => cuts++ );
		Assert.AreEqual( 1, cuts ); Assert.AreEqual( 0, queue.Count );
	}
}
