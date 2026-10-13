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

	private static byte[] Movie( byte[]? extraTag = null, bool malformedSecondBlock = false )
	{
		// Four zero-differential luma blocks and two chroma blocks, each ending with EOB.
		var frame = new byte[12];
		BinaryPrimitives.WriteUInt16LittleEndian( frame, 16 );
		BinaryPrimitives.WriteUInt16LittleEndian( frame.AsSpan( 2 ), 16 );
		var bits = "1001010010100101001000100010".PadRight( 32, '0' );
		BinaryPrimitives.WriteUInt32LittleEndian( frame.AsSpan( 8 ), Convert.ToUInt32( bits, 2 ) );
		var samples = malformedSecondBlock ? (byte)56 : (byte)28;
		var patch = new byte[] { (byte)'P', (byte)'T', 0, 0, 0x82, 1, 2, 0x85, 1, samples }
			.Concat( extraTag ?? new byte[] { 0x1b, 1, 30 } ).Concat( new byte[] { 0xff, 0, 0 } ).ToArray();
		var count = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian( count, malformedSecondBlock ? 2u : 1u );
		var audio = new byte[44];
		BinaryPrimitives.WriteUInt32LittleEndian( audio, 28 );
		audio[13] = 0xcc;
		var chunks = Chunk( "SCHl", patch ).Concat( Chunk( "pIQT", frame ) ).Concat( Chunk( "SCCl", count ) ).Concat( Chunk( "SCDl", audio ) );
		if ( malformedSecondBlock )
		{
			var malformed = (byte[])audio.Clone();
			malformed[12] = 0x40; // Valid encoded length, unsupported left predictor.
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
