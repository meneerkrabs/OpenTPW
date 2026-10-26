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
	// the tests of this file are about the original's range, 70..130 (the wide zoom range has its own tests: WideZoomTests)
	// the drag arithmetic below is the option-off path (RMBCancelOn clear: the right press is the camera's at once); the held-back press of the option-on path has its own test
	private static GameOptions Options( ScrollMode scroll = ScrollMode.Pushscroll ) => new() { Scroll = scroll, WideZoom = false, RmbCancel = false };

	/// <summary>
	/// [BIN:TPW-EXE:0x4881AE held-back right press] with RMBCancelOn the right press is not the camera's drag until it has lasted 200 ms or strayed 8 px on an axis (0x48823F, 0x488265, 0x48827F);
	/// a quick click never moves the camera, and the drag starts from where it was armed.
	/// </summary>
	[TestMethod]
	public void WithTheCancelOptionTheRightPressDragsTheCameraOnlyAfter200msOr8px()
	{
		var options = new GameOptions { Scroll = ScrollMode.RightButton, WideZoom = false, RmbCancel = true };
		var moving = Frame() with { RightButton = true, MouseDelta = new Vector2( 100, 0 ), DeltaSeconds = 0.07f };
		var quick = Camera();
		for ( var frame = 0; frame < 3; frame++ ) quick.Update( moving, options );
		Assert.AreEqual( Vector3.Zero, quick.Target, "140 ms of a press is still a click in the making" );
		quick.Update( moving, options );
		Assert.AreEqual( Vector3.Zero, quick.Target, "the frame that arms the drag moves nothing, the next one does" );
		quick.Update( moving, options );
		Assert.IsTrue( quick.Target.Length > 0, "after 200 ms the press is the drag" );
		var strayed = Camera();
		strayed.Update( moving with { DeltaSeconds = 0.016f }, options );
		strayed.Update( moving with { DeltaSeconds = 0.016f, MousePosition = new Vector2( 508, 300 ) }, options );
		Assert.AreEqual( Vector3.Zero, strayed.Target, "8 px is not beyond the slop" );
		strayed.Update( moving with { DeltaSeconds = 0.016f, MousePosition = new Vector2( 509, 300 ) }, options );
		Assert.AreEqual( Vector3.Zero, strayed.Target, "the frame that strays 9 px arms the drag without moving" );
		strayed.Update( moving with { DeltaSeconds = 0.016f, MousePosition = new Vector2( 510, 300 ) }, options );
		Assert.IsTrue( strayed.Target.Length > 0 );
		var released = Camera();
		released.Update( moving, options ); released.Update( moving, options );
		released.Update( moving with { RightButton = false }, options );
		released.Update( moving, options ); released.Update( moving, options );
		Assert.AreEqual( Vector3.Zero, released.Target, "a release starts the wait over" );
		var off = Camera();
		off.Update( moving, Options( ScrollMode.RightButton ) ); off.Update( moving, Options( ScrollMode.RightButton ) );
		Assert.IsTrue( off.Target.Length > 0, "with the option off the drag starts at once" );
	}

	[TestMethod]
	public void EdgesMoveAndCornersHaveTheSameSpeed()
	{
		var edge = Camera(); edge.Update( Frame( 0, 300 ), Options() );
		Assert.AreEqual( ParkCameraMode.PanSpeed( ParkCameraMode.HomeHeight ) * 0.1f, edge.Target.Length, 0.0001f, "the original's settled pan speed, 0.02 x sqrt(zoom) per millisecond" );
		var corner = Camera(); corner.Update( Frame( 0, 0 ), Options() );
		Assert.AreEqual( edge.Target.Length, corner.Target.Length, 0.0001f );
		Assert.AreNotEqual( edge.Target, corner.Target );
		var center = Camera(); center.Update( Frame(), Options() ); Assert.AreEqual( Vector3.Zero, center.Target );
		var opposite = Camera(); opposite.Update( Frame( 999, 300 ), Options() );
		Assert.AreEqual( 0, (edge.Target + opposite.Target).Length, 0.0001f );
	}

	/// <summary>
	/// No pointer over the window, (-1, -1) as the browser reports it (EXT:browser-no-pointer), never pushes the view, not even over a UI that leaves the
	/// edge band to the camera; a pointer in the band (the outer 1.5 % of each side), the top-left pixel included, still does.
	/// </summary>
	[TestMethod]
	public void NoPointerNeverPushesButAPointerInTheEdgeBandDoes()
	{
		var none = Camera();
		for ( var frame = 0; frame < 30; frame++ )
		{
			none.Update( Frame( -1, -1 ), Options() );
			none.Update( Frame( -1, -1 ) with { CaptureMouse = true, EdgeScrollOverUi = true }, Options() );
		}
		Assert.AreEqual( Vector3.Zero, none.Target );
		foreach ( var (x, y) in new[] { (0f, 0f), (0f, 300f), (14f, 300f), (500f, 0f), (500f, 8f), (986f, 300f), (500f, 592f), (999f, 599f) } )
		{
			var pushed = Camera(); pushed.Update( Frame( x, y ), Options() );
			Assert.AreEqual( ParkCameraMode.PanSpeed( ParkCameraMode.HomeHeight ) * 0.1f, pushed.Target.Length, 0.0001f, $"({x}, {y}) lies in the edge band" );
		}
		foreach ( var (x, y) in new[] { (16f, 300f), (500f, 10f), (984f, 300f), (500f, 590f) } )
		{
			var still = Camera(); still.Update( Frame( x, y ), Options() );
			Assert.AreEqual( Vector3.Zero, still.Target, $"({x}, {y}) lies inside the band" );
		}
	}

	[TestMethod]
	public void AUiThatLeavesTheEdgeBandToTheCameraStillScrollsAtTheEdge()
	{
		// a UI under the pointer at the left edge keeps the camera still, unless it leaves the edge band to the camera (a visit's chat overlay)
		var covered = Camera(); covered.Update( Frame( 0, 300 ) with { CaptureMouse = true }, Options() );
		Assert.AreEqual( Vector3.Zero, covered.Target );
		var park = Camera(); park.Update( Frame( 0, 300 ), Options() );
		var overlay = Camera(); overlay.Update( Frame( 0, 300 ) with { CaptureMouse = true, EdgeScrollOverUi = true, Wheel = 1 }, Options() );
		Assert.AreEqual( park.Target, overlay.Target, "the edge scrolls as over the park" );
		Assert.AreEqual( ParkCameraMode.HomeHeight, overlay.Height, "the wheel stays the UI's" );
		// the right-button mode has no edge band, and a field taking the pointer with the keys keeps the camera still
		var drag = Camera(); drag.Update( Frame( 0, 300 ) with { CaptureMouse = true, EdgeScrollOverUi = true }, Options( ScrollMode.RightButton ) );
		Assert.AreEqual( Vector3.Zero, drag.Target );
		var typing = Camera(); typing.Update( Frame( 0, 300 ) with { CaptureMouse = true, EdgeScrollOverUi = true, TextEntry = true }, Options() );
		Assert.AreEqual( Vector3.Zero, typing.Target );
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
			Assert.AreEqual( Vector3.Zero, camera.Target ); Assert.AreEqual( 110f, camera.Height ); Assert.AreEqual( -90f, camera.Yaw );
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

	/// <summary>[BIN:TPW-EXE:0x42aab0 locate jump] every locate and map jump of the original moves only the target (the zoom and yaw arguments are null), so no focus changes the zoom or the yaw and the original has no view-cycle button.</summary>
	[TestMethod]
	public void FocusMovesOnlyTheTargetAndLeavesZoomAndYaw()
	{
		var camera = Camera(); camera.SetPose( Vector3.Zero, 90, 33 );
		var height = camera.Height; var yaw = camera.Yaw;
		camera.Focus( new Vector3( 10, -20, 5 ) );
		Assert.AreEqual( new Vector3( 10, -20, 0 ), camera.Target ); Assert.AreEqual( height, camera.Height ); Assert.AreEqual( yaw, camera.Yaw );
	}

	[TestMethod]
	public void RotationZoomHomeFocusAndDeltaAreBounded()
	{
		var camera = Camera(); camera.Update( Frame() with { RotateRight = true, RotateRightPressed = true }, Options() ); Assert.AreEqual( 0f, camera.Yaw );
		camera.Update( Frame() with { RotateRight = true }, Options() ); Assert.AreEqual( 0f, camera.Yaw );
		var smooth = ParkCameraMode.RotationSpeed * 0.1f;
		camera.Update( Frame() with { RotateRight = true, DeltaSeconds = 10 }, new GameOptions { Rotation = RotationMode.Smooth } ); Assert.AreEqual( smooth, camera.Yaw, 1e-3f, "0.002 radians per millisecond, the frame capped at 0.1 s" );
		camera.Update( Frame() with { Zoom = 1 }, Options() ); Assert.AreEqual( 105f, camera.Height, 0.0001f, "0.05 zoom units per millisecond" );
		camera.Update( Frame() with { Wheel = 2 }, Options() ); Assert.AreEqual( 85f, camera.Height, 0.0001f, "ten per wheel notch" );
		camera.Update( Frame() with { Wheel = 20 }, Options() ); Assert.AreEqual( 70f, camera.Height, "the zoom stops at 70" );
		camera.Update( Frame() with { Wheel = -20 }, Options() ); Assert.AreEqual( 130f, camera.Height, "and at 130" );
		camera.Focus( new Vector3( 999, -999, 7 ) ); Assert.AreEqual( new Vector3( 32, -32, 0 ), camera.Target );
		camera.Update( Frame() with { Home = true }, Options() ); Assert.AreEqual( Vector3.Zero, camera.Target ); Assert.AreEqual( 110f, camera.Height ); Assert.AreEqual( -90f, camera.Yaw );
		camera.Update( Frame() with { Forward = 1, DeltaSeconds = float.NaN }, Options() ); Assert.AreEqual( Vector3.Zero, camera.Target );
	}
}
