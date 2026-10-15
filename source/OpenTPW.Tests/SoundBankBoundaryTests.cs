using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SoundBankBoundaryTests
{
	private static byte[] Words( params int[] values )
	{
		var bytes = new byte[values.Length * 4];
		for ( var index = 0; index < values.Length; index++ )
			BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( index * 4 ), values[index] );
		return bytes;
	}

	private static byte[] SmallBank()
	{
		var bytes = new byte[96];
		Words( 2, 12, 56 ).CopyTo( bytes, 0 );
		Words( 40, 4 ).CopyTo( bytes, 12 );
		"first.mp2"u8.CopyTo( bytes.AsSpan( 20 ) );
		new byte[] { 1, 2, 3, 4 }.CopyTo( bytes, 52 );
		Words( 40, 0 ).CopyTo( bytes, 56 );
		"empty.mp2"u8.CopyTo( bytes.AsSpan( 64 ) );
		return bytes;
	}

	[TestMethod]
	public void FragmentedFreshStreamsPreserveEntriesAndDisposeEveryRead()
	{
		var bytes = SmallBank();
		var opened = 0;
		var disposed = 0;
		var bank = new SoundBank( () => { opened++; return new ShortBankStream( bytes, bytes.Length, () => disposed++ ); }, bytes.Length );
		Assert.AreEqual( 2, opened, "Only count and table are read during construction." );
		Assert.AreEqual( 2, disposed );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3, 4 }, bank.GetMpeg( 1 ) );
		Assert.AreEqual( "empty.mp2", bank.GetName( 2 ) );
		CollectionAssert.AreEqual( Array.Empty<byte>(), bank.GetMpeg( 2 ) );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3, 4 }, bank.GetMpeg( 1 ) );
		Assert.AreEqual( "first.mp2", bank.GetName( 1 ) );
		Assert.IsNull( bank.GetName( 0 ) );
		Assert.IsNull( bank.GetMpeg( uint.MaxValue ) );
		Assert.AreEqual( opened, disposed, "No stream survives a lookup." );
	}

	[TestMethod]
	public void EveryTruncatedPrefixAndExtremeEntryClaimAgreesBetweenSources()
	{
		static void Compare( byte[] bytes )
		{
			var memory = new SoundBank( bytes );
			var streamed = new SoundBank( () => new ShortBankStream( bytes, bytes.Length, () => { } ), bytes.Length );
			Assert.AreEqual( memory.Count, streamed.Count );
			for ( uint number = 0; number <= memory.Count + 1; number++ )
			{
				CollectionAssert.AreEqual( memory.GetMpeg( number ), streamed.GetMpeg( number ) );
				Assert.AreEqual( memory.GetName( number ), streamed.GetName( number ) );
			}
		}
		var valid = SmallBank();
		for ( var prefix = 0; prefix <= valid.Length; prefix++ )
			Compare( valid[..prefix] );
		foreach ( var extreme in new[] { int.MinValue, -1, 0, int.MaxValue } )
			foreach ( var field in new[] { 0, 4, 12, 16 } )
			{
				var damaged = (byte[])valid.Clone();
				BinaryPrimitives.WriteInt32LittleEndian( damaged.AsSpan( field ), extreme );
				Compare( damaged );
			}
	}

	[TestMethod]
	public void ShortActualInputRefusesCountTableHeaderAndPayloadAndDisposesStreams()
	{
		var bytes = SmallBank();
		foreach ( var actualLength in new[] { 2, 6, 17, 54 } )
		{
			var opened = 0;
			var disposed = 0;
			Assert.ThrowsException<EndOfStreamException>( () =>
			{
				var bank = new SoundBank( () => { opened++; return new ShortBankStream( bytes[..actualLength], bytes.Length, () => disposed++ ); }, bytes.Length );
				bank.GetMpeg( 1 );
			} );
			Assert.AreEqual( opened, disposed, $"Actual length {actualLength}" );
		}
	}

	[TestMethod]
	public void NamesRefuseOffsetsInsideTheBankTable()
	{
		foreach ( var offset in new[] { 0, 4, 7 } )
		{
			var bytes = new byte[80];
			Words( 1, offset ).CopyTo( bytes, 0 );
			foreach ( var bank in new[] { new SoundBank( bytes ), new SoundBank( () => new MemoryStream( bytes ), bytes.Length ) } )
			{
				Assert.IsNull( bank.GetMpeg( 1 ) );
				Assert.IsNull( bank.GetName( 1 ), $"offset {offset}" );
			}
		}
	}

	[TestMethod]
	public void AValidSparseBankCanReadPastTheSignedOffsetRange()
	{
		const int offset = int.MaxValue - 16;
		var payloadOffset = (long)offset + 40;
		var payload = new byte[] { 1, 2, 3, 4 };
		var segments = new Dictionary<long, byte[]> { [0] = Words( 1, offset ), [offset] = Words( 40, payload.Length ), [payloadOffset] = payload };
		var disposed = 0;
		var bank = new SoundBank( () => new SparseBankStream( payloadOffset + payload.Length, segments, () => disposed++ ), payloadOffset + payload.Length );
		CollectionAssert.AreEqual( payload, bank.GetMpeg( 1 ) );
		Assert.AreEqual( 4, disposed, "Each table/header/payload read owns its fresh stream." );
	}

	[TestMethod]
	public void AValidSparseBankCanReadANamePastTheSignedOffsetRange()
	{
		const int offset = int.MaxValue;
		var segments = new Dictionary<long, byte[]> { [0] = Words( 1, offset ), [offset] = Words( 40, 0 ), [(long)offset + 8] = "large.mp2"u8.ToArray() };
		var length = (long)offset + 40;
		var bank = new SoundBank( () => new SparseBankStream( length, segments, () => { } ), length );
		Assert.AreEqual( "large.mp2", bank.GetName( 1 ) );
	}

	[TestMethod]
	public void AReplacedBankIsRefusedBeforeAllocatingTheClaimedPayload()
	{
		const int payloadBytes = 64 * 1024 * 1024;
		const long bankLength = 80L + payloadBytes;
		var segments = new Dictionary<long, byte[]> { [0] = Words( 1, 40 ), [40] = Words( 40, payloadBytes ) };
		var opened = 0;
		var disposed = 0;
		var bank = new SoundBank( () => new SparseBankStream( ++opened <= 3 ? bankLength : 128, segments, () => disposed++ ), bankLength );
		var before = GC.GetAllocatedBytesForCurrentThread();
		Assert.ThrowsException<IOException>( () => bank.GetMpeg( 1 ) );
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsTrue( allocated < 1024 * 1024, $"Changed bank allocated {allocated} bytes before refusal." );
		Assert.AreEqual( 4, disposed );
	}

	/// <summary>Small synthetic sections in a large logical bank; reads are deliberately fragmented.</summary>
	private sealed class SparseBankStream( long length, Dictionary<long, byte[]> segments, Action disposed ) : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => length;
		public override long Position { get; set; }
		public override int Read( byte[] buffer, int offset, int count ) => Read( buffer.AsSpan( offset, count ) );
		public override int Read( Span<byte> buffer )
		{
			if ( Position < 0 )
				throw new IOException( "Negative bank position." );
			var count = (int)Math.Min( Math.Min( buffer.Length, 3 ), Math.Max( 0, length - Position ) );
			for ( var index = 0; index < count; index++ )
			{
				buffer[index] = 0;
				foreach ( var segment in segments )
					if ( Position + index >= segment.Key && Position + index - segment.Key < segment.Value.Length )
						buffer[index] = segment.Value[(int)(Position + index - segment.Key)];
			}
			Position += count;
			return count;
		}
		public override long Seek( long offset, SeekOrigin origin ) => Position = origin switch
		{
			SeekOrigin.Begin => offset,
			SeekOrigin.Current => Position + offset,
			SeekOrigin.End => length + offset,
			_ => throw new ArgumentOutOfRangeException( nameof( origin ) )
		};
		protected override void Dispose( bool disposing ) { if ( disposing ) disposed(); base.Dispose( disposing ); }
		public override void Flush() => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
	}

	private sealed class ShortBankStream( byte[] bytes, long advertisedLength, Action disposed ) : MemoryStream( bytes, writable: false )
	{
		public override long Length => advertisedLength;
		public override int Read( Span<byte> buffer ) => base.Read( buffer[..Math.Min( 3, buffer.Length )] );
		public override int Read( byte[] buffer, int offset, int count ) => base.Read( buffer, offset, Math.Min( 3, count ) );
		protected override void Dispose( bool disposing ) { if ( disposing ) disposed(); base.Dispose( disposing ); }
	}
}
