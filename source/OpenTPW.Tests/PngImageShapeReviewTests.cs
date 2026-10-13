using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Packages;
using StbImageSharp;

namespace OpenTPW.Tests;

[TestClass]
public class PngImageShapeReviewTests
{
	[DataTestMethod]
	[DataRow( 0x40000002, 8 )]
	[DataRow( 0x40000003, 12 )]
	public void WrappedWidthsRejectOtherAlignedTinyBuffers( int width, int bytes ) =>
		Assert.ThrowsException<ArgumentException>( () => PngImage.EncodeRgba( width, 1, new byte[bytes] ) );

	[DataTestMethod]
	[DataRow( 7, 3 )]
	[DataRow( 1, 65537 )]
	public void SlicedInputAndTallRowsPreserveExactPixelsWithoutReadingSurroundingBytes( int width, int height )
	{
		var length = checked(width * height * 4);
		var backing = new byte[length + 29];
		Array.Fill( backing, (byte)0xA5 );
		var expected = new byte[length];
		for ( var index = 0; index < expected.Length; index++ ) expected[index] = (byte)(index * 37 + index / 4);
		expected.CopyTo( backing, 13 );
		var png = PngImage.EncodeRgba( width, height, backing.AsSpan( 13, length ) );
		Assert.AreEqual( (width, height), PngImage.Validate( png, Math.Max( width, height ) ) );
		var decoded = ImageResult.FromMemory( png, ColorComponents.RedGreenBlueAlpha );
		Assert.AreEqual( width, decoded.Width ); Assert.AreEqual( height, decoded.Height );
		CollectionAssert.AreEqual( expected, decoded.Data );
		CollectionAssert.AreEqual( png, PngImage.EncodeRgba( width, height, expected ) );
		Assert.AreEqual( (byte)0xA5, backing[12] );
		Assert.AreEqual( (byte)0xA5, backing[13 + length] );
	}
}
