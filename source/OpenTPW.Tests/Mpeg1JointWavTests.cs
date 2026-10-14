using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;
using static OpenTPW.Tests.Mpeg1JointTestFrames;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class Mpeg1JointWavTests
{
	[DataTestMethod]
	[DataRow( 4, 0, 0 )]
	[DataRow( 4, 2, 3 )]
	[DataRow( 8, 1, 1 )]
	[DataRow( 10, 0, 2 )]
	public void ActualSdtMetadataAndSelectedWavExportUseJointPcmWithoutSourceMutation( int index, int rate, int ext )
	{
		var packet = Make( index, rate, ext, crc: true, bands: new Band( 0, Left: new Channel( Samples: new[] { 2, 0, 1 } ), Right: new Channel( First: 9, Samples: new[] { 0, 1, 2 } ) ) );
		var payload = packet.Data.Concat( packet.Data ).ToArray(); var expected = Mp2Decoder.Decode( payload );
		var metadata = new MP2File( 40, "joint", packet.Data, 1234, 16, 36, 0, new byte[40].Concat( packet.Data ).ToArray() ); Assert.AreEqual( packet.Rate, metadata.SampleRate ); Assert.AreEqual( 2, metadata.Channels );
		var root = Directory.CreateTempSubdirectory( "opentpw-joint-wav-" ).FullName; var previous = Log; Log = new();
		try
		{
			var input = Path.Combine( root, "input.sdt" ); var output = Path.Combine( root, "output.wav" );
			File.WriteAllBytes( input, SdtEntryTests.CreateBank( ("joint.mp2", 1234, 36, 0, payload) ) ); var before = File.ReadAllBytes( input ); SdtWavExport.Run( input, 1, output );
			using ( var wave = new WaveFileReader( output ) ) { Assert.AreEqual( packet.Rate, wave.WaveFormat.SampleRate ); Assert.AreEqual( 2, wave.WaveFormat.Channels ); var bytes = new byte[wave.Length]; wave.ReadExactly( bytes ); CollectionAssert.AreEqual( expected.Samples, Enumerable.Range( 0, expected.Samples.Length ).Select( i => BinaryPrimitives.ReadInt16LittleEndian( bytes.AsSpan( i * 2 ) ) ).ToArray() ); }
			CollectionAssert.AreEqual( before, File.ReadAllBytes( input ) ); Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp" ).Length );
		}
		finally { Log = previous; Directory.Delete( root, true ); }
	}
	[TestMethod]
	public void BadJointCrcAndPartialTailRemainRefusedBeforeOutputMutation()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-joint-wav-refusal-" ).FullName; var previous = Log; Log = new();
		try
		{
			var packet = Make( crc: true, bands: new Band( 4 ) ); var bad = (byte[])packet.Data.Clone(); bad[4] ^= 1;
			foreach ( var payload in new[] { bad, packet.Data.Concat( packet.Data[..^1] ).ToArray() } )
			{
				var input = Path.Combine( root, "input.sdt" ); var output = Path.Combine( root, "new", "output.wav" ); File.WriteAllBytes( input, SdtEntryTests.CreateBank( ("joint.mp2", 44100, 36, 0, payload) ) ); var before = File.ReadAllBytes( input );
				Assert.ThrowsException<InvalidDataException>( () => SdtWavExport.Run( input, 1, output ) ); Assert.IsFalse( Directory.Exists( Path.GetDirectoryName( output ) ) ); CollectionAssert.AreEqual( before, File.ReadAllBytes( input ) );
			}
		}
		finally { Log = previous; Directory.Delete( root, true ); }
	}
}
