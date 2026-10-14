using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class MapFileWriterReviewTests
{
	private static byte[] Title => new byte[32];
	private static uint[] Words => new uint[] { 0xFF00A501, 0, uint.MaxValue, 0x10203040, 0x80000000 };

	[TestMethod]
	public void ParsedShortReadSuffixPreservesFieldsAndInputOwnership()
	{
		var rawTitle = Enumerable.Range( 0, 32 ).Select( n => (byte)(255 - n) ).ToArray(); rawTitle[13] = 0;
		var expected = MapFile.Encode( 5, 2, rawTitle, Words, Enumerable.Range( 0, 10 ).Select( n => (byte)(n * 23) ).ToArray() );
		using var input = new OneByteInput( new byte[] { 9, 8, 7 }.Concat( expected ).ToArray() );
		input.Seek( 3, SeekOrigin.Begin );
		var map = new MapFile( input );
		Assert.IsTrue( input.CanRead );
		Assert.AreEqual( 2, map.CellCountX ); Assert.AreEqual( 5, map.CellCountY );
		CollectionAssert.AreEqual( expected, map.Encode() );
		using var output = new MemoryStream(); map.Write( output ); CollectionAssert.AreEqual( expected, output.ToArray() );
	}

	[DataTestMethod, DataRow( 36 ), DataRow( 73 )]
	public void UnknownChunksCannotEnterParsedRoundtripContract( int position )
	{
		var canonical = MapFile.Encode( 1, 1, Title, Words, new byte[] { 255 } );
		var unknown = new byte[] { (byte)'X', (byte)'T', (byte)'R', (byte)'A', 0, 0, 0, 0 };
		var extended = canonical.Take( position ).Concat( unknown ).Concat( canonical.Skip( position ) ).ToArray();
		Assert.ThrowsException<NotSupportedException>( () => new MapFile( new MemoryStream( extended ) ) );
		CollectionAssert.AreEqual( canonical, new MapFile( new MemoryStream( canonical ) ).Encode() );
	}

	[TestMethod]
	public void InvalidDimensionsRefuseBeforeTouchingRawCollectionsOrPayloadAllocation()
	{
		using var output = new MemoryStream(); output.WriteByte( 42 );
		foreach ( var shape in new[] { (int.MinValue, int.MinValue), (int.MaxValue, int.MaxValue), (-1, -1), (1, 1025) } )
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			Assert.ThrowsException<ArgumentException>( () => MapFile.Write( output, shape.Item1, shape.Item2, new Untouchable<byte>(), new Untouchable<uint>(), new Untouchable<byte>() ) );
			Assert.IsTrue( GC.GetAllocatedBytesForCurrentThread() - before < 64 * 1024 );
			CollectionAssert.AreEqual( new byte[] { 42 }, output.ToArray() ); Assert.AreEqual( 1L, output.Position );
		}
	}

	[TestMethod]
	public void LateCellAccessorFailureCannotWriteAnyEncodedPrefix()
	{
		using var output = new MemoryStream( new byte[] { 11, 22, 33, 44 }, true ); output.Position = 2;
		Assert.ThrowsException<IOException>( () => MapFile.Write( output, 2, 1, Title, Words, new FailingCells() ) );
		CollectionAssert.AreEqual( new byte[] { 11, 22, 33, 44 }, output.ToArray() ); Assert.AreEqual( 2L, output.Position ); Assert.IsTrue( output.CanWrite );
	}

	private sealed class OneByteInput( byte[] bytes ) : MemoryStream( bytes )
	{
		public override bool CanSeek => false;
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override long Length => throw new NotSupportedException();
		public override int Read( byte[] buffer, int offset, int count ) => base.Read( buffer, offset, Math.Min( 1, count ) );
	}
	private sealed class Untouchable<T> : IReadOnlyList<T>
	{
		public int Count => throw new AssertFailedException( "Dimensions must be checked before collection counts." );
		public T this[int index] => throw new AssertFailedException( "Invalid dimensions must not read values." );
		public IEnumerator<T> GetEnumerator() => throw new NotSupportedException(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private sealed class FailingCells : IReadOnlyList<byte>
	{
		public int Count => 2;
		public byte this[int index] => index == 0 ? (byte)255 : throw new IOException( "Synthetic raw input failure" );
		public IEnumerator<byte> GetEnumerator() => throw new NotSupportedException(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
