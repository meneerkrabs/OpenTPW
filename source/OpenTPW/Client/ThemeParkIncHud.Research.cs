using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The text HUD's research rows in the staff panel (<c>[EXT:tpi-text-hud]</c>): the lab's two slots (the panel <c>0x575570</c>), "Start New Project" (<c>0x575F98</c>), the project chooser (<c>0x574180</c>), the team
/// window (<c>0x5728C0</c>) and the cancel (<c>0x576DC0</c>). The original's windows are not built; the rules they apply are the lab's (<see cref="ThemeParkIncResearch"/>).
/// </summary>
public sealed partial class ThemeParkIncHud
{
	private enum ResearchView { None, Lab, Chooser, Team }

	private ResearchView researchView;
	private int researchSlot;
	/// <summary>The chosen project and consultant of the slot being set up (the original's <c>0xA67C24</c> and <c>0xA67C2C</c>).</summary>
	private int pendingProject = -1;
	private ushort pendingConsultant;
	/// <summary>The team being assigned in the team rows.</summary>
	private readonly List<ushort> pendingTeam = new();
	/// <summary>A cancel (334/335, 333) asked once: the same row again confirms it; -1 for none.</summary>
	private int cancelAsked = -1;

	private string ProjectName( ThemeParkIncResearch research, int index )
	{
		var project = research.Projects[index];
		var entry = level.Objects.Catalog.Entries.FirstOrDefault( item => item.Settings.GetInt( "Info.Id" ) == project.ObjectId );
		var name = entry != null ? ItemNameOf( entry ) : strings.Entry( 318 );
		return project.Category == 4 ? $"{name} ({strings.Entry( 319 )} {project.Level})" : name;
	}

	/// <summary>The lab rows (below the members, or the open research view's rows).</summary>
	private void ResearchRows( Action<string, Action?> row )
	{
		if ( level.Park?.Economy is not { TpiResearch: { } research, TpiStaff: { } staff } )
			return;
		if ( researchView == ResearchView.None )
		{
			row( strings.Entry( 285 ), OpenResearch );
			return;
		}
		switch ( researchView )
		{
			case ResearchView.Lab:
				row( strings.Entry( 285 ), null );
				for ( var slot = 0; slot < ThemeParkIncResearch.Slots; slot++ )
				{
					var current = slot;
					var index = research.SlotProjects[slot];
					if ( index < 0 )
					{
						row( strings.Entry( 291 ), null );
						continue;
					}
					var status = research.SlotFinished( slot ) ? strings.Entry( 305 )
						: $"{strings.Entry( 295 )}{research.Percent( index )}% - {strings.Entry( 298 )}{research.Team( slot ).Count( id => id != 0 )}";
					row( $"{ProjectName( research, index )}: {status}", () => EditProject( research, current ) );
					row( $"{strings.Entry( 288 )}: {ProjectName( research, index )}", () => CancelProject( research, current ) );
				}
				row( strings.Entry( 290 ), () => StartNewProject( research ) );
				break;
			case ResearchView.Chooser:
				row( strings.Entry( 307 ), null );
				foreach ( var index in research.Offered( researchSlot, level.ThemeParkIncTechLevel, level.ThemeParkIncBuilt ) )
				{
					var chosen = index;
					var project = research.Projects[index];
					row( $"{ProjectName( research, index )} - {strings.Entry( 310 )} ${(long)((double)project.CashPerBrainPoint * project.Cost)}", () => ChooseProject( research, staff, chosen ) );
				}
				break;
			case ResearchView.Team:
				row( $"{strings.Entry( 321 )}{ProjectName( research, pendingProject )}", null );
				row( pendingConsultant == 0 ? strings.Entry( 325 )
					: $"{strings.Entry( 324 )} {(level.Park.Economy.TpiMemberOfThing( pendingConsultant ) is { } consultant ? strings.StaffName( (int)consultant.Role, consultant.NameIndex ) : "")}", null );
				// [BIN:TPI-EXE:0x573A80 team list] only researchers (0x62AF70 == 4), in any state, not on the other slot's team
				var other = research.Team( researchSlot == 0 ? 1 : 0 );
				foreach ( var member in staff.Members.Where( member => member.Role == ThemeParkIncStaffRole.Researcher && member.ThingId != 0 && !other.Contains( (ushort)member.ThingId ) ) )
				{
					var id = (ushort)member.ThingId;
					row( $"{(pendingTeam.Contains( id ) ? "[x]" : "[ ]")} {strings.StaffName( (int)member.Role, member.NameIndex )}", () => ToggleTeam( id ) );
				}
				row( strings.Help( ThemeParkIncText.HelpOk ), () => CommitTeam( research ) );
				break;
		}
		row( T( OpenTpwText.TpiClose ), () =>
		{
			researchView = researchView == ResearchView.Lab ? ResearchView.None : ResearchView.Lab;
			cancelAsked = -1;
		} );
	}

	/// <summary>The panel's opening (<c>0x575570</c>): with no scientist in the park, "No Scientists!" (328/329) and no panel.</summary>
	// [BIN:TPI-EXE:0x575570 research panel] park->0x5B2DB0(8) == 0 shows 328/329 and returns; no research room or lab-active test
	private void OpenResearch()
	{
		if ( level.Park?.Economy.TpiStaff?.Members.Any( member => member.Role == ThemeParkIncStaffRole.Researcher ) != true )
		{
			PostMessage( $"{strings.Entry( 328 )} {strings.Entry( 329 )}" );
			return;
		}
		researchView = ResearchView.Lab;
		cancelAsked = -1;
	}

	private void StartNewProject( ThemeParkIncResearch research )
	{
		var slot = research.StartNewProject();
		if ( slot < 0 )
		{
			PostMessage( $"{strings.Entry( 336 )} {strings.Entry( 337 )}" );
			return;
		}
		researchSlot = slot;
		researchView = ResearchView.Chooser;
	}

	/// <summary>A chooser row (<c>0x575180</c>): the consultant is the automatic pick; a project that needs one and finds none says "No Consultant!" (330/331) and the chooser stays.</summary>
	// [BIN:TPI-EXE:0x575180 project chosen] pending = the row; role 7: consultant 0, the team window; else 0x61D970: a pick opens the team window, none shows 330/331 and the chooser is rebuilt
	private void ChooseProject( ThemeParkIncResearch research, ThemeParkIncStaff staff, int index )
	{
		pendingProject = index;
		var project = research.Projects[index];
		pendingConsultant = ThemeParkIncResearch.PickConsultant( project, staff.Members );
		if ( ThemeParkIncResearch.RoleOf( project.StaffRqd ) != ThemeParkIncResearch.NoRole && pendingConsultant == 0 )
		{
			PostMessage( $"{strings.Entry( 330 )} {strings.Entry( 331 )}" );
			return;
		}
		pendingTeam.Clear();
		pendingTeam.AddRange( research.Team( researchSlot ).Where( id => id != 0 ) );
		researchView = ResearchView.Team;
		cancelAsked = -1;
	}

	/// <summary>"Edit Project": the team rows of the slot's project. The original's edit commits the last project chosen in the slot's chooser (<c>0xA67C24</c>), the slot's own project here.</summary>
	private void EditProject( ThemeParkIncResearch research, int slot )
	{
		researchSlot = slot;
		pendingProject = research.SlotProjects[slot];
		pendingConsultant = research.Consultants[slot];
		pendingTeam.Clear();
		pendingTeam.AddRange( research.Team( slot ).Where( id => id != 0 ) );
		researchView = ResearchView.Team;
		cancelAsked = -1;
	}

	private void ToggleTeam( ushort id )
	{
		if ( !pendingTeam.Remove( id ) && pendingTeam.Count < ThemeParkIncResearch.TeamSize )
			pendingTeam.Add( id );
	}

	/// <summary>The team window's OK (<c>0x5725A0</c>): a team commits (<c>0x573CD0</c>: the project, the consultant, the team); no scientist asks 333 and, confirmed, cancels the project.</summary>
	// [BIN:TPI-EXE:0x573CD0 team commit] pending > -1: 0x61EFA0(pending, slot), the consultant copy, 0x61EB80(list, slot); [BIN:TPI-EXE:0x573C00 empty team] 334/333, yes: 0x576DC0(slot)
	private void CommitTeam( ThemeParkIncResearch research )
	{
		if ( pendingTeam.Count == 0 )
		{
			if ( cancelAsked != researchSlot )
			{
				cancelAsked = researchSlot;
				PostMessage( $"{strings.Entry( 334 )} {strings.Entry( 333 )}" );
				return;
			}
			research.ClearSlot( researchSlot );
		}
		else if ( pendingProject > -1 )
		{
			research.SetProject( researchSlot, pendingProject );
			research.SetConsultant( researchSlot, pendingConsultant );
			research.SetTeam( researchSlot, pendingTeam.Concat( Enumerable.Repeat( (ushort)0, ThemeParkIncResearch.TeamSize - pendingTeam.Count ) ).ToArray() );
		}
		researchView = ResearchView.Lab;
		cancelAsked = -1;
	}

	/// <summary>"Cancel Project" (334/335, yes: <c>0x576DC0</c> -> <c>0x61F560</c>): the same row again confirms.</summary>
	private void CancelProject( ThemeParkIncResearch research, int slot )
	{
		if ( cancelAsked != slot )
		{
			cancelAsked = slot;
			PostMessage( $"{strings.Entry( 334 )} {strings.Entry( 335 )}" );
			return;
		}
		research.ClearSlot( slot );
		cancelAsked = -1;
	}
}
