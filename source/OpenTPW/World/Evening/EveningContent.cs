using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// The evening extension's own content (docs/EVENING.md, "Torches"): a folder <c>content/evening</c> of the program, copied next to the program with the rest of <c>content/</c>, holding <c>objects.json</c> and the object
/// archives below it (a torch per Theme Park World theme, ids 63900 to 63999), listed in the theme catalogs while the extension is on as a content root of the id <c>opentpw.evening</c> (asset keys
/// <c>mod:opentpw.evening:/...</c>, so a park save records and checks the torches it places like a mod's objects). Nothing of it comes from either original game: it is generated from code by
/// <c>tools/evening/build-content.sh</c>.
/// </summary>
// [EXT:evening-torches] user-approved 2026-10-19 ("Themavarianten", "Goedkoop, als een bankje"): OpenTPW's own shipped torches; neither Theme Park World nor Theme Park Inc ships anything of it
public static class EveningContent
{
	/// <summary>The folder name below <c>content/</c>.</summary>
	public const string FolderName = "evening";
	/// <summary>The first Info.Id of the extension's reservation (below the example lamp mod's 64000 to 64009 and the winter extension's <see cref="WinterContent.FirstReservedId"/>).</summary>
	public const int FirstReservedId = 63900;
	/// <summary>The last Info.Id of the reservation.</summary>
	public const int LastReservedId = 63999;

	/// <summary>Where the content is found: tests may point it elsewhere (null: <c>content/evening</c> below the program folder).</summary>
	internal static string? DirectoryOverride { get; set; }

	/// <summary>The folder of the evening content below a program folder.</summary>
	public static string DirectoryOf( string programDirectory ) => Path.Combine( programDirectory, "content", FolderName );

	/// <summary>The folder of the evening content in this run, or null when it is not there.</summary>
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
	/// The catalog content root of the extension's torches, or null while the extension is off, in a Theme Park Inc run (its parks have other themes), or while the content is missing or broken (a warning says which).
	/// One root is made per folder, so the catalogs cached for it keep the file system they were read from.
	/// </summary>
	internal static CatalogContentRoot? CatalogRoot( ModSet set )
	{
		if ( !set.IsEnabled( OfficialExtensions.Evening.Id ) || GameEditions.IsThemeParkInc || Directory is not { } folder )
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
			var root = new CatalogContentRoot( ContentRoot.Mod( OfficialExtensions.Evening.Id ), folder, objects.Objects );
			lock ( roots )
				return roots.TryAdd( Path.GetFullPath( folder ), root ) ? root : roots[Path.GetFullPath( folder )];
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException or UnauthorizedAccessException )
		{
			Log?.Warning( $"The evening extension's torches are not available: {exception.Message}" );
			return null;
		}
	}
}
