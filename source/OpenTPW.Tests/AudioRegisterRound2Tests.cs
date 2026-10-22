using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The sound behaviours settled by the second static research round over tp.exe (docs/AUDIO.md "Register round 2"): the bank folder
/// flag, the start-up sound, flat loops, placed sentences, the listener, the refused-click place, the weather's second rain voice,
/// the drawer close and the front end's click. Synthetic; the shipped-bank check skips without <c>OPENTPW_GAME_PATH</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AudioRegisterRound2Tests
{
	private readonly Dictionary<FieldInfo, object?> saved = new();
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;
	private BaseFileSystem? previousFileSystem;
	private string? temporary;

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		previousFileSystem = FileSystem;
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, 7 );
		Field( "events", system );
		foreach ( var name in new[] { "Ui", "Ambient", "Rides", "Kids", "Staff", "LevelAmbient", "LevelRides" } )
			Field( $"<{name}>k__BackingField", null );
		Field( "startupPlayed", false );
		foreach ( var name in new[] { "rain", "weatherRain", "placedAmbients", "placedSentences", "rectangleSounds", "crowd", "music" } )
			Field( name, null );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		mixer.Dispose();
		foreach ( var pair in saved )
			pair.Key.SetValue( null, pair.Value );
		saved.Clear();
		FileSystem = previousFileSystem!;
		if ( temporary != null && Directory.Exists( temporary ) )
			Directory.Delete( temporary, true );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	private void Field( string name, object? value )
	{
		var field = typeof( GameAudio ).GetField( name, BindingFlags.Static | BindingFlags.NonPublic )!;
		saved.TryAdd( field, field.GetValue( null ) );
		field.SetValue( null, value );
	}

	private void Use( string property, SoundCategory category ) => Field( $"<{property}>k__BackingField", category );

	/// <summary>A category of the given events; event i plays a decoded mono sample of its frame count from bank i + 1.</summary>
	private SoundCategory Category( params (uint Id, ushort Flags, int Frames)[] events )
	{
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!;
		var list = new List<SoundEvent>();
		for ( var index = 0; index < events.Length; index++ )
		{
			var (id, flags, frames) = events[index];
			var sample = new SoundSample( 1, 65535, 0, (ushort)(index + 1), 0 );
			var sound = new SoundEntry( 100, 100, 0, 0, 0, 0, 1, 100, new[] { sample }, Array.Empty<SoundLink>(), Array.Empty<byte>() );
			// the rain event carries the shipped event code 8 (cat_ambient 0x21, +0x12), which the level voice's parameter call targets
			list.Add( new SoundEvent( id, 0, flags, id == GameAudio.RainEvent ? GameAudio.RainParameterCode : (ushort)0, new[] { sound } ) );
			decoded[(category, index + 1, 1)] = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, frames ).ToArray(), 1, 0 );
		}
		Set( catalog, "Events", list.ToArray() );
		Set( category, "Catalog", catalog );
		return category;
	}

	// ---- AUDIO-002 ----

	private static byte[] BankMap( params (string Name, byte Flags)[] banks )
	{
		var bytes = new List<byte>( new byte[24] );
		bytes.AddRange( BitConverter.GetBytes( (uint)banks.Length ) );
		foreach ( var (_, flags) in banks )
		{
			var record = new byte[11];
			record[9] = flags;
			bytes.AddRange( record );
		}
		foreach ( var (name, _) in banks )
		{
			var text = System.Text.Encoding.Latin1.GetBytes( name );
			bytes.AddRange( BitConverter.GetBytes( (uint)text.Length ) );
			bytes.AddRange( text );
		}
		return bytes.ToArray();
	}

	private static byte[] BankWithEntries( int entries )
	{
		var bytes = new byte[4 + entries * 4];
		BinaryPrimitives.WriteInt32LittleEndian( bytes, entries );
		return bytes;
	}

	[TestMethod]
	public void TheBankMapKeepsEachRecordsGlobalFlag()
	{
		// [BIN:TPW-EXE:0x6bff14] test byte [record + 9], 0x20
		var map = SoundCatalog.ReadBankMap( BankMap( ("Sound\\A", 0x21), ("Sound\\B", 0x02), ("Sound\\C", 0x20 | 0x40) ) );
		CollectionAssert.AreEqual( new[] { true, false, true }, map.Select( bank => bank.Global ).ToArray() );
		CollectionAssert.AreEqual( new[] { "Sound\\A", "Sound\\B", "Sound\\C" }, map.Select( bank => bank.Name ).ToArray() );
		CollectionAssert.AreEqual( new[] { "Sound\\A", "Sound\\B", "Sound\\C" }, SoundCatalog.ReadBankNames( BankMap( ("Sound\\A", 0x21), ("Sound\\B", 0x02), ("Sound\\C", 0x60) ) ).ToArray() );
	}

	[TestMethod]
	public void AFlaggedBankIsUnderGlobalAndAnUnflaggedOneHasNoFallbackThere()
	{
		temporary = Directory.CreateTempSubdirectory( "opentpw-bank-flag-" ).FullName;
		void Write( string path, byte[] bytes )
		{
			var full = Path.Combine( temporary, path );
			Directory.CreateDirectory( Path.GetDirectoryName( full )! );
			File.WriteAllBytes( full, bytes );
		}
		Write( "levels/x/Sound/cat_tSFX.map", AudioTests.CreateCatalog() );
		Write( "levels/x/Sound/cat_tBANK.map", BankMap( ("Sound\\Flagged", 0x20), ("Sound\\Plain", 0x01), ("Sound\\Both", 0x20), ("Sound\\Both2", 0x01) ) );
		Write( "global/Sound/FlaggedHD.sdt", BankWithEntries( 2 ) );   // only under global: found because of the flag
		Write( "global/Sound/PlainHD.sdt", BankWithEntries( 3 ) );     // only under global, no flag: not found (no fallback)
		Write( "global/Sound/BothHD.sdt", BankWithEntries( 4 ) );      // flagged: global's copy wins
		Write( "levels/x/Sound/BothHD.sdt", BankWithEntries( 5 ) );
		Write( "levels/x/Sound/Both2HD.sdt", BankWithEntries( 6 ) );   // unflagged: the category's own folder
		Write( "global/Sound/Both2HD.sdt", BankWithEntries( 7 ) );
		var category = SoundCategory.TryLoad( new BaseFileSystem( temporary ), "/levels/x/Sound", "cat_t" )!;
		Assert.AreEqual( 2, category.GetBank( 1 )?.Count, "flag 0x20: the Global folder" );
		Assert.IsNull( category.GetBank( 2 ), "no flag: only the category's own folder, never a second try under global" );
		Assert.AreEqual( 4, category.GetBank( 3 )?.Count, "flag 0x20 prefers global over the category's own copy" );
		Assert.AreEqual( 6, category.GetBank( 4 )?.Count, "no flag prefers the category's own folder" );
	}

	[TestMethod]
	public void EveryShippedBankResolvesInItsOwnFolderWithoutAnyGlobalFlag()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var fileSystem = new BaseFileSystem( data );
		var maps = Directory.EnumerateFiles( data, "cat_*BANK.map", SearchOption.AllDirectories ).OrderBy( path => path, StringComparer.Ordinal ).ToList();
		Assert.IsTrue( maps.Count >= 31, maps.Count.ToString() );
		foreach ( var path in maps )
		{
			var banks = SoundCatalog.ReadBankMap( File.ReadAllBytes( path ) );
			Assert.IsFalse( banks.Any( bank => bank.Global ), $"{path}: no shipped bank record carries 0x20" );
			var relative = "/" + Path.GetRelativePath( data, path ).Replace( '\\', '/' );
			var directory = relative[..relative.LastIndexOf( '/' )];
			var name = Path.GetFileName( relative )[..^"BANK.map".Length];
			var category = SoundCategory.TryLoad( fileSystem, directory, name );
			Assert.IsNotNull( category, path );
			for ( var number = 1; number <= banks.Count; number++ )
				Assert.IsNotNull( category.GetBank( number ), $"{path}: bank {number} ({banks[number - 1].Name}) resolves under the map folder's parent" );
		}
	}

	// ---- start-up sound ----

	[TestMethod]
	public void TheStartUpSoundIsCatUiEvent0xD2PlayedFlat()
	{
		// [BIN:TPW-EXE:0x54de6a] the start-up init submits cat_ui 210 flat after registering the categories
		Assert.AreEqual( 0xD2u, GameAudio.StartupEvent );
		Use( "Ui", Category( (0xD2, 0x200, 4), (0x1F, 0, 8) ) );
		var voice = GameAudio.PlayStartupSound();
		Assert.IsNotNull( voice );
		Assert.IsNull( voice.Position, "submitted flat" );
		Assert.IsFalse( voice.Loops, "a one-shot with flags 0x200" );
		Assert.AreEqual( 4, voice.Frames, "event 0xD2's sample, not the click's" );
		Assert.AreEqual( 1, mixer.Voices.Count );
		Assert.IsNull( GameAudio.PlayStartupSound(), "once per process: a front end shown again does not replay it" );
		Assert.AreEqual( 1, mixer.Voices.Count );
	}

	[TestMethod]
	public void StartingTheSoundServiceAloneDoesNotPlayTheStartUpSound()
	{
		// a run that enters a park directly (--load-original-level) must not play it over the park music
		Use( "Ui", Category( (0xD2, 0x200, 4) ) );
		Assert.AreEqual( 0, mixer.Voices.Count );
		Assert.IsFalse( (bool)typeof( GameAudio ).GetField( "startupPlayed", BindingFlags.Static | BindingFlags.NonPublic )!.GetValue( null )! );
	}

	// ---- AUDIO-019 ----

	[TestMethod]
	public void AFlatOneShotWithFlagEightLoopsAndOneWithoutDoesNot()
	{
		// [BIN:TPW-EXE:0x6bc621] both the fixed and the positioned branch reach the loop test
		var category = Category( (186, 0x208, 4), (31, 0x200, 4) );
		var looping = system.Play( category, 186, AudioChannel.Effects )!;
		var plain = system.Play( category, 31, AudioChannel.Effects )!;
		Assert.IsNull( looping.Position );
		Assert.IsTrue( looping.Loops, "flat, flag 0x8" );
		Assert.IsFalse( plain.Loops );
		mixer.Mix( 64 );
		Assert.IsFalse( looping.IsFinished );
		Assert.IsTrue( plain.IsFinished );
		looping.Stop();
		Assert.IsTrue( looping.IsFinished );
		var positioned = system.PlayAt( category, 186, AudioChannel.Effects, new Vector3( 1, 0, 1 ) )!;
		Assert.IsTrue( positioned.Loops, "positioned loops as before" );
		positioned.Stop();
	}

	// ---- AUDIO-020 ----

	[TestMethod]
	public void APlacedSentenceStartsAsAPositionedSentenceForTheParksLifetime()
	{
		// [BIN:TPW-EXE:0x550e00] a type 1 record goes to the placed submit 0x51c130(0, category, effect, x/1024, y/1024, z/1024, range*2/1024); the library plays a sentence as a sentence
		temporary = Directory.CreateTempSubdirectory( "opentpw-placed-sentence-" ).FullName;
		Directory.CreateDirectory( Path.Combine( temporary, "levels", "x" ) );
		var words = new[] { new[] { 1, 0, 7, 10 * 1024, 0, 20 * 1024, 51200 }, new[] { 1, 0, 8, 30 * 1024 + 5, 0, -(40 * 1024) - 5, 0 } };
		var data = new byte[12 + words.Length * 60 + 4];
		BinaryPrimitives.WriteUInt32LittleEndian( data, 0x5F4A424F );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 4 ), words.Length );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 8 ), 60 );
		for ( var record = 0; record < words.Length; record++ )
			for ( var word = 0; word < words[record].Length; word++ )
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 12 + record * 60 + word * 4 ), words[record][word] );
		File.WriteAllBytes( Path.Combine( temporary, "levels", "x", "scape.omp" ), data );
		FileSystem = new BaseFileSystem( temporary );
		// event 7 is a sentence (flags 0x6), event 8 a one-shot
		Use( "Ambient", Category( (7, 0x6, 4), (8, 0x1, 4) ) );

		GameAudio.StartPlacedAmbients( "x" );
		system.Pump();
		system.Pump();

		var sentence = ((List<SoundSentence>)typeof( GameAudio ).GetField( "placedSentences", BindingFlags.Static | BindingFlags.NonPublic )!.GetValue( null )!).Single();
		Assert.AreEqual( new Vector3( 10, 0, 20 ), sentence.Position );
		Assert.AreEqual( 100f, sentence.Range, "range * 2 / 1024" );
		var sentenceVoice = mixer.Voices.Single( voice => voice.Position == new Vector3( 10, 0, 20 ) );
		Assert.AreEqual( 100f, sentenceVoice.Range );
		Assert.IsFalse( sentenceVoice.Loops, "the sentence continues by chaining its next segment, not by looping" );
		var oneShot = mixer.Voices.Single( voice => voice.Position != new Vector3( 10, 0, 20 ) );
		Assert.AreEqual( new Vector3( 30, 0, -40 ), oneShot.Position, "every coordinate is divided toward zero (0x550f1c-0x550f4d)" );

		GameAudio.StopPlacedAmbients();
		Assert.IsTrue( sentence.IsStopped, "the park's end stops it" );
		Assert.IsTrue( sentenceVoice.IsFinished );
	}

	// ---- AUDIO-021 ----

	[TestMethod]
	public void TheListenerIsTheEyeInTheEyeCameraModesAndTheEyeTargetMidpointOtherwise()
	{
		// [BIN:TPW-EXE:0x54f915] test 0x16, [0x790ab0]: set -> the camera position, clear -> pos - (pos - target) * 0.5
		var eye = new System.Numerics.Vector3( 10, 20, 30 );
		var target = new System.Numerics.Vector3( 30, 40, 10 );
		Assert.AreEqual( eye, GameAudio.ListenerPlace( eye, target, eyeMode: true ) );
		Assert.AreEqual( new System.Numerics.Vector3( 20, 30, 20 ), GameAudio.ListenerPlace( eye, target, eyeMode: false ) );
	}

	// ---- AUDIO-022 ----

	[TestMethod]
	public void ARefusedPathClickPlaysCatUi175AtTheClickedCell()
	{
		// [BIN:TPW-EXE:0x524a80] cat_ui 0xAF at (x * 10, 0, z * 10)
		Use( "Ui", Category( (175, 0x1, 4) ) );
		Assert.AreEqual( new Vector3( 30, 0, 40 ), GameAudio.CellSoundPosition( 3, 4 ) );
		GameAudio.PlayPathRefused( 3, 4 );
		var voice = mixer.Voices.Single();
		Assert.AreEqual( new Vector3( 30, 0, 40 ), voice.Position );
	}

	[TestMethod]
	public void ARefusedPathClickSoundsAtTheReleaseThatFollowsAParkPress()
	{
		// [BIN:TPW-EXE:0x524a6f] the release plays 0xAF with the refusal latch set, the press latch 0x8186d8 set and no layout replay
		var gate = new PathRefusalClick();
		gate.Press();
		gate.Refused( 3, 4 );
		Assert.AreEqual( (3, 4), gate.Release() );
	}

	[TestMethod]
	public void ARefusedPathClickIsSilentWithoutItsPressOrDuringALayoutReplay()
	{
		// [BIN:TPW-EXE:0x524ae6] every release clears the press latch, so a second release without a press is silent
		var gate = new PathRefusalClick();
		gate.Refused( 3, 4 );
		Assert.IsNull( gate.Release(), "no press reached the park view" );
		gate.Press();
		gate.Refused( 3, 4 );
		Assert.IsNull( gate.Release( layoutReplay: true ), "0x785a2c set: a layout replay runs" );
		Assert.IsNull( gate.Release(), "the release consumed the latch and the refusal" );
		gate.Press();
		Assert.IsNull( gate.Release(), "an accepted click is silent" );
	}

	// ---- AUDIO-015 ----

	[TestMethod]
	public void RainStartsASecondFlatVoiceBesideTheLevelVoiceAndSnowKeepsOnlyTheLevelVoice()
	{
		// [BIN:TPW-EXE:0x5131f0] mode 0 (rain) submits cat_ambient 0x21 flat with no parameter; snow and clear stop it. [BIN:TPW-EXE:0x512a7f] the level voice follows the drops in every weather
		Use( "Ambient", Category( (0x21, 0x606, 4) ) );
		GameAudio.SetRain( 40 );
		GameAudio.SetWeatherRain( true );
		var level = GameAudio.RainVoice;
		var own = GameAudio.WeatherRain;
		Assert.IsNotNull( level );
		Assert.IsNotNull( own );
		Assert.AreNotSame( level, own, "two voices of the one event sound while it rains" );
		Assert.IsNull( own.Position, "flat" );
		Assert.AreEqual( 0, own.Parameter, "no parameter: only the level voice is driven by the drop count" );
		Assert.AreEqual( 40, level.Parameter );
		GameAudio.SetWeatherRain( true );
		Assert.AreSame( own, GameAudio.WeatherRain, "a second rain mode does not start a third voice" );

		// snow: the weather mode leaves rain, the drops (and so the level voice) carry on
		GameAudio.SetWeatherRain( false );
		Assert.IsNull( GameAudio.WeatherRain );
		Assert.IsTrue( own.IsStopped );
		Assert.AreSame( level, GameAudio.RainVoice );
		Assert.IsFalse( level.IsStopped );
		GameAudio.SetRain( 20 );
		Assert.AreSame( level, GameAudio.RainVoice );
		Assert.AreEqual( 20, level.Parameter, "the level keeps following the drops in snow" );
		GameAudio.SetRain( 0 );
		Assert.IsNull( GameAudio.RainVoice );
		Assert.IsTrue( level.IsStopped );
	}

	[TestMethod]
	public void TheLevelRainLevelContinuesWhileSnowFallsAndTheWeatherIsNotRaining()
	{
		var snowy = new ParkWeather( new SamSettings( new[] { SamDocument.Parse( "WeatherEffects.QualityForSnowLow\t0\nWeatherEffects.QualityForSnowHigh\t100\nWeatherEffects.QualityForRainLow\t-1\nWeatherEffects.QualityForRainHigh\t-1\nWeatherEffects.MaxRaindrops\t600\nWeather.SpeedOfChange\t20\n", "test" ) } ), 3, 10, 10 );
		snowy.AdvanceTo( 0 );
		snowy.ApplyQuality( 1 );
		for ( var step = 0; step < 10; step++ )
			snowy.Step( 0 );
		Assert.IsTrue( snowy.Snow );
		Assert.IsFalse( snowy.Rain, "the weather mode is snow: the thing's own voice is stopped" );
		Assert.IsTrue( snowy.RainLevel > 0, "but the drop-driven level voice carries on" );
	}

	// ---- AUDIO-007 ----

	[TestMethod]
	public void AClosingDrawerPlays0x1CAsItStartsAnd0xC2WhenTheSlideFinishes()
	{
		// [BIN:TPW-EXE:0x4a186f] message 0xB plays 0x1C; [0x4a1947] the finished slide plays 0xC2
		Use( "Ui", Category( (0x1C, 0, 4), (0xC2, 0, 8) ) );
		GameAudio.PlayDrawerClose();
		CollectionAssert.AreEqual( new[] { 4, 8 }, mixer.Voices.Select( voice => voice.Frames ).ToArray(), "0x1C first, then 0xC2" );
	}

	// ---- AUDIO-012 ----

	[TestMethod]
	public void ButtonsClickAndListRowsStaySilentUnlessAnOpenTpwScreenOptsIn()
	{
		// [BIN:TPW-EXE:0x485780] button message 0x100 clicks; a type-7 list row posts 0x400, never 0x100 (ListOwnerAudioTests, docs/AUDIO.md "List owners").
		Use( "Ui", Category( (GameAudio.ClickEvent, 0, 4) ) );
		new UiButton { Bounds = new UiRect( 0, 0, 10, 10 ) }.Activate();
		Assert.AreEqual( 1, mixer.Voices.Count, "a button" );
		var rows = new UiScrollList { Bounds = new UiRect( 0, 0, 100, 100 ), Rows = () => new List<string> { "a", "b" }, RowActivated = _ => { } };
		rows.Select( 1 );
		rows.Activate();
		Assert.AreEqual( 1, mixer.Voices.Count, "a list row activation plays no click by default" );
		var silent = new UiScrollList { Bounds = new UiRect( 0, 0, 100, 100 ), Rows = () => new List<string> { "a" }, ClicksOnActivate = true };
		silent.Select( 0 );
		silent.Activate();
		Assert.AreEqual( 1, mixer.Voices.Count, "a list with nothing to activate does not click" );
		var optedIn = new UiScrollList { Bounds = new UiRect( 0, 0, 100, 100 ), Rows = () => new List<string> { "a" }, ClicksOnActivate = true, RowActivated = _ => { } };
		optedIn.Select( 0 );
		optedIn.Activate();
		Assert.AreEqual( 2, mixer.Voices.Count, "an OpenTPW screen without an original counterpart can opt in" );
		new UiButton { Bounds = new UiRect( 0, 0, 10, 10 ), Enabled = false }.Activate();
		Assert.AreEqual( 2, mixer.Voices.Count, "a disabled control sends no message" );
		Assert.IsTrue( mixer.Voices.All( voice => voice.Position == null ), "flat" );
	}

	// ---- finish: the sounds read last (docs/AUDIO.md "Finishing pass") ----

	private static void FollowRectangleSounds() =>
		typeof( GameAudio ).GetMethod( "FollowRectangleSounds", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, null );

	[TestMethod]
	public void TheRectangleClampPutsTheSoundAtTheListenerOrTheNearestPointOfTheRectangle()
	{
		// [BIN:TPW-EXE:0x6bd5f0] x clamps into [x1, x2]; z is capped at z1 and then raised to z2 (the shipped records run z down from z1); y is the listener's. The jungle record: x 353..450, z 31 down to -66
		Assert.AreEqual( new Vector3( 400, 7, 0 ), GameAudio.RectangleSoundPlace( 353, 31, 450, -66, new Vector3( 400, 7, 0 ) ), "inside: on the listener" );
		Assert.AreEqual( new Vector3( 353, 0, 31 ), GameAudio.RectangleSoundPlace( 353, 31, 450, -66, new Vector3( 100, 0, 500 ) ), "left of it and past z1" );
		Assert.AreEqual( new Vector3( 450, 2, -66 ), GameAudio.RectangleSoundPlace( 353, 31, 450, -66, new Vector3( 900, 2, -500 ) ), "right of it and past z2" );
	}

	[TestMethod]
	public void ARectangleSoundIsStartedAndFollowsTheListenerEveryFrame()
	{
		// [BIN:TPW-EXE:0x550e00] a type 3 record: 0x51bfc0(0, global cat_ambient, word 2, x, y, z) bound by 0x51c5d0 to the rectangle x..x+w7, z..z+w8 (w8 negative in every shipped record)
		temporary = Directory.CreateTempSubdirectory( "opentpw-placed-rectangle-" ).FullName;
		Directory.CreateDirectory( Path.Combine( temporary, "levels", "x" ) );
		var words = new[] { new[] { 3, 0, 8, 10 * 1024, 0, 20 * 1024, 0, 4 * 1024, -6 * 1024 } };
		var data = new byte[12 + words.Length * 60 + 4];
		BinaryPrimitives.WriteUInt32LittleEndian( data, 0x5F4A424F );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 4 ), words.Length );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 8 ), 60 );
		for ( var word = 0; word < words[0].Length; word++ )
			BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 12 + word * 4 ), words[0][word] );
		File.WriteAllBytes( Path.Combine( temporary, "levels", "x", "scape.omp" ), data );
		FileSystem = new BaseFileSystem( temporary );
		Use( "Ambient", Category( (8, 0x9, 4) ) );
		AudioListener.Update( new Vector3( 12, 5, 17 ), new Vector3( 0, 0, 1 ) );

		GameAudio.StartPlacedAmbients( "x" );
		system.Pump();
		system.Pump();

		var voice = mixer.Voices.Single();
		Assert.AreEqual( new Vector3( 12, 5, 17 ), voice.Position, "started on the listener inside the rectangle" );
		AudioListener.Update( new Vector3( 0, 0, 0 ), new Vector3( 0, 0, 1 ) );
		FollowRectangleSounds();
		Assert.AreEqual( new Vector3( 10, 0, 14 ), voice.Position, "outside: the nearest point (x1, z2)" );
		AudioListener.Update( new Vector3( 100, 3, 100 ), new Vector3( 0, 0, 1 ) );
		FollowRectangleSounds();
		Assert.AreEqual( new Vector3( 14, 3, 20 ), voice.Position, "beyond x2 and z1" );
		GameAudio.StopPlacedAmbients();
		Assert.IsTrue( voice.IsFinished, "the park's end stops it" );
	}

	[TestMethod]
	public void TheTrackAndCoasterToolClicksAndThePauseBackdropClickUseTheTracedEvents()
	{
		// [BIN:TPW-EXE:0x526ce9] cat_ui 0xCE (track modes 0xB and 0x10), [BIN:TPW-EXE:0x526423] cat_ui 0x65 (mode 0x15), [BIN:TPW-EXE:0x492d9c] cat_ui 0xC1 (the pause menu's backdrop), [BIN:TPW-EXE:0x4398c8] cat_rides 0xC (the coaster crash)
		Assert.AreEqual( 0xCEu, GameAudio.TrackToolClickEvent );
		Assert.AreEqual( 0x65u, GameAudio.CoasterToolClickEvent );
		Assert.AreEqual( 0xC1u, GameAudio.PauseBackdropEvent );
		Assert.AreEqual( 0xCu, GameAudio.CoasterCrashEvent );
		Use( "Ui", Category( (0xCE, 0x1, 4) ) );
		GameAudio.PlayUiAt( GameAudio.TrackToolClickEvent, GameAudio.CellSoundPosition( 3, 4 ) );
		Assert.AreEqual( new Vector3( 30, 0, 40 ), mixer.Voices.Single().Position, "(x * 10, 0, z * 10) of the clicked cell" );
	}

	/// <summary>A kart line writer whose ghost refuses one tile and accepts the rest.</summary>
	private sealed class RefusingTrackWriter( (int X, int Y) refused ) : ICellLineWriter
	{
		public CellToolMode Mode => CellToolMode.KartTrack;
		public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) =>
			new( cursor, Array.Empty<(int X, int Y)>(), 0, start == refused || cursor == refused ? CellBuildResult.Occupied : CellBuildResult.Ok, false )
			{ Start = start, Line = ParkPathBuilder.LayLine( start, cursor ), StoppedAt = start == refused || cursor == refused ? refused : null };
		public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end ) => Preview( start, end );
		public void Undo( SegmentResult segment ) { }
	}

	[TestMethod]
	public void ARefusedTrackToolClickThroughTheHudPlaysTheRefusalCueAndNoTrackClick()
	{
		// [BIN:TPW-EXE:0x524ab6] a release with the refused-ghost latch set plays 0xAF and leaves through the latch's mode table (0x52766c) to SetMode(0, 0) before 0x526cb3, which plays 0xCE
		Use( "Ui", Category( (GameAudio.TrackToolClickEvent, 0x1, 4), (GameAudio.PathRefusedEvent, 0x1, 4) ) );
		var fixture = new RideUpgradeIngressTests();
		fixture.Setup();
		try
		{
			var hud = fixture.Fixture( Hud.BuildCategory.Rides, out var level, out _, out _, out _, out _ );
			var context = RideUpgradeIngressTests.Context();
			context.Delta = 0.016f;
			level.CellTool.Enter( new RefusingTrackWriter( (2, 2) ), null );
			HudToolClick.Click( hud, (2, 2) );
			Assert.IsFalse( level.CellTool.IsActive, "the refused ghost of the tile ends the line tool" );
			Assert.AreEqual( 0, mixer.Voices.Count, "the refused click plays no track click 0xCE" );
			hud.Update( context, new UiInput( new System.Numerics.Vector2( -1 ), false, false, true, false, UiKeys.None ), false, false, false );
			Assert.AreEqual( new Vector3( 20, 0, 20 ), mixer.Voices.Single().Position, "its release plays the refusal cue 0xAF at the clicked cell" );
			level.CellTool.Enter( new RefusingTrackWriter( (9, 9) ), null );
			HudToolClick.Click( hud, (4, 4) );
			Assert.AreEqual( (4, 4), level.CellTool.Start, "an accepted click stores the start" );
			Assert.AreEqual( 2, mixer.Voices.Count, "and plays the track click 0xCE" );
			Assert.AreEqual( new Vector3( 40, 0, 40 ), mixer.Voices[^1].Position );
			// a line click with a start whose ghost refuses a tile leaves the same way: the latch is tested before the mode dispatch, start or none (0x524A63)
			level.CellTool.Enter( new RefusingTrackWriter( (8, 4) ), (4, 4) );
			HudToolClick.Click( hud, (8, 4) );
			Assert.IsFalse( level.CellTool.IsActive, "the refused ghost of the line ends the line tool" );
			Assert.AreEqual( 2, mixer.Voices.Count, "and plays no track click 0xCE" );
			hud.Update( context, new UiInput( new System.Numerics.Vector2( -1 ), false, false, true, false, UiKeys.None ), false, false, false );
			Assert.AreEqual( 3, mixer.Voices.Count );
			Assert.AreEqual( new Vector3( 80, 0, 40 ), mixer.Voices[^1].Position, "its release plays the refusal cue 0xAF" );
		}
		finally
		{
			fixture.Cleanup();
		}
	}

	[TestMethod]
	public void AClickOnThePauseMenuBackdropPlaysCatUi0xC1ButAClickOnALineDoesNot()
	{
		Use( "Ui", Category( (0xC1, 0x200, 40) ) );
		var clicked = 0;
		var screen = UiDialogs.TextMenu( "pause", new (string Id, Func<string> Text, Action Clicked, bool Enabled)[] { ("resume", () => "Resume", () => clicked++, true) }, () => { }, "resume" );
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 2048, 1536 ) };
		var line = screen.Elements.OfType<UiMenuItem>().Single();
		var inside = line.ScreenRect( context.Canvas ).Center;

		screen.Update( context, UiInput.Click( new System.Numerics.Vector2( 3, 3 ) ) );
		Assert.AreEqual( 1, mixer.Voices.Count, "a left click outside the lines is a backdrop click" );
		screen.Update( context, UiInput.Click( inside ) );
		Assert.AreEqual( 1, mixer.Voices.Count, "a click on a line is the line's" );
		screen.Update( context, UiInput.Idle( new System.Numerics.Vector2( 3, 3 ) ) );
		Assert.AreEqual( 1, mixer.Voices.Count, "no click, no sound" );
	}
}
