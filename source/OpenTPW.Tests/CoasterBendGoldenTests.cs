using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The bend car's tables against tp.exe's own 0x438AB0 run in the x86 oracle (docs/reverse/ORACLE.md, X43; COAST-034): every row of <see cref="CoasterBendGolden"/> is a synthetic sample list and car, and
/// <see cref="CoasterCarBend.Tables"/> must write the same rear count and the same table entries bit for bit.
/// </summary>
[TestClass]
public class CoasterBendGoldenTests
{
	private static string[] Rows()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( CoasterBendGolden.Packed ) ), CompressionMode.Decompress ) );
		return unpacked.ReadToEnd().Split( '\n', StringSplitOptions.RemoveEmptyEntries );
	}

	private static float[] Singles( string text ) =>
		text.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( item => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( item, 16 ) ) ).ToArray();

	[TestMethod]
	public void EveryRowOfTheOraclesBendTablesIsReproducedBitForBit()
	{
		var rows = Rows();
		Assert.AreEqual( CoasterBendGolden.RowCount, rows.Length );
		var mismatches = new List<string>();
		var wraps = 0;
		foreach ( var row in rows )
		{
			var sides = row.Split( " > " );
			var parts = sides[0].Split( " | " );
			Assert.AreEqual( "bend", parts[0] );
			var car = Singles( parts[1] );
			var direction = Singles( parts[2] );
			var coordinate = Singles( parts[3] )[0];
			var count = int.Parse( parts[4] );
			var fields = parts[5].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			var samples = new CoasterSample[count];
			for ( var index = 0; index < count; index++ )
			{
				var values = Singles( string.Join( ' ', fields.Skip( 6 * index + 1 ).Take( 5 ) ) );
				samples[index] = new CoasterSample { Flags = (CoasterSampleFlags)Convert.ToUInt32( fields[6 * index], 16 ), Position = new SVector3( values[0], values[1], values[2] ), Yaw = values[3], Pitch = values[4] };
			}
			var whole = Math.Min( (int)coordinate, count - 2 );
			if ( Math.Abs( samples[whole].Yaw - samples[whole + 1].Yaw ) > MathF.PI )
				wraps++;
			var tables = CoasterCarBend.Tables( samples, car[0], car[1], car[2], car[3], car[4], new SVector3( direction[0], direction[1], direction[2] ), coordinate );
			var outputs = sides[1].Split( " | " );
			var rear = int.Parse( outputs[0] );
			var expected = new[] { Singles( outputs[1] ), Singles( outputs[2] ), Singles( outputs[3] ) };
			Assert.AreEqual( rear, tables.Rear, row );
			Assert.AreEqual( expected[0].Length, tables.Count, row );
			var actual = new[] { tables.Yaw, tables.Pitch, tables.Elevation };
			for ( var table = 0; table < 3; table++ )
			{
				for ( var entry = 0; entry < expected[table].Length; entry++ )
				{
					if ( BitConverter.SingleToUInt32Bits( expected[table][entry] ) != BitConverter.SingleToUInt32Bits( actual[table][entry] ) )
						mismatches.Add( $"table {table} entry {entry}: expected {expected[table][entry]:R}, got {actual[table][entry]:R}" );
				}
			}
		}
		Assert.IsTrue( wraps >= 3, $"{wraps} rows take the angle lerp over the wrap" );
		Assert.AreEqual( 0, mismatches.Count, $"{mismatches.Count} entries differ, first: {string.Join( "; ", mismatches.Take( 6 ) )}" );
	}
}
