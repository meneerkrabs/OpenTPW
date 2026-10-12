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
public sealed class GuestSimulation : IRideVisitorHost
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
	public const float LeaveHappiness = 10;
	public const float ToiletLeaveLevel = 80;
	public const float TicketBoothSeconds = 1.0f;
	public const float ExitRideSeconds = 1.0f;
	public const float WaitForBusSeconds = 3.0f;
	public const float SpawnSpacingSeconds = 0.4f;

	private readonly List<Guest> guests = new();
	private readonly Dictionary<int, Guest> byId = new();
	private readonly List<IRideVisitorBridge> attractions = new();
	private GuestRandom random;
	private int nextId = 1;
	// The arrival timer's start turn (0 until the first group: the original's timer starts unset) and the group being dropped off.
	private long arrivalTimerStart;
	private bool deliveringArrivals;
	private float spawnTimer;
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
	/// True while the park's Traffic Lights fixed item is placed. The group size then uses 0.8 instead of 1.2. The proven rule: the
	/// original reads the signed 32-bit word at +48 of that thing (<c>mTopLeft</c> and <c>mEntryPos</c>, cell ids
	/// <c>1 + X + (Y &lt;&lt; 7)</c>) and uses 0.8 when it is positive. Fixed items are created at cell id 1 and the writer search found
	/// no later store, so the word is 1 while the item exists. Null or false is OpenTPW's own fallback (1.2): the original reads
	/// the word without checking that the thing exists, so what it does without the lights is not established.
	/// </summary>
	// [BIN:STP-PPC:0x100CB564 arrival factor] 0x1083c8 returns the thing at ThingArray[u16 game+0x1DA734] (mTrafficLights, written at 0x104cb0 and saved by 0x105d3c); 0xcb568 reads its +48 word; positive selects 0.8f
	// [DATA:Easymode.TPWI:mTopLeft] the saved Lights record (Info.Id 1603 in Jungle; the Lights ids are 1603/2603/4603/3603 for jungle/hallow/fantasy/space) holds mTopLeft = mEntryPos = mExitPos = 1, so the word is positive
	public Func<bool>? TrafficLightsPlaced { get; set; }
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
		var type = random.Next( Math.Max( 1, Settings.Types.Count ) );
		var guest = new Guest( nextId++, type );
		var peep = Settings.Types[Math.Min( type, Settings.Types.Count - 1 )];
		var variance = peep.StartingCash * Settings.StartingCashVarPercent / 100;
		guest.Money = Math.Max( 0, peep.StartingCash + random.Next( -variance, variance ) );
		guest.Happiness = Settings.StartingHappiness;
		guest.ExitLevel = Settings.ExitLevelSeconds + random.Next( -Settings.ExitLevelVarSeconds, Settings.ExitLevelVarSeconds );
		guest.OffsetX = (random.NextFloat() - 0.5f) * 0.5f;
		guest.OffsetY = (random.NextFloat() - 0.5f) * 0.5f;
		guest.DecisionTimer = random.NextFloat() * Settings.DecisionIntervalSeconds;
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
		guest.State = GuestState.WalkingAround;
		guest.DecisionTimer = 0;
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
		if ( !float.IsFinite( deltaSeconds ) || deltaSeconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( deltaSeconds ) );
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
	/// have passed since the last group was dropped off, a new group of <see cref="ArrivalGroupSize"/> guests arrives; its guests
	/// step off one by one, and the timer restarts once the group is empty.
	/// </summary>
	// [BIN:STP-PPC:0x100CB2AC arrival update] if no group is active and 0x101210F8's (turn >> 2) − (start >> 2) exceeds Arrival.TimeBetweenArrivals (settings +0x2D4), the group size is set and the group starts; at each stop one guest is created and the count falls; at 0 the timer restarts (0x10121098)
	// [APPROX:ARRIVAL-001] guests step off every 0.4 s instead of at the original bus's stops (bus sizes 1/2/3 for groups under 36, under 61, larger) — evidence needed: the arrival vehicle's stop cycle (0x10109784 states)
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
		}
		if ( !deliveringArrivals )
			return;
		spawnTimer -= deltaSeconds;
		if ( spawnTimer > 0 )
			return;
		if ( pendingArrivals < 1 )
		{
			deliveringArrivals = false;
			arrivalTimerStart = turn;
			return;
		}
		spawnTimer = SpawnSpacingSeconds;
		pendingArrivals--;
		if ( guests.Count < Settings.MaximumGuests )
			SpawnArrival();
	}

	/// <summary>
	/// Guests in the next group: 0.8 (OpenTPW's own 1.2 without the Traffic Lights, see <see cref="TrafficLightsPlaced"/>) × (park points +
	/// Arrival.NewParkBonus) / Arrival.PointsPerVisitor, at least Arrival.MinPeople, and none while the park is bankrupt; cut to
	/// the park limit (<see cref="ArrivalParkLimit"/>, <see cref="OnlineArrivalParkLimit"/> for an online visit).
	/// </summary>
	// [BIN:STP-PPC:0x100CB4E8 arrival group] 0 in world mode 4 (bankrupt); else (int)(factor (0.8f with the Traffic Lights thing's +48 word positive; 1.2f is OpenTPW's fallback without the lights) × (points 0x100C24C0 + NewParkBonus +0x2DC)) / PointsPerVisitor (+0x2E0, 1 when below 2), raised to MinPeople (+0x2D0), then cut so the counted guests (0x100C1FB4) plus the group stay within 1500 (500 when the GameType word is 1)
	public int ArrivalGroupSize()
	{
		if ( ArrivalsHalted?.Invoke() == true )
			return 0;
		var perVisitor = Settings.ArrivalPointsPerVisitor < 2 ? 1 : Settings.ArrivalPointsPerVisitor;
		var factor = TrafficLightsPlaced?.Invoke() == true ? 0.8f : 1.2f;
		var count = (int)(factor * (float)((ParkPoints?.Invoke() ?? 0) + Settings.ArrivalNewParkBonus)) / perVisitor;
		count = Math.Max( count, Settings.ArrivalMinPeople );
		var limit = OnlineVisit ? OnlineArrivalParkLimit : ArrivalParkLimit;
		if ( guests.Count + count > limit )
			// [APPROX:ARRIVAL-004] the cut group stops at 0; the original stores 1500 − guests as is (negative above 1500, compared unsigned; oracle F9). Executing 0xcb2ac with a negative group (oracle F11) shows the same behaviour as 0: at the next stop (phase 2) it creates no guest, restarts the timer and clears the active byte (0xcb3bc, 0xcb3f4), so nothing holds. What differs is the stored group word (negative) and the missing 'group == 0' arrival-vehicle step (0xcb488, slot 74), whose effect is not traced
			count = Math.Max( 0, limit - guests.Count );
		return count;
	}

	public bool IsInPark( Guest guest ) => InPark( guest );

	private bool InPark( Guest guest ) => guest.State is not (GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth
		or GuestState.AtTicketBooth or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.Gone);

	private void UpdateGuest( Guest guest, float dt )
	{
		if ( turnStarted )
			UpdateSpeed( guest );
		if ( InPark( guest ) )
			UpdateNeeds( guest, dt );
		// The needs block runs only on the guest's phase (0xeed24..0xeed40: park turn & 3 == thing id & 3), and its toilet
		// term compares the truncated toilet's low byte unsigned (0xef0bc..0xef0e8).
		// [APPROX:WALK-010] the needs phase compares the park turn with OpenTPW's guest id, not the original's 16-bit thing id (0xfa9a4), which numbers every thing — evidence needed: the thing id allocator and its order
		if ( turnStarted && (ParkTurn & 3) == (guest.Id & 3) )
			guest.ToiletBonus = ((int)guest.Toilet & 0xff) > GuestSpeed.ToiletBonusLevel ? GuestSpeed.ToiletBonus : 0;
		UpdateState( guest, dt );
		if ( !guest.HasWaypoint && guest.State is not GuestState.Boarding )
			guest.IsMoving = false;
		UpdatePresentation( guest );
	}

	/// <summary>
	/// The per-update speed (<c>0xe6b28</c>, called at <c>0xeed0c</c> before the needs and the state handler): smooth s,
	/// set the caps (<c>0xffe38</c>) and decay the boost. The toilet term +194 is rewritten after it, in the needs block.
	/// </summary>
	private static void UpdateSpeed( Guest guest )
	{
		// [APPROX:WALK-001] nothing raises the boost +196: the event-dependent increments of 0xeb0f4 are not traced, so it stays 0 and only its decay runs — evidence needed: the callers of 0xeb0f4 and their amounts
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
			case GuestState.DoingNothing:
				UpdateWalking( guest, dt );
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
				{
					guest.State = GuestState.WalkingAround;
					guest.DecisionTimer = Settings.DecisionIntervalSeconds;
				}
				break;
		}
	}

	private void UpdateNeeds( Guest guest, float dt )
	{
		var riding = guest.State == GuestState.Using;
		guest.Hunger = Math.Min( 150, guest.Hunger + Settings.HungerPerSecond * dt );
		guest.Thirst = Math.Min( 150, guest.Thirst + Settings.ThirstPerSecond * dt );
		guest.Toilet = Math.Min( 150, guest.Toilet + Settings.ToiletPerSecond * dt );
		guest.Nausea = Math.Max( 0, guest.Nausea - Settings.NauseaDecayPerSecond * dt );
		if ( riding || guest.State is GuestState.Queueing or GuestState.WaitingToBoard or GuestState.DoingNothing )
			guest.Energy = Math.Min( 100, guest.Energy + Settings.EnergyRecoveryPerSecond * dt );
		else
			guest.Energy = Math.Max( 0, guest.Energy - Settings.EnergyDrainPerSecond * dt );
		if ( !riding )
		{
			guest.SecondsSinceRide += dt;
			if ( guest.State != GuestState.LeavingPark )
				guest.ExitLevel -= dt;
		}
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
	// [APPROX:WALK-005] a cell is walkable when it is on the current walk (its route cells and the walk's extra open cells), so path cells beside the route are walls; the original tests the cell's edge flags against the guest's +180 mask (0xd768c) and commits the step through a transition call (0xfee3c..0xfee58) — evidence needed: the edge flags 0xd768c reads, the +180 mask per guest state and the commit callback
	private bool IsOpenForStep( int x, int y ) => stepping!.IsOnWalk( x, y );

	/// <summary>
	/// separation's neighbours for the stepping guest (<c>0x10087c</c>): the guests standing in the 3 × 3 cells around its
	/// cell, rows from the top and each row from the left, cells outside the map skipped (<c>0xd70d8</c>).
	/// </summary>
	// [APPROX:WALK-003] separation's neighbours are the guests outside rides and shops at their 16.16 navigation position (the thing's own 1/256-cell position +4/+6 is not traced); within a cell they come in OpenTPW's list order, rebuilt every park turn with movers appended, not the original's per-cell thing list (+764, +10); the original's query also keeps thing types 4–8 and 18, which never push in OpenTPW — evidence needed: the writers of thing +4/+6 and of the cell lists, and what thing types 4–8 and 18 are
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
					if ( other.State is not (GuestState.Using or GuestState.Gone) )
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
				guest.State = GuestState.WalkingAround;
				guest.DecisionTimer = 0;
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

	private void PayAdmission( Guest guest )
	{
		int fee;
		if ( Payments != null ? !Payments.TryPayAdmission( guest.Money, out fee ) : guest.Money < (fee = AdmissionFee) )
		{
			// Closed park or too expensive: the guest turns back at the booth.
			guest.Thought = GuestThought.Angry;
			StartLeavingLane( guest, guest.Lane!, guest.LaneIndex );
			return;
		}
		guest.Money -= fee;
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

	private void UpdateWalking( Guest guest, float dt )
	{
		guest.DecisionTimer -= dt;
		if ( guest.HasWaypoint && !Move( guest ) )
			return;
		if ( guest.DecisionTimer <= 0 )
		{
			guest.DecisionTimer = Settings.DecisionIntervalSeconds;
			UpdateMood( guest );
			if ( ShouldLeave( guest ) )
			{
				guest.State = GuestState.LeavingPark;
				return;
			}
			var choice = ChooseAttraction( guest );
			if ( choice != null )
			{
				guest.AttractionId = choice.AttractionId;
				guest.State = GuestState.GoingToRide;
				return;
			}
		}
		Wander( guest );
	}

	private void Wander( Guest guest )
	{
		var count = Grid.NeighbourCount( guest.CellX, guest.CellY );
		if ( count == 0 )
		{
			guest.State = GuestState.DoingNothing;
			return;
		}
		guest.State = GuestState.WalkingAround;
		Span<int> options = stackalloc int[4];
		var optionCount = 0;
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( !Grid.AreConnected( guest.CellX, guest.CellY, direction ) )
				continue;
			var nx = guest.CellX + GuestPathGrid.Directions[direction].DX;
			var ny = guest.CellY + GuestPathGrid.Directions[direction].DY;
			if ( count > 1 && nx == guest.PreviousCellX && ny == guest.PreviousCellY )
				continue;
			options[optionCount++] = direction;
		}
		var chosen = options[random.Next( optionCount )];
		SetCellWaypoint( guest, guest.CellX + GuestPathGrid.Directions[chosen].DX, guest.CellY + GuestPathGrid.Directions[chosen].DY );
	}

	private void UpdateMood( Guest guest )
	{
		var small = Settings.SmallHappinessChange;
		if ( guest.Hunger >= 100 || guest.Thirst >= 100 )
			guest.Happiness -= small;
		if ( guest.Toilet >= Settings.ToiletDesperate )
			guest.Happiness -= small;
		var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
		var bored = guest.SecondsSinceRide > type.BoredomThreshold;
		if ( bored )
			guest.Happiness -= small;
		guest.Happiness = Math.Clamp( guest.Happiness, 0, 100 );

		var hungry = guest.Hunger >= Settings.NeedThoughtLevel;
		var thirsty = guest.Thirst >= Settings.NeedThoughtLevel;
		guest.Thought = guest.Happiness <= 10 ? GuestThought.Angry
			: guest.Toilet >= Settings.ToiletDesperate * Settings.NeedThoughtLevel / 100 ? GuestThought.NeedToilet
			: hungry && thirsty ? GuestThought.HungryAndThirsty
			: hungry ? GuestThought.Hungry
			: thirsty ? GuestThought.Thirsty
			: bored ? GuestThought.Bored
			: guest.Happiness >= 90 ? GuestThought.SuperHappy
			: guest.Happiness >= 70 ? GuestThought.Happy
			: guest.Happiness >= 30 ? GuestThought.Okay
			: GuestThought.Unhappy;
	}

	private bool ShouldLeave( Guest guest ) => guest.ExitLevel <= 0 || guest.Happiness <= 10;

	/// <summary>
	/// Attraction score with the .sam PeepInfo.DecisionVar* weights. How the original combines them is unknown;
	/// the terms and scales below are an approximation (docs/GUESTS.md).
	/// </summary>
	public int ScoreAttraction( Guest guest, IRideVisitorBridge attraction, int distance )
	{
		var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
		var score = attraction.AttractionValue
			- Settings.DecisionDistanceWeight * distance
			- Settings.DecisionQueueWeight * attraction.QueueLength;
		if ( attraction.Kind == RideVisitorKind.Ride || attraction.Kind == RideVisitorKind.Sideshow )
			score += Settings.DecisionExcitementWeight * (25 - Math.Abs( type.PreferredExcitement - attraction.ExcitementLevel ));
		if ( attraction.Satisfies.HasFlag( GuestNeeds.Hunger ) )
			score += (int)(Settings.DecisionHungerWeight * guest.Hunger / 4);
		if ( attraction.Satisfies.HasFlag( GuestNeeds.Thirst ) )
			score += (int)(Settings.DecisionThirstWeight * guest.Thirst / 4);
		if ( attraction.Satisfies.HasFlag( GuestNeeds.Toilet ) )
			score += (int)(Settings.DecisionToiletWeight * guest.Toilet / 4);
		if ( attraction.Kind == RideVisitorKind.Ride )
			score -= (int)(Settings.DecisionIllnessWeight * guest.Nausea / 4);
		if ( attraction.AttractionId == guest.LastAttractionId && guest.SecondsSinceRide < type.BoredomThreshold )
			score -= attraction.AttractionValue;
		if ( attraction.Price > guest.Money )
			score = int.MinValue;
		return score;
	}

	private IRideVisitorBridge? ChooseAttraction( Guest guest )
	{
		IRideVisitorBridge? best = null;
		var bestScore = 0;
		foreach ( var attraction in attractions )
		{
			if ( !attraction.IsOpen || attraction.JoinCell is not { } join )
				continue;
			// [APPROX:QUEUE-017] guests do not choose a broken attraction (VAR_BROKEN ≠ 0); the original attraction choice is not traced for broken rides — evidence needed: the state-6 attraction choice and its test of the ride +408 state
			if ( attraction.IsBroken )
				continue;
			var distance = Grid.Distance( guest.CellX, guest.CellY, join.X, join.Y );
			if ( distance < 0 )
				continue;
			var score = ScoreAttraction( guest, attraction, distance );
			if ( score == int.MinValue )
				continue;
			score += random.Next( 10 );
			if ( score > bestScore )
			{
				bestScore = score;
				best = attraction;
			}
		}
		return best;
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
		if ( attraction == null || !attraction.IsOpen )
		{
			guest.AttractionId = 0;
			guest.State = GuestState.WalkingAround;
			return;
		}
		if ( attraction.JoinCell is not { } join )
		{
			guest.Thought = GuestThought.Confused;
			guest.AttractionId = 0;
			guest.State = GuestState.WalkingAround;
			return;
		}
		var (tx, ty) = join;
		if ( guest.CellX == tx && guest.CellY == ty )
		{
			var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
			var result = attraction.JoinQueue( guest.Id, type.PreferredExcitement - attraction.ExcitementLevel );
			if ( result == QueueJoinResult.Joined )
			{
				BeginQueue( guest, attraction );
				return;
			}
			guest.Thought = result switch
			{
				QueueJoinResult.TooExciting => GuestThought.Scared,
				QueueJoinResult.NotExcitingEnough => GuestThought.Bored,
				_ => GuestThought.QueueTooLong
			};
			if ( result is QueueJoinResult.NoRoom or QueueJoinResult.QueueTooLong )
				guest.Happiness = Math.Max( 0, guest.Happiness - Settings.SmallHappinessChange );
			guest.LastAttractionId = attraction.AttractionId;
			guest.AttractionId = 0;
			guest.State = GuestState.WalkingAround;
			guest.DecisionTimer = Settings.DecisionIntervalSeconds;
			return;
		}
		if ( Grid.TryStep( guest.CellX, guest.CellY, tx, ty, out var nx, out var ny ) )
			SetCellWaypoint( guest, nx, ny );
		else
		{
			guest.Thought = GuestThought.Confused;
			guest.AttractionId = 0;
			guest.State = GuestState.WalkingAround;
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
	/// "Queue edited" (<c>0xdd57c</c>): every queued guest except the called one re-evaluates. Guests beyond the
	/// new room leave ("queue was shortened"), guests whose cell left the queue leave ("queue edited underneath
	/// me"), the others walk to their position again.
	/// </summary>
	// [APPROX:QUEUE-005] the re-evaluation exits of 0xee8f8 are reduced to "position beyond 4 × cells" and "standing cell no longer in the queue" — evidence needed: 0x100EE8F8 in detail
	private void ReevaluateQueue( IRideVisitorBridge attraction )
	{
		var cells = attraction.QueueCells;
		var room = RideVisitorBridge.PositionsPerCell * attraction.QueueSizeInCells;
		foreach ( var guest in guests )
		{
			if ( guest.AttractionId != attraction.AttractionId || guest.State is not (GuestState.Queueing or GuestState.MovingUpQueue) || guest.Id == attraction.CalledGuest )
				continue;
			var position = attraction.GetQueuePosition( guest.Id );
			var index = IndexOfCell( cells, guest.CellX, guest.CellY );
			var onJoinCell = attraction.JoinCell is { } join && join == (guest.CellX, guest.CellY);
			if ( position < 0 || position >= room )
				LeaveQueue( guest, attraction, GuestThought.QueueTooLong );
			else if ( index < 0 && !onJoinCell )
				LeaveQueue( guest, attraction, GuestThought.Confused );
			else
			{
				guest.QueueCellIndex = index < 0 ? cells.Count : index;
				MoveToQueuePosition( guest, attraction );
			}
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
			LeaveQueue( guest, attraction, GuestThought.Confused );
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
		// [APPROX:WALK-006] a queue walk may also enter the queue cell ahead of its destination and the one behind its start (the entrance and the join cell at the ends), and no other cell off its route; this stands in for the original's edge flags along the queue — evidence needed: the queue cells' edge flags and the +180 mask in states 12 and 13
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
	// [APPROX:QUEUE-006] depth runs from the cell's edge toward its predecessor and the lateral byte across it, and the fourth position (depth 191 > 128) stays in its cell; the original sub-cell axis per link and the 0xdde74 branch are not traced — evidence needed: 0x100DDE74 and the axis selection after it
	public (int CellIndex, float X, float Y) QueuePositionPoint( IRideVisitorBridge attraction, int position, byte lateral )
	{
		var (index, x, y) = QueueSlotPoint( attraction, position, lateral );
		return (index, x / (float)Fixed16.One, y / (float)Fixed16.One);
	}

	/// <summary><see cref="QueuePositionPoint"/> in 16.16 cells.</summary>
	public static (int CellIndex, int X, int Y) QueueSlotPoint( IRideVisitorBridge attraction, int position, byte lateral )
	{
		var (index, depth) = QueueSlot( position, attraction.QueueSizeInCells );
		var cell = attraction.QueueCells[Math.Min( index, attraction.QueueCells.Count - 1 )];
		var (dx, dy) = GuestPathGrid.Directions[attraction.DirectionTowardsFront( index )];
		// The byte along the axis towards the front counts from the front edge; the lateral byte runs along (−dy, dx).
		int bx, by;
		if ( dx != 0 )
			(bx, by) = (dx > 0 ? 255 - depth : depth, dx > 0 ? lateral : 255 - lateral);
		else
			(bx, by) = (dy < 0 ? lateral : 255 - lateral, dy > 0 ? 255 - depth : depth);
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
		else if ( attraction.PresentForBoarding( guest.Id ) )
			guest.State = GuestState.WaitingToBoard;
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
		// 3. The ride failed.
		// [APPROX:QUEUE-008] the ride-failure exit (0xdfe34 set → thought 14, leave) is read as VAR_BROKEN ≠ 0 — evidence needed: the predicate 0x100DFE34
		if ( attraction.IsBroken )
		{
			LeaveQueue( guest, attraction, GuestThought.FeelingSick );
			return;
		}
		// 4. Position lookup.
		var position = attraction.GetQueuePosition( guest.Id );
		if ( position < 0 )
		{
			LeaveQueue( guest, attraction, GuestThought.Confused );
			return;
		}
		// 5. Moving up: wait out the delay while the gap is at most 2, else walk to the new position.
		if ( position != guest.QueuePosition )
		{
			var gap = guest.QueuePosition - position;
			if ( guest.QueueMoveDelay != 0 && gap is >= 0 and <= MoveUpWaitGap )
				guest.QueueMoveDelay--;
			else
				MoveToQueuePosition( guest, attraction );
			return;
		}
		// 6. Beyond the limit.
		if ( guest.QueuePosition > attraction.QueueLimit )
		{
			LeaveQueue( guest, attraction, GuestThought.QueueTooLong );
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
				LeaveQueue( guest, attraction, GuestThought.Unhappy );
				return;
			}
			if ( (int)guest.Toilet > (int)ToiletLeaveLevel && !attraction.ProvidesRelief )
			{
				LeaveQueue( guest, attraction, GuestThought.NeedToilet );
				return;
			}
		}
		else if ( turn > guest.QueueStandingSinceTurn + BoredomTurns )
		{
			LeaveQueue( guest, attraction, GuestThought.Bored );
			return;
		}
		// 8. A 1-in-10 facing change of ±400/2048 of a turn.
		// [APPROX:QUEUE-009] the facing change's sign comes from the same draw (0 → +400, 1 → −400 of 2048) — evidence needed: the sign selection after the 1-in-10 facing test at the end of 0x100ED244
		var draw = random.Next( 20 );
		if ( draw < 2 )
		{
			var angle = (draw == 0 ? 400 : -400) * MathF.Tau / 2048;
			var (sin, cos) = MathF.SinCos( angle );
			(guest.HeadingX, guest.HeadingY) = (guest.HeadingX * cos - guest.HeadingY * sin, guest.HeadingX * sin + guest.HeadingY * cos);
		}
	}

	/// <summary>Idle interlude (<c>0xe8b74</c>): +520 = turn, state 8 until turn &gt; +520 + 10, then state 11 again.</summary>
	private void StartInterlude( Guest guest )
	{
		guest.InterludeTurn = ParkTurn;
		guest.InQueueInterlude = true;
	}

	/// <summary>The guest leaves the queue without riding and steps back onto the path at the join cell.</summary>
	// [APPROX:QUEUE-010] a guest leaving a queue is placed on the queue's join path cell at once; the original walk out of the queue is not traced — evidence needed: the state-6 transition after a queue exit
	private void LeaveQueue( Guest guest, IRideVisitorBridge attraction, GuestThought thought )
	{
		attraction.LeaveQueue( guest.Id );
		ClearQueueState( guest );
		guest.Thought = thought;
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
		guest.State = GuestState.WalkingAround;
		guest.DecisionTimer = Settings.DecisionIntervalSeconds;
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
		guest.AttractionId = ride.AttractionId;
		guest.HasWaypoint = false;
		var price = 0;
		if ( Payments != null )
			Payments.TryPayVisit( ride, guest.Money, out price );
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
		ApplyVisit( guest, ride );
	}

	void IRideVisitorHost.OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		ClearQueueState( guest );
		guest.Thought = GuestThought.Dissatisfied;
		ReturnToPath( guest, JoinCellOf( ride ) );
	}

	/// <summary>
	/// Ride outcome from the .sam PeepInfo.PerfectRide/GoodRide/OKRide bands (±5/±15/±40 of the preferred
	/// excitement) and RideVomitDivisor/VomitCapacity; shops reset the needs they satisfy.
	/// </summary>
	private void ApplyVisit( Guest guest, IRideVisitorBridge ride )
	{
		if ( ride.Satisfies.HasFlag( GuestNeeds.Hunger ) ) guest.Hunger = 0;
		if ( ride.Satisfies.HasFlag( GuestNeeds.Thirst ) ) guest.Thirst = 0;
		if ( ride.Satisfies.HasFlag( GuestNeeds.Toilet ) ) guest.Toilet = 0;
		if ( ride.Kind is RideVisitorKind.Ride or RideVisitorKind.Sideshow )
		{
			var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
			var difference = Math.Abs( type.PreferredExcitement - ride.ExcitementLevel );
			var change = difference <= 5 ? Settings.PerfectRide
				: difference <= 15 ? Settings.GoodRide
				: difference <= 40 ? Settings.OkRide
				: -Settings.SmallHappinessChange;
			guest.Happiness = Math.Clamp( guest.Happiness + change, 0, 100 );
			guest.ExitLevel += Math.Max( 0, change );
			guest.Thought = change > 0 ? GuestThought.Pleased : GuestThought.Dissatisfied;
			guest.Nausea += ride.ExcitementLevel / (float)Settings.RideVomitDivisor;
			if ( guest.Nausea >= Settings.VomitCapacity )
			{
				guest.Nausea = 0;
				guest.Thought = GuestThought.FeelingSick;
				guest.Happiness = Math.Max( 0, guest.Happiness - Settings.MediumHappinessChange );
			}
		}
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
				case GuestState.WalkingAround or GuestState.GoingToRide or GuestState.DoingNothing or GuestState.ExitingRide: walking++; break;
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
			SecondsSinceRide = guest.SecondsSinceRide,
			RidesTaken = guest.RidesTaken,
			Thought = guest.Thought,
			AttractionId = guest.AttractionId,
			LastAttractionId = guest.LastAttractionId,
			QueuePosition = guest.QueuePosition,
			QueueJoinTurn = guest.QueueJoinTurn,
			LastQueueWaitTurns = guest.LastQueueWaitTurns,
			QueueMoveDelay = guest.QueueMoveDelay,
			QueueCalled = guest.QueueCalled,
			QueueStandingSinceTurn = guest.QueueStandingSinceTurn,
			InterludeTurn = guest.InterludeTurn,
			InQueueInterlude = guest.InQueueInterlude,
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
			StateTimer = guest.StateTimer,
			DecisionTimer = guest.DecisionTimer
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

		if ( snapshot.SchemaVersion != GuestSimulationSnapshot.CurrentSchemaVersion )
			Error( $"Guest snapshot schema version {snapshot.SchemaVersion} is not supported; this build reads {GuestSimulationSnapshot.CurrentSchemaVersion}." );
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
			if ( (uint)guest.Type >= (uint)typeCount )
				Error( $"Guest {guest.Id} type {guest.Type} is not one of the park's {typeCount} guest types." );
			Bounded( guest.Id, guest.Hunger, nameof( guest.Hunger ), 0, 150 );
			Bounded( guest.Id, guest.Thirst, nameof( guest.Thirst ), 0, 150 );
			Bounded( guest.Id, guest.Toilet, nameof( guest.Toilet ), 0, 150 );
			Bounded( guest.Id, guest.Energy, nameof( guest.Energy ), 0, 100 );
			Bounded( guest.Id, guest.Happiness, nameof( guest.Happiness ), 0, 100 );
			Bounded( guest.Id, guest.Nausea, nameof( guest.Nausea ), 0, 1000 );
			Bounded( guest.Id, guest.QueueJoinHappiness, nameof( guest.QueueJoinHappiness ), 0, 100 );
			Bounded( guest.Id, guest.SecondsSinceRide, nameof( guest.SecondsSinceRide ), 0, 1_000_000 );
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
			Finite( guest.Id, guest.ExitLevel, nameof( guest.ExitLevel ) );
			Finite( guest.Id, guest.OffsetX, nameof( guest.OffsetX ) );
			Finite( guest.Id, guest.OffsetY, nameof( guest.OffsetY ) );
			Finite( guest.Id, guest.QueueTargetX, nameof( guest.QueueTargetX ) );
			Finite( guest.Id, guest.QueueTargetY, nameof( guest.QueueTargetY ) );
			Finite( guest.Id, guest.StateTimer, nameof( guest.StateTimer ) );
			Finite( guest.Id, guest.DecisionTimer, nameof( guest.DecisionTimer ) );
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
			ApplyGuestSnapshot( guest, guestSnapshot );
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
	private static void ApplyGuestSnapshot( Guest guest, GuestSnapshot snapshot )
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
		guest.Hunger = snapshot.Hunger;
		guest.Thirst = snapshot.Thirst;
		guest.Toilet = snapshot.Toilet;
		guest.Energy = snapshot.Energy;
		guest.Nausea = snapshot.Nausea;
		guest.Happiness = snapshot.Happiness;
		guest.Money = snapshot.Money;
		guest.ExitLevel = snapshot.ExitLevel;
		guest.SecondsSinceRide = snapshot.SecondsSinceRide;
		guest.RidesTaken = snapshot.RidesTaken;
		guest.Thought = snapshot.Thought;
		guest.AttractionId = snapshot.AttractionId;
		guest.LastAttractionId = snapshot.LastAttractionId;
		guest.QueuePosition = snapshot.QueuePosition;
		guest.QueueJoinTurn = snapshot.QueueJoinTurn;
		guest.LastQueueWaitTurns = snapshot.LastQueueWaitTurns;
		guest.QueueMoveDelay = snapshot.QueueMoveDelay;
		guest.QueueCalled = snapshot.QueueCalled;
		guest.QueueStandingSinceTurn = snapshot.QueueStandingSinceTurn;
		guest.InterludeTurn = snapshot.InterludeTurn;
		guest.InQueueInterlude = snapshot.InQueueInterlude;
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
		guest.DecisionTimer = snapshot.DecisionTimer;
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

	/// <summary>Thing-table part of <see cref="WorldStateHash"/>: every field the next tick reads, in id order.</summary>
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
			hash.Add( guest.DecisionTimer );
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

	/// <summary>FNV-1a over every guest's simulation state, the RNG and the counters.</summary>
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
			AddFloat( guest.Happiness );
			AddFloat( guest.ExitLevel );
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
