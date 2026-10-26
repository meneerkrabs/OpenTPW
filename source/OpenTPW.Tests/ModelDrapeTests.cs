using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// The model drape of tp.exe's 0x469940 / 0x469A80 ([BIN:TPW-EXE:0x00469A80]): the chain matrix, the point transform, the landscape triangle and the drape height against the x86 oracle's cases
/// (<see cref="ModelDrapeGolden"/>, bit for bit, without game data), and the drape sampler's reading of the far edge.
/// </summary>
[TestClass]
public class ModelDrapeTests
{
	private static Matrix4x4 Matrix( float[] m ) => new( m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15] );

	private static Func<float, float, float> Landscape( ModelDrapeGolden.Case golden )
	{
		var stride = golden.CellsX + 1;
		return ( x, z ) => LandscapeSampler.SampleDrape( golden.CellsX, golden.CellsZ, golden.SizeX, golden.SizeZ, ( cx, cz ) => golden.Heights[cz * stride + cx], ( cx, cz ) => golden.Anti[cz * golden.CellsX + cx], x, z );
	}

	[TestMethod]
	public void TheDrapeReproducesTheOraclesVerticesBitForBit()
	{
		var (drapedNodes, vertices, skipped) = (0, 0, 0);
		foreach ( var golden in ModelDrapeGolden.Cases )
		{
			var landscape = Landscape( golden );
			var chains = new Matrix4x4[golden.Nodes.Length];
			for ( var index = 0; index < golden.Nodes.Length; index++ )
			{
				var node = golden.Nodes[index];
				chains[index] = ModelDrape.Chain( Matrix( node.Matrix ), node.Parent < 0 ? Matrix4x4.Identity : chains[node.Parent] );
				// a node without 0x20000 (the loader's drape flag), or with 0x200, moves nothing
				if ( (node.Flags & 0x20000) == 0 || (node.Flags & 0x200) != 0 )
				{
					Assert.AreEqual( 0, node.World.Length, "the oracle moved no vertex of this node" );
					skipped++;
					continue;
				}
				Assert.AreEqual( node.Vertices.Length, node.World.Length );
				drapedNodes++;
				for ( var vertex = 0; vertex < node.Vertices.Length; vertex++ )
				{
					var source = node.Vertices[vertex];
					var placed = ModelDrape.Drape( chains[index], new Vector3( source[0], source[1], source[2] ), golden.BaseHeight, landscape );
					uint[] expected = node.World[vertex];
					Assert.AreEqual( expected[0], BitConverter.SingleToUInt32Bits( placed.X ), $"x of vertex {vertex} of node {index}" );
					Assert.AreEqual( expected[1], BitConverter.SingleToUInt32Bits( placed.Y ), $"height of vertex {vertex} of node {index}: {placed.Y} against {BitConverter.UInt32BitsToSingle( expected[1] )}" );
					Assert.AreEqual( expected[2], BitConverter.SingleToUInt32Bits( placed.Z ), $"z of vertex {vertex} of node {index}" );
					vertices++;
				}
			}
		}
		Assert.IsTrue( drapedNodes >= 60 && skipped >= 60 && vertices >= 250, $"{drapedNodes} draped nodes, {skipped} others, {vertices} vertices" );
	}

	[TestMethod]
	public void TheDrapeTriangleIsTheCellSplitByTheCellWordWithoutThePlanarShortcut()
	{
		// a flat cell with one corner raised: planar sampling would give a plane, the drape follows the split
		float[] corners = [0, 0, 0, 10]; // a, b, c, d
		for ( var anti = 0; anti < 2; anti++ )
		{
			var at = ( float fx, float fz ) => LandscapeSampler.DrapeInCell( corners[0], corners[1], corners[2], corners[3], fx, fz, anti == 1 );
			// the diagonal itself is shared by both triangles
			if ( anti == 0 )
				Assert.AreEqual( 5f, at( 0.5f, 0.5f ), 1e-5f, "the middle of the (x, z)-(x+1, z+1) diagonal" );
			else
				Assert.AreEqual( 0f, at( 0.5f, 0.5f ), 1e-5f, "the middle of the other diagonal, between two zero corners" );
			Assert.AreEqual( 10f, at( 1, 1 ), 1e-5f );
			Assert.AreEqual( 0f, at( 0, 0 ), 1e-5f );
			Assert.AreEqual( 0f, at( 1, 0 ), 1e-5f );
			Assert.AreEqual( 0f, at( 0, 1 ), 1e-5f );
		}
		// off the diagonal: the planar shortcut (a cell without the 0x800 flag) answers one plane, 0 here, and the drape the triangle's 2.5
		Assert.AreEqual( 0f, LandscapeSampler.InCell( 0, 0, 0, 10, 0.75f, 0.25f, false, false ), 1e-6f );
		Assert.AreEqual( 2.5f, LandscapeSampler.DrapeInCell( 0, 0, 0, 10, 0.75f, 0.25f, false ), 1e-5f );
	}

	[TestMethod]
	public void ASamplePastTheFarEdgeReadsTheNextRowsStartAsTheIndexArithmeticDoes()
	{
		// 2 x 2 cells, corner heights by (x, z): the array is (x + 3 z)-indexed
		float Corner( int x, int z ) => 1 + x + 3 * z;
		bool Anti( int x, int z ) => false;
		// the far edge exactly: the far column weighs 1, the corner row past it 0
		Assert.AreEqual( Corner( 2, 1 ), LandscapeSampler.SampleDrape( 2, 2, 10, 10, Corner, Anti, 20, 10 ), 1e-4f );
		// the corner (2, 2): its own value, the rows past it (the array's end) read 0 with weight 0
		Assert.AreEqual( Corner( 2, 2 ), LandscapeSampler.SampleDrape( 2, 2, 10, 10, Corner, Anti, 20, 20 ), 1e-4f );
		// a point left of the origin reads the first cell at its origin
		Assert.AreEqual( Corner( 0, 0 ), LandscapeSampler.SampleDrape( 2, 2, 10, 10, Corner, Anti, -5, -5 ), 1e-4f );
		// a point 2.5 past the far edge: column 2 + 0.25 of a cell; the next corner in the flat array is the next row's first (x = 0, z + 1), weighted by 0.25
		var expected = (1 - 0.25f) * Corner( 2, 0 ) + 0.25f * Corner( 0, 1 );
		Assert.AreEqual( expected, LandscapeSampler.SampleDrape( 2, 2, 10, 10, Corner, Anti, 22.5f, 0 ), 1e-4f );
	}

	[TestMethod]
	public void TheChainAddsTheParentsTranslationLastAndKeepsTheFourthColumn()
	{
		var parent = Matrix4x4.CreateTranslation( 3, 4, 5 );
		var node = Matrix4x4.CreateTranslation( 10, 20, 30 );
		var chain = ModelDrape.Chain( node, parent );
		Assert.AreEqual( new Vector3( 13, 24, 35 ), new Vector3( chain.M41, chain.M42, chain.M43 ) );
		Assert.AreEqual( 1f, chain.M44 );
		// a quarter turn about the vertical axis of the parent turns the node's translation
		var turn = new Matrix4x4( 0, 0, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1 );
		var turned = ModelDrape.Chain( Matrix4x4.CreateTranslation( 1, 2, 3 ), turn );
		Assert.AreEqual( Vector3.Transform( new Vector3( 1, 2, 3 ), turn ), new Vector3( turned.M41, turned.M42, turned.M43 ) );
		Assert.AreEqual( new Vector3( 7, 8, 9 ), ModelDrape.Transform( Matrix4x4.Identity, new Vector3( 7, 8, 9 ) ) );
	}

	[TestMethod]
	public void TheDrapeHeightCancelsTheInstanceHeightUpToRounding()
	{
		// the vertex sits at height (model + instance height) in the grid's frame; the drape sets landscape - instance height + that
		foreach ( var (model, instance, landscape) in new[] { (0.75f, 6.25f, 9.5f), (4f, -3f, 0.125f), (0f, 100.5f, 100.5f) } )
			Assert.AreEqual( landscape + model, ModelDrape.Height( model + instance, landscape, instance ), 1e-4f );
	}
}
