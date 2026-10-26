using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-claims] A guest's track tool in a shared park (docs/COOP.md, "Claims"; C3c-1b): the track button and a placed kart or water ride claim the ride through the host, and the
/// tool starts from the claim's outcome (an open circuit's line tool from a second request's walk); its lines, its cut and its Backspace go to the host one at a time and continue the tool from
/// their outcomes, the click's Ctrl included; the tool ends with a note once the host let its claim go, and with the session.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>The cell (<paramref name="x"/>, <paramref name="y"/>) cells from the first tile of <paramref name="kart"/>'s station in <paramref name="park"/>.</summary>
	private static (int X, int Y) KartAt( Park park, OriginalObject kart, int x, int y )
	{
		var first = park.Level.TrackStations[kart].First;
		return (first.X + x, first.Y + y);
	}

	/// <summary>The loop's three corners from the station's exit (a closed circuit of a kart ride turned 0): the last click closes it.</summary>
	private static readonly (int X, int Y)[] KartLoop = { (4, 6), (-2, 6), (-2, 0) };

	/// <summary>A shared park with a kart ride on both parks, the guest granted; the guest's kart, its attraction id and the guest's seat.</summary>
	private (Park Host, Park Guest, View View, OriginalObject Kart, int Ride, int Seat) SharedKart( bool hasQueue = false )
	{
		var (host, guest, _, _) = Shared( ( park, _ ) => PlaceKartRide( park, hasQueue: hasQueue ) );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var kart = guest.Level.TrackStations.Keys.Single();
		return (host, guest, view, kart, kart.Runtime.AttractionId, guest.Level.Commands.Player);
	}

	/// <summary>The guest's track button on <paramref name="kart"/>: the claim goes to the host, and the tool starts once it is back (an open circuit's line tool after the walk's request).</summary>
	private void GuestTrackTool( Park host, Park guest, View view, OriginalObject kart )
	{
		guest.Enter();
		var starts = guest.Level.HostToolStarts;
		Assert.AreEqual( TrackToolEntry.Pending, guest.Level.BeginTrackTool( kart, placed: false ), "the claim goes to the host" );
		Assert.IsNull( guest.Level.TrackToolStation, "no tool until it is back" );
		RoundsUntil( () => guest.Level.TrackToolStation == kart, host, guest, view, "the tool started from the outcome" );
		Assert.AreEqual( starts + 1, guest.Level.HostToolStarts, "a press made before it started gives it no click" );
	}

	/// <summary>The guest's line click on (<paramref name="cell"/>): it goes to the host, the tool waits with no other click, and its outcome continues the tool.</summary>
	private SegmentResult GuestTrackLine( Park host, Park guest, View view, (int X, int Y) cell, int modifiers = 0 )
	{
		guest.Enter();
		var tool = guest.Level.CellTool;
		var click = guest.Level.ClickCellTool( cell, modifiers );
		Assert.IsNotNull( click?.Waiting, $"the line to {cell} goes to the host" );
		Assert.IsTrue( tool.IsWaiting );
		var waits = guest.Level.WaitingClicks;
		Assert.IsNull( guest.Level.ClickCellTool( cell ), "no other click while it waits" );
		Assert.AreEqual( waits + 1, guest.Level.WaitingClicks );
		Assert.IsFalse( tool.Undo(), "nor a Backspace" );
		var starts = guest.Level.HostToolStarts;
		RoundsUntil( () => !tool.IsWaiting, host, guest, view, "the line came back" );
		Assert.AreEqual( starts + 1, guest.Level.HostToolStarts, "a press made while it waited is no click of the tool its outcome moved on" );
		return tool.LastCommit!;
	}

	[TestMethod]
	public void AGuestsTrackButtonClaimsTheRideThroughTheHostAndItsLinesCloseTheCircuit()
	{
		var (host, guest, view, kart, ride, seat) = SharedKart();
		GuestTrackTool( host, guest, view, kart );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )), "the claim is the guest's on both parks" );
		Assert.AreEqual( CellToolMode.KartTrack, guest.Level.CellTool.Mode, "the open circuit's line tool" );
		Assert.AreEqual( KartAt( guest, kart, 4, 0 ), guest.Level.CellTool.Start, "from the walk's open end, the station's exit" );
		CollectionAssert.AreEqual( new[] { typeof( ClaimRideCommand ), typeof( OpenTrackLineToolCommand ) },
			HostCommandsOf( host, seat ).Where( command => command is ClaimRideCommand or OpenTrackLineToolCommand ).Select( command => command.GetType() ).ToArray() );
		Assert.AreEqual( 1, view.Hud.Messages.Count( message => message == ParkHud.TrackToolHelp ), "the tool's help, as the host's button posts it" );
		var hostKart = host.Level.TrackStations.Keys.Single();
		foreach ( var (x, y) in KartLoop )
			GuestTrackLine( host, guest, view, KartAt( guest, kart, x, y ) );
		Assert.IsTrue( hostKart.Runtime.Bump!.IsTrackValid && kart.Runtime.Bump!.IsTrackValid, "the last line closed the circuit on both parks" );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "the closed circuit ended the tool" );
		Assert.AreEqual( 3, HostCommandsOf( host, seat ).Count( command => command is LayTrackLineCommand ) );
		RoundsUntil( () => host.Level.ClaimOf( ride ) == null && guest.Level.ClaimOf( ride ) == null, host, guest, view, "the frame check let the claim go" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsCloseWithExactlyCtrlKeepsItsLineToolAndItsClaim()
	{
		var (host, guest, view, kart, ride, seat) = SharedKart();
		GuestTrackTool( host, guest, view, kart );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, KartLoop[0].X, KartLoop[0].Y ) );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, KartLoop[1].X, KartLoop[1].Y ) );
		// the click's Ctrl goes with its line: the outcome's continuation reads it, as the host's click's continuation does
		GuestTrackLine( host, guest, view, KartAt( guest, kart, KartLoop[2].X, KartLoop[2].Y ), 0xc );
		Assert.IsTrue( kart.Runtime.Bump!.IsTrackValid );
		Assert.AreEqual( CellToolMode.KartTrack, guest.Level.CellTool.Mode, "no queue start is pending: exactly Ctrl keeps the line tool" );
		Assert.IsNull( guest.Level.CellTool.Start );
		Assert.AreSame( kart, guest.Level.TrackToolStation );
		Rounds( 120, host, guest );
		view.Update();
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )), "the tool is still on the ride: the claim stays" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsBackspaceWaitsForTheHostAndTakesItsLastLineBack()
	{
		var (host, guest, view, kart, ride, seat) = SharedKart();
		GuestTrackTool( host, guest, view, kart );
		var builder = guest.Level.BuilderOf( kart )!;
		var tiles = builder.Tiles().Count();
		var tool = guest.Level.CellTool;
		var line = GuestTrackLine( host, guest, view, KartAt( guest, kart, KartLoop[0].X, KartLoop[0].Y ) );
		Assert.IsTrue( builder.Tiles().Count() > tiles );
		Assert.AreEqual( 1, tool.SegmentCount );
		// Backspace goes to the host: nothing is taken back until its outcome, and the tool takes no click nor another Backspace meanwhile
		guest.Enter();
		Assert.IsTrue( tool.Undo() );
		Assert.IsNotNull( tool.WaitingUndo );
		Assert.AreEqual( 1, tool.SegmentCount, "nothing popped yet" );
		Assert.IsFalse( tool.Undo(), "one at a time" );
		var waits = guest.Level.WaitingClicks;
		Assert.IsNull( guest.Level.ClickCellTool( KartAt( guest, kart, 4, 2 ) ) );
		Assert.AreEqual( waits + 1, guest.Level.WaitingClicks );
		var starts = guest.Level.HostToolStarts;
		RoundsUntil( () => tool.WaitingUndo == null, host, guest, view, "the Backspace came back" );
		Assert.AreEqual( starts + 1, guest.Level.HostToolStarts, "nor of the tool the Backspace's outcome moved on" );
		Assert.AreEqual( 0, tool.SegmentCount, "the line went off the stack" );
		Assert.AreEqual( line.Start, tool.Start, "the tool goes on from the line's start" );
		Assert.AreEqual( tiles, builder.Tiles().Count(), "and its tiles off the track" );
		Assert.AreEqual( tiles, host.Level.BuilderOf( host.Level.TrackStations.Keys.Single() )!.Tiles().Count() );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is UndoTrackLineCommand ) );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopGuestUndo ), "a track tool's Backspace is the guest's under its claim" );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsEditToolCutsAClosedCircuitThroughTheHostAndGoesOnAsTheLineTool()
	{
		var (host, guest, view, kart, ride, seat) = SharedKart();
		var hostView = ViewOf( host );
		var hostKart = host.Level.TrackStations.Keys.Single();
		// the host lays the closed circuit, and its frame lets the claim go
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( hostKart ) );
		foreach ( var (x, y) in KartLoop )
			Assert.IsTrue( host.Level.CellTool.Click( KartAt( host, hostKart, x, y ) ) is { Completed: true } );
		Assert.IsTrue( hostKart.Runtime.Bump!.IsTrackValid );
		hostView.Update();
		RoundsUntil( () => host.Level.ClaimOf( ride ) == null && guest.Level.ClaimOf( ride ) == null && kart.Runtime.Bump!.IsTrackValid, host, guest, view,
			"the circuit and the release reached the guest" );
		// the guest's button: the claim's outcome opens the edit tool at once on the closed circuit, with no walk to request
		GuestTrackTool( host, guest, view, kart );
		var tool = guest.Level.CellTool;
		Assert.AreEqual( CellToolMode.KartEdit, tool.Mode );
		Assert.AreEqual( 0, HostCommandsOf( host, seat ).Count( command => command is OpenTrackLineToolCommand ) );
		guest.Enter();
		Assert.IsNull( tool.Click( KartAt( guest, kart, -1, 5 ) ) );
		Assert.AreEqual( KartAt( guest, kart, -1, 5 ), tool.Start, "the first click picks a tile, here" );
		// the second click's cut goes to the host: the tool waits with the cut as its ghost
		var cut = guest.Level.ClickCellTool( KartAt( guest, kart, -2, 2 ) );
		Assert.IsNotNull( cut?.Waiting );
		Assert.IsTrue( tool.IsWaiting && tool.Ghost is { Line.Count: > 0 } );
		Assert.IsTrue( kart.Runtime.Bump!.IsTrackValid, "nothing cut yet" );
		RoundsUntil( () => !tool.IsWaiting, host, guest, view, "the cut came back" );
		Assert.IsFalse( hostKart.Runtime.Bump!.IsTrackValid || kart.Runtime.Bump!.IsTrackValid, "the cut opened the circuit on both parks" );
		Assert.AreEqual( CellToolMode.KartTrack, tool.Mode, "the tool goes on as the line tool from an open end" );
		Assert.AreSame( kart, guest.Level.TrackToolStation );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is CutTrackCommand ) );
		Rounds( 60, host, guest );
		view.Update();
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )), "the edit tool's turn into the line tool keeps the claim" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsPlacedKartRideOpensItsTrackToolOnceItsClaimIsBackAndItsCloseHandsOverToTheQueueTool()
	{
		var (host, guest, view, kart, ride, seat) = SharedKart( hasQueue: true );
		foreach ( var park in new[] { host, guest } )
		{
			var item = park.Level.TrackStations.Keys.Single();
			item.Visitors.HasCells = true;
			item.Visitors.QueueFrontCell = KartAt( park, item, 0, -2 );
		}
		guest.Enter();
		// the placement's follow-on waits for the claim, and no queue tool follows the placement meanwhile
		Assert.AreEqual( TrackToolEntry.Pending, guest.Level.EnterPlacedTrackTool( kart ) );
		Assert.IsFalse( guest.Level.CellTool.IsActive );
		RoundsUntil( () => guest.Level.TrackToolStation == kart, host, guest, view, "the tool started from the outcome" );
		foreach ( var (x, y) in KartLoop )
			GuestTrackLine( host, guest, view, KartAt( guest, kart, x, y ) );
		Assert.IsTrue( kart.Runtime.Bump!.IsTrackValid );
		Assert.AreEqual( CellToolMode.Queue, guest.Level.CellTool.Mode, "the placement's queue start, pending since the tool started, takes the queue tool" );
		Assert.AreEqual( KartAt( guest, kart, 0, -2 ), guest.Level.CellTool.Start );
		Assert.IsNull( guest.Level.TrackToolStation );
		RoundsUntil( () => host.Level.ClaimOf( ride ) == null && guest.Level.ClaimOf( ride ) == null, host, guest, view, "the track tool left the ride: the claim goes" );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is ReleaseRideCommand ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsPlacementOfARideAnotherPlayerClaimedOpensNoToolNotEvenItsQueueTool()
	{
		var (host, guest, view, kart, ride, _) = SharedKart( hasQueue: true );
		foreach ( var park in new[] { host, guest } )
		{
			var item = park.Level.TrackStations.Keys.Single();
			item.Visitors.HasCells = true;
			item.Visitors.QueueFrontCell = KartAt( park, item, 0, -2 );
		}
		// the host claims the ride before the guest's HUD finishes its placement
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( host.Level.TrackStations.Keys.Single() ) );
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == ParkCommands.LocalPlayer, host, guest, view, "the host's claim reached the guest" );
		guest.Enter();
		typeof( ParkHud ).GetField( "pendingItem", NonPublic )!.SetValue( view.Hud,
			new BuildItem( "karts", kart.Entry.InfoId, BuildCategory.Rides, 0, 0, null, Array.Empty<string>(), Entry: kart.Entry ) );
		typeof( ParkHud ).GetMethod( "FinishPlacement", NonPublic )!.Invoke( view.Hud, new object[] { kart, false } );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "no track tool, and no queue tool for a ride whose queue is the host's while its track tool is on it" );
		Assert.IsNull( guest.Level.WaitingToolStart );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimed ) );
		// unclaimed, the placement's follow-on waits for the guest's claim: no queue tool meanwhile either
		host.Enter();
		host.Level.ExitTrackTool();
		ViewOf( host ).Update();
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == null, host, guest, view, "the host let it go" );
		guest.Enter();
		typeof( ParkHud ).GetField( "pendingItem", NonPublic )!.SetValue( view.Hud,
			new BuildItem( "karts", kart.Entry.InfoId, BuildCategory.Rides, 0, 0, null, Array.Empty<string>(), Entry: kart.Entry ) );
		typeof( ParkHud ).GetMethod( "FinishPlacement", NonPublic )!.Invoke( view.Hud, new object[] { kart, false } );
		Assert.IsFalse( guest.Level.CellTool.IsActive );
		Assert.IsInstanceOfType( guest.Level.WaitingToolStart?.Command, typeof( ClaimRideCommand ) );
		RoundsUntil( () => guest.Level.TrackToolStation == kart, host, guest, view, "the track tool started from the claim's outcome" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsTrackToolEndsWithANoteWhenTheHostLetsItsIdleClaimGo()
	{
		var (host, guest, view, kart, ride, seat) = SharedKart();
		GuestTrackTool( host, guest, view, kart );
		// five minutes without a command of the guest on the ride: the host lets the claim go
		HostWaits( host, 301, step: 1 );
		Assert.IsNull( host.Level.ClaimOf( ride ) );
		Assert.AreEqual( 1, HostReleases( host, ride ) );
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == null, host, guest, view, "the release reached the guest" );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "the guest's tool ended" );
		Assert.IsNull( guest.Level.TrackToolStation );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimLost ) );
		// its next entry claims again
		GuestTrackTool( host, guest, view, kart );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsTrackLineRefusedForWantOfItsClaimEndsTheToolInTheClaimsWords()
	{
		var (host, guest, view, kart, ride, seat) = SharedKart();
		GuestTrackTool( host, guest, view, kart );
		var tiles = guest.Level.BuilderOf( kart )!.Tiles().Count();
		// the host lets the claim go while the guest's line is on its way: the line is refused at its record, for want of the claim
		guest.Enter();
		Assert.IsNotNull( guest.Level.ClickCellTool( KartAt( guest, kart, 4, 6 ) )?.Waiting );
		host.Enter();
		Assert.IsTrue( host.Issue( new ReleaseRideCommand( ride ) ).Accepted );
		RoundsUntil( () => !guest.Level.CellTool.IsWaiting, host, guest, view, "the line came back" );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "the refused line ended the tool" );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is LayTrackLineCommand ), "it reached the host" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimLost ), "in the claim's words, once" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopHostOnly ) );
		Assert.AreEqual( tiles, guest.Level.BuilderOf( kart )!.Tiles().Count(), "nothing laid" );
		Assert.IsFalse( kart.Runtime.Bump!.IsTrackValid );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsEntryOfARideAnotherPlayerClaimsSaysSoAndTheLatestEntryWins()
	{
		var (host, guest, view, kart, ride, seat) = SharedKart();
		var hostView = ViewOf( host );
		var hostKart = host.Level.TrackStations.Keys.Single();
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( hostKart ) );
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == ParkCommands.LocalPlayer, host, guest, view, "the host's claim reached the guest" );
		// the window offers the guest no track button for it, and an entry starts nothing, in the claim's words
		Assert.IsFalse( (bool)typeof( ParkHud ).GetMethod( "CanEditTrack", NonPublic )!.Invoke( view.Hud, new object[] { kart } )! );
		guest.Enter();
		Assert.AreEqual( TrackToolEntry.Refused, guest.Level.BeginTrackTool( kart, placed: false ) );
		Assert.IsNull( guest.Level.WaitingToolStart );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimed ) );
		// the host leaves the ride, and its frame lets the claim go; the guest's window offers the button again
		host.Enter();
		host.Level.ExitTrackTool();
		hostView.Update();
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == null, host, guest, view, "the host let it go" );
		Assert.IsTrue( (bool)typeof( ParkHud ).GetMethod( "CanEditTrack", NonPublic )!.Invoke( view.Hud, new object[] { kart } )! );
		// two entries before the first is back: the first's outcome starts nothing, the latest's starts the tool
		guest.Enter();
		Assert.AreEqual( TrackToolEntry.Pending, guest.Level.BeginTrackTool( kart, placed: false ) );
		var first = guest.Level.WaitingToolStart;
		Assert.AreEqual( TrackToolEntry.Pending, guest.Level.BeginTrackTool( kart, placed: false ) );
		Assert.AreNotSame( first, guest.Level.WaitingToolStart );
		RoundsUntil( () => guest.Level.TrackToolStation == kart, host, guest, view, "the latest entry's tool started" );
		Assert.AreEqual( 2, HostCommandsOf( host, seat ).Count( command => command is ClaimRideCommand ) );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is OpenTrackLineToolCommand ), "the first claim's outcome sent no open" );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheSessionsEndEndsAGuestsTrackTool()
	{
		var (host, guest, view, kart, _, _) = SharedKart();
		GuestTrackTool( host, guest, view, kart );
		guest.Enter();
		Assert.IsNotNull( guest.Level.ClickCellTool( KartAt( guest, kart, 4, 6 ) )?.Waiting );
		guest.Level.EndGuestWaits();
		Assert.IsFalse( guest.Level.CellTool.IsActive );
		Assert.IsNull( guest.Level.TrackToolStation );
		Assert.IsFalse( guest.Level.CellTool.IsWaiting );
	}

	[TestMethod]
	public void AGuestsWindowOffersAKartRidesTrackButtonAndATrackCommandNeedsTheClaim()
	{
		var (_, guest, view, kart, _, _) = SharedKart();
		var canEdit = typeof( ParkHud ).GetMethod( "CanEditTrack", NonPublic )!;
		Assert.IsTrue( (bool)canEdit.Invoke( view.Hud, new object[] { kart } )!, "a kart ride nobody claims" );
		var rights = (CoopBuildRights)guest.Level.Commands.Rights!;
		Assert.IsTrue( rights.Allows( guest.Level.Commands.Player, new ClaimRideCommand( kart.Runtime.AttractionId ) ) );
		Assert.IsFalse( rights.Allows( guest.Level.Commands.Player, new LayTrackLineCommand( kart.Runtime.AttractionId, 1, 1, 3, 1 ) ), "a track command needs the claim" );
		Assert.AreEqual( CoopBuildRights.ClaimLostReason, rights.RefusalReason( guest.Level.Commands.Player, new LayTrackLineCommand( kart.Runtime.AttractionId, 1, 1, 3, 1 ) ) );
		Assert.AreEqual( CoopRefusal.ClaimLost, CoopBuildRights.RefusalOf( CoopBuildRights.ClaimLostReason ) );
	}
}
