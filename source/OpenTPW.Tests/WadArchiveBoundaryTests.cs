using System;
using System.Buffers.Binary;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class WadArchiveBoundaryTests
{
	private static byte[] ArchiveBytes()
	{
		var bytes = new byte[186];
		"DWFB"u8.CopyTo( bytes );
		void Word( int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( offset ), value );
		Word( 4, 1 ); Word( 72, 2 ); Word( 76, 88 ); Word( 80, 80 );
		for ( var index = 0; index < 2; index++ )
		{
			var record = 88 + index * 40;
			Word( record + 4, (uint)(168 + index * 6) ); Word( record + 8, 6 );
			Word( record + 12, (uint)(180 + index * 3) ); Word( record + 16, 3 ); Word( record + 24, 3 );
		}
		"a.bin\0b.bin\0"u8.CopyTo( bytes.AsSpan( 168 ) );
		new byte[] { 1, 2, 3, 7, 8, 9 }.CopyTo( bytes, 180 );
		return bytes;
	}

	[TestMethod]
	public void SeekableShortReadsCompleteAndKeepExistingInputOwnership()
	{
		var stream = new Input( ArchiveBytes(), readLimit: 3 );
		using var archive = new WadArchive( stream );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, archive.GetFile( "a.bin" ).GetData() );
		Assert.IsTrue( stream.WasDisposed, "WAD stream constructors retain their existing consume-and-close ownership" );
	}

	[TestMethod]
	public void EarlyEofCannotInventZeroPayloadBytes()
	{
		var bytes = ArchiveBytes();
		var stream = new Input( bytes[..^1], claimedLength: bytes.Length );
		Assert.ThrowsException<InvalidDataException>( () => new WadArchive( stream ) );
		Assert.IsTrue( stream.WasDisposed );
	}

	[TestMethod]
	public void StreamLengthCannotWrapIntoASmallManagedBuffer()
	{
		var bytes = ArchiveBytes();
		var stream = new Input( bytes, claimedLength: bytes.Length + (1L << 32) );
		Assert.ThrowsException<InvalidDataException>( () => new WadArchive( stream ) );
		Assert.IsTrue( stream.WasDisposed );
	}

	[DataTestMethod]
	[DataRow( -1 )]
	[DataRow( int.MaxValue )]
	public void DirectoryCountsMustFitTheirFixedRecords( int count )
	{
		var bytes = ArchiveBytes();
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 72 ), count );
		Assert.ThrowsException<InvalidDataException>( () => new WadArchive( new MemoryStream( bytes ) ) );
	}

	[DataTestMethod]
	[DataRow( 92, uint.MaxValue )]
	[DataRow( 96, 0u )]
	[DataRow( 96, uint.MaxValue )]
	[DataRow( 100, uint.MaxValue )]
	[DataRow( 104, uint.MaxValue )]
	[DataRow( 104, 0x7fffffffu )]
	public void NameAndMemberRangesRejectBeforeNarrowingOrPayloadAllocation( int offset, uint value )
	{
		var bytes = ArchiveBytes();
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( offset ), value );
		Assert.ThrowsException<InvalidDataException>( () => new WadArchive( new MemoryStream( bytes ) ) );
	}

	[TestMethod]
	public void TruncatedHeaderAndDeclaredMemberRangeRejectFromMemoryAndPath()
	{
		var bytes = ArchiveBytes();
		for ( var length = 0; length < 88; length++ )
		{
			var prefix = length;
			Assert.ThrowsException<InvalidDataException>( () => new WadArchive( new MemoryStream( bytes, 0, prefix ) ), $"header prefix {prefix}" );
		}
		Assert.ThrowsException<InvalidDataException>( () => new WadArchive( new MemoryStream( bytes[..^1] ) ) );
		var path = Path.GetTempFileName();
		try
		{
			File.WriteAllBytes( path, bytes[..^1] );
			Assert.ThrowsException<InvalidDataException>( () => new WadArchive( path ) );
		}
		finally { File.Delete( path ); }
	}

	[TestMethod]
	public void ValidEntryStreamsAreIsolatedAndDirectReadPaddingIsPreserved()
	{
		var bytes = ArchiveBytes();
		var path = Path.GetTempFileName();
		try
		{
			File.WriteAllBytes( path, bytes );
			using var fromPath = new WadArchive( path );
			using var inMemory = new WadArchive( new MemoryStream( bytes ) );
			foreach ( var archive in new[] { fromPath, inMemory } )
			{
				CollectionAssert.AreEqual( new byte[] { 9, 0, 0, 0 }, archive.GetData( bytes.Length - 1, 4 ) );
				CollectionAssert.AreEqual( new byte[3], archive.GetData( bytes.Length + 10, 3 ) );
				using var first = archive.OpenFile( "a.bin" );
				using var second = archive.OpenFile( "b.bin" );
				using var anotherFirst = archive.OpenFile( "a.bin" );
				Assert.AreEqual( 1, first.ReadByte() ); Assert.AreEqual( 1, anotherFirst.ReadByte() );
				Assert.AreEqual( 7, second.ReadByte() );
				first.Dispose();
				Assert.AreEqual( 8, second.ReadByte() ); Assert.AreEqual( 2, anotherFirst.ReadByte() );
			}
			Assert.AreEqual( 0, fromPath.Buffer.Length, "path archives retain no full payload buffer" );
		}
		finally { File.Delete( path ); }
	}

	[TestMethod]
	public void DisposalKeepsBorrowedCachedArraysButRejectsUncachedBackingReads()
	{
		var archive = new WadArchive( new MemoryStream( ArchiveBytes() ) );
		var member = archive.GetFile( "a.bin" ); var cached = member.GetData();
		archive.Dispose();
		Assert.AreSame( cached, member.GetData(), "the existing decoded member cache survives independently of backing reads" );
		Assert.ThrowsException<ObjectDisposedException>( () => archive.GetFile( "b.bin" ).GetData() );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, cached );
	}

	private sealed class Input : Stream
	{
		private readonly MemoryStream inner;
		private readonly long claimedLength;
		private readonly int readLimit;
		public bool WasDisposed { get; private set; }
		public Input( byte[] bytes, long? claimedLength = null, int readLimit = int.MaxValue )
		{ inner = new MemoryStream( bytes ); this.claimedLength = claimedLength ?? bytes.Length; this.readLimit = readLimit; }
		public override bool CanRead => !WasDisposed;
		public override bool CanSeek => !WasDisposed;
		public override bool CanWrite => false;
		public override long Length => claimedLength;
		public override long Position { get => inner.Position; set => inner.Position = value; }
		public override int Read( byte[] buffer, int offset, int count ) => inner.Read( buffer, offset, Math.Min( count, readLimit ) );
		public override int Read( Span<byte> buffer ) => inner.Read( buffer[..Math.Min( buffer.Length, readLimit )] );
		public override long Seek( long offset, SeekOrigin origin ) => inner.Seek( offset, origin );
		public override void Flush() { }
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
		protected override void Dispose( bool disposing ) { WasDisposed = true; if ( disposing ) inner.Dispose(); base.Dispose( disposing ); }
	}
}
