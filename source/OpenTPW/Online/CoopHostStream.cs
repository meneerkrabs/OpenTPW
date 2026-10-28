using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>[EXT:online-coop-lockstep] The session stream a level's frame runs its simulation step through (<see cref="Level.SessionStream"/>): a host's or a guest's.</summary>
internal interface ILevelSessionStream
{
	/// <summary>One game frame's simulation step: <paramref name="frameSeconds"/> of real time at <paramref name="timeScale"/>, outside any tick.</summary>
	void AdvanceSimulation( double frameSeconds, float timeScale );
}

/// <summary>[EXT:online-coop-lockstep] A session's rights (C2: the host builds, the guests watch): only player 0's commands run.</summary>
internal sealed class CoopHostOnlyRights : IParkCommandRights
{
	public static CoopHostOnlyRights Instance { get; } = new();
	public bool Allows( int player, ParkCommandKind kind ) => player == ParkCommands.LocalPlayer;
}

/// <summary>
/// [EXT:online-coop-lockstep] The host's command stream (docs/COOP.md, "Sessions"): every command the host's funnel applies becomes a record of the stream, published in frames
/// with a horizon (every tick below it ran on the host, and every record before it is out) and the session digest at checkpoints; the guests replay it at the host's ticks. It
/// resends what a guest missed, holds the host's park a few seconds ahead of its slowest following guest, and issues the session clock.
/// </summary>
internal sealed class CoopHostStream : ILevelSessionStream, IDisposable
{
	/// <summary>The ticks between two tick checkpoints at the normal speed: one park second.</summary>
	public const int CheckpointTicks = FixedStepClock.TicksPerSecond;

	/// <summary>
	/// The ticks between two tick checkpoints at <paramref name="speed"/>: <see cref="CheckpointTicks"/> times the speed's factor, so the host takes one digest a second of real
	/// time at every speed (a full park's digest costs about 10 ms in the browser build, docs/COOP.md "Measurements"); the intervals divide each other, so a change of speed
	/// keeps them aligned.
	/// </summary>
	public static int CheckpointInterval( GameSpeed speed ) => CheckpointTicks * Math.Max( 1, (int)GameSpeedScale.Of( speed ) );
	/// <summary>The least time between two published frames.</summary>
	public const double PublishSeconds = 0.1;
	/// <summary>How far, in park seconds at the current speed, the host may run ahead of its slowest following guest.</summary>
	public const double WindowSeconds = 3;
	/// <summary>A guest that has not reported for this long is not waited for until it is back within the window.</summary>
	public const double StallSeconds = 10;
	/// <summary>A catching-up guest that holds every frame and has not come within the window for this long is told to resync instead.</summary>
	public const double CatchUpSeconds = 60;
	public const int MaximumHistoryFrames = 10_000;
	public const long MaximumHistoryBytes = 32L << 20;
	/// <summary>The resent bytes a guest has not acknowledged yet, at most.</summary>
	public const int MaximumResendInFlight = 1 << 20;
	/// <summary>The resent bytes a second, for every guest together.</summary>
	public const int ResendBytesPerSecond = 1 << 20;
	/// <summary>A repeated resend request within this time of the last resent message leaves the copy on its way alone.</summary>
	public const double ResendRetrySeconds = 2;
	/// <summary>A guest told it is too far back that still asks or reports otherwise is told again, at most this often (a message is at most once).</summary>
	public const double TooFarBackRepeatSeconds = 1;
	/// <summary>[EXT:online-coop-snapshot-join] How long new assignments are collected before their capture is taken: one capture serves them all.</summary>
	public const double CollectSeconds = 2;
	/// <summary>The least time between two captures for one member: a capture it asks for earlier is assigned at the mark.</summary>
	public const double CaptureBudgetSeconds = 10;
	/// <summary>How long a capture waits for a held coaster drag before it fails for now.</summary>
	public const double HeldDragSeconds = 30;
	/// <summary>The first wait a capture that failed for now asks of a member before it asks again: it doubles with the host's failed uploads in a row, and a server's Retry-After only raises it.</summary>
	public const uint RetrySeconds = 5;
	/// <summary>The least time between two repeats of an outstanding assignment to a member still on an older generation.</summary>
	public const double ReassignSeconds = 1;
	/// <summary>An upload that has not answered for this long is given up for now (its late answer is ignored), so no lost answer holds the captures back.</summary>
	public const double UploadSeconds = 180;
	/// <summary>The longest wait a failure for now asks of a member.</summary>
	public const uint MaximumRetrySeconds = 3600;
	/// <summary>[EXT:online-coop-commands] The most bytes one guest's command may take (a line or a placement takes a few dozen).</summary>
	public const int MaximumGuestCommandBytes = 1024;
	/// <summary>[EXT:online-coop-commands] The commands a second a guest's bucket refills with, and the most it holds: a burst of clicks, never a flood.</summary>
	public const double GuestCommandRate = 20;
	public const double GuestCommandBurst = 40;
	/// <summary>[EXT:online-coop-commands] The answers the host keeps for each guest, for the requests it sends again after a lost link.</summary>
	public const int GuestAnswersKept = 256;
	/// <summary>
	/// [EXT:online-coop-commands] The most commands of one guest that wait for the host's next frame: more are dropped unanswered (a flood, never a player), and the gap the next one shows
	/// has the guest send them again.
	/// </summary>
	public const int MaximumQueuedGuestCommands = 64;

	private sealed record PendingRecord( long Number, CoopStreamRecord Record );
	private readonly record struct HistoryFrame( ulong Seq, byte[] Payload );

	private enum GuestPhase
	{
		/// <summary>[EXT:online-coop-snapshot-join] Assigned a capture and not reported in its generation yet: not waited for, no timers.</summary>
		AwaitingSnapshot,
		Joining,
		Following,
		CatchingUp,
		Stalled,
		Desynced,
		TooFarBack,
		Disconnected
	}

	private sealed class Guest
	{
		public Guest( uint id, ulong needFrom, double now )
		{
			Id = id;
			NeedFrom = needFrom;
			Since = now;
		}

		public uint Id { get; }
		public GuestPhase Phase { get; set; } = GuestPhase.Joining;
		/// <summary>The first frame it may still need before it reports (the frames after the last one known relayed when it joined).</summary>
		public ulong NeedFrom { get; set; }
		/// <summary>When it joined, or when it last reported.</summary>
		public double Since { get; set; }
		public CoopProgress? Report { get; set; }
		public ulong Acknowledged { get; set; }
		/// <summary>When it began catching up while holding every frame; null while not so.</summary>
		public double? CatchingUpSince { get; set; }
		public ulong ResendFrom { get; set; }
		public ulong ResendCursor { get; set; }
		public ulong ResendEnd { get; set; }
		public double ResendSentAt { get; set; } = double.NegativeInfinity;
		/// <summary>The frame it was told the host no longer holds, and when (<see cref="GuestPhase.TooFarBack"/>).</summary>
		public ulong TooFarBackFrom { get; set; }
		public double TooFarBackSentAt { get; set; } = double.NegativeInfinity;
		public List<(ulong Seq, int Bytes)> InFlight { get; } = new();
		public HashSet<uint> Desyncs { get; } = new();
		public bool Resending => ResendCursor < ResendEnd;
		/// <summary>[EXT:online-coop-snapshot-join] Its last assigned capture (0 never: a session's identical start, or a member the host's resume reconciled in).</summary>
		public uint Generation { get; set; }
		/// <summary>The host's last frame when it was assigned (the assignment's repeats carry it: the cut is no earlier).</summary>
		public ulong AssignedFrame { get; set; }
		/// <summary>Its last assignment awaits its first report of that generation.</summary>
		public bool Outstanding { get; set; }
		/// <summary>The answer to its last assignment once decided: a <see cref="CoopSnapshotReady"/> or a <see cref="CoopSnapshotFailed"/>.</summary>
		public CoopStreamMessage? Answer { get; set; }
		/// <summary>The smallest lag it reported in its generation, while it catches up after a capture.</summary>
		public long? SmallestLag { get; set; }
		/// <summary>Its join window while it catches up after a capture: the smallest lag it reported plus the window; null once that lag is within the window.</summary>
		public long? JoinWindow { get; set; }
		/// <summary>When it was last assigned a capture (one every <see cref="CaptureBudgetSeconds"/>).</summary>
		public double LastAssigned { get; set; } = double.NegativeInfinity;
		/// <summary>A capture it asked for waits for its budget's mark.</summary>
		public bool CaptureDue { get; set; }
		public double ReassignedAt { get; set; } = double.NegativeInfinity;
		/// <summary>[EXT:online-coop-commands] The sequence number of its next command (from 1).</summary>
		public uint NextCommand { get; set; } = 1;
		/// <summary>[EXT:online-coop-commands] What became of its last requests, oldest first: the stream record that holds each, or why there is none.</summary>
		public Queue<(uint Sequence, long Record, CoopGuestRefusal? Refusal)> Answers { get; } = new();
		/// <summary>[EXT:online-coop-commands] Its rate bucket: the commands it may send now, and when that was counted.</summary>
		public double CommandTokens { get; set; } = GuestCommandBurst;
		public double CommandsCountedAt { get; set; } = double.NegativeInfinity;
		/// <summary>[EXT:online-coop-commands] Its commands waiting for the next frame.</summary>
		public int QueuedCommands { get; set; }
	}

	/// <summary>[EXT:online-coop-snapshot-join] A capture on its way: the cut, the session state and the save, serialized and compressed in slices, then uploaded.</summary>
	private sealed class JoinCapture( uint generation, List<Guest> members, long records, long ticks, ulong frame, ulong digest, ulong worldHash, CoopJoinState state,
		SlicedJsonSerializer save, CoopCaptureTimings timings )
	{
		/// <summary>What the capture costs (docs/COOP.md, "Measurements").</summary>
		public CoopCaptureTimings Timings { get; } = timings;
		/// <summary>When the upload started, on the wall clock (<see cref="System.Diagnostics.Stopwatch"/>).</summary>
		public long UploadStarted { get; set; }
		public uint Generation { get; } = generation;
		public List<Guest> Members { get; } = members;
		public long Records { get; } = records;
		public long Ticks { get; } = ticks;
		public ulong Frame { get; } = frame;
		public ulong Digest { get; } = digest;
		public ulong WorldHash { get; } = worldHash;
		public CoopJoinPayloadWriter? Writer { get; private set; }
		public byte[]? Payload { get; private set; }
		/// <summary>When the upload started; null before it.</summary>
		public double? UploadingSince { get; set; }

		/// <summary>
		/// One slice of the work: the save's serialization, then the payload's compression; true once the payload is complete. A save past <paramref name="maximumSave"/> bytes throws
		/// <see cref="CoopSnapshotTooLargeException"/> (the park is too large); anything else thrown is a park that cannot be captured.
		/// </summary>
		public bool Step( TimeSpan budget, int maximumSave )
		{
			if ( Payload != null )
				return true;
			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			if ( Writer == null )
			{
				var saved = save.Run( budget );
				// this frame of the capture: the cut's was the first
				Timings.Save.Add( CoopClock.Since( started ), Timings.Frames + 1 );
				if ( !saved )
					return false;
				var copying = System.Diagnostics.Stopwatch.GetTimestamp();
				var bytes = save.Bytes;
				Timings.SaveCopyMilliseconds = CoopClock.Since( copying );
				Timings.SaveBytes = bytes.Length;
				if ( bytes.Length > maximumSave )
					throw new CoopSnapshotTooLargeException( "World save exceeds the size limit." );
				var writing = System.Diagnostics.Stopwatch.GetTimestamp();
				Writer = new CoopJoinPayloadWriter( new CoopJoinPayload( Generation, Records, Ticks, Frame, Digest, WorldHash, state, bytes ) );
				Timings.WriterSetupMilliseconds = CoopClock.Since( writing );
				Timings.BodyBytes = Writer.BodyBytes;
				return false;
			}
			var complete = Writer.Run( budget );
			Timings.Compression.Add( CoopClock.Since( started ), Timings.Frames + 1 );
			if ( !complete )
				return false;
			Payload = Writer.Bytes;
			Timings.FinishMilliseconds = Writer.FinishMilliseconds;
			Timings.PayloadBytes = Payload.Length;
			return true;
		}
	}

	private readonly Level level;
	private readonly ICoopStreamTransport transport;
	private readonly Func<double> seconds;
	private readonly List<CoopCaptureTimings> captureTimings = new();
	private readonly Func<DateTime> localTime;
	private readonly List<PendingRecord> pending = new();
	/// <summary>
	/// [EXT:online-coop-commands] The guests' commands as they arrived, applied at the next frame's start, before its ticks (<see cref="ApplyGuestCommands"/>); a command too large to take
	/// keeps its number, not its bytes.
	/// </summary>
	private readonly List<(uint Member, CoopGuestCommand Request, bool TooLarge)> guestCommands = new();
	/// <summary>[EXT:online-coop-permissions] The seats of guests that left, whose build grants the next frame's start takes back.</summary>
	private readonly List<int> leftSeats = new();
	/// <summary>
	/// [EXT:online-coop-commands] Each guest seat's member at the last frame's start (C3b-2): a seat whose member left or changed since (a departure the live events report, one a resume's
	/// reconciliation drops, a seat taken again in between) has its grant taken back and what it held released at the next frame's start, before that frame's requests.
	/// </summary>
	private Dictionary<int, uint> seatMembers = new();
	/// <summary>
	/// [EXT:online-coop-claims] When each guest seat's member was first read disconnected (the host's clock), while it stays so (C3c-1): a seat away for
	/// <see cref="ClaimDisconnectedSeconds"/> loses its claim (<see cref="ExpireClaims"/>).
	/// </summary>
	private readonly Dictionary<int, double> disconnectedSince = new();
	/// <summary>
	/// [EXT:online-coop-claims] When each guest seat's claim was last renewed (the host's clock): its claim, or an accepted command of its holder naming its ride (C3c-1). A claim not renewed for
	/// <see cref="ClaimLeaseSeconds"/> goes (<see cref="ExpireClaims"/>).
	/// </summary>
	private readonly Dictionary<int, double> claimLeases = new();
	/// <summary>[EXT:online-coop-commands] The origin of the guest's command being applied now, which its record carries, and the record it got.</summary>
	private CoopRecordOrigin? applyingOrigin;
	private long applyingRecord;
	private readonly List<CoopCheckpoint> pendingTicks = new();
	private CoopCheckpoint? pendingRecordCheckpoint;
	private bool recordCheckpointTaken;
	private readonly List<HistoryFrame> history = new();
	private long historyBytes;
	private readonly Dictionary<uint, Guest> guests = new();
	private readonly bool previousSessionRules;
	private readonly ParkCommandInTickRule previousInTick;
	private readonly IParkCommandRights? previousRights;
	private readonly int previousPlayer;
	/// <summary>
	/// The session's own recording, from its start, so a desync can be replayed offline: under the session's rules, and with the world hash of each tick digest as its checkpoint
	/// (it takes none between the digests: a full park's world hash is most of a digest's cost).
	/// </summary>
	private readonly ParkCommandRecorder recorder;
	/// <summary>[EXT:online-coop-undo] The host's undo journal, which decides every take-back of the session (C5).</summary>
	private readonly CoopUndoJournal undo;
	/// <summary>[EXT:online-coop-undo] The cell map the journal hears the changes of, unhooked at the end.</summary>
	private readonly ParkCellMap? undoCells;
	private long nextStream = 1;
	private ulong nextSeq;
	private ulong sendNext;
	/// <summary>The last frame the server is known to hold (a resume or a refusal said so, or a guest acknowledged it).</summary>
	private ulong relayed;
	private long lastHorizon;
	private double lastPublish = double.NegativeInfinity;
	private double resendTokens = ResendBytesPerSecond;
	private double lastResendRefill;
	private long? lastClockSecond;
	/// <summary>The frame the last refusal rewound to, and how many refusals of the frames that were on their way after it (naming the same frame) are still to come.</summary>
	private ulong? rewoundTo;
	private long staleRefusals;
	/// <summary>
	/// [EXT:online-coop-snapshot-join] The frame the host's last skip goes on at (<see cref="SkipTrimmed"/>): a refusal naming an earlier frame was on its way before the server took the skip,
	/// and a resume whose server has not taken it hears it again.
	/// </summary>
	private ulong? skippedTo;
	private bool disposed;
	/// <summary>[EXT:online-coop-snapshot-join] The number the next capture takes, which the members assigned to it get.</summary>
	private uint nextGeneration = 1;
	private readonly List<Guest> assigned = new();
	private double? collectSince;
	private double? heldSince;
	/// <summary>
	/// [EXT:online-coop-claims] The coaster claims the capture step found members waiting for it hold with a coaster edit on them: each a seat and the coaster's station (C3c-2). The next frame's
	/// start lets go of them (<see cref="ReleaseCaptureHoldings"/>).
	/// </summary>
	private readonly List<(int Seat, int Station)> capturesHolding = new();
	private JoinCapture? capture;
	/// <summary>The uploads that failed for now in a row: each waits twice as long as the last, so a spent server limit is not asked for every few seconds.</summary>
	private int uploadFailures;

	/// <param name="seconds">A monotonic clock in seconds (wall time; the tests' virtual one).</param>
	/// <param name="localTime">The host's civil clock the session clock samples.</param>
	public CoopHostStream( Level level, ICoopStreamTransport transport, Func<double>? seconds = null, Func<DateTime>? localTime = null )
	{
		this.level = level ?? throw new ArgumentNullException( nameof( level ) );
		this.transport = transport ?? throw new ArgumentNullException( nameof( transport ) );
		this.seconds = seconds ?? DefaultSeconds;
		this.localTime = localTime ?? (() => DateTime.Now);
		var session = transport.Session ?? throw new InvalidOperationException( "The host's stream needs a session." );
		nextSeq = sendNext = session.LastFrameSeq + 1;
		relayed = session.LastFrameSeq;
		lastHorizon = level.TickCount;
		var now = this.seconds();
		lastResendRefill = now;
		foreach ( var member in session.Members )
			if ( member.Id != session.You )
				guests[member.Id] = new Guest( member.Id, relayed + 1, now ) { Phase = member.Connected ? GuestPhase.Joining : GuestPhase.Disconnected };
		var commands = level.Commands;
		previousSessionRules = commands.SessionRules;
		previousInTick = commands.InTick;
		previousRights = commands.Rights;
		previousPlayer = commands.Player;
		commands.SessionRules = true;
		commands.InTick = ParkCommandInTickRule.Drop;
		// [EXT:online-coop-permissions] the session's rights, whatever rights the park had: every peer asks the same ones
		commands.Rights = new CoopBuildRights( level );
		commands.Player = ParkCommands.LocalPlayer;
		// a grant is its session's: a park shared again starts without the last session's (a guest of the identical start has none either), and so is what a guest's seat carried (C3b-2)
		level.ClearSeatRights();
		// [EXT:online-coop-claims] and the rides' claims (C3c-1)
		level.ClearRideClaims();
		level.ClearGuestCarries();
		// [EXT:online-coop-permissions] and what a guest's seat held of the staff (C4c)
		level.ClearGuestStaff();
		seatMembers = SeatMembers();
		recorder = new ParkCommandRecorder( commands, level.LevelName ?? "", level.Seed.Value, level.TickCount, level.ComputeStateHash() ) { SessionRules = true };
		// [EXT:online-coop-undo] the journal decides every take-back before its rights and its log, and hears every command's cell changes (C5)
		undo = new CoopUndoJournal( level, session.You );
		commands.Prepare = command => undo.Decide( command, applyingOrigin?.Member );
		commands.Starting = undo.Begin;
		undoCells = level.Guests?.Grid.Cells;
		if ( undoCells != null )
			undoCells.Changed = undo.Changed;
		level.UndoJournal = undo;
		commands.Applied += OnApplied;
		level.TickEnded += OnTickEnded;
		transport.Resumed += OnResumed;
		transport.FrameRefused += OnFrameRefused;
		transport.MemberChanged += OnMemberChanged;
		transport.MessageReceived += OnMessage;
		level.SessionStream = this;
		// the session's clock is set before its first tick, so no script of the session reads a machine's own clock
		IssueClock();
	}

	internal static double DefaultSeconds() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

	/// <summary>Why the stream ended the session (a record that does not encode, a frame the server needs that the history no longer has on a server that cannot take a skip); null while it runs.</summary>
	public string? Failure { get; private set; }

	/// <summary>Raised once when the stream ends the session, with the reason.</summary>
	public event Action<string>? Failed;

	/// <summary>Raised for every desync a guest reports (once per desync), with the guest's member id and its report.</summary>
	public event Action<uint, CoopDesyncReport>? Desynced;

	/// <summary>
	/// The session's recorded stream (design section 6: a desync replays offline with the command tools): taken at the first desync a guest reports, and in full when the stream
	/// ends; null before either.
	/// </summary>
	public ParkCommandLogFile? SessionLog { get; private set; }

	/// <summary>The frames published so far (the next frame's number less one).</summary>
	public ulong LastFrame => nextSeq - 1;

	/// <summary>The oldest frame the history still holds (the next frame's number when it holds none).</summary>
	public ulong OldestFrame => history.Count > 0 ? history[0].Seq : nextSeq;

	/// <summary>The stream numbers given so far (the next record's number less one).</summary>
	public long LastRecord => nextStream - 1;

	/// <summary>The ticks the host may run ahead of a following guest at the park's current speed.</summary>
	public long WindowTicks => (long)(WindowSeconds * FixedStepClock.TicksPerSecond * Math.Max( 1f, GameSpeedScale.Of( level.Park?.Economy.Speed ?? GameSpeed.Normal ) ));

	/// <summary>The guests the host's park waits for now (following, within the window).</summary>
	internal IEnumerable<uint> WaitedFor => guests.Values.Where( guest => guest.Phase == GuestPhase.Following ).Select( guest => guest.Id );

	/// <summary>Whether the host has a desync of <paramref name="guest"/> on record.</summary>
	internal bool IsDesynced( uint guest ) => guests.TryGetValue( guest, out var found ) && found.Phase == GuestPhase.Desynced;

	/// <summary>Whether the host told <paramref name="guest"/> it is too far back (a resync, C2c).</summary>
	internal bool IsTooFarBack( uint guest ) => guests.TryGetValue( guest, out var found ) && found.Phase == GuestPhase.TooFarBack;

	/// <summary>[EXT:online-coop-snapshot-join] The capture <paramref name="guest"/> was last assigned (0 never).</summary>
	internal uint GenerationOf( uint guest ) => guests.TryGetValue( guest, out var found ) ? found.Generation : 0;

	/// <summary>Whether <paramref name="guest"/> awaits a capture: assigned and not reported in its generation yet.</summary>
	internal bool IsAwaitingSnapshot( uint guest ) => guests.TryGetValue( guest, out var found ) && found.Phase == GuestPhase.AwaitingSnapshot;

	/// <summary>The join window <paramref name="guest"/> catches up within, or null (none, or its lag is within the ordinary window).</summary>
	internal long? JoinWindowOf( uint guest ) => guests.TryGetValue( guest, out var found ) ? found.JoinWindow : null;

	/// <summary>The captures taken so far.</summary>
	public int CapturesTaken { get; private set; }

	/// <summary>[EXT:online-coop-snapshot-join] What the captures that reached their upload's answer cost, oldest first (at most <see cref="MaximumCaptureTimings"/>; a measurement only).</summary>
	public IReadOnlyList<CoopCaptureTimings> CaptureTimings => captureTimings;

	/// <summary>The most capture timings kept.</summary>
	public const int MaximumCaptureTimings = 16;

	/// <summary>Raised when a capture is uploaded, with its generation and its payload (the tests read the cut).</summary>
	internal event Action<uint, byte[]>? CaptureStored;

	/// <summary>The time a frame spends on a capture's serialization and compression (the periodic autosave's slice).</summary>
	internal TimeSpan CaptureSliceBudget { get; set; } = TimeSpan.FromMilliseconds( 2 );

	/// <summary>The largest save a capture takes (the save's own cap; the tests lower it).</summary>
	internal int MaximumSaveBytes { get; set; } = ParkResumeSaveFile.MaximumFileSize;

	/// <summary>Test seam: runs as a capture is taken, at the cut; a throw is a park that cannot be captured.</summary>
	internal Action<Level>? CaptureFault { get; set; }

	/// <summary>Test seam: runs before each slice of a capture's serialization and compression; a throw is a park that cannot be captured.</summary>
	internal Action? SliceFault { get; set; }

	public void AdvanceSimulation( double frameSeconds, float timeScale )
	{
		if ( Failure != null )
		{
			level.AdvanceSimulation( frameSeconds * timeScale );
			return;
		}
		var now = seconds();
		UpdateGuests( now );
		// [EXT:online-coop-commands] the guests' commands at the frame's start, after the host's own input and before the ticks, paused too: one order, the host's
		ApplyGuestCommands( now );
		// the scripts read the clock only in ticks: a paused park sends no clock command, and the first frame that runs again sets the clock before its ticks
		if ( timeScale > 0 )
			IssueClock();
		level.TickLimit = Limit();
		level.AdvanceSimulation( frameSeconds * timeScale );
		if ( Failure != null )
			return;
		Publish( now );
		Flush();
		Resend( now );
		Trim();
		Captures( now );
	}

	/// <summary>Publishes what waits now, whatever the time since the last frame (a test, a session that ends).</summary>
	internal void PublishNow()
	{
		lastPublish = double.NegativeInfinity;
		Publish( seconds() );
		Flush();
	}

	/// <summary>The tick the host's park holds at: its own while its link is down, else a window ahead of its slowest following guest; null when none holds it.</summary>
	private long? Limit()
	{
		if ( !transport.IsOpen )
			return level.TickCount;
		long? limit = null;
		var window = WindowTicks;
		foreach ( var guest in guests.Values )
			if ( guest.Phase == GuestPhase.Following && guest.Report is { } report )
				limit = Math.Min( limit ?? long.MaxValue, report.Ticks + (guest.JoinWindow ?? window) );
		return limit;
	}

	/// <summary>The session clock: at the start and whenever the host clock's second changes, a clock command as player 0 (a Theme Park Inc park reads no civil time).</summary>
	private void IssueClock()
	{
		if ( Failure != null || level.Park?.Economy.Rules.ThemeParkInc == true || GameEditions.Current != GameEdition.ThemeParkWorld )
			return;
		var command = SetSessionClockCommand.For( localTime() );
		if ( command.Seconds == lastClockSecond )
			return;
		lastClockSecond = command.Seconds;
		level.Commands.Apply( command );
	}

	private void OnApplied( ParkCommandLogEntry entry, ParkCommandResult result )
	{
		if ( Failure != null )
			return;
		byte[] bytes;
		try
		{
			bytes = ParkCommandCodec.Encode( entry.Command );
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Fail( $"The host's {entry.Command.Kind} command has no stream form: {error.Message}" );
			return;
		}
		var flags = result.Error != null ? CoopRecordFlags.Errored : result.Accepted ? CoopRecordFlags.Accepted : CoopRecordFlags.None;
		var number = nextStream++;
		// [EXT:online-coop-undo] the journal takes the record: a step of its member's, a taken take-back's end, every change of a cell it tracks (C5)
		undo.Applied( number, applyingOrigin?.Member, entry.Command, result );
		// [EXT:online-coop-claims] a guest's claim, and every accepted command of its holder naming its ride, renews the claim's lease
		if ( result.Accepted && entry.Command.Player > 0 && RideNamedBy( entry.Command ) is { } claimed && level.ClaimOf( claimed ) == entry.Command.Player )
			claimLeases[claimed] = guestCommandsSeconds;
		// [EXT:online-coop-commands] a guest's command carries its origin, so its issuer knows the record that answers its request
		var origin = applyingOrigin;
		if ( origin != null )
			applyingRecord = number;
		pending.Add( new PendingRecord( number, new CoopStreamRecord( entry.Tick, flags, bytes, origin ) ) );
		// an exception may leave different partial writes on two peers: the digest right after the first errored record of a frame period checks them
		if ( flags == CoopRecordFlags.Errored && !recordCheckpointTaken )
		{
			recordCheckpointTaken = true;
			pendingRecordCheckpoint = new CoopCheckpoint( CoopCheckpointKind.AfterRecord, number, CoopDigest.Compute( level ) );
		}
	}

	private void OnTickEnded( long ticks )
	{
		if ( Failure == null && ticks % CheckpointInterval( level.Park?.Economy.Speed ?? GameSpeed.Normal ) == 0 )
		{
			var digest = CoopDigest.Compute( level, out var world );
			recorder.AddCheckpoint( ticks, world );
			pendingTicks.Add( new CoopCheckpoint( CoopCheckpointKind.AfterTick, ticks, digest ) );
		}
	}

	/// <summary>Every 100 ms when something changed (a record, a higher horizon, a checkpoint): the waiting records in frames of at most the frame size, the checkpoints with them.</summary>
	private void Publish( double now )
	{
		var ticks = level.TickCount;
		if ( now - lastPublish < PublishSeconds || (pending.Count == 0 && pendingTicks.Count == 0 && pendingRecordCheckpoint == null && ticks <= lastHorizon) )
			return;
		lastPublish = now;
		recordCheckpointTaken = false;
		var index = 0;
		var checkpoints = new List<CoopCheckpoint>( pendingTicks );
		do
		{
			var first = index < pending.Count ? pending[index].Number : nextStream;
			var records = new List<CoopStreamRecord>();
			var size = CoopStreamFrame.EmptyBytes + checkpoints.Count * CoopStreamFrame.CheckpointBytes;
			while ( index < pending.Count && records.Count < ushort.MaxValue )
			{
				var next = pending[index];
				var add = CoopStreamFrame.RecordBytes( next.Record )
					+ (pendingRecordCheckpoint is { } checkpoint && checkpoint.At == next.Number ? CoopStreamFrame.CheckpointBytes : 0);
				if ( size + add > CoopStreamFrame.MaximumBytes && records.Count > 0 )
					break;
				records.Add( next.Record );
				size += add;
				index++;
				if ( pendingRecordCheckpoint is { } taken && taken.At == next.Number )
					checkpoints.Add( taken );
			}
			// a frame cut at the size limit stops its horizon at the first record it does not hold, so no guest runs past a record it has not got
			var horizon = index < pending.Count ? pending[index].Record.Boundary : ticks;
			var frame = new CoopStreamFrame( horizon, first, records, checkpoints ).Encode();
			history.Add( new HistoryFrame( nextSeq++, frame ) );
			historyBytes += frame.Length;
			lastHorizon = horizon;
			checkpoints = new List<CoopCheckpoint>();
		}
		while ( index < pending.Count );
		pending.Clear();
		pendingTicks.Clear();
		pendingRecordCheckpoint = null;
	}

	/// <summary>Hands the server every frame from the next one it takes, while the link is open and takes them.</summary>
	private void Flush()
	{
		while ( Failure == null && sendNext < nextSeq && transport.IsOpen )
		{
			if ( Frame( sendNext ) is not { } frame )
			{
				Fail( $"The host no longer holds frame {sendNext}, which the server has not taken." );
				return;
			}
			if ( !transport.SendFrame( frame.Seq, frame.Payload ) )
				return;
			sendNext++;
		}
	}

	private HistoryFrame? Frame( ulong seq )
	{
		var oldest = OldestFrame;
		return seq >= oldest && seq < nextSeq ? history[(int)(seq - oldest)] : null;
	}

	private void OnResumed( CoopSessionState session )
	{
		if ( Failure != null )
			return;
		rewoundTo = null;
		staleRefusals = 0;
		var last = session.LastFrameSeq;
		if ( session.SeqKnown )
		{
			if ( last >= nextSeq )
			{
				Fail( $"The server holds frame {last}, which the host never sent." );
				return;
			}
			relayed = Math.Max( relayed, last );
			// [EXT:online-coop-snapshot-join] a skip lost with the socket goes out again, without assigning the members anew, while the host still holds the frames from it;
			// a server back behind a skip whose frames are trimmed since needs a new one
			if ( skippedTo is { } skip && last + 1 < skip && skip >= OldestFrame )
			{
				transport.SkipFrames( skip );
				sendNext = skip;
			}
			else if ( last + 1 < OldestFrame )
			{
				// the members as the server has them, so one that joined while the host was away is assigned too
				Reconcile( session );
				SkipTrimmed( last + 1 );
				return;
			}
			else
				sendNext = last + 1;
		}
		else
		{
			// after a server crash its number may be behind frames the guests hold and the host trimmed: it takes any later frame, so the host resends from its oldest
			sendNext = Math.Min( nextSeq, Math.Max( last + 1, OldestFrame ) );
		}
		Reconcile( session );
		Flush();
		// [EXT:online-coop-snapshot-join] what was queued on the old socket died with it
		foreach ( var guest in guests.Values.Where( guest => guest.Outstanding ) )
			Repeat( guest );
	}

	/// <summary>
	/// A resume's state is a full refresh, without the member changes of the time the host was away: a guest that joined meanwhile is taken in, one that left goes, and a guest's
	/// connection is as the state says.
	/// </summary>
	private void Reconcile( CoopSessionState session )
	{
		var now = seconds();
		var members = session.Members.Where( member => member.Id != session.You ).ToDictionary( member => member.Id );
		foreach ( var id in guests.Keys.Where( id => !members.ContainsKey( id ) ).ToList() )
			guests.Remove( id );
		foreach ( var member in members.Values )
		{
			if ( !guests.TryGetValue( member.Id, out var guest ) )
				guests[member.Id] = new Guest( member.Id, relayed + 1, now ) { Phase = member.Connected ? GuestPhase.Joining : GuestPhase.Disconnected };
			else if ( guest.Phase is GuestPhase.Desynced or GuestPhase.TooFarBack or GuestPhase.AwaitingSnapshot )
				continue;
			else if ( !member.Connected )
				guest.Phase = GuestPhase.Disconnected;
			else if ( guest.Phase == GuestPhase.Disconnected )
			{
				guest.Phase = GuestPhase.Joining;
				guest.Since = now;
			}
		}
	}

	/// <summary>
	/// The server took a frame out of turn: it holds every frame before <see cref="CoopFrameRefused.Expected"/>, exactly (the host's frames after a crash's resume start above the
	/// server's number, which it takes), and the host resends from there. Every frame that was on its way after the gap is refused with the same number, in order: those refusals
	/// change nothing, and one more naming it means the resent frame was refused too, so the host rewinds again.
	/// </summary>
	private void OnFrameRefused( CoopFrameRefused refused )
	{
		if ( Failure != null )
			return;
		var expected = refused.Expected;
		if ( expected == rewoundTo && staleRefusals > 0 )
		{
			staleRefusals--;
			return;
		}
		// [EXT:online-coop-snapshot-join] a refusal of a frame sent before the server took the host's skip names a frame the skip passed over
		if ( expected < skippedTo )
			return;
		if ( expected > nextSeq )
		{
			Fail( $"The server expects frame {expected}, after the host's last." );
			return;
		}
		relayed = Math.Max( relayed, expected - 1 );
		if ( expected < OldestFrame )
		{
			SkipTrimmed( expected );
			return;
		}
		// the frames after the first refused one, up to the last one handed over, were on their way too: each is refused with the same number
		staleRefusals = Math.Max( 0, (long)(sendNext - expected) - 2 );
		rewoundTo = expected;
		sendNext = expected;
		Flush();
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The server needs frame <paramref name="needed"/>, which the host no longer holds (its stored position fell behind the host's trimmed history; docs/COOP.md,
	/// "The host's trimmed frame"): every member is assigned a capture at once, past the host's last frame, and the server is told the frames go on after it, so no member needs the frames
	/// between. A transport whose server cannot take the skip ends the session, as before.
	/// </summary>
	private void SkipTrimmed( ulong needed )
	{
		if ( !transport.CanSkipFrames )
		{
			Fail( $"The server needs frame {needed}, which the host no longer holds." );
			return;
		}
		var skip = nextSeq;
		Log.Warning( $"The server needs frame {needed}, which the host no longer holds: every member gets the shared park again, and its frames go on at {skip}." );
		var now = seconds();
		foreach ( var guest in guests.Values )
			Assign( guest, now, force: true );
		// nobody needs the frames before the skip: every member's capture starts after them
		relayed = Math.Max( relayed, skip - 1 );
		skippedTo = skip;
		rewoundTo = null;
		staleRefusals = 0;
		sendNext = skip;
		// a skip that cannot go out now goes out with the next resume
		if ( transport.SkipFrames( skip ) )
			Flush();
	}

	private void OnMemberChanged( CoopMemberChanged change )
	{
		var id = change.Member.Id;
		if ( transport.Session is { } session && id == session.You )
			return;
		var now = seconds();
		switch ( change.State )
		{
			case CoopMemberState.Joined:
				var joined = guests[id] = new Guest( id, relayed + 1, now );
				// [EXT:online-coop-snapshot-join] a member that joins a running session needs the park: its capture is assigned at once
				Assign( joined, now );
				break;
			case CoopMemberState.Connected:
				if ( guests.TryGetValue( id, out var back ) )
				{
					if ( back.Phase is GuestPhase.Disconnected or GuestPhase.Stalled or GuestPhase.Following or GuestPhase.CatchingUp )
						back.Phase = GuestPhase.Joining;
					back.Since = now;
					// an assignment or answer on its way when it went away was lost
					if ( back.Outstanding )
						Repeat( back );
				}
				else
					guests[id] = new Guest( id, relayed + 1, now );
				break;
			case CoopMemberState.Disconnected:
				// not waited for while away; its frames from its last acknowledgement stay while the history's caps allow, so a quick resume needs no resync
				if ( guests.TryGetValue( id, out var away ) && away.Phase is not (GuestPhase.Desynced or GuestPhase.TooFarBack or GuestPhase.AwaitingSnapshot) )
					away.Phase = GuestPhase.Disconnected;
				break;
			case CoopMemberState.Left:
			case CoopMemberState.Kicked:
				guests.Remove( id );
				// [EXT:online-coop-permissions] a seat's grant is its member's: the next member in that seat starts without one
				if ( change.Member.Slot > 0 )
					leftSeats.Add( change.Member.Slot );
				break;
		}
	}

	private void OnMessage( CoopRelayed relayedMessage )
	{
		if ( Failure != null || !guests.TryGetValue( relayedMessage.From, out var guest ) )
			return;
		// [EXT:online-coop-commands] a guest's command too large to take is refused by its number, without copying it
		if ( CoopGuestCommand.IsOversized( relayedMessage.Payload.Span, MaximumGuestCommandBytes, out var oversized ) )
		{
			Queue( guest, new CoopGuestCommand( oversized, ReadOnlyMemory<byte>.Empty ), tooLarge: true );
			return;
		}
		CoopStreamMessage message;
		try
		{
			message = CoopStreamMessage.Decode( relayedMessage.Payload.Span );
		}
		catch ( InvalidDataException error )
		{
			Log.Warning( $"A shared park guest ({guest.Id}) sent a message the host cannot read: {error.Message}" );
			return;
		}
		var now = seconds();
		// [EXT:online-coop-snapshot-join] a stream message of another generation than the member's belongs to a stream it no longer runs
		if ( message is CoopProgress or CoopResendRequest or CoopDesyncReport && message.Generation != guest.Generation )
		{
			if ( guest.Outstanding && message.Generation < guest.Generation && now - guest.ReassignedAt >= ReassignSeconds )
			{
				guest.ReassignedAt = now;
				Repeat( guest );
			}
			return;
		}
		switch ( message )
		{
			case CoopProgress progress:
				Report( guest, progress, now );
				break;
			case CoopResendRequest request:
				Request( guest, request, now );
				break;
			case CoopDesyncReport report:
				Note( guest, report );
				Desync( guest );
				Send( guest, new CoopDesyncSeen( report.Id ) );
				break;
			case CoopSnapshotRequest request:
				if ( request.Report is { } reported )
					Note( guest, reported );
				Requested( guest, request, now );
				break;
			case CoopGuestCommand command:
				Queue( guest, command, tooLarge: false );
				break;
		}
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's command waits for the next frame's start; past <see cref="MaximumQueuedGuestCommands"/> of that guest's it is dropped unanswered (a flood),
	/// and the next request shows the gap.
	/// </summary>
	private void Queue( Guest guest, CoopGuestCommand command, bool tooLarge )
	{
		if ( guest.QueuedCommands >= MaximumQueuedGuestCommands )
			return;
		guest.QueuedCommands++;
		guestCommands.Add( (guest.Id, command, tooLarge) );
	}

	/// <summary>
	/// [EXT:online-coop-commands] The grants of the guests that left are taken back, then the guests' commands that arrived since the last frame apply in their order (docs/COOP.md,
	/// "Building by every player"). Each applies between two ticks, as the host's own do, so a paused park takes them too; a grant is its member's, so a member that takes a freed seat
	/// starts without one, even when its first command comes in the frame its seat was freed.
	/// </summary>
	private void ApplyGuestCommands( double now )
	{
		// [EXT:online-coop-claims] the frame's clock, which a guest's command renews its claim's lease with and the expiry reads (C3c-1)
		guestCommandsSeconds = now;
		// [EXT:online-coop-commands] the seats whose member left or changed since the last frame, and any guest seat that still holds something without a member: a carry (C3b-2),
		// [EXT:online-coop-claims] a claim (C3c-1), [EXT:online-coop-permissions] a right (C4a); a link without its session's state (between a lost socket and its resume) shows no seat, so
		// the seats wait for its next state instead of all counting as left
		var known = transport.Session != null;
		var members = known ? SeatMembers() : seatMembers;
		var departed = seatMembers.Where( pair => !members.TryGetValue( pair.Key, out var member ) || member != pair.Value ).ToArray();
		var changed = departed.Select( pair => pair.Key )
			.Concat( level.HoldingGuestSeats().Where( seat => known && !members.ContainsKey( seat ) ) ).ToArray();
		seatMembers = members;
		// [EXT:online-coop-undo] a member that left its seat (or whose seat another took) takes back nothing any more: its steps leave the journal as they are (C5)
		foreach ( var (_, member) in departed )
			undo.Depart( member );
		if ( leftSeats.Count > 0 || changed.Length > 0 )
		{
			var seats = leftSeats.Concat( changed ).Distinct().OrderBy( seat => seat ).ToArray();
			leftSeats.Clear();
			foreach ( var seat in seats )
			{
				// a new member of the seat starts with its own link; [EXT:online-coop-permissions] one record frees all the seat held (C4a)
				disconnectedSince.Remove( seat );
				if ( Failure == null && level.SeatHoldsAnything( seat ) )
					level.Commands.Apply( new ReleaseSeatCommand( seat ) );
			}
		}
		// [EXT:online-coop-claims] each guest seat's link, read at every frame's start (a seed, a resume's reconciliation and an event alike)
		ReadLinks( now );
		if ( guestCommands.Count > 0 )
		{
			var arrived = guestCommands.ToArray();
			guestCommands.Clear();
			foreach ( var (member, request, tooLarge) in arrived )
			{
				if ( !guests.TryGetValue( member, out var guest ) )
					continue;
				guest.QueuedCommands--;
				if ( Failure == null )
					Command( guest, request, tooLarge, now );
			}
		}
		// [EXT:online-coop-claims] after the guests' commands, so a claim a late frame's queued command took is checked in that same frame (C3c-1); the claims a capture waits on go first (C3c-2)
		ReleaseCaptureHoldings();
		ExpireClaims( now );
	}

	/// <summary>
	/// [EXT:online-coop-claims] The host lets go of the coaster claims the capture step noted (<see cref="capturesHolding"/>), each with a <see cref="ReleaseRideCommand"/> of its own, a record
	/// every peer applies alike, whose claim's end releases the seat's edit (a moved pylon goes back) and ends the coaster's tool state (C3c-2): a member waiting for a capture has fenced its
	/// requests, so its drag's end never comes. Each note is read after this frame's guest commands applied and goes only while its seat still holds the coaster's claim and an edit on it; a
	/// note whose seat ended its drag or let its claim go meanwhile is dropped. Every note goes now: the capture step notes again.
	/// </summary>
	private void ReleaseCaptureHoldings()
	{
		if ( capturesHolding.Count == 0 )
			return;
		var notes = capturesHolding.ToArray();
		capturesHolding.Clear();
		foreach ( var (seat, station) in notes )
		{
			if ( Failure == null && level.ClaimOf( station ) == seat && level.CoasterEdits.Of( seat ) is { } held && held.Station == station )
				level.Commands.Apply( new ReleaseRideCommand( station ) );
		}
	}

	/// <summary>[EXT:online-coop-claims] The clock of the frame whose guest commands apply (<see cref="ApplyGuestCommands"/>): the time a guest's command renews its claim's lease at.</summary>
	private double guestCommandsSeconds;

	/// <summary>[EXT:online-coop-claims] How long a guest seat's member may be read disconnected before the host lets go of its claim (C3c-1).</summary>
	public const double ClaimDisconnectedSeconds = 30;

	/// <summary>[EXT:online-coop-claims] How long a guest seat's claim lasts without its holder's accepted command naming its ride (C3c-1).</summary>
	public const double ClaimLeaseSeconds = 5 * 60;

	/// <summary>
	/// [EXT:online-coop-claims] Stamps each guest seat whose member the session reads disconnected now with the first frame that reads it so, and drops the stamp of one read connected or gone
	/// (C3c-1); a link without its session's state changes no stamp.
	/// </summary>
	private void ReadLinks( double now )
	{
		// a link without its session's state reads no member: the stamps wait for its next state
		if ( transport.Session is not { } session )
			return;
		var away = session.Members.Where( member => member.Slot > 0 && member.Id != session.You && !member.Connected ).Select( member => (int)member.Slot ).ToHashSet();
		foreach ( var seat in disconnectedSince.Keys.Where( seat => !away.Contains( seat ) ).ToArray() )
			disconnectedSince.Remove( seat );
		foreach ( var seat in away )
			disconnectedSince.TryAdd( seat, now );
	}

	/// <summary>
	/// [EXT:online-coop-claims] The host lets go of a guest seat's claim (a <see cref="ReleaseRideCommand"/>, a record every peer applies alike) once its member has been read disconnected for
	/// <see cref="ClaimDisconnectedSeconds"/>, or once its lease ran out: no claim and no accepted command of its holder naming the ride for <see cref="ClaimLeaseSeconds"/> (C3c-1). The host's own
	/// claim has neither: its tool is on this machine, whose frame check lets it go (<see cref="Level.SettleClaim"/>).
	/// </summary>
	private void ExpireClaims( double now )
	{
		foreach ( var (ride, seat) in level.RideClaims.ToArray() )
		{
			if ( Failure != null || seat <= 0 )
				continue;
			// a claim the stream saw no renewal of (one the park had before) starts its lease now
			if ( !claimLeases.TryGetValue( ride, out var renewed ) )
				claimLeases[ride] = renewed = now;
			var away = disconnectedSince.TryGetValue( seat, out var since ) && now - since >= ClaimDisconnectedSeconds;
			if ( away || now - renewed >= ClaimLeaseSeconds )
				level.Commands.Apply( new ReleaseRideCommand( ride ) );
		}
		if ( claimLeases.Count > 0 )
		{
			foreach ( var ride in claimLeases.Keys.Where( ride => level.ClaimOf( ride ) is not > 0 ).ToArray() )
				claimLeases.Remove( ride );
		}
	}

	/// <summary>
	/// [EXT:online-coop-claims] The ride <paramref name="command"/> names as its holder's edit (its claim, a track command, and the coaster tool's commands, C3c-2: a drag in progress keeps its claim),
	/// or null.
	/// </summary>
	private static int? RideNamedBy( ParkCommand command ) => command switch
	{
		ClaimRideCommand claim => claim.Ride,
		OpenTrackLineToolCommand open => open.Ride,
		LayTrackLineCommand line => line.Ride,
		UndoTrackLineCommand undo => undo.Ride,
		CutTrackCommand cut => cut.Ride,
		BeginCoasterToolCommand begin => begin.Station,
		EndCoasterToolCommand end => end.Station,
		CoasterPanelCommand panel => panel.Station,
		_ => null
	};

	/// <summary>[EXT:online-coop-commands] Each guest seat's member as the session holds it now (C3b-2).</summary>
	private Dictionary<int, uint> SeatMembers() => transport.Session is { } session
		? session.Members.Where( member => member.Slot > 0 && member.Id != session.You ).GroupBy( member => (int)member.Slot ).ToDictionary( group => group.Key, group => group.First().Id )
		: new Dictionary<int, uint>();

	/// <summary>
	/// [EXT:online-coop-commands] One guest's request: a request answered already gets its answer again, never the command twice; one that skips a number is refused until the guest sends from
	/// the number it is told; the expected one is decided and takes its number. A command too large, too fast, from no guest's seat, or that does not decode is refused without a record; one
	/// that decodes is applied as the guest's seat (the server's word of the sender, never the command's own player) and becomes a record of the stream, accepted or refused by the rights and
	/// the park's rules alike on every peer.
	/// </summary>
	private void Command( Guest guest, CoopGuestCommand request, bool tooLarge, double now )
	{
		if ( request.Sequence < guest.NextCommand )
		{
			var known = guest.Answers.FirstOrDefault( answer => answer.Sequence == request.Sequence );
			Send( guest, known.Sequence == request.Sequence
				? new CoopGuestCommandAnswer( request.Sequence, known.Record, known.Refusal, guest.NextCommand )
				: new CoopGuestCommandAnswer( request.Sequence, 0, CoopGuestRefusal.Forgotten, guest.NextCommand ) );
			return;
		}
		if ( request.Sequence > guest.NextCommand )
		{
			Send( guest, new CoopGuestCommandAnswer( request.Sequence, 0, CoopGuestRefusal.Gap, guest.NextCommand ) );
			return;
		}
		guest.NextCommand++;
		// the rate bucket refills between requests, up to its burst
		guest.CommandTokens = Math.Min( GuestCommandBurst, guest.CommandTokens + Math.Max( 0, now - guest.CommandsCountedAt ) * GuestCommandRate );
		guest.CommandsCountedAt = now;
		var refusal = Refuse( guest, request, tooLarge, out var command );
		long record = 0;
		if ( refusal == null )
		{
			applyingOrigin = new CoopRecordOrigin( guest.Id, request.Sequence );
			applyingRecord = 0;
			try
			{
				level.Commands.ApplyRemote( command! );
			}
			finally
			{
				applyingOrigin = null;
			}
			record = applyingRecord;
			// every well-formed command applied between two ticks is recorded; a host whose stream took none cannot answer it
			if ( record == 0 )
				refusal = CoopGuestRefusal.Unavailable;
		}
		guest.Answers.Enqueue( (request.Sequence, record, refusal) );
		while ( guest.Answers.Count > GuestAnswersKept )
			guest.Answers.Dequeue();
		// the stream answers a recorded command by itself; an unrecorded one gets its refusal now
		if ( refusal != null )
			Send( guest, new CoopGuestCommandAnswer( request.Sequence, 0, refusal, guest.NextCommand ) );
	}

	/// <summary>[EXT:online-coop-commands] Why the host takes no record of <paramref name="request"/>, or null with its command, its player the guest's seat.</summary>
	private CoopGuestRefusal? Refuse( Guest guest, CoopGuestCommand request, bool tooLarge, out ParkCommand? command )
	{
		command = null;
		if ( tooLarge )
			return CoopGuestRefusal.TooLarge;
		if ( guest.CommandTokens < 1 )
			return CoopGuestRefusal.TooFast;
		guest.CommandTokens--;
		if ( transport.Session?.Members.FirstOrDefault( member => member.Id == guest.Id ) is not { Slot: > 0 } member )
			return CoopGuestRefusal.NotSeated;
		try
		{
			command = ParkCommandCodec.Decode( request.Command.Span ) with { Player = member.Slot };
		}
		// a guest's bytes never stop the host: whatever they throw makes them malformed
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			return CoopGuestRefusal.Malformed;
		}
		return command.IsWellFormed ? null : CoopGuestRefusal.Malformed;
	}

	/// <summary>A guest's desync, logged once, with the session's log taken at the first.</summary>
	private void Note( Guest guest, CoopDesyncReport report )
	{
		if ( !guest.Desyncs.Add( report.Id ) )
			return;
		Log.Warning( $"Shared park guest {guest.Id} desynced ({report.Kind}) at tick {report.Tick}: host digest {report.HostDigest:X16}, guest {report.GuestDigest:X16}, "
			+ $"stream records {report.FirstStream} to {report.LastStream}; the host recorded {recorder.Entries.Count} commands of the session." );
		SessionLog ??= RecordedLog();
		Desynced?.Invoke( guest.Id, report );
	}

	/// <summary>Sends <paramref name="message"/> to <paramref name="guest"/> in the guest's generation.</summary>
	private bool Send( Guest guest, CoopStreamMessage message ) => transport.Send( guest.Id, (message with { Generation = guest.Generation }).Encode() );

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A member asks for a join snapshot (docs/COOP.md, "Joining a running session"): an answer it missed is sent again, and only a failure (or a member never
	/// assigned) makes a new capture; a member ahead of the host's numbers is out of step and leaves.
	/// </summary>
	private void Requested( Guest guest, CoopSnapshotRequest request, double now )
	{
		if ( request.Known > guest.Generation )
		{
			transport.Send( guest.Id, new CoopSnapshotFailed( CoopSnapshotFailure.Uncapturable, true, 0 ) { Generation = request.Known }.Encode() );
			return;
		}
		var failure = request.Reason is not (CoopSnapshotReason.Join or CoopSnapshotReason.NoAnswer);
		if ( request.Known < guest.Generation || (guest.Generation != 0 && !failure) )
			Repeat( guest );
		else
			Assign( guest, now );
	}

	/// <summary>
	/// Assigns <paramref name="guest"/> the next capture and tells it at once, so its old stream stops; the capture waits for the others assigned within <see cref="CollectSeconds"/>. A member
	/// assigned within <see cref="CaptureBudgetSeconds"/> of its last assignment is assigned at the mark, unless <paramref name="force"/> (the frames it would follow are gone).
	/// </summary>
	private void Assign( Guest guest, double now, bool force = false )
	{
		if ( !force && now - guest.LastAssigned < CaptureBudgetSeconds )
		{
			guest.CaptureDue = true;
			return;
		}
		guest.CaptureDue = false;
		guest.LastAssigned = now;
		guest.Generation = nextGeneration;
		guest.AssignedFrame = LastFrame;
		guest.Phase = GuestPhase.AwaitingSnapshot;
		guest.Outstanding = true;
		guest.Answer = null;
		guest.Report = null;
		guest.Acknowledged = 0;
		guest.NeedFrom = nextSeq;
		guest.CatchingUpSince = null;
		guest.ResendCursor = guest.ResendEnd = 0;
		guest.InFlight.Clear();
		guest.TooFarBackFrom = 0;
		guest.SmallestLag = guest.JoinWindow = null;
		if ( !assigned.Contains( guest ) )
			assigned.Add( guest );
		collectSince ??= now;
		Send( guest, new CoopSnapshotAssigned( guest.AssignedFrame ) );
	}

	/// <summary>The member's outstanding assignment again, with its answer once decided (a member that has them ignores them).</summary>
	private void Repeat( Guest guest )
	{
		if ( guest.Generation == 0 )
			return;
		Send( guest, new CoopSnapshotAssigned( guest.AssignedFrame ) );
		if ( guest.Answer is { } answer )
			Send( guest, answer );
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The captures, at the end of the frame (the park between ticks, every record before the cut published): the one on its way takes its slice of serialization
	/// and compression, then uploads; else, once the assignments have been collected and no coaster drag is held, the next is taken.
	/// </summary>
	private void Captures( double now )
	{
		if ( capture is { } running )
		{
			if ( running.UploadingSince is { } since )
			{
				// an answer that never comes holds no capture back: the upload is given up for now
				if ( now - since >= UploadSeconds )
					Uploaded( running, CoopSnapshotTransfer.Failed( "The upload did not answer." ) );
				return;
			}
			bool complete;
			var sliceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
			try
			{
				SliceFault?.Invoke();
				complete = running.Step( CaptureSliceBudget, MaximumSaveBytes );
			}
			// the save's own checks run in its first slice (an inconsistent park is no too-large one)
			catch ( Exception error ) when ( error is not OutOfMemoryException )
			{
				capture = null;
				var tooLarge = error is CoopSnapshotTooLargeException;
				Log.Warning( $"The shared park {(tooLarge ? "is too large for" : "could not be written as")} a join snapshot: {error.Message}" );
				Answer( running.Members, running.Generation, new CoopSnapshotFailed( tooLarge ? CoopSnapshotFailure.TooLarge : CoopSnapshotFailure.Uncapturable, true, 0 ) );
				return;
			}
			running.Timings.Frames++;
			running.Timings.LongestFrameMilliseconds = Math.Max( running.Timings.LongestFrameMilliseconds, CoopClock.Since( sliceStarted ) );
			if ( complete )
			{
				running.UploadingSince = now;
				running.UploadStarted = System.Diagnostics.Stopwatch.GetTimestamp();
				transport.UploadSnapshot( running.Payload!, result => Uploaded( running, result ) );
			}
			return;
		}
		if ( assigned.Count == 0 || now - collectSince < CollectSeconds )
			return;
		// [EXT:online-coop-claims] a member waiting for this capture that holds a coaster edit on the coaster whose claim it holds has fenced its requests, so its drag's end never comes: the next
		// frame's start lets go of that claim, whose end releases the edit (ReleaseCaptureHoldings), and the capture waits for that (C3c-2)
		capturesHolding.Clear();
		foreach ( var guest in assigned )
		{
			if ( guests.TryGetValue( guest.Id, out var current ) && current == guest && SeatOf( guest ) is { } seat && level.CoasterEdits.Of( seat ) is { } held
				&& level.ClaimOf( held.Station ) == seat )
				capturesHolding.Add( (seat, held.Station) );
		}
		if ( capturesHolding.Count > 0 )
			return;
		// a held coaster drag's edited pylon is no part of the save: the capture waits for its release, for a while (another seat's drag, whose end is not fenced)
		if ( level.CoasterDragHeld )
		{
			heldSince ??= now;
			if ( now - heldSince >= HeldDragSeconds )
			{
				heldSince = null;
				var held = nextGeneration++;
				Answer( Take( held ), held, new CoopSnapshotFailed( CoopSnapshotFailure.Held, false, RetrySeconds ) );
			}
			return;
		}
		heldSince = null;
		var generation = nextGeneration++;
		var members = Take( generation );
		if ( members.Count == 0 )
			return;
		// the cut: every record so far applied and published, the next tick and the last frame, whose horizon is that tick
		var cutStarted = System.Diagnostics.Stopwatch.GetTimestamp();
		var timings = new CoopCaptureTimings { Generation = generation, Ticks = level.TickCount };
		var part = cutStarted;
		double Part()
		{
			var now = System.Diagnostics.Stopwatch.GetTimestamp();
			var milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime( part, now ).TotalMilliseconds;
			part = now;
			return milliseconds;
		}
		PublishNow();
		timings.PublishMilliseconds = Part();
		var records = LastRecord;
		var ticks = level.TickCount;
		var frame = LastFrame;
		try
		{
			CaptureFault?.Invoke( level );
			// a test's hook at the cut (the native smoke's own digest of the cut) is no part of the capture's cost
			Part();
			var digest = CoopDigest.Compute( level, out var world );
			timings.DigestMilliseconds = Part();
			var state = level.CaptureJoinState();
			_ = state.Encode();
			timings.StateMilliseconds = Part();
			var snapshot = level.CaptureJoinSnapshot();
			timings.SnapshotMilliseconds = Part();
			var save = ParkResumeSaveFile.BeginSlicedSerialize( snapshot );
			timings.SerializerSetupMilliseconds = Part();
			timings.Frames = 1;
			timings.LongestFrameMilliseconds = timings.CutMilliseconds;
			capture = new JoinCapture( generation, members, records, ticks, frame, digest, world, state, save, timings );
		}
		// a park the save path refuses (catalogue defaults it cannot write, an object without its binding, a key past a bound): nothing is captured, and the members hear why
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Log.Warning( $"The shared park could not be captured for a join: {error.Message}" );
			Answer( members, generation, new CoopSnapshotFailed( CoopSnapshotFailure.Uncapturable, true, 0 ) );
			return;
		}
		CapturesTaken++;
		foreach ( var guest in members )
			guest.NeedFrom = frame + 1;
	}

	/// <summary>[EXT:online-coop-claims] The guest seat of <paramref name="guest"/>'s member as the session holds it now, or null (C3c-2).</summary>
	private int? SeatOf( Guest guest ) => transport.Session?.Members.FirstOrDefault( member => member.Id == guest.Id ) is { Slot: > 0 } member ? member.Slot : null;

	/// <summary>The members still assigned capture <paramref name="generation"/>, taken off the list of those waiting for one.</summary>
	private List<Guest> Take( uint generation )
	{
		var members = assigned.Where( guest => guest.Generation == generation && guests.TryGetValue( guest.Id, out var current ) && current == guest ).ToList();
		assigned.Clear();
		collectSince = null;
		return members;
	}

	private void Uploaded( JoinCapture running, CoopSnapshotTransfer result )
	{
		if ( disposed || Failure != null || capture != running )
			return;
		capture = null;
		running.Timings.UploadSeconds = CoopClock.Since( running.UploadStarted ) / 1000;
		captureTimings.Add( running.Timings );
		if ( captureTimings.Count > MaximumCaptureTimings )
			captureTimings.RemoveAt( 0 );
		Log.Trace( $"Co-op {running.Timings}{(result.Succeeded ? "" : $" (not stored: {result.Failure})")}." );
		if ( result.Succeeded )
		{
			uploadFailures = 0;
			CaptureStored?.Invoke( running.Generation, running.Payload! );
			Answer( running.Members, running.Generation, new CoopSnapshotReady( running.Records, running.Ticks, running.Frame, running.Digest, running.WorldHash, result.Sha256!,
				running.Payload!.Length ) );
			return;
		}
		Log.Warning( $"The shared park's join snapshot could not be uploaded: {result.Failure}" );
		// a server's Retry-After may name a past date: the wait is at least a second and at most an hour, and grows with the failures in a row
		uploadFailures++;
		var backOff = RetrySeconds * Math.Pow( 2, Math.Min( uploadFailures - 1, 10 ) );
		var retry = Math.Clamp( Math.Max( result.RetryAfter?.TotalSeconds ?? 0, backOff ), 1, MaximumRetrySeconds );
		Answer( running.Members, running.Generation, new CoopSnapshotFailed( result.TooLarge ? CoopSnapshotFailure.TooLarge : result.Permanent ? CoopSnapshotFailure.Uncapturable
			: CoopSnapshotFailure.Busy, result.Permanent, (uint)Math.Ceiling( retry ) ) );
	}

	/// <summary>Answers the members still assigned capture <paramref name="generation"/>; the answer is kept for the repeats.</summary>
	private void Answer( IEnumerable<Guest> members, uint generation, CoopStreamMessage answer )
	{
		foreach ( var guest in members )
		{
			if ( !guests.TryGetValue( guest.Id, out var current ) || current != guest || guest.Generation != generation )
				continue;
			guest.Answer = answer with { Generation = generation };
			Send( guest, guest.Answer );
		}
	}

	private void Report( Guest guest, CoopProgress progress, double now )
	{
		guest.Report = progress;
		guest.Since = now;
		if ( progress.LastFrame < nextSeq && progress.LastFrame > guest.Acknowledged )
		{
			guest.Acknowledged = progress.LastFrame;
			// a guest holds what the server relayed: every frame up to its acknowledgement is the server's
			relayed = Math.Max( relayed, progress.LastFrame );
			guest.InFlight.RemoveAll( sent => sent.Seq <= progress.LastFrame );
		}
		// [EXT:online-coop-snapshot-join] its first report of its generation ends its assignment
		var first = guest.Outstanding;
		guest.Outstanding = false;
		switch ( progress.State )
		{
			case CoopGuestState.Desynced:
				Desync( guest );
				return;
			case CoopGuestState.TooFarBack:
				guest.Phase = GuestPhase.TooFarBack;
				return;
		}
		// a guest told it is too far back that reports otherwise has not heard it
		if ( guest.Phase == GuestPhase.TooFarBack )
		{
			RepeatTooFarBack( guest, now );
			return;
		}
		if ( guest.Phase == GuestPhase.Desynced )
			return;
		var behind = level.TickCount - progress.Ticks;
		// [EXT:online-coop-snapshot-join] a joiner catches up within a join window of its own: its smallest lag plus the ordinary window, so its lag never grows by more than that and the park
		// runs on while it catches up; once that lag is within the ordinary window it is an ordinary following guest. Back from a stall or a reconnect, its report decides by its join window
		// as an ordinary guest's does by the ordinary one, so a joiner that fell further behind meanwhile stops no park
		if ( first || (guest.JoinWindow != null && guest.Phase == GuestPhase.Following) )
		{
			guest.SmallestLag = Math.Min( guest.SmallestLag ?? long.MaxValue, Math.Max( 0, behind ) );
			guest.JoinWindow = guest.SmallestLag <= WindowTicks ? null : guest.SmallestLag + WindowTicks;
			guest.Phase = GuestPhase.Following;
			guest.CatchingUpSince = null;
			return;
		}
		if ( guest.Phase != GuestPhase.Following )
			guest.Phase = behind <= (guest.JoinWindow ?? WindowTicks) ? GuestPhase.Following : GuestPhase.CatchingUp;
		// the minute before a resync counts from the moment a catching-up guest holds every frame it asked for
		if ( guest.Phase == GuestPhase.CatchingUp && progress.State != CoopGuestState.Gap )
			guest.CatchingUpSince ??= now;
		else
			guest.CatchingUpSince = null;
	}

	/// <summary>Stalled and long catching-up guests: a guest without a report for ten seconds is not waited for; one that holds every frame and stays behind for a minute is told to resync.</summary>
	private void UpdateGuests( double now )
	{
		foreach ( var guest in guests.Values.Where( guest => guest.CaptureDue && now - guest.LastAssigned >= CaptureBudgetSeconds ).ToList() )
			Assign( guest, now );
		foreach ( var guest in guests.Values )
		{
			if ( guest.Phase is GuestPhase.Joining or GuestPhase.Following or GuestPhase.CatchingUp && now - guest.Since >= StallSeconds )
			{
				guest.Phase = GuestPhase.Stalled;
				guest.CatchingUpSince = null;
			}
			if ( guest.Phase == GuestPhase.CatchingUp && guest.CatchingUpSince is { } since && now - since >= CatchUpSeconds )
				TooFarBack( guest, guest.Acknowledged + 1 );
		}
	}

	private void TooFarBack( Guest guest, ulong from )
	{
		guest.Phase = GuestPhase.TooFarBack;
		guest.CatchingUpSince = null;
		guest.ResendCursor = guest.ResendEnd = 0;
		guest.InFlight.Clear();
		guest.TooFarBackFrom = from;
		guest.TooFarBackSentAt = seconds();
		Send( guest, new CoopTooFarBack( from ) );
	}

	/// <summary>A guest stopped at a desync: the host neither waits for it nor resends to it (a copy on its way stops) until a resync.</summary>
	private static void Desync( Guest guest )
	{
		guest.Phase = GuestPhase.Desynced;
		guest.CatchingUpSince = null;
		guest.ResendCursor = guest.ResendEnd = 0;
		guest.InFlight.Clear();
	}

	/// <summary>Tells a guest again that it is too far back, at most every <see cref="TooFarBackRepeatSeconds"/>: the first message may have been lost.</summary>
	private void RepeatTooFarBack( Guest guest, double now )
	{
		if ( now - guest.TooFarBackSentAt < TooFarBackRepeatSeconds )
			return;
		guest.TooFarBackSentAt = now;
		Send( guest, new CoopTooFarBack( guest.TooFarBackFrom ) );
	}

	private void Request( Guest guest, CoopResendRequest request, double now )
	{
		if ( guest.Phase == GuestPhase.TooFarBack )
		{
			RepeatTooFarBack( guest, now );
			return;
		}
		if ( guest.Phase == GuestPhase.Desynced || request.From == 0 )
			return;
		if ( request.From < OldestFrame )
		{
			TooFarBack( guest, request.From );
			return;
		}
		var end = request.To == 0 ? nextSeq : Math.Min( request.To, nextSeq );
		if ( request.From >= end )
			return;
		// a repeated request (with each progress while the gap lasts, and after the guest's resume) never starts a second copy while the last one may still be on its way:
		// it only extends the copy
		if ( request.From >= guest.ResendFrom && request.From <= guest.ResendCursor && now - guest.ResendSentAt < ResendRetrySeconds )
		{
			guest.ResendEnd = Math.Max( guest.ResendEnd, end );
			return;
		}
		// [EXT:online-coop-snapshot-join] a request that restarts within a copy still on its way keeps the copy's end: a guest that started at a cut, or resumed, asks for every frame
		// from its first gap, and its narrower request for that gap or a later one must not clip the frames after them
		if ( guest.Resending && request.From <= guest.ResendEnd )
			end = Math.Max( end, guest.ResendEnd );
		guest.ResendFrom = guest.ResendCursor = request.From;
		guest.ResendEnd = end;
		guest.InFlight.Clear();
	}

	/// <summary>Sends the guests' resends from their cursors: at most a megabyte unacknowledged per guest, and a megabyte a second for all of them.</summary>
	private void Resend( double now )
	{
		resendTokens = Math.Min( ResendBytesPerSecond, resendTokens + (now - lastResendRefill) * ResendBytesPerSecond );
		lastResendRefill = now;
		foreach ( var guest in guests.Values.OrderBy( guest => guest.Id ) )
		{
			if ( guest.Phase is GuestPhase.Desynced or GuestPhase.TooFarBack )
				continue;
			while ( guest.Resending && transport.IsOpen )
			{
				if ( guest.ResendCursor < OldestFrame )
				{
					TooFarBack( guest, guest.ResendCursor );
					break;
				}
				var batch = new List<(ulong, ReadOnlyMemory<byte>)>();
				var size = CoopResentFrames.EmptyBytes;
				var inFlight = guest.InFlight.Sum( sent => sent.Bytes );
				var cursor = guest.ResendCursor;
				while ( cursor < guest.ResendEnd && Frame( cursor ) is { } frame )
				{
					var add = CoopResentFrames.FrameOverhead + frame.Payload.Length;
					if ( size + add > CoopLink.MaximumMessagePayload || inFlight + size + add > MaximumResendInFlight || size + add > resendTokens )
						break;
					batch.Add( (frame.Seq, frame.Payload) );
					size += add;
					cursor++;
				}
				if ( batch.Count == 0 || !Send( guest, new CoopResentFrames( batch ) ) )
					break;
				resendTokens -= size;
				guest.ResendSentAt = now;
				foreach ( var (seq, payload) in batch )
					guest.InFlight.Add( (seq, payload.Length) );
				guest.ResendCursor = cursor;
			}
		}
	}

	/// <summary>
	/// Drops the frames no guest needs any more and the server is known to hold, oldest first. Past the caps (10,000 frames, 32 MiB) the oldest frames handed to the link go even if
	/// a guest still needs them or the server never confirmed them (the server confirms nothing until a resume, a refusal or a guest's acknowledgement, so a host alone would never
	/// trim): a guest that asks for one is told to resync, and a server that names one after a restart is told the frames go on past the host's last one, every member assigned a capture
	/// (<see cref="SkipTrimmed"/>). A frame not yet handed to the link never goes.
	/// </summary>
	private void Trim()
	{
		// a frame not yet handed to the server stays (after a server crash the host resends frames the guests already acknowledged)
		var keepFrom = Math.Min( relayed + 1, sendNext );
		foreach ( var guest in guests.Values )
		{
			if ( guest.Phase is GuestPhase.Desynced or GuestPhase.TooFarBack )
				continue;
			// [EXT:online-coop-snapshot-join] a member whose capture failed for now needs a new one, which pins its own cut: it holds no frames back while it waits
			if ( guest.Phase == GuestPhase.AwaitingSnapshot && guest.Answer is CoopSnapshotFailed )
				continue;
			var needs = guest.Report == null ? guest.NeedFrom : guest.Acknowledged + 1;
			if ( guest.Resending )
				needs = Math.Min( needs, guest.ResendCursor );
			keepFrom = Math.Min( keepFrom, needs );
		}
		var drop = 0;
		var droppedBytes = 0L;
		while ( drop < history.Count && (history[drop].Seq < keepFrom
			|| (history.Count - drop > MaximumHistoryFrames || historyBytes - droppedBytes > MaximumHistoryBytes) && history[drop].Seq < sendNext) )
		{
			droppedBytes += history[drop].Payload.Length;
			drop++;
		}
		if ( drop > 0 )
		{
			history.RemoveRange( 0, drop );
			historyBytes -= droppedBytes;
		}
		if ( history.Count > MaximumHistoryFrames || historyBytes > MaximumHistoryBytes )
			Fail( "The host's frames the link has not taken outgrew the history." );
	}

	/// <summary>Ends the session for everyone with <paramref name="reason"/>: the stream cannot go on without skipping or losing a record.</summary>
	private void Fail( string reason )
	{
		if ( Failure != null )
			return;
		Failure = reason;
		Log.Error( $"The shared park's stream ended the session: {reason}" );
		level.TickLimit = null;
		transport.Leave( reason );
		Failed?.Invoke( reason );
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		level.Commands.Applied -= OnApplied;
		level.TickEnded -= OnTickEnded;
		transport.Resumed -= OnResumed;
		transport.FrameRefused -= OnFrameRefused;
		transport.MemberChanged -= OnMemberChanged;
		transport.MessageReceived -= OnMessage;
		level.TickLimit = null;
		if ( level.SessionStream == this )
			level.SessionStream = null;
		// [EXT:online-coop-permissions] what every guest's seat still holds goes in records of the session's log, under the session's rules, before its last checkpoint (C4a): the seat's
		// rights, its carry and its claims, whose end leaves a coaster's tool state (C3c-2), so the log replays to the state this park runs on with. They go to no guest (the stream
		// stopped taking records above): each guest's park runs on alone, and its own teardown clears what its session held. An end inside a tick (no game path makes one; a test's
		// TickCompleted could) records nothing there, as no command may apply inside a tick: this park then ends the guests' coasters' tool state on its own after the log, as a
		// guest's teardown does
		if ( !level.Commands.IsInTick )
			foreach ( var seat in level.HoldingGuestSeats() )
				level.Commands.Apply( new ReleaseSeatCommand( seat ) );
		var unreleased = level.RideClaims.Where( claim => claim.Value > 0 ).Select( claim => claim.Key ).ToArray();
		var commands = level.Commands;
		commands.SessionRules = previousSessionRules;
		commands.InTick = previousInTick;
		commands.Rights = previousRights;
		commands.Player = previousPlayer;
		// [EXT:online-coop-undo] the journal is its session's (C5)
		commands.Prepare = null;
		commands.Starting = null;
		if ( undoCells != null )
			undoCells.Changed = null;
		if ( level.UndoJournal == undo )
			level.UndoJournal = null;
		undo.Clear();
		// [EXT:online-coop-permissions] rights are their session's: the park runs on alone without any (the guests' were released above), and without what a guest's seat carried (C3b-2)
		level.ClearSeatRights();
		// [EXT:online-coop-claims] and the rides' claims (C3c-1): the host's own, as the guests' went above
		level.ClearRideClaims();
		level.ClearGuestCarries();
		// the park's scripts read the machine's own clock again
		if ( level.Objects?.ScriptWorld is { } scripts )
			scripts.SessionTime = null;
		recorder.Dispose();
		SessionLog = RecordedLog();
		level.EndSessionCoasters( unreleased );
		// [EXT:online-coop-permissions] staff state a guest's seat still holds (an end inside a tick recorded no release) goes after the log's last checkpoint, never before it, so the log replays
		// to that checkpoint (C4c): its employees in hand drop back where they were picked up
		level.ClearGuestStaff();
	}

	/// <summary>The session's recording so far, with the world hash now as its last checkpoint.</summary>
	private ParkCommandLogFile RecordedLog() => recorder.ToLog( (level.TickCount, level.ComputeStateHash()) );
}
