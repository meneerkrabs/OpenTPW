using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SignWaveletDecoderTests
{
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
