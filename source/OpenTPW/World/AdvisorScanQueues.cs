using OpenTPW.Hud;

namespace OpenTPW;

/// <summary>The five groups of the advisor's shop statistics (tp.exe <c>0x4CAAE0</c>-<c>0x4CC090</c>): three stock types, the other shops and the sideshows.</summary>
internal enum AdvisorShopGroup
{
	/// <summary>The shops of <c>UsageInfo.ShopType</c> 2.</summary>
	Food,
	/// <summary>The shops of <c>UsageInfo.ShopType</c> 1, 5 and 6 (the retail shops).</summary>
	Shops,
	/// <summary>The shops of <c>UsageInfo.ShopType</c> 3.</summary>
	Restaurant,
	/// <summary>The shops of <c>UsageInfo.ShopType</c> 4.</summary>
	Drinks,
	/// <summary>The attractions of <c>WhichUIType</c> 2.</summary>
	Sideshows,
}

/// <summary>The shop one of the statistics picked: its handle (the callback keeps it in the descriptor's word <c>+4</c>) and the figure it was picked by (<c>+8</c>).</summary>
internal readonly record struct AdvisorShopPick( int Handle, int Value );

/// <summary>
/// The park statistics the advisor's price, shop value and research callbacks read, as the original's helpers return them. Like <see cref="IAdvisorParkState"/> every member is a pure read of
/// the park. <see cref="AdvisorScanContext.Park"/> supplies it when the park state also implements this interface (<see cref="EconomyParkState"/> does); a callback finds none and scores 0 otherwise.
/// </summary>
internal interface IAdvisorQueuesState
{
	/// <summary>Whether a perception ring holds its 50 values (<c>0x004C9920</c>).</summary>
	bool PriceRingHoldsFifty( int category );
	/// <summary>The mean of a perception ring's held values, truncated to the byte the callbacks compare (<c>0x004C9830</c>; 0 for an empty ring).</summary>
	int PriceRingMean( int category );
	/// <summary>The shop of the group with the lowest value among those older than 30 days with customers, or null (<c>0x4CADF0</c>, <c>0x4CB640</c> and <c>0x4CC090</c>).</summary>
	AdvisorShopPick? WorstValue( AdvisorShopGroup group );
	/// <summary>The shop of the group with the highest value (<c>0x4CAAE0</c>, <c>0x4CB300</c>, <c>0x4CBBA0</c>).</summary>
	AdvisorShopPick? BestValue( AdvisorShopGroup group );
	/// <summary>The shop of a group other than the sideshows with the lowest satisfaction among those older than 30 days (<c>0x4CB200</c> and <c>0x4CBA90</c>; the sideshows have no such helper).</summary>
	AdvisorShopPick? WorstSatisfaction( AdvisorShopGroup group );
	/// <summary>The shop of the group with the highest satisfaction (<c>0x4CB100</c>, <c>0x4CB980</c>, <c>0x4CBEC0</c>).</summary>
	AdvisorShopPick? BestSatisfaction( AdvisorShopGroup group );
	/// <summary>The research projects of a category that are not finished (<c>0x004CA630</c>-<c>0x004CA690</c> through <c>0x00503B30</c>).</summary>
	int ResearchOutstanding( ResearchCategory category );
	/// <summary>The research rate of a category (<c>0x00503980</c>): its effort share times the researchers' staffing percentage, 0 without effort, researchers or a researcher cap.</summary>
	int ResearchRate( ResearchCategory category );
	/// <summary>The researchers waiting in the hiring pool (<c>0x004C9E40</c> with 4: the valid candidates of the pool's type 4).</summary>
	int ResearcherCandidates { get; }
	/// <summary>The research ability of the hired researchers (<c>0x004C9E60</c>: the table <c>0x78545C</c> summed over the researcher list by grade).</summary>
	int ResearchDone { get; }
	/// <summary>The research ability a full staff would have (<c>0x004C9ED0</c>: <c>MaxResearchersInPark</c> times the best grade's ability).</summary>
	int ResearchCapacity { get; }
	/// <summary>The ride types whose researched upgrade level is above 0 (<c>0x004CA4C0</c>).</summary>
	int ResearchedUpgradeTypes { get; }
}

/// <summary>
/// The advisor's price, shop value and research callbacks, ported from <c>tp.exe</c> (messages 15-17, 61, 63, 64, 72, 76, 80, 91, 92 and 238-257), by the address of the original's callback. Each reads the park
/// through <see cref="IAdvisorParkState"/> and <see cref="IAdvisorQueuesState"/>, and its thresholds through <see cref="AdvisorScanContext.Parameter"/>; a threshold the settings file lacks leaves its callback at 0.
/// </summary>
internal static class AdvisorQueuesCallbacks
{
	private static readonly string[] ShopPrefixes = { "Food", "Shops", "Restaurant", "Drinks", "Sideshows" };

	/// <summary>The integer thresholds and multipliers these callbacks read (words of the <c>Advisor.sam</c> object).</summary>
	public static readonly IReadOnlyList<string> IntegerKeys = BuildIntegerKeys();

	private static IReadOnlyList<string> BuildIntegerKeys()
	{
		var keys = new List<string>
		{
			"StaffHireResearchers1.Most", "StaffHireResearchers1.PointsPerExtraPct", "StaffHireResearchers3.MonthsIntoGameForConsideration", "StaffHireResearchers3.SlowerThan",
			"RidesNoUpgrades.MonthsIntoGameForConsideration", "RidesMoreUpgrades.LessAvailableThan",
			"PricingTicketTooExpensive.TicketSatisfactionUnder", "PricingTicketTooCheap.TicketSatisfactionOver",
		};
		foreach ( var prefix in ShopPrefixes )
		{
			keys.Add( $"{prefix}PoorValue.ValueWorseThan" );
			keys.Add( $"{prefix}PoorValue.ScorePerPoint" );
			keys.Add( $"{prefix}PoorSatisfaction.SatisfactionWorseThan" );
			keys.Add( $"{prefix}PoorSatisfaction.ScorePerPoint" );
			keys.Add( $"{prefix}GreatValue.ValueBetterThan" );
			keys.Add( $"{prefix}GreatValue.ScorePerPoint" );
			keys.Add( $"{prefix}GreatSatisfaction.SatisfactionBetterThan" );
			keys.Add( $"{prefix}GreatSatisfaction.ScorePerPoint" );
		}
		return keys;
	}

	public const uint HireResearchersMany = 0x59CEA0;
	public const uint HireResearchersAll = 0x59CF50;
	public const uint HireResearchersSlow = 0x59CFE0;
	public const uint RidesResearchMore = 0x59DA30;
	public const uint RidesNoUpgrades = 0x59DB40;
	public const uint RidesMoreUpgrades = 0x59DBF0;
	public const uint ShopsResearchMore = 0x59DD40;
	public const uint SideshowsResearchMore = 0x59DFD0;
	public const uint FeaturesResearchMore = 0x59E260;
	public const uint TicketTooExpensive = 0x59E730;
	public const uint TicketTooCheap = 0x59E790;

	/// <summary>The shop value and satisfaction callbacks: the poor value ones (<c>0x59FB60</c>-<c>0x59FD20</c>) from food, shops, restaurant, drinks and sideshows, then the poor satisfaction ones, the great value ones and the great satisfaction ones in the same order.</summary>
	public static readonly IReadOnlyList<uint> PoorValue = new uint[] { 0x59FB60, 0x59FBD0, 0x59FC40, 0x59FCB0, 0x59FD20 };
	public static readonly IReadOnlyList<uint> PoorSatisfaction = new uint[] { 0x59FD90, 0x59FE00, 0x59FE70, 0x59FEE0, 0x59FF50 };
	public static readonly IReadOnlyList<uint> GreatValue = new uint[] { 0x59FFC0, 0x5A0010, 0x5A0060, 0x5A00B0, 0x5A0100 };
	public static readonly IReadOnlyList<uint> GreatSatisfaction = new uint[] { 0x5A0150, 0x5A01A0, 0x5A01F0, 0x5A0240, 0x5A0290 };

	/// <summary>The callbacks by the address of the original's callback (the descriptor's word +0x10).</summary>
	public static readonly IReadOnlyDictionary<uint, AdvisorScoreCallback> Callbacks = BuildCallbacks();

	private static IReadOnlyDictionary<uint, AdvisorScoreCallback> BuildCallbacks()
	{
		var callbacks = new Dictionary<uint, AdvisorScoreCallback>
		{
			[HireResearchersMany] = HireResearchersManyScore,
			[HireResearchersAll] = HireResearchersAllScore,
			[HireResearchersSlow] = HireResearchersSlowScore,
			[RidesResearchMore] = context => ResearchMore( context, AdvisorKinds.Rides, ResearchCategory.Ride, rateTested: true, "RidesResearchMore.Score" ),
			[RidesNoUpgrades] = RidesNoUpgradesScore,
			[RidesMoreUpgrades] = RidesMoreUpgradesScore,
			[ShopsResearchMore] = context => ResearchMore( context, AdvisorKinds.Shops, ResearchCategory.Shop, rateTested: true, "ShopsResearchMore.Score" ),
			[SideshowsResearchMore] = context => ResearchMore( context, AdvisorKinds.Sideshows, ResearchCategory.Sideshow, rateTested: true, "SideshowsResearchMore.Score" ),
			// [BIN:TPW-EXE:0x0059E260 features research] the only one of the four that does not look at the research rate
			[FeaturesResearchMore] = context => ResearchMore( context, AdvisorKinds.Features, ResearchCategory.Feature, rateTested: false, "FeaturesResearchMore.Score" ),
			[TicketTooExpensive] = context => TicketOpinion( context, above: false, "PricingTicketTooExpensive.TicketSatisfactionUnder", "PricingTicketTooExpensive.Score" ),
			[TicketTooCheap] = context => TicketOpinion( context, above: true, "PricingTicketTooCheap.TicketSatisfactionOver", "PricingTicketTooCheap.Score" ),
		};
		for ( var index = 0; index < ShopPrefixes.Length; index++ )
		{
			var group = (AdvisorShopGroup)index;
			var prefix = ShopPrefixes[index];
			// [BIN:TPW-EXE:0x0059FB60 poor value] 0x59FB60-0x59FD20 (food, shops, restaurant, drinks, sideshows)
			callbacks[PoorValue[index]] = context => Shop( context, group, worst: true, $"{prefix}PoorValue.ValueWorseThan", $"{prefix}PoorValue.ScorePerPoint",
				queues => queues.WorstValue( group ) );
			// [BIN:TPW-EXE:0x0059FD90 poor satisfaction] 0x59FD90-0x59FF50; the sideshows' (0x59FF50) calls the sideshow value helper 0x4CC090 where the other four call a satisfaction helper
			callbacks[PoorSatisfaction[index]] = context => Shop( context, group, worst: true, $"{prefix}PoorSatisfaction.SatisfactionWorseThan", $"{prefix}PoorSatisfaction.ScorePerPoint",
				queues => group == AdvisorShopGroup.Sideshows ? queues.WorstValue( group ) : queues.WorstSatisfaction( group ) );
			// [BIN:TPW-EXE:0x0059FFC0 great value] 0x59FFC0-0x5A0100
			callbacks[GreatValue[index]] = context => Shop( context, group, worst: false, $"{prefix}GreatValue.ValueBetterThan", $"{prefix}GreatValue.ScorePerPoint",
				queues => queues.BestValue( group ) );
			// [BIN:TPW-EXE:0x005A0150 great satisfaction] 0x5A0150-0x5A0290
			callbacks[GreatSatisfaction[index]] = context => Shop( context, group, worst: false, $"{prefix}GreatSatisfaction.SatisfactionBetterThan", $"{prefix}GreatSatisfaction.ScorePerPoint",
				queues => queues.BestSatisfaction( group ) );
		}
		return callbacks;
	}

	/// <summary>The kinds of <see cref="IAdvisorParkState.BuiltTypes"/> and <see cref="IAdvisorParkState.AvailableTypes"/> (the object type records' <c>WhichUIType</c>).</summary>
	private static class AdvisorKinds
	{
		public const int Rides = 0;
		public const int Shops = 1;
		public const int Sideshows = 2;
		public const int Features = 3;
	}

	/// <summary>Reads the park, its queues view and thresholds as the words they are; false when one is missing.</summary>
	private static bool Read( AdvisorScanContext context, out IAdvisorParkState park, out IAdvisorQueuesState queues, out int[] values, params string[] keys )
	{
		values = new int[keys.Length];
		park = context.Park!;
		queues = (context.Park as IAdvisorQueuesState)!;
		if ( context.Park is not IAdvisorQueuesState )
			return false;
		for ( var index = 0; index < keys.Length; index++ )
		{
			if ( context.Parameter( keys[index] ) is not { } value )
				return false;
			values[index] = (int)value;
		}
		return true;
	}

	// ------------------------------------------------------------------ the researchers

	// [BIN:TPW-EXE:0x0059CEA0 hire researchers, few types built] the park open, no researcher (0x4C9960 of 8 above 0 gives 0), ride research outstanding (0x4CA630), ride types available (0x4CA450 of 0) above 0 and not all built (0x4CA7B0 of 0, 0): built x 100 / available (a signed divide) above the word at +0x114 gives the excess times the word at +0x118
	private static int HireResearchersManyScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var queues, out var v, "StaffHireResearchers1.Most", "StaffHireResearchers1.PointsPerExtraPct" ) )
			return 0;
		if ( !park.ParkOpen || park.StaffCount( StaffType.Researcher ) > 0 || queues.ResearchOutstanding( ResearchCategory.Ride ) == 0 )
			return 0;
		var available = park.AvailableTypes( AdvisorKinds.Rides, withTicketItems: false );
		if ( available == 0 )
			return 0;
		var built = park.BuiltTypes( AdvisorKinds.Rides, withTicketItems: false );
		if ( built == available )
			return 0;
		var percent = unchecked( built * 100 ) / available;
		return percent > v[0] ? unchecked( (percent - v[0]) * v[1] ) : 0;
	}

	// [BIN:TPW-EXE:0x0059CF50 hire researchers, every type built] the same three tests, then the built ride types (0x4CA7B0) equal to the available ones (0x4CA450), with no test for 0 of 0: the flat word at +0x11C
	private static int HireResearchersAllScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var queues, out _, Array.Empty<string>() ) )
			return 0;
		if ( !park.ParkOpen || park.StaffCount( StaffType.Researcher ) > 0 || queues.ResearchOutstanding( ResearchCategory.Ride ) == 0 )
			return 0;
		return park.BuiltTypes( AdvisorKinds.Rides, withTicketItems: false ) == park.AvailableTypes( AdvisorKinds.Rides, withTicketItems: false ) ? context.Score( "StaffHireResearchers2.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059CFE0 hire researchers, slow research] the park open and a research total (0x4C9ED0) above 0; the park clock's months (0x4F8800 / 30 days, signed) at least the word at +0x120; a researcher hired (0x4C9960 of 8) and a researcher in the hiring pool (0x4C9E40 of 4); the research done (0x4C9E60) x 100 / the total (unsigned) below the word at +0x124: the flat word at +0x128
	private static int HireResearchersSlowScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var queues, out var v, "StaffHireResearchers3.MonthsIntoGameForConsideration", "StaffHireResearchers3.SlowerThan" ) || context.Money is not { } money )
			return 0;
		var total = queues.ResearchCapacity;
		if ( !park.ParkOpen || total == 0 || money.ParkMonths < v[0] || park.StaffCount( StaffType.Researcher ) == 0 || queues.ResearcherCandidates == 0 )
			return 0;
		return unchecked( (uint)queues.ResearchDone * 100u ) / (uint)total < (uint)v[1] ? context.Score( "StaffHireResearchers3.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059DA30 research more] 0x59DA30 (rides), 0x59DD40 (shops), 0x59DFD0 (sideshows) and 0x59E260 (features): a researcher hired (0x4C9960 of 8), a type of the kind built (0x4CA7B0 of the kind, 0), research of the category outstanding (0x4CA630-0x4CA690) and, but for the features, a research rate (0x503980 of the category) of 0, then the kind's built types equal to its available ones (0x4CA450, 0x4CA510, 0x4CA570, 0x4CA5D0; only the rides' skip the golden ticket types): the flat score. None tests the park open
	private static int ResearchMore( AdvisorScanContext context, int kind, ResearchCategory category, bool rateTested, string scoreKey )
	{
		if ( !Read( context, out var park, out var queues, out _, Array.Empty<string>() ) )
			return 0;
		if ( park.StaffCount( StaffType.Researcher ) == 0 )
			return 0;
		var built = park.BuiltTypes( kind, withTicketItems: false );
		if ( built == 0 || queues.ResearchOutstanding( category ) == 0 || rateTested && queues.ResearchRate( category ) != 0 )
			return 0;
		return built == park.AvailableTypes( kind, withTicketItems: kind != AdvisorKinds.Rides ) ? context.Score( scoreKey ) : 0;
	}

	// [BIN:TPW-EXE:0x0059DB40 no upgrades] the park clock's months (signed) at least the word at +0x238, a researcher hired, no ride type with a researched upgrade (0x4CA4C0 of 0 above 0 gives 0) and an upgrade research rate (0x503980 of 4) of 0: the flat word at +0x23C
	private static int RidesNoUpgradesScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var queues, out var v, "RidesNoUpgrades.MonthsIntoGameForConsideration" ) || context.Money is not { } money )
			return 0;
		if ( money.ParkMonths < v[0] || park.StaffCount( StaffType.Researcher ) == 0 || queues.ResearchedUpgradeTypes > 0 || queues.ResearchRate( ResearchCategory.Upgrade ) != 0 )
			return 0;
		return context.Score( "RidesNoUpgrades.Score" );
	}

	// [BIN:TPW-EXE:0x0059DBF0 more upgrades] a researcher hired, the ride types with a researched upgrade (0x4CA4C0) above 0 and not above the word at +0x240 (signed), an upgrade research rate of 0: the flat word at +0x244
	private static int RidesMoreUpgradesScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var queues, out var v, "RidesMoreUpgrades.LessAvailableThan" ) )
			return 0;
		var upgraded = queues.ResearchedUpgradeTypes;
		if ( park.StaffCount( StaffType.Researcher ) == 0 || upgraded == 0 || upgraded > v[0] || queues.ResearchRate( ResearchCategory.Upgrade ) != 0 )
			return 0;
		return context.Score( "RidesMoreUpgrades.Score" );
	}

	// ------------------------------------------------------------------ the ticket price

	/// <summary>The perception ring the booth's opinion of the entrance fee goes to (<c>0x004FF5B0</c> records through <c>0x004C74B0</c> with category 0).</summary>
	public const int TicketCategory = 0;

	// [BIN:TPW-EXE:0x0059E730 ticket too expensive] the park's ring 0 holds its 50 values (0x4C9920 of 0), its mean (0x4C9830 of 0, a byte compared as a word) is below the word at +0x2F0: the flat word at +0x2F4; 0x0059E790 is the same for a mean above the word at +0x2F8 and the word at +0x2FC
	private static int TicketOpinion( AdvisorScanContext context, bool above, string thresholdKey, string scoreKey )
	{
		if ( !Read( context, out _, out var queues, out var v, thresholdKey ) || !queues.PriceRingHoldsFifty( TicketCategory ) )
			return 0;
		var mean = queues.PriceRingMean( TicketCategory ) & 0xFF;
		return (above ? mean > v[0] : mean < v[0]) ? context.Score( scoreKey ) : 0;
	}

	// ------------------------------------------------------------------ the shops' value and satisfaction

	// [BIN:TPW-EXE:0x0059FB60 shop figure] 0x59FB60-0x5A0290 share the shape: the group's helper picks a shop (a handle of 0 gives 0); the worst figure of a "poor" line must be below the first word, the best of a "great" line above it (signed); the score is (figure - the word) x the second word with the figure first, so a poor line's is negative for a positive multiplier and never passes; the handle goes to the object's +4 and the figure to +8, which OpenTPW has no reader for
	private static int Shop( AdvisorScanContext context, AdvisorShopGroup group, bool worst, string thresholdKey, string multiplierKey, Func<IAdvisorQueuesState, AdvisorShopPick?> pick )
	{
		if ( !Read( context, out _, out var queues, out var v, thresholdKey, multiplierKey ) )
			return 0;
		if ( pick( queues ) is not { } shop || (ushort)shop.Handle == 0 )
			return 0;
		if ( worst ? shop.Value >= v[0] : shop.Value <= v[0] )
			return 0;
		return unchecked( (shop.Value - v[0]) * v[1] );
	}
}

/// <summary>The park statistics of the price, shop and research callbacks, computed from the economy without changing it.</summary>
internal sealed partial class EconomyParkState : IAdvisorQueuesState
{
	/// <summary>The days an attraction must have stood before the shop statistics count it (<c>0x004DD670</c> above 0x1E).</summary>
	private const int ShopMinimumAgeDays = 30;

	// [BIN:TPW-EXE:0x004C9920 price ring full] the ring record's full flag (+0x3C) set, or its head + 1 at the size (+0x38, 50)
	public bool PriceRingHoldsFifty( int category ) => economy.PriceStatistics.Count( category ) >= ParkPriceStatistics.Capacity;

	// [BIN:TPW-EXE:0x004C9830 price ring mean] the held values (the newest 50 at most) added in the x87 stack and divided by their number with a truncation (__ftol); no value returns a clear byte
	public int PriceRingMean( int category )
	{
		var values = economy.PriceStatistics.Values( category );
		if ( values.Count == 0 )
			return 0;
		long sum = 0;
		foreach ( var value in values )
			sum += value;
		return (int)(sum / values.Count) & 0xFF;
	}

	private static bool InGroup( EconomyObjectInfo info, AdvisorShopGroup group ) => group switch
	{
		// [BIN:TPW-EXE:0x004CADF0 stock shops] the attractions whose type record's +0x164 (UsageInfo.ShopType) is the argument: 2 food, 3 restaurant, 4 drinks
		AdvisorShopGroup.Food => info.ShopType == 2,
		AdvisorShopGroup.Restaurant => info.ShopType == 3,
		AdvisorShopGroup.Drinks => info.ShopType == 4,
		// [BIN:TPW-EXE:0x004CB640 retail shops] the attractions with +0x164 of 1, 5 or 6
		AdvisorShopGroup.Shops => info.ShopType is 1 or 5 or 6,
		// [BIN:TPW-EXE:0x004CC090 sideshows] the attractions whose type record's WhichUIType (+0x4AC) is 2
		_ => info.RatingType == 2,
	};

	// [BIN:TPW-EXE:0x004CADF0 shop value] the walk-aways of the 30-day ring (+0x230, head +0x2AC) summed x 100 over the customers of the customers ring (+0x1A8, head +0x224) summed, both as unsigned words in a double and truncated, divided unsigned; a shop whose customers sum is 0 is skipped
	private static int? Value( ParkObjectState item )
	{
		var customers = unchecked( (uint)item.CustomersRing.Total );
		if ( customers == 0 )
			return null;
		long walkAways = 0;
		foreach ( var day in item.WalkAwayDays )
			walkAways += (uint)day;
		return unchecked( (int)(unchecked( (uint)walkAways * 100u ) / customers) );
	}

	// [BIN:TPW-EXE:0x004E1E30 satisfaction] 50 plus the integer mean of the satisfaction ring's entries (+0x340, head +0x3BC)
	private static int? Satisfaction( ParkObjectState item ) => item.SatisfactionRing.Satisfaction;

	// [BIN:TPW-EXE:0x004CBEC0 sideshow satisfaction] the sideshows' best satisfaction skips a sideshow whose customers sum is 0, as the value helpers do; the stock and retail shops' satisfaction helpers do not
	private static int? SatisfactionWithCustomers( ParkObjectState item ) => unchecked( (int)item.CustomersRing.Total ) == 0 ? null : Satisfaction( item );

	/// <summary>The first shop of the group with the lowest (or highest) figure: the walk visits the things in order and only a strictly better figure replaces the pick.</summary>
	// [BIN:TPW-EXE:0x004CADF0 pick] the attraction (thing type 3) of the group older than 30 days (0x4DD670) with a figure: the first one, then a figure below (worst) or above (best) the pick's as signed words
	private AdvisorShopPick? Pick( AdvisorShopGroup group, Func<ParkObjectState, int?> figure, bool best )
	{
		AdvisorShopPick? pick = null;
		foreach ( var item in economy.Objects )
		{
			if ( !economy.Catalog.TryGet( item.InfoId, out var info ) || !InGroup( info, group ) || (int)economy.ObjectAgeDays( item ) <= ShopMinimumAgeDays )
				continue;
			if ( figure( item ) is not { } value )
				continue;
			if ( pick is not { } picked || (best ? picked.Value < value : value < picked.Value) )
				pick = new AdvisorShopPick( item.Id, value );
		}
		return pick;
	}

	public AdvisorShopPick? WorstValue( AdvisorShopGroup group ) => Pick( group, Value, best: false );
	public AdvisorShopPick? BestValue( AdvisorShopGroup group ) => Pick( group, Value, best: true );

	public AdvisorShopPick? WorstSatisfaction( AdvisorShopGroup group ) =>
		group == AdvisorShopGroup.Sideshows ? throw new ArgumentOutOfRangeException( nameof( group ), "tp.exe has no worst-satisfaction helper for the sideshows." ) : Pick( group, Satisfaction, best: false );

	public AdvisorShopPick? BestSatisfaction( AdvisorShopGroup group ) => Pick( group, group == AdvisorShopGroup.Sideshows ? SatisfactionWithCustomers : Satisfaction, best: true );

	// [BIN:TPW-EXE:0x00503B30 projects outstanding] the lab's projects of the category (5: every one) whose done points (+0x4) are below the cost (+0x0), which is every project not yet researched
	public int ResearchOutstanding( ResearchCategory category ) =>
		economy.Research.Items.Count( item => item.Category == category && !economy.Research.Completed.Contains( (item.InfoId, item.Level) ) );

	// [BIN:TPW-EXE:0x004C9E60 research done] 0x4C9E60 adds the ability (0x78545C + 12 x grade) of every researcher of the list (world +0x1DA742), whatever its state
	public int ResearchDone => economy.Staff.OfType( StaffType.Researcher ).Sum( member => economy.Settings.ResearchAbility[Math.Clamp( member.Grade, 0, BalanceSettings.GradeCount - 1 )] );

	// [BIN:TPW-EXE:0x004C9ED0 research capacity] MaxResearchersInPark (0x7852F8) times the best grade's ability (0x78548C), a 32-bit product
	public int ResearchCapacity => unchecked( economy.Settings[StaffType.Researcher].MaximumInPark * economy.Settings.ResearchAbility[BalanceSettings.GradeCount - 1] );

	// [BIN:TPW-EXE:0x00503980 research rate] the category's share (the effort, the workload and the shares' total, unsigned) times the staffing percentage (0x4C9E60 x 100 / 0x4C9ED0) / 100; 0 without a capacity; it tests no current project, which the lab's refresh does (ResearchFluid.Rate)
	public int ResearchRate( ResearchCategory category ) =>
		ResearchFluid.Rate( economy.Research.Effort, (int)category, economy.Research.Workload, hasProject: true, ResearchDone, economy.Settings[StaffType.Researcher].MaximumInPark, economy.Settings.ResearchAbility[BalanceSettings.GradeCount - 1] );

	// [BIN:TPW-EXE:0x004C9E40 researchers in the pool] 0x508380 counts the pool's 32 candidate slots (20 bytes) with the valid byte (+0xA) set whose type (+0) is the argument, 4 for the researchers
	public int ResearcherCandidates => economy.Staff.Candidates.Count( candidate => candidate.Type == StaffType.Researcher );

	// [BIN:TPW-EXE:0x004CA4C0 upgraded ride types] the type records (list 0x7890A0) of WhichUIType 0 whose control's researched level (+0x14, 0x4D3D80) is above 0
	public int ResearchedUpgradeTypes => economy.Catalog.Objects.Count( info => info.RatingType == 0 && economy.Research.ResearchedLevel( info.InfoId ) is > 0 );
}
