using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-undo] A guest's take-backs across a resync (docs/COOP.md, "Conflicts and undo (C5)"): a take-back is a request of the session's, so the swapped-in park's cut settles the
/// one the host already decided, and one made during the resync goes out after it and is decided then; the parks agree either way.
/// </summary>
public partial class CoopJoinSessionTests
{
	[TestMethod]
	public void AGuestsTakeBacksAreSettledAgainstTheResyncsCut()
	{
		var guest = Hosted( 1 ).Single();
		Build( 120, 64 );
		var seat = guest.Link.Slot;
		host.Issue( new SetBuildGrantCommand( seat, true ) );
		RoundsUntil( () => guest.Level!.HasBuildGrant( seat ), "the grant reached the guest" );
		var requests = guest.Session!.GuestCommands!;
		var first = requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 13, 21, 15, 21 ) );
		var second = requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 13, 23, 15, 23 ) );
		RoundsUntil( () => !first.IsOpen && !second.IsOpen, "both lines came back" );
		Assert.IsTrue( first.Accepted && second.Accepted );
		// the host decides and applies the second line's take-back, and the guest never gets the frame that holds its record: a resync follows
		guest.Link.DropFrame = _ => true;
		var inside = requests.Submit( new TakeBackCommand( false, second.Record, -1, -1, -1 ) );
		RoundsUntil( () => host.Level!.Commands.Log.Any( entry => entry.Command is TakeBackCommand { Verdict: TakeBackVerdict.Taken } && entry.Command.Player == seat ),
			"the host took the second line back" );
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		RoundsUntil( () => guest.Session.Phase == CoopGuestPhase.Resyncing, "the guest lost the stream and asked for the park" );
		// the first line's take-back, made while the park is resynced, waits and goes out after the cut
		var during = requests.Submit( new TakeBackCommand( false, first.Record, -1, -1, -1 ) );
		Assert.AreEqual( CoopGuestRequestState.Waiting, during.State );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old && !inside.IsOpen && !during.IsOpen, "the park was swapped in and both take-backs settled" );
		Assert.AreEqual( CoopGuestRequestState.InSnapshot, inside.State, "its record is inside the cut" );
		Assert.AreEqual( CoopGuestRequestState.Applied, during.State );
		Assert.IsTrue( during.Accepted, during.Result?.Reason );
		foreach ( var level in new[] { host.Level!, guest.Level! } )
			Assert.IsFalse( level.Guests!.Grid.IsWalkable( 14, 21 ) || level.Guests.Grid.IsWalkable( 14, 23 ), "both lines taken back on every park" );
		Build( 60, 65 );
		CatchUpAndCompare( guest );
	}
}
