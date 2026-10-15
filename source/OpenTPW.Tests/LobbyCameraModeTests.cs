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
}
