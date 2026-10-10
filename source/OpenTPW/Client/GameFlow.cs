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
	public FrontEndMenu? Menu { get; private set; }
	public LobbyScene? Lobby { get; private set; }
	public UiInput? InjectedInput { get; set; }
	/// <summary>How the current original level started; null in the front end and the generic sandbox.</summary>
	public ParkStartKind? StartKind { get; private set; }
	/// <summary>Raised after a queued transition ran.</summary>
	public event Action? Transitioned;

	public void Queue( Action transition ) => pending = transition;

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
			StartPark = ( island, mode ) => Queue( () => StartPark( island.Level, mode ) ),
			Load = entry => Queue( () => LoadPark( entry ) ),
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
		// Money, calendar, speed and purchases come from the park economy of original levels (Level.Park,
		// looked up on every access so loading a park save is followed); the generic sandbox has none.
		IHudParkStatus status = level.Park != null ? EconomyParkStatus.ForLevel( level ) : new NoEconomyStatus();
		Hud = new ParkHud( level, Strings, status, new OriginalBuildCatalog( level.Objects.Catalog ), new HudHost
		{
			ExitToLobby = () => Queue( () => ShowFrontEnd( levelName ) ),
			Quit = Quit,
			CreateOptions = CreateOptions,
			CreateLoad = stack => FrontEndMenu.CreateLoadScreen( stack, Strings, FindLoadEntries(), entry => Queue( () => LoadPark( entry ) ) ),
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
			StartKind = null;
			OpenTPW.Level.Current = null!;
		}
		foreach ( var entity in Entity.All.ToArray() )
			entity.Delete();
		Lobby = null;
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
		if ( pending != null )
		{
			var transition = pending;
			pending = null;
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
			Advisor?.Update();
			GameAudio.Update( Level.Guests?.GetStatistics().InPark );
			return;
		}
		GameAudio.EnsureStarted();
		GameAudio.Update();
		Camera.Update();
		Menu?.Stack.Update( Context, input );
		foreach ( var entity in Entity.All.ToArray() )
			entity.Update();
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
			Hud.Draw( Context );
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
		Visit = visit => Queue( () => StartLevel( visit.Level, original: !visit.IsSandbox, developerPanels: false, visit: visit ) ),
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
