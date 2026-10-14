using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// The info arm's call button: the player calls a mechanic to the selected ride (a handyman to a toilet) or cancels the call (tp.exe <c>0x0048CE50</c> →
/// <c>0x004DFE30</c>, <see cref="ParkEconomy.RequestMaintenance"/>). The call closes the ride until the repair; the button stays pressed while it lasts.
/// </summary>
public sealed partial class ParkHud
{
	private UiButton? callStaffButton;

	// [DATA:ui.wad:b_callmech,b_callhandy] the original's call buttons (with their pressed frames b_callmechd, b_callhandyd)
	// Approximation (UI-024, registered at ParkHud.Build): the call button sits in the info arm's header beside the name; the original panel's layout is not captured
	private void AddCallStaffButton()
	{
		// ui.wad lacks the highlight textures b_callmechg and b_callhandyg (the model's frame 2), so a focused button keeps its normal frame
		callStaffButton = hud.Add( new UiButton { Id = "callStaff", Model = "b_callmech", Clicked = ToggleCallStaff, KeepNormalFrameWhenFocused = true,
			Selected = () => CallTarget( selectedObject, out _, out var item, out _ ) && item.MaintenanceRequested,
			Bounds = UiRect.FromCenter( new System.Numerics.Vector2( 945, 1125 ), 90, 90 ), Anchor = UiAnchor.BottomLeft, Visible = false, Enabled = false } );
		infoElements.Add( callStaffButton );
	}

	/// <summary>The selected placed attraction's economy record when a call can be made for it (a ride, or a toilet for the handymen); toilet: true for a toilet.</summary>
	private bool CallTarget( OriginalObject? owner, out ParkEconomy economy, out ParkObjectState item, out bool toilet )
	{
		economy = null!; item = null!; toilet = false;
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || level.IsReadOnlyVisit || owner is null or PrototypeRide || owner.IsDeleted
			|| level.Park?.Economy is not { } current || level.GetEconomyInstance( owner ) is not int instance || !current.TryGetObject( instance, out var state )
			|| !current.Catalog.TryGet( state.InfoId, out var info ) )
			return false;
		toilet = info.ProvidesRelief;
		if ( state.Kind != ParkObjectKind.Ride && !toilet )
			return false;
		economy = current; item = state;
		return true;
	}

	/// <summary>
	/// The refusal of a new call (UIHELPTEXT 574 "haven't hired any", 575 "on strike"; the original's panel greys its button), or null when a call can be
	/// made. A toilet's call goes to the handymen.
	/// </summary>
	private static int? CallRefusal( ParkEconomy economy, bool toilet )
	{
		var role = toilet ? StaffType.Handyman : StaffType.Mechanic;
		if ( !economy.Staff.OfType( role ).Any() )
			return 574;
		return economy.Staff.Strikes.IsActive( role ) ? 575 : null;
	}

	private void UpdateCallStaffButton()
	{
		if ( callStaffButton == null )
			return;
		var target = InfoArmOpen && CallTarget( selectedObject, out var economy, out var item, out var toilet );
		callStaffButton.Visible = target;
		if ( !target )
			return;
		CallTarget( selectedObject, out economy, out item, out toilet );
		callStaffButton.Model = toilet ? "b_callhandy" : "b_callmech";
		var refusal = item.MaintenanceRequested ? null : CallRefusal( economy, toilet );
		// UIHELPTEXT 535 "Left-click to call a mechanic to the ride", 570 "Left-click to cancel calling the mechanic"
		callStaffButton.Help = strings.Help( refusal ?? (item.MaintenanceRequested ? 570 : 535) );
		callStaffButton.Enabled = refusal == null && PriceParkInputAllowed;
	}

	private void ToggleCallStaff()
	{
		if ( !InfoArmOpen || !PriceParkInputAllowed || !CallTarget( selectedObject, out var economy, out var item, out var toilet ) )
			return;
		if ( !item.MaintenanceRequested && CallRefusal( economy, toilet ) != null )
			return;
		economy.RequestMaintenance( item.Id, !item.MaintenanceRequested );
	}
}
