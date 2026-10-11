namespace OpenTPW;

/// <summary>
/// The game's sound service: one mixer, the original sound categories and the park music. Mirrors the
/// Mac game's sound manager (<c>0x100BC6F0</c> registers the global <c>cat_ambient</c>, <c>cat_rides</c>,
/// <c>cat_ui</c>, <c>cat_kids</c>, <c>cat_staff</c> and <c>cat_speech</c> at sound-manager offsets 0/8/12/4/16/20,
/// <c>0x100BC438</c> the global <c>cat_globallobbysfx</c> at offset 40, <c>0x100BC4C0</c> a level's
/// <c>cat_locallobbymusic</c>/<c>cat_locallobbysfx</c> pair, and <c>0x100BC864</c> a level's <c>cat_ambient</c>,
/// <c>cat_rides</c>, <c>cat_speech</c> and <c>cat_music</c> at offsets 24/28/32/36). Volumes come from the
/// options; speech ducks the other channels by <c>SoundInfo.DUCKINGLEVEL</c> from <c>sound.sam</c>.
/// </summary>
public static class GameAudio
{
	/// <summary>The park music event the original starts when a park opens (<c>0x100BC144</c>: event 2 of the level's music category).</summary>
	public const uint ParkMusicEvent = 2;
	/// <summary>The music parameter's cap (<c>0x101C2448</c>: values above 0x59 become 0x59).</summary>
	public const int MaximumMusicParameter = 89;
	/// <summary>The click every control activation plays (<c>0x10137FD0</c>: a 0x100 message from a control plays <c>cat_ui</c> 0x1F).</summary>
	public const uint ClickEvent = 0x1F;
	/// <summary>The error feedback of the buy window and the dialogs (<c>0x10139A40</c>: <c>cat_ui</c> 0x1D; called from the buy window at <c>0x10163AFC</c>/<c>0x10163BFC</c> and the checkbox dialog at <c>0x1013B7A4</c>).</summary>
	public const uint ErrorEvent = 0x1D;
	/// <summary>The map/research drawer's open sound (<c>0x1015644C</c> message 0x0b: <c>cat_ui</c> 0x1C).</summary>
	public const uint DrawerOpenEvent = 0x1C;
	/// <summary>The map/research drawer's close sound (<c>0x1015644C</c> message 0x10 finishing state 3 plays <c>cat_ui</c> 0xC2 through the play-by-id helper <c>0x101383F8</c> at <c>0x101568DC</c>).</summary>
	public const uint DrawerCloseEvent = 0xC2;
	/// <summary>The crowd event (<c>0x100BC210</c>/<c>0x100BC2E0</c>: <c>cat_kids</c> 0x5B, a branching sentence).</summary>
	public const uint CrowdEvent = 0x5B;
	/// <summary>The crowd parameter's cap (<c>0x101C2498</c> and <c>0x100BC238</c>: values above 100 become 100).</summary>
	public const int MaximumCrowdParameter = 100;
	/// <summary>The lobby music event (<c>0x10095A24</c>: event 1 of the selected level's <c>cat_locallobbymusic</c>).</summary>
	public const uint LobbyMusicEvent = 1;
	/// <summary>The looping lobby ambience event (<c>0x10095A50</c>: event 2 of the selected level's <c>cat_locallobbysfx</c>).</summary>
	public const uint LobbyAmbienceEvent = 2;
	/// <summary>The repair sound a ride script's <c>REPAIREFFECT 1</c> starts (<c>0x100B1F78</c> → <c>0x100ACC24</c>: <c>cat_kids</c>, families 75–78/79–82/83–86/87–90 by the object's repair level, index 0 for the traced call).</summary>
	public const uint RepairStartEvent = 75;
	/// <summary>The first sound pair a ride script's <c>REPAIREFFECT 0</c> plays (<c>0x100B1E60</c>–<c>0x100B1EC0</c>: <c>cat_rides</c> 111 at the object's position).</summary>
	public const uint RepairStopEvent = 111;
	/// <summary>The second sound of <c>REPAIREFFECT 0</c> (<c>0x100B1ED0</c>–<c>0x100B1F00</c>: <c>cat_rides</c> 211 at the object's position).</summary>
	public const uint RepairStopSecondEvent = 211;
	private static SoundEventSystem? events;
	private static SoundSentence? music;
	private static SoundSentence? crowd;
	private static SoundSentence? lobbyMusic;
	private static SoundSentence? lobbyAmbience;

	public static bool Enabled { get; set; } = true;
	public static AudioMixer? Mixer => events?.Mixer;
	public static SoundEventSystem? Events => events;
	public static SoundCategory? Ui { get; private set; }
	public static SoundCategory? Speech { get; private set; }
	public static SoundCategory? Ambient { get; private set; }
	public static SoundCategory? Rides { get; private set; }
	public static SoundCategory? Kids { get; private set; }
	public static SoundCategory? Staff { get; private set; }
	public static SoundCategory? GlobalLobbySfx { get; private set; }
	public static SoundCategory? LevelMusic { get; private set; }
	public static SoundCategory? LevelSpeech { get; private set; }
	public static SoundCategory? LevelAmbient { get; private set; }
	public static SoundCategory? LevelRides { get; private set; }
	public static SoundCategory? LobbyMusic { get; private set; }
	public static SoundCategory? LobbySfx { get; private set; }
	public static SoundSentence? Music => music;
	/// <summary>The running crowd sentence, when guests are near the camera.</summary>
	public static SoundSentence? Crowd => crowd;
	/// <summary>The running lobby music sentence, when the front end is showing.</summary>
	public static SoundSentence? LobbyMusicSentence => lobbyMusic;

	/// <summary>Opens the device and loads the global categories on first use; false when sound is off.</summary>
	public static bool EnsureStarted()
	{
		if ( events != null )
			return true;
		if ( !Enabled )
			return false;
		AudioApproximations.LogOnce();
		var mixer = AudioMixer.Open();
		AudioMixer.Current = mixer;
		// Clock seed like the original's LbTime_GetClock; it only colours front-end sounds, since EnterPark reseeds from the park's world seed.
		events = new SoundEventSystem( mixer, (uint)Environment.TickCount );
		mixer.DuckingLevel = ReadDuckingLevel() / 100f;
		// [BIN:STP-PPC:0x100BC6F0 sound manager] global categories registered at sound-manager offsets 12 (cat_ui), 20 (cat_speech), 0 (cat_ambient), 8 (cat_rides), 4 (cat_kids), 16 (cat_staff)
		Ui = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_ui" );
		Speech = SoundCategory.TryLoad( FileSystem, "/global/Speech", "cat_speech" );
		Ambient = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_ambient" );
		Rides = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_rides" );
		Kids = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_kids" );
		Staff = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_staff" );
		// [BIN:STP-PPC:0x100BC438 lobby registration] the global lobby effects category registered at sound-manager offset 40
		GlobalLobbySfx = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_globallobbysfx" );
		Log.Trace( $"Sound: {(mixer.HasDevice ? "SDL audio" : "no device")}, ducking {mixer.DuckingLevel:P0}, UI events {Ui?.Catalog.Events.Count ?? 0}, speech events {Speech?.Catalog.Events.Count ?? 0}." );
		return true;
	}

	// [DATA:sound.sam:SoundInfo.DUCKINGLEVEL] the percentage music and effects keep while speech plays (38 in the shipped file)
	private static int ReadDuckingLevel()
	{
		try
		{
			if ( !FileSystem.FileExists( "/sound.sam" ) )
				return 100;
			var settings = new SamSettings( new[] { SamDocument.Parse( FileSystem.ReadAllText( "/sound.sam" ), "/sound.sam" ) } );
			return Math.Clamp( settings.GetInt( "SoundInfo.DUCKINGLEVEL", 100, optional: true ), 0, 100 );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or FormatException )
		{
			Log.Warning( $"sound.sam ignored: {exception.Message}" );
			return 100;
		}
	}

	/// <summary>Loads a level's categories and starts its park music.</summary>
	/// <param name="seed">The park's sound seed (<see cref="WorldSeed.SoundStream"/>); null keeps the running chooser.</param>
	public static void EnterPark( string level, uint? seed = null )
	{
		if ( !EnsureStarted() )
			return;
		LeaveLobby();
		LeavePark();
		if ( seed is uint value )
			events!.Reseed( value );
		LevelMusic = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Music", "cat_music" );
		LevelSpeech = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Speech", "cat_speech" );
		// [BIN:STP-PPC:0x100BC864 level registration] the level's cat_ambient and cat_rides registered at sound-manager offsets 24 and 28
		LevelAmbient = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Sound", "cat_ambient" );
		LevelRides = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Sound", "cat_rides" );
		music = events!.StartSentence( LevelMusic, ParkMusicEvent, AudioChannel.Music );
		Log.Trace( $"Sound: {level} music {(music == null ? "not available" : $"{music.Event.Sounds.Count} sections")}, {LevelRides?.Catalog.Events.Count ?? 0} level ride events, {LevelAmbient?.Catalog.Events.Count ?? 0} level ambient events." );
	}

	public static void LeavePark()
	{
		music?.Stop();
		music = null;
		LevelMusic = null;
		LevelSpeech = null;
		LevelAmbient = null;
		LevelRides = null;
		SetCrowd( 0 );
	}

	/// <summary>
	/// Starts the front end's lobby audio for <paramref name="level"/>'s theme: the lobby music and the looping lobby
	/// ambience of that level's lobby folders. The original registers a <c>cat_locallobbymusic</c>/<c>cat_locallobbysfx</c>
	/// pair per level and starts event 1 of the music and event 2 of the effects of the current island's theme.
	/// </summary>
	// [BIN:STP-PPC:0x100BC4C0 lobby registration] each level's Sound folder registers the cat_locallobbymusic/cat_locallobbysfx pair in the sound manager's lobby table
	// [BIN:STP-PPC:0x1009598C lobby audio start] the lobby component stops the old handles, starts cat_locallobbymusic event 1 (handle kept) and cat_locallobbysfx event 2 (handle kept) of the current island's theme
	public static void EnterLobby( string level )
	{
		if ( !EnsureStarted() )
			return;
		if ( lobbyLevel == level )
			return;
		LeaveLobby();
		lobbyLevel = level;
		LobbyMusic = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Sound", "cat_locallobbymusic" );
		LobbySfx = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Sound", "cat_locallobbysfx" );
		if ( LobbyMusic == null && LobbySfx == null )
			lobbyLevel = null; // the level's files are not there yet (the browser build copies them on first play); a later selection retries
		lobbyMusic = events!.StartSentence( LobbyMusic, LobbyMusicEvent, AudioChannel.Music );
		lobbyAmbience = events.StartSentence( LobbySfx, LobbyAmbienceEvent, AudioChannel.Effects );
		Log.Trace( $"Sound: {level} lobby music {(lobbyMusic == null ? "not available" : "playing")}, ambience {(lobbyAmbience == null ? "not available" : "playing")}." );
	}

	private static string? lobbyLevel;

	public static void LeaveLobby()
	{
		lobbyMusic?.Stop();
		lobbyAmbience?.Stop();
		lobbyMusic = null;
		lobbyAmbience = null;
		LobbyMusic = null;
		LobbySfx = null;
		lobbyLevel = null;
	}

	/// <summary>
	/// The crowd sentence: starts <c>cat_kids</c> 0x5B when guests are near the camera, follows the count as the
	/// sentence's parameter and stops when nobody is. The original's park turn counts guests within four cells of the
	/// camera, caps the count at 100, and starts/updates/stops the sentence with it.
	/// </summary>
	// [BIN:STP-PPC:0x101C2490 park turn] guests within four cells of the camera are counted (0x100C313C, radius argument 4) and capped at 100 (0x101C2498)
	// [BIN:STP-PPC:0x100BC210 crowd update] a count above 0 starts cat_kids event 0x5B and sets its parameter; 0 stops it (the start/stop wrapper is 0x100BC2E0)
	public static void SetCrowd( int guestsNearCamera )
	{
		if ( events == null )
			return;
		var parameter = Math.Clamp( guestsNearCamera, 0, MaximumCrowdParameter );
		if ( parameter > 0 )
		{
			crowd ??= events.StartSentence( Kids, CrowdEvent, AudioChannel.Effects );
			if ( crowd != null )
			{
				crowd.Parameter = parameter;
				crowd.Resume();
			}
		}
		else
		{
			crowd?.Stop();
			crowd = null;
		}
	}

	/// <summary>
	/// The sound category an RSE <c>EVENT</c> instruction's type operand selects, or null for a type the traced switch
	/// does not route. One row per traced route: the type, the category, the sound-manager offset the submit loads the
	/// category handle from and the submit's call site.
	/// </summary>
	// [BIN:STP-PPC:0x100AE930 RSE EVENT effect switch] each type loads its category handle from the sound-manager offset and submits the id operand as the event at the object's position
	public static readonly (int Type, string Category, int HandleOffset, uint SubmitAt)[] RseEventRoutes =
	{
		(3, "cat_rides", 28, 0x100AEBC0),       // the level's rides
		(4, "cat_ambient", 24, 0x100AEC24),     // the level's ambient
		(5, "cat_rides", 8, 0x100AEE74),        // global rides
		(6, "cat_kids", 4, 0x100AEED8),         // global kids
		(7, "cat_staff", 16, 0x100AEDAC),       // global staff
		(8, "cat_ambient", 0, 0x100AED48),      // global ambient
		(9, "cat_ui", 12, 0x100AEE10),          // global UI
	};

	/// <summary>The category an RSE <c>EVENT</c> instruction's type operand selects, or null for a type the traced switch does not route.</summary>
	public static SoundCategory? RseEventCategory( int type ) => type switch
	{
		3 => LevelRides,
		4 => LevelAmbient,
		5 => Rides,
		6 => Kids,
		7 => Staff,
		8 => Ambient,
		9 => Ui,
		_ => null,
	};

	/// <summary>
	/// The event an RSE <c>EVENT</c> instruction plays: the id is looked up in the routed category, and when that is a
	/// level category without the event, in the global category of the same name. Original scripts rely on the fallback
	/// (<c>incagod.RSE</c> plays <c>EVENT 3 id 43</c>, which only the global <c>cat_rides</c> has).
	/// </summary>
	// [APPROX:AUDIO-011] the level-to-global category fallback mirrors the bank fallback (AUDIO-002); the sound library's own fallback rule is not traced
	public static (SoundCategory Category, SoundEvent Event)? FindRseEvent( int type, uint eventId )
	{
		var category = RseEventCategory( type );
		var soundEvent = category?.Catalog.Find( eventId );
		if ( soundEvent == null && category == LevelRides )
		{
			category = Rides;
			soundEvent = category?.Catalog.Find( eventId );
		}
		if ( soundEvent == null && category == LevelAmbient )
		{
			category = Ambient;
			soundEvent = category?.Catalog.Find( eventId );
		}
		return soundEvent == null || category == null ? null : (category, soundEvent);
	}

	/// <summary>Plays an RSE <c>EVENT</c> instruction's sound (see <see cref="RseEventCategory"/>); returns the voice, or null when the type is untraced or nothing plays.</summary>
	public static AudioVoice? PlayRseEvent( int type, uint eventId )
	{
		var sound = FindRseEvent( type, eventId );
		return sound == null ? null : events?.Play( sound.Value.Category, eventId, AudioChannel.Effects );
	}

	/// <summary>
	/// The sounds of a ride script's <c>REPAIREFFECT</c> instruction: showing the repair effect starts a repair sound of
	/// <c>cat_kids</c>, hiding it plays two <c>cat_rides</c> sounds at the object's position.
	/// </summary>
	// [BIN:STP-PPC:0x100B1F78 REPAIREFFECT show] the repair-start path calls the cat_kids submit family (0x100ACC24; the traced call selects event 75, families up to 90 by the object's repair level)
	// [BIN:STP-PPC:0x100B1E60 REPAIREFFECT hide] hiding plays cat_rides 111 and 211 (0x100B1EC0/0x100B1F00) at the object's position
	// [APPROX:AUDIO-008] the repair-start event stays 75 (family 1, index 0); the original picks among 75–90 by the object's repair-level field (+20) and two float operands — evidence needed: the repair-level field's source in the object runtime
	public static void PlayRepairEffect( int show )
	{
		if ( events == null )
			return;
		if ( show != 0 )
			events.Play( Kids, RepairStartEvent, AudioChannel.Effects );
		else
		{
			events.Play( Rides, RepairStopEvent, AudioChannel.Effects );
			events.Play( Rides, RepairStopSecondEvent, AudioChannel.Effects );
		}
	}

	/// <summary>
	/// The music parameter for <paramref name="guestsInPark"/>: the original passes the cached guest count
	/// halved and capped at 100 (<c>0x100C2344</c>), then caps it at 89 and uses 0 in world state 4.
	/// </summary>
	// [BIN:STP-PPC:0x101C2444 park turn] music parameter = min(min(guests in park / 2, 100), 89), or 0 when the world state is 4
	public static int MusicParameter( int guestsInPark ) => Math.Min( Math.Min( Math.Max( 0, guestsInPark ) / 2, 100 ), MaximumMusicParameter );

	/// <summary>Per frame: applies the option volumes, the music parameter and tops up the output.</summary>
	public static void Update( int? guestsInPark = null )
	{
		if ( events == null )
			return;
		var mixer = events.Mixer;
		var options = GameOptions.Current;
		ApplyOptions( mixer, options );
		if ( music != null && guestsInPark is { } guests )
		{
			var parameter = MusicParameter( guests );
			if ( parameter != music.Parameter )
			{
				music.Parameter = parameter;
				music.Resume();
			}
		}
		events.Pump();
		mixer.Pump();
	}

	internal static void ApplyOptions( AudioMixer mixer, GameOptions options )
	{
		mixer.SetChannelGain( AudioChannel.Effects, options.SoundEffectsGain );
		mixer.SetChannelGain( AudioChannel.Music, options.MusicGain );
		mixer.SetChannelGain( AudioChannel.Speech, options.SpeechGain );
	}

	/// <summary>Plays a <c>cat_ui</c> event (control activation clicks, drawer and error feedback).</summary>
	public static AudioVoice? PlayUi( uint eventId ) => events?.Play( Ui, eventId, AudioChannel.Effects );

	/// <summary>Plays one event of any loaded category on the effects channel; null when nothing plays.</summary>
	public static AudioVoice? Play( SoundCategory? category, uint eventId ) => events?.Play( category, eventId, AudioChannel.Effects );

	public static void Shutdown()
	{
		LeaveLobby();
		LeavePark();
		events?.Mixer.Dispose();
		events = null;
		AudioMixer.Current = null;
	}
}
