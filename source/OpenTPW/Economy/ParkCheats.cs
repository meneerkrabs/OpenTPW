namespace OpenTPW;

/// <summary>
/// The kinds of cheat action a park applies (docs/CHEATS.md, "Behaviour"). A batch of queued actions is applied in this order at the next park turn, whatever the order the
/// player asked for them in. Saved by name.
/// </summary>
public enum CheatKind
{
	/// <summary>[BIN:TPW-EXE:0x4053C0] Cheat mode on: the secret park name of the rename dialog (<c>0x4993E0</c>). The argument is unused (0).</summary>
	Mode = 0,
}

/// <summary>One cheat action as the player asked for it: its kind and its argument (for example a ticket kind).</summary>
public readonly record struct CheatAction( CheatKind Kind, int Argument = 0 );

/// <summary>One applied cheat action of a park's record: the park turn it was applied at, its kind and its argument.</summary>
public readonly record struct CheatLogEntry( long Turn, CheatKind Kind, int Argument );

/// <summary>
/// A park's cheats (docs/CHEATS.md): the original's cheat mode, which marks the park for good, the record of every cheat applied and the actions waiting for the next park turn.
/// <para>
/// The mode is the original's per-park byte (<c>+0xA</c> of the cheat object <c>0xFB3520</c>, saved in the <c>CHTS</c> chunk by <c>0x4055D0</c>); nothing in the original ever turns
/// it off, and nothing here does. Every action the player asks for is queued and applied by the simulation at the start of the next park turn, in the order of
/// <see cref="CheatKind"/>, with no draw from any random stream, so a run with the same actions at the same turns is the same run. A park that never cheated has an empty
/// record, which the park save leaves out: its bytes and its hash are those of a park from before cheats existed.
/// </para>
/// </summary>
// [BIN:TPW-EXE:0x4053C0 cheat mode] the mode byte +0xA is set by 0x4053C0(1) and never cleared (no call of 0x4053C0(0) exists); 0x4053C0 also posts a message of type 0x15 (0x40FB10), whose listeners are not traced and which OpenTPW does not post
// [EXT:cheats-turn-boundary] the original applies a cheat inside the key handler or the rename dialog, between two frames; OpenTPW queues it and applies it at the next park turn, so it lands at the same simulation point in every run and the record names that turn
public sealed class ParkCheats
{
	/// <summary>The most entries the record keeps; older entries are dropped (and counted in <see cref="DroppedLog"/>) beyond it.</summary>
	public const int MaximumLog = 4096;
	/// <summary>The most actions that may wait for the next park turn; <see cref="Queue"/> refuses more.</summary>
	public const int MaximumPending = 16;

	private readonly List<CheatLogEntry> log = new();
	private readonly List<CheatAction> pending = new();

	/// <summary>The original's cheat mode (<c>+0xA</c>): on once the park has cheated, never off again. It opens the cheat keys and is the park's "cheated" mark.</summary>
	public bool Mode { get; private set; }

	/// <summary>
	/// [EXT:cheats-save] Whether the park has used one of the <c>opentpw.cheats</c> extension's own cheats (<see cref="IsExtensionKind"/>); sticky like <see cref="Mode"/>. Such a park's save records the
	/// extension and needs it to load (<c>ParkResumeMods.CheatsInUse</c>); a park cheated only through the original's cheats does not.
	/// </summary>
	public bool Extension { get; private set; }

	/// <summary>The applied cheats, oldest first (at most <see cref="MaximumLog"/>).</summary>
	public IReadOnlyList<CheatLogEntry> Log => log;

	/// <summary>How many of the oldest entries the record dropped to stay within <see cref="MaximumLog"/>.</summary>
	public long DroppedLog { get; private set; }

	/// <summary>The actions waiting for the next park turn, in the order they were queued.</summary>
	public IReadOnlyList<CheatAction> Pending => pending;

	/// <summary>
	/// [EXT:cheats-save] Whether the park's save must record the <c>opentpw.cheats</c> extension: it has used one of the extension's own cheats (<see cref="Extension"/>) or one is waiting for the next turn.
	/// </summary>
	public bool UsesExtension => Extension || pending.Any( action => IsExtensionKind( action.Kind ) );

	/// <summary>True for a park that never cheated and has nothing queued: the park save leaves the record out.</summary>
	public bool IsEmpty => !Mode && !Extension && log.Count == 0 && pending.Count == 0 && DroppedLog == 0;

	/// <summary>[EXT:cheats-publish] Whether the park is marked as cheated (shown in the load menu and on a shared park; it does not stop publishing).</summary>
	public bool Cheated => Mode;

	/// <summary>Raised for every action as it is applied, with its record entry (the HUD's on-screen note).</summary>
	public event Action<CheatLogEntry>? Applied;

	/// <summary>Whether <paramref name="kind"/> is one of the extension's own cheats (<c>[EXT:cheats-*]</c>) rather than one the original game has.</summary>
	public static bool IsExtensionKind( CheatKind kind ) => kind switch
	{
		CheatKind.Mode => false,
		_ => true
	};

	/// <summary>Whether <paramref name="action"/>'s argument is one its kind accepts.</summary>
	public static bool IsValid( CheatAction action ) => action.Kind switch
	{
		CheatKind.Mode => action.Argument == 0,
		_ => false
	};

	/// <summary>
	/// Queues <paramref name="action"/> for the next park turn. False when the action is not valid or <see cref="MaximumPending"/> actions are already waiting; the caller decides whether the
	/// park may cheat at all (a Theme Park World park that is not a visit, <see cref="ParkEconomy.QueueCheat"/>).
	/// </summary>
	internal bool Queue( CheatAction action )
	{
		if ( !IsValid( action ) || pending.Count >= MaximumPending )
			return false;
		pending.Add( action );
		return true;
	}

	/// <summary>
	/// Applies the queued actions at the start of park turn <paramref name="turn"/>, in the order of <see cref="CheatKind"/> (the queue order within one kind): each marks the park, does
	/// what its kind does and joins the record.
	/// </summary>
	internal void ApplyPending( ParkEconomy economy, long turn )
	{
		if ( pending.Count == 0 )
			return;
		var batch = pending.OrderBy( action => (int)action.Kind ).ToArray();
		pending.Clear();
		foreach ( var action in batch )
		{
			Mode = true;
			if ( IsExtensionKind( action.Kind ) )
				Extension = true;
			switch ( action.Kind )
			{
				case CheatKind.Mode:
					// [BIN:TPW-EXE:0x4053C0 cheat mode] the mode byte only; the message of type 0x15 is not posted
					break;
			}
			var entry = new CheatLogEntry( turn, action.Kind, action.Argument );
			log.Add( entry );
			if ( log.Count > MaximumLog )
			{
				log.RemoveAt( 0 );
				DroppedLog++;
			}
			Applied?.Invoke( entry );
		}
	}

	/// <summary>The record as the park save holds it, or null for a park that never cheated (<see cref="IsEmpty"/>), whose save leaves it out.</summary>
	internal ParkSaveFile.CheatsData? Capture() => IsEmpty ? null : new ParkSaveFile.CheatsData
	{
		Mode = Mode,
		Extension = Extension,
		Log = log.Select( entry => new ParkSaveFile.CheatLogData { Turn = entry.Turn, Kind = entry.Kind, Argument = entry.Argument } ).ToList(),
		DroppedLog = DroppedLog,
		Pending = pending.Count == 0 ? null : pending.Select( action => new ParkSaveFile.CheatActionData { Kind = action.Kind, Argument = action.Argument } ).ToList()
	};

	/// <summary>Restores a saved record (validated by <see cref="Problem"/>); null leaves the park uncheated.</summary>
	internal void Restore( ParkSaveFile.CheatsData? data )
	{
		log.Clear();
		pending.Clear();
		Mode = data?.Mode ?? false;
		Extension = data?.Extension ?? false;
		DroppedLog = data?.DroppedLog ?? 0;
		if ( data == null )
			return;
		log.AddRange( data.Log.Select( entry => new CheatLogEntry( entry.Turn, entry.Kind, entry.Argument ) ) );
		if ( data.Pending != null )
			pending.AddRange( data.Pending.Select( action => new CheatAction( action.Kind, action.Argument ) ) );
	}

	/// <summary>What is wrong with a saved record at park turn <paramref name="turn"/>, or null when it is consistent.</summary>
	internal static string? Problem( ParkSaveFile.CheatsData data, long turn )
	{
		if ( data.Log == null )
			return "the cheat record has no log";
		if ( data.Log.Count > MaximumLog || data.DroppedLog < 0 )
			return $"the cheat record holds {data.Log.Count} entries and {data.DroppedLog} dropped ones";
		if ( data.Pending != null && (data.Pending.Count is 0 or > MaximumPending || data.Pending.Any( action => action == null || !Enum.IsDefined( action.Kind ) || !IsValid( new CheatAction( action.Kind, action.Argument ) ) )) )
			return "the cheat record's pending actions are empty, too many or not valid";
		var previous = 0L;
		foreach ( var entry in data.Log )
		{
			if ( entry == null || !Enum.IsDefined( entry.Kind ) || !IsValid( new CheatAction( entry.Kind, entry.Argument ) ) )
				return "the cheat record holds an entry that is not a valid cheat";
			if ( entry.Turn < previous || entry.Turn > turn )
				return $"the cheat record's turn {entry.Turn} is out of order or after the park turn {turn}";
			previous = entry.Turn;
		}
		if ( (data.Log.Count > 0 || data.DroppedLog > 0) && !data.Mode )
			return "the cheat record holds applied cheats but the cheat mode is off";
		if ( data.Extension && !data.Mode )
			return "the cheat record used the extension's cheats but the cheat mode is off";
		if ( data.Log.Any( entry => IsExtensionKind( entry.Kind ) ) && !data.Extension )
			return "the cheat record holds an extension cheat but does not say so";
		if ( !data.Mode && !data.Extension && data.Log.Count == 0 && data.DroppedLog == 0 && data.Pending == null )
			return "the cheat record is empty (a park that never cheated has none)";
		return null;
	}
}
