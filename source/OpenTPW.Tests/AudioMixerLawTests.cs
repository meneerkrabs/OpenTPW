using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The volume, pan and listener laws of the original's software channel stage (QMixer.dll 0x18005830, 0x18005730, 0x1800ca40, 0x1800ce70).</summary>
[TestClass]
[DoNotParallelize]
public class AudioMixerLawTests
{
	private static Mp2Audio Mono( short sample ) => new( AudioMixer.OutputRate, 1, new[] { sample }, 1, 0 );

	private static Mp2Audio Stereo( short left, short right ) => new( AudioMixer.OutputRate, 2, new[] { left, right }, 1, 0 );

	[TestCleanup]
	public void ResetListener() => AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );

	[DataTestMethod]
	[DataRow( 0f, 0 )]
	[DataRow( 45f, 2000 )]
	[DataRow( 90f, 4000 )]
	[DataRow( -90f, -4000 )]
	[DataRow( 135f, 2000 )]
	[DataRow( -135f, -2000 )]
	[DataRow( 180f, 0 )]
	[DataRow( -180f, 0 )]
	[DataRow( 450f, 4000 )]
	[DataRow( -270f, 4000 )]
	public void TheAzimuthBecomesADirectSoundPanMirroredBehindTheListener( float azimuth, int pan )
	{
		// [BIN:QMIXER-DLL:0x18005730 pan law] 44.4444 hundredths of a decibel a degree, folded at +-90 so the pan stays within +-4000
		Assert.AreEqual( pan, AudioListener.DirectSoundPan( azimuth ) );
	}

	[TestMethod]
	public void ThePanKeepsTheLouderChannelAndAttenuatesOnlyTheOtherOne()
	{
		Assert.AreEqual( (1f, 1f), AudioListener.PanGains( 0 ) );
		var (left, right) = AudioListener.PanGains( 4000 );
		Assert.AreEqual( 0.01f, left, 1e-6f );
		Assert.AreEqual( 1f, right );
		var (quiet, loud) = AudioListener.PanGains( -2000 );
		Assert.AreEqual( 0.1f, loud, 1e-6f, "-2000 hundredths of a decibel is 10^-1" );
		Assert.AreEqual( 1f, quiet );
		Assert.AreEqual( 1f, AudioListener.PanGains( -2000 ).Left );
	}

	[TestMethod]
	public void TheVolumeIsTwoThousandLog10OfTheGainRoundedToHundredthsOfADecibel()
	{
		Assert.AreEqual( 0, AudioListener.GainHundredthsOfDecibel( 1f ) );
		Assert.AreEqual( -10000, AudioListener.GainHundredthsOfDecibel( 0f ) );
		Assert.AreEqual( -10000, AudioListener.GainHundredthsOfDecibel( -3f ) );
		Assert.AreEqual( -602, AudioListener.GainHundredthsOfDecibel( 0.5f ) );
		Assert.AreEqual( -2000, AudioListener.GainHundredthsOfDecibel( 0.1f ) );
		Assert.AreEqual( 1f, AudioListener.DecibelGain( 0 ) );
		Assert.AreEqual( 0.1f, AudioListener.DecibelGain( -2000 ), 1e-6f );
		Assert.AreEqual( 0f, AudioListener.DecibelGain( -10000 ), "-10000 is DirectSound's silence" );
	}

	[TestMethod]
	public void OnlyAMonoWaveIsPannedAStereoWaveKeepsBothChannelsAtTheDistanceGain()
	{
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		using var mixer = new AudioMixer( null );
		mixer.Play( Stereo( 1000, 500 ), AudioChannel.Effects, 1, new Vector3( 1, 0, 0 ), 100 );
		CollectionAssert.AreEqual( new short[] { 1000, 500 }, mixer.Mix( 1 ), "[BIN:QMIXER-DLL:0x18005982] the pan is set only for the wave format table's single-channel entry" );
	}

	[TestMethod]
	public void ThePitchOfTheCameraDecidesTheAzimuth()
	{
		// a camera ten units up looking straight down, its up vector toward +z of the ground plane (sound space: y up, z ahead on the ground)
		AudioListener.Update( new Vector3( 0, 10, 0 ), new Vector3( 0, -1, 0 ), new Vector3( 0, 0, 1 ) );
		Assert.AreEqual( new Vector3( 0, -1, 0 ), AudioListener.Front );
		Assert.AreEqual( new Vector3( 0, 0, 1 ), AudioListener.Up );
		// the source straight below is straight ahead of the camera
		Assert.AreEqual( 0, AudioListener.Placement( new Vector3( 0, 0, 0 ), 100 ).Pan );
		// a source as far to the side as it is below is 45 degrees off, where an upright listener would call it 90 degrees
		Assert.AreEqual( 45f, MathF.Abs( AudioListener.Azimuth( new Vector3( 10, -10, 0 ) ) ), 0.001f );
		Assert.AreEqual( 2000, AudioListener.Placement( new Vector3( 10, 0, 0 ), 100 ).Pan );
		AudioListener.Update( new Vector3( 0, 10, 0 ), new Vector3( 0, 0, 1 ) );
		Assert.AreEqual( 4000, AudioListener.Placement( new Vector3( 10, 10, 0 ), 100 ).Pan );
	}

	[TestMethod]
	public void AnUprightListenerKeepsTheGroundBearing()
	{
		AudioListener.Update( Vector3.Zero, new Vector3( 1, 0, 0 ) );
		Assert.AreEqual( 0, AudioListener.Placement( new Vector3( 5, 0, 0 ), 100 ).Pan, "straight ahead along +x" );
		Assert.AreEqual( -4000, AudioListener.Placement( new Vector3( 0, 0, 5 ), 100 ).Pan, "facing +x the +z side is the left" );
		Assert.AreEqual( 4000, AudioListener.Placement( new Vector3( 0, 0, -5 ), 100 ).Pan );
		Assert.AreEqual( 0, AudioListener.Placement( new Vector3( -5, 0, 0 ), 100 ).Pan, "behind is as centred as ahead" );
	}

	[TestMethod]
	public void TheEngineCameraAxesBecomeTheSoundSpaceBasis()
	{
		GameAudio.UpdateListener( new System.Numerics.Vector3( 0, 0, 0 ), new System.Numerics.Vector3( 0, 1, 0 ), null, new System.Numerics.Vector3( 0, 0, 1 ) );
		Assert.AreEqual( new Vector3( 0, 0, 1 ), AudioListener.Front, "engine +Y (ground ahead) is the sound z" );
		Assert.AreEqual( new Vector3( 0, 1, 0 ), AudioListener.Up, "engine +Z (up) is the sound y" );
		// right = up x front is the engine +X: a source to the engine's east pans right
		Assert.IsTrue( AudioListener.Placement( new Vector3( 5, 0, 0 ), 100 ).Pan > 0 );
	}
}
