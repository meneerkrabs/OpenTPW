using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.Mpeg1LayerTwoTestFrames;

namespace OpenTPW.Tests;

[TestClass]
public class Mpeg1LayerTwoTests
{
	public static IEnumerable<object[]> LegalHeaders()
	{
		foreach ( var mode in new[] { 0, 2, 3 } ) foreach ( var index in mode == 3 ? Enumerable.Range( 1, 10 ) : new[] { 4, 6, 7, 8, 9, 10, 11, 12, 13, 14 } ) for ( var rate = 0; rate < 3; rate++ ) yield return new object[] { index, rate, mode };
	}
	[DataTestMethod]
	[DynamicData( nameof( LegalHeaders ), DynamicDataSourceType.Method )]
	public void EveryLegalNonjointHeaderDecodesMathematicalSilenceAtExactFrameSize( int index, int rate, int mode )
	{
		var frame = Frame( index, rate, mode ); var audio = Mp2Decoder.Decode( frame.Data );
		Assert.AreEqual( frame.Rate, audio.SampleRate ); Assert.AreEqual( frame.Channels, audio.Channels ); Assert.AreEqual( 1, audio.FrameCount ); Assert.AreEqual( 1152, audio.SampleFrames ); Assert.AreEqual( 0, audio.TrailingBytes ); Assert.IsTrue( audio.Samples.All( sample => sample == 0 ) );
	}
	[DataTestMethod]
	[DataRow( 4, 0, 0 )]
	[DataRow( 3, 1, 3 )]
	[DataRow( 4, 2, 2 )]
	[DataRow( 7, 0, 0 )]
	[DataRow( 10, 0, 2 )]
	[DataRow( 10, 1, 0 )]
	public void PaddingAndIgnoredNonjointModeExtensionsPreserveHeaderAndDcPcm( int index, int rate, int mode )
	{
		var channels = mode == 3 ? 1 : 2; var bands = Enumerable.Range( 0, channels ).Select( ch => new Band( ch, 0 ) ).ToArray(); var plain = Frame( index, rate, mode, bands: bands );
		var expected = Mp2Decoder.Decode( plain.Data.Concat( plain.Data ).ToArray() );
		for ( var extension = 0; extension < 4; extension++ ) { var padded = Frame( index, rate, mode, padding: 1, modeExtension: extension, bands: bands ); var audio = Mp2Decoder.Decode( padded.Data.Concat( padded.Data ).ToArray() ); CollectionAssert.AreEqual( expected.Samples, audio.Samples ); Assert.AreEqual( 0, audio.TrailingBytes ); }
		Assert.IsTrue( expected.Samples.Skip( 1152 * channels ).All( sample => Math.Abs( sample - 2.0 / 3 * 32768 ) <= 2 ) );
	}
	[DataTestMethod]
	[DataRow( 3, 0, 3, 'A' )]
	[DataRow( 10, 0, 0, 'B' )]
	[DataRow( 1, 0, 3, 'C' )]
	[DataRow( 1, 2, 3, 'D' )]
	public void LastAllocatedSubbandAndEveryRowEndpointConsumeTheirStandardWidths( int index, int rate, int mode, char table )
	{
		var transitions = table is 'C' or 'D' ? new[] { 0, 1, 2, Limit( table ) - 1 } : new[] { 0, 2, 3, 10, 11, 22, 23, Limit( table ) - 1 };
		foreach ( var band in transitions.Distinct() ) foreach ( var allocation in new[] { 1, Row( table, band ).Levels.Length - 1 } )
		{
			var frame = Frame( index, rate, mode, crc: true, bands: new Band( 0, band, allocation ) ); Assert.AreEqual( table, frame.Table );
			var audio = Mp2Decoder.Decode( frame.Data ); Assert.AreEqual( 0, audio.CrcFailures ); Assert.AreEqual( 1152, audio.SampleFrames ); Assert.IsTrue( audio.Samples.Any( sample => sample != 0 ) );
		}
	}
	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	public void ScfsiPatternsKeepSeparateChannelsAndReuseLsfDcReconstruction( int scfsi )
	{
		var frame = Frame( 10, 0, 0, bands: new Band( 0, 0, Scfsi: scfsi ) ); var audio = Mp2Decoder.Decode( frame.Data.Concat( frame.Data ).ToArray() );
		Assert.IsTrue( audio.Samples.Where( ( _, index ) => index % 2 == 1 ).All( sample => sample == 0 ) );
		if ( scfsi == 2 ) Assert.IsTrue( audio.Samples.Skip( 2304 ).Where( ( _, index ) => index % 2 == 0 ).All( sample => Math.Abs( sample - 2.0 / 3 * 32768 ) <= 2 ) );
		else Assert.IsTrue( audio.Samples.Where( ( _, index ) => index % 2 == 0 ).Distinct().Count() > 2 );
	}
	[DataTestMethod]
	[DataRow( 4, 0, 0 )]
	[DataRow( 3, 1, 3 )]
	[DataRow( 4, 2, 2 )]
	[DataRow( 7, 0, 0 )]
	[DataRow( 10, 0, 2 )]
	public void CorrectedCrcSpanIsSharedAcrossAllFourMpeg1Profiles( int index, int rate, int mode )
	{
		var frame = Frame( index, rate, mode, crc: true, bands: new Band( 0, 0 ) ); var audio = Mp2Decoder.Decode( frame.Data ); Assert.AreEqual( 1, audio.CrcChecks ); Assert.AreEqual( 0, audio.CrcFailures );
		frame.Data[(frame.ScaleStart + 5) / 8] ^= (byte)(1 << (7 - (frame.ScaleStart + 5) % 8)); Assert.AreEqual( 0, Mp2Decoder.Decode( frame.Data ).CrcFailures, "Valid scalefactor bits must stay outside CRC coverage." );
		frame.Data[4] ^= 1; Assert.AreEqual( 1, Mp2Decoder.Decode( frame.Data ).CrcFailures );
	}
	[TestMethod]
	public async Task PerFrameAllocationTableTransitionsAndCooperativeDecodeKeepPcmAndCrc()
	{
		var first = Frame( 9, 0, 0, crc: true, bands: new Band( 0, 0 ) ); var second = Frame( 10, 0, 0, crc: true, bands: new Band( 1, 29 ) ); Assert.AreEqual( 'A', first.Table ); Assert.AreEqual( 'B', second.Table );
		var stream = first.Data.Concat( second.Data ).ToArray(); var sync = Mp2Decoder.Decode( stream ); var cooperative = await Mp2Decoder.DecodeCooperativelyAsync( stream, 1, () => Task.CompletedTask ); Assert.AreEqual( 2, sync.FrameCount ); Assert.AreEqual( 2, sync.CrcChecks ); Assert.AreEqual( 0, sync.CrcFailures ); CollectionAssert.AreEqual( sync.Samples, cooperative.Samples ); Assert.AreEqual( sync.CrcFailures, cooperative.CrcFailures );
	}
	[TestMethod]
	public void Mpeg1FirstCompleteHeaderProvidesActualSdtMetadataWithoutNewContainerPolicy()
	{
		var frame = Frame( 10, 0, 2 ).Data; var bytes = new byte[40].Concat( frame ).ToArray(); var file = new MP2File( 40, "synthetic", frame, 1234, 16, 36, 0, bytes ); Assert.AreEqual( 44100, file.SampleRate ); Assert.AreEqual( 2, file.Channels );
		var incomplete = bytes[..^1]; var unknown = new MP2File( 40, "partial", Array.Empty<byte>(), 1234, 16, 36, 0, incomplete ); Assert.AreEqual( 1234, unknown.SampleRate ); Assert.AreEqual( 1, unknown.Channels );
	}
	[TestMethod]
	public void PartialFramesAndReservedFactorsKeepStrictDecodeBoundaries()
	{
		var good = Frame().Data; Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( good[..^1] ) ); var audio = Mp2Decoder.Decode( good.Concat( good[..^1] ).ToArray() ); Assert.AreEqual( 1, audio.FrameCount ); Assert.AreEqual( good.Length - 1, audio.TrailingBytes );
		var reserved = Frame( bands: new Band( 0, 0, FirstScale: 63 ) ); Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( reserved.Data ) );
	}
	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 2 )]
	public void LowTableAllocation15UsesFifteenBitsWithAnUnclippedAnalyticDcSignal( int rate )
	{
		var frame = Frame( 2, rate, 3, bands: new Band( 0, 0, 15, Codes: new[] { 8191, 8191, 8191 } ) ); var audio = Mp2Decoder.Decode( frame.Data.Concat( frame.Data ).ToArray() );
		var expected = -16384.0 / 32767 * 32768; Assert.IsTrue( audio.Samples.Skip( 1152 ).All( sample => Math.Abs( sample - expected ) <= 2 ), "Wrong65535/16bit class must fail an unclipped waveform check, not hide behind saturation." );
	}
	[DataTestMethod]
	[DataRow( 0, 1 )]
	[DataRow( 0, 2 )]
	[DataRow( 0, 3 )]
	[DataRow( 0, 5 )]
	[DataRow( 2, 1 )]
	[DataRow( 2, 2 )]
	[DataRow( 2, 3 )]
	[DataRow( 2, 5 )]
	[DataRow( 3, 11 )]
	[DataRow( 3, 12 )]
	[DataRow( 3, 13 )]
	[DataRow( 3, 14 )]
	public void IllegalMpeg1BitrateModePairsAreRefused( int mode, int bitrate )
	{
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( Frame( bitrate, 0, mode ).Data ) );
	}
	[TestMethod]
	public void InvalidGroupedTripletAndUnsupportedContractsStayExplicit()
	{
		var bad = Frame( bands: new Band( 0, 0, Codes: new[] { 3, 2, 2 } ) ); Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( bad.Data ) );
		foreach ( var bits in new[] { (2, 2, 1, false), (3, 2, 0, true), (0, 2, 3, false), (3, 1, 3, false) } )
		{
			var frame = Frame().Data; var header = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian( frame ); header = (header & ~((3u << 19) | (3u << 17) | (3u << 6))) | ((uint)bits.Item1 << 19) | ((uint)bits.Item2 << 17) | ((uint)bits.Item3 << 6); if ( bits.Item4 ) header &= ~(15u << 12); System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian( frame, header );
			Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( frame ) );
		}
	}

	[DataTestMethod]
	[DataRow( 10, 1, 3 )]
	[DataRow( 10, 0, 3 )]
	[DataRow( 2, 0, 3 )]
	[DataRow( 2, 2, 3 )]
	public void ProfileSpecificCrcCoversZeroAndAllTransmittedAllocations( int index, int rate, int mode )
	{
		var empty = Frame( index, rate, mode, crc: true ); Assert.AreEqual( 0, Mp2Decoder.Decode( empty.Data ).CrcFailures );
		var all = Enumerable.Range( 0, Limit( empty.Table ) ).Select( band => new Band( 0, band ) ).ToArray(); var full = Frame( index, rate, mode, crc: true, bands: all ); var audio = Mp2Decoder.Decode( full.Data ); Assert.AreEqual( 1, audio.CrcChecks ); Assert.AreEqual( 0, audio.CrcFailures );
		CollectionAssert.AreEqual( Mp2Decoder.Decode( Frame( index, rate, mode, bands: all ).Data ).Samples, audio.Samples );
	}

}
