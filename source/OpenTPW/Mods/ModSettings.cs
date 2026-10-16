using OpenTPW.Online.Packages;

namespace OpenTPW.Mods;

/// <summary>
/// The player's mod choices in <c>&lt;config&gt;/mods.json</c> (docs/MODS.md): which mods and extensions are enabled, the
/// order of community mods and the folders supplied as mod inputs. A missing file means the defaults; an unreadable or
/// invalid file adds a diagnostic and falls back to the defaults, like <see cref="LanguageSettings"/>. Invalid entries
/// in an otherwise valid file are dropped one by one, each with a diagnostic. Changes apply at the next start.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public sealed record ModSettings
{
	public const string FileName = "mods.json";
	public const int MaximumBytes = 256 * 1024;
	public const int MaximumEntries = 512;
	public const int MaximumInputPathLength = 4096;

	public static ModSettings Default { get; } = new();

	public IReadOnlyDictionary<string, bool> Enabled { get; init; } = new Dictionary<string, bool>();
	public IReadOnlyList<string> Order { get; init; } = Array.Empty<string>();
	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Inputs { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();

	public static string GetDefaultPath() => Path.Combine( Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )!, FileName );

	/// <summary>Whether <paramref name="id"/> is enabled: the saved choice, else <paramref name="defaultEnabled"/>.</summary>
	public bool IsEnabled( string id, bool defaultEnabled ) => Enabled.TryGetValue( id, out var enabled ) ? enabled : defaultEnabled;

	public static ModSettings Load( string path, ICollection<string> diagnostics )
	{
		try
		{
			var info = new FileInfo( path );
			if ( !info.Exists )
				return Default;
			if ( info.Length > MaximumBytes )
			{
				diagnostics.Add( $"Mod settings ({path}) are larger than {MaximumBytes} bytes; using the default mods and extensions." );
				return Default;
			}
			return Parse( File.ReadAllBytes( path ), diagnostics );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			diagnostics.Add( $"Mod settings could not be read ({exception.Message}); using the default mods and extensions." );
			return Default;
		}
	}

	public static ModSettings Parse( ReadOnlySpan<byte> utf8, ICollection<string> diagnostics )
	{
		Document document;
		try
		{
			document = StrictJson.Deserialize<Document>( utf8, FileName );
		}
		catch ( InvalidDataException exception )
		{
			diagnostics.Add( $"Mod settings could not be read ({exception.Message}); using the default mods and extensions." );
			return Default;
		}

		var enabled = new Dictionary<string, bool>( StringComparer.Ordinal );
		foreach ( var (id, value) in (document.Enabled ?? new()).Take( MaximumEntries ) )
		{
			if ( ModManifest.IsValidId( id ) )
				enabled[id] = value;
			else
				diagnostics.Add( $"Mod settings: '{id}' is not a mod id; its enabled choice was ignored." );
		}
		var order = new List<string>();
		foreach ( var id in (document.Order ?? new()).Take( MaximumEntries ) )
		{
			if ( ModManifest.IsValidId( id ) && !order.Contains( id! ) )
				order.Add( id! );
			else
				diagnostics.Add( $"Mod settings: order entry '{id}' is not a mod id or is listed twice; it was ignored." );
		}
		var inputs = new Dictionary<string, IReadOnlyDictionary<string, string>>( StringComparer.Ordinal );
		foreach ( var (id, values) in (document.Inputs ?? new()).Take( MaximumEntries ) )
		{
			if ( !ModManifest.IsValidId( id ) || values == null )
			{
				diagnostics.Add( $"Mod settings: '{id}' is not a mod id; its inputs were ignored." );
				continue;
			}
			var paths = new Dictionary<string, string>( StringComparer.Ordinal );
			foreach ( var (input, path) in values.Take( ModManifest.MaximumInputs ) )
			{
				if ( string.IsNullOrWhiteSpace( path ) || path.Length > MaximumInputPathLength || path.Any( char.IsControl ) || input.Length > ModManifest.MaximumIdLength )
					diagnostics.Add( $"Mod settings: input '{input}' of '{id}' has an invalid folder; it was ignored." );
				else
					paths[input] = path;
			}
			inputs[id] = paths;
		}
		if ( (document.Enabled?.Count ?? 0) > MaximumEntries || (document.Order?.Count ?? 0) > MaximumEntries || (document.Inputs?.Count ?? 0) > MaximumEntries )
			diagnostics.Add( $"Mod settings: only the first {MaximumEntries} entries of each list were read." );
		return new ModSettings { Enabled = enabled, Order = order, Inputs = inputs };
	}

	public byte[] Serialize() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( new Document
	{
		Enabled = Enabled.OrderBy( pair => pair.Key, StringComparer.Ordinal ).ToDictionary( pair => pair.Key, pair => pair.Value ),
		Order = Order.ToList<string?>(),
		Inputs = Inputs.OrderBy( pair => pair.Key, StringComparer.Ordinal )
			.ToDictionary( pair => pair.Key, pair => (Dictionary<string, string>?)pair.Value.OrderBy( input => input.Key, StringComparer.Ordinal ).ToDictionary( input => input.Key, input => input.Value ) ),
	}, WriteOptions );

	// The strict reading rules, written indented so the file stays readable and editable.
	private static readonly System.Text.Json.JsonSerializerOptions WriteOptions = new( StrictJson.Options ) { WriteIndented = true, NewLine = "\n" };

	/// <summary>Writes the settings through a temporary file, so a failed write keeps the previous choices.</summary>
	public void Save( string path )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( Path.GetFullPath( path ) )! );
		var temporary = path + ".tmp";
		File.WriteAllBytes( temporary, Serialize() );
		File.Move( temporary, path, overwrite: true );
	}

	public ModSettings WithEnabled( string id, bool enabled )
	{
		var copy = new Dictionary<string, bool>( Enabled, StringComparer.Ordinal ) { [id] = enabled };
		return this with { Enabled = copy };
	}

	private sealed class Document
	{
		public Dictionary<string, bool>? Enabled { get; init; }
		public List<string?>? Order { get; init; }
		public Dictionary<string, Dictionary<string, string>?>? Inputs { get; init; }
	}
}
