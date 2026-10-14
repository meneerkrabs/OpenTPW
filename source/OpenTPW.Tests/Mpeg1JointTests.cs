using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.Mpeg1JointTestFrames;

namespace OpenTPW.Tests;

[TestClass]
public class Mpeg1JointTests
{
	public static IEnumerable<object[]> LegalHeaders()
	{
		foreach ( var index in new[] { 4, 6, 7, 8, 9, 10, 11, 12, 13, 14 } ) for ( var rate = 0; rate < 3; rate++ ) for ( var ext = 0; ext < 4; ext++ ) yield return new object[] { index, rate, ext };
	}
	[DataTestMethod]
	[DynamicData( nameof( LegalHeaders ), DynamicDataSourceType.Method )]
	public void LegalJointHeadersAndAllExtensionsDecodeExactMathematicalSilence( int index, int rate, int extension )
	{
		var packet = Make( index, rate, extension, crc: true, padding: extension % 2 ); var audio = Mp2Decoder.Decode( packet.Data );
		Assert.AreEqual( packet.Rate, audio.SampleRate ); Assert.AreEqual( 2, audio.Channels ); Assert.AreEqual( 1152, audio.SampleFrames ); Assert.AreEqual( 0, audio.TrailingBytes );
		Assert.AreEqual( 1, audio.CrcChecks ); Assert.AreEqual( 0, audio.CrcFailures ); Assert.IsTrue( audio.Samples.All( s => s == 0 ) );
	}
	[DataTestMethod]
	[DataRow( 4, 0 )]
	[DataRow( 4, 2 )]
	[DataRow( 8, 0 )]
	[DataRow( 10, 0 )]
	public void SharedTripletsAndSeparateFactorsMatchIndependentlyEncodedStereoAtEveryBoundary( int index, int rate )
	{
		for ( var ext = 0; ext < 4; ext++ ) for ( var selection = 0; selection < 4; selection++ )
		{
			var descriptor = Make( index, rate, ext ); var positions = new[] { 0, 1, 2, 3, 10, 11, 22, 23, descriptor.Bound - 1, descriptor.Bound, descriptor.Limit - 1 }.Where( sb => sb < descriptor.Limit ).Distinct();
			foreach ( var sb in positions ) foreach ( var allocation in new[] { 1, Row( descriptor.Table, sb ).Levels.Length - 1 } )
			{
				var levels = Row( descriptor.Table, sb ).Levels[allocation]; var ordered = new[] { levels - 1, 0, levels / 2 };
				var left = new Channel( selection, 9, 12, 15, ordered ); var right = new Channel( (selection + 1) % 4, 12, 15, 18, sb >= descriptor.Bound ? ordered : new[] { 0, levels / 2, levels - 1 } );
				var band = new Band( sb, allocation, allocation, left, right );
				var joint = Make( index, rate, ext, crc: true, bands: band );
				var stereo = Mpeg1LayerTwoTestFrames.Frame( index, rate, 0, crc: true, bands: new[]{
					new Mpeg1LayerTwoTestFrames.Band(0,sb,allocation,left.Selection,left.First,left.Second,left.Third,left.Samples),
					new Mpeg1LayerTwoTestFrames.Band(1,sb,allocation,right.Selection,right.First,right.Second,right.Third,right.Samples)} );
				var actual = Mp2Decoder.Decode( joint.Data.Concat( joint.Data ).ToArray() ); var expected = Mp2Decoder.Decode( stereo.Data.Concat( stereo.Data ).ToArray() );
				CollectionAssert.AreEqual( expected.Samples, actual.Samples, $"{joint.Table} ext{ext} sb{sb} allocation{allocation} SCFSI{selection}" ); Assert.AreEqual( 0, actual.CrcFailures );
			}
		}
	}
	[TestMethod]
	public void MultipleSharedBandsCoexistWithUnequalIndependentChannels()
	{
		var bands = new[] { new Band( 0, 1, 2, new Channel( Samples: new[] { 2, 0, 1 } ), new Channel( Samples: new[] { 0, 4, 2 } ) ), new Band( 4, 3, 3, new Channel( 0, 9, 12, 15, new[] { 6, 0, 3 } ), new Channel( 3, 12, 15, 18 ) ), new Band( 29, 1, 1, new Channel( 1, 12, 15, 18, new[] { 2, 0, 1 } ), new Channel( 2, 15, 18, 21 ) ) };
		var joint = Make( 14, bands: bands ); var stereoBands = bands.SelectMany( b => new[]{
			new Mpeg1LayerTwoTestFrames.Band(0,b.Subband,b.LeftAllocation,b.Left!.Selection,b.Left.First,b.Left.Second,b.Left.Third,b.Left.Samples),
			new Mpeg1LayerTwoTestFrames.Band(1,b.Subband,b.RightAllocation,b.Right!.Selection,b.Right.First,b.Right.Second,b.Right.Third,b.Subband>=4?b.Left.Samples:b.Right.Samples)} ).ToArray();
		var stereo = Mpeg1LayerTwoTestFrames.Frame( 14, 0, 0, bands: stereoBands );
		CollectionAssert.AreEqual( Mp2Decoder.Decode( stereo.Data ).Samples, Mp2Decoder.Decode( joint.Data ).Samples );
	}
	[DataTestMethod]
	[DataRow( 4, 0 )]
	[DataRow( 4, 2 )]
	[DataRow( 8, 0 )]
	[DataRow( 14, 0 )]
	public void CrcCountsTransmittedAllocationAndBothSharedScfsiButExcludesFactors( int index, int rate )
	{
		var descriptor = Make( index, rate ); var empty = Make( index, rate, crc: true ); var one = Make( index, rate, crc: true, bands: new Band( descriptor.Limit - 1 ) );
		var widths = Enumerable.Range( 0, descriptor.Limit ).Sum( sb => Row( descriptor.Table, sb ).Width * (sb < descriptor.Bound ? 2 : 1) );
		Assert.AreEqual( widths, empty.ProtectedAudioBits ); Assert.AreEqual( widths + 4, one.ProtectedAudioBits );
		Assert.AreEqual( 0, Mp2Decoder.Decode( empty.Data ).CrcFailures ); Assert.AreEqual( 0, Mp2Decoder.Decode( one.Data ).CrcFailures );
		foreach ( var scfsi in one.ScfsiStarts ) { var mutation = Make( index, rate, crc: true, bands: new Band( descriptor.Limit - 1, Left: new Channel( 0, 0, 0, 0, new[] { 0, 0, 0 } ), Right: new Channel( 0, 0, 0, 0 ) ) ); var flipped = (byte[])mutation.Data.Clone(); Flip( flipped, scfsi + 1 ); Assert.AreEqual( 1, Mp2Decoder.Decode( flipped ).CrcFailures ); }
		var scale = (byte[])one.Data.Clone(); Flip( scale, one.ScaleStart + 5 ); Assert.AreEqual( 0, Mp2Decoder.Decode( scale ).CrcFailures );
		var sample = (byte[])one.Data.Clone(); Flip( sample, one.SampleStart + 4 ); Assert.AreEqual( 0, Mp2Decoder.Decode( sample ).CrcFailures );
		var ancillary = (byte[])one.Data.Clone(); Flip( ancillary, ancillary.Length * 8 - 1 ); Assert.AreEqual( 0, Mp2Decoder.Decode( ancillary ).CrcFailures );
		var all = Make( index, rate, crc: true, bands: Enumerable.Range( 0, descriptor.Limit ).Select( sb => new Band( sb ) ).ToArray() ); Assert.AreEqual( widths + 4 * descriptor.Limit, all.ProtectedAudioBits ); Assert.AreEqual( 0, Mp2Decoder.Decode( all.Data ).CrcFailures );
	}
	[DataTestMethod]
	[DataRow( 4, 0, 40 )]
	[DataRow( 4, 2, 52 )]
	[DataRow( 8, 0, 104 )]
	[DataRow( 14, 0, 110 )]
	public void LiteralEmptyCrcSpansAndFirstLastAllocationHeaderFlipsProtectOnlyTransmittedBits( int index, int rate, int expected )
	{
		var packet = Make( index, rate, crc: true ); Assert.AreEqual( expected, packet.ProtectedAudioBits );
		var last = 48 + Enumerable.Range( 0, packet.Limit ).Sum( sb => Row( packet.Table, sb ).Width * (sb < packet.Bound ? 2 : 1) ) - 1;
		foreach ( var bit in new[] { 51, last, 27 } ) { var mutated = (byte[])packet.Data.Clone(); Flip( mutated, bit ); var audio = Mp2Decoder.Decode( mutated ); Assert.AreEqual( 1, audio.CrcChecks ); Assert.AreEqual( 1, audio.CrcFailures ); }
	}
	[TestMethod]
	public async Task TableAndModeTransitionsPreserveSynthesisAndCooperativeBoundaries()
	{
		var first = Make( 8, bands: new Band( 4 ) ); var second = Make( 10, extension: 3, crc: true, bands: new Band( 16 ) ); var third = Make( 4, extension: 3, bands: new Band( 7 ) );
		var plain = Mpeg1LayerTwoTestFrames.Frame( 10, 0, 0, bands: new Mpeg1LayerTwoTestFrames.Band( 1, 0 ) );
		var bytes = first.Data.Concat( second.Data ).Concat( plain.Data ).Concat( third.Data ).ToArray(); var sync = Mp2Decoder.Decode( bytes ); var yields = 0;
		var cooperative = await Mp2Decoder.DecodeCooperativelyAsync( bytes, 1, () => { yields++; return Task.CompletedTask; } );
		CollectionAssert.AreEqual( sync.Samples, cooperative.Samples ); Assert.AreEqual( 4, sync.FrameCount ); Assert.AreEqual( 3, yields ); Assert.AreEqual( 0, sync.CrcFailures );
		var cancelled = Mp2Decoder.DecodeCooperativelyAsync( bytes, 1, () => throw new OperationCanceledException() ); await Assert.ThrowsExceptionAsync<OperationCanceledException>( () => cancelled );
	}
	[TestMethod]
	public void LayerTwoLsfJointAndOtherUnsupportedHeadersRemainRefused()
	{
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( Mp2DecoderTests.SilentFrame( 10, 1 ) ) );
		foreach ( var index in new[] { 1, 2, 3, 5 } ) { var data = Make().Data; data[2] = (byte)((data[2] & 15) | (index << 4)); Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( data ) ); }

	}
	[TestMethod]
	public void ReservedFactorAndGroupedSampleDamageRemainErrors()
	{
		var factor = Make( bands: new Band( 4, Left: new Channel( First: 63 ) ) ); Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( factor.Data ) );
		var group = Make( bands: new Band( 4, Left: new Channel( Samples: new[] { 0, 0, 3 } ) ) ); Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( group.Data ) );
		var valid = Make( bands: new Band( 4 ) ); Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( valid.Data.AsSpan( 0, valid.Data.Length - 1 ) ) );
	}
}
