using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// Replays the sky draw's records (docs/reverse/ORACLE.md 7.40, finding X78: tp.exe's 0x5863C0 run in the x86 oracle, numbers only, SkyGolden.cs) on <see cref="Sky.BuildVertices"/>: the triangle lists, the positions,
/// the texture coordinates under fixed scroll offsets, the layers' alpha and, with the globe's flag, the mirrored layers and the cylinder's other rows.
/// </summary>
[TestClass]
public class SkyGoldenTests
{
	private const float Tolerance = 0.01f;

	private static (System.Numerics.Vector3[] Cloud, System.Numerics.Vector3[] Cylinder) Grids() =>
		(Enumerable.Range( 0, Sky.GridSize * Sky.GridSize ).Select( index => Sky.ToEngine( Sky.CloudPosition( index / Sky.GridSize, index % Sky.GridSize ) ) ).ToArray(),
		 Enumerable.Range( 0, Sky.CylinderColumns * Sky.CylinderRings ).Select( index => Sky.ToEngine( Sky.CylinderPosition( index / Sky.CylinderRings, index % Sky.CylinderRings ) ) ).ToArray());

	private static int[] Flatten( IEnumerable<(int V0, int V1, int V2)> triangles ) => triangles.SelectMany( triangle => new[] { triangle.V0, triangle.V1, triangle.V2 } ).ToArray();

	[TestMethod]
	public void TheTriangleListsAreTheOriginals()
	{
		CollectionAssert.AreEqual( SkyGolden.CloudIndices, Flatten( Sky.CloudTriangles() ) );
		CollectionAssert.AreEqual( SkyGolden.CylinderIndices, Flatten( Sky.CylinderTriangles() ) );
	}

	[TestMethod]
	public void TheGridsPositionsAreTheOriginals()
	{
		var (cloud, cylinder) = Grids();
		foreach ( var golden in SkyGolden.Cases )
		{
			for ( var index = 0; index < cloud.Length; index++ )
				AssertNear( golden.CloudPositions, index, cloud[index], mirrored: false, $"{golden.Name} cloud {index}" );
			for ( var index = 0; index < cylinder.Length; index++ )
				AssertNear( golden.CylinderPositions, index, cylinder[index], mirrored: false, $"{golden.Name} cylinder {index}" );
		}
	}

	[TestMethod]
	public void BuildVerticesReplaysTheRecordsOfTheOracle()
	{
		var (cloudPositions, cylinderPositions) = Grids();
		var colours = Enumerable.Repeat( 0xFFFFFFFFu, Sky.GridSize * Sky.GridSize ).ToArray();
		var cloudTriangles = Sky.CloudTriangles().ToArray();
		var cylinderTriangles = Sky.CylinderTriangles().ToArray();
		foreach ( var golden in SkyGolden.Cases )
		{
			var vertices = Sky.BuildVertices( golden.Layers, cloudPositions, colours, cloudTriangles, cylinderPositions, cylinderTriangles, golden.OffsetU, golden.OffsetV, out var clouds, golden.Globe );
			var copies = golden.Globe ? 2 : 1;
			var perRecord = SkyGolden.CloudIndices.Length;
			Assert.AreEqual( golden.Layers * copies * perRecord, clouds, golden.Name );
			Assert.AreEqual( clouds + SkyGolden.CylinderIndices.Length, vertices.Length, golden.Name );
			Assert.AreEqual( golden.Layers * copies + 1, golden.States.Length, golden.Name );
			for ( var record = 0; record < golden.Layers * copies; record++ )
			{
				var layer = record % golden.Layers;
				var mirrored = record >= golden.Layers;
				for ( var corner = 0; corner < perRecord; corner++ )
				{
					var index = SkyGolden.CloudIndices[corner];
					var vertex = vertices[record * perRecord + corner];
					var where = $"{golden.Name} record {record} corner {corner}";
					AssertVertex( vertex.Position, mirrored ? golden.MirroredPositions : golden.CloudPositions, index, mirrored, where );
					AssertUv( vertex.TexCoords, golden.CloudUv, layer * Sky.GridSize * Sky.GridSize + index, where );
					Assert.AreEqual( golden.Alphas[record], (int)(vertex.Normal.Z / 256f), where + " alpha" );
				}
			}
			for ( var corner = 0; corner < SkyGolden.CylinderIndices.Length; corner++ )
			{
				var index = SkyGolden.CylinderIndices[corner];
				var vertex = vertices[clouds + corner];
				var where = $"{golden.Name} cylinder corner {corner}";
				AssertVertex( vertex.Position, golden.CylinderPositions, index, mirrored: false, where );
				AssertUv( vertex.TexCoords, golden.CylinderUv, index, where );
			}
		}
	}

	[TestMethod]
	public void TheGlobesFlagIsTheOnlyDifferenceInTheStates()
	{
		var normal = SkyGolden.Cases.Single( golden => !golden.Globe );
		var globe = SkyGolden.Cases.Single( golden => golden.Globe );
		CollectionAssert.AreEqual( normal.States.Take( normal.Layers ).ToArray(), globe.States.Take( globe.Layers ).ToArray() );
		CollectionAssert.AreEqual( globe.States.Take( globe.Layers ).ToArray(), globe.States.Skip( globe.Layers ).Take( globe.Layers ).ToArray(), "the mirrored layers draw with the layers' state" );
		Assert.AreEqual( normal.States[^1], globe.States[^1], "and so does the cylinder" );
	}

	// the oracle's positions are in the original's axes (x, height, z); the vertices are in the engine's (x, z, height)
	private static void AssertNear( float[] golden, int index, System.Numerics.Vector3 engine, bool mirrored, string where )
	{
		var original = new System.Numerics.Vector3( golden[3 * index], golden[3 * index + 1], golden[3 * index + 2] );
		Assert.AreEqual( original.X, engine.X, Tolerance, where + " x" );
		Assert.AreEqual( original.Y, engine.Z, Tolerance, where + " height" );
		Assert.AreEqual( original.Z, engine.Y, Tolerance, where + " z" );
		Assert.IsFalse( mirrored );
	}

	private static void AssertVertex( OpenTPW.Vector3 position, float[] golden, int index, bool mirrored, string where )
	{
		// a mirrored record's golden positions are the oracle's own mirrored ones
		Assert.AreEqual( golden[3 * index], position.X, Tolerance, where + " x" );
		Assert.AreEqual( golden[3 * index + 1], position.Z, Tolerance, where + " height" );
		Assert.AreEqual( golden[3 * index + 2], position.Y, Tolerance, where + " z" );
	}

	// the vertices' v is flipped for the upload (Sky.Upload)
	private static void AssertUv( OpenTPW.Vector2 uv, float[] golden, int index, string where )
	{
		Assert.AreEqual( golden[2 * index], uv.X, 1e-4f, where + " u" );
		Assert.AreEqual( golden[2 * index + 1], 1f - uv.Y, 1e-4f, where + " v" );
	}
}
