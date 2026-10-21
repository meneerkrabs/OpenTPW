using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Client;
using OpenTPW.Online.Coop;
using OpenTPW.Online.Packages;
using OpenTPW.Server;

namespace OpenTPW.Tests;

/// <summary>
/// A raw co-op socket for the server tests: binary messages in and out, with timeouts. A background reader queues what arrives, so waiting for a message
/// never cancels a receive (which would abort the socket).
/// </summary>
internal sealed class CoopTestClient : IAsyncDisposable
{
	public const string Build = "1.4.0+0123456789abcdef0123456789abcdef01234567";
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds( 10 );
	private readonly ClientWebSocket socket;
	private readonly System.Threading.Channels.Channel<CoopMessage?> inbox = System.Threading.Channels.Channel.CreateUnbounded<CoopMessage?>();
	private readonly Task reader;
	private readonly SemaphoreSlim sending = new( 1, 1 );
	private volatile TaskCompletionSource? stalled;

	private CoopTestClient( ClientWebSocket socket, string name, Guid instance )
	{
		this.socket = socket;
		Name = name;
		Instance = instance;
		reader = Task.Run( ReadLoopAsync );
	}

	public string Name { get; }
	public Guid Instance { get; }
	public WebSocketState State => socket.State;
	/// <summary>Messages that did not decode (the reader stops at the first).</summary>
	public int Malformed { get; private set; }
	/// <summary>The close status the server sent, once the socket closed; null for an abort.</summary>
	public int? CloseStatus { get; private set; }

	/// <summary>Opens a co-op socket with the account's token as a header, or as the browser's first frame.</summary>
	public static async Task<CoopTestClient> OpenAsync( LoopbackServer server, OnlineClient account, Guid? instance = null, bool firstFrame = false )
	{
		var socket = new ClientWebSocket();
		var token = account.Session!.Token;
		if ( !firstFrame )
			socket.Options.SetRequestHeader( "Authorization", "Bearer " + token );
		var builder = new UriBuilder( new Uri( server.Url, ApiRoutes.Coop.TrimStart( '/' ) ) ) { Scheme = "ws" };
		using var timeout = new CancellationTokenSource( Timeout );
		await socket.ConnectAsync( builder.Uri, timeout.Token );
		if ( firstFrame )
			await socket.SendAsync( StrictJson.Serialize( ChatAuthentication.For( token ) ), WebSocketMessageType.Text, true, timeout.Token );
		return new CoopTestClient( socket, account.Session.Name, instance ?? Guid.NewGuid() );
	}

	/// <summary>Opens a socket and says hello; returns the client and the server's welcome.</summary>
	public static async Task<(CoopTestClient Client, CoopWelcome Welcome)> ConnectAsync( LoopbackServer server, OnlineClient account, Guid? instance = null,
		bool takeOver = false, CoopResume? resume = null, string build = Build, IReadOnlyList<CoopMod>? mods = null, bool firstFrame = false )
	{
		var client = await OpenAsync( server, account, instance, firstFrame );
		await client.SendAsync( new CoopHello( (ushort)CoopProtocol.Version, build, mods ?? Array.Empty<CoopMod>(), client.Instance, takeOver, resume ) );
		return (client, await client.NextAsync<CoopWelcome>());
	}

	private async Task ReadLoopAsync()
	{
		var buffer = new byte[CoopProtocol.MaximumMessageBytes + 1];
		try
		{
			while ( true )
			{
				if ( stalled is { } stall )
					await stall.Task;
				var length = 0;
				WebSocketReceiveResult result;
				do
				{
					result = await socket.ReceiveAsync( new ArraySegment<byte>( buffer, length, buffer.Length - length ), CancellationToken.None );
					if ( result.MessageType == WebSocketMessageType.Close )
					{
						CloseStatus = (int?)result.CloseStatus;
						if ( socket.State == WebSocketState.CloseReceived )
						{
							using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 2 ) );
							await socket.CloseOutputAsync( WebSocketCloseStatus.NormalClosure, "bye", timeout.Token );
						}
						return;
					}
					length += result.Count;
				} while ( !result.EndOfMessage );
				inbox.Writer.TryWrite( CoopMessage.Decode( buffer.AsSpan( 0, length ) ) );
			}
		}
		catch ( InvalidDataException )
		{
			Malformed++;
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or ObjectDisposedException )
		{
		}
		finally
		{
			inbox.Writer.TryWrite( null );
			inbox.Writer.TryComplete();
		}
	}

	public async Task SendAsync( CoopMessage message ) => await SendRawAsync( message.Encode() );

	public async Task SendRawAsync( byte[] bytes, WebSocketMessageType type = WebSocketMessageType.Binary )
	{
		await sending.WaitAsync();
		try
		{
			using var timeout = new CancellationTokenSource( Timeout );
			await socket.SendAsync( bytes, type, true, timeout.Token );
		}
		finally
		{
			sending.Release();
		}
	}

	/// <summary>The next message (pings included); null when the socket closed. Throws <see cref="OperationCanceledException"/> when nothing arrives in time.</summary>
	public async Task<CoopMessage?> ReceiveAsync( TimeSpan? within = null )
	{
		using var timeout = new CancellationTokenSource( within ?? Timeout );
		try
		{
			return await inbox.Reader.ReadAsync( timeout.Token );
		}
		catch ( System.Threading.Channels.ChannelClosedException )
		{
			return null;
		}
	}

	/// <summary>The next message of type <typeparamref name="T"/> that matches, skipping pings and other messages; fails on a close or a timeout.</summary>
	public async Task<T> NextAsync<T>( Func<T, bool>? match = null ) where T : CoopMessage
	{
		var skipped = new List<string>();
		while ( true )
		{
			CoopMessage? message;
			try
			{
				message = await ReceiveAsync();
			}
			catch ( OperationCanceledException )
			{
				throw new AssertFailedException( $"{Name} received no {typeof( T ).Name} (skipped: {string.Join( ", ", skipped )})." );
			}
			if ( message == null )
				throw new AssertFailedException( $"{Name}'s socket closed ({CloseStatus}) before a {typeof( T ).Name} (skipped: {string.Join( ", ", skipped )})." );
			if ( message is T typed && (match == null || match( typed )) )
				return typed;
			skipped.Add( message.ToString() );
		}
	}

	/// <summary>Reads until the socket closes; returns the server's close status (null: aborted).</summary>
	public async Task<int?> ClosedAsync()
	{
		try
		{
			while ( await ReceiveAsync() != null )
			{
			}
		}
		catch ( OperationCanceledException )
		{
			throw new AssertFailedException( $"{Name}'s socket did not close." );
		}
		await reader;
		return CloseStatus;
	}

	/// <summary>Whether nothing but pings arrives within <paramref name="within"/>; a message that does arrive is consumed.</summary>
	public async Task<bool> QuietAsync( TimeSpan within )
	{
		var deadline = DateTime.UtcNow + within;
		while ( true )
		{
			var left = deadline - DateTime.UtcNow;
			if ( left <= TimeSpan.Zero )
				return true;
			try
			{
				if ( await ReceiveAsync( left ) is not CoopPing )
					return false;
			}
			catch ( OperationCanceledException )
			{
				return true;
			}
		}
	}

	/// <summary>Drops the connection without a close (a network loss).</summary>
	public void Abort() => socket.Abort();

	/// <summary>Stops reading the socket, as a stalled game does: the server's sends back up.</summary>
	public void StopReading() => stalled = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );

	/// <summary>Reads the socket again after <see cref="StopReading"/>.</summary>
	public void ResumeReading()
	{
		var stall = stalled;
		stalled = null;
		stall?.TrySetResult();
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			if ( socket.State == WebSocketState.Open )
			{
				using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 2 ) );
				await socket.CloseOutputAsync( WebSocketCloseStatus.NormalClosure, "bye", timeout.Token );
			}
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException )
		{
		}
		stalled?.TrySetResult();
		try { await reader.WaitAsync( TimeSpan.FromSeconds( 3 ) ); }
		catch ( TimeoutException ) { socket.Abort(); }
		socket.Dispose();
		sending.Dispose();
	}
}

/// <summary>
/// [EXT:online-coop-session] Shared park sessions on the server (docs/ONLINE.md, "Shared parks"), over the co-op socket of a loopback server: a host and
/// guests, the host's frames relayed in order, messages, refusals, leaves and kicks, two games of one account, a resume after a lost connection or a
/// restart, the expiry of an absent host, the open list, invitations and the join snapshots ([EXT:online-coop-snapshot-join]).
/// </summary>
[TestClass]
public class CoopServerTests
{
	private static readonly CoopMod Pools = new( "opentpw.pools", "1.0.0", new string( 'a', 64 ) );
	private static readonly CoopMod Evening = new( "opentpw.evening", "1.0.0", new string( 'b', 64 ) );

	private sealed class Accounts : IAsyncDisposable
	{
		private readonly List<OnlineClient> clients = new();

		public async Task<OnlineClient> Add( LoopbackServer server, string name )
		{
			var client = await server.LoginAsync( name );
			clients.Add( client );
			return client;
		}

		public ValueTask DisposeAsync()
		{
			foreach ( var client in clients )
				client.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	/// <summary>A loopback server whose login limit leaves room for a test's players (each registers and logs in).</summary>
	private static Task<LoopbackServer> Start( Action<ServerOptions>? configure = null, string? directory = null ) => LoopbackServer.StartAsync( options =>
	{
		options.AuthenticationsPerMinute = 1000;
		configure?.Invoke( options );
	}, directory );

	private static async Task<CoopCreated> Create( CoopTestClient host, bool listed = false, string title = "Our park", byte players = 4 )
	{
		await host.SendAsync( new CoopCreate( listed, title, players ) );
		return await host.NextAsync<CoopCreated>();
	}

	private static async Task<CoopJoined> Join( CoopTestClient guest, string code )
	{
		await guest.SendAsync( new CoopJoin( code ) );
		return await guest.NextAsync<CoopJoined>();
	}

	private static async Task<CoopError> Refused( CoopTestClient client, CoopMessage request, CoopReason reason )
	{
		await client.SendAsync( request );
		var error = await client.NextAsync<CoopError>();
		Assert.AreEqual( reason, error.Reason, error.Text );
		Assert.AreEqual( request.Type, error.Request );
		return error;
	}

	private static byte[] Payload( int value, int size = 8 )
	{
		var bytes = new byte[size];
		BitConverter.GetBytes( value ).CopyTo( bytes, 0 );
		return bytes;
	}

	[TestMethod]
	public async Task AHostAndThreeGuestsShareFramesAndMessages()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var (host, welcome) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			Assert.IsNull( welcome.Session, "a new player is in no session" );
			Assert.AreEqual( CoopProtocol.Version, welcome.Protocol );
			var created = await Create( host );
			Assert.AreEqual( 0, created.Session.Members.Single().Slot, "the host is slot 0" );
			Assert.AreEqual( created.Session.You, created.Session.Host );
			var guests = new List<(CoopTestClient Client, CoopJoined Joined)>();
			foreach ( var name in new[] { "Guest One", "Guest Two", "Guest Three" } )
			{
				var (guest, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, name ) );
				// the code as a player types it
				var joined = await Join( guest, created.Session.Code.ToLowerInvariant().Insert( 3, "-" ) );
				Assert.AreEqual( created.Session.Id, joined.Session.Id );
				Assert.AreEqual( CoopTestClient.Build, joined.HostBuild );
				var arrived = await host.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Joined );
				Assert.AreEqual( name, arrived.Member.Name );
				Assert.AreEqual( joined.Session.You, arrived.Member.Id );
				guests.Add( (guest, joined) );
			}
			CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, guests.Select( guest => guest.Joined.Session.Members.Single( member => member.Id == guest.Joined.Session.You ).Slot ).ToArray() );
			Assert.AreEqual( 4, guests[2].Joined.Session.Members.Count, "the last guest sees everyone" );
			Assert.AreEqual( 0, guests.Select( guest => guest.Joined.Session.You ).Intersect( new[] { created.Session.You } ).Count(), "member ids are not reused" );

			for ( var seq = 1UL; seq <= 5; seq++ )
				await host.SendAsync( new CoopHostFrame( seq, Payload( (int)seq ) ) );
			foreach ( var (guest, _) in guests )
			{
				for ( var seq = 1UL; seq <= 5; seq++ )
				{
					var frame = await guest.NextAsync<CoopFrame>();
					Assert.AreEqual( seq, frame.Seq, "every guest gets every frame in order" );
					CollectionAssert.AreEqual( Payload( (int)seq ), frame.Payload.ToArray() );
				}
			}

			var hostId = created.Session.You;
			await guests[1].Client.SendAsync( new CoopSend( hostId, Payload( 42 ) ) );
			var request = await host.NextAsync<CoopRelayed>();
			Assert.AreEqual( guests[1].Joined.Session.You, request.From, "the host sees which guest sent it" );
			await host.SendAsync( new CoopSend( CoopProtocol.EveryOtherMember, Payload( 7 ) ) );
			foreach ( var (guest, _) in guests )
				Assert.AreEqual( hostId, (await guest.NextAsync<CoopRelayed>()).From );
			await host.SendAsync( new CoopSend( guests[2].Joined.Session.You, Payload( 8 ) ) );
			CollectionAssert.AreEqual( Payload( 8 ), (await guests[2].Client.NextAsync<CoopRelayed>()).Payload.ToArray() );
			Assert.IsTrue( await guests[0].Client.QuietAsync( TimeSpan.FromMilliseconds( 300 ) ), "a message to one guest reaches only that guest" );

			foreach ( var (guest, _) in guests )
				await guest.DisposeAsync();
			await host.DisposeAsync();
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AJoinIsRefusedWithItsReason()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ), mods: new[] { Pools, Evening } );
			var created = await Create( host, players: 2 );
			var code = created.Session.Code;

			var (stranger, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Stranger" ), mods: new[] { Pools, Evening } );
			await Refused( stranger, new CoopJoin( "ZZZZZZ" ), CoopReason.UnknownCode );
			var (older, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Older Build" ), build: "1.3.9+00", mods: new[] { Pools, Evening } );
			StringAssert.Contains( (await Refused( older, new CoopJoin( code ), CoopReason.BuildDiffers )).Text, "1.3.9+00", "the reason names the builds" );
			var (missing, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "No Pools" ), mods: new[] { Evening } );
			StringAssert.Contains( (await Refused( missing, new CoopJoin( code ), CoopReason.ModsDiffer )).Text, "opentpw.pools" );
			var (reordered, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Other Order" ), mods: new[] { Evening, Pools } );
			StringAssert.Contains( (await Refused( reordered, new CoopJoin( code ), CoopReason.ModsDiffer )).Text, "order", "the load order counts" );

			Assert.AreEqual( created.Session.Id, (await Join( stranger, code )).Session.Id );
			await Refused( stranger, new CoopJoin( code ), CoopReason.InSession );
			await Refused( stranger, new CoopCreate( false, "Mine", 2 ), CoopReason.InSession );
			var (late, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Too Late" ), mods: new[] { Pools, Evening } );
			await Refused( late, new CoopJoin( code ), CoopReason.Full );

			// a kicked player cannot come back
			await host.SendAsync( new CoopKick( (await host.NextAsync<CoopMemberChanged>()).Member.Id ) );
			Assert.AreEqual( CoopReason.Kicked, (await stranger.NextAsync<CoopEnded>()).Reason );
			await Refused( stranger, new CoopJoin( code ), CoopReason.Kicked );
			await Refused( host, new CoopKick( 99 ), CoopReason.NotMember );
			await Refused( stranger, new CoopKick( 1 ), CoopReason.NotHost );
			await Refused( stranger, new CoopLeave( "" ), CoopReason.NotInSession );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task OnlyTheHostSendsFramesAndTheyFollowEachOther()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			var created = await Create( host );
			var (first, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "First Guest" ) );
			var (second, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Second Guest" ) );
			var firstId = (await Join( first, created.Session.Code )).Session.You;
			var secondId = (await Join( second, created.Session.Code )).Session.You;
			await Refused( first, new CoopHostFrame( 1, Payload( 1 ) ), CoopReason.NotHost );
			await Refused( first, new CoopSend( secondId, Payload( 1 ) ), CoopReason.NotHost );
			await Refused( first, new CoopSend( CoopProtocol.EveryOtherMember, Payload( 1 ) ), CoopReason.NotHost );
			await Refused( host, new CoopSend( 77, Payload( 1 ) ), CoopReason.NotMember );
			await second.DisposeAsync();
			await host.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Disconnected );
			await Refused( host, new CoopSend( secondId, Payload( 1 ) ), CoopReason.NotConnected );

			await host.SendAsync( new CoopHostFrame( 1, Payload( 1 ) ) );
			Assert.AreEqual( 1UL, (await first.NextAsync<CoopFrame>()).Seq );
			// a gap would let a guest miss a frame unseen: the frame is dropped and the host told which one comes next, and so is a repeat
			await host.SendAsync( new CoopHostFrame( 3, Payload( 3 ) ) );
			Assert.AreEqual( 2UL, (await host.NextAsync<CoopFrameRefused>()).Expected );
			await host.SendAsync( new CoopHostFrame( 1, Payload( 1 ) ) );
			Assert.AreEqual( 2UL, (await host.NextAsync<CoopFrameRefused>()).Expected );
			await host.SendAsync( new CoopHostFrame( 2, Payload( 2 ) ) );
			await host.SendAsync( new CoopHostFrame( 3, Payload( 3 ) ) );
			Assert.AreEqual( 2UL, (await first.NextAsync<CoopFrame>()).Seq, "the guest never saw the refused frames" );
			Assert.AreEqual( 3UL, (await first.NextAsync<CoopFrame>()).Seq );
			Assert.AreEqual( WebSocketState.Open, host.State );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task GuestsLeaveTheHostKicksAndTheHostsLeaveEndsTheSession()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			var created = await Create( host );
			var guests = new List<(CoopTestClient Client, uint Id)>();
			foreach ( var name in new[] { "Leaver", "Kicked One", "Stayer" } )
			{
				var (guest, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, name ) );
				guests.Add( (guest, (await Join( guest, created.Session.Code )).Session.You) );
			}
			await guests[0].Client.SendAsync( new CoopLeave( "" ) );
			Assert.AreEqual( CoopReason.Left, (await guests[0].Client.NextAsync<CoopEnded>()).Reason );
			Assert.AreEqual( guests[0].Id, (await guests[2].Client.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Left )).Member.Id );
			await host.SendAsync( new CoopKick( guests[1].Id ) );
			Assert.AreEqual( CoopReason.Kicked, (await guests[1].Client.NextAsync<CoopEnded>()).Reason );
			Assert.AreEqual( guests[1].Id, (await guests[2].Client.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Kicked )).Member.Id );
			// the leaver may come back; its new membership has a new id
			var back = await Join( guests[0].Client, created.Session.Code );
			Assert.AreNotEqual( guests[0].Id, back.Session.You );

			await host.SendAsync( new CoopLeave( "The host loaded another park." ) );
			foreach ( var guest in new[] { guests[0].Client, guests[2].Client } )
			{
				var ended = await guest.NextAsync<CoopEnded>();
				Assert.AreEqual( CoopReason.HostLeft, ended.Reason );
				Assert.AreEqual( "The host loaded another park.", ended.Text, "the host's reason is passed on" );
			}
			await Refused( guests[2].Client, new CoopJoin( created.Session.Code ), CoopReason.UnknownCode );
			Assert.AreEqual( "[]", File.ReadAllText( Path.Combine( server.Directory, "coop", "sessions.json" ) ).Trim(), "the ended session is no longer stored" );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task OneAccountHasOneCoopConnectionPerGame()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var account = await accounts.Add( server, "Two Screens" );
			var (desktop, _) = await CoopTestClient.ConnectAsync( server, account );
			// another game of the account (a forgotten browser tab) is refused, so the two never take turns
			var tab = await CoopTestClient.OpenAsync( server, account );
			await tab.SendAsync( new CoopHello( (ushort)CoopProtocol.Version, CoopTestClient.Build, Array.Empty<CoopMod>(), tab.Instance, false, null ) );
			Assert.AreEqual( CoopReason.InUse, (await tab.NextAsync<CoopError>()).Reason );
			Assert.AreEqual( CoopCloseStatus.InUse, await tab.ClosedAsync() );
			Assert.AreEqual( WebSocketState.Open, desktop.State );
			// the player's choice to take over closes the other game with a close it does not resume after
			var (taken, _) = await CoopTestClient.ConnectAsync( server, account, takeOver: true );
			Assert.AreEqual( CoopCloseStatus.Replaced, await desktop.ClosedAsync() );
			Assert.IsFalse( CoopCloseStatus.IsResumable( CoopCloseStatus.Replaced ) );
			// the same game reconnecting (a network change) replaces its old connection quietly, with a close a game resumes after
			var (again, _) = await CoopTestClient.ConnectAsync( server, account, instance: taken.Instance );
			Assert.AreEqual( (int)WebSocketCloseStatus.EndpointUnavailable, await taken.ClosedAsync() );
			Assert.IsTrue( CoopCloseStatus.IsResumable( (int)WebSocketCloseStatus.EndpointUnavailable ) );
			Assert.AreEqual( WebSocketState.Open, again.State );
			// a connection the server already dropped does not keep another game out
			await again.SendRawAsync( new byte[] { 250 } );
			Assert.AreEqual( CoopCloseStatus.ProtocolError, await again.ClosedAsync() );
			var (other, otherWelcome) = await CoopTestClient.ConnectAsync( server, account );
			Assert.AreEqual( WebSocketState.Open, other.State );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AGuestAndTheHostResumeAfterALostConnection()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var hostAccount = await accounts.Add( server, "Hostess" );
			var guestAccount = await accounts.Add( server, "Guest" );
			var (host, _) = await CoopTestClient.ConnectAsync( server, hostAccount );
			var created = await Create( host );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount );
			var joined = await Join( guest, created.Session.Code );
			for ( var seq = 1UL; seq <= 3; seq++ )
				await host.SendAsync( new CoopHostFrame( seq, Payload( (int)seq ) ) );
			for ( var seq = 1UL; seq <= 3; seq++ )
				await guest.NextAsync<CoopFrame>();

			guest.Abort();
			await host.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Disconnected );
			var (resumed, welcome) = await CoopTestClient.ConnectAsync( server, guestAccount, instance: guest.Instance,
				resume: new CoopResume( created.Session.Id, joined.Session.You, 3 ) );
			Assert.AreEqual( created.Session.Id, welcome.Session!.Id, "the guest is back in its session" );
			Assert.AreEqual( 3UL, welcome.Session.LastFrameSeq, "and knows how far the host got" );
			Assert.AreEqual( joined.Session.You, (await host.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Connected )).Member.Id );

			host.Abort();
			await resumed.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Disconnected );
			var (hostAgain, hostWelcome) = await CoopTestClient.ConnectAsync( server, hostAccount, instance: host.Instance,
				resume: new CoopResume( created.Session.Id, created.Session.You, 3 ) );
			Assert.AreEqual( 3UL, hostWelcome.Session!.LastFrameSeq, "the host resends after the last frame the server relayed" );
			await hostAgain.SendAsync( new CoopHostFrame( 4, Payload( 4 ) ) );
			Assert.AreEqual( 4UL, (await resumed.NextAsync<CoopFrame>()).Seq );

			// a game that no longer holds the session hears that it is a member, and leaves
			resumed.Abort();
			await hostAgain.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Disconnected );
			var (restarted, stale) = await CoopTestClient.ConnectAsync( server, guestAccount );
			Assert.AreEqual( created.Session.Id, stale.Session!.Id );
			Assert.IsFalse( stale.Session.Members.Single( member => member.Id == joined.Session.You ).Connected, "it is not resumed without asking" );
			await restarted.SendAsync( new CoopLeave( "" ) );
			Assert.AreEqual( CoopReason.Left, (await restarted.NextAsync<CoopEnded>()).Reason );

			// a resume of a session that ended while the game was away says why
			await hostAgain.SendAsync( new CoopLeave( "Bye." ) );
			await hostAgain.NextAsync<CoopEnded>();
			hostAgain.Abort();
			var (_, gone) = await CoopTestClient.ConnectAsync( server, hostAccount, instance: hostAgain.Instance,
				resume: new CoopResume( created.Session.Id, created.Session.You, 4 ) );
			Assert.IsNull( gone.Session );
			Assert.AreEqual( CoopReason.HostLeft, gone.EndedWhileAway!.Reason );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AReplacedConnectionNeverRelaysAgain()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var hostAccount = await accounts.Add( server, "Hostess" );
			var (old, _) = await CoopTestClient.ConnectAsync( server, hostAccount );
			var created = await Create( old );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Guest" ) );
			await Join( guest, created.Session.Code );
			await old.SendAsync( new CoopHostFrame( 1, Payload( 1 ) ) );
			Assert.AreEqual( 1UL, (await guest.NextAsync<CoopFrame>()).Seq );
			// the same game reconnects while its old connection still sends: the old one is fenced before the new one is admitted
			var (current, welcome) = await CoopTestClient.ConnectAsync( server, hostAccount, instance: old.Instance,
				resume: new CoopResume( created.Session.Id, created.Session.You, 1 ) );
			Assert.AreEqual( 1UL, welcome.Session!.LastFrameSeq );
			try
			{
				await old.SendAsync( new CoopHostFrame( 2, Payload( 666 ) ) );
			}
			catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException )
			{
			}
			await current.SendAsync( new CoopHostFrame( 2, Payload( 2 ) ) );
			var frame = await guest.NextAsync<CoopFrame>();
			Assert.AreEqual( 2UL, frame.Seq );
			CollectionAssert.AreEqual( Payload( 2 ), frame.Payload.ToArray(), "frame 2 is the new connection's" );
			Assert.IsTrue( await guest.QuietAsync( TimeSpan.FromMilliseconds( 300 ) ), "the old connection relayed nothing" );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ASlowGuestIsCutOffAndResumes()
	{
		var server = await Start( options =>
		{
			options.CoopOutboxBytes = 256 * 1024;
			options.CoopHostBytesPerSecond = 64 * 1024 * 1024;
		} );
		await using var accounts = new Accounts();
		try
		{
			var guestAccount = await accounts.Add( server, "Slow Reader" );
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			var created = await Create( host );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount );
			var joined = await Join( guest, created.Session.Code );
			// the guest's game stalls (its reader is gone) while the host streams far more than the socket buffers and the guest's queue hold
			guest.StopReading();
			var seq = 0UL;
			CoopMemberChanged? gone = null;
			for ( var round = 0; round < 400 && gone == null; round++ )
			{
				await host.SendAsync( new CoopHostFrame( ++seq, new byte[200 * 1024] ) );
				if ( round % 20 == 19 )
				{
					try
					{
						if ( await host.ReceiveAsync( TimeSpan.FromMilliseconds( 50 ) ) is CoopMemberChanged { State: CoopMemberState.Disconnected } change )
							gone = change;
					}
					catch ( OperationCanceledException )
					{
					}
				}
			}
			Assert.IsNotNull( gone, "the host heard the slow guest go" );
			Assert.AreEqual( joined.Session.You, gone.Member.Id );
			// a refusal answers after every frame before it, so the server has relayed them all
			await Refused( host, new CoopSend( 99, Payload( 0 ) ), CoopReason.NotMember );
			var (_, welcome) = await CoopTestClient.ConnectAsync( server, guestAccount, instance: guest.Instance,
				resume: new CoopResume( created.Session.Id, joined.Session.You, 0 ) );
			Assert.AreEqual( seq, welcome.Session!.LastFrameSeq, "it resumes knowing which frames to ask the host for" );
			// the stalled reader is let go
			await guest.DisposeAsync();
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ARestartKeepsTheSessionAndItsFramePosition()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-server-{Guid.NewGuid():N}" );
		await using var accounts = new Accounts();
		try
		{
			OnlineClient hostAccount, guestAccount;
			CoopCreated created;
			uint guestId;
			Guid hostInstance, guestInstance;
			var server = await Start( directory: directory );
			try
			{
				hostAccount = await accounts.Add( server, "Hostess" );
				guestAccount = await accounts.Add( server, "Guest" );
				var (host, _) = await CoopTestClient.ConnectAsync( server, hostAccount );
				created = await Create( host );
				var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount );
				guestId = (await Join( guest, created.Session.Code )).Session.You;
				for ( var seq = 1UL; seq <= 3; seq++ )
					await host.SendAsync( new CoopHostFrame( seq, Payload( (int)seq ) ) );
				for ( var seq = 1UL; seq <= 3; seq++ )
					await guest.NextAsync<CoopFrame>();
				hostInstance = host.Instance;
				guestInstance = guest.Instance;
				var hostClosed = host.ClosedAsync();
				var guestClosed = guest.ClosedAsync();
				var stopping = server.DisposeAsync().AsTask();
				server = null;
				Assert.AreEqual( CoopCloseStatus.Restarting, await hostClosed );
				Assert.AreEqual( CoopCloseStatus.Restarting, await guestClosed );
				await stopping;
			}
			finally
			{
				if ( server != null )
					await server.DisposeAsync();
			}
			await using var restarted = await Start( directory: directory );
			var (hostAgain, hostWelcome) = await CoopTestClient.ConnectAsync( restarted, hostAccount, instance: hostInstance,
				resume: new CoopResume( created.Session.Id, created.Session.You, 3 ) );
			Assert.AreEqual( 3UL, hostWelcome.Session!.LastFrameSeq, "the frame position was stored at the shutdown" );
			var (guestAgain, guestWelcome) = await CoopTestClient.ConnectAsync( restarted, guestAccount, instance: guestInstance,
				resume: new CoopResume( created.Session.Id, guestId, 3 ) );
			Assert.AreEqual( created.Session.Code, guestWelcome.Session!.Code );
			await hostAgain.SendAsync( new CoopHostFrame( 4, Payload( 4 ) ) );
			Assert.AreEqual( 4UL, (await guestAgain.NextAsync<CoopFrame>()).Seq );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public async Task ASessionEndsWhenItsHostStaysAway()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			var created = await Create( host );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Guest" ) );
			await Join( guest, created.Session.Code );
			host.Abort();
			await guest.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Disconnected );
			var hub = server.Services.GetRequiredService<CoopHub>();
			hub.Sweep( DateTimeOffset.UtcNow + CoopHub.HostAwayLimit - TimeSpan.FromMinutes( 1 ) );
			Assert.IsTrue( await guest.QuietAsync( TimeSpan.FromMilliseconds( 200 ) ), "nine minutes away is not yet the end" );
			hub.Sweep( DateTimeOffset.UtcNow + CoopHub.HostAwayLimit + TimeSpan.FromMinutes( 1 ) );
			Assert.AreEqual( CoopReason.Expired, (await guest.NextAsync<CoopEnded>()).Reason );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task TheOpenListShowsListedSessionsWithRoomAndAHost()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var lister = await accounts.Add( server, "Lister" );
			async Task<IReadOnlyList<CoopListedSession>> List()
			{
				using var http = new HttpClient();
				http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", lister.Session!.Token );
				var response = await http.GetAsync( new Uri( server.Url, ApiRoutes.CoopSessions.TrimStart( '/' ) ) );
				response.EnsureSuccessStatusCode();
				return StrictJson.Deserialize<CoopSessionList>( await response.Content.ReadAsByteArrayAsync(), "list" ).Sessions;
			}
			var (open, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Open Host" ) );
			var listed = await Create( open, listed: true, title: "Open park", players: 2 );
			var (closed, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Closed Host" ) );
			await Create( closed, listed: false, title: "Private park" );
			var entry = (await List()).Single();
			Assert.AreEqual( (listed.Session.Code, "Open park", "Open Host", 1, 2, CoopTestClient.Build), (entry.Code, entry.Title, entry.Host, entry.Players, entry.MaximumPlayers, entry.Build) );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Guest" ) );
			await Join( guest, entry.Code );
			Assert.AreEqual( 0, (await List()).Count, "a full session leaves the list" );
			await guest.SendAsync( new CoopLeave( "" ) );
			await guest.NextAsync<CoopEnded>();
			Assert.AreEqual( 1, (await List()).Count );
			open.Abort();
			for ( var attempt = 0; attempt < 50 && (await List()).Count > 0; attempt++ )
				await Task.Delay( 100 );
			Assert.AreEqual( 0, (await List()).Count, "so does one whose host is away" );
			await Refused( closed, new CoopCreate( true, "x", 2 ), CoopReason.InSession );
			var (rude, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Titleless" ) );
			await Refused( rude, new CoopCreate( true, "   ", 2 ), CoopReason.TitleRefused );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task InvitationsReachPlayersWhoseGameIsOnline()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			var created = await Create( host, title: "Come build" );
			var (friend, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Friend" ) );
			await accounts.Add( server, "Offline Friend" );
			await host.SendAsync( new CoopInvite( "friend" ) );
			var invited = await friend.NextAsync<CoopInvited>();
			Assert.AreEqual( (created.Session.Id, created.Session.Code, "Come build", "Hostess"), (invited.SessionId, invited.Code, invited.Title, invited.Host) );
			await Refused( host, new CoopInvite( "Offline Friend" ), CoopReason.NotOnline );
			await Refused( host, new CoopInvite( "Nobody Here" ), CoopReason.NotOnline );
			await Refused( host, new CoopInvite( "Hostess" ), CoopReason.NotOnline );
			await Join( friend, invited.Code );
			await Refused( friend, new CoopInvite( "Hostess" ), CoopReason.NotHost );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AccountDeletionEndsTheSessionItHosts()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var hostAccount = await accounts.Add( server, "Leaving Host" );
			var (host, _) = await CoopTestClient.ConnectAsync( server, hostAccount );
			var created = await Create( host );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Guest" ) );
			await Join( guest, created.Session.Code );
			await hostAccount.DeleteAccountAsync( "Leaving Host", "correct horse" );
			Assert.AreEqual( CoopReason.AccountDeleted, (await guest.NextAsync<CoopEnded>()).Reason );
			Assert.AreEqual( (int)WebSocketCloseStatus.PolicyViolation, await host.ClosedAsync() );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task TheSocketAcceptsTheBrowsersFirstFrameAndPingsWhenIdle()
	{
		var server = await Start( options => options.CoopPingInterval = TimeSpan.FromMilliseconds( 200 ) );
		await using var accounts = new Accounts();
		try
		{
			var (browser, welcome) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Browser" ), firstFrame: true );
			Assert.AreEqual( CoopProtocol.Version, welcome.Protocol );
			Assert.IsInstanceOfType( await browser.ReceiveAsync( TimeSpan.FromSeconds( 5 ) ), typeof( CoopPing ), "an idle link hears a ping its JavaScript can see" );
			await browser.SendAsync( new CoopPong() );
			// the first malformed message ends the connection with a close it does not resume after
			await browser.SendRawAsync( new byte[] { 250 } );
			Assert.AreEqual( CoopCloseStatus.ProtocolError, await browser.ClosedAsync() );
			var (text, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Texter" ) );
			await text.SendRawAsync( Encoding.UTF8.GetBytes( "{}" ), WebSocketMessageType.Text );
			Assert.AreEqual( CoopCloseStatus.ProtocolError, await text.ClosedAsync() );
			var silent = await CoopTestClient.OpenAsync( server, await accounts.Add( server, "Silent" ) );
			await silent.SendAsync( new CoopPong() );
			Assert.AreEqual( CoopCloseStatus.ProtocolError, await silent.ClosedAsync(), "the first message is the hello" );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task TheJoinSnapshotIsTheHostsToUploadAndItsMembersToRead()
	{
		var server = await Start( options => options.MaximumCoopSnapshotBytes = 1024 * 1024 );
		await using var accounts = new Accounts();
		try
		{
			var hostAccount = await accounts.Add( server, "Hostess" );
			var guestAccount = await accounts.Add( server, "Guest" );
			var outsider = await accounts.Add( server, "Outsider" );
			var (host, _) = await CoopTestClient.ConnectAsync( server, hostAccount );
			var created = await Create( host );
			var route = ApiRoutes.CoopSnapshot( created.Session.Id ).TrimStart( '/' );
			HttpClient As( OnlineClient account )
			{
				var http = new HttpClient();
				http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", account.Session!.Token );
				return http;
			}
			async Task<HttpResponseMessage> Upload( OnlineClient account, byte[] body )
			{
				using var http = As( account );
				return await http.PutAsync( new Uri( server.Url, route ), new ByteArrayContent( body ) );
			}
			async Task<HttpResponseMessage> Download( OnlineClient account, string sha )
			{
				using var http = As( account );
				return await http.GetAsync( new Uri( server.Url, route + "/" + sha ) );
			}
			var first = RandomNumberGenerator.GetBytes( 300 * 1024 );
			Assert.AreEqual( HttpStatusCode.Conflict, (await Upload( hostAccount, first )).StatusCode, "nobody is joining yet" );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount );
			await Join( guest, created.Session.Code );
			Assert.AreEqual( HttpStatusCode.Forbidden, (await Upload( guestAccount, first )).StatusCode, "only the host uploads" );
			Assert.AreEqual( HttpStatusCode.RequestEntityTooLarge, (await Upload( hostAccount, new byte[1024 * 1024 + 1] )).StatusCode );
			var stored = await Upload( hostAccount, first );
			Assert.AreEqual( HttpStatusCode.OK, stored.StatusCode );
			var answer = StrictJson.Deserialize<CoopSnapshotStored>( await stored.Content.ReadAsByteArrayAsync(), "snapshot" );
			Assert.AreEqual( Convert.ToHexStringLower( SHA256.HashData( first ) ), answer.Sha256 );
			Assert.AreEqual( first.Length, answer.Bytes );
			var read = await Download( guestAccount, answer.Sha256 );
			Assert.AreEqual( HttpStatusCode.OK, read.StatusCode );
			CollectionAssert.AreEqual( first, await read.Content.ReadAsByteArrayAsync() );
			Assert.AreEqual( HttpStatusCode.Forbidden, (await Download( outsider, answer.Sha256 )).StatusCode, "an outsider reads no member's park" );

			// the newest two stay, so a join that started on the older one finishes
			var second = RandomNumberGenerator.GetBytes( 1000 );
			var third = RandomNumberGenerator.GetBytes( 1000 );
			Assert.AreEqual( HttpStatusCode.OK, (await Upload( hostAccount, second )).StatusCode );
			Assert.AreEqual( HttpStatusCode.OK, (await Upload( hostAccount, third )).StatusCode );
			Assert.AreEqual( HttpStatusCode.NotFound, (await Download( guestAccount, answer.Sha256 )).StatusCode );
			Assert.AreEqual( HttpStatusCode.OK, (await Download( guestAccount, Convert.ToHexStringLower( SHA256.HashData( second ) ) )).StatusCode );

			await host.SendAsync( new CoopLeave( "" ) );
			await guest.NextAsync<CoopEnded>();
			Assert.IsFalse( Directory.Exists( Path.Combine( server.Directory, "coop", "blobs", created.Session.Id ) ), "the session's snapshots went with it" );
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	/// <summary>The co-op store's file format (<c>coop/sessions.json</c>).</summary>
	private static readonly System.Text.Json.JsonSerializerOptions CoopFileOptions = new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

	/// <summary>Every message a connection received until it closed.</summary>
	private static async Task<List<CoopMessage>> DrainAsync( CoopTestClient client )
	{
		var messages = new List<CoopMessage>();
		while ( await client.ReceiveAsync() is { } message )
			messages.Add( message );
		return messages;
	}

	/// <summary>A server that lets a host stream as fast as a test sends.</summary>
	private static Task<LoopbackServer> StartFast( Action<ServerOptions>? configure = null, string? directory = null ) => Start( options =>
	{
		options.CoopHostMessagesPerSecond = 100_000;
		options.CoopHostBytesPerSecond = 512 * 1024 * 1024;
		configure?.Invoke( options );
	}, directory );

	/// <summary>The host streams frames as fast as the test sends them until <paramref name="stop"/>, a few hundred at a time between short pauses.</summary>
	private static Task Stream( CoopTestClient host, CancellationToken stop ) => Task.Run( async () =>
	{
		try
		{
			for ( var seq = 1UL; !stop.IsCancellationRequested; seq++ )
			{
				await host.SendAsync( new CoopHostFrame( seq, Payload( (int)seq, 4096 ) ) );
				if ( seq % 256 == 0 )
					await Task.Delay( 1, CancellationToken.None );
			}
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or ObjectDisposedException )
		{
		}
	} );

	/// <summary>
	/// The same game reconnects and resumes, as a game does after any close it resumes after: a connection cut off for slowness before its welcome (its queue
	/// overflowed at once) is opened again.
	/// </summary>
	private static async Task<CoopTestClient> ReconnectAsync( LoopbackServer server, OnlineClient account, Guid instance, CoopResume resume, List<CoopTestClient> cutOff )
	{
		for ( var attempt = 0; ; attempt++ )
		{
			var client = await CoopTestClient.OpenAsync( server, account, instance );
			await client.SendAsync( new CoopHello( (ushort)CoopProtocol.Version, CoopTestClient.Build, Array.Empty<CoopMod>(), instance, false, resume ) );
			while ( await client.ReceiveAsync() is { } message )
			{
				if ( message is CoopWelcome )
					return client;
			}
			Assert.IsTrue( CoopCloseStatus.IsResumable( client.CloseStatus ), $"a close before the welcome is one a game resumes after ({client.CloseStatus})" );
			Assert.IsTrue( attempt < 20, "the game got a welcome again" );
			cutOff.Add( client );
		}
	}

	[TestMethod]
	public async Task AFenceWhileFramesStreamNeverCorruptsWhatAGuestReads()
	{
		// a guest queue of 32 MiB: the fences, not slowness, end the connections here (ACutOffConnectionEndsForTheGame covers the slow reader)
		var server = await StartFast( options => options.CoopOutboxBytes = 32 * 1024 * 1024 );
		await using var accounts = new Accounts();
		try
		{
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			var created = await Create( host );
			var guestAccount = await accounts.Add( server, "Guest" );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount );
			var guestId = (await Join( guest, created.Session.Code )).Session.You;
			using var stop = new CancellationTokenSource();
			var streaming = Stream( host, stop.Token );
			// the same game reconnects again and again while the frames stream: each old connection is fenced with frames still queued for it
			var fenced = new List<CoopTestClient>();
			var current = guest;
			for ( var round = 0; round < 40; round++ )
			{
				await Task.Delay( 15 );
				var again = await ReconnectAsync( server, guestAccount, guest.Instance, new CoopResume( created.Session.Id, guestId, 0 ), fenced );
				fenced.Add( current );
				current = again;
			}
			stop.Cancel();
			await streaming;
			foreach ( var connection in fenced )
			{
				var frames = (await DrainAsync( connection )).OfType<CoopFrame>().Select( frame => frame.Seq ).ToList();
				Assert.AreEqual( 0, connection.Malformed, "every message a fenced connection read decodes" );
				for ( var index = 1; index < frames.Count; index++ )
					Assert.IsTrue( frames[index] > frames[index - 1], $"frames rise on one connection: {frames[index - 1]} then {frames[index]}" );
			}
		}
		finally
		{
			// stopped first: a server still at work writes into the folder being deleted
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ACutOffConnectionEndsForTheGame()
	{
		// a guest queue of 64 KiB: the streaming host has the server cut every guest connection off for slowness within moments
		var server = await StartFast( options => options.CoopOutboxBytes = 64 * 1024 );
		await using var accounts = new Accounts();
		try
		{
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			var created = await Create( host );
			var guestAccount = await accounts.Add( server, "Guest" );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount );
			var guestId = (await Join( guest, created.Session.Code )).Session.You;
			using var stop = new CancellationTokenSource();
			var streaming = Stream( host, stop.Token );
			var ended = new List<CoopTestClient> { guest };
			for ( var round = 0; round < 30; round++ )
			{
				await Task.Delay( 15 );
				ended.Add( await ReconnectAsync( server, guestAccount, guest.Instance, new CoopResume( created.Session.Id, guestId, 0 ), ended ) );
			}
			stop.Cancel();
			await streaming;
			// the newest connection may still stand; every other one was cut off (a close of 4002 after the frame on its way) or replaced (1001), and its game sees it
			// end, never a connection that stays silent: an abort's reset can be lost (macOS limits how many resets it sends), a close cannot
			foreach ( var connection in ended.Take( ended.Count - 1 ) )
			{
				try
				{
					await DrainAsync( connection );
				}
				catch ( OperationCanceledException )
				{
					Assert.Fail( $"connection {ended.IndexOf( connection )} of {ended.Count} stayed open after the server ended it" );
				}
				Assert.AreEqual( 0, connection.Malformed );
			}
		}
		finally
		{
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	/// <summary>
	/// A restart closes every queue after storing the session's frame position; the host's socket then closes first (its queue is short) and the server tells the guest the host went.
	/// That message must not cut off a guest whose queue is at its limit: the cut-off would drop the frames whose position the restart just stored.
	/// </summary>
	[TestMethod]
	public async Task ARestartNeverCutsOffAGuestWhoseQueueIsFull()
	{
		var server = await StartFast( options => options.CoopOutboxBytes = 64 * 1024 * 1024 );
		await using var accounts = new Accounts();
		try
		{
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			var created = await Create( host );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Guest" ) );
			await Join( guest, created.Session.Code );
			var hub = server.Services.GetRequiredService<CoopHub>();
			// the hub's connection of the guest (white-box: its queue's byte count)
			var peers = (System.Collections.IDictionary)typeof( CoopHub ).GetField( "peers", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hub )!;
			var guestPeer = peers.Values.Cast<object>().Single( peer => !(bool)peer.GetType().GetField( "Hosting" )!.GetValue( peer )! );
			var queued = guestPeer.GetType().GetField( "QueuedBytes" )!;
			// the guest's game stalls while the host streams more than the sockets buffer, so frames wait in its queue
			guest.StopReading();
			var seq = 0UL;
			for ( var round = 0; round < 1000 && (long)queued.GetValue( guestPeer )! < 2 * 1024 * 1024; round++ )
			{
				await host.SendAsync( new CoopHostFrame( ++seq, new byte[64 * 1024] ) );
				if ( round % 8 == 7 )
					await Task.Delay( 20 );
			}
			// a refusal answers after every frame before it: the server has queued them all
			await Refused( host, new CoopSend( 99, Payload( 0 ) ), CoopReason.NotMember );
			Assert.IsTrue( (long)queued.GetValue( guestPeer )! > 0, "frames wait in the guest's queue" );
			// the queue stands at its limit: one more byte would cut the guest off
			queued.SetValue( guestPeer, 64L * 1024 * 1024 );
			hub.Restarting();
			// the host's close comes first, and with it the news that it went
			await DrainAsync( host );
			await Task.Delay( 200 );
			guest.ResumeReading();
			var frames = (await DrainAsync( guest )).OfType<CoopFrame>().ToList();
			Assert.AreEqual( CoopCloseStatus.Restarting, guest.CloseStatus, "the guest hears the restart, not a cut-off for slowness (4002)" );
			Assert.AreEqual( seq, frames.Last().Seq, "it got every frame up to the stored position" );
			await guest.DisposeAsync();
		}
		finally
		{
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ARefusalThatListsManyModsStaysReadable()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var mods = Enumerable.Range( 0, 12 ).Select( index => new CoopMod( $"community.a-rather-long-mod-identifier-{index:D2}", "10.20.30", new string( 'd', 64 ) ) ).ToArray();
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ), mods: mods );
			var created = await Create( host );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Guest" ) );
			var refusal = await Refused( guest, new CoopJoin( created.Session.Code ), CoopReason.ModsDiffer );
			Assert.IsTrue( refusal.Text.Length <= CoopProtocol.MaximumReasonLength );
			StringAssert.StartsWith( refusal.Text, "The host's mods differ from yours; you lack community.a-rather-long-mod-identifier-00 10.20.30" );
			var (other, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Other" ), build: "9.9.9+" + new string( 'f', 120 ), mods: mods );
			Assert.IsTrue( (await Refused( other, new CoopJoin( created.Session.Code ), CoopReason.BuildDiffers )).Text.Length <= CoopProtocol.MaximumReasonLength );
			Assert.AreEqual( WebSocketState.Open, guest.State );
		}
		finally
		{
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ARestartUnderTrafficStoresTheLastFrameEveryGuestHas()
	{
		await using var accounts = new Accounts();
		for ( var run = 0; run < 3; run++ )
		{
			var directory = Path.Combine( Path.GetTempPath(), $"opentpw-server-{Guid.NewGuid():N}" );
			try
			{
				// the guest's queue takes the whole burst (the host's 200,000-message burst of 269-byte frames stays under 54 MiB): on a slow runner a queue at its limit
				// would rightly cut the guest off for slowness (4002) before the restart, and the frames it then misses are the host's to resend after its resume, so the
				// restart's position would no longer be the guest's last frame
				var server = await StartFast( options => options.CoopOutboxBytes = 64 * 1024 * 1024, directory );
				var hostAccount = await accounts.Add( server, $"Hostess{run}" );
				var guestAccount = await accounts.Add( server, $"Guest{run}" );
				var (host, _) = await CoopTestClient.ConnectAsync( server, hostAccount );
				var created = await Create( host );
				var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount );
				var guestId = (await Join( guest, created.Session.Code )).Session.You;
				var streaming = Task.Run( async () =>
				{
					try
					{
						for ( var seq = 1UL; ; seq++ )
							await host.SendAsync( new CoopHostFrame( seq, Payload( (int)seq, 256 ) ) );
					}
					catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or ObjectDisposedException )
					{
					}
				} );
				var received = DrainAsync( guest );
				await Task.Delay( 100 );
				await server.DisposeAsync();
				await streaming;
				var last = (await received).OfType<CoopFrame>().Last().Seq;
				Assert.AreEqual( CoopCloseStatus.Restarting, guest.CloseStatus, "the restart's close (1012), not a cut-off for slowness (4002)" );
				await using var restarted = await StartFast( directory: directory );
				var (_, welcome) = await CoopTestClient.ConnectAsync( restarted, hostAccount, instance: host.Instance,
					resume: new CoopResume( created.Session.Id, created.Session.You, last ) );
				Assert.IsTrue( welcome.Session!.SeqKnown, "a shutdown stores the position exactly" );
				Assert.AreEqual( last, welcome.Session.LastFrameSeq, "the stored position is the last frame the guest has, so a resend from the next one repeats nothing" );
			}
			finally
			{
				if ( Directory.Exists( directory ) )
					Directory.Delete( directory, true );
			}
		}
	}

	[TestMethod]
	public async Task AHelloAfterTheLoginEndedIsRefused()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var account = await accounts.Add( server, "Leaver" );
			var socket = await CoopTestClient.OpenAsync( server, account );
			await account.DeleteAccountAsync( "Leaver", "correct horse" );
			// someone registers the free name before the old socket says hello
			var newcomer = await accounts.Add( server, "Leaver" );
			await socket.SendAsync( new CoopHello( (ushort)CoopProtocol.Version, CoopTestClient.Build, Array.Empty<CoopMod>(), socket.Instance, false, null ) );
			Assert.AreEqual( (int)WebSocketCloseStatus.PolicyViolation, await socket.ClosedAsync() );
			var (fresh, welcome) = await CoopTestClient.ConnectAsync( server, newcomer );
			Assert.IsNull( welcome.Session, "the new account is in no session" );
		}
		finally
		{
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AResumeWithAnotherReleaseOrOtherModsEndsTheSessionForThatGame()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			// many long mods, so what differs makes a long text
			var mods = Enumerable.Range( 0, 12 ).Select( index => new CoopMod( $"community.a-rather-long-mod-identifier-{index:D2}", "10.20.30", new string( 'd', 64 ) ) ).ToArray();
			var hostAccount = await accounts.Add( server, "Hostess" );
			var guestAccount = await accounts.Add( server, "Guest" );
			var (host, _) = await CoopTestClient.ConnectAsync( server, hostAccount, mods: mods );
			var created = await Create( host );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount, mods: mods );
			var guestId = (await Join( guest, created.Session.Code )).Session.You;
			guest.Abort();
			await host.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Disconnected );
			var (_, guestWelcome) = await CoopTestClient.ConnectAsync( server, guestAccount, instance: guest.Instance,
				resume: new CoopResume( created.Session.Id, guestId, 0 ), build: "2.0.0+other", mods: mods );
			Assert.IsNull( guestWelcome.Session );
			Assert.AreEqual( CoopReason.BuildDiffers, guestWelcome.EndedWhileAway!.Reason );
			Assert.AreEqual( CoopMemberState.Left, (await host.NextAsync<CoopMemberChanged>()).State, "the guest's seat is free again" );

			// a host whose mods changed while away ends the session
			var (second, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Second" ), mods: mods );
			await Join( second, created.Session.Code );
			host.Abort();
			await second.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Disconnected );
			var (_, hostWelcome) = await CoopTestClient.ConnectAsync( server, hostAccount, instance: host.Instance,
				resume: new CoopResume( created.Session.Id, created.Session.You, 0 ) );
			Assert.AreEqual( CoopReason.ModsDiffer, hostWelcome.EndedWhileAway!.Reason );
			var ended = await second.NextAsync<CoopEnded>();
			Assert.AreEqual( CoopReason.ModsDiffer, ended.Reason );
			StringAssert.StartsWith( ended.Text, "The host's game changed: " );
			Assert.AreEqual( CoopProtocol.MaximumReasonLength, ended.Text.Length, "a long text is clipped, not refused" );
			// the session is gone for good: the guest is free, and so is the host
			var (afterwards, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Afterwards" ) );
			await Refused( afterwards, new CoopJoin( created.Session.Code ), CoopReason.UnknownCode );
			await Create( second, title: "Second park" );
		}
		finally
		{
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AHostThatNoLongerHoldsItsSessionIsAnsweredWhenItLeaves()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var hostAccount = await accounts.Add( server, "Hostess" );
			var (host, _) = await CoopTestClient.ConnectAsync( server, hostAccount );
			var created = await Create( host );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Guest" ) );
			await Join( guest, created.Session.Code );
			host.Abort();
			await guest.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Disconnected );
			// a restarted game: a new process, no resume; the server names the session and the game leaves it
			var (restarted, welcome) = await CoopTestClient.ConnectAsync( server, hostAccount );
			Assert.AreEqual( created.Session.Id, welcome.Session!.Id );
			await restarted.SendAsync( new CoopLeave( "The game restarted." ) );
			Assert.AreEqual( CoopReason.HostLeft, (await restarted.NextAsync<CoopEnded>()).Reason );
			var ended = await guest.NextAsync<CoopEnded>();
			Assert.AreEqual( (CoopReason.HostLeft, "The game restarted."), (ended.Reason, ended.Text) );
		}
		finally
		{
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AfterACrashTheHostsNextLaterFrameSetsThePosition()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-server-{Guid.NewGuid():N}" );
		await using var accounts = new Accounts();
		try
		{
			OnlineClient hostAccount, guestAccount;
			CoopCreated created;
			uint guestId;
			Guid hostInstance, guestInstance;
			await using ( var server = await Start( directory: directory ) )
			{
				hostAccount = await accounts.Add( server, "Hostess" );
				guestAccount = await accounts.Add( server, "Guest" );
				var (host, _) = await CoopTestClient.ConnectAsync( server, hostAccount );
				created = await Create( host );
				var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount );
				guestId = (await Join( guest, created.Session.Code )).Session.You;
				for ( var seq = 1UL; seq <= 3; seq++ )
					await host.SendAsync( new CoopHostFrame( seq, Payload( (int)seq ) ) );
				for ( var seq = 1UL; seq <= 3; seq++ )
					await guest.NextAsync<CoopFrame>();
				hostInstance = host.Instance;
				guestInstance = guest.Instance;
			}
			// a crash: the file holds the position of the last membership change, not the frames since
			var file = Path.Combine( directory, "coop", "sessions.json" );
			var records = System.Text.Json.JsonSerializer.Deserialize<List<CoopSessionRecord>>( File.ReadAllBytes( file ), CoopFileOptions )!;
			File.WriteAllBytes( file, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( records.Select( record => record with { LastFrameSeq = 1, SeqExact = false } ).ToList(), CoopFileOptions ) );
			await using var restarted = await Start( directory: directory );
			var (hostAgain, welcome) = await CoopTestClient.ConnectAsync( restarted, hostAccount, instance: hostInstance,
				resume: new CoopResume( created.Session.Id, created.Session.You, 3 ) );
			Assert.IsFalse( welcome.Session!.SeqKnown );
			Assert.AreEqual( 1UL, welcome.Session.LastFrameSeq );
			var (guestAgain, _) = await CoopTestClient.ConnectAsync( restarted, guestAccount, instance: guestInstance, resume: new CoopResume( created.Session.Id, guestId, 3 ) );
			await hostAgain.SendAsync( new CoopHostFrame( 1, Payload( 1 ) ) );
			Assert.AreEqual( 2UL, (await hostAgain.NextAsync<CoopFrameRefused>()).Expected, "not a frame the server relayed already" );
			// any later number sets the position: the host goes on from its own last frame, and a guest drops what it has
			await hostAgain.SendAsync( new CoopHostFrame( 4, Payload( 4 ) ) );
			Assert.AreEqual( 4UL, (await guestAgain.NextAsync<CoopFrame>()).Seq );
			await hostAgain.SendAsync( new CoopHostFrame( 6, Payload( 6 ) ) );
			Assert.AreEqual( 5UL, (await hostAgain.NextAsync<CoopFrameRefused>()).Expected, "then frames follow each other again" );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public async Task RatesAndQuotasAreKept()
	{
		var server = await Start( options =>
		{
			options.CoopGuestMessagesPerSecond = 5;
			options.CoopCreatesPerHour = 2;
		} );
		await using var accounts = new Accounts();
		try
		{
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			var created = await Create( host );
			await host.SendAsync( new CoopLeave( "" ) );
			await host.NextAsync<CoopEnded>();
			await Create( host );
			await host.SendAsync( new CoopLeave( "" ) );
			await host.NextAsync<CoopEnded>();
			await Refused( host, new CoopCreate( false, "Third park", 2 ), CoopReason.Rate );

			// a guest sending faster than its bucket refills is closed, and resumes after the rate close's wait
			var (guest, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Guest" ) );
			for ( var index = 0; index < 30 && guest.State == WebSocketState.Open; index++ )
			{
				try { await guest.SendAsync( new CoopPong() ); }
				catch ( WebSocketException ) { }
			}
			Assert.AreEqual( CoopCloseStatus.Rate, await guest.ClosedAsync() );
			Assert.IsTrue( CoopCloseStatus.IsResumable( CoopCloseStatus.Rate ) );
		}
		finally
		{
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AMemberKickedWhileAwayHearsWhyWhenItResumes()
	{
		var server = await Start();
		await using var accounts = new Accounts();
		try
		{
			var (host, _) = await CoopTestClient.ConnectAsync( server, await accounts.Add( server, "Hostess" ) );
			var created = await Create( host );
			var guestAccount = await accounts.Add( server, "Guest" );
			var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount );
			var guestId = (await Join( guest, created.Session.Code )).Session.You;
			guest.Abort();
			await host.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Disconnected );
			await host.SendAsync( new CoopKick( guestId ) );
			await host.NextAsync<CoopMemberChanged>( change => change.State == CoopMemberState.Kicked );
			var (_, welcome) = await CoopTestClient.ConnectAsync( server, guestAccount, instance: guest.Instance, resume: new CoopResume( created.Session.Id, guestId, 0 ) );
			Assert.IsNull( welcome.Session );
			Assert.AreEqual( CoopReason.Kicked, welcome.EndedWhileAway!.Reason );
		}
		finally
		{
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	/// <summary>A request body that sends its first bytes, then waits until <see cref="Release"/> (an upload in flight).</summary>
	private sealed class HeldContent( byte[] body ) : HttpContent
	{
		private readonly TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );
		public readonly TaskCompletionSource Started = new( TaskCreationOptions.RunContinuationsAsynchronously );

		public void Release() => release.TrySetResult();

		protected override async Task SerializeToStreamAsync( Stream stream, System.Net.TransportContext? context )
		{
			await stream.WriteAsync( body.AsMemory( 0, 1024 ) );
			await stream.FlushAsync();
			Started.TrySetResult();
			await release.Task;
			await stream.WriteAsync( body.AsMemory( 1024 ) );
		}

		protected override bool TryComputeLength( out long length )
		{
			length = body.Length;
			return true;
		}
	}

	[TestMethod]
	public async Task SnapshotUploadsInFlightCountAgainstTheStoreAndDownloadsResume()
	{
		const int Megabyte = 1024 * 1024;
		var server = await Start( options =>
		{
			options.MaximumCoopSnapshotBytes = Megabyte;
			options.MaximumCoopStoreBytes = Megabyte * 3 / 2;
		} );
		await using var accounts = new Accounts();
		try
		{
			HttpClient As( OnlineClient account )
			{
				var http = new HttpClient();
				http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", account.Session!.Token );
				return http;
			}
			async Task<(OnlineClient Host, OnlineClient Guest, string Route)> Share( string name )
			{
				var hostAccount = await accounts.Add( server, name + "Host" );
				var guestAccount = await accounts.Add( server, name + "Guest" );
				var (host, _) = await CoopTestClient.ConnectAsync( server, hostAccount );
				var created = await Create( host );
				var (guest, _) = await CoopTestClient.ConnectAsync( server, guestAccount );
				await Join( guest, created.Session.Code );
				return (hostAccount, guestAccount, ApiRoutes.CoopSnapshot( created.Session.Id ).TrimStart( '/' ));
			}
			var (firstHost, firstGuest, firstRoute) = await Share( "First" );
			var (secondHost, _, secondRoute) = await Share( "Second" );
			var body = RandomNumberGenerator.GetBytes( 600 * 1024 );
			using var held = new HeldContent( body );
			using var firstHttp = As( firstHost );
			var inFlight = firstHttp.PutAsync( new Uri( server.Url, firstRoute ), held );
			await held.Started.Task;
			// the server took the upload once its file exists
			var firstFolder = Path.Combine( server.Directory, "coop", "blobs", firstRoute.Split( '/' )[^2] );
			for ( var wait = 0; wait < 1000 && !(Directory.Exists( firstFolder ) && Directory.EnumerateFiles( firstFolder, "*.tmp" ).Any()); wait++ )
				await Task.Delay( 10 );
			Assert.IsTrue( Directory.EnumerateFiles( firstFolder, "*.tmp" ).Any(), "the first upload is in flight" );
			using ( var secondHttp = As( secondHost ) )
				Assert.AreEqual( HttpStatusCode.InsufficientStorage, (await secondHttp.PutAsync( new Uri( server.Url, secondRoute ), new ByteArrayContent( new byte[1000] ) )).StatusCode,
					"the upload in flight holds its room" );
			held.Release();
			var stored = await inFlight;
			Assert.AreEqual( HttpStatusCode.OK, stored.StatusCode );
			var sha = StrictJson.Deserialize<CoopSnapshotStored>( await stored.Content.ReadAsByteArrayAsync(), "snapshot" ).Sha256;

			// a download resumes with a range, which counts against no quota; the digest is the entity tag
			using var guestHttp = As( firstGuest );
			using var request = new HttpRequestMessage( HttpMethod.Get, new Uri( server.Url, firstRoute + "/" + sha ) );
			request.Headers.Range = new RangeHeaderValue( 1000, null );
			using var partial = await guestHttp.SendAsync( request );
			Assert.AreEqual( HttpStatusCode.PartialContent, partial.StatusCode );
			CollectionAssert.AreEqual( body.AsSpan( 1000 ).ToArray(), await partial.Content.ReadAsByteArrayAsync() );
			Assert.AreEqual( $"\"{sha}\"", partial.Headers.ETag?.Tag );
			Assert.AreEqual( HttpStatusCode.NotFound, (await guestHttp.GetAsync( new Uri( server.Url, firstRoute + "/" + new string( 'e', 64 ) ) )).StatusCode );
		}
		finally
		{
			await server.DisposeAsync();
			server.DeleteData();
		}
	}

	[TestMethod]
	public void OnlyARangeFromALaterByteResumesADownload()
	{
		Assert.IsTrue( ServerProgram.ResumesDownload( "bytes=1000-" ) );
		Assert.IsTrue( ServerProgram.ResumesDownload( "bytes=1-2000" ) );
		foreach ( var whole in new[] { "", "bytes=0-", "bytes=0-99", "bytes=-500", "bytes=10-20,30-40", "bytes=abc-", "items=5-", "bytes=+5-" } )
			Assert.IsFalse( ServerProgram.ResumesDownload( whole ), whole );
	}

	[TestMethod]
	public void AStartSweepsTheSnapshotsOfSessionsThatAreGone()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-server-{Guid.NewGuid():N}" );
		try
		{
			var live = new CoopSessionRecord( "0123456789abcdef", "ABCDEF", "Park", false, 2, CoopTestClient.Build, Array.Empty<CoopMod>(), 1, 2, 0,
				new[] { new CoopMemberRecord( 1, 0, "hostess", "Hostess" ) }, Array.Empty<string>() );
			var blobs = Path.Combine( directory, "coop", "blobs" );
			Directory.CreateDirectory( Path.Combine( blobs, live.Id ) );
			Directory.CreateDirectory( Path.Combine( blobs, "fedcba9876543210" ) );
			File.WriteAllBytes( Path.Combine( blobs, live.Id, new string( 'a', 64 ) ), new byte[] { 1 } );
			File.WriteAllBytes( Path.Combine( blobs, live.Id, "half.tmp" ), new byte[] { 1 } );
			File.WriteAllBytes( Path.Combine( blobs, "fedcba9876543210", new string( 'b', 64 ) ), new byte[] { 1 } );
			File.WriteAllBytes( Path.Combine( directory, "coop", "sessions.json" ), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( new[] { live }, CoopFileOptions ) );
			var store = new CoopStore( directory );
			store.SweepOrphans( store.Load().Select( record => record.Id ).ToHashSet() );
			Assert.IsTrue( File.Exists( Path.Combine( blobs, live.Id, new string( 'a', 64 ) ) ), "a live session's snapshot stays" );
			Assert.IsFalse( File.Exists( Path.Combine( blobs, live.Id, "half.tmp" ) ), "an unfinished upload goes" );
			Assert.IsFalse( Directory.Exists( Path.Combine( blobs, "fedcba9876543210" ) ), "a gone session's folder goes" );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}
}
