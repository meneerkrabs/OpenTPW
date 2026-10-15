using System.Globalization;
using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private UiButton? shopPriceButton;
	private bool? shopPriceMouseCapture, shopPriceKeyboardCapture, shopPriceTextCapture;

	private void AddShopPriceButton()
	{
		// [EXT:shop-price-editor] the host price editor occupies the editable information arm's free ride-view slot; decimal input follows the existing nonnegative int API; there is no original rule to match
		shopPriceButton = hud.Add( new UiButton
		{
			Id = "shopPrice", Text = () => strings[SelectedObject?.Entry.WhichUIType == (int)BuildCategory.Shops ? UIStrings.SalePrice : UIStrings.PriceOfGame],
			Font = fonts => fonts.Label, Clicked = OpenShopPriceEditor,
			Bounds = UiRect.FromCenter( new System.Numerics.Vector2( 875, 1425 ), 200, 100 ), Anchor = UiAnchor.BottomLeft,
			Visible = false, Enabled = false
		} );
		infoElements.Add( shopPriceButton );
	}

	private bool TryPriceTarget( OriginalObject? owner, out ParkEconomy economy, out ParkObjectState state )
	{
		economy = null!; state = null!;
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || level.IsReadOnlyVisit || owner is not { IsDeleted: false }
			|| !level.Objects.Objects.Contains( owner ) || FinanceEconomy is not { } current
			|| level.GetEconomyInstance( owner ) is not int instance || !current.TryGetObject( instance, out var item )
			|| item.InfoId != owner.Entry.InfoId ) return false;
		var category = owner.Entry.WhichUIType;
		if ( !(category == (int)BuildCategory.Shops && item.Kind == ParkObjectKind.Shop)
			&& !(category == (int)BuildCategory.Sideshows && item.Kind == ParkObjectKind.Sideshow) ) return false;
		economy = current; state = item; return true;
	}

	private bool PriceInputCaptured()
	{
		if ( shopPriceMouseCapture is bool mouse && shopPriceKeyboardCapture is bool keyboard && shopPriceTextCapture is bool text )
			return mouse || keyboard || text;
		var io = ImGuiNET.ImGui.GetIO();
		return (shopPriceMouseCapture ?? io.WantCaptureMouse) || (shopPriceKeyboardCapture ?? io.WantCaptureKeyboard) || (shopPriceTextCapture ?? io.WantTextInput);
	}

	private bool PriceViewAllowed => !IsRiding && Camera.Mode is ParkCameraMode && level.BuildEntry == null
		&& !level.IsPlacing && !level.IsRemovingObjects && !level.CellTool.IsActive && !level.IsCoasterToolActive;

	private bool PriceParkInputAllowed => !Paused && Stack.Screens.Count == 1 && !Input.TextEntryActive && PriceViewAllowed;

	private void UpdateShopPriceButton( bool? mouse, bool? keyboard, bool? text )
	{
		shopPriceMouseCapture = mouse; shopPriceKeyboardCapture = keyboard; shopPriceTextCapture = text;
		if ( shopPriceButton == null ) return;
		shopPriceButton.Visible = InfoArmOpen && TryPriceTarget( selectedObject, out _, out _ );
		// Native capture is queried only when this dedicated action is activated; the shared frame samples IO at its existing point.
		shopPriceButton.Enabled = shopPriceButton.Visible && PriceParkInputAllowed && mouse != true && keyboard != true && text != true;
	}

	private void OpenShopPriceEditor()
	{
		if ( !InfoArmOpen || !PriceParkInputAllowed || PriceInputCaptured() || !TryPriceTarget( selectedObject, out var economy, out var state ) ) return;
		var owner = selectedObject!;
		var screen = new ShopPriceScreen();
		var window = UiDialogs.CenteredWindow( 1100, 700 );
		UiDialogs.AddWindow( screen, window, "w_dialog", () => strings[owner.Entry.WhichUIType == (int)BuildCategory.Shops ? UIStrings.SalePrice : UIStrings.PriceOfGame] );
		var number = screen.Add( new UiTextField
		{
			// Keep oversized pasted numbers invalid rather than truncating them into another valid price.
			Id = "price", Text = state.Price.ToString( CultureInfo.InvariantCulture ), MaximumLength = 64,
			Bounds = new UiRect( window.X + 180, window.Y + 220, 650, 100 ), Anchor = UiAnchor.Center
		} );
		var error = "";
		screen.Add( new UiLabel { Id = "error", Text = () => error, Wrap = true, Font = fonts => fonts.Label,
			Bounds = new UiRect( window.X + 100, window.Y + 350, 850, 100 ), Anchor = UiAnchor.Center } );
		void Cancel() { if ( Stack.Top == screen ) Stack.Pop(); }
		void Apply()
		{
			if ( Stack.Top != screen || PriceInputCaptured() ) return;
			if ( number.Text.Length > 10 || !int.TryParse( number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var price ) )
			{ error = strings.Extra( OpenTpwText.InvalidPrice ); return; }
			if ( !InfoArmOpen || !PriceViewAllowed || !ReferenceEquals( selectedObject, owner ) || !TryPriceTarget( owner, out var current, out var target )
				|| !ReferenceEquals( current, economy ) || !ReferenceEquals( target, state ) )
			{ error = strings.Extra( OpenTpwText.NotAvailable ); return; }
			current.SetPrices( target.Id, price );
			Cancel();
		}
		number.Submitted = Apply;
		screen.Add( new UiButton { Id = "apply", Model = "b_okay", Clicked = Apply,
			Bounds = new UiRect( window.X + 180, window.Bottom - 180, 200, 100 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "cancel", Model = "b_exit", Help = strings.Help( 2 ), Clicked = Cancel,
			Bounds = new UiRect( window.Right - 400, window.Bottom - 180, 200, 100 ), Anchor = UiAnchor.Center } );
		screen.Back = Cancel;
		Stack.Push( screen );
		screen.Focus( number );
	}
	// Cancellation owns the frame, including a simultaneous confirm or Apply release.
	private sealed class ShopPriceScreen : UiScreen
	{
		public ShopPriceScreen() : base( "shopPrice" ) { }
		public override bool Update( UiContext context, UiInput input )
		{
			if ( input.Has( UiKeys.Back ) || RightCancels( input ) )
			{
				Back?.Invoke();
				return true;
			}
			return base.Update( context, input );
		}
	}

}
