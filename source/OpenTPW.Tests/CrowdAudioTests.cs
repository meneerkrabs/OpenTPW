using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Crowd playback through decoded PCM, without a device or changes to gameplay random streams.</summary>
[TestClass]
[DoNotParallelize]
public class CrowdAudioTests
{
	private readonly Dictionary<FieldInfo, object?> saved = new();
	private BaseFileSystem previousFileSystem = null!;
	private string directory = null!;
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;
	private SoundCategory kids = null!;

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		previousFileSystem = FileSystem;
		foreach ( var field in typeof( GameAudio ).GetFields( BindingFlags.NonPublic | BindingFlags.Static ).Where( field => !field.IsLiteral && !field.IsInitOnly ) )
			saved.Add( field, field.GetValue( null ) );
		directory = Path.Combine( Path.GetTempPath(), $"opentpw-crowd-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( directory, "synthetic", "sound" ) );
		var catalog = AudioTests.CreateCatalog();
		BinaryPrimitives.WriteUInt32LittleEndian( catalog.AsSpan( 72, 4 ), GameAudio.CrowdEvent );
		// the shipped crowd event's code (+0x12) is 7: the code its parameter call carries, and the value the branch picker reads
		BinaryPrimitives.WriteUInt16LittleEndian( catalog.AsSpan( 90, 2 ), GameAudio.CrowdParameterCode );
		// the shipped cat_kids group mask is master + effects (0x10009): the effects volume is one of the groups of the crowd's volume index
		BinaryPrimitives.WriteUInt16LittleEndian( catalog.AsSpan( 36, 2 ), 9 );
		File.WriteAllBytes( Path.Combine( directory, "synthetic", "sound", "cat_kidsSFX.map" ), catalog );
		File.WriteAllBytes( Path.Combine( directory, "synthetic", "sound", "cat_kidsBANK.map" ), new byte[28] );
		FileSystem = new BaseFileSystem( directory );
		kids = SoundCategory.TryLoad( FileSystem, "/synthetic/sound", "cat_kids" )!;
		Assert.IsNotNull( kids );
		var sounds = (IList<SoundEntry>)kids.Catalog.Find( GameAudio.CrowdEvent )!.Sounds;
		for ( var sound = 0; sound < sounds.Count; sound++ )
		{
			var samples = Enumerable.Range( 0, 7 ).Select( index => new SoundSample(
				(uint)(10 + sound * 7 + index), (uint)((index + 1) * 65535 / 7), 0, 1, 0 ) ).ToArray();
			var volume = (byte)(60 + sound * 20);
			sounds[sound] = sounds[sound] with { Samples = samples, MinimumVolume = volume, MaximumVolume = volume };
		}
		UseCategory( kids, cacheSyntheticPcm: true );
	}

	private void UseCategory( SoundCategory category, bool cacheSyntheticPcm = false )
	{
		mixer?.Dispose();
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, seed: 11 );
		SetField( "events", system );
		SetField( "crowd", null );
		SetField( "music", null );
		SetField( "rain", null );
		SetField( "lobbyMusic", null );
		SetField( "lobbyAmbience", null );
		SetField( "placedAmbients", null );
		SetField( "<Kids>k__BackingField", category );
		if ( cacheSyntheticPcm )
		{
			var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem )
				.GetField( "decoded", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( system )!;
			foreach ( var sample in category.Catalog.Find( GameAudio.CrowdEvent )!.Sounds.SelectMany( sound => sound.Samples ) )
				decoded[(category, sample.BankNumber, sample.SampleNumber)] = new Mp2Audio( AudioMixer.OutputRate, 1,
					Enumerable.Repeat( (short)(sample.SampleNumber * 100), 32 ).ToArray(), 1, 0 );
		}
	}

	private static void SetField( string name, object? value ) => typeof( GameAudio )
		.GetField( name, BindingFlags.NonPublic | BindingFlags.Static )!.SetValue( null, value );

	[TestCleanup]
	public void Cleanup()
	{
		GameAudio.LeavePark();
		mixer.Dispose();
		foreach ( var (field, value) in saved )
			field.SetValue( null, value );
		FileSystem = previousFileSystem;
		Directory.Delete( directory, true );
	}

	private AudioVoice WaitForVoice()
	{
		var timeout = Stopwatch.StartNew();
		while ( !mixer.Voices.Any( voice => !voice.IsFinished ) && timeout.Elapsed < TimeSpan.FromSeconds( 30 ) )
		{
			system.Pump();
			Thread.Sleep( 1 );
		}
		Assert.AreEqual( 1, mixer.Voices.Count( voice => !voice.IsFinished ), "exactly one crowd voice is active" );
		return mixer.Voices.Single( voice => !voice.IsFinished );
	}

	/// <summary>
	/// Records <paramref name="count"/> segments in the order they start: each recorded voice is mixed in short chunks until it ends, so a successor that starts
	/// (and could end) inside one long mix is not skipped when its sample was already decoded.
	/// </summary>
	private (int Sound, int Sample)[] ReadSequence( SoundSentence sentence, int count, Action? beforeMix = null )
	{
		var sequence = new (int Sound, int Sample)[count];
		for ( var index = 0; index < count; index++ )
		{
			var voice = WaitForVoice();
			sequence[index] = (sentence.CurrentSound, sentence.CurrentSample);
			beforeMix?.Invoke();
			var audible = false;
			// bounded: the voice's frames at the slowest pitch step (ratio > 1/2) plus slack; an overrun fails instead of hanging
			var limit = (long)voice.Frames * AudioMixer.OutputRate / voice.SampleRate * 2 / 8 + 64;
			for ( var chunk = 0; !voice.IsFinished; chunk++ )
			{
				Assert.IsTrue( chunk < limit, $"segment {index} did not end within {limit} chunks of 8 frames" );
				audible |= mixer.Mix( 8 ).Any( value => value != 0 );
			}
			Assert.IsTrue( audible, "the selected sample reaches decoded PCM" );
		}
		return sequence;
	}

	[TestMethod]
	public void ForgettingACategoryDropsItsSamplesAndLateDecodesDoNotBringThemBack()
	{
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem )
			.GetField( "decoded", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( system )!;
		Assert.IsTrue( decoded.Keys.Any( key => key.Item1 == kids ), "the setup cached the category's samples" );
		system.Forget( kids, null );
		Assert.IsFalse( decoded.Keys.Any( key => key.Item1 == kids ), "a park's categories leave the cache when it ends" );
		var sample = kids.Catalog.Find( GameAudio.CrowdEvent )!.Sounds[0].Samples[0];
		system.RememberDecoded( kids, sample, new Mp2Audio( AudioMixer.OutputRate, 1, new short[32], 1, 0 ), retainLong: false );
		Assert.IsFalse( decoded.Keys.Any( key => key.Item1 == kids ), "a decode that finishes afterwards does not put the category back" );
	}

	[TestMethod]
	public void APlacedSoundDrawsLikePlayAtAndStartsOnceItsSampleIsDecoded()
	{
		var seed = typeof( SoundEventSystem ).GetField( "seed", BindingFlags.NonPublic | BindingFlags.Instance )!;
		var reference = new SoundEventSystem( mixer, seed: 11 );
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem )
			.GetField( "decoded", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( system )!;
		foreach ( var entry in decoded.ToArray() )
			((Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.NonPublic | BindingFlags.Instance )!
				.GetValue( reference )!)[entry.Key] = entry.Value;
		var place = new System.Numerics.Vector3( 1, 2, 3 );
		var played = reference.PlayAt( kids, GameAudio.CrowdEvent, AudioChannel.Effects, place, 40 );
		var placed = system.PlayAtWhenDecoded( kids, GameAudio.CrowdEvent, AudioChannel.Effects, place, 40 );
		Assert.AreEqual( seed.GetValue( reference ), seed.GetValue( system ), "the same draws as PlayAt" );
		Assert.IsNotNull( played );
		Assert.IsNotNull( placed?.Voice, "a decoded sample starts at once" );

		// A sample not decoded yet (this bank holds no sample data) starts nothing until a pump finds the decode done, and a
		// sound stopped before never starts.
		decoded.Clear();
		var waiting = system.PlayAtWhenDecoded( kids, GameAudio.CrowdEvent, AudioChannel.Effects, place, 40 )!;
		var stopped = system.PlayAtWhenDecoded( kids, GameAudio.CrowdEvent, AudioChannel.Effects, place, 40 )!;
		stopped.Stop();
		for ( var attempt = 0; attempt < 200 && waiting.Voice == null; attempt++ )
		{
			system.Pump();
			Thread.Sleep( 5 );
		}
		Assert.IsNull( waiting.Voice, "an undecodable sample starts no voice" );
		Assert.IsNull( stopped.Voice );

		// A decode that finishes later starts the voice at the next pump, with the loop flag; a failed one starts nothing.
		var pending = new System.Threading.Tasks.TaskCompletionSource<Mp2Audio?>();
		var later = new PlacedSound( mixer, AudioChannel.Effects, 0.5f, place, 40, loops: true, pending.Task );
		Assert.IsFalse( later.Pump(), "still decoding" );
		pending.SetResult( new Mp2Audio( AudioMixer.OutputRate, 1, new short[32], 1, 0 ) );
		Assert.IsTrue( later.Pump() );
		Assert.IsNotNull( later.Voice );
		Assert.IsTrue( later.Voice!.Loops );
		var failed = new PlacedSound( mixer, AudioChannel.Effects, 0.5f, place, 40, loops: false,
			System.Threading.Tasks.Task.FromException<Mp2Audio?>( new IOException( "bank unreadable" ) ) );
		Assert.IsTrue( failed.Pump() );
		Assert.IsNull( failed.Voice );
	}

	[TestMethod]
	public void CrowdVariesClipsWithoutAddingVoicesOnEachCountUpdateAndStopsAtZero()
	{
		GameAudio.EnterPark( "missing", seed: 11 );
		GameAudio.SetCrowd( 12 );
		var sentence = GameAudio.Crowd!;
		var sequence = ReadSequence( sentence, 32, () =>
		{
			for ( var update = 0; update < 8; update++ )
				GameAudio.SetCrowd( 12 );
			Assert.AreSame( sentence, GameAudio.Crowd, "count updates reuse the held sentence" );
			Assert.AreEqual( 1, mixer.Voices.Count( voice => !voice.IsFinished ) );
		} );
		Assert.IsTrue( sequence.Select( item => item.Sample ).Distinct().Count() > 2,
			"the shared advancing chooser varies the clips of a seven-clip section" );
		for ( var index = 1; index < sequence.Length; index++ )
			Assert.AreNotEqual( sequence[index - 1], sequence[index], "catalogue anti-repeat still applies" );
		Assert.AreNotEqual( 11u, system.Seed, "[BIN:TPW-EXE:0x6bc6ce] the crowd's sample draws advance the one shared chooser" );
		GameAudio.SetCrowd( 0 );
		Assert.IsNull( GameAudio.Crowd );
		Assert.IsTrue( sentence.IsStopped );
		system.Pump();
		Assert.IsTrue( mixer.Mix( 128 ).All( value => value == 0 ) );
		Assert.AreEqual( 0, mixer.Voices.Count );
	}

	[TestMethod]
	public void AParameterBoundToThePlayingSoundReEvaluatesItsVolumeAndPitchAtOnce()
	{
		// Every crowd sound binds code 7 in slot 1 under the volume mask (as the shipped sounds 1-6 do), 14-22 percent, pitch 0..6.
		var sounds = (IList<SoundEntry>)kids.Catalog.Find( GameAudio.CrowdEvent )!.Sounds;
		for ( var sound = 0; sound < sounds.Count; sound++ )
		{
			var raw = (byte[])sounds[sound].Raw.Clone();
			raw[22] = 7;
			raw[24] = 1;
			sounds[sound] = sounds[sound] with { Raw = raw, MinimumVolume = 14, MaximumVolume = 22, MinimumPitch = 0, MaximumPitch = 6 };
		}
		GameAudio.EnterPark( "missing", seed: 11 );
		GameAudio.SetCrowd( 100 );
		var sentence = GameAudio.Crowd!;
		var voice = WaitForVoice();
		Assert.AreEqual( 0.22f, voice.Gain, 1e-6f, "the segment starts at the parameter's volume" );
		var seed = system.Seed;
		sentence.SetParameter( GameAudio.CrowdParameterCode, 50 );
		Assert.AreEqual( 0.18f, voice.Gain, 1e-6f, "14 + (22 - 14) * 50 / 100 percent, on the playing voice" );
		Assert.AreNotEqual( seed, system.Seed, "the pitch is drawn again from the shared chooser" );
		Assert.IsTrue( voice.PitchStep is >= 0 and < 6 );
		seed = system.Seed;
		sentence.SetParameter( 4, 10 );
		Assert.AreEqual( 0.18f, voice.Gain, 1e-6f, "a code the sound does not bind leaves the playing voice alone" );
		Assert.AreEqual( seed, system.Seed, "and draws nothing" );
	}

	[TestMethod]
	public void AParameterTheSoundDoesNotBindLeavesThePlayingVoiceAlone()
	{
		GameAudio.EnterPark( "missing", seed: 11 );
		GameAudio.SetCrowd( 30 );
		var voice = WaitForVoice();
		var (gain, pitch) = (voice.Gain, voice.PitchStep);
		GameAudio.SetCrowd( 90 );
		Assert.AreEqual( (gain, pitch), (voice.Gain, voice.PitchStep), "selector 0 is not code 7 (the shipped sounds 7-12)" );
		Assert.IsFalse( SoundSentence.BindsSlot( GameAudio.Crowd!.Event.Sounds[GameAudio.Crowd.CurrentSound], GameAudio.CrowdParameterCode ) );
		Assert.AreEqual( 90, GameAudio.Crowd!.Parameter, "the branching parameter still follows the count" );
	}

	[TestMethod]
	public void ALostParkSendsNoCrowdToTheSentence()
	{
		// [BIN:TPW-EXE:0x0054F8BB crowd in a lost park] world state 4 zeroes the count the update sends
		GameAudio.EnterPark( "missing", seed: 11 );
		GameAudio.UpdateCrowd( 1000, () => 12 );
		Assert.IsNotNull( GameAudio.Crowd, "guests near the camera start the sentence" );
		GameAudio.UpdateCrowd( 1000, () => 12, parkLost: true );
		Assert.IsNull( GameAudio.Crowd, "the lost park's count is 0, which stops it" );
	}

	[TestMethod]
	public void TheCrowdUpdatesOnEveryThirtySecondWorldStepOfTheGameTimer()
	{
		var cadence = new WorldStepCadence();
		Assert.AreEqual( 0, cadence.Advance( 31 * 31 ), "31 steps of 31 ms" );
		Assert.AreEqual( 31, cadence.Steps );
		Assert.AreEqual( 1, cadence.Advance( 0.5 ), "the 32nd step runs as soon as the timer is past its time" );
		Assert.AreEqual( 0, cadence.Advance( 0 ), "a paused timer runs no step" );
		cadence.Reset();
		Assert.AreEqual( 2, cadence.Advance( 60_000 ), "a backlog over 2000 ms is dropped: 65 steps" );
		Assert.AreEqual( 65, cadence.Steps );

		GameAudio.EnterPark( "missing", seed: 11 );
		var counted = 0;
		GameAudio.UpdateCrowd( 900, () => { counted++; return 12; } );
		Assert.AreEqual( 0, counted, "no update before the 32nd step" );
		Assert.IsNull( GameAudio.Crowd );
		GameAudio.UpdateCrowd( 100, () => { counted++; return 12; } );
		Assert.AreEqual( 1, counted );
		Assert.IsNotNull( GameAudio.Crowd, "the crowd starts at the first update" );
		GameAudio.EnterPark( "missing", seed: 11 );
		Assert.AreEqual( 0, GameAudio.SoundCadence.Steps, "a park starts counting again" );
	}

	[TestMethod]
	public void ASentenceStartedWithoutACallbackDrawsFromTheSameSharedChooser()
	{
		var sentence = system.StartSentence( kids, GameAudio.CrowdEvent, AudioChannel.Effects )!;
		sentence.Parameter = 12;
		var sequence = ReadSequence( sentence, 32 );
		Assert.IsTrue( sequence.Select( item => item.Sample ).Distinct().Count() > 2 );
		Assert.AreNotEqual( 11u, system.Seed );
		sentence.Stop();
	}

	[TestMethod]
	public void ASeededParkReplaysTheSameCrowdSequenceAndAnotherSeedChangesIt()
	{
		(int Sound, int Sample)[] Run( uint seed )
		{
			GameAudio.EnterPark( "missing", seed );
			GameAudio.SetCrowd( 12 );
			return ReadSequence( GameAudio.Crowd!, 32 );
		}
		var first = Run( 11 );
		CollectionAssert.AreEqual( first, Run( 11 ) );
		CollectionAssert.AreNotEqual( first, Run( 12 ) );
	}

	[TestMethod]
	public void LeavingTheCrowdAndReturningContinuesItsPresentationStream()
	{
		GameAudio.EnterPark( "missing", seed: 11 );
		GameAudio.SetCrowd( 12 );
		ReadSequence( GameAudio.Crowd!, 8 );
		var state = system.Seed;
		Assert.AreNotEqual( 11u, state );
		GameAudio.SetCrowd( 0 );
		Assert.AreEqual( state, system.Seed, "walking away does not consume or reset choices" );
		mixer.Mix( 1 );
		GameAudio.SetCrowd( 12 );
		Assert.AreNotEqual( state, system.Seed, "restart takes its draws from the retained shared state" );
		ReadSequence( GameAudio.Crowd!, 8 );
	}

	[TestMethod]
	public void ASampleCallbackDoesNotChangeBranchingOrCatalogueVolume()
	{
		(int Sound, float Gain)[] Run( Func<uint>? sampleDraw )
		{
			var sentence = system.StartSentence( kids, GameAudio.CrowdEvent, AudioChannel.Effects, sampleDraw )!;
			sentence.Parameter = 20;
			var output = new List<(int, float)>();
			ReadSequence( sentence, 16, () => output.Add( (sentence.CurrentSound, mixer.Voices.Single( voice => !voice.IsFinished ).Gain) ) );
			sentence.Stop();
			mixer.Mix( 1 );
			return output.ToArray();
		}
		var original = Run( null );
		var draw = 0u;
		var varied = Run( () => (draw++ & 1) == 0 ? 0u : 65535u );
		CollectionAssert.AreEqual( original, varied, "only the clip within each selected section changes" );
		Assert.AreEqual( (0, 0.6f), varied[0] );
		Assert.IsTrue( varied.Skip( 1 ).All( item => item == (1, 0.8f) ) );
	}

	[TestMethod]
	public void OriginalCrowdClipsDecodeWithVariationFromTheSharedChooser()
	{
		var data = StringTableTests.OriginalDataDirectory();
		// Keep the fixture's empty level directory: EnterPark exercises crowd seeding without music or ambients masking its PCM.
		var originalKids = SoundCategory.TryLoad( new BaseFileSystem( data ), "/global/sound", "cat_kids" )!;
		Assert.IsNotNull( originalKids );
		UseCategory( originalKids );
		var original = system.StartSentence( originalKids, GameAudio.CrowdEvent, AudioChannel.Effects )!;
		original.Parameter = 12;
		var oldSequence = ReadSequence( original, 32 );
		Assert.IsTrue( oldSequence.Select( item => item.Sample ).Distinct().Count() > 2, "the shipped crowd section varies its clips with the advancing chooser (the frozen Mac reading cycled two)" );
		original.Stop();
		mixer.Mix( 1 );
		GameAudio.EnterPark( "missing", seed: 11 );
		GameAudio.SetCrowd( 12 );
		var changed = ReadSequence( GameAudio.Crowd!, 32 );
		var distinct = changed.Select( item => item.Sample ).Distinct().Count();
		Console.WriteLine( $"Original crowd: direct sentence {oldSequence.Select( item => item.Sample ).Distinct().Count()} clips; park crowd {distinct} clips over 32 decoded segments." );
		Assert.IsTrue( distinct > 2 );
		for ( var index = 1; index < changed.Length; index++ )
			Assert.AreNotEqual( changed[index - 1], changed[index], "adjacent original samples do not repeat" );
		Assert.AreNotEqual( 11u, system.Seed );
	}

	[TestMethod]
	public void OriginalCrowdVoiceVolumeFollowsTheNearbyGuestCount()
	{
		var originalKids = SoundCategory.TryLoad( new BaseFileSystem( StringTableTests.OriginalDataDirectory() ), "/global/sound", "cat_kids" )!;
		Assert.IsNotNull( originalKids );
		UseCategory( originalKids );
		GameAudio.EnterPark( "missing", seed: 11 );
		foreach ( var count in new[] { 1, 12, 100, 1000 } )
		{
			GameAudio.SetCrowd( count );
			var voice = WaitForVoice();
			var sound = originalKids.Catalog.Find( GameAudio.CrowdEvent )!.Sounds[GameAudio.Crowd!.CurrentSound];
			var expected = (sound.MinimumVolume + (sound.MaximumVolume - sound.MinimumVolume) * Math.Min( count, 100 ) / 100) / 100f;
			Assert.AreEqual( expected, voice.Gain, $"{count} nearby guests select the catalogue's interpolated volume" );
			GameAudio.SetCrowd( 0 );
			mixer.Mix( 1 );
		}
		GameAudio.SetCrowd( -1 );
		Assert.IsNull( GameAudio.Crowd );
		Assert.AreEqual( 0, mixer.Voices.Count );
		var sounds = originalKids.Catalog.Find( GameAudio.CrowdEvent )!.Sounds;
		for ( var index = 0; index < sounds.Count; index++ )
		{
			var sound = sounds[index];
			foreach ( var count in new[] { 0, 1, 12, 100 } )
			{
				if ( index < 6 )
				{
					Assert.AreEqual( sound.MinimumVolume + (sound.MaximumVolume - sound.MinimumVolume) * count / 100, system.Volume( sound, 7, count ), $"original section {index}, parameter {count}" );
					continue;
				}
				// no matching parameter: the random fallback, which advances the shared chooser, so compare from one state
				system.Reseed( 11 );
				var fallback = system.Volume( sound );
				system.Reseed( 11 );
				Assert.AreEqual( fallback, system.Volume( sound, 7, count ), $"original section {index}, parameter {count}" );
			}
		}
	}

	private SoundEntry VolumeSound( byte low, byte high, ushort firstCode, ushort firstMask, ushort secondCode = 0, ushort secondMask = 0 )
	{
		var raw = new byte[42];
		raw[12] = low;
		raw[13] = high;
		BinaryPrimitives.WriteUInt16LittleEndian( raw.AsSpan( 22 ), firstCode );
		BinaryPrimitives.WriteUInt16LittleEndian( raw.AsSpan( 24 ), firstMask );
		BinaryPrimitives.WriteUInt16LittleEndian( raw.AsSpan( 26 ), secondCode );
		BinaryPrimitives.WriteUInt16LittleEndian( raw.AsSpan( 28 ), secondMask );
		return kids.Catalog.Find( GameAudio.CrowdEvent )!.Sounds[0] with { MinimumVolume = low, MaximumVolume = high, Raw = raw };
	}

	[TestMethod]
	public void VolumeParametersUseEitherSlotAndTheNativePercentageRange()
	{
		foreach ( var sound in new[] { VolumeSound( 14, 22, 7, 3 ), VolumeSound( 22, 14, 19, 2, 7, 1 ) } )
		{
			foreach ( var (parameter, expected) in new[] { (0, 14), (1, 14), (12, 14), (50, 18), (100, 22), (200, 30), (257, 14) } )
				Assert.AreEqual( expected, system.Volume( sound, 7, parameter ), $"parameter {parameter}" );
		}
		Assert.AreEqual( 18, system.Volume( VolumeSound( 14, 22, 0x107, 1 ), 7, 50 ), "selectors are truncated to bytes when loaded" );
		Assert.AreEqual( 19, system.Volume( VolumeSound( 19, 19, 7, 1 ), 7, 100 ), "equal bounds stay fixed" );
		Assert.AreEqual( 11u, system.Seed );
	}

	[TestMethod]
	public void AVolumeMaskReadsItsSlotWhetherOrNotTheCodeWasSetAndOnlyAnUnboundSoundDraws()
	{
		// tp.exe 0x6BC040 tests only the masks: a sound whose mask +0x18 (or +0x1C) has bit 1 takes slot 1's (or 2's) value byte, 0 until a call with the
		// slot's code arrives, so it plays at its minimum without a draw. (The Mac reading kept a random volume for an unset parameter; the PC is followed.)
		int Fresh( SoundEntry sound, byte? code = null, int parameter = 0 ) { system.Reseed( 11 ); return system.Volume( sound, code, parameter ); }
		foreach ( var sound in new[] { VolumeSound( 14, 22, 19, 1 ), VolumeSound( 14, 22, 19, 1, 7, 1 ), VolumeSound( 14, 22, 7, 1 ) } )
		{
			Assert.AreEqual( 14, Fresh( sound ), "bound, never set: the bottom of the range" );
			Assert.AreEqual( 11u, system.Seed, "and no draw" );
		}
		Assert.AreEqual( 14, Fresh( VolumeSound( 14, 22, 19, 1 ), 7, 100 ), "a code the sound does not bind leaves its slot at 0" );
		Assert.AreEqual( 22, Fresh( VolumeSound( 14, 22, 19, 1, 7, 1 ), 19, 100 ), "the first mask wins: slot 1 under +0x18" );
		Assert.AreEqual( 14, Fresh( VolumeSound( 14, 22, 19, 1, 7, 1 ), 7, 100 ), "slot 2 is read only when the first mask lacks the bit" );
		// a pitch-only mask or no selector at all: the volume is drawn as without a table
		foreach ( var sound in new[] { VolumeSound( 14, 22, 7, 2 ), VolumeSound( 14, 22, 0, 0, 7, 2 ), VolumeSound( 14, 22, 7, 1 ) with { Raw = Array.Empty<byte>() } } )
		{
			system.Reseed( 11 );
			var drawn = system.Volume( sound );
			Assert.AreNotEqual( 11u, system.Seed, "the unbound volume draws" );
			Assert.AreEqual( drawn, Fresh( sound, 7, 100 ) );
		}
	}

	[TestMethod]
	public void CrowdParameterVolumeStillRespectsEffectsVolumeAndMuteInActualPcm()
	{
		var sounds = (IList<SoundEntry>)kids.Catalog.Find( GameAudio.CrowdEvent )!.Sounds;
		sounds[0] = VolumeSound( 0, 100, 7, 1 );
		GameAudio.EnterPark( "missing", seed: 11 );
		GameAudio.SetCrowd( 50 );
		var voice = WaitForVoice();
		Assert.AreEqual( 0.5f, voice.Gain );
		var rawPcm = (short)(sounds[0].Samples[GameAudio.Crowd!.CurrentSample].SampleNumber * 100);
		var options = new GameOptions { SoundEffectsVolume = 100, SoundEffectsOn = true };
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( (short)(rawPcm * AudioLevels.Of( 50 )), mixer.Mix( 1 )[0] );
		options.SoundEffectsVolume = 50;
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( (short)(rawPcm * AudioLevels.Of( 25 )), mixer.Mix( 1 )[0], "the effects group halves the index (50 x 0.5)" );
		options.SoundEffectsOn = false;
		GameAudio.ApplyOptions( mixer, options );
		Assert.IsTrue( mixer.Mix( 1 ).All( value => value == 0 ), "effects mute silences the crowd's actual output" );
		options.SoundEffectsOn = true;
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( (short)(rawPcm * AudioLevels.Of( 25 )), mixer.Mix( 1 )[0], "unmuting restores the stored effects volume" );
	}
}
