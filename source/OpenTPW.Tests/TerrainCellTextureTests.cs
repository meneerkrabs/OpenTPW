using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class TerrainCellTextureTests
{
	private static int[] Corners( uint word ) => Enumerable.Range( 0, 4 ).Select( corner => TerrainCellTexture.Corner( corner, word ) ).ToArray();

	[TestMethod]
	public void CellWordWithoutTurnBitsKeepsTheTextureUpright()
	{
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, Corners( 0 ) );
		// the stored words carry the texture slot in the high bits and 0x1 / 0x2 / 0x4 for hole and split: none of them touches the texture
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, Corners( 0x1B0007 ) );
		Assert.AreEqual( (0f, 0f), TerrainCellTexture.Coordinate( TerrainCellTexture.Cell00, 0 ) );
		Assert.AreEqual( (1f, 0f), TerrainCellTexture.Coordinate( TerrainCellTexture.Cell10, 0 ), "u runs along x" );
		Assert.AreEqual( (0f, 1f), TerrainCellTexture.Coordinate( TerrainCellTexture.Cell01, 0 ), "v runs along z" );
		Assert.AreEqual( (1f, 1f), TerrainCellTexture.Coordinate( TerrainCellTexture.Cell11, 0 ) );
	}

	[TestMethod]
	public void TurnBitsRotateTheCornerArrayAsTheOriginalRoutineDoes()
	{
		// 0x56F4F0: the array 0,1,2,3 becomes (3,0,1,2) with 0x8, (2,3,0,1) with 0x10 and (1,2,3,0) with 0x20
		CollectionAssert.AreEqual( new[] { 3, 0, 1, 2 }, Corners( 0x8 ) );
		CollectionAssert.AreEqual( new[] { 2, 3, 0, 1 }, Corners( 0x10 ) );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 0 }, Corners( 0x20 ) );
		// the first set turn bit wins, in the routine's order 0x8, 0x10, 0x20
		CollectionAssert.AreEqual( new[] { 3, 0, 1, 2 }, Corners( 0x38 ) );
		CollectionAssert.AreEqual( new[] { 2, 3, 0, 1 }, Corners( 0x30 ) );
	}

	[TestMethod]
	public void MirrorSwapsTheNeighbouringCornersBeforeTheTurn()
	{
		CollectionAssert.AreEqual( new[] { 0, 3, 2, 1 }, Corners( 0x40 ) );
		CollectionAssert.AreEqual( new[] { 1, 0, 3, 2 }, Corners( 0x48 ) );
		CollectionAssert.AreEqual( new[] { 2, 1, 0, 3 }, Corners( 0x50 ) );
		CollectionAssert.AreEqual( new[] { 3, 2, 1, 0 }, Corners( 0x60 ) );
		// mirrored, the u axis runs along z and v along x
		Assert.AreEqual( (0f, 1f), TerrainCellTexture.Coordinate( TerrainCellTexture.Cell10, 0x40 ) );
		Assert.AreEqual( (1f, 0f), TerrainCellTexture.Coordinate( TerrainCellTexture.Cell01, 0x40 ) );
	}

	[TestMethod]
	public void EveryTurnedTextureStillCoversEachTextureCornerOnce()
	{
		for ( uint word = 0; word < 0x80; word++ )
			CollectionAssert.AreEquivalent( new[] { 0, 1, 2, 3 }, Corners( word ), $"word {word:X}" );
	}
}
