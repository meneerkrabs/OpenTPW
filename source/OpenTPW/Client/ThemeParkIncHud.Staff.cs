using OpenTPW.UI.Original;

namespace OpenTPW;

public sealed partial class ThemeParkIncHud
{
	private bool staffOpen;

	/// <summary>The staff panel's parts, hidden until the Staff button opens them.</summary>
	private void BuildStaffPanel()
	{
		hud.Add( new UiLabel
		{
			Id = "staffPanelTitle",
			Text = () => T( OpenTpwText.TpiStaff ),
			Font = fonts => fonts.Heading,
			Color = UiColors.Title,
			Visible = false,
			Bounds = new UiRect( 244, 210, 1060, 70 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiLabel
		{
			Id = "staffPanelNote",
			Text = () => T( OpenTpwText.TpiStaffPoolNote ),
			Font = fonts => fonts.Small,
			Color = UiColors.Text,
			Visible = false,
			Bounds = new UiRect( 244, 284, 1560, 70 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiScrollList
		{
			Id = "staffPanelList",
			Rows = StaffRows,
			RowHeight = 52,
			RowActivated = ActivateStaffRow,
			// [EXT:tpi-text-hud] the text HUD's staff list clicks like its buttons; no original Theme Park Inc hire list is claimed
			ClicksOnActivate = true,
			Visible = false,
			Bounds = new UiRect( 244, 380, 1560, 1000 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiButton
		{
			Id = "staffPanelClose",
			Text = () => T( OpenTpwText.TpiClose ),
			Font = fonts => fonts.Menu,
			Visible = false,
			Clicked = CloseStaffPanel,
			Bounds = new UiRect( 1330, 210, 474, 84 ),
			Anchor = UiAnchor.TopLeft
		} );
	}

	/// <summary>The pool slots of the listed candidate rows, in row order (the rows <see cref="StaffRows"/> listed last).</summary>
	private readonly List<int> staffRowSlots = new();

	/// <summary>The pool's candidates in slot order, then (under a heading) the hired members, as listed rows; activating a candidate's row hires it.</summary>
	internal IReadOnlyList<string> StaffRows()
	{
		staffRowSlots.Clear();
		if ( level.Park?.Economy.TpiStaff is not { } staff )
			return Array.Empty<string>();
		var rows = new List<string>();
		for ( var slot = 0; slot < staff.Slots.Count; slot++ )
		{
			if ( staff.Slots[slot] is { } candidate )
			{
				rows.Add( StaffRow( candidate.Role, candidate.NameIndex, candidate.Experience, candidate.Wage ) );
				staffRowSlots.Add( slot );
			}
		}
		if ( staff.Members.Count > 0 )
		{
			rows.Add( T( OpenTpwText.TpiStaffMembersHeading ) );
			foreach ( var member in staff.Members )
				rows.Add( StaffRow( member.Role, member.NameIndex, member.Experience, member.Salary ) );
		}
		return rows;
	}

	// STAFF_TYPES.str holds each role's singular and plural name as consecutive entries (Cleaner, Cleaners, Engineer, Engineers, Entertainer, Entertainers, Guard, Guards,
	// Scientist, Scientists, Gardener, Gardeners), so a role in the staff role order (handyman .. gardener) reads entry 2 * role. Each per-role name table has exactly 35 entries
	// (the candidate's R mod 35) and no leading header entries; [BIN:TPI-EXE:0x62FFB0 staff name] the formatter passes the role's table and the candidate's word +4, its name
	// index, to the string lookup 0x8934A0, so the NameIndex is the table index
	private string StaffRow( ThemeParkIncStaffRole role, int nameIndex, int experience, int wage ) => string.Format( T( OpenTpwText.TpiStaffRow ),
		strings.StaffType( (int)role ), strings.StaffName( (int)role, nameIndex ), experience, wage.ToString( "N0" ) );

	/// <summary>A candidate's row activated: a cleaner (handyman) goes to the hand for placing, another role is refused with a note.</summary>
	internal void ActivateStaffRow( int index )
	{
		StaffRows();
		if ( index < 0 || index >= staffRowSlots.Count )
			return;
		var refusal = level.BeginTpiHire( staffRowSlots[index] );
		if ( refusal == null )
		{
			CloseStaffPanel();
			DisarmBuild();
			PostMessage( T( OpenTpwText.TpiStaffPlaceHint ) );
		}
		else if ( refusal == "role not ported" )
		{
			PostMessage( T( OpenTpwText.TpiStaffRoleRefused ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
		}
	}

	/// <summary>
	/// The staff placement's HUD side while a hire is in the hand: a park click drops it (a refused cell keeps it in the hand with a note), a right click cancels it. True when the click is
	/// the placement's.
	/// </summary>
	private bool UpdateStaffPlacement( UiInput input, bool inPark )
	{
		if ( level.TpiPlacingSlot < 0 || MenuOpen )
			return false;
		if ( input.RightPressed && GameOptions.Current.RmbCancel )
		{
			level.CancelTpiHire();
			PostMessage( T( OpenTpwText.TpiToolsPutAway ) );
			return true;
		}
		if ( !inPark || !input.LeftPressed )
			return false;
		if ( level.DropTpiStaff( cursorX, cursorY ) is { } member )
			PostMessage( string.Format( T( OpenTpwText.TpiStaffRow ), strings.StaffType( (int)member.Role ), strings.StaffName( (int)member.Role, member.NameIndex ), member.Experience, member.Salary.ToString( "N0" ) ) );
		else
		{
			PostMessage( T( OpenTpwText.TpiStaffCannotPlace ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
		}
		return toolPress = true;
	}

	/// <summary>The wage demands of the day, as notes (the negotiation is not ported yet).</summary>
	private void ShowWageDemands()
	{
		if ( level.Park?.Economy is not { } economy || economy.TpiWageDemands.Count == 0 )
			return;
		foreach ( var demand in economy.TpiWageDemands )
			PostMessage( string.Format( T( OpenTpwText.TpiStaffWageDemand ), strings.StaffName( (int)demand.Member.Role, demand.Member.NameIndex ), demand.Shortfall.ToString( "N0" ) ) );
		economy.TpiWageDemands.Clear();
	}

	private void ToggleStaffPanel()
	{
		if ( staffOpen )
			CloseStaffPanel();
		else
		{
			CloseBuildWindow();
			CloseChallengePanel();
			staffOpen = true;
			SetStaffPanelVisible( true );
		}
	}

	private void CloseStaffPanel()
	{
		staffOpen = false;
		SetStaffPanelVisible( false );
	}

	private void SetStaffPanelVisible( bool visible )
	{
		foreach ( var element in hud.Elements )
		{
			if ( element.Id.StartsWith( "staffPanel", StringComparison.Ordinal ) )
				element.Visible = visible;
		}
	}
}
