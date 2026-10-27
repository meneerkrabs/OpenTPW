using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using V3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The directed edge gate of the face sampler (tp.exe <c>0x4514CF..0x45153A</c>, TRACK-001) against an emulation of its x87 instructions at the control word 0x7F the game's thread runs at (24-bit
/// mantissa): every result of an x87 operation is rounded to a single, which is what the host's float operations do in the same order.
/// </summary>
[TestClass]
public class TrackSurfaceEdgeGateTests
{
	/// <summary>
	/// The instruction sequence for the edge B → A and the point (px, pz): <c>dz = Az − Bz</c>, <c>dx = Bx − Ax</c>, then <c>((dz × px) − ((dz × Bx) + (dx × Bz))) + (dx × pz)</c>, outside when it is below 0.0
	/// (<c>fsub, fsubp, fst, fmul, fmul, faddp, fmul, fchs, faddp, faddp, fcomp</c>). The converted axes put the original's z on Y.
	/// </summary>
	private static bool X87Inside( V3 b, V3 a, float px, float pz )
	{
		var dz = Ext.FromFloat( a.Y ) - Ext.FromFloat( b.Y );
		var dx = Ext.FromFloat( b.X ) - Ext.FromFloat( a.X );
		var origin = dz * Ext.FromFloat( b.X ) + dx * Ext.FromFloat( b.Y );
		var e = dz * Ext.FromFloat( px ) + origin.Negate() + dx * Ext.FromFloat( pz );
		return e.CompareTo( Ext.Zero ) >= 0;
	}

	[TestMethod]
	public void TheHostFloatGateEqualsTheX87SequenceAtTwentyFourBits()
	{
		var random = new Random( 4514 );
		float Pick( int scale ) => (float)(Math.Round( random.NextDouble() * 400 - 200, 1 ) / scale);
		var disagreements = 0;
		for ( var step = 0; step < 60000; step++ )
		{
			var scale = random.Next( 1, 5 );
			var a = new V3( Pick( scale ), Pick( scale ), 0 );
			var b = new V3( Pick( scale ), Pick( scale ), 0 );
			// points on or beside the edge, where the sign decides: the edge's own points and their float neighbours
			var along = (float)random.NextDouble();
			var (px, pz) = (b.X + (a.X - b.X) * along, b.Y + (a.Y - b.Y) * along);
			if ( step % 3 == 1 )
				px = MathF.BitIncrement( px );
			else if ( step % 3 == 2 )
				pz = MathF.BitDecrement( pz );
			var host = TrackSurface.InsideDirectedEdge( b, a, px, pz );
			var emulated = X87Inside( b, a, px, pz );
			if ( host != emulated )
				disagreements++;
		}
		Assert.AreEqual( 0, disagreements, "the same association of the same single-precision operations gives the same sign" );
	}

	[TestMethod]
	public void TheEmulationIsSensitiveToThePrecisionControl()
	{
		// the same sequence at 53 bits disagrees with the 24-bit one on some boundary points, so the equality above is not vacuous
		var random = new Random( 4514 );
		var differing = 0;
		for ( var step = 0; step < 60000 && differing == 0; step++ )
		{
			var a = new V3( (float)(random.NextDouble() * 400 - 200), (float)(random.NextDouble() * 400 - 200), 0 );
			var b = new V3( (float)(random.NextDouble() * 400 - 200), (float)(random.NextDouble() * 400 - 200), 0 );
			var along = (float)random.NextDouble();
			var (px, pz) = (b.X + (a.X - b.X) * along, b.Y + (a.Y - b.Y) * along);
			var single = X87Inside( b, a, px, pz );
			var wide = X87Precision.With( 53, () => X87Inside( b, a, px, pz ) );
			if ( single != wide )
			{
				differing++;
				Assert.AreEqual( single, TrackSurface.InsideDirectedEdge( b, a, px, pz ), "the host agrees with the 24-bit result where the wider one differs" );
			}
		}
		Assert.AreEqual( 1, differing, "a point on an edge where the precision control decides was found" );
	}

	[TestMethod]
	public void ATrianglesPointOnAnEdgeIsInsideAndOutsideTheOtherSide()
	{
		// a clockwise triangle in the converted axes: (0, 0), (0, 10), (10, 0) taken as the sampler's edges V2 → V0, V1 → V2, V0 → V1
		var (a, b, c) = (new V3( 0, 0, 0 ), new V3( 0, 10, 0 ), new V3( 10, 0, 0 ));
		bool Covers( float x, float y ) => TrackSurface.InsideDirectedEdge( c, a, x, y ) && TrackSurface.InsideDirectedEdge( b, c, x, y ) && TrackSurface.InsideDirectedEdge( a, b, x, y );
		Assert.IsTrue( Covers( 2, 2 ) );
		Assert.IsTrue( Covers( 5, 5 ), "on the long edge: the gate has no epsilon, 0.0 is inside (0x45151F)" );
		Assert.IsTrue( Covers( 0, 0 ), "on a vertex" );
		Assert.IsFalse( Covers( 5.01f, 5.01f ) );
		Assert.IsFalse( Covers( -0.01f, 3 ) );
	}
}
