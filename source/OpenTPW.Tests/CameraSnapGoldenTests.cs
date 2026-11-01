using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The loader's yaw snap against tp.exe's own tail of 0x42CEC0 (0x42D03F) run in the x86 oracle (docs/reverse/ORACLE.md, X49; SAVE-010): every row of <see cref="CameraSnapGolden"/> is a yaw, and
/// <see cref="ParkCameraMode.SnapToQuarterTurn"/> must give the same target bit for bit; the smoothed yaw (0x7909E0) keeps the saved value in every row.
/// </summary>
[TestClass]
public class CameraSnapGoldenTests
{
	private static string[] Rows()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( CameraSnapGolden.Packed ) ), CompressionMode.Decompress ) );
		return unpacked.ReadToEnd().Split( '\n', StringSplitOptions.RemoveEmptyEntries );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesYawSnapIsReproducedBitForBit()
	{
		var rows = Rows();
		Assert.AreEqual( CameraSnapGolden.RowCount, rows.Length );
		var mismatches = new List<string>();
		var moved = 0;
		foreach ( var row in rows )
		{
			var sides = row.Split( " > " );
			var yaw = BitConverter.UInt32BitsToSingle( Convert.ToUInt32( sides[0], 16 ) );
			var results = sides[1].Split( " | " );
			var expected = Convert.ToUInt32( results[0], 16 );
			var smoothed = Convert.ToUInt32( results[1], 16 );
			Assert.AreEqual( BitConverter.SingleToUInt32Bits( yaw ), smoothed, $"{row}: the smoothed yaw keeps the saved value" );
			var actual = BitConverter.SingleToUInt32Bits( ParkCameraMode.SnapToQuarterTurn( yaw ) );
			if ( actual != expected )
				mismatches.Add( $"{row}: {actual:x8}" );
			if ( expected != smoothed )
				moved++;
		}
		Assert.AreEqual( 0, mismatches.Count, string.Join( "\n", mismatches.Take( 8 ) ) );
		Assert.IsTrue( moved > rows.Length / 2, "most rows are turned" );
	}

	[TestMethod]
	public void ASavedYawIsSnappedOnlyInTheNinetyDegreeMode()
	{
		var camera = new SaveCamera( 110, 0.8f, 0, (475, 0, 175), (0, 0, 0), 0 );
		Assert.AreEqual( ParkCameraMode.YawOf( 0.8f ), ParkCameraMode.PoseOfSave( camera )!.Value.Yaw, "the smooth mode keeps the saved yaw" );
		Assert.AreEqual( ParkCameraMode.YawOf( MathF.PI / 2 ), ParkCameraMode.PoseOfSave( camera, ninetyDegrees: true )!.Value.Yaw, 1e-4f, "the 90 degree mode turns it to the quarter turn" );
	}
}
