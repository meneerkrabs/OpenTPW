using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// AUDIO-003 delay: a non-chain sound draws its delay at every sample start (after the volume and the pitch, tp.exe 0x6bc2d0 / 0x6bc4f4) and keeps
/// its place in the voice pool until its sample's nominal end plus 250 ms plus that delay (0x6bc01b, 0x6bcb60, the tick 0x6b62b0); an event flagged
/// 0x4 or 0x8 is exempt (voice flag 2, 0x6bbe90). Synthetic data.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AudioVoiceExpiryTests
{
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;
	private long clock;

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, 7 ) { PoolClock = () => clock };
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		clock = 1000;
	}

	[TestCleanup]
	public void Cleanup()
	{
		system.StopChains();
		mixer.Dispose();
	}

	private static SoundEntry Sound( ushort minimumDelay, ushort maximumDelay ) =>
		new( 100, 100, 0, 0, minimumDelay, maximumDelay, 1, 100, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, Array.Empty<SoundLink>(), Array.Empty<byte>() );

	private SoundCategory Category( uint priority, ushort flags, int frames, params SoundEntry[] sounds )
	{
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!;
		decoded[(category, 1, 1)] = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, frames ).ToArray(), 1, 0 );
		Set( catalog, "Events", new[] { new SoundEvent( 5, priority, flags, 0, sounds ) } );
		Set( category, "Catalog", catalog );
		return category;
	}

	private static int Children( SoundChain chain ) =>
		((System.Collections.ICollection)typeof( SoundChain ).GetField( "children", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( chain )!).Count;

	private static void MakeDue( SoundChain chain ) => typeof( SoundChain ).GetField( "nextChildAt", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( chain, 0L );

	[TestMethod]
	public void TheDelayIsDrawnAfterTheVolumeAndThePitchAtEverySampleStart()
	{
		var category = Category( 5300, 0, 1000, Sound( 100, 300 ) );
		var mirror = new SoundEventSystem( new AudioMixer( null ), system.Seed );
		_ = mirror.DrawBelow( 200 );
		system.Play( category, 5, AudioChannel.Effects );
		Assert.AreEqual( mirror.Seed, system.Seed, "the volume and the pitch of this sound are fixed and draw nothing; the delay draws once, min + r % (max - min)" );
	}

	[TestMethod]
	public void ASoundWithoutDelayBoundsDrawsNothing()
	{
		var category = Category( 5300, 0, 1000, Sound( 0, 0 ) );
		var seed = system.Seed;
		system.Play( category, 5, AudioChannel.Effects );
		Assert.AreEqual( seed, system.Seed );
	}

	[TestMethod]
	public void AFinishedVoiceKeepsItsPlaceUntilItsEndPlusTailPlusDelay()
	{
		// twelve flat one-shots of priority 5300 and 1000 frames (45 ms): expiry = 45 ms + 250 ms after the start
		var loud = Category( 5300, 0, 1000, Sound( 0, 0 ) );
		var voices = Enumerable.Range( 0, SoundEventSystem.VoicePool ).Select( _ => system.Play( loud, 5, AudioChannel.Effects )! ).ToList();
		var chain = system.StartChain( Category( 2700, 0x404, 1000, Sound( 0, 0 ) ), 5, AudioChannel.Effects, 50, null )!;
		foreach ( var voice in voices )
			voice.Stop();
		MakeDue( chain );
		system.Pump();
		Assert.AreEqual( 0, Children( chain ), "the finished voices still hold the twelve places" );

		clock += mixer.SecondsToFrames( 1000 / 22050.0 + SoundEventSystem.ExpiryTailSeconds ) - 1;
		system.Pump();
		Assert.AreEqual( 0, Children( chain ), "one frame before the expiry" );

		clock += 2;
		system.Pump();
		Assert.AreEqual( 1, Children( chain ), "expired: the places are free and the chain starts its first child" );
	}

	[TestMethod]
	public void TheDrawnDelayLengthensTheStay()
	{
		var sound = Sound( 1000, 1001 );
		var loud = Category( 5300, 0, 1000, sound );
		var voice = system.Play( loud, 5, AudioChannel.Effects )!;
		var expected = clock + mixer.SecondsToFrames( 1000 / 22050.0 + SoundEventSystem.ExpiryTailSeconds + 1.0 );
		Assert.AreEqual( expected, system.ExpiryFrame( voice, 0, 1.0 ) );
	}

	[TestMethod]
	public void AnEventFlaggedFourOrEightHasNoExpiry()
	{
		var category = Category( 5300, 0x8, 1000, Sound( 0, 0 ) );
		var voice = system.Play( category, 5, AudioChannel.Effects )!;
		Assert.AreEqual( -1, system.ExpiryFrame( voice, 0x8, 0 ) );
		Assert.AreEqual( -1, system.ExpiryFrame( voice, 0x4, 0 ) );
		Assert.IsTrue( system.ExpiryFrame( voice, 0x200, 0 ) > clock );
	}
}
