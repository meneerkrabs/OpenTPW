using OpenTPW.UI.Original;

namespace OpenTPW;

public sealed partial class ThemeParkIncHud
{
	/// <summary>UITEXT 116 "Special Ride: ", the activation window's title prefix.</summary>
	internal const int SpecialRideTitle = 116;
	/// <summary>UITEXT 115, the note the activation window shows while an ability its ride needs is undiscovered.</summary>
	internal const int UndiscoveredAbilitiesNote = 115;
	/// <summary>UITEXT 982 "Staff Type" and 983 "Employee Assigned", the window's column heads; UITEXT 88 heads the abilities (the mission window's).</summary>
	internal const int StaffTypeColumn = 982, EmployeeAssignedColumn = 983, AbilityColumn = 88;
	/// <summary>UITEXT 984 "Activate Ride", the button's text.</summary>
	internal const int ActivateRideText = 984;
	/// <summary>UITEXT 986 (one golden ticket) and 987 (several): "You also need" with the cost in parameter 50 and the career's tickets in parameter 51.</summary>
	internal const int OneTicketNeeded = 986, TicketsNeeded = 987;

	/// <summary>
	/// The activation lines of a selected special ride that awaits its activation, and its Activate Ride button: the original's click on such a ride opens its activation window instead of the
	/// ride window.
	/// </summary>
	// [BIN:TPI-EXE:0x5036CD object click] a ride (Info.WhichUIType 0) that is no photo booth, whose type is special (0x5E81D0: +0x5C == 1) and that 0x6242F0 does not free opens 0x5863E0 with the thing,
	// the activation window (template 0x9C6048), not the ride window
	// [EXT:tpi-text-hud] the window is not drawn: its title (UITEXT 116), the staff slots (0x55E1F0/0x55E020 into list 0x4E26, columns UITEXT 982, 983), the ticket line (0x3DA or 0x3DB, 0x5871E0), the
	// undiscovered note (0x58673C, UITEXT 115) and the Activate Ride button (UITEXT 984, control 0x4E24, enabled by 0x6243C0) are text lines and a button of the HUD, shown while the ride is selected
	private void BuildSpecialRidePanel()
	{
		hud.Add( new UiLabel
		{
			Id = "specialRideLines",
			Text = SpecialRideText,
			Font = fonts => fonts.Small,
			Color = UiColors.Text,
			Wrap = true,
			Visible = false,
			Bounds = new UiRect( 24, 560, 1300, 360 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiButton
		{
			Id = "specialRideActivate",
			Text = () => strings.Entry( ActivateRideText ),
			Font = fonts => fonts.Menu,
			Visible = false,
			Clicked = ActivateSelectedSpecialRide,
			Bounds = new UiRect( 24, 930, 600, 80 ),
			Anchor = UiAnchor.TopLeft
		} );
	}

	/// <summary>The economy instance of the selected object when it is a special ride awaiting its activation, else null.</summary>
	internal int? PendingSpecialRide => selectedObject is { IsDeleted: false } item && level.GetEconomyInstance( item ) is { } instance
		&& level.Park?.Economy.IsThemeParkIncSpecialRidePending( instance ) == true ? instance : null;

	/// <summary>Shows the activation lines while a pending special ride is selected and no panel covers them; the button is enabled as the original's control is.</summary>
	private void UpdateSpecialRidePanel()
	{
		var instance = buildOpen || staffOpen || challengesOpen ? null : PendingSpecialRide;
		foreach ( var element in hud.Elements )
		{
			if ( !element.Id.StartsWith( "specialRide", StringComparison.Ordinal ) )
				continue;
			element.Visible = instance != null;
			if ( element.Id == "specialRideActivate" )
				element.Enabled = instance is { } ride && level.Park?.Economy.CanActivateThemeParkIncSpecialRide( ride ) == true;
		}
	}

	/// <summary>The activation lines: the title with the ride's name, the slots (staff type, ability, the member assigned), the golden tickets and the undiscovered-abilities note.</summary>
	internal string SpecialRideText()
	{
		if ( PendingSpecialRide is not { } instance || selectedObject is not { } item || level.Park?.Economy is not { TpiSpecialRides: { } rides, TpiCareer: { } career } economy
			|| rides.Find( instance ) is not { } record )
			return "";
		var lines = new List<string> { strings.Entry( SpecialRideTitle ) + ItemNameOf( item.Entry ),
			$"{strings.Entry( StaffTypeColumn )} - {strings.Entry( AbilityColumn )} - {strings.Entry( EmployeeAssignedColumn )}" };
		var members = economy.TpiStaff?.Members ?? (IReadOnlyList<ThemeParkIncStaffMember>)Array.Empty<ThemeParkIncStaffMember>();
		foreach ( var slot in record.Slots.TakeWhile( slot => slot.StaffType != ThemeParkIncSpecialRideStaffSlot.NoRequirement ) )
		{
			var member = slot.Thing == 0 ? null : members.FirstOrDefault( candidate => candidate.ThingId == slot.Thing );
			lines.Add( $"{strings.StaffType( slot.StaffType )} - {strings.Ability( slot.Ability )} - {(member == null ? "-" : strings.StaffName( (int)member.Role, member.NameIndex ))}" );
		}
		var cost = economy.ThemeParkIncActivationCost( instance );
		if ( cost != 0 )
			lines.Add( TextsOrLanguage.Format( ThemeParkIncTable.UiText, cost == 1 ? OneTicketNeeded : TicketsNeeded,
				new Dictionary<int, string> { [50] = ThemeParkIncTexts.Int( cost ), [51] = ThemeParkIncTexts.Int( career.GoldenTickets ) } ) );
		if ( !economy.ThemeParkIncActivationAbilitiesDiscovered( instance ) )
			lines.Add( strings.Entry( UndiscoveredAbilitiesNote ) );
		return string.Join( "\n", lines );
	}

	/// <summary>Selects an object as the park click does (the slice smoke test's step).</summary>
	internal void SelectForSmoke( OriginalObject item ) => selectedObject = item;

	/// <summary>The Activate Ride button: the selected special ride is activated when the original's control would be enabled (<c>0x586D40</c>), and the selection ends, as the window closes.</summary>
	internal void ActivateSelectedSpecialRide()
	{
		if ( PendingSpecialRide is not { } instance || level.Park?.Economy is not { } economy || !economy.CanActivateThemeParkIncSpecialRide( instance ) )
			return;
		economy.ActivateThemeParkIncSpecialRide( instance );
		selectedObject = null;
		UpdateSpecialRidePanel();
	}
}
