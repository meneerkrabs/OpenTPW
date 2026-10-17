using System;
using System.Buffers.Binary;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SignWaveletDecoderTests
{
	[TestMethod]
	public void AColourByteRoundsToNearestEvenAndWrapsLikeTheNativeMagicNumberConversion()
	{
		// tp.exe 0x5935A0: the sum plus 12583040.0 is stored as a float32 and its low 9 bits index clamp(i - 128)
		Assert.AreEqual( 0, SignWaveletDecoder.ColourByte( 0.4f ) );
		Assert.AreEqual( 0, SignWaveletDecoder.ColourByte( 0.5f ), "ties go to the even neighbour" );
		Assert.AreEqual( 2, SignWaveletDecoder.ColourByte( 1.5f ) );
		Assert.AreEqual( 2, SignWaveletDecoder.ColourByte( 2.5f ) );
		Assert.AreEqual( 1, SignWaveletDecoder.ColourByte( 0.6f ), "truncation would give 0" );
		Assert.AreEqual( 255, SignWaveletDecoder.ColourByte( 254.6f ) );
		Assert.AreEqual( 255, SignWaveletDecoder.ColourByte( 300f ) );
		Assert.AreEqual( 0, SignWaveletDecoder.ColourByte( -100f ) );
		Assert.AreEqual( 255, SignWaveletDecoder.ColourByte( 383f ), "the last sum before the wrap" );
		Assert.AreEqual( 0, SignWaveletDecoder.ColourByte( 384f ), "a sum past 383 wraps through the 9 index bits" );
		Assert.AreEqual( 255, SignWaveletDecoder.ColourByte( -129f ), "a sum below -128 wraps to the top" );
	}

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

	[TestMethod]
	public void TheD4CoefficientsAreTheStaticInitialisersComputedAt53BitsAndStoredAsFloats()
	{
		// tp.exe 0x591600..0x591690: ((sqrt 3 - c) * 0.25) / sqrt 2 with c = -1, -3 and (3 - sqrt 3), (1 - sqrt 3); an IEEE double carries the same 53 bit roundings, then the store to a float32
		var root3 = Math.Sqrt( 3 );
		var root2 = Math.Sqrt( 2 );
		var expected = ((float)(((root3 + 1) * 0.25) / root2), (float)(((root3 + 3) * 0.25) / root2),
			(float)(((3 - root3) * 0.25) / root2), (float)(((1 - root3) * 0.25) / root2));
		var actual = SignWaveletDecoder.D4Coefficients();
		Assert.AreEqual( expected, actual );
		// the coefficients are fixed by the CRT, not by the precision the thread has later: a 24 bit thread computes the same
		Assert.AreEqual( actual, X87Precision.With( 24, SignWaveletDecoder.D4Coefficients ) );
		Assert.AreEqual( 0.4829629f, actual.H0, 1e-6f );
		Assert.AreEqual( 0.8365163f, actual.H1, 1e-6f );
		Assert.AreEqual( 0.2241439f, actual.H2, 1e-6f );
		Assert.AreEqual( -0.1294095f, actual.H3, 1e-6f );
	}

	[TestMethod]
	public void Float32ArithmeticEqualsTheEmulatedRegistersAtTwentyFourBits()
	{
		var bitmap = SyntheticBoard( 32, 16, 7 );
		var emulated = X87Precision.With( 24, () => SignWaveletDecoder.Decode( bitmap, emulateRegisters: true ) );
		CollectionAssert.AreEqual( emulated, SignWaveletDecoder.Decode( bitmap, emulateRegisters: false ) );
		CollectionAssert.AreEqual( emulated, SignWaveletDecoder.Decode( bitmap ), "the default thread precision is 24 bits" );
	}

	[TestMethod]
	public void TheRegisterPrecisionMovesDecodedPixels()
	{
		var bitmap = SyntheticBoard( 64, 32, 11 );
		var at24 = SignWaveletDecoder.Decode( bitmap );
		var at53 = X87Precision.With( 53, () => SignWaveletDecoder.Decode( bitmap ) );
		var at64 = X87Precision.With( 64, () => SignWaveletDecoder.Decode( bitmap ) );
		Assert.IsTrue( at24.Where( (value, index) => value != at53[index] ).Any(), "53 bit registers round differently from 24 bit ones" );
		Assert.IsTrue( at53.Where( (value, index) => value != at64[index] ).Count() <= at24.Where( (value, index) => value != at53[index] ).Count() );
	}

	[TestMethod]
	public void TheAlphaPlaneFollowsAnIndependentDoubleReferenceOfTheD4Inverse()
	{
		// the pair formulas written once more in doubles, rows then columns, with the scale on the coefficients: the decoder's float32 order of operations may move a byte by one level, no more
		const int side = 16;
		var random = new Random( 5 );
		var coefficients = new int[side * side];
		var alpha = new List<byte>();
		for ( var i = 0; i < coefficients.Length; i++ )
		{
			coefficients[i] = random.Next( -100, 101 ) + (i == 0 ? 600 : 0);
			if ( coefficients[i] is > -128 and < 128 )
				alpha.Add( (byte)(sbyte)coefficients[i] );
			else
			{
				alpha.Add( 0x80 );
				alpha.Add( (byte)(coefficients[i] & 0xFF) );
				alpha.Add( (byte)((coefficients[i] >> 8) & 0xFF) );
			}
		}
		const float scale = 0.25f;
		var payload = Payload( new byte[side * side + side * side / 2], alpha.ToArray(), 1, 1, 1, scale );
		var pixels = SignWaveletDecoder.Decode( new SignBitmap( side, side, 4, payload ) { IsWavelet = true } );

		var root3 = Math.Sqrt( 3 );
		var h0 = (1 + root3) / (4 * Math.Sqrt( 2 ));
		var h1 = (3 + root3) / (4 * Math.Sqrt( 2 ));
		var h2 = (3 - root3) / (4 * Math.Sqrt( 2 ));
		var h3 = (1 - root3) / (4 * Math.Sqrt( 2 ));
		double[] Inverse( double[] line )
		{
			var half = line.Length / 2;
			var result = new double[line.Length];
			for ( var k = 0; k < half; k++ )
			{
				var p = (k + half - 1) % half;
				result[2 * k] = line[p] * h2 + line[p + half] * h1 + line[k] * h0 + line[k + half] * h3;
				result[2 * k + 1] = line[p] * h3 - line[p + half] * h0 + line[k] * h1 - line[k + half] * h2;
			}
			return result;
		}
		var plane = new double[side, side];
		for ( var row = 0; row < side; row++ )
		{
			var line = Inverse( Enumerable.Range( 0, side ).Select( column => coefficients[row * side + column] * (double)scale ).ToArray() );
			for ( var column = 0; column < side; column++ ) plane[row, column] = line[column];
		}
		var differences = 0;
		for ( var column = 0; column < side; column++ )
		{
			var line = Inverse( Enumerable.Range( 0, side ).Select( row => plane[row, column] ).ToArray() );
			for ( var row = 0; row < side; row++ )
			{
				var expected = (byte)Math.Clamp( line[row], 0, 255 );
				var actual = pixels[(row * side + column) * 4];
				Assert.IsTrue( Math.Abs( expected - actual ) <= 1, $"alpha ({column},{row}): {expected} vs {actual}" );
				if ( expected != actual ) differences++;
			}
		}
		Assert.IsTrue( differences < side * side / 10, $"{differences} bytes differ by one level" );
	}

	private static SignBitmap SyntheticBoard( int width, int height, int seed )
	{
		var random = new Random( seed );
		var side = 8;
		while ( side < Math.Max( width, height ) ) side *= 2;
		byte[] Coefficients( int count, int range )
		{
			var bytes = new List<byte>();
			for ( var i = 0; i < count; i++ )
			{
				var value = random.Next( -range, range + 1 );
				if ( value is > -128 and < 128 )
					bytes.Add( (byte)(sbyte)value );
				else
				{
					bytes.Add( 0x80 );
					bytes.Add( (byte)(value & 0xFF) );
					bytes.Add( (byte)((value >> 8) & 0xFF) );
				}
			}
			return bytes.ToArray();
		}
		var colour = Coefficients( side * side, 400 ).Concat( Coefficients( side * side / 2, 300 ) ).ToArray();
		var alpha = Coefficients( side * side, 500 );
		var payload = Payload( colour, alpha, 0.37f, 0.61f, 0.83f, 0.29f );
		return new SignBitmap( width, height, 4, payload ) { IsWavelet = true };
	}

	private static byte[] Payload( byte[] colour, byte[] alpha, float y, float cb, float cr, float a )
	{
		var colourChunk = Bilz( colour );
		var alphaChunk = Bilz( alpha );
		var payload = new byte[24 + colourChunk.Length + alphaChunk.Length];
		BinaryPrimitives.WriteSingleLittleEndian( payload.AsSpan( 0 ), y );
		BinaryPrimitives.WriteSingleLittleEndian( payload.AsSpan( 4 ), cb );
		BinaryPrimitives.WriteSingleLittleEndian( payload.AsSpan( 8 ), cr );
		BinaryPrimitives.WriteSingleLittleEndian( payload.AsSpan( 12 ), a );
		BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( 16 ), (uint)colourChunk.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( 20 ), (uint)alphaChunk.Length );
		colourChunk.CopyTo( payload, 24 );
		alphaChunk.CopyTo( payload, 24 + colourChunk.Length );
		return payload;
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
	public void OriginalBoardsDecodeIdenticallyOnFloatsAndOnEmulatedRegistersAtTwentyFourBits()
	{
		var boards = SignFileTests.OriginalSignMembers( StringTableTests.OriginalDataDirectory() )
			.Select( member => new SignFile( member.Data ).ExtraImage ).Where( image => image is { IsWavelet: true } ).Take( 4 ).ToArray();
		Assert.AreEqual( 4, boards.Length );
		var moved53 = 0;
		foreach ( var board in boards )
		{
			var floats = SignWaveletDecoder.Decode( board! );
			CollectionAssert.AreEqual( X87Precision.With( 24, () => SignWaveletDecoder.Decode( board!, emulateRegisters: true ) ), floats );
			moved53 += X87Precision.With( 53, () => SignWaveletDecoder.Decode( board! ) ).Where( (value, index) => value != floats[index] ).Count();
		}
		Assert.IsTrue( moved53 > 0, "the 53 bit registers decode some pixel differently from the thread's 24 bit ones" );
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
