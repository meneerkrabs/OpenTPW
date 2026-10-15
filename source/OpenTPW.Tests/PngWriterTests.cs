using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StbImageSharp;

namespace OpenTPW.Tests;

[TestClass]
public class PngWriterTests
{
	// 3x2 RGBA pixels with distinct channels and partial alpha.
	private static readonly byte[] Rgba =
	{
		255, 0, 0, 255,    0, 255, 0, 128,    0, 0, 255, 0,
		10, 20, 30, 40,    200, 150, 100, 255,  1, 2, 3, 4,
	};

	[TestMethod]
	public void RgbaRoundTripsThroughAnIndependentDecoder()
	{
		using var stream = new MemoryStream();
		PngWriter.Write( stream, Rgba, 3, 2 );
		var image = ImageResult.FromMemory( stream.ToArray(), ColorComponents.RedGreenBlueAlpha );
		Assert.AreEqual( 3, image.Width );
		Assert.AreEqual( 2, image.Height );
		Assert.AreEqual( ColorComponents.RedGreenBlueAlpha, image.SourceComp );
		CollectionAssert.AreEqual( Rgba, image.Data );
	}

	[TestMethod]
	public void BgraInputIsStoredAsRgba()
	{
		var bgra = (byte[])Rgba.Clone();
		for ( var i = 0; i < bgra.Length; i += 4 )
			(bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-png-{Guid.NewGuid():N}.png" );
		try
		{
			PngWriter.SaveBgra( path, bgra, 3, 2 );
			var image = ImageResult.FromMemory( File.ReadAllBytes( path ), ColorComponents.RedGreenBlueAlpha );
			CollectionAssert.AreEqual( Rgba, image.Data );
		}
		finally
		{
			File.Delete( path );
		}
	}

	[TestMethod]
	public void ChunksCarryValidLengthsAndCrcs()
	{
		using var stream = new MemoryStream();
		PngWriter.Write( stream, Rgba, 3, 2 );
		var png = stream.ToArray();
		CollectionAssert.AreEqual( new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8] );

		var types = new System.Collections.Generic.List<string>();
		var offset = 8;
		while ( offset < png.Length )
		{
			var length = BinaryPrimitives.ReadInt32BigEndian( png.AsSpan( offset ) );
			var typeAndData = png.AsSpan( offset + 4, 4 + length );
			types.Add( System.Text.Encoding.ASCII.GetString( typeAndData[..4] ) );
			var crc = BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( offset + 8 + length ) );
			Assert.AreEqual( ReferenceCrc( typeAndData ), crc, types[^1] );
			offset += 12 + length;
		}
		Assert.AreEqual( png.Length, offset );
		CollectionAssert.AreEqual( new[] { "IHDR", "IDAT", "IEND" }, types );
	}

	[TestMethod]
	public void ImageDataUsesFilterNoneScanlines()
	{
		using var stream = new MemoryStream();
		PngWriter.Write( stream, Rgba, 3, 2 );
		var png = stream.ToArray();
		var idatLength = BinaryPrimitives.ReadInt32BigEndian( png.AsSpan( 33 ) );
		using var zlib = new ZLibStream( new MemoryStream( png, 41, idatLength ), CompressionMode.Decompress );
		using var raw = new MemoryStream();
		zlib.CopyTo( raw );
		var scanlines = raw.ToArray();
		Assert.AreEqual( 2 * (1 + 3 * 4), scanlines.Length );
		Assert.AreEqual( 0, scanlines[0] );
		Assert.AreEqual( 0, scanlines[13] );
		CollectionAssert.AreEqual( Rgba[..12], scanlines[1..13] );
	}

	[TestMethod]
	public void RejectsMismatchedBuffers()
	{
		using var stream = new MemoryStream();
		Assert.ThrowsException<ArgumentException>( () => PngWriter.Write( stream, new byte[10], 3, 2 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PngWriter.Write( stream, Array.Empty<byte>(), 0, 2 ) );
		Assert.ThrowsException<ArgumentException>( () => RgbaResampler.Resize( new byte[10], 3, 2, 1, 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => RgbaResampler.Resize( Rgba, 3, 2, 0, 1 ) );
	}

	[TestMethod]
	public void DownscaleAveragesEachCell()
	{
		// 4x2 opaque image: left 2x2 block red/blue, right 2x2 block uniform grey.
		var pixels = new byte[]
		{
			255, 0, 0, 255,   0, 0, 255, 255,   90, 90, 90, 255,   90, 90, 90, 255,
			255, 0, 0, 255,   0, 0, 255, 255,   90, 90, 90, 255,   90, 90, 90, 255,
		};
		var result = RgbaResampler.Resize( pixels, 4, 2, 2, 1 );
		CollectionAssert.AreEqual( new byte[] { 128, 0, 128, 255, 90, 90, 90, 255 }, result );
	}

	[TestMethod]
	public void TransparentTexelsDoNotDarkenColour()
	{
		// One opaque white texel next to three transparent black texels.
		var pixels = new byte[]
		{
			255, 255, 255, 255,   0, 0, 0, 0,
			0, 0, 0, 0,           0, 0, 0, 0,
		};
		CollectionAssert.AreEqual( new byte[] { 255, 255, 255, 64 }, RgbaResampler.Resize( pixels, 2, 2, 1, 1 ) );
	}

	[TestMethod]
	public void UpscaleRepeatsSourcePixels()
	{
		var pixels = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
		var result = RgbaResampler.Resize( pixels, 2, 1, 4, 2 );
		var row = new byte[] { 1, 2, 3, 4, 1, 2, 3, 4, 5, 6, 7, 8, 5, 6, 7, 8 };
		CollectionAssert.AreEqual( row.Concat( row ).ToArray(), result );
	}

	private static uint ReferenceCrc( ReadOnlySpan<byte> data )
	{
		var crc = 0xFFFFFFFFu;
		foreach ( var value in data )
		{
			crc ^= value;
			for ( var bit = 0; bit < 8; ++bit )
				crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
		}
		return ~crc;
	}
}
