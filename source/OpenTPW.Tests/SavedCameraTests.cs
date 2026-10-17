using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// An original save's camera block (tp.exe loader 0x42CEC0: zoom, yaw, flags, the required point of interest, the saved point and yaw) is the pose the loaded park's camera starts at, SAVE-010.
/// </summary>
[TestClass]
public class SavedCameraTests
{
	private static SaveCamera Camera( float zoom = 110, float yaw = 0, float x = 475, float z = 175 ) =>
		new( zoom, yaw, 0, (x, 0, z), (0, 0, 0), 0 );

	[TestMethod]
	public void TheHomePoseOfASaveIsTheHomePoseOfTheCamera()
	{
		var pose = ParkCameraMode.PoseOfSave( Camera() )!.Value;
		Assert.AreEqual( (ParkCameraMode.HomeTargetX, ParkCameraMode.HomeTargetZ, ParkCameraMode.HomeHeight, ParkCameraMode.HomeYaw), pose, "zoom 110, yaw 0 and the entrance target are the camera's own home" );
	}

	[TestMethod]
	public void AMovedCameraKeepsItsPointZoomAndYaw()
	{
		var pose = ParkCameraMode.PoseOfSave( Camera( zoom: 90, yaw: MathF.PI / 2, x: 483.476f, z: 219.092f ) )!.Value;
		Assert.AreEqual( 483.476f, pose.X, 1e-3f );
		Assert.AreEqual( 219.092f, pose.Z, 1e-3f );
		Assert.AreEqual( 90f, pose.Height );
		Assert.AreEqual( -180f, pose.Yaw, 1e-3f, "a quarter turn of the original's yaw is -90 - 90 degrees here" );
		Assert.AreEqual( MathF.PI / 2, ParkCameraMode.OriginalYaw( pose.Yaw ), 1e-4f, "the conversion inverts OriginalYaw" );
	}

	[TestMethod]
	public void AZoomOutsideTheRangeIsClampedAndAnUnreadablePoseIsRefused()
	{
		Assert.AreEqual( ParkCameraMode.MaximumHeight, ParkCameraMode.PoseOfSave( Camera( zoom: 500 ) )!.Value.Height );
		Assert.AreEqual( ParkCameraMode.MinimumHeight, ParkCameraMode.PoseOfSave( Camera( zoom: 1 ) )!.Value.Height );
		Assert.IsNull( ParkCameraMode.PoseOfSave( Camera( zoom: float.NaN ) ) );
		Assert.IsNull( ParkCameraMode.PoseOfSave( Camera( yaw: float.PositiveInfinity ) ) );
		Assert.IsNull( ParkCameraMode.PoseOfSave( Camera( x: -5 ) ), "a point off the map is refused" );
		Assert.IsNull( ParkCameraMode.PoseOfSave( Camera( z: 5000 ) ) );
	}

	[TestMethod]
	public void EveryCorpusSavesCameraIsAPoseOnTheMapAtTheStandardZoom()
	{
		OriginalSaveImportCorpusTests.ForEachSave( ( name, save, economy, import ) =>
		{
			var pose = ParkCameraMode.PoseOfSave( save.RuntimeBlocks!.Camera );
			Assert.IsNotNull( pose, name );
			Assert.AreEqual( (ParkCameraMode.HomeHeight, ParkCameraMode.HomeYaw), (pose!.Value.Height, pose.Value.Yaw), name );
			Assert.IsTrue( pose.Value.X is > 400 and < 560 && pose.Value.Z is > 150 and < 260, $"{name}: ({pose.Value.X}, {pose.Value.Z}) is near the entrance" );
		} );
	}
}
