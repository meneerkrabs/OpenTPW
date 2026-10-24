using System.Diagnostics;
using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>[EXT:online-coop-lockstep] A level's part in a co-op session (<see cref="Level.CoopSession"/>).</summary>
public enum CoopRole
{
	None,
	Host,
	Guest
}

/// <summary>[EXT:online-coop-session] Why a park cannot be shared (<see cref="CoopSession.ShareRefusal"/>).</summary>
public enum CoopShareRefusal
{
	None,
	/// <summary>The park is in a session already.</summary>
	Shared,
	/// <summary>A guest's copy of a shared park.</summary>
	GuestCopy,
	/// <summary>Not a Theme Park World park (Theme Park Inc, or the developer sandbox).</summary>
	NotThemeParkWorld,
	/// <summary>A visited park.</summary>
	Visit,
	/// <summary>A park without its economy or guests.</summary>
	NotRunning,
	/// <summary>A cheat waits for the next park turn.</summary>
	CheatPending,
	/// <summary>The game was started with developer options that change the park outside the commands.</summary>
	DeveloperOptions
}

/// <summary>[EXT:online-coop-snapshot-join] Where a guest's session stands (docs/COOP.md, "Joining a running session").</summary>
/// <summary>[EXT:online-coop-session] Why this game cannot join a shared park (C2d-3; <see cref="CoopSession.JoinRefusalKind"/>).</summary>
public enum CoopJoinRefusal
{
	/// <summary>It can.</summary>
	None,
	/// <summary>The game runs another edition than Theme Park World, whose parks alone are shared.</summary>
	OtherEdition,
	/// <summary>The run was started with a developer option that changes the park outside the commands.</summary>
	DeveloperOptions
}

/// <summary>[EXT:online-coop-session] Why a session ended for this game, as the screens say it (C2d-3; <see cref="CoopSession.EndCause"/>).</summary>
public enum CoopSessionEnd
{
	/// <summary>It runs.</summary>
	None,
	/// <summary>This game left it: a leave, a closed park, a logout.</summary>
	Left,
	/// <summary>The server ended it for this game (<see cref="CoopSession.ServerReason"/>: the host left, a removal, the host away too long, a deleted account, another build).</summary>
	Server,
	/// <summary>The link to the server was lost or taken over before the session could go on.</summary>
	LinkLost,
	/// <summary>The guest's park could not be built or kept: its joins failed again and again.</summary>
	JoinFailed,
	/// <summary>The host's stream could not go on.</summary>
	StreamFailed
}

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
/// [EXT:online-coop-snapshot-join] What the game does for a guest's join (docs/COOP.md, "Joining a running session"): it builds a park from a join payload over frames and swaps it in,
/// at the session's step (never inside a link's callback or a level's frame).
/// </summary>
internal interface ICoopJoinHost
{
	/// <summary>
	/// Starts building the park of <paramref name="payload"/>, a guest's from its build: the save restored with the join restore, the session state applied, standing at the cut's tick. It
	/// does nothing heavy here: the session steps the build once a frame (<see cref="ICoopJoinBuild.Step"/>). A throw here or in a step is a failed check (any exception but running out of
	/// memory, or <see cref="CoopJoinRefusedException"/>, which ends the join) and leaves nothing behind: no candidate, and nothing of the game's own park touched.
	/// </summary>
	ICoopJoinBuild Build( CoopJoinPayload payload );

	/// <summary>
	/// Makes the completed build's candidate the game's park in place of <paramref name="old"/> (null on a first join), which it tears down without ending the session. A throw ends the
	/// session, and the candidate must not have become the game's park then: the session abandons the build.
	/// </summary>
	void Install( Level? old, ICoopJoinBuild build );
}

/// <summary>[EXT:online-coop-snapshot-join] A guest's park being built from a join payload, over frames (<see cref="ICoopJoinHost.Build"/>).</summary>
internal interface ICoopJoinBuild
{
	/// <summary>
	/// One frame's slice of the build: true once the candidate is complete, and from then on without doing more. The session checks the candidate at the cut in the frame this
	/// first returns true and swaps it in the next (docs/COOP.md, "The game's join").
	/// </summary>
	bool Step();

	/// <summary>The completed candidate, set aside from the game's scene (valid once <see cref="Step"/> returned true).</summary>
	Level Candidate { get; }

	/// <summary>Releases what the build made (the candidate, its HUD and advisor, its scene); once only, in any state, and nothing once the candidate is installed.</summary>
	void Abandon();

	/// <summary>The steps of the park's level construction this build has run so far (a probe: the native join smoke abandons a build part way through them); 0 for a build without one.</summary>
	int LevelSteps => 0;

	/// <summary>The entities this build has staged in its scene so far (a probe: the native join smoke abandons a build only once it has staged some); 0 for a build without a scene.</summary>
	int StagedEntities => 0;

	/// <summary>Whether this build's level is part way through restoring the saved objects, and how many it placed so far (probes: the native join smoke abandons a build there).</summary>
	bool RestoringObjects => false;

	/// <inheritdoc cref="RestoringObjects"/>
	int PlacedObjects => 0;

	/// <summary>What the build's stages cost so far, on the wall clock (docs/COOP.md, "Measurements"); null for a build that keeps none.</summary>
	CoopBuildTimings? Timings => null;
}

/// <summary>
/// [EXT:online-coop-snapshot-join] A join every build of the payload would refuse, proven by a completed comparison (this game's simulation mods or files differ from the host's): the join
/// ends at once with the reason instead of asking for more captures.
/// </summary>
internal sealed class CoopJoinRefusedException( string message ) : Exception( message );

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
	/// <summary>How long a guest waits before it downloads again when the server asked it to wait, unless the server said.</summary>
	public const double BusyRetrySeconds = 5;
	/// <summary>How long the game's build of a capture may take before it counts as a failed check (nothing asks again while it runs).</summary>
	public const double BuildDeadlineSeconds = 300;
	/// <summary>The downloads a server may ask to be tried again in a row before the download counts as failed.</summary>
	public const int MaximumBusyRetries = 10;
	/// <summary>
	/// The longest single wait for a server that asked to be tried again (an address limit names its whole minute): the host keeps the frames after the cut for the waiting member, so the
	/// ten retries keep it to about ten minutes in all, which the host's history holds; a longer wait ends the join with the server's reason instead (the player may join again later).
	/// </summary>
	public const double MaximumBusyWaitSeconds = 60;

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
	/// <summary>When the snapshot the server asked to wait for is downloaded again; null while none waits.</summary>
	private double? downloadAt;
	private int busyRetries;
	private double? followingSince;
	/// <summary>The game's build of the downloaded capture while it runs over frames, with its payload and when it began.</summary>
	private ICoopJoinBuild? building;
	private CoopJoinPayload? buildingPayload;
	private double buildStarted;
	/// <summary>Set once the completed build passed its check at the cut, in the frame it completed in; the swap follows in the next (docs/COOP.md, "The game's join").</summary>
	private bool buildChecked;
	private bool failureAsked;
	private readonly SortedDictionary<ulong, ReadOnlyMemory<byte>> buffered = new();
	private long bufferedBytes;
	private readonly List<double> failedJoins = new();
	/// <summary>The capture's timings while its download, build, check and swap run (docs/COOP.md, "Measurements"); the finished ones, newest last.</summary>
	private CoopJoinTimings? timing;
	private long downloadPhaseStarted, downloadStarted;
	private readonly List<CoopJoinTimings> joinTimings = new();
	/// <summary>The swapped-in park's catch-up while it runs: its timings, the tick it started at, when, and the frames so far.</summary>
	private (CoopJoinTimings Timing, long Tick, ulong CutFrame, long Started, int Frames)? catchUp;
	/// <summary>The most finished timings kept.</summary>
	public const int MaximumJoinTimings = 16;

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

	/// <summary>What the guest's finished builds of captures cost, oldest first (at most <see cref="MaximumJoinTimings"/>; a measurement only).</summary>
	public IReadOnlyList<CoopJoinTimings> JoinTimings => joinTimings;

	/// <summary>The game builds a downloaded capture (it holds the game's one construction scene meanwhile).</summary>
	internal bool IsBuilding => building != null;

	/// <summary>The build on its way, if any (a probe for the native join smoke: <see cref="ICoopJoinBuild.LevelSteps"/>, <see cref="ICoopJoinBuild.StagedEntities"/>).</summary>
	internal ICoopJoinBuild? Building => building;

	/// <summary>The frames this guest buffered for a capture's stream.</summary>
	internal int BufferedFrames => buffered.Count;

	/// <summary>Why the session ended for this game; null while it runs.</summary>
	public string? EndedWith { get; private set; }

	/// <summary>[EXT:online-coop-session] What ended the session for this game (C2d-3: the screens say it in the player's language, never the server's English).</summary>
	public CoopSessionEnd EndCause { get; private set; }

	/// <summary>The server's reason, for <see cref="CoopSessionEnd.Server"/>; <see cref="CoopReason.None"/> otherwise.</summary>
	public CoopReason ServerReason { get; private set; }

	/// <summary>Raised once when the session ends for this game, with the reason.</summary>
	public event Action<string>? Ended;

	/// <summary>
	/// Whether <paramref name="level"/> can be shared (hosted, or joined from the host's park): a running Theme Park World park with its guests (decision Q3: never Theme Park Inc, and
	/// never the developer sandbox, whose load replaces the park outside the commands), not a visit, not in a session already, no cheat waiting for its park turn (decision Q17:
	/// it would apply inside the session), in a game not started with a developer option that changes the park outside the commands.
	/// </summary>
	public static bool CanShare( Level level, out string reason )
	{
		var refusal = ShareRefusal( level );
		reason = refusal switch
		{
			CoopShareRefusal.None => "",
			CoopShareRefusal.Shared => "The park is shared already.",
			CoopShareRefusal.GuestCopy => "A guest's copy of a shared park is not shared again.",
			CoopShareRefusal.NotThemeParkWorld => "Only Theme Park World parks are shared.",
			CoopShareRefusal.Visit => "A visited park is not shared.",
			CoopShareRefusal.NotRunning => "Only a running park is shared.",
			CoopShareRefusal.CheatPending => "A cheat is waiting for the next park turn; share the park after it.",
			_ => "This game was started with developer options that change the park; restart it without them to share a park.",
		};
		return refusal == CoopShareRefusal.None;
	}

	/// <summary>[EXT:online-coop-session] Why <paramref name="level"/> cannot be shared (<see cref="CanShare"/>), as a code the game's screens say in the player's language.</summary>
	public static CoopShareRefusal ShareRefusal( Level level )
	{
		ArgumentNullException.ThrowIfNull( level );
		return level.CoopSession != null ? CoopShareRefusal.Shared
			: level.IsCoopGuestPark ? CoopShareRefusal.GuestCopy
			: GameEditions.Current != GameEdition.ThemeParkWorld || level.IsThemeParkIncSlice || level.OriginalPark == null ? CoopShareRefusal.NotThemeParkWorld
			: level.IsReadOnlyVisit ? CoopShareRefusal.Visit
			: level.Park == null || level.Guests == null ? CoopShareRefusal.NotRunning
			: level.Park.Economy.Cheats.Pending.Count > 0 ? CoopShareRefusal.CheatPending
			: ChangesParkOutsideCommands ? CoopShareRefusal.DeveloperOptions
			: CoopShareRefusal.None;
	}

	/// <summary>
	/// What a host is to run over the frames before it hosts <paramref name="level"/> (<see cref="Host"/>), a step a frame: the session digest's serialization once ahead
	/// (<see cref="WorldStateHash.SerializationWarmupSteps"/>), so the stream's first checkpoint and a capture's cut run none of its code for the first time (about 50 ms of a
	/// page's frame otherwise, docs/COOP.md "The browser's host"). The park is only read; nothing when it has no economy. The smoke's host runs it; the game's own sharing does
	/// not yet.
	/// </summary>
	public static IEnumerable<bool> PrepareHostSteps( Level level )
	{
		ArgumentNullException.ThrowIfNull( level );
		return level.Park?.Economy is { } economy ? WorldStateHash.SerializationWarmupSteps( economy ) : Array.Empty<bool>();
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
		if ( JoinRefusal is { } refusal )
			throw new InvalidOperationException( refusal );
		var session = new CoopSession( null, link, CoopRole.Guest, seconds, joinHost ) { Phase = CoopGuestPhase.Joining };
		session.Subscribe();
		session.Ask( CoopSnapshotReason.Join );
		return session;
	}

	/// <summary>
	/// [EXT:online-coop-session] Why this game cannot join a shared park; <see cref="CoopJoinRefusal.None"/> when it can. Only Theme Park World parks are shared, and a run started with
	/// a developer option that changes the park outside the commands cannot follow one (C2d-3 checks it before the join goes out).
	/// </summary>
	internal static CoopJoinRefusal JoinRefusalKind => GameEditions.Current != GameEdition.ThemeParkWorld ? CoopJoinRefusal.OtherEdition
		: ChangesParkOutsideCommands ? CoopJoinRefusal.DeveloperOptions : CoopJoinRefusal.None;

	/// <summary><see cref="JoinRefusalKind"/> in English, for the log; null when this game can join.</summary>
	internal static string? JoinRefusal => JoinRefusalKind switch
	{
		CoopJoinRefusal.OtherEdition => "Only Theme Park World parks are shared.",
		CoopJoinRefusal.DeveloperOptions => "This game was started with developer options that change the park; restart it without them to join a shared park.",
		_ => null,
	};

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
	public void End( string reason ) => Finish( reason, leave: true, CoopSessionEnd.Left );

	/// <summary>The level closes (a load, a restart, a replaced park, leaving it): the session ends for this game.</summary>
	public void Dispose() => End( "The park was closed." );

	/// <summary>The link heard the end: the server's (with its reason), this game's own close with a leave, or a link that stopped.</summary>
	private void OnSessionEnded( CoopEnded ended ) => Finish( ended.Text, leave: false, ended.Reason switch
	{
		CoopReason.Left => CoopSessionEnd.Left,
		CoopReason.Unknown => CoopSessionEnd.LinkLost,
		_ => CoopSessionEnd.Server,
	}, ended.Reason is CoopReason.Left or CoopReason.Unknown ? CoopReason.None : ended.Reason );

	/// <summary>The guest's park cannot be built, entered or kept (a refused or failed build, a failed swap, a capture the server no longer serves): the session ends and is left.</summary>
	private void FailJoin( string reason ) => Finish( reason, leave: true, CoopSessionEnd.JoinFailed );

	/// <summary>The host's stream ended the session (and left it) because it cannot go on without skipping or losing a record.</summary>
	private void OnStreamFailed( string reason ) => Finish( reason, leave: false, CoopSessionEnd.StreamFailed );

	/// <summary>A guest's own click was refused by the session's rights: its HUD says only the host builds.</summary>
	private void OnRefused( ParkCommand command ) => level?.NoteSessionRefusal();

	private void Finish( string reason, bool leave, CoopSessionEnd cause, CoopReason serverReason = CoopReason.None )
	{
		if ( EndedWith != null )
			return;
		EndedWith = reason;
		EndCause = cause;
		ServerReason = serverReason;
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
		AbandonBuild( outcome: "ended" );
		FinishTiming( "ended" );
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
	private bool Waiting => Phase != CoopGuestPhase.Following && (assigned == 0 || !answered || failureAsked) && retryAt == null && downloadAt == null && payload == null
		&& building == null;

	/// <summary>A failed join: three within ten minutes leave the session.</summary>
	private bool CountFailure( string why )
	{
		var now = seconds();
		failedJoins.RemoveAll( at => now - at >= FailedJoinWindowSeconds );
		failedJoins.Add( now );
		Log.Warning( $"Joining the shared park failed: {why}" );
		if ( failedJoins.Count < MaximumFailedJoins )
			return false;
		FailJoin( $"The shared park could not be joined: {why}" );
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
		StepCatchUp();
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
		if ( downloadAt is { } again && now >= again && ready is { } named )
		{
			downloadAt = null;
			var generation = assigned;
			StartDownload( generation );
			link.DownloadSnapshot( named.Sha256, named.Bytes, result => Downloaded( generation, result ) );
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
			StartBuild( downloaded, now );
		}
		if ( building != null )
			StepBuild( now );
	}

	/// <summary>Starts the game's build of a downloaded capture, which the session then steps once a frame.</summary>
	private void StartBuild( CoopJoinPayload downloaded, double now )
	{
		// the answer is spent: a second download answer for it builds nothing again
		ready = null;
		try
		{
			building = joinHost!.Build( downloaded );
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			BuildFailed( error );
			return;
		}
		buildingPayload = downloaded;
		buildStarted = now;
		buildChecked = false;
	}

	/// <summary>One frame of the game's build; the frame it completes in checks the candidate at the cut, and the next swaps it in.</summary>
	private void StepBuild( double now )
	{
		var build = building!;
		var downloaded = buildingPayload!;
		// a build checked in time is swapped in the next frame, whatever the clock says then
		if ( !buildChecked && now - buildStarted >= BuildDeadlineSeconds )
		{
			AbandonBuild( outcome: "timed out" );
			Failed( CoopSnapshotReason.FailedCheck, $"the park's build did not finish within {BuildDeadlineSeconds} seconds" );
			return;
		}
		bool complete;
		try
		{
			complete = build.Step();
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			AbandonBuild( outcome: null );
			BuildFailed( error, build );
			return;
		}
		if ( !complete )
			return;
		building = null;
		buildingPayload = null;
		// the check at the cut takes the frame the build completed in and the swap the next, so the two never share a frame; the candidate waits set aside, unchanged, between
		// them. A failed check ends the build there, its handlers finding no build on its way, as the swap's do
		if ( !buildChecked )
		{
			if ( Check( build, downloaded ) )
				(building, buildingPayload, buildChecked) = (build, downloaded, true);
			return;
		}
		buildChecked = false;
		Swap( build, downloaded, now );
	}

	/// <summary>
	/// A build that threw: a refusal every build of the payload would meet ends the join; anything else the game's restore refuses is a failed check, and the guest's own park, if any,
	/// goes on waiting.
	/// </summary>
	private void BuildFailed( Exception error, ICoopJoinBuild? build = null )
	{
		FinishTiming( error is CoopJoinRefusedException ? "refused" : "failed", build );
		if ( error is CoopJoinRefusedException )
			FailJoin( $"The shared park cannot be joined: {error.Message}" );
		else
			Failed( CoopSnapshotReason.FailedCheck, $"the park could not be built: {error.Message}" );
	}

	/// <summary>Abandons the build on its way, if any (a newer capture, the session's end, a failure); its timings finish with <paramref name="outcome"/>, or a failure finishes them (null).</summary>
	private void AbandonBuild( string? outcome = "abandoned" )
	{
		if ( building is not { } build )
			return;
		building = null;
		buildingPayload = null;
		buildChecked = false;
		Release( build );
		if ( outcome != null )
			FinishTiming( outcome, build );
	}

	/// <summary>Checks the built candidate at the cut; false when it failed (the candidate released, the failure counted, the old park, if any, kept, a new capture asked for).</summary>
	private bool Check( ICoopJoinBuild build, CoopJoinPayload downloaded )
	{
		if ( Candidate( build ) is not { } candidate )
			return false;
		ulong digest, world;
		var checkStarted = Stopwatch.GetTimestamp();
		try
		{
			using ( FrameMarks.Measure( "coop.check" ) )
				digest = CoopDigest.Compute( candidate, out world );
		}
		// a park built so far that it cannot even be hashed fails its check like one that differs
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Release( build );
			FinishTiming( "failed its check", build );
			Failed( CoopSnapshotReason.FailedCheck, $"the park built could not be checked: {error.Message}" );
			return false;
		}
		if ( timing != null )
			timing.CheckMilliseconds = CoopClock.Since( checkStarted );
		if ( candidate.TickCount != downloaded.Ticks || digest != downloaded.Digest || world != downloaded.WorldHash )
		{
			var built = candidate.TickCount;
			Release( build );
			FinishTiming( "failed its check", build );
			Failed( CoopSnapshotReason.FailedCheck, $"the park built at tick {built} differs from the host's at its cut ({downloaded.Ticks}): digest {digest:X16} / {downloaded.Digest:X16}, "
				+ $"world {world:X16} / {downloaded.WorldHash:X16}" );
			return false;
		}
		return true;
	}

	/// <summary>The completed build's candidate, or null when the build has none (released, the failure counted, a new capture asked for).</summary>
	private Level? Candidate( ICoopJoinBuild build )
	{
		try
		{
			return build.Candidate;
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Release( build );
			FinishTiming( "failed", build );
			Failed( CoopSnapshotReason.FailedCheck, $"the park built is missing: {error.Message}" );
			return null;
		}
	}

	/// <summary>Swaps the checked candidate in, the frame after its check; a failure to enter it ends the session.</summary>
	private void Swap( ICoopJoinBuild build, CoopJoinPayload downloaded, double now )
	{
		if ( Candidate( build ) is not { } candidate )
			return;
		var swapStarted = Stopwatch.GetTimestamp();
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
			using ( FrameMarks.Measure( "coop.install" ) )
				joinHost!.Install( old, build );
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			// the old park is gone or half gone: the session cannot go on
			Release( build );
			FinishTiming( "failed", build );
			FailJoin( $"The shared park could not be entered: {error.Message}" );
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
		// frames after the last one buffered may have been lost on their way (a socket that dropped during the download, whose resume no stream heard): they are asked for now, since an
		// idle host publishes nothing that would show the gap
		GuestStream.RequestMissing();
		// the swap's frame has the park's first update: its ticks wait for the next frame
		GuestStream.HoldTicksOneFrame();
		Phase = CoopGuestPhase.Following;
		followingSince = now;
		failureAsked = false;
		report = null;
		ready = null;
		if ( timing is { } swapped )
		{
			swapped.SwapMilliseconds = CoopClock.Since( swapStarted );
			catchUp = (swapped, candidate.TickCount, downloaded.Frame, Stopwatch.GetTimestamp(), 0);
		}
		FinishTiming( old == null ? "joined" : "resynced", build );
	}

	/// <summary>Lets the game release a build that is not swapped in; a release that throws is logged, so the join goes on to its failure.</summary>
	private void Release( ICoopJoinBuild build )
	{
		try
		{
			build.Abandon();
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Log.Warning( $"A shared park's candidate could not be released: {error}" );
		}
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
				StartDownload( generation );
				link.DownloadSnapshot( answer.Sha256, answer.Bytes, result => Downloaded( generation, result ) );
				break;
			case CoopSnapshotFailed failure:
				// a permanent failure ends the join whatever it names: the member is out of step or the park cannot be shared
				if ( failure.Permanent && generation >= answeredGeneration )
				{
					FailJoin( $"The shared park cannot be joined ({failure.Reason})." );
					break;
				}
				if ( generation > assigned )
					Assign( generation, 0 );
				if ( generation != assigned || answered )
					break;
				answered = true;
				answeredGeneration = generation;
				failureAsked = false;
				retryAt = seconds() + Math.Clamp( failure.RetryAfterSeconds, 1, CoopHostStream.MaximumRetrySeconds );
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
		AbandonBuild();
		// an older capture that was still on its way (downloading, waiting for the server, downloaded but not built)
		if ( timing is { } older && older.Generation < generation )
			FinishTiming( "abandoned" );
		retryAt = null;
		downloadAt = null;
		busyRetries = 0;
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
		if ( timing is { } downloading && downloading.Generation == generation )
		{
			downloading.LastAttemptSeconds = CoopClock.Since( downloadStarted ) / 1000;
			downloading.DownloadSeconds = CoopClock.Since( downloadPhaseStarted ) / 1000;
			downloading.PayloadBytes = result.Payload?.Length ?? 0;
		}
		if ( result.Missing )
		{
			// the server keeps a session's newest two snapshots: several members' captures rotated this one out
			FinishTiming( "failed" );
			failureAsked = true;
			Ask( CoopSnapshotReason.Missing );
			return;
		}
		if ( result.Permanent )
		{
			// the server no longer counts this game a player of the session
			FinishTiming( "failed" );
			FailJoin( $"The shared park cannot be downloaded: {result.Failure}" );
			return;
		}
		if ( result.Busy )
		{
			// the server asked to wait (too many downloads, a server error): the same snapshot again after the wait it named, no failed join and no new capture; a wait longer than the
			// host keeps the frames after the cut would end in a resync anyway, so the join ends with the server's reason
			var wait = result.RetryAfter?.TotalSeconds ?? BusyRetrySeconds;
			if ( wait > MaximumBusyWaitSeconds )
			{
				FinishTiming( "failed" );
				FailJoin( $"The shared park cannot be downloaded now: {result.Failure}" );
				return;
			}
			if ( busyRetries < MaximumBusyRetries )
			{
				busyRetries++;
				downloadAt = seconds() + Math.Max( 1, wait );
				return;
			}
		}
		if ( !result.Succeeded )
		{
			FinishTiming( "failed" );
			Failed( CoopSnapshotReason.FailedDownload, $"the park's download failed: {result.Failure}" );
			return;
		}
		var decodeStarted = Stopwatch.GetTimestamp();
		try
		{
			var read = CoopJoinPayload.Decode( result.Payload );
			if ( timing != null )
				timing.DecodeMilliseconds = CoopClock.Since( decodeStarted );
			if ( read.Generation != generation || read.Records != answer.Records || read.Ticks != answer.Ticks || read.Frame != answer.Frame || read.Digest != answer.Digest
				|| read.WorldHash != answer.WorldHash )
				throw new InvalidDataException( "The snapshot is not the capture the host named." );
			payload = read;
		}
		catch ( InvalidDataException error )
		{
			FinishTiming( "failed its check" );
			Failed( CoopSnapshotReason.FailedCheck, error.Message );
		}
	}

	/// <summary>[EXT:online-coop-snapshot-join] The download of capture <paramref name="generation"/> starts (again, after a server's wait): its timings begin, or go on.</summary>
	private void StartDownload( uint generation )
	{
		if ( timing is not { } current || current.Generation != generation )
		{
			// a newer capture's: whatever the older one's came to, it was abandoned
			FinishTiming( "abandoned" );
			timing = new CoopJoinTimings { Generation = generation };
			downloadPhaseStarted = Stopwatch.GetTimestamp();
		}
		timing.DownloadAttempts++;
		downloadStarted = Stopwatch.GetTimestamp();
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The current capture's timings end with <paramref name="outcome"/>, the stages of <paramref name="build"/> (if any) with them: kept (the newest
	/// <see cref="MaximumJoinTimings"/>) and logged.
	/// </summary>
	private void FinishTiming( string outcome, ICoopJoinBuild? build = null )
	{
		if ( timing is not { } finished )
			return;
		timing = null;
		finished.Outcome = outcome;
		if ( build?.Timings is { } stages )
		{
			finished.Build = stages.Stages.ToList();
			finished.LongSteps = stages.LongSteps.ToList();
			finished.BuildFrames = stages.Frames;
			finished.LongestBuildFrameMilliseconds = stages.LongestFrameMilliseconds;
			finished.ProcessBuild = stages.ProcessBuild;
			finished.Sections = new Dictionary<string, double>( stages.Sections );
		}
		joinTimings.Add( finished );
		if ( joinTimings.Count > MaximumJoinTimings )
			joinTimings.RemoveAt( 0 );
		Log.Trace( $"Co-op {finished}." );
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A park swapped in has caught up once a frame point of its stream applied a frame of the host's from after the cut and left it following within its
	/// window: its catch-up's ticks, live updates and time, read at the next session step (the frames it had then; one still on its way is not known). Before such a frame the host's
	/// place is not known: a fresh stream at the cut follows by its own horizon. A frame point, not the session step, decides, since frames that came since the last one wait at the step.
	/// </summary>
	private void StepCatchUp()
	{
		if ( catchUp is not { } running )
			return;
		if ( GuestStream is not { } stream || level == null )
		{
			catchUp = null;
			return;
		}
		// the frame before this step ran a live update of the park (the swap's own frame the first)
		var frames = running.Frames + 1;
		if ( stream.FollowingThrough <= running.CutFrame )
		{
			catchUp = running with { Frames = frames };
			return;
		}
		catchUp = null;
		running.Timing.CatchUpTicks = level.TickCount - running.Tick;
		running.Timing.CatchUpFrames = frames;
		running.Timing.CatchUpSeconds = CoopClock.Since( running.Started ) / 1000;
		Log.Trace( $"Co-op join {running.Timing.Generation} caught up with the host after {running.Timing.CatchUpTicks} ticks in {frames} frames "
			+ $"({running.Timing.CatchUpSeconds:0.000} s)." );
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
