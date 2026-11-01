using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>RIDES-021: the normal refresh the cursor pick's cell walk runs before testing a placed object's model (tp.exe 0x473c30 -> 0x473a40).</summary>
[TestClass]
public class ObjectPickNormalRefreshTests
{
	[TestMethod]
	public void TheRefreshedNormalIsTheUnitCrossOfTheEdgesFromTheSecondCornerWord()
	{
		// base = corner word 2 (0, 0, 0), e1 to word 1 (1, 0, 0), e2 to word 3 (0, 1, 0): the original's order gives -(e1 x e2)
		var normal = ObjectPickModel.RefreshedNormal( new NVector3( 1, 0, 0 ), new NVector3( 0, 0, 0 ), new NVector3( 0, 1, 0 ) );
		Assert.AreEqual( new NVector3( 0, 0, -1 ), normal );
		var scaled = ObjectPickModel.RefreshedNormal( new NVector3( 3, 0, 0 ), new NVector3( 0, 0, 0 ), new NVector3( 0, 2, 0 ) );
		Assert.AreEqual( 1f, scaled.Length(), 1e-6f, "unit length whatever the face's size" );
	}

	[TestMethod]
	public void ADegenerateFaceGetsTheDownwardNormalWhenItsXPartIsNotANumber()
	{
		var point = new NVector3( 2, 2, 2 );
		Assert.AreEqual( new NVector3( 0, -1, 0 ), ObjectPickModel.RefreshedNormal( point, point, point ), "0 / 0 in x (0x473a40: the NaN test of the x part)" );
	}

	[TestMethod]
	public void FacesSharingANormalIndexKeepTheLastFacesNormalInStorageOrder()
	{
		// two faces, both on index 1 of a 3-entry block; the second is a vertical face
		var positions = new[] { new NVector3( 0, 0, 0 ), new NVector3( 1, 0, 0 ), new NVector3( 0, 1, 0 ), new NVector3( 0, 0, 1 ) };
		var block = new[] { new NVector3( 9, 9, 9 ), new NVector3( 7, 7, 7 ), new NVector3( 8, 8, 8 ) };
		var faces = new[] { 1, 0, 2, 3, 0, 2 };
		var result = ObjectPickModel.RefreshedFaceNormals( block, new[] { 1, 1 }, faces, i => positions[i] );
		Assert.AreEqual( 2, result.Length );
		Assert.AreEqual( result[0], result[1], "both read the same index back" );
		Assert.AreEqual( ObjectPickModel.RefreshedNormal( positions[3], positions[0], positions[2] ), result[1], "the later face wrote last" );
	}

	[TestMethod]
	public void AFaceWithoutAnIndexInsideTheBlockIsLeftOut()
	{
		var positions = new[] { new NVector3( 0, 0, 0 ), new NVector3( 1, 0, 0 ), new NVector3( 0, 1, 0 ) };
		var result = ObjectPickModel.RefreshedFaceNormals( new[] { new NVector3( 5, 5, 5 ) }, new[] { -1, 4 }, new[] { 0, 1, 2, 0, 1, 2 }, i => positions[i] );
		CollectionAssert.AreEqual( new[] { default( NVector3 ), default( NVector3 ) }, result );
	}

	[TestMethod]
	public void TheRefreshRunsOnceAtTheFirstModelTestAndKeepsTheRootNodesNormals()
	{
		var model = ObjectRayPickTests.TowerModel();
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		var placement = new ObjectPlacement( 10, 10, 0, System.Numerics.Vector2.Zero, 0 ).ModelToEngine;
		var refresh = new ObjectPickModel.NormalRefresh();
		IReadOnlyList<NVector3>? NoVertices( int mesh ) => null;
		var eye = new NVector3( 21, 21, 130 );
		var direction = new NVector3( 0, 0, -1 );
		Assert.IsFalse( refresh.Done );
		Assert.IsTrue( ObjectPickModel.TryHitLine( model, rest, _ => false, NoVertices, placement, eye, direction, out var withRefresh, refresh ) );
		Assert.IsTrue( refresh.Done );
		Assert.IsNotNull( refresh.RootFaceNormals, "the slab is the root node and has a mesh" );
		var first = refresh.RootFaceNormals;
		Assert.IsTrue( ObjectPickModel.TryHitLine( model, rest, _ => false, NoVertices, placement, eye, direction, out var again, refresh ) );
		Assert.AreSame( first, refresh.RootFaceNormals, "the second test does not refresh again (0x473c30 clears model flag 0x40000)" );
		Assert.IsTrue( ObjectPickModel.TryHitLine( model, rest, _ => false, NoVertices, placement, eye, direction, out var without ), "an instance without a conform keeps its stored normals" );
		Assert.AreEqual( without.Z, withRefresh.Z, 1e-3f, "the shipped-style quad's stored and refreshed planes agree" );
		Assert.AreEqual( withRefresh, again );
	}
}
