namespace OpenTPW;

/// <summary>
/// The kinds of cheat action a park applies (docs/CHEATS.md, "Behaviour"). A batch of queued actions is applied in this order at the next park turn, whatever the order the
/// player asked for them in. Saved by name.
/// </summary>
public enum CheatKind
{
	/// <summary>[BIN:TPW-EXE:0x4053C0] Cheat mode on: the secret park name of the rename dialog (<c>0x4993E0</c>). The argument is unused (0).</summary>
	Mode = 0,
	/// <summary>[BIN:TPW-EXE:0x40C450] Alt+1: the bank credits <see cref="ParkCheats.MoneyAmount"/> as a prize (<c>0x4D0190(10000)</c>). The argument is unused (0).</summary>
	Money = 1,
	/// <summary>[BIN:TPW-EXE:0x40C430] Alt+R: every research item's object becomes available to build (<c>0x503FF0</c>); research itself is not touched. The argument is unused (0).</summary>
	Research = 2,
	/// <summary>
	/// [BIN:TPW-EXE:0x40C6C0] Alt+G and Alt+Num1..9: a golden ticket, silently. The argument is the <see cref="GoldenTicketKind"/> (0..9: the six local tickets through <c>0x5AF810</c>, the four global
	/// ones with record 100 through <c>0x5AF940</c>); the two secret tickets have no cheat.
	/// </summary>
	Ticket = 3,
	/// <summary>[EXT:cheats-weather] The weather now, as a forecast with no warning: the argument is the <see cref="CheatWeather"/>.</summary>
	Weather = 4,
	/// <summary>
	/// [EXT:cheats-ride-vehicles] A ride's vehicle limit raised: 32 bumper cars or karts, 40 tour cars. The argument is the ride's attraction id; the ride keeps the raised limit, and its
	/// capacity maximum grows to fill it.
	/// </summary>
	RideVehicles = 5,
	/// <summary>[EXT:cheats-arrival] A group of visitors arrives now by the vehicle the argument names (<see cref="ArrivalVehicleKind"/>: bus, seaplane or Ferry), sized for it.</summary>
	Arrival = 6,
}

/// <summary>[EXT:cheats-weather] The weathers the cheats window offers in every theme (snow too: it is a cheat).</summary>
public enum CheatWeather
{
	Clear = 0,
	Rain = 1,
	Thunderstorm = 2,
	Snow = 3,
}

/// <summary>
/// What the park's cheats need from the level that the economy does not hold: the arrivals and the weather (<see cref="ParkEconomy.CheatHost"/>). Each call is made at the start of a park turn
/// and takes no random draw; false means "not now", and the cheat waits for the next turn.
/// </summary>
public interface IParkCheatHost
{
	/// <summary>[EXT:cheats-arrival] Starts a group brought by <paramref name="vehicleKind"/> now; false while a group is being delivered or a vehicle is at work.</summary>
	bool TryStartArrival( int vehicleKind );
	/// <summary>[EXT:cheats-weather] Makes the weather <paramref name="weather"/> now (at park tick <paramref name="tick"/>); false while the park's weather has not taken its first step.</summary>
	bool TryApplyWeather( CheatWeather weather, long tick );
	/// <summary>[EXT:cheats-ride-vehicles] Whether the ride <paramref name="attractionId"/> is placed, live and of a kind whose vehicle limit the cheat raises, and not raised yet.</summary>
	bool CanRaiseRideVehicles( int attractionId );
	/// <summary>[EXT:cheats-ride-vehicles] Raises the ride's vehicle limit now; false when the ride is gone or cannot be raised (the cheat is then recorded as asked, with no effect).</summary>
	bool TryRaiseRideVehicles( int attractionId );
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
// [EXT:cheats-park-mode] the original's cheat object sits behind three first-use guards (0xFB3519 save and load, 0xFB34C4 rename, 0xFB34C8 key paths), and each guard's first use in a process re-runs its constructor 0x4053B0, clearing the mode once per run (the first key that reaches the cheat check after a load or a first rename); that depends on the process's history, not on the park, so OpenTPW keeps the mode with the park and does not reproduce it
// [EXT:cheats-turn-boundary] the original applies a cheat inside the key handler or the rename dialog, between two frames; OpenTPW queues it and applies it at the next park turn, so it lands at the same simulation point in every run and the record names that turn
public sealed class ParkCheats
{
	/// <summary>The money cheat's credit (<c>push 0x2710</c> at <c>0x40C456</c>).</summary>
	public const int MoneyAmount = 10000;
	/// <summary>The record a global ticket cheat claims (<c>push 0x64</c> at <c>0x40C780..0x40C7E0</c>): it takes the ticket from a park that holds it with a lower record.</summary>
	public const int TicketRecord = 100;

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
		CheatKind.Mode or CheatKind.Money or CheatKind.Research or CheatKind.Ticket => false,
		_ => true
	};

	/// <summary>[EXT:cheats-arrival] Whether an arrival cheat is waiting: at most one may (a second request is refused until the first group has come).</summary>
	public bool ArrivalPending => pending.Any( action => action.Kind == CheatKind.Arrival );

	/// <summary>Whether <paramref name="action"/>'s argument is one its kind accepts.</summary>
	public static bool IsValid( CheatAction action ) => action.Kind switch
	{
		CheatKind.Mode or CheatKind.Money or CheatKind.Research => action.Argument == 0,
		CheatKind.Ticket => action.Argument is >= (int)GoldenTicketKind.Visitors and <= (int)GoldenTicketKind.BigPark,
		CheatKind.Weather => Enum.IsDefined( (CheatWeather)action.Argument ),
		CheatKind.Arrival => action.Argument is ArrivalVehicleKind.Bus or ArrivalVehicleKind.Seaplane or ArrivalVehicleKind.Ferry,
		CheatKind.RideVehicles => action.Argument > 0,
		_ => false
	};

	/// <summary>
	/// Queues <paramref name="action"/> for the next park turn. False when the action is not valid or <see cref="MaximumPending"/> actions are already waiting; the caller decides whether the
	/// park may cheat at all (a Theme Park World park that is not a visit, <see cref="ParkEconomy.QueueCheat"/>).
	/// </summary>
	internal bool Queue( CheatAction action )
	{
		if ( !IsValid( action ) || pending.Count >= MaximumPending || action.Kind == CheatKind.Arrival && ArrivalPending || action.Kind == CheatKind.RideVehicles && pending.Contains( action ) )
			return false;
		pending.Add( action );
		return true;
	}

	/// <summary>
	/// Applies the queued actions at the start of park turn <paramref name="turn"/>, in the order of <see cref="CheatKind"/> (the queue order within one kind): each marks the park, does
	/// what its kind does and joins the record. An action the park cannot take now (an arrival while a vehicle is at work) stays queued for the next turn, unrecorded.
	/// </summary>
	internal void ApplyPending( ParkEconomy economy, long turn )
	{
		if ( pending.Count == 0 )
			return;
		var batch = pending.OrderBy( action => (int)action.Kind ).ToArray();
		pending.Clear();
		foreach ( var action in batch )
		{
			if ( economy.ApplyCheat( action ) is not { } outcome )
			{
				pending.Add( action );
				continue;
			}
			Mode = true;
			if ( IsExtensionKind( action.Kind ) )
				Extension = true;
			var entry = new CheatLogEntry( turn, action.Kind, action.Argument );
			log.Add( entry );
			if ( log.Count > MaximumLog )
			{
				log.RemoveAt( 0 );
				DroppedLog++;
			}
			Applied?.Invoke( entry );
			economy.RaiseCheatApplied( action, outcome );
		}
	}

	/// <summary>
	/// An original save's cheat mode (the <c>CHTS</c> chunk's first byte, <see cref="OriginalEconomyImport"/>): a park saved while the mode was on is marked, with an empty record, as loading the
	/// save sets the original's mode. Off leaves the record as it is.
	/// </summary>
	// [BIN:TPW-EXE:0x405570 cheat load] the loader reads the mode byte +0xA and the byte +0xB straight from the chunk; 0x405560 tests +0xA as a whole byte, so any nonzero value is on
	internal void ImportMode( bool on )
	{
		if ( on )
			Mode = true;
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
		// entries are dropped only from a full log, so a record that dropped any holds the most it may
		if ( data.Log.Count > MaximumLog || data.DroppedLog < 0 || data.DroppedLog > 0 && data.Log.Count != MaximumLog )
			return $"the cheat record holds {data.Log.Count} entries and {data.DroppedLog} dropped ones";
		if ( data.Pending != null && (data.Pending.Count is 0 or > MaximumPending || data.Pending.Any( action => action == null || !Enum.IsDefined( action.Kind ) || !IsValid( new CheatAction( action.Kind, action.Argument ) ) )
			|| data.Pending.Count( action => action.Kind == CheatKind.Arrival ) > 1) )
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

/// <summary>
/// The original's secret park name (docs/CHEATS.md): renaming the park in the ticket price window to "Kevin the" / "Cosmic Cow" turns the cheat mode on. The two lines are compared, case-sensitively, in the
/// obfuscated form the executable stores, after the same transform the rename dialog's OK handler applies to its edit buffers.
/// </summary>
// [BIN:TPW-EXE:0x4993E0 secret park name] after storing the lines (0x4E2950/0x4E2A50) and copying them to the player record (0x5B0940/0x5B0970), the handler XORs each UTF-16 unit of both 14-character buffers (0x7C2668, 0x7C2688) with its index, re-reading wcslen (0x67ACD0) after every unit, and compares them with wcscmp (0x67C290) against [0x751A34] = "Kdtjj%rom" and [0x751A38] = "Cnqnmf&Dg~"; both equal calls 0x4053C0(1) on 0xFB3520
public static class SecretParkName
{
	/// <summary>The stored form of line 1 (<c>0x751A54</c>), "Kevin the" after the transform.</summary>
	public const string Line1Stored = "Kdtjj%rom";
	/// <summary>The stored form of line 2 (<c>0x751A3C</c>), "Cosmic Cow" after the transform.</summary>
	public const string Line2Stored = "Cnqnmf&Dg~";

	/// <summary>
	/// The handler's transform of one buffer: unit <c>i</c> becomes <c>unit ^ i</c> while <c>i</c> is below the string's length, which is read again after every unit, so a unit that becomes 0 ends
	/// the string there.
	/// </summary>
	public static string Transform( string line )
	{
		ArgumentNullException.ThrowIfNull( line );
		var units = line.ToCharArray();
		int Length()
		{
			var end = Array.IndexOf( units, '\0' );
			return end < 0 ? units.Length : end;
		}
		for ( var index = 0; index < Length(); index++ )
			units[index] = (char)(units[index] ^ index);
		return new string( units, 0, Length() );
	}

	/// <summary>Whether the two sign lines are the secret name (exact, case-sensitive).</summary>
	public static bool Matches( string line1, string line2 ) =>
		string.Equals( Transform( line1 ), Line1Stored, StringComparison.Ordinal ) && string.Equals( Transform( line2 ), Line2Stored, StringComparison.Ordinal );
}
