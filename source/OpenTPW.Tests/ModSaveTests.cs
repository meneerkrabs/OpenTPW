using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Mods in park saves and the asset manifest (docs/MODS.md, docs/DETERMINISM.md): records, validation, content roots and menus.</summary>
[TestClass]
[DoNotParallelize]
public class ModSaveTests
{
	private string root = null!;

	[TestInitialize]
	public void Initialize() => root = Directory.CreateTempSubdirectory( "opentpw-mod-saves-" ).FullName;

	[TestCleanup]
	public void Cleanup() => Directory.Delete( root, true );

	private static readonly IOpenTpwExtension Physics = new OpenTpwExtension( "opentpw.physics-test", "Physics test", new ModVersion( 1, 2, 0 ), true, true );
	private static readonly IOpenTpwExtension Quiet = new OpenTpwExtension( "opentpw.quiet-test", "Quiet test", new ModVersion( 1, 0, 0 ), true, false );

	/// <summary>Writes a mod folder with a manifest (<paramref name="extra"/> adds fields) and files.</summary>
	private DiscoveredMod Mod( string id, string version = "1.0.0", string extra = "", params (string Path, string Text)[] files )
	{
		var directory = Path.Combine( root, id );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ),
			$$"""{ "schema": 1, "id": "{{id}}", "name": "Test {{id}}", "version": "{{version}}", "category": "community"{{extra}} }""" );
		foreach ( var (path, text) in files )
		{
			var full = Path.Combine( directory, path );
			Directory.CreateDirectory( Path.GetDirectoryName( full )! );
			File.WriteAllText( full, text );
		}
		return ModDiscovery.Discover( new[] { directory }, Array.Empty<string>(), null, null ).Single();
	}

	private static ModSet Resolve( IEnumerable<DiscoveredMod> mods, params IOpenTpwExtension[] extensions ) =>
		ModSet.Resolve( mods.ToList(), new ModSettings { Enabled = mods.ToDictionary( mod => mod.Id, _ => true ) }, null, GameEdition.ThemeParkWorld, extensions );

	private DiscoveredMod Rides( string version = "1.0.0", string text = "rides" ) =>
		// a valid objects.json (the mod set validates it); the name carries the text that makes the content digest differ
		Mod( "community.tpi-rides", version, """, "content": { "objects": "objects.json" }""",
			("objects.json", $$"""{ "schema": 1, "idRange": { "first": 20000, "last": 20009 }, "objects": [{ "archive": "objects/zoom", "theme": "jungle", "category": "rides", "infoId": 20001, "names": { "English": "{{text}}" } }] }""") );

	private DiscoveredMod Textures() =>
		Mod( "community.pretty", "1.0.0", """, "content": { "overrides": "data/" }""", ("data/levels/jungle/ground.fsh", "pixels"), ("data/sounds/clip.wav", "audio") );

	// ---- Content roots -----------------------------------------------------------------------

	[TestMethod]
	public void ContentRootKeysNameTheirRoot()
	{
		Assert.AreEqual( default, ContentRoot.Data );
		Assert.AreEqual( "data:/levels/a.sam", ContentRoot.Data.Key( "/levels/a.sam" ) );
		Assert.AreEqual( "bonus:/levels/a.sam", ContentRoot.Bonus.Key( "/levels/a.sam" ) );
		var mod = ContentRoot.Mod( "community.tpi-rides" );
		Assert.AreEqual( "mod:community.tpi-rides:/levels/a.sam", mod.Key( "/levels/a.sam" ) );
		Assert.IsTrue( mod.IsMod );
		Assert.IsFalse( mod.IsBonus );
		Assert.ThrowsException<ArgumentException>( () => ContentRoot.Mod( "Bad Id" ) );
		Assert.ThrowsException<ArgumentException>( () => ContentRoot.Mod( "plain" ) );
	}

	[TestMethod]
	public void ContentRootKeysParseBackAndRefuseBadOnes()
	{
		foreach ( var root in new[] { ContentRoot.Data, ContentRoot.Bonus, ContentRoot.Mod( "community.x-y_z.1" ) } )
		{
			Assert.IsTrue( ContentRoot.TryParseKey( root.Key( "/a/b.sam" ), out var parsed, out var path ) );
			Assert.AreEqual( root, parsed );
			Assert.AreEqual( "/a/b.sam", path );
		}
		foreach ( var bad in new[] { null, "", "levels/a.sam", "model:/x", "mod:", "mod:community.x", "mod:community.x:", "mod:community.x:no-slash", "mod:Community.x:/a", "mod:other:/a", "mod::/a", "mod:community.x/a:/b" } )
			Assert.IsFalse( ContentRoot.TryParseKey( bad, out _, out _ ), $"'{bad}' is not a content key" );
	}

	[TestMethod]
	public void CatalogEntriesKnowTheirRoot()
	{
		ObjectCatalogEntry Entry( int? bonus, ContentRoot? contentRoot = null ) => new( "jungle", ObjectCategory.Ride, "/a", new ObjectSettings( Array.Empty<ObjectSettingsFile>() ),
			new ObjectShape( new[] { "*" } ), "/m.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(),
			new BaseFileSystem( root ), bonus, contentRoot );
		Assert.AreEqual( ContentRoot.Data, Entry( null ).Root );
		Assert.IsFalse( Entry( null ).IsBonus );
		Assert.AreEqual( ContentRoot.Bonus, Entry( 3 ).Root, "a bonus number alone still means the official bonus content" );
		Assert.IsTrue( Entry( 3 ).IsBonus );
		var mod = Entry( null, ContentRoot.Mod( "community.tpi-rides" ) );
		Assert.AreEqual( "mod:community.tpi-rides:", mod.Root.KeyPrefix );
		Assert.IsFalse( mod.IsBonus );
	}

	// ---- Which mods change the simulation ----------------------------------------------------

	[TestMethod]
	public void OnlyModsThatCanChangeTheSimulationAreRecorded()
	{
		Assert.IsTrue( Resolve( new[] { Rides() } ).SimulationMods.Count == 1, "an object mod changes the simulation" );
		Assert.AreEqual( 0, Resolve( new[] { Textures() } ).SimulationMods.Count, "textures and sounds are presentation" );
		var script = Mod( "community.tweak", "1.0.0", """, "content": { "overrides": "data/" }""", ("data/levels/jungle/rides/totem/totem.rse", "x") );
		Assert.AreEqual( 1, Resolve( new[] { script } ).SimulationMods.Count, "a script override changes the simulation" );
		var unknown = Mod( "community.odd", "1.0.0", """, "content": { "overrides": "data/" }""", ("data/levels/thing.bin", "x") );
		Assert.AreEqual( 1, Resolve( new[] { unknown } ).SimulationMods.Count, "an unknown file type counts as simulation" );
		var strings = Mod( "community.words", "1.0.0", """, "content": { "strings": "strings/" }""", ("strings/en.txt", "x") );
		Assert.AreEqual( 0, Resolve( new[] { strings } ).SimulationMods.Count );
		var alone = Mod( "community.empty", "1.0.0" );
		Assert.AreEqual( 0, Resolve( new[] { alone } ).SimulationMods.Count );
		var outside = Mod( "community.readme", "1.0.0", """, "content": { "overrides": "data/" }""", ("data/a.png", "x"), ("notes/readme.md", "x") );
		Assert.AreEqual( 0, Resolve( new[] { outside } ).SimulationMods.Count, "files outside the overrides folder are not content" );
		var upper = Mod( "community.shout", "1.0.0", """, "content": { "overrides": "data/" }""", ("data/GROUND.FSH", "x") );
		Assert.AreEqual( 0, Resolve( new[] { upper } ).SimulationMods.Count, "extensions are compared without case" );
	}

	[TestMethod]
	public void ExtensionsAreRecordedOnlyWhenTheyChangeTheSimulation()
	{
		var set = Resolve( Array.Empty<DiscoveredMod>(), Physics, Quiet, OfficialExtensions.Online );
		var only = set.SimulationMods.Single();
		Assert.AreEqual( new SimulationMod( "opentpw.physics-test", new ModVersion( 1, 2, 0 ), ModSet.NoFilesSha256 ), only );
		Assert.AreEqual( Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( Array.Empty<byte>() ) ).ToLowerInvariant(), ModSet.NoFilesSha256 );
		Assert.AreEqual( "", set.Digest, "extensions are not files-based mods" );
	}

	[TestMethod]
	public void SimulationModsAreListedInLoadOrderWithTheirContentDigest()
	{
		var rides = Rides();
		var set = Resolve( new[] { rides, Textures() }, Physics );
		CollectionAssert.AreEqual( new[] { "opentpw.physics-test", "community.tpi-rides" }, set.SimulationMods.Select( mod => mod.Id ).ToArray(), "official first" );
		Assert.AreEqual( set.Mods.Single( mod => mod.Id == "community.tpi-rides" ).ContentSha256, set.SimulationMods[1].ContentSha256 );
		Assert.AreNotEqual( ModSet.NoFilesSha256, set.SimulationMods[1].ContentSha256 );
	}

	// ---- Recording ---------------------------------------------------------------------------

	[TestMethod]
	public void NothingIsRecordedWithoutSimulationMods()
	{
		Assert.IsNull( ParkResumeMods.Capture( OpenTpwExtensions.Active ) );
		Assert.IsNull( ParkResumeMods.Capture( ModSet.Defaults( OfficialExtensions.All ) ) );
		Assert.IsNull( ParkResumeMods.Capture( OnlineExtensionGateTests.WithOnline( false ) ) );
		Assert.IsNull( ParkResumeMods.Capture( Resolve( new[] { Textures() }, OfficialExtensions.Online ) ), "a presentation-only mod is never recorded" );
		var records = ParkResumeMods.Capture( Resolve( new[] { Rides() }, Physics ) )!;
		CollectionAssert.AreEqual( new[] { "opentpw.physics-test 1.2.0", "community.tpi-rides 1.0.0" }, records.Select( record => $"{record.Id} {record.Version}" ).ToArray() );
	}

	[TestMethod]
	public void ASaveWithoutModsHasNoModsFieldAndStaysByteIdentical()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		Assert.IsNull( snapshot.Mods );
		var bytes = ParkResumeSaveFile.Serialize( snapshot );
		Assert.IsFalse( Encoding.UTF8.GetString( bytes ).Contains( "\"Mods\"", StringComparison.Ordinal ), "the field is left out, not written as null" );
		Assert.IsTrue( ParkResumeSaveFile.Serialize( ParkResumeSaveFile.Deserialize( bytes ) ).SequenceEqual( bytes ) );
		// an empty or online-only mod set captures nothing, so the same bytes come out
		using ( OpenTpwExtensions.Use( OnlineExtensionGateTests.WithOnline( false ) ) )
			Assert.IsTrue( ParkResumeSaveFile.Serialize( snapshot with { Mods = ParkResumeMods.Capture( OpenTpwExtensions.Active ) } ).SequenceEqual( bytes ) );
	}

	[TestMethod]
	public void RecordedModsRoundTripThroughTheFile()
	{
		var mods = new[] { new ParkModRecord( "community.tpi-rides", "1.0.0", new string( 'a', 64 ) ), new ParkModRecord( "opentpw.x", "2.0.0-beta.1", ModSet.NoFilesSha256 ) };
		var snapshot = ParkResumeSaveFileTests.Envelope() with { Mods = mods };
		var bytes = ParkResumeSaveFile.Serialize( snapshot );
		StringAssert.Contains( Encoding.UTF8.GetString( bytes ), "\"Mods\"" );
		var restored = ParkResumeSaveFile.Deserialize( bytes );
		CollectionAssert.AreEqual( mods, restored.Mods!.ToArray() );
		Assert.IsTrue( ParkResumeSaveFile.Serialize( restored ).SequenceEqual( bytes ) );
	}

	// ---- Shape -------------------------------------------------------------------------------

	private static void Rejects( ParkResumeSnapshot snapshot, string message )
	{
		var exception = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( snapshot ) );
		StringAssert.Contains( exception.Message, message );
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Serialize( snapshot ), "serializing refuses it too" );
	}

	private static ParkModRecord Record( string id = "community.a", string version = "1.0.0", string? digest = null ) =>
		new( id, version, digest ?? new string( 'b', 64 ) );

	[TestMethod]
	public void ShapeValidationBoundsTheRecordedMods()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		ParkResumeSaveFile.ValidateShape( snapshot with { Mods = new[] { Record() } } );
		Rejects( snapshot with { Mods = Array.Empty<ParkModRecord>() }, "mod list is empty" );
		Rejects( snapshot with { Mods = new ParkModRecord[] { null! } }, "empty entry" );
		Rejects( snapshot with { Mods = new[] { Record( "Community.A" ) } }, "not valid" );
		Rejects( snapshot with { Mods = new[] { Record( "plain" ) } }, "not valid" );
		Rejects( snapshot with { Mods = new[] { Record(), Record() } }, "listed twice" );
		Rejects( snapshot with { Mods = new[] { Record( version: "1.0" ) } }, "not a version" );
		Rejects( snapshot with { Mods = new[] { Record( version: "01.0.0" ) } }, "not a version" );
		Rejects( snapshot with { Mods = new[] { Record( digest: "abc" ) } }, "64 lowercase hex" );
		Rejects( snapshot with { Mods = new[] { Record( digest: new string( 'B', 64 ) ) } }, "64 lowercase hex" );
		Rejects( snapshot with { Mods = new[] { Record( digest: null! ) with { ContentSha256 = null! } } }, "64 lowercase hex" );
		Rejects( snapshot with { Mods = Enumerable.Range( 0, ParkResumeMods.MaximumRecords + 1 ).Select( index => Record( $"community.m{index}" ) ).ToArray() }, "above" );
		ParkResumeSaveFile.ValidateShape( snapshot with { Mods = Enumerable.Range( 0, ParkResumeMods.MaximumRecords ).Select( index => Record( $"community.m{index}" ) ).ToArray() } );
	}

	[TestMethod]
	public void ModAssetKeysNeedTheirRecordedMod()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		var hash = new string( 'a', 64 );
		Dictionary<string, string> Assets( string key ) => new( snapshot.AssetHashes ) { [key] = hash };
		ParkResumeSaveFile.ValidateShape( snapshot with { Mods = new[] { Record() }, AssetHashes = Assets( "mod:community.a:/levels/jungle/rides/x/x.sam" ) } );
		Rejects( snapshot with { AssetHashes = Assets( "mod:community.a:/levels/x.sam" ) }, "does not record" );
		Rejects( snapshot with { Mods = new[] { Record( "community.b" ) }, AssetHashes = Assets( "mod:community.a:/levels/x.sam" ) }, "does not record" );
		Rejects( snapshot with { Mods = new[] { Record() }, AssetHashes = Assets( "mod:Bad:/levels/x.sam" ) }, "does not name a mod" );
		Rejects( snapshot with { Mods = new[] { Record() }, AssetHashes = Assets( "mod:community.a:levels" ) }, "does not name a mod" );
		// data: and bonus: keys need no record
		ParkResumeSaveFile.ValidateShape( snapshot with { AssetHashes = Assets( "bonus:/levels/jungle/rides/_x_1/_x.sam" ) } );
	}

	// ---- Loading against the active set --------------------------------------------------------

	private static IReadOnlyList<ParkModRecord> Saved( ModSet set ) => ParkResumeMods.Capture( set )!;

	[TestMethod]
	public void LoadingNeedsTheSameModsInTheSameVersionWithTheSameContent()
	{
		var saved = Saved( Resolve( new[] { Rides() }, Quiet ) );
		ParkResumeMods.Require( saved, Resolve( new[] { Rides() }, Quiet ) );

		string Refusal( ModSet active ) => Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( saved, active ) ).Message;
		// missing
		StringAssert.Contains( Refusal( Resolve( Array.Empty<DiscoveredMod>(), Quiet ) ), "needs community.tpi-rides 1.0.0" );
		// not enabled in settings
		var off = ModSet.Resolve( new[] { Rides() }, ModSettings.Default, null, GameEdition.ThemeParkWorld, new[] { Quiet } );
		StringAssert.Contains( Refusal( off ), "needs community.tpi-rides 1.0.0" );
		// other version
		StringAssert.Contains( Refusal( Resolve( new[] { Rides( "1.1.0" ) }, Quiet ) ), "needs community.tpi-rides 1.0.0, not 1.1.0" );
		// other digest, same version
		StringAssert.Contains( Refusal( Resolve( new[] { Rides( text: "changed" ) }, Quiet ) ), "needs community.tpi-rides 1.0.0 with the content it was saved with" );
	}

	[TestMethod]
	public void AnExtraPresentationModIsAllowedButAnExtraSimulationModIsNot()
	{
		var saved = Saved( Resolve( new[] { Rides() }, Quiet ) );
		ParkResumeMods.Require( saved, Resolve( new[] { Rides(), Textures() }, Quiet ) );
		ParkResumeMods.Require( saved, Resolve( new[] { Rides() }, Quiet, OfficialExtensions.Online ) );
		var message = Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( saved, Resolve( new[] { Rides() }, Physics ) ) ).Message;
		StringAssert.Contains( message, "was saved without opentpw.physics-test 1.2.0" );
		// a park saved without mods does not load under a simulation mod either
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( null, Resolve( new[] { Rides() } ) ) ).Message, "was saved without community.tpi-rides" );
		ParkResumeMods.Require( null, Resolve( new[] { Textures() }, OfficialExtensions.Online ) );
		ParkResumeMods.Require( null, ModSet.Defaults( OfficialExtensions.All ) );
	}

	[TestMethod]
	public void LoadingNeedsTheRecordedLoadOrder()
	{
		var both = Resolve( new[] { Rides() }, Physics );
		var reversed = Saved( both ).Reverse().ToArray();
		var message = Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( reversed, both ) ).Message;
		StringAssert.Contains( message, "order" );
	}

	[TestMethod]
	public void MenusNameTheModsASaveNeeds()
	{
		var saved = Saved( Resolve( new[] { Rides() }, Physics ) );
		Assert.IsNull( ParkResumeMods.MissingText( null, OpenTpwExtensions.Active ) );
		Assert.IsNull( ParkResumeMods.MissingText( saved, Resolve( new[] { Rides() }, Physics ) ) );
		Assert.AreEqual( "community.tpi-rides 1.0.0", ParkResumeMods.MissingText( saved, Resolve( Array.Empty<DiscoveredMod>(), Physics ) ) );
		Assert.AreEqual( "opentpw.physics-test 1.2.0, community.tpi-rides 1.0.0", ParkResumeMods.MissingText( saved, ModSet.Defaults( OfficialExtensions.All ) ) );
		Assert.AreEqual( "community.tpi-rides 1.0.0", ParkResumeMods.MissingText( saved, Resolve( new[] { Rides( "2.0.0" ) }, Physics ) ) );
	}

	[TestMethod]
	public void TheLoadListKeepsAnEntryAndSaysWhichModItRequires()
	{
		var strings = OriginalUiTests.FakeStrings();
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			StringAssert.Contains( SupplementaryStrings.Get( OpenTpwText.RequiresMod, language ), "{0}", language );
		Assert.AreEqual( "", FrontEndMenu.RequiresSuffix( strings, null ) );
		Assert.AreEqual( " (requires mod community.tpi-rides 1.0.0)", FrontEndMenu.RequiresSuffix( strings, "community.tpi-rides 1.0.0" ) );
		var entries = new[]
		{
			new ParkLoadEntry( "jungle", -1, false, "/a.json", "Plain" ),
			new ParkLoadEntry( "jungle", -1, false, "/b.json", "Modded", Requires: "community.tpi-rides 1.0.0" )
		};
		var screen = FrontEndMenu.CreateLoadScreen( new UiScreenStack(), strings, entries, _ => { } );
		var rows = ((UiScrollList)screen.Find( "parks" )!).Rows();
		Assert.AreEqual( "Plain", rows[0] );
		Assert.AreEqual( "Modded (requires mod community.tpi-rides 1.0.0)", rows[1], "listed, not hidden" );
	}

	// ---- Sharing and caching --------------------------------------------------------------------

	[TestMethod]
	public void ExportingIsRefusedForParksThatRecordMods()
	{
		Assert.IsNull( ParkSharing.ModsRefusal() );
		using ( OpenTpwExtensions.Use( Resolve( new[] { Textures() }, OfficialExtensions.Online ) ) )
			Assert.IsNull( ParkSharing.ModsRefusal(), "a presentation-only mod does not block sharing" );
		using ( OpenTpwExtensions.Use( Resolve( new[] { Rides() }, Quiet ) ) )
		{
			StringAssert.Contains( ParkSharing.ModsRefusal(), "community.tpi-rides 1.0.0" );
			var level = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			var exception = Assert.ThrowsException<InvalidOperationException>( () => ParkSharing.ExportLevel( level, "Park", "Me" ) );
			StringAssert.Contains( exception.Message, "cannot be exported" );
		}
	}

	[TestMethod]
	public void TheCatalogCacheKeyFollowsTheModSet()
	{
		var game = Path.Combine( root, "game" );
		var archive = Path.Combine( game, "levels", "jungle", "rides", "totem" );
		Directory.CreateDirectory( archive );
		File.WriteAllText( Path.Combine( archive, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\n" );
		File.WriteAllBytes( Path.Combine( archive, "totem.MD2" ), Array.Empty<byte>() );
		var previous = FileSystem;
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			FileSystem = new BaseFileSystem( game );
			var plain = ObjectCatalog.Load( "jungle" );
			Assert.AreSame( plain, ObjectCatalog.Load( "jungle" ), "the same set shares a catalog" );
			using ( OpenTpwExtensions.Use( OnlineExtensionGateTests.WithOnline( false ) ) )
				Assert.AreSame( plain, ObjectCatalog.Load( "jungle" ), "toggling only the online extension keeps the digest and the catalog" );
			ObjectCatalog first, second;
			using ( OpenTpwExtensions.Use( Resolve( new[] { Rides() } ) ) )
			{
				first = ObjectCatalog.Load( "jungle" );
				Assert.AreSame( first, ObjectCatalog.Load( "jungle" ) );
			}
			using ( OpenTpwExtensions.Use( Resolve( new[] { Rides( text: "changed" ) } ) ) )
				second = ObjectCatalog.Load( "jungle" );
			Assert.AreNotSame( plain, first, "an active mod gets its own catalog" );
			Assert.AreNotSame( first, second, "another mod digest gets another one" );
			Assert.AreSame( plain, ObjectCatalog.Load( "jungle" ) );
		}
		finally
		{
			FileSystem = previous;
			ObjectCatalog.BonusDataRoot = null;
		}
	}

	// ---- Determinism --------------------------------------------------------------------------------

	/// <summary>
	/// An empty mod set, an online-only toggle and a presentation-only mod leave the pinned digests unchanged: the fixed run's hash,
	/// the guest pathing of a cross park and the empty mod digest (the M4 gate and browser digests run these code paths in the smokes).
	/// </summary>
	[TestMethod]
	public void EmptyOnlineOnlyAndPresentationOnlyModSetsKeepThePinnedDigests()
	{
		var sets = new[]
		{
			ModSet.Defaults( OfficialExtensions.All ),
			OnlineExtensionGateTests.WithOnline( false ),
			OnlineExtensionGateTests.WithOnline( true ),
			Resolve( new[] { Textures() }, OfficialExtensions.Online ),
		};
		ulong? pathing = null;
		foreach ( var set in sets )
		{
			using var scope = OpenTpwExtensions.Use( set );
			Assert.IsNull( ParkResumeMods.Capture( set ), "no record, so the save bytes cannot change" );
			var park = DeterminismTests.RunFixedPark();
			Assert.AreEqual( DeterminismTests.FixedRunHash, park, $"0x{park:X16}" );
			var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
			for ( var tick = 0; tick < 120 * 60; tick++ )
				simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			pathing ??= simulation.ComputeStateHash();
			Assert.AreEqual( pathing, simulation.ComputeStateHash() );
		}
		Assert.AreEqual( "", sets[0].Digest );
		Assert.AreEqual( "", sets[1].Digest );
		Assert.AreNotEqual( "", sets[3].Digest, "the presentation mod is in the set digest (and so the catalog key), not in saves" );
	}
}
