using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class MpegPartialTailReviewTests
{
	[DataTestMethod, DataRow( 1 ), DataRow( 2 ), DataRow( 8 )]
	public async Task IncompleteMiddleFrameKeepsTheEntireRemainderWithoutResynchronizing( int batch )
	{
		var frame = Layer1TestFrames.Frame( allocation: 2, sampleCode: 7, crc: true, emphasis: 1 );
		var prefix = frame.Concat( frame ).ToArray(); var expected = Mp2Decoder.Decode( prefix );
		Assert.IsTrue( expected.Samples.Any( sample => sample != 0 ) ); Assert.AreEqual( 2, expected.CrcChecks );
		var advertised = Mp2DecoderTests.SilentFrame( 10, 0 );
		var remainder = advertised.Take( 4 ).Concat( Mp2DecoderTests.SilentFrame() ).ToArray();
		Assert.IsTrue( remainder.Length < advertised.Length, "The subsequent bytes cannot complete the advertised frame." );
		var data = prefix.Concat( remainder ).ToArray(); var sync = Mp2Decoder.Decode( data ); var yields = 0;
		var cooperative = await Mp2Decoder.DecodeCooperativelyAsync( data, batch, () => { yields++; return Task.CompletedTask; } );
		Assert.AreEqual( 2 / batch, yields ); Assert.AreEqual( 2, sync.FrameCount ); Assert.AreEqual( remainder.Length, sync.TrailingBytes );
		Assert.AreEqual( expected.SampleRate, sync.SampleRate ); Assert.AreEqual( expected.Channels, sync.Channels ); Assert.AreEqual( expected.Emphasis, sync.Emphasis );
		Assert.AreEqual( expected.CrcChecks, sync.CrcChecks ); Assert.AreEqual( expected.CrcFailures, sync.CrcFailures );
		CollectionAssert.AreEqual( expected.Samples, sync.Samples ); CollectionAssert.AreEqual( sync.Samples, cooperative.Samples );
		Assert.AreEqual( sync.FrameCount, cooperative.FrameCount ); Assert.AreEqual( sync.TrailingBytes, cooperative.TrailingBytes ); Assert.AreEqual( sync.CrcChecks, cooperative.CrcChecks ); Assert.AreEqual( sync.CrcFailures, cooperative.CrcFailures ); Assert.AreEqual( sync.Emphasis, cooperative.Emphasis );
	}

	[DataTestMethod, DataRow( "layer1" ), DataRow( "lsf" ), DataRow( "mpeg1" )]
	public async Task IncompleteFirstFrameDoesNotInventMetadataOrScheduleDecodedWork( string kind )
	{
		var frame = kind == "layer1" ? Layer1TestFrames.Frame( crc: true, emphasis: 1 ) : kind == "lsf" ? Mp2DecoderTests.ToneFrame() : Mpeg1LayerTwoTestFrames.Frame().Data;
		foreach ( var length in new[] { 4, 5, frame.Length - 1 } )
		{
			var data = frame.Take( length ).ToArray(); Assert.IsFalse( Mp2Decoder.TryReadFrameFormat( data, out var rate, out var channels ) ); Assert.AreEqual( 0, rate ); Assert.AreEqual( 0, channels );
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( data ) ).Message, "no complete frame" );
			var yields = 0; var error = await Assert.ThrowsExceptionAsync<InvalidDataException>( async () => await Mp2Decoder.DecodeCooperativelyAsync( data, 1, () => { yields++; return Task.CompletedTask; } ) );
			StringAssert.Contains( error.Message, "no complete frame" ); Assert.AreEqual( 0, yields );
		}
	}

	[DataTestMethod, DataRow( 0 ), DataRow( 1 ), DataRow( 2 ), DataRow( 3 )]
	public async Task ShortFinalHeaderBytesDoNotCreateAnotherFrameOrBatch( int length )
	{
		var frame = Mp2DecoderTests.ToneFrame(); var expected = Mp2Decoder.Decode( frame ); var data = frame.Concat( new byte[] { 0xFF, 0xFD, 0x40 }.Take( length ) ).ToArray(); var yields = 0;
		var audio = await Mp2Decoder.DecodeCooperativelyAsync( data, 1, () => { yields++; return Task.CompletedTask; } );
		Assert.AreEqual( 0, yields ); Assert.AreEqual( 1, audio.FrameCount ); Assert.AreEqual( length, audio.TrailingBytes ); Assert.AreEqual( expected.CrcChecks, audio.CrcChecks ); CollectionAssert.AreEqual( expected.Samples, audio.Samples );
	}

	private static byte[] ChangedFrame( string kind )
	{
		if ( kind == "layer" ) return Layer1TestFrames.Frame();
		if ( kind == "channels" ) return Mp2DecoderTests.SilentFrame( 10, 0 );
		var frame = Mp2DecoderTests.SilentFrame();
		if ( kind == "rate" ) { frame[2] = (byte)((frame[2] & ~12) | 4); Array.Resize( ref frame, 144 * 48000 / 24000 ); }
		else frame[3] |= 1;
		Assert.IsTrue( Mp2Decoder.TryReadFrameFormat( frame, out _, out _ ) ); return frame;
	}
	[DataTestMethod, DataRow( "rate" ), DataRow( "channels" ), DataRow( "layer" ), DataRow( "emphasis" )]
	public async Task ActualCompleteChangesRemainRefusedAtCooperativeBoundary( string kind )
	{
		var data = Mp2DecoderTests.ToneFrame().Concat( ChangedFrame( kind ) ).ToArray(); var yields = 0;
		var error = await Assert.ThrowsExceptionAsync<NotSupportedException>( async () => await Mp2Decoder.DecodeCooperativelyAsync( data, 1, () => { yields++; return Task.CompletedTask; } ) );
		StringAssert.Contains( error.Message, "frame 1 changes" ); Assert.AreEqual( 1, yields );
	}

	[DataTestMethod, DataRow( "sync" ), DataRow( "rate" ), DataRow( "bitrate" ), DataRow( "version" ), DataRow( "layer" ), DataRow( "emphasis" ), DataRow( "free" ), DataRow( "layer3" ), DataRow( "joint" ), DataRow( "mpeg25" )]
	public async Task FourByteMalformedOrUnsupportedHeadersRemainRefusedBeforeCompleteness( string kind )
	{
		var header = Mp2DecoderTests.SilentFrame().Take( 4 ).ToArray();
		switch ( kind )
		{
			case "sync": header[0] = 0; break;
			case "rate": header[2] |= 12; break;
			case "bitrate": header[2] |= 0xF0; break;
			case "version": header[1] = (byte)((header[1] & ~24) | 8); break;
			case "layer": header[1] &= unchecked((byte)~6); break;
			case "emphasis": header[3] = (byte)((header[3] & ~3) | 2); break;
			case "free": header[2] &= 15; break;
			case "mpeg25": header[1] &= unchecked((byte)~24); break;
			case "layer3": header[1] = (byte)((header[1] & ~6) | 2); break;
			case "joint": header[3] = (byte)((header[3] & ~192) | 64); break;
		}
		var data = Mp2DecoderTests.ToneFrame().Concat( header ).ToArray();
		if ( kind is "free" or "layer3" or "joint" or "mpeg25" )
		{
			Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( data ) );
			await Assert.ThrowsExceptionAsync<NotSupportedException>( async () => await Mp2Decoder.DecodeCooperativelyAsync( data, 1, () => Task.CompletedTask ) );
		}
		else
		{
			Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( data ) );
			await Assert.ThrowsExceptionAsync<InvalidDataException>( async () => await Mp2Decoder.DecodeCooperativelyAsync( data, 1, () => Task.CompletedTask ) );
		}
	}

	[TestMethod]
	public void WavExporterStillRefusesSupportedIncompleteTailBeforeAnyOutputMutation()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-tail-review-" ).FullName; var log = Log; Log = new Logger();
		try
		{
			var payload = Mp2DecoderTests.ToneFrame().Concat( Mpeg1LayerTwoTestFrames.Frame().Data.Take( 4 ) ).ToArray(); var input = Path.Combine( root, "source.sdt" );
			var source = SdtEntryTests.CreateBank( ("partial.mp2", 1234, 37, 0, payload) ); File.WriteAllBytes( input, source );
			var parsed = Mp2Decoder.Decode( payload ); Assert.AreEqual( 1, parsed.FrameCount ); Assert.AreEqual( 4, parsed.TrailingBytes );
			var output = Path.Combine( root, "new", "partial.wav" ); var error = Assert.ThrowsException<InvalidDataException>( () => SdtWavExport.Run( input, 1, output ) );
			StringAssert.Contains( error.Message, "unconsumed MPEG bytes" ); Assert.IsFalse( Directory.Exists( Path.GetDirectoryName( output ) ) );
			CollectionAssert.AreEqual( source, File.ReadAllBytes( input ) ); Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp", SearchOption.AllDirectories ).Length );
		}
		finally { Log = log; Directory.Delete( root, true ); }
	}
}
