namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-permissions] The host's per-player rights (the user's decision: "the host sets rights per player", docs/COOP.md): whether
/// <paramref name="player"/> may issue a command of <paramref name="kind"/>. Only the hook exists; a park without rights (null) allows every command. A refusal changes
/// nothing and is the same on every peer, since the rights travel with the session.
/// </summary>
public interface IParkCommandRights
{
	bool Allows( int player, ParkCommandKind kind );
}

/// <summary>What became of one command: accepted (it ran) or rejected (nothing changed), with the reason and the value the edit returns to its issuer.</summary>
public sealed record ParkCommandResult( bool Accepted, string? Reason = null, object? Value = null )
{
	public static ParkCommandResult Ok( object? value = null ) => new( true, null, value );
	public static ParkCommandResult Rejected( string reason ) => new( false, reason );

	/// <summary>The edit's value as <typeparamref name="T"/> (the placed object, the laid line, a count), or the default.</summary>
	public T? As<T>() => Value is T value ? value : default;
}

/// <summary>One logged command: the tick it applied before and the command.</summary>
public readonly record struct ParkCommandLogEntry( long Tick, ParkCommand Command );

/// <summary>
/// [EXT:online-coop-commands] The park's one funnel for edits (docs/COOP.md): every HUD window and tool hands its edit to <see cref="Apply"/> as a <see cref="ParkCommand"/>.
/// A command applies before simulation tick <see cref="NextTick"/>, the tick the next frame runs first: in single player it runs at once, between two ticks as every edit did
/// before the command layer, so no single-player run changes. <see cref="Schedule"/> holds a command for a later tick (a replayed log now, the lockstep frames of a session
/// later); <see cref="ApplyDue"/> runs the held commands at the start of their tick. Every well-formed command, accepted or not, is logged with its tick, so the log of a session
/// replays to the same world (<see cref="ParkCommandReplay"/>).
/// </summary>
public sealed class ParkCommands
{
	/// <summary>The player id of the local player in single player (and of a session's host).</summary>
	public const int LocalPlayer = 0;
	/// <summary>The most commands the in-memory log keeps; older ones are dropped and counted (<see cref="DroppedLog"/>), a recording to a file keeps every one.</summary>
	public const int MaximumLog = 1 << 16;

	private readonly Func<long> nextTick;
	private readonly Func<ParkCommand, ParkCommandResult> execute;
	private readonly Func<bool> refusesEdits;
	private readonly ParkCommandLogRing log = new( MaximumLog );
	private readonly SortedDictionary<long, List<ParkCommand>> scheduled = new();
	private int depth;

	/// <param name="nextTick">The index of the next simulation tick (the fixed-step clock's tick count).</param>
	/// <param name="execute">Runs one well-formed command on the park; a rejection must leave the park unchanged.</param>
	/// <param name="refusesEdits">True while the park takes no edits at all (a read-only visit).</param>
	public ParkCommands( Func<long> nextTick, Func<ParkCommand, ParkCommandResult> execute, Func<bool> refusesEdits )
	{
		this.nextTick = nextTick ?? throw new ArgumentNullException( nameof( nextTick ) );
		this.execute = execute ?? throw new ArgumentNullException( nameof( execute ) );
		this.refusesEdits = refusesEdits ?? throw new ArgumentNullException( nameof( refusesEdits ) );
	}

	/// <summary>The local player's id, put on the commands the HUD and the tools issue (<see cref="LocalPlayer"/> until a session gives this machine its slot).</summary>
	public int Player { get; set; } = LocalPlayer;

	/// <summary>The host's per-player rights; null allows every command (single player).</summary>
	public IParkCommandRights? Rights { get; set; }

	/// <summary>The tick the next command applies before.</summary>
	public long NextTick => nextTick();

	/// <summary>The logged commands, oldest first (at most <see cref="MaximumLog"/>).</summary>
	public IReadOnlyList<ParkCommandLogEntry> Log => log;

	/// <summary>How many of the oldest log entries were dropped to stay within <see cref="MaximumLog"/>.</summary>
	public long DroppedLog { get; private set; }

	/// <summary>Raised for every applied command (after it ran), with its tick and result: the hook of a recording (<see cref="ParkCommandRecorder"/>).</summary>
	public event Action<ParkCommandLogEntry, ParkCommandResult>? Applied;

	/// <summary>Whether a command is being executed now (an edit inside an edit runs directly, unlogged, as part of the outer command).</summary>
	public bool IsExecuting => depth > 0;

	/// <summary>The commands held for later ticks.</summary>
	public int ScheduledCount => scheduled.Values.Sum( list => list.Count );

	/// <summary>
	/// Applies <paramref name="command"/> now, before <see cref="NextTick"/>: checks it is well formed, that the park takes edits and that the player may issue it, then runs it.
	/// A rejected command changes nothing; it is logged unless it is malformed. A command issued while another runs (a tool callback inside an edit) runs as part of the outer one and is not logged on its own.
	/// </summary>
	public ParkCommandResult Apply( ParkCommand command )
	{
		ArgumentNullException.ThrowIfNull( command );
		if ( depth > 0 )
			return Run( command );
		// a malformed command never reaches the park and has no byte form to log: it is refused before the log
		if ( !command.IsWellFormed )
			return ParkCommandResult.Rejected( $"The {command.Kind} command has fields out of range." );
		var tick = nextTick();
		var result = Run( command );
		Record( new ParkCommandLogEntry( tick, command ), result );
		return result;
	}

	/// <summary>Issues <paramref name="command"/> as the local player (<see cref="Player"/>) and applies it now.</summary>
	public ParkCommandResult Issue( ParkCommand command )
	{
		ArgumentNullException.ThrowIfNull( command );
		return Apply( command with { Player = Player } );
	}

	/// <summary>Holds <paramref name="command"/> until tick <paramref name="tick"/> (not before <see cref="NextTick"/>); <see cref="ApplyDue"/> applies it at the start of that tick.</summary>
	public void Schedule( ParkCommand command, long tick )
	{
		ArgumentNullException.ThrowIfNull( command );
		if ( tick < nextTick() )
			throw new ArgumentOutOfRangeException( nameof( tick ), $"Tick {tick} is before the next tick {nextTick()}." );
		if ( !scheduled.TryGetValue( tick, out var list ) )
			scheduled.Add( tick, list = new List<ParkCommand>() );
		list.Add( command );
	}

	/// <summary>Applies, in the order they were scheduled, the held commands of every tick up to <paramref name="tick"/> (the tick about to run).</summary>
	public void ApplyDue( long tick )
	{
		while ( scheduled.Count > 0 )
		{
			var first = scheduled.First();
			if ( first.Key > tick )
				return;
			scheduled.Remove( first.Key );
			foreach ( var command in first.Value )
				Apply( command );
		}
	}

	/// <summary>Drops every held command (a session that ends, a level that closes).</summary>
	public void ClearScheduled() => scheduled.Clear();

	private ParkCommandResult Run( ParkCommand command )
	{
		if ( !command.IsWellFormed )
			return ParkCommandResult.Rejected( $"The {command.Kind} command has fields out of range." );
		if ( command.Player < 0 )
			return ParkCommandResult.Rejected( $"Player {command.Player} is not a player." );
		if ( refusesEdits() )
			return ParkCommandResult.Rejected( "Read-only visit: the park takes no edits." );
		if ( Rights is { } rights && !rights.Allows( command.Player, command.Kind ) )
			return ParkCommandResult.Rejected( $"Player {command.Player} may not {command.Kind}." );
		depth++;
		try
		{
			return execute( command );
		}
		finally
		{
			depth--;
		}
	}

	private void Record( ParkCommandLogEntry entry, ParkCommandResult result )
	{
		if ( log.Add( entry ) )
			DroppedLog++;
		Applied?.Invoke( entry, result );
	}
}

/// <summary>
/// The in-memory command log (<see cref="ParkCommands.Log"/>): a ring of at most <see cref="Capacity"/> entries, read oldest first. Adding to a full ring overwrites its oldest
/// entry in constant time (a long bulldozer drag issues one command per cell), so the order and the kept entries are those of a list that drops its first entry when full.
/// </summary>
internal sealed class ParkCommandLogRing : IReadOnlyList<ParkCommandLogEntry>
{
	private readonly List<ParkCommandLogEntry> entries = new();
	/// <summary>The slot of the oldest entry once the ring is full (0 until then).</summary>
	private int oldest;
	/// <summary>Bumped by every <see cref="Add"/>: an enumeration across an add fails, as a list's does.</summary>
	private int version;

	public ParkCommandLogRing( int capacity )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( capacity );
		Capacity = capacity;
	}

	/// <summary>The most entries the ring keeps.</summary>
	public int Capacity { get; }

	public int Count => entries.Count;

	public ParkCommandLogEntry this[int index]
	{
		get
		{
			if ( (uint)index >= (uint)entries.Count )
				throw new ArgumentOutOfRangeException( nameof( index ) );
			var slot = oldest + index;
			return entries[slot >= entries.Count ? slot - entries.Count : slot];
		}
	}

	/// <summary>Adds <paramref name="entry"/> as the newest entry; returns true when the ring was full and its oldest entry was dropped.</summary>
	public bool Add( ParkCommandLogEntry entry )
	{
		version++;
		if ( entries.Count < Capacity )
		{
			entries.Add( entry );
			return false;
		}
		entries[oldest] = entry;
		oldest = oldest + 1 == Capacity ? 0 : oldest + 1;
		return true;
	}

	public IEnumerator<ParkCommandLogEntry> GetEnumerator()
	{
		var start = version;
		for ( var index = 0; index < entries.Count; index++ )
		{
			if ( version != start )
				throw new InvalidOperationException( "The command log changed during its enumeration." );
			yield return this[index];
		}
		if ( version != start )
			throw new InvalidOperationException( "The command log changed during its enumeration." );
	}

	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
