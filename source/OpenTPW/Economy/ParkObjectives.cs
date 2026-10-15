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
	public static string Built( int infoId ) => $"built:{infoId}";
	/// <summary>The park turn of the type's first purchase after turn 0 (park record +28 of the original); 0 while unset.</summary>
	public static string FirstPurchaseTurn( int infoId ) => $"firstturn:{infoId}";
	public static string Record( ParkRecordKind kind, int infoId ) => $"record:{kind}:{infoId}";
	public static string Researched( ResearchCategory category ) => $"researched:{category}";
	public const string Admissions = "admissions";
	public const string ShopProfit = "profit:shop";
	public const string SideshowProfit = "profit:sideshow";
}

/// <summary>State of an offered or accepted challenge.</summary>
public sealed record ActiveChallenge( int DefinitionIndex, long OfferedDay, bool Accepted, long StartDay, long DeadlineDay, long Baseline );

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
/// (docs/ECONOMY.md). <b>Approximations</b>: types 24, 25 and 27 compare the current value and some shop-sale
/// details differ (ECON-034); declines count towards a forfeit (ECON-035); a follow-up does not keep the won
/// part's object (ECON-055); types without a progress source (23, 26) never complete.
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
		NextOfferDay = settings.ChallengeTiming.DaysUntilFirstChallenge;
	}

	public long NextOfferDay { get; private set; }
	public int NextListPosition { get; private set; }
	public ActiveChallenge? Current { get; private set; }
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
		int AverageHappiness( StaffType? type )
		{
			var members = staff.Members.Where( member => type == null || member.Type == type ).ToList();
			return members.Count == 0 ? 0 : (int)members.Average( member => member.Happiness );
		}
		// [BIN:STP-PPC:0x100F41DC staff skill] skill = trunc(20 × (grade + training percentage / 100)) in single precision; grade 4 at 0 % is 80
		long SkillPercent( StaffType type )
		{
			var members = staff.OfType( type ).ToList();
			return members.Count == 0 ? 0 : (long)members.Average( member => ParkStaff.StaffSkill( member ) );
		}
		// [BIN:TPW-EXE:0x4d1660 challenge check] types 1-8, 11-13, 16, 17, 20 and 22 compare an accumulated value (the system's +0x3dc) with TargetVal; types 9 and 10 are 100 × the guests with a balloon or a costume (0x4c9530 / 0x4c95d0) / the census (0x4c9130)
		// [APPROX:ECON-034] the challenge types follow tp.exe's accumulator writers (0x4d27a0 adds to +0x3dc only for the accepted slot of that type and object): a paid shop sale adds 1 by ShopType and SpecialIngredient (0x4e16b0: ShopType 1 type 5; SpecialIngredient 1, 2, 3, 4 of ShopType 2 or 4 types 2, 1, 3, 4; ShopType 3 type 8, 5 type 7, 6 type 6), shops and sideshows add price minus cost of goods to 12 and 13 (0x4e16b0, 0x4e1920), an admission 1 to 11 (0x4c7520), a researched ride 1 to 16 (0x4d2860), a ride use 1 to 17, 20 and 22 (0x501db0), and 14, 18 and 19 read the coasters' counted sections; but OpenTPW counts every ShopType 4 sale as a drink (3) and the ingredient types in ShopType 2 only (the same on the shipped data), counts a sale of price 0, which tp.exe skips (0x4fe1a0), and compares the current value for 24, 25 and 27, where tp.exe adds the toilet cleanliness, the average staff happiness or that of the challenge's staff type (27; OpenTPW always reads the handymen) at every kind-11 message and compares the sum divided by TargetTime (0x4d2860, 0x4d1660) — evidence needed: the sender and rate of the kind-11 message (types 24, 25, 27)
		return definition.Type switch
		{
			1 => Sold( 2, 2 ),   // fries: food shop with salt
			2 => Sold( 2, 1 ),   // burgers: food shop with fat
			3 => Sold( 4 ),      // drinks
			4 => Sold( 2, 4 ),   // ice creams: food shop with sugar
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
			17 or 20 or 22 => counters[ParkCounters.RideUses( definition.TargetObject )],
			// [BIN:TPW-EXE:0x4d1873 challenge type 18] passes when TargetVal <= 0x4cc810(TargetObj): the unsigned maximum, over the rides of track type 3 (0x4e2b50) whose id is
			// TargetObj (any when 0), of the controller's counted sections (+0x108 via 0x43e0e0, zeroed and counted by the rating final 0x43df90), 0 without one; no accumulator, no baseline
			18 => coasterSections?.Invoke( definition.TargetObject ) ?? 0,
			19 => coasterSections?.Invoke( definition.TargetObject ) ?? 0,
			21 => Built( definition.TargetObject ) > 0 ? counters[ParkCounters.Record( ParkRecordKind.KartSections, definition.TargetObject )] : 0,
			24 => counters[ParkCounters.Record( ParkRecordKind.ToiletCleanlinessPercent, 0 )],
			25 => AverageHappiness( null ),
			27 => AverageHappiness( StaffType.Handyman ),
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

	/// <summary>Types measured as a change since acceptance rather than an absolute value.</summary>
	public static bool IsCountSinceAcceptance( int type ) => type is >= 1 and <= 8 or 11 or 12 or 13 or 16 or 17 or 20 or 22;

	/// <summary>Target value; the build types 30-33 measure 1 when built, and type 19 needs more than 2 counted sections whatever TargetVal.</summary>
	// [BIN:STP-PPC:0x100CE690 challenge check] a 34-entry handler table (0x1022C794) by type; types 30-33 test "built" (1), type 28 compares TargetVal with the level
	public static long Target( ChallengeDefinition definition ) => definition.Type switch { >= 30 and <= 33 => 1, 19 => 3, _ => definition.TargetValue };

	/// <summary>Daily challenge processing; returns the events to publish and the prize won (0 when none).</summary>
	// [BIN:TPW-EXE:0x4d3140 challenge lost] a lost challenge only counts a loss (slot +0x24, 0x4d1ae0, for which no reader is found): its completed byte (+0x2c) stays clear, so the offer scan 0x4d2a70 can offer it again, without limit; the next offer waits DaysAfterCompletedChallenge as after a win
	// [BIN:TPW-EXE:0x4d1e90 challenge day] nothing is offered, checked or timed out while an offer waits for its answer (+0x3da, set by 0x4d2a70 / 0x4d2b60), which only
	// the accept 0x4d2df0 (accepted +0x3d9, the deadline from mTargetTime) or the decline 0x4d2ec0 clears
	public IEnumerable<(ParkEventKind Kind, int Index, long Amount, string Detail)> AdvanceDay( long day, Func<ChallengeDefinition, long> measure )
	{
		if ( Current == null )
		{
			if ( day >= NextOfferDay && settings.Challenges.Count > 0 && TryPickNext( out var index ) )
			{
				Current = new ActiveChallenge( index, day, false, 0, 0, 0 );
				yield return (ParkEventKind.ChallengeOffered, index, settings.Challenges[index].Prize, Describe( settings.Challenges[index] ));
			}
			yield break;
		}
		if ( !Current.Accepted )
			yield break;
		var definition = settings.Challenges[Current.DefinitionIndex];
		var value = measure( definition ) - (IsCountSinceAcceptance( definition.Type ) ? Current.Baseline : 0);
		var reached = IsSupportedType( definition.Type ) && value >= Target( definition );
		if ( reached && !definition.CheckAtEndOnly )
		{
			foreach ( var result in Complete( day, definition ) )
				yield return result;
			yield break;
		}
		if ( day >= Current.DeadlineDay )
		{
			if ( reached )
			{
				foreach ( var result in Complete( day, definition ) )
					yield return result;
				yield break;
			}
			Current = null;
			NextOfferDay = day + settings.ChallengeTiming.DaysAfterCompletedChallenge;
			yield return (ParkEventKind.ChallengeFailed, definition.Index, value, Describe( definition ));
		}
	}

	/// <summary>
	/// A won challenge: the prize is paid only by a challenge without a follow-up type, so a chain pays the prize of its last part;
	/// a part whose follow-up type finds no follow-up pays nothing. The slot marked won is the one current after the follow-up offer:
	/// the follow-up when one was offered (the won part stays open and can be offered again), else the won challenge.
	/// </summary>
	// [BIN:TPW-EXE:0x4d2fb0 challenge won] FollowupType (+0x18) 0: the prize (+0x14) is credited (0x4d0190); otherwise the follow-up is offered at once (0x4d2b60: the first slot of that type not completed and below the forfeit count) and no prize is paid
	// [BIN:TPW-EXE:0x4d310d challenge won] the won mark 0x4d1a00 (mHaveWon +0x2c) goes to the slot at the current index (+0x3cc), read after 0x4d2b60 has moved it to the offered follow-up (0x4d2be6)
	private IEnumerable<(ParkEventKind, int, long, string)> Complete( long day, ChallengeDefinition definition )
	{
		ChallengeDefinition? followup = null;
		if ( definition.FollowupType != 0 )
		{
			// [BIN:TPW-EXE:0x4d2b60 follow-up offer] the slots are scanned from the first, in table order, for the follow-up type (+0x18) with mHaveWon (+0x2c) clear and
			// mChallengeDeclined (+0x28) below DeclinesToForfeit; only the type is compared, not the object (0x4d2bc0..0x4d2bd8)
			// [APPROX:ECON-055] the satisfying object a won challenge carries into its follow-up is not modelled: the checks of types 14, 15 and 18 store the best object's thing id in the slot (+0x2e, 0x4d1660), the win 0x4d2fb0 copies it to the challenge system's filter (+0x3e0) and into the follow-up slot, and while the filter is set the accumulator 0x4d27a0 adds only events of that one object (its parameter B, the object index: ride uses of types 17, 20 and 22, 0x501db0); OpenTPW counts the follow-up's uses of every object of its TargetObject type — evidence needed: none (read); OpenTPW keeps no per-object use counts
			followup = settings.ChallengesInThisLevel.Select( index => settings.Challenges[index] )
				.FirstOrDefault( item => item.Type == definition.FollowupType && !finished.Contains( item.Index ) );
		}
		finished.Add( (followup ?? definition).Index );
		Current = null;
		NextOfferDay = day + settings.ChallengeTiming.DaysAfterCompletedChallenge;
		yield return (ParkEventKind.ChallengeCompleted, definition.Index, definition.FollowupType == 0 ? definition.Prize : 0, Describe( definition ));
		if ( followup != null )
		{
			Current = new ActiveChallenge( followup.Index, day, false, 0, 0, 0 );
			yield return (ParkEventKind.ChallengeOffered, followup.Index, followup.Prize, Describe( followup ));
		}
	}

	private bool TryPickNext( out int index )
	{
		var list = settings.ChallengesInThisLevel;
		for ( var step = 0; step < list.Count; step++ )
		{
			var position = (NextListPosition + step) % list.Count;
			var candidate = list[position];
			if ( finished.Contains( candidate ) || !settings.Challenges[candidate].Independent )
				continue;
			NextListPosition = (position + 1) % list.Count;
			index = candidate;
			return true;
		}
		index = 0;
		return false;
	}

	public void Accept( long day, long baseline )
	{
		if ( Current == null || Current.Accepted )
			throw new InvalidOperationException( "No challenge is waiting to be accepted." );
		var definition = settings.Challenges[Current.DefinitionIndex];
		Current = Current with { Accepted = true, StartDay = day, DeadlineDay = day + definition.TargetTime, Baseline = baseline };
	}

	// [APPROX:ECON-035] a declined challenge counts towards DeclinesToForfeit and is finished when it reaches it; tp.exe's offer scans (0x4d2a70, 0x4d2b60) do skip a slot whose mChallengeDeclined (+0x28) is not below DeclinesToForfeit (0x7857cc), but its decline 0x4d2ec0 does not raise that count and no writer other than the slot initialisers (0x4d0f70, 0x4d1bd0) and the serializer was found, so in the original a declined challenge may be offered again without limit (offers waiting for an answer, the won mark and a declined follow-up's half prize are read and modelled: 0x4d1e90, 0x4d2df0, 0x4d2ec0, 0x4d2fb0) — evidence needed: a writer of the slot's mChallengeDeclined (+0x28) outside the challenge module 0x4d0000-0x4d6000
	public int Decline( long day )
	{
		if ( Current == null || Current.Accepted )
			throw new InvalidOperationException( "No challenge is waiting to be declined." );
		var index = Current.DefinitionIndex;
		declines[index] = declines.GetValueOrDefault( index ) + 1;
		if ( declines[index] >= settings.ChallengeTiming.DeclinesToForfeit )
			finished.Add( index );
		Current = null;
		NextOfferDay = day + settings.ChallengeTiming.DaysAfterDeclinedChallenge;
		return index;
	}

	public string Describe( ChallengeDefinition definition )
	{
		var target = definition.TargetObject != 0 && catalog.TryGet( definition.TargetObject, out var info ) ? $" ({info.Name})" : "";
		return $"Challenge {definition.Index}: type {definition.Type}{target}, target {definition.TargetValue} in {definition.TargetTime} days, prize ${definition.Prize}"
			+ (IsSupportedType( definition.Type ) ? "" : " [progress not measurable yet]");
	}

	/// <summary>Golden ticket check (every 100 park turns in Full Simulation, <see cref="ParkEconomy.RunsGoldenTicketCheck"/>); returns newly won tickets.</summary>
	public IReadOnlyList<GoldenTicketKind> CheckGoldenTickets( ParkCounters counters, IParkGuestStatistics guests, ParkResearch research, long profitThisYear, IReadOnlyList<long> monthlyAdmissions, long droppedAdmissions = 0, long parkSeconds = long.MaxValue )
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
		// [APPROX:ECON-038] the big park and camera tickets compare OpenTPW records (CellsOwned with MinCellsOwned, CellsCoveredByCameras >= MinCellsCovered) that no game code reports yet, and OpenTPW also awards an all-land ticket; tp.exe's big park (global index 3, 0x4d4e50) needs more than GoldenTicketGlobal.MinCellsCovered (0x7857bc, the fifth global key of the table 0x73fc70; MinCellsOwned, 0x7857b8, is never read) cells of type 4, 9, 10 or 21 or under an object (0x4cd7f0), its camera ticket is the secret one (0x4d4a00) won only when exactly 100 % of the cells not of type 2, 7 or 30 have a nonzero coverage short (+6 of the 10-byte cell array, 0x4cd720), and it never awards an all-land ticket (0x5afb00 and 0x4d5870 have the single caller 0x4d4a00, with index 0) — evidence needed: the writer of the cell coverage short (+6) and the original cell types in OpenTPW's cell map (ECON-050)
		Check( GoldenTicketKind.BigPark, MaxRecord( ParkRecordKind.CellsOwned ) > rules.MinCellsOwned );
		Check( GoldenTicketKind.CamerasEverywhere, MaxRecord( ParkRecordKind.CellsCoveredByCameras ) >= rules.MinCellsCovered );
		Check( GoldenTicketKind.AllLandOwned, MaxRecord( ParkRecordKind.OwnsAllLand ) > 0 );
		return won;
	}

	/// <summary>Moves the next offer day by <paramref name="days"/> (a new park started at a later day).</summary>

	internal void Restore( long nextOfferDay, int nextListPosition, ActiveChallenge? current, IReadOnlyDictionary<int, int> restoredDeclines, IEnumerable<int> restoredFinished, IEnumerable<GoldenTicketKind> restoredTickets )
	{
		if ( current != null && !settings.Challenges.ContainsKey( current.DefinitionIndex ) )
			throw new InvalidDataException( $"Saved challenge {current.DefinitionIndex} is not defined in Challenges.sam." );
		NextOfferDay = nextOfferDay;
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
