using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's coaster command queue (<see cref="CoasterRequestQueue"/>, docs/COOP.md, "A guest's coaster tool (C3c-2)"), headless: the order, the budget, a drag's steps
/// coalesced per field and paced, a drag's end held until what went before it came back, the retries of a drag's last values and end, and a drag given up.
/// </summary>
[TestClass]
public class CoasterRequestQueueTests
{
	private const int Station = 7;
	private const int Limit = 4;

	/// <summary>The requests the queue sent, in order, each the host's to resolve.</summary>
	private readonly List<CoopGuestCommands.Request> sent = new();
	private uint sequence;

	private ParkCommandResult Issue( ParkCommand command, bool completes )
	{
		var request = new CoopGuestCommands.Request( ++sequence, command, Array.Empty<byte>() ) { State = CoopGuestRequestState.Sent };
		sent.Add( request );
		return ParkCommandResult.Sent( request );
	}

	private int Open() => sent.Count( request => request.IsOpen );

	private void Send( CoasterRequestQueue queue, double now ) => queue.Send( Issue, Open, Limit, now );

	/// <summary>The host applied <paramref name="request"/>, accepted, and the HUD's drain hands it to the queue.</summary>
	private static void Applied( CoasterRequestQueue queue, CoopGuestCommands.Request request )
	{
		request.State = CoopGuestRequestState.Applied;
		request.Result = ParkCommandResult.Ok();
		Assert.IsTrue( queue.Complete( request ) );
	}

	/// <summary>The host took no record of <paramref name="request"/>, for <paramref name="refusal"/>.</summary>
	private static void Refused( CoasterRequestQueue queue, CoopGuestCommands.Request request, CoopGuestRefusal refusal )
	{
		request.State = CoopGuestRequestState.Refused;
		request.Refusal = refusal;
		Assert.IsTrue( queue.Complete( request ) );
	}

	private static EditCoasterNodeCommand Loft( float value ) => new( Station, CoasterNodeField.Loft, value, 0, 0 );

	[TestMethod]
	public void ADragsStepsAreCoalescedPerFieldAndPacedBehindItsPress()
	{
		var queue = new CoasterRequestQueue();
		var drag = queue.NewDrag();
		Assert.AreNotEqual( drag, queue.NewDrag(), "a queue's drags have numbers of their own" );
		queue.Enqueue( new BeginCoasterNodeEditCommand( Station, 3, CoasterDrag.Height ), drag );
		queue.Step( drag, Loft( 0.1f ) );
		queue.Step( drag, Loft( 0.2f ) );
		queue.Step( drag, new EditCoasterNodeCommand( Station, CoasterNodeField.Yaw, 1, 0, 0 ) );
		queue.Step( drag, Loft( 0.3f ) );
		Send( queue, 10 );
		CollectionAssert.AreEqual( new[] { "BeginCoasterNodeEditCommand", "EditCoasterNodeCommand" }, sent.Select( request => request.Command.GetType().Name ).ToArray(),
			"the press, and one step: the next waits 0.2 s" );
		Assert.AreEqual( 0.3f, ((EditCoasterNodeCommand)sent[1].Command).Value, "the loft's latest value replaced the ones before it" );
		Send( queue, 10.1 );
		Assert.AreEqual( 2, sent.Count );
		Send( queue, 10.25 );
		Assert.AreEqual( CoasterNodeField.Yaw, ((EditCoasterNodeCommand)sent[2].Command).Field );
		Assert.IsFalse( queue.HasUnsent );
	}

	[TestMethod]
	public void TheBudgetOfOpenToolRequestsHoldsTheQueue()
	{
		var queue = new CoasterRequestQueue();
		for ( var click = 0; click < Limit + 2; click++ )
			queue.Enqueue( new CoasterClickCommand( Station, click, 0, 40, false, 0 ) );
		Send( queue, 0 );
		Assert.AreEqual( Limit, sent.Count, "four open requests at most" );
		Applied( queue, sent[0] );
		Send( queue, 0 );
		Assert.AreEqual( Limit + 1, sent.Count, "a freed place takes the next, in order" );
		Assert.AreEqual( Limit, ((CoasterClickCommand)sent[^1].Command).X );
	}

	[TestMethod]
	public void ADragsEndWaitsForWhatWentBeforeItAndNothingGoesWhileItIsOut()
	{
		var queue = new CoasterRequestQueue();
		var drag = queue.NewDrag();
		queue.Enqueue( new BeginCoasterNodeEditCommand( Station, 3, CoasterDrag.Height ), drag );
		queue.Step( drag, Loft( 0.4f ) );
		queue.Enqueue( new EndCoasterNodeEditCommand( Station ), drag );
		queue.Enqueue( new EndCoasterToolCommand( Station ) );
		Send( queue, 0 );
		Assert.AreEqual( 2, sent.Count, "the press and the step; the end waits for them" );
		Applied( queue, sent[0] );
		Send( queue, 1 );
		Assert.AreEqual( 2, sent.Count, "the step is still out" );
		Applied( queue, sent[1] );
		Send( queue, 1 );
		Assert.AreEqual( 3, sent.Count );
		Assert.IsInstanceOfType( sent[2].Command, typeof( EndCoasterNodeEditCommand ) );
		Send( queue, 2 );
		Assert.AreEqual( 3, sent.Count, "the tool's end waits while the drag's end is out" );
		Applied( queue, sent[2] );
		Send( queue, 2 );
		Assert.IsInstanceOfType( sent[3].Command, typeof( EndCoasterToolCommand ) );
	}

	[TestMethod]
	public void ADragsLastValueOrEndTheHostFoundTooFastGoesAgainFirstAfterAWhile()
	{
		var queue = new CoasterRequestQueue();
		var drag = queue.NewDrag();
		queue.Step( drag, Loft( 0.1f ) );
		Send( queue, 0 );
		queue.Step( drag, Loft( 0.2f ) );
		Send( queue, 1 );
		queue.Enqueue( new EndCoasterNodeEditCommand( Station ), drag );
		Assert.AreEqual( 2, sent.Count );
		// the first step was too fast, but a later one sets its field: it is not sent again
		Refused( queue, sent[0], CoopGuestRefusal.TooFast );
		Send( queue, 1.5 );
		Assert.AreEqual( 2, sent.Count, "the end waits for the last step" );
		// the last step was too fast: it goes again, half a second later, before the end
		Refused( queue, sent[1], CoopGuestRefusal.TooFast );
		Send( queue, 2 );
		Assert.AreEqual( 2, sent.Count, "not at once: the host's bucket refills meanwhile" );
		Send( queue, 2 + CoasterRequestQueue.RetrySeconds );
		Assert.AreEqual( 3, sent.Count );
		Assert.AreEqual( 0.2f, ((EditCoasterNodeCommand)sent[2].Command).Value );
		// as often as it takes
		for ( var attempt = 0; attempt < CoasterRequestQueue.MaximumRetries + 2; attempt++ )
		{
			Refused( queue, sent[^1], CoopGuestRefusal.TooFast );
			Send( queue, 10 + attempt );
			Send( queue, 10 + attempt + CoasterRequestQueue.RetrySeconds );
			Assert.IsInstanceOfType( sent[^1].Command, typeof( EditCoasterNodeCommand ) );
		}
		Applied( queue, sent[^1] );
		Send( queue, 30 );
		Assert.IsInstanceOfType( sent[^1].Command, typeof( EndCoasterNodeEditCommand ) );
		// the end too
		Refused( queue, sent[^1], CoopGuestRefusal.TooFast );
		Send( queue, 31 );
		Send( queue, 31 + CoasterRequestQueue.RetrySeconds );
		Assert.IsInstanceOfType( sent[^1].Command, typeof( EndCoasterNodeEditCommand ) );
		Assert.AreNotSame( sent[^2], sent[^1], "a new request" );
		Applied( queue, sent[^1] );
		Assert.IsTrue( queue.IsEmpty );
		Assert.AreEqual( 0, queue.TakeReports().Count, "nothing to say" );
	}

	[TestMethod]
	public void ADragsEndWhoseOutcomeIsNotKnownGoesAgainAFewTimesThenTheDragIsGivenUp()
	{
		var queue = new CoasterRequestQueue();
		var drag = queue.NewDrag();
		CoasterOutcome? ended = null;
		queue.Enqueue( new EndCoasterNodeEditCommand( Station ), drag, outcome => ended = outcome );
		Send( queue, 0 );
		for ( var attempt = 0; attempt < CoasterRequestQueue.MaximumRetries; attempt++ )
		{
			sent[^1].State = CoopGuestRequestState.Unknown;
			Assert.IsTrue( queue.Complete( sent[^1] ) );
			Send( queue, 1 + attempt );
			Assert.AreEqual( attempt + 2, sent.Count, "it went again at once" );
		}
		sent[^1].State = CoopGuestRequestState.Unknown;
		Assert.IsTrue( queue.Complete( sent[^1] ) );
		Assert.AreEqual( true, ended?.Unknown, "given up, its completion runs" );
		var report = queue.TakeReports().Single();
		Assert.AreEqual( (CoasterGuestAction.DragLost, drag), (report.Action, report.Drag), "the drag is lost: its panel ends" );
		Assert.IsTrue( queue.IsEmpty );
	}

	[TestMethod]
	public void ADragsLastValueAParkThatTakesNoCommandsRefusedIsGivenUpAtOnce()
	{
		var queue = new CoasterRequestQueue();
		var drag = queue.NewDrag();
		queue.Step( drag, Loft( 0.5f ) );
		queue.Enqueue( new EndCoasterNodeEditCommand( Station ), drag );
		Send( queue, 0 );
		Refused( queue, sent[0], CoopGuestRefusal.Unavailable );
		var report = queue.TakeReports().Single();
		Assert.AreEqual( (CoasterGuestAction.DragLost, drag), (report.Action, report.Drag) );
		Send( queue, 1 );
		Assert.IsInstanceOfType( sent[^1].Command, typeof( EndCoasterNodeEditCommand ), "the end still goes: the host's park says what it takes" );
	}

	[TestMethod]
	public void AClosedPanelsUnsentDragGoesAndAQueueClearedAtTheSessionsEndKeepsNothing()
	{
		var queue = new CoasterRequestQueue();
		var drag = queue.NewDrag();
		queue.Enqueue( new BeginCoasterNodeEditCommand( Station, 3, CoasterDrag.Move ), drag );
		queue.Step( drag, new EditCoasterNodeCommand( Station, CoasterNodeField.Move, 0, 4, 4 ) );
		Assert.IsTrue( queue.PressUnsent( drag ) );
		queue.DropUnsent( drag );
		Assert.IsFalse( queue.HasUnsent, "a press that did not go goes nowhere, nor its steps" );
		queue.Enqueue( new CoasterClickCommand( Station, 1, 1, 40, false, 0 ), 0, _ => Assert.Fail( "no outcome after the session's end" ) );
		Send( queue, 0 );
		queue.Clear();
		Assert.IsTrue( queue.IsEmpty );
		sent[^1].State = CoopGuestRequestState.Ended;
		Assert.IsFalse( queue.Complete( sent[^1] ), "an outcome after the clear is no longer the queue's" );
	}
}
