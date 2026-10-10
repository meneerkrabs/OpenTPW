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
	/// <summary>Queue limit of rides with <c>Info.HasQueue</c>.</summary>
	// [BIN:STP-PPC:0x100DCB74 queue limit] HasQueue (flag 0x8) gives limit 100; otherwise trunc(max(QWTC × (SPEED/InitSpeed × CAP) / DUR, 4))
	public const int HasQueueLimit = 100;
	/// <summary>Excitement difference at which a guest refuses to join.</summary>
	// [BIN:STP-PPC:0x100ECCB0 join excitement gate] |difference| ≥ 45 leaves with "ride is not exciting enough!" / "ride is too exciting!"
	public const int ExcitementGate = 45;
	/// <summary>Iteration guard of the back-of-queue walk ("GetBackOfQueue() crashed!").</summary>
	public const int QueueWalkGuard = 1000;

	private readonly Dictionary<int, (int Next, int Previous)> links = new(); // guest +552/+554
	private int head; // ride +56
	private int tail;
	private readonly List<int> onRide = new();          // boarded, in boarding order
	private readonly Queue<int> walkingOff = new();     // WALKOFF done, waiting for WALKGET
	private readonly HashSet<int> hopped = new();
	private readonly List<(int Guest, double Until)> limbo = new();
	private readonly List<(int Guest, double Until)> bouncing = new();
	private readonly List<(int X, int Y)> queueCells = new();
	private RideVM? vm;
	private int called;       // ride +104: the pending visitor
	private bool letMeOnWritten;
	private long lastAdmissionTurn = long.MinValue;
	private long calledTurn = -1;
	private Func<bool> isOpen = () => true;

	public RideVisitorBridge( int attractionId, string name, RideVisitorKind kind, int capacity, int excitementLevel, int attractionValue,
		GuestNeeds satisfies = GuestNeeds.None, int price = 0 )
	{
		if ( attractionId <= 0 )
			throw new ArgumentOutOfRangeException( nameof( attractionId ) );
		AttractionId = attractionId;
		Name = name;
		Kind = kind;
		Capacity = Math.Max( 1, capacity );
		ExcitementLevel = excitementLevel;
		AttractionValue = attractionValue;
		Satisfies = satisfies;
		Price = price;
	}

	public int AttractionId { get; }
	public string Name { get; }
	public RideVisitorKind Kind { get; }
	public int Capacity { get; }
	public int ExcitementLevel { get; }
	public int AttractionValue { get; }
	public GuestNeeds Satisfies { get; }
	public int Price { get; }
	/// <summary>Toilets (<c>UsageInfo.ProvidesRelief</c>): queueing guests do not leave for the toilet.</summary>
	public bool ProvidesRelief => Satisfies.HasFlag( GuestNeeds.Toilet );
	/// <summary>Walkable path cell the queue falls back to when the entrance's outside cell is neither a queue nor a path cell.</summary>
	public (int X, int Y) EntranceCell { get; set; }
	public (int X, int Y) ExitCell { get; set; }
	public bool HasCells { get; set; }
	/// <summary>Queue rules from the object's .sam data (<see cref="QueueParameters"/>); defaults describe a queue-less object.</summary>
	public QueueParameters Parameters { get; set; } = QueueParameters.Default;
	public bool HasQueue => Parameters.HasQueue;
	public IRideVisitorHost? Host { get; set; }
	public bool IsOpen => vm != null && vm.State is (RideVMState.Running or RideVMState.Waiting) && isOpen();
	/// <summary>The ride reports a failure (<c>VAR_BROKEN</c>); queueing guests leave.</summary>
	public bool IsBroken => vm != null && Get( RideVariables.VAR_BROKEN ) != 0;
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
		AddTimed( bouncing );
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
	public int QueueLimit => ComputeQueueLimit( Parameters );
	/// <summary>Most guests that can be queued now: min(<see cref="QueueRoom"/>, <see cref="QueueLimit"/>).</summary>
	public int MaximumQueueLength => Math.Min( QueueRoom, QueueLimit );
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
	/// The queue limit of <c>0xdcb74</c>: 100 for HasQueue rides, otherwise
	/// trunc(max(QWTC × (G × CAP) / DUR, 4)) in single precision with G = SPEED / InitSpeed (1 when SPEED is 0);
	/// a NaN quotient gives 4.
	/// </summary>
	public static int ComputeQueueLimit( QueueParameters parameters )
	{
		if ( parameters.HasQueue )
			return HasQueueLimit;
		var g = parameters.Speed == 0 ? 1f : parameters.Speed / (float)parameters.InitSpeed;
		var quotient = parameters.QueueWaitTimeConstant * (g * parameters.Capacity) / parameters.Duration;
		if ( float.IsNaN( quotient ) )
			return 4;
		quotient = MathF.Max( quotient, 4f );
		return quotient >= int.MaxValue ? int.MaxValue : (int)quotient;
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

	public bool TryJoinQueue( int guestId ) => JoinQueue( guestId, 0 ) == QueueJoinResult.Joined;

	/// <summary>
	/// The joining checks of state 10 at the back cell (<c>0xeccb0</c>), in the original order: physical room
	/// (count &lt; 4 × cells), the excitement gate, then the limit. <paramref name="excitementDifference"/> is
	/// the guest's preferred excitement minus the ride's excitement.
	/// </summary>
	public QueueJoinResult JoinQueue( int guestId, int excitementDifference )
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
		// [APPROX:QUEUE-004] the excitement gate applies to rides and sideshows and uses |preferred − ride excitement|; the predicate 0xe02ec(ride, 1) and the difference 0xe9a84 are not traced — evidence needed: 0x100E02EC and 0x100E9A84
		if ( Kind is RideVisitorKind.Ride or RideVisitorKind.Sideshow && Math.Abs( excitementDifference ) >= ExcitementGate )
			return excitementDifference > 0 ? QueueJoinResult.NotExcitingEnough : QueueJoinResult.TooExciting;
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

		if ( !isOpen() )
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
		// [APPROX:QUEUE-019] admission is skipped while VAR_BROKEN ≠ 0: the ride update calls 0xe1864 → 0xe1404 only when ride +408 == 0, and a breakdown is read as +408 ≠ 0 — evidence needed: the ride +408 state names and their writers
		if ( IsBroken )
			return;
		CheckAdmission( turn );
	}

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
		hopped.Remove( guest );
		limbo.RemoveAll( entry => entry.Guest == guest );
		bouncing.RemoveAll( entry => entry.Guest == guest );
		ReleasedTotal++;
		Host?.OnVisitorReleased( this, guest );
	}

	/// <summary>Releases every held guest and turns the queue away (ride removed or script stopped).</summary>
	public void ReleaseAll()
	{
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
					bouncing.Add( (guest, now + seconds * 1000.0) );
				return true;
			}
			case Opcode.UNBOUNCE:
			case Opcode.FORCEUNBOUNCE:
			{
				// Corpus: `TEST VAR_LETMEOFF; BRANCH_NZ; UNBOUNCE VAR_LETMEOFF` writes the leaving guest into LETMEOFF.
				var guest = TakeExpired( bouncing, now, call.Opcode == Opcode.FORCEUNBOUNCE );
				if ( call.IsVariable( 0 ) )
					call.SetOutput( 0, guest );
				result = guest;
				return true;
			}
			case Opcode.BOUNCING:
				result = bouncing.Count;
				return true;
			case Opcode.ADDHEAD:
			case Opcode.DELHEAD:
				// Head sprites (Info.DoHeadProcessing) are cosmetic; on-ride guests are not drawn yet.
				return true;
			default:
				return false;
		}
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

/// <summary>Routes visitor opcodes to a <see cref="RideVisitorBridge"/> and everything else to <paramref name="inner"/>.</summary>
public sealed class VisitorRideScriptEffects( RideVisitorBridge bridge, IRideScriptEffects inner ) : IRideScriptEffects
{
	public int Perform( RideEffectCall call ) => bridge.TryPerform( call, out var result ) ? result : inner.Perform( call );
}
