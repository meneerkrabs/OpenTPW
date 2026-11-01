using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The camcorder's static is tp.exe's 320 x 240 noise (<c>0x53E760</c>): black three times in four, white and grey 150 an eighth each.</summary>
[TestClass]
public class RideStaticNoiseTests
{
	[TestMethod]
	public void TheDrawsAreTheCrtsGenerator()
	{
		// the CRT's rand after srand(1): 41, 18467, 6334, 26500, 19169 (state x 214013 + 2531011, bits 16 to 30)
		var state = 1u;
		CollectionAssert.AreEqual( new uint[] { 41, 18467, 6334, 26500, 19169 }, Enumerable.Range( 0, 5 ).Select( _ => RideStaticNoise.Next( ref state ) ).ToArray() );
	}

	[TestMethod]
	public void ThePixelsAreBlackWhiteAndGreyInTheOriginalsProportionsAndWalkedColumnByColumn()
	{
		var pixels = new byte[RideStaticNoise.Width * RideStaticNoise.Height];
		RideStaticNoise.Fill( pixels, 12345 );
		var counts = new[] { pixels.Count( p => p == RideStaticNoise.Black ), pixels.Count( p => p == RideStaticNoise.Grey ), pixels.Count( p => p == RideStaticNoise.White ) };
		Assert.AreEqual( pixels.Length, counts.Sum() );
		Assert.AreEqual( 0.75, counts[0] / (double)pixels.Length, 0.01, "three in four are black" );
		Assert.AreEqual( 0.125, counts[1] / (double)pixels.Length, 0.01, "an eighth is grey" );
		Assert.AreEqual( 0.125, counts[2] / (double)pixels.Length, 0.01, "an eighth is white" );
		// the first column is drawn first, from the generator's first draws: replay them
		var state = 12345u;
		for ( var y = 0; y < RideStaticNoise.Height; y++ )
		{
			var expected = RideStaticNoise.Black;
			if ( (RideStaticNoise.Next( ref state ) & 3) == 0 )
				expected = (RideStaticNoise.Next( ref state ) & 1) != 0 ? RideStaticNoise.White : RideStaticNoise.Grey;
			Assert.AreEqual( expected, pixels[y * RideStaticNoise.Width], $"row {y} of the first column" );
		}
	}

	[TestMethod]
	public void ANewSeedGivesNewNoise()
	{
		var first = new byte[RideStaticNoise.Width * RideStaticNoise.Height];
		var second = new byte[first.Length];
		RideStaticNoise.Fill( first, 1 );
		RideStaticNoise.Fill( second, 2 );
		CollectionAssert.AreNotEqual( first, second );
	}
}
