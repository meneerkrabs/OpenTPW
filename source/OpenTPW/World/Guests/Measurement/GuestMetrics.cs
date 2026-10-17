using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>Settings of a <see cref="GuestMetrics"/> collector.</summary>
public sealed record GuestMetricsOptions
{
	/// <summary>A guest whose major decisions find no attraction for more than this many park turns counts as stuck (a day is about 23 turns).</summary>
	public int NoChoiceTurns { get; init; } = 50;
}

/// <summary>The outcome mix of the queue attempts that started in the measured window, and the waits of the boarded ones (docs/SMARTER-GUESTS.md).</summary>
/// <param name="Unfinished">Attempts still open when the window closed (their age is in <paramref name="UnfinishedAgeP50"/> and on).</param>
/// <param name="Superseded">Attempts that ended with no report: a guest joined again, or departed, while one was open. The simulation never does this, so it must be 0; it is counted apart so the identity below is not tautological.</param>
/// <param name="WaitP50">Waits of the boarded attempts only; the long tail is right-censored (an attempt still open at the window's end is not in it), see <paramref name="CensoredWaitP90"/>.</param>
/// <param name="CensoredWaitP90">Percentile over the boarded waits and the ages of the unfinished attempts together: a lower bound of the true wait percentile.</param>
public sealed record QueueOutcomeMetrics( long Attempts, long Boarded, long Abandoned, long Rejected, long Unfinished, long Superseded, IReadOnlyDictionary<string, long> RejectedByReason,
	IReadOnlyDictionary<string, long> AbandonedByReason, double? WaitP50, double? WaitP90, double? WaitP99, double? MeanWait, double? MeanAbandonedWait,
	double? UnfinishedAgeP50, double? UnfinishedAgeP90, double? UnfinishedAgeMax, double? CensoredWaitP90, double? CensoredWaitP99 )
{
	/// <summary>The consistency the instrumentation guarantees: every attempt ended as exactly one outcome, and none ended unreported.</summary>
	public bool IsConsistent => Attempts == Boarded + Abandoned + Rejected + Unfinished + Superseded && Superseded == 0
		&& Rejected == RejectedByReason.Values.Sum() && Abandoned == AbandonedByReason.Values.Sum();
}

/// <summary>Happiness of the guests admitted in one cohort, weighted by guest time (every admitted guest-second counts, not only the survivors').</summary>
public sealed record CohortMetrics( string Cohort, long Admitted, double GuestSeconds, double? MeanHappiness, long Departed, double? MeanHappinessAtDeparture );

/// <summary>One attraction instance's use over the window.</summary>
public sealed record AttractionUse( int AttractionId, string Name, string Kind, long Uses, long Attempts, long Boarded, long Abandoned, long Rejected );

public sealed record DepartureMetrics( long Total, long Admitted, long NeverAdmitted, long HappinessZero, long ExitCounterZero, long ParkClosed, long BoothRefused, long Guard, long Other,
	double? MeanHappinessAtDeparture, double? MeanTurnsInPark, double? MeanRides );

public sealed record GuestMetricsSnapshot( long Admissions, double RevenueTotal, double RevenueEntrance, double RevenuePerAdmittedGuest, QueueOutcomeMetrics Queue,
	double? GuestTimeHappiness, double? SurvivorHappiness, IReadOnlyList<CohortMetrics> Cohorts, DepartureMetrics Departures, IReadOnlyList<AttractionUse> Attractions,
	double GiniRides, double GiniAll, double GiniRidesPerCapacity, long NoChoiceEvents, long StuckGuests, double GuestSeconds, double MeanGuestsInPark, long Ticks )
{
	/// <summary>The numbers the report compares across seeds and runs, in a fixed order.</summary>
	public IReadOnlyList<(string Name, double Value)> Headline()
	{
		var queue = Queue;
		double Rate( long count ) => queue.Attempts == 0 ? 0 : (double)count / queue.Attempts;
		var list = new List<(string, double)>
		{
			("admissions", Admissions),
			("queue.attempts", queue.Attempts),
			("queue.boarded.rate", Rate( queue.Boarded )),
			("queue.abandoned.rate", Rate( queue.Abandoned )),
			("queue.rejected.rate", Rate( queue.Rejected )),
			("queue.rejected.price.rate", Rate( queue.RejectedByReason.GetValueOrDefault( "Price" ) )),
			("queue.unfinished.rate", Rate( queue.Unfinished )),
			("queue.wait.p50", queue.WaitP50 ?? double.NaN),
			("queue.wait.p90", queue.WaitP90 ?? double.NaN),
			("queue.wait.p99", queue.WaitP99 ?? double.NaN),
			("queue.wait.p90.censored", queue.CensoredWaitP90 ?? double.NaN),
			("queue.wait.p99.censored", queue.CensoredWaitP99 ?? double.NaN),
			("queue.superseded", queue.Superseded),
			("happiness.guestTime", GuestTimeHappiness ?? double.NaN),
			("happiness.survivors", SurvivorHappiness ?? double.NaN),
			("departures.perAdmission", Admissions == 0 ? 0 : (double)Departures.Admitted / Admissions),
			("departures.happinessZero.perAdmission", Admissions == 0 ? 0 : (double)Departures.HappinessZero / Admissions),
			("departures.exitCounterZero.perAdmission", Admissions == 0 ? 0 : (double)Departures.ExitCounterZero / Admissions),
			("departures.parkClosed.perAdmission", Admissions == 0 ? 0 : (double)Departures.ParkClosed / Admissions),
			("use.giniRides", GiniRides),
			("use.giniRidesPerCapacity", GiniRidesPerCapacity),
			("use.giniAll", GiniAll),
			("revenue.perAdmittedGuest", RevenuePerAdmittedGuest),
			("guests.stuck", StuckGuests),
			("guests.meanInPark", MeanGuestsInPark)
		};
		return list.Select( item => (item.Item1, item.Item2) ).ToList();
	}

	public JsonObject ToJsonObject()
	{
		static JsonNode? N( double? value ) => value is { } v && double.IsFinite( v ) ? JsonValue.Create( GuestMetrics.Round( v ) ) : null;
		static JsonObject Counts( IReadOnlyDictionary<string, long> counts )
		{
			var node = new JsonObject();
			foreach ( var (key, value) in counts.OrderBy( item => item.Key, StringComparer.Ordinal ) )
				node[key] = value;
			return node;
		}
		var q = Queue;
		return new JsonObject
		{
			["admissions"] = Admissions,
			["ticks"] = Ticks,
			["guestSeconds"] = N( GuestSeconds ),
			["meanGuestsInPark"] = N( MeanGuestsInPark ),
			["queue"] = new JsonObject
			{
				["attempts"] = q.Attempts,
				["boarded"] = q.Boarded,
				["abandoned"] = q.Abandoned,
				["rejected"] = q.Rejected,
				["unfinished"] = q.Unfinished,
				["superseded"] = q.Superseded,
				["rejectedByReason"] = Counts( q.RejectedByReason ),
				["abandonedByReason"] = Counts( q.AbandonedByReason ),
				["waitTurnsBoarded"] = new JsonObject { ["p50"] = N( q.WaitP50 ), ["p90"] = N( q.WaitP90 ), ["p99"] = N( q.WaitP99 ), ["mean"] = N( q.MeanWait ) },
				["meanWaitTurnsAbandoned"] = N( q.MeanAbandonedWait ),
				["unfinishedAgeTurns"] = new JsonObject { ["p50"] = N( q.UnfinishedAgeP50 ), ["p90"] = N( q.UnfinishedAgeP90 ), ["max"] = N( q.UnfinishedAgeMax ) },
				["censoredWaitTurns"] = new JsonObject { ["p90"] = N( q.CensoredWaitP90 ), ["p99"] = N( q.CensoredWaitP99 ) }
			},
			["happiness"] = new JsonObject
			{
				["guestTimeWeighted"] = N( GuestTimeHappiness ),
				["survivorsAtEnd"] = N( SurvivorHappiness ),
				["cohorts"] = new JsonArray( Cohorts.Select( cohort => (JsonNode)new JsonObject
				{
					["cohort"] = cohort.Cohort,
					["admitted"] = cohort.Admitted,
					["guestSeconds"] = N( cohort.GuestSeconds ),
					["meanHappiness"] = N( cohort.MeanHappiness ),
					["departed"] = cohort.Departed,
					["meanHappinessAtDeparture"] = N( cohort.MeanHappinessAtDeparture )
				} ).ToArray() )
			},
			["departures"] = new JsonObject
			{
				["total"] = Departures.Total,
				["admitted"] = Departures.Admitted,
				["neverAdmitted"] = Departures.NeverAdmitted,
				["byPredicate"] = new JsonObject
				{
					["happinessZero"] = Departures.HappinessZero,
					["exitCounterZero"] = Departures.ExitCounterZero,
					["parkClosed"] = Departures.ParkClosed,
					["boothRefused"] = Departures.BoothRefused,
					["guard"] = Departures.Guard,
					["other"] = Departures.Other
				},
				["meanHappinessAtDeparture"] = N( Departures.MeanHappinessAtDeparture ),
				["meanTurnsInPark"] = N( Departures.MeanTurnsInPark ),
				["meanRides"] = N( Departures.MeanRides )
			},
			["use"] = new JsonObject
			{
				["giniRides"] = N( GiniRides ),
				["giniAll"] = N( GiniAll ),
				["giniRidesPerCapacity"] = N( GiniRidesPerCapacity ),
				["attractions"] = new JsonArray( Attractions.Select( use => (JsonNode)new JsonObject
				{
					["id"] = use.AttractionId,
					["name"] = use.Name,
					["kind"] = use.Kind,
					["uses"] = use.Uses,
					["attempts"] = use.Attempts,
					["boarded"] = use.Boarded,
					["abandoned"] = use.Abandoned,
					["rejected"] = use.Rejected
				} ).ToArray() )
			},
			["revenue"] = new JsonObject
			{
				["total"] = N( RevenueTotal ),
				["entrance"] = N( RevenueEntrance ),
				["perAdmittedGuest"] = N( RevenuePerAdmittedGuest )
			},
			["noChoice"] = new JsonObject { ["events"] = NoChoiceEvents, ["stuckGuests"] = StuckGuests }
		};
	}
}

/// <summary>
/// The passive guest measurement of docs/SMARTER-GUESTS.md: an <see cref="IGuestObserver"/> that tallies queue attempts, departures, happiness by admission cohort and by guest time,
/// the use of every attraction instance, revenue and guests that found nothing to do. It changes nothing in the simulation. <see cref="BeginWindow"/> starts the measured window (the
/// warm-up before it is not counted; an attempt counts when it starts inside the window), <see cref="EndWindow"/> closes it and gives the snapshot.
/// </summary>
public sealed class GuestMetrics : IGuestObserver
{
	private sealed class Open
	{
		public int AttractionId;
		public bool Counted;
		public long JoinTurn;
	}

	private sealed class Track
	{
		public int GuestId;
		public bool Admitted;
		public int Cohort = CarryOver;
		public Open? Attempt;
		public DepartureCause LastCause;
		public long NoChoiceSinceTurn = -1;
		public bool Stuck;
	}

	private sealed class Cohort
	{
		public long Admitted, Departed;
		public double Seconds, HappinessSeconds, DepartureHappiness;
	}

	private sealed class PerAttraction
	{
		public long Uses, Attempts, Boarded, Abandoned, Rejected;
	}

	private const int CarryOver = -1;
	private readonly GuestMetricsOptions options;
	private readonly Dictionary<Guest, Track> tracks = new();
	private readonly Dictionary<int, Cohort> cohorts = new();
	private readonly Dictionary<int, PerAttraction> perAttraction = new();
	private readonly Dictionary<QueueEndReason, long> rejectedBy = new(), abandonedBy = new();
	private readonly List<double> waits = new();
	private readonly List<double> abandonedWaits = new();
	private readonly List<double> unfinishedAges = new();
	private GuestSimulation? simulation;
	private bool recording, windowDone;
	private long windowStartTick, windowStartDay, lastTick;
	private long attempts, boarded, abandoned, rejected, unfinished, superseded;
	private long admissions, noChoiceEvents, stuck;
	private double revenue, entranceRevenue;
	private double guestSeconds, happinessSeconds, guestTickSum;
	private long ticks;
	private long departures, departedAdmitted, depHappinessZero, depExitZero, depClosed, depBooth, depGuard, depOther;
	private double depHappiness, depTurns, depRides;

	public GuestMetrics( GuestMetricsOptions? options = null ) => this.options = options ?? new GuestMetricsOptions();

	/// <summary>When set, the window opens by itself at the end of this simulation tick (for a simulation another loop drives, as the level gate's).</summary>
	public long? AutoBeginAtTick { get; set; }

	/// <summary>Whether a window is open.</summary>
	public bool Recording => recording;

	/// <summary>Makes this the simulation's observer; for a simulation built elsewhere set <see cref="GuestSimulation.Observer"/> before its first tick instead.</summary>
	public void Attach( GuestSimulation target )
	{
		simulation = target;
		target.Observer = this;
	}

	/// <summary>Starts the measured window now: tallies restart, and an attempt or guest already open belongs to the warm-up.</summary>
	public void BeginWindow()
	{
		recording = true;
		windowStartTick = lastTick;
		windowStartDay = ParkCalendar.DayIndex( lastTick );
		cohorts.Clear();
		perAttraction.Clear();
		rejectedBy.Clear();
		abandonedBy.Clear();
		waits.Clear();
		abandonedWaits.Clear();
		unfinishedAges.Clear();
		attempts = boarded = abandoned = rejected = unfinished = superseded = 0;
		admissions = noChoiceEvents = stuck = 0;
		revenue = entranceRevenue = 0;
		guestSeconds = happinessSeconds = guestTickSum = 0;
		ticks = 0;
		departures = departedAdmitted = depHappinessZero = depExitZero = depClosed = depBooth = depGuard = depOther = 0;
		depHappiness = depTurns = depRides = 0;
		foreach ( var track in tracks.Values )
		{
			if ( track.Attempt != null )
				track.Attempt.Counted = false;
			track.Cohort = CarryOver;
			track.Stuck = false;
		}
	}

	/// <summary>Closes the window: the attempts still open count as unfinished, and the snapshot is computed.</summary>
	public GuestMetricsSnapshot EndWindow()
	{
		if ( !recording )
			throw new InvalidOperationException( "No measured window is open." );
		recording = false;
		windowDone = true;
		var now = simulation?.ParkTurn ?? 0;
		foreach ( var track in tracks.Values )
		{
			if ( track.Attempt is { Counted: true } open )
			{
				unfinished++;
				unfinishedAges.Add( now - open.JoinTurn );
				open.Counted = false;
			}
			if ( !track.Stuck && track.NoChoiceSinceTurn >= 0 && now - track.NoChoiceSinceTurn > options.NoChoiceTurns )
			{
				track.Stuck = true;
				stuck++;
			}
		}
		var sim = simulation;
		var survivors = new List<double>();
		if ( sim != null )
		{
			foreach ( var guest in sim.Guests )
				if ( guest.Paid && guest.State != GuestState.Gone )
					survivors.Add( guest.Happiness );
		}
		var snapshotAttractions = new List<AttractionUse>();
		foreach ( var attraction in sim?.Attractions ?? Array.Empty<IRideVisitorBridge>() )
		{
			var use = perAttraction.GetValueOrDefault( attraction.AttractionId ) ?? new PerAttraction();
			snapshotAttractions.Add( new AttractionUse( attraction.AttractionId, attraction.Name, attraction.Kind.ToString(), use.Uses, use.Attempts, use.Boarded, use.Abandoned, use.Rejected ) );
		}
		var cohortList = cohorts.OrderBy( item => item.Key ).Select( item => new CohortMetrics( item.Key == CarryOver ? "carryOver" : $"day{item.Key}", item.Value.Admitted,
			item.Value.Seconds, item.Value.Seconds > 0 ? item.Value.HappinessSeconds / item.Value.Seconds : null, item.Value.Departed,
			item.Value.Departed > 0 ? item.Value.DepartureHappiness / item.Value.Departed : null ) ).ToList();
		var sorted = waits.OrderBy( value => value ).ToList();
		var ages = unfinishedAges.OrderBy( value => value ).ToList();
		var censored = waits.Concat( unfinishedAges ).OrderBy( value => value ).ToList();
		var queue = new QueueOutcomeMetrics( attempts, boarded, abandoned, rejected, unfinished, superseded,
			rejectedBy.ToDictionary( item => item.Key.ToString(), item => item.Value ), abandonedBy.ToDictionary( item => item.Key.ToString(), item => item.Value ),
			Percentile( sorted, 0.5 ), Percentile( sorted, 0.9 ), Percentile( sorted, 0.99 ), waits.Count > 0 ? waits.Average() : null, abandonedWaits.Count > 0 ? abandonedWaits.Average() : null,
			Percentile( ages, 0.5 ), Percentile( ages, 0.9 ), ages.Count > 0 ? ages[^1] : null, Percentile( censored, 0.9 ), Percentile( censored, 0.99 ) );
		var departureMetrics = new DepartureMetrics( departures, departedAdmitted, departures - departedAdmitted, depHappinessZero, depExitZero, depClosed, depBooth, depGuard, depOther,
			departedAdmitted > 0 ? depHappiness / departedAdmitted : null, departedAdmitted > 0 ? depTurns / departedAdmitted : null, departedAdmitted > 0 ? depRides / departedAdmitted : null );
		var rideUses = snapshotAttractions.Where( item => item.Kind == nameof( RideVisitorKind.Ride ) ).Select( item => (double)item.Uses ).ToList();
		var rideUsePerCapacity = (sim?.Attractions ?? Array.Empty<IRideVisitorBridge>()).Where( item => item.Kind == RideVisitorKind.Ride )
			.Select( item => (double)(perAttraction.GetValueOrDefault( item.AttractionId )?.Uses ?? 0) / Math.Max( 1, item.Capacity ) ).ToList();
		return new GuestMetricsSnapshot( admissions, revenue, entranceRevenue, admissions == 0 ? 0 : revenue / admissions, queue,
			guestSeconds > 0 ? happinessSeconds / guestSeconds : null, survivors.Count > 0 ? survivors.Average() : null, cohortList, departureMetrics, snapshotAttractions,
			Gini( rideUses ), Gini( snapshotAttractions.Select( item => (double)item.Uses ).ToList() ), Gini( rideUsePerCapacity ), noChoiceEvents, stuck, guestSeconds,
			ticks > 0 ? guestTickSum / ticks : 0, ticks );
	}

	private Track Get( Guest guest )
	{
		if ( !tracks.TryGetValue( guest, out var track ) )
			tracks[guest] = track = new Track { GuestId = guest.Id };
		return track;
	}

	private PerAttraction Of( int id )
	{
		if ( !perAttraction.TryGetValue( id, out var value ) )
			perAttraction[id] = value = new PerAttraction();
		return value;
	}

	private long Turn => simulation?.ParkTurn ?? 0;

	public void Admitted( Guest guest, int fee )
	{
		var track = Get( guest );
		track.Admitted = true;
		if ( !recording )
		{
			track.Cohort = CarryOver;
			return;
		}
		admissions++;
		track.Cohort = (int)(ParkCalendar.DayIndex( simulation?.TickCount ?? lastTick ) - windowStartDay);
		CohortOf( track.Cohort ).Admitted++;
	}

	private Cohort CohortOf( int key )
	{
		if ( !cohorts.TryGetValue( key, out var cohort ) )
			cohorts[key] = cohort = new Cohort();
		return cohort;
	}

	public void Spent( Guest guest, int amount, int attractionId )
	{
		if ( !recording )
			return;
		revenue += amount;
		if ( attractionId == 0 )
			entranceRevenue += amount;
	}

	public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason )
	{
		if ( !recording )
			return;
		attempts++;
		rejected++;
		rejectedBy[reason] = rejectedBy.GetValueOrDefault( reason ) + 1;
		var use = Of( attraction.AttractionId );
		use.Attempts++;
		use.Rejected++;
	}

	public void QueueJoined( Guest guest, IRideVisitorBridge attraction )
	{
		var track = Get( guest );
		// a join while another attempt is still open cannot happen in the simulation; it is counted apart (the tests assert 0) rather than as an outcome
		if ( track.Attempt is { Counted: true } )
			superseded++;
		track.Attempt = new Open { AttractionId = attraction.AttractionId, Counted = recording, JoinTurn = Turn };
		if ( !recording )
			return;
		attempts++;
		Of( attraction.AttractionId ).Attempts++;
	}

	public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns )
	{
		if ( recording )
			Of( attraction.AttractionId ).Uses++;
		var track = Get( guest );
		if ( track.Attempt is { } open )
		{
			if ( open.Counted )
			{
				boarded++;
				Of( open.AttractionId ).Boarded++;
				waits.Add( waitTurns );
			}
			track.Attempt = null;
		}
	}

	public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns )
	{
		var track = Get( guest );
		if ( track.Attempt is not { } open )
			return;
		track.Attempt = null;
		if ( !open.Counted )
			return;
		var use = Of( open.AttractionId );
		if ( QueueEndReasons.IsAbandonment( reason ) )
		{
			abandoned++;
			use.Abandoned++;
			abandonedBy[reason] = abandonedBy.GetValueOrDefault( reason ) + 1;
			abandonedWaits.Add( waitTurns );
		}
		else
		{
			rejected++;
			use.Rejected++;
			rejectedBy[reason] = rejectedBy.GetValueOrDefault( reason ) + 1;
		}
	}

	public void Decided( Guest guest, bool chose )
	{
		var track = Get( guest );
		if ( chose )
		{
			track.NoChoiceSinceTurn = -1;
			return;
		}
		if ( recording )
			noChoiceEvents++;
		var turn = Turn;
		if ( track.NoChoiceSinceTurn < 0 )
			track.NoChoiceSinceTurn = turn;
		if ( !track.Stuck && turn - track.NoChoiceSinceTurn > options.NoChoiceTurns )
		{
			track.Stuck = true;
			if ( recording )
				stuck++;
		}
	}

	public void Leaving( Guest guest, DepartureCause cause ) => Get( guest ).LastCause = cause;

	public void Recalled( Guest guest ) => Get( guest ).LastCause = DepartureCause.None;

	/// <summary>
	/// A restored snapshot made every guest a new object. What the collector holds per guest (admission cohort, open queue attempt, last leaving cause, no-choice streak) is carried over by
	/// guest id, so a save and a restore inside a window leave the tallies as they were; a guest the collector never saw starts fresh (admitted when its snapshot says it paid).
	/// </summary>
	public void Restored( GuestSimulation sim )
	{
		simulation ??= sim;
		var byId = new Dictionary<int, Track>();
		foreach ( var track in tracks.Values )
			byId[track.GuestId] = track;
		tracks.Clear();
		foreach ( var guest in sim.Guests )
			tracks[guest] = byId.TryGetValue( guest.Id, out var carried ) ? carried : new Track { GuestId = guest.Id, Admitted = guest.Paid };
	}

	public void Departed( Guest guest )
	{
		if ( !tracks.Remove( guest, out var track ) )
			track = new Track();
		if ( track.Attempt is { Counted: true } )
			superseded++;
		if ( !recording )
			return;
		departures++;
		var cause = track.LastCause;
		if ( cause.HasFlag( DepartureCause.HappinessZero ) )
			depHappinessZero++;
		if ( cause.HasFlag( DepartureCause.ExitCounterZero ) )
			depExitZero++;
		if ( cause.HasFlag( DepartureCause.ParkClosed ) )
			depClosed++;
		if ( cause.HasFlag( DepartureCause.BoothRefused ) )
			depBooth++;
		if ( cause.HasFlag( DepartureCause.Guard ) )
			depGuard++;
		if ( cause == DepartureCause.None )
			depOther++;
		if ( !track.Admitted )
			return;
		departedAdmitted++;
		depHappiness += guest.Happiness;
		depTurns += ParkCalendar.Turn( (long)((guest.TimeInParkSeconds ?? 0) * FixedStepClock.TicksPerSecond) );
		depRides += guest.RidesTaken;
		var cohort = CohortOf( track.Cohort );
		cohort.Departed++;
		cohort.DepartureHappiness += guest.Happiness;
	}

	public void Ticked( GuestSimulation sim, float deltaSeconds )
	{
		simulation ??= sim;
		lastTick = sim.TickCount;
		if ( !recording && !windowDone && AutoBeginAtTick is { } begin && lastTick >= begin )
		{
			// the window opens after the tick that reached the mark, so it holds exactly the ticks that follow it
			BeginWindow();
			return;
		}
		if ( !recording )
			return;
		ticks++;
		var inPark = 0;
		foreach ( var guest in sim.Guests )
		{
			if ( !guest.Paid || guest.State == GuestState.Gone || !tracks.TryGetValue( guest, out var track ) )
				continue;
			inPark++;
			guestSeconds += deltaSeconds;
			happinessSeconds += guest.Happiness * deltaSeconds;
			var cohort = CohortOf( track.Cohort );
			cohort.Seconds += deltaSeconds;
			cohort.HappinessSeconds += guest.Happiness * deltaSeconds;
		}
		guestTickSum += inPark;
	}

	/// <summary>Nearest-rank percentile of an ascending list (null when empty).</summary>
	public static double? Percentile( IReadOnlyList<double> ascending, double fraction )
	{
		if ( ascending.Count == 0 )
			return null;
		var rank = (int)Math.Ceiling( fraction * ascending.Count ) - 1;
		return ascending[Math.Clamp( rank, 0, ascending.Count - 1 )];
	}

	/// <summary>The Gini coefficient of non-negative counts: 0 for an even spread, towards 1 when one instance takes everything (0 for fewer than two instances or no use at all).</summary>
	public static double Gini( IReadOnlyList<double> values )
	{
		var sorted = values.OrderBy( value => value ).ToList();
		var n = sorted.Count;
		var total = sorted.Sum();
		if ( n < 2 || total <= 0 )
			return 0;
		double weighted = 0;
		for ( var index = 0; index < n; index++ )
			weighted += (index + 1) * sorted[index];
		return 2 * weighted / (n * total) - (n + 1.0) / n;
	}

	/// <summary>Six decimals, so the report text does not depend on the last binary digits.</summary>
	public static double Round( double value ) => Math.Round( value, 6, MidpointRounding.AwayFromZero );
}
