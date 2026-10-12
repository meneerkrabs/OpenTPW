namespace OpenTPW;

/// <summary>
/// Connects real guests to an original RSE script and owns the attraction's queue (docs/GUESTS.md,
/// docs/reverse/QUEUE-plan.md). The queue is the original's per-ride state: front cell, cached back cell,
/// size in cells and a doubly linked list of guest ids with one called ("pending") visitor. Between
/// slices, outside CRIT_LOCK, the host calls the front guest forward when the original admission gates
/// hold; the guest walks to the stand point and writes its id to VAR_LETMEON when that is 0; it stays in
/// the list until the script consumes it. When the script puts a guest id in VAR_LETMEOFF the host
/// releases that guest at the exit and writes 0 back. The visitor opcodes (HUSH, WALKON, HOP, WALKOFF, WALKGET, LIMBO,
/// UNLIMBO, FORCEUNLIMBO, INLIMBO, LIMBOSPACE, BOUNCE, UNBOUNCE, FORCEUNBOUNCE, BOUNCING, ADDHEAD,
/// DELHEAD) operate on the guests the script holds. Semantics are inferred from corpus control flow,
/// not traced from the original executable; walk animations along ride nodes are not simulated (WALKON
/// and WALKOFF complete immediately).
/// </summary>
public sealed class RideVisitorBridge : IRideVisitorBridge
{
	/// <summary>BOUNCE with a zero duration (VAR_DURATION has no source yet) keeps a guest this long. Approximation.</summary>
	public const double DefaultBounceSeconds = 10;
	/// <summary>Guests per queue cell (positions 0..3 along the cell).</summary>
	// [BIN:STP-PPC:0x100DDD2C queue coordinates] four positions per queue cell; the walk subtracts 4 per followed link
	public const int PositionsPerCell = 4;
	/// <summary>Queue limit of objects without <c>Info.HasQueue</c> (shops, toilets and sideshows: the virtual queue).</summary>
	// [BIN:STP-PPC:0x100DCB74 queue limit] ride +46 & 0x8 (Info.HasQueue, set by the ride build 0x100DAB6C) clear gives limit 100; set gives trunc(max(QWTC × (SPEED/InitSpeed × CAP) / DUR, 4)) as a u32
	public const uint NoQueueLimit = 100;
	/// <summary>Excitement difference at which a guest refuses to join (<see cref="QueueJoinRules.Gate"/>).</summary>
	// [BIN:STP-PPC:0x100ECCB0 join excitement gate] |difference| ≥ 45 leaves with "ride is not exciting enough!" / "ride is too exciting!"
	public const int ExcitementGate = QueueJoinRules.Gate;
	/// <summary>Iteration guard of the back-of-queue walk ("GetBackOfQueue() crashed!").</summary>
	public const int QueueWalkGuard = 1000;

	private readonly Dictionary<int, (int Next, int Previous)> links = new(); // guest +552/+554
	private int head; // ride +56
	private int tail;
	private readonly List<int> onRide = new();          // boarded, in boarding order
	private readonly Queue<int> walkingOff = new();     // WALKOFF done, waiting for WALKGET
	private readonly HashSet<int> hopped = new();
	private readonly List<(int Guest, double Until)> limbo = new();
	private readonly List<(int Guest, double Until, double Start)> bouncing = new(); // BOUNCE slot table in index order: visitor (0 = free), deadline, start; trailing free slots are trimmed
	private readonly List<(int X, int Y)> queueCells = new();
	private RideVM? vm;
	private int called;       // ride +104: the pending visitor
	private bool letMeOnWritten;
	private long lastAdmissionTurn = long.MinValue;
	private long calledTurn = -1;
	private Func<bool> isOpen = () => true;
	private readonly int excitementLevel;

	public RideVisitorBridge( int attractionId, string name, RideVisitorKind kind, int capacity, int excitementLevel, int attractionValue,
		GuestNeeds satisfies = GuestNeeds.None, int price = 0 )
	{
		if ( attractionId <= 0 )
			throw new ArgumentOutOfRangeException( nameof( attractionId ) );
		AttractionId = attractionId;
		Name = name;
		Kind = kind;
		Capacity = Math.Max( 1, capacity );
		this.excitementLevel = excitementLevel;
		AttractionValue = attractionValue;
		Satisfies = satisfies;
		Price = price;
	}

	public int AttractionId { get; }
	public string Name { get; }
	public RideVisitorKind Kind { get; }
	public int Capacity { get; }
	/// <summary>The excitement shown to the HUD and used by the attraction score: the coaster's guest value when there is a source, else the .sam level.</summary>
	public int ExcitementLevel => ExcitementSource?.Invoke() ?? excitementLevel;
	/// <summary>The type's <c>UsageInfo.ExcitementLevel</c> word (type <c>+316</c>); its low byte decides whether guests are tested at all.</summary>
	public int StaticExcitementLevel => excitementLevel;
	/// <summary>Replaces the .sam excitement with a live value (a coaster's rating, <c>0xdff70</c>), or null.</summary>
	public Func<int>? ExcitementSource { get; set; }
	/// <summary>The sideshow's goods, price and win chance, read by <see cref="LiveExcitement"/>; the level mirrors them from the economy each tick.</summary>
	public SideshowTerms Sideshow { get; set; }
	/// <summary>The wear gauge (ride <c>+64</c>, the state of repair 0–100) the relief predicate reads; the level mirrors it from the economy.</summary>
	public double WearGauge { get; set; } = 100;
	/// <summary>The ride's head table for ADDHEAD/DELHEAD, null when its model has no head records (see <see cref="RideHeadSpace"/>).</summary>
	// [APPROX:HEADS-005] root and resolved child/sound scripts share the attraction head table; the Mac opcode reads the calling script +48/+76, but allocation/binding for child scripts is not traced
	public RideHeadSpace? Heads { get; set; }
	public int AttractionValue { get; }
	public GuestNeeds Satisfies { get; }
	public int Price { get; }
	/// <summary>The type's <c>UsageInfo.SpecialIngredient</c> (<c>+344</c>); 4 (Sugar) makes a visit raise the guest's speed boost.</summary>
	public int SpecialIngredient { get; set; }
	/// <summary>The ride's amount of the special ingredient (<c>+404</c>), 50 when the ride is built.</summary>
	// [APPROX:WALK-001] the amount of the special ingredient stays at the ride build's 50 (a Sugar visit adds 3): the original's shop screen can change it (ride +404, saved as mAmountOfSpecialIngredient) and OpenTPW has neither that control nor the saved field — evidence needed: the screen that sets +404 (handlers 0x1685b4 and 0x168640), its range and the save field
	// Only tests change it: nothing in play sets +404 and it is neither saved nor hashed, so a public setter would make state the save loses.
	public int IngredientAmount { get; internal set; } = GuestSpeed.DefaultIngredientAmount;
	/// <summary>Toilets (<c>UsageInfo.ProvidesRelief</c>): queueing guests do not leave for the toilet.</summary>
	public bool ProvidesRelief => Satisfies.HasFlag( GuestNeeds.Toilet );
	/// <summary>Walkable path cell the queue falls back to when the entrance's outside cell is neither a queue nor a path cell.</summary>
	public (int X, int Y) EntranceCell { get; set; }
	public (int X, int Y) ExitCell { get; set; }
	public (float X, float Y) EntryCellStandPos { get; set; } = (0.5f, 0.5f);
	public int Rotation { get; set; }
	public bool HasCells { get; set; }
	/// <summary>Queue rules from the object's .sam data (<see cref="QueueParameters"/>); defaults describe a queue-less object.</summary>
	public QueueParameters Parameters { get; set; } = QueueParameters.Default;
	public bool HasQueue => Parameters.HasQueue;
	public IRideVisitorHost? Host { get; set; }
	public bool IsOpen => vm != null && vm.State is (RideVMState.Running or RideVMState.Waiting) && isOpen();
	/// <summary>
	/// The ride script reports a breakdown (<c>VAR_BROKEN</c>, which the original's ride update turns into ride state 1, or 4 when
	/// the life gauge is 0): the ride dismisses its queue head first, one guest per park turn, and takes nobody (ORACLE.md F15).
	/// </summary>
	public bool IsBroken => vm != null && Get( RideVariables.VAR_BROKEN ) != 0;
	/// <summary>The original's ride-failure predicate <c>0xdfe34</c> (ORACLE.md F15): a relief object whose wear gauge, truncated to a byte, is below 25.</summary>
	public bool ReliefWornOut => QueueJoinRules.ReliefFailed( ProvidesRelief, (float)WearGauge );
	public bool IsRunning => vm != null && Get( RideVariables.VAR_RUNNING ) != 0;
	public int RidersOnBoard => vm != null ? Get( RideVariables.VAR_ONRIDE ) : 0;
	/// <summary>The operating speed: InitSpeed of the ride record (see <see cref="QueueParameters"/>).</summary>
	public int Speed => Parameters.Speed;
	/// <summary>The guest called forward (ride <c>+104</c>), 0 when none.</summary>
	public int CalledGuest => called;
	/// <summary>Compatibility name of <see cref="CalledGuest"/>.</summary>
	public int OfferedGuest => called;
	/// <summary>Guests in the queue, front first (the called guest included until the script takes it).</summary>
	public IReadOnlyList<int> Queue
	{
		get
		{
			var list = new List<int>( links.Count );
			for ( var guest = head; guest != 0; guest = links[guest].Next )
				list.Add( guest );
			return list;
		}
	}
	public int QueueLength => links.Count;
	/// <summary>Guests the script holds (boarded and not yet released).</summary>
	public IReadOnlyList<int> Riders => onRide;
	public int BoardedTotal { get; private set; }
	public int ReleasedTotal { get; private set; }

	/// <summary>Queue and rider state for <see cref="WorldStateHash"/>.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		void AddList( IReadOnlyCollection<int> values )
		{
			hash.Add( values.Count );
			foreach ( var value in values )
				hash.Add( value );
		}
		void AddTimed( List<(int Guest, double Until)> values )
		{
			hash.Add( values.Count );
			foreach ( var (guest, until) in values )
			{
				hash.Add( guest );
				hash.Add( until );
			}
		}
		hash.Add( MaximumQueueLength );
		hash.Add( called );
		hash.Add( BoardedTotal );
		hash.Add( ReleasedTotal );
		AddList( Queue );
		AddList( onRide );
		AddList( walkingOff );
		AddList( hopped.Order().ToArray() );
		AddTimed( limbo );
		hash.Add( bouncing.Count );
		foreach ( var (guest, until, start) in bouncing )
		{
			hash.Add( guest );
			hash.Add( until );
			hash.Add( start );
		}
		hash.Add( letMeOnWritten );
		hash.Add( lastAdmissionTurn );
		hash.Add( QueueFrontCell is { } front ? front.X : -1 );
		hash.Add( QueueFrontCell is { } frontCell ? frontCell.Y : -1 );
		hash.Add( QueueEntranceDirection );
		hash.Add( queueCells.Count );
		foreach ( var (x, y) in queueCells )
		{
			hash.Add( x );
			hash.Add( y );
		}
		hash.Add( JoinCell is { } join ? join.X : -1 );
		hash.Add( JoinCell is { } joinCell ? joinCell.Y : -1 );
		hash.Add( QueueEditCount );
		hash.Add( calledTurn );
		hash.Add( HeadNotReadyStreak );
		hash.Add( MaximumHeadNotReadyStreak );
		hash.Add( CalledAgeTurns );
		hash.Add( MaximumCalledAgeTurns );
	}

	// ---- Snapshots (park save/load) -------------------------------------------------------------

	/// <summary>
	/// Captures the queue and rider bookkeeping as a typed deep copy: the guest-id linked list front first, the
	/// script-held guests with their timers, the computed queue geometry and the admission counters. The script, the
	/// open delegate and the placement's cells metadata (entrance/exit, stand position, queue parameters) are owned by
	/// the object/script slices and are re-attached around a restore, not captured.
	/// </summary>
	public RideVisitorBridgeSnapshot CaptureSnapshot() => new()
	{
		AttractionId = AttractionId,
		Queue = Queue.ToArray(),
		OnRide = onRide.ToArray(),
		WalkingOff = walkingOff.ToArray(),
		Hopped = hopped.Order().ToArray(),
		Limbo = limbo.ToArray(),
		Bounce = bouncing.ToArray(),
		CalledGuest = called,
		LetMeOnWritten = letMeOnWritten,
		LastAdmissionTurn = lastAdmissionTurn,
		CalledTurn = calledTurn,
		QueueCells = queueCells.ToArray(),
		QueueEditCount = QueueEditCount,
		JoinCell = JoinCell,
		HeadNotReadyStreak = HeadNotReadyStreak,
		MaximumHeadNotReadyStreak = MaximumHeadNotReadyStreak,
		CalledAgeTurns = CalledAgeTurns,
		MaximumCalledAgeTurns = MaximumCalledAgeTurns,
		LastAdmissionCheck = LastAdmissionCheck,
		BoardedTotal = BoardedTotal,
		ReleasedTotal = ReleasedTotal
	};

	/// <summary>
	/// Bridge-local snapshot checks that need no park context: positive and unique guest ids, the called guest in the
	/// queue, rider subsets, finite timers and bounded counters. A list the file lost (a JSON null) is named as a
	/// problem instead of crashing. Whether the referenced guests exist — and each is queued or aboard at exactly one
	/// attraction — is validated by <see cref="GuestSimulation.ValidateSnapshot"/>.
	/// </summary>
	public static IReadOnlyList<string> ValidateSnapshot( RideVisitorBridgeSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		var errors = new List<string>();
		void Error( string message ) => errors.Add( message );
		// Name every list a corrupt file nulled out before anything dereferences them; a snapshot without its
		// tables cannot be checked structurally.
		var listsMissing = false;
		void Require<T>( T[]? list, string name )
		{
			if ( list == null )
			{
				Error( $"the {name} is missing." );
				listsMissing = true;
			}
		}
		Require( snapshot.Queue, "queue list" );
		Require( snapshot.OnRide, "rider list" );
		Require( snapshot.WalkingOff, "walking-off list" );
		Require( snapshot.Hopped, "hopped set" );
		Require( snapshot.Limbo, "limbo table" );
		Require( snapshot.Bounce, "bounce table" );
		Require( snapshot.QueueCells, "queue cell list" );
		if ( listsMissing )
			return errors;
		var riders = new HashSet<int>( snapshot.OnRide );
		if ( riders.Count != snapshot.OnRide.Length )
			Error( "the rider list repeats a guest." );
		foreach ( var guestId in snapshot.Queue )
			if ( guestId <= 0 )
				Error( $"the queue holds guest id {guestId}." );
		if ( snapshot.Queue.Distinct().Count() != snapshot.Queue.Length )
			Error( "the queue repeats a guest." );
		if ( snapshot.Queue.Length > PositionsPerCell * Math.Max( 1, snapshot.QueueCells.Length ) )
			Error( $"the queue holds {snapshot.Queue.Length} guests beyond the physical room of its {snapshot.QueueCells.Length} cells." );
		if ( snapshot.QueueCells.Length == 0 )
			Error( "the queue has no cells." );
		if ( snapshot.CalledGuest < 0 )
			Error( $"the called guest id {snapshot.CalledGuest} is negative." );
		if ( snapshot.CalledGuest != 0 && !snapshot.Queue.Contains( snapshot.CalledGuest ) )
			Error( $"the called guest {snapshot.CalledGuest} is not in the queue." );
		if ( snapshot.LetMeOnWritten && snapshot.CalledGuest == 0 )
			Error( "VAR_LETMEON is marked written without a called guest." );
		foreach ( var guestId in snapshot.OnRide )
			if ( guestId <= 0 )
				Error( $"the rider list holds guest id {guestId}." );
		foreach ( var guestId in snapshot.WalkingOff )
			if ( !riders.Contains( guestId ) )
				Error( $"walking-off guest {guestId} is not aboard." );
		if ( snapshot.WalkingOff.Distinct().Count() != snapshot.WalkingOff.Length )
			Error( "the walking-off list repeats a guest." );
		foreach ( var guestId in snapshot.Hopped )
			if ( !riders.Contains( guestId ) )
				Error( $"hopped guest {guestId} is not aboard." );
		if ( snapshot.Hopped.Distinct().Count() != snapshot.Hopped.Length )
			Error( "the hopped set repeats a guest." );
		var limboGuests = new HashSet<int>();
		foreach ( var (guestId, until) in snapshot.Limbo )
		{
			if ( !riders.Contains( guestId ) )
				Error( $"limbo guest {guestId} is not aboard." );
			if ( !limboGuests.Add( guestId ) )
				Error( $"limbo holds guest {guestId} twice." );
			if ( !double.IsFinite( until ) )
				Error( $"limbo guest {guestId} has a deadline that is not finite." );
		}
		var bouncingGuests = new HashSet<int>();
		for ( var index = 0; index < snapshot.Bounce.Length; index++ )
		{
			var (guestId, until, start) = snapshot.Bounce[index];
			if ( guestId < 0 )
				Error( $"bounce slot {index} holds guest id {guestId}." );
			if ( guestId != 0 && !bouncingGuests.Add( guestId ) )
				Error( $"the bounce table repeats guest {guestId}." );
			if ( guestId != 0 && !riders.Contains( guestId ) )
				Error( $"bouncing guest {guestId} is not aboard." );
			if ( !double.IsFinite( until ) || !double.IsFinite( start ) )
				Error( $"bounce slot {index} has a time that is not finite." );
		}
		if ( snapshot.Bounce.Length > 0 && snapshot.Bounce[^1].Guest == 0 )
			Error( "the bounce table keeps trailing free slots." );
		if ( snapshot.QueueEditCount < 0 )
			Error( $"the queue edit count {snapshot.QueueEditCount} is negative." );
		if ( snapshot.HeadNotReadyStreak < 0 || snapshot.MaximumHeadNotReadyStreak < snapshot.HeadNotReadyStreak )
			Error( $"the head-not-ready streaks ({snapshot.HeadNotReadyStreak}, {snapshot.MaximumHeadNotReadyStreak}) are negative or inverted." );
		if ( snapshot.CalledAgeTurns < 0 || snapshot.MaximumCalledAgeTurns < snapshot.CalledAgeTurns )
			Error( $"the called-age turns ({snapshot.CalledAgeTurns}, {snapshot.MaximumCalledAgeTurns}) are negative or inverted." );
		if ( snapshot.CalledTurn < -1 )
			Error( $"the called turn {snapshot.CalledTurn} is negative." );
		if ( snapshot.BoardedTotal < snapshot.OnRide.Length )
			Error( $"only {snapshot.BoardedTotal} guests boarded, but {snapshot.OnRide.Length} are aboard." );
		if ( snapshot.ReleasedTotal < 0 )
			Error( $"the released total {snapshot.ReleasedTotal} is negative." );
		return errors;
	}

	/// <summary>
	/// Restores a validated queue snapshot into this bridge: the linked list is rebuilt front first from
	/// <see cref="RideVisitorBridgeSnapshot.Queue"/>, and the rider lists, timers, queue geometry and admission
	/// counters are copied. Bridge-local problems throw <see cref="InvalidDataException"/> before anything is touched;
	/// whether the guest ids exist in the park is the caller's check (see
	/// <see cref="GuestSimulation.RestoreSnapshot"/>, which runs it first). The attached script's VAR_LETMEON must be
	/// restored with the value <see cref="RideVisitorBridgeSnapshot.LetMeOnWritten"/> describes — the script slice
	/// owns that variable — and <see cref="Attach"/> must already point at the restored script.
	/// </summary>
	public void RestoreSnapshot( RideVisitorBridgeSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( snapshot.AttractionId != AttractionId )
			throw new InvalidDataException( $"The queue snapshot describes attraction {snapshot.AttractionId}, not {AttractionId} ('{Name}')." );
		var problems = ValidateSnapshot( snapshot );
		if ( problems.Count > 0 )
			throw new InvalidDataException( $"Attraction {AttractionId} ('{Name}') queue snapshot does not restore: " + string.Join( " ", problems ) );
		// Candidates first; the live tables are only replaced once everything is parsed.
		var queue = snapshot.Queue;
		var linksCandidate = new Dictionary<int, (int Next, int Previous)>( queue.Length );
		for ( var index = 0; index < queue.Length; index++ )
			linksCandidate.Add( queue[index], (index + 1 < queue.Length ? queue[index + 1] : 0, index > 0 ? queue[index - 1] : 0) );
		var headCandidate = queue.Length > 0 ? queue[0] : 0;
		var tailCandidate = queue.Length > 0 ? queue[^1] : 0;
		links.Clear();
		foreach ( var (guestId, link) in linksCandidate )
			links.Add( guestId, link );
		head = headCandidate;
		tail = tailCandidate;
		onRide.Clear();
		onRide.AddRange( snapshot.OnRide );
		walkingOff.Clear();
		foreach ( var guestId in snapshot.WalkingOff )
			walkingOff.Enqueue( guestId );
		hopped.Clear();
		hopped.UnionWith( snapshot.Hopped );
		limbo.Clear();
		limbo.AddRange( snapshot.Limbo );
		bouncing.Clear();
		bouncing.AddRange( snapshot.Bounce );
		called = snapshot.CalledGuest;
		letMeOnWritten = snapshot.LetMeOnWritten;
		lastAdmissionTurn = snapshot.LastAdmissionTurn;
		calledTurn = snapshot.CalledTurn;
		queueCells.Clear();
		queueCells.AddRange( snapshot.QueueCells );
		QueueEditCount = snapshot.QueueEditCount;
		JoinCell = snapshot.JoinCell;
		HeadNotReadyStreak = snapshot.HeadNotReadyStreak;
		MaximumHeadNotReadyStreak = snapshot.MaximumHeadNotReadyStreak;
		CalledAgeTurns = snapshot.CalledAgeTurns;
		MaximumCalledAgeTurns = snapshot.MaximumCalledAgeTurns;
		LastAdmissionCheck = snapshot.LastAdmissionCheck;
		BoardedTotal = snapshot.BoardedTotal;
		ReleasedTotal = snapshot.ReleasedTotal;
	}

	// ---- Queue geometry ----------------------------------------------------------------------

	/// <summary>The entrance's outside cell (the original front cell), or null to use <see cref="EntranceCell"/>.</summary>
	public (int X, int Y)? QueueFrontCell { get; set; }
	/// <summary><see cref="GuestPathGrid.Directions"/> index from the front cell toward the ride's entrance cell, or −1.</summary>
	public int QueueEntranceDirection { get; set; } = -1;
	/// <summary>Queue cells from the front (index 0) to the back; the front may be a path cell.</summary>
	public IReadOnlyList<(int X, int Y)> QueueCells => queueCells.Count > 0 ? queueCells : new[] { EntranceCell };
	/// <summary>Cached back-of-queue cell (ride <c>+54</c>).</summary>
	public (int X, int Y) QueueBackCell => QueueCells[^1];
	/// <summary>Queue size in cells (ride <c>+60</c>), at least 1.</summary>
	public int QueueSizeInCells => Math.Max( 1, queueCells.Count );
	/// <summary>Walkable path cell where guests step onto the back of the queue, or null when the back is not connected to a path.</summary>
	public (int X, int Y)? JoinCell { get; private set; }
	/// <summary>Physical room: four guests per queue cell.</summary>
	public int QueueRoom => PositionsPerCell * QueueSizeInCells;
	/// <summary>The data limit (<see cref="ComputeQueueLimit"/>).</summary>
	public uint QueueLimit => ComputeQueueLimit( Parameters );
	/// <summary>Most guests that can be queued now: min(<see cref="QueueRoom"/>, <see cref="QueueLimit"/>).</summary>
	public int MaximumQueueLength => (int)Math.Min( (uint)QueueRoom, QueueLimit );
	/// <summary>Incremented whenever a recompute changed the queue cells (the original "queue edited" event).</summary>
	public int QueueEditCount { get; private set; }

	// ---- Admission signal (M3 gate) ------------------------------------------------------------

	/// <summary>The last admission evaluation (one per park turn).</summary>
	public AdmissionCheck LastAdmissionCheck { get; private set; }
	/// <summary>
	/// Consecutive admission evaluations, up to the last one, whose gates held with a head guest that did not yet stand
	/// at position 0 (<see cref="AdmissionCheck.HeadNotReady"/>); 0 once the head is called or a gate fails.
	/// </summary>
	public long HeadNotReadyStreak { get; private set; }
	/// <summary>Longest <see cref="HeadNotReadyStreak"/> so far.</summary>
	public long MaximumHeadNotReadyStreak { get; private set; }
	/// <summary>
	/// Park turns since the current <see cref="CalledGuest"/> was called without having boarded (or been withdrawn), as of
	/// the last admission evaluation; 0 when nobody is called or the host has no park turn.
	/// </summary>
	public long CalledAgeTurns { get; private set; }
	/// <summary>Largest <see cref="CalledAgeTurns"/> so far.</summary>
	public long MaximumCalledAgeTurns { get; private set; }
	/// <summary>Raised after every admission evaluation.</summary>
	public event Action<RideVisitorBridge, AdmissionCheck>? AdmissionChecked;

	/// <summary>
	/// The queue limit of <c>0xdcb74</c>: 100 for objects without <c>Info.HasQueue</c>, and for HasQueue rides
	/// trunc(max(QWTC × (G × CAP) / DUR, 4)) in single precision with G = SPEED / InitSpeed (1 when SPEED is 0);
	/// a NaN quotient gives 4. Unsigned 32-bit like the original: SPEED is read as an unsigned word and the quotient
	/// saturates at 2³² − 1.
	/// </summary>
	public static uint ComputeQueueLimit( QueueParameters parameters )
	{
		if ( !parameters.HasQueue )
			return NoQueueLimit;
		// [BIN:STP-PPC:0x100DCBB8 queue limit] SPEED is the unsigned word at ride +84; 0x101C3FBC converts the quotient to a u32, saturating at 2^32 − 1
		var speed = (uint)parameters.Speed;
		var g = speed == 0 ? 1f : speed / (float)parameters.InitSpeed;
		var quotient = parameters.QueueWaitTimeConstant * (g * parameters.Capacity) / parameters.Duration;
		if ( float.IsNaN( quotient ) )
			return 4;
		quotient = MathF.Max( quotient, 4f );
		return quotient >= 4294967296f ? uint.MaxValue : (uint)quotient;
	}

	/// <summary>Binds the bridge to the script it serves; <paramref name="open"/> reports whether the owner opened the ride.</summary>
	public void Attach( RideVM script, Func<bool> open )
	{
		vm = script;
		isOpen = open;
	}

	/// <summary>
	/// Recomputes the front, back and size of the queue from the grid (<c>0xdd43c</c>): start at the front
	/// cell and follow queue cells whose link points back at the current cell, at most
	/// <see cref="QueueWalkGuard"/> steps. Returns true when the cells changed (a queue edit).
	/// </summary>
	public bool RecomputeQueue( GuestPathGrid grid )
	{
		var previous = queueCells.ToArray();
		queueCells.Clear();
		var front = QueueFrontCell ?? EntranceCell;
		// A front cell that is neither a queue nor a path cell falls back to EntranceCell, the nearest walkable path cell (RIDES-028 in Level.Objects), as a one-cell queue.
		if ( !grid.IsQueue( front.X, front.Y ) && !grid.IsWalkable( front.X, front.Y ) )
			front = EntranceCell;
		queueCells.Add( front );
		var current = front;
		for ( var step = 0; step < QueueWalkGuard; step++ )
		{
			var next = NextQueueCell( grid, current );
			if ( next == null || queueCells.Contains( next.Value ) )
				break;
			queueCells.Add( next.Value );
			current = next.Value;
		}
		JoinCell = FindJoinCell( grid );
		if ( previous.SequenceEqual( queueCells ) )
			return false;
		QueueEditCount++;
		return true;
	}

	/// <summary>The queue cell behind (x, y): a neighbour of type 3 whose link points back at (x, y) (<c>0xdda18</c>).</summary>
	// [APPROX:QUEUE-002] neighbours are tried in GuestPathGrid.Directions order; the original pairs run-time offsets with links 16, 1, 64, 4 whose order is not established — evidence needed: the neighbour offset tables at run time
	public static (int X, int Y)? NextQueueCell( GuestPathGrid grid, (int X, int Y) cell )
	{
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			var (nx, ny) = (cell.X + dx, cell.Y + dy);
			if ( grid.IsQueue( nx, ny ) && GuestPathGrid.LinkDirection( grid.GetQueueLink( nx, ny ) ) == ((direction + 2) & 3) )
				return (nx, ny);
		}
		return null;
	}

	// [APPROX:QUEUE-003] guests step onto the back of the queue from the first walkable 4-neighbour (Directions order) that is not part of the queue; the original connection test 0xdd744 is not traced — evidence needed: 0xdd744 and the state-10 walk to the back cell
	private (int X, int Y)? FindJoinCell( GuestPathGrid grid )
	{
		var back = queueCells[^1];
		if ( grid.IsWalkable( back.X, back.Y ) )
			return back;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			var cell = (back.X + dx, back.Y + dy);
			if ( grid.IsWalkable( cell.Item1, cell.Item2 ) && !queueCells.Contains( cell ) )
				return cell;
		}
		return null;
	}

	/// <summary>Direction from queue cell <paramref name="index"/> toward its predecessor (the previous cell, or the ride entrance for the front).</summary>
	public int DirectionTowardsFront( int index )
	{
		var cells = QueueCells;
		if ( index > 0 && index < cells.Count )
			return GuestPathGrid.DirectionBetween( cells[index].X, cells[index].Y, cells[index - 1].X, cells[index - 1].Y );
		if ( QueueEntranceDirection is >= 0 and < 4 )
			return QueueEntranceDirection;
		// No known entrance side: face away from the second cell, else −Y.
		if ( cells.Count > 1 )
			return (GuestPathGrid.DirectionBetween( cells[0].X, cells[0].Y, cells[1].X, cells[1].Y ) + 2) & 3;
		return 0;
	}

	// ---- Guest list --------------------------------------------------------------------------

	public bool TryJoinQueue( int guestId ) => JoinQueue( guestId ) == QueueJoinResult.Joined;

	/// <summary>
	/// The joining checks of state 10 at the back cell (<c>0xeccb0</c>), in the original order: physical room
	/// (count &lt; 4 × cells), the excitement gate, then the limit. <paramref name="preferredExcitement"/> is the guest's
	/// preferred excitement (<c>PeepTypes[n].PreferredExcitement</c>); null skips the gate.
	/// </summary>
	public QueueJoinResult JoinQueue( int guestId, int? preferredExcitement = null )
	{
		if ( guestId <= 0 || !IsOpen )
			return QueueJoinResult.Closed;
		// [APPROX:QUEUE-018] a broken ride (VAR_BROKEN ≠ 0) refuses joins as if closed; the state-10 join checks of 0xeccb0 are traced without a breakdown test, and how a broken ride keeps guests out of state 10 is not — evidence needed: the ride +408 state and the state-6/state-10 handling of a broken ride
		if ( IsBroken )
			return QueueJoinResult.Closed;
		if ( links.ContainsKey( guestId ) || Holds( guestId ) )
			return QueueJoinResult.AlreadyQueued;
		var count = links.Count;
		// [BIN:STP-PPC:0x100DC988 queue room] a guest may join only while count < 4 × queue size in cells (+60)
		if ( count >= QueueRoom )
			return QueueJoinResult.NoRoom;
		// [BIN:STP-PPC:0x100ECD6C join gate] 0xe9a84 then 0xe02ec(ride, 1) and the 45 test; executed in the oracle (ORACLE.md F16, queue-join-gate)
		// [APPROX:QUEUE-004] the excitement of a ride with a thing behind ride +36 (60 % of the level plus up to 40 from three piece counts of 0x1ea40) is not modelled, because OpenTPW has no such thing and what the counts are is not traced — evidence needed: the ride +36 handle and 0x1ea40
		if ( preferredExcitement is { } preferred && ExcitementRefusal( preferred ) is { } refusal )
			return refusal;
		if ( count >= QueueLimit )
			return QueueJoinResult.QueueTooLong;
		// [BIN:STP-PPC:0x100DCD34 queue append] the guest is appended at the tail of the ride's linked list
		links[guestId] = (0, tail);
		if ( tail != 0 )
			links[tail] = (guestId, links[tail].Previous);
		else
			head = guestId;
		tail = guestId;
		return QueueJoinResult.Joined;
	}

	/// <summary>
	/// The live excitement of <c>0xe02ec(ride, 0)</c> (<c>0xdff70</c>): 0 when the data level is 0, else the sideshow formula,
	/// a coaster's guest value, or the data level scaled by the speed and duration ratios (<see cref="QueueJoinRules"/>).
	/// </summary>
	public int LiveExcitement
	{
		get
		{
			if ( excitementLevel == 0 )
				return 0;
			if ( Kind == RideVisitorKind.Sideshow )
				return QueueJoinRules.SideshowExcitement( Sideshow );
			if ( ExcitementSource is { } source )
				return source();
			// [APPROX:QUEUE-022] a bridge without any operating values (speed, design speed, duration and design duration all 0: a synthetic object, every original object has them) is read at its design speed and duration; the original would divide 0 by 0 (NaN, excitement 0) — evidence needed: none, no original object lacks them
			if ( Parameters is { Speed: 0, InitSpeed: 0, Duration: 0, InitDuration: null or 0 } )
				return excitementLevel;
			return QueueJoinRules.ScaledExcitement( excitementLevel, Parameters.Speed, Parameters.InitSpeed, Parameters.Duration, Parameters.InitDuration ?? Parameters.Duration );
		}
	}

	/// <summary><c>0xe9a84</c>: 100 while <see cref="ReliefWornOut"/>, else <c>clamp(live excitement − preferred, −50, 50)</c> over bytes.</summary>
	public int JoinDifference( int preferredExcitement ) =>
		QueueJoinRules.Difference( ReliefWornOut, ReliefWornOut ? 0 : LiveExcitement, preferredExcitement );

	/// <summary>
	/// The excitement gate of <see cref="JoinQueue"/> (<see cref="QueueJoinRules.Refusal"/>): only when the low byte of the type's
	/// <c>ExcitementLevel</c> is nonzero, and then |<see cref="JoinDifference"/>| ≥ 45 refuses: a negative difference is "not exciting
	/// enough" (bored), otherwise "too exciting" (scared). Null when the guest may join.
	/// </summary>
	public QueueJoinResult? ExcitementRefusal( int preferredExcitement ) =>
		QueueJoinRules.Refusal( excitementLevel, JoinDifference( preferredExcitement ) );

	/// <summary>0-based list position (<c>0xdd144</c>), or −1.</summary>
	public int GetQueuePosition( int guestId )
	{
		if ( !links.ContainsKey( guestId ) )
			return -1;
		var position = 0;
		for ( var guest = head; guest != guestId; guest = links[guest].Next )
			position++;
		return position;
	}

	public bool LeaveQueue( int guestId )
	{
		if ( !Unlink( guestId ) )
			return false;
		if ( called == guestId )
			Withdraw();
		return true;
	}

	private bool Unlink( int guestId )
	{
		if ( !links.Remove( guestId, out var link ) )
			return false;
		if ( link.Previous != 0 )
			links[link.Previous] = (link.Next, links[link.Previous].Previous);
		else
			head = link.Next;
		if ( link.Next != 0 )
			links[link.Next] = (links[link.Next].Next, link.Previous);
		else
			tail = link.Previous;
		return true;
	}

	private void Withdraw()
	{
		if ( letMeOnWritten && Get( RideVariables.VAR_LETMEON ) == called )
			Set( RideVariables.VAR_LETMEON, 0 );
		called = 0;
		letMeOnWritten = false;
	}

	/// <summary>
	/// The called guest reached the stand point (state 13, admission method <c>0xe03d0</c>): its id is written
	/// to VAR_LETMEON only while that is 0. True when written (the guest then waits for the script).
	/// </summary>
	public bool PresentForBoarding( int guestId )
	{
		if ( guestId == 0 || guestId != called || vm == null )
			return false;
		if ( letMeOnWritten )
			return true;
		if ( vm.InCriticalSection || Get( RideVariables.VAR_LETMEON ) != 0 )
			return false;
		Set( RideVariables.VAR_LETMEON, guestId );
		letMeOnWritten = true;
		return true;
	}

	// Scripts declare different variable subsets (e.g. features without VAR_LETMEOFF), so access by name.
	private int Get( RideVariables variable )
	{
		var index = vm?.GetVariableIndex( variable.ToString() ) ?? -1;
		return index < 0 ? 0 : vm!.Variables[index];
	}

	private void Set( RideVariables variable, int value )
	{
		var index = vm?.GetVariableIndex( variable.ToString() ) ?? -1;
		if ( index >= 0 )
			vm!.Variables[index] = value;
	}

	private bool Holds( int guestId ) => onRide.Contains( guestId );

	/// <summary>Host side of the protocol; call once per tick before the script advances.</summary>
	public void HostStep()
	{
		if ( vm == null )
			return;
		if ( vm.State != RideVMState.Running && vm.State != RideVMState.Waiting )
		{
			ReleaseAll();
			return;
		}
		if ( vm.InCriticalSection )
			return;

		var off = Get( RideVariables.VAR_LETMEOFF );
		if ( off != 0 )
		{
			Set( RideVariables.VAR_LETMEOFF, 0 );
			Release( off );
		}

		// State 14 (0xe05c8): the guest stays in the list until the script has consumed VAR_LETMEON.
		if ( called != 0 && letMeOnWritten && Get( RideVariables.VAR_LETMEON ) != called )
			MarkBoarded( called );

		// A breakdown takes precedence over a closed ride: SetState(1) writes VAR_RIDECLOSED = 1 itself, and the original still drains the
		// queue head first (0xe0aec) instead of emptying it at once.
		if ( !isOpen() && !IsBroken )
		{
			if ( called != 0 )
			{
				// The script never took the guest before closing; give it back.
				var guest = called;
				Withdraw();
				Unlink( guest );
				Host?.OnVisitorTurnedAway( this, guest );
			}
			foreach ( var guest in Queue )
			{
				Unlink( guest );
				Host?.OnVisitorTurnedAway( this, guest );
			}
			return;
		}

		// [APPROX:QUEUE-016] every ride evaluates admission once per park turn and every queued guest runs state 11 once per park turn; the live-list eligibility of the object and guest updates is not traced — evidence needed: the live-list flags of 0x100FA9B0 and the guest update dispatcher
		var turn = Host?.ParkTurn ?? -1;
		if ( turn >= 0 && turn == lastAdmissionTurn )
			return;
		lastAdmissionTurn = turn;
		// [BIN:STP-PPC:0x100E0AEC ride update, states 1, 2 and 4] while the ride is broken (state 1; 4 when condemned) every update removes the head guest from the queue and sends it away (0xdcebc, 0xee804) unless the script has already taken it; the rest wait their turn, so a queue drains one guest per park turn. Static reading (ORACLE.md F15): the dispatch is exercised in the oracle target queue-ride-update
		// [APPROX:QUEUE-019] a breakdown is read from VAR_BROKEN ≠ 0 at once; the original's ride update first runs the admission (0xe1864 → 0xe1404) on the turn it notices VAR_BROKEN and then SetState(1), which also writes VAR_RIDECLOSED = 1 and clears the open word +100, and only a repair reopens it — evidence needed: the callers that set ride state 0 again (0xdad24, 0xde8e4, 0xdf4d4, 0xdf90c, 0xdfb08), not traced to a player action
		if ( IsBroken )
		{
			DismissHead();
			return;
		}
		CheckAdmission( turn );
	}

	/// <summary>
	/// The ride update of a broken ride (<c>0xe0aec</c>): the head guest leaves the queue (<c>0xdcebc</c>) and is sent away
	/// (<c>0xee804</c>), which also withdraws the pending visitor and VAR_LETMEON when they name it. A head the script has
	/// already taken is not here any more (<see cref="MarkBoarded"/> removed it above), which is the original's "forcing him onto
	/// the ride" (<c>0xeda04</c>) case.
	/// </summary>
	private void DismissHead()
	{
		if ( head == 0 )
			return;
		var guest = head;
		LeaveQueue( guest );
		Dismissed++;
		Host?.OnVisitorDismissed( this, guest );
	}

	/// <summary>Guests a broken ride has sent away.</summary>
	public int Dismissed { get; private set; }

	/// <summary>
	/// The ride update's admission (<c>0xe1864 → 0xe1404</c>): call the head forward when VAR_LETMEON is 0,
	/// VAR_ONRIDE &lt; VAR_CAPACITY (skipped for track types 2/3), VAR_RUNNING is 0 or the ride runs
	/// continuously, nobody is pending, and the head stands in state 11 at recorded position 0.
	/// </summary>
	// [BIN:STP-PPC:0x100E1404 LetMeOn admission] LETMEON == 0, ONRIDE < CAPACITY, RUNNING == 0 or RunsContinuously (+46 & 0x100), no pending visitor, head in state 11 at +497 == 0; then head +504 = 1 and ride +104 = head
	private void CheckAdmission( long turn )
	{
		// Original objects always have VAR_CAPACITY written by the host; synthetic scripts without it use the bridge capacity.
		var capacity = Get( RideVariables.VAR_CAPACITY );
		if ( capacity <= 0 )
			capacity = Capacity;
		var conditions = Get( RideVariables.VAR_LETMEON ) == 0
			&& (Parameters.SkipsCapacityTest || Get( RideVariables.VAR_ONRIDE ) < capacity)
			&& (Get( RideVariables.VAR_RUNNING ) == 0 || Parameters.RunsContinuously)
			&& called == 0
			&& head != 0;
		var front = head;
		var atFront = front != 0 && (Host?.IsStandingAtFront( this, front ) ?? true);
		var calledNow = 0;
		if ( conditions && atFront )
		{
			called = head;
			calledTurn = turn;
			letMeOnWritten = false;
			calledNow = head;
			Host?.OnVisitorOffered( this, head );
			// Hosts that do not simulate the walk to the stand point present the guest at once.
			if ( Host?.WalksToBoard != true && called == calledNow )
				PresentForBoarding( calledNow );
		}
		var check = new AdmissionCheck( turn, conditions, front, atFront, calledNow );
		HeadNotReadyStreak = check.HeadNotReady ? HeadNotReadyStreak + 1 : 0;
		MaximumHeadNotReadyStreak = Math.Max( MaximumHeadNotReadyStreak, HeadNotReadyStreak );
		CalledAgeTurns = called != 0 && turn >= 0 && calledTurn >= 0 ? turn - calledTurn : 0;
		MaximumCalledAgeTurns = Math.Max( MaximumCalledAgeTurns, CalledAgeTurns );
		LastAdmissionCheck = check;
		AdmissionChecked?.Invoke( this, check );
	}

	private void MarkBoarded( int guest )
	{
		if ( guest <= 0 || onRide.Contains( guest ) )
			return;
		if ( guest == called )
		{
			called = 0;
			letMeOnWritten = false;
		}
		Unlink( guest );
		onRide.Add( guest );
		BoardedTotal++;
		Host?.OnVisitorBoarded( this, guest );
	}

	private void Release( int guest )
	{
		if ( !onRide.Remove( guest ) )
			return;
		// Host cleanup: do not leave a presentation slot on the previous ride after release.
		Heads?.Remove( guest );
		hopped.Remove( guest );
		limbo.RemoveAll( entry => entry.Guest == guest );
		for ( var index = 0; index < bouncing.Count; index++ )
			if ( bouncing[index].Guest == guest )
				FreeBounceSlot( bouncing, index );
		ReleasedTotal++;
		Host?.OnVisitorReleased( this, guest );
	}

	/// <summary>Releases every held guest and turns the queue away (ride removed or script stopped).</summary>
	public void ReleaseAll()
	{
		// Host teardown, not a claim about the original script destructor: clear even orphaned head ids.
		Heads?.Clear();
		if ( called != 0 )
		{
			var guest = called;
			if ( vm != null )
				Withdraw();
			called = 0;
			letMeOnWritten = false;
			Unlink( guest );
			Host?.OnVisitorTurnedAway( this, guest );
		}
		foreach ( var guest in Queue )
		{
			Unlink( guest );
			Host?.OnVisitorTurnedAway( this, guest );
		}
		foreach ( var guest in onRide.ToArray() )
			Release( guest );
		walkingOff.Clear();
	}

	/// <summary>Performs a visitor opcode; false for opcodes the bridge does not own.</summary>
	public bool TryPerform( RideEffectCall call, out int result )
	{
		result = 0;
		var now = call.VM.TimeMilliseconds;
		switch ( call.Opcode )
		{
			case Opcode.HUSH:
				MarkBoarded( call.Argument( 0 ) );
				return true;
			case Opcode.WALKON:
				MarkBoarded( call.Argument( 0 ) );
				return true;
			case Opcode.HOP:
				result = onRide.FirstOrDefault( guest => !hopped.Contains( guest ) );
				if ( result != 0 )
					hopped.Add( result );
				return true;
			case Opcode.WALKOFF:
			{
				var guest = call.Argument( 0 );
				if ( onRide.Contains( guest ) && !walkingOff.Contains( guest ) )
				{
					hopped.Add( guest );
					walkingOff.Enqueue( guest );
				}
				return true;
			}
			case Opcode.WALKGET:
				result = walkingOff.Count > 0 ? walkingOff.Dequeue() : 0;
				return true;
			case Opcode.LIMBO:
			{
				var guest = call.Argument( 0 );
				MarkBoarded( guest );
				if ( onRide.Contains( guest ) && !limbo.Any( entry => entry.Guest == guest ) )
					limbo.Add( (guest, now + Math.Max( 0, call.Argument( 1 ) ) * 1000.0) );
				return true;
			}
			case Opcode.UNLIMBO:
				result = TakeExpired( limbo, now, force: false );
				return true;
			case Opcode.FORCEUNLIMBO:
				result = TakeExpired( limbo, now, force: true );
				return true;
			case Opcode.INLIMBO:
				result = limbo.Count;
				return true;
			case Opcode.LIMBOSPACE:
				result = Math.Max( 0, call.VM.Script.LimboSize - limbo.Count );
				return true;
			case Opcode.BOUNCE:
			{
				var guest = call.Argument( 0 );
				MarkBoarded( guest );
				var seconds = call.Argument( 1 ) > 0 ? call.Argument( 1 ) : DefaultBounceSeconds;
				if ( onRide.Contains( guest ) && !bouncing.Any( entry => entry.Guest == guest ) )
					TakeBounceSlot( bouncing, call.VM.Script.BounceSize, guest, now + seconds * 1000.0, now );
				return true;
			}
			case Opcode.UNBOUNCE:
			case Opcode.FORCEUNBOUNCE:
			{
				// Corpus: `TEST VAR_LETMEOFF; BRANCH_NZ; UNBOUNCE VAR_LETMEOFF` writes the leaving guest into LETMEOFF.
				var guest = TakeUnbounced( bouncing, now, call.Opcode == Opcode.FORCEUNBOUNCE );
				if ( call.IsVariable( 0 ) )
					call.SetOutput( 0, guest );
				result = guest;
				return true;
			}
			case Opcode.BOUNCING:
				result = bouncing.Count( entry => entry.Guest != 0 );
				return true;
			case Opcode.ADDHEAD:
				// [BIN:STP-PPC:0x100B0E9C ADDHEAD] no register write; the head is drawn by RideHeadRenderer from the table
				Heads?.Add( call.Argument( 0 ) );
				return true;
			case Opcode.DELHEAD:
				// [BIN:STP-PPC:0x100B0F90 DELHEAD] no register write
				Heads?.Remove( call.Argument( 0 ) );
				return true;
			default:
				return false;
		}
	}

	/// <summary>
	/// BOUNCE (<c>0xadf40</c>, BOARD-plan §5): the first free slot (visitor 0) of the script's table of
	/// <paramref name="capacity"/> slots (the header bounce size) gets the visitor, its deadline and its start.
	/// With every slot taken nothing is stored and the visitor is not bouncing.
	/// </summary>
	// [BIN:STP-PPC:0x100ADF40 BOUNCE] scan the +100 slots of the +40 table in index order; the first with visitor word 0 gets visitor, deadline now + 1000 × dur and start now, +108 goes up, return 1; a full table returns 0 and stores nothing; the deadline comes from one clock call (0xadfb0) and the start from a second (0xadfcc)
	// [APPROX:QUEUE-020] one script-clock reading stands for both of BOUNCE's clock calls: OpenTPW's clock does not advance inside an opcode, the original's can tick between the two calls and put the start 1 ms after the deadline's base, which moves UNBOUNCE's 200 ms window by 1 ms — evidence needed: none for the reading (0xadfb0, 0xadfcc); a stepped clock cannot reproduce the tick
	internal static bool TakeBounceSlot( List<(int Guest, double Until, double Start)> slots, int capacity, int guest, double until, double now )
	{
		for ( var index = 0; index < capacity; index++ )
		{
			if ( index == slots.Count )
			{
				slots.Add( (guest, until, now) );
				return true;
			}
			if ( slots[index].Guest == 0 )
			{
				slots[index] = (guest, until, now);
				return true;
			}
		}
		return false;
	}

	/// <summary>Clears slot <paramref name="index"/>; free slots at the end of the table are dropped, so a table without holes keeps its length.</summary>
	private static void FreeBounceSlot( List<(int Guest, double Until, double Start)> slots, int index )
	{
		slots[index] = default;
		while ( slots.Count > 0 && slots[^1].Guest == 0 )
			slots.RemoveAt( slots.Count - 1 );
	}

	/// <summary>
	/// UNBOUNCE (<c>0xae020</c>, BOARD-plan §5): the first occupied slot whose deadline has passed (deadline &lt; now,
	/// strict) inside the first 200 ms of each second since the guest bounced, trunc(((now − start) mod 1000) / 200) == 0
	/// with a C remainder and 32-bit times in whole milliseconds. FORCEUNBOUNCE takes the first occupied slot. The slot
	/// is cleared in place, so a later BOUNCE fills it first.
	/// </summary>
	// [BIN:STP-PPC:0x100AE020 UNBOUNCE] slots in index order; release needs deadline < now (cmpw, 0x100AE088) and ((now − start) mod 1000) / 200 == 0 (0x100AE040..0x100AE0B0)
	internal static int TakeUnbounced( List<(int Guest, double Until, double Start)> list, double now, bool force )
	{
		var clock = (long)Math.Floor( now );
		for ( var index = 0; index < list.Count; index++ )
		{
			var (guest, until, start) = list[index];
			if ( guest == 0 )
				continue;
			var elapsed = unchecked( (int)(clock - (long)Math.Floor( start )) );
			if ( force || ((long)Math.Floor( until ) < clock && elapsed % 1000 / 200 == 0) )
			{
				FreeBounceSlot( list, index );
				return guest;
			}
		}
		return 0;
	}

	private static int TakeExpired( List<(int Guest, double Until)> list, double now, bool force )
	{
		for ( var index = 0; index < list.Count; index++ )
		{
			if ( force || list[index].Until <= now )
			{
				var guest = list[index].Guest;
				list.RemoveAt( index );
				return guest;
			}
		}
		return 0;
	}
}

/// <summary>
/// Routes visitor opcodes to a <see cref="RideVisitorBridge"/> and everything else to <paramref name="inner"/>, including
/// TRIGWAITANIM's AnimID query: the script has an object exactly when <paramref name="inner"/> answers it.
/// </summary>
public sealed class VisitorRideScriptEffects( RideVisitorBridge bridge, IRideScriptEffects inner ) : IRideScriptEffects, IRideAnimationQuery
{
	public int Perform( RideEffectCall call ) => bridge.TryPerform( call, out var result ) ? result : inner.Perform( call );

	public int GetAnimationId( RideVM vm, int channel ) =>
		((IRideAnimationQuery)this).TryGetAnimationId( vm, channel, out var animationId ) ? animationId : ObjectAnimator.NoAnimation;

	bool IRideAnimationQuery.TryGetAnimationId( RideVM vm, int channel, out int animationId )
	{
		if ( inner is IRideAnimationQuery query )
			return query.TryGetAnimationId( vm, channel, out animationId );
		animationId = ObjectAnimator.NoAnimation;
		return false;
	}
}
