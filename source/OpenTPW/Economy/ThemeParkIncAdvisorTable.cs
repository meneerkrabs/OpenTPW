using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// One row of Theme Park Inc's advisor descriptor table (<c>0x9DE020</c>, 56-byte rows filled by <c>0x729530</c>): <see cref="Scan"/> rows are the condition scan's (kind 0),
/// the others are posted by code (kind 1). <see cref="ConstantScore"/> is the score of a callback that is a constant (<c>mov eax, imm32; ret</c>), null for every other callback.
/// </summary>
public readonly record struct ThemeParkIncAdvisorDescriptor( int Row, bool Scan, int Message, int Group, int Class, uint Callback, byte Duplicates, int Response, int Count,
	int VariantRule, int? ConstantScore );

/// <summary>
/// The descriptor table shipped with OpenTPW (<c>content/data/tpi-advisor-descriptors.toml</c>): the 664 rows of the executable before its terminator, copied for interoperability by
/// <c>tools/ppc-analysis/oracle_x86/tpi_advisor_queue_oracle.py</c> (docs/reverse/TPI-ADVISOR.md). A message's lookups take its first row in table order, as every native lookup does.
/// </summary>
// [BIN:TPI-EXE:0x71B770 descriptor lookup] the lookups scan from row 0 up to the row count 0x9E7198 (665) or a kind-2 row and take the first row whose +4 is the message; 0x285 (the
// response id the scan returns for none) is refused before the scan
public sealed class ThemeParkIncAdvisorTable
{
	public const string RelativePath = "content/data/tpi-advisor-descriptors.toml";
	public const int Rows = 664;
	/// <summary>The id the scan returns for no message (<c>0x285</c>); no lookup accepts it.</summary>
	public const int NoMessage = 0x285;
	private static readonly Regex Row = new( @"^\s*\{ row = (\d+), kind = ([01]), message = 0x([0-9a-f]+), group = (\d+), class = (\d+), callback = 0x([0-9a-f]+), duplicates = (\d+), response = (\d+), count = (\d+), variant = (\d+)(?:, score = (-?\d+))? \},?\s*$", RegexOptions.CultureInvariant );
	private static ThemeParkIncAdvisorTable? shipped;
	private readonly Dictionary<int, ThemeParkIncAdvisorDescriptor> first = new();

	private ThemeParkIncAdvisorTable( IReadOnlyList<ThemeParkIncAdvisorDescriptor> rows )
	{
		Descriptors = rows;
		foreach ( var row in rows )
			first.TryAdd( row.Message, row );
	}

	public IReadOnlyList<ThemeParkIncAdvisorDescriptor> Descriptors { get; }

	/// <summary>The first row of <paramref name="message"/>, or null (no row, or the no-message id).</summary>
	public ThemeParkIncAdvisorDescriptor? Find( int message ) => message != NoMessage && first.TryGetValue( message, out var row ) ? row : null;

	/// <summary>The table shipped with OpenTPW, loaded on first use; empty (every message refused) when the file is missing or damaged.</summary>
	public static ThemeParkIncAdvisorTable Shipped => shipped ??= LoadShipped();

	private static ThemeParkIncAdvisorTable LoadShipped()
	{
		var path = Path.Combine( AppContext.BaseDirectory, RelativePath );
		try
		{
			return Parse( File.ReadAllText( path ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Theme Park Inc advisor descriptors unavailable ({path}): {exception.Message}" );
			return new ThemeParkIncAdvisorTable( Array.Empty<ThemeParkIncAdvisorDescriptor>() );
		}
	}

	public static ThemeParkIncAdvisorTable Parse( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		var rows = new List<ThemeParkIncAdvisorDescriptor>();
		var state = 0;
		var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( state == 0 && line.Trim() == "messages = [" )
			{
				state = 1;
				continue;
			}
			if ( state == 1 && line.Trim() == "]" )
			{
				state = 2;
				continue;
			}
			var match = Row.Match( line );
			if ( state != 1 || !match.Success )
				throw new InvalidDataException( $"Theme Park Inc advisor descriptors line {lineNumber} is not a descriptor row." );
			int Value( int group ) => int.Parse( match.Groups[group].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture );
			int Hex( int group ) => int.Parse( match.Groups[group].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture );
			if ( Value( 1 ) != rows.Count || Value( 7 ) > byte.MaxValue || Hex( 3 ) >= ThemeParkIncGlobalSave.HistoryEntries || Value( 4 ) >= ThemeParkIncAdvisorSettings.GroupCount )
				throw new InvalidDataException( $"Theme Park Inc advisor descriptor row {Value( 1 )} is out of order or out of range." );
			rows.Add( new ThemeParkIncAdvisorDescriptor( Value( 1 ), Value( 2 ) == 0, Hex( 3 ), Value( 4 ), Value( 5 ), (uint)Hex( 6 ), (byte)Value( 7 ), Value( 8 ), Value( 9 ), Value( 10 ),
				match.Groups[11].Success ? Value( 11 ) : null ) );
		}
		if ( state != 2 || rows.Count != Rows )
			throw new InvalidDataException( $"Theme Park Inc advisor descriptors hold {rows.Count} rows in {(state == 2 ? "a closed" : "no closed")} 'messages' array; the executable's table has {Rows}." );
		return new ThemeParkIncAdvisorTable( rows );
	}
}
