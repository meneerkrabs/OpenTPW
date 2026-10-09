using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class TgqMovieFileTests
{
	// ---- synthetic builders -------------------------------------------------------------------

	private sealed class BitWriter
	{
		private readonly List<bool> bits = new();
		public BitWriter Put( string pattern )
		{
			foreach ( var character in pattern )
				bits.Add( character == '1' );
			return this;
		}
		public BitWriter Put( int value, int count ) => Put( Convert.ToString( value & ((1 << count) - 1), 2 ).PadLeft( count, '0' ) );
		// MSB-first bits packed into little-endian 32-bit words, zero padded to a whole word.
		public byte[] ToWords()
		{
			var words = new byte[(bits.Count + 31) / 32 * 4];
			for ( var word = 0; word < words.Length / 4; word++ )
			{
				uint value = 0;
				for ( var bit = 0; bit < 32; bit++ )
				{
					var index = word * 32 + bit;
					value = (value << 1) | (index < bits.Count && bits[index] ? 1u : 0u);
				}
				BinaryPrimitives.WriteUInt32LittleEndian( words.AsSpan( word * 4 ), value );
			}
			return words;
		}
	}

	private static byte[] Frame( BitWriter bits, ushort width = 16, ushort height = 16, byte quantizer = 99 )
	{
		var body = bits.ToWords();
		var payload = new byte[8 + body.Length];
		BinaryPrimitives.WriteUInt16LittleEndian( payload, width );
		BinaryPrimitives.WriteUInt16LittleEndian( payload.AsSpan( 2 ), height );
		payload[4] = quantizer;
		body.CopyTo( payload, 8 );
		return payload;
	}

	// One 16x16 macroblock: Y0 DC=100, Y1 DC=64 (diff -36), Y2 DC=64 + AC(0,1)=+1, Y3 DC=64 then escape,
	// Cb DC=128, Cr DC=50. Chroma predictors are independent from luma.
	private static BitWriter SampleMacroblock( string y3Coefficients = "000001" + "000000" + "00000011" + "10" )
	{
		return new BitWriter()
			.Put( "111110" ).Put( 100, 7 ).Put( "10" )
			.Put( "11110" ).Put( 27, 6 ).Put( "10" )
			.Put( "100" ).Put( "110" ).Put( "10" )
			.Put( "100" ).Put( y3Coefficients )
			.Put( "11111110" ).Put( 128, 8 ).Put( "10" )
			.Put( "111110" ).Put( 50, 6 ).Put( "10" );
	}

	private static byte[] Chunk( string fourCC, byte[] payload )
	{
		var chunk = new byte[8 + payload.Length];
		Encoding.ASCII.GetBytes( fourCC ).CopyTo( chunk, 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( chunk.AsSpan( 4 ), (uint)chunk.Length );
		payload.CopyTo( chunk, 8 );
		return chunk;
	}

	private static byte[] Patch( params byte[] tags ) => new byte[] { (byte)'P', (byte)'T', 0, 0 }.Concat( tags ).ToArray();

	private static readonly byte[] StereoPatch = Patch( 0x1b, 1, 30, 0xfd, 0x85, 1, 28, 0x82, 1, 2, 0x83, 1, 7, 0x8a, 4, 0, 0, 0, 0, 0xff );

	private static byte[] AudioBlock( uint samples, short leftCurrent, short leftPrevious, byte predictors, byte shifts, byte sampleByte, int frames = 1, int padding = 2 )
	{
		var block = new byte[12 + frames * 30 + padding];
		BinaryPrimitives.WriteUInt32LittleEndian( block, samples );
		BinaryPrimitives.WriteInt16LittleEndian( block.AsSpan( 4 ), leftCurrent );
		BinaryPrimitives.WriteInt16LittleEndian( block.AsSpan( 6 ), leftPrevious );
		for ( var frame = 0; frame < frames; frame++ )
		{
			block[12 + frame * 30] = predictors;
			block[13 + frame * 30] = shifts;
			for ( var index = 0; index < 28; index++ )
				block[14 + frame * 30 + index] = sampleByte;
		}
		return block;
	}

	private static byte[] Movie( byte[]? patch = null, byte[]? audio = null, byte[]? frame = null, uint? declaredBlocks = null, params byte[][] extra )
	{
		var audioBlock = audio ?? AudioBlock( 28, 0, 0, 0x00, 0xcc, 0x7f );
		var count = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian( count, declaredBlocks ?? 1 );
		return Chunk( "SCHl", patch ?? StereoPatch )
			.Concat( Chunk( "pIQT", frame ?? Frame( SampleMacroblock() ) ) )
			.Concat( Chunk( "SCCl", count ) )
			.Concat( Chunk( "SCDl", audioBlock ) )
			.Concat( Chunk( "SCEl", Array.Empty<byte>() ) )
			.Concat( extra.SelectMany( part => part ) )
			.ToArray();
	}

	// ---- container ------------------------------------------------------------------------------

	[TestMethod]
	public void ParsesSyntheticContainerAndLeavesStreamOpen()
	{
		using var stream = new MemoryStream( Movie() );
		var movie = new TgqMovieFile( stream );
		Assert.IsTrue( stream.CanRead );
		CollectionAssert.AreEqual( new[] { "SCHl", "pIQT", "SCCl", "SCDl", "SCEl" }, movie.Chunks.Select( chunk => chunk.FourCC ).ToArray() );
		Assert.AreEqual( 1, movie.VideoFrameCount );
		Assert.AreEqual( 1, movie.AudioBlockCount );
		Assert.AreEqual( 1, movie.DeclaredAudioBlockCount );
		Assert.AreEqual( 16, movie.Width );
		Assert.AreEqual( 16, movie.Height );
		var header = movie.AudioHeader;
		Assert.AreEqual( 0, header.Platform );
		Assert.AreEqual( 2, header.Channels );
		Assert.AreEqual( 7, header.Compression );
		Assert.IsNull( header.CompressionRevision );
		Assert.AreEqual( TgqMovieFile.DefaultSampleRate, header.SampleRate );
		Assert.AreEqual( 28L, header.SampleCount );
		Assert.AreEqual( 30, header.FrameRate );
		CollectionAssert.AreEqual( new byte[] { 0x1b, 0xfd, 0x85, 0x82, 0x83, 0x8a }, header.Tags.Select( tag => tag.Tag ).ToArray() );
	}

	[TestMethod]
	public void ReadsNonseekableShortReads()
	{
		using var stream = new ShortReadStream( Movie() );
		Assert.AreEqual( 1, new TgqMovieFile( stream ).VideoFrameCount );
		Assert.IsTrue( stream.CanRead );
	}

	[TestMethod]
	public void RejectsOversizedInputWithoutClosingIt()
	{
		using var stream = new MemoryStream( new byte[TgqMovieFile.MaximumFileBytes + 1] );
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( stream ) );
		Assert.IsTrue( stream.CanRead );
	}

	[DataTestMethod]
	[DataRow( 0u )]
	[DataRow( 7u )]
	[DataRow( 4096u )]
	[DataRow( uint.MaxValue )]
	public void RejectsInvalidChunkSizes( uint size )
	{
		var data = Movie();
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 4 ), size );
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void RejectsTruncationAtEveryChunkBoundary()
	{
		var data = Movie();
		for ( var length = 0; length < data.Length; length++ )
		{
			var copy = data.AsSpan( 0, length ).ToArray();
			Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( copy ) ), $"length {length}" );
		}
	}

	[TestMethod]
	public void RejectsStructuralViolations()
	{
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( declaredBlocks: 2 ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( extra: Chunk( "SCEl", Array.Empty<byte>() ) ) ) ) );
		var noHeaderFirst = Movie().Skip( 8 + StereoPatch.Length ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( noHeaderFirst ) ) );
		var twoHeaders = Chunk( "SCHl", StereoPatch ).Concat( Movie() ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( twoHeaders ) ) );
		var noVideo = Chunk( "SCHl", StereoPatch ).Concat( Chunk( "SCCl", new byte[4] ) ).Concat( Chunk( "SCEl", Array.Empty<byte>() ) ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( noVideo ) ) );
		var differentFrame = Chunk( "pIQT", Frame( SampleMacroblock().Put( "100" ).Put( "10" ), 16, 32 ) );
		var withSecondFrame = Chunk( "SCHl", StereoPatch ).Concat( Chunk( "pIQT", Frame( SampleMacroblock() ) ) ).Concat( differentFrame ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( withSecondFrame ) ) );
	}

	[TestMethod]
	public void RejectsUnknownChunkType()
	{
		var data = Movie();
		var offset = 8 + StereoPatch.Length;
		Encoding.ASCII.GetBytes( "SCLl" ).CopyTo( data, offset );
		Assert.ThrowsException<NotSupportedException>( () => new TgqMovieFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void RejectsMalformedPatches()
	{
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( patch: new byte[] { (byte)'X', (byte)'T', 0, 0, 0xff } ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( patch: Patch( 0xfd, 0x82, 1, 2 ) ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( patch: Patch( 0x82, 4, 2 ) ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( patch: Patch( 0x82, 1, 2, 0x82, 1, 2, 0xff ) ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( patch: Patch( 0x85, 5, 0, 0, 0, 0, 1, 0xff ) ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( patch: Patch( 0x85, 4, 0x7f, 0, 0, 0, 0xff ) ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new TgqMovieFile( new MemoryStream( Movie( patch: Patch( 0x82, 1, 2, 0xff, 1 ) ) ) ) );
		var extended = new TgqMovieFile( new MemoryStream( Movie( patch: Patch( 0x05, 0xff, 0, 0, 0, 2, 9, 9, 0x82, 1, 2, 0x85, 1, 28, 0xff, 0 ) ) ) );
		CollectionAssert.AreEqual( new byte[] { 9, 9 }, extended.AudioHeader.Tags[0].Value );
		Assert.AreEqual( 1, new TgqMovieFile( new MemoryStream( Movie( patch: Patch( 0xff ) ) ) ).AudioHeader.Channels );
	}

	// ---- audio ----------------------------------------------------------------------------------

	[TestMethod]
	public void DecodesEaXaStereoNibblesShiftsAndPredictors()
	{
		// Left: predictor 0, shift 20 => sample = signed nibble. Right: predictor 1 (240/256), shift 20.
		var block = AudioBlock( 3, 0, 0, 0x01, 0xcc, 0x70, padding: 0 );
		BinaryPrimitives.WriteInt16LittleEndian( block.AsSpan( 8 ), 256 );
		block[14] = 0x80;
		var output = new short[6];
		Assert.AreEqual( 3, EaXaAdpcmDecoder.DecodeStereoBlock( block, output ) );
		CollectionAssert.AreEqual( new short[] { -8, 240, 7, 225, 7, 211 }, output );
	}

	[TestMethod]
	public void EaXaClampsAndKeepsPartialFinalFrame()
	{
		var block = AudioBlock( 30, 32767, 32767, 0x22, 0x00, 0x77, frames: 2, padding: 0 );
		var output = new short[60];
		Assert.AreEqual( 30, EaXaAdpcmDecoder.DecodeStereoBlock( block, output ) );
		Assert.AreEqual( short.MaxValue, output[0] );
		Assert.AreEqual( short.MaxValue, output[58] );
	}

	[DataTestMethod]
	[DataRow( 57u, 2, 0 )]
	[DataRow( uint.MaxValue, 1, 0 )]
	[DataRow( 28u, 1, 4 )]
	public void EaXaRejectsInconsistentBlockSizes( uint samples, int frames, int padding )
	{
		var block = AudioBlock( samples, 0, 0, 0, 0, 0, frames, padding );
		Assert.ThrowsException<InvalidDataException>( () => EaXaAdpcmDecoder.ReadStereoBlockSampleCount( block ) );
	}

	[TestMethod]
	public void EaXaRejectsNonzeroPaddingTruncationAndUnknownPredictor()
	{
		var padded = AudioBlock( 28, 0, 0, 0, 0, 0 );
		padded[^1] = 1;
		Assert.ThrowsException<InvalidDataException>( () => EaXaAdpcmDecoder.ReadStereoBlockSampleCount( padded ) );
		Assert.ThrowsException<InvalidDataException>( () => EaXaAdpcmDecoder.ReadStereoBlockSampleCount( new byte[11] ) );
		var predictor = AudioBlock( 28, 0, 0, 0x40, 0, 0 );
		Assert.ThrowsException<InvalidDataException>( () => EaXaAdpcmDecoder.DecodeStereoBlock( predictor, new short[56] ) );
		Assert.ThrowsException<ArgumentException>( () => EaXaAdpcmDecoder.DecodeStereoBlock( AudioBlock( 28, 0, 0, 0, 0, 0 ), new short[55] ) );
	}

	[TestMethod]
	public void MovieAudioMustMatchHeaderCountAndSupportedVariant()
	{
		var pcm = new TgqMovieFile( new MemoryStream( Movie() ) ).DecodeAudio();
		Assert.AreEqual( 56, pcm.Length );
		Assert.AreEqual( (short)7, pcm[0] );
		var mismatch = new TgqMovieFile( new MemoryStream( Movie( audio: AudioBlock( 27, 0, 0, 0, 0xcc, 0x7f ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => mismatch.DecodeAudio() );
		var mono = new TgqMovieFile( new MemoryStream( Movie( patch: Patch( 0x85, 1, 28, 0x82, 1, 1, 0xff ) ) ) );
		Assert.ThrowsException<NotSupportedException>( () => mono.DecodeAudio() );
		var revision2 = new TgqMovieFile( new MemoryStream( Movie( patch: Patch( 0x80, 1, 2, 0x85, 1, 28, 0x82, 1, 2, 0xff ) ) ) );
		Assert.ThrowsException<NotSupportedException>( () => revision2.DecodeAudio() );
	}

	// ---- video ----------------------------------------------------------------------------------

	[TestMethod]
	public void DecodesDcDifferentialsAcEscapeAndWordPacking()
	{
		var frame = TqiDecoder.Decode( Frame( SampleMacroblock() ) );
		Assert.AreEqual( 16, frame.Width );
		Assert.AreEqual( 256, frame.Y.Length );
		Assert.AreEqual( 64, frame.Cb.Length );
		Assert.IsTrue( frame.Cb.All( value => value == 128 ) );
		Assert.IsTrue( frame.Cr.All( value => value == 50 ) );
		for ( var row = 0; row < 8; row++ )
		{
			for ( var column = 0; column < 8; column++ )
			{
				Assert.AreEqual( 100, frame.Y[row * 16 + column] );
				Assert.AreEqual( 64, frame.Y[row * 16 + 8 + column] );
				// Block 2: DC 64 plus horizontal frequency 1, level +1, dequantised by 16 * (107.5 - 99) * 0.625 / 8.
				var horizontal = 64 + Math.Sqrt( 0.125 ) * 0.5 * Math.Cos( (2 * column + 1) * Math.PI / 16 ) * 16 * 8.5 * 0.625 / 8;
				Assert.AreEqual( (byte)Math.Floor( horizontal + 0.5 ), frame.Y[(8 + row) * 16 + column], $"r{row} c{column}" );
				// Block 3: escape run 0 level 3 at horizontal frequency 1.
				var escaped = 64 + Math.Sqrt( 0.125 ) * 0.5 * Math.Cos( (2 * column + 1) * Math.PI / 16 ) * 3 * 16 * 8.5 * 0.625 / 8;
				Assert.AreEqual( (byte)Math.Floor( escaped + 0.5 ), frame.Y[(8 + row) * 16 + 8 + column], $"r{row} c{column}" );
			}
		}
		Assert.AreNotEqual( frame.Y[8 * 16], frame.Y[8 * 16 + 7] );
		var rgb = frame.ToRgb24();
		Assert.AreEqual( 16 * 16 * 3, rgb.Length );
	}

	[TestMethod]
	public void ZigZagPlacesSecondCoefficientVertically()
	{
		// Y2: run 1 level 1 ("011" + sign) lands on scan index 2 = natural (row 1, column 0).
		var custom = new BitWriter()
			.Put( "100" ).Put( "011" ).Put( "0" ).Put( "10" )
			.Put( "100" ).Put( "10" ).Put( "100" ).Put( "10" ).Put( "100" ).Put( "10" )
			.Put( "00" ).Put( "10" ).Put( "00" ).Put( "10" );
		var frame = TqiDecoder.Decode( Frame( custom ) );
		for ( var row = 0; row < 8; row++ )
			Assert.AreEqual( frame.Y[row * 16], frame.Y[row * 16 + 7] );
		Assert.AreEqual( (byte)2, frame.Y[0] );
		Assert.AreEqual( (byte)0, frame.Y[7 * 16] );
	}

	[DataTestMethod]
	[DataRow( "0000000000000000" )]
	[DataRow( "1111111" )]
	public void RejectsInvalidCodes( string luma )
	{
		var bits = new BitWriter().Put( luma == "1111111" ? luma : "100" + luma ).Put( new string( '0', 64 ) );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( Frame( bits ) ) );
	}

	[TestMethod]
	public void RejectsCoefficientOverflowForbiddenEscapesAndTruncation()
	{
		var overflow = new BitWriter().Put( "100" );
		for ( var index = 0; index < 64; index++ )
			overflow.Put( "110" );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( Frame( overflow ) ) );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( Frame( SampleMacroblock( "000001" + "000000" + "00000000" + "01111111" + "10" ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( Frame( SampleMacroblock( "000001" + "000000" + "10000000" + "10000000" + "10" ) ) ) );
		var extended = TqiDecoder.Decode( Frame( SampleMacroblock( "000001" + "000000" + "00000000" + "10000000" + "10" ) ) );
		Assert.AreEqual( 255, extended.Y[8 * 16 + 8] );
		var truncated = Frame( SampleMacroblock() );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( truncated.AsSpan( 0, truncated.Length - 4 ) ) );
	}

	[TestMethod]
	public void RejectsTrailingWordsNonzeroPaddingAndBadHeaders()
	{
		var withExtraWord = Frame( SampleMacroblock() ).Concat( new byte[4] ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( withExtraWord ) );
		var nonzeroPadding = Frame( SampleMacroblock().Put( "1" ) );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( nonzeroPadding ) );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( Frame( SampleMacroblock(), 15, 16 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( Frame( SampleMacroblock(), 0, 16 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( Frame( SampleMacroblock(), 2048, 16 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( Frame( SampleMacroblock(), quantizer: 108 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( Frame( SampleMacroblock() ).Concat( new byte[1] ).ToArray() ) );
		Assert.ThrowsException<InvalidDataException>( () => TqiDecoder.Decode( new byte[7] ) );
	}

	[TestMethod]
	public void MovieDecodesFrameByIndex()
	{
		var movie = new TgqMovieFile( new MemoryStream( Movie() ) );
		Assert.AreEqual( 100, movie.DecodeVideoFrame( 0 ).Y[0] );
		Assert.AreEqual( 99, movie.GetVideoFramePayload( 0 ).Span[4] );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => movie.DecodeVideoFrame( 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => movie.DecodeVideoFrame( -1 ) );
	}

	[TestMethod]
	public void CoefficientTableHasMpeg1EntryCount() => Assert.AreEqual( 111, TqiDecoder.CoefficientTableEntries );

	private sealed class ShortReadStream : MemoryStream
	{
		public ShortReadStream( byte[] data ) : base( data ) { }
		public override bool CanSeek => false;
		public override long Length => throw new NotSupportedException();
		public override int Read( byte[] readBuffer, int offset, int count ) => base.Read( readBuffer, offset, Math.Min( count, 5 ) );
	}

	// ---- private original corpus ----------------------------------------------------------------

	[DataTestMethod]
	[DataRow( "bf.tgq", "174723130D5F3F03DA58C26F6B162B886267F9BA694792DF6BC534847704A32D", 255, 265, 194815L, "9F7B0E0DE23DE02BE62C13CACA12AA292E1CA8D162A5ECCC1186CF57AA9647C8" )]
	[DataRow( "bub.tgq", "338764E9D40E3E9B49CF8356EA62C4BEA6E6701A79D74FCEDD8FBB6F9683E5E1", 1174, 1174, 862999L, "BD5CF5A5505759CFB3FEB225895E8F82B6D54452DCBD4ABE0DE4E42463E3255F" )]
	[DataRow( "buc.tgq", "50F751E8195DBE6784037A29776B46480B75CCAE542BF1EE77373DD3BBFF6A44", 1141, 1142, 839443L, "9EB746F70D2B0A1F06E9896F7A5CAA0DDA63D1DCB5CE7B107CD206E833F7ECB3" )]
	[DataRow( "grav.tgq", "FE6531E19FA0A9AEA2C9EA88428898E11FBA91034515818076E036061E85EBFD", 1212, 1213, 891701L, "A24589E3BC8FE437A37497634443EF08BB7097ADDCE3B83BADF02A72AF26B308" )]
	[DataRow( "jug.tgq", "ACBDE3241EB1268F525C4E50AF2FCEFBFEF057CB0583E8831C2D63BAFA2D4E69", 1150, 1151, 846094L, "525EE761E6208C101D92AA8062806E5475451A2149B4EA2807077743BBB748B6" )]
	[DataRow( "mir.tgq", "FFBCEB6864DDDA516922ACEAF4ED2C55E463E663D49230EA03A2E14E4BEA55A7", 1360, 1361, 1000591L, "B948174ED2A580ECF9850FA4E3A56F7C9075E131ED32659F01D05EE90AF83ADE" )]
	[DataRow( "plan.tgq", "9AC41B21B05527B76BE611BCB6951D05F461092A20906FA732701E864ABD0FF1", 1138, 1525, 1120542L, "9183960082E960D89DDF462119C369A09DBF8E68C443DB3B1B937A37AD2FADCF" )]
	[DataRow( "roc.tgq", "0906C4862F9FB594B60B9C11AFD7F657FC97DE82D60EF3CC4D7CD210D404E72D", 990, 991, 728347L, "793DBAD9B74D95801A822A795EBB0E5630933E7CBA735122D591A48F9D7C3350" )]
	[DataRow( "roll.tgq", "2BB0194447AE9DCE24D6E15C7D9C53177FFF1356E85BB6D8902DC68A69CB3FE0", 992, 993, 729817L, "BCAADF5E6134BD6A8E6F558992E6E34A3D72D9FAAFC73CA5B3AACD9F0FAC2A8B" )]
	public void OriginalMovieContainerAndAudioMatchPinnedIdentity( string name, string fileHash, int frames, int audioBlocks, long samples, string pcmHash )
	{
		var data = File.ReadAllBytes( Path.Combine( OriginalMovieDirectory(), name ) );
		Assert.AreEqual( fileHash, Convert.ToHexString( SHA256.HashData( data ) ) );
		var movie = new TgqMovieFile( new MemoryStream( data ) );
		Assert.AreEqual( data.Length, movie.Chunks.Sum( chunk => chunk.Size ) );
		Assert.AreEqual( 3 + frames + audioBlocks, movie.Chunks.Count );
		Assert.AreEqual( frames, movie.VideoFrameCount );
		Assert.AreEqual( audioBlocks, movie.AudioBlockCount );
		Assert.AreEqual( 320, movie.Width );
		Assert.AreEqual( 352, movie.Height );
		var header = movie.AudioHeader;
		Assert.AreEqual( (0, 2, 7, (int?)null, 22050, samples, (int?)30 ), (header.Platform, header.Channels, header.Compression, header.CompressionRevision, header.SampleRate, header.SampleCount, header.FrameRate) );
		var pcm = movie.DecodeAudio();
		Assert.AreEqual( samples * 2, pcm.LongLength );
		var bytes = new byte[pcm.Length * 2];
		Buffer.BlockCopy( pcm, 0, bytes, 0, bytes.Length );
		Assert.AreEqual( pcmHash, Convert.ToHexString( SHA256.HashData( bytes ) ) );
		// At 30 fps no audio track ends before its video (plan.tgq audio runs ~12.9 s past the last frame).
		Assert.IsTrue( samples / 22050.0 >= frames / 30.0, name );
	}

	[DataTestMethod]
	[DataRow( "bf.tgq", 0, "1D4342FCD8AEA35FFD74F8689FD8E31AAC5972511A436BFCEE29414A3A117495" )]
	[DataRow( "bf.tgq", 127, "6AC73535C81C09BC1FE9D14518E3434D0F96AD0731AB19A60DA3315B2D7E3640" )]
	[DataRow( "bub.tgq", 0, "83FE6707DB6C43D97D195B1CB5347E7CCD92CFBBBB7DAC13FD5C303754C108F5" )]
	[DataRow( "bub.tgq", 587, "8E8AF40C38A099A07BD8937AFAF2208DCC2BD65982D7DC9905182EA5EBFB4DF4" )]
	[DataRow( "plan.tgq", 569, "CA804C6E2081B4A2370E91640D8F9836557F785233797F375FA8E543CE0261CC" )]
	public void OriginalMovieFramesMatchPinnedPlanarHashes( string name, int index, string yuvHash )
	{
		using var stream = File.OpenRead( Path.Combine( OriginalMovieDirectory(), name ) );
		var frame = new TgqMovieFile( stream ).DecodeVideoFrame( index );
		Assert.AreEqual( yuvHash, Convert.ToHexString( SHA256.HashData( frame.Y.Concat( frame.Cb ).Concat( frame.Cr ).ToArray() ) ) );
	}

	[TestMethod]
	public void EveryOriginalMovieFrameDecodesExactlyToItsBitstreamEnd()
	{
		var files = Directory.GetFiles( OriginalMovieDirectory() ).Where( file => Path.GetExtension( file ).Equals( ".tgq", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		Assert.AreEqual( 9, files.Length );
		var frames = 0;
		foreach ( var filename in files )
		{
			using var stream = File.OpenRead( filename );
			var movie = new TgqMovieFile( stream );
			for ( var index = 0; index < movie.VideoFrameCount; index++ )
			{
				var header = TqiDecoder.ReadHeader( movie.GetVideoFramePayload( index ).Span );
				Assert.AreEqual( (byte)99, header.Quantizer, filename );
				Assert.AreEqual( 0x031614, header.UnknownBytes, filename );
				Assert.AreEqual( 320 * 352, movie.DecodeVideoFrame( index ).Y.Length );
				frames++;
			}
		}
		Assert.AreEqual( 9412, frames );
	}

	private static string OriginalMovieDirectory()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the selected original TGQ movie corpus." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var directory = Directory.EnumerateDirectories( dataPath ).FirstOrDefault( entry => string.Equals( Path.GetFileName( entry ), "movies", StringComparison.OrdinalIgnoreCase ) );
		if ( directory == null )
			Assert.Inconclusive( "The selected original TGQ movie corpus is missing." );
		return directory!;
	}
}
