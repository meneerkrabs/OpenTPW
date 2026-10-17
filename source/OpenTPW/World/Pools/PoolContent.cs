using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// The pools extension's own content (docs/POOLS.md, "Content"): a folder <c>content/pools</c> of the program, copied next to the program with the rest of <c>content/</c> and embedded in the browser build, holding
/// <list type="bullet">
/// <item><c>objects.json</c> and the object archives below it: the changing rooms, one per theme (<see cref="PoolChangingRooms"/>), listed in the theme catalogs while the extension is on as a content
/// root of the id <c>opentpw.pools</c> (asset keys <c>mod:opentpw.pools:/...</c>, so a park save records and checks them like a mod's objects);</item>
/// <item><c>esprites/Swim</c>: the swimwear sprite sets of the eight Theme Park World kid sets, drawn at twice the pixel size (<see cref="PoolSwimSprites"/>);</item>
/// <item><c>esprites/Generic/Kids</c> and <c>Kidsheads</c>: the eight kid sets and their heads in clothes, redrawn at twice the pixel size, which replace the game's while the extension is on (<see cref="PoolGuestSprites"/>).</item>
/// </list>
/// Nothing of it comes from the original game: it is generated from code by <c>tools/pools/build-content.sh</c> (docs/CONTENT-KIT.md).
/// </summary>
// [EXT:pools-content] OpenTPW's own shipped content; the original game has no pools
public static class PoolContent
{
	/// <summary>The folder name below <c>content/</c>.</summary>
	public const string FolderName = "pools";
	/// <summary>The first Info.Id OpenTPW reserves for its official extensions' objects (above every id of the original games, inside the 16-bit id of their saves).</summary>
	public const int FirstReservedId = 65000;
	/// <summary>
	/// The last Info.Id of the reservation: 65000 to 65535 belong to the pools extension. The pool extras take 65000 to 65099, the water slides
	/// 65100 to 65199, the water attractions 65200 to 65299 and the changing rooms 65500 to 65519; the rest is spare (docs/POOLS.md).
	/// </summary>
	public const int LastReservedId = 65535;

	/// <summary>Where the content is found: tests may point it elsewhere (null: <c>content/pools</c> below the program folder).</summary>
	internal static string? DirectoryOverride { get; set; }

	/// <summary>The folder of the pools content below a program folder.</summary>
	public static string DirectoryOf( string programDirectory ) => Path.Combine( programDirectory, "content", FolderName );

	/// <summary>The folder of the pools content in this run, or null when it is not there.</summary>
	public static string? Directory
	{
		get
		{
			var folder = DirectoryOverride ?? DirectoryOf( AppContext.BaseDirectory );
			return System.IO.Directory.Exists( folder ) ? folder : null;
		}
	}

	private static readonly Dictionary<string, CatalogContentRoot> roots = new( StringComparer.Ordinal );

	/// <summary>
	/// The catalog content root of the extension's objects, or null while the extension is off or the content is missing or broken (a warning says which). One root is made per folder, so the
	/// catalogs cached for it keep the file system they were read from.
	/// </summary>
	internal static CatalogContentRoot? CatalogRoot( ModSet set )
	{
		if ( !set.IsEnabled( OfficialExtensions.Pools.Id ) || GameEditions.IsThemeParkInc || Directory is not { } folder )
			return null;
		var file = Path.Combine( folder, "objects.json" );
		if ( !File.Exists( file ) )
			return null;
		lock ( roots )
		{
			if ( roots.TryGetValue( Path.GetFullPath( folder ), out var known ) )
				return known;
		}
		try
		{
			var objects = ModObjectsFile.Load( file, Array.Empty<string>() );
			var root = new CatalogContentRoot( ContentRoot.Mod( OfficialExtensions.Pools.Id ), folder, objects.Objects );
			lock ( roots )
				return roots.TryAdd( Path.GetFullPath( folder ), root ) ? root : roots[Path.GetFullPath( folder )];
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException or UnauthorizedAccessException )
		{
			Log?.Warning( $"The pools extension's objects are not available: {exception.Message}" );
			return null;
		}
	}
}
