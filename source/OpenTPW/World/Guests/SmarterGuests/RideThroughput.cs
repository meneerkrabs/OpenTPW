namespace OpenTPW;

/// <summary>
/// One attraction instance's observed throughput (docs/SMARTER-GUESTS.md, phase 3): a moving average of the turns between two boardings while the attraction had a queue, in 1/256 turn.
/// Throughput is <c>1 / interval</c> guests per turn, which is <c>capacity / cycle duration</c> once the ride runs full cycles: a ride of capacity 20 and a cycle of 100 turns boards a guest every 5 turns on average.
/// </summary>
/// <param name="AttractionId">The attraction's stable id (<see cref="IRideVisitorBridge.AttractionId"/>).</param>
/// <param name="Capacity">The attraction's capacity when the average was started; a different capacity (an upgrade) restarts it.</param>
/// <param name="Speed">The attraction's operating speed when the average was started; a different speed (an upgrade) restarts it.</param>
/// <param name="Interval256">The average interval between two boardings, in 1/256 turn.</param>
/// <param name="Samples">How many intervals the average holds (saturating at <see cref="RideThroughputs.MaximumSamples"/>).</param>
/// <param name="LastBoardTurn">The park turn of the latest boarding, −1 for none.</param>
/// <param name="ClockTurn">The park turn the silence clock started: the latest boarding, or the turn the attraction was first seen open, working and with a queue since then, whichever is later; −1 before either.</param>
/// <param name="Demand">The attraction has had a queue, was open and working since <paramref name="ClockTurn"/>, so the silence since then is the attraction's, and (when the clock was started by a boarding) the next boarding's gap measures its throughput rather than the guests' arrival rate.</param>
public readonly record struct RideThroughput( int AttractionId, int Capacity, int Speed, long Interval256, int Samples, long LastBoardTurn, long ClockTurn, bool Demand );

/// <summary>
/// The persisted throughput estimates of the smarter-guests policy, one per attraction instance that has boarded a guest since it was placed, in attraction-id order. The host feeds it from the boardings it
/// already sees (<see cref="GuestSimulation"/>: <c>OnVisitorBoarded</c>) and from the attractions' state once per park turn; the policy only reads the resulting interval from the candidate view. All arithmetic is
/// integer, so a resumed park continues exactly.
/// </summary>
/// <remarks>
/// Rules (all constants are OpenTPW's own, tuned by measurement):
/// <list type="bullet">
/// <item><b>Cold start</b>: an attraction without an estimate is read as <see cref="ColdStartInterval256"/>: <see cref="DefaultCycleTurns"/> (a ride) or <see cref="DefaultServiceTurns"/> (a shop, sideshow or toilet) divided by its catalogue capacity.</item>
/// <item><b>Average</b>: each measured gap is folded in with weight 1/(samples + 2), at most 1/<see cref="MaximumWeightDivisor"/>, so the cold-start value counts as one sample and a long history moves slowly.</item>
/// <item><b>Demand follows the queue</b>: at each park turn start an attraction that is open and working with a queue gets an entry (cold start) if it has none, and a silence clock and the <c>Demand</c> flag if it did not have them; a closed or broken attraction, or one with an empty queue, loses the flag. So a repaired ride whose head never boards, or a ride that never boards anybody, is read as stalled. The clock starts at the first turn of demand and its first boarding is not folded in as a gap (it measures the wait, not the service); only gaps that start at a boarding are.</item>
/// <item><b>Upgrade</b>: a changed capacity or speed restarts the estimate at the cold-start value for the new capacity.</item>
/// <item><b>Stall</b>: an attraction that has not boarded anybody for more than two nominal cycles while guests wait is read as the silence since its clock started (<see cref="EffectiveInterval256"/>).</item>
/// <item><b>Zero throughput</b>: an interval below a quarter turn or a capacity below 1 cannot be stored (the interval is clamped to <see cref="MinimumInterval256"/>); a view without an estimate (interval 0) is read by the policy as "unknown" and falls back to the original's queue term.</item>
/// </list>
/// </remarks>
// [EXT:smarter-guests-spread] OpenTPW's own bookkeeping; the original guests keep none
public sealed class RideThroughputs
{
	/// <summary>The cycle a ride is assumed to have until it has been measured, in park turns (see docs/SMARTER-GUESTS.md for the measurement behind it).</summary>
	public const int DefaultCycleTurns = 60;

	/// <summary>The cycle a shop, sideshow or toilet is assumed to have until it has been measured, in park turns.</summary>
	public const int DefaultServiceTurns = 12;

	/// <summary>The shortest interval an estimate can hold: a quarter of a park turn (guests of one batch can board several to a turn).</summary>
	public const long MinimumInterval256 = 64;

	/// <summary>The longest gap folded into an average, in park turns (a day is 23 turns; a ride that waits longer than this has not been running).</summary>
	public const long MaximumGapTurns = 1200;

	/// <summary>The longest interval an estimate can hold, in 1/256 turn.</summary>
	public const long MaximumInterval256 = MaximumGapTurns * 256;

	/// <summary>The most weight a long history gives the newest gap: 1/16.</summary>
	public const int MaximumWeightDivisor = 16;

	/// <summary>The samples counter saturates here.</summary>
	public const int MaximumSamples = 1000;

	/// <summary>How many nominal cycles without a boarding (while guests wait) make the policy read the gap since the last boarding instead of the average.</summary>
	public const int StallCycles = 2;

	private readonly List<RideThroughput> entries = [];

	/// <summary>The number of attractions with an estimate.</summary>
	public int Count => entries.Count;

	/// <summary>The estimates in attraction-id order.</summary>
	public IReadOnlyList<RideThroughput> Entries => entries;

	private int LowerBound( int attractionId )
	{
		int low = 0, high = entries.Count;
		while ( low < high )
		{
			var middle = low + ((high - low) >> 1);
			if ( entries[middle].AttractionId < attractionId )
				low = middle + 1;
			else
				high = middle;
		}
		return low;
	}

	/// <summary>The estimate of the attraction, or false when it has none.</summary>
	public bool TryGet( int attractionId, out RideThroughput entry )
	{
		var index = LowerBound( attractionId );
		if ( index < entries.Count && entries[index].AttractionId == attractionId )
		{
			entry = entries[index];
			return true;
		}
		entry = default;
		return false;
	}

	/// <summary>The cold-start interval of an attraction of this capacity: the default cycle over the capacity (below 1 reads as 1), never below <see cref="MinimumInterval256"/>.</summary>
	public static long ColdStartInterval256( int capacity, RideVisitorKind kind = RideVisitorKind.Ride ) =>
		Math.Max( MinimumInterval256, (kind == RideVisitorKind.Ride ? DefaultCycleTurns : DefaultServiceTurns) * 256L / Math.Max( 1, capacity ) );

	/// <summary>
	/// The interval the policy should use for a candidate: the estimate (the cold start without one, or after an upgrade), raised to the time since the last boarding when the attraction has stalled
	/// (<see cref="StallCycles"/> nominal cycles without a boarding while guests wait). Always at least <see cref="MinimumInterval256"/>.
	/// </summary>
	/// <param name="queueLength">Guests in the attraction's queue now.</param>
	public long IntervalFor( int attractionId, RideVisitorKind kind, int capacity, int speed, long turn, int queueLength )
	{
		capacity = Math.Max( 1, capacity );
		if ( !TryGet( attractionId, out var entry ) || entry.Capacity != capacity || entry.Speed != speed )
			return ColdStartInterval256( capacity, kind );
		return EffectiveInterval256( entry, turn, queueLength );
	}

	/// <summary>The estimate of <paramref name="entry"/>, or the silence since its clock started when it has stalled with guests waiting.</summary>
	public static long EffectiveInterval256( in RideThroughput entry, long turn, int queueLength )
	{
		var interval = Math.Clamp( entry.Interval256, MinimumInterval256, MaximumInterval256 );
		if ( !entry.Demand || queueLength <= 0 || entry.ClockTurn < 0 || turn <= entry.ClockTurn )
			return interval;
		var since = Math.Min( turn - entry.ClockTurn, MaximumGapTurns ) * 256L;
		// per guest: a gap of `since` with a whole cycle of `capacity` guests in it is `since / capacity` per guest; it only matters when it exceeds the nominal cycle by far
		if ( since <= interval * entry.Capacity * StallCycles )
			return interval;
		return Math.Clamp( since / entry.Capacity, interval, MaximumInterval256 );
	}

	/// <summary>
	/// A guest boarded the attraction on park turn <paramref name="turn"/>, leaving <paramref name="queueLengthAfter"/> guests in its queue. Folds the gap since the previous boarding into the average when the
	/// attraction had demand throughout, then starts the next gap.
	/// </summary>
	public void Boarded( int attractionId, RideVisitorKind kind, int capacity, int speed, long turn, int queueLengthAfter )
	{
		capacity = Math.Max( 1, capacity );
		var index = LowerBound( attractionId );
		var found = index < entries.Count && entries[index].AttractionId == attractionId;
		var entry = found ? entries[index] : default;
		if ( !found || entry.Capacity != capacity || entry.Speed != speed )
			// cold start, or an upgrade: the average restarts at the catalogue value for the new capacity
			entry = new RideThroughput( attractionId, capacity, speed, ColdStartInterval256( capacity, kind ), 0, -1, -1, false );
		// a gap counts when the attraction had demand throughout and it started at a boarding; two boardings in one park turn are a gap of 0, which the average takes down to the minimum interval
		if ( entry.Demand && entry.LastBoardTurn >= 0 && entry.ClockTurn == entry.LastBoardTurn && turn >= entry.LastBoardTurn )
		{
			var gap = Math.Min( turn - entry.LastBoardTurn, MaximumGapTurns ) * 256L;
			var divisor = Math.Min( entry.Samples + 2, MaximumWeightDivisor );
			var interval = entry.Interval256 + (gap - entry.Interval256) / divisor;
			entry = entry with { Interval256 = Math.Clamp( interval, MinimumInterval256, MaximumInterval256 ), Samples = Math.Min( MaximumSamples, entry.Samples + 1 ) };
		}
		entry = entry with { LastBoardTurn = turn, ClockTurn = turn, Demand = queueLengthAfter > 0 };
		if ( found )
			entries[index] = entry;
		else
			entries.Insert( index, entry );
	}

	/// <summary>
	/// The turn-start look at an attraction (docs/SMARTER-GUESTS.md): demand follows the queue. An attraction that is open and working with a queue and has no demand flag gets one, with its silence clock started at
	/// <paramref name="turn"/> (an attraction without an entry gets one at the cold start, so a ride that never boards anybody is read as stalled too); a closed or broken one, or one with an empty queue, loses the flag, so
	/// the next boarding starts a new gap. An upgrade (a changed capacity or speed) restarts the estimate here as well, so the policy never reads a stale one.
	/// </summary>
	public void Observe( int attractionId, RideVisitorKind kind, int capacity, int speed, bool working, int queueLength, long turn )
	{
		capacity = Math.Max( 1, capacity );
		var index = LowerBound( attractionId );
		var found = index < entries.Count && entries[index].AttractionId == attractionId;
		var wanted = working && queueLength > 0;
		if ( !found && !wanted )
			return;
		var entry = found ? entries[index] : new RideThroughput( attractionId, capacity, speed, ColdStartInterval256( capacity, kind ), 0, -1, -1, false );
		if ( found && (entry.Capacity != capacity || entry.Speed != speed) )
			entry = new RideThroughput( attractionId, capacity, speed, ColdStartInterval256( capacity, kind ), 0, -1, -1, false );
		if ( wanted && !entry.Demand )
			entry = entry with { Demand = true, ClockTurn = turn };
		else if ( !wanted && entry.Demand )
			entry = entry with { Demand = false };
		if ( found )
			entries[index] = entry;
		else
			entries.Insert( index, entry );
	}

	/// <summary>Forgets the attraction's estimate (it was removed; ids are not reused, but the bookkeeping stays bounded by the attractions in the park).</summary>
	public void Remove( int attractionId )
	{
		var index = LowerBound( attractionId );
		if ( index < entries.Count && entries[index].AttractionId == attractionId )
			entries.RemoveAt( index );
	}

	/// <summary>Removes every estimate.</summary>
	public void Clear() => entries.Clear();

	/// <summary>Replaces the estimates with <paramref name="saved"/>, which the section's validation has checked.</summary>
	internal void Load( IEnumerable<RideThroughput> saved )
	{
		entries.Clear();
		entries.AddRange( saved );
	}
}
