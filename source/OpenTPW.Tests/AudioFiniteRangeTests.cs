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
public class AudioFiniteRangeTests
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

	private SoundCategory Category( ushort flags = 0, uint priority = 100, SoundEntry? sound = null, uint groups = 0, bool ready = true, int frames = 128 )
	{
		sound ??= Sound();
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		Set( catalog, "Events", new[] { new SoundEvent( 1, priority, flags, 20, new[] { sound } ) { GroupMask = groups } } );
		Set( category, "Catalog", catalog );
		foreach ( var sample in sound.Samples )
			if ( ready ) system.RememberDecoded( category, sample, new Mp2Audio( AudioMixer.OutputRate, 1,
				Enumerable.Range( 0, frames ).Select( index => (short)(sample.SampleNumber * 1000 + index * 10) ).ToArray(), 1, 0 ), false );
		return category;
	}

	private AudioVoice Play( SoundCategory? category = null, AudioChannel channel = AudioChannel.Effects ) =>
		system.PlayAt( category ?? Category(), 1, channel, Vector3.Zero, 10 )!;

	[TestMethod]
	public void FiniteReturnRecreatesSelectedSourceAndKeepsSerialAcrossOscillation()
	{
		var voice = Play(); var serial = voice.Serial; var first = Output( voice );
		mixer.Mix( 3 );
		for ( var tick = 0; tick < 3; tick++ )
		{
			Listener( Far ); system.Pump();
			Assert.IsFalse( voice.HasOutput ); Assert.IsFalse( voice.IsFinished );
			var seed = system.Seed; system.Pump(); Assert.AreEqual( seed, system.Seed );
			Listener( Vector3.Zero ); system.Pump();
			Assert.IsTrue( voice.HasOutput ); Assert.AreNotSame( first, Output( voice ) );
			Assert.AreEqual( serial, voice.Serial );
			CollectionAssert.AreEqual( AudioLevels.Scale( 100, 1000, 1000 ), mixer.Mix( 1 ) );
			first = Output( voice ); system.Pump(); Assert.AreSame( first, Output( voice ) );
		}
		Assert.AreEqual( 1, mixer.Voices.Count );
	}

	[DataTestMethod]
	[DataRow( -1, true )] [DataRow( 0, true )] [DataRow( 1, false )]
	public void OriginalExpiryUsesStrictBoundaryAndAbsenceDoesNotPauseIt( int offset, bool returns )
	{
		var voice = Play(); var deadline = system.ExpiryFrame( voice, 0, 0 );
		Listener( Far ); system.Pump(); clock = deadline + offset;
		Listener( Vector3.Zero ); system.Pump();
		Assert.AreEqual( returns, voice.HasOutput ); Assert.AreEqual( !returns, voice.IsFinished );
		Listener( Far ); system.Pump(); clock = deadline + 1; system.Pump(); Listener( Vector3.Zero ); system.Pump();
		Assert.IsFalse( voice.HasOutput, "return never resets original birth/deadline" );
	}

	[TestMethod]
	public void ExpiredOwnerDoesNotDrawNewStartParameters()
	{
		var sound = Sound() with { MinimumVolume = 10, MaximumVolume = 90, MinimumDelay = 10, MaximumDelay = 100 };
		var voice = Play( Category( sound: sound ) ); Listener( Far ); system.Pump();
		var seed = system.Seed; clock = AudioMixer.OutputRate * 5;
		Listener( Vector3.Zero ); system.Pump();
		Assert.IsTrue( voice.IsFinished ); Assert.IsFalse( voice.HasOutput ); Assert.AreEqual( seed, system.Seed );
	}

	[TestMethod]
	public void NewShorterDelayIsRecheckedAfterAdmissionWithoutRenewingBaseEnd()
	{
		var sound = Sound() with { MinimumDelay = 1, MaximumDelay = 1000 };
		// Choose a seed whose first delay is longer than its next delay, using the production draw rule.
		using var mirrorMixer = new AudioMixer( null );
		var mirror = new SoundEventSystem( mirrorMixer );
		uint seed = 1; double oldDelay, newDelay;
		do { mirror.Reseed( seed++ ); oldDelay = mirror.DelaySeconds( sound ); newDelay = mirror.DelaySeconds( sound ); }
		while ( oldDelay - newDelay < 0.1 );
		system.Reseed( seed - 1 ); var voice = Play( Category( sound: sound ) );
		var newDeadline = system.ExpiryFrame( voice, 0, newDelay );
		var oldDeadline = system.ExpiryFrame( voice, 0, oldDelay );
		Listener( Far ); system.Pump();
		clock = newDeadline + 1;
		Assert.IsTrue( clock <= oldDeadline );
		Listener( Vector3.Zero ); system.Pump();
		Assert.IsTrue( voice.IsFinished ); Assert.IsFalse( voice.HasOutput );
		Assert.AreEqual( mirror.Seed, system.Seed, "old delay admitted, new draw rejected setup" );
	}

	[DataTestMethod]
	[DataRow( 999, true )] [DataRow( 1000, true )] [DataRow( 1001, false )]
	public void NearEndGateUsesStrictOneSecondRemainingBoundary( int elapsedMilliseconds, bool returns )
	{
		// A two-second source: remainder <= 1000 always passes; >1000 leaves <1000 and rejects.
		var voice = Play( Category( frames: AudioMixer.OutputRate * 2 ) );
		Listener( Far ); system.Pump(); clock = mixer.SecondsToFrames( elapsedMilliseconds / 1000.0 );
		Listener( Vector3.Zero ); system.Pump();
		Assert.AreEqual( returns, voice.HasOutput ); Assert.AreEqual( !returns, voice.IsFinished );
	}

	[TestMethod]
	public void ReturningFiniteOwnerWaitsForOriginalRankAndExplicitStopNeverRestarts()
	{
		var voice = Play(); var serial = voice.Serial;
		var newer = Enumerable.Range( 0, SoundEventSystem.VoicePool ).Select( _ => Play() ).ToArray();
		Listener( Far ); system.Pump(); Listener( Vector3.Zero ); system.Pump();
		Assert.IsFalse( voice.HasOutput ); Assert.IsTrue( newer.All( item => item.HasOutput ) );
		// Finite stopped entries retain their pool slot until expiry, as before; move one outside admission instead.
		newer[^1].Position = Far; system.Pump();
		Assert.IsTrue( voice.HasOutput ); Assert.AreEqual( serial, voice.Serial );
		Listener( Far ); system.Pump(); voice.Stop(); Listener( Vector3.Zero ); system.Pump();
		Assert.IsTrue( voice.IsFinished ); Assert.IsFalse( voice.HasOutput );
	}

	[TestMethod]
	public void ReturningRankZeroFiniteOwnerDoesNotDrawUntilQualified()
	{
		var voice = Play( Category( priority: 0 ) ); voice.Position = new Vector3( 3000, 0, 0 );
		system.Pump(); voice.Range = 10000; var seed = system.Seed; system.Pump();
		Assert.IsFalse( voice.HasOutput ); Assert.AreEqual( seed, system.Seed );
		voice.Position = Vector3.Zero; system.Pump(); Assert.IsTrue( voice.HasOutput );
	}
	[DataTestMethod]
	[DataRow( 1000, true )] [DataRow( 1001, true )]
	[DataRow( 1999, true )] [DataRow( 2000, true )] [DataRow( 2001, false )] [DataRow( 3000, true )]
	public void ThreeSecondSourceDistinguishesRemainderAndRemainingTime( int milliseconds, bool returns )
	{
		var voice = Play( Category( frames: AudioMixer.OutputRate * 3 ) );
		Listener( Far ); system.Pump(); clock = mixer.SecondsToFrames( milliseconds / 1000.0 );
		Listener( Vector3.Zero ); system.Pump();
		Assert.AreEqual( returns, voice.HasOutput );
	}

	[TestMethod]
	public void FiniteRestartDrawsVolumePitchDelayWithoutReselectingSource()
	{
		var sound = Sound() with { MinimumVolume = 20, MaximumVolume = 90, MinimumPitch = -3, MaximumPitch = 6,
			MinimumDelay = 10, MaximumDelay = 30,
			Samples = new[] { new SoundSample( 1, 32767, 0, 1, 0 ), new SoundSample( 2, 65535, 0, 1, 0 ) } };
		var voice = Play( Category( sound: sound ) ); var samples = voice.SampleData;
		Listener( Far ); system.Pump();
		using var mirrorMixer = new AudioMixer( null );
		var mirror = new SoundEventSystem( mirrorMixer, system.Seed );
		var volume = 20 + mirror.DrawBelow( 70 ); var pitch = -3 + (int)mirror.DrawBelow( 9 ); _ = mirror.DrawBelow( 20 );
		Listener( Vector3.Zero ); system.Pump();
		Assert.AreEqual( volume / 100f, voice.Gain ); Assert.AreEqual( pitch, voice.PitchStep );
		Assert.AreEqual( mirror.Seed, system.Seed ); Assert.AreSame( samples, voice.SampleData );
	}

	[TestMethod]
	public void LongerRedrawnDelayKeepsNonzeroBirthAndUpdatesOnlyTheTail()
	{
		var sound = Sound() with { MinimumDelay = 1, MaximumDelay = 1000 };
		using var mirrorMixer = new AudioMixer( null );
		var mirror = new SoundEventSystem( mirrorMixer );
		uint seed = 1; double oldDelay, newDelay;
		do { mirror.Reseed( seed++ ); oldDelay = mirror.DelaySeconds( sound ); newDelay = mirror.DelaySeconds( sound ); }
		while ( newDelay - oldDelay < 0.1 );
		clock = 123456; system.Reseed( seed - 1 );
		var voice = Play( Category( sound: sound ) );
		var oldDeadline = system.ExpiryFrame( voice, 0, oldDelay );
		var newDeadline = system.ExpiryFrame( voice, 0, newDelay );
		Listener( Far ); system.Pump(); clock += 100;
		Listener( Vector3.Zero ); system.Pump();
		Assert.IsTrue( voice.HasOutput ); Assert.AreEqual( mirror.Seed, system.Seed );
		Listener( Far ); system.Pump(); clock = oldDeadline + 1; system.Pump();
		Assert.IsFalse( voice.IsFinished, "the redrawn delay, not the stale initial delay, now controls expiry" );
		clock = newDeadline; system.Pump(); Assert.IsFalse( voice.IsFinished, "strict expiry equality" );
		clock++; system.Pump(); Assert.IsTrue( voice.IsFinished );
		Listener( Vector3.Zero ); system.Pump(); Assert.IsFalse( voice.HasOutput );
	}

	[TestMethod]
	public void ExpiryExemptNonloopRangeStopRemainsOutsideThisFiniteChange()
	{
		var voice = Play( Category( flags: 4 ) );
		Listener( Far ); system.Pump(); Listener( Vector3.Zero ); system.Pump();
		Assert.IsFalse( voice.Loops ); Assert.IsTrue( voice.IsFinished ); Assert.IsFalse( voice.HasOutput );
	}

}
