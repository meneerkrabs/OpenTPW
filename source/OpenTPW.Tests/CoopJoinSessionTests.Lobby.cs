using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Online.Coop;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-session] The lobby's side of shared parks (C2d-3b; docs/COOP.md, "Invitations and the open list") over real links to the loopback server: the guest's game in the
/// lobby keeps its link open, a host's invitation comes over any lobby screen and Join joins the host's park through the game's attempt; invitations wait, expire, are put aside
/// and go with the lobby or the login; a join takes another game's link over only when the player says so; a stale or filled row of the open list is refused in words.
/// </summary>
public partial class CoopJoinSessionTests
{
	/// <summary>A guest's game in the lobby with its front end's menu, another lobby screen open on it, and the UI context the online screens need.</summary>
	private Peer LobbyGame( CoopLinkTests.Player player )
	{
		var game = LobbyGuest( player );
		var flow = game.Flow!;
		SetField( flow, "<Context>k__BackingField", OriginalUiTests.FakeContext() );
		SetField( flow, "<Menu>k__BackingField", LobbyMenu() );
		flow.Menu!.Stack.Push( new UiScreen( "lobbyOptions" ) );
		return game;
	}

	private static FrontEndMenu LobbyMenu() => new( OriginalUiTests.FakeStrings(),
		new[] { new LobbyIslandInfo( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 ) }, new FrontEndActions() );

	/// <summary>The online screens over <paramref name="flow"/>'s lobby, joining through the game.</summary>
	private static UI.OnlineScreens LobbyScreens( GameFlow flow, OnlineSession session ) =>
		new( flow.Menu!.Stack, OriginalUiTests.FakeStrings(), new UiModels( model => throw new FileNotFoundException( model ) ), new UI.OnlineHost { Session = session, Joining = flow } );

	private static void UpdateTop( UiScreenStack stack ) => stack.Update( OriginalUiTests.FakeContext(), UiInput.Idle( new Vector2( -1, -1 ) ) );

	private static string Message( UiScreenStack stack ) => ((UiLabel)stack.Top!.Find( "message" )!).Text();

	/// <summary>The lobby's frames of a game with no host of the stream tests: its pump, the attempt's step and the lobby's.</summary>
	private static async Task LobbyFrames( GameFlow flow, Func<bool> until, string what, params CoopLinkTests.Player[] players )
	{
		var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
		while ( !until() )
		{
			Assert.IsTrue( DateTime.UtcNow < deadline, $"Waited in vain for {what}: {string.Join( "; ", players.Select( player => $"{player.Name} {player.Link.State} {player.Link.Stopped}" ) )}" );
			foreach ( var player in players )
				player.Session.Pump();
			flow.StepCoopJoin();
			flow.StepCoopLobby();
			await Task.Delay( 5 );
		}
	}

	[TestMethod]
	public async Task AHostsInvitationComesOverAnyLobbyScreenAndJoinJoinsItsPark()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var (hostPlayer, state, frames) = await RealHost( server, players );
			var guestPlayer = await players.Add( server, "Latecomer", firstFrame: true );
			var game = LobbyGame( guestPlayer );
			var flow = game.Flow!;
			var stack = flow.Menu!.Stack;
			// the logged-in player's lobby opens the link by itself
			await frames( 0, () => guestPlayer.Link.State == CoopLinkState.Open, "the lobby's link" );
			Assert.IsTrue( hostPlayer.Link.Invite( "Latecomer" ) );
			await frames( 0, () => flow.ShownInvitation != null, "the invitation" );
			Assert.AreEqual( GameFlow.InvitationScreenName, stack.Top!.Name );
			Assert.AreEqual( "lobbyOptions", stack.Screens[^2].Name, "over the screen that was open" );
			Assert.AreEqual( string.Format( English( OpenTpwText.CoopInvitation ), "Hostess", "Our park" ), Message( stack ) );
			// Join: the game's attempt with the invitation's code, on the join screen
			((UiButton)stack.Top.Find( "choice0" )!).Clicked!();
			Assert.IsNull( flow.ShownInvitation );
			Assert.AreEqual( UI.OnlineScreens.JoinSharedParkScreenName, stack.Top!.Name );
			Assert.AreEqual( state.Code, ((UiTextField)stack.Top.Find( "code" )!).Text, "the code stays for another try" );
			var attempt = flow.Joining!;
			Assert.AreEqual( state.Code, attempt.Code );
			await frames( 0, () => attempt.Stage == CoopJoinStage.Done, "the join" );
			Assert.AreEqual( CoopJoinOutcome.Joined, attempt.Outcome );
			Assert.AreSame( flow.Level, game.Level );
			game.Session = attempt.Session;
			Assert.IsFalse( hostPlayer.Errors.Any( error => error.Request == CoopMessageType.Invite ), "the invitation went through" );
			// in the shared park the link is the park's
			await frames( 10, null, "the guest's park" );
			Assert.AreEqual( CoopLinkState.Open, guestPlayer.Link.State );
		} );
	}

	[TestMethod]
	public async Task NotNowPutsAnInvitationAsideAndARepeatTakesThePlaceOfTheOneShown()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var hostPlayer = await players.Add( server, "Hostess" );
			await CoopLinkTests.Open( hostPlayer );
			var state = await CoopLinkTests.Share( hostPlayer );
			var guestPlayer = await players.Add( server, "Guest" );
			var flow = LobbyGame( guestPlayer ).Flow!;
			var stack = flow.Menu!.Stack;
			await LobbyFrames( flow, () => guestPlayer.Link.State == CoopLinkState.Open, "the lobby's link", hostPlayer, guestPlayer );
			Assert.IsTrue( hostPlayer.Link.Invite( "Guest" ) );
			await LobbyFrames( flow, () => flow.ShownInvitation != null, "the invitation", hostPlayer, guestPlayer );
			// the host invites again: the one shown takes it, nothing more waits
			Assert.IsTrue( hostPlayer.Link.Invite( "Guest" ) );
			await LobbyFrames( flow, () => guestPlayer.Invitations.Count == 2, "the repeat", hostPlayer, guestPlayer );
			Assert.AreEqual( 0, flow.WaitingInvitations.Count );
			Assert.AreEqual( 1, stack.Screens.Count( screen => screen.Name == GameFlow.InvitationScreenName ) );
			// Not now: it goes and nothing follows; the server hears nothing
			((UiButton)stack.Top!.Find( "choice1" )!).Clicked!();
			Assert.IsNull( flow.ShownInvitation );
			Assert.AreEqual( "lobbyOptions", stack.Top!.Name );
			for ( var step = 0; step < 5; step++ )
				flow.StepCoopLobby();
			Assert.AreEqual( "lobbyOptions", stack.Top!.Name );
			Assert.IsNull( flow.Joining );
			Assert.AreEqual( 0, hostPlayer.Errors.Count );
			Assert.AreEqual( 1, hostPlayer.Link.Session!.Members.Count );
			// Back on the dialog puts it aside as well
			Assert.IsTrue( hostPlayer.Link.Invite( "Guest" ) );
			await LobbyFrames( flow, () => flow.ShownInvitation != null, "the third invitation", hostPlayer, guestPlayer );
			stack.Top!.Back!();
			Assert.IsNull( flow.ShownInvitation );
			flow.StepCoopLobby();
			Assert.AreEqual( "lobbyOptions", stack.Top!.Name );
			Assert.AreEqual( state.Id, hostPlayer.Link.Session.Id );
		} );
	}

	[TestMethod]
	public async Task InvitationsWaitWhileAJoinRunsExpireUnseenAndGoWithTheLogin()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var first = await players.Add( server, "Hostess" );
			var second = await players.Add( server, "Other" );
			await CoopLinkTests.Open( first, second );
			Assert.IsTrue( first.Link.Create( listed: false, "First park", 4 ) );
			Assert.IsTrue( second.Link.Create( listed: false, "Second park", 4 ) );
			await CoopLinkTests.Until( () => first.Link.Session != null && second.Link.Session != null, "the parks", first, second );
			var guestPlayer = await players.Add( server, "Guest" );
			var flow = LobbyGame( guestPlayer ).Flow!;
			var stack = flow.Menu!.Stack;
			await LobbyFrames( flow, () => guestPlayer.Link.State == CoopLinkState.Open, "the lobby's link", first, second, guestPlayer );

			// while a join runs (a code no park has; its step held back here), an invitation waits; once the join is over it shows
			Assert.IsTrue( flow.StartJoining( "ZZZZZZ" ) );
			Assert.IsTrue( first.Link.Invite( "Guest" ) );
			await CoopLinkTests.Until( () =>
			{
				flow.StepCoopLobby();
				return flow.WaitingInvitations.Count == 1;
			}, "the invitation to wait", first, guestPlayer );
			Assert.IsNull( flow.ShownInvitation );
			Assert.AreEqual( "lobbyOptions", stack.Top!.Name );
			await LobbyFrames( flow, () => flow.Joining!.Stage == CoopJoinStage.Done, "the join to be refused", guestPlayer );
			Assert.AreEqual( (CoopJoinOutcome.Refused, CoopReason.UnknownCode), (flow.Joining!.Outcome, flow.Joining.Refusal) );
			flow.StepCoopLobby();
			Assert.AreEqual( "First park", flow.ShownInvitation?.Title );
			Assert.AreEqual( GameFlow.InvitationScreenName, stack.Top!.Name );

			// one shows at a time: the next waits, and one that waits too long is dropped unseen
			Assert.IsTrue( second.Link.Invite( "Guest" ) );
			await LobbyFrames( flow, () => flow.WaitingInvitations.Count == 1, "the second invitation to wait", second, guestPlayer );
			clock += GameFlow.InvitationKeepSeconds + 1;
			flow.StepCoopLobby();
			Assert.AreEqual( 0, flow.WaitingInvitations.Count );
			((UiButton)stack.Top!.Find( "choice1" )!).Clicked!();
			flow.StepCoopLobby();
			Assert.IsNull( flow.ShownInvitation );
			Assert.AreEqual( "lobbyOptions", stack.Top!.Name );

			// a logout closes the invitation on screen and forgets the waiting ones
			Assert.IsTrue( first.Link.Invite( "Guest" ) );
			await LobbyFrames( flow, () => flow.ShownInvitation != null, "the invitation again", first, guestPlayer );
			Assert.IsTrue( second.Link.Invite( "Guest" ) );
			await LobbyFrames( flow, () => flow.WaitingInvitations.Count == 1, "another to wait", second, guestPlayer );
			guestPlayer.Session.Disconnect();
			flow.StepCoopLobby();
			Assert.IsNull( flow.ShownInvitation );
			Assert.AreEqual( 0, flow.WaitingInvitations.Count );
			Assert.AreEqual( "lobbyOptions", stack.Top!.Name );
			Assert.AreEqual( CoopLinkState.Closed, guestPlayer.Link.State );
			// another login opens the link again, with nothing of the old one
			guestPlayer.Session.UseClient( await server.LoginAsync( "Guest", register: false ), "Guest" );
			await LobbyFrames( flow, () => guestPlayer.Link.State == CoopLinkState.Open, "the link of the new login", guestPlayer );
			Assert.IsNull( flow.ShownInvitation );
		} );
	}

	[TestMethod]
	public async Task AnInvitationGoesOnlyToTheLobbyAndClosesWhenTheLobbyIsLeft()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var hostPlayer = await players.Add( server, "Hostess" );
			await CoopLinkTests.Open( hostPlayer );
			await CoopLinkTests.Share( hostPlayer );
			var guestPlayer = await players.Add( server, "Guest" );
			var flow = LobbyGame( guestPlayer ).Flow!;
			await LobbyFrames( flow, () => guestPlayer.Link.State == CoopLinkState.Open, "the lobby's link", hostPlayer, guestPlayer );
			Assert.IsTrue( hostPlayer.Link.Invite( "Guest" ) );
			await LobbyFrames( flow, () => flow.ShownInvitation != null, "the invitation", hostPlayer, guestPlayer );

			// a rebuild of the lobby (a language change makes a new menu) shows it again
			var rebuilt = LobbyMenu();
			SetField( flow, "<Menu>k__BackingField", rebuilt );
			flow.StepCoopLobby();
			Assert.AreEqual( GameFlow.InvitationScreenName, rebuilt.Stack.Top!.Name );
			// a park on its way (a browser's park waits for its files in the lobby): the invitation closes, and so does the idle link
			SetField( flow, "pending", (Action)(() => { }) );
			flow.StepCoopLobby();
			Assert.IsNull( flow.ShownInvitation );
			Assert.AreNotEqual( GameFlow.InvitationScreenName, rebuilt.Stack.Top!.Name );
			Assert.AreEqual( CoopLinkState.Closed, guestPlayer.Link.State );
			// in an ordinary park the link stays closed: the host hears that the player is not online for shared parks
			SetField( flow, "pending", null );
			SetField( flow, "<Level>k__BackingField", NewPark().Level );
			try
			{
				flow.StepCoopLobby();
				Assert.AreEqual( CoopLinkState.Closed, guestPlayer.Link.State );
				Assert.IsTrue( hostPlayer.Link.Invite( "Guest" ) );
				await CoopLinkTests.Until( () => hostPlayer.Errors.Any( error => error.Request == CoopMessageType.Invite ), "the host's refusal", hostPlayer );
				Assert.AreEqual( CoopReason.NotOnline, hostPlayer.Errors.Last().Reason );
			}
			finally
			{
				SetField( flow, "<Level>k__BackingField", null );
			}
		} );
	}

	[TestMethod]
	public async Task AJoinTakesTheLinkFromAnotherGameOnlyWhenThePlayerSaysSo()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var (hostPlayer, state, frames) = await RealHost( server, players );
			var twin = await players.Add( server, "Latecomer", firstFrame: true );
			var guestPlayer = await players.Add( server, "Latecomer", firstFrame: true, register: false );
			await CoopLinkTests.Open( twin );
			var game = LobbyGame( guestPlayer );
			var flow = game.Flow!;
			var stack = flow.Menu!.Stack;
			// the lobby's own link gives way to the other game
			await frames( 0, () => guestPlayer.Link.Stopped == CoopStop.InUseElsewhere, "the lobby's link to give way" );
			// a join with the code opens the link itself, still without a take-over
			LobbyScreens( flow, guestPlayer.Session ).ShowJoinSharedPark();
			((UiTextField)stack.Top!.Find( "code" )!).Text = state.Code;
			UpdateTop( stack );
			((UiButton)stack.Top.Find( "join" )!).Clicked!();
			var attempt = flow.Joining!;
			await frames( 0, () => attempt.Stage == CoopJoinStage.Done, "the join to give way" );
			Assert.AreEqual( (CoopJoinOutcome.LinkStopped, CoopStop.InUseElsewhere), (attempt.Outcome, attempt.Stop) );
			twin.Session.Pump();
			Assert.AreEqual( (CoopLinkState.Open, CoopStop.None), (twin.Link.State, twin.Link.Stopped), "nothing was taken over before the click" );
			UpdateTop( stack );
			var takeOver = (UiButton)stack.Top.Find( "takeOver" )!;
			Assert.IsTrue( takeOver.Visible );
			Assert.AreEqual( English( OpenTpwText.SharedParkLinkInUse ) + " " + English( OpenTpwText.JoinTakeOverWarning ), Message( stack ) );
			// the click takes the link over and joins with the same code
			takeOver.Clicked!();
			var again = flow.Joining!;
			Assert.AreNotSame( attempt, again );
			Assert.AreEqual( state.Code, again.Code );
			await frames( 0, () => again.Stage == CoopJoinStage.Done, "the join over the link taken over" );
			Assert.AreEqual( CoopJoinOutcome.Joined, again.Outcome );
			game.Session = again.Session;
			await CoopLinkTests.Until( () => twin.Link.Stopped == CoopStop.Replaced, "the other game to hear it", twin );
		} );
	}

	[TestMethod]
	public async Task AJoinLeavesTheParkOfAnEarlierRunThatTheLobbysLinkStoppedAt()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var (hostPlayer, state, frames) = await RealHost( server, players );
			// an earlier run of the player's game hosted a park and is gone (its socket never comes back); a guest is still there
			var earlier = await players.Add( server, "Latecomer", firstFrame: true );
			var stranded = await players.Add( server, "Stranded" );
			await CoopLinkTests.Open( earlier, stranded );
			var old = await CoopLinkTests.Share( earlier, stranded );
			earlier.Link.Connector = async ( client, cancel ) =>
			{
				await Task.Delay( System.Threading.Timeout.Infinite, cancel );
				throw new OperationCanceledException();
			};
			earlier.Link.DropSocket();
			await CoopLinkTests.Until( () => stranded.Changes.Any( change => change.Member.Id == old.Host && change.State == CoopMemberState.Disconnected ), "the earlier run gone", stranded );
			// this run's lobby leaves that park alone: it cannot tell it from another game's that is away for a moment
			var guestPlayer = await players.Add( server, "Latecomer", firstFrame: true, register: false );
			var game = LobbyGame( guestPlayer );
			var flow = game.Flow!;
			await frames( 0, () => guestPlayer.Link.Stopped == CoopStop.HeldElsewhere, "the lobby's link to stop" );
			Assert.AreEqual( 0, stranded.Ended.Count );
			// a join is the player's own act: its open leaves that park, then joins
			Assert.IsTrue( flow.StartJoining( state.Code ) );
			var attempt = flow.Joining!;
			await frames( 0, () => attempt.Stage == CoopJoinStage.Done, "the join" );
			Assert.AreEqual( CoopJoinOutcome.Joined, attempt.Outcome );
			game.Session = attempt.Session;
			await CoopLinkTests.Until( () => stranded.Ended.Count == 1, "the earlier park's end", stranded );
			Assert.AreEqual( (old.Id, CoopReason.HostLeft), (stranded.Ended[0].SessionId, stranded.Ended[0].Reason) );
		} );
	}

	[TestMethod]
	public async Task AStaleOrFilledRowOfTheOpenListIsRefusedInWords()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var stale = await players.Add( server, "Hostess" );
			var small = await players.Add( server, "Smallhost" );
			var first = await players.Add( server, "Firstguest" );
			await CoopLinkTests.Open( stale, small, first );
			Assert.IsTrue( stale.Link.Create( listed: true, "Ending park", 4 ) );
			Assert.IsTrue( small.Link.Create( listed: true, "Small park", 2 ) );
			await CoopLinkTests.Until( () => stale.Link.Session != null && small.Link.Session != null, "the parks", stale, small );
			var guestPlayer = await players.Add( server, "Guest" );
			var flow = LobbyGame( guestPlayer ).Flow!;
			var stack = flow.Menu!.Stack;
			LobbyScreens( flow, guestPlayer.Session ).ShowSharedParks();
			var list = (UiScrollList)stack.Top!.Find( "parks" )!;
			await LobbyFrames( flow, () => { UpdateTop( stack ); return list.Rows().Count == 2; }, "the open list", guestPlayer );
			CollectionAssert.AreEquivalent( new[] { "Ending park (Hostess)", "Small park (Smallhost)" }, list.Rows().ToArray() );

			// the park ends after the list came: its row's join is refused in words; the code stays for another try, and Back leads to the list again
			var staleCode = stale.Link.Session!.Code;
			var staleId = stale.Link.Session.Id;
			// the server ends the park before the row's join goes out: a join that raced the leave would be seated first and then hear that the host left
			var ended = false;
			stale.Link.DropReceived = message =>
			{
				ended |= message is CoopEnded end && end.SessionId == staleId;
				return false;
			};
			Assert.IsTrue( stale.Link.Leave( "Closing." ) );
			await CoopLinkTests.Until( () => ended, "the server to end the park", stale );
			list.Selected = list.Rows().ToList().IndexOf( "Ending park (Hostess)" );
			UpdateTop( stack );
			((UiButton)stack.Top.Find( "join" )!).Clicked!();
			Assert.AreEqual( UI.OnlineScreens.JoinSharedParkScreenName, stack.Top!.Name );
			await LobbyFrames( flow, () => flow.Joining!.Stage == CoopJoinStage.Done, "the stale join", stale, guestPlayer );
			UpdateTop( stack );
			Assert.AreEqual( English( OpenTpwText.JoinUnknownCode ), Message( stack ) );
			((UiButton)stack.Top!.Find( "again" )!).Clicked!();
			UpdateTop( stack );
			var code = (UiTextField)stack.Top!.Find( "code" )!;
			Assert.IsTrue( code.Visible );
			Assert.AreEqual( staleCode, code.Text );
			stack.Top!.Back!();
			Assert.AreEqual( UI.OnlineScreens.SharedParksScreenName, stack.Top!.Name );

			// a park that filled after the list came: refused as full
			Assert.IsTrue( first.Link.Join( small.Link.Session!.Code ) );
			await CoopLinkTests.Until( () => small.Link.Session!.Members.Count == 2, "the small park to fill", small, first );
			list.Selected = list.Rows().ToList().IndexOf( "Small park (Smallhost)" );
			UpdateTop( stack );
			((UiButton)stack.Top.Find( "join" )!).Clicked!();
			await LobbyFrames( flow, () => flow.Joining!.Stage == CoopJoinStage.Done, "the full join", small, first, guestPlayer );
			UpdateTop( stack );
			Assert.AreEqual( English( OpenTpwText.JoinFull ), Message( stack ) );
			stack.Top!.Back!();
			// Refresh: the ended park is gone and the full one is not listed
			((UiButton)stack.Top!.Find( "refresh" )!).Clicked!();
			await LobbyFrames( flow, () => { UpdateTop( stack ); return list.Rows().Count == 0 && Message( stack ) == English( OpenTpwText.SharedParksNone ); }, "the list again", guestPlayer );
		} );
	}
}
