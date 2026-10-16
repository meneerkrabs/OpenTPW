namespace OpenTPW;

/// <summary>
/// The mechanics' state machine (tp.exe <c>0x004DA490</c>, docs/reverse/WANDER-plan.md §4): once per park turn each mechanic idles or patrols and,
/// from where he stands, takes the nearest ride that is broken down or being upgraded, walks to it and repairs or upgrades it, timed from his arrival.
/// </summary>
public sealed partial class StaffWorldSimulation
{
	// [BIN:TPW-EXE:0x004DA490 mechanic update] 0 idle: a world draw, (draw & 15) == 0 plays cat_staff 163, then once the turn passes IdleDuration[grade] + the idle stamp the search 0x004DA5B0; 1 patrol: a draw for cat_staff 162, a walk step, the walk drain while walking (0x005066A0), the search on arrival or held; 12 to the ride 0x004DA740; 13 repairing 0x004DA830; 2, 3 and the others are the generic staff handlers
	private void UpdateMechanic( ParkEconomy economy, StaffMember member, long turn, int maxSpeed, int maxForce )
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
			case StaffJob.ToRide:
				// [BIN:TPW-EXE:0x004DA740 to the ride] the ride's claim (0x004E0220) must be his ("UNUSUAL INCIDENT" else); the walk step: arrived at a ride still broken down, upgrading or requested starts the work (state 13), at one that no longer needs him searches again; held (2) clears his ride +0x218 and searches
				if ( !economy.TryGetObject( member.RideId, out var ride ) || economy.Claimant( ride ) != member.Id )
				{
					member.RideId = 0;
					break;
				}
				switch ( Walk( member, maxSpeed, maxForce ) )
				{
					case WalkResult.Walking:
						return;
					case WalkResult.Arrived when economy.NeedsMechanic( member.RideId ):
						member.JobStamp = economy.MechanicWorkTime( member, member.RideId );
						SetJob( economy, member, StaffJob.Repairing, turn );
						return;
					case WalkResult.Arrived:
						// "Arrived at ride but it doesn't need fixing": the search runs with the ride still his target, so his claim holds until the search
						// moves +0x218 on and then lapses 100 turns after it was taken (0x004E0220)
						break;
					default:
						// held (2): +0x218 = 0, so the claim lapses once 100 turns have passed
						member.RideId = 0;
						break;
				}
				break;
			case StaffJob.Repairing:
				// [BIN:TPW-EXE:0x004DA830 repairing] the work drain (0x00506760); the timer +0x214 counts down and, at 0, a broken ride plays its fix animation to the end (0x004E03F0, 0x004E0410), then 0x004DF8F0 repairs or upgrades it fully (gauge 100, the claim and the request cleared) and the search runs
				// Approximation (ECON-022, registered at UpdateStaff): the repair ends with the timer, without waiting for the ride's fix animation
				StaffVitals.Work( member );
				if ( member.JobStamp > 0 )
				{
					member.JobStamp--;
					return;
				}
				if ( economy.TryGetObject( member.RideId, out var repaired ) && repaired.MechanicId == member.Id )
					economy.FinishMechanicJob( member );
				member.RideId = 0;
				break;
		}
		SearchMechanic( economy, member, turn );
	}

	/// <summary>
	/// The mechanic's search (tp.exe <c>0x004DA5B0</c>): the strike and rest gate; unless too tired, the nearest ride that needs him and that a route
	/// reaches, which he claims and walks to; otherwise a patrol, else idle.
	/// </summary>
	// [BIN:TPW-EXE:0x004DA5B0 mechanic search] 0x00506A40; too tired (0x00506680): "Too tired to fix rides"; the ride finder 0x004DAA90 (the object list from a cursor that moves one object on per search; rides broken down (state 1), upgrading (state 2) or with mRequestedService +0x64 that are no toilet; the claim +0x5E free or his; the smallest dx² + dy² from his cell, strictly smaller replaces) stores the ride at +0x218; a route to its access cell +0x36 claims it (0x004E01F0: +0x5E = him, +0x60 = the turn), posts the advisor's "on the way" message unless requested or upgrading, and sets state 12 (no speed bonus, 0x004DA370); without one 0x004F9490 patrols or he idles
	// Approximation (ECON-022, registered at UpdateStaff): the advisor's "mechanic on the way" message 0x46 (0x0059B590 at 0x004DA672) is not posted
	private void SearchMechanic( ParkEconomy economy, StaffMember member, long turn )
	{
		if ( StrikeOrRest( economy, member, turn ) )
			return;
		var cell = Cell( member );
		if ( !StaffVitals.IsBelowRestLevel( member.Energy, economy.Settings.RestLevel ) )
		{
			// the finder's result is the mechanic's target +0x218 whether or not a route reaches it; only a route claims it
			var ride = economy.FindRideForMechanic( member, cell, RideCell );
			member.RideId = ride?.Id ?? 0;
			if ( ride != null && RideAccess( ride.Id ) is { } access && Route( member, access, GuestWander.CentreByte, GuestWander.CentreByte ) )
			{
				economy.Claim( ride, member );
				SetJob( economy, member, StaffJob.ToRide, turn );
				return;
			}
		}
		SetJob( economy, member, Patrol( economy, member, cell ) ? StaffJob.Patrol : StaffJob.Idle, turn );
	}

	/// <summary>A ride's own cell (its thing's position), null when the level does not know it.</summary>
	private (int X, int Y)? RideCell( int instanceId ) => RideCells?.Invoke( instanceId ) ?? jobAccess( instanceId );

	/// <summary>The path cell a mechanic walks to (the ride's access cell, thing <c>+0x36</c>): the economy's job access the level supplies.</summary>
	private (int X, int Y)? RideAccess( int instanceId ) => jobAccess( instanceId ) is { } access && grid.IsWalkable( access.X, access.Y ) ? access : null;

	/// <summary>A ride's cell by its economy id, for the mechanics' nearest-ride test; null: the job access cell stands in.</summary>
	public Func<int, (int X, int Y)?>? RideCells { get; set; }
}
