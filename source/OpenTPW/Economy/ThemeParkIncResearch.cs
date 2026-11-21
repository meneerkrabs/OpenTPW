using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>One research project of Theme Park Inc's lab (<c>+0x78 + 0x24 × i</c>, serializer <c>0x61F670</c>).</summary>
/// <param name="StaffRqd">+0x00: the consultant's staff type (<c>Research.RStaff[0].SType</c>); 0 or above 6 asks for none.</param>
/// <param name="StaffSA">+0x04: the consultant's ability (<c>Research.RStaff[0].SA</c>).</param>
/// <param name="Category">+0x08: 0 ride, 1 shop, 2 sideshow, 3 feature, 4 upgrade, 5 park expansion (the obstacle), 6 facility.</param>
/// <param name="Type">+0x0C: <c>Research.Type</c>, kept as the save holds it.</param>
/// <param name="ObjectId">+0x10: the object type.</param>
/// <param name="Group">+0x12: <c>Research.Group</c>.</param>
/// <param name="Level">+0x13: 0, or the upgrade level of a level project.</param>
/// <param name="Cost">+0x14: the brain points it takes.</param>
/// <param name="CashPerBrainPoint">+0x18: the cash each point costs.</param>
/// <param name="Done">+0x1C: the brain points done.</param>
public readonly record struct ThemeParkIncLabProject( int StaffRqd, int StaffSA, int Category, uint Type, ushort ObjectId, byte Group, byte Level, float Cost, int CashPerBrainPoint, float Done );

/// <summary>An object type's research words (object info): <c>Research.Category</c> (+0x254), <c>Research.LinkedWith</c> (+0x24C) and the three levels' <c>Upgrades[n].CostOfResearch</c> / <c>CashPerBrainPoint</c>.</summary>
public sealed record ThemeParkIncResearchInfo( int Id, int Category, int LinkedWith, int[] CostOfResearch, int[] CashPerBrainPoint );

/// <summary>One object control's research words (the control manager <c>W+0x1E9618</c>, <c>0x5CFCF0</c>): researched (+0x10) and the researched upgrade level (+0x14).</summary>
public sealed record ThemeParkIncObjectResearch( int Id, bool Researched, int Level );

/// <summary>The saved research lab and object controls.</summary>
public sealed record ThemeParkIncResearchSnapshot( ThemeParkIncLabProject[] Projects, int Count, int[] SlotProjects, int[] Consultants, int[] Teams, byte[] Available,
	int WorkLoad, uint[] Groups, byte[] HighestGroups, uint MostExpensive, bool LabActive, uint[] Tail, ThemeParkIncObjectResearch[] Controls );

/// <summary>
/// Theme Park Inc's research lab (thing class 14, <c>park+0x64EB20</c>; docs/reverse/TPI-STAFF.md §12.3) and the object controls' researched words it writes: two player-chosen project slots, a team of up to
/// 20 scientists and a consultant per slot, brain points every sixth world tick from the scientists working in a research room (state 24), a cash cost per point, and on completion message 0x11 (the
/// career is told; a Park Expansion project posts 0x23).
/// </summary>
public sealed class ThemeParkIncResearch
{
	public const int ProjectCapacity = 256, TeamSize = 20, Slots = 2, Categories = 7;

	/// <summary>The researcher's state while he works in a research room ("thinking space").</summary>
	public const int Researching = 24;

	/// <summary>The consultant role 0x576FE0 answers for a project that needs none.</summary>
	public const int NoRole = 7;

	private readonly ThemeParkIncLabProject[] projects = new ThemeParkIncLabProject[ProjectCapacity];
	private readonly int[] slotProjects = { -1, -1 };
	private readonly ushort[] consultants = new ushort[Slots];
	private readonly ushort[][] teams = { new ushort[TeamSize], new ushort[TeamSize] };
	private readonly byte[] available = new byte[Slots];
	private readonly uint[] groups = new uint[Categories * 3];
	private readonly byte[] highestGroups = new byte[Categories];
	private readonly uint[] tail = new uint[4];
	private readonly List<ThemeParkIncObjectResearch> controls = new();

	/// <param name="speedScaleDown"><c>Research.SpeedScaleDown</c> (G <c>0xA02594</c>), a single.</param>
	/// <param name="costScaleUp"><c>Research.CostScaleUp</c> (G <c>0xA02598</c>), a single.</param>
	/// <param name="workLoad"><c>Research.StartingWorkLoad</c> (G <c>0xA02590</c>), the lab's starting workload.</param>
	public ThemeParkIncResearch( float speedScaleDown, float costScaleUp, int workLoad )
	{
		SpeedScaleDown = speedScaleDown;
		CostScaleUp = costScaleUp;
		WorkLoad = workLoad;
	}

	/// <summary>Reads the three <c>Research.*</c> keys of Standard.sam.</summary>
	public static ThemeParkIncResearch? FromSettings( SamSettings standard ) =>
		standard.Contains( "Research.SpeedScaleDown" ) && standard.Contains( "Research.CostScaleUp" ) && standard.Contains( "Research.StartingWorkLoad" )
			? new ThemeParkIncResearch( (float)standard.GetDouble( "Research.SpeedScaleDown" ), (float)standard.GetDouble( "Research.CostScaleUp" ), standard.GetInt( "Research.StartingWorkLoad" ) )
			: null;

	public float SpeedScaleDown { get; }
	public float CostScaleUp { get; }

	/// <summary><c>+0x24EC</c>: the workload (<c>mWorkLoad</c>).</summary>
	public int WorkLoad { get; private set; }

	/// <summary><c>+0x24E8</c>: the projects in use.</summary>
	public int Count { get; private set; }

	public IReadOnlyList<ThemeParkIncLabProject> Projects => projects;

	/// <summary><c>+0x14[2]</c>: the project index per slot (0 New Items, 1 Objectives/Upgrades); below 0 for none (the lab's start leaves -4).</summary>
	public IReadOnlyList<int> SlotProjects => slotProjects;

	/// <summary><c>+0x1C[2]</c>: the consultant's staff thing per slot, 0 for none.</summary>
	public IReadOnlyList<ushort> Consultants => consultants;

	/// <summary><c>+0x24[20]</c> and <c>+0x4C[20]</c>: the team of each slot (staff thing ids, 0 for none).</summary>
	public IReadOnlyList<ushort> Team( int slot ) => teams[slot == 0 ? 0 : 1];

	/// <summary><c>+0x24F4</c>: the park has researchers (<c>mLabActive</c>).</summary>
	public bool LabActive { get; private set; }

	/// <summary>The object types' research words, in the type list's order (<c>0xA09AC8</c>): the level's catalogue fills them.</summary>
	public IReadOnlyList<ThemeParkIncResearchInfo> Infos { get; set; } = Array.Empty<ThemeParkIncResearchInfo>();

	/// <summary>The object controls' researched words in the control table's order (the first record of an id wins, <c>0x5CFCF0</c>).</summary>
	public IReadOnlyList<ThemeParkIncObjectResearch> Controls => controls;

	/// <summary>Whether the controls are a park file's (the catalogue gates on them only then).</summary>
	public bool HasControls => controls.Count > 0;

	/// <summary>The object control of <paramref name="id"/> (<c>0x5CFCF0</c>): the first record of the id; the original returns none for an unknown id and its callers fault.</summary>
	private int ControlIndex( int id )
	{
		var index = controls.FindIndex( control => control.Id == id );
		return index >= 0 ? index : throw new InvalidOperationException( $"Object type {id} has no object control (the original's 0x5CFCF0 returns none and its caller faults)." );
	}

	/// <summary>Whether the object type is researched (control <c>+0x10</c>); true for a type without a control in a park without controls.</summary>
	public bool IsResearched( int id ) => !HasControls || controls.FirstOrDefault( control => control.Id == id ) is { Researched: true };

	private void MarkResearched( int id, int? level )
	{
		var index = ControlIndex( id );
		controls[index] = controls[index] with { Researched = true, Level = level ?? controls[index].Level };
	}

	/// <summary>The consultant role of a project's <c>StaffRqd</c> (<c>0x576FE0</c>): 1 gardener, 2 handyman, 3 mechanic, 4 entertainer, 5 guard, 6 researcher, any other none (7).</summary>
	// [BIN:TPI-EXE:0x576FE0 consultant role] the table 0x577028: 0 -> 7, 1 -> 5, 2 -> 0, 3 -> 1, 4 -> 2, 5 -> 3, 6 -> 4, anything else 7 (the role codes of 0x62AF70)
	public static int RoleOf( int staffRqd ) => staffRqd switch
	{
		1 => (int)ThemeParkIncStaffRole.Gardener,
		2 => (int)ThemeParkIncStaffRole.Handyman,
		3 => (int)ThemeParkIncStaffRole.Mechanic,
		4 => (int)ThemeParkIncStaffRole.Entertainer,
		5 => (int)ThemeParkIncStaffRole.Guard,
		6 => (int)ThemeParkIncStaffRole.Researcher,
		_ => NoRole
	};

	/// <summary>
	/// The lab's thing update (<c>0x61E980</c>): every sixth world tick, each slot with a project and a consultant (or a project that needs none) gains the brain points of its team's members in state 24.
	/// </summary>
	// [BIN:TPI-EXE:0x61E980 lab tick] (W+0x1EAB40) % 6 (signed idiv) != 0 returns; slot 0 then 1: p = +0x14[slot], p <= -1 skips; consultant word 0 with 0x576FE0(StaffRqd) != 7 skips; sum = 0.0f;
	// for the 20 ids of the slot's team: a thing whose state +0x1B4 is 24 (no kind or listing test) adds (float)((ld)0x62E4C0(T) × (ld)(int)+0x24EC × (ld)SpeedScaleDown + sum) (fild, fimul, fmuls, fadds,
	// fstps); then 0x61FF80(&project, sum, slot), also with 0 points. No draw
	// [BIN:TPI-EXE:0x637716 lab dispatch] the kind-14 update after the world tick's increment (0x64945D), so the tick it reads is the turn's
	public void Tick( long turn, Func<ushort, ThemeParkIncStaffMember?> member, ThemeParkIncStaffSettings settings, Action<int> debit, Action<int, int> researched )
	{
		if ( unchecked( (int)turn ) % 6 != 0 )
			return;
		for ( var slot = 0; slot < Slots; slot++ )
		{
			var index = slotProjects[slot];
			if ( index <= -1 )
				continue;
			var project = projects[index];
			if ( consultants[slot] == 0 && RoleOf( project.StaffRqd ) != NoRole )
				continue;
			var sum = 0f;
			foreach ( var id in teams[slot] )
			{
				if ( id != 0 && member( id ) is { State: Researching } scientist )
				{
					var work = settings.WorkDuration( scientist.Role, scientist.Experience, scientist.Motivation, scientist.Salary );
					sum = (float)((double)work * WorkLoad * SpeedScaleDown + sum);
				}
			}
			Advance( index, sum, slot, debit, researched );
		}
	}

	/// <summary>
	/// A project's advance (<c>0x61FF80</c>): an unfinished project gains the points (at most its cost) and the bank pays them; reaching the cost marks the object researched, posts message 0x11, marks
	/// the types linked with it and releases the team. A finished project only releases its team.
	/// </summary>
	// [BIN:TPI-EXE:0x61FF80 research advance] !(done < cost): 0x61F590(slot), return; !(points + done <= cost): points = (float)(cost - done); done = (float)(points + done); cash = trunc((ld)(int)
	// CashPerBrainPoint × points) (0x86FAB0, the low dword); the bank 0x64EBB0 -> 0x5C07B0(cash), also for 0; done >= cost: control(objId)+0x10 = 1, +0x14 = the level byte (0x620042, 0x620062),
	// message 0x11 (0x4359E0: +8 the object, +0xC the category) to the queue 0xA09728, every info whose LinkedWith (+0x24C) is the object gets control +0x10 = 1 (its +0x14 untouched), 0x61F590(slot)
	private void Advance( int index, float points, int slot, Action<int> debit, Action<int, int> researched )
	{
		var project = projects[index];
		if ( !(project.Done < project.Cost) )
		{
			ReleaseTeam( slot );
			return;
		}
		if ( !(points + project.Done <= project.Cost) )
			points = project.Cost - project.Done;
		var done = points + project.Done;
		projects[index] = project with { Done = done };
		debit( unchecked( (int)(long)((double)project.CashPerBrainPoint * points) ) );
		if ( done >= project.Cost )
		{
			MarkResearched( project.ObjectId, project.Level );
			researched( project.ObjectId, project.Category );
			foreach ( var info in Infos )
				if ( info.LinkedWith == project.ObjectId )
					MarkResearched( info.Id, null );
			ReleaseTeam( slot );
		}
	}

	/// <summary>Releases a slot's team (<c>0x61F590</c>): every member is no longer listed and the team empties; the researchers keep their state.</summary>
	// [BIN:TPI-EXE:0x61F590 team release] slot 0 the team at +0x24, any other slot the one at +0x4C: each id's +0x274 = 0 and the entry cleared
	public void ReleaseTeam( int slot, Func<ushort, ThemeParkIncStaffMember?>? member = null )
	{
		var team = teams[slot == 0 ? 0 : 1];
		for ( var index = 0; index < TeamSize; index++ )
		{
			if ( team[index] != 0 && (member ?? Member)?.Invoke( team[index] ) is { } listed )
				listed.Listed = false;
			team[index] = 0;
		}
	}

	/// <summary>The staff by thing id, for the team setters (the park's members; null in a lab without staff).</summary>
	public Func<ushort, ThemeParkIncStaffMember?>? Member { get; set; }

	/// <summary>Sets a slot's team (<c>0x61EB80</c>): the old members are no longer listed, the 20 new ids are copied and their members listed. False for a slot other than 0 or 1.</summary>
	// [BIN:TPI-EXE:0x61EB80 team setter] slot 0 the team +0x24, slot 1 +0x4C, any other slot nothing; the old entries' +0x274 = 0, the 20 words copied, each new id's +0x274 = 1; answers 1
	public bool SetTeam( int slot, IReadOnlyList<ushort> ids )
	{
		if ( slot is not (0 or 1) || ids.Count != TeamSize )
			return false;
		var team = teams[slot];
		foreach ( var id in team )
			if ( id != 0 && Member?.Invoke( id ) is { } old )
				old.Listed = false;
		for ( var index = 0; index < TeamSize; index++ )
		{
			team[index] = ids[index];
			if ( ids[index] != 0 && Member?.Invoke( ids[index] ) is { } listed )
				listed.Listed = true;
		}
		return true;
	}

	/// <summary>Sets a slot's project (<c>0x61EFA0</c>): slot 1 the second word, any other slot the first.</summary>
	// [BIN:TPI-EXE:0x61EFA0 project setter] slot 1 -> +0x18 = p, else +0x14 = p; then 0x42A3B0()->+0x29 = 0 (a career byte whose meaning is unread, not modelled)
	public void SetProject( int slot, int project )
	{
		if ( project >= Count )
			throw new ArgumentOutOfRangeException( nameof( project ), "No such research project." );
		slotProjects[slot == 1 ? 1 : 0] = project;
	}

	/// <summary>Sets a slot's consultant word (the panel's commit, <c>0x573D85</c>).</summary>
	public void SetConsultant( int slot, ushort thing ) => consultants[slot == 1 ? 1 : 0] = thing;

	/// <summary>Clears a slot (<c>0x61F560</c>): no project, no consultant, the team released.</summary>
	// [BIN:TPI-EXE:0x61F560 slot clear] +0x14[slot] = -1, +0x1C[slot] = 0, 0x61F590(slot)
	public void ClearSlot( int slot )
	{
		slotProjects[slot == 1 ? 1 : 0] = -1;
		consultants[slot == 1 ? 1 : 0] = 0;
		ReleaseTeam( slot );
	}

	/// <summary>
	/// The consultant a project needs (<c>0x61D970</c>): none for a project without a consultant role; else with an ability the first staff member of any role who has it, without one the first member of
	/// the role (a researcher is never picked that way). <paramref name="members"/> are the staff in the staff list's order.
	/// </summary>
	// [BIN:TPI-EXE:0x61D970 consultant pick] role = 0x576FE0(StaffRqd); 7 -> 0; the staff list (0x649060 first, 0x6490A0 next) over kinds 4, 5, 6, 8, 7, 0x14: StaffSA != 0 -> the first with +0x218 == SA
	// (0x62B7D0), any role; StaffSA 0 -> the first of the role's kind by the table 0x61DA68 (0 -> 5, 1 -> 4, 2 -> 6, 3 -> 7, 5 -> 0x14, the researcher none); the slot and the leaver are not read
	public static ushort PickConsultant( ThemeParkIncLabProject project, IEnumerable<ThemeParkIncStaffMember> members )
	{
		var role = RoleOf( project.StaffRqd );
		if ( role == NoRole )
			return 0;
		var found = project.StaffSA != 0
			? members.FirstOrDefault( member => member.Ability == project.StaffSA )
			: role == (int)ThemeParkIncStaffRole.Researcher ? null : members.FirstOrDefault( member => (int)member.Role == role );
		return found is { } consultant ? (ushort)consultant.ThingId : (ushort)0;
	}

	/// <summary>
	/// Message 0x21 for a staff member's thing destroyed (<c>0x61DAD0</c>): a scientist leaves both teams; another member who consults on a slot is replaced by the pick.
	/// </summary>
	// [BIN:TPI-EXE:0x61DAD0 staff left] kind 8: the id cleared from both teams (+0x274 and the consultants untouched); another kind: each slot whose consultant is the id takes 0x61D970 for its project
	public void StaffLeft( ThemeParkIncStaffMember member, IEnumerable<ThemeParkIncStaffMember> members )
	{
		var id = (ushort)member.ThingId;
		if ( member.Role == ThemeParkIncStaffRole.Researcher )
		{
			foreach ( var team in teams )
				for ( var index = 0; index < TeamSize; index++ )
					if ( team[index] == id )
						team[index] = 0;
			return;
		}
		for ( var slot = 0; slot < Slots; slot++ )
			if ( consultants[slot] == id && slotProjects[slot] >= 0 )
				consultants[slot] = PickConsultant( projects[slotProjects[slot]], members );
	}

	/// <summary>
	/// Message 0x21 for a research room's thing destroyed (<c>0x61DB90</c>): both availability bytes clear, every team member is no longer listed and both teams empty, whichever room went.
	/// </summary>
	// [BIN:TPI-EXE:0x61DB90 room removed] the word +0x74 = 0; every member of both teams 0x62E240(T, 0) (a researcher: +0x27E = +0x274 = 0 only); both teams cleared
	public void ResearchRoomRemoved()
	{
		available[0] = available[1] = 0;
		for ( var slot = 0; slot < Slots; slot++ )
			ReleaseTeam( slot );
	}

	/// <summary>The lab's active flag (<c>0x61E4E0</c>): the park has researchers. True when it changed.</summary>
	// [BIN:TPI-EXE:0x61E4E0 lab active] park+0x1EAB84 (the first researcher) != 0 -> 0x61E320 (0 -> 1 and message 0x435AF0(5)) else 0x61E400 (1 -> 0 and message 6); called by both researcher constructors
	// and the removal 0x61B520; messages 5 and 6 reach the advisor, whose reaction is not ported
	public bool UpdateLabActive( bool hasResearchers )
	{
		if ( LabActive == hasResearchers )
			return false;
		LabActive = hasResearchers;
		return true;
	}

	/// <summary>
	/// The Park Expansion project of the mission's obstacle (<c>0x61CBF0</c>, the controller's registration and every message 0x27): with an obstacle mission whose obstacle stands not yet researched
	/// (state 1) and whose type is the viewed zone's, a project of category 5 costing the mission's <c>Obstacle.ResearchEffort</c>, one cash a point and no consultant, unless any project names the type.
	/// </summary>
	// [BIN:TPI-EXE:0x61CBF0 obstacle project] HasObstacle (0x5F0190); 0x5F01D0's type, obstacle word (!= 0) and state (== 1); the viewed zone 0x42A340: 0 type >= 0xE74, 1 type <= 0x708, 2 type 0xA8C..0xAF0
	// (unsigned words), another zone returns; with projects, any whose objId (+0x10) is the type returns; the record at the count: objId, group = the type's info +0x250, cost = (float)(int)
	// Obstacle.ResearchEffort (mission +0x2D4, no CostScaleUp), CashPerBrainPoint 1, level 0, done 0, category 5, StaffRqd 0, StaffSA 0 (mType not written); the count + 1, without a capacity test
	public bool RegisterObstacle( bool hasObstacle, int type, ushort obstacle, int state, int viewedZone, int researchEffort, byte group )
	{
		if ( !hasObstacle || obstacle == 0 )
			return false;
		var word = (ushort)type;
		var inZone = viewedZone switch
		{
			0 => word >= 0xE74,
			1 => word <= 0x708,
			2 => word is >= 0xA8C and <= 0xAF0,
			_ => false
		};
		if ( !inZone || state != 1 )
			return false;
		for ( var index = 0; index < Count; index++ )
			if ( projects[index].ObjectId == word )
				return false;
		if ( Count >= ProjectCapacity )
			throw new InvalidOperationException( $"The research lab's {ProjectCapacity} projects are full (the original would write past them)." );
		projects[Count] = projects[Count] with { ObjectId = word, Group = group, Cost = researchEffort, CashPerBrainPoint = 1, Level = 0, Done = 0f, Category = 5, StaffRqd = 0, StaffSA = 0 };
		Count++;
		return true;
	}

	/// <summary>The first unfinished Park Expansion project (<c>0x61EB30</c>), -1 for none: what the mission panel reports.</summary>
	public int ObstacleProject()
	{
		for ( var index = 0; index < Count; index++ )
			if ( projects[index].Category == 5 && projects[index].Done < projects[index].Cost )
				return index;
		return -1;
	}

	/// <summary>
	/// The next upgrade level's project of a researched object (<c>0x61FE70</c>, after message 0x11): levels without a research cost count as researched at once; the first with one becomes a project.
	/// Null when no project is added, else the project to append.
	/// </summary>
	// [BIN:TPI-EXE:0x61FE70 level project] info +0x254 == 4 or LinkedWith != 0: none; for L = control +0x14 + 1 while L < 3: Upgrades[L].CostOfResearch (+0x270 + 0x48 L) > 0 makes the project, else control
	// +0x14 = L; the project: level L, group 0, category 4, cost = (float)(cost × CostScaleUp), cash = Upgrades[L].CashPerBrainPoint, objId, done 0, StaffRqd = StaffSA = 0 (mType not written); a cash of
	// 0 calls the CRT rand() at 0x61FF1A, drops its result and takes 1
	private ThemeParkIncLabProject? NextLevel( ThemeParkIncResearchInfo info, out bool needsDraw )
	{
		needsDraw = false;
		if ( info.Category == 4 || info.LinkedWith != 0 )
			return null;
		var control = ControlIndex( info.Id );
		for ( var level = controls[control].Level + 1; level < 3; level++ )
		{
			if ( info.CostOfResearch[level] > 0 )
			{
				needsDraw = info.CashPerBrainPoint[level] == 0;
				return projects[Count] with { Level = (byte)level, Group = 0, Category = 4, Cost = (float)((double)info.CostOfResearch[level] * CostScaleUp),
					CashPerBrainPoint = needsDraw ? 1 : info.CashPerBrainPoint[level], ObjectId = (ushort)info.Id, Done = 0f, StaffRqd = 0, StaffSA = 0 };
			}
			controls[control] = controls[control] with { Level = level };
		}
		return null;
	}

	/// <summary>
	/// Message 0x11's lab side (<c>0x61DC20</c>): after the career is told and a Park Expansion posted 0x23 (the caller's), the next upgrade level's project of the object is appended. True when one was.
	/// </summary>
	// [BIN:TPI-EXE:0x61DE15 level append] info = 0x43C700(type); 0x61FE70(&projects[count], info) true -> count + 1
	// [EXT:tpi-capability-gates] a level project whose Upgrades[n].CashPerBrainPoint is 0 (every shipped object: no WAD sets the key) is made after a CRT rand() whose result is dropped (0x61FF1A); that draw
	// awaits the user's decision (OpenTPW models no C runtime stream), so such a project is not appended: the object's next level stays unresearched
	public bool AppendNextLevel( int objectId )
	{
		if ( Infos.FirstOrDefault( info => info.Id == objectId ) is not { } info )
			throw new InvalidOperationException( $"Object type {objectId} has no object info (the original's 0x43C700 returns none and its caller faults)." );
		if ( NextLevel( info, out var needsDraw ) is not { } project || needsDraw )
			return false;
		if ( Count >= ProjectCapacity )
			throw new InvalidOperationException( $"The research lab's {ProjectCapacity} projects are full (the original would write past them)." );
		projects[Count++] = project;
		return true;
	}

	/// <summary>Takes a Theme Park Inc park file's lab (load <c>0x61BCE0</c>) and object controls (<c>0x5CF9B0</c>).</summary>
	// [BIN:TPI-EXE:0x61BCE0 lab load] the 256 project records (0x61F670), the seven category records (0x6202C0), mNumProjects, mWorkLoad, mMostExpensiveProject, mLabActive, pcurrentp (+0x14), scurrentp
	// (+0x18), pavail (+0x74), savail (+0x75), lPrits, lPricat, lPrirt, lSeccat (+0x24F8..+0x2504, read and written by nothing else), the 20 interleaved team words (+0x24 + 2i, +0x4C + 2i) and the
	// consultants (+0x1C, +0x1E); the prebuilt holds -4 in both slots, the lab start's value (0x61BC84), which every reader takes for none
	internal void Import( ThemeParkIncResearchLab lab, ThemeParkIncObjectControls? saved )
	{
		for ( var index = 0; index < ProjectCapacity; index++ )
		{
			var p = lab.Projects[index];
			projects[index] = new ThemeParkIncLabProject( unchecked( (int)p.StaffRqd ), unchecked( (int)p.StaffSA ), unchecked( (int)p.Cat ), p.Type, p.ObjectId, p.ResearchGroup, p.UpgradeLevel,
				BitConverter.UInt32BitsToSingle( p.Cost ), unchecked( (int)p.CashPerBrainPoint ), BitConverter.UInt32BitsToSingle( p.ResearchDone ) );
		}
		if ( lab.NumProjects > ProjectCapacity )
			throw new InvalidDataException( "The park file's research lab counts more than 256 projects." );
		Count = (int)lab.NumProjects;
		WorkLoad = unchecked( (int)lab.WorkLoad );
		for ( var category = 0; category < Categories; category++ )
		{
			var group = lab.Groups[category];
			(groups[3 * category], groups[3 * category + 1], groups[3 * category + 2], highestGroups[category]) = (group.Effort, group.Output, group.CurrentProject, group.HighestGroupAvailable);
		}
		MostExpensive = lab.MostExpensiveProject;
		LabActive = lab.LabActive;
		slotProjects[0] = unchecked( (int)lab.Pcurrentp );
		slotProjects[1] = unchecked( (int)lab.Scurrentp );
		(available[0], available[1]) = (lab.Pavail, lab.Savail);
		(tail[0], tail[1], tail[2], tail[3]) = (lab.LPrits, lab.LPricat, lab.LPrirt, lab.LSeccat);
		for ( var index = 0; index < TeamSize; index++ )
			(teams[0][index], teams[1][index]) = (lab.Assignments[index].Pristaf, lab.Assignments[index].Secstaf);
		(consultants[0], consultants[1]) = (lab.Consultants[0], lab.Consultants[1]);
		controls.Clear();
		if ( saved != null )
			foreach ( var control in saved.Controls )
				controls.Add( new ThemeParkIncObjectResearch( control.Id, control.Researched, unchecked( (int)control.ResearchLevel ) ) );
	}

	/// <summary><c>+0x24F0</c>: <c>mMostExpensiveProject</c>, kept as the save holds it.</summary>
	public uint MostExpensive { get; private set; }

	internal ThemeParkIncResearchSnapshot Capture() => new( (ThemeParkIncLabProject[])projects.Clone(), Count, (int[])slotProjects.Clone(), consultants.Select( id => (int)id ).ToArray(),
		teams.SelectMany( team => team.Select( id => (int)id ) ).ToArray(), (byte[])available.Clone(), WorkLoad, (uint[])groups.Clone(), (byte[])highestGroups.Clone(), MostExpensive, LabActive,
		(uint[])tail.Clone(), controls.ToArray() );

	internal static bool IsValid( ThemeParkIncResearchSnapshot? state ) => state == null || state.Projects is { Length: ProjectCapacity } && state.Count is >= 0 and <= ProjectCapacity
		&& state.SlotProjects is { Length: Slots } slots && slots.All( slot => slot < state.Count ) && state.Consultants is { Length: Slots } && state.Teams is { Length: Slots * TeamSize }
		&& state.Consultants.Concat( state.Teams ).All( id => id is >= 0 and <= ushort.MaxValue ) && state.Available is { Length: Slots } && state.Groups is { Length: Categories * 3 }
		&& state.HighestGroups is { Length: Categories } && state.Tail is { Length: 4 } && state.Controls != null && state.Controls.All( control => control != null );

	internal void Restore( ThemeParkIncResearchSnapshot state )
	{
		if ( !IsValid( state ) )
			throw new InvalidDataException( "Invalid Theme Park Inc research snapshot." );
		state.Projects.CopyTo( projects, 0 );
		Count = state.Count;
		state.SlotProjects.CopyTo( slotProjects, 0 );
		for ( var slot = 0; slot < Slots; slot++ )
		{
			consultants[slot] = (ushort)state.Consultants[slot];
			for ( var index = 0; index < TeamSize; index++ )
				teams[slot][index] = (ushort)state.Teams[slot * TeamSize + index];
		}
		state.Available.CopyTo( available, 0 );
		WorkLoad = state.WorkLoad;
		state.Groups.CopyTo( groups, 0 );
		state.HighestGroups.CopyTo( highestGroups, 0 );
		MostExpensive = state.MostExpensive;
		LabActive = state.LabActive;
		state.Tail.CopyTo( tail, 0 );
		controls.Clear();
		controls.AddRange( state.Controls );
	}
}
