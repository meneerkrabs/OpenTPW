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
}

/// <summary>
/// The in-game HUD in the original style (docs/UI.md): the green main panel (mainpanel.MD2) with the
/// date display, happiness gauge and bank balance at their authored places; the panel buttons
/// (buy/build, information, finance, research, map) with their original tooltips; the build arm
/// (panel.MD2 "pan_buy") with the four category buttons and preview-model icons; the information arm
/// ("pan_info") for the selected ride; the message area (f_tagl/m/r) and the pause menu. Positions of
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
	private bool hadRide;

	public ParkHud( Level level, UiStringTable strings, IHudParkStatus status, IBuildCatalog catalog, HudHost host )
	{
		this.level = level;
		this.strings = strings;
		this.host = host;
		Status = status;
		Catalog = catalog;
		lastLevelMessage = level.LastActionMessage;
		hadRide = level.PlacedRide != null;
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
	public bool Paused => Stack.Screens.Count > 1;
	public IReadOnlyList<string> Messages => messages.Select( message => message.Text ).ToArray();

	// [DATA:UITEXT.str:448,449] currency prefix; [APPROX:UI-021] ","-grouped digits — evidence needed: locale number format of the original
	public string MoneyText => string.Format( System.Globalization.CultureInfo.InvariantCulture, "{0}{1:#,0}", Status.Money < 0 ? strings[UIStrings.NegativeDollar] : strings[UIStrings.Dollar], Math.Abs( Status.Money ) ).Replace( "  ", " " );
	public string DateText => string.Format( strings.Extra( OpenTpwText.DateFormat ), Status.Date.Year, Status.Date.Month, Status.Date.Day );

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
		PanelButton( "buy", "b_buy", 235, 1240, 469, ToggleBuildArm ).Selected = () => BuildArmOpen;
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
				Clicked = () => Category = entry.Category,
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
			Bounds = new UiRect( 480, 1185, 500, 50 ),
			Anchor = UiAnchor.BottomLeft,
			Visible = false
		} ) );
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
			Text = () => SelectedInfo == null ? "" : strings.Object( SelectedInfo.ObjectNameIndex ),
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
			Selected = () => level.PlacedRide?.IsOpen == false,
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


	public ObjectInfo? SelectedInfo => InfoArmOpen && level.PlacedRide is { } ride
		? Describe( ride, Catalog.GetItems( BuildCategory.Rides ).FirstOrDefault( item => item.Id == TotemBuildCatalog.ItemId ) ) : null;

	/// <summary>
	/// Info for the placed prototype ride. Excitement is the original <c>UsageInfo.ExcitementLevel</c>
	/// default (70 for the Totem, from Totem.sam); reliability, repair and life are not simulated yet.
	/// </summary>
	public static ObjectInfo Describe( PrototypeRide ride, BuildItem? item ) => new( TotemBuildCatalog.ObjectNameIndex, ride.IsOpen, true, new[]
	{
		// [APPROX:UI-028] excitement shown as "<ExcitementLevel>%" — evidence needed: capture of the original ride info panel
		new ObjectStat( UIStrings.Excitement, item?.DefaultExcitement is int excitement ? $"{excitement}%" : null ),
		new ObjectStat( UIStrings.Reliability, null ),
		new ObjectStat( UIStrings.StateOfRepair, null ),
		new ObjectStat( UIStrings.RemainingLife, null ),
	} );

	public void ToggleBuildArm() => SetBuildArm( !BuildArmOpen );

	public void SetBuildArm( bool open )
	{
		BuildArmOpen = open;
		if ( open )
			InfoArmOpen = false;
		if ( !open && pendingItem != null )
		{
			pendingItem = null;
			level.IsPlacing = false;
		}
	}

	public void SetInfoArm( bool open )
	{
		InfoArmOpen = open && level.PlacedRide != null;
		if ( InfoArmOpen )
			BuildArmOpen = false;
	}

	/// <summary>Starts placing a catalog item (the click on the ground is handled by <see cref="Level"/>).</summary>
	public void BeginPlacing( BuildItem item )
	{
		if ( level.PlacedRide != null )
		{
			PostMessage( strings.Extra( OpenTpwText.OnlyOnePrototypeRide ) );
			return;
		}
		if ( item.Cost > Status.Money )
		{
			PostMessage( strings.Help( 152 ) );
			return;
		}
		pendingItem = item;
		level.IsPlacing = true;
		PostMessage( strings.Help( 440 ) );
	}

	private void ToggleRideOpen()
	{
		if ( level.PlacedRide is not { } ride )
			return;
		if ( ride.IsOpen )
			ride.Stop();
		else
			ride.Start();
	}

	private void DeleteRide()
	{
		level.RemoveRide();
		SetInfoArm( false );
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
			(UIStrings.ExitToLobby, host.ExitToLobby),
			(UIStrings.QuitGame, () => Stack.Push( UiDialogs.Message( "confirmQuit", () => strings[UIStrings.ConfirmQuit],
				(() => strings.Value( UIStrings.Yes ), host.Quit), (() => strings.Value( UIStrings.No ), Stack.Pop) ) )),
		};
		for ( var index = 0; index < entries.Count; index++ )
		{
			var entry = entries[index];
			menu.Add( new UiButton
			{
				Id = entry.Label.ToString(),
				Text = () => strings[entry.Label],
				Clicked = entry.Clicked,
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
			element.Visible = InfoArmOpen && level.PlacedRide != null;
		if ( level.PlacedRide == null && InfoArmOpen )
			InfoArmOpen = false;

		var paused = Paused;
		var overUi = Stack.Covers( context.Canvas, input.Mouse ) || Stack.Screens.Count > 1;
		var consumed = Stack.Update( context, input );
		if ( !paused && input.Has( UiKeys.Pause ) )
			Status.Speed = Status.Speed == GameSpeed.Paused ? GameSpeed.Normal : GameSpeed.Paused;

		// [APPROX:UI-031] charge on appearance; one prototype ride — evidence needed: economy/catalog slices
		// Purchases: a ride appearing while an item is pending is charged; failures are reported.
		var hasRide = level.PlacedRide != null;
		if ( hasRide && !hadRide && pendingItem != null )
		{
			if ( Status.TrySpend( pendingItem.Cost ) )
				PostMessage( string.Format( strings.Extra( OpenTpwText.Built ), strings.Object( pendingItem.ObjectNameIndex ) ) );
			pendingItem = null;
			SetBuildArm( false );
		}
		hadRide = hasRide;
		if ( level.LastActionMessage != lastLevelMessage )
		{
			lastLevelMessage = level.LastActionMessage;
			if ( lastLevelMessage.StartsWith( "Cannot build", StringComparison.Ordinal ) )
				PostMessage( strings.Extra( OpenTpwText.CannotBuildHere ) );
		}

		// Selecting the placed ride: left click on the park near it while not placing.
		if ( !overUi && !consumed && !level.IsPlacing && input.LeftReleased && level.PlacedRide is { } ride
			&& level.TryGetPlacementPosition( new Vector2( input.Mouse.X, input.Mouse.Y ), new Vector2( context.Canvas.Width, context.Canvas.Height ), out var position ) )
		{
			var dx = position.X - ride.Position.X;
			var dy = position.Y - ride.Position.Y;
			SetInfoArm( dx * dx + dy * dy <= SelectionRadius * SelectionRadius );
		}

		for ( var index = messages.Count - 1; index >= 0; index-- )
		{
			messages[index] = (messages[index].Text, messages[index].Age + context.Delta);
			if ( messages[index].Age > MessageSeconds )
				messages.RemoveAt( index );
		}
		Status.Update( context.Delta );
		return overUi || consumed;
	}

	/// <summary>Click distance (engine units) that selects the prototype ride: its footprint plus one unit.</summary>
	// [APPROX:UI-027] selection radius = footprint + 1 — evidence needed: binary picking
	public const float SelectionRadius = PrototypeRide.FootprintRadius + 1;

	/// <summary>Selects the placed ride as if it had been clicked (smoke test, keyboard).</summary>
	public void SelectPlacedRide() => SetInfoArm( true );

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
				var items = owner.Catalog.GetItems( owner.Category );
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
			var iconRect = new UiRect( rect.X, rect.Y, rect.Width, rect.Width );
			context.Batch.AddRectangle( iconRect, focused || owner.pendingItem == item ? new RgbaByte( 255, 230, 70, 90 ) : new RgbaByte( 0, 0, 40, 90 ) );
			// [APPROX:UI-026] icon turn speed 0.8 rad/s — evidence needed: capture of the original build menu
			owner.GetIcon( item )?.Draw( context.Batch, iconRect.Inflate( -rect.Width * 0.06f ), context.Time * 0.8f );
			var name = owner.strings.Object( item.ObjectNameIndex );
			var price = string.Format( System.Globalization.CultureInfo.InvariantCulture, "{0}{1:#,0}", owner.strings[UIStrings.Dollar], item.Cost ).Replace( "  ", " " );
			var textTop = iconRect.Bottom + 2;
			context.DrawText( context.Fonts.Small, name, new UiRect( rect.X, textTop, rect.Width, (rect.Bottom - textTop) / 2 ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
			context.DrawText( context.Fonts.Small, price, new UiRect( rect.X, textTop + (rect.Bottom - textTop) / 2, rect.Width, (rect.Bottom - textTop) / 2 ), UiColors.Value, UiAlign.Center );
		}
	}
}
