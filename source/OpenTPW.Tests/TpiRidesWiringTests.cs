using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The wiring of a data mod that brings Theme Park Inc attractions into a Theme Park World park (docs/MODS.md), on synthetic data: the
/// example mod itself, the command-line choices, the sound root, the shared textures, the economy catalogue and the decoder for another
/// file system's textures. The end-to-end tests over the real install are <see cref="TpiRidesEndToEndTests"/>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TpiRidesWiringTests
{
	private string root = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-tpi-wiring-" ).FullName;
		previousFileSystem = FileSystem;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
		ContentSoundRoots.Clear();
		Directory.Delete( root, true );
	}

	private static void WriteSam( string folder, string member, int id, string name, int cost )
	{
		Directory.CreateDirectory( folder );
		File.WriteAllText( Path.Combine( folder, member + ".sam" ), $"Info.Id\t{id}\nInfo.Name\t\"{name}\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t{cost}\nUpgrades[0].CostOfResearch\t0\n" );
		File.WriteAllBytes( Path.Combine( folder, member + ".MD2" ), Array.Empty<byte>() );
	}

	/// <summary>A tiny Theme Park World game: the balance files the economy reads, the Rides.sam defaults and one jungle ride.</summary>
	private string Game()
	{
		var game = Path.Combine( root, "game" );
		Directory.CreateDirectory( Path.Combine( game, "levels", "jungle", "rides" ) );
		File.WriteAllText( Path.Combine( game, "levels", "Standard.sam" ), EconomyTestData.StandardText() );
		File.WriteAllText( Path.Combine( game, "levels", "jungle", "Standard.sam" ), "" );
		File.WriteAllText( Path.Combine( game, "levels", "jungle", "rides", "Rides.sam" ), "Info.IsChoosable\t1\nInfo.WhichUIType\t0\nResearch.Category\t0\nResearch.Group\t0\n" );
		WriteSam( Path.Combine( game, "levels", "jungle", "rides", "totem" ), "totem", 1110, "Inca Totem", 100 );
		FileSystem = new BaseFileSystem( game );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		return game;
	}

	/// <summary>A folder the Theme Park Inc detection recognises by its structure (Ver.txt, a Theme Park Inc world, no Theme Park World one) with one science ride.</summary>
	private string Install( bool asThemeParkInc = true )
	{
		var data = Path.Combine( root, "tpi", "Data" );
		WriteSam( Path.Combine( data, "levels", "science", "rides", "gadget" ), "gadget", 1190, "Gadget", 700 );
		Directory.CreateDirectory( Path.Combine( data, "levels", asThemeParkInc ? "arabian" : "jungle" ) );
		File.WriteAllText( Path.Combine( data, "Ver.txt" ), "1.0" );
		return Path.Combine( root, "tpi" );
	}

	private ModSet Resolve( string install, bool extension, bool dependency = true )
	{
		var mod = Path.Combine( root, "mods", "community.tpi-rides" );
		Directory.CreateDirectory( mod );
		var dependencies = dependency ? """, "dependencies": [ { "id": "opentpw.cross-edition-objects" } ]""" : "";
		File.WriteAllText( Path.Combine( mod, ModManifest.FileName ), $$"""
			{ "schema": 1, "id": "community.tpi-rides", "name": "Test", "version": "1.0.0", "category": "community"{{dependencies}},
			  "inputs": [ { "id": "tpi", "kind": "original-data", "edition": "theme-park-inc" } ], "content": { "objects": "objects.json" } }
			""" );
		File.WriteAllText( Path.Combine( mod, "objects.json" ), """
			{ "schema": 1, "idRange": { "first": 40000, "last": 40009 }, "objects": [
			  { "archive": "levels/science/rides/gadget", "input": "tpi", "theme": "jungle", "category": "rides", "infoId": 40001, "names": { "English": "Gadget Ride", "German": "Geraet" } } ] }
			""" );
		var enabled = new Dictionary<string, bool> { ["community.tpi-rides"] = true, [OfficialExtensions.CrossEditionObjects.Id] = extension };
		var settings = new ModSettings
		{
			Enabled = enabled,
			Inputs = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["community.tpi-rides"] = new Dictionary<string, string> { ["tpi"] = install } },
		};
		return ModSet.Resolve( ModDiscovery.Discover( new[] { mod }, Array.Empty<string>(), null, null ).ToList(), settings, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
	}

	// ---- The example mod -------------------------------------------------------------------------------------

	[TestMethod]
	public void TheShippedModIsTwoSmallValidFilesWithoutAnyInstallData()
	{
		var folder = TpiRidesFixture.ShippedFolder;
		CollectionAssert.AreEquivalent( new[] { "objects.json", "opentpw-mod.json" }, Directory.GetFiles( folder ).Select( Path.GetFileName ).ToArray() );
		Assert.AreEqual( 0, Directory.GetDirectories( folder ).Length );
		Assert.IsTrue( Directory.GetFiles( folder, "*", SearchOption.AllDirectories ).All( file => new FileInfo( file ).Length < 64 * 1024 ), "text only, no install data" );
		var file = ModObjectsFile.Parse( File.ReadAllBytes( Path.Combine( folder, "objects.json" ) ), new[] { "tpi" } );
		Assert.AreEqual( TpiRidesFixture.Items.Count, file.Objects.Count );
		Assert.IsTrue( file.Objects.All( definition => definition.Input == "tpi" && definition.InfoId >= file.FirstId && definition.InfoId <= file.LastId ) );
		Assert.IsTrue( file.Objects.All( definition => definition.Archive.StartsWith( "levels/", StringComparison.Ordinal ) && TpiRidesFixture.WorldThemes[definition.Archive.Split( '/' )[1]] == definition.Theme ), "each archive joins the theme of its world" );
		Assert.AreEqual( "Turban-Huepfer", file.Objects[0].Names["German"] );
		Assert.IsNull( CrossEditionObjects.RangeProblem( file.FirstId, file.LastId ) );
		var manifest = ModManifest.Parse( File.ReadAllBytes( Path.Combine( folder, ModManifest.FileName ) ) );
		Assert.AreEqual( TpiRidesFixture.Id, manifest.Id );
		Assert.AreEqual( ModCategory.Official, manifest.Category );
		Assert.AreEqual( GameEdition.ThemeParkWorld, manifest.Edition );
		Assert.AreEqual( "opentpw.cross-edition-objects", manifest.Dependencies.Single().Id );
		Assert.AreEqual( "theme-park-inc", manifest.Inputs.Single().Edition );
		Assert.AreEqual( "tpi", manifest.Inputs.Single().Id );
	}

	[TestMethod]
	public void TheShippedModIsFoundAsProgramContentAndIsOffByDefault()
	{
		var found = TpiRidesFixture.Discover().Single( mod => mod.Id == TpiRidesFixture.Id );
		Assert.IsTrue( found.IsValid, found.Error );
		Assert.AreEqual( ModSource.Program, found.Source );
		// off by default: without a saved choice it is not part of the run, and nothing else needs it
		var plain = ModSet.Resolve( TpiRidesFixture.Discover().ToList(), ModSettings.Default, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		Assert.AreEqual( 0, plain.Mods.Count );
		Assert.AreEqual( 0, plain.Refused.Count );
		Assert.IsFalse( plain.IsEnabled( OfficialExtensions.CrossEditionObjects.Id ), "the extension is off by default too" );
		var row = ModRows.Build( TpiRidesFixture.Discover(), ModSettings.Default, plain, OfficialExtensions.All ).Single( candidate => candidate.Id == TpiRidesFixture.Id );
		Assert.AreEqual( ModRowStatus.Off, row.Status );
		Assert.IsFalse( row.Enabled );
		// the same id from a mod folder of the player is refused: opentpw. ids come only from content shipped with OpenTPW
		var custom = Path.Combine( root, "custom", TpiRidesFixture.Id );
		Directory.CreateDirectory( custom );
		File.Copy( Path.Combine( TpiRidesFixture.ShippedFolder, ModManifest.FileName ), Path.Combine( custom, ModManifest.FileName ) );
		var refused = ModDiscovery.Discover( new[] { custom }, Array.Empty<string>(), null, null ).Single();
		Assert.IsFalse( refused.IsValid );
		StringAssert.Contains( refused.Error, "reserved for content shipped with OpenTPW" );
	}

	[TestMethod]
	public void TheShippedModResolvesWithItsExtensionAndAnInputFolderAndNeedsBoth()
	{
		var input = Directory.CreateDirectory( Path.Combine( root, "any", "Data" ) ).Parent!.FullName;
		var on = TpiRidesFixture.Resolve( TpiRidesFixture.Settings( input ) );
		Assert.AreEqual( 0, on.Refused.Count, string.Join( ";", on.Refused.Select( refusal => refusal.Reason ) ) );
		Assert.AreEqual( TpiRidesFixture.Id, on.Mods.Single().Id );
		// without its extension it is refused with the reason; without an input folder too
		var off = TpiRidesFixture.Resolve( new ModSettings { Enabled = new Dictionary<string, bool> { [TpiRidesFixture.Id] = true }, Inputs = TpiRidesFixture.Settings( input ).Inputs } );
		StringAssert.Contains( off.Refused.Single().Reason, "opentpw.cross-edition-objects" );
		var noInput = TpiRidesFixture.Resolve( new ModSettings { Enabled = TpiRidesFixture.Settings( input ).Enabled } );
		Assert.AreEqual( ModRefusalKind.MissingInput, noInput.Refused.Single( refusal => refusal.Id == TpiRidesFixture.Id ).Kind );
		// it adds objects, so it is recorded in park saves
		Assert.AreEqual( 1, on.SimulationMods.Count( mod => mod.Id == TpiRidesFixture.Id ) );
	}

	[TestMethod]
	public void TheSmokeScriptRunsTheShippedModInsteadOfWritingACopy()
	{
		var repository = TpiRidesFixture.RepositoryFolder();
		if ( repository == null )
			Assert.Inconclusive( "The repository's scripts folder is not beside the test binaries." );
		// a Windows checkout may turn the script into CRLF (scripts/*.sh is pinned to LF in .gitattributes, but the test does not depend on it)
		var script = File.ReadAllText( Path.Combine( repository!, "..", "..", "..", "scripts", "smoke-tpi-rides.sh" ) ).Replace( "\r\n", "\n" );
		Assert.IsTrue( script.Contains( "--enable-mod opentpw.tpi-rides" ), "the script enables the shipped mod" );
		Assert.IsTrue( script.Contains( "opentpw.tpi-rides:tpi=" ), "the script chooses the input" );
		Assert.IsFalse( script.Contains( "<<'JSON'" ), "the script uses the shipped files, not a second copy" );
		// the files the build ships (content/** is copied to the output and to the release) are the ones the tests read
		CollectionAssert.AreEqual( File.ReadAllBytes( Path.Combine( repository, "objects.json" ) ), File.ReadAllBytes( Path.Combine( TpiRidesFixture.ShippedFolder, "objects.json" ) ) );
		CollectionAssert.AreEqual( File.ReadAllBytes( Path.Combine( repository, ModManifest.FileName ) ), File.ReadAllBytes( Path.Combine( TpiRidesFixture.ShippedFolder, ModManifest.FileName ) ) );
	}

	[TestMethod]
	public void ContentRootsAreNotSharedBetweenFoldersWithTheSameContent()
	{
		// the digest names the mod's files, not where they are: the same files in another folder must read that folder (a cached root of a deleted folder would be stale)
		var install = Install();
		var first = CatalogRoots( Resolve( install, extension: true ) );
		var again = CatalogRoots( Resolve( install, extension: true ) );
		Assert.AreSame( first.Single(), again.Single(), "the same folder and content reuse the root" );
		var other = Path.Combine( root, "other-install" );
		foreach ( var directory in Directory.GetDirectories( install, "*", SearchOption.AllDirectories ) )
			Directory.CreateDirectory( directory.Replace( install, other ) );
		foreach ( var file in Directory.GetFiles( install, "*", SearchOption.AllDirectories ) )
			File.Copy( file, file.Replace( install, other ) );
		var elsewhere = CatalogRoots( Resolve( other, extension: true ) );
		Assert.AreNotSame( first.Single(), elsewhere.Single(), "another input folder gets its own root" );
		StringAssert.StartsWith( elsewhere.Single().Directory, Path.GetFullPath( other ) );
	}

	// the pools extension (on by default) adds a root of its own; these tests are about the mods' roots
	private static IReadOnlyList<CatalogContentRoot> CatalogRoots( ModSet set ) => ModContentRoots.CatalogRoots( set ).Where( contentRoot => contentRoot.ModId != OfficialExtensions.Pools.Id && contentRoot.ModId != OfficialExtensions.Umbrellas.Id && contentRoot.ModId != OfficialExtensions.Winter.Id && contentRoot.ModId != OfficialExtensions.Evening.Id ).ToList();

	// ---- Command line ----------------------------------------------------------------------------------------

	[TestMethod]
	public void TheCommandLineEnablesModsAndChoosesInputsForOneRun()
	{
		var discovered = TpiRidesFixture.Discover();
		var saved = ModSettings.Default;
		var settings = ModStartup.WithCommandLine( saved, discovered, new[] { "opentpw.cross-edition-objects", "opentpw.tpi-rides" }, new[] { "opentpw.tpi-rides:tpi=/games/Theme Park Inc" } );
		Assert.IsTrue( settings.IsEnabled( "opentpw.cross-edition-objects", false ) );
		Assert.IsTrue( settings.IsEnabled( "opentpw.tpi-rides", false ) );
		Assert.AreEqual( "/games/Theme Park Inc", settings.InputPath( "opentpw.tpi-rides", "tpi" ) );
		Assert.AreEqual( 0, saved.Enabled.Count, "the saved choices are not changed" );
		var set = ModStartup.Resolve( discovered, settings with { Inputs = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["opentpw.tpi-rides"] = new Dictionary<string, string> { ["tpi"] = root } } }, Array.Empty<string>(), new List<string>() );
		Assert.IsTrue( set.IsEnabled( "opentpw.cross-edition-objects" ), "the extension is on for the run" );
		Assert.ThrowsException<ArgumentException>( () => ModStartup.WithCommandLine( saved, discovered, new[] { "community.unknown" }, Array.Empty<string>() ) );
		foreach ( var bad in new[] { "tpi=/x", "opentpw.tpi-rides:tpi", "opentpw.tpi-rides:=/x", "opentpw.tpi-rides:tpi=", "Not A Mod:tpi=/x" } )
			StringAssert.Contains( Assert.ThrowsException<ArgumentException>( () => ModStartup.WithCommandLine( saved, discovered, Array.Empty<string>(), new[] { bad } ) ).Message, "--mod-input" );
		var splitsOnTheFirstEquals = ModStartup.WithCommandLine( saved, discovered, Array.Empty<string>(), new[] { "opentpw.tpi-rides:tpi=/games/a=b" } );
		Assert.AreEqual( "/games/a=b", splitsOnTheFirstEquals.InputPath( "opentpw.tpi-rides", "tpi" ) );
	}

	// ---- Loading ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void AThemeParkIncInputIsASoundRootAndASharedTextureRootOnlyWithTheExtension()
	{
		Game();
		var install = Install();
		var set = Resolve( install, extension: true );
		Assert.AreEqual( 0, set.Refused.Count, string.Join( ";", set.Refused.Select( refusal => refusal.Reason ) ) );
		using ( ModContentRoots.Use( set, FileSystem ) )
		{
			var entry = ObjectCatalog.Load( "jungle" ).Find( 40001 )!;
			Assert.AreEqual( "Gadget Ride", entry.DisplayName );
			Assert.AreEqual( "Gadget Ride", entry.SettingsName, "the mod's English name is the object's name in every language" );
			Assert.AreEqual( 40001, entry.InfoId, "the .sam's own 1190 is replaced" );
			var source = ContentSoundRoots.SourceOf( entry );
			Assert.IsNotNull( source );
			Assert.AreEqual( "science", source!.Value.Theme );
			// the shared textures are those of the theme folder of the archive in the install, read from the install
			var folders = ObjectAssets.TextureDirectories( entry ).ToArray();
			Assert.IsTrue( folders.All( folder => ReferenceEquals( folder.FileSystem, entry.FileSystem ) ) );
			CollectionAssert.Contains( folders.Select( folder => folder.Directory ).ToArray(), "/levels/science/sharetex" );
			CollectionAssert.DoesNotContain( folders.Select( folder => folder.Directory ).ToArray(), "/levels/jungle/sharetex" );
			Assert.IsNull( ContentSoundRoots.SourceOf( ObjectCatalog.Load( "jungle" ).Find( 1110 )! ), "the game's own objects keep the running game's banks" );
		}
		Assert.IsNull( ContentSoundRoots.SourceOf( ObjectCatalog.Load( "jungle" ).Find( 1110 )! ) );
		using ( ModContentRoots.Use( ModSet.Defaults( OfficialExtensions.All ), FileSystem ) )
		{
			Assert.IsNull( ObjectCatalog.Load( "jungle" ).Find( 40001 ), "no mod, no object" );
			Assert.AreEqual( 0, ContentSoundRoots.Snapshot().Count );
		}
		// the mod without its extension: it loads (it declared no dependency), but nothing routes sounds to the install
		using ( ModContentRoots.Use( Resolve( install, extension: false, dependency: false ), FileSystem ) )
		{
			var entry = ObjectCatalog.Load( "jungle" ).Find( 40001 )!;
			Assert.IsNull( ContentSoundRoots.SourceOf( entry ), "the extension is off: the running game's banks" );
		}
	}

	[TestMethod]
	public void AFolderThatIsNotThemeParkIncDataGetsNoSoundRoot()
	{
		Game();
		var install = Install( asThemeParkInc: false );
		using ( ModContentRoots.Use( Resolve( install, extension: true ), FileSystem ) )
		{
			var entry = ObjectCatalog.Load( "jungle" ).Find( 40001 )!;
			Assert.IsNull( ContentSoundRoots.SourceOf( entry ), "declared as Theme Park Inc, but it holds Theme Park World's worlds" );
			var folders = ObjectAssets.TextureDirectories( entry ).Select( folder => folder.Directory ).ToArray();
			CollectionAssert.Contains( folders, "/levels/jungle/sharetex", "the park's own theme folder, as for any other mod object" );
		}
	}

	[TestMethod]
	public void TheEconomyCataloguePricesAndNamesTheModObjectsAfterTheGamesAndSkipsThemWithoutAMod()
	{
		Game();
		var plain = EconomyObjectCatalog.Load( "jungle", false );
		Assert.AreEqual( 1, plain.Objects.Count );
		using ( ModContentRoots.Use( Resolve( Install(), extension: true ), FileSystem ) )
		{
			var catalog = EconomyObjectCatalog.Load( "jungle", false );
			// the evening (63900 and up), winter (64010 and up), umbrellas (64500 and up) and pools (65000 and up) extensions, on by default, list their own objects in the same catalog
			CollectionAssert.AreEqual( new[] { 1110, 40001 }, catalog.Objects.Select( info => info.InfoId ).Where( id => id < EveningContent.FirstReservedId ).ToArray(), "after the game's own objects" );
			var info = catalog[40001];
			Assert.AreEqual( 700, info.PurchaseCost );
			Assert.AreEqual( "Gadget Ride", info.Name );
			Assert.AreEqual( ParkObjectKind.Ride, info.Kind );
			Assert.AreEqual( "/levels/science/rides/gadget", info.ArchivePath );
			Assert.AreEqual( 0, info.Upgrades[0].CostOfResearch );
			var economy = new ParkEconomy( BalanceSettings.Load( "jungle", false ), catalog, ParkGameMode.FullSimulation, 1 );
			Assert.IsTrue( economy.Research.IsAvailable( 40001 ) );
			Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 40001, out var bought ) );
			Assert.AreEqual( 700, bought!.TotalSpent );
		}
		Assert.AreEqual( 1, EconomyObjectCatalog.Load( "jungle", false ).Objects.Count, "the scope ended" );
	}

	// ---- Textures of another file system -----------------------------------------------------------------------

	[TestMethod]
	public void TexturesOfAnotherFileSystemDecodeByTheirFormat()
	{
		var folder = Path.Combine( root, "textures" );
		Directory.CreateDirectory( folder );
		// an uncompressed 32-bit 2 x 1 TGA (blue, then red), top-left origin
		var tga = new byte[] { 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 1, 0, 32, 0x28, 255, 0, 0, 255, 0, 0, 255, 255 };
		File.WriteAllBytes( Path.Combine( folder, "wall.tga" ), tga );
		var fileSystem = new BaseFileSystem( root );
		var (data, width, height) = Texture.DecodeImage( fileSystem, "/textures/wall.tga" );
		Assert.AreEqual( (2, 1), (width, height) );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, data );
		File.WriteAllBytes( Path.Combine( folder, "bad.wct" ), new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Texture.DecodeImage( fileSystem, "/textures/bad.wct" ) ).Message, "WCT" );
		File.WriteAllBytes( Path.Combine( folder, "bad.fsh" ), new byte[] { 1, 2, 3 } );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Texture.DecodeImage( fileSystem, "/textures/bad.fsh" ) ).Message, "FSH" );
	}
}
