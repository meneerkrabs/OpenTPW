using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.Layer1TestFrames;

namespace OpenTPW.Tests;

/// <summary>
/// CRC check-word verification (ISO/IEC 11172-3 2.4.3.1, Annex B Table 3-B.5) and
/// de-emphasis (header emphasis '01' = 50/15 µs, '11' = CCITT J.17) of the MPEG
/// Layer I/II decoder, plus a protection/emphasis inventory of the original banks.
/// </summary>
[TestClass]
public class Mp2ErrorProtectionTests
{
	private sealed class BitWriter
	{
		private readonly List<bool> bits = new();
		public void Write( int value, int count )
		{
			for ( var index = count - 1; index >= 0; index-- )
				bits.Add( ((value >> index) & 1) != 0 );
		}
		public byte[] ToFrame( int length )
		{
			var data = new byte[length];
			for ( var index = 0; index < bits.Count; index++ )
				if ( bits[index] )
					data[index / 8] |= (byte)(0x80 >> (index % 8));
			return data;
		}
	}

	// ISO/IEC 11172-3 2.4.3.1: CRC-16, G(x) = x^16 + x^15 + x^2 + 1, all-ones start,
	// bits consumed most significant first. The byte-aligned form is the published
	// catalogue algorithm CRC-16/MPEG-2 (check value 0xAEE7 for ASCII "123456789").
	private static ushort ComputeCrc16( byte[] data, int startBit, int count )
	{
		var crc = 0xFFFF;
		for ( var index = startBit; index < startBit + count; index++ )
		{
			crc ^= ((data[index >> 3] >> (7 - (index & 7))) & 1) << 15;
			crc = (crc & 0x8000) != 0 ? (crc << 1 ^ 0x8005) & 0xFFFF : crc << 1 & 0xFFFF;
		}
		return (ushort)crc;
	}

	// CRC over header bits 16-31 and the protected audio_data bits (the stored word
	// at bits 32-47 is skipped), written into the frame's check-word position.
	private static void PatchFrameCrc( byte[] frame, int protectedBits )
	{
		var chained = ChainCrc16( frame, 16, 16, 48, protectedBits );
		frame[4] = (byte)(chained >> 8);
		frame[5] = (byte)(chained & 0xFF);
	}

	private static ushort ChainCrc16( byte[] data, int firstBit, int firstCount, int secondBit, int secondCount )
	{
		var crc = 0xFFFF;
		void Step( int startBit, int count )
		{
			for ( var index = startBit; index < startBit + count; index++ )
			{
				crc ^= ((data[index >> 3] >> (7 - (index & 7))) & 1) << 15;
				crc = (crc & 0x8000) != 0 ? (crc << 1 ^ 0x8005) & 0xFFFF : crc << 1 & 0xFFFF;
			}
		}
		Step( firstBit, firstCount );
		Step( secondBit, secondCount );
		return (ushort)crc;
	}

	private static byte[] WithEmphasis( byte[] frame, int emphasis )
	{
		var copy = (byte[])frame.Clone();
		copy[3] = (byte)((copy[3] & 0xFC) | emphasis);
		return copy;
	}

	// MPEG-2 LSF Layer II frame, 22,050 Hz, 48 kbps, protection enabled, subband 0 of
	// every channel allocated to the 3-level grouped class (same layout as the speech
	// corpus), optional emphasis and mode.
	private static byte[] LayerTwoCrcFrame( int mode = 3, int emphasis = 0 )
	{
		var writer = new BitWriter();
		writer.Write( 0x7FF, 11 );
		writer.Write( 2, 2 ); // MPEG-2
		writer.Write( 2, 2 ); // Layer II
		writer.Write( 0, 1 ); // protection_bit 0: CRC present
		writer.Write( 6, 4 ); // 48 kbps
		writer.Write( 0, 2 ); // 22,050 Hz
		writer.Write( 0, 1 ); // padding
		writer.Write( 0, 1 ); // private
		writer.Write( mode, 2 );
		writer.Write( 0, 2 ); // mode extension
		writer.Write( 0, 2 ); // copyright, original
		writer.Write( emphasis, 2 );
		writer.Write( 0, 16 ); // crc_check placeholder
		var channels = mode == 3 ? 1 : 2;
		for ( var subband = 0; subband < 30; subband++ )
			for ( var channel = 0; channel < channels; channel++ )
				writer.Write( subband == 0 ? 1 : 0, subband < 4 ? 4 : subband < 11 ? 3 : 2 );
		for ( var subband = 0; subband < 30; subband++ )
			for ( var channel = 0; channel < channels; channel++ )
				if ( subband == 0 )
					writer.Write( 0, 2 ); // scfsi: three scalefactors
		for ( var subband = 0; subband < 30; subband++ )
			for ( var channel = 0; channel < channels; channel++ )
				if ( subband == 0 )
					for ( var index = 0; index < 3; index++ )
						writer.Write( 3, 6 );
		for ( var granule = 0; granule < 12; granule++ )
			for ( var subband = 0; subband < 30; subband++ )
				for ( var channel = 0; channel < channels; channel++ )
					if ( subband == 0 )
						writer.Write( 26, 5 ); // grouped code: three samples of +2/3 units
		var frame = writer.ToFrame( 144 * 48000 / 22050 );
		PatchFrameCrc( frame, 135 * channels );
		return frame;
	}

	[TestMethod]
	public void CrcAlgorithmMatchesPublishedCheckValue()
	{
		Assert.AreEqual( (ushort)0xAEE7, ComputeCrc16( "123456789"u8.ToArray(), 0, 72 ) );
	}

	[TestMethod]
	public void LayerOneCrcWordPassesVerification()
	{
		// The pinned word is computed independently (Python) from the published
		// algorithm for this exact frame; 128 protected allocation bits, single channel.
		var frame = Frame( allocation: 1, sampleCode: 2, crc: true );
		Assert.AreEqual( (ushort)0xE456, ChainCrc16( frame, 16, 16, 48, 128 ) );
		PatchFrameCrc( frame, 128 );
		var stream = frame.Concat( frame ).ToArray();
		var audio = Mp2Decoder.Decode( stream );
		Assert.AreEqual( 2, audio.FrameCount );
		Assert.AreEqual( 2, audio.CrcChecks );
		Assert.AreEqual( 0, audio.CrcFailures );
		Assert.AreEqual( 0, audio.Emphasis );
		// The same stream with both frames' words cleared must fail both frames.
		var zeroed = (byte[])stream.Clone();
		zeroed[4] = zeroed[5] = 0;
		zeroed[140] = zeroed[141] = 0;
		audio = Mp2Decoder.Decode( zeroed );
		Assert.AreEqual( 2, audio.CrcChecks );
		Assert.AreEqual( 2, audio.CrcFailures );
		Assert.AreEqual( 2 * Mp2Decoder.LayerOneSamplesPerFrame, audio.Samples.Length );
	}

	[TestMethod]
	public void LayerOneCrcCorruptionIsCountedAndStillDecoded()
	{
		var frame = Frame( allocation: 1, sampleCode: 2, crc: true );
		PatchFrameCrc( frame, 128 );
		// Flip one protected audio_data bit (an allocation bit at bit 49): the frame
		// still decodes (mismatch policy: count, not mute).
		frame[6] ^= 0x40;
		var audio = Mp2Decoder.Decode( frame );
		Assert.AreEqual( 1, audio.CrcChecks );
		Assert.AreEqual( 1, audio.CrcFailures );
		Assert.AreEqual( Mp2Decoder.LayerOneSamplesPerFrame, audio.Samples.Length );
	}

	[TestMethod]
	public void LayerTwoCrcWordCoversAllocationAndMaximumScfsi()
	{
		// Single channel: 75 allocation bits + 2 × 30 maximum scfsi bits = 135.
		var mono = LayerTwoCrcFrame();
		var audio = Mp2Decoder.Decode( mono );
		Assert.AreEqual( 1, audio.CrcChecks );
		Assert.AreEqual( 0, audio.CrcFailures );
		// Other modes: 270 protected bits for two channels.
		var stereo = LayerTwoCrcFrame( mode: 0 );
		audio = Mp2Decoder.Decode( stereo );
		Assert.AreEqual( 1, audio.CrcChecks );
		Assert.AreEqual( 0, audio.CrcFailures );
		// A corrupted allocation bit is counted, and the frame still decodes.
		var corrupted = LayerTwoCrcFrame();
		corrupted[6] ^= 0x80;
		audio = Mp2Decoder.Decode( corrupted );
		Assert.AreEqual( 1, audio.CrcChecks );
		Assert.AreEqual( 1, audio.CrcFailures );
		Assert.AreEqual( Mp2Decoder.SamplesPerFrame, audio.Samples.Length );
	}

	[TestMethod]
	public void ReservedEmphasisIsRejectedForLayerTwo()
	{
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( LayerTwoCrcFrame( emphasis: 2 ) ) );
	}

	// The published de-emphasis curves as bilinear-transformed first-order shelves:
	// '01' inverts the 50/15 µs broadcast pre-emphasis; '11' inverts ITU-T J.17's
	// insertion loss 10·log10((75+(w/3000)^2)/(1+(w/3000)^2)) dB (time constants
	// 1/3000 s and 1/(3000·sqrt(75)) s, 18.75 dB shelf), both at unity DC gain.
	private static (double B0, double B1, double A1) ShelfCoefficients( int emphasis, int sampleRate )
	{
		var zero = emphasis == 1 ? 15e-6 : 1.0 / (3000 * Math.Sqrt( 75 ));
		var pole = emphasis == 1 ? 50e-6 : 1.0 / 3000;
		var highZero = 2.0 * sampleRate * zero;
		var highPole = 2.0 * sampleRate * pole;
		return ((1 + highZero) / (1 + highPole), (1 - highZero) / (1 + highPole), (1 - highPole) / (1 + highPole));
	}

	// Applies the shelf to already-quantized plain samples, per channel (the decoder
	// filters the float synthesis output, so ±1 LSB of rounding difference is expected).
	private static short[] ApplyShelf( short[] plain, int channels, int emphasis, int sampleRate )
	{
		var (b0, b1, a1) = ShelfCoefficients( emphasis, sampleRate );
		var previousInput = new double[channels];
		var previousOutput = new double[channels];
		var output = new short[plain.Length];
		for ( var index = 0; index < plain.Length; index++ )
		{
			var channel = index % channels;
			var input = (double)plain[index];
			var value = b0 * input + b1 * previousInput[channel] - a1 * previousOutput[channel];
			previousInput[channel] = input;
			previousOutput[channel] = value;
			output[index] = (short)Math.Clamp( MathF.Round( (float)value ), short.MinValue, short.MaxValue );
		}
		return output;
	}

	[TestMethod]
	public void DeEmphasisShelfEndpointsMatchPublishedCurves()
	{
		// Coefficients pinned (independent Python of the published constants): the DC
		// gain is exactly 1 and the Nyquist gain equals the curves' shelf limits,
		// -10.458 dB = 20·log10(15/50) and -18.751 dB = J.17's DC insertion loss.
		var (b0, b1, a1) = ShelfCoefficients( 1, 22050 );
		Assert.AreEqual( 0.518408736349, b0, 1e-9 );
		Assert.AreEqual( 0.105616224649, b1, 1e-9 );
		Assert.AreEqual( -0.375975039002, a1, 1e-9 );
		Assert.AreEqual( 1.0, (b0 + b1) / (1 + a1), 1e-9 );
		Assert.AreEqual( 0.29999, (b0 - b1) / (1 - a1), 1e-4 );
		(b0, b1, a1) = ShelfCoefficients( 3, 22050 );
		Assert.AreEqual( 0.171809540855, b0, 1e-9 );
		Assert.AreEqual( -0.044421005823, b1, 1e-9 );
		Assert.AreEqual( -0.872611464968, a1, 1e-9 );
		Assert.AreEqual( 1.0, (b0 + b1) / (1 + a1), 1e-9 );
		Assert.AreEqual( 0.11547005, (b0 - b1) / (1 - a1), 1e-6 ); // 1/sqrt(75) = 10^(-18.75/20)
	}

	[TestMethod]
	public void LayerOneDeEmphasisAppliesThePublishedShelf()
	{
		foreach ( var (emphasis, version, rate) in new[] { (1, 2, 22050), (3, 2, 22050), (1, 3, 44100) } )
		{
			var plainFrame = Frame( allocation: 1, sampleCode: 2, version: version, rateIndex: 0, bitrateIndex: version == 3 ? 8 : 4 );
			var plainFrames = Enumerable.Repeat( plainFrame, 4 ).SelectMany( bytes => bytes ).ToArray();
			var emphasizedFrames = Enumerable.Repeat( WithEmphasis( plainFrame, emphasis ), 4 ).SelectMany( bytes => bytes ).ToArray();
			var plain = Mp2Decoder.Decode( plainFrames );
			var emphasized = Mp2Decoder.Decode( emphasizedFrames );
			Assert.AreEqual( emphasis, emphasized.Emphasis );
			Assert.AreEqual( rate, emphasized.SampleRate );
			Assert.AreEqual( plain.Samples.Length, emphasized.Samples.Length );
			var expected = ApplyShelf( plain.Samples, 1, emphasis, rate );
			for ( var index = 0; index < expected.Length; index++ )
				Assert.IsTrue( Math.Abs( emphasized.Samples[index] - expected[index] ) <= 1, $"emphasis {emphasis}, sample {index}" );
		}
	}

	[TestMethod]
	public void LayerTwoDeEmphasisAppliesThePublishedShelf()
	{
		foreach ( var emphasis in new[] { 1, 3 } )
		{
			var plainFrame = LayerTwoCrcFrame( mode: 3 );
			var emphasizedFrame = LayerTwoCrcFrame( mode: 3, emphasis: emphasis );
			var plain = Mp2Decoder.Decode( Enumerable.Repeat( plainFrame, 3 ).SelectMany( bytes => bytes ).ToArray() );
			var emphasized = Mp2Decoder.Decode( Enumerable.Repeat( emphasizedFrame, 3 ).SelectMany( bytes => bytes ).ToArray() );
			Assert.AreEqual( emphasis, emphasized.Emphasis );
			var expected = ApplyShelf( plain.Samples, 1, emphasis, 22050 );
			for ( var index = 0; index < expected.Length; index++ )
				Assert.IsTrue( Math.Abs( emphasized.Samples[index] - expected[index] ) <= 1, $"emphasis {emphasis}, sample {index}" );
		}
	}

	[TestMethod]
	public void StereoDeEmphasisFiltersEachChannelIndependently()
	{
		// Left scale 3, right scale 6: the right channel is half as loud, so the two
		// channels' filter states stay distinguishable.
		var frame = Frame( allocation: 2, mode: 0, scale: 3, rightScale: 6, sampleCode: 4, rightSampleCode: 2, bitrateIndex: 14 );
		var plain = Mp2Decoder.Decode( Enumerable.Repeat( frame, 3 ).SelectMany( bytes => bytes ).ToArray() );
		var emphasized = Mp2Decoder.Decode( Enumerable.Repeat( WithEmphasis( frame, 1 ), 3 ).SelectMany( bytes => bytes ).ToArray() );
		var expected = ApplyShelf( plain.Samples, 2, 1, 22050 );
		for ( var index = 0; index < expected.Length; index++ )
			Assert.IsTrue( Math.Abs( emphasized.Samples[index] - expected[index] ) <= 1, $"sample {index}" );
	}

	[TestMethod]
	public void DeEmphasisLeavesSteadyDcUnchanged()
	{
		// Both characteristics have exactly unity gain at DC, so the steady tone of the
		// deterministic frames comes out at the plain decode's value.
		foreach ( var emphasis in new[] { 1, 3 } )
		{
			var frame = WithEmphasis( Frame( allocation: 1, sampleCode: 2 ), emphasis );
			var audio = Mp2Decoder.Decode( Enumerable.Repeat( frame, 4 ).SelectMany( bytes => bytes ).ToArray() );
			var steady = audio.Samples.Skip( 2 * Mp2Decoder.LayerOneSamplesPerFrame );
			Assert.IsTrue( steady.All( sample => Math.Abs( sample - 2.0 / 3 * 32768 ) <= 2 ), $"emphasis {emphasis}" );
		}
	}

	[TestMethod]
	public void EmphasisChangeMidStreamIsRejected()
	{
		var first = WithEmphasis( Frame( allocation: 1, sampleCode: 2 ), 1 );
		var second = Frame( allocation: 1, sampleCode: 2 );
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( first.Concat( second ).ToArray() ) );
	}

	[TestMethod]
	public void OriginalCorpusCarriesNoCrcWordAndNoEmphasis()
	{
		// Frame-level inventory of every SDT bank under OPENTPW_GAME_PATH: counts the
		// header protection bit and emphasis of every MPEG frame and checks the CRC of
		// protected ones. The shipped editions use neither (see docs/LIPS.md).
		var dataPath = Mp2DecoderTests.SpeechDirectory();
		var banks = Directory.EnumerateFiles( Path.GetDirectoryName( Path.GetDirectoryName( dataPath ) )!, "*",
			SearchOption.AllDirectories ).Where( path => string.Equals( Path.GetExtension( path ), ".sdt", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		if ( banks.Length == 0 )
			Assert.Inconclusive( "No .SDT banks below the game data directory." );
		var frames = 0;
		var protectedFrames = 0;
		var crcFailures = 0;
		var nonMpegEntries = 0;
		var emphasis = new int[4];
		foreach ( var path in banks )
		{
			foreach ( var clip in new SdtArchive( path ).soundFiles )
			{
				var (count, protectedCount, failures, emphasised) = WalkFrames( clip.FrameData );
				if ( count == 0 )
				{
					nonMpegEntries++;
					continue;
				}
				frames += count;
				protectedFrames += protectedCount;
				crcFailures += failures;
				for ( var value = 0; value < 4; value++ )
					emphasis[value] += emphasised[value];
			}
		}
		TestContext.WriteLine( $"{banks.Length} banks: {frames} frames, {protectedFrames} with CRC, {crcFailures} CRC failures, emphasis [{string.Join( ", ", emphasis )}], {nonMpegEntries} non-MPEG entries" );
		Assert.IsTrue( frames > 0 );
		Assert.AreEqual( 0, emphasis[1] + emphasis[2] + emphasis[3], "frames with nonzero emphasis" );
		Assert.AreEqual( 0, protectedFrames, "frames with a CRC check word" );
		Assert.AreEqual( 0, crcFailures );

		// Walks a raw SDT entry's frame stream with the decoder's header rules.
		static (int Frames, int Protected, int Failures, int[] Emphasis) WalkFrames( byte[] data )
		{
			var frames = 0;
			var @protected = 0;
			var failures = 0;
			var emphasis = new int[4];
			var bitrates = new[] { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 };
			var offset = 0;
			while ( data.Length - offset >= 4 )
			{
				var word = data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3];
				if ( (word & 0xFFE00000) != 0xFFE00000 )
					break;
				var version = (word >> 19) & 3;
				var layer = (word >> 17) & 3;
				var bitrateIndex = (word >> 12) & 15;
				var rateIndex = (word >> 10) & 3;
				if ( version == 1 || layer == 0 || bitrateIndex is 0 or 15 || rateIndex == 3 )
					break;
				var bitrate = layer == 3
					? bitrates[bitrateIndex]
					: new[] { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 }[bitrateIndex];
				if ( version == 3 && layer == 3 )
					bitrate = new[] { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 }[bitrateIndex];
				var sampleRate = new[] { 22050, 24000, 16000 }[rateIndex] * (version == 3 ? 2 : 1);
				var frameBytes = layer == 3
					? (12 * bitrate * 1000 / sampleRate + ((word >> 9) & 1)) * 4
					: 144 * bitrate * 1000 / sampleRate + ((word >> 9) & 1);
				if ( frameBytes > data.Length - offset )
					break;
				var channels = ((word >> 6) & 3) == 3 ? 1 : 2;
				emphasis[word & 3]++;
				if ( (word & 0x10000) == 0 )
				{
					@protected++;
					var protectedBits = (layer == 3 ? 128 : 135) * channels;
					var frame = data[offset..(offset + frameBytes)];
					// Chain the header range and the protected audio_data range; the
					// stored word at bits 32-47 lies between them and is not included.
					var chained = 0xFFFF;
					void Step( int start, int count )
					{
						for ( var index = start; index < start + count; index++ )
						{
							chained ^= ((frame[index >> 3] >> (7 - (index & 7))) & 1) << 15;
							chained = (chained & 0x8000) != 0 ? (chained << 1 ^ 0x8005) & 0xFFFF : chained << 1 & 0xFFFF;
						}
					}
					Step( 16, 16 );
					Step( 48, protectedBits );
					if ( chained != (frame[4] << 8 | frame[5]) )
						failures++;
				}
				offset += frameBytes;
				frames++;
			}
			return (frames, @protected, failures, emphasis);
		}
	}

	public TestContext TestContext { get; set; } = null!;
}
