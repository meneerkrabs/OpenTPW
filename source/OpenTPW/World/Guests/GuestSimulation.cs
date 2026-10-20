using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>Counts for overlays, logs and tests.</summary>
public readonly record struct GuestStatistics( int InPark, int Walking, int Queueing, int OnRides, int Arriving, int Leaving, float AverageHappiness, long Admissions );

/// <summary>
/// Park visitors on the fixed simulation tick: arrival from the bus stops through the ticket booth
/// (.sam FixedItemInfo cells), path walking on <see cref="GuestPathGrid"/> flow fields, needs, attraction
/// choice with the .sam decision weights, queues and rides through <see cref="IRideVisitorBridge"/>, and
/// leaving. Queues follow the original state 10/11/12/13/14 handlers (docs/reverse/QUEUE-plan.md): guests
/// join at the back cell, walk the queue cells to their position, stand and move up on park turns, and
/// are called forward by the attraction. All randomness comes from the park's one <see cref="WorldRng"/>
/// (also the queue positions' sideways offsets); guests and attractions are
/// updated in id order, so the same seed and inputs give the same <see cref="ComputeStateHash"/>.
/// Rates and thresholds without an original source are labelled approximations in
/// <see cref="GuestSettings"/> and docs/GUESTS.md.
/// </summary>
public sealed partial class GuestSimulation : IRideVisitorHost, IGuardGuests
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
	/// <summary>[EXT:pools-swim] How long a guest out of a pools changing room stands on its path-side exit before it decides again (every other let-off walks out in state 15, <see cref="ExitRide"/>).</summary>
	public const float ExitRideSeconds = 1.0f;
	public const float WaitForBusSeconds = 3.0f;

	private readonly List<Guest> guests = new();
	private readonly Dictionary<int, Guest> byId = new();
	private readonly List<IRideVisitorBridge> attractions = new();
	private WorldRng random;
	// the park's thing pool (tp.exe 0x516270): a guest's id is the slot its constructor takes; a simulation of its own owns a pool and drains its deferred destroys itself
	private ThingPool things = new();
	private bool ownsThings = true;
	// The arrival timer's start turn (0 until the first group: the original's timer starts unset) and the group being dropped off.
	private long arrivalTimerStart;
	private bool deliveringArrivals;
	private float spawnTimer;   // kept for the save format; arrivals step once per park turn (ARRIVAL-001)
	private int pendingArrivals;
	// The arrival vehicle at work (world +0x1DA72A): the kind of ArrivalVehicleKind, -1 for none.
	private int arrivalVehicle = -1;
	// kept for the save format and the hash: the arrival side is the entrance draw of SpawnArrival, so this stays 0
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
	private long cellSerial;   // the entry number the next guest to enter a cell takes (Guest.CellSerial), so the saved lists come back in their order
	private readonly List<GuestNavigation> neighbours = new();

	/// <param name="decisions">
	/// The decision layer (<see cref="IGuestDecisions"/>). Null takes the policy the <c>opentpw.smarter-guests</c> extension selects, read once here: <see cref="SmarterGuestDecisions"/> with the
	/// extension on, <see cref="OriginalGuestDecisions"/> without it. The gate is never read again.
	/// </param>
	public GuestSimulation( GuestPathGrid grid, GuestSettings settings, ulong seed, IGuestDecisions? decisions = null )
	{
		Grid = grid;
		Settings = settings;
		Seed = seed;
		random = new WorldRng( WorldRng.StateFromSeed( seed ) );
		isOpenForStep = IsOpenForStep;
		separationForStep = SeparationForStep;
		// [EXT:smarter-guests] the gate is read here, once; a smarter policy brings the extension's park state with it
		this.decisions = decisions ?? DefaultPolicy();
		// the script mods are those of the policy that runs them, however it was chosen, so what runs is what is saved and hashed
		ScriptMods = (this.decisions as ScriptGuestDecisions)?.Runtime;
		if ( this.decisions.UsesExtensionState )
			SmarterGuests = new SmarterGuestsState( seed );
		// [EXT:guest-insights] the gate is read here, once
		if ( FeatureGates.GuestInsights )
			Observer = RefusalCounters = new GuestRefusalCounters();
	}

	/// <summary>
	/// The policy a simulation built without one takes, read once from the active mod set: the smarter-guests policy or the original, wrapped by <see cref="ScriptGuestDecisions"/> when script mods are
	/// enabled (docs/SCRIPT-MODS.md). Without a script mod nothing is wrapped, built, saved or hashed.
	/// </summary>
	// [EXT:script-mods] the wrapper runs the enabled script mods' hooks over the base policy's answers
	private static IGuestDecisions DefaultPolicy()
	{
		IGuestDecisions policy = FeatureGates.SmarterGuests ? new SmarterGuestDecisions() : OriginalGuestDecisions.Instance;
		return ScriptModRuntime.ForActive( OpenTpwExtensions.Active ) is { } scriptMods ? new ScriptGuestDecisions( policy, scriptMods ) : policy;
	}

	private readonly IGuestDecisions decisions;

	/// <summary>The decision layer this simulation was built with.</summary>
	public IGuestDecisions Decisions => decisions;

	/// <summary>The <c>opentpw.smarter-guests</c> extension's park state, null when the simulation runs the original policy (docs/SMARTER-GUESTS.md).</summary>
	// [EXT:smarter-guests] OpenTPW's own extension state; never in the guest snapshot or the guest state hash
	internal SmarterGuestsState? SmarterGuests { get; }

	/// <summary>The enabled script mods of this simulation (their state, failure counters and disabled flags), null when none is enabled (docs/SCRIPT-MODS.md).</summary>
	// [EXT:script-mods] OpenTPW's own scripted mods; never in the guest snapshot or the guest state hash
	internal ScriptModRuntime? ScriptMods { get; }

	public GuestPathGrid Grid { get; }
	public GuestSettings Settings { get; }
	public ulong Seed { get; }
	/// <summary>Current state of the world generator the guests draw from (saved with the park; see <see cref="WorldRandomState"/>).</summary>
	public ulong RandomState => random.State;
	/// <summary>The world generator (tp.exe 0x00516330) every guest decision draws from: the park's one stream, shared with the economy, the staff and the scripts.</summary>
	public WorldRng World => random;
	/// <summary>Makes <paramref name="world"/> the generator the guests draw from (the level's shared one); its state is not touched.</summary>
	public void AttachWorld( WorldRng world ) => random = world ?? throw new ArgumentNullException( nameof( world ) );
	/// <summary>
	/// The park's thing pool (tp.exe 0x00516270): a guest's id is the slot its constructor takes, shared with every other kind of thing. A simulation without a level owns a
	/// pool of its own (ids 1, 2, 3, ...).
	/// </summary>
	public ThingPool Things => things;
	/// <summary>
	/// Makes <paramref name="pool"/> the thing pool the guests take their ids from (the level's shared one); the host then ends the turn's deferred destroys
	/// (<see cref="ThingPool.DestroyQueued"/>, the park turn's end 0x00516641). Only before any guest exists.
	/// </summary>
	public void AttachThings( ThingPool pool )
	{
		ArgumentNullException.ThrowIfNull( pool );
		if ( guests.Count != 0 )
			throw new InvalidOperationException( "The thing pool is attached before the first guest is built." );
		things = pool;
		ownsThings = false;
	}
	public IReadOnlyList<Guest> Guests => guests;
	/// <summary>True when the guests take their ids from a host's thing pool (<see cref="AttachThings"/>), so the host's thing sweep can visit them.</summary>
	public bool SharesThings => !ownsThings;
	public IReadOnlyList<IRideVisitorBridge> Attractions => attractions;
	public long TickCount { get; private set; }
	/// <summary>The park turn (248 ms of the fixed clock, <see cref="ParkCalendar.Turn"/>), counted from the guests' own start; queue waits and delays count these.</summary>
	public long ParkTurn => ParkCalendar.Turn( TickCount + turnPhase ) - ParkCalendar.Turn( turnPhase );

	// the ticks the economy's clock was ahead of the guests' when the host aligned them: an economy that starts at an original save's turn has run that many ticks before the first guest tick
	private long turnPhase;

	/// <summary>
	/// Puts the guests' turn boundaries on the economy's: the guests' tick count started at 0 where an economy that begins at an original save's turn started at that turn's tick, so the two
	/// clocks cut the ticks into turns at different places (a tick apart in two of five turns) and one park turn could not be swept as a whole. The guests' <see cref="ParkTurn"/> still counts
	/// from their own start. Nothing changes while both start together.
	/// </summary>
	public void AlignTurns( long economyTick )
	{
		if ( economyTick >= TickCount )
			turnPhase = economyTick - TickCount;
	}
	public double TimeSeconds { get; private set; }
	public bool ArrivalsEnabled { get; set; } = true;
	/// <summary>
	/// The park's arrival vehicles (bus, seaplane, Ferry and their scripts). Null keeps the vehicle-less step-off of a park without
	/// them (headless scenes): a group's first guest steps off on the turn after it starts and leaving guests go after
	/// <see cref="WaitForBusSeconds"/>.
	/// </summary>
	public IArrivalVehicleHost? ArrivalVehicles { get; set; }
	/// <summary>The arrival vehicle at work (<see cref="ArrivalVehicleKind"/>), -1 for none.</summary>
	public int ArrivalVehicle => arrivalVehicle;
	/// <summary>The park turn the arrival timer reads: the economy's when attached, else <see cref="ParkTurn"/>.</summary>
	public Func<long>? ArrivalClock { get; set; }
	/// <summary>The park's attraction points for the arrival rule (the economy's attractions); 0 without one.</summary>
	public Func<int>? ParkPoints { get; set; }

	/// <summary>
	/// Whether the entrance gate's script reports itself open (its script variable 1, <c>VAR_STATUS</c>; null reads as open, a level without a gate). A paid guest walks in only
	/// while it is (tp.exe's state 3, <c>0x004FF80F</c> with <c>0x0051A290</c>).
	/// </summary>
	public Func<bool>? EntranceGateOpen { get; set; }
	/// <summary>True while no group arrives (a bankrupt park).</summary>
	public Func<bool>? ArrivalsHalted { get; set; }
	/// <summary>
	/// [EXT:cheats-arrival] True while an arrival cheat waits for the vehicles to be free (<see cref="ParkCheats.ArrivalPending"/>): no natural group starts and no vehicle is called for
	/// leaving guests, so the vehicle at work finishes and the cheat's group comes at a following turn. A vehicle already at work completes as usual.
	/// </summary>
	public Func<bool>? ArrivalHeldForCheat { get; set; }
	/// <summary>
	/// True while the park's weather has drops falling (<see cref="ParkWeather.CurrentDrops"/> above 0). The group size then uses 0.8
	/// instead of 1.2. The original reads the signed 32-bit word at +48 of the weather thing (game +0x1DA724, saved as mWeather; the
	/// weather object's mCurrentDrops, the drops the weather ramps toward its target) and uses 0.8 when it is positive. Null (no
	/// weather yet, or a headless scene without one) is 1.2: no drops.
	/// </summary>
	// [BIN:STP-PPC:0x100CB564 arrival factor] 0x1083c8 returns the thing at ThingArray[u16 game+0x1DA724] (mWeather, created at 0x104a7c with the weather constructor 0x10298c, stored at 0x104a98 and saved by 0x105d3c as "mWeather"); 0xcb568 reads its +48 word, mCurrentDrops (serialised by 0x102b1c); positive selects 0.8f
	// [BIN:STP-PPC:0x10103300-0x10103348 drop ramp] the only writers of +48 are the constructor (0x102aa0, zero), the ramp toward mTargetDrops (+52) and the rain stop (0x103d08, zero), so the word is positive exactly while drops are falling
	public Func<bool>? WeatherDropsFalling { get; set; }
	/// <summary>
	/// Theme Park Inc's weather state for the group size: 1 heatwave, 2 rain, 3 storm, 4 snow, anything else (or null) none. The original reads the
	/// weather thing's word +0x70. OpenTPW runs no Theme Park Inc weather, so nothing sets this and the factor is always the plain 100 percent.
	/// </summary>
	// [BIN:TPI-EXE:0x005BCDDE weather state] the weather thing's word +0x70 minus 1, when it is at most 3, indexes a jump table (0x005BCF08) whose first entry 0x005BCE06 loads the heatwave word 0x00A02250 (the others, in order, rain 0x00A02244, storm 0x00A02248, snow 0x00A0224C; ORACLE.md X7); any other value takes 100.0f
	public Func<int>? ArrivalWeatherState { get; set; }
	/// <summary>Index into <see cref="GuestSettings.ArrivalWeatherPercentages"/> (rain, storm, snow, heatwave) of weather states 1 to 4 (heatwave, rain, storm, snow).</summary>
	private static readonly int[] WeatherStateIndex = { 3, 0, 1, 2 };
	/// <summary>The original's limit on guests in the park when a group's size is set.</summary>
	// [BIN:STP-PPC:0x100CB4E8 arrival group] a group is cut so the guests already counted plus the group stay at most 1500 (0x5DC)
	// [BIN:STP-PPC:0x100CB4E8 arrival group] executed by the PowerPC oracle (docs/reverse/ORACLE.md F9): the 500 cut applies when the GameType word (data:0x53d98) is 1, i.e. online parks only; offline parks use 1500
	// [BIN:TPW-EXE:0x004CF5B0 arrival group cut] with the GameType word 0x00FB3B7C at 1 the counted guests plus the group stay at most 500 (cmp 0x1F4 at 0x004CF68A), else at most 1500 (cmp 0x5DC at 0x004CF6E8); the main loop runs the park turn for GameType 1 through 0x005166B0 (0x0054F760), so a visited park runs this routine
	public const int ArrivalParkLimit = 1500;
	/// <summary>The group limit while <see cref="OnlineVisit"/> is set.</summary>
	public const int OnlineArrivalParkLimit = 500;
	/// <summary>The original's thing pool (10239 slots): no park holds more guests, so a cut remainder (limit − guests) is never below a limit minus this.</summary>
	private const int MaximumThings = 10239;
	/// <summary>
	/// True for an online visit of another player's park. The original cuts the group to 500 minus the guests when its GameType word
	/// (data:0x53D98) is 1 (online) and to 1500 otherwise.
	/// </summary>
	// [BIN:TPW-EXE:0x004C0830 online visit] the park-info dialog's completion (stored at 0x007CCA4C by 0x004C0AE0, called at 0x004C07FF by the completion 0x004C07D0 that the dialog passes on at 0x004C0A09) writes exit code 2 and calls SetGameType(1) (0x004C0876), so a visit of a published park is GameType 1
	// [EXT:online-visit-settings] a visit reads the group's Arrival.* keys from Standard.sam, where the original loads online_Standard.sam for GameType 1 (0x0051573C), and a read-only visit has no economy, so its groups count 0 park points: the online visit is OpenTPW's read-only extension, not the original's two-player park (docs/ONLINE.md)
	public bool OnlineVisit { get; set; }

	private ParkLitter? litter;
	/// <summary>Where guests drop their litter: set here, else the park's (<see cref="IGuestPayments.Litter"/>); null keeps none.</summary>
	public ParkLitter? Litter { get => litter ?? Payments?.Litter; set => litter = value; }
	/// <summary>The park's litter bins (objects with <c>UsageInfo.HoldsLitter</c>), in object-list order; null: none.</summary>
	public Func<IReadOnlyList<LitterBin>>? Bins { get; set; }
	/// <summary>Squared distance in cells below which a bin is in reach (tp.exe <c>0x00500DC0</c>, radius 3).</summary>
	public const int BinRadius = 3;
	/// <summary>The largest radius a policy may ask for (<see cref="IGuestDecisions.BinSearchRadius"/>).</summary>
	public const int MaxBinRadius = 16;
	/// <summary>A guest reached its bin and threw its litter away (the level triggers the bin's script; tp.exe <c>0x004FFF20</c>).</summary>
	public event Action<Guest, int>? LitterBinned;
	/// <summary>Where guests pay (the park economy). Null: the standalone <see cref="GuestSettings.AdmissionFee"/> and attraction prices apply and no money is booked anywhere.</summary>
	public IGuestPayments? Payments { get; set; }
	/// <summary>Entrance fee: the economy's when <see cref="Payments"/> is set, else <see cref="GuestSettings.AdmissionFee"/>.</summary>
	public int AdmissionFee => Payments?.AdmissionFee ?? Settings.AdmissionFee;
	/// <summary>Number of guests admitted (a visitor count; money is booked by <see cref="Payments"/>).</summary>
	public long Admissions { get; private set; }

	/// <summary>
	/// A passive measurement view (<see cref="IGuestObserver"/>), null by default. It is not part of the simulation: never saved, never hashed, and it must not draw from the
	/// world generator or change a guest.
	/// </summary>
	public IGuestObserver? Observer
	{
		get => observer;
		// [EXT:guest-insights] with the extension on, the refusal counters keep the slot's first place and a measurement collector shares it
		set
		{
			if ( ReferenceEquals( value, observer ) )
				return;
			observer = RefusalCounters == null ? value
				: value == null || ReferenceEquals( value, RefusalCounters ) ? RefusalCounters
				: value is GuestObserverChain chain && ReferenceEquals( chain.First, RefusalCounters ) ? chain
				: new GuestObserverChain( RefusalCounters, value );
		}
	}

	/// <summary>Takes the refusal counters off the observer slot, leaving any collector there (the guest report's timing runs, so its figures do not include the extension's counters).</summary>
	internal void DisableRefusalCounters()
	{
		var counters = RefusalCounters;
		if ( counters == null )
			return;
		RefusalCounters = null;
		observer = ReferenceEquals( observer, counters ) ? null : observer is GuestObserverChain chain && ReferenceEquals( chain.First, counters ) ? chain.Second : observer;
	}

	private IGuestObserver? observer;

	/// <summary>
	/// The refusal-reason counters of the <c>opentpw.guest-insights</c> extension, or null while it is off. The gate is read once, when the simulation is built; the counters are a passive
	/// observer (never saved, never hashed, no draw) that a loaded park starts again from zero.
	/// </summary>
	public GuestRefusalCounters? RefusalCounters { get; private set; }
	public int Departed { get; private set; }
	/// <summary>Raised when a guest pays (guest, amount, attraction id or 0 for admission).</summary>
	public event Action<Guest, int, int>? MoneySpent;
	/// <summary>Raised when the script takes a queued guest: (guest, attraction, wait in park turns from joining).</summary>
	public event Action<Guest, IRideVisitorBridge, long>? QueueWaitCompleted;

	/// <summary>Continues the world generator from a saved state.</summary>
	public void RestoreRandomState( uint state ) => random.Restore( state );

	public Guest? Find( int id ) => byId.TryGetValue( id, out var guest ) ? guest : null;

	public void Register( IRideVisitorBridge attraction )
	{
		if ( attractions.Contains( attraction ) )
			return;
		attraction.Host = this;
		attractions.Add( attraction );
		if ( attraction.IsUmbrellaShop )
			umbrellaShops++;
		if ( attraction.WinterTerms != null )
			winterShops++;
		attractions.Sort( ( a, b ) => a.AttractionId.CompareTo( b.AttractionId ) );
		// A queue from a save made before queue cells were linked gets the links the queue tool writes (QueuePaths.RebuildMissingLinks).
		if ( attraction is RideVisitorBridge ride && ride.HasQueue )
			QueuePaths.RebuildMissingLinks( Grid, ride );
		attraction.RecomputeQueue( Grid );
		seenQueueEdits[attraction.AttractionId] = attraction.QueueEditCount;
	}

	/// <summary>Park turns the guest has waited in its current queue so far, or −1 when it is not queued.</summary>
	public long QueueWaitTurns( Guest guest ) => guest.QueueJoinTurn < 0 ? -1 : ParkTurn - guest.QueueJoinTurn;

	/// <summary>
	/// An attraction is removed (the removal message 10, tp.exe <c>0x004DD0A0</c> → the guest handler <c>0x004FB360</c>): every guest whose target it is
	/// loses the small happiness step and its target and decides again (state 6); a queued guest (states 11–14) is first dismissed (<c>0x005012F0</c>:
	/// the medium step, the queue words cleared); every guest's used history forgets the attraction.
	/// </summary>
	// [BIN:TPW-EXE:0x004FB360 object removed] for message 10 naming the target +0x1DC: a guest in state 16 (on the ride) gets its model back (0x004D4140 / 0x004217F0), +0x28 = 0, event 0xD and the cat_kids 0x80 sound; any other guest in states 11–14 (0x00502430, state 8's held state too) the dismissal 0x005012F0; then every one the small step (0x004FE980(0), clamped by 0x004FB4F0), +0x1DC = 0 and state 6 (0x00501DB0); the target test (0x004FB383, jne 0x004FB4A6) guards only that block: for every guest the message reaches, the major destination +0x1DE is cleared when it names the object (0x004FB4A6) and each used-history slot +0x1E0 that does is zeroed with the temporary-history slot +0x1E8 of the same index (0x004FB4BA..0x004FB4D9)
	// [APPROX:GUEST-004] OpenTPW keeps no major destination (+0x1DE, a target set aside for a minor one, 0x00500900) and clears the target when a visit ends, where tp.exe keeps +0x1DC until the next choice 0x004FCB10, so a guest that has just left the removed attraction is not hit; the state-16 event 0xD and sound are not modelled; a guest on the ride or in the queue is placed at once on the exit or join cell (QUEUE-010)
	public void Unregister( IRideVisitorBridge attraction )
	{
		if ( !attractions.Remove( attraction ) )
			return;
		if ( attraction.IsUmbrellaShop )
			umbrellaShops--;
		if ( attraction.WinterTerms != null )
			winterShops--;
		SmarterGuests?.Throughputs.Remove( attraction.AttractionId );
		foreach ( var guest in guests )
		{
			if ( guest.AttractionId != attraction.AttractionId || attraction.AttractionId == 0 )
				continue;
			DropForRemovedAttraction( guest, attraction );
		}
		// every guest, targeting it or not, forgets it in its histories
		foreach ( var guest in guests )
		{
			for ( var slot = 0; slot < Guest.HistorySlots && attraction.AttractionId != 0; slot++ )
			{
				if ( guest.UsedHistory[slot] != attraction.AttractionId )
					continue;
				guest.UsedHistory[slot] = 0;
				guest.TemporaryHistory[slot] = 0;
			}
		}
		if ( attraction.Host == this )
			attraction.Host = null;
		seenQueueEdits.Remove( attraction.AttractionId );
	}

	/// <summary>The removal message's treatment of one guest whose target the attraction is (tp.exe <c>0x004FB360</c>; see <see cref="Unregister"/>).</summary>
	private void DropForRemovedAttraction( Guest guest, IRideVisitorBridge attraction )
	{
		var cell = guest.State == GuestState.Using ? attraction.ExitCell : guest.IsInQueue ? JoinCellOf( attraction ) : (guest.CellX, guest.CellY);
		if ( guest.State != GuestState.Using && guest.IsInQueue )
			guest.Happiness = MeterMath.Add( guest.Happiness, -(float)(Settings.MediumHappinessChange & 0xFF) );
		// [BIN:TPW-EXE:0x4fb3f5 rider voice] a guest in state 16 (on the ride) whose ride is removed gets its model back and plays cat_kids 0x80, whatever its thing id
		if ( guest.State == GuestState.Using )
		{
			RemovedRideBodySprite( guest );
			Voiced?.Invoke( guest, RideRemovedVoiceEvent );
		}
		NoteQueueEnd( guest, attraction.AttractionId, QueueEndReason.AttractionRemoved );
		attraction.LeaveQueue( guest.Id );
		ClearQueueState( guest );
		guest.SideshowWon = null;
		guest.Happiness = MeterMath.Add( guest.Happiness, -(float)(Settings.SmallHappinessChange & 0xFF) );
		// [EXT:pools-swim] demolishing a changing room gets its swimmers out of the water: they step out on its path-side exit
		if ( guest.InSwimSession )
			EndSwimAtOnce( guest, attraction );
		else if ( guest.State is GuestState.GoingToRide or GuestState.Using || guest.IsInQueue )
			ReturnToPath( guest, cell );
		else
		{
			guest.AttractionId = 0;
			EnterGuestState( guest, GuestState.DoingNothing );
		}
		if ( !guest.InSwimSession )
			guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Stand );
	}

	/// <summary>
	/// A litter bin is removed (the removal message 10, tp.exe <c>0x004DD0A0</c> → <c>0x004FB360</c>): a guest walking to it loses the small happiness step and
	/// its target and decides again where it stands (state 6), keeping its litter.
	/// </summary>
	// [BIN:TPW-EXE:0x004FB360 bin removed] the bin is the walking guest's target +0x1DC (state 9, 0x004FFF20): the small step (0x004FE980(0)), +0x1DC = 0 and state 6; the litter meter +0x1B4 is not touched
	public void BinRemoved( int binId )
	{
		if ( binId == 0 )
			return;
		foreach ( var guest in guests )
		{
			if ( guest.State != GuestState.ThrowingAwayLitter || guest.BinId != binId )
				continue;
			guest.Happiness = MeterMath.Add( guest.Happiness, -(float)(Settings.SmallHappinessChange & 0xFF) );
			var navigation = guest.Navigation;
			navigation.SetDestination( navigation.X, navigation.Y, [] );
			guest.HasWaypoint = false;
			guest.IsMoving = false;
			guest.BinId = 0;
			EnterGuestState( guest, GuestState.DoingNothing );
		}
	}

	private Guest? Create()
	{
		// [BIN:TPW-EXE:0x004F896A thing id] the base constructor 0x004F8940 takes the thing's id from the pool (0x0050AFE0 → the allocator 0x00516270) before its first draw; a full pool logs a fatal error and gives id 0 (here: no guest)
		// [EXT:smarter-guests] a used-up birth id counter is refused before anything is allocated or drawn, so a refused creation leaves no slot behind
		if ( SmarterGuests is { NextBirthId: ulong.MaxValue } )
			throw new OverflowException( "The smarter-guests birth id counter is used up; it is not wrapped." );
		var id = things.Allocate( SaveThingModel.Guest );
		if ( id == 0 )
			return null;
		// [BIN:TPW-EXE:0x004F89E1 base speed draw] the thing base constructor 0x004F8940 draws first: R mod 5 (unsigned) indexes the speed table {60, 80, 100, 120, 140} (0x0075C7F8) for +0xC0
		var baseSpeed = GuestSpeed.BaseSpeeds[(int)random.Next( (uint)GuestSpeed.BaseSpeeds.Length )];
		// [BIN:STP-PPC:0x100E7644 guest constructor] the starting values come from GuestStart (ORACLE.md F25): eight draws in the constructor's order
		// [BIN:TPW-EXE:0x004FAFF8 guest constructor draws] the person constructor 0x004FAEC0 takes the eight draws of GuestStart right after the base constructor's (0x004FAFF8, 0x004FB01F, 0x004FB046, 0x004FB085, 0x004FB0AB, 0x004FB0D1, 0x004FB109, 0x004FB114), all from the one world generator
		var start = GuestStart.FromDraws( Settings.Types, Settings.ExitLevelSeconds, Settings.ExitLevelVarSeconds, Settings.StartingCashVarPercent, () => random.NextWord(),
			Settings.PrankeryLikelihood, id );
		var guest = new Guest( id, start.Type );
		// [BIN:TPW-EXE:0x004FB1C1 creation speed term] the person constructor stores the word 0x0075C7F2 (25) as the purpose speed +0xC2, which the needs phase (the toilet term) and the booth walk (state 2) overwrite
		guest.ToiletBonus = GuestSpeed.ToiletBonus;
		// [BIN:TPW-EXE:0x004FAFDB arrival stamp] the person constructor stamps the world turn into +0x1D4, the one write of the visitor's time in the park
		guest.TimeInParkSeconds = 0;
		guest.PrankFlag = start.PrankFlag;
		guest.Money = start.Money;
		guest.Happiness = start.Happiness;
		guest.Thirst = start.Thirst;
		guest.Hunger = start.Hunger;
		guest.Toilet = start.Toilet;
		guest.Nausea = start.Illness;
		guest.ExitLevel = start.ExitLevel;
		// [BIN:TPW-EXE:0x004FB19E guest reseed] the constructor then reseeds the world generator with the thing's id (zero-extended u16, through 0x0050B350), so everything the guest and the world draw after its creation starts from a state that depends on the id alone
		random.Reseed( (ushort)guest.Id );
		// [BIN:TPW-EXE:0x004FB1A7 guest sprite variant] the next call, 0x00541F70 with category 0, draws (R >> 2) mod the category's bank count (0x00541F85) for the guest's sprite bank +0x20: the draw is taken, its value is not used (the sprite choice is OpenTPW's own, GUEST-017)
		random.NextWord();
		CreateBodySprite( guest, VisitorSpriteTemplate.Walk );
		// A guest placed without the arrival constructor's position draws (the park's own start cell, tests, imports) is spread by a stable hash of its id: OpenTPW's own placement
		// jitter, not a draw of the world generator (presentation of the start; the original sets +0x18/+0x1A from the entrance draws of SpawnArrival)
		var jitter = PlacementJitter( guest.Id );
		guest.OffsetX = jitter.X;
		guest.OffsetY = jitter.Y;
		guest.BaseSpeed = baseSpeed;
		// [EXT:smarter-guests] the birth id comes with the guest, before its first decision (the counter is the extension's, no draw)
		if ( SmarterGuests is { } smarter )
			guest.BirthId = smarter.AllocateBirthId();
		guests.Add( guest );
		byId.Add( guest.Id, guest );
		return guest;
	}

	/// <summary>The guests in the park's thing-list order (newest first), then any guest outside the pool's list by id: the order the extension's first tick gives birth ids in.</summary>
	private IEnumerable<Guest> GuestsInThingListOrder()
	{
		var seen = new HashSet<int>();
		foreach ( var slot in things.UsedNewestFirst() )
			if ( slot.Model == SaveThingModel.Guest && byId.TryGetValue( slot.Id, out var guest ) && seen.Add( slot.Id ) )
				yield return guest;
		foreach ( var guest in guests.Where( guest => !seen.Contains( guest.Id ) ).OrderBy( guest => guest.Id ).ToArray() )
			yield return guest;
	}

	/// <summary>Fills <paramref name="into"/> with the guests in the park's thing-list order (newest first), as <see cref="GuestsInThingListOrder"/> does, for the renderers that walk the people list.</summary>
	internal void FillInThingListOrder( List<Guest> into )
	{
		into.Clear();
		foreach ( var slot in things.UsedNewestFirst() )
			if ( slot.Model == SaveThingModel.Guest && byId.TryGetValue( slot.Id, out var guest ) )
				into.Add( guest );
		if ( into.Count == guests.Count )
			return;
		foreach ( var guest in GuestsInThingListOrder().Skip( into.Count ) )
			if ( !into.Contains( guest ) )
				into.Add( guest );
	}

	/// <summary>A stable offset inside a cell (±0.25 of a cell each way) from the guest id alone: the start point of a guest that was not made by the arrival constructor.</summary>
	private static (float X, float Y) PlacementJitter( int id )
	{
		var z = (ulong)(uint)id * 0x9E3779B97F4A7C15UL + 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		z ^= z >> 31;
		return (((z >> 40) / (float)(1UL << 24) - 0.5f) * 0.5f, (((z & 0xFFFFFF)) / (float)(1UL << 24) - 0.5f) * 0.5f);
	}

	/// <summary>Spawns a guest already inside the park on a walkable cell (tests, smoke, imported parks).</summary>
	public Guest SpawnInPark( int x, int y )
	{
		if ( !Grid.IsWalkable( x, y ) )
			throw new ArgumentException( $"Cell ({x}, {y}) is not a walkable path cell." );
		var guest = Create() ?? throw new InvalidOperationException( $"The thing pool is full ({ThingPool.Capacity} things); no guest can be built." );
		PlaceOnGrid( guest, x, y );
		// [BIN:TPW-EXE:0x004FB1D9 constructor on a park cell] a guest made on a park cell (0x004FA990 true) takes its visitor number at once (0x0051AAF0 into +0x1D8) and is in state 6
		guest.VisitorNumber = ++VisitorCount;
		EnterGuestState( guest, GuestState.DoingNothing );
		return guest;
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
		BeginTick( deltaSeconds );
		UpdateArrivals( deltaSeconds );
		if ( turnStarted )
		{
			SyncOccupants();
		}
		// [APPROX:DET-014] without a host thing sweep (a simulation of its own, the ticks between park turns) the guests' draws interleave in this list order, not the original's newest-first thing sweep (DET-013), and the draws of the won sideshow's tail (0x004FE8D9) and the balloon model's recreation (0x0050205F) are not taken
		for ( var index = 0; index < guests.Count; index++ )
			UpdateGuest( guests[index], deltaSeconds );
		for ( var index = guests.Count - 1; index >= 0; index-- )
		{
			if ( guests[index].State != GuestState.Gone )
				continue;
			RemoveGone( guests[index] );
		}
		Observer?.Ticked( this, deltaSeconds );
		if ( ownsThings )
			things.DestroyQueued();
	}

	/// <summary>The start of every tick: the checks, the tick counters, the turn flag and the queue sync.</summary>
	private void BeginTick( float deltaSeconds )
	{
		if ( !float.IsFinite( deltaSeconds ) || deltaSeconds < 0 || deltaSeconds > Guest.MaximumTimeInParkSeconds )
			throw new ArgumentOutOfRangeException( nameof( deltaSeconds ) );
		// Preflight the display counter before mutating the simulation, rather than wrapping or inventing a duration.
		foreach ( var guest in guests )
			if ( CountsParkTime( guest ) && guest.TimeInParkSeconds is { } elapsed && deltaSeconds > Guest.MaximumTimeInParkSeconds - elapsed )
				throw new OverflowException( $"Guest {guest.Id} time in park exceeds the supported duration." );
		TickCount++;
		TimeSeconds += deltaSeconds;
		decisions.BeginTick();
		// [EXT:smarter-guests] the extension's first tick gives the guests that predate it their ids, in thing-list order; before any decision of the tick
		if ( SmarterGuests is { Bootstrapped: false } smarter )
			smarter.Bootstrap( GuestsInThingListOrder() );
		staffOnMap = StaffOnMapView?.Invoke().ToArray();
		turnStarted = ParkCalendar.Turn( TickCount + turnPhase ) != ParkCalendar.Turn( TickCount - 1 + turnPhase );
		if ( turnStarted && decisions.UsesSpreading )
			ObserveThroughputs();
		SyncQueues();
	}

	/// <summary>A gone guest leaves the simulation: it asks for its destruction and its slot is freed at the park turn's end.</summary>
	private void RemoveGone( Guest guest )
	{
		RemoveBalloonPlayer( guest ); // Detached burst slots are independent of the departed/reused guest id.
		guest.BodySprite.ResetForNewPerson();
		Observer?.Departed( guest );
		// [EXT:smarter-guests-value] a departing guest's run of price-emptied decisions goes with it, so the bookkeeping is bounded by the guests in the park
		SmarterGuests?.Stalls.Reset( guest.BirthId );
		SmarterGuests?.Meals.Forget( guest.BirthId );
		SmarterGuests?.Complaints.Forget( guest.BirthId );
		// [BIN:TPW-EXE:0x0050B780 destroy request] a leaving guest asks for its destruction (0x0050B780 → 0x00516310); the id is freed at the park turn's end (0x00516641)
		things.QueueDestroy( guest.Id );
		LeaveCellLists( guest );
		byId.Remove( guest.Id );
		guests.Remove( guest );
		if ( guest.UmbrellaState != UmbrellaRules.StateNone && umbrellaHolders > 0 )
			umbrellaHolders--;
		Departed++;
	}

	/// <summary>True when the next <see cref="Tick"/> starts a park turn.</summary>
	public bool NextTickStartsTurn => ParkCalendar.Turn( TickCount + 1 + turnPhase ) != ParkCalendar.Turn( TickCount + turnPhase );

	private readonly HashSet<int> sweptGuests = new();
	private float sweepDelta;

	/// <summary>
	/// The start of a tick that begins a park turn the host sweeps thing by thing: everything <see cref="Tick"/> does except the arrival update, the guests' updates and their removal,
	/// which the host calls (<see cref="SweepGuest"/>, <see cref="FinishSweep"/>, <see cref="UpdateTurnEndArrivals"/>) at the places the original's turn takes them.
	/// </summary>
	public void BeginSweepTick( float deltaSeconds )
	{
		if ( !NextTickStartsTurn )
			throw new InvalidOperationException( "A swept tick starts a park turn." );
		BeginTick( deltaSeconds );
		SyncOccupants();
		sweptGuests.Clear();
		sweepDelta = deltaSeconds;
	}

	/// <summary>
	/// One guest's update in the thing sweep (0x00501650, then 0x005019F0 for the thing); a guest that leaves asks for its destruction at once, as the original's does in its own update.
	/// False for an id that is no guest in the park.
	/// </summary>
	public bool SweepGuest( int id )
	{
		if ( !byId.TryGetValue( id, out var guest ) || !sweptGuests.Add( id ) )
			return false;
		UpdateGuest( guest, sweepDelta );
		if ( guest.State == GuestState.Gone )
			RemoveGone( guest );
		return true;
	}

	/// <summary>Updates the guests the sweep did not reach (one outside the pool's list, a headless scene's), in list order, then forgets the sweep.</summary>
	public void FinishSweep()
	{
		foreach ( var guest in guests.ToArray() )
			if ( !things.IsQueued( guest.Id ) )
				SweepGuest( guest.Id );
		sweptGuests.Clear();
		Observer?.Ticked( this, sweepDelta );
	}

	/// <summary>
	/// A group of arriving guests starts, with its size (the guests it will drop off: 0 in a lost park): the advisor keeps the last five sizes.
	/// </summary>
	// [BIN:TPW-EXE:0x004CF3E0 arrival group start] the call that starts a group (the timer test, 0x004CF5B0 giving the size, the log "Bus about to arrive with %d people") pushes the size into the five-entry ring at park +0x216D8 (0x004CF42A-0x004CF451), which the advisor's callbacks 0x59F220, 0x59F440 and 0x5A0780 read
	public event Action<int>? ArrivalGroupStarted;

	/// <summary>The arrival update at the world turn's tail (0x004CF3E0 through 0x004D7B20, after the destroys of the turn): a group starts, a guest steps off the vehicle.</summary>
	public void UpdateTurnEndArrivals() => UpdateArrivals( sweepDelta );

	/// <summary>
	/// The original's arrivals: when more than <see cref="GuestSettings.ArrivalTimeBetween"/> timer units (four park turns each)
	/// have passed since the last group was dropped off, a new group of <see cref="ArrivalGroupSize"/> guests arrives; from the
	/// next park turn on one guest steps off per turn, and the timer restarts at the first turn the group is empty.
	/// </summary>
	// [BIN:STP-PPC:0x100CB2AC arrival update] if no group is active and 0x101210F8's (turn >> 2) − (start >> 2) exceeds Arrival.TimeBetweenArrivals (settings +0x2D4), the group size is set and the group starts; at each stop one guest is created and the count falls; at 0 the timer restarts (0x10121098)
	// [BIN:TPW-EXE:0x004CF3E0 arrival update] runs once per park turn at the end of the world turn update 0x00516380 (through 0x004D7B20); the call that starts a group only sends the bus (0x0051A2F0 with size 1, 2 or 3 for groups under 36, up to 60, larger); while the bus is at its stop (vehicle state 2) each call creates one guest (0x004CF720) and counts the group down, and the call that finds it empty restarts the timer (0x0041A960) and sends the bus away
	// Without ArrivalVehicles (a park without vehicle definitions, which the original refuses with a fatal error, 0x0051A601) the
	// group's guests step off from the turn after it starts, as if the vehicle stood at its stop.
	private void UpdateArrivals( float deltaSeconds )
	{
		if ( !ArrivalsEnabled )
			return;
		if ( ArrivalVehicles != null )
		{
			if ( turnStarted )
				UpdateArrivalVehicle();
			return;
		}
		var turn = ArrivalClock?.Invoke() ?? ParkTurn;
		// A save loaded from an earlier turn replaces the clock: the timer starts unset again, as in a freshly started park.
		if ( turn < arrivalTimerStart )
			arrivalTimerStart = 0;
		var held = ArrivalHeldForCheat?.Invoke() == true;
		if ( !deliveringArrivals && !held && (turn >> 2) - (arrivalTimerStart >> 2) > Settings.ArrivalTimeBetween )
		{
			pendingArrivals = ArrivalGroupSize();
			ArrivalGroupStarted?.Invoke( pendingArrivals );
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
		// [BIN:TPW-EXE:0x004CF3E0 arrivals counter] the analyser's running arrivals (+0x20CC0) rise by one for every step of the group, after the guest is created and the group counts down (the park information window's arrival series)
		Payments?.ArrivalCreated();
	}

	/// <summary>
	/// The arrival update with the park's vehicles, once per park turn (tp.exe <c>0x004CF3E0</c>): a group that starts dispatches the
	/// vehicle its size calls for; while the vehicle reports status 2 (at its stop) each turn one guest steps off, and the turn that
	/// finds the group empty restarts the timer and sends the vehicle on. Leaving guests waiting at the stop call a vehicle (any kind,
	/// one world draw) when none is at work and hold one at status 4 until the last has boarded.
	/// </summary>
	// [BIN:TPW-EXE:0x004CF3E0 arrival update] with no group active the timer test starts one (0x004CF5B0, "Bus about to arrive with %d people"); with a group active
	// the vehicle's status (0x0051A690) decides: -1 (none at work) dispatches by group size (0x0051A2F0 with 1 up to 35 guests, 2 up to 60, else 3, signed
	// compares at 0x004CF48C/0x004CF49A), 2 creates one guest (0x004CF720) while the group is above 0 (signed, jle at 0x004CF56B) and counts it down, else
	// restarts the timer (0x0041A960), clears the active byte and dispatches with 0; the status 6 branch (0x004CF475) is unreachable, 0x0051A690 turning 6 into -1
	// [BIN:TPW-EXE:0x004CF4B6 pick-up] then, when a leaving guest waits at the stop (0x0051A9D0), status -1 or 0, or 2 with the group exactly 0 (test at
	// 0x004CF4E1), dispatches with 0; with none waiting, status 4 does
	// [APPROX:ARRIVAL-001] a cell's thing list holds OpenTPW's guests only (WALK-003); a swept turn runs this update after the thing sweep and the turn's destroys, as the original does (the end of 0x516380, Level.RunThingSweep); original scheduler import supports only saves without cached/current vehicle handles; saved vehicle identity and script continuation remain unsupported
	private void UpdateArrivalVehicle()
	{
		var turn = ArrivalClock?.Invoke() ?? ParkTurn;
		if ( turn < arrivalTimerStart )
			arrivalTimerStart = 0;
		// [EXT:cheats-arrival] a waiting arrival cheat holds the natural group and the pick-ups below
		var held = ArrivalHeldForCheat?.Invoke() == true;
		if ( !deliveringArrivals && !held && (turn >> 2) - (arrivalTimerStart >> 2) > Settings.ArrivalTimeBetween )
		{
			pendingArrivals = ArrivalGroupSize();
			ArrivalGroupStarted?.Invoke( pendingArrivals );
			deliveringArrivals = true;
		}
		if ( deliveringArrivals )
		{
			switch ( ReadArrivalVehicle() )
			{
				case -1:
					DispatchArrivalVehicle( pendingArrivals <= 35 ? 1 : pendingArrivals <= 60 ? 2 : 3 );
					break;
				case 2 when pendingArrivals > 0:
					if ( guests.Count < Settings.MaximumGuests )
						SpawnArrival();
					pendingArrivals--;
					// [BIN:TPW-EXE:0x004CF3E0 arrivals counter] +0x20CC0 rises by one after the guest is created and the group counts down
					Payments?.ArrivalCreated();
					break;
				case 2:
					arrivalTimerStart = turn;
					deliveringArrivals = false;
					DispatchArrivalVehicle( 0 );
					break;
			}
		}
		if ( LeavingGuestWaits() )
		{
			var status = ReadArrivalVehicle();
			// [EXT:cheats-arrival] a held arrival calls no vehicle, but one standing at the stop is still sent on with the leaving guests
			if ( (status is -1 or 0) && !held || status == 2 && pendingArrivals == 0 )
				DispatchArrivalVehicle( 0 );
		}
		else if ( ReadArrivalVehicle() == 4 )
			DispatchArrivalVehicle( 0 );
	}

	/// <summary>[EXT:cheats-arrival] The group sizes each vehicle stands for, by <see cref="ArrivalVehicleKind"/>: the bus up to 35, the seaplane 36 to 60, the Ferry 61 or more (the dispatch's own bands, 0x004CF48C/0x004CF49A).</summary>
	public static (int Low, int High) CheatArrivalBand( int vehicleKind ) => vehicleKind switch
	{
		ArrivalVehicleKind.Bus => (1, 35),
		ArrivalVehicleKind.Seaplane => (36, 60),
		ArrivalVehicleKind.Ferry => (61, int.MaxValue),
		_ => throw new ArgumentOutOfRangeException( nameof( vehicleKind ), vehicleKind, "Not an arrival vehicle." )
	};

	/// <summary>
	/// [EXT:cheats-arrival] The arrival cheat: a group starts now and comes by <paramref name="vehicleKind"/>. Its size is the next natural group's (no draw) moved into the vehicle's band
	/// (<see cref="CheatArrivalBand"/>), then cut by the park limit as every group is; a lost park's group is empty. The group is delivered like a natural one: the advisor's ring sees its size, the
	/// vehicle stops and one guest steps off per turn, and the turn that finds it empty restarts the arrival timer, so the next natural group comes a full interval later. False, and nothing
	/// changes, while a group is being delivered or a vehicle is at work.
	/// </summary>
	public bool TryStartCheatArrival( int vehicleKind )
	{
		var (low, high) = CheatArrivalBand( vehicleKind );
		if ( !ArrivalsEnabled || deliveringArrivals || arrivalVehicle >= 0 )
			return false;
		pendingArrivals = ArrivalsHalted?.Invoke() == true ? 0 : CutToParkLimit( Math.Clamp( UncutArrivalGroupSize(), low, high ) );
		ArrivalGroupStarted?.Invoke( pendingArrivals );
		deliveringArrivals = true;
		// the vehicle of the cheat's choice, not the one the size would call (sizes 1, 2 and 3 of the dispatch are the bus, the seaplane and the Ferry)
		DispatchArrivalVehicle( vehicleKind + 1 );
		return true;
	}

	/// <summary>Puts <paramref name="kind"/> at work without a dispatch (tests).</summary>
	internal void RestoreArrivalVehicleForTest( int kind ) => arrivalVehicle = kind;

	/// <summary>The status of the vehicle at work (script variable 1), -1 for none; a vehicle that reports 6 (gone) is released.</summary>
	// [BIN:TPW-EXE:0x0051A690 vehicle status] no vehicle at work (+0x1DA72A 0) reads -1; else the script's variable 1 (0x0055A390); 6 writes 0 to it
	// (0x0055A0B0), clears +0x1DA72A and reads -1
	private int ReadArrivalVehicle()
	{
		if ( arrivalVehicle < 0 || ArrivalVehicles is not { } host )
			return -1;
		// A save whose vehicle object is not in the park (hand-edited, or a level without it) would otherwise hold the group for good.
		if ( !host.Exists( arrivalVehicle ) )
		{
			arrivalVehicle = -1;
			return -1;
		}
		var status = host.GetVariable( arrivalVehicle, ArrivalVehicleKind.StatusVariable );
		if ( status != 6 )
			return status;
		host.SetVariable( arrivalVehicle, ArrivalVehicleKind.StatusVariable, 0 );
		arrivalVehicle = -1;
		return -1;
	}

	/// <summary>
	/// Sends a vehicle (tp.exe <c>0x0051A2F0</c>): the one at work gets its trigger set; else size 1, 2 or 3 picks the bus, seaplane or
	/// Ferry and size 0 one at random from the world generator. A kind the park already holds gets its trigger set, a new one is
	/// placed with status 1; a kind the level lacks falls back to the Ferry, then the seaplane, then the bus.
	/// </summary>
	// [BIN:TPW-EXE:0x0051A2F0 vehicle dispatch] with +0x1DA72A set only variable 0 = 1 (0x0051A649); else kind = size − 1, or for size 0 |rotr(s·0x19660D +
	// 0x3C6EF35F, 13)| mod 3 on the world generator's state +0x1DA708 (0x0051A34B, the step of 0x00516330); a cached handle (+0x1DA72C/E/30) gets variable
	// 0 = 1, else the first definition of that name (0x00412E60/0x00412E70 over 0x007890A0, _wcsicmp 0x0067B140) is created (0x004DB090) with variable 1 = 1
	// (0x0051A59B); a missing one logs "Could not find the 'vehicle_name was here' feature" and tries the untried kinds Ferry, seaplane, bus (bit tests 4, 2,
	// 1 from 0x0051A442) before the fatal "Could not find either the bus or the plane feature" (0x0051A601); the chosen handle becomes +0x1DA72A
	private void DispatchArrivalVehicle( int size )
	{
		if ( ArrivalVehicles is not { } host )
			return;
		if ( arrivalVehicle >= 0 )
		{
			host.SetVariable( arrivalVehicle, ArrivalVehicleKind.TriggerVariable, 1 );
			return;
		}
		var tried = 0;
		while ( true )
		{
			int kind;
			if ( tried == 0 )
				kind = size == 0 ? (int)(random.NextWord() % 3) : size - 1;
			else if ( (tried & 4) == 0 )
				kind = ArrivalVehicleKind.Ferry;
			else if ( (tried & 2) == 0 )
				kind = ArrivalVehicleKind.Seaplane;
			else if ( (tried & 1) == 0 )
				kind = ArrivalVehicleKind.Bus;
			else
			{
				Log?.Warning( "No arrival vehicle: the level has neither the bus, the seaplane nor the Ferry." );
				return;
			}
			tried |= 1 << kind;
			if ( host.Exists( kind ) )
				host.SetVariable( kind, ArrivalVehicleKind.TriggerVariable, 1 );
			else if ( host.Create( kind ) )
				host.SetVariable( kind, ArrivalVehicleKind.StatusVariable, 1 );
			else
				continue;
			arrivalVehicle = kind;
			return;
		}
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
		return CutToParkLimit( UncutArrivalGroupSize() );
	}

	/// <summary>The next group's size before the park limit's cut (<see cref="ArrivalGroupSize"/>): the weather factor, the park's points and the minimum.</summary>
	private int UncutArrivalGroupSize()
	{
		var perVisitor = Settings.ArrivalPointsPerVisitor < 2 ? 1 : Settings.ArrivalPointsPerVisitor;
		int count;
		if ( Settings.ArrivalWeatherPercentages is { } percentages )
		{
			// [BIN:TPI-EXE:0x005BCD60 arrival group] Theme Park Inc's group: 0 in world state 4; else ftol( weather percentage (rain, storm, snow or heatwave by the weather thing's state, else 100.0) x 0.01f x (NewParkBonus + park points) / PointsPerVisitor ), the factor and the divide in floating point (the oracle's X7 agrees on 532 of 532 inputs read at 53 bits); then at least MinPeople and the same cut as tp.exe's (500 online, else 1500)
			// [APPROX:ARRIVAL-008] OpenTPW runs no Theme Park Inc weather (its weather thing's state word +0x70 is not traced), so the percentage is always 100 and the rain, storm, snow and heatwave modifiers never apply; the routine's other exit (a mode object answering 3, 0x005BCD7A) has no OpenTPW counterpart — evidence needed: the weather thing's states and what sets mode 3
			var state = ArrivalWeatherState?.Invoke() ?? 0;
			double percent = state is >= 1 and <= 4 ? percentages[WeatherStateIndex[state - 1]] : 100.0;
			var points = unchecked( Settings.ArrivalNewParkBonus + (ParkPoints?.Invoke() ?? 0) );
			var scaled = (double)points * (percent * (double)0.01f) / perVisitor;
			count = Math.Abs( scaled ) < 2147483648.0 ? (int)scaled : int.MinValue;
		}
		else
		{
			var factor = WeatherDropsFalling?.Invoke() == true ? 0.8f : 1.2f;
			count = (int)(factor * (float)((ParkPoints?.Invoke() ?? 0) + Settings.ArrivalNewParkBonus)) / perVisitor;
		}
		return Math.Max( count, Settings.ArrivalMinPeople );
	}

	/// <summary>A group of <paramref name="count"/> cut so the counted guests plus the group stay within the park limit (<see cref="ArrivalParkLimit"/>, <see cref="OnlineArrivalParkLimit"/> for an online visit).</summary>
	private int CutToParkLimit( int count )
	{
		var limit = OnlineVisit ? OnlineArrivalParkLimit : ArrivalParkLimit;
		if ( (uint)(guests.Count + count) > (uint)limit )   // compared unsigned (jbe at 0x004CF690 and 0x004CF6EE)
			// [BIN:TPW-EXE:0x004CF6E5 no floor] the cut group is the limit minus the counted guests with no clamp (sub esi,ebx at 0x004CF704), negative above the limit; the stop's signed test emits no guest (jle 0x004CF56B: timer restart, active byte cleared), the vehicle size selects a bus (jg 0x004CF48F), and the boarding gate 0x0051A760 takes its below-10 branch (cmp 0xA, jge at 0x0051A844). The arrival ring stores the signed value unchanged (0x004CF451); the advisor's hype callback compares and divides signed (0x0059F31C/0x0059F32A), so a negative newest group can give a drop above 100 percent
			count = limit - guests.Count;
		return count;
	}

	public bool IsInPark( Guest guest ) => InPark( guest );

	/// <summary>Whether the guest stands on a park cell: a cell of type 0, 1, 3, 9 or 10 (the walkways outside the terrain, type 0x1E, are none).</summary>
	// [BIN:TPW-EXE:0x004FA990 park cell test] the type of the cell the guest's thing stands on (+5, +7) is 0, 1, 3, 9 or 10 (0x00536390, 0x00536310, 0x00536320, 0x00536340, 0x00536350), whatever the guest's state
	public bool IsOnParkCell( Guest guest )
	{
		var (x, y) = guest.Navigation.Cell;
		return Grid.InBounds( x, y ) && (Grid.Cells.RawTypeAt( x, y ) is 0 or 1 or 3 or 9 or 10 || !guest.InSwimSession && Grid.Cells.PoolBridges?.Covers( x, y ) == true);
	}

	/// <summary>
	/// [EXT:pools-swim] Whether a guest is counted as standing in the park for the statistics that count by cell (the thoughts ranking, the balloon and costume shares): on a park cell, or in a swim session,
	/// whose water is no park cell type. The thought pick and the leaving recall keep <see cref="IsOnParkCell"/>: a swimmer neither thinks nor leaves.
	/// </summary>
	public bool IsCountedInPark( Guest guest ) => guest.InSwimSession || IsOnParkCell( guest );

	private bool InPark( Guest guest ) => guest.State is not (GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth
		or GuestState.WaitingAtBooth or GuestState.AtTicketBooth or GuestState.CrossingRoadHome or GuestState.WalkingToVehicle or GuestState.WaitingToGoHome or GuestState.Gone);

	/// <summary>
	/// Whether the guest's time in the park runs: from its creation on, through the road crossing and the booth to the road home, as the stamp
	/// <c>+0x1D4</c> (the world turn at the constructor) is never rewritten.
	/// </summary>
	private static bool CountsParkTime( Guest guest ) => guest.State != GuestState.Gone;

	private void UpdateGuest( Guest guest, float dt )
	{
		// [APPROX:GUEST-920] the elapsed seconds accumulate per step (display metadata) where tp.exe subtracts two integer world turns (0x4FD950: the turn at +0x1DA70C minus the stamp +0x1D4, >> 2); the turn count a visitor shows is the seconds over the turn's 248 ms, floored
		if ( CountsParkTime( guest ) && guest.TimeInParkSeconds is { } elapsed )
			guest.TimeInParkSeconds = elapsed + dt;
		if ( turnStarted )
			UpdateSpeed( guest );
		if ( turnStarted && bodySpriteHostRunning )
			guest.BodySprite.ApplyPending();
		if ( turnStarted )
		{
			UpdateNeedsTurn( guest );
			// The leaving test itself runs in state 6 (UpdateDeciding). Closing the park pulls nobody out of a queue or a booking here.
			// [BIN:TPW-EXE:0x00519EF0 park door closes the rides] the door closes every openable object (0x004DF300: open word 0, loading slot cleared; ParkGate.DoorMoved), and a closed ride's update dismisses its queue head, one per turn (RideVisitorBridge.HostStep, 0x004E1220 → 0x004E0450 → 0x005012F0), after which state 6 sends the guest home; a guest walking to a ride drops its target in state 10 (UpdateGoingToRide, 0x004FFEB9)
			// [APPROX:GUEST-011] the close 0x4DF300 clears the call of a guest on its way to the stand point at once, where OpenTPW withdraws it at the ride's next update (the head dismissal); shops and stalls carry the openable flag 4 of +0x32 (type record word +0x3C, Info.IsChoosable, 1 in the four Shops.sam)
			// The state-18 recall (0xedc98): the leaving handler's opening test sends a guest whose counter is above 0, happiness byte nonzero and park
			// open back to deciding, so a closed-park dismissal unwinds when the park reopens. The three leaving causes all fail it in the same turn.
			// [BIN:TPW-EXE:0x00500A50 recall cell test] the recall also demands the guest's own cell pass 0x4FA990 (cell types 0, 1, 3, 9 and 10, 0x536310..0x536390, the set of ParkGate.CountsCellType): the walk out of the park crosses the gate's walkway cells (type 0x1E, 7 outside the terrain) and a guest on one cannot change its mind; the booth refusal and a closed park's state 3 enter state 18 with a zeroed counter (0x4FF9D0, 0x4FF7F0) and are never recalled
			if ( guest.State == GuestState.LeavingPark && GuestLifecycle.ShouldRecall( guest.Happiness, guest.ExitLevel, Payments?.ParkClosed ?? false )
				&& IsOnParkCell( guest ) )
			{
				// [BIN:TPW-EXE:0x00500AB9 recall clears the walk mask] guest +0x188 (walk +0xB4) = 0 before state 6
				guest.Navigation.Mask = 0;
				guest.ExitSide = null;
				EnterGuestState( guest, GuestState.DoingNothing );
				Observer?.Recalled( guest );
			}
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
	// [EXT:online-bubbles-on] bubbles are on in a visit too, where 0x50BE80 skips them in an online game (GameType word 0xFB3B7C = 1; none of its 22 callers passes the override): a visit is OpenTPW's read-only extension and shows the park as its owner sees it
	// [APPROX:GUEST-006] the environment sources, the thought pick 0x4FC8A0, the setters of 0x50BE80, the bubble's 12 turns and its cooldown by priority follow tp.exe, but the pick's voice (0x4FAA00, [0x803A24] event 0x85 for thought 4 when the thing id's low nibble is 0) is not played; bubbles are always on where 0x50BE80 skips them while bits 0x16 of 0x790AB0 are set; a show's row 7 comes off once (FireworksSpent) where 0x4E0B90 removes it whenever VAR_STATUS is 1, a variable native code also writes (0x4E0900, 0x4E0AC0); a load puts back the row an object's saved state implies — evidence needed: whether the original saves or rebuilds the environment table, and what bits 0x16 of 0x790AB0 mean
	private void UpdateNeedsTurn( Guest guest )
	{
		var turn = (uint)ParkTurn;
		// [BIN:TPI-EXE:0x611334 umbrella switch] the switch on the state reads it once at the top of the rule: only a guest who is already open (4) when the rule starts gets the happiness modifier, never one the step below opens (1 to 4 at 0x6111E3, a due 3 at 0x611206)
		var umbrellaWasOpen = guest.UmbrellaState == UmbrellaRules.StateOpen;
		// [EXT:umbrellas-extension] the umbrella rule runs first on the guest's own phase, as Theme Park Inc's weather rule opens its needs block; only a guest who owns an umbrella is touched
		if ( guest.UmbrellaState != UmbrellaRules.StateNone && NeedsUpdate.IsPhase( turn, guest.Id ) )
			UpdateUmbrella( guest, turn );
		// [EXT:rain-mood-extension] the mood rule follows the umbrella step on the same phase, for every guest in every state (Theme Park Inc's rule runs for all of them); off unless the extension sets the settings
		if ( RainMood != null && NeedsUpdate.IsPhase( turn, guest.Id ) )
			UpdateRainMood( guest, RainMood, umbrellaWasOpen );
		// [EXT:winter-cold] the cold step right after the rain mood step, on the same phase gate
		if ( WinterCold != null && NeedsUpdate.IsPhase( turn, guest.Id ) )
			UpdateWinterCold( guest, WinterCold );
		var cell = Litter?.Environment.At( guest.CellX, guest.CellY ) ?? default;
		var result = NeedsUpdate.Apply( new GuestMeters( guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea ), guest.ExitLevel, turn, guest.Id,
			cell.Happiness, cell.Illness, cell.Hunger );
		(guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea) = result.Meters;
		guest.ExitLevel = result.ExitLevel;
		if ( result.ToiletSpeedTerm is { } term )
			guest.ToiletBonus = UmbrellaRules.SpeedTerm( guest.UmbrellaState, term );
		NeedsUpdate.DecayHistory( guest.TemporaryHistory, turn );
		if ( !NeedsUpdate.IsPhase( turn, guest.Id ) )
			return;
		if ( guest.State is not (GuestState.Using or GuestState.HeldByGuard) && IsOnParkCell( guest ) )
		{
			// [BIN:TPW-EXE:0x00501919 thought pick] outside states 16 and 17 and on a park cell (0x004FA990), a world draw modulo 10 of 0 runs the pick 0x004FC8A0 (the draw is the park's one generator's, taken whenever the condition holds)
			if ( random.NextWord() % GuestThoughts.PickOdds == 0
				&& GuestThoughts.Pick( guest.Hunger, guest.Thirst, guest.Toilet, guest.Nausea, guest.Happiness, () => MessyShare( guest ) ) is { } thought )
			{
				Think( guest, thought );
				// [BIN:TPW-EXE:0x4fca3e thought voice] the pick's tail plays a voice for the thoughts 4, 13 and 14 on one thing id in sixteen (table 0x4fca94, index = thought - 4): the need-toilet thought (4) is cat_kids 0x85 (0x4fca6f); 13 is 0x7E and 14 is 0x7F, which this pick does not return
				if ( thought == GuestThought.NeedToilet && VoiceGate( guest ) )
					Voiced?.Invoke( guest, ToiletThoughtVoiceEvent );
			}
			// [BIN:TPW-EXE:0x00501932 balloon life] then +0x214 loses 1 when it is above 0, and at 0 the balloon goes (0x004FE950)
			if ( guest.BalloonLife > 0 && --guest.BalloonLife == 0 )
				BurstBalloon( guest );
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
				UpdateStepOff( guest );
				break;
			case GuestState.CrossingRoadToPark:
				UpdateWaitingToWalk( guest );
				break;
			case GuestState.GoingToTicketBooth:
				UpdateWalkToBooth( guest );
				break;
			case GuestState.WaitingAtBooth:
				UpdateWaitingAtBooth( guest );
				break;
			case GuestState.EnteringPark:
				UpdateEnteringPark( guest );
				break;
			case GuestState.CrossingRoadHome:
				UpdateAtGate( guest );
				break;
			case GuestState.WalkingToVehicle:
				UpdateWalkToVehicle( guest );
				break;
			case GuestState.LeavingPark:
				UpdateLeaving( guest, dt );
				break;
			case GuestState.AtTicketBooth:
				// [BIN:TPW-EXE:0x004FF9D0 booth wait] an expensive verdict stores N in +0x200, every later state-4 update counts it down by one without a verdict and the update after runs the verdict again: one count per executed park turn
				if ( guest.BoothWait > 0 )
				{
					if ( turnStarted && --guest.BoothWait == 0 )
						PayAdmission( guest );
					break;
				}
				// [BIN:TPW-EXE:0x004FF9D0 booth first verdict] the walk's arrival makes state 4 (0x004FF730) and the booth's update, the first verdict (the wait +0x200 is 0), runs in the guest's next turn
				if ( turnStarted )
					PayAdmission( guest );
				break;
			case GuestState.WaitingToGoHome:
				UpdateWaitingAtStop( guest, dt );
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
				UpdateExitingRide( guest, dt );
				break;
			// [EXT:pools-swim] the swim session (GuestSimulation.Swimming.cs)
			case GuestState.Swimming:
				UpdateSwimming( guest );
				break;
			case GuestState.ClimbingOut:
				UpdateClimbingOut( guest );
				break;
			case GuestState.ChangingOut:
				UpdateChangingOut( guest );
				break;
			case GuestState.PoolActivity:
				UpdatePoolActivity( guest );
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
		guest.BodySprite.DeletePlayer();
		// [BIN:TPW-EXE:0x005022DA state 17 entry] a held guest's balloon bursts (0x004FE950); its life stays; the thought bubble goes (0x0050C0D0)
		BurstBalloon( guest );
		guest.BubbleThought = GuestThought.None;
	}

	/// <summary>
	/// The guard sets the guest down on <paramref name="cell"/> (tp.exe <c>0x004FEB50</c>): the guest stands at the cell's centre and leaves the park
	/// (state 18) towards the gate cell <paramref name="side"/> drew when a route reaches it; otherwise it is removed. True when it walks out. Without a drawn
	/// side the first gate cell with a route is taken.
	/// </summary>
	// [BIN:TPW-EXE:0x004FEB50 guard sets a guest down] the guest at the cell's centre (0x004FA930 with the bytes 0x80, 0x80), its model back (0x004D4140), cat_kids 128 at it, +0x188 = 1, a route (0x004FA530) to the one gate cell a world draw's bit 0 picks (0x004D86D0: 0 CrossingParkSideA, 1 CrossingParkSideB's X with side A's Y): state 18 ("<Humph>"); no route destroys it (0x0050B780)
	// from the gate cell the guest goes on to the arrival vehicle's stop cells as states 19 to 21 do (UpdateAtGate)
	bool IGuardGuests.Release( int guestId, (int X, int Y) cell, int? side )
	{
		if ( Find( guestId ) is not { } guest )
			return false;
		if ( !Grid.IsWalkable( cell.X, cell.Y ) )
			cell = Grid.FindNearestWalkable( cell.X, cell.Y ) ?? cell;
		PlaceOnGrid( guest, cell.X, cell.Y );
		var navigation = guest.Navigation;
		(navigation.X, navigation.Y) = GuestNavigation.Centre( cell );
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		UpdatePresentation( guest );
		guest.BodySprite.DeletePlayer();
		CreateBodySprite( guest, VisitorSpriteTemplate.Walk );
		// [BIN:TPW-EXE:0x004FEBAB set-down walk mask] guest +0x188 (walk +0xB4) = 1: the walk out may leave the paths (WalkEdges.Blocked)
		navigation.Mask = 1;
		// [BIN:TPW-EXE:0x004FEB50 set-down route] one route request (0x004FA530) to the one gate cell the draw's bit 0 picks (0x004D86D0); a route sets state 18, none destroys the guest
		var reachable = false;
		foreach ( var candidate in side is { } drawn ? new[] { drawn & 1 } : new[] { 0, 1 } )
		{
			if ( GateCell( candidate ) is { } gate && RouteToCell( guest, gate ) )
			{
				guest.ExitSide = candidate;
				reachable = true;
				break;
			}
		}
		// The leaving handler's recall (0x00500A50) applies to it like any leaving guest: with its exit counter and happiness byte above 0, the
		// park open and its cell a path, it decides again on its next turn (and clears +0x188), so a guard sends home only a guest that wants to go.
		guest.State = reachable ? GuestState.LeavingPark : GuestState.Gone;
		if ( reachable )
			guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Walk );
		if ( reachable )
			Observer?.Leaving( guest, DepartureCause.Guard );
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
			if ( InPark( guest ) && guest.OnGrid && guest.State is not (GuestState.Using or GuestState.HeldByGuard or GuestState.ChangingOut) && guest.CellX >= x0 && guest.CellX <= x1
				&& guest.CellY >= y0 && guest.CellY <= y1 )
				return true;
		return false;
	}

	IEnumerable<GuestNavigation> IGuardGuests.WalkersOn( int x, int y )
	{
		if ( !occupants.TryGetValue( (x, y), out var list ) )
			yield break;
		foreach ( var other in list )
			if ( other.State is not (GuestState.Using or GuestState.HeldByGuard or GuestState.ChangingOut or GuestState.Gone) )
				yield return other.Navigation;
	}

	(int X, int Y)[]? IGuardGuests.Lane( int side ) => side == 0 ? ValidLane( Settings.ArrivalLaneA ) : ValidLane( Settings.ArrivalLaneB );

	// [BIN:TPW-EXE:0x004FD970 guest meter +0x1B8] the energy meter is the original's unnamed float +0x1B8: nothing changes it while walking, resting, queueing or riding (the needs phase 0x00501650 does not touch it), so OpenTPW keeps only its use step (OnVisitorReleased)
	private void UpdateNeeds( Guest guest, float dt )
	{
		if ( guest.State != GuestState.Using )
			guest.SecondsSinceRide += dt;
	}

	/// <summary>
	/// The walk step (tp.exe <c>0x4FA2A0</c>): one steering step on the tick that starts a park turn. True when the walk is done:
	/// arrived at its destination, or stuck (a route search of the walk failed, <see cref="WalkResult.Stuck"/>, which
	/// <see cref="GuestNavigation.Stuck"/> keeps until the next destination); the destination is then done.
	/// </summary>
	private bool Move( Guest guest ) => MoveStep( guest ) != WalkResult.Walking;

	/// <summary>The walk step with its result (<see cref="WalkResult"/>): still walking until the turn starts; any other result ends the walk.</summary>
	private WalkResult MoveStep( Guest guest )
	{
		if ( !turnStarted )
			return WalkResult.Walking;
		var result = Step( guest );
		if ( result != WalkResult.Walking )
			guest.HasWaypoint = false;
		return result;
	}

	/// <summary>
	/// A route request to a cell's centre (tp.exe <c>0x004FA530</c>: the bytes 0x80, 0x80, then SetDest <c>0x00510100</c>): the walk of the whole way, searched
	/// on the cell map from the guest's cell with its walk mask. False when the search fails; the walk then stands arrived and stuck
	/// (<see cref="GuestNavigation.Stuck"/>), as the original leaves it.
	/// </summary>
	// [BIN:TPW-EXE:0x004FA530 route to a cell] the cell id minus 1 gives the cell bytes with the sub-cell byte 0x80 (bit 15 of x cleared), the stranded mark +0x198 is tested first (RequestRoute), then 0x00510100 with the position bytes << 8; its result is the search's success
	private bool RouteToCell( Guest guest, (int X, int Y) cell )
	{
		return RequestRoute( guest, cell, null );
	}

	/// <summary>
	/// The route request <c>0x004FA530</c>: a guest marked stranded (+0x198) whose surroundings are still unstamped refuses at once and leaves its walk alone
	/// (0x004FA770); else the mark clears and the walk is set up to the cell's centre. <paramref name="route"/> is the M3 gate's bookkeeping.
	/// </summary>
	// [BIN:TPW-EXE:0x004FA530 stranded early exit] the stamp counter below the mark clears it (0x004D8C50 against +0x198); a mark still set and held (0x004FA770) returns 0 before anything is written; else the mark is cleared and 0x00510100 sets the walk up
	private bool RequestRoute( Guest guest, (int X, int Y) cell, List<(int X, int Y)>? route )
	{
		var navigation = guest.Navigation;
		if ( guest.StrandedMark != 0 && Grid.Cells.StampCounter + 1 < guest.StrandedMark )
			guest.StrandedMark = 0;
		if ( guest.StrandedMark != 0 && StrandedHolds( guest, (guest.CellX, guest.CellY) ) )
			return false;
		guest.StrandedMark = 0;
		var (x, y) = GuestNavigation.BytePoint( cell, GuestWander.CentreByte, GuestWander.CentreByte );
		var found = route == null ? navigation.Navigate( Grid.Cells, x, y ) : SetDestination( guest, (x, y), route );
		guest.HasWaypoint = true;
		return found;
	}

	private WalkResult Step( Guest guest )
	{
		var navigation = guest.Navigation;
		var (x, y, distance) = (guest.X, guest.Y, guest.DistanceWalked);
		var (beforeX, beforeY) = (navigation.X, navigation.Y);
		stepping = navigation;
		var arrived = navigation.Step( guest.MaxSpeed, guest.MaxForce, Grid.Cells, isOpenForStep, separationForStep );
		stepping = null;
		ObserveBodyWalk( guest, beforeX, beforeY, arrived );
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
	/// A cell is walkable for a route walk (<see cref="GuestNavigation.Step(int, int, Func{int, int, bool}, Func{GuestNavigation, (int X, int Y)}?)"/>,
	/// <c>[EXT:legacy-save-route-walk]</c>) when it is on its current walk: the route cells and, for a queue walk, the queue's next cells ahead of the destination and
	/// behind the start. A walk of the original kind asks <see cref="WalkEdges"/> instead.
	/// </summary>
	private bool IsOpenForStep( int x, int y ) => stepping!.IsOnWalk( x, y );

	/// <summary>
	/// separation's neighbours for the stepping guest (<c>0x10087c</c>): the guests and the staff standing in the 3 × 3 cells around its
	/// cell, rows from the top and each row from the left, cells outside the map skipped (<c>0xd70d8</c>).
	/// </summary>
	// [APPROX:WALK-003] separation's neighbours are the guests of each cell outside rides and shops (not Using, HeldByGuard or Gone), head first like the original's persistent chains (the guest that entered the cell last first), followed by the staff on the cell; the original's query (tp.exe 0x5102f0, Mac 0x10087c, at most five neighbours) walks one chain per cell that holds the guests whatever their state (class 1), the staff (classes 4-8, kept here) and the online avatar (class 18, not kept) interleaved by the time each entered; the neighbour position (thing +4/+6 = navigation position >> 8, written by 0x4fa2a0) matches OpenTPW's — still open: the entry order of the staff in the chain (they keep no cell list, so they follow the guests) and where the thing position of a guest on a ride, held or leaving lies
	private (int X, int Y) SeparationForStep( GuestNavigation self )
	{
		neighbours.Clear();
		var (cellX, cellY) = self.Cell;
		var staff = staffOnMap;
		for ( var y = cellY - 1; y <= cellY + 1; y++ )
		{
			for ( var x = cellX - 1; x <= cellX + 1; x++ )
			{
				if ( x < 0 || y < 0 || x >= Grid.CountX || y >= Grid.CountY )
					continue;
				if ( occupants.TryGetValue( (x, y), out var list ) )
				{
					// the cell's things are visited head first, the one that entered last (0x5102F0 follows the chain from cell +0x24 by +10; 0x4D91F0 inserts at the head)
					for ( var index = list.Count - 1; index >= 0; index-- )
					{
						var other = list[index];
						if ( other.State is not (GuestState.Using or GuestState.HeldByGuard or GuestState.Gone) )
							neighbours.Add( other.Navigation );
					}
				}
				// the staff on the cell follow its guests (classes 4 to 8 of the thing list 0x5102F0 visits; the staff sweep does the same with the guests, StaffWorldSimulation.StaffSeparation)
				if ( staff == null )
					continue;
				foreach ( var member in staff )
				{
					if ( member.Walk is { } walk && !ReferenceEquals( walk, self ) && walk.Cell == (x, y) )
						neighbours.Add( walk );
				}
			}
		}
		return GuestNavigation.Separation( self, neighbours );
	}

	/// <summary>Files the guests that are in no cell list yet, or in another cell's, under their cells; the lists keep the order the guests entered them in.</summary>
	// [BIN:TPW-EXE:0x004D91F0 thing list insert] the cell's thing chain is persistent and a thing that enters a cell goes to its head (cell +0x24, next +10, previous +8; 0x0050B6A0 removes it from the old cell, 0x004D9280, and inserts it into the new one on every cell change of its position), so the lists are never rebuilt; a list here ends with the head
	// [EXT:guest-cell-lists] OpenTPW keeps the guests of a cell in a list per cell (the staff are queried beside them); a cell change this simulation did not see (an edit that moves a guest) is filed at the next turn
	private void SyncOccupants()
	{
		foreach ( var guest in guests )
		{
			if ( guest.State != GuestState.Gone && guest.OccupiedCell != guest.Navigation.Cell )
				Occupy( guest );
		}
	}

	/// <summary>The guest that heads the thing list of a cell (the one that entered it last), or null (tests).</summary>
	internal Guest? HeadOfCell( int x, int y ) => occupants.TryGetValue( (x, y), out var list ) && list.Count > 0 ? list[^1] : null;

	/// <summary>Moves a guest to a path cell and files it in that cell's list as a step into it does (tests).</summary>
	internal void PlaceForTest( Guest guest, int x, int y )
	{
		PlaceOnGrid( guest, x, y );
		Occupy( guest );
	}

	/// <summary>Files every guest under its cell, oldest entry first (a restore: the saved entry numbers give the order).</summary>
	private void RebuildOccupants()
	{
		foreach ( var list in occupants.Values )
			list.Clear();
		var ranked = guests.Select( ( guest, index ) => (guest, index) ).OrderBy( item => item.guest.CellSerial ).ThenBy( item => item.index ).Select( item => item.guest );
		foreach ( var guest in ranked )
		{
			guest.OccupiedCell = null;
			var serial = guest.CellSerial;
			Occupy( guest );
			if ( serial != 0 )
				guest.CellSerial = serial;
		}
		cellSerial = Math.Max( cellSerial, guests.Count == 0 ? 0 : guests.Max( guest => guest.CellSerial ) );
	}

	/// <summary>Moves a guest to the end of its current cell's list when it changed cell.</summary>
	private void Occupy( Guest guest )
	{
		// [EXT:pools-swim] swimmers and guests changing back are in no walkers' cell list: the water is nobody's path
		if ( guest.InSwimSession )
		{
			LeaveCellLists( guest );
			return;
		}
		var cell = guest.Navigation.Cell;
		if ( guest.OccupiedCell == cell )
			return;
		if ( guest.OccupiedCell is { } previous && occupants.TryGetValue( previous, out var old ) )
			old.Remove( guest );
		if ( !occupants.TryGetValue( cell, out var list ) )
			occupants[cell] = list = new List<Guest>();
		list.Add( guest );
		guest.CellSerial = ++cellSerial;
		guest.OccupiedCell = cell;
	}

	/// <summary>
	/// Starts a walk to <paramref name="target"/>. On the park's cells this is the original's SetDest (<see cref="GuestNavigation.Navigate"/>:
	/// a route search from the guest's cell with its walk mask), and <paramref name="cells"/> (from the guest's own cell on) and
	/// <paramref name="alsoOpen"/> are bookkeeping for the M3 gate; off the grid (a guest of an old save's arrival lane) the guest walks the cells themselves.
	/// </summary>
	private bool SetDestination( Guest guest, (int X, int Y) target, List<(int X, int Y)> cells, List<(int X, int Y)>? alsoOpen = null )
	{
		var navigation = guest.Navigation;
		var route = new List<(int X, int Y)>( cells.Count + 1 ) { navigation.Cell };
		foreach ( var cell in cells )
		{
			if ( route[^1] != cell )
				route.Add( cell );
		}
		var found = true;
		if ( navigation.OffGrid )
			navigation.SetDestination( target.X, target.Y, route, alsoOpen );
		else
		{
			found = navigation.Navigate( Grid.Cells, target.X, target.Y, route, alsoOpen );
		}
		guest.HasWaypoint = true;
		return found;
	}

	/// <summary>
	/// The booth (state 4, <c>0x004FF9D0</c>): the verdict on the entrance fee (<see cref="BoothVerdict"/>) sends a guest home when the park is far too
	/// expensive, keeps one that finds it expensive at the booth for 50 + R % 50 park turns and weighs it again on the turn after, with a medium
	/// happiness loss (home once its happiness truncates to 0), and lets the others pay, a cheap park with a medium happiness gain. The booth has no
	/// park-closed test (a closed park stops guests on the walk to it, <see cref="ArriveAtBooth"/>) and never reads the guest's purse.
	/// </summary>
	// [BIN:TPW-EXE:0x4ff9d0 booth] verdict 0: thought 6 and home (state 18); 1: +0x200 = 50 + R % 50 (counted down one per update before the verdict runs again), thought 6, 0x4fea70(1), home when the happiness byte is 0; 2: 0x4fe9c0(1), then 3: the fee is booked (0x4d0600) and the guest enters (+0x204 = 1, state 3)
	// [BIN:TPW-EXE:0x004FF9D0 booth without closure test] the booth reads neither mParkClosed nor the bank's state: a guest that reached it while the park was open is weighed and pays even once the park has closed; the paid guest's state 3 (0x004FF7F0) then sends it home while the park is closed
	private void PayAdmission( Guest guest )
	{
		// the verdict's draw is the host's, taken first; the decision layer weighs the fee (BoothVerdict.Decide in the original)
		var boothContext = ContextNow();
		var boothGuest = ViewOf( guest );
		var booth = new BoothView( (uint)Math.Max( 0, ParkPoints?.Invoke() ?? 0 ), (uint)Math.Max( 0, AdmissionFee ), random.NextWord(), OpenAttractionCount() );
		var verdict = decisions.DecideBooth( boothContext, boothGuest, booth ).Verdict;
		if ( !Enum.IsDefined( verdict ) )
			throw PolicyError( $"returned the booth verdict {(int)verdict}" );
		// [BIN:TPW-EXE:0x004FF5B0 booth opinion] every verdict but the fair one of a park value and a fee both at or below the minimum records the opinion byte of its band in the park's perception ring 0, which the advisor's ticket price lines read
		if ( Payments != null && BoothVerdict.Opinion( verdict, Settings ) is { } opinion && BoothVerdict.RecordsOpinion( BoothVerdict.ParkValue( booth.ParkPoints, booth.Draw, Settings ), booth.Fee, Settings ) )
			Payments.PriceTested( BoothVerdict.OpinionCategory, opinion );
		if ( verdict == BoothVerdictKind.FarTooExpensive )
		{
			Think( guest, GuestThought.Dissatisfied );
			SendHome( guest, DepartureCause.BoothRefused );
			return;
		}
		if ( verdict == BoothVerdictKind.Expensive )
		{
			var wait = BoothVerdict.ExpensiveWaitTurns( random.NextWord() );
			Think( guest, GuestThought.Dissatisfied );
			guest.Happiness = MeterMath.Add( guest.Happiness, -Settings.MediumHappinessChange );
			if ( ((int)guest.Happiness & 0xFF) != 0 )
			{
				// [APPROX:ARRIVAL-006] the wait is counted in executed park turns, but OpenTPW's fixed-step loop drops none (the original's loop drops at most 3 per pass, 0x0054F680)
				guest.BoothWait = wait;
				return;
			}
			// [BIN:TPW-EXE:0x004FFA9D expensive verdict, happiness 0] the case falls to SetState(0x12) and the exit counter 0 with no mask and no route: the guest's finished walk makes state 19 where it stands
			BeginLeaving( guest, DepartureCause.BoothRefused );
			return;
		}
		if ( verdict == BoothVerdictKind.Cheap )
			guest.Happiness = MeterMath.Add( guest.Happiness, Settings.MediumHappinessChange );
		var fee = AdmissionFee;
		if ( Payments != null && !Payments.TryPayAdmission( out fee ) )
		{
			// A bankrupt park turns back with thought 6 as the booth's refusals do (the original's guests stop at bankruptcy instead, ECON-030).
			Think( guest, GuestThought.Dissatisfied );
			SendHome( guest, DepartureCause.BoothRefused );
			return;
		}
		// [BIN:TPW-EXE:0x004FFAD5 booth fee] the paying verdict books the fee with the bank deposit 0x004D0600 (ECX = the bank, 0x005195D0) and sets the guest's +0x204 and state 3; the guest's purse +0x1A0 is neither tested nor debited
		Admissions++;
		Observer?.Admitted( guest, fee );
		if ( fee > 0 )
		{
			Observer?.Spent( guest, fee, 0 );
			MoneySpent?.Invoke( guest, fee, 0 );
		}
		// [BIN:TPW-EXE:0x004FFAD5 paid] +0x204 = 1 and SetState(3): the wait +0x200 is a draw of 200 to 349 (0x00501E6E); state 3 (0x004FF7F0) sends a paid guest home while the park is closed or the gate's script variable 1 (0x0051A290, VAR_STATUS) is not 1, once the wait has run out, and lets it walk on to the entrance as the head of its cell's list otherwise
		guest.Paid = true;
		EnterWaitingAtBooth( guest );
	}

	/// <summary>
	/// The four cells around the arrival vehicle's stop (tp.exe <c>0x0051A9D0</c>, <c>0x00500AD0</c>, <c>0x00500BD0</c>): the stop cell <c>0x004D8650(0)</c> (the level's BusStopA cell),
	/// its +X neighbour, and the two cells two rows up from them; the bus uses the first two and the seaplane and the Ferry the last two. Null when the level has no bus stop A.
	/// </summary>
	// [BIN:TPW-EXE:0x00500AD0 stop cells] 0x004D8650(0) is cell (0x007855AC, 0x007855B0), FixedItemInfo.BusStopA; the four cells are that id, id + 1, id − 0x100 and id − 0xFF (two rows up, 128 cells a row)
	private (int X, int Y)[]? StopCells()
	{
		if ( ValidLane( Settings.ArrivalLaneA ) is not { } lane )
			return null;
		var (x, y) = lane[0];
		return Grid.InBounds( x, y ) && Grid.InBounds( x + 1, y - 2 ) ? [(x, y), (x + 1, y), (x, y - 2), (x + 1, y - 2)] : null;
	}

	/// <summary>The purpose speed (+0xC2, the guest's speed term) of a guest on its way to the ticket booth: 25 for one in four guests, 50 while the bus waits at its status 3, else 0.</summary>
	// [BIN:TPW-EXE:0x004FF730 booth walk speed] state 2 sets +0xC2 on every update, before its walk step: with a vehicle other than the bus at work (0x0051AAD0), or with none or the bus when the vehicle's status (0x0051A690) is not 3, it is the word 0x0075C7F2 (25) for a thing id with its low two bits 0 (0x004FF776) and 0x0075C7F0 (0) for the others; the bus at status 3 gives 0x0075C7F4 (50) (0x004FF753..0x004FF765)
	private int BoothWalkSpeedTerm( Guest guest )
	{
		if ( !NonBusVehicleAtWork() && ReadArrivalVehicle() == BusWaitingStatus )
			return BusWaitingSpeedTerm;
		return (guest.Id & 3) == 0 ? GuestSpeed.ToiletBonus : 0;
	}

	/// <summary>The vehicle status in which the bus gives a guest on its way to the booth the larger speed term.</summary>
	private const int BusWaitingStatus = 3;
	private const int BusWaitingSpeedTerm = 50;

	/// <summary>Whether the arrival vehicle at work is the seaplane or the Ferry (tp.exe <c>0x0051AAD0</c>: a vehicle at work that is not the bus).</summary>
	// [BIN:TPW-EXE:0x0051AAD0 non-bus vehicle] 1 when the vehicle at work (+0x1DA72A) is set and differs from the bus's handle (+0x1DA72C)
	private bool NonBusVehicleAtWork() => arrivalVehicle >= 0 && arrivalVehicle != ArrivalVehicleKind.Bus;

	/// <summary>
	/// Whether state 19 lets a leaving guest walk to the stop (tp.exe <c>0x0051A760</c>, taken as nonzero): not while the bus is at status 3, 1 or 2 (arriving or unloading);
	/// any other vehicle, no vehicle, or a bus in another status lets it. A level whose arrival group minimum is above 9 skips the status 1 and 2 tests.
	/// </summary>
	// [BIN:TPW-EXE:0x0051A760 stop walk gate] returns 1 for no vehicle at work and for a vehicle other than the bus; with the bus: status 3 returns 0, Arrival.MinPeople (+0x2D0) above 9 returns 1, then status 1 and status 2 return 0, any other status 1; each read is 0x0055A390 on the vehicle's script and a status 6 releases the vehicle (-1)
	private bool StopWalkAllowed()
	{
		if ( ArrivalVehicles == null || arrivalVehicle < 0 || arrivalVehicle != ArrivalVehicleKind.Bus )
			return true;
		if ( ReadArrivalVehicle() == 3 )
			return false;
		if ( Settings.ArrivalMinPeople > 9 )
			return true;
		var status = ReadArrivalVehicle();
		if ( status == 1 )
			return false;
		return ReadArrivalVehicle() != 2;
	}

	/// <summary>A route request to a random point of <paramref name="cell"/> (the two sub-cell bytes are world draws in tp.exe, <c>0x00516330</c>); false when the walk search fails.</summary>
	// [BIN:TPW-EXE:0x00500BD0 stop walk draws] the cell pick and the two sub-cell bytes (0x00500AD0, 0x00500BD0) are world draws (0x00516330), taken here from the park's one world generator (WorldRng) in the original's order: the pick, then x, then y
	private bool RouteToStopPoint( Guest guest, (int X, int Y) cell )
	{
		var byteX = (int)(random.NextWord() & 0xFF);
		var byteY = (int)(random.NextWord() & 0xFF);
		var (x, y) = GuestNavigation.BytePoint( cell, byteX, byteY );
		var found = guest.Navigation.Navigate( Grid.Cells, x, y );
		guest.HasWaypoint = true;
		return found;
	}

	/// <summary>
	/// State 19 (tp.exe <c>0x00500AD0</c>), at the gate's crossing cell: once the arrival vehicle lets a leaving guest walk (<see cref="StopWalkAllowed"/>) it picks one of the four stop
	/// cells with a world draw, a random point in it with two more, asks for a route there and walks it (state 20); a stop cell no route reaches leaves the walk stuck in state 20.
	/// </summary>
	// [BIN:TPW-EXE:0x00500AD0 state 19] 0x0051A760 nonzero: the cell local_8[draw & 3] (the stop cell, its +X neighbour and the two cells two rows up), the point's bytes from two more draws, 0x004FA5F0 and SetState(0x14); the route request's result is not read
	private void UpdateAtGate( Guest guest )
	{
		if ( !turnStarted || !StopWalkAllowed() )
			return;
		var cells = StopCells();
		if ( cells == null )
		{
			guest.State = GuestState.Gone;
			return;
		}
		var cell = cells[(int)(random.NextWord() & 3)];
		RouteToStopPoint( guest, cell );
		EnterGuestState( guest, GuestState.WalkingToVehicle );
	}

	/// <summary>Sets state 21 (the guest waits at the stop to leave) and, for a guest that paid, stores its stay in the analyser's stay ring.</summary>
	// [BIN:TPW-EXE:0x00502333 state 21] the state setter's case 0x15 stores the stay (0x004C7600: the turns since the arrival stamp 0x004FD950, in whole four-turn steps, as days) when the guest's paid flag +0x204 is set, then sets the thing's state 3 (0x004217F0)
	// [APPROX:UI-914] the stay comes from the visitor's elapsed simulated seconds (GUEST-920), where the original subtracts two integer world turns (the arrival stamp +0x1D4, 0x4FD950)
	private void EnterWaitingToGoHome( Guest guest )
	{
		EnterGuestState( guest, GuestState.WaitingToGoHome );
		if ( guest.Paid && guest.TimeInParkSeconds is { } elapsed )
			Payments?.StayEnded( ParkStatisticsHistory.StayDays( (long)Math.Floor( elapsed * 1000 / ParkCalendar.TurnMilliseconds ) ) );
	}

	/// <summary>State 20 (tp.exe <c>0x005019F0</c> case 0x14): one step of the walk to the stop; arriving is state 21; a stuck walk stays where it is.</summary>
	// [BIN:TPW-EXE:0x005019F0 state 20] the walk step 0x004FA2A0: 0 sets the heading (0x7FF when the vehicle at work is not the bus, 0x400 otherwise) and SetState(0x15); 2 logs "Got stuck walking around outside" and stays
	private void UpdateWalkToVehicle( Guest guest )
	{
		if ( !guest.HasWaypoint )
		{
			EnterWaitingToGoHome( guest );
			return;
		}
		var result = MoveAlongStep( guest );
		if ( result == WalkResult.Arrived )
		{
			EnterWaitingToGoHome( guest );
			guest.StateTimer = WaitForBusSeconds;
		}
		else if ( result == WalkResult.Stuck )
			guest.HasWaypoint = true;   // the original logs and stays in state 20 (0x005019F0 case 0x14); the walk keeps reporting stuck
	}

	/// <summary>
	/// State 21 on the cell map (tp.exe <c>0x00500BD0</c>): at vehicle status 4 the guest that heads its cell's thing list boards (and is gone); at status 1 to 3 a guest that
	/// does not stand on one of the vehicle kind's two stop cells picks one and walks to a random point of it (state 20). Without the park's vehicles the guest goes after
	/// <see cref="WaitForBusSeconds"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x00500BD0 state 21] status 4: the head thing of the guest's cell (0x004CD4E0, the cell's list at +0x24) is this guest: event 0x13 and 0x0050B780; status 1, 2 or 3: on the bus's stop cells (0x0051AAD0 = 0) local_8[0] or [1], else [2] or [3] it returns, else a draw picks one of the two, two more draws give the point, 0x004FA5F0 and SetState(0x14)
	private void UpdateWaitingAtStop( Guest guest, float dt )
	{
		if ( ArrivalVehicles == null )
		{
			guest.StateTimer -= dt;
			if ( guest.StateTimer <= 0 )
				guest.State = GuestState.Gone;
			return;
		}
		if ( !turnStarted )
			return;
		var status = ReadArrivalVehicle();
		var here = guest.Navigation.Cell;
		if ( status == 4 )
		{
			if ( occupants.TryGetValue( here, out var list ) && list.Count > 0 && list[^1] == guest )
				guest.State = GuestState.Gone;
			return;
		}
		if ( status is not (1 or 2 or 3) || StopCells() is not { } cells )
			return;
		var offset = NonBusVehicleAtWork() ? 2 : 0;
		if ( here == cells[offset] || here == cells[offset + 1] )
			return;
		var cell = cells[(int)(random.NextWord() & 1) + offset];
		RouteToStopPoint( guest, cell );
		EnterGuestState( guest, GuestState.WalkingToVehicle );
	}

	/// <summary>Whether a leaving guest waits at the stop (tp.exe <c>0x0051A9D0</c>): the head thing of one of the four stop cells is a guest in state 21.</summary>
	// [BIN:TPW-EXE:0x0051A9D0 pick-up test] for the four cells of 0x00500AD0 the head of the cell's thing list (+0x2B8 + id × 0x44) must be a guest (type byte 1) in state 0x15
	private bool LeavingGuestWaits()
	{
		if ( StopCells() is not { } cells )
			return false;
		foreach ( var cell in cells )
		{
			if ( occupants.TryGetValue( cell, out var list ) && list.Count > 0 && list[^1] is { State: GuestState.WaitingToGoHome } )
				return true;
		}
		return false;
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
			{
				guest.Activity = GuestActivity.None;
				guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Stand );
			}
			return;
		}
		IRideVisitorBridge? choice = null;
		var draw = random.NextWord();
		var input = new GuestDecision.Input( turn, (uint)guest.InterludeTurn, (uint)guest.QueueStandingSinceTurn, guest.Happiness, guest.Nausea, guest.Litter,
			guest.ExitLevel, Payments?.ParkClosed ?? false, draw, guest.PrankFlag, Settings.StinkbombLikelihood );
		var outcome = GuestDecision.Decide( input, () =>
		{
			if ( SeekBin( guest ) )
				return true;
			DropLitter( guest, (LitterType)(1 + random.NextWord() % ParkLitter.OrdinaryKinds) );
			guest.Litter = 0;
			return false;
		}, () =>
		{
			// the leaving test's three predicates, read before the gate takes its happiness (see GuestLifecycle.ShouldLeave)
			var cause = Observer == null ? DepartureCause.None : LeavingPredicates( guest );
			guest.Happiness = MeterMath.Add( guest.Happiness, -Settings.BigHappinessChange );
			if ( !RouteToGate( guest, draw ) )
				return false;
			EnterGuestState( guest, GuestState.LeavingPark );
			Observer?.Leaving( guest, cause );
			return true;
		}, ( prank, type ) => Prank( guest, prank, type ), () =>
		{
			choice = ChooseAttraction( guest );
			return choice != null;
		}, () => Wander( guest ), () => guest.Happiness, () => TurnToShowOrPerformer( guest ) );
		switch ( outcome )
		{
			case GuestDecision.Outcome.Cheer:
				StartActivity( guest, GuestActivity.Cheering );
				break;
			case GuestDecision.Outcome.Vomit:
				Observer?.Vomited( guest );
				StartActivity( guest, GuestActivity.Vomiting );
				DropLitter( guest, LitterType.Vomit );
				guest.Nausea = 0;
				// [BIN:TPW-EXE:0x4fed86 vomit voice] after the litter (0x4d93b0 with 7) and the guest event 0x12 the state-6 routine plays cat_kids 0xCC at the guest, every time
				Voiced?.Invoke( guest, VomitVoiceEvent );
				break;
			case GuestDecision.Outcome.ToBin:
				break;
			case GuestDecision.Outcome.GoToRide:
				Observer?.Decided( guest, true );
				guest.AttractionId = choice!.AttractionId;
				EnterGuestState( guest, GuestState.GoingToRide );
				SmarterGuests?.Stalls.Reset( guest.BirthId );
				break;
			case GuestDecision.Outcome.CantDecide:
				Observer?.Decided( guest, false );
				if ( choiceEmptiedByPrice && PriceStalled( guest, draw ) )
					break;
				StartActivity( guest, GuestActivity.CantDecide );
				guest.Happiness = MeterMath.Add( guest.Happiness, -Settings.SmallHappinessChange );
				guest.QueueStandingSinceTurn = turn;
				break;
			case GuestDecision.Outcome.Wander:
				EnterGuestState( guest, GuestState.WalkingAround );
				break;
			case GuestDecision.Outcome.WanderFailed:
				guest.QueueStandingSinceTurn = turn;
				break;
		}
	}

	/// <summary>
	/// A price-emptied decision (the choice found nothing and the price rule turned away a stand the guest wanted): counts the run in the extension's state and asks the policy whether the guest goes home
	/// (<see cref="IGuestDecisions.DecidePriceStall"/>). A guest that goes home takes the leaving test's way: the walk to the gate (with the decision's own draw), state 19, the policy's thought. True when it left;
	/// otherwise the decision ends as any other "can't decide".
	/// </summary>
	// [EXT:smarter-guests-value] the original's price-refused guest loses happiness until it leaves; the smarter guest that the price rule keeps from every stand leaves with the same thought after a few such decisions
	private bool PriceStalled( Guest guest, uint draw )
	{
		if ( SmarterGuests is not { } smarter )
			return false;
		var run = smarter.Stalls.Increment( guest.BirthId );
		var decision = decisions.DecidePriceStall( ContextNow(), ViewOf( guest ), run );
		if ( !Enum.IsDefined( decision.Thought ) )
			throw PolicyError( $"returned the thought {(int)decision.Thought} for a price stall" );
		if ( !decision.GoHome || !RouteToGate( guest, draw ) )
			return false;
		smarter.Stalls.Reset( guest.BirthId );
		EnterGuestState( guest, GuestState.LeavingPark );
		if ( decision.Thought != GuestThought.None )
			Think( guest, decision.Thought );
		Observer?.Leaving( guest, DepartureCause.None );
		return true;
	}

	/// <summary>A staff member standing on the map, as the guests' state 6 sees it (the original's staff list, newest first).</summary>
	/// <param name="Id">The staff member's id.</param>
	/// <param name="Entertainer">The thing's type byte is 6 (an entertainer).</param>
	/// <param name="Performing">The member's own state word is 14 (an entertainer's performance).</param>
	/// <param name="Cell">The cell of the thing's position.</param>
	/// <param name="Walk">The member's walk, whose position the separation of a stepping guest measures (null where the member has none).</param>
	public readonly record struct StaffOnMap( int Id, bool Entertainer, bool Performing, (int X, int Y) Cell, GuestNavigation? Walk = null );

	/// <summary>The cells of the show objects a guest's state 6 turns toward (fireworks and laser shows whose script variable 0 is 0), newest placed first; null for none.</summary>
	public Func<IEnumerable<(int X, int Y)>>? ShowCells { get; set; }

	private StaffOnMap[]? staffOnMap;

	/// <summary>The staff on the map in the original's list order (the last one created first); null for none.</summary>
	public Func<IEnumerable<StaffOnMap>>? StaffOnMapView { get; set; }

	/// <summary>
	/// Steps 5 and 6 of state 6: the guest turns toward the nearest show object within squared distance 5 or, with an entertainer within one cell, toward the
	/// nearest staff member when that one is performing, and the call ends there (before the pranks and the decision) unless the guest already stands on that cell's
	/// diagonal (the sum of the two offsets is 0). True when it turned.
	/// </summary>
	// [BIN:TPW-EXE:0x00501020 nearest show] the objects chain (world +0x1DA746, next +0xC) is walked for a thing with flag +0x32 bit 0x80 (UsageInfo.IsFireworks, 0x4DB46F) whose script variable 0 is 0; the squared cell distance must be below 5 and below the best so far (a tie keeps the first)
	// [BIN:TPW-EXE:0x004FEF25 show turn] a found show with an offset sum (dx + dy, unsigned test) of 0 goes on to step 6, else the heading +0x1C takes the angle of the offset (0x6E7074) and the call ends
	// [BIN:TPW-EXE:0x004FEFF4 performer turn] with a thing of type 6 within one cell (0x4C8EB0, the cells x ± 1, y ± 1) the nearest member of the staff list (0x500F00, world +0x1DA740, next +0x210, no distance cap) must be in state 14 (0x4FF04A); the same offset sum test, event 0xE recorded for the debug dump, the same heading and end
	// [BIN:TPW-EXE:0x006E7074 heading angle] the heading is the 11-bit angle of the offset (HeadingAngle), kept as its direction vector
	private bool TurnToShowOrPerformer( Guest guest )
	{
		var (gx, gy) = (guest.CellX, guest.CellY);
		(int X, int Y)? show = null;
		var best = int.MaxValue;
		if ( ShowCells != null )
		{
			foreach ( var cell in ShowCells() )
			{
				var distance = (cell.X - gx) * (cell.X - gx) + (cell.Y - gy) * (cell.Y - gy);
				if ( distance < 5 && distance < best )
					(show, best) = (cell, distance);
			}
		}
		if ( show is { } found && Turn( guest, found.X - gx, found.Y - gy ) )
			return true;
		if ( staffOnMap == null )
			return false;
		var staff = staffOnMap.Select( member => member.Walk is { } walk ? member with { Cell = walk.Cell } : member ).ToArray();
		if ( !staff.Any( member => member.Entertainer && Math.Abs( member.Cell.X - gx ) <= 1 && Math.Abs( member.Cell.Y - gy ) <= 1 ) )
			return false;
		StaffOnMap? nearest = null;
		best = int.MaxValue;
		foreach ( var member in staff )
		{
			var distance = (member.Cell.X - gx) * (member.Cell.X - gx) + (member.Cell.Y - gy) * (member.Cell.Y - gy);
			if ( distance < best )
				(nearest, best) = (member, distance);
		}
		return nearest is { Performing: true } performer && Turn( guest, performer.Cell.X - gx, performer.Cell.Y - gy );
	}

	/// <summary>Turns the guest toward the offset when its two parts do not sum to 0; true when it turned.</summary>
	private static bool Turn( Guest guest, int dx, int dy )
	{
		if ( dx + dy == 0 )
			return false;
		(guest.HeadingX, guest.HeadingY) = HeadingAngle.Facing( dx, dy );
		return true;
	}

	/// <summary>
	/// The gate's crossing cell <paramref name="index"/> (tp.exe <c>0x004D86D0</c>: 0 is <c>CrossingParkSideA</c>, 1 is side B's X on side A's Y), the cell a guest
	/// on its way out of the park walks to; null when the level has no arrival lane for it.
	/// </summary>
	// [BIN:TPW-EXE:0x004D86D0 gate crossing cell] FixedItemInfo.CrossingParkSide at 0x007855CC/0x007855D0 (side A) and 0x007855D4 (side B's x); both cells take the row of side A (0x007855D0): the cell id is y × 128 + x + 1
	private (int X, int Y)? GateCell( int index )
	{
		var a = ValidLane( Settings.ArrivalLaneA );
		var b = ValidLane( Settings.ArrivalLaneB );
		if ( index == 0 )
			return a is { Length: > 2 } ? a[2] : null;
		return b is { Length: > 2 } ? (b[2].X, (a is { Length: > 2 } ? a[2] : b[2]).Y) : null;
	}

	/// <summary>
	/// The leaving gate's route (state 6, tp.exe <c>0x004FEC90</c>): one route request to each of the gate's two crossing cells, the order picked by the bit 0 of the
	/// state's draw, first with the guest's walk mask and then (a second pass) with the mask 1 that lets a walker leave the paths; the first route found is the walk of
	/// state 18. None found leaves the mask 0 and the guest in state 6. A level without a gate crossing keeps OpenTPW's way out: the guest leaves and is removed.
	/// </summary>
	// [BIN:TPW-EXE:0x004FEE87 leaving gate route] 0x004D86D0(0) and (1) packed in two words, swapped when the draw is odd; two passes of two route requests 0x004FA530 (the cell's centre 0x80, 0x80), the walk mask +0x188 set to 1 after the first pass and back to 0 when no route was found; a route sets state 18
	internal bool RouteToGate( Guest guest, uint draw )
	{
		if ( GateCell( 0 ) == null && GateCell( 1 ) == null )
			return ValidLane( Settings.ArrivalLaneA ) == null && ValidLane( Settings.ArrivalLaneB ) == null;
		var navigation = guest.Navigation;
		for ( var pass = 0; pass < 2; pass++ )
		{
			for ( var attempt = 0; attempt < 2; attempt++ )
			{
				var side = (int)((draw & 1) ^ (uint)attempt);
				if ( GateCell( side ) is { } gate && RouteToCell( guest, gate ) )
				{
					guest.ExitSide = side;
					return true;
				}
			}
			navigation.Mask = 1;
		}
		navigation.Mask = 0;
		return false;
	}

	/// <summary>An activity (<c>0x004FC800</c>): stamps +0x208 with the turn and holds the guest in state 8 for ten park turns.</summary>
	private void StartActivity( Guest guest, GuestActivity activity )
	{
		// [BIN:TPW-EXE:0x4fc83a activity voice] the activity start plays cat_kids 0x7E for activity 4 (the "can't decide" pause) on one thing id in sixteen
		if ( activity == GuestActivity.CantDecide && VoiceGate( guest ) )
			Voiced?.Invoke( guest, CantDecideVoiceEvent );
		guest.BodySprite.QueueTemplate( (VisitorSpriteTemplate)activity );
		guest.Activity = activity;
		guest.InterludeTurn = ParkTurn;
	}

	/// <summary>
	/// State 7 (the dispatcher's case 7, <c>0x00501ABB</c>): one walk step per park turn. On arrival a draw with <c>rand &amp; 3 == 0</c> may
	/// pick the next wander destination and keep walking; otherwise the guest decides again (state 6).
	/// </summary>
	// [BIN:TPW-EXE:0x00501ABB state 7] the walk step 0x004FA2A0 returns 0 (arrived), 1 (walking) or 2 (stuck); on 0 or 2 one draw, and when its low two bits are 0 the wander destination 0x004F9490 keeps state 7 on success; every other case sets state 6
	// [BIN:TPW-EXE:0x0050F8E0 stuck result] a failed search of a set-up (0x50F945) or of a reroute in follow_path (0x50ED7E, 0x50EE7E: the changed ground or a stalled progress) sets walk +0xB8 (guest +0x18C), and the walk step 0x004FA2A0 then returns 2 until the next destination (GuestNavigation.Stuck); the bin walk of state 9 drops its litter on 2 (0x004FFF20)
	private void UpdateWalkingAround( Guest guest )
	{
		if ( !turnStarted )
			return;
		if ( guest.HasWaypoint && !MoveAlong( guest ) )
			return;
		if ( (random.NextWord() & 3) == 0 && Wander( guest ) )
			return;
		EnterGuestState( guest, GuestState.DoingNothing );
	}

	/// <summary>
	/// One walk step of a wander or a walk to a bin; true on arrival. The guest's cell is the one it stands on (the original's thing cell
	/// +5/+7), which the next wander starts from and litter falls on; arriving within reach of a point near a cell's edge can leave it in the
	/// cell before.
	/// </summary>
	private bool MoveAlong( Guest guest ) => MoveAlongStep( guest ) != WalkResult.Walking;

	/// <summary><see cref="MoveAlong"/> with the walk step's result. A path or queue cell is a cell the guest stands on.</summary>
	private WalkResult MoveAlongStep( Guest guest )
	{
		var result = MoveStep( guest );
		var cell = guest.Navigation.Cell;
		if ( cell != (guest.CellX, guest.CellY) && (Grid.IsWalkable( cell.X, cell.Y ) || Grid.IsQueue( cell.X, cell.Y )) )
		{
			(guest.PreviousCellX, guest.PreviousCellY) = (guest.CellX, guest.CellY);
			(guest.CellX, guest.CellY) = cell;
		}
		return result;
	}

	/// <summary>
	/// The bin search (tp.exe <c>0x00500DC0</c>): the nearest bin, by squared distance in cells below <see cref="BinRadius"/>², that a route
	/// reaches; the guest then walks to the cell in front of it (state 9). False when there is none.
	/// </summary>
	// [BIN:TPW-EXE:0x00500DC0 bin search] the object list (game +0x1DA746, next at +0xC) in order; objects with flag +0x32 bit 0x40 (UsageInfo.HoldsLitter, type record +0xF8); dx² + dy² from the guest's cell to the object's below 9 and strictly below the best so far; the route test 0x004FA530 to the object's access cell +0x36 keeps it
	// [BIN:TPW-EXE:0x00500DC0 bin walk] every candidate that is closer than the best so far makes its own route request (0x004FA530 → 0x00510100 → 0x0050F8E0), and a failed request leaves the walk retargeted to that bin, arrived and stuck (+0xB8 = 1) with the best bin's id in +0x1DC: a closer bin tested after the reachable one it does not reach turns the guest's walk to state 9 into a stuck walk, and the walk step 0x004FA2A0 returns 2 at once (0x004FA301), so state 9 drops the litter on the cell
	private bool SeekBin( Guest guest )
	{
		if ( Bins?.Invoke() is not { Count: > 0 } bins )
			return false;
		LitterBin? best = null;
		var radius = BinRadius;
		if ( decisions.UsesBinCarry )
		{
			radius = decisions.BinSearchRadius( ContextNow(), ViewOf( guest ) );
			if ( radius < 0 || radius > MaxBinRadius )
				throw PolicyError( $"returned the bin radius {radius}" );
		}
		var maxRoute = decisions.UsesBinCarry ? Math.Max( 0, decisions.BinMaxRouteCells( ContextNow(), ViewOf( guest ) ) ) : int.MaxValue;
		var bestDistance = radius * radius;
		var lastFailed = false;
		foreach ( var bin in bins )
		{
			var dx = bin.Cell.X - guest.CellX;
			var dy = bin.Cell.Y - guest.CellY;
			var distance = dx * dx + dy * dy;
			if ( distance >= bestDistance )
				continue;
			var route = Grid.FindPath( guest.CellX, guest.CellY, bin.Access.X, bin.Access.Y );
			// [EXT:smarter-guests-litter] a bin beyond the original's reach is walked to only when the route there is short: the radius is a straight line, and a hedge or a river can make it a long walk (skipped before the route request, so nothing is retargeted)
			if ( maxRoute < int.MaxValue && distance >= BinRadius * BinRadius && (route == null || route.Count == 0 || route.Count > maxRoute) )
				continue;
			if ( !RequestRoute( guest, bin.Access, route ) )
			{
				lastFailed = true;
				continue;
			}
			(best, bestDistance, lastFailed) = (bin, distance, false);
		}
		if ( best is not { } found )
			return false;
		guest.BinId = found.Id;
		if ( lastFailed )
			guest.HasWaypoint = false;
		EnterGuestState( guest, GuestState.ThrowingAwayLitter );
		return true;
	}

	/// <summary>
	/// State 9 (tp.exe <c>0x004FFF20</c>): one walk step per park turn towards the bin. On arrival at a bin the litter goes in; a walk that ends
	/// stuck drops the litter on the guest's cell instead (<c>1 + rand % 5</c>). Either way the meter empties and the guest decides again.
	/// </summary>
	// [BIN:TPW-EXE:0x004FFF20 state 9] the walk step 0x004FA2A0: 1 keeps walking; 0 with a bin triggers the bin's script (Level.TriggerBin); 2, or a bin id of 0, drops 1 + rand % 5 on the current cell (0x004D93B0); then the meter +0x1B4 = 0 and state 6
	// [BIN:TPW-EXE:0x004FA2A0 walk result] the walk step answers 2 for a walk that is stuck (a failed set-up, 0x0050F8E0, or a reroute of follow_path that finds no way, GuestNavigation.Step); a route walk of an old OpenTPW save ends stuck when a grid edit takes a cell or link of its route (EndBrokenWalks, [EXT:legacy-save-route-walk])
	private void UpdateThrowingAwayLitter( Guest guest )
	{
		if ( !turnStarted )
			return;
		var arrived = false;
		if ( guest.HasWaypoint && !(arrived = MoveAlong( guest )) )
			return;
		if ( arrived && !guest.Navigation.Stuck && guest.BinId != 0 && Bins?.Invoke() is { } bins && bins.Any( bin => bin.Id == guest.BinId ) )
		{
			Observer?.LitterBinned( guest );
			LitterBinned?.Invoke( guest, guest.BinId );
		}
		else
			DropLitter( guest, (LitterType)(1 + random.NextWord() % ParkLitter.OrdinaryKinds) );
		guest.Litter = 0;
		guest.BinId = 0;
		EnterGuestState( guest, GuestState.DoingNothing );
	}

	/// <summary>
	/// A prank of an unhappy prankster (state 6, tp.exe <c>0x004FF0F5</c>): a stinkbomb or litter on its own cell, or a balloon burst; a prank raises
	/// <see cref="Pranked"/> (the original's prank message to the advisor and the guards) and adds 1 to the prankster's happiness.
	/// </summary>
	// [APPROX:GUEST-007] PRANKS: the prank step (0x4FF0F5, x86 X18), its outcomes, the guard reply (0x508A30), the guard's set-down at the cell's centre with the side its world draw picks (0x4FEB50) and the advisor's prank lines (0x59B060: advice 0x7D, 0x7E or 0x7F by the message's kind word, the prankster's id modulo 3) match, the bus delivers the prank message at once (0x40FB10) and event 15 feeds only the debug dump 0x4FAA60; a balloon prankster's victim is the balloon-holding other guest that entered the cell last (the cell's thing list is head-inserted on every cell change, 0x50B6A0, 0x4D91F0); and the queued advice keeps neither the prankster's id nor its cell (record words +0x0 and +0x4, 0x59B590); a set-down guest walks to its side's gate cell (0x4D86D0, the original's route) and on along OpenTPW's arrival lane (ARRIVAL-001), where the original's states 19 to 21 walk it to the arrival vehicle's stop cells — evidence needed: which guest states are on the cell thing lists, and what reads a queued advice record's id and cell words
	private bool Prank( Guest guest, GuestDecision.Prank prank, LitterType type )
	{
		if ( prank == GuestDecision.Prank.Balloon )
		{
			// [BIN:TPW-EXE:0x004FF1CD balloon prank] the first other guest (thing type 1) on the prankster's cell list whose +0x210 is set has its balloon burst (0x004FE950); without one there is no prank
			// the cell's list is walked head first (WALK-003): of the guests holding a balloon the one that entered the cell last is the victim
			Guest? victim = null;
			if ( occupants.TryGetValue( guest.Navigation.Cell, out var cellList ) )
			{
				for ( var index = cellList.Count - 1; index >= 0 && victim == null; index-- )
				{
					var other = cellList[index];
					if ( other != guest && other.HasBalloon && other.OnGrid && other.State is not (GuestState.Using or GuestState.HeldByGuard or GuestState.Gone) )
						victim = other;
				}
			}
			if ( victim == null )
				return false;
			BurstBalloon( victim );
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

	/// <summary>
	/// A guest's voice (tp.exe <c>0x004FAA00</c>: the guest sound bank <c>cat_kids</c> at the guest's position): raised with the bank's event number. Presentation only: it takes no draw
	/// from the park's generators and changes no state.
	/// </summary>
	public event Action<Guest, uint>? Voiced;

	/// <summary>Raised when a guest beams in or out of a ride that requires teleporting (boarding and leaving); presentation only, the particle starts at the guest's place.</summary>
	public event Action<Guest>? Teleported;

	/// <summary>The <c>cat_kids</c> event of a guest dismissed from a queue: the sound <c>0x0050133D</c> plays for one thing id in eight (0x80).</summary>
	public const uint DismissalVoiceEvent = 0x80;

	/// <summary>The <c>cat_kids</c> events of the guests' other voices (tp.exe <c>0x004FAA00</c>, each with the address of its call): a ride demolished under its rider (0x80, <c>0x4fb3f5</c>), the till of a price paid (0xD0, <c>0x4fe1cb</c>), a drink (0x83) or a meal (0x84) taken (<c>0x4fe312</c>, <c>0x4fe321</c>), a toilet thought (0x85), a "can't decide" pause (0x7E) and a vomit (0xCC, <c>0x4fed86</c>).</summary>
	public const uint RideRemovedVoiceEvent = 0x80, TillVoiceEvent = 0xD0, DrinkVoiceEvent = 0x83, MealVoiceEvent = 0x84, ToiletThoughtVoiceEvent = 0x85, CantDecideVoiceEvent = 0x7E, VomitVoiceEvent = 0xCC;

	/// <summary>Whether a guest's voice that tp.exe gates by the thing id plays: the thing id's low nibble is 0, one guest in sixteen (<c>0x50b350</c> reads the id's word, <c>test byte [eax], 0xf</c>).</summary>
	private static bool VoiceGate( Guest guest ) => (guest.Id & 0xF) == 0;

	/// <summary>Litter of <paramref name="type"/> on the guest's cell (lost when the cell already holds some).</summary>
	private void DropLitter( Guest guest, LitterType type )
	{
		if ( Litter?.Drop( guest.CellX, guest.CellY, type ) == true )
			Observer?.LitterDropped( guest );
	}

	/// <summary>
	/// After a grid edit, a wander walk whose route ahead lost a path cell or a link ends where the guest stands, as the original's walk
	/// step reports "stuck" (2); the guest then picks again or decides at its next turn (state 7). A guest left off the paths is put on the
	/// nearest one.
	/// </summary>
	private void EndBrokenWalks()
	{
		foreach ( var guest in guests )
		{
			// A walk of the original kind notices a changed block itself (the stamp reroute of follow_path, ParkCellMap.BlockStamp).
			// [EXT:legacy-save-route-walk] a route walk restored from an OpenTPW save made before the original walk has no original follow_path behind it, so a grid edit that takes a cell or link of its route ends it where the guest stands instead of replanning; the original has no such walks, and OpenTPW cannot rebuild the walk state such a save never held
			// [BIN:TPW-EXE:0x004DE1F0 queue recompute stamp] the queue recompute stamps its back cell (the counter raised by 0x4d8c50, written into the back cell's block by 0x4d8c60) after the queue tool's edits, a path laid over a queue end (0x5346d0, Paths.QueueCellPaved) and a queue cell clear (0x5367a0, QueuePaths.RemoveFrom), the owning ride found through the cell's owner word +0x10; every cell type change stamps its block (0x4d8c60 from 0x5346d0, 0x5348d0, 0x538fc0) and the walk compares it (0x50ed6c, a search in mode 1)
			if ( guest.State is not (GuestState.WalkingAround or GuestState.ThrowingAwayLitter) || !guest.HasWaypoint || guest.Navigation.Original
				|| RouteAheadHolds( guest.Navigation ) )
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

	/// <summary>
	/// Sets down the guests that stand on <paramref name="cells"/> after those stopped being path (an object built over a path, whose placement
	/// clears it): each walking guest on such a cell is put on the nearest path cell, its walk ended, and goes on in its state as after a broken
	/// walk (<see cref="EndBrokenWalks"/>). Returns the number of guests moved.
	/// </summary>
	// Approximation (RIDES-018, registered at ParkObjects.Check): guests on a path cell an object's placement clears are set down on the nearest path cell; tp.exe's placement (0x528A70) and ClearCell (0x5367A0) write cells and the block stamp (0x4D8C60, through SetCellType 0x5346D0), which reroutes walking things, and what a guest standing inside the new footprint does next is not traced
	public int SetDownOffCells( IReadOnlySet<(int X, int Y)> cells )
	{
		var moved = 0;
		foreach ( var guest in guests )
		{
			if ( !guest.OnGrid || guest.Navigation.OffGrid
				|| guest.State is not (GuestState.DoingNothing or GuestState.WalkingAround or GuestState.ThrowingAwayLitter or GuestState.GoingToRide
					or GuestState.LeavingPark or GuestState.ExitingRide) )
				continue;
			// The cell it stands on, and the cell a one-cell step is heading for (CellX/CellY move ahead with SetCellWaypoint).
			var here = guest.Navigation.Cell;
			if ( !(cells.Contains( here ) && !Grid.IsWalkable( here.X, here.Y )) && !(cells.Contains( guest.Cell ) && !Grid.IsWalkable( guest.CellX, guest.CellY )) )
				continue;
			if ( (Grid.IsWalkable( here.X, here.Y ) ? here : Grid.FindNearestWalkable( here.X, here.Y )) is not { } cell )
				continue;
			PlaceOnGrid( guest, cell.X, cell.Y );
			moved++;
		}
		return moved;
	}

	/// <summary>
	/// Sets down the queued guests that stand on <paramref name="cells"/> after a removal emptied them (the queue tool took queue cells, front included): each such guest, queueing,
	/// moving up, boarding or waiting to board, is put on the nearest path cell and leaves the queue as if dismissed ("queue was shortened"). Returns the number of guests moved.
	/// </summary>
	// Approximation (QUEUE-015, registered at QueuePaths.RemoveFrom): the original's cell clear (0x5367A0) writes no guest and what a guest standing on a cleared queue cell does next is not traced; the dismissal (0x005012F0) is the exit the queue-edited re-evaluation uses (0x00501390)
	public int SetDownOffQueueCells( IReadOnlySet<(int X, int Y)> cells ) => SetDownOffQueueCells( cells, null );

	/// <summary>Preserves the edited ride's overflow thought while its removed-cell occupants take the existing set-down exit.</summary>
	internal int SetDownOffQueueCells( IReadOnlySet<(int X, int Y)> cells, RideVisitorBridge? editedRide )
	{
		var angry = new HashSet<int>();
		if ( editedRide != null && ReferenceEquals( FindAttraction( editedRide.AttractionId ), editedRide ) )
		{
			// Snapshot before any set-down unlinks a guest: even an in-room occupant on a removed cell can shift a later overflow guest's place.
			// The queue-edited exit (0x00501390, ORACLE.md X14) thinks 13 for ids divisible by three; its caller (0x004DE1F0) skips the called guest.
			// Only carry that outcome through this host relocation policy; do not run re-evaluation early or change the called/state-14 set-down behavior.
			var queue = editedRide.Queue;
			var room = RideVisitorBridge.PositionsPerCell * editedRide.QueueSizeInCells;
			var called = editedRide.CalledGuest;
			for ( var position = room; position < queue.Count; position++ )
			{
				var id = queue[position];
				if ( id != called && id % 3 == 0 && Find( id ) is { } guest && guest.AttractionId == editedRide.AttractionId
					&& guest.State is GuestState.Queueing or GuestState.MovingUpQueue or GuestState.Boarding )
					angry.Add( id );
			}
		}
		var moved = 0;
		foreach ( var guest in guests.ToArray() )
		{
			if ( !guest.OnGrid || guest.Navigation.OffGrid || !guest.IsInQueue )
				continue;
			var here = guest.Navigation.Cell;
			if ( !(cells.Contains( here ) || cells.Contains( (guest.CellX, guest.CellY) )) || Grid.IsWalkable( here.X, here.Y ) || Grid.IsQueue( here.X, here.Y ) )
				continue;
			if ( Grid.FindNearestWalkable( here.X, here.Y ) is not { } cell )
				continue;
			PlaceOnGrid( guest, cell.X, cell.Y );
			if ( FindAttraction( guest.AttractionId ) is { } attraction )
				DismissFromQueue( guest, attraction, angry.Contains( guest.Id ) ? GuestThought.Angry : GuestThought.None, QueueEndReason.QueueRemoved );
			else
				ReturnOrphanToPath( guest );
			moved++;
		}
		return moved;
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
		var cells = Grid.Cells;
		// [BIN:TPW-EXE:0x004F94C6 stranded hold] a guest that found no way on is marked with the stamp counter (+0x198): while no block around its cell (the queue's tail for a queue cell) has been stamped above the mark (0x004FA770) the wander thinks "Confused" (0x0050BE80, 0x11) and returns before any draw
		if ( guest.StrandedMark != 0 && cells.StampCounter + 1 < guest.StrandedMark )
			guest.StrandedMark = 0;
		if ( guest.StrandedMark != 0 && StrandedHolds( guest, start ) )
		{
			Think( guest, GuestThought.Confused );
			return false;
		}
		guest.StrandedMark = 0;
		bool Route( (int X, int Y) cell )
		{
			// [BIN:TPW-EXE:0x004F9490 wander route] the picked destination is a SetDest (0x00510100), whose search decides success; a failed search leaves the walk stuck (the route cells below are the M3 gate's bookkeeping)
			var path = Grid.FindPath( start.X, start.Y, cell.X, cell.Y );
			SetDestination( guest, GuestNavigation.BytePoint( cell, GuestWander.CentreByte, GuestWander.CentreByte ), path );
			if ( !guest.Navigation.Stuck )
				return true;
			guest.HasWaypoint = false;
			return false;
		}
		if ( (Grid.LinksAt( start.X, start.Y ) & 0x55) == 0 )
		{
			// [BIN:TPW-EXE:0x004F9490 start without links] the step count R % 5 is drawn first, then the search for a path cell around the start (0x522810 below 1)
			random.NextWord();
			return GuestWander.OffPath( Grid, start, () => random.NextWord(), Route );
		}
		// [EXT:smarter-guests-paths] a policy that weighs the path's comfort gets the cells the original's walk visited (a copy: the walk and its draws are the same)
		Span<(int X, int Y)> trail = decisions.UsesPathComfort ? stackalloc (int X, int Y)[GuestWander.TrailCapacity] : default;
		var trailCount = 0;
		if ( GuestWander.Pick( Grid, start, () => random.NextWord(), null, out _, trail, out trailCount ) is not { } destination )
		{
			// [BIN:TPW-EXE:0x004F9D6E stranded] a guest (not a staff member) whose cell has links but no candidate is "stranded at time": thought Confused and the mark, raised stamp counter
			if ( (Grid.IsWalkable( start.X, start.Y ) || Grid.IsQueue( start.X, start.Y )) && GuestWander.OriginalCandidates( Grid, start ) == 0 && (Grid.LinksAt( start.X, start.Y ) & 0x55) != 0 )
			{
				Think( guest, GuestThought.Confused );
				guest.StrandedMark = cells.StampCounter + 1;
			}
			return false;
		}
		// the search and its draws were the host's; the decision layer accepts the proposal or names another goal
		if ( decisions.UsesPathComfort && trailCount > 0 )
			destination = WeighPath( guest, destination, trail[..trailCount] );
		destination = decisions.ChooseWanderGoal( ContextNow(), ViewOf( guest ), destination );
		if ( !Grid.InBounds( destination.Cell.X, destination.Cell.Y ) || destination.ByteX is < 0 or > 255 || destination.ByteY is < 0 or > 255 )
			throw PolicyError( $"returned the wander goal ({destination.Cell.X}, {destination.Cell.Y}) with bytes ({destination.ByteX}, {destination.ByteY}), outside the map" );
		var route = Grid.FindPath( start.X, start.Y, destination.Cell.X, destination.Cell.Y );
		SetDestination( guest, GuestNavigation.BytePoint( destination.Cell, destination.ByteX, destination.ByteY ), route );
		if ( guest.Navigation.Stuck )
		{
			guest.HasWaypoint = false;
			return false;
		}
		return true;
	}

	/// <summary>Whether the stranded guest stays stranded: no block of the nine cells around its cell, or around its queue's tail, carries a stamp at or above its mark (<c>0x004FA770</c>).</summary>
	// [BIN:TPW-EXE:0x004FA770 stranded test] the reference is the guest's cell, or for a queue cell (0x00536320) the cell reached by following the predecessor links (0x004DE670) to the end; the nine cell offsets (−1..1, −1..1) of the table 0x007CED88 give block stamps (0x004D8CA0) and the guest stays stranded while their largest is below the mark
	private bool StrandedHolds( Guest guest, (int X, int Y) start )
	{
		var cell = start;
		if ( Grid.IsQueue( cell.X, cell.Y ) )
		{
			for ( var guard = 0; guard < 1024; guard++ )
			{
				if ( RideVisitorBridge.NextQueueCell( Grid, cell ) is not { } behind )
					break;
				cell = behind;
			}
		}
		var id = (ushort)(cell.Y * 128 + cell.X + 1);
		var largest = 0;
		for ( var dy = -1; dy <= 1; dy++ )
		{
			for ( var dx = -1; dx <= 1; dx++ )
			{
				var offset = (uint)(ushort)(id + dy * 128 + dx) - 1;
				largest = Math.Max( largest, Grid.Cells.BlockStamp( (int)((offset & 0x7F) >> 4), (int)(offset >> 11) ) );
			}
		}
		return largest < guest.StrandedMark;
	}

	/// <summary>Presentation-only gate, absent for standalone simulations. It neither changes thoughts nor clears existing bubbles and is never saved or hashed.</summary>
	internal Func<bool>? ThoughtBubblesAllowed { get; set; }

	/// <summary>
	/// The guest thinks <paramref name="thought"/> (tp.exe <c>0x0050BE80</c>): the thought is stored, and a bubble shows it when the last one is old enough
	/// for the new thought's priority (<see cref="GuestThoughts.TakesBubble"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x0050BE80 thought] +0 = the thought; with bubbles on, the old bubble goes (0x00475550) and, when the turn has reached +0x8C + 20 × priority, a new one starts and +0x8C is stamped
	// Approximation (GUEST-006, registered at UpdateNeedsTurn): the Level maps ride view to the bubble gate; other camera bits of 0x16 remain unresolved. Online visits retain the existing online-bubbles-on extension.
	private void Think( Guest guest, GuestThought thought )
	{
		Observer?.Thought( guest, thought );
		guest.Thought = thought;
		if ( ThoughtBubblesAllowed?.Invoke() == false )
			return;
		guest.BubbleThought = GuestThought.None;
		if ( !GuestThoughts.TakesBubble( (int)thought, ParkTurn, guest.BubbleTurn ) )
			return;
		guest.BubbleThought = thought;
		guest.BubbleTurn = ParkTurn;
	}

	/// <summary>The share of littered path cells in the guest's 4 × 4 analyser block (tp.exe <c>0x004C8BA0</c> over <c>0x004C8AE0</c>).</summary>
	// [BIN:TPW-EXE:0x004C8AE0 block census] the block of the guest's cell (0x004C7B70: 32 × 32 blocks of 4 × 4 cells) counts its littered cells and its cells of type 1, 3 or 9 (0x00536310, 0x00536320); the ratio is a single division
	// [BIN:TPW-EXE:0x004FC8A0 thought pick] the block's counts are read through the analyser's caches with age 4 (0x004C8BA0(4), then 0x004C8AE0(4))
	private float MessyShare( Guest guest )
	{
		if ( Litter is not { } litter || Grid.Cells is null || (uint)guest.CellX >= ParkLitter.MapSize || (uint)guest.CellY >= ParkLitter.MapSize )
			return 0f;
		var block = ParkLitter.BlockOf( guest.CellX, guest.CellY );
		// the analyser stamps its caches with the world turn, the clock the strike reads them with too (the economy's, ArrivalClock)
		var turn = ArrivalClock?.Invoke() ?? ParkTurn;
		var littered = litter.LitteredInBlock( block, 4, turn );
		var paths = litter.PathCellsInBlock( block, 4, turn );
		return paths == 0 ? (littered == 0 ? 0f : float.PositiveInfinity) : (float)littered / paths;
	}

	/// <summary>
	/// A guest heads home (<c>0xebe30</c>): it loses <c>PeepInfo.BigHappinessChange</c> (<c>0xeb6f4</c> with selector 2) and goes to state 18.
	/// </summary>
	// [BIN:STP-PPC:0x100EBE30 leaving] 0xeb6f4(guest, 2) subtracts the settings word +32 from happiness (clamped), then 0xef700(guest, 18)
	/// <summary>The three predicates of <see cref="GuestLifecycle.ShouldLeave"/> as they stand for the guest now (measurement only; reads nothing it changes).</summary>
	private DepartureCause LeavingPredicates( Guest guest ) =>
		(MeterMath.Byte( guest.Happiness ) == 0 ? DepartureCause.HappinessZero : 0)
		| (guest.ExitLevel == 0 ? DepartureCause.ExitCounterZero : 0)
		| (Payments?.ParkClosed ?? false ? DepartureCause.ParkClosed : 0);

	private void StartLeaving( Guest guest )
	{
		guest.Happiness = MeterMath.Add( guest.Happiness, -Settings.BigHappinessChange );
		EnterGuestState( guest, GuestState.LeavingPark );
	}

	/// <summary>
	/// The original's attraction score (<c>0xe9164</c>, docs/reverse/ORACLE.md F24): distance, queue, excitement, thirst, hunger, toilet and illness
	/// matches normalised by the sum of all seven weights, scaled for new rides, indoor rides in rain and costly rides, and divided down by the
	/// guest's history (see <see cref="AttractionScore"/>). Unsigned, like the original; 0 for an attraction without a back-of-queue cell.
	/// </summary>
	// [BIN:STP-PPC:0x100E9164 attraction score] executed by the PowerPC oracle (attraction-score, 578 inputs; C# agrees)
	// [BIN:STP-PPC:0x100E9388 cell modifier] the environment record's component 4 (+8) at the back-of-queue cell divides the distance match; only the fireworks' RegionFX row 7 writes it (Attraction 10 in the shipped Standard.sam; tp.exe 0x004DB48C), so it is 0 everywhere else
	// [APPROX:GUEST-002] faithful scoring and major choice now admit a disconnected real raw-type-3 queue back and leave route failure to RouteToCell; missing/untyped fallback backs, the minor decision and extension planning retain their join-cell filters (docs/DISCONNECTED-QUEUE-SCORING.md)
	public uint ScoreAttraction( Guest guest, IRideVisitorBridge attraction )
	{
		if ( !HasScoreBackCell( attraction ) )
			return 0;
		return WinterScoreOf( ContextNow(), guest, ViewOf( guest ), attraction, ViewOf( attraction, guest ) );
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
	private IRideVisitorBridge? ChooseAttraction( Guest guest, bool setWalk = true )
	{
		// the host runs the choice loop: the policy scores and compares, the route request below is the host's alone (made exactly as the original makes it, for a candidate that beats the best)
		var context = ContextNow();
		var view = ViewOf( guest );
		uint best = 0;
		var chosen = 0;
		choiceEmptiedByPrice = false;
		// the whole loop is one decision: a policy that meters work (the script mods) treats its candidates as a unit
		decisions.BeginDecision();
		BeginReservations();
		try
		{
		// the plan's scan of the rides shares the decision's walker count
		view = WithPlanHorizon( view, guest, null );
		foreach ( var attraction in NewestFirst() )
		{
			// a disconnected typed back can still be scored; reachability is tested only after it beats the best
			if ( !HasScoreBackCell( attraction ) )
				continue;
			var candidate = ViewOf( attraction, guest );
			var score = WinterScoreOf( context, guest, view, attraction, candidate );
			// [EXT:smarter-guests-value] a candidate the price rule turned away that the guest could otherwise have chosen: if nothing is chosen, the decision was emptied by price
			if ( decisions.UsesValueForMoney && score < AttractionScore.CandidateFloor && attraction.IsOpen && !attraction.IsBrokenDown
				&& attraction.QueueLength < RideVisitorBridge.PositionsPerCell * attraction.QueueSizeInCells && decisions.PriceTurnedAway( context, view, candidate ) )
				choiceEmptiedByPrice = true;
			if ( !decisions.BeatsInMajorDecision( context, view, score, best ) )
				continue;
			// [BIN:TPW-EXE:0x004FCB10 choice route] the route request 0x004FA530 to the ride's back-of-queue cell (0x004DE110) is made for every candidate that passes the counts-test and would win, and its walk stays set: the winner is the last candidate whose route was found, and a candidate whose route fails after it leaves the walk stuck (the original's SetDest failure state); the walk then runs all the way to the back cell (state 10, WALK-004)
			if ( !(attraction.IsOpen && !attraction.IsBrokenDown && attraction.QueueLength < RideVisitorBridge.PositionsPerCell * attraction.QueueSizeInCells
				&& (setWalk ? RouteToCell( guest, attraction.QueueCells[^1] ) : CanRouteTo( guest, attraction.QueueCells[^1] ))) )
				continue;
			(best, chosen) = (score, attraction.AttractionId);
		}
		}
		finally
		{
			decisions.EndDecision();
			EndReservations();
		}
		return chosen == 0 ? null : FindAttraction( chosen );
	}

	/// <summary>Minor decisions in which the policy's hysteresis (<see cref="IGuestDecisions.SwitchesTarget"/>) kept the guest on its target although a better-scoring candidate was in reach (tests and the report; not saved or hashed).</summary>
	internal long TargetsKept { get; private set; }

	/// <summary>Minor decisions the hysteresis let through to the route tests (the guest may still stay if the routes say so).</summary>
	internal long TargetsSwitched { get; private set; }

	/// <summary>Set by the last <see cref="ChooseAttraction"/>: a candidate was left out on price (<see cref="IGuestDecisions.PriceTurnedAway"/>); with no choice, the decision was price-emptied.</summary>
	private bool choiceEmptiedByPrice;

	/// <summary>The attractions in the order the original's choice loop meets them: newest first.</summary>
	// [BIN:TPW-EXE:0x004FCB10 thing list order] the choice walks the object list from its head (game +0x1DA746) through each thing's next word +0xC; 0x00519D80, called only by the object constructor 0x004DB090, pushes each new object at the head, and the save 0x00516C80 and the load 0x005179C0 keep the head word, so the newest object comes first; attraction ids are allocated in placement order
	private IEnumerable<IRideVisitorBridge> NewestFirst()
	{
		for ( var index = attractions.Count - 1; index >= 0; index-- )
			yield return attractions[index];
	}

	/// <summary>The major decision's choice for <paramref name="guest"/> on the current park turn, without changing the guest (tests).</summary>
	internal IRideVisitorBridge? PeekChoice( Guest guest ) => ChooseAttraction( guest, setWalk: false );

	/// <summary>The major decision's choice with its route request, as state 6 makes it: the walk to the chosen ride's back cell is set (tests).</summary>
	internal IRideVisitorBridge? Choose( Guest guest ) => ChooseAttraction( guest );

	/// <summary>Whether the walk search from the guest's cell reaches <paramref name="cell"/> (the route request of <see cref="RouteToCell"/> without setting the walk).</summary>
	private bool CanRouteTo( Guest guest, (int X, int Y) cell ) =>
		WalkSearch.Find( Grid.Cells, GuestNavigation.CellByteOf( guest.Navigation.X, guest.Navigation.Y ), (cell.X & 0xFF, cell.Y & 0xFF), guest.Navigation.Mask, new List<(int X, int Y)>() );

	private IRideVisitorBridge? FindAttraction( int id )
	{
		foreach ( var attraction in attractions )
			if ( attraction.AttractionId == id )
				return attraction;
		return null;
	}

	private void UpdateGoingToRide( Guest guest, float dt )
	{
		if ( guest.HasWaypoint )
		{
			var result = MoveAlongStep( guest );
			if ( result == WalkResult.Walking )
			{
				DropRideTargetForClosedPark( guest );
				return;
			}
			if ( result == WalkResult.Stuck )
			{
				// [BIN:TPW-EXE:0x004FFBC0 stuck on the way] the walk step answers 2: "The person has become stuck on the way", event 3, the big happiness change (0x004FEA70(2)), the target +0x1DC cleared and state 6
				guest.Happiness = MeterMath.Add( guest.Happiness, -Settings.BigHappinessChange );
				guest.AttractionId = 0;
				EnterGuestState( guest, GuestState.DoingNothing );
				return;
			}
		}
		var attraction = FindAttraction( guest.AttractionId );
		// [BIN:TPW-EXE:0x004FFBC0 walk to the queue] state 10 reads neither the ride's state nor its open word: a guest keeps walking to a ride closed or broken meanwhile, joins, and the ride sends it away from the queue head (RideVisitorBridge.HostStep)
		if ( attraction == null || !attraction.AcceptsJoins )
		{
			guest.AttractionId = 0;
			EnterGuestState( guest, GuestState.DoingNothing );
			return;
		}
		// [BIN:TPW-EXE:0x004FFBC0 arrival at the back cell] the walk is one request to the back-of-queue cell (0x004FCB10); on arrival the guest's own cell must be that back cell (0x004DE110), else "The back of the queue has moved while I was walking": the back cell is asked for again (0x004DE110, 0x004FA530) and a route keeps the guest walking in state 10, none sends it to state 6 (event 0x16, no thought; the target stays, GUEST-004)
		var back = attraction.QueueCells[^1];
		if ( guest.Navigation.Cell != back )
		{
			if ( RouteToCell( guest, back ) )
				return;
			guest.AttractionId = 0;
			EnterGuestState( guest, GuestState.DoingNothing );
			return;
		}
		(guest.PreviousCellX, guest.PreviousCellY) = (guest.CellX, guest.CellY);
		(guest.CellX, guest.CellY) = back;
		var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
		// the physical checks (open, not listed, room) are the bridge's; the excitement gate and the limit are the decision layer's, and the bridge appends the admitted guest
		var result2 = attraction.CheckJoinRoom( guest.Id );
		if ( result2 == QueueJoinResult.Joined )
		{
			var alternative = decisions.AlternativeLookAfterTurns( ContextNow(), ViewOf( guest ) ) >= 0 ? BestAlternativeWait256( guest, attraction.AttractionId ) : -1;
			var admission = decisions.DecideAdmission( ContextNow(), ViewOf( guest ), new AdmissionView( ViewOf( attraction, guest ), attraction.StaticExcitementLevel,
				attraction.JoinDifference( type.PreferredExcitement ), attraction.QueueLength, attraction.QueueLimit, alternative ) );
			result2 = admission.Result;
			if ( result2 is not (QueueJoinResult.Joined or QueueJoinResult.TooExciting or QueueJoinResult.NotExcitingEnough or QueueJoinResult.QueueTooLong) )
				throw PolicyError( $"returned the admission result {result2}, which a queue join cannot end in" );
			if ( admission.Admits )
				attraction.AppendToQueue( guest.Id );
		}
		if ( result2 == QueueJoinResult.Joined )
		{
			Observer?.QueueJoined( guest, attraction );
			BeginQueue( guest, attraction );
			return;
		}
		Observer?.QueueRejected( guest, attraction, result2 switch
		{
			QueueJoinResult.NoRoom => QueueEndReason.Full,
			QueueJoinResult.TooExciting => QueueEndReason.TooScary,
			QueueJoinResult.NotExcitingEnough => QueueEndReason.TooBoring,
			QueueJoinResult.QueueTooLong => QueueEndReason.QueueLimit,
			_ => QueueEndReason.NotAccepting
		} );
		// [BIN:TPW-EXE:0x004FFCD7 join refusal] too exciting: 15, not exciting enough: 12, a queue too long: 16 (0x004FFD7E); none of the join's refusals changes happiness (0x004FFBC0 calls 0x004FEA70 only when the walk is stuck or the park closed)
		// [BIN:TPW-EXE:0x004FFC4F full queue room] the room test 0x004DDA20 (count below 4 × cells) fails: event 0x15 and state 6 only (0x004FFE0A → 0x004FFE54), no thought, no happiness change and no history push
		if ( result2 != QueueJoinResult.NoRoom )
			Think( guest, result2 switch
			{
				QueueJoinResult.TooExciting => GuestThought.Scared,
				QueueJoinResult.NotExcitingEnough => GuestThought.Bored,
				_ => GuestThought.QueueTooLong
			} );
		// [BIN:STP-PPC:0x100ECE18 refusal history] the queue-too-long path (0xece18) and both excitement refusals (0xecf3c) push the target's id into the temporary history (0xea290) before clearing the target
		// [APPROX:GUEST-004] the full-room refusal keeps the target +0x1DC in tp.exe (the jump past 0x004FFEDA), which OpenTPW clears; the state-6 handler cannot tell (its only read, 0x004FF437, follows the choice 0x004FCB10, which clears the word first at 0x004FCB21), but the object-removal message handler 0x004FB360 still matches a stale target and costs the guest SmallHappinessChange (0x004FE980(0)) with a reset to state 6, which OpenTPW's removal (GuestSimulation.Unregister) cannot do for a cleared target — evidence needed: none (read)
		if ( result2 is QueueJoinResult.QueueTooLong or QueueJoinResult.NotExcitingEnough or QueueJoinResult.TooExciting )
			PushHistory( guest.TemporaryHistory, attraction.AttractionId );
		// [BIN:TPW-EXE:0x004FFCF6 excitement refusal] the refusal clears the target and the decision stamp +0x1FC, so the next major decision is not held back 30 turns
		if ( result2 is QueueJoinResult.NotExcitingEnough or QueueJoinResult.TooExciting )
			guest.QueueStandingSinceTurn = 0;
		guest.LastAttractionId = attraction.AttractionId;
		guest.AttractionId = 0;
		EnterGuestState( guest, GuestState.DoingNothing );
	}

	/// <summary>
	/// A guest still walking to its ride when the park is closed (state 10, once per park turn): "The park has closed underneath me", the big happiness drop,
	/// the ride target dropped and state 6, whose leaving gate takes the big drop again and sends the guest home.
	/// </summary>
	// [BIN:TPW-EXE:0x004FFEB9 park closed underneath me] after the walk step 0x004FA2A0 returns "still walking" (neither 0 nor 2) the handler reads the closed word (0x0051A280, mParkClosed): closed gives the log line, 0x004FEA70(2) (PeepInfo.BigHappinessChange), target +0x1DC = 0 and SetState(6) (0x00501DB0); open counts a byte at +0x2C and calls 0x004FD570 every twelfth update
	private void DropRideTargetForClosedPark( Guest guest )
	{
		if ( !turnStarted )
			return;
		if ( Payments?.ParkClosed == true )
		{
			guest.Happiness = MeterMath.Add( guest.Happiness, -Settings.BigHappinessChange );
			guest.AttractionId = 0;
			EnterGuestState( guest, GuestState.DoingNothing );
			return;
		}
		// [BIN:TPW-EXE:0x004FFEF2 periodic call] the byte +0x2C counts up on every update that finds the guest still walking in an open park; past 11 it is reset to 0 and 0x004FD570 runs, so every twelfth update
		if ( ++guest.RideWalkCount > 11 )
		{
			guest.RideWalkCount = 0;
			MinorDecision( guest );
		}
	}

	/// <summary>
	/// The minor decision of a guest walking to a ride (tp.exe <c>0x004FD570</c>): the rides standing within a window of 4 × 4 cells round the guest
	/// (x − 2..x + 1, y − 2..y + 1) and strictly nearer to the target's back cell than the guest is, other than the target, that pass the counts-test, are
	/// scored (<see cref="ScoreAttraction"/>); the best one (a tie goes to the later one on an odd park turn) replaces the target when the route from the target's
	/// access cell to the best ride's access cell is shorter than the route from the target's access cell to the guest, and the guest walks to its back cell.
	/// </summary>
	// [BIN:TPW-EXE:0x004FD570 minor decision] needs a target +0x1DC; d0 = squared distance from the guest's cell to the target's back cell (0x004DE110); every thing of type 3 on the window's cells that is not the target and passes 0x004DD920 is scored with 0x004FCC30, kept when the score beats the best (starting at 0) or ties it on an odd turn (game +0x1DA70C bit 0); with a best the routes 0x004D8B40 (the sum of the legs' |dx| + |dy| of the search, mask +0x188; −1 when none) from the TARGET's access cell +0x36 to the guest's cell and from the TARGET's access cell to the best's access cell must both exist and the second be shorter (the best is nearer to the target than the guest is); then event 0x17, +0x1DE = the old target, +0x1DC = the best and the route request 0x004FA530 to the best's access cell +0x36
	// [APPROX:GUEST-019] the route request goes to the best's back cell where the original requests its access cell +0x36; a ride is placed on the window by its entrance cell where the original reads the cell its thing stands on (+5, +7) and walks the cell's thing list newest first (GUEST-002); the access cell of a ride is its entrance cell (+0x36, GUEST-005); the major destination +0x1DE and the event 0x17 are not kept (GUEST-004)
	internal void MinorDecision( Guest guest )
	{
		if ( guest.AttractionId == 0 || FindAttraction( guest.AttractionId ) is not { } target || target.JoinCell == null )
			return;
		var back = target.QueueCells[^1];
		var distance = (guest.CellX - back.X) * (guest.CellX - back.X) + (guest.CellY - back.Y) * (guest.CellY - back.Y);
		// the window's candidates in the original's order (x outer, y inner, newest attraction first), scored by the policy and compared by its rule; the host keeps the best
		var context = ContextNow();
		var view = ViewOf( guest );
		IRideVisitorBridge? best = null;
		uint bestScore = 0;
		var keeps = false;
		decisions.BeginDecision();
		BeginReservations();
		try
		{
		view = WithPlanHorizon( view, guest, target );
		for ( var x = guest.CellX - 2; x < guest.CellX + 2; x++ )
		{
			for ( var y = guest.CellY - 2; y < guest.CellY + 2; y++ )
			{
				if ( x < 0 || x >= 128 || y < 0 || y >= 128 || (x - back.X) * (x - back.X) + (y - back.Y) * (y - back.Y) >= distance )
					continue;
				foreach ( var candidate in NewestFirst() )
				{
					if ( candidate.EntranceCell != (x, y) || candidate.AttractionId == target.AttractionId || candidate.JoinCell == null
						|| !candidate.IsOpen || candidate.IsBrokenDown || candidate.QueueLength >= RideVisitorBridge.PositionsPerCell * candidate.QueueSizeInCells )
						continue;
					// [EXT:smarter-guests-planning] a candidate in the window stands on the guest's way: a policy that plans is told so (the window's rule is the original's definition of "near the route")
					var candidateView = ViewOf( candidate, guest );
					var score = WinterScoreOf( context, guest, view, candidate, decisions.UsesPlanning ? candidateView with { OnTheWay = true } : candidateView );
					if ( decisions.BeatsInMinorDecision( context, view, score, bestScore ) )
						(best, bestScore) = (candidate, score);
				}
			}
		}
		// [EXT:smarter-guests-spread] hysteresis: the target is scored once, with the same policy and inside the same metered decision, and kept unless the best candidate beats it by the policy's margin
		if ( best != null && decisions.UsesSpreading )
		{
			var targetView = ViewOf( target, guest );
			keeps = !decisions.SwitchesTarget( context, view, targetView, WinterScoreOf( context, guest, view, target, targetView ), ViewOf( best, guest ), bestScore );
			if ( keeps )
				TargetsKept++;
			else
				TargetsSwitched++;
		}
		}
		finally
		{
			decisions.EndDecision();
			EndReservations();
		}
		if ( best == null || keeps )
			return;
		var toGuest = RouteLength( guest, target.EntranceCell, (guest.CellX, guest.CellY) );
		var toBest = RouteLength( guest, target.EntranceCell, best.EntranceCell );
		if ( toGuest == -1 || toBest == -1 || toBest >= toGuest )
			return;
		guest.AttractionId = best.AttractionId;
		RouteToCell( guest, best.QueueCells[^1] );
	}

	/// <summary>The original's route length <c>0x004D8B40</c> from <paramref name="from"/> to <paramref name="to"/>: the sum of the legs' |dx| + |dy|, −1 without a route.</summary>
	private int RouteLength( Guest guest, (int X, int Y) from, (int X, int Y) to )
	{
		var path = new List<(int X, int Y)>();
		if ( !WalkSearch.Find( Grid.Cells, (from.X & 0xFF, from.Y & 0xFF), (to.X & 0xFF, to.Y & 0xFF), guest.Navigation.Mask, path ) )
			return -1;
		var total = 0;
		var previous = from;
		foreach ( var cell in path )
		{
			total += Math.Abs( cell.X - previous.X ) + Math.Abs( cell.Y - previous.Y );
			previous = cell;
		}
		return total;
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
	/// "Queue edited" (<c>0xdd57c</c>, tp.exe <c>0x004DE1F0</c>): every queued guest except the called one re-evaluates. A guest whose place
	/// is beyond the new room leaves ("queue was shortened"), unless it is already waiting to be taken (state 14); the others change nothing
	/// here: a guest whose place changed walks to it from its own state 11 (<see cref="UpdateStanding"/>, <c>0x00501160</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x00501390 queue re-evaluation] the only exit is position ≥ 4 × cells (unsigned) with the guest not in state 14; "Queue edited underneath me while Standing In Queue" is the entry log every guest prints, not an exit; the caller 0x004DE1F0 visits every guest in the queue list but the called one (ride +0x6C); executed by the x86 oracle target tpw-exe-queue-reevaluate (ORACLE.md X14)
	// [BIN:TPW-EXE:0x00501160 state 11 re-route] a guest whose place changed (0x004FFFF0 at 0x005002C2) takes the new place's queue slot (0x004DDF50, 0x004DE7E0) and routes to it with the generic route 0x004FA5F0 (state 12), or leaves through the dismissal when the route fails
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
				// [BIN:TPW-EXE:0x00501496 queue edited] before the dismissal a guest whose thing id (the word at thing +0, read at 0x00501483) is a multiple of 3 thinks 13 (Angry, 0x0050BE80); the others think nothing; the guest's id is its thing id
				// [EXT:guest-event-ring] the dismissal 0x005012F0's event 6 with the ride's id is a record of the debug-only guest event ring (0x0050C100), which OpenTPW does not keep
				DismissFromQueue( guest, attraction, guest.Id % 3 == 0 ? GuestThought.Angry : GuestThought.None, QueueEndReason.QueueShortened );
				continue;
			}
			if ( guest.State == GuestState.Boarding )
				continue;   // a boarding guest keeps its walk to the stand point
			var index = IndexOfCell( cells, guest.CellX, guest.CellY );
			// [BIN:TPW-EXE:0x00501160 queue place route] a guest whose cell left the queue is not sent away here: it keeps its place and, when its place changed, state 11 routes it to the new slot from wherever it stands with the generic search (MoveToQueuePosition); the dismissal comes only if that search fails
			guest.QueueCellIndex = index < 0 ? cells.Count : index;
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
			// a guest missing from the list cannot occur in the original (0x004DDF50 asserts, "Problem with a queue"); OpenTPW sends it away like state 11's lookup does
			DismissFromQueue( guest, attraction, GuestThought.None, QueueEndReason.NoPlace );
			return;
		}
		guest.QueuePosition = position;
		var (index, x, y) = QueueSlotPoint( attraction, position, NextLateral() );
		// [BIN:TPW-EXE:0x00501160 queue place route] the generic route request 0x004FA5F0 from wherever the guest stands to the place's slot: a route sets state 12; none leaves "Couldn't get to my intended queue position" (move-up, 0x004FFFF0) or "Couldn't get to my place" (the join, 0x004FFBC0) through the dismissal with no thought, 0x005004AA
		if ( !SetQueueDestination( guest, attraction, index, (x, y), toEntrance: false ) )
		{
			DismissFromQueue( guest, attraction, GuestThought.None, QueueEndReason.NoPlace );
			return;
		}
		EnterGuestState( guest, GuestState.MovingUpQueue );
	}

	/// <summary>
	/// A queue walk (states 12 and 13): from the guest's cell along the queue cells to cell <paramref name="index"/>,
	/// then into the ride's entrance cell for the stand point (<paramref name="toEntrance"/>).
	/// </summary>
	private bool SetQueueDestination( Guest guest, IRideVisitorBridge attraction, int index, (int X, int Y) target, bool toEntrance )
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
		// [BIN:TPW-EXE:0x004D8750 queue walk] a queue walk is the search and edge-test walk of every guest (Navigate): the edge test opens a step between queue cells, and between the join path and the back cell, through the link bytes the queue tool wrote (QueuePaths); the last walk into the ride's entrance cell is the same walk (the stand point, state 13)
		return SetDestination( guest, target, route, alsoOpen );
	}

	/// <summary>The ride's entrance cell: the front queue cell's neighbour towards the ride.</summary>
	private static (int X, int Y) EntranceCellOf( IRideVisitorBridge attraction ) => StandCellOf( attraction );

	/// <summary>The cell a called guest's stand point lies in (the ride's entrance cell; the fallback front cell itself once the queue's outside cell was removed).</summary>
	internal static (int X, int Y) StandCellOf( IRideVisitorBridge attraction )
	{
		var front = attraction.QueueCells[0];
		// [EXT:removable-access-cells] a queue whose outside cell was removed falls back to the nearest path cell as its one-cell front (RideVisitorBridge.RecomputeQueue): guests stand in that path cell itself, where the original's entrance cell (type 9, always the object's own cell, 0x4DE040) is never edited away
		if ( attraction.QueueFrontCell is { } outside && front != outside )
			return front;
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
	// [BIN:TPW-EXE:0x004DE840 queue slot] depth = trunc(255 × 0.25 × the place within the cell); lateral = 0x72 + rand % 28. Up to depth 128 the cell's own link to the front picks the axis: link 1 (−Y) gives (lateral, depth), 4 (+X) (255 − depth, lateral), 16 (+Y) (lateral, 255 − depth), 64 (−X) (depth, lateral). Beyond 128 (the fourth place) the axis is the link of the queue cell behind (0x004DE670), for the back cell the direction from its attached path cell (the first of its connections 1, 4, 16, 64 that leads to a path cell, reversed: RideVisitorBridge.JoinCell), and link 4 then gives depth − 128 instead of 255 − depth
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
			// [BIN:TPW-EXE:0x0050076F refusal records] the refusal's thought 6 (0x0050BE80), event 10 with the ride's id and the dismissal's event 6 (0x0050C100: a ring of 32 (event, argument) pairs at thought +4, index +0x88, a repeat of the last pair skipped) are read only by the guest's debug dump 0x004FAA60 (0x0050C190, its one caller 0x004FAAAB), so OpenTPW keeps no event ring
			RefusePrice( guest, attraction );
		else if ( attraction.PresentForBoarding( guest.Id ) )
			EnterGuestState( guest, GuestState.WaitingToBoard );
	}

	/// <summary>A called guest refuses the price: two medium happiness drops (the refusal and the dismissal), then it leaves the queue.</summary>
	private void RefusePrice( Guest guest, IRideVisitorBridge attraction )
	{
		// [BIN:TPW-EXE:0x00500756 price refusal] "Object is too expensive, I'm leaving the queue": thought 6, then the refusal's own 0x004FEA70(1) before the dismissal's
		guest.Happiness = Math.Max( 0, guest.Happiness - Settings.MediumHappinessChange );
		// [BIN:TPW-EXE:0x0050133D dismissal sound] the dismissal that follows (0x005012F0) plays the guest sound 0x80 for a thing id with its low three bits 0 (Voiced); [EXT:guest-event-ring] its event 6 and the refusal's event 10 are records of the debug-only guest event ring
		// [BIN:TPW-EXE:0x0050077F refusal walk-away] after 0x004FEA70(1) the ride's walk-away count (0x004E1670), then its loading slot (0x004E0AC0) and queue list (0x004DDD20) let the guest go and the dismissal 0x005012F0 runs
		Payments?.WalkedAway( attraction );
		DismissFromQueue( guest, attraction, GuestThought.Dissatisfied, QueueEndReason.Price );
	}

	/// <summary>
	/// The price test at the stand point (<see cref="QueueJoinRules.TooExpensive"/>): rides cost nothing per use. A shop or sideshow with a price
	/// records the guest's view of it in the park's statistics (<see cref="QueueJoinRules.PriceStatistics"/>), whether it pays or refuses.
	/// </summary>
	private bool TooExpensive( Guest guest, IRideVisitorBridge attraction )
	{
		if ( attraction.Kind is not (RideVisitorKind.Shop or RideVisitorKind.Sideshow) )
			return false;
		var terms = PriceTermsOf( attraction );
		if ( Payments != null && terms.Price != 0 )
		{
			var willingness = QueueJoinRules.WillingnessToPay( terms, guest.Happiness, guest.Thirst, guest.Hunger, guest.Nausea );
			foreach ( var (category, value) in QueueJoinRules.PriceStatistics( terms, willingness, attraction.SpecialIngredient, attraction.AppearanceEffect ) )
				Payments.PriceTested( category, value );
		}
		return QueueJoinRules.TooExpensive( terms, guest.Happiness, guest.Thirst, guest.Hunger, guest.Nausea, guest.Money );
	}

	/// <summary>
	/// The price test's words for <paramref name="attraction"/>: the live words the level mirrors from the economy (a free visit's
	/// all-zero terms included); a bridge nobody mirrors into keeps its constructor price.
	/// </summary>
	// [BIN:TPW-EXE:0x004D3E00 per-type price word] the word +0xC of the per-type record the price test reads (0x004E2740 → 0x004D3D10) is copied from the type record's +0x16C, UsageInfo.RipOffOK (key 42 of the table at 0x007456E8); no caller of the record lookup 0x004D3D10 stores that word
	internal static PriceTerms PriceTermsOf( IRideVisitorBridge attraction )
	{
		var live = attraction.Sideshow;
		var price = attraction.HasSideshowTerms ? live.PricePerUse : attraction.Price;
		return new PriceTerms( price, live.CostOfGoods, (100 - live.ChanceOfLosingPercent) & 0xFF, attraction.Kind == RideVisitorKind.Sideshow,
			attraction.InitCostOfGoods, attraction.ThirstEffect, attraction.HungerEffect, attraction.VomitEffect, attraction.HappinessEffect, attraction.RipOffOk );
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
		EnterGuestState( guest, GuestState.Queueing );
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
			// [BIN:TPW-EXE:0x004FFFF0 called forward] the call (flag +0x1F8, the ride's called guest 0x004E0AA0) asks the generic route 0x004FA5F0 to the stand point 0x004DEDF0(0) (the entry cell +0x36 of the ride); a route makes state 13 ("being loaded to ride"), none logs "the player has removed the path from under me and I can no longer get into object" and releases the called slot (0x004E0AC0), unlinks the guest (0x004DDD20) and dismisses it (0x005012F0) through the exit 0x005004AA, with no thought
			// [BIN:TPW-EXE:0x004FFFF0 refused stand-point route] a refused search is the original's own failure ("the player has removed the path from under me and I can no longer get into object"): the called slot is released, the guest unlinked and dismissed with no thought (DismissFromQueue), as a refused queue walk is; a park resumed from an old OpenTPW save has the entrance's link byte repaired at load ([EXT:legacy-save-access-links], ObjectAccessCells.RepairMissingLinks)
			if ( !SetQueueDestination( guest, attraction, 0, StandPoint( attraction ), toEntrance: true ) )
			{
				Log?.Warning( "The player has removed the path from under me and I can no longer get into object" );
				DismissFromQueue( guest, attraction, GuestThought.None, QueueEndReason.NoPlace );
				return;
			}
			EnterGuestState( guest, GuestState.Boarding );
			return;
		}
		// 3. The ride failed: 0xdfe34, a relief object whose wear gauge is below 25 (ORACLE.md F15), not a breakdown. A broken or closed ride
		// dismisses its queue head first from the ride update (RideVisitorBridge.DismissHead), the others keep waiting.
		// [BIN:TPW-EXE:0x004E2440 toilet wear] relief objects wear by use and are cleaned by handymen (ParkEconomy.UseRelief, DispatchToiletCleaning), and the swaps of the RegionFX rows 1 and 6 at the toilet's cell are the original's (0x004D8460 / 0x004D8440 are the remove and add wrappers of the region writer 0x004D8480, ParkEconomy.SwapToiletEffect); the branch for game type 1, the online visit, is ParkEconomy.OnlineVisit
		// [BIN:TPW-EXE:0x005004AA queue exit] every state-11 exit unlinks the guest (0x004DDD20), dismisses it (0x005012F0: "dismissed from queue", the medium happiness drop 0x004FEA70(1), the queue words cleared) and sets state 6; the thought, if any, is set before the jump (DismissFromQueue)
		if ( attraction.ReliefWornOut )
		{
			DismissFromQueue( guest, attraction, GuestThought.FeelingSick, QueueEndReason.ReliefWornOut );
			return;
		}
		// 4. Position lookup.
		var position = attraction.GetQueuePosition( guest.Id );
		if ( position < 0 )
		{
			// [BIN:TPW-EXE:0x00500270 guest missing from its queue] "Problem with a queue - shouldn't be fatal": a log line and the dismissal, no thought
			DismissFromQueue( guest, attraction, GuestThought.None, QueueEndReason.NoPlace );
			return;
		}
		// 5. Moving up: wait out the delay while the gap is at most 2, else walk to the new position.
		// While the ride is in state 1 (broken) 0xed4b0 (0xdfe0c) skips the walk and goes straight to the needs window (0xed58c), so the
		// guests keep their physical places as the head is dismissed.
		// [BIN:STP-PPC:0x100ED4B0 broken ride holds the queue] only state 1 holds (0x004E0370 on the PC); state 4 (condemned, life gauge 0, 0x004E14E0) does not, so the guests behind a dismissed head walk on
		// [BIN:TPW-EXE:0x005002C2 move-up] a changed place decrements the delay (0x005002EB), holds for a broken ride (0x004E0370) or starts the
		// walk (0x00501160, state 12); each continues into the needs window at 0x005002F2, and only an unchanged place reaches the limit test (0x0050059D)
		var moved = position != guest.QueuePosition;
		if ( moved )
		{
			var gap = guest.QueuePosition - position;
			if ( guest.QueueMoveDelay != 0 && gap is >= 0 and <= MoveUpWaitGap )
				guest.QueueMoveDelay--;
			else if ( !attraction.HoldsQueuedGuests )
				MoveToQueuePosition( guest, attraction );
		}
		// 6. Beyond the limit.
		if ( !moved && guest.QueuePosition > attraction.QueueLimit )
		{
			DismissFromQueue( guest, attraction, GuestThought.QueueTooLong, QueueEndReason.QueueLimit );
			return;
		}
		// [BIN:TPW-EXE:0x00500612 coaster or track refuses] at its position, after the limit test, a guest queueing for a coaster (0x004E2B50) that fails 0x00441970, or for a ride of track type 1 whose track word +0x2C is 0, thinks 13 and leaves (0x005004AA)
		if ( !moved && attraction.TrackRefusesGuests )
		{
			DismissFromQueue( guest, attraction, GuestThought.Angry, QueueEndReason.TrackRefuses );
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
				StartInterlude( guest, happiness > 80 ? VisitorSpriteTemplate.Cheer : VisitorSpriteTemplate.Indecision );
				return;
			}
			if ( happiness < (int)LeaveHappiness )
			{
				DismissFromQueue( guest, attraction, GuestThought.Unhappy, QueueEndReason.Unhappy );
				return;
			}
			// [BIN:TPW-EXE:0x005003AA toilet in the queue] a toilet byte above 80 thinks 4 first and then leaves unless the ride gives relief (ride +0x32 bit 0): a guest queueing for a toilet thinks it and stays
			if ( (int)guest.Toilet > (int)ToiletLeaveLevel )
			{
				if ( attraction.ProvidesRelief )
				{
					Think( guest, GuestThought.NeedToilet );
					QueuePatienceLeaves( guest, attraction, position );
					return;
				}
				DismissFromQueue( guest, attraction, GuestThought.NeedToilet, QueueEndReason.NeedToilet );
				return;
			}
			// [BIN:TPW-EXE:0x004FFFF0 needs branch returns] beyond the window every path of the needs branch returns (0x00500311..0x005003A2), so the facing change below runs only within 30 turns of the last interlude
			// [EXT:smarter-guests] the patience hook follows the leave tests on every turn the guest stays; the original policy stays. It can run on the turn a move-up has just started (the walk and the
			// needs window share a turn, as in the original), so a policy can see a guest whose place has just changed (its QueuePosition is the old one until the walk ends); the hook is deliberately not skipped there, which keeps the original's flow
			QueuePatienceLeaves( guest, attraction, position );
			return;
		}
		if ( turn > guest.QueueStandingSinceTurn + BoredomTurns )
		{
			DismissFromQueue( guest, attraction, GuestThought.Bored, QueueEndReason.Bored );
			return;
		}
		// [EXT:smarter-guests] the patience hook again, on the turns the needs window does not cover
		if ( QueuePatienceLeaves( guest, attraction, position ) )
			return;
		// 8. A 1-in-10 facing change of −400..+399 of 2048 of a turn.
		// [BIN:TPW-EXE:0x004FFFF0 queue facing change] one draw (0x00516330) mod 10 must be 0 (else return), then a second draw gives heading +0x1C += (r mod 800) − 400, wrapped by 0x800; OpenTPW draws both from the world generator and turns the heading vector by the same angle
		if ( random.Next( 10 ) != 0 )
			return;
		var angle = (random.Next( FacingChangeRange ) - FacingChangeRange / 2) * MathF.Tau / 2048;
		// [EXT:deterministic-math] the turn of the heading vector in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
		var (sin, cos) = DetMathF.SinCos( angle );
		(guest.HeadingX, guest.HeadingY) = (guest.HeadingX * cos - guest.HeadingY * sin, guest.HeadingX * sin + guest.HeadingY * cos);
	}

	/// <summary>Idle interlude (<c>0xe8b74</c>): +520 = turn, state 8 until turn &gt; +520 + 10, then state 11 again.</summary>
	private void StartInterlude( Guest guest, VisitorSpriteTemplate template )
	{
		guest.BodySprite.QueueTemplate( template );
		guest.InterludeTurn = ParkTurn;
		guest.InQueueInterlude = true;
	}

	/// <summary>
	/// The queue exits of state 11, the queue edit and the price refusal (tp.exe <c>0x005004AA</c>, <c>0x00501496</c>, <c>0x00500756</c>): the thought, then the
	/// dismissal <c>0x005012F0</c>, which lowers happiness by the medium step (<c>0x004FEA70</c> with 1, <c>PeepInfo.MediumHappinessChange</c>, clamped at 0)
	/// before the guest leaves the queue.
	/// </summary>
	// [BIN:TPW-EXE:0x005012F0 dismissal] "Person %d: dismissed from queue on ride %d", event 6 (0x0050C100, [EXT:guest-event-ring]), the sound for one thing id in eight (Voiced), 0x004FEA70(1), the queue words +0x22A/+0x228/+0x1F8/+0x1DC/+0x1F1 cleared, state 6
	private void DismissFromQueue( Guest guest, IRideVisitorBridge attraction, GuestThought thought, QueueEndReason reason )
	{
		NoteQueueEnd( guest, attraction.AttractionId, reason );
		if ( thought != GuestThought.None )
			Think( guest, thought );
		// [BIN:TPW-EXE:0x0050133D dismissal sound] a guest whose thing id has its low three bits 0 plays [0x00803A24] (cat_kids) event 0x80 through 0x004FAA00 before the happiness step; the guest's id is its thing id
		if ( (guest.Id & 7) == 0 )
			Voiced?.Invoke( guest, DismissalVoiceEvent );
		guest.Happiness = Math.Max( 0, guest.Happiness - Settings.MediumHappinessChange );
		LeaveQueue( guest, attraction, GuestThought.None );
	}

	/// <summary>Tells the observer (if any) that the guest's queue attempt ended without a ride; a guest whose join it never saw (a resumed save) is not reported.</summary>
	private void NoteQueueEnd( Guest guest, int attractionId, QueueEndReason reason )
	{
		if ( Observer is { } observer && guest.QueueJoinTurn >= 0 )
			observer.QueueEnded( guest, attractionId, reason, ParkTurn - guest.QueueJoinTurn );
	}

	/// <summary>The guest leaves the queue without riding: state 6 where it stood.</summary>
	// [BIN:TPW-EXE:0x005004AA queue exit position] every state-11 exit (0x005004AA: unlink 0x004DDD20, dismissal 0x005012F0, SetState(6)) and the queue-edit exit write no position: the guest stays where it stood, in state 6, until the decision handler 0x004FEC90 starts its next walk from there (a route search from a queue cell, or a wander along the cell's links minus its own queue link)
	// [APPROX:QUEUE-010] a linked type-9 stand cell owned by this ride preserves position. Unsupported entrance ownership/geometry and broken links still relocate to the front or nearest path; their continuation remains unverified.
	private void LeaveQueue( Guest guest, IRideVisitorBridge attraction, GuestThought thought )
	{
		attraction.LeaveQueue( guest.Id );
		ClearQueueState( guest );
		if ( thought != GuestThought.None )
			Think( guest, thought );
		guest.LastAttractionId = attraction.AttractionId;
		StandAfterQueue( guest, attraction );
	}

	private bool CanStandAfterQueueAtEntrance( IRideVisitorBridge attraction, (int X, int Y) cell )
	{
		if ( attraction is not RideVisitorBridge ride || ride.OwnerWord == 0 || ride.QueueFrontCell is not { } front
			|| ride.QueueEntranceDirection is < 0 or > 3 || cell != StandCellOf( ride )
			|| Grid.Cells.RawTypeAt( cell.X, cell.Y ) != ParkCellTypes.Entrance || Grid.Cells.OwnerAt( cell.X, cell.Y ) != ride.OwnerWord )
			return false;
		var direction = (ride.QueueEntranceDirection + 2) & 3;
		var (dx, dy) = GuestPathGrid.Directions[direction];
		return (cell.X + dx, cell.Y + dy) == front && (Grid.IsQueue( front.X, front.Y ) || Grid.IsWalkable( front.X, front.Y ))
			&& (Grid.LinksAt( cell.X, cell.Y ) & GuestPathGrid.LinkValue( direction )) != 0
			&& (Grid.LinksAt( front.X, front.Y ) & GuestPathGrid.LinkValue( ride.QueueEntranceDirection )) != 0
			&& WalkEdges.Commit( Grid.Cells, cell, front, 0 );
	}

	/// <summary>State 6 where the guest stood, including a linked, owned entrance; unsupported entrance geometry retains the front-cell fallback (QUEUE-010).</summary>
	private void StandAfterQueue( Guest guest, IRideVisitorBridge attraction )
	{
		guest.AttractionId = 0;
		var cell = guest.Navigation.Cell;
		if ( Grid.IsWalkable( cell.X, cell.Y ) || Grid.IsQueue( cell.X, cell.Y ) || CanStandAfterQueueAtEntrance( attraction, cell ) )
		{
			if ( cell != (guest.CellX, guest.CellY) )
				(guest.PreviousCellX, guest.PreviousCellY, guest.CellX, guest.CellY) = (guest.CellX, guest.CellY, cell.X, cell.Y);
			guest.HasWaypoint = false;
			EnterGuestState( guest, GuestState.DoingNothing );
			return;
		}
		var front = attraction.QueueCells[0];
		if ( Grid.IsQueue( front.X, front.Y ) || Grid.IsWalkable( front.X, front.Y ) )
		{
			PlaceOnGrid( guest, front.X, front.Y );
			EnterGuestState( guest, GuestState.DoingNothing );
			return;
		}
		ReturnToPath( guest, JoinCellOf( attraction ) );
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Stand );
	}

	/// <summary>The guest's attraction is gone (removed while the guest queued or waited to be taken).</summary>
	private void ReturnOrphanToPath( Guest guest )
	{
		NoteQueueEnd( guest, guest.AttractionId, QueueEndReason.AttractionRemoved );
		ClearQueueState( guest );
		ReturnToPath( guest, (guest.CellX, guest.CellY) );
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Stand );
	}

	private static void ClearQueueState( Guest guest )
	{
		guest.QueuePosition = -1;
		guest.QueueJoinTurn = -1;
		guest.QueueCalled = false;
		guest.InQueueInterlude = false;
	}

	/// <summary>
	/// State 18 (<c>0x00500A50</c>), the walk out of the park to the gate's crossing cell: one step per park turn; arriving makes state 19, which waits for the arrival vehicle
	/// (<see cref="UpdateAtGate"/>), and a walk that ends stuck is given up ("I'm stuck in the park": mask 0, state 6). The recall of a guest that wants to stay
	/// runs before this in <see cref="UpdateGuest"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x00500A50 state 18] the recall test (counter above 0, happiness byte nonzero, park open and the guest's cell of type 0, 1, 3, 9 or 10, 0x004FA990) clears the mask and returns to state 6 ("Make up your mind"); else the walk step 0x004FA2A0: arrived is state 19 wherever the guest stands (0x00501DB0(0x13)), stuck is "I'm stuck in the park, even though ..." with the mask cleared and state 6
	private void UpdateLeaving( Guest guest, float dt )
	{
		if ( ValidLane( Settings.ArrivalLaneA ) == null && ValidLane( Settings.ArrivalLaneB ) == null )
		{
			guest.State = GuestState.Gone; // a level without an arrival lane has no modelled gate: the guest leaves and vanishes
			return;
		}
		if ( guest.HasWaypoint )
		{
			var result = MoveAlongStep( guest );
			if ( result == WalkResult.Walking )
				return;
			if ( result == WalkResult.Stuck )
			{
				guest.Navigation.Mask = 0;
				guest.ExitSide = null;
				EnterGuestState( guest, GuestState.DoingNothing );
				return;
			}
		}
		guest.ExitSide = null;
		EnterGuestState( guest, GuestState.CrossingRoadHome );
	}

	private void ReturnToPath( Guest guest, (int X, int Y) cell )
	{
		guest.AttractionId = 0;
		if ( !Grid.IsWalkable( cell.X, cell.Y ) )
			cell = Grid.FindNearestWalkable( cell.X, cell.Y ) ?? cell;
		PlaceOnGrid( guest, cell.X, cell.Y );
		guest.State = GuestState.DoingNothing; // Placement only: the caller owns the actual body state entry.
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
		Observer?.QueueBoarded( guest, ride, guest.QueueJoinTurn >= 0 ? ParkTurn - guest.QueueJoinTurn : -1 );
		if ( decisions.UsesSpreading )
			NoteBoarding( ride );
		// [BIN:TPW-EXE:0x501db0 state 16 entry] a ride whose type record +0x100 (UsageInfo.RequiresTeleport) is set starts particle 0x13 at the guest's place (0x4fa470) before its model is taken down
		if ( ride.RequiresTeleport )
			Teleported?.Invoke( guest );
		ClearQueueState( guest );
		guest.State = GuestState.Using;
		BoardBodySprite( guest, ride );
		// [EXT:umbrellas-extension] a brolly shop takes a held balloon back for the happiness step, and any attraction closes an open umbrella (before the model of the balloon is put away as for every ride)
		EnterAttraction( guest, ride );
		// [BIN:TPW-EXE:0x00502156 state 16 entry] the balloon's model is destroyed (0x00475550) and +0x210 cleared; its life +0x214 stays; the thought bubble goes (0x0050C0D0)
		DeleteBalloon( guest );
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
			Observer?.Spent( guest, price, ride.AttractionId );
			MoneySpent?.Invoke( guest, price, ride.AttractionId );
		}
	}

	/// <summary>
	/// The ride update's dismiss found the guest in VAR_LETMEOFF (tp.exe <c>0x004E1410</c>): ExitRide (<see cref="ExitRide"/>) and, when the guest left, the use routine and the state-15 entry
	/// (<see cref="FinishUse"/>). False when the exit is blocked: the guest stays on the ride (state 16).
	/// </summary>
	bool IRideVisitorHost.OnVisitorExitRide( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return true;
		// [EXT:pools-swim] a guest released from a changing room with a pool beside it swims; any other release walks on
		if ( BeginSwim( guest, ride ) )
		{
			FinishUse( guest, ride );
			return true;
		}
		if ( !ExitRide( guest, ride ) )
			return false;
		FinishUse( guest, ride );
		return true;
	}

	/// <summary>
	/// Host teardown: the ride lets go of a guest without the script's let-off (its script halted or faulted, or a save's leftover rider). The guest leaves by ExitRide when its exit is free and is otherwise
	/// set down on the cell beyond the exit, as a guest on a removed ride is (QUEUE-010). A demolished object's riders are left to the removal (<see cref="Unregister"/>).
	/// </summary>
	void IRideVisitorHost.OnVisitorReleased( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		// [BIN:TPW-EXE:0x004FB360 object removed] a demolished object (its runtime stops before the level unregisters it) runs no ExitRide and no use routine for its riders: the removal message gives a rider in state 16 its model back, the small happiness step and state 6
		if ( ride is RideVisitorBridge { IsStopped: true } )
		{
			if ( guest.AttractionId == ride.AttractionId && ride.AttractionId != 0 )
				DropForRemovedAttraction( guest, ride );
			return;
		}
		if ( !BeginSwim( guest, ride ) && !ExitRide( guest, ride ) )
		{
			ReturnToPath( guest, ride.ExitCell );
			EnterGuestState( guest, GuestState.DoingNothing );
		}
		FinishUse( guest, ride );
	}

	/// <summary>
	/// ExitRide (tp.exe <c>0x005014E0</c>): the guest is put at <c>UsageInfo.ExitCellAppearPos</c> in the object's exit cell, and a route is asked to the cell beyond the exit, along the exit cell's queue link
	/// (reversed when the exit cell is the entrance cell); a queue cell there or no route is a blocked exit (false), and the guest stays where it was put, still on the ride. Otherwise it walks to that cell
	/// in state 15 (<see cref="UpdateExitingRide"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x005014E0 ExitRide] "Person %d: ExitRide, leaving ride %d"; the point 0x004DEDF0(1); 0x004FA930 puts the guest there; the exit cell's link byte +0x0D (0x00522850), reversed by 0x004D8C00 when +0x38 equals the entrance cell +0x36 (0x00501587); its neighbour along it (0x004D97E0); a neighbour of type 3 or 9 (0x00536320) or a failed route 0x004FA530 logs "SetDest on ride exit failed, exit of object %d is blocked!" and closes the ride (0x004DF150, RideVisitorBridge.CloseForBlockedExit), returning 0; a route runs the use routine 0x004FD970 and state 15 (0x00501DB0), returning 1
	// [BIN:TPW-EXE:0x004DEDF0 exit point] with its argument 1 the cell is +0x38 - 1 (x = & 0x7F, z = >> 7) and the bytes are ftol(255.0 x f) of ExitCellAppearPosX and Y (type +0xDC, +0xE0) after the rotation +0x10: 90 gives (y, 1 - x), 180 (1 - x, 1 - y), 270 (1 - y, x), as the stand point (argument 0) does with EntryCellStandPos
	// [BIN:TPW-EXE:0x004FA930 teleport] the navigation position is the point's bytes << 8 (0x005100E0), the velocity 0, the previous position the same point (0x00510160) and the guest joins its new cell's list (0x0050B6A0)
	private bool ExitRide( Guest guest, IRideVisitorBridge ride )
	{
		var stand = StandCellOf( ride );
		var exit = ride.ExitAccessCell ?? stand;
		var (bx, by) = StandBytes( ride.ExitCellAppearPos, ride.Rotation );
		Teleport( guest, exit, bx & 0xFF, by & 0xFF );
		var link = Grid.Cells.QueueLinkAt( exit.X, exit.Y );
		if ( exit == stand )
			link = PathLinkWriter.Opposite( link );
		// [BIN:TPW-EXE:0x004D97E0 neighbour along a link] a cardinal bit names the neighbour (0x004D96F0, none outside 0..127); every exit and entrance cell's link is cardinal (the shape's direction symbol, ParkCellMap.WriteFootprint, or the entrance's link to its queue)
		// A bridge without a placed object (test rigs, the measurement scenarios, [EXT:developer-prototype]) has no exit cell of its own: it lets off in the cell it takes its guests in and names the cell beyond as its ExitCell
		var beyond = ride.ExitAccessCell == null ? ride.ExitCell : NeighbourAlong( exit, link );
		if ( beyond is not { } target || Grid.Cells.RawTypeAt( target.X, target.Y ) is (byte)ParkCellType.Queue or ParkCellTypes.Entrance || !RouteToCell( guest, target ) )
			return false;
		(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (guest.X, guest.Y, guest.DistanceWalked, TickCount);
		// [APPROX:GUEST-004] the target +0x1DC is cleared as the visit ends (tp.exe keeps it through state 15 until the next choice)
		guest.AttractionId = 0;
		guest.StateTimer = 0;
		guest.State = GuestState.ExitingRide;
		return true;
	}

	/// <summary>The cell next to <paramref name="cell"/> along a cardinal link bit, or null for another byte or a cell off the map.</summary>
	private (int X, int Y)? NeighbourAlong( (int X, int Y) cell, byte link )
	{
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( GuestPathGrid.LinkValue( direction ) != link )
				continue;
			var (x, y) = (cell.X + GuestPathGrid.Directions[direction].DX, cell.Y + GuestPathGrid.Directions[direction].DY);
			return Grid.Cells.InBounds( x, y ) ? (x, y) : null;
		}
		return null;
	}

	/// <summary>Puts the guest at a point of a cell without a walk, its velocity 0 (tp.exe <c>0x004FA930</c>).</summary>
	private void Teleport( Guest guest, (int X, int Y) cell, int byteX, int byteY )
	{
		guest.OnGrid = true;
		guest.Lane = null;
		(guest.PreviousCellX, guest.PreviousCellY) = (guest.CellX, guest.CellY);
		(guest.CellX, guest.CellY) = cell;
		var navigation = guest.Navigation;
		navigation.OffGrid = false;
		(navigation.X, navigation.Y) = GuestNavigation.BytePoint( cell, byteX, byteY );
		(navigation.VelocityX, navigation.VelocityY) = (0, 0);
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		guest.HasWaypoint = false;
		Occupy( guest );
	}

	/// <summary>
	/// State 15 (tp.exe <c>0x00500900</c>): the guest walks to the cell beyond the exit; a stuck walk drops its target and the guest decides again (state 6), and so does the arrival ("successfully left ride").
	/// </summary>
	// [BIN:TPW-EXE:0x00500900 state 15] the walk step 0x004FA2A0: 1 keeps walking, 2 (stuck) clears +0x1DC and sets state 6; 0 (arrived) takes the major destination +0x1DE when one is set (an object: a route to its back cell 0x004DE110, "Left minor destination, found old major", state 10; a deleted one: "Deleted major dest while I was doing minor" and +0x1DC = 0), else "Person %d: successfully left ride %d" and state 6
	// [APPROX:GUEST-004] OpenTPW keeps no major destination, so every arrival decides again (state 6)
	private void UpdateExitingRide( Guest guest, float dt )
	{
		// [EXT:pools-swim] a guest out of a changing room stands on its path-side exit for ExitRideSeconds, then decides again
		if ( guest.StateTimer > 0 )
		{
			guest.StateTimer -= dt;
			if ( guest.StateTimer <= 0 )
				EnterGuestState( guest, GuestState.DoingNothing );
			return;
		}
		var result = guest.HasWaypoint ? MoveAlongStep( guest ) : WalkResult.Arrived;
		if ( result == WalkResult.Walking )
			return;
		if ( result == WalkResult.Stuck )
			guest.AttractionId = 0;
		EnterGuestState( guest, GuestState.DoingNothing );
	}

	/// <summary>The use routine (tp.exe <c>0x004FD970</c>) and the state-15 entry (<c>0x00501DB0</c>) of a guest that left a ride.</summary>
	private void FinishUse( Guest guest, IRideVisitorBridge ride )
	{
		// [BIN:TPW-EXE:0x501db0 state 15 entry] the same beam when the guest is back on the ground (after 0x4fa030 placed its model)
		if ( ride.RequiresTeleport )
			Teleported?.Invoke( guest );
		guest.LastAttractionId = ride.AttractionId;
		guest.SecondsSinceRide = 0;
		// [BIN:TPW-EXE:0x4FD9B1 visit count] the use routine 0x4FD970 sorts a finished visit by the type's Info.WhichUIType (+0x4AC): 0 raises the rides ridden (+0x1C4), 1 the shops visited (+0x1C8), 2 the sideshows played (+0x1CC), won or lost; any other kind (toilets, bins) counts none
		switch ( ride.Kind )
		{
			case RideVisitorKind.Ride: guest.RidesTaken++; break;
			case RideVisitorKind.Shop: guest.ShopsVisited++; break;
			case RideVisitorKind.Sideshow: guest.SideshowsVisited++; break;
		}
		// [BIN:TPW-EXE:0x4fe1cb till] the use routine's price step (0x4fe1a0, before the lost-sideshow test) takes the object's current price when it is not 0 and plays cat_kids 0xD0 at the guest
		if ( ride.Price != 0 )
			Voiced?.Invoke( guest, TillVoiceEvent );
		Payments?.VisitFinished( ride );
		// [BIN:STP-PPC:0x100E9FD4 used history] the end of a use (0xe9fa4) pushes the attraction's id into the used history (0xea268) before the outcome
		if ( decisions.RememberUse( ContextNow(), ViewOf( guest ), ViewOf( ride, guest ) ).PushUsedHistory )
			PushHistory( guest.UsedHistory, ride.AttractionId );
		// [BIN:TPW-EXE:0x004FD9EE use fatigue] a completed use, won or lost, lowers the meter +0x1B8 by the type's UsageInfo.FatigueEffect (type +0xE8) through the clamped adder 0x004FB4F0, before the use routine; only the save reads the meter (0x004FC026)
		guest.Energy = MeterMath.Add( guest.Energy, -(float)ride.FatigueEffect );
		// [BIN:TPW-EXE:0x004FDA0D lost sideshow] a lost play (win byte +0x1F1 of 0) skips the use routine: "Person lost this sideshow" (0x004FDB8D) costs the medium happiness step (byte 0x0078505C) and thinks 6
		if ( ride.Kind == RideVisitorKind.Sideshow && guest.SideshowWon == false )
		{
			guest.SideshowWon = null;
			guest.Happiness = MeterMath.Add( guest.Happiness, -(float)(Settings.MediumHappinessChange & 0xFF) );
			Think( guest, GuestThought.Dissatisfied );
		}
		else
		{
			ApplyVisit( guest, ride );
			// [BIN:TPW-EXE:0x004FDA1B use satisfaction] after the use routine 0x004FE1E0 the change 3 × ((u8)trunc(happiness) - (u8)trunc(the happiness at joining the queue)) goes to the object's running average (0x004E1E00)
			Payments?.UseSatisfaction( ride, ((int)guest.Happiness & 0xFF) * 3 - ((int)guest.QueueJoinHappiness & 0xFF) * 3 );
		}
		ReleaseBodySprite( guest );
		// [BIN:TPW-EXE:0x00501FD3 state 15 entry] after the use routine (0x005015F4, 0x004FD970) the state-15 entry gives a guest with balloon life its balloon back, except at a balloon shop, whose use routine has just made one
		// [EXT:pools-swim] a swimmer keeps its balloon away until it steps out of the changing room again (EndSwimAtOnce)
		// [BIN:TPW-EXE:0x00502053 balloon give-back] the new model is made like the shop's: the world generator is reseeded with the thing id (0x00516370) and the sprite set (0x00541F70(10)) and the colour (0x00541FD0) are drawn
		if ( guest.BalloonLife > 0 && ride.AppearanceEffect != GuestAppearance.Balloon && !guest.InSwimSession )
		{
			random.Reseed( (ushort)guest.Id );
			random.NextWord();
			random.NextWord();
			CreateBalloon( guest );
		}
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
		NoteQueueEnd( guest, ride.AttractionId, QueueEndReason.RideBroken );
		guest.Happiness = Math.Max( 0, guest.Happiness - Settings.MediumHappinessChange );
		ClearQueueState( guest );
		StandAfterQueue( guest, ride );
	}

	void IRideVisitorHost.OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		NoteQueueEnd( guest, ride.AttractionId, QueueEndReason.RideClosed );
		ClearQueueState( guest );
		Think( guest, GuestThought.Dissatisfied );
		StandAfterQueue( guest, ride );
	}

	/// <summary>
	/// What a use does to the guest (<c>0xea318</c>, docs/reverse/ORACLE.md F24, <see cref="RideOutcome"/>): the happiness gain from the .sam
	/// PerfectRide, GoodRide and OKRide values by the difference between the guest's preferred and the attraction's live excitement, and the
	/// illness rise by excitement / RideVomitDivisor times the stomach factor, which does nothing for an attraction whose live excitement is 0 (shops); then the
	/// type's own effects (<see cref="UseEffects"/>): thirst and hunger fall, illness and happiness rise. A relief object still sets the toilet meter to 0.
	/// </summary>
	// [BIN:STP-PPC:0x100EA318 ride outcome] executed by the PowerPC oracle (ride-outcome, 561 inputs; C# agrees)
	// [BIN:TPW-EXE:0x004FE1E0 use routine] followed in its order: the sideshow prize into the purse (0x004FE231), the outcome, the effects, the small-portion draws (0x004FE43C, 0x004FE48E), the appearance step and, for a won sideshow, the tail (0x004FE802..0x004FE8D9: happiness += trunc(medium step × log2(prize / price)) and one discarded world draw); the guest event records 8, 0x11 and 0x12 (0x0050C100) are read only by the debug dump 0x004FAA60 ([EXT:guest-event-ring])
	private void ApplyVisit( Guest guest, IRideVisitorBridge ride )
	{
		var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
		var before = guest.Happiness;
		// [BIN:TPW-EXE:0x004FE231 sideshow prize] a won play (the lost ones skip this routine) puts the prize, the sideshow's cost of goods, into the guest's purse before the outcome
		if ( ride.Kind == RideVisitorKind.Sideshow )
			guest.Money = SideshowWin.Purse( guest.Money, ride.Sideshow.CostOfGoods );
		(guest.Happiness, guest.Nausea) = RideOutcome.Apply( guest.Happiness, guest.Hunger, guest.Nausea, type.PreferredExcitement, ride.LiveExcitement,
			Settings.PerfectRide, Settings.GoodRide, Settings.OkRide, Settings.RideVomitDivisor );
		// [BIN:TPW-EXE:0x004FDB66 sideshow thought] only a sideshow (thing kind 2) sets a thought at the end of a use: 5 after a win, 6 after a loss (0x004FDC25); rides and shops set none
		if ( ride.Kind == RideVisitorKind.Sideshow )
			Think( guest, (guest.SideshowWon ?? guest.Happiness - before > 0) ? GuestThought.Pleased : GuestThought.Dissatisfied );
		// [BIN:TPW-EXE:0x4FE816 sideshow won count] a sideshow's use routine tail raises the sideshows won (+0x1D0); a lost play skips the routine
		if ( ride.Kind == RideVisitorKind.Sideshow )
			guest.SideshowsWon++;
		guest.SideshowWon = null;
		// the type's own effects follow the outcome in the original's use routine (0xeaaf8: 0xea318, then the effects fragment 0xeabb8)
		var effects = UseEffects.Apply( new GuestMeters( guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea ),
			ride.ThirstEffect, ride.HungerEffect, ride.VomitEffect, ride.HappinessEffect );
		(guest.Happiness, guest.Thirst, guest.Hunger, _, guest.Nausea) = effects;
		// [EXT:smarter-guests-planning] a use that takes hunger off is a meal; the extension remembers when (the original keeps nothing)
		if ( ride.HungerEffect > 0 && decisions.UsesPlanning )
			SmarterGuests?.Meals.Note( guest.BirthId, ParkTurn );
		// [BIN:TPW-EXE:0x4fe303 drink or meal voice] after the thirst and hunger meters the use routine compares the type's thirst effect (+0x144) with its hunger effect (+0x148): the larger thirst plays cat_kids 0x83, the larger hunger 0x84, equal ones nothing
		if ( ride.ThirstEffect != ride.HungerEffect )
			Voiced?.Invoke( guest, ride.ThirstEffect > ride.HungerEffect ? DrinkVoiceEvent : MealVoiceEvent );
		// [BIN:TPW-EXE:0x004FE1E0 litter meter] after the happiness effect the type's LitterEffect (+0x154) is added to the litter meter +0x1B4, clamped to 0..100 ("Litter gone up by %d, is now %d")
		guest.Litter = MeterMath.Add( guest.Litter, ride.LitterEffect );
		var amount = ride.IngredientAmount & 0xFF;
		// [BIN:TPW-EXE:0x004FE436 small portion] for a nonzero HungerEffect, then a nonzero ThirstEffect (0x004FE488): one world draw r each, and (r & 7) + amount + effect below 30 (unsigned) costs the small happiness step (byte 0x00785058) through the clamped adder 0x004FB4F0
		if ( ride.HungerEffect != 0 )
			SmallPortion( guest, amount, ride.HungerEffect );
		if ( ride.ThirstEffect != 0 )
			SmallPortion( guest, amount, ride.ThirstEffect );
		// [BIN:TPW-EXE:0x004FE4CF scaled happiness] then happiness += trunc(amount × HappinessEffect / 100), clamped to 0..100
		guest.Happiness = MeterMath.Add( guest.Happiness, amount * ride.HappinessEffect / 100 );
		// [BIN:TPW-EXE:0x004FE527 special ingredient] the switch on type +0x158 (jump table 0x004FE8E8): Fat adds the amount to the toilet meter, Salt to thirst, Ice adds trunc(amount × ThirstEffect / 100) to thirst (each clamped to 0..100), Sugar raises the speed boost +0xC4 by trunc(6 × amount / 100)
		switch ( ride.SpecialIngredient )
		{
			case FatIngredient:
				guest.Toilet = MeterMath.Add( guest.Toilet, amount );
				break;
			case SaltIngredient:
				guest.Thirst = MeterMath.Add( guest.Thirst, amount );
				break;
			case IceIngredient:
				guest.Thirst = MeterMath.Add( guest.Thirst, amount * ride.ThirstEffect / 100 );
				break;
			default:
				guest.Boost = GuestSpeed.RaiseBoost( guest.Boost, ride.SpecialIngredient, amount );
				break;
		}
		ApplyAppearance( guest, ride.AppearanceEffect, ride.QualityOfGoods );
		// [EXT:umbrellas-extension] the brolly shop's AppearanceEffect 3: one world draw for the colour and an open umbrella (the shop's other effects are those of any shop)
		if ( ride.IsUmbrellaShop )
			GiveUmbrella( guest );
		// [EXT:winter-items] the winter kiosk's AppearanceEffect 4: a head item first, then mittens, no draw (the shop's other effects are those of any shop)
		if ( ride.IsWinterKiosk )
			GiveWarmItem( guest );
		// [EXT:winter-gluehwein] a glühwein stall (a winter shop that is not the kiosk, with the Ice ingredient): with the cold rule on, the guest counts as warmed for a while
		else if ( WinterCold != null && ride.WinterTerms != null && ride.SpecialIngredient == IceIngredient )
			WarmByGluehwein( guest );
		if ( ride.ProvidesRelief )
		{
			// [BIN:TPW-EXE:0x004FE1E0 relief visit] for an object with the relief flag: the toilet wears by the guest's toilet meter as a byte (0x004E2440), the meter goes to 0, and an illness byte above 90 goes to 0 as well
			Payments?.ReliefUsed( ride, (int)((long)guest.Toilet & 0xFF) );
			guest.Toilet = 0;
			if ( ((long)guest.Nausea & 0xFF) > 90 )
				guest.Nausea = 0;
			// [BIN:TPW-EXE:0x004FE7F5 relief speed term] the block ends by setting the toilet speed term +0xC2 to 25 (word 0x0075C7F2), until the guest's next needs phase rewrites it
			guest.ToiletBonus = UmbrellaRules.SpeedTerm( guest.UmbrellaState, GuestSpeed.ToiletBonus );
		}
		// [BIN:TPW-EXE:0x004FE802 sideshow win tail] a won sideshow's routine ends with happiness += trunc(medium step × log2(prize / price)), clamped to 0..100 ("Sideshow won - happiness up"), and one world draw whose value nothing reads (0x004FE8D9 → 0x00516330)
		if ( ride.Kind == RideVisitorKind.Sideshow )
		{
			guest.Happiness = MeterMath.Add( guest.Happiness, SideshowWin.HappinessGain( ride.Sideshow.CostOfGoods, ride.Sideshow.PricePerUse, Settings.MediumHappinessChange ) );
			random.NextWord();
		}
	}

	/// <summary>The special ingredients of <c>UsageInfo.SpecialIngredient</c> (type <c>+0x158</c>) other than Sugar.</summary>
	private const int FatIngredient = 1, SaltIngredient = 2, IceIngredient = 3;
	/// <summary>The small-portion test's limit (tp.exe <c>0x004FE44E</c>, <c>cmp eax, 0x1e</c>).</summary>
	private const uint SmallPortionLimit = 30;

	/// <summary>One small-portion test of the use routine: a world draw, and a small happiness loss when the portion was small.</summary>
	private void SmallPortion( Guest guest, int amount, int effect )
	{
		if ( unchecked( (uint)((int)(random.NextWord() & 7) + amount + effect) ) < SmallPortionLimit )
			guest.Happiness = MeterMath.Add( guest.Happiness, -(float)(Settings.SmallHappinessChange & 0xFF) );
	}

	/// <summary>
	/// The use routine's appearance step (tp.exe <c>0x004FE1E0</c>, after the special ingredient): a balloon shop gives a balloon with a life from the shop's
	/// quality of goods (<see cref="GuestAppearance.BalloonLife"/>); a costume shop takes back the costume the guest wears or puts one on.
	/// </summary>
	// [BIN:TPW-EXE:0x004FE615 appearance effect] type +0x15C: 1 creates the balloon model (+0x210, sprite set 10) and sets +0x214 to the balloon life, guest event 12; 2 with +0x24 == 2 ("Customer returning a costume") sets +0x24 to 0 and the sprite bank +0x20 back, otherwise +0x24 = 2, the costume bank and guest event 11; another value logs "Balance file error"
	// [BIN:TPW-EXE:0x004FE615 balloon life] the balloon's life is BalloonLife of the shop's own quality of goods (+0x18C: 50 at build, 0x004DB389; 50 × the shop window's quality step, 0x004B0AA0, 0x004B0AE0), mirrored from ParkObjectState.QualityOfGoods
	// [BIN:TPW-EXE:0x004FE6FC balloon variant draw] a balloon reseeds the world generator with the guest's thing id (0x00516370) and draws the model's sprite set (0x00541F70 with category 10) and colour (0x00541FD0); returning a costume reseeds the same way (0x004FE6A1) and draws the guest's own bank (category 0), putting one on draws the costume bank (category 2, 0x004FE652) without a reseed
	// [EXT:guest-event-ring] the guest events 11 and 12 are not recorded in the original's 32-entry event ring (0x0050C100, the guest's thing at +0x30): its only reader is the guest destructor's debug dump (0x004FB330 → 0x004F8AE0 → 0x004FAA60 through the getter 0x0050C190, log level 2), so it has no gameplay effect to match and OpenTPW keeps no ring
	private void ApplyAppearance( Guest guest, int effect, int qualityOfGoods )
	{
		switch ( effect )
		{
			case GuestAppearance.Balloon:
				random.Reseed( (ushort)guest.Id );
				random.NextWord();   // the balloon model's sprite set (0x00541F70 with 10) and its colour (0x00541FD0): both draws are taken, their values are not used (GUEST-017)
				random.NextWord();
				CreateBalloon( guest );
				guest.BalloonLife = GuestAppearance.BalloonLife( qualityOfGoods );
				break;
			case GuestAppearance.Costume:
				if ( guest.InCostume )
					random.Reseed( (ushort)guest.Id );
				random.NextWord();   // the sprite bank's variant (the guest's own on return, the costume's on putting one on): taken, not used
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
		foreach ( var guest in guests )
		{
			if ( InPark( guest ) ) { inPark++; happiness += guest.Happiness; }
			switch ( guest.State )
			{
				case GuestState.WalkingAround or GuestState.GoingToRide or GuestState.DoingNothing or GuestState.ThrowingAwayLitter or GuestState.ExitingRide: walking++; break;
				case GuestState.Queueing or GuestState.MovingUpQueue or GuestState.WaitingToBoard or GuestState.Boarding: queueing++; break;
				case GuestState.Using: onRides++; break;
				// [EXT:pools-swim] a swimmer is out walking about the park's pool (counted with the walkers); a guest changing back is indoors like a rider
				case GuestState.Swimming or GuestState.ClimbingOut or GuestState.PoolActivity: walking++; break;
				case GuestState.ChangingOut: onRides++; break;
				case GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth or GuestState.WaitingAtBooth or GuestState.AtTicketBooth: arriving++; break;
				case GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WalkingToVehicle or GuestState.WaitingToGoHome: leaving++; break;
			}
		}
		return new GuestStatistics( inPark, walking, queueing, onRides, arriving, leaving, inPark == 0 ? 0 : happiness / inPark, Admissions );
	}

	/// <summary>
	/// The park information window's Top 3 Thoughts now: the thought ids of the guests standing on a park cell, ranked as the original ranks them (<see cref="TopThoughtRanking"/>).
	/// A read of the guests only: no draw, no write.
	/// </summary>
	// [BIN:TPW-EXE:0x004C8430 top three thoughts] every thing of type 1 on a cell the predicate 0x004FA990 accepts counts, whatever its state: a guest in the booth queue or on the road home stands on a non-park cell and does not
	public TopThoughts GetTopThoughts() =>
		TopThoughtRanking.Rank( guests.Where( guest => guest.State != GuestState.Gone && IsCountedInPark( guest ) ).Select( guest => (int)guest.Thought ) );

	/// <summary>
	/// The park information window's happiness levels: the percent of the guests on park cells whose happiness byte is below 33 (sad) and the percent whose byte is above 66 (happy). A read of the guests only.
	/// </summary>
	// [BIN:TPW-EXE:0x004C9750 sad percent] every guest thing (type 1) on a cell the predicate 0x004FA990 accepts counts; the happiness (+0x19C) truncated to a byte is compared strictly below the argument (0x21 = 33), and the result is count x 100 / guests, truncated, 0 without guests
	// [BIN:TPW-EXE:0x004C9670 happy percent] the same count with the byte strictly above the argument (0x42 = 66)
	public (int Sad, int Happy) GetHappinessLevels() => HappinessLevels( guests.Where( guest => guest.State != GuestState.Gone && IsCountedInPark( guest ) ).Select( guest => guest.Happiness ) );

	/// <summary>The sad and happy percents of the guests' happiness values (<see cref="GetHappinessLevels"/>).</summary>
	public static (int Sad, int Happy) HappinessLevels( IEnumerable<float> happiness )
	{
		int total = 0, sad = 0, happy = 0;
		foreach ( var value in happiness )
		{
			total++;
			var level = MeterMath.Byte( value );
			if ( level < 33 )
				sad++;
			if ( 66 < level )
				happy++;
		}
		return total > 0 ? (sad * 100 / total, happy * 100 / total) : (0, 0);
	}

	// ---- Snapshots (park save/load) ------------------------------------------------------------

	/// <summary>
	/// Captures the whole guest system between ticks as a typed deep copy: the world generator's
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
			WorldGenerator = random.State,
			TickCount = TickCount,
			TimeSeconds = TimeSeconds,
			BodySpriteClockMilliseconds = BodySpriteClockMilliseconds,
			Balloons = CaptureBalloons(),
			NextGuestId = guests.Count == 0 ? 1 : guests.Max( guest => guest.Id ) + 1,
			Things = ownsThings ? things.CaptureSnapshot() : null,
			Admissions = Admissions,
			Departed = Departed,
			VisitorCount = VisitorCount,
			ArrivalTimerStart = arrivalTimerStart,
			DeliveringArrivals = deliveringArrivals,
			SpawnTimer = spawnTimer,
			PendingArrivals = pendingArrivals,
			ArrivalVehicle = arrivalVehicle < 0 ? null : arrivalVehicle,
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
			BodySprite = guest.BodySprite.CaptureSnapshot(),
			BodySpriteInCostume = guest.BodySpriteInCostume,
			BodySpriteHistoryReconstructed = guest.BodySpriteHistoryReconstructed,
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
			ShopsVisited = guest.ShopsVisited,
			SideshowsVisited = guest.SideshowsVisited,
			SideshowsWon = guest.SideshowsWon,
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
			RideWalkCount = guest.RideWalkCount,
			BoothWait = guest.BoothWait,
			Paid = guest.Paid,
			VisitorNumber = guest.VisitorNumber,
			StrandedMark = guest.StrandedMark,
			CellSerial = guest.CellSerial,
			SwimHeading = guest.SwimHeading,
			SwimTurns = guest.SwimTurns,
			SwimStroke = guest.SwimStroke,
			SwimGoalX = guest.SwimGoalX,
			SwimGoalY = guest.SwimGoalY,
			SwimHasGoal = guest.SwimHasGoal,
			SwimRandom = guest.SwimRandom,
			PoolActivityId = guest.PoolActivityId,
			PoolActivityTarget = guest.PoolActivityTarget,
			PoolActivityPhase = guest.PoolActivityPhase,
			PoolActivityTurns = guest.PoolActivityTurns,
			PoolActivityX = guest.PoolActivityX,
			PoolActivityY = guest.PoolActivityY,
			InterludeTurn = guest.InterludeTurn,
			InQueueInterlude = guest.InQueueInterlude,
			Activity = guest.Activity,
			Litter = guest.Litter,
			BinId = guest.BinId,
			PrankFlag = guest.PrankFlag,
			ExitSide = guest.ExitSide,
			BalloonLife = guest.BalloonLife,
			HasBalloon = guest.HasBalloon,
			InCostume = guest.InCostume,
			UmbrellaState = guest.UmbrellaState,
			UmbrellaType = guest.UmbrellaType,
			UmbrellaDueTurn = guest.UmbrellaDueTurn,
			RainMoodDay = guest.RainMoodDay,
			ColdMoodDay = guest.ColdMoodDay,
			WarmItems = guest.WarmItems,
			WarmUntilDay = guest.WarmUntilDay,
			RainMoodAgeStartTurn = guest.RainMoodAgeStartTurn,
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
				Reroutes = navigation.Reroutes,
				OriginalWalk = navigation.CaptureOriginal(),
				Waypoints = navigation.Buffer.ToArray(),
				Mask = navigation.Mask
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
		if ( snapshot.VisitorCount < 0 )
			Error( $"VisitorCount {snapshot.VisitorCount} is negative." );
		if ( !float.IsFinite( snapshot.SpawnTimer ) )
			Error( $"SpawnTimer {snapshot.SpawnTimer} is not finite." );
		// PendingArrivals may be negative: a group cut above the park limit keeps limit − guests, as the original's word does (0x004CF6E5), and ends at the next stop like 0.
		if ( snapshot.PendingArrivals < OnlineArrivalParkLimit - MaximumThings )
			Error( $"PendingArrivals {snapshot.PendingArrivals} is below the lowest cut remainder ({OnlineArrivalParkLimit - MaximumThings})." );
		if ( snapshot.ArrivalVehicle is { } vehicle && (vehicle < 0 || vehicle >= ArrivalVehicleKind.Names.Count) )
			Error( $"ArrivalVehicle {vehicle} is not a vehicle kind (0 to {ArrivalVehicleKind.Names.Count - 1})." );
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
			if ( attractionSnapshot.TypedQueueFront is { } typedFront && !Grid.InBounds( typedFront.X, typedFront.Y ) )
				Error( $"Attraction {attractionSnapshot.AttractionId}'s typed queue front is outside the grid." );
			if ( attractionSnapshot.JoinCell is { } join && !Grid.InBounds( join.X, join.Y ) )
				Error( $"Attraction {attractionSnapshot.AttractionId} join cell ({join.X}, {join.Y}) is outside the map." );
		}

		ValidateBalloons( snapshot, Error );

		// Guests: identity, bounds and per-guest structure first, state cross-references after.
		var poolIds = snapshot.SchemaVersion >= GuestSimulationSnapshot.ThingPoolSchemaVersion;
		if ( poolIds && snapshot.Things is { } ownPool )
			foreach ( var problem in ThingPool.Problems( ownPool ) )
				Error( $"Things: {problem}." );
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
			if ( (guest.BodySprite != null) != (snapshot.BodySpriteClockMilliseconds != null) )
				Error( $"Guests[{index}] body history and its clock epoch must be saved together." );
			if ( guest.BodySprite is { } body )
			{
				try { VisitorSpriteState.ValidateSnapshot( body ); }
				catch ( InvalidDataException error ) { Error( $"Guests[{index}] {error.Message}" ); }
			}
			if ( guest.Id < 1 )
				Error( $"Guests[{index}] id {guest.Id} is not positive." );
			// a save from before the thing pool counts its guests: ids ascend and stay below the counter; with the pool a guest's id is a slot (ids are reused, so the order is the build order)
			if ( poolIds )
			{
				if ( guest.Id > ThingPool.Capacity )
					Error( $"Guests[{index}] id {guest.Id} is not a slot of the thing pool (1..{ThingPool.Capacity})." );
			}
			else
			{
				if ( previous != null && guest.Id <= previous.Id )
					Error( $"Guests[{index}] id {guest.Id} does not follow guest id {previous.Id}; guests are saved in ascending id order." );
				if ( guest.Id >= snapshot.NextGuestId )
					Error( $"Guest {guest.Id} is not below the next guest id {snapshot.NextGuestId}." );
			}
			previous = guest;
			if ( !guestIds.Add( guest.Id ) )
				Error( $"Guest {guest.Id} appears more than once." );
			if ( poolIds && !ownsThings && guest.Id <= ThingPool.Capacity && (!things.IsUsed( guest.Id ) || things.ModelOf( guest.Id ) != SaveThingModel.Guest) )
				Error( $"Guest {guest.Id} is not a guest slot of the park's thing pool." );
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
			if ( guest.ExitSide is not (null or 0 or 1) )
				Error( $"Guest {guest.Id} exit side {guest.ExitSide} is not 0 or 1." );
			if ( guest.BalloonLife is < 0 or > GuestAppearance.MaximumBalloonLife || (guest.HasBalloon && guest.BalloonLife == 0) )
				Error( $"Guest {guest.Id} balloon life {guest.BalloonLife} is outside 0..255 or holds a balloon at 0." );
			// [EXT:umbrellas-save] the umbrella's optional fields: a state 0 to 4, a colour of the set, and a due turn (a multiple of four) only in the states that wait for one (1 and 3)
			if ( guest.UmbrellaState is < UmbrellaRules.StateNone or > UmbrellaRules.StateOpen )
				Error( $"Guest {guest.Id} umbrella state {guest.UmbrellaState} is not 0 to 4." );
			if ( guest.UmbrellaType is < 0 or >= UmbrellaRules.Colours || (guest.UmbrellaState == UmbrellaRules.StateNone && guest.UmbrellaType != 0) )
				Error( $"Guest {guest.Id} umbrella colour {guest.UmbrellaType} is not 0 to {UmbrellaRules.Colours - 1} (and 0 without an umbrella)." );
			if ( guest.UmbrellaDueTurn < 0 || guest.UmbrellaDueTurn % 4 != 0 || (guest.UmbrellaDueTurn != 0 && guest.UmbrellaState is not (UmbrellaRules.StateClosing or UmbrellaRules.StateOpening)) )
				Error( $"Guest {guest.Id} umbrella due turn {guest.UmbrellaDueTurn} is negative, not a multiple of four, or set without a pending open or close." );
			// [EXT:winter-save] the optional warm items: only the defined bits and at most one head item
			if ( !WinterRules.IsValidItems( guest.WarmItems ) )
				Error( $"Guest {guest.Id} warm items {guest.WarmItems} are not a combination of hat (1) or earmuffs (2), not both, and mittens (4)." );
			// [EXT:winter-save] the optional day a glühwein's warmth lasts until: never negative
			if ( guest.WarmUntilDay < 0 )
				Error( $"Guest {guest.Id} warm-until day {guest.WarmUntilDay} is negative." );
			// [EXT:rain-mood-once-per-day] the optional day of the rule's last application: never negative
			if ( guest.RainMoodDay < 0 )
				Error( $"Guest {guest.Id} rain mood day {guest.RainMoodDay} is negative." );
			// [EXT:winter-cold] the optional day of the cold rule's last application: never negative
			if ( guest.ColdMoodDay < 0 )
				Error( $"Guest {guest.Id} cold mood day {guest.ColdMoodDay} is negative." );
			// [EXT:rain-mood-imported-age] the optional load turn of an imported guest: never negative, and only for a guest without a time in the park
			if ( guest.RainMoodAgeStartTurn is { } ageStart && (ageStart < 0 || guest.TimeInParkSeconds != null) )
				Error( $"Guest {guest.Id} rain mood age start {ageStart} is negative or set for a guest with a known time in the park." );
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
			if ( guest.ShopsVisited < 0 || guest.SideshowsVisited < 0 || guest.SideshowsWon < 0 )
				Error( $"Guest {guest.Id} holds a negative shop, sideshow or win count." );
			if ( guest.QueueMoveDelay < 0 )
				Error( $"Guest {guest.Id} QueueMoveDelay {guest.QueueMoveDelay} is negative." );
			if ( guest.QueueCellIndex < 0 )
				Error( $"Guest {guest.Id} QueueCellIndex {guest.QueueCellIndex} is negative." );
			if ( guest.RideWalkCount is < 0 or > 11 )
				Error( $"Guest {guest.Id} RideWalkCount {guest.RideWalkCount} is outside 0..11 (the byte +0x2C resets past 11)." );
			if ( guest.StrandedMark < 0 )
				Error( $"Guest {guest.Id} StrandedMark {guest.StrandedMark} is negative." );
			// [EXT:pools-swim] a swim session names the changing room it started at and holds its own state; a guest outside one holds none of it
			var swimming = guest.State is GuestState.Swimming or GuestState.ClimbingOut or GuestState.ChangingOut or GuestState.PoolActivity;
			if ( swimming && guest.AttractionId == 0 )
				Error( $"Guest {guest.Id} is in state {guest.State} without a changing room." );
			if ( guest.SwimHeading is < 0 or >= PoolSwimSettings.Headings || guest.SwimStroke is < 0 or >= PoolSwimSettings.Strokes || guest.SwimTurns < 0 )
				Error( $"Guest {guest.Id} swim heading {guest.SwimHeading}, stroke {guest.SwimStroke} or turns {guest.SwimTurns} is out of range." );
			// each state's turn count is bounded by what the session can set: a swim by its longest, the climb out by its limit, the change by its duration; every state still has at least one turn to go
			if ( swimming )
			{
				var maximumTurns = guest.State switch
				{
					// swim turns are frozen during an activity (state 25), so it holds the swim's own range
					GuestState.Swimming or GuestState.PoolActivity => PoolSwimSettings.MinimumTurns + PoolSwimSettings.ExtraTurns - 1,
					GuestState.ClimbingOut => ClimbOutLimitTurns,
					_ => PoolSwimSettings.ChangeOutTurns
				};
				if ( guest.SwimTurns < 1 || guest.SwimTurns > maximumTurns )
					Error( $"Guest {guest.Id} in state {guest.State} has {guest.SwimTurns} swim turns left, outside 1..{maximumTurns}." );
				if ( guest.AttractionId != 0 && FindAttraction( guest.AttractionId ) is { IsChangingRoom: false } )
					Error( $"Guest {guest.Id} is in state {guest.State} at attraction {guest.AttractionId}, which is not a changing room." );
			}
			if ( !swimming && (guest.SwimHeading != 0 || guest.SwimTurns != 0 || guest.SwimStroke != 0 || guest.SwimHasGoal || guest.SwimRandom != 0 || guest.SwimGoalX != 0 || guest.SwimGoalY != 0) )
				Error( $"Guest {guest.Id} holds swim fields outside a swim session (state {guest.State})." );
			if ( swimming && guest.SwimHasGoal && (!Grid.InBounds( guest.SwimGoalX >> 16, guest.SwimGoalY >> 16 )) )
				Error( $"Guest {guest.Id} swim goal ({guest.SwimGoalX >> 16}, {guest.SwimGoalY >> 16}) is outside the map." );
			// [EXT:pools-activities] an activity is held in state 25 only; the registry need not hold its id (an activity this build does not register: the guest then ends its session on the room's exit at its next turn)
			if ( guest.State == GuestState.PoolActivity )
			{
				if ( string.IsNullOrEmpty( guest.PoolActivityId ) )
					Error( $"Guest {guest.Id} is in a pool activity without its id." );
				if ( guest.PoolActivityPhase < 0 || guest.PoolActivityTurns < 0 || guest.PoolActivityTurns > PoolActivitySettings.MaximumTurns || !Grid.InBounds( guest.PoolActivityX >> 16, guest.PoolActivityY >> 16 ) )
					Error( $"Guest {guest.Id} pool activity phase {guest.PoolActivityPhase}, turns {guest.PoolActivityTurns} or point ({guest.PoolActivityX >> 16}, {guest.PoolActivityY >> 16}) is out of range." );
			}
			else if ( guest.PoolActivityId != null || guest.PoolActivityTarget != 0 || guest.PoolActivityPhase != 0 || guest.PoolActivityTurns != 0 || guest.PoolActivityX != 0 || guest.PoolActivityY != 0 )
				Error( $"Guest {guest.Id} holds pool activity fields outside state 25 (state {guest.State})." );
			// the word +0x200 keeps the gate's wait (0x00501E6E) after the guest has walked on, so every state may hold up to its largest value
			if ( guest.BoothWait < 0 || guest.BoothWait > GateWaitMaximum )
				Error( $"Guest {guest.Id} BoothWait {guest.BoothWait} is outside 0..{GateWaitMaximum}." );
			if ( guest.VisitorNumber < 0 )
				Error( $"Guest {guest.Id} VisitorNumber {guest.VisitorNumber} is negative." );
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
				ValidateGuestStructure( guest, snapshot.SchemaVersion, Error );
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
				else if ( !inQueue && guest.State is not (GuestState.GoingToRide or GuestState.Using or GuestState.Swimming or GuestState.ClimbingOut or GuestState.ChangingOut or GuestState.PoolActivity) )
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
	private void ValidateGuestStructure( GuestSnapshot guest, int schemaVersion, Action<string> error )
	{
		// a guest saved before schema 9 may walk an arrival lane off the grid (the restore turns it into one on the cell map); from schema 9 on no guest carries a lane
		if ( guest.Lane != null )
		{
			var laneState = guest.State is GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth or GuestState.AtTicketBooth or GuestState.EnteringPark
				or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.LeavingPark;
			if ( schemaVersion >= GuestSimulationSnapshot.ArrivalWalkSchemaVersion )
				error( $"Guest {guest.Id} carries an arrival lane, which a schema {schemaVersion} save does not hold." );
			else if ( !laneState )
				error( $"Guest {guest.Id} is in state {guest.State} but still carries an arrival lane." );
			if ( guest.Lane.Length < 2 )
				error( $"Guest {guest.Id} is in state {guest.State} with an arrival lane of fewer than two cells." );
			else if ( (uint)guest.LaneIndex >= (uint)guest.Lane.Length )
				error( $"Guest {guest.Id} lane index {guest.LaneIndex} is outside its {guest.Lane.Length}-cell lane." );
			if ( guest.OnGrid || !guest.Navigation.OffGrid )
				error( $"Guest {guest.Id} walks a lane but is marked on the path grid." );
			for ( var cell = 0; cell < guest.Lane.Length; cell++ )
			{
				var (x, y) = guest.Lane[cell];
				if ( !Grid.InBounds( x, y ) )
					error( $"Guest {guest.Id} lane cell {cell} ({x}, {y}) is outside the map." );
			}
		}
		else if ( !guest.OnGrid || guest.Navigation.OffGrid )
			error( $"Guest {guest.Id} is in state {guest.State} but is not on the path grid." );
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
		foreach ( var problem in navigation.Problems( guest.Id, Grid.InBounds ) )
			error( char.ToUpperInvariant( problem[0] ) + problem[1..] + "." );
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
		poolActivityVersion++;
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
			if ( guest.Lane != null )
				ConvertLegacyLane( guest );
			if ( guestSnapshot.BodySprite == null )
				ReconstructBodySprite( guest, snapshot.BodySpriteClockMilliseconds ?? BodySpriteSampleAtTick( snapshot.TickCount ) );
			restored.Add( guest );
			restoredById.Add( guest.Id, guest );
		}
		// Pass 2: references — each attraction's queue and riders into its bridge, then the candidate commits as a whole.
		var bridges = new Dictionary<int, RideVisitorBridge>( attractions.Count );
		foreach ( var attraction in attractions )
			bridges.Add( attraction.AttractionId, (RideVisitorBridge)attraction );
		foreach ( var attractionSnapshot in snapshot.Attractions )
			bridges[attractionSnapshot.AttractionId].RestoreSnapshot( attractionSnapshot );
		RestoreThings( snapshot, restored );
		ClearBodySprites();
		guests.Clear();
		guests.AddRange( restored );
		umbrellaHolders = restored.Count( guest => guest.UmbrellaState != UmbrellaRules.StateNone );
		byId.Clear();
		foreach ( var (guestId, guest) in restoredById )
			byId.Add( guestId, guest );
		// [EXT:smarter-guests] the guest snapshot holds no birth ids: the extension's state starts fresh (its first tick gives every guest an id) unless a saved section follows (SmarterGuestsSave.Restore)
		SmarterGuests?.Restore( SmarterGuests.ParkSeed, SmarterGuestsState.FirstBirthId, false );
		seenQueueEdits.Clear();
		foreach ( var (attractionId, seen) in snapshot.SeenQueueEdits )
			seenQueueEdits[attractionId] = seen;
		if ( snapshot.WorldGenerator is { } generator )
			random.Restore( generator );
		TickCount = snapshot.TickCount;
		TimeSeconds = snapshot.TimeSeconds;
		BodySpriteClockMilliseconds = snapshot.BodySpriteClockMilliseconds ?? BodySpriteSampleAtTick( snapshot.TickCount );
		RestoreBalloons( snapshot.Balloons );
		Admissions = snapshot.Admissions;
		Departed = snapshot.Departed;
		VisitorCount = snapshot.VisitorCount;
		arrivalTimerStart = snapshot.ArrivalTimerStart;
		deliveringArrivals = snapshot.DeliveringArrivals;
		spawnTimer = snapshot.SpawnTimer;
		pendingArrivals = snapshot.PendingArrivals;
		arrivalVehicle = snapshot.ArrivalVehicle ?? -1;
		laneToggle = snapshot.LaneToggle;
		ArrivalsEnabled = snapshot.ArrivalsEnabled;
		seenGridVersion = snapshot.SeenGridVersion;
		RebuildOccupants();
		Observer?.Restored( this );
	}

	/// <summary>
	/// The guests' slots of the thing pool after a restore. A simulation with its own pool takes the saved pool (a save from before the pool: a pool of the guests' ids, newest last);
	/// with the park's pool the guests' slots are already there (the park restored it first), or, for a save from before the pool, are taken one by one (ids above the pool's
	/// size, which a counter reaches in a long park, stay outside it).
	/// </summary>
	// [BIN:TPW-EXE:0x00518D43 free list rebuild] a load keeps each saved thing in its own slot and rebuilds the free list from the unused slots in ascending order
	private void RestoreThings( GuestSimulationSnapshot snapshot, List<Guest> restored )
	{
		if ( ownsThings )
		{
			if ( snapshot.SchemaVersion >= GuestSimulationSnapshot.ThingPoolSchemaVersion && snapshot.Things != null )
				things.RestoreSnapshot( snapshot.Things );
			else
				things.Restore( Enumerable.Reverse( restored ).Where( guest => guest.Id <= ThingPool.Capacity ).Select( guest => new ThingSlot( guest.Id, SaveThingModel.Guest ) ) );
			return;
		}
		foreach ( var current in guests )
			things.Release( current.Id );
		foreach ( var guest in restored )
		{
			if ( guest.Id > ThingPool.Capacity || (things.IsUsed( guest.Id ) && things.ModelOf( guest.Id ) == SaveThingModel.Guest) )
				continue;
			if ( !things.Occupy( guest.Id, SaveThingModel.Guest ) )
				throw new InvalidDataException( $"Guest {guest.Id}'s thing slot is taken by a {things.ModelOf( guest.Id )}." );
		}
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
		if ( snapshot.BodySprite is { } body )
			guest.BodySprite.RestoreSnapshot( body );
		guest.BodySpriteInCostume = snapshot.BodySpriteInCostume;
		guest.BodySpriteHistoryReconstructed = snapshot.BodySpriteHistoryReconstructed;
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
		(guest.ShopsVisited, guest.SideshowsVisited, guest.SideshowsWon) = (snapshot.ShopsVisited, snapshot.SideshowsVisited, snapshot.SideshowsWon);
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
		guest.RideWalkCount = Math.Clamp( snapshot.RideWalkCount, 0, 11 );
		guest.BoothWait = Math.Clamp( snapshot.BoothWait, 0, GateWaitMaximum );
		guest.Paid = snapshot.Paid;
		guest.VisitorNumber = Math.Max( 0, snapshot.VisitorNumber );
		guest.StrandedMark = Math.Max( 0, snapshot.StrandedMark );
		guest.CellSerial = Math.Max( 0, snapshot.CellSerial );
		guest.SwimHeading = snapshot.SwimHeading;
		guest.SwimTurns = snapshot.SwimTurns;
		guest.SwimStroke = snapshot.SwimStroke;
		guest.SwimGoalX = snapshot.SwimGoalX;
		guest.SwimGoalY = snapshot.SwimGoalY;
		guest.SwimHasGoal = snapshot.SwimHasGoal;
		guest.SwimRandom = snapshot.SwimRandom;
		guest.PoolActivityId = snapshot.PoolActivityId;
		guest.PoolActivityTarget = snapshot.PoolActivityTarget;
		guest.PoolActivityPhase = snapshot.PoolActivityPhase;
		guest.PoolActivityTurns = snapshot.PoolActivityTurns;
		guest.PoolActivityX = snapshot.PoolActivityX;
		guest.PoolActivityY = snapshot.PoolActivityY;
		guest.InterludeTurn = snapshot.InterludeTurn;
		guest.InQueueInterlude = snapshot.InQueueInterlude;
		guest.Activity = snapshot.Activity;
		guest.Litter = snapshot.Litter;
		guest.BinId = snapshot.BinId;
		guest.PrankFlag = snapshot.PrankFlag;
		guest.ExitSide = snapshot.ExitSide;
		guest.BalloonLife = snapshot.BalloonLife;
		guest.HasBalloon = snapshot.HasBalloon;
		guest.InCostume = snapshot.InCostume;
		guest.UmbrellaState = snapshot.UmbrellaState;
		guest.UmbrellaType = snapshot.UmbrellaType;
		guest.UmbrellaDueTurn = snapshot.UmbrellaDueTurn;
		guest.RainMoodDay = snapshot.RainMoodDay;
		guest.ColdMoodDay = snapshot.ColdMoodDay;
		guest.WarmItems = snapshot.WarmItems;
		guest.WarmUntilDay = snapshot.WarmUntilDay;
		guest.RainMoodAgeStartTurn = snapshot.RainMoodAgeStartTurn;
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
		navigation.RestoreOriginal( navigationSnapshot.OriginalWalk );
		navigation.Mask = navigationSnapshot.Mask;
		Array.Clear( navigation.Buffer );
		if ( navigationSnapshot.Waypoints is { } waypoints )
			waypoints.AsSpan().CopyTo( navigation.Buffer );
		else
			for ( var index = 0; index < navigationSnapshot.Count; index++ )
				navigation.Buffer[index] = navigation.Route[navigationSnapshot.Cursor + index];
	}

	/// <summary>Thing-table part of <see cref="WorldStateHash"/>: gameplay fields, in id order.
	/// TimeInParkSeconds and balloon/body sprite presentation are excluded, like interpolated draw positions.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( TickCount );
		hash.Add( TimeSeconds );
		hash.Add( things.NextId );
		hash.Add( Admissions );
		hash.Add( Departed );
		if ( VisitorCount != 0 )
			hash.Add( 0x56495343L + VisitorCount );
		hash.Add( arrivalTimerStart );
		hash.Add( deliveringArrivals );
		hash.Add( spawnTimer );
		hash.Add( pendingArrivals );
		// Only a park with vehicles has one at work; the vehicle-less hash stays as it was.
		if ( ArrivalVehicles != null )
			hash.Add( arrivalVehicle );
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
			hash.Add( guest.ShopsVisited );
			hash.Add( guest.SideshowsVisited );
			hash.Add( guest.SideshowsWon );
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
			// a guard's drawn way out joins the digest only while a guest has one (every other park keeps its pins)
			if ( guest.ExitSide is { } exitSide )
				hash.Add( 0x45786974L + exitSide );
			hash.Add( guest.BalloonLife );
			hash.Add( guest.HasBalloon );
			hash.Add( guest.InCostume );
			// [EXT:umbrellas-save] the umbrella joins the digest only while a guest has one (every park without one keeps its pins)
			if ( guest.UmbrellaState != UmbrellaRules.StateNone )
			{
				hash.Add( 0x554D4252L + guest.UmbrellaState );
				hash.Add( guest.UmbrellaType );
				hash.Add( guest.UmbrellaDueTurn );
			}
			// [EXT:rain-mood-once-per-day] the day of the rule's last application joins the digest only once it has applied (every park without the extension keeps its pins)
			if ( guest.RainMoodDay != 0 )
				hash.Add( 0x5241494EL + guest.RainMoodDay );
			// [EXT:winter-save] the warm items join the digest only while a guest wears one (every park without one keeps its pins)
			// [EXT:winter-cold] the day of the cold rule's last application joins the digest only once it has applied (every park without the extension keeps its pins)
			if ( guest.ColdMoodDay != 0 )
				hash.Add( 0x434F4C44L + guest.ColdMoodDay );
			if ( guest.WarmItems != 0 )
				hash.Add( 0x57494E54L + guest.WarmItems );
			if ( guest.WarmUntilDay != 0 )
				hash.Add( 0x5741524DL + guest.WarmUntilDay );
			if ( guest.RainMoodAgeStartTurn is { } rainMoodAgeStart )
				hash.Add( 0x52414153L + rainMoodAgeStart );
			hash.Add( guest.QueuePosition );
			hash.Add( guest.QueueJoinTurn );
			hash.Add( guest.LastQueueWaitTurns );
			hash.Add( guest.QueueMoveDelay );
			hash.Add( guest.QueueCalled );
			hash.Add( guest.QueueStandingSinceTurn );
			if ( guest.RideWalkCount != 0 )
				hash.Add( 0x52574B00L + guest.RideWalkCount );
			if ( guest.BoothWait != 0 )
				hash.Add( 0x42574100L + guest.BoothWait );
			if ( guest.Paid )
				hash.Add( 0x50414944L );
			if ( guest.VisitorNumber != 0 )
				hash.Add( 0x56495300L + guest.VisitorNumber );
			if ( guest.StrandedMark != 0 )
				hash.Add( 0x53545200L + guest.StrandedMark );
			// [EXT:pools-swim] the swim session joins the digest only while a guest is in one (every park without a changing room keeps its pins)
			if ( guest.InSwimSession )
			{
				hash.Add( 0x5357494DL + guest.SwimHeading );
				hash.Add( guest.SwimTurns );
				hash.Add( guest.SwimStroke );
				hash.Add( guest.SwimGoalX );
				hash.Add( guest.SwimGoalY );
				hash.Add( guest.SwimHasGoal );
				hash.Add( (long)guest.SwimRandom );
			}
			// [EXT:pools-activities] the activity joins the digest only while a guest does one (every swim without activities keeps its pins)
			if ( guest.State == GuestState.PoolActivity )
			{
				hash.Add( 0x41435456L );
				hash.Add( guest.PoolActivityId ?? "" );
				hash.Add( guest.PoolActivityTarget );
				hash.Add( guest.PoolActivityPhase );
				hash.Add( guest.PoolActivityTurns );
				hash.Add( guest.PoolActivityX );
				hash.Add( guest.PoolActivityY );
			}
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
		// [EXT:pools-slides] the park's water slides join the digest only when there are any (every park without slides keeps its pins)
		PoolSlides?.AddCanonicalState( hash );
		Grid.Cells.PoolBridges?.AddCanonicalState( hash );
	}

	/// <summary>FNV-1a over guest gameplay state, the RNG and counters; display-only TimeInParkSeconds and balloon/body sprite presentation are excluded.</summary>
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
		Add( things.NextId );
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
			// [EXT:umbrellas-save] only while a guest has an umbrella (every park without one keeps its pins)
			if ( guest.UmbrellaState != UmbrellaRules.StateNone )
			{
				Add( 0x554D4252L + guest.UmbrellaState );
				Add( guest.UmbrellaType );
				Add( guest.UmbrellaDueTurn );
			}
			if ( guest.RainMoodDay != 0 )
				Add( 0x5241494EL + guest.RainMoodDay );
			// [EXT:winter-save] only while a guest wears a warm item (every park without one keeps its pins)
			if ( guest.ColdMoodDay != 0 )
				Add( 0x434F4C44L + guest.ColdMoodDay );
			if ( guest.WarmItems != 0 )
				Add( 0x57494E54L + guest.WarmItems );
			if ( guest.WarmUntilDay != 0 )
				Add( 0x5741524DL + guest.WarmUntilDay );
			if ( guest.RainMoodAgeStartTurn is { } rainMoodAgeStart )
				Add( 0x52414153L + rainMoodAgeStart );
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
