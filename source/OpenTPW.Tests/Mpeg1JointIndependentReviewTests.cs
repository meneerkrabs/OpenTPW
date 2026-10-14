using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.Mpeg1JointTestFrames;

namespace OpenTPW.Tests;

[TestClass]
public class Mpeg1JointIndependentReviewTests
{
	// Literal CRCs independently calculated from the sixteen protected header bits
	// followed by the specified allocation-bit count, seeded FFFF with polynomial 8005.
	[DataTestMethod]
	[DataRow( 4, 0, 0, 40, 0x9c44 )]
	[DataRow( 4, 0, 1, 52, 0xe49d )]
	[DataRow( 4, 0, 2, 52, 0xd497 )]
	[DataRow( 4, 0, 3, 52, 0xc491 )]
	[DataRow( 4, 2, 0, 52, 0x77a8 )]
	[DataRow( 4, 2, 1, 64, 0x751f )]
	[DataRow( 4, 2, 2, 76, 0xdc26 )]
	[DataRow( 4, 2, 3, 76, 0x4965 )]
	[DataRow( 8, 0, 0, 104, 0x41be )]
	[DataRow( 8, 0, 1, 120, 0xe5e2 )]
	[DataRow( 8, 0, 2, 135, 0x4fc5 )]
	[DataRow( 8, 0, 3, 147, 0x7634 )]
	[DataRow( 14, 0, 0, 110, 0xbfb3 )]
	[DataRow( 14, 0, 1, 126, 0x19f3 )]
	[DataRow( 14, 0, 2, 141, 0xd306 )]
	[DataRow( 14, 0, 3, 153, 0x4182 )]
	public void AllProfileExtensionCrcWordsMatchLiteralProtectedSpans( int index, int rate, int extension, int span, int crc )
	{
		var packet = Make( index, rate, extension, crc: true );
		Assert.AreEqual( span, packet.ProtectedAudioBits );
		Assert.AreEqual( crc, (packet.Data[4] << 8) | packet.Data[5] );
		var audio = Mp2Decoder.Decode( packet.Data );
		Assert.AreEqual( 1, audio.CrcChecks );
		Assert.AreEqual( 0, audio.CrcFailures );
		Assert.IsTrue( audio.Samples.All( sample => sample == 0 ) );
	}

	[DataTestMethod]
	[DataRow( 4, 0, 1, 8 )]
	[DataRow( 4, 2, 2, 12 )]
	public void ExtensionsBeyondProfileLimitPreserveIndependentChannelSamples( int index, int rate, int firstExtension, int limit )
	{
		var bands = new[] { new Band( limit - 1, 1, 2,
			new Channel( Samples: new[] { 2, 0, 1 } ),
			new Channel( First: 15, Samples: new[] { 4, 0, 2 } ) ) };
		var first = Make( index, rate, firstExtension, crc: true, bands: bands );
		Assert.AreEqual( limit, first.Bound );
		var expected = Mp2Decoder.Decode( first.Data );
		Assert.IsTrue( expected.Samples.Any( sample => sample != 0 ) );
		for ( var extension = firstExtension + 1; extension < 4; extension++ )
		{
			var packet = Make( index, rate, extension, crc: true, bands: bands );
			Assert.AreEqual( limit, packet.Bound );
			var audio = Mp2Decoder.Decode( packet.Data );
			CollectionAssert.AreEqual( expected.Samples, audio.Samples );
			Assert.AreEqual( 0, audio.CrcFailures );
		}
	}

	[TestMethod]
	public void SmallestLegalSharedRightFactorDoesNotReplaceLeftFactor()
	{
		var codes = new[] { 2, 0, 1 };
		var joint = Make( 8, bands: new Band( 4, Left: new Channel( First: 6, Samples: codes ), Right: new Channel( First: 62 ) ) );
		var stereo = Mpeg1LayerTwoTestFrames.Frame( 8, 0, 0, bands: new[] {
			new Mpeg1LayerTwoTestFrames.Band( 0, 4, FirstScale: 6, Codes: codes ),
			new Mpeg1LayerTwoTestFrames.Band( 1, 4, FirstScale: 62, Codes: codes ) } );
		var actual = Mp2Decoder.Decode( joint.Data ).Samples;
		CollectionAssert.AreEqual( Mp2Decoder.Decode( stereo.Data ).Samples, actual );
		Assert.IsTrue( actual.Where( ( _, i ) => i % 2 == 0 ).Any( sample => sample != 0 ) );
		Assert.IsTrue( actual.Where( ( _, i ) => i % 2 == 1 ).All( sample => sample == 0 ) );
	}
}
