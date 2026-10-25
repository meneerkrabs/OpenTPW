using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-lockstep] The host's stream frames and the members' stream messages (docs/COOP.md, "Sessions"): every form reads back, and every truncation and malformed form is
/// refused with <see cref="InvalidDataException"/>, so a guest turns a broken frame into a protocol desync instead of an exception in its pump.
/// </summary>
[TestClass]
public class CoopStreamCodecTests
{
	private static CoopStreamRecord Record( long boundary, CoopRecordFlags flags, ParkCommand command ) => new( boundary, flags, ParkCommandCodec.Encode( command ) );

	private static CoopStreamFrame Sample() => new( 130, 7,
		new[]
		{
			Record( 120, CoopRecordFlags.Accepted, new SetEntranceFeeCommand( 5 ) ),
			Record( 120, CoopRecordFlags.None, new SetGameSpeedCommand( GameSpeed.Fast ) ),
			Record( 130, CoopRecordFlags.Errored, new RemoveAtCommand( 3, 4 ) )
		},
		new[]
		{
			new CoopCheckpoint( CoopCheckpointKind.AfterTick, 120, 0x0123456789ABCDEFUL ),
			new CoopCheckpoint( CoopCheckpointKind.AfterRecord, 9, 42 )
		} );

	[TestMethod]
	public void AFrameReadsBack()
	{
		var frame = Sample();
		var bytes = frame.Encode();
		Assert.AreEqual( frame.Size, bytes.Length );
		var read = CoopStreamFrame.Decode( bytes );
		Assert.AreEqual( 130, read.Horizon );
		Assert.AreEqual( 7, read.First );
		Assert.AreEqual( 3, read.Records.Count );
		for ( var index = 0; index < 3; index++ )
		{
			Assert.AreEqual( frame.Records[index].Boundary, read.Records[index].Boundary );
			Assert.AreEqual( frame.Records[index].Flags, read.Records[index].Flags );
			CollectionAssert.AreEqual( frame.Records[index].Bytes.ToArray(), read.Records[index].Bytes.ToArray() );
		}
		CollectionAssert.AreEqual( frame.Checkpoints.ToArray(), read.Checkpoints.ToArray() );
		Assert.AreEqual( new SetGameSpeedCommand( GameSpeed.Fast ), ParkCommandCodec.Decode( read.Records[1].Bytes.Span ) );

		var empty = new CoopStreamFrame( 5, 1, Array.Empty<CoopStreamRecord>(), Array.Empty<CoopCheckpoint>() ).Encode();
		Assert.AreEqual( CoopStreamFrame.EmptyBytes, empty.Length );
		Assert.AreEqual( 5, CoopStreamFrame.Decode( empty ).Horizon );
	}

	[TestMethod]
	public void EveryTruncatedFrameIsRefused()
	{
		var bytes = Sample().Encode();
		for ( var length = 0; length < bytes.Length; length++ )
			Assert.ThrowsException<InvalidDataException>( () => CoopStreamFrame.Decode( bytes.AsSpan( 0, length ) ), $"a frame cut to {length} bytes" );
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamFrame.Decode( bytes.Concat( new byte[] { 0 } ).ToArray() ), "a byte after the fields" );
	}

	private static void Refused( CoopStreamFrame frame, string what ) => Assert.ThrowsException<InvalidDataException>( () => CoopStreamFrame.Decode( frame.Encode() ), what );

	[TestMethod]
	public void AMalformedFrameIsRefused()
	{
		var fee = Record( 10, CoopRecordFlags.Accepted, new SetEntranceFeeCommand( 1 ) );
		var none = Array.Empty<CoopCheckpoint>();
		Refused( new CoopStreamFrame( 10, 0, new[] { fee }, none ), "records numbered from 0" );
		Refused( new CoopStreamFrame( 10, 1, new[] { fee with { Flags = (CoopRecordFlags)8 } }, none ), "an unknown flag" );
		Refused( new CoopStreamFrame( 10, 1, new[] { fee with { Flags = CoopRecordFlags.Accepted | CoopRecordFlags.Errored } }, none ), "accepted and errored" );
		Refused( new CoopStreamFrame( 10, 1, new[] { fee with { Bytes = Array.Empty<byte>() } }, none ), "a record without a command" );
		Refused( new CoopStreamFrame( 10, 1, new[] { fee, fee with { Boundary = 9 } }, none ), "a falling boundary" );
		Refused( new CoopStreamFrame( 9, 1, new[] { fee }, none ), "a record past the horizon" );
		Refused( new CoopStreamFrame( 10, 1, new[] { fee }, new[] { new CoopCheckpoint( CoopCheckpointKind.AfterTick, 0, 1 ) } ), "a checkpoint at tick 0" );
		Refused( new CoopStreamFrame( 10, 1, new[] { fee }, new[] { new CoopCheckpoint( CoopCheckpointKind.AfterTick, 8, 1 ), new CoopCheckpoint( CoopCheckpointKind.AfterTick, 8, 2 ) } ),
			"tick checkpoints out of order" );
		Refused( new CoopStreamFrame( 10, 1, new[] { fee }, new[] { new CoopCheckpoint( CoopCheckpointKind.AfterRecord, 2, 1 ) } ), "a record checkpoint for a record of another frame" );
		Refused( new CoopStreamFrame( 10, 5, new[] { fee }, new[] { new CoopCheckpoint( CoopCheckpointKind.AfterRecord, 4, 1 ) } ), "a record checkpoint before the frame's records" );
		Refused( new CoopStreamFrame( 10, 1, new[] { fee }, new[] { new CoopCheckpoint( (CoopCheckpointKind)3, 5, 1 ) } ), "an unknown checkpoint kind" );

		var bytes = new CoopStreamFrame( 10, 1, new[] { fee }, none ).Encode();
		var version = (byte[])bytes.Clone();
		version[0] = CoopStreamFrame.Version + 1;
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamFrame.Decode( version ), "another version" );
		var huge = (byte[])bytes.Clone();
		huge[8] = 0x80;
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamFrame.Decode( huge ), "a horizon past the signed range" );
		// both kinds together, each in its own order, read back
		var mixed = new CoopStreamFrame( 10, 1, new[] { fee, fee }, new[]
		{
			new CoopCheckpoint( CoopCheckpointKind.AfterRecord, 2, 1 ),
			new CoopCheckpoint( CoopCheckpointKind.AfterTick, 9, 2 ),
			new CoopCheckpoint( CoopCheckpointKind.AfterTick, 10, 3 )
		} );
		Assert.AreEqual( 3, CoopStreamFrame.Decode( mixed.Encode() ).Checkpoints.Count );
	}

	private static IEnumerable<CoopStreamMessage> Messages() => new CoopStreamMessage[]
	{
		new CoopProgress( CoopGuestState.Gap, 41, 9000, 3 ),
		new CoopResendRequest( 42, 50 ) { Generation = 3 },
		new CoopResentFrames( new (ulong, ReadOnlyMemory<byte>)[] { (42, Sample().Encode()), (43, new byte[] { 1, 2, 3 }) } ),
		new CoopTooFarBack( 17 ) { Generation = 4 },
		new CoopDesyncReport( 2, CoopDesyncKind.Digest, 600, 0xAAAA, 0xBBBB, 1, 77 ),
		new CoopDesyncSeen( 2 ),
		new CoopSnapshotAssigned( 87 ) { Generation = 5 },
		new CoopSnapshotRequest( 4, CoopSnapshotReason.Desync, new CoopDesyncReport( 2, CoopDesyncKind.Flags, 600, 0, 0, 1, 77 ) ) { Generation = 4 },
		new CoopSnapshotRequest( 0, CoopSnapshotReason.Join, null ),
		new CoopSnapshotReady( 120, 3600, 88, 0xAAAA, 0xBBBB, new string( 'a', 64 ), 12345 ) { Generation = 5 },
		new CoopSnapshotFailed( CoopSnapshotFailure.Busy, false, 30 ) { Generation = 5 },
		new CoopGuestCommand( 7, ParkCommandCodec.Encode( new LayLineCommand( CellToolMode.Path, -1, 2, 3, 9, 3 ) ) ) { Generation = 2 },
		new CoopGuestCommandAnswer( 7, 913, null, 8 ) { Generation = 2 },
		new CoopGuestCommandAnswer( 9, 0, CoopGuestRefusal.Gap, 8 )
	};

	[TestMethod]
	public void EveryMessageReadsBackAndRefusesEveryTruncation()
	{
		foreach ( var message in Messages() )
		{
			var bytes = message.Encode();
			var read = CoopStreamMessage.Decode( bytes );
			Assert.AreEqual( (message.GetType(), message.Generation), (read.GetType(), read.Generation), $"{message.Type} reads back, its generation included" );
			CollectionAssert.AreEqual( bytes, read.Encode(), $"{message.Type} reads back to the same bytes" );
			for ( var length = 0; length < bytes.Length; length++ )
				Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( bytes.AsSpan( 0, length ) ), $"{message.Type} cut to {length} bytes" );
			Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( bytes.Concat( new byte[] { 0 } ).ToArray() ), $"{message.Type} with a byte after its fields" );
		}
		var resent = (CoopResentFrames)CoopStreamMessage.Decode( Messages().OfType<CoopResentFrames>().Single().Encode() );
		Assert.AreEqual( 43UL, resent.Frames[1].Seq );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, resent.Frames[1].Payload.ToArray() );
	}

	[TestMethod]
	public void AMalformedMessageIsRefused()
	{
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( new byte[] { 99 } ), "an unknown tag" );
		var progress = new CoopProgress( CoopGuestState.Following, 1, 2, 0 ).Encode();
		progress[5] = 9;
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( progress ), "an unknown guest state" );
		var desync = new CoopDesyncReport( 1, CoopDesyncKind.Flags, 5, 0, 0, 1, 1 ).Encode();
		desync[9] = 0;
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( desync ), "an unknown desync kind" );
		var gap = new CoopResentFrames( new (ulong, ReadOnlyMemory<byte>)[] { (5, new byte[] { 1 }), (7, new byte[] { 1 }) } ).Encode();
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( gap ), "resent frames with a gap" );
		var zero = new CoopResentFrames( new (ulong, ReadOnlyMemory<byte>)[] { (0, new byte[] { 1 }) } ).Encode();
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( zero ), "a resent frame 0" );
		var reason = new CoopSnapshotRequest( 0, CoopSnapshotReason.Join, null ).Encode();
		reason[9] = 0;
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( reason ), "an unknown snapshot reason" );
		var failure = new CoopSnapshotFailed( CoopSnapshotFailure.Busy, false, 1 ).Encode();
		failure[5] = 9;
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( failure ), "an unknown snapshot failure" );
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( new CoopSnapshotReady( 1, 1, 1, 0, 0, new string( 'G', 64 ), 1 ).Encode() ), "a digest that is no SHA-256" );
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( new CoopSnapshotReady( 1, 1, 1, 0, 0, new string( 'a', 64 ), 0 ).Encode() ), "an empty snapshot" );
		// [EXT:online-coop-commands] a guest's request is numbered from 1; an answer is a record or a known refusal, never both or neither
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( new CoopGuestCommand( 0, new byte[] { 1 } ).Encode() ), "a request numbered 0" );
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( new CoopGuestCommandAnswer( 0, 5, null, 1 ).Encode() ), "an answer numbered 0" );
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( new CoopGuestCommandAnswer( 3, 5, CoopGuestRefusal.TooFast, 4 ).Encode() ),
			"an answer with a record and a refusal" );
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( new CoopGuestCommandAnswer( 3, 0, null, 4 ).Encode() ), "an answer with neither" );
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( new CoopGuestCommandAnswer( 3, 0, (CoopGuestRefusal)8, 4 ).Encode() ), "an unknown refusal" );
	}

	/// <summary>[EXT:online-coop-commands] A guest's record carries its origin after its flags; the flags alone never claim one, and an origin needs a request number.</summary>
	[TestMethod]
	public void AGuestsRecordCarriesItsOrigin()
	{
		var line = Record( 40, CoopRecordFlags.Accepted, new LayLineCommand( CellToolMode.Path, -1, 2, 3, 9, 3 ) ) with { Origin = new CoopRecordOrigin( 12, 3 ) };
		var host = Record( 40, CoopRecordFlags.None, new SetEntranceFeeCommand( 5 ) );
		var frame = new CoopStreamFrame( 41, 9, new[] { host, line, host with { Flags = CoopRecordFlags.Accepted | CoopRecordFlags.Guest } }, Array.Empty<CoopCheckpoint>() );
		var bytes = frame.Encode();
		Assert.AreEqual( frame.Size, bytes.Length );
		Assert.AreEqual( CoopStreamFrame.EmptyBytes + 3 * CoopStreamFrame.RecordOverhead + CoopStreamFrame.OriginBytes + frame.Records.Sum( record => record.Bytes.Length ), bytes.Length,
			"the origin's bytes are the guest's record's alone" );
		var read = CoopStreamFrame.Decode( bytes );
		Assert.IsNull( read.Records[0].Origin );
		Assert.AreEqual( new CoopRecordOrigin( 12, 3 ), read.Records[1].Origin );
		Assert.AreEqual( CoopRecordFlags.Accepted, read.Records[1].Flags, "the guest flag stays in the encoding" );
		Assert.IsNull( read.Records[2].Origin, "a guest flag without an origin is not written" );
		Assert.AreEqual( CoopRecordFlags.Accepted, read.Records[2].Flags );
		Refused( new CoopStreamFrame( 41, 9, new[] { line with { Origin = new CoopRecordOrigin( 12, 0 ) } }, Array.Empty<CoopCheckpoint>() ), "an origin without a request number" );
		for ( var length = 0; length < bytes.Length; length++ )
			Assert.ThrowsException<InvalidDataException>( () => CoopStreamFrame.Decode( bytes.AsSpan( 0, length ) ), $"a frame with an origin cut to {length} bytes" );
	}

	[TestMethod]
	public void AFrameAtTheSizeLimitFitsAResentFramesMessage()
	{
		var envelope = CoopResentFrames.EmptyBytes + CoopResentFrames.FrameOverhead + CoopStreamFrame.MaximumBytes;
		Assert.AreEqual( CoopLink.MaximumMessagePayload, envelope );
		Assert.IsTrue( CoopStreamFrame.MaximumBytes <= CoopLink.MaximumFramePayload );
		Assert.IsTrue( ParkCommandCodec.MaximumBytes + CoopStreamFrame.RecordOverhead + CoopStreamFrame.OriginBytes + CoopStreamFrame.EmptyBytes
			+ CoopStreamFrame.MaximumCheckpoints * CoopStreamFrame.CheckpointBytes <= CoopStreamFrame.MaximumBytes, "the largest command always fits a frame, a guest's origin included" );
	}
}
