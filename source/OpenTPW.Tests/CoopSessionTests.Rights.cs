using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-permissions] A shared park's rights per seat between its peers (docs/COOP.md, "Money and rights (C4)"; C4a): the rights command reaches every peer, and taking building back
/// lets go of the seat's claim in its record; a right taken back while a guest's request is on its way refuses the request at its record; the host's end releases every guest seat that holds
/// anything in records of its log, before the last checkpoint, and the log replays; the host's management of a ride a guest claims stays allowed, and a C3 session's records (the grant,
/// its taking back and the seat's release apart) replay to the same park; [EXT:online-coop-speed]
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
	public void TheHostsManagementOfARideAGuestClaimsStaysAllowedAndAC3SessionsRecordsReplay()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		// the grant's command of C3 (101), as a C3 session's log holds it
		Grant( host, guest, view );
		var ride = host.One.Runtime.AttractionId;
		var seat = GuestClaims( host, guest, ride );
		// a claim guards building and removal against the host, not the host's management: the ride's open switch goes as it did in C3
		var open = host.One.IsOpen;
		Assert.IsTrue( host.Issue( new SetAttractionOpenCommand( ride, !open ) ).Accepted );
		Assert.AreEqual( !open, host.One.IsOpen );
		// no guest manages an attraction yet: no part offers the right (C4b-2), so a guest's switch is refused with the rights' note
		Assert.IsFalse( guest.Issue( new SetAttractionOpenCommand( ride, open ) ).Accepted );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ) );
		Rounds( 10, host, guest );
		// C3's release of a seat: its grant taken back, then its release, two records
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, false ) ).Accepted );
		Assert.IsTrue( host.Issue( new ReleaseSeatCommand( seat ) ).Accepted );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( (!open, (int?)null, 0), (guest.One.IsOpen, guest.Level.ClaimOf( ride ), guest.Level.SeatRights.Count), "the host's records reached the guest" );
		host.Enter();
		var hostStream = host.Session!.HostStream!;
		host.Session.End( "The host closed the park." );
		var log = ParkCommandLogFile.Deserialize( hostStream.SessionLog!.Serialize() );
		Assert.AreEqual( 2, log.Entries.Count( entry => entry.Command is SetBuildGrantCommand ), "the log holds C3's grant and its taking back" );
		Assert.AreEqual( 1, log.Entries.Count( entry => entry.Command is ReleaseSeatCommand ), "and one release: the seat held nothing at the end" );
		// the records replay under the session's rights to the host's park, claim, switch and rights alike
		var twin = NewPark();
		twin.Enter();
		MarkStation( twin, twin.One, seat: null );
		var hashes = new Dictionary<long, ulong>();
		var claimed = false;
		ParkCommandReplay.Run( twin.Level.Commands, log, () => twin.Level.Commands.NextTick, () => twin.Level.RunTicks( 1 ), log.Checkpoints[^1].Ticks, ticks =>
		{
			hashes[ticks] = twin.Level.ComputeStateHash();
			claimed |= twin.Level.ClaimOf( ride ) == seat;
		}, new CoopBuildRights( twin.Level ) );
		hashes[log.Checkpoints[^1].Ticks] = twin.Level.ComputeStateHash();
		Assert.IsNull( ParkCommandReplay.FirstMismatch( log, hashes ) );
		Assert.IsTrue( claimed, "the guest's claim was accepted again" );
		Assert.AreEqual( (!open, (int?)null, 0), (twin.One.IsOpen, twin.Level.ClaimOf( ride ), twin.Level.SeatRights.Count), "and the host's switch, the release and the grant's end" );
	}

	/// <summary>
	/// [EXT:online-coop-permissions] The host's settings, door and call on a ride a guest claims (C4b-2): a claim guards building and removal, not the host's management, so a C3 session's
	/// log with those records replays under C4b-2's rights to its last checkpoint.
	/// </summary>
	[TestMethod]
	public void TheHostsSettingsDoorAndCallOnARideAGuestClaimsReplayUnderTheRightsToTheLastCheckpoint()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.One, seat: null );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var ride = host.One.Runtime.AttractionId;
		var seat = GuestClaims( host, guest, ride );
		// while the guest holds the claim the host changes the ride's settings, closes its door and calls a mechanic; a checkpoint's worth of ticks between them
		Assert.IsTrue( host.Issue( new SetRideSettingsCommand( ride, 60, 1, 1 ) ).Accepted );
		Rounds( 10, host, guest );
		var open = host.One.IsOpen;
		Assert.IsTrue( host.Issue( new SetAttractionOpenCommand( ride, !open ) ).Accepted );
		Rounds( 10, host, guest );
		Assert.IsTrue( host.Issue( new RequestMaintenanceCommand( ride, true ) ).Accepted );
		Rounds( 10, host, guest );
		Assert.AreEqual( seat, host.Level.ClaimOf( ride ), "the claim held throughout" );
		CatchUpAndCompare( host, guest );
		host.Enter();
		var hostStream = host.Session!.HostStream!;
		host.Session.End( "The host closed the park." );
		var log = ParkCommandLogFile.Deserialize( hostStream.SessionLog!.Serialize() );
		Assert.AreEqual( 1, log.Entries.Count( entry => entry.Command is SetRideSettingsCommand ), "the log holds the settings" );
		Assert.AreEqual( 1, log.Entries.Count( entry => entry.Command is SetAttractionOpenCommand ), "the door" );
		Assert.AreEqual( 1, log.Entries.Count( entry => entry.Command is RequestMaintenanceCommand ), "and the call" );
		Assert.IsNull( FirstReplayMismatch( log, park => MarkStation( park, park.One, seat: null ) ), "the session's log replays to its last checkpoint under the rights" );
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
