using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LayerTwoCrcIndependentReviewTests
{
	// An independent encoder records normative fields. Polynomial long division
	// computes the checkword, without production tables/ProtectedBits/StepCrc.
	private static ushort Remainder( IReadOnlyList<int> protectedMessage )
	{
		BigInteger message = 0;
		foreach ( var bit in protectedMessage ) message = (message << 1) | bit;
		var dividend = ((BigInteger)ushort.MaxValue << protectedMessage.Count) ^ (message << 16);
		while ( dividend.GetBitLength() > 16 )
			dividend ^= (BigInteger)0x18005 << (int)(dividend.GetBitLength() - 17);
		return (ushort)dividend;
	}
	private static (byte[] Bytes, int ScaleStart, ushort Word) Packet( int mode, int channel, int band, int scfsi, bool protectedFrame = true )
	{
		var bits = new List<int>(); var protectedMessage = new List<int>();
		void Emit( int value, int width, bool covered = false )
		{
			for ( var bit = width - 1; bit >= 0; bit-- )
			{
				var valueBit = (value >> bit) & 1; bits.Add( valueBit ); if ( covered ) protectedMessage.Add( valueBit );
			}
		}
		Emit( 0x7ff, 11 ); Emit( 2, 2 ); Emit( 2, 2 ); Emit( protectedFrame ? 0 : 1, 1 );
		Emit( 14, 4, true ); Emit( 0, 2, true ); Emit( 0, 1, true ); Emit( 0, 1, true ); Emit( mode, 2, true ); Emit( 0, 6, true );
		if ( protectedFrame ) Emit( 0, 16 );
		var channels = mode == 3 ? 1 : 2;
		for ( var sb = 0; sb < 30; sb++ ) for ( var ch = 0; ch < channels; ch++ )
			Emit( sb == band && ch == channel ? 1 : 0, sb < 4 ? 4 : sb < 11 ? 3 : 2, true );
		Emit( scfsi, 2, true ); // Only the selected nonzero channel/subband transmits it.
		var scaleStart = bits.Count;
		var factors = scfsi == 0 ? 3 : scfsi == 2 ? 1 : 2;
		for ( var i = 0; i < factors; i++ ) Emit( 3 + i, 6 );
		for ( var granule = 0; granule < 12; granule++ ) Emit( 26, 5 );
		var bytes = new byte[144 * 160000 / 22050];
		for ( var i = 0; i < bits.Count; i++ ) bytes[i / 8] |= (byte)(bits[i] << (7 - i % 8));
		var word = Remainder( protectedMessage );
		if ( protectedFrame ) { bytes[4] = (byte)(word >> 8); bytes[5] = (byte)word; }
		return (bytes, scaleStart, word);
	}
	[DataTestMethod]
	[DataRow( 0, 0x7f2f )]
	[DataRow( 1, 0xff2a )]
	[DataRow( 2, 0xff20 )]
	[DataRow( 3, 0x7f25 )]
	public void LastMonoSubbandAndAllScfsiChoicesMatchIndependentLiteralWords( int scfsi, int expected )
	{
		var packet = Packet( 3, 0, 29, scfsi ); Assert.AreEqual( (ushort)expected, packet.Word );
		var audio = Mp2Decoder.Decode( packet.Bytes ); Assert.AreEqual( 1, audio.CrcChecks ); Assert.AreEqual( 0, audio.CrcFailures );
		Assert.AreEqual( 1152, audio.SampleFrames ); Assert.IsTrue( audio.Samples.Any( value => value != 0 ) );
		CollectionAssert.AreEqual( Mp2Decoder.Decode( Packet( 3, 0, 29, scfsi, false ).Bytes ).Samples, audio.Samples );
	}
	[DataTestMethod]
	[DataRow( 0, 0, 0xcd37 )]
	[DataRow( 1, 29, 0x0aaa )]
	public void EqualSpanUnequalDualPositionsHaveDifferentLiteralWordsAndSilentOtherChannel( int channel, int band, int expected )
	{
		var packet = Packet( 2, channel, band, 2 ); Assert.AreEqual( (ushort)expected, packet.Word );
		var audio = Mp2Decoder.Decode( packet.Bytes ); Assert.AreEqual( 1, audio.CrcChecks ); Assert.AreEqual( 0, audio.CrcFailures );
		Assert.IsTrue( audio.Samples.Where( ( _, index ) => index % 2 != channel ).All( value => value == 0 ) );
		Assert.IsTrue( audio.Samples.Where( ( _, index ) => index % 2 == channel ).Any( value => value != 0 ) );
	}
	[TestMethod]
	public void LastProtectedScfsiBitAndFirstExcludedScaleBitAreSeparatedPrecisely()
	{
		var original = Packet( 3, 0, 29, 2 ); var changedScfsi = Packet( 3, 0, 29, 3 );
		Assert.AreEqual( 48 + 75 + 2, original.ScaleStart ); Assert.AreEqual( original.ScaleStart, changedScfsi.ScaleStart );
		// SCFSI 10 -> 11 changes just the last protected bit; encode its valid extra factor.
		changedScfsi.Bytes[4] = original.Bytes[4]; changedScfsi.Bytes[5] = original.Bytes[5];
		var bad = Mp2Decoder.Decode( changedScfsi.Bytes ); Assert.AreEqual( 1, bad.CrcFailures ); Assert.AreEqual( 1152, bad.SampleFrames );
		var changedScale = original.Bytes.ToArray(); changedScale[original.ScaleStart / 8] ^= (byte)(1 << (7 - original.ScaleStart % 8));
		var good = Mp2Decoder.Decode( changedScale ); Assert.AreEqual( 0, good.CrcFailures ); Assert.AreEqual( 1152, good.SampleFrames );
		Assert.IsFalse( Mp2Decoder.Decode( original.Bytes ).Samples.SequenceEqual( good.Samples ), "An excluded valid scale change should alter PCM without becoming a CRC failure." );
	}
	[TestMethod]
	public void LastProtectedHeaderBitIsCheckedWhileValidEmphasisStillDecodes()
	{
		var packet = Packet( 3, 0, 29, 2 ); packet.Bytes[3] ^= 1;
		var audio = Mp2Decoder.Decode( packet.Bytes ); Assert.AreEqual( 1, audio.CrcFailures ); Assert.AreEqual( 1, audio.Emphasis ); Assert.AreEqual( 1152, audio.SampleFrames );
	}
}
