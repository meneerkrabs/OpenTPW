namespace OpenTPW;

/// <summary>Incidental cell purchases priced by <c>Costs.*</c>.</summary>
public enum CellPurchase
{
	Path,
	Queue,
	KartTrack,
	WaterTrack,
	Land,
	/// <summary>[EXT:pools-cost] One pool water cell of the <c>opentpw.pools</c> extension (<see cref="ParkEconomy.PoolCellCost"/>).</summary>
	Pool,
	/// <summary>[EXT:pools-cost] One piece of a water slide of the <c>opentpw.pools</c> extension (<see cref="ParkEconomy.SlidePieceCost"/>).</summary>
	SlidePiece
}

/// <summary>
/// The park management simulation: calendar, bank account, prices, construction and scrap,
/// loans, bankruptcy, staff, maintenance, litter, research, challenges, golden tickets and park
/// rating. Deterministic: all randomness comes from the one saved <see cref="WorldRng"/> (the original's world generator) and
/// time advances in whole fixed ticks. Values come from the original settings wherever they exist;
/// every approximation is listed in docs/ECONOMY.md and in the member documentation.
/// </summary>
public sealed partial class ParkEconomy : IParkEconomy
{
	/// <summary>Advisor.sam <c>StaffHireMechanics1.PoorerStateThan</c>: below this state of repair a ride is worn and gets a mechanic.</summary>
	// [DATA:Advisor/Advisor.sam:StaffHireMechanics1.PoorerStateThan]
	public const int WornStateOfRepair = 25;
	/// <summary>Months in the red before bankruptcy: the bank's month posts message type 2 once more than 5 have passed (TAG_SYSTEM 123–127 tell of six).</summary>
	// [DATA:Language/*/TAG_SYSTEM.str:123-127 (six months in the red)]
	public const int MonthsInRedForBankruptcy = 6;
	/// <summary>The bank's month in the red: 30 days of park time (tp.exe <c>0x004D0370</c> divides by <c>0x1792F8648000</c>, 30 × 864e9 in 100 ns units).</summary>
	public const long SecondsPerMonthInRed = 30 * ParkCalendar.SecondsPerDay;

	private readonly SortedDictionary<int, ParkObjectState> objects = new();
	private readonly List<LoanAccount> loans = new();
	private readonly HashSet<int> takenOffers = new();
	private readonly Dictionary<int, bool> loanAvailable = new();
	private readonly List<long> monthlyAdmissions = new();
	private IParkGuestStatistics guestStatistics = NoGuestStatistics.Instance;

	/// <param name="rules">Which systems run and whose money rules apply; null is the whole Theme Park World economy (<see cref="EconomyRules.Full"/>).</param>
	public ParkEconomy( BalanceSettings settings, IEconomyObjectCatalog catalog, ParkGameMode mode, ulong seed, EconomyRules? rules = null )
	{
		Settings = settings ?? throw new ArgumentNullException( nameof( settings ) );
		Catalog = catalog ?? throw new ArgumentNullException( nameof( catalog ) );
		Mode = mode;
		Rules = rules ?? EconomyRules.Full;
		Features = Rules.Narrow( ParkModeFeatures.For( mode ) );
		World = new WorldRng( WorldRng.StateFromSeed( seed ) );
		Ledger = new ParkLedger( settings.InitialCash );
		Staff = new ParkStaff( settings );
		Research = new ParkResearch( settings, catalog, trackRecentItems: !Rules.ThemeParkInc ) { OwnsTicketItem = infoId => OwnsTicketItem( infoId ) };
		Objectives = new ParkObjectives( settings, catalog );
		Counters.Added += ( key, amount ) => Objectives.AccumulateCounter( key, amount, id => objects.GetValueOrDefault( id )?.InfoId ?? 0 );
		Counters.Restored += () => Objectives.RestoreLegacyRawValue( MeasureChallenge, id => objects.GetValueOrDefault( id )?.InfoId ?? 0 );
		EntranceFee = settings.InitialAdmissionFee;
		Litter.Environment.Effects = RegionEffect.Load( settings.Standard );
		Litter.World = () => World;
		// a park without staff has no pool: the world generator is not drawn from for candidates (the settings hold no staff keys then)
		if ( Rules.Staff )
			Staff.CreateInitialPool( World, 0 );
		// [BIN:TPW-EXE:0x004F7E80 calendar constructor] a new park's calendar stores day cache -1 (and the world init 0x005156A0 syncs only the month and year caches to turn 0, 0x004D7AF0 -> 0x004F85A0),
		// so its first update 0x004F8260 posts one day message whatever the date (0x004F8321 compares the computed day with the cache): the pending first day, consumed by the first actual turn
		// ([EXT:tpi-money-only] Theme Park Inc has no traced calendar of its own: its park keeps the synchronised start)
		PendingInitialDay = !Rules.ThemeParkInc;
		if ( Rules.ThemeParkInc && settings.Weather is { } weather )
			TpiWeather = new ThemeParkIncWeatherSpells( weather );
		// [BIN:TPW-EXE:0x004CF7C0 bank constructor] each offer's stored test (+0x14) starts as amount (+0x18) < 10001 (unsigned)
		foreach ( var offer in settings.Loans )
			loanAvailable[offer.Index] = unchecked( (uint)(int)offer.Amount ) < 10001;
	}

	/// <summary>Loads the original settings and objects of <paramref name="theme"/> and starts a new park.</summary>
	public static ParkEconomy CreateForTheme( string theme, bool easy, ParkGameMode mode = ParkGameMode.FullSimulation, ulong seed = 1 ) =>
		CreateForThemeSteps( theme, easy, mode, seed ).Last()!;

	/// <summary><see cref="CreateForTheme"/> in steps (the settings, the catalogue's steps, then the park): null after each part, then the park.</summary>
	internal static IEnumerable<ParkEconomy?> CreateForThemeSteps( string theme, bool easy, ParkGameMode mode = ParkGameMode.FullSimulation, ulong seed = 1 )
	{
		var settings = BalanceSettings.Load( theme, easy );
		yield return null;
		EconomyObjectCatalog? catalog = null;
		foreach ( var loaded in EconomyObjectCatalog.LoadSteps( theme, easy ) )
		{
			if ( loaded == null )
				yield return null;
			else
				catalog = loaded;
		}
		yield return null;
		yield return new ParkEconomy( settings, catalog!, mode, seed );
	}

	public BalanceSettings Settings { get; }
	public IEconomyObjectCatalog Catalog { get; }
	public ParkGameMode Mode { get; }
	public ParkModeFeatures Features { get; }
	/// <summary>Which systems this economy runs and whose money rules apply.</summary>
	public EconomyRules Rules { get; }
	/// <summary>
	/// The park's world generator (tp.exe 0x00516330): the economy's staff pool and the staff walks draw from it, and the level attaches the guests'
	/// and the scripts' draws to the same instance (<see cref="AttachWorld"/>), as the original has one generator for everything.
	/// </summary>
	// [EXT:headless-economy] a headless economy (tests and tools, no staff world) searches on the member's first update, which is OpenTPW's own test and tool design and has no original counterpart (user-approved 2026-10-16, DET-015 converted 2026-10-30); in a level the staff pool's and the staff walks' draws are the world generator's, and the staff constructors' role searches run inside the placing click of a level ([BIN:TPW-EXE:0x004D4340 constructor searches] StaffWorldSimulation.Constructed after the hire's draws: [BIN:TPW-EXE:0x004F89E1 staff base speed] the consumed base-speed draw and the guard's variant draw, see ParkStaff.Hire; no staff constructor reseeds, 0x4F7B7E is the online avatar's); a headless economy has no staff world and searches on the member's first update, the researcher constructor's last call 0x5041F0 runs at a researcher's hire and removal without a draw ([BIN:TPW-EXE:0x005041F0 researchers flag] ParkStaff.LabActive, message 5 or 6 on a change; the advisor's reaction is ADVISOR-009's), and the weather stays off the stream ([BIN:TPW-EXE:0x59A796 advisor pick] the advisor's update takes no draw: all 351 descriptor rows are in the sequential mode 2, the mode-3 draw 0x59A7AC is unreachable)
	public WorldRng World { get; private set; }

	/// <summary>Theme Park Inc's weather spells (null in Theme Park World and where the settings hold no weather).</summary>
	public ThemeParkIncWeatherSpells? TpiWeather { get; private set; }

	/// <summary>The weather thing's state word for the arrival group: 1 heatwave, 2 rain, 3 storm, 4 snow, 0 none.</summary>
	public int TpiWeatherState => TpiWeather?.State ?? 0;

	/// <summary>Makes <paramref name="world"/> this park's generator (the level's shared one); its state is not touched.</summary>
	public void AttachWorld( WorldRng world ) => World = world ?? throw new ArgumentNullException( nameof( world ) );
	public ParkLedger Ledger { get; }
	public ParkStaff Staff { get; }
	public ParkResearch Research { get; }
	public ParkObjectives Objectives { get; }
	public ParkCounters Counters { get; } = new();
	public long Tick { get; private set; }
	public ParkDate Date => ParkCalendar.ToDate( Tick );
	public GameSpeed Speed { get; set; } = GameSpeed.Normal;
	public long Balance => Ledger.Balance;
	public bool IsParkOpen { get; private set; }
	public int EntranceFee { get; private set; }
	/// <summary>
	/// The world is in state 4, the lost park (tp.exe world <c>+0x1DA738</c>, written by <c>0x005168F0</c>): the bank's bankruptcy message put it there.
	/// The per-turn thing updates stop (staff, rides, research, the challenge system; the host stops its guests and staff walks) while the turn counter,
	/// the calendar, the bank's month, the staff pool and the golden-ticket checks go on.
	/// </summary>
	public bool IsBankrupt { get; private set; }
	/// <summary>
	/// Whether a winning sequence is showing (tp.exe <c>0x00516C00</c>: the word <c>0x7CF4F4</c>, the thing the research completion's
	/// <c>StartWinningSequence</c> 0x00516B00 creates); the bank's handler does not make such a park bankrupt. The TPW Instant Action host binds this to its live End object; null answers no.
	/// </summary>
	public Func<bool>? WinningSequenceActive { get; set; }
	/// <summary>Whole 30-day months since the park went into the red, as the last month end counted them (0 in the black).</summary>
	public int MonthsInRed { get; private set; }
	/// <summary>The bank's <c>mWithdrawalsEnabled</c> (+0x114): when clear, a debit charges nothing (tp.exe <c>0x004D01F0</c>). Set in every shipped save.</summary>
	public bool WithdrawalsEnabled { get; private set; } = true;
	/// <summary>
	/// The bank's <c>mBatchBalance</c> (+0x10): a pending credit that the next month message pays into the balance (tp.exe <c>0x004D0370</c>). Nothing in play raises it
	/// (see <see cref="ParkLedger.IsIncome"/>), so it is 0 unless an original save carries one.
	/// </summary>
	public long BatchCredit { get; private set; }
	/// <summary>The bank's <c>mLastBalance</c> (+0x11C): the balance after the last debit; credits leave it.</summary>
	public long LastBalance { get; private set; }
	/// <summary>The bank's turn-entered-red stamp (+0x120): the park turn of the debit that took the balance from 0 or more below 0.</summary>
	public long TurnEnteredRed { get; private set; }
	/// <summary>The park's litter by map cell (dropped by guests, swept by handymen; docs/reverse/WANDER-plan.md §6).</summary>
	public ParkLitter Litter { get; } = new();
	public int TicketsSpent { get; private set; }
	private readonly HashSet<int> ticketItems = new();
	/// <summary>Objects bought with golden tickets; further copies are paid in cash.</summary>
	public IReadOnlyCollection<int> TicketItems => ticketItems;
	public int NextObjectId { get; private set; } = 1;
	public IReadOnlyCollection<ParkObjectState> Objects => objects.Values;
	public IReadOnlyList<LoanAccount> Loans => loans;
	public IReadOnlyList<long> MonthlyAdmissions => monthlyAdmissions;
	/// <summary>
	/// The tickets the HUD counter shows and the purchase check spends: the player's earned tickets less the player's spent count while the park is part of its player's progress, otherwise the park's own.
	/// </summary>
	// [BIN:TPW-EXE:0x005AF5D0 ticket count] the HUD's counter handler 0x4A0FB0 (at 0x4A10CD) and the purchase check 0x4D4AD0 read the player ticket object's earned count (the four global flags once, the six local bytes of every park, the two secret flags) less its spent count (+0x1C)
	public int GoldenTicketsAvailable => TicketWallet is { Active: true } wallet ? wallet.Available : Objectives.GoldenTickets.Count - TicketsSpent;

	/// <summary>The player's ticket account the owner of the player's progress attaches while the park is part of it; null for a park that counts its own tickets.</summary>
	internal IPlayerTicketWallet? TicketWallet { get; set; }

	/// <summary>Whether the item was bought with tickets already: in the player's set while the park is part of its progress, otherwise in the park's.</summary>
	// [BIN:TPW-EXE:0x004D4B70 ticket item owned] asks the player's set (0x5AF780), which every park of the player shares
	internal bool OwnsTicketItem( int infoId ) => ticketItems.Contains( infoId ) || TicketWallet is { Active: true } wallet && wallet.Owns( infoId );
	public IParkGuestStatistics GuestStatistics { get => guestStatistics; set => guestStatistics = value ?? NoGuestStatistics.Instance; }

	public event Action<ParkEvent>? EventRaised;

	/// <summary>
	/// The offers the Available tab lists: not bought and passed at the last list build (<see cref="RefreshLoanOffers"/>); none in Instant Action (UIHELPTEXT 493).
	/// </summary>
	// [BIN:STP-PPC:0x10154AA0 loans window] the Available Loans window only opens outside game type 2 (Instant Action)
	// [BIN:TPW-EXE:0x0049FA00 loan list] the Available tab lists an offer whose bought flag (+0x28) is clear and whose stored test (+0x14) passed; the Bought tab lists the bought ones
	public IEnumerable<LoanOffer> AvailableLoans => !Features.Loans
		? Enumerable.Empty<LoanOffer>()
		: Settings.Loans.Where( offer => !takenOffers.Contains( offer.Index ) && IsLoanAvailable( offer.Index ) );

	/// <summary>An offer's stored credit test (bank offer <c>+0x14</c>), as the last list build or the save left it.</summary>
	public bool IsLoanAvailable( int offerIndex ) => loanAvailable.TryGetValue( offerIndex, out var available ) && available;

	/// <summary>
	/// The loans dialog builds its list (it opens, a tab is selected): every offer's credit test runs and is stored (<see cref="PassesCreditTest"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x0049FA00 loan list] the list build calls 0x004D06B0 for each of the 8 offers; the dialog's open (0x0049FB30) and the tab buttons (0x004A06B0) build it
	public void RefreshLoanOffers()
	{
		foreach ( var offer in Settings.Loans )
			loanAvailable[offer.Index] = PassesCreditTest( offer );
	}

	/// <summary>
	/// The bank's credit test (tp.exe <c>0x004D06B0</c>): the amount is at most the net worth (read with age 20) less the instalments the bought loans still owe
	/// (their months left × the monthly repayment), in 32-bit words.
	/// </summary>
	// [BIN:TPW-EXE:0x004D06B0 credit test] remaining = sum over the bought offers (+0x28) of (months +0x20 − repaid +0x2C) × monthly +0x24; worth = 0x004C7DB0(20) × 1.0 truncated; the offer passes when amount (+0x18) <= worth − remaining (signed), stored at +0x14
	public bool PassesCreditTest( LoanOffer offer )
	{
		var remaining = 0;
		foreach ( var loan in loans )
			remaining = unchecked( remaining + (loan.Months - loan.MonthsRepaid) * (int)loan.MonthlyRepayment );
		return unchecked( (int)offer.Amount ) <= unchecked( NetWorth( 20 ) - remaining );
	}

	/// <summary>
	/// Whether the bank has an offer the park could take now (tp.exe <c>0x004D0810</c>, the advisor's "a loan can still be taken"): walking the eight offers in order,
	/// the first one that is not bought and passes the credit test of <see cref="PassesCreditTest"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x004D0810 loan available] for each offer index 0..7 it runs the credit test 0x004D06B0 (which also rewrites the stored test word +0x14) and returns 1 at the first offer whose bought flag (+0x28) is clear and whose test passed
	// OpenTPW reads the net worth as the credit test does (the cached value while its age is at most 20, else counted fresh) without writing the cache, and leaves the stored tests alone (the advisor is presentation, not simulation state): the loan list rebuilds every offer's test before it shows anything, so the stored word is never read stale
	internal bool CanTakeAnyLoan()
	{
		var remaining = 0;
		foreach ( var loan in loans )
			remaining = unchecked( remaining + (loan.Months - loan.MonthsRepaid) * (int)loan.MonthlyRepayment );
		var worth = PeekNetWorth( 20 );
		return Settings.Loans.OrderBy( offer => offer.Index )
			.Any( offer => !takenOffers.Contains( offer.Index ) && unchecked( (int)offer.Amount ) <= unchecked( worth - remaining ) );
	}

	/// <summary>
	/// The bank's eight loan records as the advisor's money callbacks read them (tp.exe <c>0x004D0690</c>, <c>0x004D0A00</c>, <c>0x004D09A0</c>, <c>0x004D0970</c>): slot <c>i</c> is offer <c>i</c>,
	/// bought when a loan of that offer is outstanding.
	/// </summary>
	internal AdvisorLoanSlot[] LoanSlots()
	{
		var slots = new AdvisorLoanSlot[AdvisorLoanSlot.Count];
		foreach ( var loan in loans )
			if ( loan.OfferIndex is >= 0 and < AdvisorLoanSlot.Count )
				slots[loan.OfferIndex] = new AdvisorLoanSlot( true, loan.Months, loan.MonthsRepaid, unchecked( (int)loan.MonthlyRepayment ) );
		return slots;
	}

	internal IEnumerable<int> ObjectInstanceIds() => objects.Keys;

	public bool TryGetObject( int instanceId, out ParkObjectState state ) => objects.TryGetValue( instanceId, out state! );

	private void Raise( ParkEventKind kind, long amount = 0, int instanceId = 0, int infoId = 0, string detail = "" ) =>
		EventRaised?.Invoke( new ParkEvent( Tick, kind, instanceId, infoId, amount, detail ) );

	private void Post( LedgerCategory category, long amount )
	{
		if ( !ParkLedger.IsIncome( category ) && !WithdrawalsEnabled )
			return;
		var wasInBlack = Ledger.Balance >= 0;
		Ledger.Post( category, amount );
		if ( !ParkLedger.IsIncome( category ) )
			RecordDebit();
		if ( wasInBlack && Ledger.Balance < 0 )
			Raise( ParkEventKind.InTheRed, Ledger.Balance );
	}

	// [BIN:TPW-EXE:0x4d01f0 bank debit] with mWithdrawalsEnabled (+0x114) set, a debit takes the amount off the balance; when the new balance is below 0
	// and mLastBalance (+0x11C) was 0 or more it stamps the current park turn (world +0x1DA70C) at +0x120, then stores the new balance as mLastBalance.
	// A credit (0x004D0190) touches neither word; with the flag clear the debit changes nothing at all
	private void RecordDebit()
	{
		if ( Ledger.Balance < 0 && LastBalance >= 0 )
			TurnEnteredRed = ParkCalendar.Turn( Tick );
		LastBalance = Ledger.Balance;
	}

	/// <summary>
	/// Whole 30-day months the park has been in the red (tp.exe <c>0x004D0370</c>): 0 unless both the balance and the balance after
	/// the last debit are below 0, else the park time since the turn-entered-red stamp (<c>0x004F88B0</c>: turns × 15000 / 4 seconds)
	/// divided by 30 days.
	/// </summary>
	public int MonthsSinceEnteringRed()
	{
		if ( Balance >= 0 || LastBalance >= 0 )
			return 0;
		var turns = Math.Max( 0, ParkCalendar.Turn( Tick ) - TurnEnteredRed );
		return (int)Math.Min( int.MaxValue, turns * ParkCalendar.SecondsPerTurn / SecondsPerMonthInRed );
	}

	// ---------------------------------------------------------------- time

	/// <summary>
	/// One fixed 60 Hz simulation tick: advances the park by one tick (none when paused). A faster <see cref="Speed"/> is the clock
	/// scale of the whole simulation (<see cref="GameSpeedScale"/>): <c>Level</c> feeds the fixed clock scaled time, so guests, rides and
	/// the economy all get more ticks per real second together.
	/// </summary>
	public void AdvanceFixedTick() => Advance( Speed == GameSpeed.Paused ? 0 : 1 );

	/// <summary>
	/// The host's thing sweep (the original's park turn 0x00516380); null keeps the economy's own thing update. The host sets it once the staff, rides and guests it sweeps are attached.
	/// </summary>
	public IThingSweep? ThingSweep { get; set; }

	/// <summary>True when the next <see cref="AdvanceFixedTick"/> enters a new park turn (the clock is not paused and the tick crosses a turn boundary).</summary>
	public bool NextTickStartsTurn => Speed != GameSpeed.Paused && ParkCalendar.Turn( Tick + 1 ) != ParkCalendar.Turn( Tick );

	/// <summary>Advances the simulation by <paramref name="ticks"/> fixed ticks. Each new park turn runs the turn work and the hourly, daily and monthly updates for the calendar boundaries it crosses.</summary>
	public void Advance( long ticks )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( ticks );
		// [BIN:TPW-EXE:0x004D7B20 world turn tail] a lost park (world state 4) keeps its turn counter, calendar and bank: only the thing loop of 0x00516380 stops
		for ( long step = 0; step < ticks; step++ )
		{
			var previousTurn = ParkCalendar.Turn( Tick );
			Tick++;
			var turn = ParkCalendar.Turn( Tick );
			if ( turn != previousTurn )
				AdvanceTurn( previousTurn, turn );
		}
	}

	/// <summary>The golden-ticket check runs every 100 park turns.</summary>
	public const int GoldenTicketCheckInterval = 100;
	/// <summary>The ride update (breakdown check) runs every 8 park turns.</summary>
	public const int RideUpdateInterval = 8;
	/// <summary>The wear step runs every 64 park turns.</summary>
	public const int WearInterval = 64;
	/// <summary>Life gauge lost by each breakdown that the state of repair causes.</summary>
	public const double BreakdownLifeLoss = 5;

	/// <summary>Ride scripts' running state, riders and speed for the wear step; none by default (nothing wears).</summary>
	public IRideOperations RideOperations { get; set; } = NoRideOperations.Instance;
	/// <summary>
	/// The host's coasters for challenge type 18: the most counted sections (the rating's sections with flags 0x40 and 0x80) of the coasters of an Info.Id,
	/// any coaster for 0, and 0 when none matches. Null in a headless economy, which has no coasters (type 18 then measures 0).
	/// </summary>
	public Func<int, long>? CoasterCountedSections { get; set; }
	/// <summary>
	/// The economy instance id of the coaster that <see cref="CoasterCountedSections"/> counts for an Info.Id (0: any), 0 when none: the object the checks of challenge
	/// types 14 and 18 carry into the follow-up (ECON-055). Null in a headless economy.
	/// </summary>
	public Func<int, int>? CoasterBestObject { get; set; }
	/// <summary>
	/// The host's kart rides for challenge type 15 (tp.exe <c>0x004CC4B0</c>): the most crossings of any kart ride now and the economy instance of the first ride that has
	/// them (the newest object first, a strictly higher count replaces); 0 and 0 without a kart. Null in a headless economy, which has no karts.
	/// </summary>
	public Func<(long Crossings, int Instance)>? KartBestCrossings { get; set; }
	/// <summary>
	/// The host's track rides for challenge type 21 (tp.exe <c>0x004CC5D0</c>): the most sections of the kart, bumper and water rides of an Info.Id (any for 0) now, 0 without one.
	/// Null in a headless economy, which has no track rides.
	/// </summary>
	public Func<int, long>? TrackRideSections { get; set; }
	/// <summary>New mechanic jobs await this host world; null keeps new headless jobs immediate.</summary>
	public IMechanicJobWorld? MechanicJobWorld { get; set; }
	/// <summary>The world that moves the handymen and runs their state machine; null keeps the economy's own dispatch (no positions).</summary>
	public IStaffWorld? StaffWorld { get; set; }
	// A mechanic's BusyTicks is the number of park turns (staff updates) until its job completes.

	public long Turn => ParkCalendar.Turn( Tick );

	/// <summary>The park's cheats (docs/CHEATS.md): the cheat mode, the record of applied cheats and the actions waiting for the next turn.</summary>
	public ParkCheats Cheats { get; } = new();

	/// <summary>
	/// Queues a cheat for the start of the next park turn (<see cref="ParkCheats"/>). False when the park cannot cheat (a Theme Park Inc economy: only Theme Park World's cheats are traced), the action is not
	/// valid or the queue is full. The caller refuses read-only visits and online sessions.
	/// </summary>
	// [EXT:cheats-extension] Theme Park World parks only: the cheats are tp.exe's (docs/CHEATS.md); Theme Park Inc's executable was not searched for any
	// [EXT:cheats-window] the extension's own cheats (the weather, the arrivals) need the extension on and a level to act on
	// [EXT:cheats-ride-vehicles] a ride's vehicle limit only for a ride the level can raise now
	public bool QueueCheat( CheatAction action ) => !Rules.ThemeParkInc && (!ParkCheats.IsExtensionKind( action.Kind ) || FeatureGates.Cheats && CheatHost != null)
		&& (action.Kind != CheatKind.RideVehicles || CheatHost!.CanRaiseRideVehicles( action.Argument )) && Cheats.Queue( action );

	/// <summary>
	/// The level's side of the park's cheats (the arrivals and the weather, <see cref="IParkCheatHost"/>); null in a headless economy, which then refuses those cheats. A saved park whose
	/// arrival or weather cheat still waits keeps it waiting in a headless economy: only a level applies it.
	/// </summary>
	public IParkCheatHost? CheatHost { get; set; }

	/// <summary>
	/// What one cheat does to the park (called by <see cref="ParkCheats.ApplyPending"/> at the turn boundary); returns its outcome for the record's event (empty when there is none to tell), or null when
	/// the park cannot take it now and it waits for the next turn.
	/// </summary>
	internal string? ApplyCheat( CheatAction action )
	{
		switch ( action.Kind )
		{
			case CheatKind.Mode:
				// [BIN:TPW-EXE:0x4053C0 cheat mode] the mode byte only (ParkCheats sets it); the message of type 0x15 it posts is not traced and not posted
				return "";
			case CheatKind.Money:
				// [BIN:TPW-EXE:0x40C450 money cheat] the bank's credit 0x4D0190(10000), the path of a challenge prize: the balance (+0xC), money in (park +0x1FC90) and the year's profit (+0x124), so it counts toward the profit ticket
				Post( LedgerCategory.OtherIncome, ParkCheats.MoneyAmount );
				return "";
			case CheatKind.Research:
				// [BIN:TPW-EXE:0x40C430 research cheat] the research manager's 0x503FF0 sets every research item's object control available (+0x10 = 1); progress and completion are not touched
				return Research.MakeEveryItemAvailable().ToString( System.Globalization.CultureInfo.InvariantCulture );
			case CheatKind.Ticket:
				// [BIN:TPW-EXE:0x40C6C0 ticket cheats] the handlers call the player ledger directly (0x409410 → 0x5AF810 / 0x5AF940): no advisor message, no first-ticket lines, no log
				return Objectives.AwardCheatTicket( (GoldenTicketKind)action.Argument ).ToString();
			case CheatKind.Weather:
				// [EXT:cheats-weather] a forecast with no warning: it replaces the pending one and applies now, with the kind set explicitly (no tie-break draw); the natural schedule goes on from it
				return CheatHost?.TryApplyWeather( (CheatWeather)action.Argument, Tick ) == true ? "" : null;
			case CheatKind.Arrival:
				// [EXT:cheats-arrival] a group by the chosen vehicle, sized for it; it waits while a group or a vehicle is at work
				return CheatHost?.TryStartArrival( action.Argument ) == true ? "" : null;
			case CheatKind.RideVehicles:
				// [EXT:cheats-ride-vehicles] the ride raised at once; one removed since it was asked for leaves the cheat recorded with no effect
				return CheatHost is not { } host ? null : host.TryRaiseRideVehicles( action.Argument ) ? "" : "unavailable";
			default:
				throw new InvalidOperationException( $"The cheat {action.Kind} is not applied by the economy." );
		}
	}

	/// <summary>Tells the listeners (the player's ticket ledger, the HUD) that a cheat was applied: <see cref="ParkEvent.InfoId"/> the <see cref="CheatKind"/>, <see cref="ParkEvent.Amount"/> its argument, <see cref="ParkEvent.Detail"/> its outcome.</summary>
	internal void RaiseCheatApplied( CheatAction action, string outcome ) => Raise( ParkEventKind.CheatApplied, action.Argument, 0, (int)action.Kind, outcome );

	private void AdvanceTurn( long previousTurn, long turn )
	{
		crowdPhaseTurn ??= previousTurn;
		// [EXT:cheats-turn-boundary] the queued cheats apply first, before the turn's thing sweep (its weather update and arrival update) and the calendar; a park without any skips this
		Cheats.ApplyPending( this, turn );
		// [BIN:TPW-EXE:0x00516380 world update] the thing loop (staff, rides, guests, the challenge system: 0x0050B360 by thing type) runs only while the world state (+0x1DA738) is not 4;
		// the turn counter before it and the calendar, staff pool, bank and golden-ticket step after it (0x004D7B20) run in every state
		// [EXT:headless-economy] without a host sweep (the headless economy of tests and tools, which has no thing pool of guests, rides and staff to visit) the thing update runs in a fixed order of its own, staff, research, rides, challenges, where a level's park turn visits one list newest first (IThingSweep)
		if ( ThingSweep != null )
			ThingSweep.Run( this, turn, IsBankrupt );
		else if ( !IsBankrupt )
			UpdateThings( turn );
		// [BIN:STP-PPC:0x100E3F0C calendar update] day, month and year events fire when the extracted date fields change from those of the previous turn (ParkCalendar.Update)
		var stored = ParkCalendar.FieldsAtTurn( previousTurn );
		var changes = ParkCalendar.Update( ref stored, turn );
		if ( PendingInitialDay )
		{
			changes |= CalendarChange.Day;
			PendingInitialDay = false;
		}
		var hourBefore = previousTurn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerHour;
		for ( var hour = hourBefore + 1; hour <= turn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerHour; hour++ )
			UpdateHour();
		if ( changes.HasFlag( CalendarChange.Day ) )
			EndObjectDay();
		if ( changes.HasFlag( CalendarChange.Day ) )
			EndDay( previousTurn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay );
		if ( changes.HasFlag( CalendarChange.Month ) )
		{
			EndMonth( (stored.Year - ParkCalendar.Epoch.Year) * ParkCalendar.MonthsPerYear + stored.Month );
			// the calendar's month field is zero-based (mMonthAtLastUpdate); the weather reads the date's month, 1 to 12
			TpiWeather?.OnMonth( stored.Month + 1, stored.Year - ParkCalendar.Epoch.Year, bound => (int)World.Next( (uint)bound ) );
		}
		if ( changes.HasFlag( CalendarChange.Year ) )
			Ledger.StartYear();
		// [BIN:STP-PPC:0x100D67F0 world update] after the calendar update (whose messages the bank, the staff and the other listeners handle at once) the golden-ticket checks (0x100D31D0, 0x100D34C0, 0x100D35B4) run when the turn counter is a multiple of 100, and only in Full Simulation (game type 0)
		if ( Features.GoldenTickets && RunsGoldenTicketCheck( turn, Mode ) )
			CheckGoldenTickets();
		// [BIN:TPW-EXE:0x0054F82D crowd read] after the substep that ran the world update, when the step counter 0x00877D34 (0 when the loop starts, +1 per 31 ms substep, a park turn every 8th) has its low five bits clear,
		// i.e. after every fourth turn counted from the turn the counter started at, the main loop reads the crowd (0x004C81E0 → 0x004C7FA0 with age 0): the cached census is counted again whenever (turn >> 2) moved
		if ( (turn - crowdPhaseTurn.Value) % CrowdReadTurns == 0 )
			CachedGuestCensus( 0 );
	}

	/// <summary>The park turns between the main loop's crowd reads: 32 substeps of 31 ms, four turns.</summary>
	public const int CrowdReadTurns = 4;

	/// <summary>One staff update's job timer step of a mechanic: the timer counts down by one and the update that finds it zero completes the repair.</summary>
	// [BIN:STP-PPC:0x100D9E04 repair state] each staff update counts a mechanic's job timer down by one; the update that finds it zero completes the repair
	internal void CountDownJob( StaffMember member )
	{
		if ( member.BusyTicks == 0 )
			return;
		if ( member.AwaitingJobArrival )
		{
			if ( OwnsMechanicJob( member ) && MechanicJobWorld?.HasArrived( member ) == true )
				member.AwaitingJobArrival = false;
			// Host policy: the arrival-observation update starts work; following updates consume the reserved timer.
			return;
		}
		member.BusyTicks--;
		if ( member.BusyTicks == 0 )
		{
			if ( !FinishJob( member ) )
			{
				// the broken ride's fix animation still runs (its script's VAR_BROKEN): the next staff update looks again
				member.BusyTicks = 1;
				return;
			}
			StrikeOrRest( member, HasRestArea );
		}
	}

	/// <summary>The economy's part of the original's per-turn thing loop: staff, mechanics' jobs, research and the ride update.</summary>
	internal void UpdateThings( long turn )
	{
		// [EXT:tpi-money-only] a park without staff runs none of this: the staff, the research lab, the challenge system and the ride update (whose wear and breakdowns only a mechanic repairs) are refused with the staff, research and challenges
		if ( !Rules.Staff )
			return;
		UpdateStaffVitals();
		foreach ( var member in Staff.Members )
			CountDownJob( member );
		if ( StaffWorld != null )
			StaffWorld.UpdateStaff( this, turn );
		else
			DispatchHandymen();
		// [BIN:STP-PPC:0x100F0284 researcher update] each researcher not in an excluded state does research when the turn counter is a multiple of 20
		if ( turn % ParkResearch.TurnsPerResearch == 0 )
			DoResearch();
		// [BIN:TPW-EXE:0x004E0B90 object update] every live object thing runs its update each turn; the wear and the breakdown check inside it run when the turn counter is a multiple of 8
		UpdateRides( turn );
		// [BIN:TPW-EXE:0x0050B360 thing update] the challenge system is a thing (type 0x13) of the same loop, which a lost park no longer runs
		UpdateChallenges( turn );
	}

	/// <summary>
	/// Staff vitals, once per park turn (ECON-014, docs/reverse/ORACLE.md F22). A member on strike waits for its role's strike to end; a resting
	/// one regains energy and happiness until the energy byte reaches 100; a working one (a mechanic on a job) loses the working rate; a free one
	/// goes on strike when its role does, otherwise a walking one loses the moving rate and, with the energy at the rest level, goes to rest
	/// (when a rest area exists) or loses happiness one turn in sixteen (when none does).
	/// </summary>
	// [BIN:TPW-EXE:0x00508E70 month gate] staff vitals and strikes follow the traced routines: the month gate 0x00508E70, the escalation 0x00508F70 and the grievance 0x00509360; the month message comes from the calendar update 0x004F8260 with no game-type test, and the age stamp 0x004F8800 counts floor(turn × 3750) seconds from world turn 0
	// [BIN:TPW-EXE:0x004C7FA0 cached census] the gate counts the guests through the analyser's cached census with age 5 (CachedGuestCensus), which the main loop's crowd read refreshes with age 0 after every fourth turn counted from the turn the step counter 0x00877D34 started at (0x0054F82D, 0x004C81E0)
	// [BIN:TPW-EXE:0x005089C0 forced strike] the forced-strike word is 0 from the constructor and written only by the save loader 0x00508BB0, so the gate sees 0 in play as here
	private void UpdateStaffVitals()
	{
		if ( Staff.Members.Count == 0 )
			return;
		var restArea = HasRestArea;
		foreach ( var member in Staff.Members )
			UpdateStaffVitals( member, restArea );
	}

	/// <summary>One member's vitals step of a park turn (the body of <see cref="UpdateStaffVitals()"/>); <paramref name="restArea"/> is <see cref="HasRestArea"/>.</summary>
	internal void UpdateStaffVitals( StaffMember member, bool restArea )
	{
		// A member a world drives walks to its rest area or the strike area and rests or strikes in its own state machine (the generic handlers
		// 0x00505FE0, 0x005061D0, 0x005056E0, 0x00506300); only the role's strike ends here, when a striker sees the park closed and empty.
		if ( DrivenByWorld( member ) )
		{
			if ( member.State == StaffState.OnStrike && !IsParkOpen && guestStatistics.GuestCensus == 0 )
				Staff.Strikes.End( member.Type );
			return;
		}
		// [EXT:headless-economy] without a staff world (the gate harnesses and tests; a level with guests always attaches one, Level.SetupStaff) the vitals skip the state machine: walking members drain every turn, working means a job in progress, resting and striking need no walk, idle members are not told apart and the rest search runs every turn; in a level the original's state machines run them (ECON-022)
		switch ( member.State )
		{
			case StaffState.PickedUp:
				return;
			case StaffState.OnStrike:
				// [BIN:STP-PPC:0x100F3F64 strike turn] a striking member that sees the park closed and without guests (0x100C3684) ends its role's strike (0x100F40E8 / 0x100F8BD0); once the role's flag is clear it walks back to work
				// [BIN:TPW-EXE:0x00506300 strike turn] the park-closed word (0x0051A280) and the uncached census 0x004C9130 (guests on a counted cell)
				if ( !IsParkOpen && guestStatistics.GuestCensus == 0 )
					Staff.Strikes.End( member.Type );
				if ( !Staff.Strikes.IsActive( member.Type ) )
					member.State = StaffState.Patrolling;
				return;
			case StaffState.Resting:
				// [BIN:STP-PPC:0x100F4D00 rest release] the original frees the member's rest-area association (+516) when the rest ends; what it does when the rest thing is removed during the rest is not traced, so OpenTPW ends the rest at once when no rest area is left
				if ( !restArea )
				{
					member.State = StaffState.Patrolling;
					return;
				}
				if ( StaffVitals.Rest( member, Settings.RecuperationRate[member.Grade], Settings.HappinessRecuperationRate[member.Grade] ) )
					member.State = StaffState.Patrolling;
				return;
		}
		if ( member.BusyTicks > 0 )
		{
			StaffVitals.Work( member );
			return;
		}
		if ( member.State == StaffState.Patrolling && !Staff.Strikes.IsActive( member.Type ) )
			StaffVitals.Walk( member );
		StrikeOrRest( member, restArea );
	}

	/// <summary>The strike and rest gate of a world-driven member's search (tp.exe <c>0x00506A40</c>): true when the member now strikes or rests.</summary>
	internal bool StaffStrikeOrRest( StaffMember member ) => StrikeOrRest( member, HasRestArea );

	/// <summary>Whether an attached world runs this member's state machine (every role once a level attaches one).</summary>
	internal bool DrivenByWorld( StaffMember member ) => StaffWorld != null;

	/// <summary>
	/// Whether a ride needs a mechanic (tp.exe <c>0x004DAA90</c> and the arrival <c>0x004DA740</c>): broken down (state 1, <c>0x004E0370</c>) or being
	/// upgraded (state 2, <c>0x004E03E0</c>); the maintenance request (<c>+0x64</c>, <c>0x004E03D0</c>) has no control in OpenTPW (ECON-049).
	/// </summary>
	internal bool NeedsMechanic( int instanceId ) => objects.TryGetValue( instanceId, out var item ) && (item.Kind == ParkObjectKind.Ride && JobRank( item ) is 0 or 1
		|| item.MaintenanceRequested && !(Catalog.TryGet( item.InfoId, out var info ) && info.ProvidesRelief));

	/// <summary>
	/// The player calls a mechanic to an object or cancels the call (tp.exe <c>0x004DFE30</c>, from the ride panel's button <c>0x0048CE50</c>): the call sets
	/// <c>mRequestedService</c> and closes a ride; the cancel clears it, and the ride reopens when its state allows (<see cref="RideOpening.MayOpen"/>).
	/// A toilet's call brings a handyman instead. False when there is no such object.
	/// </summary>
	// [BIN:TPW-EXE:0x004DFE30 request maintenance] on: nothing when +0x64 is set; else +0x64 = 1, the ride closes (mCanLoad +0x68 = 0, the guest being loaded +0x6C dropped, script variable 6 = 1) and its panel shows the call; off: +0x64 = 0, and unless the ride is broken down, condemned or upgrading (states 1, 4, 2), its queue is cut off (0x004DE4A0) or a track ride's circuit is broken, it opens (+0x68 = 1, variable 6 = 0, 0x004E0E60(0))
	public bool RequestMaintenance( int instanceId, bool on )
	{
		if ( !objects.TryGetValue( instanceId, out var item ) )
			return false;
		if ( item.MaintenanceRequested == on )
			return true;
		item.MaintenanceRequested = on;
		Raise( on ? ParkEventKind.MaintenanceRequested : ParkEventKind.MaintenanceCancelled, 0, item.Id, item.InfoId );
		return true;
	}

	/// <summary>Park turns a claim holds after it was taken before it can lapse (tp.exe <c>0x004E0220</c>).</summary>
	public const int ClaimLapseTurns = 100;

	/// <summary>A world member claims the object (tp.exe <c>0x004E01F0</c>): the claim word takes his id and the claim turn the current turn.</summary>
	// [BIN:TPW-EXE:0x004E01F0 claim] +0x5E = the claimant's id, +0x60 = game +0x1DA70C (the turn)
	internal void Claim( ParkObjectState item, StaffMember member )
	{
		item.MechanicId = member.Id;
		item.ClaimTurn = Turn;
	}

	/// <summary>
	/// The object's claimant (tp.exe <c>0x004E0220</c>), 0 for none. A claim taken more than <see cref="ClaimLapseTurns"/> turns ago lapses here,
	/// unless its claimant still targets the object: a mechanic's ride (<c>+0x218</c>) or a handyman's toilet (<c>+0x21A</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x004E0220 claimant] +0x5E zero: none; the turn above +0x60 + 100 (unsigned) and the claimant's 0x00506580 false (type 4: +0x218 is the object, type 5: +0x21A is, other types "Invalid call" and false): "Ride: removing staff member", +0x5E = 0 and +0x60 = 0
	internal int Claimant( ParkObjectState item )
	{
		if ( item.MechanicId == 0 )
			return 0;
		if ( unchecked( (uint)Turn ) > unchecked( (uint)(item.ClaimTurn + ClaimLapseTurns) ) )
		{
			var claimant = Staff.Members.FirstOrDefault( member => member.Id == item.MechanicId );
			var targets = claimant?.Type switch
			{
				StaffType.Mechanic => claimant.RideId == item.Id,
				StaffType.Handyman => claimant.ToiletId == item.Id,
				_ => false
			};
			if ( !targets )
			{
				item.MechanicId = 0;
				item.ClaimTurn = 0;
				return 0;
			}
		}
		return item.MechanicId;
	}

	/// <summary>Headless assigned jobs retain their existing lifetime; other claims use the original's lazy expiry.</summary>
	private int HeadlessClaimant( ParkObjectState item )
	{
		// Candidate-list construction also runs with a staff world attached, but no headless job can be assigned there.
		// Do not introduce a new world claim-query phase merely by building that unused list.
		if ( StaffWorld != null )
			return item.MechanicId;
		if ( item.MechanicId == 0 )
			return 0;
		var member = Staff.Members.FirstOrDefault( member => member.Id == item.MechanicId );
		// CountDownJob keeps the assignment while its timer is nonzero, including awaiting arrival and the fix-animation retry.
		// These jobs use AssignedInstanceId rather than the world's RideId/ToiletId checked by Claimant.
		if ( member != null && !DrivenByWorld( member ) && member.AssignedInstanceId == item.Id && member.BusyTicks != 0 )
			return member.Id;
		return Claimant( item );
	}

	/// <summary>The mechanic HQ's cursor into the object list (<see cref="FindRideForMechanic"/>): the object a search last started at, 0 for none.</summary>
	public int MechanicCursor { get; internal set; }

	/// <summary>
	/// The mechanics' ride finder (tp.exe <c>0x004DAA90</c>): one cursor on the mechanic HQ walks the object list (newest first) round from the
	/// object after the previous search's start, once; it takes the nearest object, by squared distance between <paramref name="cellOf"/>'s cells,
	/// that needs a mechanic and whose claim is free or <paramref name="mechanic"/>'s, the first met on a tie. A search whose previous start was the
	/// list's last object finds nothing, and the next one starts at the head.
	/// </summary>
	// [BIN:TPW-EXE:0x004DAA90 ride finder] the HQ word +0xC: when nonzero and found in the list (game +0x1DA746, next +0xC) it moves to that object's next (0 past the end), else it takes the head; start = it; 0 returns none; then each object: the cursor moves to its next (the head past the end), the object passes on state 1 (0x004E0370), mRequestedService on a non-toilet (0x004E03D0, flag +0x32 bit 0) or state 2 (0x004E03E0), with its claim (0x004E0220) free or the mechanic's; d = dx² + dy² of the cell bytes, strictly smaller replaces from 0xFFFFFFFF; until the cursor is back at the start
	internal ParkObjectState? FindRideForMechanic( StaffMember mechanic, (int X, int Y) cell, Func<int, (int X, int Y)?> cellOf )
	{
		var list = objects.Keys.OrderByDescending( id => id ).ToList();
		var positions = new Dictionary<int, int>( list.Count );
		for ( var index = 0; index < list.Count; index++ )
			positions[list[index]] = index;
		int Next( int id ) => positions.TryGetValue( id, out var index ) && index + 1 < list.Count ? list[index + 1] : 0;
		var cursor = MechanicCursor;
		cursor = cursor != 0 && positions.ContainsKey( cursor ) ? Next( cursor ) : list.Count > 0 ? list[0] : 0;
		MechanicCursor = cursor;
		if ( cursor == 0 )
			return null;
		var start = cursor;
		ParkObjectState? best = null;
		var bestDistance = uint.MaxValue;
		do
		{
			var current = objects[cursor];
			cursor = Next( cursor );
			if ( cursor == 0 )
				cursor = list[0];
			if ( !NeedsMechanic( current.Id ) || Claimant( current ) is not 0 && current.MechanicId != mechanic.Id || cellOf( current.Id ) is not { } at )
				continue;
			var dx = at.X - cell.X;
			var dy = at.Y - cell.Y;
			var distance = (uint)(dx * dx + dy * dy);
			if ( distance < bestDistance )
				(best, bestDistance) = (current, distance);
		}
		while ( cursor != start );
		MechanicCursor = cursor;
		return best;
	}

	/// <summary>The repair or upgrade time a mechanic sets on arrival (tp.exe <c>0x004DA370</c> state 13, <see cref="RepairTimer"/>).</summary>
	internal long MechanicWorkTime( StaffMember member, int instanceId )
	{
		var item = objects[instanceId];
		int? upgradeDuration = ObjectState( item ) == RideObjectStates.Upgrading && Catalog.TryGet( item.InfoId, out var info ) && item.PendingLevel < info.Upgrades.Count
			? info.Upgrades[item.PendingLevel].DurationOfUpgrade : null;
		return RepairTimer( Settings[StaffType.Mechanic].WorkDuration[member.Grade], item.Repair, upgradeDuration );
	}

	/// <summary>A world mechanic finishes his job (tp.exe <c>0x004DF8F0</c>): the repair or the upgrade, as <see cref="FinishJob"/>.</summary>
	/// <returns>False while the broken ride's fix animation still runs (its script's VAR_BROKEN): the mechanic keeps the job and tries again.</returns>
	internal bool FinishMechanicJob( StaffMember member )
	{
		member.AssignedInstanceId = member.RideId;
		var state = member.State;
		if ( !FinishJob( member ) )
		{
			member.AssignedInstanceId = 0;
			return false;
		}
		member.RideId = 0;
		member.State = state;
		return true;
	}

	/// <summary>The member's role is on strike (the strike flag 0x00509990).</summary>
	internal bool RoleOnStrike( StaffType type ) => Staff.Strikes.IsActive( type );

	/// <summary>Whether a rest area (an object with <c>UsageInfo.ChillsYouOut</c>) with this instance id stands in the park.</summary>
	internal bool IsRestArea( int instanceId ) => objects.TryGetValue( instanceId, out var item ) && Catalog.TryGet( item.InfoId, out var info ) && info.ChillsYouOut;

	/// <summary>The <c>RegionFX</c> row a member spreads where it stands: the entertainers' 0, the guards' 3 (security); null for the other roles.</summary>
	public static int? EffectRow( StaffType type ) => type switch
	{
		StaffType.Entertainer => RegionEffect.Entertainer,
		StaffType.Guard => RegionEffect.Guard,
		_ => null
	};

	/// <summary>
	/// Puts an entertainer's or a guard's <c>RegionFX</c> row on <paramref name="cell"/> (tp.exe pre-updates <c>0x004D4660</c> and <c>0x004D6360</c>: when
	/// the member's cell changed, the row comes off the old cell (<c>0x004D8460</c>) and onto the new one (<c>0x004D8440</c>)); null takes it off.
	/// </summary>
	public void MoveStaffEffect( StaffMember member, (int X, int Y)? cell )
	{
		if ( EffectRow( member.Type ) is not { } row || member.EffectCell == cell )
			return;
		if ( member.EffectCell is { } old )
			Litter.Environment.Apply( row, old.X, old.Y, add: false );
		if ( cell is { } now )
			Litter.Environment.Apply( row, now.X, now.Y, add: true );
		member.EffectCell = cell;
	}

	/// <summary>
	/// An original save holds its environment grid (the region effect records of its cell grid): once the litter, the staff and the objects have spread their rows again, what the saved grid holds beyond
	/// them is kept as the grid's residue (<see cref="ParkEnvironment.ImportSaved"/>). Returns the number of cells the save holds a record for and the number whose words differ from the rebuilt ones.
	/// </summary>
	// [BIN:TPW-EXE:0x4D7EA0 cell manager serializer] the region grid is saved and loaded with the cells (status byte bit 4, ten bytes a cell), and a loaded thing adds no row again: the saved grid is the grid after the load
	internal (int Records, int Different) ImportSavedEnvironment( SaveWorld world )
	{
		ArgumentNullException.ThrowIfNull( world );
		var records = 0;
		var different = Litter.Environment.ImportSaved( ( x, y ) =>
		{
			if ( x >= world.Cells.CountX || y >= world.Cells.CountY )
				return null;
			var words = world.Cells[x, y].RegionWords;
			if ( words != null )
				records++;
			return words;
		} );
		return (records, different);
	}

	/// <summary>A load rebuilt the environment grid from the litter alone: the saved entertainers and guards put their rows back.</summary>
	internal void ApplyStaffEffects()
	{
		foreach ( var member in Staff.Members )
			if ( EffectRow( member.Type ) is { } row && member.EffectCell is { } cell )
				Litter.Environment.Apply( row, cell.X, cell.Y, add: true );
	}

	/// <summary>
	/// The <c>RegionFX</c> row a placed object spreads where it stands (tp.exe constructor <c>0x004DB090</c>): a toilet its clean row 1 or, below the
	/// dirty gauge, row 6; a security camera row 4; a fireworks object row 7 until its show has run. Null for every other object.
	/// </summary>
	internal int? ObjectEffectRow( ParkObjectState item )
	{
		if ( !Catalog.TryGet( item.InfoId, out var info ) )
			return null;
		if ( info.ProvidesRelief )
			return IsDirtyToilet( item ) ? RegionEffect.DirtyToilet : RegionEffect.CleanToilet;
		if ( info.ProvidesSecurity )
			return RegionEffect.Camera;
		return info.IsFireworks && !item.FireworksSpent ? RegionEffect.Fireworks : null;
	}

	/// <summary>
	/// The level placed (or, after a load, linked again) the object on <paramref name="cell"/>: its <c>RegionFX</c> row goes on that cell, once.
	/// </summary>
	// [BIN:TPW-EXE:0x004DB3EE object constructor] the relief flag adds row 1, ProvidesSecurity row 4 (0x004DB458) and IsFireworks row 7 (0x004DB48C) at the thing's cell through 0x004D8440
	// a load of OpenTPW's own save puts back the row the object's saved state implies (a dirty toilet its row 6, a spent show none), which is the row the live grid held; an original save's grid is imported whole (ImportSavedEnvironment), so no constructor row is assumed there
	public void PlaceObjectEffects( int instanceId, (int X, int Y) cell )
	{
		if ( !objects.TryGetValue( instanceId, out var item ) || item.EffectCell != null || ObjectEffectRow( item ) is not { } row )
			return;
		Litter.Environment.Apply( row, cell.X, cell.Y, add: true );
		item.EffectCell = cell;
	}

	/// <summary>The object leaves the park (tp.exe destructor <c>0x004DD0A0</c>): its row comes off its cell.</summary>
	// [BIN:TPW-EXE:0x004DD0A0 object destructor] row 4 for a camera, row 7 for fireworks while their script variable 0 is below 2, and for a toilet row 1 at a gauge of 25 or more, else row 6 (0x004D8460)
	private void RemoveObjectEffects( ParkObjectState item )
	{
		if ( item.EffectCell is not { } cell || ObjectEffectRow( item ) is not { } row )
			return;
		Litter.Environment.Apply( row, cell.X, cell.Y, add: false );
		item.EffectCell = null;
	}

	/// <summary>Whether the object is a fireworks show (the only objects whose script variable 0 is read for row 7).</summary>
	public bool IsFireworks( int instanceId ) => objects.TryGetValue( instanceId, out var item ) && Catalog.TryGet( item.InfoId, out var info ) && info.IsFireworks;

	/// <summary>A fireworks object's show has run (its script's variable 0 is 1): its row 7 comes off for good (tp.exe <c>0x004E0B90</c>). True when it changed.</summary>
	// [BIN:TPW-EXE:0x004E0B90 fireworks] each object tick: with thing +0x32 bit 0x80 and script variable 0 (0x0055A070, 0x0055A390) equal to 1, row 7 comes off the thing's cell and the variable is set to 2 (0x0055A0B0)
	public bool FireworksFired( int instanceId )
	{
		if ( !objects.TryGetValue( instanceId, out var item ) || item.FireworksSpent || !Catalog.TryGet( item.InfoId, out var info ) || !info.IsFireworks )
			return false;
		RemoveObjectEffects( item );
		item.FireworksSpent = true;
		return true;
	}

	/// <summary>
	/// A fireworks object whose script variable 0 already is 2 (a park saved after its show ran, and its row 7 came off): the show is spent, so the destructor takes no row off again
	/// and a sale leaves the cells as they are (tp.exe <c>0x004DD0A0</c> subtracts row 7 only while the variable is below 2). No cell changes: the saved grid, or the live one, no longer holds the row.
	/// </summary>
	// [BIN:TPW-EXE:0x004DD0A0 object destructor] row 7 for fireworks only while script variable 0 is below 2; the every-tick test of 0x004E0B90 (variable 0 equal to 1 removes the row and writes 2) therefore fires once for a show whose script writes the variable once
	// [DATA:levels/hallow/features/firework/Firework.RSE, levels/space/features/slaser/slaser.RSE] both shows run COPY VAR_STATUS 1 once after the show, then ENDSLICE for good; Info.IsChoosable is 0, so no guest uses a show and the admission writer 0x004E0900 (which writes variable 0 for the admitted guest) never meets one: the variable runs 0, 1, 2 once
	internal void MarkFireworksRemoved( int instanceId )
	{
		if ( objects.TryGetValue( instanceId, out var item ) && !item.FireworksSpent && Catalog.TryGet( item.InfoId, out var info ) && info.IsFireworks )
			item.FireworksSpent = true;
	}

	/// <summary>A toilet's row swaps when it turns dirty or is cleaned (tp.exe <c>0x004E2440</c>, <c>0x004DFD80</c>).</summary>
	private void SwapToiletEffect( ParkObjectState item, int from, int to )
	{
		if ( item.EffectCell is not { } cell )
			return;
		Litter.Environment.Apply( from, cell.X, cell.Y, add: false );
		Litter.Environment.Apply( to, cell.X, cell.Y, add: true );
	}

	/// <summary>A toilet's gauge byte is below <see cref="DirtyToiletGauge"/> (tp.exe <c>0x004E0390</c>).</summary>
	internal static bool IsDirtyToilet( ParkObjectState item ) => (QueueJoinRules.Truncate( (float)item.Repair ) & 0xFF) < DirtyToiletGauge;

	/// <summary>The relief objects (toilets) in the original object list's order: newest first (ids descending, 0x00519D80 pushes each new object at the head).</summary>
	internal IEnumerable<ParkObjectState> Toilets() =>
		objects.Values.Where( item => Catalog.TryGet( item.InfoId, out var info ) && info.ProvidesRelief ).OrderByDescending( item => item.Id );

	/// <summary>The end of a handyman's clean (tp.exe <c>0x004DFD80</c>): the gauge is back at 100, the claim and the request clear.</summary>
	internal void FinishToiletClean( StaffMember member )
	{
		if ( objects.TryGetValue( member.ToiletId, out var item ) && item.MechanicId == member.Id )
		{
			// [BIN:TPW-EXE:0x004DFDC6 toilet cleaned] a gauge byte below 25 swaps the dirty row 6 back to the clean row 1 before the gauge goes to 100
			if ( IsDirtyToilet( item ) )
				SwapToiletEffect( item, RegionEffect.DirtyToilet, RegionEffect.CleanToilet );
			// [BIN:TPW-EXE:0x004DFE00 clean resets VAR_WORN] the clean always writes script variable 8 (VAR_WORN) = 0 (0x55A070 handle, 0x55A0B0 variable, value), dirty or not, so the toilet's script drops its dirty look; then the gauge, the claim and the call
			WriteScript( item, RideVariables.VAR_WORN, 0 );
			item.Repair = RideRepair.FullGauge;
			item.MechanicId = 0;
			var called = item.MaintenanceRequested;
			item.MaintenanceRequested = false;
			Raise( ParkEventKind.ToiletCleaned, called ? 1 : 0, item.Id, item.InfoId );
		}
		member.ToiletId = 0;
	}

	/// <summary>Whether any placed object is a rest area for staff (<c>UsageInfo.ChillsYouOut</c>, in the shipped data the Staff Room).</summary>
	internal bool HasRestArea => objects.Values.Any( item => Catalog.TryGet( item.InfoId, out var info ) && info.ChillsYouOut );

	/// <summary>
	/// What a free member does next (0x100F4900): a member of a striking role walks out, otherwise a tired one goes to rest when a rest area
	/// exists. True when its state changed. Runs every turn for free members and once when a job ends, before the next job is dispatched
	/// (the original's working member finishes its job first, then reaches this decision).
	/// </summary>
	// [BIN:STP-PPC:0x100F4900 strike or rest] a free member of a striking role walks to the strike area (raw state 4); the original also needs the world state 0x1091B8 to be 1, which is not modelled
	// [BIN:STP-PPC:0x100F4AD4 no rest area draw] the happiness hit without a rest area takes one world draw (the shared generator 0x105328, PC 0x00516330); OpenTPW takes it from the park's one world generator
	private bool StrikeOrRest( StaffMember member, bool restArea )
	{
		if ( Staff.Strikes.IsActive( member.Type ) )
		{
			member.State = StaffState.OnStrike;
			return true;
		}
		if ( !StaffVitals.SeekRest( member, Settings.RestLevel, Settings.HappyHitCosNoRestArea, restArea, () => World.NextWord() ) )
			return false;
		member.State = StaffState.Resting;
		return true;
	}

	/// <summary>The staff manager's month-end consideration of each role's strike (ECON-014): <see cref="StaffStrikeBoard.ConsiderMonth"/> with the park's state.</summary>
	// [BIN:TPW-EXE:0x004F8260 month message] the calendar update sends the month message (kind 12, 0x0040F500) to the staff manager (0x00508A30 → 0x00508E70) with no game-type test, so strikes are considered in every mode; the stamp (0x004F8800) is floor(turn × rate / 4) × 10^7 with the calendar's base added and taken off again, the park clock from world turn 0
	// [BIN:TPW-EXE:0x00509360 handyman grievance] the handymen's litter grievance: the analyser's littered cells (0x004C8180) over its path, queue and type-9 cells (0x004C8150), as singles, above 0.2 (ParkLitter.Statistic)
	private void ConsiderStrikes()
	{
		var stamp = ParkCalendar.Seconds( Tick ) * StaffStrikeBoard.StampUnitsPerSecond;
		var messages = Staff.Strikes.ConsiderMonth( Turn, forced: false, IsParkOpen, CachedGuestCensus( 5 ), stamp,
			role => Staff.OfType( role ).Select( member => new StaffVital( member.Energy, member.Happiness ) ).ToList(), Litter.StatisticAt( Turn ) );
		foreach ( var message in messages )
			Raise( ParkEventKind.StaffStrikeMessage, (int)message.Kind, 0, (int)message.Role, $"TAG_SYSTEM {message.TagSystemIndex}" );
	}

	/// <summary>Whether the world update of a park turn runs the golden-ticket checks: every 100th turn (the turn is unsigned) in Full Simulation only.</summary>
	// [BIN:STP-PPC:0x100D67F0 world update] turn % 100 == 0 (unsigned) calls the dispatcher 0x100D2F1C, which runs the checks only while the game type word is 0 (oracle F31: 507 scripts agree)
	public static bool RunsGoldenTicketCheck( long turn, ParkGameMode mode ) =>
		turn % GoldenTicketCheckInterval == 0 && ParkModeFeatures.For( mode ).GoldenTickets;

	private void DoResearch()
	{
		foreach ( var member in Staff.OfType( StaffType.Researcher ).ToList() )
			ResearchBy( member );
	}

	/// <summary>One researcher's research step of a turn that is a multiple of 20: the points of its grade's ability go to the research items (nothing for a researcher in an excluded state).</summary>
	// [BIN:TPW-EXE:0x00502960 researcher update] the researcher's thing update does its research first, when its state (0x005064F0) is not 3, 4 or 5 and the turn counter is a multiple of 20 (0x14): the points of its grade (0x0078545C + grade * 12) go to the research (0x00519550, 0x00503430), and then the staff pre-update 0x00505490 and the state handler follow
	internal void ResearchBy( StaffMember member )
	{
		if ( !ParkResearch.CanResearch( member ) )
			return;
		var ability = Settings.ResearchAbility[member.Grade];
		// Research comes from researchers only (Mac 0xf0728 has no staffless path); an imported seed park brings its own.
		ApplyResearch( Research.AddResearcherPoints( ability ) );
	}

	/// <summary>What the park does with the items one researcher's points completed: the counters, the completion events, the new groups and the all-done event.</summary>
	internal void ApplyResearch( IReadOnlyList<ResearchItem> researched )
	{
		foreach ( var item in researched )
		{
			Counters.Add( ParkCounters.Researched( item.Category ), 1 );
			// the instance field is 1 for an item finished by research points, 0 for a free item that followed one: only the first is the completion message 0x11 the advisor answers
			Raise( ParkEventKind.ItemResearched, item.Level, Research.LastResearched.Contains( item ) ? 1 : 0, item.InfoId, Catalog.TryGet( item.InfoId, out var info ) ? info.Name : "" );
		}
		// [BIN:TPW-EXE:0x00503D40 research completion] the lab's handler of the completion message opens the groups (0x503B80), which posts the new-group advice when one rose and the category still has unresearched items, then checks whether anything is left (0x503E3F)
		foreach ( var category in Research.LastGroupsOpened )
			if ( Research.HasUnresearched( category ) )
				Raise( ParkEventKind.ResearchGroupOpened, (int)category, 0, 0, category.ToString() );
		if ( Research.LastResearched.Count > 0 && Research.AllResearched )
			Raise( ParkEventKind.ResearchAllDone, 0, 0, 0, "" );
	}

	private void CheckGoldenTickets()
	{
		foreach ( var ticket in Objectives.CheckGoldenTickets( Counters, guestStatistics, Research, Ledger.ProfitThisYear, monthlyAdmissions, DroppedAdmissions, ParkCalendar.Seconds( Tick ), CoveredCells(), SecurityCoveragePercent() ) )
			Raise( ParkEventKind.GoldenTicketWon, (int)ticket, 0, 0, ticket.ToString() );
		// [BIN:TPW-EXE:0x004D5270 global ticket moved] result 4 of 0x5AF940 logs "Global ticket award moved here" (0x75A71C, a debug log) and queues the advice 0xEA + type (234 + type: coaster height, go-kart excitement, water length, big park) through 0x59B590
		foreach ( var ticket in Objectives.LastMovedTickets )
			Raise( ParkEventKind.GlobalTicketMoved, (int)ticket, 0, 0, ticket.ToString() );
	}

	/// <summary>Advances to the start of the calendar day <paramref name="days"/> days after the current one.</summary>
	public void AdvanceDays( int days ) => Advance( Math.Max( 0, ParkCalendar.TickAtDay( ParkCalendar.DayIndex( Tick ) + days ) - Tick ) );

	/// <summary>Advances to the start of the calendar month <paramref name="months"/> months after the current one.</summary>
	public void AdvanceMonths( int months ) => Advance( Math.Max( 0, ParkCalendar.TickAtMonth( ParkCalendar.MonthIndex( Tick ) + months ) - Tick ) );

	private void UpdateHour()
	{
		// [BIN:TPW-EXE:0x005084F0 staff pool update] the staff manager's update runs from the world turn's tail (0x004D7B20), in every world state
		if ( !Rules.Staff )
			return;
		Staff.UpdatePool( World, Tick );
		// the dispatch belongs to the mechanics' and rides' thing updates, which a lost park no longer runs
		if ( !IsBankrupt )
			DispatchMechanics();
	}

	/// <summary>
	/// The original object update (tp.exe <c>0x004E0B90</c>, docs/reverse/RIDE-WEAR.md, ORACLE.md F27) of every object guests may choose: every 8 turns one that
	/// is open and not broken down runs the wear step, which acts on every 64th turn, and then the breakdown check; a relief object reports itself worn
	/// below a state of repair of 25 every turn.
	/// </summary>
	private void UpdateRides( long turn )
	{
		foreach ( var item in objects.Values )
			UpdateRide( item, turn );
	}

	/// <summary>One object's update of a park turn (the body of <see cref="UpdateRides"/>): the wear step, the breakdown check and the relief objects' worn report.</summary>
	internal void UpdateRide( ParkObjectState item, long turn )
	{
		// [BIN:TPW-EXE:0x004DB4FB object state] a thing whose type record is choosable (Info.IsChoosable, +0x32 bit 4) starts in state 0, any other in state 3, which the update skips
		if ( !Catalog.TryGet( item.InfoId, out var info ) || !info.IsChoosable )
			return;
		var state = ObjectState( item );
		// [BIN:STP-PPC:0x100E077C ride update] a ride in state 1 or 4 (broken down, condemned), a closed ride (open word +0x64 = 0, which state 2 clears too) and a ride on a turn that is not a multiple of 8 are not updated; a mechanic at work on a ride that is open and not broken (a repair of a worn ride) does not stop the update
		if ( RideBreakdownRules.SkipsUpdate( state ) )
			return;
		if ( RideBreakdownRules.RunsWear( state, item.IsOpen && state != RideObjectStates.Upgrading, turn ) )
		{
			if ( turn % WearInterval == 0 )
				Wear( item );
			// [BIN:STP-PPC:0x100E077C breakdown check] a ride that is not already broken down breaks down when its truncated life gauge or state of repair is 0; when only the state of repair is 0 the life gauge first loses 5 (clamped to 0..100); 0x100E09F4 then sets VAR_BREAKSTAT; there is no random roll
			// [BIN:TPW-EXE:0x004E0B90 breakdown check] an object without the relief flag (+0x32 bit 1) whose life (+0x48) or repair (+0x44) byte is 0 and whose VAR_BREAKSTAT (script variable 4) is 0 writes VAR_BREAKSTAT = 1 ("Object %d requested breakdown"); its state stays 0 until the ride update reads the script's VAR_BROKEN (0x004E14E0)
			if ( RideBreakdownRules.BreaksDown( info.ProvidesRelief, item.ScriptHosted ? item.ScriptBreakStat : 0, (float)item.LifeGauge, (float)item.Repair ) )
			{
				item.LifeGauge = RideBreakdownRules.LifeAfterBreakdown( (float)item.LifeGauge );
				// [BIN:TPW-EXE:0x4e0e00 ride state dispatch] [EXT:headless-economy-breakdown] (user-approved) an object a script runs breaks down as tp.exe's does (0x004E0B90 writes VAR_BREAKSTAT = 1, the script reports VAR_BROKEN, the ride update's state 1 from 0x004E14E0 writes the shared state word and calls a mechanic, whose repair writes VAR_BREAKSTAT = 0 and waits for VAR_BROKEN to clear, 0x004DA830, and 0x004DF8F0 clears VAR_WORN); the headless test and tool economy without ride scripts breaks an object down at once (the same word) and repairs it without the wait, where a level runs the scripts; the visitor bridge reads the same word whole (RideStateWord.Current: the stored 2 and 3 as well as the ride update's 1 and 4), so an upgrading ride runs the head step and the script dismiss only, as the dispatch does
				if ( item.ScriptHosted )
					WriteScript( item, RideVariables.VAR_BREAKSTAT, 1 );
				else
				{
					item.IsBrokenDown = true;
					PublishRideState( item );
				}
				Raise( ParkEventKind.RideBrokeDown, 0, item.Id, item.InfoId );
			}
		}
		// [BIN:TPW-EXE:0x004E0D16 relief worn] a relief object (+0x32 bit 1) whose repair byte is below 25 writes VAR_WORN = 1 (script variable 8) on every update
		// (the write is skipped while the script already holds 1: the same value, no event per turn)
		if ( info.ProvidesRelief && RideBreakdownRules.GaugeByte( (float)item.Repair ) < ReliefWornByte && item.ScriptWorn != 1 )
			WriteScript( item, RideVariables.VAR_WORN, 1 );
	}

	/// <summary>A relief object reports itself worn (VAR_WORN) below this state of repair byte (tp.exe <c>0x004E0D16</c>: 25.0).</summary>
	public const int ReliefWornByte = 25;
	/// <summary>The wear step reports a ride worn (VAR_WORN) below this state of repair byte (tp.exe <c>0x004DF670</c>: 20.0).</summary>
	public const int WornByte = 20;

	/// <summary>Writes one of the object's script variables (the host applies <see cref="ParkEventKind.RideScriptVariable"/>); nothing for an object no script runs.</summary>
	private void WriteScript( ParkObjectState item, RideVariables variable, int value )
	{
		if ( !item.ScriptHosted )
			return;
		if ( variable == RideVariables.VAR_BREAKSTAT )
			item.ScriptBreakStat = value;
		else if ( variable == RideVariables.VAR_WORN )
			item.ScriptWorn = value;
		Raise( ParkEventKind.RideScriptVariable, value, item.Id, (int)variable );
	}

	/// <summary>
	/// Publishes the ride's entry into state 1 or 4 (the ride update's <c>0x004E14E0</c> wrote the shared state word, <see cref="RideStateWord"/>): the object is broken down and a mechanic is
	/// sent (<c>0x004DAA90</c> looks for state 1). The level calls it after the bridge's update, so the event comes in the economy's own pass as it did when the level joined the two records.
	/// </summary>
	internal void PublishRideState( ParkObjectState item )
	{
		if ( !item.BrokenStatePending )
			return;
		item.BrokenStatePending = false;
		if ( item.StateWord.IsBroken )
			Raise( ParkEventKind.RideEnteredBrokenState, 0, item.Id, item.InfoId );
	}

	/// <summary>Notes a change of the ride's state word for <see cref="PublishRideState"/>: only the entry into state 1 or 4.</summary>
	private static void HookStateWord( ParkObjectState item )
	{
		item.StateWord.Changed += ( before, after ) =>
		{
			if ( after is RideObjectStates.BrokenDown or RideObjectStates.Condemned && before is not (RideObjectStates.BrokenDown or RideObjectStates.Condemned) )
				item.BrokenStatePending = true;
		};
	}

	/// <summary>
	/// The ride object's state word (<see cref="RideObjectStates"/>): the stored 3 (not an attraction) or 2 (an upgrade was requested, which overwrites a breakdown), else the shared word's 1 or 4, else idle.
	/// </summary>
	// [BIN:TPW-EXE:0x004E0E60 SetState] one word +0x19C, stored last by every case (each of the cases 1, 2 and 4 ends in a jump to 0x4E11B0, whose store of the argument into +0x19C is the function's tail): 1 and 4 are written by the ride update (0x4E14E0) and left through SetState(0)
	// [BIN:TPW-EXE:0x004E0086 upgrade request] the request 0x4E0050(1) calls SetState(2) after the choosable test (+0x32 bit 4) and nothing tests the current state, so a ride in state 1 or 4 goes to state 2; the completion 0x4DF8F0 and the cancel end in SetState(0) (0x4DFCB8, 0x4E01B6)
	internal static int ObjectState( ParkObjectState item )
	{
		return item.StateWord.Current;
	}

	/// <summary>The stored state a record starts with: 3 for a type that is not choosable (0x4DB4FB), 2 while an upgrade is waiting, else 0.</summary>
	private int InitialStoredState( ParkObjectState item ) =>
		!Catalog.TryGet( item.InfoId, out var info ) || !info.IsChoosable ? RideObjectStates.NotAnAttraction
			: item.PendingLevel > item.Level ? RideObjectStates.Upgrading : RideObjectStates.Idle;

	/// <summary>One original wear step (0x100DEC2C) of a ride whose script reports it running.</summary>
	private void Wear( ParkObjectState item )
	{
		if ( !Catalog.TryGet( item.InfoId, out var info ) || info.Upgrades.Count == 0 )
			return;
		var level = info.Upgrades[Math.Min( item.Level, info.Upgrades.Count - 1 )];
		// [BIN:STP-PPC:0x100DEC2C wear step] nothing happens when Upgrades[level].WearRate is 0, in online mode (OpenTPW has none) or when the ride script does not report VAR_RUNNING; with VAR_ONRIDE 0 the wear is 0
		if ( level.WearRate == 0 || !RideOperations.TryGet( item.Id, out var operation ) || !operation.Running )
			return;
		var wear = operation.Riders == 0 ? 0 : WearAmount( info, level, operation );
		var before = item.StateOfRepair;
		// [BIN:STP-PPC:0x100DEC2C wear step] state of repair (+0x40) loses the wear and the life gauge (+0x44) 0.02 × the wear, each clamped to 0..100
		item.Repair = Math.Clamp( item.Repair - wear, 0, 100 );
		item.LifeGauge = Math.Clamp( item.LifeGauge - 0.02f * wear, 0, 100 );
		// [BIN:TPW-EXE:0x004DF670 wear step] after the gauges, a repair byte below 20 writes VAR_WORN = 1 (script variable 8)
		if ( RideBreakdownRules.GaugeByte( (float)item.Repair ) < WornByte )
			WriteScript( item, RideVariables.VAR_WORN, 1 );
		if ( before >= WornStateOfRepair && item.StateOfRepair < WornStateOfRepair )
			Raise( ParkEventKind.RideWorn, item.StateOfRepair, item.Id, item.InfoId );
		// [BIN:TPW-EXE:0x004DF670 breakdown imminent] the condition byte below 10.0 with the step's wear added back not below it posts the advice 0x42 (the advisor answers this event)
		if ( AdvisorRideEvents.CrossesImminentLine( (float)item.Repair, wear ) )
			Raise( ParkEventKind.RideBreakdownImminent, item.StateOfRepair, item.Id, item.InfoId );
	}

	/// <summary>
	/// Wear of one step (0x100DE904): a speed term and a rider term, each 0.9 × value / maximum plus 0.1, or halfway to
	/// 1.1 at or above the red line, times 0.5 × <c>WearRate</c>. Single precision, as in the original.
	/// </summary>
	// [BIN:STP-PPC:0x100DE904 wear amount] riders = VAR_ONRIDE clamped to 0..UsageInfo.MaxCapacity (call flag r7 = 0 at 0x100DED70); s = 0.9 speed / MaxSpeed, then (s + 1.1) × 0.5 when RedLineSpeed <= speed, else s + 0.1; c = 0.9 riders / MaxCapacity, then (c + 1.1) × 0.5 when RedLineCapacity <= riders / 10, else c + 0.1; wear = (s + c) × 0.5 × WearRate
	public static float WearAmount( EconomyObjectInfo info, UpgradeLevelInfo level, RideOperation operation )
	{
		var riders = Math.Clamp( operation.Riders, 0, Math.Max( 0, info.MaxCapacity ) );
		// A zero maximum would divide by zero in the original; no original ride has one.
		var speedTerm = info.MaxSpeed > 0 ? 0.9f * (operation.Speed / (float)info.MaxSpeed) : 0f;
		speedTerm = level.RedLineSpeed <= operation.Speed ? (speedTerm + 1.1f) * 0.5f : speedTerm + 0.1f;
		var riderTerm = info.MaxCapacity > 0 ? 0.9f * (riders / (float)info.MaxCapacity) : 0f;
		riderTerm = level.RedLineCapacity <= riders / 10f ? (riderTerm + 1.1f) * 0.5f : riderTerm + 0.1f;
		return (speedTerm + riderTerm) * 0.5f * level.WearRate;
	}

	/// <returns>False when a mechanic's broken ride still plays its fix animation (the job goes on), else true.</returns>
	private bool FinishJob( StaffMember member )
	{
		// [BIN:TPW-EXE:0x004DA830 repairing] at the end of the timer a ride in state 1 (0x004E0370) gets VAR_BREAKSTAT = 0 (0x004E03F0) and the repair waits while the script's VAR_BROKEN (variable 7) is above 0 (0x004E0410, "Ride has finished fix anim" once it is not)
		if ( member.Type == StaffType.Mechanic && member.LitterCell == null && objects.TryGetValue( member.AssignedInstanceId, out var fixing )
			&& fixing.ScriptHosted && ObjectState( fixing ) == RideObjectStates.BrokenDown && fixing.IsBrokenDown )
		{
			WriteScript( fixing, RideVariables.VAR_BREAKSTAT, 0 );
			if ( fixing.ScriptBroken )
				return false;
		}
		member.AwaitingJobArrival = false;
		member.State = StaffState.Patrolling;
		if ( member.LitterCell is { } litter )
		{
			// [BIN:TPW-EXE:0x004D76A0 sweep] when the turn passes the arrival stamp + WorkDuration[grade], 0x004D9620 removes the litter of the handyman's cell
			member.LitterCell = null;
			if ( Litter.CollectorAt( litter.X, litter.Y ) == member.Id )
				Litter.Remove( litter.X, litter.Y );
			return true;
		}
		if ( !objects.TryGetValue( member.AssignedInstanceId, out var item ) )
		{
			member.AssignedInstanceId = 0;
			return true;
		}
		member.AssignedInstanceId = 0;
		item.MechanicId = 0;
		if ( member.Type == StaffType.Handyman )
		{
			// [BIN:TPW-EXE:0x004DFD80 toilet cleaned] the end of the handyman's state 11 sets the gauge back to 100.0 and clears the ride's mRequestedService (+0x64) and assigned staff (+0x5E); a dirty toilet's row 6 turns back into row 1 first
			if ( IsDirtyToilet( item ) )
				SwapToiletEffect( item, RegionEffect.DirtyToilet, RegionEffect.CleanToilet );
			// [BIN:TPW-EXE:0x004DFE00 clean resets VAR_WORN] the same write as in FinishToiletClean: script variable 8 = 0 whatever the gauge was
			WriteScript( item, RideVariables.VAR_WORN, 0 );
			item.Repair = RideRepair.FullGauge;
			var called = item.MaintenanceRequested;
			item.MaintenanceRequested = false;
			Raise( ParkEventKind.ToiletCleaned, called ? 1 : 0, item.Id, item.InfoId );
			return true;
		}
		// [BIN:TPW-EXE:0x004DF8F0 repaired fully] the repair or the upgrade clears the call (+0x64 = 0) and the claim (+0x5E) and writes VAR_WORN = 0; a repair (not in state 2) also writes VAR_BREAKSTAT = 0
		item.MaintenanceRequested = false;
		WriteScript( item, RideVariables.VAR_WORN, 0 );
		// 0xdef2c installs the upgrade only for a ride in state 2; the request stores 2 over a breakdown (0x4E0086), so a purchased upgrade is installed even on a broken ride
		if ( ObjectState( item ) == RideObjectStates.Upgrading )
		{
			// [BIN:TPW-EXE:0x004DF8F0 repaired fully] the routine's common entry, before its state-2 upgrade branch, sets the state of repair (+0x44) to 100.0 for an upgrade too
			item.Repair = RideRepair.FullGauge;
			item.Level = item.PendingLevel;
			item.PendingLevel = 0;
			// [BIN:TPW-EXE:0x004DFCB8 upgrade installed] the routine ends in SetState(0) on this path too
			item.StoredState = RideObjectStates.Idle;
			item.IsBrokenDown = false;
			// [BIN:TPW-EXE:0x004DFA96 upgrade installed] the completion 0x004DF8F0 raises the level byte +0x50 by one and debits Upgrades[new level].CostOfUpgrade (type record +0x1B8 + 0x40 · level, 0x004DFA81) through 0x004D01F0, without a balance test
			if ( item.UpgradeCostDue && Catalog.TryGet( item.InfoId, out var upgraded ) && item.Level < upgraded.Upgrades.Count )
			{
				var cost = upgraded.Upgrades[item.Level].CostOfUpgrade;
				Post( LedgerCategory.OtherCosts, cost );
				item.TotalSpent += cost;
			}
			item.UpgradeCostDue = false;
			Raise( ParkEventKind.UpgradeCompleted, item.Level, item.Id, item.InfoId );
		}
		else
		{
			// [BIN:STP-PPC:0x100DEF2C repair] a repair sets the state of repair to 100.0 (the float at TOC -11016, ORACLE F27) and clears the worn flag; it does not restore the life gauge. It also opens the ride (the host reopens the ride on RideRepaired)
			item.Repair = RideRepair.FullGauge;
			item.IsBrokenDown = false;
			WriteScript( item, RideVariables.VAR_BREAKSTAT, 0 );
			Raise( ParkEventKind.RideRepaired, 0, item.Id, item.InfoId );
		}
		return true;
	}

	/// <summary>
	/// The mechanic's job timer (arrival routine <c>0xd9c0c</c>, ORACLE.md F19), in park turns. An upgrade takes
	/// <c>WorkDuration × Upgrades[level + 1].DurationOfUpgrade</c> (<paramref name="upgradeDuration"/>; 32-bit product, no minimum), any
	/// other job <c>WorkDuration × (100 − state of repair) / 100</c>, with the state of repair the low byte of the truncated gauge
	/// and a signed 32-bit division by 100 that truncates toward zero. A ride at 100 % repair needs no time.
	/// </summary>
	// [BIN:STP-PPC:0x100D9C0C mechanic arrival] upgrade (ride state 2): WorkDuration[grade] (+1004 + 8 × grade) × the type record word at +508 + 64 × level (Upgrades[level + 1].DurationOfUpgrade); otherwise WorkDuration × (100 − (fctiwz(+64) & 0xFF)) / 100 through mullw and the 1/100 multiply-high; stored at mechanic +528 as an unsigned word
	public static long RepairTimer( int workDuration, double stateOfRepair, int? upgradeDuration )
	{
		unchecked
		{
			if ( upgradeDuration is { } duration )
				return (uint)(workDuration * duration);
			// The gauge is a single-precision float (ride +64): round to float before truncating, as lfs then fctiwz does.
			return (uint)(workDuration * (100 - (QueueJoinRules.Truncate( (float)stateOfRepair ) & 0xFF)) / 100);
		}
	}

	/// <summary>
	/// Assigns free mechanics: broken rides first, then paid upgrades, then worn rides. A job takes
	/// <see cref="RepairTimer"/> park turns (one more for the update that completes it); the walk to the ride is
	/// supplied by an optional host world (otherwise immediate).
	/// </summary>
	private void DispatchMechanics()
	{
		var jobs = objects.Values.Where( item => JobRank( item ) >= 0 && HeadlessClaimant( item ) == 0 )
			.OrderBy( JobRank ).ThenBy( item => item.StateOfRepair ).ThenBy( item => item.Id ).ToList();
		if ( jobs.Count == 0 )
			return;
		var role = Settings[StaffType.Mechanic];
		foreach ( var mechanic in Staff.OfType( StaffType.Mechanic ).Where( member => !DrivenByWorld( member ) && member.IsAvailable && (MechanicJobWorld?.CanStartJobs( member ) ?? true) && !Staff.Strikes.IsActive( member.Type ) && !(HasRestArea && StaffVitals.Byte( member.Energy ) <= Settings.RestLevel) ).OrderBy( member => member.Id ) )
		{
			if ( jobs.Count == 0 )
				break;
			var job = jobs[0];
			jobs.RemoveAt( 0 );
			// [EXT:headless-economy] without a staff world (the gate harnesses and tests; a level with guests always attaches one, Level.SetupStaff) a mechanic starts his job at once and the jobs keep OpenTPW's order (broken, upgrading, then rides worn below 25); the original always walks its mechanics to the nearest ride that is broken down (state 1), upgrading (state 2) or called (+0x64) (0x004DAA90) and never sends one to a worn ride on its own; in a level the mechanics run its state machine (ECON-022)
			int? upgradeDuration = ObjectState( job ) == RideObjectStates.Upgrading && Catalog.TryGet( job.InfoId, out var info ) && job.PendingLevel < info.Upgrades.Count
				? info.Upgrades[job.PendingLevel].DurationOfUpgrade : null;
			// The repair handler (0xd9e04) decrements a nonzero timer and completes on the update that finds it zero: timer + 1 updates.
			mechanic.BusyTicks = RepairTimer( role.WorkDuration[mechanic.Grade], job.Repair, upgradeDuration ) + 1;
			mechanic.AwaitingJobArrival = MechanicJobWorld != null;
			mechanic.AssignedInstanceId = job.Id;
			mechanic.State = StaffState.Working;
			job.MechanicId = mechanic.Id;
		}
	}

	internal bool OwnsMechanicJob( StaffMember member ) => member.Type == StaffType.Mechanic && member.BusyTicks > 0 && Enum.IsDefined( member.State )
		&& objects.TryGetValue( member.AssignedInstanceId, out var item ) && item.Kind == ParkObjectKind.Ride
		&& Catalog.TryGet( item.InfoId, out var info ) && info.Kind == ParkObjectKind.Ride
		&& item.MechanicId == member.Id && JobRank( item ) >= 0;

	/// <summary>
	/// The mechanic's job for an object, as a dispatch rank (lower first), or -1 for none: an object the economy has broken down, then a purchased
	/// upgrade, then a called or worn ride (the worn threshold is OpenTPW's own, Advisor.sam). A condemned ride (life gauge 0) is a job only while it is called.
	/// </summary>
	// [BIN:STP-PPC:0x100D9C0C mechanic arrival] the timer starts only for a ride in state 1 (broken down) or 2 (upgrading) or with the maintenance word +96; any other ride logs and releases the mechanic (ORACLE F19)
	// [BIN:TPW-EXE:0x004DA7D5 mechanic arrival] the PC twin 0x004DA740 tests state 1 (0x004E0370), state 2 (0x004E03E0) and the call +0x64 (0x004E03D0) in turn and starts the work (state 13) on any of them, so a called ride in state 4 (condemned) is repaired; the finder 0x004DAA90 selects it too (the call of an object without the relief flag +0x32 bit 1, whatever its state)
	private static int JobRank( ParkObjectState item ) => ObjectState( item ) switch
	{
		RideObjectStates.BrokenDown when item.IsBrokenDown => 0,
		RideObjectStates.Upgrading => 1,
		RideObjectStates.Idle when item.Kind == ParkObjectKind.Ride && (item.StateOfRepair < WornStateOfRepair || item.MaintenanceRequested) => 2,
		RideObjectStates.Condemned when item.Kind == ParkObjectKind.Ride && item.MaintenanceRequested => 2,
		_ => -1,
	};

	/// <summary>An online visit, the original's game type 1: a use wears no toilet and cleans a dirty one at once (<see cref="UseRelief"/>).</summary>
	public bool OnlineVisit { get; set; }

	/// <summary>The gauge below which a toilet is dirty and handymen clean it (the single 25.0 at 0x00700550).</summary>
	public const int DirtyToiletGauge = 25;

	/// <summary>
	/// A guest has used a relief object (tp.exe <c>0x004E2440</c>, from the use routine <c>0x004FE1E0</c>): its gauge falls by 0.05 × the guest's
	/// toilet meter taken as a byte, clamped to 0..100 and stored as a single. Returns true when the toilet has just become dirty (below 25).
	/// </summary>
	// [BIN:TPW-EXE:0x004E2440 toilet wear] (int64)toilet × −0.05f + gauge in x87 precision, compared with 100 and 0, stored as a single; a gauge byte falling below 25 logs "Toilet has become dirty and smelly" and posts the dirty-toilet messages
	public bool UseRelief( int instanceId, int toiletByte )
	{
		if ( !objects.TryGetValue( instanceId, out var item ) || !Catalog.TryGet( item.InfoId, out var info ) || !info.ProvidesRelief )
			return false;
		// [EXT:tpi-no-maintenance-wear] without staff nobody cleans a toilet (the handymen are refused), so a use wears none: the gauge stays full and the toilet stays usable (see also the ride wear in UpdateThings and the litter in ParkEconomyRuntime.AttachLitter)
		if ( !Rules.Staff )
			return false;
		if ( OnlineVisit )
		{
			// [BIN:TPW-EXE:0x004E2478 toilet wear, game type 1] the branch for the online game type (SetGameType 1 at the online entries 0x4C0830 and 0x5E2700; the word 0xFB3B7C is the game-type object) wears nothing; a relief object whose gauge byte is below 25 is cleaned at once: row 6 off and row 1 on at its cell (0x4D8460, 0x4D8440), script variable 8 = 0 (0x55A0B0), gauge 100.0, the claim (+0x5E) and the call (+0x64) cleared (0x4E251F..0x4E252A)
			if ( IsDirtyToilet( item ) )
			{
				SwapToiletEffect( item, RegionEffect.DirtyToilet, RegionEffect.CleanToilet );
				WriteScript( item, RideVariables.VAR_WORN, 0 );
				item.Repair = RideRepair.FullGauge;
				item.MechanicId = 0;
				item.MaintenanceRequested = false;
			}
			return false;
		}
		var wasDirty = (QueueJoinRules.Truncate( (float)item.Repair ) & 0xFF) < DirtyToiletGauge;
		var gauge = (double)(toiletByte & 0xFF) * (double)-0.05f + (double)(float)item.Repair;
		item.Repair = (float)(gauge > 100 ? 100 : gauge >= 0 ? gauge : 0);
		var dirty = (QueueJoinRules.Truncate( (float)item.Repair ) & 0xFF) < DirtyToiletGauge;
		if ( dirty && !wasDirty )
		{
			// [BIN:TPW-EXE:0x004E2610 toilet turns dirty] the clean row 1 comes off and the dirty row 6 goes on at the toilet's cell
			SwapToiletEffect( item, RegionEffect.CleanToilet, RegionEffect.DirtyToilet );
			Raise( ParkEventKind.ToiletDirty, 0, item.Id, item.InfoId );
		}
		return dirty && !wasDirty;
	}

	/// <summary>
	/// The handyman's jobs (tp.exe <c>0x004D7100</c>): litter first, then toilets. A free handyman claims a littered cell nobody has claimed
	/// (the sweep, states 8 and 9) or, with no such cell, a dirty toilet (gauge byte below 25) that nobody cleans yet (states 10 and 11).
	/// Either job keeps him busy for <c>HandymanConstsPerGrade[grade].WorkDuration</c> park turns and the update that ends it.
	/// </summary>
	// [BIN:TPW-EXE:0x004D7100 handyman job search] after the strike/rest gate (0x00506A40) and the too-tired gate (energy byte below RestLevel), the litter finder 0x004C8ED0 scans the cells within DetectionRange (x outer, y inner) for litter claimed by nobody or by him and takes the nearest by Manhattan distance; only without litter does 0x004D7880 look for a toilet flagged by mRequestedService, or one whose gauge byte is below 25 within DetectionRange; a sweep or a clean ends when the turn passes the arrival stamp + WorkDuration[grade] (0x004D76A0, 0x004D73C0; the grade table 0x007853EC {WorkDuration, DetectionRange, PoundsPerTrainingPoint})
	// [EXT:headless-economy-dispatch] (user-approved, test and tool economy only; a level runs the staff world): without a world the handymen have no position, so a free handyman takes the first unclaimed littered cell in the finder's scan order, park-wide, else the dirtiest toilet, timed from the dispatch
	internal void DispatchHandymen()
	{
		var handymen = Staff.OfType( StaffType.Handyman ).Where( member => member.IsAvailable && !Staff.Strikes.IsActive( member.Type ) && !(HasRestArea && StaffVitals.Byte( member.Energy ) <= Settings.RestLevel) ).OrderBy( member => member.Id ).ToList();
		// no free handyman, no job: the role is read only for one (Theme Park Inc's settings hold no staff roles, and its slice runs this dispatch every turn without a staff world)
		if ( handymen.Count == 0 )
			return;
		var role = Settings[StaffType.Handyman];
		List<ParkObjectState>? dirty = null;
		using var litter = Litter.Littered().Where( cell => Litter.CollectorAt( cell.X, cell.Y ) == 0 ).GetEnumerator();
		foreach ( var handyman in handymen )
		{
			if ( litter.MoveNext() )
			{
				var (x, y) = litter.Current;
				Litter.Claim( x, y, handyman.Id, Turn );
				handyman.LitterCell = (x, y);
				handyman.BusyTicks = role.WorkDuration[handyman.Grade] + 1;
				handyman.State = StaffState.Working;
				continue;
			}
			dirty ??= objects.Values.Where( item => Catalog.TryGet( item.InfoId, out var info ) && info.ProvidesRelief
				&& (QueueJoinRules.Truncate( (float)item.Repair ) & 0xFF) < DirtyToiletGauge && HeadlessClaimant( item ) == 0 ).OrderBy( item => item.Repair ).ThenBy( item => item.Id ).ToList();
			if ( dirty.Count == 0 )
				break;
			var toilet = dirty[0];
			dirty.RemoveAt( 0 );
			handyman.BusyTicks = role.WorkDuration[handyman.Grade] + 1;
			handyman.AssignedInstanceId = toilet.Id;
			handyman.State = StaffState.Working;
			toilet.MechanicId = handyman.Id;
		}
	}

	private void EndDay( long day )
	{
		// [BIN:TPW-EXE:0x0050B360 thing update] the challenge system's day (0x004D1E90) is its thing update (type 0x13), which a lost park no longer runs
		// [BIN:TPW-EXE:0x004D2860 daily challenge message] the day message adds a running daily-average challenge's value to its sum
		if ( Features.Challenges && !IsBankrupt )
			Objectives.AccumulateDaily( MeasureChallenge );
		Raise( ParkEventKind.DayEnded, Balance );
	}

	/// <summary>The challenge system's thing update, once per park turn (tp.exe <c>0x004D1E90</c>): the switch, the offers and a running challenge's check.</summary>
	internal void UpdateChallenges( long turn )
	{
		if ( !Features.Challenges )
			return;
		foreach ( var (kind, index, amount, detail) in Objectives.Update( turn * ParkCalendar.SecondsPerTurn, turn, MeasureChallenge, IsParkOpen, BestObjectOf, () => guestStatistics.GuestCensus ).ToList() )
		{
			if ( kind == ParkEventKind.ChallengeCompleted )
				Post( LedgerCategory.OtherIncome, amount );
			Raise( kind, amount, 0, index, detail );
		}
	}

	/// <summary>The object a passing check of type 14, 15 or 18 carries: the best coaster, or for the kart type the kart ride with the most crossings now.</summary>
	private int BestObjectOf( ChallengeDefinition definition )
	{
		// [BIN:TPW-EXE:0x4cc810 best coaster] the checks of types 14 and 18 store the best coaster's id, the newest first and replaced only by a strictly higher count; the win 0x4d2fb0 copies it to the challenge system's filter (+0x3e0) and into the follow-up slot; the accumulator 0x4d27a0 then adds only the carried ride's uses and none of the object-less events (the sales, profits, admissions and daily messages)
	// [BIN:TPW-EXE:0x4d1660 challenge check] type 14 asks the best coaster of any kind (0x4cc810 with 0), type 18 the best of the info id TargetObj, type 15 the best kart ride (0x4cc4b0)
		if ( definition.Type == 15 )
			return KartBestCrossings?.Invoke().Instance ?? 0;
		return CoasterBestObject?.Invoke( definition.Type == 14 ? 0 : definition.TargetObject ) ?? 0;
	}

	private readonly Dictionary<(ParkRecordKind Kind, int InfoId), (long Value, int Instance)> recordBest = new();

	// [BIN:TPW-EXE:0x4d1660 challenge check] type 15 compares TargetVal with the most crossings of any kart ride now (0x4cc4b0, no TargetObj test), type 21 with the most sections of the track rides of the Info.Id TargetObj (0x4cc5d0); a park without such a ride measures 0, as a headless economy does
	private long MeasureChallenge( ChallengeDefinition definition ) => definition.Type switch
	{
		15 => KartBestCrossings?.Invoke().Crossings ?? 0,
		21 => TrackRideSections?.Invoke( definition.TargetObject ) ?? 0,
		_ => Objectives.Measure( definition, Counters, Staff, objects.Values, guestStatistics, CoasterCountedSections )
	};

	private void EndMonth( long nextMonthIndex )
	{
		// [BIN:TPW-EXE:0x004D0370 bank month] the month message first pays the batch word (+0x10, an unsigned credit below 1,000,000 by the routine's own assertion) into the balance, the money-in total (+0x1FC90) and the year's profit (+0x124), then zeroes it (0x4D0393..0x4D03DB), before any instalment
		if ( BatchCredit != 0 )
		{
			Post( LedgerCategory.OtherIncome, BatchCredit );
			BatchCredit = 0;
		}
		var wages = Rules.Staff ? Staff.TotalMonthlyWages : 0;
		if ( wages > 0 )
		{
			Post( LedgerCategory.StaffCosts, wages );
			Raise( ParkEventKind.WagesPaid, wages, 0, 0, $"{Staff.Members.Count} staff" );
		}
		PayRunningCosts();
		var training = Rules.Staff ? Staff.Train() : 0;
		if ( training > 0 )
		{
			// [BIN:TPW-EXE:0x00505A10 staff training] the training is a debit (0x004D01F0, money out) added to its own analyser word +0x1FA40, which no finance row shows;
			// the staff costs row (+0x1F7F0, graph bit 0x80 at 0x0049A7B0) takes only the wages (0x00504C70, 0x00505790)
			Post( LedgerCategory.OtherCosts, training );
			Raise( ParkEventKind.TrainingPaid, training );
		}
		if ( Rules.Staff )
			ConsiderStrikes();
		// [BIN:STP-PPC:0x100CC21C loan instalment] each bought loan, in offer-index order, pays exactly M and counts one more month repaid; it ends after N months (ORACLE F13)
		// [BIN:TPW-EXE:0x4d0370 bank month] the instalment is deducted only with mWithdrawalsEnabled (+0x114) set, as a debit that keeps mLastBalance and the
		// turn-entered-red stamp; the year profit then gains back the principal share either way, so with the flag clear the instalment raises the profit by it
		// [BIN:TPW-EXE:0x404140 layout replay start] besides the bank constructor (0x4cf7c0, 1) and the serializer (0x4cf920), only the online layout replay writes +0x114: its start clears it
		// (sole caller 0x54f11f, in the GameType 1 branch of the level start) and its end 0x4041d0 sets it again, so a single-player park never clears it, as here
		foreach ( var loan in loans.OrderBy( item => item.OfferIndex ).ToList() )
		{
			var (account, paid) = LoanMath.Pay( loan );
			var interest = LoanMath.InstalmentInterest( account.MonthlyRepayment, account.Months, account.OriginalAmount );
			if ( WithdrawalsEnabled )
			{
				Post( LedgerCategory.LoanPayments, paid );
				Ledger.ChargeLoanInterest( interest );
			}
			else
				Ledger.ChargeLoanInterest( interest - paid );
			Raise( ParkEventKind.LoanPayment, paid, 0, account.OfferIndex );
			var position = loans.IndexOf( loan );
			if ( account.MonthsRemaining <= 0 )
			{
				loans.RemoveAt( position );
				// [BIN:STP-PPC:0x100CC21C loan instalment] a fully repaid loan clears its bought flag; 0x100CC9E8 then offers it again when the credit test passes
				// [BIN:TPW-EXE:0x4d0370 bank month] the last instalment clears the bought flag (+0x28); the Available tab lists the offer again once a list build's credit test (0x4d06b0) passes
				takenOffers.Remove( account.OfferIndex );
				Raise( ParkEventKind.LoanRepaid, 0, 0, account.OfferIndex );
			}
			else
				loans[position] = account;
		}
		foreach ( var item in objects.Values )
		{
			// [EXT:ring-month-sums] the calendar-month sums roll here as before the rings, kept for saves written without them (see ParkSaveFile.RingAt); the original has only the rings
			// [BIN:TPW-EXE:0x004DB090 attraction history rings] an attraction's six daily 30-day rings follow tp.exe (costs +0xF8, takings +0x70, customers +0x1A8, walk-aways +0x230, winners +0x2B8 and the visits' averaged happiness change +0x340: the constructor 0x004DB090 starts each at head -1, size 30; the day message 0x004DD320 rolls them; the windows and lists sum them over their counts, 0x004E1C70, 0x00495C20, 0x004B01D0, 0x004B16C0, 0x004955E0, 0x00493CD0), the happiness-change ring is written as the use routine 0x004FD970 does (3 x the guest's happiness byte change since joining the queue, averaged in by 0x004E1E00; a won sideshow's tail is GUEST-005); the walk-aways are kept as the list of their last 30 days beside the ring type, which sums alike, and the customers row of the shop and sideshow windows shows "{customers} of {customers + walk-aways}" (UITEXT record 0x1CC, 0x4B0423, 0x4B18E6; ParkHud.CustomersText)
			item.CustomersLastMonth = item.CustomersThisMonth;
			item.TakingsLastMonth = item.TakingsThisMonth;
			item.CostsLastMonth = item.CostsThisMonth;
			item.WinnersLastMonth = item.WinnersThisMonth;
			item.CustomersThisMonth = item.TakingsThisMonth = item.CostsThisMonth = item.WinnersThisMonth = 0;
		}
		var admissionsBefore = monthlyAdmissions.Sum();
		monthlyAdmissions.Add( Counters[ParkCounters.Admissions] - admissionsBefore - DroppedAdmissions );
		if ( monthlyAdmissions.Count > ParkLedger.MaximumHistoryMonths )
		{
			DroppedAdmissions += monthlyAdmissions[0];
			monthlyAdmissions.RemoveAt( 0 );
		}
		// [BIN:TPW-EXE:0x4d0370 bank month] after the instalments, a park whose balance and mLastBalance are both below 0 counts the 30-day months since
		// the turn-entered-red stamp; above 5 it posts message type 2 (0x0040F630, bus DAT_00788D3C), at every month end while it stays so
		MonthsInRed = MonthsSinceEnteringRed();
		if ( MonthsInRed >= MonthsInRedForBankruptcy )
		{
			Raise( ParkEventKind.BankruptcyMessage, MonthsInRed );
			HandleBankruptcyMessage();
		}
		// [BIN:TPW-EXE:0x004C7720 month sample] the analyser's month handler stores the net worth read with age 0 (0x004C79A7)
		// [BIN:TPW-EXE:0x004C7720 month sample] the same handler reads the cached census with age 0 first (0x004C78C1); the month message goes to the things in id order (the listener tree walked by 0x0040FB10, dispatching through 0x0050B550) and the world constructor 0x005156A0 creates the staff manager (type 9) before the analyser (type 13), so the gate above has already read its count
		var census = CachedGuestCensus( 0 );
		// [BIN:TPW-EXE:0x004C7720 month sample] the same handler fills the statistics rings the park information window graphs: the arrivals, the guests in the park (this census), the mean happiness (0x4C7BB0) and the mean stay
		var rating = ParkRating;
		StatisticsHistory.CloseMonth( guestStatistics.CensusHappiness, census, rating );
		CloseMisbehavingMonth();
		var closed = Ledger.CloseMonth( nextMonthIndex, rating, NetWorth( 0 ) );
		Raise( ParkEventKind.MonthEnded, closed.ClosingBalance, 0, 0, $"in ${closed.MoneyIn}, out ${closed.MoneyOut}" );
		if ( nextMonthIndex % ParkCalendar.MonthsPerYear == 0 )
		{
			var year = Ledger.SummariseYear( nextMonthIndex / ParkCalendar.MonthsPerYear - 1 );
			Raise( ParkEventKind.YearEnded, year?.Profit ?? 0, 0, 0, year == null ? "" : $"in ${year.MoneyIn}, out ${year.MoneyOut}, balance ${year.ClosingBalance}" );
		}
	}

	/// <summary>
	/// Theme Park Inc's monthly costs: every object is debited its current level's <c>Upgrades[n].RunningCost</c> (nothing in ride state 1 or 4) plus the costs its sales and plays booked during the month, as one debit
	/// that also enters its cost statistics (the month's cost, the 30-day cost ring and the total profit). Theme Park World has no such cost and debits its goods and prizes at the sale.
	/// </summary>
	// [BIN:TPI-EXE:0x005DEFFF monthly object cost] the object's month message takes 0 in state 1 or 4, else the type record's running cost (thing +0x84, the current level's Upgrades[n].RunningCost), adds the amount its statistics block accumulated (+0x938, 0x5DF029..0x5DF042, whatever the state), writes the running cost into +0xDD8 and the sum into the cost ring +0xB88, takes profit +0x1028 = takings +0x6E8 - the sum, clears the accumulators and the monthly rings it rolls, and debits the bank (0x5C07B0, the twin of tp.exe 0x4D01F0) with the sum, adding it to the game's costs word (+0x20F28)
	// [BIN:TPI-EXE:0x5E60E0 cost booking] raises the daily cost ring (+0x88) and the month accumulator (+0x938) and posts the negated cost as message 0xC (a shop) or 0xD (a sideshow) to the analyser object (0x408EE5, 0x4082C9, 0x4020BD): the statistics' own feed; no debit
	// [BIN:TPI-EXE:0x5C07B0 bank debit] balance -= amount at once; every site that debits is a purchase, an upgrade, a wage or this month message: the guest's use outcome never does
	// [APPROX:ECON-060] Theme Park Inc's statistics are monthly rings (+0xB88 costs, +0x1028 profit, rolled by the same message) where OpenTPW's are the daily 30-day rings of Theme Park World (ECON-057), so the month's cost enters today's slot of the daily ring (the sales' own costs already did at the sale), and the order of this debit against the bank's own month handler (the batch credit, the loan instalments) is not read, so OpenTPW debits the running costs after the wages' place and before the loan instalments
	private void PayRunningCosts()
	{
		if ( !Rules.ThemeParkInc )
			return;
		long total = 0;
		var charged = 0;
		foreach ( var item in objects.Values )
		{
			var running = RunningCost( item );
			var cost = running + item.PendingMonthlyCosts;
			item.PendingMonthlyCosts = 0;
			if ( cost == 0 )
				continue;
			Post( LedgerCategory.OtherCosts, cost );
			item.CostsThisMonth += cost;
			// the month message also writes the sum into the object's cost statistics and its profit (0x5DF070..0x5DF0A4: costs +0xB88, profit = takings +0x6E8 - costs)
			item.CostsRing.Today += unchecked( (int)running );
			item.TotalProfit -= cost;
			total += cost;
			charged++;
		}
		if ( total != 0 )
			Raise( ParkEventKind.RunningCostsPaid, total, 0, charged, $"{charged} objects" );
	}

	/// <summary>The object's monthly running cost now: 0 broken down or condemned, else its current level's <see cref="UpgradeLevelInfo.RunningCost"/>.</summary>
	public long RunningCost( ParkObjectState item )
	{
		if ( !Rules.ThemeParkInc || !Catalog.TryGet( item.InfoId, out var info ) || info.Upgrades.Count == 0 )
			return 0;
		if ( ObjectState( item ) is RideObjectStates.BrokenDown or RideObjectStates.Condemned )
			return 0;
		return info.Upgrades[Math.Min( item.Level, info.Upgrades.Count - 1 )].RunningCost;
	}

	/// <summary>
	/// The bank's handler of its own message type 2 (tp.exe <c>0x004D02D0</c>): once, when the world is not yet in state 4 and no winning sequence
	/// shows, the park is lost (<c>0x005168F0</c>). The park-closed word is not written; the host requests a breakdown of every attraction and gives the
	/// gate its end-of-park command on <see cref="ParkEventKind.Bankrupt"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x004D02D0 bank message] a type-19 message (CMsgEvent) with value 2 calls 0x005168F0 only while the world state (+0x1DA738) is not 4 and 0x00516C00 (a winning sequence's thing id at 0x7CF4F4) answers false
	// [BIN:TPW-EXE:0x005168F0 park lost] world state = 4; every live thing of type 3 with flag +0x32 bit 4 gets VAR_BREAKSTAT = 1 (0x004E0DB0, "Object %d requested breakdown"); the door routine 0x00519EF0(0, 1) writes only the gate script's variable 0 = 2
	// [BIN:TPW-EXE:0x004D0370 bank month] the routine compares the months in the red only with 5 (above it posts type 2 at 0x004D0557): it gives no warnings before
	// [BIN:TPW-EXE:0x0054F8BB lost park sound] the main loop's sound update sends the music parameter and the crowd count as 0 while the world state is 4 (0x0054F86D, 0x0054F8BD), which GameAudio does for a bankrupt park
	// [BIN:TPW-EXE:0x0059B620 advisor caption] the advice 106 the bank's message brings says the responses 274 and 275, whose tag 128 shows TAG_SYSTEM entry 127 ("the banks have closed your park") in the message tube (AutomaticAdvisor.PostCaption); the money warnings are the captions of the advice messages 102 to 105 (entries 123 to 126)
	// [APPROX:ECON-030] the lost park follows the bank handler, thing-loop stop, zero arrival target and silent music/crowd; AutomaticAdvisor already produces money advice 102-105 through AdvisorScan and captions advice 106 on each bank message. The five-entry pause menu (0x48C150), the loss window 0x7472 with its gate camera (0x5168F0 -> 0x481B50 -> 0x48AC40, handler 0x48A740, layout 0x74FB20, banner table 0x74FC38), its dismissal into the hidden-HUD overlay (0x4A29D0) and the loaded-loss restore without the camera (0x4815E0 -> 0x48ADB0) are ported for single-player parks. The audit of the economic guards is done: tp.exe reads the world state 4 only at the bank handler (0x4D030F), the arrival target (0x4CF5BC, zero), the thing loop (0x516390), the music and crowd sound (0x54F862, 0x54F8B2), the load restore (0x4815F2), the pause menu's entries (0x48C27C) and the HUD's overlay (the key branch 0x4A2925, which lets only an Escape without modifiers through to the pause menu, and the hide toggle's early-out 0x4A2AC9); no purchase, hire, loan, build or open routine tests it, so the original refuses nothing in a lost park itself and its HUD, which holds every such control, is hidden. OpenTPW's overlay does the same for a single-player park (every shortcut and click is off under it, ParkHud.ParkShortcutsAllowed) and its economy-level refusals (ParkEconomy.IsBankrupt) stay as the stand-in where the overlay is not shown, where they are the only refusal: in co-op and visits as [EXT:online-coop-money] (user-approved 2026-10-30: solo parks get the loss scene, shared parks and visits keep the economy refusals, not part of this ID) and in Theme Park Inc, which has no bankrupt.MD2 banner and whose exe was not read. The banner's animation is read to its clock and cadence (the step runs on the frame message 0x1e, which the per-frame update 0x658F6C sends down the window tree from 0x48A5DF, through 0x65C357 to the stepper 0x65E9A4: strictly more than the interval 35 ms since the last step on the clock timeGetTime, 0x65968E), as UiWindowAnimation steps it once per drawn frame. Still open: the loss scene of a Theme Park Inc park
	private void HandleBankruptcyMessage()
	{
		if ( IsBankrupt || WinningSequenceActive?.Invoke() == true )
			return;
		IsBankrupt = true;
		Raise( ParkEventKind.Bankrupt, MonthsInRed );
	}

	/// <summary>Loses the park as the bank's message does (a capture of the loss window; the bank's own month rule is left alone).</summary>
	// [EXT:debug-hud] developer/screenshot helper, not original behaviour
	internal void DebugLose() => HandleBankruptcyMessage();

	/// <summary>Admissions of months dropped from the bounded history (keeps the monthly differences exact).</summary>
	public long DroppedAdmissions { get; private set; }

	// ---------------------------------------------------------------- park values

	/// <summary>
	/// Scrap value, the refund when an object is sold (the sale 0xDBFA8 credits the getter 0xE25E8, which the scrap value
	/// panels also show): the base purchase price <c>Upgrades[0].CostOfUpgrade</c> (catalogue +440; upgrade costs are not added)
	/// times <see cref="ScrapPercent"/>, divided by 100 (ORACLE.md F21).
	/// </summary>
	// [BIN:STP-PPC:0x100E25E8 scrap value] Upgrades[0].CostOfUpgrade × the percentage 0xE2424, unsigned division by 100
	// [BIN:TPW-EXE:0x004E2400 scrap value] the same product: the percentage 0x004E2290 × record +0x1B8 (Upgrades[0].CostOfUpgrade), unsigned division by 100; the sale 0x004DD0A0 credits it (executed by the x86 oracle target tpw-exe-scrap-value, ORACLE.md X13)
	public long ScrapValue( ParkObjectState item )
	{
		if ( !Catalog.TryGet( item.InfoId, out var info ) || info.Upgrades.Count == 0 )
			return 0;
		// the 32-bit multiply wraps and the division is unsigned (cost 50,000,000 at 100 % refunds 7,050,327)
		return unchecked( (uint)((int)info.Upgrades[0].CostOfUpgrade * ScrapPercent( item )) ) / 100;
	}

	/// <summary>The scrap percentage of an object that requires watering (<c>0x5E75F0</c> first branch): 100, 67, 34 and 0 for a count of 0 to 3, negative above.</summary>
	// [BIN:TPI-EXE:0x5E7613 watering percentage] fild n; fsubr 3.0 (0x93AD54); times 33.33333206176758 (0x93B818, a single) in the x87's extended precision; the product is truncated toward zero (0x86FAB0); 3 - n compared with 0.0 (0x93A038): equal returns the truncated product, else 1 + it
	public static int ThemeParkIncWateringPercent( int count )
	{
		var remaining = 3.0 - count;
		var truncated = (int)(remaining * 33.33333206176758);
		return remaining == 0.0 ? truncated : 1 + truncated;
	}

	/// <summary>
	/// The object's scrap percentage (0xE2424): the age is the park clock since the build, in 365-day years (0, 1, 2, anything
	/// else uses year 4), read from the current level's row <c>Upgrades[level].ScrapValueYearN</c>. An object nobody has used
	/// (<see cref="ParkObjectState.TimesUsed"/> 0) in its first 30 days is refunded 100 percent, when the base level's year-1
	/// percentage is above 0.
	/// </summary>
	// [BIN:STP-PPC:0x100E2424 scrap percentage] age = clock stamp − thing +20 as signed 100 ns units; quotient by 365 days (exactly) 0 → year 1, 1, 2, else (also negative) year 4; in year 1, quotient by 30 days 0 and thing +412 = 0 and record +392 > 0 give 100; the row is record +392 + 64 × level
	// [BIN:TPW-EXE:0x004E2290 scrap percentage] the same rule at the PC offsets: stamp thing +0x18, level byte +0x50, use counter +0x1A0 (cleared by the constructor 0x004DB090, raised with the history word +0x1A8 by 0x004E1690 when a guest leaves, 0x004FD970), percentages at record +0x188 + 64 × level; identical to 0x100E2424 on 572 inputs (ORACLE.md X13)
	public int ScrapPercent( ParkObjectState item )
	{
		if ( !Catalog.TryGet( item.InfoId, out var info ) || info.Upgrades.Count == 0 )
			return 0;
		// [BIN:TPI-EXE:0x005E75F9 scrap percentage] the routine tests the thing flag +0x43 bit 0x10 first (set for a type with UsageInfo.RequiresWatering, 0x5DB654): the script variable 0 of the thing's script (0x6B0740 finds it by the thing id +0x2C, 0x6B0C90 reads variable 0; no script reads 0) is the count n, and the answer is ftol((3 - n) x 33.333332) plus 1 unless 3 - n is exactly 0: 100, 67, 34, 0 for n = 0..3, then -32, -65 and so on (0x5E7613..0x5E764F); the condemned test follows only for the other objects
		if ( Rules.ThemeParkInc && info.RequiresWatering )
			return ThemeParkIncWateringPercent( item.ScriptCondition );
		// [BIN:TPI-EXE:0x005E765B scrap percentage] Theme Park Inc's percentage routine 0x005E75F0 refuses a condemned object (state +0xA0 = 4) any refund before it reaches the year rule it shares with tp.exe 0x004E2290
		if ( Rules.ThemeParkInc && ObjectState( item ) == RideObjectStates.Condemned )
			return 0;
		var level = Math.Min( item.Level, info.Upgrades.Count - 1 );
		var age = ParkCalendar.Seconds( Tick ) - ParkCalendar.Seconds( item.BuiltTick );
		var year = age / (365 * ParkCalendar.SecondsPerDay);
		int Row( int index ) => info.Upgrades[level].ScrapValuePercentByYear.Count > index ? info.Upgrades[level].ScrapValuePercentByYear[index] : 0;
		if ( year == 0 )
		{
			if ( age / (30 * ParkCalendar.SecondsPerDay) == 0 && item.TimesUsed == 0 && info.Upgrades[0].ScrapValuePercentByYear.FirstOrDefault() > 0 )
				return 100;
			return Row( 0 );
		}
		return year is 1 or 2 ? Row( (int)year ) : Row( 3 );
	}

	/// <summary>
	/// Park value (UITEXT 163, the net worth): the cell points of the owned map cells (<see cref="ParkValueRules.CellPoints"/>) and per object, unless it is condemned,
	/// the costs of its upgrade levels up to the current one times its state of repair / 100 (ORACLE.md F29). A headless economy without a cell map has the object part only.
	/// </summary>
	// [BIN:STP-PPC:0x100C1D4C park value] the thing loop adds sum(Upgrades[0..level].CostOfUpgrade) × byte(state of repair) / 100 for every object that is not in state 4; the 128 × 128 cell loop adds 1, 3, 5, 1, 1, 2 and 50 points by cell type (the predicates 0x10085174..0x10085418) for cells without the unowned flag 0x40; both executed by the oracle target park-value
	// [BIN:TPW-EXE:0x004C7DB0 net worth] the same sums in a 32-bit word; the result is cached (+0x1F0EC, stamp +0x1F0E8) and counted again only when the reader's age is below (turn >> 2) − (stamp >> 2): the month's history sample reads it with age 0, the loans' credit test with 20 (NetWorth)
	public long ParkValue => ComputeNetWorth();

	private bool netWorthCached;
	private long netWorthStamp;
	private int netWorthValue;

	/// <summary>
	/// The net worth as a reader of the analyser's cache sees it (tp.exe <c>0x004C7DB0</c>): the cached value while (turn >> 2) − (stamp >> 2) is at most
	/// <paramref name="maximumAge"/> (unsigned), else counted again and stamped with the current turn. The cache is saved with the park (older saves count at the first read).
	/// </summary>
	public int NetWorth( uint maximumAge )
	{
		var age = unchecked( (uint)(Turn >> 2) - (uint)(netWorthStamp >> 2) );
		if ( !netWorthCached || maximumAge < age )
		{
			netWorthValue = ComputeNetWorth();
			netWorthStamp = Turn;
			netWorthCached = true;
		}
		return netWorthValue;
	}

	/// <summary>
	/// <see cref="NetWorth"/> without writing the cache: the cached value while its age is at most <paramref name="maximumAge"/>, else a fresh count that is not stamped.
	/// </summary>
	internal int PeekNetWorth( uint maximumAge )
	{
		var age = unchecked( (uint)(Turn >> 2) - (uint)(netWorthStamp >> 2) );
		return netWorthCached && age <= maximumAge ? netWorthValue : ComputeNetWorth();
	}

	/// <summary>The net worth cache (the park save), null until a read has counted it.</summary>
	internal (int Value, long Stamp)? CachedNetWorth => netWorthCached ? (netWorthValue, netWorthStamp) : null;

	/// <summary>Restores a saved net worth cache.</summary>
	internal void RestoreNetWorthCache( int value, long stamp )
	{
		netWorthValue = value;
		netWorthStamp = stamp;
		netWorthCached = true;
	}

	private bool censusCached;
	private long censusStamp;
	private int censusValue;
	private long? crowdPhaseTurn;

	/// <summary>
	/// The guests on a counted cell as a reader of the analyser's cache sees them (tp.exe <c>0x004C7FA0</c>): the cached count while (turn >> 2) − (stamp >> 2)
	/// is at most <paramref name="maximumAge"/> (unsigned), else counted again (<see cref="IParkGuestStatistics.GuestCensus"/>) and stamped with the current turn.
	/// </summary>
	// [BIN:TPW-EXE:0x004C7FA0 cached census] valid flag +0x1F0C0, count +0x1F0BC, stamp +0x1F0B8 (0x0041A960 stores the turn, 0x0041A990 answers (turn >> 2) − (stamp >> 2)): counted again when the flag is clear or the age is above the reader's; the counted guests are the things of type 1 on a cell the predicate 0x004FA990 accepts
	// [BIN:TPW-EXE:0x004C58C0 analyser constructor] the cache starts invalid with stamp 0
	// [BIN:TPW-EXE:0x00508E70 month gate] the staff manager reads it with age 5, the arrival target (0x004CF5B0) with 20 (0x14), the month sample (0x004C7720, 0x004C78C1) and the crowd sound (0x004C81E0) with 0
	internal int CachedGuestCensus( uint maximumAge )
	{
		var age = unchecked( (uint)(Turn >> 2) - (uint)(censusStamp >> 2) );
		if ( !censusCached || maximumAge < age )
		{
			censusValue = guestStatistics.GuestCensus;
			censusStamp = Turn;
			censusCached = true;
		}
		return censusValue;
	}

	/// <summary>The census cache (the park save), null until a read has counted it.</summary>
	internal (int Value, long Stamp)? CachedCensus => censusCached ? (censusValue, censusStamp) : null;

	/// <summary>The park turn the main loop's step counter started from (0x00877D34 = 0), which times the crowd reads; null until the first turn runs.</summary>
	internal long? CrowdPhaseTurn => crowdPhaseTurn;

	/// <summary>Restores a saved census cache and the crowd read's phase.</summary>
	internal void RestoreCensusCache( int value, long stamp, long? phaseTurn )
	{
		censusValue = value;
		censusStamp = stamp;
		censusCached = true;
		crowdPhaseTurn = phaseTurn;
	}

	/// <summary>Restores only the crowd read's phase (a save without a census cache).</summary>
	internal void RestoreCrowdPhase( long? phaseTurn ) => crowdPhaseTurn = phaseTurn;

	private int ComputeNetWorth()
	{
		var total = CellPoints();
		foreach ( var item in objects.Values )
			total = unchecked( total + (int)ObjectValue( item ) );
		return total;
	}

	private (ParkCellMap? Map, int Version, int TrackVersion, int Points) cellPoints;

	/// <summary>The map part of the net worth; counted again only when the cell map changed (every cell write bumps its version), so a HUD read costs no map walk.</summary>
	private int CellPoints()
	{
		if ( Litter.Cells is not { } cells )
			return 0;
		if ( ReferenceEquals( cellPoints.Map, cells ) && cellPoints.Version == cells.Version && cellPoints.TrackVersion == cells.Tracks.Version )
			return cellPoints.Points;
		var total = 0;
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				if ( (cells.FlagsAt( x, y ) & ParkCellFlags.Unowned) != 0 )
					continue;
				var (type, structure) = CellForValue( cells, x, y );
				total = unchecked( total + ParkValueRules.CellPoints( type, structure ) );
			}
		}
		cellPoints = (cells, cells.Version, cells.Tracks.Version, total);
		return total;
	}

	/// <summary>The park's record for global ticket <paramref name="kind"/> (<see cref="ParkObjectives.GlobalRecord"/>).</summary>
	internal long GlobalTicketRecord( GoldenTicketKind kind ) => ParkObjectives.GlobalRecord( kind, Counters, CoveredCells() );

	/// <summary>
	/// The big park ticket's count (tp.exe <c>0x004CD7F0</c>): the cells, owned or not, whose type is 4, 9, 10 or 21 or that lie under a counted structure; null without a cell map.
	/// </summary>
	// [BIN:TPW-EXE:0x004CD7F0 covered cells] the loop over x and y from 127 down to 0 counts a cell when 0x00536340 (type 9), 0x00536350 (10), 0x00536380 (4), 0x005363F0 (the structure test of the net worth, ECON-026) or 0x005363E0 (type 21) answers
	internal long? CoveredCells()
	{
		if ( Litter.Cells is not { } cells )
			return null;
		var total = 0L;
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				var (type, structure) = CellForValue( cells, x, y );
				if ( structure || type is 4 or 9 or 10 or 21 )
					total++;
			}
		}
		return total;
	}

	/// <summary>
	/// The share of the cells with terrain that carry a security level, in whole percent (tp.exe <c>0x004CD720</c>): over the 128 × 128 cells without type 2, 7 or 30, those whose
	/// environment word +6 (the security coefficient the cameras and the guards spread, <see cref="ParkEnvironment"/>) is above 0, times 100 over their number; 0 without such a cell.
	/// Null without a cell map.
	/// </summary>
	// [BIN:TPW-EXE:0x004CD720 camera coverage] each cell is skipped when 0x00536300 (type 2), 0x00536360 (type 7 or 30) or 0x005D60C0 (always 0) answers; the count of the rest and the count whose 10-byte environment cell has the short at +6 above 0 (0x004D8410, the guard prank test 0x00508A30 calls it the security level) give coverage * 100 / count
	internal int? SecurityCoveragePercent()
	{
		if ( Litter.Cells is not { } cells )
			return null;
		long coverable = 0, covered = 0;
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				if ( CellForValue( cells, x, y ).Type is 2 or 7 or 30 )
					continue;
				coverable++;
				if ( Litter.Environment.At( x, y ).Security > 0 )
					covered++;
			}
		}
		return coverable == 0 ? 0 : (int)(covered * 100 / coverable);
	}

	/// <summary>
	/// A cell's map type and whether its structure record counts 50, from OpenTPW's cell map: a track tile keeps its type in the map's type byte as well as in the track
	/// layer (the original's structure records). Stations preserve the overwritten base type; ordinary and legacy track cells use 0.
	/// </summary>
	// [BIN:TPW-EXE:0x004D0AF0 structure records] a cell's structure record is not part of the cell: the records (0x28 bytes, the type in +8, 0x0B to 0x19) are an array of their own indexed by the cell id, 0x004D0AF0 (the array at game +0x10FFD8), and an extension record (types 0x0C and 0x11) names its parent through +0x10 (0x0053B280, 0x005363F0); the cell keeps its own type in +8 of its 0x44-byte record (0x536300..0x5363E0), whose only immediate writers in the map code are the init 0x00536490 (7, 2, 30, 5, 1, 0)
	// [BIN:TPW-EXE:0x0053AD90 counted structures] 0x005363F0 counts 50 for the record of a cell when its type is 0x0B, 0x0D, 0x10, 0x12 or 0x19, or when it is an extension (0x0C, 0x11) of such a record: the fence walk types the three cells beside every post (east, south, south-east) as extensions of it (0x52BA50), so a hoarding post counts its whole 2 x 2 block, each cell judged by its own flag 0x40
	// [EXT:no-land-maps] a map without the level's land counts every cell over the terrain as owned: the rest is the original's rule (ORACLE.md X35 and X38: the cell points of the saves' own cells, a station's footprint cells and an add-on's cells equal the oracle's net worth)
	// [BIN:TPW-EXE:0x00528A70 station under its footprint] a kart or water ride's two station tiles lie on eight of its footprint cells (rows 1 and 2 across the heading): the commit stamped those cells type 4 before the station line wrote the second layer, so they count 0 + 50, not 2 + 50 (ParkCellMap.WriteFootprint keeps the stamped type as the tile's base type)
	// [BIN:TPW-EXE:0x004C7DB0 net worth] the cell's type is its own map type: under a track tile the map keeps what the ground was (0 on plain ground, the base type a station overwrote), which is what this reads
	internal static (byte Type, bool Structure) CellForValue( ParkCellMap cells, int x, int y )
	{
		var track = cells.Tracks.Get( x, y );
		// [BIN:TPW-EXE:0x005323F0 park fence] the fence setup lays the level's Hoardings.sam clicks with the track tool in mode 0x19 (0x00524960), each post a structure record of type 25, which the net worth
		// counts 50 for through 0x005363F0; the land keeps the posts as a ring (ParkLand), so a post cell is a structure cell here
		if ( track.IsEmpty )
			return (cells.RawTypeAt( x, y ), cells.Land?.CountsAsStructure( x, y, cells.Width, cells.Height ) == true);
		var record = track.Type;
		if ( track.IsExtension && track.Parent >= 0 )
		{
			var (px, py) = cells.Tracks.CellOf( track.Parent );
			record = cells.Tracks.Get( px, py ).Type;
		}
		var type = cells.RawTypeAt( x, y ) == track.Type ? track.BaseType : cells.RawTypeAt( x, y );
		// [BIN:TPW-EXE:0x00528A70 add-on over its tile] an add-on's commit stamps type 4 on the first-layer cell (0x00532FC0, '+' stored as 4, 0x005290C7..0x005290EE) under the ride's straight tile, which stays on the second layer: the four cells of a placed add-on are type 4 (0 points) beneath their structure records, in the x86 oracle (ORACLE.md X38)
		if ( (track.Flags & TrackCell.FlagAddOn) != 0 )
			type = ParkCellTypes.Object;
		return (type, ParkValueRules.IsCountedStructure( record ));
	}

	/// <summary>One object's share of the park value (<see cref="ParkValueRules.ObjectValue"/>); condemned objects count for nothing.</summary>
	private long ObjectValue( ParkObjectState item )
	{
		if ( !Catalog.TryGet( item.InfoId, out var info ) || !ParkValueRules.CountsForValue( ObjectState( item ) ) )
			return 0;
		var costs = Enumerable.Range( 0, item.Level + 1 ).Select( level => level < info.Upgrades.Count ? unchecked( (int)info.Upgrades[level].CostOfUpgrade ) : 0 );
		return ParkValueRules.ObjectValue( costs, (float)item.Repair );
	}

	public int LitterItems => Litter.Count;

	/// <summary>
	/// Park rating 0–100 (UITEXT 155): a sum of capped counts of guests, attractions and staff. It does
	/// not use guest happiness, litter or whether an attraction is open.
	/// </summary>
	// [BIN:STP-PPC:0x100C7B24 park rating] min(counted guests, 1000) × 20 / 1000; objects by Info.WhichUIType (type +1960): rides (0) × 3 / 2 up to 20, shops (1) and sideshows (2) × 2 up to 10 each, features (3) up to 10, rides at upgrade level 2 or more up to 10; each of the five staff classes with the thing byte +3 clear up to 4; executed by the oracle target park-rating (ORACLE.md F29)
	public int ParkRating
	{
		get
		{
			// [BIN:TPW-EXE:0x004CCEB0 park rating] the visitor term counts the guests on a cell of type 0, 1, 3, 9 or 10 now (the predicate 0x004FA990, no cache), capped at 1000; the staff terms count the members whose byte +3 is clear, which only the deleter sets (0x0050B8E9) before it marks the thing deleted (0x0050B906): a dismissal removes the member at once here too
			var guests = Math.Min( guestStatistics.GuestCensus, 1000 ) * 20 / 1000;
			// An object counts by the type's Info.WhichUIType: the shipped add-ons (the upgrades directory) declare 0 and count as rides, fixed items declare 4 and count for nothing.
			int Count( int uiType, int minimumLevel = 0 ) => objects.Values.Count( item => Catalog.TryGet( item.InfoId, out var info ) && info.RatingType == uiType && item.Level >= minimumLevel );
			var attractions = Math.Min( Count( 0 ) * 3 / 2, 20 )
				+ Math.Min( Count( 1 ) * 2, 10 )
				+ Math.Min( Count( 2 ) * 2, 10 )
				+ Math.Min( Count( 3 ), 10 )
				+ Math.Min( Count( 0, 2 ), 10 );
			var staff = Enum.GetValues<StaffType>().Sum( type => Math.Min( Staff.Members.Count( member => member.Type == type ), 4 ) );
			return guests + attractions + staff;
		}
	}

	// ---------------------------------------------------------------- park and prices

	/// <summary>Restores a saved loss without replaying its transition; losing does not change the saved gate-open bit.</summary>
	internal void RestoreOriginalLoss( bool open )
	{
		IsBankrupt = true;
		IsParkOpen = open;
	}

	public void OpenPark()
	{
		if ( IsBankrupt || IsParkOpen )
			return;
		IsParkOpen = true;
		Raise( ParkEventKind.ParkOpened );
	}

	public void ClosePark()
	{
		if ( !IsParkOpen )
			return;
		IsParkOpen = false;
		Raise( ParkEventKind.ParkClosed );
	}

	public void SetEntranceFee( int fee ) => EntranceFee = fee >= 0 ? fee : throw new ArgumentOutOfRangeException( nameof( fee ) );

	/// <summary>Sets a shop's sale price or a sideshow's game price, prize cost and chance of losing.</summary>
	public void SetPrices( int instanceId, int price, int? costOfGoods = null, int? chanceOfLosingPercent = null )
	{
		var item = RequireObject( instanceId );
		if ( item.Kind is not (ParkObjectKind.Shop or ParkObjectKind.Sideshow) )
			throw new InvalidOperationException( "Only shops and sideshows have prices." );
		ArgumentOutOfRangeException.ThrowIfNegative( price );
		item.Price = price;
		if ( costOfGoods != null )
			item.CostOfGoods = Math.Max( 0, costOfGoods.Value );
		if ( chanceOfLosingPercent != null )
			item.ChanceOfLosingPercent = Math.Clamp( chanceOfLosingPercent.Value, 0, 100 );
	}

	/// <summary>
	/// The shop window's settings (<c>0x4b0aa0</c>): the amount of the special ingredient (<c>0x4e21e0</c> stores the word at <c>+0x198</c>) and the
	/// quality of goods (<c>0x4e2190</c> stores its low byte at <c>+0x18C</c>); null leaves a value as it is.
	/// </summary>
	// [BIN:TPW-EXE:0x4e2190 SetQualityOfGoods] ride +0x18c = the argument & 0xff; [BIN:TPW-EXE:0x4e21e0 SetAmountOfSpecialIngredient] ride +0x198 = the argument (no clamp)
	public void SetShopSettings( int instanceId, int? qualityOfGoods = null, int? ingredientAmount = null )
	{
		var item = RequireObject( instanceId );
		if ( item.Kind != ParkObjectKind.Shop )
			throw new InvalidOperationException( "Only shops have a quality of goods and an ingredient amount." );
		if ( qualityOfGoods != null )
			item.QualityOfGoods = qualityOfGoods.Value & 0xFF;
		if ( ingredientAmount != null )
			item.IngredientAmount = ingredientAmount.Value;
	}

	private ParkObjectState RequireObject( int instanceId ) =>
		objects.TryGetValue( instanceId, out var item ) ? item : throw new InvalidOperationException( $"No park object {instanceId}." );

	// ---------------------------------------------------------------- construction

	/// <summary>Why a purchase was refused, or <see cref="PurchaseResult.Ok"/>.</summary>
	public enum PurchaseResult
	{
		Ok,
		UnknownObject,
		NotBuyable,
		NotResearched,
		NotEnoughMoney,
		NotEnoughGoldenTickets,
		Bankrupt,
		NotAvailableInInstantAction,
		AlreadyFullyUpgraded,
		UpgradeInProgress,
		MissingTargetRide,
		/// <summary>The park runs no staff, and an upgrade is installed by a mechanic (Theme Park Inc's money slice).</summary>
		NeedsStaff,
		/// <summary>The park has no land for sale (Theme Park Inc has no Buy Land, though <c>Costs.MapCell</c> is in its data).</summary>
		NoLandForSale
	}

	/// <summary>
	/// Buys and registers an object. Requires research, <c>UsageInfo.GoldenTicketCost</c> golden tickets
	/// (spent) and, as an <b>approximation</b> of the original rule, a balance covering the cost.
	/// </summary>
	public PurchaseResult TryBuild( int infoId, out ParkObjectState? built, bool moving = false )
	{
		built = null;
		var check = CheckBuild( infoId, moving );
		if ( check != PurchaseResult.Ok )
			return check;
		Catalog.TryGet( infoId, out var info );
		var payWithTickets = PaysWithTickets( info );
		var cost = payWithTickets ? 0 : info.PurchaseCost;
		if ( payWithTickets )
		{
			// [BIN:TPW-EXE:0x004D4AD0 ticket purchase] the cost goes to the player's spent count (0x5AF740, +0x1C) when the check passed; the park keeps its own spent count and set as its history
			if ( TicketWallet is { Active: true } wallet && !wallet.TrySpend( infoId, info.GoldenTicketCost ) )
				return PurchaseResult.NotEnoughGoldenTickets;
			TicketsSpent += info.GoldenTicketCost;
			ticketItems.Add( infoId );
		}
		Post( LedgerCategory.OtherCosts, cost );
		built = AddObject( info, imported: false );
		built.TotalSpent = cost;
		Raise( ParkEventKind.ObjectBuilt, cost, built.Id, infoId, info.Name );
		return PurchaseResult.Ok;
	}

	/// <summary>
	/// The purchase rules of <see cref="TryBuild"/> without buying (the build tool's ghost uses it). The Move tool (<paramref name="moving"/>) places a type the park already holds, so it asks
	/// for neither the buy list's availability, research nor the balance test of the build tool's hover: the constructor 0x4db090 charges the price and nothing else, and the debit 0x4d01f0 does not test the balance.
	/// </summary>
	public PurchaseResult CheckBuild( int infoId, bool moving = false )
	{
		if ( IsBankrupt )
			return PurchaseResult.Bankrupt;
		if ( !Catalog.TryGet( infoId, out var info ) )
			return PurchaseResult.UnknownObject;
		if ( !moving && !info.IsBuyable )
			return PurchaseResult.NotBuyable;
		// [EXT:tpi-no-research] without research (refused for Theme Park Inc) every buyable object is on offer, as the experimental build list has always offered the whole catalogue
		if ( !moving && Rules.Research && !Research.IsAvailable( infoId ) )
			return PurchaseResult.NotResearched;
		if ( info.Kind == ParkObjectKind.Upgrade && !objects.Values.Any( item => item.InfoId == info.AddOnTargetId ) )
			return PurchaseResult.MissingTargetRide;
		var payWithTickets = PaysWithTickets( info );
		if ( payWithTickets && info.GoldenTicketCost > GoldenTicketsAvailable )
			return PurchaseResult.NotEnoughGoldenTickets;
		// [BIN:TPW-EXE:0x5234d0 purchase gate] a cash-priced object (GoldenTicketCost +0xc4 below 1, or already bought) is refused when its price (+0x1b8) is greater than the bank balance (+0xc), equal allowed (0x523658; the placement validator 0x535670 repeats it at 0x535927); the test is skipped only in layout replay (0x785a2c, set by 0x404140), and the debit 0x4d01f0 does not test the balance
		// [BIN:TPW-EXE:0x523940 move hover] the Move tool's hover (mode 0x3b) stores the price (0x52fb90) but has no balance compare, no cash flag and no golden-ticket test, and neither the click (0x5253d5) nor the debit does: a move is never refused for cash
		if ( !moving && !payWithTickets && info.PurchaseCost > Balance )
			return PurchaseResult.NotEnoughMoney;
		return PurchaseResult.Ok;
	}

	/// <summary>The golden tickets a buy list row of <paramref name="infoId"/> asks (the type's GoldenTicketCost) while the next copy is paid with tickets; null for a cash row.</summary>
	// [BIN:TPW-EXE:0x4ab034 buy list row] the row filler shows the ticket cost (type +0xc4, negated) for a type with a cost above 0 until 0x4d4b70 (the player's bought-with-tickets set, 0x5af780) holds it
	public int? ListedTicketCost( int infoId ) =>
		Catalog.TryGet( infoId, out var info ) && PaysWithTickets( info ) ? info.GoldenTicketCost : null;

	// [BIN:STP-PPC:0x100DA874 object purchase] an object with a GoldenTicketCost (+0xC4) above 0 that the park has not yet bought with tickets costs that many tickets (0x100D3000; allowed when cost <= earned - spent) and no cash; it joins the owned set, and later copies and ticket-free objects take the cash path
	private bool PaysWithTickets( EconomyObjectInfo info ) =>
		Settings.CanSpendTickets && info.GoldenTicketCost > 0 && !OwnsTicketItem( info.InfoId );

	/// <summary>
	/// The park's attraction points for the arrival rule: per ride or sideshow, its Info.AttractionValue plus the
	/// Attraction[n].NewBonus of its age bracket (n = age in days / Info.NewAttractionDecayTime, none from the fourth
	/// bracket on). A type without a track (Bumper.WhichTrackType 0) is aged from its first purchase and divided by its
	/// number of objects, so its copies count once; every copy of a tracked type (karts, water rides, coasters) is aged
	/// from its own build and counts in full.
	/// </summary>
	// [BIN:STP-PPC:0x100C24C0 park points] sum over placed things whose type kind (+0x7A8) is 0 or 2: (type +0x268[age / +0x5C] + type +0x58) / divisor, age in days (64-bit time / 864,000,000,000 at 0x101C4100), no bonus for a bracket outside 0..2
	// [BIN:STP-PPC:0x100C24C0 park points] executed by the PowerPC oracle (docs/reverse/ORACLE.md F10): a type whose +0x9C word (Bumper.WhichTrackType) is 0 is aged from its park record's +28 (0x100D1BE8; written by 0x100DA874 at a purchase while it is still 0, so the first purchase after turn 0) and divided by the record's +24 copy count (0x100C5828); any other type is aged from the thing's own purchase time (0x100DC520) and divided by 1
	// [BIN:STP-PPC:0x100DC834 counts-test] only things passing 0x100DC834 count: thing +0x2E bit 0x4 (the type's Info.IsChoosable, 0x100DABDC), ride state (+0x198) not BROKEN_DOWN (1, 0x100DFE0C) and not CONDEMNED (4, 0x100DFE20), the open word +0x64 nonzero (0x100DFF18; 1 at construction, cleared by closing and by breaking down), a connected entrance (0x100DD3F4 gives a cell), room in the queue (0x100DC988), a coaster without riders (0x100E2FE4, 0x10045EAC) and a built track for karts and water rides (+0x28)
	// [BIN:TPW-EXE:0x004C8240 park points] a thing counts only when its type's Info.WhichUIType (+0x4AC, copied from +0x4C at 0x004134F5) is 0 or 2, it passes the counts-test 0x004DD920 and its type has copies (0x004D3D80 +0x18 > 0)
	// [BIN:TPW-EXE:0x00441970 coaster economic test] the coaster test is CoasterRide.CountsForParkPoints (the native flags/intersection/edit predicate, without the host admission sample guard); a kart or water ride's track word is BumpController.IsTrackValid, which starts at 1 as the constructor's does (0x004DB0DF)
	public int ParkPoints()
	{
		var counts = new Dictionary<int, int>();
		foreach ( var item in objects.Values )
			counts[item.InfoId] = counts.GetValueOrDefault( item.InfoId ) + 1;
		var points = 0;
		foreach ( var item in objects.Values )
		{
			if ( !Catalog.TryGet( item.InfoId, out var info ) || info.RatingType is not (0 or 2) || !CountsForParkPoints( item, info ) )
				continue;
			var tracked = info.TrackType != 0;
			var since = tracked ? ParkCalendar.Turn( item.BuiltTick ) : Counters[ParkCounters.FirstPurchaseTurn( item.InfoId )];
			var ageDays = Math.Max( 0, Turn - since ) * ParkCalendar.SecondsPerTurn / 86400;
			var bracket = info.NewAttractionDecayTime > 0 ? ageDays / info.NewAttractionDecayTime : 0;
			var bonus = info.NewBonus is { } bonuses && bracket < Math.Min( 3, bonuses.Count ) ? bonuses[(int)bracket] : 0;
			points += (bonus + info.AttractionValue) / (tracked ? 1 : counts[item.InfoId]);
		}
		return points;
	}

	/// <summary>
	/// Whole park days since the object was built (the original's own age, <c>0xdc520</c>: the clock's timestamp minus the one stored at the thing's purchase,
	/// divided by one day in 100 ns units). The attraction score treats a ride up to <c>PeepInfo.DecisionVariable1</c> days old as new.
	/// </summary>
	// [BIN:STP-PPC:0x100DC520 own age] TbTimeStamp::operator- of the clock (+672) and thing +20, over 864,000,000,000; the park points stub of the oracle uses the same day scale (ORACLE.md F10)
	public uint ObjectAgeDays( ParkObjectState item ) => (uint)(Math.Max( 0, Turn - ParkCalendar.Turn( item.BuiltTick ) ) * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay);

	/// <summary>
	/// The original's park points counts-test (tp.exe 0x004DD920): the type is choosable, the object is neither broken down (in the
	/// economy or by its script) nor closed, its queue has cells from the entrance and room in them, a kart or water ride has a valid
	/// track and a coaster's controller accepts guests. The level mirrors the live conditions each tick.
	/// </summary>
	// [BIN:TPW-EXE:0x004DD920 counts-test] the same tests as 0x100DC834 at the PC offsets (flags +0x32 bit 4, state +0x19C not 1 or 4, open word +0x68); the back of the queue 0x004DE130 walks the queue from the front cell at the entrance and counts its cells (+0x40), so an attraction without queue cells has none and does not count, and 0x004DDF50 called with guest 0 returns the whole list's length, compared with 4 × the cells
	// [BIN:TPW-EXE:0x004DD9AD counts-test track types] Bumper.WhichTrackType (type +0x9C) 3 needs the coaster test 0x00441970 (cdecl, the thing), 1 and 2 need the track word +0x2C, any other type passes
	public static bool CountsForParkPoints( ParkObjectState item, EconomyObjectInfo info ) => info.IsChoosable && !item.IsBrokenDown && item.IsOpen && item.QueueHasRoom
		&& (info.TrackType is not (1 or 2) || item.TrackValid) && (info.TrackType != 3 || item.CoasterAcceptsGuests);

	/// <summary>Registers an object that already exists (imported original save, fixed item) without charging.</summary>
	/// <param name="builtTick">When the object was built, for an object from an original save (default: now).</param>
	/// <param name="timesUsed">Guests that used it before, for an object from an original save: a used object never refunds the unused 100 %.</param>
	public ParkObjectState RegisterExisting( int infoId, long? builtTick = null, long timesUsed = 0 )
	{
		if ( !Catalog.TryGet( infoId, out var info ) )
			throw new InvalidDataException( $"Info.Id {infoId} is not in the {Settings.Theme} object catalogue." );
		return AddObject( info, imported: true, builtTick, timesUsed );
	}

	/// <summary>
	/// A fixed item the game made while the park is played (an arrival vehicle at its first trip, the research finale's End): registered uncharged, as
	/// <see cref="RegisterExisting"/> registers it, when the catalogue knows its type, else not recorded (null). <see cref="ParkEventKind.FixedItemMade"/> is raised
	/// either way, as <see cref="TryBuild"/> raises <see cref="ParkEventKind.ObjectBuilt"/>: the object was constructed. The event adds no state.
	/// </summary>
	// [BIN:TPW-EXE:0x004DB7A3 object stamp] the common object constructor ends every construction, a fixed item made in play included, by stamping the park's mLastObjectBuilt (0x4C7680), which only the advisor reads (0x4C9070): the event carries that moment to the advisor's own record
	public ParkObjectState? RegisterFixedItemMade( int infoId )
	{
		var known = Catalog.TryGet( infoId, out var info );
		var made = known ? AddObject( info, imported: true ) : null;
		Raise( ParkEventKind.FixedItemMade, 0, made?.Id ?? 0, infoId, known ? info.Name : "" );
		return made;
	}

	private ParkObjectState AddObject( EconomyObjectInfo info, bool imported, long? builtTick = null, long timesUsed = 0 )
	{
		var item = new ParkObjectState
		{
			Id = NextObjectId++,
			InfoId = info.InfoId,
			Kind = info.Kind,
			BuiltTick = Math.Clamp( builtTick ?? Tick, 0, Tick ),
			Imported = imported,
			TimesUsed = timesUsed,
			Price = info.PricePerUse ?? (Rules.ThemeParkInc && info.Kind == ParkObjectKind.Ride ? info.RidePricePerUse : 0),
			CostOfGoods = info.CostOfGoods ?? 0,
			ChanceOfLosingPercent = info.ChanceOfLosingPercent ?? 0
		};
		// [BIN:TPW-EXE:0x004DB4FB object state] the constructor stores 0 for a choosable type and 3 for any other (SetState at 0x4DB508)
		item.StoredState = info.IsChoosable ? RideObjectStates.Idle : RideObjectStates.NotAnAttraction;
		objects.Add( item.Id, item );
		HookStateWord( item );
		Counters.Add( ParkCounters.Built( info.InfoId ), 1 );
		// [BIN:STP-PPC:0x100DA874 object purchase] the type's park record +28 takes the current turn while it is still 0 (0x100DADEC..0x100DAE14); selling does not clear it
		// [BIN:TPW-EXE:0x004DB690 first purchase] the constructor stamps the game turn into the type's record +0x1C only while it is 0; at a new park the gates and traffic lights are built after the turn is set to 0 (0x00515865), so fixed items stamp 0 as here
		// [BIN:TPW-EXE:0x004D3AA0 object controls] an object imported from an original save finds its type's first-purchase turn restored from the save's object-control block (OriginalEconomyImport.ImportFirstPurchases), and is stamped with the import turn only while the type is still at 0, which is the original's rule
		if ( Counters[ParkCounters.FirstPurchaseTurn( info.InfoId )] == 0 )
			Counters.Max( ParkCounters.FirstPurchaseTurn( info.InfoId ), Turn );
		return item;
	}

	/// <summary>Ends the jobs on a removed object: a mechanic's timed job, and a world handyman's walk to the toilet or its clean (he searches again).</summary>
	private void ReleaseJobsOn( int instanceId )
	{
		StaffWorld?.ObjectRemoved( this, instanceId );
		foreach ( var member in Staff.Members )
		{
			if ( member.AssignedInstanceId == instanceId )
			{
				member.AssignedInstanceId = 0;
				member.BusyTicks = 0;
				member.AwaitingJobArrival = false;
				member.State = StaffState.Patrolling;
			}
			if ( member.RideId == instanceId )
			{
				// the ride went: the mechanic's walk to it or his work on it ends and he searches again
				member.RideId = 0;
				if ( member.Job is StaffJob.ToRide or StaffJob.Repairing )
				{
					member.Job = StaffJob.Idle;
					member.IdleStamp = 0;
					member.State = StaffState.Idle;
				}
			}
			if ( member.RestAreaId == instanceId )
			{
				// the rest area went: a walk to it or a rest in it ends (a world's members heard the removal message first, StaffWorldSimulation.ObjectRemoved)
				member.RestAreaId = 0;
				if ( member.Job is StaffJob.ToRestArea or StaffJob.Resting )
				{
					member.Job = StaffJob.Idle;
					member.IdleStamp = 0;
					member.State = StaffState.Idle;
				}
			}
			if ( member.ToiletId == instanceId )
			{
				member.ToiletId = 0;
				if ( member.Job is StaffJob.ToToilet or StaffJob.Cleaning )
				{
					member.Job = StaffJob.Idle;
					member.IdleStamp = 0;
					member.SpeedBonus = 0;
					member.State = StaffState.Idle;
				}
			}
		}
	}

	/// <summary>Removes an object and credits its <see cref="ScrapValue"/>.</summary>
	public long Sell( int instanceId )
	{
		var item = RequireObject( instanceId );
		var value = ScrapValue( item );
		RemoveObjectEffects( item );
		objects.Remove( instanceId );
		ReleaseJobsOn( instanceId );
		Post( LedgerCategory.OtherIncome, value );
		Raise( ParkEventKind.ObjectSold, value, instanceId, item.InfoId );
		return value;
	}

	/// <summary>Removes an object without scrap credit (e.g. an unpaid prototype placement).</summary>
	public void Remove( int instanceId )
	{
		var item = RequireObject( instanceId );
		RemoveObjectEffects( item );
		objects.Remove( instanceId );
		ReleaseJobsOn( instanceId );
		Raise( ParkEventKind.ObjectSold, 0, instanceId, item.InfoId, "removed" );
	}

	/// <summary>Requests the next upgrade level of a ride (UITEXT 367–372); the mechanic who installs it debits its cost (<see cref="FinishJob"/>).</summary>
	public PurchaseResult TryBuyUpgrade( int instanceId )
	{
		if ( IsBankrupt )
			return PurchaseResult.Bankrupt;
		// the checks in their order, an absent object asked for only past the game type's (UpgradePurchaseRefusal)
		if ( !Features.Upgrades )
			return Rules.Staff ? PurchaseResult.NotAvailableInInstantAction : PurchaseResult.NeedsStaff;
		var item = RequireObject( instanceId );
		var result = UpgradePurchaseRefusal( item );
		if ( result != PurchaseResult.Ok )
			return result;
		var level = item.Level + 1;
		Catalog.TryGet( item.InfoId, out var info );
		var cost = info.Upgrades[level].CostOfUpgrade;
		item.PendingLevel = level;
		item.StoredState = RideObjectStates.Upgrading;
		item.UpgradeCostDue = true;
		// [EXT:online-coop-commands] the purchase's identity, which a guest's cancellation names (C4b-2)
		item.UpgradePurchases++;
		Raise( ParkEventKind.UpgradeBought, cost, item.Id, item.InfoId, $"level {level}" );
		return PurchaseResult.Ok;
	}

	/// <summary>
	/// What <see cref="TryBuyUpgrade"/> would answer for <paramref name="item"/> now, without buying (null: the park is bankrupt): its checks alone, so [EXT:online-coop-commands] a guest's
	/// window projects a waiting buy only when its record would take it (C4b-2).
	/// </summary>
	internal PurchaseResult UpgradePurchaseRefusal( ParkObjectState? item )
	{
		if ( IsBankrupt || item == null )
			return PurchaseResult.Bankrupt;
		// [BIN:STP-PPC:0x10165A0C upgrade list] game type 2 (Instant Action) lists no upgrades and shows UITEXT 27 instead
		if ( !Features.Upgrades )
			return Rules.Staff ? PurchaseResult.NotAvailableInInstantAction : PurchaseResult.NeedsStaff;
		if ( !Catalog.TryGet( item.InfoId, out var info ) || item.Kind != ParkObjectKind.Ride )
			return PurchaseResult.NotBuyable;
		if ( item.PendingLevel > item.Level )
			return PurchaseResult.UpgradeInProgress;
		var level = item.Level + 1;
		if ( level >= info.Upgrades.Count )
			return PurchaseResult.AlreadyFullyUpgraded;
		if ( !Research.IsAvailable( item.InfoId, level ) )
			return PurchaseResult.NotResearched;
		// [BIN:STP-PPC:0x10166F1C upgrade purchase] only the bank balance is checked; the upgrade is queued (0x100DF928) and waits for a mechanic, with or without mechanics on the staff
		// [BIN:TPW-EXE:0x004AF4E9 upgrade purchase] the buy handler compares the signed balance (bank +0xC) with Upgrades[level].CostOfUpgrade (type record +0x1B8 + 0x40 · level) and requests the upgrade (0x004E0050(1): state 2, a mechanic called) only when the balance is greater; nothing is debited here
		return Balance <= info.Upgrades[level].CostOfUpgrade ? PurchaseResult.NotEnoughMoney : PurchaseResult.Ok;
	}

	/// <summary>
	/// Cancels the upgrade a ride waits for (the upgrade list's "Cancel level" row): a ride in the upgrading state, and not broken down, waits for no level any more, owes no
	/// upgrade cost and opens (<see cref="ParkEventKind.UpgradeCancelled"/>). A mechanic on his way finds nothing to do when he arrives. False when there is nothing to cancel.
	/// </summary>
	// [BIN:TPW-EXE:0x004E0050 upgrade cancel] with argument 0 a ride in state 2 logs "Cancelling request", drops the ride from the job list (0x004547C0), skips its open guard (state 2 is one of the refused states, so it only logs "Opening non-openable ride" five times), writes the open word +0x68 = 1 and script variable 6 (VAR_RIDECLOSED) = 0 and returns to state 0 (0x004E0E60(0)); in any other state it logs "Never requested upgrade" and does nothing
	public bool TryCancelUpgrade( int instanceId )
	{
		if ( !objects.TryGetValue( instanceId, out var item ) || ObjectState( item ) != RideObjectStates.Upgrading )
			return false;
		item.PendingLevel = 0;
		// [BIN:TPW-EXE:0x004E01B6 upgrade cancelled] the cancel ends in SetState(0): the stored 2 goes and a breakdown it had overwritten is the ride update's to find again
		item.StoredState = RideObjectStates.Idle;
		item.IsBrokenDown = false;
		item.UpgradeCostDue = false;
		Raise( ParkEventKind.UpgradeCancelled, 0, item.Id, item.InfoId );
		return true;
	}

	/// <summary>
	/// [EXT:pools-cost] The price of one pool water cell: 75, the figure of Roller Coaster Tycoon 3 Soaked!'s pool tiles. The original's balance files have no pool price, so it is a
	/// constant of the extension rather than a <c>Costs.*</c> value (docs/POOLS.md).
	/// </summary>
	// [EXT:pools-cost] OpenTPW's own price; the original game has no pools
	public const int PoolCellCost = 75;

	/// <summary>
	/// [EXT:pools-cost] The price of one piece of a water slide: 90. The original's balance files have no slide price, so it is a constant of the extension (docs/POOLS.md, "Water slides");
	/// a slide of ten pieces costs 900 on top of its tower, the price of a modest ride. Riding is free.
	/// </summary>
	// [EXT:pools-cost] OpenTPW's own price; the original game has no pools
	public const int SlidePieceCost = 90;

	public long CellCost( CellPurchase kind ) => kind switch
	{
		CellPurchase.Path => Settings.Costs.PathCell,
		CellPurchase.Queue => Settings.Costs.QueueCell,
		CellPurchase.KartTrack => Settings.Costs.KartTrackCell,
		CellPurchase.WaterTrack => Settings.Costs.WaterTrackCell,
		CellPurchase.Land => Settings.Costs.MapCell,
		CellPurchase.Pool => PoolCellCost,
		CellPurchase.SlidePiece => SlidePieceCost,
		_ => throw new ArgumentOutOfRangeException( nameof( kind ) )
	};

	/// <summary>Charges <c>Costs.*</c> for <paramref name="count"/> cells of paths, queues, tracks or land.</summary>
	public PurchaseResult TryBuyCells( CellPurchase kind, int count )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( count );
		if ( IsBankrupt )
			return PurchaseResult.Bankrupt;
		if ( kind == CellPurchase.Land && Rules.ThemeParkInc )
			return PurchaseResult.NoLandForSale;
		var cost = CellCost( kind ) * count;
		if ( cost > Balance )
			return PurchaseResult.NotEnoughMoney;
		Post( LedgerCategory.OtherCosts, cost );
		Raise( ParkEventKind.CellsBought, cost, 0, (int)kind, $"{count} {kind} cells" );
		return PurchaseResult.Ok;
	}

	/// <summary>
	/// Charges a Buy Land purchase: <paramref name="cost"/> is the validator's total (<see cref="ParkLand.Price"/>, whose money
	/// test already ran), debited without another test as other costs; <paramref name="cells"/> only names the purchase.
	/// </summary>
	// [BIN:TPW-EXE:0x525d5a land debit] after the purchase (0x5327e0) the click handler debits the validator's total 0x81af4c (0x52fbc0) through the bank debit 0x4d01f0, which adds it to the money-out word +0x1f5a0 of the object 0x519510 returns and tests no balance; the ledger books it as other costs, as the other cell purchases
	public void ChargeLand( long cost, int cells )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( cells );
		// [BIN:TPI-EXE:0x004289AC cost setter] Costs.MapCell is in Theme Park Inc's data and set (equal to tp.exe 0x00408192) but no reader of it was found: there is no Buy Land (moderate confidence; the park boundary is the mission's BoundaryClicks)
		if ( Rules.ThemeParkInc )
			throw new InvalidOperationException( "Theme Park Inc has no Buy Land." );
		// Costs.MapCell cannot be negative (the balance load refuses it, 0x00401D7C), so the total is never income.
		Post( LedgerCategory.OtherCosts, cost );
		Raise( ParkEventKind.CellsBought, cost, 0, (int)CellPurchase.Land, $"{cells} {CellPurchase.Land} cells" );
	}

	/// <summary>
	/// The commit's money test (<c>SetCellType</c>, PATH-plan §3.2): whether <see cref="TrySpendCell"/> accepts one
	/// cell, balance − cost ≥ 0 as a wrapping 32-bit difference of the original's signed balance word.
	/// </summary>
	// [BIN:STP-PPC:0x10082BC8 SetCellType money test] subf balance − cost in 32 bits, then the sign bit: balance −2^31 with cost 1 wraps to affordable, balance 2^31 − 1 with cost −1 to refused
	public bool CanSpendCell( CellPurchase kind ) => Affords( Balance, CellCost( kind ) );

	/// <summary>The commit's money test of <see cref="CanSpendCell"/> on a given <paramref name="balance"/>: balance − cost ≥ 0 in 32 bits ([EXT:online-coop-undo] a take-back's prediction pays with its own copy of the balance, C5-2).</summary>
	internal static bool Affords( long balance, long cost ) => unchecked( (int)balance - (int)cost ) >= 0;

	/// <summary>
	/// The preview's money test (the placement validator, PATH-plan §3.3): <paramref name="pendingLineCost"/> is the
	/// line's cost so far with this cell included, summed in 32 bits. A zero sum passes; otherwise the line is refused
	/// when the sum is greater than the balance (a signed compare, no subtraction). It differs from the commit's
	/// <see cref="CanSpendCell"/> only at the 32-bit edges and for a zero sum.
	/// </summary>
	// [BIN:STP-PPC:0x1007BCC0 pending line cost] add (no overflow trap) of the cell price; [BIN:STP-PPC:0x10084534 placement validator] a zero pending cost skips the test; [BIN:STP-PPC:0x10084550 placement validator] cmpw pending, balance and refuse when greater
	public bool CanPreviewLine( long pendingLineCost ) => PreviewsLine( pendingLineCost, Balance );

	/// <summary>The preview's money test of <see cref="CanPreviewLine"/> on a given <paramref name="balance"/> ([EXT:online-coop-undo] a take-back's prediction pays with its own copy, C5-2).</summary>
	internal static bool PreviewsLine( long pendingLineCost, long balance )
	{
		var pending = unchecked( (int)pendingLineCost );
		return pending == 0 || pending <= unchecked( (int)balance );
	}

	/// <summary><see cref="CanPreviewLine"/> for <paramref name="cells"/> cells of one kind (this cell included).</summary>
	public bool CanPreviewCells( CellPurchase kind, int cells ) => CanPreviewLine( unchecked( cells * CellCost( kind ) ) );

	/// <summary>
	/// Charges one path or queue cell when it is written (<c>SetCellType</c>, PATH-plan §3.2): refused only
	/// when balance − cost &lt; 0 (no bankruptcy test), then spent as other costs. A negative <c>Costs.*</c> is refused
	/// when the balance file loads (tp.exe 0x00401D7C), so the cost is never negative.
	/// </summary>
	// [BIN:STP-PPC:0x10082AC4 SetCellType] type 1 costs Costs.PathCell, type 3 Costs.QueueCell; refused when balance − cost < 0, else Spend per cell at write time
	public PurchaseResult TrySpendCell( CellPurchase kind )
	{
		if ( !CanSpendCell( kind ) )
			return PurchaseResult.NotEnoughMoney;
		var cost = CellCost( kind );
		// [BIN:TPW-EXE:0x00401D7C negative cost refused at load] a negative Costs.* value fails the balance load (BalanceSettings), so a cell cost here is never negative and is always spent
		Post( LedgerCategory.OtherCosts, cost );
		Raise( ParkEventKind.CellsBought, cost, 0, (int)kind, $"1 {kind} cell" );
		return PurchaseResult.Ok;
	}

	/// <summary>
	/// Charges coaster track money (a placed pylon, or the charge side of a move) as other costs, adding it to the
	/// station's total spent. Without <paramref name="force"/> it is refused when bankrupt or when the balance cannot
	/// cover it (the build tool's affordability check); a forced charge always debits, as the original's drop does.
	/// </summary>
	public PurchaseResult TrySpendCoasterTrack( long cost, int? stationInstanceId = null, bool force = false )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( cost );
		if ( !force && IsBankrupt )
			return PurchaseResult.Bankrupt;
		if ( !force && cost > Balance )
			return PurchaseResult.NotEnoughMoney;
		Post( LedgerCategory.OtherCosts, cost );
		if ( stationInstanceId is int instance && objects.TryGetValue( instance, out var station ) )
			station.TotalSpent += cost;
		Raise( ParkEventKind.CoasterTrackBuilt, cost, stationInstanceId ?? 0, 0, "coaster track" );
		return PurchaseResult.Ok;
	}

	/// <summary>Credits the refund side of a coaster pylon move or height change as other income.</summary>
	public void CreditCoasterTrack( long amount, int? stationInstanceId = null )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( amount );
		Post( LedgerCategory.OtherIncome, amount );
		if ( stationInstanceId is int instance && objects.TryGetValue( instance, out var station ) )
			station.TotalSpent = Math.Max( 0, station.TotalSpent - amount );
	}

	/// <summary>
	/// [EXT:pools-cost] Refunds an erased pool water cell: the full <see cref="PoolCellCost"/>, as other income, or nothing while the bank's withdrawals are disabled (a debit then charges
	/// nothing, <see cref="WithdrawalsEnabled"/>, so there is nothing to give back and paint and erase cannot make money). A path cell refunds nothing in the original (ClearCell's path
	/// case), so this is a deliberate difference of the extension: a pool painted by mistake costs nothing once it is erased again (docs/POOLS.md). Returns the amount credited.
	/// </summary>
	public long RefundPoolCell()
	{
		if ( !WithdrawalsEnabled )
			return 0;
		Post( LedgerCategory.OtherIncome, PoolCellCost );
		return PoolCellCost;
	}

	/// <summary>[EXT:pools-bridges] Charges one bridge without altering original cell prices.</summary>
	public PurchaseResult TrySpendPoolBridge()
	{
		if ( Balance < PoolBridgeWorld.Price ) return PurchaseResult.NotEnoughMoney;
		Post( LedgerCategory.OtherCosts, PoolBridgeWorld.Price );
		return PurchaseResult.Ok;
	}

	public long RefundPoolBridge()
	{
		if ( !WithdrawalsEnabled ) return 0;
		Post( LedgerCategory.OtherIncome, PoolBridgeWorld.Price );
		return PoolBridgeWorld.Price;
	}

	/// <summary>[EXT:pools-cost] Refunds one removed slide piece in full, as an erased pool cell is (nothing while withdrawals are disabled). Returns the amount credited.</summary>
	public long RefundSlidePiece()
	{
		if ( !WithdrawalsEnabled )
			return 0;
		Post( LedgerCategory.OtherIncome, SlidePieceCost );
		return SlidePieceCost;
	}

	/// <summary>
	/// Refunds a removed queue cell: <c>Costs.QueueCell</c> × the ride's scrap percentage / 100
	/// (<c>ClearCell</c> queue case, PATH-plan §6). Returns the amount credited.
	/// </summary>
	// [BIN:STP-PPC:0x10085DCC ClearCell queue refund] Earn(QueueCost × 0xe2424(ride) / 100), 0xe2424 = the ride's age-based scrap percentage
	internal long RefundQueueCellNative( int rideInstanceId )
	{
		var amount = TryGetObject( rideInstanceId, out var ride )
			? unchecked((uint)((int)CellCost( CellPurchase.Queue ) * ScrapPercent( ride! ))) / 100U : 0U;
		if ( amount > 0 ) Post( LedgerCategory.OtherIncome, amount );
		return amount;
	}

	public long RefundQueueCell( int rideInstanceId )
	{
		var amount = TryGetObject( rideInstanceId, out var ride ) ? CellCost( CellPurchase.Queue ) * ScrapPercent( ride! ) / 100 : 0;
		if ( amount > 0 )
			Post( LedgerCategory.OtherIncome, amount );
		return amount;
	}

	/// <summary>
	/// The one charge the demolition of a ride with queue cells makes after its queue is cleared: the queue cell cost × the ride's scrap percentage / 100 (a signed
	/// 32-bit division), debited without a balance test, so the cells refund one cell less than they were laid for. Returns the amount debited.
	/// </summary>
	// [BIN:TPW-EXE:0x00527FDC demolition queue charge] after the undo loop (0x527FBE..0x527FDA) 0x527EE0 takes the queue cell cost (0x52FB40) times the scrap percentage (0x4E2290), divides by 100 signed (imul by 0x51EB851F, sar 5 and the sign correction, 0x527FE3..0x527FFD) and debits it through 0x4D01F0 (0x528007); the cells themselves were credited one by one by the forced clears (ClearCell 0x5367A0's queue case, 0x4D0190), so a queue refunds one cell less than it holds
	public long ChargeQueueDemolition( int rideInstanceId )
	{
		if ( !TryGetObject( rideInstanceId, out var ride ) )
			return 0;
		var amount = unchecked( (int)CellCost( CellPurchase.Queue ) * ScrapPercent( ride! ) ) / 100;
		if ( amount > 0 )
			Post( LedgerCategory.OtherCosts, amount );
		return amount;
	}

	/// <summary>
	/// Refunds one cleared kart or water track tile of a ride being demolished: the track cell cost × the ride's scrap percentage / 100, an unsigned 32-bit
	/// division as the queue cell refund. Returns the amount credited.
	/// </summary>
	// [BIN:TPW-EXE:0x0053B5FE track tile refund] ClearCell takes the cell cost of the tile type (0x52FB60 for a type 11 kart tile, 0x52FB80 for a type 16 water tile, 0x53B299) and, when the tile is cleared, credits (0x4D0190 at 0x53B622) the ride's scrap percentage (0x4E2290) × that cost / 100 (imul and unsigned mul by 0x51EB851F at 0x53B60A..0x53B617); the station tiles (types 13 and 18) refund nothing
	public long RefundTrackTile( CellPurchase kind, int rideInstanceId )
	{
		if ( !TryGetObject( rideInstanceId, out var ride ) )
			return 0;
		var amount = unchecked( (uint)((int)CellCost( kind ) * ScrapPercent( ride! )) ) / 100;
		if ( amount > 0 )
			Post( LedgerCategory.OtherIncome, amount );
		return amount;
	}

	/// <summary>
	/// The one charge a kart or water ride's demolition makes after its tiles are cleared: twice the track cell cost × the ride's scrap percentage / 100, a signed
	/// 32-bit division, debited without a balance test. Returns the amount debited.
	/// </summary>
	// [BIN:TPW-EXE:0x00528157 demolition charge] after the replays of the track tool from both station sides (0x81B46C = 1), 0x527EE0 takes twice the cell cost (0x52FB60 or 0x52FB80 by the type record +0x9C, 0x5280F0..0x52814A), multiplies by the scrap percentage (0x4E2290) and divides by 100 signed (imul by 0x51EB851F, sar 5 and the sign correction), and debits it through 0x4D01F0 (0x528179)
	public long ChargeTrackDemolition( CellPurchase kind, int rideInstanceId )
	{
		if ( !TryGetObject( rideInstanceId, out var ride ) )
			return 0;
		var amount = unchecked( ((int)CellCost( kind ) << 1) * ScrapPercent( ride! ) ) / 100;
		if ( amount > 0 )
			Post( LedgerCategory.OtherCosts, amount );
		return amount;
	}

	// ---------------------------------------------------------------- loans

	public LoanAccount TakeLoan( int offerIndex )
	{
		// [BIN:TPW-EXE:0x004D0750 take loan] taking a loan needs only its bought flag (+0x28) clear ("Applying for loan already bought"): the stored test only decides what the list shows
		var offer = (Features.Loans ? Settings.Loans.FirstOrDefault( item => item.Index == offerIndex && !takenOffers.Contains( item.Index ) ) : null)
			?? throw new InvalidOperationException( $"Loan offer {offerIndex} is not available." );
		if ( IsBankrupt )
			throw new InvalidOperationException( "A bankrupt park cannot borrow." );
		var account = LoanMath.Open( offer );
		takenOffers.Add( offer.Index );
		loans.Add( account );
		Post( LedgerCategory.LoanReceived, offer.Amount );
		Raise( ParkEventKind.LoanTaken, offer.Amount, 0, offer.Index );
		return account;
	}

	/// <summary>Repays a loan early: M × the months left (0xccc78); requires the money (UITEXT 182).</summary>
	public bool TryRepayLoan( int offerIndex )
	{
		var account = loans.FirstOrDefault( item => item.OfferIndex == offerIndex ) ?? throw new InvalidOperationException( $"Loan {offerIndex} is not outstanding." );
		// [BIN:TPW-EXE:0x4d0850 loan payoff] "Cannot afford to pay off loan" when the balance is below M × the months left as unsigned words, so a balance below 0 never refuses
		if ( unchecked( (uint)(int)Balance < (uint)(int)account.RemainingBalance ) )
			return false;
		Post( LedgerCategory.LoanPayments, account.RemainingBalance );
		// [BIN:STP-PPC:0x100CCC78 loan payoff] months repaid are cleared before they are read again, so the profit is charged the share of the whole term (q x N), even after a partial repayment (ORACLE F13)
		// [BIN:TPW-EXE:0x4d0850 loan payoff] the debit is skipped with mWithdrawalsEnabled clear, and the profit still gains the payoff less that share
		var interest = LoanMath.PayoffInterest( account.MonthlyRepayment, account.Months, account.OriginalAmount );
		Ledger.ChargeLoanInterest( WithdrawalsEnabled ? interest : interest - account.RemainingBalance );
		loans.Remove( account );
		takenOffers.Remove( offerIndex );
		Raise( ParkEventKind.LoanRepaid, account.RemainingBalance, 0, offerIndex );
		return true;
	}

	// ---------------------------------------------------------------- staff and research

	/// <summary>Refuses a staff, research or challenge operation in a park that runs none (<see cref="EconomyRules"/>).</summary>
	private void RequireSystem( bool runs, string system )
	{
		if ( !runs )
			throw new InvalidOperationException( $"This park runs no {system}: they are refused for Theme Park Inc (docs/THEME-PARK-INC.md)." );
	}

	public StaffMember Hire( int candidateId )
	{
		RequireSystem( Rules.Staff, "staff" );
		if ( IsBankrupt )
			throw new InvalidOperationException( "A bankrupt park cannot hire staff." );
		var member = Staff.Hire( candidateId, Tick, World );
		member.State = StaffState.Patrolling;
		Raise( ParkEventKind.StaffHired, Staff.MonthlyWage( member ), member.Id, (int)member.Type );
		return member;
	}

	/// <summary>
	/// Dismisses an employee. The original charges one more monthly wage for it (ORACLE.md F28): hiring costs nothing, dismissing costs the
	/// wage the employee would have been paid at the month's end.
	/// </summary>
	// [BIN:STP-PPC:0x100F33EC dismissal] the dismissal routine (the UI's dismiss button, UI-MAP) debits the wage 0xf46bc with Spend 0xcbfdc, as the monthly handler 0xf3364 does, and adds it to the staff wages statistic +0x1F7F0; the bank has no affordability check
	public void Fire( int staffId )
	{
		RequireSystem( Rules.Staff, "staff" );
		var member = Staff.Fire( staffId );
		member.AwaitingJobArrival = false;
		if ( objects.TryGetValue( member.AssignedInstanceId, out var item ) && item.MechanicId == member.Id )
			item.MechanicId = 0;
		// A dismissed handyman's litter and toilet go back to the others (the original's thing is gone, and its id no longer matches a claim).
		foreach ( var (x, y) in Litter.Littered().Where( cell => Litter.CollectorAt( cell.X, cell.Y ) == member.Id ).ToList() )
			Litter.Claim( x, y, 0, 0 );
		member.LitterCell = null;
		if ( objects.TryGetValue( member.ToiletId, out var toilet ) && toilet.MechanicId == member.Id )
			toilet.MechanicId = 0;
		member.ToiletId = 0;
		// a dismissed mechanic's ride goes back to the others
		if ( objects.TryGetValue( member.RideId, out var ride ) && ride.MechanicId == member.Id )
			ride.MechanicId = 0;
		member.RideId = 0;
		// the destructor takes the member's RegionFX row off its cell (0x004D4620 for an entertainer, through 0x004D8460)
		MoveStaffEffect( member, null );
		member.TargetGuestId = 0;
		var wage = Staff.MonthlyWage( member );
		if ( wage > 0 )
			Post( LedgerCategory.StaffCosts, wage );
		Raise( ParkEventKind.StaffFired, wage, member.Id, (int)member.Type );
	}

	public void SetTrainingBudget( StaffType type, long monthlyBudget )
	{
		RequireSystem( Rules.Staff, "staff" );
		Staff.SetTrainingBudget( type, monthlyBudget );
	}

	/// <summary>Research lab effort; refused in Instant Action, whose research panel does not open (UITEXT 467).</summary>
	public void SetResearchEffort( ResearchCategory category, int effort )
	{
		RequireSystem( Rules.Research, "research" );
		if ( !Features.ResearchPanel )
			throw new InvalidOperationException( "Research is automatic in Instant Action: the research lab cannot be changed." );
		Research.SetEffort( category, effort );
	}

	public void AcceptChallenge()
	{
		RequireSystem( Rules.Challenges, "challenges" );
		var definition = Objectives.CurrentDefinition ?? throw new InvalidOperationException( "No challenge is offered." );
		Objectives.AcceptAt( ParkCalendar.Seconds( Tick ), MeasureChallenge( definition ) );
		Raise( ParkEventKind.ChallengeAccepted, definition.Prize, 0, definition.Index );
	}

	public void DeclineChallenge()
	{
		RequireSystem( Rules.Challenges, "challenges" );
		var definition = Objectives.CurrentDefinition;
		var index = Objectives.DeclineAt( ParkCalendar.Seconds( Tick ) );
		// [BIN:TPW-EXE:0x4d2ec0 challenge declined] a declined challenge whose mIndependent (+0x1d) is clear, a follow-up, credits half its prize (+0x14 / 2,
		// truncated toward zero, 0x4d2f1d..0x4d2f33) through the bank credit 0x4d0190; an independent one pays nothing
		var half = definition is { Independent: false } ? definition.Prize / 2 : 0;
		if ( half != 0 )
			Post( LedgerCategory.OtherIncome, half );
		Raise( ParkEventKind.ChallengeDeclined, half, 0, index );
	}

	// ---------------------------------------------------------------- guests

	/// <summary>
	/// The booth books the entrance fee with the bank (tp.exe <c>0x004D0600</c>) and never reads the guest's purse: no open test, which the
	/// guests' walk to the booth makes instead (<see cref="GuestSimulation"/>, states 2 and 3), and no purse test. False only when the park is
	/// bankrupt: there the original's thing loop has stopped, so no guest reaches the booth at all (ECON-030); the level stops its guests in world
	/// state 4 as well (Level.Simulation), and the refusal also holds for an economy run without a level.
	/// </summary>
	// [BIN:TPW-EXE:0x004D0600 admission] the booth's paying verdict calls the bank deposit with the bank thing (0x004FFAE5): the fee (bank +0x118) goes to the balance, money in (+0x1FC90), the year's takings (+0x124) and the gate row (+0x1FEE0), and 0x004C7520 counts the admission; it reads no park-closed state and nothing reads the guest's purse
	// [BIN:TPW-EXE:0x00516380 park turn walk] the per-turn thing loop is skipped while the world state +0x1DA738 is 4, which the bank's bankruptcy message sets (0x005168F0, ECON-030): no booth guest pays after bankruptcy
	public bool TryAdmitVisitor( out int feePaid )
	{
		feePaid = 0;
		if ( IsBankrupt )
			return false;
		feePaid = EntranceFee;
		Post( LedgerCategory.GateTakings, feePaid );
		Counters.Add( ParkCounters.Admissions, 1 );
		Raise( ParkEventKind.GuestPaid, feePaid, 0, 0, nameof( LedgerCategory.GateTakings ) );
		return true;
	}

	public bool TryBuy( int instanceId, long visitorCash, out int pricePaid )
	{
		pricePaid = 0;
		if ( !objects.TryGetValue( instanceId, out var item ) || item.Kind != ParkObjectKind.Shop || !item.IsOpen || IsBankrupt || visitorCash < item.Price )
			return false;
		pricePaid = item.Price;
		BookShopSale( item, item.Price );
		return true;
	}

	private void BookShopSale( ParkObjectState item, int price )
	{
		// [BIN:TPW-EXE:0x004FE1A0 price taken] the use routine's price step takes the price (+0x194) only when it is not 0: the takings 0x004E16B0 (bank money in, the
		// shop row, the per-type sale count and the challenge accumulators), the till sound and the guest's money; a free sale skips all of it, while the cost of
		// goods (0x004FE1E0 → 0x004E1920) and the customer count (0x004E1690) are still booked
		if ( price != 0 )
		{
			Post( LedgerCategory.ShopTakings, price );
			item.TakingsThisMonth += price;
			item.TakingsRing.Today += price;
			if ( Catalog.TryGet( item.InfoId, out var info ) )
				Counters.Add( ParkCounters.Sold( info.ShopType, info.SpecialIngredient ), 1 );
		}
		// [BIN:TPW-EXE:0x004FE1E0 shop use] a shop (WhichUIType 1) books 0x004E1B40's value (ShopGoodsCost: the cost of goods scaled by the quality and special-ingredient bytes) through 0x004E1920: the bank debit, the object's cost rings and the challenge types 12 and 13 all take the same number
		var hasShopInfo = Catalog.TryGet( item.InfoId, out var shopInfo );
		// [BIN:TPI-EXE:0x612740 use outcome] Theme Park Inc's shop sale books its cost of goods (0x5E63E0, ThemeParkIncGoodsCost) through 0x5E60E0, which raises the object's daily cost ring (+0x88) and its month accumulator (+0x938) and debits nothing: the month message pays the accumulator with the running cost (ECON-060)
		var tpiGoods = Rules.ThemeParkInc && hasShopInfo;
		var goods = tpiGoods ? ThemeParkIncGoodsCost.Of( item.CostOfGoods, item.QualityOfGoods, item.IngredientAmount, ThemeParkIncGoodsCost.BuiltThirdByte, shopInfo!.SpecialIngredient, shopInfo.ShopType )
			: hasShopInfo ? ShopGoodsCost.Of( item.CostOfGoods, item.QualityOfGoods, item.IngredientAmount, shopInfo!.SpecialIngredient ) : item.CostOfGoods;
		item.CustomersThisMonth++;
		item.CustomersRing.Today++;
		item.CostsRing.Today += goods;
		if ( Rules.ThemeParkInc )
		{
			item.PendingMonthlyCosts += goods;
			item.TotalProfit += price;
		}
		else
		{
			Post( LedgerCategory.OtherCosts, goods );
			item.CostsThisMonth += goods;
			item.TotalProfit += price - goods;
		}
		Counters.Add( ParkCounters.ShopProfit, price - goods );
		if ( price != 0 )
			Raise( ParkEventKind.GuestPaid, price, item.Id, item.InfoId, nameof( LedgerCategory.ShopTakings ) );
	}

	/// <summary>
	/// A sideshow play's result (tp.exe <c>0x004E2670</c>): the win byte <c>+0x190</c> holds 100 − <c>ChanceOfLosing</c> and the play is won when
	/// <paramref name="draw"/> mod 100 is at most that byte, so the chance of winning is (byte + 1) %, 100 % from a byte of 99 up.
	/// </summary>
	// [BIN:TPW-EXE:0x004E26E2 sideshow win] cmp byte(+0x190), R mod 100; sbb/inc: won when R mod 100 <= the byte (unsigned); the result is written to script variable 11 and returned to the use routine 0x00501DB0
	// [BIN:TPW-EXE:0x004DB3A1 sideshow win byte] the object constructor stores 100 − the type record's InitChanceOfLoosing (+0xEC) at +0x190; the setter 0x004E21C0 stores a byte
	public static bool SideshowWins( int chanceOfLosingPercent, int draw ) => (uint)draw % 100 <= (uint)((100 - chanceOfLosingPercent) & 0xFF);

	public bool PlaySideshow( int instanceId, long visitorCash, out int pricePaid, out bool won )
	{
		pricePaid = 0;
		won = false;
		if ( !objects.TryGetValue( instanceId, out var item ) || item.Kind != ParkObjectKind.Sideshow || !item.IsOpen || IsBankrupt || visitorCash < item.Price )
			return false;
		pricePaid = item.Price;
		won = SideshowWins( item.ChanceOfLosingPercent, (int)World.Next( 100u ) );
		BookSideshowPlay( item, item.Price, won );
		return true;
	}

	private void BookSideshowPlay( ParkObjectState item, int price, bool won )
	{
		var prize = won ? item.CostOfGoods : 0;
		// [BIN:TPW-EXE:0x004FE1A0 price taken] a free play skips the takings (0x004E16B0) as a free sale does; the prize is still paid
		if ( price != 0 )
		{
			Post( LedgerCategory.SideshowTakings, price );
			item.TakingsThisMonth += price;
			item.TakingsRing.Today += price;
		}
		item.CustomersThisMonth++;
		item.CustomersRing.Today++;
		item.CostsRing.Today += prize;
		if ( Rules.ThemeParkInc )
		{
			// [BIN:TPI-EXE:0x612789 sideshow cost] a won play (the use outcome 0x612740 runs for +0x21B set) books the thing's +0x88 through 0x5E60E0, the month accumulator again (ECON-060)
			item.PendingMonthlyCosts += prize;
			item.TotalProfit += price;
		}
		else
		{
			Post( LedgerCategory.OtherCosts, prize );
			item.CostsThisMonth += prize;
			item.TotalProfit += price - prize;
		}
		if ( won )
		{
			item.WinnersThisMonth++;
			item.WinnersRing.Today++;
		}
		Counters.Add( ParkCounters.SideshowProfit, price - prize );
		if ( price != 0 )
			Raise( ParkEventKind.GuestPaid, price, item.Id, item.InfoId, nameof( LedgerCategory.SideshowTakings ) );
	}

	/// <summary>
	/// Theme Park Inc: the checks a guest passes when it boards (the object is open, the park is not bankrupt, the purse holds the price) and the price it will pay at the exit; a sideshow's play is drawn here (the
	/// result is kept by the guest and read at the exit). Nothing is booked: <see cref="SettleVisit"/> does that when the guest leaves. False refuses the visit (a ride's too, when its fee is above 0).
	/// </summary>
	// [BIN:TPI-EXE:0x619670 fee recorded] the attraction's call records the price (+0x1B4 of the guest, the thing's +0x94) and nothing is paid; the money moves in the exit's payment step (0x6126D0, called at 0x61193B inside 0x6118D0, reached from 0x619A8E just before the exit state 0x13 is entered)
	public bool TryAdmitVisit( int instanceId, long visitorCash, out int price, out bool? won )
	{
		price = 0;
		won = null;
		if ( !objects.TryGetValue( instanceId, out var item ) )
			return true;
		switch ( item.Kind )
		{
			case ParkObjectKind.Shop:
				if ( !item.IsOpen || IsBankrupt || visitorCash < item.Price )
					return false;
				price = item.Price;
				return true;
			case ParkObjectKind.Sideshow:
				if ( !item.IsOpen || IsBankrupt || visitorCash < item.Price )
					return false;
				price = item.Price;
				won = SideshowWins( item.ChanceOfLosingPercent, (int)World.Next( 100u ) );
				return true;
			case ParkObjectKind.Ride:
				if ( item.Price == 0 )
					return true;
				if ( IsBankrupt || visitorCash < item.Price )
					return false;
				price = item.Price;
				return true;
			default:
				return true;
		}
	}

	/// <summary>Theme Park Inc: the payment step and the cost booking of a finished use (see <see cref="TryAdmitVisit"/>): the fee the guest recorded is credited at once, the goods or the prize go to the object's month accumulator.</summary>
	// [BIN:TPI-EXE:0x6126D0 fare taken] the exit's payment step takes the recorded fee whenever it is above 0 (0x6126E3..0x61270A): the income booking 0x5E5DE0 credits the bank at once (0x5C0760), adds it to the object's takings and, for a shop, posts message 0xC; the guest's purse (+0x1C0) falls and its spent word (+0x1C4) rises, for every kind
	// [BIN:TPI-EXE:0x612740 use outcome] then, for a guest whose +0x21B is set (a won sideshow, a shop's sale), the cost booking: a shop's goods (0x5E63E0) or a sideshow's prize (+0x88) through 0x5E60E0 (0x6119A1)
	public void SettleVisit( int instanceId, int price, bool? won )
	{
		if ( !objects.TryGetValue( instanceId, out var item ) )
			return;
		switch ( item.Kind )
		{
			case ParkObjectKind.Shop: BookShopSale( item, price ); break;
			case ParkObjectKind.Sideshow: BookSideshowPlay( item, price, won == true ); break;
			case ParkObjectKind.Ride: if ( price != 0 ) BookRideFare( item, price ); break;
		}
	}

	/// <summary>
	/// A Theme Park Inc ride's fee for one use: the price (<see cref="ParkObjectState.Price"/>, <c>UsageInfo.InitPricePerUse</c>) when it is above 0 and the guest holds that much: the takings are credited at once, the
	/// object's takings and total profit rise and the guest's purse falls (the caller). Theme Park World's rides are free; true there and for a free ride.
	/// </summary>
	// [BIN:TPI-EXE:0x6126D0 fare taken] the use's payment step takes the fee the guest recorded when the attraction called it (+0x1B4, the thing's price +0x94 at 0x619670) whenever it is above 0: the income booking 0x5E5DE0 credits the bank at once (0x5C0760: balance += fee), adds it to the object's takings (+0x0, +0x6E8) and, for a shop, posts message 0xC; then the guest's purse (+0x1C0) falls and its spent word (+0x1C4) rises, for every kind
	// [BIN:TPI-EXE:0x61243A cash test] the price test refuses a guest whose purse (+0x1C0) is below the price (unsigned), before the use
	public bool TryPayRideFare( int instanceId, long visitorCash, out int pricePaid )
	{
		pricePaid = 0;
		if ( !Rules.ThemeParkInc || !objects.TryGetValue( instanceId, out var item ) || item.Kind != ParkObjectKind.Ride || item.Price == 0 )
			return true;
		if ( IsBankrupt || visitorCash < item.Price )
			return false;
		pricePaid = item.Price;
		BookRideFare( item, item.Price );
		return true;
	}

	private void BookRideFare( ParkObjectState item, int price )
	{
		Post( LedgerCategory.OtherIncome, price );
		item.TakingsThisMonth += price;
		item.TakingsRing.Today += price;
		item.TotalProfit += price;
		Raise( ParkEventKind.GuestPaid, price, item.Id, item.InfoId, nameof( LedgerCategory.OtherIncome ) );
	}

	/// <summary>Rides have no per-ride price in the original data of Theme Park World; use is counted for statistics and challenges.</summary>
	public void RecordRideUse( int instanceId )
	{
		if ( !objects.TryGetValue( instanceId, out var item ) )
			return;
		item.CustomersThisMonth++;
		item.CustomersRing.Today++;
		Counters.Add( ParkCounters.RideUses( item.InfoId ), 1 );
		// [BIN:TPW-EXE:0x5021c5 ride use] the event carries the used ride's thing id as its object (parameter B of 0x004D27A0), which a carried object (ECON-055) must equal
		Counters.Add( ParkCounters.RideUsesOfThing( item.Id ), 1 );
	}

	/// <summary>
	/// A guest has left the object: raises <see cref="ParkObjectState.TimesUsed"/>, the original's word +412, which the
	/// release path increments (0xEAA00 → 0xE1B44), not the boarding. Until the first guest leaves the object counts as unused.
	/// </summary>
	public void RecordUseFinished( int instanceId )
	{
		if ( objects.TryGetValue( instanceId, out var item ) )
			item.TimesUsed++;
	}

	/// <summary>A use ended with a guest happiness change (tp.exe <c>0x004FD970</c> → <c>0x004E1E00</c>): the object's satisfaction average of today takes it.</summary>
	// [BIN:TPW-EXE:0x004FDA85 use satisfaction] after the use routine the change is 3 × ((u8)trunc(happiness at +0x19c) - (u8)trunc(the happiness kept when the guest joined the queue, +0x20c)); a lost sideshow skips it
	public void RecordUseSatisfaction( int instanceId, int change )
	{
		if ( objects.TryGetValue( instanceId, out var item ) )
			item.SatisfactionRing.Average( change );
	}

	/// <summary>Days of walk-aways an object keeps (the ring <c>+0x234</c> the object constructor sizes to 30).</summary>
	// [BIN:TPW-EXE:0x004DB090 object constructor] the ring heads +0x224/+0x2AC take -1 and the sizes +0x228/+0x2B0 take 0x1E, the full flags +0x22C/+0x2B4 0 (0x004DB187..0x004DB1AB)
	public const int WalkAwayHistoryDays = 30;

	/// <summary>
	/// A guest refused the object's price at its stand point and walked away (tp.exe <c>0x004E1670</c>): <see cref="ParkObjectState.WalkAways"/>
	/// and <see cref="ParkObjectState.WalkAwaysToday"/> each rise by one.
	/// </summary>
	// [BIN:TPW-EXE:0x004E1670 walk-away] +0x1A4 (mNumWalkAways, serializer key at 0x0075B508) += 1 and the day count +0x230 += 1; the state-13 refusal (0x0050077F) is its only caller
	public void RecordWalkAway( int instanceId )
	{
		if ( !objects.TryGetValue( instanceId, out var item ) )
			return;
		item.WalkAways++;
		item.WalkAwaysToday++;
	}

	/// <summary>The day's end for the objects (tp.exe <c>0x004DD320</c> on the day message): today's walk-aways move into the history, which keeps the last 30 days.</summary>
	// [BIN:TPW-EXE:0x004DD320 object day message] on message 0xB (the calendar update 0x004F8260 sends it when the day changes, 0x0040F4C0; 0xC at a new month) each of the object's six rings takes its day count: +0x2AC = head + 1, wrapping to 0 with the full flag set at the size; the ring entry = +0x230, then +0x230 = 0
	private void EndObjectDay()
	{
		foreach ( var item in objects.Values )
		{
			item.WalkAwayDays.Add( item.WalkAwaysToday );
			if ( item.WalkAwayDays.Count > WalkAwayHistoryDays )
				item.WalkAwayDays.RemoveAt( 0 );
			item.WalkAwaysToday = 0;
			item.CostsRing.Roll();
			item.TakingsRing.Roll();
			item.CustomersRing.Roll();
			item.WinnersRing.Roll();
			item.SatisfactionRing.Roll();
		}
	}

	/// <summary>The park's perception statistics (<see cref="ParkPriceStatistics"/>), which the price test at a stand point records.</summary>
	public ParkPriceStatistics PriceStatistics { get; } = new();

	/// <summary>The analyser's monthly statistics the park information window graphs (<see cref="ParkStatisticsHistory"/>); nothing in the simulation reads them.</summary>
	public ParkStatisticsHistory StatisticsHistory { get; } = new();

	public void ReportRecord( ParkRecordKind kind, int instanceId, long value )
	{
		var infoId = objects.TryGetValue( instanceId, out var item ) ? item.InfoId : 0;
		Counters.Max( ParkCounters.Record( kind, infoId ), value );
		// the instance that holds a record, a later one replacing it only when strictly above (the best-object walks keep the first of equals, 0x004CC810, 0x004CC4B0)
		if ( objects.ContainsKey( instanceId ) && (!recordBest.TryGetValue( (kind, infoId), out var held ) || value > held.Value || !objects.ContainsKey( held.Instance )) )
			recordBest[(kind, infoId)] = (value, instanceId);
	}


	public void SetObjectOpen( int instanceId, bool open ) => RequireObject( instanceId ).IsOpen = open;

	// ---------------------------------------------------------------- save support

	internal IReadOnlyCollection<int> TakenOffers => takenOffers;
	/// <summary>The offers whose stored credit test passed (for the park save).</summary>
	internal IEnumerable<int> AvailableOfferIndices => loanAvailable.Where( pair => pair.Value ).Select( pair => pair.Key ).OrderBy( index => index );

	/// <summary>Restores the offers' stored credit tests (a park save's or an original save's); offers not listed failed it.</summary>
	internal void RestoreLoanAvailability( IEnumerable<int> availableOffers )
	{
		var available = availableOffers.ToHashSet();
		foreach ( var offer in Settings.Loans )
			loanAvailable[offer.Index] = available.Contains( offer.Index );
	}

	internal void RestoreTicketItems( IEnumerable<int> items )
	{
		ticketItems.Clear();
		foreach ( var item in items )
			ticketItems.Add( item );
	}

	// ---------------------------------------------------------------- original save import

	/// <summary>The supported TPW restart calendar still owes its first day message; consumed only by an actual park turn.</summary>
	internal bool PendingInitialDay { get; set; }


	/// <summary>
	/// Moves a park that has not run yet to an original save's park turn (<c>mGameTick</c>, the turn the calendar
	/// converts, so the date matches the save's). The ledger month and the hiring pool's timers move with it; the
	/// challenge state is imported separately (<see cref="RestoreOriginalChallenges"/>).
	/// </summary>
	internal void StartAtOriginalTurn( long turn )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( turn );
		if ( Tick != 0 || objects.Count != 0 || Ledger.History.Count != 0 )
			throw new InvalidOperationException( "Only a park that has not run can start at an original save's turn." );
		Tick = ParkCalendar.TickOfTurn( turn );
		Ledger.Restore( Ledger.Balance, Ledger.CurrentOpeningBalance, ParkCalendar.MonthIndex( Tick ), new Dictionary<LedgerCategory, long>(), Array.Empty<LedgerMonth>() );
		Staff.ShiftSchedule( Tick );
	}

	/// <summary>
	/// Takes an original save's challenge state (<see cref="OriginalEconomyImport"/>): the next offer day, the round-robin position, the finished and
	/// declined challenges, and a challenge that is running (accepted, with its deadline and the value it has reached) or offered and unanswered.
	/// </summary>
	internal void RestoreOriginalChallenges( long nextOfferDay, int nextListPosition, int? runningIndex, long deadlineDay, long value, int? offeredIndex,
		IReadOnlyDictionary<int, int> declines, IEnumerable<int> finished, bool switchedOn = true, int actualThing = 0, IReadOnlyDictionary<int, int>? carriedThings = null,
		long? nextOfferSeconds = null, long? deadlineSeconds = null )
	{
		// the carried object first: the baseline of a ride-use type counts that object's uses
		Objectives.RestoreCarried( actualThing, carriedThings );
		ActiveChallenge? current = null;
		if ( runningIndex is int running && Settings.Challenges.TryGetValue( running, out var definition ) )
		{
			// The original accumulates the value from 0 at the offer; OpenTPW measures a count type against a baseline, so the baseline is set
			// to give the saved value now
			// the daily-average types keep the system's accumulated value itself
			var baseline = ParkObjectives.IsCountSinceAcceptance( definition.Type ) ? MeasureChallenge( definition ) - value : ParkObjectives.IsDailyAverage( definition.Type ) ? value : 0;
			var endSeconds = deadlineSeconds ?? deadlineDay * ParkCalendar.SecondsPerDay;
			var start = (endSeconds - definition.TargetTime * ParkCalendar.SecondsPerDay) / ParkCalendar.SecondsPerDay;
			current = new ActiveChallenge( running, start, true, start, endSeconds / ParkCalendar.SecondsPerDay, baseline, endSeconds, unchecked( (int)value ) );
		}
		else if ( offeredIndex is int offered && Settings.Challenges.ContainsKey( offered ) )
			current = new ActiveChallenge( offered, ParkCalendar.DayIndex( Tick ), false, 0, 0, 0, RawValue: unchecked( (int)value ) );
		Objectives.Restore( nextOfferDay, nextListPosition, current, declines, finished, Objectives.GoldenTickets, switchedOn, actualThing, carriedThings, nextOfferSeconds );
	}

	/// <summary>Takes an original save's bank account: balance, admission fee, the year's profit and the bought loans.</summary>
	internal void ImportOriginalBank( long balance, int admissionFee, long profitThisYear, IEnumerable<LoanAccount> boughtLoans )
	{
		Ledger.Restore( balance, balance, Ledger.CurrentMonthIndex, new Dictionary<LedgerCategory, long>(), Array.Empty<LedgerMonth>() );
		Ledger.RestoreProfitThisYear( profitThisYear );
		SetEntranceFee( admissionFee );
		loans.Clear();
		loans.AddRange( boughtLoans );
		takenOffers.Clear();
		takenOffers.UnionWith( loans.Select( loan => loan.OfferIndex ) );
	}

	/// <summary>Restores the bank's withdrawals flag, balance after the last debit and turn-entered-red stamp (an original save's or OpenTPW's).</summary>
	internal void RestoreBankWords( bool withdrawalsEnabled, long lastBalance, long turnEnteredRed, long batchCredit = 0 )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( batchCredit );
		BatchCredit = batchCredit;
		ArgumentOutOfRangeException.ThrowIfNegative( turnEnteredRed );
		WithdrawalsEnabled = withdrawalsEnabled;
		LastBalance = lastBalance;
		TurnEnteredRed = turnEnteredRed;
	}

	internal void RestoreState( long tick, GameSpeed speed, bool open, int fee, bool bankrupt, int monthsInRed, IEnumerable<LitterCell> litter, int ticketsSpent, int nextObjectId,
		long droppedAdmissions, IEnumerable<ParkObjectState> restoredObjects, IEnumerable<LoanAccount> restoredLoans, IEnumerable<int> restoredOffers, IEnumerable<long> restoredAdmissions )
	{
		Tick = tick;
		Speed = speed;
		IsParkOpen = open;
		EntranceFee = fee;
		IsBankrupt = bankrupt;
		MonthsInRed = monthsInRed;
		Litter.Restore( litter );
		TicketsSpent = ticketsSpent;
		NextObjectId = nextObjectId;
		DroppedAdmissions = droppedAdmissions;
		objects.Clear();
		foreach ( var item in restoredObjects )
		{
			if ( !Catalog.TryGet( item.InfoId, out _ ) )
				throw new InvalidDataException( $"Saved object {item.Id} has Info.Id {item.InfoId}, which the {Settings.Theme} catalogue does not declare." );
			item.StoredState = InitialStoredState( item );
			objects.Add( item.Id, item );
			HookStateWord( item );
		}
		loans.Clear();
		loans.AddRange( restoredLoans );
		takenOffers.Clear();
		takenOffers.UnionWith( restoredOffers );
		monthlyAdmissions.Clear();
		monthlyAdmissions.AddRange( restoredAdmissions );
	}
}
