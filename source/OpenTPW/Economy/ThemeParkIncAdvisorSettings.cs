using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>One message group's controls (<c>MessageGroups[g]</c>, the scorer's <c>+0x2C + 24·g</c>): the repeat intervals by play count, say-only-once and the slaps that discard it.</summary>
public readonly record struct ThemeParkIncAdvisorGroup( uint First, uint Second, uint Third, uint Later, bool SayOnlyOnce, int DiscardAfterSlaps )
{
	/// <summary>The repeat interval after <paramref name="plays"/> plays (<c>0x71BAC0</c>), in world ticks shifted right by two.</summary>
	// [BIN:TPI-EXE:0x71BAC0 repeat interval] play count 0, 1 and 2 read the group's +0, +4 and +8, every other count its +0xC
	public uint RepeatInterval( int plays ) => plays switch { 0 => First, 1 => Second, 2 => Third, _ => Later };
}

/// <summary>
/// The advisor scorer's <c>Advisor.sam</c> controls: <c>GeneralAdvisor</c> and the ten <c>MessageGroups</c> the queue reads, and every other word of the scorer, which the score callbacks read
/// (<see cref="Word"/>), bound through the executable's own schema (<c>content/data/tpi-advisor-schema.toml</c>) and parser (<see cref="ThemeParkIncSamObject"/>), with its reset defaults for a
/// key the file does not set (the shipped file has no group 8, whose words stay 0).
/// </summary>
// [BIN:TPI-EXE:0x71B6B0 scorer constructor] the scorer at 0xC65220 binds its SAM object at +0xC (0x418F60, schema 0x97C180) and loads "Advisor\Advisor.sam" with 0x419B20(path, 0); a failure
// is logged and the reset values stay. Slot n is the scorer's +0x14 + 4n: +0x18 MinTimeAnyMessage, +0x20 MinScoreForConsideration, +0x24 UIPanelDelay, +0x28 UISelectionModeMessageFilter,
// MessageGroups[g] from +0x2C with six words per group (count dword +0x11C), then GroupTimes and the condition keys (HelpMessage.Score +0x49C, TutorialUIPanel.Score +0x770, ...)
public sealed record ThemeParkIncAdvisorSettings( int MinimumScore, int PanelDelay, int SelectionModeFilter, IReadOnlyList<ThemeParkIncAdvisorGroup> Groups )
{
	public const string Path = "/Advisor/Advisor.sam";
	public const string SchemaPath = "content/data/tpi-advisor-schema.toml";
	public const int GroupCount = 10;
	/// <summary>The schema's records before its end record (<c>0x97C180</c>).</summary>
	public const int SchemaRecords = 1006;

	// the leading records the queue reads, which a missing schema file falls back to
	private static readonly ThemeParkIncSamRecord[] Prefix =
	{
		new( ThemeParkIncSamKind.StructBegin, "" ), new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeAnyMessage" ), new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeSameMessage" ),
		new( ThemeParkIncSamKind.NonNegativeInt, "MinScoreForConsideration" ), new( ThemeParkIncSamKind.Int, "UIPanelDelay" ), new( ThemeParkIncSamKind.Int, "UISelectionModeMessageFilter" ),
		new( ThemeParkIncSamKind.StructEnd, "GeneralAdvisor" ), new( ThemeParkIncSamKind.ArrayBegin, "" ), new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeSameMessage1" ),
		new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeSameMessage2" ), new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeSameMessage3" ),
		new( ThemeParkIncSamKind.NonNegativeInt, "MinTimeSameMessageX" ), new( ThemeParkIncSamKind.IntRange, "SayOnlyOnce", 0, 2 ),
		new( ThemeParkIncSamKind.NonNegativeInt, "DiscardAfterSlaps" ), new( ThemeParkIncSamKind.ArrayEnd, "MessageGroups", Count: GroupCount ), new( ThemeParkIncSamKind.End, "" )
	};

	private static readonly Regex SchemaRow = new( @"^\s*\{ kind = (\d+), name = ""([^""]*)""(?:, minimum = (-?\d+), maximum = (-?\d+))?(?:, count = (\d+))? \},?\s*$", RegexOptions.CultureInvariant );
	private static ThemeParkIncSamRecord[]? schema;

	/// <summary>The scorer's schema shipped with OpenTPW, loaded on first use; the queue's leading records when the file is missing or damaged.</summary>
	public static IReadOnlyList<ThemeParkIncSamRecord> Schema => schema ??= LoadSchema();

	private static ThemeParkIncSamRecord[] LoadSchema()
	{
		var path = System.IO.Path.Combine( AppContext.BaseDirectory, SchemaPath );
		try
		{
			return ParseSchema( File.ReadAllText( path ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Theme Park Inc advisor schema unavailable ({path}): {exception.Message}; only the queue's keys are bound." );
			return Prefix;
		}
	}

	/// <summary>Parses the schema file: its records in order, ending with the end record.</summary>
	public static ThemeParkIncSamRecord[] ParseSchema( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		var records = new List<ThemeParkIncSamRecord>();
		var open = false;
		foreach ( var raw in text.Split( '\n' ) )
		{
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( line.Trim() == "records = [" )
			{
				open = true;
				continue;
			}
			if ( line.Trim() == "]" && open )
			{
				open = false;
				continue;
			}
			if ( !open || SchemaRow.Match( line ) is not { Success: true } match )
				throw new InvalidDataException( "Theme Park Inc advisor schema line is not a schema record." );
			int Number( int group ) => match.Groups[group].Success ? int.Parse( match.Groups[group].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture ) : 0;
			records.Add( new ThemeParkIncSamRecord( (ThemeParkIncSamKind)Number( 1 ), match.Groups[2].Value, Number( 3 ), Number( 4 ), Number( 5 ) ) );
		}
		if ( records.Count != SchemaRecords + 1 || records[^1].Kind != ThemeParkIncSamKind.End || records.Take( SchemaRecords ).Any( record => record.Kind == ThemeParkIncSamKind.End ) )
			throw new InvalidDataException( $"Theme Park Inc advisor schema holds {records.Count} records; the executable's has {SchemaRecords} and its end record." );
		return records.ToArray();
	}

	private IReadOnlyList<int> words = Array.Empty<int>();

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
	public static ThemeParkIncAdvisorSettings Bind( byte[]? file ) => Bind( file, Schema );

	/// <summary>Binds a file's bytes through <paramref name="records"/> (a schema, ending with its end record).</summary>
	public static ThemeParkIncAdvisorSettings Bind( byte[]? file, IReadOnlyList<ThemeParkIncSamRecord> records )
	{
		var sam = new ThemeParkIncSamObject( records );
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
		return new ThemeParkIncAdvisorSettings( Word( 0x20 ), Word( 0x24 ), Word( 0x28 ), groups ) { words = sam.Words.ToArray() };
	}

	/// <summary>
	/// The scorer's word at <paramref name="scorerOffset"/> (<c>0xC65220 + offset</c>), as a score callback that reads it (<c>mov eax, [ecx + offset]; ret</c>) returns it; the queue's three
	/// <c>GeneralAdvisor</c> words come from this record's properties.
	/// </summary>
	public int Word( int scorerOffset )
	{
		switch ( scorerOffset )
		{
			case 0x20: return MinimumScore;
			case 0x24: return PanelDelay;
			case 0x28: return SelectionModeFilter;
		}
		// the SAM object's slot n (object +8 + 4n) is the scorer's +0x14 + 4n
		var slot = (scorerOffset - 0x14) / 4;
		if ( scorerOffset % 4 != 0 || slot < 1 || slot >= words.Count )
			throw new ArgumentOutOfRangeException( nameof( scorerOffset ), $"Scorer offset {scorerOffset:X} is not a word the schema binds." );
		return words[slot];
	}
}
