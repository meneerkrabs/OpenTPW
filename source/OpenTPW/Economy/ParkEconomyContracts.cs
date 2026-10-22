namespace OpenTPW;

/// <summary>
/// Original game modes (UITEXT 240/241). Instant Action: research is automatic (UITEXT 467),
/// loans and upgrades are unavailable (UIHELPTEXT 493, UITEXT 27).
/// </summary>
// [BIN:STP-PPC:0x1015D220 new-player dialog] radio 0x70D "Instant Action" / 0x70E "Full Simulation"; 0x1013741C stores the choice as profile mEasyModeUser and 0x1013781C turns it into game type 2 (Instant Action) or 0
public enum ParkGameMode
{
	FullSimulation,
	InstantAction
}

public enum ParkEventKind
{
	GuestPaid,
	ObjectBuilt,
	ObjectSold,
	UpgradeBought,
	UpgradeCompleted,
	CellsBought,
	StaffHired,
	StaffFired,
	WagesPaid,
	TrainingPaid,
	LoanTaken,
	LoanPayment,
	LoanRepaid,
	ItemResearched,
	DayEnded,
	MonthEnded,
	YearEnded,
	InTheRed,
	/// <summary>Not raised: the bank's month gives no warnings before the bankruptcy message (tp.exe <c>0x004D0370</c>, ECON-030); kept so the event numbering stays.</summary>
	BankruptcyWarning,
	Bankrupt,
	ParkOpened,
	ParkClosed,
	RideWorn,
	RideBrokeDown,
	RideRepaired,
	ChallengeOffered,
	ChallengeAccepted,
	ChallengeDeclined,
	ChallengeCompleted,
	ChallengeFailed,
	GoldenTicketWon,
	CoasterTrackBuilt,
	/// <summary>
	/// A staff strike message (ECON-014): <see cref="ParkEvent.InfoId"/> the role (<see cref="StaffType"/>), <see cref="ParkEvent.Amount"/> the
	/// <see cref="StrikeMessageKind"/>, <see cref="ParkEvent.Detail"/> "TAG_SYSTEM n" with the index of the message text.
	/// </summary>
	StaffStrikeMessage,
	/// <summary>A toilet's gauge fell below 25 ("Toilet has become dirty and smelly", tp.exe 0x004E2440): <see cref="ParkEvent.InstanceId"/> the toilet.</summary>
	ToiletDirty,
	/// <summary>A handyman finished cleaning a toilet (tp.exe 0x004DFD80): <see cref="ParkEvent.InstanceId"/> the toilet, <see cref="ParkEvent.Amount"/> 1 when the clean answered the player's call.</summary>
	ToiletCleaned,
	/// <summary>The player called a mechanic to the object (tp.exe 0x004DFE30): the ride closes. <see cref="ParkEvent.InstanceId"/> the object.</summary>
	MaintenanceRequested,
	/// <summary>The player cancelled the call (tp.exe 0x004DFE30): the ride reopens when its state allows. <see cref="ParkEvent.InstanceId"/> the object.</summary>
	MaintenanceCancelled,
	/// <summary>
	/// The object's script variable <see cref="ParkEvent.InfoId"/> (a <see cref="RideVariables"/> index: VAR_BREAKSTAT, VAR_WORN) takes the value
	/// <see cref="ParkEvent.Amount"/>; the host writes it into the script of <see cref="ParkEvent.InstanceId"/>. Raised only for a script-hosted object.
	/// </summary>
	RideScriptVariable,
	/// <summary>
	/// The bank's month-end message type 2 (tp.exe <c>0x004D0370</c>): raised at every month end while the balance and the balance after the last debit
	/// are below 0 and more than five 30-day months have passed since the park went into the red; <see cref="ParkEvent.Amount"/> the months. The advisor
	/// answers each one (its game event 2); the bank's own handler loses the park once (<see cref="Bankrupt"/>).
	/// </summary>
	BankruptcyMessage,
	/// <summary>
	/// A completion opened a higher research group of the category <see cref="ParkEvent.Amount"/> (a <see cref="ResearchCategory"/>) that still has unresearched items
	/// (the lab's group opener, tp.exe <c>0x00503B80</c>); the advisor says its new-research-group advice.
	/// </summary>
	ResearchGroupOpened,
	/// <summary>A completion left no item unresearched (the lab's check, tp.exe <c>0x00503D40</c>).</summary>
	ResearchAllDone,
	/// <summary>A global golden ticket moved to this park from another park that held it with a lower record (tp.exe <c>0x005AF940</c> result 4): <see cref="ParkEvent.Amount"/> the <see cref="GoldenTicketKind"/>; the advisor says its moved-award advice.</summary>
	GlobalTicketMoved,
	/// <summary>Theme Park Inc's monthly running costs were debited (<see cref="ParkEvent.Amount"/> the total, <see cref="ParkEvent.InfoId"/> the objects charged).</summary>
	RunningCostsPaid,
	/// <summary>
	/// A wear step took a ride's condition below 10 percent (tp.exe <c>0x004DF670</c>, which then queues the advisor's advice 66): <see cref="ParkEvent.InstanceId"/> the ride,
	/// <see cref="ParkEvent.Amount"/> its state of repair.
	/// </summary>
	RideBreakdownImminent,
	/// <summary>
	/// The ride entered its broken state (state 1, or state 4 when its life gauge byte is 0): the moment the ride update (tp.exe <c>0x004E14E0</c>) has read the script's VAR_BROKEN and its
	/// state change (<c>0x004E0E60</c>) sends the breakdown message the advisor answers, unlike <see cref="RideBrokeDown"/>, which is the earlier breakdown request.
	/// <see cref="ParkEvent.InstanceId"/> the ride.
	/// </summary>
	RideEnteredBrokenState,
	/// <summary>The player cancelled the upgrade a ride waited for (the upgrade list's "Cancel level" row, tp.exe 0x004E0050(0)): the ride opens. <see cref="ParkEvent.InstanceId"/> the ride.</summary>
	UpgradeCancelled,
	/// <summary>
	/// A cheat was applied at the turn boundary (<see cref="ParkCheats"/>, docs/CHEATS.md): <see cref="ParkEvent.InfoId"/> the <see cref="CheatKind"/>, <see cref="ParkEvent.Amount"/> its argument (a ticket's
	/// <see cref="GoldenTicketKind"/>), <see cref="ParkEvent.Detail"/> its outcome (a ticket's <see cref="GlobalTicketOutcome"/>). Unlike <see cref="GoldenTicketWon"/> the advisor says nothing: the original's ticket cheats are silent.
	/// </summary>
	CheatApplied,
	/// <summary>
	/// The game made a fixed item while the park is played, with the common object constructor every purchase also runs (tp.exe <c>0x004DB090</c>): an arrival vehicle at its
	/// first trip (<c>0x0051A57E</c>) or the Instant Action finale's End (<c>0x00516B8B</c>). <see cref="ParkEvent.InstanceId"/> its uncharged record
	/// (<see cref="ParkEconomy.RegisterFixedItemMade"/>), <see cref="ParkEvent.InfoId"/> its type. Nothing was bought, unlike <see cref="ObjectBuilt"/>; the advisor's
	/// construction stamp moves for both.
	/// </summary>
	FixedItemMade
}

/// <summary>
/// A park simulation event. <see cref="Amount"/> is money for money events, a count or index
/// otherwise (months in the red, challenge index, golden ticket kind, upgrade level).
/// </summary>
public sealed record ParkEvent( long Tick, ParkEventKind Kind, int InstanceId, int InfoId, long Amount, string Detail )
{
	public ParkDate Date => ParkCalendar.ToDate( Tick );
}

/// <summary>Measurements other slices report for golden tickets and challenges.</summary>
public enum ParkRecordKind
{
	CoasterHeight,
	/// <summary>Kept only so older saves' record lists still read; type 19 now reads the coasters' counted sections (tp.exe 0x4d1660).</summary>
	CoasterLoops,
	GokartExcitement,
	/// <summary>Kept only so older saves' record lists still read; challenge type 15 counts the live karts' crossings (tp.exe 0x4cc4b0).</summary>
	KartCrossroads,
	/// <summary>Kept only so older saves' record lists still read; challenge type 21 counts the live track rides' sections (tp.exe 0x4cc5d0).</summary>
	KartSections,
	WaterLength,
	CellsOwned,
	/// <summary>Kept only so older saves' record lists still read; the camera ticket reads the security coverage of the cells (tp.exe 0x4cd720), not a record.</summary>
	CellsCoveredByCameras,
	/// <summary>Kept only so older saves' record lists still read; tp.exe awards no all-land ticket.</summary>
	OwnsAllLand,
	ToiletCleanlinessPercent
}

/// <summary>Guest-side statistics the economy needs (park rating, golden tickets, challenges). Implemented by the guests slice.</summary>
/// <summary>
/// What a ride's script reports for the original wear step: <c>VAR_RUNNING</c>, <c>VAR_ONRIDE</c> and the ride speed
/// (docs/reverse/RIDE-WEAR.md). The Mac wear step reads these through the ride's script variables.
/// </summary>
public readonly record struct RideOperation( bool Running, int Riders, int Speed );

/// <summary>Supplies <see cref="RideOperation"/> per economy object; objects it does not know do not run.</summary>
public interface IRideOperations
{
	bool TryGet( int instanceId, out RideOperation operation );
}

/// <summary>No ride scripts (economy-only parks and tests): no ride runs, so nothing wears.</summary>
public sealed class NoRideOperations : IRideOperations
{
	public static readonly NoRideOperations Instance = new();

	public bool TryGet( int instanceId, out RideOperation operation )
	{
		operation = default;
		return false;
	}
}

public interface IParkGuestStatistics
{
	int PeopleInPark { get; }
	/// <summary>
	/// The guests standing on a cell of type 0, 1, 3, 9 or 10, counted now (tp.exe <c>0x004C9130</c>, the predicate <c>0x004FA990</c>): park ground,
	/// path, queue, entrance and exit, not the cells without terrain or an object's body. Statistics without a cell map answer <see cref="PeopleInPark"/>.
	/// </summary>
	int GuestCensus => PeopleInPark;
	/// <summary>Average visitor happiness, 0–100, as the original's 0xc19e4 returns it: a single, the mean of each guest's truncated happiness, 0 while the park is closed.</summary>
	float AverageHappiness { get; }
	/// <summary>Visitors whose happiness is at least <see cref="GoldenTicketSettings.Happiness"/>.</summary>
	int CountHappierThan( int happiness );
	int KidsWithBalloonsPercent { get; }
	int KidsWithCostumesPercent { get; }

	/// <summary>
	/// The guests of the census (<see cref="GuestCensus"/>) whose need meter, truncated to its low byte, is strictly above <paramref name="threshold"/>'s low byte (tp.exe
	/// <c>0x004C93F0</c> thirst, <c>0x004C9340</c> hunger, <c>0x004C9290</c> toilet). The advisor reads these counts; statistics without guests answer 0.
	/// </summary>
	int CountGuestsNeeding( GuestNeedMeter meter, int threshold ) => 0;
	/// <summary>The census guests holding a balloon (tp.exe <c>0x004C9530</c>: guest <c>+0x210</c> nonzero).</summary>
	int GuestsWithBalloons => 0;
	/// <summary>The census guests in a costume (tp.exe <c>0x004C95D0</c>: guest <c>+0x24</c> equal to 2).</summary>
	int GuestsInCostume => 0;
	/// <summary>
	/// The mean happiness the advisor reads (tp.exe <c>0x004C7BB0</c>): 0 while the park is closed, else the single mean of the truncated happiness bytes of the census guests (0 without any).
	/// Statistics that do not tell it answer <see cref="AverageHappiness"/>.
	/// </summary>
	float CensusHappiness => AverageHappiness;
	/// <summary>The guests in state 3 standing on the two ticket booth cells (tp.exe <c>0x004C91C0</c>): the crowd the open-the-park advice counts. Statistics without guests answer 0.</summary>
	int GuestsAtGate => 0;
	/// <summary>
	/// The percent of the census guests whose happiness, truncated to its low byte, is strictly below <paramref name="happiness"/> (tp.exe <c>0x004C9750</c>: the count x 100 divided by the census guests, signed,
	/// 0 without any). The advisor's entertainer line reads it with 30. Statistics without guests answer 0.
	/// </summary>
	int CensusPercentBelowHappiness( int happiness ) => 0;
}

/// <summary>The guest need meters the advisor counts guests by (the offsets of the original's guest: <c>+0x1A4</c> thirst, <c>+0x1A8</c> hunger, <c>+0x1AC</c> toilet).</summary>
public enum GuestNeedMeter
{
	Thirst,
	Hunger,
	Toilet,
}

/// <summary>Statistics of an empty park, used until the guests slice supplies real ones.</summary>
public sealed class NoGuestStatistics : IParkGuestStatistics
{
	public static readonly NoGuestStatistics Instance = new();
	public int PeopleInPark => 0;
	public float AverageHappiness => 0;
	public int CountHappierThan( int happiness ) => 0;
	public int KidsWithBalloonsPercent => 0;
	public int KidsWithCostumesPercent => 0;
}

/// <summary>Calendar access for scripts (<c>YEAR</c>/<c>MONTH</c>/<c>DAY</c>/<c>HOUR</c> opcodes) and UI.</summary>
public interface IParkClock
{
	long Tick { get; }
	ParkDate Date { get; }
	GameSpeed Speed { get; set; }
}

/// <summary>
/// What guests, rides and the frontend need from the park economy. Guests pay through
/// <see cref="TryAdmitVisitor"/>, <see cref="TryBuy"/> and <see cref="PlaySideshow"/>; rides report
/// use and records; every change is published through <see cref="EventRaised"/>.
/// </summary>
public interface IParkEconomy : IParkClock
{
	long Balance { get; }
	bool IsParkOpen { get; }
	int EntranceFee { get; }
	IParkGuestStatistics GuestStatistics { get; set; }
	event Action<ParkEvent>? EventRaised;

	/// <summary>Books the entrance fee at the booth (no park-closed test: the guests' walk to the booth makes it; the visitor's cash is not read). False only when the park is bankrupt.</summary>
	bool TryAdmitVisitor( out int feePaid );
	/// <summary>Sells one item of a shop. False when the shop is unknown/closed or the visitor cannot pay.</summary>
	bool TryBuy( int instanceId, long visitorCash, out int pricePaid );
	/// <summary>One play of a sideshow; the economy draws the win with <c>UsageInfo.InitChanceOfLoosing</c> and pays the prize cost.</summary>
	bool PlaySideshow( int instanceId, long visitorCash, out int pricePaid, out bool won );
	void RecordRideUse( int instanceId );
	void ReportRecord( ParkRecordKind kind, int instanceId, long value );
	bool TryGetObject( int instanceId, out ParkObjectState state );
}

/// <summary>Economy state of one placed object instance.</summary>
public sealed class ParkObjectState
{
	public required int Id { get; init; }
	public required int InfoId { get; init; }
	public required ParkObjectKind Kind { get; init; }
	public long BuiltTick { get; init; }
	/// <summary>True for objects imported from an original save (not charged).</summary>
	public bool Imported { get; init; }
	/// <summary>Current upgrade level (0 = base).</summary>
	public int Level { get; set; }
	/// <summary>Level being installed by a mechanic, or 0.</summary>
	public int PendingLevel { get; set; }
	/// <summary>
	/// The pending upgrade's cost is still to be debited when the mechanic installs it (tp.exe <c>0x004DF8F0</c>). False for an upgrade restored from a
	/// save written while OpenTPW charged the purchase, which has been paid already.
	/// </summary>
	public bool UpgradeCostDue { get; set; }
	public long TotalSpent { get; set; }
	public int Price { get; set; }
	public int CostOfGoods { get; set; }
	public int ChanceOfLosingPercent { get; set; }
	/// <summary>A shop's quality of goods (ride <c>+0x18C</c>, <c>mQualityOfGoods</c>): 50 when built (tp.exe <c>0x004DB389</c>), 50 × the shop window's quality step once applied (<c>0x4b0aa0</c>).</summary>
	public int QualityOfGoods { get; set; } = GuestAppearance.BuiltQualityOfGoods;
	/// <summary>The amount of the type's special ingredient (ride <c>+0x198</c>, <c>mAmountOfSpecialIngredient</c>): 50 when built (<c>0x4db3b3</c>), the shop window's 0..100 slider once applied (<c>0x4b0aa0</c>).</summary>
	public int IngredientAmount { get; set; } = GuestSpeed.DefaultIngredientAmount;
	public bool IsOpen { get; set; } = true;
	/// <summary>Exact state of repair, 0–100. The original keeps a float and compares its truncated value.</summary>
	public double Repair { get; set; } = 100;
	/// <summary>State of repair 0–100 (UITEXT 21): the truncated <see cref="Repair"/>.</summary>
	public int StateOfRepair { get => (int)Repair; set => Repair = value; }
	/// <summary>
	/// Second gauge of the original ride (object +0x44), 0–100: it falls with wear and by 5 per breakdown, no repair
	/// restores it, and a ride breaks down while it is below 1 (docs/reverse/RIDE-WEAR.md).
	/// </summary>
	// [BIN:STP-PPC:0x100DAB1C object constructor] every built object starts at 100.0 (TOC −0x2B08) in +0x40, +0x44 and +0x48; the earlier 0.0 store (0x100DA90C) is overwritten on every path
	public double LifeGauge { get; set; } = 100;
	public bool IsBrokenDown { get; set; }
	/// <summary>The ride script reports VAR_BROKEN, which the original's ride update turns into ride state 1 or 4; mirrored each tick by the level, not saved.</summary>
	public bool ScriptBroken { get; set; }
	/// <summary>
	/// A script runs the object (the level's objects; mirrored each tick, not saved): the breakdown then goes through the script (VAR_BREAKSTAT, the script's
	/// VAR_BROKEN, the ride update's state 1); a headless economy breaks the object down at once.
	/// </summary>
	public bool ScriptHosted { get; set; }
	/// <summary>The script's VAR_BREAKSTAT, mirrored each tick by the level (and set at once when the economy writes it); not saved.</summary>
	public int ScriptBreakStat { get; set; }
	/// <summary>The script's VAR_WORN, mirrored each tick by the level (and set at once when the economy writes it); not saved.</summary>
	public int ScriptWorn { get; set; }
	/// <summary>
	/// The ride update's state 1 as the level last saw it (the visitor bridge keeps it): its rising edge breaks the object down. Saved; null until the first
	/// observation (a new object, an older save), which then only records the state.
	/// </summary>
	internal bool? ScriptStateBroken { get; set; }
	/// <summary>
	/// The attraction's track validity (<c>mIsTrackRideValid</c>, +0x2C on the PC): a water ride whose circuit walk failed does not count for the park points
	/// (the counts-test's built-track condition). Mirrored each tick by the level, not saved; true for every ride without a track.
	/// </summary>
	// [BIN:TPW-EXE:0x00524960 track validity] the walk (0x0052A970) writes the flag after every track edit; the park points counts-test reads it for karts and water rides
	public bool TrackValid { get; set; } = true;
	/// <summary>
	/// A coaster's controller passes the counts-test's coaster test: it exists, crosses itself nowhere, and is complete and closed
	/// and is not being edited (<c>CoasterRide.CountsForParkPoints</c>), without the host admission sample guard. Read only for a coaster type (Bumper.WhichTrackType 3); mirrored each tick by the level, not
	/// saved; true until the first sync.
	/// </summary>
	// [BIN:TPW-EXE:0x00441970 coaster test] the controller found by the thing's +0x20 (0x00436860, none: 0) must have +0x140 (its self-intersection count) 0, +0x3C bit 0 set and bit 1 clear, and must not be the coaster being edited (0x00790FEC)
	public bool CoasterAcceptsGuests { get; set; } = true;
	/// <summary>
	/// The attraction has real queue cells from its entrance and room in them: the queue list is shorter than 4 × the queue cells
	/// (the counts-test's 0x004DE130 back-of-queue walk and 0x004DDF50 list count; QueueJoinRules.QueueCountsForParkPoints). Synced
	/// each tick by the level and the M3/M4 gate loops; it is not saved, so it is true until the first sync after a load or a purchase.
	/// </summary>
	public bool QueueHasRoom { get; set; } = true;
	/// <summary>Employee repairing or upgrading the object, 0 when none: the original's claim word <c>+0x5E</c> (a mechanic, or a handyman for a toilet).</summary>
	public int MechanicId { get; set; }
	/// <summary>The park turn the claim was taken (<c>+0x60</c>, <see cref="ParkEconomy.Claim"/>); a claim lapses 100 turns on once its claimant no longer targets the object.</summary>
	public long ClaimTurn { get; set; }
	/// <summary>
	/// The player called a mechanic (a handyman for a toilet) to the object: the original's <c>mRequestedService</c> (<c>+0x64</c> on the PC). It closes a ride
	/// until the repair clears it (<see cref="ParkEconomy.RequestMaintenance"/>).
	/// </summary>
	public bool MaintenanceRequested { get; set; }
	/// <summary>A fireworks object whose show has run (its script's <c>VAR_STATUS</c> reached 1): its <c>RegionFX</c> row 7 is gone for good (tp.exe <c>0x004E0B90</c>).</summary>
	public bool FireworksSpent { get; set; }
	/// <summary>The map cell the object's <c>RegionFX</c> row lies on while it stands (the level supplies it; not saved, a load puts the rows back).</summary>
	internal (int X, int Y)? EffectCell { get; set; }
	/// <summary>
	/// Guests who have used the object since it was built, never reset: the original's object word +412, which the
	/// use-finished handler raises (0xEAA00 calls 0xE1B44) and only the object constructor clears. The scrap value's
	/// first-30-days refund requires it to be 0 (<see cref="ParkEconomy.ScrapPercent"/>, ORACLE.md F21).
	/// </summary>
	public long TimesUsed { get; set; }
	public long CustomersThisMonth { get; set; }
	public long CustomersLastMonth { get; set; }
	public long TakingsThisMonth { get; set; }
	public long TakingsLastMonth { get; set; }
	public long CostsThisMonth { get; set; }
	public long CostsLastMonth { get; set; }
	public long WinnersThisMonth { get; set; }
	public long WinnersLastMonth { get; set; }
	public long TotalProfit { get; set; }
	public long ProfitLastMonth => TakingsLastMonth - CostsLastMonth;
	/// <summary>The costs ring (<c>+0xF8</c>): the cost of goods and prizes of each of the last 30 days; today's running value is <see cref="DailyRing.Today"/>.</summary>
	public DailyRing CostsRing { get; set; } = new();
	/// <summary>The takings ring (<c>+0x70</c>): the price taken on each of the last 30 days.</summary>
	public DailyRing TakingsRing { get; set; } = new();
	/// <summary>The customers ring (<c>+0x1A8</c>): the uses (rides), sales (shops) or plays (sideshows) of each of the last 30 days.</summary>
	public DailyRing CustomersRing { get; set; } = new();
	/// <summary>The winners ring (<c>+0x2B8</c>): the plays a sideshow's guests won on each of the last 30 days.</summary>
	public DailyRing WinnersRing { get; set; } = new();
	/// <summary>
	/// The visits' averaged happiness change ring (<c>+0x340</c>, the satisfaction gauge's source, 0x004E1E30). Imported and saved as it is; OpenTPW's guests
	/// keep no per-visit happiness change, so nothing writes it (ECON-057).
	/// </summary>
	public DailyRing SatisfactionRing { get; set; } = new();
	/// <summary>The customers of the last 30 days (the sum over the customers ring's entries, the windows' customers and users rows).</summary>
	public long CustomersLast30Days => CustomersRing.Total;
	/// <summary>The takings of the last 30 days.</summary>
	public long TakingsLast30Days => TakingsRing.Total;
	/// <summary>The profit of the last 30 days: the takings ring's sum less the costs ring's sum, each over its own count (<c>0x004E1C70</c>).</summary>
	public long ProfitLast30Days => TakingsRing.Total - CostsRing.Total;
	/// <summary>The winners of the last 30 days.</summary>
	public long WinnersLast30Days => WinnersRing.Total;
	/// <summary>
	/// Guests that refused the object's price at its stand point and walked away, since it was built: the original's <c>mNumWalkAways</c>
	/// (<c>+0x1A4</c>), which the refusal raises (<see cref="ParkEconomy.RecordWalkAway"/>) and only the object constructor clears.
	/// </summary>
	public long WalkAways { get; set; }
	/// <summary>Guests that walked away from the object's price today (<c>+0x230</c>), rolled into <see cref="WalkAwayDays"/> at the day's end.</summary>
	public long WalkAwaysToday { get; set; }
	/// <summary>
	/// The walk-aways of the last days, oldest first, at most <see cref="ParkEconomy.WalkAwayHistoryDays"/> (the ring at <c>+0x234</c>, head
	/// <c>+0x2AC</c>, size <c>+0x2B0</c> = 30, full flag <c>+0x2B4</c>).
	/// </summary>
	public List<long> WalkAwayDays { get; init; } = new();
}
