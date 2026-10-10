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
	GoldenTicketWon
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
	/// <summary>Average visitor happiness, 0–100.</summary>
	int AverageHappiness { get; }
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
	public int AverageHappiness => 0;
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
	/// <summary>Employee repairing or upgrading the object, 0 when none.</summary>
	public int MechanicId { get; set; }
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
