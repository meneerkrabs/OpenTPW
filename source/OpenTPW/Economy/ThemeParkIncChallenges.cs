namespace OpenTPW;

/// <summary>A Theme Park Inc challenge slot's state (<c>mCurrentStatus</c>, slot <c>+0x88</c>; docs/reverse/TPI-CHALLENGE-SYSTEM.md §4).</summary>
public enum ThemeParkIncChallengeStatus
{
	/// <summary>Idle, not offered yet (the record constructor's 0, <c>0x5C22B3</c>).</summary>
	Idle = 0,
	/// <summary>Offered and awaiting the player, and also lost: the offer (<c>0x5C8244</c>) and the lost branch (<c>0x5C4815</c>) both write 1.</summary>
	Offered = 1,
	/// <summary>Accepted, the grace period running (<c>0x5C864C</c>).</summary>
	Grace = 2,
	/// <summary>Accepted, the timed phase running (<c>0x5C8561</c>, <c>0x5C5709</c>).</summary>
	Running = 3,
	/// <summary>Won (<c>0x5C428D</c>); nothing leaves it.</summary>
	Won = 4
}

/// <summary>
/// One 0x90-byte Theme Park Inc challenge record (docs/reverse/TPI-CHALLENGE-SYSTEM.md §1.2): the row's data, the availability latch, the progress values,
/// the phase end and the state. The members the state logic never reads (<c>mTargetObj2</c>, <c>mTargetStaffType</c>, <c>mGraceTargetVal</c>,
/// <c>mThingInFocus</c>, <c>mSentTimeWarning</c> and the unserialised phase start <c>+0x78</c>) are not kept.
/// </summary>
public sealed class ThemeParkIncChallengeSlot
{
	public int Type { get; internal set; }
	public int TargetTime { get; internal set; }
	public int TargetVal { get; internal set; }
	public int TargetVal2 { get; internal set; }
	/// <summary><c>mTargetObj</c> (word): the type id of the target object.</summary>
	public ushort TargetObj { get; internal set; }
	public int CashPrize { get; internal set; }
	/// <summary><c>mGTPrize</c>: golden tickets won, at least 1 (the record constructor's clamp).</summary>
	public int GTPrize { get; internal set; }
	public int SharePrize { get; internal set; }
	public int SharePriceEffectIfWon { get; internal set; }
	public int SharePriceEffectIfLost { get; internal set; }
	/// <summary><c>mDoubleOrNothingFollowup</c>: read only by the load-time follow-up filter.</summary>
	public int FollowupType { get; internal set; }
	public bool CheckAtEndOnly { get; internal set; }
	public int GracePeriodLimit { get; internal set; }
	public int TriggerData { get; internal set; }
	public int MinDaysTilTrigger { get; internal set; }
	public int MaxDaysTilTrigger { get; internal set; }
	public int Delay { get; internal set; }
	public int Sender { get; internal set; }
	/// <summary><c>mScriptIndex</c>: the challenge's text row (<c>CHALLENGE_TITLES.str</c> entry, three <c>CHALLENGE_SPECIFIC.str</c> entries).</summary>
	public int ScriptIndex { get; internal set; }
	public int MissionNeeded { get; internal set; }
	public int PromoGiven { get; internal set; }
	/// <summary><c>mChallengeLost</c> (+0x5C): how often this slot was lost.</summary>
	public int ChallengeLost { get; internal set; }
	/// <summary><c>mOfferTimeSet</c> (+0x60): the availability is latched.</summary>
	public bool OfferTimeSet { get; internal set; }
	/// <summary><c>mOfferTime</c> (+0x68): the clock time (100 ns units) after which the slot may be offered.</summary>
	public long OfferTime { get; internal set; }
	/// <summary><c>mValue</c> (+0x70).</summary>
	public int Value { get; internal set; }
	/// <summary><c>mValue2</c> (+0x74).</summary>
	public int Value2 { get; internal set; }
	/// <summary><c>mFinishTIme</c> (+0x80): the end of the current phase.</summary>
	public long FinishTime { get; internal set; }
	public ThemeParkIncChallengeStatus Status { get; internal set; }

	/// <summary>A record built from a <c>Challenges.sam</c> row (<c>0x5C21E0</c>): the row's fields, the rest 0, and the golden-ticket prize at least 1.</summary>
	// [BIN:TPI-EXE:0x5C21E0 challenge record] the constructor stores the 24 row fields (CheckAtEndOnly as row != 0, 0x5C777B setne), zeroes +0x5C, +0x60, +0x68..+0x88 and +0x8C
	// (0x5C228A..0x5C22B9) and raises mGTPrize to 1 when it is below (0x5C22C0..0x5C22CC, signed)
	public static ThemeParkIncChallengeSlot FromRow( ThemeParkIncChallengeRow row ) => new()
	{
		Type = row.Type,
		FollowupType = row.FollowupType,
		TargetTime = row.TargetTime,
		TargetVal = row.TargetVal,
		TargetVal2 = row.TargetVal2,
		TargetObj = unchecked( (ushort)row.TargetObj ),
		CashPrize = row.CashPrize,
		GTPrize = Math.Max( 1, row.GTPrize ),
		SharePrize = row.SharePrize,
		SharePriceEffectIfWon = row.SharePriceEffectWon,
		SharePriceEffectIfLost = row.SharePriceEffectLost,
		CheckAtEndOnly = row.CheckAtEndOnly != 0,
		MinDaysTilTrigger = row.MinDaysTilTrigger,
		MaxDaysTilTrigger = row.MaxDaysTilTrigger,
		Delay = row.DaysDelay,
		TriggerData = row.TriggerData,
		GracePeriodLimit = row.GracePeriodLimit,
		Sender = row.Sender,
		ScriptIndex = row.ScriptIndex,
		MissionNeeded = row.MissionNeededToActivate,
		PromoGiven = row.PromoGiven
	};

	/// <summary>
	/// <c>DidThisWinIt(atEnd)</c> (<c>0x5C3F60</c>): an end-only challenge wins only at the end; otherwise the type's test on the values (signed).
	/// <paramref name="anyStrike"/> is the live strike test type 41 reads instead of its value.
	/// </summary>
	// [BIN:TPI-EXE:0x5C3F60 win test] CheckAtEndOnly and not at the end: false (0x5C3F63..0x5C3F75); the debug byte 0xA9B894 (always 0 in play) answers true; the jump table 0x5C4130 by the byte
	// index 0x5C416C: 0 false; 1..13, 16, 17, 20, 22, 45, 46 V >= TV; 48 V2 == 0 && V >= TV; 50 V != 0; 51 V >= TV && V2 != 0; 52 V2 >= TV2 && V >= TV; 53, 56 V >= TV && V2 >= TV2;
	// 55 V >= TV && V2 == 0; 57 V == 0 ? 0 >= TV : V2 * 100 / V >= TV; 58 V2 == 0 ? 0 >= TV : V * 100 / V2 >= TV (idiv); 41 no strike now (0x634240); 19 and 28 read the park (not ported,
	// never offered); 24, 25, 27, 34..36, 38..40, 42, 43 true; every other type logs 0x9CD480 and answers true
	public bool DidThisWinIt( bool atEnd, bool anyStrike )
	{
		if ( CheckAtEndOnly && !atEnd )
			return false;
		var v = Value;
		var v2 = Value2;
		return Type switch
		{
			0 => false,
			>= 1 and <= 13 or 16 or 17 or 20 or 22 or 45 or 46 => v >= TargetVal,
			48 => v2 == 0 && v >= TargetVal,
			50 => v != 0,
			51 => v >= TargetVal && v2 != 0,
			52 => v2 >= TargetVal2 && v >= TargetVal,
			53 or 56 => v >= TargetVal && v2 >= TargetVal2,
			55 => v >= TargetVal && v2 == 0,
			57 => v == 0 ? 0 >= TargetVal : unchecked( v2 * 100 ) / v >= TargetVal,
			58 => v2 == 0 ? 0 >= TargetVal : unchecked( v * 100 ) / v2 >= TargetVal,
			41 => !anyStrike,
			// 19 and 28 compare a park accessor (0x5B70E0, 0x5B54C0) that is not ported; those types are never offered (ThemeParkIncChallenges.IsPorted)
			19 or 28 => throw new InvalidOperationException( $"Theme Park Inc challenge type {Type} reads a park accessor OpenTPW does not port." ),
			_ => true
		};
	}

	/// <summary><c>HasFailed</c> (<c>0x5C3DB0</c>), run only while the phase has days left (signed comparisons).</summary>
	// [BIN:TPI-EXE:0x5C3DB0 failure test] the jump table 0x5C3E90 by the byte index 0x5C3EC0 (type - 10): 48 V2 != 0 || V < TV; 50 V == 0; 52 V2 < TV2; 53, 56 V < TV || V2 < TV2;
	// 55 V < TV || V2 != 0; 10, 25, 34..37, 46 V < TV; 38..40 V > TV; 41..43 V != 0; every other type (types below 10 and above 58 included) never fails
	public bool HasFailed() => Type switch
	{
		48 => Value2 != 0 || Value < TargetVal,
		50 => Value == 0,
		52 => Value2 < TargetVal2,
		53 or 56 => Value < TargetVal || Value2 < TargetVal2,
		55 => Value < TargetVal || Value2 != 0,
		10 or 25 or 34 or 35 or 36 or 37 or 46 => Value < TargetVal,
		38 or 39 or 40 => Value > TargetVal,
		41 or 42 or 43 => Value != 0,
		_ => false
	};

	internal ThemeParkIncChallengeSlotSnapshot Capture() => new( Type, TargetTime, TargetVal, TargetVal2, TargetObj, CashPrize, GTPrize, SharePrize, SharePriceEffectIfWon,
		SharePriceEffectIfLost, FollowupType, CheckAtEndOnly, GracePeriodLimit, TriggerData, MinDaysTilTrigger, MaxDaysTilTrigger, Delay, Sender, ScriptIndex, MissionNeeded, PromoGiven,
		ChallengeLost, OfferTimeSet, OfferTime, Value, Value2, FinishTime, Status );

	internal static ThemeParkIncChallengeSlot Restore( ThemeParkIncChallengeSlotSnapshot saved ) => new()
	{
		Type = saved.Type,
		TargetTime = saved.TargetTime,
		TargetVal = saved.TargetVal,
		TargetVal2 = saved.TargetVal2,
		TargetObj = saved.TargetObj,
		CashPrize = saved.CashPrize,
		GTPrize = saved.GTPrize,
		SharePrize = saved.SharePrize,
		SharePriceEffectIfWon = saved.SharePriceEffectIfWon,
		SharePriceEffectIfLost = saved.SharePriceEffectIfLost,
		FollowupType = saved.FollowupType,
		CheckAtEndOnly = saved.CheckAtEndOnly,
		GracePeriodLimit = saved.GracePeriodLimit,
		TriggerData = saved.TriggerData,
		MinDaysTilTrigger = saved.MinDaysTilTrigger,
		MaxDaysTilTrigger = saved.MaxDaysTilTrigger,
		Delay = saved.Delay,
		Sender = saved.Sender,
		ScriptIndex = saved.ScriptIndex,
		MissionNeeded = saved.MissionNeeded,
		PromoGiven = saved.PromoGiven,
		ChallengeLost = saved.ChallengeLost,
		OfferTimeSet = saved.OfferTimeSet,
		OfferTime = saved.OfferTime,
		Value = saved.Value,
		Value2 = saved.Value2,
		FinishTime = saved.FinishTime,
		Status = saved.Status
	};
}

/// <summary>A challenge slot as a park save keeps it.</summary>
public sealed record ThemeParkIncChallengeSlotSnapshot( int Type, int TargetTime, int TargetVal, int TargetVal2, ushort TargetObj, int CashPrize, int GTPrize, int SharePrize,
	int SharePriceEffectIfWon, int SharePriceEffectIfLost, int FollowupType, bool CheckAtEndOnly, int GracePeriodLimit, int TriggerData, int MinDaysTilTrigger, int MaxDaysTilTrigger,
	int Delay, int Sender, int ScriptIndex, int MissionNeeded, int PromoGiven, int ChallengeLost, bool OfferTimeSet, long OfferTime, int Value, int Value2, long FinishTime,
	ThemeParkIncChallengeStatus Status );

/// <summary>
/// The challenge system as a park save keeps it: the ten slots, the next offer time and the switch. The career words the system writes (the firing line and the golden tickets) are the
/// career's (<see cref="ThemeParkIncCareer"/>, the original's 0xBFC484).
/// </summary>
public sealed record ThemeParkIncChallengesSnapshot( ThemeParkIncChallengeSlotSnapshot[] Slots, long NextChallengeDelayTime, bool SystemSwitchedOn );

/// <summary>What the challenge system reads from the park and what its outcomes do there (ParkEconomy in a park, a synthetic host in the tests).</summary>
public interface IThemeParkIncChallengeHost
{
	/// <summary>The park is open (<c>0x5B1D30</c>: world <c>+0x1EAB44</c> is 0).</summary>
	bool ParkOpen { get; }
	/// <summary>The career's mission counter (<c>+0x105C</c>).</summary>
	int MissionCounter { get; }
	/// <summary>A first loss's step of the career's firing line (<c>+0</c>, <c>0x5C4857</c>).</summary>
	void LowerFiringLine();
	/// <summary><c>0x5B9380</c>: some attraction thing's type record has <c>UsageInfo.SpecialIngredient</c> (<c>+0x218</c>) 1, fat (a hot-dog shop).</summary>
	bool HasFatShop();
	/// <summary><c>0x5B32F0(kind)</c>: the highest experience (<c>+0x210</c>) of the staff things of a kind, 0 when there is none above 0.</summary>
	int MaxExperience( int kind );
	/// <summary><c>0x5B2F40</c>: the staff things (kinds 4 to 8 and 0x14).</summary>
	int StaffCount();
	/// <summary><c>0x5AFED0</c>: the lowest truncated happiness byte (<c>+0x230</c>) of the staff things, 100 when none is lower.</summary>
	int MinStaffHappiness();
	/// <summary><c>0x634240</c>: some staff role's strike is active.</summary>
	bool AnyRoleOnStrike();
	/// <summary>A slot was offered (the offer's message 0x1B kind 0, which the original turns into the challenge mail).</summary>
	void Offered( int index );
	/// <summary>The won branch's park effects (<c>0x5C4250</c>): the cash prize through the bank and the golden tickets; the slot is already state 4.</summary>
	void Won( int index, ThemeParkIncChallengeSlot slot );
	/// <summary>The lost branch's park effects (<c>0x5C47D0</c>): the firing line on a first loss and the letter once the line is spent; the slot is already state 1.</summary>
	void Lost( int index, ThemeParkIncChallengeSlot slot, bool firstLoss );
}

/// <summary>
/// Theme Park Inc's challenge system (docs/reverse/TPI-CHALLENGE-SYSTEM.md): ten slots loaded from the level's rows of <c>Challenges.sam</c>, a switch-on gate, per-slot
/// availability, one offer at a time after <c>DaysBetweenOffers</c>, accept into a grace period or the timed phase, and per-type win and failure tests. It is not Theme Park
/// World's objective system (<see cref="ParkObjectives"/>): the rules, the type numbers and the outcomes differ. It draws no random number.
/// </summary>
// [BIN:TPI-EXE:0x5C7400 challenge system] a world thing (kind 0x13) that loads its slots at construction (0x5C74C5 -> 0x5C7630), starts with mNextChallengeDelayTime 0 and the switch off
// (0x5C744F..0x5C745E); no random number generator is called anywhere in the traced code (0x5C3610..0x5C92A0)
public sealed class ThemeParkIncChallenges
{
	/// <summary>The slots.</summary>
	public const int SlotCount = 10;

	/// <summary>One game day in clock units: 864e9 × 100 ns (every day conversion of the module pushes <c>0x2A69C000, 0xC9</c>).</summary>
	public const long Day = 864_000_000_000L;

	/// <summary>The clock's base: the FILETIME of 2000-01-01, the shared calendar's start (<see cref="ParkCalendar.Epoch"/>).</summary>
	public static readonly long ClockBase = new DateTime( 2000, 1, 1, 0, 0, 0, DateTimeKind.Utc ).ToFileTimeUtc();

	private readonly ThemeParkIncChallengeSlot[] slots = new ThemeParkIncChallengeSlot[SlotCount];

	/// <summary>A system with the level's challenges loaded (<c>0x5C7630</c>).</summary>
	public ThemeParkIncChallenges( ThemeParkIncChallengeSettings settings )
	{
		Settings = settings ?? throw new ArgumentNullException( nameof( settings ) );
		Load();
	}

	public ThemeParkIncChallengeSettings Settings { get; }
	public IReadOnlyList<ThemeParkIncChallengeSlot> Slots => slots;
	/// <summary><c>mNextChallengeDelayTime</c> (<c>sys+0x5B8</c>): the clock time the next offer must pass.</summary>
	public long NextChallengeDelayTime { get; private set; }
	/// <summary><c>mSystemSwitchedOn</c> (<c>sys+0x5C0</c>): latched once and never cleared.</summary>
	public bool SystemSwitchedOn { get; private set; }

	/// <summary>
	/// The challenge types whose trigger and value OpenTPW computes exactly from the park; any other type is never made available.
	/// </summary>
	// [EXT:tpi-capability-gates] a type whose availability trigger or progress value reads park state OpenTPW does not model (docs/reverse/TPI-CHALLENGE-SYSTEM.md, "Port status") stays
	// unoffered: its CalcAvailTime never latches. Ported: 2 (a fat shop exists, 0x5B9380; the sale counts of 0x5E5DE0), 25 (entertainer experience 0x5B32F0(6); the lowest staff happiness
	// 0x5AFED0), 41 (the staff count 0x5B2F40; the strike flags 0x634240)
	public static bool IsPorted( int type ) => type is 2 or 25 or 41;

	/// <summary>The clock at <paramref name="turn"/>: the base plus floor(turn × 15000 / 4) seconds in 100 ns units.</summary>
	// [BIN:TPI-EXE:0x6089C0 clock now] the clock (world +0x428) returns its base plus world +0x1EAB40 (the turn) × rate (+0x1C, 15000) / 4 × 10 000 000 as 64-bit arithmetic
	public static long Now( long turn ) => unchecked( ClockBase + turn * 15000 / 4 * 10_000_000 );

	/// <summary>
	/// Whole days left in a slot's phase, as the update tests it: (mFinishTIme − now) / day as a signed 64-bit division truncating toward zero, of which the low dword is kept.
	/// Once a single update has passed the finish by a day or more the result stays negative and nonzero, so the phase never times out.
	/// </summary>
	// [BIN:TPI-EXE:0x5C54E0 days left] 0x7D8E30 subtracts the clock from mFinishTIme (+0x80), __alldiv 0x871A30 divides by the day and the caller tests eax, the quotient's low dword
	public static int DaysLeft( ThemeParkIncChallengeSlot slot, long turn ) => unchecked( (int)((slot.FinishTime - Now( turn )) / Day) );

	/// <summary>
	/// Loads the slots: all ten empty, then each of the level's first ten positive row numbers built into the first empty slot (one without room is dropped), then the follow-up filter.
	/// </summary>
	// [BIN:TPI-EXE:0x5C7630 load slots] mType = 0 in every slot (0x5C765C..0x5C766F); for each level entry n (0x5C76BD) n <= 0 (signed jle) is skipped, else row Challenges[n] (a row number,
	// not a type) is built by 0x5C21E0 and copied whole into the first slot whose mType is 0 (0x5C77A5, rep movsl 0x5C77C9); [BIN:TPI-EXE:0x5C75C0 follow-up filter] then each slot in turn
	// whose mDoubleOrNothingFollowup is nonzero is emptied unless another occupied slot has that type
	private void Load()
	{
		for ( var index = 0; index < SlotCount; index++ )
			slots[index] = new ThemeParkIncChallengeSlot();
		foreach ( var row in Settings.LevelRows )
		{
			if ( row <= 0 )
				continue;
			var free = Array.FindIndex( slots, slot => slot.Type == 0 );
			if ( free >= 0 )
				slots[free] = ThemeParkIncChallengeSlot.FromRow( Settings.Rows[row] );
		}
		for ( var index = 0; index < SlotCount; index++ )
		{
			var followup = slots[index].FollowupType;
			if ( slots[index].Type == 0 || followup == 0 )
				continue;
			var found = false;
			for ( var other = 0; other < SlotCount; other++ )
			{
				if ( other != index && slots[other].Type != 0 && slots[other].Type == followup )
				{
					found = true;
					break;
				}
			}
			if ( !found )
				slots[index].Type = 0;
		}
	}

	private bool AnyAccepted() => slots.Any( slot => slot.Status is ThemeParkIncChallengeStatus.Grace or ThemeParkIncChallengeStatus.Running );

	/// <summary>
	/// The system's thing update, once per world update pass (<c>0x5C7930</c>): the switch, then either every accepted slot's progress and tests, or the availability and an offer.
	/// The caller runs it only where the original's world pass reaches the thing (not in a lost park).
	/// </summary>
	// [BIN:TPI-EXE:0x5C7930 challenge update] the mode word [0xC65B80] must be 0 (single player; OpenTPW's slice has no other mode); with the switch off it latches when the park is open (0x5B1D30)
	// and the career's +0x105C >= MissionNumberForFirstChallenge (signed jl skip, 0x5C7967..0x5C7977), else nothing happens; then any slot in state 2 or 3 makes the pass process every slot
	// (0x5C79D2..0x5C7A46), else every non-empty idle slot's CalcAvailTime runs and an offer is tried when the clock is past mNextChallengeDelayTime (0x7D8F30, unsigned, strict)
	public void Update( long turn, IThemeParkIncChallengeHost host )
	{
		ArgumentNullException.ThrowIfNull( host );
		if ( !SystemSwitchedOn )
		{
			if ( !host.ParkOpen || host.MissionCounter < Settings.MissionNumberForFirstChallenge )
				return;
			SystemSwitchedOn = true;
		}
		if ( AnyAccepted() )
		{
			for ( var index = 0; index < SlotCount; index++ )
				UpdateAccepted( index, turn, host );
			return;
		}
		foreach ( var slot in slots )
		{
			if ( slot.Type != 0 && slot.Status == ThemeParkIncChallengeStatus.Idle )
				CalcAvailTime( slot, turn, host );
		}
		if ( (ulong)Now( turn ) > (ulong)NextChallengeDelayTime && Offer( turn ) is var offered and >= 0 )
			host.Offered( offered );
	}

	// [BIN:TPI-EXE:0x5C79E2 grace slot] state 2: UpdateCurrentValue (0x5C4DD0), days left 0 starts the timed phase (0x5C5680), and then a closed park loses it (0x5C7A08..0x5C7A34); no win or failure test
	// [BIN:TPI-EXE:0x5C7A50 running slot] state 3: UpdateCurrentValue, then with no day left the warning byte is cleared, 0x5C73D0 (type 42's shop reopening) and 0x5C2150 (the sender's advice
	// stopped) run and DidThisWinIt(1) decides won (0x5C88B0) or lost (0x5C88F0) without the failure test; with days left HasFailed loses it, else a closed park loses it, else
	// DidThisWinIt(0) wins it (0x5C7AEB..0x5C7B36)
	private void UpdateAccepted( int index, long turn, IThemeParkIncChallengeHost host )
	{
		var slot = slots[index];
		switch ( slot.Status )
		{
			case ThemeParkIncChallengeStatus.Grace:
				UpdateCurrentValue( slot, host );
				if ( DaysLeft( slot, turn ) == 0 )
					StartTimedPhase( slot, turn );
				if ( !host.ParkOpen )
					Lose( index, host );
				break;
			case ThemeParkIncChallengeStatus.Running:
				UpdateCurrentValue( slot, host );
				if ( DaysLeft( slot, turn ) == 0 )
				{
					// type 42 (its shops' reopening, 0x5C73D0) is never offered (IsPorted); the sender's advice (0x5C2150) is the advisor's, which the slice does not run
					if ( slot.DidThisWinIt( atEnd: true, host.AnyRoleOnStrike() ) )
						Win( index, host );
					else
						Lose( index, host );
				}
				else if ( slot.HasFailed() || !host.ParkOpen )
					Lose( index, host );
				else if ( slot.DidThisWinIt( atEnd: false, host.AnyRoleOnStrike() ) )
					Win( index, host );
				break;
		}
	}

	/// <summary><c>UpdateCurrentValue</c> (<c>0x5C4DD0</c>) for the ported types: 25 polls the lowest staff happiness, 41 the strike flags; type 2 counts sale events and polls nothing.</summary>
	// [BIN:TPI-EXE:0x5C50C6 staff happiness value] type 25 stores 0x5AFED0(&mThingInFocus) in mValue; [BIN:TPI-EXE:0x5C4F70 strike value] type 41 stores 0x634240() in mValue;
	// [BIN:TPI-EXE:0x5C52C3 no poll] a type without a case (2, 17 and others) logs "Case missing from UpdateCurrentValue??" and leaves the values alone
	internal static void UpdateCurrentValue( ThemeParkIncChallengeSlot slot, IThemeParkIncChallengeHost host )
	{
		switch ( slot.Type )
		{
			case 25:
				slot.Value = host.MinStaffHappiness();
				break;
			case 41:
				slot.Value = host.AnyRoleOnStrike() ? 1 : 0;
				break;
		}
	}

	/// <summary>
	/// <c>CalcAvailTime</c> (<c>0x5C36D0</c>): while the latch is clear, the type's trigger after <c>MinDaysTilTrigger</c> days, or any time after <c>MaxDaysTilTrigger</c> days when that is
	/// set, and the mission gate; when it holds, the offer time is the clock plus <c>DaysDelay</c> days and the latch is set, never to be cleared.
	/// </summary>
	// [BIN:TPI-EXE:0x5C36D0 availability] only while mOfferTimeSet (+0x60) is clear; ok = trigger(type) (0x5C3BB8/0x5C3C14); days = 0x608C70() / day (the park age, ThemeParkIncStaff.GameDay),
	// ok = (ok && days > MinDaysTilTrigger) || (MaxDaysTilTrigger != 0 && days > MaxDaysTilTrigger), both unsigned seta (0x5C3AF5, 0x5C3B30); ok &= MissionNeededToActivate <= career +0x105C
	// (unsigned setbe, 0x5C3B45); then mOfferTime = clock + DaysDelay (zero-extended) × day and mOfferTimeSet = 1 (0x5C3B4C..0x5C3BAD)
	internal static void CalcAvailTime( ThemeParkIncChallengeSlot slot, long turn, IThemeParkIncChallengeHost host )
	{
		if ( slot.OfferTimeSet || !IsPorted( slot.Type ) )
			return;
		var ok = Trigger( slot, host );
		var days = ThemeParkIncStaff.GameDay( turn );
		ok = (ok && days > unchecked( (uint)slot.MinDaysTilTrigger )) || (slot.MaxDaysTilTrigger != 0 && days > unchecked( (uint)slot.MaxDaysTilTrigger ));
		ok = ok && unchecked( (uint)slot.MissionNeeded ) <= unchecked( (uint)host.MissionCounter );
		if ( !ok )
			return;
		slot.OfferTime = unchecked( Now( turn ) + (long)(uint)slot.Delay * Day );
		slot.OfferTimeSet = true;
	}

	/// <summary>The availability trigger of a ported type.</summary>
	// [BIN:TPI-EXE:0x5C3727 hot-dog trigger] type 2: 0x5B9380(), TriggerData unread; [BIN:TPI-EXE:0x5C378A entertainer trigger] type 25: 0x5B32F0(6) >= TriggerData (signed, 0x5C3917 jl);
	// [BIN:TPI-EXE:0x5C39B4 staff trigger] type 41: 0x5B2F40() >= TriggerData (signed jl)
	private static bool Trigger( ThemeParkIncChallengeSlot slot, IThemeParkIncChallengeHost host ) => slot.Type switch
	{
		2 => host.HasFatShop(),
		25 => host.MaxExperience( 6 ) >= slot.TriggerData,
		41 => host.StaffCount() >= slot.TriggerData,
		_ => false
	};

	/// <summary>
	/// The offer (<c>0x5C8140</c>): nothing while a slot is accepted or before <c>mNextChallengeDelayTime</c>; otherwise the lowest available idle slot whose offer time has passed is
	/// offered (state 1) and the next offer waits <c>DaysBetweenOffers</c> days. Unanswered offers do not block later ones. The slot offered, or -1.
	/// </summary>
	// [BIN:TPI-EXE:0x5C8140 offer] returns while any slot is in state 2 or 3 or the clock is before +0x5B8 (0x7D8ED0, unsigned strict); picks the lowest index with mType != 0, state 0, mOfferTimeSet
	// and the clock past mOfferTime (0x7D8F30, unsigned strict, 0x5C81B2..0x5C81FA); posts message 0x1B kind 0 (0x5C8320, always true), logs "Challenge %d is being offered!", sets state 1 and
	// +0x5C1 (no reader) and +0x5B8 = clock + DaysBetweenOffers (sign-extended) × day (0x5C8250..0x5C82AD)
	internal int Offer( long turn )
	{
		if ( AnyAccepted() || (ulong)Now( turn ) < (ulong)NextChallengeDelayTime )
			return -1;
		var now = Now( turn );
		for ( var index = 0; index < SlotCount; index++ )
		{
			var slot = slots[index];
			if ( slot.Type == 0 || slot.Status != ThemeParkIncChallengeStatus.Idle || !slot.OfferTimeSet || (ulong)now <= (ulong)slot.OfferTime )
				continue;
			slot.Status = ThemeParkIncChallengeStatus.Offered;
			NextChallengeDelayTime = unchecked( now + (long)Settings.DaysBetweenOffers * Day );
			return index;
		}
		return -1;
	}

	/// <summary>
	/// The accept (<c>0x5C84C0</c>): the values reset, then a challenge without a grace period starts its timed phase (state 3, <c>TargetTime</c> days), one with a grace period starts it
	/// (state 2, <c>GracePeriodLimit</c> days). The routine tests neither the state nor the other slots: the mail view's control is enabled only in state 1 with the park open.
	/// </summary>
	// [BIN:TPI-EXE:0x5C84C0 accept] the sender's advice is stopped and sound 0xC6 plays (presentation); mValue = mValue2 = 0 (0x5C8520, 0x5C852A); the challenge mails of kind 0x91 are marked;
	// GracePeriodLimit == 0: state 3, +0x8E = 0, +0x78 = clock, mFinishTIme = clock + TargetTime (sign-extended) × day (0x5C8557..0x5C8607), type 42 closes its shops (never offered here);
	// else state 2, +0x78 = clock, mFinishTIme = clock + GracePeriodLimit (sign-extended) × day (0x5C8642..0x5C86E7)
	public void Accept( int index, long turn )
	{
		var slot = slots[index];
		slot.Value = 0;
		slot.Value2 = 0;
		var now = Now( turn );
		if ( slot.GracePeriodLimit == 0 )
		{
			slot.Status = ThemeParkIncChallengeStatus.Running;
			slot.FinishTime = unchecked( now + (long)slot.TargetTime * Day );
		}
		else
		{
			slot.Status = ThemeParkIncChallengeStatus.Grace;
			slot.FinishTime = unchecked( now + (long)slot.GracePeriodLimit * Day );
		}
	}

	/// <summary>The grace period's end (<c>0x5C5680</c>): the timed phase starts; the values carry over.</summary>
	// [BIN:TPI-EXE:0x5C5680 timed phase] posts message 0x1B kind 1; state 3, +0x8E = 0, +0x78 = clock, mFinishTIme = clock + TargetTime × day; type 42 closes its shops; sound 0xC6; mValue and
	// mValue2 are not reset (0x5C569C..0x5C57E4)
	private static void StartTimedPhase( ThemeParkIncChallengeSlot slot, long turn )
	{
		slot.Status = ThemeParkIncChallengeStatus.Running;
		slot.FinishTime = unchecked( Now( turn ) + (long)slot.TargetTime * Day );
	}

	// [BIN:TPI-EXE:0x5C4250 won] sound 0xC6, state 4, then the cash prize, the golden tickets, the shares, message 0x1B kind 2 and the promotion (ParkEconomy's side, IThemeParkIncChallengeHost.Won)
	private void Win( int index, IThemeParkIncChallengeHost host )
	{
		slots[index].Status = ThemeParkIncChallengeStatus.Won;
		host.Won( index, slots[index] );
	}

	// [BIN:TPI-EXE:0x5C47D0 lost] sound 0xAF, state 1 (0x5C4815); on the slot's first loss (mChallengeLost +0x5C is 0) the career's firing line falls by one (0x5C4857) and the share price
	// by SharePriceEffectIfLost; mChallengeLost += 1 (0x5C4870); the post that follows is the host's (IThemeParkIncChallengeHost.Lost); the caller clears mActualThing[i] (no reader)
	private void Lose( int index, IThemeParkIncChallengeHost host )
	{
		var slot = slots[index];
		slot.Status = ThemeParkIncChallengeStatus.Offered;
		var first = slot.ChallengeLost == 0;
		if ( first )
			host.LowerFiringLine();
		slot.ChallengeLost = unchecked( slot.ChallengeLost + 1 );
		host.Lost( index, slot, first );
	}

	/// <summary>
	/// A challenge event (<c>0x5C8940</c>): every accepted slot (state 2 or 3) of <paramref name="type"/> whose target object is <paramref name="target"/>, or every one when
	/// <paramref name="target"/> is 0, adds <paramref name="amount"/> to its value (or its second value).
	/// </summary>
	// [BIN:TPI-EXE:0x5C8940 challenge event] per slot: state 3 or 2, mType == type, then mTargetObj == obj (cmpw) or obj == 0 (testw); toV2 adds to +0x74, else +0x70
	public void AddEvent( int type, int amount, ushort target, bool toValue2 )
	{
		foreach ( var slot in slots )
		{
			if ( slot.Status is not (ThemeParkIncChallengeStatus.Grace or ThemeParkIncChallengeStatus.Running) || slot.Type != type )
				continue;
			if ( slot.TargetObj != target && target != 0 )
				continue;
			if ( toValue2 )
				slot.Value2 = unchecked( slot.Value2 + amount );
			else
				slot.Value = unchecked( slot.Value + amount );
		}
	}

	/// <summary>
	/// Offers slot <paramref name="index"/> now whatever its trigger (a developer and smoke-test helper): its availability is latched as due and the offer runs, with every rule of the
	/// offer itself (no accepted slot, the delay since the last offer, the lowest due slot first). The slot offered, or -1.
	/// </summary>
	// [EXT:debug-hud] developer/smoke helper, not original behaviour (the original's debug menu id 0x186A1 runs the offer 0x5C8140 itself, 0x53523B, without latching a slot)
	internal int DebugOffer( int index, long turn )
	{
		var slot = slots[index];
		if ( slot.Type == 0 || slot.Status != ThemeParkIncChallengeStatus.Idle || !IsPorted( slot.Type ) )
			return -1;
		slot.OfferTimeSet = true;
		slot.OfferTime = unchecked( Now( turn ) - 1 );
		return Offer( turn );
	}

	internal ThemeParkIncChallengesSnapshot Capture() => new( slots.Select( slot => slot.Capture() ).ToArray(), NextChallengeDelayTime, SystemSwitchedOn );

	internal static bool IsValid( ThemeParkIncChallengesSnapshot? state ) =>
		state == null || state.Slots is { Length: SlotCount } saved && saved.All( slot => slot != null && Enum.IsDefined( slot.Status )
			&& (slot.Status == ThemeParkIncChallengeStatus.Idle || slot.Type != 0)
			&& (slot.Status is not (ThemeParkIncChallengeStatus.Offered or ThemeParkIncChallengeStatus.Grace or ThemeParkIncChallengeStatus.Running) || IsPorted( slot.Type )) && slot.ChallengeLost >= 0 && (slot.Type == 0 || slot.GTPrize >= 1) );

	internal void Restore( ThemeParkIncChallengesSnapshot state )
	{
		for ( var index = 0; index < SlotCount; index++ )
			slots[index] = ThemeParkIncChallengeSlot.Restore( state.Slots[index] );
		NextChallengeDelayTime = state.NextChallengeDelayTime;
		SystemSwitchedOn = state.SystemSwitchedOn;
	}
}
