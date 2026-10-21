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
	/// <summary>The ticks between two tick checkpoints: one park second.</summary>
	public const int CheckpointTicks = FixedStepClock.TicksPerSecond;
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

	private sealed record PendingRecord( long Number, CoopStreamRecord Record );
	private readonly record struct HistoryFrame( ulong Seq, byte[] Payload );

	private enum GuestPhase
	{
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
		public List<(ulong Seq, int Bytes)> InFlight { get; } = new();
		public HashSet<uint> Desyncs { get; } = new();
		public bool Resending => ResendCursor < ResendEnd;
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
	private bool disposed;

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
		commands.SessionRules = true;
		commands.InTick = ParkCommandInTickRule.Drop;
		commands.Rights ??= CoopHostOnlyRights.Instance;
		commands.Player = ParkCommands.LocalPlayer;
		level.StartCommandRecording();
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

	public void AdvanceSimulation( double frameSeconds, float timeScale )
	{
		if ( Failure != null )
		{
			level.AdvanceSimulation( frameSeconds * timeScale );
			return;
		}
		var now = seconds();
		UpdateGuests( now );
		IssueClock();
		level.TickLimit = Limit();
		level.AdvanceSimulation( frameSeconds * timeScale );
		if ( Failure != null )
			return;
		Publish( now );
		Flush();
		Resend( now );
		Trim();
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
				limit = Math.Min( limit ?? long.MaxValue, report.Ticks + window );
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
		if ( Failure == null && ticks % CheckpointTicks == 0 )
			pendingTicks.Add( new CoopCheckpoint( CoopCheckpointKind.AfterTick, ticks, CoopDigest.Compute( level ) ) );
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
		Flush();
	}

	private void OnFrameRefused( CoopFrameRefused refused )
	{
		if ( Failure != null )
			return;
		var expected = refused.Expected;
		var known = transport.Session?.SeqKnown ?? true;
		if ( expected > nextSeq )
		{
			Fail( $"The server expects frame {expected}, after the host's last." );
			return;
		}
		if ( known )
			relayed = Math.Max( relayed, expected - 1 );
		if ( expected < OldestFrame )
		{
			if ( known )
			{
				Fail( $"The server needs frame {expected}, which the host no longer holds." );
				return;
			}
			expected = OldestFrame;
		}
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
				guests[id] = new Guest( id, relayed + 1, now );
				break;
			case CoopMemberState.Connected:
				if ( guests.TryGetValue( id, out var back ) )
				{
					if ( back.Phase is GuestPhase.Disconnected or GuestPhase.Stalled or GuestPhase.Following or GuestPhase.CatchingUp )
						back.Phase = GuestPhase.Joining;
					back.Since = now;
				}
				else
					guests[id] = new Guest( id, relayed + 1, now );
				break;
			case CoopMemberState.Disconnected:
				// not waited for while away; its frames from its last acknowledgement stay while the history's caps allow, so a quick resume needs no resync
				if ( guests.TryGetValue( id, out var away ) && away.Phase is not (GuestPhase.Desynced or GuestPhase.TooFarBack) )
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
		switch ( message )
		{
			case CoopProgress progress:
				Report( guest, progress, now );
				break;
			case CoopResendRequest request:
				Request( guest, request, now );
				break;
			case CoopDesyncReport report:
				if ( guest.Desyncs.Add( report.Id ) )
				{
					Log.Warning( $"Shared park guest {guest.Id} desynced ({report.Kind}) at tick {report.Tick}: host digest {report.HostDigest:X16}, guest {report.GuestDigest:X16}, "
						+ $"stream records {report.FirstStream} to {report.LastStream}; the host recorded {level.CommandRecorder?.Entries.Count ?? 0} commands of the session." );
					Desynced?.Invoke( guest.Id, report );
				}
				guest.Phase = GuestPhase.Desynced;
				transport.Send( guest.Id, new CoopDesyncSeen( report.Id ).Encode() );
				break;
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
		switch ( progress.State )
		{
			case CoopGuestState.Desynced:
				guest.Phase = GuestPhase.Desynced;
				return;
			case CoopGuestState.TooFarBack:
				guest.Phase = GuestPhase.TooFarBack;
				return;
		}
		if ( guest.Phase is GuestPhase.Desynced or GuestPhase.TooFarBack )
			return;
		var behind = level.TickCount - progress.Ticks;
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
		transport.Send( guest.Id, new CoopTooFarBack( from ).Encode() );
	}

	private void Request( Guest guest, CoopResendRequest request, double now )
	{
		if ( guest.Phase is GuestPhase.Desynced or GuestPhase.TooFarBack || request.From == 0 )
			return;
		if ( request.From < OldestFrame )
		{
			TooFarBack( guest, request.From );
			return;
		}
		var end = request.To == 0 ? nextSeq : Math.Min( request.To, nextSeq );
		if ( request.From >= end )
			return;
		// a repeated request moves the cursor: it never starts a second copy while the last one is still on its way
		if ( guest.Resending && request.From >= guest.ResendFrom && request.From <= guest.ResendCursor && now - guest.ResendSentAt < ResendRetrySeconds )
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
				if ( batch.Count == 0 || !transport.Send( guest.Id, new CoopResentFrames( batch ).Encode() ) )
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
	/// Drops the frames no guest needs any more and the server is known to hold, oldest first; past the caps (10,000 frames, 32 MiB) the oldest go even if a guest still needs them
	/// (it is told to resync when it asks), but never a frame the server is not known to hold.
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
			|| (history.Count - drop > MaximumHistoryFrames || historyBytes - droppedBytes > MaximumHistoryBytes) && history[drop].Seq <= relayed && history[drop].Seq < sendNext) )
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
			Fail( "The host's frames the server has not taken outgrew the history." );
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
	}
}
