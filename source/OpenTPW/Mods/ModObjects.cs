using OpenTPW.Online.Packages;

namespace OpenTPW.Mods;

/// <summary>
/// One object a mod adds to a Theme Park World theme (<c>objects.json</c>, docs/MODS.md): an archive (the mod's own, or one
/// of an input folder such as another original game's install), where it goes, the Info.Id it gets and its names.
/// </summary>
/// <param name="Archive">Archive path without the <c>.wad</c> extension, '/' form: below the mod's folder, or below the input's data folder when <paramref name="Input"/> is set.</param>
/// <param name="Input">The id of one of the mod's inputs the archive is read from, or null for the mod's own folder.</param>
/// <param name="Theme">The theme directory (<c>jungle</c>, <c>hallow</c>, <c>space</c> or <c>fantasy</c>).</param>
/// <param name="Category">The category directory the object is listed under.</param>
/// <param name="InfoId">The Info.Id the object has in the catalog, from the mod's declared range; the archive's own .sam value is replaced.</param>
/// <param name="Names">The object's name per language (<c>English</c>, ...); the selected language, then English, else the .sam name.</param>
public sealed record ModObjectDefinition( string Archive, string? Input, string Theme, ObjectCategory Category, int InfoId, IReadOnlyDictionary<string, string> Names );

/// <summary>
/// A parsed and validated <c>objects.json</c>: the range of Info.Ids the mod reserves and its object definitions. Parsing is
/// strict in the <see cref="StrictJson"/> style (unknown and duplicate fields, comments and trailing commas are refused), sizes
/// are bounded, archive paths may not leave their folder, and every Info.Id must lie in the declared range, which follows the one rule
/// of <see cref="CrossEditionObjects.RangeProblem"/> (above every id the original games use, inside the 16-bit Info.Id of their saves). Layout records join on Info.Id, so ids stay stable across versions of a mod.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public sealed record ModObjectsFile
{
	public const int SchemaVersion = 1;
	public const int MaximumBytes = 256 * 1024;
	public const int MaximumObjects = 256;
	public const int MaximumNames = 16;
	public const int MaximumNameLength = 80;
	public required int FirstId { get; init; }
	public required int LastId { get; init; }
	public required IReadOnlyList<ModObjectDefinition> Objects { get; init; }

	private static readonly (string Directory, ObjectCategory Category)[] Categories =
	{
		("rides", ObjectCategory.Ride), ("shops", ObjectCategory.Shop), ("sideshow", ObjectCategory.Sideshow),
		("features", ObjectCategory.Feature), ("upgrades", ObjectCategory.Upgrade),
	};

	/// <summary>The category directory name of <paramref name="category"/> (<c>rides</c>, <c>shops</c>, <c>sideshow</c>, ...).</summary>
	public static string CategoryName( ObjectCategory category ) => Categories.First( pair => pair.Category == category ).Directory;

	/// <summary>Reads <c>objects.json</c> from a mod folder; a missing, linked, non-regular or oversized file is refused before it is parsed.</summary>
	public static ModObjectsFile Load( string path, IReadOnlyCollection<string> inputIds ) =>
		Parse( ModFiles.ReadBounded( path, MaximumBytes, "objects.json" ), inputIds );

	/// <summary>Parses and validates the bytes; <paramref name="inputIds"/> are the inputs the mod's manifest declares. Throws <see cref="InvalidDataException"/> with the reason.</summary>
	public static ModObjectsFile Parse( ReadOnlySpan<byte> utf8, IReadOnlyCollection<string> inputIds )
	{
		if ( utf8.Length > MaximumBytes )
			throw Invalid( $"is larger than {MaximumBytes} bytes" );
		var document = StrictJson.Deserialize<Document>( utf8, "objects.json" );
		if ( document.Schema != SchemaVersion )
			throw Invalid( $"'schema' must be {SchemaVersion}" );
		var range = document.IdRange ?? throw Invalid( "'idRange' is required" );
		if ( range.First is not { } first || range.Last is not { } last )
			throw Invalid( "'idRange' needs 'first' and 'last'" );
		// [EXT:cross-edition-objects] the one id rule of every mod's range: above the original ids, inside the 16-bit Info.Id of original saves
		if ( CrossEditionObjects.RangeProblem( first, last ) is { } problem )
			throw Invalid( $"'idRange' {first}..{last} {problem}" );
		var items = document.Objects ?? throw Invalid( "'objects' is required" );
		if ( items.Count > MaximumObjects )
			throw Invalid( $"'objects' has more than {MaximumObjects} entries" );

		var objects = new List<ModObjectDefinition>();
		var ids = new HashSet<int>();
		var archives = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var item in items )
		{
			if ( item == null )
				throw Invalid( "'objects' has a null entry" );
			var archive = ModManifest.NormalizeRelativePath( item.Archive ?? throw Invalid( "'objects[].archive' is required" ), "'objects[].archive'" );
			if ( archive.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) )
				throw Invalid( $"archive '{archive}' is named without its .wad extension" );
			if ( item.Input != null && !inputIds.Contains( item.Input ) )
				throw Invalid( $"archive '{archive}' reads from the input '{item.Input}', which the manifest does not declare" );
			var theme = item.Theme ?? throw Invalid( "'objects[].theme' is required" );
			if ( !ObjectCatalog.Themes.Contains( theme ) )
				throw Invalid( $"theme '{theme}' must be one of {string.Join( ", ", ObjectCatalog.Themes )}" );
			var categoryName = item.Category ?? throw Invalid( "'objects[].category' is required" );
			var category = Categories.Where( pair => pair.Directory == categoryName ).Select( pair => (ObjectCategory?)pair.Category ).FirstOrDefault()
				?? throw Invalid( $"category '{categoryName}' must be one of {string.Join( ", ", Categories.Select( pair => pair.Directory ) )}" );
			var infoId = item.InfoId ?? throw Invalid( $"object '{archive}' has no 'infoId'" );
			if ( infoId < first || infoId > last )
				throw Invalid( $"object '{archive}' has the infoId {infoId}, outside the declared range {first} to {last}" );
			if ( !ids.Add( infoId ) )
				throw Invalid( $"infoId {infoId} is used by more than one object" );
			if ( !archives.Add( $"{theme}/{categoryName}/{ObjectCatalog.ArchiveKey( Path.GetFileName( archive ) )}" ) )
				throw Invalid( $"theme {theme} lists two {categoryName} archives named '{ObjectCatalog.ArchiveKey( Path.GetFileName( archive ) )}'" );
			objects.Add( new ModObjectDefinition( archive, item.Input, theme, category, infoId, Names( item.Names, archive ) ) );
		}
		return new ModObjectsFile { FirstId = first, LastId = last, Objects = objects };
	}

	private static IReadOnlyDictionary<string, string> Names( Dictionary<string, string?>? names, string archive )
	{
		var result = new SortedDictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		if ( names == null )
			return result;
		if ( names.Count > MaximumNames )
			throw Invalid( $"object '{archive}' has more than {MaximumNames} names" );
		foreach ( var (language, name) in names )
		{
			if ( language.Length == 0 || language.Length > 32 || !language.All( char.IsAsciiLetter ) )
				throw Invalid( $"object '{archive}' names the language '{language}'; use a language name such as English" );
			if ( string.IsNullOrWhiteSpace( name ) || name.Length > MaximumNameLength || name.Any( char.IsControl ) )
				throw Invalid( $"the {language} name of '{archive}' must be 1 to {MaximumNameLength} characters without control characters" );
			if ( !result.TryAdd( language, name ) )
				throw Invalid( $"object '{archive}' names the language '{language}' twice" );
		}
		return result;
	}

	private static InvalidDataException Invalid( string reason ) => new( $"objects.json: {reason}." );

	// The JSON shape; StrictJson refuses unknown and duplicate members.
	private sealed class Document
	{
		public int Schema { get; init; }
		public RangeDocument? IdRange { get; init; }
		public List<ObjectDocument?>? Objects { get; init; }
	}

	private sealed class RangeDocument
	{
		public int? First { get; init; }
		public int? Last { get; init; }
	}

	private sealed class ObjectDocument
	{
		public string? Archive { get; init; }
		public string? Input { get; init; }
		public string? Theme { get; init; }
		public string? Category { get; init; }
		public int? InfoId { get; init; }
		public Dictionary<string, string?>? Names { get; init; }
	}
}
