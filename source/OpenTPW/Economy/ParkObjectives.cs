namespace OpenTPW;

/// <summary>
/// Cumulative park counters keyed by name (sales per shop type, admissions, ride uses, builds,
/// profits, reported records). Challenges compare them with a snapshot taken on acceptance.
/// </summary>
public sealed class ParkCounters
{
	private readonly SortedDictionary<string, long> values = new( StringComparer.Ordinal );

	public long this[string key] => values.GetValueOrDefault( key );
	public IReadOnlyDictionary<string, long> Values => values;

	public void Add( string key, long amount ) => values[key] = checked(values.GetValueOrDefault( key ) + amount);

	public void Max( string key, long value ) => values[key] = Math.Max( values.GetValueOrDefault( key ), value );

	internal void Restore( IReadOnlyDictionary<string, long> restored )
	{
		values.Clear();
		foreach ( var (key, value) in restored )
			values[key] = value;
	}

	public static string Sold( int shopType, int ingredient ) => $"sold:{shopType}:{ingredient}";
	public static string RideUses( int infoId ) => $"rideuse:{infoId}";
	/// <summary>The uses of one ride object (its economy instance id), what a challenge filtered to the carried object counts (ECON-055).</summary>
	public static string RideUsesOfThing( int instanceId ) => $"rideuse-thing:{instanceId}";
	public static string Built( int infoId ) => $"built:{infoId}";
	/// <summary>The park turn of the type's first purchase after turn 0 (park record +28 of the original); 0 while unset.</summary>
	public static string FirstPurchaseTurn( int infoId ) => $"firstturn:{infoId}";
	public static string Record( ParkRecordKind kind, int infoId ) => $"record:{kind}:{infoId}";
	public static string Researched( ResearchCategory category ) => $"researched:{category}";
	public const string Admissions = "admissions";
	public const string ShopProfit = "profit:shop";
	public const string SideshowProfit = "profit:sideshow";
}

/// <summary>State of an offered or accepted challenge; <c>Baseline</c> is the measure at acceptance of a count type, the running sum of the daily values of types 24, 25 and 27.</summary>
public sealed record ActiveChallenge( int DefinitionIndex, long OfferedDay, bool Accepted, long StartDay, long DeadlineDay, long Baseline, [property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )] long DeadlineSeconds = 0 )
{
	/// <summary>The park second the running challenge ends at (the stamp of +0x3d0 at its accept): the exact deadline when it is known, else the start of <see cref="DeadlineDay"/>.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public long EndSeconds => DeadlineSeconds != 0 ? DeadlineSeconds : DeadlineDay * ParkCalendar.SecondsPerDay;
}

/// <summary>
/// Golden tickets in the order of their award messages (TAG_SYSTEM 180–191); the thresholds come
/// from <c>GoldenTicketLocal</c>/<c>GoldenTicketGlobal</c>. The mapping of the last three to
/// <c>MinCellsOwned</c>, <c>MinCellsCovered</c> and "all land" is inferred from the messages.
/// </summary>
public enum GoldenTicketKind
{
	Visitors,
	PeopleInPark,
	Happiness,
	AllResearchedAndBuilt,
	ProfitYear,
	RecentVisitors,
	CoasterHeight,
	GokartExcitement,
	WaterLength,
	BigPark,
	CamerasEverywhere,
	AllLandOwned
}

/// <summary>
/// Challenges (<c>Challenges.sam</c> + <c>ChallengesInThisLevel</c> + <c>Challenges.*</c> timing) and
/// golden tickets. Data-driven: definitions, level order, prizes, target times/values/objects,
/// follow-ups, <c>Independent</c>, <c>CheckAtEndOnly</c>, offer delays, declines to forfeit and every
/// golden ticket threshold. The Easymode save stores the level's challenge list in exactly this order
/// (docs/ECONOMY.md). <b>Approximations</b>: types without a progress source (23, 26) never complete.
/// </summary>
public sealed class ParkObjectives
{
	private readonly BalanceSettings settings;
	private readonly IEconomyObjectCatalog catalog;
	private readonly Dictionary<int, int> declines = new();
	private readonly HashSet<int> finished = new();
	private readonly HashSet<GoldenTicketKind> tickets = new();

	public ParkObjectives( BalanceSettings settings, IEconomyObjectCatalog catalog )
	{
		this.settings = settings;
		this.catalog = catalog;
		NextOfferSeconds = settings.ChallengeTiming.DaysUntilFirstChallenge * ParkCalendar.SecondsPerDay;
	}

	/// <summary>
	/// The park second the next offer is due at (the challenge system's next event time, +0x3d0); until the system is switched on, the second the park's age reaches
	/// <c>DaysUntilFirstChallenge</c>.
	/// </summary>
	public long NextOfferSeconds { get; private set; }
	/// <summary>The park day of <see cref="NextOfferSeconds"/>.</summary>
	public long NextOfferDay => NextOfferSeconds / ParkCalendar.SecondsPerDay;
	/// <summary>
	/// The challenge system's switch (+0x3d8): off until the park is open on or after day <c>DaysUntilFirstChallenge</c>; on, offers follow the next event time alone.
	/// </summary>
	public bool SwitchedOn { get; private set; }
	public int NextListPosition { get; private set; }
	public ActiveChallenge? Current { get; private set; }
	/// <summary>
	/// The carried object (<c>mActualThing</c>, +0x3E0 of the challenge system): the economy instance id of the object a won challenge hands to its follow-up, 0 when none.
	/// While it is set, an accepted challenge's accumulator takes only events of that object (0x004D27A0): ride uses of types 17, 20 and 22, and none of the object-less
	/// events (sales, profits and the daily averages).
	/// </summary>
	public int ActualThing { get; private set; }
	private readonly Dictionary<int, int> carried = new();
	/// <summary>
	/// The object each challenge's slot carries (<c>mThingIdForFollowup</c>, +0x2E of the slot): written by the checks of types 14, 15 and 18 when they pass and by
	/// the follow-up offer from <see cref="ActualThing"/>, keyed by the challenge's Info index; 0 entries are not kept.
	/// </summary>
	public IReadOnlyDictionary<int, int> CarriedThings => carried;
	public IReadOnlyCollection<int> Finished => finished;
	public IReadOnlyDictionary<int, int> Declines => declines;
	public IReadOnlyCollection<GoldenTicketKind> GoldenTickets => tickets;

	public ChallengeDefinition? CurrentDefinition => Current == null ? null : settings.Challenges[Current.DefinitionIndex];

	/// <summary>Challenge types whose progress the simulation can measure (see <see cref="Measure"/>).</summary>
	public static bool IsSupportedType( int type ) => type is >= 1 and <= 22 or 24 or 25 or 27 or 28 or 29 or 30 or 31 or 32 or 33;

	/// <summary>The progress value of a challenge type, absolute; count-like types are compared with the acceptance baseline.</summary>
	/// <param name="coasterSections">The most counted sections of the park's coasters of an Info.Id (0: any coaster), 0 without one; null when the host has no coasters.</param>
	public long Measure( ChallengeDefinition definition, ParkCounters counters, ParkStaff staff, IReadOnlyCollection<ParkObjectState> objects, IParkGuestStatistics guests,
		Func<int, long>? coasterSections = null )
	{
		long Built( int infoId ) => infoId == 0 ? 0 : counters[ParkCounters.Built( infoId )];
		long Sold( int shopType, int ingredient = -1 ) => ingredient >= 0
			? counters[ParkCounters.Sold( shopType, ingredient )]
			: Enumerable.Range( 0, 5 ).Sum( value => counters[ParkCounters.Sold( shopType, value )] );
		// [BIN:TPW-EXE:0x4c9d80 staff happiness] the average over the staff things (model types 4-8, or the one type 0x4c9cc0 is given) of the truncated low byte of the happiness single (+0x1F8), an integer division, 0 without staff
		int AverageHappiness( StaffType? type )
		{
			var members = staff.Members.Where( member => type == null || member.Type == type ).ToList();
			return members.Count == 0 ? 0 : members.Sum( member => StaffVitals.Byte( member.Happiness ) ) / members.Count;
		}
		// [BIN:TPW-EXE:0x4ca160 toilet cleanliness] over the relief things (type 3, flag +0x32 bit 0) the integer average of 100 minus the low byte of the gauge single (+0x44), 0 without a toilet; the daily message 0x4d2860 (type 24) adds 100 minus that
		int ToiletCleanliness()
		{
			var toilets = objects.Where( item => catalog.TryGet( item.InfoId, out var info ) && info.ProvidesRelief ).ToList();
			var dirtiness = toilets.Count == 0 ? 0 : toilets.Sum( item => 100 - StaffVitals.Byte( (float)item.Repair ) ) / toilets.Count;
			return 100 - dirtiness;
		}
		// [BIN:STP-PPC:0x100F41DC staff skill] skill = trunc(20 × (grade + training percentage / 100)) in single precision; grade 4 at 0 % is 80
		long SkillPercent( StaffType type )
		{
			var members = staff.OfType( type ).ToList();
			return members.Count == 0 ? 0 : (long)members.Average( member => ParkStaff.StaffSkill( member ) );
		}
		// [BIN:TPW-EXE:0x4d1660 challenge check] types 1-8, 11-13, 16, 17, 20 and 22 compare an accumulated value (the system's +0x3dc) with TargetVal; types 9 and 10 are 100 × the guests with a balloon or a costume (0x4c9530 / 0x4c95d0) / the census (0x4c9130)
		// [BIN:TPW-EXE:0x4e16b0 shop sale] a paid sale adds 1 to a challenge type by the shop's ShopType (+0x164) and SpecialIngredient (+0x158): ShopType 1 type 5; 2 or 4 by the ingredient 1, 2, 3, 4 to types 2, 1, 3, 4 (the jump tables 0x4e18e4 / 0x4e18fc); 3 type 8; 5 type 7; 6 type 6; a ShopType 2 or 4 sale with another ingredient and other ShopTypes add nothing
		// [BIN:TPW-EXE:0x4d27a0 challenge accumulator] an event is added to the system's value (+0x3dc) only while a challenge is accepted and only into a slot of the event's type whose TargetObj equals the event's object, which a shop sale sets to 0 (the shipped challenges of types 1-8 have TargetObj 0)
		// [BIN:TPW-EXE:0x4d2860 daily challenge message] the day message 0xb (the one 0x4dd320 rolls the attraction rings on) adds to the accumulator, for the accepted slot of that type, 100 minus the toilet cleanliness 0x4ca160 (type 24), the average happiness of all staff 0x4c9d80 (25) or of the staff type of TargetStaffType 0x4c9cc0 (27, handyman model 5, mechanic 4, entertainer 6, guard 7, researcher 8); 0x4d1660 compares TargetVal with the sum divided by TargetTime (signed integer division), so these three are daily sums (AdvanceDay), not the current value
		return definition.Type switch
		{
			1 => Sold( 2, 2 ) + Sold( 4, 2 ),   // special ingredient 2 of a ShopType 2 or 4 shop (fries: food shop with salt)
			2 => Sold( 2, 1 ) + Sold( 4, 1 ),   // ingredient 1 (burgers: food shop with fat)
			3 => Sold( 2, 3 ) + Sold( 4, 3 ),   // ingredient 3 (drinks)
			4 => Sold( 2, 4 ) + Sold( 4, 4 ),   // ingredient 4 (ice creams: food shop with sugar)
			5 => Sold( 1 ),      // gifts
			6 => Sold( 6 ),      // balloons sold (approximates "kids with balloons")
			7 => Sold( 5 ),      // costumes
			8 => Sold( 3 ),      // restaurant meals
			9 => guests.KidsWithBalloonsPercent,
			10 => guests.KidsWithCostumesPercent,
			11 => counters[ParkCounters.Admissions],
			12 => counters[ParkCounters.ShopProfit],
			13 => counters[ParkCounters.SideshowProfit],
			// [BIN:TPW-EXE:0x4d1660 challenge types 14 and 19] 14 passes when TargetVal <= 0x4cc810 for any coaster (the most counted sections, as type 18); 19 when
			// 0x4cc810(TargetObj) is above 2, whatever TargetVal (Target gives 3)
			14 => coasterSections?.Invoke( 0 ) ?? 0,
			15 => counters[ParkCounters.Record( ParkRecordKind.KartCrossroads, definition.TargetObject )],
			16 => counters[ParkCounters.Researched( ResearchCategory.Ride )],
			// [BIN:TPW-EXE:0x5021e5 ride use] the event 16 of 0x501db0 adds 1 to the accumulator under 17, 20 and 22 alike (0x5021e5, 0x50224b, 0x5022b1), with the used
			// object's Info.Id as the slot's object; 0x4d1660 compares 22 like 17 and 20
			// [BIN:TPW-EXE:0x4d27a0 challenge accumulator] with the carried object (+0x3e0) set an event counts only when its object (parameter B, the used ride's thing id at 0x5021c5) is that one
			17 or 20 or 22 => ActualThing != 0 ? counters[ParkCounters.RideUsesOfThing( ActualThing )] : counters[ParkCounters.RideUses( definition.TargetObject )],
			// [BIN:TPW-EXE:0x4d1873 challenge type 18] passes when TargetVal <= 0x4cc810(TargetObj): the unsigned maximum, over the rides of track type 3 (0x4e2b50) whose id is
			// TargetObj (any when 0), of the controller's counted sections (+0x108 via 0x43e0e0, zeroed and counted by the rating final 0x43df90), 0 without one; no accumulator, no baseline
			18 => coasterSections?.Invoke( definition.TargetObject ) ?? 0,
			19 => coasterSections?.Invoke( definition.TargetObject ) ?? 0,
			21 => Built( definition.TargetObject ) > 0 ? counters[ParkCounters.Record( ParkRecordKind.KartSections, definition.TargetObject )] : 0,
			// the day's value of the three daily-average types (summed by AccumulateDaily)
			24 => ToiletCleanliness(),
			25 => AverageHappiness( null ),
			27 => AverageHappiness( (StaffType)Math.Clamp( definition.TargetStaffType, 0, 4 ) ),
			// [BIN:STP-PPC:0x100CE754 challenge type 28] passes when TargetVal <= the highest upgrade level byte (+0x4C, 0 = base) of the target rides (0x100C5740, -1 when none)
			28 => objects.Where( item => item.InfoId == definition.TargetObject ).Select( item => (long)item.Level ).DefaultIfEmpty( -1 ).Max(),
			29 => SkillPercent( (StaffType)Math.Clamp( definition.TargetStaffType, 0, 4 ) ),
			// [BIN:STP-PPC:0x100CE9CC challenge types 30-32] pass when the object control's build count of TargetObj (+0x18, raised by every Thing construction at 0x100DA874 and never lowered) is above 0; TargetVal and TargetObj2 are not read
			30 or 31 or 32 => Built( definition.TargetObject ) > 0 ? 1 : 0,
			// [BIN:STP-PPC:0x100CEA00 challenge type 33] passes when both TargetObj and TargetObj2 have been built
			33 => Built( definition.TargetObject ) > 0 && Built( definition.TargetObject2 ) > 0 ? 1 : 0,
			_ => 0
		};
	}

	/// <summary>
	/// Types the accumulator reaches with events that carry no object (sales, profits and the daily messages: both shorts of 0x004E16B0, 0x004E1920 and 0x004D2860 are 0), which
	/// the carried object's filter refuses.
	/// </summary>
	// [BIN:TPW-EXE:0x4d27a0 challenge accumulator] the event counts when the slot's type and TargetObj match and the carried object (+0x3e0) is 0 or equals the event's object
	public static bool TakesObjectlessEvents( int type ) => type is >= 1 and <= 8 or 12 or 13 or 16 or 24 or 25 or 27;

	internal void RestoreCarried( int actualThing, IReadOnlyDictionary<int, int>? carriedThings )
	{
		ActualThing = actualThing;
		carried.Clear();
		foreach ( var (index, thing) in carriedThings ?? new Dictionary<int, int>() )
			SetCarried( index, thing );
	}

	private void SetCarried( int challengeIndex, int thing )
	{
		if ( thing == 0 )
			carried.Remove( challengeIndex );
		else
			carried[challengeIndex] = thing;
	}

	/// <summary>Types whose daily value (<see cref="Measure"/>) is added up from the acceptance and compared as the sum divided by TargetTime (0x4d2860, 0x4d1660).</summary>
	public static bool IsDailyAverage( int type ) => type is 24 or 25 or 27;

	/// <summary>Types measured as a change since acceptance rather than an absolute value.</summary>
	public static bool IsCountSinceAcceptance( int type ) => type is >= 1 and <= 8 or 11 or 12 or 13 or 16 or 17 or 20 or 22;

	/// <summary>Target value; the build types 30-33 measure 1 when built, and type 19 needs more than 2 counted sections whatever TargetVal.</summary>
	// [BIN:STP-PPC:0x100CE690 challenge check] a 34-entry handler table (0x1022C794) by type; types 30-33 test "built" (1), type 28 compares TargetVal with the level
	public static long Target( ChallengeDefinition definition ) => definition.Type switch { >= 30 and <= 33 => 1, 19 => 3, _ => definition.TargetValue };

	/// <summary>
	/// The day message's share of the challenge system (0x004D2860): a running challenge of a daily-average type (24, 25, 27) adds the day's value to its sum; the carried object's
	/// filter refuses these events, which carry no object.
	/// </summary>
	public void AccumulateDaily( Func<ChallengeDefinition, long> measure )
	{
		if ( Current is not { Accepted: true } running || ActualThing != 0 )
			return;
		var definition = settings.Challenges[running.DefinitionIndex];
		if ( IsDailyAverage( definition.Type ) )
			Current = running with { Baseline = running.Baseline + measure( definition ) };
	}

	/// <summary>
	/// The day-granular entry (tests and tools): the day message's accumulation, then one update at the start of <paramref name="day"/> on a turn the running check is not skipped for.
	/// </summary>
	public IEnumerable<(ParkEventKind Kind, int Index, long Amount, string Detail)> AdvanceDay( long day, Func<ChallengeDefinition, long> measure, bool parkOpen = true, Func<ChallengeDefinition, int>? bestThing = null )
	{
		AccumulateDaily( measure );
		return Update( day * ParkCalendar.SecondsPerDay, 1, measure, parkOpen, bestThing );
	}

	/// <summary>
	/// One update of the challenge system (its thing update, every park turn): the switch, the offer when the next event time has passed, and a running challenge's check;
	/// returns the events to publish and the prize won (0 when none).
	/// </summary>
	/// <param name="seconds">The park clock in seconds.</param>
	/// <param name="turn">The park turn counter (+0x1da70c).</param>
	// [BIN:TPW-EXE:0x4d3140 challenge lost] a lost challenge only counts a loss (slot +0x24, 0x4d1ae0, for which no reader is found): its completed byte (+0x2c) stays clear, so the offer scan 0x4d2a70 can offer it again, without limit; the next offer waits DaysAfterCompletedChallenge as after a win
	// [BIN:TPW-EXE:0x4d1e90 challenge update] nothing is offered, checked or timed out while an offer waits for its answer (+0x3da, set by 0x4d2a70 / 0x4d2b60), which only
	// the accept 0x4d2df0 (accepted +0x3d9, the deadline from mTargetTime) or the decline 0x4d2ec0 clears
	// [BIN:TPW-EXE:0x4d1f99 challenge update] a running challenge is checked on the park turns whose low two bits (+0x1da70c) are not 0, on the others the update only posts the progress message 0x4d3390 (tag 0x160) and returns; the check is made at once when the clock has not reached the deadline (0x4d1660 with the at-end flag clear, a pass wins) and, once it has, with the flag set (a pass wins, else the challenge is lost)
	// [BIN:TPW-EXE:0x4d1e90 challenge update] the idle system offers (0x4d2a70) on the first update at or after the next event time (+0x3d0), not at a day end
	// [BIN:TPW-EXE:0x4d21fd challenge time warning] on a checked turn that neither passed nor ended the challenge, a slot with a TargetTime (+4) posts the message tag 0x17f with advice 0x10f when (days left × 100) / TargetTime (days left: the deadline +0x3d0 minus the park time, divided by 8.64e11; signed 32-bit division) is below the word 0x7857d0, the settings key Challenges.ShortTimeLeftWarningAt (the 60-byte descriptor records at 0x743c70 list DaysAfterCompletedChallenge, DaysAfterDeclinedChallenge, DaysUntilFirstChallenge, DeclinesToForfeit, ShortTimeLeftWarningAt in the order of the words 0x7857c0..0x7857d0)
	// [BIN:TPW-EXE:0x59b060 challenge message receivers] a challenge message (category 25, vtable 0x006FD830) carries a text tag in +8 and an advice id in +0xc: the advisor queues the advice (0x59B540, 0x59B590, 0x59A940) unless it is 0x160, and the tag system (0x509BF0) shows the tag's text; the warning's advice 0x10f is message 271 of the advisor table (Challenge.Score, getter 0x5A0A30 = +0x62C), whose tag 0x17f has no text
	// [APPROX:ECON-058] the challenge system runs tp.exe's update on every park turn (an idle system offers on the first turn at or after its next event time, a running challenge is checked on the turns whose low two bits are not 0 and ends at its exact second, 0x4d1e90, 0x4d1f99), but the progress message that the other turns post (tag 0x4d3390 by type, advice 0x160, which the advisor skips) has no OpenTPW message: the tag system shows the tag's TAG_SYSTEM text with the slot's numbers in the message tube, replacing the offer's message in place, while OpenTPW's tube shows its own description of the offer (Describe) — evidence needed: the placeholder grammar of the TAG_SYSTEM entries (the substitution 0x509e10 from the message's fields) and the tube's in-place update (0x48eb20)
	public IEnumerable<(ParkEventKind Kind, int Index, long Amount, string Detail)> Update( long seconds, long turn, Func<ChallengeDefinition, long> measure, bool parkOpen = true, Func<ChallengeDefinition, int>? bestThing = null )
	{
		if ( Current == null )
		{
			// [BIN:TPW-EXE:0x4d1e90 challenge system start] while the switch (+0x3d8) is off, the system offers only with the park open (0x4c9050: the park-closed word 0x1da710 clear) and the park's age in days (0x4f8800 / 864e9) at DaysUntilFirstChallenge (0x7857c8), then sets the switch and offers at once; afterwards only the next event time (+0x3d0) counts
			if ( !SwitchedOn )
			{
				if ( !parkOpen || seconds < NextOfferSeconds )
					yield break;
				SwitchedOn = true;
			}
			if ( seconds >= NextOfferSeconds && settings.Challenges.Count > 0 && TryPickNext( out var index ) )
			{
				Current = new ActiveChallenge( index, seconds / ParkCalendar.SecondsPerDay, false, 0, 0, 0 );
				yield return (ParkEventKind.ChallengeOffered, index, settings.Challenges[index].Prize, Describe( settings.Challenges[index] ));
			}
			yield break;
		}
		if ( !Current.Accepted || (turn & 3) == 0 )
			yield break;
		var definition = settings.Challenges[Current.DefinitionIndex];
		long value;
		if ( IsDailyAverage( definition.Type ) )
			value = definition.TargetTime > 0 ? Current.Baseline / definition.TargetTime : 0;
		else if ( ActualThing != 0 && TakesObjectlessEvents( definition.Type ) )
			value = 0;
		else
			value = measure( definition ) - (IsCountSinceAcceptance( definition.Type ) ? Current.Baseline : 0);
		var reached = IsSupportedType( definition.Type ) && value >= Target( definition );
		// [BIN:TPW-EXE:0x4d1660 challenge check] types 14, 15 and 18 write the best object's thing id (0x4cc810, 0x4cc4b0) into the slot's +0x2e when they pass; 19 only compares and leaves the slot's id as the follow-up offer put it
		if ( reached && definition.Type is 14 or 15 or 18 )
			SetCarried( definition.Index, bestThing?.Invoke( definition ) ?? 0 );
		var ended = seconds >= Current.EndSeconds;
		// a challenge that is checked at its end only (+0x1c) passes only then (0x4d1660: the flag clear or the at-end parameter set)
		if ( reached && (!definition.CheckAtEndOnly || ended) )
		{
			foreach ( var result in Complete( seconds, definition ) )
				yield return result;
			yield break;
		}
		if ( ended )
		{
			Current = null;
			NextOfferSeconds = seconds + settings.ChallengeTiming.DaysAfterCompletedChallenge * ParkCalendar.SecondsPerDay;
			yield return (ParkEventKind.ChallengeFailed, definition.Index, value, Describe( definition ));
			yield break;
		}
		// the warning is posted on every checked turn while it holds; the advisor's queue admits one pending copy
		if ( IsTimeShort( definition, seconds ) )
			TimeShort?.Invoke( definition.Index );
	}

	/// <summary>The challenge system posted its short-time-left warning for the challenge with this definition index (the advisor's advice 271).</summary>
	public event Action<int>? TimeShort;

	/// <summary>
	/// Whether the running challenge's warning holds at <paramref name="seconds"/>: a TargetTime, and the whole days left times 100 over it below
	/// <c>Challenges.ShortTimeLeftWarningAt</c> (signed 32-bit division, as the original's).
	/// </summary>
	private bool IsTimeShort( ChallengeDefinition definition, long seconds )
	{
		if ( Current == null || definition.TargetTime == 0 )
			return false;
		var daysLeft = (int)Math.Max( 0, (Current.EndSeconds - seconds) / ParkCalendar.SecondsPerDay );
		return daysLeft * 100 / definition.TargetTime < settings.ChallengeTiming.ShortTimeLeftWarningAt;
	}

	/// <summary>
	/// A won challenge: the prize is paid only by a challenge without a follow-up type, so a chain pays the prize of its last part;
	/// a part whose follow-up type finds no follow-up pays nothing. The slot marked won is the one current after the follow-up offer:
	/// the follow-up when one was offered (the won part stays open and can be offered again), else the won challenge.
	/// </summary>
	// [BIN:TPW-EXE:0x4d2fb0 challenge won] FollowupType (+0x18) 0: the prize (+0x14) is credited (0x4d0190); otherwise the follow-up is offered at once (0x4d2b60: the first slot of that type not completed and below the forfeit count) and no prize is paid
	// [BIN:TPW-EXE:0x4d310d challenge won] the won mark 0x4d1a00 (mHaveWon +0x2c) goes to the slot at the current index (+0x3cc), read after 0x4d2b60 has moved it to the offered follow-up (0x4d2be6)
	private IEnumerable<(ParkEventKind, int, long, string)> Complete( long seconds, ChallengeDefinition definition )
	{
		ChallengeDefinition? followup = null;
		if ( definition.FollowupType != 0 )
		{
			// [BIN:TPW-EXE:0x4d2b60 follow-up offer] the slots are scanned from the first, in table order, for the follow-up type (+0x18) with mHaveWon (+0x2c) clear and
			// mChallengeDeclined (+0x28) below DeclinesToForfeit; only the type is compared, not the object (0x4d2bc0..0x4d2bd8)
			// [BIN:TPW-EXE:0x4d2bc0 follow-up offer] the follow-up scan compares only the type, so the carried object travels in the slot (+0x2e), not in the choice
			followup = settings.ChallengesInThisLevel.Select( index => settings.Challenges[index] )
				.FirstOrDefault( item => item.Type == definition.FollowupType && !finished.Contains( item.Index ) && !IsForfeited( item.Index ) );
		}
		// [BIN:TPW-EXE:0x4d2fb0 challenge won] a won challenge without a follow-up type clears the carried object (+0x3e0) before its prize; one with a follow-up type sets it from the slot's +0x2e
		// first, whether or not the offer finds a follow-up, and the offered follow-up's slot takes it over (0x4d30c2)
		ActualThing = definition.FollowupType == 0 ? 0 : carried.GetValueOrDefault( definition.Index );
		if ( followup != null )
			SetCarried( followup.Index, ActualThing );
		finished.Add( (followup ?? definition).Index );
		Current = null;
		NextOfferSeconds = seconds + settings.ChallengeTiming.DaysAfterCompletedChallenge * ParkCalendar.SecondsPerDay;
		yield return (ParkEventKind.ChallengeCompleted, definition.Index, definition.FollowupType == 0 ? definition.Prize : 0, Describe( definition ));
		if ( followup != null )
		{
			Current = new ActiveChallenge( followup.Index, seconds / ParkCalendar.SecondsPerDay, false, 0, 0, 0 );
			yield return (ParkEventKind.ChallengeOffered, followup.Index, followup.Prize, Describe( followup ));
		}
	}

	/// <summary>The slot's saved declined count has reached DeclinesToForfeit: only a loaded save can hold one (0x4d2a70, 0x4d2b60).</summary>
	private bool IsForfeited( int index ) => declines.GetValueOrDefault( index ) >= settings.ChallengeTiming.DeclinesToForfeit;

	private bool TryPickNext( out int index )
	{
		var list = settings.ChallengesInThisLevel;
		for ( var step = 0; step < list.Count; step++ )
		{
			var position = (NextListPosition + step) % list.Count;
			var candidate = list[position];
			if ( finished.Contains( candidate ) || !settings.Challenges[candidate].Independent || IsForfeited( candidate ) )
				continue;
			NextListPosition = (position + 1) % list.Count;
			index = candidate;
			return true;
		}
		index = 0;
		return false;
	}

	public void Accept( long day, long baseline ) => AcceptAt( day * ParkCalendar.SecondsPerDay, baseline );

	/// <summary>Accepts the offered challenge at park second <paramref name="seconds"/>: its deadline is TargetTime days on (0x004D2DF0).</summary>
	public void AcceptAt( long seconds, long baseline )
	{
		if ( Current == null || Current.Accepted )
			throw new InvalidOperationException( "No challenge is waiting to be accepted." );
		var definition = settings.Challenges[Current.DefinitionIndex];
		var deadline = seconds + definition.TargetTime * ParkCalendar.SecondsPerDay;
		Current = Current with { Accepted = true, StartDay = seconds / ParkCalendar.SecondsPerDay, DeadlineDay = deadline / ParkCalendar.SecondsPerDay, DeadlineSeconds = deadline,
			Baseline = IsDailyAverage( definition.Type ) ? 0 : baseline };
	}

	// [BIN:TPW-EXE:0x4d2ec0 challenge declined] the decline answers a waiting offer only: it clears the waiting and running flags, pays half the prize of a follow-up (ParkEconomy.DeclineChallenge) and sets the next event time from DaysAfterDeclinedChallenge; it never raises the slot's mChallengeDeclined (+0x28)
	// [BIN:TPW-EXE:0x4d2a70 challenge offer scan] a slot is offered only while that count is below Challenges.DeclinesToForfeit (0x7857cc, compared at 0x4d2ae5 and by the follow-up scan 0x4d2bcd). The only writers of +0x28 are the slot initialisers (0x4d0f7c, 0x4d1bf9) and the serializer (0x4d2320): the challenge system's only users outside its own module are the accept/decline handler 0x48e0f0 (0x4d2df0 / 0x4d2ec0) and the accumulator callers 0x4e16b0, 0x4e1920 and 0x501db0 (the callers of its getter 0x519610), so a declined challenge is offered again without limit and the count only matters when a save holds one
	public int Decline( long day ) => DeclineAt( day * ParkCalendar.SecondsPerDay );

	/// <summary>Declines the offered challenge at park second <paramref name="seconds"/>.</summary>
	public int DeclineAt( long seconds )
	{
		if ( Current == null || Current.Accepted )
			throw new InvalidOperationException( "No challenge is waiting to be declined." );
		var index = Current.DefinitionIndex;
		Current = null;
		// [BIN:TPW-EXE:0x4d2ec0 challenge declined] the decline clears the carried object (0x4d2f38)
		ActualThing = 0;
		NextOfferSeconds = seconds + settings.ChallengeTiming.DaysAfterDeclinedChallenge * ParkCalendar.SecondsPerDay;
		return index;
	}

	public string Describe( ChallengeDefinition definition )
	{
		var target = definition.TargetObject != 0 && catalog.TryGet( definition.TargetObject, out var info ) ? $" ({info.Name})" : "";
		return $"Challenge {definition.Index}: type {definition.Type}{target}, target {definition.TargetValue} in {definition.TargetTime} days, prize ${definition.Prize}"
			+ (IsSupportedType( definition.Type ) ? "" : " [progress not measurable yet]");
	}

	/// <summary>Golden ticket check (every 100 park turns in Full Simulation, <see cref="ParkEconomy.RunsGoldenTicketCheck"/>); returns newly won tickets. <paramref name="coveredCells"/> is the big park's cell count (<see cref="ParkEconomy.CoveredCells"/>); null without a cell map.</summary>
	public IReadOnlyList<GoldenTicketKind> CheckGoldenTickets( ParkCounters counters, IParkGuestStatistics guests, ParkResearch research, long profitThisYear, IReadOnlyList<long> monthlyAdmissions, long droppedAdmissions = 0, long parkSeconds = long.MaxValue, long? coveredCells = null, int? securityCoveragePercent = null )
	{
		var won = new List<GoldenTicketKind>();
		if ( !settings.CanEarnTickets )
			return won;
		var rules = settings.GoldenTickets;
		long MaxRecord( ParkRecordKind kind ) => counters.Values.Where( pair => pair.Key.StartsWith( $"record:{kind}:", StringComparison.Ordinal ) ).Select( pair => pair.Value ).DefaultIfEmpty( 0 ).Max();
		// [BIN:STP-PPC:0x100D33F8 local golden tickets] the profit ticket is won when the bank's year profit (+292, mProfitThisYear) is signed GREATER than ProfitYear, with no factor (oracle F23); the multiplier at 0x10013FDC belongs to the advisor's near-ticket hint, not to the award
		// [BIN:TPW-EXE:0x004C94D0 visitors since a month close] the lifetime visitor counter (+0x21C08, raised per admission by 0x004C7530) minus its snapshot at the close RecentVisitorMonths back (a 144-entry ring written at each month close, 0x004C7720); 0 unless that close exists, so the open month counts and RecentVisitorMonths + 1 closes are needed. The snapshot is the dropped history plus the closed months through it.
		var recentMonths = rules.RecentVisitorMonths;
		var recent = 0L;
		if ( recentMonths >= 0 && recentMonths < ParkLedger.MaximumHistoryMonths && recentMonths <= monthlyAdmissions.Count - 1 )
			recent = counters[ParkCounters.Admissions] - (droppedAdmissions + monthlyAdmissions.Take( monthlyAdmissions.Count - recentMonths ).Sum());
		// [BIN:TPW-EXE:0x004D4BC0 recent visitors ticket] recent > RecentVisitors and the park clock's age in exact 30-day months (0x004F8800 divided by 0x1792F8648000) > RecentVisitorMonths, both strict
		var ageMonths = parkSeconds / (30 * ParkCalendar.SecondsPerDay);
		var allBuilt = research.IsAllResearched && catalog.Objects.Where( info => info.IsBuyable ).All( info => counters[ParkCounters.Built( info.InfoId )] > 0 );
		void Check( GoldenTicketKind kind, bool condition )
		{
			if ( condition && tickets.Add( kind ) )
				won.Add( kind );
		}
		// [BIN:STP-PPC:0x100D3244 local golden tickets] every local ticket compares signed and strictly GREATER (cmpw, bc 4,1) with the word of GoldenTicketLocal, none is a >= (oracle F23)
		Check( GoldenTicketKind.Visitors, counters[ParkCounters.Admissions] > rules.Visitors );
		Check( GoldenTicketKind.PeopleInPark, guests.PeopleInPark > rules.PeopleInPark );
		// [BIN:STP-PPC:0x100D3308 happiness ticket] the average guest happiness (single) is greater than single(Happiness) and the guests in the park are more than AtLeastThisManyHappyPeople; nobody is filtered by happiness (oracle F23)
		Check( GoldenTicketKind.Happiness, guests.AverageHappiness > (float)rules.Happiness && guests.PeopleInPark > rules.AtLeastThisManyHappyPeople );
		Check( GoldenTicketKind.AllResearchedAndBuilt, allBuilt );
		Check( GoldenTicketKind.ProfitYear, profitThisYear > rules.ProfitYear );
		Check( GoldenTicketKind.RecentVisitors, recent > rules.RecentVisitors && ageMonths > recentMonths );
		Check( GoldenTicketKind.CoasterHeight, MaxRecord( ParkRecordKind.CoasterHeight ) > rules.CoasterHeight );
		Check( GoldenTicketKind.GokartExcitement, MaxRecord( ParkRecordKind.GokartExcitement ) > rules.GokartExcitement );
		Check( GoldenTicketKind.WaterLength, MaxRecord( ParkRecordKind.WaterLength ) > rules.WaterLength );
		// [BIN:TPW-EXE:0x004D4E50 global golden tickets] the four global tickets are tested in index order against the GoldenTicketGlobal words (0x7857AC, B0, B4, BC), each strictly greater: coaster height, go-kart excitement, water length and, index 3, the big park, whose count (0x004CD7F0) goes over all 128 x 128 cells of type 9, 10, 4 or 21 or under a structure of 0x005363F0 and is compared with GoldenTicketGlobal.MinCellsCovered (0x7857BC, the fifth key of the table 0x73FC70); MinCellsOwned (0x7857B8) is never read
		Check( GoldenTicketKind.BigPark, coveredCells > rules.MinCellsCovered );
		// [BIN:TPW-EXE:0x004D4A00 secret golden ticket] after the local and global checks, a park without the secret ticket (0x005AF800) whose camera coverage (0x004CD720) is exactly 100 awards it (0x005AFB00, 0x004D5870, index 0); no other caller of those two exists, so there is no all-land ticket
		Check( GoldenTicketKind.CamerasEverywhere, securityCoveragePercent == 100 );
		return won;
	}

	/// <summary>Moves the next offer day by <paramref name="days"/> (a new park started at a later day).</summary>

	internal void Restore( long nextOfferDay, int nextListPosition, ActiveChallenge? current, IReadOnlyDictionary<int, int> restoredDeclines, IEnumerable<int> restoredFinished, IEnumerable<GoldenTicketKind> restoredTickets, bool switchedOn = true,
		int actualThing = 0, IReadOnlyDictionary<int, int>? carriedThings = null, long? nextOfferSeconds = null )
	{
		SwitchedOn = switchedOn;
		RestoreCarried( actualThing, carriedThings );
		if ( current != null && !settings.Challenges.ContainsKey( current.DefinitionIndex ) )
			throw new InvalidDataException( $"Saved challenge {current.DefinitionIndex} is not defined in Challenges.sam." );
		NextOfferSeconds = nextOfferSeconds ?? nextOfferDay * ParkCalendar.SecondsPerDay;
		NextListPosition = nextListPosition;
		Current = current;
		declines.Clear();
		foreach ( var (key, value) in restoredDeclines )
			declines[key] = value;
		finished.Clear();
		finished.UnionWith( restoredFinished );
		tickets.Clear();
		tickets.UnionWith( restoredTickets );
	}
}

/// <summary>
/// Player progress across themes: golden tickets won per theme and golden keys. Themes open when
/// the player owns <c>Keys.CostToEnter</c> keys (per-theme <c>global.sam</c>); the theme order follows
/// <c>THEMENAMES.str</c> (jungle, hallow, fantasy, space — also ascending key cost 1, 1, 3, 5).
/// The Windows European English manual (printed p. 28) awards one key for every third earned ticket;
/// spending tickets on mystery items preserves earned keys. Pass cumulative earned counts to
/// <see cref="SetTickets"/>, not the available ticket balance.
/// A new Full Simulation player starts with one key, and entering a theme only compares the cost with the keys.
/// </summary>
public sealed class PlayerProgress
{
	// [DATA:theme-park-world_win_manual_europe_en_ii5.pdf:PDF-page-15/printed-page-28; SHA256=c96eb25f3dc13f7f0824bbf03f9bbeb3bb94e9f4756d4d8dfa09ac71732b0668]
	// [BIN:STP-PPC:0x10128B60 profile key count] keys = mExtraKeys + (earned global, per-theme and secret tickets) / 3, truncated; spent tickets are not subtracted
	public const int TicketsPerKey = 3;
	// [BIN:TPW-EXE:0x4a6a50 new player award] a player created through name entry gets mExtraKeys +1 (0x5afc30, its only caller 0x4a6b2e) unless GameType is 2 (Instant Action: message 394 only, no key)
	// [BIN:TPW-EXE:0x5e1cc0 theme entry] Instant Action skips the test; otherwise the theme is refused when its cost (0x5b11a0) is above the keys (0x5af680), and nothing is subtracted
	public const int StartingKeys = 1;
	// [DATA:Language/*/THEMENAMES.str:order; levels/*/global.sam:Keys.CostToEnter]
	public static readonly IReadOnlyList<string> ThemeOrder = new[] { "jungle", "hallow", "fantasy", "space" };
	private readonly Dictionary<string, int> ticketsByTheme = new( StringComparer.OrdinalIgnoreCase );

	public IReadOnlyDictionary<string, int> TicketsByTheme => ticketsByTheme;
	public int TotalTickets => ticketsByTheme.Values.Sum();
	public int Keys => StartingKeys + TotalTickets / TicketsPerKey;

	public void SetTickets( string theme, int count ) => ticketsByTheme[theme] = Math.Max( ticketsByTheme.GetValueOrDefault( theme ), count );

	public bool CanEnter( int keysToEnter ) => Keys >= keysToEnter;
}
