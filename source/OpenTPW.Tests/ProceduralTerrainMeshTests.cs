using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The per frame work of the procedural terrain (<see cref="ProceduralTerrain"/>, docs/LND.md) on a synthetic level and stand-in pages, without a graphics device: which cells are in view, which slots they take
/// and when they are built, and what the mesh holds (corner order for the two diagonals, texture coordinates, the static texture of a cell that waits).
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

	/// <summary>A 4 × 3 cell heightfield (one column more than the 3 × 3 weight grid, as in the game's levels) with a hole at (1, 1) and anti-diagonal cells at (0, 0) and (3, 2).</summary>
	private static ModelHeightfield Field()
	{
		var words = new uint[12];
		words[1 * 4 + 1] = ModelHeightfield.HoleCellWord;
		words[0] = ModelHeightfield.AntiDiagonalFlag | 0x8;
		words[2 * 4 + 3] = ModelHeightfield.AntiDiagonalFlag;
		return new ModelHeightfield( 4, 3, 10, 10, 0, 0, Array.Empty<uint>(), new float[5 * 4], words );
	}

	private static Matrix4x4 Everything( ModelHeightfield field )
	{
		var low = OriginalParkPlacement.ToEngine( field, System.Numerics.Vector3.Zero );
		var high = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( field.CellCountX * field.CellSizeX, 0, field.CellCountZ * field.CellSizeZ ) );
		return Matrix4x4.CreateOrthographicOffCenter( Math.Min( low.X, high.X ) - 1, Math.Max( low.X, high.X ) + 1, Math.Min( low.Y, high.Y ) - 1, Math.Max( low.Y, high.Y ) + 1, -100, 100 );
	}

	private static Matrix4x4 OnlyColumns( ModelHeightfield field, int first, int last )
	{
		// a view of the engine x range that holds the columns first..last (a cell is kept when a corner is in view)
		var a = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( (first + 0.5f) * field.CellSizeX, 0, 0 ) );
		var b = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( (last + 0.5f) * field.CellSizeX, 0, 0 ) );
		var all = Everything( field );
		var low = OriginalParkPlacement.ToEngine( field, System.Numerics.Vector3.Zero );
		var high = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( field.CellCountX * field.CellSizeX, 0, field.CellCountZ * field.CellSizeZ ) );
		return Matrix4x4.CreateOrthographicOffCenter( Math.Min( a.X, b.X ) - 0.01f, Math.Max( a.X, b.X ) + 0.01f, Math.Min( low.Y, high.Y ) - 1, Math.Max( low.Y, high.Y ) + 1, -100, 100 );
	}

	private static (ProceduralTerrain Terrain, FakePages Pages, ModelHeightfield Field) Make( int cache = 40 )
	{
		var field = Field();
		var pages = new FakePages();
		var terrain = new ProceduralTerrain( field, Lnd(), new ProceduralTerrainRectangle( 0, 0, 3, 2 ), new ProceduralTerrainSettings( Size, cache ), ( x, z ) => (x + z) % 2, 2, pages );
		return (terrain, pages, field);
	}

	[TestMethod]
	public void EveryCellOfTheRectangleThatHasASurfaceIsBuiltAndDrawnOnTheFirstFrame()
	{
		var (terrain, pages, field) = Make();
		Assert.IsTrue( terrain.Refresh( Everything( field ), null ) );
		Assert.AreEqual( 10, terrain.VisibleCells.Count(), "12 cells less the hole and the corner cell (3, 0), whose samples run past a grid this small" );
		Assert.IsFalse( terrain.VisibleCells.Contains( (1, 1) ) );
		Assert.IsTrue( terrain.VisibleCells.Contains( (3, 1) ), "the column after the weight grid is built too" );
		Assert.AreEqual( 10, pages.Uploads.Count );
		Assert.AreEqual( 10, pages.Uploads.Distinct().Count() );
		Assert.AreEqual( 10, terrain.DrawnCells );
		Assert.AreEqual( 0, terrain.PendingCells );
		// the first cell (0, 0): its slot is the node before the middle one of the ring
		var slot = pages.Uploads[0];
		var (u0, v0, u1, v1) = pages.CoordinatesOf( slot );
		var vertices = terrain.Vertices;
		var coordinates = vertices.Take( 4 ).Select( vertex => (vertex.TexCoords.X, vertex.TexCoords.Y) ).ToArray();
		CollectionAssert.AreEquivalent( new[] { (u0, 1 - v0), (u1, 1 - v0), (u0, 1 - v1), (u1, 1 - v1) }, coordinates, "u along x, v along z, the page's v flipped for the terrain shader" );
		Assert.IsTrue( vertices.Take( 4 ).All( vertex => vertex.TexIndex == 2 + pages.PageOf( slot ) ), "the page's material slot" );
	}

	[TestMethod]
	public void AViewOfTheSameCellsChangesNothingAndBuildsNothing()
	{
		var (terrain, pages, field) = Make();
		var view = Everything( field );
		terrain.Refresh( view, null );
		var uploads = pages.Uploads.Count;
		Assert.IsFalse( terrain.Refresh( view, null ), "the mesh keeps its cells and textures" );
		Assert.AreEqual( uploads, pages.Uploads.Count );
		Assert.AreEqual( 0, terrain.Cache.Hits, "the same view is not looked up again" );
		Assert.IsFalse( terrain.Refresh( view, ( x, z ) => x == 2 && z == 0, 0 ), "the caller says nothing about the holes changed, so the old answer stands" );
		Assert.IsTrue( terrain.Refresh( view, ( x, z ) => x == 2 && z == 0, 1 ), "a new hole version looks at the cells again" );
		Assert.IsFalse( terrain.VisibleCells.Contains( (2, 0) ) );
		Assert.AreEqual( uploads, pages.Uploads.Count, "the other cells kept their textures" );
	}

	[TestMethod]
	public void OnlyTheCellsWithACornerInViewAreCachedAndAnotherViewBuildsTheNewOnes()
	{
		var (terrain, pages, field) = Make();
		terrain.Refresh( OnlyColumns( field, 0, 0 ), null );
		// columns 0 and the corner column 1 border it: a cell is in view while one of its corners is
		Assert.IsTrue( terrain.VisibleCells.All( cell => cell.X <= 1 ), string.Join( ",", terrain.VisibleCells ) );
		Assert.IsTrue( terrain.VisibleCells.Any( cell => cell.X == 0 ) );
		var first = pages.Uploads.Count;
		terrain.Refresh( OnlyColumns( field, 0, 2 ), null );
		Assert.IsTrue( pages.Uploads.Count > first, "the cells that came into view were built" );
		Assert.AreEqual( terrain.VisibleCells.Count(), terrain.Builds, "each cell once" );
	}

	[TestMethod]
	public void AViewOfMoreCellsThanTheCacheHoldsStillFillsTheMesh()
	{
		var (terrain, pages, field) = Make( cache: 4 );
		terrain.Refresh( Everything( field ), null );
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
		terrain.Refresh( Everything( field ), ( x, z ) => x == 2 && z == 0 );
		Assert.AreEqual( 9, terrain.VisibleCells.Count() );
		Assert.IsFalse( terrain.VisibleCells.Contains( (2, 0) ) );
	}

	[TestMethod]
	public void ACellThatSplitsTheOtherWayTakesItsCornersInATurnedOrderSoOneIndexPatternServesBoth()
	{
		var (terrain, _, field) = Make();
		terrain.Refresh( Everything( field ), null );
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
	public void AFrameThatCannotBuildEverythingShowsTheStaticTextureOfTheCellsThatWait()
	{
		var previous = ProceduralTerrain.BuildBudgetMilliseconds;
		try
		{
			ProceduralTerrain.BuildBudgetMilliseconds = 0;
			var (terrain, pages, field) = Make();
			terrain.Refresh( Everything( field ), null );
			Assert.AreEqual( 1, pages.Uploads.Count, "a frame builds at least one cell and then stops at its budget" );
			Assert.AreEqual( 9, terrain.PendingCells );
			Assert.AreEqual( 1, terrain.DrawnCells );
			var cells = terrain.VisibleCells.ToList();
			var waiting = 0;
			for ( var index = 0; index < cells.Count; index++ )
			{
				var (x, z) = cells[index];
				if ( terrain.Vertices[index * 4].TexIndex < 2 )
				{
					waiting++;
					Assert.AreEqual( (x + z) % 2, terrain.Vertices[index * 4].TexIndex, $"cell ({x}, {z}) shows its own static ground texture" );
				}
			}
			Assert.AreEqual( 9, waiting, "the cells that wait point at their static ground texture slot" );
			var frames = 0;
			while ( terrain.PendingCells > 0 && frames++ < 20 )
				terrain.Refresh( Everything( field ), null );
			Assert.AreEqual( 0, terrain.PendingCells, "the waiting cells arrive one frame after another, the view unchanged" );
			Assert.AreEqual( terrain.VisibleCells.Count(), terrain.DrawnCells );
			Assert.AreEqual( 10, pages.Uploads.Distinct().Count() );
		}
		finally
		{
			ProceduralTerrain.BuildBudgetMilliseconds = previous;
		}
	}

	[TestMethod]
	public void AnUnusedQuadIsDegenerateInsideTheMeshAndOnlyCellsInsideTheRectangleAreProcedural()
	{
		var (terrain, _, field) = Make();
		terrain.Refresh( Everything( field ), null );
		var used = terrain.VisibleCells.Count() * 4;
		var unused = terrain.Vertices.Skip( used ).Chunk( 4 ).ToList();
		Assert.IsTrue( unused.Count > 0 && unused.All( quad => quad.All( vertex => vertex.Position.X == quad[0].Position.X && vertex.Position.Y == quad[0].Position.Y && vertex.Position.Z == quad[0].Position.Z ) ), "four vertices on one point" );
		Assert.IsTrue( terrain.IsProcedural( 3, 2 ) );
		Assert.IsFalse( terrain.IsProcedural( 3, 0 ), "its samples run past the grid" );
		Assert.IsFalse( terrain.IsProcedural( 1, 1 ), "a hole has no surface" );
		Assert.IsFalse( terrain.IsProcedural( 4, 0 ), "past the heightfield" );
	}
}
