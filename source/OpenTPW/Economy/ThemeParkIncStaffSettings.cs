namespace OpenTPW;

/// <summary>
/// Theme Park Inc's staff roles: Theme Park World's five in the same order, and the gardener (docs/reverse/TPI-STAFF.md).
/// </summary>
// [BIN:TPI-EXE:0x631C38 staff roles] the pool switch compares the roles 0 to 5, the candidate jump table 0x6306B4 has six cases and 0x630635 refuses a
// larger one ("Trying to generate a non-existent staff type"); STAFF_TYPES.str names them Cleaner, Engineer, Entertainer, Guard, Scientist and Gardener
public enum ThemeParkIncStaffRole
{
	Handyman = 0,
	Mechanic = 1,
	Entertainer = 2,
	Guard = 3,
	Researcher = 4,
	Gardener = 5
}

/// <summary>One <c>StaffAgencies[a]</c> row (36 bytes in the parsed block at 0xA0240C).</summary>
public sealed record ThemeParkIncStaffAgency( int RequiredPromotionLevel, int MotivationGainModifier, int MotivationLossModifier, int RecuperationModifier,
	int HappinessRecuperationModifier, int InitialWageModifier, int InitialExperienceModifier, int ExperienceGainModifier, int TrainingExperienceGainModifier );

/// <summary>A role's experience-driven constants (<c>&lt;Role&gt;.*</c> and <c>&lt;Role&gt;ConstsPerGrade[0].ExperiencePerMonth</c>).</summary>
public sealed record ThemeParkIncStaffRoleSettings( ThemeParkIncStaffRole Role, int MaxWage, int MinWorkDuration, int MaxWorkDuration, int MinRange, int MaxRange,
	int ExperiencePerMonth, int MaxInPool, int MinInPool, int MaxInPark, int BeginningNumber, int AverageExperience );

/// <summary>
/// Theme Park Inc's staff settings: the keys its executable reads (docs/reverse/TPI-STAFF.md, section 2). Grades are gone, so none of Theme Park World's
/// grade-indexed keys is read; experience, wages, agencies and strikes take their place.
/// </summary>
public sealed class ThemeParkIncStaffSettings
{
	/// <summary>The staff settings of a Theme Park Inc <c>Standard.sam</c>, or null when it lacks the staff keys (a synthetic or partial file).</summary>
	public static ThemeParkIncStaffSettings? Read( SamSettings standard ) =>
		standard.Contains( "AllStaffConstants.MaxExperience" ) && standard.Contains( "StaffAgencies[0].RequiredPromotionLevel" ) ? new ThemeParkIncStaffSettings( standard ) : null;

	public ThemeParkIncStaffSettings( SamSettings standard )
	{
		ArgumentNullException.ThrowIfNull( standard );
		int Int( string key ) => standard.GetInt( key );
		float Single( string key ) => (float)standard.GetDouble( key );
		// [BIN:TPI-EXE:0x631B97 pool keys] StaffPoolInfo.TimeBetweenStaffUpdates (0xA0221C), MaxNumberOfStaffPerUpdate (0xA02220) and StaffTimeoutTime (0xA02224), read as Theme Park World reads its own
		TimeBetweenStaffUpdates = Int( "StaffPoolInfo.TimeBetweenStaffUpdates" );
		MaxNumberOfStaffPerUpdate = Int( "StaffPoolInfo.MaxNumberOfStaffPerUpdate" );
		StaffTimeoutTime = Int( "StaffPoolInfo.StaffTimeoutTime" );
		AverageWageDifference = Int( "StaffPoolInfo.AvgWageDifference" );
		MaxExperience = Int( "AllStaffConstants.MaxExperience" );
		RestLevel = Int( "AllStaffConstants.RestLevel" );
		MinWage = Int( "AllStaffConstants.MinWage" );
		UnhappyWageValue = Int( "AllStaffConstants.UnHappyWageVal" );
		HappyWageValue = Int( "AllStaffConstants.HappyWageVal" );
		LowestWageHappiness = Int( "AllStaffConstants.LowestWageHappiness" );
		PercentageForBadWageDecision = Int( "AllStaffConstants.PercentageForBadWageDecision" );
		MotivationFactor = Int( "AllStaffConstants.MotivationFactor" );
		StaffSlackingEnergy = Int( "AllStaffConstants.StaffSlackingEnergy" );
		MinDistanceForSlacking = Int( "AllStaffConstants.MinDistanceForSlacking" );
		StaffRoomCapacity = Int( "AllStaffConstants.StaffRoomCapacity" );
		MaxSpeedDifference = Int( "AllStaffConstants.MaxSpeedDifference" );
		TrainingExtraExperience = Int( "AllStaffConstants.TrainingExtraExperience" );
		MinExperienceForTraining = Int( "AllStaffConstants.MinExperienceForTraining" );
		SkillIncrement = Int( "AllStaffConstants.SkillIncrement" );
		ProgressUpperLimit = Int( "AllStaffConstants.ProgessUpperLimit" );
		HappyHitNoRestArea = Int( "AllStaffConstants.HappyHitCosNoRestArea" );
		HappyHitRoomInaccessible = Int( "AllStaffConstants.HappyHitCosRoomInaccessible" );
		MinRecuperationRate = Single( "AllStaffConstants.MinRecuperationRate" );
		MaxRecuperationRate = Single( "AllStaffConstants.MaxRecuperationRate" );
		MinHappinessRecuperationRate = Single( "AllStaffConstants.MinHappinessRecuperationRate" );
		MaxHappinessRecuperationRate = Single( "AllStaffConstants.MaxHappinessRecuperationRate" );
		// [BIN:TPI-EXE:0x633740 strike keys] Strike.MinMonthsForStrike (0x633117), MinStaffOfTypeForStrike and the three meter thresholds
		MinMonthsForStrike = Int( "Strike.MinMonthsForStrike" );
		MinStaffOfTypeForStrike = Int( "Strike.MinStaffOfTypeForStrike" );
		EnergyPercentToStrike = Int( "Strike.EnergyPercentToStrike" );
		HappyPercentToStrike = Int( "Strike.HappyPercentToStrike" );
		MotivationPercentToStrike = Int( "Strike.MotivationPercentToStrike" );
		// [BIN:TPI-EXE:0x59ACA0 offer range] the wage window's offer box takes AllStaffConstants.MinWage (0xA02280) and MaxWage (0xA02284) as its range and WageStep (0xA02288) as its step
		MaxWage = Int( "AllStaffConstants.MaxWage" );
		WageStep = Int( "AllStaffConstants.WageStep" );
		// [BIN:TPI-EXE:0x62BC03 strike area] the strike walk reads FixedItemInfo.StrikeAreaStartX/Y (0xA02600, 0xA02604) and StrikeAreaSizeX/Y (0xA02608, 0xA0260C), the theme's Standard.sam;
		// [BIN:TPI-EXE:0x62A5D1 park entrance] a striker's way back takes FixedItemInfo.EntranceAPosX/Y (0xA025D0, 0xA025D4). A file without them leaves the reset block's 0 (the strike walk then
		// divides by zero: ThemeParkIncStaffWorld refuses it)
		StrikeArea = (standard.GetInt( "FixedItemInfo.StrikeAreaStartX", 0, optional: true ), standard.GetInt( "FixedItemInfo.StrikeAreaStartY", 0, optional: true ),
			standard.GetInt( "FixedItemInfo.StrikeAreaSizeX", 0, optional: true ), standard.GetInt( "FixedItemInfo.StrikeAreaSizeY", 0, optional: true ));
		EntranceA = (standard.GetInt( "FixedItemInfo.EntranceAPosX", 0, optional: true ), standard.GetInt( "FixedItemInfo.EntranceAPosY", 0, optional: true ));
		var agencies = new List<ThemeParkIncStaffAgency>();
		foreach ( var index in standard.GetIndices( "StaffAgencies", "RequiredPromotionLevel" ) )
		{
			string Key( string field ) => $"StaffAgencies[{index}].{field}";
			agencies.Add( new ThemeParkIncStaffAgency( Int( Key( "RequiredPromotionLevel" ) ), Int( Key( "MotivationGainModifier" ) ), Int( Key( "MotivationLossModifier" ) ),
				Int( Key( "RecuperationModifier" ) ), Int( Key( "HappinessRecuperationModifier" ) ), Int( Key( "InitialWageModifier" ) ), Int( Key( "InitialExperienceModifier" ) ),
				Int( Key( "ExperienceGainModifier" ) ), Int( Key( "TrainingExperienceGainModifier" ) ) ) );
		}
		Agencies = agencies;
		// [BIN:TPI-EXE:0x631C12 gardener pool keys] the gardener takes the handyman's pool keys: MaxHandymen (0x631C4A), MinHandymenInPool (0x631792),
		// BeginningNumberOfHandymen (0x631FFE), AvgExperienceOfHandymen (0x630390) and the handyman's ExperiencePerMonth (0x629774); MaxGardeners and
		// MinGardenersInPool are read by nothing, MaxGardenersInPark is (0x631551)
		ThemeParkIncStaffRoleSettings Role( ThemeParkIncStaffRole role, string section, string plural, string pool, string grade, string minRange, string maxRange ) =>
			new( role, Int( $"{section}.MaxWage" ), Int( $"{section}.MinWorkDuration" ), Int( $"{section}.MaxWorkDuration" ),
				minRange.Length > 0 ? Int( $"{section}.{minRange}" ) : 0, maxRange.Length > 0 ? Int( $"{section}.{maxRange}" ) : 0,
				Int( $"{grade}ConstsPerGrade[0].ExperiencePerMonth" ), Int( $"StaffPoolInfo.Max{pool}" ), Int( $"StaffPoolInfo.Min{pool}InPool" ),
				Int( $"StaffPoolInfo.Max{plural}InPark" ), Int( $"StaffPoolInfo.BeginningNumberOf{pool}" ), Int( $"StaffPoolInfo.AvgExperienceOf{pool}" ) );
		// [BIN:TPI-EXE:0x62E4C0 work duration pairs] the researcher's pair is Researcher.Min/MaxResearchAbility; Researcher.Min/MaxWorkDuration are read by nothing
		var researcher = Role( ThemeParkIncStaffRole.Researcher, "Researcher", "Researchers", "Researchers", "Researcher", "", "" );
		Roles = new Dictionary<ThemeParkIncStaffRole, ThemeParkIncStaffRoleSettings>
		{
			[ThemeParkIncStaffRole.Handyman] = Role( ThemeParkIncStaffRole.Handyman, "Handyman", "Handymen", "Handymen", "Handyman", "MinDetectionRange", "MaxDetectionRange" ),
			[ThemeParkIncStaffRole.Mechanic] = Role( ThemeParkIncStaffRole.Mechanic, "Mechanic", "Mechanics", "Mechanics", "Mechanic", "", "" ),
			[ThemeParkIncStaffRole.Entertainer] = Role( ThemeParkIncStaffRole.Entertainer, "Entertainer", "Entertainers", "Entertainers", "Entertainer", "MinActiveRange", "MaxActiveRange" ),
			[ThemeParkIncStaffRole.Guard] = Role( ThemeParkIncStaffRole.Guard, "Guard", "Guards", "Guards", "Guard", "MinDetectionRange", "MaxDetectionRange" ),
			[ThemeParkIncStaffRole.Researcher] = researcher with { MinWorkDuration = Int( "Researcher.MinResearchAbility" ), MaxWorkDuration = Int( "Researcher.MaxResearchAbility" ) },
			[ThemeParkIncStaffRole.Gardener] = Role( ThemeParkIncStaffRole.Gardener, "Gardener", "Gardeners", "Handymen", "Handyman", "MinDetectionRange", "MaxDetectionRange" ),
		};
	}

	public int TimeBetweenStaffUpdates { get; }
	public int MaxNumberOfStaffPerUpdate { get; }
	public int StaffTimeoutTime { get; }
	public int AverageWageDifference { get; }
	public int MaxExperience { get; }
	public int RestLevel { get; }
	public int MinWage { get; }
	public int UnhappyWageValue { get; }
	public int HappyWageValue { get; }
	public int LowestWageHappiness { get; }
	public int PercentageForBadWageDecision { get; }
	public int MotivationFactor { get; }
	public int StaffSlackingEnergy { get; }
	public int MinDistanceForSlacking { get; }
	public int StaffRoomCapacity { get; }
	public int MaxSpeedDifference { get; }
	public int TrainingExtraExperience { get; }
	public int MinExperienceForTraining { get; }
	public int SkillIncrement { get; }
	public int ProgressUpperLimit { get; }
	public int HappyHitNoRestArea { get; }
	public int HappyHitRoomInaccessible { get; }
	public float MinRecuperationRate { get; }
	public float MaxRecuperationRate { get; }
	public float MinHappinessRecuperationRate { get; }
	public float MaxHappinessRecuperationRate { get; }
	/// <summary><c>AllStaffConstants.MaxWage</c>: the wage window's highest offer.</summary>
	public int MaxWage { get; }
	/// <summary><c>AllStaffConstants.WageStep</c>: the wage window's offer step.</summary>
	public int WageStep { get; }
	/// <summary><c>FixedItemInfo.StrikeAreaStartX/Y</c> and <c>StrikeAreaSizeX/Y</c>: the cells a striker walks to.</summary>
	public (int X, int Y, int Width, int Height) StrikeArea { get; }
	/// <summary><c>FixedItemInfo.EntranceAPosX/Y</c>: the cell a striker walks back to when his strike is over.</summary>
	public (int X, int Y) EntranceA { get; }
	public int MinMonthsForStrike { get; }
	public int MinStaffOfTypeForStrike { get; }
	public int EnergyPercentToStrike { get; }
	public int HappyPercentToStrike { get; }
	public int MotivationPercentToStrike { get; }
	public IReadOnlyList<ThemeParkIncStaffAgency> Agencies { get; }
	public IReadOnlyDictionary<ThemeParkIncStaffRole, ThemeParkIncStaffRoleSettings> Roles { get; }

	/// <summary>
	/// The executable's interpolation helper: <paramref name="a"/> below <paramref name="low"/>, <paramref name="b"/> above <paramref name="high"/>, and in between
	/// <c>((0x4000 − t)·a + t·b) / 16384</c> rounded toward zero with <c>t = (x − low) &lt;&lt; 14 / (high − low)</c> (signed division).
	/// </summary>
	// [BIN:TPI-EXE:0x62C900 staff lerp] lerp(lo, hi, x, a, b): x > hi gives b; a negative x - lo gives a; else t = (x - lo) << 14 / (hi - lo) by idiv and
	// ((0x4000 - t) * a + t * b) / 16384 toward zero (cltd, and 0x3fff, add, sar 14); hi == lo with x == lo divides by zero there as here
	public static int Lerp( int x, int low, int high, int a, int b )
	{
		if ( x > high )
			return b;
		var offset = unchecked( x - low );
		if ( offset < 0 )
			return a;
		var t = unchecked( offset << 14 ) / unchecked( high - low );
		return unchecked( (0x4000 - t) * a + t * b ) / 16384;
	}

	/// <summary>
	/// The wage a member of <paramref name="experience"/> expects for a role whose top wage is <paramref name="maxWage"/>: proportional to experience, cut to a
	/// multiple of 20, at least 10; the top wage above the maximum experience and 0 below zero.
	/// </summary>
	// [BIN:TPI-EXE:0x62CD30 expected wage] e > MaxExp takes M, a negative e takes 0, else w = ((e << 14) / MaxExp * M) / 16384 toward zero; every branch then
	// takes off w % 20 (signed) and answers at least 10, so a negative experience still expects 10
	public int ExpectedWage( int experience, int maxWage )
	{
		int wage;
		if ( experience > MaxExperience )
			wage = maxWage;
		else if ( experience < 0 )
			wage = 0;
		else
			wage = unchecked( (experience << 14) / MaxExperience * maxWage ) / 16384;
		wage -= wage % 20;
		return wage < 10 ? 10 : wage;
	}

	/// <summary>
	/// A member's work duration (the researcher's research ability): experience less the motivation's shortfall times <see cref="MotivationFactor"/>, at least 1,
	/// interpolated over the role's pair; the mechanic and handyman pairs run the inverse form. A member paid below 10 gets 1, or 200 in the inverse form.
	/// </summary>
	// [BIN:TPI-EXE:0x62E4C0 work duration] e = exp - (100 - (trunc(motivation) & 0xFF)) * MotivationFactor, 1 when not positive; the kind table 0x62E64C picks the pair:
	// mechanic Mechanic.Min/MaxWorkDuration and handyman Handyman.* in the inverse form, entertainer, guard and gardener their own pair and the researcher
	// Researcher.Min/MaxResearchAbility in the normal one; a salary below 10 (unsigned) answers 1, or 200 in the inverse form (0x62E57C, 0x62E5DC). Normal: e > MaxExp
	// gives Max, else lerp; inverse: e > MaxExp gives Max, else Min - ((e << 14) / MaxExp * (Min - Max)) / 16384 toward zero
	public int WorkDuration( ThemeParkIncStaffRole role, int experience, float motivation, int salary )
	{
		var settings = Roles[role];
		var inverse = role is ThemeParkIncStaffRole.Mechanic or ThemeParkIncStaffRole.Handyman;
		var effective = unchecked( experience - (100 - ((int)motivation & 0xFF)) * MotivationFactor );
		if ( effective <= 0 )
			effective = 1;
		if ( (uint)salary < 10 )
			return inverse ? 200 : 1;
		if ( !inverse )
			return effective > MaxExperience ? settings.MaxWorkDuration : Lerp( effective, 0, MaxExperience, settings.MinWorkDuration, settings.MaxWorkDuration );
		var spread = settings.MinWorkDuration - settings.MaxWorkDuration;
		if ( effective > MaxExperience )
			return settings.MinWorkDuration - spread;
		return settings.MinWorkDuration - unchecked( (effective << 14) / MaxExperience * spread ) / 16384;
	}

	/// <summary>
	/// The agencies a candidate can come from at <paramref name="promotionLevel"/>: the leading rows whose required promotion level the player has reached.
	/// </summary>
	// [BIN:TPI-EXE:0x6302CB candidate agency] n = the number of leading StaffAgencies rows with RequiredPromotionLevel <= the career rank (0x42A3B0() + 0x1060); the agency is rand() % n
	public int AvailableAgencies( int promotionLevel )
	{
		var count = 0;
		while ( count < Agencies.Count && Agencies[count].RequiredPromotionLevel <= promotionLevel )
			count++;
		return count;
	}
}
