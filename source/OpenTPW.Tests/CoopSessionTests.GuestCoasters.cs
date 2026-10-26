using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-claims] A guest's coaster tool in a shared park (docs/COOP.md, "A guest's coaster tool (C3c-2)"): the track button and a placed coaster claim the coaster through the host, and
/// the panel opens from the tool's entry, a second request; its clicks, Backspace, swap and cut wait for the host one at a time, and its drags go through the level's queue, a field's latest value
/// at most every 0.2 s, behind their press and before their end; the claim's end ends the coaster's tool state on every peer, so a guest that leaves, idles or resyncs never leaves a coaster closed
/// and marked.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>The cell of the coaster's station the fixtures place (<see cref="PlaceCoaster"/>): its pylons go at the offsets of <see cref="CoasterTests.Circuit"/> from it.</summary>
	private static readonly (int X, int Y) CoasterCell = (20, 20);

	/// <summary>
	/// A coaster of the asset-free park: a synthetic coaster type with the PC coasters' pylons (<see cref="CoasterTests.PcSettings"/>) placed on <see cref="CoasterCell"/>, its station's ends set
	/// beside it as the coaster tests set theirs (<see cref="CoasterTests.PlaceStation"/>), no pylon yet. Run on every park alike before the session starts (<see cref="Shared"/>).
	/// </summary>
	private OriginalObject PlaceCoaster( Park park )
	{
		var level = park.Level;
		// the park is built without its field initializers: the coaster builders the tool keeps
		var builders = typeof( Level ).GetField( "coasterBuilders", NonPublic )!;
		if ( builders.GetValue( level ) == null )
			builders.SetValue( level, new Dictionary<OriginalObject, CoasterBuilder>() );
		var root = (string)typeof( UpgradeCatalogueDefaultsTests ).GetField( "root", NonPublic )!.GetValue( fixture )!;
		var pylons = string.Join( "\n", CoasterTests.PcSettings().Select( pair => $"{pair.Key} {pair.Value}" ) );
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/coaster", new ObjectSettings( new[] { new ObjectSettingsFile(
				$"Info.Id 1160\nInfo.Name SyntheticCoaster\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nBumper.WhichTrackType {CoasterDefinition.TrackType}\n{pylons}\n", "coaster.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(),
			new BaseFileSystem( root ), null );
		var station = level.Objects.TryPlace( entry, CoasterCell.X, CoasterCell.Y, 0, out _ );
		Assert.IsNotNull( station?.Runtime.Coaster, "the coaster's station and its ride" );
		CoasterTests.PlaceStation( level.GetCoasterBuilder( station! )!, CoasterCell );
		return station!;
	}

	/// <summary>A shared park with a coaster on both parks, the guest granted; the guest's coaster, its attraction id and the guest's seat.</summary>
	private (Park Host, Park Guest, View View, OriginalObject Coaster, int Ride, int Seat) SharedCoaster()
	{
		var (host, guest, _, _) = Shared( ( park, _ ) => PlaceCoaster( park ) );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var coaster = guest.Level.Objects.Objects.Single( item => item.Runtime.Coaster != null );
		return (host, guest, view, coaster, coaster.Runtime.AttractionId, guest.Level.Commands.Player);
	}

	/// <summary>The guest's track button on <paramref name="coaster"/>: the claim goes to the host, then the tool's entry, and the panel opens once that is back.</summary>
	private void GuestCoasterTool( Park host, Park guest, View view, OriginalObject coaster )
	{
		guest.Enter();
		var starts = guest.Level.HostToolStarts;
		Assert.AreEqual( TrackToolEntry.Pending, guest.Level.BeginCoasterToolEntry( coaster ), "the claim goes to the host" );
		Assert.IsNull( guest.Level.CoasterPanel, "no panel until the entry is back" );
		RoundsUntil( () => guest.Level.CoasterPanel != null, host, guest, view, "the panel opened from the entry's outcome" );
		Assert.AreSame( coaster, guest.Level.CoasterToolStation );
		Assert.AreEqual( starts + 1, guest.Level.HostToolStarts, "a press made before it opened gives it no click" );
	}

	/// <summary>The guest's build click on (<paramref name="x"/>, <paramref name="y"/>): it goes to the host, the panel waits with no other click, and its outcome comes back.</summary>
	private void GuestCoasterClick( Park host, Park guest, View view, int x, int y, bool closing = false )
	{
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( guest.Level.SendCoasterClick( x, y, closing ), $"the click on ({x}, {y}) goes to the host" );
		Assert.IsTrue( panel.IsWaiting );
		var waits = guest.Level.WaitingClicks;
		Assert.IsFalse( guest.Level.SendCoasterClick( x, y, closing ), "no other click while it waits" );
		Assert.IsFalse( guest.Level.SendCoasterBackspace(), "nor a Backspace" );
		Assert.IsFalse( panel.Select( CoasterToolButton.Height ), "nor a button" );
		Assert.AreEqual( waits + 2, guest.Level.WaitingClicks );
		RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the click came back" );
	}

	/// <summary>The host's twin of the guest's <paramref name="coaster"/>.</summary>
	private static OriginalObject HostCoaster( Park host, OriginalObject coaster ) => host.Level.ResolveCommandObject( coaster.Runtime.AttractionId )!;

	/// <summary>The coaster builders of <paramref name="coaster"/> on both parks.</summary>
	private static (CoasterBuilder Host, CoasterBuilder Guest) Builders( Park host, Park guest, OriginalObject coaster ) =>
		(host.Level.GetCoasterBuilder( HostCoaster( host, coaster ) )!, guest.Level.GetCoasterBuilder( coaster )!);

	/// <summary>
	/// The tick of the first checkpoint of the session's <paramref name="log"/> a replay misses on a new park <paramref name="prepare"/>d as the session's parks were, under the session's rights; null
	/// when every checkpoint matches.
	/// </summary>
	private long? FirstReplayMismatch( ParkCommandLogFile log, Action<Park> prepare )
	{
		var twin = NewPark();
		twin.Enter();
		prepare( twin );
		Assert.AreEqual( (log.StartTick, log.StartHash), (twin.Level.TickCount, twin.Level.ComputeStateHash()), "the twin is the session's starting park" );
		var hashes = new Dictionary<long, ulong>();
		ParkCommandReplay.Run( twin.Level.Commands, log, () => twin.Level.Commands.NextTick, () => twin.Level.RunTicks( 1 ), log.Checkpoints[^1].Ticks,
			ticks => hashes[ticks] = twin.Level.ComputeStateHash(), new CoopBuildRights( twin.Level ) );
		hashes[log.Checkpoints[^1].Ticks] = twin.Level.ComputeStateHash();
		return ParkCommandReplay.FirstMismatch( log, hashes )?.Ticks;
	}

	/// <summary>The guest's pylons of <see cref="CoasterTests.Circuit"/>, each click back before the next.</summary>
	private void GuestPylons( Park host, Park guest, View view, int count = 6 )
	{
		guest.Level.CoasterTool!.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit.Take( count ) )
			GuestCoasterClick( host, guest, view, CoasterCell.X + dx, CoasterCell.Y + dy );
	}

	[TestMethod]
	public void AGuestsCoasterToolClaimsTheCoasterAndOpensFromTheHostsAnswers()
	{
		var (host, guest, view, coaster, ride, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var hostCoaster = HostCoaster( host, coaster );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )), "the claim is the guest's on both parks" );
		Assert.IsTrue( hostCoaster.Runtime.Coaster!.BeingEdited && coaster.Runtime.Coaster!.BeingEdited, "the entry closed the coaster on both parks" );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.ClaimRide, ParkCommandKind.BeginCoasterTool }, HostCommandsOf( host, seat ).Select( command => command.Kind ).ToArray() );
		Assert.AreEqual( 1, view.Hud.Messages.Count( message => message == view.Strings.Help( 0x125 ) ), "the tool's help, as the host's button posts it" );
		// the host's own entry is refused on the guest's coaster, in the claim's words, and so are its forged commands
		host.Enter();
		Assert.AreEqual( TrackToolEntry.Refused, host.Level.BeginCoasterToolEntry( hostCoaster ) );
		Assert.IsNull( host.Level.CoasterPanel );
		Assert.AreEqual( Level.ClaimedReason, host.Issue( new EndCoasterToolCommand( ride ) ).Reason );
		Assert.AreEqual( Level.ClaimedReason, host.Issue( new CoasterClickCommand( ride, 24, 20, 40, false, 0 ) ).Reason );
		Assert.IsTrue( coaster.Runtime.Coaster.BeingEdited );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsClicksBuildTheCircuitOneAtATimeAndItsCompletionEndsTheToolAndTheClaim()
	{
		var (host, guest, view, coaster, ride, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var (hostBuilder, builder) = Builders( host, guest, coaster );
		var starts = guest.Level.HostToolStarts;
		GuestPylons( host, guest, view );
		Assert.AreEqual( starts + CoasterTests.Circuit.Length, guest.Level.HostToolStarts, "each placing click moved the panel on: a press made while it waited is no click" );
		Assert.AreEqual( CoasterTrack.StationNodeCount + CoasterTests.Circuit.Length, builder.Track.Nodes.Count );
		Assert.AreEqual( builder.Track.Nodes.Count, hostBuilder.Track.Nodes.Count, "the host placed them" );
		// a click the park refuses comes back with its reason, as the host's click says it at once
		GuestCoasterClick( host, guest, view, 200, 200 );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CannotBuildHere ) );
		var back = builder.Track.ReturnCell!.Value;
		GuestCoasterClick( host, guest, view, back.X, back.Y, closing: true );
		Assert.IsTrue( builder.Track.IsClosed && hostBuilder.Track.IsClosed, "the chain start closed the circuit" );
		GuestCoasterClick( host, guest, view, back.X, back.Y );
		Assert.IsTrue( builder.Track.IsComplete && hostBuilder.Track.IsComplete, "the next click completed the coaster" );
		Assert.IsNull( guest.Level.CoasterPanel, "the completion finished the tool" );
		Assert.IsFalse( coaster.Runtime.Coaster!.BeingEdited || HostCoaster( host, coaster ).Runtime.Coaster!.BeingEdited );
		Assert.AreEqual( CoasterTests.Circuit.Length + 3, HostCommandsOf( host, seat ).Count( command => command is CoasterClickCommand ) );
		RoundsUntil( () => host.Level.ClaimOf( ride ) == null && guest.Level.ClaimOf( ride ) == null, host, guest, view, "the frame check let the claim go" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsBackspaceTakesTheLastPylonOffThroughTheHostOneAtATime()
	{
		var (host, guest, view, coaster, ride, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var (hostBuilder, builder) = Builders( host, guest, coaster );
		GuestPylons( host, guest, view, 3 );
		var pylons = builder.Track.Nodes.Count;
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( guest.Level.SendCoasterBackspace() );
		Assert.IsTrue( panel.IsWaiting );
		Assert.AreEqual( pylons, builder.Track.Nodes.Count, "nothing taken off yet" );
		var waits = guest.Level.WaitingClicks;
		Assert.IsFalse( guest.Level.SendCoasterBackspace(), "one at a time" );
		Assert.AreEqual( waits + 1, guest.Level.WaitingClicks );
		var starts = guest.Level.HostToolStarts;
		RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the Backspace came back" );
		Assert.AreEqual( starts + 1, guest.Level.HostToolStarts, "it moved the panel on" );
		Assert.AreEqual( (pylons - 1, pylons - 1), (builder.Track.Nodes.Count, hostBuilder.Track.Nodes.Count), "the last pylon went on both parks" );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is RemoveLastCoasterPylonCommand ) );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsHeightDragGoesInAFewThrottledStepsAndSettlesAtItsLastValue()
	{
		var (host, guest, view, coaster, ride, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var (hostBuilder, builder) = Builders( host, guest, coaster );
		GuestPylons( host, guest, view, 2 );
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( panel.Select( CoasterToolButton.Height ) );
		var node = CoasterTrack.StationNodeCount;
		var start = builder.Track.Nodes[node].T;
		var (low, high) = builder.Track.LoftRange( builder.Site, node );
		// the press goes to the host (the pointer at (500, 400) of the original's screen, which is twice the views' canvas): the drag starts from its outcome, and the pointer's moves
		// meanwhile are kept
		Assert.IsTrue( panel.Press( node, new N.Vector2( 500, 400 ), CoasterCell.X + 4, CoasterCell.Y ) );
		Assert.IsTrue( panel.PressWaits && panel.IsWaiting && !panel.IsDragging );
		hudPointer = new N.Vector2( 1000, 780 );
		RoundsUntil( () => panel.IsDragging, host, guest, view, "the press came back and the drag started" );
		// sixty frames of pointer moves the HUD follows: each frame a new height, a step at most every 0.2 s
		for ( var frame = 1; frame <= 60; frame++ )
		{
			hudPointer = new N.Vector2( 1000, 780 - 2 * frame );
			Rounds( 1, host, guest );
			guest.Session!.Step();
			view.Update();
		}
		var last = Math.Clamp( start + 70 * CoasterToolPanel.HeightRate, low, high );
		guest.Enter();
		Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( node, (CoasterCell.X + 4, CoasterCell.Y) ) );
		Assert.IsFalse( panel.IsDragging, "the drag ended at once here" );
		RoundsUntil( () => guest.Level.CoasterRequests.IsEmpty && HostCommandsOf( host, seat ).Any( command => command is EndCoasterNodeEditCommand ), host, guest, view,
			"the drag's last commands came back" );
		var steps = HostCommandsOf( host, seat ).Count( command => command is EditCoasterNodeCommand );
		Assert.IsTrue( steps is >= 2 and <= 20, $"sixty moves went in a few steps: {steps}" );
		Assert.AreEqual( last, builder.Track.Nodes[node].T, 1e-5f, "the last value went" );
		Assert.AreEqual( builder.Track.Nodes[node].T, hostBuilder.Track.Nodes[node].T );
		Assert.IsNull( host.Level.CoasterEdits.Of( seat ), "the edit ended on the host" );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsMoveDragReleasedOverNothingPutsThePylonBack()
	{
		var (host, guest, view, coaster, _, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var (hostBuilder, builder) = Builders( host, guest, coaster );
		GuestPylons( host, guest, view, 3 );
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( panel.Select( CoasterToolButton.Move ) );
		var node = CoasterTrack.StationNodeCount + 1;
		var cell = (builder.Track.Nodes[node].X, builder.Track.Nodes[node].Y);
		Assert.IsTrue( panel.Press( node, new N.Vector2( 500, 400 ), cell.X, cell.Y ) );
		RoundsUntil( () => panel.IsDragging, host, guest, view, "the press came back" );
		guest.Enter();
		panel.Drag( new N.Vector2( 520, 400 ), 0, (cell.X + 1, cell.Y) );
		RoundsUntil( () => (builder.Track.Nodes[node].X, builder.Track.Nodes[node].Y) == (cell.X + 1, cell.Y), host, guest, view, "the move's step came back" );
		guest.Enter();
		panel.Release( null, null );
		RoundsUntil( () => guest.Level.CoasterRequests.IsEmpty, host, guest, view, "the put-back and the end came back" );
		Assert.AreEqual( cell, (builder.Track.Nodes[node].X, builder.Track.Nodes[node].Y), "the pylon is back on its cell" );
		Assert.AreEqual( cell, (hostBuilder.Track.Nodes[node].X, hostBuilder.Track.Nodes[node].Y) );
		Assert.IsNull( host.Level.CoasterEdits.Of( seat ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void EscapeEndsAGuestsCoasterToolBehindItsDragAndItsClaimLeavesNoCoasterClosed()
	{
		var (host, guest, view, coaster, ride, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var (hostBuilder, builder) = Builders( host, guest, coaster );
		GuestPylons( host, guest, view, 3 );
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( panel.Select( CoasterToolButton.Move ) );
		var node = CoasterTrack.StationNodeCount + 1;
		var cell = (builder.Track.Nodes[node].X, builder.Track.Nodes[node].Y);
		Assert.IsTrue( panel.Press( node, new N.Vector2( 500, 400 ), cell.X, cell.Y ) );
		RoundsUntil( () => panel.IsDragging, host, guest, view, "the press came back" );
		guest.Enter();
		panel.Drag( new N.Vector2( 520, 400 ), 0, (cell.X + 1, cell.Y) );
		RoundsUntil( () => hostBuilder.Track.Nodes[node].X == cell.X + 1, host, guest, view, "the step reached the host" );
		// Escape mid-drag: the drag's put-back and end, then the tool's end, go in that order behind it; the claim goes after them
		view.Escape();
		Assert.IsNull( guest.Level.CoasterPanel );
		RoundsUntil( () => host.Level.ClaimOf( ride ) == null && guest.Level.ClaimOf( ride ) == null, host, guest, view, "the claim went after the tool's end" );
		var kinds = HostCommandsOf( host, seat ).Select( command => command.Kind ).SkipWhile( kind => kind != ParkCommandKind.BeginCoasterNodeEdit ).ToArray();
		CollectionAssert.AreEqual( new[] { ParkCommandKind.BeginCoasterNodeEdit, ParkCommandKind.EditCoasterNode, ParkCommandKind.EditCoasterNode, ParkCommandKind.EndCoasterNodeEdit,
			ParkCommandKind.EndCoasterTool, ParkCommandKind.ReleaseRide }, kinds );
		Assert.AreEqual( cell, (hostBuilder.Track.Nodes[node].X, hostBuilder.Track.Nodes[node].Y), "the pylon went back" );
		Assert.IsFalse( coaster.Runtime.Coaster!.BeingEdited || HostCoaster( host, coaster ).Runtime.Coaster!.BeingEdited, "no coaster is left closed and marked" );
		Assert.IsNull( host.Level.CoasterEdits.Of( seat ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AnIdleGuestsCoasterClaimExpiresAndItsReleaseEndsTheCoastersToolStateOnEveryPark()
	{
		var (host, guest, view, coaster, ride, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var (_, builder) = Builders( host, guest, coaster );
		GuestPylons( host, guest, view, 2 );
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( panel.Select( CoasterToolButton.Height ) );
		var node = CoasterTrack.StationNodeCount;
		Assert.IsTrue( panel.Press( node, new N.Vector2( 500, 400 ), CoasterCell.X + 4, CoasterCell.Y ) );
		RoundsUntil( () => panel.IsDragging && host.Level.CoasterEdits.Of( seat ) != null, host, guest, view, "the drag runs on the host" );
		// the guest holds its drag and sends nothing for the lease's length: the host lets its claim go, and the claim's end releases the drag and ends the coaster's tool state
		HostWaits( host, CoopHostStream.ClaimLeaseSeconds + 1 );
		Assert.IsNull( host.Level.ClaimOf( ride ) );
		Assert.IsNull( host.Level.CoasterEdits.Of( seat ), "the drag went with the claim" );
		var hostCoaster = HostCoaster( host, coaster );
		Assert.IsFalse( hostCoaster.Runtime.Coaster!.BeingEdited, "the coaster left the tool" );
		RoundsUntil( () => guest.Level.CoasterPanel == null, host, guest, view, "the guest's tool ended once the release reached it" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimLost ), "and said why" );
		Assert.IsFalse( coaster.Runtime.Coaster!.BeingEdited );
		Rounds( 120, host, guest );
		Assert.AreEqual( 0, HostCommandsOf( host, seat ).Count( command => command is EndCoasterToolCommand ), "no tool's end went for a claim the host let go" );
		Assert.AreEqual( builder.Track.Nodes.Count, host.Level.GetCoasterBuilder( hostCoaster )!.Track.Nodes.Count );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsDragKeepsItsClaimPastTheLeaseWhileItsStepsGo()
	{
		var (host, guest, view, coaster, ride, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		GuestPylons( host, guest, view, 2 );
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( panel.Select( CoasterToolButton.Rotate ) );
		var node = CoasterTrack.StationNodeCount;
		hudPointer = new N.Vector2( 1000, 800 );
		Assert.IsTrue( panel.Press( node, new N.Vector2( 500, 400 ), CoasterCell.X + 4, CoasterCell.Y ) );
		RoundsUntil( () => panel.IsDragging, host, guest, view, "the press came back" );
		// past the lease's length, the drag's steps renew it: a pointer the HUD follows, moved every ten seconds, keeps the claim
		var moved = 0;
		for ( var waited = 0.0; waited < CoopHostStream.ClaimLeaseSeconds + 30; waited += 10 )
		{
			var steps = host.Level.Commands.Log.Count( entry => entry.Command is EditCoasterNodeCommand );
			hudPointer = new N.Vector2( 1000 + (++moved % 2 == 0 ? 10 : -10), 800 );
			RoundsUntil( () => guest.Level.CoasterRequests.IsEmpty && host.Level.Commands.Log.Count( entry => entry.Command is EditCoasterNodeCommand ) > steps, host, guest, view,
				"the step came back" );
			HostWaits( host, 10 );
		}
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )), "the drag kept its claim" );
		guest.Enter();
		panel.Release( node, null );
		RoundsUntil( () => guest.Level.CoasterRequests.IsEmpty, host, guest, view, "the end came back" );
		Assert.IsNull( host.Level.CoasterEdits.Of( seat ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheTrackToolsEntryEndsTheCoasterToolFirst()
	{
		var (host, guest, _, _) = Shared( ( park, _ ) =>
		{
			PlaceCoaster( park );
			PlaceKartRide( park );
		} );
		host.Enter();
		var coaster = host.Level.Objects.Objects.Single( item => item.Runtime.Coaster != null );
		var kart = host.Level.TrackStations.Keys.Single();
		Assert.IsTrue( host.Level.EnterCoasterTool( coaster ) );
		Assert.AreEqual( ParkCommands.LocalPlayer, host.Level.ClaimOf( coaster.Runtime.AttractionId ), "the host's coaster tool claims its coaster" );
		Assert.AreEqual( TrackToolEntry.Entered, host.Level.BeginTrackTool( kart, placed: false ) );
		Assert.IsNull( host.Level.CoasterPanel, "the coaster tool ended first" );
		Assert.IsFalse( coaster.Runtime.Coaster!.BeingEdited );
		Assert.AreEqual( (null, ParkCommands.LocalPlayer), (host.Level.ClaimOf( coaster.Runtime.AttractionId ), host.Level.ClaimOf( kart.Runtime.AttractionId )),
			"one tool, one claim: the claim moved to the kart ride" );
		host.Level.ExitTrackTool();
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheCoasterCommandsOfAGuestNeedItsGrantAndItsClaim()
	{
		var (host, guest, view, coaster, ride, seat) = SharedCoaster();
		var rights = (CoopBuildRights)guest.Level.Commands.Rights!;
		ParkCommand[] commands =
		{
			new BeginCoasterToolCommand( ride ), new EndCoasterToolCommand( ride ), new CoasterClickCommand( ride, 24, 20, 40, false, 0 ), new RemoveLastCoasterPylonCommand( ride ),
			new BeginCoasterNodeEditCommand( ride, CoasterTrack.StationNodeCount, CoasterDrag.Height ), new EditCoasterNodeCommand( ride, CoasterNodeField.Loft, 0.5f, 0, 0 ),
			new EndCoasterNodeEditCommand( ride ), new SwapCoasterStackCommand( ride, CoasterTrack.StationNodeCount, true ), new CutCoasterCommand( ride, 3, 4 )
		};
		// without the claim, a granted guest's coaster commands are refused: the host let it go, or it never took it
		foreach ( var command in commands )
		{
			Assert.IsFalse( rights.Allows( seat, command with { Player = seat } ), $"{command.Kind} without the claim" );
			Assert.AreEqual( CoopBuildRights.ClaimLostReason, rights.RefusalReason( seat, command with { Player = seat } ) );
		}
		// a coaster is no longer claimed against a guest until a seat claims it: its sale is a granted guest's as a track ride's is
		Assert.IsTrue( rights.Allows( seat, new RemoveAttractionCommand( ride ) { Player = seat } ) );
		Assert.IsTrue( rights.Allows( seat, new ClaimRideCommand( ride ) { Player = seat } ), "a granted guest claims a coaster" );
		GuestCoasterTool( host, guest, view, coaster );
		foreach ( var command in commands )
			Assert.IsTrue( rights.Allows( seat, command with { Player = seat } ), $"{command.Kind} under the claim" );
		// and a guest without the grant has none of them, the claim held or not
		Grant( host, guest, view, false );
		foreach ( var command in commands )
			Assert.AreEqual( CoopBuildRights.NotGrantedReason, rights.RefusalReason( seat, command with { Player = seat } ), $"{command.Kind} without the grant" );
		// the host claims what no seat holds, and every coaster command of a coaster another seat claims is refused it
		var hostRights = (CoopBuildRights)host.Level.Commands.Rights!;
		foreach ( var command in commands )
			Assert.AreEqual( Level.ClaimedReason, hostRights.RefusalReason( ParkCommands.LocalPlayer, command ), $"the host's {command.Kind}" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheSessionsEndLeavesNoCoasterAGuestHeldClosedAndMarkedAndItsLogReplays()
	{
		var (host, guest, view, coaster, ride, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		GuestPylons( host, guest, view, 2 );
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( panel.Select( CoasterToolButton.Move ) );
		var builder = guest.Level.GetCoasterBuilder( coaster )!;
		var node = CoasterTrack.StationNodeCount + 1;
		var cell = (builder.Track.Nodes[node].X, builder.Track.Nodes[node].Y);
		Assert.IsTrue( panel.Press( node, new N.Vector2( 500, 400 ), cell.X, cell.Y ) );
		RoundsUntil( () => panel.IsDragging, host, guest, view, "the press came back" );
		guest.Enter();
		panel.Drag( new N.Vector2( 520, 400 ), 0, (cell.X + 1, cell.Y) );
		var hostCoaster = HostCoaster( host, coaster );
		var hostBuilder = host.Level.GetCoasterBuilder( hostCoaster )!;
		RoundsUntil( () => hostBuilder.Track.Nodes[node].X == cell.X + 1, host, guest, view, "the step reached the host" );
		CatchUpAndCompare( host, guest );
		// the host ends the session with the guest's drag held and its coaster in the tool
		host.Enter();
		var hostStream = host.Session!.HostStream!;
		host.Session.End( "The host closed the park." );
		var log = ParkCommandLogFile.Deserialize( hostStream.SessionLog!.Serialize() );
		Assert.IsFalse( hostCoaster.Runtime.Coaster!.BeingEdited, "the host's park left no coaster a guest held closed and marked" );
		Assert.IsNull( host.Level.CoasterEdits.Of( seat ), "nor its drag" );
		Assert.AreEqual( cell, (hostBuilder.Track.Nodes[node].X, hostBuilder.Track.Nodes[node].Y), "the moved pylon went back" );
		Assert.IsNull( FirstReplayMismatch( log, park => PlaceCoaster( park ) ), "the session's log replays to its last checkpoint, which the teardown's end came after" );
		guest.Enter();
		guest.Session!.End( "The guest left." );
		Assert.IsNull( guest.Level.CoasterPanel );
		Assert.IsFalse( coaster.Runtime.Coaster!.BeingEdited, "the guest's park left no claimed coaster closed and marked either" );
	}
}
