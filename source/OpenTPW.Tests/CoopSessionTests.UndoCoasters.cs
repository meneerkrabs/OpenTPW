using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-undo] The coaster tool's undo in a shared park (docs/COOP.md, "Conflicts and undo (C5)"; C5-3): a pylon a player's click placed is a step of that player's, and the tool's
/// Backspace is a take-back of the player's own last pylon, which the host's journal decides, for a guest and the host alike; taken, it takes the last pylon off as single player's Backspace
/// does, with no refund. A pylon whose claim ended or changed hands, or whose coaster a node edit, a stack swap or a cut changed, becomes a tombstone, which only says why it is no longer
/// taken back.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>A shared park with a coaster on both parks, the guest granted; the guest's coaster, its attraction id, the guest's seat and member.</summary>
	private (Park Host, Park Guest, View View, OriginalObject Coaster, int Ride, int Seat, uint Member) UndoCoaster()
	{
		var (host, guest, _, link) = Shared( ( park, _ ) => PlaceCoaster( park ) );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var coaster = guest.Level.Objects.Objects.Single( item => item.Runtime.Coaster != null );
		return (host, guest, view, coaster, coaster.Runtime.AttractionId, guest.Level.Commands.Player, link.Id);
	}

	/// <summary>A park of a game of one with the same coaster and its tool on, at the guests' pylon height: the twin a shared park's take-backs are held to.</summary>
	private (Park Park, OriginalObject Coaster) TwinCoaster()
	{
		var twin = NewPark();
		twin.Enter();
		var coaster = PlaceCoaster( twin );
		Assert.IsTrue( twin.Level.EnterCoasterTool( coaster ) );
		twin.Level.CoasterTool!.Height = 40;
		return (twin, coaster);
	}

	/// <summary><paramref name="park"/>'s coaster track as text: the circuit's state and every node's cell, height, loft, stack index and flags, so two parks' coasters compare equal.</summary>
	private static string CoasterOf( Park park, OriginalObject station )
	{
		var track = park.Level.GetCoasterBuilder( station )!.Track;
		return $"closed {track.IsClosed} complete {track.IsComplete} cut {track.CutAfter}: "
			+ string.Join( "; ", track.Nodes.Select( node => $"({node.X},{node.Y}) h{node.Height} t{node.T} s{node.StackIndex} f{(uint)node.Flags}" ) );
	}

	/// <summary>The guest's Backspace in its coaster tool through its HUD, sent to the host and back.</summary>
	private void GuestCoasterBackspace( Park host, Park guest, View view )
	{
		var panel = guest.Level.CoasterPanel!;
		guest.Enter();
		HudBackspace( view );
		Assert.IsTrue( panel.IsWaiting, "the take-back went to the host" );
		RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the take-back came back" );
	}

	[TestMethod]
	public void AGuestsPylonsAreTakenBackNewestFirstAsSinglePlayersBackspaceTakesThemOff()
	{
		var (host, guest, view, coaster, ride, seat, member) = UndoCoaster();
		var (twin, twinCoaster) = TwinCoaster();
		var twinView = ViewOf( twin );
		var hostCoaster = HostCoaster( host, coaster );
		GuestCoasterTool( host, guest, view, coaster );
		const int count = 4;
		GuestPylons( host, guest, view, count );
		twin.Enter();
		foreach ( var (dx, dy) in CoasterTests.Circuit.Take( count ) )
			twin.Level.CoasterClick( CoasterCell.X + dx, CoasterCell.Y + dy );
		Assert.AreEqual( CoasterOf( twin, twinCoaster ), CoasterOf( host, hostCoaster ), "the pylons placed as single player's" );
		var panel = guest.Level.CoasterPanel!;
		var records = panel.OwnPylonRecords.ToArray();
		Assert.AreEqual( count, records.Length, "each pylon the guest's own, named by its click's record" );
		Assert.AreEqual( (count, 0), host.Level.UndoJournal!.PylonEntriesOf( member ), "each pylon a step of the guest's" );
		// an older pylon is not the guest's last: refused, nothing changed
		var before = CoasterOf( host, hostCoaster );
		Assert.AreEqual( TakeBackVerdict.NotLast, VerdictOf( Asked( host, guest, new TakeBackCommand( false, records[0], -1, -1, -1 ) { Player = seat } ) ) );
		Assert.AreEqual( before, CoasterOf( host, hostCoaster ) );
		for ( var pylon = count; pylon > 0; pylon-- )
		{
			var balance = host.Economy.Balance;
			var twinBalance = twin.Economy.Balance;
			guest.Enter();
			HudBackspace( view );
			Assert.IsTrue( panel.IsWaiting, $"pylon {pylon}'s take-back went to the host" );
			if ( pylon == count )
			{
				view.Update();
				Assert.IsTrue( guest.Session!.GuestCommands!.Open.Single( request => request.Command is TakeBackCommand ).Command is TakeBackCommand
					{ Stroke: false, Step: var step, Part: -1, ClaimedRide: -1, Verdict: TakeBackVerdict.Undecided, Inverse: null } && step == records[^1],
					"a request names its pylon by its record alone: no ride, no inverse" );
			}
			RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the take-back came back" );
			twin.Enter();
			HudBackspace( twinView );
			Assert.AreEqual( CoasterOf( twin, twinCoaster ), CoasterOf( host, hostCoaster ), $"pylon {pylon} off as single player's Backspace takes it off" );
			Assert.AreEqual( (0L, 0L), (host.Economy.Balance - balance, twin.Economy.Balance - twinBalance), "no refund, as single player's" );
		}
		Assert.AreEqual( 0, panel.OwnPylonRecords.Count, "the panel's own pylons went off its list" );
		Assert.AreEqual( (0, 0), host.Level.UndoJournal.PylonEntriesOf( member ) );
		var takeBacks = TakeBacksOf( host, seat ).Where( command => command.Verdict == TakeBackVerdict.Taken ).ToArray();
		Assert.AreEqual( count, takeBacks.Length );
		Assert.IsTrue( takeBacks.All( command => command is { ClaimedRide: var claimed, Inverse: RemoveLastCoasterPylonCommand undo } && claimed == ride && undo.Station == ride ),
			"each decided on the coaster, its inverse the tool's own Backspace" );
		CollectionAssert.AreEqual( records.Reverse().ToArray(), takeBacks.Select( command => command.Step ).ToArray(), "newest first" );
		// with no pylon of its own the Backspace sends nothing and says so
		guest.Enter();
		HudBackspace( view );
		Assert.IsFalse( panel.IsWaiting );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopUndoNotOwn ) );
		Rounds( 5, host, guest );
		Assert.AreEqual( count + 1, TakeBacksOf( host, seat ).Length, "nothing more went to the host" );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )) );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( CoasterOf( host, hostCoaster ), CoasterOf( guest, coaster ) );
	}

	[TestMethod]
	public void TheHostsCoasterToolTakesItsOwnPylonsBackAndItsNextRunNoneOfTheEarlierOnes()
	{
		var (host, guest, view, coaster, ride, seat, member) = UndoCoaster();
		var hostView = ViewOf( host );
		var hostCoaster = HostCoaster( host, coaster );
		var builder = host.Level.GetCoasterBuilder( hostCoaster )!;
		var journal = host.Level.UndoJournal!;
		host.Enter();
		Assert.IsTrue( host.Level.EnterCoasterTool( hostCoaster ) );
		var panel = host.Level.CoasterPanel!;
		Assert.IsTrue( panel.TakesBack, "a shared park's Backspace is a take-back" );
		host.Level.CoasterTool!.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit.Take( 3 ) )
			host.Level.CoasterClick( CoasterCell.X + dx, CoasterCell.Y + dy );
		var records = panel.OwnPylonRecords.ToArray();
		Assert.AreEqual( 3, records.Length );
		Assert.AreEqual( (3, 0), journal.PylonEntriesOf( journal.Host ) );
		var nodes = builder.Track.Nodes.Count;
		var balance = host.Economy.Balance;
		HudBackspace( hostView );
		Assert.AreEqual( nodes - 1, builder.Track.Nodes.Count, "taken at once" );
		Assert.AreEqual( balance, host.Economy.Balance, "no refund, as single player's Backspace" );
		Assert.AreEqual( 2, panel.OwnPylonRecords.Count );
		var taken = TakeBacksOf( host, ParkCommands.LocalPlayer ).Single();
		Assert.AreEqual( (TakeBackVerdict.Taken, ride, records[^1]), (taken.Verdict, taken.ClaimedRide, taken.Step) );
		// the tool's run ends and its frame lets the claim go: the pylons left are tombstones, and the next run's Backspace finds no pylon of its own
		host.Level.ExitCoasterTool();
		hostView.Update();
		Assert.IsNull( host.Level.ClaimOf( ride ), "its frame let the claim go" );
		Assert.AreEqual( (0, 2), journal.PylonEntriesOf( journal.Host ) );
		Assert.IsTrue( host.Level.EnterCoasterTool( hostCoaster ) );
		nodes = builder.Track.Nodes.Count;
		HudBackspace( hostView );
		Assert.AreEqual( 1, hostView.Says( OpenTpwText.CoopUndoNotOwn ) );
		Assert.AreEqual( nodes, builder.Track.Nodes.Count );
		Assert.AreEqual( 1, TakeBacksOf( host, ParkCommands.LocalPlayer ).Length, "nothing went" );
		// a forged take-back of an earlier pylon, its claim held again since: changed, nothing changed
		var changed = host.Issue( new TakeBackCommand( false, records[1], -1, -1, -1 ) );
		Assert.AreEqual( TakeBackCommand.ChangedReason, changed.Reason );
		Assert.AreEqual( nodes, builder.Track.Nodes.Count );
		host.Level.ExitCoasterTool();
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void APylonsTakeBackIsInTheWayOnceItsToolClosedOrItsCoasterIsCompleteWhileItsClaimStays( bool complete )
	{
		var (host, guest, view, coaster, ride, seat, member) = UndoCoaster();
		var hostCoaster = HostCoaster( host, coaster );
		var (hostBuilder, builder) = Builders( host, guest, coaster );
		GuestCoasterTool( host, guest, view, coaster );
		GuestPylons( host, guest, view, complete ? CoasterTests.Circuit.Length : 3 );
		var records = guest.Level.CoasterPanel!.OwnPylonRecords.ToArray();
		// crafted requests, with no frame of the guest's HUD between them: its claim stays
		if ( complete )
		{
			var back = builder.Track.ReturnCell!.Value;
			Assert.IsTrue( Asked( host, guest, new CoasterClickCommand( ride, back.X, back.Y, 40, false, 0 ) { Player = seat } ).Accepted, "the circuit closed" );
			Assert.IsTrue( Asked( host, guest, new CoasterClickCommand( ride, back.X, back.Y, 40, false, 0 ) { Player = seat } ).Accepted, "and completed" );
			Assert.IsTrue( builder.Track.IsComplete && hostBuilder.Track.IsComplete );
		}
		else
			Assert.IsTrue( Asked( host, guest, new EndCoasterToolCommand( ride ) { Player = seat } ).Accepted, "the coaster left the tool" );
		Assert.IsFalse( hostCoaster.Runtime.Coaster!.BeingEdited || coaster.Runtime.Coaster!.BeingEdited );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )), "the claim stays the guest's" );
		Assert.AreEqual( (records.Length, 0), host.Level.UndoJournal!.PylonEntriesOf( member ), "its pylons are still its steps" );
		var before = CoasterOf( host, hostCoaster );
		var refused = Asked( host, guest, new TakeBackCommand( false, records[^1], -1, -1, -1 ) { Player = seat } );
		Assert.AreEqual( TakeBackVerdict.InTheWay, VerdictOf( refused ), "its inverse would refuse: the coaster is not in the tool, or its last pylon cannot go" );
		Assert.AreEqual( before, CoasterOf( host, hostCoaster ), "nothing changed" );
		Assert.AreEqual( complete, hostBuilder.Track.IsComplete && builder.Track.IsComplete );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( CoasterOf( host, hostCoaster ), CoasterOf( guest, coaster ) );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void APylonsTakeBackAfterANodeEditOrAStackSwapOfItsCoasterIsChangedAndAPylonPlacedSinceIsTaken( bool swap )
	{
		var (host, guest, view, coaster, ride, seat, member) = UndoCoaster();
		var hostCoaster = HostCoaster( host, coaster );
		var (hostBuilder, builder) = Builders( host, guest, coaster );
		GuestCoasterTool( host, guest, view, coaster );
		// four pylons round the station, and a fifth higher on the first one's cell: a stack of two
		guest.Level.CoasterTool!.Height = 20;
		foreach ( var (x, y) in new[] { (24, 20), (28, 24), (24, 28), (20, 24) } )
			GuestCoasterClick( host, guest, view, x, y );
		guest.Level.CoasterTool!.Height = 60;
		GuestCoasterClick( host, guest, view, 24, 20 );
		var panel = guest.Level.CoasterPanel!;
		Assert.AreEqual( 5, panel.OwnPylonRecords.Count );
		var (below, above) = (CoasterTrack.StationNodeCount, CoasterTrack.StationNodeCount + 4);
		Assert.AreEqual( (0, 1), (hostBuilder.Track.Nodes[below].StackIndex, hostBuilder.Track.Nodes[above].StackIndex) );
		guest.Enter();
		if ( swap )
		{
			Assert.IsTrue( panel.Select( CoasterToolButton.StackDown ) );
			panel.Release( above, null );
			Assert.IsTrue( panel.IsWaiting, "the swap went to the host" );
			RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the swap came back" );
			Assert.AreEqual( (1, 0), (hostBuilder.Track.Nodes[below].StackIndex, hostBuilder.Track.Nodes[above].StackIndex), "the pieces swapped" );
		}
		else
		{
			Assert.IsTrue( panel.Select( CoasterToolButton.Height ) );
			Assert.IsTrue( panel.Press( below, new N.Vector2( 500, 400 ), 24, 20 ) );
			RoundsUntil( () => panel.IsDragging, host, guest, view, "the press came back" );
			guest.Enter();
			Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( below, (24, 20) ) );
			RoundsUntil( () => guest.Level.CoasterRequests.IsEmpty && HostCommandsOf( host, seat ).Any( command => command is EndCoasterNodeEditCommand ), host, guest, view,
				"the drag's end came back" );
		}
		Assert.AreEqual( (0, 5), host.Level.UndoJournal!.PylonEntriesOf( member ), "the edit changed the chain under every pylon: tombstones" );
		// a pylon placed since is a step again, taken back
		guest.Enter();
		Assert.IsTrue( panel.Select( CoasterToolButton.Build ) );
		guest.Level.CoasterTool!.Height = 40;
		GuestCoasterClick( host, guest, view, 28, 16 );
		Assert.AreEqual( (1, 5), host.Level.UndoJournal.PylonEntriesOf( member ) );
		var nodes = hostBuilder.Track.Nodes.Count;
		GuestCoasterBackspace( host, guest, view );
		Assert.AreEqual( (nodes - 1, nodes - 1), (hostBuilder.Track.Nodes.Count, builder.Track.Nodes.Count), "the pylon placed since the edit taken off" );
		Assert.AreEqual( TakeBackVerdict.Taken, TakeBacksOf( host, seat ).Last().Verdict );
		// the pylons from before it are changed: the Backspace says so and keeps them
		var before = CoasterOf( host, hostCoaster );
		GuestCoasterBackspace( host, guest, view );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopUndoChanged ) );
		Assert.AreEqual( before, CoasterOf( host, hostCoaster ), "nothing changed" );
		Assert.AreEqual( 5, panel.OwnPylonRecords.Count, "the refused pylon stays the panel's" );
		Assert.AreEqual( (TakeBackVerdict.Changed, ride), (TakeBacksOf( host, seat ).Last().Verdict, TakeBacksOf( host, seat ).Last().ClaimedRide) );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )), "a refusal of the journal keeps the tool and the claim" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ACutBuriesTheCoastersPylonsAndAPylonPlacedInItsGapIsTakenBack()
	{
		var (host, guest, view, coaster, ride, seat, member) = UndoCoaster();
		var hostView = ViewOf( host );
		var hostCoaster = HostCoaster( host, coaster );
		var builder = host.Level.GetCoasterBuilder( hostCoaster )!;
		var journal = host.Level.UndoJournal!;
		host.Enter();
		Assert.IsTrue( host.Level.EnterCoasterTool( hostCoaster ) );
		host.Level.CoasterTool!.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			host.Level.CoasterClick( CoasterCell.X + dx, CoasterCell.Y + dy );
		var records = host.Level.CoasterPanel!.OwnPylonRecords.ToArray();
		var back = builder.Track.ReturnCell!.Value;
		host.Level.CoasterClick( back.X, back.Y );
		host.Level.CoasterClick( back.X, back.Y );
		Assert.IsTrue( builder.Track.IsComplete, "the circuit closed and completed" );
		Assert.IsNull( host.Level.CoasterPanel, "the completion ended the tool" );
		Assert.AreEqual( (CoasterTests.Circuit.Length, 0), journal.PylonEntriesOf( journal.Host ), "a close and a completion place no pylon; no frame of the host's let the claim go" );
		// the tool again, and its demolish button cuts the third pylon out
		Assert.IsTrue( host.Level.EnterCoasterTool( hostCoaster ) );
		var panel = host.Level.CoasterPanel!;
		Assert.IsTrue( panel.Select( CoasterToolButton.Demolish ) );
		var first = CoasterTrack.StationNodeCount + 1;
		var gap = (builder.Track.Nodes[first + 1].X, builder.Track.Nodes[first + 1].Y);
		Assert.AreEqual( CoasterPanelResult.CutStarted, panel.Release( first, null ) );
		Assert.AreEqual( CoasterPanelResult.Cut, panel.Release( first + 2, null ) );
		Assert.AreEqual( first, builder.Track.CutAfter );
		Assert.AreEqual( (0, CoasterTests.Circuit.Length), journal.PylonEntriesOf( journal.Host ), "the cut changed the chain under every pylon: tombstones" );
		// a pylon in the gap is a step, taken back as single player's Backspace takes the pylon before the cut off
		Assert.IsTrue( panel.Select( CoasterToolButton.Build ) );
		host.Level.CoasterClick( gap.X, gap.Y );
		Assert.AreEqual( first + 1, builder.Track.CutAfter, "the pylon went into the gap" );
		Assert.AreEqual( (1, CoasterTests.Circuit.Length), journal.PylonEntriesOf( journal.Host ) );
		HudBackspace( hostView );
		Assert.AreEqual( first, builder.Track.CutAfter, "taken off" );
		Assert.AreEqual( TakeBackVerdict.Taken, TakeBacksOf( host, ParkCommands.LocalPlayer ).Single().Verdict );
		// the pylons from before the cut: no pylon of this run's, and a forged take-back is changed
		var before = CoasterOf( host, hostCoaster );
		HudBackspace( hostView );
		Assert.AreEqual( 1, hostView.Says( OpenTpwText.CoopUndoNotOwn ) );
		Assert.AreEqual( TakeBackCommand.ChangedReason, host.Issue( new TakeBackCommand( false, records[first - CoasterTrack.StationNodeCount], -1, -1, -1 ) ).Reason );
		Assert.AreEqual( before, CoasterOf( host, hostCoaster ) );
		host.Level.ExitCoasterTool();
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AClaimLostWhileAPylonsTakeBackWaitsRefusesItInTheClaimsWordsAndEndsTheTool()
	{
		var (host, guest, view, coaster, ride, seat, member) = UndoCoaster();
		var hostCoaster = HostCoaster( host, coaster );
		GuestCoasterTool( host, guest, view, coaster );
		GuestPylons( host, guest, view, 3 );
		var before = CoasterOf( host, hostCoaster );
		// the guest's Backspace goes; before it reaches the host, the host lets the guest's claim go and takes the coaster
		guest.Enter();
		Assert.AreEqual( CoasterBackspace.Sent, guest.Level.SendCoasterBackspace() );
		host.Enter();
		Assert.IsTrue( host.Issue( new ReleaseRideCommand( ride ) ).Accepted );
		Assert.AreEqual( (0, 3), host.Level.UndoJournal!.PylonEntriesOf( member ), "the pylons are tombstones: the claim they need ended" );
		Assert.IsTrue( host.Issue( new ClaimRideCommand( ride ) ).Accepted );
		RoundsUntil( () => guest.Level.CoasterPanel == null, host, guest, view, "the take-back came back and ended the tool" );
		var refused = TakeBacksOf( host, seat ).Single();
		Assert.AreEqual( (TakeBackVerdict.Changed, ride), (refused.Verdict, refused.ClaimedRide), "decided on the tombstone's coaster" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimLost ) );
		Assert.AreEqual( before, CoasterOf( host, hostCoaster ), "nothing changed" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AClaimHandedOverLeavesTheOldHoldersPylonsToNoOne()
	{
		var (host, guest, view, coaster, ride, seat, member) = UndoCoaster();
		var hostView = ViewOf( host );
		var hostCoaster = HostCoaster( host, coaster );
		var builder = host.Level.GetCoasterBuilder( hostCoaster )!;
		GuestCoasterTool( host, guest, view, coaster );
		GuestPylons( host, guest, view, 3 );
		var records = guest.Level.CoasterPanel!.OwnPylonRecords.ToArray();
		// the guest's tool ends and its frame lets the claim go; the host's tool takes the coaster
		guest.Enter();
		guest.Level.ExitCoasterTool();
		RoundsUntil( () => host.Level.ClaimOf( ride ) == null && guest.Level.ClaimOf( ride ) == null, host, guest, view, "the frame check let the claim go" );
		Assert.AreEqual( (0, 3), host.Level.UndoJournal!.PylonEntriesOf( member ) );
		host.Enter();
		Assert.IsTrue( host.Level.EnterCoasterTool( hostCoaster ) );
		var nodes = builder.Track.Nodes.Count;
		// the new holder's Backspace takes back nothing of the old holder's: no pylon of its own, nothing sent
		HudBackspace( hostView );
		Assert.AreEqual( 1, hostView.Says( OpenTpwText.CoopUndoNotOwn ) );
		Assert.AreEqual( 0, TakeBacksOf( host, ParkCommands.LocalPlayer ).Length );
		// a forged take-back of the guest's pylon by the host is no step of the host's
		Assert.AreEqual( TakeBackCommand.NotOwnReason, host.Issue( new TakeBackCommand( false, records[^1], -1, -1, -1 ) ).Reason );
		// and the guest's own, the claim another's now, is refused in the claim's words through its tombstone's coaster
		var lost = Asked( host, guest, new TakeBackCommand( false, records[^1], -1, -1, -1 ) { Player = seat } );
		Assert.AreEqual( CoopBuildRights.ClaimLostReason, lost.Result?.Reason );
		Assert.AreEqual( ride, TakeBacksOf( host, seat ).Last().ClaimedRide );
		Assert.AreEqual( nodes, builder.Track.Nodes.Count, "nothing changed" );
		host.Level.ExitCoasterTool();
		CatchUpAndCompare( host, guest );
	}
}
