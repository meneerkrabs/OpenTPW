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

	/// <summary>What activating each listed row does, in row order (the rows <see cref="StaffRows"/> listed last); null for an inert row.</summary>
	private readonly List<Action?> staffRowActions = new();

	/// <summary>The hired member whose "Select Ability To Learn" list the panel shows, null for the pool and the members.</summary>
	private ThemeParkIncStaffMember? trainingMember;

	/// <summary>The ability whose retraining the player was asked to confirm (choosing it again confirms), -1 for none.</summary>
	private int retrainAbility = -1;

	/// <summary>
	/// The pool's candidates in slot order, then (under a heading) the hired members, as listed rows; activating a candidate's row hires it, a member's row opens his ability list. With a member's list
	/// open: the abilities he may learn, the full room's or the role's line, and a row back.
	/// </summary>
	internal IReadOnlyList<string> StaffRows()
	{
		staffRowActions.Clear();
		if ( level.Park?.Economy.TpiStaff is not { } staff )
			return Array.Empty<string>();
		var rows = new List<string>();
		void Row( string text, Action? action )
		{
			rows.Add( text );
			staffRowActions.Add( action );
		}
		if ( trainingMember is { } member && staff.Members.Contains( member ) )
		{
			AbilityRows( staff, member, Row );
			return rows;
		}
		trainingMember = null;
		if ( researchView != ResearchView.None )
		{
			ResearchRows( Row );
			return rows;
		}
		for ( var slot = 0; slot < staff.Slots.Count; slot++ )
		{
			if ( staff.Slots[slot] is { } candidate )
			{
				var hire = slot;
				Row( StaffRow( candidate.Role, candidate.NameIndex, candidate.Experience, candidate.Wage ), () => Hire( hire ) );
			}
		}
		if ( staff.Members.Count > 0 )
		{
			Row( T( OpenTpwText.TpiStaffMembersHeading ), null );
			foreach ( var hired in staff.Members )
				Row( StaffRow( hired.Role, hired.NameIndex, hired.Experience, hired.Salary ) + AbilitySuffix( hired ), () => OpenAbilities( hired ) );
		}
		ResearchRows( Row );
		return rows;
	}

	/// <summary>A member's trained ability after his row (ABILITIES.str), nothing for none.</summary>
	private string AbilitySuffix( ThemeParkIncStaffMember member ) => member.Ability == 0 ? "" : $" - {strings.Ability( member.Ability )}";

	/// <summary>
	/// The "Select Ability To Learn" list of a member (0x580860): its title, then the room-full line (713) or the abilities he may learn (0x639B20) or the role's line (707..712), and a row back.
	/// </summary>
	// [EXT:tpi-text-hud] the text HUD lists the panel's rows; the original's panel window and its confirmation dialog (715) are not built: choosing the same ability twice confirms a retraining
	private void AbilityRows( ThemeParkIncStaff staff, ThemeParkIncStaffMember member, Action<string, Action?> row )
	{
		row( $"{strings.Entry( ThemeParkIncTraining.SelectAbilityText )}: {strings.StaffName( (int)member.Role, member.NameIndex )}", null );
		var discovered = level.Park?.Economy.TpiCareer?.Abilities ?? (IReadOnlyList<byte>)Array.Empty<byte>();
		if ( ThemeParkIncTraining.RoomFull( staff.Members ) )
			row( strings.Entry( ThemeParkIncTraining.RoomFullText ), null );
		else
		{
			var any = false;
			foreach ( var ability in ThemeParkIncTraining.Learnable( member, discovered ) )
			{
				any = true;
				row( strings.Ability( ability ), () => ChooseAbility( member, ability ) );
			}
			if ( !any )
				row( strings.Entry( ThemeParkIncTraining.NothingToLearnText( member.Role ) ), null );
		}
		row( T( OpenTpwText.TpiClose ), () => trainingMember = null );
	}

	/// <summary>A member's row activated: his ability list opens when the park has a training room and he stands in it.</summary>
	private void OpenAbilities( ThemeParkIncStaffMember member )
	{
		// an OpenTPW interface guard, not a port: the original's choice (0x580550) tests for no room, and without one the walk to the training room reads thing 0's entrance word (0x639AA0 finds
		// none, 0x628492 reads its +0x46); the text HUD refuses the choice with "Build a training room first" (TpiTrainingNoRoom) rather than walking to that cell
		if ( !level.ThemeParkIncHasTrainingRoom )
		{
			PostMessage( T( OpenTpwText.TpiTrainingNoRoom ) );
			return;
		}
		if ( member.Placed )
		{
			trainingMember = member;
			retrainAbility = -1;
		}
	}

	/// <summary>An ability row activated (0x580550): already trained refuses, retraining asks to be chosen again, else the member walks to the training room.</summary>
	private void ChooseAbility( ThemeParkIncStaffMember member, int ability )
	{
		switch ( level.ChooseThemeParkIncAbility( member, ability, confirmed: retrainAbility == ability ) )
		{
			case ThemeParkIncTrainingChoice.AlreadyTrained:
				PostMessage( strings.Entry( ThemeParkIncTraining.AlreadyTrainedText ) );
				GameAudio.PlayUi( GameAudio.ErrorEvent );
				break;
			case ThemeParkIncTrainingChoice.NeedsConfirmation:
				retrainAbility = ability;
				PostMessage( string.Format( T( OpenTpwText.TpiTrainingRetrain ), strings.StaffName( (int)member.Role, member.NameIndex ), strings.Ability( member.Ability ) ) );
				break;
			case ThemeParkIncTrainingChoice.Started:
				trainingMember = null;
				retrainAbility = -1;
				break;
			default:
				PostMessage( T( OpenTpwText.TpiTrainingNoRoom ) );
				break;
		}
	}

	/// <summary>The training notices of the staff world, as notes (the advisor's lines 0x175 and 0x1C9).</summary>
	private void ShowTrainingNotices()
	{
		if ( level.TpiStaffWorld is not { TrainingNotices.Count: > 0 } world )
			return;
		foreach ( var notice in world.TrainingNotices )
			PostMessage( string.Format( T( notice.Finished ? OpenTpwText.TpiTrainingFinished : OpenTpwText.TpiTrainingCancelled ),
				strings.StaffName( (int)notice.Member.Role, notice.Member.NameIndex ), strings.Ability( notice.Ability ) ) );
		world.TrainingNotices.Clear();
	}

	// STAFF_TYPES.str holds each role's singular and plural name as consecutive entries (Cleaner, Cleaners, Engineer, Engineers, Entertainer, Entertainers, Guard, Guards,
	// Scientist, Scientists, Gardener, Gardeners), so a role in the staff role order (handyman .. gardener) reads entry 2 * role. Each per-role name table has exactly 35 entries
	// (the candidate's R mod 35) and no leading header entries; [BIN:TPI-EXE:0x62FFB0 staff name] the formatter passes the role's table and the candidate's word +4, its name
	// index, to the string lookup 0x8934A0, so the NameIndex is the table index
	private string StaffRow( ThemeParkIncStaffRole role, int nameIndex, int experience, int wage ) => string.Format( T( OpenTpwText.TpiStaffRow ),
		strings.StaffType( (int)role ), strings.StaffName( (int)role, nameIndex ), experience, wage.ToString( "N0" ) );

	/// <summary>A listed row activated (see <see cref="StaffRows"/>).</summary>
	internal void ActivateStaffRow( int index )
	{
		StaffRows();
		if ( index >= 0 && index < staffRowActions.Count )
			staffRowActions[index]?.Invoke();
	}

	/// <summary>A candidate's row activated: a staff member of any role goes to the hand for placing.</summary>
	private void Hire( int slot )
	{
		var refusal = level.BeginTpiHire( slot );
		if ( refusal == null )
		{
			CloseStaffPanel();
			DisarmBuild();
			PostMessage( T( OpenTpwText.TpiStaffPlaceHint ) );
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
		// [BIN:TPI-EXE:0x4D4F8E silent drop] an object's redirect without a cell ends the drop with no message
		else if ( !level.LastTpiDropSilent )
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
		trainingMember = null;
		researchView = ResearchView.None;
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
