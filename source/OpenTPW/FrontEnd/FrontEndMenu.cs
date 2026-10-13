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
/// f_lobbutbg button background) holding the island name and the previous/enter/next island buttons
/// with their original tooltips, a title, and Load / Options / Quit Game buttons. Entering a park asks
/// for continuation when an own save is available, or for the new park mode. Player profiles are not implemented. Positions of
/// code-placed elements are approximations (docs/UI.md).
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
		// [DATA:ui.wad:islandlobby.MD2,f_lobbutbg.MD2 root/bounds] authored panel rectangles
		screen.Add( new UiModelImage { Id = "lobbyPanel", Model = "islandlobby", Bounds = new UiRect( 27.7f, 855.1f, 447.8f, 665.8f ), Anchor = UiAnchor.BottomLeft } );
		screen.Add( new UiModelImage { Id = "lobbyButtons", Model = "f_lobbutbg", Bounds = new UiRect( 66.9f, 1272.7f, 371.2f, 211.2f ), Anchor = UiAnchor.BottomLeft } );
		// [APPROX:UI-014] island name and prev/enter/next button positions in the lobby panel — evidence needed: capture of the original lobby
		screen.Add( new UiLabel { Id = "islandName", Text = () => SelectedName, Font = fonts => fonts.Label, Color = UiColors.Title, Align = UiAlign.Center, Bounds = new UiRect( 90, 1282, 326, 70 ), Anchor = UiAnchor.BottomLeft } );
		UiButton IslandButton( string id, string model, float x, int help, Action clicked ) => screen.Add( new UiButton
		{
			Id = id,
			Model = model,
			Help = strings.Help( help ),
			Clicked = clicked,
			Adjusted = NextIsland,
			Bounds = UiRect.FromCenter( new NVector2( x, 1412 ), 112, 112 ),
			Anchor = UiAnchor.BottomLeft
		} );
		IslandButton( "previousIsland", "b_lobleft", 135, 343, () => NextIsland( -1 ) );
		var enter = IslandButton( "enterPark", "b_entpark", 252, 345, ShowThemeStart );
		IslandButton( "nextIsland", "b_lobright", 369, 344, () => NextIsland( 1 ) );

		// [APPROX:UI-014] logo/title placement and the right-hand Load/Options/Quit column — evidence needed: capture of the original lobby
		screen.Add( new UiModelImage { Id = "logo", Model = "tpwlogo", ArtOverride = "tpw_logo", Bounds = new UiRect( 819, 40, 410, 154 ), Anchor = UiAnchor.Top } );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.ThemeParkWorld], Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Bounds = new UiRect( 624, 196, 800, 90 ), Anchor = UiAnchor.Top } );

		var menu = new (string Id, UIStrings Label, Action Clicked)[]
		{
			("goOnline", UIStrings.GoOnline, () => actions.GoOnline?.Invoke( Stack )),
			("load", UIStrings.Load, ShowLoad),
			("options", UIStrings.Options, ShowOptions),
			("quit", UIStrings.QuitGame, ShowQuit),
		};
		var shown = actions.GoOnline == null ? menu.Where( entry => entry.Id != "goOnline" ).ToArray() : menu;
		for ( var index = 0; index < shown.Length; index++ )
		{
			var entry = shown[index];
			screen.Add( new UiButton
			{
				Id = entry.Id,
				Text = () => strings[entry.Label],
				Clicked = entry.Clicked,
				Adjusted = NextIsland,
				Bounds = new UiRect( 1500, 80 + index * 140, 480, 116 ),
				Anchor = UiAnchor.TopRight
			} );
		}
		screen.Back = ShowQuit;
		screen.Focus( enter );
		return screen;
	}

	// [APPROX:UI-015] Game Mode asked on every park entry and each entry starts a new park (no player profiles or per-theme autosave); the original asks once when a player is created and stores it in the profile — evidence needed: PC confirmation of the Mac player/autosave flow
	public void ShowGameMode()
	{
		var screen = new UiScreen( "gameMode" );
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
				Clicked = () => actions.StartPark( Selected, entry.Mode ),
				Bounds = new UiRect( window.X + 150, window.Y + 240 + index * 150, window.Width - 450, 120 ),
				Anchor = UiAnchor.Center
			} );
		}
		screen.Add( new UiButton
		{
			Id = "back",
			Text = () => strings.Extra( OpenTpwText.Back ),
			Help = strings.Help( 2 ),
			Clicked = Stack.Pop,
			Bounds = new UiRect( window.X + 150, window.Bottom - 190, 420, 104 ),
			Anchor = UiAnchor.Center
		} );
		screen.Back = Stack.Pop;
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
		// [APPROX:UI-934] eight-row scroll viewport and selection/activation controls are host UI; native Load navigation is not qualified.
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
			RowActivated = index => { if ( screen.TryBeginActivation() ) load( parks[index] ); },
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
			if ( input.Has( UiKeys.Back ) || input.RightPressed && Modal )
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
