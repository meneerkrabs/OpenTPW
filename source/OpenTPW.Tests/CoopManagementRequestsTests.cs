using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's management queue (<see cref="CoopManagementRequests"/>, docs/COOP.md "A guest's management windows"), headless: each target's order, the targets'
/// independence, the cap of four open requests and the pacing, the backpressure, a knob's supersession, the rights asked before the send, the outcomes a waiter keeps in order and the notes
/// nobody waits for, a batch checked whole, and the session's end. The requests are the test's to resolve, as the session's ledger resolves them (<see cref="CoopManagementRequests.Resolved"/>).
/// </summary>
[TestClass]
public class CoopManagementRequestsTests
{
	/// <summary>The test's issuer: the rights it answers, the requests it made, and the refusals it said through the session's path.</summary>
	private sealed class Issuer : IManagementIssuer
	{
		public readonly List<CoopGuestCommands.Request> Sent = new();
		/// <summary>The rights' refusal of a command, or null when they allow it.</summary>
		public Func<ParkCommand, ParkCommandResult?> Rights = _ => null;
		/// <summary>How often the session's refusal path said the rights' note.</summary>
		public int SaidRefusals;

		public ParkCommandResult? RightsRefusalOf( ParkCommand command ) => Rights( command );

		public bool RefusesHere( ParkCommand command )
		{
			if ( Rights( command ) == null )
				return false;
			SaidRefusals++;
			return true;
		}

		public ParkCommandResult Issue( ParkCommand command )
		{
			var request = new CoopGuestCommands.Request( (uint)Sent.Count + 1, command, Array.Empty<byte>() ) { State = CoopGuestRequestState.Sent };
			Sent.Add( request );
			return ParkCommandResult.Sent( request );
		}

		/// <summary>The commands it sent, in order.</summary>
		public ParkCommand[] Commands => Sent.Select( request => request.Command ).ToArray();
	}

	private static readonly ParkCommandResult NotPermitted = ParkCommandResult.Rejected( CoopBuildRights.NotPermittedReason ) with { RightsRefused = true };

	/// <summary>A test's own target, which no window has (the cap needs more targets than the windows make).</summary>
	private static ManagementTarget Synthetic( int id ) => new( ManagementTargetKind.Synthetic, id );

	private static SetEntranceFeeCommand Fee( int fee ) => new( fee );

	private static ManagementItem FeeItem( int fee ) => new( ManagementTarget.Finance, ManagementKnob.Fee, Fee( fee ) );

	private static ManagementItem Door( bool open ) => new( ManagementTarget.Finance, ManagementKnob.Door, new SetParkOpenCommand( open ) );

	private static ManagementItem Refresh => new( ManagementTarget.Loans, ManagementKnob.Refresh, new RefreshLoanOffersCommand() );

	private static ManagementItem Take( int offer ) => new( ManagementTarget.Loans, null, new TakeLoanCommand( offer, 1000 ) );

	private static ManagementEntry One( CoopManagementRequests queue, ManagementItem item, ManagementWaiter? waiter = null ) => queue.Enqueue( new[] { item }, waiter ).Entries.Single();

	/// <summary>The host applied <paramref name="request"/>'s record here with <paramref name="result"/>, and the ledger raised it.</summary>
	private static void Applied( CoopManagementRequests queue, CoopGuestCommands.Request request, ParkCommandResult result )
	{
		request.State = CoopGuestRequestState.Applied;
		request.Result = result;
		queue.Resolved( request );
	}

	private static void Accepted( CoopManagementRequests queue, CoopGuestCommands.Request request ) => Applied( queue, request, ParkCommandResult.Ok() );

	/// <summary>The host took no record of <paramref name="request"/>.</summary>
	private static void NotTaken( CoopManagementRequests queue, CoopGuestCommands.Request request, CoopGuestRefusal refusal = CoopGuestRefusal.TooFast )
	{
		request.State = CoopGuestRequestState.Refused;
		request.Refusal = refusal;
		queue.Resolved( request );
	}

	[TestMethod]
	public void ATargetsEntriesGoOneAtATimeInTheirOrderAndTargetsGoSideBySide()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var fee = One( queue, FeeItem( 5 ) );
		var name = One( queue, new ManagementItem( ManagementTarget.Finance, ManagementKnob.Name, new RenameParkCommand( "New", "Name" ) ) );
		var effort = One( queue, new ManagementItem( ManagementTarget.Research, ManagementKnob.Effort( ResearchCategory.Ride ), new SetResearchEffortCommand( ResearchCategory.Ride, 40 ) ) );
		queue.Pump( issuer, 0 );
		queue.Pump( issuer, 1 );
		queue.Pump( issuer, 2 );
		CollectionAssert.AreEqual( new ParkCommand[] { fee.Command, effort.Command }, issuer.Commands, "each target's first entry; the second of the finance target waits" );
		Assert.AreEqual( (ManagementEntryState.Open, ManagementEntryState.Unsent, ManagementEntryState.Open), (fee.State, name.State, effort.State) );
		Assert.AreSame( fee, issuer.Sent[0].Owner, "the queue owns its request" );
		Accepted( queue, issuer.Sent[1] );
		queue.Pump( issuer, 3 );
		Assert.AreEqual( 2, issuer.Sent.Count, "the research target's outcome frees nothing of the finance target's" );
		Accepted( queue, issuer.Sent[0] );
		queue.Pump( issuer, 4 );
		CollectionAssert.AreEqual( new ParkCommand[] { fee.Command, effort.Command, name.Command }, issuer.Commands, "the next goes once the open one came back" );
		Accepted( queue, issuer.Sent[2] );
		Assert.AreEqual( 0, queue.Pending.Count() );
		Assert.AreEqual( 0, queue.TakeNotes().Count, "an accepted action says nothing" );
	}

	[TestMethod]
	public void AtMostFourAreOpenAcrossTheTargetsTheOldestFirstAndOneGoesEveryFifthOfASecond()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var entries = Enumerable.Range( 1, 5 ).Select( id => One( queue, new ManagementItem( Synthetic( id ), null, Fee( id ) ) ) ).ToArray();
		// one a pump, and the next only 0.2 s after the last
		queue.Pump( issuer, 10 );
		queue.Pump( issuer, 10.1 );
		Assert.AreEqual( 1, issuer.Sent.Count, "the pacing" );
		// (a quarter of a second apart: a fifth of a second is no exact binary fraction)
		for ( var step = 1; step <= 6; step++ )
			queue.Pump( issuer, 10 + step * 0.25 );
		CollectionAssert.AreEqual( entries.Take( 4 ).Select( entry => entry.Command ).ToArray(), issuer.Commands, "four open at most, the oldest first" );
		Assert.AreEqual( CoopManagementRequests.MaximumOpen, queue.OpenCount );
		Assert.AreEqual( ManagementEntryState.Unsent, entries[4].State );
		// one comes back: the fifth goes at the next pump
		Accepted( queue, issuer.Sent[2] );
		queue.Pump( issuer, 12 );
		Assert.AreEqual( entries[4].Command, issuer.Sent[^1].Command );
		Assert.AreEqual( CoopManagementRequests.MaximumOpen, queue.OpenCount );
	}

	[TestMethod]
	public void ARequestTheHostCouldNotTakeIsAnOutcomeAndItsTargetGoesOn()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var waiter = queue.NewWaiter();
		var take = One( queue, Take( 1 ), waiter );
		var refresh = One( queue, Refresh );
		var fee = One( queue, FeeItem( 9 ) );
		queue.Pump( issuer, 0 );
		queue.Pump( issuer, 1 );
		NotTaken( queue, issuer.Sent[0] );
		NotTaken( queue, issuer.Sent[1] );
		// the waiter hears its own; the entry without one says the note, once
		var outcome = queue.TakeOutcomes( waiter ).Single();
		Assert.AreEqual( (ManagementOutcomeKind.NotTaken, ManagementNoteKind.NotTaken), (outcome.Kind, outcome.Note.Kind) );
		Assert.AreSame( take, outcome.Entry );
		var note = queue.TakeNotes().Single();
		Assert.AreSame( fee, note.Entry );
		Assert.AreEqual( ManagementNoteKind.NotTaken, note.Note.Kind );
		// never sent again by the queue: the target goes on with its next entry
		queue.Pump( issuer, 2 );
		CollectionAssert.AreEqual( new ParkCommand[] { take.Command, fee.Command, refresh.Command }, issuer.Commands );
	}

	[TestMethod]
	public void ARightTakenBackBeforeTheSendIsSaidOnceWhereItsWaiterStands()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var open = One( queue, Take( 1 ) );
		queue.Pump( issuer, 0 );
		var waiter = queue.NewWaiter();
		var waited = One( queue, Take( 2 ), waiter );
		var detached = queue.NewWaiter();
		var escaped = One( queue, Take( 3 ), detached );
		var bare = One( queue, Refresh );
		queue.Detach( detached );
		// the finance right is taken back: the open entry is the record's to decide, the others are refused at the pump without a request
		issuer.Rights = command => command is TakeLoanCommand or RefreshLoanOffersCommand ? NotPermitted : null;
		Applied( queue, issuer.Sent[0], NotPermitted );
		queue.Pump( issuer, 1 );
		Assert.AreEqual( 1, issuer.Sent.Count, "nothing more went" );
		Assert.AreEqual( 0, queue.Pending.Count() );
		// a live waiter hears it once, as an outcome
		var outcome = queue.TakeOutcomes( waiter ).Single();
		Assert.AreSame( waited, outcome.Entry );
		Assert.AreEqual( (ManagementOutcomeKind.Refused, ManagementNoteKind.Rights, CoopRefusal.NotPermitted), (outcome.Kind, outcome.Note.Kind, outcome.Note.Refusal) );
		// every refusal without a live waiter is durable: a hidden or replaced HUD must still hear the bare one's refusal, without a second transient notice
		CollectionAssert.AreEqual( new[] { open, escaped, bare }, queue.TakeNotes().Select( note => note.Entry ).ToArray() );
		Assert.AreEqual( 0, issuer.SaidRefusals );
		Assert.AreEqual( ManagementEntryState.Resolved, bare.State );
	}

	[TestMethod]
	public void AWaitersOutcomesAreKeptInOrderUntilTakenAndADetachMakesThemNotes()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var opening = queue.NewWaiter();
		// an accepted refresh, then a queued refresh refused at the pump once the right is taken back: both outcomes before any take, in order
		var first = One( queue, Refresh, opening );
		queue.Pump( issuer, 0 );
		One( queue, Take( 1 ) );
		Accepted( queue, issuer.Sent[0] );
		queue.Pump( issuer, 1 );
		var second = One( queue, Refresh, opening );
		issuer.Rights = command => command is RefreshLoanOffersCommand ? NotPermitted : null;
		Accepted( queue, issuer.Sent[1] );
		queue.Pump( issuer, 2 );
		var outcomes = queue.TakeOutcomes( opening );
		CollectionAssert.AreEqual( new[] { first, second }, outcomes.Select( outcome => outcome.Entry ).ToArray() );
		Assert.AreEqual( (ManagementOutcomeKind.Accepted, ManagementOutcomeKind.Refused), (outcomes[0].Kind, outcomes[1].Kind) );
		Assert.AreEqual( 0, queue.TakeOutcomes( opening ).Count, "each once" );
		// a live waiter's outcome stays through more resolutions than the ledger keeps and more notes than the queue keeps
		issuer.Rights = _ => null;
		var waiting = One( queue, new ManagementItem( Synthetic( 1 ), null, Fee( 1 ) ), opening );
		queue.Pump( issuer, 3 );
		Accepted( queue, issuer.Sent[^1] );
		for ( var index = 0; index < CoopGuestCommands.MaximumCompleted + 10; index++ )
		{
			One( queue, new ManagementItem( Synthetic( 2 ), null, Fee( index ) ) );
			queue.Pump( issuer, 4 + index );
			NotTaken( queue, issuer.Sent[^1] );
		}
		Assert.AreEqual( CoopManagementRequests.MaximumNotes, queue.TakeNotes().Count, "the notes keep the newest only" );
		Assert.AreSame( waiting, queue.TakeOutcomes( opening ).Single().Entry, "and the waiter's outcome stays" );
		// a detach moves what is not taken yet to the notes, in order, and so do the outcomes that come after it
		var later = queue.NewWaiter();
		One( queue, new ManagementItem( Synthetic( 3 ), null, Fee( 1 ) ), later );
		One( queue, new ManagementItem( Synthetic( 4 ), null, Fee( 2 ) ), later );
		One( queue, new ManagementItem( Synthetic( 5 ), null, Fee( 3 ) ), later );
		for ( var index = 0; index < 3; index++ )
			queue.Pump( issuer, 1000 + index );
		NotTaken( queue, issuer.Sent[^3] );
		NotTaken( queue, issuer.Sent[^2] );
		queue.Detach( later );
		NotTaken( queue, issuer.Sent[^1] );
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, queue.TakeNotes().Select( note => ((SetEntranceFeeCommand)note.Command).Fee ).ToArray() );
		Assert.AreEqual( 0, queue.TakeOutcomes( later ).Count );
	}

	[TestMethod]
	public void AWaitersOutcomesAreKeptHoweverManyComeBeforeATake()
	{
		// a storage stress test: more outcomes than any window's opening makes, put straight into the waiter
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var waiter = queue.NewWaiter();
		var count = 3 * CoopManagementRequests.MaximumNotes;
		for ( var index = 0; index < count; index++ )
		{
			One( queue, new ManagementItem( Synthetic( 1 ), null, Fee( index ) ), waiter );
			queue.Pump( issuer, index );
			Accepted( queue, issuer.Sent[^1] );
		}
		CollectionAssert.AreEqual( Enumerable.Range( 0, count ).ToArray(), queue.TakeOutcomes( waiter ).Select( outcome => ((SetEntranceFeeCommand)outcome.Command).Fee ).ToArray() );
	}

	[TestMethod]
	public void TheHudsWaiterIsNeverDetachedAndHoldsNoMoreThanAFullTarget()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		queue.Detach( queue.HudWaiter );
		Assert.IsFalse( queue.HudWaiter.Detached, "whichever HUD is current takes its outcomes" );
		// the finance target full of alternating door and fee entries behind an open one: every door outcome kept until a HUD takes it
		One( queue, FeeItem( 1 ) );
		queue.Pump( issuer, 0 );
		for ( var index = 0; index < CoopManagementRequests.MaximumUnsent; index++ )
			Assert.IsTrue( queue.Enqueue( new[] { index % 2 == 0 ? Door( index % 4 == 0 ) : FeeItem( index ) }, index % 2 == 0 ? queue.HudWaiter : null ).Queued, $"entry {index}" );
		Assert.IsTrue( queue.Enqueue( new[] { Door( true ) }, queue.HudWaiter ).Full, "the seventeenth unsent entry" );
		for ( var step = 0; queue.Pending.Any() && step < 100; step++ )
		{
			Accepted( queue, issuer.Sent[^1] );
			queue.Pump( issuer, 1 + step );
		}
		var doors = queue.TakeOutcomes( queue.HudWaiter );
		Assert.AreEqual( CoopManagementRequests.MaximumUnsent / 2, doors.Count );
		Assert.IsTrue( doors.All( outcome => outcome is { Accepted: true, Command: SetParkOpenCommand } ) );
	}

	[TestMethod]
	public void AKnobsLaterValueTakesTheUnsentTailOfItsRunAndNothingElse()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		// a drag: one entry, the latest value
		var fee = One( queue, FeeItem( 1 ) );
		Assert.AreSame( fee, One( queue, FeeItem( 2 ) ) );
		Assert.AreSame( fee, One( queue, FeeItem( 3 ) ) );
		Assert.AreEqual( Fee( 3 ), fee.Command );
		Assert.AreEqual( Fee( 3 ), queue.PendingCommand( ManagementTarget.Finance, ManagementKnob.Fee ) );
		// an open entry is never touched: the next value is appended
		queue.Pump( issuer, 0 );
		var next = One( queue, FeeItem( 4 ) );
		Assert.AreNotSame( fee, next );
		Assert.AreEqual( Fee( 3 ), issuer.Sent[0].Command );
		// only a contiguous tail run: fee, door, fee is three entries
		var door = One( queue, Door( false ) );
		var after = One( queue, FeeItem( 5 ) );
		Assert.AreEqual( 3, new[] { next, door, after }.Distinct().Count() );
		Assert.AreEqual( Fee( 5 ), queue.PendingCommand( ManagementTarget.Finance, ManagementKnob.Fee ), "the latest value is drawn" );
		// refresh, take, refresh stays three, in that order
		var refresh = One( queue, Refresh );
		var take = One( queue, Take( 1 ) );
		var again = One( queue, Refresh );
		Assert.AreEqual( 3, new[] { refresh, take, again }.Distinct().Count() );
		// a waiter's entry is taken only by one of the same waiter, which keeps it; another waiter's, or one without, is appended
		var opening = queue.NewWaiter();
		var waited = One( queue, Refresh, opening );
		Assert.AreSame( waited, One( queue, Refresh, opening ) );
		var reopened = queue.NewWaiter();
		var other = One( queue, Refresh, reopened );
		Assert.AreNotSame( waited, other );
		Assert.AreSame( reopened, other.Waiter );
		Assert.AreNotSame( other, One( queue, Refresh ) );
		Assert.AreSame( opening, waited.Waiter );
	}

	[TestMethod]
	public void AnEarlierOutcomeLeavesALaterValueDrawnUntilTheLastComesBack()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		One( queue, FeeItem( 10 ) );
		queue.Pump( issuer, 0 );
		One( queue, FeeItem( 11 ) );
		Applied( queue, issuer.Sent[0], ParkCommandResult.Rejected( "refused" ) );
		Assert.AreEqual( Fee( 11 ), queue.PendingCommand( ManagementTarget.Finance, ManagementKnob.Fee ), "the second fee stays drawn" );
		Assert.AreEqual( 0, queue.TakeNotes().Count, "a fee's refusal says nothing: its control's return shows it" );
		queue.Pump( issuer, 1 );
		Accepted( queue, issuer.Sent[1] );
		Assert.IsNull( queue.PendingCommand( ManagementTarget.Finance, ManagementKnob.Fee ), "the park's value once nothing is pending" );
		Assert.IsFalse( queue.HasPending( ManagementTarget.Finance, _ => true ) );
	}

	[TestMethod]
	public void AFullTargetTakesNoMoreAndABatchGoesWholeOrNotAtAll()
	{
		var queue = new CoopManagementRequests();
		for ( var index = 0; index < CoopManagementRequests.MaximumUnsent; index++ )
			Assert.IsTrue( queue.Enqueue( new[] { index % 2 == 0 ? Take( index ) : Refresh } ).Queued );
		var full = queue.Enqueue( new[] { Take( 99 ) } );
		Assert.IsTrue( full.Full );
		Assert.IsFalse( full.Queued );
		// a knob's run at the tail supersedes and never fills it
		Assert.IsTrue( queue.Enqueue( new[] { Refresh } ).Queued );
		// a batch that would not fit whole is not queued at all, even its part on another target
		var batch = queue.Enqueue( new[] { FeeItem( 1 ), Take( 100 ) } );
		Assert.IsTrue( batch.Full );
		Assert.IsNull( queue.PendingCommand( ManagementTarget.Finance, ManagementKnob.Fee ) );
		Assert.AreEqual( CoopManagementRequests.MaximumUnsent, queue.Pending.Count() );
	}

	[TestMethod]
	public void ABatchsEntriesSettleOneByOneAndAMalformedCommandRefusesTheWholeBatch()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var waiter = queue.NewWaiter();
		var entries = queue.Enqueue( new[] { new ManagementItem( Synthetic( 1 ), null, Fee( 1 ) ), new ManagementItem( Synthetic( 1 ), null, Fee( 2 ) ) }, waiter ).Entries;
		queue.Pump( issuer, 0 );
		Applied( queue, issuer.Sent[0], NotPermitted );
		queue.Pump( issuer, 1 );
		Accepted( queue, issuer.Sent[1] );
		var outcomes = queue.TakeOutcomes( waiter );
		CollectionAssert.AreEqual( entries.ToArray(), outcomes.Select( outcome => outcome.Entry ).ToArray() );
		Assert.AreEqual( (ManagementOutcomeKind.Refused, ManagementOutcomeKind.Accepted), (outcomes[0].Kind, outcomes[1].Kind) );
		// a window's bug: a strict run throws, and nothing of the batch is queued either way
		var malformed = new[] { FeeItem( 3 ), new ManagementItem( ManagementTarget.Finance, ManagementKnob.Fee, Fee( -1 ) ) };
		Assert.ThrowsException<InvalidOperationException>( () => queue.Enqueue( malformed ) );
		var strict = ParkCommands.Strict;
		ParkCommands.Strict = false;
		try
		{
			Assert.IsFalse( queue.Enqueue( malformed ).Queued );
		}
		finally
		{
			ParkCommands.Strict = strict;
		}
		Assert.AreEqual( 0, queue.Pending.Count() );
	}

	[TestMethod]
	public void EachOutcomeKindAndItsNote()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		ManagementOutcome Resolve( ManagementItem item, Action<CoopGuestCommands.Request> resolve )
		{
			var waiter = queue.NewWaiter();
			One( queue, item, waiter );
			queue.Pump( issuer, issuer.Sent.Count );
			resolve( issuer.Sent[^1] );
			return queue.TakeOutcomes( waiter ).Single();
		}
		var accepted = Resolve( Take( 1 ), request => Accepted( queue, request ) );
		Assert.AreEqual( (ManagementOutcomeKind.Accepted, ManagementNoteKind.None), (accepted.Kind, accepted.Note.Kind) );
		var unaffordable = Resolve( new ManagementItem( ManagementTarget.Loans, null, new RepayLoanCommand( 1, 500 ) ),
			request => Applied( queue, request, new ParkCommandResult( false, "no money", LoanRefusal.Unaffordable ) ) );
		Assert.AreEqual( ManagementNoteKind.InsufficientFunds, unaffordable.Note.Kind );
		foreach ( var refusal in new[] { LoanRefusal.Changed, LoanRefusal.Gone, LoanRefusal.Bankrupt } )
			Assert.AreEqual( ManagementNoteKind.NotAvailable, Resolve( Take( 2 ), request => Applied( queue, request, new ParkCommandResult( false, "gone", refusal ) ) ).Note.Kind, $"{refusal}" );
		var stale = Resolve( new ManagementItem( ManagementTarget.Research, null, new MoveResearchCommand( ResearchCategory.Ride, 1, 1100, 1 ) ),
			request => Applied( queue, request, ParkCommandResult.Rejected( ParkSettingCommand.StaleReason ) ) );
		Assert.AreEqual( ManagementNoteKind.NotAvailable, stale.Note.Kind );
		var stuck = Resolve( new ManagementItem( ManagementTarget.Research, null, new MoveResearchCommand( ResearchCategory.Ride, 1, 1100, 1 ) ),
			request => Applied( queue, request, ParkCommandResult.Rejected( "There is no other project to research." ) ) );
		Assert.AreEqual( ManagementNoteKind.None, stuck.Note.Kind, "the host's arrow says nothing of its own refusal either" );
		Assert.AreEqual( ManagementNoteKind.None, Resolve( FeeItem( 2 ), request => Applied( queue, request, ParkCommandResult.Rejected( "no" ) ) ).Note.Kind );
		var rights = Resolve( FeeItem( 3 ), request => Applied( queue, request, NotPermitted ) );
		Assert.AreEqual( (ManagementNoteKind.Rights, CoopRefusal.NotPermitted), (rights.Note.Kind, rights.Note.Refusal) );
		Assert.AreEqual( ManagementOutcomeKind.NotTaken, Resolve( FeeItem( 4 ), request => NotTaken( queue, request, CoopGuestRefusal.Malformed ) ).Kind );
		foreach ( var state in new[] { CoopGuestRequestState.InSnapshot, CoopGuestRequestState.Unknown } )
		{
			var unknown = Resolve( FeeItem( 5 ), request =>
			{
				request.State = state;
				queue.Resolved( request );
			} );
			Assert.AreEqual( (ManagementOutcomeKind.Unknown, ManagementNoteKind.Unknown), (unknown.Kind, unknown.Note.Kind), $"{state}" );
		}
		var threw = Resolve( FeeItem( 6 ), request => Applied( queue, request, new ParkCommandResult( false, "threw" ) { Error = new InvalidOperationException() } ) );
		Assert.AreEqual( ManagementOutcomeKind.Unknown, threw.Kind, "a command that threw: what it wrote is not known" );
		// a request that is not the queue's is not its business
		var stranger = new CoopGuestCommands.Request( 999, Fee( 1 ), Array.Empty<byte>() ) { State = CoopGuestRequestState.Applied, Result = ParkCommandResult.Ok() };
		queue.Resolved( stranger );
		Assert.AreEqual( 0, queue.TakeNotes().Count );
	}

	[TestMethod]
	public void TheSessionsEndResolvesEveryEntryAsEndedSilently()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var waiter = queue.NewWaiter();
		One( queue, Take( 1 ), waiter );
		One( queue, FeeItem( 1 ) );
		One( queue, Door( false ), queue.HudWaiter );
		queue.Pump( issuer, 0 );
		// the ledger ends its open requests first, then the queue its unsent entries
		issuer.Sent[0].State = CoopGuestRequestState.Ended;
		queue.Resolved( issuer.Sent[0] );
		queue.End();
		Assert.IsTrue( queue.Ended );
		Assert.AreEqual( 0, queue.Pending.Count() );
		Assert.AreEqual( ManagementOutcomeKind.Ended, queue.TakeOutcomes( waiter ).Single().Kind, "a waiter still hears it, to close" );
		Assert.AreEqual( ManagementOutcomeKind.Ended, queue.TakeOutcomes( queue.HudWaiter ).Single().Kind );
		Assert.AreEqual( 0, queue.TakeNotes().Count, "and nothing is said" );
		Assert.IsFalse( queue.Enqueue( new[] { FeeItem( 2 ) } ).Queued, "nothing more is queued" );
		queue.Pump( issuer, 1 );
		Assert.AreEqual( 1, issuer.Sent.Count );
	}

	[TestMethod]
	public void ARequestThatResolvedInsideItsOwnSendResolvesItsEntry()
	{
		var queue = new CoopManagementRequests();
		var waiter = queue.NewWaiter();
		var entry = One( queue, Take( 1 ), waiter );
		var issuer = new Issuer();
		queue.Pump( new ResolvingIssuer( issuer ), 0 );
		Assert.AreEqual( ManagementEntryState.Resolved, entry.State );
		Assert.AreEqual( 0, queue.OpenCount );
		Assert.AreEqual( ManagementOutcomeKind.NotTaken, queue.TakeOutcomes( waiter ).Single().Kind );
	}

	/// <summary>An issuer whose request the host refuses before the issue returns, as a link that delivers at once would.</summary>
	private sealed class ResolvingIssuer( Issuer inner ) : IManagementIssuer
	{
		public ParkCommandResult? RightsRefusalOf( ParkCommand command ) => inner.RightsRefusalOf( command );
		public bool RefusesHere( ParkCommand command ) => inner.RefusesHere( command );

		public ParkCommandResult Issue( ParkCommand command )
		{
			var result = inner.Issue( command );
			result.Request!.State = CoopGuestRequestState.Refused;
			result.Request.Refusal = CoopGuestRefusal.TooFast;
			return result;
		}
	}
}
