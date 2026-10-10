using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>What the HUD asks of the game flow (pause menu actions).</summary>
public sealed class HudHost
{
	public Action ExitToLobby { get; init; } = () => { };
	public Action Quit { get; init; } = () => { };
	/// <summary>Creates the options screen; the callback runs when it closes.</summary>
	public Func<UiScreenStack, Action, UiScreen>? CreateOptions { get; init; }
	/// <summary>Creates the load screen.</summary>
	public Func<UiScreenStack, UiScreen>? CreateLoad { get; init; }
	/// <summary>Opens the online screens (Go Online, UITEXT 1); null hides the entry.</summary>
	public Action<UiScreenStack>? GoOnline { get; init; }
}

/// <summary>
/// The in-game HUD in the original style (docs/UI.md): the green main panel (mainpanel.MD2) with the
/// date display, happiness gauge and bank balance at their authored places; the panel buttons
/// (buy/build, information, finance, research, map) with their original tooltips; the build arm
/// (panel.MD2 "pan_buy") with the four category buttons and preview-model icons; the information arm
/// ("pan_info") for the selected original object; the message area (f_tagl/m/r) and the pause menu. Positions of
/// code-placed buttons inside the panel/arms and the speed control are OpenTPW approximations.
/// </summary>
public sealed class ParkHud
{
	// [APPROX:UI-025] message area: 3 messages, 8 s — evidence needed: binary/capture of the message system
	public const float MessageSeconds = 8f;
	private static readonly UiRect ArmRect = new( 330.5f, 1068.5f, 675.8f, 426.1f );
	private static readonly (BuildCategory Category, string Model, UIStrings Title, int Help)[] Categories =
	{
		(BuildCategory.Rides, "b_srides", UIStrings.BuyRide, 521),
		(BuildCategory.Shops, "b_sshop", UIStrings.BuyShop, 522),
		(BuildCategory.Sideshows, "b_sshow", UIStrings.BuySideshow, 523),
		(BuildCategory.Features, "b_sfeature", UIStrings.BuyMiscItems, 524),
	};

	private readonly Level level;
	private readonly UiStringTable strings;
	private readonly HudHost host;
	private readonly UiScreen hud;
	private readonly List<(string Text, float Age)> messages = new();
	private readonly Dictionary<string, PreviewIcon?> icons = new();
	private readonly List<UiElement> buildElements = new();
	private readonly List<UiElement> infoElements = new();
	private string lastLevelMessage = "";
	private BuildItem? pendingItem;
	private OriginalObject? selectedObject;
	private int buildPage;
	private int cursorX = -1;
	/// <summary>Set by a press the path/queue tool used, so its release does not select an object.</summary>
	private bool toolPress;
	private int cursorY = -1;

	public ParkHud( Level level, UiStringTable strings, IHudParkStatus status, IBuildCatalog catalog, HudHost host )
	{
		this.level = level;
		this.strings = strings;
		this.host = host;
		Status = status;
		Catalog = catalog;
		lastLevelMessage = level.LastActionMessage;
		level.Objects.ObjectPlaced += OnObjectPlaced;
		hud = new UiScreen( "hud" ) { Modal = false };
		Stack.Push( hud );
		Build();
	}

	public UiScreenStack Stack { get; } = new();
	public IHudParkStatus Status { get; }
	public IBuildCatalog Catalog { get; }
	public UiScreen Screen => hud;
	public bool BuildArmOpen { get; private set; }
	public bool InfoArmOpen { get; private set; }
	public BuildCategory Category { get; private set; } = BuildCategory.Rides;
	public int BuildPage => buildPage;
	public int BuildPageCount => Math.Max( 1, (Catalog.GetItems( Category ).Count + 2) / 3 );
	/// <summary>The cat_ui event of a click in the park view.</summary>
	public const uint ParkViewClickEvent = 0x1F;

	public OriginalObject? SelectedObject => selectedObject;
	public IReadOnlyList<BuildItem> VisibleBuildItems => Catalog.GetItems( Category ).Skip( buildPage * 3 ).Take( 3 ).ToArray();
	public bool Paused => Stack.Screens.Count > 1;
	public IReadOnlyList<string> Messages => messages.Select( message => message.Text ).ToArray();

	// [DATA:UITEXT.str:448,449] currency prefix; [APPROX:UI-021] ","-grouped digits — evidence needed: locale number format of the original
	public string MoneyText => !Status.HasEconomy ? "" : string.Format( System.Globalization.CultureInfo.InvariantCulture, "{0}{1:#,0}", Status.Money < 0 ? strings[UIStrings.NegativeDollar] : strings[UIStrings.Dollar], Math.Abs( Status.Money ) ).Replace( "  ", " " );
	public string DateText => !Status.HasEconomy ? "" : string.Format( strings.Extra( OpenTpwText.DateFormat ), Status.Date.Year, Status.Date.Month, Status.Date.Day );

	public void PostMessage( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return;
		messages.Add( (text, 0) );
		if ( messages.Count > 3 )
			messages.RemoveAt( 0 );
	}

	private UiButton PanelButton( string id, string model, float x, float y, int help, Action? clicked, float size = 118.4f ) => hud.Add( new UiButton
	{
		Id = id,
		Model = model,
		Help = strings.Help( help ),
		Clicked = clicked,
		Enabled = clicked != null,
		Bounds = UiRect.FromCenter( new NVector2( x, y ), size, size ),
		Anchor = UiAnchor.BottomLeft
	} );

	private void Build()
	{
		// Build and information arms slide out from behind the main panel (drawn first).
		var buildArm = hud.Add( new UiModelImage { Id = "buildArm", Model = "panel", Frame = () => 2, Bounds = ArmRect, Anchor = UiAnchor.BottomLeft, Visible = false } );
		var infoArm = hud.Add( new UiModelImage { Id = "infoArm", Model = "panel", Frame = () => 1, Bounds = ArmRect, Anchor = UiAnchor.BottomLeft, Visible = false } );
		buildElements.Add( buildArm );
		infoElements.Add( infoArm );
		// [DATA:ui.wad:mainpanel,gauge,date,panel MD2 roots] authored HUD rectangles
		hud.Add( new UiModelImage { Id = "mainPanel", Model = "mainpanel", Bounds = new UiRect( 37.1f, 984.2f, 401.7f, 523.1f ), Anchor = UiAnchor.BottomLeft } );
		hud.Add( new UiModelImage { Id = "gauge", Model = "gauge", Bounds = new UiRect( 66.6f, 1080.4f, 96.6f, 264.2f ), Anchor = UiAnchor.BottomLeft, Help = strings.Help( 477 ) } );
		hud.Add( new UiModelImage { Id = "dateDisplay", Model = "date", Bounds = new UiRect( 152.6f, 1043.6f, 251.7f, 77.5f ), Anchor = UiAnchor.BottomLeft, Help = strings.Help( 478 ) } );
		// [APPROX:UI-021] date/money text positions and fonts — evidence needed: capture of the original HUD
		hud.Add( new UiLabel { Id = "date", Text = () => DateText, Font = fonts => fonts.Small, Align = UiAlign.Center, Bounds = new UiRect( 170, 1050, 220, 64 ), Anchor = UiAnchor.BottomLeft } );
		hud.Add( new UiLabel { Id = "money", Text = () => MoneyText, Font = fonts => fonts.Cash, Color = UiColors.Value, Align = UiAlign.Center, Bounds = new UiRect( 165, 1125, 240, 60 ), Anchor = UiAnchor.BottomLeft, Help = strings.Help( 465 ) } );

		// [APPROX:UI-020] panel button positions (the models share one authored centre) — evidence needed: capture of the original HUD
		PanelButton( "buy", "b_buy", 235, 1240, 469, level.IsReadOnlyVisit ? null : ToggleBuildArm ).Selected = () => BuildArmOpen;
		PanelButton( "info", "b_info", 355, 1240, 470, null );
		PanelButton( "finance", "b_money", 235, 1350, 471, null );
		PanelButton( "research", "b_resrch", 355, 1350, 472, null );
		PanelButton( "map", "b_map", 235, 1455, 473, null );

		// [APPROX:UI-022] speed buttons and multipliers — evidence needed: binary game speed options
		// Speed control (OpenTPW addition; the original has pause only).
		var speeds = new[] { (GameSpeed.Paused, "||"), (GameSpeed.Normal, ">"), (GameSpeed.Fast, ">>"), (GameSpeed.Fastest, ">>>") };
		for ( var index = 0; index < speeds.Length; index++ )
		{
			var (speed, label) = speeds[index];
			hud.Add( new UiButton
			{
				Id = $"speed{speed}",
				Text = () => label,
				Font = fonts => fonts.Label,
				Clicked = () => Status.Speed = speed,
				Selected = () => Status.Speed == speed,
				Bounds = new UiRect( 1540 + index * 122, 1430, 116, 84 ),
				Anchor = UiAnchor.BottomRight
			} );
		}

		// [APPROX:UI-024] layout inside the build arm — evidence needed: capture of the original build arm
		// Build arm contents.
		for ( var index = 0; index < Categories.Length; index++ )
		{
			var entry = Categories[index];
			buildElements.Add( hud.Add( new UiButton
			{
				Id = $"category{entry.Category}",
				Model = entry.Model,
				Help = strings.Help( entry.Help ),
				Clicked = () => SelectCategory( entry.Category ),
				Selected = () => Category == entry.Category,
				Bounds = UiRect.FromCenter( new NVector2( 530 + index * 112, 1130 ), 102.4f, 102.4f ),
				Anchor = UiAnchor.BottomLeft,
				Visible = false
			} ) );
		}
		buildElements.Add( hud.Add( new UiLabel
		{
			Id = "categoryTitle",
			Text = () => strings[Categories.First( entry => entry.Category == Category ).Title],
			Font = fonts => fonts.Label,
			Color = UiColors.Title,
			Bounds = new UiRect( 535, 1185, 390, 50 ),
			Anchor = UiAnchor.BottomLeft,
			Visible = false
		} ) );
		// [APPROX:UI-024] Three-slot pages and arrow positions are OpenTPW layout, not original menu evidence.
		foreach ( var direction in new[] { -1, 1 } )
		{
			var step = direction;
			buildElements.Add( hud.Add( new UiButton
			{
				Id = step < 0 ? "previousBuildPage" : "nextBuildPage",
				Model = step < 0 ? "b_sleft" : "b_sright",
				Clicked = () => ChangeBuildPage( step ),
				Bounds = new UiRect( step < 0 ? 475 : 930, 1185, 50, 50 ),
				Anchor = UiAnchor.BottomLeft,
				Visible = false
			} ) );
		}
		for ( var slot = 0; slot < 3; slot++ )
		{
			var index = slot;
			buildElements.Add( hud.Add( new BuildSlot( this, index )
			{
				Id = $"item{index}",
				Bounds = new UiRect( 480 + index * 170, 1240, 160, 248 ),
				Anchor = UiAnchor.BottomLeft,
				Visible = false
			} ) );
		}
		buildElements.Add( hud.Add( new UiButton
		{
			Id = "retractBuild",
			Model = "b_retract",
			Help = strings.Help( 481 ),
			Clicked = () => SetBuildArm( false ),
			Bounds = UiRect.FromCenter( new NVector2( 388.8f, 1426.2f ), 80, 80 ),
			Anchor = UiAnchor.BottomLeft,
			Visible = false
		} ) );

		// [APPROX:UI-024] layout inside the info arm; [APPROX:UI-029] b_door down = closed, b_erase = delete — evidence needed: capture of the ride panel
		// Information arm contents.
		infoElements.Add( hud.Add( new UiLabel
		{
			Id = "infoName",
			Text = () => SelectedInfo == null ? "" : SelectedInfo.DisplayName ?? strings.Object( SelectedInfo.ObjectNameIndex ),
			Font = fonts => fonts.Heading,
			Color = UiColors.Title,
			Bounds = new UiRect( 470, 1090, 520, 70 ),
			Anchor = UiAnchor.BottomLeft,
			Visible = false
		} ) );
		for ( var row = 0; row < 4; row++ )
		{
			var index = row;
			infoElements.Add( hud.Add( new UiLabel
			{
				Id = $"statLabel{index}",
				Text = () => SelectedInfo != null && index < SelectedInfo.Stats.Count ? strings[SelectedInfo.Stats[index].Label] : "",
				Font = fonts => fonts.Small,
				Bounds = new UiRect( 470, 1170 + index * 50, 290, 48 ),
				Anchor = UiAnchor.BottomLeft,
				Visible = false
			} ) );
			infoElements.Add( hud.Add( new UiLabel
			{
				Id = $"statValue{index}",
				Text = () => SelectedInfo != null && index < SelectedInfo.Stats.Count ? SelectedInfo.Stats[index].Value ?? strings.Extra( OpenTpwText.NotSimulated ) : "",
				Font = fonts => fonts.Small,
				Color = UiColors.Value,
				Bounds = new UiRect( 765, 1170 + index * 50, 225, 48 ),
				Anchor = UiAnchor.BottomLeft,
				Visible = false
			} ) );
		}
		infoElements.Add( hud.Add( new UiButton
		{
			Id = "openRide",
			Model = "b_door",
			Help = strings.Help( 12 ),
			Clicked = ToggleRideOpen,
			// b_door state frames: open door normally, closed door in the "down" frames.
			Selected = () => selectedObject?.IsOpen == false,
			Bounds = UiRect.FromCenter( new NVector2( 560, 1425 ), 120, 120 ),
			Anchor = UiAnchor.BottomLeft,
			Visible = false
		} ) );
		infoElements.Add( hud.Add( new UiButton
		{
			Id = "deleteRide",
			Model = "b_erase",
			Help = strings.Help( 15 ),
			Clicked = DeleteRide,
			Bounds = UiRect.FromCenter( new NVector2( 700, 1425 ), 120, 120 ),
			Anchor = UiAnchor.BottomLeft,
			Visible = false
		} ) );
		infoElements.Add( hud.Add( new UiButton
		{
			Id = "retractInfo",
			Model = "b_retract",
			Help = strings.Help( 481 ),
			Clicked = () => SetInfoArm( false ),
			Bounds = UiRect.FromCenter( new NVector2( 388.8f, 1426.2f ), 80, 80 ),
			Anchor = UiAnchor.BottomLeft,
			Visible = false
		} ) );

		hud.DrawOverlay = DrawMessages;
		hud.Back = () =>
		{
			if ( level.CellTool.IsActive )
				level.CellTool.Cancel();
			else
				OpenPauseMenu();
		};
	}


	public ObjectInfo? SelectedInfo => InfoArmOpen && selectedObject is { IsDeleted: false } item
		? new ObjectInfo( item.Entry.ObjectNameIndex ?? -1, item.IsOpen, item.Runtime.IsAttraction, new[]
		{
			// [APPROX:UI-028] authored excitement as a percentage; unknown simulation statistics stay unavailable.
			new ObjectStat( UIStrings.Excitement, item.Entry.Settings.Has( "UsageInfo.ExcitementLevel" ) ? $"{item.Entry.Settings.GetInt( "UsageInfo.ExcitementLevel" )}%" : null ),
			new ObjectStat( UIStrings.Reliability, null ),
			new ObjectStat( UIStrings.StateOfRepair, null ),
			new ObjectStat( UIStrings.RemainingLife, null ),
		}, EntryName( item.Entry ) ) : null;

	public void SelectCategory( BuildCategory category )
	{
		Category = category;
		buildPage = 0;
	}

	public void ChangeBuildPage( int direction ) => buildPage = Math.Clamp( buildPage + direction, 0, BuildPageCount - 1 );
	public string ItemName( BuildItem item ) => item.Entry == null ? strings.Object( item.ObjectNameIndex ) : EntryName( item.Entry );

	private string EntryName( ObjectCatalogEntry entry ) => entry.ObjectNameIndex is int index
		? string.Join( " ", Enumerable.Range( index, Math.Max( 1, entry.ObjectNameLength ) ).Select( strings.Object ).Where( text => text.Length > 0 ) )
		: entry.DisplayName;


	public void ToggleBuildArm() => SetBuildArm( !BuildArmOpen );

	public void SetBuildArm( bool open )
	{
		BuildArmOpen = open;
		if ( open )
			level.CellTool.Cancel();
		if ( open )
			InfoArmOpen = false;
		if ( !open && pendingItem != null )
		{
			pendingItem = null;
			level.IsPlacing = false;
			level.BuildEntry = null;
		}
	}

	public void SetInfoArm( bool open )
	{
		InfoArmOpen = open && selectedObject is { IsDeleted: false };
		if ( InfoArmOpen )
			SetBuildArm( false );
	}

	/// <summary>Starts placing a catalog item (the click on the ground is handled by <see cref="Level"/>).</summary>
	public void BeginPlacing( BuildItem item )
	{
		if ( RejectReadOnlyAction() )
			return;
		if ( !Status.IsAvailable( item ) )
		{
			PostMessage( strings.Extra( OpenTpwText.NotAvailable ) );
			return;
		}
		if ( Status.HasEconomy && (Status.PriceOf( item ) ?? item.Cost) > Status.Money )
		{
			PostMessage( strings.Help( 152 ) );
			return;
		}
		if ( item.Entry is not { } entry )
			return;
		level.CellTool.Cancel();
		pendingItem = item;
		level.BuildEntry = entry;
		level.IsRemovingObjects = false;
		level.IsPlacing = false;
		PostMessage( strings.Help( 440 ) );
	}

	// [APPROX:UI-031] one placement per menu selection, then the queue tool for Info.HasQueue rides; Level owns purchase/sale — evidence needed: original build-tool continuation
	private void OnObjectPlaced( OriginalObject item )
	{
		if ( pendingItem?.Entry != item.Entry )
			return;
		// Level.PlaceObject owns the purchase and guest link; this callback only finishes the HUD tool.
		PostMessage( string.Format( strings.Extra( OpenTpwText.Built ), ItemName( pendingItem ) ) );
		SetBuildArm( false );
		// [BIN:STP-PPC:0x1007497C ride placement] a placed HasQueue ride (type record +64) enters tool mode 3, the queue tool (PATH-plan §8); park clicks lay its queue until Back
		if ( item.Entry.HasQueue && item.Runtime.IsAttraction && item.Visitors.HasCells && level.EnterQueueTool( item.Visitors ) )
			PostMessage( strings.Extra( OpenTpwText.LayQueue ) );
	}

	private void ToggleRideOpen()
	{
		if ( RejectReadOnlyAction() )
			return;
		if ( selectedObject is not { IsDeleted: false } item || !item.Runtime.IsAttraction )
			return;
		if ( item.IsOpen ) item.Close(); else item.Open();
	}

	private void DeleteRide()
	{
		if ( RejectReadOnlyAction() )
			return;
		if ( selectedObject is { IsDeleted: false } item && !item.Entry.IsFixedItem )
			level.Objects.Remove( item ); // The level's removal handler sells/unlinks exactly once.
		selectedObject = null;
		SetInfoArm( false );
	}

	private bool RejectReadOnlyAction()
	{
		if ( !level.IsReadOnlyVisit )
			return false;
		PostMessage( OnlineStrings.Get( OnlineLabel.ReadOnlyVisit ) );
		return true;
	}

	public void OpenPauseMenu()
	{
		if ( Paused )
			return;
		var menu = new UiScreen( "pause" );
		var window = UiDialogs.CenteredWindow( 1000, 1180 );
		UiDialogs.AddWindow( menu, window, "w_med", () => strings[UIStrings.Paused] );
		var entries = new List<(UIStrings Label, Action Clicked)>
		{
			(UIStrings.ResumeGame, ClosePauseMenu),
			(UIStrings.Save, SavePark),
			(UIStrings.Load, () => { if ( host.CreateLoad != null ) Stack.Push( host.CreateLoad( Stack ) ); }),
			(UIStrings.Options, () => { if ( host.CreateOptions != null ) Stack.Push( host.CreateOptions( Stack, () => { } ) ); }),
			(UIStrings.GoOnline, () => host.GoOnline?.Invoke( Stack )),
			(UIStrings.ExitToLobby, host.ExitToLobby),
			(UIStrings.QuitGame, () => Stack.Push( UiDialogs.Message( "confirmQuit", () => strings[UIStrings.ConfirmQuit],
				(() => strings.Value( UIStrings.Yes ), host.Quit), (() => strings.Value( UIStrings.No ), Stack.Pop) ) )),
		};
		if ( host.GoOnline == null )
			entries.RemoveAll( entry => entry.Label == UIStrings.GoOnline );
		for ( var index = 0; index < entries.Count; index++ )
		{
			var entry = entries[index];
			menu.Add( new UiButton
			{
				Id = entry.Label.ToString(),
				Text = () => strings[entry.Label],
				Clicked = entry.Clicked,
				Enabled = !level.IsReadOnlyVisit || entry.Label != UIStrings.Save,
				Bounds = new UiRect( window.X + 160, window.Y + 190 + index * 140, window.Width - 400, 116 ),
				Anchor = UiAnchor.Center
			} );
		}
		menu.Back = ClosePauseMenu;
		menu.Focus( menu.FocusableElements.First() );
		Stack.Push( menu );
	}

	public void ClosePauseMenu()
	{
		while ( Stack.Screens.Count > 1 )
			Stack.Pop();
	}

	private void SavePark()
	{
		if ( RejectReadOnlyAction() )
			return;
		if ( level.OriginalPark != null )
		{
			PostMessage( strings.Extra( OpenTpwText.OriginalParkReadOnly ) );
			ClosePauseMenu();
			return;
		}
		try
		{
			level.SaveSandbox();
			PostMessage( strings.Extra( OpenTpwText.ParkSaved ) );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidOperationException )
		{
			PostMessage( string.Format( strings.Extra( OpenTpwText.SaveFailed ), exception.Message ) );
		}
		ClosePauseMenu();
	}

	/// <summary>
	/// One frame: HUD input first (so clicks on the HUD never reach the park), then park selection,
	/// purchase bookkeeping and messages. Returns true when the pointer is over the UI.
	/// </summary>
	public bool Update( UiContext context, UiInput input )
	{
		foreach ( var element in buildElements )
			element.Visible = BuildArmOpen;
		foreach ( var element in infoElements )
			element.Visible = InfoArmOpen && selectedObject is { IsDeleted: false };
		if ( selectedObject is not { IsDeleted: false } && InfoArmOpen )
			InfoArmOpen = false;

		if ( hud.Find( "openRide" ) is UiButton openButton )
			openButton.Enabled = !level.IsReadOnlyVisit && SelectedInfo?.CanOpen == true;
		if ( hud.Find( "deleteRide" ) is UiButton deleteButton )
			deleteButton.Enabled = !level.IsReadOnlyVisit && selectedObject is { IsDeleted: false } item && !item.Entry.IsFixedItem;

		var paused = Paused;
		var overUi = Stack.Covers( context.Canvas, input.Mouse ) || Stack.Screens.Count > 1;
		var consumed = Stack.Update( context, input );
		if ( !paused && input.Has( UiKeys.Pause ) )
			Status.Speed = Status.Speed == GameSpeed.Paused ? GameSpeed.Normal : GameSpeed.Paused;

		if ( level.LastActionMessage != lastLevelMessage )
		{
			lastLevelMessage = level.LastActionMessage;
			if ( lastLevelMessage.StartsWith( "Cannot build", StringComparison.Ordinal ) )
				PostMessage( strings.Extra( OpenTpwText.CannotBuildHere ) );
		}

		// [BIN:STP-PPC:0x10137FD0 park view input] a button message in the park view plays cat_ui event 0x1F (0xBD when modifier flag 0x10 is set)
		// [APPROX:AUDIO-004] the 0xBD modifier (flag 0x10 from 0x1017F618) is not mapped to a key, so the view always plays 0x1F — evidence needed: the input flag behind 0x1017F618
		if ( !overUi && !consumed && input.LeftPressed )
			GameAudio.PlayUi( ParkViewClickEvent );

		var inPark = !overUi && !consumed && level.TryGetGridCell( input.Mouse, new NVector2( context.Canvas.Width, context.Canvas.Height ), out cursorX, out cursorY );
		if ( !inPark )
			cursorX = cursorY = -1;
		var toolClick = UpdateCellTool( context, input, inPark, paused );
		// [APPROX:UI-027] Select by occupied grid cell; original cursor picking is not verified.
		if ( inPark && level.BuildEntry == null && !level.IsPlacing && !level.CellTool.IsActive && input.LeftReleased && !toolPress )
			SelectObject( level.Objects.FindAt( cursorX, cursorY ) );
		if ( input.LeftReleased )
			toolPress = false;

		for ( var index = messages.Count - 1; index >= 0; index-- )
		{
			messages[index] = (messages[index].Text, messages[index].Age + context.Delta);
			if ( messages[index].Age > MessageSeconds )
				messages.RemoveAt( index );
		}
		Status.Update( context.Delta );
		return overUi || consumed || toolClick;
	}

	/// <summary>
	/// The path/queue tool's HUD side: park-view help (441–445), the ghost line under the cursor, Backspace undo
	/// and right-click cancel. Clicks inside the tool are committed by <see cref="Level"/> (the park click).
	/// </summary>
	private bool UpdateCellTool( UiContext context, UiInput input, bool inPark, bool paused )
	{
		var tool = level.CellTool;
		if ( paused || level.BuildEntry != null || level.IsPlacing )
			return false;
		if ( inPark && tool.HoverHelpId( level.Paths, cursorX, cursorY ) is int help )
			context.HoverHelp = strings.Help( help );
		if ( !tool.IsActive )
		{
			// A left click on an empty owned cell or on a path cell starts the path tool there (help 441/442).
			if ( !inPark || !input.LeftPressed || level.IsReadOnlyVisit || level.Objects.FindAt( cursorX, cursorY ) != null
				|| tool.HoverHelpId( level.Paths, cursorX, cursorY ) is not (441 or 442) || !level.EnterPathTool( cursorX, cursorY ) )
				return false;
			PostMessage( strings.Help( 443 ) );
			tool.Hover( (cursorX, cursorY) );
			return toolPress = true;
		}
		if ( input.Backspaces > 0 )
			tool.Undo();
		if ( input.RightPressed && inPark )
		{
			tool.Cancel();
			return true;
		}
		if ( !inPark )
			return false;
		tool.Hover( (cursorX, cursorY) );
		if ( !input.LeftPressed )
			return false;
		// The commit click; Level skips it because the HUD reports the pointer as captured this frame.
		var result = tool.Click( (cursorX, cursorY) );
		if ( result is { Completed: false } )
			PostMessage( strings.Extra( result.StoppedBy == CellBuildResult.NotEnoughMoney ? OpenTpwText.NotEnoughMoney : OpenTpwText.CannotBuildHere ) );
		return toolPress = true;
	}

	/// <summary>Ghost markers for the tool's preview line: green cells it would build, grey existing cells, red where it stops; then the line's cost.</summary>
	// [APPROX:PATH-001] ghosts are flat markers at the cell centres with the line's cost beside the cursor (the original draws ghost path pieces, LayLine mode | 0x100) — evidence needed: captures of the original path tool
	private void DrawCellToolGhost( UiContext context )
	{
		if ( level.CellTool.Ghost is not { } ghost || !level.CellTool.IsActive )
			return;
		var viewport = new NVector2( context.Canvas.Width, context.Canvas.Height );
		var size = 10 * context.Canvas.TextScale;
		var built = ghost.Built.ToHashSet();
		var existing = ghost.Existing.ToHashSet();
		foreach ( var cell in ghost.Line )
		{
			if ( !level.TryProjectCell( cell.X, cell.Y, viewport, out var centre ) )
				continue;
			var colour = cell == ghost.StoppedAt ? new RgbaByte( 230, 40, 40, 200 )
				: built.Contains( cell ) ? new RgbaByte( 80, 220, 80, 170 )
				: existing.Contains( cell ) ? new RgbaByte( 200, 200, 200, 140 ) : new RgbaByte( 120, 120, 120, 90 );
			context.Batch.AddRectangle( new UiRect( centre.X - size / 2, centre.Y - size / 2, size, size ), colour );
		}
		if ( Status.HasEconomy && ghost.Charged > 0 && level.TryProjectCell( ghost.SnappedEnd.X, ghost.SnappedEnd.Y, viewport, out var end ) )
		{
			var text = string.Format( System.Globalization.CultureInfo.InvariantCulture, "{0}{1:#,0}", strings[UIStrings.Dollar], ghost.Charged ).Replace( "  ", " " );
			context.DrawText( context.Fonts.Small, text, new UiRect( end.X + size, end.Y - size * 2, 300 * context.Canvas.TextScale, size * 4 ), ghost.Completed ? UiColors.Value : UiColors.Highlight, UiAlign.Left );
		}
	}

	public void SelectObject( OriginalObject? item )
	{
		selectedObject = item;
		SetInfoArm( item != null );
	}

	private void DrawMessages( UiContext context )
	{
		if ( messages.Count == 0 )
			return;
		var canvas = context.Canvas;
		context.DrawModel( "f_tagl", 0, canvas.Map( new UiRect( 457.6f, 1100.7f, 165, 341.5f ), UiAnchor.BottomLeft ) );
		context.DrawModel( "f_tagm", 0, canvas.Map( new UiRect( 622.6f, 1100.7f, 212.1f, 341.5f ), UiAnchor.BottomLeft ) );
		context.DrawModel( "f_tagr", 0, canvas.Map( new UiRect( 834.7f, 1100.7f, 237.8f, 341.5f ), UiAnchor.BottomLeft ) );
		var area = canvas.Map( new UiRect( 495, 1135, 540, 280 ), UiAnchor.BottomLeft );
		context.DrawText( context.Fonts.Small, string.Join( "\n", messages.Select( message => message.Text ) ), area, UiColors.Text, UiAlign.Left, wrap: true );
	}

	public void Draw( UiContext context )
	{
		// The message area replaces the build/info arm when both would overlap.
		if ( BuildArmOpen || InfoArmOpen )
			hud.DrawOverlay = null;
		else
			hud.DrawOverlay = DrawMessages;
		DrawCellToolGhost( context );
		Stack.Draw( context );
	}

	internal PreviewIcon? GetIcon( BuildItem item )
	{
		if ( icons.TryGetValue( item.Id, out var icon ) )
			return icon;
		try
		{
			icon = item.PreviewModel == null ? null : new PreviewIcon( new ModelFile( item.PreviewModel ),
				name => item.TextureDirectories.Select( directory => UiImages.Resolve( name, directory ) ).FirstOrDefault( path => path != null ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException )
		{
			Log?.Warning( $"Preview icon for {item.Id} unavailable: {exception.Message}" );
			icon = null;
		}
		icons[item.Id] = icon;
		return icon;
	}

	/// <summary>One build-menu slot: preview icon, name and price; click to start placing.</summary>
	private sealed class BuildSlot : UiElement
	{
		private readonly ParkHud owner;
		private readonly int index;

		public BuildSlot( ParkHud owner, int index )
		{
			this.owner = owner;
			this.index = index;
		}

		private BuildItem? Item
		{
			get
			{
				var items = owner.VisibleBuildItems;
				return index < items.Count ? items[index] : null;
			}
		}

		public override bool Focusable => Visible && Item != null;

		public override void Activate()
		{
			if ( Item is { } item )
				owner.BeginPlacing( item );
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			if ( Item is not { } item )
				return;
			Help = owner.strings.Help( 151 );
			var rect = ScreenRect( context.Canvas );
			var name = owner.ItemName( item );
			var price = string.Format( System.Globalization.CultureInfo.InvariantCulture, "{0}{1:#,0}", owner.strings[UIStrings.Dollar], owner.Status.PriceOf( item ) ?? item.Cost ).Replace( "  ", " " );
			var nameHeight = context.Measure( context.Fonts.Small, name, (int)rect.Width ).Height;
			var priceHeight = context.Measure( context.Fonts.Small, price ).Height;
			// [APPROX:UI-024] Keep each translated name and price inside its slot; previews use the remaining height.
			var iconSize = Math.Max( 0, Math.Min( rect.Width * 0.7f, rect.Height - nameHeight - priceHeight - 6 ) );
			var iconRect = new UiRect( rect.X + (rect.Width - iconSize) / 2, rect.Y, iconSize, iconSize );
			var available = owner.Status.IsAvailable( item );
			context.Batch.AddRectangle( iconRect, !available ? new RgbaByte( 60, 60, 60, 160 ) : focused || owner.pendingItem == item ? new RgbaByte( 255, 230, 70, 90 ) : new RgbaByte( 0, 0, 40, 90 ) );
			// [APPROX:UI-026] icon turn speed 0.8 rad/s — evidence needed: capture of the original build menu
			if ( iconSize > 16 )
				owner.GetIcon( item )?.Draw( context.Batch, iconRect.Inflate( -iconSize * 0.06f ), context.Time * 0.8f );
			var textTop = iconRect.Bottom + 2;
			// [APPROX:UI-032] Wrap translated catalogue names within their slot at the integer text scale.
			context.DrawText( context.Fonts.Small, name, new UiRect( rect.X, textTop, rect.Width, nameHeight ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center, wrap: true );
			context.DrawText( context.Fonts.Small, price, new UiRect( rect.X, textTop + nameHeight + 2, rect.Width, priceHeight ), UiColors.Value, UiAlign.Center );
		}
	}
}
