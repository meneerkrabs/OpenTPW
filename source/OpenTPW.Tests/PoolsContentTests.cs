using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The pools extension's shipped content (docs/POOLS.md, "Content"): <c>content/pools</c> holds the changing rooms and the sprite sets, made by <c>tools/pools/build-content.sh</c>. These tests load
/// the committed files through the game's own pipelines (mod roots, the object catalog, the sprite readers, the ride script VM) without any original game data.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolsContentTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "space", "fantasy" };
	private string root = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-pools-" ).FullName;
		previousFileSystem = FileSystem;
		PoolContent.DirectoryOverride = RepositoryFolder();
		// a stand-in game folder (the mod pipeline needs a file system; the changing rooms' data comes from the extension's own content)
		var totem = Path.Combine( root, "game", "levels", "jungle", "rides", "totem" );
		Directory.CreateDirectory( totem );
		File.WriteAllText( Path.Combine( totem, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t100\n" );
		File.WriteAllBytes( Path.Combine( totem, "totem.MD2" ), Array.Empty<byte>() );
		FileSystem = new BaseFileSystem( Path.Combine( root, "game" ) );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		PoolContent.DirectoryOverride = null;
		FileSystem = previousFileSystem!;
		Directory.Delete( root, true );
	}

	private static string RepositoryFolder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var candidate = Path.Combine( directory.FullName, "content", "pools" );
			if ( File.Exists( Path.Combine( candidate, "objects.json" ) ) && Directory.Exists( Path.Combine( directory.FullName, "tools", "pools" ) ) )
				return candidate;
		}
		throw new FileNotFoundException( "content/pools not found above the test binaries." );
	}

	private static IDisposable Enabled( bool on, BaseFileSystem fileSystem ) => ModContentRoots.Use( PoolsTests.PoolsSet( on ), fileSystem );

	// ---- The objects ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheChangingRoomsHaveOpenTpwsOwnReservedIdsAndNoOneElsesNames()
	{
		var file = ModObjectsFile.Load( Path.Combine( PoolContent.Directory!, "objects.json" ), Array.Empty<string>() );
		Assert.AreEqual( (PoolContent.FirstReservedId, PoolContent.LastReservedId), (file.FirstId, file.LastId), "the file reserves exactly the extension's range" );
		Assert.AreEqual( 4 + 24 + 20 + 8, file.Objects.Count, "four changing rooms, six extras for each of the four themes, five water attractions for each (one of them the large spa) and two slide towers for each" );
		var rooms = file.Objects.Where( item => item.InfoId >= 65500 ).ToArray();
		CollectionAssert.AreEqual( Themes, rooms.Select( item => item.Theme ).ToArray() );
		CollectionAssert.AreEqual( new[] { 65500, 65501, 65502, 65503 }, rooms.Select( item => item.InfoId ).ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 24 ).Select( index => 65000 + index / 6 * 10 + index % 6 ).ToArray(), file.Objects.Where( item => item.InfoId < 65100 ).Select( item => item.InfoId ).ToArray(), "the extras take 65000 to 65099, six a theme" );
		CollectionAssert.AreEqual( Enumerable.Range( 65200, 20 ).ToArray(), file.Objects.Where( item => item.InfoId is >= 65200 and < 65300 ).Select( item => item.InfoId ).ToArray(), "the water attractions take 65200 to 65219, a block of four themes per kind" );
		Assert.AreEqual( file.Objects.Count, file.Objects.Select( item => item.InfoId ).Distinct().Count(), "no id twice" );
		Assert.IsTrue( file.Objects.All( item => item.Category == ObjectCategory.Feature && item.Input == null ) );
		Assert.IsTrue( file.Objects.All( item => item.InfoId is >= PoolContent.FirstReservedId and <= PoolContent.LastReservedId ) );
		Assert.IsTrue( PoolContent.FirstReservedId >= CrossEditionObjects.FirstReservableId && PoolContent.LastReservedId <= CrossEditionObjects.LastReservableId );
		Assert.AreEqual( 9, rooms[0].Names.Count, "a name for each of the nine languages" );
		Assert.AreEqual( "Changing Room", rooms[0].Names["English"] );
	}

	[TestMethod]
	public void EveryThemeListsItsChangingRoomOnlyWhileTheExtensionIsOn()
	{
		using ( Enabled( true, FileSystem ) )
		{
			for ( var index = 0; index < Themes.Length; index++ )
			{
				var entry = ObjectCatalog.Load( Themes[index] ).Get( 65500 + index );
				Assert.AreEqual( ObjectCategory.Feature, entry.Category, Themes[index] );
				Assert.AreEqual( Themes[index], entry.Theme );
				Assert.AreEqual( "opentpw.pools", entry.ModId, "a content root of the extension's own id" );
				Assert.AreEqual( "mod:opentpw.pools:", entry.Root.KeyPrefix, "a park save records and checks its files like a mod's" );
				Assert.IsTrue( PoolChangingRooms.IsChangingRoom( entry ) );
				Assert.AreEqual( "Changing Room", entry.DisplayName );
				Assert.AreEqual( new[] { "**", "2*" }.Length, entry.Shape.Height );
				CollectionAssert.AreEqual( new[] { "**", "2*" }, entry.Shape.Rows.ToArray(), "a 2 x 2 cabin whose entrance is its lower left cell" );
				Assert.IsTrue( entry.IsChoosable && !entry.HasQueue && entry.WhichUIType == 3, "a feature that guests choose, as a toilet" );
				Assert.IsTrue( entry.IsBuildable, "it is offered in the build menu" );
				Assert.AreEqual( 400, entry.BuildCost );
				Assert.AreEqual( 2, entry.InitialCapacity );
				Assert.AreEqual( 5, entry.Economy.PricePerUse );
				Assert.IsNotNull( entry.ScriptPath );
				Assert.AreEqual( 1, ObjectCatalog.Load( Themes[index] ).Entries.Count( item => item.ModId == "opentpw.pools" && PoolChangingRooms.IsChangingRoom( item ) ), "one room per theme" );
				Assert.AreEqual( 5, ObjectCatalog.Load( Themes[index] ).Entries.Count( item => item.ModId == "opentpw.pools" && PoolAttractions.KindOf( item ) != PoolAttractionKind.None ), "and its five water attractions (the large spa is the fifth)" );
				Assert.AreEqual( 6, ObjectCatalog.Load( Themes[index] ).Entries.Count( item => item.ModId == "opentpw.pools" && PoolExtras.KindOf( item ) != PoolExtraKind.None ), "and six extras" );
			}
			Assert.IsNull( ObjectCatalog.Load( "jungle" ).Find( 65501 ), "the hallow room is not a jungle object" );
		}
		using ( Enabled( false, FileSystem ) )
		{
			foreach ( var theme in Themes )
				Assert.IsFalse( ObjectCatalog.Load( theme ).Entries.Any( item => item.ModId == "opentpw.pools" ), $"{theme}: no changing room with the extension off" );
		}
		// switching the extension on again lists them again: the catalog cache does not mix the two
		using ( Enabled( true, FileSystem ) )
			Assert.IsNotNull( ObjectCatalog.Load( "space" ).Find( 65502 ) );
	}

	[TestMethod]
	public void EveryPoolObjectInTheBuyWindowHasAPreviewModelFromItsOwnRoot()
	{
		using var scope = Enabled( true, FileSystem );
		foreach ( var theme in Themes )
		{
			var items = Enum.GetValues<OpenTPW.Hud.BuildCategory>().SelectMany( new OpenTPW.Hud.OriginalBuildCatalog( ObjectCatalog.Load( theme ) ).GetItems )
				.Where( item => item.Entry?.ModId == "opentpw.pools" ).ToArray();
			Assert.IsTrue( items.Length >= 11, $"{theme}: at least the changing room, four attractions and six extras are listed ({items.Length})" );
			foreach ( var item in items )
			{
				var entry = item.Entry!;
				Assert.IsTrue( entry.IsExternal, "the pool objects come from the extension's root, not the game data" );
				Assert.AreEqual( entry.PreviewModelPath ?? entry.ModelPath, item.PreviewModel, $"{theme}: {item.Id} has a preview model (the buy window used to leave it empty)" );
				var unresolved = new List<string>();
				var icon = new OpenTPW.Hud.PreviewIcon( ObjectAssets.LoadModel( entry.FileSystem, item.PreviewModel! ), name =>
				{
					if ( ObjectAssets.ResolveTexture( entry, name ) == null )
						unresolved.Add( name );
					return null;
				} );
				Assert.IsTrue( icon.TriangleCount > 0, $"{theme}: {item.Id}" );
				CollectionAssert.AreEqual( Array.Empty<string>(), unresolved.Distinct().ToArray(), $"{theme}: {item.Id} finds every texture" );
			}
		}
	}

	[TestMethod]
	public void EachChangingRoomsModelTexturesAndScriptLoadAndRun()
	{
		using var scope = Enabled( true, FileSystem );
		for ( var index = 0; index < Themes.Length; index++ )
		{
			var theme = Themes[index];
			var entry = ObjectCatalog.Load( theme ).Get( 65500 + index );
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
			Assert.IsTrue( textures.Count >= 3 && textures.All( name => name.StartsWith( "cr_", StringComparison.Ordinal ) ), $"{theme}: {string.Join( ", ", textures )}" );
			foreach ( var name in textures )
			{
				var texture = ObjectAssets.ResolveTexture( entry, name );
				Assert.IsNotNull( texture, $"{theme}: {name}" );
				var (data, width, height) = Texture.DecodeImage( texture!.Value.FileSystem, texture.Value.Path );
				Assert.IsTrue( width is 64 or 128 && height == width && data.Length == width * height * 4, $"{theme}: {name} is {width} x {height}" );
			}
			// the footprint: 2 x 2 cells, and the cabin stands on the ground
			var composed = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), WorldOf( model, mesh.NodeIndex ) ) ) ).ToArray();
			Assert.IsTrue( composed.All( p => p.X >= -0.01f && p.X <= 20.01f && p.Z >= -0.01f && p.Z <= 20.01f && p.Y >= -0.01f && p.Y < 20f ), $"{theme}: the cabin stands on its 2 x 2 cells" );
			Assert.IsTrue( composed.Min( p => p.X ) < 2f && composed.Max( p => p.X ) > 18f && composed.Max( p => p.Z ) > 19.9f, $"{theme}: the roof and the pool-side door frame still reach the footprint" );
			AssertWalkableSidesKeepAGap( model, theme + " cabin" );
			Assert.AreEqual( 0, RideHeadSpace.Create( model, seed: 1 )?.Count ?? 0, "no head records: a guest inside is not drawn" );

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
			Assert.IsTrue( released is > 150 and < 400, $"{theme}: the cabin releases its guest after the changing time ({released} steps of 1/60 s)" );
		}
	}

	/// <summary>Where the model's surface cuts the horizontal plane at this height: the smallest and largest X and Z of the intersections of the triangle edges with it, or null when nothing reaches that high.</summary>
	internal static (float MinX, float MaxX, float MinZ, float MaxZ)? SliceAt( ModelFile model, float height )
	{
		float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
		var any = false;
		foreach ( var mesh in model.Meshes )
		{
			var world = WorldOf( model, mesh.NodeIndex );
			var points = mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), world ) ).ToArray();
			for ( var corner = 0; corner + 2 < mesh.Indices.Length; corner += 3 )
			{
				for ( var edge = 0; edge < 3; edge++ )
				{
					var a = points[mesh.Indices[corner + edge]];
					var b = points[mesh.Indices[corner + (edge + 1) % 3]];
					if ( (a.Y - height) * (b.Y - height) > 0 || a.Y == b.Y )
						continue;
					var p = a + (b - a) * ((height - a.Y) / (b.Y - a.Y));
					any = true;
					minX = Math.Min( minX, p.X ); maxX = Math.Max( maxX, p.X ); minZ = Math.Min( minZ, p.Z ); maxZ = Math.Max( maxZ, p.Z );
				}
			}
		}
		return any ? (minX, maxX, minZ, maxZ) : null;
	}

	/// <summary>
	/// [EXT:pools-model-inset] The walls of a footprint-filling model stand clear of the cell edge on the sides a path can run along (the entrance side at Z = 0 and both flanks; the pool side at Z = 20 is the water side and
	/// may reach the edge), at the height of a walker's sprite and above it up to the eaves. A walker is a camera-facing sprite at the depth of its feet and may come to 0.2 cell from the edge of a path cell, so a
	/// wall flush with the edge cuts it. Measured on the game's own shops and sideshows (docs/POOLS.md, "Model inset"): at 2 units above the ground every side of every one stands at least 0.5 units inside its footprint,
	/// the 10th percentile of the sides is 1.0 (sideshows) to 2.2 (shops), the median 2.3 (sideshows) to 4.4 (shops); the gap here is 1.0 on the entrance side (the door frame) and 1.4 or more on the flanks.
	/// </summary>
	private static void AssertWalkableSidesKeepAGap( ModelFile model, string what )
	{
		foreach ( var height in new[] { 1f, 2f, 3f, 4f, 6f } )
		{
			var slice = SliceAt( model, height );
			Assert.IsNotNull( slice, $"{what}: something stands {height} units above the ground" );
			var (minX, maxX, minZ, _) = slice!.Value;
			Assert.IsTrue( minX >= 1.0f && 20f - maxX >= 1.0f && minZ >= 1.0f, $"{what}: at {height} units the model stands X {minX:F1}..{maxX:F1} and from Z {minZ:F1}; the flanks and the entrance side keep at least 1 unit from the footprint's edge" );
		}
	}

	/// <summary>The measurement behind the pools' wall gap (docs/POOLS.md, "Model inset"): the game's own shops and sideshows stand clear of their footprint's edge at a walker's height, on every side.</summary>
	[TestMethod]
	public void TheOriginalShopsAndSideshowsKeepAtLeastHalfAUnitFromTheFootprintAtWalkerHeight()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out _ ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game." );
		ObjectCatalog.BonusDataRoot = null;
		var checkedObjects = 0;
		var shortest = float.MaxValue;
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( entry => entry.Category is ObjectCategory.Shop or ObjectCategory.Sideshow ) )
			{
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				if ( model.Kind != ModelFileKind.Geometry || model.Meshes.Count == 0 )
					continue;
				var slice = SliceAt( model, 2f );
				if ( slice == null )
					continue;
				var (minX, maxX, minZ, maxZ) = slice.Value;
				var width = entry.Shape.Width * 10f;
				var depth = entry.Shape.Height * 10f;
				var gap = Math.Min( Math.Min( minX, width - maxX ), Math.Min( minZ, depth - maxZ ) );
				shortest = Math.Min( shortest, gap );
				checkedObjects++;
			}
		}
		Assert.IsTrue( checkedObjects >= 40, $"{checkedObjects} shops and sideshows measured" );
		Assert.IsTrue( shortest >= 0.4f, $"the tightest shop or sideshow side at 2 units above the ground is {shortest:F1} units inside its footprint" );
	}

	[TestMethod]
	public void EachSlideTowersFlightAndPostsKeepTheSamePathSideGap()
	{
		using var scope = Enabled( true, FileSystem );
		for ( var index = 0; index < Themes.Length; index++ )
		{
			var theme = Themes[index];
			foreach ( var (id, kind) in new[] { (65100 + index, "flume"), (65104 + index, "tube") } )
			{
				var entry = ObjectCatalog.Load( theme ).Get( id );
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				AssertWalkableSidesKeepAGap( model, $"{theme} {kind} tower" );
				var top = SliceAt( model, 24.5f );
				Assert.IsNotNull( top, $"{theme} {kind}: the platform stands 25 units up" );
				Assert.IsTrue( top!.Value.MinX < 0.1f && top.Value.MaxX > 19.9f && top.Value.MaxZ > 19.9f, "the platform still reaches the whole footprint" );
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

	[TestMethod]
	public void TheVisitorBridgeOfAChangingRoomKnowsItIsOneOnlyWhileTheExtensionIsOn()
	{
		ObjectCatalogEntry entry;
		using ( Enabled( true, FileSystem ) )
		{
			entry = ObjectCatalog.Load( "jungle" ).Get( 65500 );
			var bridge = OriginalObjectRuntime.CreateVisitorBridge( entry, 5 );
			Assert.IsTrue( bridge.IsChangingRoom );
			Assert.AreEqual( RideVisitorKind.Facility, bridge.Kind );
			Assert.AreEqual( 5, bridge.Price );
			Assert.IsFalse( bridge.ProvidesRelief, "no relief: only a toilet gives that" );
		}
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.IsFalse( OriginalObjectRuntime.CreateVisitorBridge( entry, 5 ).IsChangingRoom, "a park loaded with the extension off has no swimmers' room" );
	}

	// ---- The pool side -----------------------------------------------------------------------------------------------

	[TestMethod]
	public void ThePoolSideCellsLieBeyondTheBackOfTheCabinForEveryRotation()
	{
		var shape = new ObjectShape( new[] { "**", "2*" } );
		// rotation 0, anchored at (10, 10): the footprint (u, v) -> (10 + u, 10 + v), v counted from the entrance's row; the entrance faces -v
		var expected = new Dictionary<int, (int X, int Y)[]>
		{
			[0] = new[] { (10, 12), (11, 12) },      // north in the cell map's growing y: the cabin's back is +v
			[90] = new[] { (12, 10), (12, 9) },
			[180] = new[] { (10, 8), (9, 8) },
			[270] = new[] { (8, 10), (8, 11) }
		};
		foreach ( var (rotation, cells) in expected )
		{
			var candidates = PoolChangingRooms.AccessCandidates( shape, 10, 10, rotation );
			CollectionAssert.AreEquivalent( cells, candidates.ToArray(), $"rotation {rotation}" );
			var footprint = ObjectFootprint.GetCells( shape, 10, 10, rotation ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
			Assert.IsTrue( candidates.All( cell => !footprint.Contains( cell ) ), "outside the cabin" );
			var entrance = ObjectFootprint.GetAccessPoints( shape, 10, 10, rotation ).Single( point => point.Kind == ObjectCellKind.Entrance );
			var (outsideX, outsideY) = (entrance.OutsideX - entrance.X, entrance.OutsideY - entrance.Y);
			foreach ( var cell in candidates )
			{
				var inside = (X: cell.X - outsideX * -1, Y: cell.Y - outsideY * -1);
				_ = inside;
				// each candidate is edge-adjacent to a footprint cell, on the side opposite the entrance's outside cell
				Assert.IsTrue( footprint.Contains( (cell.X + outsideX, cell.Y + outsideY) ), $"rotation {rotation}: {cell} touches the back row" );
			}
		}
		Assert.AreEqual( 0, PoolChangingRooms.AccessCandidates( new ObjectShape( new[] { "**" } ), 0, 0, 0 ).Count, "a shape without an entrance has no pool side" );
	}

	[TestMethod]
	public void AChangingRoomIsPlacedOnlyBesideWaterAndOnlyWhileTheExtensionIsOn()
	{
		var cells = new ParkCellMap( 30, 30 );
		var objects = new ParkObjects( ObjectCatalog.Load( "jungle" ), new PoolsTests.FlatGrid( 30, 30 ) );
		ObjectCatalogEntry entry;
		using ( Enabled( true, FileSystem ) )
			entry = ObjectCatalog.Load( "jungle" ).Get( 65500 );
		objects.PlacementRule = ( item, x, y, rotation ) => PoolChangingRooms.PlacementRule( item, cells, x, y, rotation );
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) ) )
		{
			Assert.AreEqual( OriginalPlacementResult.NeedsPool, objects.Check( entry, 10, 10, 0 ), "no water beside it" );
			cells.SetType( 11, 12, ParkCellTypes.PoolWater );
			Assert.AreEqual( OriginalPlacementResult.Allowed, objects.Check( entry, 10, 10, 0 ), "water on its back (the second back cell)" );
			Assert.AreEqual( OriginalPlacementResult.NeedsPool, objects.Check( entry, 10, 10, 90 ), "turned, its back is elsewhere" );
			Assert.AreEqual( OriginalPlacementResult.NeedsPool, objects.Check( entry, 10, 10, 180 ) );
			cells.SetType( 8, 11, ParkCellTypes.PoolWater );
			Assert.AreEqual( OriginalPlacementResult.Allowed, objects.Check( entry, 10, 10, 270 ), "rotation 270: back cells (8, 10) and (8, 11)" );
			cells.SetType( 10, 9, ParkCellTypes.PoolWater );   // water in front, beside the entrance: not the back
			Assert.AreEqual( OriginalPlacementResult.NeedsPool, objects.Check( entry, 20, 20, 0 ), "water elsewhere does not count" );
			// any other object is not asked
			var other = ObjectCatalog.Load( "jungle" ).Entries.FirstOrDefault( item => !PoolChangingRooms.IsChangingRoom( item ) && item.Shape.Entrances.Any() );
			if ( other != null )
				Assert.IsNull( PoolChangingRooms.PlacementRule( other, cells, 20, 20, 0 ) );
		}
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.IsNull( PoolChangingRooms.PlacementRule( entry, cells, 20, 20, 0 ), "with the extension off the rule has no opinion" );
	}

	// ---- The swimmers' sprites -----------------------------------------------------------------------------------------

	[TestMethod]
	public void TheSwimSpriteSetsOfTheEightKidSetsHoldTheWholeSwimwearTableAtTwiceTheSize()
	{
		var sets = PoolSwimSprites.Load();
		CollectionAssert.AreEqual( new[] { "SPR_BE", "SPR_BI", "SPR_CH", "SPR_FR", "SPR_KI", "SPR_SA", "SPR_SU", "SPR_TA" }, sets.Select( set => set.Name ).ToArray(), "one per kid set, in the kids' name order, so set n is for guests of type n" );
		var total = PoolSwimSlots.FramesPerDirection.Sum() * 5;
		foreach ( var set in sets )
		{
			Assert.AreEqual( total, set.Frames.Length, $"{set.Name}: 14 slots, five directions each" );
			for ( var slot = 0; slot < PoolSwimSlots.FramesPerDirection.Count; slot++ )
			{
				var count = PoolSwimSlots.FramesPerDirection[slot];
				Assert.AreEqual( count == 0 ? 1 : count, set.FramesPerDirection( slot ), $"{set.Name} slot {slot}" );
				Assert.AreEqual( count == 0, set.Animations.Animations[slot].IsEmpty, $"{set.Name} slot {slot} is {(count == 0 ? "empty (run, spare)" : "filled")}" );
				for ( var direction = 0; direction < (count == 0 ? 0 : 5); direction++ )
					for ( var step = 0; step < count; step++ )
					{
						var frame = set.Frames[set.GetFrame( slot, direction, step )];
						// drawn at 256 pixels per unit: twice the pixels of a 1x frame (a standing frame is 17 to 24 x 31 to 39 px at 1x), at most 192 x 192 at 2x
						Assert.AreEqual( (256, 256), (frame.PixelsPerUnitX, frame.PixelsPerUnitY), $"{set.Name} slot {slot}" );
						Assert.IsTrue( frame.Width > 8 && frame.Height > 6 && frame.Width <= 192 && frame.Height <= 192, $"{set.Name} slot {slot} d{direction} f{step}: {frame.Width} x {frame.Height}" );
						Assert.IsTrue( frame.OriginX <= 0 && frame.OriginX + frame.Width >= 0 && frame.OriginY < 0 && frame.OriginY + frame.Height >= -4, $"{set.Name} slot {slot} d{direction} f{step}: the hotspot (the feet: within two 1x pixels of the lowest sole in a stride; or the torso) lies in the frame ({frame.OriginX}, {frame.OriginY}, {frame.Width} x {frame.Height})" );
					}
			}
			// every frame draws something (an opaque pixel)
			Assert.IsTrue( set.Frames.All( frame => HasPixel( set, frame ) ), set.Name );
			Assert.IsTrue( set.Width >= 1024 && set.Height >= 1 && set.Frames.All( frame => frame.X + frame.Width <= set.Width && frame.Y + frame.Height <= set.Height ), $"{set.Name}: {set.Width} x {set.Height}" );
		}
	}

	[TestMethod]
	public void TheStandingAndWalkingSwimwearFramesKeepTheWorldSizeOfTheOnesTheyStandBesides()
	{
		// At 256 pixels per unit a 2x frame is drawn at half the world size per pixel: a standing frame is as tall in the world as a 1x kid (about 40 px at 128).
		foreach ( var set in PoolSwimSprites.Load() )
		{
			var stand = set.Frames[set.GetFrame( PoolSwimSlots.Stand, 4, 0 )];
			var world = stand.Height * stand.ScaleY;
			Assert.IsTrue( world is > 30 and < 48, $"{set.Name}: standing frame {stand.Height} px at {stand.PixelsPerUnitY} per unit is {world} 1x pixels tall" );
		}
	}

	/// <summary>A one-frame stand-in for a set of the game (only its name matters to the replacement rule).</summary>
	private static GuestSpriteAtlas GameSet( string name )
	{
		var set = new SpriteSetBuilder( "SPR_ZZ" ) { FullDetail = false }
			.AddAnimation( 0, new[] { new[] { new SpriteRgbaFrame( 1, 1, new byte[] { 1, 2, 3, 255 }, 0, 0 ) } } ).Build();
		return new GuestSpriteAtlas( name, new SpriteBankFile( new MemoryStream( set.Files[".TPC"] ) ), new SpriteAnimationFile( new MemoryStream( set.Files[".ESP"] ) ) );
	}

	internal static List<GuestSpriteAtlas> GameKids( params string[] names ) => names.Select( GameSet ).ToList();
	internal static readonly string[] TpwKidNames = { "SPR_BE", "SPR_BI", "SPR_CH", "SPR_FR", "SPR_KI", "SPR_SA", "SPR_SU", "SPR_TA" };

	[TestMethod]
	public void TheShippedKidAndHeadSetsReplaceTheGameOnesOnlyWhileTheExtensionIsOn()
	{
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.IsNull( PoolGuestSprites.LoadReplacement( GameKids( TpwKidNames ) ), "off: the game's own kid and head sets are used" );
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var replacement = PoolGuestSprites.LoadReplacement( GameKids( TpwKidNames ) )!;
		Assert.IsNotNull( replacement );
		CollectionAssert.AreEqual( TpwKidNames, replacement.Kids.Select( set => set.Name ).ToArray(), "one set per kid type, in the game's name order" );
		foreach ( var set in replacement.Kids )
		{
			// only the slots the land renderers draw ship (0 stand, 1 walk, 14 wait; the others, 2, 4, 6, 11 and 12, are empty and fall back to standing), 5 directions, 2x
			Assert.AreEqual( (1 + 8 + 2) * 5, set.Frames.Length, set.Name );
			foreach ( var (slot, count) in new[] { (0, 1), (1, 8), (14, 2) } )
				Assert.AreEqual( (count, 5), (set.Animations.Animations[slot].FramesPerDirection, set.Animations.Animations[slot].Directions), $"{set.Name} slot {slot}" );
			foreach ( var slot in new[] { 2, 4, 6, 11, 12, 15 } )
				Assert.IsTrue( set.Animations.Animations[slot].IsEmpty, $"{set.Name} slot {slot} is not shipped" );
			Assert.AreEqual( set.GetFrame( 0, 2, 0 ), set.GetFrame( 12, 2, 0 ), "an empty slot draws the standing frame" );
			Assert.IsTrue( set.Frames.All( frame => frame.PixelsPerUnitX == 256 && frame.PixelsPerUnitY == 256 ), set.Name );
			Assert.IsTrue( set.Frames.All( frame => HasPixel( set, frame ) ), set.Name );
			var walk = set.Frames[set.GetFrame( GuestSpriteAtlas.WalkSlot, 2, 0 )];
			Assert.IsTrue( walk.OriginY < 0 && -walk.OriginY <= walk.Height && -walk.OriginY >= walk.Height - 12, $"{set.Name}: the hotspot is at the feet" );
		}
		CollectionAssert.AreEqual( TpwKidNames, replacement.Heads.Select( set => set.Name ).ToArray() );
		foreach ( var set in replacement.Heads )
		{
			Assert.AreEqual( (8, 7), (set.Animations.Animations[0].FramesPerDirection, set.Animations.Animations[0].Directions), $"{set.Name}: 8 azimuths by 7 elevation rows" );
			Assert.AreEqual( 56, set.Frames.Length, set.Name );
			Assert.IsTrue( set.Frames.All( frame => frame.PixelsPerUnitX == 256 && HasPixel( set, frame ) ), set.Name );
		}
	}

	[TestMethod]
	public void TheLandGuestsSwitchIsItsOwnPresentationOnlyExtensionOnByDefault()
	{
		Assert.IsTrue( OfficialExtensions.PoolGuestArt.DefaultEnabled && !OfficialExtensions.PoolGuestArt.AffectsSimulation && OfficialExtensions.PoolGuestArt.PlayerSelectable );
		var off = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.Pools.Id] = true, [OfficialExtensions.PoolGuestArt.Id] = false } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		using var scope = OpenTpwExtensions.Use( off );
		Assert.IsTrue( FeatureGates.Pools && !FeatureGates.PoolGuestArt );
		Assert.IsNull( PoolGuestSprites.LoadReplacement( GameKids( TpwKidNames ) ), "the original guests on land; swimmers use the swim sets whatever this says (PoolSwimSprites)" );
		Assert.AreEqual( 8, PoolSwimSprites.Load().Count );
	}

	[TestMethod]
	public void TheShippedAtlasSizesArePinnedSoTheMemoryFiguresInTheDocsCannotDrift()
	{
		static string Sizes( IEnumerable<GuestSpriteAtlas> sets ) => string.Join( " ", sets.Select( set => $"{set.Width}x{set.Height}" ) );
		var replacement = PoolGuestSprites.LoadReplacement( GameKids( TpwKidNames ) );
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		replacement = PoolGuestSprites.LoadReplacement( GameKids( TpwKidNames ) )!;
		var swim = PoolSwimSprites.Load();
		var kids = Sizes( replacement.Kids );
		var heads = Sizes( replacement.Heads );
		var swimSizes = Sizes( swim );
		long Bytes( IEnumerable<GuestSpriteAtlas> sets ) => sets.Sum( set => (long)set.Width * set.Height * 4 );
		Assert.AreEqual( "1024x256 1024x256 1024x256 1024x256 1024x256 1024x256 1024x256 1024x256", kids );
		Assert.AreEqual( "1024x256 1024x256 1024x512 1024x256 1024x512 1024x256 1024x512 1024x256", heads );
		Assert.AreEqual( "1024x2048 1024x2048 1024x2048 1024x2048 1024x2048 1024x2048 1024x2048 1024x2048", swimSizes );
		const long MiB = 1024 * 1024;
		Assert.AreEqual( (8 * MiB, 11 * MiB, 64 * MiB), (Bytes( replacement.Kids ), Bytes( replacement.Heads ), Bytes( swim )), "RGBA8 bytes of the kid, head and swim atlases (docs/POOLS.md): 83 MiB, about 110 MiB on the GPU with mip levels" );
	}

	[TestMethod]
	public void AThemeParkIncGameAnIncompleteReplacementAndAMissingFolderLeaveTheGameSets()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		Assert.IsNull( PoolGuestSprites.LoadReplacement( GameKids( "SPR_BD", "SPR_GB", "SPR_GK", "SPR_KB" ) ), "a Theme Park Inc park has four kid sets of other names: the Theme Park World sets must not replace them (Type % 8)" );
		Assert.IsNull( PoolGuestSprites.LoadReplacement( GameKids() ), "no game kid sets, nothing to replace" );
		var folder = Path.Combine( root, "pools" );
		var source = PoolContent.Directory!;
		void CopySet( string kind, string name )
		{
			Directory.CreateDirectory( Path.Combine( folder, "esprites", "Generic", kind ) );
			foreach ( var extension in new[] { ".ESP", ".TPC" } )
				File.Copy( Path.Combine( source, "esprites", "Generic", kind, name + extension ), Path.Combine( folder, "esprites", "Generic", kind, name + extension ), true );
		}
		PoolContent.DirectoryOverride = folder;
		foreach ( var name in TpwKidNames )
			CopySet( "Kids", name );
		Assert.IsNull( PoolGuestSprites.LoadReplacement( GameKids( TpwKidNames ) ), "the kids are complete but there are no heads: both or neither" );
		CopySet( "Kidsheads", "SPR_BE" );
		Assert.IsNull( PoolGuestSprites.LoadReplacement( GameKids( TpwKidNames ) ), "one head set of eight" );
		foreach ( var name in TpwKidNames )
			CopySet( "Kidsheads", name );
		Assert.IsNotNull( PoolGuestSprites.LoadReplacement( GameKids( TpwKidNames ) ), "both complete" );
		File.Delete( Path.Combine( folder, "esprites", "Generic", "Kids", "SPR_TA.TPC" ) );
		Assert.IsNull( PoolGuestSprites.LoadReplacement( GameKids( TpwKidNames ) ), "seven kid sets of eight" );
		PoolContent.DirectoryOverride = Path.Combine( root, "nowhere" );
		Assert.IsNull( PoolGuestSprites.LoadReplacement( GameKids( TpwKidNames ) ) );
	}

	[TestMethod]
	public void TheWebProjectDoesNotEmbedTheSpriteSets()
	{
		var project = Path.Combine( RepositoryFolder(), "..", "..", "source", "OpenTPW.Web", "OpenTPW.Web.csproj" );
		var embed = System.Xml.Linq.XDocument.Load( project ).Descendants( "EmbeddedResource" ).Single( item => ( (string?)item.Attribute( "Include" ) ?? "" ).Contains( "content/pools" ) );
		Assert.AreEqual( "../../content/pools/esprites/**/*", (string?)embed.Attribute( "Exclude" ), "the 2x sets (about 9 MB) stay out of the startup assembly: the page fetches them on demand (PoolSpriteDelivery)" );
	}

	private static bool HasPixel( GuestSpriteAtlas set, GuestAtlasFrame frame )
	{
		for ( var y = 0; y < frame.Height; y++ )
			for ( var x = 0; x < frame.Width; x++ )
				if ( set.Pixels[((frame.Y + y) * set.Width + frame.X + x) * 4 + 3] > 0 )
					return true;
		return false;
	}

	[TestMethod]
	public void TheShippedContentIsSmallAndFreeOfOriginalGameFiles()
	{
		var folder = PoolContent.Directory!;
		var files = Directory.EnumerateFiles( folder, "*", SearchOption.AllDirectories ).ToArray();
		var sprites = files.Where( file => file.Replace( '\\', '/' ).Contains( "/esprites/" ) ).ToArray();
		var bridges = files.Where( file => file.Replace( '\\', '/' ).Contains( "/bridge/" ) ).ToArray();
		Assert.IsTrue( bridges.Sum( file => new FileInfo( file ).Length ) < 600_000, "the original Blender bridge mesh and procedural textures have a separate startup budget" );
		Assert.IsTrue( files.Except( sprites ).Except( bridges ).Sum( file => new FileInfo( file ).Length ) < 2_600_000, "the objects (with the slide towers) and the sprite manifest (what the browser build embeds) stay small" );
		Assert.IsTrue( sprites.Sum( file => new FileInfo( file ).Length ) < 9_500_000, "the 2x sprite sets (shipped on the desktop, fetched on demand in the browser) have a budget too: the kids ship only the slots that are drawn" );
		Assert.IsFalse( files.Any( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) || file.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) ), "no original archive or texture format file" );
		Assert.IsTrue( files.All( file => Path.GetFileName( file ).Length <= 24 ) );
		// the swim sets carry no full-detail bank: only the .TPC is read (docs/CONTENT-KIT.md)
		Assert.IsFalse( files.Any( file => file.EndsWith( ".FPC", StringComparison.OrdinalIgnoreCase ) ) );
	}

	// ---- Saves ---------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void ASaveHoldsTheExtensionsAssetKeysOfThePlacedObjectsOnly()
	{
		using var scope = Enabled( true, FileSystem );
		var catalog = ObjectCatalog.Load( "jungle" );
		var room = catalog.Get( 65500 );
		var manifest = new Dictionary<string, string>
		{
			["data:/levels/jungle/rides/totem/totem.sam"] = new string( 'a', 64 ),
			["mod:opentpw.pools:/objects/jungle/cabin/Cabin.sam"] = new string( 'b', 64 ),
			["mod:opentpw.pools:/objects/jungle/cabin/cabin.MD2"] = new string( 'e', 64 ),
			["mod:opentpw.pools:/objects/jungle/cabin2/Cabin.sam"] = new string( 'f', 64 ),
			["mod:community.other:/objects/x/x.sam"] = new string( 'c', 64 )
		};
		var none = ParkResumeMods.AssetsForUse( manifest, Array.Empty<ObjectCatalogEntry>() );
		CollectionAssert.AreEquivalent( new[] { "data:/levels/jungle/rides/totem/totem.sam", "mod:community.other:/objects/x/x.sam" }, none.Keys.ToArray(), "nothing of the extension placed: none of its files, every other key stays" );
		var placed = ParkResumeMods.AssetsForUse( manifest, new[] { room } );
		CollectionAssert.AreEquivalent( new[] { "data:/levels/jungle/rides/totem/totem.sam", "mod:community.other:/objects/x/x.sam", "mod:opentpw.pools:/objects/jungle/cabin/Cabin.sam", "mod:opentpw.pools:/objects/jungle/cabin/cabin.MD2" },
			placed.Keys.ToArray(), "the placed room's files only; a folder that merely starts with its name is another object" );
		var plain = new Dictionary<string, string> { ["data:/a"] = "x" };
		Assert.AreSame( plain, ParkResumeMods.AssetsForUse( plain, Array.Empty<ObjectCatalogEntry>() ), "a manifest without the extension's keys is returned untouched" );
		// the save's shape rule: a mod: key must name a recorded mod, so the manifest without them passes without the record and one with them needs it
		Assert.IsNull( ParkResumeMods.ShapeProblem( null, none.Where( pair => pair.Key.StartsWith( "mod:opentpw", StringComparison.Ordinal ) ).ToDictionary( pair => pair.Key, pair => pair.Value ) ) );
		StringAssert.Contains( ParkResumeMods.ShapeProblem( null, manifest )!, "mod opentpw.pools, which the save does not record" );
		Assert.IsNotNull( ParkResumeMods.ShapeProblem( new[] { new ParkModRecord( "community.other", "1.0.0", new string( 'd', 64 ) ) }, manifest ), "recording another mod does not cover the extension's keys" );
		Assert.IsNull( ParkResumeMods.ShapeProblem( new[] { new ParkModRecord( "opentpw.pools", "1.0.0", ModSet.NoFilesSha256 ), new ParkModRecord( "community.other", "1.0.0", new string( 'd', 64 ) ) }, manifest ) );
	}

	private static ParkResumeSnapshot WithRoomsPlaced( ParkResumeSnapshot snapshot, IReadOnlyDictionary<string, string> assets, params int[] infoIds )
	{
		var template = snapshot.Layout.Objects[0];
		var objects = infoIds.Select( ( infoId, index ) => template with { InfoId = infoId, AttractionId = 100 + index } ).ToList();
		return snapshot with { AssetHashes = assets, Layout = snapshot.Layout with { Objects = objects } };
	}

	[TestMethod]
	public void ASaveWithPoolCellsMadeBeforeTheRoomsContentExistedStillLoads()
	{
		using var scope = Enabled( true, FileSystem );
		var catalog = ObjectCatalog.Load( "jungle" );
		var current = ParkResumeAssetManifest.Capture( catalog, "jungle", easy: false );
		var roomKeys = current.Keys.Where( key => key.StartsWith( "mod:opentpw.pools:", StringComparison.Ordinal ) ).ToArray();
		Assert.IsTrue( roomKeys.Length > 0, "the park's manifest lists the extension's room" );
		// a P1 save: pool cells, no room, a manifest without any of the room's keys (it did not exist then)
		var saved = current.Where( pair => !roomKeys.Contains( pair.Key ) ).ToDictionary( pair => pair.Key, pair => pair.Value );
		var snapshot = WithRoomsPlaced( ParkResumeSaveFileTests.Envelope(), saved );
		snapshot = snapshot with { Layout = snapshot.Layout with { Objects = new List<ParkLayoutSnapshot.PlacedObjectData>() } };
		ParkResumeSaveFile.RequireAssets( snapshot, catalog, current );
		// the same save with later additions to the extension (a key of an object the catalog does not have yet) still loads
		ParkResumeSaveFile.RequireAssets( snapshot, catalog, new Dictionary<string, string>( current ) { ["mod:opentpw.pools:/objects/jungle/slide/Slide.sam"] = new string( '9', 64 ) } );
	}

	[TestMethod]
	public void ASaveWithAPlacedRoomRequiresTheRoomsKeysAndHashes()
	{
		using var scope = Enabled( true, FileSystem );
		var catalog = ObjectCatalog.Load( "jungle" );
		var current = ParkResumeAssetManifest.Capture( catalog, "jungle", easy: false );
		var roomKeys = current.Keys.Where( key => key.StartsWith( "mod:opentpw.pools:", StringComparison.Ordinal ) && key.Contains( "/jungle/cabin/", StringComparison.Ordinal ) ).ToArray();
		var withRoom = WithRoomsPlaced( ParkResumeSaveFileTests.Envelope(), current, 65500 );
		ParkResumeSaveFile.RequireAssets( withRoom, catalog, current );
		// a save that places the room but holds none of its keys is refused: the park provides them
		var withoutKeys = withRoom with { AssetHashes = current.Where( pair => !roomKeys.Contains( pair.Key ) ).ToDictionary( pair => pair.Key, pair => pair.Value ) };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.RequireAssets( withoutKeys, catalog, current ) ).Message, "the save does not expect" );
		// a room whose files changed is refused
		var changed = new Dictionary<string, string>( current ) { [roomKeys[0]] = new string( '0', 64 ) };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.RequireAssets( withRoom, catalog, changed ) ).Message, "hashes to" );
		// a room whose file is gone is refused
		var missing = current.Where( pair => pair.Key != roomKeys[0] ).ToDictionary( pair => pair.Key, pair => pair.Value );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.RequireAssets( withRoom, catalog, missing ) ).Message, "do not provide" );
	}

	[TestMethod]
	public void ARoomWithoutPoolCellsStillCountsAsUsingTheExtension()
	{
		var empty = new ParkCellMap( 20, 10 );
		CollectionAssert.AreEqual( Array.Empty<string>(), ParkResumeMods.InUse( empty ).ToArray() );
		CollectionAssert.AreEqual( new[] { "opentpw.pools" }, ParkResumeMods.InUse( empty, poolsObjects: true ).ToArray(), "a changing room names the extension even if its pool cells are gone" );
		using var scope = Enabled( true, FileSystem );
		var entry = ObjectCatalog.Load( "jungle" ).Get( 65500 );
		Assert.IsTrue( ParkResumeMods.IsPoolsObject( entry ) );
		Assert.IsFalse( ParkResumeMods.IsPoolsObject( ObjectCatalog.Load( "jungle" ).Entries.First( item => item.ModId == null ) ) );
	}

	// ---- The whole chain: a real room, a real script, a pool ------------------------------------------------------------

	[TestMethod]
	public void AGuestChangesInTheShippedRoomSwimsAndWalksBackOut()
	{
		using var scope = Enabled( true, FileSystem );
		var entry = ObjectCatalog.Load( "jungle" ).Get( 65500 );
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 0, true );
		// the cabin at (3, 1) (rotation 0): footprint (3..4, 1..2), its entrance the lower-left cell, so its front is the path row; the pool lies at y 3 to 7
		var placeX = 3;
		var points = ObjectFootprint.GetAccessPoints( entry.Shape, placeX, 1, 0 ).ToArray();
		var cabinCells = ObjectFootprint.GetCells( entry.Shape, placeX, 1, 0 ).ToArray();
		grid.Cells.WriteFootprint( cabinCells, ParkCellMap.CellWord( placeX, 1 ), 0 );
		var candidates = PoolChangingRooms.AccessCandidates( entry.Shape, placeX, 1, 0 );
		for ( var y = 4; y <= 9; y++ )
			for ( var x = 2; x <= 9; x++ )
				grid.Cells.SetType( x, y, ParkCellTypes.PoolWater );
		foreach ( var (x, y) in candidates )
			grid.Cells.SetType( x, y, ParkCellTypes.PoolWater );
		// the placement lays the cell outside the entrance as a path, linked to the entrance (ObjectAccessCells.Lay), and the level registers the room with the cells it resolves
		ObjectAccessCells.Lay( grid.Cells, points, ParkCellMap.CellWord( placeX, 1 ), hasQueue: false );
		var ride = OriginalObjectRuntime.CreateVisitorBridge( entry, 1 );
		var entrance = points.Single( point => point.Kind == ObjectCellKind.Entrance );
		var outside = (X: entrance.OutsideX, Y: entrance.OutsideY);
		Assert.IsTrue( grid.IsWalkable( outside.X, outside.Y ), $"the entrance's outside cell {outside} is a path" );
		ride.EntranceCell = outside;
		ride.ExitCell = outside;
		ride.QueueFrontCell = outside;
		ride.QueueEntranceDirection = GuestPathGrid.DirectionBetween( outside.X, outside.Y, entrance.X, entrance.Y );
		ride.HasCells = true;
		ride.PoolAccessCandidates = candidates;
		var effects = new VisitorRideScriptEffects( ride, UnimplementedRideScriptEffects.Instance );
		using var scriptStream = entry.FileSystem.OpenRead( entry.ScriptPath! );
		var script = new RideVM( new RideScriptFile( scriptStream ), new RideVMOptions { Effects = effects, Seed = 3 } );
		script[RideVariables.VAR_CAPACITY] = entry.InitialCapacity;
		ride.Attach( script, () => true );
		var settings = GuestTests.CreateSettings();
		var simulation = new GuestSimulation( grid, settings, 7 ) { ArrivalsEnabled = false };
		simulation.Register( ride );
		const float tick = 1f / FixedStepClock.TicksPerSecond;
		script.Advance( tick );

		var guest = simulation.SpawnInPark( 1, 0 );
		guest.Happiness = 60;
		guest.AttractionId = 1;
		guest.State = GuestState.GoingToRide;
		var seen = new List<GuestState>();
		var inside = true;
		var region = simulation.SwimRegionOf( ride );
		Assert.IsNotNull( region );
		for ( var step = 0; step < 60 * 400; step++ )
		{
			simulation.Tick( tick );
			ride.HostStep();
			script.Advance( tick );
			ride.WalkUpdate();
			Assert.AreNotEqual( RideVMState.Faulted, script.State, script.FaultMessage );
			guest.Hunger = guest.Thirst = guest.Toilet = 0;
			if ( seen.Count == 0 || seen[^1] != guest.State )
				seen.Add( guest.State );
			if ( guest.IsSwimming )
				inside &= region!.IsWater( guest.Navigation.X, guest.Navigation.Y );
			if ( seen.Contains( GuestState.ChangingOut ) && guest.State is GuestState.DoingNothing or GuestState.WalkingAround )
				break;
		}
		Assert.IsTrue( inside, "every stroke was in the water" );
		var order = seen.Where( state => state is GuestState.Using or GuestState.Swimming or GuestState.ClimbingOut or GuestState.ChangingOut ).ToArray();
		CollectionAssert.AreEqual( new[] { GuestState.Using, GuestState.Swimming, GuestState.ClimbingOut, GuestState.ChangingOut }, order, string.Join( " > ", seen.Take( 30 ) ) );
		Assert.IsTrue( seen[^1] is GuestState.DoingNothing or GuestState.WalkingAround, string.Join( " > ", seen.Take( 30 ) ) );
		Assert.IsTrue( grid.IsWalkable( guest.CellX, guest.CellY ) );
		Assert.AreEqual( 1, ride.ReleasedTotal, "the room counted its visit" );
	}
}
