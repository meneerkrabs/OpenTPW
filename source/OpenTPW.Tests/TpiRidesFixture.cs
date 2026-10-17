using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The Theme Park Inc rides mod OpenTPW ships (<c>content/mods/opentpw.tpi-rides</c>, docs/MODS.md): a manifest, an <c>objects.json</c> and
/// nothing else. It holds no Theme Park Inc bytes; its objects name archives of the Theme Park Inc install the player supplies as the input
/// <c>tpi</c>. The tests read the shipped files themselves (copied next to the test binaries like the game's other content), so the tests and the
/// shipped mod cannot drift. <see cref="Generate"/> (run by <c>TpiRidesShippedModTests</c> with <c>OPENTPW_WRITE_TPI_RIDES=1</c>) rewrites the
/// shipped <c>objects.json</c> from a real install.
/// </summary>
internal static class TpiRidesFixture
{
	public const string Id = "opentpw.tpi-rides";

	/// <summary>One object of the mod: the TPI archive (below the install's Data folder), its Theme Park World theme and category, its reserved id and English name.</summary>
	public sealed record Item( string Archive, string Theme, string Category, int InfoId, string English, string? German = null );

	/// <summary>The program folder the shipped mod is discovered in (the test binaries' folder: <c>content/mods/opentpw.tpi-rides</c> is copied beside them).</summary>
	public static string ProgramDirectory => AppContext.BaseDirectory;

	/// <summary>The shipped mod's folder next to the test binaries.</summary>
	public static string ShippedFolder => Path.Combine( ModDiscovery.ProgramModsDirectory( ProgramDirectory ), Id );

	/// <summary>The shipped mod's folder in the repository's <c>content</c> folder (what a release is built from), or null outside a checkout.</summary>
	public static string? RepositoryFolder()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "scripts", "smoke-tpi-rides.sh" ) ) )
			directory = directory.Parent;
		return directory == null ? null : Path.Combine( directory.FullName, "content", ModDiscovery.FolderName, Id );
	}

	/// <summary>The shipped manifest text.</summary>
	public static string Manifest => File.ReadAllText( Path.Combine( ShippedFolder, ModManifest.FileName ) );

	/// <summary>The shipped objects, in file order.</summary>
	public static IReadOnlyList<Item> Items => Parse( File.ReadAllBytes( Path.Combine( ShippedFolder, "objects.json" ) ) );

	public static IReadOnlyList<Item> Parse( byte[] objectsJson ) =>
		ModObjectsFile.Parse( objectsJson, new[] { "tpi" } ).Objects.Select( definition =>
			new Item( definition.Archive, definition.Theme, ModObjectsFile.CategoryName( definition.Category ), definition.InfoId,
				definition.Names["English"], definition.Names.TryGetValue( "German", out var german ) ? german : null ) ).ToList();

	/// <summary>The Theme Park Inc world each park theme takes archives from: arabian to the jungle, science to space, water to fantasy.</summary>
	public static readonly IReadOnlyDictionary<string, string> WorldThemes = new Dictionary<string, string> { ["arabian"] = "jungle", ["science"] = "space", ["water"] = "fantasy" };

	/// <summary>The object folders of a world that the mod lists; each archive of them is a candidate.</summary>
	public static readonly string[] Categories = { "rides", "shops", "sideshow", "features", "upgrades" };

	/// <summary>
	/// Every object archive of the install's three worlds: one item per archive in the rides, shops, sideshow, features and upgrades folders, in the
	/// park theme of its world, with consecutive ids from 40001 and "&lt;World&gt; &lt;archive name&gt;" as its English name. Which of them the parks list
	/// is decided by loading them (<c>TpiRidesShippedModTests</c>).
	/// </summary>
	public static IReadOnlyList<Item> ArchivesOf( string dataFolder )
	{
		var items = new List<Item>();
		var id = 40000;
		foreach ( var (world, theme) in WorldThemes )
			foreach ( var category in Categories )
			{
				var folder = Path.Combine( dataFolder, "levels", world, category );
				if ( !Directory.Exists( folder ) )
					continue;
				foreach ( var wad in Directory.GetFiles( folder, "*.wad" ).Order( StringComparer.OrdinalIgnoreCase ) )
				{
					var name = Path.GetFileNameWithoutExtension( wad );
					items.Add( new Item( $"levels/{world}/{category}/{name}", theme, category, ++id, $"{char.ToUpperInvariant( world[0] )}{world[1..]} {name}" ) );
				}
			}
		return items;
	}

	public static string ObjectsJson( IEnumerable<Item> items, int first = 40000, int last = 40499 )
	{
		var lines = items.Select( item =>
		{
			var names = $"\"English\": \"{item.English}\"" + (item.German == null ? "" : $", \"German\": \"{item.German}\"");
			return $"  {{ \"archive\": \"{item.Archive}\", \"input\": \"tpi\", \"theme\": \"{item.Theme}\", \"category\": \"{item.Category}\", \"infoId\": {item.InfoId}, \"names\": {{ {names} }} }}";
		} );
		return $"{{ \"schema\": 1, \"idRange\": {{ \"first\": {first}, \"last\": {last} }}, \"objects\": [\n{string.Join( ",\n", lines )} ] }}\n";
	}

	/// <summary>
	/// Writes a copy of the shipped mod below <c>&lt;<paramref name="programParent"/>&gt;/content/mods</c>, with <paramref name="items"/> instead of the shipped objects when given,
	/// and returns the program folder (where discovery finds it as shipped content).
	/// </summary>
	public static string Write( string programParent, IEnumerable<Item>? items = null )
	{
		var directory = Path.Combine( ModDiscovery.ProgramModsDirectory( programParent ), Id );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ), Manifest );
		var objects = items == null ? File.ReadAllBytes( Path.Combine( ShippedFolder, "objects.json" ) ) : new UTF8Encoding( false ).GetBytes( ObjectsJson( items ) );
		File.WriteAllBytes( Path.Combine( directory, "objects.json" ), objects );
		return programParent;
	}

	/// <summary>The settings that turn the extension and the mod on and point the input <c>tpi</c> at <paramref name="tpiFolder"/>.</summary>
	public static ModSettings Settings( string tpiFolder ) => new()
	{
		Enabled = new Dictionary<string, bool> { [OfficialExtensions.CrossEditionObjects.Id] = true, [Id] = true },
		Inputs = new Dictionary<string, IReadOnlyDictionary<string, string>> { [Id] = new Dictionary<string, string> { ["tpi"] = tpiFolder } },
	};

	/// <summary>The mod found as shipped content in <paramref name="programDirectory"/>.</summary>
	public static IReadOnlyList<DiscoveredMod> Discover( string? programDirectory = null ) =>
		ModDiscovery.Discover( Array.Empty<string>(), Array.Empty<string>(), null, programDirectory ?? ProgramDirectory );

	/// <summary>Resolves the shipped mod (found in <paramref name="programDirectory"/>, by default beside the test binaries) with <paramref name="settings"/> for a Theme Park World run.</summary>
	public static ModSet Resolve( ModSettings settings, string? programDirectory = null ) =>
		ModSet.Resolve( Discover( programDirectory ).ToList(), settings, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
}
