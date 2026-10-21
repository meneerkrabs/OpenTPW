using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
	private sealed class Player : IDisposable
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

		public void Dispose() => Session.Dispose();
	}

	private sealed class Players : IDisposable
	{
		private readonly List<Player> all = new();

		/// <summary>A logged-in session whose link names the test build and mods, with its own process id and quick retries.</summary>
		public async Task<Player> Add( LoopbackServer server, string name, bool firstFrame = false, bool register = true )
		{
			var client = await server.LoginAsync( name, register: register );
			client.AuthenticateInFirstFrame = firstFrame;
			var session = new OnlineSession( OnlineIntegrationTests.Folders() );
			session.UseClient( client, name );
			session.Coop.Identity = () => (Build, Mods);
			session.Coop.Instance = Guid.NewGuid();
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

	private static Task<LoopbackServer> Start( Action<ServerOptions>? configure = null, string? directory = null, Uri? url = null ) => LoopbackServer.StartAsync( options =>
	{
		options.AuthenticationsPerMinute = 1000;
		configure?.Invoke( options );
	}, directory, url );

	/// <summary>
	/// Runs <paramref name="test"/> with a loopback server and its players, then closes the players' links, stops the server and deletes its data, in that
	/// order (a server still at work would write into a folder being deleted).
	/// </summary>
	private static async Task WithServer( Func<LoopbackServer, Players, Task> test, Action<ServerOptions>? configure = null )
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
	private static async Task Until( Func<bool> condition, string what, params Player[] players )
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

	private static async Task Open( params Player[] players )
	{
		foreach ( var player in players )
			player.Link.Open();
		await Until( () => players.All( player => player.Link.State == CoopLinkState.Open ), "the links to open", players );
	}

	/// <summary>The host creates a session, and every guest joins it with its code.</summary>
	private static async Task<CoopSessionState> Share( Player host, params Player[] guests )
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
			await Until( () => host.Resumes.Count == 1 && host.Link.State == CoopLinkState.Open, "the host's resume", host, guest );
			Assert.AreEqual( 2, host.Link.Connections );
			Assert.AreEqual( session.Id, host.Link.Session!.Id );
			Assert.AreEqual( 2UL, host.Resumes[0].LastFrameSeq, "the server's last frame: the host resends after it" );
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
			host.Link.Silence = TimeSpan.FromSeconds( 1 );
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
			await Until( () => host.Changes.Any( change => change.State == CoopMemberState.Disconnected ), "the host to see the guest go", host );
			guest.Link.Instance = Guid.NewGuid();
			await Open( guest );
			await Until( () => host.Link.Session!.Members.Count == 1, "the restarted guest to leave", host, guest );
			Assert.AreEqual( CoopMemberState.Left, host.Changes.Last().State );
			Assert.IsNull( guest.Link.Session );
			Assert.AreEqual( 0, guest.Ended.Count );
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
			Assert.AreEqual( CoopStop.None, host.Link.Stopped );
			host.Link.Open();
			Assert.AreEqual( CoopLinkState.Closed, host.Link.State );
			host.Session.Pump();
			Assert.AreEqual( CoopStop.Closed, host.Link.Stopped, "no login, no link" );
		} );
	}
}
