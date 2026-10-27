namespace OpenTPW;

/// <summary>
/// The entertainers' state machine (tp.exe <c>0x004D4810</c>, docs/reverse/WANDER-plan.md §4): once per park turn each entertainer idles, patrols,
/// or performs for WorkDuration turns when guests stand within his ActivationDistance; his <c>RegionFX</c> row 0 cheers the cells around him.
/// </summary>
public sealed partial class StaffWorldSimulation
{
	/// <summary>The end of a performance: cat_staff 135 at the entertainer.</summary>
	public const uint PerformanceEndEvent = 135;

	// [BIN:TPW-EXE:0x004D4810 entertainer update] 0 idle: a world draw, (draw & 15) == 0 plays cat_staff 165, then waits while the turn is at most IdleDuration[grade] + the idle stamp +0x200; 1 patrol: a world draw for cat_staff 164, a walk step (0x004FA2A0), the walk drain while walking (0x005066A0); 14 performing: the work drain (0x00506760), then once the turn passes the stamp +0x214 + EntertainerConstsPerGrade[grade].WorkDuration (0x00785398 + 16 × grade) cat_staff 135 at the actor (0x0051BFC0); every end of an idle, a walk or a performance runs the search 0x004D46D0; 2, 3 and the others are the generic staff handlers
	// Approximation (ECON-022, registered at UpdateStaff): an entertainer's performance has no animation (the original picks one of 13 + n − 1 by a draw, 0x00541FA0) (the guests do turn to watch him, as in the original: GUEST-010)
	private void UpdateEntertainer( ParkEconomy economy, StaffMember member, long turn, int maxSpeed, int maxForce )
	{
		var settings = economy.Settings;
		switch ( member.Job )
		{
			case StaffJob.Idle:
				Voice( economy, member, walking: false );
				if ( unchecked( (uint)turn ) <= unchecked( (uint)(settings.IdleDuration[member.Grade] + member.IdleStamp) ) )
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
			case StaffJob.Performing:
				StaffVitals.Work( member );
				if ( unchecked( (uint)turn ) <= unchecked( (uint)(settings[StaffType.Entertainer].WorkDuration[member.Grade] + member.JobStamp) ) )
					return;
				Sound( member, PerformanceEndEvent );
				break;
		}
		SearchEntertainer( economy, member, turn );
	}

	/// <summary>
	/// The entertainer's search (tp.exe <c>0x004D46D0</c>): the strike and rest gate; unless too tired, a world draw divisible by 3 and a guest within
	/// ActivationDistance start a performance; otherwise every fourth turn he idles, and on the others he patrols when a destination is found.
	/// </summary>
	// [BIN:TPW-EXE:0x004D46D0 entertainer search] 0x00506A40 (strike, rest), 0x00506680 (too tired: "Too tired to entertain"); a draw % 3 == 0 and 0x004C8EB0 (a thing of type 1 within EntertainerConstsPerGrade[grade].ActivationDistance, 0x007853A0 + 16 × grade) draw the performance's animation and set state 14 with +0x214 = the turn; else (turn & 3) == 0 draws once and idles (0x005054D0(0)), or 0x004F9490 finds a destination, draws once and patrols (state 1); a failed wander changes nothing
	private void SearchEntertainer( ParkEconomy economy, StaffMember member, long turn )
	{
		if ( StrikeOrRest( economy, member, turn ) )
			return;
		var cell = Cell( member );
		if ( !StaffVitals.IsBelowRestLevel( member.Energy, economy.Settings.RestLevel ) && Draw( economy ) % 3 == 0
			&& Guests?.AnyGuestWithin( cell, economy.Settings.EntertainerActivationDistance[member.Grade] ) == true )
		{
			Draw( economy ); // the performance's animation
			SetJob( economy, member, StaffJob.Performing, turn );
			return;
		}
		if ( (turn & 3) == 0 )
		{
			Draw( economy );
			SetJob( economy, member, StaffJob.Idle, turn );
		}
		else if ( Patrol( economy, member, cell ) )
		{
			Draw( economy );
			SetJob( economy, member, StaffJob.Patrol, turn );
		}
	}
}
