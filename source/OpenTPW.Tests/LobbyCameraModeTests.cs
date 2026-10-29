using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The lobby's island-view camera follows tp.exe 0x5e0490 (state 0) and 0x5e1210: the angle advances by SPINSPEED per
/// tenth of a second (the lobby step, 0x5d5c50, at most 500 ms), the eye and the target glide 0.1 and 0.2 of the way
/// per step, and the field of view is ISLANDFOV.
/// </summary>
[TestClass, DoNotParallelize]
public class LobbyCameraModeTests
{
	private (float Radius, float Offset, float Spin, float Fov, Vector3 Target) saved;

	[TestInitialize]
	public void Save()
	{
		saved = (LobbyCameraMode.Radius, LobbyCameraMode.VerticalOffset, LobbyCameraMode.SpinSpeed, LobbyCameraMode.IslandFieldOfView, LobbyCameraMode.Target);
		LobbyCameraMode.Radius = 70;
		LobbyCameraMode.VerticalOffset = 20;
		LobbyCameraMode.SpinSpeed = 0.02f;
		LobbyCameraMode.IslandFieldOfView = 100;
		LobbyCameraMode.Target = new Vector3( 400, 400, 12.5f );
	}

	[TestCleanup]
	public void Restore() =>
		(LobbyCameraMode.Radius, LobbyCameraMode.VerticalOffset, LobbyCameraMode.SpinSpeed, LobbyCameraMode.IslandFieldOfView, LobbyCameraMode.Target) = saved;

	[TestMethod]
	public void SpinSpeedIsRadiansPerTenthOfASecond()
	{
		var camera = new LobbyCameraMode();
		camera.Advance( 1f / 30 );
		// 33 ms is a third of a lobby step
		Assert.AreEqual( 0.02f / 3, camera.Angle, 1e-5f );
		camera.Advance( 0.1f );
		Assert.AreEqual( 0.02f / 3 + 0.02f, camera.Angle, 1e-5f );
	}

	[TestMethod]
	public void ALongFrameCountsAsHalfASecond()
	{
		var camera = new LobbyCameraMode();
		camera.Advance( 2f );
		Assert.AreEqual( 0.02f * 5, camera.Angle, 1e-5f );
	}

	[TestMethod]
	public void TheAngleWrapsIntoOneTurn()
	{
		LobbyCameraMode.SpinSpeed = 1.5f;
		var camera = new LobbyCameraMode();
		camera.Advance( 0.5f );
		Assert.AreEqual( 7.5f - System.MathF.Tau, camera.Angle, 1e-4f );
	}

	[TestMethod]
	public void EyeAndTargetGlideToANewIsland()
	{
		LobbyCameraMode.SpinSpeed = 0;
		var camera = new LobbyCameraMode();
		Assert.AreEqual( new Vector3( 400, 400 + 70, 12.5f + 20 ), camera.Eye );
		LobbyCameraMode.Target = new Vector3( 600, 400, 12.5f );
		// 200 ms: two steps, the target moves 0.4 and the eye 0.2 of the way
		camera.Advance( 0.2f );
		Assert.AreEqual( 400 + 200 * 0.4f, camera.LookTarget.X, 1e-3f );
		Assert.AreEqual( 400 + 200 * 0.2f, camera.Eye.X, 1e-3f );
		Assert.AreEqual( camera.Eye, camera.Position );
	}

	[TestMethod]
	public void FieldOfViewIsIslandFov()
	{
		LobbyCameraMode.IslandFieldOfView = 100;
		Assert.AreEqual( 100f, new LobbyCameraMode().FieldOfView );
	}

	[TestMethod]
	public void TheIslandViewUsesTheScreenMatrixSoTheVerticalExtentIsHalfTheTangent()
	{
		// tp.exe 0x5d8cb0 builds the lobby's projection with the park's builder (0x578be0) at ISLANDFOV, and the screen matrix of 0x5781d0 scales it as in the park:
		// the visible half height at unit depth is 0.5 x tan(50 degrees) for ISLANDFOV 100, not tan(50 degrees)
		LobbyCameraMode.IslandFieldOfView = 100;
		var camera = new LobbyCameraMode();
		Assert.IsTrue( camera.UsesOriginalProjection );
		var half = 0.5f * System.MathF.Tan( 50f * System.MathF.PI / 180f );
		var projection = OriginalProjection.Create( 4f / 3f, camera.FieldOfView );
		var clip = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( 0f, half, -1f, 1f ), projection );
		Assert.AreEqual( 1f, clip.Y / clip.W, 1e-4f );
	}

	[TestMethod]
	public void TheIslandViewFarPlaneIsTheSpanSelectionsFarthestCornerDistance()
	{
		// tp.exe 0x56bb00 hands the farthest corner distance to 0x578ca0, which rebuilds the projection with it, for the lobby as for the park
		var saved = Camera.Mode;
		var savedFar = Camera.LobbyFarPlane;
		try
		{
			Camera.Mode = new LobbyCameraMode();
			Camera.LobbyFarPlane = ( _, _ ) => 321f;
			Camera.Update();
			var expected = OriginalProjection.Create( Screen.Aspect, LobbyCameraMode.IslandFieldOfView, 321f );
			Assert.AreEqual( expected.M33, Camera.ProjMatrix.M33, 1e-6f );
			Assert.AreEqual( expected.M43, Camera.ProjMatrix.M43, 1e-5f );
			Camera.LobbyFarPlane = null;
			Camera.Update();
			Assert.AreEqual( OriginalProjection.Create( Screen.Aspect, LobbyCameraMode.IslandFieldOfView, 1000f ).M33, Camera.ProjMatrix.M33, 1e-9f, "the builder's 1000 stands until 0x578ca0" );
		}
		finally
		{
			Camera.Mode = saved;
			Camera.LobbyFarPlane = savedFar;
		}
	}

	[TestMethod]
	public void TheGlobeSharesTheLobbyCamerasProjectionWithFarOneThousand()
	{
		// tp.exe 0x5dd034 sets 60.0 in the same camera object whose method 0x5d8cb0 builds the projection: screen matrix and far 1000
		var saved = Camera.Mode;
		try
		{
			Camera.Mode = new GlobeCameraMode();
			Assert.IsTrue( Camera.Mode.UsesOriginalProjection );
			Camera.Update();
			var expected = OriginalProjection.Create( Screen.Aspect, 60f, 1000f );
			Assert.AreEqual( expected.M22, Camera.ProjMatrix.M22, 1e-6f );
			Assert.AreEqual( expected.M33, Camera.ProjMatrix.M33, 1e-6f );
			Assert.AreEqual( 1f / (0.5f * System.MathF.Tan( System.MathF.PI / 6 )), Camera.ProjMatrix.M22, 1e-4f, "half height 0.5 x tan(30 degrees)" );
		}
		finally
		{
			Camera.Mode = saved;
		}
	}

	[TestMethod]
	public void TheEnterTurnsToTheIslandAnglePlusPiThenOpensTheGateAndZooms()
	{
		// tp.exe 0x5e0490 states 1 and 2: 0.05 × step a frame the short way round, the gate within half a step, then the zoom
		LobbyCameraMode.SpinSpeed = 0;
		var camera = new LobbyCameraMode();
		var opened = 0;
		var entered = 0;
		camera.BeginEnter( 1f, () => opened++, () => entered++ );
		var goal = 1f + System.MathF.PI;
		camera.Advance( 0.1f );
		// one step: the angle 0 is 2π - goal = 2.14 behind the goal (under π), so it moves 0.05 the short way round, to -0.05 (6.233 unwrapped)
		Assert.AreEqual( LobbyCameraMode.EnterPhase.Turn, camera.Phase );
		Assert.AreEqual( System.MathF.Tau - 0.05f, camera.Angle, 1e-4f );
		Assert.AreEqual( 0, opened );
		var frames = 0;
		while ( camera.Phase == LobbyCameraMode.EnterPhase.Turn && frames++ < 1000 )
			camera.Advance( 0.1f );
		Assert.AreEqual( 1, opened, "the gate opens once when the turn is done" );
		Assert.AreEqual( LobbyCameraMode.EnterPhase.Zoom, camera.Phase );
		Assert.AreEqual( 0, entered );
		var radius = camera.CurrentRadius;
		camera.Advance( 0.1f );
		Assert.AreEqual( goal, camera.Angle, 1e-5f, "the zoom holds the island angle + π" );
		Assert.AreEqual( radius * (1 - 0.07f), camera.CurrentRadius, 1e-3f );
		frames = 0;
		while ( entered == 0 && frames++ < 1000 )
			camera.Advance( 0.1f );
		Assert.AreEqual( 1, entered, "the park starts once the radius is below 8" );
		Assert.IsTrue( camera.CurrentRadius < 8f );
		Assert.AreEqual( 0x10000, camera.FadeTarget );
		camera.Advance( 0.1f );
		Assert.AreEqual( 1, entered );
	}

	[TestMethod]
	public void TheEnterFadeTracksTheClosedShareOfTheRadiusAndEasesAnEighthAFrame()
	{
		LobbyCameraMode.SpinSpeed = 0;
		var camera = new LobbyCameraMode();
		camera.BeginEnter( 0f, null, null );
		while ( camera.Phase != LobbyCameraMode.EnterPhase.Zoom )
			camera.Advance( 0.1f );
		var before = camera.FadeCurrent;
		camera.Advance( 0.1f );
		var expected = (int)((1f - (camera.CurrentRadius - 8f) / (LobbyCameraMode.Radius - 8f)) * 65536f);
		Assert.AreEqual( expected, camera.FadeTarget );
		Assert.AreEqual( before + ((expected - before) >> 3), camera.FadeCurrent );
		Assert.AreEqual( (byte)(camera.FadeCurrent >> 8), camera.FadeAlpha );
	}

	[TestMethod]
	public void ACancelledEnterResumesTheOrbit()
	{
		var camera = new LobbyCameraMode();
		camera.BeginEnter( 0f, null, null );
		Assert.IsTrue( camera.Entering );
		camera.CancelEnter();
		Assert.IsFalse( camera.Entering );
		Assert.AreEqual( 0, camera.FadeTarget );
	}

	[TestMethod]
	public void ACancelledZoomEasesTheFadeOutAndRestoresTheOrbit()
	{
		LobbyCameraMode.SpinSpeed = 0;
		var camera = new LobbyCameraMode();
		camera.BeginEnter( 0f, null, null );
		while ( camera.Phase != LobbyCameraMode.EnterPhase.Zoom )
			camera.Advance( 0.1f );
		for ( var frame = 0; frame < 20; frame++ )
			camera.Advance( 0.1f );
		Assert.IsTrue( camera.FadeCurrent > 0 && camera.CurrentRadius < LobbyCameraMode.Radius );
		camera.CancelEnter();
		for ( var frame = 0; frame < 400; frame++ )
			camera.Advance( 0.1f );
		Assert.AreEqual( 0, camera.FadeCurrent, "the fade eases all the way out" );
		Assert.AreEqual( 0, camera.FadeAlpha );
		Assert.AreEqual( LobbyCameraMode.Radius, camera.CurrentRadius );
		Assert.AreEqual( LobbyCameraMode.VerticalOffset, camera.CurrentHeight );
	}

	[TestMethod]
	public void TheEnterPutsTheEyeOppositeTheIslandAngleThroughTheTarget()
	{
		// tp.exe 0x5e1210: the eye goal is the target plus (sin a, cos a) × radius with a = the island angle + π, so the eye sits on the side of the gate,
		// which the island models carry at local -z (rotated by the island angle with the lobby scene's row-vector RotationY: (-sin θ, -cos θ))
		LobbyCameraMode.SpinSpeed = 0;
		foreach ( var degrees in new[] { 0f, 90f, 180f, 270f } )
		{
			var island = degrees * System.MathF.PI / 180f;
			var camera = new LobbyCameraMode();
			camera.BeginEnter( island, null, null );
			for ( var frame = 0; frame < 3000 && camera.Phase != LobbyCameraMode.EnterPhase.Zoom; frame++ )
				camera.Advance( 0.1f );
			camera.Advance( 0.1f );
			var gate = new System.Numerics.Vector2( -System.MathF.Sin( island ), -System.MathF.Cos( island ) );
			// the orbit's own direction (the eye's goal is Target + radius × (sin a, cos a))
			var away = new System.Numerics.Vector2( System.MathF.Sin( camera.Angle ), System.MathF.Cos( camera.Angle ) );
			Assert.IsTrue( System.Numerics.Vector2.Dot( away, gate ) > 0.999f, $"the eye goal is on the gate's side for {degrees} degrees" );
		}
	}
}
