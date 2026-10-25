using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's requests across a resync (docs/COOP.md, "Building by every player"): the requests are the session's, so the swapped-in park's cut settles the one it holds
/// and the one made during the resync goes out after it.
/// </summary>
public partial class CoopJoinSessionTests
{
	[TestMethod]
	public void AGuestsRequestsAreSettledAgainstTheResyncsCut()
	{
		var guest = Hosted( 1 ).Single();
		Build( 120, 61 );
		var seat = guest.Link.Slot;
		host.Issue( new SetBuildGrantCommand( seat, true ) );
		RoundsUntil( () => guest.Level!.HasBuildGrant( seat ), "the grant reached the guest" );
		var requests = guest.Session!.GuestCommands!;
		var inside = new LayLineCommand( CellToolMode.Path, -1, 13, 21, 15, 21 );
		// the host applies the request, and the guest never gets the frame that holds its record: a resync follows
		guest.Link.DropFrame = _ => true;
		var first = requests.Submit( inside );
		RoundsUntil( () => host.Level!.Commands.Log.Any( entry => entry.Command is LayLineCommand { StartX: 13 } && entry.Command.Player == seat ), "the host applied the request" );
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		RoundsUntil( () => guest.Session.Phase == CoopGuestPhase.Resyncing, "the guest lost the stream and asked for the park" );
		Assert.IsTrue( first.IsOpen, "its record never applied in the old park" );
		Assert.IsTrue( requests.Fenced, "requests wait while the park is resynced" );
		var during = requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 13, 23, 15, 23 ) );
		Assert.AreEqual( CoopGuestRequestState.Waiting, during.State );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old && !first.IsOpen && !during.IsOpen, "the park was swapped in and both requests settled" );
		Assert.AreEqual( CoopGuestRequestState.InSnapshot, first.State, "its record is inside the cut" );
		Assert.IsTrue( first.Record > 0 && first.Record <= requests.Included );
		Assert.IsNull( first.Result, "its result in the new park is not known" );
		Assert.AreEqual( CoopGuestRequestState.Applied, during.State, "the request made during the resync went out after it" );
		Assert.AreEqual( 1, host.Level!.Commands.Log.Count( entry => entry.Command is LayLineCommand { StartX: 13, StartY: 21 } ), "the request sent again applied once" );
		Assert.IsFalse( requests.Fenced );
		Build( 60, 62 );
		CatchUpAndCompare( guest );
	}
}
