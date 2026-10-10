using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class AudioTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private sealed class Writer
	{
		public readonly List<byte> Bytes = new();
		public Writer U32( uint value ) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian( b, value ); Bytes.AddRange( b ); return this; }
		public Writer U16( ushort value ) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian( b, value ); Bytes.AddRange( b ); return this; }
		public Writer U8( byte value ) { Bytes.Add( value ); return this; }
		public Writer F32( float value ) { var b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian( b, value ); Bytes.AddRange( b ); return this; }
		public Writer Zero( int count ) { Bytes.AddRange( new byte[count] ); return this; }
	}

	/// <summary>A catalogue with one category: event 31 (one-shot, two weighted samples) and event 2 (branching sentence of two sounds linked by parameter range).</summary>
	internal static byte[] CreateCatalog()
	{
		var w = new Writer();
		w.Zero( 16 ).U32( 0 ).U32( 0 ).U32( 1 );
		w.U32( 2 ).U32( 0 ).U16( 3 ).U16( 1 ).F32( 1 ).F32( 2 ).F32( 0.5f ); // category: 2 events
		w.U32( 31 ).U32( 1 ).U32( 0 ).U32( 5700 ).U16( 0x201 ).U16( 0 ); // one-shot
		w.U32( 2 ).U32( 2 ).U32( 0 ).U32( 10000 ).U16( 0x606 ).U16( 4 ); // branching sentence
		void Sound( ushort samples, uint links, byte minVolume, byte maxVolume, uint weight )
		{
			w.U16( samples ).U16( 0 ).U32( links ).U32( 0 ).U8( minVolume ).U8( maxVolume ).U8( 0 ).U8( 0 ).U16( 0 ).U16( 0 )
				.Zero( 10 ).U32( weight ).F32( 0.1f ).U32( 0 );
		}
		// event 31
		Sound( 2, 0, 50, 80, 0xffff );
		w.U32( 10 ).U32( 30000 ).U32( 91 ).U16( 1 ).U16( 0 );
		w.U32( 11 ).U32( 65535 ).U32( 91 ).U16( 1 ).U16( 0 );
		// event 2: sound 1 and sound 2
		Sound( 1, 2, 100, 100, 0x2aaa );
		Sound( 1, 2, 100, 100, 0x2aaa );
		w.U32( 1 ).U32( 65535 ).U32( 17457 ).U16( 1 ).U16( 0 );
		w.U32( 1 ).U16( 0 ).U8( 0 ).U8( 14 ).U32( 2 ).U16( 0 ).U8( 15 ).U8( 29 );
		w.U32( 2 ).U32( 65535 ).U32( 17457 ).U16( 1 ).U16( 0 );
		w.U32( 1 ).U16( 0 ).U8( 0 ).U8( 14 ).U32( 2 ).U16( 0 ).U8( 15 ).U8( 29 );
		return w.Bytes.ToArray();
	}

	[TestMethod]
	public void ReadsCatalogueEventsSoundsSamplesAndLinks()
	{
		var catalog = new SoundCatalog( CreateCatalog() );
		Assert.AreEqual( 2, catalog.Events.Count );
		var click = catalog.Find( 31 )!;
		Assert.AreEqual( SoundEventPlayer.OneShot, click.Player );
		Assert.AreEqual( (byte)50, click.Sounds[0].MinimumVolume );
		Assert.AreEqual( 2, click.Sounds[0].Samples.Count );
		Assert.AreEqual( (11u, 65535u, (ushort)1), (click.Sounds[0].Samples[1].SampleNumber, click.Sounds[0].Samples[1].CumulativeWeight, click.Sounds[0].Samples[1].BankNumber) );
		var music = catalog.Find( 2 )!;
		Assert.AreEqual( SoundEventPlayer.BranchingSentence, music.Player );
		Assert.AreEqual( (2u, (byte)15, (byte)29), (music.Sounds[0].Links[1].TargetSound, music.Sounds[0].Links[1].MinimumParameter, music.Sounds[0].Links[1].MaximumParameter) );
		var data = CreateCatalog();
		Assert.ThrowsException<InvalidDataException>( () => new SoundCatalog( data[..^1] ) );
		Assert.ThrowsException<InvalidDataException>( () => new SoundCatalog( data.Concat( new byte[] { 0 } ).ToArray() ) );
	}

	[TestMethod]
	public void EventFlagsSelectTheOriginalPlayerTypes()
	{
		SoundEventPlayer Player( ushort flags ) => new SoundEvent( 1, 0, flags, 0, Array.Empty<SoundEntry>() ).Player;
		Assert.AreEqual( SoundEventPlayer.OneShot, Player( 0x201 ) );
		Assert.AreEqual( SoundEventPlayer.BranchingSentence, Player( 0x606 ), "park music" );
		Assert.AreEqual( SoundEventPlayer.Sentence, Player( 0x206 ), "lobby music" );
		Assert.AreEqual( SoundEventPlayer.SentenceShuffle, Player( 0x106 ) );
		Assert.AreEqual( SoundEventPlayer.LinearSentence, Player( 0x14 ) );
		Assert.AreEqual( SoundEventPlayer.OneShotSentence, Player( 0x4 ) );
		Assert.AreEqual( SoundEventPlayer.OneShotBranchingSentence, Player( 0x404 ) );
	}

	[TestMethod]
	public void SamplesAndSoundsAreChosenByCumulativeWeight()
	{
		var samples = new[] { new SoundSample( 1, 100, 0, 1, 0 ), new SoundSample( 2, 200, 0, 1, 0 ), new SoundSample( 3, 65535, 0, 1, 0 ) };
		Assert.AreEqual( 0, SoundEventSystem.ChooseSample( samples, 0, -1, false ) );
		Assert.AreEqual( 0, SoundEventSystem.ChooseSample( samples, 100, -1, false ) );
		Assert.AreEqual( 1, SoundEventSystem.ChooseSample( samples, 101, -1, false ) );
		Assert.AreEqual( 2, SoundEventSystem.ChooseSample( samples, 65535, -1, false ) );
		Assert.AreEqual( 2, SoundEventSystem.ChooseSample( samples, 150, 1, avoidRepeat: true ), "a sentence moves on from the sample it just played" );
		var system = new SoundEventSystem( new AudioMixer( null ), seed: 1 );
		Assert.AreEqual( unchecked(1u * 0x19660D + 0x3C6EF35F) >> 16, system.Draw() );
		Assert.AreEqual( unchecked(1u * 0x19660D + 0x3C6EF35F) >> 16, system.Draw(), "the successor is never stored, so every draw is the same" );
		Assert.AreEqual( 1u, system.Seed );
	}

	[TestMethod]
	public void BranchingFollowsTheLinkWhoseRangeHoldsTheParameter()
	{
		var music = new SoundCatalog( CreateCatalog() ).Find( 2 )!;
		Assert.AreEqual( 0, SoundSentence.NextSound( music, 0, 0, 12345 ) );
		Assert.AreEqual( 0, SoundSentence.NextSound( music, 0, 14, 12345 ) );
		Assert.AreEqual( 1, SoundSentence.NextSound( music, 0, 15, 12345 ) );
		Assert.AreEqual( -1, SoundSentence.NextSound( music, 0, 30, 12345 ), "no link covers 30: the sentence waits" );
		Assert.AreEqual( 14, GameAudio.MusicParameter( 29 ), "guests / 2" );
		Assert.AreEqual( 15, GameAudio.MusicParameter( 30 ) );
		Assert.AreEqual( 89, GameAudio.MusicParameter( 1000 ), "capped at 0x59" );
	}

	private static Mp2Audio Constant( short value, int frames, int rate = AudioMixer.OutputRate, int channels = 1 ) =>
		new( rate, channels, Enumerable.Repeat( value, frames * channels ).ToArray(), 1, 0 );

	[TestMethod]
	public void MixerSumsVoicesWithChannelGainsAndDucksForSpeech()
	{
		var mixer = new AudioMixer( null ) { DuckingLevel = 0.5f };
		mixer.SetChannelGain( AudioChannel.Music, 0.5f );
		mixer.Play( Constant( 1000, 100 ), AudioChannel.Music );
		mixer.Play( Constant( 200, 100 ), AudioChannel.Effects );
		var output = mixer.Mix( 10 );
		Assert.AreEqual( (short)(500 + 200), output[0] );
		Assert.AreEqual( output[0], output[1], "mono goes to both sides" );
		mixer.Play( Constant( 100, 100 ), AudioChannel.Speech );
		output = mixer.Mix( 10 );
		Assert.AreEqual( (short)(250 + 100 + 100), output[0], "music and effects at the ducking level while speech plays" );
		mixer.SetChannelGain( AudioChannel.Speech, 0 );
		Assert.AreEqual( (short)(500 + 200), mixer.Mix( 10 )[0], "muted speech does not duck" );
	}

	[TestMethod]
	public void ACompletedVoiceStartsItsSuccessorOnTheSameFrame()
	{
		var mixer = new AudioMixer( null );
		var first = mixer.Play( Constant( 1000, 30 ), AudioChannel.Music );
		AudioVoice? second = null;
		first.Completed += _ => second = mixer.Play( Constant( 2000, 30 ), AudioChannel.Music );
		var output = mixer.Mix( 50 );
		Assert.AreEqual( (short)1000, output[2 * 29] );
		Assert.AreEqual( (short)2000, output[2 * 30], "no gap between segments" );
		Assert.AreEqual( 30, second!.StartOutputFrame );
		Assert.AreEqual( (short)2000, output[2 * 49] );
		Assert.AreEqual( 0, mixer.Mix( 20 )[2 * 10] );
		Assert.AreEqual( 0, mixer.Voices.Count );
	}

	[TestMethod]
	public void VoicesAreResampledToTheOutputRate()
	{
		var mixer = new AudioMixer( null );
		var voice = mixer.Play( Constant( 1000, 100, rate: AudioMixer.OutputRate * 2 ), AudioChannel.Effects );
		mixer.Mix( 49 );
		Assert.IsFalse( voice.IsFinished );
		mixer.Mix( 2 );
		Assert.IsTrue( voice.IsFinished, "100 frames at twice the rate last 50 output frames" );
	}

	// ---- Original data (OPENTPW_GAME_PATH) ----

	private static IEnumerable<string> Maps( string data, string suffix ) =>
		Directory.EnumerateFiles( data, $"cat_*{suffix}.map", SearchOption.AllDirectories ).OrderBy( path => path, StringComparer.Ordinal );

	[TestMethod]
	public void EveryOriginalSoundCatalogueAndBankMapParses()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var catalogs = Maps( data, "SFX" ).Select( path => (path, new SoundCatalog( File.ReadAllBytes( path ) )) ).ToList();
		Assert.IsTrue( catalogs.Count >= 31, catalogs.Count.ToString() );
		var banks = Maps( data, "BANK" ).Select( path => SoundCatalog.ReadBankNames( File.ReadAllBytes( path ) ) ).ToList();
		Assert.AreEqual( catalogs.Count, banks.Count );
		foreach ( var (path, catalog) in catalogs.Where( entry => entry.path.Replace( '\\', '/' ).Contains( "/Music/" ) ) )
		{
			var music = catalog.Find( GameAudio.ParkMusicEvent );
			Assert.IsNotNull( music, path );
			Assert.AreEqual( SoundEventPlayer.BranchingSentence, music.Player, path );
			for ( var parameter = 0; parameter <= GameAudio.MaximumMusicParameter; parameter++ )
				Assert.IsTrue( SoundSentence.NextSound( music, 0, parameter, 1 ) >= 0, $"{path}: parameter {parameter} has a section" );
		}
		var ui = catalogs.Single( entry => entry.path.Replace( '\\', '/' ).EndsWith( "global/sound/cat_uiSFX.map", StringComparison.OrdinalIgnoreCase ) ).Item2;
		Assert.IsNotNull( ui.Find( HudClickEvent ) );
	}

	private const uint HudClickEvent = 0x1F;

	[TestMethod]
	public void OriginalParkMusicAndClickSoundsDecode()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var fileSystem = new BaseFileSystem( data );
		var music = SoundCategory.TryLoad( fileSystem, "/levels/jungle/Music", "cat_music" );
		var ui = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_ui" );
		Assert.IsNotNull( music );
		Assert.IsNotNull( ui );
		var system = new SoundEventSystem( new AudioMixer( null ), seed: 7 );
		var clock = Stopwatch.StartNew();
		var sentence = system.StartSentence( music, GameAudio.ParkMusicEvent, AudioChannel.Music );
		Assert.IsNotNull( sentence );
		Assert.IsFalse( sentence.IsPlaying, "the first segment decodes in the background" );
		while ( !sentence.IsPlaying && clock.ElapsedMilliseconds < 60000 )
		{
			System.Threading.Thread.Sleep( 10 );
			system.Pump();
		}
		Console.WriteLine( $"First music segment ready after {clock.ElapsedMilliseconds} ms." );
		Assert.IsTrue( sentence.IsPlaying );
		var segment = system.Mixer.Voices.Single();
		Assert.IsTrue( segment.Frames / (double)segment.SampleRate is > 10 and < 30, $"{segment.Frames} frames at {segment.SampleRate} Hz" );
		Assert.IsNotNull( system.Play( ui, HudClickEvent, AudioChannel.Effects ), "the park view click decodes (Layer I)" );
	}
}
