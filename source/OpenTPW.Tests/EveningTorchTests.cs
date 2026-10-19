using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The evening extension's torches (docs/EVENING.md, "Torches"): <c>content/evening</c> holds a torch per theme, made by <c>tools/evening/build-content.sh</c>. These tests load the committed files through the game's own pipelines
/// (the object catalog, the model reader, the ride script VM, the lamp table) with a content root made here, without any original game data, and check the user's decisions of October 19, 2026: part of the evening extension and
/// recorded only by a park that holds a torch, light and mood only, a themed variant per theme, cheap and available from the start.
/// </summary>
// [EXT:evening-torches] user-approved 2026-10-19: OpenTPW's own shipped content; neither Theme Park World nor Theme Park Inc ships anything of it
[TestClass]
[DoNotParallelize]
public class EveningTorchTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "space", "fantasy" };
	private static readonly string[] EnglishNames = { "Tiki Torch", "Pumpkin Torch", "Plasma Torch", "Candy Torch" };
	private const string ModId = "opentpw.evening";
	private string root = null!;
	private BaseFileSystem? previousFileSystem;
	private IReadOnlyList<CatalogContentRoot>? previousRoots;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-torches-" ).FullName;
		previousFileSystem = FileSystem;
		// a stand-in game folder: the catalog needs a file system, the torches' data comes from the extension's own content
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

	private static string? repository;
	private static string Repository => repository ??= RepositoryFolder();
	private static string Folder => Path.Combine( Repository, "content", "evening" );

	private static string RepositoryFolder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			if ( File.Exists( Path.Combine( directory.FullName, "content", "evening", "objects.json" ) ) && Directory.Exists( Path.Combine( directory.FullName, "tools", "evening" ) ) )
				return directory.FullName;
		}
		throw new FileNotFoundException( "content/evening not found above the test binaries." );
	}

	private static ModSet EveningSet( bool enabled ) => EveningTests.Set( enabled );

	// ---- The torches ----------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheTorchesHaveTheirOwnReservedIdsBelowTheOtherContentAndAreAvailableFromTheStart()
	{
		var file = ModObjectsFile.Load( Path.Combine( Folder, "objects.json" ), Array.Empty<string>() );
		Assert.AreEqual( (EveningContent.FirstReservedId, EveningContent.LastReservedId), (file.FirstId, file.LastId) );
		Assert.AreEqual( (63900, 63999), (file.FirstId, file.LastId), "below the example lamp mod's 64000..64009, the winter extension's 64010..64499, the umbrellas' 64500.. and the pools' 65000..; above the Theme Park Inc rides' 40000..40499" );
		var lamp = ModObjectsFile.Load( Path.Combine( Repository, "content", "mods", "opentpw.example.lamp-lights", "objects.json" ), Array.Empty<string>() );
		Assert.IsTrue( file.LastId < lamp.FirstId && file.LastId < WinterContent.FirstReservedId && file.LastId < UmbrellaContent.FirstReservedId && file.LastId < PoolContent.FirstReservedId );
		Assert.IsTrue( file.FirstId > 40499 && file.FirstId >= CrossEditionObjects.FirstReservableId );
		// no other shipped content (the extensions' folders, the mods) reserves an id of the range
		var others = Directory.GetFiles( Path.Combine( Repository, "content" ), "objects.json", SearchOption.AllDirectories ).Select( Path.GetFullPath ).Where( path => path != Path.GetFullPath( Path.Combine( Folder, "objects.json" ) ) ).ToArray();
		Assert.IsTrue( others.Length >= 5, "the pools, the umbrellas, the winter extension and the mods are found" );
		foreach ( var path in others )
		{
			using var other = System.Text.Json.JsonDocument.Parse( File.ReadAllText( path ) );
			var range = other.RootElement.GetProperty( "idRange" );
			var (first, last) = (range.GetProperty( "first" ).GetInt32(), range.GetProperty( "last" ).GetInt32());
			Assert.IsTrue( last < file.FirstId || first > file.LastId, $"{path} reserves {first}..{last}, which overlaps the torches' range" );
		}
		CollectionAssert.AreEqual( Themes, file.Objects.Select( item => item.Theme ).ToArray() );
		CollectionAssert.AreEqual( new[] { 63900, 63901, 63902, 63903 }, file.Objects.Select( item => item.InfoId ).ToArray() );
		Assert.IsTrue( file.Objects.All( item => item.Category == ObjectCategory.Feature && item.Input == null && !item.Research ), "features, our own files, no research item (\"research\": false, user-approved 2026-10-19)" );
		CollectionAssert.AreEqual( EnglishNames, file.Objects.Select( item => item.Names["English"] ).ToArray(), "a themed variant per theme" );
		var languages = new[] { "English", "Danish", "Dutch", "French", "German", "Korean", "Polish", "Spanish", "Swedish", "Italian", "Hebrew" };
		foreach ( var item in file.Objects )
		{
			CollectionAssert.AreEquivalent( languages, item.Names.Keys.ToArray(), $"{item.Theme}: a buy-window name in each of the eleven interface languages" );
			Assert.IsTrue( item.Names.Values.All( name => name.Length is > 0 and <= 24 ), $"{item.Theme}: short names fit the buy window" );
		}
		Assert.AreEqual( 4, file.Objects.Select( item => item.Names["Dutch"] ).Distinct().Count(), "each theme's torch has its own name" );
	}

	[TestMethod]
	public void EveryThemeListsItsTorchAsACheapFeatureThatGivesNothingToTheGuests()
	{
		for ( var index = 0; index < Themes.Length; index++ )
		{
			var catalog = ObjectCatalog.Load( Themes[index] );
			var entry = catalog.Get( 63900 + index );
			Assert.AreEqual( ObjectCategory.Feature, entry.Category, Themes[index] );
			Assert.AreEqual( ModId, entry.ModId );
			Assert.AreEqual( $"mod:{ModId}:", entry.Root.KeyPrefix, "a park save records and checks its files like a mod's" );
			Assert.AreEqual( EnglishNames[index], entry.DisplayName );
			Assert.AreEqual( "torch", entry.ArchiveName );
			CollectionAssert.AreEqual( new[] { "*" }, entry.Shape.Rows.ToArray(), "one cell" );
			Assert.IsTrue( !entry.IsChoosable && !entry.HasQueue && entry.WhichUIType == 3 && entry.IsBuildable, "a feature of the Features tab that no guest chooses" );
			Assert.AreEqual( 1, catalog.Entries.Count( item => item.ModId == ModId ), "one torch per theme" );
			Assert.IsNull( catalog.Find( 63900 + ( index + 1 ) % 4 ), "another theme's torch is not in this catalog" );
			// "Goedkoop, als een bankje" (user-approved 2026-10-19): Theme Park World has no bench; 50 is its cheapest shipped scenery (space's Small Bush), no upkeep, no research
			Assert.AreEqual( 50, entry.BuildCost );
			var level0 = entry.Economy.Upgrades.Single( level => level.Level == 0 );
			Assert.AreEqual( (0, 0), (level0.GetInt( "CostOfResearch" ), level0.GetInt( "RunningCost" )) );
			Assert.AreEqual( 0, entry.Settings.GetInt( "Research.Group" ) );
			// "Alleen licht en sfeer" (user-approved 2026-10-19): no attraction value and no new-attraction bonus, so nothing the guests or the arrivals read
			Assert.AreEqual( 0, entry.Settings.GetInt( "Info.AttractionValue", -1 ) );
			Assert.IsTrue( Enumerable.Range( 0, 3 ).All( bracket => entry.Settings.GetInt( $"Attraction[{bracket}].NewBonus", -1 ) == 0 ) );
			Assert.IsNotNull( entry.ScriptPath );
		}
	}

	[TestMethod]
	public void EachTorchsModelHasABodyAndASelfLitFlameItsTexturesAndAnIdleScript()
	{
		for ( var index = 0; index < Themes.Length; index++ )
		{
			var theme = Themes[index];
			var entry = ObjectCatalog.Load( theme ).Get( 63900 + index );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			Assert.AreEqual( ModelFileKind.Geometry, model.Kind );
			var torch = model.Nodes.Single( node => node.Name == "torch" );
			var flame = model.Nodes.Single( node => node.Name == "flame" );
			Assert.IsTrue( torch.MeshIndex >= 0 && flame.MeshIndex >= 0, $"{theme}: two meshes" );
			Assert.IsTrue( model.Meshes[flame.MeshIndex].Materials.All( material => ( material.Pad & ModelBuilder.SelfLitMaterialFlag ) != 0 ), $"{theme}: the flame draws in its own colours, by day and at night" );
			Assert.IsTrue( model.Meshes[torch.MeshIndex].Materials.Any( material => ( material.Pad & ModelBuilder.SelfLitMaterialFlag ) == 0 ), $"{theme}: the body is lit like any object" );
			// the flame's node stands at its base, so it scales about the base (the flicker) and the lamp table lights the middle of its mesh
			var flameBase = WorldOf( model, flame.Index ).Translation;
			Assert.IsTrue( flameBase.X is > 4.5f and < 5.5f && flameBase.Z is > 4.5f and < 5.5f && flameBase.Y is > 11f and < 14f, $"{theme}: the flame's node at {flameBase}" );
			var textures = new HashSet<string>();
			foreach ( var mesh in model.Meshes )
			{
				Assert.IsNull( ObjectAssets.GetUnrenderableReason( mesh ), $"{theme}: {mesh.Name}" );
				Assert.IsTrue( ObjectAssets.ConvertMesh( mesh ).Length > 0 );
				foreach ( var material in mesh.Materials )
					textures.Add( material.Name );
			}
			Assert.IsTrue( textures.Count >= 5 && textures.All( name => name.StartsWith( "tt_", StringComparison.Ordinal ) ), $"{theme}: {string.Join( ", ", textures )}" );
			foreach ( var name in textures )
			{
				var texture = ObjectAssets.ResolveTexture( entry, name );
				Assert.IsNotNull( texture, $"{theme}: {name}" );
				var (data, width, height) = Texture.DecodeImage( texture!.Value.FileSystem, texture.Value.Path );
				Assert.IsTrue( width == 64 && height == 64 && data.Length == width * height * 4, $"{theme}: {name} is {width} x {height}" );
			}
			// one cell: the footing within the cell, the whole torch a little taller than a guest
			var composed = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), WorldOf( model, mesh.NodeIndex ) ) ) ).ToArray();
			Assert.IsTrue( composed.All( p => p.X >= -0.01f && p.X <= 10.01f && p.Z >= -0.01f && p.Z <= 10.01f && p.Y >= -0.01f ), $"{theme}: the torch stands on its cell" );
			Assert.IsTrue( composed.Max( p => p.Y ) is > 14f and < 18f, $"{theme}: {composed.Max( p => p.Y ):F1} units tall" );
			Assert.AreEqual( 0, RideHeadSpace.Create( model, seed: 1 )?.Count ?? 0, "no head records" );

			using var scriptStream = entry.FileSystem.OpenRead( entry.ScriptPath! );
			var vm = new RideVM( new RideScriptFile( scriptStream ), new RideVMOptions { Effects = new NoEffects(), Seed = 1 } );
			for ( var step = 0; step < 1200; step++ )
			{
				vm.Advance( 1.0 / 60 );
				Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
			}
			Assert.AreEqual( 0, vm[RideVariables.VAR_LETMEOFF], "nobody rides a torch" );
		}
	}

	private sealed class NoEffects : IRideScriptEffects
	{
		public int Perform( RideEffectCall call ) => 0;
	}

	private static System.Numerics.Matrix4x4 WorldOf( ModelFile model, int node )
	{
		var world = System.Numerics.Matrix4x4.Identity;
		for ( var index = node; index >= 0; index = model.Nodes[index].ParentIndex )
			world *= model.Nodes[index].Transform;
		return world;
	}

	// ---- The extension -----------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheTorchesAreListedInEveryThemeCatalogWhileTheExtensionIsOnAndOnlyThen()
	{
		EveningContent.DirectoryOverride = Folder;
		try
		{
			foreach ( var enabled in new[] { true, false } )
			{
				var set = EveningSet( enabled );
				using var scope = OpenTpwExtensions.Use( set );
				using var roots = ModContentRoots.Use( set, null );
				foreach ( var theme in Themes )
				{
					var torches = ObjectCatalog.Load( theme ).Entries.Where( entry => entry.ModId == ModId ).ToArray();
					if ( !enabled )
					{
						Assert.AreEqual( 0, torches.Length, $"{theme}: the extension is off, so the catalog lists no torch" );
						continue;
					}
					Assert.AreEqual( 1, torches.Length, theme );
					Assert.AreEqual( 63900 + Array.IndexOf( Themes, theme ), torches[0].InfoId );
					Assert.AreEqual( 50, torches[0].BuildCost );
				}
			}
		}
		finally
		{
			EveningContent.DirectoryOverride = null;
		}
	}

	[TestMethod]
	public void AThemeParkIncRunHasNoTorchEvenWithTheExtensionOn()
	{
		EveningContent.DirectoryOverride = Folder;
		var edition = GameEditions.Current;
		try
		{
			using var scope = OpenTpwExtensions.Use( EveningSet( true ) );
			Assert.IsNotNull( EveningContent.CatalogRoot( OpenTpwExtensions.Active ) );
			GameEditions.Select( GameEdition.ThemeParkInc );
			Assert.IsNull( EveningContent.CatalogRoot( OpenTpwExtensions.Active ), "Theme Park Inc's parks have other themes" );
		}
		finally
		{
			GameEditions.Select( edition );
			EveningContent.DirectoryOverride = null;
		}
	}

	// ---- Saves -------------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void ASaveHoldsTheTorchesAssetKeysOfThePlacedTorchesOnly()
	{
		var torch = ObjectCatalog.Load( "jungle" ).Get( 63900 );
		var other = ObjectCatalog.Load( "hallow" ).Get( 63901 );
		var manifest = new Dictionary<string, string>
		{
			["data:/levels/jungle/rides/totem/totem.sam"] = new string( 'a', 64 ),
			["mod:opentpw.evening:/objects/jungle/torch/Torch.sam"] = new string( 'b', 64 ),
			["mod:opentpw.evening:/objects/hallow/torch/Torch.sam"] = new string( 'c', 64 )
		};
		var none = ParkResumeMods.AssetsForUse( manifest, Array.Empty<ObjectCatalogEntry>() );
		CollectionAssert.AreEquivalent( new[] { "data:/levels/jungle/rides/totem/totem.sam" }, none.Keys.ToArray(), "no torch placed: none of the extension's files" );
		var placed = ParkResumeMods.AssetsForUse( manifest, new[] { torch } );
		CollectionAssert.AreEquivalent( new[] { "data:/levels/jungle/rides/totem/totem.sam", "mod:opentpw.evening:/objects/jungle/torch/Torch.sam" }, placed.Keys.ToArray(), "the placed torch's files only" );
		Assert.IsTrue( ParkResumeMods.IsEveningObject( torch ) && ParkResumeMods.IsEveningObject( other ) );
		Assert.IsFalse( ParkResumeMods.IsEveningObject( ObjectCatalog.Load( "jungle" ).Entries.First( item => item.ModId == null ) ) );
		StringAssert.Contains( ParkResumeMods.ShapeProblem( null, placed )!, "mod opentpw.evening, which the save does not record" );
		Assert.IsNull( ParkResumeMods.ShapeProblem( new[] { new ParkModRecord( ModId, "1.1.0", ModSet.NoFilesSha256 ) }, placed ) );
	}

	[TestMethod]
	public void AParkUsesTheExtensionWithATorchAndOtherwiseSavesAsIfItWereOff()
	{
		var torch = ObjectCatalog.Load( "jungle" ).Get( 63900 );
		var ordinary = ObjectCatalog.Load( "jungle" ).Entries.First( item => item.ModId == null );
		var cells = new ParkCellMap( 20, 10 );
		CollectionAssert.AreEqual( Array.Empty<string>(), ParkResumeMods.InUse( cells ).ToArray() );
		CollectionAssert.AreEqual( new[] { ModId }, ParkResumeMods.InUse( cells, evening: true ).ToArray() );
		Assert.IsFalse( ParkResumeMods.EveningInUse( new[] { ordinary } ), "an ordinary object: not in use" );
		Assert.IsFalse( ParkResumeMods.EveningInUse( Array.Empty<ObjectCatalogEntry>() ) );
		Assert.IsTrue( ParkResumeMods.EveningInUse( new[] { ordinary, torch } ), "a torch" );
		// the capture follows the rule, with the extension on and off, and the park without a torch saves the same bytes
		var plain = ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() );
		foreach ( var enabled in new[] { true, false } )
		{
			var set = EveningSet( enabled );
			using var scope = OpenTpwExtensions.Use( set );
			var unused = ParkResumeMods.Capture( set, ParkResumeMods.InUse( cells ) );
			Assert.IsNull( unused, $"extension {enabled}: nothing to record in a park without a torch" );
			Assert.IsTrue( ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() with { Mods = unused } ).SequenceEqual( plain ), $"extension {enabled}: identical bytes" );
			var used = ParkResumeMods.Capture( set, ParkResumeMods.InUse( cells, evening: true ) );
			if ( enabled )
				Assert.AreEqual( ModId, used!.Single().Id );
			else
				Assert.IsNull( used, "an extension that is off records nothing" );
		}
	}

	[TestMethod]
	public void ASaveWithATorchRecordsTheExtensionRoundTripsAndRefusesToLoadWithItOff()
	{
		var on = EveningSet( true );
		var off = EveningSet( false );
		var recorded = ParkResumeSaveFileTests.Envelope() with { Mods = ParkResumeMods.Capture( on, new HashSet<string> { ModId } ) };
		ParkResumeSaveFile.ValidateShape( recorded );
		var bytes = ParkResumeSaveFile.Serialize( recorded );
		StringAssert.Contains( System.Text.Encoding.UTF8.GetString( bytes ), "opentpw.evening" );
		var loaded = ParkResumeSaveFile.Deserialize( bytes );
		Assert.IsTrue( ParkResumeSaveFile.Serialize( loaded ).SequenceEqual( bytes ), "byte-stable" );
		ParkResumeMods.Require( loaded.Mods, on );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( loaded.Mods, off ) ).Message, "needs opentpw.evening 1.1.0" );
		// a save made without a torch loads with the extension on or off (a used-only extension that is not recorded is fine)
		ParkResumeMods.Require( null, on );
		ParkResumeMods.Require( null, off );
	}

	// ---- The lamps ---------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void EachThemesTorchHasItsOwnLampRowThatLightsTheFlameAndMatchesNoOtherObject()
	{
		var table = EveningLampTable.Parse( File.ReadAllText( Path.Combine( Repository, EveningLampTable.RelativePath ) ) );
		var colours = new List<System.Numerics.Vector3>();
		for ( var index = 0; index < Themes.Length; index++ )
		{
			var entry = ObjectCatalog.Load( Themes[index] ).Get( 63900 + index );
			var rule = table.Find( entry );
			Assert.IsNotNull( rule, Themes[index] );
			Assert.AreEqual( ModId, rule!.Mod, "matched by the extension's own content root" );
			Assert.AreEqual( Themes[index], rule.Theme );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			var missing = new List<string>();
			var lamps = EveningLampResolver.Resolve( model, rule, missing );
			Assert.AreEqual( 0, missing.Count );
			var lamp = lamps.Single();
			Assert.AreEqual( model.Nodes.Single( node => node.Name == "flame" ).Index, lamp.NodeIndex, "the light hangs on the flame" );
			Assert.IsTrue( lamp.RadiusModelUnits is >= 30 and <= 50, "a torch lights a few cells around it, less than a street lamp" );
			colours.Add( lamp.Colour );
			// an original object (here the stand-in totem) never matches a torch row, even with the same archive name
			var ordinary = ObjectCatalog.Load( "jungle" ).Entries.First( item => item.ModId == null );
			Assert.IsFalse( rule.Matches( ordinary ) );
		}
		Assert.IsTrue( colours[2].Z > colours[2].X, "space: a blue plasma flame" );
		Assert.IsTrue( colours[3].X > colours[3].Y && colours[3].Z > colours[3].Y, "fantasy: a pink flame" );
		Assert.IsTrue( colours[0].X > colours[0].Z && colours[1].X > colours[1].Z, "jungle and Halloween: a warm flame" );
		Assert.IsNotNull( EveningLampTable.Parse( "{ \"schema\": 1, \"lamps\": [ { \"match\": { \"mod\": \"opentpw.evening\" }, \"lights\": [ { \"node\": \"flame\", \"colour\": [1, 1, 1], \"radius\": 40 } ] } ] }" ).Rules.Single().Mod );
		Assert.ThrowsException<InvalidDataException>( () => EveningLampTable.Parse( "{ \"schema\": 1, \"lamps\": [ { \"match\": { \"mod\": \"\" }, \"lights\": [ { \"node\": \"flame\", \"colour\": [1, 1, 1], \"radius\": 40 } ] } ] }" ), "an empty mod id" );
	}

	// ---- The browser -------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheBrowserBuildEmbedsTheTorchesAndTheServerImageCopiesThem()
	{
		var project = File.ReadAllText( Path.Combine( Repository, "source", "OpenTPW.Web", "OpenTPW.Web.csproj" ) );
		StringAssert.Contains( project, "<EmbeddedResource Include=\"../../content/evening/**/*\" LogicalName=\"content/evening/%(RecursiveDir)%(Filename)%(Extension)\" />" );
		StringAssert.Contains( project, "!Exists('../../content/evening/objects.json')", "the build fails rather than ship the page without the torches" );
		var dockerfile = File.ReadAllText( Path.Combine( Repository, "source", "OpenTPW.Server", "Dockerfile" ) );
		StringAssert.Contains( dockerfile, "COPY content/evening/ content/evening/" );
	}
}
