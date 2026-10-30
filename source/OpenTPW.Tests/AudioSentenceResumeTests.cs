using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>AUDIO-038: the pool rank of a started any-distance voice and the resume of a stopped sentence (tp.exe 0x6bc51d, 0x6bb9f0, 0x6bdf50, 0x6bc410).</summary>
[TestClass]
[DoNotParallelize]
public class AudioSentenceResumeTests
{
	[TestCleanup]
	public void ResetListener() => AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );

	[TestMethod]
	public void AStartedAnyDistanceVoiceIsRankedAtTheOriginWhateverPlaceItsCallerGave()
	{
		// [BIN:TPW-EXE:0x6bc51d flat placement] [BIN:TPW-EXE:0x6bb9f0 rank distance]
		AudioListener.Update( new Vector3( 100, 0, 0 ), new Vector3( 0, 0, 1 ) );
		var far = new Vector3( 900, 0, 900 );
		Assert.AreEqual( Vector3.Zero, SoundEventSystem.RankPlace( 0x200, far, started: true ) );
		Assert.AreEqual( far, SoundEventSystem.RankPlace( 0x200, far, started: false ) );
		Assert.AreEqual( Vector3.Zero, SoundEventSystem.RankPlace( 0x200, null, started: false ) );
		Assert.AreEqual( far, SoundEventSystem.RankPlace( 0x4, far, started: true ), "an ordinary placed voice keeps its place" );
		Assert.IsNull( SoundEventSystem.RankPlace( 0x4, null, started: true ) );
		Assert.AreEqual( 0xFFFF - 100, SoundEventSystem.PoolKey( SoundEventSystem.RankPlace( 0x200, far, started: true ) ), "100 units from the origin: d^2 / 100 = 100" );
		Assert.AreEqual( 0xFFFF, SoundEventSystem.PoolKey( null ) );
	}

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	[TestMethod]
	public void ASentenceStoppedOutOfRangeReplaysItsSegmentFromTheStartAndKeepsItsSuccessor()
	{
		// [BIN:TPW-EXE:0x6BDF50,0x6BC410,0x6B9AB0 stopped sentence resumes]
		Log ??= new();
		using var mixer = new AudioMixer( null );
		long clock = 0;
		var system = new SoundEventSystem( mixer, 7, () => 0 ) { PoolClock = () => clock };
		// a volume range, so the sample setup draws; the pitch and delay ranges are empty
		var sound = new SoundEntry( 50, 90, 0, 0, 0, 0, 1, 10,
			new[] { new SoundSample( 1, 32767, 0, 1, 0 ), new SoundSample( 2, 65535, 0, 1, 0 ) }, Array.Empty<SoundLink>(), Array.Empty<byte>() );
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		Set( catalog, "Events", new[] { new SoundEvent( 1, 100, 0x6, 0, new[] { sound } ) } );
		Set( category, "Catalog", catalog );
		foreach ( var sample in sound.Samples )
			system.RememberDecoded( category, sample, new Mp2Audio( AudioMixer.OutputRate, 1,
				Enumerable.Range( 0, 40 ).Select( index => (short)(sample.SampleNumber * 10000 + index * 100) ).ToArray(), 1, 0 ), false );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		var sentence = system.StartSentence( category, 1, AudioChannel.Effects, null, Vector3.Zero, 10 )!;
		system.Pump();
		var first = mixer.Mix( 5 );
		var segment = first[0] < 10000 * 0.9 + 1 ? 1 : 2;
		// samples 1 and 2 are 10000 + 100 i and 20000 + 100 i at a gain of 0.5 .. 0.9
		var gain = first[0] / (segment * 10000.0);
		Assert.IsTrue( gain is >= 0.499 and <= 0.901, $"gain {gain}" );
		Assert.AreEqual( (short)(segment * 10000 * gain + 100 * gain), first[2], 2 );
		var seed = system.Seed;
		AudioListener.Update( new Vector3( 1000, 0, 0 ), new Vector3( 0, 0, 1 ) );
		system.Pump();
		Assert.IsFalse( sentence.IsPlaying, "the range stop ends the output" );
		CollectionAssert.AreEqual( new short[6], mixer.Mix( 3 ) );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		system.Pump();
		Assert.AreNotEqual( seed, system.Seed, "the sample setup draws the volume again" );
		var replayed = mixer.Mix( 2 );
		var again = replayed[0] / (segment * 10000.0);
		Assert.IsTrue( again is >= 0.499 and <= 0.901, $"replay gain {again}: the same segment from its first frame" );
		Assert.AreEqual( replayed[0], replayed[1] );
		// the replayed segment ends after 40 frames; the successor, chosen before the stop, follows it
		var rest = mixer.Mix( 60 );
		var other = 3 - segment;
		Assert.IsTrue( rest.Any( value => value >= other * 5000 && value / (other * 10000.0) <= 0.91 && value / (other * 10000.0) >= 0.49 ), "the prefetched successor plays after the replay" );
	}
}
