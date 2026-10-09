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
		Loans = standard.GetIndices( "LoanInfo", "LoanAmount" ).Select( index => new LoanOffer( index,
			standard.GetLong( $"LoanInfo[{index}].LoanAmount" ), standard.GetInt( $"LoanInfo[{index}].APRInPercent" ),
			standard.GetInt( $"LoanInfo[{index}].RepaymentPeriodInMonths" ), standard.GetInt( $"LoanInfo[{index}].Lendername" ) ) ).ToList();
		BaseWage = Grades( grade => standard.GetInt( $"PerGradeStaffConsts[{grade}].BaseWage" ) );
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
		Costs = new CellCosts( standard.GetInt( "Costs.QueueCell" ), standard.GetInt( "Costs.PathCell" ), standard.GetInt( "Costs.KartTrackCell" ),
			standard.GetInt( "Costs.WaterTrackCell" ), standard.GetInt( "Costs.MapCell" ) );
		EntryFee = new EntryFeePerception( standard.GetInt( "PeepInfo.ExcitementToCostDivisor" ), standard.GetInt( "PeepInfo.MinimumEntryFee" ),
			standard.GetDouble( "PeepInfo.CheapPriceMultiplier" ), standard.GetDouble( "PeepInfo.AveragePriceMultiplier" ), standard.GetDouble( "PeepInfo.ExpensivePriceMultiplier" ) );
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

	private static int[] Grades( Func<int, int> read ) => Enumerable.Range( 0, GradeCount ).Select( read ).ToArray();

	public string Theme { get; }
	public bool IsEasy { get; }
	public SamSettings Standard { get; }
	public long InitialCash { get; }
	public int InitialAdmissionFee { get; }
	public IReadOnlyList<LoanOffer> Loans { get; }
	public IReadOnlyList<int> BaseWage { get; }
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
	public EntryFeePerception EntryFee { get; }
	public GoldenTicketSettings GoldenTickets { get; }
	public ChallengeSchedule ChallengeTiming { get; }
	public IReadOnlyList<int> ChallengesInThisLevel { get; }
	public IReadOnlyDictionary<int, ChallengeDefinition> Challenges { get; }
	public int KeysToEnter { get; }
	public bool CanEarnTickets { get; }
	public bool CanSpendTickets { get; }

	public StaffRoleSettings this[StaffType type] => Roles[(int)type];

	/// <summary>
	/// Monthly wage: <c>PerGradeStaffConsts[grade].BaseWage × PerTypeStaffConsts[type].PayMultiplier</c>.
	/// The product is inferred from the key names (both are "pay" factors, one per grade and one per
	/// role); the original formula is unverified.
	/// </summary>
	// [APPROX:ECON-043] monthly wage = BaseWage[grade] x PayMultiplier[type] — evidence needed: staff list capture with grades
	public long GetMonthlyWage( StaffType type, int grade ) => (long)BaseWage[Math.Clamp( grade, 0, GradeCount - 1 )] * this[type].PayMultiplier;

	/// <summary>Original layer order for a theme: global Standard, theme Standard, then the theme's Easy_Standard in easy mode.</summary>
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
		var layers = GetStandardLayers( theme, easy ).Select( path => SamDocument.Parse( FileSystem.ReadAllText( path ), path ) );
		var challenges = FileSystem.FileExists( "/Challenges.sam" ) ? new SamSettings( new[] { SamDocument.Parse( FileSystem.ReadAllText( "/Challenges.sam" ), "/Challenges.sam" ) } ) : null;
		var globalPath = $"/levels/{theme}/global.sam";
		var global = FileSystem.FileExists( globalPath ) ? new SamSettings( new[] { SamDocument.Parse( FileSystem.ReadAllText( globalPath ), globalPath ) } ) : null;
		return new BalanceSettings( new SamSettings( layers ), challenges, global, theme, easy );
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
