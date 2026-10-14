using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace OpenTPW.Tests;

[TestClass]
public class LayerTwoCrcSpanTests
{
	// Test encoder marks explicit normative fields as protected while emitting them.
	// It never asks the production decoder how many bits to protect.
	internal static (byte[] Bytes, int AudioProtectedBits, int ScaleStart) Frame( int mode, int allocatedBands, bool crc = true, bool leftOnly = false )
	{
		var writer = new Layer1TestFrames.BitWriter(); var protectedFields = new List<(int Value, int Width)>(); var offset = 0;
		void Put( int value, int width, bool protect = false ) { writer.Write( value, width ); offset += width; if ( protect ) protectedFields.Add( (value, width) ); }
		Put( 0x7FF, 11 ); Put( 2, 2 ); Put( 2, 2 ); Put( crc ? 0 : 1, 1 );
		Put( 14, 4, true ); Put( 0, 2, true ); Put( 0, 1, true ); Put( 0, 1, true ); Put( mode, 2, true ); Put( 0, 2, true ); Put( 0, 2, true ); Put( 0, 2, true );
		if ( crc ) Put( 0, 16 );
		var channels = mode == 3 ? 1 : 2;
		bool Allocated( int band, int channel ) => band < allocatedBands && (!leftOnly || channel == 0);
		for ( var band = 0; band < 30; band++ ) for ( var channel = 0; channel < channels; channel++ ) Put( Allocated( band, channel ) ? 1 : 0, band < 4 ? 4 : band < 11 ? 3 : 2, true );
		for ( var band = 0; band < 30; band++ ) for ( var channel = 0; channel < channels; channel++ ) if ( Allocated( band, channel ) ) Put( 2, 2, true ); // scfsi2, one scale factor
		var scaleStart = offset;
		for ( var band = 0; band < 30; band++ ) for ( var channel = 0; channel < channels; channel++ ) if ( Allocated( band, channel ) ) Put( 3, 6 );
		for ( var granule = 0; granule < 12; granule++ ) for ( var band = 0; band < 30; band++ ) for ( var channel = 0; channel < channels; channel++ ) if ( Allocated( band, channel ) ) Put( 26, 5 );
		var bytes = writer.Finish( 144 * 160000 / 22050 );
		if ( crc ) { var word = Checksum( protectedFields ); bytes[4] = (byte)(word >> 8); bytes[5] = (byte)word; }
		return (bytes, protectedFields.Sum( field => field.Width ) - 16, scaleStart);
	}
	private static ushort Checksum( IEnumerable<(int Value, int Width)> fields )
	{
		ushort state = ushort.MaxValue;
		foreach ( var field in fields ) for ( var shift = field.Width - 1; shift >= 0; shift-- ) { var feedback = ((state >> 15) ^ ((field.Value >> shift) & 1)) != 0; state = (ushort)(state << 1); if ( feedback ) state ^= 0x8005; }
		return state;
	}
	private static void Flip( byte[] bytes, int bit ) => bytes[bit / 8] ^= (byte)(1 << (7 - bit % 8));
	[TestMethod]
	public void IndependentFieldCrcMatchesPublishedPolynomialCheckValue()
	{
		Assert.AreEqual( (ushort)0xAEE7, Checksum( "123456789"u8.ToArray().Select( value => ((int)value, 8) ) ) );
	}
	[DataTestMethod]
	[DataRow( 3, 0, 75 )]
	[DataRow( 3, 1, 77 )]
	[DataRow( 3, 30, 135 )]
	[DataRow( 0, 0, 150 )]
	[DataRow( 0, 1, 154 )]
	[DataRow( 0, 30, 270 )]
	public void ZeroOneAndAllAllocationsProtectOnlyTransmittedFields( int mode, int bands, int expectedBits )
	{
		var frame = Frame( mode, bands ); Assert.AreEqual( expectedBits, frame.AudioProtectedBits );
		var audio = Mp2Decoder.Decode( frame.Bytes ); Assert.AreEqual( 1, audio.CrcChecks ); Assert.AreEqual( 0, audio.CrcFailures );
		CollectionAssert.AreEqual( Mp2Decoder.Decode( Frame( mode, bands, crc: false ).Bytes ).Samples, audio.Samples );
	}
	[TestMethod]
	public void UnequalDualChannelAllocationsCountEachTransmittedScfsiOnce()
	{
		var frame = Frame( 2, 1, leftOnly: true ); Assert.AreEqual( 152, frame.AudioProtectedBits ); var audio = Mp2Decoder.Decode( frame.Bytes );
		Assert.AreEqual( 0, audio.CrcFailures ); Assert.AreEqual( 2, audio.Channels ); Assert.IsTrue( audio.Samples.Where( ( _, index ) => index % 2 == 1 ).All( value => value == 0 ) );
	}
	[DataTestMethod]
	[DataRow( "header" )]
	[DataRow( "allocation" )]
	[DataRow( "scfsi" )]
	[DataRow( "scale" )]
	[DataRow( "ancillary" )]
	public void ProtectedAndUnprotectedBitFlipsKeepTheCountAndContinuePolicy( string field )
	{
		var fixture = Frame( 3, 1 ); var bytes = fixture.Bytes;
		var bit = field switch { "header" => 29, "allocation" => 50, "scfsi" => 48 + 75, "scale" => fixture.ScaleStart + 5, _ => bytes.Length * 8 - 1 };
		Flip( bytes, bit ); var audio = Mp2Decoder.Decode( bytes ); Assert.AreEqual( 1, audio.CrcChecks );
		Assert.AreEqual( field is "header" or "allocation" or "scfsi" ? 1 : 0, audio.CrcFailures ); Assert.AreEqual( 1152, audio.SampleFrames );
		Assert.IsTrue( audio.Samples.Any( value => value != 0 ), "CRC mismatch counts must not mute the frame." );
	}
	[DataTestMethod]
	[DataRow( 4 )]
	[DataRow( 5 )]
	[DataRow( 6 )]
	[DataRow( 15 )]
	[DataRow( 16 )]
	[DataRow( 17 )]
	[DataRow( 1043 )]
	public void TruncatedProtectedFramesRetainIncompleteFrameBehavior( int length )
	{
		var frame = Frame( 3, 1 ).Bytes; Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( frame[..length] ) );
		var stream = Frame( 3, 30 ).Bytes.Concat( frame[..length] ).ToArray(); var audio = Mp2Decoder.Decode( stream );
		Assert.AreEqual( 1, audio.FrameCount ); Assert.AreEqual( length, audio.TrailingBytes ); Assert.AreEqual( 1, audio.CrcChecks ); Assert.AreEqual( 0, audio.CrcFailures );
	}
	[TestMethod]
	public async Task CooperativeCrcAndPcmEqualSynchronousDecodingAcrossChangingAllocations()
	{
		var stream = Frame( 0, 0 ).Bytes.Concat( Frame( 0, 1 ).Bytes ).Concat( Frame( 0, 30 ).Bytes ).ToArray();
		var sync = Mp2Decoder.Decode( stream ); var yields = 0; var async = await Mp2Decoder.DecodeCooperativelyAsync( stream, 1, () => { yields++; return Task.CompletedTask; } );
		Assert.IsTrue( yields > 0 ); Assert.AreEqual( 3, sync.CrcChecks ); Assert.AreEqual( 0, sync.CrcFailures ); Assert.AreEqual( sync.CrcFailures, async.CrcFailures ); Assert.AreEqual( sync.CrcChecks, async.CrcChecks ); CollectionAssert.AreEqual( sync.Samples, async.Samples );
	}
}

[TestClass]
[DoNotParallelize]
public class LayerTwoCrcWavExportTests
{
	[TestMethod]
	public void ValidCrcExportsExactPcmAndBadCheckwordStillRefusesWithoutOutput()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-layer2-crc-wav-" ).FullName; var oldLog = Log; Log = new();
		try
		{
			var frame = LayerTwoCrcSpanTests.Frame( 3, 1 ).Bytes; var input = Path.Combine( root, "source.sdt" );
			File.WriteAllBytes( input, SdtEntryTests.CreateBank( ("chosen.mp2", 22050, 36, 0, frame) ) ); var source = File.ReadAllBytes( input ); var output = Path.Combine( root, "chosen.wav" );
			SdtWavExport.Run( input, 1, output ); using ( var wave = new WaveFileReader( output ) ) { var bytes = new byte[wave.Length]; wave.ReadExactly( bytes ); var expected = Mp2Decoder.Decode( frame ).Samples; var actual = Enumerable.Range( 0, expected.Length ).Select( i => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian( bytes.AsSpan( i * 2 ) ) ).ToArray(); CollectionAssert.AreEqual( expected, actual ); }
			CollectionAssert.AreEqual( source, File.ReadAllBytes( input ) ); frame[4] ^= 1; File.WriteAllBytes( input, SdtEntryTests.CreateBank( ("chosen.mp2", 22050, 36, 0, frame) ) );
			Assert.ThrowsException<InvalidDataException>( () => SdtWavExport.Run( input, 1, Path.Combine( root, "absent", "bad.wav" ) ) ); Assert.IsFalse( Directory.Exists( Path.Combine( root, "absent" ) ) ); Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp" ).Length );
		}
		finally { Log = oldLog; Directory.Delete( root, true ); }
	}
}
