using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class QueueTextureTests
{
	[TestMethod]
	public void QueueAndPathTablesKeepTheirSeparateZeroIndices()
	{
		var table = "PathTex\n0 jpa_squ1.tga\nQueueTex\n0 jpa_que4.tga\n1 jpa_que3.tga\n2 jpa_que1.tga\n3 jpa_que2.tga\nWater\n0 water.tga\n";
		Assert.AreEqual( "jpa_squ1", OriginalTerrain.ReadPathTextureNames( table )[0] );
		var queue = OriginalTerrain.ReadQueueTextureNames( table.Replace( "\n", "\r\n" ) );
		CollectionAssert.AreEqual( new[] { "jpa_que4", "jpa_que3", "jpa_que1", "jpa_que2" }, queue.OrderBy( entry => entry.Key ).Select( entry => entry.Value ).ToArray() );
	}

	[TestMethod]
	public void LinkedQueuesUseStraightsCornersAndTheirPathJoin()
	{
		var grid = new GuestPathGrid( 8, 8 );
		grid.SetQueue( 2, 2, 0 );
		grid.SetQueue( 2, 3, 0 );
		grid.SetPath( 3, 3, true );
		PathTile Tile( int x, int y ) => PathTiles.ForQueueCell( x, y, 8, 8, grid.IsQueue, grid.GetQueueLink, grid.IsWalkable );
		Assert.AreEqual( new PathTile( 2, 0 ), Tile( 2, 2 ), "front continues toward the entrance and its successor" );
		Assert.AreEqual( new PathTile( 3, 270 ), Tile( 2, 3 ), "back turns from its predecessor toward the ordinary path" );
		grid.ClearQueue( 2, 3 );
		Assert.AreEqual( new PathTile( 1, 180 ), Tile( 2, 2 ), "removal restores an end pointing toward the entrance" );
	}

	[TestMethod]
	public void TouchingIndependentQueuesDoNotJoinAndRotationsFollowLinks()
	{
		for ( var direction = 0; direction < 4; direction++ )
		{
			var grid = new GuestPathGrid( 5, 5 );
			grid.SetQueue( 2, 2, direction );
			var side = (direction + 1) & 3;
			var (dx, dy) = GuestPathGrid.Directions[side];
			grid.SetQueue( 2 + dx, 2 + dy, side );
			var tile = PathTiles.ForQueueCell( 2, 2, 5, 5, grid.IsQueue, grid.GetQueueLink, grid.IsWalkable );
			Assert.AreEqual( new PathTile( 1, ((direction + 2) & 3) * 90 ), tile, "unrelated adjacent queue does not add a corner" );
		}
	}

	[TestMethod]
	public void QueueTextureLookupUsesTheQueueArchive()
	{
		string[] Files( string directory ) => directory switch
		{
			"/levels/jungle/queue/textures" => new[] { "/levels/jungle/queue/textures/jpa_que1.wct" },
			"/levels/jungle/terrain/pathtex" => new[] { "/levels/jungle/terrain/pathtex/jpa_str1.wct" },
			_ => Array.Empty<string>()
		};
		Assert.AreEqual( "/levels/jungle/queue/textures/jpa_que1.wct", OriginalTerrain.FindTexture( "/levels/jungle/queue", "jungle", "jpa_que1", "textures", Files ) );
		Assert.IsNull( OriginalTerrain.FindTexture( "/levels/jungle/terrain", "jungle", "jpa_que1", "pathtex", Files ) );
	}

	[DataTestMethod]
	[DataRow( "jungle" )]
	[DataRow( "hallow" )]
	[DataRow( "fantasy" )]
	[DataRow( "space" )]
	public void OriginalQueueTablesResolveToDistinctReadableArtwork( string theme )
	{
		var root = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( root, "levels", theme, "terrain.wad" ) ) ) Assert.Inconclusive( "Original game data is not installed." );
		using var terrain = new WadArchive( Path.Combine( root, "levels", theme, "terrain.wad" ) );
		using var queues = new WadArchive( Path.Combine( root, "levels", theme, "queue.wad" ) );
		var table = System.Text.Encoding.UTF8.GetString( terrain.GetFile( terrain.GetFiles( "" ).Single( path => path.EndsWith( ".tct", StringComparison.OrdinalIgnoreCase ) ) ).GetData() );
		var names = OriginalTerrain.ReadQueueTextureNames( table );
		Assert.AreEqual( 4, names.Count );
		var available = queues.GetFiles( "textures" ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		var missing = names.Where( entry => !available.Contains( entry.Value + ".wct" ) ).Select( entry => entry.Key ).ToArray();
		CollectionAssert.AreEqual( theme is "hallow" or "space" ? new[] { 0 } : Array.Empty<int>(), missing, "only the known original isolated-square omission is allowed" );
		var images = names.Values.Where( name => available.Contains( name + ".wct" ) ).Select( name => new TextureFile( new MemoryStream( queues.GetFile( "textures/" + name + ".wct" ).GetData() ) ).Data ).ToArray();
		Assert.IsTrue( images.All( image => image.Width == 128 && image.Height == 128 ) );
		Assert.AreEqual( names.Count - missing.Length, images.Select( image => Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( image.Data ) ) ).Distinct().Count() );
	}
}
