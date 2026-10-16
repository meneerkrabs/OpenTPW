using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>What the HUD asks of the game flow (pause menu actions).</summary>
public sealed class HudHost
{
	public Action ExitToLobby { get; init; } = () => { };
	public Action Quit { get; init; } = () => { };
	/// <summary>Saves the writable park through its owning game flow.</summary>
	public Action? SavePark { get; init; }
	/// <summary>Creates the options screen; the callback runs when it closes.</summary>
	public Func<UiScreenStack, Action, UiScreen>? CreateOptions { get; init; }
	/// <summary>Creates the load screen.</summary>
	public Func<UiScreenStack, UiScreen>? CreateLoad { get; init; }
	/// <summary>Opens the online screens (Go Online, UITEXT 1); null hides the entry.</summary>
	public Action<UiScreenStack>? GoOnline { get; init; }
	/// <summary>Restarts the park as it was started (the pause menu's Restart Park, after its confirmation); null disables the entry.</summary>
	public Action? RestartPark { get; init; }
	/// <summary>Opens the online Publish Park flow (the pause menu's Publish Park); null disables the entry.</summary>
	public Action<UiScreenStack>? PublishPark { get; init; }
	/// <summary>Opens the existing postcard composer; it does not send a card.</summary>
	public Action<UiScreenStack>? OpenPostcard { get; init; }
	/// <summary>The list sorts the windows keep for the session; the game flow shares one instance between its parks.</summary>
	public ListSortMemory ListSorts { get; init; } = new();
	/// <summary>Called after a HUD key changed a player option (Ctrl+H flips Popup help) so the host can persist the options.</summary>
	public Action? OptionsChanged { get; init; }
	/// <summary>The open/close button or its shortcut was pressed (true: open, false: close), also when the park's state does not change; the advisor raises game event 3 or 4 on it.</summary>
	public Action<bool>? ParkDoorPressed { get; init; }
	/// <summary>A screen other than the HUD opened (by <see cref="UI.Original.UiScreen.Name"/>); the advisor posts the window's help message.</summary>
	public Action<string>? WindowOpened { get; init; }
	/// <summary>A screen other than the HUD closed; the advisor withdraws the window's help message.</summary>
	public Action<string>? WindowClosed { get; init; }
	/// <summary>F1 was pressed in a HUD window that passes keys to the original's default window procedure; the advisor repeats the last tutorial advice.</summary>
	public Action? HelpKeyPressed { get; init; }
}

/// <summary>
/// The in-game HUD in the original style (docs/UI.md): the green main panel (mainpanel.MD2) with the
/// date display, happiness gauge and bank balance at their authored places; the panel buttons
/// (buy/build, information, finance, research, map) with their original tooltips; the original buy, staff and map windows
/// (Mac tables 0x4cd94, 0x4a1e0 and 0x5265e); the information arm
/// ("pan_info") for the selected original object; the cash extras (dollar icon, change text, golden keys and tickets);
/// the message area (f_tagl/m/r) and the pause menu. Positions of code-placed buttons inside the info arm and the
/// speed control are OpenTPW approximations.
/// </summary>
public sealed partial class ParkHud
{
	// [EXT:status-line] OpenTPW's own feedback lines (a refused action, a hint for the tool that just started) are not the original's messages: they show as up to three lines for eight seconds above the popup help box; the original's messages go to the message tube (ParkHud.MessageTube.cs, UI-025)
	public const float MessageSeconds = 8f;
	private readonly Level level;
	private readonly UiStringTable strings;
	private readonly HudHost host;
	private readonly UiScreen hud;
	private readonly List<(string Text, float Age)> messages = new();
	private readonly Dictionary<string, PreviewIcon?> icons = new();
	private readonly List<UiElement> infoElements = new();
	private string lastLevelMessage = "";
	private BuildItem? pendingItem;
	private UiLabel? moneyLabel;
	private ListSortMemory? fallbackSorts;
	/// <summary>The session's remembered list sorts (UI-084); a HUD built without a host keeps its own.</summary>
	private ListSortMemory Sorts => host?.ListSorts ?? (fallbackSorts ??= new());
	private OriginalObject? selectedObject;
	private CashChangeTracker cashTracker;
	private object? cashSource;
	private readonly List<UiElement> economyElements = new();
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
		buy = new BuyListModel( catalog, ItemName, BuyPrice, Sorts, LandRows );
		cashTracker = new CashChangeTracker( status.Money );
		cashSource = status.EconomySource;
		lastLevelMessage = level.LastActionMessage;
		level.Objects.ObjectPlaced += OnObjectPlaced;
		// Harnesses that build a Level without its constructor have no tool object.
		if ( (CellBuildTool?)level.CellTool is { } cellTool )
			cellTool.Ended += OnCellToolEnded;
		level.BuildToolsEnded += () => { pendingItem = null; queueRearmItem = null; };
		hud = new UiScreen( "hud" ) { Modal = false };
		Stack.Push( hud );
		Build();
		HookStaffStrikeMessages();
		HookChallengeMessages();
	}

	public UiScreenStack Stack { get; } = new();
	public IHudParkStatus Status { get; }
	public IBuildCatalog Catalog { get; }
	public UiScreen Screen => hud;
	public bool InfoArmOpen { get; private set; }
	/// <summary>The cat_ui event of a click in the park view.</summary>
	public const uint ParkViewClickEvent = 0x1F;

	public OriginalObject? SelectedObject => selectedObject;
	/// <summary>True while a menu is on top of the HUD; the buy, staff, attractions and visitors windows do not pause.</summary>
	public bool Paused => Stack.Screens.Any( screen => screen != hud && screen != rideScreen && screen != buyScreen && screen != staffScreen
		&& screen != attractionScreen && screen != visitorScreen );
	public IReadOnlyList<string> Messages => messages.Select( message => message.Text ).ToArray();
	/// <summary>The park simulation's time scale for the screens now open (see <see cref="TimeScaleFor"/>).</summary>
	public float SimulationTimeScale => TimeScaleFor( Status.TimeScale, Stack.Screens.Select( screen => screen.Name ) );

	/// <summary>
	/// The park simulation's time scale: the speed setting's <paramref name="speedScale"/>, or 0 while the pause menu is
	/// open (also under a screen opened from it, such as Load or Options) or a texture-pack download holds the park.
	/// The speed setting itself is left as it is, so the park resumes at the same speed.
	/// </summary>
	// [BIN:STP-PPC:0x10198514 pause menu pauses the game] Escape in the park (0x10157968: key 27, no modifiers) calls
	// 0x10198514 with 0, which builds the pause menu 0x10197D90 and then, while the game-state object's pause gate +0x3C is 1,
	// calls the game pause 0x10110518 (0x10198580) and clears the gate; the menu's close routine 0x101985C0 sets the gate
	// and calls the resume 0x10110598. The pause freezes the scaled clock (0x1010E888 → 0x10117C54) that drives the
	// scheduler's 31 ms substeps and park turns, together with the advisor's unscaled clock (0x10117AE8).
	// [BIN:TPW-EXE:0x4092a0 game pause] the game pause (object 0x786b68, +0x1c = 1) is called by the pause menu 0x48c830 (0x48c87e, from Esc), by the full-screen map window 0x5f0b40 (0x5f0b7f, from Space) and by the postcard 0x4a9380 (0x4a93d7, Ctrl+P), by the generic message box 0x47f020 (0x47f26c), by the main window procedure 0x46b600 when the application is deactivated (0x46b74c) and by the three screens the pause menu's handler 0x48b6a0 opens (0x49efba, 0x49f29f, 0x4a3a6c); the park pauses here for the pause menu with the screens opened from it and for the map window
	// [APPROX:UI-096] the pause menu's own screens keep the pause (its cases 1/22 → 0x49efb0, 2 → 0x49f280, 5 → 0x4a3a30 pause at 0x49efba, 0x49f29f, 0x4a3a6c; jump table 0x48bbc8 of 0x48b6a0), but the postcard (Ctrl+P), every generic message box 0x47f020 (table 0x74f920, b_okay/b_exit buttons, 27 callers, pause at 0x47f26c) and the application's deactivation (WM_ACTIVATEAPP in 0x46b600, 0x46b74c) pause in the original and not here — evidence needed: which OpenTPW dialogs are 0x47f020's message boxes
	// [EXT:texture-pack] A first-run download may take a long time; its modal keeps the park turn paused.
	public static float TimeScaleFor( float speedScale, IEnumerable<string> openScreens ) =>
		openScreens.Any( name => name is PauseMenuScreen or MapScreenName or "texturePackDownload" ) ? 0 : speedScale;

	/// <summary>The name of the map window's screen (it pauses the park while open).</summary>
	public const string MapScreenName = "map";

	/// <summary>The name of the Escape pause menu's screen.</summary>
	public const string PauseMenuScreen = "pause";

	/// <summary>
	/// The cash text box (control 47) in table units: it starts at the table's x 258, is as wide as the nine digits "999999999" in the cash font, as high as
	/// the text plus 16, and is centred vertically on the i_dollar icon (control 50, (37,58)-(242,262), centre y 160).
	/// </summary>
	// [BIN:TPW-EXE:0x4a2082 HUD builder] 0x4929f0 measures "999999999" (0x752f10) in font slot 1; x1 = x0 + width*2048/screenW (word [0xfaa5c4]), H = textHeight*1536/screenH ([0xfaa5c0]) + 16 (0x4a2138), y0 = dollarTop + (dollarBottom - dollarTop)/2 - H/2 (integer), y1 = y0 + H; 0x65cc04 applies the rectangle. The measured-extent fields are medium confidence
	internal static UiRect CashTextBounds( int digitsWidthUnits, int textHeightUnits )
	{
		const int left = 258, dollarTop = 58, dollarBottom = 262;
		var height = textHeightUnits + 16;
		var top = dollarTop + (dollarBottom - dollarTop) / 2 - height / 2;
		return new UiRect( left, top, digitsWidthUnits, height );
	}

	private void FitCashText( UiContext context )
	{
		if ( moneyLabel == null )
			return;
		var (width, height) = context.Measure( context.Fonts.Balance, "999999999" );
		var scale = Math.Max( 0.0001f, context.Canvas.Scale );
		moneyLabel.Bounds = CashTextBounds( (int)(width / scale), (int)(height / scale) );
	}

	// [BIN:TPW-EXE:0x48ff70 HUD cash text] control 47 (refresh 0x4a0ab0) shows the bare |balance| through swprintf "%d": no digit grouping and no
	// currency prefix; the sign is carried by control 50 (i_dollar), whose state is balance < 0.
	public string MoneyText => !Status.HasEconomy ? "" : Math.Abs( (decimal)Status.Money ).ToString( System.Globalization.CultureInfo.InvariantCulture );
	/// <summary>The bank balance with the currency prefix and grouped digits, for panels that spell it out (the info window).</summary>
	private string BalanceWithCurrency => !Status.HasEconomy ? "" : FormatMoney( Status.Money );

	// [DATA:tp.exe table 0x752e30] the cash text colour steps through six colours as the balance falls: index (balance + 999) / 1000 clamped to 0..5
	// (so a balance up to 0 or below is red and 5001 and more is white).
	private static readonly Veldrid.RgbaByte[] CashColors =
	{
		new( 255, 0, 0, 255 ), new( 250, 90, 0, 255 ), new( 255, 175, 0, 255 ), new( 255, 200, 0, 255 ), new( 255, 255, 150, 255 ), new( 255, 255, 255, 255 ),
	};
	public static Veldrid.RgbaByte CashColor( long balance ) => CashColors[(int)Math.Clamp( (Math.Clamp( balance, -999, 6000 ) + 999) / 1000, 0, CashColors.Length - 1 )];
	private string FormatMoney( long amount ) => string.Format( System.Globalization.CultureInfo.InvariantCulture, "{0}{1:#,0}", amount < 0 ? strings[UIStrings.NegativeDollar] : strings[UIStrings.Dollar], Math.Abs( (decimal)amount ) ).Replace( "  ", " " );

	/// <summary>The balance change of the last <see cref="CashChangeTracker.Seconds"/>; it drives the trend arrow beside the balance (control 49), not the yellow cost text.</summary>
	public long CashChange => cashTracker.Change;

	/// <summary>
	/// The cost of the build action under way, shown in yellow under the balance (control 48): the held blueprint's price, the path or queue line's cost so far, or the coaster track
	/// segment under the cursor; null outside the build tools, where the text is hidden.
	/// </summary>
	// [BIN:TPW-EXE:0x4a0cdd HUD cash refresh] control 48 shows the number the active tool keeps in 0x81af4c (read by 0x52fbc0) and is hidden, cost -10000, or reset to the unset marker -999 when no tool is active: only the mode objects whose type (vtable +0x24) is 3, 4 or 8 (8 while 0x79c658 bit 0 is set, 0x447990) show it; type 3 is the tool-mode object 0x6fe9e0 that every build tool runs in; it is no sum of balance changes
	// [DATA:video 2:05, 74:00-76:00] "$ 2500" while the blueprint of a 2500 ride is held, "$ 150" in the path tool, "$ 10000" for the coaster station and "$ 70" to "$ 130" per track segment; absent outside the build tools even when the balance changes
	// [APPROX:UI-047] type 3 is the tool-mode object (vtable 0x6fe9e0) of every build tool; type 8 is the coaster panel's mode object (vtable 0x6feaa0, 0x46cfc0, made by 0x497bc0 when a panel button is pressed), which shows the cost only while bit 0 of 0x79c658 is set (0x447990, set at 0x441a5d), a sub-state OpenTPW does not track, so the coaster tool shows its cost in every sub-mode; no mode-object class returns 4 (the classes 0x6fe980-0x6fead0 return 7, 3, 1, 5, 6, 8, 9; 5 and 6 are the staff place and carry tools, which show nothing) — evidence needed: what bit 0 of 0x79c658 stands for
	public long? BuildActionCost
	{
		get
		{
			if ( !Status.HasEconomy )
				return null;
			if ( level.IsCoasterToolActive )
				return level.CoasterPanel?.DragCost ?? CoasterGhost?.Cost ?? 0;
			if ( level.CellTool.IsActive )
				return level.CellTool.Ghost?.Charged ?? 0;
			if ( pendingItem != null && level.BuildEntry != null )
				return Status.PriceOf( pendingItem ) ?? pendingItem.Cost;
			return null;
		}
	}
	public string CashChangeText => BuildActionCost is { } cost ? FormatMoney( cost ) : "";
	// [DATA:video 40:40-41:30, 73:21] the counters show the count followed by an x (the string below) and each row shows only while its count is above zero: a first session has neither, the ticket row appears above the key row after the first ticket
	public string GoldenTicketsText => Status.HasEconomy && Status.GoldenTickets > 0 ? CounterText( Status.GoldenTickets ) : "";
	public string GoldenKeysText => Status.HasEconomy && Status.GoldenKeys > 0 ? CounterText( Status.GoldenKeys ) : "";
	// [BIN:TPW-EXE:0x004a10a9 HUD counter text] both counters format their count with the UTF-16 "%d  x" at 0x752ef8 (two spaces, a letter x)
	internal static string CounterText( int count ) => string.Create( System.Globalization.CultureInfo.InvariantCulture, $"{count}  x" );
	public string DateText => !Status.HasEconomy ? "" : string.Format( strings.Extra( OpenTpwText.DateFormat ), Status.Date.Year, Status.Date.Month, Status.Date.Day );

	public void PostMessage( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return;
		messages.Add( (text, 0) );
		if ( messages.Count > 3 )
			messages.RemoveAt( 0 );
	}

	private UiButton PanelButton( string id, string model, UiRect bounds, int? help, Action? clicked ) => hud.Add( new UiButton
	{
		Id = id,
		Model = model,
		Help = help is { } index ? strings.Help( index ) : null,
		Clicked = clicked,
		Enabled = clicked != null,
		Bounds = bounds,
		Anchor = UiAnchor.BottomLeft
	} );

	/// <summary>An authored rectangle given as (left, top, right, bottom), as the original layout tables store them.</summary>
	private static UiRect Rect( float left, float top, float right, float bottom ) => new( left, top, right - left, bottom - top );

	private void Build()
	{
		// The information arm slides out from behind the main panel (drawn first).
		// [BIN:TPW-EXE:0x755150 object window] the window frame of the object windows is window2 (w_med) at (248,30)-(1800,1007)
		var infoArm = hud.Add( new UiModelImage { Id = "infoArm", Model = "window2", Bounds = OriginalLayoutTables.RideWindow.Root.Rect, Anchor = UiAnchor.Center, Visible = false } );
		infoElements.Add( infoArm );
		// [DATA:ui.wad:mainpanel,gauge,date,panel MD2 roots] authored HUD rectangles
		hud.Add( new UiModelImage { Id = "mainPanel", Model = "mainpanel", Bounds = new UiRect( 37.1f, 984.2f, 401.7f, 523.1f ), Anchor = UiAnchor.BottomLeft } );
		// [DATA:video 15:20] the message tube (red, left of the HUD) with its icons, and the buttons of the green slide-out panel: the bomb deletes the opened message, the red cross closes the panel
		hud.Add( new MessageTubeList( this ) { Id = "messageTube", Help = strings.Extra( OpenTpwText.MessageTubeHelp ), Bounds = TubeBase, Anchor = UiAnchor.BottomLeft } );
		// [BIN:TPW-EXE:0x7501a0 message panel] mb_erase at (486,1325)-(589,1428) deletes the opened message; the two action buttons above it (486,1218) and (486,1110) take the art of their action's kind (mb_locate, mb_callmech, mb_resrch, mb_buy, mb_accept, mb_decline: ParkHud.MessageTube.cs)
		// [APPROX:UI-025] only the challenge offers carry actions (mb_accept, mb_decline: ParkHud.ChallengeMessages.cs); the action buttons stay hidden for every other message, and which other original message types use which mb_ model is not traced
		hud.Add( new UiButton { Id = "messageDelete", Model = "mb_erase", Clicked = CloseMessagePanel, Bounds = OriginalLayoutTables.MessagePanel.Model( "mb_erase" ).Rect, Anchor = UiAnchor.BottomLeft, Visible = false } );
		for ( var slot = 0; slot < MessageActionIds.Length; slot++ )
		{
			var index = slot;
			hud.Add( new UiButton { Id = MessageActionIds[slot], Model = GameMessageKind.Locate.Model(), Clicked = () => { if ( index < openedActions.Length ) openedActions[index].Clicked(); },
				Bounds = OriginalLayoutTables.MessagePanel.Control( slot == 0 ? 0x10D2Fu : 0x10D30u ).Rect, Anchor = UiAnchor.BottomLeft, Visible = false } );
		}
		// [BIN:TPW-EXE:0x7529dc HUD arm table] the red X under the HUD that closes the panel is the arm table's b_retract (id 0x24) at (349,1386)-(429,1466), help 481
		hud.Add( new UiButton { Id = "messageClose", Model = "b_retract", Help = strings.Help( OriginalLayoutTables.HudArm.Model( "b_retract" ).Help ), Clicked = CloseArmPanel, Bounds = OriginalLayoutTables.HudArm.Model( "b_retract" ).Rect, Anchor = UiAnchor.BottomLeft, Visible = false } );
		hud.Add( new UiModelImage { Id = "gauge", Model = "gauge", Bounds = new UiRect( 66.6f, 1080.4f, 96.6f, 264.2f ), Anchor = UiAnchor.BottomLeft, Help = strings.Help( 477 ) } );
		// The gauge model is the empty dish (one 'thoughts2'-textured quad): the value is drawn over it.
		// [APPROX:UI-901] the fill is a rising coloured bar in the dish (the original draws the value another way); colour red→yellow→green — evidence needed: capture of the original gauge
		hud.Add( new HappinessGaugeFill( this ) { Id = "gaugeFill", Bounds = new UiRect( 66.6f, 1080.4f, 96.6f, 264.2f ), Anchor = UiAnchor.BottomLeft } );
		hud.Add( new UiModelImage { Id = "dateDisplay", Model = "date", Bounds = new UiRect( 152.6f, 1043.6f, 251.7f, 77.5f ), Anchor = UiAnchor.BottomLeft, Help = strings.Help( 478 ) } );
		// [DATA:Mac main HUD table 0x4ab38:32,47] date text region (182,1061,383,1103) in black (font slot 3); the cash text
		// (control 47, (258,60,720,260), white font slot 1) sits at the top left of the screen, not in the panel, whose
		// buttons start right below the date box. [BIN:TPW-EXE:0x4a2082 HUD builder] the original then resizes control 47 to the measured width of
		// "999999999" and the text height + 16, centred on i_dollar (CashTextBounds); the per-frame Updating hook below does the same.
		hud.Add( new UiLabel { Id = "date", Text = () => DateText, Font = fonts => fonts.Date, Color = new Veldrid.RgbaByte( 0, 0, 0, 255 ), Align = UiAlign.Center, Bounds = Rect( 182, 1061, 383, 1103 ), Anchor = UiAnchor.BottomLeft } );
		moneyLabel = hud.Add( new UiLabel { Id = "money", Text = () => MoneyText, Font = fonts => fonts.Balance, Color = UiColors.Text, ColorSource = () => CashColor( Status.Money ), Align = UiAlign.Left, Bounds = Rect( 258, 60, 720, 260 ), Anchor = UiAnchor.TopLeft, Help = strings.Help( 465 ) } );

		// [DATA:Mac main HUD table 0x4ab38:50,48,49,51-55] cash extras: i_dollar left of the balance, the change text (yellow) with green_up, golden key and ticket counters
		// [DATA:video 15:20, 73:21] the trend arrow beside the cash is red and points down after spending
		// [BIN:TPW-EXE:0x004a0c18 HUD balance refresh] 0x4a0ab0 sets i_dollar (control 0x32) to frame 1 for a negative balance and 0 otherwise, beside the balance's absolute value
		// [APPROX:UI-051] green_up frame 1 while the balance fell in the last seconds; the ticket count is the park's available tickets — evidence needed: the trend arrow's visibility and the in-park ticket spend reaching the player ledger
		economyElements.Add( hud.Add( new UiModelImage { Id = "dollar", Model = "i_dollar", Frame = () => Status.Money < 0 ? 1 : 0, Bounds = Rect( 37, 58, 242, 262 ), Anchor = UiAnchor.TopLeft } ) );
		economyElements.Add( hud.Add( new UiLabel { Id = "cashChange", Text = () => CashChangeText, Font = fonts => fonts.Balance, Color = UiColors.Highlight, Align = UiAlign.Right, Fit = true, Bounds = Rect( 458, 273, 720, 363 ), Anchor = UiAnchor.TopLeft } ) );
		economyElements.Add( hud.Add( new UiModelImage { Id = "cashTrend", Model = "green_up", Frame = () => CashChange < 0 ? 1 : 0, Bounds = Rect( 728, 109, 831, 211 ), Anchor = UiAnchor.TopLeft } ) );
		economyElements.Add( hud.Add( new UiModelImage { Id = "goldenKey", Model = "gkey", Bounds = Rect( 1853, 165, 1955, 268 ), Anchor = UiAnchor.TopRight } ) );
		economyElements.Add( hud.Add( new UiModelImage { Id = "goldenTicket", Model = "gtick", Bounds = Rect( 1853, 54, 1955, 156 ), Anchor = UiAnchor.TopRight } ) );
		economyElements.Add( hud.Add( new UiLabel { Id = "goldenKeys", Text = () => GoldenKeysText, Font = fonts => fonts.Balance, Align = UiAlign.Right, Fit = true, Bounds = Rect( 1688, 171, 1829, 261 ), Anchor = UiAnchor.TopRight } ) );
		economyElements.Add( hud.Add( new UiLabel { Id = "goldenTickets", Text = () => GoldenTicketsText, Font = fonts => fonts.Balance, Align = UiAlign.Right, Fit = true, Bounds = Rect( 1688, 60, 1829, 150 ), Anchor = UiAnchor.TopRight } ) );

		// [DATA:Mac main HUD table 0x4ab38:38-43] original button rectangles: staggered along the panel's curve, plus the camera button
		PanelButton( "buy", "b_buy", Rect( 170, 1122, 288, 1240 ), 469, level.IsReadOnlyVisit ? null : OpenBuyWindow );
		PanelButton( "info", "b_info", Rect( 287, 1133, 405, 1252 ), 470, OpenParkInfoWindow );
		PanelButton( "finance", "b_money", Rect( 156, 1241, 274, 1360 ), 471, OpenFinanceWindow );
		PanelButton( "research", "b_resrch", Rect( 274, 1254, 392, 1372 ), 472, OpenResearchWindow );
		PanelButton( "map", "b_map", Rect( 81, 1340, 200, 1458 ), 473, OpenMapWindow );
		// [BIN:TPW-EXE:0x498bb0 camera button] the original (HUD case 0x27, 0x498bb0/0x498bd0) opens slide-out panel 33 (table 0x751720) with camcorder mode (99, help 479; key C, 0x40c5c0 → 0x481a10, which builds a park-view mode object 0x46cff0) and send a postcard (100, help 480; Ctrl+P, 0x40c4c0 → 0x481500 → 0x4a9380, which also pauses the game) and starts sound 0x131; the button's help is 474
		// [APPROX:UI-042] the panel's sound 0x131 is not played; ParkHud.CameraPanel.cs
		PanelButton( "camera", "b_camera", Rect( 202, 1355, 320, 1474 ), 474, ToggleCameraPanel );
		BuildCameraPanel();
		BuildLocatorPanels();

		// [EXT:speed-buttons] the four speed buttons are an OpenTPW addition; the original has only the numpad time keys (0x40c3b0/0x40c3c0/0x40c3d0)
		// Speed control (OpenTPW buttons): each sets the clock scale of the whole simulation.
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

		// [BIN:TPW-EXE:0x755150 object window] the information arm is the original object window: see ParkHud.ObjectWindow.cs
		// [BIN:TPW-EXE:0x4ad4e0 ride door] the ride window latches b_door (0x668bec, state 5, the down frames) while the ride is closed (+0x68 == 0, 0x4e0440; "Closing" 0x4df300 clears it, "Opening" 0x4df390 sets it), and a latched button shows its second help id (0x669410: 12, open) and the other state the first (13, close); a click (message 0x100) with parameter 1 closes the ride, any other opens it (0x4af858 → 0x48ccf0)
		// [BIN:TPW-EXE:0x4af8b4 ride erase] b_erase (control 0x3e2b) deletes the ride (0x48cd10), asking first (UITEXT 0x18c) when the Confirmations option [0x78d90f] is on
		BuildObjectWindow();
		AddShopPriceButton();
		AddRideUpgradeButton();
		AddCallStaffButton();
		hud.DrawOverlay = context => { DrawFeedback( context ); DrawRideStatic( context ); };
		hud.DrawBackground = context =>
		{
			DrawCoasterPanelBackground( context );
			DrawMessagePanel( context );
		};
		BuildCoasterPanel();
		hud.Updating = FitCashText;
		// [BIN:TPW-EXE:0x488921 key dispatch] a key goes to the camera, game and shortcut groups in turn (0x40C990, the first nonzero callback wins); Escape's game row (0x748028, 0x40C180) ends any mode object other than the default park mode (types 0 and 1): it installs the default mode and SetMode(0) 0x52F200 without writing and consumes the key, so only the next Escape reaches the shortcut row "menu" (0x748378, 0x40C4D0), which opens the pause menu (0x4816D0); the path and queue tools run in a type-3 mode object (vtable 0x6FE9E0)
		hud.Back = () =>
		{
			if ( IsRiding )
				StopRide();
			else if ( CameraPanelOpen )
				CloseCameraPanel();
			// the object window is a window like the others: Escape closes it, and only without one open does Escape open the pause menu
			else if ( InfoArmOpen )
				SetInfoArm( false );
			else if ( CancelCamcorder() ) { }
			else if ( level.IsCoasterToolActive )
				level.ExitCoasterTool();
			else if ( level.StaffPlacement is { IsActive: true } staffTool )
				staffTool.Cancel();
			else if ( level.PatrolTool is { IsActive: true } patrolTool )
				patrolTool.Cancel();
			else if ( level.BuyLandTool is { IsActive: true } landTool )
				landTool.Cancel();
			else if ( level.CellTool.IsActive )
				level.CellTool.Cancel();
			else if ( pendingItem != null || level.BuildEntry != null )
				LeaveBuildTool();
			else
				OpenPauseMenu();
		};
	}


	private ObjectInfo? infoNow;
	/// <summary>The info arm's contents as computed once at the start of the frame (the draw lambdas read it).</summary>
	private ObjectInfo? InfoNow => infoNow ?? SelectedInfo;

	public ObjectInfo? SelectedInfo => InfoArmOpen && selectedObject is { IsDeleted: false } item
		? new ObjectInfo( item.Entry.ObjectNameIndex ?? -1, item.IsOpen, item.Runtime.IsAttraction, ObjectStats( item ), EntryName( item.Entry ) ) : null;

	/// <summary>The live economy record behind a placed object, null in a visit or without an economy.</summary>
	private ParkObjectState? LiveState( OriginalObject item ) =>
		Status.EconomySource is ParkEconomy economy && !level.IsReadOnlyVisit && level.GetEconomyInstance( item ) is int instance && economy.TryGetObject( instance, out var state ) ? state : null;

	public string ItemName( BuildItem item ) => item.Entry == null ? LandRowName( item ) ?? strings.Object( item.ObjectNameIndex ) : EntryName( item.Entry );

	private string EntryName( ObjectCatalogEntry entry ) => entry.ObjectNameIndex is int index
		? ObjectNames.Join( Enumerable.Range( index, Math.Max( 1, entry.ObjectNameLength ) ).Select( strings.Object ) )
		: entry.DisplayName;


	/// <summary>Shows the info arm for <paramref name="item"/> without closing the windows open below it (a list row's left click keeps its list).</summary>
	internal void ShowInfoArmKeepingWindows( OriginalObject item )
	{
		selectedObject = item;
		InfoArmOpen = !item.IsDeleted;
	}

	public void SetInfoArm( bool open )
	{
		InfoArmOpen = open && selectedObject is { IsDeleted: false };
		if ( InfoArmOpen )
			CloseBuyWindow();
	}

	/// <summary>Starts placing a catalog item (the click on the ground is handled by <see cref="Level"/>).</summary>
	public void BeginPlacing( BuildItem item )
	{
		if ( IsLandRow( item ) )
		{
			BeginLandTool( item );
			return;
		}
		if ( RejectReadOnlyAction() )
			return;
		if ( !Status.IsAvailable( item ) )
		{
			// [BIN:STP-PPC:0x10139A40 error feedback] the buy window's rejected purchase plays cat_ui event 0x1D (buy window callers 0x10163AFC/0x10163BFC)
			PostMessage( strings.Extra( OpenTpwText.NotAvailable ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return;
		}
		if ( Status.HasEconomy && (Status.PriceOf( item ) ?? item.Cost) > Status.Money )
		{
			PostMessage( strings.Help( 152 ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return;
		}
		if ( item.Entry is not { } entry )
			return;
		level.CellTool.Cancel();
		level.StaffPlacement?.Cancel();
		level.PatrolTool?.Cancel();
		level.BuyLandTool?.Cancel();
		pendingItem = item;
		level.BuildEntry = entry;
		level.IsRemovingObjects = false;
		level.IsPlacing = false;
		CloseBuyWindow();
		PostMessage( strings.Help( 440 ) );
	}

	// [BIN:TPW-EXE:0x5253b8 placement dispatcher] a placement without an entrance keeps the build tool armed when the click held exactly Ctrl (modifier mask 0xc), else the tool returns to mode 0 (0x52f580); a ride with an entrance continues into the entrance, exit and queue sub-modes (0x52fab0 / 0x52faf0, 0x52f580(3,0)), whose last click does the same Ctrl test. Level.PlaceObject owns purchase/guest linkage and its removal handler owns scrap credits
	// [APPROX:UI-031] the coaster and water-track tools (whose original sub-modes are not traced) end the build tool whatever the modifier, and the Ctrl tests at 0x5259e2/0x5259f4 (another exit path that keeps the tool on exactly Ctrl and otherwise leaves it and resets the angle) are not mapped to an OpenTPW action — evidence needed: those sub-modes' continuation and what 0x52fab0 tests
	private void OnObjectPlaced( OriginalObject item )
	{
		if ( pendingItem?.Entry != item.Entry )
			return;
		// Level.PlaceObject owns the purchase and guest link; this callback only finishes the HUD tool.
		PostMessage( string.Format( strings.Extra( OpenTpwText.Built ), ItemName( pendingItem ) ) );
		var placed = pendingItem;
		var queueFollows = item.Entry.HasQueue && item.Runtime.IsAttraction && item.Visitors.HasCells;
		if ( level.KeepBuildToolOnPlace && item.Runtime.Coaster == null && item.Runtime.Bump == null && !queueFollows )
		{
			// Ctrl at the placing click: the tool stays armed with its item and angle for the next placement.
			return;
		}
		pendingItem = null;
		level.IsPlacing = false;
		level.BuildEntry = null;
		if ( item.Runtime.Coaster != null && level.EnterCoasterTool( item ) )
		{
			level.ResetBuildRotation();
			PostMessage( strings.Help( 0x125 ) );
			return;
		}
		// a placed water ride enters the water track tool (tool mode 16) at its station; the queue tool follows once the circuit is valid (Level.RewalkTrack)
		if ( item.Runtime.Bump != null && level.EnterTrackTool( item ) )
		{
			level.ResetBuildRotation();
			PostMessage( TrackToolHelp );
			return;
		}
		// [BIN:STP-PPC:0x1007497C ride placement] a placed HasQueue ride (type record +64) enters tool mode 3, the queue tool (PATH-plan §8); park clicks lay its queue until Back
		if ( queueFollows && level.EnterQueueTool( item.Visitors ) )
		{
			// The queue tool's last click decides whether the build tool re-arms (Ctrl) or ends (OnCellToolEnded); the angle stays until then.
			queueRearmItem = placed;
			PostMessage( strings.Extra( OpenTpwText.LayQueue ) );
			return;
		}
		level.ResetBuildRotation();
	}

	/// <summary>The item whose queue the queue tool is laying: the build tool re-arms with it when the queue ends on a click holding exactly Ctrl.</summary>
	private BuildItem? queueRearmItem;
	private bool cellToolClicking;

	/// <summary>The path/queue tool turned off. Inside a click the decision waits for the click's result (<see cref="ResolveQueueRearm"/>); any other end (Escape, right click) returns the build tool to mode 0.</summary>
	private void OnCellToolEnded()
	{
		if ( queueRearmItem == null || cellToolClicking )
			return;
		queueRearmItem = null;
		level.ResetBuildRotation();
	}

	/// <summary>Whether a queue ended by a click re-arms the build tool: the click held exactly Ctrl and the line was not refused.</summary>
	// [BIN:TPW-EXE:0x5253b8 queue end] the queue end does the same Ctrl test as a placement: exactly Ctrl continues, else the build tool ends and the angle returns to 0 (0x52f3b6); a refused cell ends the tool without a placement, so it does not continue
	internal static bool RearmsAfterQueue( int modifierMask, SegmentResult? result ) => Level.KeepsBuildTool( modifierMask ) && result is not { Completed: false };

	/// <summary>Called after a queue-tool click: when that click ended the tool, re-arm the build tool or reset the angle.</summary>
	private void ResolveQueueRearm( SegmentResult? result )
	{
		var item = queueRearmItem;
		if ( item == null || level.CellTool.IsActive )
			return;
		queueRearmItem = null;
		if ( RearmsAfterQueue( Input.ModifierMaskDown, result ) && item.Entry is { } entry )
		{
			pendingItem = item;
			level.BuildEntry = entry;
			return;
		}
		level.ResetBuildRotation();
	}

	/// <summary>Ends the armed build tool (a window opened over it): the pending item goes and the build angle returns to 0.</summary>
	private void LeaveBuildTool()
	{
		if ( pendingItem == null && level.BuildEntry == null )
			return;
		pendingItem = null;
		level.IsPlacing = false;
		level.BuildEntry = null;
		level.ResetBuildRotation();
	}

	private void ToggleRideOpen()
	{
		if ( RejectReadOnlyAction() )
			return;
		if ( selectedObject is not { IsDeleted: false } item || !item.Runtime.IsAttraction )
			return;
		// [BIN:STP-PPC:0x1007159C open button] the open buttons call the reopening guard 0xde654 first and open only when it allows (ORACLE.md F27, ECON-049)
		if ( item.IsOpen )
			item.Close();
		else if ( level.Gate?.MayOpen( item.Runtime ) ?? true )
			item.Open();
	}

	private void DeleteRide()
	{
		if ( RejectReadOnlyAction() )
			return;
		if ( selectedObject is not { IsDeleted: false } item || item.Entry.IsFixedItem )
			return;
		void Remove()
		{
			// Catalogue objects sell and unlink exactly once; the developer prototype removes through its own seam.
			level.RemoveAttraction( item );
			selectedObject = null;
			SetInfoArm( false );
		}
		if ( GameOptions.Current.Confirmations )
			Stack.Push( UiDialogs.Message( "confirmDelete", () => strings[UIStrings.DeleteItem],
				(() => strings.Value( UIStrings.Yes ), () => { Stack.Pop(); Remove(); }),
				(() => strings.Value( UIStrings.No ), Stack.Pop) ) );
		else Remove();
	}

	private bool RejectReadOnlyAction()
	{
		if ( !level.IsReadOnlyVisit )
			return false;
		PostMessage( OnlineStrings.Get( OnlineLabel.ReadOnlyVisit ) );
		return true;
	}

	/// <summary>True while this level owns an active ride view.</summary>
	public bool IsRiding => level.RideView != null;

	/// <summary>
	/// Boards a placed ride: the camera becomes the ride's view, keeping the heading the player had. The original's
	/// "Ride it!" button boards any ride whose model has a camera seat, and its visitors could ride the published
	/// park's rides (docs/reverse/ONLINE-VISIT.md); building/selling stays impossible while riding.
	/// </summary>
	public void StartRide( OriginalObject? item )
	{
		if ( item is not { IsDeleted: false } candidate || !level.EnterRideView( candidate ) )
			return;
		PostMessage( OnlineStrings.Get( OnlineLabel.Ride ) + ": " + EntryName( candidate.Entry ) );
	}

	/// <summary>Leaves the ride: the park view returns where it was.</summary>
	public void StopRide() => level.ExitRideView();

	public void OpenPauseMenu()
	{
		if ( Paused )
			return;
		// [BIN:TPW-EXE:0x48c830 pause menu] the in-park pause menu has no window and no title: its lines are, in order, Load, Save, Restart Park, Publish Park, Options, Resume Game, Exit To Lobby and Quit Game (UITEXT 3, 4, 10, 5, 6, 7, 12, 8)
		// [DATA:video 27:42-27:47] large cyan text lines, white while focused, over the dimmed park: Laden, Opslaan, Park Herstarten, Park Publiceren, Opties, Spel Hervatten, Terug naar de Lobby, Spel Stoppen
		// [EXT:pause-menu-go-online] the Go Online line (UITEXT 1) after Options is OpenTPW's: the original reaches the online world from the lobby's pause menu only
		UiScreen? menu = null;
		var entries = new List<(UIStrings Label, Action Clicked)>
		{
			(UIStrings.Load, () => { if ( host.CreateLoad != null ) Stack.Push( host.CreateLoad( Stack ) ); }),
			(UIStrings.Save, SavePark),
			(UIStrings.RestartPark, () => Stack.Push( UiDialogs.Message( "confirmRestart", () => strings[UIStrings.ConfirmRestartPark],
				(() => strings.Value( UIStrings.Yes ), () => { Stack.Pop(); host.RestartPark?.Invoke(); }), (() => strings.Value( UIStrings.No ), Stack.Pop) ) )),
			(UIStrings.PublishPark, () => host.PublishPark?.Invoke( Stack )),
			(UIStrings.Options, () => { if ( host.CreateOptions != null ) Stack.Push( host.CreateOptions( Stack, () => { } ) ); }),
			(UIStrings.GoOnline, () => host.GoOnline?.Invoke( Stack )),
			(UIStrings.ResumeGame, ClosePauseMenu),
			(UIStrings.ExitToLobby, () => { if ( menu is { IsRemoved: false } && ReferenceEquals( Stack.Top, menu ) ) host.ExitToLobby(); }),
			(UIStrings.QuitGame, () => Stack.Push( UiDialogs.Message( "confirmQuit", () => strings[UIStrings.ConfirmQuit],
				(() => strings.Value( UIStrings.Yes ), host.Quit), (() => strings.Value( UIStrings.No ), Stack.Pop) ) )),
		};
		// [BIN:STP-PPC:0x10197D90 online pause menu] the online game type's pause menu offers only Resume Game, Exit To Lobby
		// and Quit Game; a read-only visit of a shared park uses it (no Save, Load, Options or Go Online).
		if ( level.IsReadOnlyVisit )
			entries.RemoveAll( entry => entry.Label is not (UIStrings.ResumeGame or UIStrings.ExitToLobby or UIStrings.QuitGame) );
		if ( host.GoOnline == null )
			entries.RemoveAll( entry => entry.Label == UIStrings.GoOnline );
		var items = entries.Select( entry => (Id: entry.Label.ToString(), Text: (Func<string>)(() => strings[entry.Label]), entry.Clicked,
			Enabled: PauseMenuEntryEnabled( level.IsReadOnlyVisit, entry.Label, host.CreateLoad != null, host.CreateOptions != null, host.RestartPark != null, host.PublishPark != null )) ).ToList();
		menu = UiDialogs.TextMenu( PauseMenuScreen, items, ClosePauseMenu, UIStrings.ResumeGame.ToString() );
		Stack.Push( menu );
	}

	public void ClosePauseMenu()
	{
		while ( Stack.Screens.Count > 1 )
			Stack.Pop();
	}

	/// <summary>
	/// Whether a pause-menu entry is clickable: a read-only visit never saves, and an entry without its host
	/// action (Load, Options) is disabled instead of clicking to nothing.
	/// </summary>
	internal static bool PauseMenuEntryEnabled( bool readOnlyVisit, UIStrings label, bool canLoad, bool canOptions, bool canRestart = true, bool canPublish = true ) =>
		!(readOnlyVisit && label == UIStrings.Save) &&
		!(label == UIStrings.Load && !canLoad) &&
		!(label == UIStrings.Options && !canOptions) &&
		!(label == UIStrings.RestartPark && !canRestart) &&
		!(label == UIStrings.PublishPark && !canPublish);

	private void SavePark()
	{
		if ( RejectReadOnlyAction() )
			return;
		if ( level.OriginalPark != null && host.SavePark == null )
		{
			PostMessage( strings.Extra( OpenTpwText.OriginalParkReadOnly ) );
			ClosePauseMenu();
			return;
		}
		try
		{
			if ( host.SavePark != null ) host.SavePark();
			else level.SaveSandbox();
			PostMessage( strings.Extra( OpenTpwText.ParkSaved ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException )
		{
			PostMessage( string.Format( strings.Extra( OpenTpwText.SaveFailed ), exception.Message ) );
		}
		ClosePauseMenu();
	}

	/// <summary>Park shortcuts wait while a pause/menu, text field or keyboard-capturing developer UI owns input.</summary>
	internal static bool ParkShortcutsAllowed( bool paused, bool overlayOpen, bool textEntry, bool keyboardCaptured ) =>
		!paused && !overlayOpen && !textEntry && !keyboardCaptured;

	/// <summary>
	/// One frame: HUD input first (so clicks on the HUD never reach the park), then park selection,
	/// purchase bookkeeping and messages. Returns true when the pointer is over the UI.
	/// </summary>
	public bool Update( UiContext context, UiInput input ) => Update( context, input, null, null, null );

	// Explicit captures run the identical frame body in asset-free tests. Null samples native IO at its original point.
	internal bool Update( UiContext context, UiInput input, bool? mouseCaptured, bool? keyboardCaptured, bool? textCaptured )
	{
		var ridingAtStart = IsRiding;
		SyncRideScreen( context.Delta );
		var walkingAtStart = CamcorderArmed || level.FreeWalk != null;
		foreach ( var element in infoElements )
			element.Visible = InfoArmOpen && selectedObject is { IsDeleted: false };
		ApplyObjectLayout();
		if ( selectedObject is not { IsDeleted: false } && InfoArmOpen )
			InfoArmOpen = false;

		if ( hud.Find( "openRide" ) is UiButton openButton )
		{
			// a visit reads the published state while drawing instead (the snapshot stays empty)
			infoNow = level.IsReadOnlyVisit ? null : SelectedInfo;
			openButton.Enabled = !level.IsReadOnlyVisit && infoNow?.CanOpen == true;
		}
		if ( hud.Find( "deleteRide" ) is UiButton deleteButton )
			deleteButton.Enabled = !level.IsReadOnlyVisit && selectedObject is { IsDeleted: false } item && !item.Entry.IsFixedItem;
		if ( hud.Find( "rideView" ) is UiButton rideButton )
			rideButton.Enabled = IsRiding || (selectedObject is { IsDeleted: false } candidate && RideCameraMode.CanRide( candidate ));

		UpdateCoasterPanelElements();
		UpdateShopPriceButton( mouseCaptured, keyboardCaptured, textCaptured );
		UpdateRideUpgradeButton( input );
		UpdateCallStaffButton();
		TrackCashChange( context.Delta );
		UpdateCameraPanel();
		UpdateMessageTube( context.Delta );
		UpdateLocatorPanels();
		SampleHappiness( context.Delta );
		var paused = Paused;
		var overUi = Stack.Covers( context.Canvas, input.Mouse ) || Stack.Screens.Count > 1;
		var consumed = Stack.Update( context, input );
		// [BIN:TPW-EXE:0x488aa1 RMBCancelOn] the original's screens read the option (+0x39) before a right click leaves them; the object window leaves on it too
		if ( InfoArmOpen && Stack.Screens.Count == 1 && !IsRiding && input.RightPressed && GameOptions.Current.RmbCancel && !Input.TextEntryActive )
		{
			SetInfoArm( false );
			consumed = true;
		}
		ReportScreens();
		if ( Camera.Mode is ParkCameraMode camera )
		{
			camera.UiCapturesKeyboard = Input.TextEntryActive || Stack.Screens.Count > 1;
			camera.ModalOpen = Stack.Screens.Any( screen => screen != hud && screen.Modal );
		}
		// [APPROX:UI-093] the camcorder's message procedure (0x488a00) acts only on Escape, the C shortcut (action 0x10) and the postcard action (0xf) and passes every other key to 0x65f66e, which calls the window's default handler (+0x118, 0x65f6d1 for ordinary windows, set at 0x65bd42) that has no key case and returns 0, so the pause key and the park door shortcuts are not offered while the ride view is open — evidence needed: whether a key the top window returns 0 for still reaches the HUD's key groups (0x488921)
		if ( !paused && !IsRiding && input.Has( UiKeys.Pause ) )
			Status.Speed = Status.Speed == GameSpeed.Paused ? GameSpeed.Normal : GameSpeed.Paused;

		if ( mouseCaptured == null || keyboardCaptured == null || textCaptured == null )
		{
			var io = ImGuiNET.ImGui.GetIO();
			mouseCaptured ??= io.WantCaptureMouse;
			keyboardCaptured ??= io.WantCaptureKeyboard;
			textCaptured ??= io.WantTextInput;
		}
		// the ride view's own screen is not an overlay of the park: its exit keys stay live
		var shortcutsAllowed = ParkShortcutsAllowed( paused, Stack.Screens.Count > (rideScreen != null ? 2 : 1), Input.TextEntryActive, keyboardCaptured.Value || textCaptured.Value );

		var camcorderOwnsInput = UpdateCamcorder( input, new NVector2( context.Canvas.Width, context.Canvas.Height ),
			!shortcutsAllowed || overUi || consumed || mouseCaptured.Value || input.Text.Length > 0 || ridingAtStart ) || walkingAtStart;

		// Ride It (E) rides the selected ride; Escape, the C key, the eject button and a right click (RMB cancel on, after two seconds) leave the ride view, and E again does too.
		// [EXT:ride-view-key] E rides the selected ride as the object window's b_ride it! button does
		if ( shortcutsAllowed && !IsRiding && Input.Pressed( InputButton.RideView ) && selectedObject is { IsDeleted: false } rideObject )
			level.EnterRideView( rideObject );
		// only a ride view that was already open at the start of the frame can be left by a key: the E that boarded it must not also leave it
		if ( ridingAtStart )
			UpdateRideExits( input, shortcutsAllowed, mouseCaptured.Value );

		// The park door's keyboard shortcuts, named by the door button's help rows (UIHELPTEXT 193/194: Ctrl-O/Ctrl-C).
		// [DATA:UIHELPTEXT 193 "Left-click to open the park (Ctrl-O)", 194 "Left-click to close the park (Ctrl-C)"]
		// [EXT:maexah/OpenTPW park-engine.md] the original's shortcut table row 9 maps Ctrl-C to the close-park handler (PC 0x0040c410)
		if ( shortcutsAllowed && !IsRiding && FinanceEconomy is { } doorEconomy && !level.IsReadOnlyVisit )
		{
			// [BIN:TPW-EXE:0x0040C410 close-park handler] the shortcut calls the door routine 0x519EF0 whatever the park's state, which posts event 4 (0x51A1C6, also from a closed park) or, for opening, event 3 (0x51A033) every time
			if ( Input.Pressed( InputButton.OpenPark ) )
			{
				if ( !doorEconomy.IsParkOpen )
					doorEconomy.OpenPark();
				host?.ParkDoorPressed?.Invoke( true );
			}
			if ( Input.Pressed( InputButton.ClosePark ) )
			{
				if ( doorEconomy.IsParkOpen )
					doorEconomy.ClosePark();
				host?.ParkDoorPressed?.Invoke( false );
			}
		}

		// The original window keys (the key table the InputButton defaults document; the research and map buttons'
		// own help rows name "(R)" and "(Space)"), the time keys and the blueprint-turn keys.
		HandleScreenShortcuts( shortcutsAllowed && !camcorderOwnsInput );
		if ( TryOpenPostcard( shortcutsAllowed && !overUi && !consumed && !mouseCaptured.Value && input.Text.Length == 0 && !ridingAtStart ) )
		{
			overUi = consumed = true;
			if ( input.LeftDown || input.LeftPressed ) toolPress = true;
		}

		if ( level.FreeWalk is { } walk )
		{
			walk.UiCapturesMouse = overUi || consumed || mouseCaptured.Value;
			walk.UiCapturesKeyboard = !shortcutsAllowed;
			walk.ModalOpen = ridingAtStart || Stack.Screens.Any( screen => screen != hud && screen.Modal );
		}
		// Returning restores the saved pose before orbit controls resume next frame.
		if ( walkingAtStart && level.FreeWalk == null && Camera.Mode is ParkCameraMode returningCamera )
			returningCamera.ModalOpen = true;

		if ( level.LastActionMessage != lastLevelMessage )
		{
			lastLevelMessage = level.LastActionMessage;
			if ( lastLevelMessage.StartsWith( "Cannot build", StringComparison.Ordinal ) )
			{
				// [BIN:STP-PPC:0x10139A40 error feedback] a rejected action plays cat_ui event 0x1D (called from the buy window at 0x10163AFC/0x10163BFC and the checkbox dialog at 0x1013B7A4)
				// [APPROX:AUDIO-009] the traced 0x1D callers are buy and hire money and limit checks (tp.exe 0x4879d0, 0x4ac590, 0x4af500); no placement-tool site was found (the build tool's click handler 0x52496e plays only cat_ui 0xAF), and the object-placement refusal cases of its tool switch were not enumerated
				PostMessage( strings.Extra( OpenTpwText.CannotBuildHere ) );
				GameAudio.PlayUi( GameAudio.ErrorEvent );
			}
		}

		// [BIN:TPW-EXE:0x485780 UI message hook] a 0x100 (activated) message plays cat_ui event 0x1F, or 0xBD when the control's construction style (+0x48) has bit 0x10 (0x4857cb)
		// [APPROX:AUDIO-004] which original controls are created with style bit 0x10 is not traced, so the view always plays 0x1F
		if ( !overUi && !consumed && input.LeftPressed )
			GameAudio.PlayUi( ParkViewClickEvent );

		// The ride view is a view of one ride: the park's clicks and tools wait until it is left.
		var rideOwnsInput = ridingAtStart || IsRiding || camcorderOwnsInput;
		var inPark = !overUi && !consumed && !rideOwnsInput && level.TryGetGridCell( input.Mouse, new NVector2( context.Canvas.Width, context.Canvas.Height ), out cursorX, out cursorY );
		if ( !inPark )
			cursorX = cursorY = -1;
		// Also consume the exit frame: leaving a ride must not activate an armed tool on that same click.
		var staffToolClick = !rideOwnsInput && (UpdateStaffTool( input, inPark && !mouseCaptured.Value ) || UpdatePatrolTool( input, inPark && !mouseCaptured.Value )
			|| UpdateBuyLandTool( input, inPark && !mouseCaptured.Value ));
		var visitorClick = TrySelectWorldVisitor( input, new NVector2( context.Canvas.Width, context.Canvas.Height ),
			paused || overUi || consumed || rideOwnsInput || mouseCaptured.Value || keyboardCaptured.Value || textCaptured.Value || staffToolClick || level.StaffPlacement is { IsActive: true } || level.PatrolTool is { IsActive: true }
			|| level.BuyLandTool is { IsActive: true } );
		var toolClick = staffToolClick || visitorClick || rideOwnsInput || UpdateCoasterTool( context, input, inPark, paused ) || UpdateCellTool( context, input, inPark, paused );
		// A right click leaves a build tool that Ctrl kept armed (and one still waiting for its first click).
		if ( inPark && !rideOwnsInput && !paused && input.RightPressed && GameOptions.Current.RmbCancel && level.BuildEntry != null && !toolClick )
		{
			LeaveBuildTool();
			toolClick = true;
		}
		UpdateBlueprint( inPark && !rideOwnsInput );
		// [APPROX:UI-027] Select by occupied grid cell; original cursor picking is not verified.
		if ( inPark && level.BuildEntry == null && !level.IsPlacing && !level.CellTool.IsActive && !level.IsCoasterToolActive && level.StaffPlacement is not { IsActive: true } && level.PatrolTool is not { IsActive: true }
			&& level.BuyLandTool is not { IsActive: true } && input.LeftReleased && !toolPress )
			SelectObject( level.Objects.FindAt( cursorX, cursorY ) );
		if ( input.LeftReleased )
			toolPress = false;

		for ( var index = messages.Count - 1; index >= 0; index-- )
		{
			messages[index] = (messages[index].Text, messages[index].Age + context.Delta);
			if ( messages[index].Age > MessageSeconds )
				messages.RemoveAt( index );
		}
		UpdateCursor( input, overUi || consumed, mouseCaptured == true );
		Status.Update( context.Delta );
		SyncRideScreen( 0 );
		return overUi || consumed || toolClick;
	}

	/// <summary>The patrol tool's HUD side: the hovered cell, right click leaves, a left click stores a corner or sets the area (tp.exe mode 0x36).</summary>
	private bool UpdatePatrolTool( UiInput input, bool inPark )
	{
		if ( level.PatrolTool is not { IsActive: true } tool )
			return false;
		if ( input.RightPressed && inPark && GameOptions.Current.RmbCancel )
		{
			tool.Cancel();
			return toolPress = true;
		}
		if ( inPark )
			tool.Hover( cursorX, cursorY );
		else
			tool.Hover( null, null );
		if ( !inPark || !input.LeftPressed )
			return false;
		tool.Click( cursorX, cursorY );
		return toolPress = true;
	}

	private bool staffCursorSet;

	/// <summary>
	/// The staff tools' HUD side (hire placement and carrying): the cursor, the hovered cell, right click leaves, a left click on a valid cell completes it.
	/// A click on an invalid cell does nothing and the tool stays; the park view's other clicks (guest selection, the path tool) wait while it runs.
	/// </summary>
	// [APPROX:UI-100] the original loads its cursors from Data/ui/cursors/*.ani with LoadCursorFromFileA (0x489639, IAT 0x6fd314) into a cursor object (0x661935): OS animated cursors drawn at the pointer hot spot (a software cursor path, "bwcursor", also exists); OpenTPW has no .ani loader, so the tools' cursor 9 (c_carry.ani) is the closest cursor it has, Input.CursorTypes.Carry (Ccar.tga), and the dangling sprite hangs over the hovered cell's centre instead of the exact mouse point — evidence needed: a .ani cursor loader
	private bool UpdateStaffTool( UiInput input, bool inPark )
	{
		var tool = level.StaffPlacement;
		if ( tool == null || !tool.IsActive )
		{
			if ( staffCursorSet )
			{
				staffCursorSet = false;
				Input.CursorType = Input.CursorTypes.Normal;
			}
			return false;
		}
		staffCursorSet = true;
		Input.CursorType = Input.CursorTypes.Carry;
		// the same capture gate as the visitor selection: a press on an ImGui overlay is not a park click
		if ( input.RightPressed && inPark && GameOptions.Current.RmbCancel )
		{
			tool.Cancel();
			return toolPress = true;
		}
		if ( inPark )
			tool.Hover( cursorX, cursorY );
		else
			tool.Hover( null, null );
		if ( !inPark || !input.LeftPressed )
			return false;
		// a click on an invalid cell only logs in the original ("Can't put staff here"): no player text, the tool stays
		tool.Click( cursorX, cursorY );
		return toolPress = true;
	}

	/// <summary>
	/// The path/queue tool's HUD side: park-view help (441–445), the ghost line under the cursor, Backspace undo
	/// and right-click cancel. Clicks inside the tool are committed here; <see cref="Update"/> reports them as captured,
	/// so <see cref="Level"/>'s park click (the same commit for input without the HUD) skips them.
	/// </summary>
	private bool UpdateCellTool( UiContext context, UiInput input, bool inPark, bool paused )
	{
		var tool = level.CellTool;
		// [APPROX:PATH-013] PATH-PAUSE: while the pause menu is shown the park takes no tool clicks, as in the original (tp.exe gate 0x48c8d0 reads the pause-menu object 0x7c2534 +0xc; the park click handler 0x4879d0 returns before the tool dispatch); OpenTPW's speed pause keeps the tool live; the original's pause key P (0x40bf70 → 0x409350(1)) also toggles a window (0x48a720 → 0x4a27f0 → 0x47ed80): a full-screen layer (0x65dd7b(1, 1, 1, 0, 0, 0x7ff, 0x5ff), the 'lolight' backdrop) under the panel 5094 (174,500)-(1874,900) of table 0x752e00, and the park click handler 0x4879d0 does not read the pause flag 0x786b84 — still open: whether that full-screen layer stops the click route to 0x4879d0 (the routine that calls it at 0x488768 is not a defined procedure)
		// The remove tool owns the park click while it is on (Level.RemoveAt clears paths and queues too).
		if ( paused || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects || level.IsCoasterToolActive
			|| level.PatrolTool is { IsActive: true } || level.StaffPlacement is { IsActive: true } || level.BuyLandTool is { IsActive: true } )
			return false;
		if ( inPark && tool.HoverHelpId( level.Paths, cursorX, cursorY ) is int help )
			context.HoverHelp = strings.Help( help );
		if ( !tool.IsActive )
		{
			// Backspace over a path cell with no tool on: the backtrack sound, then that cell is removed (GameAudio.BacktrackEvent).
			if ( inPark && input.Backspaces > 0 && Stack.Screens.Count == 1 && !Input.TextEntryActive && Camera.Mode is ParkCameraMode && level.BacktrackAt( cursorX, cursorY ) )
				return true;
			if ( !inPark || !input.LeftPressed || level.IsReadOnlyVisit || level.Objects.FindAt( cursorX, cursorY ) != null )
				return false;
			// A left click on a queue cell edits that ride's queue (help 444): the queue tool starts at its back cell.
			if ( tool.HoverHelpId( level.Paths, cursorX, cursorY ) is 444 && level.EditQueueAt( cursorX, cursorY ) )
			{
				PostMessage( strings.Help( 445 ) );
				tool.Hover( (cursorX, cursorY) );
				return toolPress = true;
			}
			// A left click on an empty owned cell or on a path cell starts the path tool there (help 441/442).
			if ( tool.HoverHelpId( level.Paths, cursorX, cursorY ) is not (441 or 442) || !level.EnterPathTool( cursorX, cursorY ) )
				return false;
			PostMessage( strings.Help( 443 ) );
			tool.Hover( (cursorX, cursorY) );
			return toolPress = true;
		}
		if ( input.Backspaces > 0 )
			tool.Undo();
		if ( input.RightPressed && inPark && GameOptions.Current.RmbCancel )
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
		var queueJoin = level.QueueJoinCell( tool );
		cellToolClicking = true;
		SegmentResult? result;
		try
		{
			result = tool.Click( (cursorX, cursorY) );
		}
		finally
		{
			cellToolClicking = false;
		}
		ResolveQueueRearm( result );
		// [BIN:TPW-EXE:0x527587 queue-join sound] it follows every commit; a click on a refused ghost lays nothing and returns before the commit (0x524A63)
		if ( result is { GhostRefused: false } )
			level.PlayQueueJoin( queueJoin );
		// [BIN:STP-PPC:0x10070DE0 path lay click] a committed path line plays cat_ui 101 (the tool's commit site, gated by its mode); a click on a refused ghost (or a refused track commit) plays cat_ui 175 at the clicked cell (AUDIO-022)
		if ( result is { Completed: true } )
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
		if ( result is { Completed: false } )
		{
			// a commit stopped only by OpenTPW's own queue cap is not one of the original's refusals (QUEUE-013)
			if ( !result.StoppedByOwnLimit )
				GameAudio.PlayPathRefused( cursorX, cursorY );
			PostMessage( strings.Extra( result.StoppedBy == CellBuildResult.NotEnoughMoney ? OpenTpwText.NotEnoughMoney : OpenTpwText.CannotBuildHere ) );
		}
		return toolPress = true;
	}

	/// <summary>The water track tool's help line (OpenTPW text; the original's track-tool help is not identified).</summary>
	public const string TrackToolHelp = "Water track: click to lay a line of track tiles (two cells each), click again to continue; join the end of the station to complete the circuit. Right click or Escape leaves.";
	/// <summary>The cell under the cursor in the coaster tool, its check and the cost a pylon there would add.</summary>
	public (int X, int Y, CoasterPlaceResult Result, long Cost)? CoasterGhost { get; private set; }

	/// <summary>
	/// The build tools' ghosts as the original's blueprint markers: the path or queue tool's preview line, or the
	/// footprint of the object being placed under the cursor (<see cref="BlueprintMarkers"/>).
	/// </summary>
	// [BIN:STP-PPC:0x100866B8 SetCell ghost] LayLine(mode | 0x100) gives each previewed cell a BlueprintMesh face: blue (0) when the chooser 0x100895D8 accepts it, red (1) when it refuses it (money included)
	// [BIN:STP-PPC:0x10080C50 cell ghost] the same blue or red faces for the cells of other build modes, checked by 0x10084154
	// [BIN:TPW-EXE:0x0053C8D0 blueprint AddFace] the PC twins: AddFace 0x53C8D0 (face records at 0x820B00 + i * 0xA0, callers 0x46C480, 0x4FA030, 0x532FC0 and 0x5370E0), the ghost routine 0x5370E0 (called from 0x52BA50 and 0x53AAC0) whose chooser 0x539760 returns 1 for the refused branch 0x537A1B, 0 for the default texture and otherwise that texture, and 0x532FC0 (called from 0x452D30, 0x5286A0, 0x528A70 and 0x536100); the chooser walks neighbouring cell types (0x536340–0x536380, type 0x15)
	// [APPROX:BLUEPRINT-003] a line marks every cell up to the refused one, existing cells blue; a footprint is blue when the build rule and the purchase allow it, red otherwise — evidence needed: chooser 0x539760's result for existing path cells and the footprint caller among the 0x532FC0 callers (Mac 0x10075618)
	private void UpdateBlueprint( bool inPark )
	{
		if ( level.Blueprint is not { } blueprint )
			return;
		var cells = new List<(int X, int Y, BlueprintMarker Marker)>();
		if ( level.IsCoasterToolActive && level.CoasterPanel is { } coasterPanel )
		{
			// [BIN:TPW-EXE:0x5234d0 coaster tool markers] the build buttons mark the placement area (face 0x11f) and the cell under the pointer (face 0x115, red when the validator refuses it)
			foreach ( var (x, y) in coasterPanel.PlacementArea )
				cells.Add( (x, y, BlueprintMarker.Area) );
			if ( coasterPanel.IsBuilding && coasterPanel.Cursor is { } cursor )
				cells.Add( (cursor.X, cursor.Y, cursor.Result == CoasterPlaceResult.Ok ? BlueprintMarker.Allowed : BlueprintMarker.Refused) );
		}
		else if ( level.StaffPlacement is { IsActive: true, HoverValid: false, HoverCell: { } staffCell } )
			// [APPROX:UI-099] the cell-sized ground quad of an invalid staff cell is the blueprint's red face; the staff tools' per-frame method 0x46c480 (slot +0x18 of the place tool 0x6fea40 and the carry tool 0x6fea70) queues a square dynamic face with 0x53c8d0 at (x - 5, 1.0, z - 5), face type 1 (queued only when [0x763c98] is set; its 10th argument is a per-face flag, not a texture index), when one of the cell tests 0x5363f0, 0x535db0(0x40), 0x536450 holds or none of 0x536390, 0x536310, 0x536320 does; the renderer (0x53da49) takes type 1's material from slot [+0x50] + 16 × type — evidence needed: the texture of that material
			cells.Add( (staffCell.X, staffCell.Y, BlueprintMarker.Refused) );
		else if ( level.BuyLandTool is { IsActive: true } landTool )
		{
			// [APPROX:BLUEPRINT-003] the Buy Land ghost is the blueprint's cells along the rectangle's four edges (or the hovered post before the first click), blue while the validator allows the rectangle and red when it refuses it; the original draws a ghost fence model along the edges (0x53aac0 with the tool mode | 0x100) coloured by the refusal 0x816d50
			var marker = landTool.HoverRefused ? BlueprintMarker.Refused : BlueprintMarker.Allowed;
			foreach ( var (x, y) in landTool.PreviewEdge() )
				cells.Add( (x, y, marker) );
		}
		else if ( level.PatrolTool is { IsActive: true } patrol )
		{
			// Approximation (UI-024, registered at ParkHud.Build): the patrol tool's rectangle is previewed as the blueprint's edge cells between the first corner and the hovered cell; the original's marking is not traced
			foreach ( var (x, y) in patrol.PreviewEdge() )
				cells.Add( (x, y, BlueprintMarker.Allowed) );
			if ( patrol.FirstCorner == null && patrol.HoverCell is { } corner )
				cells.Add( (corner.X, corner.Y, BlueprintMarker.Allowed) );
		}
		else if ( level.CellTool.IsActive && level.CellTool.Ghost is { } ghost )
		{
			foreach ( var cell in ghost.Line )
			{
				var refused = cell == ghost.StoppedAt;
				cells.Add( (cell.X, cell.Y, refused ? BlueprintMarker.Refused : BlueprintMarker.Allowed) );
				if ( refused )
					break;
			}
		}
		else if ( inPark && level.BuildEntry is { } entry && cursorX >= 0 )
		{
			var rotation = level.BuildRotation;
			var anchor = Level.GetCentredAnchor( entry, cursorX, cursorY, rotation );
			var allowed = level.CheckObject( entry, cursorX, cursorY, rotation ) == OriginalPlacementResult.Allowed
				&& (level.Park == null || level.Park.Economy.CheckBuild( entry.InfoId ) == ParkEconomy.PurchaseResult.Ok);
			placementRefused = !allowed;
			foreach ( var (x, y, _) in ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ) )
				cells.Add( (x, y, allowed ? BlueprintMarker.Allowed : BlueprintMarker.Refused) );
		}
		if ( level.BuildEntry == null || !inPark )
			placementRefused = false;
		blueprint.Show( cells );
	}

	/// <summary>The cost of the tool's preview line beside its end (the markers themselves are <see cref="BlueprintMarkers"/>).</summary>
	private void DrawCellToolGhost( UiContext context )
	{
		if ( level.CellTool.Ghost is not { } ghost || !level.CellTool.IsActive )
			return;
		var viewport = new NVector2( context.Canvas.Width, context.Canvas.Height );
		var size = 10 * context.Canvas.TextScale;
		if ( Status.HasEconomy && ghost.Charged > 0 && level.TryProjectCell( ghost.SnappedEnd.X, ghost.SnappedEnd.Y, viewport, out var end ) )
		{
			var text = string.Format( System.Globalization.CultureInfo.InvariantCulture, "{0}{1:#,0}", strings[UIStrings.Dollar], ghost.Charged ).Replace( "  ", " " );
			context.DrawText( context.Fonts.Small, text, new UiRect( end.X + size, end.Y - size * 2, 300 * context.Canvas.TextScale, size * 4 ), ghost.Completed ? UiColors.Value : UiColors.Highlight, UiAlign.Left );
		}
	}

	private static readonly string[] TicketIds = { "goldenTicket", "goldenTickets" };
	private static readonly string[] KeyIds = { "goldenKey", "goldenKeys" };

	private void TrackCashChange( float delta )
	{
		foreach ( var element in economyElements )
			element.Visible = Status.HasEconomy;
		// [BIN:TPW-EXE:0x004a1045 HUD counter rows] the HUD's message 0x1e handler 0x4a0fb0 shows a counter's row (0x34/0x36 keys, 0x35/0x37 tickets) only while its count is not zero
		foreach ( var id in TicketIds )
			if ( hud.Find( id ) is { } ticketElement ) ticketElement.Visible = Status.HasEconomy && Status.GoldenTickets > 0;
		foreach ( var id in KeyIds )
			if ( hud.Find( id ) is { } keyElement ) keyElement.Visible = Status.HasEconomy && Status.GoldenKeys > 0;
		// A loaded save replaces the economy: start counting changes from its balance.
		if ( !ReferenceEquals( Status.EconomySource, cashSource ) )
		{
			cashSource = Status.EconomySource;
			cashTracker = new CashChangeTracker( Status.Money );
		}
		cashTracker.Update( Status.Money, delta );
		if ( hud.Find( "cashChange" ) is { } text )
			text.Visible = BuildActionCost != null;
		if ( hud.Find( "cashTrend" ) is { } arrow )
			arrow.Visible = Status.HasEconomy && CashChange != 0;
	}

	public void SelectObject( OriginalObject? item )
	{
		selectedObject = item;
		SetInfoArm( item != null );
	}

	/// <summary>The status line: the recent feedback lines in a translucent box above the popup help box.</summary>
	private void DrawFeedback( UiContext context )
	{
		// hidden while the info arm or the message panel is out, which cover the same part of the screen
		if ( messages.Count == 0 || InfoArmOpen || OpenedMessage != null )
			return;
		var canvas = context.Canvas;
		var text = string.Join( "\n", messages.Select( message => message.Text ) );
		var (width, height) = context.Measure( context.Fonts.Small, text, (int)(canvas.Width * 0.6f) );
		var pad = 4 * canvas.TextScale;
		var rect = new UiRect( (canvas.Width - width) / 2f - pad, canvas.Height * 0.91f - height - pad * 2, width + pad * 2, height + pad * 2 );
		context.Batch.AddRectangle( rect, UiColors.HelpBox );
		context.DrawText( context.Fonts.Small, text, rect.Inflate( -pad ), UiColors.Text, UiAlign.Center, wrap: true, shadow: false );
	}

	public void Draw( UiContext context )
	{
		DrawCellToolGhost( context );
		DrawCoasterGhost( context );
		if ( debugHelp != null )
			context.HoverHelp = debugHelp;
		Stack.Draw( context, HelpBarVisible );
		if ( debugModels is { Length: > 0 } )
			DrawDebugModels( context );
	}

	/// <summary>The preview shows the object's own sign text, like the object in the park (<see cref="ObjectSigns"/>).</summary>
	private static Func<string, UiTexture?>? SignTexture( BuildItem item )
	{
		if ( item.Entry == null || item.Entry.IsFixedItem )
			return null;
		var entry = item.Entry;
		return name => ObjectSigns.IsSignSlot( name ) && ObjectSigns.Get( entry )?.TextureFor( name ) is { } texture ? UiTexture.Of( texture.NativeTexture ) : null;
	}

	internal PreviewIcon? GetIcon( BuildItem item )
	{
		if ( icons.TryGetValue( item.Id, out var icon ) )
			return icon;
		try
		{
			icon = item.PreviewModel == null ? null : new PreviewIcon( ObjectAssets.LoadModel( item.PreviewModel ),
				name => item.TextureDirectories.Select( directory => UiImages.Resolve( name, directory ) ).FirstOrDefault( path => path != null ),
				SignTexture( item ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException )
		{
			Log?.Warning( $"Preview icon for {item.Id} unavailable: {exception.Message}" );
			icon = null;
		}
		icons[item.Id] = icon;
		return icon;
	}
}
