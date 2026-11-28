namespace OpenTPW;

/// <summary>
/// One <c>Challenges[n]</c> row of Theme Park Inc's <c>data/Challenges.sam</c>: the 24 of its 25 schema fields that the slot loader copies
/// (docs/reverse/TPI-CHALLENGE-SYSTEM.md §3; <c>Independent</c> is parsed but not passed to the slot). A key the row omits is 0.
/// </summary>
// [BIN:TPI-EXE:0x5C76C8 challenge row] the loader passes the row's fields to the record constructor 0x5C21E0 in schema order (records 0x98E8B4..0x98EE54)
public sealed record ThemeParkIncChallengeRow( int Type, int FollowupType, int TargetTime, int TargetVal, int TargetVal2, int TargetObj, int TargetObj2, int TargetStaffType,
	int CashPrize, int GTPrize, int SharePrize, int SharePriceEffectWon, int SharePriceEffectLost, int CheckAtEndOnly, int MinDaysTilTrigger, int MaxDaysTilTrigger,
	int DaysDelay, int TriggerData, int GracePeriodLimit, int GraceTargetVal, int Sender, int ScriptIndex, int MissionNeededToActivate, int PromoGiven )
{
	/// <summary>Row <paramref name="index"/> of a parsed <c>Challenges.sam</c>; every key the row omits reads 0 (the parser's reset to defaults, <c>0x419830</c>).</summary>
	public static ThemeParkIncChallengeRow Read( SamSettings challenges, int index )
	{
		int Field( string name ) => challenges.GetInt( $"Challenges[{index}].{name}", 0, optional: true );
		return new ThemeParkIncChallengeRow( Field( "Type" ), Field( "FollowupType" ), Field( "TargetTime" ), Field( "TargetVal" ), Field( "TargetVal2" ), Field( "TargetObj" ),
			Field( "TargetObj2" ), Field( "TargetStaffType" ), Field( "CashPrize" ), Field( "GTPrize" ), Field( "SharePrize" ), Field( "SharePriceEffectWon" ),
			Field( "SharePriceEffectLost" ), Field( "CheckAtEndOnly" ), Field( "MinDaysTilTrigger" ), Field( "MaxDaysTilTrigger" ), Field( "DaysDelay" ), Field( "TriggerData" ),
			Field( "GracePeriodLimit" ), Field( "GraceTargetVal" ), Field( "Sender" ), Field( "ScriptIndex" ), Field( "MissionNeededToActivate" ), Field( "PromoGiven" ) );
	}
}

/// <summary>
/// The settings Theme Park Inc's challenge system reads (docs/reverse/TPI-CHALLENGE-SYSTEM.md §2, §3): the offer interval and the mission gate of the layered
/// <c>Standard.sam</c>, the level's <c>ChallengesInThisLevel</c> row numbers, and the rows of <c>data/Challenges.sam</c> they name.
/// </summary>
public sealed class ThemeParkIncChallengeSettings
{
	/// <summary>The rows the parser of <c>Challenges.sam</c> holds: the array record <c>Challenges</c> has a count of 0x3C.</summary>
	// [BIN:TPI-EXE:0x5C768A challenge rows] the stack parser's schema (0x98E878) ends in the array record at 0x98EE90 with count 0x3C
	public const int RowCount = 60;

	/// <summary>The level list entries the loader reads: the first ten of the twenty parsed.</summary>
	// [BIN:TPI-EXE:0x5C76BD level challenge rows] the loop compares its pointer with 0xA0293C, ten entries past 0xA02914
	public const int LevelEntries = 10;

	public ThemeParkIncChallengeSettings( int daysBetweenOffers, int missionNumberForFirstChallenge, IReadOnlyList<int> levelRows, IReadOnlyDictionary<int, ThemeParkIncChallengeRow> rows )
	{
		ArgumentNullException.ThrowIfNull( levelRows );
		ArgumentNullException.ThrowIfNull( rows );
		if ( levelRows.Count != LevelEntries )
			throw new ArgumentException( $"The level list has {LevelEntries} entries.", nameof( levelRows ) );
		DaysBetweenOffers = daysBetweenOffers;
		MissionNumberForFirstChallenge = missionNumberForFirstChallenge;
		LevelRows = levelRows.ToArray();
		Rows = rows;
	}

	/// <summary><c>Challenges.DaysBetweenOffers</c> (<c>0xA028EC</c>): the days after an offer before the next may follow.</summary>
	public int DaysBetweenOffers { get; }

	/// <summary><c>Challenges.MissionNumberForFirstChallenge</c> (<c>0xA028F0</c>): the career's mission counter must reach it before the system switches on.</summary>
	public int MissionNumberForFirstChallenge { get; }

	/// <summary>The first ten <c>ChallengesInThisLevel[k].ChallengeType</c> values: row numbers of <c>Challenges.sam</c> (0 or less is no challenge).</summary>
	public IReadOnlyList<int> LevelRows { get; }

	/// <summary>The rows of <c>Challenges.sam</c> by index (0 to <see cref="RowCount"/> - 1).</summary>
	public IReadOnlyDictionary<int, ThemeParkIncChallengeRow> Rows { get; }

	/// <summary>
	/// The challenge settings of a Theme Park Inc <c>Standard.sam</c> (the global file under the level's) and its <c>Challenges.sam</c>, or null when the
	/// standard file lacks the challenge keys or there is no <c>Challenges.sam</c> (a synthetic or partial installation).
	/// </summary>
	// [BIN:TPI-EXE:0x6481FD settings parse] the global data\levels\Standard.sam is parsed with a reset to defaults, the level's file over it; a key neither sets is 0
	public static ThemeParkIncChallengeSettings? Read( SamSettings standard, SamSettings? challenges )
	{
		ArgumentNullException.ThrowIfNull( standard );
		if ( challenges == null || !standard.Contains( "Challenges.DaysBetweenOffers" ) )
			return null;
		var levelRows = Enumerable.Range( 0, LevelEntries ).Select( index => standard.GetInt( $"ChallengesInThisLevel[{index}].ChallengeType", 0, optional: true ) ).ToArray();
		var rows = Enumerable.Range( 0, RowCount ).ToDictionary( index => index, index => ThemeParkIncChallengeRow.Read( challenges, index ) );
		// [BIN:TPI-EXE:0x5C76BD level challenge rows] the loader has no range check on the row number: a number past the parsed rows would read the parser's memory beyond them,
		// which no shipped level does; such a file is refused here instead of being given rows of OpenTPW's own
		foreach ( var row in levelRows )
		{
			if ( row >= RowCount )
				throw new InvalidDataException( $"ChallengesInThisLevel names Challenges[{row}], past the {RowCount} rows Theme Park Inc's parser holds." );
			// [BIN:TPI-EXE:0x9CD000 challenge senders] a challenge the level loads (a positive row number of a row with a type) cancels its sender's advice when accepted or timed out
			// (ThemeParkIncChallenges.SenderAdvice): senders 2 to 6 name theirs, the others the no-message id 0x285, whose cancel would write outside the player's history. The retail file names 2 to 6;
			// a row with another sender, or without the Sender key (read as 0), is refused when the file loads rather than when its advice is cancelled
			if ( row > 0 && rows[row].Type != 0 && !ThemeParkIncChallenges.HasSenderAdvice( rows[row].Sender ) )
				throw new InvalidDataException( $"Challenges[{row}] names sender {rows[row].Sender}, whose row of the sender table (0x9CD000) names no advice; Theme Park Inc's senders are 2 to 6." );
		}
		return new ThemeParkIncChallengeSettings( standard.GetInt( "Challenges.DaysBetweenOffers", 0, optional: true ), standard.GetInt( "Challenges.MissionNumberForFirstChallenge", 0, optional: true ),
			levelRows, rows );
	}
}
