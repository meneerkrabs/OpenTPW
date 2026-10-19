using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary><see cref="DetMath"/>: the deterministic elementary functions of simulation code (docs/DETERMINISM.md, "Browser and native").</summary>
[TestClass]
public class DetMathTests
{
	/// <summary>
	/// DetMath's results over the probe's fixed inputs, pinned. The same values on every runtime (Windows, Linux and macOS in CI, and the
	/// browser's WebAssembly through tools/web-determinism) is the point of DetMath; a change of a routine moves its pin.
	/// </summary>
	[TestMethod]
	public void DetMathDigestsArePinned()
	{
		var digests = CrossRuntimeMathProbe.Digests();
		var pinned = new (string Name, string Digest)[]
		{
			("DetMath.Sin", "PIN"),
			("DetMath.Cos", "PIN"),
			("DetMath.Atan", "PIN"),
			("DetMath.Atan2", "PIN"),
			("DetMath.Acos", "PIN"),
			("DetMath.Pow", "PIN"),
			("DetMath.Log2", "PIN"),
			("DetMath.SinF", "PIN"),
			("DetMath.CosF", "PIN"),
			("DetMath.Atan2F", "PIN"),
			("DetMath.AcosF", "PIN"),
		};
		Console.WriteLine( string.Join( Environment.NewLine, digests.Select( pair => $"{pair.Key} = {pair.Value}" ) ) );
		foreach ( var (name, digest) in pinned )
			Assert.AreEqual( digest, digests[name], name );
	}

	/// <summary>The correctly rounded functions are the same everywhere: square root and fused multiply-add (IEEE 754 requires them exact).</summary>
	[TestMethod]
	public void CorrectlyRoundedPlatformFunctionsArePinned()
	{
		var digests = CrossRuntimeMathProbe.Digests();
		Assert.AreEqual( "PIN", digests["Math.Sqrt"], "Math.Sqrt" );
		Assert.AreEqual( "PIN", digests["MathF.Sqrt"], "MathF.Sqrt" );
		Assert.AreEqual( "PIN", digests["Math.FusedMultiplyAdd"], "Math.FusedMultiplyAdd" );
		Assert.AreEqual( "PIN", digests["MathF.FusedMultiplyAdd"], "MathF.FusedMultiplyAdd" );
	}

	private static long UlpDistance( double a, double b )
	{
		if ( double.IsNaN( a ) || double.IsNaN( b ) )
			return double.IsNaN( a ) && double.IsNaN( b ) ? 0 : long.MaxValue;
		if ( a == b )
			return 0;
		long Ordered( double value )
		{
			var bits = BitConverter.DoubleToInt64Bits( value );
			return bits < 0 ? long.MinValue - bits : bits;
		}
		return Math.Abs( Ordered( a ) - Ordered( b ) );
	}

	/// <summary>The port is faithful: within one unit in the last place of the platform's own function for every probe input, huge arguments included.</summary>
	[TestMethod]
	public void DetMathIsWithinOneUlpOfThePlatformLibrary()
	{
		var results = CrossRuntimeMathProbe.Results().ToDictionary( result => result.Name, result => result.Bits );
		foreach ( var (platform, deterministic) in new[] { ("Math.Sin", "DetMath.Sin"), ("Math.Cos", "DetMath.Cos"), ("Math.Atan", "DetMath.Atan"),
			("Math.Atan2", "DetMath.Atan2"), ("Math.Acos", "DetMath.Acos"), ("Math.Pow", "DetMath.Pow"), ("Math.Log2", "DetMath.Log2") } )
		{
			var worst = results[platform].Zip( results[deterministic] )
				.Select( pair => UlpDistance( BitConverter.UInt64BitsToDouble( pair.First ), BitConverter.UInt64BitsToDouble( pair.Second ) ) ).Max();
			Assert.IsTrue( worst <= 1, $"{deterministic} differs from {platform} by {worst} ulp" );
		}
	}

	[TestMethod]
	public void SpecialValuesFollowTheLibraryContract()
	{
		Assert.AreEqual( 0.0, DetMath.Sin( 0.0 ) );
		Assert.IsTrue( double.IsNegative( DetMath.Sin( -0.0 ) ) );
		Assert.AreEqual( 1.0, DetMath.Cos( 0.0 ) );
		Assert.IsTrue( double.IsNaN( DetMath.Sin( double.PositiveInfinity ) ) );
		Assert.AreEqual( BitConverter.DoubleToUInt64Bits( double.NaN ), BitConverter.DoubleToUInt64Bits( DetMath.Cos( double.NegativeInfinity ) ), "NaN results are the canonical NaN" );
		Assert.AreEqual( Math.PI, DetMath.Atan2( 0.0, -1.0 ) );
		Assert.AreEqual( -Math.PI / 2, DetMath.Atan2( -1.0, 0.0 ) );
		Assert.AreEqual( 0.0, DetMath.Acos( 1.0 ) );
		Assert.AreEqual( Math.PI, DetMath.Acos( -1.0 ) );
		Assert.IsTrue( double.IsNaN( DetMath.Acos( 1.5 ) ) );
		Assert.AreEqual( 1.0, DetMath.Pow( double.NaN, 0 ) );
		Assert.AreEqual( 1024.0, DetMath.Pow( 2, 10 ) );
		Assert.AreEqual( 3.0, DetMath.Log2( 8 ) );
		Assert.AreEqual( double.NegativeInfinity, DetMath.Log2( 0 ) );
		Assert.AreEqual( 1.5, DetMath.Pow( 2.25, 0.5 ) );
	}
}
