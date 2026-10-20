using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.FrontEnd;

/// <summary>
/// The Game Mode buttons (UITEXT 239–241). The original chooses this once per player; OpenTPW asks per
/// park. The game maps it to a <see cref="OpenTPW.ParkStartKind"/>; the values are not the economy's.
/// </summary>
public enum GameMode { InstantAction, FullSimulation }

/// <summary>A park that can be loaded from the Load Park screen.</summary>
/// <param name="Sandbox">True for the OpenTPW sandbox save (jungle sandbox), false for an original read-only park.</param>
/// <param name="Cheated">[EXT:cheats-publish] The saved park is marked as cheated (docs/CHEATS.md); the menu says so after its name.</param>
public sealed record ParkLoadEntry( string Level, int ThemeNameIndex, bool Sandbox, string? OwnSavePath = null, string? DisplayName = null, DateTime? SavedAt = null, string? Requires = null, bool Cheated = false )
{
	/// <summary>[EXT:save-extension-switch] The extension switch the player confirmed for this load (<see cref="ParkExtensionSwitch"/>); null loads strictly.</summary>
	public ParkExtensionSwitch? Switch { get; init; }
}

/// <summary>What the front-end screens ask of the game.</summary>
public sealed class FrontEndActions
{
	/// <summary>Read-only saved Full Simulation key count, sampled after leaving the previous park; null when unavailable.</summary>
	internal int? PlayerKeySnapshot { get; init; }
	public Action<LobbyIslandInfo, GameMode> StartPark { get; init; } = ( _, _ ) => { };
	public Action<ParkLoadEntry> Load { get; init; } = _ => { };
	/// <summary>Lightweight own-save availability for the selected theme; assets are checked only on actual resume.</summary>
	public Func<string, ParkContinueEntry?> ContinueEntry { get; init; } = _ => null;
	public Action<ParkContinueEntry>? ContinuePark { get; init; }
	internal Func<string, AutomaticParkContinueEntry?> AutomaticEntry { get; init; } = _ => null;
	internal Action<AutomaticParkContinueEntry>? ContinueAutomatic { get; init; }
	/// <summary>Starts the screen-space particle effect the enter-park routine places over the lobby's key panel (not called in Instant Action).</summary>
	public Action StartEnterParkEffect { get; init; } = () => { };
	public Action Quit { get; init; } = () => { };
	public Func<UiScreenStack, Action, UiScreen>? CreateOptions { get; init; }
	/// <summary>Called when the selected island changes (moves the lobby camera).</summary>
	public Action<LobbyIslandInfo> IslandSelected { get; init; } = _ => { };
	public Func<IReadOnlyList<ParkLoadEntry>> LoadEntries { get; init; } = () => Array.Empty<ParkLoadEntry>();
	/// <summary>Opens the online screens on the menu's stack (Go Online, UITEXT 1); null hides the button.</summary>
	public Action<UiScreenStack>? GoOnline { get; init; }
	/// <summary>
	/// [EXT:save-extension-switch] Before an own save at the given path is loaded: asks whether to switch the extensions it differs in (<see cref="ParkExtensionSwitch"/>) and then calls the action with the confirmed
	/// switch, or calls it at once with null when there is nothing to ask (the load then agrees, or refuses with its reasons). The default never asks.
	/// </summary>
	public Action<string?, Action<ParkExtensionSwitch?>> ConfirmExtensions { get; init; } = ( _, proceed ) => proceed( null );
}

/// <summary>
/// The front end over the 3D lobby: the original island lobby panel (islandlobby.MD2 with the
/// f_lobbutbg button background) with the globe, previous, enter and next buttons at the original table's rectangles and
/// their original tooltips, the island name beside it, and an Escape menu with Go Online, Load, Options, Resume and Quit Game. Entering a park asks
/// for continuation when an own save is available, or for the new park mode. Player profiles are not implemented (docs/UI.md).
/// </summary>
public sealed partial class FrontEndMenu
{
	private readonly UiStringTable strings;
	private readonly FrontEndActions actions;

	public FrontEndMenu( UiStringTable strings, IReadOnlyList<LobbyIslandInfo> islands, FrontEndActions actions )
	{
		if ( islands.Count == 0 )
			throw new ArgumentException( "The lobby has no islands.", nameof( islands ) );
		this.strings = strings;
		Islands = islands;
		this.actions = actions;
		Main = CreateMain();
		Stack.Push( Main );
	}

	public UiScreenStack Stack { get; } = new();
	public UiScreen Main { get; }
	public IReadOnlyList<LobbyIslandInfo> Islands { get; }
	public int SelectedIndex { get; private set; }
	public LobbyIslandInfo Selected => Islands[SelectedIndex];
	public string SelectedName => Selected.ThemeNameIndex >= 0 ? strings.Theme( Selected.ThemeNameIndex ) : Selected.Name;

	public void SelectIsland( int index )
	{
		SelectedIndex = ((index % Islands.Count) + Islands.Count) % Islands.Count;
		actions.IslandSelected( Selected );
	}

	public void NextIsland( int direction ) => SelectIsland( SelectedIndex + Math.Sign( direction ) );

	public void SelectLevel( string level )
	{
		var index = Islands.ToList().FindIndex( island => string.Equals( island.Level, level, StringComparison.OrdinalIgnoreCase ) );
		if ( index >= 0 )
			SelectIsland( index );
	}

	private UiScreen CreateMain()
	{
		var screen = new UiScreen( "lobby" ) { Modal = false };
		// [BIN:TPW-EXE:0x757f60 lobby panel] the island lobby's table: the islandlobby panel (28,855)-(476,1521) with the f_lobbutbg background behind the buttons (67,1273)-(438,1484), b_worldview (68,946) help 346, b_entpark (68,1114) help 345, b_lobleft (97,1299) help 343 and b_lobright (260,1299) help 344; the companion table 0x757d88 gives the island-name field (477,1282)-(1571,1457)
		// [DATA:video 78:41 lobby] the panel measures (26,859)-(468,1514), the globe button's centre (142,1026), the gate button's (142,1187), the arrows' (167,1374) and (335,1374), the name in large yellow centred in the field; there is no logo, no title and no menu column
		var table = OriginalLayoutTables.Lobby;
		var panel = table.Root;
		screen.Add( new UiModelImage { Id = "lobbyPanel", Model = panel.Model!, Bounds = panel.Rect, Anchor = UiAnchor.BottomLeft } );
		screen.Add( new UiModelImage { Id = "lobbyButtons", Model = panel.Model2!, Bounds = panel.Inner!.Value, Anchor = UiAnchor.BottomLeft } );
		screen.Add( new UiLabel { Id = "islandName", Text = () => SelectedName, Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Fit = true,
			Bounds = OriginalLayoutTables.LobbyCity.Control( 0x49D9 ).Rect, Anchor = UiAnchor.BottomLeft } );
		UiButton LobbyButton( string id, string model, Action clicked ) => screen.Add( new UiButton
		{
			Id = id,
			Model = model,
			Help = strings.Help( table.Model( model ).Help ),
			Clicked = clicked,
			Adjusted = NextIsland,
			Bounds = table.Model( model ).Rect,
			Anchor = UiAnchor.BottomLeft
		} );
		LobbyButton( "previousIsland", "b_lobleft", () => NextIsland( -1 ) );
		var enter = LobbyButton( "enterPark", "b_entpark", ShowThemeStart );
		LobbyButton( "nextIsland", "b_lobright", () => NextIsland( 1 ) );
		// the globe button opens the online world (the original's Go Online lobby)
		if ( actions.GoOnline != null )
			LobbyButton( "worldView", "b_worldview", () => actions.GoOnline( Stack ) );
		// [EXT:lobby-menu-hint] the original opens the lobby menu with Escape and its lobby table has no button or other mouse route for it that was found (callers of the pause menu 0x48c830 in the front-end handler 0x4be1a0 and 0x4be537 take no click); the hint tells a mouse user where it is
		screen.Add( new UiLabel { Id = "menuHint", Text = () => strings.Extra( OpenTpwText.LobbyMenuHint ), Font = fonts => fonts.Small, Color = UiColors.Disabled, Align = UiAlign.Right,
			Bounds = new UiRect( 1548, 1484, 480, 40 ), Anchor = UiAnchor.BottomRight } );
		AddKeyCounter( screen );
		screen.Back = ShowMenu;
		screen.Focus( enter );
		return screen;
	}

	/// <summary>
	/// The lobby's Escape menu: a frameless list of cyan text lines. The original's has Go Online, Options, Select New Player, Resume Game and Quit Game; OpenTPW
	/// adds Load Park (the original loads parks from the game, not the lobby) and has no player profiles.
	/// </summary>
	// [DATA:video 27:59-28:03 lobby pause menu] On-line gaan, Opties, Nieuwe Speler Selecteren, Spel Hervatten, Spel Stoppen
	// [EXT:lobby-load] the Load Park line (UITEXT 3) is OpenTPW's: it stood in the lobby's right-hand menu column, which the original lobby does not have
	public void ShowMenu()
	{
		if ( Stack.Top?.Name == "lobbyMenu" )
			return;
		UiScreen? menu = null;
		void CloseMenu()
		{
			if ( menu is { IsRemoved: false } && ReferenceEquals( Stack.Top, menu ) )
				Stack.Pop();
		}
		// the menu closes before the screen it opens, so closing that screen returns to the lobby
		Action Then( Action open ) => () =>
		{
			CloseMenu();
			open();
		};
		var items = new List<(string Id, Func<string> Text, Action Clicked, bool Enabled)>();
		if ( actions.GoOnline != null )
			items.Add( ("goOnline", () => strings[UIStrings.GoOnline], Then( () => actions.GoOnline( Stack ) ), true) );
		items.Add( ("load", () => strings[UIStrings.Load], Then( ShowLoad ), true) );
		items.Add( ("options", () => strings[UIStrings.Options], Then( ShowOptions ), actions.CreateOptions != null) );
		items.Add( ("resume", () => strings[UIStrings.ResumeGame], CloseMenu, true) );
		items.Add( ("quit", () => strings[UIStrings.QuitGame], ShowQuit, true) );
		menu = UiDialogs.TextMenu( "lobbyMenu", items, CloseMenu, "resume" );
		Stack.Push( menu );
	}

	// [EXT:no-player-profiles] OpenTPW has no player profiles, so Game Mode is asked on every park entry and each entry starts a new park. The original keeps the mode per player (STP-PPC 0x1015D220/0x1013741C): [BIN:TPW-EXE:0x550ca0 GameType] derives it from bits 0x2000000 (2) and 0x1000000 (1) of the flags word 0x7a1a8c, which a front-end toggle (0x4bbf63..0x4bbf9f) sets, and resumes the player's per-theme autosave (path pieces "save" 0x747a2c, "users" 0x747a3c, "autosave" 0x747a58); a design decision
	public void ShowGameMode()
	{
		var screen = new ProgressChoiceScreen( "gameMode" );
		// [BIN:TPW-EXE:0x753f50 name dialog] the game mode is chosen in the new-player dialog: the wdialogw window (500,233)-(1516,830) with the title box (655,285)-(1315,365), the two latched b_ltoggle buttons (764,405)-(1206,476) and (764,498)-(1206,569) (help 358 and 359), e_joy (1241,606)-(1343,708) and the tick and X; the name field (593,616) belongs to player profiles, which OpenTPW has none of
		var table = OriginalLayoutTables.NameDialog;
		var window = table.Root.Rect;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = window, Anchor = UiAnchor.Center } );
		// [EXT:no-player-profiles] the title box shows "Game Mode" where the original's dialog asks for the player's name
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.GameMode], Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = table.Control( 0x709 ).Rect, Anchor = UiAnchor.Center } );
		var toggles = table.All.Where( node => node.Model == "b_ltoggle" ).OrderBy( node => node.Rect.Y ).ToArray();
		var modes = new (string Id, UIStrings Label, GameMode Mode, int Help)[]
		{
			("instantAction", UIStrings.InstantAction, GameMode.InstantAction, 358),
			("fullSimulation", UIStrings.FullSimulation, GameMode.FullSimulation, 359),
		};
		screen.Add( new UiLabel { Id = "park", Text = () => SelectedName, Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Center, Bounds = table.Control( 0x70A ).Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x004a70d1 mode radio group] the build selects no toggle of the group 0x70c (0x66a295 with 0x7a17, which names no child); e_joy (0x70b) is drawn as the table has it
		var joy = table.All.Single( node => node.Model == "e_joy" );
		var joyImage = screen.Add( new UiModelImage { Id = "joy", Model = joy.Model!, Bounds = joy.Rect, Anchor = UiAnchor.Center } );
		GameMode? chosen = null;
		for ( var index = 0; index < modes.Length; index++ )
		{
			var entry = modes[index];
			// [BIN:TPW-EXE:0x004a6e09 mode toggles] a toggle latches in its group (message 0x101) and only shows or hides e_joy: shown while the Instant Action toggle 0x70d is the selected one
			screen.Add( new UiButton
			{
				Id = entry.Id,
				Model = "b_ltoggle",
				Text = () => strings[entry.Label],
				Font = fonts => fonts.Label,
				Help = strings.Help( entry.Help ),
				// Group 0x70c is type 8 style 0x11, so these toggles play 0xBD (the parent's style bit, hook 0x485780 at 0x4857cb).
				ClickSound = table.ClickSoundOf( toggles[index] ),
				Selected = () => chosen == entry.Mode,
				Clicked = () => { chosen = entry.Mode; joyImage.Visible = entry.Mode == GameMode.InstantAction; },
				Bounds = toggles[index].Rect,
				Anchor = UiAnchor.Center
			} );
		}
		// [BIN:TPW-EXE:0x004a6d51 mode tick] the tick (control −1) confirms: the player is made with Instant Action only when toggle 0x70d is selected, otherwise Full Simulation, also with no toggle selected (0x4a6b80)
		var ok = table.Model( "b_okay" );
		var tick = screen.Add( new UiButton
		{
			Id = "ok",
			Model = ok.Model,
			Help = strings.Help( ok.Help ),
			Clicked = () =>
			{
				var mode = chosen == GameMode.InstantAction ? GameMode.InstantAction : GameMode.FullSimulation;
				if ( screen.TryActivate( Stack ) ) { PlayEnterPark( mode == GameMode.InstantAction ); actions.StartPark( Selected, mode ); }
			},
			Bounds = ok.Rect,
			Anchor = UiAnchor.Center
		} );
		var exit = table.Model( "b_exit" );
		screen.Add( new UiButton
		{
			Id = "back",
			Model = exit.Model,
			Help = strings.Help( 2 ),
			Clicked = () => { if ( !screen.IsRemoved && ReferenceEquals( Stack.Top, screen ) ) Stack.Pop(); },
			Bounds = exit.Rect,
			Anchor = UiAnchor.Center
		} );
		screen.Back = () => { if ( !screen.IsRemoved && ReferenceEquals( Stack.Top, screen ) ) Stack.Pop(); };
		// [BIN:TPW-EXE:0x004a6dc1 dialog Enter] Enter in the dialog's name field posts 0x802 (the edit control's key handler 0x667fee: 0x0d → 0x802, Escape → 0x804),
		// which the dialog procedure 0x4a6d00 turns into the tick (0x100 with control −1), whatever toggle has the focus
		screen.Accept = tick.Activate;
		screen.Focus( screen.FocusableElements.First() );
		Stack.Push( screen );
	}

	public void ShowLoad() => Stack.Push( CreateLoadScreen( Stack, strings, actions.LoadEntries(), actions.Load ) );

	/// <summary>The Load Park screen (UITEXT 202), shared with the pause menu: tp.exe's load dialog, table 0x7523f0.</summary>
	// [BIN:TPW-EXE:0x7523f0 load dialog] root window2 (248,30)-(1800,1007), the title 0x23bce1 (746,83)-(1218,162), the f_load list 0x23bce0 (341,203)-(1614,772) with
	// the content (371,229)-(1511,733) and its scroller (1528,285)-(1587,691) with b_up, b_down and b_scroller, b_exit (1671,818) and b_okay (1660,730), and the name field
	// !frame 0x23bce2 (605,811)-(1343,895)
	// [BIN:TPW-EXE:0x49efb0 load dialog build] the load (pause line 1, the lobby's 22) titles the window UITEXT 202 and hides b_okay and the name field (0x65d9dd(0)); the list
	// rows come from the row factory 0x49f0b0, the slot-7 rule of the buy list (font slot 7's line height x 1536 / screen height + 6, [0x7cb24c]), so (733 - 229) / 41 = 12
	// rows show (0x663718); the list has no header sort (attribute 0x81) and keeps the save enumeration's order (0x49ec80); a row's activation (message 0x400) loads it and b_exit
	// closes the dialog (0x49e880)
	// [BIN:TPW-EXE:0x49ec80 save list rows] each row is the park name and the save file's local last-write date (UITEXT 0x1c0 = OpenTPW's 447 with day, month, year, 0x5fc660) plus " %02d:%02d" hour and minute, in a second right-aligned cell (0x49f170)
	/// <summary>" (requires mod X)" after a save's name when the mods it was saved with are not all active, else nothing; the entry stays listed.</summary>
	// [EXT:mods] OpenTPW's own mod system; the original game had no mods
	internal static string RequiresSuffix( UiStringTable strings, string? requires ) =>
		requires == null ? "" : " (" + string.Format( strings.Extra( OpenTpwText.RequiresMod ), requires ) + ")";

	/// <summary>[EXT:cheats-publish] " (cheated)" after the name of a park marked as cheated (docs/CHEATS.md), else nothing.</summary>
	internal static string CheatedSuffix( UiStringTable strings, bool cheated ) => cheated ? " (" + strings.Extra( OpenTpwText.CheatedPark ) + ")" : "";

	// [EXT:list-row-pitch] the rows take the buy list's fixed pitch, a design choice (see ParkHud.BuyRowHeight)
	public static UiScreen CreateLoadScreen( UiScreenStack stack, UiStringTable strings, IReadOnlyList<ParkLoadEntry> entries, Action<ParkLoadEntry> load )
	{
		var screen = new LoadChooserScreen();
		var table = OriginalLayoutTables.LoadDialog;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.LoadPark], Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Fit = true, Bounds = table.Control( 0x23BCE1 ).Rect, Anchor = UiAnchor.Center } );
		var frame = table.Control( 0x23BCE0 );
		var content = frame.Inner!.Value;
		screen.Add( new UiModelImage { Id = "listFrame", Model = frame.Model!, Bounds = frame.Rect, Anchor = UiAnchor.Center } );
		if ( entries.Count == 0 )
			screen.Add( new UiLabel { Id = "empty", Text = () => strings.Extra( OpenTpwText.NoSavedParks ), Align = UiAlign.Center, Bounds = content, Anchor = UiAnchor.Center } );
		var parks = entries.ToArray();
		// [EXT:load-list-select-then-load] the list keeps OpenTPW's select-then-load flow (a first click selects; Enter, a second click or the Load Park button loads) because the Load Park button (EXT:load-chooser) acts on the selection, which a hover would retarget on the way to it; the original's load list (attribute 0x81) selects the row under the pointer and loads on one click (0x6656a0, 0x6655a2); user-approved 2026-10-17
		var list = screen.Add( new UiScrollList
		{
			Id = "parks",
			Rows = () => parks.Select( entry => entry.OwnSavePath != null ? (entry.DisplayName ?? entry.Level) + CheatedSuffix( strings, entry.Cheated ) + RequiresSuffix( strings, entry.Requires ) : entry.Sandbox ? strings.Extra( OpenTpwText.SandboxParkEntry )
				: string.Format( strings.Extra( OpenTpwText.OriginalParkEntry ), entry.ThemeNameIndex >= 0 ? strings.Theme( entry.ThemeNameIndex ) : entry.Level ) ).ToArray(),
			Trailing = index => parks[index].SavedAt is { } saved
				? strings.Format( UiTextRecord.SaveDate, saved.Day.ToString(), saved.Month.ToString(), saved.Year.ToString() ) + $" {saved.Hour:00}:{saved.Minute:00}" : "",
			Help = strings.Help( 318 ),
			Selected = parks.Length == 0 ? -1 : 0,
			Enabled = parks.Length != 0,
			Visible = parks.Length != 0,
			ClicksOnActivate = false,
			// [BIN:TPW-EXE:0x49e901 load row] owner 0x49e880 handles type-7 notification 0x400 by loading, without a button message 0x100 or a dedicated row sound. The separate EXT Load Park button keeps its own click.
			RowActivated = index => { if ( screen.TryBeginActivation() ) load( parks[index] ); },
			RowHeight = OpenTPW.Hud.ParkHud.BuyRowHeight,
			Margin = 0,
			Background = false,
			PositionBar = false,
			Bounds = content,
			Anchor = UiAnchor.Center
		} );
		// [BIN:TPW-EXE:0x664495 list scroller] the scroller and its arrows show only while the rows do not fit
		var slider = table.All.First( node => node.Model2 == "!slider" );
		var knob = table.Model( "b_scroller" );
		bool Scrolls() => parks.Length > list.VisibleRows;
		screen.Add( new LoadScrollBar( list, slider.Rect, knob.Rect, slider.Model2!, knob.Model! ) { Id = "loadScroll", Bounds = slider.Rect, Anchor = UiAnchor.Center, Visible = Scrolls() } );
		var up = table.Model( "b_up" );
		var down = table.Model( "b_down" );
		// The arrows' parent is the type 3 slider (style 0x10) inside the load list, so they play 0xBD and repeat at 500 ms then 125 ms.
		void ScrollArrow( LayoutNode node, int step )
		{
			void Scroll() => list.Scroll( step );
			screen.Add( new UiButton { Id = step < 0 ? "scrollUp" : "scrollDown", Model = node.Model, ClickSound = table.ClickSoundOf( node ),
				Clicked = Scroll, Repeat = UiButton.ScrollArrowRepeat( Scroll ), Bounds = node.Rect, Anchor = UiAnchor.Center, Visible = Scrolls() } );
		}
		ScrollArrow( up, -1 );
		ScrollArrow( down, 1 );
		// [EXT:load-chooser] a text button that loads the selected row, at the place of the name field the load hides; the original loads only by activating a row
		screen.Add( new UiButton
		{
			Id = "loadSelected",
			Text = () => strings[UIStrings.LoadPark],
			Help = strings.Help( 318 ),
			Enabled = parks.Length != 0,
			Clicked = list.Activate,
			Bounds = table.Control( 0x23BCE2 ).Rect,
			Anchor = UiAnchor.Center
		} );
		var exit = table.Model( "b_exit" );
		screen.Add( new UiButton
		{
			Id = "back",
			Model = exit.Model,
			Help = strings.Help( 2 ),
			Clicked = CloseChooser,
			Bounds = exit.Rect,
			Anchor = UiAnchor.Center
		} );
		screen.Back = CloseChooser;
		void CloseChooser()
		{
			if ( !screen.IsRemoved && ReferenceEquals( stack.Top, screen ) ) stack.Pop();
		}
		screen.Focus( screen.FocusableElements.First() );
		return screen;
	}

	/// <summary>The load list's slider: the <c>!slider</c> track and the <c>b_scroller</c> ball at the list's first row (0x66b088, as in the buy list); dragging or clicking it sets the first row.</summary>
	private sealed class LoadScrollBar( UiScrollList list, UiRect track, UiRect knob, string trackModel, string knobModel ) : UiElement, IUiDragTarget
	{
		private int MaxFirstRow => Math.Max( 0, list.Rows().Count - list.VisibleRows );
		public override bool Focusable => Visible && Enabled;

		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			var travel = track.Height - knob.Height;
			if ( rect.Height <= 0 || travel <= 0 || MaxFirstRow == 0 )
				return;
			var authoredY = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var fraction = Math.Clamp( (authoredY - track.Y - knob.Height / 2) / travel, 0, 1 );
			list.Scroll( (int)MathF.Round( fraction * MaxFirstRow ) - list.FirstRow );
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( trackModel, 0, context.Canvas.Map( track, Anchor ) );
			context.DrawModel( knobModel, pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame,
				context.Canvas.Map( OpenTPW.Hud.ParkHud.ListScrollKnobRect( track, knob, list.FirstRow, MaxFirstRow ), Anchor ) );
		}
	}

	/// <summary>Load-only dispatch ownership; other screens keep their existing input policy.</summary>
	private sealed class LoadChooserScreen() : UiScreen( "load" )
	{
		private bool dispatching;
		private bool activated;

		public bool TryBeginActivation()
		{
			if ( IsRemoved || dispatching && activated ) return false;
			if ( dispatching ) activated = true;
			return true;
		}

		public override bool Update( UiContext context, UiInput input )
		{
			if ( IsRemoved ) return true;
			// Cancellation owns a combined input batch before a row or button can load.
			if ( input.Has( UiKeys.Back ) || RightCancels( input ) && Modal )
			{
				Back?.Invoke();
				return true;
			}
			dispatching = true;
			activated = false;
			try { return base.Update( context, input ); }
			finally { dispatching = false; }
		}
	}

	public void ShowOptions()
	{
		if ( actions.CreateOptions != null )
			Stack.Push( actions.CreateOptions( Stack, () => { } ) );
	}

	public void ShowQuit()
	{
		if ( Stack.Top?.Name == "confirmQuit" )
			return;
		Stack.Push( UiDialogs.Message( "confirmQuit", () => strings[UIStrings.ConfirmQuit],
			(() => strings.Value( UIStrings.Yes ), actions.Quit), (() => strings.Value( UIStrings.No ), Stack.Pop) ) );
	}
}
