using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// The advisor's response table shipped with OpenTPW (<c>content/data/tpi-advisor-responses.toml</c>): the 994 rows the initializer <c>0x708BF0</c> fills at <c>0xBEF1F0</c>, copied for
/// interoperability by <c>tools/ppc-analysis/oracle_x86/tpi_advisor_tables_oracle.py</c>. A lookup takes the first row of a response id, as <c>0x716D10</c> does (833 has two rows).
/// </summary>
// [BIN:TPI-EXE:0x716D10 response lookup] the rows from 0xBEF1F0 are walked until the id 0x270F; the first whose +0 is the response is taken (none: tag 0x97 / no speech)
public sealed class ThemeParkIncAdviceResponseTable
{
	public const string RelativePath = "content/data/tpi-advisor-responses.toml";
	public const int Rows = 994;
	private static readonly Regex Row = new(
		@"^\s*\{ response = (\d+), voice = (\d+), lip = (\d+), animation = (-?\d+), model = 0x([0-9a-f]+), accessory = (-?\d+), accessory2 = (-?\d+), tag = (\d+) \},?\s*$",
		RegexOptions.CultureInvariant );
	private static ThemeParkIncAdviceResponseTable? shipped;
	private readonly Dictionary<int, ThemeParkIncAdviceResponse> first = new();

	private ThemeParkIncAdviceResponseTable( IReadOnlyList<ThemeParkIncAdviceResponse> rows, IReadOnlyList<int> secondAccessories )
	{
		Responses = rows;
		SecondAccessories = secondAccessories;
		foreach ( var row in rows )
			first.TryAdd( row.Response, row );
	}

	/// <summary>The rows in table order.</summary>
	public IReadOnlyList<ThemeParkIncAdviceResponse> Responses { get; }

	/// <summary>Each row's word <c>+0x18</c>, the second accessory, in table order.</summary>
	public IReadOnlyList<int> SecondAccessories { get; }

	/// <summary>The first row of <paramref name="response"/>, or null.</summary>
	public ThemeParkIncAdviceResponse? Find( int response ) => first.TryGetValue( response, out var row ) ? row : null;

	/// <summary>The table shipped with OpenTPW, loaded on first use; empty (no response speaks) when the file is missing or damaged.</summary>
	public static ThemeParkIncAdviceResponseTable Shipped => shipped ??= LoadShipped();

	private static ThemeParkIncAdviceResponseTable LoadShipped()
	{
		var path = Path.Combine( AppContext.BaseDirectory, RelativePath );
		try
		{
			return Parse( File.ReadAllText( path ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Theme Park Inc advisor responses unavailable ({path}): {exception.Message}" );
			return new ThemeParkIncAdviceResponseTable( Array.Empty<ThemeParkIncAdviceResponse>(), Array.Empty<int>() );
		}
	}

	public static ThemeParkIncAdviceResponseTable Parse( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		var rows = new List<ThemeParkIncAdviceResponse>();
		var seconds = new List<int>();
		var state = 0;
		var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( state == 0 && line.Trim() == "responses = [" )
			{
				state = 1;
				continue;
			}
			if ( state == 1 && line.Trim() == "]" )
			{
				state = 2;
				continue;
			}
			if ( state != 1 || Row.Match( line ) is not { Success: true } match )
				throw new InvalidDataException( $"Theme Park Inc advisor responses line {lineNumber} is not a response row." );
			int Value( int group ) => int.Parse( match.Groups[group].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture );
			var model = uint.Parse( match.Groups[5].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture );
			// the word +0x10: its low word is the model that shows the clips, its high word the level's speech bank (0x716284)
			rows.Add( new ThemeParkIncAdviceResponse( Value( 1 ), (uint)Value( 2 ), Value( 3 ), Value( 4 ), (int)(model & 0xFFFF), (model >> 16) != 0, Value( 6 ), Value( 8 ) ) );
			seconds.Add( Value( 7 ) );
		}
		if ( state != 2 || rows.Count != Rows )
			throw new InvalidDataException( $"Theme Park Inc advisor responses hold {rows.Count} rows in {(state == 2 ? "a closed" : "no closed")} 'responses' array; the executable's table has {Rows}." );
		return new ThemeParkIncAdviceResponseTable( rows, seconds );
	}
}
