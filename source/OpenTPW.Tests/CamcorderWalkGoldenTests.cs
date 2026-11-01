using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The camcorder walk against tp.exe's own camera update 0x42B1C0 with the camera flags 2, run in the x86 oracle (docs/reverse/ORACLE.md, X58; UI-042): every row of <see cref="CamcorderWalkGolden"/> is one
/// frame of a synthetic park (the generator names its terrain, walk test and ride entrances), and <see cref="CamcorderWalk.Step"/> must leave the same position, yaw, velocity, look, eye height and
/// ride, and ask the walk test the same questions in the same order, bit for bit.
/// </summary>
[TestClass]
public class CamcorderWalkGoldenTests
{
	private static string[] Rows()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( CamcorderWalkGolden.Packed ) ), CompressionMode.Decompress ) );
		return unpacked.ReadToEnd().Split( '\n', StringSplitOptions.RemoveEmptyEntries );
	}

	private static float F( string hex ) => BitConverter.UInt32BitsToSingle( uint.Parse( hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture ) );
	private static string H( float value ) => BitConverter.SingleToUInt32Bits( value ).ToString( "x8" );

	/// <summary>The generator's synthetic ground: heights ((7i + 13j) mod 11) / 2, anti-diagonal cells where (5i + 3j) mod 7 is 0.</summary>
	internal static ModelHeightfield SyntheticField()
	{
		const int cells = 128;
		var heights = new float[(cells + 1) * (cells + 1)];
		for ( var j = 0; j <= cells; j++ )
			for ( var i = 0; i <= cells; i++ )
				heights[j * (cells + 1) + i] = ((i * 7 + j * 13) % 11) * 0.5f;
		var words = new uint[cells * cells];
		for ( var j = 0; j < cells; j++ )
			for ( var i = 0; i < cells; i++ )
				words[j * cells + i] = (i * 5 + j * 3) % 7 == 0 ? ModelHeightfield.AntiDiagonalFlag : 0;
		return new ModelHeightfield( cells, cells, 10f, 10f, 0, 0, Array.Empty<uint>(), heights, words );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesWalkFrameIsReproducedBitForBit()
	{
		var rows = Rows();
		Assert.AreEqual( CamcorderWalkGolden.RowCount, rows.Length );
		var field = SyntheticField();
		var mismatches = new List<string>();
		var crossed = 0;
		var rides = 0;
		var refused = 0;
		foreach ( var row in rows )
		{
			var sides = row.Split( " > " );
			var given = sides[0].Split( ' ' );
			var seed = uint.Parse( given[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture );
			var mask = int.Parse( given[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture );
			// the pointer in the generator's 1024 x 768 reference: the offsets as 0x42B567 computes them (single precision, no clamp)
			var pointerX = (F( given[2] ) - 512f) / 512f;
			var pointerY = -((F( given[3] ) - 384f) / 384f);
			var calls = new List<string>();
			(int X, int Y)? entered = null;
			var world = new CamcorderWalkWorld( field, ( x, z, direction ) =>
			{
				var byteX = x & 0xFF;
				var byteZ = z & 0xFF;
				var edge = (byteX == 0 && direction == 3) || (byteZ == 0 && direction == 0) || (byteX == 0x7F && direction == 1) || (byteZ == 0x7F && direction == 2);
				var hash = unchecked((uint)byteX * 73856093u ^ (uint)byteZ * 19349663u ^ (uint)direction * 83492791u ^ seed);
				var result = edge || (hash & 7) == 0;
				calls.Add( $"{byteX},{byteZ},{direction},{(result ? 1 : 0)}" );
				return result;
			}, ( x, z ) =>
			{
				if ( (x * 31 + z * 17 + (int)seed) % 29 != 0 )
					return null;
				entered = (x, z);
				return new object();
			} );
			var walk = new CamcorderWalk { X = F( given[4] ), Z = F( given[5] ), Yaw = F( given[6] ), VelocityX = F( given[7] ), VelocityZ = F( given[8] ) };
			var input = new CamcorderWalkInput
			{
				TurnLeft = (mask & 1) != 0, TurnRight = (mask & 2) != 0, Forward = (mask & 4) != 0, Backward = (mask & 8) != 0, Button = (mask & 16) != 0,
				PointerX = pointerX, PointerY = pointerY
			};
			var ride = walk.Step( F( given[10] ), input, F( given[9] ), world );
			var expected = sides[1].Split( ' ' );
			var actual = new[] { H( walk.X ), H( walk.Z ), H( walk.Yaw ), H( walk.VelocityX ), H( walk.VelocityZ ), H( walk.Pitch ), H( walk.TurnRate ), H( walk.EyeY ) };
			for ( var index = 0; index < actual.Length; index++ )
				if ( actual[index] != expected[index] )
					mismatches.Add( $"{row}: field {index} is {actual[index]}" );
			var (forwardX, forwardY, forwardZ) = walk.Forward;
			for ( var index = 0; index < 3; index++ )
			{
				var oracle = F( expected[8 + index] );
				var ours = index == 0 ? forwardX : index == 1 ? forwardY : forwardZ;
				if ( MathF.Abs( oracle - ours ) > 2e-6f )
					mismatches.Add( $"{row}: forward {index} is {ours}" );
			}
			var expectedRide = expected[11];
			var actualRide = ride == null ? "-" : $"{entered!.Value.X},{entered.Value.Y}";
			if ( expectedRide != actualRide )
				mismatches.Add( $"{row}: ride {actualRide}" );
			var expectedCalls = expected[12];
			var actualCalls = calls.Count == 0 ? "-" : string.Join( ";", calls );
			if ( expectedCalls != actualCalls )
				mismatches.Add( $"{row}: walk test calls {actualCalls}" );
			crossed += calls.Count > 0 ? 1 : 0;
			rides += ride != null ? 1 : 0;
			refused += calls.Any( call => call.EndsWith( ",1" ) ) ? 1 : 0;
		}
		Assert.AreEqual( 0, mismatches.Count, mismatches.Count + " mismatches\n" + string.Join( "\n", mismatches.Take( 8 ) ) );
		Assert.IsTrue( crossed > 100, $"{crossed} rows cross a cell edge" );
		Assert.IsTrue( refused > 20, $"{refused} rows meet a refused edge" );
		Assert.IsTrue( rides > 5, $"{rides} rows end in a ride's entrance" );
	}
}
