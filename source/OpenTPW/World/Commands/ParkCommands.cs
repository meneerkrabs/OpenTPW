using System.Runtime.ExceptionServices;
namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-permissions] The host's per-player rights (the user's decision: "the host sets rights per player", docs/COOP.md): whether
/// <paramref name="player"/> may issue a command of <paramref name="kind"/>. Only the hook exists; a park without rights (null) allows every command. A refusal changes
/// nothing and is the same on every peer, since the rights travel with the session.
/// </summary>
public interface IParkCommandRights
{
	bool Allows( int player, ParkCommandKind kind );

	/// <summary>
	/// [EXT:online-coop-permissions] Whether <paramref name="player"/> may issue <paramref name="command"/>, its fields and target included (a guest's allowlist names variants of
	/// a kind, C3): by default the kind's answer.
	/// </summary>
	bool Allows( int player, ParkCommand command ) => Allows( player, command.Kind );

	/// <summary>
	/// [EXT:online-coop-permissions] Whether <paramref name="player"/> may remove <paramref name="item"/> inside a command that removes what it finds (the area clear, C3b): a session's
	/// guest may not remove what its own removal click may not. By default yes.
	/// </summary>
	bool AllowsRemoval( int player, OriginalObject item ) => true;

	/// <summary>
	/// [EXT:online-coop-permissions] Whether <paramref name="player"/> may clear the path cell (<paramref name="x"/>, <paramref name="y"/>) inside a command that clears what it finds (the area
	/// clear): a session's guest may not clear what its own Backspace may not. By default yes.
	/// </summary>
	bool AllowsPathRemoval( int player, int x, int y ) => true;
}

/// <summary>What became of one command: accepted (it ran) or rejected (nothing changed), with the reason and the value the edit returns to its issuer.</summary>
public sealed record ParkCommandResult( bool Accepted, string? Reason = null, object? Value = null )
{
	/// <summary>The exception a command threw (a rule its own checks missed): it is a rejection with whatever the command wrote before the throw, logged like any other.</summary>
	public Exception? Error { get; init; }

	/// <summary>[EXT:online-coop-permissions] The rights refused the command (a session's: a guest's building without a grant, a host's command): the issuer says so in its own words.</summary>
	public bool RightsRefused { get; init; }

	/// <summary>
	/// [EXT:online-coop-commands] The command went to the host as a guest's request (C3): nothing changed here yet, and the request resolves later, when its record applies here or the host
	/// refuses it (<see cref="CoopGuestCommands"/>).
	/// </summary>
	internal CoopGuestCommands.Request? Request { get; init; }

	/// <summary>[EXT:online-coop-commands] Whether the command waits for the host (<see cref="Request"/>).</summary>
	public bool IsPending => Request != null;

	public static ParkCommandResult Ok( object? value = null ) => new( true, null, value );
	public static ParkCommandResult Rejected( string reason ) => new( false, reason );
	internal static ParkCommandResult Sent( CoopGuestCommands.Request request ) => new( false, "Sent to the host." ) { Request = request };

	/// <summary>The edit's value as <typeparamref name="T"/> (the placed object, the laid line, a count), or the default.</summary>
	public T? As<T>() => Value is T value ? value : default;
}

/// <summary>One logged command: the tick it applied before and the command.</summary>
public readonly record struct ParkCommandLogEntry( long Tick, ParkCommand Command );

/// <summary>What the funnel does with a command applied while a simulation tick runs (<see cref="ParkCommands.InTick"/>; <see cref="ParkCommands.Strict"/> throws first).</summary>
public enum ParkCommandInTickRule
{
	/// <summary>Runs it at once, as a game of one always did.</summary>
	RunAtOnce,
	/// <summary>
	/// [EXT:online-coop-lockstep] Refuses it, logged as an error, neither recorded nor raised: a session's rule, since a peer would apply it at the tick's start (its log
	/// entry says "before tick N").
	/// </summary>
	Drop
}

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
	private readonly List<Action> afterIssue = new();
	private int depth;
	/// <summary>The outermost command running now, whose player a command issued inside it takes.</summary>
	private ParkCommand? running;
	private bool issuing;
	private bool inTick;
	/// <summary>The held commands of a tick are being applied (<see cref="ApplyDue"/>).</summary>
	private bool applyingDue;
	/// <summary>[EXT:online-coop-commands] A guest's command is being applied on the host (<see cref="ApplyRemote"/>).</summary>
	private bool applyingRemote;

	/// <param name="nextTick">The index of the next simulation tick (the fixed-step clock's tick count).</param>
	/// <param name="execute">Runs one well-formed command on the park; a rejection must leave the park unchanged.</param>
	/// <param name="refusesEdits">True while the park takes no edits at all (a read-only visit).</param>
	public ParkCommands( Func<long> nextTick, Func<ParkCommand, ParkCommandResult> execute, Func<bool> refusesEdits )
	{
		this.nextTick = nextTick ?? throw new ArgumentNullException( nameof( nextTick ) );
		this.execute = execute ?? throw new ArgumentNullException( nameof( execute ) );
		this.refusesEdits = refusesEdits ?? throw new ArgumentNullException( nameof( refusesEdits ) );
	}

	/// <summary>Whether the rights refuse <paramref name="command"/> its player (no rights refuse nothing).</summary>
	private bool Refuses( ParkCommand command ) => Rights is { } rights && !rights.Allows( command.Player, command );

	/// <summary>[EXT:online-coop-permissions] Whether <paramref name="player"/> may remove <paramref name="item"/> inside a command that removes what it finds: the rights' answer, yes without any.</summary>
	internal bool AllowsRemoval( int player, OriginalObject item ) => Rights is not { } rights || rights.AllowsRemoval( player, item );

	/// <summary>[EXT:online-coop-permissions] Whether <paramref name="player"/> may clear the path cell (<paramref name="x"/>, <paramref name="y"/>) inside a command that clears what it finds: the rights' answer, yes without any.</summary>
	internal bool AllowsPathRemoval( int player, int x, int y ) => Rights is not { } rights || rights.AllowsPathRemoval( player, x, y );

	/// <summary>
	/// [EXT:online-coop-commands] A guest's way to the host (C3): a command this machine's player issues on a park that follows a stream goes here as a request
	/// (<see cref="CoopGuestCommands.Submit"/>) once <see cref="Apply"/>'s own checks pass, and <see cref="Issue"/> returns it pending; null while no session takes requests.
	/// </summary>
	internal Func<ParkCommand, CoopGuestCommands.Request?>? Submit { get; set; }

	/// <summary>[EXT:online-coop-commands] Whether this machine's commands go to the host as requests (a guest's park with its session's way there, C3a-2): its tools wait for their outcomes.</summary>
	internal bool SendsRequests => FollowsStream && Submit != null;

	/// <summary>[EXT:online-coop-commands] The player of the outermost command running now, or null outside one: whose edit a message or a sound inside it belongs to (C3a-2).</summary>
	internal int? RunningPlayer => running?.Player;

	/// <summary>
	/// [EXT:online-coop-commands] Whether what runs now is this machine's player's own: outside any command, or inside a command it issued. The cues a command plays (a placement's,
	/// a demolition's, a path's) are its issuer's alone: a peer applying another player's command hears none (C3a-2). A game of one is always its own issuer.
	/// </summary>
	internal bool IsLocalIssuer => running is not { } command || command.Player == Player;

	/// <summary>The local player's id, put on the commands the HUD and the tools issue (<see cref="LocalPlayer"/> until a session gives this machine its slot).</summary>
	public int Player { get; set; } = LocalPlayer;

	/// <summary>The host's per-player rights; null allows every command (single player).</summary>
	public IParkCommandRights? Rights { get; set; }

	/// <summary>What a command applied during a simulation tick does: runs at once in a game of one, is dropped in a session.</summary>
	public ParkCommandInTickRule InTick { get; set; } = ParkCommandInTickRule.RunAtOnce;

	/// <summary>
	/// [EXT:online-coop-lockstep] The rules of a co-op session (docs/COOP.md): a command issued inside another (a HUD callback during an edit) is refused, since it would
	/// run unstreamed on its issuer only; a command of this machine (not held) that the rights refuse is neither logged nor raised, so a guest's own issues stay out of its
	/// log, which is the host's stream, while a held one the rights refuse is logged and raised as refused, so the stream sees it; and a held command never rethrows (a
	/// strict run included): its error is in its result, which the stream compares.
	/// </summary>
	public bool SessionRules { get; set; }

	/// <summary>
	/// [EXT:online-coop-commands] The park follows a host's stream (a co-op guest's, docs/COOP.md "Building by every player"): only the stream's held commands apply here. A command of this
	/// machine is refused before it runs, neither logged nor raised, as the rights refuse it, whatever they grant the guest's seat: a granted guest's building goes to the host as a request
	/// (<see cref="CoopGuestCommands"/>) and comes back in the stream.
	/// </summary>
	public bool FollowsStream { get; set; }

	/// <summary>The tick the next command applies before.</summary>
	public long NextTick => nextTick();

	/// <summary>The logged commands, oldest first (at most <see cref="MaximumLog"/>).</summary>
	public IReadOnlyList<ParkCommandLogEntry> Log => log;

	/// <summary>How many of the oldest log entries were dropped to stay within <see cref="MaximumLog"/>.</summary>
	public long DroppedLog { get; private set; }

	/// <summary>Raised for every applied command (after it ran), with its tick and result: the hook of a recording (<see cref="ParkCommandRecorder"/>).</summary>
	public event Action<ParkCommandLogEntry, ParkCommandResult>? Applied;

	/// <summary>[EXT:online-coop-lockstep] Raised when a session's rights refuse a command of this machine (<see cref="SessionRules"/>), which leaves no trace in the log.</summary>
	public event Action<ParkCommand>? SessionRefused;

	/// <summary>Whether a command is being executed now (an edit inside an edit runs directly, unlogged, as part of the outer command).</summary>
	public bool IsExecuting => depth > 0;

	/// <summary>
	/// Whether a command this machine's player issued runs now (<see cref="Issue"/>), not a scheduled one (a replay, or a peer's in a session): the issuer's tools may follow what it does, through
	/// <see cref="AfterIssue"/>, and nobody else's may.
	/// </summary>
	public bool IsIssuing => issuing;

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
		// [EXT:online-coop-commands] a command applied while a simulation tick runs would apply inside tick N here but at the start of tick N in a replay or on a peer (its log entry says
		// "before tick N"), so no code path may apply one there: the held commands apply before the tick's scope begins. The tests and a strict run throw to find one; a game of one runs
		// it at once, as it always did, and a co-op session drops it (InTick)
		if ( inTick )
		{
			if ( Strict )
				throw new InvalidOperationException( $"The {command.Kind} command was applied during a simulation tick." );
			if ( InTick == ParkCommandInTickRule.Drop )
			{
				Common.GlobalNamespace.Log?.Error( $"The {command.Kind} command was applied during a simulation tick and dropped." );
				return ParkCommandResult.Rejected( $"The {command.Kind} command was applied during a simulation tick." );
			}
		}
		// a malformed command never reaches the park and has no byte form to log: it is refused before the log
		if ( !command.IsWellFormed )
			return ParkCommandResult.Rejected( $"The {command.Kind} command has fields out of range." );
		// [EXT:online-coop-lockstep] in a session a command of this machine the rights refuse leaves no trace: a guest's own issues stay out of its log, which is the host's stream. A held
		// command (the host's stream) is refused as usual and logged, so the stream sees that it was refused here. A guest's park takes none of its own, granted or not (C3)
		if ( SessionRules && !applyingDue && !applyingRemote && command.Player >= 0 && (FollowsStream || Refuses( command )) )
		{
			SessionRefused?.Invoke( command );
			return ParkCommandResult.Rejected( $"Player {command.Player} may not {command.Kind}." ) with { RightsRefused = Refuses( command ) };
		}
		var tick = nextTick();
		ParkCommandResult result;
		ExceptionDispatchInfo? thrown = null;
		try
		{
			result = Run( command );
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			// [EXT:online-coop-commands] a command that throws (a rule its own checks missed) is a rejection with what it wrote before the throw: it is logged like any other, so a replay or a
			// peer, which throws the same way at the same place, stays in step instead of losing the command; the tests and a strict run rethrow it once it is logged
			Common.GlobalNamespace.Log?.Error( $"The {command.Kind} command threw: {error}" );
			result = new ParkCommandResult( false, $"The {command.Kind} command failed: {error.GetType().Name}: {error.Message}" ) { Error = error };
			// a held command in a session never rethrows, nor a guest's on the host: its error is in its result, which the stream compares with the host's
			if ( Strict && !(SessionRules && (applyingDue || applyingRemote)) )
				thrown = ExceptionDispatchInfo.Capture( error );
		}
		Record( new ParkCommandLogEntry( tick, command ), result );
		thrown?.Throw();
		return result;
	}

	/// <summary>
	/// [EXT:online-coop-commands] Applies a guest's command on the host now, between two ticks (docs/COOP.md, "Building by every player"): its player is the guest's seat, which the
	/// host set from the server's word of the sender. Unlike this machine's own command it is refused by the rights as a held one is, logged and raised as refused, so the stream
	/// carries the refusal to every peer and its issuer; its error never rethrows (a strict run included).
	/// </summary>
	public ParkCommandResult ApplyRemote( ParkCommand command )
	{
		ArgumentNullException.ThrowIfNull( command );
		if ( depth > 0 || applyingRemote )
			throw new InvalidOperationException( $"A guest's {command.Kind} command was applied inside another command." );
		applyingRemote = true;
		try
		{
			return Apply( command );
		}
		finally
		{
			applyingRemote = false;
		}
	}

	/// <summary>
	/// Issues <paramref name="command"/> as the local player (<see cref="Player"/>) and applies it now. A command issued while another runs (a tool callback inside an edit) is part of the outer one: it
	/// takes the outer command's player, so it does the same on every peer, and runs unlogged.
	/// </summary>
	public ParkCommandResult Issue( ParkCommand command )
	{
		ArgumentNullException.ThrowIfNull( command );
		if ( depth > 0 )
		{
			// [EXT:online-coop-lockstep] a HUD callback inside a command would run here as part of it on its issuer only, and so on no peer: a session refuses it everywhere (the issuer's
			// follow-ups go through AfterIssue, as commands of their own)
			if ( SessionRules )
			{
				if ( Strict )
					throw new InvalidOperationException( $"The {command.Kind} command was issued inside the {running!.Kind} command." );
				return ParkCommandResult.Rejected( $"The {command.Kind} command was issued inside another command." );
			}
			return Apply( command with { Player = running!.Player } );
		}
		var outer = !issuing;
		issuing = true;
		try
		{
			// [EXT:online-coop-commands] a guest's command goes to the host as a request, and changes nothing here until its record comes back
			if ( FollowsStream && Submit is { } submit )
				return Request( command with { Player = Player }, submit );
			return Apply( command with { Player = Player } );
		}
		finally
		{
			if ( outer )
			{
				issuing = false;
				RunAfterIssue();
			}
		}
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's command as a request to the host, after the checks <see cref="Apply"/> makes before it runs one, in the same order: never inside a tick, well formed,
	/// and allowed by the session's rights here (the guest's own park's grants: a request the host would refuse is not sent). The host's rights decide again on every peer.
	/// </summary>
	private ParkCommandResult Request( ParkCommand command, Func<ParkCommand, CoopGuestCommands.Request?> submit )
	{
		if ( inTick )
		{
			if ( Strict )
				throw new InvalidOperationException( $"The {command.Kind} command was issued during a simulation tick." );
			Common.GlobalNamespace.Log?.Error( $"The {command.Kind} command was issued during a simulation tick and dropped." );
			return ParkCommandResult.Rejected( $"The {command.Kind} command was issued during a simulation tick." );
		}
		if ( !command.IsWellFormed )
			return ParkCommandResult.Rejected( $"The {command.Kind} command has fields out of range." );
		if ( command.Player < 0 || Refuses( command ) )
		{
			SessionRefused?.Invoke( command );
			return ParkCommandResult.Rejected( $"Player {command.Player} may not {command.Kind}." ) with { RightsRefused = true };
		}
		return submit( command ) is { } request ? ParkCommandResult.Sent( request ) : ParkCommandResult.Rejected( "The shared park takes no requests now." );
	}

	/// <summary>
	/// [EXT:online-coop-commands] Runs <paramref name="action"/> once the command this machine's player issued has returned (at once outside one): a HUD that follows its own edit, such as the tool a
	/// placed ride opens, acts after the command, so the commands it issues are logged on their own instead of hiding inside the placement.
	/// </summary>
	public void AfterIssue( Action action )
	{
		ArgumentNullException.ThrowIfNull( action );
		if ( issuing )
			afterIssue.Add( action );
		else
			action();
	}

	private void RunAfterIssue()
	{
		if ( afterIssue.Count == 0 )
			return;
		var actions = afterIssue.ToArray();
		afterIssue.Clear();
		foreach ( var action in actions )
			action();
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

	/// <summary>
	/// Applies, in the order they were scheduled, the held commands of every tick up to <paramref name="tick"/> (the tick about to run). A strict run's exception is rethrown once
	/// every one of them has applied, so a command that throws never drops the rest of its tick.
	/// </summary>
	public void ApplyDue( long tick )
	{
		ExceptionDispatchInfo? thrown = null;
		var outer = !applyingDue;
		applyingDue = true;
		try
		{
			while ( scheduled.Count > 0 )
			{
				var first = scheduled.First();
				if ( first.Key > tick )
					break;
				scheduled.Remove( first.Key );
				foreach ( var command in first.Value )
				{
					try
					{
						Apply( command );
					}
					catch ( Exception error ) when ( error is not OutOfMemoryException )
					{
						thrown ??= ExceptionDispatchInfo.Capture( error );
					}
				}
			}
		}
		finally
		{
			if ( outer )
				applyingDue = false;
		}
		thrown?.Throw();
	}

	/// <summary>
	/// Marks a simulation tick running, from after its held commands applied (<see cref="ApplyDue"/>) to the end of the returned scope, which restores the mark it found: no command
	/// may be applied then (<see cref="Strict"/>).
	/// </summary>
	internal TickScope EnterTick()
	{
		var scope = new TickScope( this, inTick );
		inTick = true;
		return scope;
	}

	/// <summary>Whether a simulation tick is running (<see cref="EnterTick"/>).</summary>
	public bool IsInTick => inTick;

	/// <summary>
	/// [EXT:online-coop-commands] The checks of the tests and a strict developer run (<c>OPENTPW_STRICT_COMMANDS=1</c>): a command applied while a simulation tick runs throws
	/// (<see cref="EnterTick"/>), and a command that throws is rethrown once it is logged, so neither goes unnoticed. Off, a game of one runs a command inside a tick at once, as it always
	/// did, and a command that throws is a logged rejection.
	/// </summary>
	public static bool Strict { get; set; }

	/// <summary>The scope of <see cref="EnterTick"/>: its end restores the tick mark it found, so a nested or a late end never clears an outer tick's.</summary>
	internal readonly struct TickScope : IDisposable
	{
		private readonly ParkCommands owner;
		private readonly bool previous;

		public TickScope( ParkCommands owner, bool previous )
		{
			this.owner = owner;
			this.previous = previous;
		}

		public void Dispose() => owner.inTick = previous;
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
		// the rights are asked of the outermost command only: what runs inside it is that command's, which they allowed
		if ( depth == 0 && Refuses( command ) )
			return ParkCommandResult.Rejected( $"Player {command.Player} may not {command.Kind}." ) with { RightsRefused = true };
		if ( depth == 0 )
			running = command;
		depth++;
		try
		{
			return execute( command );
		}
		finally
		{
			depth--;
			if ( depth == 0 )
				running = null;
		}
	}

	/// <summary>
	/// Logs a command and raises <see cref="Applied"/> for it: every subscriber runs, each in its own catch, so one that throws never keeps the others (a session's stream among
	/// them) from the command; a strict run rethrows the first throw once all have run.
	/// </summary>
	private void Record( ParkCommandLogEntry entry, ParkCommandResult result )
	{
		if ( log.Add( entry ) )
			DroppedLog++;
		if ( Applied is not { } applied )
			return;
		ExceptionDispatchInfo? thrown = null;
		foreach ( var subscriber in applied.GetInvocationList() )
		{
			try
			{
				((Action<ParkCommandLogEntry, ParkCommandResult>)subscriber)( entry, result );
			}
			catch ( Exception error ) when ( error is not OutOfMemoryException )
			{
				Common.GlobalNamespace.Log?.Error( $"A subscriber of the {entry.Command.Kind} command threw: {error}" );
				if ( Strict )
					thrown ??= ExceptionDispatchInfo.Capture( error );
			}
		}
		thrown?.Throw();
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
