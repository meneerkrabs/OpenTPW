using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Client;
using OpenTPW.Online.Coop;
using OpenTPW.Server;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-session] The game's co-op link (<see cref="CoopLink"/>, docs/ONLINE.md, "Shared parks") under pumped <see cref="OnlineSession"/>s against a
/// loopback server, with the desktop's header login and the browser's first-message login: a session created, listed, joined and shared (frames and
/// messages), invitations and kicks, a resume after a lost socket, after a server restart and after the silence watchdog, two games of one account, and a
/// game that no longer holds the session the server names.
/// </summary>
[TestClass]
public class CoopLinkTests
{
	private const string Build = "1.4.0+0123456789abcdef0123456789abcdef01234567";
	private static readonly CoopMod[] Mods = { new( "opentpw.pools", "1.0.0", new string( 'a', 64 ) ) };

	/// <summary>A player's online session and everything its link raised.</summary>
	internal sealed class Player : IDisposable
	{
		public Player( OnlineSession session, string name )
		{
			Session = session;
			Name = name;
			Link.SessionStarted += Started.Add;
			Link.Resumed += Resumes.Add;
			Link.MemberChanged += Changes.Add;
			Link.SessionEnded += Ended.Add;
			Link.FrameReceived += Frames.Add;
			Link.MessageReceived += Messages.Add;
			Link.Invited += Invitations.Add;
			Link.Refused += Errors.Add;
			Link.FrameRefused += Refusals.Add;
			Link.StateChanged += States.Add;
		}

		public OnlineSession Session { get; }
		public string Name { get; }
		public CoopLink Link => Session.Coop;
		public List<CoopSessionState> Started { get; } = new();
		public List<CoopSessionState> Resumes { get; } = new();
		public List<CoopMemberChanged> Changes { get; } = new();
		public List<CoopEnded> Ended { get; } = new();
		public List<CoopFrame> Frames { get; } = new();
		public List<CoopRelayed> Messages { get; } = new();
		public List<CoopInvited> Invitations { get; } = new();
		public List<CoopError> Errors { get; } = new();
		public List<CoopFrameRefused> Refusals { get; } = new();
		public List<CoopLinkState> States { get; } = new();

		public void Dispose() => Session.Dispose();
	}

	internal sealed class Players : IDisposable
	{
		private readonly List<Player> all = new();

		/// <summary>A logged-in session whose link names the test build and mods, with quick retries.</summary>
		public async Task<Player> Add( LoopbackServer server, string name, bool firstFrame = false, bool register = true, IReadOnlyList<CoopMod>? mods = null )
		{
			var client = await server.LoginAsync( name, register: register );
			client.AuthenticateInFirstFrame = firstFrame;
			var session = new OnlineSession( OnlineIntegrationTests.Folders() );
			session.UseClient( client, name );
			session.Coop.Identity = () => (Build, mods ?? Mods);
			session.Coop.RetryDelays = new[] { TimeSpan.FromMilliseconds( 50 ) };
			var player = new Player( session, name );
			all.Add( player );
			return player;
		}

		public void Dispose()
		{
			foreach ( var player in all )
				player.Dispose();
		}
	}

	internal static Task<LoopbackServer> Start( Action<ServerOptions>? configure = null, string? directory = null, Uri? url = null ) => LoopbackServer.StartAsync( options =>
	{
		options.AuthenticationsPerMinute = 1000;
		configure?.Invoke( options );
	}, directory, url );

	/// <summary>
	/// Runs <paramref name="test"/> with a loopback server and its players, then closes the players' links, stops the server and deletes its data, in that
	/// order (a server still at work would write into a folder being deleted).
	/// </summary>
	internal static async Task WithServer( Func<LoopbackServer, Players, Task> test, Action<ServerOptions>? configure = null )
	{
		var server = await Start( configure );
		var players = new Players();
		try
		{
			await test( server, players );
		}
		finally
		{
			players.Dispose();
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	/// <summary>Pumps every player until <paramref name="condition"/> holds; fails after <see cref="OnlineIntegrationTests.Deadline"/>.</summary>
	internal static async Task Until( Func<bool> condition, string what, params Player[] players )
	{
		var until = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
		while ( true )
		{
			foreach ( var player in players )
				player.Session.Pump();
			if ( condition() )
				return;
			if ( DateTime.UtcNow > until )
				Assert.Fail( $"Waited in vain for {what}: " + string.Join( "; ", players.Select( player =>
					$"{player.Name} {player.Link.State} stopped {player.Link.Stopped}, errors {string.Join( ", ", player.Errors )}" ) ) );
			await Task.Delay( 10 );
		}
	}

	internal static async Task Open( params Player[] players )
	{
		foreach ( var player in players )
			player.Link.Open();
		await Until( () => players.All( player => player.Link.State == CoopLinkState.Open ), "the links to open", players );
	}

	/// <summary>The host creates a session, and every guest joins it with its code.</summary>
	internal static async Task<CoopSessionState> Share( Player host, params Player[] guests )
	{
		Assert.IsTrue( host.Link.Create( listed: false, "Our park", 4 ) );
		await Until( () => host.Link.Session != null, "the session", host );
		var session = host.Link.Session!;
		foreach ( var guest in guests )
		{
			Assert.IsTrue( guest.Link.Join( session.Code ) );
			await Until( () => guest.Link.Session != null, $"{guest.Name} to join", guest );
		}
		await Until( () => host.Link.Session!.Members.Count == 1 + guests.Length, "the host to see every guest", host );
		return host.Link.Session!;
	}

	private static byte[] Payload( int value ) => BitConverter.GetBytes( value );

	private static int Value( ReadOnlyMemory<byte> payload ) => BitConverter.ToInt32( payload.Span );

	/// <summary>
	/// [EXT:online-coop-session] The chat follows the shared park (C2d-1): a member's chat enters the session's room once it hosts or joins, and is back in the
	/// lobby once the session is over for it, whether it left or the session ended.
	/// </summary>
	[TestMethod]
	public async Task TheChatFollowsItsSharedPark() => await WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var guest = await players.Add( server, "Guest" );
		host.Session.ConnectChat();
		guest.Session.ConnectChat();
		await Until( () => host.Session.Chat != null && guest.Session.Chat != null && !host.Session.ChatBusy && !guest.Session.ChatBusy, "both chats", host, guest );
		await Open( host, guest );
		var shared = await Share( host, guest );
		var room = OpenTPW.Online.Chat.ChatProtocol.CoopRoom( shared.Id );
		var lobby = OpenTPW.Online.Chat.ChatProtocol.LobbyRoom;
		await Until( () => host.Session.CurrentRoom == room && guest.Session.CurrentRoom == room && host.Session.RoomPlayers.Count == 2, "both chats in the session's room", host, guest );
		CollectionAssert.AreEquivalent( new[] { "Hostess", "Guest" }, host.Session.RoomPlayers.ToArray() );

		// the guest's own leave takes its chat back to the lobby, and the host's roster is its own again
		Assert.IsTrue( guest.Link.Leave( "Off home." ) );
		await Until( () => guest.Session.CurrentRoom == lobby && host.Session.RoomPlayers.Count == 1, "the guest's chat back in the lobby", host, guest );
		// joining again brings it back; the host's leave ends the session, and both chats are in the lobby
		Assert.IsTrue( guest.Link.Join( shared.Code ) );
		await Until( () => guest.Session.CurrentRoom == room && host.Session.RoomPlayers.Count == 2, "the guest's chat in the room again", host, guest );
		Assert.IsTrue( host.Link.Leave( "Closing up." ) );
		await Until( () => host.Session.CurrentRoom == lobby && guest.Session.CurrentRoom == lobby && guest.Ended.Count == 1, "both chats in the lobby", host, guest );
		Assert.AreEqual( CoopReason.HostLeft, guest.Ended.Single().Reason );
	} );

	[TestMethod]
	public async Task AHostAndAGuestShareASessionThroughTheirLinks()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			// the guest logs in as a browser does: its token is the socket's first message
			var guest = await players.Add( server, "Guest", firstFrame: true );
			await Open( host, guest );
			Assert.AreEqual( CoopProtocol.Version, host.Link.ServerProtocol );

			Assert.IsTrue( host.Link.Create( listed: true, "Our park", 3 ) );
			await Until( () => host.Link.Session != null, "the session", host );
			var created = host.Link.Session!;
			Assert.IsTrue( host.Link.IsHost );
			Assert.AreEqual( 1, host.Started.Count );
			Assert.AreEqual( 3, created.MaximumPlayers );

			IReadOnlyList<Online.Api.CoopListedSession>? listed = null;
			guest.Link.List( ( sessions, error ) => listed = sessions ?? throw new AssertFailedException( error ) );
			await Until( () => listed != null, "the open list", guest );
			Assert.AreEqual( "Hostess", listed!.Single( entry => entry.Code == created.Code ).Host );
			guest.Link.Identity = () => ("2.0.0+other", Mods);
			listed = null;
			guest.Link.List( ( sessions, error ) => listed = sessions ?? throw new AssertFailedException( error ) );
			await Until( () => listed != null, "the open list of another build", guest );
			Assert.AreEqual( 0, listed!.Count, "a game sees only the sessions of its own build" );
			guest.Link.Identity = () => (Build, Mods);

			// typed as a player might: lower case and a dash
			Assert.IsTrue( guest.Link.Join( created.Code.ToLowerInvariant().Insert( 3, "-" ) ) );
			await Until( () => guest.Link.Session != null && host.Link.Session!.Members.Count == 2, "the join", host, guest );
			Assert.IsFalse( guest.Link.IsHost );
			Assert.AreEqual( Build, guest.Link.HostBuild );
			CollectionAssert.AreEqual( Mods, guest.Link.HostMods!.ToArray() );
			Assert.AreEqual( CoopMemberState.Joined, host.Changes.Single().State );
			var guestId = guest.Link.Session!.You;
			var hostId = guest.Link.Session.Host;

			for ( var seq = 1; seq <= 3; seq++ )
				Assert.IsTrue( host.Link.SendFrame( (ulong)seq, Payload( seq * 10 ) ) );
			Assert.IsFalse( guest.Link.SendFrame( 1, Payload( 0 ) ), "only the host sends frames" );
			// a frame out of turn is dropped and the host told which one comes next
			Assert.IsTrue( host.Link.SendFrame( 5, Payload( 50 ) ) );
			await Until( () => host.Refusals.Count == 1, "the refused frame", host );
			Assert.AreEqual( 4UL, host.Refusals[0].Expected );
			await Until( () => guest.Frames.Count == 3, "the frames", guest );
			CollectionAssert.AreEqual( new ulong[] { 1, 2, 3 }, guest.Frames.Select( frame => frame.Seq ).ToArray() );
			CollectionAssert.AreEqual( new[] { 10, 20, 30 }, guest.Frames.Select( frame => Value( frame.Payload ) ).ToArray() );
			Assert.AreEqual( 3UL, guest.Link.LastFrameSeq );

			// [EXT:online-coop-snapshot-join] the host's skip: the frames go on at the number it names, and a skip back is answered with the frame the server takes next
			Assert.IsTrue( host.Link.CanSkipFrames );
			Assert.IsFalse( guest.Link.SkipFrames( 10 ), "only the host skips frames" );
			Assert.IsTrue( host.Link.SkipFrames( 10 ) );
			Assert.IsTrue( host.Link.SendFrame( 10, Payload( 100 ) ) );
			await Until( () => guest.Frames.Count == 4, "the frame after the skip", guest );
			Assert.AreEqual( (10UL, 100), (guest.Frames[3].Seq, Value( guest.Frames[3].Payload )) );
			Assert.IsTrue( host.Link.SkipFrames( 6 ) );
			await Until( () => host.Refusals.Count == 2, "the refused skip", host );
			Assert.AreEqual( 11UL, host.Refusals[1].Expected );

			Assert.IsTrue( guest.Link.Send( hostId, Payload( 7 ) ) );
			Assert.IsTrue( host.Link.Send( CoopProtocol.EveryOtherMember, Payload( 8 ) ) );
			await Until( () => host.Messages.Count == 1 && guest.Messages.Count == 1, "the messages", host, guest );
			Assert.AreEqual( (guestId, 7), (host.Messages[0].From, Value( host.Messages[0].Payload )) );
			Assert.AreEqual( (hostId, 8), (guest.Messages[0].From, Value( guest.Messages[0].Payload )) );

			Assert.IsTrue( guest.Link.Leave( "Time for tea." ) );
			Assert.IsNull( guest.Link.Session, "a leave is at once" );
			await Until( () => host.Link.Session!.Members.Count == 1, "the host to see the guest leave", host, guest );
			Assert.AreEqual( CoopMemberState.Left, host.Changes.Last().State );
			Assert.AreEqual( 0, guest.Ended.Count, "the leaver's own leave is no ended session" );
		} );
	}

	[TestMethod]
	public async Task AWelcomeOfAnOlderProtocolStopsTheLinkAsAServerTooOld()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			await Open( host );
			Assert.IsTrue( host.Link.Create( listed: false, "Our park", 2 ) );
			await Until( () => host.Link.Session != null, "the session", host );
			// a server of protocol 6 takes this game's hello but not the host's skip (7): its welcome stops the link as a server too old, and the session the link held ends
			typeof( CoopLink ).GetMethod( "Welcomed", BindingFlags.Instance | BindingFlags.NonPublic )!
				.Invoke( host.Link, new object[] { new CoopWelcome( (ushort)(CoopProtocol.Version - 1), null, null ) } );
			Assert.AreEqual( (CoopStop.ServerTooOld, CoopLinkState.Closed), (host.Link.Stopped, host.Link.State) );
			Assert.IsNull( host.Link.Session );
			Assert.AreEqual( "The server is too old to share parks with this game.", host.Ended.Single().Text );
		} );
	}

	[TestMethod]
	public async Task InvitationsKicksAndRefusalsReachTheLink()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await Open( host, guest );
			Assert.IsTrue( host.Link.Create( listed: false, "Our park", 2 ) );
			await Until( () => host.Link.Session != null, "the session", host );
			Assert.IsFalse( host.Link.Create( listed: false, "Another park", 2 ), "one session at a time" );
			Assert.IsFalse( guest.Link.Join( "ABCD0O" ), "what cannot be a code is not sent" );

			Assert.IsTrue( guest.Link.Join( "BBBBBB" ) );
			await Until( () => guest.Errors.Count == 1, "the refusal", guest );
			Assert.AreEqual( (CoopMessageType.Join, CoopReason.UnknownCode), (guest.Errors[0].Request, guest.Errors[0].Reason) );

			Assert.IsTrue( host.Link.Invite( "Guest" ) );
			await Until( () => guest.Invitations.Count == 1, "the invitation", guest );
			Assert.AreEqual( (host.Link.Session!.Code, "Hostess"), (guest.Invitations[0].Code, guest.Invitations[0].Host) );
			Assert.IsTrue( guest.Link.Join( guest.Invitations[0].Code ) );
			await Until( () => guest.Link.Session != null && host.Link.Session!.Members.Count == 2, "the join", host, guest );

			Assert.IsFalse( guest.Link.Kick( host.Link.Session!.Host ), "only the host removes players" );
			Assert.IsTrue( host.Link.Kick( guest.Link.Session!.You ) );
			await Until( () => guest.Ended.Count == 1 && host.Link.Session!.Members.Count == 1, "the kick", host, guest );
			Assert.AreEqual( CoopReason.Kicked, guest.Ended[0].Reason );
			Assert.IsNull( guest.Link.Session );
			Assert.AreEqual( CoopMemberState.Kicked, host.Changes.Last().State );
			Assert.IsTrue( guest.Link.Join( host.Link.Session!.Code ) );
			await Until( () => guest.Errors.Count == 2, "the second refusal", guest );
			Assert.AreEqual( CoopReason.Kicked, guest.Errors[1].Reason );

			Assert.IsTrue( host.Link.Leave( "Closing time." ) );
			Assert.IsNull( host.Link.Session );
		} );
	}

	[TestMethod]
	public async Task ALostSocketResumesTheSessionAndTheHostResendsAfterTheServersFrame()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest", firstFrame: true );
			await Open( host, guest );
			var session = await Share( host, guest );
			host.Link.SendFrame( 1, Payload( 1 ) );
			host.Link.SendFrame( 2, Payload( 2 ) );
			await Until( () => guest.Frames.Count == 2, "the first frames", guest );

			host.Link.DropSocket();
			Assert.IsFalse( host.Link.SendFrame( 3, Payload( 3 ) ), "a frame the lost socket cannot take is the host's to resend" );
			Assert.AreEqual( 2UL, host.Link.LastFrameSeq, "a frame that did not go out is not the host's last" );
			await Until( () => host.Resumes.Count == 1 && host.Link.State == CoopLinkState.Open, "the host's resume", host, guest );
			Assert.AreEqual( 2, host.Link.Connections );
			Assert.AreEqual( session.Id, host.Link.Session!.Id );
			Assert.AreEqual( 2UL, host.Resumes[0].LastFrameSeq, "the server's last frame: the host resends after it" );
			Assert.AreEqual( 2UL, host.Link.LastFrameSeq );
			await Until( () => guest.Changes.Any( change => change.State == CoopMemberState.Connected ), "the guest to see the host back", host, guest );
			Assert.IsTrue( guest.Changes.Any( change => change.State == CoopMemberState.Disconnected ) );
			Assert.IsTrue( host.Link.SendFrame( 3, Payload( 3 ) ) );
			await Until( () => guest.Frames.Count == 3, "the resent frame", host, guest );

			guest.Link.DropSocket();
			await Until( () => guest.Resumes.Count == 1 && guest.Link.State == CoopLinkState.Open, "the guest's resume", host, guest );
			Assert.AreEqual( 3UL, guest.Link.LastFrameSeq );
			Assert.IsTrue( host.Link.SendFrame( 4, Payload( 4 ) ) );
			await Until( () => guest.Frames.Count == 4, "a frame after the guest's resume", host, guest );
			CollectionAssert.AreEqual( new ulong[] { 1, 2, 3, 4 }, guest.Frames.Select( frame => frame.Seq ).ToArray() );
			Assert.AreEqual( 0, host.Ended.Count + guest.Ended.Count );
		} );
	}

	[TestMethod]
	public async Task ARestartResumesEveryLinkAndTheHostGoesOnFromItsLastFrame()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-server-{Guid.NewGuid():N}" );
		using var players = new Players();
		LoopbackServer? server = await Start( directory: directory );
		try
		{
			var url = server.Url;
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await Open( host, guest );
			await Share( host, guest );
			for ( var seq = 1; seq <= 3; seq++ )
				host.Link.SendFrame( (ulong)seq, Payload( seq ) );
			await Until( () => guest.Frames.Count == 3, "the frames", guest );

			await server.DisposeAsync();
			server = null;
			await Until( () => host.Link.State == CoopLinkState.Reconnecting && guest.Link.State == CoopLinkState.Reconnecting, "the links to lose the server", host, guest );
			server = await Start( directory: directory, url: url );
			await Until( () => host.Resumes.Count == 1 && guest.Resumes.Count == 1, "both resumes", host, guest );
			Assert.AreEqual( 3UL, host.Resumes[0].LastFrameSeq, "the server kept the frame position over the restart" );
			Assert.IsTrue( host.Link.SendFrame( 4, Payload( 4 ) ) );
			await Until( () => guest.Frames.Count == 4, "the frame after the restart", host, guest );
			Assert.AreEqual( 4, Value( guest.Frames[3].Payload ) );
		}
		finally
		{
			if ( server != null )
				await server.DisposeAsync();
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public async Task TheSilenceWatchdogDropsAQuietSocketAndTheLinkResumes()
	{
		// the server never pings within the test, so only the watchdog notices the quiet
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			host.Link.Silence = TimeSpan.FromSeconds( 3 );
			await Open( host );
			Assert.IsTrue( host.Link.Create( listed: false, "Our park", 2 ) );
			await Until( () => host.Link.Session != null, "the session", host );
			await Until( () => host.Resumes.Count >= 1 && host.Link.State == CoopLinkState.Open, "a resume after the quiet", host );
			Assert.IsTrue( host.Link.Connections >= 2 );
			Assert.IsNotNull( host.Link.Session, "the session stays" );
		}, options => options.CoopPingInterval = TimeSpan.FromMinutes( 5 ) );
	}

	[TestMethod]
	public async Task AnotherGameOfTheAccountIsRefusedUntilItTakesOver()
	{
		await WithServer( async ( server, players ) =>
		{
			var first = await players.Add( server, "Twin" );
			var second = await players.Add( server, "Twin", register: false );
			var guest = await players.Add( server, "Guest" );
			await Open( first, guest );
			await Share( first, guest );

			second.Link.Open();
			await Until( () => second.Link.Stopped == CoopStop.InUseElsewhere, "the refusal", first, second );
			Assert.AreEqual( CoopLinkState.Closed, second.Link.State );
			Assert.AreEqual( (CoopMessageType.Hello, CoopReason.InUse), (second.Errors.Single().Request, second.Errors.Single().Reason) );
			Assert.AreEqual( CoopLinkState.Open, first.Link.State, "the first game keeps its socket" );

			second.Link.Open( takeOver: true );
			await Until( () => second.Link.State == CoopLinkState.Open && first.Link.Stopped == CoopStop.Replaced, "the take-over", first, second );
			Assert.AreEqual( CoopLinkState.Closed, first.Link.State );
			Assert.AreEqual( 1, first.Ended.Count, "the replaced game's session is over for it" );
			Assert.IsNull( first.Link.Session );
			// the game that took over does not hold the park, so it leaves the session the server names, and the host's leave ends it
			Assert.IsNull( second.Link.Session );
			await Until( () => guest.Ended.Count == 1, "the session to end", second, guest );
			Assert.AreEqual( CoopReason.HostLeft, guest.Ended[0].Reason );
		} );
	}

	[TestMethod]
	public async Task AGameThatNoLongerHoldsItsSessionLeavesIt()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await Open( host, guest );
			await Share( host, guest );
			// the guest's game restarts: a new process, nothing held
			guest.Link.Close();
			Assert.IsNull( guest.Link.Session );
			Assert.AreEqual( 1, guest.Ended.Count, "closing the link ends the session for this game" );
			await Until( () => host.Changes.Any( change => change.State == CoopMemberState.Disconnected ), "the host to see the guest go", host );
			guest.Link.Instance = Guid.NewGuid();
			await Open( guest );
			await Until( () => host.Link.Session!.Members.Count == 1, "the restarted guest to leave", host, guest );
			Assert.AreEqual( CoopMemberState.Left, host.Changes.Last().State );
			Assert.IsNull( guest.Link.Session );
			Assert.AreEqual( 1, guest.Ended.Count, "the leave of a session the game no longer holds ends nothing more" );
		} );
	}

	[TestMethod]
	public async Task LoggingOutClosesTheLink()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			await Open( host );
			Assert.IsTrue( host.Link.Create( listed: false, "Our park", 2 ) );
			await Until( () => host.Link.Session != null, "the session", host );
			host.Session.Disconnect();
			Assert.AreEqual( CoopLinkState.Closed, host.Link.State );
			Assert.IsNull( host.Link.Session );
			Assert.AreEqual( 1, host.Ended.Count, "a logout ends the session for this game" );
			Assert.AreEqual( CoopStop.None, host.Link.Stopped );
			host.Link.Open();
			Assert.AreEqual( CoopLinkState.Closed, host.Link.State );
			host.Session.Pump();
			Assert.AreEqual( CoopStop.Closed, host.Link.Stopped, "no login, no link" );
		} );
	}

	[TestMethod]
	public async Task ALongModVersionStillSaysHello()
	{
		await WithServer( async ( server, players ) =>
		{
			// a version as long as a manifest may write it
			var mods = new[] { new CoopMod( "community.nightly", "1.0.0-nightly.2026.10.21.build.1234567+" + new string( 'a', 25 ), new string( 'e', 64 ) ) };
			Assert.AreEqual( CoopProtocol.MaximumModVersionLength, mods[0].Version.Length );
			var host = await players.Add( server, "Hostess", mods: mods );
			var guest = await players.Add( server, "Guest", mods: mods );
			await Open( host, guest );
			await Share( host, guest );
			CollectionAssert.AreEqual( mods, guest.Link.HostMods!.ToArray() );
		} );
	}

	[TestMethod]
	public async Task AJoinLostWithItsSocketIsRefusedAndItsSessionLeft()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await Open( host, guest );
			Assert.IsTrue( host.Link.Create( listed: false, "Our park", 4 ) );
			await Until( () => host.Link.Session != null, "the session", host );
			var code = host.Link.Session!.Code;
			Assert.IsTrue( guest.Link.Join( code ) );
			Assert.IsFalse( guest.Link.Join( code ), "one create or join waits at a time" );
			// the socket goes before the answer can come: the game hears that, and whatever the server made of the join is left
			guest.Link.DropSocket();
			await Until( () => guest.Errors.Any( error => error.Request == CoopMessageType.Join && error.Reason == CoopReason.Unknown ) && guest.Link.State == CoopLinkState.Open,
				"the lost join's refusal and the resume", host, guest );
			await Until( () => host.Link.Session!.Members.Count == 1, "no join to stay behind", host, guest );
			Assert.IsNull( guest.Link.Session );
			Assert.AreEqual( 0, guest.Started.Count );
			Assert.IsTrue( guest.Link.Join( code ), "the game asks again" );
			await Until( () => guest.Link.Session != null && host.Link.Session!.Members.Count == 2, "the second join", host, guest );
		} );
	}

	[TestMethod]
	public async Task AConnectThatNeverOpensIsGivenUpAndTriedAgain()
	{
		await WithServer( async ( server, players ) =>
		{
			var guest = await players.Add( server, "Guest" );
			guest.Link.ConnectTimeout = TimeSpan.FromMilliseconds( 300 );
			var attempts = 0;
			var real = guest.Link.Connector;
			// the first upgrade never answers
			guest.Link.Connector = async ( client, cancel ) =>
			{
				if ( Interlocked.Increment( ref attempts ) == 1 )
					await Task.Delay( Timeout.Infinite, cancel );
				// the real attempt is not timed: a slow machine would otherwise give up on it too and count a third
				return await real( client, CancellationToken.None );
			};
			await Open( guest );
			Assert.AreEqual( 2, attempts );
			Assert.AreEqual( 1, guest.Link.Connections );
		} );
	}

	[TestMethod]
	public async Task ALogoutWhileASocketWaitsForThePumpLeavesTheLinkClosed()
	{
		await WithServer( async ( server, players ) =>
		{
			var guest = await players.Add( server, "Guest" );
			var real = guest.Link.Connector;
			var connected = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
			guest.Link.Connector = async ( client, cancel ) =>
			{
				var connection = await real( client, cancel );
				connected.TrySetResult();
				return connection;
			};
			guest.Link.Open();
			await connected.Task;
			// the socket's install now waits for a pump that does not come before the logout
			await Task.Delay( 250 );
			guest.Session.Disconnect();
			Assert.AreEqual( CoopLinkState.Closed, guest.Link.State );
			Assert.AreEqual( 0, guest.Link.Connections );
			// the link is not stuck: a new login opens it again
			guest.Session.UseClient( await server.LoginAsync( "Guest", register: false ), "Guest" );
			await Open( guest );
			Assert.AreEqual( 1, guest.Link.Connections );
		} );
	}

	[TestMethod]
	public async Task AnAbandonedAttemptNeverReplacesTheNewerSocket()
	{
		await WithServer( async ( server, players ) =>
		{
			var guest = await players.Add( server, "Guest" );
			var attempts = 0;
			var real = guest.Link.Connector;
			var late = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
			// the first upgrade answers only after the link has moved on
			guest.Link.Connector = async ( client, cancel ) =>
			{
				if ( Interlocked.Increment( ref attempts ) == 1 )
				{
					await Task.Delay( 300, CancellationToken.None );
					var connection = await real( client, CancellationToken.None );
					late.TrySetResult();
					return connection;
				}
				return await real( client, cancel );
			};
			guest.Link.Open();
			guest.Link.Close();
			await Open( guest );
			await late.Task;
			// the late socket would have replaced the live one with its hello; it closes without a word instead
			var until = DateTime.UtcNow + TimeSpan.FromSeconds( 1 );
			while ( DateTime.UtcNow < until )
			{
				guest.Session.Pump();
				await Task.Delay( 10 );
			}
			Assert.AreEqual( CoopLinkState.Open, guest.Link.State );
			Assert.AreEqual( 1, guest.Link.Connections );
			Assert.IsFalse( guest.States.Contains( CoopLinkState.Reconnecting ) );
		} );
	}

	[TestMethod]
	public async Task TheWelcomeLeavesTheOldSessionBeforeTheLinkSaysItIsOpen()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await Open( host, guest );
			await Share( host, guest );
			guest.Link.Close();
			guest.Link.Instance = Guid.NewGuid();
			var created = false;
			guest.Link.StateChanged += state =>
			{
				if ( state == CoopLinkState.Open )
					created = guest.Link.Create( listed: false, "My own park", 2 );
			};
			await Open( guest );
			Assert.IsTrue( created );
			await Until( () => guest.Link.Session != null, "the guest's own session", host, guest );
			Assert.AreEqual( guest.Link.Session!.You, guest.Link.Session.Host );
			Assert.IsFalse( guest.Errors.Any( error => error.Reason == CoopReason.InSession ), "the old session was left first" );
		} );
	}

	[TestMethod]
	public async Task ASocketThatKeepsDroppingSoonAfterItsWelcomeBacksOffAndStops()
	{
		await WithServer( async ( server, players ) =>
		{
			var guest = await players.Add( server, "Guest" );
			guest.Link.RetryDelays = new[] { TimeSpan.FromMilliseconds( 100 ), TimeSpan.FromMilliseconds( 400 ), TimeSpan.FromMilliseconds( 1600 ) };
			guest.Link.ReconnectWindow = TimeSpan.FromSeconds( 2 );
			var real = guest.Link.Connector;
			// every socket is cut 150 ms after it opens: each one is welcomed, none stays up
			guest.Link.Connector = async ( client, cancel ) =>
			{
				var connection = await real( client, cancel );
				_ = Task.Delay( 150 ).ContinueWith( _ => connection.Abort(), TaskScheduler.Default );
				return connection;
			};
			guest.Link.Open();
			await Until( () => guest.Link.Stopped == CoopStop.Lost, "the link to give up", guest );
			Assert.IsTrue( guest.Link.Connections is >= 2 and <= 6, $"{guest.Link.Connections} connections" );
		} );
	}

	[TestMethod]
	public async Task ATakeOverSurvivesAFailedFirstAttempt()
	{
		await WithServer( async ( server, players ) =>
		{
			var first = await players.Add( server, "Twin" );
			var second = await players.Add( server, "Twin", register: false );
			await Open( first );
			var attempts = 0;
			var real = second.Link.Connector;
			second.Link.Connector = async ( client, cancel ) =>
			{
				if ( Interlocked.Increment( ref attempts ) == 1 )
					throw new WebSocketException( "The network is down." );
				return await real( client, cancel );
			};
			second.Link.Open( takeOver: true );
			await Until( () => second.Link.State == CoopLinkState.Open && first.Link.Stopped == CoopStop.Replaced, "the take-over", first, second );
			Assert.AreEqual( 2, attempts );
		} );
	}

	[TestMethod]
	public async Task TwoLinksOfOneAccountInOneGameDoNotTakeTurns()
	{
		await WithServer( async ( server, players ) =>
		{
			// two online sessions of one process (the game has one, the park panel another) each get their own id
			var first = await players.Add( server, "Twin" );
			var second = await players.Add( server, "Twin", register: false );
			Assert.AreNotEqual( first.Link.Instance, second.Link.Instance );
			await Open( first );
			second.Link.Open();
			await Until( () => second.Link.Stopped == CoopStop.InUseElsewhere, "the second link's refusal", first, second );
			var until = DateTime.UtcNow + TimeSpan.FromMilliseconds( 500 );
			while ( DateTime.UtcNow < until )
			{
				first.Session.Pump();
				second.Session.Pump();
				await Task.Delay( 10 );
			}
			Assert.AreEqual( CoopLinkState.Open, first.Link.State );
			Assert.AreEqual( 1, first.Link.Connections, "the first link kept its socket" );
		} );
	}

	[TestMethod]
	public async Task ALeaveWhileReconnectingKeepsItsReason()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await Open( host, guest );
			await Share( host, guest );
			host.Link.DropSocket();
			Assert.IsTrue( host.Link.Leave( "Closing time." ) );
			Assert.IsNull( host.Link.Session );
			await Until( () => guest.Ended.Count == 1, "the guest to hear the end", host, guest );
			Assert.AreEqual( (CoopReason.HostLeft, "Closing time."), (guest.Ended[0].Reason, guest.Ended[0].Text) );
		} );
	}

	[TestMethod]
	public async Task WhatCannotBeSentIsNoRequest()
	{
		await WithServer( async ( server, players ) =>
		{
			var host = await players.Add( server, "Hostess" );
			var guest = await players.Add( server, "Guest" );
			await Open( host, guest );
			Assert.IsFalse( host.Link.Create( listed: false, new string( 'a', CoopProtocol.MaximumTitleLength + 1 ), 2 ) );
			Assert.IsFalse( host.Link.Create( listed: false, "Our\tpark", 2 ) );
			Assert.IsFalse( host.Link.Create( listed: false, "Our park", 5 ) );
			await Share( host, guest );
			Assert.IsFalse( host.Link.Invite( new string( 'a', CoopProtocol.MaximumInviteNameLength + 1 ) ) );
			Assert.IsFalse( host.Link.Invite( "  " ) );
			Assert.IsFalse( host.Link.SendFrame( 1, new byte[CoopLink.MaximumFramePayload + 1] ) );
			Assert.IsFalse( host.Link.SendFrame( ulong.MaxValue, Payload( 1 ) ) );
			Assert.AreEqual( 0UL, host.Link.LastFrameSeq );
			Assert.IsTrue( host.Link.SendFrame( 1, new byte[CoopLink.MaximumFramePayload] ), "the largest frame goes" );
			Assert.IsFalse( host.Link.Send( CoopProtocol.EveryOtherMember, new byte[CoopLink.MaximumMessagePayload + 1] ) );
			await Until( () => guest.Frames.Count == 1, "the largest frame", host, guest );
			// a long reason is cut to what a message carries
			Assert.IsTrue( host.Link.Leave( new string( 'r', 300 ) ) );
			await Until( () => guest.Ended.Count == 1, "the end", host, guest );
			Assert.AreEqual( CoopProtocol.MaximumReasonLength, guest.Ended[0].Text.Length );
		} );
	}

	[TestMethod]
	public async Task ACloseFromTheOpeningHandlerStopsTheAttempt()
	{
		await WithServer( async ( server, players ) =>
		{
			var guest = await players.Add( server, "Guest" );
			guest.Link.StateChanged += state =>
			{
				if ( state == CoopLinkState.Opening )
					guest.Link.Close();
			};
			guest.Link.Open();
			var until = DateTime.UtcNow + TimeSpan.FromMilliseconds( 500 );
			while ( DateTime.UtcNow < until )
			{
				guest.Session.Pump();
				await Task.Delay( 10 );
			}
			Assert.AreEqual( CoopLinkState.Closed, guest.Link.State );
			Assert.AreEqual( 0, guest.Link.Connections );
		} );
	}
}
