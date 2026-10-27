namespace OpenTPW;

/// <summary>
/// Connects real guests to an original RSE script and owns the attraction's queue (docs/GUESTS.md,
/// docs/reverse/QUEUE-plan.md). The queue is the original's per-ride state: front cell, cached back cell,
/// size in cells and a doubly linked list of guest ids with one called ("pending") visitor. Between
/// slices, the host calls the front guest forward when the original admission gates
/// hold; the guest walks to the stand point and writes its id to VAR_LETMEON when that is 0; it stays in
/// the list until the script consumes it. When the script puts a guest id in VAR_LETMEOFF the host
/// releases that guest at the exit and writes 0 back. The bridge owns WALKON, WALKOFF, WALKGET, BOUNCE, UNBOUNCE,
/// FORCEUNBOUNCE, BOUNCING, ADDHEAD and DELHEAD; HUSH, HOP and the LIMBO family are the script VM's own heap and
/// limbo table (RideVM, tp.exe 0x553D25, 0x553DAA, 0x554DE5..0x555045) and never reach the bridge: a guest boards when the
/// script consumes VAR_LETMEON (or through WALKON/BOUNCE) and leaves through VAR_LETMEOFF. Walk animations along ride nodes
/// are not simulated (WALKON
/// and WALKOFF complete immediately).
/// </summary>
public sealed class RideVisitorBridge : IRideVisitorBridge
{
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
	// [BIN:TPW-EXE:0x00556F40 walk table] the script's walk table: header walk size entries of 0x20 bytes at +0x2C (count +0x7C); WALKON takes the first free entry (state 0), WALKOFF finds its rider's entry, WALKGET hands out the first entry in state 4 and frees it. A null slot is a free entry
	private readonly List<WalkRecord?> walkTable = new();
	private readonly List<(int Guest, double Until, double Start)> bouncing = new(); // BOUNCE slot table in index order: visitor (0 = free), deadline, start; trailing free slots are trimmed
	private readonly List<(int X, int Y)> queueCells = new();
	// [BIN:TPW-EXE:0x00558C45 script creation] the base of the node numbers BOUNCE entries take (script +0x70) starts at 1; the base height (+0x6E, a signed word) is not set there, so it starts at the block's 0
	public const int DefaultBounceNodeBase = 1;
	private int bounceNodeBase = DefaultBounceNodeBase;
	/// <summary>The node number each BOUNCE stored in its slot when it filled it (index-parallel to <see cref="bouncing"/>; a free or trailing slot keeps no entry).</summary>
	private readonly List<int> bounceNodes = new();
	private short bounceBaseHeight;
	private RideVM? vm;
	private int called;       // ride +104: the pending visitor
	private bool letMeOnWritten;
	private long lastAdmissionTurn = long.MinValue;
	private long calledTurn = -1;
	private Func<bool> isOpen = () => true;     // the ride's open word (+0x68)
	private Func<bool> takesGuests = () => true; // the coaster and track tests of the choice (0x00441970, the track word +0x2C of track types 1 and 2)
	private Func<bool> refusesQueue = () => false; // the queue's own test (0x00500612: a coaster failing 0x00441970, or track type 1 with +0x2C at 0)
	private readonly int excitementLevel;

	public RideVisitorBridge( int attractionId, string name, RideVisitorKind kind, int capacity, int excitementLevel, int attractionValue,
		GuestNeeds satisfies = GuestNeeds.None, int price = 0 )
	{
		if ( attractionId <= 0 )
			throw new ArgumentOutOfRangeException( nameof( attractionId ) );
		AttractionId = attractionId;
		TypeId = attractionId;
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
	public int Capacity { get; private set; }
	/// <summary>The capacity the ride runs with now: the script's <c>VAR_CAPACITY</c> (which the ride settings change), else the catalogue capacity.</summary>
	public int LiveCapacity => Get( RideVariables.VAR_CAPACITY ) is var live and > 0 ? live : Capacity;
	// Catalogue fallback only: existing occupied rider/head tables are never resized or cleared.
	internal void SetCatalogueFallbackCapacity( int capacity ) => Capacity = Math.Max( 1, capacity );
	/// <summary>The excitement shown to the HUD and used by the attraction score: the coaster's guest value when there is a source, else the .sam level.</summary>
	public int ExcitementLevel => ExcitementSource?.Invoke() ?? excitementLevel;
	/// <summary>The type's <c>UsageInfo.ExcitementLevel</c> word (type <c>+316</c>); its low byte decides whether guests are tested at all.</summary>
	public int StaticExcitementLevel => excitementLevel;
	/// <summary>Replaces the .sam excitement with a live value (a coaster's rating, <c>0xdff70</c>), or null.</summary>
	public Func<int>? ExcitementSource { get; set; }
	/// <summary>
	/// A ride with a controller behind it (a water ride's bump controller, docs/TRACKS.md) turns the data level into its excitement base before the speed and
	/// duration ratios scale it (60 % of the level plus up to 40 from the circuit's piece counts); null for every other ride.
	/// </summary>
	public Func<int, int>? ExcitementBase { get; set; }
	/// <summary>The sideshow's goods, price and win chance, read by <see cref="LiveExcitement"/>; the level mirrors them from the economy each tick.</summary>
	public SideshowTerms Sideshow
	{
		get => sideshow;
		set
		{
			sideshow = value;
			HasSideshowTerms = true;
		}
	}

	private SideshowTerms sideshow;

	/// <summary>True once <see cref="Sideshow"/> was set: all-zero terms (a free visit) are then live terms, not missing ones.</summary>
	public bool HasSideshowTerms { get; private set; }
	/// <summary>The wear gauge (ride <c>+64</c>, the state of repair 0–100) the relief predicate reads; the level mirrors it from the economy.</summary>
	public double WearGauge { get; set; } = 100;
	/// <summary>The ride's head table for ADDHEAD/DELHEAD, null when its model has no head records (see <see cref="RideHeadSpace"/>).</summary>
	// [BIN:TPW-EXE:0x00558D2B script init] only the root script gets a table: the init stores the thing id (+0xAC) and, when it is 0, skips the head-id lookup and the table allocation (0x558D2B–0x558E0B), and SPAWNCHILD (0x555081) and SPAWNSOUND (0x5551AB) create their script with thing id 0 and copy the parent's thing id only afterwards (0x555168–0x55519A)
	public RideHeadSpace? Heads { get; set; }

	/// <summary>The head table of the script that runs a head opcode: the root's; none for a child or sound script, whose ADDHEAD and DELHEAD do nothing.</summary>
	// [BIN:TPW-EXE:0x00554C3E ADDHEAD] the handler exits when the table pointer (+0x30) is NULL (0x554C89), and DELHEAD does the same (0x554D6F), so head opcodes in child and sound scripts have no effect
	private RideHeadSpace? HeadTableFor( RideVM vm ) => vm.Parent == null ? Heads : null;

	/// <summary>Appends the visitors with a head up and the model node each hangs from, without allocating.</summary>
	public void CollectHungHeads( List<(int Visitor, int Node)> into ) => Heads?.AppendHung( into );

	/// <summary>Visitors with a head up and the model node each hangs from (allocates; for tests and the smoke test).</summary>
	public IEnumerable<(int Visitor, int Node)> HungHeads()
	{
		var list = new List<(int Visitor, int Node)>();
		CollectHungHeads( list );
		return list;
	}

	public int AttractionValue { get; }
	/// <summary>Type <c>UsageInfo.ThirstEffect</c> (<c>+324</c>).</summary>
	public int ThirstEffect { get; set; }
	/// <summary>Type <c>UsageInfo.HungerEffect</c> (<c>+328</c>).</summary>
	public int HungerEffect { get; set; }
	/// <summary>Type <c>UsageInfo.VomitEffect</c> (<c>+332</c>).</summary>
	public int VomitEffect { get; set; }
	/// <summary>Type <c>UsageInfo.InitCostOfGoods</c> (<c>+320</c>).</summary>
	public int InitCostOfGoods { get; set; }
	/// <summary>Type <c>UsageInfo.RipOffOK</c> (tp.exe type <c>+0x16C</c>).</summary>
	public int RipOffOk { get; set; }
	/// <summary>Type <c>UsageInfo.FatigueEffect</c> (tp.exe type <c>+0xE8</c>).</summary>
	public int FatigueEffect { get; set; }
	/// <summary>Type <c>UsageInfo.HappinessEffect</c> (<c>+336</c>).</summary>
	public int HappinessEffect { get; set; }
	/// <summary>Type <c>UsageInfo.LitterEffect</c> (tp.exe type <c>+0x154</c>).</summary>
	public int LitterEffect { get; set; }
	/// <summary>Type <c>UsageInfo.GoldenTicketCost</c> (<c>+196</c>).</summary>
	public int GoldenTicketCost { get; set; }
	/// <summary>Type <c>Upgrades[0].CostOfUpgrade</c> (<c>+440</c>).</summary>
	public int UpgradeCost { get; set; }
	/// <summary>Type <c>UsageInfo.ISIndoors</c> (<c>+276</c>).</summary>
	public bool IsIndoors { get; set; }
	/// <summary>The object type (<c>Info.Id</c>); defaults to the attraction's own id (every attraction its own type).</summary>
	public int TypeId { get; set; }
	/// <summary>Days since the attraction was bought, mirrored from the economy each tick; <see cref="uint.MaxValue"/> (not new) until then.</summary>
	public uint AgeDays { get; set; } = uint.MaxValue;
	public GuestNeeds Satisfies { get; }
	public int Price { get; }
	/// <summary>The type's <c>UsageInfo.SpecialIngredient</c> (<c>+344</c>); 4 (Sugar) makes a visit raise the guest's speed boost.</summary>
	public int SpecialIngredient { get; set; }
	/// <summary>The type's <c>UsageInfo.AppearanceEffect</c> (tp.exe <c>+0x15C</c>): 1 gives the guest a balloon, 2 puts on or returns a costume.</summary>
	public int AppearanceEffect { get; set; }

	/// <summary>[EXT:umbrellas-extension] The weather and generation percentages of a brolly shop, set only for the umbrellas extension's own objects (<see cref="IRideVisitorBridge.UmbrellaTerms"/>).</summary>
	public UmbrellaShopTerms? UmbrellaTerms { get; set; }

	/// <summary>[EXT:winter-extension] The weather and season percentages of a winter kiosk or a glühwein stall, set only for the winter extension's own objects (<see cref="IRideVisitorBridge.WinterTerms"/>).</summary>
	public WinterShopTerms? WinterTerms { get; set; }
	/// <summary>The ride's amount of the special ingredient (Mac <c>+404</c>, PC <c>+0x198</c> beside the price per use at <c>+0x194</c>; the serializer <c>0x004DB7D0</c> names it <c>mAmountOfSpecialIngredient</c>), 50 when the ride is built.</summary>
	// [BIN:TPW-EXE:0x4b0aa0 ingredient amount] the amount is the economy record's (ParkObjectState.IngredientAmount): 50 when the ride is built (0x4db090 stores 0x32 at +0x198, 0x4db3b3), the shop window's 0..100 slider (range 0x4b06d2, shown for ingredients 1..4 by 0x4b0950) applied to the object (0x4b0aa0) or every object of the type (0x4b0ae0), saved with the park and imported as mAmountOfSpecialIngredient (0x4dcdc1); a Sugar visit adds trunc(6 x amount / 100) (0x4fe5f8)
	// The level mirrors it from the object's economy record (ParkObjectState.IngredientAmount, saved) every tick; tests set it directly.
	public int IngredientAmount { get; internal set; } = GuestSpeed.DefaultIngredientAmount;
	/// <summary>The shop's quality of goods (PC <c>+0x18C</c>, <c>mQualityOfGoods</c>), 50 when built; the level mirrors it from the economy record (ParkObjectState.QualityOfGoods) every tick.</summary>
	public int QualityOfGoods { get; internal set; } = GuestAppearance.BuiltQualityOfGoods;
	/// <summary>Toilets (<c>UsageInfo.ProvidesRelief</c>): queueing guests do not leave for the toilet.</summary>
	public bool ProvidesRelief => Satisfies.HasFlag( GuestNeeds.Toilet );
	/// <summary>Walkable path cell the queue falls back to when the entrance's outside cell is neither a queue nor a path cell.</summary>
	public (int X, int Y) EntranceCell { get; set; }
	public (int X, int Y) ExitCell { get; set; }
	/// <summary>[EXT:pools-swim] The object is a changing room (<c>Pools.ChangingRoom</c>); set when the level creates the bridge.</summary>
	public bool IsChangingRoom { get; set; }
	/// <summary><c>UsageInfo.RequiresTeleport</c> (type record +0x100): a guest boarding or leaving beams (particle 19); set when the runtime creates the bridge.</summary>
	public bool RequiresTeleport { get; set; }
	/// <summary>UsageInfo.RideHandlesSprite (type +0x104), inherited from the object settings.</summary>
	public bool RideHandlesSprite { get; set; }
	/// <summary>[EXT:pools-swim] Set by the runtime when it stops (demolition) before it releases the guests, so a changing room's guests do not start a swim; not saved (a stopped runtime restores stopped).</summary>
	public bool IsStopped { get; set; }
	/// <summary>[EXT:pools-swim] The cells beyond the back of a changing room; set when the level registers it (geometry only: whether one is a pool cell is read when a guest swims).</summary>
	public IReadOnlyList<(int X, int Y)> PoolAccessCandidates { get; set; } = Array.Empty<(int X, int Y)>();
	public (float X, float Y) EntryCellStandPos { get; set; } = (0.5f, 0.5f);
	public (float X, float Y) ExitCellAppearPos { get; set; } = (0.5f, 0.5f);
	/// <summary>The object's own exit cell (<see cref="IRideVisitorBridge.ExitAccessCell"/>), set when the level registers a placed object; not saved (the level registers it again on load).</summary>
	public (int X, int Y)? ExitAccessCell { get; set; }
	public int Rotation { get; set; }
	public bool HasCells { get; set; }
	/// <summary>Queue rules from the object's .sam data (<see cref="QueueParameters"/>); defaults describe a queue-less object.</summary>
	public QueueParameters Parameters { get; set; } = QueueParameters.Default;
	public bool HasQueue => Parameters.HasQueue;
	public IRideVisitorHost? Host { get; set; }
	public bool IsOpen => ScriptRuns && isOpen() && takesGuests();
	/// <summary>
	/// The ride's script runs: a guest walking to it may join (state 10). Unlike <see cref="IsOpen"/> it ignores the open word, the breakdown and the coaster or track test,
	/// which the original's join does not read either (the coaster predicate <c>0x00441970</c> is called by the choice, the reopen guard, the open routines and state 11 only).
	/// </summary>
	// [BIN:TPW-EXE:0x00441970 callers] the predicate's callers are 0x004DD920 (the choice's counts-test), 0x004DE1F0 (the queue edit's reopening), 0x004DF290 (the reopen guard), 0x004DF390 (the open routine), 0x004DF8F0 (the repair), 0x004DFE30 and 0x004E0050 (the maintenance request and its cancel) and 0x004FFFF0 (state 11); the join 0x004FFBC0 and the admission 0x004E1220 call none of them
	public bool AcceptsJoins => ScriptRuns;
	/// <summary>The coaster or track refuses guests (the original's state 11 sends a queued guest away with thought 13, <c>0x004FFFF0</c>).</summary>
	public bool TrackRefusesGuests => vm != null && refusesQueue();
	private bool ScriptRuns => vm != null && vm.State is (RideVMState.Running or RideVMState.Waiting);
	/// <summary>
	/// The ride script reports a breakdown (<c>VAR_BROKEN</c>, which the original's ride update turns into ride state 1, or 4 when
	/// the life gauge is 0): the ride dismisses its queue head first, one guest per park turn, and takes nobody (ORACLE.md F15).
	/// </summary>
	public bool IsBroken => vm != null && Get( RideVariables.VAR_BROKEN ) != 0;
	/// <summary>
	/// The ride is in state 1 (broken down) or 4 (condemned: the life gauge is 0): entered by the ride update that reads <c>VAR_BROKEN</c>, left only when
	/// the ride is opened again (a repair, an upgrade's end or the open button: <c>SetState(0)</c>), whatever the script does to <c>VAR_BROKEN</c> meanwhile.
	/// The choice's counts-test refuses both, and so does the ride update (both dismiss the queue head); a queued guest's move-up holds only in state 1
	/// (<see cref="HoldsQueuedGuests"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x004E0E60 SetState] the state word +0x19C is read as it stands: the repair, the open routine and the other openers write SetState(0) with the open word (OriginalObjectRuntime.Open), so an opened ride has left state 1
	public bool IsBrokenDown => stateWord.IsBroken;
	/// <summary>
	/// The ride is in state 1 exactly (the predicate <c>0x004E0370</c>, <c>+0x19C == 1</c>): a queued guest's move-up holds its walk only then, so in state 4 (condemned) the guests
	/// behind the dismissed head walk on.
	/// </summary>
	// [BIN:TPW-EXE:0x004E0370 state-1 predicate] returns ride +0x19C == 1; the guest's state 11 reads it at 0x00500523 and holds the move-up walk for a true result only
	public bool HoldsQueuedGuests => stateWord.Value == RideObjectStates.BrokenDown;
	/// <summary>The ride update's state 4 (<see cref="LifeGauge"/> truncated to a zero byte when the update read <c>VAR_BROKEN</c>, <c>0x004E14E0</c>).</summary>
	public bool IsCondemned => stateWord.Value == RideObjectStates.Condemned;
	/// <summary>The ride's life gauge (<c>+0x48</c>, 0..100): the level mirrors it from the economy; the ride update reads it when it enters state 1 or 4.</summary>
	public double LifeGauge { get; set; } = 100;
	/// <summary>
	/// The ride's state word (+0x19C, <see cref="RideStateWord"/>): entered at state 1 or 4 when the ride update reads VAR_BROKEN in state 0, left through <c>SetState(0)</c> when the ride opens again. The level
	/// replaces the bridge's own word with the one of the ride's economy record, so the economy, the guests and the opening guard read one word; a broken state the bridge held already moves to the new word.
	/// </summary>
	public RideStateWord StateWord
	{
		get => stateWord;
		internal set
		{
			ArgumentNullException.ThrowIfNull( value );
			if ( ReferenceEquals( value, stateWord ) )
				return;
			if ( stateWord.IsBroken && !value.IsBroken )
				value.Restore( stateWord.Value );
			stateWord = value;
		}
	}
	private RideStateWord stateWord = new();

	/// <summary>Restores a supported original retained state without admission, queue or script side effects.</summary>
	internal void RestoreOriginalBrokenState( bool isCondemned, double lifeGauge )
	{
		stateWord.Restore( isCondemned ? RideObjectStates.Condemned : RideObjectStates.BrokenDown );
		LifeGauge = lifeGauge;
	}

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
		hash.Add( MaximumQueueLength );
		hash.Add( called );
		hash.Add( BoardedTotal );
		hash.Add( ReleasedTotal );
		AddList( Queue );
		AddList( onRide );
		AddList( WalkingOffGuests().ToList() );
		if ( WalkOffMilliseconds > 0 || walkTable.Any( entry => entry != null ) )
		{
			if ( WalkOffMilliseconds > 0 )
				hash.Add( WalkOffMilliseconds );
			// a ride with walkers hashes the whole table; a ride without any (and without a default walk) hashes as before
			foreach ( var entry in walkTable.Where( entry => entry != null && entry.State >= 3 ) )
				hash.Add( entry!.Ends );
			foreach ( var entry in walkTable.Where( entry => entry != null && entry.State < 3 ).OrderBy( entry => entry!.Guest ) )
			{
				hash.Add( entry!.Guest );
				hash.Add( entry.From );
				hash.Add( entry.To );
				hash.Add( entry.Mode );
			}
			if ( walkTable.Any( entry => entry != null ) )
				foreach ( var (entry, slot) in walkTable.Select( ( entry, slot ) => (entry, slot) ).Where( pair => pair.entry != null ) )
				{
					hash.Add( slot );
					hash.Add( entry!.State );
					hash.Add( entry.Began );
					hash.Add( entry.Ends );
					hash.Add( entry.Via );
					hash.Add( entry.Target );
				}
		}
		hash.Add( bouncing.Count );
		foreach ( var (guest, until, start) in bouncing )
		{
			hash.Add( guest );
			hash.Add( until );
			hash.Add( start );
		}
		hash.Add( letMeOnWritten );
		hash.Add( lastAdmissionTurn );
		hash.Add( stateWord.IsBroken );
		if ( stateWord.Value == RideObjectStates.Condemned )
			hash.Add( true );   // state 4 only, so a ride in state 1 or 0 hashes as before
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
	/// script-held guests with their timers, the computed queue geometry, the admission counters and the entrance and exit
	/// cells as they stand (a placement resolved them once; <see cref="RestoreVisitorCells"/>). The script, the open
	/// delegate and the placement's other cells metadata (stand position, queue parameters) are owned by the object/script
	/// slices and are re-attached around a restore, not captured.
	/// </summary>
	public RideVisitorBridgeSnapshot CaptureSnapshot() => new()
	{
		BounceNodeBase = bounceNodeBase != DefaultBounceNodeBase ? bounceNodeBase : null,
		BounceNodes = BounceNodesDeviate() ? Enumerable.Range( 0, bouncing.Count ).Select( BounceNodeOf ).ToArray() : null,
		BounceBaseHeight = bounceBaseHeight != 0 ? bounceBaseHeight : null,
		AttractionId = AttractionId,
		Queue = Queue.ToArray(),
		OnRide = onRide.ToArray(),
		WalkingOff = WalkingOffGuests().ToArray(),
		WalkingOffReady = WalkOffMilliseconds > 0 || walkTable.Any( entry => entry is { State: >= 3 } ) ? walkTable.Where( entry => entry != null && entry.State >= 3 ).Select( entry => entry!.Ends ).ToArray() : null,
		WalkSlots = walkTable.Any( entry => entry is { State: < 3 } ) ? walkTable.Where( entry => entry is { State: < 3 } ).OrderBy( entry => entry!.Guest ).Select( entry => (entry!.Guest, entry.From, entry.To, entry.Mode) ).ToArray() : null,
		WalkTable = walkTable.Any( entry => entry != null ) ? walkTable.Select( ( entry, slot ) => (entry, slot) ).Where( pair => pair.entry != null )
			.Select( pair => new WalkEntrySnapshot( pair.slot, pair.entry!.Guest, pair.entry.Via, pair.entry.Target, pair.entry.From, pair.entry.To, pair.entry.Mode, pair.entry.Extra, pair.entry.State, pair.entry.Began, pair.entry.Ends ) ).ToArray() : null,
		VmOwnsHeapAndLimbo = true,
		Hopped = Array.Empty<int>(),
		Limbo = Array.Empty<(int Guest, double Until)>(),
		Bounce = bouncing.ToArray(),
		CalledGuest = called,
		LetMeOnWritten = letMeOnWritten,
		LastAdmissionTurn = lastAdmissionTurn,
		BrokenDown = stateWord.IsBroken,
		Condemned = stateWord.Value == RideObjectStates.Condemned ? true : null,
		CalledTurn = calledTurn,
		QueueCells = queueCells.ToArray(),
		TypedQueueFront = QueueEntryResolver != null && QueueEntranceDirection >= 0 ? QueueFrontCell : null,
		TypedQueueDirection = QueueEntryResolver != null && QueueFrontCell != null && QueueEntranceDirection >= 0 ? QueueEntranceDirection : null,
		QueueEditCount = QueueEditCount,
		JoinCell = JoinCell,
		EntranceCell = HasCells ? EntranceCell : null,
		ExitCell = HasCells ? ExitCell : null,
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
		if ( snapshot.TypedQueueFront.HasValue != snapshot.TypedQueueDirection.HasValue )
			Error( "the typed queue front and direction must be present together." );
		if ( snapshot.EntranceCell.HasValue != snapshot.ExitCell.HasValue )
			Error( "the entrance and exit cells must be present together." );
		if ( snapshot.EntranceCell is { } entranceCell && (entranceCell.X < 0 || entranceCell.Y < 0) || snapshot.ExitCell is { } exitCell && (exitCell.X < 0 || exitCell.Y < 0) )
			Error( "the entrance or exit cell is outside the positive cell map." );
		if ( snapshot.TypedQueueFront is { } typedFront && (typedFront.X < 0 || typedFront.Y < 0) )
			Error( "the typed queue front is outside the positive cell map." );
		if ( snapshot.TypedQueueDirection is { } typedDirection && (typedDirection < 0 || typedDirection > 3) )
			Error( "the typed queue direction is invalid." );
		if ( snapshot.Condemned == true && !snapshot.BrokenDown )
			Error( "a condemned ride (state 4) must be in the broken-down state." );
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
		if ( snapshot.WalkTable == null && snapshot.WalkingOff.Distinct().Count() != snapshot.WalkingOff.Length )
			Error( "the walking-off list repeats a guest." );
		if ( snapshot.WalkTable == null && snapshot.WalkSlots != null )
		{
			if ( snapshot.WalkSlots.Select( slot => slot.Guest ).Distinct().Count() != snapshot.WalkSlots.Length )
				Error( "the walk slots repeat a guest." );
			foreach ( var slot in snapshot.WalkSlots )
				if ( !riders.Contains( slot.Guest ) || snapshot.WalkingOff.Contains( slot.Guest ) )
					Error( $"walk slot guest {slot.Guest} is not aboard or already walking off." );
		}
		if ( snapshot.WalkTable != null )
		{
			var usedSlots = new HashSet<int>();
			foreach ( var entry in snapshot.WalkTable )
			{
				if ( entry == null )
				{
					Error( "the walk table holds a null entry." );
					continue;
				}
				if ( entry.Slot < 0 || entry.Slot >= RideVM.MaximumHeaderTableSize )
					Error( $"the walk table entry for guest {entry.Guest} sits in slot {entry.Slot}, outside the table." );
				if ( !usedSlots.Add( entry.Slot ) )
					Error( $"the walk table uses slot {entry.Slot} twice." );
				if ( !riders.Contains( entry.Guest ) )
					Error( $"walk table guest {entry.Guest} is not aboard." );
				if ( entry.State is < 1 or > 4 )
					Error( $"the walk table entry for guest {entry.Guest} has state {entry.State}." );
				if ( !double.IsFinite( entry.Began ) || !double.IsFinite( entry.Ends ) )
					Error( $"the walk table entry for guest {entry.Guest} has a time that is not finite." );
			}
		}
		if ( snapshot.WalkingOffReady != null && (snapshot.WalkingOffReady.Length != snapshot.WalkingOff.Length || snapshot.WalkingOffReady.Any( ready => !double.IsFinite( ready ) )) )
			Error( "the walking-off times do not match the walking-off list." );
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
		if ( snapshot.BounceNodes != null && snapshot.BounceNodes.Length != snapshot.Bounce.Length )
			Error( $"the bounce table has {snapshot.Bounce.Length} slots but {snapshot.BounceNodes.Length} stored nodes." );
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
		RestoreWalkTable( snapshot );
		bouncing.Clear();
		bouncing.AddRange( snapshot.Bounce );
		bounceNodeBase = snapshot.BounceNodeBase ?? DefaultBounceNodeBase;
		bounceBaseHeight = snapshot.BounceBaseHeight ?? 0;
		// a save written before the nodes were kept per slot derives each one from the base, which is what the original stored unless a script changed the base while riders bounced
		bounceNodes.Clear();
		bounceNodes.AddRange( snapshot.BounceNodes ?? Enumerable.Range( 0, snapshot.Bounce.Length ).Select( index => bounceNodeBase + index ) );
		called = snapshot.CalledGuest;
		letMeOnWritten = snapshot.LetMeOnWritten;
		lastAdmissionTurn = snapshot.LastAdmissionTurn;
		// an older save holds no state for a ride that was broken down (the ride update reads VAR_BROKEN again), and the economy's own save may hold it already: a clear is never written over it
		// a snapshot still "broken" after a repair reopened the ride is stale: every opener writes SetState(0) with the open word, so an open ride (the open word set and VAR_RIDECLOSED 0) has left the state
		if ( snapshot.BrokenDown && (!isOpen() || vm == null || Get( RideVariables.VAR_RIDECLOSED ) != 0) )
			stateWord.Restore( snapshot.Condemned == true ? RideObjectStates.Condemned : RideObjectStates.BrokenDown );
		calledTurn = snapshot.CalledTurn;
		queueCells.Clear();
		queueCells.AddRange( snapshot.QueueCells );
		if ( snapshot.TypedQueueFront is { } savedFront )
		{
			QueueFrontCell = savedFront;
			QueueEntranceDirection = snapshot.TypedQueueDirection!.Value;
		}
		else if ( QueueEntryResolver != null )
		{
			// Legacy snapshots never stored these caches: retain the old shape-registration reconstruction,
			// not a guessed historical direction from newly edited links. The next normal grid sync may refresh.
			QueueFrontCell = compatibilityQueueFront;
			QueueEntranceDirection = compatibilityQueueDirection;
		}
		QueueEditCount = snapshot.QueueEditCount;
		JoinCell = snapshot.JoinCell;
		RestoreVisitorCells( snapshot );
		HeadNotReadyStreak = snapshot.HeadNotReadyStreak;
		MaximumHeadNotReadyStreak = snapshot.MaximumHeadNotReadyStreak;
		CalledAgeTurns = snapshot.CalledAgeTurns;
		MaximumCalledAgeTurns = snapshot.MaximumCalledAgeTurns;
		LastAdmissionCheck = snapshot.LastAdmissionCheck;
		BoardedTotal = snapshot.BoardedTotal;
		ReleasedTotal = snapshot.ReleasedTotal;
		legacyLeftovers.Clear();
		if ( !snapshot.VmOwnsHeapAndLimbo )
			MigrateLegacyScriptState( snapshot );
	}

	/// <summary>
	/// Puts back the entrance and exit cells <paramref name="snapshot"/> holds, as they stood rather than as the registration resolved them on the restored grid (a path laid nearer since the
	/// placement resolved them did not move them); false when it holds none (a save from before) or this bridge has no cells. The resume does it before it compares the queues found from them
	/// (<see cref="Level.RestoreResumeRoutingGeometry"/>); <see cref="RestoreSnapshot"/> does it again for a caller that restores a bridge on its own.
	/// </summary>
	internal bool RestoreVisitorCells( RideVisitorBridgeSnapshot snapshot )
	{
		if ( !HasCells || snapshot.EntranceCell is not { } entrance || snapshot.ExitCell is not { } exit )
			return false;
		EntranceCell = entrance;
		ExitCell = exit;
		return true;
	}

	private readonly List<int> legacyLeftovers = new();

	/// <summary>
	/// A save written before HUSH/HOP and the limbo table moved into the script VM keeps them here: the riders HOP had not returned yet (boarded order, not in the hopped set) and the limbo list with
	/// its script-clock deadlines. The restored script has neither, so they are moved over: the heap takes the riders so that HOP returns them in the order the old HOP did (first boarded on top), the
	/// limbo table takes the limbo list with its deadlines on the scheduler clock. Riders held by the bounce table or the limbo table are not seeded. A rider that finds no room in the heap of a script
	/// that issues HOP is released at the first host step (its VAR_ONRIDE share is given back), so nobody stays aboard unreachable.
	/// </summary>
	private void MigrateLegacyScriptState( RideVisitorBridgeSnapshot snapshot )
	{
		if ( vm == null )
			return;
		var limboEntries = snapshot.Limbo.Take( vm.LimboCapacity )
			.Select( entry => (Value: entry.Guest, Expiry: unchecked( (int)(uint)(ulong)Math.Max( 0, vm.ClockOriginMilliseconds + entry.Until ) )) ).ToArray();
		vm.SeedLegacyLimbo( limboEntries );
		var held = limboEntries.Select( entry => entry.Value ).Concat( snapshot.Bounce.Where( slot => slot.Guest != 0 ).Select( slot => slot.Guest ) ).ToHashSet();
		var hopped = snapshot.Hopped.ToHashSet();
		var desired = onRide.Where( guest => !hopped.Contains( guest ) && !held.Contains( guest ) && !WalkingOffGuests().Contains( guest ) ).ToList();
		var seeded = vm.SeedLegacyHeap( desired );
		if ( seeded < desired.Count && vm.Script.Instructions.Any( instruction => instruction.Opcode == (ushort)Opcode.HOP ) )
			legacyLeftovers.AddRange( desired.Skip( seeded ) );
	}

	private void ReleaseLegacyLeftovers()
	{
		if ( legacyLeftovers.Count == 0 )
			return;
		var guests = legacyLeftovers.ToArray();
		legacyLeftovers.Clear();
		foreach ( var guest in guests )
		{
			if ( !onRide.Contains( guest ) )
				continue;
			Release( guest );
			var onRideVariable = vm?.GetVariableIndex( nameof( RideVariables.VAR_ONRIDE ) ) ?? -1;
			if ( onRideVariable >= 0 && vm!.Variables[onRideVariable] > 0 )
				vm.Variables[onRideVariable]--;
		}
	}

	// ---- Queue geometry ----------------------------------------------------------------------

	/// <summary>
	/// The ride's owner word (the cell word of its anchor, <see cref="ParkCellMap.CellWord"/>): what the queue tool writes as the owner of the queue cells it lays and of the path
	/// cell a line ends on. 0 for a bridge without a placed object (the gates, the prototype). Not saved: the level sets it when it registers the ride.
	/// </summary>
	// [BIN:TPW-EXE:0x005340E9 owner pass] the line's pass 0x83 stores the tool's ride (0x818698, the cell word of the ride's anchor) as the owner word +0x10 of each cell of the line, queue cells and the path cell it ends on; Easymode's Belly Bounce queue cells and the path cell its queue ends on all carry its owner word
	public ushort OwnerWord { get; set; }

	/// <summary>The entrance's outside cell (the original front cell), or null to use <see cref="EntranceCell"/>.</summary>
	public (int X, int Y)? QueueFrontCell { get; set; }
	/// <summary>Supported live typed entry reconstruction; rebound by the level, never persisted as a delegate. Untyped worlds retain their compatibility front.</summary>
	private Func<(int X, int Y)?>? queueEntryResolver;
	private (int X, int Y)? compatibilityQueueFront;
	private int compatibilityQueueDirection = -1;
	internal Func<(int X, int Y)?>? QueueEntryResolver
	{
		get => queueEntryResolver;
		set
		{
			compatibilityQueueFront = QueueFrontCell;
			compatibilityQueueDirection = QueueEntranceDirection;
			queueEntryResolver = value;
		}
	}

	/// <summary>Reads the native front from the entry's links, not its queue direction. Zero/unavailable entries retain the explicitly unsupported compatibility fallback.</summary>
	// [BIN:TPW-EXE:0x004DE040] mEntryPos +0x36, Links +0x0C, priority 1,16,64,4; no neighbour type, owner or reciprocity filter.
	internal bool RefreshTypedQueueFront( GuestPathGrid grid )
	{
		if ( !ReadNativeFront( grid, out var native, out var direction ) || native is not { } front ) return false;
		QueueFrontCell = front;
		QueueEntranceDirection = (direction + 2) & 3;
		return true;
	}

	/// <summary>
	/// The front cell tp.exe's selector <c>0x4DE040</c> reads from the live entry: false while the entry cannot be reconstructed (an untyped world, a neighbour off the map); true with a null
	/// <paramref name="front"/> when the entry's link byte has no cardinal bit, for which the selector writes 0 and the back-of-queue walk <c>0x4DE130</c> keeps the count 0.
	/// </summary>
	// [BIN:TPW-EXE:0x004DE040 zero front] none of the bits 1, 16, 64 and 4 set writes the cell word 0 (0x4DE0F9): the ride has no front, 0x4DE130 returns 0 with +0x40 = 0, 0x4DE4A0 reports a queue ride unconnected (a back of 0 fails its first test) and the demolition 0x527EE0 finds no count to undo
	internal bool ReadNativeFront( GuestPathGrid grid, out (int X, int Y)? front, out int direction )
	{
		front = null;
		direction = -1;
		if ( QueueEntryResolver?.Invoke() is not { } entry || !grid.InBounds( entry.X, entry.Y ) ) return false;
		var links = grid.Cells.LinksAt( entry.X, entry.Y );
		foreach ( var candidate in new[] { 0, 2, 3, 1 } )
		{
			if ( (links & GuestPathGrid.LinkValue( candidate )) == 0 ) continue;
			var (dx, dy) = GuestPathGrid.Directions[candidate];
			var neighbour = (X: entry.X + dx, Y: entry.Y + dy);
			// Out-of-map neighbours remain outside the supported host reconstruction, not a native type test.
			if ( !grid.InBounds( neighbour.X, neighbour.Y ) ) return false;
			front = neighbour;
			direction = candidate;
			return true;
		}
		return true;
	}

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

	/// <summary>
	/// Binds the bridge to the script it serves; <paramref name="open"/> reports whether the owner opened the ride (the open word +0x68),
	/// <paramref name="takes"/> whether its coaster or track takes guests for the choice (null: always), <paramref name="refuses"/> whether the
	/// queue's own test sends queued guests away (null: when <paramref name="takes"/> fails).
	/// </summary>
	public void Attach( RideVM script, Func<bool> open, Func<bool>? takes = null, Func<bool>? refuses = null )
	{
		vm = script;
		isOpen = open;
		takesGuests = takes ?? (() => true);
		refusesQueue = refuses ?? (() => !takesGuests());
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
		var typedFront = RefreshTypedQueueFront( grid );
		var front = QueueFrontCell ?? EntranceCell;
		// A front cell that is neither a queue nor a path cell falls back to EntranceCell, the nearest walkable path cell (a cell nothing laid: restores, the gates; an import takes the save's queue cells), as a one-cell queue.
		if ( !typedFront && !grid.IsQueue( front.X, front.Y ) && !grid.IsWalkable( front.X, front.Y ) )
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

	/// <summary>
	/// The queue as <see cref="RecomputeQueue"/> would find it now, on an isolated copy of the queue's geometry: this bridge's own cells, front, join cell and edit count stay as
	/// they are. [EXT:online-coop-lockstep] The queue tool's entry reads it (<see cref="Level.EnterQueueTool"/>): a tool is one player's alone, so entering it changes nothing
	/// in the park, and the queue commands bring the ride's own queue up to date on every peer.
	/// </summary>
	internal RideVisitorBridge QueueView( GuestPathGrid grid )
	{
		// Like the snapshot check's copy: it never registers, writes the map or runs a script.
		var view = new RideVisitorBridge( AttractionId, Name, Kind, Capacity, excitementLevel, AttractionValue )
		{
			EntranceCell = EntranceCell, ExitCell = ExitCell, OwnerWord = OwnerWord,
			QueueFrontCell = QueueFrontCell, QueueEntranceDirection = QueueEntranceDirection
		};
		view.queueEntryResolver = queueEntryResolver;
		view.RecomputeQueue( grid );
		return view;
	}

	/// <summary>Validates current-epoch save geometry without mutating live caches. Legacy snapshots use the pre-feature shape registration contract.</summary>
	internal bool MatchesSnapshotQueueGeometry( GuestPathGrid grid, RideVisitorBridgeSnapshot saved )
	{
		var expected = this;
		if ( saved.TypedQueueFront == null && QueueEntryResolver != null )
		{
			// Recompute only the old geometry, not guest/VM state. This isolated bridge never registers or writes the map.
			expected = new RideVisitorBridge( AttractionId, Name, Kind, Capacity, excitementLevel, AttractionValue )
			{
				EntranceCell = EntranceCell, QueueFrontCell = compatibilityQueueFront,
				QueueEntranceDirection = compatibilityQueueDirection
			};
			expected.RecomputeQueue( grid );
		}
		return saved.QueueCells.SequenceEqual( expected.QueueCells ) && saved.JoinCell == expected.JoinCell
			&& (saved.TypedQueueFront == null || (saved.TypedQueueFront == expected.QueueFrontCell
				&& saved.TypedQueueDirection == expected.QueueEntranceDirection));
	}

	/// <summary>Raised after an edit of this ride's queue cells (tp.exe's re-evaluation 0x004DE1F0), once the queue is recomputed.</summary>
	public event Action? QueueEdited;

	/// <summary>
	/// Reports a queue edit to the level (<see cref="QueueEdited"/>). With the <paramref name="grid"/> the re-evaluation's own stamp is made first:
	/// the queue's back cell gets a new block stamp, so a guest whose walk crosses its block searches again.
	/// </summary>
	// [BIN:TPW-EXE:0x004DE1F0 queue re-evaluation stamp] after the back cell is recomputed (0x4DE130) the counter 0x7CDB98 is raised by one (0x4D8C50, 0x4DE233) and written into the 16 x 16 block of that back cell (0x4D8C60, 0x4DE266) before the open test; the callers are the queue tool's cell click and the cell setter and clear (0x4DE1F0 callers)
	internal void NotifyQueueEdited( GuestPathGrid? grid = null, bool stampBack = true )
	{
		// [BIN:TPW-EXE:0x004D8C60 no block for cell 0] a back cell of 0 (a zero front: 0x4DE040 found no cardinal link bit, 0x4DE130 returns 0) raises the counter (0x4D8C50) but stamps no block (0x4D8C60 tests the cell word first)
		if ( grid != null )
		{
			if ( stampBack && !(ReadNativeFront( grid, out var native, out _ ) && native == null) )
				grid.Cells.StampBlockAt( QueueBackCell.X, QueueBackCell.Y );
			else
				grid.Cells.StampBlockAt( -1, -1 );
		}
		QueueEdited?.Invoke();
	}

	/// <summary>The queue cell behind (x, y): a neighbour of type 3 whose link points back at (x, y) (<c>0xdda18</c>).</summary>
	// [BIN:TPW-EXE:0x004DE670 queue predecessor] the neighbours are tried −Y (its link 16), +Y (link 1), +X (link 64), −X (link 4), through the run-time table 0x007CDBA0 (entries 0, 1, 5, 6); the first queue cell (type 3, not 9) whose link points back is taken
	private static readonly int[] PredecessorOrder = { 0, 2, 1, 3 };   // Directions indices: −Y, +Y, +X, −X

	public static (int X, int Y)? NextQueueCell( GuestPathGrid grid, (int X, int Y) cell )
	{
		foreach ( var direction in PredecessorOrder )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			var (nx, ny) = (cell.X + dx, cell.Y + dy);
			if ( grid.IsQueue( nx, ny ) && GuestPathGrid.LinkDirection( grid.GetQueueLink( nx, ny ) ) == ((direction + 2) & 3) )
				return (nx, ny);
		}
		return null;
	}

	/// <summary>
	/// The path cell attached to the back of the queue (<c>0x4DE840</c>'s cell): the first cardinal neighbour, in the order −Y, +X, +Y, −X,
	/// whose bit is set in the back cell's link byte and which is a path cell; the back cell itself when the queue is the one-cell fallback
	/// on a path; null when the queue is not connected to a path.
	/// </summary>
	// [BIN:TPW-EXE:0x004DE840 attached path] 0x4DEA2F..0x4DEA57: for the bits 1, 4, 16, 64 of the back cell's +0x0C (0x522770) the neighbour along it (0x4D97E0) is taken when it is type 1 (0x536310); none logs "No path attached to end of queue"
	// [APPROX:QUEUE-003] back-cell admission and fourth-slot path geometry are implemented; supported live typed HasQueue entries derive their front from the actual entry link byte (+0x0C, tp.exe 0x4DE040, bits 1, 16, 64, 4), and a native zero front (no cardinal bit; 0x4DE4A0 reads such a ride as unconnected, which the opening guard still answers permissively under ECON-049) leaves its demolition without a count to undo (0x527FB4) and its re-evaluation without a block stamp; the guests' joining and scoring of a zero-front ride, linkless or unsupported entries and untyped worlds retain the host positive one-cell fallback; divergent imported mEntryPos and the native cache invalidation timing (0x4DE130 keeps +0x3A until the next 0x4DE1F0, where OpenTPW recomputes on every grid change) remain unsupported
	private (int X, int Y)? FindJoinCell( GuestPathGrid grid )
	{
		var back = queueCells[^1];
		if ( grid.Cells.PoolBridges?.Covers( back.X, back.Y ) == true )
			return null;
		if ( grid.IsWalkable( back.X, back.Y ) )
			return back;
		var links = grid.Cells.LinksAt( back.X, back.Y );
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			if ( (links & GuestPathGrid.LinkValue( direction )) != 0 && grid.IsWalkable( back.X + dx, back.Y + dy )
				&& grid.Cells.PoolBridges?.Covers( back.X + dx, back.Y + dy ) != true )
				return (back.X + dx, back.Y + dy);
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
		// [BIN:TPW-EXE:0x004FFBC0 state-10 join] the join reads neither the ride's state (+0x19C) nor its open word (+0x68): after the walk step 0x004FA2A0 it tests the guest's cell against the back cell (0x004DE110), the room 0x004DDA20, the excitement gate 0x004FD4E0 / 0x004E0860(1) and the limit 0x004DDB60, then appends (0x004DDB90); a guest joining a closed or broken ride is sent away when it reaches the head (HostStep: 0x004E1220 → 0x004E0450, states 1, 2 and 4 through 0x004E0E00). Only the choice's counts-test 0x004DD920 refuses them. A ride whose script does not run takes nobody (OpenTPW's teardown), nor does a coaster or track that refuses guests (COAST-024)
		if ( CheckJoinRoom( guestId ) is var room && room != QueueJoinResult.Joined )
			return room;
		var count = links.Count;
		// [BIN:STP-PPC:0x100ECD6C join gate] 0xe9a84 then 0xe02ec(ride, 1) and the 45 test; executed in the oracle (ORACLE.md F16, queue-join-gate)
		// [BIN:TPW-EXE:0x004E0560 live excitement] a ride with a controller behind it (thing +0x28) has the base level × 60 / 100 + clamp(3 × crossings + longest straight run + 2 × corners, 0, 40) from the section counts 0x00545310 (BumpController.ExcitementBase); water rides have one
		// [BIN:TPW-EXE:0x00529E10 controller types] the placement builds a controller through 0x00545890 for any nonzero ride type word +0xA0 (Bumper.BumperType); the shipped rides of all four themes (bonus entries included) use only −1 and −4..−14, each with the WhichTrackType of its family (0 bumper cars, 1 karts, 2 water), so BumperTemplates.ForRide covers every one (BumperTypeCoverageTests); the loader of a saved ride (0x00543560) restores the controller from the save
		if ( preferredExcitement is { } preferred && ExcitementRefusal( preferred ) is { } refusal )
			return refusal;
		if ( count >= QueueLimit )
			return QueueJoinResult.QueueTooLong;
		AppendToQueue( guestId );
		return QueueJoinResult.Joined;
	}

	/// <summary>The attraction's part of the joining checks, in the original order: open, not listed, room (see <see cref="IRideVisitorBridge.CheckJoinRoom"/>).</summary>
	public QueueJoinResult CheckJoinRoom( int guestId )
	{
		if ( guestId <= 0 || !AcceptsJoins )
			return QueueJoinResult.Closed;
		if ( links.ContainsKey( guestId ) || Holds( guestId ) )
			return QueueJoinResult.AlreadyQueued;
		// [BIN:STP-PPC:0x100DC988 queue room] a guest may join only while count < 4 × queue size in cells (+60)
		if ( links.Count >= QueueRoom )
			return QueueJoinResult.NoRoom;
		return QueueJoinResult.Joined;
	}

	/// <summary>Appends a guest the checks let through at the tail of the queue.</summary>
	public void AppendToQueue( int guestId )
	{
		// [BIN:STP-PPC:0x100DCD34 queue append] the guest is appended at the tail of the ride's linked list
		links[guestId] = (0, tail);
		if ( tail != 0 )
			links[tail] = (guestId, links[tail].Previous);
		else
			head = guestId;
		tail = guestId;
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
			// [EXT:synthetic-ride-zero-guard] a bridge without any operating values (speed, design speed, duration and design duration all 0) is a synthetic object, since every original object has InitSpeed and InitDuration; it is read at its design speed and duration, where tp.exe's 0x004E0560 divides with no guard and would score trunc(0.5625 × level) from the 0.75 floor on a NaN ratio (0x004E07D2, 0x004E0802); no original object reaches that
			var level = ExcitementBase?.Invoke( excitementLevel ) ?? excitementLevel;
			if ( Parameters is { Speed: 0, InitSpeed: 0, Duration: 0, InitDuration: null or 0 } )
				return level;
			return QueueJoinRules.ScaledExcitement( level, Parameters.Speed, Parameters.InitSpeed, Parameters.Duration, Parameters.InitDuration ?? Parameters.Duration );
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

	/// <summary>
	/// The close (tp.exe <c>0x004DF300</c>, and <c>SetState</c> 1, 2 and 4): the loading slot <c>+0x6C</c> is cleared at once, so a guest that was called but has not yet written
	/// its offer is no longer the pending visitor (its stand-point admission then refuses it while the ride is closed, or takes it once the ride has opened again). An offer
	/// already written stays: tp.exe's AdmitPerson cleared the slot when it wrote VAR_LETMEON, and the close leaves VAR_LETMEON alone.
	/// </summary>
	// [BIN:TPW-EXE:0x004DF300 close clears the loading slot] "Object %d: Closing...": the open word +0x68 = 0, "person being loaded was %d (about to zeroise)", +0x6C = 0, script variable 6 (VAR_RIDECLOSED) = 1 and the model state call 0x00454550(1); no guest, flag or queue word is touched. Its callers are the park door 0x00519EF0, the coaster tool's entry 0x00447520 and the ride window's close 0x0048CCF0; SetState 1, 2 and 4 (0x004E0E92, 0x004E0FA0, 0x004E10A4) and the repair tail's re-close (0x004DFD3E) clear +0x6C the same way
	public void ClearLoadingSlot()
	{
		if ( !letMeOnWritten )
			called = 0;
	}

	private void Withdraw()
	{
		if ( letMeOnWritten && Get( RideVariables.VAR_LETMEON ) == called )
			Set( RideVariables.VAR_LETMEON, 0 );
		called = 0;
		letMeOnWritten = false;
	}

	/// <summary>
	/// The called guest reached the stand point (state 13, admission method AdmitPerson: Mac <c>0xe03d0</c>, PC <c>0x004E0900</c>): its id is written
	/// to VAR_LETMEON only while that is 0 and the ride is open and outside its retained broken state. True when written (the guest then waits for the script);
	/// false refuses the guest, which then walks back to its queue place (<see cref="GuestSimulation"/>'s state 13).
	/// </summary>
	public bool PresentForBoarding( int guestId )
	{
		if ( guestId == 0 || vm == null )
			return false;
		// AdmitPerson admits whoever presents itself (it only logs "admitting wrong person" when +0x6C names someone else); OpenTPW calls only the queue head and only a
		// standing head, so the presenting guest is the pending visitor or, once a close (ClearLoadingSlot) or a busy VAR_LETMEON has cleared it, nobody: any other id is refused
		if ( called != 0 && guestId != called )
			return false;
		// Recheck the owner's open word and retained state at the stand point; the choice's coaster/track predicate does not apply here.
		// [BIN:TPW-EXE:0x004E0900 AdmitPerson state/open test] AdmitPerson refuses a ride in state 1 (+0x19C), with the open word +0x68 = 0 or in state 4 before the loading-slot and VAR_LETMEON checks: it logs and returns 0 without touching the ride (0x004E0A30), so the pending visitor and VAR_LETMEON stay as they are
		// IsBrokenDown is tp.exe's +0x19C test (states 1 and 4): every opener writes the open word 1 and VAR_RIDECLOSED 0 together with SetState(0)
		// (the repair 0x004DF8F0, the open routine 0x004DF390, the queue edit's reopening 0x004DE1F0, the upgrade cancel 0x004E0050), which OriginalObjectRuntime.Open does with the word
		if ( !isOpen() || IsBrokenDown )
			return false;
		if ( letMeOnWritten )
			return true;
		// [BIN:TPW-EXE:0x004E09B0 AdmitPerson clears the loading slot] past the state/open test AdmitPerson logs a loading slot +0x6C that names someone else ("admitting wrong person", 0x004E0976: none after a close cleared it)
		// and clears it before it reads VAR_LETMEON; a busy VAR_LETMEON then logs "script changed its mind about admission!" and returns 0 (0x004E09EC), leaving the variable alone, so the ride calls its head again at a later update
		called = 0;
		// [BIN:TPW-EXE:0x551724 critical flag] the original's host side never looks at the critical flag (its only reader is the slice loop); the admission method writes VAR_LETMEON whenever it is 0, whatever the script is doing
		if ( Get( RideVariables.VAR_LETMEON ) != 0 )
			return false;
		// The written guest is kept as the bridge's pending visitor: OpenTPW's state 14 (HostStep, MarkBoarded) follows it there, where tp.exe's guest watches VAR_LETMEON itself
		called = guestId;
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
		ReleaseLegacyLeftovers();
		UpdateWalkers();

		// State 14 (0xe05c8): the guest stays in the list until the script has consumed VAR_LETMEON. Boarding comes first: a script may take a guest and hand it to VAR_LETMEOFF within one slice.
		if ( called != 0 && letMeOnWritten && Get( RideVariables.VAR_LETMEON ) != called )
			MarkBoarded( called );

		// [BIN:TPW-EXE:0x004E1410 script dismiss] VAR_LETMEOFF is read by the ride update of a park turn only (UpdateAdmission), not on every tick

		if ( DefersAdmission )
			return;
		UpdateAdmission();
	}

	/// <summary>
	/// True while the host runs the ride update of a park turn from the thing sweep (<see cref="UpdateAdmission"/>): <see cref="HostStep"/> then does the per-tick protocol only. Not saved;
	/// the host sets it for the ticks that start a swept turn.
	/// </summary>
	public bool DefersAdmission { get; set; }

	/// <summary>
	/// The ride update of a park turn (tp.exe 0x004E0E00, called from the thing sweep after the object's wear and breakdown update 0x004E0B90): once per turn it dismisses the head of a
	/// closed or broken ride's queue and runs the admission. <see cref="HostStep"/> calls it itself unless <see cref="DefersAdmission"/>.
	/// </summary>
	public void UpdateAdmission()
	{
		if ( vm == null )
			return;
		if ( vm.State != RideVMState.Running && vm.State != RideVMState.Waiting )
		{
			ReleaseAll();
			return;
		}
		// [BIN:TPW-EXE:0x00516380 park turn walk] the park update runs once per park turn (the main loop calls it when its 31 ms substep counter & 7 == 0, 0x0054F668) and dispatches every live-list thing whose pointer is set and whose byte +3 is 0 (0x005163F0..0x005163F7, no type, state or id test; the whole walk is skipped while world mode +0x1DA738 is 4, the turn still counting, 0x00516395, DET-plan §2.1) to 0x0050B360: a ride (type 3) runs 0x004E0B90, then the state dispatch 0x004E0E00, whose state 0 runs the admission 0x004E1220 once (0x004E14E0); a guest (type 1) runs 0x00501650, then 0x005019F0, whose state 11 is 0x004FFFF0; OpenTPW's lastAdmissionTurn and LastQueueUpdateTurn guards give the same once-per-turn cadence
		var turn = Host?.ParkTurn ?? -1;
		if ( turn >= 0 && turn == lastAdmissionTurn )
			return;
		lastAdmissionTurn = turn;
		// [BIN:STP-PPC:0x100E0AEC ride update, states 1, 2 and 4] while the ride is broken (state 1; 4 when condemned) every update removes the head guest from the queue and sends it away (0xdcebc, 0xee804) unless the script has already taken it; the rest wait their turn, so a queue drains one guest per park turn. Static reading (ORACLE.md F15): the dispatch is exercised in the oracle target queue-ride-update
		// [BIN:STP-PPC:0x100E1864 breakdown noticed] in state 0 the ride update runs the admission (0xe1404) first and only then reads VAR_BROKEN and enters state 1 (4 when the life gauge is 0): SetState(1) runs the head step once, zeroes the pending visitor and writes VAR_RIDECLOSED = 1 with the open word 0 (the oracle target ride-set-state executes it, ORACLE.md F27)
		// [BIN:TPW-EXE:0x4e0e00 ride state dispatch] state 0 runs 0x4e14e0 (VAR_BROKEN, script slot 7: SetState(1), or 4 when the life gauge +0x48 truncates to 0); states 1, 2 and 4 run the head step 0x4e0450 and the script dismiss 0x4e1410; nothing in states 1, 2 or 4 reads VAR_BROKEN
		// [BIN:TPW-EXE:0x004E0E60 SetState 1, 2 and 4] states 1, 2 and 4 each run the head step 0x004E0450, write the open word +0x68 = 0 and VAR_RIDECLOSED = 1 (script slot 6), and are left only through SetState(0) (the repair 0x004DF8F0 opens the ride first; the reopen guard 0x004DF290 refuses 1, 2 and 4); state 2 (upgrading) is the economy's closed ride (ECON-023); the bridge keeps state 1 and state 4 apart by the life gauge (condemned), because the guest's state 11 holds its move-up only in state 1 (0x004E0370 at 0x00500523)
		if ( stateWord.IsBroken )
		{
			DismissHead();
			DismissLetOff();
			return;
		}
		// [BIN:TPW-EXE:0x004E1220 closed ride admission] state 0 runs the admission first (0x004E14E0); with the open word +0x68 at 0 it only runs the head step 0x004E0450, which unlinks the queue head (0x004DDD20) and dismisses it (0x005012F0), so a closed ride sends away one head per park turn and the others wait (a head the script has already taken was boarded above: 0x004E0450's "forcing him onto ride" case, 0x00500870)
		if ( !isOpen() )
			DismissHead();
		if ( IsBroken )
		{
			// State 0 reads VAR_BROKEN whether or not the ride is open (0x4e0e00 → 0x4e14e0), so a closed ride enters state 1 too, after the admission's own
			// head step: a closed ride dismisses two heads on that turn. A park save keeps no bridge state: a ride still broken after a load enters state 1 again here.
			if ( isOpen() && Get( RideVariables.VAR_RIDECLOSED ) == 0 )
				CheckAdmission( turn );
			EnterBrokenState();
			return;
		}
		// [BIN:TPW-EXE:0x004E1220 admission ignores the coaster predicate] the admission reads the open word +0x68, the script's LETMEON, capacity and running variables, the pending visitor and the queue head only, never the coaster test 0x00441970: an open ride whose coaster refuses guests still calls its head forward, and its queued guests leave through state 11's own exit (thought 13, 0x00500612)
		if ( isOpen() )
			CheckAdmission( turn );
		// [BIN:TPW-EXE:0x004E159A state 0 dismiss] the state-0 update runs the admission (0x004E1220), reads VAR_BROKEN and, unless that sets state 1 or 4 and returns, the dismiss 0x004E1410
		DismissLetOff();
	}

	/// <summary>
	/// The ride update's dismiss (tp.exe <c>0x004E1410</c>): the guest in VAR_LETMEOFF runs ExitRide (<see cref="IRideVisitorHost.OnVisitorExitRide"/>); when it left, VAR_LETMEOFF is set to 0 and the ride
	/// lets go of it, and when its exit is blocked the variable keeps it, the guest stays on the ride and the ride closes (<see cref="CloseForBlockedExit"/>), so the next ride update tries again.
	/// </summary>
	// [BIN:TPW-EXE:0x004E1410 script dismiss] script variable 1 (VAR_LETMEOFF, 0x0055A390) 0 does nothing; else the person's state is checked against 16 (a log, "trying to dismiss person %d, but they're not on this ride!", not a test) and ExitRide 0x005014E0 runs; a result of 1 writes 0 to the variable (0x004E14C3), 0 leaves it
	// [BIN:TPW-EXE:0x004E0E27 dismiss in states 1, 2 and 4] the ride states 1, 2 and 4 run the head step 0x004E0450, then the same dismiss
	private void DismissLetOff()
	{
		var off = Get( RideVariables.VAR_LETMEOFF );
		if ( off == 0 )
			return;
		if ( !(Host?.OnVisitorExitRide( this, off ) ?? true) )
		{
			CloseForBlockedExit();
			return;
		}
		Set( RideVariables.VAR_LETMEOFF, 0 );
		LetGo( off );
	}

	/// <summary>Ride closings because a let-off guest's exit was blocked (diagnostics, not saved).</summary>
	public int BlockedExitClosings { get; private set; }

	/// <summary>
	/// A let-off guest's exit is blocked (tp.exe <c>0x004DF150</c>): an open ride closes as the breakdown's SetState does, the pending visitor is let go and VAR_RIDECLOSED is set to 1.
	/// </summary>
	// [BIN:TPW-EXE:0x004DF150 close for a blocked exit] with the open word +0x68 set: message 0x14 on the bus 0x0040FB10 (its one direct subscriber, the tag system's handler 0x00509BF0, logs it as unknown and does nothing), "Object %d: Closing...", +0x68 = 0, the pending person +0x6C = 0 ("about to zeroise"), script variable 6 (VAR_RIDECLOSED) = 1 (0x0055A0B0) and the model call 0x00454550(1) that SetState 0x004E0E60 makes too; a closed ride does nothing
	private void CloseForBlockedExit()
	{
		if ( !isOpen() || Get( RideVariables.VAR_RIDECLOSED ) != 0 )
			return;
		if ( called != 0 )
			Withdraw();
		Set( RideVariables.VAR_RIDECLOSED, 1 );
		ModelState?.Invoke( ObjectHoardingState.CloseMode );
		BlockedExitClosings++;
	}

	/// <summary>
	/// The ride's model calls (tp.exe <c>0x00454550</c>) at the bridge's closings: the blocked exit's close and the breakdown's state change. The runtime
	/// binds its hoarding (presentation only, docs/OBJECTS.md "Hoarding"); null in a bare bridge.
	/// </summary>
	internal Action<int>? ModelState { get; set; }

	/// <summary>
	/// <c>SetState(1)</c> (<c>0xe0ec4</c>): the head-guest step runs once, the pending visitor (ride <c>+104</c>) is zeroised and the open word and
	/// VAR_RIDECLOSED are written 0 and 1. The ride then counts as closed, so the ride update dismisses one head guest per turn from here on.
	/// </summary>
	private void EnterBrokenState()
	{
		// [BIN:TPW-EXE:0x004E14E0 state 1 or 4] the ride update reads the life gauge +0x48 (fld, __ftol, test al) when it enters the state: a nonzero low byte gives state 1 ("Setting state BROKEN", SetState 0x004E0E60(1)), a zero byte state 4 ("Setting state CONDEMNED", SetState(4))
		var condemned = RideBreakdownRules.GaugeByte( (float)LifeGauge ) == 0;
		stateWord.Set( condemned ? RideObjectStates.Condemned : RideObjectStates.BrokenDown );
		// [BIN:TPW-EXE:0x004E1542 breakdown's model calls] "Setting state BROKEN" calls 0x00454550(2) and "Setting state CONDEMNED" 0x00454550(4) before SetState(1) or SetState(4), whose close calls 0x00454550(1) (0x004E0F20, 0x004E1123): the hoarding shows the broken-down or condemned set
		ModelState?.Invoke( condemned ? ObjectHoardingState.CondemnMode : ObjectHoardingState.BreakdownMode );
		DismissHead();
		if ( called != 0 )
			Withdraw();
		Set( RideVariables.VAR_RIDECLOSED, 1 );
		ModelState?.Invoke( ObjectHoardingState.CloseMode );
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
		if ( LetGo( guest ) )
			Host?.OnVisitorReleased( this, guest );
	}

	/// <summary>The ride's own bookkeeping when a guest leaves it: its head slot, walk entry and bounce slot are freed. False when the ride does not hold the guest.</summary>
	private bool LetGo( int guest )
	{
		if ( !onRide.Remove( guest ) )
			return false;
		// Host cleanup: do not leave a presentation slot on the previous ride after release.
		Heads?.Remove( guest );
		for ( var slot = 0; slot < walkTable.Count; slot++ )
			if ( walkTable[slot]?.Guest == guest )
				walkTable[slot] = null;
		for ( var index = 0; index < bouncing.Count; index++ )
			if ( bouncing[index].Guest == guest )
				FreeBounceSlot( bouncing, index );
		TrimBounceNodes();
		ReleasedTotal++;
		return true;
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
		walkTable.Clear();
	}

	/// <summary>
	/// How long a guest takes to walk off after WALKOFF before WALKGET hands it out, in script milliseconds; 0 (the default) completes at once. The tour rides set
	/// it: their unload loops count walkers and leave the loop when WALKGET returns the walker immediately (docs/TOURS.md, TOUR-007).
	/// A ride that also gets <see cref="WalkNodeDistance"/> measures its WALKOFF walks between the slot's nodes and uses this value only when a slot or node is unknown.
	/// </summary>
	public int WalkOffMilliseconds { get; set; }

	/// <summary>
	/// The model-unit distance between two nodes of the ride as WALKOFF measures it: the walk action (4 = head: the start node is of class 0x80, else 0x800), the slot's start node number and its
	/// end node number (class 0x800); null when a node is missing. Set by the ride's runtime for a tour ride; without it every WALKOFF takes <see cref="WalkOffMilliseconds"/>.
	/// </summary>
	public Func<int, int, int, float?>? WalkNodeDistance { get; set; }

	/// <summary>
	/// Called by a WALKOFF whose walk action is 2 with the slot's start node number (class 0x800): the original creates effect 0x13 at that node
	/// ([BIN:TPW-EXE:0x5572EB WALKOFF action 2]). Set by the ride's runtime for a tour ride.
	/// </summary>
	public Action<int>? WalkOffEffect { get; set; }

	/// <summary>
	/// The time a WALKOFF walk takes for a walker of the given slot: <c>ftol(distance between the slot's two nodes) x 100</c> ms, 100 ms for a distance below one model unit
	/// ([BIN:TPW-EXE:0x005571A0 WALKOFF]); <see cref="WalkOffMilliseconds"/> when the slot or a node is not known.
	/// </summary>
	internal static int WalkOffDuration( float distance ) => (int)distance * 100 is var milliseconds and not 0 ? milliseconds : 100;

	/// <summary>
	/// The distance in model units between two nodes of the ride as WALKON measures its walk-on: the first node's class mask and number, then the second's (the head action 4 takes the second from class 0x80,
	/// every other action from 0x800); null when a node is missing. Set by the ride's runtime for every object with a model; without it a walk-on takes no time.
	/// </summary>
	public Func<int, int, int, int, float?>? WalkNodeClassDistance { get; set; }

	/// <summary>One entry of the script's walk table (0x20 bytes at +0x2C of the script, state word at +0x18).</summary>
	internal sealed class WalkRecord( int guest, int via, int target, int from, int to, int mode, int extra, int state, double began, double ends )
	{
		public int Guest { get; } = guest;
		/// <summary>The walk-on's first node (operand u1, +0x00).</summary>
		public int Via { get; } = via;
		/// <summary>The walk-on's second node (operand u2, +0x02; the head id of the head action).</summary>
		public int Target { get; } = target;
		/// <summary>WALKOFF's start node (u3, +0x04).</summary>
		public int From { get; } = from;
		/// <summary>WALKOFF's end node (u4, +0x06).</summary>
		public int To { get; } = to;
		/// <summary>The walk action (+0x16).</summary>
		public int Mode { get; } = mode;
		/// <summary>The last operand (+0x1A), kept and never read by the original's walk code that was traced.</summary>
		public int Extra { get; } = extra;
		/// <summary>1 walking on, 2 on board, 3 walking off, 4 ready for WALKGET.</summary>
		public int State { get; set; } = state;
		public double Began { get; set; } = began;
		public double Ends { get; set; } = ends;
		/// <summary>The sprite heading byte of the walking body (+0x14): set at WALKON from the walk's direction, then by the walk update's drawing. Presentation only: not saved, not hashed, null until known.</summary>
		public int? Heading { get; set; }
	}

	/// <summary>One walk table entry as the walk update's drawing reads it (<see cref="CollectWalkers"/>).</summary>
	internal readonly record struct WalkerView( int Guest, int State, int Mode, int U1, int U2, int U3, int U4, int Extra, double Began, double Ends, int? Heading );

	/// <summary>
	/// The sprite heading byte WALKON stores in its entry (+0x14), from the walk-on's two nodes' class masks and numbers.
	/// Set by the ride's runtime for every object with a placed model; without it the heading stays unknown (0 when drawn).
	/// </summary>
	public Func<int, int, int, int, int?>? WalkNodeHeading { get; set; }

	/// <summary>The live entries of the walk table in table order, for the drawing of the walking bodies (the walk update 0x00557D80 draws each one it passes).</summary>
	internal void CollectWalkers( List<WalkerView> into )
	{
		foreach ( var entry in walkTable )
			if ( entry is { State: >= 1 } )
				into.Add( new WalkerView( entry.Guest, entry.State, entry.Mode, entry.Via, entry.Target, entry.From, entry.To, entry.Extra, entry.Began, entry.Ends, entry.Heading ) );
	}

	/// <summary>Stores the heading byte the walk update's drawing wrote into the rider's entry (+0x14).</summary>
	internal void SetWalkHeading( int guest, int heading )
	{
		if ( FindWalkSlot( guest ) is { } found )
			found.Entry.Heading = heading;
	}

	/// <summary>
	/// Called by a WALKON with walk action 2 when its walk-on ends, with the entry's second node (u2, class 0x800): the original creates effect 0x13 there ([BIN:TPW-EXE:0x00557EC1 WALKON end, action 2]), as WALKOFF does at its start node.
	/// Set by the ride's runtime for a tour ride.
	/// </summary>
	public Action<int>? WalkOnEffect { get; set; }

	/// <summary>
	/// The walk update of the main loop pass (tp.exe 0x00557AB0 → 0x00557D80), run by the host step: a walk-on whose end has passed moves to state 2 (a head action 4 hangs the rider's head on head id u2, 0x0044B410; action 2 starts effect 0x13 at u2)
	/// and a walk-off whose end has passed moves to state 4, the state WALKGET hands out.
	/// </summary>
	private void UpdateWalkers()
	{
		if ( vm == null )
			return;
		var now = vm.TimeMilliseconds;
		foreach ( var entry in walkTable )
		{
			if ( entry == null || entry.Ends > now )
				continue;
			if ( entry.State == 1 )
			{
				entry.State = 2;
				entry.Began = now;
				if ( entry.Mode == 4 && Heads != null && onRide.Contains( entry.Guest ) )
					Heads.Hang( entry.Target, entry.Guest );
				if ( entry.Mode == 2 )
					WalkOnEffect?.Invoke( entry.Target );
			}
			else if ( entry.State == 3 )
				entry.State = 4;
		}
	}

	private int FreeWalkSlot( int capacity )
	{
		for ( var slot = 0; slot < capacity; slot++ )
			if ( slot >= walkTable.Count || walkTable[slot] == null )
				return slot;
		return -1;
	}

	private void SetWalkSlot( int slot, WalkRecord? entry )
	{
		while ( walkTable.Count <= slot )
			walkTable.Add( null );
		walkTable[slot] = entry;
	}

	private (int Slot, WalkRecord Entry)? FindWalkSlot( int guest )
	{
		for ( var slot = 0; slot < walkTable.Count; slot++ )
			if ( walkTable[slot] is { } entry && entry.Guest == guest )
				return (slot, entry);
		return null;
	}

	/// <summary>The riders in WALKOFF's states (3 and 4) in table order.</summary>
	private IEnumerable<int> WalkingOffGuests() => walkTable.Where( entry => entry is { State: >= 3 } ).Select( entry => entry!.Guest );

	/// <summary>WALKGET: the first entry in state 4 gives its rider and frees the entry. The frame update moves state 3 to 4 once the end has passed; the bridge applies that move here, at the script's own time.</summary>
	private int TakeWalker( double now )
	{
		for ( var slot = 0; slot < walkTable.Count; slot++ )
		{
			if ( walkTable[slot] is not { State: >= 3 } entry || entry.Ends > now )
				continue;
			walkTable[slot] = null;
			return entry.Guest;
		}
		return 0;
	}

	/// <summary>Rebuilds the walk table: from the table the snapshot stores, or, for a save written before it, the riders on board (state 2) in guest order followed by the riders walking off (state 3) in their old queue order.</summary>
	private void RestoreWalkTable( RideVisitorBridgeSnapshot snapshot )
	{
		walkTable.Clear();
		if ( snapshot.WalkTable != null )
		{
			foreach ( var entry in snapshot.WalkTable.OrderBy( entry => entry.Slot ) )
				SetWalkSlot( entry.Slot, new WalkRecord( entry.Guest, entry.Via, entry.Target, entry.From, entry.To, entry.Mode, entry.Extra, entry.State, entry.Began, entry.Ends ) );
			return;
		}
		var slot = 0;
		foreach ( var (guest, from, to, mode) in (snapshot.WalkSlots ?? Array.Empty<(int Guest, int From, int To, int Mode)>()).OrderBy( entry => entry.Guest ) )
			SetWalkSlot( slot++, new WalkRecord( guest, 0, 0, from, to, mode, 0, 2, 0, 0 ) );
		for ( var index = 0; index < snapshot.WalkingOff.Length; index++ )
			SetWalkSlot( slot++, new WalkRecord( snapshot.WalkingOff[index], 0, 0, 0, 0, 0, 0, 3, 0, snapshot.WalkingOffReady?[index] ?? 0 ) );
	}

	/// <summary>The node base BOUNCESETNODE set (script +0x70, 1 until then): slot <c>n</c> of the bounce table bounces its rider on the node numbered base + n.</summary>
	public int BounceNodeBase => bounceNodeBase;

	/// <summary>The base height BOUNCESETBASE set (script +0x6E, 0 until then), in the original's model units, added to the ride's height under the bouncing riders.</summary>
	public short BounceBaseHeight => bounceBaseHeight;

	/// <summary>
	/// The riders the bounce table holds, in slot order: the visitor, the node number its slot stored when BOUNCE filled it and the script clock reading it bounced at.
	/// </summary>
	// [BIN:TPW-EXE:0x00555742 BOUNCE node] BOUNCE fills the first free entry of the table and stores the rider (+0x00), the node number script +0x70 plus the entry's index (+0x04, 0x555742..0x55574F), then the deadline (+0x08) and the start (+0x0C); the node is kept in the entry from then on, so a later BOUNCESETNODE moves the nodes of the riders that bounce afterwards only
	internal void CollectBouncing( List<(int Guest, int Node, double Start)> into )
	{
		for ( var index = 0; index < bouncing.Count; index++ )
			if ( bouncing[index].Guest != 0 )
				into.Add( (bouncing[index].Guest, BounceNodeOf( index ), bouncing[index].Start) );
	}

	/// <summary>The node number slot <paramref name="index"/> stored (the base at its BOUNCE plus the index); a slot whose entry was never recorded takes the current base.</summary>
	private int BounceNodeOf( int index ) => index < bounceNodes.Count ? bounceNodes[index] : bounceNodeBase + index;

	/// <summary>Whether a rider's stored node differs from the current base plus its index (a script set the base while riders bounced): the one case a save has to carry the nodes.</summary>
	private bool BounceNodesDeviate()
	{
		for ( var index = 0; index < bouncing.Count; index++ )
			if ( bouncing[index].Guest != 0 && BounceNodeOf( index ) != bounceNodeBase + index )
				return true;
		return false;
	}

	/// <summary>Records the node of the slot BOUNCE just filled for <paramref name="guest"/> (0x555742: the base plus the entry's index).</summary>
	private void StoreBounceNode( int guest )
	{
		var index = bouncing.FindIndex( entry => entry.Guest == guest );
		if ( index < 0 )
			return;
		while ( bounceNodes.Count <= index )
			bounceNodes.Add( bounceNodeBase + bounceNodes.Count );
		bounceNodes[index] = bounceNodeBase + index;
	}

	/// <summary>Drops the stored nodes of the trailing slots <see cref="FreeBounceSlot"/> removed.</summary>
	private void TrimBounceNodes()
	{
		if ( bounceNodes.Count > bouncing.Count )
			bounceNodes.RemoveRange( bouncing.Count, bounceNodes.Count - bouncing.Count );
	}

	/// <summary>Performs a visitor opcode; false for opcodes the bridge does not own.</summary>
	public bool TryPerform( RideEffectCall call, out int result )
	{
		result = 0;
		var now = call.VM.TimeMilliseconds;
		switch ( call.Opcode )
		{
			case Opcode.WALKON:
			{
				var guest = call.Argument( 0 );
				MarkBoarded( guest );
				// [BIN:TPW-EXE:0x00555963 WALKON operands] seven values, each a signed word or a variable's value: the rider, the walk-on's first node (u1), its second node (u2), the nodes WALKOFF walks between (u3, u4), the walk action and a last word the handler stores and nothing reads here (0x00556F40: u1 and u2 at +0x00 and +0x02, u3 and u4 at +0x04 and +0x06, the rider at +0x10, the action at +0x16, the last word at +0x1A)
				// [BIN:TPW-EXE:0x00556F40 WALKON slot] the entry takes the first free table entry (state 0) of the header walk size; with none free the handler logs and stores nothing, so the rider is never handed out by WALKGET. Its state becomes 1 with the start now and the end now + ftol(the distance between u1 (class 0x800) and u2 (class 0x80 for the head action 4, else 0x800)) x 100 ms (100 ms for 0); state 2 follows at the frame update 0x00557D80 once the end has passed, WALKOFF's state 3 overrides either
				// [APPROX:TOUR-011] the walk update that moves state 1 to 2 runs once per rendered frame in the original (0x00557AB0, run by the main loop pass 0x0054E360 at 0x0054FA08) and draws the walking body of every entry (0x005580A0); the bridge makes the state changes in its host step (UpdateWalkers) and the bodies are drawn from the table by FlatRideBounceRiders.CollectWalkers, whose steps, terrain height and missing-node cases are the rest of this approximation
				if ( guest <= 0 )
					return true;
				var capacity = Math.Max( 0, call.VM.Script.WalkSize );
				var slot = FreeWalkSlot( capacity );
				if ( slot < 0 )
					return true;
				var headAction = call.Argument( 5 ) == 4;
				var onDuration = 0;
				if ( WalkNodeClassDistance?.Invoke( 0x800, call.Argument( 1 ), headAction ? 0x80 : 0x800, call.Argument( 2 ) ) is { } onDistance )
					onDuration = WalkOffDuration( onDistance );
				var entry = new WalkRecord( guest, call.Argument( 1 ), call.Argument( 2 ), call.Argument( 3 ), call.Argument( 4 ), call.Argument( 5 ), call.Argument( 6 ), 1, now, now + onDuration );
				// [BIN:TPW-EXE:0x00556F40 WALKON heading] the entry's heading word (+0x14) is the truncated angle atan2(z2 - z1, x2 - x1) of the two nodes, in radians, with its sign kept and its magnitude masked to three bits
				entry.Heading = WalkNodeHeading?.Invoke( 0x800, call.Argument( 1 ), headAction ? 0x80 : 0x800, call.Argument( 2 ) );
				SetWalkSlot( slot, entry );
				return true;
			}
			case Opcode.WALKOFF:
			{
				// [BIN:TPW-EXE:0x005571A0 WALKOFF] finds the first entry in use whose rider is the operand (none: the handler logs and does nothing), and whatever its state, sets state 3, the start now and the end now + ftol(the distance between u3 (class 0x80 for the head action 4, else 0x800) and u4 (class 0x800)) x 100 ms (100 ms for 0); the head action's head comes down (0x0044B4C0) and the action 2 effect starts at u3
				var guest = call.Argument( 0 );
				if ( FindWalkSlot( guest ) is { } found )
				{
					var entry = found.Entry;
					var duration = WalkOffMilliseconds;
					// [BIN:TPW-EXE:0x005571E4 WALKOFF head action] a head hangs only once the walk-on is over (state 2, 0x00557343), so only then it comes down (0x0044B4C0); an entry still in state 1 never got its head
					if ( entry.Mode == 4 && entry.State == 2 )
						Heads?.Remove( guest );
					if ( WalkNodeDistance != null )
					{
						if ( WalkNodeDistance( entry.Mode, entry.From, entry.To ) is { } distance )
							duration = WalkOffDuration( distance );
						if ( entry.Mode == 2 )
							WalkOffEffect?.Invoke( entry.From );
					}
					entry.State = 3;
					entry.Began = now;
					entry.Ends = now + duration;
				}
				return true;
			}
			case Opcode.WALKGET:
			{
				// [BIN:TPW-EXE:0x00557110 WALKGET] the first entry of the table in state 4 (the frame update moves state 3 to 4 once the end has passed) gives its rider to the operand and frees the entry; none: 0
				result = TakeWalker( now );
				return true;
			}
			case Opcode.BOUNCESETNODE:
				// [BIN:TPW-EXE:0x005555E9 BOUNCESETNODE] the raw operand word, unresolved, goes to script +0x70 (a variable operand stores its code word, not its value); the one shipped script that sets it (Jelly Bounce) passes the literal 3
				bounceNodeBase = call.RawWord( 0 );
				return true;
			case Opcode.BOUNCESETBASE:
				// [BIN:TPW-EXE:0x00555653 BOUNCESETBASE] a variable operand stores its value's low 16 bits at +0x6E, any other operand its low 16 bits sign-extended (0x00555665): the shipped scripts pass the literals 6, 8, 12 and 16
				bounceBaseHeight = unchecked( (short)call.Argument( 0 ) );
				return true;
			case Opcode.BOUNCE:
			{
				var guest = call.Argument( 0 );
				MarkBoarded( guest );
				// [BIN:TPW-EXE:0x00555753 BOUNCE deadline] the deadline is the clock plus the second operand x 1000 ms (a wrapping 32-bit product), whatever its value: a duration of 0 is a deadline that has already passed. The four shipped scripts pass VAR_DURATION, which ride init sets above 0 (WalkNodeCorpusTests)
				var seconds = call.Argument( 1 );
				// [BIN:TPW-EXE:0x00555780 BOUNCE result] the flag word takes 1 when an entry was free and 0 with a full table
				if ( onRide.Contains( guest ) && !bouncing.Any( entry => entry.Guest == guest ) )
					result = TakeBounceSlot( bouncing, call.VM.Script.BounceSize, guest, now + seconds * 1000.0, now ) ? 1 : 0;
				if ( result == 1 )
					StoreBounceNode( guest );
				return true;
			}
			case Opcode.UNBOUNCE:
			case Opcode.FORCEUNBOUNCE:
			{
				// Corpus: `TEST VAR_LETMEOFF; BRANCH_NZ; UNBOUNCE VAR_LETMEOFF` writes the leaving guest into LETMEOFF.
				var guest = TakeUnbounced( bouncing, now, call.Opcode == Opcode.FORCEUNBOUNCE );
				TrimBounceNodes();
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
				HeadTableFor( call.VM )?.Add( call.Argument( 0 ) );
				return true;
			case Opcode.DELHEAD:
				// [BIN:STP-PPC:0x100B0F90 DELHEAD] no register write
				HeadTableFor( call.VM )?.Remove( call.Argument( 0 ) );
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
	// [EXT:bounce-single-clock-read] one script-clock reading stands for both of BOUNCE's clock calls: OpenTPW's clock is stepped and does not advance inside an opcode, so the two live reads of tp.exe's BOUNCE (0x00555674: the deadline 0x00555753 and the start 0x00555770 through 0x00402D70, up to 1 ms apart, which moves UNBOUNCE's 200 ms window by 1 ms) cannot differ; a determinism design choice
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
	/// with a C remainder and 32-bit times in whole milliseconds. FORCEUNBOUNCE takes the first occupied slot inside that window whatever its deadline. The slot
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
			// [BIN:TPW-EXE:0x005558B7 FORCEUNBOUNCE window] FORCEUNBOUNCE (0x00555842) drops only the deadline test of UNBOUNCE (0x005557A3, deadline below the clock, signed, at 0x00555811): the 200 ms window of each second since the bounce (the remainder of elapsed / 1000 divided by 200 is 0) still decides
			if ( elapsed % 1000 / 200 == 0 && (force || (long)Math.Floor( until ) < clock) )
			{
				FreeBounceSlot( list, index );
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
public sealed class VisitorRideScriptEffects( RideVisitorBridge bridge, IRideScriptEffects inner ) : IRideScriptEffects, IRideAnimationQuery, IRideSliceTail
{
	public int Perform( RideEffectCall call ) => bridge.TryPerform( call, out var result ) ? result : inner.Perform( call );

	public void AfterSlice( RideVM vm ) => ( inner as IRideSliceTail )?.AfterSlice( vm );

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
