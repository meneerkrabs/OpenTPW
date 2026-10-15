using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Packages;
using StbImageSharp;

namespace OpenTPW.Tests;

[TestClass]
public class PngImageEncoderTests
{
	[TestMethod]
	public void WrappedRgbaWidthCannotBecomeAHugeHeaderWithATinyScanline()
	{
		// Both the old shape check and row stride wrap to four bytes; the call stays bounded.
		Assert.ThrowsException<ArgumentException>( () => PngImage.EncodeRgba( 0x40000001, 1, new byte[4] ) );
	}

	[DataTestMethod]
	[DataRow( 0, 1, 0 )]
	[DataRow( 1, 0, 0 )]
	[DataRow( int.MinValue, 1, 4 )]
	[DataRow( 1, int.MinValue, 4 )]
	[DataRow( 1, 1, 3 )]
	[DataRow( 2, 1, 4 )]
	[DataRow( 1, 1, 8 )]
	[DataRow( int.MaxValue, 1, 4 )]
	[DataRow( 1, int.MaxValue, 4 )]
	[DataRow( int.MaxValue, int.MaxValue, 4 )]
	[DataRow( 0x40000000, 1, 0 )]
	[DataRow( 0x40000001, 2, 8 )]
	public void InvalidOrUnrepresentableShapesRejectBeforeEncoding( int width, int height, int length )
	{
		Assert.ThrowsException<ArgumentException>( () => PngImage.EncodeRgba( width, height, new byte[length] ) );
	}

	[DataTestMethod]
	[DataRow( 1, 1 )]
	[DataRow( 3, 2 )]
	[DataRow( 257, 17 )]
	[DataRow( 1, 257 )]
	[DataRow( 4097, 1 )]
	public void ValidShapesPreserveEveryDecodedRgbaByte( int width, int height )
	{
		var pixels = new byte[width * height * 4];
		for ( var pixel = 0; pixel < pixels.Length / 4; pixel++ )
		{
			pixels[pixel * 4] = (byte)(pixel * 17);
			pixels[pixel * 4 + 1] = (byte)(pixel * 31);
			pixels[pixel * 4 + 2] = (byte)(255 - pixel % 256);
			pixels[pixel * 4 + 3] = (byte)pixel;
		}
		var png = PngImage.EncodeRgba( width, height, pixels );
		Assert.AreEqual( (width, height), PngImage.Validate( png, Math.Max( width, height ) ) );
		var decoded = ImageResult.FromMemory( png, ColorComponents.RedGreenBlueAlpha );
		Assert.AreEqual( width, decoded.Width );
		Assert.AreEqual( height, decoded.Height );
		CollectionAssert.AreEqual( pixels, decoded.Data );
		CollectionAssert.AreEqual( png, PngImage.EncodeRgba( width, height, pixels ), "Identical valid input must retain deterministic encoding." );
	}
}
