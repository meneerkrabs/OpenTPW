using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The triggered-sound rules traced in the Mac binary: the RSE EVENT category routes, the crowd sentence,
/// the lobby sentences and the catalogues they play from. Synthetic tests need no game data; the original-data
/// tests skip without <c>OPENTPW_GAME_PATH</c>.
/// </summary>
[TestClass]
public class AudioTriggerTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	// ---- RSE EVENT routes (0x100AE930) ----

	[TestMethod]
	public void RseEventRoutesMatchTheTracedSwitch()
	{
		// [BIN:STP-PPC:0x100AE930 RSE EVENT effect switch] seven traced routes; the sound-manager offsets are the level/global category slots of 0x100BC6F0/0x100BC864
		CollectionAssert.AreEquivalent( new[] { 3, 4, 5, 6, 7, 8, 9 }, GameAudio.RseEventRoutes.Select( route => route.Type ).ToArray() );
		foreach ( var route in GameAudio.RseEventRoutes )
			StringAssert.StartsWith( route.Category, "cat_", $"type {route.Type}" );
		Assert.AreEqual( (3, "cat_rides", 28, 0x100AEBC0u), GameAudio.RseEventRoutes.Single( route => route.Type == 3 ), "the level's rides" );
		Assert.AreEqual( (8, "cat_ambient", 0, 0x100AED48u), GameAudio.RseEventRoutes.Single( route => route.Type == 8 ), "global ambient" );
		Assert.IsNull( GameAudio.RseEventCategory( 0 ) );
		Assert.IsNull( GameAudio.RseEventCategory( 10 ), "type 10 plays from the object type's own sound list, not a category of the game" );
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public void ParticleEventsNeverResolveAnAudioCategory( int type )
	{
		Assert.IsNull( GameAudio.RseEventCategory( type ) );
		foreach ( var id in new uint[] { 0, 18, 43, 104, uint.MaxValue } )
		{
			Assert.IsNull( GameAudio.FindRseEvent( type, id ) );
			Assert.IsNull( GameAudio.PlayRseEvent( type, id, new Vector3( 100, 20, 30 ) ) );
		}
	}

	// ---- crowd (0x100BC210) ----

	[TestMethod]
	public void CrowdConstantsMatchTheTracedCalls()
	{
		// [BIN:STP-PPC:0x100BC210 crowd update] cat_kids event 0x5B, parameter capped at 100 (0x101C2498 and 0x100BC238)
		Assert.AreEqual( 0x5Bu, GameAudio.CrowdEvent );
		Assert.AreEqual( 100, GameAudio.MaximumCrowdParameter );
		Assert.AreEqual( 4, GameFlow.CrowdRadiusCells, "0x101C2490 passes radius 4 to the guest counter" );
	}

	[TestMethod]
	public void SetCrowdStartsStopsAndFollowsTheCountWithoutSoundData()
	{
		// Without EnsureStarted the sound service is off: the crowd sentence is neither started nor throws.
		GameAudio.SetCrowd( 12 );
		Assert.IsNull( GameAudio.Crowd );
		GameAudio.SetCrowd( 0 );
		Assert.IsNull( GameAudio.Crowd );
	}

	// ---- lobby (0x1009598C) ----

	[TestMethod]
	public void LobbyConstantsMatchTheTracedCalls()
	{
		// [BIN:STP-PPC:0x10095A24/0x10095A50 lobby audio start] cat_locallobbymusic event 1 and cat_locallobbysfx event 2
		Assert.AreEqual( 1u, GameAudio.LobbyMusicEvent );
		Assert.AreEqual( 2u, GameAudio.LobbyAmbienceEvent );
	}

	// ---- UI events (0x10137FD0, 0x10139A40, 0x1015644C) ----

	[TestMethod]
	public void UiEventConstantsMatchTheTracedCalls()
	{
		Assert.AreEqual( 0x1Fu, GameAudio.ClickEvent, "0x10137FD0 plays 0x1F for a control's 0x100 message" );
		Assert.AreEqual( 0x1Du, GameAudio.ErrorEvent, "0x10139A40 plays 0x1D" );
		Assert.AreEqual( 0x1Cu, GameAudio.DrawerOpenEvent, "0x1015644C message 0x0b plays 0x1C" );
		Assert.AreEqual( 0xC2u, GameAudio.DrawerCloseEvent, "0x101568DC plays 0xC2 when the drawer finishes" );
	}

	[TestMethod]
	public void ActivatedButtonsRunTheirActionWithoutTheSoundService()
	{
		var clicked = 0;
		var button = new UiButton { Text = () => "Click", Clicked = () => clicked++ };
		button.Activate();
		Assert.AreEqual( 1, clicked );
		var disabled = new UiButton { Text = () => "Off", Enabled = false, Clicked = () => clicked++ };
		disabled.Activate();
		Assert.AreEqual( 1, clicked, "a disabled button neither clicks nor acts" );
	}

	// ---- original data (OPENTPW_GAME_PATH) ----

	private static string? DataDirectory()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			return null;
		return Directory.EnumerateDirectories( path ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path;
	}

	[TestMethod]
	public void OriginalTriggeredEventsExistWithTheOriginalPlayerTypes()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var fileSystem = new BaseFileSystem( data );
		var kids = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_kids" );
		Assert.IsNotNull( kids );
		// The crowd event is a branching sentence like the park music, so the 0-100 parameter moves it between sections.
		var crowd = kids.Catalog.Find( GameAudio.CrowdEvent );
		Assert.IsNotNull( crowd );
		Assert.AreEqual( SoundEventPlayer.BranchingSentence, crowd.Player );
		for ( var parameter = 0; parameter <= GameAudio.MaximumCrowdParameter; parameter++ )
			Assert.IsTrue( SoundSentence.NextSound( crowd, 0, parameter, 1 ) >= 0, $"parameter {parameter} has a section" );
		// Code 7 is the event's own code (slot 0) and sounds 1-6 bind it in slot 1 under the volume mask, so a count update re-evaluates them.
		Assert.AreEqual( GameAudio.CrowdParameterCode, crowd.Extra );
		CollectionAssert.AreEqual( Enumerable.Repeat( true, 6 ).Concat( Enumerable.Repeat( false, 6 ) ).ToArray(),
			crowd.Sounds.Select( sound => SoundSentence.BindsSlot( sound, GameAudio.CrowdParameterCode ) ).ToArray() );
		Assert.IsTrue( crowd.Sounds.Take( 6 ).All( sound => sound.Raw[24] == 1 && (sound.MinimumVolume, sound.MaximumVolume) == (14, 22) ) );
		// The advisor's cut-off slaps and the ticket advice's sound exist in the global banks.
		var speech = SoundCategory.TryLoad( fileSystem, "/global/Speech", "cat_speech" );
		Assert.IsNotNull( speech );
		for ( var slap = 0; slap < 3; slap++ )
			Assert.IsNotNull( speech.Catalog.Find( AutomaticAdvisor.FirstSlapSound + (uint)slap ), $"cat_speech slap {AutomaticAdvisor.FirstSlapSound + slap:#x}" );
		var ui = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_ui" );
		Assert.IsNotNull( ui );
		foreach ( var eventId in new[] { GameAudio.ClickEvent, GameAudio.ErrorEvent, GameAudio.DrawerOpenEvent, GameAudio.DrawerCloseEvent, 0xBDu, AdvisorSequences.TimerSound } )
			Assert.IsNotNull( ui.Catalog.Find( eventId ), $"cat_ui event {eventId:#x}" );
		var ambient = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_ambient" );
		Assert.IsNotNull( ambient );
		Assert.IsNotNull( ambient.Catalog.Find( 0x21 ), "the rain sentence" );
		Assert.IsNotNull( ambient.Catalog.Find( 0x20 ), "the thunder one-shot" );
		var rides = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_rides" );
		Assert.IsNotNull( rides );
		foreach ( var eventId in new[] { GameAudio.RepairFirstEvent, GameAudio.RepairSecondEvent } )
			Assert.IsNotNull( rides.Catalog.Find( eventId ), $"cat_rides event {eventId}" );
		var staff = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_staff" );
		Assert.IsNotNull( staff );
	}

	[TestMethod]
	public void OriginalLobbyAndLevelCataloguesLoadPerLevel()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var fileSystem = new BaseFileSystem( data );
		foreach ( var level in new[] { "jungle", "space", "fantasy", "hallow" } )
		{
			// [BIN:STP-PPC:0x100BC4C0 lobby registration] each level's Sound folder holds the local lobby pair
			var music = SoundCategory.TryLoad( fileSystem, $"/levels/{level}/Sound", "cat_locallobbymusic" );
			Assert.IsNotNull( music, level );
			Assert.AreEqual( 1, music.Catalog.Events.Count, $"{level}: only event 1" );
			Assert.AreEqual( SoundEventPlayer.Sentence, music.Catalog.Find( GameAudio.LobbyMusicEvent )!.Player, $"{level}: a plain sentence" );
			var sfx = SoundCategory.TryLoad( fileSystem, $"/levels/{level}/Sound", "cat_locallobbysfx" );
			Assert.IsNotNull( sfx, level );
			Assert.IsNotNull( sfx.Catalog.Find( GameAudio.LobbyAmbienceEvent ), $"{level}: the looping ambience" );
			// [BIN:STP-PPC:0x100BC864 level registration] the level's ambient and rides categories
			var ambient = SoundCategory.TryLoad( fileSystem, $"/levels/{level}/Sound", "cat_ambient" );
			Assert.IsNotNull( ambient, level );
			Assert.IsTrue( ambient.Catalog.Events.Count > 0, $"{level}: ambient events for RSE EVENT type 4" );
			var rides = SoundCategory.TryLoad( fileSystem, $"/levels/{level}/Sound", "cat_rides" );
			Assert.IsNotNull( rides, level );
			Assert.IsTrue( rides.Catalog.Events.Count > 0, $"{level}: ride events for RSE EVENT type 3" );
		}
	}

	[TestMethod]
	public void OriginalCrowdSentencePlaysAndFollowsTheParameter()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var fileSystem = new BaseFileSystem( data );
		var kids = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_kids" );
		Assert.IsNotNull( kids );
		var system = new SoundEventSystem( new AudioMixer( null ), seed: 11 );
		var crowd = system.StartSentence( kids, GameAudio.CrowdEvent, AudioChannel.Effects );
		Assert.IsNotNull( crowd );
		crowd.Parameter = 100;
		crowd.Resume();
		var clock = System.Diagnostics.Stopwatch.StartNew();
		while ( !crowd.IsPlaying && clock.ElapsedMilliseconds < 60000 )
		{
			System.Threading.Thread.Sleep( 10 );
			system.Pump();
		}
		Assert.IsTrue( crowd.IsPlaying, "the first crowd segment decodes (Layer I)" );
		crowd.Stop();
		Assert.IsTrue( crowd.IsStopped );
	}

	[TestMethod]
	public void OriginalLobbySentencesAndRideSoundsDecode()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var fileSystem = new BaseFileSystem( data );
		var system = new SoundEventSystem( new AudioMixer( null ), seed: 13 );
		var music = SoundCategory.TryLoad( fileSystem, "/levels/jungle/Sound", "cat_locallobbymusic" );
		var sfx = SoundCategory.TryLoad( fileSystem, "/levels/jungle/Sound", "cat_locallobbysfx" );
		Assert.IsNotNull( music );
		Assert.IsNotNull( sfx );
		Assert.IsNotNull( system.StartSentence( music, GameAudio.LobbyMusicEvent, AudioChannel.Music ) );
		Assert.IsNotNull( system.StartSentence( sfx, GameAudio.LobbyAmbienceEvent, AudioChannel.Effects ) );
		// A level ride event and the repair sounds decode through the same mixer (Layer I effects).
		var rides = SoundCategory.TryLoad( fileSystem, "/levels/jungle/Sound", "cat_rides" );
		Assert.IsNotNull( rides );
		Assert.IsTrue( rides.Catalog.Events.Count > 0, "ride events for RSE EVENT type 3" );
		var globalRides = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_rides" );
		Assert.IsNotNull( globalRides );
		Assert.IsNotNull( system.Play( globalRides, GameAudio.RepairFirstEvent, AudioChannel.Effects ), "the repair-finished sound decodes" );
		Assert.IsNotNull( system.Play( globalRides, GameAudio.RepairSecondEvent, AudioChannel.Effects ) );
	}

	/// <summary>
	/// Every literal EVENT instruction of every object script of the four levels stays inside the dispatcher's
	/// switch, and the routed types' (3-9) literal ids resolve in the level or the global catalogue — so the sounds
	/// the scripts ask for are the sounds the routing plays (types 1/2 create particles and type 0 is unsupported).
	/// </summary>
	// [BIN:STP-PPC:0x100AE930 RSE EVENT effect switch] the switch guards type < 10 (0x100AE938)
	[TestMethod]
	[DoNotParallelize]
	public void OriginalObjectScriptsUseOnlyRoutedRseEventTypes()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var previous = FileSystem;
		var bonus = ObjectCatalog.BonusDataRoot;
		try
		{
			FileSystem = new BaseFileSystem( data );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			ObjectCatalog.BonusDataRoot = null;
			var routed = GameAudio.RseEventRoutes.Select( route => route.Type ).ToHashSet();
			var literalTypes = 0;
			var silent = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var level in new[] { "jungle", "space", "fantasy", "hallow" } )
			{
				var levelRides = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Sound", "cat_rides" );
				var levelAmbient = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Sound", "cat_ambient" );
				var globalRides = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_rides" );
				var globalAmbient = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_ambient" );
				Assert.IsNotNull( levelRides, level );
				Assert.IsNotNull( levelAmbient, level );
				Assert.IsNotNull( globalRides );
				Assert.IsNotNull( globalAmbient );
				foreach ( var entry in ObjectCatalog.Load( level ).Entries )
				{
					if ( entry.ScriptPath == null )
						continue;
					using var stream = FileSystem.OpenRead( entry.ScriptPath );
					RideScriptFile script;
					try
					{
						script = new RideScriptFile( stream );
					}
					catch ( InvalidDataException )
					{
						continue;
					}
					foreach ( var instruction in script.Instructions )
					{
						if ( instruction.Opcode != (ushort)Opcode.EVENT || instruction.Operands.Count < 3 )
							continue;
						var type = instruction.Operands[0];
						if ( type.Kind != RideScriptOperandKind.Literal )
							continue;
						literalTypes++;
						Assert.IsTrue( type.Value <= 9, $"{entry.ScriptPath}: EVENT type {type.Value} is outside the dispatcher's switch (0x100AE938 guards < 10)" );
						if ( !routed.Contains( type.Value ) )
							continue; // types 1/2 create particles (0x1009FD70/0x1009F968), not audio; type 0 is unsupported
						var id = instruction.Operands[2];
						if ( id.Kind != RideScriptOperandKind.Literal )
							continue;
						// [BIN:TPW-EXE:0x6b87d0 event lookup] the level category is searched alone: an id it lacks is silent (incagod.RSE's EVENT 3 id 43 is only in the global cat_rides)
						if ( type.Value == 3 )
						{
							Assert.IsTrue( levelRides.Catalog.Find( id.Value ) != null || globalRides.Catalog.Find( id.Value ) != null, $"{entry.ScriptPath}: EVENT 3 id {id.Value} is neither a {level} nor a global cat_rides event" );
							if ( levelRides.Catalog.Find( id.Value ) == null )
								silent.Add( $"{Path.GetFileName( entry.ScriptPath )} EVENT 3 id {id.Value}" );
						}
						if ( type.Value == 4 )
						{
							Assert.IsTrue( levelAmbient.Catalog.Find( id.Value ) != null || globalAmbient.Catalog.Find( id.Value ) != null, $"{entry.ScriptPath}: EVENT 4 id {id.Value} is neither a {level} nor a global cat_ambient event" );
							if ( levelAmbient.Catalog.Find( id.Value ) == null )
								silent.Add( $"{Path.GetFileName( entry.ScriptPath )} EVENT 4 id {id.Value}" );
						}
					}
				}
			}
			Assert.IsTrue( literalTypes > 200, $"{literalTypes} literal EVENT instructions checked (the corpus uses more than 200)" );
			Console.WriteLine( $"Level EVENT ids only the global category has (silent, as in the original): {string.Join( "; ", silent )}" );
			Assert.IsTrue( silent.Contains( "incagod.RSE EVENT 3 id 43" ), "incagod.RSE's EVENT 3 id 43 is the known level id only the global cat_rides has" );
		}
		finally
		{
			FileSystem = previous;
			ObjectCatalog.BonusDataRoot = bonus;
		}
	}
}
