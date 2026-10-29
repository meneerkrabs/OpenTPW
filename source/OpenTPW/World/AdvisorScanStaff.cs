namespace OpenTPW;

/// <summary>
/// The staff statistics the advisor's staff callbacks read from the park (the staff manager's counts and averages, the staff HQ's training record, the staff rooms and the visitor counter),
/// as the original's helpers <c>0x4C7CC0</c>, <c>0x4C9A10</c>, <c>0x4C9AA0</c>, <c>0x4C9B40</c>, <c>0x4C9C00</c>, <c>0x4C94C0</c>, <c>0x4C9750</c>, <c>0x4CA030</c> and <c>0x4CA0C0</c> return them.
/// A pure read of the park like <see cref="IAdvisorParkState"/>, which a park state also implements; a park state that does not leaves the staff callbacks at 0.
/// </summary>
internal interface IAdvisorStaffState
{
	/// <summary>Every staff member in the park (<c>0x004C9A10</c>: the things of the models 4 to 8).</summary>
	int StaffTotal { get; }
	/// <summary>The staff members with a patrol area set (<c>0x004C9B40</c>, <c>0x00506EB0</c>); <c>null</c> counts every role (the original's argument 0).</summary>
	int StaffWithPatrolArea( StaffType? role );
	/// <summary>The average training of a role (<c>0x004C9C00</c>): 20 x the grades added up / the members, a signed integer division, 0 without a member.</summary>
	int AverageTraining( StaffType role );
	/// <summary>The average happiness of the staff (<c>0x004C7CC0</c>), a single; 0 without staff.</summary>
	float AverageStaffHappiness { get; }
	/// <summary>The staff whose energy byte is below the rest level (<c>0x004C9AA0</c>, <c>0x00506680</c>).</summary>
	int TiredStaff { get; }
	/// <summary>The training budget word of a role in the staff HQ (<c>0x00519450</c> + <c>0xC</c> + 4 x the role's place).</summary>
	int TrainingBudget( StaffType role );
	/// <summary>The staff HQ's <c>mHaveEverTrained</c> byte (<c>+0x20</c> of the thing <c>0x00519450</c> returns).</summary>
	bool HaveEverTrained { get; }
	/// <summary>The visitors so far (<c>0x004C94C0</c>, the lifetime counter <c>+0x21C08</c>).</summary>
	int VisitorsEver { get; }
	/// <summary>The percent of the guests in the park whose happiness byte is below a level (<c>0x004C9750</c>).</summary>
	int GuestPercentBelowHappiness( int happiness );
	/// <summary>The staff rooms (<c>0x004CA030</c>: the attractions with the rest-area flag).</summary>
	int StaffRooms { get; }
	/// <summary>The closed staff rooms (<c>0x004CA0C0</c>).</summary>
	int ClosedStaffRooms { get; }
	/// <summary>The most staff of a role the park takes (the staff settings' <c>Max&lt;Role&gt;InPark</c> words <c>0x007852E8</c>-<c>0x007852F8</c>).</summary>
	int StaffLimit( StaffType role );
}

/// <summary>The staff statistics of the advisor's staff callbacks, computed from the economy without changing it.</summary>
internal sealed partial class EconomyParkState : IAdvisorStaffState
{
	// [BIN:TPW-EXE:0x004C9A10 all staff] the things of the models 4, 5, 6, 8 and 7, whatever their flag byte +3 (the role count 0x4C9960 tests it)
	public int StaffTotal => economy.Staff.Members.Count;

	// [BIN:TPW-EXE:0x004C9B40 staff with a patrol area] the staff things whose model equals the role (every one for the argument 0) and whose area is not the whole map (0x506EB0: the corner words +0x20A and +0x20C are not 1 and 0x4000)
	public int StaffWithPatrolArea( StaffType? role ) => economy.Staff.Members.Count( member => (role is null || member.Type == role) && member.HasPatrolArea );

	// [BIN:TPW-EXE:0x004C9C00 average training] the grades (+0x1E4) of the staff of the model are added up; the result is sum x 100 / (count x 5), a signed integer division, 0 without staff
	public int AverageTraining( StaffType role )
	{
		var count = 0;
		var sum = 0;
		foreach ( var member in economy.Staff.OfType( role ) )
		{
			count++;
			sum = unchecked( sum + member.Grade );
		}
		return count == 0 ? 0 : unchecked( sum * 100 ) / unchecked( count * 5 );
	}

	// [BIN:TPW-EXE:0x004C7CC0 average staff happiness] each member's happiness (+0x1F8) truncated to its low byte (0x67A830) is added in an integer sum; the sum divided by the count in the x87 (24 bits) as a single; a constant 0.0 (0x70031C) without staff
	public float AverageStaffHappiness
	{
		get
		{
			var count = 0;
			var sum = 0;
			foreach ( var member in economy.Staff.Members )
			{
				count++;
				sum = unchecked( sum + StaffVitals.Byte( member.Happiness ) );
			}
			return count == 0 ? 0f : (float)sum / count;
		}
	}

	// [BIN:TPW-EXE:0x004C9AA0 tired staff] the staff things for which 0x506680 holds: the energy (+0x1FC) truncated to its low byte is below the rest level word (0x785324, AllStaffConstants.RestLevel), a signed compare
	public int TiredStaff => economy.Staff.Members.Count( member => StaffVitals.IsBelowRestLevel( member.Energy, economy.Settings.RestLevel ) );

	// [BIN:TPW-EXE:0x0059D620 training budget] the staff HQ keeps one budget word per role from +0xC in the order handyman, mechanic, entertainer, guard, researcher (the training window stores them at 0x4B2534-0x4B25CB); the callbacks read the word of their role as a signed integer
	public int TrainingBudget( StaffType role ) => unchecked( (int)economy.Staff.GetTrainingBudget( role ) );

	// [BIN:TPW-EXE:0x0059FB10 ever trained] the byte +0x20 of the HQ's record, which the training window sets to 1 once a budget above 0 is stored (0x4B25D0) and nothing clears but the HQ's reset
	public bool HaveEverTrained => economy.Staff.HaveEverTrained;

	// [BIN:TPW-EXE:0x004C94C0 visitors ever] the park's lifetime visitor counter (+0x21C08), raised per admission: ParkCounters.Admissions
	public int VisitorsEver => unchecked( (int)economy.Counters[ParkCounters.Admissions] );

	// [BIN:TPW-EXE:0x004C9750 unhappy percent] the census guests whose happiness byte is below the level x 100 / the census guests
	public int GuestPercentBelowHappiness( int happiness ) => economy.GuestStatistics.CensusPercentBelowHappiness( happiness );

	private IEnumerable<ParkObjectState> StaffRoomObjects => economy.Objects.Where( item => economy.Catalog.TryGet( item.InfoId, out var info ) && info.ChillsYouOut );

	// [BIN:TPW-EXE:0x004CA030 staff rooms] the attractions (thing type 3) whose flag word +0x32 has bit 2 set: UsageInfo.ChillsYouOut, the rest-area flag the staff's rest search tests
	public int StaffRooms => StaffRoomObjects.Count();

	// [BIN:TPW-EXE:0x004CA0C0 closed staff rooms] the same attractions whose open word +0x68 is 0 (0x4E0440)
	public int ClosedStaffRooms => StaffRoomObjects.Count( IsClosed );

	// [BIN:TPW-EXE:0x005080A8 staff limits] the hire count of a role is compared with the words 0x7852E8 (handyman), 0x7852EC (mechanic), 0x7852F0 (entertainer), 0x7852F4 (guard) and 0x7852F8 (researcher): the schema's MaxHandymenInPark to MaxResearchersInPark of StaffPoolInfo
	public int StaffLimit( StaffType role ) => economy.Settings[role].MaximumInPark;
}

/// <summary>
/// The advisor's staff callbacks, ported from <c>tp.exe</c> (hiring and placing staff, training them, too many staff, the staff happy and trained, the staff room and the general staff lines),
/// by the address of the original's callback. Each reads the park through <see cref="IAdvisorParkState"/> and <see cref="IAdvisorStaffState"/> and its thresholds through
/// <see cref="AdvisorScanContext.Parameter"/>; a park that has no staff statistics, or a threshold the settings file lacks, leaves its callback at 0.
/// </summary>
internal static class AdvisorStaffCallbacks
{
	private static readonly (StaffType Role, string Area, string Train, string Lots, string Max, string MaxMore, string Congrat)[] RoleKeys =
	{
		(StaffType.Handyman, "StaffSetHandymanArea", "StaffTrainHandymen", "LotsHandyman", "MaxHandymen", "MaxHandymen.MoreThan", "CongratHandymenTrained"),
		(StaffType.Mechanic, "StaffSetMechanicArea", "StaffTrainMechanics", "LotsMechanics", "MaxMechanics", "MaxMechanics.MoreThan", "CongratMechanicsTrained"),
		(StaffType.Entertainer, "StaffSetEntertainerArea", "StaffTrainEntertainers", "LotsEntertainers", "MaxEntertainers", "MaxEntertainers.MoreThan", "CongratEntertainersTrained"),
		(StaffType.Guard, "StaffSetGuardArea", "StaffTrainGuards", "LotsSecurity", "MaxGuards", "MaxGuards.MoreThan", "CongratSecurityTrained"),
		(StaffType.Researcher, "", "StaffTrainResearchers", "LotsResearchers", "MaxResearchers", "MaxResearchers.MoreThan", "CongratResearchersTrained"),
	};

	private static string Minimum( StaffType role ) => role switch
	{
		StaffType.Handyman => "CongratHandymenTrained.MinimumHandymen",
		StaffType.Mechanic => "CongratMechanicsTrained.MinimumMechanics",
		StaffType.Entertainer => "CongratEntertainersTrained.MinimumEntertainers",
		StaffType.Guard => "CongratSecurityTrained.MinimumGuards",
		_ => "CongratResearchersTrained.MinimumResearchers",
	};

	/// <summary>The integer thresholds and multipliers these callbacks read (words of the <c>Advisor.sam</c> object); <c>WaitingTimes.DaysForStaff</c> is the park callbacks' key.</summary>
	public static readonly IReadOnlyList<string> IntegerKeys = new[]
	{
		"StaffHireGuards1.AtLeastThisBusy", "StaffHireGuards1.ScorePerExtraBusyPoint",
		"StaffHireGuards3.IdealDevelopedCellsPerGuard", "StaffHireGuards3.ScorePerExtra10Cells", "StaffHireGuards3.MinCellsForConsideration",
		"StaffHireEntertainers1.AtLeastThisBusy", "StaffHireEntertainers1.ScorePerExtraBusyPoint",
	}.Concat( RoleKeys.Where( entry => entry.Area.Length > 0 ).SelectMany( entry => new[]
	{
		$"{entry.Area}.MonthsIntoGameForConsideration", $"{entry.Area}.AtLeastThisMany", $"{entry.Area}.DontBotherIfPatrolledUp", $"{entry.Area}.ScorePerUnpatrolledPct",
	} ) ).Concat( RoleKeys.SelectMany( entry => new[]
	{
		$"{entry.Train}.WorseThan", $"{entry.Lots}.FudgeFactor", entry.MaxMore, Minimum( entry.Role ), $"{entry.Congrat}.BetterThan",
	} ) ).Concat( new[]
	{
		"CongratStaffHappy.MinimumStaff", "CongratStaffHappy.HappierThan", "NeedStaffRoom.PctOfStaffTired", "HireStaffGeneral.MinVisitorsEver",
		"SetPatrolsGeneral.MinStaffForMessage", "TrainStaffGeneral.MinStaffForMessage", "StaffroomClosed.MoreThanThisPercentClosed",
	} ).ToArray();

	/// <summary>
	/// The messages these callbacks produce with the key of the flat score they return, or of the multiplier of a computed one (the convention of <see cref="AdvisorTables.ScanScoreKeys"/>);
	/// the scan posts them, nothing else does.
	/// </summary>
	public static readonly IReadOnlyList<(int MessageId, string ScoreKey, int? FixedScore)> ScoreKeys = new (int, string, int?)[]
	{
		(11, "StaffHireGuards1.ScorePerExtraBusyPoint", null), (13, "StaffHireGuards3.ScorePerExtra10Cells", null), (14, "StaffHireEntertainers1.ScorePerExtraBusyPoint", null),
		(18, "StaffSetMechanicArea.ScorePerUnpatrolledPct", null), (20, "StaffSetHandymanArea.ScorePerUnpatrolledPct", null),
		(23, "StaffSetGuardArea.ScorePerUnpatrolledPct", null), (26, "StaffSetEntertainerArea.ScorePerUnpatrolledPct", null),
		(28, "StaffTrainMechanics.Score", null), (29, "StaffTrainHandymen.Score", null), (30, "StaffTrainGuards.Score", null),
		(31, "StaffTrainEntertainers.Score", null), (32, "StaffTrainResearchers.Score", null),
		(108, "LotsHandyman.Score", null), (109, "LotsEntertainers.Score", null), (110, "LotsMechanics.Score", null), (111, "LotsSecurity.Score", null), (112, "LotsResearchers.Score", null),
		(114, "MaxHandymen.Score", null), (115, "MaxHandymenV2.Score", null), (116, "MaxEntertainers.Score", null), (117, "MaxEntertainersV2.Score", null),
		(118, "MaxMechanics.Score", null), (119, "MaxMechanicsV2.Score", null), (120, "MaxGuards.Score", null), (121, "MaxGuardsV2.Score", null),
		(122, "MaxResearchers.Score", null), (123, "MaxResearchersV2.Score", null),
		(144, "CongratStaffHappy.Score", null),
		(146, "CongratSecurityTrained.Score", null), (147, "CongratResearchersTrained.Score", null), (148, "CongratHandymenTrained.Score", null),
		(149, "CongratEntertainersTrained.Score", null), (150, "CongratMechanicsTrained.Score", null),
		(169, "NeedStaffRoom.Score", null), (170, "HireStaffGeneral.Score", null), (171, "SetPatrolsGeneral.Score", null), (172, "TrainStaffGeneral.Score", null),
		(263, "StaffroomClosed.Score", null),
	};

	public const uint HireGuardsBusy = 0x59CCB0;
	public const uint CellsPerGuard = 0x59CD90;
	public const uint HireEntertainers = 0x59CE30;
	public const uint SetMechanicArea = 0x59D0C0;
	public const uint SetHandymanArea = 0x59D250;
	public const uint SetGuardArea = 0x59D400;
	public const uint SetEntertainerArea = 0x59D4C0;
	public const uint TrainMechanics = 0x59D620;
	public const uint TrainHandymen = 0x59D680;
	public const uint TrainGuards = 0x59D6E0;
	public const uint TrainEntertainers = 0x59D740;
	public const uint TrainResearchers = 0x59D7A0;
	public const uint LotsHandyman = 0x59EDD0;
	public const uint LotsEntertainers = 0x59EE20;
	public const uint LotsMechanics = 0x59EE70;
	public const uint LotsSecurity = 0x59EEC0;
	public const uint LotsResearchers = 0x59EF10;
	public const uint MaxHandymen = 0x59EFA0;
	public const uint MaxHandymenV2 = 0x59EFE0;
	public const uint MaxEntertainers = 0x59F020;
	public const uint MaxEntertainersV2 = 0x59F060;
	public const uint MaxMechanics = 0x59F0A0;
	public const uint MaxMechanicsV2 = 0x59F0E0;
	public const uint MaxGuards = 0x59F120;
	public const uint MaxGuardsV2 = 0x59F160;
	public const uint MaxResearchers = 0x59F1A0;
	public const uint MaxResearchersV2 = 0x59F1E0;
	public const uint StaffHappy = 0x59F590;
	public const uint GuardsTrained = 0x59F630;
	public const uint ResearchersTrained = 0x59F6A0;
	public const uint HandymenTrained = 0x59F710;
	public const uint EntertainersTrained = 0x59F780;
	public const uint MechanicsTrained = 0x59F7F0;
	public const uint NeedStaffRoom = 0x59F9C0;
	public const uint HireStaffGeneral = 0x59FA60;
	public const uint SetPatrolsGeneral = 0x59FAC0;
	public const uint TrainStaffGeneral = 0x59FB10;
	public const uint StaffRoomClosed = 0x5A0490;

	/// <summary>The staff callbacks by the address of the original's callback (the descriptor's word +0x10).</summary>
	public static readonly IReadOnlyDictionary<uint, AdvisorScoreCallback> Callbacks = new Dictionary<uint, AdvisorScoreCallback>
	{
		[HireGuardsBusy] = HireGuardsBusyScore,
		[CellsPerGuard] = CellsPerGuardScore,
		[HireEntertainers] = HireEntertainersScore,
		[SetMechanicArea] = context => UnpatrolledScore( context, StaffType.Mechanic, "StaffSetMechanicArea" ),
		[SetHandymanArea] = context => UnpatrolledScore( context, StaffType.Handyman, "StaffSetHandymanArea" ),
		[SetGuardArea] = context => UnpatrolledScore( context, StaffType.Guard, "StaffSetGuardArea" ),
		[SetEntertainerArea] = context => UnpatrolledScore( context, StaffType.Entertainer, "StaffSetEntertainerArea" ),
		[TrainMechanics] = context => UntrainedScore( context, StaffType.Mechanic, "StaffTrainMechanics" ),
		[TrainHandymen] = context => UntrainedScore( context, StaffType.Handyman, "StaffTrainHandymen" ),
		[TrainGuards] = context => UntrainedScore( context, StaffType.Guard, "StaffTrainGuards" ),
		[TrainEntertainers] = context => UntrainedScore( context, StaffType.Entertainer, "StaffTrainEntertainers" ),
		[TrainResearchers] = context => UntrainedScore( context, StaffType.Researcher, "StaffTrainResearchers" ),
		[LotsHandyman] = context => MoreStaffThanRides( context, StaffType.Handyman, "LotsHandyman" ),
		[LotsEntertainers] = context => MoreStaffThanRides( context, StaffType.Entertainer, "LotsEntertainers" ),
		[LotsMechanics] = context => MoreStaffThanRides( context, StaffType.Mechanic, "LotsMechanics" ),
		[LotsSecurity] = context => MoreStaffThanRides( context, StaffType.Guard, "LotsSecurity" ),
		[LotsResearchers] = context => MoreStaffThanRides( context, StaffType.Researcher, "LotsResearchers" ),
		[MaxHandymen] = context => ReachedTheLimit( context, StaffType.Handyman, "MaxHandymen.MoreThan", StaffType.Handyman, "MaxHandymen.MoreThan", "MaxHandymen.Score" ),
		[MaxHandymenV2] = context => ReachedTheLimitAgain( context, StaffType.Handyman, "MaxHandymen.MoreThan", "MaxHandymenV2.Score" ),
		[MaxEntertainers] = context => ReachedTheLimit( context, StaffType.Entertainer, "MaxEntertainers.MoreThan", StaffType.Entertainer, "MaxEntertainers.MoreThan", "MaxEntertainers.Score" ),
		[MaxEntertainersV2] = context => ReachedTheLimitAgain( context, StaffType.Entertainer, "MaxEntertainers.MoreThan", "MaxEntertainersV2.Score" ),
		[MaxMechanics] = context => ReachedTheLimit( context, StaffType.Mechanic, "MaxMechanics.MoreThan", StaffType.Mechanic, "MaxMechanics.MoreThan", "MaxMechanics.Score" ),
		[MaxMechanicsV2] = context => ReachedTheLimitAgain( context, StaffType.Mechanic, "MaxMechanics.MoreThan", "MaxMechanicsV2.Score" ),
		[MaxGuards] = context => ReachedTheLimit( context, StaffType.Guard, "MaxGuards.MoreThan", StaffType.Guard, "MaxGuards.MoreThan", "MaxGuards.Score" ),
		[MaxGuardsV2] = context => ReachedTheLimitAgain( context, StaffType.Guard, "MaxGuards.MoreThan", "MaxGuardsV2.Score" ),
		// [BIN:TPW-EXE:0x0059F1A0 researchers at the limit] the original compares the guards' limit (0x7852F4) with MaxGuards.MoreThan here and counts the researchers against MaxResearchers.MoreThan: a copy of the guards' callback with the count swapped
		[MaxResearchers] = context => ReachedTheLimit( context, StaffType.Guard, "MaxGuards.MoreThan", StaffType.Researcher, "MaxResearchers.MoreThan", "MaxResearchers.Score" ),
		[MaxResearchersV2] = context => ReachedTheLimitAgain( context, StaffType.Researcher, "MaxResearchers.MoreThan", "MaxResearchersV2.Score" ),
		[StaffHappy] = StaffHappyScore,
		[GuardsTrained] = context => TrainedScore( context, StaffType.Guard, "CongratSecurityTrained" ),
		[ResearchersTrained] = context => TrainedScore( context, StaffType.Researcher, "CongratResearchersTrained" ),
		[HandymenTrained] = context => TrainedScore( context, StaffType.Handyman, "CongratHandymenTrained" ),
		[EntertainersTrained] = context => TrainedScore( context, StaffType.Entertainer, "CongratEntertainersTrained" ),
		[MechanicsTrained] = context => TrainedScore( context, StaffType.Mechanic, "CongratMechanicsTrained" ),
		[NeedStaffRoom] = NeedStaffRoomScore,
		[HireStaffGeneral] = HireStaffGeneralScore,
		[SetPatrolsGeneral] = SetPatrolsGeneralScore,
		[TrainStaffGeneral] = TrainStaffGeneralScore,
		[StaffRoomClosed] = StaffRoomClosedScore,
	};

	/// <summary>The busy points (<c>0x004C81E0</c>): half the guests in the park, at most 100.</summary>
	// [BIN:TPW-EXE:0x004C81E0 busy points] 0x4C7FA0 (the guests on census cells, the cache refreshed for the argument 0) halved with a signed shift; below 0 gives 0, 100 or more gives 100
	public static int BusyPoints( int guestsInPark )
	{
		var half = guestsInPark / 2;
		return half < 0 ? 0 : half >= 100 ? 100 : half;
	}

	/// <summary>The park months of the clock (<c>0x004F8800</c> divided by 30 days with <c>__alldiv</c>), from the whole days.</summary>
	internal static int Months( IAdvisorParkState park ) => park.DaysSinceStart / 30;

	/// <summary>Reads the staff view of the park and the thresholds as the words they are; false when the park has no staff statistics or one key is missing.</summary>
	private static bool Read( AdvisorScanContext context, out IAdvisorParkState park, out IAdvisorStaffState staff, out int[] values, params string[] keys )
	{
		values = new int[keys.Length];
		park = context.Park!;
		staff = (context.Park as IAdvisorStaffState)!;
		if ( context.Park is not IAdvisorStaffState )
			return false;
		for ( var index = 0; index < keys.Length; index++ )
		{
			if ( context.Parameter( keys[index] ) is not { } value )
				return false;
			values[index] = (int)value;
		}
		return true;
	}

	// [BIN:TPW-EXE:0x0059CCB0 hire guards, busy] the park open; the days since the hire stamp (0x4C90D0) at least the word at +0x630 (WaitingTimes.DaysForStaff, signed); no guard (0x4C9960 of 7 above 0 gives 0); the busy points (0x4C81E0) minus the word at +0xF0 times the word at +0xF4, unclamped
	private static int HireGuardsBusyScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out _, out var v, "WaitingTimes.DaysForStaff", "StaffHireGuards1.AtLeastThisBusy", "StaffHireGuards1.ScorePerExtraBusyPoint" ) || !park.ParkOpen )
			return 0;
		if ( park.DaysSinceHireStamp < v[0] || park.StaffCount( StaffType.Guard ) > 0 )
			return 0;
		return unchecked( (BusyPoints( park.GuestsInPark ) - v[1]) * v[2] );
	}

	// [BIN:TPW-EXE:0x0059CD90 cells per guard] the park open; the days since the hire stamp at least the word at +0x630; the developed cells (0x4C81B0 of 0) at least the word at +0x108; cells minus guards (0x4C9960 of 7) times the word at +0x100 above 0: that times the word at +0x104
	private static int CellsPerGuardScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out _, out var v, "WaitingTimes.DaysForStaff", "StaffHireGuards3.MinCellsForConsideration", "StaffHireGuards3.IdealDevelopedCellsPerGuard", "StaffHireGuards3.ScorePerExtra10Cells" ) || !park.ParkOpen )
			return 0;
		if ( park.DaysSinceHireStamp < v[0] )
			return 0;
		var cells = park.DevelopedCells;
		if ( cells < v[1] )
			return 0;
		var extra = unchecked( cells - park.StaffCount( StaffType.Guard ) * v[2] );
		return extra > 0 ? unchecked( v[3] * extra ) : 0;
	}

	// [BIN:TPW-EXE:0x0059CE30 hire entertainers] the park open; no entertainer (0x4C9960 of 6 above 0 gives 0); the guests whose happiness byte is below 30 (0x4C9750 of 0x1E) as a percent of the census above 30; the busy points minus the word at +0x10C times the word at +0x110
	private static int HireEntertainersScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var staff, out var v, "StaffHireEntertainers1.AtLeastThisBusy", "StaffHireEntertainers1.ScorePerExtraBusyPoint" ) || !park.ParkOpen )
			return 0;
		if ( park.StaffCount( StaffType.Entertainer ) > 0 || staff.GuestPercentBelowHappiness( 30 ) <= 30 )
			return 0;
		return unchecked( (BusyPoints( park.GuestsInPark ) - v[0]) * v[1] );
	}

	// [BIN:TPW-EXE:0x0059D0C0 set a patrol area] 0x59D0C0 (mechanics, role 4), 0x59D250 (handymen, 5), 0x59D400 (guards, 7) and 0x59D4C0 (entertainers, 6) share the shape: the park's months (0x4F8800 / 30 days) at least the first word; the staff of the role (0x4C9960)
	// at least the second (a signed compare); the percent with a patrol area (0x4C9B40 x 100 / the staff, a signed divide) at most the third; the score is 100 minus that percent times the fourth. The original divides by the staff count even when it is 0 (only a second word of 0 or less reaches it): that is a fault there and 0 here
	private static int UnpatrolledScore( AdvisorScanContext context, StaffType role, string prefix )
	{
		if ( !Read( context, out var park, out var staff, out var v, $"{prefix}.MonthsIntoGameForConsideration", $"{prefix}.AtLeastThisMany", $"{prefix}.DontBotherIfPatrolledUp", $"{prefix}.ScorePerUnpatrolledPct" ) )
			return 0;
		if ( Months( park ) < v[0] )
			return 0;
		var count = park.StaffCount( role );
		if ( count < v[1] || count == 0 )
			return 0;
		var percent = unchecked( staff.StaffWithPatrolArea( role ) * 100 ) / count;
		return percent > v[2] ? 0 : unchecked( (100 - percent) * v[3] );
	}

	// [BIN:TPW-EXE:0x0059D620 train a role] 0x59D620 (mechanics), 0x59D680 (handymen), 0x59D6E0 (guards), 0x59D740 (entertainers) and 0x59D7A0 (researchers) share the shape: the role has staff (0x4C9960 not 0); its budget word in the staff HQ (0x519450 +0x10, +0xC, +0x18, +0x14, +0x1C) is not above 0;
	// the average training (0x4C9C00) is below the word WorseThan (a signed compare): the flat score
	private static int UntrainedScore( AdvisorScanContext context, StaffType role, string prefix )
	{
		if ( !Read( context, out var park, out var staff, out var v, $"{prefix}.WorseThan" ) )
			return 0;
		if ( park.StaffCount( role ) == 0 || staff.TrainingBudget( role ) > 0 )
			return 0;
		return staff.AverageTraining( role ) < v[0] ? context.Score( $"{prefix}.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059EDD0 too many staff] 0x59EDD0 (handymen, role 5), 0x59EE20 (entertainers, 6), 0x59EE70 (mechanics, 4), 0x59EEC0 (guards, 7) and 0x59EF10 (researchers, 8): the staff of the role above the rides (0x4CA8E0 of 0, 0) plus the word FudgeFactor: the flat score
	private static int MoreStaffThanRides( AdvisorScanContext context, StaffType role, string prefix )
	{
		if ( !Read( context, out var park, out _, out var v, $"{prefix}.FudgeFactor" ) )
			return 0;
		return park.StaffCount( role ) > unchecked( park.ObjectsOfKind( 0, 0 ) + v[0] ) ? context.Score( $"{prefix}.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059EFA0 at the limit] 0x59EFA0 (handymen), 0x59F020 (entertainers), 0x59F0A0 (mechanics), 0x59F120 (guards) and 0x59F1A0 (researchers): the role's limit word (0x7852E8, F0, EC, F4, F4 for the researchers: the original's copy slip) equals the word MoreThan and the staff of the role (0x4C9960)
	// are at least that word (a signed compare): the flat score. No park-open test
	private static int ReachedTheLimit( AdvisorScanContext context, StaffType limitRole, string limitKey, StaffType countRole, string countKey, string scoreKey )
	{
		if ( !Read( context, out var park, out var staff, out var v, limitKey, countKey ) )
			return 0;
		return staff.StaffLimit( limitRole ) == v[0] && park.StaffCount( countRole ) >= v[1] ? context.Score( scoreKey ) : 0;
	}

	// [BIN:TPW-EXE:0x0059EFE0 at the limit again] 0x59EFE0, 0x59F060, 0x59F0E0, 0x59F160 and 0x59F1E0: the limit word is not the word MoreThan (the first line's), and the staff of the role equal the limit exactly: the second flat score
	private static int ReachedTheLimitAgain( AdvisorScanContext context, StaffType role, string moreKey, string scoreKey )
	{
		if ( !Read( context, out var park, out var staff, out var v, moreKey ) )
			return 0;
		var limit = staff.StaffLimit( role );
		return limit != v[0] && park.StaffCount( role ) == limit ? context.Score( scoreKey ) : 0;
	}

	// [BIN:TPW-EXE:0x0059F590 staff happy] all the staff (0x4C9A10) at least the word MinimumStaff (+0x42C, signed); the staff's mean happiness (0x4C7CC0, a single) at least the word HappierThan (fild, fcompp: only a larger word stops it): the flat score
	private static int StaffHappyScore( AdvisorScanContext context )
	{
		if ( !Read( context, out _, out var staff, out var v, "CongratStaffHappy.MinimumStaff", "CongratStaffHappy.HappierThan" ) || staff.StaffTotal < v[0] )
			return 0;
		return staff.AverageStaffHappiness >= (float)v[1] ? context.Score( "CongratStaffHappy.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059F630 staff trained] 0x59F630 (guards, +0x18), 0x59F6A0 (researchers, +0x1C), 0x59F710 (handymen, +0xC), 0x59F780 (entertainers, +0x14) and 0x59F7F0 (mechanics, +0x10): the role's budget word in the staff HQ above 0; the staff of the role above the word Minimum (0x4C9960, signed); the average training (0x4C9C00) above the word BetterThan: the flat score
	private static int TrainedScore( AdvisorScanContext context, StaffType role, string prefix )
	{
		if ( !Read( context, out var park, out var staff, out var v, Minimum( role ), $"{prefix}.BetterThan" ) )
			return 0;
		if ( staff.TrainingBudget( role ) <= 0 || park.StaffCount( role ) <= v[0] )
			return 0;
		return staff.AverageTraining( role ) > v[1] ? context.Score( $"{prefix}.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059F9C0 need a staff room] the park's months (0x4F8800 / 30 days) at least 3; the tired staff (0x4C9AA0) x 100 / all the staff (0x4C9A10, signed; none gives 0) above the word PctOfStaffTired (+0x4B8): the flat score
	private static int NeedStaffRoomScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var staff, out var v, "NeedStaffRoom.PctOfStaffTired" ) || Months( park ) < 3 )
			return 0;
		var tired = staff.TiredStaff;
		var total = staff.StaffTotal;
		return total != 0 && unchecked( tired * 100 ) / total > v[0] ? context.Score( "NeedStaffRoom.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059FA60 hire staff] the park open; the visitors so far (0x4C94C0) at least the word MinVisitorsEver (+0x4C4, signed); no staff at all (0x4C9A10): the flat score
	private static int HireStaffGeneralScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var staff, out var v, "HireStaffGeneral.MinVisitorsEver" ) || !park.ParkOpen )
			return 0;
		return staff.VisitorsEver >= v[0] && staff.StaffTotal == 0 ? context.Score( "HireStaffGeneral.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059FAC0 set patrols] all the staff (0x4C9A10) above the word MinStaffForMessage (+0x4C8, signed) and none of them with a patrol area (0x4C9B40 of 0, every role): the flat score
	private static int SetPatrolsGeneralScore( AdvisorScanContext context )
	{
		if ( !Read( context, out _, out var staff, out var v, "SetPatrolsGeneral.MinStaffForMessage" ) )
			return 0;
		return staff.StaffTotal > v[0] && staff.StaffWithPatrolArea( null ) == 0 ? context.Score( "SetPatrolsGeneral.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059FB10 train staff] all the staff (0x4C9A10) above the word MinStaffForMessage (+0x4D0, signed) and the staff HQ's mHaveEverTrained byte (0x519450 +0x20) clear: the flat score
	private static int TrainStaffGeneralScore( AdvisorScanContext context )
	{
		if ( !Read( context, out _, out var staff, out var v, "TrainStaffGeneral.MinStaffForMessage" ) )
			return 0;
		return staff.StaffTotal > v[0] && !staff.HaveEverTrained ? context.Score( "TrainStaffGeneral.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x005A0490 staff rooms closed] staff rooms (0x4CA030) above 0; the closed ones (0x4CA0C0) x 100 / the staff rooms (signed) above the word MoreThanThisPercentClosed (+0x5A0): the flat score. No park-open test
	private static int StaffRoomClosedScore( AdvisorScanContext context )
	{
		if ( !Read( context, out _, out var staff, out var v, "StaffroomClosed.MoreThanThisPercentClosed" ) )
			return 0;
		var rooms = staff.StaffRooms;
		return rooms != 0 && unchecked( staff.ClosedStaffRooms * 100 ) / rooms > v[0] ? context.Score( "StaffroomClosed.Score" ) : 0;
	}
}
