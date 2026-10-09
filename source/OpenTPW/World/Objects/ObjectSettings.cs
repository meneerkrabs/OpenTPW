using System.Globalization;

namespace OpenTPW;

/// <summary>
/// One original object description file (<c>.sam</c>) as written by the designers: <c>Key Value comment</c>
/// lines, <c>#</c> comments, quoted strings (<c>Info.Name "Inca Totem"</c>) and multi-line blocks between
/// <c>---</c> markers (<c>Info.Shape</c>, <c>Info.Hoarding</c>). Values are kept raw; only the first
/// token (or the quoted string) is the value, the rest of the line is the designers' comment.
/// The generic <see cref="SettingsFile"/> does not handle quoted names or blocks, so objects use this reader.
/// </summary>
public sealed class ObjectSettingsFile
{
	private readonly Dictionary<string, string> values = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, IReadOnlyList<string>> blocks = new( StringComparer.OrdinalIgnoreCase );

	public ObjectSettingsFile( string text, string sourceName = "" )
	{
		SourceName = sourceName;
		var lines = text.Replace( "\r\n", "\n" ).Replace( '\r', '\n' ).Split( '\n' );
		for ( var index = 0; index < lines.Length; index++ )
		{
			var line = lines[index].Trim();
			if ( line.Length == 0 || line.StartsWith( '#' ) || line.StartsWith( "//", StringComparison.Ordinal ) )
				continue;
			var keyEnd = 0;
			while ( keyEnd < line.Length && !char.IsWhiteSpace( line[keyEnd] ) )
				keyEnd++;
			var key = line[..keyEnd];
			var rest = line[keyEnd..].Trim();
			if ( rest.Length == 0 || rest.StartsWith( '#' ) )
			{
				// A key on its own line followed by a "---" block.
				var next = index + 1;
				while ( next < lines.Length && lines[next].Trim().Length == 0 )
					next++;
				if ( next < lines.Length && lines[next].Trim() == "---" )
				{
					var rows = new List<string>();
					var end = next + 1;
					while ( end < lines.Length && lines[end].Trim() != "---" )
						rows.Add( lines[end++].Trim() );
					if ( end >= lines.Length )
						throw new InvalidDataException( $"{sourceName}: block '{key}' is not closed with ---." );
					blocks[key] = rows.AsReadOnly();
					index = end;
				}
				continue;
			}
			string value;
			if ( rest.StartsWith( '"' ) )
			{
				var close = rest.IndexOf( '"', 1 );
				value = close < 0 ? rest[1..] : rest[1..close];
			}
			else
			{
				var valueEnd = 0;
				while ( valueEnd < rest.Length && !char.IsWhiteSpace( rest[valueEnd] ) )
					valueEnd++;
				value = rest[..valueEnd];
			}
			values[key] = value;
		}
	}

	public string SourceName { get; }
	public IReadOnlyDictionary<string, string> Values => values;
	public IReadOnlyDictionary<string, IReadOnlyList<string>> Blocks => blocks;

	public static ObjectSettingsFile Load( string path ) => new( FileSystem.ReadAllText( path ), path );
}

/// <summary>
/// Layered object settings: later layers override earlier ones. The game ships a category default file
/// (<c>rides/Rides.sam</c>, <c>shops/Shops.sam</c>, ...) beside the archives and one description per
/// archive; coasters add a shared <c>coaster.sam</c>. The layering order (category, shared, object)
/// follows the files' own comments ("Always zero for shops", per-object overrides of the same keys);
/// it is inferred, not traced in the original executable.
/// </summary>
public sealed class ObjectSettings
{
	private readonly Dictionary<string, string> values = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, IReadOnlyList<string>> blocks = new( StringComparer.OrdinalIgnoreCase );

	public ObjectSettings( IEnumerable<ObjectSettingsFile> layers )
	{
		var sources = new List<string>();
		foreach ( var layer in layers )
		{
			foreach ( var (key, value) in layer.Values )
				values[key] = value;
			foreach ( var (key, rows) in layer.Blocks )
				blocks[key] = rows;
			sources.Add( layer.SourceName );
		}
		Sources = sources.AsReadOnly();
	}

	public IReadOnlyList<string> Sources { get; }
	public IReadOnlyDictionary<string, string> Values => values;
	public IReadOnlyDictionary<string, IReadOnlyList<string>> Blocks => blocks;

	public string? this[string key] => values.TryGetValue( key, out var value ) ? value : null;

	public bool Has( string key ) => values.ContainsKey( key );

	public int GetInt( string key, int fallback = 0 ) =>
		values.TryGetValue( key, out var value ) && int.TryParse( value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result ) ? result : fallback;

	public float GetFloat( string key, float fallback = 0 ) =>
		values.TryGetValue( key, out var value ) && float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result ) ? result : fallback;

	public bool GetBool( string key, bool fallback = false ) => GetInt( key, fallback ? 1 : 0 ) != 0;

	public IReadOnlyList<string>? GetBlock( string key ) => blocks.TryGetValue( key, out var rows ) ? rows : null;
}
