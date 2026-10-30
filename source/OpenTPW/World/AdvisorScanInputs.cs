namespace OpenTPW;

/// <summary>
/// The park inputs of the advisor's last nine scan callbacks (the guard and prank lines 12, 22, 98 and 264, the patrol coverage lines 19, 21 and 27, the unconnected queue line 113 and the
/// ride excitement line 141): the guards' chase counters, the patrol rectangles' coverage of a class of cells, the first queue that joins no path and the mean ride excitement, as the original's helpers
/// <c>0x4C94A0</c>, <c>0x4CD500</c>, <c>0x4CCB00</c> and <c>0x4CCBF0</c> return them. A pure read of the park like <see cref="IAdvisorParkState"/>; a park state without this view, or without the world
/// that answers the queue and excitement reads, leaves the callbacks at 0.
/// </summary>
internal interface IAdvisorInputsState
{
	/// <summary>The analyser's misbehaving-kids words (<c>0x004C94A0</c>): the guards' chases this month for <c>false</c>, the abandoned ones for <c>true</c>.</summary>
	int MisbehavingKids( bool gotAway );
	/// <summary>The percent of a class of cells the patrol rectangles of a role cover (<c>0x004CD500</c>); null without a cell map.</summary>
	int? PatrolCoveragePercent( StaffType role, int cellClass );
	/// <summary>The mean live excitement of the rides as the analyser's cached reader returns it (<c>0x004CCBF0</c>); null without a world that reads it.</summary>
	int? AverageRideExcitement( uint maximumAge );
	/// <summary>Whether a queue ride joins no path (<c>0x004CCB00</c>, the object the player has selected left out): null without a world that answers.</summary>
	bool? AnyQueueUnconnected { get; }
}

/// <summary>The inputs of the last nine scan callbacks, computed from the economy without changing anything the simulation reads.</summary>
internal sealed partial class EconomyParkState : IAdvisorInputsState
{
	public int MisbehavingKids( bool gotAway ) => economy.MisbehavingKids( gotAway );

	public int? PatrolCoveragePercent( StaffType role, int cellClass ) => economy.PatrolCoveragePercent( role, cellClass );

	public int? AverageRideExcitement( uint maximumAge ) => economy.AverageRideExcitement( maximumAge );

	// [BIN:TPW-EXE:0x004CCB00 first unconnected queue] the things of type 3 whose flag word +0x32 has the bits 4 (Info.IsChoosable) and 8 (Info.HasQueue) and for which the back-of-queue test 0x4DE4A0 answers 0, in the thing list's order, the selected object 0x527930 skipped; the first one's id is the answer (0 when none)
	public bool? AnyQueueUnconnected => economy.FirstUnconnectedQueueRide is { } probe ? probe() != 0 : null;
}

/// <summary>
/// The advisor's last nine computed scan callbacks, ported from <c>tp.exe</c>: the hire-guards line that counts the kids the guards went after (12), the patrol coverage lines of the mechanics, handymen and
/// entertainers (19, 21, 27), the build-a-camera line (22) and the lax-security line (98) that count the kids that got away, the unconnected queue (113), the ride excitement (141) and the many-pranksters line (264).
/// Each reads the park through <see cref="IAdvisorParkState"/> and <see cref="IAdvisorInputsState"/> and its thresholds through <see cref="AdvisorScanContext.Parameter"/>; a park without the view, or a threshold the
/// settings file lacks, leaves the callback at 0.
/// </summary>
internal static class AdvisorInputsCallbacks
{
	public const uint HireGuardsPranks = 0x59CD30;
	public const uint MechanicCoverage = 0x59D180;
	public const uint HandymanCoverage = 0x59D310;
	public const uint GuardBuildCamera = 0x59D3B0;
	public const uint EntertainerCoverage = 0x59D580;
	public const uint LaxSecurity = 0x59E840;
	public const uint QueueUnconnected = 0x59EF60;
	public const uint VisitorsTooScared = 0x59F4F0;
	public const uint LotsOfPranks = 0x5A04F0;

	/// <summary>The thresholds these callbacks read (words of the <c>Advisor.sam</c> object) besides the staff area keys <see cref="AdvisorStaffCallbacks.IntegerKeys"/> already holds.</summary>
	public static readonly IReadOnlyList<string> IntegerKeys = new[]
	{
		"StaffHireGuards2.Score",
		"StaffMechanicCoverage.Score", "StaffHandymanCoverage.Score", "StaffEntertainerCoverage.Score",
		"StaffGuardBuildCamera.AcceptableTrouble", "GeneralLaxSecurity.MoreThanThisGotAway",
		"VisitorsTooScared.MinimumRides", "VisitorsTooScared.AverageScarierThan", "LotsOfPranks.MorePrankstersThan",
	};

	/// <summary>The messages these callbacks produce with the key of the flat score they return, or of the multiplier of a computed one (the convention of <see cref="AdvisorTables.ScanScoreKeys"/>).</summary>
	public static readonly IReadOnlyList<(int MessageId, string ScoreKey, int? FixedScore)> ScoreKeys = new (int, string, int?)[]
	{
		(12, "StaffHireGuards2.AdditionalPerMisbehavingKid", null),
		(19, "StaffMechanicCoverage.ScorePerNonCoveredPercent", null), (21, "StaffHandymanCoverage.ScorePerNonCoveredPercent", null), (27, "StaffEntertainerCoverage.ScorePerNonCoveredPercent", null),
		(22, "StaffGuardBuildCamera.Score", null),
		(98, "GeneralLaxSecurity.ScorePerGetAway", null),
		(113, "QueueUnconnected.Score", null),
		(141, "VisitorsTooScared.Score", null),
		(264, "LotsOfPranks.Score", null),
	};

	/// <summary>The callbacks by the address of the original's callback (the descriptor's word +0x10).</summary>
	public static readonly IReadOnlyDictionary<uint, AdvisorScoreCallback> Callbacks = new Dictionary<uint, AdvisorScoreCallback>
	{
		[HireGuardsPranks] = HireGuardsPranksScore,
		[MechanicCoverage] = MechanicCoverageScore,
		[HandymanCoverage] = context => CoverageScore( context, StaffType.Handyman, 3, "StaffSetHandymanArea", "StaffHandymanCoverage" ),
		[GuardBuildCamera] = GuardBuildCameraScore,
		[EntertainerCoverage] = context => CoverageScore( context, StaffType.Entertainer, 1, "StaffSetEntertainerArea", "StaffEntertainerCoverage" ),
		[LaxSecurity] = LaxSecurityScore,
		[QueueUnconnected] = QueueUnconnectedScore,
		[VisitorsTooScared] = VisitorsTooScaredScore,
		[LotsOfPranks] = LotsOfPranksScore,
	};

	private static bool Read( AdvisorScanContext context, out IAdvisorParkState park, out IAdvisorInputsState inputs, out int[] values, params string[] keys )
	{
		values = new int[keys.Length];
		park = context.Park!;
		inputs = (context.Park as IAdvisorInputsState)!;
		if ( context.Park is not IAdvisorInputsState )
			return false;
		for ( var index = 0; index < keys.Length; index++ )
		{
			if ( context.Parameter( keys[index] ) is not { } value )
				return false;
			values[index] = (int)value;
		}
		return true;
	}

	// [BIN:TPW-EXE:0x0059CD30 hire guards, pranks] the park open (0x51A280, the closed flag, gives 0); a guard in the park (0x4C9960 of 7 above 0) gives 0; else the kids the guards went after this month (0x4C94A0 of 0) times the word +0xFC plus the word +0xF8: unclamped
	private static int HireGuardsPranksScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var inputs, out var v, "StaffHireGuards2.Score" ) || !park.ParkOpen || park.StaffCount( StaffType.Guard ) > 0 )
			return 0;
		return unchecked( inputs.MisbehavingKids( gotAway: false ) * context.Score( "StaffHireGuards2.AdditionalPerMisbehavingKid" ) + v[0] );
	}

	// [BIN:TPW-EXE:0x0059D180 mechanic coverage] the park's months (0x4F8800 / 30 days) at least the word +0x138 (the area line's MonthsIntoGameForConsideration); the mechanics (0x4C9960 of 4) at least the word +0x12C (AtLeastThisMany, a signed compare); at least one with an area of his own (0x4C9B40 of 4 not 0);
	// the share of the entrance cells (class 2) the mechanics' rectangles leave uncovered (0x4CD500 of 4, 2) is 100 minus the coverage: times the word +0x140, and below the word +0x13C (a signed compare) it gives 0
	private static int MechanicCoverageScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var inputs, out var v, "StaffSetMechanicArea.MonthsIntoGameForConsideration", "StaffSetMechanicArea.AtLeastThisMany", "StaffMechanicCoverage.Score" ) )
			return 0;
		if ( AdvisorStaffCallbacks.Months( park ) < v[0] || park.StaffCount( StaffType.Mechanic ) < v[1] )
			return 0;
		if ( context.Park is not IAdvisorStaffState staff || staff.StaffWithPatrolArea( StaffType.Mechanic ) == 0 )
			return 0;
		return Uncovered( inputs.PatrolCoveragePercent( StaffType.Mechanic, 2 ), context.Score( "StaffMechanicCoverage.ScorePerNonCoveredPercent" ), v[2] );
	}

	// [BIN:TPW-EXE:0x0059D310 handyman and entertainer coverage] 0x59D310 (handymen, role 5, class 3: the path and queue cells) and 0x59D580 (entertainers, role 6, class 1: the built cells) share the shape: the park's months at least the area line's word (+0x150, +0x188);
	// at least one of the role (0x4C9960 not 0, no minimum count and no area test here, unlike the mechanics'); 100 minus the coverage (0x4CD500) times the word after the score's (+0x158, +0x190), and below the score word (+0x154, +0x18C) it gives 0
	private static int CoverageScore( AdvisorScanContext context, StaffType role, int cellClass, string area, string coverage )
	{
		if ( !Read( context, out var park, out var inputs, out var v, $"{area}.MonthsIntoGameForConsideration", $"{coverage}.Score" ) )
			return 0;
		if ( AdvisorStaffCallbacks.Months( park ) < v[0] || park.StaffCount( role ) == 0 )
			return 0;
		return Uncovered( inputs.PatrolCoveragePercent( role, cellClass ), context.Score( $"{coverage}.ScorePerNonCoveredPercent" ), v[1] );
	}

	/// <summary>(100 minus the coverage) times the multiplier, or 0 below the floor; a signed compare (<c>CMP</c>, <c>SETL</c>, <c>DEC</c>, <c>AND</c>); 0 without a cell map.</summary>
	private static int Uncovered( int? percent, int perPercent, int floor )
	{
		if ( percent is not { } covered )
			return 0;
		var score = unchecked( (100 - covered) * perPercent );
		return score < floor ? 0 : score;
	}

	// [BIN:TPW-EXE:0x0059D3B0 build a camera] a guard in the park (0x4C9960 of 7 not 0) and the kids that got away this month (0x4C94A0 of 1) above the word +0x15C (AcceptableTrouble, a signed compare): the flat word +0x160
	private static int GuardBuildCameraScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var inputs, out var v, "StaffGuardBuildCamera.AcceptableTrouble" ) || park.StaffCount( StaffType.Guard ) == 0 )
			return 0;
		return inputs.MisbehavingKids( gotAway: true ) > v[0] ? context.Score( "StaffGuardBuildCamera.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059E840 lax security] the kids that got away this month (0x4C94A0 of 1) above the word +0x314 (MoreThanThisGotAway, a signed compare): that count times the word +0x318; no park-open test
	private static int LaxSecurityScore( AdvisorScanContext context )
	{
		if ( !Read( context, out _, out var inputs, out var v, "GeneralLaxSecurity.MoreThanThisGotAway" ) )
			return 0;
		var gotAway = inputs.MisbehavingKids( gotAway: true );
		return gotAway > v[0] ? unchecked( gotAway * context.Score( "GeneralLaxSecurity.ScorePerGetAway" ) ) : 0;
	}

	// [BIN:TPW-EXE:0x0059EF60 unconnected queue] the first queue ride that joins no path (0x4CCB00 with the flag 0, so the selected object is left out) gives the flat word +0x388; none gives 0. The callback also stores that ride's id in the scan object's word +4 (the subject, read only by the advice record's builder 0x59B6BE)
	private static int QueueUnconnectedScore( AdvisorScanContext context )
	{
		if ( !Read( context, out _, out var inputs, out _ ) )
			return 0;
		return inputs.AnyQueueUnconnected == true ? context.Score( "QueueUnconnected.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059F4F0 visitors too scared] the park open (0x4C9050); the rides (0x4CA8E0 of 0, 0) at least the word +0x41C (MinimumRides, a signed compare); the mean live excitement of the rides (0x4CCBF0 with the age 10, cached) above the word +0x418 (AverageScarierThan): the flat word +0x420.
	// The callback then asks the scariest ride (0x4CCD10) for the scan object's word +4, which only the advice record's builder reads
	private static int VisitorsTooScaredScore( AdvisorScanContext context )
	{
		if ( !Read( context, out var park, out var inputs, out var v, "VisitorsTooScared.MinimumRides", "VisitorsTooScared.AverageScarierThan" ) || !park.ParkOpen )
			return 0;
		if ( park.ObjectsOfKind( 0, 0 ) < v[0] )
			return 0;
		return inputs.AverageRideExcitement( 10 ) is { } average && average > v[1] ? context.Score( "VisitorsTooScared.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x005A04F0 lots of pranks] the kids the guards went after this month (0x4C94A0 of 0) above the word +0x5A8 (MorePrankstersThan, a signed compare): the flat word +0x5AC; no park-open test
	private static int LotsOfPranksScore( AdvisorScanContext context )
	{
		if ( !Read( context, out _, out var inputs, out var v, "LotsOfPranks.MorePrankstersThan" ) )
			return 0;
		return inputs.MisbehavingKids( gotAway: false ) > v[0] ? context.Score( "LotsOfPranks.Score" ) : 0;
	}
}
