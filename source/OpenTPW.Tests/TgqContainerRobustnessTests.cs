using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class TgqContainerRobustnessTests
{
	private static byte[] Chunk( string fourCC, byte[] payload )
	{
		var chunk = new byte[8 + payload.Length];
		Encoding.ASCII.GetBytes( fourCC ).CopyTo( chunk, 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( chunk.AsSpan( 4 ), (uint)chunk.Length );
		payload.CopyTo( chunk, 8 );
		return chunk;
	}

	private static byte[] Movie( byte[]? extraTag = null, bool malformedSecondBlock = false, bool laterFrameFailure = false )
	{
		// Four zero-differential luma blocks and two chroma blocks, each ending with EOB.
		var frame = new byte[12];
		BinaryPrimitives.WriteUInt16LittleEndian( frame, 16 );
		BinaryPrimitives.WriteUInt16LittleEndian( frame.AsSpan( 2 ), 16 );
		var bits = "1001010010100101001000100010".PadRight( 32, '0' );
		BinaryPrimitives.WriteUInt32LittleEndian( frame.AsSpan( 8 ), Convert.ToUInt32( bits, 2 ) );
		var samples = laterFrameFailure ? (byte)112 : malformedSecondBlock ? (byte)56 : (byte)28;
		var patch = new byte[] { (byte)'P', (byte)'T', 0, 0, 0x82, 1, 2, 0x85, 1, samples }
			.Concat( extraTag ?? new byte[] { 0x1b, 1, 30 } ).Concat( new byte[] { 0xff, 0, 0 } ).ToArray();
		var count = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian( count, malformedSecondBlock ? 2u : 1u );
		var audio = new byte[laterFrameFailure ? 74 : 44];
		BinaryPrimitives.WriteUInt32LittleEndian( audio, laterFrameFailure ? 56u : 28u );
		audio[13] = 0xcc;
		var chunks = Chunk( "SCHl", patch ).Concat( Chunk( "pIQT", frame ) ).Concat( Chunk( "SCCl", count ) ).Concat( Chunk( "SCDl", audio ) );
		if ( malformedSecondBlock )
		{
			var malformed = (byte[])audio.Clone();
			if ( laterFrameFailure )
			{
				audio.CopyTo( malformed, 0 );
				BinaryPrimitives.WriteUInt32LittleEndian( malformed, 56 );
				malformed[14] = 0x77; // Dirty the reusable PCM cache before the later predictor failure.
			}
			malformed[laterFrameFailure ? 42 : 12] = 0x40; // Valid encoded length, unsupported left predictor.
			chunks = chunks.Concat( Chunk( "SCDl", malformed ) );
		}
		return chunks.Concat( Chunk( "SCEl", Array.Empty<byte>() ) ).ToArray();
	}

	[DataTestMethod]
	[DataRow( (byte)0x1b )]
	[DataRow( (byte)0x80 )]
	[DataRow( (byte)0x83 )]
	public void UnsignedPatchNumbersCannotWrapIntoNegativeMetadata( byte tag )
	{
		foreach ( var number in new[] { 0x80000000u, uint.MaxValue } )
		{
			var entry = new byte[6]; entry[0] = tag; entry[1] = 4;
			BinaryPrimitives.WriteUInt32BigEndian( entry.AsSpan( 2 ), number );
			Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( entry ) ) ) );
		}
	}

	[DataTestMethod]
	[DataRow( (byte)0x1b )]
	[DataRow( (byte)0x80 )]
	[DataRow( (byte)0x83 )]
	public void LargestRepresentablePatchNumbersRemainLiteralMetadata( byte tag )
	{
		var entry = new byte[6]; entry[0] = tag; entry[1] = 4;
		BinaryPrimitives.WriteUInt32BigEndian( entry.AsSpan( 2 ), int.MaxValue );
		var movie = new TgqMovieFile( new MemoryStream( Movie( entry ) ) );
		var actual = tag switch { 0x1b => movie.AudioHeader.FrameRate, 0x80 => movie.AudioHeader.CompressionRevision, _ => movie.AudioHeader.Compression };
		Assert.AreEqual( int.MaxValue, actual );
	}

	[TestMethod]
	public void FailedAudioDecodeDoesNotSkipTheMalformedBlockOnRetry()
	{
		var movie = new TgqMovieFile( new MemoryStream( Movie( malformedSecondBlock: true ) ) );
		var reader = new TgqAudioReader( movie );
		var output = new short[56];
		Assert.AreEqual( 28, reader.Read( output ) );
		Assert.AreEqual( 28L, reader.Position );
		Assert.ThrowsException<InvalidDataException>( () => reader.Read( output ) );
		Assert.AreEqual( 28L, reader.Position );
		Assert.IsFalse( reader.EndOfStream );
		Assert.ThrowsException<InvalidDataException>( () => reader.Read( output ) );
		Assert.AreEqual( 28L, reader.Position );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FailedReadRetriesItsDiscardedPrefixFromTheOriginalCursor( bool laterFrameFailure )
	{
		var movie = new TgqMovieFile( new MemoryStream( Movie( malformedSecondBlock: true, laterFrameFailure: laterFrameFailure ) ) );
		var reader = new TgqAudioReader( movie );
		Assert.AreEqual( 5, reader.Read( new short[10] ) );
		var expected = new short[movie.GetAudioBlockSampleCount( 0 ) * 2]; movie.DecodeAudioBlock( 0, expected );
		for ( var attempt = 0; attempt < 2; attempt++ )
		{
			var output = Enumerable.Repeat( short.MinValue, expected.Length + 56 ).ToArray();
			Assert.ThrowsException<InvalidDataException>( () => reader.Read( output ) );
			Assert.AreEqual( 5L, reader.Position );
			Assert.IsFalse( reader.EndOfStream );
			CollectionAssert.AreEqual( expected.Skip( 10 ).ToArray(), output.Take( expected.Length - 10 ).ToArray(), "retry must copy the same valid prefix that the caller discarded on failure" );
		}
	}

	[TestMethod]
	public void FailedReadWithoutACachedBlockRetriesAllPreviouslyCopiedBlocks()
	{
		var movie = new TgqMovieFile( new MemoryStream( Movie( malformedSecondBlock: true, laterFrameFailure: true ) ) );
		var reader = new TgqAudioReader( movie );
		var expected = new short[movie.GetAudioBlockSampleCount( 0 ) * 2]; movie.DecodeAudioBlock( 0, expected );
		for ( var attempt = 0; attempt < 2; attempt++ )
		{
			var output = Enumerable.Repeat( short.MinValue, expected.Length + 56 ).ToArray();
			Assert.ThrowsException<InvalidDataException>( () => reader.Read( output ) );
			Assert.AreEqual( 0L, reader.Position );
			CollectionAssert.AreEqual( expected, output.Take( expected.Length ).ToArray() );
		}
	}

	[TestMethod]
	public void VariableWidthIntegersAndExtendedLengthsRemainBigEndianMetadata()
	{
		foreach ( var bytes in new[] { new byte[] { 0x7f }, new byte[] { 0x12, 0x34 }, new byte[] { 1, 2, 3 }, new byte[] { 0x7f, 0xff, 0xff, 0xff } } )
		{
			var expected = bytes.Aggregate( 0, ( number, part ) => (number << 8) | part );
			foreach ( var extended in new[] { false, true } )
			{
				var prefix = extended ? new byte[] { 0x1b, 0xff, 0, 0, 0, (byte)bytes.Length } : new byte[] { 0x1b, (byte)bytes.Length };
				var movie = new TgqMovieFile( new MemoryStream( Movie( prefix.Concat( bytes ).ToArray() ) ) );
				Assert.AreEqual( expected, movie.AudioHeader.FrameRate );
				CollectionAssert.AreEqual( bytes, movie.AudioHeader.Tags.Single( tag => tag.Tag == 0x1b ).Value );
			}
		}
		var platform = Movie(); platform[10] = 0x34; platform[11] = 0x12;
		Assert.AreEqual( 0x1234, new TgqMovieFile( new MemoryStream( platform ) ).AudioHeader.Platform, "PT platform remains little endian" );
	}

	[TestMethod]
	public void UnknownTagsAndNoDataTagsPreserveTheirPayloadsAndOrder()
	{
		var tags = new byte[] { 0xfc, 0xfd, 0xfe, 0x99, 0, 0x99, 0xff, 0, 0, 0, 3, 0xff, 0xfe, 0x80 };
		var movie = new TgqMovieFile( new MemoryStream( Movie( tags ) ) );
		var extra = movie.AudioHeader.Tags.Skip( 2 ).ToArray();
		CollectionAssert.AreEqual( new byte[] { 0xfc, 0xfd, 0xfe, 0x99, 0x99 }, extra.Select( tag => tag.Tag ).ToArray() );
		Assert.IsTrue( extra.Take( 4 ).All( tag => tag.Value.Length == 0 ) );
		CollectionAssert.AreEqual( new byte[] { 0xff, 0xfe, 0x80 }, extra[4].Value );
		Assert.IsNull( movie.AudioHeader.FrameRate );
		Assert.AreEqual( 7, movie.AudioHeader.Compression );
		Assert.IsNull( movie.AudioHeader.CompressionRevision );
		Assert.AreEqual( 28, new TgqAudioReader( movie ).Read( new short[56] ) );
	}

	[TestMethod]
	public void NumericDuplicatesEmptyOrOversizedValuesAndMalformedLengthsRefuse()
	{
		foreach ( var extra in new[]
		{
			new byte[] { 0x1b, 0 }, new byte[] { 0x1b, 5, 0, 0, 0, 0, 1 },
			new byte[] { 0x1b, 1, 30, 0x1b, 1, 30 },
			new byte[] { 0x99, 0xff, 0xff, 0xff, 0xff, 0xff },
			new byte[] { 0x99, 0xff, 0, 0, 1, 0 },
			new byte[] { 0x99, 0xff }
		} ) Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( extra ) ) ) );
		var nonzeroPadding = Movie();
		var headerBytes = BinaryPrimitives.ReadInt32LittleEndian( nonzeroPadding.AsSpan( 4 ) );
		nonzeroPadding[headerBytes - 1] = 1;
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( nonzeroPadding ) ) );
	}

	[TestMethod]
	public void EveryTruncatedContainerPrefixRejectsAndValidPaddingStillDecodes()
	{
		var bytes = Movie();
		for ( var length = 0; length < bytes.Length; length++ )
		{
			var prefix = length;
			Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( bytes, 0, prefix ) ), $"prefix {prefix}" );
		}
		var movie = new TgqMovieFile( new MemoryStream( bytes ) );
		Assert.AreEqual( 56, movie.DecodeAudio().Length );
		Assert.AreEqual( 16, movie.DecodeVideoFrame( 0 ).Width );
		var reader = new TgqAudioReader( movie );
		Assert.AreEqual( 28, reader.Read( new short[57] ), "the odd unused output unit is not a stereo frame" );
		Assert.IsTrue( reader.EndOfStream );
		Assert.AreEqual( 0, reader.Read( new short[2] ) );
	}
}
