using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class MP2FileMetadataTests
{
	private static byte[] Frame( int version = 3, int rateIndex = 0, int mode = 3, int layerBits = 3 )
	{
		var rate = new[] { 22050, 24000, 16000 }[rateIndex] * (version == 3 ? 2 : 1);
		const int bitrateIndex = 8;
		var bitrate = layerBits == 3 ? (version == 3 ? 256 : 128) : 64;
		var length = layerBits == 3 ? 12 * bitrate * 1000 / rate * 4 : 144 * bitrate * 1000 / rate;
		var frame = new byte[length];
		var header = 0xFFE00000u | (uint)version << 19 | (uint)layerBits << 17 | 1u << 16 |
			bitrateIndex << 12 | (uint)rateIndex << 10 | (uint)mode << 6;
		BinaryPrimitives.WriteUInt32BigEndian( frame, header );
		return frame;
	}

	private static MP2File Entry( byte[] frame, int containerRate = 12345, int type = 0, int header = 40 )
	{
		var data = new byte[header + frame.Length];
		frame.CopyTo( data, header );
		// SoundData intentionally differs: shipped streams start at the entry Header.
		return new MP2File( header, "synthetic.mp2", new byte[100], containerRate, 8, type, 0, data );
	}

	[DataTestMethod]
	[DataRow( 3, 0, 3, 44100, 1 )]
	[DataRow( 3, 1, 0, 48000, 2 )]
	[DataRow( 3, 2, 1, 32000, 2 )]
	[DataRow( 2, 0, 3, 22050, 1 )]
	[DataRow( 2, 1, 2, 24000, 2 )]
	[DataRow( 2, 2, 0, 16000, 2 )]
	public void LayerOneMetadataMatchesDecodedFormat( int version, int rate, int mode, int expectedRate, int expectedChannels )
	{
		var entry = Entry( Frame( version, rate, mode ), type: (int)MP2File.SoundTypes.MP2_STEREO );
		Assert.AreEqual( expectedRate, entry.SampleRate );
		Assert.AreEqual( expectedChannels, entry.Channels );
		var audio = Mp2Decoder.Decode( entry.FrameData );
		Assert.AreEqual( audio.SampleRate, entry.SampleRate );
		Assert.AreEqual( audio.Channels, entry.Channels );
	}

	[DataTestMethod]
	[DataRow( 0, 3, 22050, 1 )]
	[DataRow( 1, 0, 24000, 2 )]
	[DataRow( 2, 2, 16000, 2 )]
	public void LayerTwoMetadataMatchesDecodedFormat( int rate, int mode, int expectedRate, int expectedChannels )
	{
		var entry = Entry( Frame( 2, rate, mode, 2 ) );
		Assert.AreEqual( expectedRate, entry.SampleRate );
		Assert.AreEqual( expectedChannels, entry.Channels );
		var audio = Mp2Decoder.Decode( entry.FrameData );
		Assert.AreEqual( audio.SampleRate, entry.SampleRate );
		Assert.AreEqual( audio.Channels, entry.Channels );
	}

	[TestMethod]
	public void InvalidOrUnsupportedFrameKeepsContainerMetadataWithoutBlockingEntry()
	{
		var valid = Frame();
		var reserved = (byte[])valid.Clone();
		reserved[2] |= 0xF0;
		foreach ( var frame in new[] { Array.Empty<byte>(), new byte[3], new byte[100], valid[..20], reserved, Frame( layerBits: 2 ) } )
		{
			var entry = Entry( frame, containerRate: 32000, type: (int)MP2File.SoundTypes.MP2_STEREO );
			Assert.AreEqual( 32000, entry.SampleRate );
			Assert.AreEqual( 2, entry.Channels );
		}
		Assert.AreEqual( 0, Entry( new byte[100] ).Channels );
	}

	[TestMethod]
	public void InvalidEntryOffsetDoesNotBecomeAHeaderParse()
	{
		var valid = Frame();
		foreach ( var offset in new[] { -1, valid.Length + 1 } )
		{
			var entry = new MP2File( offset, "invalid.mp2", valid, 32000, 16, 0, 0, valid );
			Assert.AreEqual( 32000, entry.SampleRate );
			Assert.AreEqual( 0, entry.Channels );
		}
	}

	[DataTestMethod]
	[DataRow( "sound", "SfxHD.sdt", "keyexplode.mp2", 44100 )]
	[DataRow( "Speech", "speechHD.SDT", "sp_001.mp2", 22050 )]
	public void OriginalMetadataMatchesPrivateDecodedFormat( string folder, string bankName, string clipName, int expectedRate )
	{
		var global = Directory.GetParent( Mp2DecoderTests.SpeechDirectory() )!.FullName;
		var path = Path.Combine( global, folder, bankName );
		if ( !File.Exists( path ) ) Assert.Inconclusive( "The selected original metadata bank is missing." );
		using var bank = new SdtArchive( path );
		var clip = bank.soundFiles.Single( file => file.Name == clipName );
		var audio = Mp2Decoder.Decode( clip.FrameData );
		Assert.AreEqual( expectedRate, clip.SampleRate );
		Assert.AreEqual( audio.SampleRate, clip.SampleRate );
		Assert.AreEqual( audio.Channels, clip.Channels );
	}

	[TestMethod]
	public void UsesValidatedMpegRateRatherThanLegacyConstantOrContainerHint()
	{
		Assert.AreEqual( 44100, Entry( Frame() ).SampleRate );
	}
}
