using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class OriginalParkPlacementTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestMethod]
	public void ReadsTheFirstPathTextureFromACorrespondenceTable()
	{
		var table = "#\n#\tTheme Park 2 Jungle Theme\n#\n\nPathTex\n0\tjpa_squ1.tga\n1\tjpa_end1.tga\n\nQueueTex\n0\tjpa_que4.tga\n";
		Assert.AreEqual( "jpa_squ1", OriginalTerrain.ReadPathTextureName( table ) );
		Assert.AreEqual( "jpa_squ1", OriginalTerrain.ReadPathTextureName( table.Replace( "\n", "\r\n" ) ) );
		Assert.IsNull( OriginalTerrain.ReadPathTextureName( "QueueTex\n0\tjpa_que4.tga\n" ) );
		Assert.IsNull( OriginalTerrain.ReadPathTextureName( "PathTex\n1\tjpa_end1.tga\n" ) );
	}

	[TestMethod]
	public void OriginalJungleLoadsTerrainImportAndObjectNames()
	{
		UseOriginalData();
		var park = OriginalPark.Load( "jungle" );
		Assert.AreEqual( (96, 85), (park.Heightfield.CellCountX, park.Heightfield.CellCountZ) );
		Assert.IsNotNull( park.Save );
		Assert.AreEqual( 78, park.Save!.PathCells.Count );
		Assert.AreEqual( "Belly Bounce", park.DescribeObject( 1100 ) );
		CollectionAssert.AreEqual(
			new[] { "Belly Bounce", "Jungle Spray", "Drinks Shop", "Litter Bin", "Security Camera", "Security Camera", "Staff Room", "Small Toilet", "Small Toilet", "Small Toilet", "Round Fountain" },
			park.Save.PlacedObjects.Select( item => park.DescribeObject( item.Record.InfoId ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { "Gates", "Lights", "Bus" }, park.Save.FixedItems.Select( item => park.DescribeObject( item.InfoId ) ).ToArray() );
		Assert.ThrowsException<ArgumentException>( () => OriginalPark.Load( "../jungle" ) );
	}

	[TestMethod]
	public void OriginalJungleBuildRulesUseVerifiedMapAndSaveData()
	{
		UseOriginalData();
		var park = OriginalPark.Load( "jungle" );
		OriginalPlacementResult At( int x, int y ) => OriginalParkPlacement.Check( park, x, y, x, y );
		// the edge ring is Blocked ground with a surface: type 0 in tp.exe (0x536490), so only the land decides there
		Assert.AreEqual( OriginalPlacementResult.Allowed, At( 0, 40 ) );
		Assert.AreEqual( OriginalPlacementResult.Water, At( 51, 50 ) );
		Assert.AreEqual( OriginalPlacementResult.FixedWalkway, At( 51, 57 ) );
		Assert.AreEqual( OriginalPlacementResult.EntranceArea, At( 30, 5 ) );
		Assert.AreEqual( OriginalPlacementResult.Path, At( 47, 17 ) );
		Assert.AreEqual( OriginalPlacementResult.Path, At( 39, 21 ) );
		Assert.AreEqual( OriginalPlacementResult.Occupied, At( 52, 25 ) );
		Assert.AreEqual( OriginalPlacementResult.Occupied, At( 50, 22 ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, At( 20, 60 ) );
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, At( 96, 10 ) );
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, OriginalParkPlacement.Check( park, 5, 5, 4, 5 ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, OriginalParkPlacement.Check( park, 18, 58, 22, 62 ) );
		Assert.AreEqual( OriginalPlacementResult.Path, OriginalParkPlacement.Check( park, 44, 20, 48, 24 ) );

		// Without the save only MAP/heightfield data applies: the imported ring path is buildable.
		Assert.AreEqual( OriginalPlacementResult.Allowed, OriginalParkPlacement.Check( park.Map, park.Heightfield, null, 39, 21, 39, 21 ) );
		Assert.AreEqual( OriginalPlacementResult.Path, OriginalParkPlacement.Check( park.Map, park.Heightfield, null, 47, 17, 47, 17 ) );
	}

	/// <summary>
	/// tp.exe's cell validator 0x535670 never reads the MAP byte: a Blocked cell with a surface is type 0 after the map start
	/// (0x536490) and buildable once owned, a hole is refused by its type. Measured over the four base maps.
	/// </summary>
	[TestMethod]
	public void BlockedSurfaceCellsAreBuildableAndHolesAreNot()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the four base maps." );
		try
		{
			var expected = new System.Collections.Generic.Dictionary<string, int> { ["jungle"] = 326, ["hallow"] = 318, ["space"] = 320, ["fantasy"] = 318 };
			var startingLand = new System.Collections.Generic.List<string>();
			foreach ( var (theme, count) in expected )
			{
				var park = OriginalPark.Load( theme, readShippedSave: false );
				var settings = GuestSettings.Load( theme );
				var setup = ParkLandSetup.FromLevel( settings.EntranceA, settings.EntranceB, FileSystem.ReadAllText( $"/levels/{theme}/Hoardings.sam" ) );
				var cells = ParkCellMap.FromOriginal( park.Map, null, setup, park.Heightfield );
				var blockedSurface = 0;
				for ( var x = 0; x < park.Heightfield.CellCountX; x++ )
				{
					for ( var y = 0; y < park.Heightfield.CellCountZ; y++ )
					{
						var flags = park.Map.GetFlagsAt( x, y );
						var result = OriginalParkPlacement.Check( park, x, y, x, y );
						if ( park.Heightfield.IsHole( x, y ) )
						{
							Assert.IsTrue( result is OriginalPlacementResult.NoTerrainSurface or OriginalPlacementResult.Blocked or OriginalPlacementResult.Water or OriginalPlacementResult.FixedWalkway or OriginalPlacementResult.EntranceArea, $"{theme} ({x}, {y}): {result}" );
							continue;
						}
						Assert.AreEqual( flags.HasFlag( MapCellFlags.InitialPath ) ? OriginalPlacementResult.Path : OriginalPlacementResult.Allowed, result, $"{theme} ({x}, {y})" );
						if ( flags != MapCellFlags.None && !flags.HasFlag( MapCellFlags.InitialPath ) && !cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) )
							startingLand.Add( $"{theme} ({x}, {y})" );
						if ( flags.HasFlag( MapCellFlags.Blocked ) )
						{
							Assert.AreEqual( 1, park.Map.GetCellAt( x, y ), $"{theme} ({x}, {y})" );
							blockedSurface++;
						}
					}
				}
				Assert.AreEqual( count, blockedSurface, theme );
			}
			CollectionAssert.AreEqual( new[] { "space (45, 18)", "space (50, 18)", "fantasy (27, 17)" }, startingLand );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	[TestMethod]
	public void OriginalJungleCellMappingRoundTripsAndFollowsTerrainHeight()
	{
		UseOriginalData();
		var field = OriginalPark.Load( "jungle" ).Heightfield;
		Assert.AreEqual( 2f, OriginalParkPlacement.CellSize );
		foreach ( var (x, y) in new[] { (0, 0), (47, 17), (95, 84), (34, 23) } )
		{
			var center = OriginalParkPlacement.GetCellCenter( field, x, y );
			Assert.IsTrue( OriginalParkPlacement.TryGetCell( field, center.X, center.Y, out var cellX, out var cellY ) );
			Assert.AreEqual( (x, y), (cellX, cellY) );
		}
		Assert.AreEqual( -96f, OriginalParkPlacement.ToEngine( field, System.Numerics.Vector3.Zero ).X );
		Assert.AreEqual( -85f, OriginalParkPlacement.ToEngine( field, System.Numerics.Vector3.Zero ).Y );
		Assert.AreEqual( -2.5f * OriginalParkPlacement.ModelScale, OriginalParkPlacement.GetCellCenter( field, 34, 23 ).Z, 0.01f );
		Assert.IsFalse( OriginalParkPlacement.TryGetCell( field, -96.5f, 0, out _, out _ ) );
		Assert.IsFalse( OriginalParkPlacement.TryGetCell( field, 0, 85f, out _, out _ ) );
		Assert.IsFalse( OriginalParkPlacement.TryGetCell( field, float.NaN, 0, out _, out _ ) );
	}

	private void UseOriginalData()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "jungle", "terrain.wad" ) ) )
			Assert.Inconclusive( "Original Jungle terrain archive is missing." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}
}
