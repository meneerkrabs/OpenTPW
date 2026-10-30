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
/// <item>Staff (models 4–8) with grade, training percentage, happiness, energy, name, hire time, idle stamp, patrol rectangle, job and its
/// target, and the destination their walk goes on to.</item>
/// </list>
/// Every value that is read but not imported is listed in <see cref="Evidence"/> with its SAVE approximation. The
/// saved advisor controller's pending advice and history are imported later by the automatic advisor, which adds its
/// outcome through <see cref="Report"/>.
/// </summary>
public sealed partial class OriginalEconomyImport
{
	/// <summary>The original's 100 ns time-stamp units per second (TbTimeStamp, as the calendar's FunnyTimeStart).</summary>
	public const long TimeStampUnitsPerSecond = 10_000_000;

	private readonly List<string> evidence;

	private OriginalEconomyImport( SaveEconomyRecords records, bool parkClosed, int objects, int fixedItems, int staff, List<string> evidence,
		IReadOnlyDictionary<int, int> attractionInstances )
	{
		AttractionInstances = attractionInstances;
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
	/// <summary>The economy object id of each imported attraction thing, by its thing id (the save's references to attractions name them so).</summary>
	public IReadOnlyDictionary<int, int> AttractionInstances { get; }
	/// <summary>
	/// The park turns of the analyser's saved <c>mLastObjectBuilt</c> and <c>mLastStaffHired</c>, the newest construction and the newest hire, which the automatic advisor takes as its
	/// stamps when it attaches; null when the save names no analyser.
	/// </summary>
	public (long ObjectTurn, long HireTurn)? AnalyserStampTurns { get; private init; }

	/// <summary>Adds the outcome of a later import step (the automatic advisor's controller import) to <see cref="Evidence"/>.</summary>
	public void Report( string line )
	{
		ArgumentException.ThrowIfNullOrEmpty( line );
		evidence.Add( line );
	}

	/// <summary>Imports the original save of <paramref name="park"/>'s level.</summary>
	/// <param name="shippedSeed">The save is the level's shipped seed (<c>Easymode.TPWI</c>) that starts a new park, not a player's park: its cheat bytes are not applied (<see cref="ImportCheatMode"/>).</param>
	public static OriginalEconomyImport Apply( ParkEconomy economy, OriginalPark park, bool shippedSeed = false )
	{
		ArgumentNullException.ThrowIfNull( park );
		if ( park.Save == null )
			throw new InvalidOperationException( $"The {park.LevelName} level has no original save." );
		return Apply( economy, park.Save, shippedSeed );
	}

	/// <param name="shippedSeed">The save is the level's shipped seed (<c>Easymode.TPWI</c>) that starts a new park, not a player's park: its cheat bytes are not applied (<see cref="ImportCheatMode"/>).</param>
	public static OriginalEconomyImport Apply( ParkEconomy economy, OriginalParkImport save, bool shippedSeed = false )
	{
		ArgumentNullException.ThrowIfNull( economy );
		ArgumentNullException.ThrowIfNull( save );
		var evidence = new List<string>();
		var world = save.World;
		ImportTurn( economy, world, evidence );
		ImportFirstPurchases( economy, world.ObjectControls, evidence );
		Verify( economy.Settings, save.Economy, evidence );
		ImportBank( economy, save.Economy.BankAccount, evidence );
		if ( save.ResearchLab is { } lab )
			ImportResearch( economy, lab, save.World.ObjectControls, CheatModeOn( economy, save, shippedSeed ), evidence );
		economy.Research.RestoreRecentItems( save.RecentResearch );
		if ( save.RecentResearch != null )
			evidence.Add( "Park analyser: four recent-research rings imported, including scratch and inactive slots." );
		if ( save.Statistics is { } statistics )
		{
			economy.StatisticsHistory.RestoreOriginal( statistics );
			evidence.Add( "Park analyser: five monthly statistics rings, live arrivals and recent stays imported; inactive host slots normalized, financial history unchanged." );
		}
		else
			economy.StatisticsHistory.Restore( new ParkStatisticsHistory().Capture() );
		var stampTurns = ImportAnalyserStamps( economy, save, evidence );
		// [BIN:TPW-EXE:0x004C5D70 analyser serializer] the guards' chase counters are three raw words, read back as they are: the advisor's guard and prank lines (12, 22, 98, 264) test them
		if ( save.AnalyserCounters is { } misbehaving )
		{
			economy.RestoreMisbehavingKids( misbehaving.MisbehavingKids, misbehaving.GotAway, misbehaving.Ever );
			evidence.Add( $"Park analyser: the guards' chase counters imported (this month {misbehaving.MisbehavingKids}, got away {misbehaving.GotAway}, ever {misbehaving.Ever})." );
		}
		var (objects, fixedItems, things) = ImportObjects( economy, save, evidence );
		ImportLitter( economy, save, evidence );
		// after the objects: the carried object of a follow-up is one of their thing ids
		ImportChallenges( economy, save.Economy.ChallengeSystem, world.Calendar.FunnyTimeStart, things, evidence );
		var staff = ImportStaff( economy, save, things, evidence );
		ImportCheatMode( economy, save, shippedSeed, evidence );
		var lostImported = !economy.Rules.ThemeParkInc && save.ThemeParkIncWorld == null && world.Variables.WorldState == 4;
		if ( lostImported )
		{
			economy.RestoreOriginalLoss( world.Variables.ParkClosed == 0 );
			evidence.Add( "World state 4 imported: lost park, retaining the saved gate-open bit without replaying bankruptcy events." );
		}
		ReportNotImported( save, evidence, lostImported );
		// [BIN:STP-PPC:0x10106708 world reader] mParkClosed is restored with the world variables (0 = open)
		var closed = world.Variables.ParkClosed != 0;
		evidence.Add( $"Park {(closed ? "closed" : "open")} (mParkClosed {world.Variables.ParkClosed}), {world.Variables.NumberOfVisitorsToDate} visitors to date (the guest simulation's count)." );
		return new OriginalEconomyImport( save.Economy, closed, objects, fixedItems, staff, evidence, things.ToDictionary( pair => pair.Key, pair => pair.Value.Id ) )
		{
			AnalyserStampTurns = stampTurns
		};
	}

	/// <summary>The cheat mode of the save's <c>CHTS</c> chunk (docs/CHEATS.md): byte 0 on marks the park as cheated, with or without the cheats extension; a Theme Park Inc park keeps it unapplied.</summary>
	// [BIN:TPW-EXE:0x405570 cheat load] the CHTS chunk's two bytes go to +0xA (the cheat mode, tested whole by 0x405560) and +0xB of the cheat object 0xFB3520; loading a save replaces the mode, so a park saved with it on is a cheated park
	/// <summary>Whether <see cref="ImportCheatMode"/> turns the cheat mode on: a Theme Park World player's save (not the shipped seed) whose <c>CHTS</c> byte 0 is nonzero.</summary>
	private static bool CheatModeOn( ParkEconomy economy, OriginalParkImport save, bool shippedSeed ) =>
		!shippedSeed && !economy.Rules.ThemeParkInc && save.RuntimeBlocks is { Cheats: { Length: > 0 } cheats } && cheats[0] != 0;

	private static void ImportCheatMode( ParkEconomy economy, OriginalParkImport save, bool shippedSeed, List<string> evidence )
	{
		if ( save.RuntimeBlocks is not { Cheats: { Length: > 0 } cheats } )
			return;
		if ( economy.Rules.ThemeParkInc )
		{
			evidence.Add( $"Cheat bytes {Convert.ToHexString( cheats )} not applied: Theme Park Inc's cheat mode is not traced (docs/CHEATS.md)." );
			return;
		}
		// [EXT:cheats-shipped-seed] the jungle's shipped Easymode.TPWI was saved with the cheat mode on (CHTS 01 01), so in the original every park started from it (an Instant Action player's copy, 0x137600 on the Mac) inherits the mode; OpenTPW starts such a park unmarked, so a player who never cheated has a park that saves and hashes as before and is not shown as cheated
		if ( shippedSeed )
		{
			evidence.Add( $"Cheat bytes {Convert.ToHexString( cheats )} of the shipped seed not applied: a new park starts without the cheat mode ([EXT:cheats-shipped-seed], docs/CHEATS.md)." );
			return;
		}
		economy.Cheats.ImportMode( cheats[0] != 0 );
		evidence.Add( $"Cheat mode {(cheats[0] != 0 ? "on: the park is marked as cheated" : "off")} (CHTS byte 0 = {cheats[0]}){(cheats.Length > 1 ? $"; byte 1 = {cheats[1]}, which only the cheat object's constructor and the loader write, is not kept (APPROX:SAVE-010)" : "")}." );
	}

	private static void ImportTurn( ParkEconomy economy, SaveWorld world, List<string> evidence )
	{
		// [BIN:STP-PPC:0x10105EFC world writer] mGameTick (world +0x1DA70C) is saved as u32 and restored by the reader at 0x10106890; the calendar converts it (0x100E4394)
		economy.StartAtOriginalTurn( world.Variables.GameTick );
		var date = ParkCalendar.DateTimeAt( economy.Tick );
		var calendar = world.Calendar;
		var epoch = (ParkCalendar.Epoch - new DateTime( 1601, 1, 1 )).Ticks;
		// [BIN:TPW-EXE:0x4F7E80 calendar constructor] an unplayed park (restart.INTS) keeps the constructor's calendar: the park's start (2000-01-01), month cache 0, day cache -1; the first update fills the caches
		// its first update posts a day message (no date equals day -1; the world init 0x5156A0 syncs only the month and year caches, 0x4F85A0). Keep that pending message until an actual turn.
		var unplayed = calendar.IsInitial && world.Variables.GameTick == 0;
		economy.PendingInitialDay = unplayed;
		var matches = unplayed || calendar.FunnyTimeStart == epoch && calendar.FunnySecsPerRealSec == ParkCalendar.SecondsPerTurn * 4
			&& calendar.MonthAtLastUpdate == date.Month - 1 && calendar.DayAtLastUpdate == date.Day;
		evidence.Add( $"Park turn {world.Variables.GameTick} imported ({ParkCalendar.ToDate( economy.Tick )}); the save's calendar (rate {calendar.FunnySecsPerRealSec}, month cache {calendar.MonthAtLastUpdate}, day cache {calendar.DayAtLastUpdate}) {(matches ? "agrees" : "does not agree")} with that date." );
		// [BIN:TPW-EXE:0x0051710F world generator word] the world writer saves the u32 at world +0x1DA708 raw under the name "mRandomSeed" (0x00517123 with the name at 0x00762924) and the loader reads it back raw (0x00517DFC): it is the whole state of the one world generator 0x00516330, which the park continues from
		economy.World.Restore( world.Variables.RandomSeed );
		evidence.Add( $"World generator state mRandomSeed 0x{world.Variables.RandomSeed:X8} imported: the park's draws continue the saved stream." );
	}

	/// <summary>
	/// Restores every object type's first-purchase turn from the save's object-control table, before the saved objects are registered: the registration stamps the import turn into a
	/// type only while it is still 0, as the original's constructor does.
	/// </summary>
	// [BIN:TPW-EXE:0x004D3AA0 object controls] the world loader reads the control manager's table (0x005179C0 calls 0x004D3AA0 at 0x005181E7) before the saved things, so every record's +0x1C (the first purchase, 0x004DB690) comes back verbatim
	// [BIN:TPW-EXE:0x004DB690 first purchase] a constructed object stamps the game turn into its type's record +0x1C only while it is 0, so a type the table lacks (0x004D3E00 adds a zeroed record) or holds at 0 is stamped when its first saved object is built, at the saved turn that ImportTurn has restored
	internal static void ImportFirstPurchases( ParkEconomy economy, SaveObjectControls? controls, List<string> evidence )
	{
		ArgumentNullException.ThrowIfNull( economy );
		ArgumentNullException.ThrowIfNull( evidence );
		if ( controls == null )
		{
			evidence.Add( "First purchases not imported: the save's object-control table does not read; each type is stamped with the import turn." );
			return;
		}
		var restored = 0;
		foreach ( var control in controls.Controls )
		{
			if ( control.Word1C == 0 )
				continue;
			economy.Counters.Max( ParkCounters.FirstPurchaseTurn( control.Id ), control.Word1C );
			restored++;
		}
		evidence.Add( $"First purchases imported: {restored} object types carry the turn of their first purchase from the save's object-control table." );
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
		// [BIN:TPW-EXE:0x4d0f90 challenge slot serializer] the save holds the 20 challenge slots with their own definitions (type, time, value, objects, staff type, prize, follow-up type and the two flags), and a load takes them as they are:
		// a slot that differs from the level's own challenge (a modified save, another count) is a challenge of its own in this park
		var level = settings.ChallengesInThisLevel;
		var different = records.Challenges.Count != level.Count || records.Challenges.Where( ( saved, index ) => !settings.Challenges.TryGetValue( level[index], out var definition ) || saved.Type != definition.Type || saved.TargetTime != definition.TargetTime
			|| saved.TargetValue != definition.TargetValue || saved.TargetObject != definition.TargetObject || saved.TargetObject2 != definition.TargetObject2 || saved.TargetStaffType != definition.TargetStaffType || saved.Prize != definition.Prize
			|| saved.FollowupType != definition.FollowupType || saved.CheckAtEndOnly != definition.CheckAtEndOnly || saved.Independent != definition.Independent ).Any();
		if ( !different )
		{
			evidence.Add( $"{level.Count} challenge records equal Challenges[{string.Join( ", ", level )}] in ChallengesInThisLevel order." );
			return;
		}
		settings.AdoptChallenges( records.Challenges.Select( ( saved, index ) => new ChallengeDefinition( index, saved.Type, saved.FollowupType, saved.TargetTime, saved.TargetValue, saved.TargetObject, saved.TargetObject2,
			saved.TargetStaffType, saved.Prize, saved.CheckAtEndOnly, saved.Independent ) ).ToList() );
		evidence.Add( $"Challenge slots adopted from the save: its {records.Challenges.Count} slots differ from the level's {level.Count} challenges Challenges[{string.Join( ", ", level )}], so the park plays the saved definitions (keys {string.Join( ", ", settings.ChallengesInThisLevel )})." );
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
		economy.RestoreBankWords( account.WithdrawalsEnabled, account.LastBalance, account.TurnEnteredRed, account.BatchBalance );
		evidence.Add( $"Bank words imported: withdrawals {(account.WithdrawalsEnabled ? "enabled" : "disabled")}, last balance {account.LastBalance}, turn entered red {account.TurnEnteredRed}{(account.Balance < 0 && account.LastBalance < 0 ? $" ({economy.MonthsSinceEnteringRed()} months in the red)" : "")}." );
		// [BIN:TPW-EXE:0x4d0850 loan payoff] a bought loan's outstanding amount is its monthly repayment (+0x24) times the months not yet repaid (+0x20 − +0x2C), compared unsigned with the balance (0x4d0898)
		// [BIN:TPW-EXE:0x4d0370 bank month] the batch word (+0x10) is a pending credit: the bank's month message adds it to the balance, the money-in total and the year's profit and zeroes it (0x4D0393..0x4D03DB), so it is kept and paid at the next month end; the constructor (0x4CF7C0) starts it at 0, and the bank keeps no month-opening word (mLastBalance is the balance after the last debit, instalment or payoff, 0x4D0228, 0x4D08E1), so the ledger month opens at the current balance
		if ( account.BatchBalance < 0 )
			throw new InvalidDataException( $"Save bank batch balance {account.BatchBalance} is negative." );
		evidence.Add( $"Bank batch credit imported: {account.BatchBalance}{(account.BatchBalance == 0 ? "" : " (paid into the balance at the next month end)")}; the month opens at the current balance {account.Balance}." );
		// [BIN:TPW-EXE:0x4d06b0 credit test] each offer's +0x14 is the stored result of the last list build's credit test (the constructor 0x4cf7c0 starts it as amount < 10001); the Available tab lists the offers not bought whose test passed
		var available = bank.Loans.Where( loan => loan.Available ).Select( loan => loan.Index ).ToArray();
		economy.RestoreLoanAvailability( available );
		evidence.Add( $"Loan offers' stored credit tests imported: {(available.Length == 0 ? "no offer passed" : $"{(available.Length == 1 ? "offer" : "offers")} {string.Join( ", ", available )} passed")}; the Available tab lists {string.Join( ", ", economy.AvailableLoans.Select( offer => offer.Index ).DefaultIfEmpty( -1 ).Select( index => index < 0 ? "none" : index.ToString() ) )} until the dialog tests them again." );
	}

	/// <summary>The first park tick at or after a saved attraction time stamp (the date and time of the park clock the loader rebuilds); null when it is not a date.</summary>
	private static long? SavedBuildTick( SaveTimestamp stamp )
	{
		try
		{
			var elapsed = (stamp.ToDateTime() - ParkCalendar.Epoch).Ticks;
			return ParkCalendar.TickAtSeconds( Math.Max( 0, (elapsed + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond ) );
		}
		catch ( ArgumentOutOfRangeException )
		{
			return null;
		}
	}

	/// <summary>
	/// The park turns of the analyser's saved object and hire stamps (<see cref="AnalyserStampTurns"/>): the turn of the save's calendar, its start and rate, whose park time each stamp
	/// is. The original's clock writes only such times, so a stamp between two of them (a modified save) is taken at the earlier turn's and reported. A stamp before the calendar's
	/// start or past the park's turn keeps its turn, which the advisor counts days from as the original counts them from the word. The days count on OpenTPW's calendar of 3,750
	/// seconds a turn: for a modified save whose calendar runs at another rate than 15000 they follow the park's own calendar, as its dates do (<see cref="ImportTurn"/> reports that
	/// the save's calendar does not agree).
	/// </summary>
	// [BIN:TPW-EXE:0x004C7680 object stamp] mLastObjectBuilt (+0x21C30) and mLastStaffHired (+0x21C38, 0x004C76D0) take the park time 0x4F8690 returns: the calendar's FunnyTimeStart (+0) plus floor(turn x rate (+0x1C, 15000) / 4) x 10^7 (0x5FC530); only the calendar constructor 0x4F7E80 and the loader 0x4F7F30 write those two words, so every stamp of an original save is a whole turn from the calendar's start
	// [BIN:TPW-EXE:0x005179C0 world loader] the analyser case zeroes the six stamps (0x518A43-0x518A85) and runs the serializer's load (0x518ABA), which reads them back raw (0x4C7289, 0x4C72B3); the analyser constructor 0x4C58C0, which stamps a new park's time, runs only in the new-world setup 0x5156A0, and the loader builds the staff through 0x518E20 and their role serializers, not through the staff init 0x504B90 that restamps mLastStaffHired, and no object through 0x4DB090: a loaded park keeps the saved words until its next construction or hire
	private static (long ObjectTurn, long HireTurn)? ImportAnalyserStamps( ParkEconomy economy, OriginalParkImport save, List<string> evidence )
	{
		if ( save.AnalyserStamps is not { } stamps )
			return null;
		// the World reader refuses a calendar without a positive rate (SaveCalendar.IsUpdated, IsInitial), so every stamp has a turn
		var (start, rate) = (save.World.Calendar.FunnyTimeStart, save.World.Calendar.FunnySecsPerRealSec);
		var adjusted = new List<string>();
		long Turn( string name, long stamp )
		{
			var (turn, rest) = StampTurn( stamp, start, rate );
			if ( rest != 0 )
				adjusted.Add( $"{name} time stamp {stamp} lies {rest / (double)TimeStampUnitsPerSecond} park seconds into turn {turn}, imported as that turn's start" );
			return turn;
		}
		var turns = (ObjectTurn: Turn( "mLastObjectBuilt", stamps.LastObjectBuilt ), HireTurn: Turn( "mLastStaffHired", stamps.LastStaffHired ));
		evidence.Add( $"Park analyser: the stamps of the newest construction, mLastObjectBuilt (turn {turns.ObjectTurn}), and of the newest hire, mLastStaffHired (turn {turns.HireTurn}), imported for the automatic advisor's days-since tests (calendar start {start}, park turn {economy.Turn})." );
		foreach ( var adjustment in adjusted )
			evidence.Add( $"Park analyser value adjusted on import: {adjustment}." );
		return turns;
	}

	/// <summary>
	/// The park turn of a saved 100 ns time stamp on a calendar, whose turns have the park time of its start plus floor(turn x rate / 4) seconds: the first turn of the stamp's second, or
	/// the last turn before it when no turn has that second, and the units the stamp lies past that turn's time; exact for any saved words and a positive rate.
	/// </summary>
	private static (long Turn, long Remainder) StampTurn( long stamp, long calendarStart, int rate )
	{
		var offset = (Int128)stamp - calendarStart;
		var seconds = FloorDivide( offset, TimeStampUnitsPerSecond );
		// the first turn whose second is not before the stamp's: floor(turn x rate / 4) >= seconds exactly when turn x rate >= 4 x seconds
		var turn = -FloorDivide( -4 * seconds, rate );
		if ( FloorDivide( turn * rate, 4 ) > seconds )
			turn--;
		return ((long)turn, (long)(offset - FloorDivide( turn * rate, 4 ) * TimeStampUnitsPerSecond));
	}

	private static Int128 FloorDivide( Int128 value, Int128 divisor )
	{
		var (quotient, remainder) = Int128.DivRem( value, divisor );
		return remainder < 0 ? quotient - 1 : quotient;
	}

	/// <summary>Registers the save's objects; returns the counts and the economy object of each matched attraction thing id.</summary>
	private static (int Objects, int FixedItems, Dictionary<int, ParkObjectState> Things) ImportObjects( ParkEconomy economy, OriginalParkImport save, List<string> evidence )
	{
		var things = new Dictionary<int, ParkObjectState>();
		var objects = 0;
		var fixedItems = 0;
		var unmatched = 0;
		var notImported = new List<string>();
		var adjusted = new List<string>();
		// The order matches ParkObjects.ImportOriginal (placed, unverified footprints, fixed items), which Level.ConnectObjectsToEconomy pairs by Info.Id
		foreach ( var record in save.PlacedObjects.Select( item => item.Record ).Concat( save.UnresolvedObjects ).Concat( save.FixedItems ) )
		{
			var matched = save.FindAttraction( record );
			// [BIN:STP-PPC:0x100DAD90 object purchase] thing +20 is the clock's stamp at the purchase; the saved attraction time stamp is taken as it, and the saved customer count as the use word +412 (raised with the history record at +420 when a guest leaves, 0x100EAA00 → 0x100E1B44), so an imported used or old object never gets the unused first-30-days scrap refund
			// [BIN:TPW-EXE:0x004DC89E attraction loader stamp] on the PC the saved date-time is the stamp at +0x18 and the count mNumCustomers (+0x1A0) that the scrap rule 0x004E2290 reads
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
			// [BIN:TPW-EXE:0x004DC12B walk-away load] the attraction loader reads mNumWalkAways (key 0x0075B508) into +0x1A4, the count the refusal raises (0x004E1670)
			state.WalkAways = attraction.WalkAwayCount;
			ImportRings( state, attraction );
			things.TryAdd( attraction.Id, state );
			// [BIN:STP-PPC:0x100DAF04 attraction serializer] mPricePerUse, mCostOfGoods, mUpgradeLevel and the gauges at +0x40 (state of repair) and +0x44 (life gauge)
			// [BIN:TPW-EXE:0x004DB7D0 attraction serializer] the PC loader puts the three floats at +0x4C, +0x48 (life gauge) and +0x44 (state of repair) and clamps mPricePerUse to 0–500
			if ( attraction.UpgradeLevel <= 2 && attraction.StateOfRepair <= 100 && attraction.LifeGauge <= 100 && attraction.CostOfGoods >= 0 )
			{
				state.Price = attraction.PricePerUse;
				state.CostOfGoods = attraction.CostOfGoods;
				state.Level = attraction.UpgradeLevel;
				state.Repair = attraction.StateOfRepair;
				state.LifeGauge = attraction.LifeGauge;
				// [BIN:TPW-EXE:0x4dcdc1 attraction loader] mAmountOfSpecialIngredient is loaded into +0x198 as saved
				state.IngredientAmount = attraction.AmountOfSpecialIngredient;
				// [BIN:TPW-EXE:0x4dcd29 attraction loader] mQualityOfGoods (+0x18C) and mChanceOfWinning (+0x190) are 4-byte words read without a clamp; the quality getter 0x4e2180 and the win test 0x4e26e2 read their low bytes, the win byte being 100 − the chance of losing (0x4db3a1)
				state.QualityOfGoods = attraction.QualityOfGoods & 0xFF;
				// a win byte of 99 and up always wins (R mod 100 <= byte), as a chance of losing of 0 does
				state.ChanceOfLosingPercent = Math.Clamp( 100 - (attraction.ChanceOfWinning & 0xFF), 0, 100 );
				ImportAttractionState( economy, attraction, state, adjusted );
			}
			else
				notImported.Add( $"{record.InfoId} (level {attraction.UpgradeLevel}, repair {attraction.StateOfRepair}, life {attraction.LifeGauge}, cost of goods {attraction.CostOfGoods})" );
		}
		evidence.Add( $"Registered {objects} placed objects ({save.UnresolvedObjects.Count} with an unverified footprint{(save.UnresolvedObjects.Count == 0 ? "" : $": {string.Join( ", ", save.UnresolvedObjects.Select( item => $"{item.InfoId} at ({item.X}, {item.Y}) {item.Width}x{item.Height} turned {item.Rotation}" ) )}")}) and {fixedItems} fixed items without charge; {objects + fixedItems - unmatched - notImported.Count} took their attraction's price, cost of goods, upgrade level, state of repair, life gauge, amount of the special ingredient, quality of goods, chance of winning, state, total profit and maintenance call." );
		if ( unmatched != 0 || notImported.Count != 0 )
			evidence.Add( $"{unmatched} objects have no matching attraction thing; outside OpenTPW's ranges and kept at their defaults: {string.Join( ", ", notImported )}." );
		foreach ( var line in adjusted )
			evidence.Add( $"Attraction value adjusted on import: {line}." );
		// [BIN:TPW-EXE:0x004DC12B walk-away load] the loader reads mNumWalkAways (+0x1A4) back with the other fields, so it is imported (ImportObjects); 0x004E1670 raises it (from 0x005006B0) and nothing else reads the field
		evidence.Add( $"Attraction walk-away counts {string.Join( "/", save.Attractions.Select( item => item.WalkAwayCount ) )} imported (mNumWalkAways, +0x1A4)." );
		// [BIN:TPW-EXE:0x4DB7D0 third gauge] the float at attraction +0x4C is written by the constructor (0x4DB10B), the loader (0x4DCE42) and one more store (0x4DB363) and read only by the serializer (0x4DC64F); no other load of a +0x4C field exists in the attraction code (0x4D8000..0x4E4000), the guest code or the interface, so the third gauge needs no import
		// [BIN:TPW-EXE:0x004E0E60 SetState] the open word (mCanLoad, +0x68) is the park's open state of the attraction: an idle attraction saved with it clear is closed again by the level (Level.ImportOriginalRideSettings), the others are opened by their placement; the gate's two sign lines are the park's name (Level.SavedGateName) and every other type's lines are its OBJECT_NAMES row
		// [BIN:TPW-EXE:0x004DB7D0 attraction serializer] the mesh instance, script and track handles, flags, footprint, entrance and exit cells, upgrade parent and balloon sprite are rebuilt by the placement, and the queue cells and ends from the placement; head, links and called guest with the guests (SAVE-002)
		foreach ( var item in save.Attractions )
			evidence.Add( $"Attraction thing {item.Id} (Info.Id {item.InfoId}): open word {item.CanLoad}, name \"{item.NameLine1}\"/\"{item.NameLine2}\", queue size {item.QueueSizeInCells} cells, first {item.FirstInQueue}, back-of-queue cell {item.BackOfQueueCell}, called {item.PersonBeingLoaded}, mesh instance {item.MeshInstanceId}, flags 0x{item.Flags:X4}; the open word and the gate's name are imported by the level, the queue cells and ends come from the placement; head, links and called guest with the guests (SAVE-002)." );
		return (objects, fixedItems, things);
	}

	/// <summary>
	/// The six daily rings of an attraction record (the serializer order costs, takings, customers, walk-aways, winners, satisfaction change); a ring that
	/// does not fit the original's 30 slots is left as it was.
	/// </summary>
	// [BIN:TPW-EXE:0x004DB7D0 attraction serializer] the six rings follow in the order +0xF8 costs, +0x70 takings, +0x1A8 customers (after the word +0x1A0), +0x230 walk-aways (after +0x1A4), +0x2B8 winners and +0x340 the visits' averaged happiness change; each is its head, its size, the full flag, its running value and the 30 slots
	private static void ImportRings( ParkObjectState state, SaveAttraction attraction )
	{
		if ( attraction.Histories.Count != SaveAttraction.HistoryBuffers || attraction.Histories.Any( ring => ring.Entries.Count != DailyRing.Size ) )
			return;
		DailyRing Ring( int index )
		{
			var saved = attraction.Histories[index];
			return DailyRing.FromSlots( saved.CurrentEntry, saved.WrappedAround, saved.Current, saved.Entries );
		}
		try
		{
			var (costs, takings, customers, walkAways, winners, satisfaction) = (Ring( 0 ), Ring( 1 ), Ring( 2 ), Ring( 3 ), Ring( 4 ), Ring( 5 ));
			state.CostsRing = costs;
			state.TakingsRing = takings;
			state.CustomersRing = customers;
			state.WinnersRing = winners;
			state.SatisfactionRing = satisfaction;
			state.WalkAwayDays.Clear();
			state.WalkAwayDays.AddRange( walkAways.Entries.Select( entry => (long)Math.Max( 0, entry ) ) );
			state.WalkAwaysToday = Math.Max( 0, walkAways.Today );
		}
		catch ( InvalidDataException )
		{
			// a head outside the ring leaves the object's rings new
		}
	}

	private static int ImportStaff( ParkEconomy economy, OriginalParkImport save, IReadOnlyDictionary<int, ParkObjectState> things, List<string> evidence )
	{
		var start = save.World.Calendar.FunnyTimeStart;
		var imported = 0;
		var adjusted = new List<string>();
		var jobs = new List<string>();
		var resumedClaims = new HashSet<int>();
		var membersByThing = new Dictionary<int, StaffMember>();
		// a guard chases a guest the guest import restores in the park (states 6–10, GuestSimulation.ImportsOriginalState)
		var chaseable = save.Guests.Where( guest => guest.State is >= 6 and <= 10 ).Select( guest => guest.Id ).ToHashSet();
		// a guard escorting a guest (state 18) names a saved guest (+0x212); the guest import decides which of them are restored (IdleEscortsWithoutARestoredGuest)
		var savedGuests = save.Guests.Select( guest => guest.Id ).ToHashSet();
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
			// [BIN:TPW-EXE:0x00504DE0 staff serializer] grade (+0x1E4), percentage through the grade (+0x1E8), happiness (+0x1F8) and energy (+0x1FC) written as their truncated low byte, the hire stamp (+0x1F0)
			// [BIN:TPW-EXE:0x004F8690 game clock] the constructor's hire stamp (0x00504BC4) is the clock FunnyTimeStart + rate × mGameTick / 4 seconds, a whole park turn, which the employee window counts days from (0x00505B70)
			var hired = 0L;
			var seconds = (staff.HiredTimestamp - start) / TimeStampUnitsPerSecond;
			if ( staff.HiredTimestamp >= start && seconds / ParkCalendar.SecondsPerTurn <= ParkCalendar.Turn( economy.Tick ) )
			{
				hired = ParkCalendar.TickOfTurn( seconds / ParkCalendar.SecondsPerTurn );
				// the original's clock only stamps whole turns, so a stamp inside a turn comes from a modified save; it is taken at that turn's start
				var rest = staff.HiredTimestamp - start - seconds / ParkCalendar.SecondsPerTurn * ParkCalendar.SecondsPerTurn * TimeStampUnitsPerSecond;
				if ( rest != 0 )
					adjusted.Add( $"{saved.Model} thing {saved.Id} hired time stamp {staff.HiredTimestamp} lies {rest / (double)TimeStampUnitsPerSecond} park seconds into turn {seconds / ParkCalendar.SecondsPerTurn}, imported as hired at that turn's start" );
			}
			else
				adjusted.Add( $"{saved.Model} thing {saved.Id} hired time stamp {staff.HiredTimestamp} lies outside the park's calendar (start {start}, turn {ParkCalendar.Turn( economy.Tick )}), imported as hired at tick 0" );
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
			var name = staff.Name.Length <= StaffMember.MaximumNameLength ? staff.Name : null;
			// [BIN:TPW-EXE:0x004F8B10 person sprite variant] the person serializer saves mSpriteID (+0x20) and mESPSprite (+0x24, the sprite category) and, once both are read, reduces the id modulo the category's loaded bank count (0x00541F60: the variant the placement drew, 0x00541F70); the staff serializer 0x00504DE0 calls it, so a member of an original save keeps his bank. The loaded count is the role's banks here (GUEST-020)
			var banks = StaffSpriteRules.Banks( type, economy.Settings.Theme )?.Banks.Count ?? 0;
			var variant = banks > 0 ? (int)(saved.Person.SpriteId % (uint)banks) : -1;
			var member = economy.Staff.AddImported( type, staff.Grade, points, happiness, StaffState.Idle, hired, energy, name, thingId: saved.Id, variant: variant );
			membersByThing.Add( saved.Id, member );
			// [DATA:SaveThingBase] exact base position is in 1/256-cell units, including the sub-cell offset.
			member.CellX = saved.Base.X / 256f;
			member.CellY = saved.Base.Y / 256f;
			// [BIN:TPW-EXE:0x005054D0 staff state setter] +0x200 is the turn an idle spell started (0 after any state but a patrol), which the idle states compare with IdleDuration[grade]
			member.IdleStamp = staff.TimeStartedIdling;
			ImportPatrolArea( saved, member, adjusted );
			ImportJob( economy, saved, member, things, save.Attractions, chaseable, savedGuests, jobs, resumedClaims, save.Litter );
			// [BIN:TPW-EXE:0x004F8B10 person serializer] the loader routes the person again to mAccurateDestX/Y (+0x18/+0x1A, 1/256 cell) once it is read (0x004FA5F0 at 0x004F9407);
			// the walk speeds are the person's mBaseSpeed (+0xC0), mPurposeSpeed (+0xC2, the bonus a walk to a job or a chase takes) and mPreviousSpeed (+0xC8)
			var person = saved.Person;
			if ( person.AccurateDestX < ParkLitter.MapSize * 256 && person.AccurateDestY < ParkLitter.MapSize * 256 )
				member.ResumeDestination = (person.AccurateDestX, person.AccurateDestY);
			else
				adjusted.Add( $"{saved.Model} thing {saved.Id} walk destination ({person.AccurateDestX}, {person.AccurateDestY}) lies outside the map, not walked to" );
			// [BIN:TPW-EXE:0x004D6977 escort walk mask] the walk mask +0x188 is saved in the navigation state (nav_mode) and the loader's route uses it: a guard escorting by the second try (+0x188 = 1) walks off the paths to the booth
			if ( person.NavigationState.NavMode == 1 && member.ResumeDestination != null )
				member.ResumeMask = 1;
			if ( GuestSpeed.BaseSpeeds.Contains( person.BaseSpeed ) )
				member.BaseSpeed = person.BaseSpeed;
			member.SpeedBonus = person.PurposeSpeed <= byte.MaxValue ? person.PurposeSpeed : 0;
			var smoothed = BitConverter.Int32BitsToSingle( unchecked( (int)person.PreviousSpeed ) );
			member.SmoothedSpeed = float.IsFinite( smoothed ) && smoothed >= 0 ? smoothed : 0;
			imported++;
		}
		ImportStaffRoleOrder( economy.Staff, save, membersByThing, evidence );
		evidence.Add( $"Imported {imported} staff ({string.Join( ", ", save.Staff.Select( item => $"{item.Model} grade {item.Staff.Grade}" ) )})." );
		foreach ( var adjustment in adjusted )
			evidence.Add( $"Staff value adjusted on import: {adjustment}." );
		foreach ( var job in jobs )
			evidence.Add( job );
		// [BIN:TPW-EXE:0x004E01F0 claim] the claim is two words of the attraction, the claimant's thing id (+0x5E) and the turn it was made (+0x60), saved and loaded as they are (the attraction serializer reads them back with no check against any job)
		// [BIN:TPW-EXE:0x004E0220 claim lapse] the query lets a claim lapse once the turn is above its stamp + 100 (unsigned) and the claimant thing's 0x00506580 is false: a mechanic whose ride +0x218 is the object, a handyman whose toilet +0x21A is the object, any other thing "Invalid call" and false, a dead one as well; ParkEconomy.Claimant is that query, so each claim not taken again by a resumed job is imported as it was saved
		foreach ( var held in save.Attractions.Where( item => item.AssignedStaffMember != 0 && !resumedClaims.Contains( item.Id ) ) )
		{
			if ( !things.TryGetValue( held.Id, out var attraction ) )
			{
				evidence.Add( $"Attraction thing {held.Id} (Info.Id {held.InfoId}) claim by thing {held.AssignedStaffMember} on turn {held.TimeMarkedForMaintenance} has no imported attraction to stay on." );
				continue;
			}
			// the claimant is an imported member where the save has one; a thing the import does not hold (a guest, a destroyed thing's slot) is a claimant that is allocated to nothing, which a negative word stands for: it keeps the attraction claimed until the query lapses it
			var claimant = membersByThing.GetValueOrDefault( held.AssignedStaffMember );
			// a stamp ahead of the save's own clock cannot come from the game (the turn only grows); the claim is taken as made now
			var stamp = Math.Min( held.TimeMarkedForMaintenance, economy.Turn );
			if ( stamp != held.TimeMarkedForMaintenance )
				evidence.Add( $"Staff value adjusted on import: attraction thing {held.Id} claim stamp {held.TimeMarkedForMaintenance} lies after the save's turn {economy.Turn}, imported as made on that turn." );
			attraction.MechanicId = claimant?.Id ?? -(int)held.AssignedStaffMember;
			attraction.ClaimTurn = stamp;
			evidence.Add( $"Attraction thing {held.Id} claim by thing {held.AssignedStaffMember} on turn {held.TimeMarkedForMaintenance} imported as saved{(claimant == null ? " (the claimant is no imported staff member)" : "")}, lapsing at the first query after turn {stamp + ParkEconomy.ClaimLapseTurns}." );
		}
		// [BIN:TPW-EXE:0x00504BDC staff constructor] mJobsDone (+0x204) is set to 0 here and otherwise touched only by the serializer (0x00504EFC, 0x005052ED): nothing reads it, so it needs no import
		if ( imported != 0 )
			evidence.Add( $"Staff jobs-done counts {string.Join( "/", save.Staff.Select( item => item.Staff.JobsDone ) )} need no import (nothing reads mJobsDone)." );
		if ( save.StaffHq is { } hq )
		{
			// [BIN:TPW-EXE:0x50c800 training split] budget word k serves the role of thing model 5, 4, 6, 7 and 8 for k = 0..4 (handyman, mechanic, entertainer, guard, researcher: StaffType's order), shared by its role's headcount at month end
			for ( var role = 0; role < BalanceSettings.StaffTypeCount && role < hq.TrainingBudgets.Count; role++ )
				economy.Staff.SetTrainingBudget( (StaffType)role, hq.TrainingBudgets[role] );
			// [BIN:TPW-EXE:0x0059FB10 train staff general] the byte mHaveEverTrained (+0x20 of the HQ's budget record) is read by the advisor's train-your-staff line; the save's value replaces what the budgets above implied
			economy.Staff.RestoreHaveEverTrained( hq.Trained != 0 );
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
			// [EXT:staff-pickup-reconcile] a member saved in state 7 (picked up) with the staff HQ's picked-up id (+0x60) naming him is imported idle (StaffPlacement.Reconcile) instead of reproducing the original's limbo (its only reader of the id is the drop 0x509AF0 from the carry tool's exit 0x46CC40, which also clears it, and no carry tool exists after a load, so the original keeps him in state 7 for good, the pick-up guard 0x505BD0 refusing state 7, loader 0x508BB0): a member nobody can ever move again would be a defect, user-approved 2026-10-17
			// [BIN:TPW-EXE:0x5089C0 staff HQ constructor] the force strike (+0x64) is written only by the HQ constructor (0) and the save loader 0x508BB0, so the month gate 0x508E70 and the grievance 0x509360 see 0 in play as OpenTPW does; the trained byte (mHaveEverTrained, training +0x14) is cleared (0x50C620), serialized (0x50C640), set by the training window (0x4B25D0) and read only by the advisor's train-your-staff line (0x59FB3B), so it is imported above
			evidence.Add( $"Staff HQ values read, nothing to import: force strike {hq.ForceStrike}, trained {hq.Trained} (the trained byte is the advisor's train-your-staff flag, imported above; nothing reads the force strike after the load)." );
			if ( hq.PickedUp != 0 )
				evidence.Add( $"[EXT:staff-pickup-reconcile] Staff HQ thing {hq.Id} saved member {hq.PickedUp} as picked up: he is imported idle, where the original keeps him in state 7 for good (no carry tool exists after a load to drop him)." );
		}
		else
			evidence.Add( "The save has no staff HQ thing; OpenTPW's training budgets start at 0." );
		if ( save.World.StaffPool is { } pool )
		{
			// [BIN:TPW-EXE:0x5084f0 staff pool update] a valid slot is a candidate of role +0 (StaffType's order, 0x507600) that leaves once the turns since its stamp
			// (+0xc) exceed 4 × its timeout (+0x10); the pool refills once the turns since its own stamp exceed 4 × TimeBetweenStaffUpdates
			var saved = pool.Candidates.Where( candidate => candidate.Valid )
				.Select( candidate => ((StaffType)candidate.Type, (int)(candidate.Name % ParkStaff.NameTableSize), Math.Min( (int)candidate.Grade, BalanceSettings.GradeCount - 1 ),
					(long)candidate.TimeSig, (long)candidate.Timeout, (int)candidate.SubType) )
				.ToList();
			economy.Staff.RestoreOriginalPool( saved, pool.TimeSig, pool.Candidates.Where( candidate => candidate.Valid ).Select( candidate => candidate.OnPointer ).ToList() );
			evidence.Add( $"Staff pool imported: {saved.Count} candidates ({string.Join( ", ", saved.Select( item => $"{item.Item1} grade {item.Item3} made on turn {item.Item4} for {item.Item5}" ) )}), last refill on turn {pool.TimeSig}." );
			// [BIN:TPW-EXE:0x507600 staff candidate] a candidate's sub-type (+9) is its sprite variant, drawn at its creation (0x507600 → 0x541F70: (R >> 2) % the category's loaded bank count, the table 0x763F88 of 12-byte entries) and handed to the placement object 0x46C6D0 when it is picked up (0x507BF0): imported as the candidate's variant, which the member hired from it keeps (StaffSpriteRules.KeyOf)
			// [BIN:TPW-EXE:0x508000 staff census] the people-in-category counts and stop-producing flags are recomputed from the park's staff before every read (the refill 0x5084F0, its top-up 0x508170 and the pick-up 0x507BF0), as UpdatePool counts the hired staff; the opening-pool flag only gates the opening pool 0x5087D0, which OpenTPW draws for a new park only
			evidence.Add( $"Staff pool sub-types imported as the candidates' sprite variants: {string.Join( "/", economy.Staff.Candidates.Select( candidate => candidate.Variant ) )} (-1: a sub-type past the role's banks); the counts in category {string.Join( "/", pool.PeopleInCategory )}, stop producing {string.Join( "/", pool.StopProducing.Select( flag => flag ? 1 : 0 ) )} and the opening-pool flag {pool.OpeningPoolGenerated} are recomputed from the park's staff." );
			// [BIN:TPW-EXE:0x5083B0 tool exit] a candidate saved with its on-pointer byte set (+0xB: picked up for placement) keeps the flag, as the original does until a carry tool ends: the refill 0x5084F0 never expires a flagged candidate, the census counts it as valid, and the next tool exit clears the flag (0x5083B0: with the placement made it also removes the flagged candidate); the carry tool itself is not saved or recreated (ParkStaff.StaleOnPointerId)
			var flagged = pool.Candidates.Count( candidate => candidate.Valid && candidate.OnPointer );
			if ( flagged > 0 )
				evidence.Add( $"{flagged} saved candidates were on the pointer (picked up for placement); the first stays in the pool without expiring until the next placement tool ends." );
		}
		else
			evidence.Add( $"The save's hiring pool does not read before the calendar ({save.World.OpaqueMacroAiLength} macro-AI bytes); {economy.Staff.Candidates.Count} candidates were drawn." );
		return imported;
	}

	/// <summary>
	/// An attraction's state word, total profit and maintenance call. State 1 (broken down) and 4 (condemned: its life gauge
	/// truncated to 0) break the object down; 2 (an upgrade requested, 0x004E0050) waits for a mechanic to install the next level, whose cost is
	/// debited then; 0 is a working choosable object and 3 one that is not choosable (the constructor's SetState at 0x004DB4FB). The total profit is takings − costs (0x004E1C60). A state that does not fit the type is
	/// reported and the object kept working.
	/// </summary>
	// [BIN:TPW-EXE:0x004E0E60 attraction SetState] states 0 and 3 only store the word; 1, 2 and 4 close the object (open word +0x68 = 0, called guest cleared, script variable 6 = 1, the queue dismissed by 0x004E0450) first; any other value logs "Unknown state in CObject::SetState"
	// [BIN:TPW-EXE:0x004E16B0 takings] a payment adds to mTotalTakings (+0x180) and today's takings (+0x70); [BIN:TPW-EXE:0x004E1920 costs] a cost to mTotalCosts (+0x184) and today's costs (+0xF8)
	// [BIN:TPW-EXE:0x004E2290 scrap value] the scrap rule reads the stamp at +0x18 (the loader's date-time, 0x004DC89E) against the clock and mNumCustomers (+0x1A0, raised by 0x004E1690 at every visit's end 0x004FD970)
	private static void ImportAttractionState( ParkEconomy economy, SaveAttraction attraction, ParkObjectState state, List<string> adjusted )
	{
		var what = $"attraction thing {attraction.Id} (Info.Id {attraction.InfoId})";
		economy.Catalog.TryGet( attraction.InfoId, out var info );
		state.TotalProfit = (long)attraction.TotalTakings - attraction.TotalCosts;
		state.MaintenanceRequested = attraction.RequestedService != 0;
		var choosable = info?.IsChoosable ?? false;
		switch ( attraction.State )
		{
			case 0 when choosable:
			case 3 when !choosable:
				break;
			case 1 or 4 when choosable:
				// the saved state word is the state word: 1 or 4 as saved (the ride update 0x004E14E0 sets 4 for a life gauge byte of 0, so a save that disagrees with its gauge is reported)
				state.StateWord.Restore( attraction.State );
				if ( (attraction.State == 4) != (RideBreakdownRules.GaugeByte( attraction.LifeGauge ) == 0) )
					adjusted.Add( $"{what} state {attraction.State} with life gauge {attraction.LifeGauge} disagrees with the state the ride update would enter ({RideStateWord.BrokenStateFor( attraction.LifeGauge )}); the saved state is kept" );
				break;
			case 2 when choosable && info!.Upgrades.Count > state.Level + 1:
				state.PendingLevel = state.Level + 1;
				state.StoredState = RideObjectStates.Upgrading;
				state.UpgradeCostDue = true;
				break;
			default:
				adjusted.Add( $"{what} state {attraction.State} does not fit a {(choosable ? "choosable" : "not choosable")} type{(attraction.State == 2 ? " with a next level" : "")}, imported working" );
				break;
		}
	}

	/// <summary>
	/// The patrol rectangle: the corner cell ids <c>mPatrolRegionBL</c>/<c>mPatrolRegionTR</c>, which the patrol tool stores with the smaller x and y first
	/// and the clear sets to 1 and 0x4000 (the whole map). Corners that are no cell or out of order are imported as the whole map and reported.
	/// </summary>
	// [BIN:TPW-EXE:0x00506ED0 patrol area test] a cell id is y × 128 + x + 1: the test compares (id − 1) &amp; 0x7F and (id − 1) >> 7 with both corners'
	private static void ImportPatrolArea( SaveStaff saved, StaffMember member, List<string> adjusted )
	{
		var staff = saved.Staff;
		if ( SaveStaffRecord.CellOf( staff.PatrolRegionBottomLeft ) is { } low && SaveStaffRecord.CellOf( staff.PatrolRegionTopRight ) is { } high
			&& low.X <= high.X && low.Y <= high.Y && high.X < ParkLitter.MapSize && high.Y < ParkLitter.MapSize )
			member.PatrolArea = (low.X, low.Y, high.X, high.Y);
		else
			adjusted.Add( $"{saved.Model} thing {saved.Id} patrol corners {staff.PatrolRegionBottomLeft}/{staff.PatrolRegionTopRight} are no ordered pair of map cells, imported as the whole map" );
	}

	/// <summary>
	/// After the guests are imported: a guard imported escorting a guest the guest import did not restore at all (dropped) is idle, as the import gives every guard whose guest is missing
	/// (SAVE-002). A guest restored in any state keeps its escorts: the set-down (0x004FEB50) does not look at the state. Returns the evidence lines.
	/// </summary>
	internal static IReadOnlyList<string> IdleEscortsWithoutARestoredGuest( ParkEconomy economy, Func<int, bool> isImported )
	{
		var lines = new List<string>();
		foreach ( var member in economy.Staff.Members.Where( member => member.Type == StaffType.Guard && member.Job == StaffJob.Escorting && !isImported( member.TargetGuestId ) ) )
		{
			lines.Add( $"[APPROX:SAVE-002] guard {member.Id} escorting guest {member.TargetGuestId} imported idle: the guest import did not restore that guest." );
			member.TargetGuestId = 0;
			member.Job = StaffJob.Idle;
			member.State = ParkStaff.DisplayState( (uint)StaffJob.Idle );
		}
		return lines;
	}

	/// <summary>
	/// The member's own state word (+0x19C) and what it works on: a mechanic's ride (<c>mObjectToRepair</c>) and repair timer (<c>mDurationOfRepair</c>), a
	/// handyman's toilet (<c>mToiletToClean</c>) and cleaning start, an entertainer's or researcher's work start, a guard's chase timer, and the rest area
	/// (<c>mRestArea</c>) of a member walking to or resting in one. Thing ids name the reloaded attraction things. A word that is no state of the role, or
	/// whose target is not imported, is imported as idle (state 0, which searches on its first update) and reported.
	/// </summary>
	// [BIN:TPW-EXE:0x004DA110 mechanic serializer] mDurationOfRepair (+0x214) and mObjectToRepair (+0x218) after the staff suffix; the work state 13 counts +0x214 down (0x004DA830)
	// [BIN:TPW-EXE:0x004D6D60 handyman serializer] mTargetLitterCell (+0x218), mTimeStartedCleaning (+0x214), mToiletToClean (+0x21A); the clean state 11 ends once the turn passes +0x214 + WorkDuration (0x004D73C0)
	// [BIN:TPW-EXE:0x004D4460 entertainer serializer] mTimeStartedEntertaining (+0x214); [BIN:TPW-EXE:0x00502760 researcher serializer] mTimeStartedResearching (+0x214)
	// [BIN:TPW-EXE:0x004D6000 guard serializer] mPerp (+0x212), mProsecutionTimestamp (+0x214, the chase timer the state setter 0x004D65D0 sets)
	// [BIN:TPW-EXE:0x00506D10 rest end] mRestArea (+0x208) names the rest area only while the member walks to it (state 2) or rests in it (state 3): the rest's end clears it
	private static void ImportJob( ParkEconomy economy, SaveStaff saved, StaffMember member, IReadOnlyDictionary<int, ParkObjectState> things, IReadOnlyList<SaveAttraction> attractions, IReadOnlySet<int> chaseable, ISet<int> savedGuests, List<string> evidence, ISet<int> resumedClaims, IReadOnlyList<SaveLitter> litter )
	{
		var word = saved.Staff.State;
		var what = $"{saved.Model} thing {saved.Id}";
		if ( word > byte.MaxValue || !ParkSaveFile.StaffWorldData.Fits( member.Type, (StaffJob)word ) )
		{
			evidence.Add( $"Staff value adjusted on import: {what} state word {word} is no state of a {member.Type}, imported idle." );
			word = 0;
		}
		var job = (StaffJob)word;
		ParkObjectState? Thing( ushort id ) => id != 0 && things.TryGetValue( id, out var item ) ? item : null;
		// [BIN:TPW-EXE:0x004E01F0 claim] an attraction names its claimant (+0x5E) and the turn the claim was made (+0x60); the world that places the member takes the claim again with that turn when the target names him
		// (a turn after the save's own clock is taken as now), so it lapses 100 turns after it was made unless he still targets the object (0x004E0220)
		void NoteClaim( ushort thingId )
		{
			if ( attractions.FirstOrDefault( entry => entry.Id == thingId ) is { } held && held.AssignedStaffMember == saved.Id )
			{
				member.ResumeClaimTurn = Math.Min( held.TimeMarkedForMaintenance, ParkCalendar.Turn( economy.Tick ) );
				resumedClaims.Add( held.Id );
			}
		}
		string? dropped = null;
		switch ( job )
		{
			case StaffJob.ToRide or StaffJob.Repairing:
				if ( Thing( saved.RoleTarget ) is { } ride )
				{
					member.RideId = ride.Id;
					NoteClaim( saved.RoleTarget );
					if ( job == StaffJob.Repairing )
						member.JobStamp = saved.RoleTimer;
				}
				else
					dropped = $"its object to repair, thing {saved.RoleTarget}, is no imported attraction";
				break;
			case StaffJob.ToToilet or StaffJob.Cleaning:
				if ( Thing( saved.RoleToilet ) is { } toilet && economy.Catalog.TryGet( toilet.InfoId, out var info ) && info.ProvidesRelief )
				{
					member.ToiletId = toilet.Id;
					NoteClaim( saved.RoleToilet );
					if ( job == StaffJob.Cleaning )
						member.JobStamp = saved.RoleTimer;
				}
				else
					dropped = $"its toilet, thing {saved.RoleToilet}, is no imported relief object";
				break;
			case StaffJob.ToLitter or StaffJob.Sweeping:
				// [BIN:TPW-EXE:0x004D6D60 handyman serializer] mTargetLitterCell (+0x218, a cell id y × 128 + x + 1) and mTimeStartedCleaning (+0x214): the target is the littered cell the handyman walks to or sweeps, which he holds the claim of (the cell's collector word and its marked turn)
				if ( SaveStaffRecord.CellOf( saved.RoleTarget ) is { } target && economy.Litter.TypeAt( target.X, target.Y ) != LitterType.None )
				{
					member.LitterCell = target;
					member.JobStamp = saved.RoleTimer;
					var held = litter.FirstOrDefault( entry => entry.X == target.X && entry.Y == target.Y );
					economy.Litter.Claim( target.X, target.Y, member.Id, held.Collector == saved.Id ? Math.Min( held.MarkedTurn, ParkCalendar.Turn( economy.Tick ) ) : ParkCalendar.Turn( economy.Tick ) );
				}
				else
					dropped = $"its litter cell {saved.RoleTarget} holds no imported litter";
				break;
			case StaffJob.Chasing when chaseable.Contains( saved.RoleTarget ):
				// the guest keeps its thing id as its guest id (GuestSimulation.ImportOriginal); the chase timer is +0x214
				member.TargetGuestId = saved.RoleTarget;
				member.JobStamp = saved.RoleTimer;
				break;
			case StaffJob.Escorting when savedGuests.Contains( saved.RoleTarget ):
				// [BIN:TPW-EXE:0x004D6A50 escort arrival] state 18 carries its perpetrator (+0x212, mPerp) to the ticket booth and its arrival sets that thing down (0x004FEB50) without testing its state (0x004D6A72..0x004D6AC6), so every guard naming a saved guest escorts, duplicates included; the walk goes on to the saved destination (0x004F9407) and the arrival sets the guest down (0x004D6A50)
				member.TargetGuestId = saved.RoleTarget;
				break;
			case StaffJob.Chasing or StaffJob.Escorting:
				// [APPROX:SAVE-002] [BIN:TPW-EXE:0x004D6350 escort state] a guard chasing a guest the import does not restore (a rider, state 16), or escorting one that the save does not hold (state 17), is imported idle
				evidence.Add( $"[APPROX:SAVE-002] {what} guard job (state {word}, perpetrator thing {saved.RoleTarget}) imported idle: the guest is not imported in the park, or the save holds no such guest." );
				job = StaffJob.Idle;
				break;
			case StaffJob.Performing or StaffJob.Researching:
				member.JobStamp = saved.RoleTimer;
				break;
			case StaffJob.ToRestArea or StaffJob.Resting:
				if ( Thing( saved.Staff.RestAreaId ) is { } rest && economy.IsRestArea( rest.Id ) )
					member.RestAreaId = rest.Id;
				else
					dropped = $"its rest area, thing {saved.Staff.RestAreaId}, is no imported rest area";
				break;
		}
		if ( dropped != null )
		{
			evidence.Add( $"Staff value adjusted on import: {what} state {word} imported idle, {dropped}." );
			job = StaffJob.Idle;
		}
		member.Job = job;
		member.State = ParkStaff.DisplayState( (uint)job );
		if ( dropped == null && job != StaffJob.Idle )
			evidence.Add( $"{what} job imported: state {word} ({job}){(member.RideId != 0 ? $", ride object {member.RideId}" : "")}{(member.ToiletId != 0 ? $", toilet object {member.ToiletId}" : "")}{(member.RestAreaId != 0 ? $", rest area object {member.RestAreaId}" : "")}{(member.TargetGuestId != 0 ? $", guest {member.TargetGuestId}" : "")}{(member.JobStamp != 0 ? $", timer {member.JobStamp}" : "")}." );
	}

	/// <summary>
	/// The litter of the cell grid: every cell whose <c>mLitter</c> word is a litter type (1 to 8) holds that litter, unclaimed until a handyman's saved job claims it again. A word that is no litter
	/// type, or a cell outside the litter map, is reported and skipped.
	/// </summary>
	// [BIN:TPW-EXE:0x004D8DD0 map cell serializer] mLitter (+0x2C), mLitterCollector (+0x30), mLitterScript (+0x28, +0x38), mTimeMarkedForLitterCollection (+0x34) are saved with the cell; the litter's sprite is the cell's sprite script (0x4D93B0: the cell's corner plus a draw of the cell's own reseed), which OpenTPW draws from the cell alone (ParkLitter.SpriteOffset)
	private static void ImportLitter( ParkEconomy economy, OriginalParkImport save, List<string> evidence )
	{
		var restored = new List<LitterCell>();
		var skipped = new List<string>();
		foreach ( var item in save.Litter )
		{
			if ( item.Type is >= 1 and <= (uint)LitterType.Stinkbomb && economy.Litter.InBounds( item.X, item.Y ) )
				restored.Add( new LitterCell( item.X, item.Y, (LitterType)item.Type, 0, 0 ) );
			else
				skipped.Add( $"({item.X}, {item.Y}) type {item.Type}" );
		}
		if ( restored.Count > 0 || skipped.Count > 0 )
			economy.Litter.Restore( restored );
		evidence.Add( $"Litter imported: {restored.Count} littered cells{(restored.Count == 0 ? "" : $" ({string.Join( ", ", restored.GroupBy( cell => cell.Type ).OrderBy( group => group.Key ).Select( group => $"{group.Count()} of type {(int)group.Key}" ) )})")}{(skipped.Count == 0 ? "" : $"; skipped {string.Join( ", ", skipped )}")}." );
	}

	/// <summary>
	/// The challenge system: the slots' won and declined counts, whether the system has started offering, a running or offered challenge and the
	/// time of the next event. Slot k is the park's k-th challenge: the level's own (<c>ChallengesInThisLevel</c>) or, where the slot differs, the saved definition (<see cref="Verify"/> adopts it, SAVE-005).
	/// </summary>
	// [BIN:TPW-EXE:0x4d2320 challenge system serializer] after the 20 slots: mCurrentChallenge (+0x3cc, −1 none), mNextChallengeEventTime (+0x3d0, an absolute 100 ns stamp), mSystemSwitchedOn (+0x3d8), mChallengeOn (+0x3d9, accepted and running), mWaitingForReply (+0x3da, an offer is open), mValue (+0x3dc), mActualThing (+0x3e0)
	// [BIN:TPW-EXE:0x4d1e90 daily challenges] before the switch is on, the first offer comes once the park's elapsed day (0x4f8800 / 864e9) reaches DaysUntilFirstChallenge while the park is open (0x4c9050), and the switch is set; then an idle system offers when the clock passes the next event time, and a running challenge ends at its deadline, which the accept (0x4d2df0) stores in the same word
	private static void ImportChallenges( ParkEconomy economy, SaveChallengeSystem system, long calendarStart, IReadOnlyDictionary<int, ParkObjectState> things, List<string> evidence )
	{
		var settings = economy.Settings;
		var level = settings.ChallengesInThisLevel;
		var slots = system.Challenges;
		// the slots are the park's challenges (Verify adopted them where they differ from the level's own): slot k is level[k]
		// a stamp before the calendar start (or a wild one) is cut to the range a park save accepts
		long Seconds( long stamp ) => Math.Clamp( (stamp - calendarStart) / OriginalEconomyImport.TimeStampUnitsPerSecond, 0, ParkSaveFile.MaximumParkSeconds );
		long Day( long stamp ) => Seconds( stamp ) / ParkCalendar.SecondsPerDay;
		var finished = slots.Select( ( slot, index ) => (slot, index) ).Where( entry => entry.slot.Won ).Select( entry => level[entry.index] ).ToList();
		var declines = slots.Select( ( slot, index ) => (slot, index) ).Where( entry => entry.slot.Declined > 0 ).ToDictionary( entry => level[entry.index], entry => entry.slot.Declined );
		var current = system.CurrentChallenge >= 0 && system.CurrentChallenge < slots.Count ? system.CurrentChallenge : -1;
		var nextPosition = current >= 0 ? (current + 1) % slots.Count : 0;
		long nextOffer = settings.ChallengeTiming.DaysUntilFirstChallenge;
		int? running = null, offered = null;
		long deadline = 0;
		long? nextSeconds = null, deadlineSeconds = null;
		string state;
		if ( system.Switch1 == 0 )
			state = $"not started: the first offer is due on park day {nextOffer}";
		else if ( system.Switch2 != 0 && current >= 0 )
		{
			running = level[current];
			// [BIN:TPW-EXE:0x4d1e90 challenge update] the running challenge ends at the stamp itself (the accept stored the clock plus mTargetTime there), tested on every park turn
			deadlineSeconds = nextSeconds = Seconds( system.NextEventTime );
			deadline = nextOffer = deadlineSeconds.Value / ParkCalendar.SecondsPerDay;
			state = $"challenge {running} running until day {deadline} with value {system.Value}";
		}
		else if ( system.Switch3 != 0 && current >= 0 )
		{
			offered = level[current];
			state = $"challenge {offered} offered and unanswered";
		}
		else
		{
			// an idle system offers on the first park turn at or after the stamp
			nextSeconds = Seconds( system.NextEventTime );
			nextOffer = nextSeconds.Value / ParkCalendar.SecondsPerDay;
			state = $"the next offer is due on day {nextOffer}";
		}
		// [BIN:TPW-EXE:0x4d2fb0 challenge won] the carried object (+0x3e0) and the slots' ids (+0x2e) are thing ids of the saved park; an object the import restored keeps its id as its economy instance
		int Carried( int thing ) => things.TryGetValue( thing, out var carriedState ) ? carriedState.Id : 0;
		var carriedThings = slots.Select( ( slot, index ) => (Index: level[index], Thing: Carried( slot.FollowupThing )) ).Where( entry => entry.Thing != 0 ).ToDictionary( entry => entry.Index, entry => entry.Thing );
		economy.RestoreOriginalChallenges( nextOffer, nextPosition, running, deadline, system.Value, offered, declines, finished, system.Switch1 != 0, Carried( system.ActualThing ), carriedThings, nextSeconds, deadlineSeconds );
		evidence.Add( $"Challenges imported: {state}; {finished.Count} won, {declines.Count} declined, carried object {(system.ActualThing == 0 ? "none" : Carried( system.ActualThing ) == 0 ? $"thing {system.ActualThing} (not an imported object)" : $"thing {system.ActualThing}")}." );
		// [BIN:TPW-EXE:0x4d1ae0 challenge loss] a slot's loss count (+0x24) is raised by the loss 0x4d3140 → 0x4d1ae0 and read only by the save serializer (0x4d12b7) and loader (0x4d159b); the offered word (+0x20) is zeroed by the constructor (0x4d0f76) and the clear (0x4d1bf3), never written again in play and read only by the serializer and loader; neither feeds the offer scan 0x4d2a70 (used, not won, declined below the limit, independent), the follow-up scan 0x4d2b60 or the win 0x4d2fb0, so there is nothing for an import to change
		evidence.Add( $"Challenge loss counts {string.Join( "/", slots.Select( slot => slot.Lost ) )} and offered words {string.Join( "/", slots.Select( slot => slot.Offered ) )} read, not kept: no challenge code reads them (0x4d1ae0)." );
	}

	/// <summary>
	/// The research lab: a project whose points done reach its cost is researched, a started one keeps its points, each category's effort and current
	/// project come from its group record and the lab's work load from the tail. Items OpenTPW does not list, and current projects it would not research
	/// now, are reported.
	/// </summary>
	// [BIN:TPW-EXE:0x502dc0 research lab serializer] 256 projects (0x504210), five category records (0x504760) in ResearchCategory order, then mNumProjects, mWorkLoad, mMostExpensiveProject, mLabActive; used projects are the first mNumProjects
	// [BIN:TPW-EXE:0x504630 research points] points are added to mResearchDone while it is below mCost; reaching the cost marks the object researched (its object-table record +0x10) and notifies
	private static void ImportResearch( ParkEconomy economy, SaveResearchLab lab, SaveObjectControls? controls, bool cheatMode, List<string> evidence )
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
		// [BIN:TPW-EXE:0x504580 upgrade level project] the lab's category-4 projects (the add-ons and the appended level projects) keep the saved table's order
		var upgradeOrder = Enumerable.Range( 0, used ).Select( lab.Project ).Where( project => project.Category == (uint)ResearchCategory.Upgrade && research.HasItem( project.ObjectId, project.UpgradeLevel ) )
			.Select( project => ((int)project.ObjectId, (int)project.UpgradeLevel) ).ToList();
		research.Restore( completed, progress, effort, null, upgradeOrder.Count == 0 ? null : upgradeOrder );
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
			// [BIN:TPW-EXE:0x00504760 research group serializer] the saved current project is kept as it is, offered by the picker now or not
			if ( research.SelectAsSaved( category, project.ObjectId, project.UpgradeLevel ) )
				current.Add( $"{category} {project.ObjectId}{(research.TrySelect( category, project.ObjectId, project.UpgradeLevel ) ? "" : " (not offered by the picker now)")}" );
			else
				refused.Add( $"{category} {project.ObjectId} level {project.UpgradeLevel}" );
		}
		// [BIN:TPW-EXE:0x00504760 research group serializer] the group record's last byte is the category's highest group available, read back as it is (only the opener 0x503B80 raises it, per completed project): the saved value is the lab's, whatever the completed projects would give
		var savedGroups = Enumerable.Range( 0, SaveResearchLab.GroupRecords ).Select( index => (int)lab.Group( index ).HighestGroup ).ToArray();
		var recomputedGroups = Enum.GetValues<ResearchCategory>().Select( category => research.HighestOpenGroup( category ) ).ToArray();
		if ( savedGroups.All( group => group <= ParkResearch.MaximumOpenGroup ) && savedGroups.Length == recomputedGroups.Length )
		{
			research.RestoreHighestGroups( savedGroups );
			evidence.Add( $"Research highest open groups imported as saved: {string.Join( "/", savedGroups )}{(savedGroups.SequenceEqual( recomputedGroups ) ? ", the groups the researched projects give" : $" (the researched projects give {string.Join( "/", recomputedGroups )})")}." );
		}
		else
			evidence.Add( $"Research highest open groups {string.Join( "/", savedGroups )} are outside 0 to {ParkResearch.MaximumOpenGroup}: the groups the researched projects give ({string.Join( "/", recomputedGroups )}) are kept." );
		evidence.Add( $"Research imported: {used} projects, {progress.Count} started, {completed.Count - initial} researched by the lab, effort {string.Join( "/", effort )}, work load {lab.WorkLoad}, current {(current.Count == 0 ? "none" : string.Join( ", ", current ))}." );
		// [BIN:TPW-EXE:0x4D3AA0 object controls] the control manager's table (150 records of 32 bytes, the used ones first) is the first part of the World after its variables; a record's researched byte (+0x10) is written by the lab's init 0x5031A0 (1 for an object without a research cost, so it starts researched), by the research step 0x504630 when a project reaches its cost (1, with the upgrade level in +0x14) and by the research cheat 0x503FF0 (1 for every research item's object, the project left unfinished), so the flags follow from the lab's projects and the objects' costs and are checked against OpenTPW's research here; in a save with the cheat mode on an object marked researched whose project is unfinished is the research cheat's (docs/CHEATS.md)
		var disagreements = new List<string>();
		var cheatAvailable = new List<int>();
		var checkedControls = 0;
		if ( controls != null )
		{
			foreach ( var control in controls.Controls )
			{
				if ( !research.HasItem( control.Id, 0 ) )
					continue;
				checkedControls++;
				if ( research.Completed.Contains( (control.Id, 0) ) == control.Researched )
					continue;
				// [BIN:TPW-EXE:0x503FF0 research cheat] a cheated park's object that is buildable without its finished project
				if ( cheatMode && control.Researched )
					cheatAvailable.Add( control.Id );
				else
					disagreements.Add( $"{control.Id} (saved {(control.Researched ? "researched" : "not researched")})" );
			}
			if ( cheatAvailable.Count > 0 )
				evidence.Add( $"Research cheat: {research.MakeAvailableByCheat( cheatAvailable )} objects the cheated save marks researched without a finished project are available to build ({string.Join( ", ", cheatAvailable )})." );
			evidence.Add( $"Research object controls: {controls.Controls.Count} records, {controls.Controls.Count( control => control.Researched )} researched, {checkedControls} research items checked against OpenTPW's research, {(disagreements.Count == 0 ? "none" : string.Join( ", ", disagreements ))} disagree." );
		}
		// [EXT:unknown-research-dropped] (user-approved 2026-10-30, SAVE-007 converted: a save naming objects or projects OpenTPW's data lacks, from a mod or another build, is a situation of OpenTPW's data paths, not an original behaviour) projects of objects the level archives do not define, and current project identities absent from their category's reconstructed table, are dropped and reported; the 11 corpus labs contain neither case. Known current projects are kept as saved even when the picker would not offer them (SelectAsSaved).
		var dropped = new List<string>();
		if ( unknown.Count > 0 )
			dropped.Add( $"projects of objects the level archives do not define ({string.Join( ", ", unknown )})" );
		if ( refused.Count > 0 )
			dropped.Add( $"current projects the lab's table does not hold ({string.Join( ", ", refused )})" );
		if ( disagreements.Count > 0 )
			dropped.Add( $"object-control researched flags that disagree with OpenTPW's research ({string.Join( ", ", disagreements )})" );
		if ( controls == null )
			dropped.Add( "the object controls' researched flags (the save's control table does not read)" );
		if ( dropped.Count > 0 )
			evidence.Add( $"Research not imported: {string.Join( "; ", dropped )}." );
	}

	private static void ReportNotImported( OriginalParkImport save, List<string> evidence, bool lostImported )
	{
		// the guests are restored by the level's guest simulation (GuestSimulation.ImportOriginal), which reports its outcome through Report
		var byState = string.Join( ", ", save.Guests.GroupBy( guest => guest.State ).OrderBy( group => group.Key ).Select( group => $"{group.Count()} in state {group.Key}" ) );
		evidence.Add( $"{save.GuestCount} saved guests{(byState.Length == 0 ? "" : $" ({byState})")} are imported by the guest simulation when a level attaches it." );
		// [EXT:original-engine-state-dumps] the raw dumps of the original's own engine structures that OpenTPW's engines have no counterpart for (the Scripts SCPT records: the sprite scripts of the saved guests and litter, which OpenTPW animates from their state and the cell; the RSSE scripts' object lists, node, bounce and walk tables, whose saved ids, list order, pass counter and run state are imported, RideScriptWorld.ImportSaved and RideVM.ImportSavedRunState; the Sound block's engine state: the sound flags, voice handles, crowd, rain and reverb words and the sound library's voice list (not QMixer data; the saved volumes and on-flags are applied, GameAudio.ApplySavedSound); the message-centre lists and the Track and Flying ride chunks) are not restored: OpenTPW has different engines that restart from the archives; the widths are traced (SaveBlockWidths) to split the save; the Particles block's pool is restored (SAVE-013)
		// [APPROX:SAVE-010] the advisor presentation block, the second clock word (the advisor clock, applied when the automatic advisor attaches; the first, the game clock the saved scripts' waits count from, is applied with their run state when the level imports its objects, SavedScriptImport), vanilla time, game-system words, the cheat chunk's second byte (+0xB of the cheat object 0xFB3520, which only its constructor and the loader 0x405570 write; the first, the cheat mode, is imported, docs/CHEATS.md), advisor scoring and UI record count (SaveRuntimeBlocks), the weather, park analyser apart from its recent-research rings, five monthly graph rings, live arrivals and recent stays and the arrival ring and the object and hire stamps the automatic advisor takes when it attaches, tag system, mechanic HQ and UI message receiver things, world states other than TPW loss state 4, arrival vehicle handles and bytes (the saved scripts of the arrival vehicles resume on their own, but the arrival scheduler's words that point at them are not imported) and the calendar session start and the advisor controller's time signature, sound, three raw words, mRequestedLastTutorial and mMessagePlaying are not imported (its current record is imported as the last tutorial advice when an automatic advisor attaches), nor are its pending advice and history when no automatic advisor is attached; the camera is imported (zoom, yaw and the required point of interest, loader 0x42CEC0) without the loader's yaw correction by the word 0x78D912; of the cell map the path cells, occupancy, MAP attributes, each path cell's texture and turn and the litter (type, claim) are read, and a saved path tile is drawn only while the cell's path neighbours equal the saved ones and its turn is 0/90/180/270 (otherwise the tile is chosen from the neighbours) — evidence needed: how the world state and arrival words (the current arrival vehicle's handle and the arrival bytes) steer the original's arrival update beyond what OpenTPW's arrival scheduler rebuilds and the camera's yaw correction word
		var engineDumps = save.Layout.Blocks.Where( block => block.Block is SaveBlock.Scripts or SaveBlock.MessageCentre or SaveBlock.TrackRides or SaveBlock.FlyingRides or SaveBlock.Rsse or SaveBlock.Sound ).ToList();
		evidence.Add( $"[EXT:original-engine-state-dumps] Not restored (OpenTPW's engines restart from the archives): {string.Join( ", ", engineDumps.Select( block => $"{block.Block} {block.Length} bytes" ) )}; the Rsse block's script ids, list order, pass counter and run state are imported when the level attaches its objects, the Particles block's pool when the level builds its particle system." );
		evidence.Add( $"[APPROX:SAVE-010] Not imported: {string.Join( ", ", save.Layout.Blocks.Where( block => block.Block is not (SaveBlock.ActionRecording or SaveBlock.World or SaveBlock.RideSystem or SaveBlock.Particles or SaveBlock.Camera) && !engineDumps.Contains( block ) ).Select( block => $"{block.Block} {block.Length} bytes" ) )}; {Things( save.World, SaveThingModel.Weather, SaveThingModel.TagSystem, SaveThingModel.MechanicHq, SaveThingModel.UIMsgReceiver )}; park analyser apart from its recent-research rings, five monthly graph rings, live arrivals and recent stays and the arrival ring and the object and hire stamps the automatic advisor takes when it attaches." );
		if ( save.RuntimeBlocks is { } blocks )
		{
			var camera = blocks.Camera;
			evidence.Add( $"Camera imported (a level starts it there): zoom {camera.Zoom}, yaw {camera.YRotation}, required point of interest ({camera.RequiredPoi.X}, {camera.RequiredPoi.Y}, {camera.RequiredPoi.Z}); the saved point ({camera.SavedPoi.X}, {camera.SavedPoi.Y}, {camera.SavedPoi.Z}), saved yaw {camera.SavedYRotation} and flags 0x{camera.Flags:X} (cleared by the loader) are not used." );
			evidence.Add( $"[APPROX:SAVE-010] Runtime blocks decoded, not applied: clock words {string.Join( "/", blocks.ClockWords )} (the first is applied to the scripts with their run state, the second to the automatic advisor when it attaches), vanilla time {blocks.VanillaTime}, game system words {string.Join( "/", blocks.GameSystemWords )}, cheat byte 1 {(blocks.Cheats.Length > 1 ? blocks.Cheats[1].ToString() : "absent")}, advisor scoring {blocks.AdvisorScoring}, UI window records {blocks.UiRecords}." );
		}
		var variables = save.World.Variables;
		var worldStateResidual = lostImported ? "" : $"world state {variables.WorldState}, ";
		evidence.Add( $"[APPROX:SAVE-010] World values not imported: {worldStateResidual}calendar session start {save.World.Calendar.SessionStart}." );
		evidence.Add( "[APPROX:ARRIVAL-001] The saved arrival scheduler is attached by the level only for an exact park clock with no saved vehicle handles and no host vehicles; other vehicle continuations remain unsupported. The level reports the attachment outcome." );
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
