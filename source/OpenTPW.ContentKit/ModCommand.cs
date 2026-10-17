using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenTPW.ContentKit;

/// <summary>One object of a mod's <c>objects.json</c>, as <c>mod --object</c> describes it.</summary>
/// <param name="Source">An object folder built by <c>object</c> that is copied to <paramref name="Archive"/> in the mod folder, or null when the archive is already there.</param>
public sealed record ModObjectSpec( string Archive, string Theme, string Category, int InfoId, IReadOnlyDictionary<string, string> Names, string? Source = null );

/// <summary>The manifest fields <c>mod</c> can write (docs/MODS.md, "Manifest").</summary>
public sealed record ModManifestSpec( string Id, string Name, string Version = "1.0.0", string? Author = null, string? Description = null );

/// <summary>
/// Lays out a data-only mod folder (docs/MODS.md): <c>opentpw-mod.json</c>, <c>objects.json</c> and the object archives. The documents are validated here with the
/// rules the game's strict loaders apply (schema, id and version shapes, the Info.Id range, themes, categories, archive paths), so a mistake is reported
/// when the folder is made instead of when the game refuses the mod; the game's own loaders stay the authority.
/// </summary>
public static class ModBuilder
{
	public const string ManifestFileName = "opentpw-mod.json";
	public const string ObjectsFileName = "objects.json";
	private static readonly string[] Themes = { "jungle", "hallow", "space", "fantasy" };
	private static readonly string[] Categories = { "rides", "shops", "sideshow", "features", "upgrades" };
	private static readonly string[] ManifestFields = { "schema", "id", "name", "version", "category", "openTpw", "edition", "author", "description", "dependencies", "after", "inputs", "content" };
	private static readonly string[] ContentFields = { "overrides", "objects", "strings" };
	private static readonly Regex IdPattern = new( @"\A(opentpw|community)\.[a-z0-9]+(?:[-_.][a-z0-9]+)*\z", RegexOptions.CultureInvariant );
	private static readonly Regex VersionPattern = new( @"\A\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?\z", RegexOptions.CultureInvariant );
	private static readonly JsonWriterOptions WriterOptions = new() { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	/// <summary>The manifest of a new mod: <c>content.objects</c> is always <c>objects.json</c>.</summary>
	public static string RenderManifest( ModManifestSpec spec )
	{
		CheckManifest( spec.Id, spec.Name, spec.Version );
		using var stream = new MemoryStream();
		using ( var writer = new Utf8JsonWriter( stream, WriterOptions ) )
		{
			writer.WriteStartObject();
			writer.WriteNumber( "schema", 1 );
			writer.WriteString( "id", spec.Id );
			writer.WriteString( "name", spec.Name );
			writer.WriteString( "version", spec.Version );
			writer.WriteString( "category", "community" );
			if ( spec.Author != null )
				writer.WriteString( "author", spec.Author );
			if ( spec.Description != null )
			{
				if ( spec.Description.Length > 1000 )
					throw new InvalidDataException( "The description is at most 1000 characters." );
				writer.WriteString( "description", spec.Description );
			}
			writer.WriteStartObject( "content" );
			writer.WriteString( "objects", ObjectsFileName );
			writer.WriteEndObject();
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString( stream.ToArray() ) + "\n";
	}

	/// <summary>Checks a manifest the caller wrote and returns it with <c>content.objects</c> set to <c>objects.json</c> when it names no objects file.</summary>
	public static string CompleteManifest( string json )
	{
		using var document = JsonDocument.Parse( json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow } );
		var root = document.RootElement;
		if ( root.ValueKind != JsonValueKind.Object )
			throw new InvalidDataException( $"{ManifestFileName} must be a JSON object." );
		foreach ( var property in root.EnumerateObject() )
		{
			if ( !ManifestFields.Contains( property.Name ) )
				throw new InvalidDataException( $"{ManifestFileName}: unknown field '{property.Name}'." );
		}
		if ( !root.TryGetProperty( "schema", out var schema ) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1 )
			throw new InvalidDataException( $"{ManifestFileName}: 'schema' must be 1." );
		CheckManifest( String( root, "id" ), String( root, "name" ), String( root, "version" ) );
		if ( String( root, "category" ) != "community" )
			throw new InvalidDataException( $"{ManifestFileName}: a community. id needs the category 'community'." );
		if ( root.TryGetProperty( "content", out var content ) )
		{
			foreach ( var property in content.EnumerateObject() )
			{
				if ( !ContentFields.Contains( property.Name ) )
					throw new InvalidDataException( $"{ManifestFileName}: unknown content field '{property.Name}'." );
			}
			if ( content.TryGetProperty( "objects", out var objects ) && objects.GetString() != ObjectsFileName )
				throw new InvalidDataException( $"{ManifestFileName}: 'content.objects' must be \"{ObjectsFileName}\" for this command (it writes that file)." );
		}
		using var stream = new MemoryStream();
		using ( var writer = new Utf8JsonWriter( stream, WriterOptions ) )
		{
			writer.WriteStartObject();
			foreach ( var property in root.EnumerateObject() )
			{
				if ( property.Name != "content" )
					property.WriteTo( writer );
			}
			writer.WriteStartObject( "content" );
			if ( root.TryGetProperty( "content", out var existing ) )
			{
				foreach ( var property in existing.EnumerateObject().Where( property => property.Name != "objects" ) )
					property.WriteTo( writer );
			}
			writer.WriteString( "objects", ObjectsFileName );
			writer.WriteEndObject();
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString( stream.ToArray() ) + "\n";
	}

	private static string String( JsonElement root, string field ) =>
		root.TryGetProperty( field, out var value ) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException( $"{ManifestFileName}: '{field}' is required." );

	private static void CheckManifest( string id, string name, string version )
	{
		if ( id.Length > 64 || !IdPattern.IsMatch( id ) )
			throw new InvalidDataException( $"The mod id '{id}' is opentpw. or community. followed by lowercase letters, digits, '.', '-' and '_', at most 64 characters." );
		if ( !id.StartsWith( "community.", StringComparison.Ordinal ) )
			throw new InvalidDataException( "Only community. ids can be made here; opentpw. ids belong to the official extensions." );
		if ( name.Length is 0 or > 80 )
			throw new InvalidDataException( "The mod name is 1 to 80 characters." );
		if ( !VersionPattern.IsMatch( version ) )
			throw new InvalidDataException( $"The version '{version}' must be MAJOR.MINOR.PATCH, optionally with -prerelease." );
	}

	/// <summary>The <c>objects.json</c> of a mod from its objects and the Info.Id range it reserves.</summary>
	public static string RenderObjects( int first, int last, IReadOnlyList<ModObjectSpec> objects )
	{
		CheckRange( first, last );
		if ( objects.Count > 256 )
			throw new InvalidDataException( "A mod lists at most 256 objects." );
		var ids = new HashSet<int>();
		var archives = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var item in objects )
		{
			CheckArchivePath( item.Archive );
			if ( !Themes.Contains( item.Theme ) )
				throw new InvalidDataException( $"The theme '{item.Theme}' must be one of {string.Join( ", ", Themes )}." );
			if ( !Categories.Contains( item.Category ) )
				throw new InvalidDataException( $"The category '{item.Category}' must be one of {string.Join( ", ", Categories )}." );
			if ( item.InfoId < first || item.InfoId > last )
				throw new InvalidDataException( $"The object '{item.Archive}' has the id {item.InfoId}, outside the range {first} to {last}." );
			if ( !ids.Add( item.InfoId ) )
				throw new InvalidDataException( $"The id {item.InfoId} is used by more than one object." );
			if ( !archives.Add( $"{item.Theme}/{item.Category}/{Path.GetFileName( item.Archive )}" ) )
				throw new InvalidDataException( $"The theme {item.Theme} lists two {item.Category} archives named '{Path.GetFileName( item.Archive )}'." );
			if ( item.Names.Count > 16 )
				throw new InvalidDataException( $"The object '{item.Archive}' has more than 16 names." );
			foreach ( var (language, text) in item.Names )
			{
				if ( language.Length is 0 or > 32 || !language.All( char.IsAsciiLetter ) )
					throw new InvalidDataException( $"The language '{language}' must be a language name such as English." );
				if ( string.IsNullOrWhiteSpace( text ) || text.Length > 80 || text.Any( char.IsControl ) )
					throw new InvalidDataException( $"The {language} name of '{item.Archive}' must be 1 to 80 characters without control characters." );
			}
		}
		using var stream = new MemoryStream();
		using ( var writer = new Utf8JsonWriter( stream, WriterOptions ) )
		{
			writer.WriteStartObject();
			writer.WriteNumber( "schema", 1 );
			writer.WriteStartObject( "idRange" );
			writer.WriteNumber( "first", first );
			writer.WriteNumber( "last", last );
			writer.WriteEndObject();
			writer.WriteStartArray( "objects" );
			foreach ( var item in objects )
			{
				writer.WriteStartObject();
				writer.WriteString( "archive", item.Archive );
				writer.WriteString( "theme", item.Theme );
				writer.WriteString( "category", item.Category );
				writer.WriteNumber( "infoId", item.InfoId );
				writer.WriteStartObject( "names" );
				foreach ( var (language, text) in item.Names.OrderBy( pair => pair.Key, StringComparer.Ordinal ) )
					writer.WriteString( language, text );
				writer.WriteEndObject();
				writer.WriteEndObject();
			}
			writer.WriteEndArray();
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString( stream.ToArray() ) + "\n";
	}

	/// <summary>Checks an <c>objects.json</c> the caller wrote (the same rules as <see cref="RenderObjects"/>) and returns it unchanged.</summary>
	public static string CheckObjects( string json )
	{
		using var document = JsonDocument.Parse( json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow } );
		var root = document.RootElement;
		foreach ( var property in root.EnumerateObject() )
		{
			if ( property.Name is not ("schema" or "idRange" or "objects") )
				throw new InvalidDataException( $"{ObjectsFileName}: unknown field '{property.Name}'." );
		}
		if ( !root.TryGetProperty( "schema", out var schema ) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1 )
			throw new InvalidDataException( $"{ObjectsFileName}: 'schema' must be 1." );
		var range = root.TryGetProperty( "idRange", out var r ) ? r : throw new InvalidDataException( $"{ObjectsFileName}: 'idRange' is required." );
		var first = range.GetProperty( "first" ).GetInt32();
		var last = range.GetProperty( "last" ).GetInt32();
		var objects = new List<ModObjectSpec>();
		foreach ( var item in root.GetProperty( "objects" ).EnumerateArray() )
		{
			foreach ( var property in item.EnumerateObject() )
			{
				if ( property.Name is not ("archive" or "input" or "theme" or "category" or "infoId" or "names") )
					throw new InvalidDataException( $"{ObjectsFileName}: unknown object field '{property.Name}'." );
			}
			if ( item.TryGetProperty( "input", out _ ) )
				throw new InvalidDataException( $"{ObjectsFileName}: objects read from an input are not made by this kit; write that mod by hand (docs/MODS.md)." );
			var names = item.TryGetProperty( "names", out var n ) ? n.EnumerateObject().ToDictionary( p => p.Name, p => p.Value.GetString() ?? "" ) : new Dictionary<string, string>();
			objects.Add( new ModObjectSpec( item.GetProperty( "archive" ).GetString()!, item.GetProperty( "theme" ).GetString()!, item.GetProperty( "category" ).GetString()!, item.GetProperty( "infoId" ).GetInt32(), names ) );
		}
		RenderObjects( first, last, objects );
		return json.EndsWith( '\n' ) ? json : json + "\n";
	}

	/// <summary>The same rule as the game's <c>CrossEditionObjects.RangeProblem</c>: 5000 to 65535, at most 4096 ids.</summary>
	private static void CheckRange( int first, int last )
	{
		if ( first < 5000 || last > 65535 || last < first || last - first + 1 > 4096 )
			throw new InvalidDataException( $"The id range {first} to {last} must lie between 5000 and 65535 and hold at most 4096 ids." );
	}

	private static void CheckArchivePath( string archive )
	{
		if ( archive.Length == 0 || archive.StartsWith( '/' ) || archive.Contains( '\\' ) || archive.Contains( ':' ) || archive.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( $"The archive path '{archive}' must be relative, use '/', and omit the .wad extension." );
		foreach ( var segment in archive.Split( '/' ) )
		{
			if ( segment.Length == 0 || segment is "." or ".." || segment.EndsWith( '.' ) || segment.EndsWith( ' ' ) )
				throw new InvalidDataException( $"The archive path '{archive}' has an empty, '.', '..' or Windows-unsafe segment." );
		}
	}

	public static CommandResult Build( string outDirectory, string manifestJson, string objectsJson, IReadOnlyList<ModObjectSpec> sources, bool force )
	{
		var files = new List<(string Path, byte[] Bytes)> { (ManifestFileName, Encoding.UTF8.GetBytes( manifestJson )), (ObjectsFileName, Encoding.UTF8.GetBytes( objectsJson )) };
		foreach ( var item in sources.Where( item => item.Source != null ) )
		{
			var root = Path.GetFullPath( item.Source! );
			if ( !Directory.Exists( root ) )
				throw new DirectoryNotFoundException( $"The object folder '{item.Source}' does not exist." );
			foreach ( var file in Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories ).OrderBy( file => file, StringComparer.Ordinal ) )
				files.Add( ($"{item.Archive}/{Path.GetRelativePath( root, file ).Replace( '\\', '/' )}", File.ReadAllBytes( file )) );
		}
		var written = new List<string>();
		Output.Write( outDirectory, files, force, written );
		return new CommandResult( outDirectory, written, Array.Empty<string>() );
	}
}

public static class ModCommand
{
	public static CommandResult Run( CommandOptions options )
	{
		var outDirectory = options.Require( "out" );
		var force = options.Has( "force" );

		string manifest;
		if ( options.Optional( "manifest" ) is { } manifestPath )
			manifest = ModBuilder.CompleteManifest( File.ReadAllText( manifestPath ) );
		else
			manifest = ModBuilder.RenderManifest( new ModManifestSpec( options.Require( "id" ), options.Require( "name" ), options.Optional( "version" ) ?? "1.0.0", options.Optional( "author" ), options.Optional( "description" ) ) );

		var specs = options.Many( "object" ).Select( ParseObject ).ToList();
		string objects;
		if ( options.Optional( "objects" ) is { } objectsPath )
		{
			if ( specs.Count > 0 || options.Has( "id-first" ) || options.Has( "id-last" ) )
				throw new UsageException( "--objects replaces --object, --id-first and --id-last." );
			objects = ModBuilder.CheckObjects( File.ReadAllText( objectsPath ) );
		}
		else
		{
			if ( specs.Count == 0 )
				throw new UsageException( "Give --objects <file> or at least one --object <spec>." );
			var first = int.Parse( options.Require( "id-first" ), System.Globalization.CultureInfo.InvariantCulture );
			var last = int.Parse( options.Require( "id-last" ), System.Globalization.CultureInfo.InvariantCulture );
			objects = ModBuilder.RenderObjects( first, last, specs );
		}
		options.RejectUnknown();
		return ModBuilder.Build( outDirectory, manifest, objects, specs, force );
	}

	/// <summary>Parses <c>archive=..,theme=..,category=..,id=..,name=..[,name.German=..][,src=..]</c>.</summary>
	public static ModObjectSpec ParseObject( string text )
	{
		var values = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( var part in text.Split( ',' ) )
		{
			var split = part.IndexOf( '=' );
			if ( split <= 0 )
				throw new UsageException( $"The object spec part '{part}' is not key=value." );
			if ( !values.TryAdd( part[..split], part[(split + 1)..] ) )
				throw new UsageException( $"The object spec names '{part[..split]}' twice." );
		}
		string Get( string key ) => values.TryGetValue( key, out var value ) ? value : throw new UsageException( $"The object spec has no {key}=." );
		var names = new SortedDictionary<string, string>( StringComparer.Ordinal );
		if ( values.TryGetValue( "name", out var english ) )
			names["English"] = english;
		foreach ( var (key, value) in values.Where( pair => pair.Key.StartsWith( "name.", StringComparison.Ordinal ) ) )
			names[key[5..]] = value;
		var known = new[] { "archive", "theme", "category", "id", "name", "src" };
		var unknown = values.Keys.FirstOrDefault( key => !known.Contains( key ) && !key.StartsWith( "name.", StringComparison.Ordinal ) );
		if ( unknown != null )
			throw new UsageException( $"The object spec key '{unknown}' is unknown." );
		return new ModObjectSpec( Get( "archive" ), Get( "theme" ), Get( "category" ),
			int.TryParse( Get( "id" ), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id ) ? id : throw new UsageException( "id= must be a whole number." ),
			names, values.GetValueOrDefault( "src" ) );
	}
}
