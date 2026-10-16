using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The jungle bumper car's wake (tp.exe 0x00546280, flag 0x2000000): the second instance trails the car by its speed, 0.7 above it, turned by the heading only.</summary>
[TestClass]
public class BumperLookTests
{
	[TestMethod]
	public void TheWakeTrailsTheCarBySpeedTimesFourOver49143()
	{
		Assert.AreEqual( 49143f, BitConverter.Int32BitsToSingle( 0x473FF700 ), "the float at 0x764CD8" );
		// heading 0 faces +y (sine 0, cosine 255 in the truncated table): at speed 300 the wake lies 255 × 300 × 4 / 49143 = 6.23 units behind
		var still = TrackRenderer.WakeTransform( 50, 60, 30, 0, 0, System.Numerics.Vector2.Zero );
		var moving = TrackRenderer.WakeTransform( 50, 60, 30, 0, 300, System.Numerics.Vector2.Zero );
		Assert.AreEqual( new System.Numerics.Vector3( 50, 60, 30.7f ) * ObjectPlacement.ModelScale, still.Translation, "standing: under the car, 0.7 above its height" );
		Assert.AreEqual( 50 * ObjectPlacement.ModelScale, moving.Translation.X, 1e-4f );
		Assert.AreEqual( 255, BumpMath.Cos( 0 ) );
		Assert.AreEqual( (60 - 255 * 300 * 4 / 49143f) * ObjectPlacement.ModelScale, moving.Translation.Y, 1e-4f );
		// a quarter turn (128) faces +x: the wake falls back along x
		var turned = TrackRenderer.WakeTransform( 50, 60, 30, 128, 300, System.Numerics.Vector2.Zero );
		Assert.AreEqual( (50 - BumpMath.Sin( 128 ) * 300 * 4 / 49143f) * ObjectPlacement.ModelScale, turned.Translation.X, 1e-4f );
		// level: the wake's up axis stays up whatever the heading
		var up = System.Numerics.Vector3.TransformNormal( System.Numerics.Vector3.UnitZ, turned );
		Assert.AreEqual( 0f, up.X, 1e-5f );
		Assert.AreEqual( 0f, up.Y, 1e-5f );
	}
}
