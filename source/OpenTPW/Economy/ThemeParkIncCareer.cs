namespace OpenTPW;

/// <summary>
/// One zone record of the career (0xF8 bytes, constructor <c>0x74FC80</c>, map <c>career+0x3C</c>; docs/reverse/TPI-CAREER.md §2.3): the zone's availability, its awards, challenge count and
/// tech level, kept with every raw field the global save carries.
/// </summary>
public sealed class ThemeParkIncZoneRecord
{
	internal ThemeParkIncZoneRecord( ThemeParkIncZoneData data ) => Data = data;

	/// <summary>The record as the global save holds it.</summary>
	public ThemeParkIncZoneData Data { get; }

	public string Name => Data.Name;
	/// <summary><c>d_available</c> (<c>+0x61</c>): the zone can be entered (its portals open).</summary>
	public bool Available => Data.Available != 0;
	/// <summary><c>mParkNumber</c> (<c>+0x64</c>): the zone's index in the name table (arabian 0, science 1, water 2, wIntro 3, any other 4).</summary>
	public int ParkNumber => Data.ParkNumber;
	/// <summary><c>mChallengesDone</c> (<c>+0xF0</c>).</summary>
	public int ChallengesDone => Data.ChallengesDone;
	/// <summary><c>mCurrentTechLevel</c> (<c>+0</c>).</summary>
	public int CurrentTechLevel => Data.CurrentTechLevel;
}

/// <summary>
/// The career as a park save keeps it (docs/reverse/TPI-CAREER.md): every career field the global save carries, the zone records and the name of the current zone. The mission object is not kept:
/// it is parsed again from the mission file of the counter, as the original's global save load does.
/// </summary>
public sealed record ThemeParkIncCareerSnapshot( int FiringLine, bool GameFinished, byte[] Flags, byte[] Awards, bool SwearFilter, bool NewCareer, int CardCount, int CardCursor,
	int[] Cards, int MissionCounter, int Rank, byte[] Abilities, int GoldenTickets, int[] Regions, bool ObstacleResearched, bool ObstacleRemoved, int[] Statuses, bool GmiFlag,
	byte[] History, ThemeParkIncZoneSnapshot[] Zones, string? CurrentZone );

/// <summary>A zone record as a park save keeps it: the global save's 215 bytes after the name.</summary>
public sealed record ThemeParkIncZoneSnapshot( byte[] Name, byte[] OneShot, byte[] OptionalFlags, int[] OptionalValues, byte[] AwardFlags, int[] AwardValues, ushort[] SignA, ushort[] SignB,
	byte Available, byte Open, int ParkNumber, byte NameChanged, byte AllResearchCompleted, int ChallengesDone, byte AllChallengesDone, int CurrentTechLevel );

/// <summary>The game files the career reads: the mission files and the zones' <c>global.sam</c> (the game file system in a game, a dictionary in the tests).</summary>
public interface IThemeParkIncCareerFiles
{
	/// <summary>The bytes of a game file such as <c>/missions/Miss00.sam</c>, or null when it does not exist.</summary>
	byte[]? Read( string path );
	/// <summary>Whether <c>data\cdk registration</c> holds any <c>.cdk</c> file (none ships).</summary>
	bool HasCdkRegistrations { get; }
}

/// <summary>
/// Theme Park Inc's career, the Global Master Save object <c>0xBFC484</c> (0x4740 bytes; docs/reverse/TPI-CAREER.md): the mission counter and the mission object, the rank and the firing line,
/// the golden tickets, the discovered staff abilities, the opened map regions, the exit-statistic states, the CDK registration table and the zone records. One career spans every zone; OpenTPW
/// runs one zone park at a time over it.
/// </summary>
// [BIN:TPI-EXE:0x74B490 career constructor] the 256 CDK slots get the element constructor's (0, 4, 0x7FFFFFFF, -1), the mission object is bound (0x418F60), the five statuses' start dates
// (+0x10/+0x14) are 0, every history entry is {0, -1, byte 0, 0, 0} (bytes +9..+0xB untouched), then the reset 0x74BB40 runs
public sealed class ThemeParkIncCareer
{
	public const int CardSlots = ThemeParkIncGlobalSave.CardSlots;
	public const int AbilityCount = 15;
	public const int RegionCount = 15;
	public const int StatusCount = 5;
	public const int StatusWords = 12;
	public const int HistoryBytes = ThemeParkIncGlobalSave.HistoryEntries * ThemeParkIncGlobalSave.HistoryEntryBytes;

	/// <summary>The zone names by index (<c>0x9D53B4</c>, stride 9).</summary>
	public static readonly IReadOnlyList<string> ZoneNames = new[] { "arabian", "science", "water", "wIntro" };

	/// <summary>The mission index → map region table <c>0x9E907C</c>: the region an advance to that counter opens (-1 none). Read for counters 0 to 23.</summary>
	// [BIN:TPI-EXE:0x9E907C region table] 24 dwords before string data: -1, 13, 10, 8, 7, 1, 2, 0, -1, 12, 11, -1, 6, 5, 9, -1, 4, 3, -1, -1, 0, 0, 0, 0
	public static readonly IReadOnlyList<int> RegionForMission = new[] { -1, 13, 10, 8, 7, 1, 2, 0, -1, 12, 11, -1, 6, 5, 9, -1, 4, 3, -1, -1, 0, 0, 0, 0 };

	/// <summary>The sixteen built-in CDK registrations (static initializer <c>0x74AC30</c>, copied over slots 0 to 15 by every reset).</summary>
	// [BIN:TPI-EXE:0x74AC30 built-in CDK table] 0xBFC680: (0x4A6,1,1,7500), (0x1036,1,0,7500), (0x4A1,1,0,9000), (0x1194,1,1,12000), (0x1070,1,1,11000), (0x1059,2,1,12000) by stores, then
	// 0x74AED0(id, level, flag, amount) for (0x88E,2,0,12000), (0x884,2,1,14500), (0x84B,2,1,12000), (0x1040,2,1,12000), (0x1069,2,0,12000), (0x100E,0,1,15000), (0x104A,0,1,15000),
	// (0xC71,0,0,13000), (0xC76,0,1,15000), (0x10CC,0,1,15000)
	public static readonly IReadOnlyList<ThemeParkIncCdkEntry> BuiltInCards = new ThemeParkIncCdkEntry[]
	{
		new( 0x4A6, 1, 1, 7500 ), new( 0x1036, 1, 0, 7500 ), new( 0x4A1, 1, 0, 9000 ), new( 0x1194, 1, 1, 12000 ), new( 0x1070, 1, 1, 11000 ), new( 0x1059, 2, 1, 12000 ),
		new( 0x88E, 2, 0, 12000 ), new( 0x884, 2, 1, 14500 ), new( 0x84B, 2, 1, 12000 ), new( 0x1040, 2, 1, 12000 ), new( 0x1069, 2, 0, 12000 ), new( 0x100E, 0, 1, 15000 ),
		new( 0x104A, 0, 1, 15000 ), new( 0xC71, 0, 0, 13000 ), new( 0xC76, 0, 1, 15000 ), new( 0x10CC, 0, 1, 15000 )
	};

	private readonly IThemeParkIncCareerFiles files;
	private readonly Func<int, int?> firingLineLimit;
	private readonly ThemeParkIncCdkEntry[] cards = Enumerable.Repeat( ThemeParkIncCdkEntry.Empty, CardSlots ).ToArray();
	private readonly byte[] abilities = new byte[AbilityCount];
	private readonly int[] regions = new int[RegionCount];
	private readonly int[] statuses = new int[StatusCount * StatusWords];
	private readonly byte[] flags = new byte[10];
	private readonly byte[] awards = new byte[8];
	private byte[] history = new byte[HistoryBytes];
	private readonly SortedDictionary<string, ThemeParkIncZoneRecord> zones = new( StringComparer.Ordinal );

	/// <summary>
	/// A new career (<c>0x74B490</c>, then the reset): <paramref name="firingLineLimit"/> answers the word the promotion reads for a rank (<see cref="ThemeParkIncStandard.FiringLineLimit"/>), or null
	/// where OpenTPW cannot read it.
	/// </summary>
	public ThemeParkIncCareer( IThemeParkIncCareerFiles files, Func<int, int?> firingLineLimit )
	{
		this.files = files ?? throw new ArgumentNullException( nameof( files ) );
		this.firingLineLimit = firingLineLimit ?? throw new ArgumentNullException( nameof( firingLineLimit ) );
		for ( var index = 0; index < ThemeParkIncGlobalSave.HistoryEntries; index++ )
		{
			var entry = index * ThemeParkIncGlobalSave.HistoryEntryBytes;
			BitConverter.TryWriteBytes( history.AsSpan( entry + 4, 4 ), -1 );
		}
		Reset();
	}

	/// <summary>The career's firing line (<c>+0</c>): challenges the player may still fail.</summary>
	public int FiringLine { get; internal set; }
	/// <summary><c>+4</c>: the last mission's end of game has run.</summary>
	public bool GameFinished { get; internal set; }
	/// <summary><c>+0x20..+0x29</c> (<c>+0x20</c> is the "objectives letter posted" byte of the mission controller).</summary>
	public IReadOnlyList<byte> Flags => flags;
	/// <summary><c>+0x2A..+0x31</c>: the park awards held.</summary>
	public IReadOnlyList<byte> Awards => awards;
	/// <summary><c>+0x38</c>: the swear filter.</summary>
	public bool SwearFilter { get; private set; }
	/// <summary><c>+0x50</c>: a new career, whose first Science world is the prebuilt park.</summary>
	public bool NewCareer { get; internal set; }
	/// <summary>The CDK registration table (<c>+0x5C</c>) and its count (<c>+0x54</c>) and cursor (<c>+0x58</c>).</summary>
	public IReadOnlyList<ThemeParkIncCdkEntry> Cards => cards;
	public int CardCount { get; private set; }
	public int CardCursor { get; private set; }
	/// <summary><c>+0x105C</c>: the mission counter.</summary>
	public int MissionCounter { get; private set; }
	/// <summary><c>+0x1060</c>: the promotion rank.</summary>
	public int Rank { get; private set; }
	/// <summary><c>+0x1064[15]</c>: the discovered staff abilities.</summary>
	public IReadOnlyList<byte> Abilities => abilities;
	/// <summary><c>+0x1074</c>: the golden tickets.</summary>
	public int GoldenTickets { get; private set; }
	/// <summary><c>+0x1078[15]</c>: the opened overview-map regions (dwords).</summary>
	public IReadOnlyList<int> Regions => regions;
	/// <summary>The mission object at <c>+0x10B4</c>.</summary>
	public ThemeParkIncMission Mission { get; } = new();
	/// <summary>The career told that an object type is researched (<c>0x74D740</c>, message 0x11): the CDK table's entry of the type, when it has one, clears its flag.</summary>
	// [BIN:TPI-EXE:0x74D740 researched object] "GlobaMasterSave has been informed that Object %d has been researched"; the first of the +0x54 entries at +0x5C (stride 0x10) whose id dword is the type gets
	// its flag (+8) = 0 ("Object was recognised as a base coaster and has been logged."), else "The object was not recognised."
	public void InformResearched( int type )
	{
		for ( var index = 0; index < CardCount && index < cards.Length; index++ )
		{
			if ( cards[index].Id == type )
			{
				cards[index] = cards[index] with { Flag = 0 };
				return;
			}
		}
	}

	/// <summary><c>+0x13E4</c>: the mission's obstacle has been researched.</summary>
	public bool ObstacleResearched { get; private set; }
	/// <summary><c>+0x13E5</c>: the mission's obstacle has been removed.</summary>
	public bool ObstacleRemoved { get; private set; }
	/// <summary><c>+0x13E8</c>: the five exit-statistic status blocks, 12 dwords each (<see cref="StatusWord"/>).</summary>
	public IReadOnlyList<int> Statuses => statuses;
	/// <summary><c>+0x14EC</c>: the refresh (<c>.GMI</c>) flag the writer stores.</summary>
	public bool GmiFlag { get; internal set; }
	/// <summary><c>mHistory[0x284]</c> (<c>+0x14F0</c>), raw: its writers are not traced.</summary>
	public ReadOnlySpan<byte> History => history;
	/// <summary>The zone records by name (map order: byte-wise).</summary>
	public IReadOnlyDictionary<string, ThemeParkIncZoneRecord> Zones => zones;
	/// <summary><c>+0x4C</c>: the current zone's record, or null.</summary>
	public ThemeParkIncZoneRecord? CurrentZone { get; private set; }

	/// <summary>A status block's word <paramref name="word"/> (0 state, 1 type, 2 target value, 3 target time, 4/5 start date, 6 current value, 7 elapsed days, 8 zone, 9 baseline).</summary>
	public int StatusWord( int index, int word ) => statuses[index * StatusWords + word];

	/// <summary>A status block's 64-bit start date (words 4 and 5, the park clock in 100 ns).</summary>
	public long StatusStart( int index ) => (long)(uint)statuses[index * StatusWords + 4] | (long)statuses[index * StatusWords + 5] << 32;

	/// <summary>
	/// Starts exit statistic <paramref name="index"/> (<c>0x74C620</c>): running, the start date and the baseline; the value and the days are left as they are.
	/// </summary>
	// [BIN:TPI-EXE:0x74C620 status start] +0 = 1, +0x10 = date low, +0x14 = date high, +0x24 = baseline
	internal void StartStatus( int index, long date, int baseline )
	{
		var block = StatusBlock( index );
		statuses[block] = 1;
		statuses[block + 4] = unchecked( (int)date );
		statuses[block + 5] = (int)(date >> 32);
		statuses[block + 9] = baseline;
	}

	/// <summary>Stores a daily measurement of exit statistic <paramref name="index"/> (<c>0x74C5B0</c>): the elapsed days and the value.</summary>
	// [BIN:TPI-EXE:0x74C5B0 status store] +0x1C = days, +0x18 = value
	internal void StoreStatus( int index, int days, int value )
	{
		var block = StatusBlock( index );
		statuses[block + 7] = days;
		statuses[block + 6] = value;
	}

	/// <summary>Sets the state word of exit statistic <paramref name="index"/> (<c>0x74C5F0</c>): 2 achieved, 3 failed.</summary>
	// [BIN:TPI-EXE:0x74C5F0 status state] +0 = state
	internal void SetStatusState( int index, int state ) => statuses[StatusBlock( index )] = state;

	/// <summary>
	/// Whether exit statistic <paramref name="index"/> has reached its target (<c>0x74C670</c>): types 4 and 5 compare the value with the baseline raised by the target percentage,
	/// <c>(TargetValue + 100) × baseline / 100</c> in 32-bit arithmetic truncated toward zero; every other type compares it with the target value.
	/// </summary>
	// [BIN:TPI-EXE:0x74C670 status target] type 5 or 4: edx = (+8 + 100) * +0x24 (imul, 32 bits), the high word of edx * 0x51EB851F shifted right 5 plus its sign bit (a signed division by 100),
	// then +0x18 >= that (setge); otherwise +0x18 >= +8 (setge)
	public bool StatusReached( int index )
	{
		var block = StatusBlock( index );
		var value = statuses[block + 6];
		var type = statuses[block + 1];
		if ( type is 4 or 5 )
			return value >= unchecked( (statuses[block + 2] + 100) * statuses[block + 9] ) / 100;
		return value >= statuses[block + 2];
	}

	private static int StatusBlock( int index ) => index is >= 0 and < StatusCount ? index * StatusWords : throw new ArgumentOutOfRangeException( nameof( index ) );

	/// <summary>
	/// <c>+0x20</c>: the mission controller's "objective letter posted" byte (cleared by the completion tick, set when the controller posts the letter of the mission just completed).
	/// </summary>
	public bool ObjectiveLetterPosted
	{
		get => flags[0] != 0;
		internal set => flags[0] = (byte)(value ? 1 : 0);
	}

	/// <summary>
	/// The tech level step of the exit statistics' start (<c>0x5EF30B..0x5EF365</c>): for <c>TechLevel.Theme</c> 0 to 2 the zone's record's current tech level rises to
	/// <c>TechLevel.NewMaxGroup</c> when it is below.
	/// </summary>
	// [BIN:TPI-EXE:0x5EF30B tech level] edi = +0x10E4 (TechLevel.Theme); edi > -1 and edi < 3 (signed): the record 0x74F4B0(edi) +0 = +0x10E8 (NewMaxGroup) when +0 < it (signed jle skips)
	internal void RaiseTechLevel()
	{
		var theme = Mission.TechLevelTheme;
		if ( theme <= -1 || theme >= 3 )
			return;
		if ( ZoneRecord( theme ) is not { } zone )
			throw new InvalidOperationException( $"The tech level's zone {theme} has no record (the original's 0x74F4B0 would return none and fault)." );
		if ( zone.Data.CurrentTechLevel < Mission.TechLevelNewMaxGroup )
			zone.Data.CurrentTechLevel = Mission.TechLevelNewMaxGroup;
	}

	/// <summary>The park awards held (<c>0x74C860</c>): the nonzero bytes of <c>+0x2A..+0x31</c>.</summary>
	// [BIN:TPI-EXE:0x74C860 awards held] counts the nonzero bytes +0x2A + i for i = 0..7
	public int AwardsHeld => awards.Count( award => award != 0 );

	/// <summary>
	/// The firing line table from OpenTPW's own reading of the layered Standard.sam keys: the six <c>FiringLineForRank[n].Limit</c> values (0 for a key no layer sets) and the count dword after them.
	/// Used where the Standard files' bytes are not at hand (<see cref="BalanceSettings.ForThemeParkInc"/> over parsed settings); the game reads <see cref="ThemeParkIncStandard"/>.
	/// </summary>
	// [BIN:TPI-EXE:0x996684 Standard schema] FiringLineForRank[6].Limit sits at the Standard object's +0xE98 (0xA028F8) with its count dword at +0xEB0, the slot after the sixth element; the
	// global file is parsed with the reset and the level's over it (0x6481FD), so a key neither sets is 0 and the count is one past the highest index any of them set
	public static IReadOnlyList<int> FiringLineTable( SamSettings standard )
	{
		ArgumentNullException.ThrowIfNull( standard );
		var table = new int[7];
		var count = 0;
		for ( var rank = 0; rank < 6; rank++ )
		{
			if ( standard.Find( $"FiringLineForRank[{rank}].Limit" ) is not null )
			{
				table[rank] = standard.GetInt( $"FiringLineForRank[{rank}].Limit" );
				count = rank + 1;
			}
		}
		table[6] = count;
		return table;
	}

	/// <summary>
	/// The career reset (<c>0x74BB40</c>): the CDK table to its sixteen built-ins, the counter 0, rank 1, firing line 5, no tickets, abilities 1, 7 and 9, the statuses cleared with zone 4,
	/// no awards, the swear filter and the new-career flag on, the flags 0,1,...,1, mission 0 loaded, not finished, and only map region 14 open.
	/// </summary>
	// [BIN:TPI-EXE:0x74BB40 career reset] +0x54 = 16, +0x58 = 0, 16 entries copied from 0xBFC680; the scan of data\cdk registration\*.cdk (validation 0x74BC2E..) finds nothing in the shipped
	// data; then +0x105C = 0, +0x1060 = 1, +0 = 5, +0x13E5 = +0x13E4 = 0, +0x1074 = 0, +0x1064[i] = (i is 1, 7 or 9), statuses +0..+0x1C = 0 and +0x20 = 4 (+0x24..+0x2F untouched),
	// +0x2A..+0x31 = 0, +0x34 = 0, +0x38 = 1, +0x50 = 1, +0x20 = 0, +0x21..+0x29 = 1, 0x74C050 (mission 0), the shares reset 0x621210 (not ported), +4 = 0, +0x1078[0..14] = 0 and +0x10B0 = 1
	public void Reset()
	{
		CardCount = 16;
		CardCursor = 0;
		for ( var index = 0; index < BuiltInCards.Count; index++ )
			cards[index] = BuiltInCards[index];
		// [EXT:tpi-capability-gates] a data\cdk registration folder with .cdk files (none ships) is refused: its validation calls 0x43E9B0, whose meaning is not traced
		if ( files.HasCdkRegistrations )
			throw new NotSupportedException( "CDK registration files are not ported." );
		MissionCounter = 0;
		Rank = 1;
		FiringLine = 5;
		ObstacleRemoved = false;
		ObstacleResearched = false;
		GoldenTickets = 0;
		for ( var index = 0; index < AbilityCount; index++ )
			abilities[index] = (byte)(index is 1 or 7 or 9 ? 1 : 0);
		ClearStatuses();
		Array.Clear( awards );
		SwearFilter = true;
		NewCareer = true;
		flags[0] = 0;
		for ( var index = 1; index < flags.Length; index++ )
			flags[index] = 1;
		LoadMission();
		GameFinished = false;
		Array.Clear( regions );
		regions[14] = 1;
	}

	// [BIN:TPI-EXE:0x74BDF0 status clear] per block: +0x10, +0, +4, +8, +0xC, +0x14, +0x18, +0x1C = 0 and +0x20 = 4; the advance 0x74C1B0 clears the same way
	private void ClearStatuses()
	{
		for ( var index = 0; index < StatusCount; index++ )
		{
			var block = index * StatusWords;
			for ( var word = 0; word < 8; word++ )
				statuses[block + word] = 0;
			statuses[block + 8] = 4;
		}
	}

	/// <summary>
	/// The mission loader (<c>0x74C050</c>): parses <c>missions/Miss%02d.sam</c> of the counter into the mission object (with the reset to defaults) and, when that succeeds, copies each exit
	/// statistic's type, target value, target time and zone into its status block (state 0, values and baseline 0). False when the file is missing or malformed; a missing file leaves the
	/// mission object as it was, a malformed one keeps the lines before the error, and the statuses are left alone either way.
	/// </summary>
	// [BIN:TPI-EXE:0x74C050 mission load] logs "Loading mission %d", builds "%s\Miss%02d.sam" and parses it with 0x419B20(path, 0); on 0 it returns 0; else for i = 0..4: +0 = 0, +4 = ES.Type,
	// +8 = ES.TargetValue, +0xC = ES.TargetTime, +0x18 = 0, +0x1C = 0, +0x20 = ES.InTheme, +0x24 = 0 (0x74C0B0..0x74C0F3)
	public bool LoadMission()
	{
		Log?.Trace( $"Loading mission {MissionCounter}" );
		if ( !Mission.Object.Load( files.Read( ThemeParkIncMission.PathOf( MissionCounter ) ), overlay: false ) )
			return false;
		for ( var index = 0; index < StatusCount; index++ )
		{
			var exit = Mission.ExitStatistic( index );
			var block = index * StatusWords;
			statuses[block] = 0;
			statuses[block + 1] = exit.Type;
			statuses[block + 2] = exit.TargetValue;
			statuses[block + 3] = exit.TargetTime;
			statuses[block + 6] = 0;
			statuses[block + 7] = 0;
			statuses[block + 8] = exit.InTheme;
			statuses[block + 9] = 0;
		}
		return true;
	}

	/// <summary>
	/// The mission advance (<c>0x74C1B0</c>): the counter rises, the obstacle bytes clear, the statuses clear, the region the new counter names opens and the next mission file loads.
	/// </summary>
	// [BIN:TPI-EXE:0x74C1B0 mission advance] +0x105C += 1, +0x13E4 = +0x13E5 = 0, the statuses cleared as by the reset, then if 0x9E907C[counter] >= 0 (signed) +0x1078[that] = 1, then 0x74C050
	public void Advance()
	{
		MissionCounter = unchecked( MissionCounter + 1 );
		ObstacleResearched = false;
		ObstacleRemoved = false;
		ClearStatuses();
		// [EXT:original-undefined-reads] the table holds 24 dwords before string data, which a counter past it would read as a region: no region opens then (the completion tick stops at
		// the end-of-game mission 17, and a loaded counter is refused outside 0 to 23)
		if ( MissionCounter >= 0 && MissionCounter < RegionForMission.Count && RegionForMission[MissionCounter] is var region and >= 0 )
			regions[region] = 1;
		LoadMission();
	}

	/// <summary>The obstacle's research is complete (<c>0x74C3C0</c>): each obstacle staff entry with a nonzero number discovers its ability; the obstacle counts as researched.</summary>
	// [BIN:TPI-EXE:0x74C3C0 obstacle abilities] for i below the ObstacleStaff count dword (+0x13D0): Number (+0x139C + 12i) != 0 sets +0x1064[Ability] = 1; then +0x13E4 = 1
	public void DiscoverObstacleAbilities()
	{
		for ( var index = 0; index < Mission.ObstacleStaffCount; index++ )
		{
			var staff = Mission.ObstacleStaff( index );
			if ( staff.Number != 0 )
			{
				if ( staff.Ability is < 0 or >= AbilityCount )
					throw new InvalidDataException( $"Mission obstacle staff {index} names ability {staff.Ability}, outside the career's {AbilityCount}." );
				abilities[staff.Ability] = 1;
			}
		}
		ObstacleResearched = true;
	}

	/// <summary>The obstacle is removed (<c>0x74C430</c>: <c>+0x13E5 = 1</c>).</summary>
	public void MarkObstacleRemoved() => ObstacleRemoved = true;

	/// <summary>The mission has an obstacle to research and remove (<c>0x5F0190</c>): <c>Obstacle.Type</c> nonzero and <c>Info.NoResearch</c> zero.</summary>
	// [BIN:TPI-EXE:0x5F0190 has obstacle] +0x1380 != 0 and the mission's +0x10 (Info.NoResearch) == 0
	public bool HasObstacle => Mission.ObstacleType != 0 && Mission.NoResearch == 0;

	/// <summary>
	/// A promotion (<c>0x74C450</c>): the rank becomes <paramref name="rank"/> and the firing line rises to the word the promotion reads for it when it is below. False when that word is one
	/// OpenTPW cannot read (the rank is still set); never throws.
	/// </summary>
	// [BIN:TPI-EXE:0x74C450 promotion] +0x1060 = rank first; then if +0 < [0xA028F8 + rank * 4] (signed, no bound) +0 = that word (ThemeParkIncStandard.FiringLineLimit)
	public bool Promote( int rank )
	{
		Rank = rank;
		if ( firingLineLimit( rank ) is not { } limit )
			return false;
		if ( FiringLine < limit )
			FiringLine = limit;
		return true;
	}

	/// <summary>A challenge loss's firing line step (<c>0x5C4857</c>).</summary>
	internal void LowerFiringLine() => FiringLine = unchecked( FiringLine - 1 );

	/// <summary>Golden tickets won (<c>0x74C890</c>).</summary>
	// [BIN:TPI-EXE:0x74C890 add tickets] +0x1074 += n
	public void AddGoldenTickets( int count ) => GoldenTickets = unchecked( GoldenTickets + count );

	/// <summary>Spends <paramref name="count"/> golden tickets when there are enough (<c>0x74C8C0</c>); false and nothing spent otherwise.</summary>
	// [BIN:TPI-EXE:0x74C8C0 spend tickets] if +0x1074 < n (signed) return 0; else +0x1074 -= n, return 1
	public bool TrySpendGoldenTickets( int count )
	{
		if ( GoldenTickets < count )
			return false;
		GoldenTickets = unchecked( GoldenTickets - count );
		return true;
	}

	/// <summary>
	/// A zone opens (<c>0x74C490</c>): the record of zone <paramref name="theme"/> becomes available, when it exists and its <c>global.sam</c> loads. A missing record is not created.
	/// </summary>
	// [BIN:TPI-EXE:0x74C490 open theme] the name 0x9D53B4 + 9n, map find 0x751ED0; when found and 0x7509C0 succeeds, record +0x61 = 1
	public void OpenTheme( int theme )
	{
		if ( zones.TryGetValue( ZoneName( theme ), out var record ) && LoadsGlobalSam( record.Name ) )
			record.Data.Available = 1;
	}

	/// <summary>
	/// The zone record of <paramref name="theme"/> (<c>0x74F4B0</c>), created when missing; null when its <c>global.sam</c> does not load.
	/// </summary>
	// [BIN:TPI-EXE:0x74F4B0 zone record] finds the record by name (0x7519A0); when missing logs "Trying to get info from a theme...", creates it (0x74FC80) and inserts it (0x751020); returns it only
	// when 0x7509C0 succeeds
	public ThemeParkIncZoneRecord? ZoneRecord( int theme ) => ZoneRecord( ZoneName( theme ) );

	/// <summary>The current zone becomes <paramref name="name"/> (<c>0x74F330</c>), its record created when missing.</summary>
	// [BIN:TPI-EXE:0x74F330 change theme] the record is looked up in the map, created by 0x74F760 when missing, stored at +0x4C, then 0x7509C0 loads its global.sam
	public void ChangeTheme( string name ) => CurrentZone = ZoneRecord( name );

	private ThemeParkIncZoneRecord? ZoneRecord( string name )
	{
		if ( !zones.TryGetValue( name, out var record ) )
		{
			Log?.Trace( $"Trying to get info from a theme ({name}) that is not in the global save; creating it." );
			record = new ThemeParkIncZoneRecord( NewZone( name ) );
			zones[name] = record;
		}
		return LoadsGlobalSam( name ) ? record : null;
	}

	/// <summary>
	/// A new zone record (<c>0x74FC80</c>): no flags, awards, sign or counts, the zone's index as its park number and its availability from its <c>global.sam</c>
	/// (<c>ParkInfo.AvailableFromStart == 1</c>).
	/// </summary>
	// [BIN:TPI-EXE:0x74FC80 zone record constructor] +0x48 = 0; +4..+0xD = 0 (one-shot bytes and the optional flags); +0x20..+0x27 = 0 (award flags); +0x64 = the name's index in 0x9D53B4
	// (4 when none matches); +0xEC = +0xED = 0; the first sign characters +0x68/+0xAA = 0; 0x7509C0; +0x60 = 0; +0x61 = (global.sam +0x14 == 1); +0xF0 = 0, +0xF4 = 0, +0 = 0
	// [EXT:original-undefined-reads] the optional values (+0x10..), the award values (+0x28..) and the sign characters after the first stay uninitialised heap in the original: 0 here
	private ThemeParkIncZoneData NewZone( string name )
	{
		var index = ZoneNames.ToList().IndexOf( name );
		var global = GlobalSam( name ) ?? throw new InvalidDataException( $"The zone '{name}' has no global.sam (data\\levels\\{name}\\global.sam), which the record constructor reads." );
		return new ThemeParkIncZoneData
		{
			NameBytes = System.Text.Encoding.Latin1.GetBytes( name ),
			ParkNumber = index < 0 ? 4 : index,
			Available = (byte)(global[0x14] == 1 ? 1 : 0)
		};
	}

	/// <summary>The schema of a zone's <c>global.sam</c> object (<c>0x996240</c>, 0x1C bytes).</summary>
	// [BIN:TPI-EXE:0x996240 global.sam schema] Tickets{CanEarnTicketsInTheme 4, CanSpendTicketsInTheme 4}, ParkInfo{AvailableFromStart 4}, ParkName{GateObjectId 5}, end
	private static readonly ThemeParkIncSamRecord[] GlobalSamSchema =
	{
		new( ThemeParkIncSamKind.StructBegin, "" ), new( ThemeParkIncSamKind.Int, "CanEarnTicketsInTheme" ), new( ThemeParkIncSamKind.Int, "CanSpendTicketsInTheme" ),
		new( ThemeParkIncSamKind.StructEnd, "Tickets" ), new( ThemeParkIncSamKind.StructBegin, "" ), new( ThemeParkIncSamKind.Int, "AvailableFromStart" ),
		new( ThemeParkIncSamKind.StructEnd, "ParkInfo" ), new( ThemeParkIncSamKind.StructBegin, "" ), new( ThemeParkIncSamKind.NonNegativeInt, "GateObjectId" ),
		new( ThemeParkIncSamKind.StructEnd, "ParkName" ), new( ThemeParkIncSamKind.End, "" )
	};

	// [BIN:TPI-EXE:0x7509C0 global.sam load] once per record: a new 0x1C object bound through 0x418F60 parses "data\levels\%s\global.sam" with 0x419B20(path, 0); a failure frees it and logs
	// "...Loading the global sam went wrong"
	private ThemeParkIncSamObject? GlobalSam( string name )
	{
		var global = new ThemeParkIncSamObject( GlobalSamSchema );
		return global.Load( files.Read( $"/levels/{name}/global.sam" ), overlay: false ) ? global : null;
	}

	private bool LoadsGlobalSam( string name )
	{
		if ( GlobalSam( name ) is not null )
			return true;
		Log?.Warning( $"Theme Park Inc career: loading the global sam of '{name}' went wrong." );
		return false;
	}

	private static string ZoneName( int theme ) => theme >= 0 && theme < ZoneNames.Count
		? ZoneNames[theme]
		: throw new ArgumentOutOfRangeException( nameof( theme ), $"Zone {theme} is past the name table." );

	/// <summary>
	/// A challenge won in the current zone (<c>0x5C4514</c>): its record counts it (<c>+0xF0</c>) and, when every slot of the zone's system is empty or won, marks all done (<c>+0xF4</c>).
	/// </summary>
	internal void CountChallengeWon( bool allDone )
	{
		if ( CurrentZone is not { } current || current.ParkNumber < 0 || current.ParkNumber >= ZoneNames.Count || ZoneRecord( current.ParkNumber ) is not { } zone )
			throw new InvalidOperationException( "A challenge was won with no current zone record (the original's 0x74F4B0 would return none and fault)." );
		if ( allDone )
			zone.Data.AllChallengesDone = 1;
		zone.Data.ChallengesDone = unchecked( zone.Data.ChallengesDone + 1 );
	}

	/// <summary>
	/// The career side of the mission completion tick (<c>0x5F0940</c>): nothing once the game is finished; the obstacle's golden tickets are spent (the result is not looked at); a mission that
	/// opens a theme opens it; the end-of-game mission marks the game finished, any other advances. The world side (the new extent, the end sequence, the next obstacle, the exit statistics'
	/// start, the letters) is the caller's. True when the career advanced.
	/// </summary>
	// [BIN:TPI-EXE:0x5F0940 complete mission] returns when +4 is set; 0x74C8C0(Obstacle.GoldenTickets) with the result ignored; LoadNewHoardings == 1 loads "%sextent.%03d" with the counter
	// (0x678B00, the world's); ThemeOpened == 1 calls 0x74C490(ThemeNumber); EndOfGame == 1 posts the end object 0x5CD6E0(0x5D, 6, 1), sets +4 = 1 and logs "Game is finished!"; else 0x74C1B0,
	// then 0x6476F0 and 0x5EEF60 (the world's), +0x20 = 0 and message 0x2C
	public ThemeParkIncCompletion CompleteMission()
	{
		if ( GameFinished )
			return ThemeParkIncCompletion.None;
		TrySpendGoldenTickets( Mission.ObstacleGoldenTickets );
		var extent = Mission.LoadNewHoardings == 1 ? MissionCounter : -1;
		if ( Mission.ThemeOpened == 1 )
			OpenTheme( Mission.ThemeNumber );
		if ( Mission.EndOfGame == 1 )
		{
			GameFinished = true;
			Log?.Trace( "Game is finished!" );
			return new ThemeParkIncCompletion( extent, Advanced: false, Finished: true );
		}
		Advance();
		flags[0] = 0;
		return new ThemeParkIncCompletion( extent, Advanced: true, Finished: false );
	}

	internal ThemeParkIncCareerSnapshot Capture() => new( FiringLine, GameFinished, (byte[])flags.Clone(), (byte[])awards.Clone(), SwearFilter, NewCareer, CardCount, CardCursor,
		cards.SelectMany( card => new[] { card.Id, card.Level, card.Flag, card.Amount } ).ToArray(), MissionCounter, Rank, (byte[])abilities.Clone(), GoldenTickets, (int[])regions.Clone(),
		ObstacleResearched, ObstacleRemoved, (int[])statuses.Clone(), GmiFlag, (byte[])history.Clone(), zones.Values.Select( zone => SnapshotOf( zone.Data ) ).ToArray(), CurrentZone?.Name );

	/// <summary>Whether a mission counter is one OpenTPW takes from a save: inside the region table (0 to 23), which the advance reads.</summary>
	public static bool IsValidMissionCounter( int counter ) => counter >= 0 && counter < RegionForMission.Count;

	internal static bool IsValid( ThemeParkIncCareerSnapshot? state ) =>
		state is { MissionCounter: var counter } && IsValidMissionCounter( counter ) && state is { Flags.Length: 10, Awards.Length: 8, Cards.Length: CardSlots * 4, Abilities.Length: AbilityCount, Regions.Length: RegionCount, Statuses.Length: StatusCount * StatusWords,
			History.Length: HistoryBytes, Zones: not null }
		&& state.Zones.All( zone => zone is { Name: not null, OneShot.Length: 6, OptionalFlags.Length: 4, OptionalValues.Length: 4, AwardFlags.Length: 8, AwardValues.Length: 8,
			SignA.Length: ThemeParkIncZoneData.SignCharacters, SignB.Length: ThemeParkIncZoneData.SignCharacters } )
		&& state.Zones.Select( zone => DataOf( zone ).Name ).Distinct( StringComparer.Ordinal ).Count() == state.Zones.Length
		&& (state.CurrentZone == null || state.Zones.Any( zone => DataOf( zone ).Name == state.CurrentZone ));

	/// <summary>Restores a saved career (validated by <see cref="IsValid"/>): the fields, then the mission file of the saved counter, then the saved statuses over what it copied.</summary>
	internal void Restore( ThemeParkIncCareerSnapshot state )
	{
		FiringLine = state.FiringLine;
		GameFinished = state.GameFinished;
		state.Flags.CopyTo( flags, 0 );
		state.Awards.CopyTo( awards, 0 );
		SwearFilter = state.SwearFilter;
		NewCareer = state.NewCareer;
		CardCount = state.CardCount;
		CardCursor = state.CardCursor;
		for ( var index = 0; index < CardSlots; index++ )
			cards[index] = new ThemeParkIncCdkEntry( state.Cards[index * 4], state.Cards[index * 4 + 1], state.Cards[index * 4 + 2], state.Cards[index * 4 + 3] );
		MissionCounter = state.MissionCounter;
		Rank = state.Rank;
		state.Abilities.CopyTo( abilities, 0 );
		GoldenTickets = state.GoldenTickets;
		state.Regions.CopyTo( regions, 0 );
		ObstacleResearched = state.ObstacleResearched;
		ObstacleRemoved = state.ObstacleRemoved;
		GmiFlag = state.GmiFlag;
		history = (byte[])state.History.Clone();
		zones.Clear();
		foreach ( var zone in state.Zones )
		{
			var data = DataOf( zone );
			zones[data.Name] = new ThemeParkIncZoneRecord( data );
		}
		CurrentZone = state.CurrentZone is { } current ? zones[current] : null;
		LoadMission();
		state.Statuses.CopyTo( statuses, 0 );
	}

	private static ThemeParkIncZoneSnapshot SnapshotOf( ThemeParkIncZoneData data ) => new( (byte[])data.NameBytes.Clone(), (byte[])data.OneShot.Clone(),
		data.Optional.Select( pair => pair.Flag ).ToArray(), data.Optional.Select( pair => pair.Value ).ToArray(), data.Awards.Select( pair => pair.Flag ).ToArray(),
		data.Awards.Select( pair => pair.Value ).ToArray(), (ushort[])data.SignA.Clone(), (ushort[])data.SignB.Clone(), data.Available, data.Open, data.ParkNumber, data.NameChanged,
		data.AllResearchCompleted, data.ChallengesDone, data.AllChallengesDone, data.CurrentTechLevel );

	private static ThemeParkIncZoneData DataOf( ThemeParkIncZoneSnapshot zone ) => new()
	{
		NameBytes = (byte[])zone.Name.Clone(), OneShot = (byte[])zone.OneShot.Clone(),
		Optional = zone.OptionalFlags.Zip( zone.OptionalValues ).Select( pair => (pair.First, pair.Second) ).ToArray(),
		Awards = zone.AwardFlags.Zip( zone.AwardValues ).Select( pair => (pair.First, pair.Second) ).ToArray(),
		SignA = (ushort[])zone.SignA.Clone(), SignB = (ushort[])zone.SignB.Clone(), Available = zone.Available, Open = zone.Open, ParkNumber = zone.ParkNumber, NameChanged = zone.NameChanged,
		AllResearchCompleted = zone.AllResearchCompleted, ChallengesDone = zone.ChallengesDone, AllChallengesDone = zone.AllChallengesDone, CurrentTechLevel = zone.CurrentTechLevel
	};

	/// <summary>
	/// The global save's career sections from this career, and the session sections from <paramref name="session"/> (the last global save loaded, or the new-session state); the theme word is
	/// <paramref name="theme"/> (3 for none).
	/// </summary>
	// [BIN:TPI-EXE:0x74C8F0 global save write] the header's theme is 0x42A340() (3 when it is -1); +0x14EC is set to the refresh flag before the body is written
	public ThemeParkIncGlobalSave ToGlobalSave( ThemeParkIncGlobalSave session, int theme, bool refresh )
	{
		ArgumentNullException.ThrowIfNull( session );
		GmiFlag = refresh;
		return new ThemeParkIncGlobalSave
		{
			Theme = theme < 0 ? 3 : theme,
			GoldenTickets = GoldenTickets, Awards = (byte[])awards.Clone(), Cards = (ThemeParkIncCdkEntry[])cards.Clone(), CardCount = CardCount, CardCursor = CardCursor,
			SwearFilter = (byte)(SwearFilter ? 1 : 0), NewCareer = (byte)(NewCareer ? 1 : 0), Flags = (byte[])flags.Clone(), Mission = MissionCounter, Rank = Rank, FiringLine = FiringLine,
			Abilities = (byte[])abilities.Clone(), ObstacleResearched = (byte)(ObstacleResearched ? 1 : 0), ObstacleRemoved = (byte)(ObstacleRemoved ? 1 : 0),
			Statuses = (int[])statuses.Clone(), Regions = (int[])regions.Clone(), Zones = zones.Values.Select( zone => zone.Data.Clone() ).ToList(),
			OptionsBytesRaw = session.OptionsBytesRaw, GmiFlag = (byte)(GmiFlag ? 1 : 0), GameFinished = (byte)(GameFinished ? 1 : 0), FirstHourTime = session.FirstHourTime,
			History = (byte[])history.Clone(), Block592720 = session.Block592720, SharesRaw = session.SharesRaw, MapFlagsRaw = session.MapFlagsRaw, AmbientRaw = session.AmbientRaw
		};
	}

	/// <summary>
	/// Loads a global save into this career as the original's loader does (<c>0x74D110</c>): the reset and an empty zone map first, then every section the read reached, with the mission file of
	/// the read counter parsed before the statuses are read over it. A failed read leaves what was applied, as the original's object does; the caller decides what to do with a failure.
	/// The zone named <paramref name="currentZone"/> becomes the current one (created when missing).
	/// </summary>
	// [BIN:TPI-EXE:0x74D110 global save load] 0x74BB40 and 0x74F9E0 (all zone records freed) before the file is opened; after the body the current zone's record is found by the theme name
	// (0x7513D0) or created, and stored at +0x4C when 0x7509C0 succeeds; [BIN:TPI-EXE:0x74E770 mission reparse] the body calls 0x74C050 after +0x13E5 and before the statuses
	public ThemeParkIncGlobalSaveOutcome Load( ThemeParkIncGlobalSave save, string? currentZone )
	{
		ArgumentNullException.ThrowIfNull( save );
		Reset();
		zones.Clear();
		CurrentZone = null;
		if ( (uint)save.Version < ThemeParkIncGlobalSave.SupportedVersion )
			return ThemeParkIncGlobalSaveOutcome.Failed;
		// the original would load "Miss%02d.sam" of any counter and read the region table past its end on the next advance: a counter outside the table is refused
		if ( !IsValidMissionCounter( save.Mission ) )
			throw new InvalidDataException( $"The global save's mission counter {save.Mission} is outside 0 to {RegionForMission.Count - 1}." );
		GoldenTickets = save.GoldenTickets;
		save.Awards.CopyTo( awards, 0 );
		for ( var index = 0; index < CardSlots; index++ )
			cards[index] = save.Cards[index];
		CardCount = save.CardCount;
		CardCursor = save.CardCursor;
		SwearFilter = save.SwearFilter != 0;
		NewCareer = save.NewCareer != 0;
		save.Flags.CopyTo( flags, 0 );
		MissionCounter = save.Mission;
		Rank = save.Rank;
		FiringLine = save.FiringLine;
		save.Abilities.CopyTo( abilities, 0 );
		ObstacleResearched = save.ObstacleResearched != 0;
		ObstacleRemoved = save.ObstacleRemoved != 0;
		LoadMission();
		if ( save.Statuses is { } savedStatuses )
			savedStatuses.CopyTo( statuses, 0 );
		if ( save.Regions is { } savedRegions )
			savedRegions.CopyTo( regions, 0 );
		foreach ( var zone in save.Zones ?? new List<ThemeParkIncZoneData>() )
			zones[zone.Name] = new ThemeParkIncZoneRecord( zone.Clone() );
		if ( save.OptionsBytesRaw != null )
		{
			GmiFlag = save.GmiFlag != 0;
			GameFinished = save.GameFinished != 0;
		}
		if ( save.History is { } savedHistory )
			history = (byte[])savedHistory.Clone();
		if ( currentZone != null )
			ChangeTheme( currentZone );
		return save.Outcome;
	}
}

/// <summary>What the career's side of a mission completion did: the extent the world loads (the counter before the advance, or -1), whether it advanced, whether the game finished.</summary>
public readonly record struct ThemeParkIncCompletion( int Extent, bool Advanced, bool Finished )
{
	public static ThemeParkIncCompletion None => new( -1, false, false );
}
