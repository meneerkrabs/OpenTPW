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
	/// <summary>
	/// [EXT:mods] Why the park cannot be published now (it records community mods; <see cref="ParkSharing.PublishRefusal"/>), or null: the pause menu's Publish Park line is then disabled, as the online menu's, and says why on hover.
	/// </summary>
	public Func<string?>? PublishRefusal { get; init; }
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
	/// <summary>[EXT:restored-advice-tutorial] Asks the advisor for a help message the restored advice extension adds (<see cref="RestoredAdvice"/>: 202, 306, 322); the advisor posts it only while the extension is on. Separate from <see cref="HelpPosted"/>, which carries the original's own posts.</summary>
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
	/// <summary>[EXT:online-coop-session] Opens the screen that shares the park with other players (the pause menu's Build Together); null hides the line.</summary>
	public Action<UiScreenStack>? BuildTogether { get; init; }
	/// <summary>[EXT:online-coop-session] The shared park's status line while the park is shared or being shared (and a while after its end); null for none.</summary>
	public Func<string?>? SessionStatus { get; init; }
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
	/// <summary>[EXT:online-coop-lockstep] The level's session refusals the HUD has said already (<see cref="Level.SessionRefusals"/>), and when it last said so.</summary>
	private int lastSessionRefusals;
	private float lastSessionRefusalNotice = float.NegativeInfinity;
	/// <summary>The least time between two notices of a session's refusal (the host has not let this player build, only the host does that): a drag that issues a command per cell says it once.</summary>
	internal const float SessionRefusalNoticeSeconds = 1.5f;
	private BuildItem? pendingItem;
	private UiLabel? moneyLabel;
	private ListSortMemory? fallbackSorts;
	/// <summary>The session's remembered list sorts (UI-084); a HUD built without a host keeps its own.</summary>
	private ListSortMemory Sorts => host?.ListSorts ?? (fallbackSorts ??= new());
	/// <summary>The client's selection: the level's own (<see cref="Level.Selection"/>), which placements and the removal of the object also write.</summary>
	private OriginalObject? selectedObject { get => level.Selection; set => level.Selection = value; }
	private readonly List<UiElement> economyElements = new();
	private int cursorX = -1;
	/// <summary>The last left press was taken by a tool or a pick, so its release is no click on the ground (the online walk).</summary>
	private bool pressConsumed;
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
		lastWaitingClicks = level.WaitingClicks;
		// the park view's tool clicks act on the left button's release, as the original's tool object does (Level.WorldClicksOnRelease)
		level.WorldClicksOnRelease = true;
		try
		{
			level.Objects.ObjectPlaced += OnObjectPlaced;
			// Harnesses that build a Level without its constructor have no tool object.
			if ( (CellBuildTool?)level.CellTool is { } cellTool )
				cellTool.Ended += OnCellToolEnded;
			level.BuildToolsEnded += OnBuildToolsEnded;
			// [EXT:online-coop-commands] a guest's patrol area goes through its management queue (C4c-2)
			guestPatrolArea = QueueGuestPatrolArea;
			level.GuestPatrolArea = guestPatrolArea;
			hud = new UiScreen( "hud" ) { Modal = false };
			Stack.Push( hud );
			Build();
			HookStaffStrikeMessages();
			HookChallengeMessages();
			HookLossEvents();
			// the park's save takes this HUD's message tube, and a resumed park's saved tube comes back (tp.exe saves its tag tube with the park, 0x48EE50)
			level.MessageTubeCaptured = CaptureMessageTube;
			var resumedTube = level.ResumedMessageTube;
			level.ResumedMessageTube = null;
			RestoreMessageTube( resumedTube );
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
		queueAfterPlacement = false;
	}

	/// <summary>Removes every handler the constructor puts on the level and its park (removing one that was not added is a no-op).</summary>
	/// <summary>This HUD's route of a guest's patrol area into its management queue, as the level holds it (<see cref="Level.GuestPatrolArea"/>).</summary>
	private Func<SetPatrolAreaCommand, bool>? guestPatrolArea;

	private void UnhookLevel()
	{
		if ( level.Objects is { } objects )
			objects.ObjectPlaced -= OnObjectPlaced;
		if ( (CellBuildTool?)level.CellTool is { } cellTool )
			cellTool.Ended -= OnCellToolEnded;
		level.BuildToolsEnded -= OnBuildToolsEnded;
		if ( level.MessageTubeCaptured?.Target == this )
			level.MessageTubeCaptured = null;
		if ( guestPatrolArea != null && ReferenceEquals( level.GuestPatrolArea, guestPatrolArea ) )
			level.GuestPatrolArea = null;
		if ( level.Park != null )
		{
			level.Park.EventRaised -= OnStaffStrikeEvent;
			level.Park.EventRaised -= OnLossEvent;
			level.Park.ChallengePresentationPosted -= OnChallengePresentation;
		}
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] Retires a HUD whose park lives on without it (a join's build makes the park's HUD again after a language change): its handlers leave the level and
	/// its park, and its screens close.
	/// </summary>
	internal void Retire()
	{
		UnhookLevel();
		Stack.Clear();
		level.LossOverlayActive = false;
	}

	public UiScreenStack Stack { get; } = new();
	public IHudParkStatus Status { get; }
	public IBuildCatalog Catalog { get; }
	public UiScreen Screen => hud;
	public bool InfoArmOpen { get; private set; }
	/// <summary>The cat_ui event of a click in the park view.</summary>
	public const uint ParkViewClickEvent = 0x1F;

	public OriginalObject? SelectedObject => selectedObject;
	/// <summary>True while a screen other than the HUD's own windows is open (the pause menu and its children, the map, a box, the research, finance and other windows); the buy, staff, attractions and visitors windows, the ride view, the finale and loss screens, a visit's chat mode and its name popups do not count. This gates the park's shortcuts and tools; the clock is <see cref="ClockPaused"/>.</summary>
	public bool Paused => Stack.Screens.Any( screen => screen != hud && screen != rideScreen && screen != finaleScreen && screen != lossScreen && screen != buyScreen && screen != staffScreen
		&& screen != attractionScreen && screen != visitorScreen && screen != chatModeScreen && screen.Name != OpenTPW.UI.ChatPanel.PopupScreenName );
	public IReadOnlyList<string> Messages => messages.Select( message => message.Text ).ToArray();
	/// <summary>The park simulation's time scale for the screens now open (see <see cref="TimeScaleFor"/>).</summary>
	public float SimulationTimeScale => TimeScaleFor( applicationPaused ? 0f : Status.TimeScale, Stack.Screens.Select( screen => screen.Name ), OnlineSession );

	/// <summary>
	/// A shared park or a read-only visit: its clock is the host's, so no box and no deactivation of one player may stop it. [EXT:online-coop-lockstep] tp.exe has no such case: the word
	/// [0xF82884] that 0x47F26C tests is non-zero only while the front end runs (its one write is at 0x54E66C in game state 1; state 3 clears it at 0x54E7E6 before a park is loaded), so in every
	/// park, logged in or not, the generic message boxes pause. The level's own time scale ignores every screen of a session as well (<c>GameFlow.SessionTimeScale</c>).
	/// </summary>
	private bool OnlineSession => level.IsReadOnlyVisit || level.CoopRole != CoopRole.None;

	/// <summary>
	/// Whether the game timer stands still, the clock the advisor's speech and the crowd's steps follow (the game pause object 0x786B68, flag +0x1C): the owners of the pause 0x4092A0 are
	/// the pause menu and its windows, the map window, the postcard, the offline message boxes, the application's deactivation, and the P key (0x409350 toggles the same flag and freezes
	/// the same timer, 0x402DD0). Other windows (research, finance, the object windows) leave it running.
	/// </summary>
	// [BIN:TPW-EXE:0x004092A0 game pause] 0x4092A0 sets the flag and freezes the timer object 0x785970 (0x402D90), 0x409300 clears it and thaws the timer (0x402DB0), 0x409350 (the P key) toggles both (0x402DD0)
	// [BIN:TPW-EXE:0x4092a0 pause sound side] 0x4092A0 also pauses the advisor's speech handle (0x598960 -> 0x51C060, with the advisor option on) and sets the sound entry's paused word (0x51C1C0 -> 0x803AD2), which lifts the listener 10000 units in 0x51C1D0; the application's deactivation pauses the whole mixer instead (0x51BCF0, 0x51BD30); GamePauseSound is that, and ObservePauseSound hands it the frame's change of owners
	public bool ClockPaused => applicationPaused || Status.Speed == GameSpeed.Paused || SimulationTimeScaleOfScreens == 0;

	private float SimulationTimeScaleOfScreens => TimeScaleFor( 1f, Stack.Screens.Select( screen => screen.Name ), OnlineSession );

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
	// [BIN:TPW-EXE:0x0048AAC2 winning message pause] the Instant Action completion message after the winning screen is a 0x47F020 message box too (its call at 0x48AAC2 and 0x48AC06), so winningMessage stops the park until it closes
	// [BIN:TPW-EXE:0x48cd10 delete confirmation] the delete confirmation of an object or staff member (the object windows 0x4af600, 0x4b0b30, 0x4b2150, the attractions window 0x497640 and the All Staff window 0x4b4fc0) is a generic message box 0x47f020 with UITEXT 0x18c and the b_exit and b_okay buttons when the Confirmations option is on, so it pauses (0x47f26c) and its close resumes (0x47eed0, message 0x14 → 0x409300); the postcard (Ctrl+P, 0x4a9380) pauses the same way: confirmDelete, confirmDeleteAttraction and the postcard screen stop the park here; the employee dismiss box (0x4b5e5d, UITEXT 0x18d) is a 0x47f020 caller too (its call at 0x4b5eac, yes callback 0x4b6070), so confirmDismissStaff stops it as well
	// [BIN:TPW-EXE:0x47f26c message box pause] the generic message box 0x47F020 pauses (0x4092A0(0, 0)) and clears the enable word only while that word is 1 and [0xF82884] is 0 (it is 0 in every park: only the front end sets it), so its boxes pause every park (a logged-in player's own park included) and, when
	// the pause menu, a window of it or the map holds the gate (the enable word is 0), add nothing: a close resumes (0x47EED0 message 0x14, 0x409300) only for the box that paused. A game pause is the set of open screens here, which is the same for nested boxes
	// [BIN:TPW-EXE:0x4aa480 research refusals] the research window's builder shows the boxes UITEXT 0x1D4 (Instant Action) and 0x1D3 (no scientist hired) instead of the window; they are 0x47F020 boxes too (0x4AA533, 0x4AA596)
	// [BIN:TPW-EXE:0x47f410 alert queue] the alert queue 0x47F620 (fed through 0x47F410 with ERRORMSG.str ids) shows each entry as a 0x47F020 box (0x47F76E, UITEXT-style one-button box, kind 2); an offline park builds one when the postcard composer 0x4A8C00 is asked for and the check 0x5B6460 on the online object [0xF7D88C] returns 0: ERRORMSG 0x4E is queued at 0x4A8C89 instead of the window (the postcard close 0x4A91D0-0x4A9265 reaches it)
	// [EXT:texture-pack] A first-run download may take a long time; its modal keeps the park turn paused.
	public static float TimeScaleFor( float speedScale, IEnumerable<string> openScreens, bool online = false ) =>
		openScreens.Any( name => name is PauseMenuScreen or MapScreenName or "texturePackDownload" or PostcardScreen || !online && IsPausingMessageBox( name ) ) ? 0 : speedScale;

	/// <summary>The screens that are generic message boxes (0x47F020) of a park: the delete and dismiss confirmations, the winning message, the research refusals and the alert queue's box.</summary>
	internal static bool IsPausingMessageBox( string name ) =>
		name is "confirmDelete" or "confirmDeleteAttraction" or "confirmDismissStaff" or WinningMessageScreen or ResearchRefusedScreen or AlertBoxScreen;

	/// <summary>The alert queue's box (0x47F620 -> 0x47F020): ERRORMSG.str entry 0x4E when the postcard composer cannot be opened.</summary>
	public const string AlertBoxScreen = "alertBox";

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
	internal string FormatMoney( long amount ) => string.Format( System.Globalization.CultureInfo.InvariantCulture, "{0}{1:#,0}", amount < 0 ? strings[UIStrings.NegativeDollar] : strings[UIStrings.Dollar], Math.Abs( (decimal)amount ) ).Replace( "  ", " " );

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
	// [BIN:TPW-EXE:0x4a0cdd HUD cash refresh] control 48 shows the number the active tool keeps in 0x81af4c (read by 0x52fbc0) and is hidden, cost -9999, or reset to the unset marker -999 when no tool is active: only the mode objects whose type (vtable +0x24) is 3, 4 or 8 (8 while 0x79c658 bit 0 is set, 0x447990) show it; type 3 is the tool-mode object 0x6fe9e0 that every build tool runs in; it is no sum of balance changes
	// [DATA:video 2:05, 74:00-76:00] "$ 2500" while the blueprint of a 2500 ride is held, "$ 150" in the path tool, "$ 10000" for the coaster station and "$ 70" to "$ 130" per track segment; absent outside the build tools even when the balance changes
	// [BIN:TPW-EXE:0x447990 coaster edit cost] the cost line follows the one cursor mode object of tp.exe ([0x7b05d8], replaced by 0x46c350, no stack): the tool-mode object of type 3 runs every build tool, the Buy Land tool included (its price shows; the Clear Area and patrol tools set the hidden marker -9999), and no class returns type 4; the coaster panel's edit modes (type 8) show it only while bit 0 of 0x79c658 is set, and the only writer that sets that bit is the height press (0x441a5d; the rotate press sets bit 1 at 0x441edd, the tilt press bit 2 and 0x2000 at 0x44205b, the stack handlers their own bits), so it is exactly an active height drag; a button selected during a drag runs the sub-mode's second action through the switch of 0x446fa0's deselect path (0x44700e, 0x447032 for the height drag, 0x442220 for the build and loop buttons), which clears the bit and settles the money as the release does (CoasterToolPanel.Select)
	public long? BuildActionCost
	{
		get
		{
			if ( !Status.HasEconomy )
				return null;
			// [BIN:TPW-EXE:0x4ac855 land tool] the buy window builds the Buy Land tool (0x39) as the type-3 tool-mode object (0x46c580), so the HUD refresh shows the tool's cost 0x81af4c for it as for every build tool: the land's price while a rectangle is hovered (0x532303), else 0 (the tool start 0x52f050 clears it, 0x52f087)
			// [BIN:TPW-EXE:0x523c08 clear tool] the Clear Area tool (0x3a, 0x4ac907) is a type-3 mode too but its hover sets the cost to -9999 (0x523c18), which the refresh hides (0x4a0d68), as the patrol tool's (0x36, 0x523d68)
			if ( level.BuyLandTool is { IsActive: true } landTool )
				return landTool.HoverCost;
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

	/// <summary>How many feedback lines the HUD keeps: a fourth message pushes the oldest out.</summary>
	internal const int MaximumFeedbackLines = 3;

	public void PostMessage( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return;
		messages.Add( (text, 0) );
		if ( messages.Count > MaximumFeedbackLines )
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
		// [BIN:TPW-EXE:0x005AF5D0 ticket count] [EXT:sandbox] [EXT:online-coop-snapshot-join] (a park outside the player's progress counts its own tickets: the generic sandbox has no player file to attach and a co-op park decides by its own records so every peer decides alike, situations that exist only in OpenTPW; UI-051 converted 2026-10-30) the ticket count is the player's earned tickets less the spent count (the HUD handler 0x4a0fb0 at 0x4a10cd; the four global flags once, the six local bytes of every park, the two secret flags) while the park is part of its player's progress (ParkEconomy.GoldenTicketsAvailable reads the attached ledger), otherwise the park's own unspent tickets
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
				// [EXT:online-coop-speed] a guest's buttons draw subdued and still take the click, which says the speed is the host's (C4a)
				Subdued = () => SpeedIsTheHosts,
				Unclipped = true,
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
		hud.DrawOverlay = context => { DrawFeedback( context ); DrawSessionStatus( context ); DrawRideStatic( context ); };
		hud.DrawBackground = context =>
		{
			DrawMessagePanel( context );
		};
		BuildCoasterPanel();
		hud.Updating = FitCashText;
		// [BIN:TPW-EXE:0x488921 key dispatch] a key goes to the camera, game and shortcut groups in turn (0x40C990, the first nonzero callback wins); Escape's game row (0x748028, 0x40C180) ends any mode object other than the default park mode (types 0 and 1): it installs the default mode and SetMode(0) 0x52F200 without writing and consumes the key, so only the next Escape reaches the shortcut row "menu" (0x748378, 0x40C4D0), which opens the pause menu (0x4816D0); the path and queue tools run in a type-3 mode object (vtable 0x6FE9E0)
		hud.Back = () =>
		{
			// the P window holds the focus: Escape reaches only its procedure, which ignores it
			if ( PauseBackdropHoldsFocus )
				return;
			// [EXT:online-coop-commands] a guest's hire or pick-up that waits for the host with no staff tool on yet is let go as its tool would be, before anything else takes the key (C4c-2):
			// only the next Escape closes a window or opens the pause menu
			if ( level.StaffPlacement is { } waitingStaffTool && (frameStaffStartWithoutTool || GuestStaffStartWaits && !waitingStaffTool.IsActive) )
				waitingStaffTool.Cancel();
			else if ( IsRiding )
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
			// [EXT:online-coop-commands] a guest's queue edit or Move pick-up that waits for the host is left as a tool is (C3b): only the next Escape opens the pause menu
			else if ( level.WaitingToolStart != null )
				level.DropToolStartWait();
			else
				OpenPauseMenu();
		};
	}


	private ObjectInfo? infoNow;
	/// <summary>The info arm's contents as computed once at the start of the frame (the draw lambdas read it).</summary>
	private ObjectInfo? InfoNow => infoNow ?? SelectedInfo;

	// [EXT:online-coop-commands] a guest's open word as drawn: projected while an open-state action of the object waits (C4b-2)
	public ObjectInfo? SelectedInfo => InfoArmOpen && selectedObject is { IsDeleted: false } item
		? new ObjectInfo( item.Entry.ObjectNameIndex ?? -1, DrawnIsOpen( item ), item.Runtime.IsAttraction, ObjectStats( item ), EntryName( item.Entry ) ) : null;

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
		ArmBuildTool( item, entry );
	}

	/// <summary>
	/// Ctrl and a left click on a placed object arms a copy of it: the build tool of the object's type, turned to the object's own angle, the same tool the buy window's row arms. A money balance
	/// below the price plays the error cue and arms nothing (the click then opens no window), and no window of the object opens.
	/// </summary>
	// [BIN:TPW-EXE:0x4879D0 object copy] the click dispatcher's mode 4 (the hovered cell's owner, 0x527e80) tests GetAsyncKeyState(VK_CONTROL) (0x487b0b: either Ctrl key): without it the object's window opens (0x486920), with it the type record of the thing's Info id (+0xE, 0x4D3D10) is read, the bank's balance (0x5195D0, +0xC) below the record's price (+4) plays cat_ui 0x1D (0x487bf1) and returns, else the build tool object of mode 4 for that Info id (0x46C5A0(4, id), the call the buy row makes at 0x4AC6EF after the same comparison at 0x4AC6C7) becomes the tool (0x46C350) and the tool's angle takes the thing's +0x10 (0x52F1B0)
	// in a shared park the copy arms its tool as a buy row does and the rights (the host's build grant, the claims) are asked at the placement like for every placement, so a guest the host has not let build gets the refusal there (CoopBuildRights)
	private bool CopyObject( OriginalObject clicked )
	{
		if ( BuyItemOf( clicked.Entry ) is not { } item || clicked.Entry is not { } entry )
			return false;
		// a read-only visit builds nothing: the click opens the object's window as it does without Ctrl
		if ( level.IsReadOnlyVisit )
			return false;
		if ( Status.HasEconomy && (Status.PriceOf( item ) ?? item.Cost) > Status.Money )
		{
			PostMessage( strings.Help( 152 ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return true;
		}
		ArmBuildTool( item, entry );
		level.SetBuildRotation( clicked.Placement.Rotation );
		return true;
	}

	private void ArmBuildTool( BuildItem item, ObjectCatalogEntry entry )
	{
		// [BIN:TPW-EXE:0x0052F1B0 angle setter] the angle word 0x81D7A4 changes only through its setter 0x52F1B0 (callers: the rotate key 0x40C133, the replayed angle 0x404DFF, the object copy 0x487BF4, the Move pick-up 0x48D12F, the add-on hover 0x5237F7, the placement tail 0x5259FE and 0x525E6C) and where the tool mode becomes 0 (0x52F3B6); the buy click (0x4AC683) builds the tool object (0x46C5A0) and calls none of them, so a tool armed during the queue tool of a placement starts at the angle that placement kept
		queueAfterPlacement = false;
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

	// [BIN:TPW-EXE:0x5253b8 placement dispatcher] a placement without an entrance keeps the build tool armed when the click held exactly Ctrl (modifier mask 0xc), else the tool returns to mode 0 (0x52f580); a ride with an entrance (0x52fab0, the type's +0x40) continues into the queue tool (0x52f580(3, 0): 0x52529e, the Move tool's 0x5258e1; a kart or water ride once its circuit closes while the placement's queue start is pending, 0x526f95 and 0x527004), whose last click ends the tool whatever the modifier (0x5275f2). Level.PlaceObject owns purchase/guest linkage and its removal handler owns scrap credits
	// [APPROX:UI-031] the coaster tool ends the build tool at the placement whatever the modifier, where the original's coaster placement goes on into the queue tool (0x52519f → 0x525259) and its track tool (mode 0x15) is not entered in 0x524960; an entrance-bearing placement keeps the angle through the queue tool whatever the modifier, where the original keeps it only for exactly Ctrl (0x5259f4, else 0x52f1b0(0, 0) at 0x5259fe), which shows only in a tool armed while the queue tool runs (OpenTPW starts it at 0); and a queue whose start cell is a path (the cell type +0x29c = 1, 0x5259cf) ends the tool at once in the original — evidence needed: where the original's coaster placement enters its track tool, which of OpenTPW's placements have a path cell as the queue's start, and whether a tool armed from the queue tool keeps the angle
	private void OnObjectPlaced( OriginalObject item )
	{
		// [EXT:online-coop-commands] a placement inside a command: the HUD follows its own player's placement once the command has returned, so the tool the placed ride opens issues commands of its own;
		// a scheduled placement (a replay, a peer's) moves no tool here
		var keepTool = level.KeepBuildToolOnPlace;
		if ( level.Commands.IsExecuting )
		{
			if ( level.Commands.IsIssuing )
				level.Commands.AfterIssue( () => FinishPlacement( item, keepTool ) );
			return;
		}
		FinishPlacement( item, keepTool );
	}

	/// <summary>
	/// Finishes the build tool after its placement of <paramref name="item"/>: the Built message, then the tool kept for the next placement (<paramref name="keepTool"/>: exactly Ctrl at
	/// the click) or the tool the placed object opens. [EXT:online-coop-commands] A guest's placement finishes when its request comes back, with the Ctrl of its click (C3a-2); one that
	/// a later record <paramref name="removed"/> again opens no tool, also when it was placed in the park a resync replaced, whose catalogue held entries of its own (C3b-2).
	/// </summary>
	private void FinishPlacement( OriginalObject item, bool keepTool, bool removed = false )
	{
		if ( pendingItem?.Entry is not { } armed || ParkCommandCodec.EntryKey( armed ) != ParkCommandCodec.EntryKey( item.Entry ) )
			return;
		// Level.PlaceObject owns the purchase and guest link; this callback only finishes the HUD tool.
		PostMessage( string.Format( strings.Extra( OpenTpwText.Built ), ItemName( pendingItem ) ) );
		removed |= item.IsDeleted;
		var queueFollows = !removed && item.Entry.HasQueue && item.Runtime.IsAttraction && item.Visitors.HasCells;
		if ( keepTool && item.Runtime.Coaster == null && item.Runtime.Bump == null && !queueFollows )
		{
			// Ctrl at the placing click: the tool stays armed with its item and angle for the next placement.
			return;
		}
		pendingItem = null;
		level.IsPlacing = false;
		level.BuildEntry = null;
		// [EXT:online-coop-commands] a guest's placement that a later record removed before its outcome came back opens no tool (C3a-2)
		if ( removed )
		{
			level.ResetBuildRotation();
			return;
		}
		// a placed coaster enters its tool; [EXT:online-coop-claims] a guest's tool starts once its claim and its entry are back, with the tool's help then (C3c-2), and no queue tool follows the
		// placement meanwhile, nor after a refusal (another player's claim, the guest's coaster commands still to send)
		if ( item.Runtime.Coaster != null && level.BeginCoasterToolEntry( item ) is var coaster and not TrackToolEntry.None )
		{
			level.ResetBuildRotation();
			if ( coaster == TrackToolEntry.Entered )
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
		// a placed kart or water ride enters its track tool (tool mode 11 or 16) at its station with its queue start pending; the queue tool follows once the circuit is valid (Level.ContinueTrackTool);
		// [EXT:online-coop-claims] a guest's tool starts once its claim is back, with the tool's help then (C3c-1b), and no queue tool follows the placement meanwhile, nor after another
		// player's claim refused the tool: that ride's queue is that player's while its track tool is on it
		if ( item.Runtime.Bump != null && level.EnterPlacedTrackTool( item ) is var track and not TrackToolEntry.None )
		{
			level.ResetBuildRotation();
			if ( track == TrackToolEntry.Entered )
				PostMessage( TrackToolHelp );
			return;
		}
		// [BIN:STP-PPC:0x1007497C ride placement] a placed HasQueue ride (type record +64) enters tool mode 3, the queue tool (PATH-plan §8); park clicks lay its queue until Back
		if ( queueFollows && level.EnterQueueTool( item.Visitors ) )
		{
			// The build tool (and a move with it) ended above whatever the click held: the queue tool ends in mode 0 (OnCellToolEnded), and the angle stays until then.
			queueAfterPlacement = true;
			PostMessage( strings.Extra( OpenTpwText.LayQueue ) );
			return;
		}
		level.ResetBuildRotation();
	}

	/// <summary>Whether the queue tool on is the one a placed ride opened (<see cref="FinishPlacement"/>): its end resets the build angle the placement kept.</summary>
	private bool queueAfterPlacement;

	/// <summary>
	/// The path/queue tool turned off. The queue tool a placed ride opened ends in mode 0 however it ends (its last click, Escape, a right click): neither the build tool nor the Move
	/// tool is armed again, and the build angle returns to 0.
	/// </summary>
	// [BIN:TPW-EXE:0x5275f2 queue end] the queue tool's last click (a click on the line's start, or a line whose commit ends on a path or queue cell, 0x536100(0x81) and 0x527517; both reach 0x5275b1) runs 0x52f200(0, 0) whatever the modifier, which the queue tool's click (0x5271b7..0x527669) never reads: the mode is 0, the angle 0 (0x52f3b6) and the build tool object, with its mode 4 or 0x3b and its type (0x46c5a0), is replaced by the default tool (0x46c6a0 installed by 0x46c350, 0x52f46c), so a click holding exactly Ctrl arms neither the build tool nor the Move tool again
	private void OnCellToolEnded()
	{
		if ( !queueAfterPlacement )
			return;
		queueAfterPlacement = false;
		level.ResetBuildRotation();
	}

	/// <summary>
	/// Ends every armed tool, as their own Escape ends them: the build and Move tools, the remove tool, the coaster, staff, patrol, land, clearing and pool tools and the
	/// path/queue tool (the winning screen, and [EXT:online-coop-presence] a shared park's chat mode, which builds nothing).
	/// </summary>
	private void RetireArmedTools()
	{
		// [EXT:online-coop-commands] a guest's queue edit or Move pick-up that waits for the host starts no tool after this (C3b)
		level.DropToolStartWait();
		LeaveBuildTool();
		level.IsPlacing = level.IsRemovingObjects = false;
		if ( level.IsCoasterToolActive ) level.ExitCoasterTool();
		level.StaffPlacement?.Cancel();
		level.PatrolTool?.Cancel();
		level.BuyLandTool?.Cancel();
		level.AreaClearTool?.Cancel();
		level.PoolTool?.Cancel();
		level.CellTool.Cancel();
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
		// [EXT:online-coop-commands] a guest's door is an open-state action of its object, from the drawn state (C4b-2)
		if ( GuestManagement is { } queue )
		{
			RequestAttractionDoor( queue, item );
			return;
		}
		// the command asks the reopening guard before it opens (SetAttractionOpenCommand)
		level.Commands.Issue( new SetAttractionOpenCommand( item.Runtime.AttractionId, !item.IsOpen ) );
	}

	/// <summary>
	/// Sells the placed object with attraction id <paramref name="attraction"/> (<see cref="RemoveAttractionCommand"/>): [EXT:online-coop-commands] a guest's sale goes to the host, and the sell
	/// buttons take no second sale of the object until its outcome comes back (C3b).
	/// </summary>
	private void SellAttraction( int attraction )
	{
		// the sell button's select (0x48CD95) writes the track tool's globals, which the command then carries
		level.NoteToolSelection( level.ResolveCommandObject( attraction ) );
		if ( level.Commands.Issue( new RemoveAttractionCommand( attraction ) ).Request is { } request )
			level.WaitSale( attraction, request );
	}

	private void DeleteRide()
	{
		if ( RejectReadOnlyAction() )
			return;
		// [EXT:online-coop-commands] a guest's sale of the object that waits for the host takes no second one (C3b), and [EXT:online-coop-claims] a ride claimed against this player is not its
		// to sell (C3c-1)
		if ( selectedObject is not { IsDeleted: false } item || item.Entry.IsFixedItem || level.IsSaleWaiting( item.Runtime.AttractionId ) || ClaimedAgainstThisPlayer( item ) )
			return;
		void Remove()
		{
			// Catalogue objects sell and unlink exactly once; the developer prototype removes through its own seam ([EXT:online-coop-commands]: one command either way).
			SellAttraction( item.Runtime.AttractionId );
			selectedObject = null;
			SetInfoArm( false );
		}
		if ( GameOptions.Current.Confirmations )
			Stack.Push( UiDialogs.MessageOfOriginalText( "confirmDelete", () => strings[UIStrings.DeleteItem],
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
			(UIStrings.RestartPark, () => Stack.Push( UiDialogs.MessageOfOriginalText( "confirmRestart", () => strings[UIStrings.ConfirmRestartPark],
				(() => strings.Value( UIStrings.Yes ), () => { Stack.Pop(); host.RestartPark?.Invoke(); }), (() => strings.Value( UIStrings.No ), Stack.Pop) ) )),
			(UIStrings.PublishPark, () => host.PublishPark?.Invoke( Stack )),
			(UIStrings.Options, () => { if ( host.CreateOptions != null ) Stack.Push( host.CreateOptions( Stack, () => { } ) ); }),
			(UIStrings.GoOnline, () => host.GoOnline?.Invoke( Stack )),
			(UIStrings.ResumeGame, ResumeGameAfterApplicationPause),
			(UIStrings.ExitToLobby, () => { if ( menu is { IsRemoved: false } && ReferenceEquals( Stack.Top, menu ) ) ExitToLobbyFromMenu(); }),
			(UIStrings.QuitGame, () => Stack.Push( UiDialogs.MessageOfOriginalText( "confirmQuit", () => strings[UIStrings.ConfirmQuit],
				(() => strings.Value( UIStrings.Yes ), host.Quit), (() => strings.Value( UIStrings.No ), Stack.Pop) ) )),
		};
		// [BIN:TPW-EXE:0x48c150 pause menu selection] game type 1 (0x48c1e3) selects the three online entries before the
		// world-state-4 test (0x48c27a). Otherwise a lost park gets Load, Restart, Resume, Exit To Lobby and Quit, in that order.
		// The loss Load row uses UITEXT 3 and command 22, not text 22. Its 0x49efb0 argument is unused; the extra 0x5da3c0
		// calls on loss Restart/Exit/Quit return immediately, so the existing callbacks and confirmations are shared (0x48b6a0).
		entries.RemoveAll( entry => !PauseMenuOffers( level.IsReadOnlyVisit, level.IsCoopGuestPark, level.Park?.Economy.IsBankrupt == true, entry.Label ) );
		if ( host.GoOnline == null )
			entries.RemoveAll( entry => entry.Label == UIStrings.GoOnline );
		var items = entries.Select( entry => (Id: entry.Label.ToString(), Text: (Func<string>)(() => strings[entry.Label]), entry.Clicked,
			Enabled: PauseMenuEntryEnabled( level.IsReadOnlyVisit, entry.Label, host.CreateLoad != null, host.CreateOptions != null, host.RestartPark != null, host.PublishPark != null && host.PublishRefusal?.Invoke() == null )) ).ToList();
		// [EXT:online-coop-session] the Build Together line (C2d-2) is OpenTPW's, after Go Online: the player's own park shared with other players
		if ( host.BuildTogether is { } buildTogether && PauseMenuOffersBuildTogether( level.IsReadOnlyVisit, level.IsCoopGuestPark, level.Park?.Economy.IsBankrupt == true, level.CoopRole == CoopRole.Host ) )
		{
			var after = items.FindIndex( item => item.Id == nameof( UIStrings.GoOnline ) );
			if ( after < 0 )
				after = items.FindIndex( item => item.Id == nameof( UIStrings.Options ) );
			items.Insert( after + 1, (BuildTogetherId, () => strings.Extra( OpenTpwText.BuildTogether ), () => buildTogether( Stack ), true) );
		}
		menu = UiDialogs.TextMenu( PauseMenuScreen, items, ResumeGameAfterApplicationPause, UIStrings.ResumeGame.ToString(), compact: host.IsLoggedIn?.Invoke() == true );
		// [EXT:mods] a greyed Publish Park line says why on hover (community mods in the park), as the online menu's does
		if ( host.PublishPark != null && host.PublishRefusal is { } publishRefusal && menu.Find( nameof( UIStrings.PublishPark ) ) is { } publishLine )
		{
			publishLine.HelpWhenDisabled = true;
			publishLine.HelpSource = publishRefusal;
		}
		Stack.Push( menu );
	}

	public void ClosePauseMenu()
	{
		while ( Stack.Screens.Count > 1 )
			Stack.Pop();
	}

	/// <summary>
	/// Whether the pause menu lists <paramref name="label"/>: a read-only visit only Resume Game, Exit To Lobby and Quit Game; a lost park (bankrupt) Load, Restart Park, Resume Game,
	/// Exit To Lobby and Quit Game. [EXT:online-coop-lockstep] A co-op guest's park is the host's: it lists no Save, Restart Park or Publish Park (also after the session ended).
	/// </summary>
	internal static bool PauseMenuOffers( bool readOnlyVisit, bool coopGuestPark, bool bankrupt, UIStrings label ) =>
		readOnlyVisit ? label is UIStrings.ResumeGame or UIStrings.ExitToLobby or UIStrings.QuitGame
		: !(coopGuestPark && label is UIStrings.Save or UIStrings.RestartPark or UIStrings.PublishPark)
			&& (!bankrupt || label is UIStrings.Load or UIStrings.RestartPark or UIStrings.ResumeGame or UIStrings.ExitToLobby or UIStrings.QuitGame);

	/// <summary>[EXT:online-coop-session] The pause menu's Build Together line.</summary>
	internal const string BuildTogetherId = "buildTogether";

	/// <summary>
	/// [EXT:online-coop-session] Whether the pause menu lists Build Together: the player's own park (not a visit, not a guest's copy of a shared park), and a lost
	/// park only while it is shared (its players and its end stay reachable).
	/// </summary>
	/// <summary>The name of the question before a guest leaves its running shared park.</summary>
	internal const string ConfirmLeaveSharedParkScreen = "confirmLeaveSharedPark";

	/// <summary>
	/// [EXT:online-coop-session] The pause menu's Exit To Lobby: a guest in a running shared park is asked first, since the exit leaves the session and the park stays the host's
	/// (C2d-3); otherwise, and once the session ended, the exit goes at once.
	/// </summary>
	private void ExitToLobbyFromMenu()
	{
		if ( level.CoopSession is { Role: CoopRole.Guest, EndedWith: null } )
		{
			Stack.Push( UiDialogs.Message( ConfirmLeaveSharedParkScreen, () => strings.Extra( OpenTpwText.ConfirmLeaveSharedPark ),
				(() => strings.Value( UIStrings.Yes ), () => { Stack.Pop(); host.ExitToLobby(); }),
				(() => strings.Value( UIStrings.No ), () => Stack.Pop()) ) );
			return;
		}
		host.ExitToLobby();
	}

	internal static bool PauseMenuOffersBuildTogether( bool readOnlyVisit, bool coopGuestPark, bool bankrupt, bool hosting ) =>
		!readOnlyVisit && !coopGuestPark && (!bankrupt || hosting);

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
	public bool Update( UiContext context, UiInput input )
	{
		var captured = Update( context, input, null, null, null );
		// the frame's change of pause owners reaches the sound side once the frame has settled which screens are open (docs/AUDIO.md, "Pause sound")
		ObservePauseSound();
		return captured;
	}

	// Explicit captures run the identical frame body in asset-free tests. Null samples native IO at its original point.
	internal bool Update( UiContext context, UiInput input, bool? mouseCaptured, bool? keyboardCaptured, bool? textCaptured )
	{
		level.WorldPointerRouted = false;
		level.ParkOwnsRelease = false;
		frameHostStarts = level.HostToolStarts;
		// [EXT:online-coop-commands] staff presses and Escape belong to the state seen before this frame takes a start's or a click's outcome (C4c-2)
		RememberGuestStaffInput();
		// [EXT:online-coop-commands] whether the guest's coaster panel waited for the host before this frame's answers (C3c-2): a press first seen now was made while it waited
		frameCoasterWaiting = level.CoasterPanel?.IsWaiting == true;
		UpdatePausingBoxes();
		// [EXT:online-coop-commands] a guest's queue edit or Move pick-up that waits for the host starts no tool once any tool was on meanwhile (C3b): the HUD's own tools, the pools', the staff's
		if ( level.WaitingToolStart != null && !level.NoToolActive )
			level.DropToolStartWait();
		// [EXT:online-coop-commands] the buy item a pick-up arms the Move tool with is its wait's: a wait dropped (another tool, Escape, the session's end) takes it along (C3b-2)
		if ( waitingMove is { } move && !ReferenceEquals( level.WaitingToolStart, move.Request ) )
			waitingMove = null;
		// [EXT:online-coop-commands] a resync's park arms the Move tool again from its carry, waiting again for its placement, before the outcomes are taken, so that placement's finishes it
		// (C3b-2); a guest's requests resolved since the last frame complete their tools, outside any command, tick and stream application (C3a-2); then a Move tool whose carry the host
		// released disarms, and a carry left without its Move tool is ended (C3b-2)
		RestoreMove();
		CompleteGuestRequests();
		level.EndCarrylessMove();
		level.EndStrayCarry( Time.Now );
		// [EXT:online-coop-commands] the staff tool follows what the park holds for this player, every frame, paused too and whatever the pointer is over, before any pointer's dispatch (C4c); a
		// guest's follows its seat's state through its staff requests (C4c-2)
		if ( GuestStaff is { } guestStaff )
			FollowGuestStaff( guestStaff );
		else
			level.StaffPlacement?.FollowPark();
		// [EXT:online-coop-commands] the guest's coaster tool says what its waited actions came back with, before the claim is bound: an outcome that ended the tool says why itself (C3c-2)
		ReportGuestCoasterActions();
		// [EXT:online-coop-commands] one tool at a time: a coaster tool on together with a tool that started after it ends (C3c-2)
		level.RetireCoasterToolForOtherTool();
		// [EXT:online-coop-claims] and this machine's claim is bound to its track or coaster tool's ride (C3c-1, C3c-2)
		level.SettleClaim( Time.Now );
		// [EXT:online-coop-commands] a guest's coaster commands go first, then its pool strokes send what waits and say what finished (C3b-3, C3c-2)
		UpdateGuestCoasterTool();
		UpdateGuestPoolStrokes();
		SyncChallengeMessageSuspension();
		QueueChallengeTerminalTimers();
		if ( UpdateResearchFinale( context, input, mouseCaptured, keyboardCaptured, textCaptured ) )
		{
			// The full-screen owner consumes input, not the unscaled UI timer/delayed-release pumps.
			if ( !finaleObservedCamera )
				rideStatic.SkipFrame(); // the frame that retires the screen observes no camera and refreshes no timestamp
			// [EXT:online-coop-commands] the management windows' outcomes are taken under the research finale too (C4b)
			CompleteGuestManagement();
			FinishChallengeMessageFrame();
			return true;
		}
		if ( UpdateLossScene( context, input, mouseCaptured, keyboardCaptured, textCaptured ) )
		{
			if ( !lossObservedCamera )
				rideStatic.SkipFrame(); // the frame that retires the window observes no camera and refreshes no timestamp
			FinishChallengeMessageFrame();
			return true;
		}
		var pointerFrameInput = input;
		var orderedPointerFrame = (input.OrderedEvents != null || pointerRoutingStarted || pausePointerOverlay != null) && level.CoopRole == CoopRole.None && !level.IsReadOnlyVisit && !IsRiding && !CamcorderArmed && level.FreeWalk == null;
		if ( !orderedPointerFrame && pointerRoutingActive ) SuspendParkPointer();
		if ( orderedPointerFrame )
			input = input with { LeftPressed = false, LeftReleased = false, RightPressed = false, RightReleased = false, RightClicked = false, Pressed = pointerFrameInput.OrderedEvents != null ? input.Pressed & ~UiKeys.Pause : input.Pressed, PointerSuppressed = true };
		var ridingAtStart = IsRiding;
		SyncRideScreen();
		HandleChatModeKey();
		SyncChatMode();
		// [EXT:online-coop-presence] the chat mode has no tools: its park clicks never reach the level's tool layer (a shared park's chat mode builds nothing)
		if ( ChatMode )
			level.WorldPointerRouted = true;
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
			// [EXT:online-coop-commands] greyed while an open-state action of a guest's object waits (C4b-2)
			openButton.Enabled = !level.IsReadOnlyVisit && infoNow?.CanOpen == true && objectDoorAllowed && !(selectedObject is { } waiting && OpenStateWaits( waiting ));
		}
		if ( hud.Find( "deleteRide" ) is UiButton deleteButton )
			// [EXT:online-coop-commands] not while this guest's sale of the object waits for the host (C3b), nor [EXT:online-coop-claims] for a ride claimed against this player (C3c-1)
			deleteButton.Enabled = !level.IsReadOnlyVisit && selectedObject is { IsDeleted: false } item && !item.Entry.IsFixedItem && !level.IsSaleWaiting( item.Runtime.AttractionId )
				&& !ClaimedAgainstThisPlayer( item );
		if ( hud.Find( "rideView" ) is UiButton rideButton )
			rideButton.Enabled = IsRiding || (selectedObject is { IsDeleted: false } candidate && RideCameraMode.CanRide( candidate ));

		UpdateCoasterPanelElements( context.Delta );
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
		UpdateLocatorSlide( context.Delta );
		// [BIN:TPW-EXE:0x54e2b8 hover cell refresh] the park loop refreshes the hover cell from the pointer ray every frame while the camera is the overview (the crowd count reads it, GameFlow.GuestsNearCamera); the eye views leave it
		if ( Camera.Mode is ParkCameraMode && input.Mouse is { X: >= 0, Y: >= 0 } hoverPointer )
			level.RefreshHoverCell( hoverPointer, new NVector2( context.Canvas.Width, context.Canvas.Height ) );
		SampleHappiness( context.Delta );
		var admittedPointerEvents = orderedPointerFrame ? AdmitPointerEvents( context, pointerFrameInput ) : null;
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
		// [EXT:online-coop-commands] the management windows' outcomes, after the screens took this frame's input (C4b): a confirmation the player closed this frame already detached, so an
		// outcome that came this frame is its note, never a dialog its own Back would close unseen
		CompleteGuestManagement();
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
			camera.UiCapturesKeyboard = Input.TextEntryActive || Stack.Screens.Count > (LossOverlayOnTop ? 2 : 1);
			// the chat entry over the park keeps only the keys: over the park the wheel, the edges and the drag still move the camera
			camera.TextEntryKeepsPointer = ChatEntryFocused;
			// [EXT:online-visit-chat-mode] the chat overlay's panels lie along the screen's left and bottom edges, where the HUD leaves the edge band free:
			// pushing the pointer against those edges still scrolls the camera
			camera.EdgeScrollOverUi = ChatMode && Stack.Screens.Count == 1;
			camera.ModalOpen = Stack.Screens.Any( screen => screen != hud && screen.Modal ) || pausePointerOverlay != null;
		}
		// [BIN:TPW-EXE:0x6698e6 key delivery] a key event (0x1000a/b/c, posted to window 1 by 0x658c38/0x658c67/0x658c96) is handed to exactly one window: an accelerator-table window (0x65e640, list and edit controls) when the key is in its table, else the focus window (0x65e59b, [0xfaa5ec]) while its flag bit 0 is set, else the root ([0xfaa5dc]); nothing falls through to a second window
		// [BIN:TPW-EXE:0x48a1f5 ride view creation] the ride view window (flags 1, built at 0x48a1f5 and 0x48a4d4) takes the focus (0x65e59b) and the HUD's own window handle [0x7c24c8] gets message 5 and is cleared, so the HUD's key groups (0x787c14, 0x787a60, 0x78721c at 0x488886) are never reached while it is open: the procedure 0x488a00 acts only on Escape, the C shortcut (action 0x10) and the postcard action (0xf), and the pause key and the park door shortcuts are not offered while riding
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
		// [BIN:TPW-EXE:0x47ee67 P window focus] the P window takes the focus (0x47ee67 -> 0x65e59b), so a key reaches only its procedure 0x4a2680 (the P key and the panel's animation) while the backdrop is up: no HUD shortcut works under it
		var parkShortcuts = !PauseBackdropHoldsFocus && ParkShortcutsAllowed( paused, Stack.Screens.Count > (rideScreen != null ? 2 : 1), Input.TextEntryActive, keyboardCaptured.Value || textCaptured.Value );
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
			// [EXT:online-coop-commands] a guest's keys ask the host for the state they name, unless the door is drawn in it already, and the advisor's event follows the accepted request (C4b)
			if ( GuestManagement is { } doorQueue )
			{
				if ( Input.Pressed( InputButton.OpenPark ) )
					RequestParkDoor( doorQueue, true );
				if ( Input.Pressed( InputButton.ClosePark ) )
					RequestParkDoor( doorQueue, false );
			}
			else
			{
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

		// [EXT:online-coop-lockstep] a click the shared park's rights refuse (once for a drag's run of refusals): a guest's building the host has not let it do yet, or what only the host
		// does ([EXT:online-coop-permissions], C3a-2); [EXT:online-coop-claims] any player's edit of a ride another player claims (C3c-1)
		if ( level.SessionRefusals != lastSessionRefusals )
		{
			lastSessionRefusals = level.SessionRefusals;
			if ( Time.Now - lastSessionRefusalNotice >= SessionRefusalNoticeSeconds )
			{
				lastSessionRefusalNotice = Time.Now;
				PostMessage( strings.Extra( RefusalText( level.LastSessionRefusal ) ) );
				GameAudio.PlayUi( GameAudio.ErrorEvent );
			}
		}
		// [EXT:online-coop-commands] a click a tool did not take while its request waited for the host (C3a-2)
		SayWaitingClicks();
		// [EXT:online-coop-commands] another player's command's message is not this machine's to say, and a guest hears its own commands' outcomes from its requests (C3a-2); the HUD
		// remembers only a message it may say, so its own refusal right after another player's identical one is still said
		if ( level.LastActionMessage != lastLevelMessage && level.SaysLastActionMessage )
		{
			lastLevelMessage = level.LastActionMessage;
			if ( lastLevelMessage.StartsWith( "Cannot build", StringComparison.Ordinal ) )
				SayCannotBuild( lastLevelMessage, pendingItem?.Entry );
		}
		// the release's cue spares the error feedback of the message that follows it by one frame, no more
		buildRefusalCue = false;

		var rideOwnsInput = ridingAtStart || IsRiding || camcorderOwnsInput;
		bool inPark, toolClick;
		if ( admittedPointerEvents != null )
		{
			toolClick = DrainPointerEvents( context, admittedPointerEvents, pointerFrameInput, rideOwnsInput, mouseCaptured.Value, keyboardCaptured.Value, textCaptured.Value );
			inPark = !overUi && pausePointerOverlay == null && !rideOwnsInput;
		}
		else
			(inPark, toolClick) = DispatchParkPointer( context, input, overUi, consumed, rideOwnsInput, paused,
				mouseCaptured.Value, keyboardCaptured.Value, textCaptured.Value );
		UpdateBlueprint( inPark && !rideOwnsInput, context.Delta, paused || Status.Speed == GameSpeed.Paused );

		for ( var index = messages.Count - 1; index >= 0; index-- )
		{
			messages[index] = (messages[index].Text, messages[index].Age + context.Delta);
			if ( messages[index].Age > MessageSeconds )
				messages.RemoveAt( index );
		}
		UpdateCursor( input, overUi || consumed || pausePointerOverlay != null, mouseCaptured == true );
		Status.Update( context.Delta );
		SyncRideScreen();
		PrepareRideStatic( context ); // observe the final camera once, after this frame's callbacks and shortcuts
		FinishChallengeMessageFrame();
		// the lost park's overlay takes every click for itself (no tool, no selection) and leaves the pointer to the park camera
		if ( LossOverlayOnTop )
		{
			level.WorldPointerRouted = true;
			return false;
		}
		return overUi || consumed || toolClick;
	}

	/// <summary>The window that took a left or right press: the park view when the press landed on it (null: no such press seen since the last release). Used when no routed pointer batch says so.</summary>
	private bool? legacyLeftPress, legacyRightPress;

	/// <summary>The right quick click of this delivery that the park view's tools and modes cancel on (<see cref="UiInput.RightClicked"/>, taken by the park, with the cancel option on).</summary>
	private bool parkRightClick;

	/// <summary>A press entered the path tool (the original's tool object, 0x4879D0 case 1 or 2): the release that ends this click is the tool's first click, at the cell under the pointer there.</summary>
	private bool pendingPathEntry;

	/// <summary>[EXT:online-coop-commands] <see cref="Level.HostToolStarts"/> when the last left press was taken (null: no press seen): a tool a guest's request starts when the host's answer comes back in between (CompleteMove, CompleteQueueEdit) takes no click from that press's release.</summary>
	private int? pressHostStarts;

	/// <summary>
	/// [EXT:online-coop-commands] <see cref="Level.HostToolStarts"/> at the start of this frame, before the host's answers moved any tool: a press first seen in a frame whose answers started or moved
	/// a tool was made while the player saw the tool as it was, so its release is no click of the tool as it is now (C3c-1b).
	/// </summary>
	private int frameHostStarts;

	/// <summary>
	/// [EXT:online-coop-commands] Whether the guest's coaster panel waited for the host at the start of this frame, before its answers (C3c-2): a press first seen in this frame was made while it
	/// waited (<see cref="coasterPressWhileWaiting"/>), whatever the answers did to the wait.
	/// </summary>
	private bool frameCoasterWaiting;

	/// <summary>The release played the refusal cue (0xAF) for a refused build click: its "Cannot build" message follows without the error feedback of <see cref="SayCannotBuild"/>.</summary>
	private bool buildRefusalCue;

	/// <summary>A press on a queue cell chose that queue's edit (0x4879D0 case 3, tool 0x14): the release starts the queue tool for it, wherever the pointer is then.</summary>
	private (int X, int Y)? pendingQueueEdit;

	/// <summary>Whether a tool object of the original (vtable 0x6FE9E0: the placement, the line tools, the rectangles, the coaster tool) is installed: its press handler 0x524790 sets the latch the release's refusal sound reads.</summary>
	private bool ToolObjectActive => level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects || level.CellTool.IsActive || level.IsCoasterToolActive
		|| level.PatrolTool is { IsActive: true } || level.BuyLandTool is { IsActive: true } || level.AreaClearTool is { IsActive: true };

	/// <summary>The press handler of the tool object (0x524790) sets the press latch 0x8186D8 when a tool object is installed: the refused click's cue plays at the release only behind it.</summary>
	private void LatchToolPress()
	{
		if ( ToolObjectActive )
			refusalClick.Press();
	}

	/// <summary>The left press ends with no click (a release the platform made up, or one an overlay took): what the press chose and the latch it set end with it.</summary>
	internal void AbandonLeftGesture()
	{
		pendingPathEntry = false;
		pendingQueueEdit = null;
		pressConsumed = false;
		// a pylon being dragged settles where it is, as when the park pointer is suspended: it does not stay on the cursor
		EndCoasterGesture();
		legacyLeftPress = null;
		pressHostStarts = null;
		refusalClickState = null;
		buildRefusalCue = false;
	}

	/// <summary>The release handler's refused-click cue (cat_ui 0xAF at the clicked cell, 0x524AA7) when the click it just handled found the ghost refused; the latch is consumed either way.</summary>
	private bool SoundRefusedRelease()
	{
		if ( refusalClick.Release() is not { } refused )
			return false;
		GameAudio.PlayPathRefused( refused.X, refused.Y );
		return true;
	}

	/// <summary>
	/// Whether the park view took the press that this delivery's release (or right release) ends: a release goes to the window that took its press, wherever the pointer is now (without a routed batch, which
	/// settles this itself, the press is remembered here; a release with no press seen is taken where the pointer is).
	/// </summary>
	// [BIN:TPW-EXE:0x658b7f release recipient] the window manager saves the down and the up recipients (0x658b1a, 0x658b7f): an up goes to its down's window
	private (bool Left, bool Right) ReleaseOwners( UiInput input, bool overUi, bool consumed, bool rideOwnsInput )
	{
		// a press made in the ride view or while walking is not the park view's: its release after leaving is nobody's click
		var free = !overUi && !consumed && !rideOwnsInput;
		if ( input.LeftPressed )
			legacyLeftPress = free;
		if ( input.RightPressed )
			legacyRightPress = free;
		bool left = true, right = true;
		if ( input.LeftReleased )
		{
			left = legacyLeftPress ?? free;
			legacyLeftPress = null;
		}
		if ( input.RightReleased )
		{
			right = legacyRightPress ?? free;
			legacyRightPress = null;
		}
		return (left, right);
	}

	/// <summary>
	/// One pointer delivery; frame timers, keyboard shortcuts and visual animation run in Update only. The park view's mouse procedure 0x4881A0 gives the mode object a left press (slot +4) and a
	/// left release (slot +8): the idle mode's press picks and enters (0x4879D0), the tool object's release is the tool's click (0x524960) at the cell under the pointer then; the right button's quick click
	/// ends the mode on its release.
	/// </summary>
	private (bool InPark, bool Consumed) DispatchParkPointer( UiContext context, UiInput input, bool overUi, bool consumed,
		bool rideOwnsInput, bool paused, bool mouseCaptured, bool keyboardCaptured, bool textCaptured )
	{
		var viewport = new NVector2( context.Canvas.Width, context.Canvas.Height );
		var covered = overUi || consumed;
		var (leftOwned, rightOwned) = ReleaseOwners( input, overUi, consumed, rideOwnsInput );
		// a release the park view took acts where the pointer is, over the interface or not: the hover cell comes from the pointer ray whatever lies over it (0x54E2D5 per frame)
		if ( input.LeftReleased && leftOwned || input.RightReleased && rightOwned )
			overUi = consumed = false;
		// a release the platform made up (a browser blur) ends the press without a click: the tools see no release, the latches and the press' choices end with it
		// [EXT:online-coop-commands] a guest's Move pick-up, queue edit or track tool that the host's answer started between this press and its release, or a line, cut or track Backspace whose
		// outcome moved its tool on meanwhile (C3c-1b): the release is not a click of that tool (tp.exe's release needs no press, 0x46C610, but the guest's click came before the tool was as it is
		// now); every other tool armed in between takes the release as the original does, with no cue (the press latch was not set)
		var hostStartedSincePress = input.LeftReleased && leftOwned && pressHostStarts is { } started && started != level.HostToolStarts;
		var lostRelease = input.LeftReleased && leftOwned && input.LeftReleaseLost;
		input = input with { LeftReleased = input.LeftReleased && leftOwned && !input.LeftReleaseLost && !hostStartedSincePress, RightClicked = input.RightClicked && rightOwned };
		// The park view's own click is not a button-class 0x100. The hook 0x485780 plays cat_ui 0xBD only when the receiver of 0x100 has style bit 0x10 (the choice at 0x4857cb); this click stays 0x1F.
		if ( !overUi && !consumed && input.LeftPressed )
			GameAudio.PlayUi( ParkViewClickEvent );

		// The ride view is a view of one ride: the park's clicks and tools wait until it is left.
		var parkView = !overUi && !consumed && !rideOwnsInput;
		var inPark = parkView && level.TryGetGridCell( input.Mouse, viewport, out cursorX, out cursorY );
		if ( !inPark )
			cursorX = cursorY = -1;
		// [BIN:TPW-EXE:0x488434 right click cancel] with RMBCancelOn a right press the park view took and let go within 200 ms and 8 px ends the mode (0x52F200(0, 1)) at the release, wherever the pointer then is; nothing happens on the press
		parkRightClick = input.RightClicked && parkView && !mouseCaptured && GameOptions.Current.RmbCancel;
		// Also consume the exit frame: leaving a ride must not activate an armed tool on that same click.
		var staffToolClick = !rideOwnsInput && (UpdateStaffTool( input, inPark && !mouseCaptured, viewport ) || UpdatePatrolTool( input, inPark && !mouseCaptured )
			|| UpdateBuyLandTool( input, inPark && !mouseCaptured ) || UpdateAreaClearTool( input, inPark && !mouseCaptured )
			|| UpdatePoolTool( context, input, inPark && !mouseCaptured ));
		var visitorClick = TrySelectWorldVisitor( input, viewport,
			paused || overUi || consumed || rideOwnsInput || ChatMode || mouseCaptured || keyboardCaptured || textCaptured || staffToolClick || level.StaffPlacement is { IsActive: true } || level.PatrolTool is { IsActive: true }
			|| level.BuyLandTool is { IsActive: true } || level.AreaClearTool is { IsActive: true } || level.PoolTool is { IsActive: true } );
		var toolClick = staffToolClick || visitorClick || rideOwnsInput || UpdateCoasterTool( context, input, inPark, paused ) || UpdateCellTool( context, input, inPark, paused );
		// [BIN:TPW-EXE:0x524790 press latch] the tool object's press handler sets 0x8186D8 for every press it takes; the release reads it for the refusal sound (PathRefusalClick)
		if ( input.LeftPressed && parkView && !mouseCaptured )
		{
			LatchToolPress();
			pressHostStarts = frameHostStarts;
		}
		// A right click leaves a build tool that Ctrl kept armed (and one still waiting for its first click).
		if ( parkRightClick && !paused && level.BuildEntry != null && !toolClick )
		{
			LeaveBuildTool();
			toolClick = true;
		}
		// [BIN:TPW-EXE:0x487013 hover modes] with Shift alone, or with the visitor locator panel out, the hover handler picks visitors only (IsVisitorPickState) and no object mode is set
		// [BIN:TPW-EXE:0x4879D0 object click] the object a park click opens is the owner of the hovered cell: the hover handler 0x486d90 sets cursor mode 4 for a hovered cell of map type 4, 9 or 10
		// with an owner thing (0x527e80; the cell-type switch 0x4875ae, mode 4 at 0x48769f) once no visitor or staff member is hovered, and the click dispatcher's mode 4 (switch 0x487a74) opens that thing's window (0x487c16: 0x486920 by its kind; the modes 8 to 0xc of a hovered staff member open 0x4b6080 instead, 0x487c77); the hovered
		// cell 0x7b05cc is the cell of the cursor ray's nearest hit (0x45bf90 → 0x45d560, Level.TryGetGroundHit), which lands on an object's model before the terrain behind it (ObjectRayPick), so a
		// click on the visible top of a tall ride, shop or feature picks that object; the pick sprites of the sprite pass (0x5890e0 → 0x532bd0) are the people's only: the owner search accepts
		// thing types 1, 4 to 8 and 0x12 (visitors and staff, 0x532d14-0x532d2c; the hovered-thing filter 0x46c110), and the sprite scripts' FRAME op 0x476940 marks its sprite pickable (+0x114 = 1 through 0x540b90) for any frame
		// but -1, so no ride or shop (thing type 3) is picked by a sprite
		// [BIN:TPW-EXE:0x488768 left press] the press (message 0x10005) calls the click dispatcher 0x4879D0 before the mode object's press slot: in the idle mode it opens the window of the hovered object, guest or
		// staff member, enters the path, queue or track tool or arms a copy; a tool object takes no part in it. The pick is the press, not the release, and no click-or-drag test applies to it.
		var idle = level.BuildEntry == null && !level.IsPlacing && !level.CellTool.IsActive && !level.IsCoasterToolActive && level.StaffPlacement is not { IsActive: true } && level.PatrolTool is not { IsActive: true }
			&& level.BuyLandTool is not { IsActive: true } && level.AreaClearTool is not { IsActive: true } && level.PoolTool is not { IsActive: true };
		if ( input.LeftPressed )
		{
			pressConsumed = toolClick;
		}
		if ( inPark && idle && !IsVisitorPickState( Locator.Mode, Input.ModifierMaskDown ) && input.LeftPressed && !toolClick )
		{
			var clicked = level.Objects.FindAt( cursorX, cursorY );
			// [EXT:pools-waves] a deliberate second click on the wave machine that is already selected opens its settings window; selecting it (here or from the attractions list) changes nothing (ParkHud.Pools.cs)
			if ( !ChatMode && clicked != null && clicked == selectedObject && IsWaveMachine( clicked ) )
				OpenWaveSettings( clicked );
			// [BIN:TPW-EXE:0x487b0b object copy] Ctrl at the click arms a copy of the object instead of opening its window (CopyObject); the chat mode has no object window
			// a click on a track add-on opens its ride's window (Level.WindowTarget); the chat mode has no object window
			if ( !ChatMode && !(clicked != null && (Input.ModifierMaskDown & 0xC) != 0 && CopyObject( clicked )) )
			{
				// the park view's click selects the object it hit (0x4879D0), which writes the track tool's globals
				level.NoteToolSelection( clicked );
				SelectObject( level.WindowTarget( clicked ) );
			}
		}
		// [EXT:online-avatar-click] in a visit with other players a click on the ground walks the player's figure there (manual p. 68: "Left click to walk to destination"): in the
		// chat mode wherever it lands but on a rideable ride, which it rides (ChatModeRide), as the original's chat mode panel does (0x4BD4A0, help 401), and in the HUD where it
		// hits no object; a deliberate click, acting on its release
		if ( inPark && idle && !IsVisitorPickState( Locator.Mode, Input.ModifierMaskDown ) && input.LeftReleased && !input.NotClick && !pressConsumed && !toolClick )
		{
			var clicked = level.Objects.FindAt( cursorX, cursorY );
			if ( ChatMode && ChatModeRide( clicked ) is { } ride )
				StartRide( ride );
			else if ( (clicked == null || ChatMode) && level.IsReadOnlyVisit && host?.Presence?.Invoke() is { Walks: true } presence
				&& level.TryGetGroundCell( input.Mouse, viewport, out var groundX, out var groundY ) )
				presence.Click( groundX, groundY );
		}
		if ( lostRelease )
			AbandonLeftGesture();
		if ( input.LeftReleased )
		{
			// what the press chose (the path tool's entry, a queue's edit) ends with the release that answered it
			pendingPathEntry = false;
			pendingQueueEdit = null;
			pressConsumed = false;
			// [BIN:TPW-EXE:0x524A63 build refusal] a build click whose ghost is refused places nothing and the tool stays, as for the line tools: the release plays the refusal cue behind the press latch
			// [EXT:online-coop-commands] a guest's placement that waits for the host takes no click, so it has no cue either; the original skips the cue only during a layout replay (0x785A2C), which OpenTPW has none of
			if ( inPark && !toolClick && !paused && !level.IsReadOnlyVisit && level.WaitingPlacement == null && level.BuildClickRefused( cursorX, cursorY ) )
				refusalClick.Refused( cursorX, cursorY );
			// the error feedback of "Cannot build" is spared only when the cue really sounded (SayCannotBuild, next frame)
			buildRefusalCue = SoundRefusedRelease();
		}
		// the level's own tool click (the build tool, the removal) acts on the release the park took and no tool of the HUD used
		if ( input.LeftReleased || hostStartedSincePress )
			pressHostStarts = null;
		level.ParkOwnsRelease = input.LeftReleased && !toolClick;

		return (inPark, covered || toolClick || hostStartedSincePress);
	}

	/// <summary>The patrol tool's HUD side: the hovered cell, right click leaves, a left click stores a corner or sets the area (tp.exe mode 0x36).</summary>
	private bool UpdatePatrolTool( UiInput input, bool inPark )
	{
		if ( level.PatrolTool is not { IsActive: true } tool )
			return false;
		if ( parkRightClick )
		{
			tool.Cancel();
			return true;
		}
		if ( debugPatrolHover is { } held )
			tool.Hover( held.X, held.Y );
		else if ( inPark )
			tool.Hover( cursorX, cursorY );
		else
			tool.Hover( null, null );
		// [BIN:TPW-EXE:0x524960 patrol click] mode 0x36 is a tool object's: both corners are clicks of the release (0x525d..), at the cell under the pointer then
		if ( !inPark || !input.LeftReleased )
			return false;
		tool.Click( cursorX, cursorY );
		// [BIN:TPW-EXE:0x524A63 refused ghost] the second click on a red rectangle ends the tool and the release plays the refusal cue (cat_ui 0xAF, 0x524AA7), behind the press latch the tool's press set
		if ( tool.TakeRefusal() is { } refused )
			refusalClick.Refused( refused.X, refused.Y );
		return true;
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
		// [EXT:online-coop-commands] a press made while the staff tool waited is no click even when this frame's outcome armed, replaced or ended it (C4c-2). A first start still waiting
		// with no tool on keeps the idle park's selections, as before; the next frame samples the new state and can take a fresh staff press.
		if ( inPark && input.LeftPressed && !parkRightClick && frameStaffRequestWaited && (frameStaffToolWasActive || tool is { IsActive: true }) )
		{
			level.NoteWaitingClick();
			return true;
		}
		if ( tool == null || !tool.IsActive )
		{
			if ( staffCursorSet )
			{
				staffCursorSet = false;
				Input.CursorType = Input.CursorTypes.Normal;
			}
			// [EXT:online-coop-commands] a guest's right click lets go of a hire or pick-up that waits for the host with no staff tool on yet, before any other tool takes it (C4c-2)
			if ( parkRightClick && tool != null && GuestStaffStartWaits )
			{
				tool.Cancel();
				return true;
			}
			return false;
		}
		staffCursorSet = true;
		Input.CursorType = Input.CursorTypes.Carry;
		// the same capture gate as the visitor selection: a press on an ImGui overlay is not a park click
		if ( parkRightClick )
		{
			tool.Cancel();
			return true;
		}
		// the sprite hangs at the exact ground point under the pointer every frame, also while the pointer is over the HUD (the original reads the mouse's world point, not the hover cell);
		// a frame without a pointer (a key alone) keeps the last one
		// [BIN:TPW-EXE:0x45CD44 mouse point] the point is the picker's nearest hit, the terrain's or an object model's (0x45BF90 writes the same point to 0x7A1C90; Level.TryGetGroundHit)
		// [APPROX:GUEST-021] only where the original's cursor and the person's sprite anchor land on the screen is open: the point, its +5 and the terrain's height the sprite pass adds are read and ported (StaffRenderer.DangleAnchor), the pointer's hotspot is (0, 0); the PC video shows the person below the glove (evidence needed: a capture)
		if ( input.Mouse.X >= 0 && input.Mouse.Y >= 0 )
		{
			// Keep the cell mapping unchanged, but retain the same cached pick's height for presentation.
			if ( level.TryGetGroundCell( input.Mouse, viewport, out var groundX, out var groundY )
				&& level.TryGetGroundHit( input.Mouse, viewport, out var ground ) )
				tool.SetCursor( groundX, groundY, ground.GetSystemVector3() );
			else
				tool.SetCursor( null, null );
			if ( inPark )
				tool.Hover( cursorX, cursorY );
			else
				tool.Hover( null, null );
		}
		// [BIN:TPW-EXE:0x46C8E0 staff placement press] the hire and carry tools are not the tool object: their own classes (vtables 0x6FEA40 and 0x6FEA70) take the left PRESS (slot +4 = 0x46C8E0 / 0x46CC40) and ignore the release (slot +8 is empty)
		if ( !inPark || !input.LeftPressed )
			return false;
		// [EXT:online-coop-commands] a guest's click is a request to the host (C4c-2)
		if ( GuestStaff is { } guestStaff )
		{
			ClickGuestStaff( guestStaff, tool, cursorX, cursorY );
			return true;
		}
		// a click on an invalid cell only logs in the original ("Can't put staff here"): no player text, the tool stays
		tool.Click( cursorX, cursorY );
		return true;
	}

	/// <summary>
	/// A refused build ("Cannot build ...", <paramref name="message"/>) said as the HUD says it, with the error feedback: a pool's own words where its rule refused it, else that the
	/// object or line cannot be built here. <paramref name="entry"/> is the refused object's, when known.
	/// </summary>
	// [BIN:STP-PPC:0x10139A40 error feedback] a rejected action plays cat_ui event 0x1D (called from the buy window at 0x10163AFC/0x10163BFC and the checkbox dialog at 0x1013B7A4)
	// [EXT:cannot-build-sound] the "Cannot build" message text, and the error feedback 0x1D where the original's own cue did not sound: a refused placement click plays cat_ui 0xAF at its release (0x524AA7, behind the refused-ghost latch 0x816D48, outside a layout replay 0x785A2C), and tp.exe plays 0x1D at exactly two sites (0x487b51 in the park click handler 0x4879d0, a cost above the money, and the helper 0x486ae0, whose only caller is the hire window 0x4af480); a refusal the host sends back sounds no 0xAF here, so it keeps 0x1D; the message text is an OpenTPW addition
	// [BIN:TPW-EXE:0x524AA7 refusal cue] [BIN:TPW-EXE:0x535670 refusal stage] [BIN:TPW-EXE:0x536100 refusal stage] the refusal stages submit no sound (no call site of the sound entry 0x51bfc0 lies in them or the callees reached from them); the build tools' only refusal cue is the release handler's 0xAF
	// [EXT:pools-changing-room] a changing room that is refused for want of a pool says so
	// [EXT:pools-slides] a slide tower refused for want of a pool says so in its own words, turned away from the pool beside it in others
	private void SayCannotBuild( string message, ObjectCatalogEntry? entry )
	{
		PostMessage( strings.Extra( message.EndsWith( $": {nameof( OriginalPlacementResult.NeedsPool )}.", StringComparison.Ordinal )
			? (entry is { } refused && PoolSlideTowers.IsSlideTower( refused ) ? (level.LastRefusalTowerFacesAway ? OpenTpwText.PoolSlideTowerFacesAway : OpenTpwText.PoolSlideTowerNeedsPool) : OpenTpwText.PoolChangingRoomNeedsPool)
			: message.EndsWith( $": {nameof( OriginalPlacementResult.NeedsPoolWater )}.", StringComparison.Ordinal ) ? OpenTpwText.PoolAttractionNeedsPool
			: message.EndsWith( $": {nameof( OriginalPlacementResult.NeedsPoolEdge )}.", StringComparison.Ordinal ) ? OpenTpwText.PoolExtraNeedsEdge : OpenTpwText.CannotBuildHere ) );
		// [EXT:cannot-build-sound] the error feedback only where the original's own refusal cue did not sound: its release plays 0xAF (buildRefusalCue), a guest's click and a refusal the host sends back play none
		if ( !buildRefusalCue )
			GameAudio.PlayUi( GameAudio.ErrorEvent );
	}

	/// <summary>
	/// The path/queue tool's HUD side: park-view help (441–445), the ghost line under the cursor, Backspace undo
	/// and right-click cancel. Clicks inside the tool are committed here; <see cref="Update"/> reports them as captured,
	/// so <see cref="Level"/>'s park click (the same commit for input without the HUD) skips them.
	/// </summary>
	private bool UpdateCellTool( UiContext context, UiInput input, bool inPark, bool paused )
	{
		var tool = level.CellTool;
		// [BIN:TPW-EXE:0x0048C8D0 pause-menu click gate] the handler tests the pause-menu object, not the speed flag;
		// admission is separate: 0x65db25 chooses the topmost eligible window, and 0x658b1a/0x658b7f save down/up recipients.
		// [APPROX:PATH-017] ParkHud.PointerInput implements bounded ordinary-WM batch admission then P/pointer drain, including captured movement and original release owners. The P window's backdrop (0x65dd7b, flags 1, appended last under the root;
		// 0x65db25 lets the last eligible sibling win, 0x65f6d1 forwards nothing) is created by the P key and by the offline focus-loss pause (0x4a27f0, 0x4a2790) and covers only the windows that existed then: it takes every click and the camera takes no input under it.
		// Alternate buffered input, the aggregate wheel and the keys other than P of the screens below it, and the original backdrop/panel rendering remain outside this contract; a generic speed flag is not an input owner.
		// The remove tool owns the park click while it is on (Level.RemoveAt clears paths and queues too).
		if ( paused || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects || level.IsCoasterToolActive
			|| level.PatrolTool is { IsActive: true } || level.StaffPlacement is { IsActive: true } || level.BuyLandTool is { IsActive: true }
			|| level.AreaClearTool is { IsActive: true } || level.PoolTool is { IsActive: true } )
			return false;
		// a visit builds no paths: while other players are shown its ground's help says that a click walks the player's figure there (UIHELPTEXT 401), not while the
		// chat entry takes the keys (the help box would cover it)
		// [BIN:TPW-EXE:0x4BD933 chat mode hover] the chat mode panel shows help 0x191 (401, "Left-click to walk to this point") over the park
		// [EXT:online-coop-presence] a shared park's chat mode gives the ride help too, never the walk's (its clicks never walk)
		if ( inPark && (level.IsReadOnlyVisit || ChatMode) )
		{
			// [BIN:TPW-EXE:0x4BD7F4 chat mode ride help] over a rideable ride the chat mode panel shows help 0x244 (580, "<ride>: Left-click to ride") with the ride's name
			if ( ChatMode && !Input.TextEntryActive && ChatModeRide( level.Objects.FindAt( cursorX, cursorY ) ) is { } ride )
				context.HoverHelp = strings.HelpFormat( 580, EntryName( ride.Entry ) );
			else if ( level.IsReadOnlyVisit && host?.Presence?.Invoke() is { Walks: true } && !Input.TextEntryActive && (ChatMode || level.Objects.FindAt( cursorX, cursorY ) == null) )
				context.HoverHelp = strings.Help( 401 );
		}
		else if ( inPark && tool.HoverHelpId( level.Paths, cursorX, cursorY ) is int help )
			context.HoverHelp = strings.Help( help );
		// [EXT:online-coop-presence] the chat mode has no tools (its entry retired them): no click, Backspace or line starts or edits one
		if ( ChatMode )
			return false;
		if ( !tool.IsActive )
		{
			pendingPathEntry = false;
			// Backspace over a path, queue, pool bridge or pool water cell with no tool on: the backtrack sound, then that cell is removed (GameAudio.BacktrackEvent; a queue is [EXT:queue-backtrack], a pool bridge or water cell [EXT:pools-bridges] [EXT:pools-tool]); a held key repeats only over path and queue cells.
			if ( inPark && input.Backspaces > 0 && Stack.Screens.Count == 1 && !Input.TextEntryActive && Camera.Mode is ParkCameraMode && BacktrackKey( input.BackspaceRepeating ) )
				return true;
			// [EXT:online-coop-commands] a right click leaves a guest's queue edit or Move pick-up that waits for the host, as it leaves a tool (C3b)
			if ( parkRightClick && level.WaitingToolStart != null )
			{
				level.DropToolStartWait();
				return true;
			}
			// [BIN:TPW-EXE:0x48C940 queue edit release] the press on a queue cell chose the ride (0x4879D0 case 3: tool 0x14, whose enter 0x46D5A0 only sets the mode); the mode's release handler (0x524960, mode 0x14: 0x530120 walks the queue
			// into the stack, then SetMode(3)) starts the queue tool for the chosen ride, wherever the pointer is released
			if ( pendingQueueEdit is { } edit )
			{
				if ( !input.LeftReleased )
					return input.LeftPressed;
				pendingQueueEdit = null;
				if ( level.EditQueueAt( edit.X, edit.Y ) )
				{
					PostMessage( strings.Help( 445 ) );
					if ( inPark )
						tool.Hover( (cursorX, cursorY) );
					return true;
				}
				// [EXT:online-coop-commands] a guest's edit went to the host (or one waits already): the queue tool starts once it comes back (C3b, CompleteQueueEdit)
				return level.WaitingToolStart != null;
			}
			if ( !inPark || !input.LeftPressed || level.IsReadOnlyVisit || level.Objects.FindAt( cursorX, cursorY ) != null )
				return false;
			// A left press on a queue cell chooses that ride's queue edit (help 444): the queue tool starts at its back cell when the button is let go.
			if ( tool.HoverHelpId( level.Paths, cursorX, cursorY ) is 444 && level.CanEditQueueAt( cursorX, cursorY ) )
			{
				pendingQueueEdit = (cursorX, cursorY);
				return true;
			}
			// A left press on an empty owned cell or on a path cell enters the path tool (help 441/442); no start is set and nothing is laid until the release, which is the tool's first click.
			// [BIN:TPW-EXE:0x46D5A0 tool enter] the path tool object's enter hovers the cell under the pointer (0x5234D0) and calls SetMode(1, 1); the click is the release handler's (0x524960)
			if ( tool.HoverHelpId( level.Paths, cursorX, cursorY ) is not (441 or 442) || !level.BeginPathTool() )
				return false;
			pendingPathEntry = true;
			tool.Hover( (cursorX, cursorY) );
			return true;
		}
		if ( input.Backspaces > 0 )
		{
			// [EXT:online-coop-commands] a guest's Backspace in its track tool goes to the host, one at a time, under the ride's claim (C3c-1b); a line or Backspace that waits for the host is not
			// done yet; [EXT:online-coop-undo] in a shared park a path or queue segment is a take-back the host decides (C5): the host's refusal, or a segment no step of the player's laid, says why
			if ( level.Commands.SendsRequests && tool.IsWaiting )
				level.NoteWaitingClick();
			else if ( !tool.Undo() && tool.TakeUndoRefusal() is { } refused )
			{
				// [EXT:online-coop-undo] a take-back on a ride whose claim the host no longer holds ends its tool in the claim's words, as a guest's does (C5-3)
				if ( refused.Reason == CoopBuildRights.ClaimLostReason )
				{
					tool.Cancel();
					SayGuestNote( OpenTpwText.CoopClaimLost );
				}
				else
					SayTakeBackNote( refused.Reason );
			}
		}
		if ( parkRightClick )
		{
			pendingPathEntry = false;
			tool.Cancel();
			return true;
		}
		if ( !inPark )
			return false;
		tool.Hover( (cursorX, cursorY) );
		// [BIN:TPW-EXE:0x524960 line click] every line click of the tool object is its release handler's: the press (0x524790) only sets the latch
		if ( !input.LeftReleased )
			return false;
		// [EXT:online-coop-commands] a guest's line or track Backspace that waits for the host takes no other click (C3a-2, C3c-1b)
		if ( tool.IsWaiting )
		{
			pendingPathEntry = false;
			level.NoteWaitingClick();
			return true;
		}
		// The release of the click that entered the path tool is the tool's first click: a refused cell ends the tool at once, and the refused click sounds at this release (PathRefusalClick).
		if ( pendingPathEntry )
		{
			pendingPathEntry = false;
			level.ClickEnteredPathTool( cursorX, cursorY, out var refused );
			if ( refused != null )
			{
				refusalClick.Refused( cursorX, cursorY );
				PostMessage( strings.Extra( refused.StoppedBy == CellBuildResult.NotEnoughMoney ? OpenTpwText.NotEnoughMoney : OpenTpwText.CannotBuildHere ) );
				return true;
			}
			// [BIN:TPW-EXE:0x005271B7 lay click] every path or queue click past the refused-ghost test plays cat_ui 101 (0x65) before its start or line, the first click included
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
			ReportCellToolClick( false, CellToolMode.Path, tool, false );
			PostMessage( strings.Help( 443 ) );
			tool.Hover( (cursorX, cursorY) );
			return true;
		}
		// The commit click; Level skips it because the HUD reports the pointer as captured this frame.
		var click = new LineClick( (cursorX, cursorY), level.QueueJoinCell( tool ), tool.Start != null, tool.Mode, InterfaceMode() );
		var sessionRefusals = level.SessionRefusals;
		// the click's modifiers go with it: a kart or water line that closes its circuit with exactly Ctrl and no queue start pending keeps its line tool (Level.ContinueTrackTool)
		// [BIN:TPW-EXE:0x527146 track line end] with no queue start (0x8186c0 zero) the line tool's last click clears the start and keeps its mode (0xB or 0x10) when the modifier word is exactly 0xC, and any other word ends the tool (0x52f200(0, 0) at 0x527156)
		var result = level.ClickCellTool( (cursorX, cursorY), Input.ModifierMaskDown );
		// [EXT:online-coop-commands] a guest's line went to the host: its sounds, help and messages wait for its outcome (CompleteLine, C3a-2)
		if ( result?.Waiting != null )
		{
			waitingLineClick = click;
			return true;
		}
		FinishLineClick( click, result, released: false, sessionRefused: level.SessionRefusals != sessionRefusals );
		return true;
	}

	/// <summary>
	/// The sounds, the advisor's help and the messages of a path or queue click's line, once it is known: at once, or [EXT:online-coop-commands] when a guest's request comes back (C3a-2),
	/// the button <paramref name="released"/> by then, so a refused line sounds at once. A line the session's rights refused is said by that notice, not as a cell that cannot be built on.
	/// </summary>
	private void FinishLineClick( LineClick click, SegmentResult? result, bool released, bool sessionRefused )
	{
		// [BIN:TPW-EXE:0x526cb3 track click] every release in the kart (0xB) or water (0x10) track mode that passes the refused-ghost latch plays cat_ui 0xCE at the clicked cell, the first click included and whatever the line then does
		if ( PlaysTrackToolClick( click.Mode, result ) )
			GameAudio.PlayUiAt( GameAudio.TrackToolClickEvent, GameAudio.CellSoundPosition( click.Cell.X, click.Cell.Y ) );
		if ( result is not { GhostRefused: true } )
			ReportCellToolClick( click.HadStart, click.Mode, level.CellTool, click.QueueJoin != null, click.InterfaceBefore );
		// [BIN:TPW-EXE:0x527587 queue-join sound] it follows every commit; a click on a refused ghost lays nothing and returns before the commit (0x524A63)
		if ( result is { GhostRefused: false } )
			level.PlayQueueJoin( click.QueueJoin );
		// [BIN:STP-PPC:0x10070DE0 path lay click] a committed path line plays cat_ui 101 (the tool's commit site, gated by its mode); a click on a refused ghost (or a refused track commit) plays cat_ui 175 at the clicked cell when the button is released (PathRefusalClick)
		if ( result is { Completed: true } )
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
		// [EXT:online-coop-lockstep] a line the session refused is said once, by its notice, not also as a cell that cannot be built on
		if ( result is { Completed: false } && !sessionRefused )
		{
			// a commit stopped only by the back-of-queue walk's bound is not one of the original's refusals
			if ( !result.StoppedByOwnLimit )
			{
				if ( released )
					GameAudio.PlayPathRefused( click.Cell.X, click.Cell.Y );
				else
					refusalClick.Refused( click.Cell.X, click.Cell.Y );
			}
			PostMessage( strings.Extra( result.StoppedBy == CellBuildResult.NotEnoughMoney ? OpenTpwText.NotEnoughMoney : OpenTpwText.CannotBuildHere ) );
		}
	}

	/// <summary>Whether a tool release plays the track tools' click (<see cref="GameAudio.TrackToolClickEvent"/>): a release in a kart or water line mode whose ghost was not refused.</summary>
	// [BIN:TPW-EXE:0x524ab6 refused release] a release with the refused-ghost latch set plays the refusal cue 0xAF and leaves through the latch's own mode table (0x52766c: 0xB and 0x10 to SetMode(0, 0) at 0x5266a3), so it never reaches 0x526cb3 and its 0xCE
	internal static bool PlaysTrackToolClick( CellToolMode modeBefore, SegmentResult? result ) => modeBefore is CellToolMode.KartTrack or CellToolMode.WaterTrack && result is not { GhostRefused: true };

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
			if ( level.BuyLandGhost( landTool ) is { } fence )
			{
				// [BIN:TPW-EXE:0x0053AAC0 fence ghost] the fence tool's tiles are the tile chooser's: red for the answer 1 (every tile while the rectangle is refused), blue otherwise
				foreach ( var ((x, y), answer) in fence )
					cells.Add( (x, y, answer == 1 ? BlueprintMarker.RefusedTile : BlueprintMarker.AllowedTile) );
			}
			else
			{
				// [EXT:untyped-harness-world] a world that does not type its cells has no records for the tile chooser: its Buy Land ghost is the host's cells along the rectangle's four edges (or the hovered post before the first click), red while the validator refuses
				var marker = landTool.HoverRefused ? BlueprintMarker.Refused : BlueprintMarker.Allowed;
				foreach ( var (x, y) in landTool.PreviewEdge() )
					cells.Add( (x, y, marker) );
			}
		}
		else if ( level.AreaClearTool is { IsActive: true } clearTool )
		{
			// [APPROX:BLUEPRINT-003] the area clear's ghost is the blueprint's cells along the rectangle's four edges (or the hovered cell before the first click), all blue: the preview lays the edges with LayLine(0x13a), whose validator answers 0xd and never refuses
			foreach ( var (x, y) in clearTool.PreviewEdge() )
				cells.Add( (x, y, BlueprintMarker.Allowed) );
		}
		else if ( level.PatrolTool is { IsActive: true } patrol )
		{
			// [BIN:TPW-EXE:0x005234d0 patrol tool hover] the hovered cell alone before the first click, the four edges of the rectangle after it, red when no cell of the rectangle can be stood on, blue otherwise
			var marker = patrol.PreviewRefused ? BlueprintMarker.Refused : BlueprintMarker.Allowed;
			foreach ( var (x, y) in patrol.PreviewEdge() )
				cells.Add( (x, y, marker) );
		}
		else if ( level.PoolTool is { IsBridge: true, HoverCell: not null } bridgeTool )
		{
			// [EXT:pools-bridges] the bridge tool previews the whole bridge: its two outside path ends and its seven cells, blue while it can be built, the cell to blame red when one is
			// (a missing path, water where land must be, something in the way), and every cell red when the refusal is the bridge's as a whole (the slope, the money)
			foreach ( var (x, y) in bridgeTool.HoverBridgeCells() )
				cells.Add( (x, y, bridgeTool.HoverBridgeRefuses( (x, y) ) ? BlueprintMarker.Refused : BlueprintMarker.Allowed) );
		}
		else if ( level.PoolTool is { IsActive: true, HoverCell: { } poolCell, HoverResult: { } poolResult } )
		{
			// [EXT:pools-tool] the pool tool marks the cell under the pointer: blue where it can be painted (or erased), red where it is refused
			cells.Add( (poolCell.X, poolCell.Y, poolResult is PoolBuildResult.Ok or PoolBuildResult.Existing or PoolBuildResult.RiverMarked ? BlueprintMarker.Allowed : BlueprintMarker.Refused) );
		}
		else if ( level.CellTool.IsActive && level.CellTool.Ghost is { } ghost )
		{
			// the path and queue tools mark each cell by the validator's answer (m_link, m_end: BlueprintLineGhost), the kart and water line tools each 2 x 2 tile by the tile chooser's (TileLineGhost); the other line tools and an untyped world mark the host's own line
			if ( level.TileLineGhostFor( ghost ) is { } tileLine )
			{
				// [BIN:TPW-EXE:0x005372AD tile answers] the chooser's answer 1 is the red tile, 0xB the m_end tile, any other the blue one
				foreach ( var ((x, y), answer) in tileLine.Tiles )
					cells.Add( (x, y, answer == 1 ? BlueprintMarker.RefusedTile : answer == 0xB ? BlueprintMarker.EndTile : BlueprintMarker.AllowedTile) );
			}
			else
				cells.AddRange( level.PathLineGhost( ghost )?.Cells ?? BlueprintGhosts.LineCells( ghost.Line, ghost.StoppedAt, ghost.Symbols ) );
		}
		else if ( inPark && level.BuildEntry is { } entry && cursorX >= 0 )
		{
			// a track add-on's ghost goes to its hover position on its ride's track, turned to the tile's flow, and keeps that angle (TrackAddOns.HoverTarget, 0x0052F1B0)
			var (targetX, targetY, rotation) = level.BuildTarget( entry, cursorX, cursorY );
			level.SetBuildRotation( rotation );
			var anchor = Level.GetCentredAnchor( entry, targetX, targetY, rotation );
			var placementResult = level.CheckBuildTarget( entry, targetX, targetY, rotation );
			var allowed = level.BuildGhostAllowed( entry, placementResult );
			placementRefused = !allowed;
			var footprint = level.ObjectBlueprint( entry, anchor.X, anchor.Y, rotation, placementResult, allowed );
			cells.AddRange( footprint.Cells );
			directions = footprint.Directions;
		}
		cells.AddRange( LocatedPatrolAreaEdge() );
		// [EXT:online-coop-commands] a guest's pool strokes: the cells sent or waiting to be sent, until their outcome, as a waiting line's ghost (C3b-3)
		if ( level.PoolTool is { HasGuestBatches: true } guestPool )
			cells.AddRange( guestPool.GuestStrokeCells.Select( cell => (cell.X, cell.Y, BlueprintMarker.Allowed) ) );
		if ( level.BuildEntry == null || !inPark )
			placementRefused = false;
		// the track symbols turn with the camera's yaw (0x790A38, BlueprintMarkers.SymbolAngle)
		blueprint.SymbolAngle = Camera.Mode is ParkCameraMode symbolCamera ? ParkCameraMode.OriginalYaw( symbolCamera.Yaw ) : 0f;
		// [BIN:TPW-EXE:0x005234D0 hover cell] every hover stores the cell under the pointer in 0x8186B8/0x8186B4, which the ghost's glide chases; an edit tool's hover stores its snapped tile (0x527960)
		// [BIN:TPW-EXE:0x0053DF75 glide tools] the faces glide only while the tool is the placement (4) or an edit tool (0xE, 0x13)
		var editGhost = level.CellTool.IsActive && level.CellTool.Mode is CellToolMode.KartEdit or CellToolMode.WaterEdit && level.CellTool.Ghost is { } editHover ? editHover.GlideCell : null;
		if ( editGhost is { } snapped )
			blueprint.GlideCursor = snapped;
		else if ( inPark && cursorX >= 0 )
			blueprint.GlideCursor = (cursorX, cursorY);
		blueprint.GlideActive = editGhost != null || inPark && level.BuildEntry != null && cursorX >= 0;
		blueprint.Update( delta, paused, Math.Max( 0, (int)level.SimulationTimeScale ) );
		blueprint.Show( cells, directions );
	}

	/// <summary>
	/// The patrol area of the staff member the staff locator has found and the camera follows: its border cells, blue. Empty without one, for a member without an area of its own
	/// and for a visitor.
	/// </summary>
	// [BIN:TPW-EXE:0x004b3640 patrol area highlight] on every message 0x1e of the locator panel (0x4b3790) while its camera flag 0x1000 is set and the located thing (0x7cc308) is staff (types 4 to 8) with an own area (0x506eb0), four LayLine passes of the op 0x136 mark its rectangle; the validator's flag 0x816D50 is shared with the other tools' hover branches, so the original's cells can turn red while another tool's ghost is refused; OpenTPW keeps them blue (the flag is 0 while no tool's hover sets it)
	private IEnumerable<(int X, int Y, BlueprintMarker Marker)> LocatedPatrolAreaEdge()
	{
		if ( !Locator.Following || Locator.ActorId == 0 || Locator.Role == LocatorModel.VisitorRole
			|| (Status.EconomySource as ParkEconomy)?.Staff.Members.FirstOrDefault( member => member.Id == Locator.ActorId ) is not { HasPatrolArea: true } located )
			return Array.Empty<(int X, int Y, BlueprintMarker Marker)>();
		var area = located.PatrolArea;
		if ( locatedAreaEdge is not { } cached || cached.Area != area )
			locatedAreaEdge = (area, PatrolAreaTool.EdgeCells( (area.X0, area.Y0), (area.X1, area.Y1) ).Select( cell => (cell.X, cell.Y, BlueprintMarker.Allowed) ).ToArray());
		return locatedAreaEdge.Value.Cells;
	}

	/// <summary>The border cells of the last located rectangle, kept while the rectangle is the same (the blueprint asks every frame).</summary>
	private ((int X0, int Y0, int X1, int Y1) Area, (int X, int Y, BlueprintMarker Marker)[] Cells)? locatedAreaEdge;

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

	/// <summary>
	/// [EXT:online-coop-session] The shared park's status line (decision C2d answer 4): a persistent line in a translucent box at the top centre while the park is shared
	/// or being shared, and a while after its end. Under the screens over the HUD (the pause menu dims it with the park).
	/// </summary>
	private void DrawSessionStatus( UiContext context )
	{
		if ( host.SessionStatus?.Invoke() is not { Length: > 0 } text )
			return;
		var canvas = context.Canvas;
		var (width, height) = context.Measure( context.Fonts.Small, text, (int)(canvas.Width * 0.8f) );
		var pad = 4 * canvas.TextScale;
		var rect = new UiRect( (canvas.Width - width) / 2f - pad, canvas.Height * 0.015f, width + pad * 2, height + pad * 2 );
		context.Batch.AddRectangle( rect, UiColors.HelpBox );
		context.DrawText( context.Fonts.Small, text, rect.Inflate( -pad ), UiColors.Text, UiAlign.Center, wrap: true );
	}

	/// <summary>The shared park's status line now (<see cref="HudHost.SessionStatus"/>), for tests.</summary>
	internal string? SessionStatusLine => host.SessionStatus?.Invoke();

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
		context.DrawText( context.Fonts.Small, text, rect.Inflate( -pad ), UiColors.Text, UiAlign.Center, wrap: true );
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
			// [EXT:pools-bridges] the bridge row has no object behind it: its preview is the committed Blender mesh
			if ( item.InfoId == PoolBridgeRow.InfoId && IsPoolRow( item ) )
			{
				icons[item.Id] = icon = PoolBridgePreview.Create();
				return icon;
			}
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
