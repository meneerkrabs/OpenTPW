using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using V3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

[TestClass]
public class HeadReferenceBasisTests
{
	// Independent geometric construction: rotate an XYZ frame about X and then Y.
	// Unlike production this does not construct up/direction or take their cross product.
	private static Matrix4x4 ArtBasis( int row, int column ) =>
		Matrix4x4.CreateRotationX( row * MathF.PI / 6 - MathF.PI / 2 ) * Matrix4x4.CreateRotationY( -column * MathF.PI / 4 );

	private static Matrix4x4 CameraFor( Matrix4x4 node, int row, int column, float turns )
	{
		var basis = ArtBasis( row, column );
		var camera = Matrix4x4.CreateRotationZ( turns * 2 * MathF.PI ) * basis * node;
		var direction = new V3( basis.M31, basis.M32, basis.M33 );
		camera.Translation = V3.Transform( direction * 30, node );
		return camera;
	}

	private static Matrix4x4 Mirrored( Matrix4x4 node ) => Matrix4x4.CreateScale( -1, 1, -1 ) * node;

	[TestMethod]
	public void EveryFrameRemovesItsOwnReferenceBasisBeforeExtractingCameraRoll()
	{
		var nodes = new[]
		{
			Matrix4x4.Identity,
			Matrix4x4.CreateRotationX( 0.27f ) * Matrix4x4.CreateRotationY( -0.62f ) * Matrix4x4.CreateTranslation( 7, -3, 9 ),
			Matrix4x4.CreateScale( 0.4f, 1.7f, 2.3f ) * Matrix4x4.CreateRotationZ( 0.41f ) * Matrix4x4.CreateTranslation( -11, 6, 4 )
		};
		foreach ( var node in nodes )
		foreach ( var mirrored in new[] { false, true } )
		for ( var row = 0; row < 7; row++ )
		for ( var inputColumn = 0; inputColumn < 8; inputColumn++ )
		foreach ( var turns in new[] { -0.375f, -0.125f, 0f, 0.125f, 0.375f } )
		{
			var column = row is 0 or 6 ? 0 : inputColumn;
			var camera = CameraFor( mirrored ? Mirrored( node ) : node, row, column, turns );
			var actual = HeadPresentation.Compute( node, mirrored ? HeadPresentation.MirroredFlag : 0, camera );
			Assert.AreEqual( row, actual.Row );
			Assert.AreEqual( column, actual.Column );
			Assert.AreEqual( turns, actual.Roll, 2e-5f, $"row {row}, column {column}, mirrored {mirrored}" );
			Assert.AreEqual( node.Translation, actual.Anchor );
			Assert.AreEqual( HeadPresentation.HeadScale, actual.Scale );
		}
	}

	[TestMethod]
	public void CameraOrientationChangesRollWithoutChangingPositionOrSelectedFrame()
	{
		var first = CameraFor( Matrix4x4.Identity, 2, 3, 0 );
		var second = CameraFor( Matrix4x4.Identity, 2, 3, 0.25f );
		Assert.AreEqual( first.Translation, second.Translation );
		var a = HeadPresentation.Compute( Matrix4x4.Identity, 0, first );
		var b = HeadPresentation.Compute( Matrix4x4.Identity, 0, second );
		Assert.AreEqual( a.Frame, b.Frame );
		Assert.AreEqual( 0f, a.Roll, 1e-5f );
		Assert.AreEqual( 0.25f, b.Roll, 1e-5f );
	}

	[TestMethod]
	public void WorldUpIsNotTheArtUpForObliqueAndPoleFrames()
	{
		foreach ( var (row, column) in new[] { (0, 0), (1, 2), (2, 5), (4, 7), (6, 0) } )
		{
			var camera = CameraFor( Matrix4x4.Identity, row, column, -0.125f );
			var actual = HeadPresentation.Compute( Matrix4x4.Identity, 0, camera );
			Assert.AreEqual( -0.125f, actual.Roll, 1e-5f );
			// The removed world-axis approximation returned zero for every identity node.
			Assert.AreNotEqual( 0f, actual.Roll );
		}
	}

	[TestMethod]
	public void RendererViewBridgePreservesRightUpAndFrontAfterWorldAxisConversion()
	{
		foreach ( var row in new[] { 0, 2, 3, 5, 6 } )
		foreach ( var column in new[] { 0, 1, 4, 7 } )
		{
			var native = CameraFor( Matrix4x4.Identity, row, row is 0 or 6 ? 0 : column, 0.125f );
			// Independently form an engine camera world matrix: right/up/backward, Z up.
			var engineWorld = new Matrix4x4(
				native.M11, native.M13, native.M12, 0,
				native.M21, native.M23, native.M22, 0,
				-native.M31, -native.M33, -native.M32, 0,
				native.M41, native.M43, native.M42, 1 );
			Assert.IsTrue( Matrix4x4.Invert( engineWorld, out var view ) );
			var converted = HeadPresentation.CameraFromEngineView( view, engineWorld.Translation );
			var actual = HeadPresentation.Compute( Matrix4x4.Identity, 0, converted );
			Assert.AreEqual( row, actual.Row );
			Assert.AreEqual( row is 0 or 6 ? 0 : column, actual.Column );
			Assert.AreEqual( 0.125f, actual.Roll, 2e-5f );
		}
	}

	[TestMethod]
	public void RealLookToCameraBankMatchesNodeUpInTheRenderedBillboardPlane()
	{
		foreach ( var side in new[] { -1f, 1f } )
		foreach ( var nodeAngle in new[] { -0.7f, 0f, 0.35f } )
		foreach ( var cameraAngle in new[] { -0.4f, 0f, 0.6f } )
		{
			var eye = new V3( 0, side * 20, 0 );
			var baselineRight = new V3( -side, 0, 0 );
			var cameraUp = V3.UnitZ * MathF.Cos( cameraAngle ) + baselineRight * MathF.Sin( cameraAngle );
			// The real camera path in Camera.CalcViewProjMatrix uses CreateLookTo.
			var view = Matrix4x4.CreateLookTo( eye, -eye, cameraUp );
			var node = Matrix4x4.CreateRotationZ( nodeAngle );
			var actual = HeadPresentation.Compute( node, 0, HeadPresentation.CameraFromEngineView( view, eye ) );
			Assert.AreEqual( 3, actual.Row );
			Assert.AreEqual( side > 0 ? 0 : 4, actual.Column );
			// Horizon art has local +Y up. Project the actual transformed node up onto
			// the billboard's engine right/up; spriteUp = up*cos(angle) - right*sin(angle).
			var nodeUpEngine = new V3( node.M21, node.M23, node.M22 );
			var right = new V3( view.M11, view.M21, view.M31 );
			var up = new V3( view.M12, view.M22, view.M32 );
			var expected = MathF.Atan2( -V3.Dot( nodeUpEngine, right ), V3.Dot( nodeUpEngine, up ) ) / (2 * MathF.PI);
			Assert.AreEqual( expected, actual.Roll, 2e-5f );
		}
	}

	[TestMethod]
	public void NonfiniteCameraOrientationKeepsSelectedFrameButFallsBackToFiniteRoll()
	{
		var camera = CameraFor( Matrix4x4.Identity, 2, 3, 0.125f );
		camera.M21 = float.NaN;
		var actual = HeadPresentation.Compute( Matrix4x4.Identity, 0, camera );
		Assert.AreEqual( 19, actual.Frame );
		Assert.AreEqual( 0f, actual.Roll );
	}

	[TestMethod]
	public void InvalidDirectionAndSingularNodeKeepFiniteDefensiveFallback()
	{
		var invalid = Matrix4x4.Identity;
		invalid.Translation = new V3( float.NaN, 0, 1 );
		foreach ( var camera in new[] { invalid, Matrix4x4.Identity } )
		{
			var actual = HeadPresentation.Compute( Matrix4x4.Identity, 0, camera );
			Assert.AreEqual( 0, actual.Frame );
			Assert.AreEqual( 0f, actual.Roll );
		}
		var singular = HeadPresentation.Compute( Matrix4x4.CreateScale( 0, 1, 1 ), 0, CameraFor( Matrix4x4.Identity, 2, 3, 0.25f ) );
		Assert.AreEqual( 0, singular.Frame );
		Assert.AreEqual( 0f, singular.Roll );
	}

	[TestMethod]
	public void HiddenNodesDoNotRequireValidCameraOrNodeMatrices()
	{
		var hidden = HeadPresentation.Compute( new Matrix4x4(), HeadPresentation.HiddenFlag | HeadPresentation.MirroredFlag, new Matrix4x4() );
		Assert.IsFalse( hidden.Visible );
		Assert.AreEqual( -1, hidden.Frame );
		Assert.AreEqual( 0f, hidden.Roll );
	}
}
