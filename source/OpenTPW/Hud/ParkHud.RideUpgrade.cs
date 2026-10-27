using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The ride window's upgrade list, built from tp.exe's table 0x755750 (<c>0x004AE560</c>): the wdialogw frame, the title, the upgrades list with its scroll bar and the exit
/// button. Its rows are the ride's add-ons, its next level or the cancel row (<see cref="Level.UpgradeOffer"/>); a click on an add-on arms the build tool with it, a click on the
/// level asks for the upgrade and the cancel row cancels it.
/// </summary>
// [BIN:TPW-EXE:0x004AE560 upgrade list window] the b_upgrade click builds table 0x755750 with handler 0x004AE430 once (the window word +0x24), titles 0x1F4E with UITEXT 0x1F (31, Buy Upgrades), gives list 0x1F4D the row cells 0x004AE700/0x004AE7D0 (a left-aligned name and a right-aligned price in font slot 7, the rows as high as the buy list's, 0x004AE700) and the hilight bar, and fills it (0x004AE140); the ride window's refresh fills it again while it is open (0x004ADE40)
// [BIN:TPW-EXE:0x004AE430 upgrade list click] message 0x400 (a row clicked) ignores a line of the message (−7890); −12999 cancels the upgrade (0x004AF480(0, 1) → 0x004E0050(0)); any other value goes to 0x004AF480(value, 0): an add-on whose price is at most the balance arms the build tool and closes the ride window (0x0048CF70), a dearer one plays the error cue 0x1D (0x00486AE0), a level is requested (0x004E0050(1)) when its price is below the balance; every click but an armed add-on closes the list and refreshes the ride window; message 0x100 with −2 (b_exit) closes it, message 0x14 clears the window word
// [BIN:TPW-EXE:0x004AD6B4 upgrade button] the ride window's refresh enables b_upgrade (0x3E35) for every ride whose window is not in mode 3
public sealed partial class ParkHud
{
	private static LayoutTable UpgradeTable => OriginalLayoutTables.RideUpgradeDialog;
	private static LayoutNode UpgradeListNode => UpgradeTable.Control( 0x1F4D );
	/// <summary>The rows' region: the list control's inner rectangle.</summary>
	private static UiRect UpgradeContent => UpgradeListNode.Inner!.Value;
	private static LayoutNode UpgradeSlider => UpgradeListNode.Children.Single( node => node.Type == 3 );
	private static UiRect UpgradeTrack => UpgradeSlider.Rect;
	private static UiRect UpgradeKnob => UpgradeSlider.Children.Single( node => node.Model == "b_scroller" ).Rect;
	/// <summary>The rows the list shows at once, at the buy list's pitch (its rows come from the same font slot and formula).</summary>
	public static int UpgradeVisibleRows => (int)(UpgradeContent.Height / BuyRowHeight);

	private UiButton? rideUpgradeButton;
	private bool upgradeMouseGestureBlocked;
	private UiScreen? upgradeScreen;
	private OriginalObject? upgradeOwner;
	private int upgradeSelected = -1;
	private int upgradeFirstRow;

	/// <summary>Whether the ride window's upgrade list is open.</summary>
	public bool RideUpgradeOpen => upgradeScreen != null && Stack.Screens.Contains( upgradeScreen );

	/// <summary>One shown row of the upgrade list: its name and price cells and the offer's row (null for a line of the message).</summary>
	internal readonly record struct UpgradeListRow( string Text, string Price, RideUpgradeRow? Row );

	private void AddRideUpgradeButton()
	{
		// [BIN:TPW-EXE:0x755150 b_upgrade] the ride window's upgrade button is the round b_upgrade at (1271,800)-(1425,953), help 19; ParkHud.ObjectWindow.cs places it
		rideUpgradeButton = hud.Add( new UiButton { Id = "rideUpgrade", Model = "b_upgrade", Clicked = OpenRideUpgrade,
			Bounds = new UiRect( 1271, 800, 154, 153 ), Anchor = UiAnchor.Center,
			Visible = false, Enabled = false } );
		infoElements.Add( rideUpgradeButton );
	}

	/// <summary>Whether <paramref name="owner"/> is a ride of this park whose upgrade list the window may open: Theme Park World, a park economy, a ride's economy record.</summary>
	private bool IsUpgradeListTarget( OriginalObject? owner ) =>
		GameEditions.Current == GameEdition.ThemeParkWorld && !level.IsReadOnlyVisit && owner is not (null or PrototypeRide) && !owner.IsDeleted
		&& FinanceEconomy is { } economy && ReferenceEquals( level.Park?.Economy, economy ) && level.GetEconomyInstance( owner ) is int instance
		&& economy.TryGetObject( instance, out var item ) && item.InfoId == owner.Entry.InfoId && item.Kind == ParkObjectKind.Ride;

	private bool UpgradeInputCaptured()
	{
		if ( shopPriceMouseCapture is bool mouse && shopPriceKeyboardCapture is bool keyboard && shopPriceTextCapture is bool text )
			return mouse || keyboard || text;
		if ( shopPriceMouseCapture == true || shopPriceKeyboardCapture == true || shopPriceTextCapture == true ) return true;
		// Native capture is sampled only for an actual action/gesture; no-context callers cannot activate it.
		return ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero || PriceInputCaptured();
	}

	private void UpdateRideUpgradeButton( UiInput input )
	{
		if ( rideUpgradeButton == null ) return;
		// a condemned ride's window hides its upgrade button (0x4ad4e0, ApplyObjectStatus)
		rideUpgradeButton.Visible = InfoArmOpen && IsUpgradeListTarget( selectedObject ) && !objectCondemned;
		if ( (input.LeftDown || input.LeftPressed || input.LeftReleased)
			&& (rideUpgradeButton.Visible || Stack.Top is RideUpgradeScreen) && UpgradeInputCaptured() )
			upgradeMouseGestureBlocked = true;
		else if ( input.LeftPressed || (!input.LeftDown && !input.LeftReleased) )
			upgradeMouseGestureBlocked = false;
		rideUpgradeButton.Enabled = rideUpgradeButton.Visible && !upgradeMouseGestureBlocked && PriceParkInputAllowed
			&& shopPriceMouseCapture != true && shopPriceKeyboardCapture != true && shopPriceTextCapture != true;
	}

	/// <summary>The rows of the open list: the offer's rows with their name and price, or the lines of the message it shows instead.</summary>
	internal IReadOnlyList<UpgradeListRow> UpgradeListRows()
	{
		if ( upgradeOwner is not { IsDeleted: false } owner )
			return Array.Empty<UpgradeListRow>();
		var offer = level.UpgradeOffer( owner );
		if ( offer.Message is { } message )
			return strings[message].Split( '\n' ).Where( line => line.Length > 0 ).Select( line => new UpgradeListRow( line, "", null ) ).ToArray();
		return offer.Rows.Select( row => new UpgradeListRow( row.Kind switch
		{
			RideUpgradeRowKind.AddOn => EntryName( row.AddOn! ),
			RideUpgradeRowKind.Level => strings.Format( UIStrings.UpgradeToLevel, (row.Level + 1).ToString( System.Globalization.CultureInfo.InvariantCulture ) ),
			_ => strings.Format( UIStrings.CancelLevel, (row.Level + 1).ToString( System.Globalization.CultureInfo.InvariantCulture ) )
		}, row.Price > 0 ? FormatMoney( row.Price ) : "", (RideUpgradeRow?)row ) ).ToArray();
	}

	private int UpgradeMaxFirstRow => Math.Max( 0, UpgradeListRows().Count - UpgradeVisibleRows );
	private void ScrollUpgrades( int delta ) => upgradeFirstRow = Math.Clamp( upgradeFirstRow + delta, 0, UpgradeMaxFirstRow );
	private void SetUpgradeFirstRow( int first ) => upgradeFirstRow = Math.Clamp( first, 0, UpgradeMaxFirstRow );
	private void SelectUpgrade( int index )
	{
		var count = UpgradeListRows().Count;
		upgradeSelected = count == 0 ? -1 : Math.Clamp( index, 0, count - 1 );
		if ( upgradeSelected < upgradeFirstRow ) upgradeFirstRow = upgradeSelected;
		if ( upgradeSelected >= upgradeFirstRow + UpgradeVisibleRows ) upgradeFirstRow = upgradeSelected - UpgradeVisibleRows + 1;
		upgradeFirstRow = Math.Clamp( upgradeFirstRow, 0, UpgradeMaxFirstRow );
	}

	private void OpenRideUpgrade()
	{
		if ( !InfoArmOpen || !PriceParkInputAllowed || upgradeMouseGestureBlocked || Stack.Top != hud || UpgradeInputCaptured()
			|| !IsUpgradeListTarget( selectedObject ) || RideUpgradeOpen ) return;
		upgradeOwner = selectedObject;
		upgradeScreen ??= CreateUpgradeScreen();
		upgradeFirstRow = 0;
		// [BIN:TPW-EXE:0x66403b first row] the list's first added row is chosen, so a filled list opens with its top row highlighted
		upgradeSelected = UpgradeListRows().Count > 0 ? 0 : -1;
		Stack.Push( upgradeScreen );
		upgradeScreen.Focus( upgradeScreen.Find( "upgradeList" ) );
	}

	private void CloseRideUpgrade()
	{
		if ( upgradeScreen == null || !RideUpgradeOpen )
			return;
		while ( Stack.Top != upgradeScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == upgradeScreen )
			Stack.Pop();
		upgradeOwner = null;
	}

	/// <summary>Whether the open list still belongs to the ride window it was opened from: the window shows that ride, which may still open the list.</summary>
	private bool UpgradeListCurrent => upgradeOwner is { IsDeleted: false } owner && InfoArmOpen && ReferenceEquals( selectedObject, owner ) && PriceViewAllowed && IsUpgradeListTarget( owner );

	/// <summary>A click on a row of the list (message 0x400): what the row does, then the list closes unless an add-on armed the build tool.</summary>
	private void ActivateUpgrade()
	{
		if ( Stack.Top != upgradeScreen || upgradeMouseGestureBlocked || UpgradeInputCaptured() )
			return;
		if ( !UpgradeListCurrent )
		{
			CloseRideUpgrade();
			return;
		}
		var rows = UpgradeListRows();
		if ( upgradeSelected < 0 || upgradeSelected >= rows.Count || rows[upgradeSelected].Row is not { } row || upgradeOwner is not { IsDeleted: false } owner
			|| !IsUpgradeListTarget( owner ) || FinanceEconomy is not { } economy || level.GetEconomyInstance( owner ) == null )
			return;
		switch ( row.Kind )
		{
			case RideUpgradeRowKind.AddOn:
				if ( row.Price <= economy.Balance && row.AddOn is { } entry && BeginAddOnPlacement( entry, owner, row.Price ) )
					return;
				// [BIN:TPW-EXE:0x4af5d6 add-on refusal] the upgrade list's row activation (0x400, 0x4ae465) buys an add-on through 0x4af480, which plays the error 0x1D (0x486ae0) when the money is below its price; level rows refuse silently there, and no row adds a click
				GameAudio.PlayUi( GameAudio.ErrorEvent );
				break;
			case RideUpgradeRowKind.Level:
				// the level is asked for only while the balance is above its price; the economy refuses otherwise and the list closes either way
				level.Commands.Issue( new BuyUpgradeCommand( owner.Runtime.AttractionId ) );
				break;
			case RideUpgradeRowKind.CancelLevel:
				level.Commands.Issue( new CancelUpgradeCommand( owner.Runtime.AttractionId ) );
				break;
		}
		CloseRideUpgrade();
	}

	/// <summary>Arms the build tool with an add-on of <paramref name="ride"/> and closes the list and the ride window (tp.exe <c>0x004AF480</c>, <c>0x0048CF70</c>).</summary>
	private bool BeginAddOnPlacement( ObjectCatalogEntry entry, OriginalObject ride, long price )
	{
		if ( RejectReadOnlyAction() )
			return false;
		level.CellTool.Cancel();
		level.StaffPlacement?.Cancel();
		level.PatrolTool?.Cancel();
		level.BuyLandTool?.Cancel();
		level.AreaClearTool?.Cancel();
		level.PoolTool?.Cancel();
		if ( !level.BeginAddOn( entry, ride ) )
			return false;
		pendingItem = new BuildItem( $"addon:{entry.InfoId}", entry.InfoId, (BuildCategory)Math.Clamp( entry.WhichUIType, 0, 3 ), entry.ObjectNameIndex ?? -1, price, null,
			Array.Empty<string>(), Entry: entry );
		level.IsRemovingObjects = false;
		level.IsPlacing = false;
		CloseRideUpgrade();
		selectedObject = null;
		SetInfoArm( false );
		PostMessage( strings.Help( 440 ) );
		return true;
	}

	private UiScreen CreateUpgradeScreen()
	{
		var screen = new RideUpgradeScreen( this );
		var table = UpgradeTable;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiModelImage { Id = "listFrame", Model = UpgradeListNode.Model!, Bounds = UpgradeListNode.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.BuyUpgrades], Font = fonts => fonts.Title, Shadow = true, Color = UiColors.Title, Align = UiAlign.Center, Fit = true,
			Bounds = table.Control( 0x1F4E ).Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UpgradeList( this ) { Id = "upgradeList", Bounds = UpgradeContent, Anchor = UiAnchor.Center } );
		screen.Add( new UpgradeScrollBar( this ) { Id = "upgradeScroll", Bounds = new UiRect( UpgradeKnob.X, UpgradeTrack.Y, UpgradeTrack.Right - UpgradeKnob.X, UpgradeTrack.Height ), Anchor = UiAnchor.Center } );
		// The list 0x1F4D is style 0x81 (no bit 0x10). The arrows' parent is its type 3 slider, style 0x10, so they play 0xBD and repeat at 500 ms then 125 ms.
		void ScrollArrow( LayoutNode node, int step )
		{
			void Scroll() => ScrollUpgrades( step );
			screen.Add( new UiButton { Id = step < 0 ? "scrollUp" : "scrollDown", Model = node.Model, ClickSound = table.ClickSoundOf( node ),
				Clicked = Scroll, Repeat = UiButton.ScrollArrowRepeat( Scroll ), Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		ScrollArrow( table.Model( "b_up" ), -1 );
		ScrollArrow( table.Model( "b_down" ), 1 );
		var exit = table.Model( "b_exit" );
		screen.Add( new UiButton { Id = "cancel", Model = exit.Model, Help = strings.Help( 2 ), Clicked = CloseRideUpgrade, Bounds = exit.Rect, Anchor = UiAnchor.Center } );
		screen.Back = CloseRideUpgrade;
		return screen;
	}

	private sealed class RideUpgradeScreen : UiScreen
	{
		private readonly ParkHud owner;
		public RideUpgradeScreen( ParkHud owner ) : base( "rideUpgrade" ) => this.owner = owner;
		public override bool Update( UiContext context, UiInput input )
		{
			if ( input.Has( UiKeys.Back ) || RightCancels( input ) ) { Back?.Invoke(); return true; }
			if ( input.Wheel != 0 )
			{
				owner.ScrollUpgrades( input.Wheel > 0 ? -1 : 1 );
				input = input with { Wheel = 0 };
			}
			// the list closes with the ride window it belongs to (it is that window's child, 0x0065E6D5)
			if ( !owner.UpgradeListCurrent )
			{
				owner.CloseRideUpgrade();
				return true;
			}
			var shown = BuyScrollShown( owner.UpgradeMaxFirstRow );
			foreach ( var id in new[] { "upgradeScroll", "scrollUp", "scrollDown" } )
				if ( Find( id ) is { } element )
					element.Visible = shown;
			return base.Update( context, input );
		}

		internal override bool UpdatePointer( UiContext context, UiInput input, UiElement? recipient )
		{
			if ( IsRemoved ) return true;
			if ( RightCancels( input ) ) { Back?.Invoke(); return true; }
			return base.UpdatePointer( context, input, recipient );
		}
	}

	private sealed class UpgradeList : UiElement, IUiRowList
	{
		private readonly ParkHud owner;
		public UpgradeList( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		public int Selected => owner.upgradeSelected;
		public bool ArrowsSelect => true;
		public void Select( int index ) => owner.SelectUpgrade( index );
		public void Scroll( int rows ) => owner.ScrollUpgrades( rows );
		public override void Activate() => owner.ActivateUpgrade();
		public bool HoverSelects => true;

		/// <summary>A pointer move onto a row selects it; leaving the list keeps the selection.</summary>
		// [BIN:TPW-EXE:0x6656a0 list hover] the list's attribute 0x81 (table 0x755750) includes 0x80: every pointer move over it selects the row under it
		public void Hover( int index, bool pointerMoved )
		{
			if ( pointerMoved && index >= 0 )
				owner.SelectUpgrade( index );
		}

		public override void Adjust( int direction ) => owner.SelectUpgrade( Math.Max( 0, owner.upgradeSelected ) + direction );

		public int RowAt( UiCanvas canvas, NVector2 point )
		{
			var content = canvas.Map( UpgradeContent, Anchor );
			if ( !content.Contains( point ) ) return -1;
			var visible = (int)MathF.Floor( (point.Y - content.Y) / (BuyRowHeight * canvas.Scale) );
			var index = owner.upgradeFirstRow + visible;
			return visible < 0 || visible >= UpgradeVisibleRows || index >= owner.UpgradeListRows().Count ? -1 : index;
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var rows = owner.UpgradeListRows();
			for ( var visible = 0; visible < UpgradeVisibleRows && owner.upgradeFirstRow + visible < rows.Count; visible++ )
			{
				var index = owner.upgradeFirstRow + visible;
				var y = UpgradeContent.Y + visible * BuyRowHeight;
				var row = context.Canvas.Map( new UiRect( UpgradeContent.X, y, UpgradeContent.Width, BuyRowHeight ), Anchor );
				var selected = index == owner.upgradeSelected && rows[index].Row != null;
				if ( selected )
					context.Batch.AddRectangle( row, new RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)120 ) );
				var color = selected ? UiColors.Highlight : UiColors.Text;
				context.DrawFittedText( context.Fonts.Label, rows[index].Text, row, color, UiAlign.Left );
				if ( rows[index].Price.Length > 0 )
					context.DrawFittedText( context.Fonts.Label, rows[index].Price, row, color, UiAlign.Right );
			}
		}
	}

	private sealed class UpgradeScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public UpgradeScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		private UiRect KnobRect() => ListScrollKnobRect( UpgradeTrack, UpgradeKnob, owner.upgradeFirstRow, owner.UpgradeMaxFirstRow );
		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			var travel = UpgradeTrack.Height - UpgradeKnob.Height;
			if ( rect.Height <= 0 || travel <= 0 || owner.UpgradeMaxFirstRow == 0 ) return;
			var authoredY = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var fraction = Math.Clamp( (authoredY - UpgradeTrack.Y - UpgradeKnob.Height / 2) / travel, 0, 1 );
			owner.SetUpgradeFirstRow( (int)MathF.Round( fraction * owner.UpgradeMaxFirstRow ) );
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( "!slider", 0, context.Canvas.Map( UpgradeTrack, Anchor ) );
			context.DrawModel( "b_scroller", owner.UpgradeMaxFirstRow == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame,
				context.Canvas.Map( KnobRect(), Anchor ) );
		}
	}
}
