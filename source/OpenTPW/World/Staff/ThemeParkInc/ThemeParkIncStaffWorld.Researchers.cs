namespace OpenTPW;

public sealed partial class ThemeParkIncStaffWorld
{
	/// <summary>The researcher's walk to his research room (state 25); state 24 is <see cref="ThemeParkIncResearch.Researching"/>.</summary>
	public const int ToResearchRoom = 25;

	/// <summary>
	/// The display category of a member (<c>0x62B010</c>): <see cref="Category"/>, except that a researcher in a research state counts researching only while listed in a lab team (+0x274), else idle.
	/// </summary>
	// [BIN:TPI-EXE:0x62B059 research display] states 0x18, 0x19, 0x21 and 0x22 of a kind-8 thing: 7 with +0x274 != 0, else 0
	public static int DisplayCategory( ThemeParkIncStaffMember member )
	{
		var category = Category( member.State );
		return category == 7 && !(member.Role == ThemeParkIncStaffRole.Researcher && member.Listed) ? 0 : category;
	}

	/// <summary>
	/// The researcher's start at the drop (<c>0x61B070</c> after the member's start): no room, the look-for-work sequence and the lab's active flag.
	/// </summary>
	// [BIN:TPI-EXE:0x61B070 researcher constructor] the hire constructor 0x626190 (kind 8: +0x1B4 = 0, +0x27E = 0, 0x62E240(0) leaves +0x274 = 0), +0x27C = 0, +0x5D0 = 0, 0x64F2B0 (the role list), template 3
	// (0x5D1000(3, 0xC, look)), then the sequence of 0x61B820 (the gate, the tired test, the room finder), the lab's 0x61E4E0, 0x62E240(0) (no effect) and the name
	private void StartResearcher( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		member.ResearchRoom = 0;
		member.Listed = false;
		SearchResearchRoom( economy, economy.TpiStaff!.Settings, member, turn );
		economy.TpiResearch?.UpdateLabActive( economy.TpiStaff.Members.Any( other => other.Role == ThemeParkIncStaffRole.Researcher && other.Placed ) );
	}

	/// <summary>The researcher's state handler (<c>0x61B620</c>): states 0 and 1 his own, 24 and 25 the common handler's.</summary>
	// [BIN:TPI-EXE:0x61B620 researcher dispatch] state <= 3 by the table 0x61B774 (0 0x61B73D, 1 0x61B63A, 2 0x629EF0, 3 0x62A1C0), others the common handler 0x6287A0 (24 0x6289BD, 25 0x62DCB0)
	private void DispatchResearcher( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		switch ( member.State )
		{
			case Idle:
				// [BIN:TPI-EXE:0x61B73D state 0] a world draw, its low nibble 0 plays the voice 0xA9; then the idle end 0x62CA10 (look for work 0x62D810)
				Voice( economy, member, 0xA9 );
				if ( unchecked( (uint)turn ) > unchecked( (uint)(member.IdleStamp - IdleTerm( member.Experience, settings.MaxExperience )) ) )
					LookForWork( economy, settings, member, turn );
				break;
			case Walking:
				// [BIN:TPI-EXE:0x61B63A state 1] a world draw for the voice 0xA8; the walk step: still walking takes the moving step 0x62B3E0; arrived or stuck: the gate 0x62BBA0, the tired test, the room
				// finder (state 25) or happiness - 2.0 and state 0 (the walk to the park's entrance after a strike)
				Voice( economy, member, 0xA8 );
				if ( Walk( member ) is WalkResult.Walking )
					member.Step( settings );
				else
					SearchResearchRoom( economy, settings, member, turn );
				break;
			case ToResearchRoom:
				UpdateToResearchRoom( economy, settings, member, turn );
				break;
			case ThemeParkIncResearch.Researching:
				UpdateResearching( economy, settings, member, turn );
				break;
		}
	}

	/// <summary>
	/// The researcher's look for work (<c>0x61B820</c>): the strike and rest gate, then a tired researcher idles, else he walks to the research room the finder reaches, or loses happiness and idles.
	/// </summary>
	// [BIN:TPI-EXE:0x61B820 researcher search] 0x62BBA0 nonzero returns; 0x62B3B0 ("Too tired to research") sets 0; the finder 0x62E0E0 ("Looking for Research Room .."): a room sets 25, none happiness
	// = clamp(happiness - 2.0) (0x61B875) and 0. No team or +0x274 test: a researcher walks to a room whether or not a team lists him
	private void SearchResearchRoom( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		if ( Gate( economy, settings, member, turn ) )
			return;
		if ( member.Tired( settings ) )
		{
			SetState( economy, member, Idle, turn );
			return;
		}
		if ( FindResearchRoom( economy, member ) != 0 )
			SetState( economy, member, ToResearchRoom, turn );
		else
		{
			member.AddHappiness( -2f );
			SetState( economy, member, Idle, turn );
		}
	}

	/// <summary>The research-room finder (<c>0x62E0E0</c>): the first research room, when a walk to its entrance reaches it, becomes the researcher's room. Its thing, or 0.</summary>
	// [BIN:TPI-EXE:0x62E0E0 room finder] the list park+0x1EAB88 to the first thing with +0x42 & 0x200 (no distance, capacity or tie test, no retry of another room); the staff route 0x62EB10 to the centre
	// of its entrance cell (+0x46, 0x62E198): success sets +0x27C = the room ("Found a rest area!") and answers it; a refused walk answers 0 and keeps +0x27C (no 0x60B7F0 fallback here)
	private int FindResearchRoom( ParkEconomy economy, ThemeParkIncStaffMember member )
	{
		if ( Rooms?.First( ThemeParkIncRoomKind.Research ) is not { } room )
			return 0;
		if ( !StaffRoute( economy, member, room.Entrance, GuestWander.CentreByte, GuestWander.CentreByte ) )
			return 0;
		member.ResearchRoom = room.Thing;
		return room.Thing;
	}

	/// <summary>State 25 entered (<c>0x628180</c>'s arm 25): the thought 0x1B and the walk template.</summary>
	// [BIN:TPI-EXE:0x628180 state 25 entry] 0x638960(0x1B, 0) (presentation); template 1 (0x5D1000 without a handle, else 0x5D0F90(1))
	private void EnterToResearchRoom( ThemeParkIncStaffMember member ) => RequestTemplate( member, 1 );

	/// <summary>State 24 entered (<c>0x628180</c>'s arm 24): the stamp, and the researcher disappears into the room.</summary>
	// [BIN:TPI-EXE:0x628180 state 24 entry] +0x278 = the world tick; 0x638D10 (the thought stops, presentation); 0x5D1050 deletes the sprite source
	private static void EnterResearching( ThemeParkIncStaffMember member, long turn )
	{
		member.ResearchStamp = turn;
		member.Sprite.Delete();
	}

	/// <summary>State 25's tick (<c>0x62DCB0</c>): arrived, the room's occupancy rises and he researches (24); stuck, he loses happiness and idles.</summary>
	// [BIN:TPI-EXE:0x62DCB0 walk to room] the walk step 0x60B440: 0 -> the room's script variable 0 += 1 (0x6B0740/0x6B0C90/0x6B0780 on room +0x2C), 0x628180(0x18), 0x5D1050; 2 -> "Staff member couldn't get
	// to his research room", happiness = clamp(happiness - (float)HappyHitCosRoomInaccessible), the thought 0x1E, 0x628180(0) (+0x27C kept); walking -> nothing
	private void UpdateToResearchRoom( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		switch ( Walk( member ) )
		{
			case WalkResult.Arrived:
				Rooms?.AddOccupancy( (ushort)member.ResearchRoom, +1 );
				SetState( economy, member, ThemeParkIncResearch.Researching, turn );
				break;
			case WalkResult.Stuck:
				member.AddHappiness( -(float)settings.HappyHitRoomInaccessible );
				SetState( economy, member, Idle, turn );
				break;
		}
	}

	/// <summary>
	/// State 24's tick (<c>0x6289BD</c>): research costs energy and happiness; once his energy byte is at most RestLevel he leaves the room (occupancy down, shown again), idles and looks for work.
	/// Research ends no other way: a finished project does not send him out.
	/// </summary>
	// [BIN:TPI-EXE:0x6289BD research in room] ThemeParkIncStaffMember.ResearchStep; (u8)trunc(energy) > RestLevel stays; else +0x27C != 0: the room's variable 0 -= 1, +0x27C = 0, 0x5D1000(3, +0x34, +0x30); then
	// 0x628180(0) and 0x62D810
	private void UpdateResearching( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		member.ResearchStep( settings );
		if ( ((int)(long)member.Energy & 0xFF) > settings.RestLevel )
			return;
		LeaveResearchRoom( member );
		SetState( economy, member, Idle, turn );
		LookForWork( economy, settings, member, turn );
	}

	/// <summary>The room left: its occupancy falls, the room word clears and the researcher is shown again (the finish <c>0x62DFC0</c>'s first part).</summary>
	private void LeaveResearchRoom( ThemeParkIncStaffMember member )
	{
		if ( member.ResearchRoom == 0 )
			return;
		Rooms?.AddOccupancy( (ushort)member.ResearchRoom, -1 );
		member.ResearchRoom = 0;
		ShowAgain( member );
	}

	/// <summary>
	/// A research room's thing removed (the researcher's message 10, <c>0x61B570</c>): a researcher whose room it is leaves it (in the room: the finish; on his way: the room word clears) and idles.
	/// </summary>
	// [BIN:TPI-EXE:0x61B570 researcher message] message 10 whose word +8 is +0x27C: state 24 -> 0x62DFC0 (the room left and 0x628180(0)); else +0x1B4 = 25 and +0x27C = 0; then 0x628180(0) and +0x1B4 = 0 in
	// both cases; every message then goes to the base handler 0x6265C0. [BIN:TPI-EXE:0x436E97 message order] ascending handles
	public void ResearchRoomRemoved( ParkEconomy economy, ushort room, long turn )
	{
		if ( economy.TpiStaff is not { } staff || room == 0 )
			return;
		foreach ( var member in staff.Members.Where( member => member.Placed && member.Role == ThemeParkIncStaffRole.Researcher && member.ResearchRoom == room ).OrderBy( member => member.ThingId ).ToList() )
		{
			if ( member.State == ThemeParkIncResearch.Researching )
			{
				LeaveResearchRoom( member );
				SetState( economy, member, Idle, turn );
			}
			else
			{
				member.State = ToResearchRoom;
				member.ResearchRoom = 0;
			}
			SetState( economy, member, Idle, turn );
		}
	}
}
