using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-session] Joining a shared park with a code (C2d-3; docs/COOP.md, "Joining a shared park from the lobby"): the game's join attempt over real links to the loopback
/// server, against a host whose park is the stream tests' asset-free park, and the guest's game (an uninitialized <see cref="GameFlow"/> whose builds are the replay twin) taking the
/// answer, building the host's park and swapping it in.
/// </summary>
public partial class CoopJoinSessionTests
{
	private static string English( OpenTpwText key ) => SupplementaryStrings.Get( key, "English" );

	/// <summary>A host on the loopback server: its session (the stream tests' park, hosted) and its frames, which also run every other member's game.</summary>
	private async Task<(CoopLinkTests.Player Player, CoopSessionState State, Func<int, Func<bool>?, string, Task> Frames)> RealHost( LoopbackServer server, CoopLinkTests.Players players )
	{
		var hostPlayer = await players.Add( server, "Hostess" );
		await CoopLinkTests.Open( hostPlayer );
		var state = await CoopLinkTests.Share( hostPlayer );
		var (level, list) = NewPark();
		host = new Peer { Level = level, Entities = list, Pump = hostPlayer.Session.Pump };
		peers.Add( host );
		host.Enter();
		hostLogStart = level.Commands.Log.Count;
		host.Session = CoopSession.Host( level, hostPlayer.Link, () => clock, HostTime );
		host.Session.HostStream!.CaptureSliceBudget = TimeSpan.FromMilliseconds( 50 );
		async Task Frames( int count, Func<bool>? until, string what )
		{
			var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
			for ( var round = 0; round < count || (until != null && !until()); round++ )
			{
				Assert.IsTrue( DateTime.UtcNow < deadline, $"{what}: the host took {host.Session!.HostStream!.CapturesTaken} captures; "
					+ string.Join( "; ", peers.Select( peer => $"{peer.Session?.Phase} {peer.Session?.EndedWith} {peer.Flow?.Joining?.Stage} {peer.Flow?.Joining?.Outcome}" ) ) );
				if ( round % 40 == 7 && host.Session!.EndedWith == null )
					host.Issue( new LayLineCommand( CellToolMode.Path, -1, 4 + round % 6, 20, 4 + round % 6, 26 ) );
				clock += 1 / 60.0;
				foreach ( var peer in peers.ToArray() )
					peer.Frame( 1 / 60.0 );
				await Task.Delay( 2 );
			}
		}
		return (hostPlayer, state, Frames);
	}

	/// <summary>A guest's game in the lobby, over <paramref name="player"/>'s online session, whose join builds are the replay twin.</summary>
	private Peer LobbyGuest( CoopLinkTests.Player player )
	{
		var joiner = new Peer { Pump = player.Session.Pump };
		peers.Add( joiner );
		joiner.Flow = NewFlow( joiner );
		SetField( joiner.Flow, "onlineSession", player.Session );
		return joiner;
	}

	[TestMethod]
	public async Task AJoinWithACodeThroughTheGamesAttemptMakesTheHostsParkTheGuestsAndItsEndIsSaid()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var (hostPlayer, state, frames) = await RealHost( server, players );
			var joinPlayer = await players.Add( server, "Latecomer", firstFrame: true );
			await frames( 60, null, "the host's first frames" );

			// the guest's game in the lobby joins with the code (case and dashes do not matter) through its own attempt
			var joiner = LobbyGuest( joinPlayer );
			var flow = joiner.Flow!;
			Assert.IsTrue( flow.StartJoining( state.Code.ToLowerInvariant().Insert( 3, "-" ) ) );
			var attempt = flow.Joining!;
			Assert.IsFalse( flow.StartJoining( state.Code ), "one attempt at a time" );
			await frames( 0, () => attempt.Stage == CoopJoinStage.Done, "the join through the game's attempt" );
			Assert.AreEqual( CoopJoinOutcome.Joined, attempt.Outcome );
			Assert.IsTrue( attempt.Taken );
			Assert.AreSame( flow.Level, joiner.Level );
			Assert.AreEqual( CoopRole.Guest, joiner.Level!.CoopRole );
			Assert.AreSame( attempt.Session, joiner.Level.CoopSession );
			joiner.Session = attempt.Session;
			Assert.IsNull( flow.HeldJoin );
			// the guest's status line names the park, its host and the players
			Assert.AreEqual( string.Format( English( OpenTpwText.GuestStatus ), "Our park", "Hostess", 2, 4 ), GameFlow.GuestStatusLine( attempt.Session!, joinPlayer.Link, English ) );
			await frames( 60, null, "the guest follows" );
			Assert.IsNull( joiner.Session!.EndedWith );

			// the guest's pause menu asks before Exit To Lobby leaves the running shared park
			var hud = flow.Hud!;
			hud.OpenPauseMenu();
			var exit = (UiMenuItem)hud.Stack.Top!.Find( nameof( UIStrings.ExitToLobby ) )!;
			exit.Clicked!();
			Assert.AreEqual( Hud.ParkHud.ConfirmLeaveSharedParkScreen, hud.Stack.Top!.Name );
			hud.Stack.Top!.Elements.OfType<UiButton>().Last().Clicked!();
			Assert.AreEqual( Hud.ParkHud.PauseMenuScreen, hud.Stack.Top!.Name, "No keeps the guest in the park" );
			Assert.IsNull( joiner.Session.EndedWith );
			hud.ClosePauseMenu();

			// the host leaves: the guest hears why over its park (in words), and its OK leaves for the lobby
			host.Session!.End( "The host closed the park." );
			await frames( 0, () => joiner.Session.EndedWith != null, "the guest hears the end" );
			Assert.AreEqual( (CoopSessionEnd.Server, CoopReason.HostLeft), (joiner.Session.EndCause, joiner.Session.ServerReason) );
			Assert.AreEqual( GameFlow.GuestEndedScreenName, hud.Stack.Top!.Name );
			Assert.AreEqual( English( OpenTpwText.JoinHostLeft ), ((UiLabel)hud.Stack.Top.Find( "message" )!).Text() );
			Assert.IsNull( GameFlow.GuestStatusLine( joiner.Session, joinPlayer.Link, English ), "no status line once the session ended" );
		} );
	}

	[TestMethod]
	public async Task ACancelWhileTheParkIsBuiltEndsTheSessionAndLeavesItsSeat()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var (hostPlayer, state, frames) = await RealHost( server, players );
			var joinPlayer = await players.Add( server, "Latecomer", firstFrame: true );
			await frames( 30, null, "the host's first frames" );
			var joiner = LobbyGuest( joinPlayer );
			joiner.FlowSteps = _ => 600;
			var flow = joiner.Flow!;
			Assert.IsTrue( flow.StartJoining( state.Code ) );
			var attempt = flow.Joining!;
			await frames( 0, () => attempt.Session is { IsBuilding: true }, "the build began" );
			Assert.AreEqual( CoopJoinStage.Building, attempt.Stage );
			flow.CancelJoining();
			Assert.AreEqual( (CoopJoinStage.Done, CoopJoinOutcome.Cancelled), (attempt.Stage, attempt.Outcome) );
			Assert.AreEqual( CoopSessionEnd.Left, attempt.Session!.EndCause );
			Assert.IsNull( flow.HeldJoin );
			Assert.AreEqual( 1, joiner.FlowAbandoned, "its build released" );
			Assert.IsNull( flow.Level, "the lobby stays" );
			// the seat is left: the host's session holds no guest
			await frames( 0, () => hostPlayer.Link.Session!.Members.Count == 1, "the host to see the guest leave" );
		} );
	}

	/// <summary>A player's link, open, and an attempt of its own (no game) that the test steps as the game would, with the player's pump before it.</summary>
	private static async Task<CoopJoinAttempt> Attempt( CoopLinkTests.Player guest, string code, Func<CoopJoinAttempt, bool> until, string what, params CoopLinkTests.Player[] others )
	{
		var attempt = new CoopJoinAttempt( guest.Link, code );
		var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
		while ( !until( attempt ) )
		{
			guest.Session.Pump();
			foreach ( var other in others )
				other.Session.Pump();
			attempt.Step();
			Assert.IsTrue( DateTime.UtcNow < deadline, $"{what}: the attempt is {attempt.Stage} ({attempt.Outcome}: {attempt.FailureText})" );
			await Task.Delay( 5 );
		}
		return attempt;
	}

	[TestMethod]
	public async Task AnUnknownCodeAndAFullParkAreRefusedAndACodeThatCannotBeOneIsNotSent()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var hostPlayer = await players.Add( server, "Hostess" );
			var first = await players.Add( server, "First" );
			var second = await players.Add( server, "Second" );
			await CoopLinkTests.Open( hostPlayer );
			Assert.IsTrue( hostPlayer.Link.Create( listed: false, "Small park", 2 ) );
			await CoopLinkTests.Until( () => hostPlayer.Link.Session != null, "the session", hostPlayer );
			var code = hostPlayer.Link.Session!.Code;

			var unknown = await Attempt( first, code == "ABCDEF" ? "ABCDEG" : "ABCDEF", attempt => attempt.Stage == CoopJoinStage.Done, "the unknown code's refusal", hostPlayer );
			Assert.AreEqual( (CoopJoinOutcome.Refused, CoopReason.UnknownCode), (unknown.Outcome, unknown.Refusal) );
			Assert.AreEqual( OpenTpwText.JoinUnknownCode, GameFlow.JoinRefusalText( unknown.Refusal ) );
			unknown.Dispose();

			var notACode = await Attempt( first, "no such!", attempt => attempt.Stage == CoopJoinStage.Done, "the local refusal", hostPlayer );
			Assert.AreEqual( CoopJoinOutcome.NotACode, notACode.Outcome );
			notACode.Dispose();

			// the park takes two players: the first guest fills it, the second is refused
			var seated = await Attempt( first, code, attempt => attempt.Stage == CoopJoinStage.Answered, "the first guest's seat", hostPlayer );
			seated.Dispose();
			var full = await Attempt( second, code, attempt => attempt.Stage == CoopJoinStage.Done, "the full park's refusal", hostPlayer );
			Assert.AreEqual( (CoopJoinOutcome.Refused, CoopReason.Full), (full.Outcome, full.Refusal) );
			full.Dispose();
		} );
	}

	[TestMethod]
	public async Task ACancelBeforeTheAnswerAndAnAnswerTheGameCannotTakeLeaveTheSeat()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var hostPlayer = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await CoopLinkTests.Open( hostPlayer );
			var state = await CoopLinkTests.Share( hostPlayer );

			// cancelled while the join waits: the seat the server gives is left once it answers
			var cancelled = await Attempt( guest, state.Code, attempt => attempt.Stage == CoopJoinStage.Joining, "the join to go out", hostPlayer );
			cancelled.Cancel();
			Assert.AreEqual( CoopJoinStage.Cancelling, cancelled.Stage );
			var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
			while ( cancelled.Stage != CoopJoinStage.Done )
			{
				guest.Session.Pump();
				hostPlayer.Session.Pump();
				cancelled.Step();
				Assert.IsTrue( DateTime.UtcNow < deadline, "the cancelled join to end" );
				await Task.Delay( 5 );
			}
			Assert.AreEqual( CoopJoinOutcome.Cancelled, cancelled.Outcome );
			Assert.IsNull( guest.Link.Session );
			await CoopLinkTests.Until( () => hostPlayer.Changes.Count(change => change.State == CoopMemberState.Left) == 1, "the host to see the seat left", hostPlayer, guest );
			cancelled.Dispose();

			// answered, but the game cannot take it (a park came first): the seat is left
			var answered = await Attempt( guest, state.Code, attempt => attempt.Stage == CoopJoinStage.Answered, "the second join's answer", hostPlayer );
			Assert.IsTrue( answered.HoldsSeat );
			answered.Reject();
			Assert.AreEqual( (CoopJoinStage.Done, CoopJoinOutcome.NotTaken), (answered.Stage, answered.Outcome) );
			await CoopLinkTests.Until( () => hostPlayer.Changes.Count( change => change.State == CoopMemberState.Left ) == 2, "the host to see the second seat left", hostPlayer, guest );
			answered.Dispose();
		} );
	}

	[TestMethod]
	public async Task ASeatTheServerEndsBeforeTheGameTakesItIsTheServersEnd()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var hostPlayer = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await CoopLinkTests.Open( hostPlayer );
			var state = await CoopLinkTests.Share( hostPlayer );
			var attempt = await Attempt( guest, state.Code, attempt => attempt.Stage == CoopJoinStage.Joining, "the join to go out", hostPlayer );
			// the answer arrives, and the host's end with it, before the game's step takes the answer
			await CoopLinkTests.Until( () => attempt.Answer != null, "the answer", guest, hostPlayer );
			Assert.IsTrue( hostPlayer.Link.Leave( "The host went home." ) );
			await CoopLinkTests.Until( () => guest.Link.Session == null, "the guest to hear the end", guest, hostPlayer );
			attempt.Step();
			Assert.AreEqual( CoopJoinStage.Answered, attempt.Stage );
			Assert.IsFalse( attempt.HoldsSeat );
			attempt.Reject();
			Assert.AreEqual( (CoopJoinOutcome.Ended, CoopSessionEnd.Server, CoopReason.HostLeft), (attempt.Outcome, attempt.EndCause, attempt.ServerReason) );
			Assert.AreEqual( OpenTpwText.JoinHostLeft, GameFlow.JoinEndText( attempt.EndCause, attempt.ServerReason ) );
			attempt.Dispose();
		} );
	}

	[TestMethod]
	public async Task TheGameStartsAJoinOnlyInTheLobbyAndTakesItsAnswerOnlyWhileFreeToJoin()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var hostPlayer = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await CoopLinkTests.Open( hostPlayer );
			var state = await CoopLinkTests.Share( hostPlayer );
			var joiner = LobbyGuest( guest );
			var flow = joiner.Flow!;
			// not while a park is open, nor while a transition waits
			SetField( flow, "<Level>k__BackingField", NewPark().Level );
			Assert.IsFalse( flow.StartJoining( state.Code ), "a park is open" );
			SetField( flow, "<Level>k__BackingField", null );
			SetField( flow, "pending", (Action)(() => { }) );
			Assert.IsFalse( flow.StartJoining( state.Code ), "a transition waits" );
			SetField( flow, "pending", null );

			// a transition queued while the join waits for its answer: the game cannot take it, so the seat is left
			Assert.IsTrue( flow.StartJoining( state.Code ) );
			var attempt = flow.Joining!;
			var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
			while ( attempt.Stage is CoopJoinStage.Opening )
			{
				guest.Session.Pump();
				flow.StepCoopJoin();
				Assert.IsTrue( DateTime.UtcNow < deadline, "the join to go out" );
				await Task.Delay( 5 );
			}
			SetField( flow, "pending", (Action)(() => { }) );
			while ( attempt.Stage != CoopJoinStage.Done )
			{
				guest.Session.Pump();
				hostPlayer.Session.Pump();
				flow.StepCoopJoin();
				Assert.IsTrue( DateTime.UtcNow < deadline, "the answer to be left" );
				await Task.Delay( 5 );
			}
			Assert.AreEqual( CoopJoinOutcome.NotTaken, attempt.Outcome );
			Assert.IsNull( flow.HeldJoin );
			await CoopLinkTests.Until( () => hostPlayer.Link.Session!.Members.Count == 1 && hostPlayer.Changes.Any( change => change.State == CoopMemberState.Left ),
				"the host to see the seat left", hostPlayer, guest );
			flow.DismissJoining();
			Assert.IsNull( flow.Joining );
		} );
	}

	[TestMethod]
	public void EveryEndAndRefusalHasItsWords()
	{
		Assert.AreEqual( OpenTpwText.JoinHostLeft, GameFlow.JoinEndText( CoopSessionEnd.Server, CoopReason.HostLeft ) );
		Assert.AreEqual( OpenTpwText.JoinHostLeft, GameFlow.JoinEndText( CoopSessionEnd.Server, CoopReason.AccountDeleted ) );
		Assert.AreEqual( OpenTpwText.JoinKicked, GameFlow.JoinEndText( CoopSessionEnd.Server, CoopReason.Kicked ) );
		Assert.AreEqual( OpenTpwText.JoinHostGone, GameFlow.JoinEndText( CoopSessionEnd.Server, CoopReason.Expired ) );
		Assert.AreEqual( OpenTpwText.JoinBuildDiffers, GameFlow.JoinEndText( CoopSessionEnd.Server, CoopReason.BuildDiffers ) );
		Assert.AreEqual( OpenTpwText.JoinModsDiffer, GameFlow.JoinEndText( CoopSessionEnd.Server, CoopReason.ModsDiffer ) );
		Assert.AreEqual( OpenTpwText.SharedParkLinkLost, GameFlow.JoinEndText( CoopSessionEnd.LinkLost, CoopReason.None ) );
		Assert.AreEqual( OpenTpwText.JoinFailed, GameFlow.JoinEndText( CoopSessionEnd.JoinFailed, CoopReason.None ) );
		Assert.IsNull( GameFlow.JoinEndText( CoopSessionEnd.Left, CoopReason.None ), "this game left it: nothing to say" );
		Assert.AreEqual( OpenTpwText.JoinFull, GameFlow.JoinRefusalText( CoopReason.Full ) );
		Assert.AreEqual( OpenTpwText.JoinKicked, GameFlow.JoinRefusalText( CoopReason.Kicked ) );
		Assert.AreEqual( OpenTpwText.JoinBuildDiffers, GameFlow.JoinRefusalText( CoopReason.BuildDiffers ) );
		Assert.AreEqual( OpenTpwText.JoinModsDiffer, GameFlow.JoinRefusalText( CoopReason.ModsDiffer ) );
		Assert.AreEqual( OpenTpwText.SharedParkTryLater, GameFlow.JoinRefusalText( CoopReason.Rate ) );
		Assert.AreEqual( OpenTpwText.JoinInSession, GameFlow.JoinRefusalText( CoopReason.InSession ) );
		Assert.AreEqual( OpenTpwText.SharedParkLinkLost, GameFlow.JoinRefusalText( CoopReason.Unknown ) );
		Assert.AreEqual( OpenTpwText.JoinRefused, GameFlow.JoinRefusalText( CoopReason.NotMember ) );
	}
}
