using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace OpenTPW.Tests;

[TestClass]
public class MpegFrameCompletionBoundaryTests
{
	private static byte[] Tail( string change )
	{
		if ( change == "channels" ) return Mp2DecoderTests.SilentFrame( 10, 0 );
		if ( change == "layer" ) return Layer1TestFrames.Frame();
		var frame = Mp2DecoderTests.SilentFrame();
		if ( change == "rate" ) { frame[2] |= 4; Array.Resize( ref frame, 144 * 48000 / 24000 ); }
		else frame[3] |= 1;
		return frame;
	}
	[DataTestMethod]
	[DataRow( "rate" )]
	[DataRow( "channels" )]
	[DataRow( "layer" )]
	[DataRow( "emphasis" )]
	public async Task SupportedPartialTailDoesNotChangeTheCompletedPrefixFormat( string change )
	{
		var prefix = Mp2DecoderTests.SilentFrame(); var expected = Mp2Decoder.Decode( prefix ); var tail = Tail( change );
		foreach ( var length in new[] { 4, 5, tail.Length - 1 } )
		{
			var data = prefix.Concat( tail[..length] ).ToArray(); var audio = Mp2Decoder.Decode( data ); var cooperative = await Mp2Decoder.DecodeCooperativelyAsync( data, 1, () => Task.CompletedTask );
			Assert.AreEqual( 1, audio.FrameCount ); Assert.AreEqual( length, audio.TrailingBytes ); Assert.AreEqual( expected.SampleRate, audio.SampleRate ); Assert.AreEqual( expected.Channels, audio.Channels ); Assert.AreEqual( expected.Emphasis, audio.Emphasis ); Assert.AreEqual( 0, audio.CrcChecks );
			CollectionAssert.AreEqual( expected.Samples, audio.Samples ); CollectionAssert.AreEqual( audio.Samples, cooperative.Samples ); Assert.AreEqual( audio.TrailingBytes, cooperative.TrailingBytes );
		}
	}
	[DataTestMethod]
	[DataRow( "rate" )]
	[DataRow( "channels" )]
	[DataRow( "layer" )]
	[DataRow( "emphasis" )]
	public void CompleteFormatChangingTailRemainsUnsupported( string change )
	{
		var data = Mp2DecoderTests.SilentFrame().Concat( Tail( change ) ).ToArray(); Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( data ) );
	}
	[TestMethod]
	public void UnsupportedOrMalformedTailHeadersAreNotSilentlyTreatedAsPadding()
	{
		var prefix = Mp2DecoderTests.SilentFrame(); var unsupported = Mp2DecoderTests.SilentFrame(); unsupported[2] &= 0x0F;
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( prefix.Concat( unsupported[..4] ).ToArray() ) );
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( prefix.Concat( new byte[] { 0, 0, 0, 0 } ).ToArray() ) );
	}
	[TestMethod]
	public async Task FormerLayerSwitchCloneIsAnExplicit136BytePartialTail()
	{
		var frame = Layer1TestFrames.Frame(); Assert.AreEqual( 136, frame.Length ); var mutated = (byte[])frame.Clone(); mutated[1] = (byte)((mutated[1] & ~6) | 4);
		var data = frame.Concat( mutated ).ToArray(); var expected = Mp2Decoder.Decode( frame ); var audio = Mp2Decoder.Decode( data ); var cooperative = await Mp2Decoder.DecodeCooperativelyAsync( data, 1, () => Task.CompletedTask );
		Assert.AreEqual( 1, audio.FrameCount ); Assert.AreEqual( 136, audio.TrailingBytes ); Assert.AreEqual( expected.SampleRate, audio.SampleRate ); Assert.AreEqual( expected.Channels, audio.Channels ); Assert.AreEqual( expected.CrcChecks, audio.CrcChecks ); CollectionAssert.AreEqual( expected.Samples, audio.Samples ); CollectionAssert.AreEqual( audio.Samples, cooperative.Samples ); Assert.AreEqual( 136, cooperative.TrailingBytes );
	}

}
