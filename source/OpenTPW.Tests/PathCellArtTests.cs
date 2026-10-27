using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Path tiles as terrain cell textures (docs/PATHS.md, "Path textures"; tp.exe <c>0x46DF50</c>, <c>0x56F090</c>, <c>0x572440</c>): the word the cell refresh writes, the turn of its texture coordinates, and the composite of the tile
/// over the cell's procedural texture with the cache's dirty rebuild. Synthetic level and art, no game data.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PathCellArtTests
{
	private const int Size = 16;

	private sealed class Pages : IProceduralPages
	{
		public readonly Dictionary<int, byte[]> Tiles = new();
		public readonly List<int> Uploads = new();
		public byte[] Scratch { get; } = new byte[Size * Size * 4];
		public int PageOf( int slot ) => 0;
		public (float U0, float V0, float U1, float V1) CoordinatesOf( int slot ) => (0.1f, 0.2f, 0.3f, 0.4f);
		public void Upload( int slot, byte[] rgba ) { Uploads.Add( slot ); Tiles[slot] = (byte[])rgba.Clone(); }
	}

	private static LndFile Lnd()
	{
		var data = SyntheticLnd.Build( 3, 3, [48, 40, 40, 90, 68, 40, 97, 30, 55, 50], ( x, y, channel ) => channel == 0 ? (byte)255 : (byte)(x * 11 + y * 7 + channel),
			( size, image, x, y, channel ) => (byte)(size + image * 5 + x * 3 + y * 11 + channel) );
		return new LndFile( new MemoryStream( data ) );
	}

	private static (ProceduralTerrain Terrain, Pages Pages) Make()
	{
		var field = new ModelHeightfield( 5, 4, 10, 10, 0, 0, Array.Empty<uint>(), new float[6 * 5], new uint[20] );
		var pages = new Pages();
		return (new ProceduralTerrain( field, Lnd(), new ProceduralTerrainRectangle( 0, 0, 3, 2 ), new ProceduralTerrainSettings( Size, 40 ), 2, pages ), pages);
	}

	private static bool Refresh( ProceduralTerrain terrain )
	{
		var view = Matrix4x4.CreateLookAt( new System.Numerics.Vector3( 0, 0, 40 ), new System.Numerics.Vector3( 0, 0, 0 ), System.Numerics.Vector3.UnitY );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( 90 * MathF.PI / 180, 4f / 3, 0.1f, 1000 );
		return terrain.Refresh( view, projection, null );
	}

	/// <summary>A 64 × 64 art: opaque red on the left half, half transparent green on the right, a transparent first row.</summary>
	private static ProceduralCellArt Art( int key, uint word )
	{
		var rgba = new byte[64 * 64 * 4];
		for ( var row = 0; row < 64; row++ )
			for ( var column = 0; column < 64; column++ )
			{
				var at = (row * 64 + column) * 4;
				(rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) = row == 0 ? ((byte)0, (byte)0, (byte)255, (byte)0) : column < 32 ? ((byte)255, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)255, (byte)0, (byte)128);
			}
		return new ProceduralCellArt( key, rgba, 64, 64, word );
	}

	[TestMethod]
	public void TheWordOfATileFollowsTheWriter()
	{
		// slot in the high half; 0xFF87 and 0xFFFE clear the turn, mirror and hole bits; 0x180 and the angle's bit are set; the other bits stay
		Assert.AreEqual( 0x0007_0180u, PathCellWord.Word( 0x0000_0000, 7, 0 ) );
		Assert.AreEqual( 0x0007_01A0u, PathCellWord.Word( 0x1234_0078, 7, 90 ), "90 degrees sets 0x20; the old turn and mirror bits go" );
		Assert.AreEqual( 0x0003_0190u, PathCellWord.Word( 0x0101_0001, 3, 180 ), "180 sets 0x10; the old hole bit goes" );
		Assert.AreEqual( 0x0000_038Eu, PathCellWord.Word( 0x0000_0206, 0, 270 ), "270 sets 0x08; 0x200, 0x04 and 0x02 stay" );
		Assert.AreEqual( 0u, PathCellWord.OrientationBits( 45 ) );
	}

	[TestMethod]
	public void TheTurnBitsTurnTheTextureCoordinatesAsThePathLayerDid()
	{
		// the layer's own mapping (PathTiles.TextureCoordinates, checked against the saved tiles) is what the cell word's bits give: the terrain draw needs no second rule
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var word = PathCellWord.Word( 0, 1, rotation );
			var corners = new[] { (TerrainCellTexture.Cell00, 0f, 0f), (TerrainCellTexture.Cell10, 1f, 0f), (TerrainCellTexture.Cell01, 0f, 1f), (TerrainCellTexture.Cell11, 1f, 1f) };
			foreach ( var (corner, fx, fy) in corners )
			{
				var expected = PathTiles.TextureCoordinates( fx, fy, rotation );
				var actual = TerrainCellTexture.Coordinate( corner, word );
				Assert.AreEqual( expected.U, actual.U, 1e-6f, $"{rotation} corner {corner}" );
				Assert.AreEqual( expected.V, actual.V, 1e-6f, $"{rotation} corner {corner}" );
			}
		}
	}

	[TestMethod]
	public void ACellWithArtIsBuiltWithTheCompositeAndAChangeBuildsItAgainThroughTheDirtyBit()
	{
		var (terrain, pages) = Make();
		Refresh( terrain );
		var slot = pages.Uploads[0];
		var cell = terrain.VisibleCells.First();
		var plain = pages.Tiles[slot];
		var word = PathCellWord.Word( 0, 4, 0 );
		Assert.IsTrue( terrain.SetCellArt( cell.X, cell.Z, Art( 4, word ) ) );
		var uploadsBefore = pages.Uploads.Count;
		var builtBefore = terrain.Builds;
		// the cell is a cache hit that was marked: it is built again, once, into the same slot
		Refresh( terrain );
		Assert.AreEqual( builtBefore + 1, terrain.Builds );
		Assert.AreEqual( uploadsBefore + 1, pages.Uploads.Count );
		Assert.AreEqual( slot, pages.Uploads[^1] );
		var composite = pages.Tiles[slot];
		CollectionAssert.AreNotEqual( plain, composite );
		// the composite is the tile point sampled over the base: the texel of an opaque red art pixel keeps 1/256 of the base
		var baseRgba = new byte[plain.Length];
		var expected = new byte[plain.Length];
		var texturer = terrain.Texturer;
		texturer.BuildRgba( cell.X, cell.Z, baseRgba );
		ProceduralTerrainTexturer.Compose( baseRgba, Size, Art( 4, word ).Rgba, 64, 64, (int)word, expected );
		CollectionAssert.AreEqual( expected, composite );
		// nothing changed: no further build
		Refresh( terrain );
		Assert.AreEqual( builtBefore + 1, terrain.Builds );
		// the same tile again marks nothing
		Assert.IsFalse( terrain.SetCellArt( cell.X, cell.Z, Art( 4, word ) ) );
		Refresh( terrain );
		Assert.AreEqual( builtBefore + 1, terrain.Builds );
		// a turn changes the word: built again; taking the art away restores the plain texture
		Assert.IsTrue( terrain.SetCellArt( cell.X, cell.Z, Art( 4, PathCellWord.Word( 0, 4, 90 ) ) ) );
		Refresh( terrain );
		Assert.AreEqual( builtBefore + 2, terrain.Builds );
		Assert.IsTrue( terrain.SetCellArt( cell.X, cell.Z, null ) );
		Refresh( terrain );
		CollectionAssert.AreEqual( plain, pages.Tiles[slot] );
		Assert.AreEqual( 0, terrain.ArtCells );
	}

	[TestMethod]
	public void ACellOutOfViewKeepsTheChangeUntilItIsListed()
	{
		var (terrain, pages) = Make();
		Refresh( terrain );
		var builds = terrain.Builds;
		terrain.SetCellArt( 3, 1, Art( 2, PathCellWord.Word( 0, 2, 0 ) ) );
		terrain.SetCellArt( 3, 1, Art( 2, PathCellWord.Word( 0, 2, 180 ) ) );
		Refresh( terrain );
		Assert.AreEqual( builds + 1, terrain.Builds, "one build for the latest word" );
	}

	[TestMethod]
	public void ThePathCellsTextureCoordinatesTurnAndTheMeshMarksThemAsPathLike()
	{
		var (terrain, _) = Make();
		Refresh( terrain );
		var first = terrain.VisibleCells.ToList().IndexOf( (0, 0) );
		var plain = terrain.Vertices.Skip( first * 4 ).Take( 4 ).Select( vertex => vertex.TexCoords ).ToArray();
		terrain.SetCellArt( 0, 0, Art( 1, PathCellWord.Word( 0, 1, 90 ) ) );
		Refresh( terrain );
		var turned = terrain.Vertices.Skip( first * 4 ).Take( 4 ).ToArray();
		CollectionAssert.AreEquivalent( plain, turned.Select( vertex => vertex.TexCoords ).ToArray(), "the same four corners of the tile" );
		CollectionAssert.AreNotEqual( plain, turned.Select( vertex => vertex.TexCoords ).ToArray(), "in another order" );
		Assert.IsTrue( turned.All( vertex => (vertex.MatFlags & TextureSlotAddressing.SlushFlag) != 0 ) );
	}

	[TestMethod]
	public void AStaticPathCellStandsOnTheGroundsCornersOfASlopedCellAndSharesItsDiagonal()
	{
		var heights = new float[6 * 5];
		for ( var index = 0; index < heights.Length; index++ )
			heights[index] = (index * 37 % 11) * 0.7f;
		var words = new uint[20];
		words[1 * 5 + 2] = ModelHeightfield.AntiDiagonalFlag;
		var field = new ModelHeightfield( 5, 4, 10, 10, 0, 0, Array.Empty<uint>(), heights, words );
		foreach ( var (x, y) in new[] { (1, 1), (2, 1) } )
		{
			var ground = new List<Vertex>();
			var groundIndices = new List<uint>();
			var path = new List<Vertex>();
			var pathIndices = new List<uint>();
			OriginalTerrain.AddCell( ground, groundIndices, field, x, y, 0, field.GetCellWord( x, y ) );
			OriginalTerrain.AddCell( path, pathIndices, field, x, y, 3, PathCellWord.Word( field.GetCellWord( x, y ), 3, 90 ), WorldTranslucency.OpaqueSlotFlag );
			Assert.AreEqual( 4, path.Count );
			for ( var corner = 0; corner < 4; corner++ )
			{
				var expected = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( (x + (corner & 1)) * 10, field.GetCornerHeight( x + (corner & 1), y + (corner >> 1) ), (y + (corner >> 1)) * 10 ) );
				Assert.AreEqual( expected, path[corner].Position, $"cell ({x}, {y}) corner {corner}: the corner's own height, no lift" );
				Assert.AreEqual( ground[corner].Position, path[corner].Position );
			}
			CollectionAssert.AreEqual( groundIndices, pathIndices, "the same diagonal as the ground" );
			Assert.AreEqual( 3, path[0].TexIndex );
		}
		Assert.IsTrue( heights.Distinct().Count() > 3, "the cell is sloped" );
	}
}
