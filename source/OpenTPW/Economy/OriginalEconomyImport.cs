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
/// <item>Placed objects and fixed items (registered without charge) with their attraction's price, cost of goods,
/// upgrade level, state of repair and life gauge.</item>
/// <item>Staff (models 4–8) with grade, training percentage, happiness, state and hire time.</item>
/// </list>
/// Every value that is read but not imported is listed in <see cref="Evidence"/> with its SAVE approximation.
/// </summary>
public sealed class OriginalEconomyImport
{
	/// <summary>The original's 100 ns time-stamp units per second (TbTimeStamp, as the calendar's FunnyTimeStart).</summary>
	public const long TimeStampUnitsPerSecond = 10_000_000;

	private OriginalEconomyImport( SaveEconomyRecords records, bool parkClosed, int objects, int fixedItems, int staff, IReadOnlyList<string> evidence )
	{
		Records = records;
		ParkClosed = parkClosed;
		ImportedObjects = objects;
		ImportedFixedItems = fixedItems;
		ImportedStaff = staff;
		Evidence = evidence;
	}

	public SaveEconomyRecords Records { get; }
	/// <summary>The save's <c>mParkClosed</c> word is nonzero.</summary>
	public bool ParkClosed { get; }
	public int ImportedObjects { get; }
	public int ImportedFixedItems { get; }
	public int ImportedStaff { get; }
	public IReadOnlyList<string> Evidence { get; }

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
		var challenges = save.Economy.ChallengeSystem;
		evidence.Add( $"[APPROX:SAVE-005] Challenge system current challenge {challenges.CurrentChallenge} and next event time {challenges.NextEventTime} are not imported; the first offer is due on day {economy.Objectives.NextOfferDay}." );
		ImportBank( economy, save.Economy.BankAccount, evidence );
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
		// [APPROX:SAVE-001] the save's world generator state (mRandomSeed) is reported but not imported: OpenTPW's economy, guest and script streams keep their world-seed derivation — evidence needed: DET-I2 port of the world LCG (DET-005 limits the restore to this one field)
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
		var zeroAprRepayments = 0;
		for ( var index = 0; index < records.Loans.Count; index++ )
		{
			var saved = records.Loans[index];
			var offer = settings.Loans[index];
			if ( saved.Amount != offer.Amount || saved.AprPercent != offer.AprPercent || saved.Months != offer.Months
				|| saved.LenderNameIndex != offer.LenderNameIndex )
				throw new InvalidDataException( $"Save loan offer {index} ({saved.Amount}/{saved.AprPercent} % APR/{saved.Months} months/lender {saved.LenderNameIndex}) differs from LoanInfo[{offer.Index}] ({offer.Amount}/{offer.AprPercent}/{offer.Months}/lender {offer.LenderNameIndex})." );
			if ( saved.AprPercent == 0 )
			{
				var expected = offer.Amount / offer.Months;
				if ( saved.MonthlyRepayment != expected )
					throw new InvalidDataException( $"Save loan offer {index} zero-APR monthly repayment {saved.MonthlyRepayment} differs from floor(amount/months) = {expected}." );
				zeroAprRepayments++;
			}
			else
				evidence.Add( $"[APPROX:ECON-006] Save loan offer {index} monthly repayment {saved.MonthlyRepayment} at {saved.AprPercent} % APR is unverified: original PC repayment arithmetic is not established; this stored value was not validated against the runtime approximation." );
		}
		evidence.Add( $"{records.Loans.Count} loan offers match LoanInfo amounts, APR, terms and lenders." );
		if ( zeroAprRepayments != 0 )
			evidence.Add( $"{zeroAprRepayments} zero-APR monthly repayments equal floor(amount/months)." );
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
			bought.Add( new LoanAccount( loan.Index, loan.LenderNameIndex, loan.Amount, loan.AprPercent, loan.Months, loan.MonthlyRepayment, left, (long)loan.MonthlyRepayment * left ) );
		}
		// [BIN:STP-PPC:0x100CBA54 bank serializer] admission fee, balance and mProfitThisYear (+292) are the bank words of the save; bought loans keep offer, amount, APR, term, repayment and months repaid
		economy.ImportOriginalBank( account.Balance, account.AdmissionFee, account.ProfitThisYear, bought );
		evidence.Add( $"Bank imported: balance {account.Balance}, admission fee {account.AdmissionFee}, profit this year {account.ProfitThisYear}, {bought.Count} bought loans." );
		// [APPROX:SAVE-008] bank words without an OpenTPW counterpart (batch balance, last balance, withdrawals flag, turn entered red) are not imported; the ledger month opens at the current balance because mLastBalance, the candidate opening-balance word, is the balance after the last debit (withdrawal 0x100CBFDC), not a month opening; a bought loan's outstanding balance is its monthly repayment times the months left — evidence needed: a save with a bought loan or a negative balance, and the original's month-opening balance
		evidence.Add( $"[APPROX:SAVE-008] Not imported: batch balance {account.BatchBalance}, last balance (mLastBalance, balance after the last debit; the month opens at the current balance {account.Balance}) {account.LastBalance}, withdrawals {(account.WithdrawalsEnabled ? "enabled" : "disabled")}, turn entered red {account.TurnEnteredRed}{(account.Balance < 0 ? "; the park is in the red but its months in the red start at 0" : "")}." );
	}

	private static (int Objects, int FixedItems) ImportObjects( ParkEconomy economy, OriginalParkImport save, List<string> evidence )
	{
		var objects = 0;
		var fixedItems = 0;
		var unmatched = 0;
		var notImported = new List<string>();
		foreach ( var record in save.PlacedObjects.Select( item => item.Record ).Concat( save.FixedItems ) )
		{
			var state = economy.RegisterExisting( record.InfoId );
			if ( record.IsFixedItem )
				fixedItems++;
			else
				objects++;
			if ( save.FindAttraction( record ) is not { } attraction )
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
		evidence.Add( $"Registered {objects} placed objects and {fixedItems} fixed items without charge; {objects + fixedItems - unmatched - notImported.Count} took their attraction's price, cost of goods, upgrade level, state of repair and life gauge." );
		if ( unmatched != 0 || notImported.Count != 0 )
			evidence.Add( $"{unmatched} objects have no matching attraction thing; outside OpenTPW's ranges and kept at their defaults: {string.Join( ", ", notImported )}." );
		// [APPROX:SAVE-006] an attraction's saved state enum, chance of winning, third gauge, histories and totals are not imported (open/broken state comes from its script) — evidence needed: the attraction state values and the chance-of-winning/losing relation
		evidence.Add( $"[APPROX:SAVE-006] Not imported for {save.Attractions.Count} attraction things: state ({string.Join( ", ", save.Attractions.Select( item => item.State ).Distinct().Order() )}), chance of winning, third gauge, histories, totals." );
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
				hired = ParkCalendar.TickOfTurn( seconds / ParkCalendar.SecondsPerTurn );
			else
				adjusted.Add( $"{saved.Model} thing {saved.Id} hired time stamp {staff.HiredTimestamp} lies outside the park's calendar (start {start}, turn {ParkCalendar.Turn( economy.Tick )}), imported as hired at tick 0" );
			// [APPROX:SAVE-003] saved staff are imported with their state enum read in STAFFSTATES.str order, their inline name replaced by name index 0 and no job, patrol region, rest area or position — evidence needed: the staff state values and an inline-name field in OpenTPW's staff record
			var state = staff.State <= (uint)StaffState.PickedUp ? (StaffState)staff.State : StaffState.Idle;
			if ( staff.State > (uint)StaffState.PickedUp )
				adjusted.Add( $"{saved.Model} thing {saved.Id} state {staff.State} has no STAFFSTATES entry, imported as {StaffState.Idle}" );
			// [BIN:STP-PPC:0x100F3D8C staff rest] happiness is clamped to [0, 100] by every rest, idle and work update
			var happiness = (int)Math.Clamp( staff.Happiness, 0, 100 );
			if ( happiness != staff.Happiness )
				adjusted.Add( $"{saved.Model} thing {saved.Id} happiness {staff.Happiness} imported as {happiness} (traced range 0-100, whole numbers)" );
			// OpenTPW keeps 0-99 training points and promotes at 100 (ECON-008), so a saved 100 % is imported as 99 and reported
			var points = Math.Min( staff.GradePercentage, ParkStaff.TrainingPointsPerGrade - 1 );
			if ( points != staff.GradePercentage )
				adjusted.Add( $"{saved.Model} thing {saved.Id} grade percentage {staff.GradePercentage} imported as {points} (OpenTPW promotes at {ParkStaff.TrainingPointsPerGrade})" );
			economy.Staff.AddImported( type, staff.Grade, points, happiness, state, hired );
			imported++;
		}
		evidence.Add( $"Imported {imported} staff ({string.Join( ", ", save.Staff.Select( item => $"{item.Model} grade {item.Staff.Grade}" ) )})." );
		foreach ( var adjustment in adjusted )
			evidence.Add( $"[APPROX:SAVE-003] Adjusted on import: {adjustment}." );
		if ( imported != 0 )
			evidence.Add( $"[APPROX:SAVE-003] Staff states {string.Join( ", ", save.Staff.Select( item => item.Staff.State ) )} read in STAFFSTATES order; inline names ({string.Join( ", ", save.Staff.Select( item => item.Staff.Name ) )}), jobs, patrol regions, rest areas, energy and positions are not imported." );
		if ( save.StaffHq is { } hq )
		{
			if ( hq.TrainingBudgets.All( budget => budget == 0 ) )
				evidence.Add( "Staff HQ training budgets are all 0, as in a new park." );
			else
				// [APPROX:SAVE-009] the staff HQ's five training budgets are imported only when all are zero (their role order is not traced); strike levels are not imported — evidence needed: the role order of the budget loop 0xfc108
				evidence.Add( $"[APPROX:SAVE-009] Staff HQ training budgets {string.Join( ", ", hq.TrainingBudgets )} are not imported (role order not traced)." );
		}
		// [APPROX:SAVE-004] the macro-AI hiring pool is not decoded; the new park draws its own pool, with its timers moved to the imported turn — evidence needed: the candidate-pool serializer 0xf5f1c widths
		evidence.Add( $"[APPROX:SAVE-004] The save's hiring pool ({save.World.OpaqueMacroAiLength} opaque control-manager and macro-AI bytes) is not imported; {economy.Staff.Candidates.Count} candidates were drawn." );
		return imported;
	}

	private static void ReportNotImported( OriginalParkImport save, List<string> evidence )
	{
		// [APPROX:SAVE-002] the saved guests (model 1) are not imported; OpenTPW's guest simulation starts with its own arrivals — evidence needed: decoded person/guest fields and an OpenTPW guest save target
		evidence.Add( $"[APPROX:SAVE-002] {save.GuestCount} saved guests are not imported." );
		// [APPROX:SAVE-007] research projects and groups are not imported; research starts from the level settings — evidence needed: the research project and group field widths
		if ( save.ResearchLab != null )
			evidence.Add( $"[APPROX:SAVE-007] Research lab ({SaveResearchLab.ProjectRecords} project and {SaveResearchLab.GroupRecords} group records) is not imported." );
		// [APPROX:SAVE-010] the opaque blocks (scripts, particles, messages, clock words, cadence, track/flying rides, RSSE, camera, coasters, advisor presentation, sound, cheats, advisor scoring, UI), the weather, park analyser, tag system, mechanic HQ and UI message receiver things, the world state, arrival variables and arrival bytes, the calendar session start and the advisor controller's time signature, sound, three raw words, current record, mRequestedLastTutorial and mMessagePlaying are not imported, and of the cell map only path cells, occupancy and MAP attributes are read; object scripts restart from their archives — evidence needed: decoded block layouts and the original's post-load reconstruction
		evidence.Add( $"[APPROX:SAVE-010] Not imported: {string.Join( ", ", save.Layout.Blocks.Where( block => block.Block is not (SaveBlock.ActionRecording or SaveBlock.World or SaveBlock.RideSystem) ).Select( block => $"{block.Block} {block.Length} bytes" ) )}; {Things( save.World, SaveThingModel.Weather, SaveThingModel.ParkAnalyser, SaveThingModel.TagSystem, SaveThingModel.MechanicHq, SaveThingModel.UIMsgReceiver )}." );
		var variables = save.World.Variables;
		evidence.Add( $"[APPROX:SAVE-010] World values not imported: world state {variables.WorldState}, arrival vehicle sizes {variables.ArrivalVehicleSize1}/{variables.ArrivalVehicleSize2}/{variables.ArrivalVehicleSize3}, current arrival vehicle thing {variables.CurrentArrivalVehicle}, arrival bytes {Convert.ToHexString( save.World.Arrivals )}, calendar session start {save.World.Calendar.SessionStart}." );
		if ( save.Advisor is { } advisor )
		{
			var current = advisor.Current;
			evidence.Add( $"[APPROX:SAVE-010] Advisor controller values not imported: time signature {advisor.TimeSignature}, sound {advisor.Sound}, raw words {string.Join( "/", advisor.RawWords.Select( word => $"0x{word:X8}" ) )}, current record ({(current.Valid ? "valid" : "free")}, message {current.MessageId}, thing {current.ThingId}, secondary {current.Secondary}, score {current.Score}, response {current.ResponseNo}), mRequestedLastTutorial 0x{advisor.RequestedLastTutorial:X2}, mMessagePlaying {advisor.MessagePlaying}." );
		}
	}

	private static string Things( SaveWorld world, params SaveThingModel[] models ) =>
		string.Join( ", ", models.Select( model => $"{model} things ({string.Join( ", ", world.OfModel( model ).Select( thing => thing.Id ) )})" ) );
}
