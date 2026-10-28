using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-undo] The kart and water track tools' undo in a shared park (docs/COOP.md, "Conflicts and undo (C5)"; C5-3): a line tool's Backspace is a take-back the host's journal
/// decides, for a guest and the host alike. The host follows each member's line tool from its records (its entries, its lines, the restart a closing line carried), so a line's take-back
/// carries the first-line flag single player's Backspace would, and the result is single player's on a twin park after the same clicks. A line that ends its tool has no take-back; a line
/// whose claim ended or whose ride a cut changed becomes a tombstone, which only says why it is no longer taken back.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>A shared park with a kart ride on both parks (<paramref name="prepare"/> runs on each after its placement), the guest granted; its kart, ride, seat and member.</summary>
	private (Park Host, Park Guest, View View, OriginalObject Kart, int Ride, int Seat, uint Member) UndoKart( Action<Park, OriginalObject>? prepare = null )
	{
		var (host, guest, _, link) = Shared( ( park, _ ) =>
		{
			var kart = PlaceKartRide( park );
			prepare?.Invoke( park, kart );
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var kart = guest.Level.TrackStations.Keys.Single();
		return (host, guest, view, kart, kart.Runtime.AttractionId, guest.Level.Commands.Player, link.Id);
	}

	/// <summary>A park of a game of one with the same kart ride (<paramref name="prepare"/> runs on it after the placement): the twin a shared park's take-backs are held to.</summary>
	private (Park Park, OriginalObject Kart) TwinKart( Action<Park, OriginalObject>? prepare = null )
	{
		var twin = NewPark();
		twin.Enter();
		var kart = PlaceKartRide( twin );
		prepare?.Invoke( twin, kart );
		return (twin, kart);
	}

	/// <summary>Every track tile record of <paramref name="park"/> as text, so two parks' tracks compare equal.</summary>
	private static string TrackCellsOf( Park park ) => string.Join( "; ", park.Level.Guests!.Grid.Cells.Tracks.Cells().Select( item =>
		$"({item.X},{item.Y}) t{item.Cell.Type} m{item.Cell.Mask} l{item.Cell.Link} f{item.Cell.Flags} o{item.Cell.Overlap} s{item.Cell.Segment} p{item.Cell.Parent} " +
		$"{item.Cell.Family}/{item.Cell.Piece}/{item.Cell.Rotation} b{item.Cell.BaseType} w{item.Cell.Owner}" ) );

	/// <summary>The twin's track tool on its kart as a placement starts it, and its clicks and Backspaces at once, as single player's are.</summary>
	private static void TwinTool( Park twin, OriginalObject kart )
	{
		twin.Enter();
		Assert.IsTrue( twin.Level.EnterTrackTool( kart, justPlaced: true ) );
	}

	private static void TwinClick( Park twin, (int X, int Y) cell, int modifiers = 0 )
	{
		twin.Enter();
		twin.Level.ClickCellTool( cell, modifiers );
	}

	/// <summary>The twin's Backspace: the money it gave back.</summary>
	private static long TwinBackspace( Park twin )
	{
		twin.Enter();
		var before = twin.Economy.Balance;
		Assert.IsTrue( twin.Level.CellTool.Undo(), "the twin's Backspace took its line back" );
		return twin.Economy.Balance - before;
	}

	/// <summary>The guest's line click on <paramref name="cell"/> as <see cref="GuestTrackLine"/> sends it, with the request its record answered.</summary>
	private CoopGuestCommands.Request GuestTrackLineRequest( Park host, Park guest, View view, (int X, int Y) cell, int modifiers = 0 )
	{
		guest.Enter();
		var tool = guest.Level.CellTool;
		Assert.IsNotNull( guest.Level.ClickCellTool( cell, modifiers )?.Waiting, $"the line to {cell} goes to the host" );
		var request = tool.Waiting!.Commit.Request;
		RoundsUntil( () => !tool.IsWaiting, host, guest, view, "the line came back" );
		return request;
	}

	/// <summary>The guest's Backspace in its track tool, a take-back sent to the host and back: the money it gave back on the host's park, and whether it took the line back.</summary>
	private (bool Taken, long Refund) GuestTrackBackspace( Park host, Park guest, View view )
	{
		var tool = guest.Level.CellTool;
		var segments = tool.SegmentCount;
		var before = host.Economy.Balance;
		guest.Enter();
		Assert.IsTrue( tool.Undo(), "the Backspace went to the host" );
		Assert.IsNotNull( tool.WaitingUndo );
		Assert.IsTrue( tool.WaitingUndo!.Request.Command is TakeBackCommand { Stroke: false, Part: -1, ClaimedRide: -1, Verdict: TakeBackVerdict.Undecided, Inverse: null },
			"a request names its line by its record alone: no ride, no flag" );
		RoundsUntil( () => tool.WaitingUndo == null, host, guest, view, "the take-back came back" );
		return (tool.SegmentCount == segments - 1, host.Economy.Balance - before);
	}

	/// <summary>A guest's request sent to the host and back with no HUD running (no frame check of its claim): the host's answer.</summary>
	private CoopGuestCommands.Request Asked( Park host, Park guest, ParkCommand command )
	{
		guest.Enter();
		var request = guest.Session!.GuestCommands!.Submit( command )!;
		for ( var round = 0; round < 600 && request.IsOpen; round++ )
			Rounds( 1, host, guest );
		Assert.IsFalse( request.IsOpen, $"the {command.Kind} came back" );
		return request;
	}

	/// <summary>The verdict a refused take-back's record carries, from its reason.</summary>
	private static TakeBackVerdict? VerdictOf( CoopGuestCommands.Request request ) => TakeBackCommand.VerdictOf( request.Result?.Reason );

	/// <summary>
	/// Leaves the kart's exit tile unlaid: as a fresh placement leaves it when its exit tile was refused (the station's second tile then has no arm toward it), or with
	/// <paramref name="legacy"/> as a migrated station of an older save lays only its two station tiles, each with its axis.
	/// </summary>
	private static void UnlayExit( Park park, OriginalObject kart, bool legacy = false )
	{
		var level = park.Level;
		var station = level.TrackStations[kart];
		var builder = level.BuilderOf( kart )!;
		builder.EraseTile( station.Exit.X, station.Exit.Y );
		if ( legacy )
		{
			builder.EraseTile( station.Entry.X, station.Entry.Y );
			builder.RemoveStation( station );
			builder.PlaceStation( station );
			return;
		}
		var second = builder.Tracks.Get( station.Second.X, station.Second.Y );
		builder.Tracks.Set( station.Second.X, station.Second.Y, second with { Mask = TrackHeading.Opposite( station.Heading ) } );
		builder.Refresh( station.Second.X, station.Second.Y );
	}

	[TestMethod]
	public void AGuestsTrackLinesAreTakenBackNewestFirstAsSinglePlayersBackspaceTakesThemBack()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		var (twin, twinKart) = TwinKart();
		var hostKart = host.Level.TrackStations.Keys.Single();
		GuestTrackTool( host, guest, view, kart );
		TwinTool( twin, twinKart );
		var tool = guest.Level.CellTool;
		// the walk to the open end leaves the exit on the stack twice: the tool's first line is laid on a stack of two points
		var loaded = tool.SegmentCount;
		Assert.AreEqual( 1, loaded );
		var clicks = new[] { (4, 6), (8, 6), (8, 2) };
		foreach ( var (x, y) in clicks )
		{
			GuestTrackLine( host, guest, view, KartAt( guest, kart, x, y ) );
			TwinClick( twin, KartAt( twin, twinKart, x, y ) );
		}
		Assert.AreEqual( loaded + 3, tool.SegmentCount );
		Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ), "the lines laid as single player's" );
		Assert.AreEqual( (3, 0), host.Level.UndoJournal!.TrackEntriesOf( member ), "each line a step of the guest's" );
		Assert.AreEqual( (false, 5), host.Level.UndoJournal.LineToolOf( member, ride ), "the host follows the guest's tool: five points on its stack" );
		for ( var line = 0; line < clicks.Length; line++ )
		{
			var (taken, refund) = GuestTrackBackspace( host, guest, view );
			Assert.IsTrue( taken, $"line {3 - line} taken back" );
			Assert.AreEqual( TwinBackspace( twin ), refund, "its tiles' refunds, as single player's" );
			Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ), "the track as single player's Backspace leaves it" );
		}
		Assert.AreEqual( loaded, tool.SegmentCount, "the tool's own lines went off its stack" );
		var takeBacks = TakeBacksOf( host, seat );
		Assert.AreEqual( 3, takeBacks.Length );
		Assert.IsTrue( takeBacks.All( command => command is { Verdict: TakeBackVerdict.Taken, ClaimedRide: var claimed, Inverse: UndoTrackLineCommand { Last: false } undo }
			&& claimed == ride && undo.Ride == ride ), "each decided on the line's ride, none the stack's first line" );
		Assert.AreEqual( (0, 0), host.Level.UndoJournal.TrackEntriesOf( member ) );
		Assert.AreEqual( (false, 2), host.Level.UndoJournal.LineToolOf( member, ride ), "back to the two points it was entered with" );
		// the stack's loaded segment was laid by no step of the guest's: Backspace sends nothing and says so
		HudBackspace( view );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopUndoNotOwn ) );
		Assert.AreEqual( loaded, tool.SegmentCount );
		Rounds( 5, host, guest );
		Assert.AreEqual( 3, TakeBacksOf( host, seat ).Length, "nothing more went to the host" );
		Assert.IsTrue( host.Level.ClaimOf( ride ) == seat && hostKart.Runtime.Bump is { IsTrackValid: false } );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( TrackCellsOf( host ), TrackCellsOf( guest ) );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AFirstLineFromAnUnlaidExitTakesBackWithTheFirstLineFlagAndSparesTheExitAsSinglePlayer( bool legacy )
	{
		{
			var (host, guest, view, kart, ride, seat, member) = UndoKart( ( park, placed ) => UnlayExit( park, placed, legacy ) );
			var (twin, twinKart) = TwinKart( ( park, placed ) => UnlayExit( park, placed, legacy ) );
			var exit = host.Level.TrackStations.Values.Single().Exit;
			Assert.IsFalse( host.Level.BuilderOf( host.Level.TrackStations.Keys.Single() )!.IsTile( exit.X, exit.Y ), "no exit tile" );
			GuestTrackTool( host, guest, view, kart );
			TwinTool( twin, twinKart );
			Assert.AreEqual( (false, 1), host.Level.UndoJournal!.LineToolOf( member, ride ), "the walk to the open end found no laid exit: one point on the stack" );
			for ( var round = 0; round < 2; round++ )
			{
				GuestTrackLine( host, guest, view, KartAt( guest, kart, 4, 6 ) );
				TwinClick( twin, KartAt( twin, twinKart, 4, 6 ) );
				Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ) );
				var (taken, refund) = GuestTrackBackspace( host, guest, view );
				Assert.IsTrue( taken, legacy ? "a migrated station's first line" : "a fresh station's first line" );
				Assert.AreEqual( TwinBackspace( twin ), refund, "the cleared tiles' refunds, none for the spared exit" );
				Assert.IsTrue( TakeBacksOf( host, seat ).Last() is { Verdict: TakeBackVerdict.Taken, Inverse: UndoTrackLineCommand { Last: true } }, "the pop leaves one point: the repair's flag" );
				Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ), "the exit spared and repaired as single player's Backspace leaves it" );
				Assert.IsTrue( host.Level.BuilderOf( host.Level.TrackStations.Keys.Single() )!.IsTile( exit.X, exit.Y ), "the exit tile the line laid stays" );
				Assert.AreEqual( (false, 1), host.Level.UndoJournal.LineToolOf( member, ride ), "and the station's next line from it is a step again" );
			}
			CatchUpAndCompare( host, guest );
		}
	}

	[TestMethod]
	public void ARestartsFirstLineAfterAClosingLineWithExactlyCtrlTakesBackWithTheFirstLineFlag()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		var (twin, twinKart) = TwinKart();
		GuestTrackTool( host, guest, view, kart );
		TwinTool( twin, twinKart );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, KartLoop[0].X, KartLoop[0].Y ) );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, KartLoop[1].X, KartLoop[1].Y ) );
		var closing = GuestTrackLineRequest( host, guest, view, KartAt( guest, kart, KartLoop[2].X, KartLoop[2].Y ), 0xc );
		foreach ( var (x, y) in KartLoop )
			TwinClick( twin, KartAt( twin, twinKart, x, y ), x == KartLoop[2].X && y == KartLoop[2].Y ? 0xc : 0 );
		Assert.IsTrue( host.Level.Commands.Log.Select( entry => entry.Command ).OfType<LayTrackLineCommand>().Last() is { KeepsTool: true }, "the closing line carried its click's restart" );
		Assert.AreEqual( (true, 0), host.Level.UndoJournal!.LineToolOf( member, ride ), "closed: the restart comes" );
		Assert.AreEqual( (2, 0), host.Level.UndoJournal.TrackEntriesOf( member ), "the closing line is no step" );
		var tool = guest.Level.CellTool;
		Assert.IsTrue( tool.IsActive && tool.Start == null, "the line tool restarted without a start" );
		// the restart's first click sets its start, the second lays a line out of the loop's corner: the walk now ends in it
		guest.Enter();
		Assert.IsNull( guest.Level.ClickCellTool( KartAt( guest, kart, 4, 6 ) ) );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, 4, 10 ) );
		TwinClick( twin, KartAt( twin, twinKart, 4, 6 ) );
		TwinClick( twin, KartAt( twin, twinKart, 4, 10 ) );
		Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ) );
		Assert.AreEqual( (false, 2), host.Level.UndoJournal.LineToolOf( member, ride ), "the restart's first line laid on one point" );
		var (taken, refund) = GuestTrackBackspace( host, guest, view );
		Assert.IsTrue( taken );
		Assert.IsTrue( TakeBacksOf( host, seat ).Last() is { Verdict: TakeBackVerdict.Taken, Inverse: UndoTrackLineCommand { Last: true } } );
		Assert.AreEqual( TwinBackspace( twin ), refund );
		Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ), "as single player's Backspace leaves the restart's first line" );
		// the closing line has no take-back: a forged one is not the guest's
		Assert.AreEqual( TakeBackVerdict.NotOwn, VerdictOf( Asked( host, guest, new TakeBackCommand( false, closing.Record, -1, -1, -1 ) ) ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void ALineThatClosesTheCircuitIsNoStepWhetherItEndsOnANewTileOrAnExistingOne( bool newTile )
	{
		{
			// a new tile: the entry tile is not laid, and the station's first tile's arm toward it dangles; the closing line lays it, and the new tile takes that arm
			var (host, guest, view, kart, ride, seat, member) = UndoKart( ( park, placed ) =>
			{
				if ( newTile )
				{
					var station = park.Level.TrackStations[placed];
					park.Level.BuilderOf( placed )!.EraseTile( station.Entry.X, station.Entry.Y );
				}
			} );
			GuestTrackTool( host, guest, view, kart );
			GuestTrackLine( host, guest, view, KartAt( guest, kart, KartLoop[0].X, KartLoop[0].Y ) );
			GuestTrackLine( host, guest, view, KartAt( guest, kart, KartLoop[1].X, KartLoop[1].Y ) );
			var closing = GuestTrackLineRequest( host, guest, view, KartAt( guest, kart, KartLoop[2].X, KartLoop[2].Y ) );
			Assert.IsTrue( kart.Runtime.Bump!.IsTrackValid, "the circuit is closed" );
			Assert.IsTrue( closing.Result?.Value is TrackLineDone { Valid: true, Segment.EndedOnExisting: var existing } && existing != newTile,
				newTile ? "its end was no tile before the line: the walk alone says it closed" : "its end was a tile" );
			Assert.IsFalse( guest.Level.CellTool.IsActive, "without exactly Ctrl the close ended the tool" );
			Assert.IsNull( host.Level.UndoJournal!.LineToolOf( member, ride ), "the host's model: off" );
			Assert.IsTrue( host.Level.UndoJournal.TrackEntriesOf( member ) is var (live, buried) && live + buried == 2, "the closing line is no step" );
			Assert.AreEqual( TakeBackVerdict.NotOwn, VerdictOf( Asked( host, guest, new TakeBackCommand( false, closing.Record, -1, -1, -1 ) ) ), "no take-back reaches it" );
			// a line sent with no tool on (none is: after the tool's end a line needs an entry) is no step either, under the claim taken again
			RoundsUntil( () => host.Level.ClaimOf( ride ) == null, host, guest, view, "the ended tool let the claim go" );
			GuestClaims( host, guest, ride );
			var tiles = host.Level.BuilderOf( host.Level.TrackStations.Keys.Single() )!.Tiles().Count();
			var (from, to) = (KartAt( guest, kart, 4, 6 ), KartAt( guest, kart, 4, 10 ));
			var stray = Asked( host, guest, new LayTrackLineCommand( ride, from.X, from.Y, to.X, to.Y ) );
			Assert.IsTrue( stray.Accepted && host.Level.BuilderOf( host.Level.TrackStations.Keys.Single() )!.Tiles().Count() > tiles, "it laid its tiles" );
			Assert.IsTrue( host.Level.UndoJournal.TrackEntriesOf( member ) is var (after, gone) && after + gone == 2 && after == 0 );
			Assert.AreEqual( TakeBackVerdict.NotOwn, VerdictOf( Asked( host, guest, new TakeBackCommand( false, stray.Record, -1, -1, -1 ) ) ) );
			CatchUpAndCompare( host, guest );
		}
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 0xc )]
	public void TheHostsClosingLineIsNoStepAndItsRestartFollowsTheFlagItCarried( int modifiers )
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		var hostKart = host.Level.TrackStations.Keys.Single();
		var journal = host.Level.UndoJournal!;
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( hostKart, justPlaced: true ) );
		foreach ( var (x, y) in KartLoop )
			Assert.IsTrue( host.Level.ClickCellTool( KartAt( host, hostKart, x, y ), (x, y) == KartLoop[^1] ? modifiers : 0 ) is { Completed: true } );
		var closing = journal.LastRecord!.Value;
		Assert.IsTrue( hostKart.Runtime.Bump!.IsTrackValid );
		Assert.IsTrue( host.Level.Commands.Log.Select( entry => entry.Command ).OfType<LayTrackLineCommand>().Last().KeepsTool == (modifiers == 0xc ) );
		Assert.AreEqual( (2, 0), journal.TrackEntriesOf( journal.Host ), "the closing line is no step" );
		Assert.AreEqual( modifiers == 0xc ? (true, 0) : null, journal.LineToolOf( journal.Host, ride ) );
		Assert.AreEqual( modifiers == 0xc, host.Level.CellTool.IsActive && host.Level.CellTool.Start == null, "the tool restarted as the flag says" );
		Assert.AreEqual( TakeBackVerdict.NotOwn, TakeBackCommand.VerdictOf( host.Issue( new TakeBackCommand( false, closing, -1, -1, -1 ) ).Reason ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AClaimThatExpiresBeforeAWaitingTakeBackReachesTheHostRefusesItInTheClaimsWords()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		GuestTrackTool( host, guest, view, kart );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, 4, 6 ) );
		var tracks = TrackCellsOf( host );
		// five minutes without a command of the guest on the ride: the host lets the claim go before the guest's park hears of it, and the guest's Backspace goes meanwhile
		HostWaits( host, 301, step: 1 );
		Assert.IsNull( host.Level.ClaimOf( ride ) );
		Assert.AreEqual( (0, 1), host.Level.UndoJournal!.TrackEntriesOf( member ), "the line is a tombstone" );
		guest.Enter();
		Assert.IsTrue( guest.Level.CellTool.Undo() );
		RoundsUntil( () => guest.Level.CellTool.WaitingUndo == null, host, guest, view, "the take-back came back" );
		Assert.AreEqual( ride, TakeBacksOf( host, seat ).Single().ClaimedRide, "through the tombstone's ride" );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "the tool ended" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimLost ) );
		Assert.AreEqual( tracks, TrackCellsOf( host ), "nothing changed" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheHostsTrackToolTakesItsOwnLinesBackInASharedParkAndItsLoadedCornersAreNotItsOwn()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		var hostView = ViewOf( host );
		var (twin, twinKart) = TwinKart();
		var hostKart = host.Level.TrackStations.Keys.Single();
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( hostKart, justPlaced: true ) );
		TwinTool( twin, twinKart );
		var tool = host.Level.CellTool;
		var loaded = tool.SegmentCount;
		foreach ( var (x, y) in new[] { (4, 6), (8, 6) } )
		{
			host.Enter();
			Assert.IsTrue( host.Level.ClickCellTool( KartAt( host, hostKart, x, y ) ) is { Completed: true } );
			TwinClick( twin, KartAt( twin, twinKart, x, y ) );
		}
		Assert.IsTrue( tool.Waiting == null && tool.SegmentCount == loaded + 2 );
		for ( var line = 0; line < 2; line++ )
		{
			var balance = host.Economy.Balance;
			HudBackspace( hostView );
			Assert.AreEqual( loaded + 1 - line, tool.SegmentCount, "taken at once" );
			Assert.AreEqual( TwinBackspace( twin ), host.Economy.Balance - balance );
			Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ) );
		}
		Assert.IsTrue( TakeBacksOf( host, ParkCommands.LocalPlayer ).All( command => command is { Verdict: TakeBackVerdict.Taken, ClaimedRide: var claimed, Inverse: UndoTrackLineCommand }
			&& claimed == ride ) );
		Assert.AreEqual( 2, TakeBacksOf( host, ParkCommands.LocalPlayer ).Length );
		Assert.IsFalse( host.Level.Commands.Log.Any( entry => entry.Command is UndoTrackLineCommand ), "no undo by its content" );
		HudBackspace( hostView );
		Assert.AreEqual( 1, hostView.Says( OpenTpwText.CoopUndoNotOwn ), "the loaded corners were laid by no step of the host's" );
		Assert.AreEqual( 2, TakeBacksOf( host, ParkCommands.LocalPlayer ).Length, "nothing issued" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALineOverATileAtTheCountersLimitIsAStepWhoseTakeBackIsInTheWay()
	{
		// an owner-zero tile of an older save on the line's way, its counter at its limit: the line raises it by nothing, and its clear would count it down all the same
		var (host, guest, view, kart, ride, seat, member) = UndoKart( ( park, placed ) =>
		{
			var at = KartAt( park, placed, 4, 4 );
			var builder = park.Level.BuilderOf( placed )!;
			Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( at.X, at.Y ) );
			builder.Tracks.Set( at.X, at.Y, builder.Tracks.Get( at.X, at.Y ) with { Overlap = short.MaxValue } );
		} );
		GuestTrackTool( host, guest, view, kart );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, 4, 6 ) );
		var saturated = KartAt( host, host.Level.TrackStations.Keys.Single(), 4, 4 );
		Assert.AreEqual( short.MaxValue, host.Level.Guests!.Grid.Cells.Tracks.Get( saturated.X, saturated.Y ).Overlap );
		Assert.AreEqual( 1, host.Level.UndoJournal!.TrackEntriesOf( member ).Live, "a step all the same" );
		var tracks = TrackCellsOf( host );
		var balance = host.Economy.Balance;
		var (taken, _) = GuestTrackBackspace( host, guest, view );
		Assert.IsFalse( taken );
		Assert.AreEqual( TakeBackVerdict.InTheWay, TakeBacksOf( host, seat ).Single().Verdict );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopUndoInTheWay ) );
		Assert.AreEqual( tracks, TrackCellsOf( host ), "nothing changed" );
		Assert.AreEqual( balance, host.Economy.Balance );
		Assert.IsTrue( guest.Level.CellTool.IsActive, "the tool stays on with its line" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALineOfAnOlderSavesOwnerZeroTrackIsTakenBackAsSinglePlayersBackspace()
	{
		// the ride's track laid by a save without owner words: a line from the exit down two tiles, each of owner word 0
		void Legacy( Park park, OriginalObject placed )
		{
			var builder = park.Level.BuilderOf( placed )!;
			new TrackLineWriter( builder ).Commit( KartAt( park, placed, 4, 0 ), KartAt( park, placed, 4, 4 ) );
		}
		var (host, guest, view, kart, ride, seat, member) = UndoKart( Legacy );
		var (twin, twinKart) = TwinKart( Legacy );
		var corner = KartAt( host, host.Level.TrackStations.Keys.Single(), 4, 2 );
		Assert.AreEqual( 0, host.Level.Guests!.Grid.Cells.Tracks.Get( corner.X, corner.Y ).Owner );
		GuestTrackTool( host, guest, view, kart );
		TwinTool( twin, twinKart );
		Assert.AreEqual( KartAt( guest, kart, 4, 4 ), guest.Level.CellTool.Start, "the tool goes on from the legacy track's end" );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, 8, 4 ) );
		TwinClick( twin, KartAt( twin, twinKart, 8, 4 ) );
		var (taken, refund) = GuestTrackBackspace( host, guest, view );
		Assert.IsTrue( taken );
		Assert.AreEqual( TwinBackspace( twin ), refund );
		Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALinesWalkRewritingAnEarlierLinesTilesLeavesBothTakenBackNewestFirst()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		var (twin, twinKart) = TwinKart();
		GuestTrackTool( host, guest, view, kart );
		TwinTool( twin, twinKart );
		var hostKart = host.Level.TrackStations.Keys.Single();
		// the second line turns at the first line's end: that tile becomes a corner, whose flow link and piece the walk rewrites
		GuestTrackLine( host, guest, view, KartAt( guest, kart, 4, 6 ) );
		var turn = KartAt( host, hostKart, 4, 6 );
		var straight = host.Level.Guests!.Grid.Cells.Tracks.Get( turn.X, turn.Y );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, 0, 6 ) );
		TwinClick( twin, KartAt( twin, twinKart, 4, 6 ) );
		TwinClick( twin, KartAt( twin, twinKart, 0, 6 ) );
		Assert.AreNotEqual( straight.Mask, host.Level.Guests.Grid.Cells.Tracks.Get( turn.X, turn.Y ).Mask, "the later line changed the earlier line's end" );
		Assert.IsTrue( GuestTrackBackspace( host, guest, view ).Taken, "the newest line's own take-back" );
		TwinBackspace( twin );
		Assert.IsTrue( straight.SameAs( host.Level.Guests.Grid.Cells.Tracks.Get( turn.X, turn.Y ) ), "its walk's writes went back with it" );
		Assert.IsTrue( GuestTrackBackspace( host, guest, view ).Taken, "then the earlier line's: the lists cleaned by the real writes" );
		TwinBackspace( twin );
		Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ) );
		Assert.IsNull( host.Level.UndoJournal!.HistoryAt( turn.X, turn.Y ), "no step tracks it any more" );
		Assert.IsTrue( TakeBacksOf( host, seat ).All( command => command.Verdict == TakeBackVerdict.Taken ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALaterLinesWalkChangingAnEarlierLinesTileOutsideItsOwnClearLeavesTheEarlierLineTakenBackAfterIt()
	{
		// the first line runs over two tiles of path: a bridge, whose ends are ramps by their bridged neighbours along the flow
		var bridge = new[] { (6, 0), (8, 0) };
		void Paths( Park park, OriginalObject placed )
		{
			var cells = park.Level.Guests!.Grid.Cells;
			foreach ( var (x, y) in bridge.Select( tile => KartAt( park, placed, tile.Item1, tile.Item2 ) ) )
			{
				foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
					cells.SetType( cx, cy, ParkCellType.Path );
			}
		}
		var (host, guest, view, kart, ride, seat, member) = UndoKart( Paths );
		var (twin, twinKart) = TwinKart( Paths );
		GuestTrackTool( host, guest, view, kart );
		TwinTool( twin, twinKart );
		var hostKart = host.Level.TrackStations.Keys.Single();
		var tracks = host.Level.Guests!.Grid.Cells.Tracks;
		var lines = new[] { (10, 0), (10, 6), (8, 6), (8, -4) };
		var ramps = new[] { KartAt( host, hostKart, 6, 0 ) };
		TrackCell[]? laid = null;
		foreach ( var (x, y) in lines )
		{
			// the last line crosses the bridge's second tile, which then has four arms and is no bridge: the ramp before it, the first line's tile and none of the last line's, is a plain bridge
			if ( (x, y) == lines[^1] )
				laid = ramps.Select( tile => tracks.Get( tile.X, tile.Y ) ).ToArray();
			GuestTrackLine( host, guest, view, KartAt( guest, kart, x, y ) );
			TwinClick( twin, KartAt( twin, twinKart, x, y ) );
		}
		Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ), "the lines laid as single player's" );
		Assert.IsTrue( laid!.All( tile => (tile.Flags & TrackBuilder.FlagOverPath) != 0 && (tile.Flags & TrackCell.BridgeMask) == TrackCell.FlagBridgeUp ), "the bridge's first tile was a ramp" );
		for ( var index = 0; index < ramps.Length; index++ )
			Assert.IsFalse( laid[index].SameAs( tracks.Get( ramps[index].X, ramps[index].Y ) ), "the last line's walk changed a tile of the first line's beyond its own" );
		Assert.AreEqual( (lines.Length, 0), host.Level.UndoJournal!.TrackEntriesOf( member ) );
		var (taken, refund) = GuestTrackBackspace( host, guest, view );
		Assert.IsTrue( taken, "the last line's own take-back" );
		Assert.AreEqual( TwinBackspace( twin ), refund );
		Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ) );
		for ( var index = 0; index < ramps.Length; index++ )
			Assert.IsTrue( laid[index].SameAs( tracks.Get( ramps[index].X, ramps[index].Y ) ), "its undo's walk made the ramp again" );
		for ( var line = lines.Length - 1; line > 0; line-- )
		{
			(taken, refund) = GuestTrackBackspace( host, guest, view );
			Assert.IsTrue( taken, $"line {line} taken back after it, the first line's tiles no change of a later step's any more" );
			Assert.AreEqual( TwinBackspace( twin ), refund, "its tiles' refunds, as single player's" );
			Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ), "the track as single player's Backspace leaves it" );
		}
		Assert.AreEqual( (0, 0), host.Level.UndoJournal.TrackEntriesOf( member ) );
		Assert.IsTrue( ramps.All( tile => host.Level.UndoJournal.HistoryAt( tile.X, tile.Y ) == null ), "no step tracks the ramp any more" );
		Assert.IsTrue( TakeBacksOf( host, seat ).All( command => command.Verdict == TakeBackVerdict.Taken ) );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( TrackCellsOf( host ), TrackCellsOf( guest ) );
	}

	[TestMethod]
	public void ATakeBackIsDecidedOnItsStepsRideWhateverRideItsRequestNames()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		GuestTrackTool( host, guest, view, kart );
		var line = GuestTrackLineRequest( host, guest, view, KartAt( guest, kart, 4, 6 ) );
		// a forged request: another ride, a selected ride; the host puts the step's own ride there
		var forged = Asked( host, guest, new TakeBackCommand( false, line.Record, -1, host.One.Runtime.AttractionId, host.One.Runtime.AttractionId ) );
		Assert.IsTrue( forged.Accepted, forged.Result?.Reason );
		Assert.IsTrue( TakeBacksOf( host, seat ).Single() is { Verdict: TakeBackVerdict.Taken, ClaimedRide: var claimed, Inverse: UndoTrackLineCommand { Last: false } } && claimed == ride );
		// a take-back naming no step of the guest's at all: not its own, no ride to ask
		var none = Asked( host, guest, new TakeBackCommand( false, line.Record + 1000, -1, ride, -1 ) );
		Assert.AreEqual( TakeBackVerdict.NotOwn, VerdictOf( none ) );
		Assert.AreEqual( -1, TakeBacksOf( host, seat ).Last().ClaimedRide );
		// and an undo by its content is the host's tools' alone: a request cannot name the first-line flag
		var lay = (LayTrackLineCommand)line.Command;
		var undo = Asked( host, guest, new UndoTrackLineCommand( ride, lay.StartX, lay.StartY, lay.EndX, lay.EndY, Last: true ) );
		Assert.IsFalse( undo.Accepted );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AClaimLostWhileATakeBackWaitsRefusesItInTheClaimsWordsAndEndsTheTool()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		GuestTrackTool( host, guest, view, kart );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, 4, 6 ) );
		var tracks = TrackCellsOf( host );
		// the guest's Backspace goes; before it reaches the host, the host lets the guest's claim go and takes the ride
		guest.Enter();
		Assert.IsTrue( guest.Level.CellTool.Undo() );
		host.Enter();
		Assert.IsTrue( host.Issue( new ReleaseRideCommand( ride ) ).Accepted );
		Assert.AreEqual( (0, 1), host.Level.UndoJournal!.TrackEntriesOf( member ), "the line is a tombstone: the claim it needs ended" );
		Assert.IsTrue( host.Issue( new ClaimRideCommand( ride ) ).Accepted );
		RoundsUntil( () => guest.Level.CellTool.WaitingUndo == null, host, guest, view, "the take-back came back" );
		var refused = TakeBacksOf( host, seat ).Single();
		Assert.AreEqual( (TakeBackVerdict.Changed, ride), (refused.Verdict, refused.ClaimedRide), "decided on the tombstone's ride" );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "the claim's loss ended the tool" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimLost ) );
		Assert.AreEqual( tracks, TrackCellsOf( host ), "nothing changed" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ATombstoneOutlivesOtherMembersBoundsAndSaysChangedOnceItsMemberHoldsTheClaimAgain()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		GuestTrackTool( host, guest, view, kart );
		var line = GuestTrackLineRequest( host, guest, view, KartAt( guest, kart, 4, 6 ) );
		host.Enter();
		Assert.IsTrue( host.Issue( new ReleaseRideCommand( ride ) ).Accepted );
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == null && !guest.Level.CellTool.IsActive, host, guest, view, "the release reached the guest, whose tool ended" );
		// the host lays more steps than its bounds keep: the guest's tombstone is no part of them
		host.Level.UndoJournal!.StepBound = 1;
		var hostView = ViewOf( host );
		host.Enter();
		Assert.IsTrue( host.Level.EnterPathTool( 5, 20 ) );
		Assert.IsTrue( host.Level.ClickCellTool( (9, 20) ) is { Completed: true } );
		Assert.IsTrue( host.Level.ClickCellTool( (9, 24) ) is { Completed: true } );
		host.Level.CellTool.Cancel();
		Assert.AreEqual( (0, 1), host.Level.UndoJournal.TrackEntriesOf( member ), "the tombstone is still there" );
		// claimless, a forged take-back of it is refused by the rights in the claim's words, through its ride
		var lost = Asked( host, guest, new TakeBackCommand( false, line.Record, -1, -1, -1 ) );
		Assert.AreEqual( CoopBuildRights.ClaimLostReason, lost.Result?.Reason );
		Assert.AreEqual( ride, TakeBacksOf( host, seat ).Last().ClaimedRide );
		// with the claim again it is changed: its claim ended since the line
		GuestClaims( host, guest, ride );
		var changed = Asked( host, guest, new TakeBackCommand( false, line.Record, -1, -1, -1 ) );
		Assert.AreEqual( TakeBackVerdict.Changed, VerdictOf( changed ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AClaimHandedOverLeavesTheOldHoldersLinesToNoOneAndACutBuriesThem()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		var hostView = ViewOf( host );
		var hostKart = host.Level.TrackStations.Keys.Single();
		GuestTrackTool( host, guest, view, kart );
		var line = GuestTrackLineRequest( host, guest, view, KartAt( guest, kart, 4, 6 ) );
		// the guest leaves the ride: its claim goes, its line is a tombstone
		guest.Enter();
		guest.Level.ExitTrackTool();
		RoundsUntil( () => host.Level.ClaimOf( ride ) == null, host, guest, view, "the claim went" );
		Assert.AreEqual( (0, 1), host.Level.UndoJournal!.TrackEntriesOf( member ) );
		// the host takes the ride: the guest's line is no step of the host's, and the tool's loaded corners hold it as no step of the host's either
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( hostKart ) );
		var takeBacks = TakeBacksOf( host, ParkCommands.LocalPlayer ).Length;
		HudBackspace( hostView );
		Assert.AreEqual( 1, hostView.Says( OpenTpwText.CoopUndoNotOwn ) );
		Assert.AreEqual( takeBacks, TakeBacksOf( host, ParkCommands.LocalPlayer ).Length, "nothing sent" );
		host.Enter();
		Assert.AreEqual( TakeBackVerdict.NotOwn, TakeBackCommand.VerdictOf( host.Issue( new TakeBackCommand( false, line.Record, -1, -1, -1 ) ).Reason ), "a forged one is not the host's" );
		// the host's own line, then a close with exactly Ctrl (the tool stays on the ride, and so does its claim) and a cut: the cut buries the host's lines on the ride
		var journal = host.Level.UndoJournal;
		host.Enter();
		Assert.IsTrue( host.Level.ClickCellTool( KartAt( host, hostKart, 0, 6 ) ) is { Completed: true } );
		var hostLine = journal.LastRecord!.Value;
		Assert.AreEqual( (1, 0), journal.TrackEntriesOf( journal.Host ) );
		Assert.IsTrue( host.Level.ClickCellTool( KartAt( host, hostKart, -2, 6 ) ) is { Completed: true } );
		Assert.IsTrue( host.Level.ClickCellTool( KartAt( host, hostKart, -2, 0 ), 0xc ) is { Completed: true } );
		Assert.IsTrue( hostKart.Runtime.Bump!.IsTrackValid );
		Assert.AreEqual( (2, 0), journal.TrackEntriesOf( journal.Host ), "the closing line is no step" );
		hostView.Update();
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( hostKart ) );
		Assert.AreEqual( CellToolMode.KartEdit, host.Level.CellTool.Mode );
		Assert.IsNull( host.Level.CellTool.Click( KartAt( host, hostKart, -1, 5 ) ) );
		host.Level.CellTool.Click( KartAt( host, hostKart, -2, 2 ) );
		Assert.IsTrue( host.Level.Commands.Log.Any( entry => entry.Command is CutTrackCommand ), "the cut went" );
		Assert.AreEqual( (0, 2), journal.TrackEntriesOf( journal.Host ), "the cut buried the host's lines on the ride" );
		host.Enter();
		Assert.AreEqual( TakeBackVerdict.Changed, TakeBackCommand.VerdictOf( host.Issue( new TakeBackCommand( false, hostLine, -1, -1, -1 ) ).Reason ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheHostsTakeBackOfItsOwnLineAfterItsClaimWentIsRefusedInTheClaimsWords()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		var hostView = ViewOf( host );
		var hostKart = host.Level.TrackStations.Keys.Single();
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( hostKart, justPlaced: true ) );
		Assert.IsTrue( host.Level.ClickCellTool( KartAt( host, hostKart, 4, 6 ) ) is { Completed: true } );
		var line = host.Level.UndoJournal!.LastRecord!.Value;
		host.Level.ExitTrackTool();
		hostView.Update();
		Assert.IsNull( host.Level.ClaimOf( ride ), "its frame let the claim go" );
		var tracks = TrackCellsOf( host );
		host.Enter();
		var refused = host.Issue( new TakeBackCommand( false, line, -1, -1, -1 ) );
		Assert.AreEqual( CoopBuildRights.ClaimLostReason, refused.Reason );
		Assert.AreEqual( tracks, TrackCellsOf( host ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ATakenTakeBackRenewsTheClaimsLease()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		GuestTrackTool( host, guest, view, kart );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, 4, 6 ) );
		GuestTrackLine( host, guest, view, KartAt( guest, kart, 8, 6 ) );
		HostWaits( host, 200, step: 1 );
		Assert.IsTrue( GuestTrackBackspace( host, guest, view ).Taken );
		HostWaits( host, 200, step: 1 );
		Assert.AreEqual( seat, host.Level.ClaimOf( ride ), "the take-back renewed the five minutes" );
		HostWaits( host, 120, step: 1 );
		Assert.IsNull( host.Level.ClaimOf( ride ), "five minutes after it the claim went" );
	}

	[TestMethod]
	public void InASharedParkAFullTrackStackEndsTheToolBeforeItsLineGoes()
	{
		var (host, guest, view, kart, ride, seat, member) = UndoKart();
		var hostKart = host.Level.TrackStations.Keys.Single();
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( hostKart, justPlaced: true ) );
		Assert.IsTrue( host.Level.ClickCellTool( KartAt( host, hostKart, 4, 6 ) ) is { Completed: true } );
		var line = host.Level.UndoJournal!.LastRecord!.Value;
		// the tool's stack filled to its 1,025 points under the line: its click ends the tool before the next line goes
		var tool = host.Level.CellTool;
		var writer = tool.Writer!;
		var end = KartAt( host, hostKart, 4, 6 );
		var stack = Enumerable.Repeat( end, TrackEditor.CornerCapacity ).ToArray();
		var between = Enumerable.Range( 0, stack.Length - 1 ).Select( _ => new SegmentResult( end, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) { Start = end } ).ToArray();
		tool.Enter( writer, end, stack, between );
		var log = host.Level.Commands.Log.Count;
		var tracks = TrackCellsOf( host );
		Assert.IsNull( tool.Click( KartAt( host, hostKart, 8, 6 ) ) );
		Assert.IsFalse( tool.IsActive );
		Assert.AreEqual( log, host.Level.Commands.Log.Count, "no line went" );
		Assert.AreEqual( tracks, TrackCellsOf( host ) );
		// the line the tool showed last stays the journal's newest: its take-back is taken
		Assert.IsTrue( host.Issue( new TakeBackCommand( false, line, -1, -1, -1 ) ).Accepted );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void SinglePlayersTrackLinesKeepTheirBytesAndItsBackspaceItsUndo()
	{
		var (twin, twinKart) = TwinKart();
		TwinTool( twin, twinKart );
		TwinClick( twin, KartAt( twin, twinKart, 4, 6 ), 0xc );
		var lay = twin.Level.Commands.Log.Select( entry => entry.Command ).OfType<LayTrackLineCommand>().Single();
		Assert.IsFalse( lay.KeepsTool, "a game of one's line carries no restart" );
		Assert.AreEqual( 1, Convert.ToInt32( ParkCommandCodec.ToHex( lay )[..2], 16 ) / 2, "its bytes version 1" );
		TwinBackspace( twin );
		Assert.IsTrue( twin.Level.Commands.Log.Select( entry => entry.Command ).OfType<UndoTrackLineCommand>().Single() is { Last: false } );
		Assert.IsFalse( twin.Level.Commands.Log.Any( entry => entry.Command is TakeBackCommand ) );
	}
}
