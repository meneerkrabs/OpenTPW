using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.Layer1TestFrames;

namespace OpenTPW.Tests;

[TestClass]
public class Layer1DecoderTests
{

	[TestMethod]
	public void DecodesLayerOneSilentMono384Samples()
	{
		var audio = Mp2Decoder.Decode( Frame() );
		Assert.AreEqual( 22050, audio.SampleRate );
		Assert.AreEqual( 1, audio.Channels );
		Assert.AreEqual( 384, audio.Samples.Length );
		Assert.IsTrue( audio.Samples.All( value => value == 0 ) );
	}

	[TestMethod]
	public void DecodesLayerOneMpegOneFixtureHeader()
	{
		var audio = Mp2Decoder.Decode( Frame( version: 3, bitrateIndex: 2 ) );
		Assert.AreEqual( 44100, audio.SampleRate );
		Assert.AreEqual( 384, audio.SampleFrames );
	}

	[TestMethod]
	public void PreservesLayerOnePaddingAndCrcFrameBoundaries()
	{
		var frame = Frame( padding: 1, crc: true );
		var audio = Mp2Decoder.Decode( frame.Concat( frame ).ToArray() );
		Assert.AreEqual( 2, audio.FrameCount );
		Assert.AreEqual( 768, audio.SampleFrames );
		Assert.AreEqual( 0, audio.TrailingBytes );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 2 )]
	public void DecodesLayerOneStereoAndDualChannel( int mode )
	{
		var audio = Mp2Decoder.Decode( Frame( mode: mode, bitrateIndex: 8 ) );
		Assert.AreEqual( 2, audio.Channels );
		Assert.AreEqual( 768, audio.Samples.Length );
	}

	[TestMethod]
	public void LayerOneRequantizationHasOffsetAndExpectedSteadyGain()
	{
		var frame = Frame( allocation: 1, sampleCode: 2 );
		var audio = Mp2Decoder.Decode( Enumerable.Range( 0, 4 ).SelectMany( _ => frame ).ToArray() );
		Assert.IsTrue( audio.Samples.Skip( 768 ).All( sample => Math.Abs( sample - 2.0 / 3 * 32768 ) <= 2 ) );
		var zero = Frame( allocation: 1, sampleCode: 1 );
		Assert.IsTrue( Mp2Decoder.Decode( zero ).Samples.All( sample => sample == 0 ) );
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 7 )]
	[DataRow( 14 )]
	public void LayerOneAllocationUsesAllocationPlusOneSampleBits( int allocation )
	{
		var code = (1 << allocation) - 1; // Exact zero after original offset requantization.
		var audio = Mp2Decoder.Decode( Frame( allocation: allocation, sampleCode: code, bitrateIndex: 14 ) );
		Assert.IsTrue( audio.Samples.All( sample => sample == 0 ) );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	public void LayerOneJointStereoSharesCodeButHasSeparateChannelFactors( int modeExtension )
	{
		var bound = (modeExtension + 1) * 4;
		var frame = Frame( allocation: 2, mode: 1, subband: bound, modeExtension: modeExtension,
			rightScale: 6, bitrateIndex: 14 );
		var audio = Mp2Decoder.Decode( frame.Concat( frame ).Concat( frame ).ToArray() );
		Assert.AreEqual( 2, audio.Channels );
		Assert.IsTrue( audio.Samples.Any( sample => sample != 0 ) );
		for ( var index = 768 * 2; index < audio.Samples.Length; index += 2 )
			Assert.IsTrue( Math.Abs( audio.Samples[index] - 2 * audio.Samples[index + 1] ) <= 2 );
	}

	[TestMethod]
	public void LayerOneFactorsControlGain()
	{
		var loud = Frame( allocation: 2, scale: 3 );
		var quiet = Frame( allocation: 2, scale: 6 );
		var a = Mp2Decoder.Decode( Enumerable.Range( 0, 4 ).SelectMany( _ => loud ).ToArray() );
		var b = Mp2Decoder.Decode( Enumerable.Range( 0, 4 ).SelectMany( _ => quiet ).ToArray() );
		for ( var index = 768; index < a.Samples.Length; index++ )
			Assert.IsTrue( Math.Abs( a.Samples[index] - 2 * b.Samples[index] ) <= 2 );
	}

	[TestMethod]
	public void RejectsLayerOneForbiddenAllocationAndReservedFactor()
	{
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( Frame( allocation: 15, bitrateIndex: 14 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( Frame( allocation: 1, scale: 63 ) ) );
	}

	[TestMethod]
	public void RejectsLayerOneReservedHeaderAndIncompletePayload()
	{
		foreach ( var frame in new[] { Frame( version: 1 ), Frame( bitrateIndex: 15 ), Frame( rateIndex: 3 ), Frame( emphasis: 2 ) } )
			Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( frame ) );
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( Frame().Take( 12 ).ToArray() ) );
		var oversizedPayload = Frame( bitrateIndex: 1 );
		for ( var index = 4; index < 20; index++ ) oversizedPayload[index] = 0xEE;
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( oversizedPayload ) );
	}

	[TestMethod]
	public void LayerOneReportsTrailingIncompleteFrameAndRejectsLayerSwitch()
	{
		var frame = Frame();
		var audio = Mp2Decoder.Decode( frame.Concat( frame.Take( 20 ) ).ToArray() );
		Assert.AreEqual( 1, audio.FrameCount );
		Assert.AreEqual( 20, audio.TrailingBytes );
		var layerTwo = (byte[])frame.Clone(); layerTwo[1] = (byte)((layerTwo[1] & ~6) | 4);
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( frame.Concat( layerTwo ).ToArray() ) );
	}
	[DataTestMethod]
	[DataRow( 2, 0, 22050 )]
	[DataRow( 2, 1, 24000 )]
	[DataRow( 2, 2, 16000 )]
	[DataRow( 3, 0, 44100 )]
	[DataRow( 3, 1, 48000 )]
	[DataRow( 3, 2, 32000 )]
	public void LayerOneSampleRateTableIsVersionSpecific( int version, int rateIndex, int expected )
	{
		Assert.AreEqual( expected, Mp2Decoder.Decode( Frame( version: version, rateIndex: rateIndex ) ).SampleRate );
	}

	[TestMethod]
	public void LayerOneDecodesLastSubbandAndIndependentJointStereoRegion()
	{
		Assert.IsTrue( Mp2Decoder.Decode( Frame( allocation: 2, subband: 31 ) ).Samples.Any( sample => sample != 0 ) );
		var frame = Frame( allocation: 2, mode: 1, subband: 0, sampleCode: 4, rightSampleCode: 2 );
		var audio = Mp2Decoder.Decode( Enumerable.Range( 0, 4 ).SelectMany( _ => frame ).ToArray() );
		for ( var index = 768; index < audio.Samples.Length; index += 2 )
			Assert.IsTrue( Math.Abs( audio.Samples[index] + audio.Samples[index + 1] ) <= 1 );
	}

	[TestMethod]
	public void LayerOneClipsBothPcmExtremes()
	{
		foreach ( var (frame, expected) in new[]
		{
			(Frame( allocation: 1, scale: 3, sampleCode: 3 ), short.MaxValue),
			(Frame( allocation: 1, scale: 0, sampleCode: 0 ), short.MinValue),
		} )
		{
			var audio = Mp2Decoder.Decode( Enumerable.Range( 0, 4 ).SelectMany( _ => frame ).ToArray() );
			Assert.IsTrue( audio.Samples.Skip( 768 ).All( sample => sample == expected ) );
		}
	}

	[TestMethod]
	public void LayerOneRejectsUnsupportedDeemphasisAndReservedLayer()
	{
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( Frame( emphasis: 1 ) ) );
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( Frame( emphasis: 3 ) ) );
		var reservedLayer = Frame(); reservedLayer[1] &= 0xF9;
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( reservedLayer ) );
	}

	[TestMethod]
	public void OriginalLayerOneErrorClipMatchesIndependentReference()
	{
		var clip = Mp2DecoderTests.OpenSpeechBank().soundFiles.Single( file => file.Name == "z_error.mp2" );
		Assert.AreEqual( "948AA426D01917A422A7CB590A36C46F17049CF3FF56404D49C390229DBDA94A",
			Convert.ToHexString( SHA256.HashData( clip.FrameData ) ) );
		var audio = Mp2Decoder.Decode( clip.FrameData );
		Assert.AreEqual( 25, audio.FrameCount );
		Assert.AreEqual( 9600, audio.Samples.Length );
		Assert.AreEqual( 1, audio.TrailingBytes );
		// Independent installed ffmpeg mp1 PCM hash (metadata only, no PCM fixture):
		// 1e86de97c3e240d3ed363fa8fcb3303d06fba3a6755060283fbf440d6e19a48a.
		foreach ( var (index, expected) in new[] { (500, 8595), (2000, 26658), (4000, -18197), (8000, -7409) } )
			Assert.IsTrue( Math.Abs( audio.Samples[index] - expected ) <= 1 );
		Assert.AreEqual( 15349.650836, Math.Sqrt( audio.Samples.Average( sample => (double)sample * sample ) ), 0.5 );
	}

	[TestMethod]
	public void OriginalLayerOneUiBankDecodesAllEntries()
	{
		var directory = Directory.GetParent( Mp2DecoderTests.SpeechDirectory() )!.FullName;
		var path = Path.Combine( directory, "sound", "UIHD.sdt" );
		if ( !File.Exists( path ) ) Assert.Inconclusive( "The selected original UIHD bank is missing." );
		Assert.AreEqual( "565FA2AA98935268B03DB3DBC3F903D2F65D48005241691B0172414F60372CB7",
			Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( path ) ) ) );
		using var bank = new SdtArchive( path );
		Assert.AreEqual( 32, bank.soundFiles.Count );
		foreach ( var clip in bank.soundFiles )
		{
			var audio = Mp2Decoder.Decode( clip.FrameData );
			Assert.AreEqual( 22050, audio.SampleRate, clip.Name );
			Assert.AreEqual( 1, audio.Channels, clip.Name );
			Assert.IsTrue( audio.FrameCount > 0, clip.Name );
			Assert.AreEqual( 1, audio.TrailingBytes, clip.Name );
			Assert.AreEqual( audio.FrameCount * 384, audio.SampleFrames, clip.Name );
		}
	}

}
