namespace OpenTPW;

/// <summary>Staff roles in the order of <c>PerTypeStaffConsts[n]</c>, <c>STAFF_TYPES.str</c> and the name tables.</summary>
public enum StaffType
{
	Handyman = 0,
	Mechanic = 1,
	Entertainer = 2,
	Guard = 3,
	Researcher = 4
}

/// <summary>Research categories in the order of <c>ResearchCategories[n]</c> and object <c>Research.Category</c>.</summary>
public enum ResearchCategory
{
	Ride = 0,
	Shop = 1,
	Sideshow = 2,
	Feature = 3,
	Upgrade = 4
}

/// <summary><c>LoanInfo[n]</c>: one loan offer; <see cref="LenderNameIndex"/> indexes <c>LOANNAMES.str</c>.</summary>
public sealed record LoanOffer( int Index, long Amount, int AprPercent, int Months, int LenderNameIndex );

/// <summary>Per-role staff constants from <c>StaffPoolInfo</c>, <c>PerTypeStaffConsts</c> and <c>&lt;Role&gt;ConstsPerGrade</c>.</summary>
public sealed record StaffRoleSettings(
	StaffType Type,
	int ChanceToGetGreat,
	int AverageGrade,
	int BeginningNumberInPool,
	int MaximumInPool,
	int MinimumInPool,
	int MaximumInPark,
	int PayMultiplier,
	IReadOnlyList<int> WorkDuration,
	IReadOnlyList<int> PoundsPerTrainingPoint );

/// <summary>One <c>Challenges[n]</c> definition from <c>data/Challenges.sam</c>.</summary>
public sealed record ChallengeDefinition( int Index, int Type, int FollowupType, int TargetTime, int TargetValue, int TargetObject, int TargetObject2,
	int TargetStaffType, int Prize, bool CheckAtEndOnly, bool Independent );

/// <summary><c>GoldenTicketLocal</c> (per theme) and <c>GoldenTicketGlobal</c> (levels/Standard.sam) thresholds.</summary>
public sealed record GoldenTicketSettings( int Visitors, int PeopleInPark, int Happiness, int AtLeastThisManyHappyPeople, int ProfitYear,
	int RecentVisitors, int RecentVisitorMonths, int CoasterHeight, int GokartExcitement, int WaterLength, int MinCellsOwned, int MinCellsCovered );

/// <summary><c>Costs.*</c>: incidental building costs per cell.</summary>
public sealed record CellCosts( int QueueCell, int PathCell, int KartTrackCell, int WaterTrackCell, int MapCell );

/// <summary>Entry-fee perception constants (<c>PeepInfo</c>) for the guests slice.</summary>
public sealed record EntryFeePerception( int ExcitementToCostDivisor, int MinimumEntryFee, double CheapPriceMultiplier, double AveragePriceMultiplier, double ExpensivePriceMultiplier );

/// <summary><c>Challenges.*</c> scheduling constants (days).</summary>
public sealed record ChallengeSchedule( int DaysUntilFirstChallenge, int DaysAfterCompletedChallenge, int DaysAfterDeclinedChallenge, int DeclinesToForfeit, int ShortTimeLeftWarningAt );

/// <summary>
/// Typed view of the original balance settings used by the park simulation. Every value comes from
/// the layered <c>.sam</c> files (docs/ECONOMY.md lists the keys and how each is used); nothing in
/// here is a hard-coded balance number.
/// </summary>
public sealed class BalanceSettings
{
	public const int GradeCount = 5;
	public const int StaffTypeCount = 5;

	public BalanceSettings( SamSettings standard, SamSettings? challenges, SamSettings? themeGlobal, string theme, bool easy )
	{
		ArgumentNullException.ThrowIfNull( standard );
		Theme = theme;
		IsEasy = easy;
		Standard = standard;
		// [DATA:levels/Standard.sam:BankAccountInfo.* (layered)]
		InitialCash = standard.GetLong( "BankAccountInfo.InitialCash" );
		InitialAdmissionFee = standard.GetInt( "BankAccountInfo.InitialAdmissionFee" );
		Loans = ReadLoans( standard );
		BaseWage = Grades( grade => standard.GetInt( $"PerGradeStaffConsts[{grade}].BaseWage" ) );
		// [DATA:levels/Standard.sam:PerGradeStaffConsts[g].RecuperationRate/HappinessRecuperationRate, AllStaffConstants.RestLevel/HappyHitCosNoRestArea] read as singles: the rest step 0x100F3D8C adds the two rates at settings +756/+760 + 16 g to a staff member's energy and happiness, RestLevel (+740) and the missing-rest-area happiness hit (+744) are the words 0x100F4900 compares and subtracts
		RecuperationRate = Grades( grade => (float)standard.GetDouble( $"PerGradeStaffConsts[{grade}].RecuperationRate" ) );
		// [DATA:levels/Standard.sam:PerGradeStaffConsts[g].IdleDuration, HandymanConstsPerGrade[g].DetectionRange] the idle spell (0x00785330 + 16 × grade) and the handyman's search range (0x007853F0 + 12 × grade)
		IdleDuration = Grades( grade => standard.GetInt( $"PerGradeStaffConsts[{grade}].IdleDuration", 0, optional: true ) );
		HandymanDetectionRange = Grades( grade => standard.GetInt( $"HandymanConstsPerGrade[{grade}].DetectionRange", 0, optional: true ) );
		// [DATA:levels/Standard.sam:EntertainerConstsPerGrade[g].ActivationDistance] the square an entertainer looks around for guests before he performs (0x007853A0 + 16 × grade)
		EntertainerActivationDistance = Grades( grade => standard.GetInt( $"EntertainerConstsPerGrade[{grade}].ActivationDistance", 0, optional: true ) );
		// [DATA:levels/Standard.sam:FixedItemInfo.StrikeAreaStartX/Y, StrikeAreaSizeX/Y] the rectangle striking staff walk to (0x007855EC..0x007855F8)
		StrikeArea = (standard.GetInt( "FixedItemInfo.StrikeAreaStartX", 0, optional: true ), standard.GetInt( "FixedItemInfo.StrikeAreaStartY", 0, optional: true ),
			Math.Max( 1, standard.GetInt( "FixedItemInfo.StrikeAreaSizeX", 1, optional: true ) ), Math.Max( 1, standard.GetInt( "FixedItemInfo.StrikeAreaSizeY", 1, optional: true ) ));
		HappinessRecuperationRate = Grades( grade => (float)standard.GetDouble( $"PerGradeStaffConsts[{grade}].HappinessRecuperationRate" ) );
		RestLevel = standard.GetInt( "AllStaffConstants.RestLevel" );
		HappyHitCosNoRestArea = standard.GetInt( "AllStaffConstants.HappyHitCosNoRestArea" );
		ResearchAbility = Grades( grade => standard.GetInt( $"ResearcherConstsPerGrade[{grade}].ResearchAbility" ) );
		ResearchEffort = Enumerable.Range( 0, 5 ).Select( index => standard.GetInt( $"ResearchCategories[{index}].Effort" ) ).ToArray();
		ResearchTechPercentage = Grades( group => standard.GetInt( $"ResearchTech[{group}].PercentageForThisTech" ) );
		ResearchStartingWorkLoad = standard.GetInt( "Research.StartingWorkLoad" );
		BaseCostPerStaff = standard.GetInt( "StaffPoolInfo.BaseCostPerStaff" );
		CostPerQualityLevel = standard.GetInt( "StaffPoolInfo.CostPerQualityLevel" );
		TimeBetweenStaffUpdates = standard.GetInt( "StaffPoolInfo.TimeBetweenStaffUpdates" );
		MaxNumberOfStaffPerUpdate = standard.GetInt( "StaffPoolInfo.MaxNumberOfStaffPerUpdate" );
		StaffTimeoutTime = standard.GetInt( "StaffPoolInfo.StaffTimeoutTime" );
		Roles = new[]
		{
			Role( StaffType.Handyman, "Handymen", "Handyman" ),
			Role( StaffType.Mechanic, "Mechanics", "Mechanic" ),
			Role( StaffType.Entertainer, "Entertainers", "Entertainer" ),
			Role( StaffType.Guard, "Guards", "Guard" ),
			Role( StaffType.Researcher, "Researchers", "Researcher" )
		};
		Costs = ReadCosts( standard );
		EntryFee = ReadEntryFee( standard );
		GoldenTickets = new GoldenTicketSettings( standard.GetInt( "GoldenTicketLocal.Visitors" ), standard.GetInt( "GoldenTicketLocal.PeopleInPark" ),
			standard.GetInt( "GoldenTicketLocal.Happiness" ), standard.GetInt( "GoldenTicketLocal.AtLeastThisManyHappyPeople" ),
			standard.GetInt( "GoldenTicketLocal.ProfitYear" ), standard.GetInt( "GoldenTicketLocal.RecentVisitors" ), standard.GetInt( "GoldenTicketLocal.RecentVisitorMonths" ),
			standard.GetInt( "GoldenTicketGlobal.CoasterHeight" ), standard.GetInt( "GoldenTicketGlobal.GokartExcitement" ), standard.GetInt( "GoldenTicketGlobal.WaterLength" ),
			standard.GetInt( "GoldenTicketGlobal.MinCellsOwned" ), standard.GetInt( "GoldenTicketGlobal.MinCellsCovered" ) );
		ChallengeTiming = new ChallengeSchedule( standard.GetInt( "Challenges.DaysUntilFirstChallenge" ), standard.GetInt( "Challenges.DaysAfterCompletedChallenge" ),
			standard.GetInt( "Challenges.DaysAfterDeclinedChallenge" ), standard.GetInt( "Challenges.DeclinesToForfeit" ), standard.GetInt( "Challenges.ShortTimeLeftWarningAt" ) );
		ChallengesInThisLevel = standard.GetIndices( "ChallengesInThisLevel", "ChallengeType" )
			.Select( index => standard.GetInt( $"ChallengesInThisLevel[{index}].ChallengeType" ) ).ToList();
		Challenges = challenges == null ? new Dictionary<int, ChallengeDefinition>() : challenges.GetIndices( "Challenges", "Type" ).ToDictionary( index => index, index =>
			new ChallengeDefinition( index, challenges.GetInt( $"Challenges[{index}].Type" ), challenges.GetInt( $"Challenges[{index}].FollowupType" ),
				challenges.GetInt( $"Challenges[{index}].TargetTime" ), challenges.GetInt( $"Challenges[{index}].TargetVal" ),
				challenges.GetInt( $"Challenges[{index}].TargetObj" ), challenges.GetInt( $"Challenges[{index}].TargetObj2" ),
				challenges.GetInt( $"Challenges[{index}].TargetStaffType" ), challenges.GetInt( $"Challenges[{index}].Prize" ),
				challenges.GetInt( $"Challenges[{index}].CheckAtEndOnly" ) != 0, challenges.GetInt( $"Challenges[{index}].Independent" ) != 0 ) );
		KeysToEnter = themeGlobal?.GetInt( "Keys.CostToEnter" ) ?? 0;
		CanEarnTickets = themeGlobal == null || themeGlobal.GetInt( "Tickets.CanEarnTicketsInTheme" ) != 0;
		CanSpendTickets = themeGlobal == null || themeGlobal.GetInt( "Tickets.CanSpendTicketsInTheme" ) != 0;
		foreach ( var index in ChallengesInThisLevel )
		{
			if ( challenges != null && !Challenges.ContainsKey( index ) )
				throw new InvalidDataException( $"ChallengesInThisLevel refers to Challenges[{index}], which Challenges.sam does not define." );
		}

		StaffRoleSettings Role( StaffType type, string plural, string singular )
		{
			var prefix = $"{singular}ConstsPerGrade";
			return new StaffRoleSettings( type,
				standard.GetInt( $"StaffPoolInfo.ChanceToGetGreat{plural}" ), standard.GetInt( $"StaffPoolInfo.AvgGradeOf{plural}" ),
				standard.GetInt( $"StaffPoolInfo.BeginningNumberOf{plural}" ), standard.GetInt( $"StaffPoolInfo.Max{plural}" ),
				standard.GetInt( $"StaffPoolInfo.Min{plural}InPool" ), standard.GetInt( $"StaffPoolInfo.Max{plural}InPark" ),
				standard.GetInt( $"PerTypeStaffConsts[{(int)type}].PayMultiplier" ),
				Grades( grade => standard.GetInt( $"{prefix}[{grade}].WorkDuration" ) ),
				Grades( grade => standard.GetInt( $"{prefix}[{grade}].PoundsPerTrainingPoint" ) ) );
		}
	}

	private static T[] Grades<T>( Func<int, T> read ) => Enumerable.Range( 0, GradeCount ).Select( read ).ToArray();

	// The money keys Theme Park World and Theme Park Inc share (same names, same readers; docs/THEME-PARK-INC.md "Economy"): both ctors read them through these.

	/// <summary><c>LoanInfo[n]</c> offers; the array is read as far as <c>LoanAmount</c> is declared (Theme Park Inc declares ten slots, eight used).</summary>
	private static List<LoanOffer> ReadLoans( SamSettings standard ) => standard.GetIndices( "LoanInfo", "LoanAmount" ).Select( index => new LoanOffer( index,
		standard.GetLong( $"LoanInfo[{index}].LoanAmount" ), standard.GetInt( $"LoanInfo[{index}].APRInPercent" ),
		standard.GetInt( $"LoanInfo[{index}].RepaymentPeriodInMonths" ), standard.GetInt( $"LoanInfo[{index}].Lendername" ) ) ).ToList();

	/// <summary><c>Costs.*</c>, refused when negative.</summary>
	private static CellCosts ReadCosts( SamSettings standard )
	{
		int NonNegative( string key )
		{
			var value = standard.GetInt( key );
			return value >= 0 ? value : throw new InvalidDataException( $"Balance problem: invalid -ve value specified ({key} {value})" );
		}
		// [BIN:TPW-EXE:0x00401D7C balance parser] Costs.* are type-5 balance keys: the parser tests the parsed value and a negative one (jl 0x40260D) reports 'Balance problem: invalid -ve value specified', which fails the balance load, so the level never starts with a negative cell cost
		// [BIN:TPI-EXE:0x004289AC cost setter] the same four cell costs are read and applied by TPI's setter, which equals tp.exe 0x00408192
		return new CellCosts( NonNegative( "Costs.QueueCell" ), NonNegative( "Costs.PathCell" ), NonNegative( "Costs.KartTrackCell" ),
			NonNegative( "Costs.WaterTrackCell" ), NonNegative( "Costs.MapCell" ) );
	}

	/// <summary><c>PeepInfo</c>'s entry-fee perception constants. The divisor is data: levels/Standard.sam holds 4 in Theme Park World and 6 in Theme Park Inc, whose arabian layer sets 4.</summary>
	private static EntryFeePerception ReadEntryFee( SamSettings standard ) =>
		new( standard.GetInt( "PeepInfo.ExcitementToCostDivisor" ), standard.GetInt( "PeepInfo.MinimumEntryFee" ),
			standard.GetDouble( "PeepInfo.CheapPriceMultiplier" ), standard.GetDouble( "PeepInfo.AveragePriceMultiplier" ), standard.GetDouble( "PeepInfo.ExpensivePriceMultiplier" ) );

	/// <summary>
	/// The settings of a Theme Park Inc park: only the keys Theme Park Inc's own <c>levels/Standard.sam</c> (and the theme layer) carry. The money keys
	/// (bank, loans, <c>Costs.*</c>, entry-fee perception) are read as for Theme Park World; every staff, research, golden-ticket and challenge
	/// member is empty or zero, an inert placeholder and never a Theme Park World value: Theme Park Inc's file lacks 86 of the 152 fixed keys
	/// Theme Park World reads (docs/THEME-PARK-INC.md), no theme layer supplies them, and the systems that read those members stay refused
	/// (<see cref="EditionCapabilities"/>). A missing money key fails the load, as in Theme Park World.
	/// </summary>
	// [BIN:TPI-EXE:0x0041B350 balance getter] Theme Park Inc's Standard.sam schema (rows 0x009966C0) fills the block at 0x00A01A6C, a different layout from tp.exe's 0x0078504C; the money keys below keep tp.exe's names and meaning
	// [BIN:TPI-EXE:0x005BFBBA bank constructor] BankAccountInfo.InitialCash and InitialAdmissionFee fill the bank as tp.exe's constructor 0x004CF8C6 does, and LoanInfo[n] (a ten-slot array, eight used) fills the offers (0x005BFB97)
	private BalanceSettings( SamSettings standard, string theme )
	{
		Edition = GameEdition.ThemeParkInc;
		Theme = theme;
		IsEasy = false;
		Standard = standard;
		InitialCash = standard.GetLong( "BankAccountInfo.InitialCash" );
		InitialAdmissionFee = standard.GetInt( "BankAccountInfo.InitialAdmissionFee" );
		Loans = ReadLoans( standard );
		Costs = ReadCosts( standard );
		EntryFee = ReadEntryFee( standard );
		Weather = ThemeParkIncWeatherSettings.Read( standard );
		// inert placeholders: nothing reads them while staff, research and challenges are refused
		BaseWage = Array.Empty<int>();
		RecuperationRate = HappinessRecuperationRate = Array.Empty<float>();
		IdleDuration = HandymanDetectionRange = EntertainerActivationDistance = Array.Empty<int>();
		StrikeArea = (0, 0, 1, 1);
		ResearchAbility = ResearchEffort = ResearchTechPercentage = Array.Empty<int>();
		Roles = Array.Empty<StaffRoleSettings>();
		GoldenTickets = new GoldenTicketSettings( 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 );
		ChallengeTiming = new ChallengeSchedule( 0, 0, 0, 0, 0 );
		ChallengesInThisLevel = Array.Empty<int>();
		Challenges = new Dictionary<int, ChallengeDefinition>();
		CanEarnTickets = false;
		CanSpendTickets = false;
	}

	/// <summary>Which edition's keys these settings hold (<see cref="ForThemeParkInc"/> builds the Theme Park Inc subset).</summary>
	public GameEdition Edition { get; } = GameEdition.ThemeParkWorld;
	public string Theme { get; }
	public bool IsEasy { get; }
	public SamSettings Standard { get; }
	public long InitialCash { get; }
	public int InitialAdmissionFee { get; }
	public IReadOnlyList<LoanOffer> Loans { get; }
	public IReadOnlyList<int> BaseWage { get; }
	/// <summary>Energy a resting staff member regains per park turn, by grade (<c>PerGradeStaffConsts[g].RecuperationRate</c>).</summary>
	public IReadOnlyList<float> RecuperationRate { get; }
	/// <summary>Happiness a resting staff member regains per park turn, by grade (<c>PerGradeStaffConsts[g].HappinessRecuperationRate</c>).</summary>
	public IReadOnlyList<float> HappinessRecuperationRate { get; }
	/// <summary>Park turns a staff member idles before it searches again, by grade (<c>PerGradeStaffConsts[g].IdleDuration</c>).</summary>
	public IReadOnlyList<int> IdleDuration { get; }
	/// <summary>The cells a handyman looks around for litter and dirty toilets, by grade (<c>HandymanConstsPerGrade[g].DetectionRange</c>).</summary>
	public IReadOnlyList<int> HandymanDetectionRange { get; }
	/// <summary>The cells an entertainer looks around for guests before he performs, by grade (<c>EntertainerConstsPerGrade[g].ActivationDistance</c>).</summary>
	public IReadOnlyList<int> EntertainerActivationDistance { get; }
	/// <summary>The strike area outside the park (<c>FixedItemInfo.StrikeAreaStart/Size</c>): its first cell and its size in cells.</summary>
	public (int X, int Y, int Width, int Height) StrikeArea { get; }
	/// <summary><c>AllStaffConstants.RestLevel</c>: a staff member whose truncated energy byte is at most this seeks a rest area.</summary>
	public int RestLevel { get; }
	/// <summary><c>AllStaffConstants.HappyHitCosNoRestArea</c>: the happiness a tired staff member without a rest area to go to loses (one draw in sixteen).</summary>
	public int HappyHitCosNoRestArea { get; }
	public IReadOnlyList<int> ResearchAbility { get; }
	public IReadOnlyList<int> ResearchEffort { get; }
	public IReadOnlyList<int> ResearchTechPercentage { get; }
	public int ResearchStartingWorkLoad { get; }
	public int BaseCostPerStaff { get; }
	public int CostPerQualityLevel { get; }
	public int TimeBetweenStaffUpdates { get; }
	public int MaxNumberOfStaffPerUpdate { get; }
	public int StaffTimeoutTime { get; }
	public IReadOnlyList<StaffRoleSettings> Roles { get; }
	public CellCosts Costs { get; }
	/// <summary>Theme Park Inc's weather settings (null in Theme Park World and in a file with no weather).</summary>
	public ThemeParkIncWeatherSettings? Weather { get; }
	public EntryFeePerception EntryFee { get; }
	public GoldenTicketSettings GoldenTickets { get; }
	public ChallengeSchedule ChallengeTiming { get; }
	public IReadOnlyList<int> ChallengesInThisLevel { get; private set; }
	public IReadOnlyDictionary<int, ChallengeDefinition> Challenges { get; private set; }

	/// <summary>The challenge slots an original save loaded in place of the level's own (<see cref="AdoptChallenges"/>); null while the park plays the level's challenges.</summary>
	public IReadOnlyList<ChallengeDefinition>? AdoptedChallenges { get; private set; }

	/// <summary>
	/// Makes the challenges the slots of an original save: slot k holds <paramref name="slots"/>[k] (its definition, which an original load takes from the save and not from
	/// <c>Challenges.sam</c>), and the level's list becomes the slots' keys in order. A slot equal to the level's own definition keeps that definition's key, any other gets a key of its own.
	/// </summary>
	// [BIN:TPW-EXE:0x4d0f90 challenge slot serializer] each slot's type, time, value, objects, staff type, prize, follow-up type and the two flags are saved and loaded as they are (the definition of the challenge, not a reference to Challenges.sam)
	internal void AdoptChallenges( IReadOnlyList<ChallengeDefinition> slots )
	{
		ArgumentNullException.ThrowIfNull( slots );
		var definitions = new Dictionary<int, ChallengeDefinition>( Challenges );
		var order = new List<int>();
		var nextKey = definitions.Keys.DefaultIfEmpty( -1 ).Max() + 1;
		for ( var index = 0; index < slots.Count; index++ )
		{
			var slot = slots[index];
			var key = index < ChallengesInThisLevel.Count && Challenges.TryGetValue( ChallengesInThisLevel[index], out var own ) && Same( own, slot ) ? own.Index : nextKey++;
			definitions[key] = slot with { Index = key };
			order.Add( key );
		}
		var unchanged = order.SequenceEqual( ChallengesInThisLevel );
		Challenges = definitions;
		ChallengesInThisLevel = order;
		AdoptedChallenges = unchanged ? null : order.Select( key => definitions[key] ).ToList();
	}

	/// <summary>Takes the adopted challenge slots of an own park save back with the keys they were saved under (<see cref="AdoptedChallenges"/>).</summary>
	internal void RestoreAdoptedChallenges( IReadOnlyList<ChallengeDefinition> adopted )
	{
		ArgumentNullException.ThrowIfNull( adopted );
		var definitions = new Dictionary<int, ChallengeDefinition>( Challenges );
		foreach ( var definition in adopted )
			definitions[definition.Index] = definition;
		Challenges = definitions;
		ChallengesInThisLevel = adopted.Select( definition => definition.Index ).ToList();
		AdoptedChallenges = adopted.ToList();
	}

	private static bool Same( ChallengeDefinition a, ChallengeDefinition b ) => a with { Index = 0 } == b with { Index = 0 };
	public int KeysToEnter { get; }
	public bool CanEarnTickets { get; }
	public bool CanSpendTickets { get; }

	public StaffRoleSettings this[StaffType type] => Roles[(int)type];

	/// <summary>
	/// Monthly wage: <c>PerGradeStaffConsts[grade].BaseWage × PerTypeStaffConsts[type].PayMultiplier</c>.
	/// The product is read from the Mac routine <c>0xf46bc</c> and executed by the oracle (ORACLE.md F28): a 32-bit signed product.
	/// </summary>
	// [BIN:STP-PPC:0x100F46BC staff wage] monthly wage = PerGradeStaffConsts[grade].BaseWage (balance +748, stride 16) × PerTypeStaffConsts[type].PayMultiplier (+832, stride 4), unscaled, a 32-bit product (mullw; executed by the oracle target staff-wage-month, ORACLE.md F28)
	public long GetMonthlyWage( StaffType type, int grade ) => unchecked( BaseWage[Math.Clamp( grade, 0, GradeCount - 1 )] * this[type].PayMultiplier );

	/// <summary>Original layer order for a theme: global Standard, theme Standard, then the theme's Easy_Standard in easy mode.</summary>
	// [BIN:STP-PPC:0x1010474C world setup] game type 0/2 load levels/Standard.sam then <theme>/Standard.sam; type 2 (Instant Action) adds <theme>/Easy_Standard.sam and only logs when it is missing
	public static IReadOnlyList<string> GetStandardLayers( string theme, bool easy )
	{
		ValidateTheme( theme );
		var layers = new List<string> { "/levels/Standard.sam", $"/levels/{theme}/Standard.sam" };
		if ( easy )
			layers.Add( $"/levels/{theme}/Easy_Standard.sam" );
		return layers;
	}

	/// <summary>Loads the balance settings of an original theme from the game file system.</summary>
	public static BalanceSettings Load( string theme, bool easy )
	{
		// The original treats a missing Easy_Standard.sam as "not critical"; only jungle ships one.
		var layers = GetStandardLayers( theme, easy )
			.Where( path => !path.EndsWith( "/Easy_Standard.sam", StringComparison.Ordinal ) || FileSystem.FileExists( path ) )
			.Select( path => SamDocument.Parse( FileSystem.ReadAllText( path ), path ) );
		var challenges = FileSystem.FileExists( "/Challenges.sam" ) ? new SamSettings( new[] { SamDocument.Parse( FileSystem.ReadAllText( "/Challenges.sam" ), "/Challenges.sam" ) } ) : null;
		var globalPath = $"/levels/{theme}/global.sam";
		var global = FileSystem.FileExists( globalPath ) ? new SamSettings( new[] { SamDocument.Parse( FileSystem.ReadAllText( globalPath ), globalPath ) } ) : null;
		return new BalanceSettings( new SamSettings( layers ), challenges, global, theme, easy );
	}

	/// <summary>The Theme Park Inc money subset (see the private constructor) over explicit layers; the caller owns the layer order.</summary>
	public static BalanceSettings ForThemeParkInc( SamSettings standard, string theme )
	{
		ArgumentNullException.ThrowIfNull( standard );
		ValidateTheme( theme );
		return new BalanceSettings( standard, theme );
	}

	/// <summary>Loads the Theme Park Inc money settings of <paramref name="theme"/> (arabian, science, water): the global <c>levels/Standard.sam</c> under the theme's own.</summary>
	// [BIN:TPI-EXE:0x0041B350 balance getter] the global Standard.sam and the theme's are loaded in this order as in tp.exe; Theme Park Inc ships no Easy_Standard layer
	public static BalanceSettings LoadThemeParkInc( string theme )
	{
		var layers = GetStandardLayers( theme, easy: false ).Select( path => SamDocument.Parse( FileSystem.ReadAllText( path ), path ) );
		return ForThemeParkInc( new SamSettings( layers ), theme );
	}

	/// <summary>Whether the theme ships an easy-mode balance layer (only jungle in the available data).</summary>
	public static bool HasEasyLayer( string theme )
	{
		ValidateTheme( theme );
		return FileSystem.FileExists( $"/levels/{theme}/Easy_Standard.sam" );
	}

	private static void ValidateTheme( string theme )
	{
		if ( string.IsNullOrWhiteSpace( theme ) || theme.IndexOfAny( new[] { '/', '\\', '.' } ) >= 0 )
			throw new ArgumentException( "Theme names are plain level directory names such as 'jungle'.", nameof( theme ) );
	}
}
