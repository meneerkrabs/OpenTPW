using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Online.Coop;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-claims] The rides' claims in a shared park (docs/COOP.md, "Claims"; C3c-1): the host's track tool claims its ride while it is on and lets it go at the next frame after
/// it left; a claim keeps its ride from every other player, the host included, in the claim's words; a guest's claim its track tool is not on goes once its claim is back; and the host lets
/// go of the claim of a seat that left, of one away for thirty seconds, also when a late frame takes its claim, and of one whose holder sent nothing for five minutes.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>
	/// A kart ride of the asset-free park: the level's track builders as the game sets them up, and a synthetic go-kart ride (the Dino Karts' controller type −4) placed at
	/// (<paramref name="x"/>, <paramref name="y"/>), its station tiles laid and no track; with <paramref name="hasQueue"/> a type with a queue. Run on every park alike before the session
	/// starts (<see cref="Shared"/>).
	/// </summary>
	private OriginalObject PlaceKartRide( Park park, int x = 20, int y = 9, bool hasQueue = false )
	{
		var level = park.Level;
		foreach ( var (name, value) in new (string, object)[]
		{
			("trackPieceAnimations", new TrackPieceAnimations()),
			("waterPieceHandlers", new Dictionary<OriginalObject, Action<bool>>()),
			("addOnSurfaces", new Dictionary<ObjectCatalogEntry, TrackSurface?>())
		} )
		{
			var field = typeof( Level ).GetField( name, NonPublic )!;
			if ( field.GetValue( level ) == null )
				field.SetValue( level, value );
		}
		// a second ride shares the level's builders
		if ( level.KartTracks == null )
			typeof( Level ).GetMethod( "SetupTracks", NonPublic )!.Invoke( level, null );
		var root = (string)typeof( UpgradeCatalogueDefaultsTests ).GetField( "root", NonPublic )!.GetValue( fixture )!;
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/karts", new ObjectSettings( new[] { new ObjectSettingsFile(
				"Info.Id 1150\nInfo.Name SyntheticKarts\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nBumper.WhichTrackType 1\nBumper.BumperType -4\n" + (hasQueue ? "Info.HasQueue 1\n" : ""), "karts.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(),
			new BaseFileSystem( root ), null );
		var ride = level.Objects.TryPlace( entry, x, y, 0, out _ );
		Assert.IsNotNull( ride?.Runtime.Bump, "the kart ride and its controller" );
		Assert.IsTrue( level.TrackStations.ContainsKey( ride! ), "a track ride's station" );
		return ride!;
	}

	/// <summary>The guest claims <paramref name="ride"/> by a request of its own (no tool: its HUD does not run, so its frame check sends no release), until both parks hold the claim.</summary>
	private int GuestClaims( Park host, Park guest, int ride )
	{
		var seat = guest.Level.Commands.Player;
		Assert.IsTrue( guest.Issue( new ClaimRideCommand( ride ) ).IsPending, "the claim goes to the host" );
		for ( var round = 0; round < 600 && (host.Level.ClaimOf( ride ) != seat || guest.Level.ClaimOf( ride ) != seat); round++ )
			Rounds( 1, host, guest );
		Assert.AreEqual( (seat, seat), (host.Level.ClaimOf( ride ), guest.Level.ClaimOf( ride )), "the claim is the guest's on both parks" );
		return seat;
	}

	/// <summary>The host alone for <paramref name="seconds"/> of the clock in steps of <paramref name="step"/>, its park held: the guests' commands and the claims' expiry run paused too.</summary>
	private void HostWaits( Park host, double seconds, double step = 0.5 )
	{
		var holds = host.Holds;
		host.Holds = true;
		for ( var waited = 0.0; waited < seconds - 1e-9; waited += step )
		{
			clock += step;
			host.Frame( step );
		}
		host.Holds = holds;
	}

	/// <summary>The host's releases of <paramref name="ride"/>'s claim in its log.</summary>
	private static int HostReleases( Park host, int ride ) =>
		host.Level.Commands.Log.Count( entry => entry.Command is ReleaseRideCommand release && release.Ride == ride && release.Player == ParkCommands.LocalPlayer );

	/// <summary>
	/// <paramref name="ride"/>'s entrance cell <paramref name="entrance"/> (type 9) beside the path cell <paramref name="path"/>: its owner word the ride's and its queue link pointing away from
	/// the path cell, which the path link writer then links into it (tp.exe <c>0x5348D0</c>), and the ride registered with the guests, as a placement through the park registers it: a path
	/// clear's re-evaluation looks the ride up there by the word (<c>Level.ReevaluateRideOfOwner</c>). Run on every park alike before the session starts.
	/// </summary>
	private static void LinkEntranceBeside( Park park, OriginalObject ride, (int X, int Y) entrance, (int X, int Y) path )
	{
		var guests = park.Level.Guests!;
		ride.Visitors.OwnerWord = ParkCellMap.CellWord( ride.Placement.X, ride.Placement.Y );
		ride.Visitors.HasCells = true;
		guests.Register( ride.Visitors );
		var cells = guests.Grid.Cells;
		var away = GuestPathGrid.LinkValue( Array.IndexOf( GuestPathGrid.Directions, (entrance.X - path.X, entrance.Y - path.Y) ) );
		cells.SetType( entrance.X, entrance.Y, ParkCellTypes.Entrance );
		cells.SetOwner( entrance.X, entrance.Y, ride.Visitors.OwnerWord );
		cells.SetQueueLink( entrance.X, entrance.Y, away );
		PathLinkWriter.LinkPath( cells, path.X, path.Y );
		Assert.AreNotEqual( 0, cells.LinksAt( path.X, path.Y ) & away, "the path cell links into the entrance" );
	}

	[TestMethod]
	public void TheHostsTrackToolClaimsItsRideWhileItIsOnAndAGuestsEditOfItIsRefusedInTheClaimsWords()
	{
		var (host, guest, _, _) = Shared( ( park, _ ) => PlaceKartRide( park ) );
		var view = ViewOf( guest );
		var hostView = ViewOf( host );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var kart = host.Level.TrackStations.Keys.Single();
		var ride = kart.Runtime.AttractionId;
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( kart ), "the tool claims its ride and starts" );
		Assert.AreEqual( ParkCommands.LocalPlayer, host.Level.ClaimOf( ride ) );
		Assert.AreSame( kart, host.Level.TrackToolStation );
		Assert.IsTrue( host.Level.Commands.Log.Any( entry => entry.Command is ClaimRideCommand claim && claim.Ride == ride ), "a record of the stream" );
		hostView.Update();
		Assert.AreEqual( ParkCommands.LocalPlayer, host.Level.ClaimOf( ride ), "the frame check keeps the claim of the ride the tool is on" );
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == ParkCommands.LocalPlayer, host, guest, view, "the claim reached the guest" );
		// the guest's window offers no sale, Move or queue tool for the ride, and a sale is refused here in the claim's words: nothing goes
		guest.Enter();
		var guestKart = guest.Level.TrackStations.Keys.Single();
		typeof( ParkHud ).GetProperty( "selectedObject", NonPublic | System.Reflection.BindingFlags.Instance )!.SetValue( view.Hud, guestKart );
		view.Update();
		var erase = ((UiScreen)typeof( ParkHud ).GetField( "hud", NonPublic )!.GetValue( view.Hud )!).Find( "deleteRide" ) as UiButton;
		Assert.IsFalse( erase!.Enabled, "the erase button is off for a ride another player claims" );
		Assert.IsFalse( (bool)typeof( ParkHud ).GetMethod( "CanMoveObject", NonPublic )!.Invoke( view.Hud, new object[] { guestKart } )!, "and the Move button" );
		var sale = guest.Issue( new RemoveAttractionCommand( ride ) );
		Assert.IsTrue( sale is { Accepted: false, RightsRefused: true } && sale.Reason == Level.ClaimedReason, sale.Reason );
		Assert.IsFalse( guest.Level.IsSaleWaiting( ride ) );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimed ) );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopHostOnly ) + view.Says( OpenTpwText.CoopNotGranted ) );
		// the tool leaves the ride: the host's next frame lets the claim go
		host.Enter();
		host.Level.ExitTrackTool();
		Assert.AreEqual( ParkCommands.LocalPlayer, host.Level.ClaimOf( ride ), "no tool event releases it" );
		hostView.Update();
		Assert.IsNull( host.Level.ClaimOf( ride ) );
		Assert.AreEqual( 1, HostReleases( host, ride ) );
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == null, host, guest, view, "the release reached the guest" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsClaimKeepsItsRideFromTheHostAndGoesOnceItsClaimIsBackWithoutATool()
	{
		var (host, guest, _, _) = Shared( ( park, _ ) => PlaceKartRide( park ) );
		var view = ViewOf( guest );
		var hostView = ViewOf( host );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var kart = host.Level.TrackStations.Keys.Single();
		var ride = kart.Runtime.AttractionId;
		var seat = GuestClaims( host, guest, ride );
		// the host's track tool starts on no ride another player claims, and says so; its sale and its demolition by the area clear are refused alike
		host.Enter();
		Assert.IsFalse( host.Level.EnterTrackTool( kart ) );
		Assert.IsNull( host.Level.TrackToolStation );
		hostView.Update();
		Assert.AreEqual( 1, hostView.Says( OpenTpwText.CoopClaimed ) );
		var sale = host.Issue( new RemoveAttractionCommand( ride ) );
		Assert.IsTrue( sale is { Accepted: false, RightsRefused: true } && sale.Reason == Level.ClaimedReason, sale.Reason );
		Assert.IsFalse( kart.IsDeleted );
		Assert.IsFalse( host.Level.Commands.AllowsRemoval( ParkCommands.LocalPlayer, kart ) );
		Assert.IsFalse( host.Level.Commands.Log.Any( entry => entry.Command is RemoveAttractionCommand ), "a refused command of the host leaves no record" );
		// the guest's HUD takes its claim back: no track tool is on the ride, so its frame check sends the release, once
		var sent = guest.Session!.GuestCommands!;
		RoundsUntil( () => host.Level.ClaimOf( ride ) == null && guest.Level.ClaimOf( ride ) == null, host, guest, view, "the stray claim went" );
		var guestsOwn = host.Level.Commands.Log.Where( entry => entry.Command.Player == seat ).Select( entry => entry.Command ).ToArray();
		Assert.AreEqual( 1, guestsOwn.Count( command => command is ReleaseRideCommand ), "one release" );
		Assert.IsTrue( Array.FindIndex( guestsOwn, command => command is ClaimRideCommand ) < Array.FindIndex( guestsOwn, command => command is ReleaseRideCommand ) );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopClaimed ) + view.Says( OpenTpwText.CoopHostOnly ), "the guest is told nothing" );
		Rounds( 120, host, guest );
		Assert.AreEqual( 1, host.Level.Commands.Log.Count( entry => entry.Command is ReleaseRideCommand && entry.Command.Player == seat ), "and sends no second" );
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( kart ), "the host's tool starts on the ride let go" );
		host.Level.ExitTrackTool();
		hostView.Update();
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheHostLetsGoOfTheClaimOfASeatThatLeft()
	{
		var (host, guest, _, guestLink) = Shared();
		var view = ViewOf( guest );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var ride = host.One.Runtime.AttractionId;
		var seat = GuestClaims( host, guest, ride );
		guest.Enter();
		guest.Session!.End( "The guest left." );
		Assert.AreEqual( 0, guest.Level.RideClaims.Count, "its park keeps no claim after the session" );
		network!.Remove( guestLink );
		for ( var round = 0; round < 600 && host.Level.ClaimOf( ride ) != null; round++ )
			Rounds( 1, host );
		Assert.IsNull( host.Level.ClaimOf( ride ), "the host released the departed seat's claim" );
		Assert.IsTrue( host.Level.Commands.Log.Any( entry => entry.Command is ReleaseSeatCommand { Seat: var released } && released == seat ),
			"a record of the stream: the departed seat's release, which takes its rights back and lets go of its claim" );
		Assert.IsFalse( host.Level.Commands.Log.Any( entry => entry.Command is SetBuildGrantCommand { Granted: false } ), "in the one record" );
	}

	[TestMethod]
	public void TakingAGuestsGrantBackLetsGoOfItsClaimInTheSameRecordOnEveryPeer()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var ride = host.One.Runtime.AttractionId;
		var seat = GuestClaims( host, guest, ride );
		// the host takes the grant back: the guest may build no more, so it holds the ride against no one
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, false ) ).Accepted );
		Assert.IsNull( host.Level.ClaimOf( ride ), "the grant's record let go of the claim" );
		Assert.AreEqual( 0, HostReleases( host, ride ), "with no release of its own" );
		RoundsUntil( () => !guest.Level.HasBuildGrant( seat ), host, guest, view, "the grant's record reached the guest" );
		Assert.IsNull( guest.Level.ClaimOf( ride ), "and let go of the claim there too" );
		// the host's own track tool takes the ride at once
		host.Enter();
		Assert.IsTrue( host.Issue( new ClaimRideCommand( ride ) ).Accepted );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheHostLetsGoOfTheClaimOfASeatAwayForThirtySecondsAndKeepsTheClaimOfOneThatCameBack()
	{
		var (host, guest, _, guestLink) = Shared();
		var view = ViewOf( guest );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var ride = host.One.Runtime.AttractionId;
		GuestClaims( host, guest, ride );
		// away for twenty seconds, then back: the claim stays
		guestLink.Drop();
		HostWaits( host, 20 );
		Assert.IsNotNull( host.Level.ClaimOf( ride ) );
		guestLink.Resume();
		HostWaits( host, 20 );
		Assert.IsNotNull( host.Level.ClaimOf( ride ), "a seat that came back keeps its claim" );
		// away again: thirty seconds from the first frame that read it so
		guestLink.Drop();
		HostWaits( host, 29 );
		Assert.IsNotNull( host.Level.ClaimOf( ride ), "not yet thirty seconds" );
		Assert.AreEqual( 0, HostReleases( host, ride ) );
		HostWaits( host, 1.5 );
		Assert.IsNull( host.Level.ClaimOf( ride ), "thirty seconds away" );
		Assert.AreEqual( 1, HostReleases( host, ride ), "the host's release, a record of the stream" );
	}

	[TestMethod]
	public void ALateFrameTakesTheQueuedClaimOfASeatAwayForThirtySecondsAndLetsItGoInTheSameFrame()
	{
		var (host, guest, hostLink, guestLink) = Shared();
		var view = ViewOf( guest );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var ride = host.One.Runtime.AttractionId;
		var seat = guest.Level.Commands.Player;
		// the session reads the guest away (its socket gone, the event not yet heard), and a host frame stamps it so
		hostLink.Snapshot = hostLink.Snapshot! with
		{
			Members = hostLink.Snapshot.Members.Select( member => member.Id == guestLink.Id ? member with { Connected = false } : member ).ToArray()
		};
		HostWaits( host, 0.5 );
		// the guest's claim reaches the host, whose loop then stalls for 31 seconds: one late frame takes the claim from its queue
		Assert.IsTrue( guest.Issue( new ClaimRideCommand( ride ) ).IsPending );
		var log = host.Level.Commands.Log.Count;
		host.Holds = true;
		clock += 31;
		host.Frame( 31 );
		host.Holds = false;
		var since = host.Level.Commands.Log.Skip( log ).ToArray();
		var claim = Array.FindIndex( since, entry => entry.Command is ClaimRideCommand && entry.Command.Player == seat );
		Assert.IsTrue( claim >= 0, "the late frame took the claim" );
		Assert.IsTrue( claim + 1 < since.Length && since[claim + 1].Command is ReleaseRideCommand release && release.Ride == ride && release.Player == ParkCommands.LocalPlayer,
			"and let it go right after it" );
		Assert.AreEqual( since[claim].Tick, since[claim + 1].Tick, "in the same frame" );
		Assert.IsNull( host.Level.ClaimOf( ride ) );
	}

	[TestMethod]
	public void AClaimWhoseHolderSendsNoCommandForItForFiveMinutesGoes()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var ride = host.One.Runtime.AttractionId;
		var seat = GuestClaims( host, guest, ride );
		HostWaits( host, 200 );
		// an accepted command of its holder naming the ride renews the lease: the claim again
		var claims = host.Level.Commands.Log.Count( entry => entry.Command is ClaimRideCommand && entry.Command.Player == seat );
		Assert.IsTrue( guest.Issue( new ClaimRideCommand( ride ) ).IsPending );
		for ( var round = 0; round < 600 && host.Level.Commands.Log.Count( entry => entry.Command is ClaimRideCommand && entry.Command.Player == seat ) == claims; round++ )
			Rounds( 1, host, guest );
		HostWaits( host, 200 );
		Assert.AreEqual( seat, host.Level.ClaimOf( ride ), "renewed: not five minutes since" );
		HostWaits( host, 101 );
		Assert.IsNull( host.Level.ClaimOf( ride ), "five minutes without its holder's command" );
		Assert.AreEqual( 1, HostReleases( host, ride ) );
	}

	[TestMethod]
	public void AHostsTrackToolThatWasOnWhenTheParkWasSharedClaimsItsRideAtTheFirstFrame()
	{
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			var kart = PlaceKartRide( park );
			// the same command on both parks, so they start alike; only the host's tool stays on
			Assert.IsTrue( park.Level.EnterTrackTool( kart ) );
			if ( !isHost )
				park.Level.ExitTrackTool();
		} );
		var view = ViewOf( guest );
		var hostView = ViewOf( host );
		var kart = host.Level.TrackStations.Keys.Single();
		var ride = kart.Runtime.AttractionId;
		Assert.AreSame( kart, host.Level.TrackToolStation, "the host's tool is on" );
		Assert.IsNull( host.Level.ClaimOf( ride ), "a session starts without a claim" );
		hostView.Update();
		Assert.AreEqual( ParkCommands.LocalPlayer, host.Level.ClaimOf( ride ), "the frame check claims the ride the tool is on" );
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == ParkCommands.LocalPlayer, host, guest, view, "the claim reached the guest" );
		hostView.Update();
		Assert.AreEqual( 1, host.Level.Commands.Log.Count( entry => entry.Command is ClaimRideCommand ), "once" );
		host.Enter();
		host.Level.ExitTrackTool();
		hostView.Update();
		Assert.IsNull( host.Level.ClaimOf( ride ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsClaimOfARideTheHostClaimsIsRefusedInTheClaimsWords()
	{
		var (host, guest, _, _) = Shared( ( park, _ ) => PlaceKartRide( park ) );
		var view = ViewOf( guest );
		var hostView = ViewOf( host );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var kart = host.Level.TrackStations.Keys.Single();
		var ride = kart.Runtime.AttractionId;
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( kart ) );
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == ParkCommands.LocalPlayer, host, guest, view, "the host's claim reached the guest" );
		var request = guest.Issue( new ClaimRideCommand( ride ) ).Request!;
		RoundsUntil( () => request.State != CoopGuestRequestState.Waiting && request.State != CoopGuestRequestState.Sent && view.Says( OpenTpwText.CoopClaimed ) == 1, host, guest, view,
			"the refusal came back and was said" );
		Assert.AreEqual( CoopGuestRequestState.Applied, request.State, "a record, refused by the park's rule" );
		Assert.IsFalse( request.Accepted );
		Assert.AreEqual( ParkCommands.LocalPlayer, host.Level.ClaimOf( ride ), "the claim stays the host's" );
		host.Enter();
		host.Level.ExitTrackTool();
		hostView.Update();
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheHostsUndoOfALineThatBuiltTheJoinOfAQueueAnotherPlayerClaimsIsRefused()
	{
		var park = NewPark();
		park.Enter();
		var level = park.Level;
		// One's queue from (10, 6) up to its back cell (10, 2), joined to the path at (10, 1), its claim another guest's
		ConfigureQueue( level.Guests!.Grid, park.One, 10, 5 );
		var rights = new CoopBuildRights( level );
		MarkStation( park, park.One, seat: 2 );
		try
		{
			Assert.IsFalse( rights.Allows( ParkCommands.LocalPlayer, new UndoLineCommand( CellToolMode.Path, -1, 10, 1, new[] { (10, 1) }, Array.Empty<(int, int)>() ) ),
				"an undo takes the cells its line built off again: the claimed queue's join among them" );
			Assert.IsTrue( rights.Allows( ParkCommands.LocalPlayer, new UndoLineCommand( CellToolMode.Path, -1, 12, 1, new[] { (12, 1) }, new[] { (10, 1) } ) ),
				"one that only counts the join down, a path cell its line crossed, takes no link off (ParkPathBuilder.Undo)" );
			Assert.IsFalse( rights.Allows( ParkCommands.LocalPlayer, new UndoLineCommand( CellToolMode.Path, -1, 12, 1, new[] { (12, 1) }, new[] { (10, 2) } ) ),
				"a claimed queue cell among them is still the claim's" );
			Assert.IsTrue( rights.Allows( ParkCommands.LocalPlayer, new UndoLineCommand( CellToolMode.Queue, level.Objects.Objects.First( item => !ReferenceEquals( item, park.One ) ).Runtime.AttractionId, 10, 3, Array.Empty<(int, int)>(), new[] { (10, 2) } ) ),
				"another ride's queue line's undo leaves the cells it crossed as they are (QueueLineWriter.Undo), the claimed queue's cell among them" );
			Assert.IsTrue( rights.Allows( ParkCommands.LocalPlayer, new UndoLineCommand( CellToolMode.Path, -1, 14, 1, new[] { (14, 1) }, new[] { (13, 1) } ) ), "away from it" );
		}
		finally
		{
			MarkStation( park, park.One, station: false );
		}
	}

	[TestMethod]
	public void AHostsClaimItsTrackToolIsNotOnGoesAtItsNextFrame()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		var hostView = ViewOf( host );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		var ride = host.One.Runtime.AttractionId;
		Assert.IsTrue( host.Issue( new ClaimRideCommand( ride ) ).Accepted );
		HostWaits( host, 600 );
		Assert.AreEqual( ParkCommands.LocalPlayer, host.Level.ClaimOf( ride ), "the host's own claim has no lease" );
		hostView.Update();
		Assert.IsNull( host.Level.ClaimOf( ride ), "its frame check lets it go: no track tool is on the ride" );
		Assert.AreEqual( 1, HostReleases( host, ride ) );
		CatchUpAndCompare( host, guest );
	}

	/// <summary>
	/// [EXT:online-coop-claims] A path cell linked into the entrance of a claimed ride is the claim's holder's to clear: its clear re-evaluates that ride (tp.exe <c>0x53694B</c>), which
	/// recomputes its queue, stamps a block and may open it. While the host claims the kart ride, the guest's remove click and Backspace over the cell are refused here in the claim's words and
	/// nothing changes; with the claim let go, the same Backspace and remove click take the cell, and its clear re-evaluates the ride on every peer.
	/// </summary>
	[TestMethod]
	public void AGuestsRemoveClickAndBackspaceOverAPathCellLinkedIntoTheEntranceOfARideTheHostClaimsAreRefusedInTheClaimsWords()
	{
		// the kart ride's entrance cell (12, 2) beside the path cell (12, 1), which links into it; each park counts the ride's re-evaluations
		var reevaluations = new Dictionary<Level, int>();
		var (host, guest, _, _) = Shared( ( park, _ ) =>
		{
			var placed = PlaceKartRide( park );
			LinkEntranceBeside( park, placed, (12, 2), (12, 1) );
			reevaluations[park.Level] = 0;
			placed.Visitors.QueueEdited += () => reevaluations[park.Level]++;
		} );
		var view = ViewOf( guest );
		var hostView = ViewOf( host );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var kart = host.Level.TrackStations.Keys.Single();
		var ride = kart.Runtime.AttractionId;
		var seat = guest.Level.Commands.Player;
		host.Enter();
		Assert.IsTrue( host.Level.EnterTrackTool( kart ), "the host's track tool claims the ride" );
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == ParkCommands.LocalPlayer, host, guest, view, "the host's claim reached the guest" );
		var before = (reevaluations[host.Level], reevaluations[guest.Level]);
		var links = (Links( host, 12, 1 ), Links( host, 12, 2 ));
		Assert.AreEqual( links, (Links( guest, 12, 1 ), Links( guest, 12, 2 )) );
		// the remove click on the path cell is refused here in the claim's words: no request goes to the host
		guest.Enter();
		var refusals = guest.Level.SessionRefusals;
		guest.Level.IsRemovingObjects = true;
		guest.Level.ClickRemoveTool( 12, 1 );
		Assert.IsNull( guest.Level.WaitingRemoval, "refused before it went" );
		Assert.AreEqual( (refusals + 1, CoopRefusal.Claimed), (guest.Level.SessionRefusals, guest.Level.LastSessionRefusal) );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimed ) );
		guest.Level.IsRemovingObjects = false;
		// and so is Backspace over it, a moment later (the HUD says a run of refusals once, ParkHud.SessionRefusalNoticeSeconds)
		Rounds( (int)(ParkHud.SessionRefusalNoticeSeconds * 60) + 1, host, guest );
		guest.Enter();
		SetCursor( view.Hud, (12, 1) );
		Assert.IsFalse( view.Hud.BacktrackKey(), "the key took nothing" );
		Assert.IsNull( guest.Level.WaitingBacktrack );
		Assert.AreEqual( (refusals + 2, CoopRefusal.Claimed), (guest.Level.SessionRefusals, guest.Level.LastSessionRefusal) );
		view.Update();
		Assert.AreEqual( 2, view.Says( OpenTpwText.CoopClaimed ), "the Backspace's refusal is said too" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopHostOnly ) + view.Says( OpenTpwText.CoopNotGranted ) );
		Assert.IsTrue( Walkable( host, 12, 1 ) && Walkable( guest, 12, 1 ), "the path cell stays" );
		Assert.AreEqual( links, (Links( host, 12, 1 ), Links( host, 12, 2 )), "linked into the entrance as before" );
		Assert.AreEqual( links, (Links( guest, 12, 1 ), Links( guest, 12, 2 )) );
		Assert.AreEqual( before, (reevaluations[host.Level], reevaluations[guest.Level]), "the ride was not re-evaluated" );
		Assert.IsFalse( HostCommandsOf( host, seat ).Any( command => command is RemoveAtCommand or BacktrackCommand ), "nothing went to the host" );
		// the claim let go, the same Backspace takes the cell, and its clear re-evaluates the ride on every peer
		host.Enter();
		host.Level.ExitTrackTool();
		hostView.Update();
		Assert.IsNull( host.Level.ClaimOf( ride ) );
		RoundsUntil( () => guest.Level.ClaimOf( ride ) == null, host, guest, view, "the release reached the guest" );
		guest.Enter();
		SetCursor( view.Hud, (12, 1) );
		Assert.IsTrue( view.Hud.BacktrackKey(), "the key sends the cell's backtrack" );
		Assert.IsNotNull( guest.Level.WaitingBacktrack );
		RoundsUntil( () => guest.Level.WaitingBacktrack == null, host, guest, view, "the Backspace came back" );
		Assert.IsFalse( Walkable( host, 12, 1 ) || Walkable( guest, 12, 1 ) );
		Assert.IsTrue( reevaluations[host.Level] > before.Item1 && reevaluations[guest.Level] > before.Item2, "the clear re-evaluated the ride on both parks" );
		// the host lays the cell again, whose line's link pass links it into the entrance, and the guest's remove click takes it
		Assert.IsTrue( host.Issue( new LayLineCommand( CellToolMode.Path, -1, 11, 1, 12, 1 ) ).Accepted );
		RoundsUntil( () => Walkable( guest, 12, 1 ), host, guest, view, "the host's line reached the guest" );
		Assert.AreNotEqual( 0, Links( guest, 12, 1 ) & GuestPathGrid.LinkValue( 2 ), "the line linked it into the entrance" );
		var laid = (reevaluations[host.Level], reevaluations[guest.Level]);
		guest.Enter();
		guest.Level.IsRemovingObjects = true;
		guest.Level.ClickRemoveTool( 12, 1 );
		Assert.IsNotNull( guest.Level.WaitingRemoval, "the removal went to the host" );
		RoundsUntil( () => guest.Level.WaitingRemoval == null, host, guest, view, "the removal came back" );
		guest.Level.IsRemovingObjects = false;
		Assert.IsFalse( Walkable( host, 12, 1 ) || Walkable( guest, 12, 1 ) );
		Assert.IsTrue( reevaluations[host.Level] > laid.Item1 && reevaluations[guest.Level] > laid.Item2, "re-evaluated again on both parks" );
		Assert.AreEqual( refusals + 2, guest.Level.SessionRefusals, "and nothing more was refused" );
		CatchUpAndCompare( host, guest );
	}

	/// <summary>
	/// [EXT:online-coop-claims] Every command that clears a path cell linked into the entrance of a ride another seat claims leaves it to the claim's holder, the host included: the remove click,
	/// the host's single-cell removal, Backspace, a path line's undo that built it, a native queue segment laid over it and that segment's undo, a placement over it and a kart or water track
	/// line over it, and the area clear leaves it standing. What takes no such link off stays anyone's: a path cell beside the entrance that does not link into it, a queue cell there, the
	/// queue tool's per-cell line (which lays no queue over path), a native segment that ends on the cell, a path line along it, an undo or a Backspace that only counts it down, and anything
	/// away from it.
	/// </summary>
	[TestMethod]
	public void EveryClearOfAPathCellLinkedIntoTheEntranceOfARideAnotherSeatClaimsIsTheClaimsHolders()
	{
		var park = NewPark();
		park.Enter();
		var level = park.Level;
		var grid = level.Guests!.Grid;
		// One's entrance cell (12, 2) beside the path cell (12, 1), which links into it; (13, 1) a path cell beside that one, not beside the entrance
		LinkEntranceBeside( park, park.One, (12, 2), (12, 1) );
		grid.SetPath( 13, 1, true );
		var other = level.Objects.Objects.First( item => !ReferenceEquals( item, park.One ) );
		var queueRide = other.Runtime.AttractionId;
		var kart = PlaceKartRide( park ).Runtime.AttractionId;
		CollectionAssert.Contains( TrackLineWriter.LayLine( (7, 0), (11, 0) ).SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ).ToArray(), (12, 1) );
		ParkCommand[] clears =
		{
			new RemoveAtCommand( 12, 1 ),
			new RemovePathCellCommand( 12, 1 ),
			new BacktrackCommand( 12, 1 ),
			new UndoLineCommand( CellToolMode.Path, -1, 12, 1, new[] { (12, 1) }, Array.Empty<(int, int)>() ),
			new QueueSegmentCommand( queueRide, 14, 1, 10, 1 ),
			new UndoQueueSegmentCommand( queueRide, 14, 1, 10, 1 ),
			level.BuildCommand( other.Entry, 12, 1, 0 ),
			new LayTrackLineCommand( kart, 7, 0, 11, 0 )
		};
		ParkCommand[] away =
		{
			new RemoveAtCommand( 13, 1 ),
			new BacktrackCommand( 13, 1 ),
			// the queue tool's per-cell line lays no queue over path (QueuePaths.CheckExtend) and only links one it ends on (QueuePaths.Connect); a native segment stops at a last path
			// cell (QueueSegmentReducer.Commit); a path line along the cell lays path over path
			new LayLineCommand( CellToolMode.Queue, queueRide, 14, 1, 10, 1 ),
			new LayLineCommand( CellToolMode.Queue, queueRide, 14, 1, 12, 1 ),
			new QueueSegmentCommand( queueRide, 14, 1, 12, 1 ),
			new LayLineCommand( CellToolMode.Path, -1, 14, 1, 10, 1 ),
			// an undo only counts down a cell its line crossed (ParkPathBuilder.Undo)
			new UndoLineCommand( CellToolMode.Path, -1, 13, 1, new[] { (13, 1) }, new[] { (12, 1) } ),
			level.BuildCommand( other.Entry, 24, 20, 0 ),
			new LayTrackLineCommand( kart, 7, 14, 11, 14 )
		};
		var rights = new CoopBuildRights( level );
		level.SetBuildGrant( 1, true );
		level.SetBuildGrant( 2, true );
		foreach ( var command in clears )
			Assert.IsTrue( rights.LeavesClaimsAlone( 1, command ) && rights.LeavesClaimsAlone( ParkCommands.LocalPlayer, command ), $"an unclaimed ride's entrance is anyone's: {command}" );
		Assert.IsTrue( rights.AllowsPathRemoval( 1, 12, 1 ) );
		// One is a track ride's station whose claim another guest holds
		MarkStation( park, park.One, seat: 2 );
		try
		{
			foreach ( var command in clears )
			{
				Assert.IsFalse( rights.LeavesClaimsAlone( 1, command ), $"a guest's: {command}" );
				Assert.IsFalse( rights.LeavesClaimsAlone( ParkCommands.LocalPlayer, command ), $"the host's: {command}" );
				Assert.IsTrue( rights.LeavesClaimsAlone( 2, command ), $"the claim's holder's own: {command}" );
			}
			Assert.AreEqual( Level.ClaimedReason, rights.RefusalReason( 1, new RemoveAtCommand( 12, 1 ) ), "refused in the claim's words" );
			Assert.AreEqual( Level.ClaimedReason, rights.RefusalReason( ParkCommands.LocalPlayer, new BacktrackCommand( 12, 1 ) ) );
			Assert.IsFalse( rights.AllowsPathRemoval( 1, 12, 1 ) || rights.AllowsPathRemoval( ParkCommands.LocalPlayer, 12, 1 ), "the area clear leaves it standing" );
			Assert.IsTrue( rights.AllowsPathRemoval( 2, 12, 1 ) && rights.AllowsPathRemoval( 1, 13, 1 ) );
			foreach ( var command in away )
				Assert.IsTrue( rights.LeavesClaimsAlone( 1, command ) && rights.LeavesClaimsAlone( ParkCommands.LocalPlayer, command ), $"no link into the entrance: {command}" );
			// laid twice, the cell only loses a placement to Backspace (ParkPathBuilder.RemoveOnce), which takes no link off; a removal still clears it
			grid.Cells.SetPlacementCount( 12, 1, 1 );
			Assert.IsTrue( rights.LeavesClaimsAlone( 1, new BacktrackCommand( 12, 1 ) ), "a Backspace that only counts the cell down" );
			Assert.IsFalse( rights.LeavesClaimsAlone( 1, new RemoveAtCommand( 12, 1 ) ) || rights.AllowsPathRemoval( 1, 12, 1 ) );
			grid.Cells.SetPlacementCount( 12, 1, 0 );
			Assert.IsFalse( rights.LeavesClaimsAlone( 1, new BacktrackCommand( 12, 1 ) ), "the press that clears it" );
			// a path cell beside the entrance that does not link into it, and a queue cell there linked toward it: neither clear re-evaluates the ride (a queue cell's takes only its cardinal
			// links off, PathLinkWriter.Unlink)
			grid.SetPath( 13, 2, true, connections: 0b0111 );
			grid.SetQueue( 11, 2, -1 );
			grid.Cells.SetLinks( 11, 2, GuestPathGrid.LinkValue( 1 ) );
			Assert.AreEqual( 0, Links( park, 13, 2 ) & GuestPathGrid.LinkValue( 3 ), "not linked into the entrance" );
			foreach ( var command in new ParkCommand[] { new RemoveAtCommand( 13, 2 ), new BacktrackCommand( 13, 2 ), new RemoveAtCommand( 11, 2 ), new BacktrackCommand( 11, 2 ) } )
				Assert.IsTrue( rights.LeavesClaimsAlone( 1, command ), $"beside it, no path cell linked into it: {command}" );
			Assert.IsTrue( rights.AllowsPathRemoval( 1, 13, 2 ) );
			// the same path cell linked into it is the claim's
			grid.SetPath( 13, 2, true );
			Assert.IsFalse( rights.LeavesClaimsAlone( 1, new RemoveAtCommand( 13, 2 ) ) || rights.AllowsPathRemoval( 1, 13, 2 ) );
		}
		finally
		{
			MarkStation( park, park.One, station: false );
		}
		Assert.IsTrue( rights.LeavesClaimsAlone( 1, new RemoveAtCommand( 12, 1 ) ) && rights.AllowsPathRemoval( 1, 12, 1 ), "the claim let go" );
	}

	/// <summary>
	/// [EXT:online-coop-claims] A guest's area clear, and the host's, leave the path cell linked into the entrance of a ride another seat claims standing, and clear the cells beside it, without
	/// re-evaluating the ride; the claim's holder's clear takes the cell, which re-evaluates it.
	/// </summary>
	[TestMethod]
	public void AnAreaClearLeavesThePathCellLinkedIntoTheEntranceOfAClaimedRideToTheClaimsHolder()
	{
		var park = NewPark();
		park.Enter();
		var level = park.Level;
		// One's entrance cell (12, 2) beside the path cell (12, 1), which links into it
		LinkEntranceBeside( park, park.One, (12, 2), (12, 1) );
		var reevaluations = 0;
		park.One.Visitors.QueueEdited += () => reevaluations++;
		var rights = new CoopBuildRights( level );
		level.SetBuildGrant( 1, true );
		level.SetBuildGrant( 2, true );
		var before = level.Commands.Rights;
		level.Commands.Rights = rights;
		MarkStation( park, park.One, seat: 2 );
		try
		{
			var guests = level.Commands.ApplyRemote( new ClearAreaCommand( 11, 0, 12, 1 ) { Player = 1 } );
			Assert.IsTrue( guests.Accepted, guests.Reason );
			Assert.AreEqual( (0, 1), guests.As<(int Objects, int Paths)>(), "the guest's clear takes the path cell beside it" );
			Assert.IsTrue( Walkable( park, 12, 1 ) && !Walkable( park, 11, 1 ) );
			Assert.AreNotEqual( 0, Links( park, 12, 2 ) & GuestPathGrid.LinkValue( 0 ), "the entrance keeps its link" );
			Assert.AreEqual( 0, reevaluations, "nothing re-evaluated the ride" );
			Assert.AreEqual( (0, 0), level.Commands.ApplyRemote( new ClearAreaCommand( 11, 0, 12, 1 ) ).As<(int Objects, int Paths)>(), "nor does the host's" );
			Assert.IsTrue( Walkable( park, 12, 1 ) );
			Assert.AreEqual( 0, reevaluations );
			// the claim's holder's clear takes it
			Assert.AreEqual( (0, 1), level.Commands.ApplyRemote( new ClearAreaCommand( 11, 0, 12, 1 ) { Player = 2 } ).As<(int Objects, int Paths)>() );
			Assert.IsFalse( Walkable( park, 12, 1 ) );
			Assert.AreEqual( 0, Links( park, 12, 2 ), "its clear took the entrance's link off" );
			Assert.AreEqual( 1, reevaluations, "and re-evaluated the ride" );
		}
		finally
		{
			MarkStation( park, park.One, station: false );
			level.Commands.Rights = before;
		}
	}
}
