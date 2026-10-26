using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:wide-zoom] The park camera's wide zoom range: tp.exe's own 40..300 (the flag 0x200 clamp 0x42c631) behind <see cref="GameOptions.WideZoom"/>, on by default, against the normal 70..130.
/// The pitch and the eye (0x42b1c0) are the original's formulas over the wider range; inside 70..130 nothing changes.
/// </summary>
[TestClass]
public class WideZoomTests
{
	// the wide range's far end follows FIRSTPERSONVIEWDISTANCE and the display (WideMaximumFor); the tests of the camera's steps use the whole 300
	[TestInitialize]
	public void TheWholeWideRange() => ParkCameraMode.WideMaximumOverride = 300f;

	[TestCleanup]
	public void RestoreTheOption()
	{
		RenderFog.OptionOverride = null;
		ParkCameraMode.WideMaximumOverride = null;
	}

	private static CameraControlInput Frame( float wheel = 0, float zoom = 0 ) => new()
	{
		HasFocus = true, LogicalSize = new Point2( 1000, 600 ), MousePosition = new Vector2( 500, 300 ), DeltaSeconds = 0.1f, Wheel = wheel, Zoom = zoom
	};

	private static ParkCameraMode Camera( bool wide = true )
	{
		ParkCameraMode.TargetExtent = 32;
		ParkCameraMode.InitialTarget = Vector3.Zero;
		var camera = new ParkCameraMode();
		camera.Update( Frame(), new GameOptions { WideZoom = wide } );
		return camera;
	}

	[TestMethod]
	public void TheOptionIsOnByDefaultAndTheWideRangeIsTheOriginalsOwn()
	{
		Assert.IsTrue( new GameOptions().WideZoom );
		Assert.AreEqual( (40f, 300f), (ParkCameraMode.WideMinimumHeight, ParkCameraMode.WideMaximumHeight), "the constants 0x74c9d8 and 0x74c9dc" );
		Assert.AreEqual( (70f, 130f), (ParkCameraMode.MinimumHeight, ParkCameraMode.MaximumHeight), "the normal range 0x6fde08 and 0x6fde0c is untouched" );
		Assert.AreEqual( 110f, ParkCameraMode.HomeHeight );
		var camera = Camera();
		Assert.AreEqual( (40f, 300f), (camera.MinimumZoom, camera.MaximumZoom) );
	}

	[TestMethod]
	public void TheWheelAndTheKeysReachTheWideEndsWithTheirOriginalSteps()
	{
		var options = new GameOptions();
		var camera = Camera();
		camera.Update( Frame( wheel: -1 ), options );
		Assert.AreEqual( 120f, camera.Height, "one wheel notch is still 10 zoom units" );
		camera.Update( Frame( wheel: 1 ), options );
		Assert.AreEqual( 110f, camera.Height );
		for ( var i = 0; i < 40; i++ )
			camera.Update( Frame( wheel: -1 ), options );
		Assert.AreEqual( 300f, camera.Height );
		for ( var i = 0; i < 60; i++ )
			camera.Update( Frame( wheel: 1 ), options );
		Assert.AreEqual( 40f, camera.Height );
		// the zoom keys keep 0.05 per millisecond: a 0.1 s frame moves 5 units
		var keys = Camera();
		keys.Update( Frame( zoom: -1 ), options );
		Assert.AreEqual( 115f, keys.Height, 1e-3f );
		for ( var i = 0; i < 100; i++ )
			keys.Update( Frame( zoom: -1 ), options );
		Assert.AreEqual( 300f, keys.Height );
		for ( var i = 0; i < 200; i++ )
			keys.Update( Frame( zoom: 1 ), options );
		Assert.AreEqual( 40f, keys.Height );
	}

	[TestMethod]
	public void WithTheOptionOffTheRangeStaysSeventyToOneHundredThirty()
	{
		var options = new GameOptions { WideZoom = false };
		var camera = Camera( wide: false );
		Assert.AreEqual( (70f, 130f), (camera.MinimumZoom, camera.MaximumZoom) );
		for ( var i = 0; i < 40; i++ )
			camera.Update( Frame( wheel: -1 ), options );
		Assert.AreEqual( 130f, camera.Height );
		for ( var i = 0; i < 60; i++ )
			camera.Update( Frame( wheel: 1 ), options );
		Assert.AreEqual( 70f, camera.Height );
		for ( var i = 0; i < 100; i++ )
			camera.Update( Frame( zoom: -1 ), options );
		Assert.AreEqual( 130f, camera.Height );
	}

	[TestMethod]
	public void SwitchingTheOptionOffBringsAZoomedOutCameraBackInside()
	{
		var camera = Camera();
		camera.SetPose( Vector3.Zero, 300, -90 );
		Assert.AreEqual( 300f, camera.Height );
		var far = camera.Position;
		camera.Update( Frame(), new GameOptions { WideZoom = false } );
		Assert.AreEqual( 130f, camera.Height );
		Assert.IsTrue( camera.Position.Z < far.Z, "the eye follows the zoom" );
		camera.SetPose( Vector3.Zero, 20, -90 );
		Assert.AreEqual( 70f, camera.Height, "SetPose clamps to the range of the option" );
		var wide = Camera();
		wide.SetPose( Vector3.Zero, 20, -90 );
		Assert.AreEqual( 40f, wide.Height );
		wide.SetPose( Vector3.Zero, 500, -90 );
		Assert.AreEqual( 300f, wide.Height );
	}

	[TestMethod]
	public void HomeStillGoesToOneHundredTen()
	{
		var camera = Camera();
		camera.SetPose( Vector3.Zero, 300, 33 );
		camera.Update( Frame() with { Home = true }, new GameOptions() );
		Assert.AreEqual( (110f, -90f), (camera.Height, camera.Yaw) );
	}

	/// <summary>[BIN:TPW-EXE:0x42c742 pitch] the same line runs outside 70..130 until the clamp to 0..1.4707963.</summary>
	[TestMethod]
	public void ThePitchKeepsExtrapolatingThenStopsAtTheOriginalsClamp()
	{
		Assert.AreEqual( 35f, ParkCameraMode.PitchOf( 40 ).RadiansToDegrees(), 1e-3f, "(1 + 0.5) x 45 - 0.5 x 65 degrees" );
		Assert.AreEqual( 45f, ParkCameraMode.PitchOf( 70 ).RadiansToDegrees(), 1e-3f );
		Assert.AreEqual( 65f, ParkCameraMode.PitchOf( 130 ).RadiansToDegrees(), 1e-3f );
		// beyond 130 the line continues: 45 + (b - a) x t, t = (zoom - 70) / 60
		var slope = (1.134464f - 0.7853982f) / 60f;
		Assert.AreEqual( 0.7853982f + slope * (150 - 70), ParkCameraMode.PitchOf( 150 ), 1e-4f );
		Assert.AreEqual( 0.7853982f + slope * (180 - 70), ParkCameraMode.PitchOf( 180 ), 1e-4f );
		// the clamp 0x42c7c0 (1.4707963 = 84.27 degrees) is reached near a zoom value of 188 and kept up to 300
		Assert.AreEqual( 1.4707963f, ParkCameraMode.PitchOf( 200 ), 1e-6f );
		Assert.AreEqual( 1.4707963f, ParkCameraMode.PitchOf( 300 ), 1e-6f );
		Assert.IsTrue( ParkCameraMode.PitchOf( 187 ) < 1.4707963f && ParkCameraMode.PitchOf( 189 ) == 1.4707963f );
		for ( var zoom = 40f; zoom <= 300; zoom += 5 )
			Assert.IsTrue( ParkCameraMode.PitchOf( zoom ) >= ParkCameraMode.PitchOf( zoom - 5 ) || zoom == 40, "never decreasing" );
	}

	[TestMethod]
	public void TheEyeDistanceAndHeightFollowThePitchOutsideTheNormalRange()
	{
		foreach ( var zoom in new[] { 40f, 70f, 110f, 130f, 200f, 300f } )
		{
			var pitch = ParkCameraMode.PitchOf( zoom );
			var eye = ParkCameraMode.EyeOffset( zoom, ParkCameraMode.HomeYaw, 0 );
			Assert.AreEqual( zoom * MathF.Cos( pitch ) * 0.2f, ParkCameraMode.HorizontalDistanceOf( zoom ) * 0.2f, 1e-4f, $"ground distance at {zoom}" );
			Assert.AreEqual( zoom * MathF.Sin( pitch ) * 0.2f, eye.Z, 1e-3f, $"height at {zoom}" );
			Assert.AreEqual( -ParkCameraMode.HorizontalDistanceOf( zoom ) * 0.2f, eye.Y, 1e-3f, $"behind the target at {zoom}" );
		}
		// at 300 the camera looks almost straight down from 298.5 units (59.7 engine units): 30 units behind the target
		Assert.AreEqual( 29.96f, ParkCameraMode.HorizontalDistanceOf( 300 ), 0.05f );
		Assert.AreEqual( 59.7f, ParkCameraMode.EyeOffset( 300, ParkCameraMode.HomeYaw, 0 ).Z, 0.05f );
		// at 40 it is a low 35 degree view from 22.9 units up
		Assert.AreEqual( 4.58f, ParkCameraMode.EyeOffset( 40, ParkCameraMode.HomeYaw, 0 ).Z, 0.01f );
		// the camera object agrees with the formula
		var camera = Camera();
		camera.SetPose( Vector3.Zero, 300, ParkCameraMode.HomeYaw );
		Assert.AreEqual( ParkCameraMode.EyeOffset( 300, ParkCameraMode.HomeYaw, 0 ).Z, camera.Position.Z, 1e-3f );
		Assert.AreEqual( ParkCameraMode.HorizontalDistanceOf( 300 ) * 0.2f, camera.HorizontalDistance, 1e-3f );
	}

	[TestMethod]
	public void InsideSeventyToOneHundredThirtyNothingChanged()
	{
		var pins = new (float Zoom, float Pitch, float Distance, float Height)[]
		{
			(70, 0.7853982f, 70 * 0.7071068f, 70 * 0.7071068f), (110, 1.018130f, 110 * MathF.Cos( 1.018130f ), 110 * MathF.Sin( 1.018130f )), (130, 1.134464f, 130 * MathF.Cos( 1.134464f ), 130 * MathF.Sin( 1.134464f ))
		};
		foreach ( var (zoom, pitch, distance, height) in pins )
		{
			Assert.AreEqual( pitch, ParkCameraMode.PitchOf( zoom ), 1e-4f );
			Assert.AreEqual( distance, ParkCameraMode.HorizontalDistanceOf( zoom ), 1e-2f );
			Assert.AreEqual( height * 0.2f, ParkCameraMode.EyeOffset( zoom, ParkCameraMode.HomeYaw, 0 ).Z, 1e-2f );
		}
		// a camera of either option places the eye identically inside the normal range
		foreach ( var zoom in new[] { 70f, 90f, 110f, 130f } )
		{
			var on = Camera(); on.SetPose( Vector3.Zero, zoom, 20 );
			var off = Camera( wide: false ); off.SetPose( Vector3.Zero, zoom, 20 );
			Assert.AreEqual( on.Position, off.Position );
			Assert.AreEqual( on.Rotation, off.Rotation );
		}
	}

	[TestMethod]
	public void ThePanSpeedKeepsTheOriginalsSquareRootRuleAtEveryZoom()
	{
		Assert.AreEqual( 0.02f * MathF.Sqrt( 300 ) * 1000 * 0.2f, ParkCameraMode.PanSpeed( 300 ), 1e-3f );
		Assert.AreEqual( 0.02f * MathF.Sqrt( 40 ) * 1000 * 0.2f, ParkCameraMode.PanSpeed( 40 ), 1e-3f );
		Assert.AreEqual( 0.02f * MathF.Sqrt( 110 ) * 1000 * 0.2f, ParkCameraMode.PanSpeed( 110 ), 1e-3f );
		var camera = Camera();
		camera.SetPose( Vector3.Zero, 300, ParkCameraMode.HomeYaw );
		camera.Update( Frame() with { MousePosition = new Vector2( 0, 300 ) }, new GameOptions() );
		Assert.AreEqual( ParkCameraMode.PanSpeed( 300 ) * 0.1f, camera.Target.Length, 1e-3f );
	}

	/// <summary>The optical axis of the camera meets the ground at the target at every zoom, so the cursor pick (0x45bf90) works over the whole range, also in the almost vertical view at 300.</summary>
	[TestMethod]
	public void TheTerrainPickWorksAtEveryZoomOfTheWideRange()
	{
		var heights = new float[11 * 11];
		var field = new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), heights, new uint[100] );
		var target = OriginalParkPlacement.ToEngine( field, new NVector3( 50, 0, 50 ) );
		foreach ( var zoom in new[] { 40f, 70f, 110f, 130f, 200f, 300f } )
			foreach ( var yaw in new[] { -90f, 20f, 135f } )
			{
				var eye = target + ParkCameraMode.EyeOffset( zoom, yaw, 0 ).GetSystemVector3();
				var direction = ParkCameraMode.ViewDirection( zoom, yaw ).GetSystemVector3();
				Assert.IsTrue( TerrainRayPick.TryPick( field, eye, direction, out var hit ), $"zoom {zoom}, yaw {yaw}" );
				Assert.AreEqual( target.X, hit.X, 1e-2f, $"x at zoom {zoom}, yaw {yaw}" );
				Assert.AreEqual( target.Y, hit.Y, 1e-2f, $"y at zoom {zoom}, yaw {yaw}" );
				Assert.AreEqual( 0f, hit.Z, 1e-3f );
			}
	}

	[TestMethod]
	public void TheOptionIsSavedLoadedAndCopied()
	{
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-wide-zoom-{Guid.NewGuid():N}.json" );
		try
		{
			new GameOptions { WideZoom = false }.Save( path );
			Assert.IsFalse( GameOptions.Load( path ).WideZoom );
			new GameOptions().Save( path );
			Assert.IsTrue( GameOptions.Load( path ).WideZoom );
			File.WriteAllText( path, "{ \"MusicVolume\": 5 }" );
			Assert.IsTrue( GameOptions.Load( path ).WideZoom, "a file from before the option existed gets the default, on" );
			var copy = new GameOptions();
			copy.CopyFrom( new GameOptions { WideZoom = false } );
			Assert.IsFalse( copy.WideZoom );
			Assert.IsFalse( new GameOptions { WideZoom = false }.Clone().WideZoom );
		}
		finally { File.Delete( path ); }
	}

	[DataTestMethod]
	[DataRow( 0, 16f / 9, 130f )]
	[DataRow( 1, 16f / 9, 282.75f )]
	[DataRow( 2, 16f / 9, 183.75f )]
	[DataRow( 3, 16f / 9, 183.75f )]
	[DataRow( 0, 4f / 3, 130f )]
	[DataRow( 1, 4f / 3, 282.75f )]
	[DataRow( 2, 4f / 3, 229f )]
	[DataRow( 3, 4f / 3, 213.75f )]
	public void TheWideRangeEndsWhereTheSpanListNoLongerReachesTheTopOfTheScreen( int setting, float aspect, float expected )
	{
		// [EXT:wide-zoom] the largest zoom whose top-centre ground point is still inside the span hull (ray distance 200, 325, 450, 700) and whose hull holds at most 2500 cells
		var ray = RenderFog.ViewDistanceOf( setting ).Distance;
		Assert.AreEqual( expected, ParkCameraMode.WideMaximumFor( ray, aspect ), 0.26f, $"setting {setting}, aspect {aspect}" );
		Assert.IsTrue( ParkCameraMode.ListCoversTheScreenTop( 130, ray, aspect, out _ ) == (setting > 0) || expected == 130f );
	}

	[TestMethod]
	public void ThePastTheEndCameraSeesNoGroundAtTheTop()
	{
		// setting 0 (ray 200, half 100): already at 130 the hull stops short of the top edge's ground point
		Assert.IsFalse( ParkCameraMode.ListCoversTheScreenTop( 130, 200, 16f / 9, out _ ) );
		// setting 1 reaches at 200 and no longer at 300
		Assert.IsTrue( ParkCameraMode.ListCoversTheScreenTop( 200, 325, 16f / 9, out _ ) );
		Assert.IsFalse( ParkCameraMode.ListCoversTheScreenTop( 300, 325, 16f / 9, out _ ) );
		// setting 3 at 300 lists far more than the 2500 cells of a frame
		ParkCameraMode.ListCoversTheScreenTop( 300, 700, 16f / 9, out var cells );
		Assert.IsTrue( cells > 2500 );
	}

	[TestMethod]
	public void TheCameraTakesTheEndFromTheViewDistanceSetting()
	{
		ParkCameraMode.WideMaximumOverride = null;
		RenderFog.OptionOverride = 1;
		var camera = Camera();
		var aspect = Screen.Aspect > 0 && float.IsFinite( Screen.Aspect ) ? Screen.Aspect : 16f / 9;
		Assert.AreEqual( ParkCameraMode.WideMaximumFor( 325, aspect ), camera.MaximumZoom );
		camera.SetPose( Vector3.Zero, 500, -90 );
		Assert.AreEqual( camera.MaximumZoom, camera.Height );
	}

	[TestMethod]
	public void AChangedViewDistanceClampsTheZoomAtOnce()
	{
		ParkCameraMode.WideMaximumOverride = null;
		RenderFog.OptionOverride = 1;
		var camera = Camera();
		camera.SetPose( Vector3.Zero, 300, -90 );
		var wide = camera.Height;
		Assert.IsTrue( wide > 200f, $"{wide}" );
		RenderFog.OptionOverride = 0;
		camera.Update( Frame(), new GameOptions() );
		Assert.AreEqual( 130f, camera.Height );
		RenderFog.OptionOverride = 1;
		camera.Update( Frame(), new GameOptions() );
		Assert.AreEqual( 130f, camera.Height, "a longer view distance does not move the camera back out" );
	}

	[TestMethod]
	public void TheOriginalRangeSeventyToOneHundredThirtyIsAlwaysThere()
	{
		ParkCameraMode.WideMaximumOverride = null;
		foreach ( var setting in new[] { 0, 1, 2, 3 } )
		{
			RenderFog.OptionOverride = setting;
			var camera = Camera( wide: false );
			Assert.AreEqual( (70f, 130f), (camera.MinimumZoom, camera.MaximumZoom), $"setting {setting}, wide zoom off" );
			var wide = Camera();
			Assert.IsTrue( wide.MaximumZoom >= 130f, $"setting {setting}" );
			wide.SetPose( Vector3.Zero, 130, -90 );
			Assert.AreEqual( 130f, wide.Height );
		}
	}
}
