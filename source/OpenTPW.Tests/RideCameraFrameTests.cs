using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class RideCameraFrameTests
{
	[TestMethod]
	public void ATranslatedRideKeepsItsViewingDirection()
	{
		var eye = new Vector3( 12, -40, 18 );
		var direction = new Vector3( 1, 2, 0.5f ).Normal;
		var rotation = RideCameraMode.FrameRotation( eye, eye + direction );
		var translation = new Vector3( -80, 125, 30 );
		var moved = RideCameraMode.FrameRotation( eye + translation, eye + direction + translation );
		Assert.IsTrue( (rotation.Forward - direction).Length < 0.0001f );
		Assert.IsTrue( (moved.Forward - direction).Length < 0.0001f );
		Assert.IsTrue( (moved.Forward - rotation.Forward).Length < 0.0001f,
			"moving a ride must not rotate the camera toward a world position" );
	}
}
