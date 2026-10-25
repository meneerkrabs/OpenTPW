using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-session] The lobby's link (C2d-3b; docs/COOP.md, "Invitations and the open list"): <see cref="CoopLobbyLink"/> over real links to the loopback server opens the
/// link for a logged-in player in the lobby and closes it idle elsewhere, leaves a link in use alone, never takes the link from another game of the account nor leaves that
/// game's park, and opens a lost link again on a spread timer; the link spreads its own reconnect waits.
/// </summary>
public partial class CoopLinkTests
{
	private static (Uri Server, string Account) LoginOf( Player player ) => player.Session.LoginIdentity!.Value;

	[TestMethod]
	public async Task TheLobbyOpensTheLinkForALoggedInPlayerAndClosesItIdleElsewhere()
	{
		await WithServer( async ( server, players ) =>
		{
			var guest = await players.Add( server, "Guest" );
			var lobby = new CoopLobbyLink( guest.Link, () => 0 );
			// logged in, in the lobby: it opens, once
			lobby.Step( wanted: true, LoginOf( guest ), inUse: false );
			Assert.AreEqual( CoopLinkState.Opening, guest.Link.State );
			await Until( () => guest.Link.State == CoopLinkState.Open, "the lobby's link", guest );
			lobby.Step( true, LoginOf( guest ), false );
			Assert.AreEqual( 1, guest.Link.Connections );
			// an ordinary park: the idle link closes, with no stop
			lobby.Step( false, LoginOf( guest ), false );
			Assert.AreEqual( (CoopLinkState.Closed, CoopStop.None), (guest.Link.State, guest.Link.Stopped) );
			// back in the lobby: it opens again
			lobby.Step( true, LoginOf( guest ), false );
			await Until( () => guest.Link.State == CoopLinkState.Open, "the link back", guest );
			Assert.AreEqual( 2, guest.Link.Connections );
			// a link something else closed opens again while the lobby wants it
			guest.Link.Close();
			lobby.Step( true, LoginOf( guest ), false );
			await Until( () => guest.Link.State == CoopLinkState.Open, "the link once more", guest );
			Assert.AreEqual( 3, guest.Link.Connections );
		} );
	}

	[TestMethod]
	public async Task TheLobbyLeavesALinkInUseAlone()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await Open( host, guest );
			var lobby = new CoopLobbyLink( host.Link, () => 0 );
			// a create on its way: the server would seat the player once it reads it, also after a close without a leave, so nothing closes
			Assert.IsTrue( host.Link.Create( listed: false, "Our park", 4 ) );
			Assert.IsTrue( host.Link.Waiting );
			lobby.Step( false, LoginOf( host ), false );
			Assert.AreEqual( CoopLinkState.Open, host.Link.State );
			await Until( () => host.Link.Session != null, "the park", host );
			Assert.IsFalse( host.Link.Waiting );
			// the host's park: the link is the park's
			lobby.Step( false, LoginOf( host ), false );
			Assert.AreEqual( CoopLinkState.Open, host.Link.State );
			// a join on its way, then its seat: the same
			var guestLobby = new CoopLobbyLink( guest.Link, () => 0 );
			Assert.IsTrue( guest.Link.Join( host.Link.Session!.Code ) );
			Assert.IsTrue( guest.Link.Waiting );
			guestLobby.Step( false, LoginOf( guest ), false );
			Assert.AreEqual( CoopLinkState.Open, guest.Link.State );
			await Until( () => guest.Link.Session != null, "the guest's seat", guest );
			guestLobby.Step( false, LoginOf( guest ), false );
			Assert.AreEqual( CoopLinkState.Open, guest.Link.State );
			// once the guest left, the link is idle: an attempt or a share that uses it keeps it, else it closes (the leave goes out before the socket closes)
			Assert.IsTrue( guest.Link.Leave( "Bye." ) );
			Assert.IsFalse( guest.Link.Settling );
			guestLobby.Step( false, LoginOf( guest ), inUse: true );
			Assert.AreEqual( CoopLinkState.Open, guest.Link.State );
			guestLobby.Step( false, LoginOf( guest ), inUse: false );
			Assert.AreEqual( CoopLinkState.Closed, guest.Link.State );
			await Until( () => host.Link.Session!.Members.Count == 1, "the guest's leave", host );
		} );
	}

	[TestMethod]
	public async Task TheLobbyNeverTakesTheLinkFromAnotherGameOfTheAccountAndTriesOnceAStay()
	{
		await WithServer( async ( server, players ) =>
		{
			var first = await players.Add( server, "Twin" );
			var second = await players.Add( server, "Twin", register: false );
			await Open( first );
			var lobby = new CoopLobbyLink( second.Link, () => 0 );
			lobby.Step( true, LoginOf( second ), false );
			await Until( () => second.Link.Stopped == CoopStop.InUseElsewhere, "the lobby's link to give way", first, second );
			// the stop is kept: the rest of the stay tries nothing
			for ( var step = 0; step < 20; step++ )
			{
				lobby.Step( true, LoginOf( second ), false );
				second.Session.Pump();
				await Task.Delay( 5 );
			}
			Assert.AreEqual( (1, CoopLinkState.Closed), (second.Link.Connections, second.Link.State) );
			// the next stay tries once more, still without a take-over
			lobby.Step( false, LoginOf( second ), false );
			lobby.Step( true, LoginOf( second ), false );
			await Until( () => second.Link.Stopped == CoopStop.InUseElsewhere, "the second try to give way", first, second );
			Assert.AreEqual( 2, second.Link.Connections );
			Assert.AreEqual( (CoopLinkState.Open, CoopStop.None), (first.Link.State, first.Link.Stopped), "the other game keeps its link" );
		} );
	}

	[TestMethod]
	public async Task TheLobbysLinkLeavesNoParkOfTheAccountItDidNotAskFor()
	{
		await WithServer( async ( server, players ) =>
		{
			var first = await players.Add( server, "Twin" );
			var second = await players.Add( server, "Twin", register: false );
			var guest = await players.Add( server, "Guest" );
			await Open( first, guest );
			var state = await Share( first, guest );
			// the hosting game's socket is away for a moment: its reconnects wait
			var back = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
			var real = first.Link.Connector;
			first.Link.Connector = async ( client, cancel ) =>
			{
				await back.Task.WaitAsync( cancel );
				return await real( client, cancel );
			};
			first.Link.DropSocket();
			await Until( () => guest.Changes.Any( change => change.Member.Id == state.Host && change.State == CoopMemberState.Disconnected ), "the host away", first, guest );
			// the account's other game, in the lobby: the welcome names the park, which the lobby leaves alone
			var lobby = new CoopLobbyLink( second.Link, () => 0 );
			lobby.Step( true, LoginOf( second ), false );
			await Until( () => second.Link.Stopped == CoopStop.HeldElsewhere, "the lobby's link to stop", second, guest );
			Assert.AreEqual( 0, guest.Ended.Count );
			// an explicit open would leave it (any open that did not ask for the park does): the lobby's next stay opens as the lobby again
			lobby.Step( false, LoginOf( second ), false );
			lobby.Step( true, LoginOf( second ), false );
			await Until( () => second.Link.Stopped == CoopStop.HeldElsewhere, "the next stay to stop as well", second, guest );
			// the hosting game comes back once the lobby's socket is gone: its park goes on
			await Task.Delay( 200 );
			back.SetResult();
			await Until( () => first.Resumes.Count == 1, "the host's resume", first, guest );
			Assert.AreEqual( state.Id, first.Link.Session!.Id );
			Assert.AreEqual( 0, guest.Ended.Count, "the guest's park did not end" );
		} );
	}

	[TestMethod]
	public async Task ALobbysLinkThatAsksForASeatIsAnyOpensAgain()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await Open( host );
			var state = await Share( host );
			bool FromTheLobby( Player player ) => (bool)typeof( CoopLink ).GetField( "lobbyOpen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( player.Link )!;
			// a join from a link the lobby opened: a welcome that names the seat it may make (its answer lost with a socket) is this game's to leave, never another game's
			guest.Link.OpenInLobby();
			await Until( () => guest.Link.State == CoopLinkState.Open, "the lobby's link", guest );
			Assert.IsTrue( FromTheLobby( guest ) );
			Assert.IsTrue( guest.Link.Join( state.Code ) );
			Assert.IsFalse( FromTheLobby( guest ) );
			await Until( () => guest.Link.Session != null, "the seat", host, guest );
			Assert.IsTrue( guest.Link.Leave( "Bye." ) );
			// a create, the same; an explicit open of a closed link is any open's from the start
			guest.Link.Close();
			guest.Link.OpenInLobby();
			Assert.IsTrue( FromTheLobby( guest ) );
			await Until( () => guest.Link.State == CoopLinkState.Open, "the lobby's link again", guest );
			Assert.IsTrue( guest.Link.Create( listed: false, "Mine", 2 ) );
			Assert.IsFalse( FromTheLobby( guest ) );
			await Until( () => guest.Link.Session != null, "the guest's own park", guest );
			guest.Link.Close( "Bye." );
			guest.Link.OpenInLobby();
			guest.Link.Close();
			guest.Link.Open();
			Assert.IsFalse( FromTheLobby( guest ) );
		} );
	}

	/// <summary>The server seats the guest, but the answer is lost with the socket: the join is refused with no seat for the game, which the server still holds.</summary>
	private static async Task<CoopSessionState> SeatWithALostAnswer( Player host, Player guest, Func<bool> down )
	{
		await Open( host );
		var state = await Share( host );
		await Until( () => guest.Link.State == CoopLinkState.Open, "the guest's link", guest );
		guest.Link.DropReceived = message => message is CoopJoined;
		Assert.IsTrue( guest.Link.Join( state.Code ) );
		await Until( () => host.Link.Session!.Members.Count == 2, "the server to seat the guest", host, guest );
		var real = guest.Link.Connector;
		guest.Link.Connector = async ( client, cancel ) => down() ? throw new WebSocketException( "The network is down." ) : await real( client, cancel );
		guest.Link.DropReceived = null;
		guest.Link.DropSocket();
		await Until( () => guest.Errors.Any( error => error.Request == CoopMessageType.Join && error.Reason == CoopReason.Unknown ), "the lost answer", guest );
		Assert.IsNull( guest.Link.Session );
		Assert.IsTrue( guest.Link.Settling, "the seat waits for the link's next welcome" );
		return state;
	}

	[TestMethod]
	public async Task TheLobbyKeepsALinkWhoseLostAnswerMadeASeatForAWhileSoItsWelcomeLeavesIt()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			var now = 0.0;
			var lobby = new CoopLobbyLink( guest.Link, () => now );
			lobby.Step( true, LoginOf( guest ), false );
			var down = true;
			await SeatWithALostAnswer( host, guest, () => down );
			// an ordinary park: the link stays for a while, and its reconnect's welcome leaves the seat
			lobby.Step( false, LoginOf( guest ), false );
			now += CoopLobbyLink.SettleHoldSeconds - 1;
			lobby.Step( false, LoginOf( guest ), false );
			Assert.AreNotEqual( CoopLinkState.Closed, guest.Link.State );
			down = false;
			await Until( () => host.Link.Session!.Members.Count == 1, "the seat to be left", host, guest );
			Assert.IsFalse( guest.Link.Settling );
			lobby.Step( false, LoginOf( guest ), false );
			Assert.AreEqual( CoopLinkState.Closed, guest.Link.State );

			// not for longer: a link that cannot come back is closed, and the seat stays on the server (the player's next join or share leaves it)
			lobby.Step( true, LoginOf( guest ), false );
			await SeatAgain();
			lobby.Step( false, LoginOf( guest ), false );
			now += CoopLobbyLink.SettleHoldSeconds + 1;
			lobby.Step( false, LoginOf( guest ), false );
			Assert.AreEqual( CoopLinkState.Closed, guest.Link.State );
			Assert.AreEqual( 2, host.Link.Session!.Members.Count );

			async Task SeatAgain()
			{
				down = false;
				await Until( () => guest.Link.State == CoopLinkState.Open, "the lobby's link back", guest );
				guest.Link.DropReceived = message => message is CoopJoined;
				Assert.IsTrue( guest.Link.Join( host.Link.Session!.Code ) );
				await Until( () => host.Link.Session!.Members.Count == 2, "the server to seat the guest again", host, guest );
				down = true;
				guest.Link.DropReceived = null;
				guest.Link.DropSocket();
				await Until( () => guest.Errors.Count( error => error.Request == CoopMessageType.Join && error.Reason == CoopReason.Unknown ) == 2, "the second lost answer", guest );
				Assert.IsTrue( guest.Link.Settling );
			}
		} );
	}

	[TestMethod]
	public async Task ASeatALostAnswerMadeIsLeftByThePlayersNextActNeverByTheLobby()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			guest.Link.ReconnectWindow = TimeSpan.FromMilliseconds( 200 );
			var lobby = new CoopLobbyLink( guest.Link, () => 0 );
			lobby.Step( true, LoginOf( guest ), false );
			var down = true;
			var state = await SeatWithALostAnswer( host, guest, () => down );
			// the link cannot come back and gives up before its welcome
			await Until( () => guest.Link.Stopped == CoopStop.Lost, "the link to be lost", guest );
			// the next stay in the lobby: its welcome names the seat, which the lobby cannot tell from another game's, so it stays
			down = false;
			lobby.Step( false, LoginOf( guest ), false );
			lobby.Step( true, LoginOf( guest ), false );
			await Until( () => guest.Link.Stopped == CoopStop.HeldElsewhere, "the lobby's link to stop", guest );
			Assert.AreEqual( 2, host.Link.Session!.Members.Count );
			// the player's next act (a join or a share opens the link itself) leaves it
			guest.Link.Open();
			await Until( () => host.Link.Session!.Members.Count == 1, "the seat to be left", host, guest );
			Assert.AreEqual( state.Id, host.Link.Session.Id );
		} );
	}

	[TestMethod]
	public async Task AnOwnLeaveLostBeforeItWentOutGoesAgainAtTheNextWelcome()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await Open( host, guest );
			var state = await Share( host, guest );
			// the guest leaves while its link cannot send: the leave waits for the account's next welcome
			var down = true;
			var real = guest.Link.Connector;
			guest.Link.Connector = async ( client, cancel ) => down ? throw new WebSocketException( "The network is down." ) : await real( client, cancel );
			guest.Link.DropSocket();
			await Until( () => guest.Link.State == CoopLinkState.Reconnecting, "the guest's link to reconnect", guest );
			Assert.IsTrue( guest.Link.Leave( "Bye." ) );
			Assert.IsTrue( guest.Link.Settling );
			// the link gives up soon now: a short window from the start could pass on a loaded machine before the test's pump saw the reconnect
			guest.Link.ReconnectWindow = TimeSpan.FromMilliseconds( 200 );
			await Until( () => guest.Link.Stopped == CoopStop.Lost, "the link to be lost", guest );
			// the lobby's open: its welcome names the session the leave is for, which it sends; that leave is lost before it goes out, and kept
			down = false;
			guest.Link.DropSent = message => message is CoopLeave;
			var lobby = new CoopLobbyLink( guest.Link, () => 0 );
			lobby.Step( true, LoginOf( guest ), false );
			await Until( () => guest.Link.State == CoopLinkState.Open, "the lobby's link", host, guest );
			Assert.AreEqual( CoopStop.None, guest.Link.Stopped, "this game's own leave, not another game's park" );
			Assert.IsTrue( guest.Link.Settling, "an unconfirmed leave is kept" );
			Assert.AreEqual( 2, host.Link.Session!.Members.Count );
			// the next welcome sends it again, and the server's confirmation settles it; the network is back, so the reconnect has the usual window (a loaded
			// machine's reconnect may take longer than the short one the loss above took)
			guest.Link.DropSent = null;
			guest.Link.ReconnectWindow = CoopLink.DefaultReconnectWindow;
			guest.Link.DropSocket();
			await Until( () => host.Link.Session!.Members.Count == 1, "the seat to be left", host, guest );
			await Until( () => !guest.Link.Settling, "the confirmation", host, guest );
			Assert.AreEqual( state.Id, host.Link.Session.Id );
		} );
	}

	[TestMethod]
	public async Task ALostLobbyLinkOpensAgainOnASpreadTimerAtMostTenTimesAStay()
	{
		await WithServer( async ( server, players ) =>
		{
			var guest = await players.Add( server, "Guest" );
			guest.Link.ReconnectWindow = TimeSpan.FromMilliseconds( 150 );
			var real = guest.Link.Connector;
			var down = true;
			guest.Link.Connector = async ( client, cancel ) => down ? throw new WebSocketException( "The network is down." ) : await real( client, cancel );
			var now = 0.0;
			var spread = 0.0;
			var lobby = new CoopLobbyLink( guest.Link, () => now, () => spread );
			lobby.Step( true, LoginOf( guest ), false );
			await Until( () => guest.Link.Stopped == CoopStop.Lost, "the link to be lost", guest );
			for ( var retry = 1; retry <= CoopLobbyLink.MaximumLostRetries; retry++ )
			{
				// the wait is spread from 70 to 130 per cent of a minute: the first at the least, the others at the most
				var wait = CoopLobbyLink.LostRetrySeconds * (retry == 1 ? 0.7 : 1.3);
				lobby.Step( true, LoginOf( guest ), false );
				now += wait - 0.5;
				lobby.Step( true, LoginOf( guest ), false );
				Assert.AreEqual( (retry - 1, CoopLinkState.Closed), (lobby.LostRetries, guest.Link.State), $"retry {retry} not before its time" );
				now += 1;
				lobby.Step( true, LoginOf( guest ), false );
				Assert.AreEqual( retry, lobby.LostRetries );
				Assert.AreNotEqual( CoopLinkState.Closed, guest.Link.State );
				spread = 1;
				await Until( () => guest.Link.Stopped == CoopStop.Lost, $"retry {retry} to be lost", guest );
			}
			// at most ten a stay
			for ( var step = 0; step < 3; step++ )
			{
				lobby.Step( true, LoginOf( guest ), false );
				now += 10 * CoopLobbyLink.LostRetrySeconds;
			}
			Assert.AreEqual( (CoopLinkState.Closed, CoopStop.Lost, CoopLobbyLink.MaximumLostRetries), (guest.Link.State, guest.Link.Stopped, lobby.LostRetries) );
			// the next stay starts over
			down = false;
			lobby.Step( false, LoginOf( guest ), false );
			Assert.AreEqual( 0, lobby.LostRetries );
			lobby.Step( true, LoginOf( guest ), false );
			await Until( () => guest.Link.State == CoopLinkState.Open, "the link back", guest );
			// a link that drops soon after it opened goes on with the stay's count
			async Task Lose()
			{
				down = true;
				guest.Link.DropSocket();
				await Until( () => guest.Link.Stopped == CoopStop.Lost, "the link lost again", guest );
			}
			async Task Retry()
			{
				lobby.Step( true, LoginOf( guest ), false );
				now += 2 * CoopLobbyLink.LostRetrySeconds;
				lobby.Step( true, LoginOf( guest ), false );
				await Until( () => guest.Link.State == CoopLinkState.Open, "the retry to open", guest );
				lobby.Step( true, LoginOf( guest ), false );
			}
			await Lose();
			down = false;
			await Retry();
			Assert.AreEqual( 1, lobby.LostRetries );
			now += CoopLobbyLink.StableOpenSeconds - 1;
			lobby.Step( true, LoginOf( guest ), false );
			await Lose();
			down = false;
			await Retry();
			Assert.AreEqual( 2, lobby.LostRetries, "a brief open did not start over" );
			// one that stays up a minute does
			now += CoopLobbyLink.StableOpenSeconds;
			lobby.Step( true, LoginOf( guest ), false );
			Assert.AreEqual( 0, lobby.LostRetries );
		} );
	}

	[TestMethod]
	public void TheReconnectWaitIsSpreadOverEightyToOneHundredAndTwentyPerCent()
	{
		var low = TimeSpan.MaxValue;
		var high = TimeSpan.Zero;
		for ( var sample = 0; sample < 2000; sample++ )
		{
			var wait = CoopLink.DefaultJitter( TimeSpan.FromSeconds( 10 ) );
			Assert.IsTrue( wait >= TimeSpan.FromSeconds( 8 ) && wait <= TimeSpan.FromSeconds( 12 ), wait.ToString() );
			low = wait < low ? wait : low;
			high = wait > high ? wait : high;
		}
		Assert.IsTrue( low < TimeSpan.FromSeconds( 8.5 ) && high > TimeSpan.FromSeconds( 11.5 ), $"spread {low} to {high}" );
	}

	[TestMethod]
	public async Task EveryReconnectWaitGoesThroughTheSpread()
	{
		await WithServer( async ( server, players ) =>
		{
			var guest = await players.Add( server, "Guest" );
			var waits = new List<TimeSpan>();
			guest.Link.RetryDelays = new[] { TimeSpan.FromMilliseconds( 30 ), TimeSpan.FromMilliseconds( 60 ) };
			guest.Link.Jitter = wait =>
			{
				waits.Add( wait );
				return wait * 2;
			};
			await Open( guest );
			guest.Link.DropSocket();
			await Until( () => waits.Count > 0 && guest.Link.State == CoopLinkState.Open, "the link back", guest );
			Assert.AreEqual( TimeSpan.FromMilliseconds( 30 ), waits[0] );
		} );
	}
}
