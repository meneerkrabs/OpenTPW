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

	private (int Sound, int Sample)[] ReadSequence( SoundSentence sentence, int count, Action? beforeMix = null )
	{
		var sequence = new (int Sound, int Sample)[count];
		for ( var index = 0; index < count; index++ )
		{
			var voice = WaitForVoice();
			sequence[index] = (sentence.CurrentSound, sentence.CurrentSample);
			beforeMix?.Invoke();
			var frames = (int)Math.Ceiling( voice.Frames * (double)AudioMixer.OutputRate / voice.SampleRate );
			Assert.IsTrue( mixer.Mix( frames ).Any( value => value != 0 ), "the selected sample reaches decoded PCM" );
		}
		return sequence;
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
			"the frozen global draw previously alternated only two clips from a seven-clip section" );
		for ( var index = 1; index < sequence.Length; index++ )
			Assert.AreNotEqual( sequence[index - 1], sequence[index], "catalogue anti-repeat still applies" );
		Assert.AreEqual( 11u, system.Seed, "presentation sample selection leaves the traced global chooser frozen" );
		GameAudio.SetCrowd( 0 );
		Assert.IsNull( GameAudio.Crowd );
		Assert.IsTrue( sentence.IsStopped );
		system.Pump();
		Assert.IsTrue( mixer.Mix( 128 ).All( value => value == 0 ) );
		Assert.AreEqual( 0, mixer.Voices.Count );
	}

	[TestMethod]
	public void TheUnchangedGlobalSentenceChooserStillAlternatesOnlyTwoClips()
	{
		var sentence = system.StartSentence( kids, GameAudio.CrowdEvent, AudioChannel.Effects )!;
		sentence.Parameter = 12;
		var sequence = ReadSequence( sentence, 32 );
		Assert.AreEqual( 2, sequence.Select( item => item.Sample ).Distinct().Count() );
		Assert.AreEqual( 11u, system.Seed );
		sentence.Stop();
	}

	[TestMethod]
	public void ASeededParkReplaysTheSameCrowdSequenceAndAnotherSeedChangesIt()
	{
		(int Sound, int Sample)[] Run( uint seed )
		{
			GameAudio.EnterPark( "missing", seed );
			GameAudio.SetCrowd( 12 );
			var sequence = ReadSequence( GameAudio.Crowd!, 32 );
			Assert.AreEqual( seed, system.Seed );
			return sequence;
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
		var random = (DeterministicRandom)typeof( GameAudio ).GetField( "crowdRandom", BindingFlags.NonPublic | BindingFlags.Static )!.GetValue( null )!;
		var state = random.State;
		GameAudio.SetCrowd( 0 );
		Assert.AreEqual( state, random.State, "walking away does not consume or reset choices" );
		mixer.Mix( 1 );
		GameAudio.SetCrowd( 12 );
		Assert.AreSame( random, typeof( GameAudio ).GetField( "crowdRandom", BindingFlags.NonPublic | BindingFlags.Static )!.GetValue( null ) );
		var reference = new DeterministicRandom( state );
		reference.Next( 1 << 16 );
		Assert.AreEqual( reference.State, random.State, "restart takes the next sample draw from the retained state" );
		ReadSequence( GameAudio.Crowd!, 8 );
		Assert.AreEqual( 11u, system.Seed );
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
		Assert.AreEqual( 11u, system.Seed );
	}

	[TestMethod]
	public void OriginalCrowdClipsDecodeWithMoreVariationThanTheFrozenChooser()
	{
		var data = StringTableTests.OriginalDataDirectory();
		// Keep the fixture's empty level directory: EnterPark exercises crowd seeding without music or ambients masking its PCM.
		var originalKids = SoundCategory.TryLoad( new BaseFileSystem( data ), "/global/sound", "cat_kids" )!;
		Assert.IsNotNull( originalKids );
		UseCategory( originalKids );
		var original = system.StartSentence( originalKids, GameAudio.CrowdEvent, AudioChannel.Effects )!;
		original.Parameter = 12;
		var oldSequence = ReadSequence( original, 32 );
		Assert.AreEqual( 2, oldSequence.Select( item => item.Sample ).Distinct().Count(), "the shipped crowd section reproduces the two-clip cycle" );
		original.Stop();
		mixer.Mix( 1 );
		GameAudio.EnterPark( "missing", seed: 11 );
		GameAudio.SetCrowd( 12 );
		var changed = ReadSequence( GameAudio.Crowd!, 32 );
		var distinct = changed.Select( item => item.Sample ).Distinct().Count();
		Console.WriteLine( $"Original crowd: frozen chooser {oldSequence.Select( item => item.Sample ).Distinct().Count()} clips; host crowd stream {distinct} clips over 32 decoded segments." );
		Assert.IsTrue( distinct > 2 );
		for ( var index = 1; index < changed.Length; index++ )
			Assert.AreNotEqual( changed[index - 1], changed[index], "adjacent original samples do not repeat" );
		Assert.AreEqual( 11u, system.Seed );
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
				var expected = index < 6
					? sound.MinimumVolume + (sound.MaximumVolume - sound.MinimumVolume) * count / 100
					: system.Volume( sound );
				Assert.AreEqual( expected, system.Volume( sound, 7, count ), $"original section {index}, parameter {count}" );
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
	public void UnmatchedVolumeParametersPreserveTheFrozenRandomFallbackAndFirstMaskWins()
	{
		foreach ( var sound in new[] { VolumeSound( 14, 22, 19, 1 ), VolumeSound( 14, 22, 7, 2 ),
			VolumeSound( 14, 22, 19, 1, 7, 1 ), VolumeSound( 14, 22, 0, 0, 7, 2 ) } )
			Assert.AreEqual( system.Volume( sound ), system.Volume( sound, 7, 100 ), "an unrelated selector or pitch-only mask does not change volume" );
		var matching = VolumeSound( 14, 22, 7, 1 );
		Assert.AreEqual( system.Volume( matching ), system.Volume( matching, null, 100 ), "unwired sentence codes preserve existing volume" );
		Assert.AreEqual( system.Volume( matching ), system.Volume( matching with { Raw = Array.Empty<byte>() }, 7, 100 ) );
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
		var options = new GameOptions { SoundEffectsVolume = 10, SoundEffectsOn = true };
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( (short)(rawPcm * 0.5f), mixer.Mix( 1 )[0] );
		options.SoundEffectsVolume = 5;
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( (short)(rawPcm * 0.25f), mixer.Mix( 1 )[0] );
		options.SoundEffectsOn = false;
		GameAudio.ApplyOptions( mixer, options );
		Assert.IsTrue( mixer.Mix( 1 ).All( value => value == 0 ), "effects mute silences the crowd's actual output" );
		options.SoundEffectsOn = true;
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( (short)(rawPcm * 0.25f), mixer.Mix( 1 )[0], "unmuting restores the stored effects volume" );
	}
}
