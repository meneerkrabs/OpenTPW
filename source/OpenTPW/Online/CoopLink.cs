using System.Net.WebSockets;
using System.Reflection;
using System.Threading.Channels;
using OpenTPW.Mods;
using OpenTPW.Online.Api;
using OpenTPW.Online.Client;
using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>What the co-op link is doing (<see cref="CoopLink.State"/>).</summary>
public enum CoopLinkState
{
	/// <summary>Not open: never opened, closed, or stopped (<see cref="CoopLink.Stopped"/> says why).</summary>
	Closed,
	/// <summary>Opening the socket and waiting for the server's welcome (and trying again while the first one fails).</summary>
	Opening,
	/// <summary>Welcomed: requests go out.</summary>
	Open,
	/// <summary>The socket was lost; the link opens a new one and resumes its session, for <see cref="CoopLink.ReconnectWindow"/>.</summary>
	Reconnecting
}

/// <summary>Why the co-op link stopped on its own (<see cref="CoopLink.Stopped"/>).</summary>
public enum CoopStop
{
	None,
	/// <summary>Another game of the account holds the co-op socket (close 4003); opening with a take-over replaces it.</summary>
	InUseElsewhere,
	/// <summary>Another game of the account took the co-op socket over (close 4001).</summary>
	Replaced,
	/// <summary>The server is older than protocol 6 and has no shared parks.</summary>
	ServerTooOld,
	/// <summary>The server closed the link for good: a login, protocol or size refusal, or a close the link does not resume.</summary>
	Closed,
	/// <summary>No socket could be opened again within <see cref="CoopLink.ReconnectWindow"/>.</summary>
	Lost
}

/// <summary>
/// [EXT:online-coop-session] The game's link to the co-op socket of shared parks (docs/ONLINE.md, "Shared parks"), under an <see cref="OnlineSession"/> and its
/// login: a background reader and writer per socket, a reconnect after a lost socket that resumes the session, a silence watchdog, and the requests and
/// events the shared park uses. Every request and event is on the game thread; events are raised from <see cref="OnlineSession.Pump"/>.
/// </summary>
public sealed class CoopLink
{
	/// <summary>Waits before each reconnect attempt after a lost socket; the last wait repeats until <see cref="ReconnectWindow"/> has passed.</summary>
	internal static readonly TimeSpan[] DefaultRetryDelays =
	{
		TimeSpan.FromSeconds( 1 ), TimeSpan.FromSeconds( 2 ), TimeSpan.FromSeconds( 4 ), TimeSpan.FromSeconds( 8 ), TimeSpan.FromSeconds( 15 ), TimeSpan.FromSeconds( 30 )
	};

	/// <summary>A link that hears nothing for this long drops its socket and resumes (the server pings after 15 seconds of quiet; a browser never sees WebSocket pings).</summary>
	internal static readonly TimeSpan DefaultSilence = TimeSpan.FromSeconds( 45 );

	/// <summary>How long a lost link keeps trying: the server ends a session whose host has been away this long.</summary>
	internal static readonly TimeSpan DefaultReconnectWindow = TimeSpan.FromMinutes( 10 );

	/// <summary>The bytes queued for a socket at most; a socket that falls this far behind is dropped and resumed (a host resends its frames after a resume).</summary>
	internal const long MaximumQueuedBytes = 8 * 1024 * 1024;

	/// <summary>The random id of this game process, sent in every hello: a reconnect of this process replaces its own old socket, another process is refused.</summary>
	internal static readonly Guid ProcessInstance = Guid.NewGuid();

	private static readonly byte[] Pong = new CoopPong().Encode();
	private readonly OnlineSession owner;
	/// <summary>The socket in use.</summary>
	private Socket? live;
	/// <summary>Counts opens, closes and attempts: what an older one posts back is dropped.</summary>
	private int epoch;
	/// <summary>When the socket was lost (<see cref="Environment.TickCount64"/>), while reconnecting.</summary>
	private long? lostAt;
	/// <summary>Attempts since the socket was lost, or since <see cref="Open"/> while none has been welcomed.</summary>
	private int attempt;
	/// <summary>The server's protocol was asked after a failed first attempt (an older server has no co-op socket).</summary>
	private bool protocolAsked;

	internal CoopLink( OnlineSession owner ) => this.owner = owner;

	/// <summary>This game process's id in its hellos (tests give each link of a process its own).</summary>
	internal Guid Instance { get; set; } = ProcessInstance;
	internal TimeSpan[] RetryDelays { get; set; } = DefaultRetryDelays;
	internal TimeSpan Silence { get; set; } = DefaultSilence;
	internal TimeSpan ReconnectWindow { get; set; } = DefaultReconnectWindow;
	/// <summary>The build and mods a hello names (<see cref="CurrentIdentity"/>; tests give their own).</summary>
	internal Func<(string Build, IReadOnlyList<CoopMod> Mods)> Identity { get; set; } = CurrentIdentity;
	/// <summary>Sockets the link has opened (tests: a reconnect).</summary>
	internal int Connections { get; private set; }

	/// <summary>Drops the socket without a close, as a lost network does (tests).</summary>
	internal void DropSocket() => live?.Abort();

	public CoopLinkState State { get; private set; }
	/// <summary>Why the link stopped on its own; <see cref="CoopStop.None"/> while it runs or after <see cref="Close"/>.</summary>
	public CoopStop Stopped { get; private set; }
	/// <summary>The session this game is in, as the server last told it; null outside a session.</summary>
	public CoopSessionState? Session { get; private set; }
	public bool IsHost => Session is { } session && session.You == session.Host;
	/// <summary>The joined session's host build and mods (they match this game's, or the join was refused); null for a host.</summary>
	public string? HostBuild { get; private set; }
	public IReadOnlyList<CoopMod>? HostMods { get; private set; }
	/// <summary>The server's protocol, from its welcome.</summary>
	public int? ServerProtocol { get; private set; }
	/// <summary>The newest frame of the session this game holds: the last one it sent as the host or received as a guest. A resume reports it.</summary>
	public ulong LastFrameSeq { get; private set; }

	/// <summary>This game created a session (it hosts it) or joined one.</summary>
	public event Action<CoopSessionState>? SessionStarted;
	/// <summary>A lost socket is back and the session with it; a host resends its frames after the state's <see cref="CoopSessionState.LastFrameSeq"/>.</summary>
	public event Action<CoopSessionState>? Resumed;
	public event Action<CoopMemberChanged>? MemberChanged;
	/// <summary>This game is no longer in its session: kicked, the host left, it expired while this game was away, or the link stopped.</summary>
	public event Action<CoopEnded>? SessionEnded;
	/// <summary>A frame of the host's command stream (a guest).</summary>
	public event Action<CoopFrame>? FrameReceived;
	public event Action<CoopRelayed>? MessageReceived;
	public event Action<CoopInvited>? Invited;
	/// <summary>The server refused a request (<see cref="CoopError.Request"/>), with a reason that names what differs.</summary>
	public event Action<CoopError>? Refused;
	public event Action<CoopLinkState>? StateChanged;

	/// <summary>
	/// This game's build (the informational version: exactly the same release for everyone in a session, decision Q11) and its simulation-changing mods in load
	/// order, the used-only extensions included whether or not a park uses them (decision Q16; <see cref="ParkResumeMods.RecordableMods"/>).
	/// </summary>
	internal static (string Build, IReadOnlyList<CoopMod> Mods) CurrentIdentity() =>
		(typeof( CoopLink ).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
			ParkResumeMods.RecordableMods( OpenTpwExtensions.Active ).Select( mod => new CoopMod( mod.Id, mod.Version.ToString(), mod.ContentSha256 ) ).ToArray());

	/// <summary>
	/// Opens the link: a socket and the hello, then requests go out once the server welcomes it. Nothing happens while it is open or on its way.
	/// <paramref name="takeOver"/> replaces another game of the account that holds the co-op socket (an explicit choice after <see cref="CoopStop.InUseElsewhere"/>).
	/// </summary>
	public void Open( bool takeOver = false )
	{
		if ( State != CoopLinkState.Closed )
			return;
		Stopped = CoopStop.None;
		if ( owner.ServerProtocol is < CoopProtocol.Version )
		{
			Stopped = CoopStop.ServerTooOld;
			return;
		}
		attempt = 0;
		lostAt = null;
		protocolAsked = false;
		SetState( CoopLinkState.Opening );
		Connect( takeOver );
	}

	/// <summary>Closes the link and forgets its session without leaving it (<see cref="Leave"/> leaves); a later open names it, and the game leaves it then.</summary>
	public void Close()
	{
		epoch++;
		live?.Close();
		live = null;
		ForgetSession();
		Stopped = CoopStop.None;
		SetState( CoopLinkState.Closed );
	}

	/// <summary>Creates a session hosted by this game: listed on the open list or by code and invitation only (decision Q4), its title and most players (2 to 4).</summary>
	public bool Create( bool listed, string title, int maximumPlayers ) =>
		Session == null && maximumPlayers is >= 2 and <= CoopProtocol.MaximumPlayers && Send( new CoopCreate( listed, title, (byte)maximumPlayers ) );

	/// <summary>Joins the session with <paramref name="code"/> as typed (case, spaces and dashes do not matter); false for what cannot be a code.</summary>
	public bool Join( string code ) => Session == null && CoopProtocol.NormalizeCode( code ) is { } normalized && Send( new CoopJoin( normalized ) );

	/// <summary>
	/// Leaves the session, at once for this game: the server tells the others <paramref name="reason"/>, and a host's leave ends the session. A leave that
	/// cannot go out now goes out when the server next names the session.
	/// </summary>
	public bool Leave( string reason )
	{
		if ( Session == null )
			return false;
		Send( new CoopLeave( reason ) );
		ForgetSession();
		return true;
	}

	/// <summary>The host removes member <paramref name="memberId"/>; the account cannot join the session again.</summary>
	public bool Kick( uint memberId ) => IsHost && Send( new CoopKick( memberId ) );

	/// <summary>The host invites a player, who must have a co-op link open.</summary>
	public bool Invite( string player ) => IsHost && Send( new CoopInvite( player.Trim() ) );

	/// <summary>
	/// The host's frame <paramref name="seq"/> (each one after the last, from 1) for the other members. A frame that cannot go out now (the link is
	/// reconnecting) is the host's to resend after <see cref="Resumed"/>.
	/// </summary>
	public bool SendFrame( ulong seq, ReadOnlyMemory<byte> payload )
	{
		if ( !IsHost || seq == 0 )
			return false;
		LastFrameSeq = seq;
		return Send( new CoopHostFrame( seq, payload ) );
	}

	/// <summary>
	/// A message to member <paramref name="to"/> (<see cref="CoopProtocol.EveryOtherMember"/>: every other member, the host's only); a guest sends to the
	/// host. At most once: one to a member who is not connected is refused, and one queued on a lost socket is lost.
	/// </summary>
	public bool Send( uint to, ReadOnlyMemory<byte> payload ) => Session != null && Send( new CoopSend( to, payload ) );

	/// <summary>
	/// The open list of shared parks of this game's build (another build cannot join): <paramref name="onListed"/> runs on the game thread with the sessions,
	/// or with null and why the list failed.
	/// </summary>
	public void List( Action<IReadOnlyList<CoopListedSession>?, string?> onListed )
	{
		if ( owner.Client is not { Session: not null } client )
		{
			onListed( null, OnlineStrings.Get( OnlineLabel.OnlineOff ) );
			return;
		}
		var build = Identity().Build;
		owner.Background( async () =>
		{
			IReadOnlyList<CoopListedSession>? sessions = null;
			string? error = null;
			try
			{
				sessions = (await client.ListCoopSessionsAsync()).Sessions.Where( session => session.Build == build ).ToArray();
			}
			catch ( Exception exception ) when ( exception is OnlineException or HttpRequestException or IOException or InvalidDataException or TaskCanceledException
				or InvalidOperationException or ObjectDisposedException )
			{
				error = exception.Message;
			}
			owner.Post( () => onListed( sessions, error ) );
		} );
	}

	private bool Send( CoopMessage message )
	{
		if ( State != CoopLinkState.Open || live is not { } socket )
			return false;
		var bytes = message.Encode();
		if ( bytes.Length > CoopProtocol.MaximumMessageBytes )
			throw new ArgumentException( $"The co-op message is over {CoopProtocol.MaximumMessageBytes} bytes." );
		if ( socket.Enqueue( bytes ) )
			return true;
		// the socket is this far behind: dropping it lets the link resume on a new one
		socket.Abort();
		return false;
	}

	private void Connect( bool takeOver )
	{
		if ( owner.Client is not { Session: not null } client )
		{
			Stop( CoopStop.Closed );
			return;
		}
		var current = ++epoch;
		var (build, mods) = Identity();
		var resume = Session is { } session ? new CoopResume( session.Id, session.You, LastFrameSeq ) : null;
		var hello = new CoopHello( (ushort)CoopProtocol.Version, build, mods, Instance, takeOver, resume ).Encode();
		var silence = Silence;
		var askProtocol = State == CoopLinkState.Opening && !protocolAsked && owner.ServerProtocol == null;
		owner.Background( async () =>
		{
			CoopConnection connection;
			try
			{
				connection = await client.ConnectCoopAsync();
			}
			catch ( OnlineException exception ) when ( exception.Status is System.Net.HttpStatusCode.Unauthorized )
			{
				// the login is no longer valid (a password change, a deleted account): another attempt would be refused the same way
				owner.Post( () =>
				{
					if ( current == epoch )
						Stop( CoopStop.Closed );
				} );
				return;
			}
			catch ( Exception exception ) when ( exception is WebSocketException or HttpRequestException or IOException or OperationCanceledException
				or InvalidOperationException or ObjectDisposedException or OnlineException )
			{
				// a first open that fails asks the server's protocol once: an older server has no co-op socket to wait for
				int? protocol = null;
				if ( askProtocol )
				{
					try
					{
						protocol = (await client.GetServerInfoAsync()).ProtocolVersion;
					}
					catch ( Exception info ) when ( info is OnlineException or HttpRequestException or IOException or InvalidDataException or TaskCanceledException )
					{
					}
				}
				owner.Post( () =>
				{
					if ( current != epoch )
						return;
					if ( askProtocol )
						protocolAsked = true;
					if ( protocol is < CoopProtocol.Version )
						Stop( CoopStop.ServerTooOld );
					else
						Retry( rate: false );
				} );
				return;
			}
			var socket = new Socket( connection );
			socket.Enqueue( hello );
			owner.Post( () =>
			{
				if ( current != epoch )
				{
					socket.Close();
					return;
				}
				live = socket;
				Connections++;
			}, socket.Close );
			_ = Task.Run( socket.WriteLoopAsync );
			await ReadLoopAsync( socket, silence );
		} );
	}

	/// <summary>Reads <paramref name="socket"/> until it ends, answering pings at once and passing every other message to the game thread.</summary>
	private async Task ReadLoopAsync( Socket socket, TimeSpan silence )
	{
		var malformed = false;
		using var quiet = new CancellationTokenSource();
		try
		{
			while ( true )
			{
				// no message for this long drops the socket (a cancelled receive aborts it), and the link resumes on a new one
				quiet.CancelAfter( silence );
				var message = await socket.Connection.ReceiveAsync( quiet.Token );
				if ( message == null )
					break;
				if ( message is CoopPing )
				{
					socket.Enqueue( Pong );
					continue;
				}
				owner.Post( () => Receive( socket, message ) );
			}
		}
		catch ( InvalidDataException )
		{
			malformed = true;
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or ObjectDisposedException )
		{
		}
		var status = socket.Connection.CloseStatus;
		if ( malformed )
			socket.Fail( (WebSocketCloseStatus)CoopCloseStatus.ProtocolError, "invalid message" );
		else
			socket.Close();
		owner.Post( () => SocketEnded( socket, status, malformed ) );
	}

	private void Receive( Socket socket, CoopMessage message )
	{
		if ( socket != live )
			return;
		switch ( message )
		{
			case CoopWelcome welcome:
				Welcomed( welcome );
				break;
			case CoopCreated created:
				Started( created.Session, null, null );
				break;
			case CoopJoined joined:
				Started( joined.Session, joined.HostBuild, joined.HostMods );
				break;
			case CoopMemberChanged change when Session is { } session:
				var others = session.Members.Where( member => member.Id != change.Member.Id );
				Session = session with
				{
					Members = (change.State is CoopMemberState.Left or CoopMemberState.Kicked ? others : others.Append( change.Member with { Connected = change.State != CoopMemberState.Disconnected } ))
						.OrderBy( member => member.Slot ).ToArray()
				};
				MemberChanged?.Invoke( change );
				break;
			case CoopEnded ended when Session?.Id == ended.SessionId:
				ForgetSession();
				SessionEnded?.Invoke( ended );
				break;
			case CoopFrame frame when Session != null && !IsHost:
				LastFrameSeq = frame.Seq;
				FrameReceived?.Invoke( frame );
				break;
			case CoopRelayed relayed when Session != null:
				MessageReceived?.Invoke( relayed );
				break;
			case CoopInvited invited:
				Invited?.Invoke( invited );
				break;
			case CoopError error:
				Refused?.Invoke( error );
				break;
		}
	}

	private void Started( CoopSessionState session, string? hostBuild, IReadOnlyList<CoopMod>? hostMods )
	{
		Session = session;
		HostBuild = hostBuild;
		HostMods = hostMods;
		LastFrameSeq = session.LastFrameSeq;
		SessionStarted?.Invoke( session );
	}

	/// <summary>
	/// The server's answer to the hello: the session resumed, or why it ended while this game was away. A session the server names that this game does not
	/// hold (it restarted, or took the socket over) cannot be resumed, so the account leaves it.
	/// </summary>
	private void Welcomed( CoopWelcome welcome )
	{
		ServerProtocol = welcome.Protocol;
		lostAt = null;
		attempt = 0;
		CoopSessionState? resumed = null;
		CoopEnded? ended = null;
		if ( Session is { } held )
		{
			if ( welcome.Session is { } state && state.Id == held.Id && state.You == held.You )
				resumed = Session = state;
			else
			{
				ended = welcome.EndedWhileAway is { } away && away.SessionId == held.Id ? away
					: new CoopEnded( held.Id, CoopReason.Unknown, "The shared park ended while this game was away." );
				ForgetSession();
			}
		}
		SetState( CoopLinkState.Open );
		if ( Session == null && welcome.Session != null )
			Send( new CoopLeave( "This game no longer holds the shared park." ) );
		if ( ended != null )
			SessionEnded?.Invoke( ended );
		if ( resumed != null )
			Resumed?.Invoke( resumed );
	}

	/// <summary>The socket ended: a close the link resumes after starts the reconnect attempts, any other stops the link.</summary>
	private void SocketEnded( Socket socket, int? status, bool malformed )
	{
		if ( socket != live )
			return;
		live = null;
		if ( malformed )
			Stop( CoopStop.Closed );
		else if ( status == CoopCloseStatus.InUse )
			Stop( CoopStop.InUseElsewhere );
		else if ( status == CoopCloseStatus.Replaced )
			Stop( CoopStop.Replaced );
		else if ( !CoopCloseStatus.IsResumable( status ) )
			Stop( CoopStop.Closed );
		else
		{
			if ( State == CoopLinkState.Open )
			{
				lostAt = Environment.TickCount64;
				attempt = 0;
				SetState( CoopLinkState.Reconnecting );
			}
			Retry( rate: status == CoopCloseStatus.Rate );
		}
	}

	/// <summary>Waits, then opens a new socket, until <see cref="ReconnectWindow"/> has passed since the loss (or since the first attempt of an open).</summary>
	private void Retry( bool rate )
	{
		lostAt ??= Environment.TickCount64;
		if ( Environment.TickCount64 - lostAt.Value >= ReconnectWindow.TotalMilliseconds )
		{
			Stop( CoopStop.Lost );
			return;
		}
		var delay = RetryDelays[Math.Min( attempt, RetryDelays.Length - 1 )];
		if ( rate && delay < CoopCloseStatus.RateRetryDelay )
			delay = CoopCloseStatus.RateRetryDelay;
		attempt++;
		var current = ++epoch;
		owner.Background( async () =>
		{
			await Task.Delay( delay );
			owner.Post( () =>
			{
				if ( current == epoch && State is CoopLinkState.Opening or CoopLinkState.Reconnecting )
					Connect( takeOver: false );
			} );
		} );
	}

	/// <summary>The link stopped on its own: the socket goes, and a session it held is over for this game.</summary>
	private void Stop( CoopStop reason )
	{
		epoch++;
		live?.Close();
		live = null;
		var held = Session;
		ForgetSession();
		Stopped = reason;
		SetState( CoopLinkState.Closed );
		if ( held != null )
			SessionEnded?.Invoke( new CoopEnded( held.Id, CoopReason.Unknown, reason switch
			{
				CoopStop.Replaced => "Another game of this account took the shared park over.",
				CoopStop.Lost => "The connection to the server was lost.",
				_ => "The server closed the connection."
			} ) );
	}

	private void ForgetSession()
	{
		Session = null;
		HostBuild = null;
		HostMods = null;
		LastFrameSeq = 0;
	}

	private void SetState( CoopLinkState state )
	{
		if ( State == state )
			return;
		State = state;
		StateChanged?.Invoke( state );
	}

	/// <summary>One open socket: its connection and the queue its writer sends, bounded by <see cref="MaximumQueuedBytes"/>.</summary>
	private sealed class Socket
	{
		private readonly Channel<byte[]> outbox = Channel.CreateUnbounded<byte[]>( new UnboundedChannelOptions { SingleReader = true } );
		private long queuedBytes;

		public Socket( CoopConnection connection ) => Connection = connection;

		public CoopConnection Connection { get; }

		/// <summary>Queues a message for the writer; false when the socket is closing or is <see cref="MaximumQueuedBytes"/> behind.</summary>
		public bool Enqueue( byte[] message )
		{
			if ( Interlocked.Add( ref queuedBytes, message.Length ) <= MaximumQueuedBytes && outbox.Writer.TryWrite( message ) )
				return true;
			Interlocked.Add( ref queuedBytes, -message.Length );
			return false;
		}

		/// <summary>Sends the queue in order; once the queue is closed and sent, closes the connection. A failed send drops it (the reader notices).</summary>
		public async Task WriteLoopAsync()
		{
			try
			{
				await foreach ( var message in outbox.Reader.ReadAllAsync() )
				{
					await Connection.SendAsync( message );
					Interlocked.Add( ref queuedBytes, -message.Length );
				}
				Connection.Close();
			}
			catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException )
			{
				Connection.Abort();
			}
		}

		/// <summary>Sends what is queued, then closes; a socket that has not taken it within two seconds is dropped.</summary>
		public void Close()
		{
			if ( !outbox.Writer.TryComplete() )
				return;
			_ = Task.Delay( TimeSpan.FromSeconds( 2 ) ).ContinueWith( _ => Connection.Abort(), TaskScheduler.Default );
		}

		/// <summary>Closes at once with <paramref name="status"/>, dropping what is queued (the server sent something this game cannot read).</summary>
		public void Fail( WebSocketCloseStatus status, string reason )
		{
			outbox.Writer.TryComplete();
			Connection.Close( status, reason );
		}

		public void Abort()
		{
			outbox.Writer.TryComplete();
			Connection.Abort();
		}
	}
}
