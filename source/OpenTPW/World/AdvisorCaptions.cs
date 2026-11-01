using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// The caption of an advisor response: the tag of the text the advisor posts to the message tube when it says the response (field +0x1C of its
/// 32-byte record in the Windows executable's table at 0x768FB8) and the <c>TAG_SYSTEM.str</c> entry that tag shows (field +0xC of the tag's record
/// in the table at 0x75ECA8).
/// </summary>
/// <param name="Kind">The case of the caption formatter (<c>0x509E10</c>) that fills the text's parameter slots: field +0x1C of the tag's record (<see cref="AdvisorCaptionArguments"/>); 0 is the case without arguments.</param>
internal readonly record struct AdvisorCaption( int Tag, int TextEntry, int Kind = 0 );

/// <summary>
/// The shipped caption table (<c>content/data/advisor-captions.toml</c>), copied for interoperability from the Windows executable by
/// <c>tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py</c>. A response that the table does not list has the tag 0x17F, which has no text.
/// </summary>
internal static class AdvisorCaptionTable
{
	public const string RelativePath = "content/data/advisor-captions.toml";
	private static readonly Regex Row = new( @"^\s*\{ response = (\d+), tag = (\d+), text = (\d+)(?:, kind = (\d+))? \},?\s*$", RegexOptions.CultureInvariant );
	private static IReadOnlyDictionary<int, AdvisorCaption>? loaded;

	/// <summary>The table shipped with OpenTPW (loaded on first use; empty when the file is missing or damaged).</summary>
	public static IReadOnlyDictionary<int, AdvisorCaption> Table => loaded ??= LoadShipped();

	public static AdvisorCaption? For( int responseId ) => Table.TryGetValue( responseId, out var caption ) ? caption : null;

	private static IReadOnlyDictionary<int, AdvisorCaption> LoadShipped()
	{
		var path = Path.Combine( AppContext.BaseDirectory, RelativePath );
		try
		{
			return Parse( File.ReadAllText( path ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Advisor captions unavailable ({path}): {exception.Message}" );
			return new Dictionary<int, AdvisorCaption>();
		}
	}

	public static IReadOnlyDictionary<int, AdvisorCaption> Parse( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		var table = new Dictionary<int, AdvisorCaption>();
		var inArray = false;
		var closed = false;
		var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( !inArray && !closed && line.Trim() == "captions = [" )
			{
				inArray = true;
				continue;
			}
			if ( inArray && line.Trim() == "]" )
			{
				inArray = false;
				closed = true;
				continue;
			}
			var match = Row.Match( line );
			if ( !inArray || !match.Success )
				throw new InvalidDataException( $"Advisor captions line {lineNumber} is not a caption row." );
			int Value( int group ) => int.Parse( match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture );
			if ( !table.TryAdd( Value( 1 ), new AdvisorCaption( Value( 2 ), Value( 3 ), match.Groups[4].Success ? Value( 4 ) : 0 ) ) )
				throw new InvalidDataException( $"Advisor caption of response {Value( 1 )} appears twice." );
		}
		if ( !closed )
			throw new InvalidDataException( "Advisor captions have no closed 'captions' array." );
		return table;
	}
}
