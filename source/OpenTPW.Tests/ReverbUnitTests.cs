using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// OpenTPW's own stand-in reverb (the audio-reverb extension) that follows the held <c>SndReverb.map</c> record: the record's word order (tp.exe 0x6C46B0), the tail an impulse leaves,
/// and the mixer's send (the positioned effects only). Synthetic; needs no game data.
/// </summary>
[TestClass]
public class ReverbUnitTests
{
	private static uint Bits( float value ) => BitConverter.SingleToUInt32Bits( value );

	/// <summary>The shipped presets in the file's word order: environment, size, diffusion, room, roomHF, decay, decayHF, reflections, reflections delay, reverb, reverb delay, rolloff, air absorption.</summary>
	private static uint[] Record( float decay, float decayHF, int reverb, int room = 0, int reflections = 0, float reflectionsDelay = 0, float size = 7.5f ) =>
		new[] { 0u, Bits( size ), Bits( 0.65f ), (uint)room, 0u, Bits( decay ), Bits( decayHF ), (uint)reflections, Bits( reflectionsDelay ), (uint)reverb, Bits( 0f ), Bits( 0f ), Bits( -5f ) };

	private static float TailEnergy( ReverbSettings settings, int frames )
	{
		var unit = new ReverbUnit( AudioMixer.OutputRate );
		unit.Set( settings );
		var send = new float[frames * 2];
		send[0] = send[1] = 10000f;
		var output = new float[frames * 2];
		unit.Process( send, output, frames );
		return output.Sum( value => value * value );
	}

	[TestMethod]
	public void ARecordIsReadInTheOrderTheAudioDeviceGetsItsFields()
	{
		var settings = ReverbSettings.FromRecord( new uint[] { 3, Bits( 1.5f ), Bits( 0.5f ), unchecked((uint)-500), 0, Bits( 5f ), Bits( 0.1f ), 500, Bits( 0.15f ), 0, Bits( 0.02f ), Bits( 10f ), Bits( -100f ) } )!.Value;
		Assert.AreEqual( 3, settings.Environment );
		Assert.AreEqual( 1.5f, settings.Size );
		Assert.AreEqual( 0.5f, settings.Diffusion );
		Assert.AreEqual( -500, settings.Room );
		Assert.AreEqual( 5f, settings.DecayTime );
		Assert.AreEqual( 0.1f, settings.DecayHFRatio );
		Assert.AreEqual( 500, settings.Reflections );
		Assert.AreEqual( 0.15f, settings.ReflectionsDelay );
		Assert.AreEqual( 0.02f, settings.ReverbDelay );
		Assert.AreEqual( 10f, settings.RoomRolloff );
		Assert.AreEqual( -100f, settings.AirAbsorptionHF );
		Assert.IsNull( ReverbSettings.FromRecord( new uint[12] ), "a record of another length is not one" );
		Assert.IsNull( ReverbSettings.FromRecord( null ) );
	}

	[TestMethod]
	public void WithoutAPresetTheUnitAddsNothing()
	{
		var unit = new ReverbUnit( AudioMixer.OutputRate );
		Assert.IsFalse( unit.Active );
		var send = new float[400];
		send[0] = 5000;
		var output = new float[400];
		unit.Process( send, output, 200 );
		Assert.IsTrue( output.All( value => value == 0 ) );
		unit.Set( ReverbSettings.FromRecord( Record( 4f, 0.3f, 600 ) ) );
		Assert.IsTrue( unit.Active );
		unit.Set( null );
		Assert.IsFalse( unit.Active );
	}

	[TestMethod]
	public void AnImpulseLeavesATailThatLastsLongerForALongerDecayTime()
	{
		var shortDecay = TailEnergy( ReverbSettings.FromRecord( Record( 1f, 1f, 0 ) )!.Value, AudioMixer.OutputRate * 2 );
		var longDecay = TailEnergy( ReverbSettings.FromRecord( Record( 8f, 1f, 0 ) )!.Value, AudioMixer.OutputRate * 2 );
		Assert.IsTrue( shortDecay > 0, "a tail exists" );
		Assert.IsTrue( longDecay > shortDecay * 2, $"decay 8 s keeps more of the tail than 1 s: {longDecay} against {shortDecay}" );
	}

	[TestMethod]
	public void ALowerReverbLevelIsQuieterAndTheHighFrequencyRatioDarkensTheTail()
	{
		var loud = TailEnergy( ReverbSettings.FromRecord( Record( 4f, 1f, 600 ) )!.Value, 8000 );
		var quiet = TailEnergy( ReverbSettings.FromRecord( Record( 4f, 1f, -600 ) )!.Value, 8000 );
		Assert.IsTrue( loud > quiet * 10, $"+600 mB against -600 mB is a factor of 10^(1200/1000) in power: {loud} against {quiet}" );
		var bright = TailEnergy( ReverbSettings.FromRecord( Record( 4f, 1f, 0 ) )!.Value, 8000 );
		var dark = TailEnergy( ReverbSettings.FromRecord( Record( 4f, 0.1f, 0 ) )!.Value, 8000 );
		Assert.IsTrue( dark < bright, "the damping takes energy from the tail" );
	}

	[TestMethod]
	public void EarlyReflectionsAddAnEchoAtTheirDelay()
	{
		var unit = new ReverbUnit( AudioMixer.OutputRate );
		unit.Set( ReverbSettings.FromRecord( Record( 1f, 1f, -10000, reflections: 500, reflectionsDelay: 0.1f ) ) );
		var frames = AudioMixer.OutputRate / 4;
		var send = new float[frames * 2];
		send[0] = send[1] = 10000f;
		var output = new float[frames * 2];
		unit.Process( send, output, frames );
		var echoAt = (int)(0.1f * AudioMixer.OutputRate);
		Assert.IsTrue( Math.Abs( output[echoAt * 2] ) > 100 || Math.Abs( output[echoAt * 2 + 1] ) > 100, "the first reflection arrives at flReflectionsDelay" );
		Assert.AreEqual( 0f, output[10 * 2], "nothing before it" );
	}

	[TestMethod]
	public void TheMixerSendsOnlyPositionedEffectsThroughTheReverb()
	{
		short[] pulse = Enumerable.Repeat( (short)8000, 64 ).ToArray();
		float TailAfterVoice( AudioChannel channel, bool reverb, bool positioned = true )
		{
			var mixer = new AudioMixer( null );
			if ( reverb )
				mixer.Reverb.Set( ReverbSettings.FromRecord( Record( 4f, 1f, 600 ) ) );
			var audio = new Mp2Audio( AudioMixer.OutputRate, 1, pulse, 1, 0 );
			if ( positioned )
				mixer.Play( audio, channel, 1f, new Vector3( 3, 0, 4 ), 100 );
			else
				mixer.Play( audio, channel, 1f );
			mixer.Mix( 64 );
			// the voice has ended; what the next block holds is the reverb tail alone
			var tail = mixer.Mix( 4000 );
			return tail.Sum( value => Math.Abs( (float)value ) );
		}
		Assert.AreEqual( 0f, TailAfterVoice( AudioChannel.Effects, reverb: false ), "dry: silence after the voice" );
		Assert.IsTrue( TailAfterVoice( AudioChannel.Effects, reverb: true ) > 0, "an effects voice leaves a reverb tail" );
		Assert.AreEqual( 0f, TailAfterVoice( AudioChannel.Effects, reverb: true, positioned: false ), "a flat effect (an interface click) stays dry" );
		Assert.AreEqual( 0f, TailAfterVoice( AudioChannel.Speech, reverb: true ), "speech stays dry" );
		Assert.AreEqual( 0f, TailAfterVoice( AudioChannel.Music, reverb: true ), "music stays dry" );
	}

	[TestMethod]
	public void ASustainedSoundGetsAReturnWellBelowItsOwnLevel()
	{
		// the shipped presets: 0 (decay 1 s, level 0), the common 4 s / +600 mB preset and the small room (decay 5 s, reflections at 0.15 s)
		var random = new Random( 5 );
		foreach ( var record in new[] { Record( 1f, 0.3f, 0 ), Record( 4f, 0.3f, 600 ), Record( 5f, 0.1f, 0, room: -500, reflections: 500, reflectionsDelay: 0.15f, size: 1.5f ) } )
		{
			var unit = new ReverbUnit( AudioMixer.OutputRate );
			unit.Set( ReverbSettings.FromRecord( record ) );
			var frames = AudioMixer.OutputRate * 3;
			var send = new float[frames * 2];
			for ( var index = 0; index < send.Length; index++ )
				send[index] = (float)(random.NextDouble() * 2 - 1) * 8000f;
			var output = new float[frames * 2];
			unit.Process( send, output, frames );
			var inRms = MathF.Sqrt( send.Sum( v => v * v ) / send.Length );
			var outRms = MathF.Sqrt( output.Sum( v => v * v ) / output.Length );
			Console.WriteLine( $"reverb return rms {outRms:F0} for input rms {inRms:F0} ({outRms / inRms:P0})" );
			Assert.IsTrue( outRms < inRms, "the return stays below the dry level" );
			Assert.IsTrue( outRms > inRms * 0.02, "and is audible" );
		}
	}
}
