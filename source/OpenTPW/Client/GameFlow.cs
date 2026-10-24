using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// Switches between the original-style front end (3D lobby + menus) and a park with the original
/// HUD, and owns the UI renderer, UI strings/fonts and options. Transitions are queued and run at
/// the start of the next update so no entity list is modified while it is iterated.
/// </summary>
internal sealed partial class GameFlow : IDisposable
{
	private readonly UiRenderer backgroundRenderer;
	private readonly UiRenderer overlayRenderer;
	private readonly UiInputSource inputSource = new();
	private readonly UiBatch backgroundBatch = new();
	private Action? pending;
	private string? pendingLevel;
	private bool prepareBuyWindowArt;
	private UiImagePreparation? buyWindowPreparation;
	private LobbyDefinition? lobbyDefinition;
	private (int Requested, int Fitted)? reportedUiScale;
	private OnlineSession? onlineSession;
	private UI.ChatOverlay? chatOverlay;
	/// <summary>The online players of the visited park, while a visit of a downloaded park runs with a login (docs/ONLINE.md).</summary>
	private ParkPresence? presence;

	public GameFlow()
	{
		UiApproximations.LogOnce();
		using ( LoadProfile.Measure( "strings" ) )
			Strings = UiStringTable.Load( GameLanguage.Current );
		UiFonts fonts;
		using ( LoadProfile.Measure( "fonts" ) )
			fonts = UiFonts.Load( GameLanguage.Current );
		using ( LoadProfile.Measure( "ui-models" ) )
			Context = new UiContext( Strings, fonts, new UiModels() );
		OptionsPath = SaveFileSystem.GetAbsolutePath( GameOptions.FileName );
		GameOptions.Current = GameOptions.Load( OptionsPath );
		Display = IDisplaySettings.Instance ?? new StubDisplaySettings();
		// Sky backdrop in the world pass; menus/HUD in the overlay pass at output pixels (after world upscaling).
		using ( LoadProfile.Measure( "ui-renderers" ) )
		{
			backgroundRenderer = new UiRenderer( global::Global.Render.MultisampledFramebuffer.OutputDescription );
			overlayRenderer = new UiRenderer( global::Global.Render.OutputDescription );
		}
		global::Global.Render.OnOverlayRender += RenderOverlay;
	}

	public UiStringTable Strings { get; private set; }
	public UiContext Context { get; private set; }
	/// <summary>A language chosen in the options, applied after the frame (front end) or when the park is left.</summary>
	private string? pendingLanguage;
	public OnlineFolders? OnlineFolders { get; set; }
	/// <summary>Automatic advice in original-level parks (off with <c>--no-advisor</c> and for <c>--advisor-say</c>/<c>--advisor-response</c>).</summary>
	public bool AutomaticAdvisorEnabled { get; set; } = true;
	/// <summary>The automatic advisor, created with the first park (null when disabled or its settings are missing).</summary>
	public AutomaticAdvisor? Advisor { get; private set; }
	private bool advisorUnavailable;
	public IDisplaySettings Display { get; set; }
	public string OptionsPath { get; set; }
	/// <summary>When false, options are not written (smoke tests).</summary>
	public bool PersistSettings { get; set; } = true;
	public Level? Level { get; private set; }
	public ParkHud? Hud { get; private set; }
	/// <summary>The level of the Theme Park Inc slice, which has no <see cref="Hud"/> but the experimental <see cref="IncHud"/> (docs/THEME-PARK-INC.md).</summary>
	// [EXT:tpi-viewer] viewing aid, not original behaviour
	private Level? ViewerLevel { get; set; }
	public FrontEndMenu? Menu { get; private set; }
	public LobbyScene? Lobby { get; private set; }
	/// <summary>The online world's globe, created when the Online World first opens in the lobby.</summary>
	public GlobeScene? Globe { get; private set; }
	private IReadOnlyList<Online.Api.CityInfo>? shownCities;
	public UiInput? InjectedInput { get; set; }
	/// <summary>How the current original level started; null in the front end and the generic sandbox.</summary>
	public ParkStartKind? StartKind { get; private set; }
	/// <summary>Raised after a queued transition ran.</summary>
	public event Action? Transitioned;

	public void Queue( Action transition )
	{
		AbandonParkBuild();
		prepareBuyWindowArt = false;
		buyWindowPreparation = null;
		pending = transition;
		pendingLevel = null;
	}

	/// <summary>The lobby's enter sequence holds a queued park until the camera has zoomed in (tp.exe 0x5e1e50 starts it).</summary>
	private bool enterHold;
	private LobbyIslandInfo? enterIsland;

	/// <summary>
	/// Starts the lobby's enter sequence on the selected island: the menu is hidden, the camera turns to the island's angle + π, the gate opens
	/// and the camera zooms in under a black fade, and the queued park starts when the radius is below 8 (tp.exe 0x5e1e30, 0x5e0490 states 1 and 2).
	/// </summary>
	// [BIN:TPW-EXE:0x5e1e30 enter] a park started from the lobby's island menu plays the sequence first; a park started by the CLI, a load or a restart skips it, as the original's loads do not pass the island
	internal void BeginLobbyEnter( string theme )
	{
		if ( Lobby == null || backdrop != Backdrop.Lobby || Camera.Mode is not LobbyCameraMode camera || Menu == null || camera.Entering )
			return;
		var island = Menu.Selected;
		if ( !string.Equals( island.Level, theme, StringComparison.OrdinalIgnoreCase ) )
			return;
		var lobby = Lobby;
		enterHold = true;
		enterIsland = island;
		camera.BeginEnter( island.Angle * MathF.PI / 180f, () => lobby.PlayGate( island, open: true ), () => enterHold = false );
	}

	/// <summary>The enter sequence is running: the lobby menu is hidden and a queued park waits.</summary>
	internal bool LobbyEntering => Camera.Mode is LobbyCameraMode { Entering: true };

	/// <summary>
	/// Abandons the enter sequence when the park it was to start did not start (the transition was refused or its dialog needs the menu):
	/// the orbit resumes, the fade eases out, the menu is back and an opened gate closes again with M2 (tp.exe 0x5e1350 and the early-leave
	/// callers play M2).
	/// </summary>
	internal void CancelLobbyEnter()
	{
		enterHold = false;
		if ( Camera.Mode is not LobbyCameraMode { Entering: true } camera )
			return;
		var opened = camera.Phase == LobbyCameraMode.EnterPhase.Zoom;
		camera.CancelEnter();
		if ( opened && Lobby != null && enterIsland != null )
			Lobby.PlayGate( enterIsland, open: false );
	}

	/// <summary>Queues a transition into <paramref name="level"/>; it waits until <see cref="LevelDataReady"/> says the level's files are there.</summary>
	public void QueueLevel( string level, Action transition, bool prepareBuyWindow = false )
	{
		AbandonParkBuild();
		prepareBuyWindowArt = prepareBuyWindow;
		buyWindowPreparation = null;
		pending = transition;
		pendingLevel = level;
	}

	/// <summary>
	/// Whether a level's files can be read now. The browser build copies a level's folder from the player's
	/// installation only when it is first played and answers false until it is in (docs/WEB.md); the lobby
	/// keeps running meanwhile. Null on the desktop, which reads the installation directly.
	/// </summary>
	public static Func<string, bool>? LevelDataReady { get; set; }

	/// <summary>
	/// Whether the speech files of a language can be read now. The browser copies them from the player's files when the
	/// language is first chosen and answers false until they are in; a language switch waits for it, so the advisor
	/// changes its voice together with the text. Null on the desktop.
	/// </summary>
	public static Func<string, bool>? LanguageDataReady { get; set; }

	/// <summary>
	/// Whether copying a language's speech files failed (browser). The choice is then dropped with a warning and the current
	/// language stays; the flag is cleared when the language is requested again. Null on the desktop.
	/// </summary>
	public static Func<string, bool>? LanguageDataFailed { get; set; }

	/// <summary>A language was picked in the options: the browser starts (or, after a failure, restarts) copying its speech files.</summary>
	public static Action<string>? LanguageChosen { get; set; }

	private bool SettlePendingLanguage() =>
		PendingLanguage.Settle( ref pendingLanguage, GameLanguage.Current.Name, LanguageDataReady, LanguageDataFailed, Log.Warning );

	/// <summary>
	/// Records the keys and buttons held right now as already seen, so a press that skipped the start-up movies
	/// (Space also activates a menu button) does not act on the front end's first frame.
	/// </summary>
	public void DiscardHeldInput() => inputSource.Poll( 1, 1 );

	// ---- Front end -------------------------------------------------------------------------

	/// <param name="keepJoin">[EXT:online-coop-snapshot-join] The front end's own rebuild (a language change): a join held in it goes on, and extension choices wait for it as in a park.</param>
	public void ShowFrontEnd( string? selectLevel = null, bool keepJoin = false )
	{
		TearDown( keepJoin: keepJoin );
		// [EXT:extensions-apply-at-load] extension choices made in the park just left are active in the lobby and for the next park
		if ( coopJoin == null )
			ApplyPendingExtensions();
		enterHold = false;
		using var frontEndProfile = LoadProfile.Measure( "front-end" );
		using ( LoadProfile.Measure( "lobby-definition" ) )
			lobbyDefinition ??= LobbyDefinition.Load();
		using ( LoadProfile.Measure( "lobby-scene" ) )
			Lobby = new LobbyScene( lobbyDefinition, generator: lobbyEffects.Draw, flyerPercent: LobbyObjectsPercent );
		LobbyCameraMode.Radius = lobbyDefinition.SpinRadius;
		LobbyCameraMode.VerticalOffset = lobbyDefinition.VerticalOffset;
		LobbyCameraMode.SpinSpeed = lobbyDefinition.SpinSpeed;
		LobbyCameraMode.IslandFieldOfView = lobbyDefinition.FieldOfView;
		FrontEndMenu? createdMenu = null;
		createdMenu = new FrontEndMenu( Strings, lobbyDefinition.Islands, new FrontEndActions
		{
			PlayerKeySnapshot = GameEditions.Capabilities.Economy && !GameEditions.IsThemeParkInc ? ReadLobbyPlayerKeys( ProgressPath ) : null,
			StartPark = ( island, mode ) => RequestParkStart( island.Level, mode, createdMenu ),
			Load = entry => ConfirmExtensions( entry.OwnSavePath, change => QueueProgressLevel( entry.Level, () => LoadPark( entry with { Switch = change }, overFrames: true ) ) ),
			ContinueEntry = FindContinueEntry,
			AutomaticEntry = FindAutomaticContinueEntry,
			ContinueAutomatic = entry => RequestAutomaticContinue( createdMenu!, entry ),
			ContinuePark = entry => QueueProgressLevel( entry.Park.Level, () => ContinuePark( entry ) ),
			Quit = RequestProgressQuit,
			StartEnterParkEffect = StartEnterParkEffect,
			CreateOptions = CreateOptions,
			IslandSelected = island =>
			{
				LobbyCameraMode.Target = Lobby!.Target( island );
				// [BIN:STP-PPC:0x1009598C lobby audio start] the front end starts the selected island's theme lobby music (cat_locallobbymusic 1) and looping ambience (cat_locallobbysfx 2)
				// [BIN:TPW-EXE:0x5e1640 island step] the island-step virtuals (0x5e1640, 0x5e1730; vtable 0x702ce8/0x702cec) stop the old island's two voices (+0x1c/+0x20, 0x51c300) and start the new island's music event 1 and ambience event 2 from the lobby table (0x803a4c, registered by 0x51e8f0); 0x5e14a0 (after the UI toggle at 0x4a6a33), 0x5e18d0, 0x5e23f0 and 0x5e0b21 restart them too
				GameAudio.EnterLobby( island.Level );
			},
			LoadEntries = FindLoadEntries,
			GoOnline = WhenOnline( ShowOnline ),
			// [EXT:save-extension-switch] an own save whose extensions differ asks before it loads
			ConfirmExtensions = ConfirmExtensions,
		} );
		Menu = createdMenu;
		using var selectProfile = LoadProfile.Measure( "lobby-select-island" );
		Menu.SelectIsland( 0 );
		if ( selectLevel != null )
			Menu.SelectLevel( selectLevel );
		Camera.SetCameraMode<LobbyCameraMode>();
		Log.Trace( $"Front end: lobby with {Lobby.IslandCount} islands ({Lobby.PartCount} draw batches)." );
	}

	private ParticleRenderer? enterParkEffect;

	/// <summary>The screen-space draws of the enter-park effect's last frame (smoke instrumentation); 0 when none runs.</summary>
	internal int EnterParkEffectDraws => enterParkEffect != null && Entity.All.Contains( enterParkEffect ) ? enterParkEffect.DrawnScreenParticles : 0;

	/// <summary>Starts the enter-park routine's screen-space particle effect over the lobby's key panel; it lives until the lobby is torn down (a park load).</summary>
	internal void StartEnterParkEffect()
	{
		enterParkEffect?.Delete();
		enterParkEffect = ParticleRendererModule.CreateScreenRenderer();
		if ( enterParkEffect != null )
			LobbyEnterEffect.Start( new ParticleSystemPool( enterParkEffect.Particles ), instantAction: false );
	}

	public UiScreen CreateOptions( UiScreenStack stack, Action closed ) => OptionsScreen.Create( stack, Strings, new OptionsServices
	{
		Display = Display,
		Graphics = IGraphicsSettings.Instance,
		TexturePacks = TexturePack.InstalledPacks(),
		BeginTexturePackSwitch = name =>
		{
			var diagnostics = new List<string>();
			var textureSwitch = TexturePackSwitch.Begin( name, diagnostics );
			foreach ( var diagnostic in diagnostics )
				Log.Warning( $"Texture pack: {diagnostic}" );
			return textureSwitch;
		},
		Options = GameOptions.Current,
		Languages = GameLanguage.Choosable(),
		// A choice still waiting for the park to be left is what the row shows, so it can also be taken back.
		CurrentLanguage = pendingLanguage ?? GameLanguage.Current.Name,
		// [EXT:extensions-apply-at-load] extensions switched on the Extensions and mods screen apply at the next park start or load
		ApplyExtensions = ApplyExtensionChoices,
		ApplyLanguage = language =>
		{
			pendingLanguage = string.Equals( language, GameLanguage.Current.Name, StringComparison.OrdinalIgnoreCase ) ? null : language;
			// Starts the browser's copy of the language's speech while the options are still open.
			if ( pendingLanguage != null ) LanguageChosen?.Invoke( pendingLanguage );
			return Level == null;
		},
		SaveOptions = () => { if ( PersistSettings ) GameOptions.Current.Save( OptionsPath ); },
		SaveLanguage = language =>
		{
			if ( !PersistSettings )
				return;
			// language.json beside display.json, so the choice survives a new release unpacked elsewhere.
			var path = LanguageSettings.GetDefaultPath();
			var saved = LanguageSettings.Load( path, new List<string>() );
			(saved with { Language = language }).Save( path );
		},
	}, closed );

	/// <summary>The save file's local last-write time (the original lists FILETIME to local time, 0x49ec80), or null when the file system has none.</summary>
	private static DateTime? SavedAt( string path )
	{
		try { return File.GetLastWriteTime( path ); }
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException or PlatformNotSupportedException ) { return null; }
	}

	public IReadOnlyList<ParkLoadEntry> FindLoadEntries()
	{
		var entries = new List<ParkLoadEntry>();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var path = SaveFileSystem.GetAbsolutePath( $"opentpw-{theme}-world.json" );
			if ( !File.Exists( path ) ) continue;
			try
			{
				var saved = ParkResumeSaveFile.Load( path );
				// [EXT:mods] a save made with mods that are not all active stays listed and says which it needs ([EXT:save-extension-switch] unless the load can switch them: it asks then)
				// [EXT:cheats-publish] and says when the park is marked as cheated
				entries.Add( new ParkLoadEntry( saved.Theme, -1, false, path, saved.DisplayName, SavedAt( path ), ParkExtensionSwitch.RequiresText( saved.Mods, NextExtensions ), saved.Economy.Cheats?.Mode == true ) );
			}
			catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException )
			{
				Log.Warning( $"Saved park {Path.GetFileName( path )} cannot be listed: {error.Message}" );
			}
		}
		if ( File.Exists( SaveFileSystem.GetAbsolutePath( "opentpw-sandbox.json" ) ) )
			entries.Add( new ParkLoadEntry( "jungle", 0, true ) );
		foreach ( var island in (lobbyDefinition ??= LobbyDefinition.Load()).Islands )
		{
			try
			{
				if ( FileSystem.GetFiles( $"/levels/{island.Level}" ).Any( path => string.Equals( Path.GetFileName( path ), OriginalPark.SaveFileName, StringComparison.OrdinalIgnoreCase ) ) )
					entries.Add( new ParkLoadEntry( island.Level, island.ThemeNameIndex, false ) );
			}
			catch ( DirectoryNotFoundException ) { }
		}
		return entries;
	}

	// ---- Park ------------------------------------------------------------------------------

	/// <summary>
	/// New park from the lobby in the chosen mode: Instant Action adds the <c>Easy_</c> balance layer and
	/// the shipped Easymode seed where the level has them; Full Simulation starts from terrain and MAP rules.
	/// </summary>
	/// <param name="overFrames">Build the park over several frames while the current scene keeps running (the front end's own transitions).</param>
	public void StartPark( string levelName, GameMode mode, bool overFrames = false )
	{
		if ( mode == GameMode.InstantAction ) StartInstantPark( levelName, overFrames );
		else RequestParkStart( levelName, mode );
	}

	/// <param name="overFrames">As for <see cref="StartPark"/>; a park save of the player's own is still resumed at once.</param>
	public void LoadPark( ParkLoadEntry entry, bool overFrames = false )
	{
		if ( entry.OwnSavePath != null )
		{
			try { ResumePark( ParkResumeSaveFile.Load( entry.OwnSavePath ), extensionSwitch: entry.Switch ); }
			catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException
				or ArgumentException or InvalidOperationException or NotSupportedException )
			{
				Log.Warning( $"Park load failed: {error.Message}" );
				var stack = Hud?.Stack ?? Menu?.Stack;
				stack?.Push( UiDialogs.Message( "loadFailed", () => error.Message,
					(() => Strings.Extra( OpenTpwText.Back ), () => stack.Pop()) ) );
			}
			return;
		}
		if ( entry.Sandbox )
			StartLevel( "jungle", original: false, developerPanels: false, overFrames: overFrames, ready: level => level.LoadSandbox() );
		else
			// [EXT:no-player-profiles] a loaded shipped park is the reference start (its own balance, Full Simulation rules) whatever Game Mode was last chosen, a design choice: OpenTPW has no player profiles to hold the mode ([EXT:lobby-load])
			// [APPROX:UI-041] a loaded park takes no game type from the park: the original derives GameType from flag bits 0x2000000/0x1000000 of 0x7a1a8c (0x550ca0), set by a front-end toggle (0x4bbf63) — evidence needed: whether a park file of the PC build can carry the game type
			StartLevel( entry.Level, original: true, developerPanels: false, overFrames: overFrames );
	}

	/// <summary>
	/// Creates a level with the original HUD. CLI paths keep the developer panels. Original levels
	/// default to the read-only reference start (shipped save imported as it was made).
	/// </summary>
	public Level StartLevel( string levelName, bool original, bool developerPanels, ParkVisitInfo? visit = null, ParkStartKind start = ParkStartKind.OriginalSaveReference, ReadOnlyMemory<byte>? playerSave = null )
	{
		using var profile = LoadProfile.Measure( "start-level" );
		using ( LoadProfile.Measure( "tear-down" ) )
			TearDown();
		// [EXT:extensions-apply-at-load] the new park is built with the extension choices made in the park just left
		ApplyPendingExtensions();
		Level level;
		using ( LoadProfile.Measure( "level" ) )
			level = new Level( levelName, loadOriginalLevel: original, visit: visit, onlineFolders: OnlineFolders, start: start, playerSave: playerSave ) { ShowDeveloperPanels = developerPanels };
		return EnterLevel( level, levelName, original, visit );
	}

	/// <summary>
	/// Plays the guests' voices and the stinkbomb at the guest's place. Every route into a park attaches it, a resumed park included: the voices draw from the sound seed,
	/// which a park save carries, so a resumed park must draw exactly as the park it continues.
	/// </summary>
	private static void AttachGuestSounds( Level level )
	{
		if ( level.Guests == null )
			return;
		level.Guests.Voiced += ( guest, eventId ) => GameAudio.PlayGuestVoice( eventId, new Vector3( guest.X * 10, 0, guest.Y * 10 ) );
		level.Guests.Teleported += level.StartGuestBeam;
		level.Guests.Pranked += ( guest, prank ) =>
		{
			if ( prank == GuestDecision.Prank.Stinkbomb )
				GameAudio.PlayStinkbomb( new Vector3( guest.X * 10, 0, guest.Y * 10 ) );
		};
	}

	/// <summary>
	/// The rest of <see cref="StartLevel"/> once its level is built and current: park audio, HUD and advisor. <paramref name="preparedHud"/> is the HUD
	/// a build step made for this level (GameFlow.PrepareFirstFrame); without it the HUD is made here.
	/// </summary>
	private Level EnterLevel( Level level, string levelName, bool original, ParkVisitInfo? visit, ParkHud? preparedHud = null )
	{
		Level = level;
		if ( original )
			using ( LoadProfile.Measure( "enter-park-audio" ) )
				GameAudio.EnterPark( levelName, level.Seed.SoundStream );
		// [BIN:TPW-EXE:0x51C400 sound loader] a park started from an original save (a player's own or the shipped Easymode seed, Level.OriginalSaveSound) plays at the save's volumes until the options take over (docs/AUDIO.md, "Volumes of a loaded original save"); the player's options are not changed
		if ( original && visit == null && level.OriginalSaveSound is { } savedSound )
		{
			var line = GameAudio.ApplySavedSound( savedSound, GameOptions.Current );
			level.Park?.Import?.Report( line );
			Log.Trace( line );
		}
		StartKind = level.Park?.Start.Kind;
		AttachGuestSounds( level );
		// [EXT:tpi-viewer] the edition capability policy runs the experimental Theme Park Inc slice: the shared world,
		// build and guest systems under the experimental HUD, without the reference game's economy, HUD or advisor.
		if ( level.IsThemeParkIncSlice )
		{
			ViewerLevel = level;
			try
			{
				IncHud = CreateThemeParkIncHud( level );
				Log.Trace( $"Theme Park Inc experimental slice: {levelName} with the experimental HUD; {GameEditions.SliceNotice}" );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException )
			{
				IncHud = null;
				Log.Warning( $"Theme Park Inc experimental HUD unavailable ({exception.Message}); {levelName} runs as the bare viewer. Esc quits, P pauses." );
			}
			return level;
		}
		if ( preparedHud != null )
		{
			// [EXT:park-entry-preparation] made in a build step; what it took from the input of that frame is taken from this one
			preparedHud.RefreshEntryInput();
			Hud = preparedHud;
		}
		else
			using ( LoadProfile.Measure( "park-hud" ) )
				Hud = CreateParkHud( level, HudStatusFor( level ) );
		// [EXT:online-visit-presence] a visit with a chat room (a downloaded park's, a chat satellite's) joins it and draws its players while logged in
		if ( visit != null && FeatureGates.Online )
			presence = ParkPresence.TryCreate( onlineSession, level );
		// [EXT:online-visit] read-only visits of shared parks get no advisor (the original has no visits)
		// [EXT:sandbox] the generic sandbox (not an original level) gets no automatic advisor either; only original levels raise its game events
		if ( AutomaticAdvisorEnabled && original && visit == null && !advisorUnavailable )
		{
			using ( LoadProfile.Measure( "advisor" ) )
				Advisor ??= AutomaticAdvisor.TryCreate();
			advisorUnavailable = Advisor == null;
			Advisor?.AttachLevel( level );
			if ( Advisor != null )
			{
				Advisor.CaptionSink = Hud.PostAdvisorCaption;
				Advisor.PlayerTickets = PlayerTickets;
			}
		}
		if ( Advisor?.IsAttached != true && level.Park?.Import is { } import && level.OriginalPark?.Save?.Advisor is { } saved )
		{
			var dropped = $"[APPROX:SAVE-010] No automatic advisor is attached ({(advisorUnavailable ? "unavailable" : "disabled")}): the saved advisor controller's {OriginalEconomyImport.AdvisorRecords( saved )} are dropped.";
			import.Report( dropped );
			Log.Trace( dropped );
		}
		AttachProgress( level );
		if ( original && visit == null ) TakeRestartSnapshot( level, overFrames: true );
		return level;
	}

	/// <summary>The window list sorts of this session, shared by every park's HUD (UI-084).</summary>
	private readonly ListSortMemory listSorts = new();

	/// <summary>
	/// Money, calendar, speed and purchases come from the park economy of original levels (Level.Park, looked up on every access so loading a
	/// park save is followed); the generic sandbox has none.
	/// </summary>
	private IHudParkStatus HudStatusFor( Level level ) => level.Park != null ? EconomyParkStatus.ForLevel( level, PlayerKeys ) : new NoEconomyStatus();

	private ParkHud CreateParkHud( Level level, IHudParkStatus status )
	{
		ParkHud? createdHud = null;
		createdHud = new( level, Strings, status, new OriginalBuildCatalog( level.Objects.Catalog ), CreateHudHost( level, () => createdHud! ) );
		return createdHud;
	}

	/// <summary>The park HUD's connections to the flow; the online entries are null while the online extension is off.</summary>
	internal HudHost CreateHudHost( Level level, Func<ParkHud> hud ) => new()
	{
		ExitToLobby = () => RequestAutomaticExit( level, hud() ),
		Quit = RequestProgressQuit,
		SavePark = SaveCurrentPark,
		OpenPostcard = WhenOnline( ShowPostcard ),
		CreateOptions = CreateOptions,
		CreateLoad = stack => FrontEndMenu.CreateLoadScreen( stack, Strings, FindLoadEntries(), entry => ConfirmExtensions( entry.OwnSavePath, change => QueueProgressLevel( entry.Level, () => LoadPark( entry with { Switch = change }, overFrames: true ) ) ) ),
		GoOnline = WhenOnline( ShowOnline ),
		IsLoggedIn = () => FeatureGates.Online && onlineSession?.IsLoggedIn == true,
		// Restart Park reloads the restart snapshot in every original park but the read-only visit (the original's pause menu 0x48C150 lists the entry for every game type except the online visit)
		RestartPark = RestartEligible( level ) ? () => RestartCurrentPark( level ) : null,
		PublishPark = WhenOnline( stack => CreateOnlineScreens( stack ).ShowPublish() ),
		ListSorts = listSorts,
		OptionsChanged = () => { if ( PersistSettings ) GameOptions.Current.Save( OptionsPath ); },
		ParkDoorPressed = open => Advisor?.OnParkDoorPressed( open ),
		WindowOpened = window => Advisor?.OnWindowOpened( window ),
		WindowClosed = window => Advisor?.OnWindowClosed( window ),
		HelpPosted = message => Advisor?.PostHelp( message ),
		RestoredHelp = message => Advisor?.PostHelp( message ),
		HelpWithdrawn = message => Advisor?.WithdrawHelp( message ),
		ModeChanged = () => Advisor?.OnModeChanged(),
		AdvisorSaysStaffMessages = () => Advisor != null,
		HelpKeyPressed = () => Advisor?.OnHelpKeyPressed(),
		Presence = () => presence,
		// [EXT:online-coop-session] sharing the park with other players (C2d-2) and its status line
		BuildTogether = WhenOnline( stack => CreateOnlineScreens( stack ).ShowBuildTogether() ),
		SessionStatus = () => SessionStatusLine( level ),
	};

	/// <summary>
	/// Guests whose cell is within four cells of the camera's ground target — the crowd sentence's parameter
	/// (<c>cat_kids</c> 0x5B, capped at 100). The original's park turn counts them the same way each turn.
	/// </summary>
	// [BIN:STP-PPC:0x101C2490 park turn] guests within four cells of the camera are counted (0x100C313C, radius argument 4) for the crowd sentence
	private static int GuestsNearCamera( Level level )
	{
		// [APPROX:UI-094] in the ride view the park camera's target stays where it was and the count keeps using it (the camera cell of the crowd call 0x54f899 is not traced to its source; the ride moves only the eye matrix 0x790990, never the target 0x7908f0)
		if ( level.Guests == null || (Camera.Mode as ParkCameraMode ?? level.ParkCameraWhileRiding) is not { } camera )
			return 0;
		if ( !level.TryGetGridCell( camera.Target.X, camera.Target.Y, out var x, out var y ) )
			return 0;
		var count = 0;
		foreach ( var guest in level.Guests.Guests )
		{
			if ( Math.Max( Math.Abs( guest.Cell.X - x ), Math.Abs( guest.Cell.Y - y ) ) <= CrowdRadiusCells )
				count++;
		}
		return count;
	}

	/// <summary>The crowd count's radius in cells (<c>0x101C2490</c> passes 4 to the guest counter).</summary>
	// [BIN:TPW-EXE:0x4c8d30 crowd counter] the park loop (0x54e360) calls it with the camera cell, radius 4 (0x54f4ba) and counts the things whose type byte (+2) is 1 over the cell rectangle [x-4, x+4] x [y-4, y+4] clamped to 0..127 (0x4c8d43-0x4c8da5); the caller caps the count at 100 (0x54f89e) and sends 0 when the world state is 4 (setter 0x51e7b0): the same square as this Chebyshev test
	public const int CrowdRadiusCells = 4;

	/// <param name="keepAdvisorPreparation">The park being entered tears down the live one: what its build read for the advisor is kept for its first update.</param>
	/// <param name="keepJoin">[EXT:online-coop-snapshot-join] The front end only rebuilds itself (a language change): a join held in it goes on. Every other teardown ends it.</param>
	private void TearDown( bool applyPendingLanguage = true, bool keepAdvisorPreparation = false, bool keepJoin = false )
	{
		LeaveCoopSession( "The park was closed.", keepJoin );
		RequireProgressSaved();
		FlushRestartSnapshot();
		DetachProgress();
		AbandonParkBuild();
		if ( !keepAdvisorPreparation )
		{
			DropAdvisorPreparation();
			keepJoinAdvisorPreparation = false;
		}
		presence?.Dispose();
		presence = null;
		Hud?.Stack.Clear();
		Menu?.Stack.Clear();
		IncFrontEnd?.Stack.Clear();
		IncFrontEnd = null;
		Advisor?.DetachLevel();
		GameAudio.LeavePark();
		GameAudio.LeaveLobby();
		if ( Level != null )
		{
			Level.Dispose();
			Level = null;
			Hud = null;
			ViewerLevel = null;
			IncHud = null;
			StartKind = null;
			OpenTPW.Level.Current = null!;
		}
		foreach ( var entity in Entity.All.ToArray() )
			entity.Delete();
		Lobby = null;
		Globe = null;
		parkCamera = null;
		shownCities = null;
		cityScene = null;
		cityKey = null;
		backdrop = Backdrop.Lobby;
		Menu = null;
		// Leaving a park (to the lobby or straight into another park) applies a language chosen inside it.
		if ( applyPendingLanguage ) ApplyPendingLanguage();
	}

	public static void Quit() => global::Global.Render.Window.SdlWindow.Close();

	// ---- Frame -----------------------------------------------------------------------------

	/// <summary>Canvas at the output's drawable pixels with the display's integer UI scale.</summary>
	private UiCanvas CurrentCanvas()
	{
		var pixels = Screen.PixelSize;
		var canvas = new UiCanvas( Math.Max( 1, pixels.X ), Math.Max( 1, pixels.Y ), Math.Max( 1, Display.EffectiveUiScale ), Screen.PixelDensity );
		var scales = (canvas.UiScale, canvas.TextScale);
		if ( reportedUiScale != scales && canvas.TextScale < canvas.UiScale )
			Log.Warning( $"Interface scale {canvas.UiScale}x falls back to {canvas.TextScale}x: {canvas.Width}x{canvas.Height} drawable pixels cannot fit the {UiScaling.ReferenceWidth}x{UiScaling.ReferenceHeight} reference layout at the requested scale." );
		reportedUiScale = scales;
		return canvas;
	}

	public void Update()
	{
		PumpRestartSnapshot();
		// [EXT:texture-pack] the pack switch belongs to the game loop, not to its loading screen
		PumpProgress();
		TexturePackSwitch.PumpCurrent();
		// A park being built over frames (a transition queued meanwhile replaces it).
		if ( parkBuild != null )
			PumpParkBuild();
		// A park waits for a language chosen just before it (its speech files may still be coming) and is then entered in it:
		// the transition's tear-down applies the choice before the park is built.
		else if ( pending != null )
		{
			// The readiness is polled while the lobby's enter sequence plays too (the browser starts copying the level's files then), but the park waits for the zoom.
			var ready = pendingLevel == null || (SettlePendingLanguage() && LevelDataReady?.Invoke( pendingLevel ) != false);
			if ( ready && !(enterHold && LobbyEntering) )
			{
				var transition = pending;
				pending = null;
				pendingLevel = null;
				prepareBuyWindowArt = false;
				buyWindowPreparation = null;
				transition();
				// A park built over frames raises it once it is entered.
				if ( parkBuild == null )
					Transitioned?.Invoke();
			}
			else if ( !ready && prepareBuyWindowArt && TexturePackSwitch.Current == null )
			{
				// Global UI files are ready while the browser copies theme files. Prepare only
				// buy-window art here; previews and signs still load only when requested.
				buyWindowPreparation ??= new UiImagePreparation( ParkHud.BuyWindowImagePaths( Context ) );
				var start = System.Diagnostics.Stopwatch.GetTimestamp();
				buyWindowPreparation.Pump( overlayRenderer.PrepareImage,
					() => System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds, 6 );
			}
		}
		// An enter sequence that has zoomed in with no park queued or being built (the transition was refused, or it left the lobby as it was) must
		// not leave the lobby black with its menu hidden, where the refusal's dialog could not be answered
		else if ( LobbyEntering && !enterHold )
			CancelLobbyEnter();
		Context.Canvas = CurrentCanvas();
		Context.Time = Time.Now;
		Context.Delta = Time.Delta;
		Context.PopupHelp = GameOptions.Current.PopupHelp;
		// Input is in logical window units; the UI works in drawable pixels.
		var logical = Screen.Size;
		var input = InjectedInput ?? inputSource.Poll( Context.Canvas.Width / (float)Math.Max( 1, logical.X ), Context.Canvas.Height / (float)Math.Max( 1, logical.Y ) );
		InjectedInput = null;
		// [EXT:park-build-over-frames] while a park is built the live scene runs on without input
		if ( parkBuild != null )
			input = UiInput.Idle( input.Mouse ) with { PointerHostBlocked = true };
		Input.TextEntryActive = false;
		onlineSession?.Pump();
		// [EXT:online-coop-session] sharing the open park (C2d-2): its preparation, its create and the hosting run here, after the link's answers and before the level
		StepCoopShare();
		// [EXT:online-coop-snapshot-join] a guest's join and resync: the build's slice, the check at the cut and the swap run here, before the HUD and the level
		using ( FrameMarks.Measure( "coop.session" ) )
			StepCoopSession();
		var imguiMouse = ImGuiNET.ImGui.GetIO().WantCaptureMouse;
		if ( imguiMouse )
			input = input with { LeftPressed = false, LeftReleased = false, RightPressed = false, Wheel = 0 };

		if ( Level != null && Hud != null )
		{
			Hud.ObserveApplicationFocus( Window.Current?.Focused == true, Window.Current?.IsOffscreen != false, onlineSession?.IsLoggedIn == true );
			using ( FrameMarks.Measure( "park.hud" ) )
				Level.UiCapturesMouse = Hud.Update( Context, input ) || parkBuild != null;
			// The camera reads the keyboard itself: while the next park is built it stays where it is, as under a modal window.
			if ( parkBuild != null && Camera.Mode is ParkCameraMode parkCamera )
				parkCamera.ModalOpen = true;
			if ( parkBuild != null && Camera.Mode is FreeWalkCameraMode walkCamera )
				walkCamera.ModalOpen = true;
			UpdateParkGlobe( Hud );
			// The pause menu, the map window and a texture-pack download stop the park; the speed setting is kept (ParkHud.TimeScaleFor).
			// The park being left waits too while the next one is built. [EXT:online-coop-lockstep] in a co-op session no screen of one player stops the park: it runs at its speed
			// (a guest's clock follows the host's stream, and its scene clock this speed)
			Level.SimulationTimeScale = parkBuild != null ? 0 : SessionTimeScale( Level ) ?? Hud.SimulationTimeScale;
			using ( FrameMarks.Measure( "park.level" ) )
				Level.Update();
			presence?.Update( Time.Delta );
			using ( FrameMarks.Measure( "park.advisor" ) )
				Advisor?.Update( Hud.Paused, parkPaused: Level.SimulationTimeScale <= 0 );
			// [EXT:park-entry-preparation] what the build prepared for the advisor and its first line is used by the update of the frame that entered the park; anything left is dropped
			// (a joined park's, once its advisor has said its first line: a resync's says it the frame after the swap), but for the lines decoded ahead, which wait for their own
			// saying until a teardown
			if ( parkBuild == null && !KeepingJoinAdvisorPreparation() )
				AdvisorEntryPreparation.DropAllButLines();
			using ( FrameMarks.Measure( "park.autosave" ) )
				UpdateAutosave( Level, Hud );
			var parkLost = Level.Park?.Economy.IsBankrupt == true;
			using ( FrameMarks.Measure( "park.audio" ) )
			{
				GameAudio.Update( Level.Guests?.GetStatistics().InPark, parkLost );
				ReverbSelector.Update( Level.RideView?.Ride );
			}
			// The game timer the steps follow stands still while the park is paused, as the advisor's clock does.
			var crowdLevel = Level;
			GameAudio.UpdateCrowd( Hud.Paused ? 0 : Time.Delta * 1000.0, () => GuestsNearCamera( crowdLevel ), parkLost );
			GameAudio.UpdateStaffVoices( Level.StaffWorld?.Snapshots ?? Array.Empty<StaffWorldSnapshot>(), Level.Park?.Economy.Turn ?? -1, Level.StaffWorld?.DrivesStaff == true );
			if ( Level.StaffWorld != null )
				GameAudio.PlayStaffVoices( Level.StaffWorld.TakeVoices() );
			GameAudio.SetRain( Level.Weather?.RainLevel ?? 0 );
			GameAudio.SetWeatherRain( Level.Weather?.Rain ?? false );
			foreach ( var strike in Level.Weather?.TakeThunder() ?? Array.Empty<Vector3>() )
				GameAudio.PlayThunder( strike );
			return;
		}
		if ( Level != null && ViewerLevel == Level )
		{
			if ( IncHud != null )
			{
				UpdateThemeParkIncSlice( Level, IncHud, input );
			}
			else
			{
				UpdateViewer( Level, input );
			}
			return;
		}
		// [EXT:tpi-front-end-phase1] the Theme Park Inc Personnel File: no lobby scene, camera or sound in phase 1
		if ( IncFrontEnd != null )
		{
			IncFrontEnd.Update( Context, input );
			return;
		}
		GameAudio.EnsureStarted();
		GameAudio.PlayStartupSound();
		GameAudio.Update();
		UpdateGlobeView();
		Camera.Update();
		UpdateLobbyEffects();
		using ( FrameMarks.Measure( "lobby.codecWarmup" ) )
			PumpLobbyCodecWarmup();
		if ( !LobbyEntering )
			Menu?.Stack.Update( Context, input );
		foreach ( var entity in Entity.All.ToArray() )
			entity.Update();
		// After the frame's input and once every dialog is closed (a display change may still wait for confirmation), so the
		// options screen that asked for it is not torn down while it runs.
		if ( pendingLanguage != null && Level == null && Menu != null && Menu.Stack.Top == Menu.Main && SettlePendingLanguage() && pendingLanguage != null )
		{
			var selected = Menu.Selected.Level;
			// [EXT:online-coop-snapshot-join] the front end's own rebuild keeps a join under way: the player has not gone elsewhere
			ShowFrontEnd( selected, keepJoin: true );
			Menu?.ShowOptions();
		}
	}

	/// <summary>
	/// [EXT:live-language] Switches to <see cref="pendingLanguage"/>: the language, the string table and the fonts. Caches of
	/// language data follow <see cref="GameLanguage.Version"/>; screens are rebuilt by the caller.
	/// </summary>
	private void ApplyPendingLanguage()
	{
		// The speech files are not in yet (browser): keep the choice, the frame update applies it once they are.
		if ( !SettlePendingLanguage() )
			return;
		var name = pendingLanguage;
		pendingLanguage = null;
		if ( name == null || string.Equals( name, GameLanguage.Current.Name, StringComparison.OrdinalIgnoreCase ) )
			return;
		try
		{
			// Everything is loaded before anything changes, so a language that fails to load leaves the old one whole.
			var language = GameLanguage.ResolveSwitch( name );
			var strings = UiStringTable.Load( language );
			var fonts = UiFonts.Load( language );
			GameLanguage.Current = language;
			LanguageInterfaceArchive.Apply( FileSystem, language );
			Strings = strings;
			Context = new UiContext( strings, fonts, Context.Models );
			Log.Trace( $"Language: {language}" );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException )
		{
			Log.Warning( $"Language {name} could not be loaded ({exception.Message}); keeping {GameLanguage.Current.Name}." );
		}
	}

	/// <summary>The bare Theme Park Inc viewer's frame (the experimental HUD could not be built): Esc quits, P pauses and resumes, the camera moves as in a park.</summary>
	private void UpdateViewer( Level level, UiInput input )
	{
		if ( input.Has( UiKeys.Back ) )
		{
			Quit();
			return;
		}
		if ( input.Has( UiKeys.Pause ) )
			viewerPaused = !viewerPaused;
		level.UiCapturesMouse = false;
		level.SimulationTimeScale = viewerPaused ? 0f : 1f;
		level.Update();
		GameAudio.Update( level.Guests?.GetStatistics().InPark );
	}

	private bool viewerPaused;

	/// <summary>
	/// While the Online World is open in the lobby, the 3D background is the online globe instead of the islands
	/// (docs/ONLINE.md); closing it brings the lobby and its camera back.
	/// </summary>
	/// <summary>The park's camera while the online globe is shown over the park.</summary>
	private CameraMode? parkCamera;

	/// <summary>
	/// In a park, the online globe screens (the Online World, choosing a city to publish in) draw the globe instead of the
	/// park and the HUD, with the globe camera; closing them restores the park's camera.
	/// </summary>
	private void UpdateParkGlobe( ParkHud hud )
	{
		var wanted = hud.Stack.Screens.Any( screen => screen is UI.OnlineGlobeScreen );
		if ( wanted == (parkCamera != null) )
		{
			if ( wanted && Globe != null && onlineSession != null && !ReferenceEquals( onlineSession.Cities, shownCities ) )
			{
				shownCities = onlineSession.Cities;
				Globe.SetCities( shownCities );
			}
			return;
		}
		if ( wanted )
		{
			try
			{
				Globe ??= new GlobeScene();
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException )
			{
				Log.Warning( $"Online globe unavailable: {exception.Message}" );
				return;
			}
			var definition = LobbyDefinitionOrNull();
			if ( definition != null )
			{
				GlobeCameraMode.RadiusOut = definition.GlobeRadiusOut;
				GlobeCameraMode.RadiusIn = definition.GlobeRadiusIn;
			}
			GlobeCameraMode.Reset();
			parkCamera = Camera.Mode;
			Camera.SetCameraMode<GlobeCameraMode>();
			Globe.Visible = true;
			shownCities = null;
		}
		else
		{
			Camera.Mode = parkCamera!;
			parkCamera = null;
			if ( Globe != null )
				Globe.Visible = false;
		}
	}

	private LobbyDefinition? LobbyDefinitionOrNull()
	{
		try
		{
			return LobbyDefinition.Load();
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException )
		{
			return null;
		}
	}

	private enum Backdrop { Lobby, Globe, City }

	private Backdrop backdrop = Backdrop.Lobby;
	/// <summary>The open city lobby's islands (a lobby scene with the city's parks at the island slots).</summary>
	private LobbyScene? cityScene;
	/// <summary>Islands of the open city lobby (front-end smoke test).</summary>
	internal int CityIslandCount => cityScene?.IslandCount ?? 0;
	private string? cityKey;

	/// <summary>
	/// The 3D background follows the topmost online screen in the lobby: the online globe for the Online World, the
	/// city's park islands for a city lobby (docs/ONLINE.md), else the own lobby with its camera.
	/// </summary>
	private void UpdateGlobeView()
	{
		var top = Lobby == null ? null : Menu?.Stack.Screens.LastOrDefault( screen => screen is UI.OnlineGlobeScreen or UI.CityLobbyScreen );
		var wanted = top switch { UI.CityLobbyScreen => Backdrop.City, UI.OnlineGlobeScreen => Backdrop.Globe, _ => Backdrop.Lobby };
		// The server's cities, whenever the session fetched a new list.
		if ( wanted == Backdrop.Globe && Globe != null && onlineSession != null && !ReferenceEquals( onlineSession.Cities, shownCities ) )
		{
			shownCities = onlineSession.Cities;
			Globe.SetCities( shownCities );
		}
		if ( top is UI.CityLobbyScreen city )
			UpdateCityScene( city );
		if ( wanted == backdrop )
			return;
		if ( wanted == Backdrop.Globe )
		{
			try
			{
				Globe ??= new GlobeScene();
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException )
			{
				Log.Warning( $"Online globe unavailable: {exception.Message}" );
				return;
			}
			GlobeCameraMode.RadiusOut = Lobby!.Definition.GlobeRadiusOut;
			GlobeCameraMode.RadiusIn = Lobby.Definition.GlobeRadiusIn;
			// Every globe activation faces the start view again, also back from a city lobby (GlobeCameraMode.Reset).
			GlobeCameraMode.Reset();
			Camera.SetCameraMode<GlobeCameraMode>();
		}
		else
		{
			if ( wanted == Backdrop.Lobby && Lobby != null && Menu != null )
			{
				LobbyCameraMode.Target = Lobby.Target( Menu.Selected );
				cityScene?.Delete();
				cityScene = null;
				cityKey = null;
			}
			Camera.SetCameraMode<LobbyCameraMode>();
		}
		// The globe plays its own music and ambience while it is up (GameAudio.EnterGlobe).
		if ( wanted == Backdrop.Globe )
			GameAudio.EnterGlobe();
		else if ( backdrop == Backdrop.Globe )
			GameAudio.LeaveGlobe();
		backdrop = wanted;
		if ( Globe != null )
			Globe.Visible = wanted == Backdrop.Globe;
		if ( cityScene != null )
			cityScene.Visible = wanted == Backdrop.City;
		if ( Lobby != null )
			Lobby.Visible = wanted == Backdrop.Lobby;
	}

	/// <summary>The LOBBYOBJECTS setting in effect (the share of the lobby's flyers that is spawned); 100 without graphics settings.</summary>
	// [BIN:TPW-EXE:0x5d9e22 lobby objects] the flying-mesh groups scale their counts by the settings' LOBBYOBJECTS value ([0x7858dc]) in percent
	private static int LobbyObjectsPercent => IGraphicsSettings.Instance?.Detail.LobbyObjects ?? 100;

	/// <summary>The front end's island effects (sky, rain, lightning and the island one-shots), seeded once from the clock like the original's.</summary>
	private readonly LobbyEffects lobbyEffects = new( (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() );

	/// <summary>Steps the lobby's island effects for this frame and plays the sounds they raise; the globe eases the sky to its own colour.</summary>
	private void UpdateLobbyEffects()
	{
		if ( Lobby == null )
			return;
		lobbyEffects.EasedSky = CompatibilityRuntime.RenderQuality.SkyLayers > 1;
		if ( backdrop == Backdrop.Globe )
		{
			// [BIN:TPW-EXE:0x5d5c50 lobby frame] the lobby update has no globe branch (the main state stays 2, 0x54e6c7): its flying-mesh system and island clips keep running while the globe is up, so the scene steps as in the lobby
			// [APPROX:UI-018] the islands component (0x5e0470) probably runs too, with its lightning, thunder and nearest-island sky target, before the globe component sets the sky target and no rain each frame; whether it does (the registration order of the globe component against it) and whether the lobby scene is drawn behind the globe are not traced, so only the flyers and clips step and the effects keep the globe's own easing
			for ( var frames = lobbyFrames.Advance( Time.Delta ); frames > 0; frames-- )
				Lobby.StepFrame( ReferenceFrameClock.FrameSeconds );
			Lobby.Animate( Time.Delta );
			Lobby.StepSigns( Menu?.Selected, paused: false );
			lobbyEffects.StepGlobe( Time.Delta );
			return;
		}
		if ( backdrop != Backdrop.Lobby )
		{
			lobbyEffects.StopRain();
			if ( cityScene is { } city )
			{
				// [EXT:frame-driven-at-60hz] the city lobby's islands run the same per-frame update as the lobby's (0x5d5c50), so it steps per reference frame too
				for ( var frames = lobbyFrames.Advance( Time.Delta ); frames > 0; frames-- )
					city.StepFrame( ReferenceFrameClock.FrameSeconds );
				city.Animate( Time.Delta );
				city.StepSigns( null, paused: false );
			}
			return;
		}
		// The lobby's rain and bolts are drawn by the engine's weather renderer, as the original's are (CRain), at the lobby's own scale.
		if ( lobbyWeather == null || !Entity.All.Contains( lobbyWeather ) )
			lobbyWeather = new WeatherRenderer( new LobbyWeatherSource( lobbyEffects ), original => new System.Numerics.Vector3( original.X, original.Z, original.Y ), () => 1f, engineUnitsPerOriginal: 1f );
		lobbyIslands.Clear();
		foreach ( var (island, slot) in Lobby.Slots )
		{
			var (x, y) = Lobby.Definition.PositionOf( slot );
			lobbyIslands.Add( new LobbyEffects.Island( island, x, y ) );
		}
		// [BIN:TPW-EXE:0x5d5c50 lobby frame] the lobby steps its subsystems once per rendered frame in the order they registered: the flying-mesh system (0x5d5aed) before the islands component, so the flyers draw from the generator first
		// [EXT:frame-driven-at-60hz] one such frame per 60 Hz reference frame, whatever OpenTPW's own frame rate; the models' clips below follow the real time
		for ( var frames = lobbyFrames.Advance( Time.Delta ); frames > 0; frames-- )
		{
			Lobby.StepFrame( ReferenceFrameClock.FrameSeconds );
			var (thunders, oneShots) = lobbyEffects.Step( ReferenceFrameClock.FrameSeconds, lobbyIslands, Camera.Position.X, Camera.Position.Y );
			for ( var index = 0; index < thunders; index++ )
				GameAudio.PlayLobbyThunder();
			foreach ( var oneShot in oneShots )
				GameAudio.PlayLobbyOneShot( oneShot );
		}
		Lobby.Animate( Time.Delta );
		// one lobby sign per frame from the second frame on, the selected island's first; none during the zoom into a park, whose island keeps its board then
		Lobby.StepSigns( Menu?.Selected, paused: LobbyEntering );
	}

	/// <summary>The lobby's reference frames (60 Hz) for the frame-driven effects and flyers.</summary>
	private readonly ReferenceFrameClock lobbyFrames = new();

	private readonly List<LobbyEffects.Island> lobbyIslands = new();
	private WeatherRenderer? lobbyWeather;

	/// <summary>Rebuilds the city's islands when its park list changes and points the lobby camera at the selected island.</summary>
	private void UpdateCityScene( UI.CityLobbyScreen city )
	{
		var islands = city.Islands;
		var key = $"{city.City.Id}:{string.Join( ",", islands.Select( park => park.Id ) )}";
		if ( key != cityKey )
		{
			cityScene?.Delete();
			cityScene = null;
			cityKey = key;
			// [BIN:TPW-EXE:0x005E27B0 city islands] the top-10 completion builds one lobby island per list entry with a running slot counter
			// [BIN:TPW-EXE:0x005E27B0 city islands] each entry calls 0x005E3210 with the name its theme id (+0x4c) has in the server's theme list (0x005BD270), the entry and the running counter: 0x005E3210 reads that theme's island file and its ISLAND line, whose slot the counter replaces when it is not negative (0x005E3363), so each park stands as its theme's island at its list position
			// A park of an unknown theme leaves its slot empty.
			var themes = islands.Select( park => Lobby!.Definition.Islands.FirstOrDefault( island => string.Equals( island.Level, park.Level, StringComparison.OrdinalIgnoreCase ) ) ).ToList();
			try
			{
				cityScene = new LobbyScene( Lobby!.Definition, themes, lobbyEffects.Draw, LobbyObjectsPercent );
				cityScene.Visible = backdrop == Backdrop.City;
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException )
			{
				Log.Warning( $"City lobby unavailable: {exception.Message}" );
			}
		}
		if ( cityScene != null )
		{
			var selected = cityScene.Slots.FirstOrDefault( entry => entry.Slot == city.Selected );
			if ( selected.Island != null )
				LobbyCameraMode.Target = cityScene.Target( selected.Island, selected.Slot );
		}
	}

	private Sky? lobbyCloudLayer;
	private bool lobbyCloudLayerLoaded;

	/// <summary>The lobby's cloud layer: the engine sky of the fantasy level's sky files (null where the install has none).</summary>
	// [BIN:TPW-EXE:0x5d8b50 lobby sky] the lobby init loads the engine sky from "Data\Levels\fantasy" (0x5852b0); its draw colours the cloud grid with the colour 0x5d96c0 writes
	private Sky? LobbySky()
	{
		if ( !lobbyCloudLayerLoaded )
		{
			lobbyCloudLayerLoaded = true;
			lobbyCloudLayer = Sky.TryCreate( "fantasy" );
		}
		return lobbyCloudLayer;
	}

	/// <summary>World pass (internal render size): the level, or the lobby sky and 3D scene.</summary>
	public void Render()
	{
		backgroundRenderer.BeginFrame();
		overlayRenderer.BeginFrame();
		var framebuffer = global::Global.Render.MultisampledFramebuffer;
		// The engine fog every fogged shader blends into: the level's fog colour in a park, the lobby's clear colour otherwise (RenderFog).
		// [BIN:TPW-EXE:0x4080e0 fog colour] [0x8bd47c] is the one colour of the viewport clear, the sky's haze and the distant vertices
		// [EXT:evening] at dusk and night the evening extension darkens it (the level's WorldBackdropColour; the same colour at day)
		RenderFog.WorldColour = RenderFog.ColourOf( Level?.WorldBackdropColour ?? RenderFog.LobbyColour );
		if ( Level != null )
		{
			// The park's backdrop: the level's fog colour (what the original's viewport clear and the sky's haze blend with), then the sky over it.
			// [BIN:TPW-EXE:0x4080e0 fog colour] the level load copies ThemeEngine.FogColour into the fog colour [0x8bd47c]; [BIN:TPW-EXE:0x5737aa clear] the viewport clear uses it when render flags & 0x4002000, else black
			// [APPROX:UI-107] the clear is always the fog colour, since the park's render flags (0x4002000 bits of [0x8bcbc8]) are not traced; the sky is Level.DrawSky (World/Sky.cs)
			backgroundBatch.Clear();
			backgroundBatch.AddRectangle( new UiRect( 0, 0, framebuffer.Width, framebuffer.Height ), Level.WorldBackdropColour );
			using ( FrameMarks.Measure( "draw.backdrop" ) )
				backgroundRenderer.Draw( global::Global.Render.CommandList, backgroundBatch, framebuffer.Width, framebuffer.Height );
			using ( FrameMarks.Measure( "draw.sky" ) )
				Level.DrawSky();
			using ( FrameMarks.Measure( "draw.level" ) )
				Level.Render();
			using ( FrameMarks.Measure( "draw.advisorModel" ) )
				Advisor?.Render();
		}
		else
		{
			// The lobby's backdrop. The original clears the screen to the lobby's fog colour (LobbyEffects.ClearColour) and tints only its
			// cloud layer with the island's eased colour (LobbyEffects.Sky); until that layer is drawn, the backdrop shows the cloud colour flat.
			// [BIN:TPW-EXE:0x5d8b50 lobby sky] the lobby loads the engine sky (0x8c1828) from "Data\Levels\fantasy" (0x5852b0: sky.tga, sky_cyl.tga) and its draw (0x5863c0) colours the clouds from the 16 x 16 grid at +0xf60 that 0x5d96c0 fills with one colour each frame (the eased island colour, else the fog colour); the world pass is cleared to the fog colour
			// [APPROX:UI-018] the lobby's own fog (50 to 300 units) is not applied: OpenTPW's world shaders keep their shared fog range (UI-107), and the scene's sky flags (0x5863c0 is gated on bit 0x10 of the scene flags, 0x576a00) are taken as set, as the video's clouds show
			backgroundBatch.Clear();
			var sky = lobbyEffects.Sky;
			// [EXT:tpi-front-end-phase1] the Theme Park Inc Personnel File stands on a flat dark backdrop; the original's 3D intro world (wIntro) is not drawn
			var cloudLayer = IncFrontEnd == null ? LobbySky() : null;
			var backdropColour = IncFrontEnd != null ? new RgbaByte( 14, 18, 38, 255 )
				: cloudLayer != null ? new RgbaByte( LobbyEffects.ClearColour.R, LobbyEffects.ClearColour.G, LobbyEffects.ClearColour.B, 255 ) : new RgbaByte( sky.R, sky.G, sky.B, 255 );
			backgroundBatch.AddRectangle( new UiRect( 0, 0, framebuffer.Width, framebuffer.Height ), backdropColour );
			backgroundRenderer.Draw( global::Global.Render.CommandList, backgroundBatch, framebuffer.Width, framebuffer.Height );
			if ( cloudLayer != null )
			{
				cloudLayer.SetGridColour( 0xFF000000u | (uint)sky.R << 16 | (uint)sky.G << 8 | sky.B );
				cloudLayer.Draw( Time.Delta );
			}
			foreach ( var entity in Entity.All.ToArray() )
				entity.Render();
			// [BIN:TPW-EXE:0x57ee30 lightning flash] the lightning draw tests the flash flag (+4) first and draws one full-screen quad (0x57ed50: corners -1..1, vertex colour 0xFFFFFFFF, state 0x9A1) before it steps the bolts
			// [BIN:TPW-EXE:0x57ed75 flash state] 0x9A1: alpha blend on (bit 0x100), z write off (bit 0x800), source blend INVDESTCOLOR and destination ZERO (low byte 0xA1), so the quad inverts the picture for the frame
			if ( lobbyEffects.Flash && IncFrontEnd == null )
			{
				backgroundBatch.Clear();
				backgroundBatch.AddRectangle( new UiRect( 0, 0, framebuffer.Width, framebuffer.Height ), RgbaByte.White );
				backgroundRenderer.Draw( global::Global.Render.CommandList, backgroundBatch, framebuffer.Width, framebuffer.Height, invert: true );
			}
		}
		Context.Canvas = CurrentCanvas();
		Context.Batch.Clear();
		// [BIN:STP-PPC:engine_shared 0x1D4B4 lightning flash] LightningFlash() sets a flag; the rain render then draws one full-screen white quad for exactly one frame and clears it (lobby only)
		// [DATA:video 78:47-78:48] the capture shows the bolt but no inverted frame (the top third's mean luminance stays at 143-149 over 4729.4-4730.8): a flash that lasts one frame at 60 Hz can fall between the capture's frames, so this does not contradict the inverted flash above
		// [BIN:TPW-EXE:0x57ee30 flash flag] the flag is cleared once the quad is drawn: it lasts one frame
		if ( Level == null && lobbyEffects.Flash )
			lobbyEffects.Flash = false;
		if ( Hud != null )
		{
			using ( FrameMarks.Measure( "draw.hud" ) )
				Hud.Draw( Context );
			using ( FrameMarks.Measure( "draw.advisorUi" ) )
				Advisor?.Draw( Context );
		}
		else if ( IncHud != null )
			IncHud.Draw( Context );
		else if ( IncFrontEnd != null )
			IncFrontEnd.Draw( Context );
		else if ( !LobbyEntering )
			Menu?.Stack.Draw( Context );
		// [BIN:TPW-EXE:0x5d8d22 lobby fade] the enter's fade is a black overlay whose alpha is the eased fade value >> 8
		if ( Camera.Mode is LobbyCameraMode { FadeAlpha: > 0 } fading )
			Context.Batch.AddRectangle( new UiRect( 0, 0, Context.Canvas.Width, Context.Canvas.Height ), new RgbaByte( 0, 0, 0, fading.FadeAlpha ) );
		// [EXT:texture-pack] a running texture switch shows its progress without blocking the game
		if ( TexturePackSwitch.Current is { } textureSwitch )
			OptionsScreen.DrawTextureSwitchProgress( Context, Strings, textureSwitch );
		DrawParkBuildProgress();
		// the game's own pointer goes on top of every UI layer
		Hud?.DrawCursor( Context );
		// the model previews are drawn with a depth buffer into their own images, which the overlay pass shows in the batch's order
		using ( FrameMarks.Measure( "draw.depthImages" ) )
			overlayRenderer.PrepareDepthImages( global::Global.Render.CommandList, Context.Batch, framebuffer );
	}

	/// <summary>Overlay pass (output pixels, possibly twice per frame when the output is captured).</summary>
	private void RenderOverlay()
	{
		var target = global::Global.Render.OverlayFramebuffer;
		using var mark = FrameMarks.Measure( "overlay.ui" );
		overlayRenderer.Draw( global::Global.Render.CommandList, Context.Batch, target.Width, target.Height );
	}

	// ---- Online ------------------------------------------------------------------------------

	/// <summary>
	/// An online entry of the front end or the park HUD, or null while the <c>opentpw.online</c> extension is off: a null
	/// entry hides its button or menu line (FrontEndMenu, HudHost), and the postcard shortcuts check it too.
	/// </summary>
	// [EXT:extension-online-gate] with the online extension off, the lobby's original globe button (b_worldview), Go Online, Publish Park and the postcard entries are hidden; the original always showed them
	internal static Action<UiScreenStack>? WhenOnline( Action<UiScreenStack> entry ) => FeatureGates.Online ? entry : null;

	/// <summary>
	/// The online extension's session, created on the first Go Online and kept across parks. Throws while the
	/// <c>opentpw.online</c> extension is off, so no session or chat overlay is ever created then.
	/// </summary>
	public OnlineSession Online
	{
		get
		{
			if ( !FeatureGates.Online )
				throw new InvalidOperationException( FeatureGates.OnlineDisabledMessage + "." );
			if ( onlineSession == null )
			{
				onlineSession = new OnlineSession( OnlineFolders ?? global::OpenTPW.OnlineFolders.FromEnvironment() );
				// the chat park screen and a visit's chat mode show the room in the original chat overlay, also under a name popup or the postcard composer
				// opened from it; elsewhere (a visit's HUD, F4) the corner box shows it
				chatOverlay = new UI.ChatOverlay( onlineSession )
				{
					Hidden = () => (presence != null && Hud?.ChatMode == true) || ShowsChatScreen( Menu?.Stack ) || ShowsChatScreen( Hud?.Stack ),
				};
				global::Global.Render.OnOverlayRender += chatOverlay.Draw;
			}
			return onlineSession;
		}
	}

	private static bool ShowsChatScreen( UiScreenStack? stack ) => stack?.Screens.Any( screen => screen.Name == UI.OnlineScreens.ChatScreenName ) == true;

	/// <summary>The visit's online players (null: none).</summary>
	internal ParkPresence? Presence => presence;

	/// <summary>Replaces the visit's presence (the visit smoke's scripted players): the flow updates and disposes it as its own.</summary>
	internal void AttachPresence( ParkPresence attached )
	{
		if ( !ReferenceEquals( presence, attached ) )
			presence?.Dispose();
		presence = attached;
	}

	/// <summary>Go Online: the original-style online screens on <paramref name="stack"/> (docs/ONLINE.md).</summary>
	public void ShowOnline( UiScreenStack stack ) => CreateOnlineScreens( stack ).ShowWorld();

	/// <summary>Opens the existing composer only; sending remains an explicit outbox action.</summary>
	internal void ShowPostcard( UiScreenStack stack )
	{
		if ( !FeatureGates.Sharing )
			throw new NotSupportedException( FeatureGates.Online ? "Postcards are unavailable for this edition's experimental slice." : FeatureGates.OnlineDisabledMessage + "." );
		// [BIN:TPW-EXE:0x4a9380 postcard] the postcard button (100 of the camera panel, 0x498ad0) and Ctrl+P (0x481500) run 0x4a9380, which plays cat_ui 0x95 flat before it builds the composer
		GameAudio.PlayUi( PostcardEvent );
		CreateOnlineScreens( stack ).ShowSendPostcard();
	}

	/// <summary>The <c>cat_ui</c> sound the postcard composer opens with (0x95).</summary>
	public const uint PostcardEvent = 0x95;

	private UI.OnlineScreens CreateOnlineScreens( UiScreenStack stack ) => new( stack, Strings, Context.Models, new UI.OnlineHost
	{
		Session = Online,
		Level = () => Level,
		Visit = visit => QueueProgressLevel( visit.Level, () => StartLevel( visit.Level, original: !visit.IsSandbox, developerPanels: false, visit: visit, overFrames: true ) ),
		VisitLevel = ( level, prepare ) => QueueProgressLevel( level, () =>
		{
			if ( PrepareLevelVisit( prepare ) is { } visit )
				StartLevel( level, original: true, developerPanels: false, visit: visit, overFrames: true );
		} ),
		Globe = () => Globe,
		Advise = message => Advisor?.OnOnlineScreenOpened( message ),
		Hosting = this,
	} );

	/// <summary>A chat park's visit, built once its level's files are there; null after a failure, which is shown over the screen it came from.</summary>
	private ParkVisitInfo? PrepareLevelVisit( Func<ParkVisitInfo> prepare )
	{
		try
		{
			return prepare();
		}
		catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException )
		{
			Log.Warning( $"Chat park visit failed: {error.Message}" );
			var stack = Hud?.Stack ?? Menu?.Stack;
			stack?.Push( UiDialogs.Message( "visitFailed", () => error.Message, (() => Strings.Extra( OpenTpwText.Back ), () => stack.Pop()) ) );
			return null;
		}
	}

	public void Dispose()
	{
		// [EXT:online-coop-lockstep] quitting leaves a shared park's session while the link can still say so (a host's leave ends it for everyone)
		LeaveCoopSession( "The player quit the game." );
		FlushRestartSnapshot();
		// a window closed past the in-app Quit's check still writes earned awards a failed save left behind
		if ( progressOwner is { Dirty: true } owner && !owner.TryFlush() )
			Log.Warning( $"OpenTPW progress could not be saved on exit: {owner.Error}" );
		DetachProgress();
		AbandonParkBuild();
		presence?.Dispose();
		presence = null;
		coopShare?.Dispose();
		coopShare = null;
		Advisor?.Dispose();
		GameAudio.Shutdown();
		Hud?.Stack.Clear();
		Menu?.Stack.Clear();
		IncFrontEnd?.Stack.Clear();
		if ( chatOverlay != null )
			global::Global.Render.OnOverlayRender -= chatOverlay.Draw;
		chatOverlay?.Dispose();
		onlineSession?.Dispose();
		global::Global.Render.OnOverlayRender -= RenderOverlay;
		Level?.DetachOverlay();
		Level?.TextOverlay?.Dispose();
		backgroundRenderer.Dispose();
		overlayRenderer.Dispose();
	}
}
