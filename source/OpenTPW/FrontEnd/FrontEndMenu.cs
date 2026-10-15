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
public sealed record ParkLoadEntry( string Level, int ThemeNameIndex, bool Sandbox, string? OwnSavePath = null, string? DisplayName = null );

/// <summary>What the front-end screens ask of the game.</summary>
public sealed class FrontEndActions
{
	public Action<LobbyIslandInfo, GameMode> StartPark { get; init; } = ( _, _ ) => { };
	public Action<ParkLoadEntry> Load { get; init; } = _ => { };
	/// <summary>Lightweight own-save availability for the selected theme; assets are checked only on actual resume.</summary>
	public Func<string, ParkContinueEntry?> ContinueEntry { get; init; } = _ => null;
	public Action<ParkContinueEntry>? ContinuePark { get; init; }
	internal Func<string, AutomaticParkContinueEntry?> AutomaticEntry { get; init; } = _ => null;
	internal Action<AutomaticParkContinueEntry>? ContinueAutomatic { get; init; }
	public Action Quit { get; init; } = () => { };
	public Func<UiScreenStack, Action, UiScreen>? CreateOptions { get; init; }
	/// <summary>Called when the selected island changes (moves the lobby camera).</summary>
	public Action<LobbyIslandInfo> IslandSelected { get; init; } = _ => { };
	public Func<IReadOnlyList<ParkLoadEntry>> LoadEntries { get; init; } = () => Array.Empty<ParkLoadEntry>();
	/// <summary>Opens the online screens on the menu's stack (Go Online, UITEXT 1); null hides the button.</summary>
	public Action<UiScreenStack>? GoOnline { get; init; }
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
		var window = UiDialogs.CenteredWindow( 1100, 760 );
		UiDialogs.AddWindow( screen, window, "w_med", () => strings[UIStrings.GameMode] );
		screen.Add( new UiLabel { Id = "park", Text = () => SelectedName, Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Center, Bounds = new UiRect( window.X + 80, window.Y + 140, window.Width - 300, 70 ), Anchor = UiAnchor.Center } );
		var modes = new (string Id, UIStrings Label, GameMode Mode, int Help)[]
		{
			("instantAction", UIStrings.InstantAction, GameMode.InstantAction, 358),
			("fullSimulation", UIStrings.FullSimulation, GameMode.FullSimulation, 359),
		};
		for ( var index = 0; index < modes.Length; index++ )
		{
			var entry = modes[index];
			screen.Add( new UiButton
			{
				Id = entry.Id,
				Text = () => strings[entry.Label],
				Help = strings.Help( entry.Help ),
				Clicked = () => { if ( screen.TryActivate( Stack ) ) { PlayEnterPark( entry.Mode == GameMode.InstantAction ); actions.StartPark( Selected, entry.Mode ); } },
				Bounds = new UiRect( window.X + 150, window.Y + 240 + index * 150, window.Width - 450, 120 ),
				Anchor = UiAnchor.Center
			} );
		}
		screen.Add( new UiButton
		{
			Id = "back",
			Text = () => strings.Extra( OpenTpwText.Back ),
			Help = strings.Help( 2 ),
			Clicked = () => { if ( !screen.IsRemoved && ReferenceEquals( Stack.Top, screen ) ) Stack.Pop(); },
			Bounds = new UiRect( window.X + 150, window.Bottom - 190, 420, 104 ),
			Anchor = UiAnchor.Center
		} );
		screen.Back = () => { if ( !screen.IsRemoved && ReferenceEquals( Stack.Top, screen ) ) Stack.Pop(); };
		screen.Focus( screen.FocusableElements.First() );
		Stack.Push( screen );
	}

	public void ShowLoad() => Stack.Push( CreateLoadScreen( Stack, strings, actions.LoadEntries(), actions.Load ) );

	/// <summary>The Load Park screen (UITEXT 202), shared with the pause menu.</summary>
	public static UiScreen CreateLoadScreen( UiScreenStack stack, UiStringTable strings, IReadOnlyList<ParkLoadEntry> entries, Action<ParkLoadEntry> load )
	{
		var screen = new LoadChooserScreen();
		var window = UiDialogs.CenteredWindow( 1500, 1100 );
		UiDialogs.AddWindow( screen, window, "w_med", () => strings[UIStrings.LoadPark] );
		if ( entries.Count == 0 )
			screen.Add( new UiLabel { Id = "empty", Text = () => strings.Extra( OpenTpwText.NoSavedParks ), Align = UiAlign.Center, Bounds = new UiRect( window.X + 100, window.Y + 200, window.Width - 400, 80 ), Anchor = UiAnchor.Center } );
		// [EXT:load-chooser] the eight-row scroll viewport and the selection/activation controls are host UI; there is no original rule to match
		var parks = entries.ToArray();
		var list = screen.Add( new UiScrollList
		{
			Id = "parks",
			Rows = () => parks.Select( entry => entry.OwnSavePath != null ? entry.DisplayName ?? entry.Level : entry.Sandbox ? strings.Extra( OpenTpwText.SandboxParkEntry )
				: string.Format( strings.Extra( OpenTpwText.OriginalParkEntry ), entry.ThemeNameIndex >= 0 ? strings.Theme( entry.ThemeNameIndex ) : entry.Level ) ).ToArray(),
			Help = strings.Help( 318 ),
			Selected = parks.Length == 0 ? -1 : 0,
			Enabled = parks.Length != 0,
			Visible = parks.Length != 0,
			ClicksOnActivate = false,
			RowActivated = index => { if ( screen.TryBeginActivation() ) { GameAudio.PlayUi( GameAudio.ClickEvent ); load( parks[index] ); } },
			RowHeight = 90,
			Margin = 10,
			Bounds = new UiRect( window.X + 100, window.Y + 170, window.Width - 360, 740 ),
			Anchor = UiAnchor.Center
		} );
		screen.Add( new UiButton
		{
			Id = "loadSelected",
			Text = () => strings[UIStrings.LoadPark],
			Help = strings.Help( 318 ),
			Enabled = parks.Length != 0,
			Clicked = list.Activate,
			Bounds = new UiRect( window.X + 620, window.Bottom - 190, 420, 104 ),
			Anchor = UiAnchor.Center
		} );
		screen.Add( new UiButton
		{
			Id = "back",
			Text = () => strings.Extra( OpenTpwText.Back ),
			Help = strings.Help( 2 ),
			Clicked = CloseChooser,
			Bounds = new UiRect( window.X + 120, window.Bottom - 190, 420, 104 ),
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
