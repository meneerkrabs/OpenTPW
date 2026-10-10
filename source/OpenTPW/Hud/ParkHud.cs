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
/// (buy/build, information, finance, research, map) with their original tooltips; the original buy window
/// (<c>w_big</c>, Mac buy table 0x4cd94, see <c>ParkHud.BuyWindow.cs</c>); the information arm
/// ("pan_info") for the selected original object; the cash extras (dollar icon, change text, golden keys and tickets);
/// the message area (f_tagl/m/r) and the pause menu. Positions of code-placed buttons inside the info arm and the
/// speed control are OpenTPW approximations.
/// </summary>
public sealed partial class ParkHud
{
	// [APPROX:UI-025] message area: 3 messages, 8 s — evidence needed: binary/capture of the message system
	public const float MessageSeconds = 8f;
	private static readonly UiRect ArmRect = new( 330.5f, 1068.5f, 675.8f, 426.1f );
	private readonly Level level;
	private readonly UiStringTable strings;
	private readonly HudHost host;
	private readonly UiScreen hud;
	private readonly List<(string Text, float Age)> messages = new();
	private readonly Dictionary<string, PreviewIcon?> icons = new();
	private readonly List<UiElement> infoElements = new();
	private string lastLevelMessage = "";
	private BuildItem? pendingItem;
	private OriginalObject? selectedObject;
	private CashChangeTracker cashTracker;
	private readonly List<UiElement> economyElements = new();

	public ParkHud( Level level, UiStringTable strings, IHudParkStatus status, IBuildCatalog catalog, HudHost host )
	{
		this.level = level;
		this.strings = strings;
		this.host = host;
		Status = status;
		Catalog = catalog;
		buy = new BuyListModel( catalog, ItemName, item => Status.PriceOf( item ) ?? item.Cost );
		cashTracker = new CashChangeTracker( status.Money );
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
	public bool InfoArmOpen { get; private set; }
	/// <summary>The cat_ui event of a click in the park view.</summary>
	public const uint ParkViewClickEvent = 0x1F;

	public OriginalObject? SelectedObject => selectedObject;
	/// <summary>True while a menu (pause menu and what it opens) is on top of the HUD; the buy window does not pause.</summary>
	public bool Paused => Stack.Screens.Any( screen => screen != hud && screen != buyScreen );
	public IReadOnlyList<string> Messages => messages.Select( message => message.Text ).ToArray();

	// [DATA:UITEXT.str:448,449] currency prefix; [APPROX:UI-021] ","-grouped digits — evidence needed: locale number format of the original
	public string MoneyText => !Status.HasEconomy ? "" : FormatMoney( Status.Money );
	private string FormatMoney( long amount ) => string.Format( System.Globalization.CultureInfo.InvariantCulture, "{0}{1:#,0}", amount < 0 ? strings[UIStrings.NegativeDollar] : strings[UIStrings.Dollar], Math.Abs( amount ) ).Replace( "  ", " " );

	/// <summary>The balance change shown beside the balance (Mac HUD table control 48): the sum of the changes of the last <see cref="CashChangeTracker.Seconds"/>; 0 when none is shown.</summary>
	public long CashChange => cashTracker.Change;
	public string CashChangeText => CashChange == 0 ? "" : FormatMoney( CashChange );
	public string GoldenTicketsText => Status.HasEconomy ? Status.GoldenTickets.ToString( System.Globalization.CultureInfo.InvariantCulture ) : "";
	public string GoldenKeysText => Status.HasEconomy ? Status.GoldenKeys.ToString( System.Globalization.CultureInfo.InvariantCulture ) : "";
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
		var infoArm = hud.Add( new UiModelImage { Id = "infoArm", Model = "panel", Frame = () => 1, Bounds = ArmRect, Anchor = UiAnchor.BottomLeft, Visible = false } );
		infoElements.Add( infoArm );
		// [DATA:ui.wad:mainpanel,gauge,date,panel MD2 roots] authored HUD rectangles
		hud.Add( new UiModelImage { Id = "mainPanel", Model = "mainpanel", Bounds = new UiRect( 37.1f, 984.2f, 401.7f, 523.1f ), Anchor = UiAnchor.BottomLeft } );
		hud.Add( new UiModelImage { Id = "gauge", Model = "gauge", Bounds = new UiRect( 66.6f, 1080.4f, 96.6f, 264.2f ), Anchor = UiAnchor.BottomLeft, Help = strings.Help( 477 ) } );
		hud.Add( new UiModelImage { Id = "dateDisplay", Model = "date", Bounds = new UiRect( 152.6f, 1043.6f, 251.7f, 77.5f ), Anchor = UiAnchor.BottomLeft, Help = strings.Help( 478 ) } );
		// [DATA:Mac main HUD table 0x4ab38:32,47] date text region (182,1061,383,1103) in black (font slot 3); the cash text
		// (control 47, (258,60,720,260), white font slot 1) sits at the top left of the screen, not in the panel, whose
		// buttons start right below the date box. [APPROX:UI-021] the original resizes/repositions control 47 from the
		// measured font extents and the drawable size (0x156ef4); the table rectangle is used as is; ","-grouped digits.
		hud.Add( new UiLabel { Id = "date", Text = () => DateText, Font = fonts => fonts.Small, Color = new Veldrid.RgbaByte( 0, 0, 0, 255 ), Align = UiAlign.Center, Bounds = Rect( 182, 1061, 383, 1103 ), Anchor = UiAnchor.BottomLeft } );
		hud.Add( new UiLabel { Id = "money", Text = () => MoneyText, Font = fonts => fonts.Balance, Color = UiColors.Text, Align = UiAlign.Left, Bounds = Rect( 258, 60, 720, 260 ), Anchor = UiAnchor.TopLeft, Help = strings.Help( 465 ) } );

		// [DATA:Mac main HUD table 0x4ab38:50,48,49,51-55] cash extras: i_dollar left of the balance, the change text (yellow) with green_up, golden key and ticket counters
		// [APPROX:UI-050] i_dollar frame 1 for a negative balance and green_up frame 1 for a decrease: the two-frame models' state meaning is guessed from their art — evidence needed: capture of the original cash display
		economyElements.Add( hud.Add( new UiModelImage { Id = "dollar", Model = "i_dollar", Frame = () => Status.Money < 0 ? 1 : 0, Bounds = Rect( 37, 58, 242, 262 ), Anchor = UiAnchor.TopLeft } ) );
		economyElements.Add( hud.Add( new UiLabel { Id = "cashChange", Text = () => CashChangeText, Font = fonts => fonts.Balance, Color = UiColors.Highlight, Align = UiAlign.Right, Fit = true, Bounds = Rect( 458, 273, 720, 363 ), Anchor = UiAnchor.TopLeft } ) );
		economyElements.Add( hud.Add( new UiModelImage { Id = "cashTrend", Model = "green_up", Frame = () => CashChange < 0 ? 1 : 0, Bounds = Rect( 728, 109, 831, 211 ), Anchor = UiAnchor.TopLeft } ) );
		economyElements.Add( hud.Add( new UiModelImage { Id = "goldenKey", Model = "gkey", Bounds = Rect( 1853, 165, 1955, 268 ), Anchor = UiAnchor.TopRight } ) );
		economyElements.Add( hud.Add( new UiModelImage { Id = "goldenTicket", Model = "gtick", Bounds = Rect( 1853, 54, 1955, 156 ), Anchor = UiAnchor.TopRight } ) );
		economyElements.Add( hud.Add( new UiLabel { Id = "goldenKeys", Text = () => GoldenKeysText, Font = fonts => fonts.Balance, Align = UiAlign.Right, Fit = true, Bounds = Rect( 1688, 171, 1829, 261 ), Anchor = UiAnchor.TopRight } ) );
		economyElements.Add( hud.Add( new UiLabel { Id = "goldenTickets", Text = () => GoldenTicketsText, Font = fonts => fonts.Balance, Align = UiAlign.Right, Fit = true, Bounds = Rect( 1688, 60, 1829, 150 ), Anchor = UiAnchor.TopRight } ) );

		// [DATA:Mac main HUD table 0x4ab38:38-43] original button rectangles: staggered along the panel's curve, plus the camera button
		PanelButton( "buy", "b_buy", Rect( 170, 1122, 288, 1240 ), 469, level.IsReadOnlyVisit ? null : OpenBuyWindow );
		PanelButton( "info", "b_info", Rect( 287, 1133, 405, 1252 ), 470, null );
		PanelButton( "finance", "b_money", Rect( 156, 1241, 274, 1360 ), 471, null );
		PanelButton( "research", "b_resrch", Rect( 274, 1254, 392, 1372 ), 472, null );
		PanelButton( "map", "b_map", Rect( 81, 1340, 200, 1458 ), 473, null );
		// [APPROX:UI-042] the camera button's action (camera views) is not implemented; it is shown disabled
		PanelButton( "camera", "b_camera", Rect( 202, 1355, 320, 1474 ), null, null );

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
		hud.Back = OpenPauseMenu;
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

	public string ItemName( BuildItem item ) => item.Entry == null ? strings.Object( item.ObjectNameIndex ) : EntryName( item.Entry );

	private string EntryName( ObjectCatalogEntry entry ) => entry.ObjectNameIndex is int index
		? string.Join( " ", Enumerable.Range( index, Math.Max( 1, entry.ObjectNameLength ) ).Select( strings.Object ).Where( text => text.Length > 0 ) )
		: entry.DisplayName;


	public void SetInfoArm( bool open )
	{
		InfoArmOpen = open && selectedObject is { IsDeleted: false };
		if ( InfoArmOpen )
			CloseBuyWindow();
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
		pendingItem = item;
		level.BuildEntry = entry;
		level.IsRemovingObjects = false;
		level.IsPlacing = false;
		CloseBuyWindow();
		PostMessage( strings.Help( 440 ) );
	}

	// [APPROX:UI-031] one placement per menu selection; Level owns purchase/sale — evidence needed: original build-tool continuation
	private void OnObjectPlaced( OriginalObject item )
	{
		if ( pendingItem?.Entry != item.Entry )
			return;
		// Level.PlaceObject owns the purchase and guest link; this callback only finishes the HUD tool.
		PostMessage( string.Format( strings.Extra( OpenTpwText.Built ), ItemName( pendingItem ) ) );
		pendingItem = null;
		level.IsPlacing = false;
		level.BuildEntry = null;
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
		foreach ( var element in infoElements )
			element.Visible = InfoArmOpen && selectedObject is { IsDeleted: false };
		if ( selectedObject is not { IsDeleted: false } && InfoArmOpen )
			InfoArmOpen = false;

		if ( hud.Find( "openRide" ) is UiButton openButton )
			openButton.Enabled = !level.IsReadOnlyVisit && SelectedInfo?.CanOpen == true;
		if ( hud.Find( "deleteRide" ) is UiButton deleteButton )
			deleteButton.Enabled = !level.IsReadOnlyVisit && selectedObject is { IsDeleted: false } item && !item.Entry.IsFixedItem;

		TrackCashChange( context.Delta );
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

		// [APPROX:UI-027] Select by occupied grid cell; original cursor picking is not verified.
		if ( !overUi && !consumed && level.BuildEntry == null && !level.IsPlacing && input.LeftReleased
			&& level.TryGetGridCell( input.Mouse, new NVector2( context.Canvas.Width, context.Canvas.Height ), out var x, out var y ) )
			SelectObject( level.Objects.FindAt( x, y ) );

		for ( var index = messages.Count - 1; index >= 0; index-- )
		{
			messages[index] = (messages[index].Text, messages[index].Age + context.Delta);
			if ( messages[index].Age > MessageSeconds )
				messages.RemoveAt( index );
		}
		Status.Update( context.Delta );
		return overUi || consumed;
	}

	private void TrackCashChange( float delta )
	{
		foreach ( var element in economyElements )
			element.Visible = Status.HasEconomy;
		cashTracker.Update( Status.Money, delta );
		if ( hud.Find( "cashChange" ) is { } text )
			text.Visible = Status.HasEconomy && CashChange != 0;
		if ( hud.Find( "cashTrend" ) is { } arrow )
			arrow.Visible = Status.HasEconomy && CashChange != 0;
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
		// The message area is replaced by the info arm where both would overlap.
		if ( InfoArmOpen )
			hud.DrawOverlay = null;
		else
			hud.DrawOverlay = DrawMessages;
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
}
