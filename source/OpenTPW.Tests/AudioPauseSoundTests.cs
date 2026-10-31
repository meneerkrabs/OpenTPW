using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// UI-096, the sound side of the game pause: the mixer pause, the paused advisor speech handle, the raised listener and the words the pause object keeps between a pause and its resume
/// (tp.exe 0x4092A0 / 0x409300 / 0x409350, 0x598960 / 0x598990, 0x51BCF0 / 0x51BD30, 0x51C060 / 0x51C0A0, 0x51C1C0 / 0x51C1D0). Synthetic PCM, no device, no sound.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AudioPauseSoundTests
{
	private AudioMixer? oldCurrent;
	private GameOptions oldOptions = null!;

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		oldCurrent = AudioMixer.Current;
		oldOptions = GameOptions.Current;
		GameOptions.Current = new GameOptions { Advisor = true };
		GamePauseSound.Reset();
	}

	[TestCleanup]
	public void Cleanup()
	{
		AudioMixer.Current = oldCurrent;
		GameOptions.Current = oldOptions;
		GamePauseSound.Reset();
	}

	private static Mp2Audio Ramp( int frames, int start = 1000 ) =>
		new( AudioMixer.OutputRate, 1, Enumerable.Range( 0, frames ).Select( index => (short)(start + index * 100) ).ToArray(), 1, 0 );

	private static SpeechAudioPlayer Speech( AudioMixer mixer, int frames = 200 )
	{
		var player = new SpeechAudioPlayer( Ramp( frames ), mixer );
		player.Start();
		return player;
	}

	[TestMethod]
	public void APausedMixerMixesSilenceAndEveryVoiceStaysWhereItIs()
	{
		// [BIN:TPW-EXE:0x51bcf0 mixer pause] [BIN:QMIXER-DLL:0x18003490 pause all]
		using var mixer = new AudioMixer( null );
		var voice = mixer.Play( Ramp( 100 ), AudioChannel.Effects );
		var first = mixer.Mix( 3 );
		Assert.AreEqual( (short)1000, first[0] );
		var startFrame = voice.StartOutputFrame;
		mixer.Pause();
		Assert.IsTrue( mixer.Paused );
		CollectionAssert.AreEqual( new short[10], mixer.Mix( 5 ) );
		Assert.AreEqual( startFrame + 5, voice.StartOutputFrame, "the heard time of the voice does not grow while the mixer is paused" );
		mixer.Resume();
		Assert.IsFalse( mixer.Paused );
		var next = mixer.Mix( 2 );
		Assert.AreEqual( (short)1300, next[0], "the sample goes on at its fourth frame" );
	}

	[TestMethod]
	public void AVoicePausedOnItsOwnIsLeftOutOfTheMixAndTheMixerResumeRestartsIt()
	{
		// [BIN:TPW-EXE:0x6b8a10 pause handle] [BIN:QMIXER-DLL:0x18003710 restart all]
		using var mixer = new AudioMixer( null );
		var quiet = mixer.Play( Ramp( 100, 5000 ), AudioChannel.Effects );
		mixer.Play( Ramp( 100, 100 ), AudioChannel.Effects );
		mixer.Mix( 1 );
		quiet.Paused = true;
		var mixed = mixer.Mix( 2 );
		Assert.AreEqual( (short)200, mixed[0], "only the other voice (frame 1 of 100 + 100 i)" );
		quiet.Paused = true;
		mixer.Resume();
		Assert.IsFalse( quiet.Paused, "the restart reaches every channel of the session" );
		Assert.AreEqual( (short)(5100 + 400), mixer.Mix( 1 )[0], "the paused voice continues at its second frame, beside the other at its fourth" );
	}

	[TestMethod]
	public void TheGamePauseStopsTheAdvisorSpeechAndRaisesTheListenerAndTheResumeUndoesBoth()
	{
		// [BIN:TPW-EXE:0x4092a0 game pause] [BIN:TPW-EXE:0x598960 advisor pause] [BIN:TPW-EXE:0x51c1d0 paused listener]
		using var mixer = new AudioMixer( null );
		AudioMixer.Current = mixer;
		using var speech = Speech( mixer );
		Assert.AreSame( speech, SpeechAudioPlayer.AdvisorHandle );
		var place = new Vector3( 12, 3, -40 );
		Assert.AreEqual( place, GamePauseSound.ListenerPlace( place, true ) );
		GamePauseSound.Pause( byDeactivation: false );
		Assert.IsTrue( mixer.Voices.Single().Paused, "the advisor's handle is paused" );
		Assert.IsFalse( mixer.Paused, "the mixer goes on: the other sounds play" );
		Assert.IsTrue( GamePauseSound.ListenerRaised );
		Assert.IsTrue( GamePauseSound.AdvisorSpeechPaused );
		Assert.AreEqual( new Vector3( 12, 10000, -40 ), GamePauseSound.ListenerPlace( place, true ), "the height is 10000, the other two floats stay" );
		Assert.AreEqual( place, GamePauseSound.ListenerPlace( place, false ), "no sound system, no listener call" );
		GamePauseSound.Resume();
		Assert.IsFalse( mixer.Voices.Single().Paused );
		Assert.IsFalse( GamePauseSound.ListenerRaised );
		Assert.IsFalse( GamePauseSound.AdvisorSpeechPaused );
		Assert.AreEqual( place, GamePauseSound.ListenerPlace( place, true ) );
	}

	[TestMethod]
	public void TheAdvisorPauseNeedsTheAdvisorOptionAndAHandle()
	{
		// [BIN:TPW-EXE:0x598960 advisor pause] the option byte 0x78D90C, the response flag 0xF7966C and the handle 0xF79680 all gate it; the paused word is set whatever they say
		using var mixer = new AudioMixer( null );
		AudioMixer.Current = mixer;
		GamePauseSound.Pause( byDeactivation: false );
		Assert.IsTrue( GamePauseSound.ListenerRaised, "no handle: only the word" );
		Assert.IsFalse( GamePauseSound.AdvisorSpeechPaused );
		GamePauseSound.Resume();
		GameOptions.Current = new GameOptions { Advisor = false };
		using var speech = Speech( mixer );
		GamePauseSound.Pause( byDeactivation: false );
		Assert.IsFalse( mixer.Voices.Single().Paused, "the advisor option is off" );
		Assert.IsFalse( GamePauseSound.AdvisorSpeechPaused );
		Assert.IsTrue( GamePauseSound.ListenerRaised );
	}

	[TestMethod]
	public void TheDeactivationPausesTheWholeMixerAndNothingElseAndItsResumeRestartsIt()
	{
		// [BIN:TPW-EXE:0x4092a0 game pause] a non-zero second argument calls 0x51BCF0 and returns before the advisor pause and the paused word
		using var mixer = new AudioMixer( null );
		AudioMixer.Current = mixer;
		using var speech = Speech( mixer );
		GamePauseSound.Pause( byDeactivation: true );
		Assert.IsTrue( mixer.Paused );
		Assert.IsFalse( mixer.Voices.Single().Paused );
		Assert.IsFalse( GamePauseSound.ListenerRaised );
		Assert.IsTrue( GamePauseSound.LastPauseWasDeactivation );
		GamePauseSound.Resume();
		Assert.IsFalse( mixer.Paused );
	}

	[TestMethod]
	public void AResumeFollowsTheLastPauseSoADeactivationOverAGamePauseLeavesTheWordAndThePausedSpeechToTheMixerRestart()
	{
		// [BIN:TPW-EXE:0x409300 game resume] the byte +0x20 of the pause object holds the last pause's second argument: non-zero resumes the mixer only (0x51BD30), zero the advisor and the word
		using var mixer = new AudioMixer( null );
		AudioMixer.Current = mixer;
		using var speech = Speech( mixer );
		GamePauseSound.Pause( byDeactivation: false );
		GamePauseSound.Pause( byDeactivation: true );
		GamePauseSound.Resume();
		Assert.IsFalse( mixer.Paused );
		Assert.IsFalse( mixer.Voices.Single().Paused, "the restart of every channel restarts the paused handle too" );
		Assert.IsTrue( GamePauseSound.ListenerRaised, "the paused word was set by the first pause and the resume of the second does not clear it" );
		Assert.IsTrue( GamePauseSound.AdvisorSpeechPaused, "and the advisor's flag stays up" );
		GamePauseSound.LevelStart();
		Assert.IsFalse( GamePauseSound.ListenerRaised, "the next level start clears the word (0x415254)" );
		Assert.IsTrue( GamePauseSound.LastPauseWasDeactivation, "the resume leaves the byte as it was" );
	}

	[TestMethod]
	public void TheAdvisorSpeechStopClearsThePausedFlagAndTheHandle()
	{
		// [BIN:TPW-EXE:0x5994e0 advisor stop]
		using var mixer = new AudioMixer( null );
		AudioMixer.Current = mixer;
		var speech = Speech( mixer );
		GamePauseSound.Pause( byDeactivation: false );
		Assert.IsTrue( GamePauseSound.AdvisorSpeechPaused );
		speech.Dispose();
		Assert.IsNull( SpeechAudioPlayer.AdvisorHandle );
		Assert.IsFalse( GamePauseSound.AdvisorSpeechPaused );
	}

	[TestMethod]
	public void TheFrontEndPausesTheMixerWhenItLosesTheFocusAndRestartsItWhenItIsBack()
	{
		// [BIN:TPW-EXE:0x46b70a front end focus]
		using var mixer = new AudioMixer( null );
		AudioMixer.Current = mixer;
		GamePauseSound.ObserveFrontEndFocus( focused: false, offscreen: false );
		GamePauseSound.ObserveFrontEndFocus( focused: true, offscreen: false );
		Assert.IsFalse( mixer.Paused, "a start without focus is no deactivation" );
		GamePauseSound.ObserveFrontEndFocus( focused: false, offscreen: false );
		Assert.IsTrue( mixer.Paused );
		Assert.IsFalse( GamePauseSound.ListenerRaised, "the front end touches nothing of the game pause" );
		GamePauseSound.ObserveFrontEndFocus( focused: true, offscreen: false );
		Assert.IsFalse( mixer.Paused );
		GamePauseSound.ObserveFrontEndFocus( focused: false, offscreen: true );
		GamePauseSound.ObserveFrontEndFocus( focused: true, offscreen: true );
		GamePauseSound.ObserveFrontEndFocus( focused: false, offscreen: true );
		Assert.IsFalse( mixer.Paused, "offscreen automation is no window" );
	}

	[TestMethod]
	public void AMixerPausedWhileAVoiceStartsPlaysItFromItsFirstFrameOnTheResume()
	{
		using var mixer = new AudioMixer( null );
		mixer.Pause();
		var late = mixer.Play( Ramp( 50 ), AudioChannel.Effects );
		CollectionAssert.AreEqual( new short[6], mixer.Mix( 3 ) );
		Assert.AreEqual( -1, late.StartOutputFrame, "a voice that never played has no start frame to move" );
		mixer.Resume();
		Assert.AreEqual( (short)1000, mixer.Mix( 1 )[0] );
	}
}
