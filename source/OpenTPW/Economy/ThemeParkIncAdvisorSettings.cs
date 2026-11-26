namespace OpenTPW;

/// <summary>One message group's controls (<c>MessageGroups[g]</c>, the scorer's <c>+0x2C + 24·g</c>): the repeat intervals by play count, say-only-once and the slaps that discard it.</summary>
public readonly record struct ThemeParkIncAdvisorGroup( uint First, uint Second, uint Third, uint Later, bool SayOnlyOnce, int DiscardAfterSlaps )
{
	/// <summary>The repeat interval after <paramref name="plays"/> plays (<c>0x71BAC0</c>), in world ticks shifted right by two.</summary>
	// [BIN:TPI-EXE:0x71BAC0 repeat interval] play count 0, 1 and 2 read the group's +0, +4 and +8, every other count its +0xC
	public uint RepeatInterval( int plays ) => plays switch { 0 => First, 1 => Second, 2 => Third, _ => Later };
}

/// <summary>
/// The advisor scorer's <c>Advisor.sam</c> controls the queue reads: <c>GeneralAdvisor</c> and the ten <c>MessageGroups</c>, bound through the executable's own schema and
/// parser (<see cref="ThemeParkIncSamObject"/>), with its reset defaults for a key the file does not set (the shipped file has no group 8, whose words stay 0).
/// </summary>
// [BIN:TPI-EXE:0x71B6B0 scorer constructor] the scorer at 0xC65220 binds its SAM object at +0xC (0x418F60, schema 0x97C180) and loads "Advisor\Advisor.sam" with 0x419B20(path, 0); a failure
// is logged and the reset values stay. Slot n is the scorer's +0x14 + 4n: +0x18 MinTimeAnyMessage, +0x20 MinScoreForConsideration, +0x24 UIPanelDelay, +0x28 UISelectionModeMessageFilter,
// MessageGroups[g] from +0x2C with six words per group (count dword +0x11C). The schema goes on with GroupTimes and the condition keys, which the scan (P3) binds; the prefix here stops the
// parse at the first key after MessageGroups, which the shipped file reaches only once every key bound here is read
public sealed record ThemeParkIncAdvisorSettings( int MinimumScore, int PanelDelay, int SelectionModeFilter, IReadOnlyList<ThemeParkIncAdvisorGroup> Groups )
{
	public const string Path = "/Advisor/Advisor.sam";
	public const int GroupCount = 10;

	private static readonly ThemeParkIncSamRecord[] Schema =
	{
		new( ThemeParkIncSamKind.StructBegin, "" ), new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeAnyMessage" ), new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeSameMessage" ),
		new( ThemeParkIncSamKind.NonNegativeInt, "MinScoreForConsideration" ), new( ThemeParkIncSamKind.Int, "UIPanelDelay" ), new( ThemeParkIncSamKind.Int, "UISelectionModeMessageFilter" ),
		new( ThemeParkIncSamKind.StructEnd, "GeneralAdvisor" ), new( ThemeParkIncSamKind.ArrayBegin, "" ), new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeSameMessage1" ),
		new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeSameMessage2" ), new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeSameMessage3" ),
		new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeSameMessageX" ), new( ThemeParkIncSamKind.IntRange, "SayOnlyOnce", 0, 2 ),
		new( ThemeParkIncSamKind.NonNegativeInt, "DiscardAfterSlaps" ), new( ThemeParkIncSamKind.ArrayEnd, "MessageGroups", Count: GroupCount ), new( ThemeParkIncSamKind.End, "" )
	};

	/// <summary>The settings of a scorer whose file did not load: every word 0.</summary>
	public static readonly ThemeParkIncAdvisorSettings Reset = Bind( null );

	/// <summary>The mounted game's file, or the reset values when no game data is mounted or the file is missing.</summary>
	public static ThemeParkIncAdvisorSettings Load()
	{
		if ( FileSystem?.FileExists( Path ) != true )
		{
			Log.Warning( $"Theme Park Inc advisor: {Path} is missing; the scorer keeps its reset values." );
			return Reset;
		}
		return Bind( FileSystem.ReadAllBytes( Path ) );
	}

	/// <summary>Binds a file's bytes (null: no file) as the scorer's constructor does.</summary>
	public static ThemeParkIncAdvisorSettings Bind( byte[]? file )
	{
		var sam = new ThemeParkIncSamObject( Schema );
		sam.Load( file, overlay: false );
		// the scorer's +0x14 + 4n is the SAM object's +8 + 4n (the object sits at the scorer's +0xC)
		int Word( int scorerOffset ) => sam[scorerOffset - 0xC];
		var groups = new ThemeParkIncAdvisorGroup[GroupCount];
		for ( var group = 0; group < GroupCount; group++ )
		{
			var at = 0x2C + 24 * group;
			groups[group] = new( unchecked( (uint)Word( at ) ), unchecked( (uint)Word( at + 4 ) ), unchecked( (uint)Word( at + 8 ) ), unchecked( (uint)Word( at + 0xC ) ),
				Word( at + 0x10 ) != 0, Word( at + 0x14 ) );
		}
		return new ThemeParkIncAdvisorSettings( Word( 0x20 ), Word( 0x24 ), Word( 0x28 ), groups );
	}
}
