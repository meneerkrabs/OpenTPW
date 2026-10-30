using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>COAST-033: the clip sampler's table-sine rotation blend (tp.exe 0x4742A0) in both its forms, the platform's and the deterministic one the pylon rig uses.</summary>
[TestClass]
public class ModelRotationBlendTests
{
	private static Quaternion About( System.Numerics.Vector3 axis, float angle ) => Quaternion.CreateFromAxisAngle( axis, angle );

	private static void Close( Quaternion expected, Quaternion actual, float tolerance, string what ) =>
		Assert.IsTrue( MathF.Abs( expected.X - actual.X ) <= tolerance && MathF.Abs( expected.Y - actual.Y ) <= tolerance && MathF.Abs( expected.Z - actual.Z ) <= tolerance && MathF.Abs( expected.W - actual.W ) <= tolerance,
			$"{what}: expected {expected}, got {actual}" );

	[TestMethod]
	public void ASpreadOfKeysBlendsAsTheExactSlerpWithinTheTableStep()
	{
		var from = About( System.Numerics.Vector3.UnitY, 0.3f );
		var to = About( System.Numerics.Vector3.UnitY, 2.1f );
		foreach ( var t in new[] { 0.1f, 0.25f, 0.5f, 0.77f, 0.99f } )
		{
			var expected = About( System.Numerics.Vector3.UnitY, 0.3f + (2.1f - 0.3f) * t );
			// the weights are table sines at steps of 2 pi / 4096 (0.0015 rad), over a sine table entry
			Close( expected, ModelAnimationTrack.Slerp( from, to, t ), 0.004f, $"platform t={t}" );
			Close( expected, DetMatrix.Slerp( from, to, t ), 0.004f, $"deterministic t={t}" );
		}
	}

	[TestMethod]
	public void TheWeightsAreTableEntriesNotExactSines()
	{
		var from = Quaternion.Identity;
		var to = About( System.Numerics.Vector3.UnitX, 1f );
		var exact = About( System.Numerics.Vector3.UnitX, 0.37f );
		var blended = ModelAnimationTrack.Slerp( from, to, 0.37f );
		Assert.AreNotEqual( exact, blended, "the table's quantised weights are not the exact ones" );
		Assert.AreEqual( DetMatrix.Slerp( from, to, 0.37f ).X, blended.X, 1e-6f, "both forms use the same table to the last digit of a table sine" );
	}

	[TestMethod]
	public void TheOppositeKeysBranchKeepsTheFourthComponentOfTheFirstKeyAsTheOriginalDoes()
	{
		// no shipped clip has two consecutive keys with a dot product of -1 (0 of 23194 pairs of the corpus); the branch 0x4743E0 builds (-from.y, from.x, -from.w, from.z), blends x, y and z
		// with the table weights and never writes the fourth component again, so it stays from.z; the sampler's own normalisation follows
		var from = new Quaternion( 0, 0, 0, 1 );
		var to = new Quaternion( 0, 0, 0, -1 );
		var half = DetMatrix.Slerp( from, to, 0.5f );
		Close( new Quaternion( 0, 0, -1, 0 ), half, 0.001f, "t = 0.5: z = -sin(π/4), w = from.z = 0, normalised" );
		var turned = DetMatrix.Slerp( new Quaternion( 0.5f, 0.5f, 0.5f, -0.5f ), new Quaternion( -0.5f, -0.5f, -0.5f, 0.5f ), 0.25f );
		Assert.IsTrue( float.IsFinite( turned.X ) && float.IsFinite( turned.Y ) && float.IsFinite( turned.Z ) && float.IsFinite( turned.W ) );
	}

	[TestMethod]
	public void NearlyEqualKeysBlendLinearly()
	{
		var from = new Quaternion( 0, 0, 0, 1 );
		var to = Quaternion.Normalize( new Quaternion( 0, 0.0004f, 0, 1 ) );
		var blended = DetMatrix.Slerp( from, to, 0.5f );
		Assert.AreEqual( 0.0002f, blended.Y, 1e-5f );
	}
}
