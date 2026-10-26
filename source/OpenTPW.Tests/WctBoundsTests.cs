using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class WctBoundsTests
{
	private static byte[] Bilz( byte[] raw )
	{
		using var compressed = new MemoryStream();
		using ( var zip = new ZLibStream( compressed, CompressionLevel.Optimal, true ) ) zip.Write( raw );
		using var output = new MemoryStream(); using var writer = new BinaryWriter( output );
		writer.Write( Encoding.ASCII.GetBytes( "BILZ" ) ); writer.Write( raw.Length ); writer.Write( 0x1C + (int)compressed.Length );
		writer.Write( 15 ); writer.Write( 9 ); writer.Write( 0 ); writer.Write( 0 ); writer.Write( compressed.ToArray() );
		return output.ToArray();
	}
	private static byte[] Wct( byte[] color, short width = 8, short height = 8, byte[]? alpha = null )
	{
		using var output = new MemoryStream(); using var writer = new BinaryWriter( output );
		writer.Write( (byte)1 ); writer.Write( (byte)(alpha == null ? 0 : 1) ); writer.Write( (byte)(alpha == null ? 0 : 0x20) ); writer.Write( (byte)0 );
		writer.Write( width ); writer.Write( height );
		for ( var i = 0; i < 4; i++ ) writer.Write( (short)0 );
		writer.Write( color.Length ); writer.Write( alpha?.Length ?? 0 ); writer.Write( 0 );
		writer.Write( color ); if ( alpha != null ) writer.Write( alpha );
		return output.ToArray();
	}
	private static TextureData Decode( byte[] data ) => new TextureFile( new MemoryStream( data ) ).Data;

	[TestMethod]
	public void NonpositiveDimensionsRejectBeforeDecode()
	{
		foreach ( var dimension in new short[] { 0, -1, short.MinValue } )
		{
			Assert.ThrowsException<InvalidDataException>( () => Decode( Wct( Bilz( new byte[96] ), dimension, 8 ) ) );
			Assert.ThrowsException<InvalidDataException>( () => Decode( Wct( Bilz( new byte[96] ), 8, dimension ) ) );
		}
	}

	[TestMethod]
	public void ShortCompressionHeaderRejectsAsMalformedData()
	{
		foreach ( var chunk in new[] { new byte[] { 0x42 }, Encoding.ASCII.GetBytes( "BILZ" ) } )
			Assert.ThrowsException<InvalidDataException>( () => Decode( Wct( chunk ) ) );
	}

	[TestMethod]
	public void LaterChannelsCannotReadDuplicatedBytesFromAnEarlierChannel()
	{
		// An 8x8 image needs 64 Y + 16 Cb + 16 Cr one-byte coefficients.
		// The old in-place shift retained the original length and silently reused bytes for missing Cr.
		Assert.ThrowsException<InvalidDataException>( () => Decode( Wct( Bilz( new byte[80] ) ) ) );
	}

	[TestMethod]
	public void CompleteOneByteAndEscapedCoefficientsDecodeWithBothCodecs()
	{
		var raw = new byte[96];
		var expected = Decode( Wct( Bilz( raw ) ) );
		Assert.AreEqual( 8 * 8 * 4, expected.Data.Length );
		for ( var i = 0; i < expected.Data.Length; i++ ) Assert.AreEqual( i % 4 == 3 ? (byte)255 : (byte)0, expected.Data[i] );
		var escaped = new byte[96 * 3];
		for ( var i = 0; i < 96; i++ ) escaped[i * 3] = 0x80;
		CollectionAssert.AreEqual( expected.Data, Decode( Wct( Bilz( escaped ) ) ).Data );
		CollectionAssert.AreEqual( expected.Data, Decode( Wct( LiteralLzss( raw ) ) ).Data );
		var alpha = Decode( Wct( Bilz( raw ), alpha: Bilz( new byte[64] ) ) );
		Assert.IsTrue( Array.TrueForAll( alpha.Data, value => value == 0 ) );
	}

	private static byte[] LiteralLzss( byte[] raw )
	{
		var bits = new System.Collections.Generic.List<bool>();
		void Put( int value, int count ) { for ( var i = 0; i < count; i++ ) bits.Add( (value & (1 << i)) != 0 ); }
		foreach ( var value in raw ) { Put( 0, 1 ); Put( value, 8 ); }
		Put( 1, 1 ); Put( 0, 12 );
		var bytes = new byte[(bits.Count + 15) / 16 * 2];
		for ( var i = 0; i < bits.Count; i++ ) if ( bits[i] ) bytes[i / 8] |= (byte)(1 << (i % 8));
		return bytes;
	}

	[TestMethod]
	public void OddLengthLzssReadsItsFinalByteAsAHalfWord()
	{
		// 97 literals and the terminator take 886 bits: 111 bytes, one past a whole 16-bit word. Retail chunks end
		// like this (thousands of the original WCTs have an odd LZSS length); the last word is that byte plus zeros.
		var raw = new byte[97];
		for ( var i = 0; i < raw.Length; i++ ) raw[i] = (byte)(i * 7);
		var padded = LiteralLzss( raw );
		Assert.AreEqual( 112, padded.Length );
		Assert.AreEqual( 0, padded[^1] );
		CollectionAssert.AreEqual( Decode( Wct( padded ) ).Data, Decode( Wct( padded[..^1] ) ).Data );
		// Without that byte the terminator is gone: the stream is refused rather than completed with invented bits.
		Assert.ThrowsException<InvalidDataException>( () => Decode( Wct( padded[..^2] ) ) );
	}

	[TestMethod]
	public void TruncatedLzssAndCoefficientEscapesRejectInsteadOfFabricatingInput()
	{
		foreach ( var compressed in new[] { new byte[2], LiteralLzss( new byte[96] )[..^2] } )
			Assert.ThrowsException<InvalidDataException>( () => Decode( Wct( compressed ) ) );
		var escape = new byte[96]; escape[^1] = 0x80;
		Assert.ThrowsException<InvalidDataException>( () => Decode( Wct( Bilz( escape ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Decode( Wct( Bilz( new byte[96] ), alpha: Bilz( new byte[63] ) ) ) );
	}

	[TestMethod]
	public void TruncatedHeadersChunksAndSignedCountsReject()
	{
		var valid = Wct( Bilz( new byte[96] ) );
		for ( var i = 0; i < 28; i++ ) Assert.ThrowsException<InvalidDataException>( () => Decode( valid[..i] ) );
		Assert.ThrowsException<InvalidDataException>( () => Decode( valid[..^1] ) );
		foreach ( var count in new[] { -1, int.MinValue, 0, int.MaxValue } )
		{
			var color = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian( color.AsSpan( 16 ), count );
			Assert.ThrowsException<InvalidDataException>( () => Decode( color ) );
		}
		var alpha = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian( alpha.AsSpan( 20 ), -1 );
		Assert.ThrowsException<InvalidDataException>( () => Decode( alpha ) );
		alpha = valid.ToArray(); alpha[1] = 1; alpha[2] = 0x20;
		Assert.ThrowsException<InvalidDataException>( () => Decode( alpha ) );
	}

	[TestMethod]
	public void BilzSizesAndTruncatedCompressedStreamReject()
	{
		var valid = Wct( Bilz( new byte[96] ) );
		foreach ( var output in new[] { -1, 0, 95, 97, int.MaxValue } )
		{
			var changed = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian( changed.AsSpan( 32 ), output );
			Assert.ThrowsException<InvalidDataException>( () => Decode( changed ) );
		}
		var inputLength = BinaryPrimitives.ReadInt32LittleEndian( valid.AsSpan( 36 ) );
		foreach ( var input in new[] { -1, 0, inputLength - 1, inputLength + 1, int.MaxValue } )
		{
			var changed = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian( changed.AsSpan( 36 ), input );
			Assert.ThrowsException<InvalidDataException>( () => Decode( changed ) );
		}
	}

	[TestMethod]
	public void ArrayArithmeticAndInsufficientLargeImageDataRejectBeforeLargeAllocation()
	{
		foreach ( var width in new short[] { 16384, 16385, short.MaxValue } )
		{
			var bytes = Wct( Bilz( new byte[96] ), width, 1 );
			Assert.ThrowsException<InvalidDataException>( () => Decode( bytes ) ); // warm up
			var before = GC.GetAllocatedBytesForCurrentThread();
			Assert.ThrowsException<InvalidDataException>( () => Decode( bytes ) );
			Assert.IsTrue( GC.GetAllocatedBytesForCurrentThread() - before < 512 * 1024, "invalid dimensions/data must not allocate image planes" );
		}
	}

	[TestMethod]
	public void InflatedPaddingIsValidatedWithoutRetainingTheUnusedTail()
	{
		var padded = Wct( Bilz( new byte[1024 * 1024] ) );
		var expected = Decode( Wct( Bilz( new byte[96] ) ) );
		Decode( padded );
		var before = GC.GetAllocatedBytesForCurrentThread();
		var actual = Decode( padded );
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		CollectionAssert.AreEqual( expected.Data, actual.Data );
		Assert.IsTrue( allocated < 512 * 1024, $"unused padded output allocated {allocated} bytes" );
	}

	private sealed class Nonseekable : Stream
	{
		private readonly MemoryStream stream;
		public Nonseekable( byte[] data ) => stream = new MemoryStream( data );
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override int Read( byte[] buffer, int offset, int count ) => stream.Read( buffer, offset, Math.Min( count, 3 ) );
		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
		public override void Flush() { }
		protected override void Dispose( bool disposing ) { if ( disposing ) stream.Dispose(); base.Dispose( disposing ); }
	}

	[TestMethod]
	public void NonseekableShortReadsWorkAndHugeClaimDoesNotAllocateByTheClaim()
	{
		var valid = Wct( Bilz( new byte[96] ) );
		using ( var input = new Nonseekable( valid ) ) CollectionAssert.AreEqual( Decode( valid ).Data, new TextureFile( input ).Data.Data );
		BinaryPrimitives.WriteInt32LittleEndian( valid.AsSpan( 16 ), int.MaxValue );
		var before = GC.GetAllocatedBytesForCurrentThread();
		using var malformed = new Nonseekable( valid );
		Assert.ThrowsException<InvalidDataException>( () => new TextureFile( malformed ) );
		Assert.IsTrue( GC.GetAllocatedBytesForCurrentThread() - before < 512 * 1024 );
	}

	[TestMethod]
	public void CheckedBitReadsDoNotChangeLegacyPaddingBehavior()
	{
		Assert.AreEqual( (short)0, new BitReader( Array.Empty<byte>() ).GetBits( 8 ) );
		Assert.ThrowsException<InvalidDataException>( () => new BitReader( Array.Empty<byte>() ).GetBitsChecked( 8 ) );
		var padded = new BitReader( new byte[] { 0xA5, 0x5A } );
		Assert.AreEqual( (short)0xA5, padded.GetBitsChecked( 8 ) );
		Assert.AreEqual( (short)0x5A, padded.GetBitsChecked( 8 ) );
		Assert.ThrowsException<InvalidDataException>( () => padded.GetBitsChecked( 1 ) );
	}
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void IncompleteEscapedLargeImageRejectsBeforeAllocatingImagePlanes( bool escapedAlpha )
	{
		// The minimum byte count is sufficient only when coefficients use one byte.
		// An all-escape stream has fewer coefficients and must fail before image-plane allocation.
		var color = Enumerable.Repeat( escapedAlpha ? (byte)0 : (byte)0x80, 1024 * 1024 * 3 / 2 ).ToArray();
		var alpha = escapedAlpha ? Bilz( Enumerable.Repeat( (byte)0x80, 1024 * 1024 ).ToArray() ) : null;
		var malformed = Wct( Bilz( color ), width: 1024, height: 1, alpha: alpha );
		Assert.ThrowsException<InvalidDataException>( () => Decode( malformed ) );
		var before = GC.GetAllocatedBytesForCurrentThread();
		Assert.ThrowsException<InvalidDataException>( () => Decode( malformed ) );
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsTrue( allocated < 16 * 1024 * 1024, $"incomplete coefficients allocated image planes: {allocated} bytes" );
	}

	[TestMethod]
	public void PaddingChecksumIsValidatedBeyondTheRetainedCoefficientPrefix()
	{
		var padded = Bilz( new byte[1024 * 1024] );
		padded[^1] ^= 1;
		Assert.ThrowsException<InvalidDataException>( () => Decode( Wct( padded ) ) );
	}

	[TestMethod]
	public void ValidEscapedAlphaAndLzssPaddingPreserveDecodedPixels()
	{
		var expected = Decode( Wct( Bilz( new byte[96] ), width: 3, height: 5, alpha: Bilz( new byte[64] ) ) );
		var color = new byte[96 * 3];
		var alpha = new byte[64 * 3];
		for ( var index = 0; index < 96; index++ ) color[index * 3] = 0x80;
		for ( var index = 0; index < 64; index++ ) alpha[index * 3] = 0x80;
		CollectionAssert.AreEqual( expected.Data, Decode( Wct( Bilz( color.Concat( new byte[1024] ).ToArray() ),
			width: 3, height: 5, alpha: Bilz( alpha.Concat( new byte[1024] ).ToArray() ) ) ).Data );
		CollectionAssert.AreEqual( expected.Data, Decode( Wct( LiteralLzss( color.Concat( new byte[1024] ).ToArray() ),
			width: 3, height: 5, alpha: LiteralLzss( alpha.Concat( new byte[1024] ).ToArray() ) ) ).Data );
	}

}
