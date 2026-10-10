using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class PathTilesTests
{
	[TestMethod]
	public void EveryMaskHasOneShapeAndTurningTheMaskTurnsTheTile()
	{
		for ( var neighbours = 0; neighbours < 256; neighbours++ )
		{
			var mask = PathTiles.Reduce( (byte)neighbours );
			var tile = PathTiles.Choose( mask, 0, 0 );
			var turned = PathTiles.Choose( PathTiles.Turn( mask ), 0, 0 );
			Assert.AreEqual( PathTiles.BaseTexture( tile.TextureIndex ), PathTiles.BaseTexture( turned.TextureIndex ), $"0x{mask:X2}" );
			// Shapes that look the same after a turn (straights, crossings, square, centre) keep their smallest turn.
			if ( PathTiles.Turn( mask ) != mask && PathTiles.Turn( PathTiles.Turn( mask ) ) != mask )
				Assert.AreEqual( (tile.Rotation + 90) % 360, turned.Rotation, $"0x{mask:X2}" );
		}
		Assert.AreEqual( 15, Enumerable.Range( 0, 256 ).Select( mask => PathTiles.BaseTexture( PathTiles.Choose( (byte)mask, 0, 0 ).TextureIndex ) ).Distinct().Count() );
	}

	[TestMethod]
	public void DiagonalsCountOnlyBetweenTwoSides()
	{
		Assert.AreEqual( 0x11, PathTiles.Reduce( 0x93 ), "+X−Y and −X−Y without +X/−X" );
		Assert.AreEqual( 0x1F, PathTiles.Reduce( 0x9F ) );
		Assert.AreEqual( new PathTile( 4, 180 ), PathTiles.Choose( 0x53, 0, 0 ), "the extra +X−Y bit of 0x53 does not change the T" );
		Assert.AreEqual( new PathTile( 0, 0 ), PathTiles.Choose( 0xAA, 0, 0 ), "diagonals alone are no path" );
	}

	[TestMethod]
	public void StraightsAndEdgesPickAVariantByCellAndKeepIt()
	{
		var picks = Enumerable.Range( 0, 64 ).Select( index => PathTiles.Choose( 0x11, index % 8, index / 8 ).TextureIndex ).ToArray();
		CollectionAssert.IsSubsetOf( picks.Distinct().ToArray(), new[] { 2, PathTiles.StraightVariant } );
		Assert.AreEqual( 2, picks.Distinct().Count() );
		// Pinned picks: a change to the hash would change built paths that players already see.
		CollectionAssert.AreEqual( new[] { 2, 19, 19, 2 }, new[] { (0, 0), (1, 0), (0, 1), (1, 1) }.Select( cell => PathTiles.Choose( 0x11, cell.Item1, cell.Item2 ).TextureIndex ).ToArray() );
		CollectionAssert.IsSubsetOf( Enumerable.Range( 0, 16 ).Select( index => PathTiles.Choose( 0x1F, index, 1 ).TextureIndex ).Distinct().ToArray(), new[] { 10, PathTiles.EdgeVariant } );
	}

	[TestMethod]
	public void TurnedTexturesShowTheirSidesWhereTheMaskTurnedThem()
	{
		// Texture sides at rotation 0: +X is the right column (u = 1), +Y the top row, which the terrain shader's
		// V flip (v' = 1 − v) samples at vertex v = 1.
		Assert.AreEqual( (1f, 0.5f), PathTiles.TextureCoordinates( 1, 0.5f, 0 ), "cell +X edge shows texture +X" );
		Assert.AreEqual( (0.5f, 1f), PathTiles.TextureCoordinates( 0.5f, 1, 0 ), "cell +Y edge shows texture +Y" );
		// A quarter turn moves −Y to +X, +X to +Y, +Y to −X: the cell's −X edge shows the texture's +Y side.
		Assert.AreEqual( (0.5f, 1f), PathTiles.TextureCoordinates( 0, 0.5f, 90 ) );
		Assert.AreEqual( (1f, 0.5f), PathTiles.TextureCoordinates( 0.5f, 1, 90 ) );
		Assert.AreEqual( (0.5f, 0f), PathTiles.TextureCoordinates( 1, 0.5f, 90 ) );
		Assert.AreEqual( PathTiles.TextureCoordinates( 0, 0, 0 ), PathTiles.TextureCoordinates( 0, 0, 360 ) );
		for ( var mask = 0; mask < 256; mask++ )
			Assert.AreEqual( PathTiles.Reduce( (byte)mask ), PathTiles.Reduce( PathTiles.Turn( PathTiles.Turn( PathTiles.Turn( PathTiles.Turn( (byte)mask ) ) ) ) ) );
	}

	/// <summary>Every path cell of the original Jungle Easymode save: the saved texture (+17) and turn (+21, u16) follow from the saved neighbour bits (+8).</summary>
	[TestMethod]
	public void OriginalEasymodePathCellsMatchTheRule()
	{
		var path = Path.Combine( OriginalParkImportTests.OriginalDataPath(), "levels", "jungle", "Easymode.TPWI" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( "The original Jungle Easymode.TPWI fixture is missing." );
		using var reader = new SaveReader( path );
		var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) ) );
		var park = OriginalParkImport.Import( reader.ReadFile(), map );
		Assert.AreEqual( 78, park.PathCells.Count );
		foreach ( var (x, y) in park.PathCells )
		{
			var cell = park.Cells[x, y];
			var saved = new PathTile( cell.PathTexture, cell.PathRotation );
			var chosen = PathTiles.Choose( (byte)cell.PathConnections, x, y );
			Assert.AreEqual( (PathTiles.BaseTexture( saved.TextureIndex ), saved.Rotation), (PathTiles.BaseTexture( chosen.TextureIndex ), chosen.Rotation), $"({x}, {y}) 0x{(byte)cell.PathConnections:X2}" );
		}
		CollectionAssert.AreEquivalent( new[] { 2, 3, 4, 5, 6, 7, 9, 10, 15, PathTiles.StraightVariant, PathTiles.EdgeVariant },
			park.PathCells.Select( cell => (int)park.Cells[cell.X, cell.Y].PathTexture ).Distinct().ToArray() );

		// Unchanged cells keep the saved tile, ride entrances included; a new path cell changes its neighbours to the rule.
		var paths = park.PathCells.ToHashSet();
		foreach ( var (x, y) in paths )
			Assert.AreEqual( new PathTile( park.Cells[x, y].PathTexture, park.Cells[x, y].PathRotation ),
				PathTiles.ForCell( x, y, map.CellCountX, map.CellCountY, ( nx, ny ) => paths.Contains( (nx, ny) ), park.Cells ) );
		var (endX, endY) = paths.First( cell => PathTiles.Reduce( PathTiles.Neighbours( cell.X, cell.Y, map.CellCountX, map.CellCountY, ( nx, ny ) => paths.Contains( (nx, ny) ) ) ) == 0x11
			&& !paths.Contains( (cell.X + 1, cell.Y) ) && !paths.Contains( (cell.X + 1, cell.Y - 1) ) && !paths.Contains( (cell.X + 1, cell.Y + 1) )
			&& !park.Cells[cell.X + 1, cell.Y].IsOccupied );
		var extended = paths.Append( (endX + 1, endY) ).ToHashSet();
		var tile = PathTiles.ForCell( endX, endY, map.CellCountX, map.CellCountY, ( nx, ny ) => extended.Contains( (nx, ny) ), park.Cells );
		Assert.AreEqual( (4, 0), (tile.TextureIndex, tile.Rotation), "a straight north–south cell with a new east neighbour becomes a T open to −Y, +X and +Y" );
	}
}
