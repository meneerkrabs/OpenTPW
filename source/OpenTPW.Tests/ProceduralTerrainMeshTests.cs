using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The per frame work of the procedural terrain (<see cref="ProceduralTerrain"/>, docs/LND.md) on a synthetic level and stand-in pages, without a graphics device: which cells the span selection lists, which slots they take
/// and when they are built, and what the mesh holds (corner order for the two diagonals, texture coordinates).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ProceduralTerrainMeshTests
{
	private const int Size = 16;

	private sealed class FakePages : IProceduralPages
	{
		public readonly List<int> Uploads = new();
		public byte[] Scratch { get; } = new byte[Size * Size * 4];
		public int PageOf( int slot ) => slot / 5;
		public (float U0, float V0, float U1, float V1) CoordinatesOf( int slot ) => (slot * 0.01f + 0.001f, slot * 0.02f + 0.002f, slot * 0.01f + 0.009f, slot * 0.02f + 0.018f);
		public void Upload( int slot, byte[] rgba ) => Uploads.Add( slot );
	}

	private static LndFile Lnd()
	{
		var data = SyntheticLnd.Build( 3, 3, [48, 40, 40, 90, 68, 40, 97, 30, 55, 50], ( x, y, channel ) => channel == 0 ? (byte)255 : (byte)(x * 11 + y * 7 + channel),
			( size, image, x, y, channel ) => (byte)(size + image * 5 + x * 3 + y * 11 + channel) );
		return new LndFile( new MemoryStream( data ) );
	}

	/// <summary>A 5 × 4 cell heightfield (the weight grid is 3 × 3 and the rectangle ends one column and row before the field's) with a hole at (1, 1) and anti-diagonal cells at (0, 0) and (3, 2).</summary>
	private static ModelHeightfield Field()
	{
		var words = new uint[20];
		words[1 * 5 + 1] = ModelHeightfield.HoleCellWord;
		words[0] = ModelHeightfield.AntiDiagonalFlag | 0x8;
		words[2 * 5 + 3] = ModelHeightfield.AntiDiagonalFlag;
		return new ModelHeightfield( 5, 4, 10, 10, 0, 0, Array.Empty<uint>(), new float[6 * 5], words );
	}

	/// <summary>A camera: the matrix from engine space and the projection.</summary>
	private readonly record struct Camera( Matrix4x4 View, Matrix4x4 Projection );

	/// <summary>A camera <paramref name="height"/> engine units above the engine x, y looking straight down at the field. The span selection's rays are wider than any screen's (a tangent of 1), so the footprint on the flat ground is the height times 1.33 to each side in x and the height in y (model units: five times as many).</summary>
	private static Camera Above( float x, float y, float height )
		=> new( Matrix4x4.CreateLookAt( new System.Numerics.Vector3( x, y, height ), new System.Numerics.Vector3( x, y, 0 ), System.Numerics.Vector3.UnitY ),
			Matrix4x4.CreatePerspectiveFieldOfView( 90 * MathF.PI / 180, 4f / 3, 0.1f, 1000 ) );

	private static Camera Everything() => Above( 0, 0, 40 );

	/// <summary>A low view over the cell column <paramref name="column"/>: its footprint reaches that column and the next one.</summary>
	private static Camera OverColumn( ModelHeightfield field, int column )
	{
		var centre = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( (column + 0.5f) * field.CellSizeX, 0, 2.5f * field.CellSizeZ ) );
		return Above( centre.X, centre.Y, 1.6f );
	}

	private static bool Refresh( ProceduralTerrain terrain, Camera camera, Func<int, int, bool>? hole = null, ulong version = 0 ) => terrain.Refresh( camera.View, camera.Projection, hole, version );

	private static (ProceduralTerrain Terrain, FakePages Pages, ModelHeightfield Field) Make( int cache = 40 )
	{
		var field = Field();
		var pages = new FakePages();
		var terrain = new ProceduralTerrain( field, Lnd(), new ProceduralTerrainRectangle( 0, 0, 3, 2 ), new ProceduralTerrainSettings( Size, cache ), 2, pages );
		return (terrain, pages, field);
	}

	[TestMethod]
	public void EveryCellOfTheRectangleThatHasASurfaceIsBuiltAndDrawnOnTheFirstFrame()
	{
		var (terrain, pages, field) = Make();
		Assert.IsTrue( Refresh( terrain, Everything() ) );
		Assert.AreEqual( 10, terrain.VisibleCells.Count(), "12 cells of the rectangle less the hole and the corner cell (3, 0), whose samples run past a grid this small" );
		Assert.IsFalse( terrain.VisibleCells.Contains( (1, 1) ) );
		Assert.IsTrue( terrain.VisibleCells.Contains( (3, 1) ), "the column after the weight grid is built too" );
		Assert.AreEqual( 10, pages.Uploads.Count );
		Assert.AreEqual( 10, pages.Uploads.Distinct().Count() );
		Assert.AreEqual( 10, terrain.DrawnCells );
		// the first cell (0, 0): its slot is the node before the middle one of the ring
		var slot = pages.Uploads[0];
		var (u0, v0, u1, v1) = pages.CoordinatesOf( slot );
		var vertices = terrain.Vertices;
		var coordinates = vertices.Take( 4 ).Select( vertex => (vertex.TexCoords.X, vertex.TexCoords.Y) ).ToArray();
		CollectionAssert.AreEquivalent( new[] { (u0, 1 - v0), (u1, 1 - v0), (u0, 1 - v1), (u1, 1 - v1) }, coordinates, "u along x, v along z, the page's v flipped for the terrain shader" );
		Assert.IsTrue( vertices.Take( 4 ).All( vertex => vertex.TexIndex == 2 + pages.PageOf( slot ) ), "the page's material slot" );
	}

	[TestMethod]
	public void EveryFrameLooksTheCellsUpAgainButAViewOfTheSameCellsChangesNothingAndBuildsNothing()
	{
		var (terrain, pages, field) = Make();
		var view = Everything();
		Func<int, int, bool> none = ( x, z ) => false;
		Refresh( terrain, view, none );
		var uploads = pages.Uploads.Count;
		Assert.AreEqual( 0, terrain.Cache.Hits );
		Assert.IsFalse( Refresh( terrain, view, none ), "the mesh keeps its cells and textures" );
		Assert.AreEqual( uploads, pages.Uploads.Count );
		Assert.AreEqual( 10, terrain.Cache.Hits, "the original looks the cells up in every rendered frame (a hit moves its slot in the ring)" );
		Refresh( terrain, view, none );
		Assert.AreEqual( 20, terrain.Cache.Hits );
		Assert.IsFalse( Refresh( terrain, view, ( x, z ) => x == 2 && z == 0, 0 ), "the caller says nothing about the holes changed, so the old answer stands" );
		Assert.IsTrue( Refresh( terrain, view, ( x, z ) => x == 2 && z == 0, 1 ), "a new hole version looks at the cells again" );
		Assert.IsFalse( terrain.VisibleCells.Contains( (2, 0) ) );
		Assert.AreEqual( uploads, pages.Uploads.Count, "the other cells kept their textures" );
	}

	[TestMethod]
	public void TheHoleAnswersAreKeptUntilTheVersionChanges()
	{
		var (terrain, _, field) = Make();
		var view = Everything();
		var asked = 0;
		bool Hole( int x, int z ) { asked++; return false; }
		Refresh( terrain, view, Hole, 5 );
		var first = asked;
		Assert.IsTrue( first > 0 );
		Refresh( terrain, view, Hole, 5 );
		Refresh( terrain, view, Hole, 5 );
		Assert.AreEqual( first, asked, "each cell is asked once per version" );
		Refresh( terrain, view, Hole, 6 );
		Assert.AreEqual( 2 * first, asked );
	}

	[TestMethod]
	public void OnlyTheCellsOfTheSpansAreCachedAndAnotherViewBuildsTheNewOnes()
	{
		var (terrain, pages, field) = Make();
		Refresh( terrain, OverColumn( field, 0 ) );
		// the low view's spans reach the columns 0 and 1 at most
		Assert.IsTrue( terrain.VisibleCells.All( cell => cell.X <= 1 ), string.Join( ",", terrain.VisibleCells ) );
		Assert.IsTrue( terrain.VisibleCells.Any( cell => cell.X == 0 ) );
		var first = pages.Uploads.Count;
		Refresh( terrain, Everything() );
		Assert.IsTrue( pages.Uploads.Count > first, "the cells that came into view were built" );
		Assert.AreEqual( terrain.VisibleCells.Count(), terrain.Builds, "each cell once" );
	}

	[TestMethod]
	public void AViewOfMoreCellsThanTheCacheHoldsStillFillsTheMesh()
	{
		var (terrain, pages, field) = Make( cache: 4 );
		Refresh( terrain, Everything() );
		Assert.AreEqual( 10, terrain.VisibleCells.Count(), "the frame lists every cell in view" );
		Assert.IsTrue( terrain.Vertices.Length >= 10 * 4 );
		Assert.AreEqual( 4, pages.Uploads.Count, "a slot is built once, for the last cell that took it (what the original's page holds at the draw, after it built the earlier cells into it first)" );
		Assert.AreEqual( 4, pages.Uploads.Distinct().Count() );
		Assert.AreEqual( 10, terrain.DrawnCells );
	}

	[TestMethod]
	public void ARenderHoleLeavesTheCellOutOfTheMesh()
	{
		var (terrain, _, field) = Make();
		Refresh( terrain, Everything(), ( x, z ) => x == 2 && z == 0 );
		Assert.AreEqual( 9, terrain.VisibleCells.Count() );
		Assert.IsFalse( terrain.VisibleCells.Contains( (2, 0) ) );
	}

	[TestMethod]
	public void ACellThatSplitsTheOtherWayTakesItsCornersInATurnedOrderSoOneIndexPatternServesBoth()
	{
		var (terrain, _, field) = Make();
		Refresh( terrain, Everything() );
		var cells = terrain.VisibleCells.ToList();
		uint[] pattern = [0, 2, 3, 3, 1, 0];
		foreach ( var (x, z) in new[] { (0, 0), (2, 0), (3, 2) } )
		{
			var first = cells.IndexOf( (x, z) ) * 4;
			var vertices = terrain.Vertices.Skip( first ).Take( 4 ).ToArray();
			var corner = ( int dx, int dz ) => OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( (x + dx) * field.CellSizeX, field.GetCornerHeight( x + dx, z + dz ), (z + dz) * field.CellSizeZ ) );
			var shared = ( OpenTPW.Vector3 a, OpenTPW.Vector3 b ) => (a, b);
			var diagonalOne = vertices[(int)pattern[0]].Position;
			var diagonalTwo = vertices[(int)pattern[2]].Position;
			var expected = field.SplitsAlongAntiDiagonal( x, z ) ? (corner( 1, 0 ), corner( 0, 1 )) : (corner( 0, 0 ), corner( 1, 1 ));
			Assert.AreEqual( expected.Item1.X, diagonalOne.X, 1e-4f, $"cell ({x}, {z}) first end of the shared diagonal" );
			Assert.AreEqual( expected.Item1.Y, diagonalOne.Y, 1e-4f );
			Assert.AreEqual( expected.Item2.X, diagonalTwo.X, 1e-4f, $"cell ({x}, {z}) second end of the shared diagonal" );
			Assert.AreEqual( expected.Item2.Y, diagonalTwo.Y, 1e-4f );
		}
	}

	[TestMethod]
	public void AnUnusedQuadIsDegenerateInsideTheMeshAndOnlyCellsInsideTheRectangleAreProcedural()
	{
		var (terrain, _, field) = Make();
		Refresh( terrain, Everything() );
		var used = terrain.VisibleCells.Count() * 4;
		var unused = terrain.Vertices.Skip( used ).Chunk( 4 ).ToList();
		Assert.IsTrue( unused.Count > 0 && unused.All( quad => quad.All( vertex => vertex.Position.X == quad[0].Position.X && vertex.Position.Y == quad[0].Position.Y && vertex.Position.Z == quad[0].Position.Z ) ), "four vertices on one point" );
		Assert.IsTrue( terrain.IsProcedural( 3, 2 ) );
		Assert.IsFalse( terrain.IsProcedural( 3, 0 ), "its samples run past the grid" );
		Assert.IsFalse( terrain.IsProcedural( 1, 1 ), "a hole has no surface" );
		Assert.IsFalse( terrain.IsProcedural( 5, 0 ), "past the heightfield" );
	}
}
