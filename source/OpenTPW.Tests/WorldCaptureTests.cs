using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class WorldCaptureTests
{
	[TestMethod]
	public void ParsePoseReadsInvariantNumbers()
	{
		var pose = WorldCapture.ParsePose( "10.5,-4,30,-135" );
		Assert.AreEqual( new WorldCapture.CameraPose( 10.5f, -4f, 30f, -135f ), pose );
	}

	[TestMethod]
	[DataRow( null )]
	[DataRow( "" )]
	[DataRow( "1,2,3" )]
	[DataRow( "1,2,3,4,5" )]
	[DataRow( "1,2,x,4" )]
	[DataRow( "1,2,NaN,4" )]
	[DataRow( "1,2,Infinity,4" )]
	public void ParsePoseRejectsMalformedText( string? text ) => Assert.IsNull( WorldCapture.ParsePose( text ) );

	[TestMethod]
	public void SequencePathNumbersTheFileName() =>
		Assert.AreEqual( System.IO.Path.Combine( "out", "shot-0007.png" ), WorldCapture.SequencePath( System.IO.Path.Combine( "out", "shot.png" ), 7 ) );

	[TestMethod]
	public void SetPoseClampsHeightAndKeepsTargetOnTheGround()
	{
		var camera = new ParkCameraMode();
		camera.SetPose( new Vector3( 3, 4, 9 ), 1, 405 );
		Assert.AreEqual( new Vector3( 3, 4, 0 ), camera.Target );
		Assert.AreEqual( 12f, camera.Height );
		Assert.AreEqual( 45f, camera.Yaw );
	}
}
