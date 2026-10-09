using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SaveReaderTests
{
	internal static byte[] CreateContainer( byte[] payload, uint magic = 500 )
	{
		using var compressed = new MemoryStream();
		using ( var encoder = new ZLibStream( compressed, CompressionLevel.Optimal, true ) )
			encoder.Write( payload );
		// .NET 9+ (zlib-ng) writes nothing for an empty input; the game's saves hold a complete
		// zlib stream, so use the canonical empty stream there.
		var compressedBytes = compressed.Length > 0 ? compressed.ToArray() : Convert.FromHexString( "789C030000000001" );
		var container = new byte[0x629 + compressedBytes.Length];
		BinaryPrimitives.WriteUInt32LittleEndian( container, magic );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x604 ), 0x19220100 );
		container[0x608] = 133;
		Encoding.ASCII.GetBytes( "BILZ" ).CopyTo( container, 0x60d );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x611 ), (uint)payload.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x615 ), (uint)(28 + compressedBytes.Length) );
		compressedBytes.CopyTo( container, 0x629 );
		return container;
	}

	[TestMethod]
	public void DecodesLegacyOfflineContainer()
	{
		var payload = Encoding.ASCII.GetBytes( "park\0data\0" );
		var reader = new SaveReader( new MemoryStream( CreateContainer( payload ) ) );
		try
		{
			CollectionAssert.AreEqual( payload, reader.ReadFile() );
		}
		finally
		{
			reader.Dispose();
		}
	}

	[TestMethod]
	public void RepeatedReadsReturnIdenticalPayload()
	{
		var payload = new byte[] { 1, 2, 3, 0, 255 };
		var reader = new SaveReader( new MemoryStream( CreateContainer( payload ) ) );
		try
		{
			CollectionAssert.AreEqual( payload, reader.ReadFile() );
			CollectionAssert.AreEqual( payload, reader.ReadFile() );
		}
		finally
		{
			reader.Dispose();
		}
	}

	[TestMethod]
	public void TextViewRemovesNullsWithoutChangingPayload()
	{
		var payload = Encoding.ASCII.GetBytes( "park\0data\0" );
		var reader = new SaveReader( new MemoryStream( CreateContainer( payload ) ) );
		try
		{
			Assert.AreEqual( "parkdata", reader.FileToString() );
			CollectionAssert.AreEqual( payload, reader.ReadFile() );
		}
		finally
		{
			reader.Dispose();
		}
	}

	[TestMethod]
	public void ObservedTpwiHeaderAndEmptyPayloadAreSupported()
	{
		using var reader = new SaveReader( new MemoryStream( CreateContainer( Array.Empty<byte>(), 400 ) ) );
		var header = reader.Inspect();
		Assert.AreEqual( 400u, header.Magic );
		Assert.AreEqual( 0x19220100u, header.FileType );
		Assert.AreEqual( (byte)133, header.Version );
		Assert.AreEqual( 0, header.DecodedLength );
		Assert.AreEqual( 0, reader.ReadFile().Length );
	}

	[TestMethod]
	public void LeavesCallerStreamOpenAndSupportsNonseekableShortReads()
	{
		var payload = Encoding.ASCII.GetBytes( "short reads" );
		using var stream = new ShortReadStream( CreateContainer( payload ) );
		using ( var reader = new SaveReader( stream ) )
			CollectionAssert.AreEqual( payload, reader.ReadFile() );
		Assert.IsTrue( stream.CanRead );
		Assert.AreEqual( -1, stream.ReadByte() );
	}

	[TestMethod]
	public void ReadsFromCurrentPositionAndDoesNotExposeMutableInternalBuffer()
	{
		var payload = new byte[] { 7, 8, 9 };
		var container = CreateContainer( payload );
		using var stream = new MemoryStream();
		stream.Write( new byte[8] );
		stream.Write( container );
		stream.Position = 8;
		using var reader = new SaveReader( stream );
		reader.buffer[0] = 0;
		CollectionAssert.AreEqual( payload, reader.ReadFile() );
		Assert.IsTrue( stream.CanRead );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 3 )]
	[DataRow( 0x608 )]
	[DataRow( 0x60d )]
	[DataRow( 0x629 )]
	public void RejectsTruncatedHeaders( int length )
	{
		using var reader = new SaveReader( new MemoryStream( new byte[length] ) );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 0x608 )]
	[DataRow( 0x60d )]
	public void RejectsUnsupportedMagicVersionOrChunk( int offset )
	{
		var container = CreateContainer( new byte[] { 1 } );
		container[offset] ^= 0xff;
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
	}

	[TestMethod]
	public void RejectsOnlineFlagExplicitly()
	{
		var container = CreateContainer( new byte[] { 1 } );
		container[0x609] = 1;
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<NotSupportedException>( () => reader.ReadFile() );
	}

	[TestMethod]
	public void RejectsDeclaredChunkSizeMismatch()
	{
		var container = CreateContainer( new byte[] { 1 } );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x615 ), uint.MaxValue );
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 10 )]
	public void RejectsBothUndersizedAndOversizedDecodedDeclarations( int declaredLength )
	{
		var container = CreateContainer( new byte[] { 1, 2, 3 } );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x611 ), (uint)declaredLength );
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
	}

	[TestMethod]
	public void RejectsInputLimitWithoutClosingCallerStream()
	{
		var container = CreateContainer( new byte[] { 1 } );
		using var stream = new ShortReadStream( container );
		Assert.ThrowsException<InvalidDataException>( () => new SaveReader( stream, maximumContainerBytes: container.Length - 1 ) );
		Assert.IsTrue( stream.CanRead );
		using var exactReader = new SaveReader( new MemoryStream( container ), maximumContainerBytes: container.Length );
		CollectionAssert.AreEqual( new byte[] { 1 }, exactReader.ReadFile() );
	}

	[TestMethod]
	public void RejectsDecodedLimitAndBombWithFalseDeclaration()
	{
		var container = CreateContainer( new byte[65536] );
		using var reader = new SaveReader( new MemoryStream( container ), maximumDecodedBytes: 64 );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x611 ), 64 );
		using var lyingReader = new SaveReader( new MemoryStream( container ), maximumDecodedBytes: 64 );
		Assert.ThrowsException<InvalidDataException>( () => lyingReader.ReadFile() );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x611 ), uint.MaxValue );
		using var overflowingReader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => overflowingReader.ReadFile() );
	}

	[TestMethod]
	public void AcceptsExactDecodedLimit()
	{
		var payload = new byte[64];
		using var reader = new SaveReader( new MemoryStream( CreateContainer( payload ) ), maximumDecodedBytes: payload.Length );
		CollectionAssert.AreEqual( payload, reader.ReadFile() );
	}

	[TestMethod]
	public void RejectsChecksumCorruption()
	{
		var container = CreateContainer( Encoding.ASCII.GetBytes( "checksum" ) );
		container[^1] ^= 0xff;
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
	}

	[TestMethod]
	public void RejectsTruncatedZlibEvenWithMatchingChunkLength()
	{
		var container = CreateContainer( new byte[100] );
		Array.Resize( ref container, container.Length - 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x615 ), (uint)(container.Length - 0x60d) );
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
	}

	[TestMethod]
	public void RejectsTrailingZlibBytesEvenWithMatchingChunkLength()
	{
		var container = CreateContainer( new byte[100] );
		Array.Resize( ref container, container.Length + 1 );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x615 ), (uint)(container.Length - 0x60d) );
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
	}

	[TestMethod]
	public void RejectsDictionaryAndInvalidZlibHeader()
	{
		var container = CreateContainer( new byte[100] );
		container[0x629] = 0x78;
		container[0x62a] = 0x20;
		using var dictionaryReader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => dictionaryReader.ReadFile() );
		container[0x629] = 0;
		using var invalidReader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => invalidReader.ReadFile() );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( -1 )]
	[DataRow( int.MaxValue )]
	public void RejectsInvalidLimits( int limit )
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new SaveReader( new MemoryStream(), maximumContainerBytes: limit ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new SaveReader( new MemoryStream(), maximumDecodedBytes: limit ) );
	}

	[TestMethod]
	public void DisposeIsIdempotentAndReadsAfterDisposalFail()
	{
		var reader = new SaveReader( new MemoryStream( CreateContainer( new byte[] { 1 } ) ) );
		reader.Dispose();
		reader.Dispose();
		Assert.ThrowsException<ObjectDisposedException>( () => reader.Inspect() );
		Assert.ThrowsException<ObjectDisposedException>( () => reader.ReadFile() );
	}

	private sealed class ShortReadStream : Stream
	{
		private readonly MemoryStream inner;
		public ShortReadStream( byte[] data ) => inner = new MemoryStream( data );
		public override bool CanRead => inner.CanRead;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override int Read( byte[] readBuffer, int offset, int count ) => inner.Read( readBuffer, offset, Math.Min( count, 3 ) );
		public override void Flush() => throw new NotSupportedException();
		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] writeBuffer, int offset, int count ) => throw new NotSupportedException();
		protected override void Dispose( bool disposing )
		{
			if ( disposing )
				inner.Dispose();
			base.Dispose( disposing );
		}
	}
}
