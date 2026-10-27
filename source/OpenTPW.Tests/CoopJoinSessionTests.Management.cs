using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's management queue across a resync (C4b-1, docs/COOP.md "A guest's management windows"): the queue is the session's, so its open entries settle against the
/// swapped-in park's cut, its unsent ones wait through the resync and are asked against the new park's rights, and what was queued meanwhile goes once after the swap.
/// </summary>
public partial class CoopJoinSessionTests
{
	[TestMethod]
	public void AGuestsManagementEntriesSettleAgainstTheResyncsCutAndTheRestGoOnceAgainstTheNewPark()
	{
		var guest = Hosted( 1 ).Single();
		Build( 120, 64 );
		var seat = guest.Link.Slot;
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.Finance | CoopRights.Research ) ).Accepted );
		RoundsUntil( () => guest.Level!.RightsOf( seat ) == (CoopRights.Finance | CoopRights.Research), "the rights reached the guest" );
		var queue = guest.Session!.Management!;
		var waiter = queue.NewWaiter();
		int FromSeat<T>( System.Func<T, bool>? matches = null ) where T : ParkCommand =>
			host.Level!.Commands.Log.Count( entry => entry.Command is T command && command.Player == seat && (matches == null || matches( command )) );
		// an accepted fee, a take of an offer no bank makes (refused at its record) and an effort, whose records the guest never gets: a resync follows
		guest.Link.DropFrame = _ => true;
		var fee = new SetEntranceFeeCommand( 37 );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Finance, ManagementKnob.Fee, fee ) }, waiter ).Queued );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Loans, null, new TakeLoanCommand( 7, 1 ) ) }, waiter ).Queued );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Research, ManagementKnob.Effort( ResearchCategory.Shop ),
			new SetResearchEffortCommand( ResearchCategory.Shop, 21 ) ) } ).Queued );
		RoundsUntil( () => FromSeat<SetEntranceFeeCommand>() == 1 && FromSeat<TakeLoanCommand>() == 1 && FromSeat<SetResearchEffortCommand>() == 1, "the host applied the three" );
		Assert.AreEqual( 3, queue.OpenCount, "their records never applied in the old park" );
		// behind the open ones: a workload whose right the host takes back before the cut, and a refresh unsent on the frame the stream stops
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Research, ManagementKnob.Workload, new SetResearchWorkloadCommand( 90 ) ) } ).Queued );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Loans, ManagementKnob.Refresh, new RefreshLoanOffersCommand() ) } ).Queued );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.Finance ) ).Accepted );
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		RoundsUntil( () => guest.Session.Phase == CoopGuestPhase.Resyncing, "the guest lost the stream and asked for the park" );
		// a fee queued during the resync goes after it, once
		var later = new SetEntranceFeeCommand( 41 );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Finance, ManagementKnob.Fee, later ) } ).Queued );
		var refreshes = FromSeat<RefreshLoanOffersCommand>();
		Rounds( 30 );
		Assert.AreEqual( (0, refreshes), (FromSeat<SetEntranceFeeCommand>( command => command == later with { Player = seat } ), FromSeat<RefreshLoanOffersCommand>()),
			"nothing goes during the resync" );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old && !queue.Pending.Any(), "the park was swapped in and every entry settled" );
		// the open ones' records are inside the cut: their outcomes are not known
		var outcomes = queue.TakeOutcomes( waiter );
		Assert.AreEqual( 2, outcomes.Count );
		Assert.IsTrue( outcomes.All( outcome => outcome.Kind == ManagementOutcomeKind.Unknown ), "the fee and the take: not known" );
		Assert.AreEqual( ManagementNoteKind.Unknown, queue.TakeNotes().Single().Note.Kind, "the effort's note" );
		// the workload is asked against the new park, which no longer gives research: refused at the send, said through the session's path
		Assert.AreEqual( 0, FromSeat<SetResearchWorkloadCommand>() );
		Assert.AreEqual( (1, CoopRefusal.NotPermitted), (guest.Level!.SessionRefusals, guest.Level.LastSessionRefusal) );
		// the refresh and the later fee went once each
		Assert.AreEqual( refreshes + 1, FromSeat<RefreshLoanOffersCommand>() );
		Assert.AreEqual( 1, FromSeat<SetEntranceFeeCommand>( command => command == later with { Player = seat } ) );
		Assert.AreEqual( 1, FromSeat<SetEntranceFeeCommand>( command => command == fee with { Player = seat } ), "the fee inside the cut applied once" );
		Build( 60, 65 );
		CatchUpAndCompare( guest );
	}
}
