using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SignWaveletDecoderTests
{
	[TestMethod]
	public void PlanarCoefficientHalvesReconstructUniformAlpha()
	{
		var alpha = new byte[8 * 8];
		for ( var row = 0; row < 4; row++ )
		for ( var column = 0; column < 4; column++ )
			alpha[row * 8 + column] = 100;
		var color = new byte[8 * 8 + 4 * 4 * 2];
		var colorChunk = Bilz( color );
		var alphaChunk = Bilz( alpha );
		var payload = new byte[24 + colorChunk.Length + alphaChunk.Length];
		for ( var i = 0; i < 4; i++ ) BinaryPrimitives.WriteSingleLittleEndian( payload.AsSpan( i * 4 ), 1 );
		BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( 16 ), (uint)colorChunk.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( 20 ), (uint)alphaChunk.Length );
		colorChunk.CopyTo( payload, 24 );
		alphaChunk.CopyTo( payload, 24 + colorChunk.Length );

		var pixels = SignWaveletDecoder.Decode( new SignBitmap( 8, 8, 4, payload ) { IsWavelet = true } );
		for ( var at = 0; at < pixels.Length; at += 4 )
			Assert.AreEqual( 50, pixels[at], $"alpha at pixel {at / 4}" );
	}

	private static byte[] Bilz( byte[] uncompressed )
	{
		using var compressed = new MemoryStream();
		using ( var zlib = new ZLibStream( compressed, CompressionLevel.Optimal, leaveOpen: true ) )
			zlib.Write( uncompressed );
		var chunk = new byte[28 + compressed.Length];
		"BILZ"u8.CopyTo( chunk );
		BinaryPrimitives.WriteUInt32LittleEndian( chunk.AsSpan( 4 ), (uint)uncompressed.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( chunk.AsSpan( 8 ), (uint)chunk.Length );
		compressed.ToArray().CopyTo( chunk, 28 );
		return chunk;
	}

	[TestMethod]
	public void EveryOriginalExtraImageDecodesToNonemptyArgb()
	{
		var signs = SignFileTests.OriginalSignMembers( StringTableTests.OriginalDataDirectory() )
			.Select( member => (member.Path, Sign: new SignFile( member.Data )) )
			.Where( entry => entry.Sign.ExtraImage is { IsWavelet: true } ).ToArray();
		Assert.AreEqual( 23, signs.Length );
		CollectionAssert.AreEquivalent( new[] { "fantasy", "hallow", "jungle", "space" },
			signs.Where( entry => entry.Path.StartsWith( "levels/", StringComparison.Ordinal ) )
				.Select( entry => entry.Path.Split( '/' )[1] ).Distinct().ToArray() );
		foreach ( var (path, sign) in signs )
		{
			var bitmap = sign.ExtraImage!;
			Assert.AreEqual( (256, 128, 4), (bitmap.Width, bitmap.Height, bitmap.BytesPerPixel), path );
			var pixels = SignWaveletDecoder.Decode( bitmap );
			Assert.AreEqual( 256 * 128 * 4, pixels.Length, path );
			Assert.IsTrue( pixels.Any( value => value != 0 ), path );
			Assert.IsTrue( pixels.Where( (_, index ) => index % 4 == 0 ).Any( value => value != 0 ), path );
			Assert.IsTrue( pixels.Where( (_, index ) => index % 4 != 0 ).Distinct().Count() > 1, path );
		}
	}

	[TestMethod]
	public void JungleGateBoardDecodesWithCoverageAcrossBothFaces()
	{
		var (_, bytes) = SignFileTests.OriginalSignMembers( StringTableTests.OriginalDataDirectory() )
			.Single( member => member.Path == "levels/jungle/features/gates.wad/gates.sgn" );
		var board = new SignFile( bytes ).ExtraImage!;
		var pixels = SignWaveletDecoder.Decode( board );
		for ( var x = 0; x < board.Width; x++ )
		for ( var y = 0; y < board.Height; y++ )
			Assert.IsTrue( pixels[(y * board.Width + x) * 4] >= 250, $"gate board alpha at ({x},{y})" );
	}

	[TestMethod]
	public void RejectsMalformedHeaderAndChunks()
	{
		var bitmap = new SignBitmap( 256, 128, 4, new byte[24] ) { IsWavelet = true };
		Assert.ThrowsException<InvalidDataException>( () => SignWaveletDecoder.Decode( bitmap ) );
		Assert.ThrowsException<InvalidDataException>( () => SignWaveletDecoder.Decode( bitmap with { Width = 4096 } ) );
		Assert.ThrowsException<InvalidDataException>( () => SignWaveletDecoder.Decode( bitmap with { IsWavelet = false } ) );
		var invalid = new byte[80];
		for ( var i = 0; i < 4; i++ ) BinaryPrimitives.WriteSingleLittleEndian( invalid.AsSpan( i * 4 ), 1 );
		BinaryPrimitives.WriteUInt32LittleEndian( invalid.AsSpan( 16 ), 28 );
		BinaryPrimitives.WriteUInt32LittleEndian( invalid.AsSpan( 20 ), 28 );
		Assert.ThrowsException<InvalidDataException>( () => SignWaveletDecoder.Decode( bitmap with { Pixels = invalid } ) );
		BinaryPrimitives.WriteUInt32LittleEndian( invalid.AsSpan( 20 ), uint.MaxValue );
		Assert.ThrowsException<InvalidDataException>( () => SignWaveletDecoder.Decode( bitmap with { Pixels = invalid } ) );
	}

	[TestMethod]
	public void CorpusChunkEnvelopeRejectsMutationAndTruncation()
	{
		var bitmap = SignFileTests.OriginalSignMembers( StringTableTests.OriginalDataDirectory() )
			.Select( member => new SignFile( member.Data ).ExtraImage )
			.First( image => image is { IsWavelet: true } )!;
		var payload = (byte[])bitmap.Payload.Clone();
		payload[24] = 0;
		Assert.ThrowsException<InvalidDataException>( () => SignWaveletDecoder.Decode( bitmap with { Pixels = payload } ) );
		Assert.ThrowsException<InvalidDataException>( () => SignWaveletDecoder.Decode( bitmap with { Pixels = bitmap.Payload[..^1] } ) );
		Assert.ThrowsException<InvalidDataException>( () => SignWaveletDecoder.Decode( bitmap with { Pixels = bitmap.Payload.Concat( new byte[] { 0 } ).ToArray() } ) );
		payload = (byte[])bitmap.Payload.Clone();
		BinaryPrimitives.WriteSingleLittleEndian( payload, float.NaN );
		Assert.ThrowsException<InvalidDataException>( () => SignWaveletDecoder.Decode( bitmap with { Pixels = payload } ) );
		payload = (byte[])bitmap.Payload.Clone();
		BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( 24 + 4 ), uint.MaxValue );
		Assert.ThrowsException<InvalidDataException>( () => SignWaveletDecoder.Decode( bitmap with { Pixels = payload } ) );
	}
}
