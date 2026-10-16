using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// One record of the original's message descriptor table (<c>0x76E300</c>, 351 records of 0x38 bytes, filled by the initializer <c>0x5A0A50</c>):
/// <see cref="Callback"/> is the address of its score callback (a getter of an <c>Advisor.sam</c> score, <c>0x5D60C0</c> for a score of 0, or a computation).
/// </summary>
internal readonly record struct AdvisorDescriptorRow( int MessageId, int FirstResponseId, int ResponseCount, byte Group, byte MaximumPendingDuplicates, byte GameMode, uint Callback );

/// <summary>
/// The shipped descriptor table (<c>content/data/advisor-descriptors.toml</c>): all 351 descriptors of the Windows executable, copied for
/// interoperability by <c>tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py</c>. Every message of an original save's advisor controller
/// has one; the producers of most messages (<see cref="Callback"/>, the game state behind their scores) are not bound.
/// </summary>
internal static class AdvisorDescriptorTable
{
	public const string RelativePath = "content/data/advisor-descriptors.toml";
	/// <summary>The number of records of the original's table; a message ID at or above it has no descriptor.</summary>
	public const int Count = 351;
	private static readonly Regex Row = new( @"^\s*\{ message = (\d+), response = (\d+), count = (\d+), group = (\d+), duplicates = (\d+), mode = (\d+), callback = 0x([0-9a-fA-F]+) \},?\s*$", RegexOptions.CultureInvariant );
	private static IReadOnlyList<AdvisorDescriptorRow>? loaded;

	/// <summary>The table shipped with OpenTPW (loaded on first use; empty when the file is missing or damaged).</summary>
	public static IReadOnlyList<AdvisorDescriptorRow> Rows => loaded ??= LoadShipped();

	private static IReadOnlyList<AdvisorDescriptorRow> LoadShipped()
	{
		var path = Path.Combine( AppContext.BaseDirectory, RelativePath );
		try
		{
			return Parse( File.ReadAllText( path ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Advisor descriptors unavailable ({path}): {exception.Message}" );
			return Array.Empty<AdvisorDescriptorRow>();
		}
	}

	public static IReadOnlyList<AdvisorDescriptorRow> Parse( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		var rows = new List<AdvisorDescriptorRow>();
		var inArray = false;
		var closed = false;
		var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( !inArray && !closed && line.Trim() == "messages = [" )
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
				throw new InvalidDataException( $"Advisor descriptors line {lineNumber} is not a descriptor row." );
			int Value( int group ) => int.Parse( match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture );
			if ( Value( 1 ) != rows.Count || Value( 1 ) >= Count || Value( 3 ) <= 0 || Value( 4 ) > byte.MaxValue || Value( 5 ) > byte.MaxValue || Value( 6 ) > 2 )
				throw new InvalidDataException( $"Advisor descriptor {Value( 1 )} is out of order or has a value out of range." );
			rows.Add( new AdvisorDescriptorRow( Value( 1 ), Value( 2 ), Value( 3 ), (byte)Value( 4 ), (byte)Value( 5 ), (byte)Value( 6 ), uint.Parse( match.Groups[7].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture ) ) );
		}
		if ( !closed )
			throw new InvalidDataException( "Advisor descriptors have no closed 'messages' array." );
		return rows;
	}
}
