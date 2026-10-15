using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The ride camera's frame (OpenTPW <see cref="RideCameraMode"/>): the eye sits on the ridden carriage's height
/// above the ride's anchor and looks along the heading, flattened to the ground. The frame math is asset-free;
/// the entry and exit of <see cref="Level.EnterRideView"/> need a running level and are covered by the smoke test.
/// </summary>
[TestClass]
public class RideCameraModeTests
{
	[TestMethod]
	public void PrototypeEyeSitsAboveTheCarriageAndLooksAlongTheHeading()
	{
		var (eye, look) = RideCameraMode.PrototypeFrame( new Vector3( 1, 2, 3 ), motionHeight: 4, new Vector3( 1, 0, 5 ) );
		// [APPROX:UI-094] the eye is motion height plus PrototypeSeatHeight above the anchor; the heading is flattened (the Z part is dropped)
		Assert.AreEqual( new Vector3( 1, 2, 3 + 4 + RideCameraMode.PrototypeSeatHeight ), eye );
		Assert.AreEqual( eye + new Vector3( 1, 0, 0 ), look );
	}

	[TestMethod]
	public void PrototypeWithoutHeadingLooksForward()
	{
		var (eye, look) = RideCameraMode.PrototypeFrame( Vector3.Zero, motionHeight: 0, Vector3.Zero );
		Assert.AreEqual( eye + Vector3.Forward, look, "no horizontal heading falls back to forward" );
	}

	[TestMethod]
	public void WorldTranslationDoesNotChangeTheRideViewDirection()
	{
		var heading = new Vector3( 3, 4, 0 ).Normal;
		var (eye, look) = RideCameraMode.PrototypeFrame( new Vector3( 1, 2, 3 ), 4, heading );
		var offset = new Vector3( 100, -250, 80 );
		var original = RideCameraMode.FrameRotation( eye, look ).Forward;
		var translated = RideCameraMode.FrameRotation( eye + offset, look + offset ).Forward;
		Assert.AreEqual( 0f, (heading - original).Length, 0.0001f );
		Assert.AreEqual( 0f, (original - translated).Length, 0.0001f, "a look target is relative to the eye, not the world origin" );
	}

	[TestMethod]
	public void PrototypeFieldOfViewIsTheParkViewsDefault()
	{
		var camera = new RideCameraMode( null!, Vector3.Forward );
		Assert.AreEqual( RideCameraMode.ViewFieldOfView, camera.FieldOfView );
	}
}
