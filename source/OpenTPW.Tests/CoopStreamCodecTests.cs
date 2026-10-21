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
		Refused( new CoopStreamFrame( 10, 1, new[] { fee with { Flags = (CoopRecordFlags)4 } }, none ), "an unknown flag" );
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
		version[0] = 2;
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
		new CoopResendRequest( 42, 50 ),
		new CoopResentFrames( new (ulong, ReadOnlyMemory<byte>)[] { (42, Sample().Encode()), (43, new byte[] { 1, 2, 3 }) } ),
		new CoopTooFarBack( 17 ),
		new CoopDesyncReport( 2, CoopDesyncKind.Digest, 600, 0xAAAA, 0xBBBB, 1, 77 ),
		new CoopDesyncSeen( 2 )
	};

	[TestMethod]
	public void EveryMessageReadsBackAndRefusesEveryTruncation()
	{
		foreach ( var message in Messages() )
		{
			var bytes = message.Encode();
			var read = CoopStreamMessage.Decode( bytes );
			Assert.AreEqual( message.GetType(), read.GetType() );
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
		progress[1] = 9;
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( progress ), "an unknown guest state" );
		var desync = new CoopDesyncReport( 1, CoopDesyncKind.Flags, 5, 0, 0, 1, 1 ).Encode();
		desync[5] = 0;
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( desync ), "an unknown desync kind" );
		var gap = new CoopResentFrames( new (ulong, ReadOnlyMemory<byte>)[] { (5, new byte[] { 1 }), (7, new byte[] { 1 }) } ).Encode();
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( gap ), "resent frames with a gap" );
		var zero = new CoopResentFrames( new (ulong, ReadOnlyMemory<byte>)[] { (0, new byte[] { 1 }) } ).Encode();
		Assert.ThrowsException<InvalidDataException>( () => CoopStreamMessage.Decode( zero ), "a resent frame 0" );
	}

	[TestMethod]
	public void AFrameAtTheSizeLimitFitsAResentFramesMessage()
	{
		var envelope = CoopResentFrames.EmptyBytes + CoopResentFrames.FrameOverhead + CoopStreamFrame.MaximumBytes;
		Assert.AreEqual( CoopLink.MaximumMessagePayload, envelope );
		Assert.IsTrue( CoopStreamFrame.MaximumBytes <= CoopLink.MaximumFramePayload );
		Assert.IsTrue( ParkCommandCodec.MaximumBytes + CoopStreamFrame.RecordOverhead + CoopStreamFrame.EmptyBytes
			+ CoopStreamFrame.MaximumCheckpoints * CoopStreamFrame.CheckpointBytes <= CoopStreamFrame.MaximumBytes, "the largest command always fits a frame" );
	}
}
