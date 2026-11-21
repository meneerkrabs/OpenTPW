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

	/// <summary>The pool's candidates in slot order, then (under a heading) the hired members, as listed rows; read only.</summary>
	internal IReadOnlyList<string> StaffRows()
	{
		if ( level.Park?.Economy.TpiStaff is not { } staff )
			return Array.Empty<string>();
		var rows = new List<string>();
		foreach ( var candidate in staff.Slots )
		{
			if ( candidate != null )
				rows.Add( StaffRow( candidate.Role, candidate.NameIndex, candidate.Experience, candidate.Wage ) );
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

	private void ToggleStaffPanel()
	{
		if ( staffOpen )
			CloseStaffPanel();
		else
		{
			CloseBuildWindow();
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
