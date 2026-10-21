using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>CPU row/half/channel contract only; this does not establish native GPU pixel parity.</summary>
[TestClass]
public class SignBoardOrientationTests
{
	[TestMethod]
	public void AsymmetricRawBoardPreservesEveryRowAndBothHalfSeams()
	{
		const int width = 256, height = 128, halfWidth = 128;
		var metadata = SignFileTests.CreateNativeMetadataSign( (0, "Test", "TEST____.TTF", 100, 0, -100), (0, "Test", "TEST____.TTF", 100, 128, -90) );
		BinaryPrimitives.WriteUInt32LittleEndian( metadata, 100 );
		metadata[8] = 1;
		var board = new byte[12 + width * height * 4];
		BinaryPrimitives.WriteUInt32LittleEndian( board, width );
		BinaryPrimitives.WriteUInt32LittleEndian( board.AsSpan( 4 ), height );
		BinaryPrimitives.WriteUInt32LittleEndian( board.AsSpan( 8 ), 4 );
		for ( var y = 0; y < height; y++ )
		for ( var x = 0; x < width; x++ )
		{
			var at = 12 + (y * width + x) * 4;
			board[at] = 255;
			board[at + 1] = (byte)(32 + x % halfWidth);
			board[at + 2] = (byte)(64 + y);
			board[at + 3] = (byte)(x < halfWidth ? 50 : 200);
		}
		var sign = new SignFile( metadata.Concat( board ).ToArray() );
		var result = SignSurfaceComposer.Compose( sign, SignFontLibrary.FromFiles( Array.Empty<(string, byte[])>() ), new string?[] { null, null }, new List<string>() );
		Assert.AreEqual( 0, result.TextPixelCount );
		foreach ( var half in new[] { 0, 1 } )
		{
			var expected = new byte[halfWidth * height * 4];
			for ( var y = 0; y < height; y++ )
			for ( var x = 0; x < halfWidth; x++ )
			{
				var at = (y * halfWidth + x) * 4;
				// The native alpha-blit divides by 256, not 255: opaque nonzero channels lose one.
				expected[at] = (byte)(31 + x);
				expected[at + 1] = (byte)(63 + y);
				expected[at + 2] = (byte)(half == 0 ? 49 : 199);
				expected[at + 3] = 255;
			}
			CollectionAssert.AreEqual( expected, half == 0 ? result.Left : result.Right, $"Half {half}: row reversal, horizontal reversal, half swap and channel permutation must remain detectable." );
		}
	}
}
