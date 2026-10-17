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
		// u32 stamp, htonl(0x01221985), at 0x605; the byte before it is the last byte of the 0x100-byte header
		BinaryPrimitives.WriteUInt32BigEndian( container.AsSpan( 0x605 ), 0x01221985 );
		Encoding.ASCII.GetBytes( "BILZ" ).CopyTo( container, 0x60d );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x611 ), (uint)payload.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( container.AsSpan( 0x615 ), (uint)(28 + compressedBytes.Length) );
		compressedBytes.CopyTo( container, 0x629 );
		return container;
	}

	private static byte[] CreateExtendedContainer( byte[] payload )
	{
		var legacy = CreateContainer( payload );
		var extended = new byte[legacy.Length + 24];
		legacy.AsSpan( 0, 4 ).CopyTo( extended );
		new byte[] { 1, 19, 0, 0, 0 }.CopyTo( extended, 4 );
		Encoding.ASCII.GetBytes( "Version: Beta_21\r\n\0" ).CopyTo( extended, 9 );
		legacy.AsSpan( 4 ).CopyTo( extended.AsSpan( 28 ) );
		return extended;
	}

	[TestMethod]
	public void DecodesObservedExtendedTpwsContainer()
	{
		var payload = Encoding.ASCII.GetBytes( "extended park" );
		using var reader = new SaveReader( new MemoryStream( CreateExtendedContainer( payload ) ) );
		Assert.AreEqual( new SaveContainerInfo( 500, 0, 0x01221985, payload.Length, reader.buffer.Length - 0x625, 24, "Version: Beta_21\r\n" ), reader.Inspect() );
		CollectionAssert.AreEqual( payload, reader.ReadFile() );
		CollectionAssert.AreEqual( payload, reader.ReadFile() );
	}

	/// <summary>A Theme Park Inc container: <paramref name="block"/> (the version block bytes) sits between the magic and the language byte.</summary>
	private static byte[] InsertVersionBlock( byte[] legacy, byte[] block )
	{
		var result = new byte[legacy.Length + block.Length];
		legacy.AsSpan( 0, 4 ).CopyTo( result );
		block.CopyTo( result, 4 );
		legacy.AsSpan( 4 ).CopyTo( result.AsSpan( 4 + block.Length ) );
		return result;
	}

	private static byte[] VersionBlock( string text )
	{
		var bytes = Encoding.ASCII.GetBytes( text + "\0" );
		var block = new byte[5 + bytes.Length];
		block[0] = 1;
		BinaryPrimitives.WriteUInt32LittleEndian( block.AsSpan( 1 ), (uint)bytes.Length );
		bytes.CopyTo( block, 5 );
		return block;
	}

	[TestMethod]
	public void KeepsAnyVersionStringOfTheThemeParkIncVersionBlock()
	{
		// The Theme Park Inc loader rejects a string other than its Ver.txt line (error 9); the container reader only records it.
		var payload = Encoding.ASCII.GetBytes( "park" );
		foreach ( var text in new[] { "Version: Beta_21\r\n", "Version: Beta_22\r\n", "x\n", "Retail 1.0" } )
		{
			var legacy = CreateContainer( payload );
			legacy[4] = 3;
			using var reader = new SaveReader( new MemoryStream( InsertVersionBlock( legacy, VersionBlock( text ) ) ) );
			var info = reader.Inspect();
			Assert.AreEqual( text, info.VersionText );
			Assert.AreEqual( 5 + text.Length + 1, info.VersionBlockBytes );
			Assert.IsTrue( info.IsThemeParkInc );
			Assert.AreEqual( (byte)3, info.Language );
			CollectionAssert.AreEqual( payload, reader.ReadFile() );
		}
	}

	[TestMethod]
	public void ReadsThemeParkIncContainerWithoutVersionString()
	{
		var payload = Encoding.ASCII.GetBytes( "no version" );
		var legacy = CreateContainer( payload );
		legacy[4] = 2;
		using var reader = new SaveReader( new MemoryStream( InsertVersionBlock( legacy, new byte[] { 0 } ) ) );
		var info = reader.Inspect();
		Assert.AreEqual( 1, info.VersionBlockBytes );
		Assert.IsNull( info.VersionText );
		Assert.IsTrue( info.IsThemeParkInc );
		Assert.AreEqual( (byte)2, info.Language );
		CollectionAssert.AreEqual( payload, reader.ReadFile() );
	}

	[TestMethod]
	public void SpanishThemeParkWorldContainerIsNotTakenForThemeParkInc()
	{
		// Language 1 (Spanish) is the byte a Theme Park Inc container uses as its has-version flag.
		var payload = Encoding.ASCII.GetBytes( "spanish park" );
		foreach ( var banner in new[] { "", "Theme Park World", "Version: Beta_21\r\n", "\u0013" } )
		{
			var container = CreateContainer( payload );
			container[4] = 1;
			for ( var index = 0; index < banner.Length; index++ )
				BinaryPrimitives.WriteUInt16LittleEndian( container.AsSpan( 5 + index * 2 ), banner[index] );
			using var reader = new SaveReader( new MemoryStream( container ) );
			var info = reader.Inspect();
			Assert.IsFalse( info.IsThemeParkInc, banner );
			Assert.AreEqual( (byte)1, info.Language, banner );
			Assert.AreEqual( 0, info.VersionBlockBytes, banner );
			Assert.IsNull( info.VersionText, banner );
			Assert.AreEqual( banner, reader.InspectHeader().Banner );
			CollectionAssert.AreEqual( payload, reader.ReadFile(), banner );
		}
	}

	[TestMethod]
	public void ReadsLanguageBannerAndRequiredContentBitmap()
	{
		var container = CreateContainer( new byte[] { 1 } );
		container[4] = 6;
		container[0x505] = 0xA5;
		container[0x505 + 31] = 0x5A;
		container[0x505 + 32] = 0xFF;
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.AreEqual( (byte)6, reader.Inspect().Language );
		var header = reader.InspectHeader();
		Assert.AreEqual( 32, header.RequiredContent.Length );
		Assert.AreEqual( 0xA5, header.RequiredContent[0] );
		Assert.AreEqual( 0x5A, header.RequiredContent[31] );
		Assert.AreEqual( 0u, header.OnlineFlag );
	}

	[TestMethod]
	public void RejectsLanguageStampAndWholeOnlineWord()
	{
		var container = CreateContainer( new byte[] { 1 } );
		container[4] = 7;
		using ( var reader = new SaveReader( new MemoryStream( container ) ) )
			Assert.ThrowsException<InvalidDataException>( () => reader.Inspect() );
		container[4] = 0;
		container[0x605] ^= 1;
		using ( var reader = new SaveReader( new MemoryStream( container ) ) )
			Assert.ThrowsException<InvalidDataException>( () => reader.Inspect() );
		container[0x605] ^= 1;
		// The online word is a u32: any nonzero byte of it is an online save.
		container[0x60c] = 1;
		using ( var reader = new SaveReader( new MemoryStream( container ) ) )
			Assert.ThrowsException<NotSupportedException>( () => reader.Inspect() );
	}

	[DataTestMethod]
	[DataRow( 5 )]
	[DataRow( 9 )]
	[DataRow( 27 )]
	public void RejectsMalformedExtendedHeader( int offset )
	{
		var container = CreateExtendedContainer( new byte[] { 1 } );
		container[offset] ^= 0xff;
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
	}

	[DataTestMethod]
	[DataRow( 0x620 )]
	[DataRow( 0x625 )]
	[DataRow( 0x62d )]
	public void RejectsCorruptExtendedHeaderFields( int offset )
	{
		var container = CreateExtendedContainer( new byte[] { 1 } );
		container[offset] ^= 0xff;
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
	}

	[TestMethod]
	public void RejectsTruncatedExtendedContainer()
	{
		var container = CreateExtendedContainer( new byte[] { 1 } );
		Array.Resize( ref container, 0x629 + 6 );
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<InvalidDataException>( () => reader.ReadFile() );
	}

	[TestMethod]
	public void RejectsOnlineExtendedContainer()
	{
		var container = CreateExtendedContainer( new byte[] { 1 } );
		container[0x621] = 1;
		using var reader = new SaveReader( new MemoryStream( container ) );
		Assert.ThrowsException<NotSupportedException>( () => reader.ReadFile() );
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
		Assert.AreEqual( 0x01221985u, header.Stamp );
		Assert.AreEqual( (byte)0, header.Language );
		Assert.IsFalse( header.IsThemeParkInc );
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
