using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-lockstep] A guest's side of the host's command stream (docs/COOP.md, "Sessions"): it takes the host's frames in order (asking for any it missed), schedules each
/// record's command before the record's tick and runs its own ticks only up to the host's horizon, at one point of each game frame. It checks every record's outcome and the host's
/// digests at their checkpoints; a mismatch stops its clock (a desync) until a resync. It tells the host how far it is twice a second.
/// </summary>
internal sealed class CoopGuestStream : ILevelSessionStream, IDisposable
{
	/// <summary>The time between two progress reports (one also goes at once when the guest's state changes).</summary>
	public const double ProgressSeconds = 0.5;
	public const int MaximumHeldFrames = 10_000;
	public const long MaximumHeldBytes = 32L << 20;
	/// <summary>The time a game frame may spend on ticks; more while far behind (over a park second).</summary>
	public const double FrameBudgetSeconds = 0.008;
	public const double CatchUpBudgetSeconds = 0.024;
	/// <summary>The share of the ticks beyond its aim a frame runs extra, so a guest catches up smoothly instead of in one jump.</summary>
	public const double CatchUpShare = 0.25;

	private readonly record struct Expected( long Number, CoopRecordFlags Flags, CoopRecordOrigin? Origin );

	private readonly Level level;
	private readonly ICoopStreamTransport transport;
	private readonly Func<double> seconds;
	private readonly SortedDictionary<ulong, ReadOnlyMemory<byte>> held = new();
	private readonly Queue<ReadOnlyMemory<byte>> ready = new();
	private readonly Dictionary<ParkCommand, Expected> expected = new( ReferenceEqualityComparer.Instance );
	private readonly Dictionary<long, ulong> recordCheckpoints = new();
	private readonly SortedDictionary<long, ulong> tickCheckpoints = new();
	private readonly bool previousSessionRules;
	private readonly ParkCommandInTickRule previousInTick;
	private readonly IParkCommandRights? previousRights;
	private readonly int previousPlayer;
	private readonly bool previousFollows;
	private long heldBytes;
	private ulong nextFrame;
	private long horizon;
	private long nextStream = 1;
	private long firstApplied;
	private long lastApplied;
	private uint desyncs;
	private bool reportSeen;
	private bool tooFarBack;
	private double lastProgress = double.NegativeInfinity;
	private CoopGuestState? lastSentState;
	private double tickCredit;
	private bool silenced;
	private bool disposed;

	/// <param name="seconds">A monotonic clock in seconds (wall time; the tests' virtual one).</param>
	/// <param name="generation">[EXT:online-coop-snapshot-join] The join snapshot the stream starts from (0 a session's identical start); every stream message carries it.</param>
	/// <param name="cut">The snapshot's cut: records up to it applied, its tick the level's next, its frame the last (null: the session's own start, from the next frame the server relays).</param>
	/// <param name="desyncs">The desync ids the session gave out before this stream (they go on across resyncs).</param>
	public CoopGuestStream( Level level, ICoopStreamTransport transport, Func<double>? seconds = null, uint generation = 0, (long Records, long Ticks, ulong Frame)? cut = null,
		uint desyncs = 0 )
	{
		this.level = level ?? throw new ArgumentNullException( nameof( level ) );
		this.transport = transport ?? throw new ArgumentNullException( nameof( transport ) );
		this.seconds = seconds ?? CoopHostStream.DefaultSeconds;
		var session = transport.Session ?? throw new InvalidOperationException( "A guest's stream needs a session." );
		if ( session.You == session.Host )
			throw new InvalidOperationException( "The host's game streams; it does not follow." );
		if ( cut is { } start && start.Ticks != level.TickCount )
			throw new InvalidOperationException( $"The park stands at tick {level.TickCount}, not at the cut's {start.Ticks}." );
		Generation = generation;
		this.desyncs = desyncs;
		nextFrame = cut is { } from ? from.Frame + 1 : session.LastFrameSeq + 1;
		nextStream = (cut?.Records ?? 0) + 1;
		horizon = level.TickCount;
		var commands = level.Commands;
		previousSessionRules = commands.SessionRules;
		previousInTick = commands.InTick;
		previousRights = commands.Rights;
		previousPlayer = commands.Player;
		previousFollows = commands.FollowsStream;
		commands.SessionRules = true;
		commands.InTick = ParkCommandInTickRule.Drop;
		// [EXT:online-coop-permissions] the session's rights, whatever rights the park had: every peer asks the same ones
		commands.Rights = new CoopBuildRights( level );
		// [EXT:online-coop-commands] the guest's park takes the host's stream only: its own issues never run here, so its log is the host's stream (a granted guest's building goes to the host)
		commands.FollowsStream = true;
		// the guest's own issues carry its slot, as the commands it sends the host do
		var slot = session.Members.FirstOrDefault( member => member.Id == session.You )?.Slot ?? 1;
		commands.Player = Math.Max( 1, (int)slot );
		commands.Applied += OnApplied;
		level.TickEnded += OnTickEnded;
		transport.FrameReceived += OnFrame;
		transport.MessageReceived += OnMessage;
		transport.Resumed += OnResumed;
		level.SessionStream = this;
	}

	/// <summary>[EXT:online-coop-snapshot-join] The join snapshot this stream started from (0 a session's identical start).</summary>
	public uint Generation { get; }

	/// <summary>The desync ids given out so far (the session's next stream goes on from them).</summary>
	public uint Desyncs => desyncs;

	/// <summary>The desync this guest found, or null; its clock stands still while there is one (until a resync, C2c).</summary>
	public CoopDesyncReport? Desync { get; private set; }

	/// <summary>Raised once when the guest finds a desync.</summary>
	public event Action<CoopDesyncReport>? Desynced;

	/// <summary>
	/// [EXT:online-coop-commands] Raised for each stream record that applied here as on the host (its flags, and its digest when the host sent one, agree): its number, its origin when a
	/// guest's request made it, and what it did here (<see cref="CoopGuestCommands.RecordApplied"/>).
	/// </summary>
	public event Action<long, CoopRecordOrigin?, ParkCommandResult>? RecordApplied;

	/// <summary>Whether the guest's clock stands still (a desync, the host no longer holds the frames it needs, or a newer capture is on its way: a resync, C2c).</summary>
	public bool Stopped => Desync != null || tooFarBack || silenced;

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The host assigned a newer capture: the stream stops as a desync stops it (no frames applied, no ticks) and goes silent (no progress, resend requests or
	/// desyncs). It stays attached, so its level stays paused and the player's clicks stay refused, until the session swaps the capture's park in.
	/// </summary>
	public void Silence() => silenced = true;

	/// <summary>Whether the stream went silent (<see cref="Silence"/>).</summary>
	public bool Silenced => silenced;

	/// <summary>Takes a frame the session buffered before the stream started (<see cref="Accept"/>).</summary>
	internal void Take( ulong seq, ReadOnlyMemory<byte> payload ) => Accept( seq, payload );

	/// <summary>
	/// Asks the host for every frame from the next one this stream needs: a stream that starts at a capture's cut may have lost frames on their way (a socket that dropped during the
	/// download), also after the frames it holds past a gap, and an idle host publishes no later frame that would show them. The host sends what it holds from there, or nothing; a frame
	/// this stream holds already is a copy.
	/// </summary>
	internal void RequestMissing() => Send( new CoopResendRequest( nextFrame, 0 ) );

	/// <summary>The host's horizon the guest holds: it runs every tick below it.</summary>
	public long Horizon => horizon;

	/// <summary>The last frame the guest holds without a gap.</summary>
	public ulong LastFrame => nextFrame - 1;

	/// <summary>The frames held after a gap.</summary>
	public int HeldFrames => held.Count;

	/// <summary>The last stream record this guest took (scheduled or applied).</summary>
	public long LastRecord => nextStream - 1;

	/// <summary>The host's digests this guest compared so far (tick and record checkpoints).</summary>
	public int Checks { get; private set; }

	/// <summary>The digests after an errored record this guest compared so far.</summary>
	public int RecordChecks { get; private set; }

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The last frame the stream had applied when its last frame point left it following within its window (every frame it held applied, its ticks
	/// run); 0 when that frame point did not, or held its ticks (<see cref="HoldTicksOneFrame"/>). A join's catch-up ends at the first such frame point past the cut.
	/// </summary>
	internal ulong FollowingThrough { get; private set; }

	/// <summary>The guest's state as its progress reports it.</summary>
	public CoopGuestState State => Desync != null ? CoopGuestState.Desynced
		: tooFarBack ? CoopGuestState.TooFarBack
		: held.Count > 0 ? CoopGuestState.Gap
		: horizon - level.TickCount > WindowTicks ? CoopGuestState.CatchingUp
		: CoopGuestState.Following;

	private long WindowTicks => (long)(CoopHostStream.WindowSeconds * FixedStepClock.TicksPerSecond * Math.Max( 1f, GameSpeedScale.Of( level.Park?.Economy.Speed ?? GameSpeed.Normal ) ));

	private bool holdTicks;

	/// <summary>
	/// The next frame point applies the frames that came but runs no ticks: the frame that swapped this stream's park in has its first-time work, and the ticks wait for the next frame
	/// (deferred, never dropped: the park after a tick depends on the ticks and the records only).
	/// </summary>
	internal void HoldTicksOneFrame() => holdTicks = true;

	/// <summary>The frame point: the frames that came since the last game frame apply (never inside a tick), then the guest runs its ticks toward the horizon.</summary>
	public void AdvanceSimulation( double frameSeconds, float timeScale )
	{
		var now = seconds();
		if ( !Stopped )
		{
			using ( FrameMarks.Measure( "coop.ready" ) )
				ApplyReady();
		}
		var held = holdTicks;
		holdTicks = false;
		if ( !Stopped && !held )
		{
			using ( FrameMarks.Measure( "coop.ticks" ) )
				RunTicks( frameSeconds, now );
		}
		// a frame point that held its ticks follows nothing yet: it ran none toward the frames it applied
		FollowingThrough = !Stopped && !held && ready.Count == 0 && State == CoopGuestState.Following ? LastFrame : 0;
		if ( !silenced )
			Progress( now );
	}

	private void OnFrame( CoopFrame frame ) => Accept( frame.Seq, frame.Payload );

	/// <summary>Takes frame <paramref name="seq"/>: the next one waits for the frame point, a later one is held and the missing ones asked for, an earlier one is a copy.</summary>
	private void Accept( ulong seq, ReadOnlyMemory<byte> payload )
	{
		if ( Stopped || seq < nextFrame )
			return;
		if ( seq == nextFrame )
		{
			ready.Enqueue( payload );
			nextFrame++;
			while ( held.Remove( nextFrame, out var next ) )
			{
				heldBytes -= next.Length;
				ready.Enqueue( next );
				nextFrame++;
			}
			return;
		}
		var gap = held.Count == 0;
		// past the caps a later frame is dropped: the request after this gap closes brings it again
		if ( !held.ContainsKey( seq ) && held.Count < MaximumHeldFrames && heldBytes + payload.Length <= MaximumHeldBytes )
		{
			held[seq] = payload;
			heldBytes += payload.Length;
		}
		if ( gap )
			RequestResend();
	}

	private void RequestResend() => Send( new CoopResendRequest( nextFrame, held.Count > 0 ? held.Keys.First() : 0 ) );

	/// <summary>Sends <paramref name="message"/> to the host in this stream's generation.</summary>
	private void Send( CoopStreamMessage message ) => transport.Send( Host, (message with { Generation = Generation }).Encode() );

	private uint Host => transport.Session?.Host ?? 0;

	private void OnMessage( CoopRelayed message )
	{
		if ( message.From != Host )
			return;
		CoopStreamMessage decoded;
		try
		{
			decoded = CoopStreamMessage.Decode( message.Payload.Span );
		}
		catch ( InvalidDataException error )
		{
			Log.Warning( $"The shared park's host sent a message this game cannot read: {error.Message}" );
			return;
		}
		// [EXT:online-coop-snapshot-join] the host's stream messages of another generation belong to a stream this one is not
		if ( decoded.Generation != Generation || silenced )
			return;
		switch ( decoded )
		{
			case CoopResentFrames resent:
				foreach ( var (seq, payload) in resent.Frames )
					Accept( seq, payload );
				break;
			case CoopTooFarBack back when !Stopped:
				Log.Warning( $"The shared park's host no longer holds frame {back.From}: this game needs the park again." );
				tooFarBack = true;
				Progress( seconds() );
				break;
			case CoopDesyncSeen seen when Desync?.Id == seen.Id:
				reportSeen = true;
				break;
		}
	}

	/// <summary>After its own resume the guest asks for whatever it may have missed while away: every frame from the first it lacks, also those after the frames it holds past a gap.</summary>
	private void OnResumed( CoopSessionState session )
	{
		if ( Stopped || silenced )
			return;
		RequestMissing();
		lastSentState = null;
	}

	/// <summary>Applies the frames that came, in order: each record's command is held until its tick, the checkpoints are remembered, the horizon moves.</summary>
	private void ApplyReady()
	{
		while ( !Stopped && ready.TryDequeue( out var payload ) )
		{
			CoopStreamFrame frame;
			try
			{
				frame = CoopStreamFrame.Decode( payload.Span );
			}
			catch ( InvalidDataException error )
			{
				Fail( CoopDesyncKind.Protocol, $"a frame that does not decode ({error.Message})" );
				return;
			}
			if ( frame.Horizon < horizon )
			{
				Fail( CoopDesyncKind.Protocol, $"a horizon that falls from {horizon} to {frame.Horizon}" );
				return;
			}
			if ( frame.First != nextStream )
			{
				Fail( CoopDesyncKind.Protocol, $"record {frame.First} where {nextStream} was next" );
				return;
			}
			var ticks = level.TickCount;
			for ( var index = 0; index < frame.Records.Count; index++ )
			{
				var record = frame.Records[index];
				if ( record.Boundary < ticks )
				{
					Fail( CoopDesyncKind.Protocol, $"a record for tick {record.Boundary}, which this game ran already" );
					return;
				}
				ParkCommand command;
				try
				{
					command = ParkCommandCodec.Decode( record.Bytes.Span );
				}
				catch ( InvalidDataException error )
				{
					Fail( CoopDesyncKind.Protocol, $"a record that does not decode ({error.Message})" );
					return;
				}
				expected[command] = new Expected( frame.First + index, record.Flags, record.Origin );
				level.Commands.Schedule( command, record.Boundary );
			}
			nextStream = frame.First + frame.Records.Count;
			foreach ( var checkpoint in frame.Checkpoints )
			{
				if ( checkpoint.Kind == CoopCheckpointKind.AfterRecord )
					recordCheckpoints[checkpoint.At] = checkpoint.Digest;
				else if ( checkpoint.At > ticks )
					tickCheckpoints[checkpoint.At] = checkpoint.Digest;
				else
				{
					Fail( CoopDesyncKind.Protocol, $"a checkpoint for tick {checkpoint.At}, which this game ran already" );
					return;
				}
			}
			horizon = frame.Horizon;
		}
		// the records of the tick the guest stands at (a paused park, clicks between two ticks) apply now, outside any tick
		if ( !Stopped )
			level.Commands.ApplyDue( level.TickCount );
	}

	/// <summary>
	/// Runs the frame's ticks toward the horizon: the park's own rate at its speed, aiming to stay one published frame behind, plus a share of any further lag; within a time budget,
	/// never past the horizon, never a dropped tick.
	/// </summary>
	private void RunTicks( double frameSeconds, double now )
	{
		var available = horizon - level.TickCount;
		if ( available <= 0 )
		{
			tickCredit = 0;
			return;
		}
		var rate = FixedStepClock.TicksPerSecond * (double)GameSpeedScale.Of( level.Park?.Economy.Speed ?? GameSpeed.Normal );
		var aim = Math.Ceiling( rate * CoopHostStream.PublishSeconds );
		var want = rate > 0 ? frameSeconds * rate : available;
		if ( available > 2 * aim )
			want += (available - aim) * CatchUpShare;
		tickCredit += want;
		var ticks = (long)Math.Min( Math.Floor( tickCredit ), available );
		tickCredit = ticks == available ? 0 : tickCredit - ticks;
		var budget = available > rate ? CatchUpBudgetSeconds : FrameBudgetSeconds;
		for ( var run = 0L; run < ticks && !Stopped; run++ )
		{
			level.RunTicks( 1 );
			if ( seconds() - now >= budget )
			{
				tickCredit = 0;
				break;
			}
		}
	}

	/// <summary>Twice a second, and at once when the state changes: the progress, with the resend request while a gap lasts and the desync until the host has seen it.</summary>
	private void Progress( double now )
	{
		var state = State;
		if ( now - lastProgress < ProgressSeconds && state == lastSentState )
			return;
		lastProgress = now;
		lastSentState = state;
		var report = Desync is { } desync && !reportSeen ? desync : null;
		Send( new CoopProgress( state, LastFrame, level.TickCount, report?.Id ?? 0 ) );
		if ( held.Count > 0 && !Stopped )
			RequestResend();
		if ( report != null )
			Send( report );
	}

	private void OnApplied( ParkCommandLogEntry entry, ParkCommandResult result )
	{
		if ( Stopped )
			return;
		if ( !expected.Remove( entry.Command, out var want ) )
		{
			Fail( CoopDesyncKind.Protocol, $"this game applied a {entry.Command.Kind} command the host did not stream", entry.Tick );
			return;
		}
		if ( firstApplied == 0 )
			firstApplied = want.Number;
		lastApplied = want.Number;
		var flags = result.Error != null ? CoopRecordFlags.Errored : result.Accepted ? CoopRecordFlags.Accepted : CoopRecordFlags.None;
		if ( flags != want.Flags )
		{
			Fail( CoopDesyncKind.Flags, $"record {want.Number} ({entry.Command.Kind}) was {Outcome( want.Flags )} on the host and {Outcome( flags )} here", entry.Tick );
			return;
		}
		if ( recordCheckpoints.Remove( want.Number, out var digest ) )
		{
			Checks++;
			RecordChecks++;
			if ( CoopDigest.Compute( level ) is var mine && mine != digest )
			{
				Fail( CoopDesyncKind.Digest, $"the digest after record {want.Number} differs", entry.Tick, digest, mine );
				return;
			}
		}
		RecordApplied?.Invoke( want.Number, want.Origin, result );
	}

	private static string Outcome( CoopRecordFlags flags ) => flags switch
	{
		CoopRecordFlags.Accepted => "accepted",
		CoopRecordFlags.Errored => "errored",
		_ => "refused"
	};

	private void OnTickEnded( long ticks )
	{
		if ( Stopped || !tickCheckpoints.Remove( ticks, out var digest ) )
			return;
		Checks++;
		if ( CoopDigest.Compute( level ) is var mine && mine != digest )
			Fail( CoopDesyncKind.Digest, $"the digest after tick {ticks} differs", ticks, digest, mine );
	}

	/// <summary>A desync: the guest's clock stops, and it reports the desync with every progress until the host has seen it.</summary>
	private void Fail( CoopDesyncKind kind, string reason, long? tick = null, ulong hostDigest = 0, ulong guestDigest = 0 )
	{
		if ( Desync != null )
			return;
		Desync = new CoopDesyncReport( ++desyncs, kind, tick ?? level.TickCount, hostDigest, guestDigest, firstApplied, lastApplied );
		reportSeen = false;
		Log.Warning( $"The shared park desynced ({kind}) at tick {Desync.Tick}: {reason}." );
		Desynced?.Invoke( Desync );
		lastSentState = null;
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		level.Commands.Applied -= OnApplied;
		level.TickEnded -= OnTickEnded;
		transport.FrameReceived -= OnFrame;
		transport.MessageReceived -= OnMessage;
		transport.Resumed -= OnResumed;
		if ( level.SessionStream == this )
			level.SessionStream = null;
		var commands = level.Commands;
		commands.ClearScheduled();
		commands.SessionRules = previousSessionRules;
		commands.InTick = previousInTick;
		commands.Rights = previousRights;
		commands.Player = previousPlayer;
		commands.FollowsStream = previousFollows;
		// [EXT:online-coop-permissions] a grant is its session's: the park left after the session (or replaced by a resync's) keeps none, nor what a guest's seat carried (C3b-2)
		level.ClearBuildGrants();
		level.ClearGuestCarries();
		// the park's scripts read the machine's own clock again
		if ( level.Objects?.ScriptWorld is { } scripts )
			scripts.SessionTime = null;
	}
}
