namespace OpenTPW;

/// <summary>
/// What the ride scripts did with STARTSCREAM and STOPSCREAM while a park ran: how many started a chain, how many were refused because the ride still held one
/// (the original's "Started screaming without stopping first.", which leaves the held chain screaming unreferenced), and the band operand of each start.
/// </summary>
internal sealed class ScreamStatistics
{
	private readonly Dictionary<string, (int Starts, int Refusals, int Stops)> perRide = new();
	private readonly SortedDictionary<int, int> bands = new();

	public int Starts { get; private set; }
	public int Refusals { get; private set; }
	public int Stops { get; private set; }
	/// <summary>STARTSCREAM instructions by band operand (every one the script ran, refused or not).</summary>
	public IReadOnlyDictionary<int, int> Bands => bands;
	public IReadOnlyDictionary<string, (int Starts, int Refusals, int Stops)> PerRide => perRide;

	public void NoteStart( string? ride, int band, bool refused )
	{
		if ( refused ) Refusals++; else Starts++;
		bands[band] = bands.GetValueOrDefault( band ) + 1;
		var key = ride ?? "?";
		var (starts, refusals, stops) = perRide.GetValueOrDefault( key );
		perRide[key] = refused ? (starts, refusals + 1, stops) : (starts + 1, refusals, stops);
	}

	public void NoteStop( string? ride )
	{
		Stops++;
		var key = ride ?? "?";
		var (starts, refusals, stops) = perRide.GetValueOrDefault( key );
		perRide[key] = (starts, refusals, stops + 1);
	}

	/// <summary>One line per fact for the gates' output: totals, the band histogram and every ride that ran a scream instruction.</summary>
	public string Summary()
	{
		var text = new System.Text.StringBuilder();
		text.AppendLine( $"Scream diagnostics: {Starts} STARTSCREAM started a chain, {Refusals} refused (leaked chains), {Stops} STOPSCREAM stopped one." );
		text.AppendLine( $"  STARTSCREAM bands (operand: count): {string.Join( ", ", bands.Select( pair => $"{pair.Key}: {pair.Value}" ) )}" );
		foreach ( var (name, counts) in perRide.OrderBy( pair => pair.Key, StringComparer.Ordinal ) )
			text.AppendLine( $"  {name}: {counts.Starts} starts, {counts.Refusals} refused, {counts.Stops} stops" );
		return text.ToString();
	}

	public void Reset()
	{
		Starts = Refusals = Stops = 0;
		bands.Clear();
		perRide.Clear();
	}
}

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
	/// <summary>The crowd sentence's parameter code (cat_kids 0x5B's event code).</summary>
	public const byte CrowdParameterCode = 7;
	/// <summary>The park music's parameter code (<c>0x51E790</c>: 0x51BC40(handle, 4, value)).</summary>
	public const byte MusicParameterCode = 4;
	/// <summary>The level rain voice's parameter code (<c>0x51E830</c>: 0x51BC40(handle, 8, level); cat_ambient 0x21's event code, its sounds bind it under the volume mask).</summary>
	public const byte RainParameterCode = 8;
	/// <summary>The park music event the original starts when a park opens (<c>0x100BC144</c>: event 2 of the level's music category).</summary>
	public const uint ParkMusicEvent = 2;
	/// <summary>The music parameter's cap (<c>0x101C2448</c>: values above 0x59 become 0x59).</summary>
	public const int MaximumMusicParameter = 89;
	/// <summary>The sound the application plays once at start-up (<c>cat_ui</c> 0xD2, submitted flat by the start-up initialisation <c>0x54dcf0</c> at <c>0x54de6a</c>-<c>0x54de7d</c>, right after it registers the global categories).</summary>
	// [BIN:TPW-EXE:0x54de6a start-up sound] WinMain (0x45a960, call at 0x45abed) runs the start-up init 0x54dcf0, which registers the global categories (0x51eae0) and submits cat_ui event 210 (a one-shot, flags 0x200) flat
	public const uint StartupEvent = 0xD2;
	private static bool startupPlayed;

	/// <summary>
	/// Plays the start-up sound (<see cref="StartupEvent"/>) once per process, when the front end first shows (a real application start;
	/// a run that loads a level directly never plays it over the park music); null when already played, or when sound or the event is missing.
	/// </summary>
	internal static AudioVoice? PlayStartupSound()
	{
		if ( startupPlayed || events == null )
			return null;
		startupPlayed = true;
		return StartupVoice = events.Play( Ui, StartupEvent, AudioChannel.Effects );
	}

	/// <summary>The click every control activation plays (<c>0x10137FD0</c>: a 0x100 message from a control plays <c>cat_ui</c> 0x1F).</summary>
	public const uint ClickEvent = 0x1F;
	/// <summary>
	/// The second UI click (<c>cat_ui</c> 0xBD). The hook <c>0x485780</c> plays this instead of <see cref="ClickEvent"/> when the
	/// receiver of message 0x100 has style bit 0x10 (the choice at <c>0x4857cb</c>).
	/// </summary>
	public const uint SelectClickEvent = 0xBD;
	/// <summary>The error feedback of the buy window and the dialogs (<c>0x10139A40</c>: <c>cat_ui</c> 0x1D; called from the buy window at <c>0x10163AFC</c>/<c>0x10163BFC</c> and the checkbox dialog at <c>0x1013B7A4</c>).</summary>
	public const uint ErrorEvent = 0x1D;
	/// <summary>The map/research drawer's open sound (<c>0x1015644C</c> message 0x0b: <c>cat_ui</c> 0x1C); the PC drawer (<c>0x4a14f0</c>) plays it again when a close starts (message 0xB, <c>0x4a186f</c>).</summary>
	public const uint DrawerOpenEvent = 0x1C;
	/// <summary>The map/research drawer's close sound (<c>0x1015644C</c> message 0x10 finishing state 3 plays <c>cat_ui</c> 0xC2 through the play-by-id helper <c>0x101383F8</c> at <c>0x101568DC</c>).</summary>
	public const uint DrawerCloseEvent = 0xC2;
	/// <summary>
	/// The drawer closing: <c>cat_ui</c> 0x1C when the close starts, then 0xC2 when it completes. The PC's drawer window procedure (<c>0x4a14f0</c>) plays 0x1C on message 0xB (<c>0x4a186f</c>)
	/// and 0xC2 when its slide timer finishes a state-3 close (<c>0x4a1947</c>); OpenTPW has no slide, so the two follow at once.
	/// </summary>
	// [BIN:TPW-EXE:0x4a186f drawer close] message 0xB sets state 3 (4 when wparam is -1) and plays cat_ui 0x1C; [BIN:TPW-EXE:0x4a1947] the finished state-3 slide (timer 0x80080, about ten 20 ms steps) plays 0xC2, a state-4 close posts 0xB to the parent instead; 0x1C and 0xC2 have no other callers
	public static void PlayDrawerClose()
	{
		PlayUi( DrawerOpenEvent );
		PlayUi( DrawerCloseEvent );
	}

	/// <summary>The crowd event (<c>0x100BC210</c>/<c>0x100BC2E0</c>: <c>cat_kids</c> 0x5B, a branching sentence).</summary>
	public const uint CrowdEvent = 0x5B;
	/// <summary>The crowd parameter's cap (<c>0x101C2498</c> and <c>0x100BC238</c>: values above 100 become 100).</summary>
	public const int MaximumCrowdParameter = 100;
	/// <summary>The lobby music event (<c>0x10095A24</c>: event 1 of the selected level's <c>cat_locallobbymusic</c>).</summary>
	public const uint LobbyMusicEvent = 1;
	/// <summary>The looping lobby ambience event (<c>0x10095A50</c>: event 2 of the selected level's <c>cat_locallobbysfx</c>).</summary>
	public const uint LobbyAmbienceEvent = 2;
	/// <summary>The lobby's thunder (<c>cat_globallobbysfx</c> 1, with the lobby lightning).</summary>
	public const uint LobbyThunderEvent = 1;
	/// <summary>The globe's music and ambience sentences (<c>cat_globallobbysfx</c> 2 and 3).</summary>
	public const uint GlobeMusicEvent = 2;
	public const uint GlobeAmbienceEvent = 3;
	/// <summary>The enter-park sound (<c>cat_globallobbysfx</c> 4).</summary>
	public const uint EnterParkEvent = 4;
	/// <summary>The scream-family sound a ride script's <c>REPAIREFFECT 0</c> plays by the script's band (tp.exe <c>0x55635F</c> → <c>0x551320</c>: <c>cat_kids</c>, families 75–78/79–82/83–86/87–90, index 0; silent for band 0).</summary>
	public const uint RepairBandEvent = 75;
	/// <summary>The first sound a ride script's non-zero <c>REPAIREFFECT</c> plays (tp.exe <c>0x5562CD</c>: event 0x6F of the global <c>cat_rides</c> at the object's position).</summary>
	public const uint RepairFirstEvent = 111;
	/// <summary>The second sound of a non-zero <c>REPAIREFFECT</c> (tp.exe <c>0x556340</c>: event 0xD3 of the global <c>cat_rides</c> at the object's position).</summary>
	public const uint RepairSecondEvent = 211;
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
	// [BIN:STP-PPC:0x10073D1C refused click] cat_ui 175 behind the latch test; [BIN:TPW-EXE:0x524a80] the release handler 0x524960 plays cat_ui 0xAF at (x*10, 0, z*10) of the clicked cell (0x524a80-0x524aae) before its tool switch, while tool 0x38 is off, the refusal latch (0x816d48) is set, no layout replay runs (0x785a2c == 0) and the park view's press handler ran since the last release (0x8186d8, PathRefusalClick)
	public const uint PathRefusedEvent = 175;
	/// <summary>
	/// The track tools' click: <c>cat_ui</c> 0xCE at the clicked cell (x*10, 0, z*10) for every release in the kart (0xB) and water (0x10) track modes whose ghost was not refused, the first click included.
	/// </summary>
	// [BIN:TPW-EXE:0x526cb3 track click] the release handler 0x524960 reaches 0x526cb3 from its mode tests 0xB (0x525a1c) and 0x10 (0x525a2e) and, unless a layout replays (0x785a2c), submits 0x51bfc0(0, cat_ui 0x803a2c, 0xCE, x*10, 0, z*10) at 0x526ce9 before the start/line logic; a release with the refused-ghost latch set never gets there: after the cue 0xAF (0x524aae) it leaves through the latch's own mode table (0x524ab6, table 0x52766c: 0xB and 0x10 to SetMode(0, 0) at 0x5266a3), so it plays no 0xCE (ParkHud.PlaysTrackToolClick)
	public const uint TrackToolClickEvent = 0xCE;
	/// <summary>
	/// The coaster tool's click: the path tool's laying sound <c>cat_ui</c> 101 at the clicked cell for every release in mode 0x15, building or editing.
	/// </summary>
	// [BIN:TPW-EXE:0x5263df coaster click] the release handler 0x524960 tests mode 0x15 at 0x5263e1 and, unless a layout replays (0x785a2c), submits cat_ui 0x65 at (x*10, 0, z*10) at 0x526423 before the coaster release 0x446300(0) and help 0xCD
	public const uint CoasterToolClickEvent = PathLayEvent;
	/// <summary>
	/// The pause menu's backdrop click: <c>cat_ui</c> 0xC1, flat.
	/// </summary>
	// [BIN:TPW-EXE:0x492d80 pause backdrop] the procedure of the pause menu's full-canvas window (made by 0x492e80 for the menu 0x48c150; parent callback 0x48b6a0) answers a click (message 0x10006) by playing 0xC1 through the cat_ui helper 0x485aa0 (flat, at the origin) and sending the activate message 0x100001 to its owner
	public const uint PauseBackdropEvent = 0xC1;
	/// <summary>Two coaster trains bumping hard: the global <c>cat_rides</c> crash sound 12 (the kart crunch).</summary>
	// [BIN:TPW-EXE:0x439840 coaster crash] the collision exchange 0x43cd60 calls the crash effect when the two trains' new speeds differ by more than 1.0 (0x6fe020): 0x51bfc0(0, cat_rides 0x803a28, 0xC, x, y, z) at 0x4398cd, the point the front of the train lies at on the track samples (fmod of +0x20 by the sample count, 0x43c000 interpolates the sample +4/+8/+0xC positions), each coordinate truncated
	public const uint CoasterCrashEvent = 12;
	/// <summary>
	/// A queue joined to a path: <c>cat_ui</c> 139 at the cell. The queue tool's commit plays it at the clicked end when the
	/// line's last cell lies on a path (the validator's code 8 sets the flag it reads), on every commit click including the
	/// ending one, while the park's money is on.
	/// </summary>
	// [BIN:STP-PPC:0x100711CC queue joins a path] queue mode (3), money on (game+36 == 0) and the flag 0x7C500 the validator 0x84154 sets when the queue line's last cell is a path cell: cat_ui 139 at (x·10, 0, y·10) after the ride is told its queue changed; [BIN:TPW-EXE:0x527587] the same
	// [BIN:TPW-EXE:0x00534A76 queue neighbour sound] the link writer's mode 1, with its refresh flag set and 0x785A2C clear, plays cat_ui 0x8B for a laid path cell whose cardinal queue neighbour's +0x0C links back to it (four sites: 0x534ACA, 0x534CDA, 0x534EE9, 0x5350F8), at (10x, 0, 10x): both coordinates are taken from the cell's x (0x5348E0 stores (id − 1) & 0x7F into both locals); PathLinkWriter reports it, QueueNeighbourSoundPosition places it
	public const uint QueueJoinEvent = 139;
	/// <summary>Backspace in the park view over a path cell, with no build tool on: <c>cat_ui</c> 95 at that cell, then the cell is removed.</summary>
	// [BIN:STP-PPC:0x10112430 backtrack key] the key command "backtrack" (Backspace, command table data 0x455B0): tool mode 0, mouse-mode state 0 or 1 and a path cell under the cursor play cat_ui 95 at (x·10, 0, y·10) before the single-cell removal (mode 0x32, build pass 0x10070B98, ClearCell's path case); [BIN:TPW-EXE:0x40BDA0] the same, its sound call at 0x40BE71
	public const uint BacktrackEvent = 95;
	/// <summary>The visitor click: <c>cat_kids</c> 129, flat, every time the visitor status panel is opened.</summary>
	// [BIN:STP-PPC:0x1010B808 visitor status panel open] stores the visitor, builds its dialog, plays cat_kids 129 (mode 0, no position) unconditionally and shows the panel; called by a park-view click on a visitor, a roster row activation, the map's visitor markers and the selection strip; [BIN:TPW-EXE:0x4B79B0] the same
	public const uint VisitorClickEvent = 129;
	/// <summary>A prankster's stinkbomb: <c>cat_kids</c> 207 at the prankster.</summary>
	// [BIN:TPW-EXE:0x004FF289 stinkbomb sound] after the stinkbomb's litter (0x004D93B0, type 8) the prank plays bank 0x00803A24 (cat_kids, loaded at 0x0051EBFE) event 0xCF through 0x004FAA00, which places it at the thing's model position (+0xC)
	public const uint StinkbombEvent = 207;
	private static SoundEventSystem? events;
	private static SoundFadeSettings fadeSettings = new();
	private static SoundSentence? music;
	private static SoundSentence? crowd;
	private static SoundSentence? lobbyMusic;
	private static SoundSentence? lobbyAmbience;
	private static SoundSentence? globeMusic;
	private static SoundSentence? globeAmbience;
	private static string? lobbyLevelBeforeGlobe;
	private static SoundSentence? rain;
	private static SoundSentence? weatherRain;
	private static List<SoundSentence>? placedSentences;
	private static long staffTurn = -1;
	private static DeterministicRandom staffRandom = new( 1 );
	private static bool staffVoiceLogged;
	private static IReadOnlyList<PlacedSound>? placedAmbients;

	private static bool enabled = true;
	public static bool Enabled
	{
		get => enabled;
		set { enabled = value; if ( !value ) events?.HeldSounds.StopAll(); }
	}
	internal static int ResolveSoundQuality( GameOptions options ) => fadeSettings.Resolve( options.SoundQuality );
	/// <summary>The start-up sound's voice (<see cref="StartupEvent"/>), played once when the service first starts; null when <c>cat_ui</c> or the event is missing.</summary>
	public static AudioVoice? StartupVoice { get; private set; }
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
		// Clock seed like the original's start-up seeding (tp.exe 0x72e09c, QueryPerformanceCounter); it only colours front-end sounds, since EnterPark reseeds from the park's world seed ([EXT:sound-chooser-seed]).
		events = new SoundEventSystem( mixer, (uint)Environment.TickCount );
		mixer.DuckingLevel = ReadDuckingLevel() / 100f;
		fadeSettings = ReadFadeSettings();
		events.HeldSounds.Configure( fadeSettings, GameOptions.Current.SoundQuality );
		mixer.MusicDuckRequest = MusicDuckRequest;
		ResetReverb();
		if ( ReverbMap == null )
		{
			// The browser has no System.Configuration store; an optional reverb map must not stop startup.
			var gamePath = LegacySettings.GamePath;
			if ( !string.IsNullOrEmpty( gamePath ) )
				LoadReverbMap( gamePath );
		}
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

	private static SoundFadeSettings ReadFadeSettings()
	{
		try
		{
			return FileSystem.FileExists( "/sound.sam" )
				? SoundFadeSettings.Read( new SamSettings( new[] { SamDocument.Parse( FileSystem.ReadAllText( "/sound.sam" ), "/sound.sam" ) } ) ) : new();
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or FormatException )
		{
			Log.Warning( $"Sound fade settings ignored: {exception.Message}" );
			return new();
		}
	}

	/// <summary>Loads a level's categories and starts its park music.</summary>
	/// <param name="seed">The park's sound seed (<see cref="WorldSeed.SoundStream"/>); null keeps the running chooser.</param>
	/// <summary>Bytes of the decoded samples the sound events keep (<see cref="SoundEventSystem.DecodedBytes"/>).</summary>
	public static long DecodedSampleBytes => events?.DecodedBytes ?? 0;

	/// <summary>What the sound system holds (<see cref="SoundEventSystem.Counts"/>); null before it starts.</summary>
	public static SoundSystemCounts? Counts => events?.Counts();

	public static void EnterPark( string level, uint? seed = null )
	{
		if ( !EnsureStarted() )
			return;
		LeaveLobby();
		LeavePark();
		SoundCadence.Reset();
		if ( seed is uint value )
			events!.Reseed( value );
		staffRandom = new DeterministicRandom( events!.Seed ^ 0x53544146u );
		LevelMusic = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Music", "cat_music" );
		LevelSpeech = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Speech", "cat_speech" );
		// [BIN:STP-PPC:0x100BC864 level registration] the level's cat_ambient and cat_rides registered at sound-manager offsets 24 and 28
		LevelAmbient = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Sound", "cat_ambient" );
		LevelRides = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Sound", "cat_rides" );
		music = events!.StartSentence( LevelMusic, ParkMusicEvent, AudioChannel.Music );
		StartPlacedAmbients( level );
		Log.Trace( $"Sound: {level} music {(music == null ? "not available" : $"{music.Event.Sounds.Count} sections")}, {LevelRides?.Catalog.Events.Count ?? 0} level ride events, {LevelAmbient?.Catalog.Events.Count ?? 0} level ambient events." );
	}

	/// <summary>
	/// The music duck request word (the original's 0x803AD0): a ride script's DIPMUSIC sets it, and while it is non-zero the music channel is silent
	/// (<see cref="AudioMixer.MusicDuckRequest"/>). The scripts that set it write 0 again when they end.
	/// </summary>
	public static ushort MusicDuckRequest { get; private set; }

	/// <summary>Sets the music duck request (tp.exe <c>0x51E710</c>). Presentation only: it is neither saved nor part of any state digest.</summary>
	public static void SetMusicDuck( ushort request )
	{
		MusicDuckRequest = request;
		if ( AudioMixer.Current is { } mixer )
			mixer.MusicDuckRequest = request;
	}

	/// <summary>The reverb preset the sound service holds (the original's 0x803AB4); -1 until the first <see cref="SelectReverb"/>.</summary>
	public static int ReverbPreset { get; private set; } = -1;

	/// <summary>The presets of <c>SndReverb.map</c>, when the host loaded the file from the game folder (it is not part of the mounted data); the mixer's ReverbUnit follows the held preset (the audio-reverb extension).</summary>
	public static SndReverbMap? ReverbMap { get; set; }

	/// <summary>The record of the held preset, when the map has one (the original refuses a preset the map lacks).</summary>
	public static IReadOnlyList<uint>? ReverbRecord => ReverbMap?.Record( ReverbPreset );

	/// <summary>Counts the presets sent to the reverb device (the original sends one only when the preset changes).</summary>
	public static int ReverbChanges { get; private set; }

	/// <summary>
	/// Holds a reverb preset (tp.exe <c>0x51C8D0</c>): a preset other than the held one is sent to the reverb device, the same one again is not.
	/// </summary>
	// [BIN:TPW-EXE:0x51C8D0 reverb preset] when the sound service is up and the preset differs from the held one (0x803AB4, -1 at start, 0 once the service started with sound on) the held one is replaced and the preset sent as a mixer command (0x700B30 → 0x6C6000, which fetches the SndReverb.map record and refuses an absent one)
	// [EXT:audio-reverb] the held preset's record drives ReverbUnit, OpenTPW's own network on the positioned effects by design: the original hands the record to the audio device's EAX 2.0 listener property set or to a COM object behind QMixer.dll (which holds no reverb DSP), whose sound lives in the device or driver
	public static void SelectReverb( int preset )
	{
		if ( preset == ReverbPreset )
			return;
		ReverbPreset = preset;
		ReverbChanges++;
		ApplyReverb();
	}

	/// <summary>
	/// Hands the held preset's record to the mixer's reverb (none for a preset the map lacks, or without a map).
	/// </summary>
	// [EXT:audio-reverb] preset 0 (the default everywhere outside a ridden ride that sets a level) stays dry: its record asks for the device's full room level, which this stand-in would put on every positioned sound of the park; only the ride presets (1-14) are mixed
	private static void ApplyReverb() => AudioMixer.Current?.Reverb.Set( ReverbPreset > 0 ? ReverbSettings.FromRecord( ReverbRecord ) : null );

	/// <summary>
	/// Loads <c>SndReverb.map</c> from the game folder (it sits beside <c>Data</c>, not inside it, so the mounted data does not hold it); a missing or damaged file leaves the reverb off.
	/// </summary>
	// [BIN:TPW-EXE:0x6C5D20 SndReverb.map loader] the sound service reads the file once while it starts
	public static void LoadReverbMap( string gameRoot )
	{
		try
		{
			if ( GameLanguage.FindEntry( gameRoot, "SndReverb.map", false ) is { } path )
			{
				ReverbMap = SndReverbMap.Read( File.ReadAllBytes( path ) );
				ApplyReverb();
			}
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"SndReverb.map ignored: {exception.Message}" );
		}
	}

	/// <summary>Forgets the held preset (a new sound service starts at -1).</summary>
	public static void ResetReverb()
	{
		ReverbPreset = -1;
		ReverbChanges = 0;
	}

	public static void LeavePark()
	{
		// [BIN:TPW-EXE:0x5C8650 park exit] state 12 of the park state machine 0x54E360 writes the profile (0x5AFC60 -> 0x5AFF50 -> 0x423E90), which ends in the options applier 0x423DD0
		EndSavedVolumes();
		events?.HeldSounds.StopAll();
		// the park's scripts are destroyed with it, and each writes the duck request 0 (tp.exe 0x558513); the reverb returns to the default at the next frame outside a ride
		SetMusicDuck( 0 );
		var (leftMusic, leftSpeech, leftAmbient, leftRides) = (LevelMusic, LevelSpeech, LevelAmbient, LevelRides);
		music?.Stop();
		music = null;
		LevelMusic = null;
		LevelSpeech = null;
		LevelAmbient = null;
		LevelRides = null;
		SetCrowd( 0 );
		SetRain( 0 );
		SetWeatherRain( false );
		StopPlacedAmbients();
		events?.StopChains();
		events?.Forget( leftMusic, leftSpeech, leftAmbient, leftRides );
		staffTurn = -1;
		staffVoiceLogged = false;
	}

	/// <summary>
	/// Starts the land's own ambient sounds: the type 1 records of the level's <c>scape.omp</c>, each an effect of the
	/// global <c>cat_ambient</c> (word 1 nought) or of the level's own category, played at its place with its range
	/// so the mixer's distance law turns it down. Type 3 records (a sound that follows the listener inside a rectangle)
	/// start at the listener's nearest point of the rectangle and move with it every frame (<see cref="FollowRectangleSounds"/>).
	/// </summary>
	// [DATA:levels/*/scape.omp] type 1 records: word 1 the category (nought: global cat_ambient), word 2 the effect, words 3-5 the place, word 6 the range (x2/1024)
	// [BIN:TPW-EXE:0x550e00 scape.omp loader] type 1 records go to the placed submit 0x51c130(0, global or level ambient, effect, x/1024, y/1024, z/1024, range*2/1024), each coordinate divided toward zero (0x550f1c-0x550f4d): the same play message as 0x51bfc0 plus a range, so the library picks its player from the event flags and a sentence plays as a positioned sentence; the returned handle is discarded; type 3 records submit 0x51bfc0 and then bind a rectangle (0x51c5d0)
	// [BIN:TPW-EXE:0x54f8f6 units] placements share the int world units of every submit (cell*10 for tool sounds, footprint*10 for screams, scape words /1024 truncated toward zero, range*2/1024) with the listener's place
	public static void StartPlacedAmbients( string level )
	{
		StopPlacedAmbients();
		var path = $"/levels/{level}/scape.omp";
		if ( events == null || !FileSystem.FileExists( path ) )
			return;
		var file = new PlacedObjectsFile( FileSystem.ReadAllBytes( path ) );
		var voices = new List<PlacedSound>();
		var sentences = new List<SoundSentence>();
		var type3 = 0;
		var rectangles = new List<RectangleSound>();
		foreach ( var record in file.Records )
		{
			if ( record.Type == PlacedObjectsFile.RectangleSoundType )
			{
				type3++;
				StartRectangleSound( record, rectangles );
				continue;
			}
			if ( record.Type != PlacedObjectsFile.RangedSoundType )
				continue;
			var category = record.Category == 0 ? Ambient : LevelAmbient;
			var (x, height, z) = record.SoundPlace;
			var place = new Vector3( x, height, z );
			var range = record.Range > 0 ? record.Range : (float?)null;
			// A placed sentence plays as a positioned sentence for the park's lifetime; the placed submit's handle is discarded (nothing stops it but the park's end).
			if ( category?.Catalog.Find( (uint)record.Sound ) is { Player: not SoundEventPlayer.OneShot } )
			{
				if ( events.StartSentence( category, (uint)record.Sound, AudioChannel.Effects, null, place, range ) is { } sentence )
					sentences.Add( sentence );
				continue;
			}
			// Each starts once its sample is decoded, so the park's first frame does not wait for them (docs/BROWSER-PERFORMANCE.md).
			var voice = events.PlayAtWhenDecoded( category, (uint)record.Sound, AudioChannel.Effects, place, range );
			if ( voice != null )
				voices.Add( voice );
		}
		placedAmbients = voices;
		placedSentences = sentences;
		rectangleSounds = rectangles;
		Log.Trace( $"Sound: {level} placed ambients {voices.Count} starting, {sentences.Count} placed sentences, {type3} rectangle sounds ({rectangles.Count} started, scape.omp type 3)." );
	}

	private static List<RectangleSound>? rectangleSounds;

	/// <summary>One type 3 record's sound: its rectangle (x from <c>X1</c> up to <c>X2</c>; z down from <c>Z1</c> to <c>Z2</c>) and whichever player it started.</summary>
	private sealed record RectangleSound( int X1, int Z1, int X2, int Z2, PlacedSound? Placed, SoundSentence? Sentence );

	/// <summary>
	/// The place a rectangle sound plays at for a listener at <paramref name="listener"/>: the listener's place clamped into the rectangle on x and z, its height kept.
	/// </summary>
	// [BIN:TPW-EXE:0x6bd5f0 rectangle clamp] every tick of a voice with flag 0x1000 (set by the bind 0x6b64b0, never cleared; 0x6bca10 tests it before the range test) the voice is put at the listener's place (listener object +0x60) clamped: x below x1 becomes x1 and above x2 becomes x2; z above z1 becomes z1 and, otherwise, below z2 becomes z2 (the shipped records run z down from z1, their extent word being negative); y is the listener's
	public static Vector3 RectangleSoundPlace( int x1, int z1, int x2, int z2, Vector3 listener )
	{
		var x = listener.X < x1 ? x1 : listener.X > x2 ? x2 : listener.X;
		var z = listener.Z > z1 ? z1 : listener.Z < z2 ? z2 : listener.Z;
		return new Vector3( x, listener.Y, z );
	}

	// [BIN:TPW-EXE:0x550e00 scape.omp type 3] the loader submits 0x51bfc0(0, global (word 1 nought) or level cat_ambient, word 2, words 3-5 / 1024) and binds the returned handle with 0x51c5d0(handle, x, z, w7 / 1024, w8 / 1024): the voice then plays at the clamp of the listener's place into [x, x + w7] by [z, z + w8] (RectangleSoundPlace)
	private static void StartRectangleSound( PlacedObjectsFile.Record record, List<RectangleSound> started )
	{
		var category = record.Category == 0 ? Ambient : LevelAmbient;
		var ((x1, z1), (x2, z2)) = record.Rectangle;
		var place = RectangleSoundPlace( x1, z1, x2, z2, AudioListener.Position );
		PlacedSound? placed = null;
		SoundSentence? sentence = null;
		if ( category?.Catalog.Find( (uint)record.Sound ) is { Player: not SoundEventPlayer.OneShot } )
			sentence = events!.StartSentence( category, (uint)record.Sound, AudioChannel.Effects, null, place, null );
		else
			placed = events!.PlayAtWhenDecoded( category, (uint)record.Sound, AudioChannel.Effects, place );
		if ( placed != null || sentence != null )
			started.Add( new RectangleSound( x1, z1, x2, z2, placed, sentence ) );
	}

	/// <summary>Moves every rectangle sound to the listener's clamped place (once a frame, before the voices are pumped).</summary>
	private static void FollowRectangleSounds()
	{
		if ( rectangleSounds is not { Count: > 0 } sounds )
			return;
		var listener = AudioListener.Position;
		foreach ( var sound in sounds )
		{
			var place = RectangleSoundPlace( sound.X1, sound.Z1, sound.X2, sound.Z2, listener );
			sound.Placed?.MoveTo( place );
			sound.Sentence?.MoveTo( place );
		}
	}

	/// <summary>Stops the land's own ambient voices (the level's <c>scape.omp</c> records).</summary>
	public static void StopPlacedAmbients()
	{
		if ( rectangleSounds != null )
		{
			foreach ( var sound in rectangleSounds )
			{
				sound.Placed?.Stop();
				sound.Sentence?.Stop();
			}
			rectangleSounds = null;
		}
		if ( placedSentences != null )
		{
			foreach ( var sentence in placedSentences )
				sentence.Stop();
			placedSentences = null;
		}
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

	public static void LeaveLobby()
	{
		events?.Forget( LobbyMusic, LobbySfx );
		lobbyMusic?.Stop();
		lobbyAmbience?.Stop();
		lobbyMusic = null;
		lobbyAmbience = null;
		LobbyMusic = null;
		LobbySfx = null;
		lobbyLevel = null;
		// Leaving the front end (or the island's audio switching) also ends the globe's sentences.
		globeMusic?.Stop();
		globeAmbience?.Stop();
		globeMusic = null;
		globeAmbience = null;
	}

	/// <summary>An island one-shot of the lobby (event 3, 4 or 5 of the island's <c>cat_locallobbysfx</c>), drawn by <see cref="LobbyEffects"/>.</summary>
	// [BIN:STP-PPC:0x100953F8-0x10095450 lobby one-shots] cat_locallobbysfx 3, 4 and 5 play beside the looping ambience (event 2), picked by the islands update's draws
	public static void PlayLobbyOneShot( uint eventId )
	{
		if ( events == null || lobbyAmbience == null )
			return;
		events.Play( LobbySfx, eventId, AudioChannel.Effects );
	}

	/// <summary>The lobby thunder: <c>cat_globallobbysfx</c> 1, flat, at the strike (no delay).</summary>
	public static void PlayLobbyThunder()
	{
		var voice = events?.Play( GlobalLobbySfx, LobbyThunderEvent, AudioChannel.Effects );
		Log.Trace( $"Sound: lobby thunder cat_globallobbysfx {LobbyThunderEvent} {(voice == null ? "not available" : "played")}." );
	}

	/// <summary>The enter-park sound: <c>cat_globallobbysfx</c> 4, flat, as the camera flies into the island.</summary>
	// [BIN:STP-PPC:0x10183CC0 enter park] the theme islands' enter action sets the fly-in camera state and plays cat_globallobbysfx 4 (keyexplode, volume 100) flat, except when the game type that follows is 2 (Instant Action; FrontEndMenu.EnterParkSoundPlays; the PC enter routine 0x4b8fd0 skips it the same way while the game-mode global 0xfb3b7c is 2)
	public static void PlayEnterPark()
	{
		var voice = events?.Play( GlobalLobbySfx, EnterParkEvent, AudioChannel.Effects );
		Log.Trace( $"Sound: enter park cat_globallobbysfx {EnterParkEvent} {(voice == null ? "not available" : "played")}." );
	}

	/// <summary>Starts the globe's music (<c>cat_globallobbysfx</c> 2) and ambience (3) as looping sentences while the online globe is up.</summary>
	// [BIN:STP-PPC:0x100923A8 globe audio] the globe component starts cat_globallobbysfx 2 (globe music) and 3 (globe ambience) and keeps both handles; closing stops them by handle (0x1009261C)
	// [EXT:online-globe-audio] the globe is the online extension's screen: its music and ambience replace the lobby's, which OpenTPW stops while the globe plays and restarts after it
	public static void EnterGlobe()
	{
		if ( events == null || globeMusic != null )
			return;
		var level = lobbyLevel;
		LeaveLobby();
		lobbyLevelBeforeGlobe = level;
		globeMusic = events.StartSentence( GlobalLobbySfx, GlobeMusicEvent, AudioChannel.Music );
		globeAmbience = events.StartSentence( GlobalLobbySfx, GlobeAmbienceEvent, AudioChannel.Effects );
	}

	/// <summary>Stops the globe's sounds and brings the lobby's back.</summary>
	public static void LeaveGlobe()
	{
		globeMusic?.Stop();
		globeAmbience?.Stop();
		var hadGlobe = globeMusic != null || globeAmbience != null;
		globeMusic = null;
		globeAmbience = null;
		if ( hadGlobe && lobbyLevelBeforeGlobe is { } level )
			EnterLobby( level );
		lobbyLevelBeforeGlobe = null;
	}
	/// <summary>
	/// The crowd sentence: starts <c>cat_kids</c> 0x5B when guests are near the camera, follows the count as the
	/// sentence's parameter and stops when nobody is. The original's park turn counts guests within four cells of the
	/// camera, caps the count at 100, and starts/updates/stops the sentence with it.
	/// </summary>
	// [BIN:STP-PPC:0x101C2490 park turn] guests within four cells of the camera are counted (0x100C313C, radius argument 4) and capped at 100 (0x101C2498)
	// [BIN:STP-PPC:0x100BC210 crowd update] a count above 0 starts cat_kids event 0x5B and sets its parameter; 0 stops it (the start/stop wrapper is 0x100BC2E0)
	/// <summary>The world-step gate of the park's music and crowd parameter updates (<see cref="WorldStepCadence"/>).</summary>
	public static WorldStepCadence SoundCadence { get; } = new();

	/// <summary>
	/// Advances the park's world steps by the game timer's <paramref name="elapsedMilliseconds"/> (0 while paused) and runs the crowd update
	/// (<see cref="SetCrowd"/>) once for every 32nd step, as the original's main loop does; the count is read only when an update falls due.
	/// </summary>
	public static void UpdateCrowd( double elapsedMilliseconds, Func<int> guestsNearCamera, bool parkLost = false )
	{
		ArgumentNullException.ThrowIfNull( guestsNearCamera );
		for ( var due = SoundCadence.Advance( elapsedMilliseconds ); due > 0; due-- )
			// [BIN:TPW-EXE:0x0054F8BB crowd in a lost park] world state 4 (a park the banks closed) sends 0 instead of the count (0x0054F8B0 compares +0x1DA738 with 4, 0x0054F8BD zeroes eax), which stops the crowd sentence
			SetCrowd( parkLost ? 0 : guestsNearCamera() );
	}

	public static void SetCrowd( int guestsNearCamera )
	{
		if ( events == null )
			return;
		var parameter = Math.Clamp( guestsNearCamera, 0, MaximumCrowdParameter );
		if ( parameter > 0 )
		{
			if ( crowd == null )
			{
				crowd = events.StartSentence( Kids, CrowdEvent, AudioChannel.Effects );
				if ( crowd != null )
					Log.Trace( $"Sound: crowd sentence started ({guestsNearCamera} guests within {GameFlow.CrowdRadiusCells} cells of the camera)." );
			}
			if ( crowd != null )
			{
				// [BIN:STP-PPC:0x100BC298–0x100BC29C crowd update] the capped nearby count is sent with parameter code 7 to 0x100BAF70
				// [BIN:TPW-EXE:0x0051E7FC crowd update] the PC sends it on every update through 0x51BC40(handle, 7, count); code 7 is the crowd event's own code (cat_kids 0x5B, event +0x12 = 7) and sounds 1-6 of the twelve bind it in slot 1 under the volume mask (selector +0x16 = 7, mask +0x18 = 1), so while one of them plays each update re-evaluates its volume (14 + 8 * count / 100) and redraws its pitch (0..6); sounds 7-12 have selector 0 and are untouched
				crowd.SetParameter( CrowdParameterCode, parameter );
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
	/// starts a second, unmodulated voice of its own (<see cref="SetWeatherRain"/>).
	/// </summary>
	// [BIN:STP-PPC:0x100BC388 rain level] above 0 the global rain voice (cat_ambient 0x21, submitted flat) is started once and given parameter 8 = the level; 0 stops it with 0x100BBA58
	// [BIN:STP-PPC:0x10103394 level source] the level is the weather thing's current drop count × 100 / MaxRaindrops (the Mac's rain tick; the fork's +0x34 target reading is refuted)
	// [BIN:TPW-EXE:0x6b87d0 event submit] the library has no per-event instance limit: a submit replaces only the live voice of the handle it carries (0x6b6830; refused while that voice's priority +0x22 >= the new event's +0xc, else the old voice is flagged 0x100, 0x6b88c0-0x6b88df), the thing submits its voice with handle 0 (0x51327c) while +0x54 is free, and the pool admits voices by rank alone (0x6b62b0, 0x6b6bc0), so both rain voices sound, as here
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
				// [BIN:TPW-EXE:0x0051E870 rain level] the level (clamped to 0..100) goes to the voice with 0x51BC40(handle, 8, level): the event's code and the volume selector of its sounds, so the level also sets the rain's volume (the shipped sections span 1 to 2..5 percent)
				rain.SetParameter( RainParameterCode, parameter );
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

	/// <summary>
	/// The weather thing's own rain voice: <c>cat_ambient</c> 0x21, flat and with no parameter, started while the weather mode is rain and stopped in snow or clear weather. It sounds
	/// beside the level-driven voice of <see cref="SetRain"/>, which keeps following the drop count in every weather (snow included) until the drops reach 0.
	/// </summary>
	// [BIN:TPW-EXE:0x5131f0 weather mode setter] mode 0 (rain) submits cat_ambient 0x21 (0x51327c) with no parameter into the thing's +0x54 (mRainSoundHandle, saved); mode 1 (snow) and mode 2 (clear) stop it
	// [BIN:TPW-EXE:0x006BC040 parameter query] with no parameter call its table still exists (the event's code 8 and the sounds' selector 8), so its sounds take their volume from a value of 0: the bottom of their range
	// [BIN:TPW-EXE:0x512a7f level rain voice] the level-driven voice 0x51e830 (parameter 8) is fed every tick from the current drops * 100 / MaxRaindrops, also in snow
	public static void SetWeatherRain( bool raining )
	{
		if ( events == null )
			return;
		if ( raining )
		{
			if ( weatherRain == null )
			{
				weatherRain = events.StartSentence( Ambient, RainEvent, AudioChannel.Effects );
				if ( weatherRain != null )
					Log.Trace( "Sound: the weather's own rain voice started (cat_ambient 0x21, flat)." );
			}
		}
		else if ( weatherRain != null )
		{
			weatherRain.Stop();
			weatherRain = null;
			Log.Trace( "Sound: the weather's own rain voice stopped (snow or clear)." );
		}
	}

	/// <summary>The weather thing's own rain voice while it rains (<see cref="SetWeatherRain"/>).</summary>
	public static SoundSentence? WeatherRain => weatherRain;

	/// <summary>The level-driven rain voice (<see cref="SetRain"/>).</summary>
	public static SoundSentence? RainVoice => rain;

	/// <summary>Plays the thunder one-shot at a strike's position (the original passes the top of the last bolt, height 300).</summary>
	// [BIN:STP-PPC:0x101034C8-0x10103518 thunder submit] when the weather thing's thunder countdown (+0x44) reaches 0, cat_ambient 0x20 is submitted at the last lightning location (+0x48..+0x50)
	public static AudioVoice? PlayThunder( Vector3 position )
	{
		var voice = events?.PlayAt( Ambient, ThunderEvent, AudioChannel.Effects, position );
		Log.Trace( $"Sound: thunder at ({position.X:0},{position.Y:0},{position.Z:0}) {(voice == null ? "not available" : "played")}." );
		return voice;
	}

	/// <summary>
	/// The chain of a ride script's held scream (<c>STARTSCREAM</c>, or a resumed park's held scream once its audio
	/// runs): the band operand picks the held-chain event, the level operand (with the script's speed) its parameter.
	/// Returns the chain, or null when the band is zero (nothing plays) or the sound service is off. Refusing a second
	/// start while a scream is held is the script's (<see cref="OriginalObjectEffects"/>), with or without audio.
	/// </summary>
	// [BIN:STP-PPC:0x100B19DC STARTSCREAM] operand 1 is the band itself (no visitor lookup), operand 2 the level; the handle is kept in the ride's VM at +208
	// [BIN:STP-PPC:0x100ACA38 scream event] band 0 plays nothing; 1 plays cat_kids 71; 2-3 play 72 (negative bands too); 4-7 play 73; 8 and above play 74
	// [BIN:STP-PPC:0x100ACB7C scream parameter] parameter = (level + speed) / 2 clamped to 0..100 (signed shift); the speed is the ride VM's word at +192, default 50 (0x100B3154, 0x100B318C)
	// [BIN:TPW-EXE:0x551130 STARTSCREAM submit] the held scream is positioned: cat_kids 0x47-0x4A at (x, y, z) with parameter 6 = clamp((a + b) / 2, 0, 100); the place is the ride's footprint middle (0x555edb -> 0x551130, SINGLESCREAM 0x555f1b -> 0x556af0 -> 0x551320; 0x466b70 reads the footprint rectangle at object +0xC0/+0xC4, scaled x10)
	// [BIN:TPW-EXE:0x5516b0 script speed word] the speed word is the script's speed bias (RideVM.SpeedBias, the ride's InitSpeed; default 50 at 0x558c3c), the same short the script loop pushes to the ride every pass
	public static SoundChain? StartScream( int band, int level, Vector3? position, int scriptSpeed = 50 )
	{
		if ( events == null )
			return null;
		if ( ScreamBandEvent( band ) is not { } eventId )
			return null;
		var parameter = ScreamParameter( level, scriptSpeed );
		var chain = events.StartChain( Kids, eventId, AudioChannel.Effects, parameter, position );
		if ( chain != null )
			Log.Trace( $"Sound: scream chain started (cat_kids {eventId}, parameter {parameter})." );
		return chain;
	}

	/// <summary>Counts of the scream instructions the ride scripts ran, for the leaked-chain and rider-band diagnostics (docs/AUDIO.md, "Scream diagnostics").</summary>
	internal static readonly ScreamStatistics Screams = new();

	/// <summary>The held-chain event a scream's band operand picks (Mac <c>0x100ACA38</c>: 0 nothing, 1 → 71, 2–3 → 72, 4–7 → 73, 8+ → 74), or null for band 0.</summary>
	public static uint? ScreamBandEvent( int band ) => band == 0 ? null : band == 1 ? 71u : band >= 8 ? 74u : band >= 4 ? 73u : 72u;

	/// <summary>A scream's parameter: (level + script speed) / 2 clamped to 0..100 (Mac <c>0x100ACB7C</c>; the speed word is the script's speed bias, default 50).</summary>
	public static int ScreamParameter( int level, int scriptSpeed = 50 ) => Math.Clamp( (level + scriptSpeed) / 2, 0, 100 );

	/// <summary>
	/// A ride script's <c>SINGLESCREAM</c>: placed at the ride's footprint middle (<paramref name="position"/>; flat only for a runtime without a placement). A non-negative second operand takes
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
	// [EXT:staff-voice-fallback] the original always has staff state machines, so voices without a staff world are OpenTPW's own mode: the turn is the park turn, draws advance an independent host SplitMix64 stream seeded from the sound seed XOR ASCII STAF, walking is the travelling flag; in a level every role's state machine draws its voices (PlayStaffVoices) from the world stream (the PC draws from the shared world LCG 0x516330, docs/DETERMINISM.md)
	public static void UpdateStaffVoices( IReadOnlyList<StaffWorldSnapshot> staff, long turn, bool worldDrawsVoices = false )
	{
		if ( events == null || turn == staffTurn )
			return;
		staffTurn = turn;
		foreach ( var member in staff )
		{
			if ( worldDrawsVoices )
				continue;
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

	/// <summary>The voices a staff state machine drew (the handymen's states 0 and 1, tp.exe <c>0x004D73C0</c>), each at the member's cell.</summary>
	// [BIN:TPW-EXE:0x004D73C0 handyman voices] states 0 and 1 draw the world generator on every update and at (draw & 0xF) == 0 play bank 0x00803A30 (cat_staff) 161 (idle) or 160 (walking) through 0x004FAA00
	public static void PlayStaffVoices( IReadOnlyList<StaffVoice> voices )
	{
		foreach ( var voice in voices )
		{
			var position = new Vector3( voice.CellX * 10, 0, voice.CellY * 10 );
			if ( voice.Event == 0 )
				PlayStaffSound( voice.Type, voice.Walking, position );
			else
				events?.PlayAt( voice.Kids ? Kids : Staff, voice.Event, AudioChannel.Effects, position );
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

	/// <summary>The path tool's sounds: laying (<see cref="PathLayEvent"/>) and refused (<see cref="PathRefusedEvent"/>).</summary>
	public static void PlayPathToolSound( uint eventId )
	{
		var voice = events?.Play( Ui, eventId, AudioChannel.Effects );
		Log.Trace( $"Sound: path tool cat_ui {eventId} {(voice == null ? "not available" : "played")}." );
	}

	/// <summary>Where a cell's sounds play: the original submits them at (x*10, 0, z*10) (a cell is ten units, height 0).</summary>
	public static Vector3 CellSoundPosition( int x, int y ) => new( x * 10, 0, y * 10 );

	/// <summary>
	/// Where the link writer's queue-neighbour sound (<see cref="QueueJoinEvent"/>) plays for the laid path cell (<paramref name="x"/>, <paramref name="y"/>):
	/// tp.exe takes both coordinates from the cell's x, so it plays at (10x, 0, 10x), not at the cell.
	/// </summary>
	public static Vector3 QueueNeighbourSoundPosition( int x, int y ) => new( x * 10, 0, x * 10 );

	/// <summary>The path tool's refused click at the clicked cell (<see cref="PathRefusedEvent"/>, <c>cat_ui</c> 175 at (x*10, 0, z*10); tp.exe <c>0x524a80</c>).</summary>
	public static void PlayPathRefused( int cellX, int cellY ) => PlayUiAt( PathRefusedEvent, CellSoundPosition( cellX, cellY ) );

	/// <summary>Test seam: told the event of every <see cref="PlayUiAt"/> sound, played or not.</summary>
	internal static Action<uint>? UiSoundObserver { get; set; }

	/// <summary>A <c>cat_ui</c> sound at a park position: the queue joining a path (<see cref="QueueJoinEvent"/>) and the backtrack (<see cref="BacktrackEvent"/>).</summary>
	public static void PlayUiAt( uint eventId, Vector3 position )
	{
		UiSoundObserver?.Invoke( eventId );
		var voice = events?.PlayAt( Ui, eventId, AudioChannel.Effects, position );
		Log.Trace( $"Sound: cat_ui {eventId} at a cell {(voice == null ? "not available" : "played")}." );
	}

	/// <summary>A global <c>cat_rides</c> sound at a park position (the coaster trains' crash, <see cref="CoasterCrashEvent"/>).</summary>
	public static void PlayRidesAt( uint eventId, Vector3 position )
	{
		var voice = events?.PlayAt( Rides, eventId, AudioChannel.Effects, position );
		Log.Trace( $"Sound: cat_rides {eventId} at a place {(voice == null ? "not available" : "played")}." );
	}

	/// <summary>A stinkbomb's sound (<see cref="StinkbombEvent"/>) at a park position.</summary>
	public static void PlayStinkbomb( Vector3 position )
	{
		var voice = events?.PlayAt( Kids, StinkbombEvent, AudioChannel.Effects, position );
		Log.Trace( $"Sound: stinkbomb cat_kids {StinkbombEvent} {(voice == null ? "not available" : "played")}." );
	}

	/// <summary>A guest's voice: a <c>cat_kids</c> event at the guest's position (tp.exe <c>0x004FAA00</c>, which places it at the thing's model position and plays it through <c>0x0051BFC0</c>).</summary>
	public static void PlayGuestVoice( uint eventId, Vector3 position )
	{
		var voice = events?.PlayAt( Kids, eventId, AudioChannel.Effects, position );
		Log.Trace( $"Sound: guest voice cat_kids {eventId} {(voice == null ? "not available" : "played")}." );
	}

	/// <summary>The visitor click (<see cref="VisitorClickEvent"/>), played whenever a visitor's details are opened.</summary>
	public static void PlayVisitorClick()
	{
		var voice = events?.Play( Kids, VisitorClickEvent, AudioChannel.Effects );
		Log.Trace( $"Sound: visitor click cat_kids {VisitorClickEvent} {(voice == null ? "not available" : "played")}." );
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

	/// <summary>The audio category selected by an RSE <c>EVENT</c> type; null for particles (1/2), the default (0) and the object type's own sound list (10, which belongs to the type: <see cref="ObjectCatalogEntry.OwnSounds"/>).</summary>
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
	/// The event an RSE <c>EVENT</c> instruction plays: the id is looked up in the routed category only, so an id the
	/// level category lacks plays nothing (<c>incagod.RSE</c>'s <c>EVENT 3 id 43</c>, which only the global
	/// <c>cat_rides</c> has, is silent in the original too).
	/// </summary>
	// [BIN:TPW-EXE:0x5575c7 RSE EVENT dispatch] the PC dispatcher submits type 3 to the level cat_rides (0x803a3c) and type 4 to the level cat_ambient with no handle check and no global retry (the submit 0x51bfc0 returns 0 for event id 0 or with sound off, 0x802bc8/0x802bd4; an empty category slot returns in 0x6b9b30)
	// [BIN:TPW-EXE:0x6b87d0 event lookup] the submit takes the category by its handle (0x6b9b30, a deque index; an empty slot returns) and searches only that category's 20-byte event records for the id (0x6c5870, linear, null when absent), returning without a voice (0x6b8919); the level category is loaded from the level's own data under its own path key (0x51ec50 -> 0x51b530 -> 0x6bf330, path joined at 0x6b8690), so nothing falls back to the global category
	// [BIN:TPW-EXE:0x557686 EVENT type 10] case 10 submits to the object type's own category (type record +0x4F0, 0x4145E0) the same way, so its events are looked up in that category only
	public static (SoundCategory Category, SoundEvent Event)? FindRseEvent( int type, uint eventId, RseSoundSource? source = null, SoundCategory? ownSounds = null )
	{
		// [EXT:cross-edition-objects] an object from another content root looks the event up in that root's categories only, with no fallback to the running game's
		var category = type == 10 ? ownSounds : source is { } other ? other.Category( type ) : RseEventCategory( type );
		var soundEvent = category?.Catalog.Find( eventId );
		return soundEvent == null || category == null ? null : (category, soundEvent);
	}

	/// <summary>Plays an RSE <c>EVENT</c> instruction's sound at the object's place (see <see cref="RseEventCategory"/>; type 10 plays from <paramref name="ownSounds"/>, the object type's own list); returns the voice, or null when the type is untraced or nothing plays.</summary>
	public static AudioVoice? PlayRseEvent( int type, uint eventId, Vector3? position = null, RseSoundSource? source = null, SoundCategory? ownSounds = null )
	{
		var sound = FindRseEvent( type, eventId, source, ownSounds );
		if ( sound == null || events == null )
			return null;
		// The traced switch submits every routed event at the object's position; the sound's own range applies when the object has one.
		return position == null ? events.Play( sound.Value.Category, eventId, AudioChannel.Effects ) : events.PlayAt( sound.Value.Category, eventId, AudioChannel.Effects, position.Value );
	}

	/// <summary>
	/// Starts an event for a script's <c>ADDOBJ</c> record (types 3-10): the same category lookup and submit as <see cref="PlayRseEvent"/>, but the sound is kept so the script can
	/// move, parameterise and end it; null when the event is missing or no sound system runs (the original's submit returns handle 0, so the record is freed).
	/// </summary>
	// [BIN:TPW-EXE:0x557970 ADDOBJ record] the dispatcher 0x5573D0 returns the submit's handle (0x51BFC0, 0 for event 0 or with the sound system off, 0x802BC8/0x802BD4) and 0x557970 keeps the record only when it is not 0
	public static HeldSound? StartHeldRseEvent( int type, uint eventId, Vector3? position = null, RseSoundSource? source = null, SoundCategory? ownSounds = null )
	{
		if ( !Enabled ) return null;
		var sound = FindRseEvent( type, eventId, source, ownSounds );
		if ( sound == null || events == null )
			return null;
		return events.PlayHeld( sound.Value.Category, eventId, AudioChannel.Effects, position );
	}

	/// <summary>
	/// Starts a held RSE event voice that takes parameters and moves (a ride vehicle's engine): a sentence event runs as a sentence
	/// (<see cref="SoundSentence.SetParameter"/>, <see cref="SoundSentence.MoveTo"/>); null when the event is missing or is not a repeating sentence (play those with <see cref="PlayRseEvent"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x0051EEB0 sound-variable submit] the vehicle code resolves an EventMap variable to an event id (0x55A3E0) and submits it through 0x51BFC0, keeping the handle for the per-tick 0x51C270 (position) and 0x51BC40 (parameter) calls
	public static SoundSentence? StartRseSentence( int type, uint eventId, Vector3 position, RseSoundSource? source = null )
	{
		var sound = FindRseEvent( type, eventId, source );
		if ( sound == null || events == null || sound.Value.Event.Player is not (SoundEventPlayer.Sentence or SoundEventPlayer.BranchingSentence) )
			return null;
		return events.StartSentence( sound.Value.Category, eventId, AudioChannel.Effects, position: position );
	}

	// [BIN:TPW-EXE:0x5562C5 REPAIREFFECT polarity] after the type's repair effect is switched (0x556DE0), `cmp esi, edi; je 0x55635F` sends the value 0 to the band sound and every other value falls through to the two sounds 0x6F and 0xD3 of the global ride category ([0x803A28], the category EVENT type 5 uses, 0x557875) at the object's position (0x556A80, each coordinate through __ftol). Earlier versions read the Mac code the other way round (REPAIREFFECT 1 as the band sound)
	// [BIN:TPW-EXE:0x55635f REPAIREFFECT 0] with more than five operands it calls 0x551320(band, 1, 1, x, y, z) at the object's position (0x556a80): the index (a + b) / 50 capped at 3 is 0 here, and the band is the word [[vm+0x1c]+0x14], the script's own variable slot 5 (the array [vm+0x1c] holds the script variables: the interpreter's variable operand 0x40000000 | n is stored at [vm+0x1c + 4n], 0x556408), which the standard ride scripts keep as VAR_ONRIDE; the family comes from it (0 silent, 1 -> 75, 2-3 and negative -> 79, 4-7 -> 83, >= 8 -> 87, all cat_kids 0x803a24; 0x551351-0x5514d8)
	/// <summary>The <c>cat_kids</c> sound of a band at index 0 (<c>0x551320</c>), or <c>null</c> for the silent band 0.</summary>
	public static uint? RepairBandEventForBand( int band ) => band switch
	{
		0 => null,
		1 => RepairBandEvent,
		< 4 => RepairBandEvent + 4,
		< 8 => RepairBandEvent + 8,
		_ => RepairBandEvent + 12,
	};

	/// <summary>
	/// The sounds of a ride script's <c>REPAIREFFECT</c> instruction: a non-zero value plays two <c>cat_rides</c> sounds at the object's position, zero plays the band-dependent
	/// <c>cat_kids</c> one-shot (silent for the riders count 0 of a repair).
	/// </summary>
	public static void PlayRepairEffect( int show ) => PlayRepairEffect( show, null );

	/// <summary>The band sound (value 0, <c>cat_kids</c> 75 family) or the pair of the non-zero value at a validated sound-space origin (the object's place; flat without one); returns submitted voices for owner cleanup.</summary>
	internal static (AudioVoice? First, AudioVoice? Second) PlayRepairEffect( int show, Vector3? position, int band = 1 )
	{
		if ( events == null )
			return (null, null);
		if ( show == 0 )
		{
			if ( RepairBandEventForBand( band ) is not { } start )
				return (null, null);
			return (position == null ? events.Play( Kids, start, AudioChannel.Effects ) : events.PlayAt( Kids, start, AudioChannel.Effects, position.Value ), null);
		}
		var first = position == null ? events.Play( Rides, RepairFirstEvent, AudioChannel.Effects ) : events.PlayAt( Rides, RepairFirstEvent, AudioChannel.Effects, position.Value );
		try
		{
			var second = position == null ? events.Play( Rides, RepairSecondEvent, AudioChannel.Effects ) : events.PlayAt( Rides, RepairSecondEvent, AudioChannel.Effects, position.Value );
			return (first, second);
		}
		catch
		{
			// The pair cannot return this handle for ownership if its second submission fails.
			// Preserve one-shot/flat lifetimes; only a partially started owned loop needs cleanup.
			if ( first is { Loops: true } ) first.Stop();
			throw;
		}
	}

	/// <summary>
	/// The music parameter for <paramref name="guestsInPark"/>: the original passes the cached guest count
	/// halved and capped at 100 (<c>0x100C2344</c>), then caps it at 89 and uses 0 in world state 4.
	/// </summary>
	// [BIN:STP-PPC:0x101C2444 park turn] music parameter = min(min(guests in park / 2, 100), 89), or 0 when the world state is 4
	// [BIN:TPW-EXE:0x0054F86B music in a lost park] the PC main loop caps the halved count at 0x59 (0x0054F851) and replaces it by 0 when the world state +0x1DA738 is 4 (0x0054F860, 0x0054F86D)
	public static int MusicParameter( int guestsInPark, bool parkLost = false ) => parkLost ? 0 : Math.Min( Math.Min( Math.Max( 0, guestsInPark ) / 2, 100 ), MaximumMusicParameter );

	/// <summary>Per frame: applies the option volumes, the music parameter, updates the listener and tops up the output.</summary>
	public static void Update( int? guestsInPark = null, bool parkLost = false )
	{
		if ( events == null )
			return;
		var mixer = events.Mixer;
		var options = GameOptions.Current;
		ApplyOptions( mixer, options );
		events.HeldSounds.Configure( fadeSettings, options.SoundQuality );
		if ( music != null && guestsInPark is { } guests )
		{
			var parameter = MusicParameter( guests, parkLost );
			if ( parameter != music.Parameter )
			{
				// [BIN:TPW-EXE:0x0051E79E music parameter] the update sends the value with 0x51BC40(handle, 4, value); the shipped park music sounds bind no selector, so it only steers the branching
				music.SetParameter( MusicParameterCode, parameter );
				music.Resume();
			}
		}
		FollowRectangleSounds();
		events.Pump();
		mixer.Pump();
	}

	/// <summary>
	/// Places the listener for the frame (the caller picks the eye or the eye/target midpoint with <see cref="ListenerPlace"/>, as
	/// the original's park loop does). The place is given in the
	/// engine's coordinates (X/Y ground, Z up) and moved into the original's sound units (five sound units per
	/// engine unit at model scale 0.2), undoing the heightfield's centering for imported parks.
	/// </summary>
	// [BIN:TPW-EXE:0x54f8f6 listener] the park loop sets the listener through 0x51c1d0: when the camera-mode byte 0x790ab0 has bits 0x16 the listener is the camera position (0x7909c0..c8); otherwise it is pos - (pos - target) * 0.5 (0x700f88) with the target at 0x790ab8..c0; the orientation vectors are 0x7909a0.. and 0x7909b0..
	public static void UpdateListener( System.Numerics.Vector3 ear, System.Numerics.Vector3 forward, ModelHeightfield? heightfield = null, System.Numerics.Vector3? up = null )
	{
		const float soundUnitsPerEngineUnit = 1f / OriginalParkPlacement.ModelScale;
		var origin = heightfield == null ? System.Numerics.Vector2.Zero : OriginalParkPlacement.GetOrigin( heightfield );
		var place = new Vector3( (ear.X + origin.X) * soundUnitsPerEngineUnit, ear.Z * soundUnitsPerEngineUnit, (ear.Y + origin.Y) * soundUnitsPerEngineUnit );
		// the engine's (X, Y ground, Z up) becomes the sound space (x, y up, z ground), as for positions; with the camera's up vector the listener keeps its pitch (AudioListener.Update)
		if ( up is { } upward )
			AudioListener.Update( place, new Vector3( forward.X, forward.Z, forward.Y ), new Vector3( upward.X, upward.Z, upward.Y ) );
		else
			AudioListener.Update( place, new Vector3( forward.X, 0, forward.Y ) );
	}

	/// <summary>
	/// The listener's place for a camera: the eye in the camera modes whose byte <c>0x790ab0</c> has bits 0x16 (OpenTPW's free-walk camera), otherwise the midpoint of
	/// eye and target.
	/// </summary>
	// [BIN:TPW-EXE:0x54f915 listener mode test] test 0x16, [0x790ab0]: set takes the eye (0x54f921-0x54f958), clear the midpoint (0x54f965-0x54f9e4)
	// [EXT:listener-eye-mode] OpenTPW's mapping of the original's camera-mode bits 0x16 to its camera modes: the free-walk camera (eye heard); the orbiting park camera hears the midpoint
	public static System.Numerics.Vector3 ListenerPlace( System.Numerics.Vector3 eye, System.Numerics.Vector3 target, bool eyeMode )
		=> eyeMode ? eye : eye - (eye - target) * 0.5f;

	/// <summary>The original's sound units of an engine place (X/Y ground, Z up): x/z stay ground, the engine's Z becomes the sound height, × 5 at the model scale 0.2.</summary>
	public static Vector3 SoundPosition( System.Numerics.Vector3 engine ) =>
		new( engine.X / OriginalParkPlacement.ModelScale, engine.Z / OriginalParkPlacement.ModelScale, engine.Y / OriginalParkPlacement.ModelScale );

	internal static void ApplyOptions( AudioMixer mixer, GameOptions options )
	{
		var saved = SavedVolumes;
		mixer.SetChannelGain( AudioChannel.Effects, saved?.Gain( AudioChannel.Effects, options ) ?? options.SoundEffectsGain );
		mixer.SetChannelGain( AudioChannel.Music, saved?.Gain( AudioChannel.Music, options ) ?? options.MusicGain );
		mixer.SetChannelGain( AudioChannel.Speech, saved?.Gain( AudioChannel.Speech, options ) ?? options.SpeechGain );
		if ( saved is { Holding: false } )
			SavedVolumes = null;
		var master = GameOptions.Gain( MasterVolume );
		if ( mixer.MasterGain != master )
			mixer.MasterGain = master;
	}

	/// <summary>
	/// The mixer values of the original save the running park was loaded from (<see cref="ApplySavedSound"/>), per channel until the player's options take over;
	/// null when none hold.
	/// </summary>
	public static SavedSoundVolumes? SavedVolumes { get; private set; }

	/// <summary>
	/// The master volume in percent. The original sets it to 100 at start-up and otherwise only a loaded save's Sound block changes it; no option drives it, so it
	/// holds for the rest of the process (OpenTPW has no master slider either).
	/// </summary>
	// [BIN:TPW-EXE:0x54DE31 master volume] the start-up calls the master setter 0x51BB60 with 100; the options applier 0x423DD0 never sets it, so only a loaded save's +0x18 changes it
	public static int MasterVolume { get; private set; } = GameOptions.MaximumVolume;

	/// <summary>
	/// Plays a park started from an original save (Level.OriginalSaveSound) at the volumes its Sound block holds, as tp.exe does after a load, without touching the player's options;
	/// returns the import report's line.
	/// </summary>
	// [BIN:TPW-EXE:0x51C400 sound loader] the payload loader 0x415270 calls the Sound block loader at 0x415BA2; the copied shadows reach the mixer on the next tick 0x51BD70 (park entry sets the dirty word after the load, 0x54F3AE), and the post-load pass 0x415140 does not call the options applier 0x423DD0
	public static string ApplySavedSound( SaveSoundData? sound, GameOptions options )
	{
		SavedVolumes = SavedSoundVolumes.FromSave( sound, options );
		if ( SavedVolumes == null )
			return $"Sound block: no 84-byte settings (length {sound?.Settings.Length.ToString() ?? "unreadable"}); the options' volumes play.";
		MasterVolume = SavedVolumes.MasterVolume;
		return $"Sound block: the save's volumes play until the options take over (OK on the options screen, a changed slider or toggle, or leaving the park): {SavedVolumes.Describe()}"
			+ (SavedVolumes.MatchesOptions( options ) ? "; equal to the options, so nothing changes audibly." : "; the options file is not changed.");
	}

	/// <summary>Ends a loaded save's channel volumes: the options drive every channel again (the master volume stays, as in the original).</summary>
	// [BIN:TPW-EXE:0x4237F0 options OK] OK on the options screen calls the applier 0x423DD0, which pushes every channel's profile volume and on-flag
	public static void EndSavedVolumes() => SavedVolumes = null;

	/// <summary>Forgets a loaded save's sound values, the master volume included (tests and a process restart).</summary>
	internal static void ResetSavedSound()
	{
		SavedVolumes = null;
		MasterVolume = GameOptions.MaximumVolume;
	}

	/// <summary>Plays a <c>cat_ui</c> event (control activation clicks, drawer and error feedback).</summary>
	public static AudioVoice? PlayUi( uint eventId ) => events?.Play( Ui, eventId, AudioChannel.Effects );

	/// <summary>Starts a <c>cat_ui</c> sentence event flat (the research window's loops and slider sound); null when sound is off or the event is missing.</summary>
	public static SoundSentence? StartUiSentence( uint eventId ) => events?.StartSentence( Ui, eventId, AudioChannel.Effects );

	/// <summary>Plays one event of the global <c>cat_speech</c> category flat on the speech channel (the advisor's cut-off slap); null when nothing plays.</summary>
	public static AudioVoice? PlaySpeechEffect( uint eventId ) => events?.Play( Speech, eventId, AudioChannel.Speech );

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
