namespace OpenTPW;

/// <summary>
/// The park statistics the advisor's park callbacks (rides and breakdowns, cleanliness, guest mood) read, as the original's helpers <c>0x4C7BB0</c>-<c>0x4CCDF0</c> return them.
/// Every member is a pure read of the park: nothing here writes to the economy or the world, so the advisor stays outside the simulation hash.
/// </summary>
internal interface IAdvisorParkState
{
	/// <summary>Whether the park is open (<c>0x004C9050</c>: the closed flag <c>0x51A280</c> is clear).</summary>
	bool ParkOpen { get; }
	/// <summary>The staff of a role in the park (<c>0x004C9960</c>).</summary>
	int StaffCount( StaffType type );
	/// <summary>Whole park days since the stamp the object constructor writes (<c>0x004C9070</c>, park <c>+0x21C30</c>).</summary>
	int DaysSinceObjectStamp { get; }
	/// <summary>Whole park days since the stamp the staff thing's init writes (<c>0x004C90D0</c>, park <c>+0x21C38</c>).</summary>
	int DaysSinceHireStamp { get; }
	/// <summary>Whole park days of the park's clock (<c>0x004F8800</c> divided by one day with <c>__alldiv</c>).</summary>
	int DaysSinceStart { get; }
	/// <summary>Object types available to build of a kind (<c>0x004CA450</c> rides, <c>0x004CA510</c> shops, <c>0x004CA570</c> sideshows, <c>0x004CA5D0</c> features).</summary>
	int AvailableTypes( int kind, bool withTicketItems );
	/// <summary>Object types of a kind that have been built at least once (<c>0x004CA7B0</c>).</summary>
	int BuiltTypes( int kind, bool withTicketItems );
	/// <summary>Standing objects of a kind (<c>0x004CA8E0</c>; a ride must also be at the upgrade level).</summary>
	int ObjectsOfKind( int kind, int minimumLevel );
	/// <summary>Standing objects of a kind that are closed (<c>0x004CA9B0</c>: the open word <c>+0x68</c> is 0).</summary>
	int ClosedObjectsOfKind( int kind );
	/// <summary>Rides broken down (<c>0x004CA310</c>: state 1).</summary>
	int RidesBrokenDown { get; }
	/// <summary>Rides whose condition byte is below a percent, or that are broken down (<c>0x004CA3A0</c>).</summary>
	int RidesBelowCondition( int percent );
	/// <summary>The litter points (<c>0x004CCDF0</c>): littered cells as a percent of the walkable and littered cells.</summary>
	int LitterPoints { get; }
	/// <summary>The developed cells (<c>0x004C81B0</c>): cells of the types 1, 3, 4, 9 and 10.</summary>
	int DevelopedCells { get; }
	/// <summary>The toilets (<c>0x004C9EE0</c>).</summary>
	int Toilets { get; }
	/// <summary>The closed toilets (<c>0x004C9F90</c>).</summary>
	int ClosedToilets { get; }
	/// <summary>The mean toilet filth (<c>0x004CA160</c>): the integer mean of 100 minus the condition byte, 0 without a toilet.</summary>
	int MeanToiletFilth { get; }
	/// <summary>The guests in the park (<c>0x004C9130</c>).</summary>
	int GuestsInPark { get; }
	/// <summary>The guests whose need meter byte is above the threshold byte (<c>0x004C93F0</c>, <c>0x004C9340</c>, <c>0x004C9290</c>).</summary>
	int GuestsNeeding( GuestNeedMeter meter, int threshold );
	/// <summary>The guests holding a balloon (<c>0x004C9530</c>).</summary>
	int GuestsWithBalloons { get; }
	/// <summary>The guests in a costume (<c>0x004C95D0</c>).</summary>
	int GuestsInCostume { get; }
	/// <summary>The mean happiness (<c>0x004C7BB0</c>), a single.</summary>
	float AverageHappiness { get; }
}

/// <summary>
/// The park statistics of the advisor's park callbacks, computed from the economy without changing it. The helpers that the original caches (<c>0x004C81B0</c>'s block counts, the toilet count)
/// are recounted here and not written: the advisor's own calls do write them (<c>0x4C8D16</c>-<c>0x4C8D1D</c> for the blocks, <c>0x4C9F63</c>-<c>0x4C9F70</c> for the toilets), but <c>0x4C8C40</c> has the single caller <c>0x4C81B0</c>,
/// used only by the two advisor callbacks with age 0, and nothing else reads the toilet cache, so a fresh count gives what the original's callbacks see.
/// </summary>
internal sealed partial class EconomyParkState : IAdvisorParkState
{
	private readonly ParkEconomy economy;
	private readonly long? objectStampTurn, hireStampTurn;

	/// <param name="economy">The park.</param>
	/// <param name="objectStampTurn">The park turn of the newest object construction (<c>mLastObjectBuilt</c>), the advisor's own record; null derives it from the standing objects.</param>
	/// <param name="hireStampTurn">The park turn of the newest hire (<c>mLastStaffHired</c>); null derives it from the employed staff.</param>
	public EconomyParkState( ParkEconomy economy, long? objectStampTurn = null, long? hireStampTurn = null )
	{
		this.economy = economy ?? throw new ArgumentNullException( nameof( economy ) );
		this.objectStampTurn = objectStampTurn;
		this.hireStampTurn = hireStampTurn;
	}

	/// <summary>
	/// The stamps of a park the advisor has no record of (a new park, or an OpenTPW save from before the advisor's snapshot kept them; an original save's come from its analyser,
	/// <see cref="OriginalEconomyImport.AnalyserStampTurns"/>): the newest standing object's and employed member's turn (0 without any), where the original keeps the stamp after a demolition or a quit.
	/// </summary>
	public static (long ObjectTurn, long HireTurn) NewestStamps( ParkEconomy economy ) => (
		economy.Objects.Select( item => ParkCalendar.Turn( item.BuiltTick ) ).DefaultIfEmpty( 0 ).Max(),
		economy.Staff.Members.Select( member => ParkCalendar.Turn( member.HiredTick ) ).DefaultIfEmpty( 0 ).Max() );

	// [BIN:TPW-EXE:0x004C9050 park open] the closed flag of the world (0x51A280, +0x1DA710) clear
	public bool ParkOpen => economy.IsParkOpen;

	// [BIN:TPW-EXE:0x004C9960 staff count] the things of the models 4 to 8 whose model equals the argument and whose flag byte +3 is 0; StaffType is the order handyman, mechanic, entertainer, guard, researcher the original numbers 5, 4, 6, 7, 8
	public int StaffCount( StaffType type ) => economy.Staff.OfType( type ).Count();

	// [BIN:TPW-EXE:0x004C7680 object stamp] every object construction (0x4DB090, its last call 0x4DB7A3) overwrites the park's mLastObjectBuilt (+0x21C30, saved at 0x4C6B9A) with the park time, so it is the newest construction, not the first; the park init 0x4C58C0 stamps it with the current time (0x4C5CAF) after zeroing it (0x4C5B84)
	public int DaysSinceObjectStamp => DaysSince( objectStampTurn ?? NewestStamps( economy ).ObjectTurn );

	// [BIN:TPW-EXE:0x004C76D0 hire stamp] every staff thing init (0x504B90, at 0x504C65) overwrites mLastStaffHired (+0x21C38, saved at 0x4C6BC4) with the park time: the newest hire; the park init stamps it too (0x4C5CDE)
	public int DaysSinceHireStamp => DaysSince( hireStampTurn ?? NewestStamps( economy ).HireTurn );

	// [BIN:TPW-EXE:0x004C9070 days since a stamp] (0x4F8690 park time now − stamp, both from the calendar base) divided by 864,000,000,000 (100 ns per day) with __alldiv: signed, truncated toward zero, so a stamp still to come (only a modified save holds one) counts negative days
	private int DaysSince( long stampTurn ) => (int)((economy.Turn - stampTurn) * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay);

	// [BIN:TPW-EXE:0x0059DB04 days since start] 0x4F8800 (world + 0x2A0) divided by 0xC92A69C000 with __alldiv
	public int DaysSinceStart => (int)(economy.Turn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay);

	/// <summary>Whether the object control's available flag (<c>+0x10</c> of the record <c>0x4D3D80</c>/<c>0x4D3D10</c> finds: the buy list shows only these, <c>0x4AB023</c>) is set: a type outside the research tables, or researched.</summary>
	private bool Available( EconomyObjectInfo info ) => !economy.Rules.Research || economy.Research.IsAvailable( info.InfoId );

	// [BIN:TPW-EXE:0x004CA450 available types] the type records (list 0x7890A0) whose WhichUIType (+0x4AC) is the kind and whose control is available; a type with a golden ticket cost (+0xC4) counts only with the flag set (0x4CA510, 0x4CA570 and 0x4CA5D0 count without the cost test)
	public int AvailableTypes( int kind, bool withTicketItems ) =>
		economy.Catalog.Objects.Count( info => info.RatingType == kind && Available( info ) && (withTicketItems || info.GoldenTicketCost == 0) );

	// [BIN:TPW-EXE:0x004CA7B0 built types] the type records of the kind whose control's built count (+0x18, never lowered) is above 0, without the golden ticket cost types unless the flag is set
	public int BuiltTypes( int kind, bool withTicketItems ) =>
		economy.Catalog.Objects.Count( info => info.RatingType == kind && economy.Counters[ParkCounters.Built( info.InfoId )] > 0 && (withTicketItems || info.GoldenTicketCost == 0) );

	private IEnumerable<(ParkObjectState Item, EconomyObjectInfo Info)> Standing( int kind ) =>
		economy.Objects.Select( item => (Item: item, Found: economy.Catalog.TryGet( item.InfoId, out var info ), Info: info) ).Where( entry => entry.Found && entry.Info.RatingType == kind ).Select( entry => (entry.Item, entry.Info) );

	// [BIN:TPW-EXE:0x004CA8E0 standing objects] the attractions (thing type 3) whose type's WhichUIType is the kind; for a ride (kind 0) its upgrade level byte (+0x50) must be at least the argument
	public int ObjectsOfKind( int kind, int minimumLevel ) => Standing( kind ).Count( entry => kind != 0 || (byte)entry.Item.Level >= minimumLevel );

	// [BIN:TPW-EXE:0x004E0440 closed] the open word +0x68 is 0: the ride was closed, and entering the broken, upgrading and condemned states (0x4E0FD8, 0x4E10DC, the condemn case of 0x4E0E60) clears it;
	// state 3, which the constructor gives a type nobody may choose (0x4DB4FB: the staff room and the other features), is no closing state and leaves the word as the object's script sets it
	private static bool IsClosed( ParkObjectState item ) =>
		!item.IsOpen || ParkEconomy.ObjectState( item ) is not (RideObjectStates.Idle or RideObjectStates.NotAnAttraction);

	// [BIN:TPW-EXE:0x004CA9B0 closed objects] the attractions of the kind whose open word is 0
	public int ClosedObjectsOfKind( int kind ) => Standing( kind ).Count( entry => IsClosed( entry.Item ) );

	// [BIN:TPW-EXE:0x004E0370 broken] state 1 only; a condemned ride (state 4) is no broken one
	private static bool IsBrokenDown( ParkObjectState item ) => ParkEconomy.ObjectState( item ) == RideObjectStates.BrokenDown;

	// [BIN:TPW-EXE:0x004CA310 rides broken down] the rides (WhichUIType 0) in state 1
	public int RidesBrokenDown => Standing( 0 ).Count( entry => IsBrokenDown( entry.Item ) );

	// [BIN:TPW-EXE:0x004CA3A0 rides worn or broken] the rides whose condition byte (+0x44, truncated, signed compare with the argument) is below the percent, or that are in state 1
	public int RidesBelowCondition( int percent ) =>
		Standing( 0 ).Count( entry => StaffVitals.Byte( (float)entry.Item.Repair ) < percent || IsBrokenDown( entry.Item ) );

	// [BIN:TPW-EXE:0x004CCDF0 litter points] over the 128 x 128 cells: a cell counts when it is a path (type 1) or a queue or entrance (3 or 9) cell or holds litter (+0x2C); the litter cells x 100 divided by that count (signed), 0 without any
	public int LitterPoints
	{
		get
		{
			var litter = economy.Litter;
			int cells = 0, littered = 0;
			for ( var x = 0; x < ParkLitter.MapSize; x++ )
			{
				for ( var y = 0; y < ParkLitter.MapSize; y++ )
				{
					var dirty = litter.TypeAt( x, y ) != LitterType.None;
					if ( dirty || litter.Cells?.RawTypeAt( x, y ) is 1 or 3 or 9 )
						cells++;
					if ( dirty )
						littered++;
				}
			}
			return cells == 0 ? 0 : littered * 100 / cells;
		}
	}

	// [BIN:TPW-EXE:0x004C8C40 developed cells] the 1024 blocks of 4 x 4 cells each count the cells of the types 1, 3 or 9 (0x536310, 0x536320), 9, 10, 4 (0x536380) and the constant-zero stub 0x5D60C0; 0x4C81B0 adds the blocks
	public int DevelopedCells
	{
		get
		{
			if ( economy.Litter.Cells is not { } map )
				return 0;
			var count = 0;
			for ( var x = 0; x < ParkLitter.MapSize; x++ )
				for ( var y = 0; y < ParkLitter.MapSize; y++ )
					count += map.RawTypeAt( x, y ) is 1 or 3 or 4 or 9 or 10 ? 1 : 0;
			return count;
		}
	}

	private IEnumerable<ParkObjectState> ToiletObjects => economy.Objects.Where( item => economy.Catalog.TryGet( item.InfoId, out var info ) && info.ProvidesRelief );

	// [BIN:TPW-EXE:0x004C9EE0 toilets] the attractions with the relief flag (+0x32 bit 0)
	public int Toilets => ToiletObjects.Count();

	// [BIN:TPW-EXE:0x004C9F90 closed toilets] the relief attractions whose open word is 0
	public int ClosedToilets => ToiletObjects.Count( IsClosed );

	// [BIN:TPW-EXE:0x004CA160 mean toilet filth] the signed integer mean over the relief attractions of 100 minus the condition byte (+0x44 truncated), 0 without a toilet
	public int MeanToiletFilth
	{
		get
		{
			var toilets = ToiletObjects.ToList();
			return toilets.Count == 0 ? 0 : toilets.Sum( item => 100 - StaffVitals.Byte( (float)item.Repair ) ) / toilets.Count;
		}
	}

	private IParkGuestStatistics Guests => economy.GuestStatistics;

	public int GuestsInPark => Guests.GuestCensus;
	public int GuestsNeeding( GuestNeedMeter meter, int threshold ) => Guests.CountGuestsNeeding( meter, threshold );
	public int GuestsWithBalloons => Guests.GuestsWithBalloons;
	public int GuestsInCostume => Guests.GuestsInCostume;
	public float AverageHappiness => Guests.CensusHappiness;
}

/// <summary>
/// The advisor's park callbacks, ported from <c>tp.exe</c> (rides and breakdowns, cleanliness and guest mood), by the address of the original's callback. Each reads the park
/// through <see cref="IAdvisorParkState"/> and its thresholds through <see cref="AdvisorScanContext.Parameter"/>; a threshold the settings file lacks leaves its callback at 0.
/// </summary>
internal static class AdvisorParkCallbacks
{
	/// <summary>The integer thresholds and multipliers these callbacks read (words of the <c>Advisor.sam</c> object).</summary>
	public static readonly IReadOnlyList<string> IntegerKeys = new[]
	{
		"VisitorsThirsty.ThirstierThan", "VisitorsThirsty.ScorePerThirstyPerson", "VisitorsHungry.HungrierThan", "VisitorsHungry.ScorePerHungryPerson",
		"StaffHireMechanics1.PoorerStateThan", "StaffHireMechanics1.ScorePerWornRide", "StaffHireMechanics2.ScorePerBrokenRide",
		"StaffHireMechanics3.IdealNumberOfRidesPerMechanic", "StaffHireMechanics3.ScorePerExtraRide",
		"WaitingTimes.DaysForStaff", "WaitingTimes.DaysForObjects",
		"StaffHireHandymen1.ScorePerLitterPoint", "StaffHireHandymen1.LitterPointCutoffLow", "StaffHireHandymen1.LitterPointCutoffHigh",
		"StaffHireHandymen2.ScorePerLitterPoint", "StaffHireHandymen2.LitterPointCutoffLow",
		"StaffHireHandymen3.IdealDevelopedCellsPerHandyman", "StaffHireHandymen3.ScorePerExtra10Cells", "StaffHireHandymen3.MinCellsForConsideration",
		"RidesPoorSelection.PoorerThan", "RidesPoorSelection.MoreRidesThan", "RidesBuildRides.AdditionalPerDay",
		"ShopsPoorSelection.PoorerThan", "ShopsPoorSelection.MoreShopsThan", "ShopsBuildShops.AdditionalPerDay", "ShopsBuildMoreShops.WealthMoreThan",
		"SideshowsPoorSelection.PoorerThan", "SideshowsPoorSelection.MoreSideshowsThan", "SideshowsBuildSideshows.AdditionalPerDay", "SideshowsBuildMoreSideshows.WealthMoreThan",
		"FeaturesPoorSelection.PoorerThan", "FeaturesPoorSelection.MoreFeaturesThan", "FeaturesBuildFeatures.AdditionalPerDay",
		"FeaturesNoToilets.NeedingMoreThan", "FeaturesNoToilets.MorePeopleThanThisNeeding",
		"FeaturesDirtyToilets1.FilthierThan", "FeaturesDirtyToilets1.PointsPerFilthPct", "FeaturesDirtyToilets2.FilthierThan", "FeaturesDirtyToilets2.PointsPerFilthPct",
		"FeaturesDirtyToilets3.FilthierThan", "FeaturesDirtyToilets3.PointsPerFilthPct", "FeaturesDirtyToilets4.FilthierThan", "FeaturesDirtyToilets4.PointsPerFilthPct",
		"CongratVisitorsHappy.MinimumVisitors", "CongratVisitorsHappy.HappierThan", "CongratBalloons.MoreThan", "CongratCostumes.MoreThan",
		"RidesClosed.AtLeastThisManyRides", "RidesClosed.MoreThanThisPercentClosed", "ShopsClosed.MoreThanThisPercentClosed",
		"SideshowsClosed.MoreThanThisPercentClosed", "ToiletsClosed.MoreThanThisPercentClosed",
	};

	/// <summary>The single-precision ratios these callbacks read (<c>fmul dword</c>).</summary>
	public static readonly IReadOnlyList<string> FloatKeys = new[] { "ShopsBuildMoreShops.ShopsPerRideRatio", "SideshowsBuildMoreSideshows.SideshowsPerRideRatio" };

	public const uint Thirsty = 0x59C9D0;
	public const uint Hungry = 0x59CA00;
	public const uint WornRides = 0x59CA30;
	public const uint BrokenRides = 0x59CA80;
	public const uint TooFewMechanics = 0x59CAC0;
	public const uint LitterHandymenNone = 0x59CB40;
	public const uint LitterHandymenHigh = 0x59CBB0;
	public const uint CellsPerHandyman = 0x59CC20;
	public const uint RidesPoorSelection = 0x59D990;
	public const uint RidesBuildRides = 0x59DAD0;
	public const uint ShopsPoorSelection = 0x59DCA0;
	public const uint ShopsBuildShops = 0x59DDE0;
	public const uint ShopsBuildMoreShops = 0x59DE80;
	public const uint SideshowsPoorSelection = 0x59DF30;
	public const uint SideshowsBuildSideshows = 0x59E070;
	public const uint SideshowsBuildMoreSideshows = 0x59E110;
	public const uint FeaturesPoorSelection = 0x59E1C0;
	public const uint FeaturesBuildFeatures = 0x59E2E0;
	public const uint NoToilets = 0x59E380;
	public const uint ToiletPerFigure = 0x59E420;
	public const uint DirtyToilets1 = 0x59E450;
	public const uint DirtyToilets2 = 0x59E510;
	public const uint DirtyToilets3 = 0x59E5D0;
	public const uint DirtyToilets4 = 0x59E680;
	public const uint VisitorsHappy = 0x59F5E0;
	public const uint Balloons = 0x59F940;
	public const uint Costumes = 0x59F970;
	public const uint RidesClosed = 0x5A02F0;
	public const uint ShopsClosed = 0x5A0370;
	public const uint SideshowsClosed = 0x5A03D0;
	public const uint ToiletsClosed = 0x5A0430;

	private const int Rides = 0;
	private const int Shops = 1;
	private const int Sideshows = 2;
	private const int Features = 3;

	/// <summary>The park callbacks by the address of the original's callback (the descriptor's word +0x10).</summary>
	public static readonly IReadOnlyDictionary<uint, AdvisorScoreCallback> Callbacks = new Dictionary<uint, AdvisorScoreCallback>
	{
		[Thirsty] = context => NeedingScore( context, GuestNeedMeter.Thirst, "VisitorsThirsty.ThirstierThan", "VisitorsThirsty.ScorePerThirstyPerson" ),
		[Hungry] = context => NeedingScore( context, GuestNeedMeter.Hunger, "VisitorsHungry.HungrierThan", "VisitorsHungry.ScorePerHungryPerson" ),
		[WornRides] = WornRidesScore,
		[BrokenRides] = BrokenRidesScore,
		[TooFewMechanics] = TooFewMechanicsScore,
		[LitterHandymenNone] = LitterNoHandymenScore,
		[LitterHandymenHigh] = LitterManyScore,
		[CellsPerHandyman] = CellsPerHandymanScore,
		[RidesPoorSelection] = context => PoorSelection( context, Rides, "RidesPoorSelection.PoorerThan", "RidesPoorSelection.MoreRidesThan", "RidesPoorSelection.Score" ),
		[RidesBuildRides] = RidesBuildRidesScore,
		[ShopsPoorSelection] = context => PoorSelection( context, Shops, "ShopsPoorSelection.PoorerThan", "ShopsPoorSelection.MoreShopsThan", "ShopsPoorSelection.Score" ),
		[ShopsBuildShops] = context => BuildFirst( context, Rides, Shops, "ShopsBuildShops.AdditionalPerDay", "ShopsBuildShops.Score" ),
		[ShopsBuildMoreShops] = ShopsBuildMoreShopsScore,
		[SideshowsPoorSelection] = context => PoorSelection( context, Sideshows, "SideshowsPoorSelection.PoorerThan", "SideshowsPoorSelection.MoreSideshowsThan", "SideshowsPoorSelection.Score" ),
		[SideshowsBuildSideshows] = context => BuildFirst( context, Shops, Sideshows, "SideshowsBuildSideshows.AdditionalPerDay", "SideshowsBuildSideshows.Score" ),
		[SideshowsBuildMoreSideshows] = SideshowsBuildMoreSideshowsScore,
		[FeaturesPoorSelection] = context => PoorSelection( context, Features, "FeaturesPoorSelection.PoorerThan", "FeaturesPoorSelection.MoreFeaturesThan", "FeaturesPoorSelection.Score" ),
		[FeaturesBuildFeatures] = context => BuildFirst( context, Sideshows, Features, "FeaturesBuildFeatures.AdditionalPerDay", "FeaturesBuildFeatures.Score" ),
		[NoToilets] = NoToiletsScore,
		// [BIN:TPW-EXE:0x0059E420 toilet figure] with the park open it calls the per-toilet statistic 0x4CCA40 and pops its result unused (fstp st(0)); every path returns 0, so the line never passes
		[ToiletPerFigure] = _ => 0,
		[DirtyToilets1] = context => DirtyToilets( context, handymen: false, "FeaturesDirtyToilets1.FilthierThan", "FeaturesDirtyToilets3.FilthierThan", "FeaturesDirtyToilets1.PointsPerFilthPct" ),
		[DirtyToilets2] = context => DirtyToilets( context, handymen: true, "FeaturesDirtyToilets2.FilthierThan", "FeaturesDirtyToilets4.FilthierThan", "FeaturesDirtyToilets1.PointsPerFilthPct" ),
		[DirtyToilets3] = context => DirtyToilets( context, handymen: false, "FeaturesDirtyToilets3.FilthierThan", null, "FeaturesDirtyToilets3.PointsPerFilthPct" ),
		[DirtyToilets4] = context => DirtyToilets( context, handymen: true, "FeaturesDirtyToilets4.FilthierThan", null, "FeaturesDirtyToilets4.PointsPerFilthPct" ),
		[VisitorsHappy] = VisitorsHappyScore,
		[Balloons] = context => CountAbove( context, park => park.GuestsWithBalloons, "CongratBalloons.MoreThan", "CongratBalloons.Score" ),
		[Costumes] = context => CountAbove( context, park => park.GuestsInCostume, "CongratCostumes.MoreThan", "CongratCostumes.Score" ),
		[RidesClosed] = RidesClosedScore,
		[ShopsClosed] = context => ClosedShare( context, Shops, "ShopsClosed.MoreThanThisPercentClosed", "ShopsClosed.Score" ),
		[SideshowsClosed] = context => ClosedShare( context, Sideshows, "SideshowsClosed.MoreThanThisPercentClosed", "SideshowsClosed.Score" ),
		[ToiletsClosed] = ToiletsClosedScore,
	};

	/// <summary>Reads thresholds as the words they are (a float key cast to a word is never read this way); false when the park or one key is missing.</summary>
	private static bool Read( AdvisorScanContext context, out IAdvisorParkState park, out int[] values, params string[] keys )
	{
		values = new int[keys.Length];
		park = context.Park!;
		if ( context.Park is null )
			return false;
		for ( var index = 0; index < keys.Length; index++ )
		{
			if ( context.Parameter( keys[index] ) is not { } value )
				return false;
			values[index] = (int)value;
		}
		return true;
	}

	// [BIN:TPW-EXE:0x0059C9D0 thirsty] the guests above the byte at +0xB0 (0x4C93F0) times the word at +0xAC; 0x0059CA00 is the same for hunger (+0xB8, +0xB4)
	private static int NeedingScore( AdvisorScanContext context, GuestNeedMeter meter, string thresholdKey, string scoreKey ) =>
		Read( context, out var park, out var v, thresholdKey, scoreKey ) ? unchecked( park.GuestsNeeding( meter, v[0] ) * v[1] ) : 0;

	// [BIN:TPW-EXE:0x0059CA30 worn rides] no mechanic (0x4C9960 of 4 above 0 gives 0); then the rides worn or broken below the percent at +0xC0 (0x4CA3A0) times the word at +0xBC
	private static int WornRidesScore( AdvisorScanContext context ) =>
		Read( context, out var park, out var v, "StaffHireMechanics1.PoorerStateThan", "StaffHireMechanics1.ScorePerWornRide" ) && park.StaffCount( StaffType.Mechanic ) <= 0
			? unchecked( park.RidesBelowCondition( v[0] ) * v[1] ) : 0;

	// [BIN:TPW-EXE:0x0059CA80 broken rides] no mechanic; the rides broken down (0x4CA310) times the word at +0xC4
	private static int BrokenRidesScore( AdvisorScanContext context ) =>
		Read( context, out var park, out var v, "StaffHireMechanics2.ScorePerBrokenRide" ) && park.StaffCount( StaffType.Mechanic ) <= 0
			? unchecked( park.RidesBrokenDown * v[0] ) : 0;

	// [BIN:TPW-EXE:0x0059CAC0 too few mechanics] the days since the hire stamp (0x4C90D0) must reach the word at +0x630 (signed); rides (0x4CA8E0 of 0, 0) minus mechanics times the word at +0xC8 must be above 0: that times the word at +0xCC
	private static int TooFewMechanicsScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var v, "WaitingTimes.DaysForStaff", "StaffHireMechanics3.IdealNumberOfRidesPerMechanic", "StaffHireMechanics3.ScorePerExtraRide" ) )
			return 0;
		if ( park.DaysSinceHireStamp < v[0] )
			return 0;
		var extra = unchecked( park.ObjectsOfKind( Rides, 0 ) - park.StaffCount( StaffType.Mechanic ) * v[1] );
		return extra > 0 ? unchecked( v[2] * extra ) : 0;
	}

	// [BIN:TPW-EXE:0x0059CB40 litter, no handyman] the park open, no handyman (0x4C9960 of 5 above 0 gives 0), litter points (0x4CCDF0) at most the word at +0xD8: the word at +0xD0 times them minus the word at +0xD4
	private static int LitterNoHandymenScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var v, "StaffHireHandymen1.ScorePerLitterPoint", "StaffHireHandymen1.LitterPointCutoffLow", "StaffHireHandymen1.LitterPointCutoffHigh" )
			|| !park.ParkOpen || park.StaffCount( StaffType.Handyman ) > 0 )
			return 0;
		var litter = park.LitterPoints;
		return litter > v[2] ? 0 : unchecked( v[0] * litter - v[1] );
	}

	// [BIN:TPW-EXE:0x0059CBB0 litter, many] the park open, no handyman, litter points at least the word at +0xE0: the word at +0xDC times them minus the word at +0xE0
	private static int LitterManyScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var v, "StaffHireHandymen2.ScorePerLitterPoint", "StaffHireHandymen2.LitterPointCutoffLow" )
			|| !park.ParkOpen || park.StaffCount( StaffType.Handyman ) > 0 )
			return 0;
		var litter = park.LitterPoints;
		return litter < v[1] ? 0 : unchecked( v[0] * litter - v[1] );
	}

	// [BIN:TPW-EXE:0x0059CC20 cells per handyman] the park open and at least one handyman; the developed cells (0x4C81B0) at least the word at +0xEC; cells minus handymen times the word at +0xE4 above 0: that times the word at +0xE8
	private static int CellsPerHandymanScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var v, "StaffHireHandymen3.MinCellsForConsideration", "StaffHireHandymen3.IdealDevelopedCellsPerHandyman", "StaffHireHandymen3.ScorePerExtra10Cells" )
			|| !park.ParkOpen )
			return 0;
		var handymen = park.StaffCount( StaffType.Handyman );
		if ( handymen == 0 )
			return 0;
		var cells = park.DevelopedCells;
		if ( cells < v[0] )
			return 0;
		var extra = unchecked( cells - handymen * v[1] );
		return extra > 0 ? unchecked( v[2] * extra ) : 0;
	}

	/// <summary>Built types as a percent of the available ones (<c>lea</c> x 5, x 5, <c>shl</c> 2, then an unsigned <c>div</c>).</summary>
	private static uint BuiltPercent( int built, int available ) => unchecked( (uint)built * 100u / (uint)available );

	// [BIN:TPW-EXE:0x0059D990 poor selection] 0x59DCA0 (shops), 0x59DF30 (sideshows) and 0x59E1C0 (features) share the shape: types available (0x4CA450 of 0 for rides, else 0x4CA510, 0x4CA570, 0x4CA5D0) above 0; the days since the object stamp (0x4C9070) at least the word at +0x634; the standing objects of the kind (0x4CA8E0) at least the 'more than' word (both signed); built types x 100 / available (unsigned) below the 'poorer than' word (unsigned): the flat score
	private static int PoorSelection( AdvisorScanContext context, int kind, string poorerKey, string moreKey, string scoreKey )
	{
		if ( !Read( context, out var park, out var v, "WaitingTimes.DaysForObjects", moreKey, poorerKey ) )
			return 0;
		var available = park.AvailableTypes( kind, withTicketItems: kind != Rides );
		if ( available == 0 || park.DaysSinceObjectStamp < v[0] || park.ObjectsOfKind( kind, 0 ) < v[1] )
			return 0;
		return BuiltPercent( park.BuiltTypes( kind, withTicketItems: false ), available ) < unchecked( (uint)v[2] ) ? context.Score( scoreKey ) : 0;
	}

	// [BIN:TPW-EXE:0x0059DAD0 build rides] no ride type was ever built (0x4CA7B0 of 0 with the flag 1): the flat word at +0x230 plus the days since the start (0x4F8800 / one day) times the word at +0x234
	private static int RidesBuildRidesScore( AdvisorScanContext context ) =>
		Read( context, out var park, out var v, "RidesBuildRides.AdditionalPerDay" ) && park.BuiltTypes( Rides, withTicketItems: true ) == 0
			? unchecked( park.DaysSinceStart * v[0] + context.Score( "RidesBuildRides.Score" ) ) : 0;

	// [BIN:TPW-EXE:0x0059DDE0 build the first] 0x59DDE0, 0x59E070 and 0x59E2E0 share the shape: the previous kind's types were built (0x4CA7B0, rides for the shops, shops for the sideshows, sideshows for the features: the original asks for the previous kind, not for rides); the days since the object stamp at least the word at +0x634; none of this kind's types built: the flat score plus the days since the start times the 'per day' word
	private static int BuildFirst( AdvisorScanContext context, int previousKind, int kind, string perDayKey, string scoreKey ) =>
		Read( context, out var park, out var v, "WaitingTimes.DaysForObjects", perDayKey )
			&& park.BuiltTypes( previousKind, withTicketItems: false ) != 0 && park.DaysSinceObjectStamp >= v[0] && park.BuiltTypes( kind, withTicketItems: false ) == 0
			? unchecked( park.DaysSinceStart * v[1] + context.Score( scoreKey ) ) : 0;

	// [BIN:TPW-EXE:0x0059DE80 more shops] shop types built (0x4CA7B0 of 1), the days since the object stamp, then the original counts the shop types a second time where 0x59E110 counts the rides (push 1, not push 0, at 0x59DEC9): the product of that second count and the single at +0x270 must exceed the first, which no ratio below 1 allows; then the balance above the word at +0x274 (signed): the flat score
	private static int ShopsBuildMoreShopsScore( AdvisorScanContext context ) =>
		BuildMore( context, Shops, Shops, "ShopsBuildMoreShops.ShopsPerRideRatio", "ShopsBuildMoreShops.WealthMoreThan", "ShopsBuildMoreShops.Score" );

	// [BIN:TPW-EXE:0x0059E110 more sideshows] sideshow types built (0x4CA7B0 of 2), the days since the object stamp, the ride types built (0x4CA7B0 of 0) times the single at +0x294 above the sideshows (fild, fmul, fcompp), the balance above the word at +0x298: the flat score
	private static int SideshowsBuildMoreSideshowsScore( AdvisorScanContext context ) =>
		BuildMore( context, Sideshows, Rides, "SideshowsBuildMoreSideshows.SideshowsPerRideRatio", "SideshowsBuildMoreSideshows.WealthMoreThan", "SideshowsBuildMoreSideshows.Score" );

	private static int BuildMore( AdvisorScanContext context, int kind, int comparedKind, string ratioKey, string wealthKey, string scoreKey )
	{
		if ( context.Park is not { } park || context.Money is not { } money || context.Parameter( ratioKey ) is not { } ratio || context.Parameter( wealthKey ) is not { } wealth
			|| context.Parameter( "WaitingTimes.DaysForObjects" ) is not { } days )
			return 0;
		var built = park.BuiltTypes( kind, withTicketItems: false );
		if ( built == 0 || park.DaysSinceObjectStamp < (int)days )
			return 0;
		// the x87 runs at 24 bits: fild is exact, fmul rounds the product to a single
		var product = (float)((float)park.BuiltTypes( comparedKind, withTicketItems: false ) * (float)ratio);
		return product > built && money.Balance > (int)wealth ? context.Score( scoreKey ) : 0;
	}

	// [BIN:TPW-EXE:0x0059E380 no toilets] the park open and no toilet (0x4C9EE0); the guests needing the toilet above the byte at +0x2B8 (0x4C9290) x 100 / the guests in the park (0x4C9130, signed, none gives 0) above the word at +0x2BC: the flat score
	private static int NoToiletsScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var v, "FeaturesNoToilets.NeedingMoreThan", "FeaturesNoToilets.MorePeopleThanThisNeeding" ) || !park.ParkOpen || park.Toilets != 0 )
			return 0;
		var needing = park.GuestsNeeding( GuestNeedMeter.Toilet, v[0] );
		var census = park.GuestsInPark;
		return census != 0 && unchecked( needing * 100 ) / census > v[1] ? context.Score( "FeaturesNoToilets.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059E450 dirty toilets] 0x59E450-0x59E680: the park open; no handyman (0x59E450, 0x59E5D0) or a handyman (0x59E510, 0x59E680); a toilet exists (the worst toilet's handle 0x4CA220 is not 0); the mean filth (0x4CA160) at least the first threshold and, when there is an upper one, at most it: the points word times the filth
	private static int DirtyToilets( AdvisorScanContext context, bool handymen, string lowKey, string? highKey, string pointsKey )
	{
		if ( !Read( context, out var park, out var v, highKey == null ? new[] { lowKey, pointsKey } : new[] { lowKey, pointsKey, highKey } ) || !park.ParkOpen )
			return 0;
		if ( (park.StaffCount( StaffType.Handyman ) > 0) != handymen )
			return 0;
		if ( park.Toilets == 0 )
			return 0;
		var filth = park.MeanToiletFilth;
		if ( filth < v[0] || (highKey != null && filth > v[2]) )
			return 0;
		return unchecked( v[1] * filth );
	}

	// [BIN:TPW-EXE:0x0059F5E0 visitors happy] the guests in the park (0x4C9130) at least the word at +0x438 (signed); the mean happiness (0x4C7BB0, a single) at least the word at +0x43C (fcompp: only a larger threshold stops it): the flat score
	private static int VisitorsHappyScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var v, "CongratVisitorsHappy.MinimumVisitors", "CongratVisitorsHappy.HappierThan" ) || park.GuestsInPark < v[0] )
			return 0;
		return park.AverageHappiness >= (float)v[1] ? context.Score( "CongratVisitorsHappy.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059F940 balloons] the guests holding a balloon (0x4C9530) above the word at +0x4A0 (signed): the flat score; 0x0059F970 is the same for the costumes (0x4C95D0, +0x4A8)
	private static int CountAbove( AdvisorScanContext context, Func<IAdvisorParkState, int> count, string moreKey, string scoreKey ) =>
		Read( context, out var park, out var v, moreKey ) && count( park ) > v[0] ? context.Score( scoreKey ) : 0;

	/// <summary>Closed objects as a percent of the standing ones: x 100 and a signed divide.</summary>
	private static int ClosedPercent( int closed, int total ) => unchecked( closed * 100 ) / total;

	// [BIN:TPW-EXE:0x005A02F0 rides closed] the park open; rides (0x4CA8E0 of 0, 0) at least the word at +0x57C and above 0; closed rides (0x4CA9B0) x 100 / rides above the word at +0x580: the flat score
	private static int RidesClosedScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var v, "RidesClosed.AtLeastThisManyRides", "RidesClosed.MoreThanThisPercentClosed" ) || !park.ParkOpen )
			return 0;
		var rides = park.ObjectsOfKind( Rides, 0 );
		if ( rides == 0 || rides < v[0] )
			return 0;
		return ClosedPercent( park.ClosedObjectsOfKind( Rides ), rides ) > v[1] ? context.Score( "RidesClosed.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x005A0370 shops closed] objects of the kind (0x4CA8E0 of the kind, 0) above 0; closed ones (0x4CA9B0) x 100 / them above the percent word: the flat score; 0x005A03D0 is the same for the sideshows. Neither tests the park open
	private static int ClosedShare( AdvisorScanContext context, int kind, string percentKey, string scoreKey )
	{
		if ( !Read( context, out var park, out var v, percentKey ) )
			return 0;
		var total = park.ObjectsOfKind( kind, 0 );
		return total != 0 && ClosedPercent( park.ClosedObjectsOfKind( kind ), total ) > v[0] ? context.Score( scoreKey ) : 0;
	}

	// [BIN:TPW-EXE:0x005A0430 toilets closed] toilets (0x4C9EE0) above 0; closed toilets (0x4C9F90) x 100 / toilets above the word at +0x598: the flat score
	private static int ToiletsClosedScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var v, "ToiletsClosed.MoreThanThisPercentClosed" ) )
			return 0;
		var toilets = park.Toilets;
		return toilets != 0 && ClosedPercent( park.ClosedToilets, toilets ) > v[0] ? context.Score( "ToiletsClosed.Score" ) : 0;
	}
}

/// <summary>
/// The advice game code posts about a ride that breaks down or is about to: the wear step's imminent-breakdown line (66) and the breakdown handler's lines (68, 69 and the condemned ride's 258).
/// Pure decisions over the economy: the controller queues the result.
/// </summary>
internal static class AdvisorRideEvents
{
	/// <summary>The advice of a ride whose condition falls below 10 percent in one wear step (<c>0x4DF8C0</c>).</summary>
	public const int BreakdownImminentMessage = 66;
	/// <summary>The advice of a breakdown with no mechanic hired (<c>0x4DAE64</c>: message 0x44).</summary>
	public const int NoMechanicsMessage = 68;
	/// <summary>The advice of a breakdown with every mechanic busy (<c>0x4DAFF2</c>: message 0x45).</summary>
	public const int BusyMechanicsMessage = 69;

	/// <summary>The condition byte below which the wear step announces the breakdown (<c>0x7005A0</c>: 10.0).</summary>
	public const float ImminentBelow = 10f;

	/// <summary>
	/// Whether a wear step of <paramref name="wear"/> took the condition to <paramref name="conditionAfter"/> across the 10 percent line.
	/// </summary>
	// [BIN:TPW-EXE:0x004DF670 breakdown imminent] after the gauges are written: the byte of the new condition (+0x44, truncated) below 10.0 and that byte plus the step's wear (single) not below 10.0 post 0x42 (0x4DF8BE) through 0x59B590 and 0x59A940; the step already returns without wearing in game mode 1
	public static bool CrossesImminentLine( float conditionAfter, float wear )
	{
		var conditionByte = StaffVitals.Byte( conditionAfter );
		return conditionByte < ImminentBelow && (float)(wear + conditionByte) >= ImminentBelow;
	}

	/// <summary>
	/// The advice of a ride that broke down: 258 for a condemned one (its life gauge byte is 0: the condemn case of <c>0x4E0E60</c> sends the message of type 8), else, as the handler <c>0x4DADC0</c> reads the mechanics,
	/// 68 without a mechanic and 69 when every mechanic is busy elsewhere or resting, 70 when a mechanic is already on the way to this ride and the ride was not called for; null when the ride was called for or a mechanic is free (the dispatch sends him).
	/// </summary>
	// [BIN:TPW-EXE:0x004DADC0 breakdown handler] the list of mechanics (world +0x1DA73E) empty posts 0x44; else each mechanic: a target (+0x218) equal to the ride stops the walk (0x4DAEE1: 0x46 unless the ride's +0x64 is set), a free one (no target and a display state 0x5064F0 other than 3 or 4) stops it with no post, a busy one is passed; a walk that passes them all posts 0x45
	public static int? ForBreakdown( ParkEconomy economy, ParkObjectState ride )
	{
		if ( RideBreakdownRules.GaugeByte( (float)ride.LifeGauge ) == 0 )
			return AdvisorTables.RideCondemnedMessage;
		var mechanics = economy.Staff.OfType( StaffType.Mechanic ).ToList();
		if ( mechanics.Count == 0 )
			return NoMechanicsMessage;
		foreach ( var mechanic in mechanics )
		{
			// [BIN:TPW-EXE:0x004DAEE1 mechanic already on the ride] a mechanic whose target (+0x218) is this ride ends the walk: the ride's mRequestedService (+0x64, 0x4E03D0) set posts nothing, else the advice 0x46 (0x4DAF7E)
			if ( mechanic.AssignedInstanceId == ride.Id )
				return ride.MaintenanceRequested ? null : AdvisorTables.MechanicOnTheWayMessage;
			if ( mechanic.AssignedInstanceId == 0 && mechanic.State is not (StaffState.Resting or StaffState.OnStrike) )
				return null;
		}
		return BusyMechanicsMessage;
	}
}
