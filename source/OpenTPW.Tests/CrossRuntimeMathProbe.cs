using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The elementary functions over fixed inputs, reduced to one digest per function (docs/DETERMINISM.md, "Browser and native"). The
/// platform's <see cref="Math"/> and <see cref="MathF"/> call the C library of the runtime (MSVC, glibc, Apple's libm, musl in the
/// browser), so their digests may differ between runtimes; <see cref="DetMath"/>'s may not. The browser determinism check
/// (tools/web-determinism) reports both, so a change of the libraries shows which functions differ and in how many inputs.
/// </summary>
public static class CrossRuntimeMathProbe
{
	/// <summary>Inputs per function.</summary>
	public const int Count = 20000;

	/// <summary>SplitMix64: a fixed input stream that every runtime generates alike.</summary>
	private static IEnumerable<ulong> Stream( ulong seed )
	{
		var state = seed;
		while ( true )
		{
			state += 0x9E3779B97F4A7C15UL;
			var z = state;
			z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
			yield return z ^ (z >> 31);
		}
	}

	/// <summary>Uniform doubles in [low, high), from the top 53 bits of each word.</summary>
	public static double[] Uniform( ulong seed, double low, double high ) =>
		Stream( seed ).Take( Count ).Select( word => low + (high - low) * ((word >> 11) * (1.0 / 9007199254740992.0)) ).ToArray();

	/// <summary>Angles as the simulation passes them: mostly within a few turns, some far out (table builders, accumulated headings), and the edges.</summary>
	public static double[] Angles() => Uniform( 1, -4 * Math.PI, 4 * Math.PI ).Take( Count - 2000 )
		.Concat( Uniform( 2, -1e6, 1e6 ).Take( 1000 ) ).Concat( Uniform( 3, -1e300, 1e300 ).Take( 990 ) )
		.Concat( new[] { 0.0, -0.0, Math.PI / 2, Math.PI, 2 * Math.PI, 1e-300, 3 * Math.PI / 2, 1e22, double.PositiveInfinity, double.NaN } ).ToArray();

	private static ulong Bits( double value ) => double.IsNaN( value ) ? 0x7FF8000000000000UL : BitConverter.DoubleToUInt64Bits( value );
	private static ulong Bits( float value ) => float.IsNaN( value ) ? 0x7FC00000UL : BitConverter.SingleToUInt32Bits( value );

	private static ulong Digest( IEnumerable<ulong> values )
	{
		var hash = 14695981039346656037UL;
		foreach ( var value in values )
			for ( var shift = 0; shift < 64; shift += 8 )
				hash = (hash ^ ((value >> shift) & 0xFF)) * 1099511628211UL;
		return hash;
	}

	/// <summary>Every probed function: name → the results' bit patterns (NaN canonical).</summary>
	public static IReadOnlyList<(string Name, ulong[] Bits)> Results()
	{
		var angles = Angles();
		var unit = Uniform( 4, -1, 1 ).Concat( new[] { 1.0, -1.0, 0.5, -0.5, 1.0000001 } ).ToArray();
		var ratios = Uniform( 5, -50, 50 ).Concat( Uniform( 6, -1e-3, 1e-3 ) ).Take( Count ).ToArray();
		var ys = Uniform( 7, -1000, 1000 );
		var xs = Uniform( 8, -1000, 1000 ).Select( ( x, index ) => index % 50 == 0 ? 0.0 : x ).ToArray();
		var positives = Uniform( 9, 0, 64 ).Concat( Uniform( 10, 1e-6, 1 ) ).Take( Count ).ToArray();
		var bases = Uniform( 11, 1, 2.5 );
		var exponents = Uniform( 12, -10, 10 );
		var fmaA = Uniform( 13, -100, 100 );
		var fmaB = Uniform( 14, -100, 100 );
		var fmaC = Uniform( 15, -100, 100 );
		ulong[] D( IEnumerable<double> values ) => values.Select( Bits ).ToArray();
		var cells = Uniform( 16, -100, 100 ).Select( value => (float)value ).ToArray();
		var fractions = Uniform( 17, 0, 1 ).Select( value => (float)value ).ToArray();
		System.Numerics.Matrix4x4 MatrixAt( int index ) => new(
			cells[index % Count], cells[(index + 1) % Count], cells[(index + 2) % Count], cells[(index + 3) % Count],
			cells[(index + 4) % Count], cells[(index + 5) % Count], cells[(index + 6) % Count], cells[(index + 7) % Count],
			cells[(index + 8) % Count], cells[(index + 9) % Count], cells[(index + 10) % Count], cells[(index + 11) % Count],
			cells[(index + 12) % Count], cells[(index + 13) % Count], cells[(index + 14) % Count], cells[(index + 15) % Count] );
		System.Numerics.Vector3 VectorAt( int index ) => new( cells[index % Count], cells[(index + 7) % Count], cells[(index + 13) % Count] );
		System.Numerics.Quaternion QuaternionAt( int index ) => System.Numerics.Quaternion.Normalize( new( cells[index % Count], cells[(index + 3) % Count], cells[(index + 5) % Count], cells[(index + 11) % Count] ) );
		IEnumerable<float> Elements( System.Numerics.Matrix4x4 m ) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
		IEnumerable<float> Components( System.Numerics.Vector3 v ) => new[] { v.X, v.Y, v.Z };
		IEnumerable<float> QuaternionComponents( System.Numerics.Quaternion q ) => new[] { q.X, q.Y, q.Z, q.W };
		var products = Enumerable.Range( 0, Count / 16 ).Select( index => (MatrixAt( index * 16 ), MatrixAt( index * 16 + 8 )) ).ToArray();
		var points = Enumerable.Range( 0, Count / 4 ).Select( index => (VectorAt( index ), MatrixAt( index * 3 )) ).ToArray();
		var blends = Enumerable.Range( 0, Count / 4 ).Select( index => (VectorAt( index ), VectorAt( index + 101 ), fractions[index]) ).ToArray();
		var turns = Enumerable.Range( 0, Count / 4 ).Select( index => (QuaternionAt( index ), QuaternionAt( index + 37 ), fractions[index]) ).ToArray();
		ulong[] F( IEnumerable<float> values ) => values.Select( Bits ).ToArray();
		var pairs = ys.Zip( xs ).ToArray();
		var powPairs = bases.Zip( exponents ).ToArray();
		float[] Single( double[] values ) => values.Select( value => (float)value ).ToArray();
		return new List<(string, ulong[])>
		{
			("Math.Sin", D( angles.Select( Math.Sin ) )),
			("Math.Cos", D( angles.Select( Math.Cos ) )),
			("Math.Atan", D( ratios.Select( Math.Atan ) )),
			("Math.Atan2", D( pairs.Select( pair => Math.Atan2( pair.First, pair.Second ) ) )),
			("Math.Acos", D( unit.Select( Math.Acos ) )),
			("Math.Pow", D( powPairs.Select( pair => Math.Pow( pair.First, pair.Second ) ) )),
			("Math.Log2", D( positives.Select( Math.Log2 ) )),
			("Math.Sqrt", D( positives.Select( Math.Sqrt ) )),
			("Math.FusedMultiplyAdd", D( fmaA.Select( ( a, index ) => Math.FusedMultiplyAdd( a, fmaB[index], fmaC[index] ) ) )),
			("MathF.Sin", F( Single( angles ).Select( MathF.Sin ) )),
			("MathF.Cos", F( Single( angles ).Select( MathF.Cos ) )),
			("MathF.Atan2", F( pairs.Select( pair => MathF.Atan2( (float)pair.First, (float)pair.Second ) ) )),
			("MathF.Acos", F( Single( unit ).Select( MathF.Acos ) )),
			("MathF.Sqrt", F( Single( positives ).Select( MathF.Sqrt ) )),
			("MathF.FusedMultiplyAdd", F( fmaA.Select( ( a, index ) => MathF.FusedMultiplyAdd( (float)a, (float)fmaB[index], (float)fmaC[index] ) ) )),
			("DetMath.Sin", D( angles.Select( DetMath.Sin ) )),
			("DetMath.Cos", D( angles.Select( DetMath.Cos ) )),
			("DetMath.Atan", D( ratios.Select( DetMath.Atan ) )),
			("DetMath.Atan2", D( pairs.Select( pair => DetMath.Atan2( pair.First, pair.Second ) ) )),
			("DetMath.Acos", D( unit.Select( DetMath.Acos ) )),
			("DetMath.Pow", D( powPairs.Select( pair => DetMath.Pow( pair.First, pair.Second ) ) )),
			("DetMath.Log2", D( positives.Select( DetMath.Log2 ) )),
			("DetMath.SinF", F( Single( angles ).Select( DetMathF.Sin ) )),
			("DetMath.CosF", F( Single( angles ).Select( DetMathF.Cos ) )),
			("DetMath.Atan2F", F( pairs.Select( pair => DetMathF.Atan2( (float)pair.First, (float)pair.Second ) ) )),
			("DetMath.AcosF", F( Single( unit ).Select( DetMathF.Acos ) )),
			("Matrix4x4.Multiply", F( products.SelectMany( pair => Elements( pair.Item1 * pair.Item2 ) ) )),
			("Vector3.Transform", F( points.SelectMany( pair => Components( System.Numerics.Vector3.Transform( pair.Item1, pair.Item2 ) ) ) )),
			("Vector3.Lerp", F( blends.SelectMany( blend => Components( System.Numerics.Vector3.Lerp( blend.Item1, blend.Item2, blend.Item3 ) ) ) )),
			("DetMatrix.Multiply", F( products.SelectMany( pair => Elements( DetMatrix.Multiply( pair.Item1, pair.Item2 ) ) ) )),
			("DetMatrix.TransformPosition", F( points.SelectMany( pair => Components( DetMatrix.TransformPosition( pair.Item1, pair.Item2 ) ) ) )),
			("DetMatrix.Lerp", F( blends.SelectMany( blend => Components( DetMatrix.Lerp( blend.Item1, blend.Item2, blend.Item3 ) ) ) )),
			("DetMatrix.Slerp", F( turns.SelectMany( turn => QuaternionComponents( DetMatrix.Slerp( turn.Item1, turn.Item2, turn.Item3 ) ) ) )),
		};
	}

	/// <summary>name → digest (hex), and for each platform function the number of inputs where it differs from the DetMath counterpart.</summary>
	public static IReadOnlyDictionary<string, string> Digests()
	{
		var results = Results();
		var byName = results.ToDictionary( result => result.Name, result => result.Bits );
		var digests = new SortedDictionary<string, string>( StringComparer.Ordinal );
		foreach ( var (name, bits) in results )
			digests[name] = Digest( bits ).ToString( "X16" );
		foreach ( var (platform, deterministic) in new[] { ("Math.Sin", "DetMath.Sin"), ("Math.Cos", "DetMath.Cos"), ("Math.Atan", "DetMath.Atan"), ("Math.Atan2", "DetMath.Atan2"),
			("Math.Acos", "DetMath.Acos"), ("Math.Pow", "DetMath.Pow"), ("Math.Log2", "DetMath.Log2"), ("MathF.Sin", "DetMath.SinF"), ("MathF.Cos", "DetMath.CosF"),
			("MathF.Atan2", "DetMath.Atan2F"), ("MathF.Acos", "DetMath.AcosF") } )
			digests[$"{platform} != {deterministic}"] = byName[platform].Zip( byName[deterministic] ).Count( pair => pair.First != pair.Second ).ToString();
		return digests;
	}

	public static string DigestsJson()
	{
		var result = new JsonObject();
		foreach ( var (name, digest) in Digests() )
			result[name] = digest;
		return result.ToJsonString();
	}
}
