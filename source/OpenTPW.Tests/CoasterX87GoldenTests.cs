using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The coaster arithmetic against tp.exe's own routines run in the x86 oracle at the thread's control word 0x7F (docs/reverse/ORACLE.md, X40; COAST-032, COAST-014): every row of
/// <see cref="CoasterX87Golden"/> is a synthetic input with the singles the real routine stored, and the C# port must reproduce every bit.
/// </summary>
[TestClass]
public class CoasterX87GoldenTests
{
	private static string[] Rows()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( CoasterX87Golden.Packed ) ), CompressionMode.Decompress ) );
		return unpacked.ReadToEnd().Split( '\n', StringSplitOptions.RemoveEmptyEntries );
	}

	private static float[] Singles( string text ) =>
		text.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( item => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( item, 16 ) ) ).ToArray();

	private static SVector3 Vector( string text )
	{
		var v = Singles( text );
		return new SVector3( v[0], v[1], v[2] );
	}

	private static void Same( float[] expected, params float[] actual )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var i = 0; i < expected.Length; i++ )
			Assert.AreEqual( BitConverter.SingleToUInt32Bits( expected[i] ), BitConverter.SingleToUInt32Bits( actual[i] ), $"value {i}: expected {expected[i]:R}, got {actual[i]:R}" );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesCoasterRoutinesIsReproducedBitForBit()
	{
		var rows = Rows();
		Assert.AreEqual( CoasterX87Golden.RowCount, rows.Length );
		var kinds = new System.Collections.Generic.Dictionary<string, int>();
		foreach ( var row in rows )
		{
			var sides = row.Split( " > " );
			var parts = sides[0].Split( " | " );
			if ( parts[0] == "table" )
			{
				// the sine table 0x4708D0 builds: 4096 singles from +0x7B0968 (the row's text is its SHA-256)
				var bytes = new byte[4 * CoasterTrig.Size];
				for ( var k = 0; k < CoasterTrig.Size; k++ )
					BitConverter.TryWriteBytes( bytes.AsSpan( 4 * k ), CoasterTrig.PcEntry( k ) );
				Assert.AreEqual( parts[1].ToLowerInvariant(), Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( bytes ) ).ToLowerInvariant() );
				kinds["table"] = 1;
				continue;
			}
			var expected = Singles( sides[1] );
			kinds[parts[0]] = (kinds.TryGetValue( parts[0], out var seen ) ? seen : 0) + 1;
			switch ( parts[0] )
			{
				case "point":
				{
					var t = Singles( parts[5] )[0];
					var q = HermiteCoasterCurve.Bezier( Vector( parts[1] ), Vector( parts[2] ), Vector( parts[3] ), Vector( parts[4] ), t );
					Same( expected, q.X, q.Y, q.Z );
					break;
				}
				case "derivative":
				{
					var t = Singles( parts[5] )[0];
					var q = HermiteCoasterCurve.Derivative( Vector( parts[1] ), Vector( parts[2] ), Vector( parts[3] ), Vector( parts[4] ), t );
					Same( expected, q.X, q.Y, q.Z );
					break;
				}
				case "chord":
					Same( expected, HermiteCoasterCurve.Chord( Vector( parts[1] ), Vector( parts[2] ) ), HermiteCoasterCurve.Chord( Vector( parts[2] ), Vector( parts[3] ) ) );
					break;
				case "knot":
				{
					var chords = Singles( parts[4] );
					var curviness = Singles( parts[1] )[0];
					var (back, forward) = HermiteCoasterCurve.KnotControls( Vector( parts[2] ), Vector( parts[3] ), chords[0], chords[1], curviness, curviness );
					Same( expected, back.X, back.Y, back.Z, forward.X, forward.Y, forward.Z );
					break;
				}
				case "orient":
				{
					var (yaw, pitch) = CoasterTrack.PcOrientation( Vector( parts[1] ) );
					Same( expected, yaw, pitch );
					break;
				}
				default:
					Assert.Fail( "unknown row kind " + parts[0] );
					break;
			}
		}
		Assert.IsTrue( kinds.Count >= 5, string.Join( ", ", kinds ) );
	}
}
