namespace OpenTPW;

/// <summary>A hash checkpoint of a command log: the world hash after <paramref name="Ticks"/> simulation ticks.</summary>
public readonly record struct ParkCommandHashCheckpoint( long Ticks, ulong Hash );

/// <summary>
/// [EXT:online-coop-commands] A recorded build session (docs/COOP.md, "Recording and replay"): the park it started in (theme, world seed, the tick and world hash at the
/// start), every command with the tick it applied before, and hash checkpoints, so a replay from the same start can prove it reached the same world.
/// </summary>
public sealed record ParkCommandLogFile( string Theme, ulong Seed, long StartTick, ulong StartHash, IReadOnlyList<ParkCommandLogEntry> Entries, IReadOnlyList<ParkCommandHashCheckpoint> Checkpoints )
{
	/// <summary>The file's magic bytes, "OTPWCMDS".</summary>
	public static ReadOnlySpan<byte> Magic => "OTPWCMDS"u8;
	/// <summary>The file format version.</summary>
	public const int FormatVersion = 2;

	private const byte EndTag = 0;
	private const byte CommandTag = 1;
	private const byte CheckpointTag = 2;

	/// <summary>The file's bytes: the magic, the format version, the start, then the commands and checkpoints in tick order and an end mark.</summary>
	public byte[] Serialize()
	{
		var writer = new ParkCommandWriter();
		foreach ( var value in Magic )
			writer.Byte( value );
		writer.Int( FormatVersion );
		writer.String( Theme );
		writer.UInt64( Seed );
		writer.Long( StartTick );
		writer.UInt64( StartHash );
		var commands = 0;
		var checkpoints = 0;
		// commands and checkpoints merged by tick; the checkpoint after N ticks is taken before a command logged at N (which applies before tick N runs), so it comes first
		while ( commands < Entries.Count || checkpoints < Checkpoints.Count )
		{
			if ( checkpoints >= Checkpoints.Count || (commands < Entries.Count && Entries[commands].Tick < Checkpoints[checkpoints].Ticks) )
			{
				var entry = Entries[commands++];
				writer.Byte( CommandTag );
				writer.Long( entry.Tick );
				writer.Bytes( ParkCommandCodec.Encode( entry.Command ) );
			}
			else
			{
				var checkpoint = Checkpoints[checkpoints++];
				writer.Byte( CheckpointTag );
				writer.Long( checkpoint.Ticks );
				writer.UInt64( checkpoint.Hash );
			}
		}
		writer.Byte( EndTag );
		return writer.ToArray();
	}

	/// <summary>Reads a command log; throws <see cref="InvalidDataException"/> for anything that is not a complete log of this format.</summary>
	public static ParkCommandLogFile Deserialize( byte[] bytes )
	{
		ArgumentNullException.ThrowIfNull( bytes );
		if ( bytes.Length < Magic.Length || !bytes.AsSpan( 0, Magic.Length ).SequenceEqual( Magic ) )
			throw new InvalidDataException( "Not an OpenTPW command log." );
		var reader = new ParkCommandReader( bytes[Magic.Length..] );
		var version = reader.Int();
		if ( version is not (1 or FormatVersion) )
			throw new InvalidDataException( $"Command log format {version} is not {FormatVersion}." );
		var theme = reader.String();
		var seed = reader.UInt64();
		var startTick = reader.Long();
		var startHash = reader.UInt64();
		if ( startTick < 0 )
			throw new InvalidDataException( "The log starts at a negative tick." );
		var entries = new List<ParkCommandLogEntry>();
		var checkpoints = new List<ParkCommandHashCheckpoint>();
		var last = startTick;
		while ( true )
		{
			var tag = reader.Byte();
			if ( tag == EndTag )
				break;
			var tick = reader.Long();
			if ( tick < last )
				throw new InvalidDataException( $"The log goes back from tick {last} to {tick}." );
			last = tick;
			switch ( tag )
			{
				case CommandTag:
					var command = ParkCommandCodec.Decode( reader.Bytes( ParkCommandCodec.MaximumBytes ) );
					if ( version == 1 && ParkCommandCodec.IsNativeQueue( command ) )
						throw new InvalidDataException( "Legacy command logs cannot contain native queue commands." );
					entries.Add( new ParkCommandLogEntry( tick, command ) );
					break;
				case CheckpointTag:
					checkpoints.Add( new ParkCommandHashCheckpoint( tick, reader.UInt64() ) );
					break;
				default:
					throw new InvalidDataException( $"Unknown command log record {tag}." );
			}
		}
		if ( !reader.AtEnd )
			throw new InvalidDataException( "The command log has bytes after its end mark." );
		return new ParkCommandLogFile( theme, seed, startTick, startHash, entries, checkpoints );
	}
}

/// <summary>
/// [EXT:online-coop-commands] Records a running park's commands and its world hash every <see cref="CheckpointInterval"/> ticks (<c>--record-commands</c>): from
/// <see cref="Start"/> on, every command the park's <see cref="ParkCommands"/> applies, with its tick, and the checkpoints the level reports after its ticks.
/// </summary>
public sealed class ParkCommandRecorder : IDisposable
{
	/// <summary>The ticks between two hash checkpoints (one per second of simulation).</summary>
	public const int CheckpointInterval = FixedStepClock.TicksPerSecond;

	private readonly ParkCommands commands;
	private readonly List<ParkCommandLogEntry> entries = new();
	private readonly List<ParkCommandHashCheckpoint> checkpoints = new();
	private readonly string theme;
	private readonly ulong seed;
	private readonly long startTick;
	private readonly ulong startHash;

	public ParkCommandRecorder( ParkCommands commands, string theme, ulong seed, long startTick, ulong startHash )
	{
		this.commands = commands ?? throw new ArgumentNullException( nameof( commands ) );
		this.theme = theme ?? throw new ArgumentNullException( nameof( theme ) );
		this.seed = seed;
		this.startTick = startTick;
		this.startHash = startHash;
		commands.Applied += OnApplied;
	}

	public IReadOnlyList<ParkCommandLogEntry> Entries => entries;
	public IReadOnlyList<ParkCommandHashCheckpoint> Checkpoints => checkpoints;

	/// <summary>Called by the level after a tick, with the ticks run so far; a checkpoint is taken every <see cref="CheckpointInterval"/> ticks from the start.</summary>
	public void AfterTick( long ticks, Func<ulong> hash )
	{
		if ( (ticks - startTick) % CheckpointInterval == 0 )
			checkpoints.Add( new ParkCommandHashCheckpoint( ticks, hash() ) );
	}

	/// <summary>The log as recorded so far, with a final checkpoint after <paramref name="ticks"/> ticks when given.</summary>
	public ParkCommandLogFile ToLog( (long Ticks, ulong Hash)? final = null )
	{
		var all = checkpoints.ToList();
		if ( final is { } last && (all.Count == 0 || all[^1].Ticks != last.Ticks) )
			all.Add( new ParkCommandHashCheckpoint( last.Ticks, last.Hash ) );
		return new ParkCommandLogFile( theme, seed, startTick, startHash, entries.ToArray(), all );
	}

	private void OnApplied( ParkCommandLogEntry entry, ParkCommandResult result ) => entries.Add( entry );

	public void Dispose() => commands.Applied -= OnApplied;
}

/// <summary>
/// [EXT:online-coop-commands] Replays a command log on a park that stands where the log started (docs/COOP.md): every command is scheduled before its tick and the park is
/// run tick by tick, so each command lands exactly where it was recorded. The hash after every tick goes to <c>afterTick</c>; a checkpoint that differs is reported.
/// </summary>
public static class ParkCommandReplay
{
	/// <summary>
	/// Schedules every entry of <paramref name="entries"/> on <paramref name="commands"/>, then runs <paramref name="tick"/> (one simulation tick) until <paramref name="nextTick"/>
	/// reaches <paramref name="endTick"/>, applying the commands due before each tick. Commands logged at <paramref name="endTick"/> itself are applied after the last tick.
	/// </summary>
	public static void Run( ParkCommands commands, IReadOnlyList<ParkCommandLogEntry> entries, Func<long> nextTick, Action tick, long endTick, Action<long>? afterTick = null )
	{
		ArgumentNullException.ThrowIfNull( commands );
		ArgumentNullException.ThrowIfNull( entries );
		foreach ( var entry in entries )
			commands.Schedule( entry.Command, entry.Tick );
		while ( nextTick() < endTick )
		{
			var before = nextTick();
			tick();
			if ( nextTick() != before + 1 )
				throw new InvalidOperationException( $"A replay tick ran {nextTick() - before} ticks, not one." );
			afterTick?.Invoke( nextTick() );
		}
		commands.ApplyDue( endTick );
	}

	/// <summary>The first checkpoint of <paramref name="log"/> whose hash differs from <paramref name="hashes"/> (ticks run → hash), or null when every recorded checkpoint matches.</summary>
	public static ParkCommandHashCheckpoint? FirstMismatch( ParkCommandLogFile log, IReadOnlyDictionary<long, ulong> hashes )
	{
		foreach ( var checkpoint in log.Checkpoints )
			if ( !hashes.TryGetValue( checkpoint.Ticks, out var hash ) || hash != checkpoint.Hash )
				return checkpoint;
		return null;
	}
}
