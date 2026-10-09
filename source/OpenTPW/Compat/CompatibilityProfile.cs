using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>Which known-issue fixes are active: none (Original), the presentation-only set (Recommended), or a per-fix choice (Custom).</summary>
public enum CompatibilityProfile
{
	Original,
	Recommended,
	Custom
}

/// <summary>Presentation fixes only change what is shown; simulation fixes change game state and are recorded in save metadata.</summary>
public enum CompatibilityFixKind
{
	Presentation,
	Simulation
}

/// <summary>A known-issue fix with a stable id (never renamed: ids are stored in user config and save metadata).</summary>
public sealed record CompatibilityFix( string Id, CompatibilityFixKind Kind, bool InRecommended, string Title, string Description );

/// <summary>The registry of fixes (docs/COMPATIBILITY.md). Every fix is a deliberate deviation from the original: [EXT:COMPAT-FIX &lt;id&gt;].</summary>
public static class CompatibilityFixes
{
	public const string SignFontSubstitution = "sign-font-substitution";
	public const string EnhancedGameOptions = "enhanced-game-options";

	public static readonly IReadOnlyList<CompatibilityFix> All = new CompatibilityFix[]
	{
		new( SignFontSubstitution, CompatibilityFixKind.Presentation, true, "Substitute missing sign fonts",
			"space/rides/megacost.sgn asks for 'EggIt Italic' (EGGII___.TTF), which fonts.wad does not ship; an in-memory data correction (keyed by the file's SHA-256) points it at the shipped EGGITAOE.TTF. Without it the line is not drawn." ),
		new( EnhancedGameOptions, CompatibilityFixKind.Simulation, false, "Enhanced game options",
			"With the Enhanced graphics preset, also raise NUMKIDS to 3 (MAX) and PARTICLEDENSITY to 2000. Changes the simulation; recorded in save metadata." ),
	};

	public static CompatibilityFix? Find( string id ) => All.FirstOrDefault( fix => string.Equals( fix.Id, id, StringComparison.Ordinal ) );
}

/// <summary>
/// The active profile and fix set. <see cref="ToMetadata"/> is the stable string the economy slice
/// stores in save metadata (format <c>opentpw-compat/1;profile=&lt;Profile&gt;;fixes=&lt;id,id&gt;;simulation=&lt;id,id&gt;</c>);
/// <see cref="ParseMetadata"/> reads it back and keeps unknown ids (from newer versions) verbatim.
/// </summary>
public sealed record CompatibilityFlags( CompatibilityProfile Profile, IReadOnlyList<string> EnabledFixes )
{
	public const string MetadataPrefix = "opentpw-compat/1";

	public static CompatibilityFlags Original { get; } = new( CompatibilityProfile.Original, Array.Empty<string>() );

	public static CompatibilityFlags Recommended { get; } = new( CompatibilityProfile.Recommended,
		CompatibilityFixes.All.Where( fix => fix.InRecommended ).Select( fix => fix.Id ).OrderBy( id => id, StringComparer.Ordinal ).ToArray() );

	/// <summary>Custom flags: the given ids, sorted and de-duplicated.</summary>
	public static CompatibilityFlags Custom( IEnumerable<string> ids ) =>
		new( CompatibilityProfile.Custom, ids.Distinct( StringComparer.Ordinal ).OrderBy( id => id, StringComparer.Ordinal ).ToArray() );

	public bool IsEnabled( string id ) => EnabledFixes.Contains( id, StringComparer.Ordinal );

	/// <summary>Enabled fixes that change the simulation (unknown ids count as simulation-affecting to be safe).</summary>
	public IReadOnlyList<string> SimulationFixes =>
		EnabledFixes.Where( id => CompatibilityFixes.Find( id )?.Kind != CompatibilityFixKind.Presentation ).ToArray();

	/// <summary>True when the simulation matches the original (no simulation-affecting fix enabled).</summary>
	public bool IsOriginalSimulation => SimulationFixes.Count == 0;

	public string ToMetadata() =>
		$"{MetadataPrefix};profile={Profile};fixes={string.Join( ",", EnabledFixes )};simulation={string.Join( ",", SimulationFixes )}";

	public static CompatibilityFlags ParseMetadata( string metadata )
	{
		ArgumentNullException.ThrowIfNull( metadata );
		var parts = metadata.Split( ';' );
		if ( parts.Length == 0 || parts[0] != MetadataPrefix )
			throw new FormatException( $"Compatibility metadata must start with '{MetadataPrefix}'." );
		var fields = parts.Skip( 1 ).Select( part => part.Split( '=', 2 ) ).Where( pair => pair.Length == 2 )
			.ToDictionary( pair => pair[0], pair => pair[1], StringComparer.Ordinal );
		if ( !fields.TryGetValue( "profile", out var profileText ) || !Enum.TryParse<CompatibilityProfile>( profileText, false, out var profile ) || !Enum.IsDefined( profile ) )
			throw new FormatException( "Compatibility metadata has no valid profile." );
		var ids = fields.TryGetValue( "fixes", out var list ) ? list.Split( ',', StringSplitOptions.RemoveEmptyEntries ) : Array.Empty<string>();
		return new CompatibilityFlags( profile, ids.Distinct( StringComparer.Ordinal ).OrderBy( id => id, StringComparer.Ordinal ).ToArray() );
	}

	public string Describe() => EnabledFixes.Count == 0 ? $"{Profile} (no fixes)" : $"{Profile}: {string.Join( ", ", EnabledFixes )}";
}

/// <summary>User compatibility choice, stored as <c>compatibility.json</c> in the user config directory.</summary>
public sealed record CompatibilitySettings
{
	public const string FileName = "compatibility.json";

	/// <summary>The default profile is Original: no fix is active unless the user opts in.</summary>
	public CompatibilityProfile Profile { get; init; } = CompatibilityProfile.Original;
	/// <summary>Per-fix choices for <see cref="CompatibilityProfile.Custom"/> (fix id -> enabled).</summary>
	public Dictionary<string, bool> Fixes { get; init; } = new();

	public static CompatibilitySettings Default { get; } = new();

	public CompatibilityFlags Resolve( ICollection<string> diagnostics )
	{
		switch ( Profile )
		{
			case CompatibilityProfile.Original:
				return CompatibilityFlags.Original;
			case CompatibilityProfile.Recommended:
				return CompatibilityFlags.Recommended;
			case CompatibilityProfile.Custom:
				foreach ( var id in Fixes.Keys.Where( id => CompatibilityFixes.Find( id ) == null ) )
					diagnostics.Add( $"Unknown compatibility fix '{id}' ignored." );
				return CompatibilityFlags.Custom( Fixes.Where( pair => pair.Value && CompatibilityFixes.Find( pair.Key ) != null ).Select( pair => pair.Key ) );
			default:
				diagnostics.Add( $"Unknown compatibility profile {(int)Profile}; using Original." );
				return CompatibilityFlags.Original;
		}
	}

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	public string ToJson() => JsonSerializer.Serialize( this, JsonOptions ).Replace( "\r\n", "\n" );

	public static CompatibilitySettings FromJson( string json, ICollection<string> diagnostics )
	{
		try
		{
			return JsonSerializer.Deserialize<CompatibilitySettings>( json, JsonOptions ) ?? Default;
		}
		catch ( JsonException exception )
		{
			diagnostics.Add( $"Compatibility settings file is invalid ({exception.Message}); using Original." );
			return Default;
		}
	}

	public static string GetDefaultPath() => Path.Combine( Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )!, FileName );

	public static CompatibilitySettings Load( string path, ICollection<string> diagnostics )
	{
		if ( !File.Exists( path ) )
			return Default;
		try
		{
			return FromJson( File.ReadAllText( path ), diagnostics );
		}
		catch ( IOException exception )
		{
			diagnostics.Add( $"Compatibility settings could not be read ({exception.Message}); using Original." );
			return Default;
		}
	}

	public void Save( string path )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( Path.GetFullPath( path ) )! );
		var temporary = path + ".tmp";
		File.WriteAllText( temporary, ToJson() );
		File.Move( temporary, path, overwrite: true );
	}
}
