using System.Globalization;

namespace OpenTPW;

/// <summary>One <c>Key value...</c> line of an original <c>.sam</c> settings file.</summary>
public sealed record SamEntry( string Key, IReadOnlyList<string> Values, string Source, int Line );

/// <summary>
/// A parsed original <c>.sam</c> file. Unlike <see cref="SettingsFile"/> this keeps every value
/// token of a line (<c>PeepTypes[0].PreferredExcitement.StartingCash.BoredomThreshold 80 300 40</c>),
/// strips quotes from string values and skips <c>---</c>-delimited shape blocks. Values end at the
/// first token that is neither numeric nor quoted, because the original files append free-text
/// comments without a <c>#</c> (<c>CostOfUpgrade 1000 cash cost when buying this item</c>).
/// </summary>
public sealed class SamDocument
{
	public SamDocument( string source, IReadOnlyList<SamEntry> entries )
	{
		Source = source;
		Entries = entries;
	}

	public string Source { get; }
	public IReadOnlyList<SamEntry> Entries { get; }

	public static SamDocument Parse( string text, string source )
	{
		ArgumentNullException.ThrowIfNull( text );
		var entries = new List<SamEntry>();
		var inBlock = false;
		var lines = text.Replace( "\r\n", "\n" ).Replace( '\r', '\n' ).Split( '\n' );
		for ( var index = 0; index < lines.Length; index++ )
		{
			var line = lines[index].Trim();
			if ( line.StartsWith( "---", StringComparison.Ordinal ) )
			{
				inBlock = !inBlock;
				continue;
			}
			if ( inBlock || line.Length == 0 || line[0] == '#' || line.StartsWith( "//", StringComparison.Ordinal ) )
				continue;
			var keyEnd = 0;
			while ( keyEnd < line.Length && !char.IsWhiteSpace( line[keyEnd] ) )
				keyEnd++;
			var key = line[..keyEnd];
			var values = ReadValues( line.AsSpan( keyEnd ) );
			if ( values.Count > 0 && key.Contains( '.' ) )
				entries.Add( new SamEntry( key, values, source, index + 1 ) );
		}
		return new SamDocument( source, entries );
	}

	private static List<string> ReadValues( ReadOnlySpan<char> rest )
	{
		var values = new List<string>();
		var position = 0;
		while ( true )
		{
			while ( position < rest.Length && char.IsWhiteSpace( rest[position] ) )
				position++;
			if ( position >= rest.Length || rest[position] == '#' )
				break;
			if ( rest[position] == '"' )
			{
				var close = rest[(position + 1)..].IndexOf( '"' );
				var end = close < 0 ? rest.Length : position + 1 + close;
				values.Add( rest[(position + 1)..end].ToString() );
				position = Math.Min( rest.Length, end + 1 );
				continue;
			}
			var start = position;
			while ( position < rest.Length && !char.IsWhiteSpace( rest[position] ) && rest[position] != '#' )
				position++;
			var token = rest[start..position].ToString();
			if ( !double.TryParse( token, NumberStyles.Float, CultureInfo.InvariantCulture, out _ ) )
				break;
			values.Add( token );
		}
		return values;
	}
}

/// <summary>
/// Layered original settings: later documents override earlier ones key by key (case-insensitive),
/// the way <c>levels/Standard.sam</c>, <c>levels/&lt;theme&gt;/Standard.sam</c> and
/// <c>Easy_Standard.sam</c> repeat and change subsets of the same keys. Lookups report which file
/// supplied a value so evidence can be traced; missing required keys throw.
/// </summary>
public sealed class SamSettings
{
	private readonly Dictionary<string, SamEntry> values = new( StringComparer.OrdinalIgnoreCase );
	private readonly List<string> sources = new();

	public SamSettings( IEnumerable<SamDocument> layers )
	{
		foreach ( var layer in layers )
		{
			sources.Add( layer.Source );
			foreach ( var entry in layer.Entries )
				values[entry.Key] = entry;
		}
	}

	public IReadOnlyList<string> Sources => sources;
	public IEnumerable<SamEntry> Entries => values.Values;

	public bool Contains( string key ) => values.ContainsKey( key );

	public SamEntry? Find( string key ) => values.TryGetValue( key, out var entry ) ? entry : null;

	public SamEntry Get( string key ) => values.TryGetValue( key, out var entry )
		? entry
		: throw new KeyNotFoundException( $"Original setting '{key}' is missing from {string.Join( ", ", sources )}." );

	public long GetLong( string key, int index = 0 ) => ParseLong( Get( key ), index );

	public long GetLong( string key, long fallback ) => values.TryGetValue( key, out var entry ) ? ParseLong( entry, 0 ) : fallback;

	public int GetInt( string key, int index = 0 ) => checked((int)GetLong( key, index ));

	public int GetInt( string key, int fallback, bool optional ) => optional && !values.ContainsKey( key ) ? fallback : GetInt( key );

	public double GetDouble( string key ) => ParseDouble( Get( key ), 0 );

	public string GetString( string key ) => Get( key ).Values[0];

	/// <summary>Indices <c>n</c> for which <c>prefix[n].suffix</c> exists, ascending.</summary>
	public IReadOnlyList<int> GetIndices( string prefix, string suffix )
	{
		var result = new SortedSet<int>();
		var head = prefix + "[";
		var tail = "]." + suffix;
		foreach ( var key in values.Keys )
		{
			if ( key.Length > head.Length + tail.Length && key.StartsWith( head, StringComparison.OrdinalIgnoreCase ) && key.EndsWith( tail, StringComparison.OrdinalIgnoreCase )
				&& int.TryParse( key.AsSpan( head.Length, key.Length - head.Length - tail.Length ), NumberStyles.None, CultureInfo.InvariantCulture, out var index ) )
				result.Add( index );
		}
		return result.ToList();
	}

	private static long ParseLong( SamEntry entry, int index )
	{
		if ( index >= entry.Values.Count )
			throw new InvalidDataException( $"Original setting '{entry.Key}' ({entry.Source}:{entry.Line}) has no value {index}." );
		var text = entry.Values[index];
		if ( long.TryParse( text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value ) )
			return value;
		throw new InvalidDataException( $"Original setting '{entry.Key}' ({entry.Source}:{entry.Line}) is not an integer: '{text}'." );
	}

	private static double ParseDouble( SamEntry entry, int index )
	{
		if ( index < entry.Values.Count && double.TryParse( entry.Values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) )
			return value;
		throw new InvalidDataException( $"Original setting '{entry.Key}' ({entry.Source}:{entry.Line}) is not a number." );
	}
}
