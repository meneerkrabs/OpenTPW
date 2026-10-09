using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ParkPlacementTests
{
	[DataTestMethod]
	[DataRow( -33f, -32f )]
	[DataRow( -32f, -32f )]
	[DataRow( -31f, -30f )]
	[DataRow( -0.01f, 0f )]
	[DataRow( 0f, 0f )]
	[DataRow( 1f, 2f )]
	[DataRow( 30f, 30f )]
	[DataRow( 31f, 30f )]
	public void SnapsToActualPlaneCellCenters( float coordinate, float expected )
	{
		Assert.AreEqual( -33f, ParkPlacement.MinimumCoordinate );
		Assert.AreEqual( 31f, ParkPlacement.MaximumCoordinate );
		Assert.IsTrue( ParkPlacement.TrySnapToCell( new Vector3( coordinate, coordinate, 0 ), out var position ) );
		Assert.AreEqual( new Vector3( expected, expected, 0 ), position );
	}

	[DataTestMethod]
	[DataRow( -33.01f )]
	[DataRow( 31.01f )]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	public void RejectsOutsideOrInvalidCoordinatesWithoutClamping( float coordinate )
	{
		Assert.IsFalse( ParkPlacement.TrySnapToCell( new Vector3( coordinate, 0, 0 ), out var position ) );
		Assert.AreEqual( Vector3.Zero, position );
		Assert.IsFalse( ParkPlacement.TrySnapToCell( new Vector3( 0, coordinate, 0 ), out position ) );
	}

	[TestMethod]
	public void EveryCellCenterRemainsStable()
	{
		for ( int row = 0; row < ParkPlacement.TileCount; row++ )
		{
			for ( int column = 0; column < ParkPlacement.TileCount; column++ )
			{
				var center = new Vector3( -32 + column * ParkPlacement.TileSize, -32 + row * ParkPlacement.TileSize, 0 );
				Assert.IsTrue( ParkPlacement.TrySnapToCell( center, out var position ) );
				Assert.AreEqual( center, position );
				Assert.IsTrue( ParkPlacement.IsWithinBounds( position, ParkPlacement.TileSize / 2 ) );
			}
		}
	}

	[TestMethod]
	public void FootprintMustFitOnBothAxes()
	{
		Assert.IsTrue( ParkPlacement.IsWithinBounds( new Vector3( -32, 30, 0 ), 1 ) );
		Assert.IsFalse( ParkPlacement.IsWithinBounds( new Vector3( -32, 30, 0 ), 1.01f ) );
		Assert.IsFalse( ParkPlacement.IsWithinBounds( new Vector3( 30, -32, 0 ), 1.01f ) );
		Assert.IsTrue( ParkPlacement.IsWithinBounds( new Vector3( -1, -1, 0 ), 32 ) );
		Assert.IsFalse( ParkPlacement.IsWithinBounds( Vector3.Zero, 32 ) );
	}

	[DataTestMethod]
	[DataRow( -1f )]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	public void RejectsInvalidFootprintRadius( float radius )
	{
		Assert.IsFalse( ParkPlacement.IsWithinBounds( Vector3.Zero, radius ) );
	}

	[TestMethod]
	public void RejectsPositionsOffGround()
	{
		Assert.IsFalse( ParkPlacement.IsWithinBounds( new Vector3( 0, 0, 1 ), 0 ) );
		Assert.IsFalse( ParkPlacement.TrySnapToCell( new Vector3( 0, 0, float.NaN ), out _ ) );
		Assert.IsFalse( ParkPlacement.TrySnapToCell( new Vector3( 0, 0, 1 ), out _ ) );
	}

	[TestMethod]
	public void IntersectsForwardRayWithoutRequiringNormalizedDirection()
	{
		Assert.IsTrue( ParkPlacement.TryIntersectGround( new Vector3( 2, 4, 10 ), new Vector3( 2, -4, -20 ), out var position ) );
		Assert.AreEqual( new Vector3( 3, 2, 0 ), position );
		Assert.IsTrue( ParkPlacement.TryIntersectGround( Vector3.Zero, new Vector3( 0, 0, -1 ), out position ) );
		Assert.AreEqual( Vector3.Zero, position );
	}

	[TestMethod]
	public void RejectsParallelBackwardAndInvalidRays()
	{
		var origin = new Vector3( 0, 0, 10 );
		Assert.IsFalse( ParkPlacement.TryIntersectGround( origin, new Vector3( 1, 0, 0 ), out _ ) );
		Assert.IsFalse( ParkPlacement.TryIntersectGround( origin, Vector3.Zero, out _ ) );
		Assert.IsFalse( ParkPlacement.TryIntersectGround( origin, new Vector3( 0, 0, 1 ), out _ ) );
		Assert.IsFalse( ParkPlacement.TryIntersectGround( origin, new Vector3( float.NaN, 0, -1 ), out _ ) );
		Assert.IsFalse( ParkPlacement.TryIntersectGround( new Vector3( 0, 0, float.PositiveInfinity ), new Vector3( 0, 0, -1 ), out _ ) );
	}

	[TestMethod]
	public void UnprojectsPerspectiveCenterOntoGround()
	{
		var view = Matrix4x4.CreateLookAt( new System.Numerics.Vector3( 4, 6, 20 ), new System.Numerics.Vector3( 4, 6, 0 ), System.Numerics.Vector3.UnitY );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( MathF.PI / 3, 1, 0.1f, 100 );
		Assert.IsTrue( ParkPlacement.TryGetPosition( new Vector2( 50, 50 ), new Vector2( 100, 100 ), view, projection, out var position ) );
		Assert.AreEqual( new Vector3( 4, 6, 0 ), position );
	}

	[TestMethod]
	public void ScreenTopMapsToPositiveWorldY()
	{
		var view = Matrix4x4.CreateLookAt( new System.Numerics.Vector3( 0, 0, 10 ), System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY );
		var projection = Matrix4x4.CreateOrthographic( 20, 20, 0.1f, 100 );
		Assert.IsTrue( ParkPlacement.TryGetPosition( new Vector2( 75, 25 ), new Vector2( 100, 100 ), view, projection, out var position ) );
		Assert.AreEqual( new Vector3( 6, 6, 0 ), position );
	}

	[TestMethod]
	public void RejectsInvalidViewportPointerAndMatrices()
	{
		var viewport = new Vector2( 100, 100 );
		Assert.IsFalse( ParkPlacement.TryGetPosition( Vector2.Zero, Vector2.Zero, Matrix4x4.Identity, Matrix4x4.Identity, out _ ) );
		Assert.IsFalse( ParkPlacement.TryGetPosition( Vector2.Zero, new Vector2( float.NaN, 100 ), Matrix4x4.Identity, Matrix4x4.Identity, out _ ) );
		Assert.IsFalse( ParkPlacement.TryGetPosition( Vector2.Zero, new Vector2( 100, -1 ), Matrix4x4.Identity, Matrix4x4.Identity, out _ ) );
		Assert.IsFalse( ParkPlacement.TryGetPosition( Vector2.Zero, new Vector2( 100, float.PositiveInfinity ), Matrix4x4.Identity, Matrix4x4.Identity, out _ ) );
		Assert.IsFalse( ParkPlacement.TryGetPosition( new Vector2( -1, 50 ), viewport, Matrix4x4.Identity, Matrix4x4.Identity, out _ ) );
		Assert.IsFalse( ParkPlacement.TryGetPosition( new Vector2( 50, 101 ), viewport, Matrix4x4.Identity, Matrix4x4.Identity, out _ ) );
		Assert.IsFalse( ParkPlacement.TryGetPosition( new Vector2( float.NaN, 50 ), viewport, Matrix4x4.Identity, Matrix4x4.Identity, out _ ) );
		Assert.IsFalse( ParkPlacement.TryGetPosition( Vector2.Zero, viewport, default, Matrix4x4.Identity, out _ ) );
		var invalid = Matrix4x4.Identity;
		invalid.M11 = float.NaN;
		Assert.IsFalse( ParkPlacement.TryGetPosition( Vector2.Zero, viewport, invalid, Matrix4x4.Identity, out _ ) );
	}

	[TestMethod]
	public void RejectsUnprojectedHitOutsideTerrainAndCameraFacingAway()
	{
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( MathF.PI / 3, 1, 0.1f, 100 );
		var outsideView = Matrix4x4.CreateLookAt( new System.Numerics.Vector3( 40, 0, 10 ), new System.Numerics.Vector3( 40, 0, 0 ), System.Numerics.Vector3.UnitY );
		Assert.IsFalse( ParkPlacement.TryGetPosition( new Vector2( 50, 50 ), new Vector2( 100, 100 ), outsideView, projection, out _ ) );
		var awayView = Matrix4x4.CreateLookAt( new System.Numerics.Vector3( 0, 0, 10 ), new System.Numerics.Vector3( 0, 0, 20 ), System.Numerics.Vector3.UnitY );
		Assert.IsFalse( ParkPlacement.TryGetPosition( new Vector2( 50, 50 ), new Vector2( 100, 100 ), awayView, projection, out _ ) );
	}
}
