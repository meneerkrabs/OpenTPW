using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>[EXT:online-coop-lockstep] A level's part in a co-op session (<see cref="Level.CoopSession"/>).</summary>
public enum CoopRole
{
	None,
	Host,
	Guest
}

/// <summary>[EXT:online-coop-snapshot-join] Where a guest's session stands (docs/COOP.md, "Joining a running session").</summary>
public enum CoopGuestPhase
{
	/// <summary>No park of the session yet: it waits for its capture, downloads it or builds it.</summary>
	Joining,
	/// <summary>Its park follows the host's stream.</summary>
	Following,
	/// <summary>Its park stopped (a desync, too far back, a newer capture assigned) and waits for a capture to replace it.</summary>
	Resyncing
}

/// <summary>
/// [EXT:online-coop-snapshot-join] What the game does for a guest's join (docs/COOP.md, "Joining a running session"): it builds a park from a join payload and swaps it in, at the session's
/// step (never inside a link's callback or a level's frame).
/// </summary>
internal interface ICoopJoinHost
{
	/// <summary>
	/// Builds the park of <paramref name="payload"/>, a guest's from its build: the save restored with the join restore, the session state applied, standing at the cut's tick. A payload that
	/// cannot be built throws <see cref="InvalidDataException"/> (a failed check); nothing of the game's own park is touched.
	/// </summary>
	Level Build( CoopJoinPayload payload );

	/// <summary>Makes <paramref name="candidate"/> the game's park in place of <paramref name="old"/> (null on a first join), which it tears down without ending the session.</summary>
	void Install( Level? old, Level candidate );
}

/// <summary>
/// [EXT:online-coop-lockstep] A level in a co-op session (docs/COOP.md, "The level in a session"): the stream it runs over the link (the host's, or a guest's), and the session's
/// rules on the level. Only the host builds (C2), and a guest's own click is refused with a message; local pause screens stop no park; cheats and the developer kinds are refused
/// alike on every peer; a game started with developer park options shares nothing; a guest's park is never saved, not even after its session. It ends when the level closes or
/// is replaced, when the link's session ends, or when the host's stream ends it. [EXT:online-coop-snapshot-join] A guest that joins a running session, or whose park lost the stream,
/// gets the host's park from a join snapshot and swaps it in without leaving (docs/COOP.md, "Joining a running session"). The screens that start one are C2d's.
/// </summary>
internal sealed class CoopSession : IDisposable
{
	/// <summary>How long a guest waits for an assignment or an answer before it asks again.</summary>
	public const double AnswerWaitSeconds = 30;
	/// <summary>A guest's failed joins within this time count against its budget.</summary>
	public const double FailedJoinWindowSeconds = 600;
	/// <summary>The failed joins within <see cref="FailedJoinWindowSeconds"/> after which a guest leaves.</summary>
	public const int MaximumFailedJoins = 3;
	/// <summary>A desync or too far back within this time of following a capture counts as a failed join (a divergence the restore misses would otherwise resync for ever).</summary>
	public const double EarlyFailureSeconds = 180;

	private Level? level;
	private readonly ICoopStreamTransport link;
	private readonly Func<double> seconds;
	private readonly ICoopJoinHost? joinHost;
	// [EXT:online-coop-snapshot-join] the guest's side of the handshake
	private uint assigned;
	private ulong assignedFrame;
	private uint answeredGeneration;
	private bool answered;
	private CoopSnapshotReady? ready;
	private CoopJoinPayload? payload;
	private CoopSnapshotReason reason = CoopSnapshotReason.Join;
	private CoopDesyncReport? report;
	private double waitingSince;
	private double? retryAt;
	private double? followingSince;
	private bool failureAsked;
	private readonly SortedDictionary<ulong, ReadOnlyMemory<byte>> buffered = new();
	private long bufferedBytes;
	private readonly List<double> failedJoins = new();

	private CoopSession( Level? level, ICoopStreamTransport link, CoopRole role, Func<double>? seconds, ICoopJoinHost? joinHost )
	{
		this.level = level;
		this.link = link;
		this.seconds = seconds ?? CoopHostStream.DefaultSeconds;
		this.joinHost = joinHost;
		Role = role;
	}

	/// <summary>
	/// Whether this run may change the park outside the commands: the desktop game started with a developer option (every <c>--debug-*</c> and <c>--perf-*</c> option, and the smokes
	/// that build their parks directly: <c>--smoke-test</c>, <c>--tpi-smoke-test</c>, <c>--park-resume-smoke</c>, <c>--smoke-presence</c>) or the browser page of the performance
	/// harness, whose hooks stage crowds, cheats and research directly. Such a game shares no park, since a peer would never see those changes.
	/// </summary>
	internal static bool ChangesParkOutsideCommands { get; set; }

	public CoopRole Role { get; }
	public CoopHostStream? HostStream { get; private set; }
	public CoopGuestStream? GuestStream { get; private set; }

	/// <summary>The session's park on this machine; null while a joining guest has none yet.</summary>
	public Level? Level => level;

	/// <summary>[EXT:online-coop-snapshot-join] Where a guest's session stands (a host's is always following).</summary>
	public CoopGuestPhase Phase { get; private set; } = CoopGuestPhase.Following;

	/// <summary>The capture this guest was last assigned (0 none).</summary>
	public uint Assigned => assigned;

	/// <summary>The failed joins this guest counts now (within <see cref="FailedJoinWindowSeconds"/>).</summary>
	public int FailedJoins => failedJoins.Count;

	/// <summary>The frames this guest buffered for a capture's stream.</summary>
	internal int BufferedFrames => buffered.Count;

	/// <summary>Why the session ended for this game; null while it runs.</summary>
	public string? EndedWith { get; private set; }

	/// <summary>Raised once when the session ends for this game, with the reason.</summary>
	public event Action<string>? Ended;

	/// <summary>
	/// Whether <paramref name="level"/> can be shared (hosted, or joined from the host's park): a running Theme Park World park with its guests (decision Q3: never Theme Park Inc, and
	/// never the developer sandbox, whose load replaces the park outside the commands), not a visit, not in a session already, no cheat waiting for its park turn (decision Q17:
	/// it would apply inside the session), in a game not started with a developer option that changes the park outside the commands.
	/// </summary>
	public static bool CanShare( Level level, out string reason )
	{
		ArgumentNullException.ThrowIfNull( level );
		reason = level.CoopSession != null ? "The park is shared already."
			: level.IsCoopGuestPark ? "A guest's copy of a shared park is not shared again."
			: GameEditions.Current != GameEdition.ThemeParkWorld || level.IsThemeParkIncSlice || level.OriginalPark == null ? "Only Theme Park World parks are shared."
			: level.IsReadOnlyVisit ? "A visited park is not shared."
			: level.Park == null || level.Guests == null ? "Only a running park is shared."
			: level.Park.Economy.Cheats.Pending.Count > 0 ? "A cheat is waiting for the next park turn; share the park after it."
			: ChangesParkOutsideCommands ? "This game was started with developer options that change the park; restart it without them to share a park."
			: "";
		return reason.Length == 0;
	}

	/// <summary>Hosts the session <paramref name="link"/> is in with <paramref name="level"/>: the host's stream starts (the session clock with it).</summary>
	public static CoopSession Host( Level level, ICoopStreamTransport link, Func<double>? seconds = null, Func<DateTime>? localTime = null )
	{
		Require( level, link, CoopRole.Host );
		// a park that lost an economy binding is repaired while it is still this game's alone: once shared, it changes only through its commands
		level.RepairMissingEconomyBindings();
		var session = new CoopSession( level, link, CoopRole.Host, seconds, null );
		session.HostStream = new CoopHostStream( level, link, seconds, localTime );
		session.HostStream.Failed += session.OnStreamFailed;
		return session.Attach();
	}

	/// <summary>
	/// Joins the session <paramref name="link"/> is in with <paramref name="level"/>, which must be the host's park as it stands at the session's last frame: a session's identical start
	/// (generation 0). With <paramref name="joinHost"/> the guest asks for a capture when its park loses the stream, and swaps it in.
	/// </summary>
	public static CoopSession Join( Level level, ICoopStreamTransport link, Func<double>? seconds = null, ICoopJoinHost? joinHost = null )
	{
		Require( level, link, CoopRole.Guest );
		var session = new CoopSession( level, link, CoopRole.Guest, seconds, joinHost );
		session.GuestStream = new CoopGuestStream( level, link, seconds );
		session.followingSince = session.seconds();
		level.Commands.SessionRefused += session.OnRefused;
		level.IsCoopGuestPark = true;
		level.CoopSession = session;
		session.Subscribe();
		return session;
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] Joins the running session <paramref name="link"/> is in without a park (docs/COOP.md, "Joining a running session"): the guest asks for the host's park,
	/// buffers the frames from its join, downloads its capture and lets <paramref name="joinHost"/> build and swap it in at <see cref="Step"/>.
	/// </summary>
	public static CoopSession JoinRunning( ICoopStreamTransport link, ICoopJoinHost joinHost, Func<double>? seconds = null )
	{
		ArgumentNullException.ThrowIfNull( link );
		ArgumentNullException.ThrowIfNull( joinHost );
		if ( link.Session is not { } state )
			throw new InvalidOperationException( "The link is in no session." );
		if ( state.You == state.Host )
			throw new InvalidOperationException( "The host's game streams; it does not follow." );
		if ( GameEditions.Current != GameEdition.ThemeParkWorld )
			throw new InvalidOperationException( "Only Theme Park World parks are shared." );
		if ( ChangesParkOutsideCommands )
			throw new InvalidOperationException( "This game was started with developer options that change the park; restart it without them to join a shared park." );
		var session = new CoopSession( null, link, CoopRole.Guest, seconds, joinHost ) { Phase = CoopGuestPhase.Joining };
		session.Subscribe();
		session.Ask( CoopSnapshotReason.Join );
		return session;
	}

	private static void Require( Level level, ICoopStreamTransport link, CoopRole role )
	{
		ArgumentNullException.ThrowIfNull( link );
		if ( !CanShare( level, out var reason ) )
			throw new InvalidOperationException( reason );
		if ( link.Session is not { } session )
			throw new InvalidOperationException( "The link is in no session." );
		if ( (session.You == session.Host) != (role == CoopRole.Host) )
			throw new InvalidOperationException( role == CoopRole.Host ? "This game does not host the link's session." : "The host's game streams; it does not follow." );
	}

	private CoopSession Attach()
	{
		link.SessionEnded += OnSessionEnded;
		level!.CoopSession = this;
		return this;
	}

	/// <summary>A guest's subscriptions: the session's end, and the handshake's messages, frames and reconnects.</summary>
	private void Subscribe()
	{
		link.SessionEnded += OnSessionEnded;
		if ( joinHost == null )
			return;
		link.MessageReceived += OnMessage;
		link.FrameReceived += OnFrame;
		link.Resumed += OnResumed;
		link.MemberChanged += OnMemberChanged;
	}

	/// <summary>Ends the session for this game with <paramref name="reason"/> and leaves it (a host's leave ends it for everyone).</summary>
	public void End( string reason ) => Finish( reason, leave: true );

	/// <summary>The level closes (a load, a restart, a replaced park, leaving it): the session ends for this game.</summary>
	public void Dispose() => End( "The park was closed." );

	private void OnSessionEnded( CoopEnded ended ) => Finish( ended.Text, leave: false );

	/// <summary>The host's stream ended the session (and left it) because it cannot go on without skipping or losing a record.</summary>
	private void OnStreamFailed( string reason ) => Finish( reason, leave: false );

	/// <summary>A guest's own click was refused by the session's rights: its HUD says only the host builds.</summary>
	private void OnRefused( ParkCommand command ) => level?.NoteSessionRefusal();

	private void Finish( string reason, bool leave )
	{
		if ( EndedWith != null )
			return;
		EndedWith = reason;
		link.SessionEnded -= OnSessionEnded;
		link.MessageReceived -= OnMessage;
		link.FrameReceived -= OnFrame;
		link.Resumed -= OnResumed;
		link.MemberChanged -= OnMemberChanged;
		if ( level != null )
			level.Commands.SessionRefused -= OnRefused;
		if ( HostStream != null )
		{
			HostStream.Failed -= OnStreamFailed;
			HostStream.Dispose();
		}
		GuestStream?.Dispose();
		if ( level?.CoopSession == this )
			level.CoopSession = null;
		buffered.Clear();
		payload = null;
		if ( leave )
			link.Leave( reason );
		// a listener that throws keeps neither the other listeners nor a closing park from the rest of its teardown
		foreach ( var listener in Ended?.GetInvocationList() ?? Array.Empty<Delegate>() )
		{
			try { ((Action<string>)listener)( reason ); }
			catch ( Exception error ) when ( error is not OutOfMemoryException ) { Log.Warning( $"A co-op session's end listener failed: {error}" ); }
		}
	}

	// ---- [EXT:online-coop-snapshot-join] the guest's join and resync ----

	private uint HostId => link.Session?.Host ?? 0;

	/// <summary>Asks the host for a capture: <paramref name="why"/> says whether it waits or reports a failure (only a failure makes a new capture).</summary>
	private void Ask( CoopSnapshotReason why )
	{
		reason = why;
		waitingSince = seconds();
		retryAt = null;
		link.Send( HostId, new CoopSnapshotRequest( answeredGeneration, why, report ) { Generation = GuestStream?.Generation ?? 0 }.Encode() );
	}

	/// <summary>Whether the guest waits for an assignment or its answer (not while it downloads, builds or follows).</summary>
	private bool Waiting => Phase != CoopGuestPhase.Following && (assigned == 0 || !answered || failureAsked) && retryAt == null && payload == null;

	/// <summary>A failed join: three within ten minutes leave the session.</summary>
	private bool CountFailure( string why )
	{
		var now = seconds();
		failedJoins.RemoveAll( at => now - at >= FailedJoinWindowSeconds );
		failedJoins.Add( now );
		Log.Warning( $"Joining the shared park failed: {why}" );
		if ( failedJoins.Count < MaximumFailedJoins )
			return false;
		End( $"The shared park could not be joined: {why}" );
		return true;
	}

	/// <summary>
	/// The guest's step, once a game frame after the online pump and before the HUD's and the level's update: a stream that stopped asks for a capture, a guest that waited too long asks
	/// again, and a downloaded capture is built, checked and swapped in.
	/// </summary>
	public void Step()
	{
		if ( EndedWith != null || Role != CoopRole.Guest || joinHost == null )
			return;
		var now = seconds();
		if ( Phase == CoopGuestPhase.Following && GuestStream is { Stopped: true, Silenced: false } stopped )
		{
			// the park lost the stream: a desync, or frames the host no longer holds
			var why = stopped.Desync != null ? CoopSnapshotReason.Desync : CoopSnapshotReason.TooFarBack;
			report = stopped.Desync;
			Phase = CoopGuestPhase.Resyncing;
			failureAsked = true;
			if ( followingSince is { } since && now - since < EarlyFailureSeconds && CountFailure( $"the park lost the stream ({why}) soon after it joined" ) )
				return;
			Ask( why );
			return;
		}
		if ( retryAt is { } retry && now >= retry )
		{
			failureAsked = true;
			Ask( CoopSnapshotReason.Retry );
			return;
		}
		if ( Waiting && now - waitingSince >= AnswerWaitSeconds )
		{
			Ask( failureAsked ? reason : CoopSnapshotReason.NoAnswer );
			return;
		}
		if ( payload is { } downloaded )
		{
			payload = null;
			Swap( downloaded, now );
		}
	}

	/// <summary>Builds the capture's park, checks it at the cut and swaps it in; a failure keeps the old park (if any) and asks again.</summary>
	private void Swap( CoopJoinPayload downloaded, double now )
	{
		Level candidate;
		try
		{
			candidate = joinHost!.Build( downloaded );
		}
		catch ( InvalidDataException error )
		{
			Failed( CoopSnapshotReason.FailedCheck, $"the park could not be built: {error.Message}" );
			return;
		}
		var digest = CoopDigest.Compute( candidate, out var world );
		if ( candidate.TickCount != downloaded.Ticks || digest != downloaded.Digest || world != downloaded.WorldHash )
		{
			candidate.Dispose();
			Failed( CoopSnapshotReason.FailedCheck, $"the park built at tick {candidate.TickCount} differs from the host's at its cut ({downloaded.Ticks}): digest {digest:X16} / {downloaded.Digest:X16}, world {world:X16} / {downloaded.WorldHash:X16}" );
			return;
		}
		var old = level;
		var desyncs = GuestStream?.Desyncs ?? 0;
		if ( old != null )
		{
			old.Commands.SessionRefused -= OnRefused;
			if ( old.CoopSession == this )
				old.CoopSession = null;
		}
		GuestStream?.Dispose();
		GuestStream = null;
		level = null;
		candidate.IsCoopGuestPark = true;
		try
		{
			joinHost!.Install( old, candidate );
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			// the old park is gone or half gone: the session cannot go on
			End( $"The shared park could not be entered: {error.Message}" );
			return;
		}
		level = candidate;
		candidate.CoopSession = this;
		candidate.Commands.SessionRefused += OnRefused;
		GuestStream = new CoopGuestStream( candidate, link, seconds, downloaded.Generation, (downloaded.Records, downloaded.Ticks, downloaded.Frame), desyncs );
		foreach ( var (seq, frame) in buffered.Where( pair => pair.Key > downloaded.Frame ).ToList() )
			GuestStream.Take( seq, frame );
		buffered.Clear();
		bufferedBytes = 0;
		Phase = CoopGuestPhase.Following;
		followingSince = now;
		failureAsked = false;
		report = null;
		ready = null;
	}

	/// <summary>A failed join: counted, then a new capture is asked for (unless the budget is spent).</summary>
	private void Failed( CoopSnapshotReason why, string what )
	{
		failureAsked = true;
		if ( CountFailure( what ) )
			return;
		Ask( why );
	}

	private void OnMessage( CoopRelayed relayed )
	{
		if ( EndedWith != null || relayed.From != HostId )
			return;
		CoopStreamMessage message;
		try
		{
			message = CoopStreamMessage.Decode( relayed.Payload.Span );
		}
		catch ( InvalidDataException )
		{
			// the stream logs what it cannot read
			return;
		}
		var generation = message.Generation;
		switch ( message )
		{
			case CoopSnapshotAssigned assignment:
				if ( generation > assigned )
					Assign( generation, assignment.Frame );
				else
					waitingSince = seconds();
				break;
			case CoopSnapshotReady answer:
				if ( generation > assigned )
					Assign( generation, 0 );
				if ( generation != assigned || answered )
					break;
				answered = true;
				answeredGeneration = generation;
				failureAsked = false;
				ready = answer;
				link.DownloadSnapshot( answer.Sha256, answer.Bytes, result => Downloaded( generation, result ) );
				break;
			case CoopSnapshotFailed failure:
				// a permanent failure ends the join whatever it names: the member is out of step or the park cannot be shared
				if ( failure.Permanent && generation >= answeredGeneration )
				{
					End( $"The shared park cannot be joined ({failure.Reason})." );
					break;
				}
				if ( generation > assigned )
					Assign( generation, 0 );
				if ( generation != assigned || answered )
					break;
				answered = true;
				answeredGeneration = generation;
				failureAsked = false;
				retryAt = seconds() + Math.Max( 1, failure.RetryAfterSeconds );
				break;
		}
	}

	/// <summary>A newer capture is assigned: the old stream stops and goes silent, any older download or build is abandoned, and the frames from now on are buffered.</summary>
	private void Assign( uint generation, ulong frame )
	{
		assigned = generation;
		assignedFrame = Math.Max( assignedFrame, frame );
		answered = false;
		ready = null;
		payload = null;
		retryAt = null;
		failureAsked = false;
		reason = CoopSnapshotReason.NoAnswer;
		waitingSince = seconds();
		if ( GuestStream is { } stream && stream.Generation < generation )
		{
			stream.Silence();
			Phase = CoopGuestPhase.Resyncing;
		}
		foreach ( var old in buffered.Keys.Where( seq => seq <= assignedFrame ).ToList() )
		{
			bufferedBytes -= buffered[old].Length;
			buffered.Remove( old );
		}
	}

	private void Downloaded( uint generation, CoopSnapshotTransfer result )
	{
		if ( EndedWith != null || generation != assigned || ready is not { } answer )
			return;
		if ( result.Missing )
		{
			// the server keeps a session's newest two snapshots: several members' captures rotated this one out
			failureAsked = true;
			Ask( CoopSnapshotReason.Missing );
			return;
		}
		if ( !result.Succeeded )
		{
			Failed( CoopSnapshotReason.FailedDownload, $"the park's download failed: {result.Failure}" );
			return;
		}
		try
		{
			var read = CoopJoinPayload.Decode( result.Payload );
			if ( read.Generation != generation || read.Records != answer.Records || read.Ticks != answer.Ticks || read.Frame != answer.Frame || read.Digest != answer.Digest
				|| read.WorldHash != answer.WorldHash )
				throw new InvalidDataException( "The snapshot is not the capture the host named." );
			payload = read;
		}
		catch ( InvalidDataException error )
		{
			Failed( CoopSnapshotReason.FailedCheck, error.Message );
		}
	}

	/// <summary>The frames while no stream of the current capture runs, buffered for it (within the stream's caps).</summary>
	private void OnFrame( CoopFrame frame )
	{
		if ( EndedWith != null || Phase == CoopGuestPhase.Following || frame.Seq <= assignedFrame || buffered.ContainsKey( frame.Seq )
			|| buffered.Count >= CoopGuestStream.MaximumHeldFrames || bufferedBytes + frame.Payload.Length > CoopGuestStream.MaximumHeldBytes )
			return;
		buffered[frame.Seq] = frame.Payload;
		bufferedBytes += frame.Payload.Length;
	}

	/// <summary>A resumed link: a request sent while the socket was down was lost.</summary>
	private void OnResumed( CoopSessionState state )
	{
		if ( EndedWith == null && Waiting )
			Ask( failureAsked ? reason : CoopSnapshotReason.NoAnswer );
	}

	/// <summary>The host is back: a request sent while it was away was refused.</summary>
	private void OnMemberChanged( CoopMemberChanged change )
	{
		if ( EndedWith == null && change.Member.Id == HostId && change.State == CoopMemberState.Connected && Waiting )
			Ask( failureAsked ? reason : CoopSnapshotReason.NoAnswer );
	}
}

public partial class Level
{
	/// <summary>[EXT:online-coop-lockstep] The co-op session this level is in (<see cref="OpenTPW.CoopSession"/>), or null.</summary>
	internal CoopSession? CoopSession { get; set; }

	/// <summary>[EXT:online-coop-lockstep] This level's part in a co-op session.</summary>
	internal CoopRole CoopRole => CoopSession?.Role ?? CoopRole.None;

	/// <summary>[EXT:online-coop-lockstep] Whether this level joined a co-op session as a guest: its park is the host's, which nothing saves, also after the session ended.</summary>
	internal bool IsCoopGuestPark { get; set; }

	/// <summary>[EXT:online-coop-lockstep] How many of this machine's clicks the session refused (a guest: only the host builds); the HUD says so when it changes.</summary>
	internal int SessionRefusals { get; private set; }

	internal void NoteSessionRefusal() => SessionRefusals++;
}
