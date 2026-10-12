namespace OpenTPW;

/// <summary>Incidental cell purchases priced by <c>Costs.*</c>.</summary>
public enum CellPurchase
{
	Path,
	Queue,
	KartTrack,
	WaterTrack,
	Land
}

/// <summary>
/// The park management simulation: calendar, bank account, prices, construction and scrap,
/// loans, bankruptcy, staff, maintenance, litter, research, challenges, golden tickets and park
/// rating. Deterministic: all randomness comes from one saved <see cref="DeterministicRandom"/> and
/// time advances in whole fixed ticks. Values come from the original settings wherever they exist;
/// every approximation is listed in docs/ECONOMY.md and in the member documentation.
/// </summary>
public sealed class ParkEconomy : IParkEconomy
{
	/// <summary>Advisor.sam <c>StaffHireMechanics1.PoorerStateThan</c>: below this state of repair a ride is worn and gets a mechanic.</summary>
	// [DATA:Advisor/Advisor.sam:StaffHireMechanics1.PoorerStateThan]
	public const int WornStateOfRepair = 25;
	/// <summary>Months in the red before bankruptcy (TAG_SYSTEM 123–127: warnings at 3 and 5, bankrupt after 6).</summary>
	// [DATA:Language/*/TAG_SYSTEM.str:123-127 (six months in the red)]
	public const int MonthsInRedForBankruptcy = 6;
	// [APPROX:ECON-020] a sale adds LitterEffect/100 litter items to a park-wide store; the original adds LitterEffect to the buying guest's 0..100 litter meter, which drops one litter event at 90 (ORACLE F18) — evidence needed: a guest litter meter with the drop-at-90 and bin rules, and the park's litter count
	public const int LitterScale = 100;

	private readonly SortedDictionary<int, ParkObjectState> objects = new();
	private readonly List<LoanAccount> loans = new();
	private readonly HashSet<int> takenOffers = new();
	private readonly List<long> monthlyAdmissions = new();
	private IParkGuestStatistics guestStatistics = NoGuestStatistics.Instance;

	public ParkEconomy( BalanceSettings settings, IEconomyObjectCatalog catalog, ParkGameMode mode, ulong seed )
	{
		Settings = settings ?? throw new ArgumentNullException( nameof( settings ) );
		Catalog = catalog ?? throw new ArgumentNullException( nameof( catalog ) );
		Mode = mode;
		Features = ParkModeFeatures.For( mode );
		Random = new DeterministicRandom( seed );
		Ledger = new ParkLedger( settings.InitialCash );
		Staff = new ParkStaff( settings );
		Research = new ParkResearch( settings, catalog );
		Objectives = new ParkObjectives( settings, catalog );
		EntranceFee = settings.InitialAdmissionFee;
		Staff.CreateInitialPool( Random, 0 );
	}

	/// <summary>Loads the original settings and objects of <paramref name="theme"/> and starts a new park.</summary>
	public static ParkEconomy CreateForTheme( string theme, bool easy, ParkGameMode mode = ParkGameMode.FullSimulation, ulong seed = 1 ) =>
		new( BalanceSettings.Load( theme, easy ), EconomyObjectCatalog.Load( theme, easy ), mode, seed );

	public BalanceSettings Settings { get; }
	public IEconomyObjectCatalog Catalog { get; }
	public ParkGameMode Mode { get; }
	public ParkModeFeatures Features { get; }
	public DeterministicRandom Random { get; }
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
	public bool IsBankrupt { get; private set; }
	public int MonthsInRed { get; private set; }
	public long LitterScaled { get; private set; }
	public int TicketsSpent { get; private set; }
	private readonly HashSet<int> ticketItems = new();
	/// <summary>Objects bought with golden tickets; further copies are paid in cash.</summary>
	public IReadOnlyCollection<int> TicketItems => ticketItems;
	public int NextObjectId { get; private set; } = 1;
	public IReadOnlyCollection<ParkObjectState> Objects => objects.Values;
	public IReadOnlyList<LoanAccount> Loans => loans;
	public IReadOnlyList<long> MonthlyAdmissions => monthlyAdmissions;
	public int GoldenTicketsAvailable => Objectives.GoldenTickets.Count - TicketsSpent;
	public IParkGuestStatistics GuestStatistics { get => guestStatistics; set => guestStatistics = value ?? NoGuestStatistics.Instance; }

	public event Action<ParkEvent>? EventRaised;

	/// <summary>Loan offers that can still be taken; none in Instant Action (UIHELPTEXT 493).</summary>
	// [BIN:STP-PPC:0x10154AA0 loans window] the Available Loans window only opens outside game type 2 (Instant Action)
	public IEnumerable<LoanOffer> AvailableLoans => !Features.Loans
		? Enumerable.Empty<LoanOffer>()
		: Settings.Loans.Where( offer => !takenOffers.Contains( offer.Index ) );

	public bool TryGetObject( int instanceId, out ParkObjectState state ) => objects.TryGetValue( instanceId, out state! );

	private void Raise( ParkEventKind kind, long amount = 0, int instanceId = 0, int infoId = 0, string detail = "" ) =>
		EventRaised?.Invoke( new ParkEvent( Tick, kind, instanceId, infoId, amount, detail ) );

	private void Post( LedgerCategory category, long amount )
	{
		var wasInBlack = Ledger.Balance >= 0;
		Ledger.Post( category, amount );
		if ( wasInBlack && Ledger.Balance < 0 )
			Raise( ParkEventKind.InTheRed, Ledger.Balance );
	}

	// ---------------------------------------------------------------- time

	/// <summary>One fixed 60 Hz frame tick: advances the park by <see cref="Speed"/> ticks (0 when paused).</summary>
	public void AdvanceFixedTick() => Advance( (int)Speed );

	/// <summary>Advances the simulation by <paramref name="ticks"/> fixed ticks. Each new park turn runs the turn work and the hourly, daily and monthly updates for the calendar boundaries it crosses.</summary>
	public void Advance( long ticks )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( ticks );
		// [APPROX:ECON-030] the simulation stops once bankrupt — evidence needed: capture of the bankrupt state
		for ( long step = 0; step < ticks && !IsBankrupt; step++ )
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
	// A mechanic's BusyTicks is the number of park turns (staff updates) until its job completes.

	public long Turn => ParkCalendar.Turn( Tick );

	private void AdvanceTurn( long previousTurn, long turn )
	{
		// [BIN:STP-PPC:0x100D9E04 repair state] each staff update counts a mechanic's job timer down by one; the update that finds it zero completes the repair
		foreach ( var member in Staff.Members )
		{
			if ( member.BusyTicks == 0 )
				continue;
			member.BusyTicks--;
			if ( member.BusyTicks == 0 )
				FinishJob( member );
		}
		// [BIN:STP-PPC:0x100F0284 researcher update] each researcher not in an excluded state does research when the turn counter is a multiple of 20
		if ( turn % ParkResearch.TurnsPerResearch == 0 )
			DoResearch();
		// [BIN:STP-PPC:0x100E077C ride update] each ride is updated when the turn counter is a multiple of 8
		if ( turn % RideUpdateInterval == 0 )
			UpdateRides( turn );
		// [BIN:STP-PPC:0x100D67F0 world update] the golden-ticket check (0x100D31D0) runs when the turn counter is a multiple of 100, and only in Full Simulation (game type 0)
		if ( turn % GoldenTicketCheckInterval == 0 && Mode == ParkGameMode.FullSimulation )
			CheckGoldenTickets();
		// [BIN:STP-PPC:0x100E3F0C calendar update] day, month and year events fire when the extracted date fields change
		var before = ParkCalendar.Epoch.AddSeconds( previousTurn * ParkCalendar.SecondsPerTurn );
		var now = ParkCalendar.Epoch.AddSeconds( turn * ParkCalendar.SecondsPerTurn );
		var hourBefore = previousTurn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerHour;
		for ( var hour = hourBefore + 1; hour <= turn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerHour && !IsBankrupt; hour++ )
			UpdateHour();
		var dayBefore = previousTurn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay;
		var day = turn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay;
		for ( var ended = dayBefore; ended < day && !IsBankrupt; ended++ )
			EndDay( ended );
		if ( now.Month != before.Month || now.Year != before.Year )
			EndMonth( (now.Year - ParkCalendar.Epoch.Year) * ParkCalendar.MonthsPerYear + now.Month - 1 );
		if ( now.Year != before.Year )
			Ledger.StartYear();
	}

	private void DoResearch()
	{
		var employed = Staff.OfType( StaffType.Researcher ).ToList();
		var abilities = employed.Where( ParkResearch.CanResearch ).Select( member => Settings.ResearchAbility[member.Grade] ).ToList();
		// Research comes from researchers only (Mac 0xf0728 has no staffless path); an imported seed park brings its own.
		foreach ( var ability in abilities )
		{
			foreach ( var item in Research.AddResearcherPoints( ability ) )
			{
				Counters.Add( ParkCounters.Researched( item.Category ), 1 );
				Raise( ParkEventKind.ItemResearched, item.Level, 0, item.InfoId, Catalog.TryGet( item.InfoId, out var info ) ? info.Name : "" );
			}
		}
	}

	private void CheckGoldenTickets()
	{
		foreach ( var ticket in Objectives.CheckGoldenTickets( Counters, guestStatistics, Research, Ledger.ProfitThisYear, monthlyAdmissions ) )
			Raise( ParkEventKind.GoldenTicketWon, (int)ticket, 0, 0, ticket.ToString() );
	}

	/// <summary>Advances to the start of the calendar day <paramref name="days"/> days after the current one.</summary>
	public void AdvanceDays( int days ) => Advance( Math.Max( 0, ParkCalendar.TickAtDay( ParkCalendar.DayIndex( Tick ) + days ) - Tick ) );

	/// <summary>Advances to the start of the calendar month <paramref name="months"/> months after the current one.</summary>
	public void AdvanceMonths( int months ) => Advance( Math.Max( 0, ParkCalendar.TickAtMonth( ParkCalendar.MonthIndex( Tick ) + months ) - Tick ) );

	private void UpdateHour()
	{
		Staff.UpdatePool( Random, Tick );
		DispatchMechanics();
		CleanLitter();
	}

	/// <summary>
	/// The original ride update (docs/reverse/RIDE-WEAR.md): every 8 turns each ride runs the wear step, which acts on
	/// every 64th turn, and then the breakdown check.
	/// </summary>
	private void UpdateRides( long turn )
	{
		foreach ( var item in objects.Values.Where( item => item.Kind == ParkObjectKind.Ride ).ToList() )
		{
			// [APPROX:ECON-023] the ride update skips rides in states 1, 3 and 4 (+0x198) and rides with +0x64 == 0, and the breakdown check skips +0x2e bit 0; OpenTPW maps these only to "a mechanic is at work" — evidence needed: the names of those ride states and flags
			if ( item.MechanicId != 0 )
				continue;
			if ( turn % WearInterval == 0 )
				Wear( item );
			// [BIN:STP-PPC:0x100E077C breakdown check] a ride that is not already broken breaks down when its truncated life gauge or state of repair is 0; when only the state of repair is 0 the life gauge first loses 5 (clamped to 0..100); 0x100E09F4 then sets VAR_BREAKSTAT; there is no random roll
			if ( item.IsBrokenDown || ((int)item.LifeGauge != 0 && (int)item.Repair != 0) )
				continue;
			if ( (int)item.LifeGauge != 0 )
				item.LifeGauge = Math.Clamp( item.LifeGauge - BreakdownLifeLoss, 0, 100 );
			item.IsBrokenDown = true;
			Raise( ParkEventKind.RideBrokeDown, 0, item.Id, item.InfoId );
		}
	}

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
		if ( before >= WornStateOfRepair && item.StateOfRepair < WornStateOfRepair )
			Raise( ParkEventKind.RideWorn, item.StateOfRepair, item.Id, item.InfoId );
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

	private void FinishJob( StaffMember member )
	{
		member.State = StaffState.Patrolling;
		if ( !objects.TryGetValue( member.AssignedInstanceId, out var item ) )
		{
			member.AssignedInstanceId = 0;
			return;
		}
		member.AssignedInstanceId = 0;
		item.MechanicId = 0;
		if ( item.PendingLevel > item.Level )
		{
			item.Level = item.PendingLevel;
			item.PendingLevel = 0;
			Raise( ParkEventKind.UpgradeCompleted, item.Level, item.Id, item.InfoId );
		}
		else
		{
			// [BIN:STP-PPC:0x100DEF2C repair] a repair sets the state of repair to 100 and clears the worn flag; it does not restore the life gauge
			item.Repair = 100;
			item.IsBrokenDown = false;
			Raise( ParkEventKind.RideRepaired, 0, item.Id, item.InfoId );
		}
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
	/// not modelled (<b>approximation</b>).
	/// </summary>
	private void DispatchMechanics()
	{
		var jobs = objects.Values.Where( item => item.MechanicId == 0 && (item.IsBrokenDown || item.PendingLevel > item.Level || (item.Kind == ParkObjectKind.Ride && item.StateOfRepair < WornStateOfRepair)) )
			.OrderBy( item => item.IsBrokenDown ? 0 : item.PendingLevel > item.Level ? 1 : 2 ).ThenBy( item => item.StateOfRepair ).ThenBy( item => item.Id ).ToList();
		if ( jobs.Count == 0 )
			return;
		var role = Settings[StaffType.Mechanic];
		foreach ( var mechanic in Staff.OfType( StaffType.Mechanic ).Where( member => member.IsAvailable ).OrderBy( member => member.Id ) )
		{
			if ( jobs.Count == 0 )
				break;
			var job = jobs[0];
			jobs.RemoveAt( 0 );
			// [APPROX:ECON-021] the mechanic starts at once (the original first walks to the ride, 0xe6454) and the timer counts one park turn per staff update (dispatcher cadence read statically) — evidence needed: capture of a repair from arrival to completion
			int? upgradeDuration = job.PendingLevel > job.Level && Catalog.TryGet( job.InfoId, out var info ) && job.PendingLevel < info.Upgrades.Count
				? info.Upgrades[job.PendingLevel].DurationOfUpgrade : null;
			// The repair handler (0xd9e04) decrements a nonzero timer and completes on the update that finds it zero: timer + 1 updates.
			mechanic.BusyTicks = RepairTimer( role.WorkDuration[mechanic.Grade], job.Repair, upgradeDuration ) + 1;
			mechanic.AssignedInstanceId = job.Id;
			mechanic.State = StaffState.Working;
			job.MechanicId = mechanic.Id;
		}
	}

	/// <summary>Each free handyman removes one litter item per <c>HandymanConstsPerGrade[grade].WorkDuration</c> game minutes (<b>approximation</b>).</summary>
	private void CleanLitter()
	{
		if ( LitterScaled == 0 )
			return;
		var role = Settings[StaffType.Handyman];
		foreach ( var handyman in Staff.OfType( StaffType.Handyman ).Where( member => member.IsAvailable ) )
			// [APPROX:ECON-022] a handyman removes one litter item per WorkDuration game minutes, park-wide — evidence needed: capture of cleaning speed
			LitterScaled = Math.Max( 0, LitterScaled - 60L * LitterScale / Math.Max( 1, role.WorkDuration[handyman.Grade] ) );
	}

	private void EndDay( long day )
	{
		var challenges = Features.Challenges ? Objectives.AdvanceDay( day + 1, MeasureChallenge ).ToList() : new();
		foreach ( var (kind, index, amount, detail) in challenges )
		{
			if ( kind == ParkEventKind.ChallengeCompleted )
				Post( LedgerCategory.OtherIncome, amount );
			Raise( kind, amount, 0, index, detail );
		}
		Raise( ParkEventKind.DayEnded, Balance );
	}

	private long MeasureChallenge( ChallengeDefinition definition ) => Objectives.Measure( definition, Counters, Staff, objects.Values, guestStatistics );

	private void EndMonth( long nextMonthIndex )
	{
		var wages = Staff.TotalMonthlyWages;
		if ( wages > 0 )
		{
			Post( LedgerCategory.StaffCosts, wages );
			Raise( ParkEventKind.WagesPaid, wages, 0, 0, $"{Staff.Members.Count} staff" );
		}
		var training = Staff.Train();
		if ( training > 0 )
		{
			Post( LedgerCategory.StaffCosts, training );
			Raise( ParkEventKind.TrainingPaid, training );
		}
		// [BIN:STP-PPC:0x100CC21C loan instalment] each bought loan, in offer-index order, pays exactly M and counts one more month repaid; it ends after N months (ORACLE F13)
		// [APPROX:ECON-051] the bank's withdrawals flag (+276) has no counterpart: loan instalments are always deducted from the balance (the original, with the flag off, skips the deduction and keeps the profit bookkeeping) — evidence needed: the writer of the flag and when the original clears it
		foreach ( var loan in loans.OrderBy( item => item.OfferIndex ).ToList() )
		{
			var (account, paid) = LoanMath.Pay( loan );
			Post( LedgerCategory.LoanPayments, paid );
			Ledger.ChargeLoanInterest( LoanMath.InstalmentInterest( account.MonthlyRepayment, account.Months, account.OriginalAmount ) );
			Raise( ParkEventKind.LoanPayment, paid, 0, account.OfferIndex );
			var position = loans.IndexOf( loan );
			if ( account.MonthsRemaining <= 0 )
			{
				loans.RemoveAt( position );
				// [BIN:STP-PPC:0x100CC21C loan instalment] a fully repaid loan clears its bought flag; 0x100CC9E8 then offers it again when the credit test passes
				// [APPROX:ECON-007] reopening has no original credit-eligibility gate — evidence needed: implement the traced credit predicate and qualify its cross-edition behavior
				takenOffers.Remove( account.OfferIndex );
				Raise( ParkEventKind.LoanRepaid, 0, 0, account.OfferIndex );
			}
			else
				loans[position] = account;
		}
		foreach ( var item in objects.Values )
		{
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
		if ( Balance < 0 )
		{
			MonthsInRed++;
			if ( MonthsInRed >= MonthsInRedForBankruptcy )
			{
				IsBankrupt = true;
				IsParkOpen = false;
				Raise( ParkEventKind.Bankrupt, MonthsInRed );
			}
			else if ( MonthsInRed is 3 or 5 )
				Raise( ParkEventKind.BankruptcyWarning, MonthsInRed );
		}
		else
			MonthsInRed = 0;
		var closed = Ledger.CloseMonth( nextMonthIndex, ParkRating, ParkValue );
		Raise( ParkEventKind.MonthEnded, closed.ClosingBalance, 0, 0, $"in ${closed.MoneyIn}, out ${closed.MoneyOut}" );
		if ( nextMonthIndex % ParkCalendar.MonthsPerYear == 0 )
		{
			var year = Ledger.SummariseYear( nextMonthIndex / ParkCalendar.MonthsPerYear - 1 );
			Raise( ParkEventKind.YearEnded, year?.Profit ?? 0, 0, 0, year == null ? "" : $"in ${year.MoneyIn}, out ${year.MoneyOut}, balance ${year.ClosingBalance}" );
		}
	}

	/// <summary>Admissions of months dropped from the bounded history (keeps the monthly differences exact).</summary>
	public long DroppedAdmissions { get; private set; }

	// ---------------------------------------------------------------- park values

	/// <summary>
	/// Scrap value, the refund when an object is sold (the sale 0xDBFA8 credits the getter 0xE25E8, which the scrap value
	/// panels also show): the base purchase price <c>Upgrades[0].CostOfUpgrade</c> (catalogue +440; upgrade costs are not added)
	/// times <see cref="ScrapPercent"/>, divided by 100 (ORACLE.md F21).
	/// </summary>
	// [BIN:STP-PPC:0x100E25E8 scrap value] Upgrades[0].CostOfUpgrade × the percentage 0xE2424, unsigned division by 100
	public long ScrapValue( ParkObjectState item )
	{
		if ( !Catalog.TryGet( item.InfoId, out var info ) || info.Upgrades.Count == 0 )
			return 0;
		// the 32-bit multiply wraps and the division is unsigned (cost 50,000,000 at 100 % refunds 7,050,327)
		return unchecked( (uint)((int)info.Upgrades[0].CostOfUpgrade * ScrapPercent( item )) ) / 100;
	}

	/// <summary>
	/// The object's scrap percentage (0xE2424): the age is the park clock since the build, in 365-day years (0, 1, 2, anything
	/// else uses year 4), read from the current level's row <c>Upgrades[level].ScrapValueYearN</c>. An object nobody has used
	/// (<see cref="ParkObjectState.TimesUsed"/> 0) in its first 30 days is refunded 100 percent, when the base level's year-1
	/// percentage is above 0.
	/// </summary>
	// [BIN:STP-PPC:0x100E2424 scrap percentage] age = clock stamp − thing +20 as signed 100 ns units; quotient by 365 days (exactly) 0 → year 1, 1, 2, else (also negative) year 4; in year 1, quotient by 30 days 0 and thing +412 = 0 and record +392 > 0 give 100; the row is record +392 + 64 × level
	public int ScrapPercent( ParkObjectState item )
	{
		// [APPROX:ECON-025] the scrap rule is the Mac build's (0xE25E8 / 0xE2424: base cost x the age percentage, 100 for an unused object in its first 30 days); the Windows build's counterpart is not executed, and the object use counter (thing +412) is ParkObjectState.TimesUsed — evidence needed: the tp.exe counterpart of 0xE25E8 run in the x86 oracle, or a capture of a sale
		if ( !Catalog.TryGet( item.InfoId, out var info ) || info.Upgrades.Count == 0 )
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
	/// Park value (UITEXT 163): the sum over all objects of the catalogue costs up to the current level times the level's age
	/// percentage, which was the scrap value before ORACLE.md F21 — an <b>approximation</b>.
	/// </summary>
	// [APPROX:ECON-026] park value = sum of catalogue costs up to the current level times the age percentage (the scrap rule before ORACLE F21), not the sale refund — evidence needed: capture of the park value screen
	public long ParkValue => objects.Values.Sum( item =>
	{
		if ( !Catalog.TryGet( item.InfoId, out var info ) || info.Upgrades.Count == 0 )
			return 0;
		var level = Math.Min( item.Level, info.Upgrades.Count - 1 );
		var year = (int)Math.Min( 3, (ParkCalendar.Seconds( Tick ) - ParkCalendar.Seconds( item.BuiltTick )) / (365 * ParkCalendar.SecondsPerDay) );
		var percent = info.Upgrades[level].ScrapValuePercentByYear.Count > year ? info.Upgrades[level].ScrapValuePercentByYear[year] : 0;
		return info.Upgrades.Take( level + 1 ).Sum( upgrade => upgrade.CostOfUpgrade ) * percent / 100;
	} );

	public int LitterItems => (int)(LitterScaled / LitterScale);

	/// <summary>
	/// Park rating 0–100 (UITEXT 155): a sum of capped counts of guests, attractions and staff. It does
	/// not use guest happiness, litter or whether an attraction is open.
	/// </summary>
	// [BIN:STP-PPC:0x100C7B24 park rating] min(guests in park, 1000) × 20 / 1000; attractions of sub-kind 0 × 3 / 2 up to 20; sub-kinds 1 and 2 × 2 up to 10 each; sub-kind 3 up to 10; sub-kind 0 at upgrade level 2 or more up to 10; each of the five staff types up to 4
	public int ParkRating
	{
		get
		{
			var guests = Math.Min( guestStatistics.PeopleInPark, 1000 ) * 20 / 1000;
			// [APPROX:ECON-027] the record sub-kinds 0–3 are rides, shops, sideshows and features, and every hired staff member counts — evidence needed: the record field at +0x4C behind sub-kind +0x7A8 and the staff byte +3 tested by FUN_100C4064
			int Count( ParkObjectKind kind, int minimumLevel = 0 ) => objects.Values.Count( item => item.Kind == kind && item.Level >= minimumLevel );
			var attractions = Math.Min( Count( ParkObjectKind.Ride ) * 3 / 2, 20 )
				+ Math.Min( Count( ParkObjectKind.Shop ) * 2, 10 )
				+ Math.Min( Count( ParkObjectKind.Sideshow ) * 2, 10 )
				+ Math.Min( Count( ParkObjectKind.Feature ), 10 )
				+ Math.Min( Count( ParkObjectKind.Ride, 2 ), 10 );
			var staff = Enum.GetValues<StaffType>().Sum( type => Math.Min( Staff.Members.Count( member => member.Type == type ), 4 ) );
			return guests + attractions + staff;
		}
	}

	// ---------------------------------------------------------------- park and prices

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
		MissingTargetRide
	}

	/// <summary>
	/// Buys and registers an object. Requires research, <c>UsageInfo.GoldenTicketCost</c> golden tickets
	/// (spent) and, as an <b>approximation</b> of the original rule, a balance covering the cost.
	/// </summary>
	public PurchaseResult TryBuild( int infoId, out ParkObjectState? built )
	{
		built = null;
		var check = CheckBuild( infoId );
		if ( check != PurchaseResult.Ok )
			return check;
		Catalog.TryGet( infoId, out var info );
		var payWithTickets = PaysWithTickets( info );
		var cost = payWithTickets ? 0 : info.PurchaseCost;
		if ( payWithTickets )
		{
			TicketsSpent += info.GoldenTicketCost;
			ticketItems.Add( infoId );
		}
		Post( LedgerCategory.OtherCosts, cost );
		built = AddObject( info, imported: false );
		built.TotalSpent = cost;
		Raise( ParkEventKind.ObjectBuilt, cost, built.Id, infoId, info.Name );
		return PurchaseResult.Ok;
	}

	/// <summary>The purchase rules of <see cref="TryBuild"/> without buying (the build tool's ghost uses it).</summary>
	public PurchaseResult CheckBuild( int infoId )
	{
		if ( IsBankrupt )
			return PurchaseResult.Bankrupt;
		if ( !Catalog.TryGet( infoId, out var info ) )
			return PurchaseResult.UnknownObject;
		if ( !info.IsBuyable )
			return PurchaseResult.NotBuyable;
		if ( !Research.IsAvailable( infoId ) )
			return PurchaseResult.NotResearched;
		if ( info.Kind == ParkObjectKind.Upgrade && !objects.Values.Any( item => item.InfoId == info.AddOnTargetId ) )
			return PurchaseResult.MissingTargetRide;
		var payWithTickets = PaysWithTickets( info );
		if ( payWithTickets && info.GoldenTicketCost > GoldenTicketsAvailable )
			return PurchaseResult.NotEnoughGoldenTickets;
		// [APPROX:ECON-028] purchases need a balance covering the cost — evidence needed: capture of building with too little money
		if ( !payWithTickets && info.PurchaseCost > Balance )
			return PurchaseResult.NotEnoughMoney;
		return PurchaseResult.Ok;
	}

	// [BIN:STP-PPC:0x100DA874 object purchase] an object with a GoldenTicketCost (+0xC4) above 0 that the park has not yet bought with tickets costs that many tickets (0x100D3000; allowed when cost <= earned - spent) and no cash; it joins the owned set, and later copies and ticket-free objects take the cash path
	private bool PaysWithTickets( EconomyObjectInfo info ) =>
		Settings.CanSpendTickets && info.GoldenTicketCost > 0 && !ticketItems.Contains( info.InfoId );

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
	// [APPROX:ARRIVAL-003] kind 0 or 2 is read as Info.WhichUIType ride or sideshow; of the counts-test only Info.IsChoosable, IsBrokenDown (which also stands for the original's CONDEMNED, a breakdown at life gauge 0) and IsOpen (+0x64) are applied — evidence needed: world state for the entrance cell 0x100DD3F4 (a cell other than the u16 behind TOC -19648, zero in the file), the queue room 0x100DC988 (guests listed < 4 × queue cells), the coaster controller riders +0x140 (equal to the script's VAR_ONRIDE?) and the kart or water track word +0x28 (written only by the constructor, as 1), and whether a condemned ride stays condemned after a repair
	public int ParkPoints()
	{
		var counts = new Dictionary<int, int>();
		foreach ( var item in objects.Values )
			counts[item.InfoId] = counts.GetValueOrDefault( item.InfoId ) + 1;
		var points = 0;
		foreach ( var item in objects.Values )
		{
			if ( !Catalog.TryGet( item.InfoId, out var info ) || info.Kind is not (ParkObjectKind.Ride or ParkObjectKind.Sideshow) || !CountsForParkPoints( item, info ) )
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
	/// The part of the original's park points counts-test (0x100DC834) that OpenTPW has state for: the type is choosable, and the
	/// object is neither broken down (in the economy or by its script) nor closed. The test's world conditions are listed at <see cref="ParkPoints"/>.
	/// </summary>
	public static bool CountsForParkPoints( ParkObjectState item, EconomyObjectInfo info ) => info.IsChoosable && !item.IsBrokenDown && !item.ScriptBroken && item.IsOpen;

	/// <summary>Registers an object that already exists (imported original save, fixed item) without charging.</summary>
	/// <param name="builtTick">When the object was built, for an object from an original save (default: now).</param>
	/// <param name="timesUsed">Guests that used it before, for an object from an original save: a used object never refunds the unused 100 %.</param>
	public ParkObjectState RegisterExisting( int infoId, long? builtTick = null, long timesUsed = 0 )
	{
		if ( !Catalog.TryGet( infoId, out var info ) )
			throw new InvalidDataException( $"Info.Id {infoId} is not in the {Settings.Theme} object catalogue." );
		return AddObject( info, imported: true, builtTick, timesUsed );
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
			Price = info.PricePerUse ?? 0,
			CostOfGoods = info.CostOfGoods ?? 0,
			ChanceOfLosingPercent = info.ChanceOfLosingPercent ?? 0
		};
		objects.Add( item.Id, item );
		Counters.Add( ParkCounters.Built( info.InfoId ), 1 );
		// [BIN:STP-PPC:0x100DA874 object purchase] the type's park record +28 takes the current turn while it is still 0 (0x100DADEC..0x100DAE14); selling does not clear it
		// [APPROX:ARRIVAL-005] an object imported from an original save or placed as a fixed item also sets the first-purchase turn to the current turn; the original restores park record +28 from the save (0x100D1E8C) — evidence needed: park record +28 in an original save
		if ( Counters[ParkCounters.FirstPurchaseTurn( info.InfoId )] == 0 )
			Counters.Max( ParkCounters.FirstPurchaseTurn( info.InfoId ), Turn );
		return item;
	}

	/// <summary>Removes an object and credits its <see cref="ScrapValue"/>.</summary>
	public long Sell( int instanceId )
	{
		var item = RequireObject( instanceId );
		var value = ScrapValue( item );
		objects.Remove( instanceId );
		foreach ( var member in Staff.Members.Where( member => member.AssignedInstanceId == instanceId ) )
		{
			member.AssignedInstanceId = 0;
			member.BusyTicks = 0;
			member.State = StaffState.Patrolling;
		}
		Post( LedgerCategory.OtherIncome, value );
		Raise( ParkEventKind.ObjectSold, value, instanceId, item.InfoId );
		return value;
	}

	/// <summary>Removes an object without scrap credit (e.g. an unpaid prototype placement).</summary>
	public void Remove( int instanceId )
	{
		var item = RequireObject( instanceId );
		objects.Remove( instanceId );
		foreach ( var member in Staff.Members.Where( member => member.AssignedInstanceId == instanceId ) )
		{
			member.AssignedInstanceId = 0;
			member.BusyTicks = 0;
			member.State = StaffState.Patrolling;
		}
		Raise( ParkEventKind.ObjectSold, 0, instanceId, item.InfoId, "removed" );
	}

	/// <summary>Buys the next upgrade level of a ride; a mechanic installs it (UITEXT 367–372).</summary>
	public PurchaseResult TryBuyUpgrade( int instanceId )
	{
		if ( IsBankrupt )
			return PurchaseResult.Bankrupt;
		// [BIN:STP-PPC:0x10165A0C upgrade list] game type 2 (Instant Action) lists no upgrades and shows UITEXT 27 instead
		if ( !Features.Upgrades )
			return PurchaseResult.NotAvailableInInstantAction;
		var item = RequireObject( instanceId );
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
		var cost = info.Upgrades[level].CostOfUpgrade;
		if ( cost > Balance )
			return PurchaseResult.NotEnoughMoney;
		Post( LedgerCategory.OtherCosts, cost );
		item.TotalSpent += cost;
		item.PendingLevel = level;
		Raise( ParkEventKind.UpgradeBought, cost, item.Id, item.InfoId, $"level {level}" );
		return PurchaseResult.Ok;
	}

	public long CellCost( CellPurchase kind ) => kind switch
	{
		CellPurchase.Path => Settings.Costs.PathCell,
		CellPurchase.Queue => Settings.Costs.QueueCell,
		CellPurchase.KartTrack => Settings.Costs.KartTrackCell,
		CellPurchase.WaterTrack => Settings.Costs.WaterTrackCell,
		CellPurchase.Land => Settings.Costs.MapCell,
		_ => throw new ArgumentOutOfRangeException( nameof( kind ) )
	};

	/// <summary>Charges <c>Costs.*</c> for <paramref name="count"/> cells of paths, queues, tracks or land.</summary>
	public PurchaseResult TryBuyCells( CellPurchase kind, int count )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( count );
		if ( IsBankrupt )
			return PurchaseResult.Bankrupt;
		var cost = CellCost( kind ) * count;
		if ( cost > Balance )
			return PurchaseResult.NotEnoughMoney;
		Post( LedgerCategory.OtherCosts, cost );
		Raise( ParkEventKind.CellsBought, cost, 0, (int)kind, $"{count} {kind} cells" );
		return PurchaseResult.Ok;
	}

	/// <summary>
	/// The commit's money test (<c>SetCellType</c>, PATH-plan §3.2): whether <see cref="TrySpendCell"/> accepts one
	/// cell, balance − cost ≥ 0 as a wrapping 32-bit difference of the original's signed balance word.
	/// </summary>
	// [BIN:STP-PPC:0x10082BC8 SetCellType money test] subf balance − cost in 32 bits, then the sign bit: balance −2^31 with cost 1 wraps to affordable, balance 2^31 − 1 with cost −1 to refused
	public bool CanSpendCell( CellPurchase kind ) => unchecked( (int)Balance - (int)CellCost( kind ) ) >= 0;

	/// <summary>
	/// The preview's money test (the placement validator, PATH-plan §3.3): <paramref name="pendingLineCost"/> is the
	/// line's cost so far with this cell included, summed in 32 bits. A zero sum passes; otherwise the line is refused
	/// when the sum is greater than the balance (a signed compare, no subtraction). It differs from the commit's
	/// <see cref="CanSpendCell"/> only at the 32-bit edges and for a zero sum.
	/// </summary>
	// [BIN:STP-PPC:0x1007BCC0 pending line cost] add (no overflow trap) of the cell price; [BIN:STP-PPC:0x10084534 placement validator] a zero pending cost skips the test; [BIN:STP-PPC:0x10084550 placement validator] cmpw pending, balance and refuse when greater
	public bool CanPreviewLine( long pendingLineCost )
	{
		var pending = unchecked( (int)pendingLineCost );
		return pending == 0 || pending <= unchecked( (int)Balance );
	}

	/// <summary><see cref="CanPreviewLine"/> for <paramref name="cells"/> cells of one kind (this cell included).</summary>
	public bool CanPreviewCells( CellPurchase kind, int cells ) => CanPreviewLine( unchecked( cells * CellCost( kind ) ) );

	/// <summary>
	/// Charges one path or queue cell when it is written (<c>SetCellType</c>, PATH-plan §3.2): refused only
	/// when balance − cost &lt; 0 (no bankruptcy test), then spent as other costs. A negative cost (modified
	/// <c>Costs.*</c>) raises the balance by its amount, as the original's Spend of a negative amount does.
	/// </summary>
	// [BIN:STP-PPC:0x10082AC4 SetCellType] type 1 costs Costs.PathCell, type 3 Costs.QueueCell; refused when balance − cost < 0, else Spend per cell at write time
	public PurchaseResult TrySpendCell( CellPurchase kind )
	{
		if ( !CanSpendCell( kind ) )
			return PurchaseResult.NotEnoughMoney;
		var cost = CellCost( kind );
		// [APPROX:ECON-048] a negative cell cost is booked as other income (same balance and profit as the original's Spend of a negative amount; the month category is not traced) — evidence needed: the ledger category of a negative Spend
		if ( cost >= 0 )
			Post( LedgerCategory.OtherCosts, cost );
		else
			Post( LedgerCategory.OtherIncome, -cost );
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
	/// Refunds a removed queue cell: <c>Costs.QueueCell</c> × the ride's scrap percentage / 100
	/// (<c>ClearCell</c> queue case, PATH-plan §6). Returns the amount credited.
	/// </summary>
	// [BIN:STP-PPC:0x10085DCC ClearCell queue refund] Earn(QueueCost × 0xe2424(ride) / 100), 0xe2424 = the ride's age-based scrap percentage
	public long RefundQueueCell( int rideInstanceId )
	{
		var amount = TryGetObject( rideInstanceId, out var ride ) ? CellCost( CellPurchase.Queue ) * ScrapPercent( ride! ) / 100 : 0;
		if ( amount > 0 )
			Post( LedgerCategory.OtherIncome, amount );
		return amount;
	}

	// ---------------------------------------------------------------- loans

	public LoanAccount TakeLoan( int offerIndex )
	{
		var offer = AvailableLoans.FirstOrDefault( item => item.Index == offerIndex ) ?? throw new InvalidOperationException( $"Loan offer {offerIndex} is not available." );
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
		if ( account.RemainingBalance > Balance )
			return false;
		Post( LedgerCategory.LoanPayments, account.RemainingBalance );
		// [BIN:STP-PPC:0x100CCC78 loan payoff] months repaid are cleared before they are read again, so the profit is charged the share of the whole term (q x N), even after a partial repayment (ORACLE F13)
		Ledger.ChargeLoanInterest( LoanMath.PayoffInterest( account.MonthlyRepayment, account.Months, account.OriginalAmount ) );
		loans.Remove( account );
		takenOffers.Remove( offerIndex );
		Raise( ParkEventKind.LoanRepaid, account.RemainingBalance, 0, offerIndex );
		return true;
	}

	// ---------------------------------------------------------------- staff and research

	public StaffMember Hire( int candidateId )
	{
		if ( IsBankrupt )
			throw new InvalidOperationException( "A bankrupt park cannot hire staff." );
		var member = Staff.Hire( candidateId, Tick );
		member.State = StaffState.Patrolling;
		Raise( ParkEventKind.StaffHired, Staff.MonthlyWage( member ), member.Id, (int)member.Type );
		return member;
	}

	public void Fire( int staffId )
	{
		var member = Staff.Fire( staffId );
		if ( objects.TryGetValue( member.AssignedInstanceId, out var item ) )
			item.MechanicId = 0;
		Raise( ParkEventKind.StaffFired, 0, member.Id, (int)member.Type );
	}

	public void SetTrainingBudget( StaffType type, long monthlyBudget ) => Staff.SetTrainingBudget( type, monthlyBudget );

	/// <summary>Research lab effort; refused in Instant Action, whose research panel does not open (UITEXT 467).</summary>
	public void SetResearchEffort( ResearchCategory category, int effort )
	{
		if ( !Features.ResearchPanel )
			throw new InvalidOperationException( "Research is automatic in Instant Action: the research lab cannot be changed." );
		Research.SetEffort( category, effort );
	}

	public void AcceptChallenge()
	{
		var definition = Objectives.CurrentDefinition ?? throw new InvalidOperationException( "No challenge is offered." );
		var day = ParkCalendar.DayIndex( Tick );
		Objectives.Accept( day, MeasureChallenge( definition ) );
		Raise( ParkEventKind.ChallengeAccepted, definition.Prize, 0, definition.Index );
	}

	public void DeclineChallenge()
	{
		var index = Objectives.Decline( ParkCalendar.DayIndex( Tick ) );
		Raise( ParkEventKind.ChallengeDeclined, 0, 0, index );
	}

	// ---------------------------------------------------------------- guests

	public bool TryAdmitVisitor( long visitorCash, out int feePaid )
	{
		feePaid = 0;
		if ( !IsParkOpen || IsBankrupt || visitorCash < EntranceFee )
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
		Post( LedgerCategory.ShopTakings, item.Price );
		Post( LedgerCategory.OtherCosts, item.CostOfGoods );
		item.CustomersThisMonth++;
		item.TakingsThisMonth += item.Price;
		item.CostsThisMonth += item.CostOfGoods;
		item.TotalProfit += item.Price - item.CostOfGoods;
		if ( Catalog.TryGet( item.InfoId, out var info ) )
		{
			Counters.Add( ParkCounters.Sold( info.ShopType, info.SpecialIngredient ), 1 );
			LitterScaled += info.LitterEffect;
		}
		Counters.Add( ParkCounters.ShopProfit, item.Price - item.CostOfGoods );
		Raise( ParkEventKind.GuestPaid, item.Price, item.Id, item.InfoId, nameof( LedgerCategory.ShopTakings ) );
		return true;
	}

	public bool PlaySideshow( int instanceId, long visitorCash, out int pricePaid, out bool won )
	{
		pricePaid = 0;
		won = false;
		if ( !objects.TryGetValue( instanceId, out var item ) || item.Kind != ParkObjectKind.Sideshow || !item.IsOpen || IsBankrupt || visitorCash < item.Price )
			return false;
		pricePaid = item.Price;
		won = !Random.Chance( item.ChanceOfLosingPercent );
		var prize = won ? item.CostOfGoods : 0;
		Post( LedgerCategory.SideshowTakings, item.Price );
		Post( LedgerCategory.OtherCosts, prize );
		item.CustomersThisMonth++;
		item.TakingsThisMonth += item.Price;
		item.CostsThisMonth += prize;
		item.TotalProfit += item.Price - prize;
		if ( won )
			item.WinnersThisMonth++;
		Counters.Add( ParkCounters.SideshowProfit, item.Price - prize );
		Raise( ParkEventKind.GuestPaid, item.Price, item.Id, item.InfoId, nameof( LedgerCategory.SideshowTakings ) );
		return true;
	}

	/// <summary>Rides have no per-ride price in the original data; use is counted for statistics and challenges.</summary>
	public void RecordRideUse( int instanceId )
	{
		if ( !objects.TryGetValue( instanceId, out var item ) )
			return;
		item.CustomersThisMonth++;
		Counters.Add( ParkCounters.RideUses( item.InfoId ), 1 );
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

	public void ReportRecord( ParkRecordKind kind, int instanceId, long value )
	{
		var infoId = objects.TryGetValue( instanceId, out var item ) ? item.InfoId : 0;
		Counters.Max( ParkCounters.Record( kind, infoId ), value );
	}

	/// <summary>Adds litter (in 1/<see cref="LitterScale"/> items) reported by guests or pranksters.</summary>
	public void AddLitter( int scaledAmount ) => LitterScaled += Math.Max( 0, scaledAmount );

	public void SetObjectOpen( int instanceId, bool open ) => RequireObject( instanceId ).IsOpen = open;

	// ---------------------------------------------------------------- save support

	internal IReadOnlyCollection<int> TakenOffers => takenOffers;

	internal void RestoreTicketItems( IEnumerable<int> items )
	{
		ticketItems.Clear();
		foreach ( var item in items )
			ticketItems.Add( item );
	}

	// ---------------------------------------------------------------- original save import

	/// <summary>
	/// Moves a park that has not run yet to an original save's park turn (<c>mGameTick</c>, the turn the calendar
	/// converts, so the date matches the save's). The ledger month, the hiring pool's timers and the first challenge
	/// offer day move with it.
	/// </summary>
	internal void StartAtOriginalTurn( long turn )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( turn );
		if ( Tick != 0 || objects.Count != 0 || Ledger.History.Count != 0 )
			throw new InvalidOperationException( "Only a park that has not run can start at an original save's turn." );
		Tick = ParkCalendar.TickOfTurn( turn );
		Ledger.Restore( Ledger.Balance, Ledger.CurrentOpeningBalance, ParkCalendar.MonthIndex( Tick ), new Dictionary<LedgerCategory, long>(), Array.Empty<LedgerMonth>() );
		Staff.ShiftSchedule( Tick );
		// [APPROX:SAVE-005] the challenge system's next-offer time, current challenge, three switch bytes, Value and ActualThing are not imported; the first offer day is counted from the imported day — evidence needed: the unit of the challenge system's next event time and the meaning of its switch bytes, Value and ActualThing
		Objectives.ShiftSchedule( ParkCalendar.DayIndex( Tick ) );
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

	internal void RestoreState( long tick, GameSpeed speed, bool open, int fee, bool bankrupt, int monthsInRed, long litter, int ticketsSpent, int nextObjectId,
		long droppedAdmissions, IEnumerable<ParkObjectState> restoredObjects, IEnumerable<LoanAccount> restoredLoans, IEnumerable<int> restoredOffers, IEnumerable<long> restoredAdmissions )
	{
		Tick = tick;
		Speed = speed;
		IsParkOpen = open;
		EntranceFee = fee;
		IsBankrupt = bankrupt;
		MonthsInRed = monthsInRed;
		LitterScaled = litter;
		TicketsSpent = ticketsSpent;
		NextObjectId = nextObjectId;
		DroppedAdmissions = droppedAdmissions;
		objects.Clear();
		foreach ( var item in restoredObjects )
		{
			if ( !Catalog.TryGet( item.InfoId, out _ ) )
				throw new InvalidDataException( $"Saved object {item.Id} has Info.Id {item.InfoId}, which the {Settings.Theme} catalogue does not declare." );
			objects.Add( item.Id, item );
		}
		loans.Clear();
		loans.AddRange( restoredLoans );
		takenOffers.Clear();
		takenOffers.UnionWith( restoredOffers );
		monthlyAdmissions.Clear();
		monthlyAdmissions.AddRange( restoredAdmissions );
	}
}
