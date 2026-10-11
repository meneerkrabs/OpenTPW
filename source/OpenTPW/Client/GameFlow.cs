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
internal sealed class GameFlow : IDisposable
{
	private readonly UiRenderer backgroundRenderer;
	private readonly UiRenderer overlayRenderer;
	private readonly UiInputSource inputSource = new();
	private readonly UiBatch backgroundBatch = new();
	private Action? pending;
	private string? pendingLevel;
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

	public UiStringTable Strings { get; }
	public UiContext Context { get; }
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
	/// <summary>The level of the Theme Park Inc viewer, which has no <see cref="Hud"/> (docs/THEME-PARK-INC.md).</summary>
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
		pending = transition;
		pendingLevel = null;
	}

	/// <summary>Queues a transition into <paramref name="level"/>; it waits until <see cref="LevelDataReady"/> says the level's files are there.</summary>
	public void QueueLevel( string level, Action transition )
	{
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
			StartPark = ( island, mode ) => QueueLevel( island.Level, () => StartPark( island.Level, mode ) ),
			Load = entry => QueueLevel( entry.Level, () => LoadPark( entry ) ),
			Quit = Quit,
			CreateOptions = CreateOptions,
			IslandSelected = island => LobbyCameraMode.Target = Lobby!.Target( island ),
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
			var diagnostics = new List<string>();
			var textureSwitch = TexturePackSwitch.Begin( name, diagnostics );
			foreach ( var diagnostic in diagnostics )
				Log.Warning( $"Texture pack: {diagnostic}" );
			return textureSwitch;
		},
		Options = GameOptions.Current,
		Languages = GameLanguage.Choosable(),
		CurrentLanguage = GameLanguage.Current.Name,
		SaveOptions = () => { if ( PersistSettings ) GameOptions.Current.Save( OptionsPath ); },
		SaveLanguage = language =>
		{
			if ( !PersistSettings )
				return;
			Settings.Default.Language = language;
			Settings.Default.Save();
		},
	}, closed );

	public IReadOnlyList<ParkLoadEntry> FindLoadEntries()
	{
		var entries = new List<ParkLoadEntry>();
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
	public void StartPark( string levelName, GameMode mode )
	{
		var start = ParkStart.FromFrontEnd( mode );
		StartLevel( levelName, original: true, developerPanels: false, start: start );
		Log.Trace( $"Started {levelName} as a new {start} park from the front end." );
	}

	public void LoadPark( ParkLoadEntry entry )
	{
		if ( entry.Sandbox )
		{
			StartLevel( "jungle", original: false, developerPanels: false );
			Level!.LoadSandbox();
		}
		else
			// [APPROX:UI-041] a loaded shipped park is the reference start whatever Game Mode was last chosen; the original takes the mode from the loading player's profile, not the park — evidence needed: player profiles and the Mac park loader 0x11acfc
			StartLevel( entry.Level, original: true, developerPanels: false );
	}

	/// <summary>
	/// Creates a level with the original HUD. CLI paths keep the developer panels. Original levels
	/// default to the read-only reference start (shipped save imported as it was made).
	/// </summary>
	public Level StartLevel( string levelName, bool original, bool developerPanels, ParkVisitInfo? visit = null, ParkStartKind start = ParkStartKind.OriginalSaveReference )
	{
		TearDown();
		var level = new Level( levelName, loadOriginalLevel: original, visit: visit, onlineFolders: OnlineFolders, start: start ) { ShowDeveloperPanels = developerPanels };
		Level = level;
		if ( original )
			GameAudio.EnterPark( levelName, level.Seed.SoundStream );
		StartKind = level.Park?.Start.Kind;
		// [EXT:tpi-viewer] Theme Park Inc worlds are viewed without economy, HUD or advisor (the camera, Esc and P only).
		if ( GameEditions.IsThemeParkInc )
		{
			ViewerLevel = level;
			Log.Trace( $"Theme Park Inc viewer: {levelName} without economy, HUD or advisor. Esc quits, P pauses." );
			return level;
		}
		// Money, calendar, speed and purchases come from the park economy of original levels (Level.Park,
		// looked up on every access so loading a park save is followed); the generic sandbox has none.
		IHudParkStatus status = level.Park != null ? EconomyParkStatus.ForLevel( level ) : new NoEconomyStatus();
		Hud = new ParkHud( level, Strings, status, new OriginalBuildCatalog( level.Objects.Catalog ), new HudHost
		{
			ExitToLobby = () => Queue( () => ShowFrontEnd( levelName ) ),
			Quit = Quit,
			CreateOptions = CreateOptions,
			CreateLoad = stack => FrontEndMenu.CreateLoadScreen( stack, Strings, FindLoadEntries(), entry => QueueLevel( entry.Level, () => LoadPark( entry ) ) ),
			GoOnline = ShowOnline,
		} );
		// [EXT:online-visit] read-only visits of shared parks get no advisor (the original has no visits)
		// [EXT:sandbox] the generic sandbox (not an original level) gets no automatic advisor either; only original levels raise its game events
		if ( AutomaticAdvisorEnabled && original && visit == null && !advisorUnavailable )
		{
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

	private void TearDown()
	{
		Hud?.Stack.Clear();
		Menu?.Stack.Clear();
		Advisor?.DetachLevel();
		GameAudio.LeavePark();
		if ( Level != null )
		{
			Level.Dispose();
			Level = null;
			Hud = null;
			ViewerLevel = null;
			StartKind = null;
			OpenTPW.Level.Current = null!;
		}
		foreach ( var entity in Entity.All.ToArray() )
			entity.Delete();
		Lobby = null;
		Globe = null;
		shownCities = null;
		cityScene = null;
		cityKey = null;
		backdrop = Backdrop.Lobby;
		Menu = null;
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
		if ( pending != null && (pendingLevel == null || LevelDataReady?.Invoke( pendingLevel ) != false) )
		{
			var transition = pending;
			pending = null;
			pendingLevel = null;
			transition();
			Transitioned?.Invoke();
		}
		Context.Canvas = CurrentCanvas();
		Context.Time = Time.Now;
		Context.Delta = Time.Delta;
		Context.PopupHelp = GameOptions.Current.PopupHelp;
		// Input is in logical window units; the UI works in drawable pixels.
		var logical = Screen.Size;
		var input = InjectedInput ?? inputSource.Poll( Context.Canvas.Width / (float)Math.Max( 1, logical.X ), Context.Canvas.Height / (float)Math.Max( 1, logical.Y ) );
		InjectedInput = null;
		Input.TextEntryActive = false;
		onlineSession?.Pump();
		var imguiMouse = ImGuiNET.ImGui.GetIO().WantCaptureMouse;
		if ( imguiMouse )
			input = input with { LeftPressed = false, LeftReleased = false, RightPressed = false, Wheel = 0 };

		if ( Level != null && Hud != null )
		{
			Level.UiCapturesMouse = Hud.Update( Context, input );
			Level.SimulationTimeScale = Hud.Status.TimeScale;
			Level.Update();
			Advisor?.Update( Hud.Paused );
			GameAudio.Update( Level.Guests?.GetStatistics().InPark );
			return;
		}
		if ( Level != null && ViewerLevel == Level )
		{
			UpdateViewer( Level, input );
			return;
		}
		GameAudio.EnsureStarted();
		GameAudio.Update();
		UpdateGlobeView();
		Camera.Update();
		Menu?.Stack.Update( Context, input );
		foreach ( var entity in Entity.All.ToArray() )
			entity.Update();
	}

	/// <summary>The Theme Park Inc viewer's frame: Esc quits, P pauses and resumes, the camera moves as in a park.</summary>
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
		backdrop = wanted;
		if ( Globe != null )
			Globe.Visible = wanted == Backdrop.Globe;
		if ( cityScene != null )
			cityScene.Visible = wanted == Backdrop.City;
		if ( Lobby != null )
			Lobby.Visible = wanted == Backdrop.Lobby;
	}

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
			// [DATA:lobby.wad:<theme>.txt SKYCOLOUR] [APPROX:UI-018] drawn as a flat backdrop — evidence needed: capture of the lobby sky
			// Lobby sky: the SKYCOLOUR of the selected island behind the 3D scene.
			backgroundBatch.Clear();
			var sky = Menu?.Selected.SkyColour ?? ((byte)40, (byte)90, (byte)160);
			backgroundBatch.AddRectangle( new UiRect( 0, 0, framebuffer.Width, framebuffer.Height ), new RgbaByte( sky.R, sky.G, sky.B, 255 ) );
			backgroundRenderer.Draw( global::Global.Render.CommandList, backgroundBatch, framebuffer.Width, framebuffer.Height );
			foreach ( var entity in Entity.All.ToArray() )
				entity.Render();
		}
		Context.Canvas = CurrentCanvas();
		Context.Batch.Clear();
		if ( Hud != null )
		{
			Hud.Draw( Context );
			Advisor?.Draw( Context );
		}
		else
			Menu?.Stack.Draw( Context );
		// [EXT:texture-pack] a running texture switch shows its progress without blocking the game
		if ( TexturePackSwitch.Current is { } textureSwitch )
			OptionsScreen.DrawTextureSwitchProgress( Context, Strings, textureSwitch );
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
	public void ShowOnline( UiScreenStack stack ) => new UI.OnlineScreens( stack, Strings, Context.Models, new UI.OnlineHost
	{
		Session = Online,
		Level = () => Level,
		Visit = visit => QueueLevel( visit.Level, () => StartLevel( visit.Level, original: !visit.IsSandbox, developerPanels: false, visit: visit ) ),
		Globe = () => Level == null ? Globe : null,
	} ).ShowWorld();

	public void Dispose()
	{
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
