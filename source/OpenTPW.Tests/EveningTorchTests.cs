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
	public void TheEveningExtensionsTorchFlamesAreLightSourcesAndItsBodiesAreLitExceptTheirOwnGlow()
	{
		foreach ( var (theme, index) in new[] { "jungle", "hallow", "space", "fantasy" }.Select( ( theme, index ) => (theme, index) ) )
		{
			var entry = ObjectCatalog.Load( theme ).Get( 63900 + index );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			var flame = model.Meshes[model.Nodes.Single( node => node.Name == "flame" ).MeshIndex];
			Assert.IsTrue( flame.Materials.All( material => (material.Pad & ModelBuilder.SelfLitMaterialFlag) != 0 && TextureSlotAddressing.IsLightSource( material.Name ) ), $"{theme}: the flame stays full-bright at night" );
			var vertices = ObjectAssets.ConvertMesh( flame );
			Assert.IsTrue( vertices.All( vertex => (vertex.MatFlags & TextureSlotAddressing.LightSourceFlag) != 0 && (vertex.MatFlags & TextureSlotAddressing.SelfLitFlag) != 0 ), $"{theme}: the flame's vertices" );
			// the body is lit except for what its own art makes a light (the hallow torch's tt_hal_glow): a corner is a light source exactly when its material is self-lit and named for light
			var body = model.Meshes[model.Nodes.Single( node => node.Name == "torch" ).MeshIndex];
			Assert.IsTrue( body.Materials.Any( material => (material.Pad & ModelBuilder.SelfLitMaterialFlag) == 0 && !TextureSlotAddressing.IsLightSource( material.Name ) ), $"{theme}: the body has lit parts" );
			var bodyVertices = ObjectAssets.ConvertMesh( body );
			for ( var corner = 0; corner < bodyVertices.Length; corner++ )
			{
				var material = body.Materials[(int)body.Vertices[corner].TextureIndex];
				Assert.AreEqual( (material.Pad & ModelBuilder.SelfLitMaterialFlag) != 0 && TextureSlotAddressing.IsLightSource( material.Name ), (bodyVertices[corner].MatFlags & TextureSlotAddressing.LightSourceFlag) != 0, $"{theme}: {material.Name}" );
			}
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
		// the load offers to switch the extension on for a park with a torch, as for the other content extensions, and then needs nothing more
		var plan = ParkExtensionSwitch.Plan( loaded.Mods, off );
		Assert.IsTrue( plan.CanSwitch );
		CollectionAssert.AreEqual( new[] { ModId }, plan.TurnOn.Select( extension => extension.Id ).ToArray() );
		Assert.IsNull( ParkExtensionSwitch.RequiresText( loaded.Mods, off ), "not listed as needing a mod" );
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

	// ---- The flicker and the flame -----------------------------------------------------------------------------------------

	[TestMethod]
	public void TheFlickerIsDeterministicBoundedSmoothAndDiffersBetweenNeighbouringTorches()
	{
		var seed = TorchFlicker.SeedOf( 12, 7 );
		Assert.AreEqual( seed, TorchFlicker.SeedOf( 12, 7 ), "the same cell, the same seed" );
		Assert.AreNotEqual( seed, TorchFlicker.SeedOf( 13, 7 ) );
		var (low, high, sum, steps) = (1f, 0f, 0.0, 0);
		var previous = TorchFlicker.At( 0, seed );
		for ( var frame = 1; frame <= 60 * 120; frame++ )
		{
			var time = frame / 60.0;
			var value = TorchFlicker.At( time, seed );
			Assert.AreEqual( value, TorchFlicker.At( time, seed ), "a pure function of the time and the seed" );
			Assert.IsTrue( value is >= 0f and <= 1f );
			Assert.IsTrue( MathF.Abs( value - previous ) < 0.105f, $"gentle: no jump between two frames at {time:F2} s ({previous:F3} to {value:F3})" );
			(low, high, sum, steps, previous) = (MathF.Min( low, value ), MathF.Max( high, value ), sum + value, steps + 1, value);
		}
		Assert.IsTrue( low < 0.2f && high > 0.8f, $"it does flicker: {low:F2} to {high:F2}" );
		Assert.AreEqual( 0.5, sum / steps, 0.05, "half on average" );
		// two torches side by side do not pulse together
		var neighbour = TorchFlicker.SeedOf( 14, 7 );
		var together = Enumerable.Range( 0, 600 ).Count( frame => MathF.Abs( TorchFlicker.At( frame / 60.0, seed ) - TorchFlicker.At( frame / 60.0, neighbour ) ) < 0.05f );
		Assert.IsTrue( together < 300, $"{together} of 600 frames alike" );
	}

	[TestMethod]
	public void TheFlameStretchesAndLeansAboutItsBaseWithinItsBounds()
	{
		var seed = TorchFlicker.SeedOf( 3, 4 );
		for ( var frame = 0; frame < 600; frame++ )
		{
			var matrix = TorchFlicker.FlameMatrix( frame / 60.0, seed );
			Assert.AreEqual( System.Numerics.Vector3.Zero, System.Numerics.Vector3.Transform( System.Numerics.Vector3.Zero, matrix ), "the base stays put" );
			var tip = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( 0, 1, 0 ), matrix );
			Assert.IsTrue( tip.Length() is > 1f - TorchFlicker.FlameStretch - 0.001f and < 1f + TorchFlicker.FlameStretch + 0.001f, $"the height stays within the stretch: {tip.Length():F3}" );
			Assert.IsTrue( tip.Y > 0.8f && MathF.Abs( tip.X ) < 0.1f && MathF.Abs( tip.Z ) < 0.1f, $"a small lean: {tip}" );
		}
		Assert.AreNotEqual( TorchFlicker.FlameMatrix( 1.0, seed ), TorchFlicker.FlameMatrix( 1.25, seed ), "it moves" );
		// by day (the lamp level at the lamps' threshold or below) there is no flame; it grows with the lamp level about its base and is whole at night
		Assert.IsNull( TorchFlicker.FlameAt( 1.0, seed, 0f ) );
		Assert.IsNull( TorchFlicker.FlameAt( 1.0, seed, TorchFlicker.FlameLevelThreshold ) );
		Assert.AreEqual( TorchFlicker.FlameMatrix( 1.0, seed ), TorchFlicker.FlameAt( 1.0, seed, 1f ) );
		var dusk = TorchFlicker.FlameAt( 1.0, seed, 0.3f )!.Value;
		Assert.AreEqual( System.Numerics.Vector3.Zero, System.Numerics.Vector3.Transform( System.Numerics.Vector3.Zero, dusk ), "it grows from its base" );
		Assert.AreEqual( 0.3f * System.Numerics.Vector3.Transform( System.Numerics.Vector3.UnitY, TorchFlicker.FlameMatrix( 1.0, seed ) ).Length(), System.Numerics.Vector3.Transform( System.Numerics.Vector3.UnitY, dusk ).Length(), 1e-5f );
	}

	[TestMethod]
	public void OnlyTheTorchRowsFlickerAndNameAFlameAndBadValuesAreRefused()
	{
		var table = EveningLampTable.Parse( File.ReadAllText( Path.Combine( Repository, EveningLampTable.RelativePath ) ) );
		var torches = table.Rules.Where( rule => rule.Mod == ModId ).ToArray();
		Assert.AreEqual( 4, torches.Length );
		Assert.IsTrue( torches.All( rule => rule.Flame == "flame" && rule.Lights.All( light => light.Flicker == 0.25f ) ), "a quarter: a gentle flicker" );
		Assert.IsTrue( table.Rules.Except( torches ).All( rule => rule.Flame == null && rule.Lights.All( light => light.Flicker == 0f ) ), "the other lamps stay steady" );
		string Table( string row ) => "{ \"schema\": 1, \"lamps\": [ { \"match\": { \"mod\": \"x\" }, " + row + " } ] }";
		var good = EveningLampTable.Parse( Table( "\"flame\": \"f\", \"lights\": [ { \"node\": \"f\", \"colour\": [1, 1, 1], \"radius\": 40, \"flicker\": 0.5 } ]" ) );
		Assert.AreEqual( ("f", 0.5f), (good.Rules[0].Flame, good.Rules[0].Lights[0].Flicker) );
		foreach ( var bad in new[] { "\"flicker\": 0.6", "\"flicker\": -0.1", "\"flicker\": \"lots\"" } )
			Assert.ThrowsException<InvalidDataException>( () => EveningLampTable.Parse( Table( "\"lights\": [ { \"node\": \"f\", \"colour\": [1, 1, 1], \"radius\": 40, " + bad + " } ]" ) ), bad );
		foreach ( var bad in new[] { "\"flame\": \"\"", "\"flame\": 3" } )
			Assert.ThrowsException<InvalidDataException>( () => EveningLampTable.Parse( Table( bad + ", \"lights\": [ { \"node\": \"f\", \"colour\": [1, 1, 1], \"radius\": 40 } ]" ) ), bad );
		// the resolved lamp carries the flicker
		var entry = ObjectCatalog.Load( "space" ).Get( 63902 );
		var lamp = EveningLampResolver.Resolve( ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath ), table.Find( entry )! ).Single();
		Assert.AreEqual( 0.25f, lamp.Flicker );
	}

	[TestMethod]
	public void EachTorchsFlameIsDrawnAsItsOwnPartSoTheExtensionCanMoveAndHideIt()
	{
		for ( var index = 0; index < Themes.Length; index++ )
		{
			var entry = ObjectCatalog.Load( Themes[index] ).Get( 63900 + index );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			var flame = model.Nodes.Single( node => node.Name == "flame" ).Index;
			Assert.IsTrue( ObjectRenderParts.FindAnimatedNodes( entry, model )[flame], $"{Themes[index]}: the flame node is kept apart" );
			var parts = ObjectRenderParts.Build( entry, model );
			Assert.AreEqual( 1, parts.Count( part => part.NodeIndex == flame ), $"{Themes[index]}: one part draws the flame alone" );
			Assert.IsTrue( parts.Any( part => part.NodeIndex == -1 ), "the body is baked as any static model" );
		}
		// an original object's static nodes stay baked: only a row that names a flame keeps a node apart
		var totem = ObjectCatalog.Load( "jungle" ).Entries.First( item => item.ModId == null );
		Assert.IsNull( EveningLampTable.Shipped.Find( totem )?.Flame );
	}

	[TestMethod]
	public void TheFlamesFollowPlacementAndRemovalMoveEveryFrameAndOnlyBurnWhileTheLampsAreOn()
	{
		static OriginalObject Stub() => (OriginalObject)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( OriginalObject ) );
		var placed = new List<OriginalObject>();
		var version = 0;
		var torches = new HashSet<OriginalObject>();
		var applied = new List<(OriginalObject Item, int Node, System.Numerics.Matrix4x4? Local)>();
		var flames = new EveningFlames( new EveningLampTable( Array.Empty<EveningLampRule>() ), () => placed, () => version )
		{
			NodeOverride = item => torches.Contains( item ) ? 1 : -1,
			ApplyOverride = ( item, node, local ) => applied.Add( (item, node, local) )
		};
		flames.Animate( 0, 1f );
		Assert.AreEqual( 0, flames.Count );
		var (a, b) = (Stub(), Stub());
		torches.Add( a );
		placed.AddRange( new[] { a, b } );
		version++;
		flames.Animate( 1.0, 1f );
		Assert.AreEqual( 1, flames.Count, "only the object whose row names a flame" );
		Assert.AreEqual( (a, 1), (applied.Single().Item, applied.Single().Node) );
		Assert.AreEqual( TorchFlicker.FlameMatrix( 1.0, TorchFlicker.SeedOf( 0, 0 ) ), applied.Single().Local, "at night the full flame of its cell's seed at that time" );
		flames.Animate( 1.5, 1f );
		Assert.AreNotEqual( applied[0].Local, applied[1].Local, "a frame later it has moved" );
		flames.Animate( 2.0, 0f );
		Assert.IsNull( applied[^1].Local, "by day the flame is hidden: an empty cup" );
		flames.Animate( 2.0, 0.5f );
		Assert.AreEqual( System.Numerics.Matrix4x4.CreateScale( 0.5f ) * TorchFlicker.FlameMatrix( 2.0, TorchFlicker.SeedOf( 0, 0 ) ), applied[^1].Local, "at dusk it grows with the lamp level" );
		placed.Remove( a );
		version++;
		applied.Clear();
		flames.Animate( 2.0, 1f );
		Assert.AreEqual( 0, flames.Count, "a removed torch leaves the cache" );
		Assert.AreEqual( 0, applied.Count );
		// switching the look off gives the remaining flames their model pose back
		var cleared = new List<OriginalObject>();
		flames.ClearOverride = ( item, node ) => cleared.Add( item );
		torches.Add( b );
		version++;
		flames.Animate( 2.5, 1f );
		flames.Clear();
		CollectionAssert.AreEqual( new[] { b }, cleared );
		Assert.AreEqual( 0, flames.Count );
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
