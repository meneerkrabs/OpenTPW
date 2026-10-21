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
	/// <summary>How long a member waits before it asks again after a capture failed for now, unless the server said.</summary>
	public const uint RetrySeconds = 5;
	/// <summary>The least time between two repeats of an outstanding assignment to a member still on an older generation.</summary>
	public const double ReassignSeconds = 1;

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
	}

	/// <summary>[EXT:online-coop-snapshot-join] A capture on its way: the cut, the session state and the save, serialized and compressed in slices, then uploaded.</summary>
	private sealed class JoinCapture( uint generation, List<Guest> members, long records, long ticks, ulong frame, ulong digest, ulong worldHash, CoopJoinState state,
		SlicedJsonSerializer save )
	{
		public uint Generation { get; } = generation;
		public List<Guest> Members { get; } = members;
		public long Records { get; } = records;
		public long Ticks { get; } = ticks;
		public ulong Frame { get; } = frame;
		public ulong Digest { get; } = digest;
		public ulong WorldHash { get; } = worldHash;
		public CoopJoinPayloadWriter? Writer { get; private set; }
		public byte[]? Payload { get; private set; }
		public bool Uploading { get; set; }

		/// <summary>One slice of the work: the save's serialization, then the payload's compression; true once the payload is complete. A park past the save's cap throws.</summary>
		public bool Step( TimeSpan budget )
		{
			if ( Payload != null )
				return true;
			if ( Writer == null )
			{
				if ( !save.Run( budget ) )
					return false;
				Writer = new CoopJoinPayloadWriter( new CoopJoinPayload( Generation, Records, Ticks, Frame, Digest, WorldHash, state, ParkResumeSaveFile.CheckSize( save.Bytes ) ) );
				return false;
			}
			if ( !Writer.Run( budget ) )
				return false;
			Payload = Writer.Bytes;
			return true;
		}
	}

	private readonly Level level;
	private readonly ICoopStreamTransport transport;
	private readonly Func<double> seconds;
	private readonly Func<DateTime> localTime;
	private readonly List<PendingRecord> pending = new();
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
	private bool disposed;
	/// <summary>[EXT:online-coop-snapshot-join] The number the next capture takes, which the members assigned to it get.</summary>
	private uint nextGeneration = 1;
	private readonly List<Guest> assigned = new();
	private double? collectSince;
	private double? heldSince;
	private JoinCapture? capture;

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
		commands.Rights ??= CoopHostOnlyRights.Instance;
		commands.Player = ParkCommands.LocalPlayer;
		recorder = new ParkCommandRecorder( commands, level.LevelName ?? "", level.Seed.Value, level.TickCount, level.ComputeStateHash() ) { SessionRules = true };
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

	/// <summary>Why the stream ended the session (a record that does not encode, a frame the server needs that the history no longer has); null while it runs.</summary>
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

	/// <summary>Raised when a capture is uploaded, with its generation and its payload (the tests read the cut).</summary>
	internal event Action<uint, byte[]>? CaptureStored;

	/// <summary>The time a frame spends on a capture's serialization and compression (the periodic autosave's slice).</summary>
	internal TimeSpan CaptureSliceBudget { get; set; } = TimeSpan.FromMilliseconds( 2 );

	public void AdvanceSimulation( double frameSeconds, float timeScale )
	{
		if ( Failure != null )
		{
			level.AdvanceSimulation( frameSeconds * timeScale );
			return;
		}
		var now = seconds();
		UpdateGuests( now );
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
		pending.Add( new PendingRecord( number, new CoopStreamRecord( entry.Tick, flags, bytes ) ) );
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
				var add = CoopStreamFrame.RecordOverhead + next.Record.Bytes.Length
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
			if ( last + 1 < OldestFrame )
			{
				Fail( $"The server needs frame {last + 1}, which the host no longer holds." );
				return;
			}
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
		if ( expected > nextSeq )
		{
			Fail( $"The server expects frame {expected}, after the host's last." );
			return;
		}
		relayed = Math.Max( relayed, expected - 1 );
		if ( expected < OldestFrame )
		{
			Fail( $"The server needs frame {expected}, which the host no longer holds." );
			return;
		}
		// the frames after the first refused one, up to the last one handed over, were on their way too: each is refused with the same number
		staleRefusals = Math.Max( 0, (long)(sendNext - expected) - 2 );
		rewoundTo = expected;
		sendNext = expected;
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
				break;
		}
	}

	private void OnMessage( CoopRelayed relayedMessage )
	{
		if ( Failure != null || !guests.TryGetValue( relayedMessage.From, out var guest ) )
			return;
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
		}
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
	/// assigned within <see cref="CaptureBudgetSeconds"/> of its last assignment is assigned at the mark.
	/// </summary>
	private void Assign( Guest guest, double now )
	{
		if ( now - guest.LastAssigned < CaptureBudgetSeconds )
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
			if ( running.Uploading )
				return;
			bool complete;
			try
			{
				complete = running.Step( CaptureSliceBudget );
			}
			catch ( Exception error ) when ( error is InvalidDataException or InvalidOperationException )
			{
				capture = null;
				Log.Warning( $"The shared park is too large for a join snapshot: {error.Message}" );
				Answer( running.Members, running.Generation, new CoopSnapshotFailed( CoopSnapshotFailure.TooLarge, true, 0 ) );
				return;
			}
			if ( complete )
			{
				running.Uploading = true;
				transport.UploadSnapshot( running.Payload!, result => Uploaded( running, result ) );
			}
			return;
		}
		if ( assigned.Count == 0 || now - collectSince < CollectSeconds )
			return;
		// a held coaster drag's edited pylon is no part of the save: the capture waits for its release, for a while
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
		PublishNow();
		var records = LastRecord;
		var ticks = level.TickCount;
		var frame = LastFrame;
		try
		{
			var digest = CoopDigest.Compute( level, out var world );
			var state = level.CaptureJoinState();
			var save = ParkResumeSaveFile.BeginSlicedSerialize( level.CaptureJoinSnapshot() );
			capture = new JoinCapture( generation, members, records, ticks, frame, digest, world, state, save );
		}
		catch ( Exception error ) when ( error is InvalidOperationException or InvalidDataException )
		{
			Log.Warning( $"The shared park could not be captured for a join: {error.Message}" );
			Answer( members, generation, new CoopSnapshotFailed( CoopSnapshotFailure.Uncapturable, true, 0 ) );
			return;
		}
		CapturesTaken++;
		foreach ( var guest in members )
			guest.NeedFrom = frame + 1;
	}

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
		if ( result.Succeeded )
		{
			CaptureStored?.Invoke( running.Generation, running.Payload! );
			Answer( running.Members, running.Generation, new CoopSnapshotReady( running.Records, running.Ticks, running.Frame, running.Digest, running.WorldHash, result.Sha256!,
				running.Payload!.Length ) );
			return;
		}
		Log.Warning( $"The shared park's join snapshot could not be uploaded: {result.Failure}" );
		Answer( running.Members, running.Generation, new CoopSnapshotFailed( result.TooLarge ? CoopSnapshotFailure.TooLarge : result.Permanent ? CoopSnapshotFailure.Uncapturable
			: CoopSnapshotFailure.Busy, result.Permanent, (uint)Math.Ceiling( (result.RetryAfter ?? TimeSpan.FromSeconds( RetrySeconds )).TotalSeconds ) ) );
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
		// runs on while it catches up; once that lag is within the ordinary window it is an ordinary following guest
		if ( first || guest.JoinWindow != null )
		{
			guest.SmallestLag = Math.Min( guest.SmallestLag ?? long.MaxValue, Math.Max( 0, behind ) );
			guest.JoinWindow = guest.SmallestLag <= WindowTicks ? null : guest.SmallestLag + WindowTicks;
			guest.Phase = GuestPhase.Following;
			guest.CatchingUpSince = null;
			return;
		}
		if ( guest.Phase != GuestPhase.Following )
			guest.Phase = behind <= WindowTicks ? GuestPhase.Following : GuestPhase.CatchingUp;
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
	/// trim): a guest that asks for one is told to resync, and a server that names one after a restart ends the session. A frame not yet handed to the link never goes.
	/// </summary>
	private void Trim()
	{
		// a frame not yet handed to the server stays (after a server crash the host resends frames the guests already acknowledged)
		var keepFrom = Math.Min( relayed + 1, sendNext );
		foreach ( var guest in guests.Values )
		{
			if ( guest.Phase is GuestPhase.Desynced or GuestPhase.TooFarBack )
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
		var commands = level.Commands;
		commands.SessionRules = previousSessionRules;
		commands.InTick = previousInTick;
		commands.Rights = previousRights;
		commands.Player = previousPlayer;
		// the park's scripts read the machine's own clock again
		if ( level.Objects?.ScriptWorld is { } scripts )
			scripts.SessionTime = null;
		recorder.Dispose();
		SessionLog = RecordedLog();
	}

	/// <summary>The session's recording so far, with the world hash now as its last checkpoint.</summary>
	private ParkCommandLogFile RecordedLog() => recorder.ToLog( (level.TickCount, level.ComputeStateHash()) );
}
