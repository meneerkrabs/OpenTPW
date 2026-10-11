using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class CameraControlTests
{
	private static CameraControlInput Frame( float x = 500, float y = 300 ) => new()
	{
		HasFocus = true, LogicalSize = new Point2( 1000, 600 ), MousePosition = new Vector2( x, y ), DeltaSeconds = 0.1f
	};
	private static ParkCameraMode Camera() { ParkCameraMode.TargetExtent = 32; ParkCameraMode.InitialTarget = Vector3.Zero; return new(); }
	private static GameOptions Options( ScrollMode scroll = ScrollMode.Pushscroll ) => new() { Scroll = scroll };

	[TestMethod]
	public void EdgesMoveAndCornersHaveTheSameSpeed()
	{
		var edge = Camera(); edge.Update( Frame( 0, 300 ), Options() );
		Assert.AreEqual( 2.4f, edge.Target.Length, 0.0001f );
		var corner = Camera(); corner.Update( Frame( 0, 0 ), Options() );
		Assert.AreEqual( edge.Target.Length, corner.Target.Length, 0.0001f );
		Assert.AreNotEqual( edge.Target, corner.Target );
		var center = Camera(); center.Update( Frame(), Options() ); Assert.AreEqual( Vector3.Zero, center.Target );
		var opposite = Camera(); opposite.Update( Frame( 999, 300 ), Options() );
		Assert.AreEqual( 0, (edge.Target + opposite.Target).Length, 0.0001f );
	}

	[TestMethod]
	public void LogicalCoordinatesIgnoreDrawableDensityAndScaleWithResolution()
	{
		var originalPixels = Screen.PixelSize;
		try
		{
			Screen.PixelSize = new Point2( 1000, 600 ); var one = Camera(); one.Update( Frame( 10, 300 ), Options() );
			Screen.PixelSize = new Point2( 4000, 2400 ); var four = Camera(); four.Update( Frame( 10, 300 ), Options() );
			Assert.AreEqual( one.Target, four.Target );
			var larger = Camera(); larger.Update( Frame( 20, 600 ) with { LogicalSize = new Point2( 2000, 1200 ) }, Options() );
			Assert.AreEqual( one.Target, larger.Target );
		}
		finally { Screen.PixelSize = originalPixels; }
	}

	[TestMethod]
	public void GuardsSuppressInputAndFocusLossRearmsDrag()
	{
		var active = Frame( 0, 0 ) with { Forward = 1, Wheel = 4, RotateRightPressed = true, RightButton = true };
		foreach ( var blocked in new[] { active with { HasFocus = false }, active with { Modal = true }, active with { MousePosition = new Vector2( -1, 0 ) }, active with { MousePosition = new Vector2( 1000, 0 ) }, active with { TextEntry = true }, active with { CaptureMouse = true, CaptureKeyboard = true } } )
		{
			var camera = Camera(); camera.Update( blocked, Options() );
			Assert.AreEqual( Vector3.Zero, camera.Target ); Assert.AreEqual( 42f, camera.Height ); Assert.AreEqual( -135f, camera.Yaw );
		}
		var drag = Camera(); var right = Frame() with { RightButton = true, MouseDelta = new Vector2( 100, 0 ) };
		drag.Update( right, Options( ScrollMode.RightButton ) );
		drag.Update( right with { HasFocus = false }, Options( ScrollMode.RightButton ) );
		drag.Update( right, Options( ScrollMode.RightButton ) ); Assert.AreEqual( Vector3.Zero, drag.Target );
		drag.Update( right, Options( ScrollMode.RightButton ) ); Assert.IsTrue( drag.Target.Length > 0 );
	}

	[TestMethod]
	public void ScrollModesAndNormalizedDragAreIndependent()
	{
		var right = Camera(); right.Update( Frame( 0, 0 ), Options( ScrollMode.RightButton ) ); Assert.AreEqual( Vector3.Zero, right.Target );
		var push = Camera(); var frame = Frame() with { RightButton = true, MouseDelta = new Vector2( 100, 60 ) };
		push.Update( frame, Options() ); push.Update( frame, Options() ); Assert.AreEqual( Vector3.Zero, push.Target );
		right.Update( frame, Options( ScrollMode.RightButton ) ); right.Update( frame, Options( ScrollMode.RightButton ) );
		var large = Camera(); var largeFrame = frame with { LogicalSize = new Point2( 2000, 1200 ), MousePosition = new Vector2( 1000, 600 ), MouseDelta = new Vector2( 200, 120 ) };
		large.Update( largeFrame, Options( ScrollMode.RightButton ) ); large.Update( largeFrame, Options( ScrollMode.RightButton ) );
		Assert.AreEqual( right.Target, large.Target );
	}

	[TestMethod]
	public void RotationZoomHomeFocusAndDeltaAreBounded()
	{
		var camera = Camera(); camera.Update( Frame() with { RotateRight = true, RotateRightPressed = true }, Options() ); Assert.AreEqual( -45f, camera.Yaw );
		camera.Update( Frame() with { RotateRight = true }, Options() ); Assert.AreEqual( -45f, camera.Yaw );
		camera.Update( Frame() with { RotateRight = true, DeltaSeconds = 10 }, new GameOptions { Rotation = RotationMode.Smooth } ); Assert.AreEqual( -36f, camera.Yaw );
		camera.Update( Frame() with { Zoom = 1 }, Options() ); Assert.AreEqual( 39.6f, camera.Height, 0.0001f );
		camera.Focus( new Vector3( 999, -999, 7 ) ); Assert.AreEqual( new Vector3( 32, -32, 0 ), camera.Target );
		camera.Update( Frame() with { Home = true }, Options() ); Assert.AreEqual( Vector3.Zero, camera.Target ); Assert.AreEqual( 42f, camera.Height );
		Assert.AreEqual( ParkCameraView.Close, camera.CycleView() ); Assert.AreEqual( 20f, camera.Height );
		Assert.AreEqual( ParkCameraView.Overview, camera.CycleView() ); Assert.AreEqual( 100f, camera.Height );
		Assert.AreEqual( ParkCameraView.Standard, camera.CycleView() );
		camera.Update( Frame() with { Forward = 1, DeltaSeconds = float.NaN }, Options() ); Assert.AreEqual( Vector3.Zero, camera.Target );
	}
}
