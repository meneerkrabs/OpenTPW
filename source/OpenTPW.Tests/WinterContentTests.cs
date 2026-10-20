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
/// The winter extension's shipped content (docs/WINTER.md, phase W2): <c>content/winter</c> holds the winter kiosk and the gluhwein stall of each theme, the warm item sprite set (hats, earmuffs, mittens) and
/// the schema 2 anchors of the 2x kid sets (head and both hands for every land slot), made by <c>tools/winter/build-content.sh</c>. These tests load
/// the committed files through the game's own pipelines (the object catalog, the sprite readers, the ride script VM) with a content root made here, without any original game data; the extension that lists them in the game
/// (phase W3) is covered by <see cref="WinterSimulationTests"/> and <see cref="WinterWiringTests"/>.
/// </summary>
// [EXT:winter-content] OpenTPW's own shipped content; neither Theme Park World nor Theme Park Inc ships anything of it
[TestClass]
[DoNotParallelize]
public class WinterContentTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "space", "fantasy" };
	private static readonly string[] KidSets = { "BE", "BI", "CH", "FR", "KI", "SA", "SU", "TA" };
	private const string ModId = "opentpw.winter";
	private string root = null!;
	private BaseFileSystem? previousFileSystem;
	private IReadOnlyList<CatalogContentRoot>? previousRoots;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-winter-" ).FullName;
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
	internal static string Folder => folder ??= RepositoryFolder();

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

	private const int FirstId = 64010, LastId = 64499;   // 64000..64009 belong to the example lamp mod (content/mods/opentpw.example.lamp-lights)
	private static readonly string[] Shops = { "kiosk", "gluehwein" };

	private static int IdOf( int theme, int shop ) => FirstId + theme * 2 + shop;

	// ---- The shops ------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheWinterShopsHaveTheirOwnReservedIdsAndNoOtherContentRangeOverlapsThem()
	{
		var file = ModObjectsFile.Load( Path.Combine( Folder, "objects.json" ), Array.Empty<string>() );
		Assert.AreEqual( (FirstId, LastId), (file.FirstId, file.LastId), "the winter extension's own range" );
		Assert.IsTrue( file.FirstId >= CrossEditionObjects.FirstReservableId && file.LastId <= CrossEditionObjects.LastReservableId );
		Assert.IsTrue( file.LastId < PoolContent.FirstReservedId );
		var content = Path.GetFullPath( Path.Combine( Folder, ".." ) );
		var others = Directory.GetFiles( content, "objects.json", SearchOption.AllDirectories ).Select( Path.GetFullPath ).Where( path => path != Path.GetFullPath( Path.Combine( Folder, "objects.json" ) ) ).ToArray();
		Assert.IsTrue( others.Length >= 3, "the pools, the umbrellas and the mods are found" );
		foreach ( var path in others )
		{
			using var other = JsonDocument.Parse( File.ReadAllText( path ) );
			var range = other.RootElement.GetProperty( "idRange" );
			var (first, last) = (range.GetProperty( "first" ).GetInt32(), range.GetProperty( "last" ).GetInt32());
			Assert.IsTrue( last < FirstId || first > LastId, $"{path} reserves {first}..{last}, which overlaps the winter range" );
		}
		Assert.AreEqual( 8, file.Objects.Count );
		CollectionAssert.AreEqual( Enumerable.Range( FirstId, 8 ).ToArray(), file.Objects.Select( item => item.InfoId ).ToArray(), "a kiosk and a stall for each theme, in order" );
		CollectionAssert.AreEqual( Themes.SelectMany( theme => new[] { theme, theme } ).ToArray(), file.Objects.Select( item => item.Theme ).ToArray() );
		Assert.IsTrue( file.Objects.All( item => item.Category == ObjectCategory.Shop && item.Input == null && !item.Research ), "shops, our own files, no research item (decision of October 18, 2026)" );
		Assert.IsTrue( file.Objects.All( item => item.Names.Count == 11 ), "a name for each of the eleven interface languages" );
		Assert.IsTrue( file.Objects.Where( ( item, index ) => index % 2 == 0 ).All( item => item.Names["English"] == "Winter Kiosk" ) );
		Assert.IsTrue( file.Objects.Where( ( item, index ) => index % 2 == 1 ).All( item => item.Names["English"] == "Gl\u00fchwein Stall" ) );
	}

	[TestMethod]
	public void EveryThemeListsItsKioskAndItsStallWithTheDataOfThePlan()
	{
		for ( var index = 0; index < Themes.Length; index++ )
		{
			var catalog = ObjectCatalog.Load( Themes[index] );
			Assert.AreEqual( 2, catalog.Entries.Count( item => item.ModId == ModId ), "one kiosk and one stall per theme" );
			Assert.IsNull( catalog.Find( IdOf( ( index + 1 ) % 4, 0 ) ), "another theme's kiosk is not in this catalog" );
			foreach ( var (shop, name) in new[] { (0, "Winter Kiosk"), (1, "Gl\u00fchwein Stall") } )
			{
				var entry = catalog.Get( IdOf( index, shop ) );
				Assert.AreEqual( ObjectCategory.Shop, entry.Category, Themes[index] );
				Assert.AreEqual( ModId, entry.ModId );
				Assert.AreEqual( $"mod:{ModId}:", entry.Root.KeyPrefix, "a park save records and checks its files like a mod's" );
				Assert.AreEqual( name, entry.DisplayName );
				CollectionAssert.AreEqual( new[] { "**", "2*" }, entry.Shape.Rows.ToArray(), "a 2 x 2 hut whose customer cell is its lower left cell" );
				Assert.IsTrue( entry.IsChoosable && !entry.HasQueue && entry.WhichUIType == 1 && entry.IsBuildable );
				Assert.AreEqual( 1, entry.Settings.GetInt( "UsageInfo.RideHandlesSprite" ) );
				Assert.AreEqual( 0, entry.Settings.GetInt( "UsageInfo.ISIndoors", -1 ) );
				Assert.AreEqual( 0, entry.Economy.Upgrades.Single( level => level.Level == 0 ).GetInt( "CostOfResearch" ), "available from the start" );
				Assert.AreEqual( 30, entry.Economy.Upgrades.Single( level => level.Level == 0 ).GetInt( "RunningCost" ) );
				Assert.IsNotNull( entry.ScriptPath );
			}
		}
	}

	[TestMethod]
	public void TheKioskSamHoldsTheDecidedNumbersAndTheStallSamIsADrinksShopWithoutDrunkEffects()
	{
		for ( var index = 0; index < Themes.Length; index++ )
		{
			var kiosk = ObjectCatalog.Load( Themes[index] ).Get( IdOf( index, 0 ) );
			var s = kiosk.Settings;
			Assert.AreEqual( 4, s.GetInt( "UsageInfo.AppearanceEffect" ), "4 = a warm item (1 balloon, 2 costume and 3 umbrella are in use elsewhere)" );
			Assert.AreEqual( 1, s.GetInt( "UsageInfo.ShopType" ), "a gift shop's kind: 5 would count as costumes" );
			Assert.AreEqual( (25, 10, 15, 5), (kiosk.Economy.PricePerUse, kiosk.Economy.CostOfGoods, s.GetInt( "UsageInfo.HappinessEffect" ), s.GetInt( "UsageInfo.LitterEffect" )) );
			Assert.AreEqual( (0, 0, 0), (s.GetInt( "UsageInfo.ThirstEffect" ), s.GetInt( "UsageInfo.HungerEffect" ), s.GetInt( "UsageInfo.VomitEffect" )) );
			Assert.AreEqual( (5, 1000), (kiosk.InitialCapacity, kiosk.BuildCost) );
			Assert.AreEqual( (200, 150, 60, 20, 60), (s.GetInt( "WeatherModifier.SnowPercentage" ), s.GetInt( "SeasonModifier.WinterPercentage" ), s.GetInt( "SeasonModifier.SpringPercentage" ), s.GetInt( "SeasonModifier.SummerPercentage" ), s.GetInt( "SeasonModifier.AutumnPercentage" )), "sells all year: demand follows the season (winter 150, summer 20) and the snow (200)" );

			var stall = ObjectCatalog.Load( Themes[index] ).Get( IdOf( index, 1 ) );
			s = stall.Settings;
			Assert.AreEqual( (4, 3), (s.GetInt( "UsageInfo.ShopType" ), s.GetInt( "UsageInfo.SpecialIngredient" )), "a drinks shop with Ice's mechanics: the drinks challenge and price statistics apply" );
			Assert.AreEqual( 0, s.GetInt( "UsageInfo.AppearanceEffect", 0 ), "no item" );
			Assert.AreEqual( (30, 15), (stall.Economy.PricePerUse, stall.Economy.CostOfGoods) );
			Assert.AreEqual( (30, 5, 5, 15, 40), (s.GetInt( "UsageInfo.ThirstEffect" ), s.GetInt( "UsageInfo.HungerEffect" ), s.GetInt( "UsageInfo.VomitEffect" ), s.GetInt( "UsageInfo.HappinessEffect" ), s.GetInt( "UsageInfo.LitterEffect" )) );
			Assert.IsTrue( s.GetInt( "UsageInfo.VomitEffect" ) < 10, "half of the original drinks shops' 10: no drunk effects" );
			Assert.AreEqual( (1, 750), (stall.InitialCapacity, stall.BuildCost) );
			StringAssert.Contains( s["WinterInfo.IngredientLabel"]!, "Spices" );
			Assert.AreEqual( 1, s.GetInt( "WinterInfo.WarmEffectDays" ) );
			Assert.AreEqual( (250, 130, 130), (s.GetInt( "WeatherModifier.SnowPercentage" ), s.GetInt( "WeatherModifier.RainPercentage" ), s.GetInt( "WeatherModifier.StormPercentage" )) );
			Assert.AreEqual( (150, 60, 20, 60), (s.GetInt( "SeasonModifier.WinterPercentage" ), s.GetInt( "SeasonModifier.SpringPercentage" ), s.GetInt( "SeasonModifier.SummerPercentage" ), s.GetInt( "SeasonModifier.AutumnPercentage" )) );
		}
	}

	[TestMethod]
	public void EachWinterShopsModelTexturesAndScriptLoadAndRun()
	{
		for ( var index = 0; index < Themes.Length; index++ )
		{
			foreach ( var shop in new[] { 0, 1 } )
			{
				var theme = Themes[index];
				var label = $"{theme} {Shops[shop]}";
				var entry = ObjectCatalog.Load( theme ).Get( IdOf( index, shop ) );
				var prefix = shop == 0 ? "wk_" : "wg_";
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				Assert.AreEqual( ModelFileKind.Geometry, model.Kind );
				Assert.IsTrue( model.Meshes.Count >= 1, label );
				var textures = new HashSet<string>();
				foreach ( var mesh in model.Meshes )
				{
					Assert.IsNull( ObjectAssets.GetUnrenderableReason( mesh ), $"{label}: {mesh.Name}" );
					Assert.IsTrue( ObjectAssets.ConvertMesh( mesh ).Length > 0 );
					foreach ( var material in mesh.Materials )
						textures.Add( material.Name );
				}
				Assert.IsTrue( textures.Count >= 8 && textures.All( name => name.StartsWith( prefix, StringComparison.Ordinal ) ), $"{label}: {string.Join( ", ", textures )}" );
				foreach ( var name in new[] { "wall", "counter", "trim", "roof", "roof2" } )
					Assert.IsTrue( textures.Any( texture => texture.EndsWith( "_" + name, StringComparison.Ordinal ) ), $"{label}: the {name} texture" );
				foreach ( var name in textures )
				{
					var texture = ObjectAssets.ResolveTexture( entry, name );
					Assert.IsNotNull( texture, $"{label}: {name}" );
					var (data, width, height) = Texture.DecodeImage( texture!.Value.FileSystem, texture.Value.Path );
					Assert.IsTrue( width is 64 or 128 && height == width && data.Length == width * height * 4, $"{label}: {name} is {width} x {height}" );
				}
				// the footprint: 2 x 2 cells, the roof reaches 1 unit from the edge, the hut stands inside it
				var composed = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), WorldOf( model, mesh.NodeIndex ) ) ) ).ToArray();
				Assert.IsTrue( composed.All( p => p.X >= -0.01f && p.X <= 20.01f && p.Z >= -0.01f && p.Z <= 20.01f && p.Y >= -0.01f && p.Y < 20f ), $"{label}: the hut stands on its 2 x 2 cells" );
				Assert.IsTrue( composed.Min( p => p.X ) < 1.1f && composed.Max( p => p.X ) > 18.9f && composed.Max( p => p.Y ) > 11f, $"{label}: the pitched roof reaches the footprint and rises above the hut" );
				// [EXT:pools-model-inset] a walker is a sprite: at its height no wall, counter, post or hanging prop comes within 1 unit of the footprint's edge
				foreach ( var height in new[] { 1f, 2f, 3f, 4f, 5f, 6f } )
				{
					var slice = PoolsContentTests.SliceAt( model, height );
					Assert.IsNotNull( slice, $"{label}: something stands {height} units above the ground" );
					var (minX, maxX, minZ, maxZ) = slice!.Value;
					Assert.IsTrue( minX >= 1.0f && 20f - maxX >= 1.0f && minZ >= 1.0f && 20f - maxZ >= 1.0f, $"{label}: at {height} units the hut stands X {minX:F1}..{maxX:F1}, Z {minZ:F1}..{maxZ:F1}; every side keeps 1 unit from the edge" );
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
				Assert.IsTrue( released is > 60 and < 300, $"{label}: the shop serves and releases its customer within a few seconds ({released} steps of 1/60 s)" );
			}
		}
	}

	[TestMethod]
	public void EveryWinterShopsBuyPreviewShowsTheRoofTilesOverTheWholeRoofAtEveryTurn()
	{
		// The buy window's preview used to be painter sorted without a depth buffer: when it sorted back faces with the front faces, the undersides of the roof
		// slopes (trim, 0.55 below the tiles) won over one triangle of each slope quad, so the slopes showed half tiles and half trim stripes, split along their
		// diagonals. The preview is now depth tested, and the original's face test drops the undersides seen from above.
		var frame = new OpenTPW.UI.Original.UiRect( 437, 202, 360, 363 );
		for ( var index = 0; index < Themes.Length; index++ )
		{
			foreach ( var shop in new[] { 0, 1 } )
			{
				var label = $"{Themes[index]} {Shops[shop]}";
				var entry = ObjectCatalog.Load( Themes[index] ).Get( IdOf( index, shop ) );
				var icon = new OpenTPW.Hud.PreviewIcon( ObjectAssets.LoadModel( entry.FileSystem, entry.PreviewModelPath ?? entry.ModelPath ), _ => null );
				var slopes = 0;
				for ( var step = 0; step < 16; step++ )
				{
					var yaw = step * MathF.PI / 8;
					var drawn = icon.ArrangeBuyPreview( frame, yaw, 1 ).Triangles.Where( triangle => triangle.FacesViewer ).ToArray();
					var roof = drawn.Where( triangle => triangle.MaterialName.EndsWith( "_roof", StringComparison.Ordinal ) && triangle.ModelNormal.Y > 0 ).ToArray();
					// both slopes are less steep than the 45 degree tilt, so every tile triangle faces the viewer at every turn
					Assert.AreEqual( 4, roof.Length, $"{label} at {step} x 22.5 degrees: the four tile triangles of the two slopes are kept" );
					foreach ( var tile in roof )
					{
						var point = (tile.A2 + tile.B2 + tile.C2) / 3;
						var top = PreviewDepthRaster.Nearest( drawn, point );
						Assert.IsNotNull( top, label );
						Assert.IsTrue( top.Value.ModelNormal.Y >= 0, $"{label} at {step} x 22.5 degrees: the middle of a roof tile triangle shows {top.Value.MaterialName}, a face turned down (normal {top.Value.ModelNormal})" );
						slopes++;
					}
				}
				Assert.AreEqual( 64, slopes, label );
			}
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

	// ---- The warm item sprites -------------------------------------------------------------------------------------

	private static GuestSpriteAtlas LoadItems() =>
		Load( Path.Combine( Folder, "esprites", "Winter", "SPR_WI.TPC" ), "SPR_WI" );

	private static GuestSpriteAtlas Load( string tpc, string name ) =>
		new( name, new SpriteBankFile( new MemoryStream( File.ReadAllBytes( tpc ) ) ), new SpriteAnimationFile( new MemoryStream( File.ReadAllBytes( Path.ChangeExtension( tpc, ".ESP" ) ) ) ) );

	// slot -> (frames per direction, first of the slot kinds): 0..2 hats, 3..4 earmuffs, 5..7 mittens (frame 0 the right mitten, frame 1 the left)
	private static readonly (int Slot, int Frames, string Kind)[] ItemSlots =
	{
		(0, 1, "hat"), (1, 1, "hat"), (2, 1, "hat"), (3, 1, "earmuffs"), (4, 1, "earmuffs"), (5, 2, "mittens"), (6, 2, "mittens"), (7, 2, "mittens"),
	};

	[TestMethod]
	public void TheItemSetHoldsHatsEarmuffsAndMittensInAFewColourwaysOfFiveDirectionsAtTwiceTheSizeWithTheHotspotAtTheAttachPoint()
	{
		var set = LoadItems();
		Assert.AreEqual( 5 * ItemSlots.Sum( slot => slot.Frames ), set.Frames.Length, "5 directions of every slot's frames (the game mirrors the five to eight)" );
		foreach ( var (slot, frames, kind) in ItemSlots )
		{
			Assert.AreEqual( (frames, 5), (set.Animations.Animations[slot].FramesPerDirection, set.Animations.Animations[slot].Directions), $"slot {slot} ({kind})" );
			for ( var direction = 0; direction < 5; direction++ )
				for ( var f = 0; f < frames; f++ )
				{
					var frame = set.Frames[set.GetFrame( slot, direction, f )];
					var label = $"{kind} slot {slot} d{direction} f{f}";
					Assert.AreEqual( (256, 256), (frame.PixelsPerUnitX, frame.PixelsPerUnitY), $"{label}: 2x frames at 256 pixels per unit" );
					Assert.IsTrue( frame.OriginX < 0 && frame.OriginX + frame.Width > 0 && frame.OriginY < 0 && frame.OriginY + frame.Height > 0, $"{label}: the hotspot (the attach point) lies inside the frame ({frame.OriginX}, {frame.OriginY}, {frame.Width} x {frame.Height})" );
					switch ( kind )
					{
						case "hat":   // the hotspot is the top of the head: the hat rises above it and its cuff reaches a little below
							Assert.IsTrue( frame.Width is >= 18 and <= 40 && frame.Height is >= 18 and <= 40 && -frame.OriginY > frame.Height / 2, $"{label}: {frame.Width} x {frame.Height}" );
							break;
						case "earmuffs":   // the hotspot is the head centre: the cups are about a head apart in the front and back views
							Assert.IsTrue( frame.Width is >= 8 and <= 50 && frame.Height is >= 12 and <= 40, $"{label}: {frame.Width} x {frame.Height}" );
							break;
						default:   // the hotspot is the hand end point, a mitten about the size of a hand
							Assert.IsTrue( frame.Width is >= 8 and <= 24 && frame.Height is >= 10 and <= 26, $"{label}: {frame.Width} x {frame.Height}" );
							break;
					}
					Assert.IsTrue( HasPixel( set, frame ), label );
				}
		}
		foreach ( var slot in Enumerable.Range( 8, 8 ) )
			Assert.IsTrue( set.Animations.Animations[slot].IsEmpty, $"slot {slot} is not shipped" );
	}

	[TestMethod]
	public void TheColourwaysDifferTheDirectionsDifferAndTheTwoMittensAreNotTheSamePicture()
	{
		var set = LoadItems();
		string Sum( int slot, int direction, int f )
		{
			var frame = set.Frames[set.GetFrame( slot, direction, f )];
			var bytes = new List<byte>();
			for ( var y = 0; y < frame.Height; y++ )
				for ( var x = 0; x < frame.Width; x++ )
					bytes.AddRange( set.Pixels.AsSpan( ((frame.Y + y) * set.Width + frame.X + x) * 4, 4 ).ToArray() );
			return Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( bytes.ToArray() ) );
		}
		foreach ( var slots in new[] { new[] { 0, 1, 2 }, new[] { 3, 4 }, new[] { 5, 6, 7 } } )
			foreach ( var direction in Enumerable.Range( 0, 5 ) )
				Assert.AreEqual( slots.Length, slots.Select( slot => Sum( slot, direction, 0 ) ).Distinct().Count(), $"slots {string.Join( "/", slots )} d{direction}: every colourway is its own picture" );
		Assert.IsTrue( Enumerable.Range( 0, 5 ).Select( direction => Sum( 3, direction, 0 ) ).Distinct().Count() >= 3, "earmuffs look different from different sides" );
		Assert.IsTrue( Enumerable.Range( 5, 3 ).All( slot => Enumerable.Range( 0, 5 ).Any( direction => Sum( slot, direction, 0 ) != Sum( slot, direction, 1 ) ) ), "a left and a right mitten differ" );
	}

	private static bool HasPixel( GuestSpriteAtlas set, GuestAtlasFrame frame )
	{
		for ( var y = 0; y < frame.Height; y++ )
			for ( var x = 0; x < frame.Width; x++ )
				if ( set.Pixels[((frame.Y + y) * set.Width + frame.X + x) * 4 + 3] > 0 )
					return true;
		return false;
	}

	// ---- The anchors (schema 2) ------------------------------------------------------------------------------------

	private static JsonDocument Anchors() => JsonDocument.Parse( File.ReadAllText( Path.Combine( Folder, "anchors.json" ) ) );

	private static readonly (string Name, int Slot, int Frames)[] LandSlots =
	{
		("stand", 0, 1), ("walk", 1, 8), ("run", 2, 8), ("gesture-a", 4, 4), ("gesture-b", 6, 4), ("stumble", 11, 4), ("cheer", 12, 4), ("hands-on-hips", 14, 2),
	};

	[TestMethod]
	public void TheAnchorTableCoversEveryKidSetLandSlotFrameAndDirectionWithTheHeadAndBothHands()
	{
		using var document = Anchors();
		var top = document.RootElement;
		Assert.AreEqual( 2, top.GetProperty( "schema" ).GetInt32() );
		Assert.AreEqual( 256, top.GetProperty( "pixelsPerUnit" ).GetInt32(), "the pixels of the 2x kid sets" );
		Assert.AreEqual( 45.0, top.GetProperty( "elevation" ).GetDouble(), "the pools kid camera" );
		Assert.AreEqual( 5, top.GetProperty( "directions" ).GetInt32() );
		var fields = top.GetProperty( "fields" ).EnumerateArray().Select( field => field.GetString() ).ToArray();
		CollectionAssert.AreEqual( new[] { "headTopX", "headTopY", "headX", "headY", "rightHandX", "rightHandY", "rightHandAngle", "leftHandX", "leftHandY", "leftHandAngle", "visible" }, fields );
		var sets = top.GetProperty( "sets" );
		CollectionAssert.AreEqual( KidSets, sets.EnumerateObject().Select( set => set.Name ).ToArray(), "the eight Theme Park World kid sets" );
		foreach ( var set in sets.EnumerateObject() )
		{
			var slots = set.Value.GetProperty( "slots" );
			CollectionAssert.AreEqual( LandSlots.Select( slot => slot.Name ).ToArray(), slots.EnumerateObject().Select( slot => slot.Name ).ToArray(), $"{set.Name}: every land slot of the kid sets" );
			foreach ( var (slotName, slot, frames) in LandSlots )
			{
				var table = slots.GetProperty( slotName );
				Assert.AreEqual( (slot, frames), (table.GetProperty( "slot" ).GetInt32(), table.GetProperty( "frames" ).GetInt32()), $"{set.Name} {slotName}" );
				Assert.AreEqual( frames, table.GetProperty( "headYaw" ).GetArrayLength() );
				Assert.AreEqual( frames, table.GetProperty( "headPitch" ).GetArrayLength() );
				var directions = table.GetProperty( "points" ).EnumerateArray().ToArray();
				Assert.AreEqual( 5, directions.Length, $"{set.Name} {slotName}: five directions" );
				foreach ( var direction in directions )
				{
					Assert.AreEqual( frames, direction.GetArrayLength(), $"{set.Name} {slotName}: {frames} frames per direction" );
					foreach ( var record in direction.EnumerateArray() )
					{
						Assert.AreEqual( fields.Length, record.GetArrayLength() );
						var values = record.EnumerateArray().Select( value => value.GetDouble() ).ToArray();
						Assert.IsTrue( values[10] is >= 0 and <= 3 && values[10] == Math.Floor( values[10] ), "two visibility bits" );
						Assert.IsTrue( values[3] < 0 && values[1] < 0, $"{set.Name} {slotName}: the head is above the feet ({values[1]}, {values[3]})" );
						Assert.IsTrue( Math.Sqrt( Math.Pow( values[0] - values[2], 2 ) + Math.Pow( values[1] - values[3], 2 ) ) <= 13.0, "the skull's top is 0.56 units from its centre: at most 12.5 pixels at 22.2 per unit, whatever way the head is tilted" );
						if ( slotName == "stand" )
							Assert.IsTrue( values[3] - values[1] is > 6 and < 12, "standing, the top is about 9 pixels above the centre seen from 45 degrees" );
						foreach ( var angle in new[] { values[6], values[9] } )
							Assert.IsTrue( angle is >= -180 and <= 180 );
					}
				}
			}
		}
	}

	[TestMethod]
	public void TheHandAnchorsAgreeWithTheUmbrellasTableAndTheVisibilityFlagsAreNotConstant()
	{
		using var winter = Anchors();
		var umbrellas = Path.Combine( Folder, "..", "umbrellas", "anchors.json" );
		using var old = JsonDocument.Parse( File.ReadAllText( umbrellas ) );
		Assert.AreEqual( 1, old.RootElement.GetProperty( "schema" ).GetInt32(), "the umbrellas' table is still schema 1: this phase adds a separate file" );
		var visible = new HashSet<int>();
		foreach ( var set in winter.RootElement.GetProperty( "sets" ).EnumerateObject() )
		{
			foreach ( var (oldName, newName) in new[] { ("stand", "stand"), ("walk", "walk"), ("wait", "hands-on-hips") } )
			{
				var v1 = old.RootElement.GetProperty( "sets" ).GetProperty( set.Name ).GetProperty( oldName ).GetProperty( "frames" ).EnumerateArray().ToArray();
				var v2 = set.Value.GetProperty( "slots" ).GetProperty( newName ).GetProperty( "points" ).EnumerateArray().ToArray();
				for ( var direction = 0; direction < 5; direction++ )
					for ( var f = 0; f < v1[direction].GetArrayLength(); f++ )
					{
						var record = v2[direction][f];
						Assert.AreEqual( v1[direction][f][0].GetDouble(), record[4].GetDouble(), 0.06, $"{set.Name} {oldName} d{direction} f{f}: the right hand's x is the umbrellas' anchor" );
						Assert.AreEqual( v1[direction][f][1].GetDouble(), record[5].GetDouble(), 0.06, $"{set.Name} {oldName} d{direction} f{f}: and its y" );
					}
			}
			foreach ( var slot in set.Value.GetProperty( "slots" ).EnumerateObject() )
				foreach ( var direction in slot.Value.GetProperty( "points" ).EnumerateArray() )
					foreach ( var record in direction.EnumerateArray() )
						visible.Add( record[10].GetInt32() );
		}
		CollectionAssert.AreEquivalent( new[] { 0, 1, 2, 3 }, visible.ToArray(), "from some views one hand, both or neither are hidden behind the body" );
	}

	[TestMethod]
	public void TheHeadAnchorsLieInsideTheShippedKidFramesAndFollowTheMirroredDirections()
	{
		using var document = Anchors();
		var pools = Path.Combine( Folder, "..", "pools", "esprites", "Generic", "Kids" );
		foreach ( var set in document.RootElement.GetProperty( "sets" ).EnumerateObject() )
		{
			var stand = set.Value.GetProperty( "slots" ).GetProperty( "stand" ).GetProperty( "points" ).EnumerateArray().Select( direction => direction[0].EnumerateArray().Select( value => value.GetDouble() ).ToArray() ).ToArray();
			Assert.IsTrue( Math.Abs( stand[0][2] ) < 1.0 && Math.Abs( stand[4][2] ) < 1.0, $"{set.Name}: the head centre is over the feet's line in the back and front views" );
			Assert.IsTrue( Math.Abs( stand[1][2] + stand[3][2] ) < 2.0, $"{set.Name}: the side views are mirrors" );
			var file = Path.Combine( pools, $"SPR_{set.Name}.TPC" );
			if ( !File.Exists( file ) )
				continue;
			var kids = Load( file, $"SPR_{set.Name}" );
			for ( var direction = 0; direction < 5; direction++ )
			{
				var frame = kids.Frames[kids.GetFrame( GuestSpriteAtlas.StandSlot, direction, 0 )];
				Assert.AreEqual( 256, frame.PixelsPerUnitX, "the anchors are in the pixels of these frames" );
				foreach ( var (x, y) in new[] { (stand[direction][0], stand[direction][1]), (stand[direction][2], stand[direction][3]) } )
					Assert.IsTrue( x >= frame.OriginX && x <= frame.OriginX + frame.Width && y >= frame.OriginY && y <= frame.OriginY + frame.Height,
						$"{set.Name} d{direction}: the head point ({x}, {y}) lies in the standing frame ({frame.OriginX}, {frame.OriginY}, {frame.Width} x {frame.Height})" );
			}
		}
	}

	// ---- The wiring ------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheContentIsListedOnlyThroughTheWinterExtensionsOwnRoot()
	{
		CollectionAssert.AreEquivalent( new[] { "opentpw.winter", "opentpw.winter-weather", "opentpw.winter-snow", "opentpw.winter-cold", "opentpw.winter-always" }, OfficialExtensions.All.Where( extension => extension.Id.StartsWith( "opentpw.winter", StringComparison.Ordinal ) ).Select( extension => extension.Id ).ToArray(),
			"W3 registers the winter extension and the wintry Decembers option beside the settling snow (W1); W5 registers the cold rule; the always winter option joins them" );
		WinterContent.DirectoryOverride = Folder;
		try
		{
			var roots = ModContentRoots.CatalogRoots( ModSet.Defaults( OfficialExtensions.All ) );
			Assert.AreEqual( 1, roots.Count( root => root.ModId == ModId ), "a default run lists the winter root once" );
			Assert.AreEqual( 8, roots.Single( root => root.ModId == ModId ).Objects.Count( definition => definition.InfoId is >= FirstId and <= LastId ) );
			var off = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool> { [ModId] = false } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
			Assert.IsFalse( ModContentRoots.CatalogRoots( off ).Any( root => root.ModId == ModId ), "switched off: no winter root" );
		}
		finally
		{
			WinterContent.DirectoryOverride = null;
		}
		ObjectCatalog.SetContentRoots( Array.Empty<CatalogContentRoot>() );
		foreach ( var theme in Themes )
			Assert.IsFalse( ObjectCatalog.Load( theme ).Entries.Any( entry => entry.InfoId is >= FirstId and <= LastId ), $"{theme}: no winter shop in a catalog without the test's own root" );
	}
}
