namespace OpenTPW;

/// <summary>
/// One of the park analyser's history rings (tp.exe's <c>CHistory</c>): a fixed number of entries, the newest at the head, which wraps round once full.
/// Entries are stored as doubles so the happiness ring (singles) and the count rings (integers) share one type.
/// </summary>
// [BIN:TPW-EXE:0x4C58C0 analyser constructor] the history rings start with head -1 (+0x244 of the ring), their size (+0x248: 0x90 for the sixteen monthly rings, 0x32 for the stay ring) and the wrapped flag 0 (+0x24C)
// [BIN:TPW-EXE:0x4C7720 month handler] a month step moves the head on (back to 0 at the size, setting the wrapped flag) and stores the value there; a read k takes the entry head - k, wrapping once the ring has wrapped (0x4801D0), and fails past the entries held
public sealed class HistoryRing
{
	private readonly double[] data;

	public HistoryRing( int size )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( size, 1 );
		data = new double[size];
	}

	/// <summary>The number of slots.</summary>
	public int Size => data.Length;
	/// <summary>The newest entry's slot, -1 before the first entry.</summary>
	public int Head { get; private set; } = -1;
	/// <summary>Whether the ring has gone round once.</summary>
	public bool Wrapped { get; private set; }

	/// <summary>The entries the ring holds: its size once wrapped, else the head plus one.</summary>
	// [BIN:TPW-EXE:0x480460 sample count] the graph's reader takes the size once the wrapped flag is set, else the head + 1, as the number of samples it can show
	public int Count => Wrapped ? data.Length : Head + 1;

	/// <summary>Stores <paramref name="value"/> as the newest entry.</summary>
	public void Add( double value )
	{
		Head++;
		if ( Head >= data.Length )
		{
			Head = 0;
			Wrapped = true;
		}
		data[Head] = value;
	}

	/// <summary>The entry <paramref name="back"/> steps before the newest (0: the newest); false past the entries the ring holds.</summary>
	public bool TryGet( int back, out double value )
	{
		value = 0;
		if ( back < 0 || back >= data.Length )
			return false;
		if ( back <= Head )
		{
			value = data[Head - back];
			return true;
		}
		if ( !Wrapped )
			return false;
		value = data[Head - back + data.Length];
		return true;
	}

	/// <summary>The entries oldest first.</summary>
	public IEnumerable<double> Oldest()
	{
		for ( var back = Count - 1; back >= 0; back-- )
			if ( TryGet( back, out var value ) )
				yield return value;
	}

	/// <summary>A ring holding <paramref name="oldestFirst"/> (the last <see cref="Size"/> of them when there are more).</summary>
	public static HistoryRing Of( int size, IEnumerable<double> oldestFirst )
	{
		var ring = new HistoryRing( size );
		foreach ( var value in oldestFirst )
			ring.Add( value );
		return ring;
	}

	/// <summary>
	/// A ring with exactly the saved <paramref name="slots"/>, <paramref name="head"/> and <paramref name="wrapped"/> flag (already validated by
	/// <see cref="RingData.IsValid"/>), so a ring that has gone round keeps its head and flag and saves back to the same data.
	/// </summary>
	internal static HistoryRing Restore( IReadOnlyList<double> slots, int head, bool wrapped )
	{
		var ring = new HistoryRing( slots.Count ) { Head = head, Wrapped = wrapped };
		for ( var slot = 0; slot < slots.Count; slot++ )
			ring.data[slot] = slots[slot];
		return ring;
	}

	/// <summary>
	/// The sum and the number of entries of the newest <paramref name="count"/> (cut to the entries held), the way <c>0x4CEF30</c> adds them.
	/// </summary>
	// [BIN:TPW-EXE:0x4CEF30 history sum] the newest n entries added as signed integers into a double, n cut to the entries the ring holds; the caller divides by the count it gets back
	public (double Sum, int Count) SumNewest( int count )
	{
		var n = Math.Min( count, Count );
		var sum = 0d;
		for ( var back = 0; back < n; back++ )
			if ( TryGet( back, out var value ) )
				sum += (int)value;
		return (sum, n);
	}
}

/// <summary>
/// The park analyser's monthly statistics that the park information window graphs: the guests dropped off by the bus, the mean happiness, the mean stay,
/// the guests in the park and the park rating. Nothing here is read by the simulation.
/// </summary>
// [APPROX:UI-914] TPW and independently traced TPI statistics are imported; TPI adapts partial inputs across a fresh host clock, not native saved-clock continuation. Real populated/wrapped-save corpus coverage and other park-information presentation residuals remain open.
public sealed class ParkStatisticsHistory
{
	/// <summary>The entries of a monthly ring (0x90).</summary>
	// [BIN:TPW-EXE:0x4C58C0 analyser constructor] sixteen rings of 0x90 entries, one of 0x32 and one of 5
	public const int MonthsKept = 0x90;
	/// <summary>The entries of the stay ring (0x32).</summary>
	public const int StayWindow = 0x32;
	/// <summary>The samples a graph period shows: 1, 3 and 12 years of months.</summary>
	// [BIN:TPW-EXE:0x480460 period] the period index 0, 1 and 2 map to 0xC, 0x24 and 0x90 samples; any other index to 0
	public static int PeriodSamples( int period ) => period switch { 0 => 12, 1 => 36, 2 => 144, _ => 0 };

	/// <summary>The guests the bus has dropped off since the month closed (<c>+0x20CC0</c>).</summary>
	// [BIN:TPW-EXE:0x4CF3E0 arrival update] each guest the vehicle's group drops off adds one to the analyser's running arrivals (+0x20CC0) after the guest is created and the group counts down
	public int ArrivalsThisMonth { get; private set; }
	/// <summary>The arrivals of each closed month (ring at <c>+0x20CC4</c>).</summary>
	public HistoryRing Arrivals { get; private set; } = new( MonthsKept );
	/// <summary>The mean happiness of each closed month (single ring at <c>+0x20F14</c>).</summary>
	public HistoryRing Happiness { get; private set; } = new( MonthsKept );
	/// <summary>The mean stay in days of the last guests, as each month closed (ring at <c>+0x21164</c>).</summary>
	public HistoryRing Stay { get; private set; } = new( MonthsKept );
	/// <summary>The guests in the park as each month closed (ring at <c>+0x205D4</c>).</summary>
	public HistoryRing People { get; private set; } = new( MonthsKept );
	/// <summary>The park rating at each month close (ring at <c>+0x213B4</c>), independent of financial history.</summary>
	public HistoryRing Rating { get; private set; } = new( MonthsKept );
	/// <summary>The stay in days of the last 50 guests that left (ring at <c>+0x21604</c>).</summary>
	public HistoryRing RecentStays { get; private set; } = new( StayWindow );

	/// <summary>Counts a guest the bus dropped off.</summary>
	public void RecordArrival() => ArrivalsThisMonth++;

	/// <summary>Stores the stay of a guest that reaches the stop to leave (the stay ring, <c>0x4C7600</c>).</summary>
	// [BIN:TPW-EXE:0x4C7600 stay ring] the guest's turns in the park, in whole four-turn steps, become days (x * 15000 / 4 seconds over 86400, integer) and are stored in a 50-entry ring; the caller 0x502357 (guest state 21) stores it only for a guest whose admission was paid (+0x204)
	public void RecordStay( int days ) => RecentStays.Add( days );

	/// <summary>The days a guest has stayed after <paramref name="turns"/> park turns since its arrival stamp.</summary>
	public static int StayDays( long turns ) => (int)(ParkCalendar.SecondsPerTurn * ((turns >> 2) << 2) / ParkCalendar.SecondsPerDay);

	/// <summary>
	/// The month step: the running arrivals, happiness, mean stay, guests in the park and rating each go into their ring; arrivals start again at 0.
	/// </summary>
	// [BIN:TPW-EXE:0x4C7720 month sample] arrivals (+0x20CC0) into its ring and cleared; the happiness (0x4C7BB0, a single) and the census (0x4C7FA0, age 0) into their rings; the stay ring's mean over its newest 50 entries (0x4CEF30 over the count it returns, truncated; 0 when the ring is empty) into the stay ring
	public void CloseMonth( float happiness, int peopleInPark, int rating )
	{
		Arrivals.Add( ArrivalsThisMonth );
		ArrivalsThisMonth = 0;
		People.Add( peopleInPark );
		var (sum, count) = RecentStays.SumNewest( StayWindow );
		Stay.Add( count < 1 ? 0 : (int)(sum / count) );
		Happiness.Add( happiness );
		Rating.Add( rating );
	}

	/// <summary>The rings and the running arrivals, for a save.</summary>
	public ParkStatisticsData Capture() => new()
	{
		ArrivalsThisMonth = ArrivalsThisMonth,
		Arrivals = RingData.Of( Arrivals ), Happiness = RingData.Of( Happiness ), Stay = RingData.Of( Stay ), People = RingData.Of( People ), Rating = RingData.Of( Rating ), RecentStays = RingData.Of( RecentStays )
	};

	/// <summary>Replaces all rings atomically. Only an absent rating field migrates already-restored legacy ledger ratings.</summary>
	public void Restore( ParkStatisticsData data, IEnumerable<int>? legacyRatings = null )
	{
		if ( data == null || !data.IsValid )
			throw new InvalidDataException( "Invalid park statistics history." );
		var arrivals = data.Arrivals!.ToRing();
		var happiness = data.Happiness!.ToRing();
		var stay = data.Stay!.ToRing();
		var people = data.People!.ToRing();
		var rating = data.Rating?.ToRing() ?? LegacyRating( legacyRatings );
		var recentStays = data.RecentStays!.ToRing();
		ArrivalsThisMonth = data.ArrivalsThisMonth;
		Arrivals = arrivals;
		Happiness = happiness;
		Stay = stay;
		People = people;
		Rating = rating;
		RecentStays = recentStays;
	}

	/// <summary>Imports a validated TPW or TPI bundle without creating financial months or interpreting inactive native words as observations.</summary>
	public void RestoreOriginal( NativeParkStatistics statistics )
	{
		ArgumentNullException.ThrowIfNull( statistics );
		Restore( new ParkStatisticsData
		{
			ArrivalsThisMonth = statistics.ArrivalsThisMonth,
			Arrivals = RingData.OfOriginal( statistics.Arrivals ), Happiness = RingData.OfOriginal( statistics.Happiness ),
			Stay = RingData.OfOriginal( statistics.Stay ), People = RingData.OfOriginal( statistics.People ),
			Rating = RingData.OfOriginal( statistics.Rating ), RecentStays = RingData.OfOriginal( statistics.RecentStays )
		} );
	}

	private static HistoryRing LegacyRating( IEnumerable<int>? ratings ) => HistoryRing.Of( MonthsKept,
		(ratings ?? Enumerable.Empty<int>()).TakeLast( MonthsKept ).Select( rating => (double)rating ) );

	/// <summary>One-time migration when an old host save has no statistics section. Never called by graph drawing or month close.</summary>
	internal void RestoreLegacyRating( IEnumerable<int> ratings ) => Rating = LegacyRating( ratings );

	/// <summary>Whether nothing has been recorded (a fresh park, or a save from before the history).</summary>
	public bool IsEmpty => ArrivalsThisMonth == 0 && Arrivals.Count == 0 && Happiness.Count == 0 && Stay.Count == 0 && People.Count == 0 && Rating.Count == 0 && RecentStays.Count == 0;

	// ---- the graph's reads ---------------------------------------------------------------------------------------------------------

	/// <summary>
	/// The highest sample of the newest <c>period + 1</c> that the scale takes from a ring (<paramref name="floating"/>: a single ring, whose maximum is truncated), 0 when none is above 0.
	/// </summary>
	// [BIN:TPW-EXE:0x4801D0 series maximum] the scan reads samples 0 up to and including the period's sample count (one more than the graph draws), stops at the first it cannot read, and keeps the largest strictly above 0; the single ring's maximum (0x4802A0) is truncated to an integer
	public static int SeriesMaximum( HistoryRing ring, int period, bool floating )
	{
		var samples = PeriodSamples( period );
		var best = 0d;
		for ( var back = 0; back <= samples; back++ )
		{
			if ( !ring.TryGet( back, out var value ) )
				break;
			if ( value > best )
				best = value;
		}
		return floating ? (int)best : (int)Math.Min( best, int.MaxValue );
	}

	/// <summary>
	/// The points a series draws, oldest first: the newest <c>min(period samples, entries held)</c> plus one, where the extra oldest point is 0 when the ring has no older entry.
	/// </summary>
	// [BIN:TPW-EXE:0x480460 series points] n = min(period samples, entries held); the buffer holds n + 1 values, sample n back down to sample 0, an unreadable sample keeping the previous value (0 at the start); a single ring (0x480570) truncates each to an integer
	public static int[] SeriesPoints( HistoryRing ring, int period, bool floating )
	{
		var n = Math.Min( PeriodSamples( period ), ring.Count );
		var points = new int[n + 1];
		var last = 0;
		for ( var back = n; back >= 0; back-- )
		{
			if ( ring.TryGet( back, out var value ) )
				last = floating ? (int)value : (int)Math.Clamp( value, int.MinValue, int.MaxValue );
			points[n - back] = last;
		}
		return points;
	}
}

/// <summary>A history ring as saved: its slots, head and wrapped flag.</summary>
public sealed class RingData
{
	public required List<double> Slots { get; init; }
	public required int Head { get; init; }
	public required bool Wrapped { get; init; }

	internal static RingData Of( HistoryRing ring )
	{
		var slots = new List<double>( ring.Size );
		for ( var slot = 0; slot < ring.Size; slot++ )
			slots.Add( SlotValue( ring, slot ) );
		return new RingData { Slots = slots, Head = ring.Head, Wrapped = ring.Wrapped };
	}

	internal static RingData OfOriginal( NativeStatisticsRing ring ) => new() { Slots = ring.Values.ToList(), Head = ring.Head, Wrapped = ring.Wrapped };

	private static double SlotValue( HistoryRing ring, int slot )
	{
		// a slot is read through its age: head - slot, wrapped round
		var back = (ring.Head - slot + ring.Size) % ring.Size;
		return ring.TryGet( back, out var value ) ? value : 0;
	}

	internal bool IsValid( int size ) => Slots is { } slots && slots.Count == size && Head >= -1 && Head < size && (Head >= 0 || !Wrapped) && slots.All( double.IsFinite );

	// the slots, head and wrapped flag are restored as saved: replaying the entries into a fresh ring would leave a ring that has gone round with its
	// head at size - 1 and the flag clear, so the next save would differ from the loaded one (IsValid has checked the size before this runs)
	internal HistoryRing ToRing() => HistoryRing.Restore( Slots, Head, Wrapped );
}

/// <summary>The park statistics history as saved (optional; absent in saves from before the park information window's graph).</summary>
public sealed class ParkStatisticsData
{
	public int ArrivalsThisMonth { get; init; }
	public RingData? Arrivals { get; init; }
	public RingData? Happiness { get; init; }
	public RingData? Stay { get; init; }
	public RingData? People { get; init; }
	/// <summary>Optional for legacy migration only; a present empty ring deliberately carries no rating history.</summary>
	public RingData? Rating { get; init; }
	public RingData? RecentStays { get; init; }

	/// <summary>Whether required rings and any optional legacy rating have their expected sizes and bounded, finite state.</summary>
	public bool IsValid => ArrivalsThisMonth >= 0
		&& Arrivals?.IsValid( ParkStatisticsHistory.MonthsKept ) == true && Happiness?.IsValid( ParkStatisticsHistory.MonthsKept ) == true
		&& Stay?.IsValid( ParkStatisticsHistory.MonthsKept ) == true && People?.IsValid( ParkStatisticsHistory.MonthsKept ) == true
		&& RecentStays?.IsValid( ParkStatisticsHistory.StayWindow ) == true
		&& (Rating is null || Rating.IsValid( ParkStatisticsHistory.MonthsKept ));
}
