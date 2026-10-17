using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;

namespace OpenTPW.Tests;

[TestClass]
public class SlideRiderRotationTests
{
	private static readonly Vector2 Right = new( 1, 0 ), Forward = new( 0, 1 );

	private static float Residual( float degrees, float foreshortening = 1f )
	{
		var radians = degrees * MathF.PI / 180;
		var heading = Right * MathF.Sin( radians ) + Forward * MathF.Cos( radians );
		return GuestRenderer.ResidualRotation( heading, Right, Forward, Right, new Vector2( 0, foreshortening ) ) * 180 / MathF.PI;
	}

	[TestMethod]
	public void NoRotationWhenTheHeadingMatchesASpriteDirection()
	{
		foreach ( var degrees in new[] { 0f, 45, 90, 135, 180, -45, -90, -135 } )
			Assert.AreEqual( 0f, Residual( degrees ), 0.01f, $"{degrees}" );
		Assert.AreEqual( 0f, GuestRenderer.ResidualRotation( Vector2.Zero, Right, Forward, Right, new Vector2( 0, 1 ) ), "no heading, no rotation" );
	}

	[TestMethod]
	public void TheResidualIsTheSnapErrorAndNeverMoreThan22AndAHalfDegrees()
	{
		for ( var degrees = -179f; degrees <= 179f; degrees += 0.5f )
		{
			var residual = Residual( degrees );
			Assert.IsTrue( MathF.Abs( residual ) <= 22.51f, $"{degrees}: {residual}" );
			var nearest = MathF.Round( degrees / 45f ) * 45f;
			if ( MathF.Abs( degrees - nearest ) < 22.4f ) // not on a tie between two directions
				Assert.AreEqual( degrees - nearest, residual, 0.02f, $"{degrees}" );
		}
		Assert.AreEqual( 20f, Residual( 20f ), 0.01f );
		Assert.AreEqual( -20f, Residual( -20f ), 0.01f );
		Assert.AreEqual( 22.4f, Residual( 22.4f ), 0.01f );
	}

	[TestMethod]
	public void OnlyTheSlidingSlotIsRotatedAndNeverAnUprightOne()
	{
		CollectionAssert.AreEquivalent( new[] { PoolSwimSlots.Sliding }, PoolSwimSlots.Rotated.ToArray() );
		foreach ( var upright in new[] { PoolSwimSlots.Stand, PoolSwimSlots.Walk, PoolSwimSlots.Run, PoolSwimSlots.LadderClimb, PoolSwimSlots.Shower } )
			Assert.IsFalse( PoolSwimSlots.Rotated.Contains( upright ) );
	}
}
