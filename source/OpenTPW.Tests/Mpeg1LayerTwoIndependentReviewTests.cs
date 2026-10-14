using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.Mpeg1LayerTwoTestFrames;

namespace OpenTPW.Tests;

[TestClass]
public class Mpeg1LayerTwoIndependentReviewTests
{
	// Explicit LSF companion fields isolate quantizer-map reuse, not a new external PCM golden.
	private static byte[] LsfCompanion( int band, int allocation, int levels, int[] codes )
	{
		var bits = new List<int>();
		void Put( int value, int width ) { for ( var i = width - 1; i >= 0; i-- ) bits.Add( (value >> i) & 1 ); }
		Put( 0x7ff, 11 ); Put( 2, 2 ); Put( 2, 2 ); Put( 1, 1 ); Put( 14, 4 ); Put( 0, 4 ); Put( 3, 2 ); Put( 0, 6 );
		for ( var sb = 0; sb < 30; sb++ ) Put( sb == band ? allocation : 0, sb < 4 ? 4 : sb < 11 ? 3 : 2 );
		Put( 2, 2 ); Put( 3, 6 );
		for ( var granule = 0; granule < 12; granule++ )
			if ( levels is 5 or 9 ) Put( codes[0] + levels * (codes[1] + levels * codes[2]), levels == 5 ? 7 : 10 );
			else foreach ( var code in codes ) Put( code, 3 );
		var frame = new byte[144 * 160000 / 22050];
		for ( var i = 0; i < bits.Count; i++ ) frame[i / 8] |= (byte)(bits[i] << (7 - i % 8));
		return frame;
	}
	[DataTestMethod]
	[DataRow( 0, 2, 3, 7, 8 )]
	[DataRow( 2, 2, 3, 7, 8 )]
	[DataRow( 3, 2, 2, 5, 8 )]
	[DataRow( 10, 2, 2, 5, 8 )]
	[DataRow( 0, 3, 4, 9, 2 )]
	[DataRow( 1, 3, 4, 9, 2 )]
	public void InteriorHighAndLowMappingsProduceTheUnchangedLsfQuantizerWaveform( int band, int m1Allocation, int lsfAllocation, int levels, int bitrate )
	{
		var codes = new[] { levels - 1, levels / 2, 0 };
		var m1 = Frame( bitrate, 0, 3, crc: true, bands: new Band( 0, band, m1Allocation, Codes: codes ) );
		var expectedFrame = LsfCompanion( band, lsfAllocation, levels, codes );
		var actual = Mp2Decoder.Decode( Enumerable.Range( 0, 4 ).SelectMany( _ => m1.Data ).ToArray() );
		var expected = Mp2Decoder.Decode( Enumerable.Range( 0, 4 ).SelectMany( _ => expectedFrame ).ToArray() );
		Assert.AreEqual( 0, actual.CrcFailures ); Assert.AreEqual( 4, actual.CrcChecks );
		Assert.IsTrue( actual.Samples.Any( sample => sample != 0 ) );
		CollectionAssert.AreEqual( expected.Samples, actual.Samples, "Different allocation codes must resolve to the same existing quantizer and synthesis path." );
	}
	[TestMethod]
	public async Task ReverseTableTransitionsResolveEachFrameAndKeepCooperativeCrcPcm()
	{
		var packets = new[] { Frame( 8, bands: new Band( 0, 29 ), crc: true ), Frame( 1, bands: new Band( 0, 7 ), crc: true ), Frame( 5, bands: new Band( 0, 26 ), crc: true ), Frame( 10, bands: new Band( 0, 29 ), crc: true ) };
		CollectionAssert.AreEqual( new[] { 'B', 'C', 'A', 'B' }, packets.Select( p => p.Table ).ToArray() );
		var data = packets.SelectMany( packet => packet.Data ).ToArray(); var sync = Mp2Decoder.Decode( data );
		var cooperative = await Mp2Decoder.DecodeCooperativelyAsync( data, 1, () => Task.CompletedTask );
		Assert.AreEqual( 4, sync.FrameCount ); Assert.AreEqual( 4, sync.CrcChecks ); Assert.AreEqual( 0, sync.CrcFailures ); Assert.AreEqual( 0, sync.TrailingBytes );
		CollectionAssert.AreEqual( sync.Samples, cooperative.Samples ); Assert.AreEqual( sync.CrcChecks, cooperative.CrcChecks ); Assert.AreEqual( sync.CrcFailures, cooperative.CrcFailures );
	}
	[TestMethod]
	public void AllPinnedNonjointReferencePacketsIgnoreModeExtensionWithoutChangingPcm()
	{
		var cases = 0;
		foreach ( var row in Mpeg1LayerTwoReferenceTests.References() )
		{
			var frame = Convert.FromBase64String( (string)row[1] ); Assert.AreNotEqual( 0, frame[1] & 1, "These independent reference packets have no CRC word." );
			var expected = Mp2Decoder.Decode( Enumerable.Range( 0, 4 ).SelectMany( _ => frame ).ToArray() );
			for ( var extension = 0; extension < 4; extension++ )
			{
				var altered = frame.ToArray(); altered[3] = (byte)((altered[3] & 0xcf) | (extension << 4));
				var actual = Mp2Decoder.Decode( Enumerable.Range( 0, 4 ).SelectMany( _ => altered ).ToArray() );
				Assert.AreEqual( expected.SampleRate, actual.SampleRate ); Assert.AreEqual( expected.Channels, actual.Channels ); CollectionAssert.AreEqual( expected.Samples, actual.Samples );
			}
			cases++;
		}
		Assert.AreEqual( 15, cases );
	}
}
