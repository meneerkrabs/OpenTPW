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
	private OriginalObject PlaceCoaster( Park park ) => PlaceCoasterOn( park.Level, fixture! );

	/// <summary><see cref="PlaceCoaster"/> on <paramref name="level"/>, a park of the stream tests' asset-free start built over <paramref name="fixture"/>.</summary>
	internal static OriginalObject PlaceCoasterOn( Level level, UpgradeCatalogueDefaultsTests fixture )
	{
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
		Assert.AreEqual( starts, guest.Level.HostToolStarts, "a click's outcome starts no tool: a press held from before it keeps its release, one made while it waited is marked" );
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
		RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the Backspace came back" );
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

	/// <summary>Forty one-cell lines between two of the host's frames spend the guest's burst: a request of the guest's sent in the same frame is too fast, and the host keeps no record of it.</summary>
	private static CoopGuestCommands.Request[] SpendTheBurst( Park guest ) => Enumerable.Range( 0, 40 )
		.Select( _ => guest.Session!.GuestCommands!.Submit( new LayLineCommand( CellToolMode.Path, -1, 30, 30, 30, 30 ) { Player = guest.Level.Commands.Player } ) ).ToArray();

	[TestMethod]
	public void AGuestsLastDragStepAndItsEndTheHostCouldNotTakeGoAgainBeforeAnythingElse()
	{
		var (host, guest, view, coaster, _, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var (hostBuilder, builder) = Builders( host, guest, coaster );
		GuestPylons( host, guest, view, 2 );
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( panel.Select( CoasterToolButton.Height ) );
		var node = CoasterTrack.StationNodeCount;
		hudPointer = new N.Vector2( 1000, 800 );
		Assert.IsTrue( panel.Press( node, new N.Vector2( 500, 400 ), CoasterCell.X + 4, CoasterCell.Y ) );
		RoundsUntil( () => panel.IsDragging && guest.Level.CoasterRequests.IsEmpty, host, guest, view, "the press came back" );
		// two seconds refill the host's bucket of the guest's requests
		Rounds( 120, host, guest );
		// the drag's last step goes in a frame whose burst the host cannot take: it goes again, and the end waits for it
		var retried = guest.Level.CoasterRequests.Retried;
		var burst = SpendTheBurst( guest );
		hudPointer = new N.Vector2( 1000, 760 );
		view.Update();
		guest.Enter();
		panel.Release( node, null );
		RoundsUntil( () => guest.Level.CoasterRequests.IsEmpty && burst.All( request => !request.IsOpen ), host, guest, view, "the drag's last commands came back" );
		Assert.IsTrue( burst.All( request => request.State == CoopGuestRequestState.Applied ), "the burst spent the host's bucket" );
		Assert.IsTrue( guest.Level.CoasterRequests.Retried > retried, "the step after it was too fast and went again" );
		var steps = HostCommandsOf( host, seat ).OfType<EditCoasterNodeCommand>().ToArray();
		Assert.AreEqual( builder.Track.Nodes[node].T, steps.Last().Value, "the last value applied" );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is EndCoasterNodeEditCommand ) );
		Assert.IsTrue( Array.IndexOf( host.Level.Commands.Log.Select( entry => entry.Command ).ToArray(), steps.Last() )
			< host.Level.Commands.Log.ToList().FindIndex( entry => entry.Command is EndCoasterNodeEditCommand && entry.Command.Player == seat ), "before the end" );
		Assert.AreEqual( builder.Track.Nodes[node].T, hostBuilder.Track.Nodes[node].T );
		Assert.IsNull( host.Level.CoasterEdits.Of( seat ), "the end applied: the host holds no drag" );
		// a drag whose end alone goes in such a frame: the end goes again
		guest.Enter();
		hudPointer = new N.Vector2( 1000, 800 );
		Assert.IsTrue( panel.Press( node, new N.Vector2( 500, 400 ), CoasterCell.X + 4, CoasterCell.Y ) );
		RoundsUntil( () => panel.IsDragging && guest.Level.CoasterRequests.IsEmpty, host, guest, view, "the second press came back" );
		Rounds( 120, host, guest );
		retried = guest.Level.CoasterRequests.Retried;
		burst = SpendTheBurst( guest );
		guest.Enter();
		panel.Release( node, null );
		view.Update();
		RoundsUntil( () => guest.Level.CoasterRequests.IsEmpty && burst.All( request => !request.IsOpen ), host, guest, view, "the end came back" );
		Assert.IsTrue( burst.All( request => request.State == CoopGuestRequestState.Applied ), "the burst spent the host's bucket" );
		Assert.IsTrue( guest.Level.CoasterRequests.Retried > retried, "the end after it was too fast and went again" );
		Assert.AreEqual( 2, HostCommandsOf( host, seat ).Count( command => command is EndCoasterNodeEditCommand ), "the second drag's end has a record" );
		Assert.IsNull( host.Level.CoasterEdits.Of( seat ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ClosingTheToolMidDragSendsTheDragsLatestValueBeforeItsEnd()
	{
		var (host, guest, view, coaster, _, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var (hostBuilder, builder) = Builders( host, guest, coaster );
		GuestPylons( host, guest, view, 2 );
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( panel.Select( CoasterToolButton.Height ) );
		var node = CoasterTrack.StationNodeCount;
		var start = builder.Track.Nodes[node].T;
		var (low, high) = builder.Track.LoftRange( builder.Site, node );
		hudPointer = new N.Vector2( 1000, 800 );
		Assert.IsTrue( panel.Press( node, new N.Vector2( 500, 400 ), CoasterCell.X + 4, CoasterCell.Y ) );
		RoundsUntil( () => panel.IsDragging && guest.Level.CoasterRequests.IsEmpty, host, guest, view, "the press came back" );
		Rounds( 120, host, guest );
		// a step goes in a frame whose burst the host cannot take; the next value waits for its 0.2 s when the tool ends
		var retried = guest.Level.CoasterRequests.Retried;
		var burst = SpendTheBurst( guest );
		hudPointer = new N.Vector2( 1000, 780 );
		view.Update();
		hudPointer = new N.Vector2( 1000, 760 );
		view.Update();
		Assert.IsTrue( guest.Level.CoasterRequests.HasUnsent, "the latest value waits" );
		guest.Enter();
		guest.Level.ExitCoasterTool();
		hudPointer = new N.Vector2( -1 );
		RoundsUntil( () => guest.Level.CoasterRequests.IsEmpty && burst.All( request => !request.IsOpen ), host, guest, view, "the drag's last commands came back" );
		Assert.IsTrue( burst.All( request => request.State == CoopGuestRequestState.Applied ), "the burst spent the host's bucket: the step after it was too fast" );
		Assert.AreEqual( retried, guest.Level.CoasterRequests.Retried, "the later value replaced it: it did not go again" );
		var latest = Math.Clamp( start + 20 * CoasterToolPanel.HeightRate, low, high );
		Assert.AreEqual( latest, builder.Track.Nodes[node].T, 1e-5f, "the drag ended at its latest value, as the host's ends at its own" );
		Assert.AreEqual( builder.Track.Nodes[node].T, hostBuilder.Track.Nodes[node].T );
		Assert.IsNull( host.Level.CoasterEdits.Of( seat ), "and its end applied" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsCutGoesToTheHostAndItsOutcomeSelectsTheBuildButton()
	{
		var (host, guest, view, coaster, _, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var (hostBuilder, builder) = Builders( host, guest, coaster );
		GuestPylons( host, guest, view );
		var back = builder.Track.ReturnCell!.Value;
		GuestCoasterClick( host, guest, view, back.X, back.Y, closing: true );
		GuestCoasterClick( host, guest, view, back.X, back.Y );
		Assert.IsTrue( builder.Track.IsComplete );
		// the completion ended the tool: the track button opens it again on the finished coaster
		GuestCoasterTool( host, guest, view, coaster );
		guest.Enter();
		var panel = guest.Level.CoasterPanel!;
		Assert.IsTrue( panel.Select( CoasterToolButton.Demolish ) );
		var first = CoasterTrack.StationNodeCount + 1;
		Assert.AreEqual( CoasterPanelResult.CutStarted, panel.Release( first, null ), "the first release starts the range here" );
		var nodes = builder.Track.Nodes.Count;
		Assert.AreEqual( CoasterPanelResult.Nothing, panel.Release( first + 2, null ), "the second goes to the host" );
		Assert.IsTrue( panel.IsWaiting );
		Assert.AreEqual( CoasterPanelResult.Waiting, panel.Release( first, null ), "no other cut while it waits" );
		RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the cut came back" );
		Assert.IsTrue( builder.Track.Nodes.Count < nodes && hostBuilder.Track.Nodes.Count == builder.Track.Nodes.Count, "the cut took pylons off on both parks" );
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected, "the outcome selected the build button" );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is CutCoasterCommand ) );
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
		// the host claims what no seat holds, and every coaster command of a coaster another seat claims is refused it
		var hostRights = (CoopBuildRights)host.Level.Commands.Rights!;
		foreach ( var command in commands )
			Assert.AreEqual( Level.ClaimedReason, hostRights.RefusalReason( ParkCommands.LocalPlayer, command ), $"the host's {command.Kind}" );
		// a guest without the grant has none of them, and its grant's record let go of its claim, which ended the coaster's tool state (C3c-1b)
		Grant( host, guest, view, false );
		foreach ( var command in commands )
			Assert.AreEqual( CoopBuildRights.NotGrantedReason, rights.RefusalReason( seat, command with { Player = seat } ), $"{command.Kind} without the grant" );
		Assert.IsNull( host.Level.ClaimOf( ride ) );
		Assert.IsNull( guest.Level.ClaimOf( ride ) );
		Assert.IsFalse( coaster.Runtime.Coaster!.BeingEdited, "the coaster left the tool" );
		foreach ( var command in commands )
			Assert.IsNull( hostRights.RefusalReason( ParkCommands.LocalPlayer, command ), $"the host's {command.Kind} once no seat claims the coaster" );
		CatchUpAndCompare( host, guest );
	}

	/// <summary>A synthetic ride of the asset-free park's theme with the given .sam lines (its type's archive path <paramref name="name"/>), placed as nothing yet.</summary>
	private ObjectCatalogEntry SyntheticEntry( string name, string settings )
	{
		var root = (string)typeof( UpgradeCatalogueDefaultsTests ).GetField( "root", NonPublic )!.GetValue( fixture )!;
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, $"/{name}", new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.WhichUIType 0\n{settings}", $"{name}.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(),
			new BaseFileSystem( root ), null );
	}

	[TestMethod]
	public void AGuestsTrackAddOnIsBuildingOnlyOntoItsOwnKartOrWaterRide()
	{
		// the add-on types stand in the park as objects of their own, far from the rides, so a command's key resolves to them on every park (the asset-free park has no catalogue)
		var (host, guest, _, _) = Shared( ( park, _ ) =>
		{
			PlaceKartRide( park );
			PlaceCoaster( park );
			foreach ( var (name, settings, y) in new[]
			{
				("kartjump", $"Info.Id 1170\nAddOn.UpgradesId 1150\nAddOn.UpgradeType {TrackAddOns.TrackUpgradeType}\n", 12),
				("otherjump", $"Info.Id 1171\nAddOn.UpgradesId 1151\nAddOn.UpgradeType {TrackAddOns.TrackUpgradeType}\n", 14),
				("coasterjump", $"Info.Id 1172\nAddOn.UpgradesId 1160\nAddOn.UpgradeType {TrackAddOns.TrackUpgradeType}\n", 16)
			} )
				Assert.IsNotNull( park.Level.Objects.TryPlace( SyntheticEntry( name, settings ), 29, y, 0, out var placed ), $"{name}: {placed}" );
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var seat = guest.Level.Commands.Player;
		var rights = (CoopBuildRights)guest.Level.Commands.Rights!;
		var kart = guest.Level.TrackStations.Keys.Single().Runtime.AttractionId;
		var coaster = guest.Level.Objects.Objects.Single( item => item.Runtime.Coaster != null ).Runtime.AttractionId;
		string KeyOf( string name ) => ParkCommandCodec.EntryKey( guest.Level.Objects.Objects.Single( item => item.Entry.ArchivePath == $"/{name}" ).Entry );
		bool Builds( string name, int ride, bool moving = false ) => rights.Allows( seat, new PlaceObjectCommand( KeyOf( name ), 24, 9, 0, moving, ride ) { Player = seat } );
		Assert.IsTrue( Builds( "kartjump", kart ), "the add-on of the ride's type onto that ride" );
		Assert.IsFalse( Builds( "kartjump", kart, moving: true ), "never a moving placement" );
		Assert.IsFalse( Builds( "otherjump", kart ), "nor another ride type's add-on" );
		Assert.IsFalse( Builds( "kartjump", -1 ), "nor a track add-on as an object of its own" );
		Assert.IsFalse( Builds( "coasterjump", coaster ), "a coaster takes no track add-on" );
		Assert.IsFalse( Builds( "kartjump", 999_999 ), "nor a ride the park does not hold" );
		Assert.IsFalse( rights.Allows( seat, new PlaceObjectCommand( ParkCommandCodec.EntryKey( guest.One.Entry ), 24, 9, 0, false, kart ) { Player = seat } ),
			"an ordinary object that names a ride" );
		// a ride another seat claims refuses it in the claim's words
		Assert.IsTrue( host.Issue( new ClaimRideCommand( kart ) ).Accepted );
		RoundsUntil( () => guest.Level.ClaimOf( kart ) == ParkCommands.LocalPlayer, host, guest, view, "the host's claim reached the guest" );
		Assert.IsFalse( Builds( "kartjump", kart ) );
		Assert.AreEqual( Level.ClaimedReason, rights.RefusalReason( seat, new PlaceObjectCommand( KeyOf( "kartjump" ), 24, 9, 0, false, kart ) { Player = seat } ) );
		Assert.IsTrue( host.Issue( new ReleaseRideCommand( kart ) ).Accepted );
		RoundsUntil( () => guest.Level.ClaimOf( kart ) == null, host, guest, view, "the release reached the guest" );
		// a guest's add-on request is decided alike on every park: the park's add-on rule refuses a jump off the ride's track
		var request = guest.Issue( new PlaceObjectCommand( KeyOf( "kartjump" ), 30, 30, 0, false, kart ) );
		Assert.IsTrue( request.IsPending, "the rights let it go to the host" );
		RoundsUntil( () => !request.Request!.IsOpen, host, guest, view, "the placement came back" );
		Assert.IsFalse( request.Request!.Accepted, "no track there" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void APressMadeWhileTheCoasterPanelWaitedIsNoClickWhateverCameBack()
	{
		var (host, guest, view, coaster, _, seat) = SharedCoaster();
		GuestCoasterTool( host, guest, view, coaster );
		var (oldMode, oldView, oldProjection) = (Camera.Mode, Camera.ViewMatrix, Camera.ProjMatrix);
		// the asset-free park's original park has no heightfield: the HUD's frames that point at the park pick on the generic sandbox's ground plane instead, the park's state untouched
		var originalPark = typeof( Level ).GetProperty( nameof( Level.OriginalPark ) )!;
		T OnThePlane<T>( Func<T> pick )
		{
			var park = originalPark.GetValue( guest.Level );
			originalPark.SetValue( guest.Level, null );
			try
			{
				return pick();
			}
			finally
			{
				originalPark.SetValue( guest.Level, park );
			}
		}
		void Hud( UiInput input ) => OnThePlane( () =>
		{
			view.Input( input );
			return true;
		} );
		try
		{
			// a camera above the coaster's site, looking down at it, so a point of the views' canvas lies on a cell of the park
			Camera.Mode = new ParkCameraMode();
			var centre = new N.Vector3( ParkPlacement.MinimumCoordinate + 26.5f * ObjectPlacement.CellSize, ParkPlacement.MinimumCoordinate + 24.5f * ObjectPlacement.CellSize, 0 );
			Camera.ViewMatrix = N.Matrix4x4.CreateLookAt( centre + new N.Vector3( 0, -20, 30 ), centre + new N.Vector3( 0, 0, 12 ), N.Vector3.UnitZ );
			Camera.ProjMatrix = N.Matrix4x4.CreatePerspectiveFieldOfView( 0.8f, 2048f / 1536f, 0.5f, 500 );
			guest.Enter();
			// the first point of the canvas's middle band that lies on a cell of the park
			var (point, cellX, cellY) = (default( N.Vector2 ), -1, -1);
			for ( var y = 500; y <= 1100 && cellX < 0; y += 50 )
				for ( var x = 600; x <= 1500 && cellX < 0; x += 50 )
				{
					var candidate = new N.Vector2( x, y );
					if ( OnThePlane( () => (guest.Level.TryGetGridCell( candidate, new N.Vector2( 2048, 1536 ), out var cx, out var cy ), cx, cy) ) is (true, var hx, var hy) )
						(point, cellX, cellY) = (candidate, hx, hy);
				}
			Assert.IsTrue( cellX >= 0, "the camera hits the park" );
			var requests = guest.Session!.GuestCommands!;
			int Clicks() => host.Level.Commands.Log.Count( entry => entry.Command is CoasterClickCommand && entry.Command.Player == seat );
			// a click the park refuses waits; its answer comes back in the guest's park before the HUD's frame that takes the press
			var panel = guest.Level.CoasterPanel!;
			Assert.IsTrue( guest.Level.SendCoasterClick( 200, 200, false ) );
			view.Update();
			var click = requests.Open.Single( request => request.Command is CoasterClickCommand );
			for ( var round = 0; round < 600 && click.IsOpen; round++ )
			{
				clock += 1 / 60.0;
				host.Frame( 1 / 60.0 );
				guest.Frame( 1 / 60.0 );
				guest.Session.Step();
			}
			Assert.IsFalse( click.IsOpen );
			Assert.IsTrue( panel.IsWaiting, "the HUD has not taken the answer yet" );
			var clicks = Clicks();
			var waits = guest.Level.WaitingClicks;
			Hud( UiInput.Idle( point ) with { LeftDown = true, LeftPressed = true } );
			Assert.IsFalse( panel.IsWaiting, "the frame took the answer first" );
			Assert.AreEqual( waits, guest.Level.WaitingClicks, "a press the answer of its own frame freed asks no wait" );
			Hud( UiInput.Idle( point ) with { LeftReleased = true } );
			RoundsUntil( () => guest.Level.CoasterRequests.IsEmpty, host, guest, view, "nothing waits" );
			Rounds( 30, host, guest );
			Assert.AreEqual( clicks, Clicks(), "the press made while the panel waited was no click" );
			// the next press is the panel's again: its release is a click
			Hud( UiInput.Idle( point ) with { LeftDown = true, LeftPressed = true } );
			Hud( UiInput.Idle( point ) with { LeftReleased = true } );
			Assert.IsTrue( panel.IsWaiting, $"the release clicked cell ({cellX}, {cellY})" );
			RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the click came back" );
			Assert.AreEqual( clicks + 1, Clicks() );
			// a press held from before a wait keeps its release: the Backspace's outcome changes no gesture
			Hud( UiInput.Idle( point ) with { LeftDown = true, LeftPressed = true } );
			Assert.IsTrue( guest.Level.SendCoasterBackspace() );
			RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the Backspace came back" );
			Hud( UiInput.Idle( point ) with { LeftReleased = true } );
			Assert.IsTrue( panel.IsWaiting, "the release clicked" );
			RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the click came back" );
			Assert.AreEqual( clicks + 2, Clicks() );
			// a press while a request still waits asks the player to wait, and its release is nobody's either
			Assert.IsTrue( guest.Level.SendCoasterBackspace() );
			waits = guest.Level.WaitingClicks;
			Hud( UiInput.Idle( point ) with { LeftDown = true, LeftPressed = true } );
			Assert.AreEqual( waits + 1, guest.Level.WaitingClicks );
			RoundsUntil( () => !panel.IsWaiting, host, guest, view, "the Backspace came back" );
			Hud( UiInput.Idle( point ) with { LeftReleased = true } );
			Rounds( 30, host, guest );
			Assert.AreEqual( clicks + 2, Clicks() );
		}
		finally
		{
			(Camera.Mode, Camera.ViewMatrix, Camera.ProjMatrix) = (oldMode, oldView, oldProjection);
		}
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
