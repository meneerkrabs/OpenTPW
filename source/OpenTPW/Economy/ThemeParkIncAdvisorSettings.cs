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
/// The advisor scorer's <c>Advisor.sam</c> words, bound through the executable's own schema (<c>content/data/tpi-advisor-schema.toml</c>) and parser (<see cref="ThemeParkIncSamObject"/>), with its
/// reset defaults for a key the file does not set (the shipped file has no group 8, whose words stay 0): <c>GeneralAdvisor</c> and the ten <c>MessageGroups</c> the queue reads, and every
/// other word, which the score callbacks read (<see cref="Word"/>). Every control is read from the bound words, so an instance is immutable and two instances are equal when their words are.
/// </summary>
// [BIN:TPI-EXE:0x71B6B0 scorer constructor] the scorer at 0xC65220 binds its SAM object at +0xC (0x418F60, schema 0x97C180) and loads "Advisor\Advisor.sam" with 0x419B20(path, 0); a failure
// is logged and the reset values stay. Slot n is the scorer's +0x14 + 4n: +0x18 MinTimeAnyMessage, +0x20 MinScoreForConsideration, +0x24 UIPanelDelay, +0x28 UISelectionModeMessageFilter,
// MessageGroups[g] from +0x2C with six words per group (count dword +0x11C), then GroupTimes and the condition keys (HelpMessage.Score +0x49C, TutorialUIPanel.Score +0x770, ...)
public sealed class ThemeParkIncAdvisorSettings : IEquatable<ThemeParkIncAdvisorSettings>
{
	public const string Path = "/Advisor/Advisor.sam";
	public const string SchemaPath = "content/data/tpi-advisor-schema.toml";
	public const int GroupCount = 10;
	/// <summary>The schema's records before its end record (<c>0x97C180</c>).</summary>
	public const int SchemaRecords = 1006;

	private static readonly Regex SchemaRow = new( @"^\s*\{ kind = (\d+), name = ""([^""]*)""(?:, minimum = (-?\d+), maximum = (-?\d+))?(?:, count = (\d+))? \},?\s*$", RegexOptions.CultureInvariant );
	private static ThemeParkIncSamRecord[]? schema;
	private static ThemeParkIncAdvisorSettings? reset;

	/// <summary>
	/// The scorer's schema shipped with OpenTPW, loaded on first use. A missing or damaged file is refused (<see cref="InvalidDataException"/>): without it the score callbacks that read a scorer
	/// word could not be answered, so no Theme Park Inc advisor is built.
	/// </summary>
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
			Log.Error( $"Theme Park Inc advisor schema unavailable ({path}): {exception.Message}" );
			throw new InvalidDataException( $"The Theme Park Inc advisor's Advisor.sam schema ({SchemaPath}) is missing or damaged: {exception.Message}", exception );
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

	// the SAM object's words: slot n (object +8 + 4n) is the scorer's +0x14 + 4n
	private readonly int[] words;

	private ThemeParkIncAdvisorSettings( int[] words )
	{
		this.words = words;
		var groups = new ThemeParkIncAdvisorGroup[GroupCount];
		for ( var group = 0; group < GroupCount; group++ )
		{
			var at = 0x2C + 24 * group;
			groups[group] = new( unchecked( (uint)Word( at ) ), unchecked( (uint)Word( at + 4 ) ), unchecked( (uint)Word( at + 8 ) ), unchecked( (uint)Word( at + 0xC ) ),
				Word( at + 0x10 ) != 0, Word( at + 0x14 ) );
		}
		Groups = Array.AsReadOnly( groups );
	}

	/// <summary><c>GeneralAdvisor.MinScoreForConsideration</c> (<c>+0x20</c>).</summary>
	public int MinimumScore => Word( 0x20 );
	/// <summary><c>GeneralAdvisor.UIPanelDelay</c> (<c>+0x24</c>).</summary>
	public int PanelDelay => Word( 0x24 );
	/// <summary><c>GeneralAdvisor.UISelectionModeMessageFilter</c> (<c>+0x28</c>).</summary>
	public int SelectionModeFilter => Word( 0x28 );
	/// <summary><c>MessageGroups[g]</c> (<c>+0x2C + 24·g</c>).</summary>
	public IReadOnlyList<ThemeParkIncAdvisorGroup> Groups { get; }

	/// <summary>The settings of a scorer whose file did not load: every word 0.</summary>
	public static ThemeParkIncAdvisorSettings Reset => reset ??= Bind( null );

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
		return new ThemeParkIncAdvisorSettings( sam.Words.ToArray() );
	}

	/// <summary>
	/// The scorer's word at <paramref name="scorerOffset"/> (<c>0xC65220 + offset</c>), as a score callback that reads it (<c>mov eax, [ecx + offset]; ret</c>) returns it.
	/// </summary>
	public int Word( int scorerOffset )
	{
		// the SAM object's slot n (object +8 + 4n) is the scorer's +0x14 + 4n
		var slot = (scorerOffset - 0x14) / 4;
		if ( scorerOffset % 4 != 0 || slot < 1 || slot >= words.Length )
			throw new ArgumentOutOfRangeException( nameof( scorerOffset ), $"Scorer offset {scorerOffset:X} is not a word the schema binds." );
		return words[slot];
	}

	public bool Equals( ThemeParkIncAdvisorSettings? other ) => other is not null && words.AsSpan().SequenceEqual( other.words );

	public override bool Equals( object? obj ) => Equals( obj as ThemeParkIncAdvisorSettings );

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach ( var word in words )
			hash.Add( word );
		return hash.ToHashCode();
	}
}
