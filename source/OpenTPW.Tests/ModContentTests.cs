using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>Mod content roots (docs/MODS.md): objects.json, overrides, strings and the catalog entries of a synthetic mod. No game data is used.</summary>
[TestClass]
[DoNotParallelize]
public class ModContentTests
{
	private string root = null!;
	private string game = null!;
	private BaseFileSystem? previousFileSystem;

	private static readonly IOpenTpwExtension Core = new OpenTpwExtension( "opentpw.core-test", "Core test", new ModVersion( 1, 0, 0 ), true, false );

	[TestInitialize]
	public void Initialize()
	{
		root = Directory.CreateTempSubdirectory( "opentpw-modcontent-" ).FullName;
		game = Path.Combine( root, "game" );
		previousFileSystem = FileSystem;
		// a game with one jungle ride and a shared texture folder
		Archive( game, "levels/jungle/rides/totem", "totem", 1110, "Inca Totem" );
		Directory.CreateDirectory( Path.Combine( game, "levels", "jungle", "sharetex" ) );
		File.WriteAllText( Path.Combine( game, "levels", "jungle", "sharetex", "leaf.txt" ), "original" );
		FileSystem = new BaseFileSystem( game );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
		Directory.Delete( root, true );
	}

	private static void Archive( string baseDirectory, string path, string member, int id, string name )
	{
		var directory = Path.Combine( baseDirectory, path );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, member + ".sam" ), $"Info.Id\t{id}\nInfo.Name\t\"{name}\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t100\n" );
		File.WriteAllBytes( Path.Combine( directory, member + ".MD2" ), Array.Empty<byte>() );
	}

	private static string ObjectsJson( int first, int last, params string[] objects ) =>
		$$"""{ "schema": 1, "idRange": { "first": {{first}}, "last": {{last}} }, "objects": [{{string.Join( ",", objects )}}] }""";

	private static string ObjectJson( string archive, int id, string category = "rides", string theme = "jungle", string? input = null, string names = "\"English\": \"Zoom\", \"German\": \"Sause\"" ) =>
		$$"""{ "archive": "{{archive}}", {{(input == null ? "" : $"\"input\": \"{input}\", ")}}"theme": "{{theme}}", "category": "{{category}}", "infoId": {{id}}, "names": { {{names}} } }""";

	/// <summary>A mod folder with its manifest and files.</summary>
	private string Mod( string id, string extra = "", params (string Path, string Text)[] files )
	{
		var directory = Path.Combine( root, "mods", id );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ),
			$$"""{ "schema": 1, "id": "{{id}}", "name": "Test {{id}}", "version": "1.0.0", "category": "community"{{extra}} }""" );
		foreach ( var (path, text) in files )
		{
			var full = Path.Combine( directory, path );
			Directory.CreateDirectory( Path.GetDirectoryName( full )! );
			File.WriteAllText( full, text );
		}
		return directory;
	}

	private static ModSet Resolve( ModSettings settings, params string[] directories ) =>
		ModSet.Resolve( ModDiscovery.Discover( directories, Array.Empty<string>(), null, null ).ToList(), settings, null, GameEdition.ThemeParkWorld, new[] { Core } );

	private static ModSettings Enable( params string[] ids ) => new() { Enabled = ids.ToDictionary( id => id, _ => true ) };

	private static ModObjectsFile ParseObjects( string json, params string[] inputs ) => ModObjectsFile.Parse( Encoding.UTF8.GetBytes( json ), inputs );

	private static void AssertInvalid( string json, string expected, params string[] inputs ) =>
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParseObjects( json, inputs ) ).Message, expected );

	// ---- objects.json ---------------------------------------------------------------------

	[TestMethod]
	public void ObjectsJsonParsesAndIsValidatedStrictly()
	{
		var file = ParseObjects( ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20001 ), ObjectJson( "levels/space/shops/cafe", 20002, "shops", "space", "tpi" ) ), "tpi" );
		Assert.AreEqual( (20000, 20009), (file.FirstId, file.LastId) );
		Assert.AreEqual( 2, file.Objects.Count );
		Assert.AreEqual( ObjectCategory.Shop, file.Objects[1].Category );
		Assert.AreEqual( "tpi", file.Objects[1].Input );
		Assert.AreEqual( "Sause", file.Objects[0].Names["German"] );

		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20010 ) ), "outside the declared range" );
		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "objects/a", 20001 ), ObjectJson( "objects/b", 20001 ) ), "more than one object" );
		AssertInvalid( ObjectsJson( 1000, 1009 ), "'idRange' must lie between" );
		AssertInvalid( ObjectsJson( 20000, 30000 ), "'idRange' must lie between" );
		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "../outside/zoom", 20001 ) ), "leaves the mod's folder" );
		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "/abs/zoom", 20001 ) ), "leaves the mod's folder" );
		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom.wad", 20001 ) ), ".wad extension" );
		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20001, theme: "moon" ) ), "theme 'moon'" );
		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20001, category: "rocks" ) ), "category 'rocks'" );
		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20001, input: "tpi" ) ), "does not declare" );
		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20001, names: "\"Eng lish\": \"Zoom\"" ) ), "language" );
		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20001, names: "\"English\": \"\"" ) ), "1 to 80 characters" );
		AssertInvalid( ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20001 ), ObjectJson( "other/Zoom", 20002 ) ), "two rides archives" );
		AssertInvalid( """{ "schema": 2, "idRange": { "first": 20000, "last": 20001 }, "objects": [] }""", "'schema'" );
		AssertInvalid( """{ "schema": 1, "idRange": { "first": 20000, "last": 20001 }, "objects": [], "extra": 1 }""", "malformed" );
		AssertInvalid( """{ "schema": 1, "schema": 1, "idRange": { "first": 20000, "last": 20001 }, "objects": [] }""", "more than once" );
	}

	[TestMethod]
	public void AnOversizedObjectsFileIsRefusedBeforeParsing()
	{
		var big = new byte[ModObjectsFile.MaximumBytes + 1];
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ModObjectsFile.Parse( big, Array.Empty<string>() ) ).Message, "larger than" );
	}

	// ---- Resolution -----------------------------------------------------------------------

	[TestMethod]
	public void AModWithObjectsIsValidatedAtResolution()
	{
		var good = Mod( "community.good", ", \"content\": { \"objects\": \"objects.json\" }", ("objects.json", ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20001 ) )) );
		var missing = Mod( "community.missing", ", \"content\": { \"objects\": \"nothing.json\" }" );
		var broken = Mod( "community.broken", ", \"content\": { \"objects\": \"objects.json\" }", ("objects.json", "{ nope" ) );
		var undeclared = Mod( "community.undeclared", ", \"content\": { \"objects\": \"objects.json\" }", ("objects.json", ObjectsJson( 21000, 21009, ObjectJson( "objects/zoom", 21001, input: "tpi" ) )) );
		var set = Resolve( Enable( "community.good", "community.missing", "community.broken", "community.undeclared" ), good, missing, broken, undeclared );
		Assert.AreEqual( 1, set.Mods.Count );
		Assert.IsNotNull( set.Mods[0].Objects );
		Assert.AreEqual( 20001, set.Mods[0].Objects!.Objects.Single().InfoId );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.missing" ).Reason, "not a file of the mod" );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.broken" ).Reason, "objects.json" );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.undeclared" ).Reason, "does not declare" );
	}

	[TestMethod]
	public void ObjectsOfAThemeParkIncRunAreRefused()
	{
		var directory = Mod( "community.objects", ", \"content\": { \"objects\": \"objects.json\" }", ("objects.json", ObjectsJson( 20000, 20009 )) );
		var set = ModSet.Resolve( ModDiscovery.Discover( new[] { directory }, Array.Empty<string>(), null, null ).ToList(), Enable( "community.objects" ), null, GameEdition.ThemeParkInc, new[] { Core } );
		StringAssert.Contains( set.Refused.Single().Reason, "Theme Park World" );
	}

	[TestMethod]
	public void InputsMustBeGameDataFolders()
	{
		var directory = Mod( "community.tpi", ", \"inputs\": [{ \"id\": \"tpi\", \"kind\": \"original-data\" }], \"content\": { \"objects\": \"objects.json\" }",
			("objects.json", ObjectsJson( 20000, 20009, ObjectJson( "levels/jungle/rides/bumper", 20001, input: "tpi" ) )) );
		var empty = Path.Combine( root, "not-a-game" );
		Directory.CreateDirectory( empty );
		Assert.AreEqual( 0, Resolve( Inputs( "community.tpi", empty ), directory ).Mods.Count, "a folder without Data content is refused" );
		var tpi = Path.Combine( root, "tpi" );
		Archive( Path.Combine( tpi, "Data" ), "levels/jungle/rides/bumper", "bumper", 99, "Bumper" );
		var set = Resolve( Inputs( "community.tpi", tpi ), directory );
		Assert.AreEqual( Path.Combine( tpi, "Data" ), set.Mods.Single().InputDirectories["tpi"] );
	}

	private static ModSettings Inputs( string id, string folder, string input = "tpi" ) =>
		Enable( id ) with { Inputs = new Dictionary<string, IReadOnlyDictionary<string, string>> { [id] = new Dictionary<string, string> { [input] = folder } } };

	[TestMethod]
	public void UnorderedConflictsAreRefusedAndOrderedOnesResolvedByAfter()
	{
		string[] OverrideMod( string id, string text, string? after = null ) =>
			new[] { Mod( id, $", \"content\": {{ \"overrides\": \"data\" }}{(after == null ? "" : $", \"after\": [\"{after}\"]")}", ("data/levels/jungle/sharetex/leaf.txt", text) ) };
		var a = OverrideMod( "community.a", "from a" )[0];
		var b = OverrideMod( "community.b", "from b" )[0];
		var unordered = Resolve( Enable( "community.a", "community.b" ), a, b );
		Assert.AreEqual( 0, unordered.Mods.Count, "both are refused; there is no silent last-wins" );
		Assert.AreEqual( 2, unordered.Refused.Count );
		StringAssert.Contains( unordered.Refused[0].Reason, "neither declares the other in 'after'" );

		var after = OverrideMod( "community.c", "from c", after: "community.a" )[0];
		var ordered = Resolve( Enable( "community.a", "community.c" ), a, after );
		CollectionAssert.AreEqual( new[] { "community.a", "community.c" }, ordered.Mods.Select( mod => mod.Id ).ToArray() );
		using ( ModContentRoots.Use( ordered, FileSystem ) )
			Assert.AreEqual( "from c", ReadText( "/levels/jungle/sharetex/leaf.txt" ), "the mod ordered after wins" );
		Assert.AreEqual( "original", ReadText( "/levels/jungle/sharetex/leaf.txt" ), "the hooks are gone with the scope" );
	}

	[TestMethod]
	public void OverlappingIdRangesAndSameNamedArchivesAreRefused()
	{
		var one = Mod( "community.one", ", \"content\": { \"objects\": \"objects.json\" }", ("objects.json", ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20001 ) )) );
		var two = Mod( "community.two", ", \"content\": { \"objects\": \"objects.json\" }", ("objects.json", ObjectsJson( 20005, 20015, ObjectJson( "other/spin", 20006 ) )) );
		var three = Mod( "community.three", ", \"content\": { \"objects\": \"objects.json\" }", ("objects.json", ObjectsJson( 30000, 30009, ObjectJson( "objects/zoom", 30001 ) )) );
		var overlap = Resolve( Enable( "community.one", "community.two" ), one, two );
		Assert.AreEqual( 0, overlap.Mods.Count );
		StringAssert.Contains( overlap.Refused[0].Reason, "overlap" );
		var same = Resolve( Enable( "community.one", "community.three" ), one, three );
		Assert.AreEqual( 0, same.Mods.Count );
		StringAssert.Contains( same.Refused[0].Reason, "object archive" );
		var apart = Mod( "community.apart", ", \"content\": { \"objects\": \"objects.json\" }", ("objects.json", ObjectsJson( 40000, 40009, ObjectJson( "objects/zoom", 40001, category: "features" )) ) );
		Assert.AreEqual( 2, Resolve( Enable( "community.one", "community.apart" ), one, apart ).Mods.Count, "the same name in another category is another archive" );
	}

	[TestMethod]
	public void StringFilesMustSitInALanguageFolder()
	{
		var bad = Mod( "community.bad", ", \"content\": { \"strings\": \"strings\" }", ("strings/UITEXT.str", "x") );
		var good = Mod( "community.good", ", \"content\": { \"strings\": \"strings\" }", ("strings/German/UITEXT.str", "x") );
		var set = Resolve( Enable( "community.bad", "community.good" ), bad, good );
		CollectionAssert.AreEqual( new[] { "community.good" }, set.Mods.Select( mod => mod.Id ).ToArray() );
		StringAssert.Contains( set.Refused.Single().Reason, "<Language>/<NAME>.str" );
		var other = Mod( "community.other", ", \"content\": { \"strings\": \"strings\" }", ("strings/german/uitext.STR", "y") );
		Assert.AreEqual( 0, Resolve( Enable( "community.good", "community.other" ), good, other ).Mods.Count, "the same table of the same language, whatever the case, conflicts" );
	}

	// ---- Content ----------------------------------------------------------------------------

	private static string ReadText( string path ) => FileSystem.ReadAllText( path );

	[TestMethod]
	public void OverridesReplaceGameFilesWithoutCaseAndOnlyForTheScope()
	{
		var directory = Mod( "community.look", ", \"content\": { \"overrides\": \"data/\" }",
			("data/Levels/Jungle/ShareTex/LEAF.txt", "modded"), ("data/levels/jungle/sharetex/new.txt", "added") );
		var set = Resolve( Enable( "community.look" ), directory );
		using ( ModContentRoots.Use( set, FileSystem ) )
		{
			Assert.AreEqual( "modded", ReadText( "/levels/jungle/sharetex/leaf.txt" ) );
			Assert.AreEqual( 6, FileSystem.GetSize( "/levels/jungle/sharetex/leaf.txt" ) );
			Assert.IsTrue( FileSystem.FileExists( "/Levels/jungle/sharetex/NEW.txt" ), "a file the game lacks is added" );
			Assert.AreEqual( "added", ReadText( "/levels/jungle/sharetex/new.txt" ) );
			Assert.AreEqual( "original", File.ReadAllText( Path.Combine( game, "levels", "jungle", "sharetex", "leaf.txt" ) ), "the game's own file is never written" );
		}
		Assert.IsNull( FileSystem.Overrides );
		Assert.IsFalse( FileSystem.FileExists( "/levels/jungle/sharetex/new.txt" ) );
	}

	[TestMethod]
	public void OverridesReplaceMembersOfArchivesAndInvalidateCachedLookups()
	{
		var cached = new BaseFileSystem( game ) { CacheLookups = true };
		var totem = "/levels/jungle/rides/totem/totem.sam";
		Assert.IsTrue( cached.ReadAllText( totem ).Contains( "Inca Totem" ) );
		var directory = Mod( "community.sam", ", \"content\": { \"overrides\": \"data\" }", ("data/levels/jungle/rides/totem/totem.sam", "Info.Id 1110\nInfo.Name \"Mod Totem\"\n") );
		using ( ModContentRoots.Use( Resolve( Enable( "community.sam" ), directory ), cached ) )
			Assert.IsTrue( cached.ReadAllText( totem ).Contains( "Mod Totem" ) );
		Assert.IsTrue( cached.ReadAllText( totem ).Contains( "Inca Totem" ) );
	}

	[TestMethod]
	public void StringTablesOfAModReplaceTheLanguagesOwn()
	{
		var directory = Mod( "community.words", ", \"content\": { \"strings\": \"strings\" }", ("strings/German/UITEXT.str", "modded text") );
		var set = Resolve( Enable( "community.words" ), directory );
		Assert.IsNull( GameLanguage.StringOverride );
		using ( ModContentRoots.Use( set, FileSystem ) )
		{
			using var stream = GameLanguage.StringOverride!( "German", "UITEXT.str" )!;
			Assert.AreEqual( "modded text", new StreamReader( stream ).ReadToEnd() );
			Assert.IsNull( GameLanguage.StringOverride( "French", "UITEXT.str" ) );
			Assert.IsNull( GameLanguage.StringOverride( "German", "OBJECT_NAMES.str" ) );
			// a string table is also the file at that path of the game's data
			Assert.AreEqual( "modded text", ReadText( "/Language/German/UITEXT.str" ) );
		}
		Assert.IsNull( GameLanguage.StringOverride );
	}

	[TestMethod]
	public void ModObjectsJoinTheCatalogWithTheirOwnIdsNamesAndRoots()
	{
		var tpi = Path.Combine( root, "tpi", "Data" );
		Archive( tpi, "levels/jungle/rides/bumper", "bumper", 7, "TPI Bumper" );
		var directory = Mod( "community.rides", ", \"inputs\": [{ \"id\": \"tpi\", \"kind\": \"original-data\" }], \"content\": { \"objects\": \"objects.json\" }",
			("objects.json", ObjectsJson( 20000, 20009, ObjectJson( "objects/zoom", 20001 ), ObjectJson( "levels/jungle/rides/bumper", 20002, input: "tpi", names: "\"English\": \"Bumper Cars\"" ),
				ObjectJson( "objects/stall", 20003, "shops" ) )) );
		Archive( directory, "objects/zoom", "zoom", 5, "Zoomer" );
		Archive( directory, "objects/stall", "stall", 6, "Stall" );
		var plain = ObjectCatalog.Load( "jungle" );
		Assert.AreEqual( 1, plain.Entries.Count );
		var set = Resolve( Inputs( "community.rides", Path.Combine( root, "tpi" ) ), directory );
		Assert.AreEqual( 1, set.Mods.Count, string.Join( "; ", set.Refused.Select( refusal => refusal.Reason ) ) );
		using ( ModContentRoots.Use( set, FileSystem ) )
		{
			var catalog = ObjectCatalog.Load( "jungle" );
			Assert.AreNotSame( plain, catalog, "the mod set is part of the catalog's identity" );
			CollectionAssert.AreEqual( new[] { 1110, 20001, 20002, 20003 }, catalog.Entries.Select( entry => entry.InfoId ).ToArray() );
			var zoom = catalog.Get( 20001 );
			Assert.AreEqual( "Zoom", zoom.DisplayName, "the name from objects.json replaces the .sam's" );
			Assert.AreEqual( 20001, zoom.Settings.GetInt( "Info.Id" ), "the .sam's own Info.Id (5) is replaced by the assigned one" );
			Assert.AreEqual( "community.rides", zoom.ModId );
			Assert.AreEqual( ContentRoot.Mod( "community.rides" ), zoom.Root );
			Assert.IsTrue( zoom.IsExternal );
			Assert.IsFalse( zoom.IsBonus );
			Assert.AreEqual( "Bumper Cars", catalog.Get( 20002 ).DisplayName );
			Assert.AreEqual( ObjectCategory.Shop, catalog.Get( 20003 ).Category );
			Assert.IsNull( catalog.Find( 1110 )!.ModId );
			// each root has its own file system, and the entries read from it
			Assert.AreNotSame( FileSystem, zoom.FileSystem );
			Assert.AreNotSame( zoom.FileSystem, catalog.Get( 20002 ).FileSystem );
			Assert.IsTrue( zoom.FileSystem.FileExists( zoom.ModelPath ) );
			Assert.AreSame( catalog, ObjectCatalog.Load( "jungle" ), "cached for the same set" );
			// the asset manifest pins each root under its own prefix
			var manifest = ParkResumeAssetManifest.Capture( catalog, "jungle", false );
			Assert.IsTrue( manifest.ContainsKey( "mod:community.rides:/objects/zoom/zoom.sam" ) );
			Assert.IsTrue( manifest.ContainsKey( "mod:community.rides:/levels/jungle/rides/bumper/bumper.sam" ) );
			Assert.IsTrue( manifest.ContainsKey( "data:/levels/jungle/rides/totem/totem.sam" ) );
			Assert.IsFalse( manifest.Keys.Any( key => key.StartsWith( "bonus:", StringComparison.Ordinal ) ) );
		}
		var after = ObjectCatalog.Load( "jungle" );
		Assert.AreEqual( 1, after.Entries.Count, "without the set the catalog is the game's again" );
		Assert.AreSame( plain, after );
	}

	[TestMethod]
	public void AnObjectWithATakenArchiveNameOrNoSamIsSkippedNotSilently()
	{
		var directory = Mod( "community.dup", ", \"content\": { \"objects\": \"objects.json\" }",
			("objects.json", ObjectsJson( 20000, 20009, ObjectJson( "objects/totem", 20001 ), ObjectJson( "objects/missing", 20002 ), ObjectJson( "objects/empty", 20003 ) )) );
		Archive( directory, "objects/totem", "totem", 5, "Dup" );
		Directory.CreateDirectory( Path.Combine( directory, "objects", "empty" ) );
		var warnings = new List<string>();
		var previousLog = Log;
		Log = new Logger();
		Logger.LogDelegate capture = ( severity, text ) => { if ( severity == Logger.Level.Warning ) warnings.Add( text ); };
		Logger.OnLog += capture;
		try
		{
			using ( ModContentRoots.Use( Resolve( Enable( "community.dup" ), directory ), FileSystem ) )
			{
				var catalog = ObjectCatalog.Load( "jungle" );
				Assert.AreEqual( 1, catalog.Entries.Count );
			}
		}
		finally
		{
			Logger.OnLog -= capture;
			Log = previousLog;
		}
		Assert.IsTrue( warnings.Any( warning => warning.Contains( "already loaded" ) ), string.Join( "\n", warnings ) );
		Assert.IsTrue( warnings.Any( warning => warning.Contains( "no archive or folder" ) ), string.Join( "\n", warnings ) );
		Assert.IsTrue( warnings.Any( warning => warning.Contains( "no .sam file" ) ), string.Join( "\n", warnings ) );
	}

	[TestMethod]
	public void NoModsInstallNothing()
	{
		var plain = ObjectCatalog.Load( "jungle" );
		var empty = ModSet.Defaults( new[] { Core } );
		using ( ModContentRoots.Use( empty, FileSystem ) )
		{
			Assert.IsNull( FileSystem.Overrides );
			Assert.IsNull( GameLanguage.StringOverride );
			Assert.AreSame( plain, ObjectCatalog.Load( "jungle" ), "an empty set keeps the catalog and its cache key" );
		}
	}

	/// <summary>
	/// Real data (OPENTPW_GAME_PATH, inconclusive without it): the original install as a mod input. The Jungle totem archive is added to the
	/// Hallow rides under a new Info.Id, and a mod override replaces a real texture-folder file; the original objects stay as they were.
	/// </summary>
	[TestMethod]
	public void AnOriginalInstallWorksAsAModInput()
	{
		var original = ObjectCatalogCorpusTests.UseOriginalData( out var previous );
		if ( original == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the mod input corpus." );
		FileSystem = previous;
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		var directory = Mod( "community.original-input", ", \"inputs\": [{ \"id\": \"original\", \"kind\": \"original-data\" }], \"content\": { \"objects\": \"objects.json\" }",
			("objects.json", ObjectsJson( 20000, 20009, ObjectJson( "levels/jungle/rides/totem", 20001, theme: "hallow", input: "original", names: "\"English\": \"Borrowed Totem\"" ) )) );
		FileSystem = original;
		ObjectCatalog.BonusDataRoot = null;
		var plain = ObjectCatalog.Load( "hallow" );
		var plainJungle = ObjectCatalog.Load( "jungle" );
		var set = Resolve( Inputs( "community.original-input", dataPath, "original" ), directory );
		Assert.AreEqual( 1, set.Mods.Count, string.Join( "; ", set.Refused.Select( refusal => refusal.Reason ) ) );
		using ( ModContentRoots.Use( set, FileSystem ) )
		{
			var catalog = ObjectCatalog.Load( "hallow" );
			Assert.AreEqual( plain.Entries.Count + 1, catalog.Entries.Count );
			var borrowed = catalog.Get( 20001 );
			Assert.AreEqual( "Borrowed Totem", borrowed.DisplayName );
			Assert.AreEqual( ObjectCategory.Ride, borrowed.Category );
			Assert.AreEqual( "hallow", borrowed.Theme );
			Assert.AreEqual( 1110, ObjectCatalog.Load( "jungle" ).Get( 1110 ).InfoId, "the original is untouched" );
			Assert.AreEqual( plainJungle.Entries.Count, ObjectCatalog.Load( "jungle" ).Entries.Count );
			using ( var model = borrowed.FileSystem.OpenRead( borrowed.ModelPath ) )
				Assert.IsTrue( model.Length > 0, "the model is read from the original's archive through the input root" );
			Assert.IsTrue( borrowed.Shape.OccupiedCells.Any() );
			Assert.AreEqual( ContentRoot.Mod( "community.original-input" ), borrowed.Root );
			// each catalog entry's manifest members hash through the root they were read from
			var manifest = ParkResumeAssetManifest.Capture( catalog, "hallow", false );
			Assert.IsTrue( manifest.Keys.Any( key => key.StartsWith( "mod:community.original-input:/levels/jungle/rides/totem/", StringComparison.Ordinal ) ) );
		}
		Assert.AreEqual( plain.Entries.Count, ObjectCatalog.Load( "hallow" ).Entries.Count );
	}
}
