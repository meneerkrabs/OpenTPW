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
	/// <summary>The bank's month in the red: 30 days of park time (tp.exe <c>0x004D0370</c> divides by <c>0x1792F8648000</c>, 30 × 864e9 in 100 ns units).</summary>
	public const long SecondsPerMonthInRed = 30 * ParkCalendar.SecondsPerDay;

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
		Litter.Environment.Effects = RegionEffect.Load( settings.Standard );
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
	/// <summary>Whole 30-day months since the park went into the red, as the last month end counted them (0 in the black).</summary>
	public int MonthsInRed { get; private set; }
	/// <summary>The bank's <c>mWithdrawalsEnabled</c> (+0x114): when clear, a debit charges nothing (tp.exe <c>0x004D01F0</c>). Set in every shipped save.</summary>
	public bool WithdrawalsEnabled { get; private set; } = true;
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

	/// <summary>Advances the simulation by <paramref name="ticks"/> fixed ticks. Each new park turn runs the turn work and the hourly, daily and monthly updates for the calendar boundaries it crosses.</summary>
	public void Advance( long ticks )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( ticks );
		// A bankrupt park stops (ECON-030, EndMonth)
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
	/// <summary>
	/// The host's coasters for challenge type 18: the most counted sections (the rating's sections with flags 0x40 and 0x80) of the coasters of an Info.Id,
	/// any coaster for 0, and 0 when none matches. Null in a headless economy, which has no coasters (type 18 then measures 0).
	/// </summary>
	public Func<int, long>? CoasterCountedSections { get; set; }
	/// <summary>New mechanic jobs await this host world; null keeps new headless jobs immediate.</summary>
	public IMechanicJobWorld? MechanicJobWorld { get; set; }
	/// <summary>The world that moves the handymen and runs their state machine; null keeps the economy's own dispatch (no positions).</summary>
	public IStaffWorld? StaffWorld { get; set; }
	// A mechanic's BusyTicks is the number of park turns (staff updates) until its job completes.

	public long Turn => ParkCalendar.Turn( Tick );

	private void AdvanceTurn( long previousTurn, long turn )
	{
		UpdateStaffVitals();
		// [BIN:STP-PPC:0x100D9E04 repair state] each staff update counts a mechanic's job timer down by one; the update that finds it zero completes the repair
		foreach ( var member in Staff.Members )
		{
			if ( member.BusyTicks == 0 )
				continue;
			if ( member.AwaitingJobArrival )
			{
				if ( OwnsMechanicJob( member ) && MechanicJobWorld?.HasArrived( member ) == true )
					member.AwaitingJobArrival = false;
				// Host policy: the arrival-observation update starts work; following updates consume the reserved timer.
				continue;
			}
			member.BusyTicks--;
			if ( member.BusyTicks == 0 )
			{
				FinishJob( member );
				StrikeOrRest( member, HasRestArea );
			}
		}
		if ( StaffWorld != null )
			StaffWorld.UpdateStaff( this, turn );
		else
			DispatchHandymen();
		// [BIN:STP-PPC:0x100F0284 researcher update] each researcher not in an excluded state does research when the turn counter is a multiple of 20
		if ( turn % ParkResearch.TurnsPerResearch == 0 )
			DoResearch();
		// [BIN:STP-PPC:0x100E077C ride update] each ride is updated when the turn counter is a multiple of 8
		if ( turn % RideUpdateInterval == 0 )
			UpdateRides( turn );
		// [BIN:STP-PPC:0x100E3F0C calendar update] day, month and year events fire when the extracted date fields change from those of the previous turn (ParkCalendar.Update)
		var stored = ParkCalendar.FieldsAtTurn( previousTurn );
		var changes = ParkCalendar.Update( ref stored, turn );
		var hourBefore = previousTurn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerHour;
		for ( var hour = hourBefore + 1; hour <= turn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerHour && !IsBankrupt; hour++ )
			UpdateHour();
		if ( changes.HasFlag( CalendarChange.Day ) )
			EndObjectDay();
		if ( changes.HasFlag( CalendarChange.Day ) && !IsBankrupt )
			EndDay( previousTurn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay );
		if ( changes.HasFlag( CalendarChange.Month ) )
			EndMonth( (stored.Year - ParkCalendar.Epoch.Year) * ParkCalendar.MonthsPerYear + stored.Month );
		if ( changes.HasFlag( CalendarChange.Year ) )
			Ledger.StartYear();
		// [BIN:STP-PPC:0x100D67F0 world update] after the calendar update (whose messages the bank, the staff and the other listeners handle at once) the golden-ticket checks (0x100D31D0, 0x100D34C0, 0x100D35B4) run when the turn counter is a multiple of 100, and only in Full Simulation (game type 0)
		if ( RunsGoldenTicketCheck( turn, Mode ) )
			CheckGoldenTickets();
	}

	/// <summary>
	/// Staff vitals, once per park turn (ECON-014, docs/reverse/ORACLE.md F22). A member on strike waits for its role's strike to end; a resting
	/// one regains energy and happiness until the energy byte reaches 100; a working one (a mechanic on a job) loses the working rate; a free one
	/// goes on strike when its role does, otherwise a walking one loses the moving rate and, with the energy at the rest level, goes to rest
	/// (when a rest area exists) or loses happiness one turn in sixteen (when none does).
	/// </summary>
	// [APPROX:ECON-014] staff vitals and strikes follow the traced routines (tp.exe month gate 0x508E70, escalation 0x508F70, grievance 0x509360; the month message comes from the calendar update 0x4F8260 with no game-type test, and the age stamp 0x4F8800 counts floor(turn × 3750) seconds from world turn 0, both as here); without a staff world (a headless economy) they skip the state machine: walking members drain every turn, working means a job in progress, resting and striking need no walk, idle members are not told apart and the rest search runs every turn; the month gate skips a month while any guest is in the park by state, where the original counts the guests standing on a cell of type 0, 1, 3, 9 or 10 in a census reused for up to 23 turns (0x4C7FA0); the handymen's litter share counts the live map (ECON-020); the forced-strike word (zero at creation 0x5089FE, written only by the save 0x508BB0) is not imported (SAVE-009) — evidence needed: none for the routines (read); the census needs the original cell types (ECON-050)
	private void UpdateStaffVitals()
	{
		if ( Staff.Members.Count == 0 )
			return;
		var restArea = HasRestArea;
		foreach ( var member in Staff.Members )
		{
			// A member a world drives walks to its rest area or the strike area and rests or strikes in its own state machine (the generic handlers
			// 0x00505FE0, 0x005061D0, 0x005056E0, 0x00506300); only the role's strike ends here, when a striker sees the park closed and empty.
			if ( DrivenByWorld( member ) )
			{
				if ( member.State == StaffState.OnStrike && !IsParkOpen && guestStatistics.PeopleInPark == 0 )
					Staff.Strikes.End( member.Type );
				continue;
			}
			switch ( member.State )
			{
				case StaffState.PickedUp:
					continue;
				case StaffState.OnStrike:
					// [BIN:STP-PPC:0x100F3F64 strike turn] a striking member that sees the park closed and without guests (0x100C3684) ends its role's strike (0x100F40E8 / 0x100F8BD0); once the role's flag is clear it walks back to work
					if ( !IsParkOpen && guestStatistics.PeopleInPark == 0 )
						Staff.Strikes.End( member.Type );
					if ( !Staff.Strikes.IsActive( member.Type ) )
						member.State = StaffState.Patrolling;
					continue;
				case StaffState.Resting:
					// [BIN:STP-PPC:0x100F4D00 rest release] the original frees the member's rest-area association (+516) when the rest ends; what it does when the rest thing is removed during the rest is not traced, so OpenTPW ends the rest at once when no rest area is left
					if ( !restArea )
					{
						member.State = StaffState.Patrolling;
						continue;
					}
					if ( StaffVitals.Rest( member, Settings.RecuperationRate[member.Grade], Settings.HappinessRecuperationRate[member.Grade] ) )
						member.State = StaffState.Patrolling;
					continue;
			}
			if ( member.BusyTicks > 0 )
			{
				StaffVitals.Work( member );
				continue;
			}
			if ( member.State == StaffState.Patrolling && !Staff.Strikes.IsActive( member.Type ) )
				StaffVitals.Walk( member );
			StrikeOrRest( member, restArea );
		}
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
		int Next( int id ) => list.IndexOf( id ) is var index and >= 0 && index + 1 < list.Count ? list[index + 1] : 0;
		var cursor = MechanicCursor;
		cursor = cursor != 0 && list.Contains( cursor ) ? Next( cursor ) : list.Count > 0 ? list[0] : 0;
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
	internal void FinishMechanicJob( StaffMember member )
	{
		member.AssignedInstanceId = member.RideId;
		member.RideId = 0;
		var state = member.State;
		FinishJob( member );
		member.State = state;
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
	// Approximation (GUEST-006, registered at UpdateNeedsTurn): a load puts back the row the object's saved state implies (a dirty toilet its row 6, a spent show none), where the original's constructor always adds the clean, camera or fireworks row
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
			item.Repair = RideRepair.FullGauge;
			item.MechanicId = 0;
			var called = item.MaintenanceRequested;
			item.MaintenanceRequested = false;
			Raise( ParkEventKind.ToiletCleaned, called ? 1 : 0, item.Id, item.InfoId );
		}
		member.ToiletId = 0;
	}

	/// <summary>Whether any placed object is a rest area for staff (<c>UsageInfo.ChillsYouOut</c>, in the shipped data the Staff Room).</summary>
	private bool HasRestArea => objects.Values.Any( item => Catalog.TryGet( item.InfoId, out var info ) && info.ChillsYouOut );

	/// <summary>
	/// What a free member does next (0x100F4900): a member of a striking role walks out, otherwise a tired one goes to rest when a rest area
	/// exists. True when its state changed. Runs every turn for free members and once when a job ends, before the next job is dispatched
	/// (the original's working member finishes its job first, then reaches this decision).
	/// </summary>
	// [BIN:STP-PPC:0x100F4900 strike or rest] a free member of a striking role walks to the strike area (raw state 4); the original also needs the world state 0x1091B8 to be 1, which is not modelled
	// [APPROX:DET-015] the world draw of the no-rest-area happiness hit (0x100F4AD4 takes it from the shared world LCG 0x105328, which OpenTPW has not ported) comes from the economy's own stream
	private bool StrikeOrRest( StaffMember member, bool restArea )
	{
		if ( Staff.Strikes.IsActive( member.Type ) )
		{
			member.State = StaffState.OnStrike;
			return true;
		}
		if ( !StaffVitals.SeekRest( member, Settings.RestLevel, Settings.HappyHitCosNoRestArea, restArea, () => (uint)Random.NextUInt64() ) )
			return false;
		member.State = StaffState.Resting;
		return true;
	}

	/// <summary>The staff manager's month-end consideration of each role's strike (ECON-014): <see cref="StaffStrikeBoard.ConsiderMonth"/> with the park's state.</summary>
	// [BIN:TPW-EXE:0x004F8260 month message] the calendar update sends the month message (kind 12, 0x0040F500) to the staff manager (0x00508A30 → 0x00508E70) with no game-type test, so strikes are considered in every mode; the stamp (0x004F8800) is floor(turn × rate / 4) × 10^7 with the calendar's base added and taken off again, the park clock from world turn 0
	// Approximation (ECON-014, registered at UpdateStaffVitals): the forced-strike word is never set (an original save's mForceStrike is not imported, SAVE-009), and the month gate takes the guests in the park by state for the cell census 0x004C7FA0
	// [BIN:TPW-EXE:0x00509360 handyman grievance] the handymen's litter grievance: the analyser's littered cells (0x004C8180) over its path, queue and type-9 cells (0x004C8150), as singles, above 0.2 (ParkLitter.Statistic)
	private void ConsiderStrikes()
	{
		var stamp = ParkCalendar.Seconds( Tick ) * StaffStrikeBoard.StampUnitsPerSecond;
		var messages = Staff.Strikes.ConsiderMonth( Turn, forced: false, IsParkOpen, guestStatistics.PeopleInPark, stamp,
			role => Staff.OfType( role ).Select( member => new StaffVital( member.Energy, member.Happiness ) ).ToList(), Litter.Statistic );
		foreach ( var message in messages )
			Raise( ParkEventKind.StaffStrikeMessage, (int)message.Kind, 0, (int)message.Role, $"TAG_SYSTEM {message.TagSystemIndex}" );
	}

	/// <summary>Whether the world update of a park turn runs the golden-ticket checks: every 100th turn (the turn is unsigned) in Full Simulation only.</summary>
	// [BIN:STP-PPC:0x100D67F0 world update] turn % 100 == 0 (unsigned) calls the dispatcher 0x100D2F1C, which runs the checks only while the game type word is 0 (oracle F31: 507 scripts agree)
	public static bool RunsGoldenTicketCheck( long turn, ParkGameMode mode ) =>
		turn % GoldenTicketCheckInterval == 0 && ParkModeFeatures.For( mode ).GoldenTickets;

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
		foreach ( var ticket in Objectives.CheckGoldenTickets( Counters, guestStatistics, Research, Ledger.ProfitThisYear, monthlyAdmissions, DroppedAdmissions, ParkCalendar.Seconds( Tick ) ) )
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
	}

	/// <summary>
	/// The original ride update (docs/reverse/RIDE-WEAR.md, ORACLE.md F27): every 8 turns each ride that is open and not broken down runs the
	/// wear step, which acts on every 64th turn, and then the breakdown check.
	/// </summary>
	private void UpdateRides( long turn )
	{
		foreach ( var item in objects.Values.Where( item => item.Kind == ParkObjectKind.Ride ).ToList() )
		{
			// [BIN:STP-PPC:0x100E077C ride update] a ride in state 1 or 4 (broken down, condemned), a closed ride (open word +0x64 = 0, which state 2 clears too) and a ride on a turn that is not a multiple of 8 are not updated; a mechanic at work on a ride that is open and not broken (a repair of a worn ride) does not stop the update
			// [APPROX:ECON-023] the ride's state is derived from the economy (breakdown flag, pending upgrade) and the script's VAR_BROKEN instead of a state word: an economy breakdown (a life or repair byte of 0, relief objects exempt) marks the ride broken down at once, where the original only writes VAR_BREAKSTAT = 1 (0x004E0CA5 / 0x004E0CFA) and leaves it in state 0 until the script sets VAR_BROKEN (state 1, or 4 with a life byte of 0: 0x004E14E0); a breakdown that only the script reports gets no mechanic, where the original's state 1 gets one (0x004DAA90) and the repair waits for VAR_BROKEN to clear (0x004DA830); VAR_WORN is not written (the wear step 0x004DF670 sets it below a repair byte of 20, the update for relief objects below 25 at 0x004E0D16); only rides are updated, where the original updates every choosable object (state 0 at 0x004DB4FB): the shipped toilets are choosable with WearRate 0, so only their use (0x004E2440, modelled) wears them; state 2 comes only from the upgrade request 0x004E0050 (buy handler 0x004AF480) — evidence needed: the ride scripts' handling of VAR_BREAKSTAT and VAR_WORN (.rse data)
			var state = ObjectState( item );
			if ( !RideBreakdownRules.RunsWear( state, item.IsOpen && state != RideObjectStates.Upgrading, turn ) )
				continue;
			if ( turn % WearInterval == 0 )
				Wear( item );
			// [BIN:STP-PPC:0x100E077C breakdown check] a ride that is not already broken down breaks down when its truncated life gauge or state of repair is 0; when only the state of repair is 0 the life gauge first loses 5 (clamped to 0..100); 0x100E09F4 then sets VAR_BREAKSTAT; there is no random roll
			if ( !RideBreakdownRules.BreaksDown( false, 0, (float)item.LifeGauge, (float)item.Repair ) )
				continue;
			item.LifeGauge = RideBreakdownRules.LifeAfterBreakdown( (float)item.LifeGauge );
			item.IsBrokenDown = true;
			Raise( ParkEventKind.RideBrokeDown, 0, item.Id, item.InfoId );
		}
	}

	/// <summary>
	/// The ride object's state word as far as OpenTPW can tell it (<see cref="RideObjectStates"/>): broken down or condemned (the life gauge
	/// truncates to a zero byte) when the economy or the ride script reports a breakdown, upgrading while a purchased upgrade is not installed.
	/// </summary>
	internal static int ObjectState( ParkObjectState item )
	{
		if ( item.IsBrokenDown || item.ScriptBroken )
			return RideBreakdownRules.GaugeByte( (float)item.LifeGauge ) == 0 ? RideObjectStates.Condemned : RideObjectStates.BrokenDown;
		return item.PendingLevel > item.Level ? RideObjectStates.Upgrading : RideObjectStates.Idle;
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
		member.AwaitingJobArrival = false;
		member.State = StaffState.Patrolling;
		if ( member.LitterCell is { } litter )
		{
			// [BIN:TPW-EXE:0x004D76A0 sweep] when the turn passes the arrival stamp + WorkDuration[grade], 0x004D9620 removes the litter of the handyman's cell
			member.LitterCell = null;
			if ( Litter.CollectorAt( litter.X, litter.Y ) == member.Id )
				Litter.Remove( litter.X, litter.Y );
			return;
		}
		if ( !objects.TryGetValue( member.AssignedInstanceId, out var item ) )
		{
			member.AssignedInstanceId = 0;
			return;
		}
		member.AssignedInstanceId = 0;
		item.MechanicId = 0;
		if ( member.Type == StaffType.Handyman )
		{
			// [BIN:TPW-EXE:0x004DFD80 toilet cleaned] the end of the handyman's state 11 sets the gauge back to 100.0 and clears the ride's mRequestedService (+0x64) and assigned staff (+0x5E); a dirty toilet's row 6 turns back into row 1 first
			if ( IsDirtyToilet( item ) )
				SwapToiletEffect( item, RegionEffect.DirtyToilet, RegionEffect.CleanToilet );
			item.Repair = RideRepair.FullGauge;
			var called = item.MaintenanceRequested;
			item.MaintenanceRequested = false;
			Raise( ParkEventKind.ToiletCleaned, called ? 1 : 0, item.Id, item.InfoId );
			return;
		}
		// [BIN:TPW-EXE:0x004DF8F0 repaired fully] the repair or the upgrade clears the call (+0x64 = 0) and the claim (+0x5E)
		item.MaintenanceRequested = false;
		// 0xdef2c installs the upgrade only for a ride in state 2; a broken ride with a purchased upgrade is repaired first
		if ( ObjectState( item ) == RideObjectStates.Upgrading )
		{
			item.Level = item.PendingLevel;
			item.PendingLevel = 0;
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
	/// supplied by an optional host world (otherwise immediate).
	/// </summary>
	private void DispatchMechanics()
	{
		var jobs = objects.Values.Where( item => item.MechanicId == 0 && JobRank( item ) >= 0 )
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
	// Approximation (ECON-022, registered at StaffWorldSimulation.UpdateStaff): without a world the handymen have no position, so a free handyman takes the first unclaimed littered cell in the finder's scan order, park-wide, else the dirtiest toilet, timed from the dispatch
	private void DispatchHandymen()
	{
		var role = Settings[StaffType.Handyman];
		List<ParkObjectState>? dirty = null;
		using var litter = Litter.Littered().Where( cell => Litter.CollectorAt( cell.X, cell.Y ) == 0 ).GetEnumerator();
		foreach ( var handyman in Staff.OfType( StaffType.Handyman ).Where( member => member.IsAvailable && !Staff.Strikes.IsActive( member.Type ) && !(HasRestArea && StaffVitals.Byte( member.Energy ) <= Settings.RestLevel) ).OrderBy( member => member.Id ).ToList() )
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
			dirty ??= objects.Values.Where( item => item.MechanicId == 0 && Catalog.TryGet( item.InfoId, out var info ) && info.ProvidesRelief
				&& (QueueJoinRules.Truncate( (float)item.Repair ) & 0xFF) < DirtyToiletGauge ).OrderBy( item => item.Repair ).ThenBy( item => item.Id ).ToList();
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
		var challenges = Features.Challenges ? Objectives.AdvanceDay( day + 1, MeasureChallenge ).ToList() : new();
		foreach ( var (kind, index, amount, detail) in challenges )
		{
			if ( kind == ParkEventKind.ChallengeCompleted )
				Post( LedgerCategory.OtherIncome, amount );
			Raise( kind, amount, 0, index, detail );
		}
		Raise( ParkEventKind.DayEnded, Balance );
	}

	private long MeasureChallenge( ChallengeDefinition definition ) => Objectives.Measure( definition, Counters, Staff, objects.Values, guestStatistics, CoasterCountedSections );

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
			// [BIN:TPW-EXE:0x00505A10 staff training] the training is a debit (0x004D01F0, money out) added to its own analyser word +0x1FA40, which no finance row shows;
			// the staff costs row (+0x1F7F0, graph bit 0x80 at 0x0049A7B0) takes only the wages (0x00504C70, 0x00505790)
			Post( LedgerCategory.OtherCosts, training );
			Raise( ParkEventKind.TrainingPaid, training );
		}
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
				// [BIN:TPW-EXE:0x4d0750 take loan] taking a loan only needs its bought flag (+0x28) clear: no credit test
				// [APPROX:ECON-007] a repaid loan is offered again at once and every offer not taken is listed, without a credit test: the original clears the bought flag at the last instalment (0x4D0370) and lists an offer on the Available tab (0x49FA00) only when it is not bought and its stored test passed, amount ≤ net worth (0x4C7DB0, the park value with its cell points, ECON-026) − the remaining instalments of the bought loans (0x4D06B0), stored whenever the list is built (the dialog opens, a tab is selected); taking a loan needs only the bought flag clear (0x4D0750) — evidence needed: the net worth's cell points (ECON-026)
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
		// [BIN:TPW-EXE:0x4d0370 bank month] after the instalments, a park whose balance and mLastBalance are both below 0 counts the 30-day months since
		// the turn-entered-red stamp; above 5 it posts message type 2 (0x0040F630, bus DAT_00788D3C), at every month end while it stays so
		var previousMonthsInRed = MonthsInRed;
		MonthsInRed = MonthsSinceEnteringRed();
		if ( MonthsInRed >= MonthsInRedForBankruptcy )
		{
			IsBankrupt = true;
			IsParkOpen = false;
			Raise( ParkEventKind.Bankrupt, MonthsInRed );
		}
		// [APPROX:ECON-030] message type 2 of the bank's month (posted at each month end while more than 5 thirty-day months have passed since the turn-entered-red stamp, tp.exe 0x4D0370) is taken as bankruptcy: the park closes and the simulation stops, and the TAG_SYSTEM warnings come when the count reaches 3 and 5; the original's bank handler (0x4D02D0) acts on it once, unless the word 0x7CF4F4 is set (0x516C00), by setting the world state to 4 (0x5168F0): the per-turn thing loop stops (0x516380) and the park script's variable 0 becomes 2 (0x519EF0); the park-closed word is not written, and the turn counter, the calendar and the monthly bank go on; the bank routine compares the count only with 5, so it gives no warnings — evidence needed: the producer of the TAG_SYSTEM warnings and the meaning of the word 0x7CF4F4
		else if ( previousMonthsInRed < 5 && MonthsInRed >= 5 )
			Raise( ParkEventKind.BankruptcyWarning, 5 );
		else if ( previousMonthsInRed < 3 && MonthsInRed >= 3 )
			Raise( ParkEventKind.BankruptcyWarning, 3 );
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
	// [BIN:TPW-EXE:0x004E2400 scrap value] the same product: the percentage 0x004E2290 × record +0x1B8 (Upgrades[0].CostOfUpgrade), unsigned division by 100; the sale 0x004DD0A0 credits it (executed by the x86 oracle target tpw-exe-scrap-value, ORACLE.md X13)
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
	// [BIN:TPW-EXE:0x004E2290 scrap percentage] the same rule at the PC offsets: stamp thing +0x18, level byte +0x50, use counter +0x1A0 (cleared by the constructor 0x004DB090, raised with the history word +0x1A8 by 0x004E1690 when a guest leaves, 0x004FD970), percentages at record +0x188 + 64 × level; identical to 0x100E2424 on 572 inputs (ORACLE.md X13)
	public int ScrapPercent( ParkObjectState item )
	{
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
	/// Park value (UITEXT 163): the object part of the original's <c>0xc1d4c</c> (ORACLE.md F29): per object, unless it is condemned, the costs of its upgrade levels up to the
	/// current one times its state of repair / 100. The original adds the map's cell points to it (2 per owned empty cell, 3 per path, 5 per queue, 50 per owned hoarding, ...).
	/// </summary>
	// [BIN:STP-PPC:0x100C1D4C park value] the thing loop adds sum(Upgrades[0..level].CostOfUpgrade) × byte(state of repair) / 100 for every object that is not in state 4; the 128 × 128 cell loop adds 1, 3, 5, 1, 1, 2 and 50 points by cell type (the predicates 0x10085174..0x10085418) for cells without the unowned flag 0x40; both executed by the oracle target park-value
	// [APPROX:ECON-026] the map's cell points are not added: the original (0x004C7DB0) adds, for each cell without flag 0x40, 2 for type 0, 3 for type 1, 1 for types 2 and 10, 5 for type 3, 6 for type 9 and 50 when the cell's structure record (for a record of type 12 or 17, the record its link names) has type 11, 13, 16, 18 or 25 (a hoarding is 25); the ownership exists now (the park's land, ParkCellMap.Land), but OpenTPW's cell types are incomplete (objects, ride cells and terrain are not typed, ECON-050) and it has no structure records, so the park value is the object part only, which matches, without the scrap values, as in the original — evidence needed: the original cell types (ECON-050) and structure records
	public long ParkValue => objects.Values.Sum( ObjectValue );

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
			// [APPROX:ECON-027] the visitor term counts the guests in the park by state, where the original counts the guests standing on a cell of type 0, 1, 3, 9 or 10 (0x004CCEB0 with the cell test 0x004FA990, recounted at every rating, capped at 1000); ParkGate.GuestCensus has that test, but the imported cell types are incomplete (ECON-050), so the rating keeps the state count; the staff terms match (the byte +3 is set only by the deleter at 0x0050B8E9, which then marks the thing deleted at 0x0050B906, and a dismissal removes the member at once here too) — evidence needed: the original cell types (ECON-050)
			var guests = Math.Min( guestStatistics.PeopleInPark, 1000 ) * 20 / 1000;
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
		// [BIN:TPW-EXE:0x5234d0 purchase gate] a cash-priced object (GoldenTicketCost +0xc4 below 1, or already bought) is refused when its price (+0x1b8) is greater than the bank balance (+0xc), equal allowed (0x523658; the placement validator 0x535670 repeats it at 0x535927); the test is skipped only in layout replay (0x785a2c, set by 0x404140), and the debit 0x4d01f0 does not test the balance
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
	// [BIN:TPW-EXE:0x004C8240 park points] a thing counts only when its type's Info.WhichUIType (+0x4AC, copied from +0x4C at 0x004134F5) is 0 or 2, it passes the counts-test 0x004DD920 and its type has copies (0x004D3D80 +0x18 > 0)
	// [APPROX:ARRIVAL-003] the coaster test is CoasterRide.AcceptsGuests, which also needs 3 track samples and has no not-the-coaster-being-edited term (0x00790FEC; COAST-024), and a kart or water ride's track word is false before its first circuit walk where the original's constructor sets it to 1 (0x004DB0DF), and a coaster imported from an original save has no controller (its track is not imported, SAVE-920 to SAVE-923), so it gives no points where the original counts it — evidence needed: whether a kart or water ride can have queue cells before its first walk
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
	public static bool CountsForParkPoints( ParkObjectState item, EconomyObjectInfo info ) => info.IsChoosable && !item.IsBrokenDown && !item.ScriptBroken && item.IsOpen && item.QueueHasRoom
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
		// [BIN:TPW-EXE:0x004DB690 first purchase] the constructor stamps the game turn into the type's record +0x1C only while it is 0; at a new park the gates and traffic lights are built after the turn is set to 0 (0x00515865), so fixed items stamp 0 as here
		// [APPROX:ARRIVAL-005] an object imported from an original save sets the first-purchase turn to the import turn; the original restores the record from the save's object-control block (0x004D3AA0, called by the world loader at 0x005181E7), which OpenTPW keeps opaque — evidence needed: the block's offset and its +0x1C values in an original save
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
		// [BIN:TPW-EXE:0x004AF4E9 upgrade purchase] the buy handler compares the signed balance (bank +0xC) with Upgrades[level].CostOfUpgrade (type record +0x1B8 + 0x40 · level) and requests the upgrade (0x004E0050(1): state 2, a mechanic called) only when the balance is greater; nothing is debited here
		var cost = info.Upgrades[level].CostOfUpgrade;
		if ( Balance <= cost )
			return PurchaseResult.NotEnoughMoney;
		item.PendingLevel = level;
		item.UpgradeCostDue = true;
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
	/// Charges a Buy Land purchase: <paramref name="cost"/> is the validator's total (<see cref="ParkLand.Price"/>, whose money
	/// test already ran), debited without another test as other costs; <paramref name="cells"/> only names the purchase.
	/// </summary>
	// [BIN:TPW-EXE:0x525d5a land debit] after the purchase (0x5327e0) the click handler debits the validator's total 0x81af4c (0x52fbc0) through the bank debit 0x4d01f0, which adds it to the money-out word +0x1f5a0 of the object 0x519510 returns and tests no balance; the ledger books it as other costs, as the other cell purchases
	public void ChargeLand( long cost, int cells )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( cells );
		// A negative total (a modified Costs.MapCell) is booked as other income, as TrySpendCell books a negative cell cost (the ECON-048 approximation: the original's debit of a negative amount raises the balance; its month category is not traced).
		if ( cost >= 0 )
			Post( LedgerCategory.OtherCosts, cost );
		else
			Post( LedgerCategory.OtherIncome, -cost );
		Raise( ParkEventKind.CellsBought, cost, 0, (int)CellPurchase.Land, $"{cells} {CellPurchase.Land} cells" );
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
	/// <c>Costs.*</c>, which tp.exe refuses when it loads the balance file) raises the balance by its amount.
	/// </summary>
	// [BIN:STP-PPC:0x10082AC4 SetCellType] type 1 costs Costs.PathCell, type 3 Costs.QueueCell; refused when balance − cost < 0, else Spend per cell at write time
	public PurchaseResult TrySpendCell( CellPurchase kind )
	{
		if ( !CanSpendCell( kind ) )
			return PurchaseResult.NotEnoughMoney;
		var cost = CellCost( kind );
		// [APPROX:ECON-048] a negative path or queue cell cost (modified Costs.*) is accepted and booked as other income; tp.exe never spends one: Costs.* are type-5 balance keys, whose parser refuses a negative value ('Balance problem: invalid -ve value specified', 0x401d7c), which fails the balance load (0x407f20, 0x407e00 skips the cost copy 0x4080e0) and so the level start (0x54e360) — evidence needed: none (read); OpenTPW does not refuse the value at load
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

	public StaffMember Hire( int candidateId )
	{
		if ( IsBankrupt )
			throw new InvalidOperationException( "A bankrupt park cannot hire staff." );
		var member = Staff.Hire( candidateId, Tick );
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
		var definition = Objectives.CurrentDefinition;
		var index = Objectives.Decline( ParkCalendar.DayIndex( Tick ) );
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
	/// bankrupt: there the original's thing loop has stopped, so no guest reaches the booth at all (ECON-030), where OpenTPW's guests keep updating.
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
		// [BIN:TPW-EXE:0x004FE1A0 price taken] the use routine's price step takes the price (+0x194) only when it is not 0: the takings 0x004E16B0 (bank money in, the
		// shop row, the per-type sale count and the challenge accumulators), the till sound and the guest's money; a free sale skips all of it, while the cost of
		// goods (0x004FE1E0 → 0x004E1920) and the customer count (0x004E1690) are still booked
		if ( item.Price != 0 )
		{
			Post( LedgerCategory.ShopTakings, item.Price );
			item.TakingsThisMonth += item.Price;
			if ( Catalog.TryGet( item.InfoId, out var info ) )
				Counters.Add( ParkCounters.Sold( info.ShopType, info.SpecialIngredient ), 1 );
		}
		Post( LedgerCategory.OtherCosts, item.CostOfGoods );
		item.CustomersThisMonth++;
		item.CostsThisMonth += item.CostOfGoods;
		item.TotalProfit += item.Price - item.CostOfGoods;
		Counters.Add( ParkCounters.ShopProfit, item.Price - item.CostOfGoods );
		if ( item.Price != 0 )
			Raise( ParkEventKind.GuestPaid, item.Price, item.Id, item.InfoId, nameof( LedgerCategory.ShopTakings ) );
		return true;
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
		won = SideshowWins( item.ChanceOfLosingPercent, Random.Next( 100 ) );
		var prize = won ? item.CostOfGoods : 0;
		// [BIN:TPW-EXE:0x004FE1A0 price taken] a free play skips the takings (0x004E16B0) as a free sale does; the prize is still paid
		if ( item.Price != 0 )
		{
			Post( LedgerCategory.SideshowTakings, item.Price );
			item.TakingsThisMonth += item.Price;
		}
		Post( LedgerCategory.OtherCosts, prize );
		item.CustomersThisMonth++;
		item.CostsThisMonth += prize;
		item.TotalProfit += item.Price - prize;
		if ( won )
			item.WinnersThisMonth++;
		Counters.Add( ParkCounters.SideshowProfit, item.Price - prize );
		if ( item.Price != 0 )
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
		}
	}

	/// <summary>The park's perception statistics (<see cref="ParkPriceStatistics"/>), which the price test at a stand point records.</summary>
	public ParkPriceStatistics PriceStatistics { get; } = new();

	public void ReportRecord( ParkRecordKind kind, int instanceId, long value )
	{
		var infoId = objects.TryGetValue( instanceId, out var item ) ? item.InfoId : 0;
		Counters.Max( ParkCounters.Record( kind, infoId ), value );
	}


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
		IReadOnlyDictionary<int, int> declines, IEnumerable<int> finished )
	{
		ActiveChallenge? current = null;
		if ( runningIndex is int running && Settings.Challenges.TryGetValue( running, out var definition ) )
		{
			// The original accumulates the value from 0 at the offer; OpenTPW measures a count type against a baseline, so the baseline is set
			// to give the saved value now
			var baseline = ParkObjectives.IsCountSinceAcceptance( definition.Type ) ? MeasureChallenge( definition ) - value : 0;
			var start = deadlineDay - definition.TargetTime;
			current = new ActiveChallenge( running, start, true, start, deadlineDay, baseline );
		}
		else if ( offeredIndex is int offered && Settings.Challenges.ContainsKey( offered ) )
			current = new ActiveChallenge( offered, ParkCalendar.DayIndex( Tick ), false, 0, 0, 0 );
		Objectives.Restore( nextOfferDay, nextListPosition, current, declines, finished, Objectives.GoldenTickets );
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
	internal void RestoreBankWords( bool withdrawalsEnabled, long lastBalance, long turnEnteredRed )
	{
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
