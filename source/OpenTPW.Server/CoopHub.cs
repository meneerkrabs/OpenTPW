using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Coop;
using OpenTPW.Online.Moderation;

namespace OpenTPW.Server;

/// <summary>
/// [EXT:online-coop-session] Shared park sessions over the co-op socket (docs/ONLINE.md, "Shared parks"): a host and up to three guests, the host's frames
/// relayed in order to the guests, messages between members, invitations and the open list. The server never reads a frame, a message or a snapshot.
/// <para>
/// Locks: <c>registry</c> (connections, sessions by id and code, memberships) before a session's own gate (its members and relays) before the store's lock
/// (its file). A relay takes only its session's gate; a membership change is stored before it is announced; nothing writes a file under a lock a relay
/// of another session waits for. [EXT:online-coop-session] The chat's lock comes after all of these: <see cref="MemberGone"/> is raised under them and its
/// handler takes the chat's rooms, while the chat asks <see cref="IsMember"/> without waiting on any lock of the hub.
/// </para>
/// </summary>
public sealed class CoopHub : ICoopMembership
{
	/// <summary>How long a session lives while its host is away (a reconnect, a restart); then it ends.</summary>
	public static readonly TimeSpan HostAwayLimit = TimeSpan.FromMinutes( 10 );
	/// <summary>How long the reason a session ended is kept for a member that resumes after it.</summary>
	private static readonly TimeSpan EndedMemory = TimeSpan.FromHours( 1 );
	/// <summary>How long the server waits for a connection's <see cref="CoopHello"/>.</summary>
	private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds( 10 );
	/// <summary>How long one send to a game may take; a game that reads slower than this is dropped (Kestrel's minimum response rate leaves WebSockets alone).</summary>
	private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds( 30 );
	/// <summary>How long a connection's clean-up waits for its writer once the connection ended.</summary>
	private static readonly TimeSpan WriterDrain = TimeSpan.FromSeconds( 3 );
	/// <summary>Snapshots kept per session: the newest two, so a join that started on the older one finishes.</summary>
	public const int KeptSnapshots = 2;
	private const int ReceiveChunk = 4096;

	private readonly ServerStore accounts;
	private readonly ServerOptions options;
	private readonly WordFilter filter;
	private readonly CoopStore store;
	private readonly Func<DateTimeOffset> clock;
	private readonly object registry = new();
	private readonly Dictionary<string, Peer> peers = new();
	private readonly Dictionary<string, Session> sessions = new();
	private readonly Dictionary<string, Session> codes = new();
	private readonly Dictionary<string, Session> memberships = new();
	/// <summary>[EXT:online-coop-session] The session id each member account holds a seat in: <see cref="memberships"/> as the chat reads it, without the registry lock (written under it, with it).</summary>
	private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> memberSessions = new();
	private readonly Dictionary<string, (string SessionId, CoopReason Reason, string Text, DateTimeOffset At)> endedSessions = new();
	/// <summary>Each account's relay buckets, kept over reconnects so a new socket starts with what the last one left. Under the registry lock.</summary>
	private readonly Dictionary<string, AccountLimits> limits = new();
	/// <summary>Every running connection, for the server's queue budget (read without the registry lock).</summary>
	private readonly System.Collections.Concurrent.ConcurrentDictionary<Peer, byte> running = new();
	private readonly Quotas quotas;
	private long queuedBytes;
	/// <summary>Snapshot bytes promised to uploads in progress (the store's cap counts them). Under the registry lock.</summary>
	private long reservedUploadBytes;
	private int connections;
	private volatile bool restarting;

	/// <summary>Where a failure to write the co-op files is reported (the server's log); the sessions go on in memory and the next change writes them again.</summary>
	public Action<string>? Warn { get; set; }

	/// <summary>The co-op sessions' files (the routes stream snapshots through it).</summary>
	public CoopStore Store => store;

	public CoopHub( ServerStore accounts, ServerOptions options, WordFilter filter, CoopStore store, Func<DateTimeOffset>? clock = null )
	{
		this.accounts = accounts;
		this.options = options;
		this.filter = filter;
		this.store = store;
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
		quotas = new Quotas( this.clock );
		// a start counts as everyone disconnecting now: a host has the away limit to come back
		var started = this.clock();
		var loaded = store.Load();
		foreach ( var record in loaded )
		{
			var session = Session.FromRecord( record, started );
			sessions[session.Id] = session;
			codes[session.Code] = session;
			foreach ( var seat in session.Seats )
				AddMembership( seat.AccountKey, session );
		}
		store.SweepOrphans( sessions.Keys.ToHashSet() );
		// a position stored at a stop is exact for this run only: it is stored again as unknown at once, so a crash later in the run, after frames went on, brings it back
		// unknown (the host then resends from its oldest frame and a guest drops what it has), never as an exact number behind frames the guests hold. A failed write here
		// leaves the store unsaved, and the next change writes it.
		if ( loaded.Any( record => record.SeqExact ) )
			Persist( () => store.PutAll( sessions.Values.Select( session => session.ToRecord( exact: false ) ).ToList() ) );
	}

	// ---- connections ----

	/// <summary>Takes a connection slot before the socket is accepted; false when the server holds its most co-op connections.</summary>
	public bool TryReserveConnection()
	{
		if ( Interlocked.Increment( ref connections ) <= options.MaximumCoopConnections )
			return true;
		Interlocked.Decrement( ref connections );
		return false;
	}

	public void ReleaseConnection() => Interlocked.Decrement( ref connections );

	/// <summary>
	/// Runs an accepted, authenticated co-op socket until it closes; the caller has reserved its connection slot. <paramref name="abortConnection"/> drops the
	/// connection under the socket (the request's own abort): a socket that ends without its close handshake goes with it, since an aborted WebSocket can
	/// leave its connection open under it, and the game would then hear nothing until its silence watchdog.
	/// </summary>
	public async Task RunAsync( WebSocket socket, AccountRecord account, string token, Action abortConnection, CancellationToken cancel )
	{
		// a refusal before the socket runs closes it, and drops the connection when the game does not answer the close
		async Task RefuseAsync( int status, string reason )
		{
			await CloseAsync( socket, status, reason );
			if ( socket.State != WebSocketState.Closed )
				abortConnection();
		}
		var (hello, first) = await ReadHelloAsync( socket, cancel );
		if ( hello == null )
		{
			if ( first is { } type )
				await SendDirectAsync( socket, new CoopError( type, CoopReason.HelloFirst, "The first message is the hello." ), cancel );
			await RefuseAsync( CoopCloseStatus.ProtocolError, "hello first" );
			return;
		}
		if ( hello.Protocol < CoopProtocol.Version )
		{
			await SendDirectAsync( socket, new CoopError( CoopMessageType.Hello, CoopReason.ProtocolTooOld, $"This server shares parks from protocol {CoopProtocol.Version} on." ), cancel );
			await RefuseAsync( CoopCloseStatus.ProtocolError, "protocol too old" );
			return;
		}
		var peer = new Peer( socket, account, token, hello );
		var registration = Register( peer );
		if ( registration != Registration.Welcomed )
		{
			if ( registration == Registration.InUse )
			{
				await SendDirectAsync( socket, new CoopError( CoopMessageType.Hello, CoopReason.InUse, "Another game of this account is connected." ), cancel );
				await RefuseAsync( CoopCloseStatus.InUse, "in use elsewhere" );
			}
			else if ( registration == Registration.Restarting )
				await RefuseAsync( CoopCloseStatus.Restarting, "server restarting" );
			else
				await RefuseAsync( (int)WebSocketCloseStatus.PolicyViolation, "unauthorized" );
			peer.Dispose();
			return;
		}
		running[peer] = 0;
		using var linked = CancellationTokenSource.CreateLinkedTokenSource( cancel, peer.Closed.Token );
		var writer = Task.Run( () => WriteLoopAsync( peer, linked.Token ), linked.Token );
		try
		{
			await ReadLoopAsync( peer, linked.Token );
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or InvalidDataException )
		{
		}
		finally
		{
			Disconnected( peer );
			running.TryRemove( peer, out _ );
			peer.Outbox.Writer.TryComplete();
			// a writer stuck on a game that stopped reading is cut off
			try { peer.Closed.CancelAfter( WriterDrain ); }
			catch ( ObjectDisposedException ) { }
			try { await writer; }
			catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException ) { }
			finally
			{
				// whatever ended the writer, its queue no longer counts against the server's budget
				Interlocked.Add( ref queuedBytes, -Interlocked.Exchange( ref peer.QueuedBytes, 0 ) );
			}
			// the writer sent the close a kill asked for; this finishes the handshake (or sends a plain close) and drops a peer that does not answer
			if ( socket.State is WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.CloseSent )
				await CloseAsync( socket, peer.CloseStatus ?? (int)WebSocketCloseStatus.NormalClosure, peer.CloseReason ?? "bye" );
			if ( socket.State != WebSocketState.Closed )
			{
				socket.Abort();
				abortConnection();
			}
			peer.Dispose();
		}
	}

	/// <summary>The connection's hello; null without one, with the type of a well-formed first message that was something else.</summary>
	private static async Task<(CoopHello? Hello, CoopMessageType? First)> ReadHelloAsync( WebSocket socket, CancellationToken cancel )
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancel );
		timeout.CancelAfter( HelloTimeout );
		try
		{
			if ( await ReceiveAsync( socket, timeout.Token ) is not { } bytes )
				return (null, null);
			var message = CoopMessage.Decode( bytes );
			return message is CoopHello hello ? (hello, null) : (null, message.Type);
		}
		catch ( Exception exception ) when ( exception is OperationCanceledException or WebSocketException or InvalidDataException or MessageTooLargeException )
		{
			return (null, null);
		}
	}

	private enum Registration
	{
		Welcomed,
		/// <summary>Another game of the account holds the socket and the hello does not take it over.</summary>
		InUse,
		/// <summary>The login ended between the upgrade and the hello (a logout, a recovery, a deleted account).</summary>
		LoggedOut,
		/// <summary>The server is stopping: the game resumes on the new one.</summary>
		Restarting
	}

	/// <summary>
	/// Makes <paramref name="peer"/> its account's co-op connection once its login still holds: a reconnect of the same game process replaces the old
	/// connection (fenced first), another process of the account is refused unless it takes over or the old connection is already dead. Then answers the
	/// hello: the session the account belongs to, resumed when the hello asks for it and still has the session's build and mods.
	/// </summary>
	private Registration Register( Peer peer )
	{
		var key = peer.Account.Key;
		lock ( registry )
		{
			// a hello after the stop began would hold a socket the stop never closes: it resumes on the new server instead
			if ( restarting )
				return Registration.Restarting;
			// the hello may come seconds after the upgrade: a login that ended meanwhile (a deleted account's name may even belong to someone new) is refused
			if ( accounts.Authenticate( peer.Token ) is not { } current || current.Key != key )
				return Registration.LoggedOut;
			if ( peers.TryGetValue( key, out var old ) )
			{
				if ( old.Hello.Instance != peer.Hello.Instance && !peer.Hello.TakeOver && !old.Dead )
					return Registration.InUse;
				// the same game's old socket gets a close it would resume after (it never acts on it: its game already holds the new one)
				Fence( old, old.Hello.Instance == peer.Hello.Instance ? (int)WebSocketCloseStatus.EndpointUnavailable : CoopCloseStatus.Replaced, "replaced" );
			}
			peers[key] = peer;
			if ( !limits.TryGetValue( key, out var buckets ) )
				limits[key] = buckets = new AccountLimits( options );
			peer.Buckets = buckets;
			var resume = peer.Hello.Resume;
			if ( memberships.TryGetValue( key, out var session ) )
			{
				lock ( session.Gate )
				{
					var seat = session.Seat( key )!;
					if ( resume != null && resume.SessionId == session.Id && resume.MemberId == seat.Id )
					{
						// a game that changed its release or its mods while away cannot go on in the session (decisions Q11 and Q16)
						if ( Mismatch( session, peer.Hello ) is { } mismatch )
						{
							var hosting = seat.Id == session.HostMember;
							if ( hosting )
								End( session, mismatch.Reason, "The host's game changed: " + mismatch.Text );
							else
							{
								RemoveSeat( session, seat, CoopMemberState.Left );
								endedSessions[key] = (session.Id, mismatch.Reason, mismatch.Text, clock());
							}
							Enqueue( peer, new CoopWelcome( CoopProtocol.Version, null, new CoopEnded( session.Id, mismatch.Reason, mismatch.Text ) ).Encode() );
							return Registration.Welcomed;
						}
						seat.Peer = peer;
						peer.Session = session;
						peer.MemberId = seat.Id;
						peer.Hosting = seat.Id == session.HostMember;
						if ( peer.Hosting )
							session.HostAwaySince = null;
						Announce( session, seat, CoopMemberState.Connected );
					}
					Enqueue( peer, new CoopWelcome( CoopProtocol.Version, session.StateFor( seat.Id ), null ).Encode() );
				}
				return Registration.Welcomed;
			}
			var ended = resume != null && endedSessions.TryGetValue( key, out var gone ) && gone.SessionId == resume.SessionId
				? new CoopEnded( gone.SessionId, gone.Reason, gone.Text )
				: resume != null ? new CoopEnded( resume.SessionId, CoopReason.Unknown, "The session no longer exists." ) : null;
			Enqueue( peer, new CoopWelcome( CoopProtocol.Version, null, ended ).Encode() );
			return Registration.Welcomed;
		}
	}

	/// <summary>How <paramref name="hello"/>'s build or mods differ from the session's (decisions Q11 and Q16), or null when they are the same.</summary>
	private static (CoopReason Reason, string Text)? Mismatch( Session session, CoopHello hello )
	{
		if ( hello.Build != session.Build )
			return (CoopReason.BuildDiffers, CoopProtocol.Clip( $"The host plays OpenTPW {session.Build}; you play {hello.Build}." ));
		if ( !hello.Mods.SequenceEqual( session.Mods ) )
			return (CoopReason.ModsDiffer, ModsDifference( session.Mods, hello.Mods ));
		return null;
	}

	/// <summary>
	/// Takes a connection out of the hub so it can never relay again (a reconnect replaced it): it is no longer its account's connection, and it is ended
	/// (<see cref="Kill"/>: its seat emptied, its queue dropped, a close with <paramref name="status"/>). Under the registry lock.
	/// </summary>
	private void Fence( Peer peer, int status, string reason )
	{
		peers.Remove( peer.Account.Key );
		Kill( peer, status, reason );
	}

	/// <summary>
	/// Empties the seat <paramref name="peer"/> holds, if it still holds one: the other members hear it disconnected (a resume connects it again), and a host
	/// is away from now. Takes the session's gate (after the registry lock, or inside the same gate: a kill during a relay).
	/// </summary>
	private void Detach( Peer peer )
	{
		if ( peer.Session is not { } session )
			return;
		lock ( session.Gate )
		{
			peer.Session = null;
			if ( !session.Ended && session.Seat( peer.Account.Key ) is { } seat && seat.Peer == peer )
			{
				seat.Peer = null;
				if ( seat.Id == session.HostMember )
					session.HostAwaySince = clock();
				Announce( session, seat, CoopMemberState.Disconnected );
			}
		}
	}

	/// <summary>A connection's socket closed: its seat is empty until it resumes, and its session's host is away when it hosted.</summary>
	private void Disconnected( Peer peer )
	{
		lock ( registry )
		{
			if ( peers.TryGetValue( peer.Account.Key, out var current ) && current == peer )
				peers.Remove( peer.Account.Key );
			Detach( peer );
		}
	}

	private async Task ReadLoopAsync( Peer peer, CancellationToken cancel )
	{
		while ( peer.Socket.State == WebSocketState.Open && !cancel.IsCancellationRequested )
		{
			byte[]? bytes;
			try
			{
				bytes = await ReceiveAsync( peer.Socket, cancel );
			}
			catch ( MessageTooLargeException )
			{
				Kill( peer, (int)WebSocketCloseStatus.MessageTooBig, "message too large" );
				return;
			}
			catch ( InvalidDataException )
			{
				// a text message
				Kill( peer, CoopCloseStatus.ProtocolError, "invalid message" );
				return;
			}
			if ( bytes == null )
				return;
			// a killed or fenced connection acts on nothing more, and nothing is done while the server stops (the stored frame positions stay true)
			if ( peer.Dead || restarting )
				continue;
			if ( !peer.Buckets!.Take( peer.Hosting, bytes.Length ) )
			{
				Kill( peer, CoopCloseStatus.Rate, "rate" );
				return;
			}
			CoopMessage request;
			try
			{
				request = CoopMessage.Decode( bytes );
			}
			catch ( InvalidDataException )
			{
				Kill( peer, CoopCloseStatus.ProtocolError, "malformed message" );
				return;
			}
			if ( !Handle( peer, request, bytes ) )
				return;
		}
	}

	/// <summary>Reads one whole binary message: null at a close; a text message throws <see cref="InvalidDataException"/>, an oversized one <see cref="MessageTooLargeException"/>.</summary>
	private static async Task<byte[]?> ReceiveAsync( WebSocket socket, CancellationToken cancel )
	{
		var chunk = new byte[ReceiveChunk];
		byte[]? large = null;
		var length = 0;
		try
		{
			while ( true )
			{
				var target = large ?? chunk;
				if ( length == target.Length )
				{
					if ( large != null )
						throw new MessageTooLargeException();
					// an idle socket holds only the small buffer: a long message rents the large one
					large = ArrayPool<byte>.Shared.Rent( CoopProtocol.MaximumMessageBytes + 1 );
					chunk.AsSpan( 0, length ).CopyTo( large );
					target = large;
				}
				var result = await socket.ReceiveAsync( target.AsMemory( length, Math.Min( target.Length, CoopProtocol.MaximumMessageBytes + 1 ) - length ), cancel );
				if ( result.MessageType == WebSocketMessageType.Close )
					return null;
				if ( result.MessageType != WebSocketMessageType.Binary )
					throw new InvalidDataException( "The co-op socket takes binary messages." );
				length += result.Count;
				if ( length > CoopProtocol.MaximumMessageBytes )
					throw new MessageTooLargeException();
				if ( result.EndOfMessage )
					return (large ?? chunk).AsSpan( 0, length ).ToArray();
			}
		}
		finally
		{
			if ( large != null )
				ArrayPool<byte>.Shared.Return( large );
		}
	}

	private static readonly byte[] Ping = new CoopPing().Encode();

	private async Task WriteLoopAsync( Peer peer, CancellationToken cancel )
	{
		var reader = peer.Outbox.Reader;
		var lastSent = clock();
		Task<bool>? waiting = null;
		// one timer at a time, whatever the traffic: it fires once per ping interval
		Task? tick = null;
		using var sending = CancellationTokenSource.CreateLinkedTokenSource( cancel );
		while ( true )
		{
			waiting ??= reader.WaitToReadAsync( cancel ).AsTask();
			// timed from the last send, so a ping follows it after one interval, not two
			if ( tick == null )
			{
				var wait = options.CoopPingInterval - (clock() - lastSent);
				tick = Task.Delay( wait > TimeSpan.Zero ? wait : TimeSpan.Zero, cancel );
			}
			if ( await Task.WhenAny( waiting, tick ) == waiting )
			{
				if ( !await waiting )
					break;
				waiting = null;
				while ( reader.TryRead( out var item ) )
				{
					if ( peer.Socket.State != WebSocketState.Open )
						return;
					await SendBoundedAsync( peer, item, sending );
					Interlocked.Add( ref peer.QueuedBytes, -item.Length );
					Interlocked.Add( ref queuedBytes, -item.Length );
					lastSent = clock();
				}
			}
			else
			{
				await tick;
				tick = null;
			}
			var now = clock();
			if ( now - lastSent >= options.CoopPingInterval && peer.Socket.State == WebSocketState.Open )
			{
				await SendBoundedAsync( peer, Ping, sending );
				lastSent = now;
			}
		}
		// the queue ended: a kill's close, a restart's, or none (the read loop ended first)
		var status = peer.CloseStatus ?? (restarting ? CoopCloseStatus.Restarting : null);
		if ( status != null && peer.Socket.State == WebSocketState.Open )
			await peer.Socket.CloseOutputAsync( (WebSocketCloseStatus)status.Value, peer.CloseReason ?? "server restarting", cancel );
	}

	/// <summary>Sends one message, cancelling (which drops the socket) when the game has not taken it within <see cref="SendTimeout"/>.</summary>
	private static async Task SendBoundedAsync( Peer peer, ReadOnlyMemory<byte> item, CancellationTokenSource sending )
	{
		sending.CancelAfter( SendTimeout );
		await peer.Socket.SendAsync( item, WebSocketMessageType.Binary, true, sending.Token );
		sending.CancelAfter( Timeout.InfiniteTimeSpan );
	}

	/// <summary>
	/// Queues a message for a connection. A queue over its limit marks the connection dead and ends it (<see cref="Kill"/>), so it never sees a gap; a server
	/// over its budget ends the connection with the longest queue instead. A connection that is closing (a kill's or a restart's close is on its way) takes
	/// nothing more.
	/// </summary>
	private void Enqueue( Peer peer, ReadOnlyMemory<byte> bytes )
	{
		if ( peer.Dead || peer.Closing )
			return;
		var mine = Interlocked.Add( ref peer.QueuedBytes, bytes.Length );
		var all = Interlocked.Add( ref queuedBytes, bytes.Length );
		if ( mine > options.CoopOutboxBytes )
		{
			Unqueue( peer, bytes.Length );
			// a queue a restart closed meanwhile is never cut off: it holds frames whose position the restart stored
			if ( !peer.Closing )
				Kill( peer, CoopCloseStatus.TooSlow, "too slow" );
			return;
		}
		if ( all > options.CoopServerOutboxBytes )
		{
			// the slowest reader goes, not whichever connection happens to queue next, and never one a restart is closing
			var slowest = running.Keys.Where( candidate => !candidate.Dead && !candidate.Closing ).MaxBy( candidate => Volatile.Read( ref candidate.QueuedBytes ) ) ?? peer;
			if ( !slowest.Closing )
				Kill( slowest, CoopCloseStatus.TooSlow, "too slow" );
			if ( slowest == peer || peer.Dead )
			{
				Unqueue( peer, bytes.Length );
				return;
			}
		}
		if ( !peer.Outbox.Writer.TryWrite( bytes ) )
			Unqueue( peer, bytes.Length );
	}

	private void Unqueue( Peer peer, int count )
	{
		Interlocked.Add( ref peer.QueuedBytes, -count );
		Interlocked.Add( ref queuedBytes, -count );
	}

	/// <summary>
	/// Ends a connection from the server side: nothing more is queued for it, its queue is dropped, and its writer sends a close with
	/// <paramref name="status"/> after the message it is sending, which the game answers. A connection that has not closed within three seconds (its
	/// game reads nothing) is dropped. A close is never lost the way a dropped connection's reset can be (macOS limits how many resets it sends), so even
	/// a reader cut off for slowness gets one (<see cref="CoopCloseStatus.TooSlow"/>) and resumes at once.
	/// </summary>
	private void Kill( Peer peer, int status, string reason )
	{
		if ( Interlocked.Exchange( ref peer.DeadFlag, 1 ) == 1 )
			return;
		peer.CloseStatus = status;
		peer.CloseReason = reason;
		peer.Outbox.Writer.TryComplete();
		while ( peer.Outbox.Reader.TryRead( out var dropped ) )
		{
			Interlocked.Add( ref peer.QueuedBytes, -dropped.Length );
			Interlocked.Add( ref queuedBytes, -dropped.Length );
		}
		// the members hear it gone now, not once its close has run its course
		Detach( peer );
		try
		{
			// a timer, never a cancel here: a kill runs under the hub's locks, and a cancel could run the connection's own clean-up (which takes them) inline
			peer.Closed.CancelAfter( TimeSpan.FromSeconds( 3 ) );
		}
		catch ( ObjectDisposedException ) { }
	}

	// ---- requests ----

	/// <summary>Runs one request, whose received bytes are <paramref name="bytes"/> (a relay forwards them); false when the connection ends.</summary>
	private bool Handle( Peer peer, CoopMessage request, byte[] bytes )
	{
		switch ( request )
		{
			case CoopHostFrame frame:
				RelayFrame( peer, frame, bytes );
				return true;
			case CoopSend send:
				RelaySend( peer, send, bytes );
				return true;
			case CoopPong:
				return true;
			case CoopSkipFrames skip:
				SkipFrames( peer, skip );
				return true;
			case CoopCreate create:
				Create( peer, create );
				return true;
			case CoopJoin join:
				Join( peer, join );
				return true;
			case CoopLeave leave:
				Leave( peer, leave );
				return true;
			case CoopKick kick:
				Kick( peer, kick );
				return true;
			case CoopInvite invite:
				Invite( peer, invite );
				return true;
			default:
				// a second hello or a server's message
				Kill( peer, CoopCloseStatus.ProtocolError, "unexpected message" );
				return false;
		}
	}

	private void Refuse( Peer peer, CoopMessageType request, CoopReason reason, string text ) =>
		Enqueue( peer, new CoopError( request, reason, CoopProtocol.Clip( text ) ).Encode() );

	/// <summary>Writes the co-op files; a failure (a full disk) is reported and the sessions go on in memory, written again with the next change.</summary>
	private void Persist( Action write )
	{
		try
		{
			write();
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			Warn?.Invoke( $"The co-op sessions could not be stored: {exception.Message}" );
		}
	}

	private void Create( Peer peer, CoopCreate create )
	{
		string title;
		try
		{
			title = OnlineText.RequireText( create.Title.Trim(), CoopProtocol.MaximumTitleLength, false, false, "Title" );
		}
		catch ( InvalidDataException exception )
		{
			Refuse( peer, CoopMessageType.Create, CoopReason.TitleRefused, exception.Message );
			return;
		}
		filter.Apply( title, out var blocked );
		if ( blocked )
		{
			Refuse( peer, CoopMessageType.Create, CoopReason.TitleRefused, "The title contains words blocked by this server's filter." );
			return;
		}
		lock ( registry )
		{
			if ( !Current( peer ) )
				return;
			if ( memberships.ContainsKey( peer.Account.Key ) )
			{
				Refuse( peer, CoopMessageType.Create, CoopReason.InSession, "Leave your shared park first." );
				return;
			}
			if ( sessions.Count >= options.MaximumCoopSessions )
			{
				Refuse( peer, CoopMessageType.Create, CoopReason.ServerFull, "The server holds its most shared parks." );
				return;
			}
			if ( !quotas.Allow( peer.Account.Key, "create", options.CoopCreatesPerHour, TimeSpan.FromHours( 1 ) ) )
			{
				Refuse( peer, CoopMessageType.Create, CoopReason.Rate, "Too many shared parks started; try again later." );
				return;
			}
			var session = new Session( NewSessionId(), NewCode(), title, create.Listed, create.MaximumPlayers, peer.Hello.Build, peer.Hello.Mods );
			lock ( session.Gate )
			{
				var seat = new Seat( session.NextMember++, 0, peer.Account.Key, peer.Account.Name ) { Peer = peer };
				session.Seats.Add( seat );
				session.HostMember = seat.Id;
				session.SeqKnown = true;
				Persist( () => store.Put( session.ToRecord( exact: false ) ) );
				sessions[session.Id] = session;
				codes[session.Code] = session;
				AddMembership( peer.Account.Key, session );
				peer.Session = session;
				peer.MemberId = seat.Id;
				peer.Hosting = true;
				Enqueue( peer, new CoopCreated( session.StateFor( seat.Id ) ).Encode() );
			}
		}
	}

	private void Join( Peer peer, CoopJoin join )
	{
		lock ( registry )
		{
			if ( !Current( peer ) )
				return;
			var key = peer.Account.Key;
			if ( memberships.ContainsKey( key ) )
			{
				Refuse( peer, CoopMessageType.Join, CoopReason.InSession, "Leave your shared park first." );
				return;
			}
			if ( !quotas.Allow( key, "join", options.CoopJoinsPerHour, TimeSpan.FromHours( 1 ) ) || quotas.Count( key, "wrong-code", TimeSpan.FromHours( 1 ) ) >= options.CoopWrongCodesPerHour )
			{
				Refuse( peer, CoopMessageType.Join, CoopReason.Rate, "Too many joins; try again later." );
				return;
			}
			if ( CoopProtocol.NormalizeCode( join.Code ) is not { } code || !codes.TryGetValue( code, out var session ) )
			{
				quotas.Record( key, "wrong-code" );
				Refuse( peer, CoopMessageType.Join, CoopReason.UnknownCode, "No shared park has this code." );
				return;
			}
			lock ( session.Gate )
			{
				if ( session.Kicked.Contains( key ) )
				{
					Refuse( peer, CoopMessageType.Join, CoopReason.Kicked, "The host removed you from this shared park." );
					return;
				}
				if ( session.Seats.Count >= session.MaximumPlayers )
				{
					Refuse( peer, CoopMessageType.Join, CoopReason.Full, "This shared park is full." );
					return;
				}
				// decisions Q11 and Q16: exactly the same release and the same simulation-changing mods, in load order
				if ( Mismatch( session, peer.Hello ) is { } mismatch )
				{
					Refuse( peer, CoopMessageType.Join, mismatch.Reason, mismatch.Text );
					return;
				}
				var slot = (byte)Enumerable.Range( 1, CoopProtocol.MaximumPlayers - 1 ).First( candidate => session.Seats.All( seat => seat.Slot != candidate ) );
				var joined = new Seat( session.NextMember++, slot, key, peer.Account.Name ) { Peer = peer };
				session.Seats.Add( joined );
				Persist( () => store.Put( session.ToRecord( exact: false ) ) );
				AddMembership( key, session );
				peer.Session = session;
				peer.MemberId = joined.Id;
				peer.Hosting = false;
				Enqueue( peer, new CoopJoined( session.StateFor( joined.Id ), session.Build, session.Mods ).Encode() );
				Announce( session, joined, CoopMemberState.Joined );
			}
		}
	}

	/// <summary>What differs between the host's mods and a joiner's, cut to what a refusal carries (<see cref="CoopProtocol.Clip"/>).</summary>
	private static string ModsDifference( IReadOnlyList<CoopMod> host, IReadOnlyList<CoopMod> joiner )
	{
		static string Name( CoopMod mod ) => $"{mod.Id} {mod.Version}";
		var missing = host.Where( mod => !joiner.Contains( mod ) ).Select( Name ).ToList();
		var extra = joiner.Where( mod => !host.Contains( mod ) ).Select( Name ).ToList();
		if ( missing.Count == 0 && extra.Count == 0 )
			return CoopProtocol.Clip( "The host loads the same mods in another order: " + string.Join( ", ", host.Select( Name ) ) + "." );
		return CoopProtocol.Clip( "The host's mods differ from yours" + (missing.Count > 0 ? "; you lack " + string.Join( ", ", missing ) : "")
			+ (extra.Count > 0 ? "; the host lacks " + string.Join( ", ", extra ) : "") + "." );
	}

	private void Leave( Peer peer, CoopLeave leave )
	{
		lock ( registry )
		{
			if ( !Current( peer ) )
				return;
			if ( !memberships.TryGetValue( peer.Account.Key, out var session ) )
			{
				Refuse( peer, CoopMessageType.Leave, CoopReason.NotInSession, "You are in no shared park." );
				return;
			}
			if ( !quotas.Allow( peer.Account.Key, "leave", options.CoopKicksPerHour, TimeSpan.FromHours( 1 ) ) )
			{
				Refuse( peer, CoopMessageType.Leave, CoopReason.Rate, "Too many changes; try again later." );
				return;
			}
			// the reason reaches the other players: it passes the word filter like a title
			var reason = CoopProtocol.Clip( filter.Apply( leave.Reason.Trim() ) );
			lock ( session.Gate )
			{
				var seat = session.Seat( peer.Account.Key )!;
				if ( seat.Id == session.HostMember )
				{
					var text = reason.Length > 0 ? reason : "The host left.";
					// a host that never resumed (a restarted game leaving what it no longer holds) is told too, as the members are
					var attached = seat.Peer == peer;
					End( session, CoopReason.HostLeft, text );
					if ( !attached )
						Enqueue( peer, new CoopEnded( session.Id, CoopReason.HostLeft, text ).Encode() );
					return;
				}
				RemoveSeat( session, seat, CoopMemberState.Left );
				Enqueue( peer, new CoopEnded( session.Id, CoopReason.Left, reason ).Encode() );
			}
		}
	}

	private void Kick( Peer peer, CoopKick kick )
	{
		lock ( registry )
		{
			if ( !Current( peer ) )
				return;
			if ( peer.Session is not { } session || !peer.Hosting )
			{
				Refuse( peer, CoopMessageType.Kick, CoopReason.NotHost, "Only the host removes players." );
				return;
			}
			if ( !quotas.Allow( peer.Account.Key, "kick", options.CoopKicksPerHour, TimeSpan.FromHours( 1 ) ) )
			{
				Refuse( peer, CoopMessageType.Kick, CoopReason.Rate, "Too many changes; try again later." );
				return;
			}
			lock ( session.Gate )
			{
				if ( session.Seat( kick.MemberId ) is not { } seat || seat.Id == session.HostMember )
				{
					Refuse( peer, CoopMessageType.Kick, CoopReason.NotMember, "No such player in your shared park." );
					return;
				}
				session.Kicked.Add( seat.AccountKey );
				var target = seat.Peer;
				RemoveSeat( session, seat, CoopMemberState.Kicked );
				if ( target != null )
					Enqueue( target, new CoopEnded( session.Id, CoopReason.Kicked, "The host removed you from the shared park." ).Encode() );
				endedSessions[seat.AccountKey] = (session.Id, CoopReason.Kicked, "The host removed you from the shared park.", clock());
			}
		}
	}

	private void Invite( Peer peer, CoopInvite invite )
	{
		lock ( registry )
		{
			if ( !Current( peer ) )
				return;
			// every refusal names the invited player, which tells the host's game whose invitation it answers (CoopProtocol.InviteRefusalText)
			if ( peer.Session is not { } session || !peer.Hosting )
			{
				Refuse( peer, CoopMessageType.Invite, CoopReason.NotHost, CoopProtocol.InviteRefusalText( CoopReason.NotHost, invite.Player ) );
				return;
			}
			if ( !quotas.Allow( peer.Account.Key, "invite", options.CoopInvitesPerHour, TimeSpan.FromHours( 1 ) ) )
			{
				Refuse( peer, CoopMessageType.Invite, CoopReason.Rate, CoopProtocol.InviteRefusalText( CoopReason.Rate, invite.Player ) );
				return;
			}
			var target = OnlineText.IsValidPlayerName( invite.Player ) ? accounts.FindAccount( invite.Player ) : null;
			if ( target == null || target.Key == peer.Account.Key || !peers.TryGetValue( target.Key, out var invited ) )
			{
				Refuse( peer, CoopMessageType.Invite, CoopReason.NotOnline, CoopProtocol.InviteRefusalText( CoopReason.NotOnline, invite.Player ) );
				return;
			}
			Enqueue( invited, new CoopInvited( session.Id, session.Code, session.Title, peer.Account.Name ).Encode() );
		}
	}

	/// <summary>
	/// The host's frame: relayed to every other connected member when it comes from the host's current connection and follows the last frame. Any other
	/// number is dropped and the host told which one comes next (<see cref="CoopFrameRefused"/>), so no member ever sees a frame out of order. The relay
	/// forwards the received bytes with the frame's server type (the two layouts match).
	/// </summary>
	private void RelayFrame( Peer peer, CoopHostFrame frame, byte[] bytes )
	{
		if ( peer.Session is not { } session )
		{
			Refuse( peer, CoopMessageType.HostFrame, CoopReason.NotInSession, "You are in no shared park." );
			return;
		}
		lock ( session.Gate )
		{
			// a stopping server relays nothing more: the frame position it stored stays the last frame the guests have
			if ( restarting || session.Ended || session.Seat( peer.Account.Key ) is not { } seat || seat.Peer != peer )
				return;
			if ( seat.Id != session.HostMember )
			{
				Refuse( peer, CoopMessageType.HostFrame, CoopReason.NotHost, "Only the host sends frames." );
				return;
			}
			// after a crash the stored position may be behind: the host's first frame after it sets it
			if ( session.SeqKnown ? frame.Seq != session.LastSeq + 1 : frame.Seq <= session.LastSeq )
			{
				Enqueue( peer, new CoopFrameRefused( session.LastSeq + 1 ).Encode() );
				return;
			}
			session.LastSeq = frame.Seq;
			session.SeqKnown = true;
			bytes[0] = (byte)CoopMessageType.Frame;
			ReadOnlyMemory<byte> relayed = bytes;
			foreach ( var other in session.Seats )
			{
				if ( other.Peer != null && other != seat )
					Enqueue( other.Peer, relayed );
			}
		}
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The host's skip: it no longer holds the frames this server needs, and every member is assigned a capture past them, so the sequence goes on at the
	/// frame the skip names. A skip never goes back: one to a frame already relayed is answered with the frame the server takes next, as a frame out of turn is.
	/// </summary>
	private void SkipFrames( Peer peer, CoopSkipFrames skip )
	{
		if ( peer.Session is not { } session )
		{
			Refuse( peer, CoopMessageType.SkipFrames, CoopReason.NotInSession, "You are in no shared park." );
			return;
		}
		lock ( session.Gate )
		{
			if ( restarting || session.Ended || session.Seat( peer.Account.Key ) is not { } seat || seat.Peer != peer )
				return;
			if ( seat.Id != session.HostMember )
			{
				Refuse( peer, CoopMessageType.SkipFrames, CoopReason.NotHost, "Only the host skips frames." );
				return;
			}
			if ( skip.From <= session.LastSeq )
			{
				Enqueue( peer, new CoopFrameRefused( session.LastSeq + 1 ).Encode() );
				return;
			}
			session.LastSeq = skip.From - 1;
			session.SeqKnown = true;
		}
	}

	/// <summary>A member's message, forwarded as received with the server's type and the sender's member id in place of the addressee (the layouts match).</summary>
	private void RelaySend( Peer peer, CoopSend send, byte[] bytes )
	{
		if ( peer.Session is not { } session )
		{
			Refuse( peer, CoopMessageType.Send, CoopReason.NotInSession, "You are in no shared park." );
			return;
		}
		lock ( session.Gate )
		{
			if ( restarting || session.Ended || session.Seat( peer.Account.Key ) is not { } seat || seat.Peer != peer )
				return;
			var hosting = seat.Id == session.HostMember;
			// C2: guests talk to the host only
			if ( !hosting && send.To != session.HostMember )
			{
				Refuse( peer, CoopMessageType.Send, CoopReason.NotHost, "A guest sends to the host." );
				return;
			}
			bytes[0] = (byte)CoopMessageType.Message;
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 1, 4 ), seat.Id );
			ReadOnlyMemory<byte> relayed = bytes;
			if ( send.To == CoopProtocol.EveryOtherMember )
			{
				foreach ( var other in session.Seats )
				{
					if ( other.Peer != null && other != seat )
						Enqueue( other.Peer, relayed );
				}
				return;
			}
			if ( session.Seat( send.To ) is not { } target || target == seat )
			{
				Refuse( peer, CoopMessageType.Send, CoopReason.NotMember, $"Member {send.To} is not in your shared park." );
				return;
			}
			if ( target.Peer == null )
			{
				Refuse( peer, CoopMessageType.Send, CoopReason.NotConnected, $"{target.Name} is not connected." );
				return;
			}
			Enqueue( target.Peer, relayed );
		}
	}

	/// <summary>
	/// Whether <paramref name="peer"/> is still its account's connection (a replaced one's late requests do nothing) and the server is not stopping (a request the stop overtook
	/// changes nothing, so the positions and memberships it stored stay the last). Under the registry lock.
	/// </summary>
	private bool Current( Peer peer ) => !restarting && !peer.Dead && peers.TryGetValue( peer.Account.Key, out var current ) && current == peer;

	// ---- membership ----

	/// <summary>Tells every other connected member of the session about <paramref name="seat"/>. Under the session's gate.</summary>
	private void Announce( Session session, Seat seat, CoopMemberState state )
	{
		ReadOnlyMemory<byte> message = new CoopMemberChanged( seat.Info, state ).Encode();
		foreach ( var other in session.Seats )
		{
			if ( other.Peer != null && other != seat )
				Enqueue( other.Peer, message );
		}
	}

	/// <inheritdoc/>
	public event Action<string, string>? MemberGone;

	/// <inheritdoc/>
	public bool IsMember( string accountKey, string sessionId ) => memberSessions.TryGetValue( accountKey, out var id ) && id == sessionId;

	/// <summary>[EXT:online-coop-session] <paramref name="accountKey"/> holds a seat in <paramref name="session"/>. Under the registry lock.</summary>
	private void AddMembership( string accountKey, Session session )
	{
		memberships[accountKey] = session;
		memberSessions[accountKey] = session.Id;
	}

	/// <summary>
	/// [EXT:online-coop-session] <paramref name="accountKey"/>'s seat in <paramref name="session"/> is gone: it is no member from here on, and then the chat hears of it
	/// (<see cref="MemberGone"/>). A listener's failure is reported, never thrown into the change. Under the registry lock and the session's gate.
	/// </summary>
	private void RemoveMembership( string accountKey, Session session )
	{
		memberships.Remove( accountKey );
		memberSessions.TryRemove( accountKey, out _ );
		foreach ( var listener in MemberGone?.GetInvocationList() ?? Array.Empty<Delegate>() )
		{
			try { ((Action<string, string>)listener)( accountKey, session.Id ); }
			catch ( Exception exception ) when ( exception is not OutOfMemoryException ) { Warn?.Invoke( $"A co-op membership listener failed: {exception.Message}" ); }
		}
	}

	/// <summary>A guest leaves or is kicked: its seat goes, the change is stored, then announced. Under the registry lock and the session's gate.</summary>
	private void RemoveSeat( Session session, Seat seat, CoopMemberState state )
	{
		ReadOnlyMemory<byte> message = new CoopMemberChanged( seat.Info with { Connected = false }, state ).Encode();
		session.Seats.Remove( seat );
		// while the server stops the frame position can no longer move, so the stored one stays exact (an account deleted during the stop)
		Persist( () => store.Put( session.ToRecord( exact: restarting && session.SeqKnown ) ) );
		RemoveMembership( seat.AccountKey, session );
		if ( seat.Peer != null )
		{
			seat.Peer.Session = null;
			seat.Peer.Hosting = false;
		}
		foreach ( var other in session.Seats )
		{
			if ( other.Peer != null )
				Enqueue( other.Peer, message );
		}
	}

	/// <summary>Ends a session for everyone: each connected member hears why, and the session, its code and its snapshots go. Under the registry lock and the gate.</summary>
	private void End( Session session, CoopReason reason, string text )
	{
		// encoded first: nothing changes unless the message can be sent
		text = CoopProtocol.Clip( text );
		ReadOnlyMemory<byte> message = new CoopEnded( session.Id, reason, text ).Encode();
		session.Ended = true;
		sessions.Remove( session.Id );
		codes.Remove( session.Code );
		// stored (and its snapshots gone) before anyone hears of it
		Persist( () => store.Remove( session.Id ) );
		var now = clock();
		foreach ( var seat in session.Seats )
		{
			RemoveMembership( seat.AccountKey, session );
			endedSessions[seat.AccountKey] = (session.Id, reason, text, now);
			if ( seat.Peer is { } peer )
			{
				peer.Session = null;
				peer.Hosting = false;
				Enqueue( peer, message );
			}
		}
	}

	/// <summary>
	/// The account is deleted: its co-op connection ends, it leaves its session, and a session it hosts ends, before its name is free for a new account.
	/// </summary>
	public void RemoveAccount( string accountKey )
	{
		lock ( registry )
		{
			if ( memberships.TryGetValue( accountKey, out var session ) )
			{
				lock ( session.Gate )
				{
					var seat = session.Seat( accountKey )!;
					if ( seat.Id == session.HostMember )
						End( session, CoopReason.AccountDeleted, "The host's account was deleted." );
					else
						RemoveSeat( session, seat, CoopMemberState.Left );
				}
			}
			endedSessions.Remove( accountKey );
			if ( peers.TryGetValue( accountKey, out var peer ) )
				Fence( peer, (int)WebSocketCloseStatus.PolicyViolation, "account deleted" );
		}
	}

	/// <summary>Ends an account's co-op connection (a recovery revoked its logins); its membership stays until it resumes or the session ends.</summary>
	public void Disconnect( string accountKey )
	{
		lock ( registry )
		{
			if ( peers.TryGetValue( accountKey, out var peer ) )
				Fence( peer, (int)WebSocketCloseStatus.PolicyViolation, "logged out" );
		}
	}

	/// <summary>
	/// Ends the sessions whose host has been away longer than <see cref="HostAwayLimit"/> at <paramref name="now"/> and forgets old end reasons and quotas.
	/// The server runs it every minute; tests call it with a later time.
	/// </summary>
	public void Sweep( DateTimeOffset now )
	{
		lock ( registry )
		{
			foreach ( var session in sessions.Values.ToList() )
			{
				lock ( session.Gate )
				{
					if ( session.HostAwaySince is { } away && now - away >= HostAwayLimit )
						End( session, CoopReason.Expired, "The host did not come back." );
				}
			}
			foreach ( var key in endedSessions.Where( pair => now - pair.Value.At >= EndedMemory ).Select( pair => pair.Key ).ToList() )
				endedSessions.Remove( key );
			// an account without a connection whose buckets have filled again needs none
			foreach ( var key in limits.Where( pair => !peers.ContainsKey( pair.Key ) && pair.Value.Full ).Select( pair => pair.Key ).ToList() )
				limits.Remove( key );
		}
		quotas.Prune( now );
	}

	/// <summary>Ends the connections whose login no longer holds (a logout elsewhere, an expiry, a ban); the server runs it every minute.</summary>
	public void CheckLogins()
	{
		lock ( registry )
		{
			foreach ( var peer in peers.Values.ToList() )
			{
				if ( accounts.Authenticate( peer.Token ) is not { } account || account.Key != peer.Account.Key )
					Fence( peer, (int)WebSocketCloseStatus.PolicyViolation, "logged out" );
			}
		}
	}

	/// <summary>
	/// The server is stopping (a deploy): every session's frame position is stored exactly, then each co-op socket closes with
	/// <see cref="CoopCloseStatus.Restarting"/> after its queue, and the games resume on the new server.
	/// </summary>
	public void Restarting()
	{
		// from here on no request is handled and nothing is relayed, so the positions stored below stay the last frames every guest has
		restarting = true;
		List<Peer> open;
		lock ( registry )
		{
			var records = new List<CoopSessionRecord>();
			foreach ( var session in sessions.Values )
			{
				lock ( session.Gate )
					records.Add( session.ToRecord( exact: session.SeqKnown ) );
			}
			// under the registry lock, so no membership change stored meanwhile is overwritten with an older record; with no session there is nothing to
			// keep, unless an earlier write failed and the file still holds a session that ended
			if ( records.Count > 0 || store.Unsaved )
				Persist( () => store.PutAll( records ) );
			open = peers.Values.ToList();
			// every queue stops taking messages before any closes, and before anything waiting on the registry runs: a host's socket closes first (its queue is short)
			// and its departure is announced, and that message must not cut off a guest whose queue is at its limit, which would drop the frames whose position was
			// just stored
			foreach ( var peer in open )
				peer.Closing = true;
		}
		foreach ( var peer in open )
		{
			peer.Outbox.Writer.TryComplete();
			try { peer.Closed.CancelAfter( TimeSpan.FromSeconds( 3 ) ); }
			catch ( ObjectDisposedException ) { }
		}
	}

	// ---- the open list (decision Q4) ----

	/// <summary>The listed sessions a player can join: not full, their host connected.</summary>
	public IReadOnlyList<CoopListedSession> Listed()
	{
		var listed = new List<CoopListedSession>();
		lock ( registry )
		{
			foreach ( var session in sessions.Values )
			{
				lock ( session.Gate )
				{
					if ( session.Listed && session.Seats.Count < session.MaximumPlayers && session.Seat( session.HostMember ) is { Peer: not null } host )
						listed.Add( new CoopListedSession( session.Code, session.Title, host.Name, session.Seats.Count, session.MaximumPlayers, session.Build ) );
				}
			}
		}
		return listed.OrderBy( item => item.Title, StringComparer.OrdinalIgnoreCase ).ThenBy( item => item.Code, StringComparer.Ordinal ).ToList();
	}

	// ---- join snapshots ([EXT:online-coop-snapshot-join]) ----

	/// <summary>An upload in progress: the space it holds in the store's cap is given back once (<see cref="FinishUpload"/>).</summary>
	public sealed class Upload( string sessionId )
	{
		public string SessionId { get; } = sessionId;
		internal bool Finished;
	}

	/// <summary>
	/// Starts the host's snapshot upload for session <paramref name="id"/>: only its host, only while another member is in it, one at a time, and within the
	/// server's snapshot space, which counts the room every upload in progress may still take.
	/// </summary>
	public Upload BeginUpload( AccountRecord user, string id )
	{
		lock ( registry )
		{
			if ( !sessions.TryGetValue( id, out var session ) )
				throw new StoreException( StatusCodes.Status404NotFound, "No such shared park." );
			lock ( session.Gate )
			{
				if ( session.Seat( user.Key ) is not { } seat || seat.Id != session.HostMember )
					throw new StoreException( StatusCodes.Status403Forbidden, "Only the host uploads the park." );
				if ( session.Seats.Count < 2 )
					throw new StoreException( StatusCodes.Status409Conflict, "Nobody is joining." );
				if ( session.Uploading )
					throw new StoreException( StatusCodes.Status409Conflict, "An upload is running." );
				if ( store.BlobBytes() + reservedUploadBytes + options.MaximumCoopSnapshotBytes > options.MaximumCoopStoreBytes )
					throw new StoreException( StatusCodes.Status507InsufficientStorage, "The server has no room for the park now." );
				// [EXT:online-coop-snapshot-join] the session's own limit, counted only for its authenticated host with room for the upload, so nobody else and no full store can spend it
				if ( !quotas.Allow( session.Id, "snapshot", options.CoopSnapshotsPerSessionPerHour, TimeSpan.FromHours( 1 ) ) )
					throw new StoreException( StatusCodes.Status429TooManyRequests, "Too many join snapshots for this shared park; try again later." )
					{
						RetryAfter = quotas.Wait( session.Id, "snapshot", TimeSpan.FromHours( 1 ) )
					};
				reservedUploadBytes += options.MaximumCoopSnapshotBytes;
				session.Uploading = true;
				return new Upload( id );
			}
		}
	}

	/// <summary>
	/// Ends an upload, once (a second call does nothing): a received snapshot is kept under its digest while the session lives (else deleted); the newest two
	/// stay.
	/// </summary>
	public void FinishUpload( Upload upload, string? temporary, string? sha256 )
	{
		lock ( registry )
		{
			if ( upload.Finished )
				return;
			upload.Finished = true;
			reservedUploadBytes -= options.MaximumCoopSnapshotBytes;
			sessions.TryGetValue( upload.SessionId, out var session );
			if ( session == null )
			{
				if ( temporary != null )
					File.Delete( temporary );
				return;
			}
			lock ( session.Gate )
			{
				session.Uploading = false;
				if ( temporary == null || sha256 == null )
					return;
				if ( session.Ended )
					File.Delete( temporary );
				else
					store.Commit( upload.SessionId, temporary, sha256, KeptSnapshots );
			}
		}
	}

	/// <summary>
	/// The file of snapshot <paramref name="sha256"/> of session <paramref name="id"/> for one of its members, or null when it is gone. A download that
	/// <paramref name="counts"/> (one from the start, not a resumed range) takes one of the member's downloads an hour.
	/// </summary>
	public string? FindSnapshot( AccountRecord user, string id, string sha256, bool counts )
	{
		lock ( registry )
		{
			if ( !sessions.TryGetValue( id, out var session ) )
				return null;
			lock ( session.Gate )
			{
				if ( session.Seat( user.Key ) == null )
					throw new StoreException( StatusCodes.Status403Forbidden, "Only its players download the park." );
			}
		}
		var path = store.BlobPath( id, sha256 );
		if ( !File.Exists( path ) )
			return null;
		if ( counts && !quotas.Allow( user.Key, "download", options.CoopDownloadsPerHour, TimeSpan.FromHours( 1 ) ) )
			throw new StoreException( StatusCodes.Status429TooManyRequests, "Too many downloads; try again later." )
			{
				// [EXT:online-coop-snapshot-join] a joiner waits as long as it says, or gives up the join when that is too long
				RetryAfter = quotas.Wait( user.Key, "download", TimeSpan.FromHours( 1 ) )
			};
		return path;
	}

	// ---- helpers ----

	private static string NewSessionId() => Convert.ToHexStringLower( RandomNumberGenerator.GetBytes( CoopProtocol.SessionIdLength / 2 ) );

	/// <summary>A code no live session has. Under the registry lock.</summary>
	private string NewCode()
	{
		while ( true )
		{
			var code = new string( Enumerable.Range( 0, CoopProtocol.CodeLength )
				.Select( _ => CoopProtocol.CodeAlphabet[RandomNumberGenerator.GetInt32( CoopProtocol.CodeAlphabet.Length )] ).ToArray() );
			if ( !codes.ContainsKey( code ) )
				return code;
		}
	}

	private static async Task SendDirectAsync( WebSocket socket, CoopMessage message, CancellationToken cancel )
	{
		try
		{
			await socket.SendAsync( message.Encode(), WebSocketMessageType.Binary, true, cancel );
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException ) { }
	}

	private static Task CloseAsync( WebSocket socket, int status, string reason ) => ChatHub.CloseAsync( socket, (WebSocketCloseStatus)status, reason );

	/// <summary>A connection: its socket, account, login token and hello, its queue, its session seat, and its account's relay buckets.</summary>
	private sealed class Peer( WebSocket socket, AccountRecord account, string token, CoopHello hello ) : IDisposable
	{
		public WebSocket Socket { get; } = socket;
		public AccountRecord Account { get; } = account;
		public string Token { get; } = token;
		public CoopHello Hello { get; } = hello;
		/// <summary>The writer reads the queue, and so does a kill that drops it: two readers.</summary>
		public readonly Channel<ReadOnlyMemory<byte>> Outbox = Channel.CreateUnbounded<ReadOnlyMemory<byte>>( new UnboundedChannelOptions { SingleReader = false } );
		public long QueuedBytes;
		public int DeadFlag;
		public bool Dead => Volatile.Read( ref DeadFlag ) == 1;
		/// <summary>
		/// A restart closed the queue after storing the frame positions (<see cref="Restarting"/>): nothing more is queued or counted for it, so no limit cuts it off and
		/// drops the frames whose position was stored.
		/// </summary>
		public volatile bool Closing;
		public int? CloseStatus;
		public string? CloseReason;
		public readonly CancellationTokenSource Closed = new();
		/// <summary>The session this connection holds a seat of, set under the registry lock and the session's gate.</summary>
		public volatile Session? Session;
		public uint MemberId;
		public volatile bool Hosting;
		/// <summary>The account's relay buckets, set when the connection registers.</summary>
		public AccountLimits? Buckets;

		public void Dispose() => Closed.Dispose();
	}

	/// <summary>
	/// An account's relay buckets: messages and bytes a second with a two-second burst, a host's higher than a guest's, kept over its reconnects. They refill
	/// as they are used, without a timer.
	/// </summary>
	private sealed class AccountLimits( ServerOptions options )
	{
		// a byte burst holds at least one whole message
		private readonly TokenBucket guestMessages = new( options.CoopGuestMessagesPerSecond );
		private readonly TokenBucket guestBytes = new( Math.Max( options.CoopGuestBytesPerSecond, CoopProtocol.MaximumMessageBytes / 2 ) );
		private readonly TokenBucket hostMessages = new( options.CoopHostMessagesPerSecond );
		private readonly TokenBucket hostBytes = new( Math.Max( options.CoopHostBytesPerSecond, CoopProtocol.MaximumMessageBytes / 2 ) );

		/// <summary>Takes one message of <paramref name="bytes"/> from the host's or the guest's buckets; false when either is empty.</summary>
		public bool Take( bool hosting, int bytes )
		{
			var (messages, volume) = hosting ? (hostMessages, hostBytes) : (guestMessages, guestBytes);
			lock ( this )
			{
				var now = System.Diagnostics.Stopwatch.GetTimestamp();
				messages.Refill( now );
				volume.Refill( now );
				var cost = Math.Max( 1, bytes );
				if ( messages.Tokens < 1 || volume.Tokens < cost )
					return false;
				messages.Tokens -= 1;
				volume.Tokens -= cost;
				return true;
			}
		}

		/// <summary>Whether every bucket has filled again (an idle account's buckets can go).</summary>
		public bool Full
		{
			get
			{
				lock ( this )
				{
					var now = System.Diagnostics.Stopwatch.GetTimestamp();
					foreach ( var bucket in new[] { guestMessages, guestBytes, hostMessages, hostBytes } )
					{
						bucket.Refill( now );
						if ( !bucket.IsFull )
							return false;
					}
					return true;
				}
			}
		}
	}

	/// <summary>A bucket of <paramref name="perSecond"/> tokens a second that holds two seconds' worth; it starts full.</summary>
	private sealed class TokenBucket( int perSecond )
	{
		private readonly double capacity = perSecond * 2.0;
		private long last = System.Diagnostics.Stopwatch.GetTimestamp();
		public double Tokens = perSecond * 2.0;

		public bool IsFull => Tokens >= capacity;

		public void Refill( long now )
		{
			Tokens = Math.Min( capacity, Tokens + (now - last) * (double)perSecond / System.Diagnostics.Stopwatch.Frequency );
			last = now;
		}
	}

	/// <summary>A message over <see cref="CoopProtocol.MaximumMessageBytes"/>: the connection closes with 1009.</summary>
	private sealed class MessageTooLargeException() : Exception( "The co-op message is too large." )
	{
	}

	private sealed class Seat( uint id, byte slot, string accountKey, string name )
	{
		public uint Id { get; } = id;
		public byte Slot { get; } = slot;
		public string AccountKey { get; } = accountKey;
		public string Name { get; } = name;
		/// <summary>The member's connection while it is connected. Under the session's gate.</summary>
		public Peer? Peer { get; set; }
		public CoopMemberInfo Info => new( Id, Slot, Name, Peer != null );
	}

	private sealed class Session( string id, string code, string title, bool listed, byte maximumPlayers, string build, IReadOnlyList<CoopMod> mods )
	{
		public readonly object Gate = new();
		public string Id { get; } = id;
		public string Code { get; } = code;
		public string Title { get; } = title;
		public bool Listed { get; } = listed;
		public byte MaximumPlayers { get; } = maximumPlayers;
		public string Build { get; } = build;
		public IReadOnlyList<CoopMod> Mods { get; } = mods;
		public uint HostMember;
		public uint NextMember = 1;
		public ulong LastSeq;
		/// <summary>Whether <see cref="LastSeq"/> is the host's last frame (false after a start whose store was not written at shutdown).</summary>
		public bool SeqKnown;
		public readonly List<Seat> Seats = new();
		public readonly HashSet<string> Kicked = new();
		public DateTimeOffset? HostAwaySince;
		public bool Ended;
		public bool Uploading;

		public Seat? Seat( string accountKey ) => Seats.FirstOrDefault( seat => seat.AccountKey == accountKey );
		public Seat? Seat( uint memberId ) => Seats.FirstOrDefault( seat => seat.Id == memberId );

		public CoopSessionState StateFor( uint you ) => new( Id, Code, Title, Listed, MaximumPlayers, you, HostMember, LastSeq, SeqKnown,
			Seats.OrderBy( seat => seat.Slot ).Select( seat => seat.Info ).ToList() );

		public CoopSessionRecord ToRecord( bool exact ) => new( Id, Code, Title, Listed, MaximumPlayers, Build, Mods, HostMember, NextMember, LastSeq,
			Seats.Select( seat => new CoopMemberRecord( seat.Id, seat.Slot, seat.AccountKey, seat.Name ) ).ToList(), Kicked.ToList() ) { SeqExact = exact };

		public static Session FromRecord( CoopSessionRecord record, DateTimeOffset started )
		{
			var session = new Session( record.Id, record.Code, record.Title, record.Listed, record.MaximumPlayers, record.Build, record.Mods )
			{
				HostMember = record.HostMember,
				NextMember = record.NextMember,
				LastSeq = record.LastFrameSeq,
				SeqKnown = record.SeqExact,
				HostAwaySince = started
			};
			foreach ( var member in record.Members )
				session.Seats.Add( new Seat( member.Id, member.Slot, member.AccountKey, member.Name ) );
			foreach ( var kicked in record.Kicked )
				session.Kicked.Add( kicked );
			return session;
		}
	}

	/// <summary>Per-account counts of requests in a sliding window (memory only).</summary>
	private sealed class Quotas( Func<DateTimeOffset> clock )
	{
		private readonly Dictionary<(string Key, string Kind), Queue<DateTimeOffset>> events = new();

		/// <summary>Counts one request of <paramref name="kind"/> when fewer than <paramref name="limit"/> fell in <paramref name="window"/>.</summary>
		public bool Allow( string key, string kind, int limit, TimeSpan window )
		{
			lock ( events )
			{
				var queue = Queue( key, kind, window );
				if ( queue.Count >= limit )
					return false;
				queue.Enqueue( clock() );
				return true;
			}
		}

		public void Record( string key, string kind )
		{
			lock ( events )
				Queue( key, kind, TimeSpan.MaxValue ).Enqueue( clock() );
		}

		public int Count( string key, string kind, TimeSpan window )
		{
			lock ( events )
				return Queue( key, kind, window ).Count;
		}

		/// <summary>How long until the oldest counted request of <paramref name="kind"/> leaves <paramref name="window"/>: when a refused one may be tried again.</summary>
		public TimeSpan Wait( string key, string kind, TimeSpan window )
		{
			lock ( events )
			{
				var queue = Queue( key, kind, window );
				return queue.Count == 0 ? TimeSpan.Zero : queue.Peek() + window - clock();
			}
		}

		private Queue<DateTimeOffset> Queue( string key, string kind, TimeSpan window )
		{
			if ( !events.TryGetValue( (key, kind), out var queue ) )
				events[(key, kind)] = queue = new Queue<DateTimeOffset>();
			if ( window != TimeSpan.MaxValue )
			{
				var now = clock();
				while ( queue.Count > 0 && now - queue.Peek() >= window )
					queue.Dequeue();
			}
			return queue;
		}

		public void Prune( DateTimeOffset now )
		{
			lock ( events )
			{
				foreach ( var pair in events.ToList() )
				{
					while ( pair.Value.Count > 0 && now - pair.Value.Peek() >= TimeSpan.FromHours( 1 ) )
						pair.Value.Dequeue();
					if ( pair.Value.Count == 0 )
						events.Remove( pair.Key );
				}
			}
		}
	}
}
