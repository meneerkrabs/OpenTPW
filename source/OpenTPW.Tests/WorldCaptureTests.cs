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
		var previous = GameOptions.Current;
		try
		{
			// the normal range, with the wide zoom option off
			GameOptions.Current = new GameOptions { WideZoom = false };
			var camera = new ParkCameraMode();
			camera.SetPose( new Vector3( 3, 4, 9 ), 1, 405 );
			Assert.AreEqual( new Vector3( 3, 4, 0 ), camera.Target );
			Assert.AreEqual( 70f, camera.Height, "the original's nearest zoom" );
			Assert.AreEqual( 45f, camera.Yaw );
			camera.SetPose( new Vector3( 3, 4, 9 ), 500, 0 );
			Assert.AreEqual( 130f, camera.Height, "and its farthest" );
			// [EXT:wide-zoom] the default option widens it to the original's own flag-0x200 range
			GameOptions.Current = new GameOptions();
			var wide = new ParkCameraMode();
			wide.SetPose( new Vector3( 3, 4, 9 ), 1, 405 );
			Assert.AreEqual( 40f, wide.Height );
			// its far end follows FIRSTPERSONVIEWDISTANCE: all 300 at setting 3, about 173 at setting 2 (the High preset)
			RenderFog.OptionOverride = 3;
			wide.SetPose( new Vector3( 3, 4, 9 ), 500, 0 );
			Assert.AreEqual( 300f, wide.Height );
			RenderFog.OptionOverride = 2;
			wide.SetPose( new Vector3( 3, 4, 9 ), 500, 0 );
			Assert.AreEqual( 173f, wide.Height, 1f );
		}
		finally { GameOptions.Current = previous; RenderFog.OptionOverride = null; }
	}
}
