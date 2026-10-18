using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The umbrellas extension's shipped content (docs/UMBRELLAS.md): <c>content/umbrellas</c> holds the brolly shop of each theme, the umbrella sprite set and the hand anchors of the 2x kid sets, made by
/// <c>tools/umbrellas/build-content.sh</c>. Most of these tests load the committed files through the game's own pipelines (the object catalog, the sprite
/// readers, the ride script VM) with a content root made here, without any original game data.
/// </summary>
// [EXT:umbrellas-content] OpenTPW's own shipped content; neither Theme Park World nor Theme Park Inc ships anything of it
[TestClass]
[DoNotParallelize]
public class UmbrellasContentTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "space", "fantasy" };
	private static readonly string[] KidSets = { "BE", "BI", "CH", "FR", "KI", "SA", "SU", "TA" };
	private const string ModId = "opentpw.umbrellas";
	private string root = null!;
	private BaseFileSystem? previousFileSystem;
	private IReadOnlyList<CatalogContentRoot>? previousRoots;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-umbrellas-" ).FullName;
		previousFileSystem = FileSystem;
		// a stand-in game folder: the catalog needs a file system, the shops' data comes from the extension's own content
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
			var candidate = Path.Combine( directory.FullName, "content", "umbrellas" );
			if ( File.Exists( Path.Combine( candidate, "objects.json" ) ) && Directory.Exists( Path.Combine( directory.FullName, "tools", "umbrellas" ) ) )
				return candidate;
		}
		throw new FileNotFoundException( "content/umbrellas not found above the test binaries." );
	}

	// ---- The shops ------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheBrollyShopsHaveTheirOwnReservedIdsBelowThePoolsRangeAndAvailableFromTheStart()
	{
		var file = ModObjectsFile.Load( Path.Combine( Folder, "objects.json" ), Array.Empty<string>() );
		Assert.AreEqual( (64500, 64999), (file.FirstId, file.LastId), "the umbrellas' own range: it overlaps neither the pools (65000 and up) nor the TPI rides (40000 to 40499)" );
		Assert.IsTrue( file.LastId < PoolContent.FirstReservedId );
		Assert.IsTrue( file.FirstId >= CrossEditionObjects.FirstReservableId && file.LastId <= CrossEditionObjects.LastReservableId );
		CollectionAssert.AreEqual( Themes, file.Objects.Select( item => item.Theme ).ToArray() );
		CollectionAssert.AreEqual( new[] { 64500, 64501, 64502, 64503 }, file.Objects.Select( item => item.InfoId ).ToArray() );
		Assert.IsTrue( file.Objects.All( item => item.Category == ObjectCategory.Shop && item.Input == null && !item.Research ), "shops, our own files, no research item (decision of October 18, 2026)" );
		Assert.IsTrue( file.Objects.All( item => item.Names.Count == 11 && item.Names["English"] == "Brolly Shop" ), "a name for each of the eleven languages" );
	}

	[TestMethod]
	public void EveryThemeListsItsBrollyShopWithTheDataOfThePlan()
	{
		for ( var index = 0; index < Themes.Length; index++ )
		{
			var catalog = ObjectCatalog.Load( Themes[index] );
			var entry = catalog.Get( 64500 + index );
			Assert.AreEqual( ObjectCategory.Shop, entry.Category, Themes[index] );
			Assert.AreEqual( ModId, entry.ModId );
			Assert.AreEqual( $"mod:{ModId}:", entry.Root.KeyPrefix, "a park save records and checks its files like a mod's" );
			Assert.AreEqual( "Brolly Shop", entry.DisplayName );
			CollectionAssert.AreEqual( new[] { "**", "2*" }, entry.Shape.Rows.ToArray(), "a 2 x 2 booth whose customer cell is its lower left cell" );
			Assert.IsTrue( entry.IsChoosable && !entry.HasQueue && entry.WhichUIType == 1 && entry.IsBuildable );
			Assert.AreEqual( 1, catalog.Entries.Count( item => item.ModId == ModId ), "one shop per theme" );
			Assert.IsNull( catalog.Find( 64500 + (index + 1) % 4 ), "another theme's shop is not in this catalog" );

			var settings = entry.Settings;
			Assert.AreEqual( 3, settings.GetInt( "UsageInfo.AppearanceEffect" ), "3 = an umbrella, [TPI-EXE:0x612740]" );
			Assert.AreEqual( 1, settings.GetInt( "UsageInfo.ShopType" ) );
			Assert.AreEqual( 20, settings.GetInt( "UsageInfo.HappinessEffect" ) );
			Assert.AreEqual( 15, settings.GetInt( "UsageInfo.LitterEffect" ) );
			Assert.AreEqual( 1, settings.GetInt( "UsageInfo.RideHandlesSprite" ) );
			Assert.AreEqual( 0, settings.GetInt( "UsageInfo.ISIndoors", -1 ), "not indoors, so the original's indoor-in-rain bonus does not stack on the 200 %" );
			Assert.AreEqual( (200, 200, 150, 5), (settings.GetInt( "WeatherModifier.RainPercentage" ), settings.GetInt( "WeatherModifier.StormPercentage" ), settings.GetInt( "WeatherModifier.SnowPercentage" ), settings.GetInt( "WeatherModifier.HeatWavePercentage" )) );
			Assert.AreEqual( 75, settings.GetInt( "GenerationModifier.KidScorePercentage" ), "Theme Park Inc's value, per the owner's decision" );
			Assert.AreEqual( 30, entry.Economy.PricePerUse, "as Theme Park Inc" );
			Assert.AreEqual( 13, entry.Economy.CostOfGoods );
			Assert.AreEqual( 1000, entry.BuildCost );
			Assert.AreEqual( 10, entry.InitialCapacity );
			Assert.AreEqual( 0, entry.Economy.Upgrades.Single( level => level.Level == 0 ).GetInt( "CostOfResearch" ), "available from the start" );
			Assert.AreEqual( 35, entry.Economy.Upgrades.Single( level => level.Level == 0 ).GetInt( "RunningCost" ) );
			Assert.IsNotNull( entry.ScriptPath );
		}
	}

	[TestMethod]
	public void EachBrollyShopsModelTexturesAndScriptLoadAndRun()
	{
		for ( var index = 0; index < Themes.Length; index++ )
		{
			var theme = Themes[index];
			var entry = ObjectCatalog.Load( theme ).Get( 64500 + index );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			Assert.AreEqual( ModelFileKind.Geometry, model.Kind );
			Assert.IsTrue( model.Meshes.Count >= 1, theme );
			var textures = new HashSet<string>();
			foreach ( var mesh in model.Meshes )
			{
				Assert.IsNull( ObjectAssets.GetUnrenderableReason( mesh ), $"{theme}: {mesh.Name}" );
				Assert.IsTrue( ObjectAssets.ConvertMesh( mesh ).Length > 0 );
				foreach ( var material in mesh.Materials )
					textures.Add( material.Name );
			}
			Assert.IsTrue( textures.Count >= 8 && textures.All( name => name.StartsWith( "br_", StringComparison.Ordinal ) ), $"{theme}: {string.Join( ", ", textures )}" );
			foreach ( var name in new[] { "u1", "u2", "u3", "roof", "roof2" } )
				Assert.IsTrue( textures.Any( texture => texture.EndsWith( "_" + name, StringComparison.Ordinal ) ), $"{theme}: the {name} texture" );
			foreach ( var name in textures )
			{
				var texture = ObjectAssets.ResolveTexture( entry, name );
				Assert.IsNotNull( texture, $"{theme}: {name}" );
				var (data, width, height) = Texture.DecodeImage( texture!.Value.FileSystem, texture.Value.Path );
				Assert.IsTrue( width is 64 or 128 && height == width && data.Length == width * height * 4, $"{theme}: {name} is {width} x {height}" );
			}
			// the footprint: 2 x 2 cells, the roof reaches the whole width, the booth stands inside it
			var composed = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), WorldOf( model, mesh.NodeIndex ) ) ) ).ToArray();
			Assert.IsTrue( composed.All( p => p.X >= -0.01f && p.X <= 20.01f && p.Z >= -0.01f && p.Z <= 20.01f && p.Y >= -0.01f && p.Y < 20f ), $"{theme}: the shop stands on its 2 x 2 cells" );
			Assert.IsTrue( composed.Min( p => p.X ) < 1.1f && composed.Max( p => p.X ) > 18.9f && composed.Max( p => p.Y ) > 13f, $"{theme}: the umbrella roof reaches the footprint and rises above the booth" );
			// [EXT:pools-model-inset] a walker is a sprite: at its height no wall, counter or post comes within 1 unit of the footprint's edge
			foreach ( var height in new[] { 1f, 2f, 3f, 4f, 6f } )
			{
				var slice = PoolsContentTests.SliceAt( model, height );
				Assert.IsNotNull( slice, $"{theme}: something stands {height} units above the ground" );
				var (minX, maxX, minZ, maxZ) = slice!.Value;
				Assert.IsTrue( minX >= 1.0f && 20f - maxX >= 1.0f && minZ >= 1.0f && 20f - maxZ >= 1.0f, $"{theme}: at {height} units the shop stands X {minX:F1}..{maxX:F1}, Z {minZ:F1}..{maxZ:F1}; every side keeps 1 unit from the edge" );
			}
			Assert.AreEqual( 0, RideHeadSpace.Create( model, seed: 1 )?.Count ?? 0, "no head records: a customer inside is not drawn" );

			using var scriptStream = entry.FileSystem.OpenRead( entry.ScriptPath! );
			var vm = new RideVM( new RideScriptFile( scriptStream ), new RideVMOptions { Effects = new NoEffects(), Seed = 1 } );
			vm[RideVariables.VAR_CAPACITY] = entry.InitialCapacity;
			vm[RideVariables.VAR_LETMEON] = 7;
			var released = -1;
			for ( var step = 0; step < 600 && released < 0; step++ )
			{
				vm.Advance( 1.0 / 60 );
				Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
				if ( vm[RideVariables.VAR_LETMEOFF] == 7 )
					released = step;
			}
			Assert.IsTrue( released is > 60 and < 240, $"{theme}: the shop serves and releases its customer within a few seconds ({released} steps of 1/60 s)" );
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

	// ---- The umbrella sprites --------------------------------------------------------------------------------------

	private static GuestSpriteAtlas LoadUmbrellas() =>
		Load( Path.Combine( Folder, "esprites", "Umbrellas", "SPR_UM.TPC" ), "SPR_UM" );

	private static GuestSpriteAtlas Load( string tpc, string name ) =>
		new( name, new SpriteBankFile( new MemoryStream( File.ReadAllBytes( tpc ) ) ), new SpriteAnimationFile( new MemoryStream( File.ReadAllBytes( Path.ChangeExtension( tpc, ".ESP" ) ) ) ) );

	[TestMethod]
	public void TheUmbrellaSetHoldsThreeColourwaysOfFiveDirectionsAtTwiceTheSizeWithTheHotspotAtTheHandleTip()
	{
		var set = LoadUmbrellas();
		Assert.AreEqual( 15, set.Frames.Length, "3 colourways x 5 directions x 1 frame (the game mirrors the five to eight)" );
		for ( var slot = 0; slot < 3; slot++ )
		{
			Assert.AreEqual( (1, 5), (set.Animations.Animations[slot].FramesPerDirection, set.Animations.Animations[slot].Directions), $"colourway {slot}" );
			for ( var direction = 0; direction < 5; direction++ )
			{
				var frame = set.Frames[set.GetFrame( slot, direction, 0 )];
				Assert.AreEqual( (256, 256), (frame.PixelsPerUnitX, frame.PixelsPerUnitY), $"colourway {slot} d{direction}: 2x frames at 256 pixels per unit" );
				// the canopy is 0.8 of a kid's height across (kid about 76 pixels at 2x): 40 to 70 pixels wide, the whole umbrella 55 to 100 tall
				Assert.IsTrue( frame.Width is >= 40 and <= 70 && frame.Height is >= 55 and <= 100, $"colourway {slot} d{direction}: {frame.Width} x {frame.Height}" );
				// the hotspot is the handle tip, the lowest point: inside the frame at its bottom edge, within the width
				Assert.IsTrue( frame.OriginY < -frame.Height / 2 && frame.OriginY + frame.Height is >= 0 and <= 4, $"colourway {slot} d{direction}: the tip is at the bottom edge, the crook's hook reaches a few pixels below it ({frame.OriginX}, {frame.OriginY}, {frame.Width} x {frame.Height})" );
				Assert.IsTrue( frame.OriginX < 0 && frame.OriginX + frame.Width > 0, $"colourway {slot} d{direction}: the tip lies under the umbrella" );
			}
		}
		foreach ( var slot in Enumerable.Range( 3, 13 ) )
			Assert.IsTrue( set.Animations.Animations[slot].IsEmpty, $"slot {slot} is not shipped" );
		Assert.IsTrue( set.Frames.All( frame => HasPixel( set, frame ) ) );
	}

	[TestMethod]
	public void TheThreeColourwaysDifferAndTheDirectionsDiffer()
	{
		var set = LoadUmbrellas();
		string Sum( int slot, int direction )
		{
			var frame = set.Frames[set.GetFrame( slot, direction, 0 )];
			var bytes = new List<byte>();
			for ( var y = 0; y < frame.Height; y++ )
				for ( var x = 0; x < frame.Width; x++ )
					bytes.AddRange( set.Pixels.AsSpan( ((frame.Y + y) * set.Width + frame.X + x) * 4, 4 ).ToArray() );
			return Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( bytes.ToArray() ) );
		}
		var all = Enumerable.Range( 0, 3 ).SelectMany( slot => Enumerable.Range( 0, 5 ).Select( direction => Sum( slot, direction ) ) ).ToArray();
		Assert.AreEqual( 15, all.Distinct().Count(), "every frame is its own picture" );
	}

	private static bool HasPixel( GuestSpriteAtlas set, GuestAtlasFrame frame )
	{
		for ( var y = 0; y < frame.Height; y++ )
			for ( var x = 0; x < frame.Width; x++ )
				if ( set.Pixels[((frame.Y + y) * set.Width + frame.X + x) * 4 + 3] > 0 )
					return true;
		return false;
	}

	// ---- The hand anchors ------------------------------------------------------------------------------------------

	private static JsonDocument Anchors() => JsonDocument.Parse( File.ReadAllText( Path.Combine( Folder, "anchors.json" ) ) );

	[TestMethod]
	public void TheAnchorTableCoversEveryKidSetSlotAndDirection()
	{
		using var document = Anchors();
		var top = document.RootElement;
		Assert.AreEqual( 1, top.GetProperty( "schema" ).GetInt32() );
		Assert.AreEqual( 256, top.GetProperty( "pixelsPerUnit" ).GetInt32(), "the pixels of the 2x kid sets" );
		Assert.AreEqual( 45.0, top.GetProperty( "elevation" ).GetDouble(), "the pools kid camera" );
		Assert.AreEqual( 5, top.GetProperty( "directions" ).GetInt32() );
		var sets = top.GetProperty( "sets" );
		CollectionAssert.AreEqual( KidSets, sets.EnumerateObject().Select( set => set.Name ).ToArray(), "the eight Theme Park World kid sets" );
		foreach ( var set in sets.EnumerateObject() )
		{
			Assert.IsTrue( set.Value.GetProperty( "handHeight" ).GetDouble() is > 0.5 and < 2.0, $"{set.Name}: the hand's height above the feet" );
			foreach ( var (slotName, slot, frames) in new[] { ("stand", 0, 1), ("walk", 1, 8), ("wait", 14, 2) } )
			{
				var table = set.Value.GetProperty( slotName );
				Assert.AreEqual( slot, table.GetProperty( "slot" ).GetInt32(), $"{set.Name} {slotName}" );
				var directions = table.GetProperty( "frames" ).EnumerateArray().ToArray();
				Assert.AreEqual( 5, directions.Length, $"{set.Name} {slotName}: five directions" );
				foreach ( var direction in directions )
				{
					Assert.AreEqual( frames, direction.GetArrayLength(), $"{set.Name} {slotName}: {frames} frames per direction" );
					foreach ( var point in direction.EnumerateArray() )
						Assert.AreEqual( 2, point.GetArrayLength() );
				}
			}
		}
	}

	[TestMethod]
	public void TheAnchorsAreSymmetricAcrossTheMirroredDirectionsAndLieInsideTheShippedKidFrames()
	{
		using var document = Anchors();
		var pools = Path.Combine( Folder, "..", "pools", "esprites", "Generic", "Kids" );
		foreach ( var set in document.RootElement.GetProperty( "sets" ).EnumerateObject() )
		{
			var stand = set.Value.GetProperty( "stand" ).GetProperty( "frames" ).EnumerateArray().Select( direction => direction[0] ).Select( point => (X: point[0].GetDouble(), Y: point[1].GetDouble()) ).ToArray();
			// the hand is 0.05 units in front of the body's axis, so the side views (1 and 3) are not exact mirrors: within 2 pixels (an eighth of a unit), the back and front views in x exactly and in y within 2
			Assert.IsTrue( Math.Abs( stand[0].X + stand[4].X ) < 0.5 && Math.Abs( stand[1].X + stand[3].X ) < 2.0, $"{set.Name}: the hand's x flips with the mirrored directions" );
			Assert.IsTrue( Math.Abs( stand[1].Y - stand[3].Y ) < 2.0 && Math.Abs( stand[0].Y - stand[4].Y ) < 2.0, $"{set.Name}: and its y does not" );
			Assert.IsTrue( stand.All( point => point.Y < 0 ), $"{set.Name}: the hand is above the feet" );
			Assert.IsTrue( stand[0].X > 10 && stand[4].X < -10, $"{set.Name}: the right hand is on the right seen from behind, on the left seen from the front" );
			var file = Path.Combine( pools, $"SPR_{set.Name}.TPC" );
			if ( !File.Exists( file ) )
				continue;
			var kids = Load( file, $"SPR_{set.Name}" );
			for ( var direction = 0; direction < 5; direction++ )
			{
				var frame = kids.Frames[kids.GetFrame( GuestSpriteAtlas.StandSlot, direction, 0 )];
				Assert.AreEqual( 256, frame.PixelsPerUnitX, "the anchors are in the pixels of these frames" );
				Assert.IsTrue( stand[direction].X >= frame.OriginX && stand[direction].X <= frame.OriginX + frame.Width && stand[direction].Y >= frame.OriginY && stand[direction].Y <= frame.OriginY + frame.Height,
					$"{set.Name} d{direction}: the hand ({stand[direction].X}, {stand[direction].Y}) lies in the standing frame ({frame.OriginX}, {frame.OriginY}, {frame.Width} x {frame.Height})" );
			}
		}
	}

	// ---- The wiring ------------------------------------------------------------------------------------------------

	private static ModSet UmbrellasSet( bool enabled ) => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
		new ModSettings { Enabled = new Dictionary<string, bool> { [ModId] = enabled } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	[TestMethod]
	public void TheExtensionIsRegisteredOnByDefaultAndRecordedLikeThePools()
	{
		var extension = OfficialExtensions.All.Single( candidate => candidate.Id == ModId );
		Assert.AreSame( OfficialExtensions.Umbrellas, extension );
		Assert.AreEqual( "1.0.0", extension.Version.ToString() );
		Assert.IsTrue( extension.DefaultEnabled && extension.AffectsSimulation && extension.PlayerSelectable, "on by default, a simulation rule, selectable (decision of October 18, 2026)" );
		Assert.IsTrue( ParkResumeMods.UsedOnlyExtensionIds.Contains( ModId ), "a save records it only while the park uses it (the pools rule)" );
		using var scope = OpenTpwExtensions.Use( ModSet.Defaults( OfficialExtensions.All ) );
		Assert.IsTrue( FeatureGates.Umbrellas );
	}

	[TestMethod]
	public void TheBrollyShopsAreListedInEveryThemeCatalogWhileTheExtensionIsOnAndOnlyThen()
	{
		UmbrellaContent.DirectoryOverride = Folder;
		try
		{
			foreach ( var enabled in new[] { true, false } )
			{
				var set = UmbrellasSet( enabled );
				using var scope = OpenTpwExtensions.Use( set );
				using var roots = ModContentRoots.Use( set, null );
				foreach ( var theme in Themes )
				{
					var shops = ObjectCatalog.Load( theme ).Entries.Where( entry => entry.ModId == ModId ).ToArray();
					if ( !enabled )
					{
						Assert.AreEqual( 0, shops.Length, $"{theme}: the extension is off, so the catalog lists no brolly shop (and is not the cached catalog of the run with it on)" );
						continue;
					}
					Assert.AreEqual( 1, shops.Length, theme );
					Assert.AreEqual( 64500 + Array.IndexOf( Themes, theme ), shops[0].InfoId );
					Assert.AreEqual( 3, shops[0].Settings.GetInt( "UsageInfo.AppearanceEffect" ) );
				}
			}
		}
		finally
		{
			UmbrellaContent.DirectoryOverride = null;
		}
	}

	[TestMethod]
	public void AThemeParkIncRunHasNoBrollyShopEvenWithTheExtensionOn()
	{
		UmbrellaContent.DirectoryOverride = Folder;
		var edition = GameEditions.Current;
		try
		{
			using var scope = OpenTpwExtensions.Use( UmbrellasSet( true ) );
			Assert.IsNotNull( UmbrellaContent.CatalogRoot( OpenTpwExtensions.Active ) );
			GameEditions.Select( GameEdition.ThemeParkInc );
			Assert.IsFalse( FeatureGates.Umbrellas, "Theme Park Inc has its own shop, art and rules" );
			Assert.IsNull( UmbrellaContent.CatalogRoot( OpenTpwExtensions.Active ), "no brolly shop in a Theme Park Inc catalog" );
		}
		finally
		{
			GameEditions.Select( edition );
			UmbrellaContent.DirectoryOverride = null;
		}
	}

	[TestMethod]
	public void ABrollyShopsBridgeCarriesItsWeatherAndGenerationPercentagesAndNoOtherObjectDoes()
	{
		var shop = ObjectCatalog.Load( "jungle" ).Entries.Single( entry => entry.ModId == ModId );
		var bridge = OriginalObjectRuntime.CreateVisitorBridge( shop, 7 );
		Assert.IsTrue( ((IRideVisitorBridge)bridge).IsUmbrellaShop );
		Assert.AreEqual( new UmbrellaShopTerms( 200, 200, 150, 5, 75 ), bridge.UmbrellaTerms );
		var totem = ObjectCatalog.Load( "jungle" ).Entries.First( entry => entry.ModId == null );
		Assert.IsNull( OriginalObjectRuntime.CreateVisitorBridge( totem, 8 ).UmbrellaTerms, "an original object keeps the game's score: the keys are read for the extension's objects only" );
	}

	// ---- Saves -------------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void ASaveHoldsTheBrollyShopsAssetKeysOfThePlacedShopsOnly()
	{
		var shop = ObjectCatalog.Load( "jungle" ).Get( 64500 );
		var other = ObjectCatalog.Load( "hallow" ).Get( 64501 );
		var manifest = new Dictionary<string, string>
		{
			["data:/levels/jungle/rides/totem/totem.sam"] = new string( 'a', 64 ),
			["mod:opentpw.umbrellas:/objects/jungle/brolly/Brolly.sam"] = new string( 'b', 64 ),
			["mod:opentpw.umbrellas:/objects/hallow/brolly/Brolly.sam"] = new string( 'e', 64 ),
			["mod:opentpw.pools:/objects/jungle/cabin/Cabin.sam"] = new string( 'f', 64 )
		};
		var none = ParkResumeMods.AssetsForUse( manifest, Array.Empty<ObjectCatalogEntry>() );
		CollectionAssert.AreEquivalent( new[] { "data:/levels/jungle/rides/totem/totem.sam" }, none.Keys.ToArray(), "no extension object placed: none of either extension's files" );
		var placed = ParkResumeMods.AssetsForUse( manifest, new[] { shop } );
		CollectionAssert.AreEquivalent( new[] { "data:/levels/jungle/rides/totem/totem.sam", "mod:opentpw.umbrellas:/objects/jungle/brolly/Brolly.sam" }, placed.Keys.ToArray(), "the placed shop's files only" );
		Assert.IsTrue( ParkResumeMods.IsUmbrellasObject( shop ) && ParkResumeMods.IsUmbrellasObject( other ) );
		Assert.IsFalse( ParkResumeMods.IsUmbrellasObject( ObjectCatalog.Load( "jungle" ).Entries.First( item => item.ModId == null ) ) );
		StringAssert.Contains( ParkResumeMods.ShapeProblem( null, placed )!, "mod opentpw.umbrellas, which the save does not record" );
		Assert.IsNull( ParkResumeMods.ShapeProblem( new[] { new ParkModRecord( ModId, "1.0.0", ModSet.NoFilesSha256 ) }, placed ) );
	}

	[TestMethod]
	public void AParkUsesTheExtensionWithABrollyShopOrAnUmbrellaAndOtherwiseSavesAsIfItWereOff()
	{
		var shop = ObjectCatalog.Load( "jungle" ).Get( 64500 );
		var ordinary = ObjectCatalog.Load( "jungle" ).Entries.First( item => item.ModId == null );
		var cells = new ParkCellMap( 20, 10 );
		CollectionAssert.AreEqual( Array.Empty<string>(), ParkResumeMods.InUse( cells ).ToArray() );
		CollectionAssert.AreEqual( new[] { ModId }, ParkResumeMods.InUse( cells, umbrellas: true ).ToArray() );
		var rig = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = rig.SpawnInPark( 10, 5 );
		Assert.IsFalse( ParkResumeMods.UmbrellasInUse( new[] { ordinary }, rig ), "an ordinary object and a guest without an umbrella: not in use" );
		Assert.IsTrue( ParkResumeMods.UmbrellasInUse( new[] { ordinary, shop }, rig ), "a brolly shop" );
		guest.UmbrellaState = UmbrellaRules.StateClosed;
		Assert.IsTrue( ParkResumeMods.UmbrellasInUse( Array.Empty<ObjectCatalogEntry>(), rig ), "a guest with an umbrella, the shop long sold" );
		Assert.IsFalse( ParkResumeMods.UmbrellasInUse( Array.Empty<ObjectCatalogEntry>(), null ) );
		// the capture follows the rule, with the extension on and off, and the park without either saves the same bytes
		var plain = ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() );
		foreach ( var enabled in new[] { true, false } )
		{
			var set = UmbrellasSet( enabled );
			using var scope = OpenTpwExtensions.Use( set );
			var unused = ParkResumeMods.Capture( set, ParkResumeMods.InUse( cells ) );
			Assert.IsNull( unused, $"extension {enabled}: nothing to record in a park that does not use it" );
			Assert.IsTrue( ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() with { Mods = unused } ).SequenceEqual( plain ), $"extension {enabled}: identical bytes" );
			var used = ParkResumeMods.Capture( set, ParkResumeMods.InUse( cells, umbrellas: true ) );
			if ( enabled )
				Assert.AreEqual( ModId, used!.Single().Id );
			else
				Assert.IsNull( used, "an extension that is off records nothing" );
		}
	}

	[TestMethod]
	public void ASaveWithAnUmbrellaRecordsTheExtensionRoundTripsAndRefusesToLoadWithItOff()
	{
		var on = UmbrellasSet( true );
		var off = UmbrellasSet( false );
		var envelope = ParkResumeSaveFileTests.Envelope();
		Assert.IsTrue( envelope.Guests.Guests.Length > 0, "the envelope holds guests" );
		var carrying = envelope with { Guests = envelope.Guests with { Guests = envelope.Guests.Guests.Select( ( guest, index ) => index == 0 ? guest with { UmbrellaState = UmbrellaRules.StateClosing, UmbrellaType = 2, UmbrellaDueTurn = 400 } : guest ).ToArray() } };
		// a save with an umbrella must record the extension: it cannot hide the state from the mod check
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( carrying ) ).Message, "has an umbrella but the save does not record opentpw.umbrellas" );
		var recorded = carrying with { Mods = ParkResumeMods.Capture( on, new HashSet<string> { ModId } ) };
		ParkResumeSaveFile.ValidateShape( recorded );
		var bytes = ParkResumeSaveFile.Serialize( recorded );
		var text = System.Text.Encoding.UTF8.GetString( bytes );
		StringAssert.Contains( text, "opentpw.umbrellas" );
		StringAssert.Contains( text, "\"UmbrellaDueTurn\":400" );
		var loaded = ParkResumeSaveFile.Deserialize( bytes );
		Assert.IsTrue( ParkResumeSaveFile.Serialize( loaded ).SequenceEqual( bytes ), "byte-stable" );
		Assert.AreEqual( (UmbrellaRules.StateClosing, 2, 400L), (loaded.Guests.Guests[0].UmbrellaState, loaded.Guests.Guests[0].UmbrellaType, loaded.Guests.Guests[0].UmbrellaDueTurn) );
		ParkResumeMods.Require( loaded.Mods, on );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( loaded.Mods, off ) ).Message, "needs opentpw.umbrellas 1.0.0" );
		// the same park with another version of the extension is refused too
		var other = new[] { new ParkModRecord( ModId, "1.1.0", ModSet.NoFilesSha256 ) };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( other, on ) ).Message, "needs opentpw.umbrellas 1.1.0, not 1.0.0" );
		// a save made without the extension in use loads with it on (a used-only extension that is not recorded is fine)
		ParkResumeMods.Require( null, on );
	}
}
