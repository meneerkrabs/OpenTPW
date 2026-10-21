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
	/// <summary>Existing authenticated session state, sampled when the park pause menu opens; null means offline.</summary>
	public Func<bool>? IsLoggedIn { get; init; }
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
	/// <summary>The HUD posts an advisor help message (0x486B00) that belongs to no screen, such as the camera panel's.</summary>
	public Action<int>? HelpPosted { get; init; }
	/// <summary>[EXT:restored-advice-tutorial] Asks the advisor for a help message the restored advice extension adds (<see cref="RestoredAdvice"/>: 202, 204, 306, 322); the advisor posts it only while the extension is on. Separate from <see cref="HelpPosted"/>, which carries the original's own posts.</summary>
	public Action<int>? RestoredHelp { get; init; }
	/// <summary>The HUD withdraws an advisor help message it posted (0x486B40).</summary>
	public Action<int>? HelpWithdrawn { get; init; }
	/// <summary>The interface mode changed (a tool started or ended): the advisor withdraws the help of the tools (0x52F200).</summary>
	public Action? ModeChanged { get; init; }
	/// <summary>
	/// Whether an advisor says the staff strike messages: it queues them as advice 33-57 and posts the text of each as the caption of its speech, so the HUD does not
	/// post the text itself. Null (no advisor) leaves the HUD to post it when the event happens.
	/// </summary>
	public Func<bool>? AdvisorSaysStaffMessages { get; init; }
	/// <summary>F1 was pressed in a HUD window that passes keys to the original's default window procedure; the advisor repeats the last tutorial advice.</summary>
	public Action? HelpKeyPressed { get; init; }
	/// <summary>The online players of a visited park, while there are any (a visit of a downloaded park while logged in); null otherwise.</summary>
	public Func<IVisitPresence?>? Presence { get; init; }
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
	/// <summary>[EXT:online-coop-lockstep] The level's session refusals the HUD has said already (<see cref="Level.SessionRefusals"/>).</summary>
	private int lastSessionRefusals;
	private BuildItem? pendingItem;
	private UiLabel? moneyLabel;
	private ListSortMemory? fallbackSorts;
	/// <summary>The session's remembered list sorts (UI-084); a HUD built without a host keeps its own.</summary>
	private ListSortMemory Sorts => host?.ListSorts ?? (fallbackSorts ??= new());
	private OriginalObject? selectedObject;
	private readonly List<UiElement> economyElements = new();
	private int cursorX = -1;
	/// <summary>Set by a press the path/queue tool used, so its release does not select an object.</summary>
	private bool toolPress;
	/// <summary>The refused path click's press latch: its sound plays at the release (GameAudio.PathRefusedEvent); created on first use.</summary>
	private PathRefusalClick? refusalClickState;
	private PathRefusalClick refusalClick => refusalClickState ??= new();
	private int cursorY = -1;

	public ParkHud( Level level, UiStringTable strings, IHudParkStatus status, IBuildCatalog catalog, HudHost host )
	{
		this.level = level;
		rideStatic = new EyeModeStaticTransition( EyeCameraActive );
		this.strings = strings;
		this.host = host;
		Status = status;
		challengeUserPaused = status.Speed == GameSpeed.Paused;
		Catalog = catalog;
		buy = new BuyListModel( catalog, ListName, BuyPrice, Sorts, LandRows, item => Status.IsAvailable( item ) );
		lastLevelMessage = level.LastActionMessage;
		lastSessionRefusals = level.SessionRefusals;
		try
		{
			level.Objects.ObjectPlaced += OnObjectPlaced;
			// Harnesses that build a Level without its constructor have no tool object.
			if ( (CellBuildTool?)level.CellTool is { } cellTool )
				cellTool.Ended += OnCellToolEnded;
			level.BuildToolsEnded += OnBuildToolsEnded;
			hud = new UiScreen( "hud" ) { Modal = false };
			Stack.Push( hud );
			Build();
			HookStaffStrikeMessages();
			HookChallengeMessages();
		}
		catch
		{
			// a HUD that fails partway leaves no handler on the level it would have shown
			UnhookLevel();
			throw;
		}
	}

	private void OnBuildToolsEnded()
	{
		pendingItem = null;
		queueRearmItem = null;
	}

	/// <summary>Removes every handler the constructor puts on the level and its park (removing one that was not added is a no-op).</summary>
	private void UnhookLevel()
	{
		if ( level.Objects is { } objects )
			objects.ObjectPlaced -= OnObjectPlaced;
		if ( (CellBuildTool?)level.CellTool is { } cellTool )
			cellTool.Ended -= OnCellToolEnded;
		level.BuildToolsEnded -= OnBuildToolsEnded;
		if ( level.Park != null )
		{
			level.Park.EventRaised -= OnStaffStrikeEvent;
			level.Park.ChallengePresentationPosted -= OnChallengePresentation;
		}
	}

	public UiScreenStack Stack { get; } = new();
	public IHudParkStatus Status { get; }
	public IBuildCatalog Catalog { get; }
	public UiScreen Screen => hud;
	public bool InfoArmOpen { get; private set; }
	/// <summary>The cat_ui event of a click in the park view.</summary>
	public const uint ParkViewClickEvent = 0x1F;

	public OriginalObject? SelectedObject => selectedObject;
	/// <summary>True while a menu is on top of the HUD; the buy, staff, attractions and visitors windows, a visit's chat mode and its name popups do not pause.</summary>
	public bool Paused => Stack.Screens.Any( screen => screen != hud && screen != rideScreen && screen != finaleScreen && screen != buyScreen && screen != staffScreen
		&& screen != attractionScreen && screen != visitorScreen && screen != chatModeScreen && screen.Name != OpenTPW.UI.ChatPanel.PopupScreenName );
	public IReadOnlyList<string> Messages => messages.Select( message => message.Text ).ToArray();
	/// <summary>The park simulation's time scale for the screens now open (see <see cref="TimeScaleFor"/>).</summary>
	public float SimulationTimeScale => TimeScaleFor( applicationPaused ? 0f : Status.TimeScale, Stack.Screens.Select( screen => screen.Name ) );

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
	// [BIN:TPW-EXE:0x48cd10 delete confirmation] the delete confirmation of an object or staff member (the object windows 0x4af600, 0x4b0b30, 0x4b2150, the attractions window 0x497640 and the All Staff window 0x4b4fc0) is a generic message box 0x47f020 with UITEXT 0x18c and the b_exit and b_okay buttons when the Confirmations option is on, so it pauses (0x47f26c) and its close resumes (0x47eed0, message 0x14 → 0x409300); the postcard (Ctrl+P, 0x4a9380) pauses the same way: confirmDelete, confirmDeleteAttraction and the postcard screen stop the park here; the employee dismiss box (0x4b5e5d, UITEXT 0x18d) is a 0x47f020 caller too (its call at 0x4b5eac, yes callback 0x4b6070), so confirmDismissStaff stops it as well
	// [APPROX:UI-096] the other message boxes (0x47f020's 27 call sites: the alert queue 0x47f620 that the goal and challenge code feeds through 0x47f410, the file errors of 0x4237f0, 0x4a77f0, the online notices 0x4bc500 and 0x4ec030) pause in the original and not here; application deactivation has a bounded offline host focus-loss latch (ParkHud.ApplicationPause), not full native activation/modal/audio parity — evidence needed: which of OpenTPW's other dialogs (the loan boxes, the restart and quit boxes) correspond to those call sites, and the remaining native activation/modal ownership gates
	// [EXT:texture-pack] A first-run download may take a long time; its modal keeps the park turn paused.
	public static float TimeScaleFor( float speedScale, IEnumerable<string> openScreens ) =>
		openScreens.Any( name => name is PauseMenuScreen or MapScreenName or "texturePackDownload" or PostcardScreen or "confirmDelete" or "confirmDeleteAttraction" or "confirmDismissStaff" ) ? 0 : speedScale;

	/// <summary>The name of the postcard composer's screen (it pauses the park while open, as Ctrl+P does).</summary>
	public const string PostcardScreen = "sendPostcard";

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

	/// <summary>
	/// The frame of the trend arrow beside the balance (control 49, <c>green_up</c>): 1 (the red down arrow) when the last closed month spent more than it took, else 0;
	/// 0 before the first month closes.
	/// </summary>
	// [BIN:TPW-EXE:0x4a0e30 date refresh] at every date update (0x4815e0 daily, 0x481560) the arrow's frame becomes 1 when the newest entry of the money-out sample ring (+0x1f5a4, index +0x1f7e4) exceeds the newest of the money-in ring (+0x1fc94, index +0x1fed4); an empty ring reads as the year, so both sides tie and the frame is 0
	// [BIN:TPW-EXE:0x4c7720 month samples] both rings take one entry per month close (month message 12; 144 entries each), the entries the hire window reads as its income and money-out rows (0x49bdd0); the builder 0x4a1d70 sets the arrow to frame 0 and never hides it, only the cost text (control 48)
	public int CashTrendFrame => FinanceEconomy is { } economy && economy.Ledger.History is { Count: > 0 } history && history[^1].MoneyOut > history[^1].MoneyIn ? 1 : 0;

	/// <summary>
	/// The cost of the build action under way, shown in yellow under the balance (control 48): the held blueprint's price, the path or queue line's cost so far, or the coaster track
	/// segment under the cursor; null outside the build tools, where the text is hidden.
	/// </summary>
	// [BIN:TPW-EXE:0x4a0cdd HUD cash refresh] control 48 shows the number the active tool keeps in 0x81af4c (read by 0x52fbc0) and is hidden, cost -10000, or reset to the unset marker -999 when no tool is active: only the mode objects whose type (vtable +0x24) is 3, 4 or 8 (8 while 0x79c658 bit 0 is set, 0x447990) show it; type 3 is the tool-mode object 0x6fe9e0 that every build tool runs in; it is no sum of balance changes
	// [DATA:video 2:05, 74:00-76:00] "$ 2500" while the blueprint of a 2500 ride is held, "$ 150" in the path tool, "$ 10000" for the coaster station and "$ 70" to "$ 130" per track segment; absent outside the build tools even when the balance changes
	// [APPROX:UI-047] coaster panel cost visibility now follows the traced build/move mode versus active height-drag distinction. OpenTPW still derives the active mode from its panel rather than reproducing the original mode-object stack; a missing panel keeps the existing cost fallback. The unidentified type-4 mode remains unqualified.
	public long? BuildActionCost
	{
		get
		{
			if ( !Status.HasEconomy )
				return null;
			if ( level.IsCoasterToolActive )
				return level.CoasterPanel is { ShowsActionCost: false } ? null : level.CoasterPanel?.DragCost ?? CoasterGhost?.Cost ?? 0;
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
		// UI-025: only the challenge offers carry actions (mb_accept, mb_decline: ParkHud.ChallengeMessages.cs); the action buttons stay hidden for every other message, and the original's other action kinds (0x48e610: mb_locate, mb_buy, b_loan, mb_resrch, b_pkinfo, b_staffcost, mb_callmech, b_entry) have no OpenTPW message that posts them
		hud.Add( new UiButton { Id = "messageDelete", Model = "mb_erase", Clicked = DeleteOpenedMessage, Bounds = OriginalLayoutTables.MessagePanel.Model( "mb_erase" ).Rect, Anchor = UiAnchor.BottomLeft, Visible = false } );
		for ( var slot = 0; slot < MessageActionIds.Length; slot++ )
		{
			var index = slot;
			hud.Add( new UiButton { Id = MessageActionIds[slot], Model = GameMessageKind.Locate.Model(), Clicked = () => { if ( index < openedActions.Length ) openedActions[index].Clicked(); },
				Bounds = OriginalLayoutTables.MessagePanel.Control( slot == 0 ? 0x10D2Fu : 0x10D30u ).Rect, Anchor = UiAnchor.BottomLeft, Visible = false } );
		}
		// [BIN:TPW-EXE:0x7529dc HUD arm table] the red X under the HUD that closes the panel is the arm table's b_retract (id 0x24) at (349,1386)-(429,1466), help 481
		hud.Add( new UiButton { Id = "messageClose", Model = "b_retract", Help = strings.Help( OriginalLayoutTables.HudArm.Model( "b_retract" ).Help ), Clicked = CloseArmPanel, Bounds = OriginalLayoutTables.HudArm.Model( "b_retract" ).Rect, Anchor = UiAnchor.BottomLeft, Visible = false } );
		// [DATA:tp.exe HUD table 0x752940] shell 0x1e (guage, host alias gauge), help477; child 0x1f at (85,1100)-(144,1324) has its own absolute rectangle.
		hud.Add( new UiModelImage { Id = "gauge", Model = "gauge", Bounds = new UiRect( 67, 1080, 96, 265 ), Anchor = UiAnchor.BottomLeft, Help = strings.Help( 477 ) } );
		// [APPROX:UI-901] the source-backed cropped vertical meter now replaces the synthetic colour fill; actual rendered shell/meter composition is still unverified.
		hud.Add( new HappinessGaugeFill( this ) { Id = "gaugeFill", Bounds = new UiRect( 85, 1100, 59, 224 ), Anchor = UiAnchor.BottomLeft } );
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
		// [APPROX:UI-051] the ticket count is the park's available tickets, where the original shows the player's earned tickets less the spent count (0x5af5d0, from the HUD handler 0x4a0fb0 at 0x4a10cd: the global flags once, every park's locals and the secret flags; a moved global ticket changes no count) (the purchase check 0x4d4ad0 → 0x5af740 adds the cost to the player's spent count +0x1c, which the park's own spent count does not reach) — evidence needed: routing the park's ticket purchases into the player ledger
		economyElements.Add( hud.Add( new UiModelImage { Id = "dollar", Model = "i_dollar", Frame = () => Status.Money < 0 ? 1 : 0, Bounds = Rect( 37, 58, 242, 262 ), Anchor = UiAnchor.TopLeft } ) );
		economyElements.Add( hud.Add( new UiLabel { Id = "cashChange", Text = () => CashChangeText, Font = fonts => fonts.Balance, Color = UiColors.Highlight, Align = UiAlign.Right, Fit = true, Bounds = Rect( 458, 273, 720, 363 ), Anchor = UiAnchor.TopLeft } ) );
		economyElements.Add( hud.Add( new UiModelImage { Id = "cashTrend", Model = "green_up", Frame = () => CashTrendFrame, Bounds = Rect( 728, 109, 831, 211 ), Anchor = UiAnchor.TopLeft } ) );
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
		// [BIN:TPW-EXE:0x498bb0 camera button] the original (HUD case 0x27, 0x498bb0/0x498bd0) opens slide-out panel 33 (table 0x751720) with camcorder mode (99, help 479; key C, 0x40c5c0 → 0x481a10, which builds a park-view mode object 0x46cff0) and send a postcard (100, help 480; Ctrl+P, 0x40c4c0 → 0x481500 → 0x4a9380, which also pauses the game) and posts advisor help 0x131 (ParkHud.CameraPanel.cs); the button's help is 474
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
				Clicked = () => SetUserGameSpeed( speed ),
				Selected = () => DisplayedGameSpeed == speed,
				Bounds = new UiRect( 1540 + index * 122, 1430, 116, 84 ),
				Anchor = UiAnchor.BottomRight
			} );
		}

		// [BIN:TPW-EXE:0x755150 object window] the information arm is the original object window: see ParkHud.ObjectWindow.cs
		// [BIN:TPW-EXE:0x4ad4e0 ride door] the ride window latches b_door (0x668bec, state 5, the down frames) while the ride is closed (+0x68 == 0, 0x4e0440; "Closing" 0x4df300 clears it, "Opening" 0x4df390 sets it), and a latched button shows its second help id (0x669410: 12, open) and the other state the first (13, close); a click (message 0x100) with parameter 1 closes the ride, any other opens it (0x4af858 → 0x48ccf0)
		// [BIN:TPW-EXE:0x4af8b4 ride erase] b_erase (control 0x3e2b) deletes the ride (0x48cd10), asking first (UITEXT 0x18c) when the Confirmations option [0x78d90f] is on
		BuildObjectWindow();
		AddShopPriceButton();
		AddWaveSettingsButton();
		BuildObjectSettingControls();
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
			else if ( level.AreaClearTool is { IsActive: true } clearTool )
				clearTool.Cancel();
			else if ( level.PoolTool is { IsActive: true } poolTool )
				poolTool.Cancel();
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
		// [BIN:TPW-EXE:0x4ac7f6 buy row refusal] on the PC the buy list's row activation (0x400, 0x4ac4f9) plays the error 0x1D (0x486ae0) when the money is below the price (0x4ac6c7) or a golden-ticket row cannot be paid (0x4ac5b0), and starts no tool; the row itself adds no click
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
		level.AreaClearTool?.Cancel();
		level.PoolTool?.Cancel();
		pendingItem = item;
		level.BuildEntry = entry;
		level.IsRemovingObjects = false;
		level.IsPlacing = false;
		CloseBuyWindow();
		PostMessage( strings.Help( 440 ) );
		// [BIN:TPW-EXE:0x4ac683 buy click] the chosen item's type record +0x4C (Info.WhichUIType) is 0 (a ride) and no ride stands in the park (0x4824d0(0x80, 0): the first placed object with that type and no add-on id, 0x4828f0): help 0xC9 (201), else 0xCE (206)
		PostToolHelp( AdvisorToolHelp.BuildStart( entry.WhichUIType, level.Objects.Objects.Any( placed => !placed.IsDeleted && placed.Entry.WhichUIType == 0 ) ) );
	}

	// [BIN:TPW-EXE:0x5253b8 placement dispatcher] a placement without an entrance keeps the build tool armed when the click held exactly Ctrl (modifier mask 0xc), else the tool returns to mode 0 (0x52f580); a ride with an entrance continues into the entrance, exit and queue sub-modes (0x52fab0 / 0x52faf0, 0x52f580(3,0)), whose last click does the same Ctrl test. Level.PlaceObject owns purchase/guest linkage and its removal handler owns scrap credits
	// [APPROX:UI-031] the coaster and water-track tools (whose original sub-modes are not traced) end the build tool whatever the modifier, and the Ctrl tests at 0x5259e2/0x5259f4 (another exit path that keeps the tool on exactly Ctrl and otherwise leaves it and resets the angle) (Ctrl alone, the builder 0x46b420) are only partly mapped: an entrance-bearing item (0x52fab0, sub-record +0x40) resets the angle to 0x80 and, with the record flag +0x29c = 1, ends the tool whatever the modifier — evidence needed: the coaster sub-mode's continuation and what +0x29c = 1 stands for
	private void OnObjectPlaced( OriginalObject item )
	{
		// [EXT:online-coop-commands] a placement inside a command: the HUD follows its own player's placement once the command has returned, so the tool the placed ride opens issues commands of its own;
		// a scheduled placement (a replay, a peer's) moves no tool here
		if ( level.Commands.IsExecuting )
		{
			if ( level.Commands.IsIssuing )
				level.Commands.AfterIssue( () => FinishPlacement( item ) );
			return;
		}
		FinishPlacement( item );
	}

	private void FinishPlacement( OriginalObject item )
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
		// [EXT:pools-slides] a placed slide tower enters the slide mode of the pool tool: the player lays the track from the tower down into the pool
		if ( PoolSlideTowers.IsSlideTower( item.Entry ) && level.EnterSlideTool( item ) )
		{
			level.ResetBuildRotation();
			PostMessage( strings.Extra( OpenTpwText.PoolSlideHint ) );
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
		// the command asks the reopening guard before it opens (SetAttractionOpenCommand)
		level.Commands.Issue( new SetAttractionOpenCommand( item.Runtime.AttractionId, !item.IsOpen ) );
	}

	private void DeleteRide()
	{
		if ( RejectReadOnlyAction() )
			return;
		if ( selectedObject is not { IsDeleted: false } item || item.Entry.IsFixedItem )
			return;
		void Remove()
		{
			// Catalogue objects sell and unlink exactly once; the developer prototype removes through its own seam ([EXT:online-coop-commands]: one command either way).
			level.Commands.Issue( new RemoveAttractionCommand( item.Runtime.AttractionId ) );
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
		// [BIN:TPW-EXE:0x48c830 pause menu] the ordinary in-park pause menu has no window and no title: its lines are, in order, Load, Save, Restart Park, Publish Park, Options, Resume Game, Exit To Lobby and Quit Game (UITEXT 3, 4, 10, 5, 6, 7, 12, 8)
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
			(UIStrings.ResumeGame, ResumeGameAfterApplicationPause),
			(UIStrings.ExitToLobby, () => { if ( menu is { IsRemoved: false } && ReferenceEquals( Stack.Top, menu ) ) host.ExitToLobby(); }),
			(UIStrings.QuitGame, () => Stack.Push( UiDialogs.Message( "confirmQuit", () => strings[UIStrings.ConfirmQuit],
				(() => strings.Value( UIStrings.Yes ), host.Quit), (() => strings.Value( UIStrings.No ), Stack.Pop) ) )),
		};
		// [BIN:TPW-EXE:0x48c150 pause menu selection] game type 1 (0x48c1e3) selects the three online entries before the
		// world-state-4 test (0x48c27a). Otherwise a lost park gets Load, Restart, Resume, Exit To Lobby and Quit, in that order.
		// The loss Load row uses UITEXT 3 and command 22, not text 22. Its 0x49efb0 argument is unused; the extra 0x5da3c0
		// calls on loss Restart/Exit/Quit return immediately, so the existing callbacks and confirmations are shared (0x48b6a0).
		if ( level.IsReadOnlyVisit )
			entries.RemoveAll( entry => entry.Label is not (UIStrings.ResumeGame or UIStrings.ExitToLobby or UIStrings.QuitGame) );
		else if ( level.Park?.Economy.IsBankrupt == true )
			entries.RemoveAll( entry => entry.Label is not (UIStrings.Load or UIStrings.RestartPark or UIStrings.ResumeGame or UIStrings.ExitToLobby or UIStrings.QuitGame) );
		if ( host.GoOnline == null )
			entries.RemoveAll( entry => entry.Label == UIStrings.GoOnline );
		var items = entries.Select( entry => (Id: entry.Label.ToString(), Text: (Func<string>)(() => strings[entry.Label]), entry.Clicked,
			Enabled: PauseMenuEntryEnabled( level.IsReadOnlyVisit, entry.Label, host.CreateLoad != null, host.CreateOptions != null, host.RestartPark != null, host.PublishPark != null )) ).ToList();
		menu = UiDialogs.TextMenu( PauseMenuScreen, items, ResumeGameAfterApplicationPause, UIStrings.ResumeGame.ToString(), compact: host.IsLoggedIn?.Invoke() == true );
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
		SyncChallengeMessageSuspension();
		QueueChallengeTerminalTimers();
		if ( UpdateResearchFinale( context, input, mouseCaptured, keyboardCaptured, textCaptured ) )
		{
			// The full-screen owner consumes input, not the unscaled UI timer/delayed-release pumps.
			rideStatic.SkipFrame(); // no camera observation or timestamp refresh while the finale owns this update
			FinishChallengeMessageFrame();
			return true;
		}
		var ridingAtStart = IsRiding;
		SyncRideScreen();
		HandleChatModeKey();
		SyncChatMode();
		var walkingAtStart = CamcorderArmed || level.FreeWalk != null;
		foreach ( var element in infoElements )
			element.Visible = InfoArmOpen && selectedObject is { IsDeleted: false };
		ApplyObjectLayout();
		if ( selectedObject is not { IsDeleted: false } && InfoArmOpen )
			InfoArmOpen = false;
		SyncObjectSettings();

		if ( hud.Find( "openRide" ) is UiButton openButton )
		{
			// a visit reads the published state while drawing instead (the snapshot stays empty)
			infoNow = level.IsReadOnlyVisit ? null : SelectedInfo;
			openButton.Enabled = !level.IsReadOnlyVisit && infoNow?.CanOpen == true && objectDoorAllowed;
		}
		if ( hud.Find( "deleteRide" ) is UiButton deleteButton )
			deleteButton.Enabled = !level.IsReadOnlyVisit && selectedObject is { IsDeleted: false } item && !item.Entry.IsFixedItem;
		if ( hud.Find( "rideView" ) is UiButton rideButton )
			rideButton.Enabled = IsRiding || (selectedObject is { IsDeleted: false } candidate && RideCameraMode.CanRide( candidate ));

		UpdateCoasterPanelElements();
		UpdateShopPriceButton( mouseCaptured, keyboardCaptured, textCaptured );
		UpdateWaveSettingsButton();
		UpdateRideUpgradeButton( input );
		UpdateCallStaffButton();
		UpdateEconomyElements();
		UpdateCameraPanel( context.Delta );
		UpdateMessageTube( context.Delta );
		// the research window's loops end with the window, however it left the stack
		if ( researchHum != null && !ResearchWindowOpen )
			StopResearchSounds();
		UpdateLocatorPanels();
		SampleHappiness( context.Delta );
		var paused = Paused;
		var overUi = Stack.Covers( context.Canvas, input.Mouse ) || Stack.Screens.Count > 1;
		// Enter over the park goes to a visit's chat entry before the screen sees the key, which would otherwise press the button the pointer last rested on
		var chatEntry = ChatEntryWanted( context, input );
		var consumed = Stack.Update( context, chatEntry ? input with { Pressed = input.Pressed & ~UiKeys.Accept } : input );
		if ( chatEntry )
		{
			FocusChatEntry();
			overUi = consumed = true;
		}
		// [BIN:TPW-EXE:0x488aa1 RMBCancelOn] the original's screens read the option (+0x39) before a right click leaves them; the object window leaves on it too
		if ( InfoArmOpen && Stack.Screens.Count == 1 && !IsRiding && input.RightPressed && GameOptions.Current.RmbCancel && !Input.TextEntryActive )
		{
			SetInfoArm( false );
			consumed = true;
		}
		ReportScreens();
		ReportInterfaceMode();
		if ( Camera.Mode is ParkCameraMode camera )
		{
			camera.UiCapturesKeyboard = Input.TextEntryActive || Stack.Screens.Count > 1;
			// the chat entry over the park keeps only the keys: over the park the wheel, the edges and the drag still move the camera
			camera.TextEntryKeepsPointer = ChatEntryFocused;
			// [EXT:online-visit-chat-mode] the chat overlay's panels lie along the screen's left and bottom edges, where the HUD leaves the edge band free:
			// pushing the pointer against those edges still scrolls the camera
			camera.EdgeScrollOverUi = ChatMode && Stack.Screens.Count == 1;
			camera.ModalOpen = Stack.Screens.Any( screen => screen != hud && screen.Modal );
		}
		// [BIN:TPW-EXE:0x6698e6 key delivery] a key event (0x1000a/b/c, posted to window 1 by 0x658c38/0x658c67/0x658c96) is handed to exactly one window: an accelerator-table window (0x65e640, list and edit controls) when the key is in its table, else the focus window (0x65e59b, [0xfaa5ec]) while its flag bit 0 is set, else the root ([0xfaa5dc]); nothing falls through to a second window
		// [BIN:TPW-EXE:0x48ac40 ride view creation] the ride view window (flags 1) takes the focus (0x65e59b) and the HUD's own window handle [0x7c24c8] gets message 5 and is cleared, so the HUD's key groups (0x787c14, 0x787a60, 0x78721c at 0x488886) are never reached while it is open: the procedure 0x488a00 acts only on Escape, the C shortcut (action 0x10) and the postcard action (0xf), and the pause key and the park door shortcuts are not offered while riding
		if ( !paused && !IsRiding && !Input.TextEntryActive && input.Has( UiKeys.Pause ) )
			ToggleUserPause();

		if ( mouseCaptured == null || keyboardCaptured == null || textCaptured == null )
		{
			var io = ImGuiNET.ImGui.GetIO();
			mouseCaptured ??= io.WantCaptureMouse;
			keyboardCaptured ??= io.WantCaptureKeyboard;
			textCaptured ??= io.WantTextInput;
		}
		// the ride view's own screen is not an overlay of the park: its exit keys stay live
		var parkShortcuts = ParkShortcutsAllowed( paused, Stack.Screens.Count > (rideScreen != null ? 2 : 1), Input.TextEntryActive, keyboardCaptured.Value || textCaptured.Value );
		// [BIN:TPW-EXE:0x4862A0 chat mode] the HUD's key groups go with the HUD: the chat mode has none of its windows or tools
		var shortcutsAllowed = parkShortcuts && !ChatMode;

		var camcorderOwnsInput = UpdateCamcorder( input, new NVector2( context.Canvas.Width, context.Canvas.Height ),
			!shortcutsAllowed || overUi || consumed || mouseCaptured.Value || input.Text.Length > 0 || ridingAtStart ) || walkingAtStart;

		// Ride It (E) rides the selected ride; Escape, the C key, the eject button and a right click (RMB cancel on, after two seconds) leave the ride view, and E again does too.
		// [EXT:ride-view-key] E rides the selected ride as the object window's b_ride it! button does
		// [BIN:TPW-EXE:0x0040C5E0 Clear key] the key builds the area clear tool 0x46C580(0x3A)
		if ( shortcutsAllowed && !IsRiding && Input.Pressed( InputButton.Clear ) && level.AreaClearTool != null && !level.IsReadOnlyVisit )
			BeginAreaClear();
		if ( shortcutsAllowed && !IsRiding && Input.Pressed( InputButton.RideView ) && selectedObject is { IsDeleted: false } rideObject )
			level.EnterRideView( rideObject );
		// only a ride view that was already open at the start of the frame can be left by a key: the E that boarded it must not also leave it; a ride boarded
		// from the chat mode, which stays under the ride view's screen, is left by the same keys and the right click (0x488A00)
		if ( ridingAtStart )
			UpdateRideExits( input, parkShortcuts, mouseCaptured.Value );

		// The park door's keyboard shortcuts, named by the door button's help rows (UIHELPTEXT 193/194: Ctrl-O/Ctrl-C).
		// [DATA:UIHELPTEXT 193 "Left-click to open the park (Ctrl-O)", 194 "Left-click to close the park (Ctrl-C)"]
		// [EXT:maexah/OpenTPW park-engine.md] the original's shortcut table row 9 maps Ctrl-C to the close-park handler (PC 0x0040c410)
		if ( shortcutsAllowed && !IsRiding && FinanceEconomy is { } doorEconomy && !level.IsReadOnlyVisit )
		{
			// [BIN:TPW-EXE:0x0040C410 close-park handler] the shortcut calls the door routine 0x519EF0 whatever the park's state, which posts event 4 (0x51A1C6, also from a closed park) or, for opening, event 3 (0x51A033) every time
			if ( Input.Pressed( InputButton.OpenPark ) )
			{
				if ( !doorEconomy.IsParkOpen )
					level.Commands.Issue( new SetParkOpenCommand( true ) );
				host?.ParkDoorPressed?.Invoke( true );
			}
			if ( Input.Pressed( InputButton.ClosePark ) )
			{
				if ( doorEconomy.IsParkOpen )
					level.Commands.Issue( new SetParkOpenCommand( false ) );
				host?.ParkDoorPressed?.Invoke( false );
			}
		}

		// The original window keys (the key table the InputButton defaults document; the research and map buttons'
		// own help rows name "(R)" and "(Space)"), the time keys and the blueprint-turn keys.
		HandleScreenShortcuts( shortcutsAllowed && !camcorderOwnsInput );
		// the original's cheat keys, consulted after the shortcuts (docs/CHEATS.md), and the short note of every cheat the park applied
		HandleCheatKeys( shortcutsAllowed && !camcorderOwnsInput && !IsRiding );
		HandleCheatsWindowKey( shortcutsAllowed && !camcorderOwnsInput && !IsRiding );
		PostCheatNotes();
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

		// [EXT:online-coop-lockstep] a guest's click in a shared park: only the host builds
		if ( level.SessionRefusals != lastSessionRefusals )
		{
			lastSessionRefusals = level.SessionRefusals;
			PostMessage( strings.Extra( OpenTpwText.OnlyTheHostBuilds ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
		}
		if ( level.LastActionMessage != lastLevelMessage )
		{
			lastLevelMessage = level.LastActionMessage;
			if ( lastLevelMessage.StartsWith( "Cannot build", StringComparison.Ordinal ) )
			{
				// [BIN:STP-PPC:0x10139A40 error feedback] a rejected action plays cat_ui event 0x1D (called from the buy window at 0x10163AFC/0x10163BFC and the checkbox dialog at 0x1013B7A4)
				// [EXT:cannot-build-sound] a refused placement ('Cannot build') plays the error feedback 0x1D with OpenTPW's own message text: tp.exe plays 0x1D at exactly two sites (0x487b51 in the park click handler 0x4879d0, a cost above the money, and the helper 0x486ae0, whose only caller is the hire window 0x4af480), so the original plays nothing for a refused placement; the feedback is an OpenTPW addition
				// [BIN:TPW-EXE:0x535670 refusal stage] [BIN:TPW-EXE:0x536100 refusal stage] the build tools' code (0x524960-0x535100) plays only 0xAF, 0x65, 0xCE, 0x8B, 0x97-0x99 and 0x90, and no call site of the sound entry 0x51bfc0 lies in the refusal stages or the callees reached from them, so the original plays nothing for a refused placement
				// [EXT:pools-changing-room] a changing room that is refused for want of a pool says so
				// [EXT:pools-slides] a slide tower refused for want of a pool says so in its own words, turned away from the pool beside it in others
				PostMessage( strings.Extra( lastLevelMessage.EndsWith( $": {nameof( OriginalPlacementResult.NeedsPool )}.", StringComparison.Ordinal )
					? (pendingItem?.Entry is { } refused && PoolSlideTowers.IsSlideTower( refused ) ? (level.LastRefusalTowerFacesAway ? OpenTpwText.PoolSlideTowerFacesAway : OpenTpwText.PoolSlideTowerNeedsPool) : OpenTpwText.PoolChangingRoomNeedsPool)
					: lastLevelMessage.EndsWith( $": {nameof( OriginalPlacementResult.NeedsPoolWater )}.", StringComparison.Ordinal ) ? OpenTpwText.PoolAttractionNeedsPool
					: lastLevelMessage.EndsWith( $": {nameof( OriginalPlacementResult.NeedsPoolEdge )}.", StringComparison.Ordinal ) ? OpenTpwText.PoolExtraNeedsEdge : OpenTpwText.CannotBuildHere ) );
				GameAudio.PlayUi( GameAudio.ErrorEvent );
			}
		}

		// The park view's own click is not a button-class 0x100. The hook 0x485780 plays cat_ui 0xBD only when the receiver of 0x100 has style bit 0x10 (the choice at 0x4857cb); this click stays 0x1F.
		if ( !overUi && !consumed && input.LeftPressed )
			GameAudio.PlayUi( ParkViewClickEvent );

		// The ride view is a view of one ride: the park's clicks and tools wait until it is left.
		var rideOwnsInput = ridingAtStart || IsRiding || camcorderOwnsInput;
		var inPark = !overUi && !consumed && !rideOwnsInput && level.TryGetGridCell( input.Mouse, new NVector2( context.Canvas.Width, context.Canvas.Height ), out cursorX, out cursorY );
		if ( !inPark )
			cursorX = cursorY = -1;
		// Also consume the exit frame: leaving a ride must not activate an armed tool on that same click.
		var staffToolClick = !rideOwnsInput && (UpdateStaffTool( input, inPark && !mouseCaptured.Value, new NVector2( context.Canvas.Width, context.Canvas.Height ) ) || UpdatePatrolTool( input, inPark && !mouseCaptured.Value )
			|| UpdateBuyLandTool( input, inPark && !mouseCaptured.Value ) || UpdateAreaClearTool( input, inPark && !mouseCaptured.Value )
			|| UpdatePoolTool( context, input, inPark && !mouseCaptured.Value ));
		var visitorClick = TrySelectWorldVisitor( input, new NVector2( context.Canvas.Width, context.Canvas.Height ),
			paused || overUi || consumed || rideOwnsInput || ChatMode || mouseCaptured.Value || keyboardCaptured.Value || textCaptured.Value || staffToolClick || level.StaffPlacement is { IsActive: true } || level.PatrolTool is { IsActive: true }
			|| level.BuyLandTool is { IsActive: true } || level.AreaClearTool is { IsActive: true } || level.PoolTool is { IsActive: true } );
		var toolClick = staffToolClick || visitorClick || rideOwnsInput || UpdateCoasterTool( context, input, inPark, paused ) || UpdateCellTool( context, input, inPark, paused );
		// A right click leaves a build tool that Ctrl kept armed (and one still waiting for its first click).
		if ( inPark && !rideOwnsInput && !paused && input.RightPressed && GameOptions.Current.RmbCancel && level.BuildEntry != null && !toolClick )
		{
			LeaveBuildTool();
			toolClick = true;
		}
		UpdateBlueprint( inPark && !rideOwnsInput, context.Delta, paused || Status.Speed == GameSpeed.Paused );
		// [BIN:TPW-EXE:0x487013 hover modes] with Shift alone, or with the visitor locator panel out, the hover handler picks visitors only (IsVisitorPickState) and no object mode is set
		// [BIN:TPW-EXE:0x4879D0 object click] the object a park click opens is the owner of the hovered cell: the hover handler 0x486d90 sets cursor mode 4 for a hovered cell of map type 4, 9 or 10
		// with an owner thing (0x527e80; the cell-type switch 0x4875ae, mode 4 at 0x48769f) once no visitor or staff member is hovered, and the click dispatcher's mode 4 (switch 0x487a74) opens that thing's window (0x487c16: 0x486920 by its kind; the modes 8 to 0xc of a hovered staff member open 0x4b6080 instead, 0x487c77); the hovered
		// cell 0x7b05cc is the cell of the cursor ray's nearest hit (0x45bf90 → 0x45d560, Level.TryGetGroundHit), which lands on an object's model before the terrain behind it (ObjectRayPick), so a
		// click on the visible top of a tall ride, shop or feature picks that object; the pick sprites of the sprite pass (0x5890e0 → 0x532bd0) are the people's only: the owner search accepts
		// thing types 1, 4 to 8 and 0x12 (visitors and staff, 0x532d14-0x532d2c; the hovered-thing filter 0x46c110), and the sprite scripts' FRAME op 0x476940 marks its sprite pickable (+0x114 = 1 through 0x540b90) for any frame
		// but -1, so no ride or shop (thing type 3) is picked by a sprite
		if ( inPark && !IsVisitorPickState( Locator.Mode, Input.ModifierMaskDown ) && level.BuildEntry == null && !level.IsPlacing && !level.CellTool.IsActive && !level.IsCoasterToolActive && level.StaffPlacement is not { IsActive: true } && level.PatrolTool is not { IsActive: true }
			&& level.BuyLandTool is not { IsActive: true } && level.AreaClearTool is not { IsActive: true } && level.PoolTool is not { IsActive: true } && input.LeftReleased && !toolPress )
			{
				var clicked = level.Objects.FindAt( cursorX, cursorY );
				// [EXT:pools-waves] a deliberate second click on the wave machine that is already selected opens its settings window; selecting it (here or from the attractions list) changes nothing (ParkHud.Pools.cs)
				if ( !ChatMode && clicked != null && clicked == selectedObject && IsWaveMachine( clicked ) )
					OpenWaveSettings( clicked );
				// a click on a track add-on opens its ride's window (Level.WindowTarget); the chat mode has no object window
				if ( !ChatMode )
					SelectObject( level.WindowTarget( clicked ) );
				// [EXT:online-avatar-click] in a visit with other players a click on the ground walks the player's figure there (manual p. 68: "Left click to walk to destination"): in the
				// chat mode wherever it lands but on a rideable ride, which it rides (ChatModeRide), as the original's chat mode panel does (0x4BD4A0, help 401), and in the HUD where it
				// hits no object
				if ( ChatMode && !input.NotClick && ChatModeRide( clicked ) is { } ride )
					StartRide( ride );
				else if ( (clicked == null || ChatMode) && level.IsReadOnlyVisit && !input.NotClick && host?.Presence?.Invoke() is { Walks: true } presence
					&& level.TryGetGroundCell( input.Mouse, new NVector2( context.Canvas.Width, context.Canvas.Height ), out var groundX, out var groundY ) )
					presence.Click( groundX, groundY );
			}
		if ( input.LeftReleased )
		{
			toolPress = false;
			if ( refusalClick.Release() is { } refused )
				GameAudio.PlayPathRefused( refused.X, refused.Y );
		}

		for ( var index = messages.Count - 1; index >= 0; index-- )
		{
			messages[index] = (messages[index].Text, messages[index].Age + context.Delta);
			if ( messages[index].Age > MessageSeconds )
				messages.RemoveAt( index );
		}
		UpdateCursor( input, overUi || consumed, mouseCaptured == true );
		Status.Update( context.Delta );
		SyncRideScreen();
		PrepareRideStatic( context ); // observe the final camera once, after this frame's callbacks and shortcuts
		FinishChallengeMessageFrame();
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
	// [EXT:software-pointers] the original loads its cursors from Data/ui/cursors/*.ani with LoadCursorFromFileA (0x489639, IAT 0x6fd314) into a cursor object (0x661935): OS animated cursors drawn at the pointer hot spot (a software cursor path, "bwcursor", also exists); OpenTPW has no .ani loader (the platforms cannot load animated Windows cursors), so the tools' cursor 9 (c_carry.ani) is the closest cursor it has, Input.CursorTypes.Carry (Ccar.tga); the person in hand still hangs at the exact mouse ground point, where tp.exe's tool frame places it (0x46c480, GUEST-021)
	private bool UpdateStaffTool( UiInput input, bool inPark, NVector2 viewport )
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
		// the sprite hangs at the exact ground point under the pointer every frame, also while the pointer is over the HUD (the original reads the mouse's world point, not the hover cell);
		// a frame without a pointer (a key alone) keeps the last one
		// [BIN:TPW-EXE:0x45CD44 mouse point] the point is the picker's nearest hit, the terrain's or an object model's (0x45BF90 writes the same point to 0x7A1C90; Level.TryGetGroundHit)
		// [APPROX:GUEST-021] where the original's cursor and the person's sprite anchor land on the screen
		if ( input.Mouse.X >= 0 && input.Mouse.Y >= 0 )
		{
			if ( level.TryGetGroundCell( input.Mouse, viewport, out var groundX, out var groundY ) )
				tool.SetCursor( groundX, groundY );
			else
				tool.SetCursor( null, null );
			if ( inPark )
				tool.Hover( cursorX, cursorY );
			else
				tool.Hover( null, null );
		}
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
		// [BIN:TPW-EXE:0x0048C8D0 pause-menu click gate] while the pause menu is shown the park takes no tool clicks (the gate reads the pause-menu object 0x7c2534 +0xc; the park click handler 0x4879d0 returns before the tool dispatch) and the handler does not read the speed pause flag 0x786b84, so the speed pause keeps the tool live
		// [APPROX:PATH-017] the P key's window (the global 0x7cb2cc, 0x4a27f0 → 0x47ed80) puts a full-screen layer (0x65dd7b, the 'lolight' backdrop) under the panel 5094 of table 0x752e00; OpenTPW draws neither, and whether the layer stops the click route to 0x4879d0 is not read (the window manager's hit test of the layer classes)
		// The remove tool owns the park click while it is on (Level.RemoveAt clears paths and queues too).
		if ( paused || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects || level.IsCoasterToolActive
			|| level.PatrolTool is { IsActive: true } || level.StaffPlacement is { IsActive: true } || level.BuyLandTool is { IsActive: true }
			|| level.AreaClearTool is { IsActive: true } || level.PoolTool is { IsActive: true } )
			return false;
		// a visit builds no paths: while other players are shown its ground's help says that a click walks the player's figure there (UIHELPTEXT 401), not while the
		// chat entry takes the keys (the help box would cover it)
		// [BIN:TPW-EXE:0x4BD933 chat mode hover] the chat mode panel shows help 0x191 (401, "Left-click to walk to this point") over the park
		if ( inPark && level.IsReadOnlyVisit )
		{
			// [BIN:TPW-EXE:0x4BD7F4 chat mode ride help] over a rideable ride the chat mode panel shows help 0x244 (580, "<ride>: Left-click to ride") with the ride's name
			if ( ChatMode && !Input.TextEntryActive && ChatModeRide( level.Objects.FindAt( cursorX, cursorY ) ) is { } ride )
				context.HoverHelp = strings.HelpFormat( 580, EntryName( ride.Entry ) );
			else if ( host?.Presence?.Invoke() is { Walks: true } && !Input.TextEntryActive && (ChatMode || level.Objects.FindAt( cursorX, cursorY ) == null) )
				context.HoverHelp = strings.Help( 401 );
		}
		else if ( inPark && tool.HoverHelpId( level.Paths, cursorX, cursorY ) is int help )
			context.HoverHelp = strings.Help( help );
		if ( !tool.IsActive )
		{
			// Backspace over a path or queue cell with no tool on: the backtrack sound, then that cell is removed (GameAudio.BacktrackEvent; a queue is [EXT:queue-backtrack]).
			if ( inPark && input.Backspaces > 0 && Stack.Screens.Count == 1 && !Input.TextEntryActive && Camera.Mode is ParkCameraMode && level.Commands.Issue( new BacktrackCommand( cursorX, cursorY ) ).Accepted )
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
			if ( tool.HoverHelpId( level.Paths, cursorX, cursorY ) is not (441 or 442) || !level.EnterPathTool( cursorX, cursorY, out var refused ) )
				return false;
			// The entering click is the tool's first click: a refused cell ends the tool at once, and the refused click sounds at the release (PathRefusalClick).
			if ( refused != null )
			{
				refusalClick.Press();
				refusalClick.Refused( cursorX, cursorY );
				PostMessage( strings.Extra( refused.StoppedBy == CellBuildResult.NotEnoughMoney ? OpenTpwText.NotEnoughMoney : OpenTpwText.CannotBuildHere ) );
				return toolPress = true;
			}
			// [BIN:TPW-EXE:0x005271B7 lay click] every path or queue click past the refused-ghost test plays cat_ui 101 (0x65) before its start or line, the first click included
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
			ReportCellToolClick( false, CellToolMode.Path, tool, false );
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
		refusalClick.Press();
		var queueJoin = level.QueueJoinCell( tool );
		var hadStart = tool.Start != null;
		var modeBefore = tool.Mode;
		var interfaceBefore = InterfaceMode();
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
		// [BIN:TPW-EXE:0x526cb3 track click] every release in the kart (0xB) or water (0x10) track mode plays cat_ui 0xCE at the clicked cell, the first click included and whatever the line then does
		if ( modeBefore is CellToolMode.KartTrack or CellToolMode.WaterTrack )
			GameAudio.PlayUiAt( GameAudio.TrackToolClickEvent, GameAudio.CellSoundPosition( cursorX, cursorY ) );
		if ( result is not { GhostRefused: true } )
			ReportCellToolClick( hadStart, modeBefore, tool, queueJoin != null, interfaceBefore );
		ResolveQueueRearm( result );
		// [BIN:TPW-EXE:0x527587 queue-join sound] it follows every commit; a click on a refused ghost lays nothing and returns before the commit (0x524A63)
		if ( result is { GhostRefused: false } )
			level.PlayQueueJoin( queueJoin );
		// [BIN:STP-PPC:0x10070DE0 path lay click] a committed path line plays cat_ui 101 (the tool's commit site, gated by its mode); a click on a refused ghost (or a refused track commit) plays cat_ui 175 at the clicked cell when the button is released (PathRefusalClick)
		if ( result is { Completed: true } )
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
		if ( result is { Completed: false } )
		{
			// a commit stopped only by the back-of-queue walk's bound is not one of the original's refusals
			if ( !result.StoppedByOwnLimit )
				refusalClick.Refused( cursorX, cursorY );
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
	// The line preview and the footprint markers (every cell of the line, red from the refused one; m_front row, m_enter and m_exit arrows) are BlueprintGhosts; the PC evidence sits there (LayLine 0x536100, SetCell 0x532FC0, the placement 0x528A70)
	private void UpdateBlueprint( bool inPark, float delta, bool paused )
	{
		if ( level.Blueprint is not { } blueprint )
			return;
		var cells = new List<(int X, int Y, BlueprintMarker Marker)>();
		Dictionary<(int X, int Y), byte>? directions = null;
		if ( level.IsCoasterToolActive && level.CoasterPanel is { } coasterPanel )
		{
			// [BIN:TPW-EXE:0x5234d0 coaster tool markers] the build buttons mark the placement area (face 0x11f) and the cell under the pointer (face 0x115, red when the validator refuses it)
			foreach ( var (x, y) in coasterPanel.PlacementArea )
				cells.Add( (x, y, BlueprintMarker.Area) );
			if ( coasterPanel.IsBuilding && coasterPanel.Cursor is { } cursor )
				cells.Add( (cursor.X, cursor.Y, cursor.Result == CoasterPlaceResult.Ok ? BlueprintMarker.Allowed : BlueprintMarker.Refused) );
		}
		else if ( level.StaffPlacement is { IsActive: true, HoverValid: false, HoverCell: { } staffCell } )
			// [BIN:TPW-EXE:0x46c480 staff tool face] the staff tools' per-frame method 0x46c480 (slot +0x18 of the place tool 0x6fea40 and the carry tool 0x6fea70) queues a square dynamic face with 0x53c8d0 at (x - 5, 1.0, z - 5), face type 1, when one of the cell tests 0x5363f0, 0x535db0(0x40), 0x536450 holds or none of 0x536390, 0x536310, 0x536320 does (queued only when [0x763c98] is set)
			// [BIN:TPW-EXE:0x53d9e0 dynamic face texture] the renderer takes a face's batch from slot [+0x50] + 16 × type and the textures from the table 0x763b38 (16-byte records, loaded from Data\Generic\Dynamic\Textures by 0x53d790): type 0 blue.tga, 1 red.tga, 2 orange.tga, 3 m_enter ..., so type 1 is the blueprint's red face (BlueprintMarker.Refused)
			cells.Add( (staffCell.X, staffCell.Y, BlueprintMarker.Refused) );
		else if ( level.BuyLandTool is { IsActive: true } landTool )
		{
			// [APPROX:BLUEPRINT-003] the Buy Land ghost is the blueprint's cells along the rectangle's four edges (or the hovered post before the first click), blue while the validator allows the rectangle and red when it refuses it; the original draws a ghost fence model along the edges (0x53aac0 with the tool mode | 0x100) coloured by the refusal 0x816d50
			var marker = landTool.HoverRefused ? BlueprintMarker.Refused : BlueprintMarker.Allowed;
			foreach ( var (x, y) in landTool.PreviewEdge() )
				cells.Add( (x, y, marker) );
		}
		else if ( level.AreaClearTool is { IsActive: true } clearTool )
		{
			// [APPROX:BLUEPRINT-003] the area clear's ghost is the blueprint's cells along the rectangle's four edges (or the hovered cell before the first click), all blue: the preview lays the edges with LayLine(0x13a), whose validator answers 0xd and never refuses
			foreach ( var (x, y) in clearTool.PreviewEdge() )
				cells.Add( (x, y, BlueprintMarker.Allowed) );
		}
		else if ( level.PatrolTool is { IsActive: true } patrol )
		{
			// Approximation (UI-024, registered at ParkHud.Build): the patrol tool's rectangle is previewed as the blueprint's edge cells between the first corner and the hovered cell; the original's marking is not traced
			foreach ( var (x, y) in patrol.PreviewEdge() )
				cells.Add( (x, y, BlueprintMarker.Allowed) );
			if ( patrol.FirstCorner == null && patrol.HoverCell is { } corner )
				cells.Add( (corner.X, corner.Y, BlueprintMarker.Allowed) );
		}
		else if ( level.PoolTool is { IsActive: true, HoverCell: { } poolCell, HoverResult: { } poolResult } )
		{
			// [EXT:pools-tool] the pool tool marks the cell under the pointer: blue where it can be painted (or erased), red where it is refused
			cells.Add( (poolCell.X, poolCell.Y, poolResult is PoolBuildResult.Ok or PoolBuildResult.Existing or PoolBuildResult.RiverMarked ? BlueprintMarker.Allowed : BlueprintMarker.Refused) );
		}
		else if ( level.CellTool.IsActive && level.CellTool.Ghost is { } ghost )
		{
			cells.AddRange( BlueprintGhosts.LineCells( ghost.Line, ghost.StoppedAt ) );
		}
		else if ( inPark && level.BuildEntry is { } entry && cursorX >= 0 )
		{
			// a track add-on's ghost goes to its hover position on its ride's track, turned to the tile's flow, and keeps that angle (TrackAddOns.HoverTarget, 0x0052F1B0)
			var (targetX, targetY, rotation) = level.BuildTarget( entry, cursorX, cursorY );
			level.SetBuildRotation( rotation );
			var anchor = Level.GetCentredAnchor( entry, targetX, targetY, rotation );
			var placementResult = level.CheckBuildTarget( entry, targetX, targetY, rotation );
			var allowed = placementResult == OriginalPlacementResult.Allowed
				&& (level.Park == null || level.Park.Economy.CheckBuild( entry.InfoId, level.IsMoving ) == ParkEconomy.PurchaseResult.Ok);
			placementRefused = !allowed;
			var footprint = level.ObjectBlueprint( entry, anchor.X, anchor.Y, rotation, placementResult, allowed );
			cells.AddRange( footprint.Cells );
			directions = footprint.Directions;
		}
		if ( level.BuildEntry == null || !inPark )
			placementRefused = false;
		blueprint.Update( delta, paused );
		blueprint.Show( cells, directions );
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

	private void UpdateEconomyElements()
	{
		foreach ( var element in economyElements )
			element.Visible = Status.HasEconomy;
		// [BIN:TPW-EXE:0x004a1045 HUD counter rows] the HUD's message 0x1e handler 0x4a0fb0 shows a counter's row (0x34/0x36 keys, 0x35/0x37 tickets) only while its count is not zero
		foreach ( var id in TicketIds )
			if ( hud.Find( id ) is { } ticketElement ) ticketElement.Visible = Status.HasEconomy && Status.GoldenTickets > 0;
		foreach ( var id in KeyIds )
			if ( hud.Find( id ) is { } keyElement ) keyElement.Visible = Status.HasEconomy && Status.GoldenKeys > 0;
		if ( hud.Find( "cashChange" ) is { } text )
			text.Visible = BuildActionCost != null;
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
		DrawVisitNames( context );
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

	/// <summary>
	/// Textures the preview takes as run-time textures: the sign faces, and for an object outside the game data (a mod's, the pools extension's, bonus content)
	/// the textures of its own archive, decoded from its root as the park draws it (<see cref="OriginalObject.TryLoadTexture"/>); a name found in the game data
	/// is left to the path resolver.
	/// </summary>
	private static Func<string, UiTexture?>? RuntimeTexture( BuildItem item )
	{
		var sign = SignTexture( item );
		if ( item.Entry is not { IsExternal: true } entry )
			return sign;
		return name => sign?.Invoke( name )
			?? (ObjectAssets.ResolveTexture( entry, name ) is { } resolved && resolved.FileSystem != FileSystem && OriginalObject.TryLoadTexture( entry, name ) is { } texture
				? UiTexture.Of( texture.NativeTexture ) : null);
	}

	/// <summary>The item's preview model, read from the root its object comes from: the game data, or a mod or bonus root.</summary>
	private static ModelFile LoadPreviewModel( BuildItem item, string path ) =>
		item.Entry is { IsExternal: true } entry ? ObjectAssets.LoadModel( entry.FileSystem, path ) : ObjectAssets.LoadModel( path );

	internal PreviewIcon? GetIcon( BuildItem item )
	{
		if ( icons.TryGetValue( item.Id, out var icon ) )
			return icon;
		try
		{
			var clipMember = item.PreviewModel != null && item.Entry != null ? PreviewIcon.BuyClipMember( item.PreviewModel, item.Entry.Models ) : null;
			icon = item.PreviewModel == null ? null : new PreviewIcon( LoadPreviewModel( item, item.PreviewModel ),
				name => item.TextureDirectories.Select( directory => UiImages.Resolve( name, directory ) ).FirstOrDefault( path => path != null ),
				RuntimeTexture( item ), clipMember == null ? null : LoadPreviewModel( item, clipMember.Path ).Clip, clipMember?.Name );
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
