namespace OpenTPW;

/// <summary>One <c>ExitStatistic[i]</c> of a mission file (stride 0x1C from the mission object's <c>+0x23C</c>).</summary>
public readonly record struct ThemeParkIncExitStatistic( int TextId, int Type, int InTheme, int TargetValue, int TargetTime, int TargetObject, int TargetStaffType );

/// <summary>One <c>ObstacleStaff[i]</c> of a mission file (stride 0xC from <c>+0x2E0</c>).</summary>
public readonly record struct ThemeParkIncObstacleStaff( int StaffType, int Ability, int Number );

/// <summary>
/// Theme Park Inc's mission object (docs/reverse/TPI-CAREER.md §1): the 0x330 bytes embedded at the career's <c>+0x10B4</c>, bound by the schema table <c>0x9A0C68</c> through the generic SAM binding
/// (<see cref="ThemeParkIncSamObject"/>) and filled from <c>missions\Miss%02d.sam</c>. Offsets are the mission object's (career offset minus 0x10B4).
/// </summary>
// [BIN:TPI-EXE:0x74B490 mission object] the career constructor writes the mission vtable 0x940920 at +0x10B4 (0x74B535: 0x74B600 returns &schema[i] = 0x9A0C68 + i * 0x3C, 0x74B630 the data base
// this + 8) and binds it through 0x418F60
public sealed class ThemeParkIncMission
{
	/// <summary>The schema table <c>0x9A0C68</c> (50 records): kinds, names, the range of each kind-6 leaf and each array's count.</summary>
	// [BIN:TPI-EXE:0x9A0C68 mission schema] Info{Number 4, NoResearch 6 [0,2)}, Completion{ThemeOpened 6 [0,2), ThemeNumber, CutsceneObject, ObstacleOpened 4, EndOfGame 6 [0,2),
	// LoadNewHoardings 6 [0,2), EndingTheme 4}, TechLevel{Theme 6 [0,3), NewMaxGroup 4}, BoundaryClicks[64]{X, Y}, ExitStatistic[5]{TextId, Type, InTheme, TargetValue, TargetTime,
	// TargetObject, TargetStaffType}, Obstacle{Type, InTheme, ResearchEffort, RemoveEffort, GoldenTickets}, ObstacleStaff[5]{StaffType, Ability, Number}, Promotion{Number, StockAward},
	// RequiredChallenges{Theme, Number}, end
	public static IReadOnlyList<ThemeParkIncSamRecord> Schema { get; } = new ThemeParkIncSamRecord[]
	{
		new( ThemeParkIncSamKind.StructBegin, "" ),
		new( ThemeParkIncSamKind.Int, "Number" ),
		new( ThemeParkIncSamKind.IntRange, "NoResearch", 0, 2 ),
		new( ThemeParkIncSamKind.StructEnd, "Info" ),
		new( ThemeParkIncSamKind.StructBegin, "" ),
		new( ThemeParkIncSamKind.IntRange, "ThemeOpened", 0, 2 ),
		new( ThemeParkIncSamKind.Int, "ThemeNumber" ),
		new( ThemeParkIncSamKind.Int, "CutsceneObject" ),
		new( ThemeParkIncSamKind.Int, "ObstacleOpened" ),
		new( ThemeParkIncSamKind.IntRange, "EndOfGame", 0, 2 ),
		new( ThemeParkIncSamKind.IntRange, "LoadNewHoardings", 0, 2 ),
		new( ThemeParkIncSamKind.Int, "EndingTheme" ),
		new( ThemeParkIncSamKind.StructEnd, "Completion" ),
		new( ThemeParkIncSamKind.StructBegin, "" ),
		new( ThemeParkIncSamKind.IntRange, "Theme", 0, 3 ),
		new( ThemeParkIncSamKind.Int, "NewMaxGroup" ),
		new( ThemeParkIncSamKind.StructEnd, "TechLevel" ),
		new( ThemeParkIncSamKind.ArrayBegin, "" ),
		new( ThemeParkIncSamKind.Int, "X" ),
		new( ThemeParkIncSamKind.Int, "Y" ),
		new( ThemeParkIncSamKind.ArrayEnd, "BoundaryClicks", Count: BoundaryClickCapacity ),
		new( ThemeParkIncSamKind.ArrayBegin, "" ),
		new( ThemeParkIncSamKind.Int, "TextId" ),
		new( ThemeParkIncSamKind.Int, "Type" ),
		new( ThemeParkIncSamKind.Int, "InTheme" ),
		new( ThemeParkIncSamKind.Int, "TargetValue" ),
		new( ThemeParkIncSamKind.Int, "TargetTime" ),
		new( ThemeParkIncSamKind.Int, "TargetObject" ),
		new( ThemeParkIncSamKind.Int, "TargetStaffType" ),
		new( ThemeParkIncSamKind.ArrayEnd, "ExitStatistic", Count: ExitStatisticCapacity ),
		new( ThemeParkIncSamKind.StructBegin, "" ),
		new( ThemeParkIncSamKind.Int, "Type" ),
		new( ThemeParkIncSamKind.Int, "InTheme" ),
		new( ThemeParkIncSamKind.Int, "ResearchEffort" ),
		new( ThemeParkIncSamKind.Int, "RemoveEffort" ),
		new( ThemeParkIncSamKind.Int, "GoldenTickets" ),
		new( ThemeParkIncSamKind.StructEnd, "Obstacle" ),
		new( ThemeParkIncSamKind.ArrayBegin, "" ),
		new( ThemeParkIncSamKind.Int, "StaffType" ),
		new( ThemeParkIncSamKind.Int, "Ability" ),
		new( ThemeParkIncSamKind.Int, "Number" ),
		new( ThemeParkIncSamKind.ArrayEnd, "ObstacleStaff", Count: ObstacleStaffCapacity ),
		new( ThemeParkIncSamKind.StructBegin, "" ),
		new( ThemeParkIncSamKind.Int, "Number" ),
		new( ThemeParkIncSamKind.Int, "StockAward" ),
		new( ThemeParkIncSamKind.StructEnd, "Promotion" ),
		new( ThemeParkIncSamKind.StructBegin, "" ),
		new( ThemeParkIncSamKind.Int, "Theme" ),
		new( ThemeParkIncSamKind.Int, "Number" ),
		new( ThemeParkIncSamKind.StructEnd, "RequiredChallenges" ),
		new( ThemeParkIncSamKind.End, "" )
	};

	public const int BoundaryClickCapacity = 64;
	public const int ExitStatisticCapacity = 5;
	public const int ObstacleStaffCapacity = 5;

	/// <summary>The mission object's size (career <c>+0x10B4</c> to <c>+0x13E4</c>).</summary>
	public const int Size = 0x330;

	public ThemeParkIncMission() => Object = new ThemeParkIncSamObject( Schema );

	/// <summary>The bound words.</summary>
	public ThemeParkIncSamObject Object { get; }

	public int Number => Object[0x0C];
	public int NoResearch => Object[0x10];
	public int ThemeOpened => Object[0x14];
	public int ThemeNumber => Object[0x18];
	public int CutsceneObject => Object[0x1C];
	public int ObstacleOpened => Object[0x20];
	public int EndOfGame => Object[0x24];
	public int LoadNewHoardings => Object[0x28];
	public int EndingTheme => Object[0x2C];
	public int TechLevelTheme => Object[0x30];
	public int TechLevelNewMaxGroup => Object[0x34];
	/// <summary>The <c>BoundaryClicks</c> count dword (<c>+0x238</c>): one past the highest index any line set.</summary>
	public int BoundaryClickCount => Object[0x238];
	/// <summary>The <c>ExitStatistic</c> count dword (<c>+0x2C8</c>).</summary>
	public int ExitStatisticCount => Object[0x2C8];
	public int ObstacleType => Object[0x2CC];
	public int ObstacleInTheme => Object[0x2D0];
	public int ObstacleResearchEffort => Object[0x2D4];
	public int ObstacleRemoveEffort => Object[0x2D8];
	public int ObstacleGoldenTickets => Object[0x2DC];
	/// <summary>The <c>ObstacleStaff</c> count dword (<c>+0x31C</c>), which the ability discovery <c>0x74C3C0</c> walks.</summary>
	public int ObstacleStaffCount => Object[0x31C];
	/// <summary><c>Promotion.Number</c>: the rank the mission requires (not a reward).</summary>
	public int PromotionNumber => Object[0x320];
	public int PromotionStockAward => Object[0x324];
	public int RequiredChallengesTheme => Object[0x328];
	public int RequiredChallengesNumber => Object[0x32C];

	public (int X, int Y) BoundaryClick( int index ) => (Object[Element( 0x38, 8, index, BoundaryClickCapacity )], Object[Element( 0x3C, 8, index, BoundaryClickCapacity )]);

	public ThemeParkIncExitStatistic ExitStatistic( int index )
	{
		var at = Element( 0x23C, 0x1C, index, ExitStatisticCapacity );
		return new ThemeParkIncExitStatistic( Object[at], Object[at + 4], Object[at + 8], Object[at + 0xC], Object[at + 0x10], Object[at + 0x14], Object[at + 0x18] );
	}

	public ThemeParkIncObstacleStaff ObstacleStaff( int index )
	{
		var at = Element( 0x2E0, 0xC, index, ObstacleStaffCapacity );
		return new ThemeParkIncObstacleStaff( Object[at], Object[at + 4], Object[at + 8] );
	}

	private static int Element( int start, int stride, int index, int capacity ) =>
		index >= 0 && index < capacity ? start + stride * index : throw new ArgumentOutOfRangeException( nameof( index ) );

	/// <summary>The path of mission <paramref name="counter"/> in the game file system: <c>missions/Miss%02d.sam</c>.</summary>
	// [BIN:TPI-EXE:0x74C083 mission path] "%s\Miss%02d.sam" (0x9E9264) with the career's counter; the base is the data folder's missions directory (the shipped files are Data\missions\miss00..17.sam)
	public static string PathOf( int counter ) => $"/missions/Miss{counter.ToString( System.Globalization.CultureInfo.InvariantCulture ).PadLeft( 2, '0' )}.sam";
}
