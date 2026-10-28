using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// AUDIO-018: QMixer's Doppler factor (QMixer.dll 0x1800a178-0x1800a2c3, applied by 0x18005550) fed by the listener velocity tp.exe sends (0x6b79d0 -> 0x6d27b0).
/// Synthetic PCM on the offline mixer: the stream's rate is read from which source frame each output frame takes.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AudioDopplerTests
{
	private static readonly Vector3 Ahead = new( 0, 0, 1 );
	private static readonly Vector3 UpAxis = new( 0, 1, 0 );

	[TestInitialize]
	public void Setup()
	{
		AudioListener.DopplerEnabled = true;
		AudioListener.ResetMotion();
		AudioListener.Update( Vector3.Zero, Ahead, UpAxis );
		AudioListener.ResetMotion();
	}

	[TestCleanup]
	public void Cleanup()
	{
		AudioListener.ResetMotion();
		AudioListener.Update( Vector3.Zero, Ahead );
		AudioListener.ResetMotion();
		AudioListener.DopplerEnabled = false;
	}

	private static Mp2Audio Ramp() => new( AudioMixer.OutputRate, 1, Enumerable.Range( 0, 256 ).Select( index => (short)(index * 100) ).ToArray(), 1, 0 );

	/// <summary>The listener at <paramref name="start"/>, then one changed update that moves it by <paramref name="step"/>: velocity = step.</summary>
	private static void MoveBy( Vector3 start, Vector3 step )
	{
		AudioListener.ResetMotion();
		AudioListener.Update( start, Ahead, UpAxis );
		AudioListener.Update( start + step, Ahead, UpAxis );
	}

	[TestMethod]
	public void TheVelocityIsThePlaceChangeOfTheLastChangedUpdateAndTheFirstUpdateStartsFromTheOrigin()
	{
		// [BIN:TPW-EXE:0x6b79d0 listener velocity] previous place = +8 of the sound system object, zero when the process starts
		AudioListener.ResetMotion();
		AudioListener.Update( new Vector3( 100, 7, 300 ), Ahead, UpAxis );
		Assert.AreEqual( new Vector3( 100, 7, 300 ), AudioListener.Velocity, "the first update gets its whole place as velocity" );
		AudioListener.Update( new Vector3( 100, 7, 300 ), Ahead, UpAxis );
		Assert.AreEqual( new Vector3( 100, 7, 300 ), AudioListener.Velocity, "an identical update (0x6b8b60 compares byte for byte) sends nothing" );
		AudioListener.Update( new Vector3( 103, 7, 298 ), Ahead, UpAxis );
		Assert.AreEqual( new Vector3( 3, 0, -2 ), AudioListener.Velocity );
		AudioListener.Update( new Vector3( 103, 7, 298 ), new Vector3( 1, 0, 0 ), UpAxis );
		Assert.AreEqual( Vector3.Zero, AudioListener.Velocity, "a turn alone is a changed update with no movement: velocity 0" );
		AudioListener.Update( new Vector3( 103, 7, 298 ), new Vector3( 1, 0, 0 ), UpAxis );
		Assert.AreEqual( Vector3.Zero, AudioListener.Velocity );
	}

	[TestMethod]
	public void TheLobbyListenerStartsTheParksVelocityFromItsPlace()
	{
		// [BIN:TPW-EXE:0x54e70e lobby listener] (0, 0, -50), front (0, 0, 1), up (0, 1, 0); nothing resets the previous place for a park
		AudioListener.ResetMotion();
		GameAudio.UpdateLobbyListener();
		Assert.AreEqual( new Vector3( 0, 0, -50 ), AudioListener.Position );
		Assert.AreEqual( new Vector3( 0, 0, -50 ), AudioListener.Velocity );
		GameAudio.UpdateLobbyListener();
		Assert.AreEqual( new Vector3( 0, 0, -50 ), AudioListener.Velocity, "the unchanged lobby listener sends nothing more" );
		AudioListener.Update( new Vector3( 200, 5, 150 ), Ahead, UpAxis );
		Assert.AreEqual( new Vector3( 200, 5, 200 ), AudioListener.Velocity, "the park's first update is measured from the lobby's place" );
	}

	[TestMethod]
	public void ALobbyFrameThenAParkFrameGivesTheParkItsFirstVelocityAndAFlatVoiceTheFactorOfIt()
	{
		// the lobby listener, then the park's first update (the Jungle camera of the smoke run: place (475, 46.8, 146.1), pitched down 58 degrees): V = -(place - (0, 0, -50)), the flat voice sits straight ahead
		AudioListener.ResetMotion();
		GameAudio.UpdateLobbyListener();
		var front = new Vector3( 0, -0.8511167f, 0.52497655f );
		AudioListener.Update( new Vector3( 475, 46.811417f, 146.12628f ), front, new Vector3( 0, 0.52497655f, 0.8511167f ) );
		Assert.AreEqual( new Vector3( 475, 46.811417f, 196.12628f ), AudioListener.Velocity );
		var closing = Vector3.Dot( front, -AudioListener.Velocity );
		Assert.AreEqual( 1f / (1 + closing / 331.36f), AudioListener.DopplerFactor( null ), 1e-4f );
		AudioListener.DopplerEnabled = true;
		using var mixer = new AudioMixer( null );
		mixer.Play( Ramp(), AudioChannel.Effects, 1f );
		var pcm = mixer.Mix( 3 );
		var factor = AudioListener.DopplerFactor( null );
		Assert.AreEqual( 2 * Math.Round( AudioMixer.OutputRate * factor ) / AudioMixer.OutputRate, pcm[4] / 100.0, 0.1, "the third frame reads the source at the stream's rate" );
		// the next changed update (the camera moves 25 units) replaces the velocity
		AudioListener.Update( new Vector3( 500, 46.811417f, 146.12628f ), front, new Vector3( 0, 0.52497655f, 0.8511167f ) );
		Assert.AreEqual( new Vector3( 25, 0, 0 ), AudioListener.Velocity );
	}

	[TestMethod]
	public void TheMotionIsCapturedAndRestoredWithTheListener()
	{
		MoveBy( new Vector3( 10, 0, 10 ), new Vector3( 0, 0, 4 ) );
		var motion = AudioListener.CaptureMotion();
		AudioListener.Update( new Vector3( 500, 0, 500 ), Ahead, UpAxis );
		Assert.AreNotEqual( new Vector3( 0, 0, 4 ), AudioListener.Velocity );
		AudioListener.RestoreMotion( motion );
		Assert.AreEqual( new Vector3( 0, 0, 4 ), AudioListener.Velocity );
	}

	[TestMethod]
	public void AListenerClosingOnASourceRaisesItsPitchAndOneMovingAwayLowersIt()
	{
		// [BIN:QMIXER-DLL:0x1800a178 Doppler] x = (direction . relative velocity) / 331.36 with the source at rest: closing at 5 units an update gives 1 / (1 - 5 / 331.36)
		MoveBy( new Vector3( 0, 0, 0 ), new Vector3( 0, 0, 5 ) );
		var ahead = new Vector3( 0, 0, 105 );
		var closing = AudioListener.DopplerFactor( ahead );
		Assert.AreEqual( 1 / (1 - 5 / 331.36), closing, 1e-5 );
		Assert.IsTrue( closing > 1 );
		var behind = AudioListener.DopplerFactor( new Vector3( 0, 0, -95 ) );
		Assert.AreEqual( 1 / (1 + 5 / 331.36), behind, 1e-5 );
		Assert.AreEqual( 1f, AudioListener.DopplerFactor( new Vector3( 100, 0, 5 ) ), 1e-6, "a source abeam has no closing speed" );
		Assert.AreEqual( closing, AudioListener.DopplerFactor( null ), 1e-5, "a flat voice sits straight ahead of the listener" );
	}

	[TestMethod]
	public void TheFactorIsHeldAtTenAndAStandingListenerLeavesPitchAlone()
	{
		MoveBy( Vector3.Zero, new Vector3( 0, 0, 400 ) );
		Assert.AreEqual( 10f, AudioListener.DopplerFactor( new Vector3( 0, 0, 900 ) ), "x <= -1: 0x1800a26f" );
		MoveBy( Vector3.Zero, new Vector3( 0, 0, 330 ) );
		Assert.AreEqual( 10f, AudioListener.DopplerFactor( null ), "1 / (x + 1) above 10 is held at 10: 0x1800a2b0" );
		MoveBy( new Vector3( 5, 0, 5 ), Vector3.Zero );
		AudioListener.Update( new Vector3( 5, 0, 5 ), new Vector3( 1, 0, 0 ), UpAxis );
		Assert.AreEqual( 1f, AudioListener.DopplerFactor( new Vector3( 50, 0, 50 ) ) );
		Assert.AreEqual( 1f, AudioListener.DopplerFactor( null ) );
		AudioListener.ResetMotion();
		Assert.AreEqual( 1f, AudioListener.DopplerFactor( null ) );
	}

	[TestMethod]
	public void AFlatVoiceRunsAtTenTimesTheRateAfterTheFirstUpdateOfAPark()
	{
		// the first update gives the listener its whole place as velocity (300 units straight ahead): x = -300 / 331.36, 1 / (x + 1) > 10, held at 10
		AudioListener.ResetMotion();
		AudioListener.Update( new Vector3( 0, 0, 300 ), Ahead, UpAxis );
		using var mixer = new AudioMixer( null );
		mixer.Play( Ramp(), AudioChannel.Effects, 1f );
		var pcm = mixer.Mix( 3 );
		CollectionAssert.AreEqual( new short[] { 0, 0, 1000, 1000, 2000, 2000 }, pcm, "source frames 0, 10, 20" );
	}

	private static short[] Render( bool doppler, bool loops )
	{
		AudioListener.DopplerEnabled = doppler;
		using var mixer = new AudioMixer( null );
		var voice = mixer.Play( Ramp(), AudioChannel.Effects, 1f, new Vector3( 0, 0, 900 ), 100000 );
		voice.Loops = loops;
		return mixer.Mix( 4 );
	}

	[TestMethod]
	public void APositionedVoiceTakesItsOwnClosingSpeedAndALoopTakesNone()
	{
		MoveBy( new Vector3( 0, 0, 0 ), new Vector3( 0, 0, 400 ) );
		var still = Render( false, false );
		var fast = Render( true, false );
		Assert.IsTrue( still[2] != 0 );
		Assert.AreEqual( 10.0, fast[2] / (double)still[2], 0.2, "x <= -1: ten source frames per output frame" );
		var loop = Render( true, true );
		CollectionAssert.AreEqual( Render( false, true ), loop, "the loop flag disables Doppler for the voice" );
	}

	[TestMethod]
	public void TheStreamFrequencyIsTheSampleFrequencyTimesTheFactorRoundedToAnInteger()
	{
		// [BIN:QMIXER-DLL:0x180055c8 Doppler applied] fild(base) x factor -> single -> fistp
		MoveBy( Vector3.Zero, new Vector3( 0, 0, -331.36f ) );
		var factor = AudioListener.DopplerFactor( null );
		Assert.AreEqual( 0.5f, factor, 1e-3f );
		using var mixer = new AudioMixer( null );
		mixer.Play( Ramp(), AudioChannel.Effects, 1f );
		var pcm = mixer.Mix( 5 );
		var frames = Enumerable.Range( 0, 5 ).Select( index => pcm[2 * index] / 100.0 ).ToArray();
		for ( var frame = 0; frame < 5; frame++ )
			Assert.AreEqual( frame * 0.5, frames[frame], 0.05, $"frame {frame}" );
	}
}
