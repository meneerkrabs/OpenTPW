using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// AUDIO-003 pitch (docs/AUDIO.md): every sample playback draws its pitch step from the sound's signed range with the shared chooser
/// (tp.exe 0x6bc170, applied through the mixer wrapper 0x6d2820) and plays at ratio 1 for step 0, otherwise 2^(sign * (|step| + 1) / 96).
/// The video of the original confirmed 141 playbacks on exactly the ratios of steps 0 to 5 of the range 0..6. Synthetic data.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AudioPitchTests
{
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, 7 );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		system.StopChains();
		mixer.Dispose();
	}

	private static SoundEntry Sound( sbyte minimumPitch, sbyte maximumPitch, SoundLink[]? links = null, ushort delay = 0 ) =>
		new( 100, 100, minimumPitch, maximumPitch, delay, delay, 1, 100, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, links ?? Array.Empty<SoundLink>(), Array.Empty<byte>() );

	private (SoundCategory Category, SoundEvent Event) Category( ushort flags, params SoundEntry[] sounds )
	{
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!;
		decoded[(category, 1, 1)] = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, 22050 ).ToArray(), 1, 0 );
		var soundEvent = new SoundEvent( 5, 0, flags, 0, sounds );
		Set( catalog, "Events", new[] { soundEvent } );
		Set( category, "Catalog", catalog );
		return (category, soundEvent);
	}

	[TestMethod]
	public void TheRatioTableIsOneStepOffAndSymmetric()
	{
		// [BIN:TPW-EXE:0x782f40] the floats in tp.exe: 1.007246 1.014545 1.021897 ... (index 0 is 2^(1/96)); 0x783540: 0.992806 0.985663 ...
		Assert.AreEqual( 1f, AudioVoice.PitchRatio( 0 ) );
		// some of the stored floats of tp.exe 0x782f40 and 0x783540 bit for bit (entry k is read for step k; entry 0 is never used);
		// they are the six-decimal values, a few ULP from the exact powers, so the table computed from that rule must reproduce them
		var positive = new uint[] { 0x3f81dc9c, 0x3f82cd85, 0x3f83c02b, 0x3f84b496, 0x3f85aac5, 0x3f86a2ba, 0x3f879c7c };
		var negative = new uint[] { 0x3f7c5469, 0x3f7a83b2, 0x3f78b652, 0x3f76ec39, 0x3f752578, 0x3f7361fe, 0x3f71a1ba };
		for ( var step = 1; step <= positive.Length; step++ )
		{
			Assert.AreEqual( positive[step - 1], BitConverter.SingleToUInt32Bits( AudioVoice.PitchRatio( step ) ), $"step {step}" );
			Assert.AreEqual( negative[step - 1], BitConverter.SingleToUInt32Bits( AudioVoice.PitchRatio( -step ) ), $"step -{step}" );
		}
		Assert.AreEqual( 0x40000000u, BitConverter.SingleToUInt32Bits( AudioVoice.PitchRatio( 95 ) ), "step 95 is an octave" );
		Assert.AreEqual( 0x3f000000u, BitConverter.SingleToUInt32Bits( AudioVoice.PitchRatio( -95 ) ) );
		Assert.AreEqual( 0x3eee2ae5u, BitConverter.SingleToUInt32Bits( AudioVoice.PitchRatio( -105 ) ), "a stored entry where the six decimals differ most from the power" );
		Assert.AreEqual( AudioVoice.PitchRatio( 109 ), AudioVoice.PitchRatio( int.MaxValue ), "a step beyond the table holds its last entry" );
		Assert.AreEqual( AudioVoice.PitchRatio( -109 ), AudioVoice.PitchRatio( int.MinValue ) );
		Assert.AreEqual( 1.014545f, AudioVoice.PitchRatio( 1 ), 1e-6f );
		Assert.AreEqual( 1.021897f, AudioVoice.PitchRatio( 2 ), 1e-6f );
		Assert.AreEqual( 1.044274f, AudioVoice.PitchRatio( 5 ), 1e-6f );
		Assert.AreEqual( 1.051841f, AudioVoice.PitchRatio( 6 ), 1e-6f );
		Assert.AreEqual( 0.985663f, AudioVoice.PitchRatio( -1 ), 1e-6f );
		Assert.AreEqual( 0.957603f, AudioVoice.PitchRatio( -5 ), 1e-6f );
		for ( var step = 1; step <= 100; step++ )
			Assert.AreEqual( 1.0, AudioVoice.PitchRatio( step ) * AudioVoice.PitchRatio( -step ), 1e-5, $"step {step}" );
		Assert.IsFalse( Enumerable.Range( 0, 40 ).Select( AudioVoice.PitchRatio ).Any( ratio => Math.Abs( ratio - Math.Pow( 2, 1 / 96.0 ) ) < 1e-4 ), "2^(1/96) is never reached" );
	}

	[TestMethod]
	public void AVoiceTakesTheRoundedFrequencyOfItsStep()
	{
		var voice = mixer.Play( new Mp2Audio( 22050, 1, new short[22050], 1, 0 ), AudioChannel.Effects );
		Assert.AreEqual( 22050, voice.Frequency );
		voice.SetPitch( 5 );
		Assert.AreEqual( (int)Math.Round( 22050 * 1.044274 ), voice.Frequency );
		Assert.AreEqual( 5, voice.PitchStep );
		voice.SetPitch( -3 );
		Assert.AreEqual( (int)Math.Round( 22050 * 0.971532 ), voice.Frequency );
		voice.SetPitch( 0 );
		Assert.AreEqual( 22050, voice.Frequency );
	}

	[TestMethod]
	public void ARangeOfZeroToSixDrawsExactlyTheStepsZeroToFive()
	{
		var (_, _) = Category( 0, Sound( 0, 6 ) );
		var sound = Sound( 0, 6 );
		var seen = Enumerable.Range( 0, 3000 ).Select( _ => system.Pitch( sound ) ).ToHashSet();
		CollectionAssert.AreEquivalent( new[] { 0, 1, 2, 3, 4, 5 }, seen.ToArray(), "the range maximum (6) is never drawn, 0 is" );
		Assert.IsFalse( seen.Select( AudioVoice.PitchRatio ).Any( ratio => Math.Abs( ratio - 1.007246f ) < 1e-5 || Math.Abs( ratio - 1.051841f ) < 1e-5 ) );
	}

	[TestMethod]
	public void ASignedRangeIsPutInOrderAndAnEmptyRangeDrawsNothing()
	{
		var swapped = Sound( 4, -4 );
		var seen = Enumerable.Range( 0, 2000 ).Select( _ => system.Pitch( swapped ) ).ToHashSet();
		Assert.AreEqual( -4, seen.Min() );
		Assert.AreEqual( 3, seen.Max() );
		var seed = system.Seed;
		Assert.AreEqual( 0, system.Pitch( Sound( 0, 0 ) ) );
		Assert.AreEqual( -16, system.Pitch( Sound( -16, -16 ) ) );
		Assert.AreEqual( seed, system.Seed, "an empty range draws nothing (0x6bc170)" );
	}

	[TestMethod]
	public void ThePitchIsOneDrawOnTheSoundChooserAfterTheVolume()
	{
		var (category, _) = Category( 0, Sound( 0, 6 ) );
		var seed = system.Seed;
		var voice = system.Play( category, 5, AudioChannel.Effects )!;
		var mirror = new SoundEventSystem( new AudioMixer( null ), seed );
		var expected = mirror.Pitch( Sound( 0, 6 ) );
		Assert.AreEqual( expected, voice.PitchStep );
		Assert.AreEqual( mirror.Seed, system.Seed, "one chooser step: the sound, the sample and the volume draw nothing here, the pitch draws once" );
	}

	[TestMethod]
	public void RangeExitStopsSamplesButRetainsTheLoopOwner()
	{
		// [BIN:TPW-EXE:0x6bca10] out of range without the any-distance flag 0x200: the sample is stopped (0x6bca70, message 3)
		var far = new Vector3( 1000, 1000, 0 );
		var (plain, _) = Category( 0, Sound( 0, 0 ) );
		var stopped = system.PlayAt( plain, 5, AudioChannel.Effects, far, 10 )!;
		var (anyDistance, _) = Category( SoundEventSystem.AnyDistanceFlag, Sound( 0, 0 ) );
		var heard = system.PlayAt( anyDistance, 5, AudioChannel.Effects, far, 10 )!;
		var (looping, _) = Category( 0x8, Sound( 0, 0 ) );
		var loop = system.PlayAt( looping, 5, AudioChannel.Effects, far, 10 )!;
		var (near, _) = Category( 0, Sound( 0, 0 ) );
		var close = system.PlayAt( near, 5, AudioChannel.Effects, Vector3.Zero, 10 )!;
		system.Pump();
		Assert.IsTrue( stopped.IsFinished, "out of range: stopped" );
		Assert.IsFalse( heard.IsFinished, "flag 0x200: heard at any distance" );
		Assert.IsTrue( loop.Loops );
		Assert.IsFalse( loop.IsFinished, "a loop retains its logical owner for re-admission" );
		Assert.IsFalse( loop.HasOutput, "its old sample output is released, not advanced silently" );
		Assert.IsFalse( close.IsFinished, "in range: plays on" );
	}

	[TestMethod]
	public void TheVolumeIsDrawnBeforeThePitch()
	{
		var sound = new SoundEntry( 40, 100, 0, 6, 0, 0, 1, 100, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, Array.Empty<SoundLink>(), Array.Empty<byte>() );
		var (category, _) = Category( 0, sound );
		var orderShows = false;
		for ( var playback = 0; playback < 20; playback++ )
		{
			var seed = system.Seed;
			var voice = system.Play( category, 5, AudioChannel.Effects )!;
			var mirror = new SoundEventSystem( new AudioMixer( null ), seed );
			mirror.Volume( sound );
			Assert.AreEqual( mirror.Pitch( sound ), voice.PitchStep, "the pitch is the second draw (0x6bc410: volume, then pitch)" );
			Assert.AreEqual( mirror.Seed, system.Seed );
			orderShows |= new SoundEventSystem( new AudioMixer( null ), seed ).Pitch( sound ) != voice.PitchStep;
		}
		Assert.IsTrue( orderShows, "drawn first, the pitch would differ for some playback" );
	}

	[TestMethod]
	public void EveryPlaybackPathAppliesAPitch()
	{
		// a positioned one-shot, a placed sound, a sentence segment and a held-chain child
		var (category, _) = Category( 0, Sound( 3, 3 ) );
		Assert.AreEqual( 3, system.PlayAt( category, 5, AudioChannel.Effects, new Vector3( 1, 0, 1 ) )!.PitchStep );
		var placed = system.PlayAtWhenDecoded( category, 5, AudioChannel.Effects, new Vector3( 1, 0, 1 ) )!;
		system.Pump();
		Assert.AreEqual( 3, placed.Voice!.PitchStep );
		var (sentenceCategory, _) = Category( 0x6, Sound( -2, -2 ) );
		var sentence = system.StartSentence( sentenceCategory, 5, AudioChannel.Effects )!;
		for ( var attempt = 0; attempt < 400 && !sentence.IsPlaying; attempt++ )
		{
			system.Pump();
			System.Threading.Thread.Sleep( 5 );
		}
		Assert.IsTrue( sentence.IsPlaying );
		Assert.AreEqual( -2, mixer.Voices.Last( voice => voice.PitchStep != 0 ).PitchStep );
		sentence.Stop();
		var (chainCategory, _) = Category( 0x404, Sound( 0, 6 ) );
		var chain = system.StartChain( chainCategory, 5, AudioChannel.Effects, 35, null )!;
		var steps = new HashSet<int>();
		for ( var child = 0; child < 200; child++ )
		{
			typeof( SoundChain ).GetField( "nextChildAt", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( chain, 0L );
			chain.Pump();
			steps.Add( mixer.Voices[^1].PitchStep );
			Assert.IsTrue( mixer.Voices[^1].PitchStep is >= 0 and <= 5 );
		}
		Assert.IsTrue( steps.Count >= 5, "children are detuned independently" );
	}

	[TestMethod]
	public void AShiftedVoiceEndsSoonerByItsRatio()
	{
		var audio = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, 22050 ).ToArray(), 1, 0 );
		var plain = mixer.Play( audio, AudioChannel.Effects );
		var shifted = mixer.Play( audio, AudioChannel.Effects );
		shifted.SetPitch( 5 );
		mixer.Mix( 21200 );
		Assert.IsFalse( plain.IsFinished, "22,050 frames at ratio 1 outlast 21,200" );
		Assert.IsTrue( shifted.IsFinished, "22,050 frames at ratio 1.044 last 21,117 frames, fewer than the 21,200 mixed" );
	}
}
