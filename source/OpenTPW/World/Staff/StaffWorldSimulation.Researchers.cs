namespace OpenTPW;

/// <summary>
/// The researchers' state machine (tp.exe <c>0x005029F0</c>, docs/reverse/WANDER-plan.md §4): once per park turn each researcher idles, wanders,
/// or researches where he stands for WorkDuration turns before he wanders on. His research points come every 20th turn whatever he does, except
/// while he rests, strikes or is picked up (the economy, tp.exe <c>0x00502960</c>).
/// </summary>
public sealed partial class StaffWorldSimulation
{
	/// <summary>A researcher at work: cat_staff 138 at him, at one turn in sixteen.</summary>
	public const uint ResearchingEvent = 138;

	// [BIN:TPW-EXE:0x005029F0 researcher update] 0 idle: a world draw, (draw & 15) == 0 plays cat_staff 169; once the turn passes IdleDuration[grade] + the idle stamp, the gate 0x00506A40 and the search; 1 patrol: a draw for cat_staff 168, a walk step, the walk drain while walking (0x005066A0), then the gate and the search; 15 researching: the work drain (0x00506760); once the turn passes the stamp +0x214 + ResearcherConstsPerGrade[grade].WorkDuration (0x00785458 + 12 × grade) a wander (0x004F9490) patrols (state 1), or none restamps; before that a draw plays cat_staff 138 at (draw & 15) == 0
	private void UpdateResearcher( ParkEconomy economy, StaffMember member, long turn, int maxSpeed, int maxForce )
	{
		switch ( member.Job )
		{
			case StaffJob.Idle:
				Voice( economy, member, walking: false );
				if ( unchecked( (uint)turn ) <= unchecked( (uint)(economy.Settings.IdleDuration[member.Grade] + member.IdleStamp) ) )
					return;
				break;
			case StaffJob.Patrol:
				Voice( economy, member, walking: true );
				if ( Walk( member, maxSpeed, maxForce ) == WalkResult.Walking )
				{
					StaffVitals.Walk( member );
					return;
				}
				break;
			case StaffJob.Researching:
				StaffVitals.Work( member );
				if ( unchecked( (uint)turn ) > unchecked( (uint)(economy.Settings[StaffType.Researcher].WorkDuration[member.Grade] + member.JobStamp) ) )
				{
					if ( Patrol( economy, member, Cell( member ) ) )
						SetJob( economy, member, StaffJob.Patrol, turn );
					else
						member.JobStamp = turn;
					return;
				}
				if ( (Draw( economy ) & 0xF) == 0 )
					Sound( member, ResearchingEvent );
				return;
		}
		SearchResearcher( economy, member, turn );
	}

	/// <summary>
	/// The researcher's search (tp.exe <c>0x00502C70</c>, also the tail of states 0 and 1): the strike and rest gate; on a world draw whose low two bits
	/// are not both 0 a wander; otherwise, unless too tired, he researches where he stands.
	/// </summary>
	// [BIN:TPW-EXE:0x00502C70 researcher search] 0x00506A40; a draw with (draw & 3) != 0 and 0x004F9490: state 1 (0x005054D0); else too tired (0x00506680): "Too tired to research" and no change; else +0x214 = the turn, animation 10 and state 15 (the setter 0x00502C20)
	private void SearchResearcher( ParkEconomy economy, StaffMember member, long turn )
	{
		if ( StrikeOrRest( economy, member, turn ) )
			return;
		if ( (Draw( economy ) & 3) != 0 && Patrol( economy, member, Cell( member ) ) )
		{
			SetJob( economy, member, StaffJob.Patrol, turn );
			return;
		}
		if ( StaffVitals.IsBelowRestLevel( member.Energy, economy.Settings.RestLevel ) )
			return;
		SetJob( economy, member, StaffJob.Researching, turn );
	}
}
