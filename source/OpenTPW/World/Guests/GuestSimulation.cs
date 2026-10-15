namespace OpenTPW;

/// <summary>Deterministic SplitMix64 stream; independent of System.Random's implementation.</summary>
// [APPROX:DET-014] guests draw from their own SplitMix64 stream seeded from the world seed, not from the original's shared world LCG reseeded to each new guest's id — evidence needed: DET-I2 port of WorldRng (docs/reverse/DET-plan.md §2.3, §4.3)
public struct GuestRandom
{
	public ulong State;

	public GuestRandom( ulong seed ) => State = seed;

	public ulong NextULong()
	{
		var z = State += 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return z ^ (z >> 31);
	}

	/// <summary>Uniform integer in [0, maximumExclusive).</summary>
	public int Next( int maximumExclusive ) => maximumExclusive <= 0 ? 0 : (int)(NextULong() % (ulong)maximumExclusive);
	public int Next( int minimum, int maximumInclusive ) => minimum + Next( maximumInclusive - minimum + 1 );
	public float NextFloat() => (NextULong() >> 40) / (float)(1UL << 24);

	/// <summary>A word in [0, 2³¹): the range of the original's world generator, which returns the absolute value of its state (<c>0x105328</c>), for formulas that take a draw modulo something.</summary>
	public uint NextWord() => (uint)(NextULong() >> 33);
}

/// <summary>Counts for overlays, logs and tests.</summary>
public readonly record struct GuestStatistics( int InPark, int Walking, int Queueing, int OnRides, int Arriving, int Leaving, float AverageHappiness, GuestThought CommonThought, long Admissions );

/// <summary>
/// Park visitors on the fixed simulation tick: arrival from the bus stops through the ticket booth
/// (.sam FixedItemInfo cells), path walking on <see cref="GuestPathGrid"/> flow fields, needs, attraction
/// choice with the .sam decision weights, queues and rides through <see cref="IRideVisitorBridge"/>, and
/// leaving. Queues follow the original state 10/11/12/13/14 handlers (docs/reverse/QUEUE-plan.md): guests
/// join at the back cell, walk the queue cells to their position, stand and move up on park turns, and
/// are called forward by the attraction. All randomness comes from one seeded <see cref="GuestRandom"/>
/// (also the queue positions' sideways offsets); guests and attractions are
/// updated in id order, so the same seed and inputs give the same <see cref="ComputeStateHash"/>.
/// Rates and thresholds without an original source are labelled approximations in
/// <see cref="GuestSettings"/> and docs/GUESTS.md.
/// </summary>
public sealed class GuestSimulation : IRideVisitorHost, IGuardGuests
{
	/// <summary>Move-up delay factor: entering state 11 sets the delay to trunc(1.2 × position) park turns.</summary>
	// [BIN:STP-PPC:0x100EF868 state 11 entry] +508 = turn, +500 = trunc(1.2f × +497) (float data 0x55a0)
	public const float MoveDelayFactor = 1.2f;
	/// <summary>Largest position gap a guest waits out before moving up.</summary>
	public const int MoveUpWaitGap = 2;
	/// <summary>Park turns between needs checks in a queue (the interlude window).</summary>
	// [BIN:STP-PPC:0x100ED58C queue needs window] w = turn − +520; w > 30: happiness > 80 or 10..19 idles, < 10 leaves, else toilet > 80 leaves unless ProvidesRelief; w ≤ 30: boredom test turn > +508 + 100
	public const int NeedsWindowTurns = 30;
	public const int BoredomTurns = 100;
	/// <summary>Park turns an idle interlude lasts (state 8 restores the saved state once turn &gt; +520 + 10).</summary>
	public const int InterludeTurns = 10;
	/// <summary>The queue facing change spans (r mod 800) − 400 of 2048 (tp.exe <c>0x004FFFF0</c>).</summary>
	public const int FacingChangeRange = 800;
	public const float LeaveHappiness = 10;
	public const float ToiletLeaveLevel = 80;
	public const float TicketBoothSeconds = 1.0f;
	public const float ExitRideSeconds = 1.0f;
	public const float WaitForBusSeconds = 3.0f;

	private readonly List<Guest> guests = new();
	private readonly Dictionary<int, Guest> byId = new();
	private readonly List<IRideVisitorBridge> attractions = new();
	private GuestRandom random;
	private int nextId = 1;
	// The arrival timer's start turn (0 until the first group: the original's timer starts unset) and the group being dropped off.
	private long arrivalTimerStart;
	private bool deliveringArrivals;
	private float spawnTimer;   // kept for the save format; arrivals step once per park turn (ARRIVAL-001)
	private int pendingArrivals;
	private int laneToggle;
	private int seenGridVersion = -1;
	private readonly Dictionary<int, int> seenQueueEdits = new();
	/// <summary>True on the tick that starts a park turn: guests update their speed and take one steering step.</summary>
	private bool turnStarted;
	private GuestNavigation? stepping;
	private readonly Func<int, int, bool> isOpenForStep;
	private readonly Func<GuestNavigation, (int X, int Y)> separationForStep;
	/// <summary>The guests standing in each cell, for separation's neighbour query; rebuilt at the start of every park turn.</summary>
	private readonly Dictionary<(int X, int Y), List<Guest>> occupants = new();
	private readonly List<GuestNavigation> neighbours = new();

	public GuestSimulation( GuestPathGrid grid, GuestSettings settings, ulong seed )
	{
		Grid = grid;
		Settings = settings;
		Seed = seed;
		random = new GuestRandom( seed );
		isOpenForStep = IsOpenForStep;
		separationForStep = SeparationForStep;
	}

	public GuestPathGrid Grid { get; }
	public GuestSettings Settings { get; }
	public ulong Seed { get; }
	/// <summary>Current <see cref="GuestRandom"/> state (saved with the park; see <see cref="WorldRandomState"/>).</summary>
	public ulong RandomState => random.State;
	public IReadOnlyList<Guest> Guests => guests;
	public IReadOnlyList<IRideVisitorBridge> Attractions => attractions;
	public long TickCount { get; private set; }
	/// <summary>The park turn (248 ms of the fixed clock, <see cref="ParkCalendar.Turn"/>); queue waits and delays count these.</summary>
	public long ParkTurn => ParkCalendar.Turn( TickCount );
	public double TimeSeconds { get; private set; }
	public bool ArrivalsEnabled { get; set; } = true;
	/// <summary>The park turn the arrival timer reads: the economy's when attached, else <see cref="ParkTurn"/>.</summary>
	public Func<long>? ArrivalClock { get; set; }
	/// <summary>The park's attraction points for the arrival rule (the economy's attractions); 0 without one.</summary>
	public Func<int>? ParkPoints { get; set; }
	/// <summary>True while no group arrives (a bankrupt park).</summary>
	public Func<bool>? ArrivalsHalted { get; set; }
	/// <summary>
	/// True while the park's weather has drops falling (<see cref="ParkWeather.CurrentDrops"/> above 0). The group size then uses 0.8
	/// instead of 1.2. The original reads the signed 32-bit word at +48 of the weather thing (game +0x1DA724, saved as mWeather; the
	/// weather object's mCurrentDrops, the drops the weather ramps toward its target) and uses 0.8 when it is positive. Null (no
	/// weather yet, or a headless scene without one) is 1.2: no drops.
	/// </summary>
	// [BIN:STP-PPC:0x100CB564 arrival factor] 0x1083c8 returns the thing at ThingArray[u16 game+0x1DA724] (mWeather, created at 0x104a7c with the weather constructor 0x10298c, stored at 0x104a98 and saved by 0x105d3c as "mWeather"); 0xcb568 reads its +48 word, mCurrentDrops (serialised by 0x102b1c); positive selects 0.8f
	// [BIN:STP-PPC:0x10103300-0x10103348 drop ramp] the only writers of +48 are the constructor (0x102aa0, zero), the ramp toward mTargetDrops (+52) and the rain stop (0x103d08, zero), so the word is positive exactly while drops are falling
	public Func<bool>? WeatherDropsFalling { get; set; }
	/// <summary>The original's limit on guests in the park when a group's size is set.</summary>
	// [BIN:STP-PPC:0x100CB4E8 arrival group] a group is cut so the guests already counted plus the group stay at most 1500 (0x5DC)
	// [BIN:STP-PPC:0x100CB4E8 arrival group] executed by the PowerPC oracle (docs/reverse/ORACLE.md F9): the 500 cut applies when the GameType word (data:0x53d98) is 1, i.e. online parks only; offline parks use 1500
	// [APPROX:ARRIVAL-002] the online-park cut (500 instead of 1500 for GameType 1) has no counterpart: OpenTPW's online visits run no simulation — evidence needed: whether a hosted online park runs this routine and which game type it reports
	public const int ArrivalParkLimit = 1500;
	/// <summary>The group limit while <see cref="OnlineVisit"/> is set.</summary>
	public const int OnlineArrivalParkLimit = 500;
	/// <summary>
	/// True for an online visit of another player's park. The original cuts the group to 500 minus the guests when its GameType word
	/// (data:0x53D98) is 1 (online) and to 1500 otherwise.
	/// </summary>
	// [APPROX:ARRIVAL-002] an OpenTPW online visit (a non-sandbox park visit) is taken to be the original's GameType 1 — evidence needed: which GameType the original sets for a visit of another park, and whether a hosted online park runs the arrival routine at all
	public bool OnlineVisit { get; set; }

	private ParkLitter? litter;
	/// <summary>Where guests drop their litter: set here, else the park's (<see cref="IGuestPayments.Litter"/>); null keeps none.</summary>
	public ParkLitter? Litter { get => litter ?? Payments?.Litter; set => litter = value; }
	/// <summary>The park's litter bins (objects with <c>UsageInfo.HoldsLitter</c>), in object-list order; null: none.</summary>
	public Func<IReadOnlyList<LitterBin>>? Bins { get; set; }
	/// <summary>Squared distance in cells below which a bin is in reach (tp.exe <c>0x00500DC0</c>, radius 3).</summary>
	public const int BinRadius = 3;
	/// <summary>A guest reached its bin and threw its litter away (the level triggers the bin's script; tp.exe <c>0x004FFF20</c>).</summary>
	public event Action<Guest, int>? LitterBinned;
	/// <summary>Where guests pay (the park economy). Null: the standalone <see cref="GuestSettings.AdmissionFee"/> and attraction prices apply and no money is booked anywhere.</summary>
	public IGuestPayments? Payments { get; set; }
	/// <summary>Entrance fee: the economy's when <see cref="Payments"/> is set, else <see cref="GuestSettings.AdmissionFee"/>.</summary>
	public int AdmissionFee => Payments?.AdmissionFee ?? Settings.AdmissionFee;
	/// <summary>Number of guests admitted (a visitor count; money is booked by <see cref="Payments"/>).</summary>
	public long Admissions { get; private set; }
	public int Departed { get; private set; }
	/// <summary>Raised when a guest pays (guest, amount, attraction id or 0 for admission).</summary>
	public event Action<Guest, int, int>? MoneySpent;
	/// <summary>Raised when the script takes a queued guest: (guest, attraction, wait in park turns from joining).</summary>
	public event Action<Guest, IRideVisitorBridge, long>? QueueWaitCompleted;

	/// <summary>Continues the guest stream from a saved state.</summary>
	public void RestoreRandomState( ulong state ) => random = new GuestRandom( state );

	public Guest? Find( int id ) => byId.TryGetValue( id, out var guest ) ? guest : null;

	public void Register( IRideVisitorBridge attraction )
	{
		if ( attractions.Contains( attraction ) )
			return;
		attraction.Host = this;
		attractions.Add( attraction );
		attractions.Sort( ( a, b ) => a.AttractionId.CompareTo( b.AttractionId ) );
		attraction.RecomputeQueue( Grid );
		seenQueueEdits[attraction.AttractionId] = attraction.QueueEditCount;
	}

	/// <summary>Park turns the guest has waited in its current queue so far, or −1 when it is not queued.</summary>
	public long QueueWaitTurns( Guest guest ) => guest.QueueJoinTurn < 0 ? -1 : ParkTurn - guest.QueueJoinTurn;

	public void Unregister( IRideVisitorBridge attraction )
	{
		if ( !attractions.Remove( attraction ) )
			return;
		foreach ( var guest in guests )
		{
			if ( guest.AttractionId == attraction.AttractionId && (guest.State is GuestState.GoingToRide or GuestState.Using || guest.IsInQueue) )
			{
				attraction.LeaveQueue( guest.Id );
				ClearQueueState( guest );
				ReturnToPath( guest, guest.State == GuestState.Using ? attraction.ExitCell : guest.IsInQueue ? JoinCellOf( attraction ) : (guest.CellX, guest.CellY) );
			}
		}
		if ( attraction.Host == this )
			attraction.Host = null;
		seenQueueEdits.Remove( attraction.AttractionId );
	}

	private Guest Create()
	{
		// [BIN:STP-PPC:0x100E7644 guest constructor] the starting values come from GuestStart (ORACLE.md F25): eight draws in the constructor's order
		var start = GuestStart.FromDraws( Settings.Types, Settings.ExitLevelSeconds, Settings.ExitLevelVarSeconds, Settings.StartingCashVarPercent, () => random.NextWord(),
			Settings.PrankeryLikelihood, nextId );
		var guest = new Guest( nextId++, start.Type );
		guest.PrankFlag = start.PrankFlag;
		guest.Money = start.Money;
		guest.Happiness = start.Happiness;
		guest.Thirst = start.Thirst;
		guest.Hunger = start.Hunger;
		guest.Toilet = start.Toilet;
		guest.Nausea = start.Illness;
		guest.ExitLevel = start.ExitLevel;
		guest.OffsetX = (random.NextFloat() - 0.5f) * 0.5f;
		guest.OffsetY = (random.NextFloat() - 0.5f) * 0.5f;
		guest.BaseSpeed = GuestSpeed.BaseSpeeds[random.Next( GuestSpeed.BaseSpeeds.Length )];
		guests.Add( guest );
		byId.Add( guest.Id, guest );
		return guest;
	}

	/// <summary>Spawns a guest at a bus stop; it crosses the road, pays at the ticket booth and enters.</summary>
	public Guest? SpawnArrival()
	{
		var lane = PickLane( laneToggle++ );
		if ( lane == null )
		{
			var start = FindSpawnCell();
			return start == null ? null : SpawnInPark( start.Value.X, start.Value.Y );
		}
		var guest = Create();
		guest.Lane = lane;
		guest.LaneIndex = 1;
		guest.LaneReversed = false;
		guest.Navigation.OffGrid = true;
		Place( guest, lane[0] );
		guest.State = GuestState.CrossingRoad;
		SetLaneWaypoint( guest );
		return guest;
	}

	/// <summary>Spawns a guest already inside the park on a walkable cell (tests, smoke, imported parks).</summary>
	public Guest SpawnInPark( int x, int y )
	{
		if ( !Grid.IsWalkable( x, y ) )
			throw new ArgumentException( $"Cell ({x}, {y}) is not a walkable path cell." );
		var guest = Create();
		PlaceOnGrid( guest, x, y );
		guest.State = GuestState.DoingNothing;
		guest.TimeInParkSeconds = 0;
		return guest;
	}

	private (int X, int Y)[]? PickLane( int index )
	{
		var a = ValidLane( Settings.ArrivalLaneA );
		var b = ValidLane( Settings.ArrivalLaneB );
		if ( a != null && b != null )
			return (index & 1) == 0 ? a : b;
		return a ?? b;
	}

	private (int X, int Y)[]? ValidLane( (int X, int Y)[]? lane ) =>
		lane != null && lane.Length >= 2 && Grid.IsWalkable( lane[^1].X, lane[^1].Y ) ? lane : null;

	private (int X, int Y)? FindSpawnCell()
	{
		for ( var y = 0; y < Grid.CountY; y++ )
			for ( var x = 0; x < Grid.CountX; x++ )
				if ( Grid.IsWalkable( x, y ) )
					return (x, y);
		return null;
	}

	private void PlaceOnGrid( Guest guest, int x, int y )
	{
		guest.OnGrid = true;
		guest.Lane = null;
		guest.CellX = x;
		guest.CellY = y;
		guest.PreviousCellX = -1;
		guest.PreviousCellY = -1;
		guest.Navigation.OffGrid = false;
		Place( guest, (x, y) );
	}

	/// <summary>The guest's sub-cell destination bytes inside a cell: its random offset around the centre, in 1/256 of a cell.</summary>
	private static (int X, int Y) OffsetBytes( Guest guest ) => ((int)((0.5f + guest.OffsetX) * 256), (int)((0.5f + guest.OffsetY) * 256));

	/// <summary>Puts the guest at its offset point in <paramref name="cell"/> without a walk; the velocity is kept.</summary>
	private void Place( Guest guest, (int X, int Y) cell )
	{
		var navigation = guest.Navigation;
		var (bx, by) = OffsetBytes( guest );
		(navigation.X, navigation.Y) = GuestNavigation.BytePoint( cell, bx, by );
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		guest.HasWaypoint = false;
		(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (guest.X, guest.Y, guest.DistanceWalked, TickCount);
		UpdatePresentation( guest );
		Occupy( guest );
	}

	public void Tick( float deltaSeconds )
	{
		if ( !float.IsFinite( deltaSeconds ) || deltaSeconds < 0 || deltaSeconds > Guest.MaximumTimeInParkSeconds )
			throw new ArgumentOutOfRangeException( nameof( deltaSeconds ) );
		// Preflight the display counter before mutating the simulation, rather than wrapping or inventing a duration.
		foreach ( var guest in guests )
			if ( CountsParkTime( guest ) && guest.TimeInParkSeconds is { } elapsed && deltaSeconds > Guest.MaximumTimeInParkSeconds - elapsed )
				throw new OverflowException( $"Guest {guest.Id} time in park exceeds the supported duration." );
		TickCount++;
		TimeSeconds += deltaSeconds;
		turnStarted = ParkCalendar.Turn( TickCount ) != ParkCalendar.Turn( TickCount - 1 );
		SyncQueues();
		UpdateArrivals( deltaSeconds );
		if ( turnStarted )
			RebuildOccupants();
		for ( var index = 0; index < guests.Count; index++ )
			UpdateGuest( guests[index], deltaSeconds );
		for ( var index = guests.Count - 1; index >= 0; index-- )
		{
			if ( guests[index].State != GuestState.Gone )
				continue;
			byId.Remove( guests[index].Id );
			guests.RemoveAt( index );
			Departed++;
		}
	}

	/// <summary>
	/// The original's arrivals: when more than <see cref="GuestSettings.ArrivalTimeBetween"/> timer units (four park turns each)
	/// have passed since the last group was dropped off, a new group of <see cref="ArrivalGroupSize"/> guests arrives; from the
	/// next park turn on one guest steps off per turn, and the timer restarts at the first turn the group is empty.
	/// </summary>
	// [BIN:STP-PPC:0x100CB2AC arrival update] if no group is active and 0x101210F8's (turn >> 2) − (start >> 2) exceeds Arrival.TimeBetweenArrivals (settings +0x2D4), the group size is set and the group starts; at each stop one guest is created and the count falls; at 0 the timer restarts (0x10121098)
	// [BIN:TPW-EXE:0x004CF3E0 arrival update] runs once per park turn at the end of the world turn update 0x00516380 (through 0x004D7B20); the call that starts a group only sends the bus (0x0051A2F0 with size 1, 2 or 3 for groups under 36, up to 60, larger); while the bus is at its stop (vehicle state 2) each call creates one guest (0x004CF720) and counts the group down, and the call that finds it empty restarts the timer (0x0041A960) and sends the bus away
	// [APPROX:ARRIVAL-001] the bus is not modelled: the first guest steps off on the turn after the group starts, where the original waits for the bus to drive to its stop (vehicle state 2, the bus animation's own time), and guests appear at the arrival lanes — evidence needed: the bus animation's length up to its stop
	private void UpdateArrivals( float deltaSeconds )
	{
		if ( !ArrivalsEnabled )
			return;
		var turn = ArrivalClock?.Invoke() ?? ParkTurn;
		// A save loaded from an earlier turn replaces the clock: the timer starts unset again, as in a freshly started park.
		if ( turn < arrivalTimerStart )
			arrivalTimerStart = 0;
		if ( !deliveringArrivals && (turn >> 2) - (arrivalTimerStart >> 2) > Settings.ArrivalTimeBetween )
		{
			pendingArrivals = ArrivalGroupSize();
			deliveringArrivals = true;
			return;   // this turn's call only sends the bus
		}
		if ( !deliveringArrivals || !turnStarted )
			return;
		if ( pendingArrivals < 1 )
		{
			deliveringArrivals = false;
			arrivalTimerStart = turn;
			return;
		}
		pendingArrivals--;
		if ( guests.Count < Settings.MaximumGuests )
			SpawnArrival();
	}

	/// <summary>
	/// Guests in the next group: 0.8 while drops are falling (see <see cref="WeatherDropsFalling"/>), else 1.2 × (park points +
	/// Arrival.NewParkBonus) / Arrival.PointsPerVisitor, at least Arrival.MinPeople, and none while the park is bankrupt; cut to
	/// the park limit (<see cref="ArrivalParkLimit"/>, <see cref="OnlineArrivalParkLimit"/> for an online visit).
	/// </summary>
	// [BIN:STP-PPC:0x100CB4E8 arrival group] 0 in world mode 4 (bankrupt); else (int)(factor (0.8f with the weather thing's +48 word, mCurrentDrops, positive; else 1.2f) × (points 0x100C24C0 + NewParkBonus +0x2DC)) / PointsPerVisitor (+0x2E0, 1 when below 2), raised to MinPeople (+0x2D0), then cut so the counted guests (0x100C1FB4) plus the group stay within 1500 (500 when the GameType word is 1)
	public int ArrivalGroupSize()
	{
		if ( ArrivalsHalted?.Invoke() == true )
			return 0;
		var perVisitor = Settings.ArrivalPointsPerVisitor < 2 ? 1 : Settings.ArrivalPointsPerVisitor;
		var factor = WeatherDropsFalling?.Invoke() == true ? 0.8f : 1.2f;
		var count = (int)(factor * (float)((ParkPoints?.Invoke() ?? 0) + Settings.ArrivalNewParkBonus)) / perVisitor;
		count = Math.Max( count, Settings.ArrivalMinPeople );
		var limit = OnlineVisit ? OnlineArrivalParkLimit : ArrivalParkLimit;
		if ( guests.Count + count > limit )
			// [APPROX:ARRIVAL-004] the cut group stops at 0; the original stores 1500 − guests as is (negative above 1500, compared unsigned; oracle F9). Executing 0xcb2ac with a negative group (oracle F11) shows the same behaviour as 0: at the next stop (phase 2) it creates no guest, restarts the timer and clears the active byte (0xcb3bc, 0xcb3f4), so nothing holds. What differs is the stored group word (negative) and the missing 'group == 0' arrival-vehicle step (0xcb488, slot 74), whose effect is not traced
			count = Math.Max( 0, limit - guests.Count );
		return count;
	}

	public bool IsInPark( Guest guest ) => InPark( guest );

	/// <summary>Whether the guest stands on a park cell: in the park and on the grid (not on the leaving lanes).</summary>
	// Approximation (ECON-044, registered at ApplyAppearance): the original's park-cell test (0x004FA990, cell types 0, 1, 3, 9 and 10) is a guest in the park by its state that stands on the grid
	public bool IsOnParkCell( Guest guest ) => InPark( guest ) && guest.OnGrid;

	private bool InPark( Guest guest ) => guest.State is not (GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth
		or GuestState.AtTicketBooth or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.Gone);

	private bool CountsParkTime( Guest guest ) => InPark( guest ) && !guest.LaneReversed;

	private void UpdateGuest( Guest guest, float dt )
	{
		// [APPROX:GUEST-920] elapsed simulated seconds begin at successful admission (seeded guests at zero), stop on road-home travel, and omit the admission tick — evidence needed: original visitor elapsed-time update and admission boundary
		if ( CountsParkTime( guest ) && guest.TimeInParkSeconds is { } elapsed )
			guest.TimeInParkSeconds = elapsed + dt;
		if ( turnStarted )
			UpdateSpeed( guest );
		if ( turnStarted )
		{
			UpdateNeedsTurn( guest );
			// The leaving test itself runs in state 6 (UpdateDeciding). The closure clause only, for the states the state-6 test never sees: a closed park also pulls guests out of queues and drops reservations, or it
			// would strand every guest whose ride holds admission (no queue state reads the closing word, and a queued guest's happiness rarely sinks below the
			// queue's leave level). Guests on a ride finish it and are sent home by the test above once they walk again.
			// [APPROX:GUEST-011] OpenTPW pulls queued and boarding guests out when the park closes; in tp.exe nothing in the queue reads the closed word (game +0x1DA710, 0x0051A280): its readers are the guest state handlers for states 2, 3, 5, 6, 10 and 18 (0x004FF730, 0x004FF7F0, 0x004FFB20, 0x004FEC90, 0x004FFBC0, 0x00500A50; the state dispatcher 0x005019F0), the strike test 0x00508E70, staff code 0x00506300, the park's average happiness 0x004C7BB0 (0 while closed), 0x004C9050 and the UI 0x00498D80, so the original's queues keep running and their guests leave only after the ride — evidence needed: whether OpenTPW's rides keep admitting in a closed park (oracle F26), which decides whether the safeguard can go
			if ( Payments?.ParkClosed == true )
			{
				if ( guest.State is GuestState.Queueing or GuestState.MovingUpQueue or GuestState.WaitingToBoard or GuestState.Boarding && FindAttraction( guest.AttractionId ) is { } closed )
				{
					LeaveQueue( guest, closed, GuestThought.None );
					StartLeaving( guest );
				}
				else if ( guest.State == GuestState.GoingToRide )
				{
					guest.AttractionId = 0;
					StartLeaving( guest );
				}
			}
			// The state-18 recall (0xedc98): the leaving handler's opening test sends a guest whose counter is above 0, happiness byte nonzero and park
			// open back to deciding, so a closed-park dismissal unwinds when the park reopens. The three leaving causes all fail it in the same turn.
			// [APPROX:GUEST-012] the original also demands the guest's cell pass 0xe6c6c (its cell record through 0x10a9b0 and 0x85190..0x851d0) — evidence needed: which cells qualify; OpenTPW approximates the predicate by the state boundary: only a guest still walking the paths to the exit (no lane yet) recalls, and the recall ends at the road (CrossingRoadHome or a lane), the stage before leaving. The booth refusal also enters LeavingPark on a lane and is exempt, like the original's zeroed counter (0xecacc)
			if ( guest.State == GuestState.LeavingPark && guest.Lane == null && GuestLifecycle.ShouldRecall( guest.Happiness, guest.ExitLevel, Payments?.ParkClosed ?? false ) )
				guest.State = GuestState.DoingNothing;
		}
		if ( InPark( guest ) )
			UpdateNeeds( guest, dt );
		UpdateState( guest, dt );
		if ( !guest.HasWaypoint && guest.State is not GuestState.Boarding )
			guest.IsMoving = false;
		UpdatePresentation( guest );
	}

	/// <summary>
	/// The needs function of one park turn (<c>0xeece4</c>, called for every guest in every state before its state handler; ORACLE.md F25, <see cref="NeedsUpdate"/>):
	/// on the guest's phase the exit counter loses 1 and the needs grow or cost happiness, the toilet speed term follows the toilet byte, and every 20th turn a 0
	/// enters the temporary history.
	/// </summary>
	// [APPROX:GUEST-006] the guest's cell environment record has all the original's sources (vomit, stinkbombs, entertainers, guards, toilets, cameras, fireworks; ParkEnvironment) and the thoughts follow tp.exe (the pick 0x4FC8A0 on one needs phase in ten, the setters of 0x50BE80, the bubble's 12 turns and its cooldown by priority), but a load puts back the row an object's saved state implies, a show loses its row when its script's VAR_STATUS reaches 1, which only the script writes in OpenTPW, the pick's one-in-ten draw is a hash of the guest and the turn rather than a draw of the world generator, and bubbles are always on where the original skips them online and while bits 0x16 of the flag word 0x790AB0 are set — evidence needed: who sets a show's VAR_STATUS and what bits 0x16 of 0x790AB0 mean
	private void UpdateNeedsTurn( Guest guest )
	{
		var turn = (uint)ParkTurn;
		var cell = Litter?.Environment.At( guest.CellX, guest.CellY ) ?? default;
		var result = NeedsUpdate.Apply( new GuestMeters( guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea ), guest.ExitLevel, turn, guest.Id,
			cell.Happiness, cell.Illness, cell.Hunger );
		(guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea) = result.Meters;
		guest.ExitLevel = result.ExitLevel;
		if ( result.ToiletSpeedTerm is { } term )
			guest.ToiletBonus = term;
		NeedsUpdate.DecayHistory( guest.TemporaryHistory, turn );
		if ( !NeedsUpdate.IsPhase( turn, guest.Id ) )
			return;
		if ( guest.State is not (GuestState.Using or GuestState.HeldByGuard) && IsOnParkCell( guest ) )
		{
			// [BIN:TPW-EXE:0x00501919 thought pick] outside states 16 and 17 and on a park cell (0x004FA990), a world draw modulo 10 of 0 runs the pick 0x004FC8A0
			if ( GuestThoughts.PickDraw( guest.Id, turn ) % GuestThoughts.PickOdds == 0
				&& GuestThoughts.Pick( guest.Hunger, guest.Thirst, guest.Toilet, guest.Nausea, guest.Happiness, MessyShare( guest ) ) is { } thought )
				Think( guest, thought );
			// [BIN:TPW-EXE:0x00501932 balloon life] then +0x214 loses 1 when it is above 0, and at 0 the balloon goes (0x004FE950)
			if ( guest.BalloonLife > 0 && --guest.BalloonLife == 0 )
				guest.HasBalloon = false;
		}
		// [BIN:TPW-EXE:0x0050BE40 bubble timer] the needs phase ends with the thought object's update: a bubble whose stamp + 12 the turn has passed goes
		if ( guest.BubbleThought != GuestThought.None && (ulong)(guest.BubbleTurn + GuestThoughts.BubbleTurns) < turn )
			guest.BubbleThought = GuestThought.None;
	}

	/// <summary>
	/// The per-update speed (<c>0xe6b28</c>, called at <c>0xeed0c</c> before the needs and the state handler): smooth s,
	/// set the caps (<c>0xffe38</c>) and decay the boost. The toilet term +194 is rewritten after it, in the needs block.
	/// </summary>
	private static void UpdateSpeed( Guest guest )
	{
		// The boost +196 is raised when a guest leaves an object whose type has the special ingredient Sugar (ApplyVisit, 0xeb0cc..0xeb0f4); here it only decays.
		(guest.SmoothedSpeed, guest.Boost, guest.MaxSpeed, guest.MaxForce) = GuestSpeed.Update( guest.SmoothedSpeed, guest.BaseSpeed, guest.ToiletBonus, guest.Boost );
	}

	/// <summary>Presentation only: the drawn position moves from the position before the last step to the current one over a park turn.</summary>
	private void UpdatePresentation( Guest guest )
	{
		var t = Math.Clamp( (TickCount - guest.StepTick) / TicksPerTurn, 0f, 1f );
		guest.DrawX = guest.StepFromX + (guest.X - guest.StepFromX) * t;
		guest.DrawY = guest.StepFromY + (guest.Y - guest.StepFromY) * t;
		guest.DrawDistanceWalked = guest.StepFromDistance + (guest.DistanceWalked - guest.StepFromDistance) * t;
	}

	/// <summary>
	/// Presentation only: where the guest was drawn <paramref name="turnsAgo"/> park turns ago along its last step, the fraction clamped to −1..2 as
	/// tp.exe <c>0x004F9F00</c> does with its flag 0 (the balloon's trailing point).
	/// </summary>
	// [BIN:TPW-EXE:0x004F9F00 position at a fraction] previous position (+0x190, +0x194) + (current − previous) × t, t clamped to 0..1 with the flag set and to −1..2 without it, in world units (cells × 10)
	public (float X, float Y) DrawPositionBefore( Guest guest, float turnsAgo )
	{
		if ( guest.StepTick < 0 )
			return (guest.X, guest.Y);
		var t = Math.Clamp( Math.Clamp( (TickCount - guest.StepTick) / TicksPerTurn, 0f, 1f ) - turnsAgo, -1f, 2f );
		return (guest.StepFromX + (guest.X - guest.StepFromX) * t, guest.StepFromY + (guest.Y - guest.StepFromY) * t);
	}

	private const float TicksPerTurn = ParkCalendar.TicksPerSecond * ParkCalendar.TurnMilliseconds / 1000f;

	private void UpdateState( Guest guest, float dt )
	{
		switch ( guest.State )
		{
			case GuestState.CrossingRoad:
			case GuestState.CrossingRoadToPark:
			case GuestState.GoingToTicketBooth:
			case GuestState.EnteringPark:
			case GuestState.CrossingRoadHome:
				UpdateLane( guest, dt );
				break;
			case GuestState.LeavingPark:
				if ( guest.Lane != null )
					UpdateLane( guest, dt );
				else
					UpdateLeaving( guest, dt );
				break;
			case GuestState.AtTicketBooth:
				guest.StateTimer -= dt;
				if ( guest.StateTimer <= 0 )
					PayAdmission( guest );
				break;
			case GuestState.WaitingToGoHome:
				guest.StateTimer -= dt;
				if ( guest.StateTimer <= 0 )
					guest.State = GuestState.Gone;
				break;
			case GuestState.WalkingAround:
				UpdateWalkingAround( guest );
				break;
			case GuestState.DoingNothing:
				UpdateDeciding( guest );
				break;
			case GuestState.ThrowingAwayLitter:
				UpdateThrowingAwayLitter( guest );
				break;
			case GuestState.GoingToRide:
				UpdateGoingToRide( guest, dt );
				break;
			case GuestState.MovingUpQueue:
			case GuestState.Boarding:
				UpdateQueueWalk( guest, dt );
				break;
			case GuestState.Queueing:
				UpdateStanding( guest );
				break;
			case GuestState.WaitingToBoard:
				if ( FindAttraction( guest.AttractionId ) == null )
					ReturnOrphanToPath( guest );
				break;
			case GuestState.ExitingRide:
				guest.StateTimer -= dt;
				if ( guest.StateTimer <= 0 )
					guest.State = GuestState.DoingNothing;
				break;
			case GuestState.HeldByGuard:
				// [BIN:TPW-EXE:0x00501C94 state 17] a held guest waits while a guard in state 18 (0x004D6350) has it as its target (+0x212); otherwise it is destroyed (0x0050B780)
				if ( turnStarted && IsEscorted?.Invoke( guest.Id ) != true )
					guest.State = GuestState.Gone;
				break;
		}
	}

	/// <summary>Whether a guard is escorting the guest with this id (a held guest without one leaves the simulation); null: nobody escorts.</summary>
	public Func<int, bool>? IsEscorted { get; set; }

	(int X, int Y)? IGuardGuests.CellOf( int guestId ) => Find( guestId ) is { State: not GuestState.Gone } guest ? guest.Cell : null;

	// [BIN:TPW-EXE:0x004FEC10 catchable] states 6, 7, 8, 9 and 10 (deciding, wandering, the activity state, throwing litter away, going to a ride)
	bool IGuardGuests.IsCatchable( int guestId ) => Find( guestId )?.State is GuestState.DoingNothing or GuestState.WalkingAround or GuestState.ThrowingAwayLitter or GuestState.GoingToRide;

	/// <summary>A guard caught the guest (tp.exe <c>0x004FEB10</c>): its balloon goes, its model is taken off the map, and it is held (state 17).</summary>
	void IGuardGuests.Hold( int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		var navigation = guest.Navigation;
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		guest.HasWaypoint = false;
		guest.IsMoving = false;
		guest.AttractionId = 0;
		guest.BinId = 0;
		// a guest caught in its activity (the original's state 8) leaves it: state 17 replaces state 8
		guest.Activity = GuestActivity.None;
		guest.State = GuestState.HeldByGuard;
		// [BIN:TPW-EXE:0x005022DA state 17 entry] a held guest's balloon bursts (0x004FE950); its life stays; the thought bubble goes (0x0050C0D0)
		guest.HasBalloon = false;
		guest.BubbleThought = GuestThought.None;
	}

	/// <summary>
	/// The guard sets the guest down on <paramref name="cell"/> (tp.exe <c>0x004FEB50</c>): the guest leaves the park (state 18) when a way out is in
	/// reach; otherwise it is removed. True when it walks out.
	/// </summary>
	// [BIN:TPW-EXE:0x004FEB50 guard sets a guest down] the guest at the cell's centre (0x004FA930), its model back (0x004D4140), cat_kids 128 at it, +0x188 = 1, a route to one of the two gate cells by a world draw's bit 0 (0x004D86D0): state 18 ("<Humph>"); no route destroys it (0x0050B780)
	// Approximation (GUEST-007, registered at Prank): the guest stands at its own offset in the cell rather than the centre and walks to the nearer arrival lane like any leaving guest, where the original routes to the gate cell its draw picked
	bool IGuardGuests.Release( int guestId, (int X, int Y) cell )
	{
		if ( Find( guestId ) is not { } guest )
			return false;
		if ( !Grid.IsWalkable( cell.X, cell.Y ) )
			cell = Grid.FindNearestWalkable( cell.X, cell.Y ) ?? cell;
		PlaceOnGrid( guest, cell.X, cell.Y );
		var reachable = new[] { ValidLane( Settings.ArrivalLaneA ), ValidLane( Settings.ArrivalLaneB ) }
			.Any( lane => lane != null && Grid.Distance( cell.X, cell.Y, lane[^1].X, lane[^1].Y ) >= 0 );
		// The leaving handler's recall (0x00500A50) applies to it like any leaving guest: with its exit counter and happiness byte above 0, the
		// park open and its cell a path, it decides again on its next turn (and clears +0x188), so a guard sends home only a guest that wants to go.
		guest.State = reachable ? GuestState.LeavingPark : GuestState.Gone;
		return reachable;
	}

	/// <summary>Whether a guest stands on the map within <paramref name="radius"/> cells of <paramref name="cell"/> (a square; tp.exe <c>0x004C8D30</c> for things of type 1).</summary>
	// [BIN:TPW-EXE:0x004C8D30 things in a square] the cells x ± r, y ± r clipped to 0..127, each cell's thing list (+0x24 of the cell record, next at +0xA); any thing whose type byte +2 is 1 (a guest) answers yes
	// Approximation (ECON-022, registered at StaffWorldSimulation.UpdateStaff): a guest on the map is one in the park that is not riding, held or gone (the original's cell lists hold the guests whose things stand on a cell)
	bool IGuardGuests.AnyGuestWithin( (int X, int Y) cell, int radius )
	{
		var x0 = Math.Max( 0, cell.X - radius );
		var x1 = Math.Min( ParkLitter.MapSize - 1, cell.X + radius );
		var y0 = Math.Max( 0, cell.Y - radius );
		var y1 = Math.Min( ParkLitter.MapSize - 1, cell.Y + radius );
		foreach ( var guest in guests )
			if ( InPark( guest ) && guest.OnGrid && guest.State is not (GuestState.Using or GuestState.HeldByGuard) && guest.CellX >= x0 && guest.CellX <= x1
				&& guest.CellY >= y0 && guest.CellY <= y1 )
				return true;
		return false;
	}

	(int X, int Y)[]? IGuardGuests.Lane( int side ) => side == 0 ? ValidLane( Settings.ArrivalLaneA ) : ValidLane( Settings.ArrivalLaneB );

	// [APPROX:GUEST-008] OpenTPW's energy meter (drained while walking, restored while resting) has no original counterpart: the original has no energy field and nothing reads this one; its rates are invented — evidence needed: none (a field the original lacks)
	private void UpdateNeeds( Guest guest, float dt )
	{
		var riding = guest.State == GuestState.Using;
		if ( riding || guest.State is GuestState.Queueing or GuestState.WaitingToBoard or GuestState.DoingNothing )
			guest.Energy = Math.Min( 100, guest.Energy + Settings.EnergyRecoveryPerSecond * dt );
		else
			guest.Energy = Math.Max( 0, guest.Energy - Settings.EnergyDrainPerSecond * dt );
		if ( !riding )
			guest.SecondsSinceRide += dt;
	}

	/// <summary>
	/// The walk step (<c>0xe6454</c>): one steering step on the tick that starts a park turn. True when the guest has
	/// arrived at its destination (tested on the position at the start of the step); the destination is then done.
	/// </summary>
	private bool Move( Guest guest )
	{
		if ( !turnStarted )
			return false;
		if ( !Step( guest ) )
			return false;
		guest.HasWaypoint = false;
		return true;
	}

	private bool Step( Guest guest )
	{
		var navigation = guest.Navigation;
		var (x, y, distance) = (guest.X, guest.Y, guest.DistanceWalked);
		stepping = navigation;
		var arrived = navigation.Step( guest.MaxSpeed, guest.MaxForce, isOpenForStep, separationForStep );
		stepping = null;
		Occupy( guest );
		(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (x, y, distance, TickCount);
		var dx = guest.X - x;
		var dy = guest.Y - y;
		var step = MathF.Sqrt( dx * dx + dy * dy );
		guest.IsMoving = step > 0;
		if ( step > 0 )
		{
			guest.HeadingX = dx / step;
			guest.HeadingY = dy / step;
			guest.DistanceWalked += step;
		}
		return arrived;
	}

	/// <summary>
	/// A cell is walkable for the stepping guest (the <c>+180</c> mask) when it is on its current walk: the route cells
	/// and, for a queue walk, the queue's next cells ahead of the destination and behind the start.
	/// </summary>
	// [APPROX:WALK-005] a cell is walkable when it is on the current walk (route and extra open cells), so path cells beside the route are walls; the original commits a step through the callback 0x510510 (at most one cell, diagonal steps through either L-shaped route) and the edge test 0x4d8750 with the guest's mask (walk +0xb4): path-to-path steps are always open, queue boundaries need the target's link back, other cell types and the masks 1 and 2 have their own rules; none of this is modelled — still open: the edge test 0x4d8750 on ParkCellMap and the mask writers are not ported
	private bool IsOpenForStep( int x, int y ) => stepping!.IsOnWalk( x, y );

	/// <summary>
	/// separation's neighbours for the stepping guest (<c>0x10087c</c>): the guests standing in the 3 × 3 cells around its
	/// cell, rows from the top and each row from the left, cells outside the map skipped (<c>0xd70d8</c>).
	/// </summary>
	// [APPROX:WALK-003] separation's neighbours are the guests outside rides and shops (not Using, HeldByGuard or Gone) in OpenTPW's cell lists, rebuilt every park turn in guest order with movers appended; the original (tp.exe query 0x5102f0, Mac 0x10087c) walks per-cell thing lists that are head-inserted when a thing enters a cell (0x50b6a0 via 0x4d91f0) and never rebuilt, keeps class-1 guests whatever their state, and also keeps staff (classes 4-8) and the online avatar (class 18); the neighbour position (thing +4/+6 = navigation position >> 8, written by 0x4fa2a0) matches OpenTPW's — still open: where the thing position of a guest on a ride, held or leaving lies
	private (int X, int Y) SeparationForStep( GuestNavigation self )
	{
		neighbours.Clear();
		var (cellX, cellY) = self.Cell;
		for ( var y = cellY - 1; y <= cellY + 1; y++ )
		{
			for ( var x = cellX - 1; x <= cellX + 1; x++ )
			{
				if ( x < 0 || y < 0 || x >= Grid.CountX || y >= Grid.CountY || !occupants.TryGetValue( (x, y), out var list ) )
					continue;
				foreach ( var other in list )
				{
					if ( other.State is not (GuestState.Using or GuestState.HeldByGuard or GuestState.Gone) )
						neighbours.Add( other.Navigation );
				}
			}
		}
		return GuestNavigation.Separation( self, neighbours );
	}

	/// <summary>Files every guest under its cell, in list order.</summary>
	private void RebuildOccupants()
	{
		foreach ( var list in occupants.Values )
			list.Clear();
		foreach ( var guest in guests )
		{
			guest.OccupiedCell = null;
			Occupy( guest );
		}
	}

	/// <summary>Moves a guest to the end of its current cell's list when it changed cell.</summary>
	private void Occupy( Guest guest )
	{
		var cell = guest.Navigation.Cell;
		if ( guest.OccupiedCell == cell )
			return;
		if ( guest.OccupiedCell is { } previous && occupants.TryGetValue( previous, out var old ) )
			old.Remove( guest );
		if ( !occupants.TryGetValue( cell, out var list ) )
			occupants[cell] = list = new List<Guest>();
		list.Add( guest );
		guest.OccupiedCell = cell;
	}

	/// <summary>Starts a walk to <paramref name="target"/> along <paramref name="cells"/>; the route starts at the guest's own cell.</summary>
	private static void SetDestination( Guest guest, (int X, int Y) target, List<(int X, int Y)> cells, List<(int X, int Y)>? alsoOpen = null )
	{
		var navigation = guest.Navigation;
		var route = new List<(int X, int Y)>( cells.Count + 1 ) { navigation.Cell };
		foreach ( var cell in cells )
		{
			if ( route[^1] != cell )
				route.Add( cell );
		}
		navigation.SetDestination( target.X, target.Y, route, alsoOpen );
		guest.HasWaypoint = true;
	}

	/// <summary>The next path cell: a destination at the guest's offset point in it, routed through the current cell.</summary>
	private void SetCellWaypoint( Guest guest, int x, int y )
	{
		var current = (guest.CellX, guest.CellY);
		guest.PreviousCellX = guest.CellX;
		guest.PreviousCellY = guest.CellY;
		guest.CellX = x;
		guest.CellY = y;
		var (bx, by) = OffsetBytes( guest );
		SetDestination( guest, GuestNavigation.BytePoint( (x, y), bx, by ), [current, (x, y)] );
	}

	private void SetLaneWaypoint( Guest guest )
	{
		var cell = guest.Lane![guest.LaneIndex];
		var (bx, by) = OffsetBytes( guest );
		SetDestination( guest, GuestNavigation.BytePoint( cell, bx, by ), [cell] );
	}

	// Arrival lane: [bus stop, crossing (bus side), crossing (park side), ticket booth, entrance].
	private void UpdateLane( Guest guest, float dt )
	{
		if ( !Move( guest ) )
			return;
		var lane = guest.Lane!;
		if ( !guest.LaneReversed )
		{
			if ( guest.LaneIndex == lane.Length - 1 )
			{
				PlaceOnGrid( guest, lane[^1].X, lane[^1].Y );
				guest.State = GuestState.DoingNothing;
				return;
			}
			if ( guest.LaneIndex == lane.Length - 2 && guest.State != GuestState.EnteringPark )
			{
				guest.State = GuestState.AtTicketBooth;
				guest.StateTimer = TicketBoothSeconds;
				return;
			}
			guest.LaneIndex++;
			guest.State = guest.LaneIndex switch
			{
				1 => GuestState.CrossingRoad,
				2 => GuestState.CrossingRoadToPark,
				_ when guest.LaneIndex == lane.Length - 1 => GuestState.EnteringPark,
				_ => GuestState.GoingToTicketBooth
			};
			SetLaneWaypoint( guest );
			return;
		}
		if ( guest.LaneIndex == 0 )
		{
			guest.State = GuestState.WaitingToGoHome;
			guest.StateTimer = WaitForBusSeconds;
			return;
		}
		guest.LaneIndex--;
		guest.State = guest.LaneIndex <= 1 ? GuestState.CrossingRoadHome : GuestState.LeavingPark;
		SetLaneWaypoint( guest );
	}

	/// <summary>
	/// The booth (state 4, <c>0x004FF9D0</c>): the verdict on the entrance fee (<see cref="BoothVerdict"/>) sends a guest home when the park is far too
	/// expensive, keeps one that finds it expensive at the booth for 50 + R % 50 park turns with a medium happiness loss (home once its happiness
	/// truncates to 0), and lets the others pay, a cheap park with a medium happiness gain.
	/// </summary>
	// [BIN:TPW-EXE:0x4ff9d0 booth] verdict 0: thought 6 and home (state 18); 1: +0x200 = 50 + R % 50 (counted down one per update before the verdict runs again), thought 6, 0x4fea70(1), home when the happiness byte is 0; 2: 0x4fe9c0(1), then 3: the fee is booked (0x4d0600) and the guest enters (+0x204 = 1, state 3)
	private void PayAdmission( Guest guest )
	{
		if ( Payments?.ParkClosed != true )
		{
			var verdict = BoothVerdict.Decide( (uint)Math.Max( 0, ParkPoints?.Invoke() ?? 0 ), (uint)Math.Max( 0, AdmissionFee ), random.NextWord(), Settings );
			if ( verdict == BoothVerdictKind.FarTooExpensive )
			{
				Think( guest, GuestThought.Dissatisfied );
				StartLeavingLane( guest, guest.Lane!, guest.LaneIndex );
				return;
			}
			if ( verdict == BoothVerdictKind.Expensive )
			{
				var wait = BoothVerdict.ExpensiveWaitBase + random.NextWord() % BoothVerdict.ExpensiveWaitBase;
				Think( guest, GuestThought.Dissatisfied );
				guest.Happiness = MeterMath.Add( guest.Happiness, -Settings.MediumHappinessChange );
				if ( ((int)guest.Happiness & 0xFF) != 0 )
				{
					// [APPROX:ARRIVAL-006] the wait counts park turns of the booth's fixed clock (248 ms each) rather than the guest updates of state 4, and the draws come from the guest stream (DET-014)
					guest.StateTimer = wait * ParkCalendar.TurnMilliseconds / 1000f;
					return;
				}
				StartLeavingLane( guest, guest.Lane!, guest.LaneIndex );
				return;
			}
			if ( verdict == BoothVerdictKind.Cheap )
				guest.Happiness = MeterMath.Add( guest.Happiness, Settings.MediumHappinessChange );
		}
		int fee;
		if ( Payments != null ? !Payments.TryPayAdmission( guest.Money, out fee ) : guest.Money < (fee = AdmissionFee) )
		{
			// A closed park, or a guest whose purse is short of the fee, turns back with thought 6 as the booth's refusals do.
			// [APPROX:ARRIVAL-007] OpenTPW charges the fee from the guest's purse and turns back a guest who cannot pay; the original's booth books the fee (0x004D0600) without reading the guest's cash — evidence needed: whether anything takes the fee from the guest's cash
			Think( guest, GuestThought.Dissatisfied );
			StartLeavingLane( guest, guest.Lane!, guest.LaneIndex );
			return;
		}
		guest.Money -= fee;
		guest.TimeInParkSeconds = 0;
		Admissions++;
		if ( fee > 0 )
			MoneySpent?.Invoke( guest, fee, 0 );
		guest.LaneIndex = guest.Lane!.Length - 1;
		guest.State = GuestState.EnteringPark;
		SetLaneWaypoint( guest );
	}

	private void StartLeavingLane( Guest guest, (int X, int Y)[] lane, int fromIndex )
	{
		guest.OnGrid = false;
		guest.Navigation.OffGrid = true;
		guest.Lane = lane;
		guest.LaneReversed = true;
		guest.LaneIndex = Math.Max( 0, fromIndex - 1 );
		guest.State = guest.LaneIndex <= 1 ? GuestState.CrossingRoadHome : GuestState.LeavingPark;
		SetLaneWaypoint( guest );
	}

	/// <summary>
	/// State 6 (<c>0x004FEC90</c>), once per park turn: the tests of <see cref="GuestDecision"/> in the original order. A guest in an
	/// activity (the original's state 8) waits it out instead.
	/// </summary>
	private void UpdateDeciding( Guest guest )
	{
		if ( !turnStarted )
			return;
		var turn = (uint)ParkTurn;
		if ( guest.Activity != GuestActivity.None )
		{
			// [BIN:TPW-EXE:0x00501D26 state 8] the activity ends (0x004FC890, back to the state saved at +0x224) once the turn is above +0x208 + 10 (unsigned)
			if ( turn > unchecked((uint)guest.InterludeTurn + GuestDecision.ActivityTurns) )
				guest.Activity = GuestActivity.None;
			return;
		}
		IRideVisitorBridge? choice = null;
		var input = new GuestDecision.Input( turn, (uint)guest.InterludeTurn, (uint)guest.QueueStandingSinceTurn, guest.Happiness, guest.Nausea, guest.Litter,
			guest.ExitLevel, Payments?.ParkClosed ?? false, random.NextWord(), guest.PrankFlag, Settings.StinkbombLikelihood );
		var outcome = GuestDecision.Decide( input, () =>
		{
			if ( SeekBin( guest ) )
				return true;
			DropLitter( guest, (LitterType)(1 + random.NextWord() % ParkLitter.OrdinaryKinds) );
			guest.Litter = 0;
			return false;
		}, () =>
		{
			guest.Happiness = MeterMath.Add( guest.Happiness, -Settings.BigHappinessChange );
			if ( !HasWayOut( guest ) )
				return false;
			guest.State = GuestState.LeavingPark;
			return true;
		}, ( prank, type ) => Prank( guest, prank, type ), () =>
		{
			choice = ChooseAttraction( guest );
			return choice != null;
		}, () => Wander( guest ), () => guest.Happiness );
		switch ( outcome )
		{
			case GuestDecision.Outcome.Cheer:
				StartActivity( guest, GuestActivity.Cheering );
				break;
			case GuestDecision.Outcome.Vomit:
				StartActivity( guest, GuestActivity.Vomiting );
				DropLitter( guest, LitterType.Vomit );
				guest.Nausea = 0;
				break;
			case GuestDecision.Outcome.ToBin:
				break;
			case GuestDecision.Outcome.GoToRide:
				guest.AttractionId = choice!.AttractionId;
				guest.State = GuestState.GoingToRide;
				break;
			case GuestDecision.Outcome.CantDecide:
				StartActivity( guest, GuestActivity.CantDecide );
				guest.Happiness = MeterMath.Add( guest.Happiness, -Settings.SmallHappinessChange );
				guest.QueueStandingSinceTurn = turn;
				break;
			case GuestDecision.Outcome.Wander:
				guest.State = GuestState.WalkingAround;
				break;
			case GuestDecision.Outcome.WanderFailed:
				guest.QueueStandingSinceTurn = turn;
				break;
		}
	}

	/// <summary>
	/// True when a path leads from the guest's cell to the end of an arrival lane, the way out <see cref="UpdateLeaving"/> takes. A park
	/// without arrival lanes has no modelled gate: its guests leave and vanish, as <see cref="UpdateLeaving"/> handles it.
	/// </summary>
	private bool HasWayOut( Guest guest )
	{
		var lanes = new[] { ValidLane( Settings.ArrivalLaneA ), ValidLane( Settings.ArrivalLaneB ) };
		if ( lanes.All( lane => lane == null ) )
			return true;
		foreach ( var lane in lanes )
		{
			if ( lane != null && Grid.Distance( guest.CellX, guest.CellY, lane[^1].X, lane[^1].Y ) >= 0 )
				return true;
		}
		return false;
	}

	/// <summary>An activity (<c>0x004FC800</c>): stamps +0x208 with the turn and holds the guest in state 8 for ten park turns.</summary>
	private void StartActivity( Guest guest, GuestActivity activity )
	{
		guest.Activity = activity;
		guest.InterludeTurn = ParkTurn;
	}

	/// <summary>
	/// State 7 (the dispatcher's case 7, <c>0x00501ABB</c>): one walk step per park turn. On arrival a draw with <c>rand &amp; 3 == 0</c> may
	/// pick the next wander destination and keep walking; otherwise the guest decides again (state 6).
	/// </summary>
	// [BIN:TPW-EXE:0x00501ABB state 7] the walk step 0x004FA2A0 returns 0 (arrived), 1 (walking) or 2 (stuck); on 0 or 2 one draw, and when its low two bits are 0 the wander destination 0x004F9490 keeps state 7 on success; every other case sets state 6
	// [APPROX:GUEST-010] OpenTPW's walk has no stuck result (its steering reroutes, WALK-I): a walk ends on arrival, or as stuck when a grid edit takes a cell of its route (EndBrokenWalks)
	private void UpdateWalkingAround( Guest guest )
	{
		if ( !turnStarted )
			return;
		if ( guest.HasWaypoint && !MoveAlong( guest ) )
			return;
		if ( (random.NextWord() & 3) == 0 && Wander( guest ) )
			return;
		guest.State = GuestState.DoingNothing;
	}

	/// <summary>
	/// One walk step of a wander or a walk to a bin; true on arrival. The guest's cell is the one it stands on (the original's thing cell
	/// +5/+7), which the next wander starts from and litter falls on; arriving within reach of a point near a cell's edge can leave it in the
	/// cell before.
	/// </summary>
	private bool MoveAlong( Guest guest )
	{
		var arrived = Move( guest );
		var cell = guest.Navigation.Cell;
		if ( cell != (guest.CellX, guest.CellY) && Grid.IsWalkable( cell.X, cell.Y ) )
		{
			(guest.PreviousCellX, guest.PreviousCellY) = (guest.CellX, guest.CellY);
			(guest.CellX, guest.CellY) = cell;
		}
		return arrived;
	}

	/// <summary>
	/// The bin search (tp.exe <c>0x00500DC0</c>): the nearest bin, by squared distance in cells below <see cref="BinRadius"/>², that a route
	/// reaches; the guest then walks to the cell in front of it (state 9). False when there is none.
	/// </summary>
	// [BIN:TPW-EXE:0x00500DC0 bin search] the object list (game +0x1DA746, next at +0xC) in order; objects with flag +0x32 bit 0x40 (UsageInfo.HoldsLitter, type record +0xF8); dx² + dy² from the guest's cell to the object's below 9 and strictly below the best so far; the route test 0x004FA530 to the object's access cell +0x36 keeps it
	// [APPROX:GUEST-005] the route test of an unreachable but closer bin also overwrites the guest's destination words in the original, which a later reachable candidate's test sets again; OpenTPW keeps the reachable bin's route — evidence needed: none (read); the overwrite matters only when the last bin tried is unreachable
	private bool SeekBin( Guest guest )
	{
		if ( Bins?.Invoke() is not { Count: > 0 } bins )
			return false;
		LitterBin? best = null;
		List<(int X, int Y)>? bestRoute = null;
		var bestDistance = BinRadius * BinRadius;
		foreach ( var bin in bins )
		{
			var dx = bin.Cell.X - guest.CellX;
			var dy = bin.Cell.Y - guest.CellY;
			var distance = dx * dx + dy * dy;
			if ( distance >= bestDistance )
				continue;
			var route = Grid.FindPath( guest.CellX, guest.CellY, bin.Access.X, bin.Access.Y );
			if ( route.Count == 0 )
				continue;
			(best, bestRoute, bestDistance) = (bin, route, distance);
		}
		if ( best is not { } found )
			return false;
		guest.BinId = found.Id;
		SetDestination( guest, GuestNavigation.BytePoint( found.Access, GuestWander.CentreByte, GuestWander.CentreByte ), bestRoute! );
		guest.State = GuestState.ThrowingAwayLitter;
		return true;
	}

	/// <summary>
	/// State 9 (tp.exe <c>0x004FFF20</c>): one walk step per park turn towards the bin. On arrival at a bin the litter goes in; a walk that ends
	/// stuck drops the litter on the guest's cell instead (<c>1 + rand % 5</c>). Either way the meter empties and the guest decides again.
	/// </summary>
	// [BIN:TPW-EXE:0x004FFF20 state 9] the walk step 0x004FA2A0: 1 keeps walking; 0 with a bin triggers the bin's script (Level.TriggerBin); 2, or a bin id of 0, drops 1 + rand % 5 on the current cell (0x004D93B0); then the meter +0x1B4 = 0 and state 6
	// [APPROX:GUEST-005] a bin removed during the walk counts as no bin (the litter falls on the cell), where the original still triggers the script of the object id it holds; the walk ends stuck only when a grid edit takes a cell or link of its route (EndBrokenWalks) — evidence needed: none (read)
	private void UpdateThrowingAwayLitter( Guest guest )
	{
		if ( !turnStarted )
			return;
		var arrived = false;
		if ( guest.HasWaypoint && !(arrived = MoveAlong( guest )) )
			return;
		if ( arrived && guest.BinId != 0 && Bins?.Invoke() is { } bins && bins.Any( bin => bin.Id == guest.BinId ) )
			LitterBinned?.Invoke( guest, guest.BinId );
		else
			DropLitter( guest, (LitterType)(1 + random.NextWord() % ParkLitter.OrdinaryKinds) );
		guest.Litter = 0;
		guest.BinId = 0;
		guest.State = GuestState.DoingNothing;
	}

	/// <summary>
	/// A prank of an unhappy prankster (state 6, tp.exe <c>0x004FF0F5</c>): a stinkbomb or litter on its own cell, or a balloon burst; a prank raises
	/// <see cref="Pranked"/> (the original's prank message to the advisor and the guards) and adds 1 to the prankster's happiness.
	/// </summary>
	// [APPROX:GUEST-007] PRANKS: pranksters prank as in tp.exe (0x4FF0F5, x86 X18) and a prank on a cell with security sends a guard (0x508A30); a balloon prankster's victim is the first other guest on the cell in OpenTPW's guest order rather than the cell's thing list; the prank message reaches no advisor line and the guest event 15 is not recorded; a guest the guard sets down stands at its own offset and walks to the nearer arrival lane rather than the gate cell its draw picked — evidence needed: none (read); waits for the advisor's prank lines and the cell thing lists
	private bool Prank( Guest guest, GuestDecision.Prank prank, LitterType type )
	{
		if ( prank == GuestDecision.Prank.Balloon )
		{
			// [BIN:TPW-EXE:0x004FF1CD balloon prank] the first other guest (thing type 1) on the prankster's cell list whose +0x210 is set has its balloon burst (0x004FE950); without one there is no prank
			var victim = guests.FirstOrDefault( other => other != guest && other.HasBalloon && other.OnGrid && other.Cell == guest.Cell
				&& other.State is not (GuestState.Using or GuestState.HeldByGuard or GuestState.Gone) );
			if ( victim == null )
				return false;
			victim.HasBalloon = false;
		}
		else
			// A littering prank counts even when the cell already holds litter (the drop is lost; tp.exe sets the prank flag after 0x004D93B0 either way).
			DropLitter( guest, type );
		Pranked?.Invoke( guest, prank );
		guest.Happiness = MeterMath.Add( guest.Happiness, 1f );
		return true;
	}

	/// <summary>A prankster pranked (the stinkbomb's sound, the advisor and the guards listen to the original's prank message).</summary>
	public event Action<Guest, GuestDecision.Prank>? Pranked;

	/// <summary>Litter of <paramref name="type"/> on the guest's cell (lost when the cell already holds some).</summary>
	private void DropLitter( Guest guest, LitterType type ) => Litter?.Drop( guest.CellX, guest.CellY, type );

	/// <summary>
	/// After a grid edit, a wander walk whose route ahead lost a path cell or a link ends where the guest stands, as the original's walk
	/// step reports "stuck" (2); the guest then picks again or decides at its next turn (state 7). A guest left off the paths is put on the
	/// nearest one.
	/// </summary>
	private void EndBrokenWalks()
	{
		foreach ( var guest in guests )
		{
			if ( guest.State is not (GuestState.WalkingAround or GuestState.ThrowingAwayLitter) || !guest.HasWaypoint || RouteAheadHolds( guest.Navigation ) )
				continue;
			var navigation = guest.Navigation;
			navigation.SetDestination( navigation.X, navigation.Y, [] );
			guest.HasWaypoint = false;
			var cell = navigation.Cell;
			if ( Grid.IsWalkable( cell.X, cell.Y ) )
				(guest.CellX, guest.CellY) = cell;
			else
			{
				var (x, y) = Grid.FindNearestWalkable( cell.X, cell.Y ) ?? cell;
				PlaceOnGrid( guest, x, y );
			}
		}
	}

	/// <summary>True when the route from the guest's cell on is still path cells, each linked to the next (a wander route is a shortest path, so no cell repeats).</summary>
	private bool RouteAheadHolds( GuestNavigation navigation )
	{
		var route = navigation.Route;
		var from = Math.Max( 0, Array.IndexOf( route, navigation.Cell ) );
		for ( var index = from; index < route.Length; index++ )
		{
			var (x, y) = route[index];
			if ( !Grid.IsWalkable( x, y ) )
				return false;
			if ( index + 1 < route.Length && GuestPathGrid.DirectionBetween( x, y, route[index + 1].X, route[index + 1].Y ) is var direction
				&& (direction < 0 || !Grid.AreConnected( x, y, direction )) )
				return false;
		}
		return true;
	}

	/// <summary>Sets a wander destination (<see cref="GuestWander"/>) and the shortest route to it; false when there is none.</summary>
	private bool Wander( Guest guest )
	{
		var start = (X: guest.CellX, Y: guest.CellY);
		if ( GuestWander.Pick( Grid, start, () => random.NextWord() ) is not { } destination )
			return false;
		var route = Grid.FindPath( start.X, start.Y, destination.Cell.X, destination.Cell.Y );
		if ( route.Count == 0 )
			return false;
		SetDestination( guest, GuestNavigation.BytePoint( destination.Cell, destination.ByteX, destination.ByteY ), route );
		return true;
	}

	/// <summary>
	/// The guest thinks <paramref name="thought"/> (tp.exe <c>0x0050BE80</c>): the thought is stored, and a bubble shows it when the last one is old enough
	/// for the new thought's priority (<see cref="GuestThoughts.TakesBubble"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x0050BE80 thought] +0 = the thought; with bubbles on, the old bubble goes (0x00475550) and, when the turn has reached +0x8C + 20 × priority, a new one starts and +0x8C is stamped
	// Approximation (GUEST-006, registered at UpdateNeedsTurn): bubbles are always on; the original skips them in an online game (GameType 1) and while bits 0x16 of the flag word 0x00790AB0 are set, whose meaning is untraced
	private void Think( Guest guest, GuestThought thought )
	{
		guest.Thought = thought;
		guest.BubbleThought = GuestThought.None;
		if ( !GuestThoughts.TakesBubble( (int)thought, ParkTurn, guest.BubbleTurn ) )
			return;
		guest.BubbleThought = thought;
		guest.BubbleTurn = ParkTurn;
	}

	/// <summary>The share of littered path cells in the guest's 4 × 4 analyser block (tp.exe <c>0x004C8BA0</c> over <c>0x004C8AE0</c>).</summary>
	// [BIN:TPW-EXE:0x004C8AE0 block census] the block of the guest's cell (0x004C7B70: 32 × 32 blocks of 4 × 4 cells) counts its littered cells and its cells of type 1, 3 or 9 (0x00536310, 0x00536320); the ratio is a single division
	// Approximation (ECON-020, registered at ParkLitter.Drop): the counts are live where the analyser caches them for a few turns
	private float MessyShare( Guest guest )
	{
		if ( Litter is not { } litter || Grid.Cells is not { } cells )
			return 0f;
		int x0 = guest.CellX & ~3, y0 = guest.CellY & ~3, littered = 0, paths = 0;
		for ( var x = x0; x < x0 + 4; x++ )
			for ( var y = y0; y < y0 + 4; y++ )
			{
				if ( litter.TypeAt( x, y ) != LitterType.None )
					littered++;
				if ( cells.RawTypeAt( x, y ) is 1 or 3 or 9 )
					paths++;
			}
		return paths == 0 ? (littered == 0 ? 0f : float.PositiveInfinity) : (float)littered / paths;
	}

	/// <summary>
	/// A guest heads home (<c>0xebe30</c>): it loses <c>PeepInfo.BigHappinessChange</c> (<c>0xeb6f4</c> with selector 2) and goes to state 18.
	/// </summary>
	// [BIN:STP-PPC:0x100EBE30 leaving] 0xeb6f4(guest, 2) subtracts the settings word +32 from happiness (clamped), then 0xef700(guest, 18)
	private void StartLeaving( Guest guest )
	{
		guest.Happiness = MeterMath.Add( guest.Happiness, -Settings.BigHappinessChange );
		guest.State = GuestState.LeavingPark;
	}

	/// <summary>
	/// The original's attraction score (<c>0xe9164</c>, docs/reverse/ORACLE.md F24): distance, queue, excitement, thirst, hunger, toilet and illness
	/// matches normalised by the sum of all seven weights, scaled for new rides, indoor rides in rain and costly rides, and divided down by the
	/// guest's history (see <see cref="AttractionScore"/>). Unsigned, like the original; 0 for an attraction without a back-of-queue cell.
	/// </summary>
	// [BIN:STP-PPC:0x100E9164 attraction score] executed by the PowerPC oracle (attraction-score, 578 inputs; C# agrees)
	// [BIN:STP-PPC:0x100E9388 cell modifier] the environment record's component 4 (+8) at the back-of-queue cell divides the distance match; only the fireworks' RegionFX row 7 writes it (Attraction 10 in the shipped Standard.sam; tp.exe 0x004DB48C), so it is 0 everywhere else
	// [APPROX:GUEST-002] the attraction's back-of-queue cell (0xdd3f4) is the bridge's last queue cell and its queue count the guests in OpenTPW's queue list; a back cell that is not connected to a path (no join cell) is scored 0; the type's own thing-array order, tie rule on equal scores and the thing behind ride +36 follow the join gate's approximations (QUEUE-004)
	public uint ScoreAttraction( Guest guest, IRideVisitorBridge attraction )
	{
		if ( attraction.JoinCell == null )
			return 0;
		var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
		var sameType = guest.UsedHistory[0] != 0 && FindAttraction( guest.UsedHistory[0] ) is { } last && last.TypeId == attraction.TypeId;
		var back = attraction.QueueCells[^1];
		var modifier = Litter?.Environment.At( back.X, back.Y ).Attraction ?? 0;
		var input = new AttractionScoreInput( guest.CellX, guest.CellY, back.X, back.Y, modifier, attraction.QueueLength, attraction.QueueSizeInCells,
			type.PreferredExcitement, attraction.LiveExcitement, (attraction.StaticExcitementLevel & 0xFF) != 0, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea,
			attraction.ThirstEffect, attraction.HungerEffect, attraction.ProvidesRelief, attraction.AgeDays, WeatherDropsFalling?.Invoke() ?? false, attraction.IsIndoors,
			attraction.GoldenTicketCost, attraction.UpgradeCost, attraction.AttractionId, sameType, guest.UsedHistory, guest.TemporaryHistory );
		return AttractionScore.Compute( input, Settings.DecisionWeights, Settings.DecisionVariables );
	}

	/// <summary>
	/// The major decision (<c>0xe8ff4</c>): every attraction in list order is scored; a score of 10 or more that beats the best so far, or ties it on an
	/// odd park turn, wins if the attraction is open and not broken and the guest can reach it. There is no random bonus.
	/// </summary>
	// [BIN:STP-PPC:0x100E8FF4 choice loop] executed by the PowerPC oracle (attraction-choice); the tie rule reads the world turn's bit 0 (game +0x1da70c)
	// [BIN:TPW-EXE:0x4dd920 counts-test] the candidate needs flag +0x32 bit 4, a state other than 1 and 4 (+0x19c), the open word (+0x68), a back cell, queue room below 4 × cells (+0x40) and the type checks; VAR_BROKEN is not read, so a ride whose script just reported a breakdown is chosen until its ride update enters state 1
	// [BIN:TPW-EXE:0x004DD920 counts-test room] the candidate's queue list count (0x004DDF50 with id 0) must be below 4 × its cells (+0x40, from the back-of-queue walk 0x004DE130); the coaster test 0x00441970 and the kart/water track word +0x2C are applied through IsOpen (CoasterRide.AcceptsGuests, BumpController.IsTrackValid; the edited-coaster exclusion is COAST-024)
	// [BIN:TPW-EXE:0x004FCB10 choice loop] no price or money test in the choice: a candidate needs flag +0x32 bit 4, a back-of-queue cell, a score above 9 that beats the best (ties on an odd turn), the counts-test 0x004DD920 and a route; the price is tested when the guest is called forward (0x004FDE50)
	// [BIN:TPW-EXE:0x4ff419 major decision] once the turn is above +0x1FC + 30 the choice 0x4fcb10 runs; with no target the guest logs event 1, thinks 4, loses the small step (0x4fea70(0)) and is stamped; nothing in it tests time since a ride, and no instruction reads PeepTypes' BoredomThreshold (0x7850EC + 12 × type has no reference; excitement 0x7850E4 and cash 0x7850E8 have)
	private IRideVisitorBridge? ChooseAttraction( Guest guest )
	{
		IEnumerable<AttractionChoice.Candidate> Candidates()
		{
			foreach ( var attraction in attractions )
			{
				var canJoin = attraction.JoinCell != null;
				yield return new AttractionChoice.Candidate( attraction.AttractionId, true, canJoin, canJoin ? ScoreAttraction( guest, attraction ) : 0,
					() => attraction.IsOpen && !attraction.IsBrokenDown && attraction.QueueLength < RideVisitorBridge.PositionsPerCell * attraction.QueueSizeInCells
						&& attraction.JoinCell is { } join && Grid.Distance( guest.CellX, guest.CellY, join.X, join.Y ) >= 0 );
			}
		}
		var id = AttractionChoice.Pick( Candidates(), ParkTurn );
		return id == 0 ? null : FindAttraction( id );
	}

	private IRideVisitorBridge? FindAttraction( int id )
	{
		foreach ( var attraction in attractions )
			if ( attraction.AttractionId == id )
				return attraction;
		return null;
	}

	private void UpdateGoingToRide( Guest guest, float dt )
	{
		if ( guest.HasWaypoint && !Move( guest ) )
			return;
		var attraction = FindAttraction( guest.AttractionId );
		// [BIN:TPW-EXE:0x004FFBC0 walk to the queue] state 10 reads neither the ride's state nor its open word: a guest keeps walking to a ride closed or broken meanwhile, joins, and the ride sends it away from the queue head (RideVisitorBridge.HostStep)
		if ( attraction == null || !attraction.AcceptsJoins )
		{
			guest.AttractionId = 0;
			guest.State = GuestState.DoingNothing;
			return;
		}
		if ( attraction.JoinCell is not { } join )
		{
			Think( guest, GuestThought.Confused );
			guest.AttractionId = 0;
			guest.State = GuestState.DoingNothing;
			return;
		}
		var (tx, ty) = join;
		if ( guest.CellX == tx && guest.CellY == ty )
		{
			var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
			var result = attraction.JoinQueue( guest.Id, type.PreferredExcitement );
			if ( result == QueueJoinResult.Joined )
			{
				BeginQueue( guest, attraction );
				return;
			}
			// [BIN:TPW-EXE:0x004FFCD7 join refusal] too exciting: 15, not exciting enough: 12, a queue too long: 16 (0x004FFD7E)
			Think( guest, result switch
			{
				QueueJoinResult.TooExciting => GuestThought.Scared,
				QueueJoinResult.NotExcitingEnough => GuestThought.Bored,
				_ => GuestThought.QueueTooLong
			} );
			if ( result is QueueJoinResult.NoRoom or QueueJoinResult.QueueTooLong )
				guest.Happiness = Math.Max( 0, guest.Happiness - Settings.SmallHappinessChange );
			// [BIN:STP-PPC:0x100ECE18 refusal history] the queue-too-long path (0xece18) and both excitement refusals (0xecf3c) push the target's id into the temporary history (0xea290) before clearing the target
			// [APPROX:GUEST-004] a full queue room (NoRoom, count ≥ 4 × queue cells) is treated like the original's queue-too-long refusal for the thought and the happiness loss, but pushes no history: its original path (0xdc988 then the dismissal 0xee804) pushes none — evidence needed: where a guest meets a physically full queue in state 10
			if ( result is QueueJoinResult.QueueTooLong or QueueJoinResult.NotExcitingEnough or QueueJoinResult.TooExciting )
				PushHistory( guest.TemporaryHistory, attraction.AttractionId );
			// [BIN:TPW-EXE:0x004FFCF6 excitement refusal] the refusal clears the target and the decision stamp +0x1FC, so the next major decision is not held back 30 turns
			if ( result is QueueJoinResult.NotExcitingEnough or QueueJoinResult.TooExciting )
				guest.QueueStandingSinceTurn = 0;
			guest.LastAttractionId = attraction.AttractionId;
			guest.AttractionId = 0;
			guest.State = GuestState.DoingNothing;
			return;
		}
		if ( Grid.TryStep( guest.CellX, guest.CellY, tx, ty, out var nx, out var ny ) )
			SetCellWaypoint( guest, nx, ny );
		else
		{
			Think( guest, GuestThought.Confused );
			guest.AttractionId = 0;
			guest.State = GuestState.DoingNothing;
		}
	}

	// ---- Queues (docs/reverse/QUEUE-plan.md §4–6) ----------------------------------------------

	/// <summary>Brings every attraction's queue up to date after grid edits and re-evaluates queued guests after queue edits.</summary>
	private void SyncQueues()
	{
		if ( Grid.Version != seenGridVersion )
		{
			seenGridVersion = Grid.Version;
			foreach ( var attraction in attractions )
				attraction.RecomputeQueue( Grid );
			EndBrokenWalks();
		}
		foreach ( var attraction in attractions )
		{
			if ( seenQueueEdits.TryGetValue( attraction.AttractionId, out var seen ) && seen == attraction.QueueEditCount )
				continue;
			seenQueueEdits[attraction.AttractionId] = attraction.QueueEditCount;
			ReevaluateQueue( attraction );
		}
	}

	/// <summary>
	/// "Queue edited" (<c>0xdd57c</c>): every queued guest except the called one re-evaluates. A guest whose place
	/// is beyond the new room leaves ("queue was shortened"), unless it is already waiting to be taken (state 14);
	/// the others walk to their position again.
	/// </summary>
	// [BIN:TPW-EXE:0x00501390 queue re-evaluation] the only exit is position ≥ 4 × cells (unsigned) with the guest not in state 14; "Queue edited underneath me while Standing In Queue" is the entry log every guest prints, not an exit; the caller 0x004DE1F0 visits every guest in the queue list but the called one (ride +0x6C); executed by the x86 oracle target tpw-exe-queue-reevaluate (ORACLE.md X14)
	private void ReevaluateQueue( IRideVisitorBridge attraction )
	{
		var cells = attraction.QueueCells;
		var room = RideVisitorBridge.PositionsPerCell * attraction.QueueSizeInCells;
		foreach ( var guest in guests )
		{
			if ( guest.AttractionId != attraction.AttractionId || guest.State is not (GuestState.Queueing or GuestState.MovingUpQueue or GuestState.Boarding) || guest.Id == attraction.CalledGuest )
				continue;
			var position = attraction.GetQueuePosition( guest.Id );
			// a guest missing from the list cannot occur in the original (0x004DDF50 asserts); OpenTPW sends it away
			if ( position < 0 || position >= room )
			{
				// [BIN:TPW-EXE:0x00501496 queue edited] before the dismissal a guest whose thing id is a multiple of 3 thinks 13 (Angry); the others think nothing
				DismissFromQueue( guest, attraction, guest.Id % 3 == 0 ? GuestThought.Angry : GuestThought.None );
				continue;
			}
			if ( guest.State == GuestState.Boarding )
				continue;   // a boarding guest keeps its walk to the stand point
			var index = IndexOfCell( cells, guest.CellX, guest.CellY );
			var onJoinCell = attraction.JoinCell is { } join && join == (guest.CellX, guest.CellY);
			// [APPROX:QUEUE-005] the original changes nothing for a guest that stays (0x00501390 only logs): state 11 (0x004FFFF0) later re-routes a guest whose place changed through 0x00501160 and the generic route 0x004FA5F0; OpenTPW's queue walk cannot route a guest standing off the queue back, so such a guest leaves (Confused) and the guests that stay are given their walk again. A leaving guest thinks Angry (thought 13) for one guest in three (id % 3 = 0, 0x00501496) by OpenTPW's guest id rather than the thing id (the word at thing +0, 0x0050B350), and its dismissal 0x005012F0 records event 6 with the ride id in the guest's own event history (0x0050C100 with ECX = guest + 0x30) and plays the sound [0x00803A24] when the thing id's low three bits are 0 (0x0050133D), which OpenTPW does not (its medium happiness drop is applied) — evidence needed: whether the generic route reaches a queue place from a cell off the queue, the thing-id numbering, and what the event history feeds
			if ( index < 0 && !onJoinCell )
			{
				DismissFromQueue( guest, attraction, GuestThought.Confused );
				continue;
			}
			guest.QueueCellIndex = index < 0 ? cells.Count : index;
			MoveToQueuePosition( guest, attraction );
		}
	}

	private static int IndexOfCell( IReadOnlyList<(int X, int Y)> cells, int x, int y )
	{
		for ( var index = 0; index < cells.Count; index++ )
		{
			if ( cells[index] == (x, y) )
				return index;
		}
		return -1;
	}

	private static (int X, int Y) JoinCellOf( IRideVisitorBridge attraction ) => attraction.JoinCell ?? attraction.EntranceCell;

	/// <summary>The guest joined (state 10): it steps onto the back cell and walks to its position.</summary>
	// [BIN:STP-PPC:0x100ECE58 join] +524 = happiness, append to the list, then 0xee604 (position, slot, state 12)
	private void BeginQueue( Guest guest, IRideVisitorBridge attraction )
	{
		var cells = attraction.QueueCells;
		guest.QueueJoinHappiness = guest.Happiness;
		guest.QueueJoinTurn = ParkTurn;
		guest.QueueCalled = false;
		guest.InQueueInterlude = false;
		// On a one-cell queue whose front is the path cell itself the guest already stands on the back cell.
		guest.QueueCellIndex = cells[^1] == (guest.CellX, guest.CellY) ? cells.Count - 1 : cells.Count;
		MoveToQueuePosition( guest, attraction );
	}

	/// <summary>The destination for the guest's list position (<c>0xee604</c>): store the position and walk there (state 12).</summary>
	private void MoveToQueuePosition( Guest guest, IRideVisitorBridge attraction )
	{
		var position = attraction.GetQueuePosition( guest.Id );
		if ( position < 0 )
		{
			// the move-up's failure in 0x004FFFF0 ("Couldn't get to my intended queue position") leaves through the dismissal too
			DismissFromQueue( guest, attraction, GuestThought.Confused );
			return;
		}
		guest.QueuePosition = position;
		var (index, x, y) = QueueSlotPoint( attraction, position, NextLateral() );
		SetQueueDestination( guest, attraction, index, (x, y), toEntrance: false );
		guest.State = GuestState.MovingUpQueue;
	}

	/// <summary>
	/// A queue walk (states 12 and 13): from the guest's cell along the queue cells to cell <paramref name="index"/>,
	/// then into the ride's entrance cell for the stand point (<paramref name="toEntrance"/>).
	/// </summary>
	private static void SetQueueDestination( Guest guest, IRideVisitorBridge attraction, int index, (int X, int Y) target, bool toEntrance )
	{
		var cells = attraction.QueueCells;
		var route = new List<(int X, int Y)>();
		// The walk starts at the guest's own cell: a queue cell by its index, the join cell behind the back cell, the entrance
		// cell ahead of the front cell. QueueCellIndex is only the last queue cell the guest stood on, so a guest that cut
		// into a queue cell and drifted back onto the join cell would get a route whose first hop skips cells (review WV-13).
		var current = guest.Navigation.Cell;
		var from = IndexOfCell( cells, current.X, current.Y );
		if ( from < 0 && attraction.JoinCell == current )
			from = cells.Count;
		else if ( from < 0 && EntranceCellOf( attraction ) == current )
			from = 0;
		else if ( from < 0 )
			from = Math.Min( guest.QueueCellIndex, cells.Count );
		index = Math.Min( index, cells.Count - 1 );
		for ( var cell = Math.Min( from, cells.Count - 1 ); cell > index; cell-- )
			route.Add( cells[cell] );
		for ( var cell = Math.Max( from, 0 ); cell < index; cell++ )
			route.Add( cells[cell] );
		route.Add( cells[index] );
		if ( toEntrance )
			route.Add( EntranceCellOf( attraction ) );
		// The queue goes on beyond both ends of the walk: the cell ahead of the destination (the entrance cell ahead of
		// the front cell) and the cell behind the start (the join cell behind the back cell) are walkable too.
		// [APPROX:WALK-006] a queue walk may also enter the queue cell ahead of its destination and the one behind its start, and no other cell off its route; in the original a step from a queue cell is open (edge test 0x4d8750) into a path, queue or queue-end neighbour whose link points back at it, or into a type-0x1e fixed walkway when the mask is not 2, and blocked otherwise; OpenTPW does not read the cell links for this — still open: the edge test 0x4d8750 on the queue links is not ported (WALK-005)
		(int X, int Y)? ahead = toEntrance ? null : index > 0 ? cells[index - 1] : EntranceCellOf( attraction );
		var behind = from + 1 < cells.Count ? cells[from + 1] : attraction.JoinCell;
		var alsoOpen = new List<(int X, int Y)>( 2 );
		if ( ahead is { } a )
			alsoOpen.Add( a );
		if ( behind is { } b )
			alsoOpen.Add( b );
		guest.QueueTargetIndex = index;
		guest.QueueTargetX = target.X / (float)Fixed16.One;
		guest.QueueTargetY = target.Y / (float)Fixed16.One;
		SetDestination( guest, target, route, alsoOpen );
	}

	/// <summary>The ride's entrance cell: the front queue cell's neighbour towards the ride.</summary>
	private static (int X, int Y) EntranceCellOf( IRideVisitorBridge attraction )
	{
		var front = attraction.QueueCells[0];
		var (dx, dy) = GuestPathGrid.Directions[attraction.DirectionTowardsFront( 0 )];
		return (front.X + dx, front.Y + dy);
	}

	// [BIN:STP-PPC:0x100DDCC4 queue lateral byte] the sideways byte is rand mod 28 + 114 (114..141) from the shared RNG 0x105328; OpenTPW draws it from the simulation's seeded GuestRandom
	private byte NextLateral() => (byte)(114 + random.Next( 28 ));

	/// <summary>
	/// Queue cell and point of a position (<c>0xddcc4</c>): follow the queue four positions per cell, then the
	/// depth byte trunc(255 × 0.25 × remaining) (0, 63, 127, 191) along the cell from its front edge and the
	/// sideways byte <paramref name="lateral"/> (114..141) across it, each byte / 256 of a cell (<c>0xe68cc</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x004DE840 queue slot] depth = trunc(255 × 0.25 × the place within the cell); lateral = 0x72 + rand % 28. Up to depth 128 the cell's own link to the front picks the axis: link 1 (−Y) gives (lateral, depth), 4 (+X) (255 − depth, lateral), 16 (+Y) (lateral, 255 − depth), 64 (−X) (depth, lateral). Beyond 128 (the fourth place) the axis is the link of the queue cell behind (0x004DE670), for the back cell the direction from its attached path cell (the first of its connections 1, 4, 16, 64 that leads to a path cell, reversed), and link 4 then gives depth − 128 instead of 255 − depth
	// [APPROX:QUEUE-006] the back cell's fourth place takes its axis from OpenTPW's join cell, the first adjacent path cell in the order 1, 4, 16, 64; tp.exe 0x004DE840 takes the first such cell whose bit is also set in the back cell's connection byte +0x0C (0x004DEA2F..0x004DEA57), so the two differ when the back cell touches an unconnected path cell earlier in that order — evidence needed: the queue tool's connection writes (QUEUE-003)
	public (int CellIndex, float X, float Y) QueuePositionPoint( IRideVisitorBridge attraction, int position, byte lateral )
	{
		var (index, x, y) = QueueSlotPoint( attraction, position, lateral );
		return (index, x / (float)Fixed16.One, y / (float)Fixed16.One);
	}

	/// <summary><see cref="QueuePositionPoint"/> in 16.16 cells.</summary>
	public static (int CellIndex, int X, int Y) QueueSlotPoint( IRideVisitorBridge attraction, int position, byte lateral )
	{
		var (index, depth) = QueueSlot( position, attraction.QueueSizeInCells );
		var cells = attraction.QueueCells;
		var cellIndex = Math.Min( index, cells.Count - 1 );
		var cell = cells[cellIndex];
		var far = depth > 128;
		int direction;
		if ( !far )
			direction = attraction.DirectionTowardsFront( cellIndex );
		else if ( cellIndex + 1 < cells.Count )
			direction = attraction.DirectionTowardsFront( cellIndex + 1 );
		else if ( attraction.JoinCell is { } join && Math.Abs( join.X - cell.X ) + Math.Abs( join.Y - cell.Y ) == 1 )
			direction = GuestPathGrid.DirectionBetween( join.X, join.Y, cell.X, cell.Y );
		else
			direction = attraction.DirectionTowardsFront( cellIndex );
		var (bx, by) = direction switch
		{
			0 => (lateral, depth),                                // link 1, front towards −Y
			1 => (far ? depth - 128 : 255 - depth, (int)lateral), // link 4, front towards +X
			2 => (lateral, 255 - depth),                          // link 16, front towards +Y
			_ => (depth, (int)lateral)                            // link 64, front towards −X
		};
		var (x, y) = GuestNavigation.BytePoint( cell, bx, by );
		return (index, x, y);
	}

	/// <summary>The queue cell index and depth byte of a 0-based position in a queue of <paramref name="cells"/> cells.</summary>
	public static (int CellIndex, int Depth) QueueSlot( int position, int cells )
	{
		var remaining = Math.Max( 0, position );
		var index = 0;
		while ( remaining >= RideVisitorBridge.PositionsPerCell && index < cells - 1 )
		{
			remaining -= RideVisitorBridge.PositionsPerCell;
			index++;
		}
		remaining = Math.Min( remaining, RideVisitorBridge.PositionsPerCell - 1 );
		return (index, (int)(255f * (0.25f * remaining)));
	}

	/// <summary>
	/// The stand point where a called guest offers itself (<c>0xde1d8</c>): in the ride's entrance cell, at the bytes
	/// trunc(255 × <c>UsageInfo.EntryCellStandPos</c>) after rotating it with the object (0/90/180/270); (0.5, 0.5)
	/// gives (127, 127), the entrance cell's centre less 1/256.
	/// </summary>
	// [BIN:STP-PPC:0x100DE1D8 entry stand point] host +50 entry cell; (x, y) rotated by host +16: 90 → (y, 1 − x), 180 → (1 − x, 1 − y), 270 → (1 − y, x); × 255.0 (data 0x54c0), truncated to bytes (0xde500..0xde528)
	public static (int X, int Y) StandPoint( IRideVisitorBridge attraction )
	{
		var (bx, by) = StandBytes( attraction.EntryCellStandPos, attraction.Rotation );
		return GuestNavigation.BytePoint( EntranceCellOf( attraction ), bx, by );
	}

	/// <summary>The stand point's bytes for a stand position and an object rotation in degrees.</summary>
	public static (int X, int Y) StandBytes( (float X, float Y) position, int rotation )
	{
		var (x, y) = position;
		var (fx, fy) = (((rotation % 360) + 360) % 360) switch
		{
			90 => (y, 1 - x),
			180 => (1 - x, 1 - y),
			270 => (1 - y, x),
			_ => (x, y)
		};
		return ((int)(255.0 * fx), (int)(255.0 * fy));
	}

	/// <summary>States 12 (walking to a queue position) and 13 (walking to the stand point): one walk along the queue cells.</summary>
	private void UpdateQueueWalk( Guest guest, float dt )
	{
		var attraction = FindAttraction( guest.AttractionId );
		if ( attraction == null )
		{
			ReturnOrphanToPath( guest );
			return;
		}
		if ( guest.HasWaypoint )
		{
			var arrived = Move( guest );
			TrackQueueCell( guest, attraction );
			if ( !arrived )
				return;
		}
		else if ( turnStarted )
			Step( guest ); // state 13 keeps stepping (braking) until the ride takes the guest's offer
		if ( guest.State == GuestState.MovingUpQueue )
			EnterStanding( guest );
		else if ( TooExpensive( guest, attraction ) )
			// [BIN:TPW-EXE:0x005006B0 called forward] at the stand point the price test 0x004FDE50 comes before the ride takes the guest (AdmitPerson); a guest that refuses leaves the queue ("Object %d is too expensive, I'm leaving the queue") and keeps its thought
			// [APPROX:GUEST-003] the refusal costs happiness twice at the medium step (0x004FEA70 with 1, once for the refusal and once in the dismissal 0x005012F0), but its other effects are not modelled: animation 6, the guest record of event 10 (0x0050C100), the ride's walk-away count (mNumWalkAways, 0x004E1670) and the price perception statistic per shop category (0x004C74B0); the park's per-type word +0xC (0x004E2740) is taken as 0 — evidence needed: the writers of that word (script opcodes around 0x004CA47F) and the uses of the walk-away count and the statistic
			RefusePrice( guest, attraction );
		else if ( attraction.PresentForBoarding( guest.Id ) )
			guest.State = GuestState.WaitingToBoard;
	}

	/// <summary>A called guest refuses the price: two medium happiness drops (the refusal and the dismissal), then it leaves the queue.</summary>
	private void RefusePrice( Guest guest, IRideVisitorBridge attraction )
	{
		// [BIN:TPW-EXE:0x00500756 price refusal] "Object is too expensive, I'm leaving the queue": thought 6, then the refusal's own 0x004FEA70(1) before the dismissal's
		guest.Happiness = Math.Max( 0, guest.Happiness - Settings.MediumHappinessChange );
		DismissFromQueue( guest, attraction, GuestThought.Dissatisfied );
	}

	/// <summary>The price test at the stand point (<see cref="QueueJoinRules.TooExpensive"/>): rides cost nothing per use.</summary>
	private static bool TooExpensive( Guest guest, IRideVisitorBridge attraction )
	{
		if ( attraction.Kind is not (RideVisitorKind.Shop or RideVisitorKind.Sideshow) )
			return false;
		return QueueJoinRules.TooExpensive( PriceTermsOf( attraction ), guest.Happiness, guest.Thirst, guest.Hunger, guest.Nausea, guest.Money );
	}

	/// <summary>
	/// The price test's words for <paramref name="attraction"/>: the live words the level mirrors from the economy (a free visit's
	/// all-zero terms included); a bridge nobody mirrors into keeps its constructor price.
	/// </summary>
	internal static PriceTerms PriceTermsOf( IRideVisitorBridge attraction )
	{
		var live = attraction.Sideshow;
		var price = attraction.HasSideshowTerms ? live.PricePerUse : attraction.Price;
		return new PriceTerms( price, live.CostOfGoods, (100 - live.ChanceOfLosingPercent) & 0xFF, attraction.Kind == RideVisitorKind.Sideshow,
			attraction.InitCostOfGoods, attraction.ThirstEffect, attraction.HungerEffect, attraction.VomitEffect, attraction.HappinessEffect, 0 );
	}

	/// <summary>The cell the walking guest stands on, and its queue index when it is a queue cell.</summary>
	private static void TrackQueueCell( Guest guest, IRideVisitorBridge attraction )
	{
		var cell = guest.Navigation.Cell;
		if ( cell == (guest.CellX, guest.CellY) )
			return;
		guest.PreviousCellX = guest.CellX;
		guest.PreviousCellY = guest.CellY;
		(guest.CellX, guest.CellY) = cell;
		var index = IndexOfCell( attraction.QueueCells, cell.X, cell.Y );
		if ( index >= 0 )
			guest.QueueCellIndex = index;
	}

	private void EnterStanding( Guest guest )
	{
		guest.State = GuestState.Queueing;
		guest.QueueStandingSinceTurn = ParkTurn;
		guest.QueueMoveDelay = (int)(MoveDelayFactor * guest.QueuePosition);
		guest.LastQueueUpdateTurn = ParkTurn;
	}

	/// <summary>State 11 (<c>0xed244</c>), once per park turn, in the original order.</summary>
	private void UpdateStanding( Guest guest )
	{
		var turn = ParkTurn;
		if ( guest.LastQueueUpdateTurn == turn )
			return;
		guest.LastQueueUpdateTurn = turn;
		var attraction = FindAttraction( guest.AttractionId );
		if ( attraction == null )
		{
			ReturnOrphanToPath( guest );
			return;
		}
		if ( guest.InQueueInterlude )
		{
			if ( turn > guest.InterludeTurn + InterludeTurns )
			{
				guest.InQueueInterlude = false;
				EnterStanding( guest );
			}
			return;
		}
		// 1. Called forward: walk to the stand point (state 13).
		if ( guest.QueuePosition == 0 && guest.QueueCalled && attraction.CalledGuest == guest.Id )
		{
			guest.QueueCalled = false;
			SetQueueDestination( guest, attraction, 0, StandPoint( attraction ), toEntrance: true );
			guest.State = GuestState.Boarding;
			return;
		}
		// 3. The ride failed: 0xdfe34, a relief object whose wear gauge is below 25 (ORACLE.md F15), not a breakdown. A broken or closed ride
		// dismisses its queue head first from the ride update (RideVisitorBridge.DismissHead), the others keep waiting.
		// [APPROX:QUEUE-008] the predicate follows the original; relief objects wear by use and are cleaned by handymen (ParkEconomy.UseRelief, DispatchToiletCleaning), and the swaps of the RegionFX rows 1 and 6 at the toilet's cell are the original's (0x004D8460 / 0x004D8440 are the remove and add wrappers of the region writer 0x004D8480, ParkEconomy.SwapToiletEffect). Not modelled: the branch of 0x004E2440 for game type 1 (the word 0x00FB3B7C), where a use wears nothing and a toilet whose gauge byte is below 25 is cleaned at once (rows 6 → 1, script variable 8 = 0, gauge 100, +0x5E and +0x64 cleared); OpenTPW wears toilets in every game — evidence needed: what game type 1 is in OpenTPW's terms
		// [BIN:TPW-EXE:0x005004AA queue exit] every state-11 exit unlinks the guest (0x004DDD20), dismisses it (0x005012F0: "dismissed from queue", the medium happiness drop 0x004FEA70(1), the queue words cleared) and sets state 6; the thought, if any, is set before the jump (DismissFromQueue)
		if ( attraction.ReliefWornOut )
		{
			DismissFromQueue( guest, attraction, GuestThought.FeelingSick );
			return;
		}
		// 4. Position lookup.
		var position = attraction.GetQueuePosition( guest.Id );
		if ( position < 0 )
		{
			// [BIN:TPW-EXE:0x00500270 guest missing from its queue] "Problem with a queue - shouldn't be fatal": a log line and the dismissal, no thought
			DismissFromQueue( guest, attraction, GuestThought.None );
			return;
		}
		// 5. Moving up: wait out the delay while the gap is at most 2, else walk to the new position.
		// While the ride is in state 1 (broken) 0xed4b0 (0xdfe0c) skips the walk and goes straight to the needs window (0xed58c), so the
		// guests keep their physical places as the head is dismissed.
		// [BIN:STP-PPC:0x100ED4B0 broken ride holds the queue] only state 1 holds; state 4 (condemned, life gauge 0) is not told apart from it here (QUEUE-019)
		// [BIN:TPW-EXE:0x005002C2 move-up] a changed place decrements the delay (0x005002EB), holds for a broken ride (0x004E0370) or starts the
		// walk (0x00501160, state 12); each continues into the needs window at 0x005002F2, and only an unchanged place reaches the limit test (0x0050059D)
		var moved = position != guest.QueuePosition;
		if ( moved )
		{
			var gap = guest.QueuePosition - position;
			if ( guest.QueueMoveDelay != 0 && gap is >= 0 and <= MoveUpWaitGap )
				guest.QueueMoveDelay--;
			else if ( !attraction.IsBrokenDown )
				MoveToQueuePosition( guest, attraction );
		}
		// 6. Beyond the limit.
		if ( !moved && guest.QueuePosition > attraction.QueueLimit )
		{
			DismissFromQueue( guest, attraction, GuestThought.QueueTooLong );
			return;
		}
		// [BIN:TPW-EXE:0x00500612 coaster or track refuses] at its position, after the limit test, a guest queueing for a coaster (0x004E2B50) that fails 0x00441970, or for a ride of track type 1 whose track word +0x2C is 0, thinks 13 and leaves (0x005004AA)
		if ( !moved && attraction.TrackRefusesGuests )
		{
			DismissFromQueue( guest, attraction, GuestThought.Angry );
			return;
		}
		// 7. Needs window, and the boredom test that cannot fire (QUEUE-plan §5.3): +508 ≥ +520 always holds here.
		// The original truncates happiness +412 and toilet +428 to bytes (fctiwz, clrlwi 24) before comparing: "> 80" means ≥ 81.
		var window = turn - guest.InterludeTurn;
		if ( window > NeedsWindowTurns )
		{
			var happiness = (int)guest.Happiness;
			if ( happiness > 80 || happiness is >= (int)LeaveHappiness and < 20 )
			{
				StartInterlude( guest );
				return;
			}
			if ( happiness < (int)LeaveHappiness )
			{
				DismissFromQueue( guest, attraction, GuestThought.Unhappy );
				return;
			}
			// [BIN:TPW-EXE:0x005003AA toilet in the queue] a toilet byte above 80 thinks 4 first and then leaves unless the ride gives relief (ride +0x32 bit 0): a guest queueing for a toilet thinks it and stays
			if ( (int)guest.Toilet > (int)ToiletLeaveLevel )
			{
				if ( attraction.ProvidesRelief )
				{
					Think( guest, GuestThought.NeedToilet );
					return;
				}
				DismissFromQueue( guest, attraction, GuestThought.NeedToilet );
				return;
			}
			// [BIN:TPW-EXE:0x004FFFF0 needs branch returns] beyond the window every path of the needs branch returns (0x00500311..0x005003A2), so the facing change below runs only within 30 turns of the last interlude
			return;
		}
		if ( turn > guest.QueueStandingSinceTurn + BoredomTurns )
		{
			DismissFromQueue( guest, attraction, GuestThought.Bored );
			return;
		}
		// 8. A 1-in-10 facing change of −400..+399 of 2048 of a turn.
		// [BIN:TPW-EXE:0x004FFFF0 queue facing change] one draw (0x00516330) mod 10 must be 0 (else return), then a second draw gives heading +0x1C += (r mod 800) − 400, wrapped by 0x800; OpenTPW draws both from its GuestRandom (DET-014) and turns the heading vector by the same angle
		if ( random.Next( 10 ) != 0 )
			return;
		var angle = (random.Next( FacingChangeRange ) - FacingChangeRange / 2) * MathF.Tau / 2048;
		var (sin, cos) = MathF.SinCos( angle );
		(guest.HeadingX, guest.HeadingY) = (guest.HeadingX * cos - guest.HeadingY * sin, guest.HeadingX * sin + guest.HeadingY * cos);
	}

	/// <summary>Idle interlude (<c>0xe8b74</c>): +520 = turn, state 8 until turn &gt; +520 + 10, then state 11 again.</summary>
	private void StartInterlude( Guest guest )
	{
		guest.InterludeTurn = ParkTurn;
		guest.InQueueInterlude = true;
	}

	/// <summary>
	/// The queue exits of state 11, the queue edit and the price refusal (tp.exe <c>0x005004AA</c>, <c>0x00501496</c>, <c>0x00500756</c>): the thought, then the
	/// dismissal <c>0x005012F0</c>, which lowers happiness by the medium step (<c>0x004FEA70</c> with 1, <c>PeepInfo.MediumHappinessChange</c>, clamped at 0)
	/// before the guest leaves the queue.
	/// </summary>
	// [BIN:TPW-EXE:0x005012F0 dismissal] "Person %d: dismissed from queue on ride %d", event 6 (0x0050C100), the sound for one thing id in eight, 0x004FEA70(1), the queue words +0x22A/+0x228/+0x1F8/+0x1DC/+0x1F1 cleared, state 6; the event and the sound are not modelled (QUEUE-005)
	private void DismissFromQueue( Guest guest, IRideVisitorBridge attraction, GuestThought thought )
	{
		if ( thought != GuestThought.None )
			Think( guest, thought );
		guest.Happiness = Math.Max( 0, guest.Happiness - Settings.MediumHappinessChange );
		LeaveQueue( guest, attraction, GuestThought.None );
	}

	/// <summary>The guest leaves the queue without riding and steps back onto the path at the join cell.</summary>
	// [APPROX:QUEUE-010] a guest leaving a queue is placed on the queue's join path cell at once; in tp.exe every state-11 exit (0x005004AA: unlink 0x004DDD20, dismissal 0x005012F0, SetState(6)) and the queue-edit exit write no position, so the guest stays on its queue cell in state 6 until the state-6 handler 0x004FEC90 starts its next walk from there — evidence needed: the state-6 handler's walk from a queue cell and the route it takes
	private void LeaveQueue( Guest guest, IRideVisitorBridge attraction, GuestThought thought )
	{
		attraction.LeaveQueue( guest.Id );
		ClearQueueState( guest );
		if ( thought != GuestThought.None )
			Think( guest, thought );
		guest.LastAttractionId = attraction.AttractionId;
		ReturnToPath( guest, JoinCellOf( attraction ) );
	}

	/// <summary>The guest's attraction is gone (removed while the guest queued or waited to be taken).</summary>
	private void ReturnOrphanToPath( Guest guest )
	{
		ClearQueueState( guest );
		ReturnToPath( guest, (guest.CellX, guest.CellY) );
	}

	private static void ClearQueueState( Guest guest )
	{
		guest.QueuePosition = -1;
		guest.QueueJoinTurn = -1;
		guest.QueueCalled = false;
		guest.InQueueInterlude = false;
	}

	private void UpdateLeaving( Guest guest, float dt )
	{
		if ( guest.HasWaypoint && !Move( guest ) )
			return;
		var lanes = new[] { ValidLane( Settings.ArrivalLaneA ), ValidLane( Settings.ArrivalLaneB ) };
		(int X, int Y)[]? bestLane = null;
		var bestDistance = int.MaxValue;
		foreach ( var lane in lanes )
		{
			if ( lane == null )
				continue;
			var distance = Grid.Distance( guest.CellX, guest.CellY, lane[^1].X, lane[^1].Y );
			if ( distance >= 0 && distance < bestDistance )
			{
				bestDistance = distance;
				bestLane = lane;
			}
		}
		if ( bestLane == null )
		{
			guest.State = GuestState.Gone; // no way out: the original would eject; documented simplification
			return;
		}
		if ( bestDistance == 0 )
		{
			StartLeavingLane( guest, bestLane, bestLane.Length - 1 );
			return;
		}
		if ( Grid.TryStep( guest.CellX, guest.CellY, bestLane[^1].X, bestLane[^1].Y, out var nx, out var ny ) )
			SetCellWaypoint( guest, nx, ny );
	}

	private void ReturnToPath( Guest guest, (int X, int Y) cell )
	{
		guest.AttractionId = 0;
		if ( !Grid.IsWalkable( cell.X, cell.Y ) )
			cell = Grid.FindNearestWalkable( cell.X, cell.Y ) ?? cell;
		PlaceOnGrid( guest, cell.X, cell.Y );
		guest.State = GuestState.DoingNothing;
	}

	long IRideVisitorHost.ParkTurn => ParkTurn;
	bool IRideVisitorHost.WalksToBoard => true;

	bool IRideVisitorHost.IsStandingAtFront( IRideVisitorBridge ride, int guestId ) =>
		Find( guestId ) is { State: GuestState.Queueing, InQueueInterlude: false, QueuePosition: 0 } guest && guest.AttractionId == ride.AttractionId;

	void IRideVisitorHost.OnVisitorOffered( IRideVisitorBridge ride, int guestId )
	{
		// Head +504 = 1; the guest notices on its next state-11 update.
		if ( Find( guestId ) is { } guest )
			guest.QueueCalled = true;
	}

	void IRideVisitorHost.OnVisitorBoarded( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		if ( guest.QueueJoinTurn >= 0 )
		{
			guest.LastQueueWaitTurns = ParkTurn - guest.QueueJoinTurn;
			QueueWaitCompleted?.Invoke( guest, ride, guest.LastQueueWaitTurns );
		}
		ClearQueueState( guest );
		guest.State = GuestState.Using;
		// [BIN:TPW-EXE:0x00502156 state 16 entry] the balloon's model is destroyed (0x00475550) and +0x210 cleared; its life +0x214 stays; the thought bubble goes (0x0050C0D0)
		guest.HasBalloon = false;
		guest.BubbleThought = GuestThought.None;
		guest.AttractionId = ride.AttractionId;
		guest.HasWaypoint = false;
		var price = 0;
		guest.SideshowWon = null;
		if ( Payments != null )
		{
			// [BIN:TPW-EXE:0x00501F3C state 14 entry] a sideshow's play is decided as it starts (+0x1F1 from 0x004E2670) and read when the visit ends (0x004FD970)
			Payments.TryPayVisit( ride, guest.Money, out price, out var won );
			guest.SideshowWon = ride.Kind == RideVisitorKind.Sideshow ? won : null;
		}
		else if ( ride.Price > 0 && guest.Money >= ride.Price )
			price = ride.Price;
		if ( price > 0 )
		{
			guest.Money -= price;
			MoneySpent?.Invoke( guest, price, ride.AttractionId );
		}
	}

	void IRideVisitorHost.OnVisitorReleased( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		ReturnToPath( guest, ride.ExitCell );
		guest.State = GuestState.ExitingRide;
		guest.StateTimer = ExitRideSeconds;
		guest.LastAttractionId = ride.AttractionId;
		guest.SecondsSinceRide = 0;
		guest.RidesTaken++;
		Payments?.VisitFinished( ride );
		// [BIN:STP-PPC:0x100E9FD4 used history] the end of a use (0xe9fa4) pushes the attraction's id into the used history (0xea268) before the outcome
		PushHistory( guest.UsedHistory, ride.AttractionId );
		ApplyVisit( guest, ride );
		// [BIN:TPW-EXE:0x00501FD3 state 15 entry] after the use routine (0x005015F4, 0x004FD970) the state-15 entry gives a guest with balloon life its balloon back, except at a balloon shop, whose use routine has just made one
		if ( guest.BalloonLife > 0 && ride.AppearanceEffect != GuestAppearance.Balloon )
			guest.HasBalloon = true;
	}

	/// <summary>
	/// A broken ride sends its head guest away (<c>0xee804</c>): reason 6 (not a thought), happiness down by the medium change
	/// (<c>0xeb6f4</c> selector 1, <c>PeepInfo.MediumHappinessChange</c>, clamped to 0..100), queue fields cleared and state 6.
	/// </summary>
	// [BIN:STP-PPC:0x100EE804 dismissal] "Person %d: dismissed from queue on ride %d"; the target id +476 is cleared and no thought or last attraction is written
	void IRideVisitorHost.OnVisitorDismissed( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		guest.Happiness = Math.Max( 0, guest.Happiness - Settings.MediumHappinessChange );
		ClearQueueState( guest );
		ReturnToPath( guest, JoinCellOf( ride ) );
	}

	void IRideVisitorHost.OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		ClearQueueState( guest );
		Think( guest, GuestThought.Dissatisfied );
		ReturnToPath( guest, JoinCellOf( ride ) );
	}

	/// <summary>
	/// What a use does to the guest (<c>0xea318</c>, docs/reverse/ORACLE.md F24, <see cref="RideOutcome"/>): the happiness gain from the .sam
	/// PerfectRide, GoodRide and OKRide values by the difference between the guest's preferred and the attraction's live excitement, and the
	/// illness rise by excitement / RideVomitDivisor times the stomach factor, which does nothing for an attraction whose live excitement is 0 (shops); then the
	/// type's own effects (<see cref="UseEffects"/>): thirst and hunger fall, illness and happiness rise. A relief object still sets the toilet meter to 0.
	/// </summary>
	// [BIN:STP-PPC:0x100EA318 ride outcome] executed by the PowerPC oracle (ride-outcome, 561 inputs; C# agrees)
	// [APPROX:GUEST-005] a relief visit wears the toilet, empties the toilet meter and clears an illness byte above 90, and every visit fills the litter meter (tp.exe 0x004FE1E0), but its guest records (events 0x11 and 0x12) and the ingredient and appearance effects beyond the speed boost are not modelled — evidence needed: what the guest records feed
	private void ApplyVisit( Guest guest, IRideVisitorBridge ride )
	{
		var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
		var before = guest.Happiness;
		(guest.Happiness, guest.Nausea) = RideOutcome.Apply( guest.Happiness, guest.Hunger, guest.Nausea, type.PreferredExcitement, ride.LiveExcitement,
			Settings.PerfectRide, Settings.GoodRide, Settings.OkRide, Settings.RideVomitDivisor );
		// [BIN:TPW-EXE:0x004FDB66 sideshow thought] only a sideshow (thing kind 2) sets a thought at the end of a use: 5 after a win, 6 after a loss (0x004FDC25); rides and shops set none
		if ( ride.Kind == RideVisitorKind.Sideshow )
			Think( guest, (guest.SideshowWon ?? guest.Happiness - before > 0) ? GuestThought.Pleased : GuestThought.Dissatisfied );
		guest.SideshowWon = null;
		// the type's own effects follow the outcome in the original's use routine (0xeaaf8: 0xea318, then the effects fragment 0xeabb8)
		var effects = UseEffects.Apply( new GuestMeters( guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea ),
			ride.ThirstEffect, ride.HungerEffect, ride.VomitEffect, ride.HappinessEffect );
		(guest.Happiness, guest.Thirst, guest.Hunger, _, guest.Nausea) = effects;
		// [BIN:TPW-EXE:0x004FE1E0 litter meter] after the happiness effect the type's LitterEffect (+0x154) is added to the litter meter +0x1B4, clamped to 0..100 ("Litter gone up by %d, is now %d")
		guest.Litter = MeterMath.Add( guest.Litter, ride.LitterEffect );
		// then 0xeaaf8 switches on the type's special ingredient (0xeaf44; its sole caller 0xe9ff8 runs when a guest leaves the object): Sugar raises the speed boost
		guest.Boost = GuestSpeed.RaiseBoost( guest.Boost, ride.SpecialIngredient, ride.IngredientAmount );
		ApplyAppearance( guest, ride.AppearanceEffect );
		if ( ride.ProvidesRelief )
		{
			// [BIN:TPW-EXE:0x004FE1E0 relief visit] for an object with the relief flag: the toilet wears by the guest's toilet meter as a byte (0x004E2440), the meter goes to 0, and an illness byte above 90 goes to 0 as well
			Payments?.ReliefUsed( ride, (int)((long)guest.Toilet & 0xFF) );
			guest.Toilet = 0;
			if ( ((long)guest.Nausea & 0xFF) > 90 )
				guest.Nausea = 0;
		}
	}

	/// <summary>
	/// The use routine's appearance step (tp.exe <c>0x004FE1E0</c>, after the special ingredient): a balloon shop gives a balloon with a life from the shop's
	/// quality of goods (<see cref="GuestAppearance.BalloonLife"/>); a costume shop takes back the costume the guest wears or puts one on.
	/// </summary>
	// [BIN:TPW-EXE:0x004FE615 appearance effect] type +0x15C: 1 creates the balloon model (+0x210, sprite set 10) and sets +0x214 to the balloon life, guest event 12; 2 with +0x24 == 2 ("Customer returning a costume") sets +0x24 to 0 and the sprite bank +0x20 back, otherwise +0x24 = 2, the costume bank and guest event 11; another value logs "Balance file error"
	// [APPROX:ECON-044] the shop's quality of goods stays at the build's 50 (OpenTPW has no quality control, ride +0x18C, so a balloon lives 127 needs phases), the balloon model's world reseed (0x00516370, DET-014) is not followed and the guest events 11 and 12 are not recorded; a balloon's life counts down, and the challenge percentages count, while the guest is in the park by its state and on the grid rather than on a cell of type 0, 1, 3, 9 or 10 (0x004FA990, as ECON-027) — evidence needed: none (read, tp.exe 0x004FE1E0, 0x00501650, 0x00501DB0)
	private static void ApplyAppearance( Guest guest, int effect )
	{
		switch ( effect )
		{
			case GuestAppearance.Balloon:
				guest.HasBalloon = true;
				guest.BalloonLife = GuestAppearance.BalloonLife( GuestAppearance.BuiltQualityOfGoods );
				break;
			case GuestAppearance.Costume:
				guest.InCostume = !guest.InCostume;
				break;
		}
	}

	/// <summary>
	/// Shifts an id into a four-slot history, newest first (<c>0xea268</c> for the used history, <c>0xea290</c> for the temporary one): the oldest entry falls out.
	/// </summary>
	private static void PushHistory( int[] history, int id )
	{
		for ( var slot = history.Length - 1; slot > 0; slot-- )
			history[slot] = history[slot - 1];
		history[0] = id;
	}

	public GuestStatistics GetStatistics()
	{
		int inPark = 0, walking = 0, queueing = 0, onRides = 0, arriving = 0, leaving = 0;
		var happiness = 0f;
		Span<int> thoughts = stackalloc int[18];
		foreach ( var guest in guests )
		{
			if ( InPark( guest ) ) { inPark++; happiness += guest.Happiness; }
			switch ( guest.State )
			{
				case GuestState.WalkingAround or GuestState.GoingToRide or GuestState.DoingNothing or GuestState.ThrowingAwayLitter or GuestState.ExitingRide: walking++; break;
				case GuestState.Queueing or GuestState.MovingUpQueue or GuestState.WaitingToBoard or GuestState.Boarding: queueing++; break;
				case GuestState.Using: onRides++; break;
				case GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth or GuestState.AtTicketBooth: arriving++; break;
				case GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome: leaving++; break;
			}
			if ( (int)guest.Thought is > 0 and < 18 )
				thoughts[(int)guest.Thought]++;
		}
		var common = 0;
		for ( var index = 1; index < thoughts.Length; index++ )
			if ( thoughts[index] > thoughts[common] )
				common = index;
		return new GuestStatistics( inPark, walking, queueing, onRides, arriving, leaving, inPark == 0 ? 0 : happiness / inPark, (GuestThought)common, Admissions );
	}

	// ---- Snapshots (park save/load) ------------------------------------------------------------

	/// <summary>
	/// Captures the whole guest system between ticks as a typed deep copy: the explicit <see cref="GuestRandom"/>
	/// state, tick/counters, the arrival stream, every guest in update order and every registered attraction's queue
	/// state (with the simulation's seen queue-edit counters). Economy bindings (<see cref="Payments"/>,
	/// <see cref="ArrivalClock"/>, <see cref="ParkPoints"/>, <see cref="ArrivalsHalted"/>) are live references the
	/// park re-attaches after a restore and are not captured. Throws <see cref="NotSupportedException"/> when a
	/// registered attraction is not a <see cref="RideVisitorBridge"/>: an unsupported attraction is an explicit save
	/// failure, never a silent omission.
	/// </summary>
	public GuestSimulationSnapshot CaptureSnapshot()
	{
		var attractionSnapshots = new RideVisitorBridgeSnapshot[attractions.Count];
		var seenEdits = new (int AttractionId, int SeenEditCount)[attractions.Count];
		for ( var index = 0; index < attractions.Count; index++ )
		{
			if ( attractions[index] is not RideVisitorBridge bridge )
				throw new NotSupportedException( $"Attraction {attractions[index].AttractionId} ('{attractions[index].Name}', {attractions[index].GetType().Name}) has no queue snapshot; only RideVisitorBridge attractions can be saved." );
			attractionSnapshots[index] = bridge.CaptureSnapshot();
			seenEdits[index] = (bridge.AttractionId, seenQueueEdits.TryGetValue( bridge.AttractionId, out var seen ) ? seen : -1);
		}
		var guestSnapshots = new GuestSnapshot[guests.Count];
		for ( var index = 0; index < guests.Count; index++ )
			guestSnapshots[index] = CaptureGuest( guests[index] );
		return new GuestSimulationSnapshot
		{
			SchemaVersion = GuestSimulationSnapshot.CurrentSchemaVersion,
			Seed = Seed,
			RandomState = random.State,
			TickCount = TickCount,
			TimeSeconds = TimeSeconds,
			NextGuestId = nextId,
			Admissions = Admissions,
			Departed = Departed,
			ArrivalTimerStart = arrivalTimerStart,
			DeliveringArrivals = deliveringArrivals,
			SpawnTimer = spawnTimer,
			PendingArrivals = pendingArrivals,
			LaneToggle = laneToggle,
			ArrivalsEnabled = ArrivalsEnabled,
			SeenGridVersion = seenGridVersion,
			Guests = guestSnapshots,
			Attractions = attractionSnapshots,
			SeenQueueEdits = seenEdits
		};
	}

	private static GuestSnapshot CaptureGuest( Guest guest )
	{
		var navigation = guest.Navigation;
		return new GuestSnapshot
		{
			Id = guest.Id,
			Type = guest.Type,
			State = guest.State,
			HeadingX = guest.HeadingX,
			HeadingY = guest.HeadingY,
			IsMoving = guest.IsMoving,
			DistanceWalked = guest.DistanceWalked,
			DrawX = guest.DrawX,
			DrawY = guest.DrawY,
			DrawDistanceWalked = guest.DrawDistanceWalked,
			StepFromX = guest.StepFromX,
			StepFromY = guest.StepFromY,
			StepFromDistance = guest.StepFromDistance,
			StepTick = guest.StepTick,
			Hunger = guest.Hunger,
			Thirst = guest.Thirst,
			Toilet = guest.Toilet,
			Energy = guest.Energy,
			Nausea = guest.Nausea,
			Happiness = guest.Happiness,
			Money = guest.Money,
			ExitLevel = guest.ExitLevel,
			TimeInParkSeconds = guest.TimeInParkSeconds,
			SecondsSinceRide = guest.SecondsSinceRide,
			RidesTaken = guest.RidesTaken,
			Thought = guest.Thought,
			BubbleThought = guest.BubbleThought,
			BubbleTurn = guest.BubbleTurn,
			SideshowWon = guest.SideshowWon,
			AttractionId = guest.AttractionId,
			LastAttractionId = guest.LastAttractionId,
			UsedHistory = (int[])guest.UsedHistory.Clone(),
			TemporaryHistory = (int[])guest.TemporaryHistory.Clone(),
			QueuePosition = guest.QueuePosition,
			QueueJoinTurn = guest.QueueJoinTurn,
			LastQueueWaitTurns = guest.LastQueueWaitTurns,
			QueueMoveDelay = guest.QueueMoveDelay,
			QueueCalled = guest.QueueCalled,
			QueueStandingSinceTurn = guest.QueueStandingSinceTurn,
			InterludeTurn = guest.InterludeTurn,
			InQueueInterlude = guest.InQueueInterlude,
			Activity = guest.Activity,
			Litter = guest.Litter,
			BinId = guest.BinId,
			PrankFlag = guest.PrankFlag,
			BalloonLife = guest.BalloonLife,
			HasBalloon = guest.HasBalloon,
			InCostume = guest.InCostume,
			QueueJoinHappiness = guest.QueueJoinHappiness,
			QueueCellIndex = guest.QueueCellIndex,
			QueueTargetIndex = guest.QueueTargetIndex,
			QueueTargetX = guest.QueueTargetX,
			QueueTargetY = guest.QueueTargetY,
			LastQueueUpdateTurn = guest.LastQueueUpdateTurn,
			BaseSpeed = guest.BaseSpeed,
			ToiletBonus = guest.ToiletBonus,
			Boost = guest.Boost,
			SmoothedSpeed = guest.SmoothedSpeed,
			MaxSpeed = guest.MaxSpeed,
			MaxForce = guest.MaxForce,
			Navigation = new GuestNavigationSnapshot
			{
				X = navigation.X,
				Y = navigation.Y,
				VelocityX = navigation.VelocityX,
				VelocityY = navigation.VelocityY,
				TargetX = navigation.TargetX,
				TargetY = navigation.TargetY,
				Arrived = navigation.Arrived,
				Index = navigation.Index,
				Count = navigation.Count,
				Route = navigation.Route.ToArray(),
				Cursor = navigation.Cursor,
				WalkCrossings = navigation.WalkCrossings,
				AlsoOpen = navigation.AlsoOpen.ToArray(),
				History = navigation.History,
				PreviousRemaining = navigation.PreviousRemaining,
				OffGrid = navigation.OffGrid,
				Reroutes = navigation.Reroutes
			},
			CellX = guest.CellX,
			CellY = guest.CellY,
			PreviousCellX = guest.PreviousCellX,
			PreviousCellY = guest.PreviousCellY,
			OnGrid = guest.OnGrid,
			OffsetX = guest.OffsetX,
			OffsetY = guest.OffsetY,
			HasWaypoint = guest.HasWaypoint,
			Lane = guest.Lane?.ToArray(),
			LaneIndex = guest.LaneIndex,
			LaneReversed = guest.LaneReversed,
			StateTimer = guest.StateTimer
		};
	}

	/// <summary>
	/// Pure validation of a guest snapshot against this park: schema, bounded numbers, guest and attraction identity,
	/// state-specific structure (arrival lanes, grid position, queue and rider membership) and the cross-references
	/// between guests and attraction queues. Reference-typed parts a corrupt file may null out — the core lists, an
	/// attraction entry, a guest's navigation or route — are named as errors instead of crashing. Nothing is
	/// mutated; every problem found is returned, so a caller can reject a save without touching the live world.
	/// </summary>
	public GuestSnapshotValidation ValidateSnapshot( GuestSimulationSnapshot? snapshot )
	{
		var errors = new List<string>();
		void Error( string message ) => errors.Add( message );
		if ( snapshot == null )
		{
			Error( "The guest snapshot is missing." );
			return new GuestSnapshotValidation( errors );
		}
		void Bounded( int id, float value, string name, float minimum, float maximum )
		{
			if ( !float.IsFinite( value ) || value < minimum || value > maximum )
				Error( $"Guest {id} {name} {value} is outside [{minimum}, {maximum}]." );
		}
		void Finite( int id, float value, string name )
		{
			if ( !float.IsFinite( value ) )
				Error( $"Guest {id} {name} is not finite." );
		}

		if ( !GuestSimulationSnapshot.AcceptedSchemaVersions.Contains( snapshot.SchemaVersion ) )
			Error( $"Guest snapshot schema version {snapshot.SchemaVersion} is not supported; this build reads {string.Join( ", ", GuestSimulationSnapshot.AcceptedSchemaVersions )}." );
		if ( snapshot.TickCount < 0 )
			Error( $"TickCount {snapshot.TickCount} is negative." );
		if ( !double.IsFinite( snapshot.TimeSeconds ) || snapshot.TimeSeconds < 0 )
			Error( $"TimeSeconds {snapshot.TimeSeconds} is negative or not finite." );
		if ( snapshot.NextGuestId < 1 )
			Error( $"NextGuestId {snapshot.NextGuestId} is not positive." );
		if ( snapshot.Admissions < 0 )
			Error( $"Admissions {snapshot.Admissions} is negative." );
		if ( snapshot.Departed < 0 )
			Error( $"Departed {snapshot.Departed} is negative." );
		if ( !float.IsFinite( snapshot.SpawnTimer ) )
			Error( $"SpawnTimer {snapshot.SpawnTimer} is not finite." );
		if ( snapshot.PendingArrivals < 0 )
			Error( $"PendingArrivals {snapshot.PendingArrivals} is negative." );
		if ( snapshot.LaneToggle < 0 )
			Error( $"LaneToggle {snapshot.LaneToggle} is negative." );
		// A file that lost one of its core lists (a JSON null) cannot be cross-checked; the hole is named and
		// validation stops before anything dereferences it, exactly like a wholly missing snapshot.
		if ( snapshot.Guests == null )
		{
			Error( "Guests is missing; a guest snapshot needs its guest list." );
			return new GuestSnapshotValidation( errors );
		}
		if ( snapshot.Attractions == null )
		{
			Error( "Attractions is missing; a guest snapshot needs its attraction list." );
			return new GuestSnapshotValidation( errors );
		}
		if ( snapshot.SeenQueueEdits == null )
		{
			Error( "SeenQueueEdits is missing; a guest snapshot needs one counter per attraction." );
			return new GuestSnapshotValidation( errors );
		}

		// Attractions: the snapshot must describe exactly the registered set.
		var registeredIds = new HashSet<int>();
		foreach ( var attraction in attractions )
		{
			if ( !registeredIds.Add( attraction.AttractionId ) )
				Error( $"The park registers attraction {attraction.AttractionId} twice." );
			if ( attraction is not RideVisitorBridge )
				Error( $"Registered attraction {attraction.AttractionId} ('{attraction.Name}', {attraction.GetType().Name}) has no queue snapshot support." );
		}
		var snapshotIds = new HashSet<int>();
		for ( var index = 0; index < snapshot.Attractions.Length; index++ )
		{
			var attractionSnapshot = snapshot.Attractions[index];
			if ( attractionSnapshot == null )
			{
				Error( $"Attractions[{index}] is missing." );
				continue;
			}
			if ( !snapshotIds.Add( attractionSnapshot.AttractionId ) )
				Error( $"Attractions[{index}] repeats attraction {attractionSnapshot.AttractionId}." );
		}
		foreach ( var attractionId in registeredIds )
			if ( !snapshotIds.Contains( attractionId ) )
				Error( $"Registered attraction {attractionId} has no snapshot." );
		foreach ( var attractionId in snapshotIds )
			if ( !registeredIds.Contains( attractionId ) )
				Error( $"Attraction snapshot {attractionId} is not registered in this park." );
		var seenIds = new HashSet<int>();
		for ( var index = 0; index < snapshot.SeenQueueEdits.Length; index++ )
		{
			var (attractionId, seen) = snapshot.SeenQueueEdits[index];
			if ( !seenIds.Add( attractionId ) )
				Error( $"SeenQueueEdits[{index}] repeats attraction {attractionId}." );
			if ( !snapshotIds.Contains( attractionId ) )
				Error( $"SeenQueueEdits[{index}] names attraction {attractionId}, which has no snapshot." );
			if ( seen < -1 )
				Error( $"SeenQueueEdits[{index}] of attraction {attractionId} is {seen}." );
		}
		foreach ( var attractionId in snapshotIds )
			if ( !seenIds.Contains( attractionId ) )
				Error( $"Attraction {attractionId} has no seen queue-edit counter." );

		// Bridge snapshots: local invariants and map bounds, then the membership indexes for the guest cross-checks.
		var queuedIn = new Dictionary<int, int>(); // guest id -> attraction id whose queue holds it
		var aboardOf = new Dictionary<int, int>(); // guest id -> attraction id whose riders hold it
		for ( var index = 0; index < snapshot.Attractions.Length; index++ )
		{
			var attractionSnapshot = snapshot.Attractions[index];
			if ( attractionSnapshot == null )
				continue; // already reported above: Attractions[index] is missing
			foreach ( var problem in RideVisitorBridge.ValidateSnapshot( attractionSnapshot ) )
				Error( $"Attractions[{index}] (attraction {attractionSnapshot.AttractionId}): {problem}" );
			// A list the file lost is already named by the bridge validation; the membership cross-checks skip it.
			if ( attractionSnapshot.Queue != null )
				foreach ( var guestId in attractionSnapshot.Queue )
				{
					if ( queuedIn.TryGetValue( guestId, out var otherAttraction ) )
						Error( $"Guest {guestId} is queued at attractions {otherAttraction} and {attractionSnapshot.AttractionId}." );
					else
						queuedIn[guestId] = attractionSnapshot.AttractionId;
				}
			if ( attractionSnapshot.OnRide != null )
				foreach ( var guestId in attractionSnapshot.OnRide )
				{
					if ( aboardOf.TryGetValue( guestId, out var otherAttraction ) )
						Error( $"Guest {guestId} is aboard attractions {otherAttraction} and {attractionSnapshot.AttractionId}." );
					else
						aboardOf[guestId] = attractionSnapshot.AttractionId;
				}
			if ( attractionSnapshot.QueueCells != null )
				for ( var cell = 0; cell < attractionSnapshot.QueueCells.Length; cell++ )
				{
					var (x, y) = attractionSnapshot.QueueCells[cell];
					if ( !Grid.InBounds( x, y ) )
						Error( $"Attraction {attractionSnapshot.AttractionId} queue cell {cell} ({x}, {y}) is outside the map." );
				}
			if ( attractionSnapshot.JoinCell is { } join && !Grid.InBounds( join.X, join.Y ) )
				Error( $"Attraction {attractionSnapshot.AttractionId} join cell ({join.X}, {join.Y}) is outside the map." );
		}

		// Guests: identity, bounds and per-guest structure first, state cross-references after.
		var guestIds = new HashSet<int>();
		var typeCount = Math.Max( 1, Settings.Types.Count );
		GuestSnapshot? previous = null;
		for ( var index = 0; index < snapshot.Guests.Length; index++ )
		{
			var guest = snapshot.Guests[index];
			if ( guest == null )
			{
				Error( $"Guests[{index}] is missing." );
				continue;
			}
			if ( guest.Id < 1 )
				Error( $"Guests[{index}] id {guest.Id} is not positive." );
			if ( previous != null && guest.Id <= previous.Id )
				Error( $"Guests[{index}] id {guest.Id} does not follow guest id {previous.Id}; guests are saved in ascending id order." );
			previous = guest;
			if ( !guestIds.Add( guest.Id ) )
				Error( $"Guest {guest.Id} appears more than once." );
			if ( guest.Id >= snapshot.NextGuestId )
				Error( $"Guest {guest.Id} is not below the next guest id {snapshot.NextGuestId}." );
			if ( !Enum.IsDefined( guest.State ) )
				Error( $"Guest {guest.Id} state {(int)guest.State} is not a guest state." );
			else if ( guest.State == GuestState.Gone )
				Error( $"Guest {guest.Id} is already gone; gone guests are removed at the end of their tick." );
			if ( !Enum.IsDefined( guest.Thought ) )
				Error( $"Guest {guest.Id} thought {(int)guest.Thought} is not a guest thought." );
			if ( !Enum.IsDefined( guest.BubbleThought ) || guest.BubbleTurn < 0 || guest.BubbleTurn > ParkCalendar.Turn( snapshot.TickCount ) )
				Error( $"Guest {guest.Id} bubble {(int)guest.BubbleThought} at turn {guest.BubbleTurn} is not a thought bubble." );
			if ( (uint)guest.Type >= (uint)typeCount )
				Error( $"Guest {guest.Id} type {guest.Type} is not one of the park's {typeCount} guest types." );
			// Schema 1 grew hunger, thirst and toilet to the old model's 150 cap; the meter model (schema 2 on) holds 0..100.
			var needCeiling = snapshot.SchemaVersion == 1 ? 150 : 100;
			Bounded( guest.Id, guest.Hunger, nameof( guest.Hunger ), 0, needCeiling );
			Bounded( guest.Id, guest.Thirst, nameof( guest.Thirst ), 0, needCeiling );
			Bounded( guest.Id, guest.Toilet, nameof( guest.Toilet ), 0, needCeiling );
			Bounded( guest.Id, guest.Energy, nameof( guest.Energy ), 0, 100 );
			Bounded( guest.Id, guest.Happiness, nameof( guest.Happiness ), 0, 100 );
			Bounded( guest.Id, guest.Nausea, nameof( guest.Nausea ), 0, 100 );
			Bounded( guest.Id, guest.Litter, nameof( guest.Litter ), 0, 100 );
			if ( guest.PrankFlag is not (0 or 100 or 101 or 102) )
				Error( $"Guest {guest.Id} prankery flag {guest.PrankFlag} is not 0, 100, 101 or 102." );
			if ( guest.BalloonLife is < 0 or > GuestAppearance.MaximumBalloonLife || (guest.HasBalloon && guest.BalloonLife == 0) )
				Error( $"Guest {guest.Id} balloon life {guest.BalloonLife} is outside 0..255 or holds a balloon at 0." );
			if ( guest.BinId < 0 || (guest.BinId != 0) != (guest.State == GuestState.ThrowingAwayLitter) )
				Error( $"Guest {guest.Id} bin {guest.BinId} does not match its state {guest.State}." );
			Bounded( guest.Id, guest.QueueJoinHappiness, nameof( guest.QueueJoinHappiness ), 0, 100 );
			if ( guest.TimeInParkSeconds is { } elapsed && (!double.IsFinite( elapsed ) || elapsed < 0 || elapsed > Guest.MaximumTimeInParkSeconds) )
				Error( $"Guest {guest.Id} time in park is negative, not finite or exceeds the supported duration." );
			Bounded( guest.Id, guest.SecondsSinceRide, nameof( guest.SecondsSinceRide ), 0, 1_000_000 );
			// Schema 1 predates the histories (they start empty); from schema 2 on they must be whole.
			foreach ( var (name, history) in new[] { (nameof( guest.UsedHistory ), guest.UsedHistory), (nameof( guest.TemporaryHistory ), guest.TemporaryHistory) } )
			{
				if ( history == null )
				{
					if ( snapshot.SchemaVersion != 1 )
						Error( $"Guest {guest.Id} has no {name}." );
				}
				else if ( history.Length != Guest.HistorySlots || history.Any( id => id < 0 ) )
					Error( $"Guest {guest.Id} {name} is not {Guest.HistorySlots} non-negative attraction ids." );
			}
			// Schema 1 stores the former seconds-based exit value as a fraction; the truncated counter must stay a counter
			// (the live decrement has no floor, so negative counts are legitimate). Schema 2 counters are whole by construction.
			if ( !double.IsFinite( guest.ExitLevel ) || Math.Truncate( guest.ExitLevel ) is < int.MinValue or > int.MaxValue )
				Error( $"Guest {guest.Id} exit counter {guest.ExitLevel} does not fit the truncated counter." );
			else if ( snapshot.SchemaVersion != 1 && Math.Truncate( guest.ExitLevel ) != guest.ExitLevel )
				Error( $"Guest {guest.Id} exit counter {guest.ExitLevel} is not a whole number of decisions." );
			if ( guest.Money < 0 )
				Error( $"Guest {guest.Id} Money {guest.Money} is negative." );
			if ( guest.RidesTaken < 0 )
				Error( $"Guest {guest.Id} RidesTaken {guest.RidesTaken} is negative." );
			if ( guest.QueueMoveDelay < 0 )
				Error( $"Guest {guest.Id} QueueMoveDelay {guest.QueueMoveDelay} is negative." );
			if ( guest.QueueCellIndex < 0 )
				Error( $"Guest {guest.Id} QueueCellIndex {guest.QueueCellIndex} is negative." );
			Finite( guest.Id, guest.HeadingX, nameof( guest.HeadingX ) );
			Finite( guest.Id, guest.HeadingY, nameof( guest.HeadingY ) );
			Finite( guest.Id, guest.DistanceWalked, nameof( guest.DistanceWalked ) );
			Finite( guest.Id, guest.DrawX, nameof( guest.DrawX ) );
			Finite( guest.Id, guest.DrawY, nameof( guest.DrawY ) );
			Finite( guest.Id, guest.DrawDistanceWalked, nameof( guest.DrawDistanceWalked ) );
			Finite( guest.Id, guest.StepFromX, nameof( guest.StepFromX ) );
			Finite( guest.Id, guest.StepFromY, nameof( guest.StepFromY ) );
			Finite( guest.Id, guest.StepFromDistance, nameof( guest.StepFromDistance ) );
			Finite( guest.Id, guest.OffsetX, nameof( guest.OffsetX ) );
			Finite( guest.Id, guest.OffsetY, nameof( guest.OffsetY ) );
			Finite( guest.Id, guest.QueueTargetX, nameof( guest.QueueTargetX ) );
			Finite( guest.Id, guest.QueueTargetY, nameof( guest.QueueTargetY ) );
			Finite( guest.Id, guest.StateTimer, nameof( guest.StateTimer ) );
			if ( guest.DecisionTimer is { } decisionTimer )
				Finite( guest.Id, decisionTimer, nameof( guest.DecisionTimer ) );
			if ( !Enum.IsDefined( guest.Activity ) )
				Error( $"Guest {guest.Id} activity {(int)guest.Activity} is not a state-6 activity." );
			else if ( guest.Activity != GuestActivity.None && guest.State != GuestState.DoingNothing )
				Error( $"Guest {guest.Id} has activity {guest.Activity} outside state 6 (DoingNothing)." );
			if ( guest.Navigation == null )
				Error( $"Guests[{index}] (guest {guest.Id}) has no navigation snapshot." );
			else
				ValidateGuestStructure( guest, Error );
		}

		// State cross-references: queue and rider membership both ways.
		for ( var index = 0; index < snapshot.Guests.Length; index++ )
		{
			var guest = snapshot.Guests[index];
			if ( guest == null )
				continue;
			var inQueue = guest.State is GuestState.MovingUpQueue or GuestState.Queueing or GuestState.Boarding or GuestState.WaitingToBoard;
			if ( inQueue )
			{
				if ( !queuedIn.TryGetValue( guest.Id, out var queueAttraction ) )
					Error( $"Guest {guest.Id} is in state {guest.State} but no attraction's queue holds it." );
				else if ( queueAttraction != guest.AttractionId )
					Error( $"Guest {guest.Id} queues for attraction {guest.AttractionId} but attraction {queueAttraction}'s queue holds it." );
				if ( guest.QueuePosition < 0 )
					Error( $"Guest {guest.Id} queues without a queue position." );
				if ( guest.QueueJoinTurn < 0 )
					Error( $"Guest {guest.Id} queues without a join turn." );
				if ( guest.QueueTargetIndex < 0 )
					Error( $"Guest {guest.Id} queues without a queue target index." );
				if ( guest.State == GuestState.Queueing && guest.LastQueueUpdateTurn < 0 )
					Error( $"Guest {guest.Id} stands in a queue without a last queue update turn." );
			}
			else
			{
				if ( queuedIn.TryGetValue( guest.Id, out var queueAttraction ) )
					Error( $"Guest {guest.Id} is in state {guest.State} but is still held in attraction {queueAttraction}'s queue." );
				if ( guest.QueuePosition >= 0 )
					Error( $"Guest {guest.Id} is in state {guest.State} but still carries queue position {guest.QueuePosition}." );
			}
			if ( guest.State == GuestState.Using )
			{
				if ( !aboardOf.TryGetValue( guest.Id, out var rideAttraction ) )
					Error( $"Guest {guest.Id} rides but no attraction holds it aboard." );
				else if ( rideAttraction != guest.AttractionId )
					Error( $"Guest {guest.Id} rides attraction {guest.AttractionId} but is held aboard attraction {rideAttraction}." );
			}
			else if ( aboardOf.TryGetValue( guest.Id, out var rideAttraction ) )
				Error( $"Guest {guest.Id} is in state {guest.State} but is held aboard attraction {rideAttraction}." );
			if ( guest.AttractionId != 0 )
			{
				if ( !snapshotIds.Contains( guest.AttractionId ) )
					Error( $"Guest {guest.Id} names attraction {guest.AttractionId}, which is not registered." );
				else if ( !inQueue && guest.State is not (GuestState.GoingToRide or GuestState.Using) )
					Error( $"Guest {guest.Id} is in state {guest.State} while bound to attraction {guest.AttractionId}." );
			}
			else if ( guest.State == GuestState.GoingToRide )
				Error( $"Guest {guest.Id} heads for a ride without naming an attraction." );
		}
		foreach ( var (guestId, attractionId) in queuedIn )
			if ( !guestIds.Contains( guestId ) )
				Error( $"Attraction {attractionId}'s queue holds guest {guestId}, which does not exist." );
		foreach ( var (guestId, attractionId) in aboardOf )
			if ( !guestIds.Contains( guestId ) )
				Error( $"Attraction {attractionId}'s riders hold guest {guestId}, which does not exist." );

		return new GuestSnapshotValidation( errors );
	}

	/// <summary>Per-guest structure that needs the park's grid: arrival lane shape and position, cells, navigation buffers.</summary>
	private void ValidateGuestStructure( GuestSnapshot guest, Action<string> error )
	{
		var onLane = guest.State is GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth
			or GuestState.AtTicketBooth or GuestState.EnteringPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome;
		if ( onLane )
		{
			if ( guest.Lane == null || guest.Lane.Length < 2 )
				error( $"Guest {guest.Id} is in state {guest.State} without an arrival lane of at least two cells." );
			else if ( (uint)guest.LaneIndex >= (uint)guest.Lane.Length )
				error( $"Guest {guest.Id} lane index {guest.LaneIndex} is outside its {guest.Lane.Length}-cell lane." );
			if ( guest.OnGrid || !guest.Navigation.OffGrid )
				error( $"Guest {guest.Id} is in state {guest.State} but is marked on the path grid." );
		}
		else if ( guest.State == GuestState.LeavingPark && guest.Lane != null )
		{
			if ( guest.Lane.Length < 2 || (uint)guest.LaneIndex >= (uint)guest.Lane.Length )
				error( $"Guest {guest.Id} walks a lane home with index {guest.LaneIndex} of {guest.Lane.Length} cells." );
			if ( guest.OnGrid || !guest.Navigation.OffGrid )
				error( $"Guest {guest.Id} walks a lane home but is marked on the path grid." );
		}
		else
		{
			if ( !guest.OnGrid || guest.Navigation.OffGrid )
				error( $"Guest {guest.Id} is in state {guest.State} but is not on the path grid." );
			if ( guest.Lane != null )
				error( $"Guest {guest.Id} is in state {guest.State} but still carries an arrival lane." );
		}
		if ( guest.Lane != null )
		{
			for ( var cell = 0; cell < guest.Lane.Length; cell++ )
			{
				var (x, y) = guest.Lane[cell];
				if ( !Grid.InBounds( x, y ) )
					error( $"Guest {guest.Id} lane cell {cell} ({x}, {y}) is outside the map." );
			}
		}
		if ( !Grid.InBounds( guest.CellX, guest.CellY ) )
			error( $"Guest {guest.Id} cell ({guest.CellX}, {guest.CellY}) is outside the map." );
		if ( (guest.PreviousCellX >= 0 || guest.PreviousCellY >= 0) && !Grid.InBounds( guest.PreviousCellX, guest.PreviousCellY ) )
			error( $"Guest {guest.Id} previous cell ({guest.PreviousCellX}, {guest.PreviousCellY}) is outside the map." );
		var navigation = guest.Navigation;
		if ( !Grid.InBounds( navigation.X >> 16, navigation.Y >> 16 ) )
			error( $"Guest {guest.Id} navigation position ({navigation.X >> 16}, {navigation.Y >> 16}) is outside the map." );
		if ( !Grid.InBounds( navigation.TargetX >> 16, navigation.TargetY >> 16 ) )
			error( $"Guest {guest.Id} navigation target ({navigation.TargetX >> 16}, {navigation.TargetY >> 16}) is outside the map." );
		if ( Math.Abs( (long)navigation.VelocityX ) > GuestSpeed.VelocityBound || Math.Abs( (long)navigation.VelocityY ) > GuestSpeed.VelocityBound )
			error( $"Guest {guest.Id} navigation velocity ({navigation.VelocityX}, {navigation.VelocityY}) exceeds the speed cap." );
		if ( navigation.Route == null )
			error( $"Guest {guest.Id} has no route." );
		else
		{
			if ( navigation.Cursor < 0 || navigation.Cursor > navigation.Route.Length )
				error( $"Guest {guest.Id} navigation cursor {navigation.Cursor} is outside its {navigation.Route.Length}-cell route." );
			else
			{
				var buffered = Math.Min( GuestNavigation.BufferedWaypoints, navigation.Route.Length - navigation.Cursor );
				if ( navigation.Count != buffered )
					error( $"Guest {guest.Id} buffers {navigation.Count} waypoints, but its route from cursor {navigation.Cursor} gives {buffered}." );
				if ( navigation.Count == 0 ? navigation.Index != 0 : (uint)navigation.Index >= (uint)navigation.Count )
					error( $"Guest {guest.Id} waypoint index {navigation.Index} is outside its {navigation.Count} buffered waypoints." );
			}
			for ( var cell = 0; cell < navigation.Route.Length; cell++ )
			{
				var (x, y) = navigation.Route[cell];
				if ( !Grid.InBounds( x, y ) )
					error( $"Guest {guest.Id} route cell {cell} ({x}, {y}) is outside the map." );
			}
		}
		if ( navigation.History is < 0 or > 0x7fff )
			error( $"Guest {guest.Id} progress history {navigation.History} is outside its 15-bit window." );
		if ( navigation.PreviousRemaining < -1 )
			error( $"Guest {guest.Id} previous remaining distance {navigation.PreviousRemaining} is negative." );
		if ( navigation.Reroutes < 0 || navigation.WalkCrossings < 0 )
			error( $"Guest {guest.Id} navigation counters ({navigation.Reroutes} reroutes, {navigation.WalkCrossings} crossings) are negative." );
		if ( navigation.AlsoOpen == null )
			error( $"Guest {guest.Id} has no extra-open-cell list." );
		else
			for ( var cell = 0; cell < navigation.AlsoOpen.Length; cell++ )
			{
				var (x, y) = navigation.AlsoOpen[cell];
				if ( !Grid.InBounds( x, y ) )
					error( $"Guest {guest.Id} extra open cell {cell} ({x}, {y}) is outside the map." );
			}
		if ( guest.BaseSpeed is < 60 or > 140 )
			error( $"Guest {guest.Id} base speed {guest.BaseSpeed} is not one of the walk speeds." );
		if ( guest.MaxSpeed is < GuestSpeed.Floor or > GuestSpeed.VelocityBound )
			error( $"Guest {guest.Id} max speed {guest.MaxSpeed} is outside the speed setter's range." );
		if ( guest.MaxForce is < GuestSpeed.Floor or > GuestSpeed.ForceBound )
			error( $"Guest {guest.Id} max force {guest.MaxForce} is outside the speed setter's range." );
		if ( !float.IsFinite( guest.SmoothedSpeed ) || guest.SmoothedSpeed < 0 )
			error( $"Guest {guest.Id} smoothed speed {guest.SmoothedSpeed} is negative or not finite." );
	}

	/// <summary>
	/// Restores a validated snapshot into this simulation in two passes: pass one materialises every guest as a fresh
	/// object with its identity and all fields; pass two restores each attraction's queue, then commits the guests,
	/// counters, arrival stream, explicit RNG state and the derived occupant index. Anything
	/// <see cref="ValidateSnapshot"/> rejects throws <see cref="InvalidDataException"/> before this simulation is
	/// touched. The economy bindings (<see cref="Payments"/>, <see cref="ArrivalClock"/>, <see cref="ParkPoints"/>,
	/// <see cref="ArrivalsHalted"/>) are live references the caller re-attaches; the restore does not change them.
	/// Every attraction in the snapshot must already be registered (see <see cref="Register"/>).
	/// </summary>
	public void RestoreSnapshot( GuestSimulationSnapshot snapshot )
	{
		var validation = ValidateSnapshot( snapshot );
		if ( !validation.IsValid )
			throw new InvalidDataException( "The guest snapshot does not restore: " + string.Join( " ", validation.Errors ) );
		// Pass 1: identities first — every guest becomes a fresh object; nothing live is replaced yet.
		var restored = new List<Guest>( snapshot.Guests.Length );
		var restoredById = new Dictionary<int, Guest>( snapshot.Guests.Length );
		foreach ( var guestSnapshot in snapshot.Guests )
		{
			var guest = new Guest( guestSnapshot.Id, guestSnapshot.Type );
			ApplyGuestSnapshot( guest, guestSnapshot, snapshot.SchemaVersion );
			restored.Add( guest );
			restoredById.Add( guest.Id, guest );
		}
		// Pass 2: references — each attraction's queue and riders into its bridge, then the candidate commits as a whole.
		var bridges = new Dictionary<int, RideVisitorBridge>( attractions.Count );
		foreach ( var attraction in attractions )
			bridges.Add( attraction.AttractionId, (RideVisitorBridge)attraction );
		foreach ( var attractionSnapshot in snapshot.Attractions )
			bridges[attractionSnapshot.AttractionId].RestoreSnapshot( attractionSnapshot );
		guests.Clear();
		guests.AddRange( restored );
		byId.Clear();
		foreach ( var (guestId, guest) in restoredById )
			byId.Add( guestId, guest );
		seenQueueEdits.Clear();
		foreach ( var (attractionId, seen) in snapshot.SeenQueueEdits )
			seenQueueEdits[attractionId] = seen;
		random = new GuestRandom( snapshot.RandomState );
		nextId = snapshot.NextGuestId;
		TickCount = snapshot.TickCount;
		TimeSeconds = snapshot.TimeSeconds;
		Admissions = snapshot.Admissions;
		Departed = snapshot.Departed;
		arrivalTimerStart = snapshot.ArrivalTimerStart;
		deliveringArrivals = snapshot.DeliveringArrivals;
		spawnTimer = snapshot.SpawnTimer;
		pendingArrivals = snapshot.PendingArrivals;
		laneToggle = snapshot.LaneToggle;
		ArrivalsEnabled = snapshot.ArrivalsEnabled;
		seenGridVersion = snapshot.SeenGridVersion;
		RebuildOccupants();
	}

	/// <summary>Copies a validated guest snapshot into a fresh <see cref="Guest"/> (route, lane and buffer arrays are copied again).</summary>
	private static void ApplyGuestSnapshot( Guest guest, GuestSnapshot snapshot, int schemaVersion )
	{
		guest.State = snapshot.State;
		guest.HeadingX = snapshot.HeadingX;
		guest.HeadingY = snapshot.HeadingY;
		guest.IsMoving = snapshot.IsMoving;
		guest.DistanceWalked = snapshot.DistanceWalked;
		guest.DrawX = snapshot.DrawX;
		guest.DrawY = snapshot.DrawY;
		guest.DrawDistanceWalked = snapshot.DrawDistanceWalked;
		guest.StepFromX = snapshot.StepFromX;
		guest.StepFromY = snapshot.StepFromY;
		guest.StepFromDistance = snapshot.StepFromDistance;
		guest.StepTick = snapshot.StepTick;
		// Schema 1's meters grew to the old model's 150 cap; the counter model tops out at 100, so that migration clamps.
		guest.Hunger = schemaVersion == 1 ? Math.Min( 100, snapshot.Hunger ) : snapshot.Hunger;
		guest.Thirst = schemaVersion == 1 ? Math.Min( 100, snapshot.Thirst ) : snapshot.Thirst;
		guest.Toilet = schemaVersion == 1 ? Math.Min( 100, snapshot.Toilet ) : snapshot.Toilet;
		guest.Energy = snapshot.Energy;
		guest.Nausea = snapshot.Nausea;
		guest.Happiness = snapshot.Happiness;
		guest.Money = snapshot.Money;
		// Schema 1 stored the former seconds-based value as a fraction; truncating toward zero keeps it a counter (one per 0.992 s).
		guest.ExitLevel = (int)Math.Truncate( snapshot.ExitLevel );
		guest.TimeInParkSeconds = snapshot.TimeInParkSeconds;
		guest.SecondsSinceRide = snapshot.SecondsSinceRide;
		guest.RidesTaken = snapshot.RidesTaken;
		// Saves before schema 7 hold OpenTPW's former thoughts: "Super happy" and "Okay", which the original never thinks, become "Happy!" and no thought.
		guest.Thought = schemaVersion < 7 ? snapshot.Thought switch
		{
			GuestThought.SuperHappy => GuestThought.Happy,
			GuestThought.Okay => GuestThought.None,
			var thought => thought
		} : snapshot.Thought;
		guest.SideshowWon = snapshot.SideshowWon;
		guest.BubbleThought = snapshot.BubbleThought;
		guest.BubbleTurn = snapshot.BubbleTurn;
		guest.AttractionId = snapshot.AttractionId;
		guest.LastAttractionId = snapshot.LastAttractionId;
		snapshot.UsedHistory?.CopyTo( guest.UsedHistory, 0 );
		snapshot.TemporaryHistory?.CopyTo( guest.TemporaryHistory, 0 );
		guest.QueuePosition = snapshot.QueuePosition;
		guest.QueueJoinTurn = snapshot.QueueJoinTurn;
		guest.LastQueueWaitTurns = snapshot.LastQueueWaitTurns;
		guest.QueueMoveDelay = snapshot.QueueMoveDelay;
		guest.QueueCalled = snapshot.QueueCalled;
		guest.QueueStandingSinceTurn = snapshot.QueueStandingSinceTurn;
		guest.InterludeTurn = snapshot.InterludeTurn;
		guest.InQueueInterlude = snapshot.InQueueInterlude;
		guest.Activity = snapshot.Activity;
		guest.Litter = snapshot.Litter;
		guest.BinId = snapshot.BinId;
		guest.PrankFlag = snapshot.PrankFlag;
		guest.BalloonLife = snapshot.BalloonLife;
		guest.HasBalloon = snapshot.HasBalloon;
		guest.InCostume = snapshot.InCostume;
		guest.QueueJoinHappiness = snapshot.QueueJoinHappiness;
		guest.QueueCellIndex = snapshot.QueueCellIndex;
		guest.QueueTargetIndex = snapshot.QueueTargetIndex;
		guest.QueueTargetX = snapshot.QueueTargetX;
		guest.QueueTargetY = snapshot.QueueTargetY;
		guest.LastQueueUpdateTurn = snapshot.LastQueueUpdateTurn;
		guest.BaseSpeed = snapshot.BaseSpeed;
		guest.ToiletBonus = snapshot.ToiletBonus;
		guest.Boost = snapshot.Boost;
		guest.SmoothedSpeed = snapshot.SmoothedSpeed;
		guest.MaxSpeed = snapshot.MaxSpeed;
		guest.MaxForce = snapshot.MaxForce;
		guest.CellX = snapshot.CellX;
		guest.CellY = snapshot.CellY;
		guest.PreviousCellX = snapshot.PreviousCellX;
		guest.PreviousCellY = snapshot.PreviousCellY;
		guest.OnGrid = snapshot.OnGrid;
		guest.OffsetX = snapshot.OffsetX;
		guest.OffsetY = snapshot.OffsetY;
		guest.HasWaypoint = snapshot.HasWaypoint;
		guest.Lane = snapshot.Lane?.ToArray();
		guest.LaneIndex = snapshot.LaneIndex;
		guest.LaneReversed = snapshot.LaneReversed;
		guest.StateTimer = snapshot.StateTimer;
		var navigation = guest.Navigation;
		var navigationSnapshot = snapshot.Navigation;
		navigation.X = navigationSnapshot.X;
		navigation.Y = navigationSnapshot.Y;
		navigation.VelocityX = navigationSnapshot.VelocityX;
		navigation.VelocityY = navigationSnapshot.VelocityY;
		navigation.TargetX = navigationSnapshot.TargetX;
		navigation.TargetY = navigationSnapshot.TargetY;
		navigation.Arrived = navigationSnapshot.Arrived;
		navigation.Index = navigationSnapshot.Index;
		navigation.Count = navigationSnapshot.Count;
		navigation.Route = navigationSnapshot.Route.ToArray();
		navigation.Cursor = navigationSnapshot.Cursor;
		navigation.WalkCrossings = navigationSnapshot.WalkCrossings;
		navigation.AlsoOpen = navigationSnapshot.AlsoOpen.ToArray();
		navigation.History = navigationSnapshot.History;
		navigation.PreviousRemaining = navigationSnapshot.PreviousRemaining;
		navigation.OffGrid = navigationSnapshot.OffGrid;
		navigation.Reroutes = navigationSnapshot.Reroutes;
		for ( var index = 0; index < navigationSnapshot.Count; index++ )
			navigation.Buffer[index] = navigation.Route[navigationSnapshot.Cursor + index];
	}

	/// <summary>Thing-table part of <see cref="WorldStateHash"/>: gameplay fields, in id order.
	/// TimeInParkSeconds is saved display metadata and is excluded, like interpolated draw positions.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( TickCount );
		hash.Add( TimeSeconds );
		hash.Add( nextId );
		hash.Add( Admissions );
		hash.Add( Departed );
		hash.Add( arrivalTimerStart );
		hash.Add( deliveringArrivals );
		hash.Add( spawnTimer );
		hash.Add( pendingArrivals );
		hash.Add( laneToggle );
		hash.Add( ArrivalsEnabled );
		hash.Add( guests.Count );
		foreach ( var guest in guests )
		{
			hash.Add( guest.Id );
			hash.Add( guest.Type );
			hash.Add( (int)guest.State );
			hash.Add( guest.X );
			hash.Add( guest.Y );
			hash.Add( guest.HeadingX );
			hash.Add( guest.HeadingY );
			hash.Add( guest.IsMoving );
			hash.Add( guest.DistanceWalked );
			hash.Add( guest.Hunger );
			hash.Add( guest.Thirst );
			hash.Add( guest.Toilet );
			hash.Add( guest.Energy );
			hash.Add( guest.Nausea );
			hash.Add( guest.Happiness );
			hash.Add( guest.Money );
			hash.Add( guest.ExitLevel );
			hash.Add( guest.SecondsSinceRide );
			hash.Add( guest.RidesTaken );
			hash.Add( (int)guest.Thought );
			hash.Add( guest.AttractionId );
			hash.Add( guest.LastAttractionId );
			foreach ( var id in guest.UsedHistory )
				hash.Add( id );
			foreach ( var id in guest.TemporaryHistory )
				hash.Add( id );
			hash.Add( guest.CellX );
			hash.Add( guest.CellY );
			hash.Add( guest.PreviousCellX );
			hash.Add( guest.PreviousCellY );
			hash.Add( guest.OnGrid );
			hash.Add( guest.OffsetX );
			hash.Add( guest.OffsetY );
			hash.Add( guest.HasWaypoint );
			hash.Add( guest.BaseSpeed );
			hash.Add( guest.ToiletBonus );
			hash.Add( (int)guest.Boost );
			hash.Add( guest.SmoothedSpeed );
			hash.Add( guest.MaxSpeed );
			hash.Add( guest.MaxForce );
			guest.Navigation.AddCanonicalState( hash );
			hash.Add( guest.Lane?.Length ?? -1 );
			foreach ( var (x, y) in guest.Lane ?? [] )
				hash.Add( x * 4096L + y );
			hash.Add( guest.LaneIndex );
			hash.Add( guest.LaneReversed );
			hash.Add( guest.StateTimer );
			hash.Add( (byte)guest.Activity );
			hash.Add( guest.Litter );
			hash.Add( guest.BinId );
			hash.Add( guest.PrankFlag );
			hash.Add( guest.BalloonLife );
			hash.Add( guest.HasBalloon );
			hash.Add( guest.InCostume );
			hash.Add( guest.QueuePosition );
			hash.Add( guest.QueueJoinTurn );
			hash.Add( guest.LastQueueWaitTurns );
			hash.Add( guest.QueueMoveDelay );
			hash.Add( guest.QueueCalled );
			hash.Add( guest.QueueStandingSinceTurn );
			hash.Add( guest.InterludeTurn );
			hash.Add( guest.InQueueInterlude );
			hash.Add( guest.QueueJoinHappiness );
			hash.Add( guest.QueueCellIndex );
			hash.Add( guest.QueueTargetIndex );
			hash.Add( guest.QueueTargetX );
			hash.Add( guest.QueueTargetY );
			hash.Add( guest.LastQueueUpdateTurn );
		}
		// The queue edge detectors decide whether the next tick recomputes queues and re-evaluates queued guests.
		hash.Add( seenGridVersion );
		hash.Add( attractions.Count );
		foreach ( var attraction in attractions )
		{
			hash.Add( attraction.AttractionId );
			hash.Add( attraction.IsOpen );
			hash.Add( attraction.EntranceCell.X * 4096L + attraction.EntranceCell.Y );
			hash.Add( attraction.ExitCell.X * 4096L + attraction.ExitCell.Y );
			hash.Add( seenQueueEdits.TryGetValue( attraction.AttractionId, out var seen ) ? seen : -1 );
			if ( attraction is RideVisitorBridge bridge )
				bridge.AddCanonicalState( hash );
			else
				hash.Add( attraction.QueueLength );
		}
	}

	/// <summary>FNV-1a over guest gameplay state, the RNG and counters; display-only TimeInParkSeconds is excluded.</summary>
	public ulong ComputeStateHash()
	{
		var hash = 14695981039346656037UL;
		void Add( long value )
		{
			for ( var shift = 0; shift < 64; shift += 8 )
			{
				hash ^= (byte)(value >> shift);
				hash *= 1099511628211UL;
			}
		}
		void AddFloat( float value ) => Add( BitConverter.SingleToInt32Bits( value ) );
		Add( TickCount );
		Add( (long)random.State );
		Add( nextId );
		Add( Admissions );
		Add( guests.Count );
		foreach ( var guest in guests )
		{
			Add( guest.Id );
			Add( guest.Type );
			Add( (int)guest.State );
			AddFloat( guest.X );
			AddFloat( guest.Y );
			AddFloat( guest.Hunger );
			AddFloat( guest.Thirst );
			AddFloat( guest.Toilet );
			AddFloat( guest.Energy );
			AddFloat( guest.Nausea );
			AddFloat( guest.Litter );
			Add( guest.BinId );
			Add( guest.PrankFlag );
			Add( guest.BalloonLife );
			Add( guest.HasBalloon ? 1 : 0 );
			Add( guest.InCostume ? 1 : 0 );
			AddFloat( guest.Happiness );
			Add( guest.ExitLevel );
			Add( guest.Money );
			Add( guest.AttractionId );
			Add( (int)guest.Thought );
			Add( guest.CellX * 4096 + guest.CellY );
			Add( guest.QueuePosition );
			Add( guest.QueueMoveDelay );
		}
		return hash;
	}
}
