using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;
using static OpenTPW.Tests.Mpeg1LayerTwoTestFrames;
namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class Mpeg1LayerTwoWavExportTests
{
	[DataTestMethod]
	[DataRow( 8, 0, 3 )]
	[DataRow( 14, 1, 0 )]
	[DataRow( 4, 2, 2 )]
	public void ExistingSelectedWavExportUsesMpeg1PcmRateAndChannels( int bitrate, int rate, int mode )
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-mpeg1-wav-" ).FullName; var previous = Log; Log = new();
		try
		{
			var packet = Frame( bitrate, rate, mode, crc: true, bands: Enumerable.Range( 0, mode == 3 ? 1 : 2 ).Select( channel => new Band( channel, 0 ) ).ToArray() ); var payload = packet.Data.Concat( packet.Data ).ToArray(); var expected = Mp2Decoder.Decode( payload ); Assert.AreEqual( 0, expected.CrcFailures );
			var input = Path.Combine( root, "source.sdt" ); File.WriteAllBytes( input, SdtEntryTests.CreateBank( ("generated.mp2", 1234, 36, 0, payload) ) ); var before = File.ReadAllBytes( input ); var output = Path.Combine( root, "selected.wav" ); SdtWavExport.Run( input, 1, output );
			using ( var wave = new WaveFileReader( output ) ) { Assert.AreEqual( packet.Rate, wave.WaveFormat.SampleRate ); Assert.AreEqual( packet.Channels, wave.WaveFormat.Channels ); var bytes = new byte[wave.Length]; wave.ReadExactly( bytes ); var actual = Enumerable.Range( 0, expected.Samples.Length ).Select( i => BinaryPrimitives.ReadInt16LittleEndian( bytes.AsSpan( i * 2 ) ) ).ToArray(); CollectionAssert.AreEqual( expected.Samples, actual ); }
			CollectionAssert.AreEqual( before, File.ReadAllBytes( input ) ); Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp" ).Length );
		}
		finally { Log = previous; Directory.Delete( root, true ); }
	}
}
