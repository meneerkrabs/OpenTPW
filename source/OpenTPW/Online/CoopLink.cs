using System.Net.WebSockets;
using System.Reflection;
using System.Threading.Channels;
using OpenTPW.Mods;
using OpenTPW.Online;
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
	/// <summary>The server is older than this game's co-op protocol (<see cref="CoopProtocol.Version"/>) and cannot share its parks.</summary>
	ServerTooOld,
	/// <summary>
	/// The link cannot go on: no login, a login the server refuses, a protocol or size refusal, a close the link does not resume after, or a hello this
	/// game cannot write.
	/// </summary>
	Closed,
	/// <summary>No socket stayed up within <see cref="CoopLink.ReconnectWindow"/> of the loss.</summary>
	Lost,
	/// <summary>
	/// [EXT:online-coop-session] The lobby opened the link (C2d-3b, <see cref="CoopLink.OpenInLobby"/>) and the server named a shared park of the account that this game did not ask
	/// for: another game's of the account, away for a moment, or an earlier run's. The lobby leaves neither, so the link stops; a join or a share opens it again (and leaves that
	/// park then, as any open that did not ask for it does).
	/// </summary>
	HeldElsewhere
}

/// <summary>
/// [EXT:online-coop-session] The game's link to the co-op socket of shared parks (docs/ONLINE.md, "Shared parks"), under an <see cref="OnlineSession"/> and its
/// login: a background reader and writer per socket, a reconnect after a lost socket that resumes the session, a silence watchdog, and the requests and
/// events the shared park uses. Every request and event is on the game thread. What the server sends is raised from <see cref="OnlineSession.Pump"/>;
/// <see cref="Open"/>, <see cref="Close"/> and a stop raise their own state and session events at once.
/// </summary>
public sealed class CoopLink : ICoopStreamTransport
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

	/// <summary>
	/// A socket that stays up this long after its welcome counts as healthy: only then does a later loss start the waits and the window afresh. A socket that
	/// keeps dropping soon after its welcome stays one loss, so the link backs off and stops in time.
	/// </summary>
	internal static readonly TimeSpan DefaultStableAfter = TimeSpan.FromSeconds( 30 );

	/// <summary>A connect attempt whose socket has not opened within this long is given up and tried again (a server that never answers the upgrade).</summary>
	internal static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds( 20 );

	/// <summary>The bytes queued for a socket at most; a socket that falls this far behind is dropped and resumed (a host resends its frames after a resume).</summary>
	internal const long MaximumQueuedBytes = 8 * 1024 * 1024;

	/// <summary>The largest frame payload a host sends: the message limit less the frame's type, number and length.</summary>
	public const int MaximumFramePayload = CoopProtocol.MaximumMessageBytes - 13;

	/// <summary>The largest message payload a member sends: the message limit less the message's type, addressee and length.</summary>
	public const int MaximumMessagePayload = CoopProtocol.MaximumMessageBytes - 9;

	private static readonly byte[] Pong = new CoopPong().Encode();
	private readonly OnlineSession owner;
	/// <summary>The socket in use.</summary>
	private Socket? live;
	/// <summary>Counts opens, closes, stops and attempts: what an older one posts back is dropped.</summary>
	private int epoch;
	/// <summary>When the loss began (<see cref="Environment.TickCount64"/>), while reconnecting or opening.</summary>
	private long? lostAt;
	/// <summary>Attempts since the loss began, or since <see cref="Open"/> while none has been welcomed.</summary>
	private int attempt;
	/// <summary>The server's protocol was asked after a failed first attempt (an older server has no co-op socket).</summary>
	private bool protocolAsked;
	/// <summary>The take-over <see cref="Open"/> asked for, kept until a welcome so a failed first attempt does not lose it.</summary>
	private bool takeOver;
	/// <summary>The connect attempt in flight: its timeout, and its cancel at a close or a stop.</summary>
	private CancellationTokenSource? attemptCancel;
	/// <summary>When the live socket was welcomed (<see cref="Environment.TickCount64"/>).</summary>
	private long? welcomedAt;
	/// <summary>The creates and joins sent on the live socket and not answered yet: a lost socket refuses them (<see cref="Refused"/>).</summary>
	private readonly List<CoopMessageType> pending = new();
	/// <summary>
	/// The leaves that could not go out, one an account of a server (an account holds one session there): the session and the reason. The account's next welcome that names
	/// the session sends it, and keeps it until the server confirms it (the session's end for this game, a refusal that it is in none, or a welcome that does not name it),
	/// so one lost with its socket before it went out goes again (C2d-3b); another server's or another account's welcome keeps it.
	/// </summary>
	private readonly Dictionary<(Uri? Server, string? Account), DeferredLeave> pendingLeaves = new();
	/// <summary>
	/// [EXT:online-coop-session] A create or join was refused with its socket: the server may have made the seat. The link's next welcome leaves it (a session it does not
	/// hold), unless the lobby opened the link anew meanwhile (C2d-3b), which cannot tell such a seat from another game's.
	/// </summary>
	private bool lostAnswer;
	/// <summary>The server and the account of the link's latest socket, which a session the link holds belongs to (they outlive the login: a logout's leave is theirs).</summary>
	private (Uri? Server, string? Account) socketOwner;
	/// <summary>The lobby opened the link and nothing has asked it for a seat since (<see cref="OpenInLobby"/>).</summary>
	private bool lobbyOpen;

	internal CoopLink( OnlineSession owner ) => this.owner = owner;

	/// <summary>
	/// This link's random id in its hellos: a reconnect of this link replaces its own old socket, another link of the account (another game, or another
	/// online session of this one) is refused unless it takes over.
	/// </summary>
	internal Guid Instance { get; set; } = Guid.NewGuid();
	internal TimeSpan[] RetryDelays { get; set; } = DefaultRetryDelays;
	/// <summary>
	/// [EXT:online-coop-session] Spreads each wait of <see cref="RetryDelays"/> (<see cref="DefaultJitter"/>): with a link open in every lobby (C2d-3b), a server that restarts must
	/// not bring every game back at the same moment. Tests give their own.
	/// </summary>
	internal Func<TimeSpan, TimeSpan> Jitter { get; set; } = DefaultJitter;
	internal TimeSpan Silence { get; set; } = DefaultSilence;
	internal TimeSpan ReconnectWindow { get; set; } = DefaultReconnectWindow;
	internal TimeSpan StableAfter { get; set; } = DefaultStableAfter;
	internal TimeSpan ConnectTimeout { get; set; } = DefaultConnectTimeout;
	/// <summary>The build and mods a hello names (<see cref="CurrentIdentity"/>; tests give their own).</summary>
	internal Func<(string Build, IReadOnlyList<CoopMod> Mods)> Identity { get; set; } = CurrentIdentity;
	/// <summary>Opens a socket (<see cref="OnlineClient.ConnectCoopAsync"/>; tests delay or cut it).</summary>
	internal Func<OnlineClient, CancellationToken, Task<CoopConnection>> Connector { get; set; } = ( client, cancel ) => client.ConnectCoopAsync( cancel );
	/// <summary>Fetches the open list of shared parks (<see cref="OnlineClient.ListCoopSessionsAsync"/>; tests give their own, as the server never lists a full park).</summary>
	internal Func<OnlineClient, Task<CoopSessionList>> Lister { get; set; } = client => client.ListCoopSessionsAsync();
	/// <summary>Drops a received message it accepts before the link reads it (tests: an answer lost with its socket).</summary>
	internal Func<CoopMessage, bool>? DropReceived { get; set; }
	/// <summary>Drops a message it accepts as if it went out (tests: a leave lost with its socket before it was written).</summary>
	internal Func<CoopMessage, bool>? DropSent { get; set; }
	/// <summary>Sockets the link has opened (tests: a reconnect).</summary>
	internal int Connections { get; private set; }

	/// <summary>Drops the socket without a close, as a lost network does (tests).</summary>
	internal void DropSocket() => live?.Abort();

	/// <summary>A wait of <see cref="RetryDelays"/> spread over 80 to 120 per cent of itself.</summary>
	internal static TimeSpan DefaultJitter( TimeSpan delay ) => delay * (0.8 + 0.4 * Random.Shared.NextDouble());

	public CoopLinkState State { get; private set; }
	/// <summary>Why the link stopped on its own; <see cref="CoopStop.None"/> while it runs or after <see cref="Close"/>.</summary>
	public CoopStop Stopped { get; private set; }
	/// <summary>The session this game is in, as the server last told it; null outside a session.</summary>
	public CoopSessionState? Session { get; private set; }
	public bool IsHost => Session is { } session && session.You == session.Host;
	/// <summary>
	/// [EXT:online-coop-session] A create or join waits for its answer: once the server reads it, it seats this game, also after a close without a leave (the socket sends what
	/// it holds before it closes). The lobby's link is never closed while one waits (C2d-3b).
	/// </summary>
	internal bool Waiting => pending.Count > 0;
	bool ICoopStreamTransport.IsOpen => State == CoopLinkState.Open;
	/// <summary>The joined session's host build and mods (they match this game's, or the join was refused); null for a host.</summary>
	public string? HostBuild { get; private set; }
	public IReadOnlyList<CoopMod>? HostMods { get; private set; }
	/// <summary>The server's protocol, from its welcome.</summary>
	public int? ServerProtocol { get; private set; }
	/// <summary>
	/// The newest frame of the session this game holds: for a host the last frame it queued (after a resume, the last one the server relayed), for a guest
	/// the last one it received (when it joined, the host's last frame then). A resume reports it.
	/// </summary>
	public ulong LastFrameSeq { get; private set; }

	/// <summary>This game created a session (it hosts it) or joined one.</summary>
	public event Action<CoopSessionState>? SessionStarted;
	/// <summary>
	/// A lost socket is back and the session with it: the state is a full refresh (members that came or went while the socket was lost raise no
	/// <see cref="MemberChanged"/>). A host resends its frames after the state's <see cref="CoopSessionState.LastFrameSeq"/>; when that number is not exact
	/// (<see cref="CoopSessionState.SeqKnown"/>: the server crashed), the server takes any later frame, and a guest drops the frames it already holds.
	/// </summary>
	public event Action<CoopSessionState>? Resumed;
	/// <summary>The server dropped a frame of the host's that was not the next one (no guest saw it); the host resends from <see cref="CoopFrameRefused.Expected"/>.</summary>
	public event Action<CoopFrameRefused>? FrameRefused;
	public event Action<CoopMemberChanged>? MemberChanged;
	/// <summary>This game is no longer in its session: kicked, the host left, it ended while this game was away, or the link closed or stopped.</summary>
	public event Action<CoopEnded>? SessionEnded;
	/// <summary>A frame of the host's command stream (a guest).</summary>
	public event Action<CoopFrame>? FrameReceived;
	public event Action<CoopRelayed>? MessageReceived;
	public event Action<CoopInvited>? Invited;
	/// <summary>
	/// The server refused a request (<see cref="CoopError.Request"/>), with a reason that names what differs; or a create or join got no answer because its
	/// socket was lost (<see cref="CoopReason.Unknown"/>), so the game can ask again.
	/// </summary>
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
		lobbyOpen = false;
		Stopped = CoopStop.None;
		if ( owner.ServerProtocol is < CoopProtocol.Version )
		{
			Stopped = CoopStop.ServerTooOld;
			return;
		}
		attempt = 0;
		lostAt = null;
		protocolAsked = false;
		this.takeOver = takeOver;
		var opening = ++epoch;
		SetState( CoopLinkState.Opening );
		// a handler of the change may have closed the link again
		if ( opening == epoch && State == CoopLinkState.Opening )
			Connect();
	}

	/// <summary>
	/// [EXT:online-coop-session] Opens the link for the lobby (C2d-3b, <see cref="CoopLobbyLink"/>): as <see cref="Open"/> without a take-over, except that a welcome naming a shared
	/// park of the account that this game did not ask for stops the link (<see cref="CoopStop.HeldElsewhere"/>) instead of leaving that park: it may be another game's of the
	/// account, whose socket is away for a moment. Once a create or join goes out, the link is any open's again. Nothing happens while it is open or on its way.
	/// </summary>
	internal void OpenInLobby()
	{
		if ( State != CoopLinkState.Closed )
			return;
		Open();
		lobbyOpen = State != CoopLinkState.Closed;
	}

	/// <summary>
	/// Closes the link and forgets its session: the session is over for this game (<see cref="SessionEnded"/>), a later open names it, and the game leaves it then.
	/// </summary>
	/// <param name="leaveReason">
	/// [EXT:online-coop-session] Leaves the session first with this reason, which the others hear (a host's leave ends it for everyone; a logout or another server):
	/// the leave is queued on the socket, which sends what it holds before it closes, and a link that cannot send it now leaves at the next welcome that names the
	/// session. The end this game hears is its own leave. Null closes without leaving.
	/// </param>
	public void Close( string? leaveReason = null )
	{
		lobbyOpen = false;
		var held = Session;
		string? left = null;
		if ( leaveReason != null && (held != null || pending.Count > 0) )
		{
			left = CoopProtocol.Clip( leaveReason.Trim() );
			// a create or join still waiting for its answer makes a session the server holds once it reads it: the leave follows it on the same socket, which the
			// server reads in order, so it leaves that session too
			var sent = Send( new CoopLeave( left ) );
			if ( held != null )
				DeferLeave( sent, held.Id, left );
		}
		Shut( "This game closed its link to the shared park." );
		Stopped = CoopStop.None;
		ForgetSession();
		SetState( CoopLinkState.Closed, quietly: true );
		if ( held != null )
			SessionEnded?.Invoke( left != null ? new CoopEnded( held.Id, CoopReason.Left, left )
				: new CoopEnded( held.Id, CoopReason.Unknown, "This game closed its link to the shared park." ) );
		RaiseState();
	}

	/// <summary>
	/// Creates a session hosted by this game: listed on the open list or by code and invitation only (decision Q4), its title and most players (2 to 4).
	/// False for a title over <see cref="CoopProtocol.MaximumTitleLength"/> characters or with a control character, and while a create or join waits.
	/// </summary>
	public bool Create( bool listed, string title, int maximumPlayers )
	{
		if ( Session != null || pending.Count > 0 || maximumPlayers is < 2 or > CoopProtocol.MaximumPlayers || !Fits( title, CoopProtocol.MaximumTitleLength ) )
			return false;
		if ( !Send( new CoopCreate( listed, title, (byte)maximumPlayers ) ) )
			return false;
		pending.Add( CoopMessageType.Create );
		lobbyOpen = false;
		return true;
	}

	/// <summary>Joins the session with <paramref name="code"/> as typed (case, spaces and dashes do not matter); false for what cannot be a code, and while a create or join waits.</summary>
	public bool Join( string code )
	{
		if ( Session != null || pending.Count > 0 || CoopProtocol.NormalizeCode( code ) is not { } normalized )
			return false;
		if ( !Send( new CoopJoin( normalized ) ) )
			return false;
		pending.Add( CoopMessageType.Join );
		lobbyOpen = false;
		return true;
	}

	/// <summary>
	/// Leaves the session, at once for this game: the server tells the others <paramref name="reason"/> (cut to what a message carries), and a host's leave
	/// ends the session. A leave that cannot go out now goes out, with its reason, when the server next names the session.
	/// </summary>
	public bool Leave( string reason )
	{
		if ( Session is not { } session )
			return false;
		reason = CoopProtocol.Clip( reason.Trim() );
		DeferLeave( Send( new CoopLeave( reason ) ), session.Id, reason );
		ForgetSession();
		// the session is over for this game without an end event: its chat leaves the session's room
		owner.LeftCoopSession( session.Id );
		return true;
	}

	/// <summary>A leave of the socket's account went out (<paramref name="sent"/>), or waits for that account's next welcome on that server.</summary>
	private void DeferLeave( bool sent, string sessionId, string reason )
	{
		if ( sent )
			pendingLeaves.Remove( socketOwner );
		else
			pendingLeaves[socketOwner] = new DeferredLeave( sessionId, reason );
	}

	/// <summary>[EXT:online-coop-session] A leave of the session <paramref name="SessionId"/> the server has not confirmed (its id: never a guess at another seat).</summary>
	private readonly record struct DeferredLeave( string SessionId, string Reason );

	/// <summary>
	/// [EXT:online-coop-session] The link has something to settle at its next welcome: a leave of this game's that waits for the server, or a seat a create or join whose
	/// answer was lost may have made. The lobby keeps such a link open for a while (C2d-3b, <see cref="CoopLobbyLink.SettleHoldSeconds"/>).
	/// </summary>
	internal bool Settling => lostAnswer || pendingLeaves.ContainsKey( socketOwner );

	/// <summary>The host removes member <paramref name="memberId"/>; the account cannot join the session again.</summary>
	public bool Kick( uint memberId ) => IsHost && Send( new CoopKick( memberId ) );

	/// <summary>The host invites a player, who must have a co-op link open; false for a name that cannot be one.</summary>
	public bool Invite( string player )
	{
		player = player.Trim();
		return IsHost && player.Length > 0 && Fits( player, CoopProtocol.MaximumInviteNameLength ) && Send( new CoopInvite( player ) );
	}

	/// <summary>[EXT:online-coop-snapshot-join] The server takes the host's skip from protocol 7 on (<see cref="CoopSkipFrames"/>), the protocol this link needs.</summary>
	public bool CanSkipFrames => ServerProtocol >= CoopProtocol.Version;

	/// <summary>[EXT:online-coop-snapshot-join] The host's skip of the frames it no longer holds: the server takes frame <paramref name="from"/> next.</summary>
	public bool SkipFrames( ulong from ) => IsHost && CanSkipFrames && from is > 0 and < ulong.MaxValue && Send( new CoopSkipFrames( from ) );

	/// <summary>
	/// The host's frame <paramref name="seq"/> (each one after the last, from 1) for the other members; false when it cannot go out now (the link is
	/// reconnecting: the host resends after <see cref="Resumed"/>) or the payload is over <see cref="MaximumFramePayload"/>.
	/// </summary>
	public bool SendFrame( ulong seq, ReadOnlyMemory<byte> payload )
	{
		if ( !IsHost || seq is 0 or ulong.MaxValue || payload.Length > MaximumFramePayload || !Send( new CoopHostFrame( seq, payload ) ) )
			return false;
		LastFrameSeq = seq;
		return true;
	}

	/// <summary>
	/// A message to member <paramref name="to"/> (<see cref="CoopProtocol.EveryOtherMember"/>: every other member, the host's only); a guest sends to the
	/// host. At most once: one to a member who is not connected is refused, and one queued on a lost socket is lost.
	/// </summary>
	public bool Send( uint to, ReadOnlyMemory<byte> payload ) => Session != null && payload.Length <= MaximumMessagePayload && Send( new CoopSend( to, payload ) );

	/// <summary>
	/// The open list of shared parks of this game's build (another build cannot join): <paramref name="onListed"/> runs on the game thread with the sessions,
	/// or with null and why the list failed. It does not run once the player has logged out or changed server meanwhile.
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
				sessions = (await Lister( client )).Sessions.Where( session => session.Build == build ).ToArray();
			}
			catch ( Exception exception )
			{
				if ( exception is not (OnlineException or HttpRequestException or IOException or InvalidDataException or TaskCanceledException
					or InvalidOperationException or ObjectDisposedException) )
					Log.Warning( $"The shared parks list failed: {exception}" );
				error = exception.Message;
			}
			owner.Post( () => onListed( sessions, error ) );
		} );
	}

	/// <summary>[EXT:online-coop-snapshot-join] How long a snapshot download may wait for its next bytes before it resumes with a range.</summary>
	internal static readonly TimeSpan SnapshotStall = TimeSpan.FromSeconds( 20 );

	void ICoopStreamTransport.UploadSnapshot( byte[] payload, Action<CoopSnapshotTransfer> done )
	{
		ArgumentNullException.ThrowIfNull( payload );
		ArgumentNullException.ThrowIfNull( done );
		if ( owner.Client is not { Session: not null } client || Session is not { } session )
		{
			owner.Post( () => done( CoopSnapshotTransfer.Failed( OnlineStrings.Get( OnlineLabel.OnlineOff ) ) ) );
			return;
		}
		owner.Background( async () =>
		{
			CoopSnapshotTransfer result;
			try
			{
				result = CoopSnapshotTransfer.Stored( (await client.PutCoopSnapshotAsync( session.Id, payload )).Sha256 );
			}
			// every failure answers: the host's captures wait for this answer
			catch ( Exception exception ) when ( exception is not OutOfMemoryException )
			{
				result = UploadFailure( exception );
			}
			// a logout drops what was posted (its discard may run on this thread, so none is given): the link closes then, and the session ends with it
			owner.Post( () => done( result ) );
		} );
	}

	void ICoopStreamTransport.DownloadSnapshot( string sha256, long bytes, Action<CoopSnapshotTransfer> done )
	{
		ArgumentNullException.ThrowIfNull( done );
		if ( owner.Client is not { Session: not null } client || Session is not { } session )
		{
			owner.Post( () => done( CoopSnapshotTransfer.Failed( OnlineStrings.Get( OnlineLabel.OnlineOff ) ) ) );
			return;
		}
		owner.Background( async () =>
		{
			CoopSnapshotTransfer result;
			try
			{
				result = CoopSnapshotTransfer.Downloaded( await client.DownloadCoopSnapshotAsync( session.Id, sha256, bytes, SnapshotStall ) );
			}
			catch ( Exception exception ) when ( exception is not OutOfMemoryException )
			{
				result = DownloadFailure( exception );
			}
			owner.Post( () => done( result ) );
		} );
	}

	/// <summary>[EXT:online-coop-snapshot-join] What a failed snapshot upload means for the host's capture: too large, or refused for good (not the host's, the session gone), or for now.</summary>
	internal static CoopSnapshotTransfer UploadFailure( Exception exception ) => exception switch
	{
		// too large, not the host's, the session gone: trying again cannot help
		OnlineException { Status: System.Net.HttpStatusCode.RequestEntityTooLarge } error => CoopSnapshotTransfer.Failed( error.Message, tooLarge: true ),
		OnlineException { Status: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound } error => CoopSnapshotTransfer.Failed( error.Message, permanent: true ),
		// nobody joining yet, another upload running, too many uploads, no room now
		OnlineException error => CoopSnapshotTransfer.Failed( error.Message, retryAfter: error.RetryAfter ),
		_ => CoopSnapshotTransfer.Failed( exception.Message )
	};

	/// <summary>
	/// [EXT:online-coop-snapshot-join] What a failed snapshot download means for the guest: the snapshot rotated out (404), the guest no longer the session's (403, for good), the server asking
	/// to be tried again later (429, a server error), or a failed download.
	/// </summary>
	internal static CoopSnapshotTransfer DownloadFailure( Exception exception ) => exception switch
	{
		OnlineException { Status: System.Net.HttpStatusCode.NotFound } error => CoopSnapshotTransfer.Failed( error.Message, missing: true ),
		OnlineException { Status: System.Net.HttpStatusCode.Forbidden } error => CoopSnapshotTransfer.Failed( error.Message, permanent: true ),
		OnlineException { Status: System.Net.HttpStatusCode.TooManyRequests or >= System.Net.HttpStatusCode.InternalServerError } error =>
			CoopSnapshotTransfer.Failed( error.Message, retryAfter: error.RetryAfter, busy: true ),
		OnlineException error => CoopSnapshotTransfer.Failed( error.Message, retryAfter: error.RetryAfter ),
		_ => CoopSnapshotTransfer.Failed( exception.Message )
	};

	/// <summary>Whether <paramref name="text"/> fits a field of <paramref name="maximumLength"/> characters (the writer refuses longer ones and control characters).</summary>
	private static bool Fits( string text, int maximumLength ) => text.Length <= maximumLength && !text.Any( char.IsControl );

	private bool Send( CoopMessage message )
	{
		if ( State != CoopLinkState.Open || live is not { } socket )
			return false;
		// tests: a message the socket would have lost before it went out
		if ( DropSent?.Invoke( message ) == true )
			return true;
		byte[] bytes;
		try
		{
			bytes = message.Encode();
		}
		catch ( ArgumentException )
		{
			return false;
		}
		if ( bytes.Length > CoopProtocol.MaximumMessageBytes )
			return false;
		if ( socket.Enqueue( bytes ) )
			return true;
		// the socket is this far behind: dropping it lets the link resume on a new one
		socket.Abort();
		return false;
	}

	private void Connect()
	{
		if ( owner.Client is not { Session: not null } client )
		{
			Stop( CoopStop.Closed );
			return;
		}
		socketOwner = (client.ServerUrl, client.Session.Name);
		var current = ++epoch;
		byte[] hello;
		try
		{
			var (build, mods) = Identity();
			var resume = Session is { } session ? new CoopResume( session.Id, session.You, LastFrameSeq ) : null;
			hello = new CoopHello( (ushort)CoopProtocol.Version, build, mods, Instance, takeOver, resume ).Encode();
		}
		catch ( ArgumentException exception )
		{
			// a build or a mod list a hello cannot carry: no attempt would get further
			Log.Warning( $"The shared park link cannot say hello: {exception.Message}" );
			Stop( CoopStop.Closed );
			return;
		}
		var silence = Silence;
		var askProtocol = State == CoopLinkState.Opening && !protocolAsked && owner.ServerProtocol == null;
		var cancel = attemptCancel = new CancellationTokenSource( ConnectTimeout );
		var connector = Connector;
		owner.Background( async () =>
		{
			CoopConnection connection;
			try
			{
				connection = await connector( client, cancel.Token );
			}
			catch ( OnlineException exception ) when ( exception.Status is System.Net.HttpStatusCode.Unauthorized )
			{
				// the login is no longer valid (a password change, a deleted account): another attempt would be refused the same way
				owner.Post( () =>
				{
					Forget( cancel );
					if ( current == epoch )
						Stop( CoopStop.Closed );
				} );
				return;
			}
			catch ( Exception exception )
			{
				if ( exception is not (WebSocketException or HttpRequestException or IOException or OperationCanceledException or InvalidOperationException
					or ObjectDisposedException or OnlineException) )
					Log.Warning( $"A shared park connection failed: {exception}" );
				// a first open that fails asks the server's protocol once: an older server has no co-op socket to wait for
				int? protocol = null;
				var asked = askProtocol && !cancel.IsCancellationRequested;
				if ( asked )
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
					Forget( cancel );
					if ( current != epoch )
						return;
					if ( asked )
						protocolAsked = true;
					if ( protocol is < CoopProtocol.Version )
						Stop( CoopStop.ServerTooOld );
					else
						Retry( rate: false );
				} );
				return;
			}
			// only an attempt the link still wants starts reading and says hello: an abandoned one closes without a word, so it cannot replace a newer socket
			owner.Post( () =>
			{
				Forget( cancel );
				if ( current != epoch )
				{
					connection.Dispose();
					return;
				}
				var socket = new Socket( connection );
				live = socket;
				Connections++;
				socket.Enqueue( hello );
				owner.Background( socket.WriteLoopAsync );
				owner.Background( () => ReadLoopAsync( socket, silence ) );
			}, connection.Dispose );
		} );
	}

	/// <summary>The attempt <paramref name="cancel"/> belongs to has ended: its timeout goes.</summary>
	private void Forget( CancellationTokenSource cancel )
	{
		if ( attemptCancel == cancel )
			attemptCancel = null;
		cancel.Dispose();
	}

	/// <summary>Reads <paramref name="socket"/> until it ends, answering pings at once and passing every other message to the game thread; its end always reaches it too.</summary>
	private async Task ReadLoopAsync( Socket socket, TimeSpan silence )
	{
		var malformed = false;
		try
		{
			using var quiet = new CancellationTokenSource();
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
		catch ( Exception exception )
		{
			if ( exception is not (WebSocketException or OperationCanceledException or ObjectDisposedException) )
				Log.Warning( $"A shared park socket failed: {exception}" );
		}
		finally
		{
			var status = socket.Connection.CloseStatus;
			var endedAt = Environment.TickCount64;
			if ( malformed )
				socket.Fail( (WebSocketCloseStatus)CoopCloseStatus.ProtocolError, "invalid message" );
			else
				socket.Close();
			owner.Post( () => SocketEnded( socket, status, malformed, endedAt ) );
		}
	}

	private void Receive( Socket socket, CoopMessage message )
	{
		if ( socket != live || DropReceived?.Invoke( message ) == true )
			return;
		switch ( message )
		{
			case CoopWelcome welcome:
				Welcomed( welcome );
				break;
			case CoopCreated created:
				pending.Remove( CoopMessageType.Create );
				Started( created.Session, null, null );
				break;
			case CoopJoined joined:
				pending.Remove( CoopMessageType.Join );
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
			case CoopEnded ended when Session == null && pendingLeaves.TryGetValue( socketOwner, out var left ) && left.SessionId == ended.SessionId:
				// the server confirms this game's leave of a session it no longer holds
				pendingLeaves.Remove( socketOwner );
				break;
			case CoopFrame frame when Session != null && !IsHost:
				LastFrameSeq = frame.Seq;
				FrameReceived?.Invoke( frame );
				break;
			case CoopRelayed relayed when Session != null:
				MessageReceived?.Invoke( relayed );
				break;
			case CoopFrameRefused refused when IsHost:
				FrameRefused?.Invoke( refused );
				break;
			case CoopInvited invited:
				Invited?.Invoke( invited );
				break;
			case CoopError error:
				if ( error.Request is CoopMessageType.Create or CoopMessageType.Join )
					pending.Remove( error.Request );
				// a leave of a session the account is no longer in is settled
				if ( error.Request == CoopMessageType.Leave && error.Reason == CoopReason.NotInSession && Session == null )
					pendingLeaves.Remove( socketOwner );
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
	/// hold (it restarted, or took the socket over, or its create or join was answered on a lost socket) cannot be resumed, so the account leaves it, before
	/// anything else this welcome raises; a link the lobby opened stops instead (<see cref="CoopStop.HeldElsewhere"/>).
	/// </summary>
	private void Welcomed( CoopWelcome welcome )
	{
		// an older server takes this game's hello, but not what its protocol added since (the host's skip, protocol 7): the link stops as before an older server
		if ( welcome.Protocol < CoopProtocol.Version )
		{
			Stop( CoopStop.ServerTooOld );
			return;
		}
		ServerProtocol = welcome.Protocol;
		welcomedAt = Environment.TickCount64;
		takeOver = false;
		// [EXT:online-coop-session] the lobby's link leaves no shared park it did not ask for (C2d-3b), but for this game's own leave that waits
		if ( lobbyOpen && Session == null && welcome.Session is { } elsewhere
			&& !(pendingLeaves.TryGetValue( socketOwner, out var own ) && own.SessionId == elsewhere.Id) )
		{
			// the account is in that park, so a leave of another one is settled; a lost answer's seat may be this park, which the lobby cannot tell
			pendingLeaves.Remove( socketOwner );
			lostAnswer = false;
			Stop( CoopStop.HeldElsewhere );
			return;
		}
		lostAnswer = false;
		CoopSessionState? resumed = null;
		CoopEnded? ended = null;
		if ( Session is { } held )
		{
			if ( welcome.Session is { } state && state.Id == held.Id && state.You == held.You )
			{
				resumed = Session = state;
				// what the server relayed last, not what the host queued on the lost socket
				if ( IsHost )
					LastFrameSeq = state.LastFrameSeq;
			}
			else
			{
				ended = welcome.EndedWhileAway is { } away && away.SessionId == held.Id ? away
					: new CoopEnded( held.Id, CoopReason.Unknown, "The shared park ended while this game was away." );
				ForgetSession();
			}
		}
		SetState( CoopLinkState.Open, quietly: true );
		if ( Session == null && welcome.Session is { } named )
		{
			if ( pendingLeaves.TryGetValue( socketOwner, out var leave ) && leave.SessionId == named.Id )
				// this game's own leave goes out, and is kept until the server confirms it
				Send( new CoopLeave( leave.Reason ) );
			else
			{
				// a session the link does not hold (a lost answer's seat, an earlier run's, or one taken over): the account leaves it, and a leave of another one is settled
				pendingLeaves.Remove( socketOwner );
				Send( new CoopLeave( "This game no longer holds the shared park." ) );
			}
		}
		else
			// the account's leave on this server is settled: its session is gone, or the link holds it again; another server's or account's waits for its own welcome
			pendingLeaves.Remove( socketOwner );
		if ( ended != null )
			SessionEnded?.Invoke( ended );
		if ( resumed != null )
			Resumed?.Invoke( resumed );
		RaiseState();
	}

	/// <summary>
	/// The socket ended (at <paramref name="endedAt"/>, which a stalled pump may reach late): a close the link resumes after starts the reconnect attempts, any
	/// other stops the link.
	/// </summary>
	private void SocketEnded( Socket socket, int? status, bool malformed, long endedAt )
	{
		if ( socket != live )
			return;
		live = null;
		// the welcome's time is when the pump took it, never before it came, so a socket's measured life is never longer than it was
		var stable = welcomedAt is { } at && endedAt - at >= StableAfter.TotalMilliseconds;
		welcomedAt = null;
		var current = epoch;
		FailPending();
		// a refusal's handler may have closed the link: the game's own close is no stop of the server's
		if ( current != epoch )
			return;
		if ( malformed )
			Stop( CoopStop.Closed );
		else if ( status == CoopCloseStatus.InUse )
			Stop( CoopStop.InUseElsewhere );
		else if ( status == CoopCloseStatus.Replaced )
		{
			// the game that took the link over leaves whatever the account holds that it does not, so nothing of this link's waits behind it
			pendingLeaves.Remove( socketOwner );
			lostAnswer = false;
			Stop( CoopStop.Replaced );
		}
		else if ( !CoopCloseStatus.IsResumable( status ) )
			Stop( CoopStop.Closed );
		else
		{
			// a socket that stayed up starts a new loss; one that dropped soon after its welcome goes on with the loss before it
			if ( stable || lostAt == null )
			{
				lostAt = endedAt;
				attempt = 0;
			}
			if ( State == CoopLinkState.Open )
				SetState( CoopLinkState.Reconnecting );
			// a handler of the change may have closed the link
			if ( current == epoch && State is CoopLinkState.Opening or CoopLinkState.Reconnecting )
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
		var delay = Jitter( RetryDelays[Math.Min( attempt, RetryDelays.Length - 1 )] );
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
					Connect();
			} );
		} );
	}

	/// <summary>The link stopped on its own: the socket goes, and a session it held is over for this game.</summary>
	private void Stop( CoopStop reason )
	{
		Shut( "The link to the server ended before the server answered." );
		var held = Session;
		ForgetSession();
		Stopped = reason;
		SetState( CoopLinkState.Closed, quietly: true );
		if ( held != null )
			SessionEnded?.Invoke( new CoopEnded( held.Id, CoopReason.Unknown, reason switch
			{
				CoopStop.InUseElsewhere => "Another game of this account holds the link to shared parks.",
				CoopStop.Replaced => "Another game of this account took the shared park over.",
				CoopStop.ServerTooOld => "The server is too old to share parks with this game.",
				CoopStop.Lost => "The connection to the server was lost.",
				_ => "The server closed the connection."
			} ) );
		RaiseState();
	}

	/// <summary>Ends whatever the link is doing: an attempt on its way, the live socket and the requests waiting on it (refused with <paramref name="text"/>).</summary>
	private void Shut( string text )
	{
		epoch++;
		// the game thread alone cancels and disposes an attempt's timeout (here or in Forget), so a discarded post never leaves it disposed under the link
		if ( attemptCancel != null )
		{
			attemptCancel.Cancel();
			attemptCancel.Dispose();
			attemptCancel = null;
		}
		live?.Close();
		live = null;
		welcomedAt = null;
		FailPending( text );
	}

	/// <summary>Refuses the creates and joins that wait on a socket that is gone: the game hears no answer will come and can ask again.</summary>
	private void FailPending( string text = "The connection was lost before the server answered." )
	{
		if ( pending.Count == 0 )
			return;
		// [EXT:online-coop-session] the server may have read it and made the seat, which the link's next welcome leaves (C2d-3b)
		lostAnswer = true;
		var requests = pending.ToArray();
		pending.Clear();
		foreach ( var request in requests )
			Refused?.Invoke( new CoopError( request, CoopReason.Unknown, text ) );
	}

	private void ForgetSession()
	{
		Session = null;
		HostBuild = null;
		HostMods = null;
		LastFrameSeq = 0;
	}

	private CoopLinkState raised;

	/// <summary>Sets <see cref="State"/>; <paramref name="quietly"/> leaves raising <see cref="StateChanged"/> to <see cref="RaiseState"/>, after the events that go first.</summary>
	private void SetState( CoopLinkState state, bool quietly = false )
	{
		State = state;
		if ( !quietly )
			RaiseState();
	}

	private void RaiseState()
	{
		if ( raised == State )
			return;
		raised = State;
		StateChanged?.Invoke( State );
	}

	/// <summary>One open socket: its connection and the queue its writer sends, bounded by <see cref="MaximumQueuedBytes"/>.</summary>
	private sealed class Socket
	{
		private readonly Channel<byte[]> outbox = Channel.CreateUnbounded<byte[]>( new UnboundedChannelOptions { SingleReader = false } );
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

		/// <summary>Closes at once with <paramref name="status"/> and drops what is queued (the server sent something this game cannot read).</summary>
		public void Fail( WebSocketCloseStatus status, string reason )
		{
			outbox.Writer.TryComplete();
			while ( outbox.Reader.TryRead( out var dropped ) )
				Interlocked.Add( ref queuedBytes, -dropped.Length );
			Connection.Close( status, reason );
		}

		public void Abort()
		{
			outbox.Writer.TryComplete();
			Connection.Abort();
		}
	}
}
