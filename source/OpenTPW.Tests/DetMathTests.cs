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
			("DetMath.Sin", "01CC3A7FD54A72CF"),
			("DetMath.Cos", "EA2D25BF8F645BFC"),
			("DetMath.Atan", "0A7BD01CCD016A7A"),
			("DetMath.Atan2", "8B2D2273846A71E0"),
			("DetMath.Acos", "CE2879A1A4AAC746"),
			("DetMath.Pow", "F8A652E4DCB55A72"),
			("DetMath.Log2", "9C58CED87AF61BFA"),
			("DetMath.SinF", "787B222A3E28D75A"),
			("DetMath.CosF", "692A75B98B026D5D"),
			("DetMath.Atan2F", "D83E3DC9E1CA6A54"),
			("DetMath.AcosF", "F4574816A8C633B3"),
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
		Assert.AreEqual( "6D95DD8BF98FCE42", digests["Math.Sqrt"], "Math.Sqrt" );
		Assert.AreEqual( "81C0DA7BC4AB6414", digests["MathF.Sqrt"], "MathF.Sqrt" );
		Assert.AreEqual( "639D23E88AB141DD", digests["Math.FusedMultiplyAdd"], "Math.FusedMultiplyAdd" );
		Assert.AreEqual( "9EA201E6896D55B7", digests["MathF.FusedMultiplyAdd"], "MathF.FusedMultiplyAdd" );
	}

	/// <summary>
	/// <see cref="DetMatrix"/>'s plain matrix and vector arithmetic over the probe's fixed inputs, pinned: the same on every runtime, whether or not it fuses
	/// multiply-adds.
	/// </summary>
	[TestMethod]
	public void DetMatrixDigestsArePinned()
	{
		var digests = CrossRuntimeMathProbe.Digests();
		var pinned = new (string Name, string Digest)[]
		{
			("DetMatrix.Multiply", "FA3C778478B438C8"),
			("DetMatrix.TransformPosition", "B966CD083F830EF7"),
			("DetMatrix.Lerp", "22BC04FC179F5D91"),
			("DetMatrix.Slerp", "835A114DD5A79937"),
		};
		foreach ( var (name, digest) in pinned )
			Assert.AreEqual( digest, digests[name], name );
	}

	/// <summary>
	/// System.Numerics' <c>Matrix4x4</c> product, <c>Vector3.Transform</c> and <c>Vector3.Lerp</c> evaluate with <c>MultiplyAddEstimate</c>, which fuses the multiply and
	/// the add where the processor has fused multiply-add (Windows and Linux x64, macOS arm64) and rounds twice where it has not. The digests are those of the
	/// fused desktop runtimes. The browser's Mono AOT build computes other values (the interpreter matches), so this comparison is informative in the browser
	/// check, like the platform library's: it documents the difference, and simulation code uses <see cref="DetMatrix"/> instead (docs/DETERMINISM.md, "Browser and native").
	/// </summary>
	[TestMethod]
	[TestCategory( PlatformLibrary )]
	public void FusedSystemNumericsOperationsArePinned()
	{
		var digests = CrossRuntimeMathProbe.Digests();
		Assert.AreEqual( "D7194F8E92CB6F70", digests["Matrix4x4.Multiply"], "Matrix4x4.Multiply" );
		Assert.AreEqual( "E6741682D33CF59E", digests["Vector3.Transform"], "Vector3.Transform" );
		Assert.AreEqual( "2E265BE756605AD5", digests["Vector3.Lerp"], "Vector3.Lerp" );
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

	/// <summary>The lookup tables the simulation builds from sines and arctangents at start-up, pinned: the same on every runtime once they are built with DetMath.</summary>
	[TestMethod]
	public void LookupTablesArePinned()
	{
		var hash = 14695981039346656037UL;
		void Add( long value )
		{
			for ( var shift = 0; shift < 64; shift += 8 )
				hash = (hash ^ (ulong)((value >> shift) & 0xFF)) * 1099511628211UL;
		}
		for ( var k = 0; k < CoasterTrig.Size; k++ )
		{
			Add( BitConverter.SingleToInt32Bits( CoasterTrig.Entry( k ) ) );
			Add( BitConverter.SingleToInt32Bits( CoasterTrig.PcEntry( k ) ) );
			Add( TourWorld.Sin( k ) );
		}
		for ( var k = 0; k < BumpMath.Turn; k++ )
			Add( BumpMath.Sine[k] );
		for ( var k = 0; k < BounceRiders.SineTableSize; k++ )
			Add( BitConverter.SingleToInt32Bits( BounceRiders.SineAt( k ) ) );
		for ( var dx = -40; dx <= 40; dx++ )
			for ( var dy = -40; dy <= 40; dy++ )
			{
				Add( HeadingAngle.Of( dx, dy ) );
				var (x, y) = HeadingAngle.Facing( dx, dy );
				Add( BitConverter.SingleToInt32Bits( x ) );
				Add( BitConverter.SingleToInt32Bits( y ) );
			}
		Assert.AreEqual( "8198AB085CA1AAFC", hash.ToString( "X16" ) );
	}

	/// <summary>The port is faithful: within one unit in the last place of the platform's own function for every probe input, huge arguments included.</summary>
	[TestMethod]
	[TestCategory( PlatformLibrary )]
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

	/// <summary>Tests that compare DetMath with the runtime's own C library: informative in the browser check (tools/web-determinism), where that library is musl.</summary>
	public const string PlatformLibrary = "PlatformLibrary";

	/// <summary>
	/// The simulation's lookup tables, built at start-up from sines and arctangents as tp.exe builds them with x87 <c>fsin</c>/<c>fpatan</c>, come out the same with DetMath as
	/// with this platform's library (the tables the pins were made with), entry for entry; and the tables the game holds are those values.
	/// </summary>
	[TestMethod]
	[TestCategory( PlatformLibrary )]
	public void LookupTablesBuiltWithDetMathEqualThePlatformLibrarysTables()
	{
		void Same<T>( string name, int count, Func<Func<double, double>, Func<double, double>, int, T> entry )
		{
			for ( var index = 0; index < count; index++ )
				Assert.AreEqual( entry( Math.Sin, Math.Atan, index ), entry( DetMath.Sin, DetMath.Atan, index ), $"{name}[{index}]" );
		}
		var twoPi = (float)(2 * Math.PI);
		Same( "CoasterTrig", CoasterTrig.Size, ( sin, _, k ) => (float)sin( (float)((float)k / CoasterTrig.Size) * twoPi ) );
		Same( "CoasterTrig.Pc", CoasterTrig.Size, ( sin, _, k ) => (float)sin( (k / 4096.0) * (double)6.2831855f ) );
		Same( "OriginalSineTable", 4096, ( sin, _, k ) => (float)sin( k / 4096.0 * 6.2831855f ) );
		Same( "TourWorld", 4096, ( sin, _, k ) => (int)(0.5 - sin( k * 2 * 0.00076698974609375 ) * -1024.0) );
		Same( "BumpMath", BumpMath.Turn, ( sin, _, k ) => (int)(sin( k * 2 * 0.00613591796875 ) * 256.0) );
		Same( "PoolSlide", 17, ( sin, _, k ) => (int)Math.Round( sin( k * Math.PI / 32 ) * 1024 ) );
		Same( "BounceRiders", BounceRiders.SineTableSize, ( sin, _, k ) => (float)sin( (float)k / BounceRiders.SineTableSize * 6.2831854820251465 ) );
		Same( "HeadingAngle", 257, ( _, atan, k ) => (ushort)Math.Floor( atan( k / 256d ) * 1024d / Math.PI ) );
		for ( var k = 0; k < CoasterTrig.Size; k++ )
		{
			Assert.AreEqual( (float)Math.Sin( (float)((float)k / CoasterTrig.Size) * twoPi ), CoasterTrig.Entry( k ), $"CoasterTrig[{k}]" );
			Assert.AreEqual( (float)Math.Sin( (k / 4096.0) * (double)6.2831855f ), CoasterTrig.PcEntry( k ), $"CoasterTrig.Pc[{k}]" );
		}
		for ( var k = 0; k < 4096; k++ )
			Assert.AreEqual( (int)(0.5 - Math.Sin( k * 2 * 0.00076698974609375 ) * -1024.0), TourWorld.Sin( k ), $"TourWorld[{k}]" );
		for ( var k = 0; k < BumpMath.Turn; k++ )
			Assert.AreEqual( (int)(Math.Sin( k * 2 * 0.00613591796875 ) * 256.0), BumpMath.Sine[k], $"BumpMath[{k}]" );
		for ( var k = 0; k < BounceRiders.SineTableSize; k++ )
			Assert.AreEqual( (float)Math.Sin( (float)k / BounceRiders.SineTableSize * 6.2831854820251465 ), BounceRiders.SineAt( k ), $"BounceRiders[{k}]" );
	}

	/// <summary>A loan's instalment (<see cref="LoanMath.MonthlyRepayment"/>, DetMath.Pow) equals the platform's Math.Pow result for every offer the game can make.</summary>
	[TestMethod]
	[TestCategory( PlatformLibrary )]
	public void LoanInstalmentsWithDetMathEqualThePlatformLibrarys()
	{
		for ( var apr = 0; apr <= 60; apr++ )
			for ( var months = 1; months <= 120; months++ )
				foreach ( var amount in new long[] { 1000, 5000, 10000, 25000, 50000, 100000, 250000, 500000, 1000000 } )
				{
					var power = Math.Pow( 1.0 + apr / 100.0, months / 12.0 * 0.5 );
					var expected = LoanMath.TruncateUnsigned( amount * power / months );
					Assert.AreEqual( expected, LoanMath.MonthlyRepayment( amount, apr, months ), $"{amount} at {apr} % over {months} months" );
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
