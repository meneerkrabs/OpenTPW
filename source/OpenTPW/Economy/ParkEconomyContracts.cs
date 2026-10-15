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
	MaintenanceCancelled
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
	KartCrossroads,
	KartSections,
	WaterLength,
	CellsOwned,
	CellsCoveredByCameras,
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
	/// <summary>Average visitor happiness, 0–100, as the original's 0xc19e4 returns it: a single, the mean of each guest's truncated happiness, 0 while the park is closed.</summary>
	float AverageHappiness { get; }
	/// <summary>Visitors whose happiness is at least <see cref="GoldenTicketSettings.Happiness"/>.</summary>
	int CountHappierThan( int happiness );
	int KidsWithBalloonsPercent { get; }
	int KidsWithCostumesPercent { get; }
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

	/// <summary>Charges the entrance fee at the gate. False when the park is closed or the visitor cannot pay.</summary>
	bool TryAdmitVisitor( long visitorCash, out int feePaid );
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
	public long TotalSpent { get; set; }
	public int Price { get; set; }
	public int CostOfGoods { get; set; }
	public int ChanceOfLosingPercent { get; set; }
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
	/// The attraction's track validity (<c>mIsTrackRideValid</c>, +0x2C on the PC): a water ride whose circuit walk failed does not count for the park points
	/// (the counts-test's built-track condition). Mirrored each tick by the level, not saved; true for every ride without a track.
	/// </summary>
	// [BIN:TPW-EXE:0x00524960 track validity] the walk (0x0052A970) writes the flag after every track edit; the park points counts-test reads it for karts and water rides
	public bool TrackValid { get; set; } = true;
	/// <summary>
	/// The attraction has real queue cells from its entrance and room in them: the queue list is shorter than 4 × the queue cells
	/// (the counts-test's 0x004DE130 back-of-queue walk and 0x004DDF50 list count; QueueJoinRules.QueueCountsForParkPoints). Synced
	/// each tick by the level and the M3/M4 gate loops; it is not saved, so it is true until the first sync after a load or a purchase.
	/// </summary>
	public bool QueueHasRoom { get; set; } = true;
	/// <summary>Employee repairing or upgrading the object, 0 when none.</summary>
	public int MechanicId { get; set; }
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
}
