using System;
using NVector3 = System.Numerics.Vector3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class GlobeTests
{
	[TestMethod]
	public void GeographicPositionsFollowTheGlobeTextureLayout()
	{
		// Longitude 0 on the equator lies at atan2(x, z) = 30° (the middle of texture column 4 is at 0°, its west edge at 30°).
		var greenwich = GlobeScene.Direction( 0, 0 );
		Assert.AreEqual( 30f, MathF.Atan2( greenwich.X, greenwich.Z ) * 180 / MathF.PI, 1e-3f );
		Assert.AreEqual( 0f, greenwich.Y, 1e-6f );
		var pole = GlobeScene.Direction( 90, 123 );
		Assert.AreEqual( 1f, pole.Y, 1e-6f, "the model's +Y axis is the north pole" );
		var west = GlobeScene.Direction( 0, -150 );
		Assert.AreEqual( 180f, MathF.Abs( MathF.Atan2( west.X, west.Z ) * 180 / MathF.PI ), 1e-3f, "150° W is the middle of the first texture column" );
	}

	[TestMethod]
	public void PinsStandOutwardAndGrowWithTheirParks()
	{
		var direction = GlobeScene.Direction( 51.5f, -0.1f );
		var few = GlobeScene.PinPlacement( direction, 0 );
		var many = GlobeScene.PinPlacement( direction, 500 );
		var foot = NVector3.Transform( NVector3.Zero, few );
		Assert.AreEqual( GlobeScene.PinRadius, foot.Length(), 1e-3f, "the pin's origin is on the surface" );
		var up = NVector3.TransformNormal( NVector3.UnitY, few );
		Assert.AreEqual( 1f, NVector3.Dot( NVector3.Normalize( up ), direction ), 1e-5f, "the pin's height axis points away from the centre" );
		Assert.IsTrue( NVector3.TransformNormal( NVector3.UnitY, many ).Length() > up.Length() * 4, "a city with many parks gets a taller pin (p from 0.02 to the 0.1 cap)" );
	}
}
