using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// The umbrellas extension's own content (docs/UMBRELLAS.md, "Content"): a folder <c>content/umbrellas</c> of the program, copied next to the program with the rest of <c>content/</c>, holding <c>objects.json</c> and the object
/// archives below it (the brolly shops, one per Theme Park World theme, ids 64500 to 64999), listed in the theme catalogs while the extension is on as a content root of the id <c>opentpw.umbrellas</c> (asset keys
/// <c>mod:opentpw.umbrellas:/...</c>, so a park save records and checks the shops it places like a mod's objects). The umbrella sprites and the hand anchors beside it are the drawing's (phase U3). Nothing of it comes from
/// either original game: it is generated from code by <c>tools/umbrellas/build-content.sh</c>.
/// </summary>
// [EXT:umbrellas-content] OpenTPW's own shipped content; neither Theme Park World nor Theme Park Inc ships anything of it
public static class UmbrellaContent
{
	/// <summary>The folder name below <c>content/</c>.</summary>
	public const string FolderName = "umbrellas";
	/// <summary>The first Info.Id of the extension's reservation (below the pools' <see cref="PoolContent.FirstReservedId"/>, above the Theme Park Inc rides' 40000 to 40499).</summary>
	public const int FirstReservedId = 64500;
	/// <summary>The last Info.Id of the reservation.</summary>
	public const int LastReservedId = 64999;

	/// <summary>Where the content is found: tests may point it elsewhere (null: <c>content/umbrellas</c> below the program folder).</summary>
	internal static string? DirectoryOverride { get; set; }

	/// <summary>The folder of the umbrellas content below a program folder.</summary>
	public static string DirectoryOf( string programDirectory ) => Path.Combine( programDirectory, "content", FolderName );

	/// <summary>The folder of the umbrellas content in this run, or null when it is not there.</summary>
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
	/// The catalog content root of the extension's objects, or null while the extension is off, in a Theme Park Inc run, or while the content is missing or broken (a warning says which). One root is made per folder, so the
	/// catalogs cached for it keep the file system they were read from.
	/// </summary>
	internal static CatalogContentRoot? CatalogRoot( ModSet set )
	{
		if ( !set.IsEnabled( OfficialExtensions.Umbrellas.Id ) || GameEditions.IsThemeParkInc || Directory is not { } folder )
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
			var root = new CatalogContentRoot( ContentRoot.Mod( OfficialExtensions.Umbrellas.Id ), folder, objects.Objects );
			lock ( roots )
				return roots.TryAdd( Path.GetFullPath( folder ), root ) ? root : roots[Path.GetFullPath( folder )];
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException or UnauthorizedAccessException )
		{
			Log?.Warning( $"The umbrellas extension's objects are not available: {exception.Message}" );
			return null;
		}
	}
}
