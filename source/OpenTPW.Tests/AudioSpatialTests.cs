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
	public void PositionedPcmHasBalancedCentreAndNonInvertedSideChannels()
	{
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		foreach ( var (position, left, right) in new[]
		{
			(new Vector3( 0, 0, 1 ), (short)707, (short)707),
			(new Vector3( -1, 0, 0 ), (short)1000, (short)0),
			(new Vector3( 1, 0, 0 ), (short)0, (short)1000),
			(Vector3.Zero, (short)707, (short)707)
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
	public void OutOfRangeLoopsAdvanceSilentlyAndBecomeAudibleAgain()
	{
		using var mixer = new AudioMixer( null );
		AudioListener.Update( new Vector3( 100, 0, 0 ), new Vector3( 0, 0, 1 ) );
		var loop = mixer.Play( Sample( 1000, 2000, 3000, 4000 ), AudioChannel.Effects, 1, Vector3.Zero, 10 );
		loop.Loops = true;
		CollectionAssert.AreEqual( new short[6], mixer.Mix( 3 ) );
		Assert.IsFalse( loop.IsFinished, "a inaudible loop remains available when the listener returns" );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		CollectionAssert.AreEqual( new short[] { 2828, 2828, 707, 707 }, mixer.Mix( 2 ), "phase advances while virtualized and then wraps" );
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
			camera.SetPose( new Vector3( 10, 20, 0 ), 20, 0 );
			Assert.AreEqual( new Vector3( 100, 50, 100 ), AudioListener.Position, "eye/target midpoint in original sound units" );
			Assert.AreEqual( -1f, AudioListener.Forward.X, 0.001f );
			Assert.AreEqual( 0f, AudioListener.Forward.Z, 0.001f );
			camera.SetPose( new Vector3( 20, 30, 0 ), 20, 90 );
			Assert.AreEqual( 100f, AudioListener.Position.X, 0.001f );
			Assert.AreEqual( 200f, AudioListener.Position.Z, 0.001f );
			Assert.AreEqual( -1f, AudioListener.Forward.Z, 0.001f );
		}
		finally { ParkCameraMode.TargetExtent = extent; ParkCameraMode.InitialTarget = target; }
	}
}
