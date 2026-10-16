using System.Text.RegularExpressions;
using OpenTPW.Online.Packages;

namespace OpenTPW.Mods;

/// <summary>Who ships a mod: compiled-in or bundled OpenTPW content (<c>opentpw.</c> ids) or a community mod (<c>community.</c> ids).</summary>
public enum ModCategory { Official, Community }

/// <summary>A dependency on another mod or extension, with the versions it accepts.</summary>
public sealed record ModDependency( string Id, ModVersionRange Version );

/// <summary>An input the player supplies, such as the folder of another original game (phase 6; docs/MODS.md).</summary>
public sealed record ModInput( string Id, string Kind, string? Edition );

/// <summary>The mod-relative content paths. Nothing consumes them yet: content roots arrive in a later phase (docs/MODS.md).</summary>
public sealed record ModContent( string? Overrides, string? Objects, string? Strings );

/// <summary>
/// A parsed and validated <c>opentpw-mod.json</c> (docs/MODS.md). Parsing is strict in the <see cref="StrictJson"/> style:
/// unknown or duplicate fields, comments and trailing commas are refused, sizes are bounded, and no content path may
/// leave the mod's folder.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public sealed record ModManifest
{
	public const string FileName = "opentpw-mod.json";
	public const int SchemaVersion = 1;
	public const int MaximumBytes = 64 * 1024;
	public const int MaximumIdLength = 64;
	public const int MaximumNameLength = 80;
	public const int MaximumDescriptionLength = 1000;
	public const int MaximumPathLength = 240;
	public const int MaximumDependencies = 32;
	public const int MaximumInputs = 16;

	public const string OfficialPrefix = "opentpw.";
	public const string CommunityPrefix = "community.";

	private static readonly Regex IdPattern = new( @"^(opentpw|community)\.[a-z0-9]+(?:[-_.][a-z0-9]+)*$", RegexOptions.CultureInvariant );
	private static readonly Regex InputIdPattern = new( @"^[a-z0-9]+(?:[-_][a-z0-9]+)*$", RegexOptions.CultureInvariant );

	public required string Id { get; init; }
	public required string Name { get; init; }
	public required ModVersion Version { get; init; }
	public required ModCategory Category { get; init; }
	public ModVersionRange? OpenTpw { get; init; }
	/// <summary>The edition the mod is for (<see cref="GameEdition"/>), or null for any.</summary>
	public GameEdition? Edition { get; init; }
	public string? Author { get; init; }
	public string? Description { get; init; }
	public IReadOnlyList<ModDependency> Dependencies { get; init; } = Array.Empty<ModDependency>();
	/// <summary>Mods this one is ordered after when both are enabled; it also lets this mod's overrides win over theirs.</summary>
	public IReadOnlyList<string> After { get; init; } = Array.Empty<string>();
	public IReadOnlyList<ModInput> Inputs { get; init; } = Array.Empty<ModInput>();
	public ModContent Content { get; init; } = new( null, null, null );

	public static bool IsValidId( string? id ) => id != null && id.Length <= MaximumIdLength && IdPattern.IsMatch( id );

	public static ModCategory CategoryOf( string id ) => id.StartsWith( OfficialPrefix, StringComparison.Ordinal ) ? ModCategory.Official : ModCategory.Community;

	/// <summary>The edition token of the manifest's <c>edition</c> field.</summary>
	public static string EditionToken( GameEdition edition ) => edition == GameEdition.ThemeParkInc ? "theme-park-inc" : "theme-park-world";

	public static ModManifest Load( string path )
	{
		var info = new FileInfo( path );
		if ( !info.Exists )
			throw new FileNotFoundException( $"{FileName} is missing.", path );
		if ( info.Length > MaximumBytes )
			throw new InvalidDataException( $"{FileName} is larger than {MaximumBytes} bytes." );
		return Parse( File.ReadAllBytes( path ) );
	}

	/// <summary>Parses and validates manifest bytes; throws <see cref="InvalidDataException"/> with the reason.</summary>
	public static ModManifest Parse( ReadOnlySpan<byte> utf8 )
	{
		if ( utf8.Length > MaximumBytes )
			throw new InvalidDataException( $"{FileName} is larger than {MaximumBytes} bytes." );
		var document = StrictJson.Deserialize<Document>( utf8, FileName );
		if ( document.Schema != SchemaVersion )
			throw Invalid( $"'schema' must be {SchemaVersion}" );
		var id = Required( document.Id, "id" );
		if ( !IsValidId( id ) )
			throw Invalid( $"'id' '{id}' must start with '{OfficialPrefix}' or '{CommunityPrefix}' and use lowercase letters, digits, '.', '-' and '_'" );
		var category = Required( document.Category, "category" ) switch
		{
			"official" => ModCategory.Official,
			"community" => ModCategory.Community,
			var other => throw Invalid( $"'category' must be 'official' or 'community', not '{other}'" ),
		};
		if ( category != CategoryOf( id ) )
			throw Invalid( $"'category' {document.Category} does not match the id '{id}'" );
		var versionText = Required( document.Version, "version" );
		if ( !ModVersion.TryParse( versionText, out var version ) )
			throw Invalid( $"'version' '{versionText}' is not a version such as 1.0.0" );
		ModVersionRange? openTpw = null;
		if ( document.OpenTpw != null && !ModVersionRange.TryParse( document.OpenTpw, out openTpw ) )
			throw Invalid( $"'openTpw' '{document.OpenTpw}' is not a version range such as >=0.9.0 <0.10.0" );

		var dependencies = new List<ModDependency>();
		foreach ( var dependency in Bounded( document.Dependencies, MaximumDependencies, "dependencies" ) )
		{
			var dependencyId = Required( dependency?.Id, "dependencies[].id" );
			if ( !IsValidId( dependencyId ) || dependencyId == id )
				throw Invalid( $"dependency '{dependencyId}' is not another mod's id" );
			if ( dependencies.Any( existing => existing.Id == dependencyId ) )
				throw Invalid( $"dependency '{dependencyId}' is listed more than once" );
			var rangeText = dependency!.Version ?? "*";
			if ( !ModVersionRange.TryParse( rangeText, out var range ) )
				throw Invalid( $"dependency '{dependencyId}' has the invalid version range '{rangeText}'" );
			dependencies.Add( new ModDependency( dependencyId, range ) );
		}

		var after = new List<string>();
		foreach ( var other in Bounded( document.After, MaximumDependencies, "after" ) )
		{
			if ( !IsValidId( other ) || other == id || after.Contains( other! ) )
				throw Invalid( $"'after' entry '{other}' is not another mod's id, or is listed twice" );
			after.Add( other! );
		}

		var inputs = new List<ModInput>();
		foreach ( var input in Bounded( document.Inputs, MaximumInputs, "inputs" ) )
		{
			var inputId = Required( input?.Id, "inputs[].id" );
			if ( inputId.Length > MaximumIdLength || !InputIdPattern.IsMatch( inputId ) || inputs.Any( existing => existing.Id == inputId ) )
				throw Invalid( $"input id '{inputId}' must be unique and use lowercase letters, digits, '-' and '_'" );
			var kind = Required( input!.Kind, "inputs[].kind" );
			if ( kind != "original-data" )
				throw Invalid( $"input '{inputId}' has the unknown kind '{kind}' (only 'original-data')" );
			if ( input.Edition != null )
				ParseEdition( input.Edition, $"input '{inputId}' edition" );
			inputs.Add( new ModInput( inputId, kind, input.Edition ) );
		}

		var content = document.Content ?? new ContentDocument();
		return new ModManifest
		{
			Id = id,
			Name = Text( Required( document.Name, "name" ), MaximumNameLength, "name", required: true, allowNewline: false )!,
			Version = version,
			Category = category,
			OpenTpw = openTpw,
			Edition = document.Edition == null ? null : ParseEdition( document.Edition, "'edition'" ),
			Author = Text( document.Author, MaximumNameLength, "author", required: false, allowNewline: false ),
			Description = Text( document.Description, MaximumDescriptionLength, "description", required: false, allowNewline: true ),
			Dependencies = dependencies,
			After = after,
			Inputs = inputs,
			Content = new ModContent( ContentPath( content.Overrides, "content.overrides" ), ContentPath( content.Objects, "content.objects" ), ContentPath( content.Strings, "content.strings" ) ),
		};
	}

	/// <summary>
	/// A mod-relative path in '/' form without a trailing '/', or an exception when it is empty, too long, contains
	/// control characters or ':' (drives, alternate streams), or leaves the mod's folder.
	/// </summary>
	public static string NormalizeRelativePath( string path, string what )
	{
		if ( path.Length == 0 || path.Length > MaximumPathLength )
			throw Invalid( $"{what} must be 1 to {MaximumPathLength} characters" );
		if ( path.Any( c => char.IsControl( c ) || c == ':' ) )
			throw Invalid( $"{what} contains a control character or ':'" );
		if ( BonusContent.Escapes( path ) || path.StartsWith( '\\' ) )
			throw Invalid( $"{what} '{path}' leaves the mod's folder" );
		var normalized = path.Replace( '\\', '/' ).TrimEnd( '/' );
		if ( normalized.Length == 0 || normalized.Split( '/' ).Any( part => part.Length == 0 || part == "." ) )
			throw Invalid( $"{what} '{path}' is not a plain relative path" );
		return normalized;
	}

	private static string? ContentPath( string? path, string what ) => path == null ? null : NormalizeRelativePath( path, what );

	private static GameEdition ParseEdition( string text, string what ) => text switch
	{
		"theme-park-world" => GameEdition.ThemeParkWorld,
		"theme-park-inc" => GameEdition.ThemeParkInc,
		_ => throw Invalid( $"{what} must be 'theme-park-world' or 'theme-park-inc', not '{text}'" ),
	};

	private static string Required( string? value, string field ) =>
		value ?? throw Invalid( $"'{field}' is required" );

	private static string? Text( string? value, int maximum, string field, bool required, bool allowNewline )
	{
		if ( value == null )
			return null;
		if ( (required && string.IsNullOrWhiteSpace( value )) || value.Length > maximum || value.Any( c => char.IsControl( c ) && !(allowNewline && c == '\n') ) )
			throw Invalid( $"'{field}' must be at most {maximum} characters without control characters" );
		return value;
	}

	private static IEnumerable<T> Bounded<T>( IReadOnlyList<T>? items, int maximum, string field )
	{
		if ( items == null )
			return Array.Empty<T>();
		if ( items.Count > maximum )
			throw Invalid( $"'{field}' has more than {maximum} entries" );
		return items;
	}

	private static InvalidDataException Invalid( string reason ) => new( $"{FileName}: {reason}." );

	// The JSON shape; StrictJson refuses unknown and duplicate members.
	private sealed class Document
	{
		public int Schema { get; init; }
		public string? Id { get; init; }
		public string? Name { get; init; }
		public string? Version { get; init; }
		public string? Category { get; init; }
		public string? OpenTpw { get; init; }
		public string? Edition { get; init; }
		public string? Author { get; init; }
		public string? Description { get; init; }
		public List<DependencyDocument?>? Dependencies { get; init; }
		public List<string?>? After { get; init; }
		public List<InputDocument?>? Inputs { get; init; }
		public ContentDocument? Content { get; init; }
	}

	private sealed class DependencyDocument
	{
		public string? Id { get; init; }
		public string? Version { get; init; }
	}

	private sealed class InputDocument
	{
		public string? Id { get; init; }
		public string? Kind { get; init; }
		public string? Edition { get; init; }
	}

	private sealed class ContentDocument
	{
		public string? Overrides { get; init; }
		public string? Objects { get; init; }
		public string? Strings { get; init; }
	}
}
