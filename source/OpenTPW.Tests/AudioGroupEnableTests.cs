using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// AUDIO-038, the group enable: the sound service's on-flags switch the groups of the sound library on and off (tp.exe 0x6B9900), and the pool tick (0x6BC760) stops the voices of a
/// group that is off and does not run them, so they come back through the sample setup when it is on again. Synthetic data, no device.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AudioGroupEnableTests
{
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;
	private long clock;

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void SetProperty( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, 7, () => 0 ) { PoolClock = () => clock };
		clock = 0;
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		system.StopChains();
		mixer.Dispose();
		GameAudio.ResetSavedSound();
		GameOptions.Current = new GameOptions();
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	private static SoundEntry Sound( uint weight = 1 ) =>
		new( 50, 90, 0, 0, 0, 0, weight, 100, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, Array.Empty<SoundLink>(), Array.Empty<byte>() );

	private SoundCategory Category( ushort flags, uint groupMask )
	{
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		var sound = Sound();
		var soundEvent = new SoundEvent( 1, 100, flags, 0, new[] { sound } ) { GroupMask = groupMask };
		SetProperty( catalog, "Events", new[] { soundEvent } );
		SetProperty( category, "Catalog", catalog );
		system.RememberDecoded( category, sound.Samples[0], new Mp2Audio( AudioMixer.OutputRate, 1,
			Enumerable.Range( 0, 400 ).Select( index => (short)(10000 + index * 10) ).ToArray(), 1, 0 ), false );
		return category;
	}

	[TestMethod]
	public void AGroupIsOnWhenEveryBitOfAMaskIsOn()
	{
		// [BIN:TPW-EXE:0x6bc760 group gate] bit 1 master (never written), 2 music, 4 speech, 8 effects
		Assert.IsTrue( mixer.GroupsEnabled( 0 ) );
		Assert.IsTrue( mixer.GroupsEnabled( 0xF ) );
		mixer.SetChannelEnabled( AudioChannel.Music, false );
		Assert.IsFalse( mixer.GroupsEnabled( 2 ) );
		Assert.IsFalse( mixer.GroupsEnabled( 3 ) );
		Assert.IsTrue( mixer.GroupsEnabled( 1 | 4 | 8 ) );
		mixer.SetChannelEnabled( AudioChannel.Speech, false );
		mixer.SetChannelEnabled( AudioChannel.Effects, false );
		Assert.IsTrue( mixer.GroupsEnabled( 1 ), "the master group is always on" );
		Assert.IsFalse( mixer.GroupsEnabled( 1 | 4 ) );
		Assert.IsFalse( mixer.GroupsEnabled( 8 ) );
		Assert.IsTrue( mixer.IsChannelEnabled( AudioChannel.Speech ) is false );
	}

	[TestMethod]
	public void ALoopOfASwitchedOffGroupIsStoppedAndRestartsFromItsFirstFrameWithFreshDraws()
	{
		// [BIN:TPW-EXE:0x6bc760 group gate] the sample stops and the voice is not ticked; back on, the tick starts the sample again from its beginning (0x6bc410), drawing the volume anew
		var category = Category( flags: 0x8, groupMask: 8 );
		var voice = system.Play( category, 1, AudioChannel.Effects )!;
		system.Pump();
		var heard = mixer.Mix( 4 );
		Assert.AreEqual( 4, heard.Length / 2 );
		Assert.IsTrue( heard[0] != 0 );
		Assert.IsTrue( voice.HasOutput );
		mixer.SetChannelEnabled( AudioChannel.Effects, false );
		system.Pump();
		Assert.IsFalse( voice.HasOutput, "the group is off: the sample is stopped" );
		Assert.IsFalse( voice.IsFinished, "the logical voice stays for the way back" );
		CollectionAssert.AreEqual( new short[6], mixer.Mix( 3 ) );
		system.Pump();
		Assert.IsFalse( voice.HasOutput, "and it is not started while the group is off" );
		var seed = system.Seed;
		mixer.SetChannelEnabled( AudioChannel.Effects, true );
		system.Pump();
		Assert.IsTrue( voice.HasOutput );
		Assert.AreNotEqual( seed, system.Seed, "the sample setup draws the volume again" );
		var again = mixer.Mix( 2 );
		Assert.IsTrue( again[0] > 0 && again[0] <= 9000 + 1, "the loop begins again at its first frame (10000 x a gain of 0.5 to 0.9)" );
		Assert.IsTrue( again[0] >= 4999 );
	}

	[TestMethod]
	public void AVoiceOfAnotherGroupIsLeftAlone()
	{
		var category = Category( flags: 0x8, groupMask: 2 );
		var voice = system.Play( category, 1, AudioChannel.Music )!;
		system.Pump();
		mixer.SetChannelEnabled( AudioChannel.Effects, false );
		mixer.SetChannelEnabled( AudioChannel.Speech, false );
		system.Pump();
		Assert.IsTrue( voice.HasOutput, "a music voice does not care for the effects and speech groups" );
		mixer.SetChannelEnabled( AudioChannel.Music, false );
		system.Pump();
		Assert.IsFalse( voice.HasOutput );
	}

	[TestMethod]
	public void ASentenceOfASwitchedOffGroupStopsItsSegmentAndReplaysItWhenTheGroupIsBackOn()
	{
		// [BIN:TPW-EXE:0x6bc760 group gate] [BIN:TPW-EXE:0x6BDF50,0x6BC410,0x6B9AB0 stopped sentence resumes]
		var category = Category( flags: 0x6, groupMask: 2 );
		var sentence = system.StartSentence( category, 1, AudioChannel.Music )!;
		system.Pump();
		Assert.IsTrue( sentence.IsPlaying );
		mixer.Mix( 5 );
		mixer.SetChannelEnabled( AudioChannel.Music, false );
		system.Pump();
		Assert.IsFalse( sentence.IsPlaying );
		system.Pump();
		Assert.IsFalse( sentence.IsPlaying, "it is not run while the group is off" );
		mixer.SetChannelEnabled( AudioChannel.Music, true );
		system.Pump();
		Assert.IsTrue( sentence.IsPlaying, "the segment is started again" );
		var restarted = mixer.Mix( 2 );
		Assert.IsTrue( restarted[0] >= 4999 && restarted[0] <= 9001, "from its first frame" );
	}

	[TestMethod]
	public void AChainOfASwitchedOffGroupMakesNoChildAndTheChildrenPlayOut()
	{
		// [BIN:TPW-EXE:0x6bc760 group gate] the root is not ticked; its children are not in the pool
		var category = Category( flags: 0x404, groupMask: 8 );
		var chain = system.StartChain( category, 1, AudioChannel.Effects, 50, null )!;
		system.Pump();
		var children = mixer.Voices.Count;
		Assert.AreEqual( 1, children, "the first child is made in the first pooled tick" );
		mixer.SetChannelEnabled( AudioChannel.Effects, false );
		typeof( SoundChain ).GetField( "nextChildAt", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( chain, 0L );
		system.Pump();
		Assert.AreEqual( 1, mixer.Voices.Count, "no second child while the group is off" );
		Assert.IsTrue( mixer.Voices[0].HasOutput, "the child already playing plays out" );
		mixer.SetChannelEnabled( AudioChannel.Effects, true );
		system.Pump();
		system.Pump();
		Assert.AreEqual( 2, mixer.Voices.Count, "back on, the chain goes on (a sound without a link starts the walk over at the next tick, AUDIO-034)" );
	}

	[TestMethod]
	public void TheOnFlagsComeFromTheApplierAndASavedBlockNotFromATogglesVolume()
	{
		// [BIN:TPW-EXE:0x423dd0 options applier] [BIN:TPW-EXE:0x4A2E90 volume rows] a toggle sets the volume (0 while it is off), the on-flag follows the applier (the options screen's OK, leaving a park) and a save's block
		GameAudio.ResetSavedSound();
		var options = new GameOptions { SoundEffectsOn = false, MusicOn = true, SpeechOn = false };
		GameOptions.Current = options;
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0f, mixer.GetChannelGain( AudioChannel.Effects ), "the toggle's volume" );
		Assert.IsTrue( mixer.IsChannelEnabled( AudioChannel.Effects ), "the on-flag has not been applied yet" );
		GameAudio.EndSavedVolumes();
		GameAudio.ApplyOptions( mixer, options );
		Assert.IsFalse( mixer.IsChannelEnabled( AudioChannel.Effects ), "the applier (OK) sets it" );
		Assert.IsTrue( mixer.IsChannelEnabled( AudioChannel.Music ) );
		Assert.IsFalse( mixer.IsChannelEnabled( AudioChannel.Speech ) );
		options.SoundEffectsOn = true;
		GameAudio.ApplyOptions( mixer, options );
		Assert.IsFalse( mixer.IsChannelEnabled( AudioChannel.Effects ), "a toggle alone leaves the flag as it was" );
		GameAudio.EndSavedVolumes();
		GameAudio.ApplyOptions( mixer, options );
		Assert.IsTrue( mixer.IsChannelEnabled( AudioChannel.Effects ) );
	}
}
