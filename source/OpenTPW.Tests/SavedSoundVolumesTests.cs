using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The Sound block's 84 settings bytes (tp.exe 0x803AA0, loader 0x51C400) by name, and the volumes a park loaded from a player's original save plays at
/// (GameAudio.ApplySavedSound): the save's master, effects, music and speech percents and on-flags reach the mixer, the player's options are not changed, and the
/// options take over per channel, on OK and on leaving the park, as the original's setters, options applier 0x423DD0 and park exit do. Synthetic blocks
/// (no original data) plus the corpus (OPENTPW_TPWS_SAVES, OPENTPW_GAME_PATH), whose saves all hold the defaults and so change nothing audible.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SavedSoundVolumesTests
{
	[TestCleanup]
	public void Cleanup() => GameAudio.ResetSavedSound();

	/// <summary>Synthetic 84 settings bytes with the given volumes, on-flags and dirty word; every other word gets a distinct marker.</summary>
	private static byte[] Settings( int master, int effects, int music, int speech, uint effectsOn, uint musicOn, uint speechOn, uint dirty = 1 )
	{
		var bytes = new byte[SaveSoundSettings.Bytes];
		void Word( int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( offset ), value );
		Word( 0x00, 0x00010007 );
		Word( 0x04, 0x00020003 );
		Word( 0x08, 1 );
		Word( 0x0C, 0x00030004 );
		Word( 0x10, 11 );
		Word( 0x14, 3 );
		Word( 0x18, (uint)master );
		Word( 0x1C, (uint)effects );
		Word( 0x20, (uint)music );
		Word( 0x24, (uint)speech );
		Word( 0x28, dirty );
		Word( 0x2C, 0 );
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x30 ), 5 );
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x32 ), 1 );
		Word( 0x38, effectsOn );
		Word( 0x3C, musicOn );
		Word( 0x40, speechOn );
		Word( 0x50, 0xAB );
		return bytes;
	}

	private static SaveSoundData Block( byte[] settings ) => new() { On = true, Word = 0x400, Settings = settings, ChunkWords = new byte[12], Chunk = new byte[8] };

	[TestMethod]
	public void SettingsViewNamesTheTwentyOneWords()
	{
		var view = Block( Settings( 100, 75, 60, 75, 1, 1, 1 ) ).SettingsView;
		Assert.IsNotNull( view );
		Assert.AreEqual( 0x00010007u, view.MusicHandle );
		Assert.AreEqual( 0x00020003u, view.CrowdHandle );
		Assert.AreEqual( 1u, view.CrowdAllowed );
		Assert.AreEqual( 0x00030004u, view.RainHandle );
		Assert.AreEqual( 11, view.CrowdLevel );
		Assert.AreEqual( 3, view.ReverbPreset );
		Assert.AreEqual( (100, 75, 60, 75), (view.MasterVolume, view.EffectsVolume, view.MusicVolume, view.SpeechVolume) );
		Assert.AreEqual( 1u, view.Dirty );
		Assert.AreEqual( 0u, view.SpeechPlaying );
		Assert.AreEqual( (ushort)5, view.MusicDuck, "the low half of +0x30 is the DIPMUSIC request" );
		Assert.AreEqual( (ushort)1, view.ListenerFreeze, "the high half of +0x30 is the listener freeze" );
		Assert.AreEqual( 0u, view.Unused34 );
		Assert.AreEqual( (1u, 1u, 1u), (view.EffectsOn, view.MusicOn, view.SpeechOn) );
		CollectionAssert.AreEqual( new uint[] { 0, 0, 0, 0xAB }, view.Tail.ToArray() );
	}

	[TestMethod]
	public void SettingsOtherThanEightyFourBytesHaveNoView()
	{
		Assert.IsNull( Block( new byte[83] ).SettingsView );
		Assert.IsNull( Block( new byte[88] ).SettingsView );
		Assert.IsNull( new SaveSoundData().SettingsView, "a block without settings (a synthetic payload) has none" );
	}

	[TestMethod]
	public void TheSoundBlockReadsBackFromAWrittenPayload()
	{
		var model = OriginalSavePayloadFixtures.Blank( OriginalSaveImportTests.Settings() );
		model.Sound = Block( Settings( 50, 40, 20, 90, 1, 0, 1 ) );
		var written = model.Write();
		var sound = OriginalSavePayload.TryReadSound( written, SavePayloadLayout.Parse( written ) );
		Assert.IsNotNull( sound?.SettingsView );
		Assert.AreEqual( 20, sound.SettingsView.MusicVolume );
		Assert.AreEqual( 0u, sound.SettingsView.MusicOn );
	}

	[TestMethod]
	public void ASavesNonDefaultVolumesReachTheMixerAndTheOptionsStay()
	{
		using var mixer = new AudioMixer( null );
		var options = new GameOptions();
		var before = System.Text.Json.JsonSerializer.Serialize( options );
		var line = GameAudio.ApplySavedSound( Block( Settings( 50, 40, 20, 90, 1, 0, 1 ) ), options );
		StringAssert.Contains( line, "master 50, effects 40 on, music 20 off, speech 90 on" );
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0.4f, mixer.GetChannelGain( AudioChannel.Effects ), 1e-6f );
		Assert.AreEqual( 0f, mixer.GetChannelGain( AudioChannel.Music ), "the saved music on-flag is off" );
		Assert.AreEqual( 0.9f, mixer.GetChannelGain( AudioChannel.Speech ), 1e-6f );
		Assert.AreEqual( 0.5f, mixer.MasterGain, 1e-6f );
		Assert.AreEqual( before, System.Text.Json.JsonSerializer.Serialize( options ), "the player's options (and so opentpw-options.json) are not changed" );
	}

	[TestMethod]
	public void TheOptionsTakeOverPerChannelThenOnOkAndOnLeavingThePark()
	{
		using var mixer = new AudioMixer( null );
		var options = new GameOptions();
		GameAudio.ApplySavedSound( Block( Settings( 50, 40, 20, 90, 1, 1, 1 ) ), options );
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0.2f, mixer.GetChannelGain( AudioChannel.Music ), 1e-6f );
		// a music slider move (0x4A2BF0 calls the music setter only): music follows the option, effects and speech keep the save's values
		options.MusicVolume = 30;
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0.3f, mixer.GetChannelGain( AudioChannel.Music ), 1e-6f );
		options.MusicVolume = 60;
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0.6f, mixer.GetChannelGain( AudioChannel.Music ), 1e-6f, "once ended, the channel follows the options even back at the old value" );
		Assert.AreEqual( 0.4f, mixer.GetChannelGain( AudioChannel.Effects ), 1e-6f );
		Assert.AreEqual( 0.9f, mixer.GetChannelGain( AudioChannel.Speech ), 1e-6f );
		// OK on the options screen (0x4237F0 -> 0x423DD0): every channel follows the options; the master stays (0x423DD0 never sets it)
		GameAudio.EndSavedVolumes();
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0.75f, mixer.GetChannelGain( AudioChannel.Effects ), 1e-6f );
		Assert.AreEqual( 0.75f, mixer.GetChannelGain( AudioChannel.Speech ), 1e-6f );
		Assert.AreEqual( 0.5f, mixer.MasterGain, 1e-6f );
		// leaving the park writes the profile, whose serializer ends in 0x423DD0
		GameAudio.ApplySavedSound( Block( Settings( 100, 10, 10, 10, 1, 1, 1 ) ), options );
		GameAudio.LeavePark();
		Assert.IsNull( GameAudio.SavedVolumes );
	}

	[TestMethod]
	public void ADirtyWordOfZeroStillPlaysTheSavesValues()
	{
		// park entry ends with 0x51E6F0(0), which sets the dirty word after the load (0x54F3AE), so the saved word does not matter
		using var mixer = new AudioMixer( null );
		var options = new GameOptions();
		var line = GameAudio.ApplySavedSound( Block( Settings( 50, 10, 20, 30, 1, 1, 1, dirty: 0 ) ), options );
		StringAssert.Contains( line, "master 50, effects 10 on, music 20 on, speech 30 on" );
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0.2f, mixer.GetChannelGain( AudioChannel.Music ), 1e-6f );
		Assert.AreEqual( 0.5f, mixer.MasterGain, 1e-6f );
		GameAudio.ResetSavedSound();
		StringAssert.Contains( GameAudio.ApplySavedSound( Block( new byte[83] ), options ), "no 84-byte settings" );
		Assert.IsNull( GameAudio.SavedVolumes );
	}

	[TestMethod]
	public void AChannelTheSaveSwitchedOffStaysSilentUntilOk()
	{
		// the slider and the toggle call only the volume setter (0x4A2E90); only the applier 0x423DD0 sets the on-flags
		using var mixer = new AudioMixer( null );
		var options = new GameOptions();
		GameAudio.ApplySavedSound( Block( Settings( 100, 40, 20, 90, 1, 0, 1 ) ), options );
		options.MusicVolume = 30;
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0f, mixer.GetChannelGain( AudioChannel.Music ), "the saved music on-flag still keeps music off" );
		options.MusicOn = false;
		GameAudio.ApplyOptions( mixer, options );
		options.MusicOn = true;
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0f, mixer.GetChannelGain( AudioChannel.Music ), "a toggle does not set the on-flag either" );
		Assert.AreEqual( 0.4f, mixer.GetChannelGain( AudioChannel.Effects ), 1e-6f );
		GameAudio.EndSavedVolumes();
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0.3f, mixer.GetChannelGain( AudioChannel.Music ), 1e-6f, "OK applies the option's on-flag and volume" );
	}

	[TestMethod]
	public void EveryCorpusSaveHoldsTheDefaultsAndChangesNothingAudible()
	{
		var files = OriginalSaveWriterCorpusTests.CorpusFiles().Select( file => (file.Name, file.Path) ).ToList();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( !string.IsNullOrWhiteSpace( gamePath ) && Directory.Exists( gamePath ) )
		{
			var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
			var easymode = Path.Combine( dataPath, "levels", "jungle", "Easymode.TPWI" );
			if ( File.Exists( easymode ) )
				files.Add( ("jungle/Easymode.TPWI", easymode) );
		}
		foreach ( var (name, path) in files )
		{
			using var reader = new SaveReader( new MemoryStream( File.ReadAllBytes( path ) ) );
			var payload = reader.ReadFile();
			var sound = OriginalSavePayload.TryReadSound( payload, SavePayloadLayout.Parse( payload ) );
			var view = sound?.SettingsView;
			Assert.IsNotNull( view, $"{name}: 84 settings bytes" );
			Assert.IsTrue( sound!.On, $"{name}: sound initialised" );
			Assert.AreEqual( 0x400u, sound.Word, $"{name}: sound enabled word" );
			Assert.AreEqual( (100, 75, 60, 75), (view.MasterVolume, view.EffectsVolume, view.MusicVolume, view.SpeechVolume), $"{name}: volumes" );
			Assert.AreEqual( (1u, 1u, 1u, 1u), (view.EffectsOn, view.MusicOn, view.SpeechOn, view.Dirty), $"{name}: on-flags and dirty" );
			Assert.AreEqual( (ushort)0, view.MusicDuck, $"{name}: duck" );
			Assert.AreEqual( 0u, view.Unused34, $"{name}: +0x34" );
			Assert.IsTrue( view.Tail.All( word => word == 0 ), $"{name}: +0x44..+0x50" );

			var options = new GameOptions();
			using var plain = new AudioMixer( null );
			GameAudio.ResetSavedSound();
			GameAudio.ApplyOptions( plain, options );
			using var loaded = new AudioMixer( null );
			var line = GameAudio.ApplySavedSound( sound, options );
			StringAssert.Contains( line, "equal to the options", name );
			GameAudio.ApplyOptions( loaded, options );
			foreach ( var channel in new[] { AudioChannel.Effects, AudioChannel.Music, AudioChannel.Speech } )
				Assert.AreEqual( plain.GetChannelGain( channel ), loaded.GetChannelGain( channel ), $"{name}: {channel}" );
			Assert.AreEqual( plain.MasterGain, loaded.MasterGain, $"{name}: master" );
			Assert.AreEqual( System.Text.Json.JsonSerializer.Serialize( new GameOptions() ), System.Text.Json.JsonSerializer.Serialize( options ), $"{name}: options unchanged" );
		}
	}
}
