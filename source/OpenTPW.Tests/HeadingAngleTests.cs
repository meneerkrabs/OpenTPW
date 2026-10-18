using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The 11-bit heading angle of tp.exe's arctangent routine (<c>0x006E7074</c>) and the direction a guest faces after it.</summary>
[TestClass]
public class HeadingAngleTests
{
	[TestMethod]
	public void TheAxesAndDiagonalsAreTheRoutinesQuadrantBases()
	{
		Assert.AreEqual( 0, HeadingAngle.Of( 0, 0 ) );
		Assert.AreEqual( 0x000, HeadingAngle.Of( 0, -1 ), "−Y" );
		Assert.AreEqual( 0x200, HeadingAngle.Of( 1, 0 ), "the first word's +X" );
		Assert.AreEqual( 0x400, HeadingAngle.Of( 0, 1 ), "+Y" );
		Assert.AreEqual( 0x600, HeadingAngle.Of( -1, 0 ) );
		Assert.AreEqual( 0x100, HeadingAngle.Of( 1, -1 ), "ratio 256 / 256 reads the table's last entry, 256" );
		Assert.AreEqual( 0x300, HeadingAngle.Of( 1, 1 ) );
		Assert.AreEqual( 0x500, HeadingAngle.Of( -1, 1 ) );
		Assert.AreEqual( 0x700, HeadingAngle.Of( -1, -1 ) );
	}

	[TestMethod]
	public void TheAngleGrowsWithTheTurnAndStaysInsideTheTable()
	{
		var last = -1;
		for ( var step = 0; step < 720; step++ )
		{
			var radians = step * Math.PI / 360;   // from −Y through −X in the routine's own order: first = −sin, second = −cos
			var first = (int)Math.Round( -Math.Sin( radians ) * -1000 );
			var second = (int)Math.Round( Math.Cos( radians ) * -1000 );
			var angle = HeadingAngle.Of( first, second );
			var expected = (int)Math.Round( radians * HeadingAngle.Turn / (2 * Math.PI) ) & 0x7FF;
			Assert.IsTrue( Math.Abs( ((angle - expected + 0x400) & 0x7FF) - 0x400 ) <= 2, $"step {step}: {angle} against {expected}" );
			if ( step > 0 && angle < last && last - angle < 0x400 )
				Assert.Fail( $"step {step} went back from {last} to {angle}" );
			last = angle;
		}
	}

	[TestMethod]
	public void FacingPointsAlongTheOffsetToWithinTheAngleUnit()
	{
		foreach ( var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (2, 1), (-1, 2), (1, -2), (-2, -1), (2, 2), (-1, 1) } )
		{
			var (x, y) = HeadingAngle.Facing( dx, dy );
			var length = MathF.Sqrt( dx * dx + dy * dy );
			Assert.AreEqual( dx / length, x, 0.004f, $"({dx}, {dy}) x" );
			Assert.AreEqual( dy / length, y, 0.004f, $"({dx}, {dy}) y" );
		}
	}
}
