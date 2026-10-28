using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>AUDIO-040: the fade poll's worker-thread arithmetic against the main thread's (tp.exe 0x6bb860, 0x6b6260, 0x6b9e30).</summary>
[TestClass]
[DoNotParallelize]
public class AudioPollPrecisionTests
{
	private static Mp2Audio Mono( params short[] samples ) => new( AudioMixer.OutputRate, 1, samples, 1, 0 );

	[TestMethod]
	public void TheFadePollMultipliesAtFiftyThreeBitsAndTheMainThreadAtTwentyFour()
	{
		// [BIN:TPW-EXE:0x6BB860 effective volume] [BIN:TPW-EXE:0x6B6260 fade thread]: master 0.75 and effects 0.7 on a volume of 60 are 31.5 in 53 bits and 31.499998 once the product is a single
		using var mixer = new AudioMixer( null ) { MasterGain = 0.75f };
		mixer.SetChannelGain( AudioChannel.Effects, 0.7f );
		var lifetime = new HeldSoundLifetime( mixer, SoundEventPlayer.OneShot, 0b1001 );
		var voice = mixer.Play( Mono( 1000, 1000 ), AudioChannel.Effects, 0.6f );
		lifetime.Admit( voice, 60 );
		Assert.AreEqual( 31, lifetime.MainEffectiveVolume() );
		Assert.AreEqual( 32, lifetime.PollEffectiveVolume() );
		Assert.AreEqual( 31, lifetime.DeviceVolume, "a group write or a start computes on the main thread" );
		lifetime.PostFade();
		lifetime.Poll();
		Assert.AreEqual( 32 * 60 / 100, lifetime.OwnVolume, "the poll's own multiplication" );
	}

	[TestMethod]
	public void TheTwoPrecisionsAgreeWhereTheProductIsExact()
	{
		using var mixer = new AudioMixer( null ) { MasterGain = 0.5f };
		mixer.SetChannelGain( AudioChannel.Effects, 0.5f );
		var lifetime = new HeldSoundLifetime( mixer, SoundEventPlayer.OneShot, 0b1001 );
		lifetime.Admit( mixer.Play( Mono( 1000, 1000 ), AudioChannel.Effects, 1f ), 100 );
		Assert.AreEqual( 25, lifetime.MainEffectiveVolume() );
		Assert.AreEqual( 25, lifetime.PollEffectiveVolume() );
		var plain = new HeldSoundLifetime( mixer, SoundEventPlayer.OneShot, 0 );
		plain.Admit( mixer.Play( Mono( 1000, 1000 ), AudioChannel.Effects, 1f ), 77 );
		Assert.AreEqual( 77, plain.PollEffectiveVolume() );
		Assert.AreEqual( 77, plain.MainEffectiveVolume() );
	}

}
