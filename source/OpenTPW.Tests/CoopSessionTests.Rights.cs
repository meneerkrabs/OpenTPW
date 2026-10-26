using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-permissions] A shared park's rights per seat between its peers (docs/COOP.md, "Money and rights (C4)"; C4a): the rights command reaches every peer, and taking building back
/// lets go of the seat's claim in its record; a right taken back while a guest's request is on its way refuses the request at its record; the host's end releases every guest seat that holds
/// anything in records of its log, before the last checkpoint, and the log replays; the host's management of a ride a guest claims stays allowed and its log replays; [EXT:online-coop-speed]
/// a guest's speed buttons draw subdued and every speed input of the guest says that only the host can do that.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>The host sets the guest's rights through the rights command, until the guest's park holds them.</summary>
	private void GiveRights( Park host, Park guest, View view, CoopRights rights )
	{
		var seat = guest.Level.Commands.Player;
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, rights ) ).Accepted );
		RoundsUntil( () => guest.Level.RightsOf( seat ) == rights, host, guest, view, "the rights reached the guest" );
	}

	[TestMethod]
	public void TheRightsCommandReachesEveryPeerAndTakingBuildingBackLetsGoOfTheSeatsClaim()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Build );
		var ride = host.One.Runtime.AttractionId;
		var seat = GuestClaims( host, guest, ride );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.None ) ).Accepted );
		Assert.IsNull( host.Level.ClaimOf( ride ), "the rights' record let go of the claim" );
		Assert.AreEqual( 0, HostReleases( host, ride ), "with no release of its own" );
		RoundsUntil( () => guest.Level.SeatRights.Count == 0, host, guest, view, "the record reached the guest" );
		Assert.IsNull( guest.Level.ClaimOf( ride ), "and let go of the claim there too" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ARightTakenBackWhileAGuestsRequestIsOnItsWayRefusesItAtItsRecordOnEveryPeer()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Build );
		var tool = StartPath( guest );
		Assert.IsNotNull( guest.Level.ClickCellTool( (5, 24) )?.Waiting, "the line went to the host" );
		// the host takes every right back before its stream takes the request: the request stands after the rights' record in the stream
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( guest.Level.Commands.Player, CoopRights.None ) ).Accepted );
		RoundsUntil( () => tool.Waiting == null, host, guest, view, "the line came back" );
		Assert.IsFalse( Walkable( host, 5, 22 ) || Walkable( guest, 5, 22 ), "refused on every peer" );
		Assert.IsTrue( tool.LastCommit is { CommandRefused: true }, "it laid nothing" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ), "said in the rights' words" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CannotBuildHere ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheHostsEndReleasesEveryGuestSeatInItsLogBeforeItsLastCheckpointAndTheLogReplays()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var ride = host.One.Runtime.AttractionId;
		var seat = GuestClaims( host, guest, ride );
		CatchUpAndCompare( host, guest );
		// the host ends the share with the guest's right and claim held
		host.Enter();
		var hostStream = host.Session!.HostStream!;
		host.Session.End( "The host closed the park." );
		var log = ParkCommandLogFile.Deserialize( hostStream.SessionLog!.Serialize() );
		var releases = log.Entries.Where( entry => entry.Command is ReleaseSeatCommand ).ToArray();
		Assert.AreEqual( 1, releases.Length, "one record for the one seat that held anything" );
		Assert.AreEqual( seat, ((ReleaseSeatCommand)releases[0].Command).Seat );
		Assert.AreEqual( log.Entries[^1], releases[0], "the log's last record" );
		Assert.IsTrue( releases[0].Tick <= log.Checkpoints[^1].Ticks, "before its last checkpoint" );
		Assert.AreEqual( (0, (int?)null), (host.Level.SeatRights.Count, host.Level.ClaimOf( ride )), "the park runs on alone with no guest's right or claim" );
		Assert.IsNull( FirstReplayMismatch( log, park => MarkStation( park, park.One, seat: null ) ), "the session's log replays to its last checkpoint, the releases included" );
	}

	[TestMethod]
	public void TheHostsManagementOfARideAGuestClaimsStaysAllowedAndTheLogReplays()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		// the grant's command of C3 (101), as a C3 session's log holds it
		Grant( host, guest, view );
		var ride = host.One.Runtime.AttractionId;
		GuestClaims( host, guest, ride );
		// a claim guards building and removal against the host, not the host's management: the ride's open switch goes as it did in C3
		var open = host.One.IsOpen;
		Assert.IsTrue( host.Issue( new SetAttractionOpenCommand( ride, !open ) ).Accepted );
		Assert.AreEqual( !open, host.One.IsOpen );
		// no guest manages anything yet: no part offers the right (C4b), so a guest's switch is the host's to throw
		Assert.IsFalse( guest.Issue( new SetAttractionOpenCommand( ride, open ) ).Accepted );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopHostOnly ) );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( !open, guest.One.IsOpen, "the host's switch reached the guest" );
		host.Enter();
		var hostStream = host.Session!.HostStream!;
		host.Session.End( "The host closed the park." );
		var log = ParkCommandLogFile.Deserialize( hostStream.SessionLog!.Serialize() );
		Assert.IsTrue( log.Entries.Any( entry => entry.Command is SetBuildGrantCommand { Granted: true } ), "the log holds C3's grant" );
		Assert.IsNull( FirstReplayMismatch( log, park => MarkStation( park, park.One, seat: null ) ), "and replays, the host's switch accepted again" );
	}

	[TestMethod]
	public void AGuestsSpeedButtonsDrawSubduedAndEverySpeedInputSaysOnlyTheHostCanDoThat()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		var hostView = ViewOf( host );
		Rounds( 20, host, guest );
		UiButton Button( View of, GameSpeed speed ) => (UiButton)of.Hud.Stack.Screens[0].Find( "speed" + speed )!;
		var running = guest.Economy.Speed;
		foreach ( var speed in new[] { GameSpeed.Paused, GameSpeed.Normal, GameSpeed.Fast, GameSpeed.Fastest } )
		{
			var button = Button( view, speed );
			Assert.IsTrue( button.Enabled && button.Subdued!(), $"{speed}: subdued, and still the click's" );
			Assert.AreEqual( speed == running ? UiButton.DownFrame : UiButton.DisabledFrame, button.StateFrame( false, false ), $"{speed}: the host's speed shows" );
			Assert.IsFalse( Button( hostView, speed ).Subdued!(), "the host's own buttons are not" );
		}
		void Refused( Action input, string what )
		{
			Time.Update( ParkHud.SessionRefusalNoticeSeconds );
			var refusals = guest.Level.SessionRefusals;
			guest.Enter();
			input();
			view.Update();
			Assert.AreEqual( (refusals + 1, CoopRefusal.HostOnly), (guest.Level.SessionRefusals, guest.Level.LastSessionRefusal), $"{what}: refused by the session's rights, the host's alone" );
			Assert.AreEqual( view.Strings.Extra( OpenTpwText.CoopHostOnly ), view.Hud.Messages[^1], $"{what}: and said so" );
			Assert.AreEqual( running, guest.Economy.Speed, $"{what}: the guest's park runs on at the host's speed" );
		}
		Refused( () => Button( view, GameSpeed.Fastest ).Activate(), "a speed button" );
		Refused( () => view.Input( UiInput.Key( UiKeys.Pause ) ), "the pause key" );
		var shortcut = typeof( ParkHud ).GetMethod( "ApplyShortcut", NonPublic )!;
		foreach ( var key in new[] { HudScreenShortcut.TimeFaster, HudScreenShortcut.TimeSlower, HudScreenShortcut.TimeNormal } )
			Refused( () => shortcut.Invoke( view.Hud, new object[] { key } ), $"the numpad's {key}" );
		Assert.AreEqual( 0, host.Level.Commands.Log.Count( entry => entry.Command is SetGameSpeedCommand ), "nothing went to the host" );
		// the host's speed reaches the guest's buttons
		Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Fast ) ).Accepted );
		RoundsUntil( () => guest.Economy.Speed == GameSpeed.Fast, host, guest, view, "the host's speed reached the guest" );
		Assert.AreEqual( UiButton.DownFrame, Button( view, GameSpeed.Fast ).StateFrame( false, false ) );
		Assert.AreEqual( UiButton.DisabledFrame, Button( view, running ).StateFrame( false, false ) );
		CatchUpAndCompare( host, guest );
	}
}
