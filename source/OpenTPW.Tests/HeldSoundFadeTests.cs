using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic PCM, fake monotonic time and explicit admission/poll/manager order; no original media needed.</summary>
[TestClass]
[DoNotParallelize]
public class HeldSoundFadeTests
{
	private double now;
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;
	private static Mp2Audio Pcm( int frames = 20000 ) => new( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, frames ).ToArray(), 1, 0 );
	private static void Set( object target, string property, object value ) => target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( target, value );

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		now = 0;
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, 7, () => now );
	}

	private HeldSound Place( int volume = 100, uint mask = 0, Task<Mp2Audio?>? decode = null, AudioChannel channel = AudioChannel.Effects )
	{
		var lifetime = new HeldSoundLifetime( mixer, SoundEventPlayer.OneShot, mask );
		var placed = new PlacedSound( mixer, channel, volume / 100f, null, 0, true, decode ?? Task.FromResult<Mp2Audio?>( Pcm() ) ) { Lifetime = lifetime };
		var held = system.HeldSounds.Add( new HeldSound( placed, system.HeldSounds, channel ) );
		placed.Pump();
		return held;
	}

	private void PollAt( double time ) { now = time; system.HeldSounds.Poll(); }
	private void NextPoll() => PollAt( system.HeldSounds.NextPoll );

	[TestMethod]
	public void Q1LoadedConfigurationAndAbsentOptionUseSuppliedDefaults()
	{
		var settings = SoundFadeSettings.Read( new SamSettings( new[] { SamDocument.Parse( "SoundInfo.DEFAULTQUALITY 50\nSoundInfo.FADETOGGLE 30\n", "synthetic.sam" ) } ) );
		Assert.AreEqual( new SoundFadeSettings( 50, 30 ), settings );
		system.HeldSounds.Configure( settings, null );
		Assert.AreEqual( 50, system.HeldSounds.Quality );
		Assert.IsTrue( system.HeldSounds.FadeEnabled );
		var custom = SoundFadeSettings.Read( new SamSettings( new[] { SamDocument.Parse( "SoundInfo.DEFAULTQUALITY 20\nSoundInfo.FADETOGGLE 65\n", "synthetic.sam" ) } ) );
		system.HeldSounds.Configure( custom, null );
		Assert.AreEqual( 20, system.HeldSounds.Quality );
		Assert.IsFalse( system.HeldSounds.FadeEnabled );
	}

	[DataTestMethod]
	[DataRow( 0, false )] [DataRow( 1, false )] [DataRow( 30, false )]
	[DataRow( 31, true )] [DataRow( 50, true )] [DataRow( 100, true )]
	public void Q2Q3StrictQualityThresholdAndPostingDoesNotWriteVolume( int quality, bool fades )
	{
		system.HeldSounds.Configure( new(), quality );
		var held = Place();
		held.Fade();
		Assert.AreEqual( fades, held.Lifetime.Fading );
		Assert.AreEqual( !fades, held.IsFinished );
		Assert.AreEqual( 100, held.Lifetime.OwnVolume );
		Assert.AreEqual( 100, held.Lifetime.DeviceVolume );
	}

	[TestMethod]
	public void Q4CustomThresholdAndNormalizedValues()
	{
		foreach ( var (quality, expected) in new[] { (65, false), (66, true), (-4, false), (101, true) } )
		{
			system.HeldSounds.Configure( new( 50, 65 ), quality );
			var held = Place(); held.Fade();
			Assert.AreEqual( expected, held.Lifetime.Fading );
			Assert.AreEqual( Math.Clamp( quality, 0, 100 ), system.HeldSounds.Quality );
		}
	}

	[TestMethod]
	public void Q5SavedQualityCopyCancelAndLegacyVolumesAreIndependent()
	{
		var path = Path.GetTempFileName();
		try
		{
			File.WriteAllText( path, "{\"SoundQuality\":31,\"MusicVolume\":8}" );
			var options = GameOptions.Load( path );
			Assert.AreEqual( 31, options.SoundQuality );
			Assert.AreEqual( 80, options.MusicVolume );
			var original = options.Clone(); options.SoundQuality = 100; options.CopyFrom( original );
			Assert.AreEqual( 31, options.SoundQuality );
			options.Save( path ); Assert.AreEqual( 31, GameOptions.Load( path ).SoundQuality );
			File.WriteAllText( path, "{}" ); Assert.IsNull( GameOptions.Load( path ).SoundQuality );
			File.WriteAllText( path, "{\"SoundQuality\":-20}" ); Assert.AreEqual( 0, GameOptions.Load( path ).SoundQuality );
			File.WriteAllText( path, "{\"SoundQuality\":150}" ); Assert.AreEqual( 100, GameOptions.Load( path ).SoundQuality );
			options.CopyFrom( new() ); Assert.IsNull( options.SoundQuality );
		}
		finally { File.Delete( path ); }
	}

	[TestMethod]
	public void Q6DowngradeMarksEveryReleasedFadeBeforeManagerCleanup()
	{
		var a = Place(); var b = Place(); var other = Place(); a.Fade(); b.Fade();
		system.HeldSounds.Configure( new(), 30 );
		Assert.IsTrue( a.Lifetime.StopPending && b.Lifetime.StopPending );
		Assert.IsFalse( a.Lifetime.Fading );
		Assert.IsFalse( a.IsFinished );
		system.HeldSounds.ProcessStops();
		Assert.IsTrue( a.IsFinished && b.IsFinished ); Assert.IsFalse( other.IsFinished );
	}

	[TestMethod]
	public void T1T2IdlePhaseAndJoiningFadeShareOnePoll()
	{
		Assert.AreEqual( 250d, system.HeldSounds.NextPoll );
		var a = Place(); now = 10; a.Fade();
		PollAt( 110 ); Assert.AreEqual( 100, a.Lifetime.OwnVolume );
		PollAt( 250 ); Assert.AreEqual( 60, a.Lifetime.OwnVolume ); Assert.AreEqual( 350d, system.HeldSounds.NextPoll );
		var b = Place(); now = 300; b.Fade();
		Assert.AreEqual( 350d, system.HeldSounds.NextPoll );
		PollAt( 350 ); Assert.AreEqual( 36, a.Lifetime.OwnVolume ); Assert.AreEqual( 60, b.Lifetime.OwnVolume );
	}

	[TestMethod]
	public void T3FinalFadeStillChoosesActiveDelayThenIdle()
	{
		var held = Place( 1 ); held.Fade(); NextPoll();
		Assert.IsTrue( held.Lifetime.StopPending ); Assert.IsFalse( held.Lifetime.Fading );
		Assert.AreEqual( 350d, system.HeldSounds.NextPoll );
		system.HeldSounds.ProcessStops(); NextPoll();
		Assert.AreEqual( 600d, system.HeldSounds.NextPoll );
	}

	[TestMethod]
	public void T4HostStallRunsOnlyOneStepAndDelaysFromActualCallback()
	{
		var held = Place(); held.Fade(); PollAt( 10000 );
		Assert.AreEqual( 60, held.Lifetime.OwnVolume ); Assert.AreEqual( 10100d, system.HeldSounds.NextPoll );
		PollAt( 10000 ); Assert.AreEqual( 60, held.Lifetime.OwnVolume );
	}

	[DataTestMethod]
	[DataRow( 100, "60,36,21,12,7,4,2,1,0" )]
	[DataRow( 68, "40,24,14,8,4,2,1,0" )]
	public void A1A2IntegerFeedbackReachesStopPending( int initial, string expected )
	{
		var held = Place( initial ); held.Fade();
		foreach ( var value in expected.Split( ',' ).Select( int.Parse ) )
		{
			NextPoll(); Assert.AreEqual( value, held.Lifetime.OwnVolume ); Assert.AreEqual( value, held.Lifetime.DeviceVolume );
		}
		Assert.IsTrue( held.Lifetime.StopPending ); Assert.IsFalse( held.IsFinished );
		system.HeldSounds.ProcessStops(); Assert.IsTrue( held.IsFinished );
	}

	[TestMethod]
	public void G1G2GroupFeedbackAndReapplyDoNotMultiplyDeviceGainTwice()
	{
		mixer.SetChannelGain( AudioChannel.Effects, 0.5f );
		var held = Place( mask: 9 ); held.Fade(); NextPoll();
		Assert.AreEqual( 30, held.Lifetime.OwnVolume ); Assert.AreEqual( 30, held.Lifetime.DeviceVolume );
		Assert.AreEqual( AudioLevels.Scale( 30, 1000 )[0], mixer.Mix( 1 )[0], "Final device gain (index 30 through the driver table) must bypass channel gain" );
		NextPoll(); Assert.AreEqual( 9, held.Lifetime.OwnVolume );
		held.Stop();
		var changed = Place( mask: 9 ); changed.Fade(); NextPoll();
		mixer.SetChannelGain( AudioChannel.Effects, 1 );
		Assert.AreEqual( 30, changed.Lifetime.OwnVolume ); Assert.AreEqual( 30, changed.Lifetime.DeviceVolume );
		NextPoll(); Assert.AreEqual( 18, changed.Lifetime.OwnVolume );
	}

	[DataTestMethod]
	[DataRow( 1, 30 )] [DataRow( 2, 30 )] [DataRow( 4, 30 )] [DataRow( 8, 30 )]
	[DataRow( 3, 15 )] [DataRow( 5, 15 )] [DataRow( 9, 15 )] [DataRow( 15, 3 )]
	public void G3EveryEnabledMaskBitParticipatesInOrder( int mask, int expected )
	{
		mixer.MasterGain = 0.5f;
		foreach ( var channel in Enum.GetValues<AudioChannel>() ) mixer.SetChannelGain( channel, 0.5f );
		var held = Place( mask: (uint)mask ); held.Fade(); NextPoll();
		Assert.AreEqual( expected, held.Lifetime.OwnVolume );
		var before = held.Lifetime.DeviceVolume;
		if ( (mask & 2) == 0 ) mixer.SetChannelGain( AudioChannel.Music, 0 );
		Assert.AreEqual( before, held.Lifetime.DeviceVolume, "An unused group does not reapply a direct fade write" );
	}

	[TestMethod]
	public void G4ZeroGroupReappliesOnlyDeviceUntilNextFadePoll()
	{
		var held = Place( mask: 9 ); held.Fade(); NextPoll();
		mixer.SetChannelGain( AudioChannel.Effects, 0 );
		Assert.AreEqual( 60, held.Lifetime.OwnVolume ); Assert.AreEqual( 0, held.Lifetime.DeviceVolume );
		NextPoll(); Assert.AreEqual( 0, held.Lifetime.OwnVolume ); Assert.IsTrue( held.Lifetime.StopPending );
	}

	[TestMethod]
	public void G5FractionalTiesUseDocumentedManagedNearestEvenPolicy()
	{
		mixer.SetChannelGain( AudioChannel.Effects, 0.5f );
		var held = Place( 7, 8 ); held.Fade(); NextPoll();
		Assert.AreEqual( 2, held.Lifetime.OwnVolume, "Managed 3.5 -> 4, then integer 4*60/100; this is not an x87 parity assertion" );
	}

	private HeldSound Sentence( ushort flags, uint mask = 0, bool bindParameters = false, bool randomVolume = false )
	{
		var raw = new byte[42];
		if ( bindParameters ) { raw[22] = 20; raw[24] = 3; }
		var sound = new SoundEntry( (byte)(randomVolume ? 60 : 100), 100, 0, (sbyte)(bindParameters ? 20 : 0), 0, 0, 1, 10, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, new[] { new SoundLink( 1, 1, 0, 100 ) }, raw );
		var category = (SoundCategory)RuntimeHelpers.GetUninitializedObject( typeof( SoundCategory ) );
		var catalog = (SoundCatalog)RuntimeHelpers.GetUninitializedObject( typeof( SoundCatalog ) );
		Set( catalog, "Events", new[] { new SoundEvent( 1, 0, flags, 20, new[] { sound } ) { GroupMask = mask } } );
		Set( category, "Catalog", catalog );
		system.RememberDecoded( category, sound.Samples[0], Pcm( 4 ), false );
		return system.PlayHeld( category, 1, AudioChannel.Effects, null )!;
	}

	[DataTestMethod]
	[DataRow( 0x0, false )] [DataRow( 0x4, true )] [DataRow( 0x404, true )]
	[DataRow( 0x6, false )] [DataRow( 0x106, false )] [DataRow( 0x14, false )] [DataRow( 0x406, false )]
	public void S1S2S4AllSevenPlayersAndLinearPriorityUseCorrectSampleOwnership( int flags, bool container )
	{
		var held = Sentence( (ushort)flags ); system.Pump();
		Assert.AreEqual( container, held.Lifetime.IsContainer );
		held.Fade(); Assert.IsFalse( held.Lifetime.StopPending );
		NextPoll(); Assert.AreEqual( container, held.Lifetime.StopPending );
		if ( container )
		{
			Assert.IsTrue( held.Sentence!.IsPlaying, "Posting and scan do not recursively fade children" );
			system.HeldSounds.ProcessStops(); Assert.IsTrue( held.IsFinished );
			Assert.IsTrue( mixer.Voices.All( voice => voice.IsFinished ) );
		}
		else Assert.AreEqual( 60, held.Lifetime.OwnVolume );
	}

	[DataTestMethod]
	[DataRow( 0x6 )] [DataRow( 0x106 )] [DataRow( 0x14 )] [DataRow( 0x406 )]
	public void S3StreamSegmentContinuationRetainsDecayingLogicalVolume( int flags )
	{
		var held = Sentence( (ushort)flags ); system.Pump(); var old = held.Sentence!.Voice;
		held.Fade(); NextPoll(); mixer.Mix( 5 );
		Assert.AreNotSame( old, held.Sentence.Voice );
		Assert.AreEqual( 60, held.Lifetime.OwnVolume ); Assert.AreEqual( 0.6f, held.Sentence.Voice!.Gain );
		NextPoll(); Assert.AreEqual( 36, held.Lifetime.OwnVolume );
	}

	[TestMethod]
	public void A3BoundReevaluationKeepsFadeVolumeAndStillUpdatesPitch()
	{
		foreach ( var flags in new ushort[] { 0, 6 } )
		{
			var held = Sentence( flags, bindParameters: true ); system.Pump(); held.Fade(); NextPoll();
			held.SetParameter( 20, 100 );
			Assert.AreEqual( 60, held.Lifetime.OwnVolume );
			Assert.AreEqual( 20, (held.Sentence?.Voice ?? held.Placed!.Voice)!.PitchStep );
			held.Stop();
		}
	}

	[TestMethod]
	public void P1ReadyAdmissionBeforePollStartsThenDecays()
	{
		var ready = new TaskCompletionSource<Mp2Audio?>(); var held = Place( decode: ready.Task ); held.Fade();
		Assert.IsFalse( held.IsFinished ); Assert.IsFalse( held.Lifetime.HasSample );
		ready.SetResult( Pcm() ); held.Placed!.Pump();
		Assert.AreEqual( 100, held.Lifetime.OwnVolume );
		NextPoll(); Assert.AreEqual( 60, held.Lifetime.OwnVolume );
	}

	[TestMethod]
	public void P2PollBeforeAdmissionCancelsLateReadyOutput()
	{
		var ready = new TaskCompletionSource<Mp2Audio?>(); var held = Place( decode: ready.Task ); held.Fade(); NextPoll();
		Assert.IsTrue( held.Lifetime.StopPending );
		ready.SetResult( Pcm() ); held.Placed!.Pump(); Assert.AreEqual( 0, mixer.Voices.Count );
		system.HeldSounds.ProcessStops(); Assert.IsTrue( held.IsFinished ); held.Placed.Pump(); Assert.AreEqual( 0, mixer.Voices.Count );
	}

	[TestMethod]
	public void P3DecodeGapKeepsLogicalStreamAndLateContinuationUsesLatestDecay()
	{
		var held = Sentence( 6 ); system.Pump(); held.Fade(); NextPoll();
		var ready = new TaskCompletionSource<(int Sound, int Sample, Mp2Audio? Audio)>();
		typeof( SoundSentence ).GetField( "next", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( held.Sentence, ready.Task );
		mixer.Mix( 4 ); Assert.IsNull( held.Sentence!.Voice ); Assert.IsTrue( held.Lifetime.HasSample );
		NextPoll(); Assert.AreEqual( 36, held.Lifetime.OwnVolume ); Assert.IsFalse( held.Lifetime.StopPending );
		ready.SetResult( (0, 0, Pcm()) ); system.Pump(); Assert.AreEqual( 0.36f, held.Sentence.Voice!.Gain );
	}

	[TestMethod]
	public void L1StopAllAndDisposeReachReleasedAndPendingTails()
	{
		foreach ( var dispose in new[] { false, true } )
		{
			Setup(); var ready = new TaskCompletionSource<Mp2Audio?>();
			var playing = Place(); var pending = Place( decode: ready.Task ); playing.Fade(); pending.Fade();
			if ( dispose ) mixer.Dispose(); else mixer.StopAll();
			ready.SetResult( Pcm() ); pending.Placed!.Pump();
			Assert.IsTrue( playing.IsFinished && pending.IsFinished );
			Assert.IsTrue( mixer.Voices.All( voice => voice.IsFinished ) );
			Assert.AreEqual( 0, system.HeldSounds.Sounds.Count );
		}
	}

	[TestMethod]
	public void L1ChannelStopDoesNotCancelUnrelatedChannel()
	{
		var effect = Place(); var music = Place( channel: AudioChannel.Music ); effect.Fade(); music.Fade();
		mixer.StopAll( AudioChannel.Effects ); Assert.IsTrue( effect.IsFinished ); Assert.IsFalse( music.IsFinished );
	}

	[TestMethod]
	public void L1StaleCompletionCannotReplaceOrResurrectCurrentVoice()
	{
		var held = Sentence( 6 ); system.Pump(); var first = held.Sentence!.Voice!;
		mixer.Mix( 5 ); var current = held.Sentence.Voice!; first.RaiseCompleted();
		Assert.AreSame( current, held.Sentence.Voice );
		held.Fade(); mixer.StopAll(); current.RaiseCompleted(); system.Pump();
		Assert.IsTrue( held.IsFinished ); Assert.IsFalse( held.Sentence.IsPlaying );
	}

	[DataTestMethod]
	[DataRow( true )] [DataRow( false )]
	public void L2MissingReplyOrSuspendedPlaceholderStopsAtPoll( bool suspended )
	{
		var held = Place(); held.Fade();
		if ( suspended ) held.Lifetime.Suspended = true; else held.Lifetime.ReplyAvailable = false;
		NextPoll(); Assert.IsTrue( held.Lifetime.StopPending ); system.HeldSounds.ProcessStops(); Assert.IsTrue( held.IsFinished );
	}

	[TestMethod]
	public void L2FailedCancelledAndNullDecodeAreTerminal()
	{
		foreach ( var decode in new[] { Task.FromException<Mp2Audio?>( new IOException( "synthetic" ) ), Task.FromCanceled<Mp2Audio?>( new System.Threading.CancellationToken( true ) ), Task.FromResult<Mp2Audio?>( null ) } )
		{
			var held = Place( decode: decode ); held.Fade(); NextPoll(); system.HeldSounds.ProcessStops();
			Assert.IsTrue( held.IsFinished ); Assert.AreEqual( 0, mixer.Voices.Count );
		}
	}

	[TestMethod]
	public void L2NaturalOneShotEndIsPrunedAndNeverFadedAgain()
	{
		var held = Sentence( 0 ); mixer.Mix( 4 ); held.Fade(); system.HeldSounds.ProcessStops();
		Assert.IsTrue( held.IsFinished ); Assert.AreEqual( 0, system.HeldSounds.Sounds.Count );
	}

	[TestMethod]
	public void R1KillIsImmediateAtEveryQualityAndRepeatedEndIsBenign()
	{
		foreach ( var quality in new[] { 0, 100 } )
		{
			system.HeldSounds.Configure( new(), quality ); var held = Place(); held.Stop(); held.Stop(); held.Fade();
			Assert.IsTrue( held.IsFinished ); Assert.IsFalse( held.Lifetime.Fading );
		}
	}
	[TestMethod]
	public void StreamFadeContinuationDoesNotDrawAbsoluteVolumeAgain()
	{
		var held = Sentence( 6, randomVolume: true ); system.Pump(); held.Fade(); NextPoll();
		var seed = system.Seed; var volume = held.Lifetime.OwnVolume;
		mixer.Mix( 5 );
		Assert.AreEqual( seed, system.Seed, "Single sample and fixed pitch/delay draw nothing; fading continuation must not redraw volume" );
		Assert.AreEqual( volume, held.Lifetime.OwnVolume );
	}

	[TestMethod]
	public void ExplicitRangeStopLosesReplyButOrdinaryDecodeGapDoesNot()
	{
		var held = Sentence( 6 ); held.MoveTo( Vector3.Zero );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		system.Pump(); held.Fade();
		held.MoveTo( new Vector3( 100, 0, 100 ) ); system.Pump();
		Assert.IsTrue( held.Lifetime.HasSample, "Native placeholder identity is not asserted cleared by range stop" );
		Assert.IsFalse( held.Lifetime.ReplyAvailable ); Assert.IsNull( held.Sentence!.Voice );
		NextPoll(); Assert.IsTrue( held.Lifetime.StopPending );
		held.MoveTo( Vector3.Zero ); system.Pump(); Assert.IsTrue( held.IsFinished );
	}

	[TestMethod]
	public void SpeechStartAndEndReapplyDeviceWithoutRewritingOwnVolume()
	{
		mixer.DuckingLevel = 0.5f;
		var held = Place( mask: 9 ); held.Fade(); NextPoll();
		var speech = mixer.Play( Pcm(), AudioChannel.Speech ); mixer.Mix( 1 );
		Assert.AreEqual( 60, held.Lifetime.OwnVolume ); Assert.AreEqual( 30, held.Lifetime.DeviceVolume );
		NextPoll(); Assert.AreEqual( 18, held.Lifetime.OwnVolume ); Assert.AreEqual( 18, held.Lifetime.DeviceVolume );
		speech.Stop(); mixer.Mix( 1 );
		Assert.AreEqual( 18, held.Lifetime.OwnVolume ); Assert.AreEqual( 18, held.Lifetime.DeviceVolume );
		NextPoll(); Assert.AreEqual( 10, held.Lifetime.OwnVolume );
	}

	[TestMethod]
	public void MusicDuckRequestOnlyReappliesEnabledMusicGroup()
	{
		var music = Place( mask: 3, channel: AudioChannel.Music ); var effect = Place( mask: 9 );
		music.Fade(); effect.Fade(); NextPoll(); mixer.MusicDuckRequest = 1;
		Assert.AreEqual( 60, music.Lifetime.OwnVolume ); Assert.AreEqual( 0, music.Lifetime.DeviceVolume );
		Assert.AreEqual( 60, effect.Lifetime.DeviceVolume );
		mixer.MusicDuckRequest = 0;
		Assert.AreEqual( 60, music.Lifetime.DeviceVolume ); NextPoll(); Assert.AreEqual( 36, music.Lifetime.OwnVolume );
		mixer.MusicDuckRequest = 1; NextPoll(); Assert.IsTrue( music.Lifetime.StopPending );
	}

	[TestMethod]
	public void NonFiniteHostGroupCannotPoisonIntegerFeedback()
	{
		var held = Place( mask: 9 ); held.Fade(); mixer.SetChannelGain( AudioChannel.Effects, float.NaN ); NextPoll();
		Assert.AreEqual( 0, held.Lifetime.OwnVolume ); Assert.IsTrue( held.Lifetime.StopPending );
	}

	[TestMethod]
	public void QueuedSubmissionReturnsNoHeldRecord()
	{
		Assert.IsNull( Sentence( 1 ) );
		Assert.AreEqual( 0, system.HeldSounds.Sounds.Count ); Assert.AreEqual( 1, mixer.Voices.Count );
	}

	[DataTestMethod]
	[DataRow( true )] [DataRow( false )]
	public void InitialSentenceReadinessCanPrecedeOrFollowSharedPoll( bool readyFirst )
	{
		var held = Sentence( 6 );
		var ready = new TaskCompletionSource<(int Sound, int Sample, Mp2Audio? Audio)>();
		typeof( SoundSentence ).GetField( "next", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( held.Sentence, ready.Task );
		held.Fade();
		if ( readyFirst )
		{
			ready.SetResult( (0, 0, Pcm()) ); system.Pump(); NextPoll();
			Assert.AreEqual( 60, held.Lifetime.OwnVolume ); Assert.IsTrue( held.Sentence!.IsPlaying );
		}
		else
		{
			NextPoll(); ready.SetResult( (0, 0, Pcm()) ); system.Pump();
			Assert.IsTrue( held.IsFinished ); Assert.AreEqual( 0, mixer.Voices.Count );
		}
	}

	[TestMethod]
	public void FailedSentenceContinuationIsTerminalAndCannotResume()
	{
		var held = Sentence( 6 ); system.Pump(); held.Fade(); NextPoll();
		var failed = Task.FromException<(int Sound, int Sample, Mp2Audio? Audio)>( new IOException( "synthetic" ) );
		typeof( SoundSentence ).GetField( "next", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( held.Sentence, failed );
		mixer.Mix( 4 ); held.Sentence!.Resume(); system.Pump();
		Assert.IsTrue( held.IsFinished ); Assert.AreEqual( 0, mixer.Voices.Count );
	}

}
