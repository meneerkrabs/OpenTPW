using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-undo] Undo in a shared park (docs/COOP.md, "Conflicts and undo (C5)"): the path and queue tools' Backspace is a take-back the host's journal decides, for a guest and
/// the host alike. It takes back the player's own last step only, whole, with the tool's own refunds, and is refused while a later command of another player's still changes the
/// step's cells; the refusal changes nothing and says why.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>A shared park whose guest may build, with the guest's HUD.</summary>
	private (Park Host, Park Guest, View View) UndoSession()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		return (host, guest, view);
	}

	/// <summary>The guest's click of its path tool, sent to the host and back.</summary>
	private void GuestClick( Park host, Park guest, View view, (int X, int Y) cell )
	{
		guest.Enter();
		Assert.IsNotNull( guest.Level.ClickCellTool( cell )?.Waiting, "the line went to the host" );
		RoundsUntil( () => guest.Level.CellTool.Waiting == null, host, guest, view, "the line came back" );
	}

	/// <summary>The guest's Backspace, sent to the host as a take-back and back: whether it took the segment back.</summary>
	private bool GuestBackspace( Park host, Park guest, View view )
	{
		var tool = guest.Level.CellTool;
		var segments = tool.SegmentCount;
		guest.Enter();
		Assert.IsTrue( tool.Undo(), "the Backspace went to the host" );
		Assert.IsNotNull( tool.WaitingUndo );
		RoundsUntil( () => tool.WaitingUndo == null, host, guest, view, "the take-back came back" );
		return tool.SegmentCount == segments - 1;
	}

	/// <summary>The host's path tool from <paramref name="start"/>, its line to <paramref name="end"/> laid at once, the tool kept on.</summary>
	private static CellBuildTool HostLine( Park host, (int X, int Y) start, (int X, int Y) end )
	{
		host.Enter();
		Assert.IsTrue( host.Level.EnterPathTool( start.X, start.Y ) );
		Assert.IsTrue( host.Level.ClickCellTool( end ) is { Completed: true }, "the host's line" );
		return host.Level.CellTool;
	}

	private static TakeBackCommand[] TakeBacksOf( Park host, int seat ) => host.Level.Commands.Log.Select( entry => entry.Command ).OfType<TakeBackCommand>().Where( command => command.Player == seat ).ToArray();

	/// <summary>The HUD's frame with one Backspace, the pointer off the park.</summary>
	private static void HudBackspace( View view ) => view.Input( UiInput.Idle( new N.Vector2( -1 ) ) with { Backspaces = 1 } );

	/// <summary>The queue tests' synthetic queue of five cells on <paramref name="park"/>'s ride, from (10, 6) up to its back cell (10, 2).</summary>
	private static void QueueOf( Park park )
	{
		park.Enter();
		ConfigureQueue( park.Level.Guests!.Grid, park.One, 10, 5 );
	}

	private static string QueueCellsOf( Park park ) => string.Join( " ", park.One.Visitors.QueueCells );

	/// <summary>Frames of the host and the guests as the game runs them (each guest's session step and HUD after its park), until <paramref name="done"/> holds.</summary>
	private void RoundsOf( Func<bool> done, string what, Park host, params (Park Park, View View)[] guests )
	{
		for ( var round = 0; round < 900 && !done(); round++ )
		{
			clock += 1 / 60.0;
			host.Frame( 1 / 60.0 );
			foreach ( var (park, view) in guests )
			{
				park.Frame( 1 / 60.0 );
				park.Session!.Step();
				view.Update();
			}
		}
		Assert.IsTrue( done(), what );
	}

	/// <summary>A guest's request, sent and answered.</summary>
	private CoopGuestCommands.Request Requested( Park host, Park guest, View view, ParkCommand command )
	{
		guest.Enter();
		var request = guest.Session!.GuestCommands!.Submit( command )!;
		RoundsUntil( () => !request.IsOpen, host, guest, view, $"the {command.Kind} came back" );
		return request;
	}

	[TestMethod]
	public void AGuestTakesItsNativeQueueSegmentsBackNewestFirstWithTheirRefundsButNotTheQueuesOwn()
	{
		var (host, guest, _, _) = Shared( ( park, _ ) => QueueOf( park ) );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var seat = guest.Level.Commands.Player;
		var queue = QueueCellsOf( host );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterQueueTool( guest.One.Visitors ) );
		var tool = guest.Level.CellTool;
		var loaded = tool.SegmentCount;
		Assert.IsTrue( loaded > 0, "the tool loaded the queue's own segments" );
		// a turning chain: along the row, then down from its end
		GuestClick( host, guest, view, (8, 2) );
		GuestClick( host, guest, view, (8, 4) );
		Assert.IsTrue( host.One.Visitors.QueueCells.Contains( (8, 4) ) );
		// each queue cell an undo clears refunds the queue cell's cost times the ride's scrap percentage (the native undo's own rule, RefundQueueCellNative); a count-down refunds nothing
		host.Enter();
		var instance = host.Level.GetEconomyInstance( host.One );
		Assert.IsNotNull( instance );
		Assert.IsTrue( host.Economy.TryGetObject( instance.Value, out var state ) );
		var perCell = (long)(unchecked( (uint)((int)host.Economy.CellCost( CellPurchase.Queue ) * host.Economy.ScrapPercent( state )) ) / 100U);
		Assert.IsTrue( perCell > 0 );
		var cells = host.Level.Guests!.Grid.Cells;
		int QueueCellsNow() => Enumerable.Range( 0, 9 ).Sum( y => Enumerable.Range( 2, 11 ).Count( x => cells.RawTypeAt( x, y ) == (byte)ParkCellType.Queue ) );
		var (paid, before) = (host.Economy.Balance, QueueCellsNow());
		Assert.IsTrue( GuestBackspace( host, guest, view ), "the second segment taken back" );
		Assert.IsFalse( host.One.Visitors.QueueCells.Contains( (8, 4) ) || host.One.Visitors.QueueCells.Contains( (8, 3) ), "its cells gone" );
		Assert.AreEqual( paid + (before - QueueCellsNow()) * perCell, host.Economy.Balance, "refunded per cleared cell at the ride's scrap percentage" );
		(paid, before) = (host.Economy.Balance, QueueCellsNow());
		Assert.IsTrue( GuestBackspace( host, guest, view ), "the first segment taken back, its bend corner cleared with the second as the original's undo clears it" );
		Assert.AreEqual( paid + (before - QueueCellsNow()) * perCell, host.Economy.Balance, "the same for the first" );
		Assert.AreEqual( queue, QueueCellsOf( host ), "the queue as it was" );
		Assert.AreEqual( loaded, tool.SegmentCount );
		Assert.IsTrue( TakeBacksOf( host, seat ).All( command => command.Verdict == TakeBackVerdict.Taken && command.Inverse is UndoQueueSegmentCommand ) );
		// the segments the tool loaded were laid by no step of the guest's: Backspace sends nothing and says the not-own note
		HudBackspace( view );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopUndoNotOwn ) );
		Assert.AreEqual( loaded, tool.SegmentCount, "the queue's own segment stays" );
		Rounds( 5, host, guest );
		Assert.AreEqual( 2, TakeBacksOf( host, seat ).Length, "nothing more went to the host" );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( host.Economy.Balance, guest.Economy.Balance );
		Assert.AreEqual( queue, QueueCellsOf( guest ) );
		guest.Level.CellTool.Cancel();
		QueueCellsAlike( host, guest );
	}

	[TestMethod]
	public void TheHostsBackspaceOnAQueuesOwnSegmentInASharedParkTakesNothing()
	{
		var (host, guest, _, _) = Shared( ( park, _ ) => QueueOf( park ) );
		var hostView = ViewOf( host );
		Rounds( 20, host, guest );
		var queue = QueueCellsOf( host );
		host.Enter();
		Assert.IsTrue( host.Level.EnterQueueTool( host.One.Visitors ) );
		var loaded = host.Level.CellTool.SegmentCount;
		HudBackspace( hostView );
		Assert.AreEqual( 1, hostView.Says( OpenTpwText.CoopUndoNotOwn ), "no step of the host's laid it" );
		Assert.AreEqual( loaded, host.Level.CellTool.SegmentCount );
		Assert.AreEqual( queue, QueueCellsOf( host ) );
		Assert.IsFalse( host.Level.Commands.Log.Any( entry => entry.Command is TakeBackCommand or UndoQueueSegmentCommand or UndoLineCommand ), "nothing issued" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsQueueLineIsTakenBackWholeWithItsRefund()
	{
		// a queue cell behind the back cell that no recompute took in yet: a line from it is the queue tool's line command (the queue tests' geometry)
		var (host, guest, _, _) = Shared( ( park, _ ) =>
		{
			QueueOf( park );
			park.Level.Guests!.Grid.SetQueue( 9, 2, towards: GuestPathGrid.DirectionBetween( 9, 2, 10, 2 ) );
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var seat = guest.Level.Commands.Player;
		var ride = host.One.Visitors.AttractionId;
		var line = Requested( host, guest, view, new LayLineCommand( CellToolMode.Queue, ride, 9, 2, 7, 2 ) );
		Assert.IsTrue( line.Accepted );
		Assert.IsTrue( host.One.Visitors.QueueCells.Contains( (7, 2) ) );
		var paid = host.Economy.Balance;
		var taken = Requested( host, guest, view, new TakeBackCommand( false, line.Record, -1, -1, -1 ) );
		Assert.IsTrue( taken.Accepted, taken.Result?.Reason );
		Assert.IsTrue( TakeBacksOf( host, seat ).Single() is { Verdict: TakeBackVerdict.Taken, Inverse: UndoLineCommand { Mode: CellToolMode.Queue } } );
		Assert.IsFalse( host.One.Visitors.QueueCells.Contains( (7, 2) ) || host.One.Visitors.QueueCells.Contains( (8, 2) ), "its built cells gone" );
		// its two built cells, each refunded at the queue cell's cost times the ride's scrap percentage (the queue removal's rule, RefundQueueCell)
		host.Enter();
		var instance = host.Level.GetEconomyInstance( host.One );
		Assert.IsNotNull( instance );
		Assert.IsTrue( host.Economy.TryGetObject( instance.Value, out var state ) );
		var perCell = host.Economy.CellCost( CellPurchase.Queue ) * host.Economy.ScrapPercent( state ) / 100;
		Assert.IsTrue( perCell > 0 );
		Assert.AreEqual( paid + 2 * perCell, host.Economy.Balance, "refunded per removed cell" );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( host.Economy.Balance, guest.Economy.Balance );
		QueueCellsAlike( host, guest );
	}

	[TestMethod]
	public void AToolsOwnChainOfThreePathSegmentsIsTakenBackInReverse()
	{
		var (host, guest, view) = UndoSession();
		var tool = StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		GuestClick( host, guest, view, (12, 26) );
		GuestClick( host, guest, view, (16, 26) );
		Assert.AreEqual( 3, tool.SegmentCount );
		Assert.IsTrue( GuestBackspace( host, guest, view ) );
		Assert.IsFalse( Walkable( host, 14, 26 ) );
		Assert.IsTrue( Walkable( host, 12, 26 ), "the corner the second segment laid stays" );
		Assert.IsTrue( GuestBackspace( host, guest, view ) );
		Assert.IsFalse( Walkable( host, 12, 24 ) || Walkable( host, 12, 26 ) );
		Assert.IsTrue( Walkable( host, 12, 20 ), "the corner the first laid" );
		Assert.IsTrue( GuestBackspace( host, guest, view ) );
		Assert.IsFalse( Walkable( host, 5, 20 ) || Walkable( host, 12, 20 ) );
		Assert.AreEqual( 0, tool.SegmentCount );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALineOverACellAtItsCountersLimitIsTakenBackWithoutCountingItDown()
	{
		var (host, guest, _, _) = Shared( ( park, _ ) =>
		{
			var cells = park.Level.Guests!.Grid.Cells;
			cells.SetType( 8, 20, ParkCellType.Path );
			cells.SetPlacementCount( 8, 20, short.MaxValue );
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		Assert.AreEqual( short.MaxValue, host.Level.Guests!.Grid.Cells.PlacementCountAt( 8, 20 ), "the line raised nothing there" );
		Assert.IsTrue( GuestBackspace( host, guest, view ), "taken" );
		Assert.IsFalse( Walkable( host, 7, 20 ) || Walkable( host, 9, 20 ), "its own cells gone" );
		Assert.AreEqual( ParkCellType.Path, host.Level.Guests.Grid.Cells.TypeAt( 8, 20 ) );
		Assert.AreEqual( short.MaxValue, host.Level.Guests.Grid.Cells.PlacementCountAt( 8, 20 ), "never counted down" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ACellAnotherPlayerRemovedAndLaidAgainRefusesTheTakeBackThoughItLooksTheSame()
	{
		var (host, guest, view) = UndoSession();
		var tool = StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		Assert.IsTrue( host.Issue( new BacktrackCommand( 12, 20 ) ).Accepted, "the host takes the guest's end cell off" );
		HostLine( host, (12, 20), (13, 20) ).Cancel();
		Rounds( 5, host, guest );
		Assert.IsFalse( GuestBackspace( host, guest, view ), "refused: the cell's history, not its state" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopUndoChanged ) );
		Assert.AreEqual( 1, tool.SegmentCount );
		Assert.IsTrue( Walkable( host, 8, 20 ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TwoOtherPlayersLinesBesideAGuestsCellTakenBackInTheOtherOrderLetItsTakeBackThrough()
	{
		var (host, first, second) = SharedWithTwo();
		var (firstView, secondView) = (ViewOf( first ), ViewOf( second ));
		var guests = new[] { (first, firstView), (second, secondView) };
		RoundsOf( () => first.Level.TickCount > 20 && second.Level.TickCount > 20, "the parks run", host, guests );
		var seats = new[] { first.Level.Commands.Player, second.Level.Commands.Player };
		foreach ( var seat in seats )
			Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, true ) ).Accepted );
		RoundsOf( () => first.Level.HasBuildGrant( seats[0] ) && second.Level.HasBuildGrant( seats[1] ), "the grants arrived", host, guests );
		// the first guest's line along the row; the host's line from above the row's cell (8, 20), the second guest's from below it: both link into it
		var tool = StartPath( first );
		Assert.IsNotNull( first.Level.ClickCellTool( (12, 20) )?.Waiting );
		RoundsOf( () => tool.Waiting == null, "the first guest's line came back", host, guests );
		var hostTool = HostLine( host, (8, 19), (8, 14) );
		second.Enter();
		Assert.IsTrue( second.Level.EnterPathTool( 8, 21 ) );
		var secondTool = second.Level.CellTool;
		Assert.IsNotNull( second.Level.ClickCellTool( (8, 26) )?.Waiting );
		RoundsOf( () => secondTool.Waiting == null, "the second guest's line came back", host, guests );
		bool FirstTakesBack()
		{
			first.Enter();
			Assert.IsTrue( tool.Undo() );
			RoundsOf( () => tool.WaitingUndo == null, "the take-back came back", host, guests );
			return tool.SegmentCount == 0;
		}
		Assert.IsFalse( FirstTakesBack(), "both later lines link into its cell" );
		// the host takes its line back first, though it laid it first: the second guest's still links
		host.Enter();
		Assert.IsTrue( hostTool.Undo() );
		Assert.IsFalse( FirstTakesBack(), "the second guest's line still links" );
		second.Enter();
		Assert.IsTrue( secondTool.Undo() );
		RoundsOf( () => secondTool.WaitingUndo == null, "the second guest's take-back came back", host, guests );
		Assert.AreEqual( 0, secondTool.SegmentCount );
		Assert.IsTrue( FirstTakesBack(), "nothing of anyone else's is left on its cells" );
		Assert.IsFalse( Walkable( host, 8, 20 ) || Walkable( first, 8, 20 ) || Walkable( second, 8, 20 ) );
		Assert.AreEqual( 2, firstView.Says( OpenTpwText.CoopUndoChanged ) );
		CutAndCompare( host, first, second );
	}

	[TestMethod]
	public void APathTakeBackReEvaluatesOnlyTheHostsOwnSelectionAndNoRideAnotherSeatClaims()
	{
		var (host, guest, view, kart, ride, seat) = SharedKart();
		// the guest's selection rides along with its take-back, and is never used
		guest.Enter();
		guest.Level.Selection = guest.One;
		StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		Assert.IsTrue( GuestBackspace( host, guest, view ) );
		var guests = TakeBacksOf( host, seat );
		Assert.AreEqual( guest.One.Runtime.AttractionId, guests.Single().SelectedRide, "the guest's selection rode along" );
		Assert.AreEqual( -1, ((UndoLineCommand)guests.Single().Inverse!).Ride, "a guest's take-back re-evaluates no ride" );
		// the host's own selection is re-evaluated, as the original's Back does
		host.Enter();
		host.Level.Selection = host.One;
		var hostTool = HostLine( host, (5, 30), (12, 30) );
		Assert.IsTrue( hostTool.Undo() );
		var own = TakeBacksOf( host, ParkCommands.LocalPlayer ).Last();
		Assert.AreEqual( host.One.Runtime.AttractionId, ((UndoLineCommand)own.Inverse!).Ride );
		// but not a ride the guest claims
		Assert.IsTrue( Requested( host, guest, view, new ClaimRideCommand( ride ) ).Accepted );
		host.Enter();
		host.Level.Selection = host.Level.TrackStations.Keys.Single();
		hostTool = HostLine( host, (5, 24), (12, 24) );
		Assert.IsTrue( hostTool.Undo() );
		own = TakeBacksOf( host, ParkCommands.LocalPlayer ).Last();
		Assert.AreEqual( ride, own.SelectedRide );
		Assert.AreEqual( -1, ((UndoLineCommand)own.Inverse!).Ride, "the guest's claimed ride is never re-evaluated by the host's take-back" );
		CatchUpAndCompare( host, guest );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void APathTakeBackLinkedToTheEntranceOfARideAnotherSeatClaimsIsRefusedByTheRights( bool linked )
	{
		// a kart ride whose entrance cell (8, 21) lies beside the guest's line: a path clear that unlinks an entrance with an owner word re-evaluates that ride, and may open it; the
		// entrance faces the line's cell (8, 20) (its queue link points on into the ride, +Y), or away from it, when the line's cell does not link to it
		var (host, guest, _, _) = Shared( ( park, _ ) =>
		{
			var kart = PlaceKartRide( park );
			kart.Visitors.OwnerWord = ParkCellMap.CellWord( kart.Placement.X, kart.Placement.Y );
			kart.Visitors.HasCells = true;
			// as a placement through the park registers it with the guests, whose rides the re-evaluation looks up
			park.Level.Guests!.Register( kart.Visitors );
			park.Level.Guests!.Grid.Cells.SetType( 8, 21, ParkCellTypes.Entrance );
			park.Level.Guests.Grid.Cells.SetOwner( 8, 21, kart.Visitors.OwnerWord );
			park.Level.Guests.Grid.Cells.SetQueueLink( 8, 21, GuestPathGrid.LinkValue( linked ? 2 : 1 ) );
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var seat = guest.Level.Commands.Player;
		var ride = host.Level.TrackStations.Keys.Single();
		var tool = StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		Assert.AreEqual( linked, (host.Level.Guests!.Grid.Cells.LinksAt( 8, 20 ) & GuestPathGrid.LinkValue( 2 )) != 0, "the line's cell links to the entrance it faces" );
		// the host claims the ride: the guest's take-back would re-evaluate it through that link
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( ride ) );
		RoundsUntil( () => guest.Level.ClaimOf( ride.Runtime.AttractionId ) == ParkCommands.LocalPlayer, host, guest, view, "the host's claim reached the guest" );
		if ( !linked )
		{
			Assert.IsTrue( GuestBackspace( host, guest, view ), "an entrance the line's cell does not link to is not re-evaluated: taken" );
			Assert.AreEqual( 0, view.Says( OpenTpwText.CoopClaimed ) );
			Assert.IsFalse( Walkable( host, 8, 20 ) || Walkable( guest, 8, 20 ) );
			CatchUpAndCompare( host, guest );
			return;
		}
		Assert.IsFalse( GuestBackspace( host, guest, view ), "refused by the rights" );
		Assert.IsTrue( tool.IsActive && tool.SegmentCount == 1, "another player's claim keeps the step and the tool, as a line refused there" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimed ) );
		Assert.IsTrue( Walkable( host, 8, 20 ) && Walkable( guest, 8, 20 ), "nothing taken back" );
		Assert.AreEqual( TakeBackVerdict.Taken, TakeBacksOf( host, seat ).Single().Verdict, "decided, then refused by the rights" );
		// the claim let go, the same tool's Backspace takes the step back
		host.Enter();
		host.Level.ExitTrackTool();
		ViewOf( host ).Update();
		RoundsUntil( () => guest.Level.ClaimOf( ride.Runtime.AttractionId ) == null, host, guest, view, "the host let the claim go" );
		Assert.IsTrue( GuestBackspace( host, guest, view ) );
		Assert.IsFalse( Walkable( host, 8, 20 ) || Walkable( guest, 8, 20 ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGrantTakenBackWhileATakeBackIsOnItsWayRefusesItAndEndsTheTool()
	{
		var (host, guest, view) = UndoSession();
		var seat = guest.Level.Commands.Player;
		var tool = StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		guest.Enter();
		Assert.IsTrue( tool.Undo() );
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, false ) ).Accepted, "the grant goes before the take-back reaches the host" );
		RoundsUntil( () => tool.WaitingUndo == null, host, guest, view, "the take-back came back" );
		Assert.IsFalse( tool.IsActive, "a refusal by the rights ends the tool" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ) );
		Assert.IsTrue( Walkable( host, 8, 20 ) && Walkable( guest, 8, 20 ), "nothing taken back" );
		Assert.IsTrue( TakeBacksOf( host, seat ).Single().Verdict == TakeBackVerdict.Taken, "decided, then refused by the rights" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsLineTheRightsRefuseOnItsWayEndsTheToolAndMakesNoStep()
	{
		var (host, guest, _, guestLink) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var seat = guest.Level.Commands.Player;
		var tool = StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		Assert.AreEqual( 1, host.Level.UndoJournal!.StepsOf( guestLink.Id ) );
		// the second line goes, and the host takes the grant back before its stream takes it: refused at its record, it lays nothing and ends the tool as any refused command does
		// (its segment's snapped end is its start), so no segment that laid nothing stays for a Backspace
		guest.Enter();
		Assert.IsNotNull( guest.Level.ClickCellTool( (12, 26) )?.Waiting, "the line went to the host" );
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, false ) ).Accepted );
		RoundsUntil( () => tool.Waiting == null, host, guest, view, "the line came back" );
		Assert.IsTrue( tool.LastCommit is { CommandRefused: true }, "it laid nothing" );
		Assert.IsFalse( tool.IsActive, "the refused line ended the tool" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ) );
		Assert.AreEqual( 1, host.Level.UndoJournal.StepsOf( guestLink.Id ), "the refused line made no step" );
		Assert.AreEqual( 0, TakeBacksOf( host, seat ).Length, "and nothing was sent back" );
		Assert.IsFalse( Walkable( host, 12, 24 ) || Walkable( guest, 12, 24 ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALineEndingOnAnotherPlayersPathEndsItsToolAndTheSegmentItEndedOnStaysChanged()
	{
		var (host, guest, hostLink, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var seat = guest.Level.Commands.Player;
		var tool = StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		// the host's line from above ends on the guest's cell (8, 20): a line ending on a path ends its tool, as the original's does, so no Backspace of the host's reaches it
		host.Enter();
		Assert.IsTrue( host.Level.EnterPathTool( 8, 16 ) );
		Assert.IsTrue( host.Level.ClickCellTool( (8, 20) ) is { Completed: true, EndedOnExisting: true }, "the host's line" );
		Assert.IsFalse( host.Level.CellTool.IsActive, "the host's tool ended on the guest's path" );
		var joining = host.Level.UndoJournal!.LastRecord!.Value;
		Assert.AreEqual( 0, host.Level.UndoJournal.StepsOf( hostLink.Id ), "a line that ends its tool is no step" );
		// nor does a take-back of it made by hand take anything (the user's decision: as the original)
		Assert.IsFalse( host.Issue( new TakeBackCommand( false, joining, -1, -1, -1 ) ).Accepted );
		Assert.AreEqual( TakeBackVerdict.NotOwn, TakeBacksOf( host, 0 ).Single().Verdict );
		Assert.IsTrue( Walkable( host, 8, 18 ) );
		Rounds( 5, host, guest );
		Assert.IsFalse( GuestBackspace( host, guest, view ), "refused while the host's line stands on its cell" );
		Assert.AreEqual( TakeBackVerdict.Changed, TakeBacksOf( host, seat ).Single().Verdict );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopUndoChanged ) );
		Assert.IsTrue( tool.IsActive && tool.SegmentCount == 1, "the guest's tool keeps its segment" );
		Assert.IsTrue( Walkable( host, 8, 18 ) && Walkable( host, 10, 20 ) && Walkable( guest, 10, 20 ), "nothing changed" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ABackspaceWhileATakeBackWaitsSaysSoAndSendsNothing()
	{
		var (host, guest, view) = UndoSession();
		var seat = guest.Level.Commands.Player;
		var tool = StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		GuestClick( host, guest, view, (12, 26) );
		HudBackspace( view );
		Assert.IsNotNull( tool.WaitingUndo, "the first went to the host" );
		var waits = guest.Level.WaitingClicks;
		HudBackspace( view );
		Assert.AreEqual( waits + 1, guest.Level.WaitingClicks, "the second waits for it" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopWaitingForHost ) );
		RoundsUntil( () => tool.WaitingUndo == null, host, guest, view, "the take-back came back" );
		Assert.AreEqual( 1, TakeBacksOf( host, seat ).Length, "one take-back went" );
		Assert.AreEqual( 1, tool.SegmentCount );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestTakesItsTwoPathSegmentsBackOneBackspaceAtATimeOnEveryPeer()
	{
		var (host, guest, view) = UndoSession();
		var seat = guest.Level.Commands.Player;
		var tool = StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		GuestClick( host, guest, view, (12, 26) );
		Assert.AreEqual( 2, tool.SegmentCount );
		Assert.IsTrue( Walkable( host, 12, 24 ) && Walkable( guest, 12, 24 ) );
		var balance = host.Economy.Balance;
		Assert.IsTrue( GuestBackspace( host, guest, view ), "the second segment taken back" );
		Assert.IsFalse( Walkable( host, 12, 24 ) || Walkable( guest, 12, 24 ), "its cells gone on both parks" );
		Assert.IsTrue( Walkable( host, 8, 20 ) && Walkable( guest, 8, 20 ), "the first stays" );
		Assert.AreEqual( (12, 20), tool.Start, "the tool goes on from the segment's start" );
		Assert.IsTrue( GuestBackspace( host, guest, view ), "the first segment taken back" );
		Assert.IsFalse( Walkable( host, 8, 20 ) || Walkable( guest, 8, 20 ) );
		Assert.AreEqual( 0, tool.SegmentCount );
		Assert.AreEqual( balance, host.Economy.Balance, "a path's undo refunds nothing, as the original's" );
		var takeBacks = TakeBacksOf( host, seat );
		Assert.AreEqual( 2, takeBacks.Length );
		Assert.IsTrue( takeBacks.All( command => command.Verdict == TakeBackVerdict.Taken && command.Inverse is UndoLineCommand { Ride: -1 } ), "the host decided each, re-evaluating no ride" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopUndoChanged ) + view.Says( OpenTpwText.CoopUndoNotOwn ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AnotherPlayersLineAcrossAGuestsCellRefusesItsTakeBackUntilThatLineIsTakenBack()
	{
		var (host, guest, view) = UndoSession();
		var tool = StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		// the host's line crosses the guest's cell (8, 20)
		var hostTool = HostLine( host, (8, 17), (8, 23) );
		Rounds( 5, host, guest );
		Assert.IsFalse( GuestBackspace( host, guest, view ), "refused: another player's line still crosses it" );
		Assert.AreEqual( 1, tool.SegmentCount, "the segment stays" );
		Assert.IsTrue( Walkable( host, 10, 20 ) && Walkable( guest, 10, 20 ), "nothing changed" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopUndoChanged ) );
		// the host takes its own line back at once
		host.Enter();
		Assert.IsTrue( hostTool.Undo(), "the host's own take-back" );
		Assert.IsFalse( Walkable( host, 8, 22 ) );
		Assert.IsTrue( GuestBackspace( host, guest, view ), "now taken" );
		Assert.IsFalse( Walkable( host, 8, 20 ) || Walkable( guest, 8, 20 ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALinkIntoACellAGuestsLineOnlyCrossedLeavesItsTakeBackThrough()
	{
		var (host, guest, view) = UndoSession();
		var cells = host.Level.Guests!.Grid.Cells;
		// the host's column ends at (8, 20); the guest's row crosses it there
		HostLine( host, (8, 17), (8, 20) ).Cancel();
		var counter = cells.PlacementCountAt( 8, 20 );
		var tool = StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		Assert.AreEqual( counter + 1, cells.PlacementCountAt( 8, 20 ), "crossed" );
		// the host's line down from (8, 21) links into that cell from below, its tool kept on
		var hostTool = HostLine( host, (8, 21), (8, 24) );
		Rounds( 5, host, guest );
		Assert.IsTrue( GuestBackspace( host, guest, view ), "the row's undo counts the cell down and unlinks its own cells there alone: no dependency" );
		Assert.AreEqual( 0, tool.SegmentCount );
		Assert.IsFalse( Walkable( host, 10, 20 ) || Walkable( guest, 10, 20 ) );
		Assert.IsTrue( Walkable( host, 8, 20 ) && Walkable( host, 8, 22 ) && Walkable( guest, 8, 22 ), "the column and the host's line stay" );
		Assert.AreEqual( counter, cells.PlacementCountAt( 8, 20 ) );
		Assert.AreNotEqual( 0, cells.LinksAt( 8, 20 ) & GuestPathGrid.LinkValue( 2 ), "the host's link into it stays" );
		host.Enter();
		Assert.IsTrue( hostTool.Undo(), "the host's own take-back after it" );
		Assert.IsFalse( Walkable( host, 8, 22 ) );
		Rounds( 5, host, guest );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheHostsBackspaceIsRefusedWhileAGuestsLineJoinsItsSegment()
	{
		var (host, guest, view) = UndoSession();
		var hostTool = HostLine( host, (5, 20), (12, 20) );
		Rounds( 5, host, guest );
		// the guest's line starts beside the host's end and links into it
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterPathTool( 13, 20 ) );
		GuestClick( host, guest, view, (13, 26) );
		host.Enter();
		Assert.IsFalse( hostTool.Undo(), "refused: the guest's line joins it" );
		Assert.AreEqual( TakeBackVerdict.Changed, TakeBackCommand.VerdictOf( hostTool.TakeUndoRefusal()?.Reason ) );
		Assert.AreEqual( 1, hostTool.SegmentCount, "the host's tool keeps its segment" );
		Assert.IsTrue( Walkable( host, 12, 20 ) );
		Assert.IsTrue( GuestBackspace( host, guest, view ), "the guest takes its line back" );
		host.Enter();
		Assert.IsTrue( hostTool.Undo(), "then the host's is taken" );
		Assert.IsFalse( Walkable( host, 8, 20 ) );
		Rounds( 5, host, guest );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AForgedTakeBackOfAnotherPlayersOrAnOlderStepTakesNothing()
	{
		var (host, guest, view) = UndoSession();
		var seat = guest.Level.Commands.Player;
		var hostTool = HostLine( host, (5, 30), (12, 30) );
		var hostRecord = hostTool.LastCommit!.Record!.Value;
		// the guest lays two lines in two tool runs
		StartPath( guest );
		GuestClick( host, guest, view, (12, 20) );
		var older = guest.Level.CellTool.LastCommit!.Record!.Value;
		guest.Level.CellTool.Cancel();
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterPathTool( 5, 24 ) );
		GuestClick( host, guest, view, (12, 24) );
		var requests = guest.Session!.GuestCommands!;
		var forged = new[] { requests.Submit( new TakeBackCommand( false, hostRecord, -1, -1, -1 ) )!, requests.Submit( new TakeBackCommand( false, older, -1, -1, -1 ) )! };
		RoundsUntil( () => forged.All( request => !request.IsOpen ), host, guest, view, "both came back" );
		Assert.AreEqual( TakeBackVerdict.NotOwn, TakeBacksOf( host, seat )[0].Verdict, "the host's step is not the guest's" );
		Assert.AreEqual( TakeBackVerdict.NotLast, TakeBacksOf( host, seat )[1].Verdict, "an older line while a newer stands" );
		Assert.IsTrue( forged.All( request => request.Result is { Accepted: false } ) );
		Assert.IsTrue( Walkable( host, 8, 30 ) && Walkable( host, 8, 20 ) && Walkable( host, 8, 24 ), "nothing taken" );
		// its newest line taken back, then the same record again: no such step any more
		var newest = guest.Level.CellTool.LastCommit!.Record!.Value;
		Assert.IsTrue( GuestBackspace( host, guest, view ) );
		var again = Requested( host, guest, view, new TakeBackCommand( false, newest, -1, -1, -1 ) );
		Assert.AreEqual( TakeBackVerdict.NotOwn, TakeBacksOf( host, seat ).Last().Verdict, "a step taken back already" );
		Assert.IsFalse( again.Accepted );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheHostsSlideBackspaceTakesBackItsOwnPiecesAndNeverAGuestsLaterOne()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		// the host lays the slide's first piece, then the guest the second on the same slide
		host.Enter();
		Assert.IsTrue( host.Level.EnterSlideTool( tower ) );
		var hostTool = host.Level.PoolTool!;
		hostTool.Press( 15, 19 );
		hostTool.Release();
		Rounds( 5, host, guest );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterSlideTool( tower ) );
		var guestTool = guest.Level.PoolTool!;
		guestTool.Press( 16, 19 );
		guestTool.Release();
		RoundsUntil( () => !guestTool.HasGuestBatches, host, guest, view, "the guest's piece came back" );
		Assert.AreEqual( 2, SlideOf( host )!.Pieces.Count );
		// the host's Backspace names its own piece, which is no longer the slide's last: refused, nothing changed
		host.Enter();
		Assert.IsFalse( hostTool.Undo() );
		Assert.AreEqual( TakeBackVerdict.Changed, TakeBackCommand.VerdictOf( hostTool.TakeUndoRefusal()?.Reason ) );
		Assert.AreEqual( 2, SlideOf( host )!.Pieces.Count, "the guest's piece stays" );
		// the guest takes its own back, then the host its own
		guest.Enter();
		Assert.IsTrue( guestTool.Undo() );
		RoundsUntil( () => !guestTool.HasGuestBatches, host, guest, view, "the guest's take-back came back" );
		Assert.AreEqual( 1, SlideOf( host )!.Pieces.Count );
		Assert.AreEqual( 1, SlideOf( guest )!.Pieces.Count );
		host.Enter();
		Assert.IsTrue( hostTool.Undo(), "the host's own piece taken back" );
		Assert.AreEqual( 0, SlideOf( host )?.Pieces.Count ?? 0, "the slide's last piece gone" );
		// with no piece of its own left, the host's Backspace takes nothing, quietly
		Assert.IsFalse( hostTool.Undo() );
		Assert.IsNull( hostTool.TakeUndoRefusal() );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopUndoChanged ) + view.Says( OpenTpwText.CoopUndoNotOwn ), "the guest heard nothing" );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( 0, SlideOf( guest )?.Pieces.Count ?? 0, "taken back on the guest's park too" );
	}

	/// <summary>Money for one queue cell and not two on both parks (the test's own change, alike outside the commands).</summary>
	private void MoneyForOneQueueCell( Park host, Park guest ) =>
		Alike( host, guest, economy => economy.Ledger.Post( LedgerCategory.OtherCosts, economy.Balance - (2 * economy.CellCost( CellPurchase.Queue ) - 1) ) );

	/// <summary>
	/// The host's and the guest's queue cells (the rectangle around the queue tests' queue) are alike in every field, owner words, queue links and residue included, which the canonical cell
	/// hash leaves out; then the host lays a later native segment from the queue's back cell and they are compared again.
	/// </summary>
	private void QueueCellsAlike( Park host, Park guest )
	{
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( CellsOf( host, (2, 0), (12, 8) ), CellsOf( guest, (2, 0), (12, 8) ), "every queue cell field alike" );
		Assert.IsTrue( host.Issue( new QueueSegmentCommand( host.One.Visitors.AttractionId, 10, 2, 8, 2 ) ).Accepted, "a later native segment" );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( CellsOf( host, (2, 0), (12, 8) ), CellsOf( guest, (2, 0), (12, 8) ), "alike after it too" );
	}

	/// <summary>Every field of the cells of the rectangle from <paramref name="from"/> to <paramref name="to"/>, as one string.</summary>
	private static string CellsOf( Park park, (int X, int Y) from, (int X, int Y) to )
	{
		var cells = park.Level.Guests!.Grid.Cells;
		return string.Join( " ", Enumerable.Range( from.Y, to.Y - from.Y + 1 ).SelectMany( y => Enumerable.Range( from.X, to.X - from.X + 1 ).Select( x => cells.FieldsAt( x, y ).ToString() ) ) );
	}

	[TestMethod]
	public void ANativeSegmentTheMoneyCutShortEndsItsToolAndIsNoStepItsResidueAsInSinglePlayer()
	{
		// a guest's line its record cut short ends its tool, as the click would have on the refused ghost: no step, so a take-back naming it, made by hand, takes nothing
		var (host, guest, _, guestLink) = Shared( ( park, _ ) => QueueOf( park ) );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		MoneyForOneQueueCell( host, guest );
		var seat = guest.Level.Commands.Player;
		// the segment builds (9, 2) and stops at (8, 2), its passes leaving queue metadata on the cells past the stop
		var segment = Requested( host, guest, view, new QueueSegmentCommand( host.One.Visitors.AttractionId, 10, 2, 6, 2 ) );
		Assert.IsTrue( segment.Result?.Value is SegmentResult { StoppedBy: CellBuildResult.NotEnoughMoney }, "stopped by the money" );
		var cells = host.Level.Guests!.Grid.Cells;
		Assert.AreEqual( ParkCellType.Queue, cells.TypeAt( 9, 2 ), $"its one cell (balance {host.Economy.Balance})" );
		Assert.IsTrue( cells.RawTypeAt( 8, 2 ) == 0 && cells.HasQueuePassResidue( 8, 2 ), "residue past the stop" );
		Assert.AreEqual( 0, host.Level.UndoJournal!.StepsOf( guestLink.Id ), "no step" );
		Alike( host, guest, economy => economy.Ledger.Post( LedgerCategory.OtherIncome, 2_000_000 ) );
		var taken = Requested( host, guest, view, new TakeBackCommand( false, segment.Record, -1, -1, -1 ) );
		Assert.IsFalse( taken.Accepted );
		Assert.AreEqual( TakeBackVerdict.NotOwn, TakeBacksOf( host, seat ).Single().Verdict );
		Assert.AreEqual( ParkCellType.Queue, cells.TypeAt( 9, 2 ), "its built cell stays" );
		Assert.IsTrue( cells.HasQueuePassResidue( 8, 2 ), "and the residue past the stop, as in single player" );
		CatchUpAndCompare( host, guest );
		Assert.IsTrue( guest.Level.Guests!.Grid.Cells.HasQueuePassResidue( 8, 2 ) );
		QueueCellsAlike( host, guest );
	}

	[TestMethod]
	public void APoolBridgeOverAGuestsPathCellsPutsItsTakeBackInTheWayUntilItGoes()
	{
		var (host, guest, view) = UndoSession();
		PoolOnBoth( host, guest, (12, 10), (14, 20) );
		Rounds( 5, host, guest );
		// the guest's path up to the pool's edge; the host's bridge from it over the water
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterPathTool( 5, 15 ) );
		var tool = guest.Level.CellTool;
		GuestClick( host, guest, view, (11, 15) );
		Assert.IsTrue( host.Issue( new PlacePoolBridgeCommand( 10, 15, 1 ) ).Accepted, "the host's bridge over the guest's cells" );
		Assert.IsTrue( host.Level.PoolBridges!.Covers( 10, 15 ) );
		Rounds( 5, host, guest );
		Assert.IsFalse( GuestBackspace( host, guest, view ), "the bridge stands on its cells" );
		Assert.AreEqual( TakeBackVerdict.InTheWay, TakeBacksOf( host, guest.Level.Commands.Player ).Last().Verdict );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopUndoInTheWay ) );
		Assert.IsTrue( Walkable( host, 8, 15 ) );
		// the bridge gone, the cause is gone: taken
		Assert.IsTrue( host.Issue( new RemovePoolBridgeCommand( 10, 15 ) ).Accepted );
		Rounds( 5, host, guest );
		Assert.IsTrue( GuestBackspace( host, guest, view ) );
		Assert.IsFalse( Walkable( host, 8, 15 ) || Walkable( guest, 8, 15 ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALineCrossingAPathCellUnderAPoolBridgeIsTakenBackAndTheBridgeStays()
	{
		var (host, guest, view) = UndoSession();
		PoolOnBoth( host, guest, (12, 10), (14, 20) );
		Rounds( 5, host, guest );
		// the host's path to the pool's edge, and its bridge from (10, 15) over the water
		HostLine( host, (5, 15), (11, 15) ).Cancel();
		Assert.IsTrue( host.Issue( new PlacePoolBridgeCommand( 10, 15, 1 ) ).Accepted );
		Assert.IsTrue( host.Level.PoolBridges!.Covers( 10, 15 ) );
		Rounds( 5, host, guest );
		var cells = host.Level.Guests!.Grid.Cells;
		var counter = cells.PlacementCountAt( 10, 15 );
		// the guest's line down across the bridge's path cell: it only counts that cell up
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterPathTool( 10, 11 ) );
		GuestClick( host, guest, view, (10, 19) );
		Assert.IsTrue( guest.Level.CellTool.LastCommit is { Completed: true } && cells.PlacementCountAt( 10, 15 ) == counter + 1, "a crossing under the bridge" );
		Assert.IsTrue( GuestBackspace( host, guest, view ), "its count-down leaves the bridge as it is: taken" );
		Assert.AreEqual( counter, cells.PlacementCountAt( 10, 15 ) );
		Assert.IsTrue( host.Level.PoolBridges.Covers( 10, 15 ) && Walkable( host, 10, 15 ) );
		Assert.IsFalse( Walkable( host, 10, 17 ) || Walkable( guest, 10, 17 ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TwoGuestsPlacingOnOneCellInOneFrameAgreeThatTheEarlierGotIt()
	{
		var (host, first, second) = SharedWithTwo();
		var (firstView, secondView) = (ViewOf( first ), ViewOf( second ));
		var guests = new[] { (first, firstView), (second, secondView) };
		RoundsOf( () => first.Level.TickCount > 20 && second.Level.TickCount > 20, "the parks run", host, guests );
		var seats = new[] { first.Level.Commands.Player, second.Level.Commands.Player };
		foreach ( var seat in seats )
			Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, true ) ).Accepted );
		RoundsOf( () => first.Level.HasBuildGrant( seats[0] ) && second.Level.HasBuildGrant( seats[1] ), "the grants arrived", host, guests );
		// both requests leave in one frame, before the host runs one: the host's order decides
		first.Enter();
		var one = first.Session!.GuestCommands!.Submit( first.Level.BuildCommand( first.One.Entry, 20, 12, 90 ) )!;
		second.Enter();
		var two = second.Session!.GuestCommands!.Submit( second.Level.BuildCommand( second.One.Entry, 20, 12, 90 ) )!;
		RoundsOf( () => !one.IsOpen && !two.IsOpen, "both answered", host, guests );
		Assert.IsTrue( one.Accepted, "the earlier request placed" );
		Assert.IsFalse( two.Accepted, "the later is refused, the cell taken" );
		Assert.IsTrue( one.Record < two.Record );
		foreach ( var park in new[] { host, first, second } )
			Assert.IsNotNull( park.Level.Objects.FindAt( 20, 12 ) );
		CutAndCompare( host, first, second );
	}

	[TestMethod]
	public void TwoGuestsLinesOverTheSameCellsInOneFrameAreBothLaidAndTheEarlierWaitsForTheLater()
	{
		var (host, first, second) = SharedWithTwo();
		var (firstView, secondView) = (ViewOf( first ), ViewOf( second ));
		var guests = new[] { (first, firstView), (second, secondView) };
		RoundsOf( () => first.Level.TickCount > 20 && second.Level.TickCount > 20, "the parks run", host, guests );
		var seats = new[] { first.Level.Commands.Player, second.Level.Commands.Player };
		foreach ( var seat in seats )
			Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, true ) ).Accepted );
		RoundsOf( () => first.Level.HasBuildGrant( seats[0] ) && second.Level.HasBuildGrant( seats[1] ), "the grants arrived", host, guests );
		first.Enter();
		var one = first.Session!.GuestCommands!.Submit( new LayLineCommand( CellToolMode.Path, -1, 5, 20, 12, 20 ) )!;
		second.Enter();
		var two = second.Session!.GuestCommands!.Submit( new LayLineCommand( CellToolMode.Path, -1, 8, 17, 8, 23 ) )!;
		RoundsOf( () => !one.IsOpen && !two.IsOpen, "both answered", host, guests );
		Assert.IsTrue( one.Accepted && two.Accepted, "overlapping lines are no conflict: both laid, the later crossing the earlier" );
		CoopGuestCommands.Request TakeBack( Park guest, CoopGuestCommands.Request line )
		{
			guest.Enter();
			var request = guest.Session!.GuestCommands!.Submit( new TakeBackCommand( false, line.Record, -1, -1, -1 ) )!;
			RoundsOf( () => !request.IsOpen, "the take-back came back", host, guests );
			return request;
		}
		Assert.AreEqual( TakeBackVerdict.Changed, TakeBackCommand.VerdictOf( TakeBack( first, one ).Result?.Reason ), "the later line crosses its cell" );
		Assert.IsTrue( TakeBack( second, two ).Accepted );
		Assert.IsTrue( TakeBack( first, one ).Accepted, "then taken" );
		foreach ( var park in new[] { host, first, second } )
			Assert.IsFalse( Walkable( park, 8, 20 ) || Walkable( park, 8, 22 ) || Walkable( park, 6, 20 ) );
		CutAndCompare( host, first, second );
	}

	[TestMethod]
	public void ATakeBackAndTheHostsLineOverItsCellsInOneFrameFollowTheHostsOrderEitherWay()
	{
		foreach ( var hostFirst in new[] { true, false } )
		{
			Cleanup();
			Initialize();
			var (host, guest, view) = UndoSession();
			var seat = guest.Level.Commands.Player;
			var tool = StartPath( guest );
			GuestClick( host, guest, view, (12, 20) );
			guest.Enter();
			Assert.IsTrue( tool.Undo() );
			if ( !hostFirst )
			{
				// the host's frames take the take-back before its own line
				for ( var round = 0; round < 600 && TakeBacksOf( host, seat ).Length == 0; round++ )
				{
					clock += 1 / 60.0;
					guest.Frame( 1 / 60.0 );
					guest.Session!.Step();
					host.Frame( 1 / 60.0 );
				}
				Assert.AreEqual( 1, TakeBacksOf( host, seat ).Length );
			}
			HostLine( host, (8, 17), (8, 23) ).Cancel();
			RoundsUntil( () => tool.WaitingUndo == null, host, guest, view, "the take-back came back" );
			var verdict = TakeBacksOf( host, seat ).Single().Verdict;
			Assert.AreEqual( hostFirst ? TakeBackVerdict.Changed : TakeBackVerdict.Taken, verdict, hostFirst ? "the host's line came first" : "the take-back came first" );
			Assert.AreEqual( hostFirst, Walkable( host, 6, 20 ), "the guest's cells off the host's line" );
			Assert.IsTrue( Walkable( host, 8, 22 ), "the host's line is laid either way" );
			CatchUpAndCompare( host, guest );
		}
	}

	[TestMethod]
	public void AGuestsSaleOfAnObjectItJustPlacedStaysTheSeparateRemovalWithItsFirstYearsFullScrap()
	{
		// the fixture's parks sell a removed object as a started park does (Level.ConnectObjectsToEconomy), alike on both before the session
		var (host, guest, _, _) = Shared( ( park, _ ) =>
			typeof( Level ).GetMethod( "ConnectObjectsToEconomy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.Invoke( park.Level, null ) );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var seat = guest.Level.Commands.Player;
		var balance = host.Economy.Balance;
		var placed = Requested( host, guest, view, guest.Level.BuildCommand( guest.One.Entry, 20, 12, 90 ) );
		Assert.IsTrue( placed.Accepted );
		var item = host.Level.Objects.FindAt( 20, 12 )!;
		var instance = host.Level.GetEconomyInstance( item );
		Assert.IsNotNull( instance, "the placed ride's economy record" );
		Assert.IsTrue( host.Economy.TryGetObject( instance.Value, out var state ) );
		Assert.AreEqual( 100, host.Economy.ScrapPercent( state ), "the rule's conditions: a ride nobody used, in its first 30 days, scraps at 100" );
		var scrap = host.Economy.ScrapValue( state );
		var placedAt = host.Economy.Balance;
		Assert.IsTrue( placedAt < balance && scrap > 0, "the placement was paid for" );
		var sold = Requested( host, guest, view, new RemoveAttractionCommand( item.Runtime.AttractionId ) );
		Assert.IsTrue( sold.Accepted, "the sale, no take-back" );
		// the sale credits the base cost at 100 percent, as in single player; what else the placement charged (its footprint's own costs) is no part of the object's scrap value
		Assert.AreEqual( placedAt + scrap, host.Economy.Balance, $"the full first-year scrap (the placement charged {balance - placedAt}, the scrap {scrap})" );
		Assert.AreEqual( balance, host.Economy.Balance, "the price was its base cost: the money is back where it was" );
		Assert.AreEqual( 0, TakeBacksOf( host, seat ).Length );
		// the sale of another player's object stays allowed, as since C3b
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 24, 12, 90 ) ).Accepted );
		var other = host.Level.Objects.FindAt( 24, 12 )!;
		Rounds( 5, host, guest );
		Assert.IsTrue( Requested( host, guest, view, new RemoveAttractionCommand( other.Runtime.AttractionId ) ).Accepted );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( host.Economy.Balance, guest.Economy.Balance );
	}

	[TestMethod]
	public void AGuestTakesBackThePiecesOfOneBatchEachByItsOwnBackspaceNewestFirst()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterSlideTool( tower ) );
		var tool = guest.Level.PoolTool!;
		// three pieces in one drag, sent before the next send time: one batch, one record
		tool.Press( 15, 19 );
		tool.Hover( 16, 19 );
		tool.Hover( 17, 19 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the pieces came back" );
		var seat = guest.Level.Commands.Player;
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is LayPoolSlidePiecesCommand ), "one batch" );
		Assert.AreEqual( 3, SlideOf( host )!.Pieces.Count );
		for ( var left = 2; left >= 0; left-- )
		{
			guest.Enter();
			Assert.IsTrue( tool.Undo() );
			RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the take-back came back" );
			Assert.AreEqual( left, SlideOf( host )?.Pieces.Count ?? 0 );
			Assert.AreEqual( left, TakeBacksOf( host, seat ).Last().Part, "the newest piece by its ordinal within the batch" );
		}
		Assert.IsTrue( TakeBacksOf( host, seat ).All( command => command.Verdict == TakeBackVerdict.Taken ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AForgedTakeBackOfAFinishedSlidesLastPieceIsInTheWayAndASoldTowersPiecesAreNoOnesAnyMore()
	{
		var (host, guest, _, guestLink) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var seat = guest.Level.Commands.Player;
		var laid = Requested( host, guest, view, new LayPoolSlidePiecesCommand( 13, 19, new[] { (15, 19), (16, 19), (17, 19), (17, 18) } ) );
		Assert.IsTrue( laid.Accepted && SlideOf( host ) is { IsComplete: true }, "laid to its splash" );
		// a finished slide takes riders: its pieces go only with its tower, whatever piece a take-back names
		Assert.IsFalse( Requested( host, guest, view, new TakeBackCommand( false, laid.Record, 3, -1, -1 ) ).Accepted );
		Assert.AreEqual( TakeBackVerdict.InTheWay, TakeBacksOf( host, seat ).Last().Verdict );
		Assert.IsFalse( Requested( host, guest, view, new TakeBackCommand( false, laid.Record, 1, -1, -1 ) ).Accepted );
		Assert.AreEqual( TakeBackVerdict.InTheWay, TakeBacksOf( host, seat ).Last().Verdict, "an older piece too, before the last-step rule" );
		Assert.IsTrue( SlideOf( host ) is { IsComplete: true } && SlideOf( host )!.Pieces.Count == 4 );
		// the tower sold: its slide's steps leave the journal, so no piece of a later slide on the same anchor can be taken back by them
		Assert.IsTrue( Requested( host, guest, view, new RemoveAttractionCommand( tower.Runtime.AttractionId ) ).Accepted );
		Assert.IsFalse( Requested( host, guest, view, new TakeBackCommand( false, laid.Record, 3, -1, -1 ) ).Accepted );
		Assert.AreEqual( TakeBackVerdict.NotOwn, TakeBacksOf( host, seat ).Last().Verdict );
		Assert.AreEqual( 0, host.Level.UndoJournal!.StepsOf( guestLink.Id ), "no step of the guest's is left" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ASinglePlayersSlideBackspaceStaysThePlainUndo()
	{
		var park = NewPark();
		park.Enter();
		Assert.IsTrue( park.Level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (12, 12), (18, 18) ) ).Completed );
		park.Level.Objects.Things ??= new ThingPool();
		var tower = park.Level.Objects.TryPlace( SlideTower( park ), 13, 19, 0, out _ )!;
		park.Level.Guests!.Grid.Cells.WriteFootprint( tower.Cells );
		Assert.IsTrue( park.Level.EnterSlideTool( tower ) );
		var tool = park.Level.PoolTool!;
		tool.Press( 15, 19 );
		tool.Release();
		Assert.AreEqual( 1, park.Level.PoolSlides!.Find( PoolSlide.KeyOf( 13, 19 ) )!.Pieces.Count );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 0, park.Level.PoolSlides.Find( PoolSlide.KeyOf( 13, 19 ) )?.Pieces.Count ?? 0 );
		Assert.IsTrue( park.Level.Commands.Log.Any( entry => entry.Command is UndoPoolSlidePieceCommand ) );
		Assert.IsFalse( park.Level.Commands.Log.Any( entry => entry.Command is TakeBackCommand ) );
	}

	[TestMethod]
	public void ASinglePlayersBackspaceStaysThePlainUndo()
	{
		var park = NewPark();
		park.Enter();
		Assert.IsTrue( park.Level.EnterPathTool( 5, 20 ) );
		Assert.IsTrue( park.Level.ClickCellTool( (12, 20) ) is { Completed: true } );
		Assert.IsNull( park.Level.CellTool.LastCommit!.Record, "no record outside a session" );
		Assert.IsTrue( park.Level.CellTool.Undo() );
		Assert.IsFalse( Walkable( park, 8, 20 ) );
		Assert.IsTrue( park.Level.Commands.Log.Any( entry => entry.Command is UndoLineCommand ) );
		Assert.IsFalse( park.Level.Commands.Log.Any( entry => entry.Command is TakeBackCommand ) );
	}
}
