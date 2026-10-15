using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>One advisor response: what the original plays when the game asks the advisor for response <see cref="Id"/>.</summary>
/// <param name="Sample">1-based entry of the speech bank (global <c>speechHD.SDT</c>, or the level's when <see cref="Local"/>).</param>
/// <param name="Lip">N of <c>sp_NNN.lip</c>; 0 plays without a LIP file.</param>
/// <param name="Animation">Advisor animation sequence; -1 lets the advisor generate one.</param>
/// <param name="Model">Advisor model index.</param>
/// <param name="Local">Use the level's speech bank and <c>Speech/lips</c> instead of the global ones.</param>
public sealed record AdvisorResponse( int Id, int Sample, int Lip, int Animation, int Model, bool Local );

/// <summary>
/// The advisor response table (<c>content/data/advisor-responses.toml</c>), copied for interoperability
/// from the Mac application's table at initialized-data offset 0x18FF4 (see docs/AUDIO.md). The reader
/// accepts exactly the layout the generator writes: comments, <c>responses = [</c>, one inline table per
/// line and <c>]</c>.
/// </summary>
public static class AdvisorResponses
{
	public const string RelativePath = "content/data/advisor-responses.toml";
	private static readonly Regex Row = new( @"^\s*\{ id = (-?\d+), sample = (-?\d+), lip = (-?\d+), animation = (-?\d+), model = (\d+), local = (true|false) \},?\s*$", RegexOptions.CultureInvariant );
	private static IReadOnlyDictionary<int, AdvisorResponse>? loaded;

	/// <summary>The table shipped with OpenTPW (loaded on first use; empty when the file is missing or damaged).</summary>
	public static IReadOnlyDictionary<int, AdvisorResponse> Table => loaded ??= LoadShipped();

	private static IReadOnlyDictionary<int, AdvisorResponse> LoadShipped()
	{
		var path = Path.Combine( AppContext.BaseDirectory, RelativePath );
		try
		{
			return Parse( File.ReadAllText( path ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Advisor responses unavailable ({path}): {exception.Message}" );
			return new Dictionary<int, AdvisorResponse>();
		}
	}

	public static IReadOnlyDictionary<int, AdvisorResponse> Parse( string text )
	{
		var table = new Dictionary<int, AdvisorResponse>();
		var inArray = false;
		var closed = false;
		var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( !inArray && !closed && line.Trim() == "responses = [" )
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
				throw new InvalidDataException( $"Advisor responses line {lineNumber} is not a response row." );
			int Value( int group ) => int.Parse( match.Groups[group].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture );
			var response = new AdvisorResponse( Value( 1 ), Value( 2 ), Value( 3 ), Value( 4 ), Value( 5 ), match.Groups[6].Value == "true" );
			if ( !table.TryAdd( response.Id, response ) )
				throw new InvalidDataException( $"Advisor response {response.Id} appears twice." );
		}
		if ( !closed )
			throw new InvalidDataException( "Advisor responses have no closed 'responses' array." );
		return table;
	}
}
