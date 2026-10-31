using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>AUDIO-038: synthetic PCM and explicit host ticks, with no original data, device or wall-clock waits.</summary>
[TestClass]
[DoNotParallelize]
public class AudioLoopRangeTests
{
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;
	private long clock;
	private double fadeClock;
	private static readonly Vector3 Far = new( 1000, 0, 0 );
	private static void Listener( Vector3 position ) => AudioListener.Update( position, new Vector3( 0, 0, 1 ) );
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
	private static object? Output( AudioVoice voice ) => typeof( AudioVoice ).GetField( "output", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( voice );

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, 7, () => fadeClock ) { PoolClock = () => clock };
		Listener( Vector3.Zero );
	}

	[TestCleanup]
	public void Cleanup()
	{
		system.StopChains();
		mixer.Dispose();
		Listener( Vector3.Zero );
	}

	private static SoundEntry Sound() => new( 100, 100, 0, 0, 0, 0, 1, 10,
		new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, Array.Empty<SoundLink>(), Array.Empty<byte>() );

	private SoundCategory Category( ushort flags = 8, uint priority = 100, SoundEntry? sound = null, uint groups = 0, bool ready = true )
	{
		sound ??= Sound();
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		Set( catalog, "Events", new[] { new SoundEvent( 1, priority, flags, 20, new[] { sound } ) { GroupMask = groups } } );
		Set( category, "Catalog", catalog );
		foreach ( var sample in sound.Samples )
			if ( ready ) system.RememberDecoded( category, sample, new Mp2Audio( AudioMixer.OutputRate, 1,
				Enumerable.Range( 0, 128 ).Select( index => (short)(sample.SampleNumber * 1000 + index * 10) ).ToArray(), 1, 0 ), false );
		return category;
	}

	private AudioVoice Play( SoundCategory? category = null, AudioChannel channel = AudioChannel.Effects ) =>
		system.PlayAt( category ?? Category(), 1, channel, Vector3.Zero, 10 )!;

	[TestMethod]
	public void RangeReleaseRetainsOwnerAndReturnCreatesExactlyOneFreshOutput()
	{
		var voice = Play();
		var ownerSerial = voice.Serial;
		var firstOutput = Output( voice );
		var completions = 0;
		voice.Completed += _ => completions++;
		CollectionAssert.AreEqual( AudioLevels.Scale( 100, 1000, 1000, 1010, 1010, 1020, 1020 ), mixer.Mix( 3 ) );
		Listener( Far ); system.Pump();
		Assert.IsFalse( voice.HasOutput ); Assert.IsNull( Output( voice ) ); Assert.IsFalse( voice.IsFinished );
		var seed = system.Seed;
		for ( var tick = 0; tick < 4; tick++ ) { clock += 100000; system.Pump(); }
		CollectionAssert.AreEqual( new short[10], mixer.Mix( 5 ) );
		Assert.AreEqual( seed, system.Seed ); Assert.AreEqual( 0, completions );
		Assert.AreSame( voice, mixer.Voices.Single(), "logical ownership survives multiple absent ticks" );
		Listener( Vector3.Zero ); system.Pump();
		Assert.IsTrue( voice.HasOutput ); Assert.AreNotSame( firstOutput, Output( voice ) );
		Assert.AreEqual( ownerSerial, voice.Serial ); Assert.AreEqual( -1L, voice.StartOutputFrame );
		CollectionAssert.AreEqual( AudioLevels.Scale( 100, 1000, 1000 ), mixer.Mix( 1 ), "fresh source, not the old or silently advanced cursor" );
		Assert.AreEqual( 8L, voice.StartOutputFrame );
		var resumed = Output( voice );
		system.Pump(); voice.RaiseCompleted(); system.Pump();
		Assert.AreSame( resumed, Output( voice ) ); Assert.AreEqual( 0, completions, "no stale completion for an active logical owner" );
		Assert.AreEqual( 1, mixer.Voices.Count );
	}

	[TestMethod]
	public void ReturnRedrawsVolumeThenPitchThenDelayWithoutSelectingAnotherSample()
	{
		var sound = Sound() with { MinimumVolume = 50, MaximumVolume = 90, MinimumPitch = -3, MaximumPitch = 6,
			MinimumDelay = 10, MaximumDelay = 30,
			Samples = new[] { new SoundSample( 1, 32767, 0, 1, 0 ), new SoundSample( 2, 65535, 0, 1, 0 ) } };
		var voice = Play( Category( sound: sound ) );
		var source = typeof( AudioVoice ).GetField( "samples", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( voice );
		Listener( Far ); system.Pump();
		using var mirrorMixer = new AudioMixer( null );
		var mirror = new SoundEventSystem( mirrorMixer, system.Seed );
		var volume = 50 + mirror.DrawBelow( 40 );
		var pitch = -3 + (int)mirror.DrawBelow( 9 );
		_ = mirror.DrawBelow( 20 );
		Listener( Vector3.Zero ); system.Pump();
		Assert.AreEqual( volume / 100f, voice.Gain ); Assert.AreEqual( pitch, voice.PitchStep );
		Assert.AreEqual( mirror.Seed, system.Seed, "exactly volume, pitch and delay; no sample-choice draw" );
		Assert.AreSame( source, typeof( AudioVoice ).GetField( "samples", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( voice ) );
	}

	[TestMethod]
	public void PreservedFadeVolumeStillEvaluatesPitchThenDelayWithoutAnAbsoluteVolumeDraw()
	{
		var sound = Sound() with { MinimumVolume = 20, MaximumVolume = 100, MinimumPitch = 0, MaximumPitch = 20,
			MinimumDelay = 10, MaximumDelay = 30 };
		var voice = Play( Category( sound: sound ) );
		var lifetime = new HeldSoundLifetime( mixer, SoundEventPlayer.OneShot, 0 );
		lifetime.Admit( voice, 60 ); lifetime.PostFade();
		using var mirrorMixer = new AudioMixer( null );
		var mirror = new SoundEventSystem( mirrorMixer, system.Seed );
		var pitch = (int)mirror.DrawBelow( 20 ); _ = mirror.DrawBelow( 20 );
		var prepared = new PlacedSoundParameters( system, sound, null ).Restart( lifetime );
		Assert.AreEqual( (60, pitch), prepared ); Assert.AreEqual( mirror.Seed, system.Seed );
	}

	[DataTestMethod]
	[DataRow( false )] [DataRow( true )]
	public void PendingPlacedDecodeRegistersOneRetainedLoopUnlessStopped( bool stopBeforeReady )
	{
		var category = Category( ready: false );
		var ready = new TaskCompletionSource<Mp2Audio?>();
		var flights = (Dictionary<(SoundCategory, int, uint), Lazy<Task<Mp2Audio?>>>)typeof( SoundEventSystem )
			.GetField( "decoding", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!;
		flights.Add( (category, 1, 1), new Lazy<Task<Mp2Audio?>>( () => ready.Task ) );
		var placed = system.PlayAtWhenDecoded( category, 1, AudioChannel.Effects, Vector3.Zero, 10 )!;
		Assert.IsNull( placed.Voice );
		if ( stopBeforeReady ) placed.Stop();
		Listener( Far ); ready.SetResult( new Mp2Audio( AudioMixer.OutputRate, 1, new short[] { 1000, 2000, 3000 }, 1, 0 ) );
		system.Pump(); Listener( Vector3.Zero ); system.Pump();
		if ( stopBeforeReady ) { Assert.IsNull( placed.Voice ); Assert.AreEqual( 0, mixer.Voices.Count ); }
		else
		{
			Assert.IsTrue( placed.Voice!.HasOutput ); Assert.AreEqual( 1, mixer.Voices.Count );
			CollectionAssert.AreEqual( AudioLevels.Scale( 100, 1000, 1000 ), mixer.Mix( 1 ) );
		}
	}

	[TestMethod]
	public void AnyDistanceFlatAndZeroRangeLoopsKeepTheirCurrentOutput()
	{
		var any = Play( Category( 0x208 ) );
		var flat = system.Play( Category(), 1, AudioChannel.Effects )!;
		var zero = system.PlayAt( Category(), 1, AudioChannel.Effects, Vector3.Zero, 0 )!;
		var originals = new[] { Output( any ), Output( flat ), Output( zero ) };
		Listener( Far ); system.Pump();
		CollectionAssert.AreEqual( originals, new[] { Output( any ), Output( flat ), Output( zero ) } );
		Assert.IsTrue( any.HasOutput && flat.HasOutput && zero.HasOutput );
	}

	[DataTestMethod]
	[DataRow( false )] [DataRow( true )]
	public void ExplicitStopRemainsTerminalBeforeOrDuringRangeRelease( bool released )
	{
		var voice = Play();
		if ( released ) { Listener( Far ); system.Pump(); }
		voice.Stop(); Listener( Vector3.Zero ); system.Pump();
		voice.RaiseCompleted(); system.Pump();
		Assert.IsTrue( voice.IsFinished ); Assert.IsFalse( voice.HasOutput );
		CollectionAssert.AreEqual( new short[2], mixer.Mix( 1 ) );
		Assert.AreEqual( 0, mixer.Voices.Count );
	}

	[TestMethod]
	public void ChannelStopAlsoEndsReleasedOwnersWithoutStoppingOtherChannels()
	{
		var effects = Play(); var music = Play( channel: AudioChannel.Music );
		Listener( Far ); system.Pump(); mixer.StopAll( AudioChannel.Effects );
		Listener( Vector3.Zero ); system.Pump();
		Assert.IsTrue( effects.IsFinished ); Assert.IsFalse( effects.HasOutput );
		Assert.IsFalse( music.IsFinished ); Assert.IsTrue( music.HasOutput );
	}

	[TestMethod]
	public void ReturningOwnerWaitsForItsOriginalPoolPlaceButEvictionDoesNotStopPlayingOutput()
	{
		var category = Category();
		var oldest = Play( category );
		var later = Enumerable.Range( 0, SoundEventSystem.VoicePool ).Select( _ => Play( category ) ).ToArray();
		system.Pump(); Assert.IsTrue( oldest.HasOutput, "pool eviction is not a range stop" );
		var serial = oldest.Serial;
		Listener( Far ); system.Pump(); Listener( Vector3.Zero ); system.Pump();
		Assert.IsFalse( oldest.HasOutput, "thirteen equal owners: original newest twelve are admitted" );
		Assert.IsTrue( later.All( voice => voice.HasOutput ) );
		Assert.AreEqual( SoundEventSystem.VoicePool + 1, mixer.Voices.Count );
		later[^1].Stop(); system.Pump();
		Assert.IsTrue( oldest.HasOutput ); Assert.AreEqual( serial, oldest.Serial );
	}

	[DataTestMethod]
	[DataRow( 100, 100 )] [DataRow( 200, 100 )] [DataRow( 100, 200 )]
	public void SimultaneousReturnDrawsInRankThenNewestOwnerOrder( int firstPriority, int secondPriority )
	{
		var sound = Sound() with { MinimumVolume = 20, MaximumVolume = 100, MinimumPitch = 0, MaximumPitch = 20,
			MinimumDelay = 10, MaximumDelay = 30 };
		var first = Play( Category( priority: (uint)firstPriority, sound: sound ) );
		var second = Play( Category( priority: (uint)secondPriority, sound: sound ) );
		Listener( Far ); system.Pump();
		using var mirrorMixer = new AudioMixer( null );
		var mirror = new SoundEventSystem( mirrorMixer, system.Seed );
		var ordered = firstPriority > secondPriority ? new[] { first, second } : new[] { second, first };
		var expected = ordered.Select( voice =>
		{
			var volume = 20 + mirror.DrawBelow( 80 ); var pitch = (int)mirror.DrawBelow( 20 );
			_ = mirror.DrawBelow( 20 ); return (Voice: voice, Volume: volume / 100f, Pitch: pitch);
		} ).ToArray();
		Listener( Vector3.Zero ); system.Pump();
		foreach ( var entry in expected ) { Assert.AreEqual( entry.Volume, entry.Voice.Gain ); Assert.AreEqual( entry.Pitch, entry.Voice.PitchStep ); }
		Assert.AreEqual( mirror.Seed, system.Seed );
	}

	[TestMethod]
	public void SuspendedHighRankOwnerDoesNotConsumeAReturningLoopsPoolSlot()
	{
		var eligible = Play( Category( priority: 50 ) );
		var held = system.PlayHeld( Category( priority: 200 ), 1, AudioChannel.Effects, Vector3.Zero )!;
		var others = Enumerable.Range( 0, SoundEventSystem.VoicePool - 1 ).Select( _ => Play( Category( priority: 100 ) ) ).ToArray();
		Listener( Far ); system.Pump(); held.Lifetime.Suspended = true;
		Listener( Vector3.Zero ); system.Pump();
		Assert.IsFalse( held.Placed!.Voice!.HasOutput );
		Assert.IsTrue( eligible.HasOutput ); Assert.IsTrue( others.All( voice => voice.HasOutput ) );
	}

	[TestMethod]
	public void ReturningRankZeroOwnerWaitsUntilItsRankQualifies()
	{
		var voice = Play( Category( priority: 0 ) );
		var beyondRankScale = new Vector3( 3000, 0, 0 );
		voice.Position = beyondRankScale; system.Pump();
		voice.Range = 10000; system.Pump();
		Assert.AreEqual( 0u, SoundEventSystem.PoolRank( 0, beyondRankScale ) );
		Assert.IsFalse( voice.HasOutput, "audibility alone does not admit a zero rank" );
		voice.Position = Vector3.Zero; system.Pump(); Assert.IsTrue( voice.HasOutput );
	}

	[TestMethod]
	public void PlacedLoopRetainsItsHandleAndUpdatedParameterTableAcrossRelease()
	{
		var raw = new byte[42]; raw[22] = 20; raw[24] = 3;
		var sound = Sound() with { MinimumVolume = 20, MaximumVolume = 100, MinimumPitch = 0, MaximumPitch = 20, Raw = raw };
		var placed = system.PlayAtWhenDecoded( Category( sound: sound ), 1, AudioChannel.Effects, Vector3.Zero, 10 )!;
		var voice = placed.Voice!;
		Listener( Far ); system.Pump(); placed.SetParameter( 20, 50 );
		Listener( Vector3.Zero ); system.Pump();
		Assert.AreSame( voice, placed.Voice ); Assert.IsFalse( placed.IsFinished );
		Assert.AreEqual( 0.6f, voice.Gain ); Assert.AreEqual( 10, voice.PitchStep );
		placed.Stop(); system.Pump(); Assert.IsFalse( voice.HasOutput ); Assert.IsTrue( placed.IsFinished );
	}

	[TestMethod]
	public void HeldLoopCannotRestartAfterLostReplyMakesItsFadeTerminal()
	{
		var held = system.PlayHeld( Category(), 1, AudioChannel.Effects, Vector3.Zero )!;
		var voice = held.Placed!.Voice!;
		held.Fade(); Listener( Far ); system.Pump();
		Assert.IsFalse( held.Lifetime.ReplyAvailable ); Assert.IsFalse( held.IsFinished );
		fadeClock = system.HeldSounds.NextPoll;
		Listener( Vector3.Zero ); system.Pump();
		Assert.IsTrue( held.IsFinished ); Assert.IsFalse( voice.HasOutput );
		Assert.IsTrue( held.Lifetime.Terminal );
	}

	[TestMethod]
	public void HeldSuspensionGatesReturnAndGroupGainStillAppliesWithoutBecomingOwnVolume()
	{
		var held = system.PlayHeld( Category( groups: 8 ), 1, AudioChannel.Effects, Vector3.Zero )!;
		var voice = held.Placed!.Voice!;
		Listener( Far ); system.Pump(); held.Lifetime.Suspended = true;
		Listener( Vector3.Zero ); system.Pump(); Assert.IsFalse( voice.HasOutput );
		mixer.SetChannelGain( AudioChannel.Effects, 0.5f );
		held.Lifetime.Suspended = false; system.Pump();
		Assert.IsTrue( voice.HasOutput ); Assert.AreEqual( 100, held.Lifetime.OwnVolume );
		Assert.AreEqual( 50, held.Lifetime.DeviceVolume ); Assert.AreEqual( 0.5f, voice.Gain );
	}

	[TestMethod]
	public void AReleasedSpeechLoopDoesNotDuckOtherOutputs()
	{
		mixer.DuckingLevel = 0.5f;
		var speech = Play( channel: AudioChannel.Speech );
		Assert.AreEqual( 0.5f, mixer.GetGroupFactor( 3 ) );
		Listener( Far ); system.Pump();
		Assert.IsFalse( speech.HasOutput ); Assert.AreEqual( 1f, mixer.GetGroupFactor( 3 ) );
	}

	[TestMethod]
	public void RangeGatingAChainRootLeavesItsExistingChildOutputUntouched()
	{
		var sound = Sound() with { MinimumDelay = 10000, MaximumDelay = 10000 };
		var chain = system.StartChain( Category( 0x404, sound: sound ), 1, AudioChannel.Effects, 50, Vector3.Zero, 10 )!;
		system.Pump(); var child = mixer.Voices.Single(); var output = Output( child );
		mixer.Mix( 3 ); Listener( Far ); system.Pump(); mixer.Mix( 5 );
		Assert.AreSame( output, Output( child ) ); Assert.IsTrue( child.HasOutput );
		Assert.AreEqual( 1, mixer.Voices.Count, "no new child while the root is range-gated" );
		Listener( Vector3.Zero );
		CollectionAssert.AreEqual( AudioLevels.Scale( 100, 1080, 1080 ), mixer.Mix( 1 ), "already-started child continues its own source cursor" );
		chain.Stop(); Assert.IsTrue( child.IsFinished );
	}
}
