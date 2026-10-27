using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTPW.Hud;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>[EXT:online-coop-session] The guest's side of the live acceptance (docs/COOP.md, "The live acceptance").</summary>
internal sealed partial class CoopLiveTest
{
	/// <summary>
	/// The guest's script:
	/// <list type="number">
	/// <item>The lobby; Escape opens the lobby menu, Build Together the hub, Join with a code the join screen; Log in there; the host's code; Join.</item>
	/// <item>The host's park is fetched and built, replaces the lobby, and follows the host's stream with compared checkpoints; it is the host's session and park.</item>
	/// <item>The figures: once the host saw the guest's, the guest sees the host's figure and name in its view; the host's figure where the host's view jumped; the guest's own
	/// view jumps, which the host sees.</item>
	/// <item>The host's line applied at its record, with checkpoints compared after it; the guest's own line refused (the host has not let it build yet), its park following on;
	/// once the host lets it build, its next line goes to the host and is laid at its record, with checkpoints compared after it (C3a-2) (recovery: the server's restart,
	/// following again and the second line).</item>
	/// <item>Management (C4b-2): once the host gives the attractions right, a shop's typed price and a ride's door each at its record; a second price queued, the stream forced to
	/// desync before the price is sent, the resync's park, and the price sent once after it.</item>
	/// <item>The host's chat line heard and answered through the chat mode's entry (endurance: following the fastest speed for the run's minutes, with no desync or resync).</item>
	/// <item>F4 to the HUD and back; a click on a ride in the chat mode rides it, which the host sees as the figure gone, and Escape leaves it; Go to in the host's name popup
	/// moves the view to the host's figure with no park command.</item>
	/// <item>Done: the host ends the session; its end dialog's Back leaves for the lobby.</item>
	/// </list>
	/// </summary>
	private IEnumerable<CoopLiveWait> GuestSteps()
	{
		// the lobby, as the game opens it (no intro, no autorun)
		if ( flow.Menu == null )
			flow.ShowFrontEnd();
		yield return Wait( "the lobby", () => flow.Level == null && Top( "lobby" ), 120 );
		yield return Pause( "the lobby's first second", 1 );

		// Escape opens the lobby menu; Build Together opens the hub, Join with a code the join screen, which asks for a login
		const string join = OnlineScreens.JoinSharedParkScreenName;
		Give( UiInput.Key( UiKeys.Back ) );
		yield return Wait( "Escape opens the lobby menu", () => Top( "lobbyMenu" ), 10 );
		Choose( "lobbyMenu", "buildTogether" );
		yield return Wait( "the Build Together hub", () => Top( OnlineScreens.BuildTogetherLobbyScreenName ), 10 );
		Press( OnlineScreens.BuildTogetherLobbyScreenName, "joinWithCode" );
		yield return Wait( "the join screen", () => Top( join ), 10 );
		foreach ( var step in LogIn( join ) )
			yield return step;
		TypeInto( join, "code", code! );
		yield return Wait( "Join is enabled", () => Usable<UiButton>( join, "join" ), 10 );
		Press( join, "join" );

		// the host's park is fetched and built, and replaces the lobby; it follows the host's stream
		yield return Wait( "the shared park in place of the lobby", () => flow.Level is { CoopRole: CoopRole.Guest } && flow.Hud != null, 600 );
		yield return Wait( "the park follows the host", () => flow.Level?.CoopSession is { Phase: CoopGuestPhase.Following, GuestStream: { Checks: > 0, Stopped: false } }, 300 );
		var state = flow.Online.Coop.Session ?? throw new InvalidOperationException( "Live co-op acceptance: the guest's link has no session." );
		Require( state.Id == Text( Other( "session" ) ) && state.Code == Text( Other( "code" ) ) && state.Title == SharedTitle, "the guest is in the host's session" );
		Require( ThePark.IsCoopGuestPark && TheSession.JoinTimings.Any( timing => timing.Outcome == "joined" ), "the park was built from the host's capture" );
		var hostName = state.Members.Single( member => member.Id == state.Host ).Name;
		Keep( "session", SessionEvidence() );
		Keep( "joinTimings", JsonSerializer.SerializeToNode( TheSession.JoinTimings ) );
		Publish( "player", playerName );
		Publish( "session", state.Id );
		Publish( "following", true );

		// the figures (C2d-4): once the host saw the guest's, the guest sees the host's figure and name
		yield return Wait( "the shared room", () => Presence is { InRoom: true }, 90 );
		Publish( "inRoom", true );
		yield return Wait( "the host sees the guest's figure", () => OtherSays( "sawGuest" ), 180 );
		foreach ( var step in See( hostName, "host" ) )
			yield return step;
		Publish( "sawHost", true );
		// the host's view jumped: its figure is there
		yield return Wait( "the host's view moves", () => Other( "hostAt" ) is JsonArray, 180 );
		foreach ( var step in See( hostName, "host-moved", Place( Other( "hostAt" )! ) ) )
			yield return step;
		Publish( "sawHostMoved", true );
		// the guest's own view jumps: the host sees its figure there
		(float X, float Y) away = default;
		foreach ( var step in MoveViewAway( place => away = place ) )
			yield return step;
		Publish( "guestAt", Cell( away.X, away.Y ) );
		yield return Wait( "the host sees the guest's figure moved", () => OtherSays( "sawGuestMoved" ), 180 );

		// the host's line: applied at its record, with checkpoints compared after it
		yield return Wait( "the host's line", () => Other( "built" ) is JsonObject, 300 );
		var applied = new JsonObject();
		foreach ( var step in Applied( (JsonObject)Other( "built" )!, applied ) )
			yield return step;
		Keep( "applied", applied );
		Publish( "sawBuild", true );
		// the guest's own line through its HUD: refused, the host has not let it build yet; the park follows on
		var avoid = new List<(int X, int Y)>( Cells( Other( "built" )!["cells"] ) );
		var refused = new LineResult();
		foreach ( var step in LayLine( host: false, avoid, refused ) )
			yield return step;
		var checks = TheSession.GuestStream!.Checks;
		yield return Wait( "the park follows on after the refusal", () => TheSession is { Phase: CoopGuestPhase.Following, GuestStream: { Stopped: false } stream } && stream.Checks > checks, 60 );
		Keep( "refusal", refused.Evidence );
		// [EXT:online-coop-permissions] the host lets the guest build through its Build Together screen's switch (C3a-2): the guest's next line goes to the host and is laid at its
		// record on both parks, with checkpoints compared after it
		Publish( "refused", true );
		var seat = ThePark.Commands.Player;
		yield return Wait( "the host lets the guest build", () => OtherSays( "granted" ) && ThePark.HasBuildGrant( seat ), 180 );
		var own = new LineResult();
		// [EXT:online-coop-money] the management scenario's purchase: the line's charge as this park applies its record (C4b-2)
		var purchase = scenario == "management" ? new LinePurchase( ThePark, seat ) : null;
		try
		{
			foreach ( var step in LayLine( host: false, avoid, own, granted: true ) )
				yield return step;
		}
		finally
		{
			purchase?.Dispose();
		}
		if ( purchase != null )
		{
			Require( purchase.Evidence != null, "the guest's line was charged in its park" );
			Keep( "ownPurchase", purchase.Evidence );
		}
		var ownApplied = new JsonObject();
		foreach ( var step in Applied( own.Evidence, ownApplied ) )
			yield return step;
		own.Evidence["seat"] = seat;
		own.Evidence["checksAtLine"] = ownApplied["checksAtLine"]!.DeepClone();
		own.Evidence["checksAfter"] = ownApplied["checksAfter"]!.DeepClone();
		Keep( "ownBuild", own.Evidence );
		Publish( "ownBuilt", own.Evidence );
		yield return Wait( "the host's park has the guest's line", () => OtherSays( "sawGuestBuild" ), 180 );

		if ( scenario == "management" )
			foreach ( var step in GuestManagement() )
				yield return step;

		// [EXT:online-coop-commands] the staff scenario (C4c-2)
		if ( scenario == "staff" )
			foreach ( var step in GuestStaff() )
				yield return step;

		if ( scenario == "recovery" )
			foreach ( var step in GuestRecovery( hostName, avoid ) )
				yield return step;

		// the chat (C2d-1): the host's line heard, and an answer through the chat mode's entry
		yield return Wait( "the host's line in the chat", () => Text( Other( "said" ) ) is { } said && Heard( said ), 180 );
		var answer = $"Hi {hostName}, code {state.Code} here";
		foreach ( var step in Say( answer ) )
			yield return step;
		Publish( "said", answer );
		Keep( "chat", new JsonObject { ["heard"] = Text( Other( "said" ) ), ["said"] = answer } );

		if ( scenario == "endurance" )
			foreach ( var step in GuestEndurance() )
				yield return step;

		// F4: the HUD and the chat mode change places (Enter left the guest in the chat mode)
		foreach ( var step in ToggleChatMode( chatMode: false ) )
			yield return step;
		foreach ( var step in ToggleChatMode( chatMode: true ) )
			yield return step;
		foreach ( var step in RideFromTheChatMode() )
			yield return step;
		foreach ( var step in GoToTheHost( hostName ) )
			yield return step;

		Keep( "stream", StreamEvidence() );
		Keep( "session", SessionEvidence() );
		// [EXT:online-coop-snapshot-join] the management scenario's one resync is its forced one (C4b-2)
		if ( scenario == "management" )
			Require( streams == 2 && TheSession is { FailedJoins: <= 1, GuestStream: { Desyncs: 1, Desync: null } }, "the session followed with one resync, the forced one, and no other desync" );
		// [EXT:online-coop-snapshot-join] the staff scenario's two resyncs are its forced ones, each inside the early window after a join, so each may count as the join's failure (C4c-2)
		else if ( scenario == "staff" )
			Require( streams == 3 && TheSession is { FailedJoins: <= 2, GuestStream: { Desyncs: 2, Desync: null } }, "the session followed with two resyncs, the forced ones, and no other desync" );
		else if ( scenario != "recovery" )
			Require( streams == 1 && TheSession is { FailedJoins: 0, GuestStream.Desyncs: 0 }, "the session followed with no resync and no desync" );
		Publish( "done", true );

		// the host ends the session: its end dialog over the park, whose Back leaves for the lobby
		yield return Wait( "the session's end over the park", () => Top( GameFlow.GuestEndedScreenName ), 120 );
		Press( GameFlow.GuestEndedScreenName, "choice0" );
		yield return Wait( "back in the lobby", () => flow.Level == null && flow.Menu != null, 120 );
	}

	/// <summary>The guest's park has the host's line (<paramref name="built"/>): its stream took the line's record, its cells are paths, and checkpoints were compared after it.</summary>
	private IEnumerable<CoopLiveWait> Applied( JsonObject built, JsonObject applied )
	{
		var record = built["record"]!.GetValue<long>();
		var cells = Cells( built["cells"] );
		yield return Wait( "the line in the guest's park", () => flow.Level?.CoopSession?.GuestStream is { } stream && stream.LastRecord >= record
			&& cells.All( cell => flow.Level.Guests!.Grid.Cells.RawTypeAt( cell.X, cell.Y ) == (byte)ParkCellType.Path ), 120 );
		var checks = TheSession.GuestStream!.Checks;
		yield return Wait( "checkpoints compared after the line", () => TheSession.GuestStream is { Stopped: false } stream && stream.Checks >= checks + 2, 60 );
		applied["record"] = record;
		applied["lastRecord"] = TheSession.GuestStream!.LastRecord;
		applied["checksAtLine"] = checks;
		applied["checksAfter"] = TheSession.GuestStream.Checks;
		applied["frame"] = frame;
		applied["at"] = Math.Round( Now, 3 );
	}

	/// <summary>
	/// The recovery scenario's middle: the driver restarts the server; the guest's park follows the host again with checkpoints compared after the restart (a resync may replace
	/// it), the figures meet again, and the host's second line reaches it.
	/// </summary>
	private IEnumerable<CoopLiveWait> GuestRecovery( string hostName, List<(int X, int Y)> avoid )
	{
		// the links as they stand at the barrier: the restart's resume and reconnect come after it
		var atRestart = LinkEvidence();
		Publish( "readyForRestart", true );
		var stream = TheSession.GuestStream;
		var checks = stream?.Checks ?? 0;
		yield return Wait( "the driver's restart of the server", () => StatusOf( DriverRole, "restarted" ) is JsonValue, 300 );
		yield return Wait( "the park follows the host again", () => flow.Level?.CoopSession is { Phase: CoopGuestPhase.Following, GuestStream: { Stopped: false } now }
			&& (ReferenceEquals( now, stream ) ? now.Checks > checks : now.Checks > 0), 600 );
		Publish( "followingAgain", true );
		yield return Wait( "the host's figure again", () => Presence is { InRoom: true } && Figure( hostName ) != null, 180 );
		yield return Wait( "the host's view moves again", () => Other( "hostAtAgain" ) is JsonArray, 300 );
		foreach ( var step in See( hostName, "host-moved-again", Place( Other( "hostAtAgain" )! ) ) )
			yield return step;
		Publish( "sawHostMovedAgain", true );
		yield return Wait( "the host's second line", () => Other( "rebuilt" ) is JsonObject, 300 );
		var applied = new JsonObject();
		foreach ( var step in Applied( (JsonObject)Other( "rebuilt" )!, applied ) )
			yield return step;
		foreach ( var cell in Cells( Other( "rebuilt" )!["cells"] ) )
			avoid.Add( cell );
		Keep( "reapplied", applied );
		Publish( "sawRebuild", true );
		Keep( "recovery", new JsonObject { ["session"] = SessionEvidence(), ["stream"] = StreamEvidence(), ["resynced"] = !ReferenceEquals( TheSession.GuestStream, stream ),
			["linksAtRestart"] = atRestart, ["links"] = LinkEvidence() } );
	}

	/// <summary>
	/// The endurance scenario's middle: the guest follows the host's fastest speed for the run's minutes, sampled every <see cref="EnduranceSampleSeconds"/>, with no resync and no desync, and with a
	/// checkpoint compared at least every two seconds of the run on average (the faster speeds check once a second of real time).
	/// </summary>
	private IEnumerable<CoopLiveWait> GuestEndurance()
	{
		// the first sample before the host changes the speed, so the rates cover the whole interval
		yield return Wait( "the host's endurance run", () => OtherSays( "enduranceReady" ), 180 );
		var stream = TheSession.GuestStream!;
		var first = StreamEvidence();
		first["at"] = Math.Round( Now, 3 );
		var progress = new JsonArray( first );
		Keep( "endurance", progress );
		Publish( "enduranceBaseline", true );
		yield return Wait( "the host's fastest speed", () => OtherSays( "enduranceStarted" ), 180 );
		var (checks, started) = (stream.Checks, Now);
		while ( true )
		{
			// a sample every few seconds while the host's interval runs, and a last one when the host asks for it, still at the fastest speed
			var next = Now + EnduranceSampleSeconds;
			yield return Wait( "the fastest speed followed", () => OtherSays( "enduranceFinal" ) || Now >= next, 90 );
			var final = OtherSays( "enduranceFinal" );
			Require( ReferenceEquals( TheSession.GuestStream, stream ) && TheSession.Phase == CoopGuestPhase.Following && !stream.Stopped && stream.Desyncs == 0,
				"the park follows the fastest speed with no resync and no desync" );
			var sample = StreamEvidence();
			sample["at"] = Math.Round( Now, 3 );
			sample["final"] = final;
			progress.Add( sample );
			Keep( "endurance", progress );
			if ( final )
				break;
		}
		var compared = stream.Checks - checks;
		Require( compared >= (Now - started) / 2, $"checkpoints compared through the run ({compared} in {Now - started:0} s)" );
		Publish( "enduranceFinalSampled", true );
		yield return Wait( "the host's normal speed", () => OtherSays( "enduranceDone" ), 120 );
		Publish( "enduranceChecked", true );
	}

	/// <summary>
	/// A click on a ride in the chat mode rides it (C2d-4, tp.exe 0x4BD7F4): the figure leaves the host's park while it rides (the host says when it saw that), and Escape leaves the
	/// ride with the figure back.
	/// </summary>
	private IEnumerable<CoopLiveWait> RideFromTheChatMode()
	{
		var level = ThePark;
		var ride = level.Objects.Objects.Select( ParkHud.ChatModeRide ).FirstOrDefault( candidate => candidate != null && RideCell( candidate ) != null );
		Require( ride != null, "the park has a ride to ride from the chat mode" );
		var cell = RideCell( ride! )!.Value;
		Look( (cell.X + 0.5f, cell.Y + 0.5f) );
		yield return Pause( "the view over the ride", 0.5 );
		var canvas = flow.Context.Canvas;
		Require( level.TryProjectCell( cell.X, cell.Y, new System.Numerics.Vector2( canvas.Width, canvas.Height ), out var point ), "the ride is in the view" );
		foreach ( var step in ClickThrough( point ) )
			yield return step;
		yield return Wait( "the click rides the ride", () => TheHud.IsRiding && ReferenceEquals( level.RideView?.Ride, ride ), 10 );
		yield return Wait( "the figure hidden for the room", () => Presence is { State.Riding: true }, 10 );
		Publish( "riding", true );
		yield return Wait( "the host sees the figure gone", () => OtherSays( "sawHidden" ), 90 );
		Give( UiInput.Key( UiKeys.Back ) );
		yield return Wait( "Escape leaves the ride", () => !TheHud.IsRiding && Presence is { State.Riding: false } && TheHud.ChatMode, 10 );
		Publish( "rode", true );
		yield return Wait( "the host sees the figure back", () => OtherSays( "sawBack" ), 90 );
		Keep( "ride", new JsonObject { ["ride"] = ride!.Entry.DisplayName, ["cell"] = new JsonArray( cell.X, cell.Y ) } );
	}

	/// <summary>A cell of <paramref name="ride"/>'s footprint near its placement, where a click picks it, or null (the visit smoke's).</summary>
	private (int X, int Y)? RideCell( OriginalObject ride )
	{
		for ( var radius = 0; radius <= 3; radius++ )
			for ( var dy = -radius; dy <= radius; dy++ )
				for ( var dx = -radius; dx <= radius; dx++ )
					if ( Math.Max( Math.Abs( dx ), Math.Abs( dy ) ) == radius && ReferenceEquals( ThePark.Objects.FindAt( ride.Placement.X + dx, ride.Placement.Y + dy ), ride ) )
						return (ride.Placement.X + dx, ride.Placement.Y + dy);
		return null;
	}

	/// <summary>
	/// Go to in the host's name popup (a right click on the host's row in the chat mode's people list) moves the view to the host's figure, with no park command (C2d-4,
	/// <see cref="ChatPanel.TakeTo"/>).
	/// </summary>
	private IEnumerable<CoopLiveWait> GoToTheHost( string hostName )
	{
		// the view first moves away from the host's figure, so the Go to has somewhere to go
		if ( Figure( hostName ) is { } host && Near( host, ViewCentre() ) )
			foreach ( var step in MoveViewAway( _ => { } ) )
				yield return step;
		var context = flow.Context;
		yield return Wait( "the host's row in the people list", () => TheHud.ChatMode && VisitPresenceSmoke.RowPoint( TheHud, context, hostName ) != null, 30 );
		// a guest's park command is refused by the session's rights, and every refusal is counted (Level.SessionRefusals)
		var refusals = ThePark.SessionRefusals;
		Give( new UiInput( VisitPresenceSmoke.RowPoint( TheHud, context, hostName )!.Value, false, false, false, true, UiKeys.None ) );
		yield return Wait( "the right click opens the host's name popup", () => InputGiven && Usable<UiButton>( ChatPanel.PopupScreenName, "popupGoto" ), 10 );
		Give( UiInput.Click( Element<UiButton>( ChatPanel.PopupScreenName, "popupGoto" ).ScreenRect( context.Canvas ).Center ) );
		yield return Wait( "Go to moves the view to the host's figure", () => InputGiven && !Top( ChatPanel.PopupScreenName ) && Figure( hostName ) is { } there
			&& Near( there, ViewCentre() ), 10 );
		Require( ThePark.SessionRefusals == refusals, "Go to issued no park command" );
		Keep( "goTo", new JsonObject { ["name"] = hostName, ["view"] = Cell( ViewCentre().X, ViewCentre().Y ) } );
	}

	/// <summary>Cells published as [[x, y], ...].</summary>
	private static (int X, int Y)[] Cells( JsonNode? node ) => node is JsonArray cells
		? cells.Select( cell => (cell![0]!.GetValue<int>(), cell[1]!.GetValue<int>()) ).ToArray()
		: throw new InvalidOperationException( $"Live co-op acceptance: {node} is no list of cells." );
}
