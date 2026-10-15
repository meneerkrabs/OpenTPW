using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private UiButton? rideUpgradeButton;
	private bool upgradeMouseGestureBlocked;

	private void AddRideUpgradeButton()
	{
		// [EXT:upgrade-confirm] the host confirmation for catalogue-supported ordinary ride upgrades has no original rule to match
		// [BIN:TPW-EXE:0x755150 b_upgrade] the ride window's upgrade button is the round b_upgrade at (1271,800)-(1425,953), help 19; ParkHud.ObjectWindow.cs places it
		rideUpgradeButton = hud.Add( new UiButton { Id = "rideUpgrade", Model = "b_upgrade", Clicked = OpenRideUpgrade,
			Bounds = new UiRect( 1271, 800, 154, 153 ), Anchor = UiAnchor.Center,
			Visible = false, Enabled = false } );
		infoElements.Add( rideUpgradeButton );
	}

	private bool TryUpgradeTarget( OriginalObject? owner, out ParkEconomy economy, out ParkObjectState state, out long cost, bool validateOwner = false )
	{
		economy = null!; state = null!; cost = 0;
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || level.IsReadOnlyVisit || owner is null or PrototypeRide
			|| owner.IsDeleted || FinanceEconomy is not { } current
			|| !ReferenceEquals( level.Park?.Economy, current ) || level.GetEconomyInstance( owner ) is not int instance
			|| !current.TryGetObject( instance, out var item ) || item.InfoId != owner.Entry.InfoId || item.Kind != ParkObjectKind.Ride
			|| item.Level < 0 || item.Level == int.MaxValue || !current.Catalog.TryGet( item.InfoId, out var info )
			|| item.Level + 1 >= info.Upgrades.Count || info.Upgrades[item.Level + 1].Level != item.Level + 1
			|| !owner.Runtime.CanBuyCatalogueTier( item.Level, item.Level + 1 )
			|| (validateOwner && !level.CanBuyCatalogueUpgrade( owner, item )) ) return false;
		economy = current; state = item; cost = info.Upgrades[item.Level + 1].CostOfUpgrade; return true;
	}

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
		rideUpgradeButton.Visible = InfoArmOpen && TryUpgradeTarget( selectedObject, out _, out _, out _ );
		if ( (input.LeftDown || input.LeftPressed || input.LeftReleased)
			&& (rideUpgradeButton.Visible || Stack.Top is RideUpgradeScreen) && UpgradeInputCaptured() )
			upgradeMouseGestureBlocked = true;
		else if ( input.LeftPressed || (!input.LeftDown && !input.LeftReleased) )
			upgradeMouseGestureBlocked = false;
		rideUpgradeButton.Enabled = rideUpgradeButton.Visible && !upgradeMouseGestureBlocked && PriceParkInputAllowed
			&& shopPriceMouseCapture != true && shopPriceKeyboardCapture != true && shopPriceTextCapture != true;
	}

	private void OpenRideUpgrade()
	{
		if ( !InfoArmOpen || !PriceParkInputAllowed || upgradeMouseGestureBlocked || Stack.Top != hud || UpgradeInputCaptured()
			|| !TryUpgradeTarget( selectedObject, out var economy, out var state, out var cost, validateOwner: true ) ) return;
		var owner = selectedObject!; var tier = state.Level;
		var screen = new RideUpgradeScreen(); var window = UiDialogs.CenteredWindow( 1100, 700 );
		UiDialogs.AddWindow( screen, window, "w_dialog", () => strings[UIStrings.Upgrades] );
		screen.Add( new UiLabel { Id = "preview", Text = () => $"{strings[UIStrings.UpgradeToLevel]} {tier + 1}\n{strings[UIStrings.EstimatedPrice]} {FormatMoney( cost )}",
			Wrap = true, Font = fonts => fonts.Label, Bounds = new UiRect( window.X + 100, window.Y + 180, 850, 140 ), Anchor = UiAnchor.Center } );
		var error = "";
		screen.Add( new UiLabel { Id = "error", Text = () => error, Wrap = true, Font = fonts => fonts.Label,
			Bounds = new UiRect( window.X + 100, window.Y + 340, 850, 120 ), Anchor = UiAnchor.Center } );
		void Cancel() { if ( Stack.Top == screen ) Stack.Pop(); }
		void Apply()
		{
			if ( Stack.Top != screen || upgradeMouseGestureBlocked || UpgradeInputCaptured() ) return;
			if ( !InfoArmOpen || !PriceViewAllowed || !ReferenceEquals( selectedObject, owner )
				|| !TryUpgradeTarget( owner, out var current, out var target, out var currentCost, validateOwner: true )
				|| !ReferenceEquals( current, economy ) || !ReferenceEquals( target, state ) || target.Level != tier || currentCost != cost )
			{ error = strings.Extra( OpenTpwText.NotAvailable ); return; }
			var result = current.TryBuyUpgrade( target.Id );
			if ( result == ParkEconomy.PurchaseResult.Ok ) { Cancel(); return; }
			error = result switch
			{
				ParkEconomy.PurchaseResult.NotAvailableInInstantAction => strings[UIStrings.UpgradesNotAvailable],
				ParkEconomy.PurchaseResult.AlreadyFullyUpgraded => strings[UIStrings.RideFullyUpgraded],
				ParkEconomy.PurchaseResult.UpgradeInProgress => strings[UIStrings.UpgradeInProgress],
				ParkEconomy.PurchaseResult.NotEnoughMoney => strings.Extra( OpenTpwText.NotEnoughMoney ),
				_ => strings.Extra( OpenTpwText.NotAvailable )
			};
		}
		screen.Add( new UiButton { Id = "apply", Model = "b_okay", Text = () => strings[UIStrings.BuyUpgrades], Clicked = Apply,
			Bounds = new UiRect( window.X + 180, window.Bottom - 180, 200, 100 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "cancel", Model = "b_exit", Help = strings.Help( 2 ), Clicked = Cancel,
			Bounds = new UiRect( window.Right - 400, window.Bottom - 180, 200, 100 ), Anchor = UiAnchor.Center } );
		screen.Back = Cancel; Stack.Push( screen ); screen.Focus( screen.Find( "apply" )! );
	}

	private sealed class RideUpgradeScreen : UiScreen
	{
		public RideUpgradeScreen() : base( "rideUpgrade" ) { }
		public override bool Update( UiContext context, UiInput input )
		{
			if ( input.Has( UiKeys.Back ) || input.RightPressed ) { Back?.Invoke(); return true; }
			return base.Update( context, input );
		}
	}
}
