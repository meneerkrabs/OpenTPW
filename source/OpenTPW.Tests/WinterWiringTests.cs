using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The winter extension wired into the game (docs/WINTER.md, phase W3): its registration, its shops in the theme catalogs while it is on, their bridges' terms, the used-only rule of a park save, the saves that hold items,
/// the Theme Park Inc gate and the wintry Decembers option's registration and recording. The shipped content is loaded through the game's own catalog with the extension's own root, without any original game data.
/// </summary>
// [EXT:winter-extension] OpenTPW's own extension; Theme Park World has neither the shops nor the items
[TestClass]
[DoNotParallelize]
public class WinterWiringTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "space", "fantasy" };
	private const string ModId = "opentpw.winter";
	private string root = null!;
	private BaseFileSystem? previousFileSystem;
	private IReadOnlyList<CatalogContentRoot>? previousRoots;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-winter-wiring-" ).FullName;
		previousFileSystem = FileSystem;
		var totem = Path.Combine( root, "game", "levels", "jungle", "rides", "totem" );
		Directory.CreateDirectory( totem );
		File.WriteAllText( Path.Combine( totem, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t100\n" );
		File.WriteAllBytes( Path.Combine( totem, "totem.MD2" ), Array.Empty<byte>() );
		FileSystem = new BaseFileSystem( Path.Combine( root, "game" ) );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		previousRoots = ObjectCatalog.ContentRoots;
		var objects = ModObjectsFile.Load( Path.Combine( Folder, "objects.json" ), Array.Empty<string>() );
		ObjectCatalog.SetContentRoots( new[] { new CatalogContentRoot( ContentRoot.Mod( ModId ), Folder, objects.Objects ) } );
	}

	[TestCleanup]
	public void Cleanup()
	{
		ObjectCatalog.SetContentRoots( previousRoots! );
		FileSystem = previousFileSystem!;
		Directory.Delete( root, true );
	}

	private static string? folder;
	private static string Folder => folder ??= RepositoryFolder();

	private static string RepositoryFolder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var candidate = Path.Combine( directory.FullName, "content", "winter" );
			if ( File.Exists( Path.Combine( candidate, "objects.json" ) ) && Directory.Exists( Path.Combine( directory.FullName, "tools", "winter" ) ) )
				return candidate;
		}
		throw new FileNotFoundException( "content/winter not found above the test binaries." );
	}

	private static ModSet WinterSet( bool enabled ) => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
		new ModSettings { Enabled = new Dictionary<string, bool> { [ModId] = enabled } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	// ---- Registration ----------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheWinterExtensionIsRegisteredOnByDefaultAndRecordedLikeTheUmbrellas()
	{
		var extension = OfficialExtensions.All.Single( candidate => candidate.Id == ModId );
		Assert.AreSame( OfficialExtensions.Winter, extension );
		Assert.AreEqual( "1.0.0", extension.Version.ToString() );
		Assert.IsTrue( extension.DefaultEnabled && extension.AffectsSimulation && extension.PlayerSelectable, "on by default, a simulation rule, selectable (decision of October 18, 2026)" );
		Assert.IsTrue( ParkResumeMods.UsedOnlyExtensionIds.Contains( ModId ), "a save records it only while the park uses it (the pools and umbrellas rule)" );
		using var scope = OpenTpwExtensions.Use( ModSet.Defaults( OfficialExtensions.All ) );
		Assert.IsTrue( FeatureGates.Winter );
		Assert.IsFalse( FeatureGates.WinterWeather, "the wintry Decembers option is off by default" );
	}

	[TestMethod]
	public void TheWintryDecembersOptionIsOffByDefaultAlwaysRecordedWhileOnAndOnlyInAThemeParkWorldRun()
	{
		var option = OfficialExtensions.WinterWeather;
		Assert.AreEqual( "opentpw.winter-weather", option.Id );
		Assert.AreEqual( "1.0.0", option.Version.ToString() );
		Assert.IsTrue( !option.DefaultEnabled && option.AffectsSimulation && option.PlayerSelectable, "off by default, a simulation rule, selectable" );
		Assert.IsFalse( ParkResumeMods.UsedOnlyExtensionIds.Contains( option.Id ), "it changes every park of two themes, so a save always records it while it is on" );
		var on = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool> { [option.Id] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		using ( OpenTpwExtensions.Use( on ) )
		{
			Assert.IsTrue( FeatureGates.WinterWeather );
			var recorded = ParkResumeMods.Capture( on, ParkResumeMods.InUse( new ParkCellMap( 20, 10 ) ) );
			CollectionAssert.AreEqual( new[] { option.Id }, recorded!.Select( record => record.Id ).ToArray(), "recorded in a park that has no winter shop at all" );
		}
		var edition = GameEditions.Current;
		try
		{
			using var scope = OpenTpwExtensions.Use( on );
			GameEditions.Select( GameEdition.ThemeParkInc );
			Assert.IsFalse( FeatureGates.WinterWeather, "a Theme Park Inc run has no such weather" );
			Assert.IsFalse( ParkResumeMods.RecordableMods( OpenTpwExtensions.Active ).Any( mod => mod.Id == option.Id ), "and the option leaves no trace in what it records" );
		}
		finally
		{
			GameEditions.Select( edition );
		}
	}

	// ---- The catalogs ----------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheWinterShopsAreListedInEveryThemeCatalogWhileTheExtensionIsOnAndOnlyThen()
	{
		WinterContent.DirectoryOverride = Folder;
		try
		{
			foreach ( var enabled in new[] { true, false } )
			{
				var set = WinterSet( enabled );
				using var scope = OpenTpwExtensions.Use( set );
				using var roots = ModContentRoots.Use( set, null );
				foreach ( var theme in Themes )
				{
					var shops = ObjectCatalog.Load( theme ).Entries.Where( entry => entry.ModId == ModId ).OrderBy( entry => entry.InfoId ).ToArray();
					if ( !enabled )
					{
						Assert.AreEqual( 0, shops.Length, $"{theme}: the extension is off, so the catalog lists no winter shop (and is not the cached catalog of the run with it on)" );
						continue;
					}
					Assert.AreEqual( 2, shops.Length, theme );
					var index = Array.IndexOf( Themes, theme );
					CollectionAssert.AreEqual( new[] { 64010 + 2 * index, 64011 + 2 * index }, shops.Select( shop => shop.InfoId ).ToArray() );
					CollectionAssert.AreEqual( new[] { 4, 0 }, shops.Select( shop => shop.Settings.GetInt( "UsageInfo.AppearanceEffect" ) ).ToArray(), "the kiosk's effect 4, the stall's none" );
				}
			}
		}
		finally
		{
			WinterContent.DirectoryOverride = null;
		}
	}

	[TestMethod]
	public void TheGoldenTicketForEverythingBuiltDoesNotAskForTheUsedOnlyExtensionsObjects()
	{
		// The original asks for every shipped buyable object; the objects of the used-only extensions (winter, umbrellas, pools) are catalogue entries of "research": false files, so a park that never places one wins the ticket the
		// same with the extensions on or off ([EXT:mods], EconomyObjectInfo.NotResearched).
		var content = Path.GetDirectoryName( Folder )!;
		PoolContent.DirectoryOverride = Path.Combine( content, "pools" );
		UmbrellaContent.DirectoryOverride = Path.Combine( content, "umbrellas" );
		WinterContent.DirectoryOverride = Folder;
		try
		{
			ParkEconomy Park( bool extensions )
			{
				var set = extensions ? ModSet.Defaults( OfficialExtensions.All ) : ModSet.Resolve( Array.Empty<DiscoveredMod>(),
					new ModSettings { Enabled = new Dictionary<string, bool> { [ModId] = false, [OfficialExtensions.Umbrellas.Id] = false, [OfficialExtensions.Pools.Id] = false } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
				using var roots = ModContentRoots.Use( set, FileSystem );
				return new ParkEconomy( EconomyTestData.Settings(), EconomyObjectCatalog.Load( "jungle", false ), ParkGameMode.FullSimulation, 7 );
			}
			foreach ( var extensions in new[] { false, true } )
			{
				var park = Park( extensions );
				var extra = park.Catalog.Objects.Where( info => info.InfoId >= 64000 ).ToArray();
				Assert.AreEqual( extensions, extra.Any( info => info.InfoId is >= 64010 and <= 64499 ), "the winter shops are in the catalogue with the extension on only" );
				Assert.AreEqual( extensions, extra.Any( info => info.InfoId is >= 64500 and <= 64999 ), "the brolly shop, with the umbrellas on only" );
				Assert.AreEqual( extensions, extra.Any( info => info.InfoId >= 65000 ), "the pool objects, with the pools on only" );
				Assert.IsTrue( extra.All( info => info.NotResearched ), "every extension object is available from the start and no research item" );
				park.Research.Restore( park.Research.Items.Select( item => (item.InfoId, item.Level) ), Array.Empty<((int InfoId, int Level), long)>(), park.Research.Effort );
				foreach ( var info in park.Catalog.Objects.Where( info => info.IsBuyable && !info.NotResearched ) )
					park.Counters.Add( ParkCounters.Built( info.InfoId ), 1 );
				Assert.IsTrue( park.Catalog.Objects.Any( info => info.IsBuyable && !info.NotResearched && info.InfoId < 64000 ), "the fixture holds an ordinary object" );
				var won = park.Objectives.CheckGoldenTickets( park.Counters, park.GuestStatistics, park.Research, 0, Array.Empty<long>() );
				CollectionAssert.Contains( won.ToList(), GoldenTicketKind.AllResearchedAndBuilt, $"every shipped object is built, extensions {(extensions ? "on" : "off")}: the winter shops, brollies and pool objects nobody built do not hold the ticket back" );
			}
		}
		finally
		{
			PoolContent.DirectoryOverride = null;
			UmbrellaContent.DirectoryOverride = null;
			WinterContent.DirectoryOverride = null;
		}
	}

	[TestMethod]
	public void AThemeParkIncRunHasNoWinterShopEvenWithTheExtensionOn()
	{
		WinterContent.DirectoryOverride = Folder;
		var edition = GameEditions.Current;
		try
		{
			using var scope = OpenTpwExtensions.Use( WinterSet( true ) );
			Assert.IsNotNull( WinterContent.CatalogRoot( OpenTpwExtensions.Active ) );
			GameEditions.Select( GameEdition.ThemeParkInc );
			Assert.IsFalse( FeatureGates.Winter, "Theme Park Inc has its own shops and no weather of this kind" );
			Assert.IsNull( WinterContent.CatalogRoot( OpenTpwExtensions.Active ), "no winter shop in a Theme Park Inc catalog" );
		}
		finally
		{
			GameEditions.Select( edition );
			WinterContent.DirectoryOverride = null;
		}
	}

	[TestMethod]
	public void AWinterShopsBridgeCarriesItsWeatherAndSeasonPercentagesAndNoOtherObjectDoes()
	{
		using var scope = OpenTpwExtensions.Use( WinterSet( true ) );
		var catalog = ObjectCatalog.Load( "jungle" );
		var kiosk = catalog.Get( 64010 );
		var stall = catalog.Get( 64011 );
		var kioskBridge = OriginalObjectRuntime.CreateVisitorBridge( kiosk, 7 );
		Assert.IsTrue( ((IRideVisitorBridge)kioskBridge).IsWinterKiosk );
		Assert.AreEqual( new WinterShopTerms( 0, 0, 200, 150, 60, 20, 60 ), kioskBridge.WinterTerms, "the kiosk's .sam: snow 200, winter 150, spring and autumn 60, summer 20 (it sells all year)" );
		Assert.AreEqual( WinterRules.KioskAppearanceEffect, kioskBridge.AppearanceEffect );
		var stallBridge = OriginalObjectRuntime.CreateVisitorBridge( stall, 8 );
		Assert.IsFalse( ((IRideVisitorBridge)stallBridge).IsWinterKiosk );
		Assert.AreEqual( new WinterShopTerms( 130, 130, 250, 150, 60, 20, 60 ), stallBridge.WinterTerms, "the stall's .sam" );
		// the stall keeps Ice's mechanics: SpecialIngredient 3 ([EXT:winter-spice-label])
		Assert.AreEqual( 3, stallBridge.SpecialIngredient );
		var totem = catalog.Entries.First( entry => entry.ModId == null );
		Assert.IsNull( OriginalObjectRuntime.CreateVisitorBridge( totem, 9 ).WinterTerms, "an original object keeps the game's score: the keys are read for the extension's objects only" );
		Assert.IsNull( OriginalObjectRuntime.CreateVisitorBridge( totem, 9 ).UmbrellaTerms );
	}

	[TestMethod]
	public void AWinterShopBridgeHasNoTermsWhileTheExtensionIsOff()
	{
		var kiosk = ObjectCatalog.Load( "jungle" ).Get( 64010 );
		using var scope = OpenTpwExtensions.Use( WinterSet( false ) );
		Assert.IsNull( OriginalObjectRuntime.CreateVisitorBridge( kiosk, 7 ).WinterTerms, "the gate is off: the object, if a save still holds one, scores as the game does" );
	}

	[TestMethod]
	public void TheStallIsADrinksShopForTheChallengesAndTheKioskIsAGiftShop()
	{
		using var scope = OpenTpwExtensions.Use( WinterSet( true ) );
		var catalog = EconomyObjectCatalog.Load( "jungle", false );
		var stall = catalog[64011];
		var kiosk = catalog[64010];
		Assert.AreEqual( (4, 3), (stall.ShopType, stall.SpecialIngredient), "a drinks shop with Ice's ingredient" );
		Assert.AreEqual( ParkCounters.Sold( 4, 3 ), ParkCounters.Sold( stall.ShopType, stall.SpecialIngredient ), "a sale counts for the drinks challenge as any drinks shop's does" );
		Assert.AreEqual( 1, kiosk.ShopType, "the kiosk is a gift shop's kind: not 5, which the challenges would count as costumes" );
		Assert.AreNotEqual( ParkCounters.Sold( 5, 0 ), ParkCounters.Sold( kiosk.ShopType, kiosk.SpecialIngredient ) );
	}

	// ---- Saves -------------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void ASaveHoldsTheWinterShopsAssetKeysOfThePlacedShopsOnly()
	{
		var kiosk = ObjectCatalog.Load( "jungle" ).Get( 64010 );
		var manifest = new Dictionary<string, string>
		{
			["data:/levels/jungle/rides/totem/totem.sam"] = new string( 'a', 64 ),
			["mod:opentpw.winter:/objects/jungle/kiosk/Kiosk.sam"] = new string( 'b', 64 ),
			["mod:opentpw.winter:/objects/hallow/gluehwein/Gluehwein.sam"] = new string( 'e', 64 ),
			["mod:opentpw.umbrellas:/objects/jungle/brolly/Brolly.sam"] = new string( 'f', 64 )
		};
		var none = ParkResumeMods.AssetsForUse( manifest, Array.Empty<ObjectCatalogEntry>() );
		CollectionAssert.AreEquivalent( new[] { "data:/levels/jungle/rides/totem/totem.sam" }, none.Keys.ToArray(), "no extension object placed: none of the extensions' files" );
		var placed = ParkResumeMods.AssetsForUse( manifest, new[] { kiosk } );
		CollectionAssert.AreEquivalent( new[] { "data:/levels/jungle/rides/totem/totem.sam", "mod:opentpw.winter:/objects/jungle/kiosk/Kiosk.sam" }, placed.Keys.ToArray(), "the placed shop's files only" );
		Assert.IsTrue( ParkResumeMods.IsWinterObject( kiosk ) && !ParkResumeMods.IsUmbrellasObject( kiosk ) && !ParkResumeMods.IsPoolsObject( kiosk ) );
		Assert.IsFalse( ParkResumeMods.IsWinterObject( ObjectCatalog.Load( "jungle" ).Entries.First( item => item.ModId == null ) ) );
		StringAssert.Contains( ParkResumeMods.ShapeProblem( null, placed )!, "mod opentpw.winter, which the save does not record" );
		Assert.IsNull( ParkResumeMods.ShapeProblem( new[] { new ParkModRecord( ModId, "1.0.0", ModSet.NoFilesSha256 ) }, placed ) );
	}

	[TestMethod]
	public void AParkUsesTheExtensionWithAWinterShopOrAnItemAndOtherwiseSavesAsIfItWereOff()
	{
		var kiosk = ObjectCatalog.Load( "jungle" ).Get( 64010 );
		var ordinary = ObjectCatalog.Load( "jungle" ).Entries.First( item => item.ModId == null );
		var cells = new ParkCellMap( 20, 10 );
		CollectionAssert.AreEqual( Array.Empty<string>(), ParkResumeMods.InUse( cells ).ToArray() );
		CollectionAssert.AreEqual( new[] { ModId }, ParkResumeMods.InUse( cells, winter: true ).ToArray() );
		var rig = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = rig.SpawnInPark( 10, 5 );
		Assert.IsFalse( ParkResumeMods.WinterInUse( new[] { ordinary }, rig ), "an ordinary object and a guest without an item: not in use" );
		Assert.IsTrue( ParkResumeMods.WinterInUse( new[] { ordinary, kiosk }, rig ), "a winter kiosk" );
		guest.WarmItems = WinterRules.Hat;
		Assert.IsTrue( ParkResumeMods.WinterInUse( Array.Empty<ObjectCatalogEntry>(), rig ), "a guest with an item, the kiosk long sold" );
		guest.WarmItems = 0;
		guest.WarmUntilDay = 5;
		Assert.IsTrue( ParkResumeMods.WinterInUse( Array.Empty<ObjectCatalogEntry>(), rig ), "a guest still warmed by a glühwein" );
		Assert.IsFalse( ParkResumeMods.WinterInUse( Array.Empty<ObjectCatalogEntry>(), null ) );
		var plain = ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() );
		foreach ( var enabled in new[] { true, false } )
		{
			var set = WinterSet( enabled );
			using var scope = OpenTpwExtensions.Use( set );
			var unused = ParkResumeMods.Capture( set, ParkResumeMods.InUse( cells ) );
			Assert.IsNull( unused, $"extension {enabled}: nothing to record in a park that does not use it" );
			Assert.IsTrue( ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() with { Mods = unused } ).SequenceEqual( plain ), $"extension {enabled}: identical bytes" );
			var used = ParkResumeMods.Capture( set, ParkResumeMods.InUse( cells, winter: true ) );
			if ( enabled )
				Assert.AreEqual( ModId, used!.Single().Id );
			else
				Assert.IsNull( used, "an extension that is off records nothing" );
		}
	}

	[TestMethod]
	public void ASaveWithAWarmItemRecordsTheExtensionRoundTripsAndRefusesToLoadWithItOff()
	{
		var on = WinterSet( true );
		var off = WinterSet( false );
		var envelope = ParkResumeSaveFileTests.Envelope();
		Assert.IsTrue( envelope.Guests.Guests.Length > 0, "the envelope holds guests" );
		var wearing = envelope with { Guests = envelope.Guests with { Guests = envelope.Guests.Guests.Select( ( guest, index ) => index == 0 ? guest with { WarmItems = WinterRules.Earmuffs | WinterRules.Mittens } : guest ).ToArray() } };
		// a save with an item must record the extension: it cannot hide the state from the mod check
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( wearing ) ).Message, "has a warm item but the save does not record opentpw.winter" );
		var warmed = envelope with { Guests = envelope.Guests with { Guests = envelope.Guests.Guests.Select( ( guest, index ) => index == 0 ? guest with { WarmUntilDay = 9 } : guest ).ToArray() } };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( warmed ) ).Message, "has a warm item but the save does not record opentpw.winter", "warmth counts as winter state too" );
		var recorded = wearing with { Mods = ParkResumeMods.Capture( on, new HashSet<string> { ModId } ) };
		ParkResumeSaveFile.ValidateShape( recorded );
		var bytes = ParkResumeSaveFile.Serialize( recorded );
		StringAssert.Contains( System.Text.Encoding.UTF8.GetString( bytes ), "opentpw.winter" );
		StringAssert.Contains( System.Text.Encoding.UTF8.GetString( bytes ), "\"WarmItems\":6" );
		var loaded = ParkResumeSaveFile.Deserialize( bytes );
		Assert.IsTrue( ParkResumeSaveFile.Serialize( loaded ).SequenceEqual( bytes ), "byte-stable" );
		Assert.AreEqual( WinterRules.Earmuffs | WinterRules.Mittens, loaded.Guests.Guests[0].WarmItems );
		ParkResumeMods.Require( loaded.Mods, on );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( loaded.Mods, off ) ).Message, "needs opentpw.winter 1.0.0" );
		var other = new[] { new ParkModRecord( ModId, "1.1.0", ModSet.NoFilesSha256 ) };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( other, on ) ).Message, "needs opentpw.winter 1.1.0, not 1.0.0" );
		ParkResumeMods.Require( null, on );
	}
}
