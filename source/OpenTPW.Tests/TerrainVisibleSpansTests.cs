using System;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The visible-cell spans of tp.exe 0x56BB00 (docs/LND.md): cameras from the x86 oracle's generator, the rows, the farthest corner distance and the span bytes the original's code gives. No game data is needed.
/// </summary>
[TestClass]
public class TerrainVisibleSpansTests
{
	[TestMethod]
	public void EveryCameraGivesTheRowsColumnsAndDistanceOfTheOriginal()
	{
		var spans = new TerrainVisibleSpans();
		foreach ( var (index, c) in TerrainVisibleSpansGolden.Cases.Select( ( value, index ) => (index, value) ) )
		{
			Assert.IsTrue( spans.Compute( c.Camera, c.Matrix, c.Low, c.High, c.Fov, c.Aspect, c.MaxDistance, c.Origin[0], c.Origin[1], c.Cells[0], c.Cells[1], c.Size[0], c.Size[1] ) );
			var bytes = new byte[256];
			for ( var row = 0; row < 128; row++ )
			{
				bytes[row] = (byte)spans.Start[row];
				bytes[128 + row] = (byte)spans.End[row];
			}
			Assert.AreEqual( c.MinRow, spans.MinRow, $"case {index} min row" );
			Assert.AreEqual( c.MaxRow, spans.MaxRow, $"case {index} max row" );
			Assert.AreEqual( c.FarBits, BitConverter.SingleToUInt32Bits( spans.FarthestCorner ), $"case {index} far" );
			Assert.AreEqual( c.Sha256, Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant(), $"case {index} spans" );
		}
	}

	[TestMethod]
	public void AFieldWiderThanTheTablesHasNoSpans()
	{
		var spans = new TerrainVisibleSpans();
		var c = TerrainVisibleSpansGolden.Cases[0];
		Assert.IsFalse( spans.Compute( c.Camera, c.Matrix, c.Low, c.High, c.Fov, c.Aspect, c.MaxDistance, 0, 0, 200, 85, 10, 10 ) );
		Assert.IsTrue( spans.IsEmpty );
	}
}
