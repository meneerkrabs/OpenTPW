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
	[DataRow( 2, 3, 6, "Layer I" )]
	[DataRow( 2, 1, 6, "Layer III" )]
	[DataRow( 2, 2, 0, "free format" )]
	public void RejectsUnsupportedStreams( int versionBits, int layerBits, int bitrateIndex, string description )
	{
		var writer = new BitWriter();
		WriteHeader( writer, bitrateIndex, 3, layerBits, versionBits );
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( writer.ToFrame( FrameBytes48k ) ), description );
	}

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
