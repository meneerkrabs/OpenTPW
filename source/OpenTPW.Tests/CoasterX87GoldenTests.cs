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

	private static string current = "";

	private static void Same( float[] expected, params float[] actual )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var i = 0; i < expected.Length; i++ )
			Assert.AreEqual( BitConverter.SingleToUInt32Bits( expected[i] ), BitConverter.SingleToUInt32Bits( actual[i] ), $"value {i}: expected {expected[i]:R}, got {actual[i]:R} in {current}" );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesCoasterRoutinesIsReproducedBitForBit()
	{
		var rows = Rows();
		Assert.AreEqual( CoasterX87Golden.RowCount, rows.Length );
		var kinds = new System.Collections.Generic.Dictionary<string, int>();
		foreach ( var row in rows )
		{
			current = row;
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
			var expected = parts[0] == "tangent" ? Array.Empty<float>() : Singles( sides[1] );
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
				case "tangent":
				{
					SVector3? Optional( string text ) => text == "-" ? null : Vector( text );
					var def = parts[5].Split( ' ' );
					var flags = parts[6].Split( ' ' );
					var scalars = Singles( parts[7] );
					var hasDefinition = def[0] == "d";
					var input = new CoasterNodeTangentInput( Vector( parts[1] ), Optional( parts[2] ), Optional( parts[3] ), Optional( parts[4] ), hasDefinition, hasDefinition && def[1] == "1",
						hasDefinition ? Singles( def[2] )[0] : 0f, flags[0] == "1", flags[1] == "1", flags[2] == "1", flags[3] == "1", scalars[0], scalars[1], flags[4] == "1" );
					var result = CoasterNodeTangent.Compute( input );
					if ( sides[1] == "none" )
					{
						Assert.IsNull( result, row );
						break;
					}
					var values = sides[1].Split( ' ' );
					var exp = Singles( string.Join( ' ', values.Take( 7 ) ) );
					Assert.IsNotNull( result, row );
					var r = result.Value;
					Same( exp, r.Back.X, r.Back.Y, r.Back.Z, r.Forward.X, r.Forward.Y, r.Forward.Z, r.Turn );
					Assert.AreEqual( values[7], r.TurnedBack.ToString().ToLowerInvariant(), row );
					break;
				}
				case "bank":
				{
					var settings = Singles( parts[4] );
					var position = Vector( parts[1] );
					Same( expected, CoasterTrack.AutoBank( settings[1], settings[0], CoasterX87.Length( Vector( parts[2] ) - position ), CoasterX87.Length( Vector( parts[3] ) - position ), settings[2] ) );
					break;
				}
				case "speed":
				{
					var v = Singles( parts[1] );
					Same( expected, CoasterX87.SpeedEstimate( v[0], v[1], v[2], v[3], v[4], v[5], v[6] ) );
					break;
				}
				case "loopknot":
				{
					SVector3? Optional( string text ) => text == "-" ? null : Vector( text );
					var settings = Singles( parts[1] );
					var chords = Singles( parts[4] );
					var previousParts = parts[5].Split( ' ' );
					var nextParts = parts[6].Split( ' ' );
					var previous = previousParts[0] == "-" ? (SVector3?)null : Vector( string.Join( ' ', previousParts.Take( 3 ) ) );
					var next = nextParts[0] == "-" ? (SVector3?)null : Vector( string.Join( ' ', nextParts.Take( 3 ) ) );
					var previousIsLoop = previous != null && previousParts[^1] == "1";
					var nextIsLoop = next != null && nextParts[^1] == "1";
					var reversed = parts[7] == "1";
					var position = Vector( parts[2] );
					var link = reversed ? next : previous;
					var fraction = CoasterX87.TurnFraction( settings[1] );
					Same( new[] { expected[0] }, fraction );
					var side = CoasterX87.LoopSide( fraction, link is { } l ? l.X - position.X : 0f, link is { } m ? m.Z - position.Z : 0f, link != null );
					var k = HermiteCoasterCurve.LoopKnotFactor( settings[0], side, settings[2] );
					var (back, forward) = HermiteCoasterCurve.SideFactors( true, reversed, previousIsLoop, nextIsLoop, k, settings[0] );
					var (b, f) = HermiteCoasterCurve.KnotControls( position, Vector( parts[3] ), chords[0], chords[1], back, forward );
					Same( expected.Skip( 1 ).ToArray(), b.X, b.Y, b.Z, f.X, f.Y, f.Z );
					break;
				}
				case "cost":
				{
					var v = Singles( parts[1] );
					var position = Singles( parts[2] );
					var other = Singles( parts[3] );
					var rates = Singles( parts[4] );
					var pylon = new CoasterPylonControl { MeshHeightLow = v[1], MeshHeightHigh = v[2], CostPerUnit = rates[0], CostPer2DLength = rates[1], CostPerHeight = rates[2] };
					Same( expected, CoasterTrack.PylonCost( pylon, CoasterTrack.CostLength( position[0] - other[0], position[1] - other[1] ), CoasterTrack.CostHeight( pylon, v[0] ), parts[5] == "1" ) );
					break;
				}
				default:
					Assert.Fail( "unknown row kind " + parts[0] );
					break;
			}
		}
		Assert.IsTrue( kinds.Count >= 10, string.Join( ", ", kinds ) );
	}
}
