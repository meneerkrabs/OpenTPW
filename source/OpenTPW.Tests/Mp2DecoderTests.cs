using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class Mp2DecoderTests
{
	private sealed class BitWriter
	{
		private readonly List<bool> bits = new();
		public void Write( int value, int count )
		{
			for ( var index = count - 1; index >= 0; index-- )
				bits.Add( ((value >> index) & 1) != 0 );
		}
		public byte[] ToFrame( int length )
		{
			var data = new byte[length];
			for ( var index = 0; index < bits.Count; index++ )
				if ( bits[index] )
					data[index / 8] |= (byte)(0x80 >> (index % 8));
			return data;
		}
	}

	// MPEG-2 LSF Layer II, no CRC, 22,050 Hz, given bitrate index, no padding.
	private static void WriteHeader( BitWriter writer, int bitrateIndex = 6, int mode = 3, int layerBits = 2, int versionBits = 2 )
	{
		writer.Write( 0x7FF, 11 );
		writer.Write( versionBits, 2 );
		writer.Write( layerBits, 2 );
		writer.Write( 1, 1 );
		writer.Write( bitrateIndex, 4 );
		writer.Write( 0, 2 );
		writer.Write( 0, 1 );
		writer.Write( 0, 1 );
		writer.Write( mode, 2 );
		writer.Write( 0, 2 );
		writer.Write( 0, 4 );
	}

	private const int FrameBytes48k = 313; // 144 * 48000 / 22050

	private static byte[] SilentFrame( int bitrateIndex = 6, int mode = 3, int layerBits = 2, int versionBits = 2 )
	{
		var writer = new BitWriter();
		WriteHeader( writer, bitrateIndex, mode, layerBits, versionBits );
		return writer.ToFrame( 144 * new[] { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 }[bitrateIndex] * 1000 / 22050 );
	}

	// Mono frame with only subband 0 allocated to the 3-level class and one scalefactor.
	private static byte[] ToneFrame( int groupedCode = 26, int scaleFactor = 3 )
	{
		var writer = new BitWriter();
		WriteHeader( writer );
		writer.Write( 1, 4 );
		for ( var subband = 1; subband < 30; subband++ )
			writer.Write( 0, subband < 4 ? 4 : subband < 11 ? 3 : 2 );
		writer.Write( 2, 2 );
		writer.Write( scaleFactor, 6 );
		for ( var granule = 0; granule < 12; granule++ )
			writer.Write( groupedCode, 5 );
		return writer.ToFrame( FrameBytes48k );
	}

	[TestMethod]
	public void DecodesSilentMonoFrame()
	{
		var audio = Mp2Decoder.Decode( SilentFrame() );
		Assert.AreEqual( 22050, audio.SampleRate );
		Assert.AreEqual( 1, audio.Channels );
		Assert.AreEqual( 1, audio.FrameCount );
		Assert.AreEqual( Mp2Decoder.SamplesPerFrame, audio.Samples.Length );
		Assert.IsTrue( audio.Samples.All( sample => sample == 0 ) );
	}

	[TestMethod]
	public void DecodesStereoFramesInterleaved()
	{
		var frame = SilentFrame( 10, 0 );
		var audio = Mp2Decoder.Decode( frame.Concat( frame ).ToArray() );
		Assert.AreEqual( 2, audio.Channels );
		Assert.AreEqual( 2, audio.FrameCount );
		Assert.AreEqual( 2 * 2 * Mp2Decoder.SamplesPerFrame, audio.Samples.Length );
	}

	[TestMethod]
	public void DecodesAllocatedSubbandDeterministically()
	{
		// Code 26 = (2, 2, 2) in base 3: every sample is +2/3 of a unit scalefactor, i.e. a
		// constant input to subband 0, which the synthesis filterbank turns into DC.
		var audio = Mp2Decoder.Decode( ToneFrame().Concat( ToneFrame() ).ToArray() );
		Assert.AreEqual( 2 * Mp2Decoder.SamplesPerFrame, audio.Samples.Length );
		var steady = audio.Samples.Skip( Mp2Decoder.SamplesPerFrame ).ToArray();
		// The window and matrixing have unit DC gain: the steady output is 2/3 × 32,768.
		Assert.IsTrue( steady.All( sample => Math.Abs( sample - 2.0 / 3.0 * 32768 ) <= 2 ) );
	}

	[TestMethod]
	public void ReportsTrailingPartialFrame()
	{
		var audio = Mp2Decoder.Decode( SilentFrame().Concat( new byte[] { 0xFF, 0xF5, 0x60 } ).ToArray() );
		Assert.AreEqual( 1, audio.FrameCount );
		Assert.AreEqual( 3, audio.TrailingBytes );
	}

	[TestMethod]
	public void RejectsGroupedCodeOutOfRange()
	{
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( ToneFrame( groupedCode: 27 ) ) );
	}

	[TestMethod]
	public void RejectsReservedScalefactor()
	{
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( ToneFrame( scaleFactor: 63 ) ) );
	}

	[TestMethod]
	public void RejectsMissingSyncAndIncompleteStreams()
	{
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( new byte[FrameBytes48k] ) );
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( SilentFrame().Take( 100 ).ToArray() ) );
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( new byte[] { 0xFF, 0xF5 } ) );
		var reserved = new BitWriter();
		WriteHeader( reserved, bitrateIndex: 15 );
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( reserved.ToFrame( FrameBytes48k ) ) );
	}

	[DataTestMethod]
	[DataRow( 3, 2, 6, "MPEG-1" )]
	[DataRow( 0, 2, 6, "MPEG-2.5" )]
	[DataRow( 2, 1, 6, "Layer III" )]
	[DataRow( 2, 2, 0, "free format" )]
	public void RejectsUnsupportedStreams( int versionBits, int layerBits, int bitrateIndex, string description )
	{
		var writer = new BitWriter();
		WriteHeader( writer, bitrateIndex, 3, layerBits, versionBits );
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( writer.ToFrame( FrameBytes48k ) ), description );
	}

	[TestMethod]
	public void RejectsReservedLayer()
	{
		var writer = new BitWriter();
		WriteHeader( writer, layerBits: 0 );
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( writer.ToFrame( FrameBytes48k ) ) );
	}

	// Layer I header: version 3 = MPEG-1, 2 = MPEG-2 (LSF); rate index 0 = 44,100 / 22,050 Hz.
	private static void WriteLayerOneHeader( BitWriter writer, int versionBits, int bitrateIndex, int mode, int modeExtension = 0, bool crc = false, int padding = 0 )
	{
		writer.Write( 0x7FF, 11 );
		writer.Write( versionBits, 2 );
		writer.Write( 3, 2 );
		writer.Write( crc ? 0 : 1, 1 );
		writer.Write( bitrateIndex, 4 );
		writer.Write( 0, 2 );
		writer.Write( padding, 1 );
		writer.Write( 0, 1 );
		writer.Write( mode, 2 );
		writer.Write( modeExtension, 2 );
		writer.Write( 0, 4 );
		if ( crc )
			writer.Write( 0xBEEF, 16 );
	}

	// MPEG-2 Layer I at 22,050 Hz, 64 kbps: (12 * 64000 / 22050) * 4 = 136 bytes.
	private const int LayerOneFrameBytes64k = 136;

	// Mono MPEG-2 Layer I frame with only subband 0 allocated (nb = allocation + 1 bits).
	private static byte[] LayerOneToneFrame( int allocation = 1, int code = 2, int scaleFactor = 3 )
	{
		var writer = new BitWriter();
		WriteLayerOneHeader( writer, 2, 4, 3 );
		writer.Write( allocation, 4 );
		for ( var subband = 1; subband < 32; subband++ )
			writer.Write( 0, 4 );
		writer.Write( scaleFactor, 6 );
		for ( var slot = 0; slot < 12; slot++ )
			writer.Write( code, allocation + 1 );
		return writer.ToFrame( LayerOneFrameBytes64k );
	}

	[TestMethod]
	public void DecodesSilentLayerOneFrames()
	{
		var writer = new BitWriter();
		WriteLayerOneHeader( writer, 2, 4, 3 );
		var audio = Mp2Decoder.Decode( writer.ToFrame( LayerOneFrameBytes64k ) );
		Assert.AreEqual( 22050, audio.SampleRate );
		Assert.AreEqual( 1, audio.Channels );
		Assert.AreEqual( Mp2Decoder.LayerOneSamplesPerFrame, audio.Samples.Length );
		Assert.IsTrue( audio.Samples.All( sample => sample == 0 ) );

		// MPEG-1, 44,100 Hz, 128 kbps stereo: 34 slots of 4 bytes, plus one slot when padded.
		var unpadded = new BitWriter();
		WriteLayerOneHeader( unpadded, 3, 4, 0 );
		var padded = new BitWriter();
		WriteLayerOneHeader( padded, 3, 4, 0, padding: 1 );
		var stream = unpadded.ToFrame( 136 ).Concat( padded.ToFrame( 140 ) ).Concat( new byte[] { 0xFF } ).ToArray();
		audio = Mp2Decoder.Decode( stream );
		Assert.AreEqual( 44100, audio.SampleRate );
		Assert.AreEqual( 2, audio.Channels );
		Assert.AreEqual( 2, audio.FrameCount );
		Assert.AreEqual( 1, audio.TrailingBytes );
		Assert.AreEqual( 2 * 2 * Mp2Decoder.LayerOneSamplesPerFrame, audio.Samples.Length );
	}

	[TestMethod]
	public void DecodesLayerOneSubbandDeterministically()
	{
		// nb = 2 bits gives 3 levels; code 2 is +2/3 of a unit scalefactor. A constant
		// subband-0 input becomes DC once the 512-tap synthesis window has filled.
		var frame = LayerOneToneFrame();
		var audio = Mp2Decoder.Decode( Enumerable.Repeat( frame, 3 ).SelectMany( bytes => bytes ).ToArray() );
		var steady = audio.Samples.Skip( 2 * Mp2Decoder.LayerOneSamplesPerFrame ).ToArray();
		Assert.IsTrue( steady.All( sample => Math.Abs( sample - 2.0 / 3.0 * 32768 ) <= 2 ) );

		// 15 bits (allocation 14): code 0 is the most negative level, -(2^15 - 2) / (2^15 - 1);
		// scalefactor 6 is 2^-1.
		audio = Mp2Decoder.Decode( Enumerable.Repeat( LayerOneToneFrame( 14, 0, 6 ), 3 ).SelectMany( bytes => bytes ).ToArray() );
		steady = audio.Samples.Skip( 2 * Mp2Decoder.LayerOneSamplesPerFrame ).ToArray();
		Assert.IsTrue( steady.All( sample => Math.Abs( sample + 0.5 * 32766.0 / 32767.0 * 32768 ) <= 2 ) );
	}

	[TestMethod]
	public void DecodesLayerOneIntensityStereo()
	{
		// Joint stereo, mode extension 0: subbands 4 and up share one allocation and one
		// sample per slot, but each channel has its own scalefactor (here 2^-1 and 2^-2).
		byte[] Frame( bool crc )
		{
			var writer = new BitWriter();
			WriteLayerOneHeader( writer, 3, 4, 1, 0, crc );
			for ( var subband = 0; subband < 4; subband++ )
			{
				writer.Write( 0, 4 );
				writer.Write( 0, 4 );
			}
			writer.Write( 3, 4 );
			for ( var subband = 5; subband < 32; subband++ )
				writer.Write( 0, 4 );
			writer.Write( 6, 6 );
			writer.Write( 9, 6 );
			for ( var slot = 0; slot < 12; slot++ )
				writer.Write( slot % 2 == 0 ? 14 : 1, 4 );
			return writer.ToFrame( 12 * 128000 / 44100 * 4 );
		}
		foreach ( var crc in new[] { false, true } )
		{
			var audio = Mp2Decoder.Decode( Enumerable.Repeat( Frame( crc ), 4 ).SelectMany( bytes => bytes ).ToArray() );
			Assert.AreEqual( 2, audio.Channels );
			double left = 0, right = 0;
			for ( var index = 0; index < audio.Samples.Length; index += 2 )
			{
				left += Math.Abs( audio.Samples[index] );
				right += Math.Abs( audio.Samples[index + 1] );
				Assert.IsTrue( Math.Abs( audio.Samples[index] - 2 * audio.Samples[index + 1] ) <= 2, $"sample {index}" );
			}
			Assert.IsTrue( left > 0 && Math.Abs( left / right - 2 ) < 0.01 );
		}
	}

	[TestMethod]
	public void RejectsInvalidLayerOneStreams()
	{
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( LayerOneToneFrame( allocation: 15 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( LayerOneToneFrame( scaleFactor: 63 ) ) );
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( LayerOneToneFrame().Concat( SilentFrame() ).ToArray() ) );
		var freeFormat = new BitWriter();
		WriteLayerOneHeader( freeFormat, 2, 0, 3 );
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( freeFormat.ToFrame( LayerOneFrameBytes64k ) ) );
	}

	// Deterministic stereo MPEG-1 Layer I stream that exercises every allocation width and
	// both channels with pseudo-random scalefactors and samples (fixed LCG, no System.Random).
	internal static byte[] LayerOneReferenceStream()
	{
		var state = 12345u;
		int Next( int bound )
		{
			state = state * 1664525u + 1013904223u;
			return (int)((state >> 8) % (uint)bound);
		}
		var stream = new List<byte>();
		for ( var frame = 0; frame < 8; frame++ )
		{
			var writer = new BitWriter();
			WriteLayerOneHeader( writer, 3, 14, 2, crc: frame % 2 == 1 );
			var allocation = new int[2, 32];
			for ( var subband = 0; subband < 32; subband++ )
				for ( var channel = 0; channel < 2; channel++ )
				{
					allocation[channel, subband] = subband < 16 ? (subband + channel + frame) % 15 : 0;
					writer.Write( allocation[channel, subband], 4 );
				}
			for ( var subband = 0; subband < 32; subband++ )
				for ( var channel = 0; channel < 2; channel++ )
					if ( allocation[channel, subband] != 0 )
						writer.Write( 12 + Next( 40 ), 6 );
			for ( var slot = 0; slot < 12; slot++ )
				for ( var subband = 0; subband < 32; subband++ )
					for ( var channel = 0; channel < 2; channel++ )
						if ( allocation[channel, subband] != 0 )
							writer.Write( Next( (1 << (allocation[channel, subband] + 1)) - 1 ), allocation[channel, subband] + 1 );
			stream.AddRange( writer.ToFrame( 12 * 448000 / 44100 * 4 ) );
		}
		return stream.ToArray();
	}

	[TestMethod]
	public void LayerOneStreamMatchesPinnedReferenceDecode()
	{
		var audio = Mp2Decoder.Decode( LayerOneReferenceStream() );
		Assert.AreEqual( 8, audio.FrameCount );
		Assert.AreEqual( 8 * 2 * Mp2Decoder.LayerOneSamplesPerFrame, audio.Samples.Length );
		// Reference values come from ffmpeg's independent mp1 decoder, run separately on this
		// exact byte stream; across 72 random Layer I streams (MPEG-1/2, all rates and modes,
		// CRC and intensity stereo) its PCM differs from this decoder's by at most 1 LSB.
		foreach ( var (index, expected) in PinnedLayerOneSamples )
			Assert.IsTrue( Math.Abs( audio.Samples[index] - expected ) <= 1, $"sample {index}: {audio.Samples[index]}" );
	}

	private static readonly (int Index, int Expected)[] PinnedLayerOneSamples =
	{
		(700, -1101), (1500, -594), (2300, -932), (3100, -1314),
		(3900, 1708), (4700, -863), (5500, 1708), (6100, 1398),
	};

	[TestMethod]
	public void RejectsJointStereoAndOversizedInput()
	{
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( SilentFrame( 10, 1 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( new byte[Mp2Decoder.MaximumInputBytes + 1] ) );
	}

	[TestMethod]
	public void OriginalSpeechBankDecodesEveryLayerTwoClip()
	{
		var bank = OpenSpeechBank();
		var decoded = 0;
		var layerOne = new List<string>();
		foreach ( var clip in bank.soundFiles )
		{
			if ( (clip.FrameData[1] & 0x06) == 0x06 )
			{
				layerOne.Add( clip.Name );
				continue;
			}
			var audio = Mp2Decoder.Decode( clip.FrameData );
			Assert.AreEqual( 22050, audio.SampleRate, clip.Name );
			Assert.AreEqual( 1, audio.Channels, clip.Name );
			Assert.IsTrue( audio.TrailingBytes <= 1, clip.Name );
			decoded++;
		}
		Assert.AreEqual( 640, decoded );
		CollectionAssert.AreEqual( new[] { "z_error.mp2" }, layerOne );
	}

	[TestMethod]
	public void OriginalLayerOneSoundEffectsDecode()
	{
		// Every Layer I entry in every shipped .SDT bank (sound effects and speech z_error).
		var dataPath = Path.GetDirectoryName( Path.GetDirectoryName( SpeechDirectory() ) )!;
		var banks = Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories )
			.Where( path => string.Equals( Path.GetExtension( path ), ".sdt", StringComparison.OrdinalIgnoreCase ) )
			.ToArray();
		if ( banks.Length == 0 )
			Assert.Inconclusive( "No .SDT banks below the game data directory." );
		var decoded = 0;
		var failures = new List<string>();
		foreach ( var path in banks )
		{
			foreach ( var clip in new SdtArchive( path ).soundFiles )
			{
				var frames = clip.FrameData;
				if ( frames.Length < 4 || frames[0] != 0xFF || (frames[1] & 0xE6) != 0xE6 )
					continue;
				try
				{
					var audio = Mp2Decoder.Decode( frames );
					Assert.AreEqual( audio.Channels * audio.FrameCount * Mp2Decoder.LayerOneSamplesPerFrame, audio.Samples.Length, clip.Name );
					decoded++;
				}
				catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException )
				{
					failures.Add( $"{Path.GetFileName( path )}/{clip.Name}: {exception.Message}" );
				}
			}
		}
		Assert.AreEqual( 0, failures.Count, string.Join( "\n", failures.Take( 20 ) ) );
		Assert.IsTrue( decoded > 0, "No Layer I entries found." );
	}

	[TestMethod]
	public void OriginalSpeechClipMatchesPinnedDecode()
	{
		var clip = OpenSpeechBank().soundFiles.Single( file => file.Name == "sp_001.mp2" );
		var audio = Mp2Decoder.Decode( clip.FrameData );
		// Reference values come from an independent decoder (ffmpeg's mp2 decoder, run
		// separately); its PCM differs from this decoder's by at most 1 LSB over the corpus.
		Assert.AreEqual( 81, audio.FrameCount );
		Assert.AreEqual( 93312, audio.Samples.Length );
		foreach ( var (index, expected) in new[] { (10000, 1523), (20000, -4351), (50000, 238), (80000, -7038) } )
			Assert.IsTrue( Math.Abs( audio.Samples[index] - expected ) <= 1, $"sample {index}: {audio.Samples[index]}" );
		var rms = Math.Sqrt( audio.Samples.Average( sample => (double)sample * sample ) );
		Assert.AreEqual( 5355.33, rms, 0.5 );
	}

	internal static SdtArchive OpenSpeechBank()
	{
		var path = Path.Combine( SpeechDirectory(), "speechHD.SDT" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( "The global speechHD.SDT is missing." );
		return new SdtArchive( path );
	}

	internal static string SpeechDirectory()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original speech bank." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		return Path.Combine( dataPath, "global", "Speech" );
	}
}
