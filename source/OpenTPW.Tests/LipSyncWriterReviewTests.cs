using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LipSyncWriterReviewTests
{
	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( LipSyncFile.MaximumFileBytes / 4 - 1 )]
	public void ValidIndexedListsNeedNoEnumerationAndPreserveRawTimeline( int count )
	{
		var input = new IndexedMarks( count );
		var encoded = LipSyncFile.Encode( input );
		Assert.AreEqual( count, input.Reads );
		Assert.AreEqual( 1, input.CountReads );
		var expected = Enumerable.Range( 0, count ).Select( index => (uint)index ).ToArray();
		var file = new LipSyncFile( new MemoryStream( encoded ) );
		CollectionAssert.AreEqual( expected, file.Marks.ToArray() );
		var original = new LipSyncTimeline( expected );
		var restored = new LipSyncTimeline( file );
		foreach ( var mark in expected.Where( ( _, index ) => index % 127 == 0 ) )
			foreach ( var time in new[] { (long)mark - 1, (long)mark, (long)mark + 1 } )
				Assert.AreEqual( original.IsTalking( time ), restored.IsTalking( time ) );
	}

	[TestMethod]
	public void CurrentPositionWritePreservesPrefixAndTrailingBytesWithoutTruncation()
	{
		using var output = new MemoryStream();
		output.Write( Enumerable.Repeat( (byte)0xA5, 32 ).ToArray() );
		output.Position = 7;
		LipSyncFile.Write( output, new uint[] { 0x01020304 } );
		Assert.AreEqual( 15L, output.Position ); Assert.AreEqual( 32L, output.Length );
		var bytes = output.ToArray();
		Assert.IsTrue( bytes[..7].All( value => value == 0xA5 ) );
		Assert.IsTrue( bytes[15..].All( value => value == 0xA5 ) );
		CollectionAssert.AreEqual( new uint[] { 0x01020304 }, new LipSyncFile( new MemoryStream( bytes, 7, 8 ) ).Marks.ToArray() );
	}

	[TestMethod]
	public void LateInputFailureDoesNotCallTheOutputAndPartialIoFailurePropagates()
	{
		using var output = new PartialFailureOutput();
		var failure = Assert.ThrowsException<IOException>( () => LipSyncFile.Write( output, new IndexedMarks( 3, failAt: 2 ) ) );
		Assert.AreEqual( "Input failed", failure.Message );
		Assert.AreEqual( 0, output.Writes ); Assert.AreEqual( 0L, output.Length );
		failure = Assert.ThrowsException<IOException>( () => LipSyncFile.Write( output, new uint[] { 0 } ) );
		Assert.AreEqual( "Output failed", failure.Message );
		Assert.AreEqual( 1, output.Writes ); Assert.AreEqual( 3L, output.Length );
		Assert.IsTrue( output.CanWrite );
	}

	private sealed class IndexedMarks( int count, int failAt = -1 ) : IReadOnlyList<uint>
	{
		public int Reads { get; private set; }
		public int CountReads { get; private set; }
		public int Count { get { CountReads++; return count; } }
		public uint this[int index] { get { Reads++; return index == failAt ? throw new IOException( "Input failed" ) : (uint)index; } }
		public IEnumerator<uint> GetEnumerator() => throw new AssertFailedException( "Encoding must not enumerate or copy an unbounded source." );
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	private sealed class PartialFailureOutput : MemoryStream
	{
		public int Writes { get; private set; }
		public override void Write( ReadOnlySpan<byte> buffer )
		{
			Writes++;
			base.Write( buffer[..3] );
			throw new IOException( "Output failed" );
		}
	}
}
