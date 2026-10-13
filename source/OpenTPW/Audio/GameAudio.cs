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
	/// <summary>The rain loop of the global <c>cat_ambient</c> (<c>0x10103C4C</c> mode 0: effect 0x21 submitted flat, the handle kept at the weather thing's +0x54).</summary>
	public const uint RainEvent = 0x21;
	/// <summary>The thunder one-shot of the global <c>cat_ambient</c> (<c>0x101034D8</c>–<c>0x10103518</c>: event 0x20 submitted at the last bolt's top, the weather thing's +0x48..+0x50).</summary>
	public const uint ThunderEvent = 0x20;
	/// <summary>The rain sentence's parameter id (<c>0x100BC388</c> sets parameter 8 of the global rain voice to the drop-percentage level, as the music uses 4 and the crowd 7).</summary>
	public const int RainParameterId = 8;
	/// <summary>The path tool's laying click: a committed line plays <c>cat_ui</c> 101 (Mac <c>0x10070DE0</c> in the commit pass <c>0x10070B98</c>, gated by the tool mode and its latch).</summary>
	// [BIN:STP-PPC:0x10070DE0 path lay click] cat_ui 101 under tool mode 1 or 3 with the latch clear; the PC doc's 0x005271B7 is a lead (packed PC exe)
	public const uint PathLayEvent = 101;
	/// <summary>The path tool's refused click: <c>cat_ui</c> 175 (Mac <c>0x10073D1C</c>, reached behind the latch and a byte test; the mapping to a refusal is inferred from the fork).</summary>
	// [BIN:STP-PPC:0x10073D1C refused click] cat_ui 175 behind the latch test; [APPROX:AUDIO-022] whether it is the refusal of a commit or a preview is not fully read
	public const uint PathRefusedEvent = 175;
	/// <summary>The path tool's link-end sound (cat_ui 139, Mac <c>0x100711CC</c> in queue mode): not wired, its trigger is inferred.</summary>
	// [BIN:STP-PPC:0x100711CC queue link end] cat_ui 139 in queue mode after the passes; [APPROX:AUDIO-023] not wired — the link-end condition is inferred, not traced
	public const uint PathLinkEvent = 139;
	private static SoundEventSystem? events;
	private static SoundSentence? music;
	private static SoundSentence? crowd;
	private static SoundSentence? lobbyMusic;
	private static SoundSentence? lobbyAmbience;
	private static SoundSentence? rain;
	private static long staffTurn = -1;
	private static DeterministicRandom staffRandom = new( 1 );
	private static DeterministicRandom crowdRandom = new( 1 );
	private static bool staffVoiceLogged;
	private static IReadOnlyList<PlacedSound>? placedAmbients;

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
		staffRandom = new DeterministicRandom( events!.Seed ^ 0x53544146u );
		// [APPROX:AUDIO-035] crowd sample choices advance an independent host stream seeded from the sound seed XOR ASCII CROW; the original source of variation is unverified, and this presentation stream is not persisted
		crowdRandom = new DeterministicRandom( events.Seed ^ 0x43524F57u );
		LevelMusic = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Music", "cat_music" );
		LevelSpeech = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Speech", "cat_speech" );
		// [BIN:STP-PPC:0x100BC864 level registration] the level's cat_ambient and cat_rides registered at sound-manager offsets 24 and 28
		LevelAmbient = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Sound", "cat_ambient" );
		LevelRides = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Sound", "cat_rides" );
		music = events!.StartSentence( LevelMusic, ParkMusicEvent, AudioChannel.Music );
		StartPlacedAmbients( level );
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
		SetRain( 0 );
		StopPlacedAmbients();
		events?.StopChains();
		staffTurn = -1;
		staffVoiceLogged = false;
	}

	/// <summary>
	/// Starts the land's own ambient sounds: the type 1 records of the level's <c>scape.omp</c>, each an effect of the
	/// global <c>cat_ambient</c> (word 1 nought) or of the level's own category, played at its place with its range
	/// so the mixer's distance law turns it down. Type 3 records (a sound that follows the listener inside a rectangle)
	/// are counted, not played.
	/// </summary>
	// [DATA:levels/*/scape.omp] type 1 records: word 1 the category (nought: global cat_ambient), word 2 the effect, words 3-5 the place, word 6 the range (x2/1024)
	// [LEAD:maexah/OpenTPW docs/exe/park.md "The land's own sounds" and World/Park/ParkPlacedObjects.cs] the hand-off to FUN_0051c130 with the place and range; PC addresses unverifiable (packed exe)
	// [APPROX:AUDIO-021] the record's place is in park units and the listener's place in the same sound units — evidence needed: the Mac listener and placement units traced together
	// [APPROX:AUDIO-020] a placed effect that is a sentence (not a one-shot) plays its first sound once — evidence needed: the placed sentence's player on the Mac
	public static void StartPlacedAmbients( string level )
	{
		StopPlacedAmbients();
		var path = $"/levels/{level}/scape.omp";
		if ( events == null || !FileSystem.FileExists( path ) )
			return;
		var file = new PlacedObjectsFile( FileSystem.ReadAllBytes( path ) );
		var voices = new List<PlacedSound>();
		var type3 = 0;
		foreach ( var record in file.Records )
		{
			if ( record.Type == PlacedObjectsFile.RectangleSoundType )
			{
				type3++;
				continue;
			}
			if ( record.Type != PlacedObjectsFile.RangedSoundType )
				continue;
			var category = record.Category == 0 ? Ambient : LevelAmbient;
			var (x, height, z) = record.SoundPlace;
			// Each starts once its sample is decoded, so the park's first frame does not wait for them (docs/BROWSER-PERFORMANCE.md).
			var voice = events.PlayAtWhenDecoded( category, (uint)record.Sound, AudioChannel.Effects, new Vector3( x, height, z ), record.Range > 0 ? record.Range : null );
			if ( voice != null )
				voices.Add( voice );
		}
		placedAmbients = voices;
		Log.Trace( $"Sound: {level} placed ambients {voices.Count} starting, {type3} rectangle sounds not played (scape.omp type 3)." );
	}

	/// <summary>Stops the land's own ambient voices (the level's <c>scape.omp</c> records).</summary>
	public static void StopPlacedAmbients()
	{
		if ( placedAmbients != null )
		{
			foreach ( var voice in placedAmbients )
				voice.Stop();
			placedAmbients = null;
		}
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
	private static double lobbyOneShotSeconds;
	private static readonly uint[] LobbyIslandOneShotEvents = { 3, 4, 5 };
	private static System.Random? lobbyOneShotRandom;

	public static void LeaveLobby()
	{
		lobbyMusic?.Stop();
		lobbyAmbience?.Stop();
		lobbyMusic = null;
		lobbyAmbience = null;
		LobbyMusic = null;
		LobbySfx = null;
		lobbyLevel = null;
		lobbyOneShotSeconds = 0;
		lobbyOneShotRandom = null;
	}

	/// <summary>
	/// The lobby's island ambience one-shots: events 3, 4 and 5 of the selected level's <c>cat_locallobbysfx</c>, played
	/// on a random timer while the front end shows. The original arms a timer per one-shot and restarts it after each
	/// plays; only the events are traced, not the timer's shape, so the gap here is OpenTPW's own.
	/// </summary>
	// [BIN:STP-PPC:0x100953F8-0x10095450 lobby one-shots] cat_locallobbysfx 3, 4 and 5 play off random timers beside the looping ambience (event 2)
	// [APPROX:AUDIO-017] the one-shot timer's distribution is not traced; a uniform gap of 5-15 s stands — evidence needed: the timers armed at 0x100953F8
	private static void UpdateLobbyOneShots()
	{
		if ( events == null || lobbyAmbience == null )
			return;
		lobbyOneShotRandom ??= new System.Random();
		lobbyOneShotSeconds -= Time.Delta;
		if ( lobbyOneShotSeconds > 0 )
			return;
		var eventId = LobbyIslandOneShotEvents[lobbyOneShotRandom.Next( LobbyIslandOneShotEvents.Length )];
		events.Play( LobbySfx, eventId, AudioChannel.Effects );
		lobbyOneShotSeconds = 5 + lobbyOneShotRandom.NextDouble() * 10;
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
			if ( crowd == null )
			{
				crowd = events.StartSentence( Kids, CrowdEvent, AudioChannel.Effects, () => (uint)crowdRandom.Next( 1 << 16 ) );
				if ( crowd != null )
					Log.Trace( $"Sound: crowd sentence started ({guestsNearCamera} guests within {GameFlow.CrowdRadiusCells} cells of the camera)." );
			}
			if ( crowd != null )
			{
				// [BIN:STP-PPC:0x100BC298–0x100BC29C crowd update] the capped nearby count is sent with parameter code 7 to 0x100BAF70
				crowd.ParameterCode = 7;
				crowd.Parameter = parameter;
				crowd.Resume();
			}
		}
		else if ( crowd != null )
		{
			crowd.Stop();
			crowd = null;
			Log.Trace( "Sound: crowd sentence stopped (nobody near the camera)." );
		}
	}

	/// <summary>
	/// The rain sentence: starts <c>cat_ambient</c> 0x21 when the level rises above zero, follows the level as the
	/// sentence's parameter (the branching links pick the heavier sections), and stops when it returns to zero.
	/// The original's level setter starts, updates and stops one global rain voice this way; the weather thing also
	/// starts a second, unmodulated voice of its own that OpenTPW does not reproduce.
	/// </summary>
	// [BIN:STP-PPC:0x100BC388 rain level] above 0 the global rain voice (cat_ambient 0x21, submitted flat) is started once and given parameter 8 = the level; 0 stops it with 0x100BBA58
	// [BIN:STP-PPC:0x10103394 level source] the level is the weather thing's current drop count × 100 / MaxRaindrops (the Mac's rain tick; the fork's +0x34 target reading is refuted)
	// [APPROX:AUDIO-015] only the level-driven rain voice is built; the weather thing's own +0x54 voice (0x10103C4C mode 0) is not — whether the original's second voice is audible is not established
	public static void SetRain( int level )
	{
		if ( events == null )
			return;
		var parameter = Math.Clamp( level, 0, 100 );
		if ( parameter > 0 )
		{
			if ( rain == null )
			{
				rain = events.StartSentence( Ambient, RainEvent, AudioChannel.Effects );
				if ( rain != null )
					Log.Trace( $"Sound: rain sentence started (level {parameter})." );
			}
			if ( rain != null )
			{
				rain.Parameter = parameter;
				rain.Resume();
			}
		}
		else if ( rain != null )
		{
			rain.Stop();
			rain = null;
			Log.Trace( "Sound: rain sentence stopped (the weather dried up)." );
		}
	}

	/// <summary>Plays the thunder one-shot at a strike's position (the original passes the top of the last bolt, height 300).</summary>
	// [BIN:STP-PPC:0x101034C8-0x10103518 thunder submit] when the weather thing's thunder countdown (+0x44) reaches 0, cat_ambient 0x20 is submitted at the last lightning location (+0x48..+0x50)
	public static AudioVoice? PlayThunder( Vector3 position )
	{
		var voice = events?.PlayAt( Ambient, ThunderEvent, AudioChannel.Effects, position );
		Log.Trace( $"Sound: thunder at ({position.X:0},{position.Y:0},{position.Z:0}) {(voice == null ? "not available" : "played")}." );
		return voice;
	}

	/// <summary>
	/// A ride script's <c>STARTSCREAM</c>: the band operand picks the held-chain event, the level operand (with the
	/// script's speed) its parameter. Returns the chain, or null when the band is zero (nothing plays) or a scream is
	/// already held — the original refuses then, logs its line, and leaves the held chain screaming unreferenced.
	/// </summary>
	// [BIN:STP-PPC:0x100B19DC STARTSCREAM] operand 1 is the band itself (no visitor lookup), operand 2 the level; the handle is kept in the ride's VM at +208
	// [BIN:STP-PPC:0x100ACA38 scream event] band 0 plays nothing; 1 plays cat_kids 71; 2-3 play 72 (negative bands too); 4-7 play 73; 8 and above play 74
	// [BIN:STP-PPC:0x100ACB7C scream parameter] parameter = (level + speed) / 2 clamped to 0..100 (signed shift); the speed is the ride VM's word at +192, default 50 (0x100B3154, 0x100B318C)
	// [BIN:STP-PPC:0x100B19DC refusal] a ride already holding a scream handle refuses: the engine logs "Started screaming without stopping first." and overwrites the handle with 0, so the held chain screams on unreferenced
	// [APPROX:AUDIO-032] the scream's place is the ride's box middle in the original (0x100AC71C, midpoints of two floats and the object's height at +68); OpenTPW plays it flat — evidence needed: the box-middle mapping onto OpenTPW's ride objects
	// [APPROX:AUDIO-016] OpenTPW has no per-script speed word; the loader's default 50 stands — evidence needed: the RSE loader's speed field beside RideVM's timing
	public static SoundChain? StartScream( Func<SoundChain?> held, int band, int level, Vector3? position )
	{
		if ( events == null || held() != null )
		{
			if ( held() != null )
				Log.Trace( "RSSE: Started screaming without stopping first." );
			return null;
		}
		if ( ScreamBandEvent( band ) is not { } eventId )
			return null;
		var parameter = ScreamParameter( level );
		var chain = events.StartChain( Kids, eventId, AudioChannel.Effects, parameter, position );
		if ( chain != null )
			Log.Trace( $"Sound: scream chain started (cat_kids {eventId}, parameter {parameter})." );
		return chain;
	}

	/// <summary>The held-chain event a scream's band operand picks (Mac <c>0x100ACA38</c>: 0 nothing, 1 → 71, 2–3 → 72, 4–7 → 73, 8+ → 74), or null for band 0.</summary>
	public static uint? ScreamBandEvent( int band ) => band == 0 ? null : band == 1 ? 71u : band >= 8 ? 74u : band >= 4 ? 73u : 72u;

	/// <summary>A scream's parameter: (level + script speed) / 2 clamped to 0..100 (Mac <c>0x100ACB7C</c>; the speed word's default 50).</summary>
	// [APPROX:AUDIO-016] OpenTPW keeps no per-script speed word, so the default 50 stands for every ride
	public static int ScreamParameter( int level, int scriptSpeed = 50 ) => Math.Clamp( (level + scriptSpeed) / 2, 0, 100 );

	/// <summary>
	/// A ride script's <c>SINGLESCREAM</c>: the original places it at the ride's box middle; the host plays it flat
	/// while that mapping is missing (AUDIO-032). A non-negative second operand takes
	/// the 4×4 grid (the band's row crossed with (level + speed) / 50 clamped to 0..3); a negative one picks on the
	/// band alone. The second operand is read as a short, so the corpus's literal 65535 is −1.
	/// </summary>
	// [BIN:STP-PPC:0x100ACC24 single scream grid] band row 1/2-3/4-7/8+ holds event 75/79/83/87 plus (level + speed) / 50 clamped to 0..3 (ids 0x4B..0x5A, one-shots, no handle kept)
	// [BIN:STP-PPC:0x100ACE68 single scream, negative level] band 1 → 105, 2-3 → 106, 4-7 → 108, 8+ → 109; event 107 is the original's own gap
	// [BIN:STP-PPC:0x100B1AD4 branch] the test is on the second operand read as a signed short: >= 0 takes the grid, < 0 the band alone
	public static AudioVoice? SingleScream( int band, short level, Vector3? position, int scriptSpeed = 50 )
	{
		if ( events == null )
			return null;
		if ( ScreamBandEvent( band ) is not { } row )
			return null;
		uint eventId;
		if ( level >= 0 )
			eventId = (uint)(ScreamGridRow( row ) + Math.Clamp( (level + scriptSpeed) / 50, 0, 3 ));
		else
			eventId = ScreamSingleEvent( row );
		if ( eventId == 0 )
			return null;
		var voice = position is { } place
			? events.PlayAt( Kids, eventId, AudioChannel.Effects, place )
			: events.Play( Kids, eventId, AudioChannel.Effects );
		Log.Trace( $"Sound: single scream cat_kids {eventId} {(voice == null ? "not available" : "played")}." );
		return voice;
	}

	/// <summary>The grid row base of a scream band's event (75 for band 1, 79 for 2–3, 83 for 4–7, 87 for 8+).</summary>
	public static int ScreamGridRow( uint bandEvent ) => bandEvent switch { 71 => 75, 72 => 79, 73 => 83, _ => 87 };

	/// <summary>The band-alone event of a negative-level single scream (105 for band 1, 106 for 2–3, 108 for 4–7, 109 for 8+).</summary>
	public static uint ScreamSingleEvent( uint bandEvent ) => bandEvent switch { 71 => 105, 72 => 106, 73 => 108, _ => 109 };

	/// <summary>
	/// One staff voice: the walking or idle event of a role, played at the member's feet. The original draws the
	/// world random once per idle or walking turn and plays at 1 in 16 (most samples of these events are blanks, so
	/// a drawn sound is usually silence — the original's own chatter rate).
	/// </summary>
	// [BIN:STP-PPC:0x100D2CE0 staff voices] the entertainer's handler draws the world random each idle/walking turn and at (draw & 0xF) == 0 plays cat_staff (manager +16) 164 (walking) or 165 (idle) through the wrapper 0x100E6D20 at the member's position; the performance state (14) plays 135 at 0x100D2D98
	// [BIN:STP-PPC:0x100D5EC0..0x100F0630 all five kinds] walking 160/162/164/166/168 and idle 161/163/165/167/169 (handyman, mechanic, entertainer, guard, researcher), a researching turn 138, a performance's end 135, a guard's chase 136 and catch 137; the fork's PC reading (maexah/OpenTPW docs/exe/audio.md) is a lead only
	public static AudioVoice? PlayStaffSound( StaffType type, bool walking, Vector3 position )
	{
		var eventId = StaffSoundEvent( type, walking );
		return eventId == null ? null : events?.PlayAt( Staff, eventId.Value, AudioChannel.Effects, position );
	}

	/// <summary>
	/// The staff voices of one park turn: each member draws once, and a draw whose low four bits are clear plays its
	/// walking or idle voice at its cell (sound units: a cell is ten). Runs once per turn with an independent host stream.
	/// </summary>
	// [BIN:STP-PPC:0x100D2CE0 staff voices] the entertainer handler draws the world random at each walking/idle turn and plays at (draw & 0xF) == 0 — the gate is traced for all roles (0x100D5EAC, 0x100D6434, 0x100D9BAC, 0x100D9F10, 0x100D2DDC, 0x100D2E44, 0x100D4EF0, 0x100D4E18, 0x100F0460, 0x100F0630)
	// [APPROX:AUDIO-024] the turn is the park turn; draws advance an independent host SplitMix64 stream seeded from the sound seed XOR ASCII STAF, not the Mac's shared world LCG; walking is the travelling flag — evidence needed: the handlers' turn boundary and world draw order
	public static void UpdateStaffVoices( IReadOnlyList<StaffWorldSnapshot> staff, long turn )
	{
		if ( events == null || turn == staffTurn )
			return;
		staffTurn = turn;
		foreach ( var member in staff )
		{
			if ( staffRandom.Next( 16 ) != 0 )
				continue;
			if ( !staffVoiceLogged )
			{
				staffVoiceLogged = true;
				Log.Trace( "Sound: staff voices playing (cat_staff walking and idle, 1 in 16 per member per turn)." );
			}
			PlayStaffSound( member.Type, member.Travelling, new Vector3( member.CellX * 10, 0, member.CellY * 10 ) );
		}
	}

	/// <summary>The cat_staff event of a role's walking or idle turn, or null when none is traced.</summary>
	public static uint? StaffSoundEvent( StaffType type, bool walking ) => (type, walking) switch
	{
		(StaffType.Handyman, true) => 160,
		(StaffType.Handyman, false) => 161,
		(StaffType.Mechanic, true) => 162,
		(StaffType.Mechanic, false) => 163,
		(StaffType.Entertainer, true) => 164,
		(StaffType.Entertainer, false) => 165,
		(StaffType.Guard, true) => 166,
		(StaffType.Guard, false) => 167,
		(StaffType.Researcher, true) => 168,
		(StaffType.Researcher, false) => 169,
		_ => null,
	};

	/// <summary>The path tool's sounds: laying (<see cref="PathLayEvent"/>), refused (<see cref="PathRefusedEvent"/>) and the link-end (<see cref="PathLinkEvent"/>).</summary>
	public static void PlayPathToolSound( uint eventId )
	{
		var voice = events?.Play( Ui, eventId, AudioChannel.Effects );
		Log.Trace( $"Sound: path tool cat_ui {eventId} {(voice == null ? "not available" : "played")}." );
	}

	/// <summary>
	/// The sound category an RSE <c>EVENT</c> instruction's type operand selects, or null for a type the traced switch
	/// does not route. Types 1/2 create particles (0x1009FD70/0x1009F968), not sounds. One row per traced route:
	/// the type, the category, the sound-manager offset the submit loads the category handle from and the submit's call site.
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

	/// <summary>The audio category selected by an RSE <c>EVENT</c> type; null for particles (1/2), the default (0) or the unsupported custom bank (10).</summary>
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

	/// <summary>Plays an RSE <c>EVENT</c> instruction's sound at the object's place (see <see cref="RseEventCategory"/>); returns the voice, or null when the type is untraced or nothing plays.</summary>
	public static AudioVoice? PlayRseEvent( int type, uint eventId, Vector3? position = null )
	{
		var sound = FindRseEvent( type, eventId );
		if ( sound == null || events == null )
			return null;
		// The traced switch submits every routed event at the object's position; the sound's own range applies when the object has one.
		return position == null ? events.Play( sound.Value.Category, eventId, AudioChannel.Effects ) : events.PlayAt( sound.Value.Category, eventId, AudioChannel.Effects, position.Value );
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

	/// <summary>Per frame: applies the option volumes, the music parameter, updates the listener and tops up the output.</summary>
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
		UpdateLobbyOneShots();
		events.Pump();
		mixer.Pump();
	}

	/// <summary>
	/// Places the listener for the frame: the host follows the fork's proposed camera eye/target midpoint,
	/// whose exact mapping is not independently verified. The place is given in the
	/// engine's coordinates (X/Y ground, Z up) and moved into the original's sound units (five sound units per
	/// engine unit at model scale 0.2), undoing the heightfield's centering for imported parks.
	/// </summary>
	// [LEAD:maexah/OpenTPW docs/exe/audio.md "The listener", PC 0x0054F9E4] camera eye/target midpoint; packed PC exe prevents local verification and the prior nine-digit Mac address was malformed and is withdrawn
	// [APPROX:AUDIO-021] the camera midpoint follows the fork's listener lead; the Mac listener's exact position and placement units are not traced together
	public static void UpdateListener( System.Numerics.Vector3 ear, System.Numerics.Vector3 forward, ModelHeightfield? heightfield = null )
	{
		const float soundUnitsPerEngineUnit = 1f / OriginalParkPlacement.ModelScale;
		var origin = heightfield == null ? System.Numerics.Vector2.Zero : OriginalParkPlacement.GetOrigin( heightfield );
		AudioListener.Update(
			new Vector3( (ear.X + origin.X) * soundUnitsPerEngineUnit, ear.Z * soundUnitsPerEngineUnit, (ear.Y + origin.Y) * soundUnitsPerEngineUnit ),
			new Vector3( forward.X, 0, forward.Y ) );
	}

	/// <summary>The original's sound units of an engine place (X/Y ground, Z up): x/z stay ground, the engine's Z becomes the sound height, × 5 at the model scale 0.2.</summary>
	public static Vector3 SoundPosition( System.Numerics.Vector3 engine ) =>
		new( engine.X / OriginalParkPlacement.ModelScale, engine.Z / OriginalParkPlacement.ModelScale, engine.Y / OriginalParkPlacement.ModelScale );

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
