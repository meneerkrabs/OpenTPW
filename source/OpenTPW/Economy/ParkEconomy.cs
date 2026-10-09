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
	// [APPROX:ECON-020] a sale drops LitterEffect/100 litter items — evidence needed: capture of litter after sales
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
	public int NextObjectId { get; private set; } = 1;
	public IReadOnlyCollection<ParkObjectState> Objects => objects.Values;
	public IReadOnlyList<LoanAccount> Loans => loans;
	public IReadOnlyList<long> MonthlyAdmissions => monthlyAdmissions;
	public int GoldenTicketsAvailable => Objectives.GoldenTickets.Count - TicketsSpent;
	public IParkGuestStatistics GuestStatistics { get => guestStatistics; set => guestStatistics = value ?? NoGuestStatistics.Instance; }

	public event Action<ParkEvent>? EventRaised;

	/// <summary>Loan offers that can still be taken; none in Instant Action (UIHELPTEXT 493).</summary>
	// [BIN:STP-PPC:0x10154AA0 loans window] the Available Loans window only opens outside game type 2 (Instant Action)
	public IEnumerable<LoanOffer> AvailableLoans => Mode == ParkGameMode.InstantAction
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

	/// <summary>Advances the simulation by <paramref name="ticks"/> park ticks, running hourly, daily and monthly updates at their boundaries.</summary>
	public void Advance( long ticks )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( ticks );
		// [APPROX:ECON-030] the simulation stops once bankrupt — evidence needed: capture of the bankrupt state
		for ( long step = 0; step < ticks && !IsBankrupt; step++ )
		{
			Tick++;
			if ( Tick % ParkCalendar.TicksPerHour == 0 )
				UpdateHour();
			if ( Tick % ParkCalendar.TicksPerDay != 0 )
				continue;
			var day = ParkCalendar.DayIndex( Tick );
			EndDay( day - 1 );
			if ( day % ParkCalendar.DaysPerMonth == 0 )
				EndMonth( day / ParkCalendar.DaysPerMonth );
		}
	}

	public void AdvanceDays( int days ) => Advance( (long)days * ParkCalendar.TicksPerDay );

	private void UpdateHour()
	{
		Staff.UpdatePool( Random, Tick );
		foreach ( var member in Staff.Members )
		{
			if ( member.BusyTicks == 0 )
				continue;
			member.BusyTicks = Math.Max( 0, member.BusyTicks - ParkCalendar.TicksPerHour );
			if ( member.BusyTicks == 0 )
				FinishJob( member );
		}
		DispatchMechanics();
		CleanLitter();
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
			// [APPROX:ECON-024] a repair restores state of repair to 100 — evidence needed: capture after a repair
			item.StateOfRepair = 100;
			item.IsBrokenDown = false;
			Raise( ParkEventKind.RideRepaired, 0, item.Id, item.InfoId );
		}
	}

	/// <summary>
	/// Assigns free mechanics: broken rides first, then paid upgrades, then worn rides. A job takes
	/// <c>MechanicConstsPerGrade[grade].WorkDuration</c> game hours (× <c>DurationOfUpgrade</c> for
	/// upgrades) — the unit is an <b>approximation</b>.
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
			// [APPROX:ECON-021] a repair takes WorkDuration game hours (x DurationOfUpgrade for upgrades); mechanics are dispatched instantly — evidence needed: capture of repair duration per grade
			var hours = (long)role.WorkDuration[mechanic.Grade];
			if ( job.PendingLevel > job.Level && Catalog.TryGet( job.InfoId, out var info ) && job.PendingLevel < info.Upgrades.Count )
				hours *= Math.Max( 1, info.Upgrades[job.PendingLevel].DurationOfUpgrade );
			mechanic.BusyTicks = Math.Max( 1, hours ) * ParkCalendar.TicksPerHour;
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
		foreach ( var item in objects.Values.Where( item => item.Kind == ParkObjectKind.Ride && item.IsOpen && !item.IsBrokenDown && item.MechanicId == 0 ) )
		{
			if ( !Catalog.TryGet( item.InfoId, out var info ) || info.Upgrades.Count == 0 )
				continue;
			var wear = info.Upgrades[Math.Min( item.Level, info.Upgrades.Count - 1 )].WearRate;
			if ( wear <= 0 )
				continue;
			var before = item.StateOfRepair;
			// [APPROX:ECON-023] an open ride loses WearRate state of repair per game day; breakdown at 0 — evidence needed: capture of state of repair over time
			item.StateOfRepair = Math.Max( 0, before - wear );
			if ( item.StateOfRepair == 0 )
			{
				item.IsBrokenDown = true;
				Raise( ParkEventKind.RideBrokeDown, 0, item.Id, item.InfoId );
			}
			else if ( before >= WornStateOfRepair && item.StateOfRepair < WornStateOfRepair )
				Raise( ParkEventKind.RideWorn, item.StateOfRepair, item.Id, item.InfoId );
		}
		var points = Research.DailyPoints( Staff.OfType( StaffType.Researcher ), Mode == ParkGameMode.InstantAction );
		foreach ( var item in Research.AdvanceDay( points ) )
		{
			Counters.Add( ParkCounters.Researched( item.Category ), 1 );
			Raise( ParkEventKind.ItemResearched, item.Level, 0, item.InfoId, Catalog.TryGet( item.InfoId, out var info ) ? info.Name : "" );
		}
		foreach ( var (kind, index, amount, detail) in Objectives.AdvanceDay( day + 1, MeasureChallenge ).ToList() )
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
		for ( var index = loans.Count - 1; index >= 0; index-- )
		{
			var (account, paid) = LoanMath.Pay( loans[index] );
			Post( LedgerCategory.LoanPayments, paid );
			Raise( ParkEventKind.LoanPayment, paid, 0, account.OfferIndex );
			if ( account.MonthsRemaining == 0 || account.RemainingBalance <= 0 )
			{
				loans.RemoveAt( index );
				// [BIN:STP-PPC:0x100CC21C loan instalment] a fully repaid loan clears its bought flag; 0x100CC9E8 then offers it again when the credit test passes
				// [APPROX:ECON-007] reopening has no original credit-eligibility gate — evidence needed: implement the traced credit predicate and qualify its cross-edition behavior
				takenOffers.Remove( account.OfferIndex );
				Raise( ParkEventKind.LoanRepaid, 0, 0, account.OfferIndex );
			}
			else
				loans[index] = account;
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
		// [APPROX:ECON-033] golden tickets are checked at each month end — evidence needed: capture of the award timing
		foreach ( var ticket in Objectives.CheckGoldenTickets( Counters, guestStatistics, Research, Ledger.History, monthlyAdmissions ) )
			Raise( ParkEventKind.GoldenTicketWon, (int)ticket, 0, 0, ticket.ToString() );
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
	/// Scrap value: the purchase and upgrade costs up to the current level times
	/// <c>Upgrades[level].ScrapValueYearN</c> percent for the object's age (year 4 onwards uses year 4).
	/// Basing it on catalogue costs rather than money paid is an <b>approximation</b>.
	/// </summary>
	public long ScrapValue( ParkObjectState item )
	{
		if ( !Catalog.TryGet( item.InfoId, out var info ) || info.Upgrades.Count == 0 )
			return 0;
		var level = Math.Min( item.Level, info.Upgrades.Count - 1 );
		// [APPROX:ECON-025] scrap value basis = catalogue cost of all levels up to the current one — evidence needed: capture of scrap value
		var basis = info.Upgrades.Take( level + 1 ).Sum( upgrade => upgrade.CostOfUpgrade );
		var year = (int)Math.Min( 3, (Tick - item.BuiltTick) / ((long)ParkCalendar.DaysPerYear * ParkCalendar.TicksPerDay) );
		var percent = info.Upgrades[level].ScrapValuePercentByYear.Count > year ? info.Upgrades[level].ScrapValuePercentByYear[year] : 0;
		return basis * percent / 100;
	}

	/// <summary>Park value (UITEXT 163): the sum of all scrap values — an <b>approximation</b>.</summary>
	// [APPROX:ECON-026] park value = sum of scrap values — evidence needed: capture of the park value screen
	public long ParkValue => objects.Values.Sum( ScrapValue );

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
		if ( Settings.CanSpendTickets && info.GoldenTicketCost > GoldenTicketsAvailable )
			return PurchaseResult.NotEnoughGoldenTickets;
		// [APPROX:ECON-028] purchases need a balance covering the cost — evidence needed: capture of building with too little money
		if ( info.PurchaseCost > Balance )
			return PurchaseResult.NotEnoughMoney;
		if ( Settings.CanSpendTickets )
			// [APPROX:ECON-029] golden tickets are spent when buying items with GoldenTicketCost — evidence needed: capture of ticket count after such a purchase
			TicketsSpent += info.GoldenTicketCost;
		Post( LedgerCategory.OtherCosts, info.PurchaseCost );
		built = AddObject( info, imported: false );
		built.TotalSpent = info.PurchaseCost;
		Raise( ParkEventKind.ObjectBuilt, info.PurchaseCost, built.Id, infoId, info.Name );
		return PurchaseResult.Ok;
	}

	/// <summary>Registers an object that already exists (imported original save, fixed item) without charging.</summary>
	public ParkObjectState RegisterExisting( int infoId )
	{
		if ( !Catalog.TryGet( infoId, out var info ) )
			throw new InvalidDataException( $"Info.Id {infoId} is not in the {Settings.Theme} object catalogue." );
		return AddObject( info, imported: true );
	}

	private ParkObjectState AddObject( EconomyObjectInfo info, bool imported )
	{
		var item = new ParkObjectState
		{
			Id = NextObjectId++,
			InfoId = info.InfoId,
			Kind = info.Kind,
			BuiltTick = Tick,
			Imported = imported,
			Price = info.PricePerUse ?? 0,
			CostOfGoods = info.CostOfGoods ?? 0,
			ChanceOfLosingPercent = info.ChanceOfLosingPercent ?? 0
		};
		objects.Add( item.Id, item );
		Counters.Add( ParkCounters.Built( info.InfoId ), 1 );
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
		if ( Mode == ParkGameMode.InstantAction )
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

	/// <summary>Repays a loan's remaining balance in full; requires the money (UITEXT 182).</summary>
	public bool TryRepayLoan( int offerIndex )
	{
		var account = loans.FirstOrDefault( item => item.OfferIndex == offerIndex ) ?? throw new InvalidOperationException( $"Loan {offerIndex} is not outstanding." );
		if ( account.RemainingBalance > Balance )
			return false;
		Post( LedgerCategory.LoanPayments, account.RemainingBalance );
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

	public void SetResearchEffort( ResearchCategory category, int effort ) => Research.SetEffort( category, effort );

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
