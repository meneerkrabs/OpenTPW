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
/// (docs/ECONOMY.md). <b>Approximations</b>: the meaning of each challenge type is taken from the
/// <c>Challenges.sam</c> comments; offers wait for an explicit accept/decline; follow-ups are offered
/// immediately after completion; types without a progress source never complete.
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
	public static bool IsSupportedType( int type ) => type is >= 1 and <= 13 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 24 or 25 or 27 or 28 or 29 or 30 or 31 or 33;

	/// <summary>The progress value of a challenge type, absolute; count-like types are compared with the acceptance baseline.</summary>
	public long Measure( ChallengeDefinition definition, ParkCounters counters, ParkStaff staff, IReadOnlyCollection<ParkObjectState> objects, IParkGuestStatistics guests )
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
		// [APPROX:ECON-034] challenge type meanings come from Challenges.sam comments (shop types by ShopType/SpecialIngredient) — evidence needed: challenge captures per type
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
			15 => counters[ParkCounters.Record( ParkRecordKind.KartCrossroads, definition.TargetObject )],
			16 => counters[ParkCounters.Researched( ResearchCategory.Ride )],
			17 or 20 => counters[ParkCounters.RideUses( definition.TargetObject )],
			18 or 30 => Built( definition.TargetObject ),
			19 => counters[ParkCounters.Record( ParkRecordKind.CoasterLoops, definition.TargetObject )],
			21 => Built( definition.TargetObject ) > 0 ? counters[ParkCounters.Record( ParkRecordKind.KartSections, definition.TargetObject )] : 0,
			24 => counters[ParkCounters.Record( ParkRecordKind.ToiletCleanlinessPercent, 0 )],
			25 => AverageHappiness( null ),
			27 => AverageHappiness( StaffType.Handyman ),
			28 => objects.Where( item => item.InfoId == definition.TargetObject ).Select( item => item.Level + 1 ).DefaultIfEmpty( 0 ).Max(),
			29 => SkillPercent( (StaffType)Math.Clamp( definition.TargetStaffType, 0, 4 ) ),
			31 or 33 => Built( definition.TargetObject2 != 0 ? definition.TargetObject2 : definition.TargetObject ),
			_ => 0
		};
	}

	/// <summary>Types measured as a change since acceptance rather than an absolute value.</summary>
	public static bool IsCountSinceAcceptance( int type ) => type is >= 1 and <= 8 or 11 or 12 or 13 or 16 or 17 or 18 or 20 or 30 or 31 or 33;

	/// <summary>Target value; build/research-and-build challenges with TargetVal 0 need one item.</summary>
	// [APPROX:ECON-036] build challenges with TargetVal 0 need one item; type 28 needs level 3 — evidence needed: challenge captures
	public static long Target( ChallengeDefinition definition ) => definition.Type is 18 or 30 or 31 or 33 ? Math.Max( 1, definition.TargetValue ) : definition.Type == 28 ? 3 : definition.TargetValue;

	/// <summary>Daily challenge processing; returns the events to publish and the prize won (0 when none).</summary>
	// [APPROX:ECON-035] offers wait for accept/decline; follow-ups are offered right after completion; failed challenges count as finished — evidence needed: challenge flow captures
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
			finished.Add( Current.DefinitionIndex );
			Current = null;
			NextOfferDay = day + settings.ChallengeTiming.DaysAfterCompletedChallenge;
			yield return (ParkEventKind.ChallengeFailed, definition.Index, value, Describe( definition ));
		}
	}

	private IEnumerable<(ParkEventKind, int, long, string)> Complete( long day, ChallengeDefinition definition )
	{
		finished.Add( definition.Index );
		Current = null;
		NextOfferDay = day + settings.ChallengeTiming.DaysAfterCompletedChallenge;
		yield return (ParkEventKind.ChallengeCompleted, definition.Index, definition.Prize, Describe( definition ));
		if ( definition.FollowupType != 0 )
		{
			var followup = settings.ChallengesInThisLevel.Select( index => settings.Challenges[index] )
				.FirstOrDefault( item => item.Type == definition.FollowupType && item.TargetObject == definition.TargetObject && !finished.Contains( item.Index ) );
			if ( followup != null )
			{
				Current = new ActiveChallenge( followup.Index, day, false, 0, 0, 0 );
				yield return (ParkEventKind.ChallengeOffered, followup.Index, followup.Prize, Describe( followup ));
			}
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

	/// <summary>Monthly golden ticket check; returns newly won tickets.</summary>
	public IReadOnlyList<GoldenTicketKind> CheckGoldenTickets( ParkCounters counters, IParkGuestStatistics guests, ParkResearch research, IReadOnlyList<LedgerMonth> history, IReadOnlyList<long> monthlyAdmissions )
	{
		var won = new List<GoldenTicketKind>();
		if ( !settings.CanEarnTickets )
			return won;
		var rules = settings.GoldenTickets;
		long MaxRecord( ParkRecordKind kind ) => counters.Values.Where( pair => pair.Key.StartsWith( $"record:{kind}:", StringComparison.Ordinal ) ).Select( pair => pair.Value ).DefaultIfEmpty( 0 ).Max();
		// [APPROX:ECON-039] profit year = profit of the last 12 closed months — evidence needed: golden ticket award capture
		var lastYear = history.Count >= ParkCalendar.MonthsPerYear ? history.Skip( history.Count - ParkCalendar.MonthsPerYear ).Sum( month => month.Profit ) : long.MinValue;
		var recentMonths = Math.Max( 1, rules.RecentVisitorMonths );
		var recent = monthlyAdmissions.Count >= recentMonths ? monthlyAdmissions.Skip( monthlyAdmissions.Count - recentMonths ).Sum() : 0;
		var allBuilt = research.IsAllResearched && catalog.Objects.Where( info => info.IsBuyable ).All( info => counters[ParkCounters.Built( info.InfoId )] > 0 );
		void Check( GoldenTicketKind kind, bool condition )
		{
			if ( condition && tickets.Add( kind ) )
				won.Add( kind );
		}
		Check( GoldenTicketKind.Visitors, counters[ParkCounters.Admissions] >= rules.Visitors );
		Check( GoldenTicketKind.PeopleInPark, guests.PeopleInPark >= rules.PeopleInPark );
		Check( GoldenTicketKind.Happiness, guests.PeopleInPark > 0 && guests.AverageHappiness >= rules.Happiness && guests.CountHappierThan( rules.Happiness ) >= rules.AtLeastThisManyHappyPeople );
		Check( GoldenTicketKind.AllResearchedAndBuilt, allBuilt );
		Check( GoldenTicketKind.ProfitYear, lastYear >= rules.ProfitYear );
		Check( GoldenTicketKind.RecentVisitors, recent >= rules.RecentVisitors );
		Check( GoldenTicketKind.CoasterHeight, MaxRecord( ParkRecordKind.CoasterHeight ) >= rules.CoasterHeight );
		Check( GoldenTicketKind.GokartExcitement, MaxRecord( ParkRecordKind.GokartExcitement ) >= rules.GokartExcitement );
		Check( GoldenTicketKind.WaterLength, MaxRecord( ParkRecordKind.WaterLength ) >= rules.WaterLength );
		// [APPROX:ECON-038] big park uses MinCellsOwned, cameras use MinCellsCovered — evidence needed: golden ticket award captures
		Check( GoldenTicketKind.BigPark, MaxRecord( ParkRecordKind.CellsOwned ) >= rules.MinCellsOwned );
		Check( GoldenTicketKind.CamerasEverywhere, MaxRecord( ParkRecordKind.CellsCoveredByCameras ) >= rules.MinCellsCovered );
		Check( GoldenTicketKind.AllLandOwned, MaxRecord( ParkRecordKind.OwnsAllLand ) > 0 );
		return won;
	}

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
/// <b>Approximation</b>: OpenTPW starts with one key; keys are not consumed by entering.
/// </summary>
public sealed class PlayerProgress
{
	// [DATA:theme-park-world_win_manual_europe_en_ii5.pdf:PDF-page-15/printed-page-28; SHA256=c96eb25f3dc13f7f0824bbf03f9bbeb3bb94e9f4756d4d8dfa09ac71732b0668]
	// [BIN:STP-PPC:0x10128B60 profile key count] keys = mExtraKeys + (earned global, per-theme and secret tickets) / 3, truncated; spent tickets are not subtracted
	public const int TicketsPerKey = 3;
	// [APPROX:ECON-040] players start with 1 golden key and keys are not consumed by entering themes — evidence needed: initial lobby and repeated theme-entry captures
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
