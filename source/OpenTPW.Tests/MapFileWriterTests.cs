using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class MapFileWriterTests
{
	private static readonly uint[] Words = { 0, 0x01020304, uint.MaxValue, 0x80000000, 7 };
	private static byte[] TitleSlot()
	{
		var slot = Enumerable.Range( 0, 32 ).Select( value => (byte)(128 + value) ).ToArray();
		slot[0] = (byte)'X'; slot[1] = 0; return slot;
	}
	private static byte[] Fixture( int width, int height, byte[] cells )
	{
		var bytes = new byte[80 + cells.Length]; "TP2M"u8.CopyTo( bytes ); TitleSlot().CopyTo( bytes, 4 );
		"MAP "u8.CopyTo( bytes.AsSpan( 36 ) ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 40 ), (uint)(28 + cells.Length) );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 44 ), (uint)width ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 48 ), (uint)height );
		for ( var i = 0; i < 5; i++ ) BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 52 + i * 4 ), Words[i] );
		cells.CopyTo( bytes, 72 ); "END "u8.CopyTo( bytes.AsSpan( 72 + cells.Length ) ); return bytes;
	}
	private static byte[] Encode( MapFile map ) => map.Encode();
	private static void Write( Stream output, int width, int height, IReadOnlyList<byte> title, IReadOnlyList<uint> opaque, IReadOnlyList<byte> cells ) =>
		MapFile.Write( output, width, height, title, opaque, cells );
	private static IReadOnlyList<byte> RawTitle( MapFile map ) => map.RawTitleSlot;

	[TestMethod]
	public void ParsedNonSquareMapPreservesEveryRawByteAndTitleIsCopiedReadOnly()
	{
		var cells = Enumerable.Range( 0, 256 ).Select( value => (byte)value ).ToArray();
		// Use a nonsquare map carrying all 256 bit patterns, plus a second cell row.
		cells = cells.Concat( cells ).ToArray(); var bytes = Fixture( 32, 16, cells ); var original = bytes.ToArray();
		var map = new MapFile( new MemoryStream( bytes ) ); var title = RawTitle( map ); bytes[6] = 1;
		Assert.AreEqual( "X", map.Title ); CollectionAssert.AreEqual( TitleSlot(), title.ToArray() );
		if ( title is IList<byte> list ) Assert.ThrowsException<NotSupportedException>( () => list[2] = 0 );
		CollectionAssert.AreEqual( original, Encode( map ) ); CollectionAssert.AreEqual( cells, map.Cells );
		Assert.AreEqual( cells[3 * 32 + 17], map.GetCellAt( 3, 17 ) );
	}
	[TestMethod]
	public void SingleCellEditChangesOnlyItsRawByteAndReReadsInXMajorOrder()
	{
		var original = Fixture( 3, 2, new byte[] { 0, 255, 128, 4, 3, 8 } ); var map = new MapFile( new MemoryStream( original ) );
		map.Cells[1 * 3 + 2] = 0xF7; var encoded = Encode( map );
		CollectionAssert.AreEqual( new[] { 77 }, Enumerable.Range( 0, encoded.Length ).Where( i => encoded[i] != original[i] ).ToArray() );
		var reread = new MapFile( new MemoryStream( encoded ) ); Assert.AreEqual( (byte)0xF7, reread.GetCellAt( 1, 2 ) );
		CollectionAssert.AreEqual( Words, reread.OpaqueHeaderValues.ToArray() ); CollectionAssert.AreEqual( TitleSlot(), RawTitle( reread ).ToArray() );
	}
	[TestMethod]
	public void InvalidShapeCountsAndTitleCannotWriteOrMoveOutput()
	{
		var cases = new (int Width, int Height, IReadOnlyList<byte> Title, IReadOnlyList<uint> Words, IReadOnlyList<byte> Cells)[] {
			(0,1,TitleSlot(),Words,new byte[1]),(1,-1,TitleSlot(),Words,new byte[1]),(1025,1,TitleSlot(),Words,new byte[1]),
			(int.MaxValue,int.MaxValue,TitleSlot(),Words,new CountOnly<byte>(int.MaxValue)),
			(1,1,new byte[31],Words,new byte[1]),(1,1,Enumerable.Repeat((byte)255,32).ToArray(),Words,new byte[1]),
			(1,1,TitleSlot(),new CountOnly<uint>(int.MaxValue),new byte[1]),(1,1,TitleSlot(),Array.Empty<uint>(),new byte[1]),
			(1,1,TitleSlot(),Words,new CountOnly<byte>(-1)),(1,1,TitleSlot(),Words,new CountOnly<byte>(int.MaxValue)),(2,2,TitleSlot(),Words,new byte[3])};
		foreach ( var item in cases )
		{
			using var output = new MemoryStream(); output.Write( new byte[] { 10, 20, 30, 40 } ); output.Position = 2;
			Assert.ThrowsException<ArgumentException>( () => Write( output, item.Width, item.Height, item.Title, item.Words, item.Cells ) );
			CollectionAssert.AreEqual( new byte[] { 10, 20, 30, 40 }, output.ToArray() ); Assert.AreEqual( 2L, output.Position ); Assert.IsTrue( output.CanWrite );
		}
	}
	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 31 )]
	public void EmptyOrFinalSlotTerminatedTitlePreservesHighBitsExactly( int terminator )
	{
		var title = Enumerable.Repeat( (byte)0xF1, 32 ).ToArray(); title[terminator] = 0;
		var bytes = MapFile.Encode( 1, 1, title, Words, new byte[] { 0xFF } ); var parsed = new MapFile( new MemoryStream( bytes ) );
		CollectionAssert.AreEqual( title, parsed.RawTitleSlot.ToArray() ); CollectionAssert.AreEqual( bytes, parsed.Encode() );
		Assert.AreEqual( terminator, parsed.Title.Length ); Assert.AreEqual( (byte)0xFF, parsed.GetCellAt( 0, 0 ) );
	}
	[TestMethod]
	public void InvalidShapeIsRejectedBeforeAllocatingMaximumGridPayload()
	{
		var title = TitleSlot(); using var output = new MemoryStream();
		Assert.ThrowsException<ArgumentException>( () => MapFile.Write( output, 1024, 1024, title, Words, Array.Empty<byte>() ) );
		var before = GC.GetAllocatedBytesForCurrentThread();
		try { MapFile.Write( output, 1024, 1024, title, Words, Array.Empty<byte>() ); Assert.Fail( "Invalid shape must be refused." ); }
		catch ( ArgumentException ) { }
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsTrue( allocated < 64 * 1024, $"Invalid shape allocated {allocated} bytes before refusal." ); Assert.AreEqual( 0L, output.Length );
	}
	[TestMethod]
	public void MaximumReadableGridIsWritableWithoutAdditionalCaps()
	{
		var cells = new byte[MapFile.MaximumCells]; cells[^1] = 255; using var output = new MemoryStream();
		Write( output, 1024, 1024, TitleSlot(), Words, cells ); Assert.AreEqual( 80 + MapFile.MaximumCells, output.Length );
		output.Position = 0; var read = new MapFile( output ); Assert.AreEqual( (byte)255, read.GetCellAt( 1023, 1023 ) );
	}
	[TestMethod]
	public void WritesAtCurrentPositionWithoutTruncatingFlushingOrClosing()
	{
		var bytes = Fixture( 3, 2, new byte[] { 0, 1, 2, 3, 4, 5 } ); var map = new MapFile( new MemoryStream( bytes ) );
		using var output = new TrackingOutput(); output.Write( new byte[bytes.Length + 10] ); output.Position = 4;
		map.Write( output ); Assert.AreEqual( bytes.Length + 10, output.Length );
		CollectionAssert.AreEqual( bytes, output.ToArray().AsSpan( 4, bytes.Length ).ToArray() ); Assert.AreEqual( 4L + bytes.Length, output.Position );
		Assert.AreEqual( 0, output.FlushCalls ); Assert.IsTrue( output.CanWrite );
	}
	[TestMethod]
	public void NonseekableOutputNeedsNoPositionOrLengthAndKeepsCallerOwnership()
	{
		using var output = new NonseekableOutput(); output.WriteByte( 42 ); Write( output, 1, 1, TitleSlot(), Words, new byte[] { 255 } );
		CollectionAssert.AreEqual( new byte[] { 42 }.Concat( Fixture( 1, 1, new byte[] { 255 } ) ).ToArray(), output.ToArray() );
		Assert.IsTrue( output.CanWrite ); Assert.AreEqual( 0, output.FlushCalls );
	}
	[TestMethod]
	public void NullAndUnwritableStreamsAreRefusedWithoutMutatingOutput()
	{
		Assert.ThrowsException<ArgumentNullException>( () => Write( null!, 1, 1, TitleSlot(), Words, new byte[1] ) );
		using var output = new MemoryStream( new byte[] { 42 }, false );
		Assert.ThrowsException<NotSupportedException>( () => Write( output, 1, 1, TitleSlot(), Words, new byte[1] ) ); CollectionAssert.AreEqual( new byte[] { 42 }, output.ToArray() );
		using var writable = new MemoryStream(); writable.WriteByte( 42 );
		Assert.ThrowsException<ArgumentNullException>( () => Write( writable, 1, 1, null!, Words, new byte[1] ) );
		Assert.ThrowsException<ArgumentNullException>( () => Write( writable, 1, 1, TitleSlot(), null!, new byte[1] ) );
		Assert.ThrowsException<ArgumentNullException>( () => Write( writable, 1, 1, TitleSlot(), Words, null! ) ); CollectionAssert.AreEqual( new byte[] { 42 }, writable.ToArray() );
	}
	[TestMethod]
	public void PartialIoFailurePropagatesAndKeepsStreamOpenWithoutClaimingRollback()
	{
		using var output = new PartialOutput(); var bytes = Fixture( 1, 1, new byte[] { 3 } );
		Assert.ThrowsException<IOException>( () => Write( output, 1, 1, TitleSlot(), Words, new byte[] { 3 } ) );
		CollectionAssert.AreEqual( bytes.AsSpan( 0, 7 ).ToArray(), output.ToArray() ); Assert.IsTrue( output.CanWrite ); Assert.AreEqual( 0, output.FlushCalls );
	}
	private class TrackingOutput : MemoryStream { public TrackingOutput() : base( 256 ) { } public int FlushCalls { get; private set; } public override void Flush() => FlushCalls++; }
	private sealed class NonseekableOutput : TrackingOutput
	{
		public override bool CanSeek => false; public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override long Length => throw new NotSupportedException();
	}
	private sealed class PartialOutput : TrackingOutput { public override void Write( ReadOnlySpan<byte> bytes ) { base.Write( bytes[..7] ); throw new IOException( "Synthetic partial write" ); } }
	private sealed class CountOnly<T>( int count ) : IReadOnlyList<T>
	{
		public int Count => count; public T this[int index] => throw new AssertFailedException( "Invalid counts must be checked before access or output allocation." );
		public IEnumerator<T> GetEnumerator() => throw new NotSupportedException(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
