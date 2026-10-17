using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The slide towers the pools extension ships (docs/POOLS.md, "Water slides"): eight objects, a flume and a tube tower for each theme, made by <c>tools/pools/build-content.sh</c> from
/// <c>tools/blender/opentpw_slide_tower.py</c>. They load through the game's own pipelines with no original game data.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolSlideContentTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "space", "fantasy" };
	private string root = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-slides-" ).FullName;
		previousFileSystem = FileSystem;
		PoolContent.DirectoryOverride = RepositoryFolder();
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

	private static IDisposable Enabled( bool on ) => ModContentRoots.Use( PoolsTests.PoolsSet( on ), FileSystem );

	private static int IdOf( string theme, PoolSlideProfile profile ) => 65100 + (profile == PoolSlideProfile.Tube ? 4 : 0) + Array.IndexOf( Themes, theme );

	[TestMethod]
	public void TheEightTowersHaveOpenTpwsOwnIdsInTheSlideRangeAndNamesInEveryLanguage()
	{
		var file = ModObjectsFile.Load( Path.Combine( PoolContent.Directory!, "objects.json" ), Array.Empty<string>() );
		var towers = file.Objects.Where( item => item.InfoId is >= 65100 and <= 65199 ).ToList();
		CollectionAssert.AreEqual( Enumerable.Range( 65100, 8 ).ToArray(), towers.Select( item => item.InfoId ).ToArray() );
		Assert.IsTrue( towers.All( item => item.Category == ObjectCategory.Feature && item.Names.Count == 9 ) );
		Assert.IsTrue( towers.Take( 4 ).All( item => item.Archive.EndsWith( "flumetower", StringComparison.Ordinal ) ) && towers.Skip( 4 ).All( item => item.Archive.EndsWith( "tubetower", StringComparison.Ordinal ) ) );
		CollectionAssert.AreEqual( Themes.Concat( Themes ).ToArray(), towers.Select( item => item.Theme ).ToArray() );
	}

	[TestMethod]
	public void EveryThemeListsAFlumeAndATubeTowerOnlyWhileTheExtensionIsOn()
	{
		using ( Enabled( true ) )
		{
			foreach ( var theme in Themes )
				foreach ( var profile in new[] { PoolSlideProfile.Flume, PoolSlideProfile.Tube } )
				{
					var entry = ObjectCatalog.Load( theme ).Get( IdOf( theme, profile ) );
					Assert.IsTrue( PoolSlideTowers.IsSlideTower( entry ), $"{theme} {profile}" );
					Assert.AreEqual( profile, PoolSlideTowers.ProfileOf( entry ) );
					Assert.AreEqual( PoolSlideTowers.DefaultCapacity, PoolSlideTowers.CapacityOf( entry ) );
					Assert.AreEqual( ObjectCategory.Feature, entry.Category );
					Assert.AreEqual( "opentpw.pools", entry.ModId );
					CollectionAssert.AreEqual( new[] { "**", "**" }, entry.Shape.Rows.ToArray(), "a 2 x 2 tower with no entrance" );
					Assert.IsFalse( entry.IsChoosable || entry.HasQueue, "guests never choose it" );
					Assert.IsTrue( entry.IsBuildable );
					Assert.AreEqual( 600, entry.BuildCost );
					Assert.IsFalse( PoolChangingRooms.IsChangingRoom( entry ) );
				}
			Assert.AreEqual( 2, ObjectCatalog.Load( "jungle" ).Entries.Count( PoolSlideTowers.IsSlideTower ) );
		}
		using ( Enabled( false ) )
			foreach ( var theme in Themes )
				Assert.IsFalse( ObjectCatalog.Load( theme ).Entries.Any( PoolSlideTowers.IsSlideTower ), $"{theme}: no tower with the extension off" );
	}

	[TestMethod]
	public void EachTowersModelTexturesAndScriptLoadAndTheScriptIdles()
	{
		using var scope = Enabled( true );
		foreach ( var theme in Themes )
			foreach ( var profile in new[] { PoolSlideProfile.Flume, PoolSlideProfile.Tube } )
			{
				var entry = ObjectCatalog.Load( theme ).Get( IdOf( theme, profile ) );
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				Assert.IsTrue( model.Meshes.Count >= 1, theme );
				var textures = new HashSet<string>();
				foreach ( var mesh in model.Meshes )
				{
					Assert.IsNull( ObjectAssets.GetUnrenderableReason( mesh ), $"{theme}: {mesh.Name}" );
					Assert.IsTrue( ObjectAssets.ConvertMesh( mesh ).Length > 0 );
					foreach ( var material in mesh.Materials )
						textures.Add( material.Name );
				}
				Assert.IsTrue( textures.Count >= 3, $"{theme}: {string.Join( ", ", textures )}" );
				foreach ( var name in textures )
					Assert.IsNotNull( ObjectAssets.ResolveTexture( entry, name ), $"{theme}: {name}" );
				// the footprint is the 2 x 2 cells, the platform's top 25 model units (five engine units) above the ground, and nothing below the ground
				var composed = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), WorldOf( model, mesh.NodeIndex ) ) ) ).ToArray();
				Assert.IsTrue( composed.All( p => p.X >= -0.01f && p.X <= 20.01f && p.Z >= -0.01f && p.Z <= 20.01f && p.Y >= -0.01f ), $"{theme}: inside the footprint, above the ground: x {composed.Min( p => p.X )}..{composed.Max( p => p.X )}, y {composed.Min( p => p.Y )}..{composed.Max( p => p.Y )}, z {composed.Min( p => p.Z )}..{composed.Max( p => p.Z )}" );
				Assert.IsTrue( composed.Any( p => MathF.Abs( p.Y - 25f ) < 0.01f ), $"{theme}: the platform's top is at 25" );
				Assert.IsTrue( composed.Max( p => p.Y ) > 25f && composed.Max( p => p.Y ) < 40f );
				using var scriptStream = entry.FileSystem.OpenRead( entry.ScriptPath! );
				var vm = new RideVM( new RideScriptFile( scriptStream ), new RideVMOptions { Effects = new NoEffects(), Seed = 1 } );
				for ( var step = 0; step < 200; step++ )
				{
					vm.Advance( 1.0 / 60 );
					Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
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
	public void ThePoolSideOfATowerIsBeyondItsFrontForEveryRotation()
	{
		using var scope = Enabled( true );
		var entry = ObjectCatalog.Load( "jungle" ).Get( 65100 );
		// rotation 0: the front is the top row, the local -V side, which is north (smaller y) on the map
		CollectionAssert.AreEquivalent( new[] { (10, 9), (11, 9) }, PoolSlideTowers.PoolSideCandidates( entry.Shape, 10, 10, 0 ).ToArray() );
		foreach ( var (rotation, expectedStep) in new[] { (0, (0, -1)), (90, (-1, 0)), (180, (0, 1)), (270, (1, 0)) } )
		{
			var footprint = PoolSlideTowers.Footprint( entry.Shape, 10, 10, rotation );
			Assert.AreEqual( 4, footprint.Count );
			var side = PoolSlideTowers.PoolSideCandidates( entry.Shape, 10, 10, rotation );
			Assert.AreEqual( 2, side.Count, $"rotation {rotation}" );
			Assert.IsTrue( side.All( cell => !footprint.Contains( cell ) && footprint.Contains( (cell.X - expectedStep.Item1, cell.Y - expectedStep.Item2) ) ), $"rotation {rotation}: beyond the front, each beside a footprint cell" );
		}
	}

	[TestMethod]
	public void ATowerIsPlacedOnlyBesideWaterAndOnlyWhileTheExtensionIsOn()
	{
		var cells = new ParkCellMap( 30, 30 );
		ObjectCatalogEntry entry;
		using ( Enabled( true ) )
			entry = ObjectCatalog.Load( "jungle" ).Get( 65100 );
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) ) )
		{
			Assert.AreEqual( OriginalPlacementResult.NeedsPool, PoolSlideTowers.PlacementRule( entry, cells, 10, 10, 0 ), "no water beside it" );
			cells.SetType( 11, 9, ParkCellTypes.PoolWater );
			Assert.IsNull( PoolSlideTowers.PlacementRule( entry, cells, 10, 10, 0 ), "water at its front: no objection" );
			Assert.AreEqual( OriginalPlacementResult.NeedsPool, PoolSlideTowers.PlacementRule( entry, cells, 10, 10, 90 ), "turned, its front is elsewhere" );
			var turned = PoolSlideTowers.PoolSideCandidates( entry.Shape, 10, 10, 90 );
			cells.SetType( turned[1].X, turned[1].Y, ParkCellTypes.PoolWater );
			Assert.IsNull( PoolSlideTowers.PlacementRule( entry, cells, 10, 10, 90 ), $"rotation 90: front cells {turned[0]} and {turned[1]}" );
			var changingRoom = ObjectCatalog.Load( "jungle" ).Entries.FirstOrDefault( item => !PoolSlideTowers.IsSlideTower( item ) );
			if ( changingRoom != null )
				Assert.IsNull( PoolSlideTowers.PlacementRule( changingRoom, cells, 20, 20, 0 ), "another object is not asked" );
		}
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.IsNull( PoolSlideTowers.PlacementRule( entry, cells, 20, 20, 0 ), "with the extension off the rule has no opinion" );
	}
}
