using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// AUDIO-018, the volume chain of an event-system voice: the effective integer volume of its groups (tp.exe 0x6BB860), the driver's table (0x6D2980, 0x782E40), QMixer's
/// message case 2 (0x18007A1C) and the decibel quantisation of the product (0x1800586B, 0x1800CA40).
/// </summary>
[TestClass]
[DoNotParallelize]
public class AudioVolumeTableTests
{
	private static Mp2Audio Constant( short value ) => new( AudioMixer.OutputRate, 1, Enumerable.Repeat( value, 64 ).ToArray(), 1, 0 );

	[TestMethod]
	public void TheTableIs258TimesTheIndexWithTheLastEntryAt32767()
	{
		for ( var index = 0; index <= 126; index++ ) Assert.AreEqual( 258 * index, SoundVolume.Entry( index ) );
		Assert.AreEqual( 32767, SoundVolume.Entry( 127 ) );
		Assert.AreEqual( 0.7874f, SoundVolume.Library( 100 )!.Value, 0.0001f, "a percent of 100 gives 25800 / 32767" );
		Assert.AreEqual( 1f, SoundVolume.Library( 127 )!.Value );
	}

	[TestMethod]
	public void AnIndexOutsideZeroTo127SendsNothing()
	{
		Assert.IsNull( SoundVolume.Library( -1 ) );
		Assert.IsNull( SoundVolume.Library( 128 ) );
		var voice = new AudioVoice( Constant( 1000 ), AudioChannel.Effects, 1f, AudioMixer.OutputRate );
		voice.SetLibraryIndex( 50 );
		var before = voice.LibraryVolume;
		voice.SetLibraryIndex( 128 );
		Assert.AreEqual( before, voice.LibraryVolume, "no SetVolume: the voice keeps its last volume" );
		voice.SetLibraryIndex( -3 );
		Assert.AreEqual( before, voice.LibraryVolume );
	}

	[TestMethod]
	public void TheIndexMultipliesTheGroupsOfTheMaskAndRoundsToEven()
	{
		var groups = new[] { 0.5f, 0.25f, 0.1f, 0.8f }; // master, music, speech, effects
		Assert.AreEqual( 100, SoundVolume.Index( 100, 0, groups ), "no group: the own volume" );
		Assert.AreEqual( 50, SoundVolume.Index( 100, 1, groups ) );
		Assert.AreEqual( 40, SoundVolume.Index( 100, 1 | 8, groups ) );
		Assert.AreEqual( 10, SoundVolume.Index( 100, 4, groups ) );
		Assert.AreEqual( 2, SoundVolume.Index( 5, 1, groups ), "2.5 rounds to the even 2" );
		Assert.AreEqual( 4, SoundVolume.Index( 7, 1, groups ), "3.5 rounds to the even 4" );
		Assert.AreEqual( 6, SoundVolume.Index( 100, 1 | 2 | 4 | 8, new[] { 1f, 1f, 1f, 0.06f } ) );
	}

	[TestMethod]
	public void AFlatEventVoiceIsQuantisedFromItsLibraryVolumeAlone()
	{
		using var mixer = new AudioMixer( null );
		var voice = mixer.Play( Constant( 1000 ), AudioChannel.Effects, 1f ).ForEvent( 0 );
		var pcm = mixer.Mix( 1 );
		Assert.AreEqual( (short)(1000 * AudioLevels.Of( 100 )), pcm[0] );
		Assert.IsFalse( voice.IsFinished );
	}

	[TestMethod]
	public void AGroupVolumeEntersTheIndexAndNoChannelGainIsAppliedAgain()
	{
		using var mixer = new AudioMixer( null );
		mixer.Play( Constant( 1000 ), AudioChannel.Effects, 1f ).ForEvent( 1 | 8 );
		mixer.MasterGain = 0.5f;
		mixer.SetChannelGain( AudioChannel.Effects, 0.5f );
		Assert.AreEqual( (short)(1000 * AudioLevels.Of( 25 )), mixer.Mix( 1 )[0], "100 x 0.5 x 0.5 is the index 25, once" );
	}

	[TestMethod]
	public void AGroupChangeMovesTheIndexAtTheNextBlock()
	{
		using var mixer = new AudioMixer( null );
		mixer.Play( Constant( 1000 ), AudioChannel.Effects, 1f ).ForEvent( 8 );
		Assert.AreEqual( (short)(1000 * AudioLevels.Of( 100 )), mixer.Mix( 1 )[0] );
		mixer.SetChannelGain( AudioChannel.Effects, 0.5f );
		Assert.AreEqual( (short)(1000 * AudioLevels.Of( 50 )), mixer.Mix( 1 )[0] );
		mixer.SetChannelGain( AudioChannel.Effects, 0f );
		Assert.AreEqual( 0, mixer.Mix( 1 )[0], "the index 0 is the table entry 0: -10000 hundredths of a decibel" );
	}

	[TestMethod]
	public void AnIndexAbove127KeepsTheLastVolume()
	{
		using var mixer = new AudioMixer( null );
		var voice = mixer.Play( Constant( 1000 ), AudioChannel.Effects, 1f ).ForEvent( 0 );
		var normal = mixer.Mix( 1 )[0];
		voice.Gain = 1.5f; // percent 150: the driver is sent nothing
		Assert.AreEqual( normal, mixer.Mix( 1 )[0] );
		voice.Gain = 1.27f;
		Assert.AreEqual( (short)(1000 * AudioLevels.Of( 127 )), mixer.Mix( 1 )[0], "127 is the table's last entry, the full volume" );
	}

	[TestMethod]
	public void ADistanceGainAndTheLibraryVolumeShareOneDecibelQuantisation()
	{
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		using var mixer = new AudioMixer( null );
		var place = new Vector3( 0, 0, 40 );
		mixer.Play( Constant( 10000 ), AudioChannel.Effects, 1f, place, 100 ).ForEvent( 0 );
		var (distance, _) = AudioListener.Placement( place, 100 );
		var level = AudioListener.DecibelGain( AudioListener.GainHundredthsOfDecibel( SoundVolume.Library( 100 )!.Value * distance ) );
		Assert.AreEqual( (short)(10000 * level), mixer.Mix( 1 )[0] );
	}
}
