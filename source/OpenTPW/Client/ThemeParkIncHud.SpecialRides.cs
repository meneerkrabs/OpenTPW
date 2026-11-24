using OpenTPW.UI.Original;

namespace OpenTPW;

public sealed partial class ThemeParkIncHud
{
	/// <summary>UITEXT 116 "Special Ride: ", the activation window's title prefix.</summary>
	internal const int SpecialRideTitle = 116;
	/// <summary>UITEXT 115, the note the activation window shows while an ability its ride needs is undiscovered.</summary>
	internal const int UndiscoveredAbilitiesNote = 115;
	/// <summary>The activation window's column heads: UITEXT 234 "Name" (control 0x4E28), <c>STAFFSTATES.str</c> entry 8 "Training" (0x4E29), UITEXT 983 "Employee Assigned" (0x4E2A).</summary>
	internal const int NameColumn = 234, TrainingColumn = 8, EmployeeAssignedColumn = 983;
	/// <summary>UITEXT 984 "Activate Ride", the button's text.</summary>
	internal const int ActivateRideText = 984;
	/// <summary>UITEXT 986 (one golden ticket) and 987 (several): "You also need" with the cost in parameter 50 and the career's tickets in parameter 51.</summary>
	internal const int OneTicketNeeded = 986, TicketsNeeded = 987;
	/// <summary>UIHELPTEXT 11 "Open attraction" and 12 "Close attraction", the ride window's open toggle's two help texts (its second and first help ids).</summary>
	internal const int OpenAttractionHelp = 11, CloseAttractionHelp = 12;

	private UiLabel? specialRideLines;
	private UiButton? specialRideActivate;
	private UiButton? rideDoorButton;
	/// <summary>The state the panel's elements were last set for, and the lines then composed; nothing is walked or composed again while it holds.</summary>
	private (int? Ride, bool Enabled, int Key, Level.ThemeParkIncDoor Door, bool DoorShown) specialRideState = (null, false, 0, default, false);
	private string specialRideText = "";

	/// <summary>
	/// The activation lines of a selected special ride that awaits its activation, and its Activate Ride button: the original's click on such a ride opens its activation window instead of the
	/// ride window. A selected ride that no record holds shows the ride window's open toggle instead.
	/// </summary>
	// [BIN:TPI-EXE:0x5036CD object click] a ride (Info.WhichUIType 0) that is no photo booth, whose type is special (0x5E81D0: +0x5C == 1) and that 0x6242F0 does not free opens 0x5863E0 with the thing,
	// the activation window (template 0x9C6048), not the ride window
	// [BIN:TPI-EXE:0x5865AC activation columns] the heads: 0x4E28 UITEXT 234 (0x5865AC), 0x4E29 entry 8 of the table 0xA0D60C (the language loader's +0x724, mUIStaffStates: STAFFSTATES.str, 0x58662C),
	// 0x4E2A UITEXT 983 (0x5866B5)
	// [EXT:tpi-text-hud] the windows are not drawn: the activation window's title (UITEXT 116), column heads and staff slots (0x55E1F0/0x55E020 into list 0x4E26), the ticket line (0x3DA or 0x3DB, 0x5871E0),
	// the undiscovered note (0x58673C, UITEXT 115) and the Activate Ride button (UITEXT 984, control 0x4E24, enabled by 0x6243C0), and the ride window's open toggle (control 0x2EE4, its help ids 12 and 11),
	// are text lines and buttons of the HUD, shown while the ride is selected
	private void BuildSpecialRidePanel()
	{
		specialRideLines = hud.Add( new UiLabel
		{
			Id = "specialRideLines",
			Text = () => specialRideText,
			Font = fonts => fonts.Small,
			Color = UiColors.Text,
			Wrap = true,
			Visible = false,
			Bounds = new UiRect( 24, 560, 1300, 360 ),
			Anchor = UiAnchor.TopLeft
		} );
		specialRideActivate = hud.Add( new UiButton
		{
			Id = "specialRideActivate",
			Text = () => strings.Entry( ActivateRideText ),
			Font = fonts => fonts.Menu,
			Visible = false,
			Clicked = ActivateSelectedSpecialRide,
			Bounds = new UiRect( 24, 930, 600, 80 ),
			Anchor = UiAnchor.TopLeft
		} );
		rideDoorButton = hud.Add( new UiButton
		{
			Id = "rideDoor",
			Text = () => strings.Help( specialRideState.Door.Open ? CloseAttractionHelp : OpenAttractionHelp ),
			Font = fonts => fonts.Menu,
			Visible = false,
			Clicked = ToggleSelectedRideDoor,
			Bounds = new UiRect( 24, 560, 600, 80 ),
			Anchor = UiAnchor.TopLeft
		} );
	}

	/// <summary>The economy instance of the selected object when it is a special ride awaiting its activation, else null.</summary>
	internal int? PendingSpecialRide => selectedObject is { IsDeleted: false } item && level.GetEconomyInstance( item ) is { } instance
		&& level.Park?.Economy.IsThemeParkIncSpecialRidePending( instance ) == true ? instance : null;

	/// <summary>
	/// The panel's state for this frame: the activation lines and button for a pending special ride, the open toggle for another ride, nothing while a menu, a panel, the loss scene or a flight covers
	/// them (<paramref name="hidden"/>). The elements are touched and the lines composed only when the state changed.
	/// </summary>
	private void UpdateSpecialRidePanel( bool hidden = false )
	{
		hidden |= MenuOpen || buildOpen || staffOpen || challengesOpen;
		var economy = level.Park?.Economy;
		var ride = hidden ? null : PendingSpecialRide;
		var enabled = ride is { } pending && economy?.CanActivateThemeParkIncSpecialRide( pending ) == true;
		var key = ride is { } shown && economy != null ? SpecialRideKey( economy, shown ) : 0;
		var door = !hidden && ride == null && selectedObject is { } item ? level.ThemeParkIncDoorOf( item ) : default;
		if ( specialRideState == (ride, enabled, key, door, door.Shown) )
			return;
		specialRideState = (ride, enabled, key, door, door.Shown);
		specialRideText = ride is { } instance ? SpecialRideText( instance ) : "";
		if ( specialRideLines != null )
			specialRideLines.Visible = ride != null;
		if ( specialRideActivate != null )
		{
			specialRideActivate.Visible = ride != null;
			specialRideActivate.Enabled = enabled;
		}
		if ( rideDoorButton != null )
		{
			rideDoorButton.Visible = door.Shown;
			rideDoorButton.Enabled = door.Enabled;
		}
	}

	/// <summary>What the activation lines read, folded into one number: the ride, its slots' members, the cost, the career's tickets and whether its abilities are discovered.</summary>
	private static int SpecialRideKey( ParkEconomy economy, int instance )
	{
		var hash = new HashCode();
		hash.Add( instance );
		if ( economy.TpiSpecialRides?.Find( instance ) is { } record )
			foreach ( var slot in record.Slots )
				hash.Add( slot );
		hash.Add( economy.ThemeParkIncActivationCost( instance ) );
		hash.Add( economy.TpiCareer?.GoldenTickets ?? 0 );
		hash.Add( economy.ThemeParkIncActivationAbilitiesDiscovered( instance ) );
		return hash.ToHashCode();
	}

	/// <summary>The activation lines of the selected pending special ride (see <see cref="ComposeSpecialRideLines"/>).</summary>
	internal string SpecialRideText( int instance )
	{
		if ( selectedObject is not { } item || level.Park?.Economy is not { TpiSpecialRides: { } rides, TpiCareer: { } career } economy || rides.Find( instance ) is not { } record )
			return "";
		var members = economy.TpiStaff?.Members ?? (IReadOnlyList<ThemeParkIncStaffMember>)Array.Empty<ThemeParkIncStaffMember>();
		return ComposeSpecialRideLines( strings, TextsOrLanguage, ItemNameOf( item.Entry ), record, members, economy.ThemeParkIncActivationCost( instance ), career.GoldenTickets,
			economy.ThemeParkIncActivationAbilitiesDiscovered( instance ) );
	}

	/// <summary>
	/// The activation lines: the title with the ride's name, the column heads, a line per used slot (its staff type, its ability, the member assigned or "-"), the golden tickets needed and the
	/// undiscovered-abilities note.
	/// </summary>
	internal static string ComposeSpecialRideLines( UiStringTable strings, IThemeParkIncTexts texts, string rideName, ThemeParkIncSpecialRideRecord record,
		IReadOnlyList<ThemeParkIncStaffMember> members, int cost, int tickets, bool abilitiesDiscovered )
	{
		var lines = new List<string> { strings.Entry( SpecialRideTitle ) + rideName,
			$"{strings.Entry( NameColumn )} - {texts.Format( ThemeParkIncTable.StaffStates, TrainingColumn )} - {strings.Entry( EmployeeAssignedColumn )}" };
		foreach ( var slot in record.Slots.TakeWhile( slot => slot.StaffType != ThemeParkIncSpecialRideStaffSlot.NoRequirement ) )
		{
			var member = slot.Thing == 0 ? null : members.FirstOrDefault( candidate => candidate.ThingId == slot.Thing );
			lines.Add( $"{strings.StaffType( slot.StaffType )} - {strings.Ability( slot.Ability )} - {(member == null ? "-" : strings.StaffName( (int)member.Role, member.NameIndex ))}" );
		}
		if ( cost != 0 )
			lines.Add( texts.Format( ThemeParkIncTable.UiText, cost == 1 ? OneTicketNeeded : TicketsNeeded,
				new Dictionary<int, string> { [50] = ThemeParkIncTexts.Int( cost ), [51] = ThemeParkIncTexts.Int( tickets ) } ) );
		if ( !abilitiesDiscovered )
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

	/// <summary>The ride window's open toggle on the selected ride (<see cref="Level.ToggleThemeParkIncDoor"/>).</summary>
	internal void ToggleSelectedRideDoor()
	{
		if ( selectedObject is { } item && level.ToggleThemeParkIncDoor( item ) )
			UpdateSpecialRidePanel();
	}
}
