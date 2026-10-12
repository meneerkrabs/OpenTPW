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
		Assert.AreEqual( new PathTile( 3, 0 ), Tile( 2, 3 ), "back turns from its predecessor toward the ordinary path" );
		grid.ClearQueue( 2, 3 );
		Assert.AreEqual( new PathTile( 1, 0 ), Tile( 2, 2 ), "removal restores an end pointing toward the entrance" );
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
			Assert.AreEqual( new PathTile( 1, direction * 90 ), tile, "unrelated adjacent queue does not add a corner" );
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
	public void OriginalQueueTilesOpenTowardTheirActualLinks( string theme )
	{
		var path = Path.Combine( OriginalParkImportTests.OriginalDataPath(), "levels", theme, "queue.wad" );
		if ( !File.Exists( path ) ) Assert.Inconclusive( "Original game data is not installed." );
		using var archive = new WadArchive( path );
		var textures = new[] { "jpa_que3", "jpa_que1", "jpa_que2" }.Select( name =>
			new TextureFile( new MemoryStream( archive.GetFile( "textures/" + name + ".wct" ).GetData() ) ).Data ).ToArray();
		for ( var shape = 1; shape <= 3; shape++ )
			for ( var direction = 0; direction < 4; direction++ )
			{
				var grid = new GuestPathGrid( 7, 7 );
				grid.SetQueue( 3, 3, direction );
				var expected = GuestPathGrid.LinkValue( direction );
				if ( shape != 1 )
				{
					var successor = (direction + (shape == 2 ? 2 : 1)) & 3;
					var (dx, dy) = GuestPathGrid.Directions[successor];
					grid.SetQueue( 3 + dx, 3 + dy, (successor + 2) & 3 );
					expected |= GuestPathGrid.LinkValue( successor );
				}
				var tile = PathTiles.ForQueueCell( 3, 3, 7, 7, grid.IsQueue, grid.GetQueueLink, grid.IsWalkable );
				Assert.AreEqual( shape, tile.TextureIndex );
				// Fantasy's straight includes fully opaque grass: its alpha cannot identify the two openings.
				if ( theme == "fantasy" && shape == 2 )
				{
					Assert.IsTrue( textures[shape - 1].Data.Where( (value, index) => index % 4 == 3 ).All( alpha => alpha == 255 ) );
					Assert.AreEqual( (direction & 1) * 90, tile.Rotation );
					continue;
				}
				Assert.AreEqual( expected, ArtworkOpenSides( textures[shape - 1], tile.Rotation ), $"{theme}: shape {shape}, direction {direction}" );
			}
	}

	private static byte ArtworkOpenSides( TextureData image, int rotation )
	{
		byte mask = 0;
		for ( var side = 0; side < 4; side++ )
		{
			var alphaSum = 0;
			for ( var sample = 0; sample < 32; sample++ )
			{
				var t = 1f / 3 + sample / 31f / 3;
				var (x, y) = side switch { 0 => (t, 0f), 1 => (1f, t), 2 => (t, 1f), _ => (0f, t) };
				var uv = PathTiles.TextureCoordinates( x, y, rotation );
				// The terrain shader flips V. Sample the decoded original border exactly as the renderer does.
				var px = (int)MathF.Round( uv.U * (image.Width - 1) );
				var py = (int)MathF.Round( (1 - uv.V) * (image.Height - 1) );
				alphaSum += image.Data[(py * image.Width + px) * 4 + 3];
			}
			var mean = alphaSum / 32f;
			Assert.IsTrue( mean <= 96 || mean >= 192, "Original tile border must clearly be closed or open." );
			if ( mean >= 192 ) mask |= GuestPathGrid.LinkValue( side );
		}
		return mask;
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
