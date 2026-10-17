namespace OpenTPW;

/// <summary>
/// The security cover of the park's path cells, the new tracking behind messages 24 and 25. A path or queue cell is covered when it has a security level (the
/// <c>RegionFX</c> security word of a camera, or of a guard where he stands: the cell test the camera golden ticket reads, tp.exe <c>0x004CD720</c>) or lies inside the patrol
/// area of a guard who has one set (the flag the original keeps in word <c>+0x3C</c> of its 0x44-byte cell records for the staff of a role, <c>0x004CD340</c>).
/// </summary>
internal readonly record struct RestoredSecurityCoverage( int PathCells, int CoveredCells, int Cameras )
{
	/// <summary>Whole percent of the path cells left uncovered: 100 less the covered share truncated, as the original's coverage function (<c>0x004CD500</c>) returns the covered share.</summary>
	public int UncoveredPercent => PathCells == 0 ? 0 : 100 - (int)(CoveredCells * 100L / PathCells);
}

/// <summary>One ride as the restored triggers see it (the economy's object joined with the level's visitor bridge).</summary>
/// <param name="InstanceId">The ride's economy instance.</param>
/// <param name="Open">The ride is open to guests.</param>
/// <param name="BrokenDown">The ride is in state 1 (broken down).</param>
/// <param name="Condemned">The ride is in state 4 (condemned: its life gauge is 0).</param>
/// <param name="RidersAboard">Guests aboard now (the ride script's <c>VAR_ONRIDE</c> or the bridge's rider list, the larger).</param>
/// <param name="QueueLength">Guests in the ride's queue.</param>
/// <param name="ExitBlocked">The ride has an exit whose outside cell is no path (guests cannot get off).</param>
internal readonly record struct RestoredRide( int InstanceId, bool Open, bool BrokenDown, bool Condemned, int RidersAboard, int QueueLength, bool ExitBlocked );

/// <summary>The cells the player owns: <see cref="Free"/> is how many of the <see cref="Owned"/> terrain cells have nothing built on them.</summary>
internal readonly record struct RestoredLand( int Owned, int Free );

/// <summary>
/// What the restored advice triggers read from the park, once per scan call: plain facts computed without changing anything (<see cref="ParkRestoredAdviceState"/> reads the economy and the level). A
/// test supplies its own. Nothing here is kept between calls: the extension holds no state of its own that a save would have to carry.
/// </summary>
internal interface IRestoredAdviceState
{
	/// <summary>Whole 30-day months of the park's clock (<see cref="EconomyMoneyState.ParkMonths"/>).</summary>
	int ParkMonths { get; }
	int HiredGuards { get; }
	int HiredMechanics { get; }
	/// <summary>The mechanics' strike is on (<see cref="ParkEconomy.RoleOnStrike"/>).</summary>
	bool MechanicsOnStrike { get; }
	/// <summary>Whether the game type has ride upgrades (not Instant Action).</summary>
	bool UpgradesPossible { get; }
	/// <summary>The security cover of the path cells.</summary>
	RestoredSecurityCoverage SecurityCoverage { get; }
	/// <summary>The park's rides.</summary>
	IReadOnlyList<RestoredRide> Rides { get; }
	/// <summary>Built rides with a researched upgrade level that nobody has bought yet.</summary>
	int UpgradesAvailable { get; }
	/// <summary>The upgrade levels bought over all rides.</summary>
	int UpgradesBought { get; }
	/// <summary>Toilets built.</summary>
	int Toilets { get; }
	/// <summary>Guests in the park whose toilet meter is strictly above <paramref name="above"/> (the original's guest counter <c>0x004C9290</c>).</summary>
	int GuestsNeedingToilet( int above );
	/// <summary>Of those guests, how many are <paramref name="cells"/> cells (Manhattan) or more from the nearest toilet.</summary>
	int GuestsFarFromToilets( int above, int cells );
	/// <summary>Retail (not food or drink) items sold in this month and the last.</summary>
	long RetailSalesTwoMonths { get; }
	/// <summary>Visitors admitted in this month and the last.</summary>
	long VisitorsTwoMonths { get; }
	RestoredLand Land { get; }
	/// <summary>The park's best value of a ticket record (<see cref="ParkRecordKind"/>).</summary>
	long Record( ParkRecordKind kind );
	/// <summary>The golden ticket's threshold: the record, or the year's profit, that wins it.</summary>
	long TicketThreshold( GoldenTicketKind kind );
	bool HoldsTicket( GoldenTicketKind kind );
	long ProfitThisYear { get; }
	/// <summary>The staff's monthly wages (0 without staff).</summary>
	long MonthlyWages { get; }
}
