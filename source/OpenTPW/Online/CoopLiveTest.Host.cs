using System.Text.Json.Nodes;
using OpenTPW.Hud;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>[EXT:online-coop-session] The host's side of the live acceptance (docs/COOP.md, "The live acceptance").</summary>
internal sealed partial class CoopLiveTest
{
	/// <summary>The shared park's title, as the host writes it.</summary>
	internal const string SharedTitle = "Live acceptance";

	/// <summary>
	/// The host's script:
	/// <list type="number">
	/// <item>Jungle as Instant Action; Escape opens the pause menu, whose Build Together line opens the screen; Log in there (server, name, password).</item>
	/// <item>The shared park's title, two players, not listed; Share. Once hosted, the screen shows the code the session has, which the host publishes for the driver.</item>
	/// <item>Back to the running park; the guest joins with the code and follows.</item>
	/// <item>The figures: the host sees the guest's figure and name in its view, then the guest sees the host's; the host's view jumps far and the guest sees its figure there; the
	/// guest's view jumps and the host sees its figure there.</item>
	/// <item>A path line laid through the HUD, which the guest's park applies; the guest's own line is refused, the host lets it build with the Build Together screen's switch, and
	/// its next line is laid at its record on both parks (C3a-2) (recovery: the driver restarts the server, both come back, and a second line reaches the guest).</item>
	/// <item>Management (C4b-2): the attractions right through the rights dialog; the guest's price and door at their records here; the guest's forced resync seen as one desync report
	/// and one capture, and its second price applied once.</item>
	/// <item>A chat line through the chat mode's entry, and the guest's answer; F4 back to the HUD (endurance: the fastest speed for the run's minutes).</item>
	/// <item>The guest rides: its figure is gone from the host's park while it rides and comes back after.</item>
	/// <item>Once the guest's checks are done, the host ends sharing from the Build Together screen (Yes), and its park runs on alone.</item>
	/// </list>
	/// </summary>
	private IEnumerable<CoopLiveWait> HostSteps()
	{
		// the park's files first: the page copies a theme when a park asks for it (GameFlow.LevelDataReady), which a park started here does not wait for; natively they are there
		yield return Wait( "the park's files", () => GameFlow.LevelDataReady?.Invoke( CoopJoinSmokeTest.HostLevel ) != false, 300 );
		Require( levelCopyFailure?.Invoke( CoopJoinSmokeTest.HostLevel ) is null, $"the park's files arrived whole ({levelCopyFailure?.Invoke( CoopJoinSmokeTest.HostLevel )})" );
		var level = flow.StartLevel( CoopJoinSmokeTest.HostLevel, original: true, developerPanels: false, start: ParkStartKind.InstantAction );
		yield return Wait( "the park's HUD", () => ReferenceEquals( flow.Level, level ) && flow.Hud != null && Top( "hud" ), 120 );
		yield return Pause( "the park's first seconds", 3 );
		Require( CoopSession.CanShare( level, out var refusal ), $"the park can be shared ({refusal})" );

		// Escape opens the pause menu; its Build Together line opens the screen, which asks for a login
		const string screen = OnlineScreens.BuildTogetherScreenName;
		Give( UiInput.Key( UiKeys.Back ) );
		yield return Wait( "Escape opens the pause menu", () => Top( ParkHud.PauseMenuScreen ), 10 );
		Choose( ParkHud.PauseMenuScreen, ParkHud.BuildTogetherId );
		yield return Wait( "the Build Together screen", () => Top( screen ), 10 );
		foreach ( var step in LogIn( screen ) )
			yield return step;

		// the shared park's choices: a title of its own, two players (the button steps 4 to 2), not on the open list; then Share
		TypeInto( screen, "parkTitle", SharedTitle );
		Press( screen, "players" );
		yield return Wait( "Share is enabled", () => Usable<UiButton>( screen, "share" ), 10 );
		Press( screen, "share" );
		yield return Wait( "the park is hosted", () => flow.Sharing is { Stage: CoopShareStage.Hosting, State: not null }, 120 );
		var state = flow.Sharing!.State!;
		Require( state.Title == SharedTitle && state.MaximumPlayers == 2 && !state.Listed && state.Members.Count == 1, "the session has the choices made" );
		yield return Wait( "the screen shows the session's code", () => Usable<UiLabel>( screen, "code" ) && LabelText( screen, "code" ).Contains( state.Code, StringComparison.Ordinal ), 10 );
		Keep( "session", SessionEvidence() );
		Publish( "player", playerName );
		Publish( "session", state.Id );
		Publish( "code", state.Code );

		// back to the running park (the screen's Back, then Resume Game); the driver starts the guest with the code
		Press( screen, "back" );
		yield return Wait( "the pause menu again", () => Top( ParkHud.PauseMenuScreen ), 10 );
		Choose( ParkHud.PauseMenuScreen, nameof( UIStrings.ResumeGame ) );
		yield return Wait( "the park again", () => TheHud.Stack.Screens.Count == 1 && !TheHud.Paused, 10 );
		yield return Wait( "the guest in the session", () => flow.Sharing?.State?.Members.Count == 2, 900 );
		var guestName = flow.Sharing!.State!.Members.Single( member => member.Id != flow.Sharing.State.Host ).Name;
		yield return Wait( "the guest follows the park", () => OtherSays( "following" ), 600 );
		Keep( "session", SessionEvidence() );

		// the figures (C2d-4): the host sees the guest's figure and name, then the guest sees the host's
		yield return Wait( "the shared room on both sides", () => Presence is { InRoom: true } && OtherSays( "inRoom" ), 90 );
		foreach ( var step in See( guestName, "guest" ) )
			yield return step;
		Publish( "sawGuest", true );
		yield return Wait( "the guest sees the host's figure", () => OtherSays( "sawHost" ), 180 );
		// the host's view moves far: its figure jumps there, and the guest sees it there
		(float X, float Y) away = default;
		foreach ( var step in MoveViewAway( place => away = place ) )
			yield return step;
		Publish( "hostAt", Cell( away.X, away.Y ) );
		yield return Wait( "the guest sees the host's figure moved", () => OtherSays( "sawHostMoved" ), 180 );
		// the guest's view moves far: the host sees its figure there
		yield return Wait( "the guest's view moves", () => Other( "guestAt" ) is JsonArray, 180 );
		foreach ( var step in See( guestName, "guest-moved", Place( Other( "guestAt" )! ) ) )
			yield return step;
		Publish( "sawGuestMoved", true );

		// the host builds through its HUD: a path line, which the guest's park applies at the host's tick
		var avoid = new List<(int X, int Y)>();
		var built = new LineResult();
		foreach ( var step in LayLine( host: true, avoid, built ) )
			yield return step;
		Keep( "build", built.Evidence );
		Publish( "built", built.Evidence );
		yield return Wait( "the guest's park has the line", () => OtherSays( "sawBuild" ), 180 );

		// [EXT:online-coop-permissions] the guest's own line is refused; the host lets it build through the Build Together screen's switch (C3a-2), and the guest's next line is laid
		// at its record on both parks. The host keeps what its own park did with the guest's line: the record its stream gave the command and the cells the command laid here
		yield return Wait( "the guest's line refused", () => OtherSays( "refused" ), 180 );
		var seat = (int)flow.Sharing!.State!.Members.Single( member => member.Name == guestName ).Slot;
		JsonObject? observed = null;
		void Observe( ParkCommandLogEntry entry, ParkCommandResult result )
		{
			if ( observed == null && entry.Command.Player == seat && entry.Command is LayLineCommand or QueueSegmentCommand && result.Accepted && result.As<SegmentResult>() is { } segment )
				observed = new JsonObject
				{
					["record"] = TheSession.HostStream?.LastRecord,
					["seat"] = seat,
					["kind"] = entry.Command.Kind.ToString(),
					["cells"] = new JsonArray( segment.Line.Select( cell => (JsonNode?)new JsonArray( cell.X, cell.Y ) ).ToArray() )
				};
		}
		level.Commands.Applied += Observe;
		// [EXT:online-coop-money] the management scenario's purchase: the guest's line charged at its record in this park (C4b-2)
		var purchase = scenario == "management" ? new LinePurchase( level, seat ) : null;
		try
		{
			foreach ( var step in GiveTheGuestARight( guestName, seat, CoopRights.Build, "grant" ) )
				yield return step;
			Publish( "granted", true );
			yield return Wait( "the guest's own line", () => Other( "ownBuilt" ) is JsonObject, 300 );
			var ownCells = Cells( Other( "ownBuilt" )!["cells"] );
			yield return Wait( "the guest's line in the host's park", () => observed != null
				&& ownCells.All( cell => level.Guests!.Grid.Cells.RawTypeAt( cell.X, cell.Y ) == (byte)ParkCellType.Path ), 60 );
		}
		finally
		{
			level.Commands.Applied -= Observe;
			purchase?.Dispose();
		}
		Keep( "guestBuild", observed! );
		if ( purchase != null )
		{
			Require( purchase.Evidence != null, "the guest's line was charged in the host's park" );
			Keep( "guestPurchase", purchase.Evidence );
		}
		Publish( "sawGuestBuild", true );

		if ( scenario == "management" )
			foreach ( var step in HostManagement( guestName, seat ) )
				yield return step;

		if ( scenario == "recovery" )
			foreach ( var step in HostRecovery( guestName, avoid ) )
				yield return step;

		// the chat (C2d-1): a line through the chat mode's entry; the guest hears it and answers
		var hello = $"Hello {guestName}, code {state.Code}";
		foreach ( var step in Say( hello ) )
			yield return step;
		Publish( "said", hello );
		yield return Wait( "the guest's answer", () => Text( Other( "said" ) ) is { } answer && Heard( answer ), 180 );
		Keep( "chat", new JsonObject { ["said"] = hello, ["heard"] = Text( Other( "said" ) ) } );
		foreach ( var step in ToggleChatMode( chatMode: false ) )
			yield return step;

		if ( scenario == "endurance" )
			foreach ( var step in HostEndurance() )
				yield return step;

		// the guest rides: its figure leaves the host's park while it rides, and comes back after the ride
		yield return Wait( "the guest rides", () => OtherSays( "riding" ), 300 );
		yield return Wait( "the guest's figure gone while it rides", () => Presence is { InRoom: true } && Figure( guestName ) == null, 60 );
		Publish( "sawHidden", true );
		yield return Wait( "the guest's ride ends", () => OtherSays( "rode" ), 120 );
		yield return Wait( "the guest's figure back", () => Figure( guestName ) != null, 60 );
		Publish( "sawBack", true );

		// the guest's checks done, the host ends sharing from its screen: Escape, Build Together, End sharing, Yes
		yield return Wait( "the guest's checks", () => OtherSays( "done" ), 300 );
		Keep( "stream", StreamEvidence() );
		Keep( "session", SessionEvidence() );
		Give( UiInput.Key( UiKeys.Back ) );
		yield return Wait( "Escape opens the pause menu", () => Top( ParkHud.PauseMenuScreen ), 10 );
		Choose( ParkHud.PauseMenuScreen, ParkHud.BuildTogetherId );
		yield return Wait( "the Build Together screen of the running session", () => Usable<UiButton>( screen, "endSharing" ) && Usable<UiLabel>( screen, "member0" ) && Usable<UiLabel>( screen, "member1" ), 10 );
		Require( LabelText( screen, "member1" ).Contains( guestName, StringComparison.Ordinal ) || LabelText( screen, "member0" ).Contains( guestName, StringComparison.Ordinal ),
			"the screen lists the guest" );
		Press( screen, "endSharing" );
		yield return Wait( "the confirmation", () => Top( "confirmEndSharing" ), 10 );
		Press( "confirmEndSharing", "choice0" );
		yield return Wait( "the session's end", () => flow.Sharing is { Stage: CoopShareStage.Done, Outcome: CoopShareOutcome.Ended, EndedBySelf: true }, 60 );
		Require( level.CoopSession is null or { EndedWith: not null }, "the park left the session" );
		// the park is the host's again: it runs on alone
		Press( screen, "back" );
		yield return Wait( "the pause menu again", () => Top( ParkHud.PauseMenuScreen ), 10 );
		Choose( ParkHud.PauseMenuScreen, nameof( UIStrings.ResumeGame ) );
		yield return Wait( "the park again", () => TheHud.Stack.Screens.Count == 1 && !TheHud.Paused, 10 );
		var ticks = level.TickCount;
		yield return Wait( "the park runs on alone", () => level.TickCount > ticks + 60, 30 );
		Keep( "ended", new JsonObject { ["outcome"] = flow.Sharing?.Outcome.ToString(), ["ticks"] = level.TickCount } );
	}

	/// <summary>
	/// [EXT:online-coop-permissions] The host gives the guest <paramref name="right"/> (C3a-2, C4b's rights dialog; the management scenario's attractions, C4b-2): Escape, Build Together,
	/// the guest's rights button, the right's switch in its dialog, which the shared park's rights then say, the dialog closed, the row's button naming the right, and back to the park.
	/// Its evidence is kept as <paramref name="key"/>.
	/// </summary>
	private IEnumerable<CoopLiveWait> GiveTheGuestARight( string guestName, int seat, CoopRights right, string key )
	{
		const string screen = OnlineScreens.BuildTogetherScreenName;
		const string dialog = OnlineScreens.SeatRightsScreenName;
		var toggle = $"right{right}";
		var level = ThePark;
		var members = flow.Sharing!.State!.Members.OrderBy( member => member.Slot ).ToList();
		var row = members.FindIndex( member => member.Name == guestName );
		Require( row > 0 && members[row].Slot == seat, "the guest has a row of its own" );
		Give( UiInput.Key( UiKeys.Back ) );
		yield return Wait( "Escape opens the pause menu", () => Top( ParkHud.PauseMenuScreen ), 10 );
		Choose( ParkHud.PauseMenuScreen, ParkHud.BuildTogetherId );
		yield return Wait( "the guest's rights button", () => Usable<UiButton>( screen, $"rights{row}" ), 10 );
		Require( !level.RightsOf( seat ).HasFlag( right ), $"the guest has no {right} right before the dialog" );
		var before = Element<UiButton>( screen, $"rights{row}" ).Text?.Invoke() ?? "";
		Press( screen, $"rights{row}" );
		yield return Wait( "the guest's rights dialog", () => Top( dialog ) && Usable<UiButton>( dialog, toggle ), 10 );
		Require( Element<UiButton>( dialog, toggle ).Selected?.Invoke() == false, $"the dialog draws {right} as not given" );
		Press( dialog, toggle );
		yield return Wait( $"the guest has the {right} right", () => level.RightsOf( seat ).HasFlag( right ) && Element<UiButton>( dialog, toggle ).Selected?.Invoke() == true, 10 );
		Press( dialog, "back" );
		yield return Wait( "Build Together again", () => Top( screen ), 10 );
		var after = Element<UiButton>( screen, $"rights{row}" ).Text?.Invoke() ?? "";
		Require( after != before, "the row's button names the right" );
		Keep( key, new JsonObject
		{
			["seat"] = seat,
			["record"] = TheSession.HostStream?.LastRecord,
			["before"] = before,
			["after"] = after,
			["rights"] = level.RightsOf( seat ).ToString(),
			["at"] = Math.Round( Now, 3 )
		} );
		Press( screen, "back" );
		yield return Wait( "the pause menu again", () => Top( ParkHud.PauseMenuScreen ), 10 );
		Choose( ParkHud.PauseMenuScreen, nameof( UIStrings.ResumeGame ) );
		yield return Wait( "the park again", () => TheHud.Stack.Screens.Count == 1 && !TheHud.Paused, 10 );
	}

	/// <summary>
	/// The recovery scenario's middle (docs/COOP.md, "The live acceptance"): the driver restarts the server; the host's link and the guest's come back to the session, the guest
	/// follows again (a resync may replace its park), the figures meet again, and a second line laid through the HUD reaches the guest's park.
	/// </summary>
	private IEnumerable<CoopLiveWait> HostRecovery( string guestName, List<(int X, int Y)> avoid )
	{
		// the links as they stand at the barrier: the restart's resume and reconnect come after it
		var atRestart = LinkEvidence();
		Publish( "readyForRestart", true );
		yield return Wait( "the driver's restart of the server", () => StatusOf( DriverRole, "restarted" ) is JsonValue, 300 );
		yield return Wait( "the session back on the host's link", () => flow.Online.Coop.Session is { } back && back.Members.Count == 2 && back.Members.All( member => member.Connected ), 300 );
		yield return Wait( "the guest follows again", () => OtherSays( "followingAgain" ), 600 );
		yield return Wait( "the guest's figure again", () => Presence is { InRoom: true } && Figure( guestName ) != null, 180 );
		// the figures after the restart: the host's view moves far again, and the guest sees its figure there (a place relayed by the restarted server)
		(float X, float Y) away = default;
		foreach ( var step in MoveViewAway( place => away = place ) )
			yield return step;
		Publish( "hostAtAgain", Cell( away.X, away.Y ) );
		yield return Wait( "the guest sees the host's figure moved again", () => OtherSays( "sawHostMovedAgain" ), 180 );
		var again = new LineResult();
		foreach ( var step in LayLine( host: true, avoid, again ) )
			yield return step;
		Keep( "rebuild", again.Evidence );
		Publish( "rebuilt", again.Evidence );
		yield return Wait( "the guest's park has the second line", () => OtherSays( "sawRebuild" ), 180 );
		Keep( "recovery", new JsonObject { ["session"] = SessionEvidence(), ["stream"] = StreamEvidence(), ["linksAtRestart"] = atRestart, ["links"] = LinkEvidence() } );
	}

	/// <summary>
	/// The endurance scenario's middle (docs/COOP.md, "The live acceptance"): the HUD's fastest speed (its <c>&gt;&gt;&gt;</c>) for the run's minutes of wall time while the guest
	/// follows, the speed and the stream sampled every <see cref="EnduranceSampleSeconds"/>; then the normal speed again. The evidence keeps when the fastest speed was set and when
	/// the normal speed came back.
	/// </summary>
	private IEnumerable<CoopLiveWait> HostEndurance()
	{
		// the guest takes its ticks before the speed changes, so its rates cover the whole interval too
		Publish( "enduranceReady", true );
		yield return Wait( "the guest's ticks before the speed", () => OtherSays( "enduranceBaseline" ), 120 );
		yield return Wait( "the HUD alone", () => Top( "hud" ) && TheHud.Stack.Screens.Count == 1, 30 );
		Press( "hud", $"speed{GameSpeed.Fastest}" );
		Require( TheHud.Status.Speed == GameSpeed.Fastest, $"the HUD's fastest speed is the park's (it is {TheHud.Status.Speed})" );
		var started = Now;
		var until = started + enduranceMinutes * 60;
		var samples = new JsonArray();
		// the park's ticks when the speed was set: the first stretch's rate counts too
		var endurance = new JsonObject { ["started"] = Math.Round( started, 3 ), ["startTicks"] = ThePark.TickCount, ["samples"] = samples };
		Publish( "enduranceStarted", true );
		while ( Now < until )
		{
			yield return Pause( "the fastest speed", Math.Min( EnduranceSampleSeconds, until - Now ) );
			Require( TheHud.Status.Speed == GameSpeed.Fastest, $"the park stays at the fastest speed (it is {TheHud.Status.Speed})" );
			var sample = StreamEvidence();
			sample["at"] = Math.Round( Now, 3 );
			sample["speed"] = TheHud.Status.Speed.ToString();
			samples.Add( sample );
			Keep( "endurance", endurance );
			Publish( "enduranceProgress", sample );
		}
		// the guest's last sample, still at the fastest speed: its rates cover the interval to its end
		Publish( "enduranceFinal", true );
		yield return Wait( "the guest's last sample", () => OtherSays( "enduranceFinalSampled" ), 120 );
		Require( TheHud.Status.Speed == GameSpeed.Fastest, $"the park stays at the fastest speed (it is {TheHud.Status.Speed})" );
		yield return Wait( "the HUD alone", () => Top( "hud" ) && TheHud.Stack.Screens.Count == 1, 30 );
		Press( "hud", $"speed{GameSpeed.Normal}" );
		Require( TheHud.Status.Speed == GameSpeed.Normal, $"the HUD's normal speed is the park's (it is {TheHud.Status.Speed})" );
		endurance["ended"] = Math.Round( Now, 3 );
		Keep( "endurance", endurance );
		Publish( "enduranceDone", true );
		yield return Wait( "the guest's endurance check", () => OtherSays( "enduranceChecked" ), 120 );
	}

	/// <summary>A cell published as [x, y].</summary>
	private static (float X, float Y) Place( JsonNode node ) => node is JsonArray { Count: 2 } pair
		? ((float)pair[0]!.GetValue<double>(), (float)pair[1]!.GetValue<double>()) : throw new InvalidOperationException( $"Live co-op acceptance: {node} is no cell." );

	/// <summary>A published text, or null.</summary>
	private static string? Text( JsonNode? node ) => node is JsonValue value && value.TryGetValue<string>( out var text ) ? text : null;
}
