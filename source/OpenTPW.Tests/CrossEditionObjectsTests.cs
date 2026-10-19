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

	private static string Manifest( string id, string extra ) =>
		$$"""{ "schema": 1, "id": "{{id}}", "name": "Test {{id}}", "version": "1.0.0", "category": "community"{{extra}} }""";

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
	public void TheReservableIdsSitAboveEveryTableIdOfTheGame()
	{
		Assert.IsTrue( ObjectNameTable.InfoIds().Max() < CrossEditionObjects.FirstReservableId, "the object name table lists ids up to 4605" );
		Assert.IsTrue( PrototypeRide.InfoId < CrossEditionObjects.FirstReservableId );
	}

	[TestMethod]
	public void TheRangeMustSitAboveEveryOriginalIdAndInsideSixteenBits()
	{
		// Theme Park World's ids reach 4605 and Theme Park Inc's 4500; the original park saves hold a placed object's Info.Id in 16 bits.
		Assert.IsNotNull( CrossEditionObjects.RangeProblem( 4999, 5100 ) );
		Assert.IsNull( CrossEditionObjects.RangeProblem( 5000, 5100 ) );
		Assert.IsNull( CrossEditionObjects.RangeProblem( 65000, 65535 ) );
		Assert.IsNotNull( CrossEditionObjects.RangeProblem( 65000, 65536 ) );
		Assert.IsNotNull( CrossEditionObjects.RangeProblem( 6000, 5999 ) );
		Assert.IsNull( CrossEditionObjects.RangeProblem( 10000, 10000 + CrossEditionObjects.MaximumRangeSize - 1 ) );
		Assert.IsNotNull( CrossEditionObjects.RangeProblem( 10000, 10000 + CrossEditionObjects.MaximumRangeSize ) );
		Assert.IsNotNull( CrossEditionObjects.RangeProblem( 1, 1_000_000_000 ) );
		StringAssert.Contains( CrossEditionObjects.RangeProblem( 1100, 1200 )!, "5000..65535" );
		StringAssert.Contains( CrossEditionObjects.RangeProblem( 7000, 12000 )!, "at most 4096 ids" );
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
	public void TheEnabledExtensionIsRecordedInParkSavesAndRequiredToLoadThem()
	{
		var extension = OfficialExtensions.CrossEditionObjects;
		var off = ModSet.Defaults( OfficialExtensions.All );
		Assert.IsNull( ParkResumeMods.Capture( off ), "off by default: a park saves exactly as before" );
		var on = Resolve( Array.Empty<string>(), extension.Id );
		var saved = ParkResumeMods.Capture( on );
		Assert.AreEqual( new ParkModRecord( extension.Id, "1.0.0", ModSet.NoFilesSha256 ), saved!.Single() );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( saved, on ).Count );
		StringAssert.Contains( ParkResumeMods.Unmet( saved, off ).Single(), "needs opentpw.cross-edition-objects 1.0.0" );
		StringAssert.Contains( ParkResumeMods.Unmet( null, on ).Single(), "was saved without opentpw.cross-edition-objects" );
		// a mod that needs the extension is recorded after it, in load order
		var directory = Mod( "community.tpi-rides", """, "dependencies": [ { "id": "opentpw.cross-edition-objects" } ], "content": { "overrides": "data" }""" );
		Directory.CreateDirectory( Path.Combine( directory, "data" ) );
		File.WriteAllText( Path.Combine( directory, "data", "rides.sam" ), "x" );
		var both = Resolve( new[] { directory }, extension.Id, "community.tpi-rides" );
		CollectionAssert.AreEqual( new[] { extension.Id, "community.tpi-rides" }, ParkResumeMods.Capture( both )!.Select( record => record.Id ).ToArray() );
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
		CollectionAssert.AreEqual( new[] { "opentpw.cross-edition-objects", "community.tpi-rides" }, on.Order.Where( id => !OfficialExtensions.All.Any( extension => extension.DefaultEnabled && extension.Id == id ) ).ToArray(), "the extension loads before the mod that needs it" );
	}

	[TestMethod]
	public void TheObjectsFileOfAModThatUsesTheExtensionFollowsTheOneRule()
	{
		static string Json( int first, int last, int id ) =>
			$$"""{ "schema": 1, "idRange": { "first": {{first}}, "last": {{last}} }, "objects": [ { "archive": "levels/science/rides/x", "input": "tpi", "theme": "space", "category": "rides", "infoId": {{id}}, "names": { "English": "X" } } ] }""";
		static ModObjectsFile Read( string json ) => ModObjectsFile.Parse( Encoding.UTF8.GetBytes( json ), new[] { "tpi" } );
		Assert.AreEqual( 65535, Read( Json( 61440, 65535, 65535 ) ).LastId );
		Assert.AreEqual( 5000, Read( Json( 5000, 5000, 5000 ) ).FirstId );
		// the Theme Park Inc ids themselves (1100 is the Belly Bounce in Theme Park World), the old 10000..1e9 bounds and a missing range
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Read( Json( 1100, 1100, 1100 ) ) ).Message, "must lie within 5000..65535" );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Read( Json( 70000, 70000, 70000 ) ) ).Message, "must lie within 5000..65535" );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Read( Json( 10000, 14096, 10000 ) ) ).Message, "at most 4096 ids" );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Read( """{ "schema": 1, "objects": [] }""" ) ).Message, "'idRange' is required" );
		// an id outside the declared range, inside the allowed bounds
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Read( Json( 20000, 20099, 1100 ) ) ).Message, "outside the declared range" );
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

	[TestMethod]
	public void EveryThemeParkIncInfoIdSitsBelowTheReservableRange()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path;
		var fileSystem = new BaseFileSystem( data );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		var ids = new HashSet<int>();
		foreach ( var theme in fileSystem.GetDirectories( "/levels" ) )
			foreach ( var category in fileSystem.GetDirectories( fileSystem.GetRelativePath( fileSystem.GetAbsolutePath( theme ) ) ) )
				foreach ( var archive in fileSystem.GetDirectories( fileSystem.GetRelativePath( fileSystem.GetAbsolutePath( category ) ) ) )
					foreach ( var file in fileSystem.GetFiles( fileSystem.GetRelativePath( fileSystem.GetAbsolutePath( archive ) ) ).Where( file => file.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) ) )
						if ( ObjectSettingsFile.Load( fileSystem, fileSystem.GetRelativePath( fileSystem.GetAbsolutePath( file ) ) ).Values.TryGetValue( "Info.Id", out var text ) )
							ids.Add( int.Parse( text, System.Globalization.CultureInfo.InvariantCulture ) );
		Assert.IsTrue( ids.Count > 150, $"{ids.Count} distinct Theme Park Inc Info.Ids" );
		Assert.IsTrue( ids.Max() < CrossEditionObjects.FirstReservableId, $"the highest Theme Park Inc Info.Id is {ids.Max()}" );
	}
}
