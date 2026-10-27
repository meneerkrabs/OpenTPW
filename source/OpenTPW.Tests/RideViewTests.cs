using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The ride view's asset-free math: the smoothing weight, the free look, the seat axes and the right-click guard (UI-093/094/095).</summary>
[TestClass]
public class RideViewTests
{
	[TestMethod]
	public void BlendWeightIsAboutHalfAtThirtyFramesAndClamped()
	{
		Assert.AreEqual( 0.4835f, RideCameraMode.BlendFactor( 33f ), 0.0005f );
		Assert.AreEqual( 0.4995f, RideCameraMode.BlendFactor( 0f ), 0.0001f, "the frame time is at least 1 ms" );
		Assert.AreEqual( 0f, RideCameraMode.BlendFactor( 5000f ), 0.0001f, "and at most 1000 ms" );
		Assert.AreEqual( 0.4995f, RideCameraMode.BlendFactor( float.NaN ), 0.0001f );
	}

	[TestMethod]
	public void LookHasADeadZoneAndTurnsPastFortyFiveDegrees()
	{
		Assert.AreEqual( (0f, 0f), RideCameraMode.LookAngles( 0.1f, 0f ) );
		Assert.AreEqual( 0f, RideCameraMode.LookAngles( 0f, -0.3f ).Pitch, "no pitch between -0.4 and 0.2" );
		var (yaw, _) = RideCameraMode.LookAngles( 0.5f, 0 );
		Assert.AreEqual( 1.05f, yaw, 0.001f );
		Assert.IsTrue( yaw > MathF.PI / 4 );
		Assert.AreEqual( -1.05f, RideCameraMode.LookAngles( -0.5f, 0 ).Yaw, 0.001f );
		Assert.AreEqual( 0.6f, RideCameraMode.LookAngles( 0, -1 ).Pitch, 0.001f, "looking down below -0.4" );
		Assert.AreEqual( -0.96f, RideCameraMode.LookAngles( 0, 1 ).Pitch, 0.001f, "looking up above 0.2" );
	}

	[TestMethod]
	public void PointerOffsetIsMinusOneToOneWithYUp()
	{
		Assert.AreEqual( 0f, RideCameraMode.PointerOffset( 500, 300, 1000, 600 ).Length(), 1e-6f );
		var corner = RideCameraMode.PointerOffset( 5000, -50, 1000, 600 );
		Assert.AreEqual( (1f, 1f), (corner.X, corner.Y) );
	}

	[TestMethod]
	public void FreeLookTurnsRightAndDownAndLeavesTheEyeAlone()
	{
		System.Numerics.Vector3 forward = System.Numerics.Vector3.UnitX;
		var up = System.Numerics.Vector3.UnitZ;
		var (rightForward, _) = RideCameraMode.ApplyLook( forward, up, 0.5f, 0 );
		Assert.IsTrue( rightForward.Y < -0.4f, "engine right is -Y" );
		var (downForward, _) = RideCameraMode.ApplyLook( forward, up, 0, 0.5f );
		Assert.IsTrue( downForward.Z < -0.4f );
	}

	[TestMethod]
	public void SeatAxesTakeTheNodesZAsForwardAndYAsUp()
	{
		// engine matrix of a node rotated a quarter turn about the vertical: model Z (engine Y row) points along engine +X
		var matrix = new Matrix4x4( 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 3, 4, 5, 1 ) * Matrix4x4.CreateScale( 0.2f );
		var (forward, up) = RideCameraMode.SeatAxes( matrix );
		Assert.AreEqual( 0f, (forward - System.Numerics.Vector3.UnitX).Length(), 1e-5f );
		Assert.AreEqual( 0f, (up - System.Numerics.Vector3.UnitZ).Length(), 1e-5f );
	}

	[TestMethod]
	public void TheEyeIsTheSeatsTranslationExactlyAndTheViewIsSmoothedOnce()
	{
		var pose = Matrix4x4.CreateTranslation( 1, 2, 3 );
		var camera = new RideCameraMode( null!, Vector3.Forward, () => pose );
		camera.Advance( 33, Vector2.Zero );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), camera.Position );
		// a quarter turn of the node: the first frame follows it fully, the next frame averages with the node of the frame before
		pose = Matrix4x4.CreateRotationZ( MathF.PI / 2 ) * Matrix4x4.CreateTranslation( 7, 8, 9 );
		camera.Advance( 33, Vector2.Zero );
		Assert.AreEqual( new Vector3( 7, 8, 9 ), camera.Position, "the translation is not smoothed" );
		var turned = RideCameraMode.SeatAxes( pose ).Forward;
		var halfway = camera.Rotation.Forward;
		Assert.IsTrue( System.Numerics.Vector3.Dot( halfway.GetSystemVector3(), turned ) is > 0.7f and < 0.99f, "between the previous and the current heading" );
	}
}
