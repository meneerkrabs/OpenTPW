using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// The winter extension's own content (docs/WINTER.md, "Content"): a folder <c>content/winter</c> of the program, copied next to the program with the rest of <c>content/</c>, holding <c>objects.json</c> and the object
/// archives below it (the winter kiosks and glühwein stalls, one per Theme Park World theme, ids 64010 to 64499), listed in the theme catalogs while the extension is on as a content root of the id <c>opentpw.winter</c> (asset keys
/// <c>mod:opentpw.winter:/...</c>, so a park save records and checks the shops it places like a mod's objects). The item sprites and the anchors beside it are the drawing's (phase W4). Nothing of it comes from
/// either original game: it is generated from code by <c>tools/winter/build-content.sh</c>.
/// </summary>
// [EXT:winter-content] OpenTPW's own shipped content; neither Theme Park World nor Theme Park Inc ships anything of it
public static class WinterContent
{
	/// <summary>The folder name below <c>content/</c>.</summary>
	public const string FolderName = "winter";
	/// <summary>The first Info.Id of the extension's reservation (64000 to 64009 belong to the example lamp mod; the range sits below the umbrellas' <see cref="UmbrellaContent.FirstReservedId"/>).</summary>
	public const int FirstReservedId = 64010;
	/// <summary>The last Info.Id of the reservation.</summary>
	public const int LastReservedId = 64499;

	/// <summary>Where the content is found: tests may point it elsewhere (null: <c>content/winter</c> below the program folder).</summary>
	internal static string? DirectoryOverride { get; set; }

	/// <summary>The folder of the winter content below a program folder.</summary>
	public static string DirectoryOf( string programDirectory ) => Path.Combine( programDirectory, "content", FolderName );

	/// <summary>The folder of the winter content in this run, or null when it is not there.</summary>
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
		if ( !set.IsEnabled( OfficialExtensions.Winter.Id ) || GameEditions.IsThemeParkInc || Directory is not { } folder )
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
			var root = new CatalogContentRoot( ContentRoot.Mod( OfficialExtensions.Winter.Id ), folder, objects.Objects );
			lock ( roots )
				return roots.TryAdd( Path.GetFullPath( folder ), root ) ? root : roots[Path.GetFullPath( folder )];
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException or UnauthorizedAccessException )
		{
			Log?.Warning( $"The winter extension's objects are not available: {exception.Message}" );
			return null;
		}
	}
}
