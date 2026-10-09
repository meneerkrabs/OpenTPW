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
	private readonly UiRenderer renderer;
	private readonly UiInputSource inputSource = new();
	private readonly UiBatch backgroundBatch = new();
	private Action? pending;
	private LobbyDefinition? lobbyDefinition;

	public GameFlow()
	{
		Strings = UiStringTable.Load( GameLanguage.Current );
		Context = new UiContext( Strings, UiFonts.Load( GameLanguage.Current ), new UiModels() );
		OptionsPath = SaveFileSystem.GetAbsolutePath( GameOptions.FileName );
		GameOptions.Current = GameOptions.Load( OptionsPath );
		var size = new DisplayResolution( Settings.Default.GameWindowSize.X, Settings.Default.GameWindowSize.Y );
		Display = new StubDisplaySettings( GameOptions.Current, size, new DisplayResolution( (int)Screen.Width, (int)Screen.Height ), PersistResolution );
		renderer = new UiRenderer( global::Global.Render.MultisampledFramebuffer.OutputDescription );
	}

	public UiStringTable Strings { get; }
	public UiContext Context { get; }
	public IDisplaySettings Display { get; set; }
	public string OptionsPath { get; set; }
	/// <summary>When false, options are not written (smoke tests).</summary>
	public bool PersistSettings { get; set; } = true;
	public Level? Level { get; private set; }
	public ParkHud? Hud { get; private set; }
	public FrontEndMenu? Menu { get; private set; }
	public LobbyScene? Lobby { get; private set; }
	public UiInput? InjectedInput { get; set; }
	public GameMode Mode { get; private set; } = GameMode.FullSimulation;
	/// <summary>Raised after a queued transition ran.</summary>
	public event Action? Transitioned;

	private void PersistResolution( DisplayResolution resolution )
	{
		if ( !PersistSettings )
			return;
		Settings.Default.GameWindowSize = new System.Drawing.Point( resolution.Width, resolution.Height );
		Settings.Default.Save();
	}

	public void Queue( Action transition ) => pending = transition;

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
		Options = GameOptions.Current,
		Languages = GameLanguage.FindLanguages( GameLanguage.Current.BaseDataDirectory, GameLanguage.Current.OverlayDataDirectory ),
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

	/// <summary>New park from the lobby: the original level (terrain, MAP rules, Easymode import where shipped).</summary>
	public void StartPark( string levelName, GameMode mode )
	{
		Mode = mode;
		StartLevel( levelName, original: true, developerPanels: false );
		Log.Trace( $"Started {levelName} in {mode} mode from the front end (both modes currently play the same)." );
	}

	public void LoadPark( ParkLoadEntry entry )
	{
		if ( entry.Sandbox )
		{
			StartLevel( "jungle", original: false, developerPanels: false );
			Level!.LoadSandbox();
		}
		else
			StartLevel( entry.Level, original: true, developerPanels: false );
	}

	/// <summary>Creates a level with the original HUD. CLI paths keep the developer panels.</summary>
	public Level StartLevel( string levelName, bool original, bool developerPanels )
	{
		TearDown();
		var level = new Level( levelName, loadOriginalLevel: original ) { ShowDeveloperPanels = developerPanels };
		Level = level;
		Hud = new ParkHud( level, Strings, StubParkStatus.ForLevel( levelName ), new TotemBuildCatalog( "jungle" ), new HudHost
		{
			ExitToLobby = () => Queue( () => ShowFrontEnd( levelName ) ),
			Quit = Quit,
			CreateOptions = CreateOptions,
			CreateLoad = stack => FrontEndMenu.CreateLoadScreen( stack, Strings, FindLoadEntries(), entry => Queue( () => LoadPark( entry ) ) ),
		} );
		return level;
	}

	private void TearDown()
	{
		if ( Level != null )
		{
			Level.TextOverlay?.Dispose();
			Level = null;
			Hud = null;
			OpenTPW.Level.Current = null!;
		}
		foreach ( var entity in Entity.All.ToArray() )
			entity.Delete();
		Lobby = null;
		Menu = null;
	}

	public static void Quit() => global::Global.Render.Window.SdlWindow.Close();

	// ---- Frame -----------------------------------------------------------------------------

	private UiCanvas CurrentCanvas()
	{
		var framebuffer = global::Global.Render.MultisampledFramebuffer;
		return new UiCanvas( (int)framebuffer.Width, (int)framebuffer.Height, Display.UiScale );
	}

	public void Update()
	{
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
		var window = global::Global.Render.Window.Size;
		var input = InjectedInput ?? inputSource.Poll( Context.Canvas.Width / (float)Math.Max( 1, window.X ), Context.Canvas.Height / (float)Math.Max( 1, window.Y ) );
		InjectedInput = null;
		var imguiMouse = ImGuiNET.ImGui.GetIO().WantCaptureMouse;
		if ( imguiMouse )
			input = input with { LeftPressed = false, LeftReleased = false, RightPressed = false, Wheel = 0 };

		if ( Level != null && Hud != null )
		{
			Level.UiCapturesMouse = Hud.Update( Context, input );
			Level.SimulationTimeScale = Hud.Status.TimeScale;
			Level.Update();
			return;
		}
		Camera.Update();
		Menu?.Stack.Update( Context, input );
		foreach ( var entity in Entity.All.ToArray() )
			entity.Update();
	}

	public void Render()
	{
		renderer.BeginFrame();
		var framebuffer = global::Global.Render.MultisampledFramebuffer;
		if ( Level != null )
			Level.Render();
		else
		{
			// Lobby sky: the SKYCOLOUR of the selected island behind the 3D scene.
			backgroundBatch.Clear();
			var sky = Menu?.Selected.SkyColour ?? ((byte)40, (byte)90, (byte)160);
			backgroundBatch.AddRectangle( new UiRect( 0, 0, framebuffer.Width, framebuffer.Height ), new RgbaByte( sky.R, sky.G, sky.B, 255 ) );
			renderer.Draw( global::Global.Render.CommandList, backgroundBatch, framebuffer.Width, framebuffer.Height );
			foreach ( var entity in Entity.All.ToArray() )
				entity.Render();
		}
		Context.Batch.Clear();
		if ( Hud != null )
			Hud.Draw( Context );
		else
			Menu?.Stack.Draw( Context );
		renderer.Draw( global::Global.Render.CommandList, Context.Batch, framebuffer.Width, framebuffer.Height );
	}

	public void Dispose()
	{
		Level?.TextOverlay?.Dispose();
		renderer.Dispose();
	}
}
