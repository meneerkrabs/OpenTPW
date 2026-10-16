namespace OpenTPW;

/// <summary>
/// Imports an original level save (<see cref="OriginalParkImport"/>) into a new <see cref="ParkEconomy"/>, in the
/// dependency order of docs/reverse/SAVE-plan.md §8.2:
/// <list type="number">
/// <item>The park turn <c>mGameTick</c>, which the calendar converts to the date; the save's calendar caches are
/// checked against that date.</item>
/// <item>The loan offers and the challenge list are verified against the balance settings (they confirm, rather
/// than replace, the setting-driven state). Zero-APR monthly repayments are checked by integer division;
/// positive-APR repayment arithmetic is unverified on PC and is reported only.</item>
/// <item>The bank: balance, admission fee, the year's profit and bought loans.</item>
/// <item>Placed objects (also those whose footprint is unverified) and fixed items, registered without charge, with
/// their attraction's price, cost of goods, upgrade level, state of repair and life gauge.</item>
/// <item>Staff (models 4–8) with grade, training percentage, happiness, state and hire time.</item>
/// </list>
/// Every value that is read but not imported is listed in <see cref="Evidence"/> with its SAVE approximation. The
/// saved advisor controller's pending advice and history are imported later by the automatic advisor, which adds its
/// outcome through <see cref="Report"/>.
/// </summary>
public sealed class OriginalEconomyImport
{
	/// <summary>The original's 100 ns time-stamp units per second (TbTimeStamp, as the calendar's FunnyTimeStart).</summary>
	public const long TimeStampUnitsPerSecond = 10_000_000;

	private readonly List<string> evidence;

	private OriginalEconomyImport( SaveEconomyRecords records, bool parkClosed, int objects, int fixedItems, int staff, List<string> evidence )
	{
		Records = records;
		ParkClosed = parkClosed;
		ImportedObjects = objects;
		ImportedFixedItems = fixedItems;
		ImportedStaff = staff;
		this.evidence = evidence;
	}

	public SaveEconomyRecords Records { get; }
	/// <summary>The save's <c>mParkClosed</c> word is nonzero.</summary>
	public bool ParkClosed { get; }
	/// <summary>Placed objects registered, including those whose footprint is unverified.</summary>
	public int ImportedObjects { get; }
	public int ImportedFixedItems { get; }
	public int ImportedStaff { get; }
	public IReadOnlyList<string> Evidence => evidence;

	/// <summary>Adds the outcome of a later import step (the automatic advisor's controller import) to <see cref="Evidence"/>.</summary>
	public void Report( string line )
	{
		ArgumentException.ThrowIfNullOrEmpty( line );
		evidence.Add( line );
	}

	/// <summary>Imports the original save of <paramref name="park"/>'s level.</summary>
	public static OriginalEconomyImport Apply( ParkEconomy economy, OriginalPark park )
	{
		ArgumentNullException.ThrowIfNull( park );
		if ( park.Save == null )
			throw new InvalidOperationException( $"The {park.LevelName} level has no original save." );
		return Apply( economy, park.Save );
	}

	public static OriginalEconomyImport Apply( ParkEconomy economy, OriginalParkImport save )
	{
		ArgumentNullException.ThrowIfNull( economy );
		ArgumentNullException.ThrowIfNull( save );
		var evidence = new List<string>();
		var world = save.World;
		ImportTurn( economy, world, evidence );
		Verify( economy.Settings, save.Economy, evidence );
		ImportChallenges( economy, save.Economy.ChallengeSystem, world.Calendar.FunnyTimeStart, evidence );
		ImportBank( economy, save.Economy.BankAccount, evidence );
		if ( save.ResearchLab is { } lab )
			ImportResearch( economy, lab, evidence );
		var (objects, fixedItems) = ImportObjects( economy, save, evidence );
		var staff = ImportStaff( economy, save, evidence );
		ReportNotImported( save, evidence );
		// [BIN:STP-PPC:0x10106708 world reader] mParkClosed is restored with the world variables (0 = open)
		var closed = world.Variables.ParkClosed != 0;
		evidence.Add( $"Park {(closed ? "closed" : "open")} (mParkClosed {world.Variables.ParkClosed}), {world.Variables.NumberOfVisitorsToDate} visitors to date (not imported)." );
		return new OriginalEconomyImport( save.Economy, closed, objects, fixedItems, staff, evidence );
	}

	private static void ImportTurn( ParkEconomy economy, SaveWorld world, List<string> evidence )
	{
		// [BIN:STP-PPC:0x10105EFC world writer] mGameTick (world +0x1DA70C) is saved as u32 and restored by the reader at 0x10106890; the calendar converts it (0x100E4394)
		economy.StartAtOriginalTurn( world.Variables.GameTick );
		var date = ParkCalendar.DateTimeAt( economy.Tick );
		var calendar = world.Calendar;
		var epoch = (ParkCalendar.Epoch - new DateTime( 1601, 1, 1 )).Ticks;
		var matches = calendar.FunnyTimeStart == epoch && calendar.FunnySecsPerRealSec == ParkCalendar.SecondsPerTurn * 4
			&& calendar.MonthAtLastUpdate == date.Month - 1 && calendar.DayAtLastUpdate == date.Day;
		evidence.Add( $"Park turn {world.Variables.GameTick} imported ({ParkCalendar.ToDate( economy.Tick )}); the save's calendar (rate {calendar.FunnySecsPerRealSec}, month cache {calendar.MonthAtLastUpdate}, day cache {calendar.DayAtLastUpdate}) {(matches ? "agrees" : "does not agree")} with that date." );
		// [APPROX:SAVE-001] the save's world generator word (mRandomSeed) is reported but not imported: it is the whole state of the original's one shared world LCG (tp.exe 0x516330, world +0x1DA708), saved and loaded raw (0x51710E, 0x517DE7), while OpenTPW's economy, guest and script streams are separate generators derived from the world seed — evidence needed: DET-I2 port of the world LCG and its call order (DET-005, DET-013)
		evidence.Add( $"[APPROX:SAVE-001] World generator state mRandomSeed 0x{world.Variables.RandomSeed:X8} is not imported; the park's streams come from its world seed." );
	}

	/// <summary>Checks the save's loan offers and challenge list against the balance settings.</summary>
	public static void Verify( BalanceSettings settings, SaveEconomyRecords records, List<string> evidence )
	{
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( records );
		ArgumentNullException.ThrowIfNull( evidence );
		if ( records.Loans.Count != settings.Loans.Count )
			throw new InvalidDataException( $"The save has {records.Loans.Count} loan offers; the settings define {settings.Loans.Count}." );
		var matchedRepayments = 0;
		for ( var index = 0; index < records.Loans.Count; index++ )
		{
			var saved = records.Loans[index];
			var offer = settings.Loans[index];
			if ( saved.Amount != offer.Amount || saved.AprPercent != offer.AprPercent || saved.Months != offer.Months
				|| saved.LenderNameIndex != offer.LenderNameIndex )
				throw new InvalidDataException( $"Save loan offer {index} ({saved.Amount}/{saved.AprPercent} % APR/{saved.Months} months/lender {saved.LenderNameIndex}) differs from LoanInfo[{offer.Index}] ({offer.Amount}/{offer.AprPercent}/{offer.Months}/lender {offer.LenderNameIndex})." );
			// [BIN:STP-PPC:0xCB7A8 loan offer setup] the stored instalment is trunc_u32(P x pow(1 + APR/100, N/24) / N) for every APR (ORACLE F12); OpenTPW computes the same value
			// the stored M is kept for a bought loan whatever pow gives here: PC and Mac pow agreement in the last bit is unproven
			var expected = LoanMath.MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months );
			if ( saved.MonthlyRepayment == expected )
				matchedRepayments++;
			else
				evidence.Add( $"Save loan offer {index} monthly repayment {saved.MonthlyRepayment} at {saved.AprPercent} % APR differs from trunc(P x pow(1 + APR/100, N/24) / N) = {expected}; the stored value is kept for a bought loan (pow may differ in the last bit, or the save is modified)." );
		}
		evidence.Add( $"{records.Loans.Count} loan offers match LoanInfo amounts, APR, terms and lenders." );
		if ( matchedRepayments != 0 )
			evidence.Add( $"{matchedRepayments} monthly repayments equal trunc(P x pow(1 + APR/100, N/24) / N)." );
		var level = settings.ChallengesInThisLevel;
		if ( records.Challenges.Count != level.Count )
			throw new InvalidDataException( $"The save has {records.Challenges.Count} challenges; ChallengesInThisLevel lists {level.Count}." );
		for ( var index = 0; index < level.Count; index++ )
		{
			var saved = records.Challenges[index];
			if ( !settings.Challenges.TryGetValue( level[index], out var definition ) )
				throw new InvalidDataException( $"ChallengesInThisLevel[{index}] = {level[index]} is not defined." );
			if ( saved.Type != definition.Type || saved.TargetTime != definition.TargetTime || saved.TargetValue != definition.TargetValue || saved.TargetObject != definition.TargetObject
				|| saved.TargetObject2 != definition.TargetObject2 || saved.TargetStaffType != definition.TargetStaffType || saved.Prize != definition.Prize
				|| saved.FollowupType != definition.FollowupType || saved.CheckAtEndOnly != definition.CheckAtEndOnly || saved.Independent != definition.Independent )
				throw new InvalidDataException( $"Save challenge {index} (type {saved.Type}, prize {saved.Prize}) differs from Challenges[{definition.Index}]." );
		}
		evidence.Add( $"{level.Count} challenge records equal Challenges[{string.Join( ", ", level )}] in ChallengesInThisLevel order." );
	}

	private static void ImportBank( ParkEconomy economy, SaveBank bank, List<string> evidence )
	{
		var account = bank.Account;
		var bought = new List<LoanAccount>();
		foreach ( var loan in bank.Loans.Where( loan => loan.Bought ) )
		{
			var left = loan.Months - loan.MonthsRepaid;
			if ( left <= 0 )
				throw new InvalidDataException( $"Save loan offer {loan.Index} is bought but fully repaid ({loan.MonthsRepaid}/{loan.Months} months)." );
			// [BIN:STP-PPC:0x100CCC78 loan payoff] the outstanding amount of a bought loan is M × (N − months repaid); the original keeps no declining principal
			bought.Add( new LoanAccount( loan.Index, loan.LenderNameIndex, loan.Amount, loan.AprPercent, loan.Months, loan.MonthlyRepayment, loan.MonthsRepaid ) );
		}
		// [BIN:STP-PPC:0x100CBA54 bank serializer] admission fee, balance and mProfitThisYear (+292) are the bank words of the save; bought loans keep offer, amount, APR, term, repayment and months repaid
		economy.ImportOriginalBank( account.Balance, account.AdmissionFee, account.ProfitThisYear, bought );
		evidence.Add( $"Bank imported: balance {account.Balance}, admission fee {account.AdmissionFee}, profit this year {account.ProfitThisYear}, {bought.Count} bought loans." );
		// [BIN:TPW-EXE:0x4cf920 bank serializer] mWithdrawalsEnabled (+0x114), mLastBalance (+0x11C) and the turn-entered-red stamp (+0x120) are bank words of the save; the debit 0x4d01f0 and the month 0x4d0370 read them
		economy.RestoreBankWords( account.WithdrawalsEnabled, account.LastBalance, account.TurnEnteredRed );
		evidence.Add( $"Bank words imported: withdrawals {(account.WithdrawalsEnabled ? "enabled" : "disabled")}, last balance {account.LastBalance}, turn entered red {account.TurnEnteredRed}{(account.Balance < 0 && account.LastBalance < 0 ? $" ({economy.MonthsSinceEnteringRed()} months in the red)" : "")}." );
		// [BIN:TPW-EXE:0x4d0850 loan payoff] a bought loan's outstanding amount is its monthly repayment (+0x24) times the months not yet repaid (+0x20 − +0x2C), compared unsigned with the balance (0x4d0898)
		// [APPROX:SAVE-008] the batch balance (+0x10, a pending credit the month message folds into the balance, 0x4d0393) and the loan offers' Available flags (amount at most 10,000 at construction, 0x4cf84b) are not imported (OpenTPW offers every offer not taken); the ledger month opens at the current balance, the bank having no month-opening word (mLastBalance is the balance after the last debit, instalment or payoff) — evidence needed: the writer of the offers' Available flags after construction and the offer list that reads them
		evidence.Add( $"[APPROX:SAVE-008] Not imported: batch balance {account.BatchBalance}; the month opens at the current balance {account.Balance}." );
		// Available is neither the inverse of Bought nor of the taken offers (docs/reverse/PPC-economy.md), and OpenTPW offers every offer not taken
		var available = bank.Loans.Where( loan => loan.Available ).Select( loan => loan.Index ).ToArray();
		var offered = economy.AvailableLoans.Select( offer => offer.Index ).ToArray();
		evidence.Add( $"[APPROX:SAVE-008] Loan offers' Available flags not imported: the save marks {(available.Length == 0 ? "no offer" : $"{(available.Length == 1 ? "offer" : "offers")} {string.Join( ", ", available )}")} available ({string.Join( ", ", bank.Loans.Select( loan => $"{loan.Index}:{(loan.Available ? 1 : 0)}" ) )}); OpenTPW offers {(offered.Length == 0 ? "none" : string.Join( ", ", offered ))}{(offered.SequenceEqual( available ) ? ", the same" : "")}." );
	}

	/// <summary>The park tick of a saved attraction time stamp (a calendar date and time of the park clock); null when it is not a date.</summary>
	private static long? SavedBuildTick( SaveTimestamp stamp )
	{
		try
		{
			var date = new DateTime( stamp.Year, stamp.Month, stamp.Day, stamp.Hour, stamp.Minute, stamp.Second );
			return ParkCalendar.TickAtSeconds( Math.Max( 0, (long)(date - ParkCalendar.Epoch).TotalSeconds ) );
		}
		catch ( ArgumentOutOfRangeException )
		{
			return null;
		}
	}

	private static (int Objects, int FixedItems) ImportObjects( ParkEconomy economy, OriginalParkImport save, List<string> evidence )
	{
		var objects = 0;
		var fixedItems = 0;
		var unmatched = 0;
		var notImported = new List<string>();
		// The order matches ParkObjects.ImportOriginal (placed, unverified footprints, fixed items), which Level.ConnectObjectsToEconomy pairs by Info.Id
		foreach ( var record in save.PlacedObjects.Select( item => item.Record ).Concat( save.UnresolvedObjects ).Concat( save.FixedItems ) )
		{
			var matched = save.FindAttraction( record );
			// [BIN:STP-PPC:0x100DAD90 object purchase] thing +20 is the clock's stamp at the purchase; the saved attraction time stamp is taken as it, and the saved customer count as the use word +412 (raised with the history record at +420 when a guest leaves, 0x100EAA00 → 0x100E1B44), so an imported used or old object never gets the unused first-30-days scrap refund
			var state = economy.RegisterExisting( record.InfoId, matched is { } stamped ? SavedBuildTick( stamped.Timestamp ) : null, matched is { } counted ? counted.CustomerCount : 0 );
			if ( record.IsFixedItem )
				fixedItems++;
			else
				objects++;
			if ( matched is not { } attraction )
			{
				unmatched++;
				continue;
			}
			// [BIN:STP-PPC:0x100DAF04 attraction serializer] mPricePerUse, mCostOfGoods, mUpgradeLevel and the gauges at +0x40 (state of repair) and +0x44 (life gauge)
			if ( attraction.UpgradeLevel <= 2 && attraction.StateOfRepair <= 100 && attraction.LifeGauge <= 100 && attraction.CostOfGoods >= 0 )
			{
				state.Price = attraction.PricePerUse;
				state.CostOfGoods = attraction.CostOfGoods;
				state.Level = attraction.UpgradeLevel;
				state.Repair = attraction.StateOfRepair;
				state.LifeGauge = attraction.LifeGauge;
			}
			else
				notImported.Add( $"{record.InfoId} (level {attraction.UpgradeLevel}, repair {attraction.StateOfRepair}, life {attraction.LifeGauge}, cost of goods {attraction.CostOfGoods})" );
		}
		evidence.Add( $"Registered {objects} placed objects ({save.UnresolvedObjects.Count} with an unverified footprint{(save.UnresolvedObjects.Count == 0 ? "" : $": {string.Join( ", ", save.UnresolvedObjects.Select( item => $"{item.InfoId} at ({item.X}, {item.Y}) {item.Width}x{item.Height} turned {item.Rotation}" ) )}")}) and {fixedItems} fixed items without charge; {objects + fixedItems - unmatched - notImported.Count} took their attraction's price, cost of goods, upgrade level, state of repair and life gauge." );
		if ( unmatched != 0 || notImported.Count != 0 )
			evidence.Add( $"{unmatched} objects have no matching attraction thing; outside OpenTPW's ranges and kept at their defaults: {string.Join( ", ", notImported )}." );
		// [APPROX:SAVE-006] an attraction's saved state enum, chance of winning, third gauge, histories, totals, name lines, operating capacity, duration and speed, quality of goods, queue size, walk-away counts and mesh instance ID are not imported (the customer count is taken as the use word +412 and the time stamp as the build time +20, unverified) (open/broken state comes from its script, the queue from the laid queue cells); in tp.exe a saved state 1, 2 or 4 persists (0x4E0E00 re-derives only state 0) and the win byte is 100 minus the losing chance (0x4DB39A) — evidence needed: readers of the histories and operating fields and OpenTPW counterparts of the count fields
		evidence.Add( $"[APPROX:SAVE-006] Not imported for {save.Attractions.Count} attraction things: state ({string.Join( ", ", save.Attractions.Select( item => item.State ).Distinct().Order() )}), chance of winning, third gauge, histories, totals, name lines, operating capacity, duration and speed, quality of goods, queue size, walk-away counts, mesh instance ID (the customer count is the use count and the time stamp the build time, evidence needed: that the save's time stamp is thing +20 and its customer count +412)." );
		foreach ( var item in save.Attractions )
		{
			var stamp = item.Timestamp;
			evidence.Add( $"[APPROX:SAVE-006] Attraction thing {item.Id} (Info.Id {item.InfoId}) values not imported: state {item.State}, chance of winning {item.ChanceOfWinning}, third gauge {item.Gauge48}, total costs {item.TotalCosts}, total takings {item.TotalTakings}, name \"{item.NameLine1}\"/\"{item.NameLine2}\", operating capacity {item.OperatingCapacity}, duration {item.OperatingDuration}, speed {item.OperatingSpeed}, quality of goods {item.QualityOfGoods}, queue size {item.QueueSizeInCells} cells, customers {item.CustomerCount}, walk-aways {item.WalkAwayCount}, time stamp {stamp.Year:D4}-{stamp.Month:D2}-{stamp.Day:D2} {stamp.Hour:D2}:{stamp.Minute:D2}:{stamp.Second:D2}, mesh instance {item.MeshInstanceId}." );
		}
		return (objects, fixedItems);
	}

	private static int ImportStaff( ParkEconomy economy, OriginalParkImport save, List<string> evidence )
	{
		var start = save.World.Calendar.FunnyTimeStart;
		var imported = 0;
		var adjusted = new List<string>();
		foreach ( var saved in save.Staff )
		{
			var type = saved.Model switch
			{
				SaveThingModel.Mechanic => StaffType.Mechanic,
				SaveThingModel.Handyman => StaffType.Handyman,
				SaveThingModel.Entertainer => StaffType.Entertainer,
				SaveThingModel.Guard => StaffType.Guard,
				_ => StaffType.Researcher,
			};
			var staff = saved.Staff;
			// [BIN:STP-PPC:0x100F2C28 staff serializer] grade (+484), percentage through the grade (+488), happiness and energy written as whole-number floats, hired time stamp
			var hired = 0L;
			var seconds = (staff.HiredTimestamp - start) / TimeStampUnitsPerSecond;
			if ( staff.HiredTimestamp >= start && seconds / ParkCalendar.SecondsPerTurn <= ParkCalendar.Turn( economy.Tick ) )
			{
				hired = ParkCalendar.TickOfTurn( seconds / ParkCalendar.SecondsPerTurn );
				// OpenTPW's hire tick is a whole turn, so the time stamp is rounded down to the turn it falls in
				var rest = staff.HiredTimestamp - start - seconds / ParkCalendar.SecondsPerTurn * ParkCalendar.SecondsPerTurn * TimeStampUnitsPerSecond;
				if ( rest != 0 )
					adjusted.Add( $"{saved.Model} thing {saved.Id} hired time stamp {staff.HiredTimestamp} lies {rest / (double)TimeStampUnitsPerSecond} park seconds into turn {seconds / ParkCalendar.SecondsPerTurn}, imported as hired at that turn's start" );
			}
			else
				adjusted.Add( $"{saved.Model} thing {saved.Id} hired time stamp {staff.HiredTimestamp} lies outside the park's calendar (start {start}, turn {ParkCalendar.Turn( economy.Tick )}), imported as hired at tick 0" );
			// [BIN:TPW-EXE:0x504de0 staff serializer] the saved state is the member's own state word +0x19c (the StaffJob values), which 0x5064f0 maps to the display state
			// [APPROX:SAVE-003] saved staff are imported with the display state of their saved state word but no job (the word's target, ride, toilet or rest area is not restored), their inline name replaced by name index 0, their hire time stamp rounded down to its park turn and no patrol region, rest area, idle tick or jobs-done count — evidence needed: the units of the patrol corners +0x20a/+0x20c and whether a saved rest-area id names the reloaded object
			var state = ParkStaff.DisplayState( staff.State );
			// [BIN:STP-PPC:0x100F3D8C staff rest] happiness and energy are singles clamped to [0, 100] by every rest, idle and work update
			var happiness = Math.Clamp( staff.Happiness, 0f, 100f );
			if ( happiness != staff.Happiness )
				adjusted.Add( $"{saved.Model} thing {saved.Id} happiness {staff.Happiness} imported as {happiness} (traced range 0-100)" );
			var energy = Math.Clamp( staff.Energy, 0f, 100f );
			if ( energy != staff.Energy )
				adjusted.Add( $"{saved.Model} thing {saved.Id} energy {staff.Energy} imported as {energy} (traced range 0-100)" );
			// OpenTPW keeps 0-99 training points and promotes at 100 (tp.exe 0x505a10), so a saved 100 % is imported as 99 and reported
			var points = Math.Min( staff.GradePercentage, ParkStaff.TrainingPointsPerGrade - 1 );
			if ( points != staff.GradePercentage )
				adjusted.Add( $"{saved.Model} thing {saved.Id} grade percentage {staff.GradePercentage} imported as {points} (OpenTPW promotes at {ParkStaff.TrainingPointsPerGrade})" );
			var member = economy.Staff.AddImported( type, staff.Grade, points, happiness, state, hired, energy );
			// [DATA:SaveThingBase] exact base position is in 1/256-cell units, including the sub-cell offset.
			member.CellX = saved.Base.X / 256f;
			member.CellY = saved.Base.Y / 256f;
			imported++;
		}
		evidence.Add( $"Imported {imported} staff ({string.Join( ", ", save.Staff.Select( item => $"{item.Model} grade {item.Staff.Grade}" ) )})." );
		foreach ( var adjustment in adjusted )
			evidence.Add( $"[APPROX:SAVE-003] Adjusted on import: {adjustment}." );
		if ( imported != 0 )
		{
			evidence.Add( $"[APPROX:SAVE-003] Staff states {string.Join( ", ", save.Staff.Select( item => item.Staff.State ) )} read in STAFFSTATES order; inline names ({string.Join( ", ", save.Staff.Select( item => item.Staff.Name ) )}), jobs, patrol regions, rest areas, idle ticks, role suffixes are not imported." );
			foreach ( var saved in save.Staff )
				evidence.Add( $"[APPROX:SAVE-003] {saved.Model} thing {saved.Id} values not imported: jobs 0x{saved.Staff.Jobs:X8}, patrol region {saved.Staff.PatrolRegion}, rest area {saved.Staff.RestAreaId}, idle tick {saved.Staff.IdleTick}, role bytes {Convert.ToHexString( saved.RoleBytes )}, role next ID {saved.RoleNextId}." );
		}
		if ( save.StaffHq is { } hq )
		{
			// [BIN:TPW-EXE:0x50c800 training split] budget word k serves the role of thing model 5, 4, 6, 7 and 8 for k = 0..4 (handyman, mechanic, entertainer, guard, researcher: StaffType's order), shared by its role's headcount at month end
			for ( var role = 0; role < BalanceSettings.StaffTypeCount && role < hq.TrainingBudgets.Count; role++ )
				economy.Staff.SetTrainingBudget( (StaffType)role, hq.TrainingBudgets[role] );
			// [BIN:TPW-EXE:0x508f70 strike step] each role's 12-byte strike record holds its level (+0, 0-4) and its active word (+4); 0x508e70 stamps the third word (+8) with the turn the role was last considered
			var levels = new int[BalanceSettings.StaffTypeCount];
			var active = new bool[BalanceSettings.StaffTypeCount];
			var turns = new uint[BalanceSettings.StaffTypeCount];
			for ( var role = 0; role < levels.Length && (role + 1) * 12 <= hq.StrikeLevels.Length; role++ )
			{
				levels[role] = Math.Clamp( BitConverter.ToInt32( hq.StrikeLevels, role * 12 ), 0, 4 );
				active[role] = BitConverter.ToInt32( hq.StrikeLevels, role * 12 + 4 ) != 0;
				turns[role] = BitConverter.ToUInt32( hq.StrikeLevels, role * 12 + 8 );
			}
			economy.Staff.Strikes.Restore( levels, active, turns );
			evidence.Add( $"Staff HQ imported: training budgets {string.Join( ", ", hq.TrainingBudgets )} (handyman, mechanic, entertainer, guard, researcher), strike levels {string.Join( "/", levels )}, active {string.Join( "/", active.Select( flag => flag ? 1 : 0 ) )}, last considered on turns {string.Join( "/", turns )}." );
			// [APPROX:SAVE-009] the staff HQ's picked-up staff id (+0x60) is not imported: it is set when a member is picked up (0x509AE0 from 0x505C50) and read and cleared when the placement tool puts it down (0x509AF0 from 0x46CC40), and OpenTPW does not save a member in hand. The force-strike word and the trained byte need no import: the force strike (+0x64) is written only by the HQ constructor 0x5089C0 (0) and the save loader 0x508BB0, so the month gate 0x508E70 and the grievance 0x509360 see 0 in play as OpenTPW does, and the trained byte (mHaveEverTrained, training +0x14) is only cleared (0x50C620) and serialized (0x50C640), never read — evidence needed: what a save made with a staff member in hand restores
			evidence.Add( $"[APPROX:SAVE-009] Staff HQ thing {hq.Id} values not imported: force strike {hq.ForceStrike}, picked up {hq.PickedUp}, trained {hq.Trained}." );
		}
		else
			evidence.Add( "[APPROX:SAVE-009] The save has no staff HQ thing; OpenTPW's training budgets start at 0." );
		if ( save.World.StaffPool is { } pool )
		{
			// [BIN:TPW-EXE:0x5084f0 staff pool update] a valid slot is a candidate of role +0 (StaffType's order, 0x507600) that leaves once the turns since its stamp
			// (+0xc) exceed 4 × its timeout (+0x10); the pool refills once the turns since its own stamp exceed 4 × TimeBetweenStaffUpdates
			var saved = pool.Candidates.Where( candidate => candidate.Valid )
				.Select( candidate => ((StaffType)candidate.Type, (int)(candidate.Name % ParkStaff.NameTableSize), Math.Min( (int)candidate.Grade, BalanceSettings.GradeCount - 1 ),
					(long)candidate.TimeSig, (long)candidate.Timeout) )
				.ToList();
			economy.Staff.RestoreOriginalPool( saved, pool.TimeSig );
			evidence.Add( $"Staff pool imported: {saved.Count} candidates ({string.Join( ", ", saved.Select( item => $"{item.Item1} grade {item.Item3} made on turn {item.Item4} for {item.Item5}" ) )}), last refill on turn {pool.TimeSig}." );
			// [APPROX:SAVE-004] the pool's sub-type and on-pointer bytes are not imported: a candidate's sub-type (+9) is drawn at its creation (0x507600 → 0x541F70, (R >> 2) % a per-model count of the table 0x763F90 that is filled at run time) and handed to the placement object 0x46C6D0 when it is picked up (0x507BF0), which also sets its on-pointer byte (+0xB); OpenTPW keeps no staff sub-type and does not save the placement in hand. The people-in-category counts and stop-producing flags need no import: the census 0x508000 recomputes them from the park's staff before every read (the refill 0x5084F0, its top-up 0x508170 and the pick-up 0x507BF0), as UpdatePool counts the hired staff; the opening-pool flag only gates the opening pool 0x5087D0, which OpenTPW draws for a new park only — evidence needed: what the sub-type selects in the placement object 0x46C6D0 and the per-model counts at 0x763F90
			evidence.Add( $"[APPROX:SAVE-004] Staff pool values not imported: sub-types {string.Join( "/", pool.Candidates.Where( candidate => candidate.Valid ).Select( candidate => candidate.SubType ) )}, on pointer {pool.Candidates.Count( candidate => candidate.Valid && candidate.OnPointer )}, people in category {string.Join( "/", pool.PeopleInCategory )}, stop producing {string.Join( "/", pool.StopProducing.Select( flag => flag ? 1 : 0 ) )}, opening pool generated {(pool.OpeningPoolGenerated ? 1 : 0)}." );
		}
		else
			evidence.Add( $"[APPROX:SAVE-004] The save's hiring pool does not read before the calendar ({save.World.OpaqueMacroAiLength} macro-AI bytes); {economy.Staff.Candidates.Count} candidates were drawn." );
		return imported;
	}

	/// <summary>
	/// The challenge system: the slots' won and declined counts, whether the system has started offering, a running or offered challenge and the
	/// time of the next event. Slot k is the level's k-th challenge (<c>ChallengesInThisLevel</c>); a save whose slots do not match it keeps the new park's state.
	/// </summary>
	// [BIN:TPW-EXE:0x4d2320 challenge system serializer] after the 20 slots: mCurrentChallenge (+0x3cc, −1 none), mNextChallengeEventTime (+0x3d0, an absolute 100 ns stamp), mSystemSwitchedOn (+0x3d8), mChallengeOn (+0x3d9, accepted and running), mWaitingForReply (+0x3da, an offer is open), mValue (+0x3dc), mActualThing (+0x3e0)
	// [BIN:TPW-EXE:0x4d1e90 daily challenges] before the switch is on, the first offer comes once the park's elapsed day (0x4f8800 / 864e9) reaches DaysUntilFirstChallenge while the park is open (0x4c9050), and the switch is set; then an idle system offers when the clock passes the next event time, and a running challenge ends at its deadline, which the accept (0x4d2df0) stores in the same word
	private static void ImportChallenges( ParkEconomy economy, SaveChallengeSystem system, long calendarStart, List<string> evidence )
	{
		var settings = economy.Settings;
		var level = settings.ChallengesInThisLevel;
		var slots = system.Challenges;
		var matches = slots.Count == level.Count && slots.Select( ( slot, index ) => settings.Challenges.TryGetValue( level[index], out var definition ) && definition.Type == slot.Type ).All( match => match );
		if ( !matches )
		{
			evidence.Add( $"[APPROX:SAVE-005] Challenge state not imported: the save's {slots.Count} slots do not match the level's {level.Count} challenges; the first offer is due on day {economy.Objectives.NextOfferDay}." );
			return;
		}
		long Day( long stamp ) => (stamp - calendarStart) / (OriginalEconomyImport.TimeStampUnitsPerSecond * ParkCalendar.SecondsPerDay);
		var finished = slots.Select( ( slot, index ) => (slot, index) ).Where( entry => entry.slot.Won ).Select( entry => level[entry.index] ).ToList();
		var declines = slots.Select( ( slot, index ) => (slot, index) ).Where( entry => entry.slot.Declined > 0 ).ToDictionary( entry => level[entry.index], entry => entry.slot.Declined );
		var current = system.CurrentChallenge >= 0 && system.CurrentChallenge < slots.Count ? system.CurrentChallenge : -1;
		var nextPosition = current >= 0 ? (current + 1) % slots.Count : 0;
		long nextOffer = settings.ChallengeTiming.DaysUntilFirstChallenge;
		int? running = null, offered = null;
		long deadline = 0;
		string state;
		if ( system.Switch1 == 0 )
			state = $"not started: the first offer is due on park day {nextOffer}";
		else if ( system.Switch2 != 0 && current >= 0 )
		{
			running = level[current];
			// the deadline is passed at the first daily update after the stamp
			deadline = Day( system.NextEventTime ) + 1;
			nextOffer = deadline;
			state = $"challenge {running} running until day {deadline} with value {system.Value}";
		}
		else if ( system.Switch3 != 0 && current >= 0 )
		{
			offered = level[current];
			state = $"challenge {offered} offered and unanswered";
		}
		else
		{
			// an idle system offers at the first daily update after the stamp
			nextOffer = Day( system.NextEventTime ) + 1;
			state = $"the next offer is due on day {nextOffer}";
		}
		economy.RestoreOriginalChallenges( nextOffer, nextPosition, running, deadline, system.Value, offered, declines, finished );
		evidence.Add( $"Challenges imported: {state}; {finished.Count} won, {declines.Count} declined." );
		// [APPROX:SAVE-005] the loss counts, the follow-up's carried object (mActualThing and the slots' mThingIdForFollowup, ECON-055) and the day an unanswered offer was made are not imported, and the first offer does not wait for an open park (0x4c9050) — evidence needed: what reads the loss counts and how the follow-up types read the carried object
		evidence.Add( $"[APPROX:SAVE-005] Challenge values not imported: losses {string.Join( "/", slots.Select( slot => slot.Lost ) )}, ActualThing {system.ActualThing}, follow-up things {string.Join( "/", slots.Select( slot => slot.FollowupThing ) )}." );
	}

	/// <summary>
	/// The research lab: a project whose points done reach its cost is researched, a started one keeps its points, each category's effort and current
	/// project come from its group record and the lab's work load from the tail. Items OpenTPW does not list, and current projects it would not research
	/// now, are reported.
	/// </summary>
	// [BIN:TPW-EXE:0x502dc0 research lab serializer] 256 projects (0x504210), five category records (0x504760) in ResearchCategory order, then mNumProjects, mWorkLoad, mMostExpensiveProject, mLabActive; used projects are the first mNumProjects
	// [BIN:TPW-EXE:0x504630 research points] points are added to mResearchDone while it is below mCost; reaching the cost marks the object researched (its object-table record +0x10) and notifies
	private static void ImportResearch( ParkEconomy economy, SaveResearchLab lab, List<string> evidence )
	{
		var research = economy.Research;
		var used = Math.Clamp( BitConverter.ToInt32( lab.Tail, 0 ), 0, SaveResearchLab.ProjectRecords );
		var initial = research.Completed.Count;
		var completed = new HashSet<(int, int)>( research.Completed );
		var progress = new List<((int, int) Key, long Points)>();
		var unknown = new List<string>();
		for ( var index = 0; index < used; index++ )
		{
			var project = lab.Project( index );
			var key = ((int)project.ObjectId, (int)project.UpgradeLevel);
			if ( !research.HasItem( key.Item1, key.Item2 ) )
			{
				unknown.Add( $"{project.ObjectId} level {project.UpgradeLevel}" );
				continue;
			}
			if ( project.Cost > 0 && project.Done >= project.Cost )
				completed.Add( key );
			else if ( project.Done > 0 )
				progress.Add( (key, (long)Math.Round( project.Done * (double)ParkResearch.PointScale )) );
		}
		var effort = Enumerable.Range( 0, SaveResearchLab.GroupRecords ).Select( index => lab.Group( index ).Effort ).ToArray();
		research.Restore( completed, progress, effort );
		research.SetWorkload( lab.WorkLoad );
		var current = new List<string>();
		var refused = new List<string>();
		for ( var index = 0; index < SaveResearchLab.GroupRecords; index++ )
		{
			var group = lab.Group( index );
			var category = (ResearchCategory)index;
			if ( group.CurrentProject < 0 || group.CurrentProject >= used )
				continue;
			var project = lab.Project( group.CurrentProject );
			if ( research.TrySelect( category, project.ObjectId, project.UpgradeLevel ) )
				current.Add( $"{category} {project.ObjectId}" );
			else
				refused.Add( $"{category} {project.ObjectId} level {project.UpgradeLevel}" );
			if ( research.HighestOpenGroup( category ) != group.HighestGroup )
				evidence.Add( $"[APPROX:SAVE-007] Research category {category}: the save's highest open group {group.HighestGroup} differs from OpenTPW's {research.HighestOpenGroup( category )}, which is kept." );
		}
		evidence.Add( $"Research imported: {used} projects, {progress.Count} started, {completed.Count - initial} researched by the lab, effort {string.Join( "/", effort )}, work load {lab.WorkLoad}, current {(current.Count == 0 ? "none" : string.Join( ", ", current ))}." );
		// [APPROX:SAVE-007] lab projects OpenTPW does not list are dropped, a saved current project OpenTPW would not research now is not selected (OpenTPW then picks its own), and the save's highest open group is recomputed from the researched items — evidence needed: the object table's researched flags (+0x10, not in the lab block), which mark items researched outside the lab
		var dropped = new List<string>();
		if ( unknown.Count > 0 )
			dropped.Add( $"projects OpenTPW does not list ({string.Join( ", ", unknown )})" );
		if ( refused.Count > 0 )
			dropped.Add( $"current projects OpenTPW would not research now ({string.Join( ", ", refused )})" );
		dropped.Add( "the object table's researched flags (items researched outside the lab keep OpenTPW's level start)" );
		evidence.Add( $"[APPROX:SAVE-007] Research not imported: {string.Join( "; ", dropped )}." );
	}

	private static void ReportNotImported( OriginalParkImport save, List<string> evidence )
	{
		// [APPROX:SAVE-002] the saved guests (model 1) are not imported; OpenTPW's guest simulation starts with its own arrivals — evidence needed: decoded person/guest fields and an OpenTPW guest save target
		evidence.Add( $"[APPROX:SAVE-002] {save.GuestCount} saved guests are not imported." );
		// [APPROX:SAVE-010] the opaque blocks (scripts, particles, messages, clock words, cadence, track/flying rides, RSSE, camera, coasters, advisor presentation, sound, cheats, advisor scoring, UI), the weather, park analyser, tag system, mechanic HQ and UI message receiver things, the world state, arrival variables and arrival bytes, the calendar session start and the advisor controller's time signature, sound, three raw words, mRequestedLastTutorial and mMessagePlaying are not imported (its current record is imported as the last tutorial advice when an automatic advisor attaches), nor are its pending advice and history when no automatic advisor is attached; of the cell map only path cells, occupancy, MAP attributes and each path cell's texture and turn are read, and a saved path tile is drawn only while the cell's path neighbours equal the saved ones and its turn is 0/90/180/270 (otherwise the tile is chosen from the neighbours); object scripts restart from their archives, where the original's RSSE loader (tp.exe 0x5597A0: header RSSE, then per script its 0xF4-byte record and saved buffers, then its OBJ list) restores every script's saved state — evidence needed: decoded block layouts and the original's post-load reconstruction
		evidence.Add( $"[APPROX:SAVE-010] Not imported: {string.Join( ", ", save.Layout.Blocks.Where( block => block.Block is not (SaveBlock.ActionRecording or SaveBlock.World or SaveBlock.RideSystem) ).Select( block => $"{block.Block} {block.Length} bytes" ) )}; {Things( save.World, SaveThingModel.Weather, SaveThingModel.ParkAnalyser, SaveThingModel.TagSystem, SaveThingModel.MechanicHq, SaveThingModel.UIMsgReceiver )}." );
		var variables = save.World.Variables;
		evidence.Add( $"[APPROX:SAVE-010] World values not imported: world state {variables.WorldState}, arrival vehicle sizes {variables.ArrivalVehicleSize1}/{variables.ArrivalVehicleSize2}/{variables.ArrivalVehicleSize3}, current arrival vehicle thing {variables.CurrentArrivalVehicle}, arrival bytes {Convert.ToHexString( save.World.Arrivals )}, calendar session start {save.World.Calendar.SessionStart}." );
		if ( save.Advisor is { } advisor )
		{
			var current = advisor.Current;
			evidence.Add( $"[APPROX:SAVE-010] Advisor controller values not imported: time signature {advisor.TimeSignature}, sound {advisor.Sound}, raw words {string.Join( "/", advisor.RawWords.Select( word => $"0x{word:X8}" ) )}, current record (the last tutorial advice, imported when the advisor attaches; {(current.Valid ? "valid" : "free")}, message {current.MessageId}, thing {current.ThingId}, secondary {current.Secondary}, score {current.Score}, response {current.ResponseNo}), mRequestedLastTutorial 0x{advisor.RequestedLastTutorial:X2}, mMessagePlaying {advisor.MessagePlaying}." );
			evidence.Add( $"[APPROX:SAVE-010] Saved advisor pending advice and history ({AdvisorRecords( advisor )}) are imported only when the automatic advisor attaches to the level, which reports its outcome below; without an attached advisor they are dropped." );
		}
	}

	/// <summary>The saved pending advice and history an advisor import would take, for evidence lines.</summary>
	public static string AdvisorRecords( SaveAdvisorController advisor )
	{
		ArgumentNullException.ThrowIfNull( advisor );
		return $"{advisor.Pending.Count( record => record.Valid )} valid pending advice and {advisor.History.Count( entry => !entry.IsInitial )} non-initial history entries";
	}

	private static string Things( SaveWorld world, params SaveThingModel[] models ) =>
		string.Join( ", ", models.Select( model => $"{model} things ({string.Join( ", ", world.OfModel( model ).Select( thing => thing.Id ) )})" ) );
}
