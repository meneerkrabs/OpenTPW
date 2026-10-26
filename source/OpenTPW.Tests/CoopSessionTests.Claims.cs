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
		typeof( ParkHud ).GetField( "selectedObject", NonPublic )!.SetValue( view.Hud, guestKart );
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
		Assert.IsTrue( host.Level.Commands.Log.Any( entry => entry.Command is SetBuildGrantCommand { Seat: var released, Granted: false } && released == seat ),
			"a record of the stream: the departed seat's grant taken back, which lets go of its claim" );
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
}
