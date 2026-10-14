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

	public GameFlow()
	{
		UiApproximations.LogOnce();
		Strings = UiStringTable.Load( GameLanguage.Current );
		Context = new UiContext( Strings, UiFonts.Load( GameLanguage.Current ), new UiModels() );
		OptionsPath = SaveFileSystem.GetAbsolutePath( GameOptions.FileName );
		GameOptions.Current = GameOptions.Load( OptionsPath );
		Display = IDisplaySettings.Instance ?? new StubDisplaySettings();
		// Sky backdrop in the world pass; menus/HUD in the overlay pass at output pixels (after world upscaling).
		backgroundRenderer = new UiRenderer( global::Global.Render.MultisampledFramebuffer.OutputDescription );
		overlayRenderer = new UiRenderer( global::Global.Render.OutputDescription );
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
	/// <summary>True when selecting a pack opens its download page instead of an immediate texture switch.</summary>
	public Func<UiScreenStack, string, bool>? PrepareTexturePack { get; set; }
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

	public void ShowFrontEnd( string? selectLevel = null )
	{
		TearDown();
		lobbyDefinition ??= LobbyDefinition.Load();
		Lobby = new LobbyScene( lobbyDefinition );
		LobbyCameraMode.Radius = lobbyDefinition.SpinRadius;
		LobbyCameraMode.VerticalOffset = lobbyDefinition.VerticalOffset;
		LobbyCameraMode.SpinSpeed = lobbyDefinition.SpinSpeed;
		Menu = new FrontEndMenu( Strings, lobbyDefinition.Islands, new FrontEndActions
		{
			StartPark = ( island, mode ) => QueueLevel( island.Level, () => StartPark( island.Level, mode, overFrames: true ), prepareBuyWindow: true ),
			Load = entry => QueueLevel( entry.Level, () => LoadPark( entry, overFrames: true ), prepareBuyWindow: true ),
			ContinueEntry = FindContinueEntry,
			ContinuePark = entry => QueueLevel( entry.Park.Level, () => ContinuePark( entry ), prepareBuyWindow: true ),
			Quit = Quit,
			CreateOptions = CreateOptions,
			IslandSelected = island =>
			{
				LobbyCameraMode.Target = Lobby!.Target( island );
				// [BIN:STP-PPC:0x1009598C lobby audio start] the front end starts the selected island's theme lobby music (cat_locallobbymusic 1) and looping ambience (cat_locallobbysfx 2)
				// [APPROX:AUDIO-010] the sentences restart on every island selection; when the original switches them is not traced
				GameAudio.EnterLobby( island.Level );
			},
			LoadEntries = FindLoadEntries,
			GoOnline = ShowOnline,
		} );
		Menu.SelectIsland( 0 );
		if ( selectLevel != null )
			Menu.SelectLevel( selectLevel );
		Camera.SetCameraMode<LobbyCameraMode>();
		Log.Trace( $"Front end: lobby with {Lobby.IslandCount} islands ({Lobby.PartCount} draw batches)." );
	}

	public UiScreen CreateOptions( UiScreenStack stack, Action closed ) => OptionsScreen.Create( stack, Strings, new OptionsServices
	{
		Display = Display,
		Graphics = IGraphicsSettings.Instance,
		TexturePacks = TexturePack.InstalledPacks(),
		BeginTexturePackSwitch = name =>
		{
			if ( PrepareTexturePack?.Invoke( stack, name ) == true )
				return null;
			var diagnostics = new List<string>();
			var textureSwitch = TexturePackSwitch.Begin( name, diagnostics );
			foreach ( var diagnostic in diagnostics )
				Log.Warning( $"Texture pack: {diagnostic}" );
			return textureSwitch;
		},
		BeginInterfaceSwitch = enhanced =>
		{
			var diagnostics = new List<string>();
			var textureSwitch = TexturePackSwitch.Begin( IGraphicsSettings.Instance?.Current.TexturePackName, diagnostics, enhancedInterface: enhanced );
			foreach ( var diagnostic in diagnostics )
				Log.Warning( $"Interface art: {diagnostic}" );
			return textureSwitch;
		},
		Options = GameOptions.Current,
		Languages = GameLanguage.Choosable(),
		// A choice still waiting for the park to be left is what the row shows, so it can also be taken back.
		CurrentLanguage = pendingLanguage ?? GameLanguage.Current.Name,
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
				entries.Add( new ParkLoadEntry( saved.Theme, -1, false, path, saved.DisplayName ) );
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
		var start = ParkStart.FromFrontEnd( mode );
		StartLevel( levelName, original: true, developerPanels: false, start: start, overFrames: overFrames,
			ready: _ => Log.Trace( $"Started {levelName} as a new {start} park from the front end." ) );
	}

	/// <param name="overFrames">As for <see cref="StartPark"/>; a park save of the player's own is still resumed at once.</param>
	public void LoadPark( ParkLoadEntry entry, bool overFrames = false )
	{
		if ( entry.OwnSavePath != null )
		{
			try { ResumePark( ParkResumeSaveFile.Load( entry.OwnSavePath ) ); }
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
			// [APPROX:UI-041] a loaded shipped park is the reference start whatever Game Mode was last chosen; the original takes the mode from the loading player's profile, not the park — evidence needed: player profiles and the Mac park loader 0x11acfc
			StartLevel( entry.Level, original: true, developerPanels: false, overFrames: overFrames );
	}

	/// <summary>
	/// Creates a level with the original HUD. CLI paths keep the developer panels. Original levels
	/// default to the read-only reference start (shipped save imported as it was made).
	/// </summary>
	public Level StartLevel( string levelName, bool original, bool developerPanels, ParkVisitInfo? visit = null, ParkStartKind start = ParkStartKind.OriginalSaveReference )
	{
		using var profile = LoadProfile.Measure( "start-level" );
		using ( LoadProfile.Measure( "tear-down" ) )
			TearDown();
		Level level;
		using ( LoadProfile.Measure( "level" ) )
			level = new Level( levelName, loadOriginalLevel: original, visit: visit, onlineFolders: OnlineFolders, start: start ) { ShowDeveloperPanels = developerPanels };
		return EnterLevel( level, levelName, original, visit );
	}

	/// <summary>The rest of <see cref="StartLevel"/> once its level is built and current: park audio, HUD and advisor.</summary>
	private Level EnterLevel( Level level, string levelName, bool original, ParkVisitInfo? visit )
	{
		Level = level;
		if ( original )
			using ( LoadProfile.Measure( "enter-park-audio" ) )
				GameAudio.EnterPark( levelName, level.Seed.SoundStream );
		StartKind = level.Park?.Start.Kind;
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
		// Money, calendar, speed and purchases come from the park economy of original levels (Level.Park,
		// looked up on every access so loading a park save is followed); the generic sandbox has none.
		IHudParkStatus status = level.Park != null ? EconomyParkStatus.ForLevel( level ) : new NoEconomyStatus();
		using ( LoadProfile.Measure( "park-hud" ) )
			Hud = CreateParkHud( level, status );
		// [EXT:online-visit] read-only visits of shared parks get no advisor (the original has no visits)
		// [EXT:sandbox] the generic sandbox (not an original level) gets no automatic advisor either; only original levels raise its game events
		if ( AutomaticAdvisorEnabled && original && visit == null && !advisorUnavailable )
		{
			using ( LoadProfile.Measure( "advisor" ) )
				Advisor ??= AutomaticAdvisor.TryCreate();
			advisorUnavailable = Advisor == null;
			Advisor?.AttachLevel( level );
		}
		if ( Advisor?.IsAttached != true && level.Park?.Import is { } import && level.OriginalPark?.Save?.Advisor is { } saved )
		{
			var dropped = $"[APPROX:SAVE-010] No automatic advisor is attached ({(advisorUnavailable ? "unavailable" : "disabled")}): the saved advisor controller's {OriginalEconomyImport.AdvisorRecords( saved )} are dropped.";
			import.Report( dropped );
			Log.Trace( dropped );
		}
		return level;
	}

	private ParkHud CreateParkHud( Level level, IHudParkStatus status ) =>
		new( level, Strings, status, new OriginalBuildCatalog( level.Objects.Catalog ), new HudHost
		{
			ExitToLobby = () => Queue( () => ShowFrontEnd( level.LevelName ) ),
			Quit = Quit,
			SavePark = SaveCurrentPark,
			OpenPostcard = ShowPostcard,
			CreateOptions = CreateOptions,
			CreateLoad = stack => FrontEndMenu.CreateLoadScreen( stack, Strings, FindLoadEntries(), entry => QueueLevel( entry.Level, () => LoadPark( entry, overFrames: true ), prepareBuyWindow: true ) ),
			GoOnline = ShowOnline,
		} );

	/// <summary>
	/// Guests whose cell is within four cells of the camera's ground target — the crowd sentence's parameter
	/// (<c>cat_kids</c> 0x5B, capped at 100). The original's park turn counts them the same way each turn.
	/// </summary>
	// [BIN:STP-PPC:0x101C2490 park turn] guests within four cells of the camera are counted (0x100C313C, radius argument 4) for the crowd sentence
	// [APPROX:AUDIO-006] cells are counted by rectangle (Chebyshev distance); the original's area shape inside the radius is not traced
	private static int GuestsNearCamera( Level level )
	{
		if ( level.Guests == null || Camera.Mode is not ParkCameraMode camera )
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
	public const int CrowdRadiusCells = 4;

	private void TearDown( bool applyPendingLanguage = true )
	{
		AbandonParkBuild();
		Hud?.Stack.Clear();
		Menu?.Stack.Clear();
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
		// [EXT:texture-pack] the pack switch belongs to the game loop, not to its loading screen
		TexturePackSwitch.PumpCurrent();
		// A park being built over frames (a transition queued meanwhile replaces it).
		if ( parkBuild != null )
			PumpParkBuild();
		// A park waits for a language chosen just before it (its speech files may still be coming) and is then entered in it:
		// the transition's tear-down applies the choice before the park is built.
		else if ( pending != null )
		{
			var ready = pendingLevel == null || (SettlePendingLanguage() && LevelDataReady?.Invoke( pendingLevel ) != false);
			if ( ready )
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
			else if ( prepareBuyWindowArt && TexturePackSwitch.Current == null )
			{
				// Global UI files are ready while the browser copies theme files. Prepare only
				// buy-window art here; previews and signs still load only when requested.
				buyWindowPreparation ??= new UiImagePreparation( ParkHud.BuyWindowImagePaths( Context ) );
				var start = System.Diagnostics.Stopwatch.GetTimestamp();
				buyWindowPreparation.Pump( overlayRenderer.PrepareImage,
					() => System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds, 6 );
			}
		}
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
			input = UiInput.Idle( input.Mouse );
		Input.TextEntryActive = false;
		onlineSession?.Pump();
		var imguiMouse = ImGuiNET.ImGui.GetIO().WantCaptureMouse;
		if ( imguiMouse )
			input = input with { LeftPressed = false, LeftReleased = false, RightPressed = false, Wheel = 0 };

		if ( Level != null && Hud != null )
		{
			Level.UiCapturesMouse = Hud.Update( Context, input ) || parkBuild != null;
			// The camera reads the keyboard itself: while the next park is built it stays where it is, as under a modal window.
			if ( parkBuild != null && Camera.Mode is ParkCameraMode parkCamera )
				parkCamera.ModalOpen = true;
			if ( parkBuild != null && Camera.Mode is FreeWalkCameraMode walkCamera )
				walkCamera.ModalOpen = true;
			UpdateParkGlobe( Hud );
			// The pause menu and a texture-pack download stop the park; the speed setting is kept (ParkHud.TimeScaleFor).
			// The park being left waits too while the next one is built.
			Level.SimulationTimeScale = parkBuild != null ? 0 : Hud.SimulationTimeScale;
			Level.Update();
			Advisor?.Update( Hud.Paused );
			GameAudio.Update( Level.Guests?.GetStatistics().InPark );
			GameAudio.SetCrowd( GuestsNearCamera( Level ) );
			GameAudio.UpdateStaffVoices( Level.StaffWorld?.Snapshots ?? Array.Empty<StaffWorldSnapshot>(), Level.Park?.Economy.Turn ?? -1, Level.StaffWorld?.DrivesHandymen == true );
			if ( Level.StaffWorld != null )
				GameAudio.PlayStaffVoices( Level.StaffWorld.TakeVoices() );
			GameAudio.SetRain( Level.Weather?.RainLevel ?? 0 );
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
		GameAudio.EnsureStarted();
		GameAudio.Update();
		UpdateGlobeView();
		Camera.Update();
		UpdateLobbyEffects();
		Menu?.Stack.Update( Context, input );
		foreach ( var entity in Entity.All.ToArray() )
			entity.Update();
		// After the frame's input and once every dialog is closed (a display change may still wait for confirmation), so the
		// options screen that asked for it is not torn down while it runs.
		if ( pendingLanguage != null && Level == null && Menu != null && Menu.Stack.Top == Menu.Main && SettlePendingLanguage() && pendingLanguage != null )
		{
			var selected = Menu.Selected.Level;
			ShowFrontEnd( selected );
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
			if ( backdrop == Backdrop.Lobby )
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

	/// <summary>The front end's island effects (sky, rain, lightning and the island one-shots), seeded once from the clock like the original's.</summary>
	private readonly LobbyEffects lobbyEffects = new( (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() );

	/// <summary>Steps the lobby's island effects for this frame and plays the sounds they raise; the globe eases the sky to its own colour.</summary>
	private void UpdateLobbyEffects()
	{
		if ( Lobby == null )
			return;
		if ( backdrop == Backdrop.Globe )
		{
			lobbyEffects.StepGlobe( Time.Delta );
			return;
		}
		if ( backdrop != Backdrop.Lobby )
		{
			lobbyEffects.StopRain();
			return;
		}
		// The lobby's rain and bolts are drawn by the engine's weather renderer, as the original's are (CRain), at the lobby's own scale.
		if ( lobbyWeather == null || !Entity.All.Contains( lobbyWeather ) )
			lobbyWeather = new WeatherRenderer( new LobbyWeatherSource( lobbyEffects ), original => new System.Numerics.Vector3( original.X, original.Z, original.Y ), () => false, engineUnitsPerOriginal: 1f );
		lobbyIslands.Clear();
		foreach ( var (island, slot) in Lobby.Slots )
		{
			var (x, y) = Lobby.Definition.PositionOf( slot );
			lobbyIslands.Add( new LobbyEffects.Island( island, x, y ) );
		}
		var (thunders, oneShots) = lobbyEffects.Step( Time.Delta, lobbyIslands, Camera.Position.X, Camera.Position.Y );
		for ( var index = 0; index < thunders; index++ )
			GameAudio.PlayLobbyThunder();
		foreach ( var oneShot in oneShots )
			GameAudio.PlayLobbyOneShot( oneShot );
	}

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
			// [APPROX:ONLINE-016] each park stands as its theme's island (as the own lobby builds its four) — evidence needed: what 0x005E27B0 passes to 0x005E3210 per entry
			// A park of an unknown theme leaves its slot empty.
			var themes = islands.Select( park => Lobby!.Definition.Islands.FirstOrDefault( island => string.Equals( island.Level, park.Level, StringComparison.OrdinalIgnoreCase ) ) ).ToList();
			try
			{
				cityScene = new LobbyScene( Lobby!.Definition, themes );
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

	/// <summary>World pass (internal render size): the level, or the lobby sky and 3D scene.</summary>
	public void Render()
	{
		backgroundRenderer.BeginFrame();
		overlayRenderer.BeginFrame();
		var framebuffer = global::Global.Render.MultisampledFramebuffer;
		if ( Level != null )
		{
			Level.Render();
			Advisor?.Render();
		}
		else
		{
			// [DATA:lobby.wad:<theme>.txt SKYCOLOUR] Lobby sky: one flat colour eased toward the nearest island's SKYCOLOUR (LobbyEffects), behind the 3D scene.
			backgroundBatch.Clear();
			var sky = lobbyEffects.HasSky ? lobbyEffects.Sky : Menu?.Selected.SkyColour ?? ((byte)40, (byte)90, (byte)160);
			backgroundBatch.AddRectangle( new UiRect( 0, 0, framebuffer.Width, framebuffer.Height ), new RgbaByte( sky.R, sky.G, sky.B, 255 ) );
			backgroundRenderer.Draw( global::Global.Render.CommandList, backgroundBatch, framebuffer.Width, framebuffer.Height );
			foreach ( var entity in Entity.All.ToArray() )
				entity.Render();
		}
		Context.Canvas = CurrentCanvas();
		Context.Batch.Clear();
		// [BIN:STP-PPC:engine_shared 0x1D4B4 lightning flash] LightningFlash() sets a flag; the rain render then draws one full-screen white quad for exactly one frame and clears it (lobby only)
		// [APPROX:UI-018] the flash's blend flags (0x9A1) are not decoded: drawn as opaque white under the menus
		if ( Level == null && lobbyEffects.Flash )
		{
			Context.Batch.AddRectangle( new UiRect( 0, 0, Context.Canvas.Width, Context.Canvas.Height ), new RgbaByte( 255, 255, 255, 255 ) );
			lobbyEffects.Flash = false;
		}
		if ( Hud != null )
		{
			Hud.Draw( Context );
			Advisor?.Draw( Context );
		}
		else if ( IncHud != null )
			IncHud.Draw( Context );
		else
			Menu?.Stack.Draw( Context );
		// [EXT:texture-pack] a running texture switch shows its progress without blocking the game
		if ( TexturePackSwitch.Current is { } textureSwitch )
			OptionsScreen.DrawTextureSwitchProgress( Context, Strings, textureSwitch );
		DrawParkBuildProgress();
	}

	/// <summary>Overlay pass (output pixels, possibly twice per frame when the output is captured).</summary>
	private void RenderOverlay()
	{
		var target = global::Global.Render.OverlayFramebuffer;
		overlayRenderer.Draw( global::Global.Render.CommandList, Context.Batch, target.Width, target.Height );
	}

	// ---- Online ------------------------------------------------------------------------------

	/// <summary>The online extension's session, created on the first Go Online and kept across parks.</summary>
	public OnlineSession Online
	{
		get
		{
			if ( onlineSession == null )
			{
				onlineSession = new OnlineSession( OnlineFolders ?? global::OpenTPW.OnlineFolders.FromEnvironment() );
				chatOverlay = new UI.ChatOverlay( onlineSession );
				global::Global.Render.OnOverlayRender += chatOverlay.Draw;
			}
			return onlineSession;
		}
	}

	/// <summary>Go Online: the original-style online screens on <paramref name="stack"/> (docs/ONLINE.md).</summary>
	public void ShowOnline( UiScreenStack stack ) => CreateOnlineScreens( stack ).ShowWorld();

	/// <summary>Opens the existing composer only; sending remains an explicit outbox action.</summary>
	internal void ShowPostcard( UiScreenStack stack )
	{
		if ( !GameEditions.Capabilities.Sharing )
			throw new NotSupportedException( "Postcards are unavailable for this edition's experimental slice." );
		CreateOnlineScreens( stack ).ShowSendPostcard();
	}

	private UI.OnlineScreens CreateOnlineScreens( UiScreenStack stack ) => new( stack, Strings, Context.Models, new UI.OnlineHost
	{
		Session = Online,
		Level = () => Level,
		Visit = visit => QueueLevel( visit.Level, () => StartLevel( visit.Level, original: !visit.IsSandbox, developerPanels: false, visit: visit, overFrames: true ) ),
		Globe = () => Globe,
	} );

	public void Dispose()
	{
		AbandonParkBuild();
		Advisor?.Dispose();
		GameAudio.Shutdown();
		Hud?.Stack.Clear();
		Menu?.Stack.Clear();
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
