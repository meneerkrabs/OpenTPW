using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class AudioDecodeSchedulingTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static byte[] Repeat( byte[] frame, int count ) => Enumerable.Range( 0, count ).SelectMany( _ => frame ).ToArray();

	private static void EqualAudio( Mp2Audio expected, Mp2Audio actual )
	{
		Assert.AreEqual( (expected.SampleRate, expected.Channels, expected.FrameCount, expected.TrailingBytes,
			expected.CrcChecks, expected.CrcFailures, expected.Emphasis),
			(actual.SampleRate, actual.Channels, actual.FrameCount, actual.TrailingBytes,
			actual.CrcChecks, actual.CrcFailures, actual.Emphasis) );
		CollectionAssert.AreEqual( expected.Samples, actual.Samples );
	}

	private static byte[] LayerTwoFrame( int mode, int emphasis, bool crc )
	{
		var frame = new byte[144 * 48000 / 22050];
		var word = 0xFFE00000u | 2u << 19 | 2u << 17 | (crc ? 0u : 1u) << 16 | 6u << 12 | (uint)mode << 6 | (uint)emphasis;
		for ( var index = 0; index < 4; index++ )
			frame[index] = (byte)(word >> (24 - index * 8));
		return frame;
	}

	private static void PatchCrc( byte[] frame, int protectedBits )
	{
		var crc = 0xFFFF;
		foreach ( var (start, length) in new[] { (16, 16), (48, protectedBits) } )
			for ( var bit = start; bit < start + length; bit++ )
			{
				crc ^= ((frame[bit >> 3] >> (7 - (bit & 7))) & 1) << 15;
				crc = (crc & 0x8000) != 0 ? (crc << 1 ^ 0x8005) & 0xFFFF : crc << 1 & 0xFFFF;
			}
		frame[4] = (byte)(crc >> 8);
		frame[5] = (byte)crc;
	}

	[TestMethod]
	public async Task CooperativeDecodePreservesPcmCrcEmphasisModesAndTrailingBytes()
	{
		foreach ( var layer in new[] { 1, 2 } )
			foreach ( var mode in layer == 1 ? new[] { 3, 0, 1, 2 } : new[] { 3, 0, 2 } )
				foreach ( var emphasis in new[] { 0, 1, 3 } )
					foreach ( var crc in new[] { false, true } )
					{
						var frame = layer == 1
							? Layer1TestFrames.Frame( allocation: 1, mode: mode, bitrateIndex: 8, emphasis: emphasis, crc: crc )
							: LayerTwoFrame( mode, emphasis, crc );
						if ( crc )
							PatchCrc( frame, (layer == 1 ? 128 : 135) * (mode == 3 ? 1 : 2) );
						var stream = Repeat( frame, 9 ).Concat( frame.Take( 3 ) ).ToArray();
						EqualAudio( Mp2Decoder.Decode( stream ), await Mp2Decoder.DecodeCooperativelyAsync( stream, 4, () => Task.CompletedTask ) );
						if ( crc )
						{
							stream[4] ^= 1;
							EqualAudio( Mp2Decoder.Decode( stream ), await Mp2Decoder.DecodeCooperativelyAsync( stream, 2, () => Task.CompletedTask ) );
						}
					}
	}

	[TestMethod]
	public async Task CooperativeDecodeYieldsBetweenBoundedFrameBatches()
	{
		var stream = Repeat( Layer1TestFrames.Frame( allocation: 1 ), 10 );
		var gate = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
		var yields = 0;
		var pending = Mp2Decoder.DecodeCooperativelyAsync( stream, 4, () => ++yields == 1 ? gate.Task : Task.CompletedTask );
		Assert.AreEqual( 1, yields );
		Assert.IsFalse( pending.IsCompleted, "the decode returns control while the scheduler gate is pending" );
		gate.SetResult();
		EqualAudio( Mp2Decoder.Decode( stream ), await pending );
		Assert.AreEqual( 2, yields, "ten complete frames require three batches of at most four frames" );
		await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>( () => Mp2Decoder.DecodeCooperativelyAsync( stream, 0 ) );
		await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>( () => Mp2Decoder.DecodeCooperativelyAsync( stream, 9 ) );
	}

	[TestMethod]
	public async Task CooperativeDecodeKeepsTheSynchronousRejectionRules()
	{
		var frame = Layer1TestFrames.Frame();
		foreach ( var stream in new[] { Array.Empty<byte>(), new byte[4], frame.Take( 12 ).ToArray(),
			frame.Concat( Layer1TestFrames.Frame( mode: 0 ) ).ToArray(),
			frame.Concat( LayerTwoFrame( 3, 0, false ) ).ToArray(),
			Layer1TestFrames.Frame( emphasis: 2 ), Layer1TestFrames.Frame( allocation: 15, bitrateIndex: 14 ),
			new byte[Mp2Decoder.MaximumInputBytes + 1] } )
		{
			Exception? synchronous = null, cooperative = null;
			try { Mp2Decoder.Decode( stream ); } catch ( Exception exception ) { synchronous = exception; }
			try { await Mp2Decoder.DecodeCooperativelyAsync( stream, 2, () => Task.CompletedTask ); } catch ( Exception exception ) { cooperative = exception; }
			Assert.IsNotNull( synchronous );
			Assert.IsNotNull( cooperative );
			Assert.AreEqual( synchronous.GetType(), cooperative.GetType() );
			Assert.AreEqual( synchronous.Message, cooperative.Message );
		}
	}

	[TestMethod]
	public async Task OriginalLayerOneAndLayerTwoPcmIsIdenticalAcrossSchedulingPaths()
	{
		var bank = Mp2DecoderTests.OpenSpeechBank();
		foreach ( var name in new[] { "z_error.mp2", "sp_001.mp2" } )
		{
			var stream = bank.soundFiles.Single( file => file.Name == name ).FrameData;
			EqualAudio( Mp2Decoder.Decode( stream ), await Mp2Decoder.DecodeCooperativelyAsync( stream, 4, () => Task.CompletedTask ) );
		}
	}

	private static SoundCategory MusicCategory() => SoundCategory.TryLoad(
		new BaseFileSystem( StringTableTests.OriginalDataDirectory() ), "/levels/jungle/Music", "cat_music" )!;

	private static SoundCategory EmptyCategory()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-audio-cache-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			File.WriteAllBytes( Path.Combine( directory, "cat_musicSFX.map" ), AudioTests.CreateCatalog() );
			File.WriteAllBytes( Path.Combine( directory, "cat_musicBANK.map" ), new byte[28] );
			return SoundCategory.TryLoad( new BaseFileSystem( directory ), "", "cat_music" )!;
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public async Task RetainedOriginalMusicAvoidsAnotherDecodeAndPreservesItsPcm()
	{
		var category = MusicCategory();
		Assert.IsNotNull( category );
		var sample = category.Catalog.Find( GameAudio.ParkMusicEvent )!.Sounds[0].Samples[0];
		using var mixer = new AudioMixer( null );
		var system = new SoundEventSystem( mixer, 11 );
		var yields = 0;
		var first = await system.DecodeSentenceAsync( category, sample, cooperative: true, yieldBatch: () => { yields++; return Task.CompletedTask; } );
		Assert.IsNotNull( first );
		Assert.IsTrue( first.SampleFrames >= SoundEventSystem.CacheFrameLimit );
		Assert.IsTrue( yields > 0 );
		EqualAudio( Mp2Decoder.Decode( category.GetBank( sample.BankNumber )!.GetMpeg( sample.SampleNumber )! ), first );
		var again = await system.DecodeSentenceAsync( category, sample, cooperative: true, yieldBatch: () => throw new AssertFailedException( "a cached music sample must not decode again" ) );
		Assert.AreSame( first, again );
		Assert.AreEqual( first.Samples.LongLength * sizeof( short ), system.RetainedSentenceBytes );
		Assert.AreEqual( 11u, system.Seed );
	}

	[TestMethod]
	public async Task LongSentencePcmEvictionIsBoundedAndKeepsRecentlyUsedAndShortSamples()
	{
		var category = EmptyCategory();
		using var mixer = new AudioMixer( null );
		var system = new SoundEventSystem( mixer );
		var longAudio = new Mp2Audio( 22050, 2, new short[SoundEventSystem.LongCacheByteLimit / sizeof( short ) / 2], 1, 0 );
		var sample = new SoundSample( 900000, 65535, 0, 1, 0 );
		var first = sample;
		var second = sample with { SampleNumber = sample.SampleNumber + 1 };
		var third = sample with { SampleNumber = sample.SampleNumber + 2 };
		system.RememberDecoded( category, first, longAudio, retainLong: true );
		system.RememberDecoded( category, second, longAudio, retainLong: true );
		Assert.AreEqual( SoundEventSystem.LongCacheByteLimit, system.RetainedSentenceBytes );
		Assert.AreSame( longAudio, await system.DecodeSentenceAsync( category, first, cooperative: true ) );
		system.RememberDecoded( category, third, longAudio, retainLong: true );
		Assert.AreEqual( SoundEventSystem.LongCacheByteLimit, system.RetainedSentenceBytes );
		Assert.AreSame( longAudio, await system.DecodeSentenceAsync( category, first, cooperative: true ) );
		Assert.IsNull( await system.DecodeSentenceAsync( category, second, cooperative: true ), "the least recently used sample was evicted" );
		var smallSample = sample with { SampleNumber = sample.SampleNumber + 3 };
		var shortAudio = new Mp2Audio( 22050, 1, new short[32], 1, 0 );
		system.RememberDecoded( category, smallSample, shortAudio, retainLong: true );
		var oversized = new Mp2Audio( 22050, 1, new short[SoundEventSystem.LongCacheByteLimit / sizeof( short ) + 1], 1, 0 );
		var oversizedSample = sample with { SampleNumber = sample.SampleNumber + 4 };
		system.RememberDecoded( category, oversizedSample, oversized, retainLong: true );
		Assert.AreEqual( SoundEventSystem.LongCacheByteLimit, system.RetainedSentenceBytes );
		Assert.IsNull( await system.DecodeSentenceAsync( category, oversizedSample, cooperative: true ), "an oversized sample is not silently retained outside the budget" );
		Assert.AreSame( shortAudio, system.Decode( category, smallSample ) );
	}
}
