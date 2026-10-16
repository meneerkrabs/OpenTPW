using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The minimal TPI-rides example mod of docs/MODS.md, written to a temporary folder at test time: a manifest, an
/// <c>objects.json</c> and nothing else. It holds no Theme Park Inc bytes; its objects name archives of the Theme Park Inc
/// install the player supplies as the input <c>tpi</c>.
/// </summary>
internal static class TpiRidesFixture
{
	public const string Id = "community.tpi-rides";

	/// <summary>One object of the example: the TPI archive (below the install's Data folder), its Theme Park World theme and category, its reserved id and English name.</summary>
	public sealed record Item( string Archive, string Theme, string Category, int InfoId, string English, string? German = null );

	/// <summary>
	/// Two TPI rides with a queue (one into the jungle, one into the space park), the TPI litter bin, whose script runs the Theme Park Inc
	/// only opcode WAITABS, and a drinks shop.
	/// </summary>
	public static readonly IReadOnlyList<Item> Items = new[]
	{
		new Item( "levels/arabian/rides/tbounce", "jungle", "rides", 40001, "Turban Bounce", "Turban-Huepfer" ),
		new Item( "levels/science/rides/robot", "space", "rides", 40002, "Giant Robot Swing" ),
		new Item( "levels/science/Facilities/litbin", "space", "features", 40003, "Science Litter Bin" ),
		new Item( "levels/arabian/shops/drinks", "jungle", "shops", 40004, "Desert Drinks" ),
	};

	/// <summary>The Theme Park Inc world each park theme takes archives from in the bulk example: arabian to the jungle, science to space, water to fantasy.</summary>
	public static readonly IReadOnlyDictionary<string, string> WorldThemes = new Dictionary<string, string> { ["arabian"] = "jungle", ["science"] = "space", ["water"] = "fantasy" };

	/// <summary>
	/// Every object archive of the install's three worlds, listed for the bulk example: one item per archive in the rides, shops, sideshow, features and
	/// upgrades folders, in the park theme of its world, with consecutive ids from 40001 and the archive's name as its English name.
	/// </summary>
	public static IReadOnlyList<Item> ArchivesOf( string dataFolder )
	{
		var items = new List<Item>();
		var id = 40000;
		foreach (var (world, theme) in WorldThemes)
			foreach (var category in new[] { "rides", "shops", "sideshow", "features", "upgrades" })
			{
				var folder = Path.Combine( dataFolder, "levels", world, category );
				if (!Directory.Exists( folder ))
					continue;
				foreach (var wad in Directory.GetFiles( folder, "*.wad" ).Order( StringComparer.OrdinalIgnoreCase ))
				{
					var name = Path.GetFileNameWithoutExtension( wad );
					items.Add( new Item( $"levels/{world}/{category}/{name}", theme, category, ++id, $"{world} {name}" ) );
				}
			}
		return items;
	}

	public static string ObjectsJson( IEnumerable<Item> items, int last = 40099 ) =>
		$$"""{ "schema": 1, "idRange": { "first": 40000, "last": {{last}} }, "objects": [{{string.Join( ",", items.Select( item =>
			$$"""{ "archive": "{{item.Archive}}", "input": "tpi", "theme": "{{item.Theme}}", "category": "{{item.Category}}", "infoId": {{item.InfoId}}, "names": { "English": "{{item.English}}"{{(item.German == null ? "" : $$""", "German": "{{item.German}}" """)}} } }""" ) )}}] }""";

	public const string Manifest = """
		{ "schema": 1, "id": "community.tpi-rides", "name": "Theme Park Inc rides", "version": "1.0.0", "category": "community",
		  "edition": "theme-park-world",
		  "dependencies": [ { "id": "opentpw.cross-edition-objects", "version": ">=1.0.0" } ],
		  "inputs": [ { "id": "tpi", "kind": "original-data", "edition": "theme-park-inc" } ],
		  "content": { "objects": "objects.json" } }
		""";

	/// <summary>Writes the mod below <paramref name="parent"/> and returns its folder.</summary>
	public static string Write( string parent, IEnumerable<Item>? items = null, int last = 40099 )
	{
		var directory = Path.Combine( parent, Id );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ), Manifest );
		File.WriteAllText( Path.Combine( directory, "objects.json" ), ObjectsJson( items ?? Items, last ), new UTF8Encoding( false ) );
		return directory;
	}

	/// <summary>The settings that turn the extension and the mod on and point the input <c>tpi</c> at <paramref name="tpiFolder"/>.</summary>
	public static ModSettings Settings( string tpiFolder ) => new()
	{
		Enabled = new Dictionary<string, bool> { [OfficialExtensions.CrossEditionObjects.Id] = true, [Id] = true },
		Inputs = new Dictionary<string, IReadOnlyDictionary<string, string>> { [Id] = new Dictionary<string, string> { ["tpi"] = tpiFolder } },
	};

	/// <summary>Resolves the example mod from <paramref name="modFolder"/> with <paramref name="settings"/> for a Theme Park World run.</summary>
	public static ModSet Resolve( string modFolder, ModSettings settings ) =>
		ModSet.Resolve( ModDiscovery.Discover( new[] { modFolder }, Array.Empty<string>(), null, null ).ToList(), settings, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
}
