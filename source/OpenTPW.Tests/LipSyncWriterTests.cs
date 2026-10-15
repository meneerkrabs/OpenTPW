using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LipSyncWriterTests
{
	[TestMethod]
	public void EncodesCanonicalLittleEndianValuesAndPreservesTimelineEvents()
	{
		var marks = new uint[] { 0, 0x01020304, uint.MaxValue - 1 };
		var original = marks.ToArray();
		var encoded = LipSyncFile.Encode( marks );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 0, 0, 4, 3, 2, 1, 254, 255, 255, 255, 255, 255, 255, 255 }, encoded );
		var read = new LipSyncFile( new MemoryStream( encoded ) );
		CollectionAssert.AreEqual( original, read.Marks.ToArray() );
		CollectionAssert.AreEqual( encoded, LipSyncFile.Encode( read.Marks ) );
		CollectionAssert.AreEqual( original, marks );
		var before = new LipSyncTimeline( marks );
		var after = new LipSyncTimeline( read );
		CollectionAssert.AreEqual( before.TalkingIntervals.ToArray(), after.TalkingIntervals.ToArray() );
		foreach ( var time in new long[] { -1, 0, 1, 0x01020303, 0x01020304, uint.MaxValue - 2, uint.MaxValue - 1, long.MaxValue } )
		{
			Assert.AreEqual( before.IsTalking( time ), after.IsTalking( time ), $"event at {time}" );
			if ( time >= 0 && time <= uint.MaxValue )
				Assert.AreEqual( before.IsTalking( TimeSpan.FromTicks( time * 10 ) ), after.IsTalking( TimeSpan.FromTicks( time * 10 ) ) );
		}
	}

	[DataTestMethod]
	[DataRow( 0u )]
	[DataRow( uint.MaxValue - 1 )]
	public void SingleMarkIsReadable( uint mark )
	{
		var bytes = LipSyncFile.Encode( new[] { mark } );
		Assert.AreEqual( 8, bytes.Length );
		CollectionAssert.AreEqual( new[] { mark }, new LipSyncFile( new MemoryStream( bytes ) ).Marks.ToArray() );
	}

	[TestMethod]
	public void MaximumReadableOddCountRoundTripsWithoutAnAdditionalCap()
	{
		var marks = Enumerable.Range( 0, LipSyncFile.MaximumFileBytes / 4 - 1 ).Select( value => (uint)value ).ToArray();
		var bytes = LipSyncFile.Encode( marks );
		Assert.AreEqual( LipSyncFile.MaximumFileBytes, bytes.Length );
		CollectionAssert.AreEqual( marks, new LipSyncFile( new MemoryStream( bytes ) ).Marks.ToArray() );
	}

	[TestMethod]
	public void InvalidMarksCannotModifyOutputOrMoveItsPosition()
	{
		IReadOnlyList<uint>[] invalid = { Array.Empty<uint>(), new uint[] { 1, 2 }, new uint[] { 1, 1, 2 },
			new uint[] { 2, 1, 3 }, new uint[] { 1, 2, uint.MaxValue }, new uint[] { uint.MaxValue },
			new CountOnlyList( LipSyncFile.MaximumFileBytes / 4 + 1 ), new CountOnlyList( int.MaxValue ), new CountOnlyList( -1 ) };
		foreach ( var marks in invalid )
		{
			using var output = new MemoryStream();
			output.Write( new byte[] { 10, 20, 30, 40 } );
			output.Position = 2;
			Assert.ThrowsException<ArgumentException>( () => LipSyncFile.Write( output, marks ) );
			CollectionAssert.AreEqual( new byte[] { 10, 20, 30, 40 }, output.ToArray() );
			Assert.AreEqual( 2L, output.Position );
			Assert.IsTrue( output.CanWrite );
		}
	}

	[TestMethod]
	public void WritesAtCurrentPositionWithoutSeekingFlushingOrClosing()
	{
		using var output = new NonseekableOutput();
		output.WriteByte( 42 );
		LipSyncFile.Write( output, new uint[] { 0 } );
		CollectionAssert.AreEqual( new byte[] { 42, 0, 0, 0, 0, 255, 255, 255, 255 }, output.ToArray() );
		Assert.IsTrue( output.CanWrite );
		Assert.AreEqual( 0, output.FlushCalls );
	}

	[TestMethod]
	public void NullAndUnwritableOutputAreRefused()
	{
		Assert.ThrowsException<ArgumentNullException>( () => LipSyncFile.Encode( null! ) );
		Assert.ThrowsException<ArgumentNullException>( () => LipSyncFile.Write( null!, new uint[] { 0 } ) );
		using var unchanged = new MemoryStream();
		unchanged.WriteByte( 42 );
		Assert.ThrowsException<ArgumentNullException>( () => LipSyncFile.Write( unchanged, null! ) );
		CollectionAssert.AreEqual( new byte[] { 42 }, unchanged.ToArray() );
		using var output = new MemoryStream( new byte[] { 42 }, writable: false );
		Assert.ThrowsException<NotSupportedException>( () => LipSyncFile.Write( output, new uint[] { 0 } ) );
		CollectionAssert.AreEqual( new byte[] { 42 }, output.ToArray() );
	}

	[TestMethod]
	public void WriteFailurePropagatesWithoutClosingTheCallerStream()
	{
		using var output = new FailingOutput();
		Assert.ThrowsException<IOException>( () => LipSyncFile.Write( output, new uint[] { 0 } ) );
		Assert.IsTrue( output.CanWrite );
	}

	private sealed class FailingOutput : MemoryStream
	{
		public override void Write( ReadOnlySpan<byte> buffer ) => throw new IOException( "Synthetic write failure" );
	}

	private sealed class CountOnlyList( int count ) : IReadOnlyList<uint>
	{
		public int Count => count;
		public uint this[int index] => throw new AssertFailedException( "Oversized mark lists must be refused before reading items." );
		public IEnumerator<uint> GetEnumerator() => throw new NotSupportedException();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	private sealed class NonseekableOutput : MemoryStream
	{
		public NonseekableOutput() : base( 32 ) { }
		public int FlushCalls { get; private set; }
		public override bool CanSeek => false;
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override long Length => throw new NotSupportedException();
		public override void Flush() => FlushCalls++;
	}
}
