using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The engine side of the <c>opentpw.cross-edition-objects</c> extension that does not need content roots (docs/MODS.md): the
/// extension itself, the reserved <c>Info.Id</c> ranges and the per-root sound lookup. The loading of another game's archives
/// arrives with the content roots.
/// </summary>
[TestClass]
public class CrossEditionObjectsTests
{
	private string root = null!;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-cross-" ).FullName;
	}

	[TestCleanup]
	public void Cleanup()
	{
		ContentSoundRoots.Clear();
		Directory.Delete( root, true );
	}

	private static byte[] Parse( string json ) => Encoding.UTF8.GetBytes( json );

	private static string Manifest( string id, string extra ) =>
		$$"""{ "schema": 1, "id": "{{id}}", "name": "Test {{id}}", "version": "1.0.0", "category": "community"{{extra}} }""";

	private static ModManifest Read( string id, string extra ) => ModManifest.Parse( Parse( Manifest( id, extra ) ) );

	private static void AssertInvalid( string extra, string expected )
	{
		var exception = Assert.ThrowsException<InvalidDataException>( () => Read( "community.tpi-rides", extra ) );
		StringAssert.Contains( exception.Message, expected );
	}

	// ---- The extension ------------------------------------------------------------------------

	[TestMethod]
	public void TheExtensionIsOffByDefaultAndMarkedAsChangingTheSimulation()
	{
		var extension = OfficialExtensions.CrossEditionObjects;
		Assert.AreEqual( "opentpw.cross-edition-objects", extension.Id );
		Assert.IsFalse( extension.DefaultEnabled, "nothing of another game loads unless the player turns it on" );
		Assert.IsTrue( extension.AffectsSimulation, "objects it brings change the park, so saves record it" );
		Assert.IsTrue( OfficialExtensions.All.Contains( extension ) );
		Assert.AreSame( extension, OfficialExtensions.Find( extension.Id ) );
		var defaults = ModSet.Defaults( OfficialExtensions.All );
		Assert.IsFalse( defaults.IsEnabled( extension.Id ) );
		Assert.IsTrue( defaults.IsEnabled( OfficialExtensions.Online.Id ), "the online extension keeps its default" );
		using ( OpenTpwExtensions.Use( defaults ) )
			Assert.IsFalse( FeatureGates.CrossEditionObjects );
		var enabled = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool> { [extension.Id] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		using ( OpenTpwExtensions.Use( enabled ) )
			Assert.IsTrue( FeatureGates.CrossEditionObjects );
		Assert.AreEqual( "", enabled.Digest, "an extension alone leaves the mod digest empty, like no mods" );
	}

	// ---- Info.Id ranges -----------------------------------------------------------------------

	[TestMethod]
	public void TheManifestReadsTheReservedRange()
	{
		var manifest = Read( "community.tpi-rides", """, "infoIds": { "first": 20000, "last": 20999 }""" );
		Assert.AreEqual( new InfoIdRange( 20000, 20999 ), manifest.InfoIds );
		Assert.IsTrue( manifest.InfoIds!.Value.Contains( 20999 ) );
		Assert.IsFalse( manifest.InfoIds!.Value.Contains( 21000 ) );
		Assert.IsNull( Read( "community.plain", "" ).InfoIds );
		Assert.AreEqual( "20000..20999", manifest.InfoIds.ToString() );
	}

	[TestMethod]
	public void TheRangeMustSitAboveEveryOriginalIdAndInsideSixteenBits()
	{
		// Theme Park World's ids reach 4605 and Theme Park Inc's 4500; a park save holds an attraction id in 16 bits.
		Assert.IsNotNull( CrossEditionObjects.RangeProblem( 4999, 5100 ) );
		Assert.IsNull( CrossEditionObjects.RangeProblem( 5000, 5100 ) );
		Assert.IsNull( CrossEditionObjects.RangeProblem( 65000, 65535 ) );
		Assert.IsNotNull( CrossEditionObjects.RangeProblem( 65000, 65536 ) );
		Assert.IsNotNull( CrossEditionObjects.RangeProblem( 6000, 5999 ) );
		Assert.IsNull( CrossEditionObjects.RangeProblem( 10000, 10000 + CrossEditionObjects.MaximumRangeSize - 1 ) );
		Assert.IsNotNull( CrossEditionObjects.RangeProblem( 10000, 10000 + CrossEditionObjects.MaximumRangeSize ) );
		AssertInvalid( """, "infoIds": { "first": 1100, "last": 1200 }""", "'infoIds' 1100..1200 must lie within 5000..65535" );
		AssertInvalid( """, "infoIds": { "first": 7000, "last": 6000 }""", "must not end before it starts" );
		AssertInvalid( """, "infoIds": { "first": 7000, "last": 70000 }""", "must lie within" );
		AssertInvalid( """, "infoIds": { "first": 7000, "last": 12000 }""", "at most 4096 ids" );
		AssertInvalid( """, "infoIds": { "first": 7000 }""", "needs 'first' and 'last'" );
		AssertInvalid( """, "infoIds": { "first": 7000, "last": 7100, "extra": 1 }""", "extra" );
	}

	[TestMethod]
	public void AModThatListsObjectsForTheExtensionMustReserveIds()
	{
		const string depends = """, "dependencies": [ { "id": "opentpw.cross-edition-objects" } ], "content": { "objects": "objects.json" }""";
		var without = Mod( "community.tpi-rides", depends );
		var refused = Resolve( new[] { without }, "community.tpi-rides", "opentpw.cross-edition-objects" );
		Assert.AreEqual( 0, refused.Mods.Count );
		StringAssert.Contains( refused.Refused.Single().Reason, "declares no 'infoIds' range" );
		var with = Mod( "community.tpi-rides-ranged", depends + """, "infoIds": { "first": 20000, "last": 20099 }""" );
		Assert.AreEqual( 1, Resolve( new[] { with }, "community.tpi-rides-ranged", "opentpw.cross-edition-objects" ).Mods.Count );
		// the rule is only for mods that use the extension: another mod's object list is the content roots' business
		var other = Mod( "community.other", """, "content": { "objects": "objects.json" }""" );
		Assert.AreEqual( 1, Resolve( new[] { other }, "community.other" ).Mods.Count );
		// the extension without any object list needs no range
		var input = Mod( "community.input", """, "dependencies": [ { "id": "opentpw.cross-edition-objects" } ]""" );
		Assert.AreEqual( 1, Resolve( new[] { input }, "community.input", "opentpw.cross-edition-objects" ).Mods.Count );
	}

	private string Mod( string id, string extra )
	{
		var directory = Path.Combine( root, id );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ), Manifest( id, extra ) );
		return directory;
	}

	private static ModSet Resolve( IEnumerable<string> directories, params string[] enabled ) =>
		ModSet.Resolve( directories.Select( directory => ModDiscovery.Discover( new[] { directory }, Array.Empty<string>(), null, null ).Single() ).ToList(),
			new ModSettings { Enabled = enabled.ToDictionary( id => id, _ => true ) }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	[TestMethod]
	public void ModsWithOverlappingRangesAreBothRefused()
	{
		var first = Mod( "community.first", """, "infoIds": { "first": 20000, "last": 20099 }""" );
		var second = Mod( "community.second", """, "infoIds": { "first": 20099, "last": 20199 }""" );
		var third = Mod( "community.third", """, "infoIds": { "first": 20200, "last": 20299 }""" );
		var set = Resolve( new[] { first, second, third }, "community.first", "community.second", "community.third" );
		CollectionAssert.AreEqual( new[] { "community.third" }, set.Mods.Select( mod => mod.Id ).ToArray(), "a third mod outside both ranges still runs" );
		Assert.AreEqual( 2, set.Refused.Count );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.first" ).Reason, "reserves the Info.Ids 20000..20099 like community.second (20099..20199)" );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.second" ).Reason, "like community.first" );
		var disjoint = Resolve( new[] { first, third }, "community.first", "community.third" );
		Assert.AreEqual( 2, disjoint.Mods.Count );
		Assert.AreEqual( 0, disjoint.Refused.Count );
	}

	[TestMethod]
	public void AModNeedingTheDisabledExtensionIsRefusedWithTheReason()
	{
		var directory = Mod( "community.tpi-rides", """, "dependencies": [ { "id": "opentpw.cross-edition-objects", "version": ">=1.0.0" } ]""" );
		var off = Resolve( new[] { directory }, "community.tpi-rides" );
		Assert.AreEqual( 0, off.Mods.Count );
		StringAssert.Contains( off.Refused.Single().Reason, "opentpw.cross-edition-objects" );
		StringAssert.Contains( off.Refused.Single().Reason, "disabled or refused" );
		var on = Resolve( new[] { directory }, "community.tpi-rides", "opentpw.cross-edition-objects" );
		Assert.AreEqual( 1, on.Mods.Count );
		Assert.IsTrue( on.IsEnabled( "opentpw.cross-edition-objects" ) );
		CollectionAssert.AreEqual( new[] { "opentpw.cross-edition-objects", "community.tpi-rides" }, on.Order.Where( id => id != OfficialExtensions.Online.Id ).ToArray(), "the extension loads before the mod that needs it" );
	}

	[TestMethod]
	public void ClaimsOutsideTheRangeOrAlreadyTakenAreRefusedOneByOne()
	{
		var range = new InfoIdRange( 20000, 20099 );
		var taken = new HashSet<int> { 1100, 20005 };
		var refusals = CrossEditionObjects.CheckClaims( range, new[]
		{
			("/levels/science/rides/a", 20000), ("/levels/science/rides/b", 1100), ("/levels/science/rides/c", 20005),
			("/levels/science/rides/d", 20000), ("/levels/science/rides/e", 20050),
		}, taken );
		Assert.AreEqual( 3, refusals.Count );
		Assert.AreEqual( "/levels/science/rides/b", refusals[0].Archive );
		StringAssert.Contains( refusals[0].Reason, "outside the mod's range 20000..20099", "the Theme Park Inc id 1100 is also a Theme Park World id" );
		StringAssert.Contains( refusals[1].Reason, "already used by another object" );
		StringAssert.Contains( refusals[2].Reason, "also claimed by /levels/science/rides/a" );
		Assert.AreEqual( 0, CrossEditionObjects.CheckClaims( range, new[] { ("/a", 20001), ("/b", 20002) }, new HashSet<int>() ).Count );
		var undeclared = CrossEditionObjects.CheckClaims( null, new[] { ("/a", 20001) }, new HashSet<int>() );
		StringAssert.Contains( undeclared.Single().Reason, "declares no 'infoIds' range" );
	}

	// ---- Sound roots --------------------------------------------------------------------------

	private static byte[] BankMap( params string[] banks )
	{
		var bytes = new List<byte>( new byte[24] );
		bytes.AddRange( BitConverter.GetBytes( (uint)banks.Length ) );
		foreach ( var _ in banks )
			bytes.AddRange( new byte[11] );
		foreach ( var name in banks )
		{
			var text = Encoding.Latin1.GetBytes( name );
			bytes.AddRange( BitConverter.GetBytes( (uint)text.Length ) );
			bytes.AddRange( text );
		}
		return bytes.ToArray();
	}

	private BaseFileSystem WriteCategory( string rootName, string directory, string category )
	{
		var folder = Path.Combine( root, rootName, directory.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) );
		Directory.CreateDirectory( folder );
		File.WriteAllBytes( Path.Combine( folder, $"{category}SFX.map" ), AudioTests.CreateCatalog() );
		File.WriteAllBytes( Path.Combine( folder, $"{category}BANK.map" ), BankMap( "Sound\\Ride" ) );
		return new BaseFileSystem( Path.Combine( root, rootName ) );
	}

	[TestMethod]
	public void ARegisteredRootRoutesEventsToItsOwnCategoriesWithoutFallback()
	{
		WriteCategory( "tpi", "/global/sound", "cat_rides" );
		var fileSystem = WriteCategory( "tpi", "/levels/science/Sound", "cat_rides" );
		var source = new RseSoundSource( ContentSoundRoots.Register( fileSystem ), "science" );
		// global rides (type 5) and the object's own theme's level rides (type 3)
		Assert.IsNotNull( GameAudio.FindRseEvent( 5, 31, source ) );
		Assert.AreEqual( "cat_rides", GameAudio.FindRseEvent( 5, 31, source )!.Value.Category.Name );
		Assert.IsNotNull( GameAudio.FindRseEvent( 3, 31, source ) );
		Assert.IsNull( GameAudio.FindRseEvent( 5, 99, source ), "an id the root's category lacks plays nothing" );
		// a theme the root has no level category for is silent: the running game's level category is never consulted
		Assert.IsNull( GameAudio.FindRseEvent( 3, 31, source with { Theme = "water" } ) );
		Assert.IsNull( GameAudio.FindRseEvent( 3, 31, source with { Theme = null } ) );
		// categories the root does not carry, and the types that are not sound categories
		Assert.IsNull( GameAudio.FindRseEvent( 4, 31, source ), "no cat_ambient in this root" );
		Assert.IsNull( GameAudio.FindRseEvent( 6, 31, source ) );
		foreach ( var type in new[] { 0, 1, 2, 10, 11 } )
			Assert.IsNull( GameAudio.FindRseEvent( type, 31, source ), $"type {type} routes to no category" );
		Assert.AreSame( source.Category( 5 ), source.Category( 5 ), "a category loads once per root" );
		// without a source the running game's categories answer (none are loaded in this test)
		Assert.IsNull( GameAudio.FindRseEvent( 5, 31 ) );
	}

	[TestMethod]
	public void OnlyEntriesOfARegisteredRootGetASoundSourceAndItsTheme()
	{
		var inc = WriteCategory( "tpi", "/global/sound", "cat_rides" );
		var own = new BaseFileSystem( Path.Combine( root, "tpw" ) );
		ObjectCatalogEntry Entry( BaseFileSystem fileSystem, string archive ) => new( "jungle", ObjectCategory.Ride, archive,
			new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1100\n", "synthetic.sam" ) } ), new ObjectShape( new[] { "*" } ), "/model.MD2", null, null,
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), fileSystem, null );
		Assert.IsNull( ContentSoundRoots.SourceOf( Entry( inc, "/levels/science/rides/x" ) ), "nothing registered: every entry keeps the running game's banks" );
		var root1 = ContentSoundRoots.Register( inc );
		Assert.AreSame( root1, ContentSoundRoots.Register( inc ) );
		var source = ContentSoundRoots.SourceOf( Entry( inc, "/levels/science/rides/x" ) );
		Assert.IsNotNull( source );
		Assert.AreSame( root1, source!.Value.Root );
		Assert.AreEqual( "science", source.Value.Theme );
		Assert.IsNull( ContentSoundRoots.SourceOf( Entry( own, "/levels/jungle/rides/x" ) ), "the game's own entries and official bonus archives are not routed" );
		Assert.AreEqual( "arabian", ContentSoundRoots.ThemeOf( "/Levels/arabian/Rides/x" ) );
		Assert.AreEqual( "water", ContentSoundRoots.ThemeOf( "levels\\water" ) );
		Assert.IsNull( ContentSoundRoots.ThemeOf( "/synthetic" ) );
		Assert.IsNull( ContentSoundRoots.ThemeOf( "/levels" ) );
		ContentSoundRoots.Clear();
		Assert.IsNull( ContentSoundRoots.SourceOf( Entry( inc, "/levels/science/rides/x" ) ) );
	}

	// ---- Original data (OPENTPW_TPI_PATH) -----------------------------------------------------

	[TestMethod]
	public void EveryThemeParkIncThemeLoadsItsOwnRideAndAmbientBanks()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path;
		var fileSystem = new BaseFileSystem( data );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		var soundRoot = ContentSoundRoots.Register( fileSystem );
		// Theme Park Inc's global cat_rides holds far more events than Theme Park World's 25, so ids must resolve in this root, not the running game's.
		var global = soundRoot.Category( 5, null );
		Assert.IsNotNull( global, "global cat_rides" );
		Assert.IsTrue( global!.Catalog.Events.Count > 100, $"{global.Catalog.Events.Count} global ride events" );
		foreach ( var theme in new[] { "arabian", "science", "water" } )
		{
			var rides = soundRoot.Category( 3, theme );
			Assert.IsTrue( rides?.Catalog.Events.Count > 0, $"{theme} level cat_rides" );
			Assert.IsNotNull( soundRoot.Category( 4, theme ), $"{theme} level cat_ambient" );
			for ( var bank = 1; bank <= rides!.BankNames.Count; bank++ )
				Assert.IsNotNull( rides.GetBank( bank ), $"{theme} cat_rides bank {bank} resolves in the Theme Park Inc root" );
		}
	}
}
