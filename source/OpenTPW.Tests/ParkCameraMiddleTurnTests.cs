using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>UI-102: the park camera's middle-button turn (tp.exe 0x42a760, message 0x1e: yaw += X / width - 0.5 radians a drawn frame).</summary>
[TestClass]
public class ParkCameraMiddleTurnTests
{
	private static CameraControlInput Frame( float x, bool middle = true ) => new()
	{
		HasFocus = true, LogicalSize = new Point2( 1000, 600 ), MousePosition = new Vector2( x, 300 ), DeltaSeconds = ReferenceFrameClock.FrameSeconds, MiddleButton = middle
	};

	private static ParkCameraMode Camera() { ParkCameraMode.TargetExtent = 32; ParkCameraMode.InitialTarget = Vector3.Zero; return new(); }
	private static GameOptions Options() => new() { Scroll = ScrollMode.RightButton };

	[TestMethod]
	public void TheTurnPerFrameIsThePointersDistanceFromTheCentreInRadians()
	{
		Assert.AreEqual( 0.5f, ParkCameraMode.MiddleTurnRadians( 1000, 1000 ), 1e-6f, "the right edge: half a radian" );
		Assert.AreEqual( -0.5f, ParkCameraMode.MiddleTurnRadians( 0, 1000 ), 1e-6f );
		Assert.AreEqual( 0f, ParkCameraMode.MiddleTurnRadians( 500, 1000 ), 1e-6f );
		Assert.AreEqual( 0.25f, ParkCameraMode.MiddleTurnRadians( 750.9f, 1000 ), 1e-6f, "the pointer is a whole pixel" );
	}

	[TestMethod]
	public void HoldingTheMiddleButtonTurnsOneStepPerReferenceFrameLikeTheLeftArrowForAPointerRightOfCentre()
	{
		var camera = Camera();
		camera.Update( Frame( 700 ), Options() );
		var expected = -0.2f * 180f / MathF.PI;
		Assert.AreEqual( -90f + expected, camera.Yaw, 1e-3f, "right of the centre lowers OpenTPW's yaw, as the Left arrow (key 6) raises the original's" );
		camera.Update( Frame( 700 ), Options() );
		Assert.AreEqual( -90f + 2 * expected, camera.Yaw, 1e-3f );
		camera.Update( Frame( 300 ), Options() );
		Assert.AreEqual( -90f + expected, camera.Yaw, 1e-3f, "left of the centre turns back" );
	}

	[TestMethod]
	public void ReleasingStopsTheTurnAndAPressOverTheUiNeverStartsIt()
	{
		var camera = Camera();
		camera.Update( Frame( 800 ), Options() );
		var after = camera.Yaw;
		camera.Update( Frame( 800, middle: false ), Options() );
		camera.Update( Frame( 800, middle: false ), Options() );
		Assert.AreEqual( after, camera.Yaw, "no turn without the button" );

		var blocked = Camera();
		blocked.Update( Frame( 800 ) with { CaptureMouse = true }, Options() );
		blocked.Update( Frame( 800 ), Options() );
		Assert.AreEqual( -90f, blocked.Yaw, "a press the UI took is not a camera press, and holding on does not start it" );
	}

	[TestMethod]
	public void TheTurnFollowsTheReferenceRateNotTheDisplayRate()
	{
		var slow = Camera();
		slow.Update( Frame( 700 ) with { DeltaSeconds = 4 * ReferenceFrameClock.FrameSeconds }, Options() );
		var fast = Camera();
		for ( var frame = 0; frame < 4; frame++ )
			fast.Update( Frame( 700 ), Options() );
		Assert.AreEqual( fast.Yaw, slow.Yaw, 1e-3f );
	}
}
