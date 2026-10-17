using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The park camera's pose and projection from tp.exe (UI-101, UI-094): the eye computation 0x42b1c0, the init 0x42a990 and the pick frustum 0x56b790.</summary>
[TestClass]
public class ParkCameraPoseTests
{
	[TestMethod]
	public void ThePitchRunsFrom45To65DegreesOverTheZoomRange()
	{
		Assert.AreEqual( MathF.PI / 4, ParkCameraMode.PitchOf( 70 ), 1e-5f );
		Assert.AreEqual( 1.134464f, ParkCameraMode.PitchOf( 130 ), 1e-5f );
		// (1 - 2/3) x 0.7853982 + 2/3 x 1.134464 at the Home zoom 110
		Assert.AreEqual( 1.018130f, ParkCameraMode.PitchOf( 110 ), 1e-4f );
	}

	[TestMethod]
	public void TheHomeEyeSitsNinetyThreeUnitsHighAndFiftyEightBehindTheTarget()
	{
		var eye = ParkCameraMode.EyeOffset( 110, ParkCameraMode.HomeYaw, 0 );
		Assert.AreEqual( 110 * MathF.Sin( 1.018130f ) * 0.2f, eye.Z, 1e-3f, "height: zoom x sin(pitch), in engine units" );
		Assert.AreEqual( 0f, eye.X, 1e-4f );
		Assert.AreEqual( -110 * MathF.Cos( 1.018130f ) * 0.2f, eye.Y, 1e-3f, "behind the target: the original's yaw 0 looks along +Y" );
		Assert.AreEqual( 0f, ParkCameraMode.OriginalYaw( ParkCameraMode.HomeYaw ), 1e-6f );
	}

	[TestMethod]
	public void TheViewLooksDownByThePitchAndTheGroundHeightOnlyLiftsTheEye()
	{
		var direction = ParkCameraMode.ViewDirection( 110, ParkCameraMode.HomeYaw );
		Assert.AreEqual( 0f, direction.X, 1e-5f );
		Assert.AreEqual( MathF.Cos( 1.018130f ), direction.Y, 1e-4f );
		Assert.AreEqual( -MathF.Sin( 1.018130f ), direction.Z, 1e-4f );
		var lifted = ParkCameraMode.EyeOffset( 110, ParkCameraMode.HomeYaw, 3 ) - ParkCameraMode.EyeOffset( 110, ParkCameraMode.HomeYaw, 0 );
		Assert.AreEqual( 3f, lifted.Z, 1e-5f );
		Assert.AreEqual( 0f, lifted.X + lifted.Y, 1e-5f );
	}

	[TestMethod]
	public void AQuarterTurnOfTheOriginalYawLooksAlongPlusX()
	{
		// the original's forward on the ground is (sin yaw, cos yaw) in (x, z) = engine (X, Y): yaw pi/2 looks along +X
		var yaw = -90f - 90f;
		Assert.AreEqual( MathF.PI / 2, ParkCameraMode.OriginalYaw( yaw ), 1e-5f );
		var direction = ParkCameraMode.ViewDirection( 110, yaw );
		Assert.IsTrue( direction.X > 0.5f && MathF.Abs( direction.Y ) < 1e-4f );
	}

	[TestMethod]
	public void ANewCameraStartsAtTheOriginalHomePoseAndProjection()
	{
		ParkCameraMode.TargetExtent = 32; ParkCameraMode.InitialTarget = Vector3.Zero;
		var camera = new ParkCameraMode();
		Assert.AreEqual( 110f, camera.Height ); Assert.AreEqual( -90f, camera.Yaw );
		Assert.IsTrue( camera.UsesOriginalProjection );
		Assert.AreEqual( 90f, camera.FieldOfView );
		Assert.AreEqual( 475f, ParkCameraMode.HomeTargetX ); Assert.AreEqual( 175f, ParkCameraMode.HomeTargetZ );
	}

	[TestMethod]
	public void TheProjectionSeesHalfAUnitOfHeightAtUnitDepthAndTheWidthFollowsTheAspect()
	{
		Assert.AreEqual( 0.5f, OriginalProjection.HalfHeight( 90 ), 1e-6f );
		var projection = OriginalProjection.Create( 4f / 3f );
		Assert.AreEqual( 2f, projection.M22, 1e-4f, "half height 0.5: y = 2 x / z" );
		Assert.AreEqual( 1.5f, projection.M11, 1e-4f, "half width 0.5 / 0.75 = 2/3: x scale 1.5" );
		var wide = OriginalProjection.Create( 16f / 9f );
		Assert.AreEqual( 2f, wide.M22, 1e-4f, "a wider window keeps the height and shows more width" );
		Assert.IsTrue( wide.M11 < projection.M11 );
	}

	[TestMethod]
	public void TheRideAndWalkViewsShareTheProjection()
	{
		Assert.AreEqual( OriginalProjection.FieldOfView, RideCameraMode.ViewFieldOfView );
		Assert.IsTrue( new CameraMode { UsesOriginalProjection = true }.UsesOriginalProjection );
	}
}
