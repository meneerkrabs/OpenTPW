using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's requests to the host (docs/COOP.md, "Building by every player") over the in-memory server (<see cref="CoopTestNetwork"/>), the host's answers
/// played by the test: the numbering, the fence, the resend after a resume, a burst of gap answers that resends once, refusals, the records the host names, the records that apply, the
/// snapshot's reconciliation and the session's end. The host's side and the whole round trip are <see cref="CoopStreamTests"/>'.
/// </summary>
[TestClass]
public class CoopGuestCommandsTests
{
	private static readonly ParkCommand Line = new LayLineCommand( CellToolMode.Path, -1, 2, 3, 9, 3 );
	private static readonly ParkCommand Fee = new SetEntranceFeeCommand( 4 );

	private static (CoopTestNetwork.Member Host, CoopTestNetwork.Member Guest, CoopGuestCommands Requests, List<CoopGuestCommands.Request> Resolved) Session( Func<double>? clock = null )
	{
		var network = new CoopTestNetwork();
		var host = network.Add( host: true );
		var guest = network.Add();
		host.Deliver();
		guest.Deliver();
		var requests = new CoopGuestCommands( guest, clock ?? (() => 0) );
		var resolved = new List<CoopGuestCommands.Request>();
		requests.Resolved += resolved.Add;
		return (host, guest, requests, resolved);
	}

	/// <summary>The request numbers the guest's link sent, in order.</summary>
	private static uint[] Sent( CoopTestNetwork.Member guest ) => guest.Sent.Select( sent => sent.Message ).OfType<CoopGuestCommand>().Select( request => request.Sequence ).ToArray();

	/// <summary>The host's answer, as it arrives at the guest's next frame.</summary>
	private static void Answer( CoopTestNetwork.Member host, CoopTestNetwork.Member guest, uint sequence, long record, CoopGuestRefusal? refusal, uint next )
	{
		host.Send( guest.Id, new CoopGuestCommandAnswer( sequence, record, refusal, next ).Encode() );
		guest.Deliver();
	}

	private static ParkCommandResult Ok => ParkCommandResult.Ok();

	[TestMethod]
	public void RequestsAreNumberedFromOneAndSentToTheHostInOrder()
	{
		var (host, guest, requests, resolved) = Session();
		var line = requests.Submit( Line );
		var fee = requests.Submit( Fee );
		CollectionAssert.AreEqual( new uint[] { 1, 2 }, Sent( guest ) );
		Assert.IsTrue( guest.Sent.All( sent => sent.To == host.Id ), "every request goes to the host" );
		var first = guest.Sent.Select( sent => sent.Message ).OfType<CoopGuestCommand>().First();
		Assert.AreEqual( Line, ParkCommandCodec.Decode( first.Command.Span ), "a request carries its command's bytes" );
		Assert.AreEqual( (CoopGuestRequestState.Sent, CoopGuestRequestState.Sent), (line.State, fee.State) );
		Assert.IsTrue( line.IsOpen );
		CollectionAssert.AreEqual( new[] { line, fee }, requests.Open.ToArray() );
		Assert.AreEqual( 0, resolved.Count, "a request changes nothing by itself" );
	}

	[TestMethod]
	public void AFencedRequestWaitsUntilTheFenceLifts()
	{
		var (_, guest, requests, _) = Session();
		requests.Fenced = true;
		var line = requests.Submit( Line );
		requests.Submit( Fee );
		Assert.AreEqual( 0, Sent( guest ).Length, "nothing goes out while the park is being resynced" );
		Assert.AreEqual( CoopGuestRequestState.Waiting, line.State );
		requests.Fenced = false;
		CollectionAssert.AreEqual( new uint[] { 1, 2 }, Sent( guest ), "the fence lifts and the requests go out in order" );
		Assert.AreEqual( CoopGuestRequestState.Sent, line.State );
	}

	[TestMethod]
	public void AResumeSendsEveryOpenRequestAgainInOrder()
	{
		var (_, guest, requests, _) = Session();
		requests.Submit( Line );
		requests.Submit( Fee );
		guest.Drop();
		var away = requests.Submit( Line );
		Assert.AreEqual( CoopGuestRequestState.Waiting, away.State, "a request made while the link is down waits for the resume" );
		CollectionAssert.AreEqual( new uint[] { 1, 2 }, Sent( guest ) );
		guest.Resume();
		guest.Deliver();
		CollectionAssert.AreEqual( new uint[] { 1, 2, 1, 2, 3 }, Sent( guest ), "the resume sends every open request again, the waiting one included" );
		Assert.AreEqual( CoopGuestRequestState.Sent, away.State );
	}

	[TestMethod]
	public void ARefusalResolvesItsRequestOnce()
	{
		var (host, guest, requests, resolved) = Session();
		var line = requests.Submit( Line );
		var fee = requests.Submit( Fee );
		Answer( host, guest, 2, 0, CoopGuestRefusal.Malformed, 3 );
		Assert.AreEqual( CoopGuestRequestState.Refused, fee.State );
		Assert.AreEqual( CoopGuestRefusal.Malformed, fee.Refusal );
		Assert.IsFalse( fee.IsOpen );
		CollectionAssert.AreEqual( new[] { fee }, resolved );
		CollectionAssert.AreEqual( new[] { line }, requests.Open.ToArray() );
		Answer( host, guest, 2, 0, CoopGuestRefusal.Malformed, 3 );
		Assert.AreEqual( 1, resolved.Count, "an answer for a resolved request changes nothing" );
	}

	[TestMethod]
	public void AForgottenRequestsOutcomeIsUnknownNotRefused()
	{
		var (host, guest, requests, resolved) = Session();
		var line = requests.Submit( Line );
		Answer( host, guest, 1, 0, CoopGuestRefusal.Forgotten, 5 );
		Assert.AreEqual( (CoopGuestRequestState.Unknown, (CoopGuestRefusal?)CoopGuestRefusal.Forgotten), (line.State, line.Refusal), "it may have applied: the park shows" );
		CollectionAssert.AreEqual( new[] { line }, resolved );
	}

	[TestMethod]
	public void ARequestWaitingTooLongForItsOutcomeGoesOutAgain()
	{
		var now = 0.0;
		var (host, guest, requests, _) = Session( () => now );
		requests.Submit( Line );
		requests.Submit( Fee );
		now = CoopGuestCommands.ResendSeconds - 0.1;
		requests.Step();
		CollectionAssert.AreEqual( new uint[] { 1, 2 }, Sent( guest ), "nothing goes out again before its time" );
		// a gap's resend lost on its way: the next gap answer of the same number is the burst's, so only the wait sends again
		Answer( host, guest, 2, 0, CoopGuestRefusal.Gap, 1 );
		Answer( host, guest, 2, 0, CoopGuestRefusal.Gap, 1 );
		CollectionAssert.AreEqual( new uint[] { 1, 2, 1, 2 }, Sent( guest ) );
		now += CoopGuestCommands.ResendSeconds;
		requests.Step();
		CollectionAssert.AreEqual( new uint[] { 1, 2, 1, 2, 1, 2 }, Sent( guest ), "the wait sends every open request again, in order" );
		requests.Fenced = true;
		now += 10 * CoopGuestCommands.ResendSeconds;
		requests.Step();
		Assert.AreEqual( 6, Sent( guest ).Length, "nothing goes out while fenced" );
	}

	[TestMethod]
	public void ABurstOfGapAnswersSendsTheOpenRequestsAgainOnce()
	{
		var (host, guest, requests, _) = Session();
		for ( var index = 0; index < 4; index++ )
			requests.Submit( Line );
		// the host lost request 2 with a socket: requests 3 and 4 skip it
		Answer( host, guest, 3, 0, CoopGuestRefusal.Gap, 2 );
		Answer( host, guest, 4, 0, CoopGuestRefusal.Gap, 2 );
		CollectionAssert.AreEqual( new uint[] { 1, 2, 3, 4, 2, 3, 4 }, Sent( guest ), "from the number the host expects, once for the burst" );
		Assert.AreEqual( 4, requests.Open.Count, "a gap resolves nothing" );
		// a gap from another number is new: the guest sends from there
		Answer( host, guest, 4, 0, CoopGuestRefusal.Gap, 3 );
		CollectionAssert.AreEqual( new uint[] { 1, 2, 3, 4, 2, 3, 4, 3, 4 }, Sent( guest ) );
	}

	[TestMethod]
	public void TheGuestsOwnRecordResolvesItsRequestWithTheResultHere()
	{
		var (host, guest, requests, resolved) = Session();
		var line = requests.Submit( Line );
		var fee = requests.Submit( Fee );
		var result = ParkCommandResult.Rejected( "No room." );
		requests.RecordApplied( 7, new CoopRecordOrigin( host.Id, 2 ), Ok );
		requests.RecordApplied( 8, null, Ok );
		Assert.AreEqual( 0, resolved.Count, "another member's record and the host's own resolve nothing" );
		requests.RecordApplied( 9, new CoopRecordOrigin( guest.Id, 2 ), result );
		Assert.AreEqual( CoopGuestRequestState.Applied, fee.State );
		Assert.AreEqual( 9L, fee.Record );
		Assert.AreSame( result, fee.Result, "a refusal by the park's rules is a record: it applied, refused, on every peer alike" );
		Assert.IsTrue( line.IsOpen );
		Assert.AreEqual( 9L, requests.Included );
	}

	[TestMethod]
	public void ARecordTheHostNamesResolvesOnlyWhenTheParkHoldsItWithoutHavingSeenItApply()
	{
		var (host, guest, requests, resolved) = Session();
		var line = requests.Submit( Line );
		var fee = requests.Submit( Fee );
		requests.RecordApplied( 20, null, Ok );
		// a request sent twice: the host names its record. Record 15 is in the park (applied before a lost answer could tell which); record 25 is still on its way
		Answer( host, guest, 1, 15, null, 3 );
		Answer( host, guest, 2, 25, null, 3 );
		Assert.AreEqual( CoopGuestRequestState.InSnapshot, line.State );
		Assert.AreEqual( 15L, line.Record );
		Assert.IsNull( line.Result, "its result here is not known" );
		Assert.IsTrue( fee.IsOpen, "a record still on its way resolves when it applies" );
		Assert.AreEqual( 25L, fee.Record );
		requests.RecordApplied( 25, new CoopRecordOrigin( guest.Id, 2 ), Ok );
		Assert.AreEqual( CoopGuestRequestState.Applied, fee.State );
		CollectionAssert.AreEqual( new[] { line, fee }, resolved );
	}

	[TestMethod]
	public void ASnapshotReconcilesTheRequestsAgainstItsCutAndLiftsTheFence()
	{
		var (host, guest, requests, resolved) = Session();
		var inside = requests.Submit( Line );
		var after = requests.Submit( Fee );
		var unknown = requests.Submit( Line );
		requests.RecordApplied( 12, null, Ok );
		Answer( host, guest, 1, 30, null, 4 );
		Answer( host, guest, 2, 45, null, 4 );
		requests.Fenced = true;
		var waiting = requests.Submit( Fee );
		requests.Snapshot( 40 );
		Assert.AreEqual( CoopGuestRequestState.InSnapshot, inside.State, "its record is inside the cut" );
		Assert.IsTrue( after.IsOpen && unknown.IsOpen && waiting.IsOpen, "a record past the cut, or not known, is still to come" );
		Assert.IsFalse( requests.Fenced );
		Assert.AreEqual( 40L, requests.Included, "the new park holds the records up to its cut" );
		CollectionAssert.AreEqual( new uint[] { 1, 2, 3, 2, 3, 4 }, Sent( guest ), "the open requests go out again, the one fenced among them" );
		CollectionAssert.AreEqual( new[] { inside }, resolved );
		// the host answers the request it had already: its record is inside the cut
		Answer( host, guest, 3, 38, null, 5 );
		Assert.AreEqual( CoopGuestRequestState.InSnapshot, unknown.State );
	}

	[TestMethod]
	public void TheSessionsEndResolvesTheOpenRequestsAsEnded()
	{
		var (host, guest, requests, resolved) = Session();
		var line = requests.Submit( Line );
		var fee = requests.Submit( Fee );
		Answer( host, guest, 1, 0, CoopGuestRefusal.NotSeated, 2 );
		requests.End();
		Assert.AreEqual( CoopGuestRequestState.Refused, line.State );
		Assert.AreEqual( CoopGuestRequestState.Ended, fee.State );
		CollectionAssert.AreEqual( new[] { line, fee }, resolved );
		Assert.AreEqual( 0, requests.Open.Count );
		Assert.ThrowsException<ObjectDisposedException>( () => requests.Submit( Line ) );
		Answer( host, guest, 2, 9, null, 3 );
		guest.Drop();
		guest.Resume();
		guest.Deliver();
		CollectionAssert.AreEqual( new uint[] { 1, 2 }, Sent( guest ), "an ended session sends nothing more" );
		Assert.AreEqual( 2, resolved.Count );
	}

	[TestMethod]
	public void AListenerThatThrowsKeepsTheOthers()
	{
		var (host, guest, requests, resolved) = Session();
		requests.Resolved += _ => throw new InvalidOperationException( "a broken tool" );
		var second = new List<CoopGuestCommands.Request>();
		requests.Resolved += second.Add;
		requests.Submit( Line );
		Answer( host, guest, 1, 0, CoopGuestRefusal.TooFast, 2 );
		Assert.AreEqual( 1, resolved.Count );
		Assert.AreEqual( 1, second.Count, "the listener after the broken one heard it too" );
	}
}
