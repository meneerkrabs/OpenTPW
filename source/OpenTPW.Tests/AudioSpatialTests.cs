using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class AudioSpatialTests
{
	private static Mp2Audio Sample( params short[] samples ) => new( AudioMixer.OutputRate, 1, samples, 1, 0 );

	[TestCleanup]
	public void ResetListener() => AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );

	[TestMethod]
	public void PositionedMonoPcmFollowsTheDirectSoundPanOfTheSoftwareStage()
	{
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		foreach ( var (position, left, right) in new[]
		{
			// [BIN:QMIXER-DLL:0x18005830 volume and pan] the louder channel keeps the level and the other is 10^(-|pan| / 2000) of it: 90 degrees is a pan of 4000, so 0.01
			(new Vector3( 0, 0, 1 ), (short)1000, (short)1000),
			(new Vector3( -1, 0, 0 ), (short)1000, (short)10),
			(new Vector3( 1, 0, 0 ), (short)10, (short)1000),
			(Vector3.Zero, (short)1000, (short)1000)
		} )
		{
			using var mixer = new AudioMixer( null );
			mixer.Play( Sample( 1000 ), AudioChannel.Effects, 1, position, 100 );
			var pcm = mixer.Mix( 1 );
			Assert.AreEqual( left, pcm[0], $"left at {position}" );
			Assert.AreEqual( right, pcm[1], $"right at {position}" );
		}
	}

	[TestMethod]
	public void AZeroRangeVoicePlaysFlatAtFullGain()
	{
		using var mixer = new AudioMixer( null );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		mixer.Play( Sample( 1000 ), AudioChannel.Effects, 1, new Vector3( 1000, 0, 0 ), 0 );
		CollectionAssert.AreEqual( new short[] { 1000, 1000 }, mixer.Mix( 1 ) );
	}

	[TestMethod]
	public void RawMixerLoopsKeepTheirCursorWithoutCatalogueRangeAdmission()
	{
		using var mixer = new AudioMixer( null );
		AudioListener.Update( new Vector3( 100, 0, 0 ), new Vector3( 0, 0, 1 ) );
		var loop = mixer.Play( Sample( 1000, 2000, 3000, 4000 ), AudioChannel.Effects, 1, Vector3.Zero, 10 );
		loop.Loops = true;
		CollectionAssert.AreEqual( new short[6], mixer.Mix( 3 ) );
		Assert.IsFalse( loop.IsFinished, "a inaudible loop remains available when the listener returns" );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		CollectionAssert.AreEqual( new short[] { 4000, 4000, 1000, 1000 }, mixer.Mix( 2 ), "a raw mixer source has no catalogue owner/range-stop policy" );
		loop.Stop();
		CollectionAssert.AreEqual( new short[2], mixer.Mix( 1 ) );
	}

	[TestMethod]
	public void InaudibleOneShotsStillFinishOnTheirOriginalFrame()
	{
		using var mixer = new AudioMixer( null );
		AudioListener.Update( new Vector3( 100, 0, 0 ), new Vector3( 0, 0, 1 ) );
		var voice = mixer.Play( Sample( 1000, 2000, 3000, 4000 ), AudioChannel.Effects, 1, Vector3.Zero, 10 );
		var completed = 0;
		voice.Completed += _ => completed++;
		CollectionAssert.AreEqual( new short[6], mixer.Mix( 3 ) );
		Assert.IsFalse( voice.IsFinished );
		mixer.Mix( 1 );
		Assert.IsTrue( voice.IsFinished );
		Assert.AreEqual( 1, completed );
		Assert.AreEqual( 0, mixer.Voices.Count );
	}

	[TestMethod]
	public void ALoopWrapsWhenOneResamplingStepSpansMultipleSourceBuffers()
	{
		using var mixer = new AudioMixer( null );
		var voice = mixer.Play( new Mp2Audio( AudioMixer.OutputRate * 8, 1, new short[] { 1000, 2000, 3000 }, 1, 0 ), AudioChannel.Effects );
		voice.Loops = true;
		CollectionAssert.AreEqual( new short[] { 1000, 1000, 3000, 3000, 2000, 2000, 1000, 1000 }, mixer.Mix( 4 ) );
		Assert.IsFalse( voice.IsFinished );
	}

	[TestMethod]
	public void InvalidPlacementIsRejectedBeforeItCanPoisonTheMix()
	{
		using var mixer = new AudioMixer( null );
		foreach ( var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
		{
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => mixer.Play( Sample( 1000 ), AudioChannel.Effects, 1, new Vector3( invalid, 0, 0 ), 100 ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => mixer.Play( Sample( 1000 ), AudioChannel.Effects, 1, Vector3.Zero, invalid ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => AudioListener.Update( new Vector3( invalid, 0, 0 ), new Vector3( 0, 0, 1 ) ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, invalid ) ) );
		}
		Assert.AreEqual( 0, mixer.Voices.Count );
		mixer.Play( Sample( 1000 ), AudioChannel.Effects );
		CollectionAssert.AreEqual( new short[] { 1000, 1000 }, mixer.Mix( 1 ) );
	}

	[TestMethod]
	public void CameraPoseMovesAndRotatesTheSoundListener()
	{
		var extent = ParkCameraMode.TargetExtent;
		var target = ParkCameraMode.InitialTarget;
		try
		{
			ParkCameraMode.TargetExtent = 100;
			ParkCameraMode.InitialTarget = Vector3.Zero;
			var camera = new ParkCameraMode();
			// zoom 70: pitch 45 degrees, eye 9.8995 engine units out and up (70 x cos/sin 45 x 0.2)
			camera.SetPose( new Vector3( 10, 20, 0 ), 70, 0 );
			Assert.AreEqual( 74.7487f, AudioListener.Position.X, 0.01f, "eye/target midpoint in original sound units" );
			Assert.AreEqual( 24.7487f, AudioListener.Position.Y, 0.01f );
			Assert.AreEqual( 100f, AudioListener.Position.Z, 0.01f );
			Assert.AreEqual( -1f, AudioListener.Forward.X, 0.001f );
			Assert.AreEqual( 0f, AudioListener.Forward.Z, 0.001f );
			camera.SetPose( new Vector3( 20, 30, 0 ), 70, 90 );
			Assert.AreEqual( 100f, AudioListener.Position.X, 0.001f );
			Assert.AreEqual( 174.7487f, AudioListener.Position.Z, 0.01f );
			Assert.AreEqual( -1f, AudioListener.Forward.Z, 0.001f );
		}
		finally { ParkCameraMode.TargetExtent = extent; ParkCameraMode.InitialTarget = target; }
	}
}
