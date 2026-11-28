namespace OpenTPW;

/// <summary>
/// Theme Park Inc's mechanic in the park (docs/reverse/TPI-STAFF.md §11, "Mechanics and ride wear"): the state machine <c>0x5DA280</c> (0 idle, 1 patrol, 21 to the ride, 22 repairing), the
/// search <c>0x5DA3D0</c> with the ride finder <c>0x5DAAD0</c> and the claim test <c>0x5E37F0</c>, and the repair it ends in (ParkEconomy.FinishThemeParkIncRepair).
/// </summary>
/// <remarks>
/// The upgrade job is not reached: a ride is upgraded only by a mechanic whose learnt ability (+0x218) is 1 (Ride Upgrading, <c>0x62B7D0</c> in the finder), which only training writes
/// (<c>0x62AA50</c>, <c>0x62B860</c>, <c>0x62B8B0</c>; the member start <c>0x626390</c> clears it); a mechanic trains through the common handler like the others, but the economy accepts no upgrade
/// purchase in a Theme Park Inc park, so no ride is ever in state 2 and a trained mechanic finds no upgrade to take. The states 2 and 3 (to a rest area and resting) and 0x23 run as the handyman's do (ThemeParkIncStaffWorld.Rest.cs): the staff rooms are rest areas.
/// </remarks>
public sealed partial class ThemeParkIncStaffWorld
{
	/// <summary>The mechanic's states this world runs besides idle and walking: 21 (0x15) to the ride, 22 (0x16) repairing.</summary>
	public const int ToRide = 0x15, Repairing = 0x16;

	/// <summary>The sprite template the mechanic's repair queues (0x5DA175).</summary>
	public const int RepairTemplate = 0x1A;

	/// <summary>The mechanic's idle (0xA3) and walking (0xA2) voice events (cat_staff, the same as Theme Park World's mechanic's 163 and 162).</summary>
	public const int MechanicIdleVoice = 0xA3, MechanicWalkingVoice = 0xA2;

	/// <summary>
	/// The cells the mechanic's search needs of an economy object: its thing's own cell (the finder's distance, the thing's bytes +5 and +7) and its entrance cell (the thing's word +0x46, which
	/// the search walks to); null for an object the level does not place.
	/// </summary>
	public Func<int, ((int X, int Y) Thing, (int X, int Y)? Entrance)?>? ObjectCells { get; set; }

	/// <summary>The mechanic's state handler.</summary>
	// [BIN:TPI-EXE:0x5DA280 mechanic dispatch] states 0..0x16 by the byte table 0x5DA358 and the jump table 0x5DA33C: 0 idle 0x5DA309, 1 patrol 0x5DA2A1, 2 0x629EF0 and 3 0x62A1C0 (the rest
	// area's, ThemeParkIncStaffWorld.Rest.cs: a tired mechanic walks to a staff room as the others do), 21 0x5DA650, 22 0x5DA7C0, the others and anything above 0x16 the common 0x6287A0 (entry 6, 0x5DA333)
	// [BIN:TPI-EXE:0x6287A0 common states] state - 4 by the byte table 0x628B10 (up to 0x24) and the jump table 0x628ADC: 4, 5, 6, 7, 8, 9, 11, 12, 14, 24, 25, 35 and 36 have a case; of them the
	// training's 7 (0x62A7C0) and 8 (0x62AA50) are what a mechanic reaches (the panel's ability choice, 0x580550); the suspended 0x25 and 0x26 lie above the table and have none
	private void DispatchMechanic( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		switch ( member.State )
		{
			case Idle:
				// [BIN:TPI-EXE:0x5DA309 state 0] a world draw; its low nibble 0 plays the idle voice 0xA3 (0x60BE90); then the idle end 0x62CA10
				Voice( economy, member, MechanicIdleVoice );
				if ( unchecked( (uint)turn ) > unchecked( (uint)(member.IdleStamp - IdleTerm( member.Experience, settings.MaxExperience )) ) )
					LookForWork( economy, settings, member, turn );
				break;
			case Walking:
				// [BIN:TPI-EXE:0x5DA2A1 state 1] a world draw for the walking voice 0xA2; the walk step: arrived (0) or stuck (2) searches, still walking takes the moving step 0x62B3E0
				Voice( economy, member, MechanicWalkingVoice );
				if ( Walk( member ) is WalkResult.Arrived or WalkResult.Stuck )
					Search( economy, member, turn );
				else
					member.Step( settings );
				break;
			case ToRide:
				UpdateToRide( economy, member, turn );
				break;
			case Repairing:
				UpdateRepairing( economy, settings, member, turn );
				break;
			case ThemeParkIncTraining.ToTrainingRoom:
				UpdateToTrainingRoom( economy, settings, member, turn );
				break;
			case ThemeParkIncTraining.Training:
				UpdateTraining( economy, settings, member, turn );
				break;
			// [BIN:TPI-EXE:0x5DA280 mechanic rest states] the byte table 0x5DA358 sends 2 to 0x629EF0 and 3 to 0x62A1C0 (the handyman's entries for the same states), and 0x23 (above 0x16) to the
			// common handler 0x6287A0, whose case is 0x62CA90 as for the handyman
			case ToRestArea:
				UpdateToRestArea( economy, settings, member, turn );
				break;
			case Resting:
				UpdateResting( economy, settings, member, turn );
				break;
			case RestSearch:
				UpdateRestSearch( economy, settings, member, turn );
				break;
			// [BIN:TPI-EXE:0x6287A0 strike states] the role's table sends 4 and 5 to the common handler: 4 -> 0x628855, 5 -> 0x628892 (ThemeParkIncStaffWorld.Strikes.cs)
			case ToStrike:
				UpdateToStrike( economy, member, turn );
				break;
			case Striking:
				UpdateStriking( economy, member, turn );
				break;
		}
	}

	/// <summary>
	/// State 21: the ride's claim must still be his; arrived at a ride that is broken down, being upgraded or has asked for maintenance, he starts the repair; arrived at one that needs nothing,
	/// he searches with the ride kept; stuck, he gives it up and searches.
	/// </summary>
	// [BIN:TPI-EXE:0x5DA650 to the ride] the claimant 0x5E37F0 of table[+0x5D8] against his thing id (0x6375C0): another one logs "UNUSUAL INCIDENT!! Mechanic: didn't get to breakdown in time,
	// someone else assigned", +0x5D8 = 0 and the search (0x5DA740); the walk step 0x60B440: 1 returns, 2 clears +0x5D8 and searches, 0 with the ride in state 1 (0x5E3950) or 2 (0x5E3B90) or its
	// request +0x78 set (0x5E3B70) sets 22 (0x5DA6DF), else "Arrived at ride but it's not broken down" and the search with +0x5D8 kept (0x5DA749); the thought request 0x1D (0x638960) and, after
	// the setter (presentation) and [BIN:TPI-EXE:0x5DA6E8 ride notices cleared] after the setter the tag system's rows of tags 6, 7, 8 and 9 keyed by the ride (+0x5D8) are flagged (0x64EA60 ->
	// 0x636D10(4, {6, 7, 8, 9}, ride)): the captions of the advice 0x40, 0x41 and 0x42 about it
	private void UpdateToRide( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		var claimant = economy.TryGetObject( member.RideId, out var ride ) ? Claimant( economy, ride, turn ) : 0;
		if ( claimant != member.ThingId )
		{
			member.RideId = 0;
			Search( economy, member, turn );
			return;
		}
		switch ( Walk( member ) )
		{
			case WalkResult.Walking:
				return;
			case WalkResult.Stuck:
				member.RideId = 0;
				Search( economy, member, turn );
				return;
		}
		if ( ParkEconomy.ObjectState( ride ) is RideObjectStates.BrokenDown or RideObjectStates.Upgrading || ride.MaintenanceRequested )
		{
			SetState( economy, member, Repairing, turn );
			economy.DeleteThemeParkIncRideNotices( ride );
			return;
		}
		Search( economy, member, turn );
	}

	/// <summary>
	/// State 22: the working step, then the job timer counts down; at 0 a broken ride's VAR_BREAKSTAT clears and the repair waits while its script still reports VAR_BROKEN (the fix animation);
	/// then the ride is repaired fully and he searches again.
	/// </summary>
	// [BIN:TPI-EXE:0x5DA7C0 repairing] the working step 0x62B4D0; +0x5D4 nonzero counts down by one and returns; at 0 the ride in state 1 (0x5E3950) gets VAR_BREAKSTAT = 0 (0x5E3BB0) and
	// returns while VAR_BROKEN (variable 7, 0x5E3BE0) is nonzero, else "Ride has finished fix anim"; then 0x5E2910(ride, his thing id), +0x5D8 = 0 and the search (0x5DA825..0x5DA835)
	private void UpdateRepairing( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		member.Step( settings );
		if ( member.JobTimer != 0 )
		{
			member.JobTimer--;
			return;
		}
		// the ride's removal message (0x5D9DD0) ends this state, so the ride is there (ObjectRemoved)
		if ( !economy.TryGetObject( member.RideId, out var ride ) )
			throw new InvalidOperationException( $"Theme Park Inc mechanic {member.Id} repairs object {member.RideId}, which is not in the park." );
		if ( ParkEconomy.ObjectState( ride ) == RideObjectStates.BrokenDown && !economy.ThemeParkIncFixAnimationEnded( ride ) )
			return;
		economy.FinishThemeParkIncRepair( ride );
		member.RideId = 0;
		Search( economy, member, turn );
	}

	/// <summary>
	/// The mechanic's search (0x5DA3D0): the strike and rest gate; unless too tired, the ride the finder takes, inside his patrol area and reached by a route to its entrance: he claims it, a
	/// ride below its <c>RideBreakDown.Imminent</c> line that is neither broken down nor called for asks for maintenance, and he sets off (21); else a patrol, else idle.
	/// </summary>
	// [BIN:TPI-EXE:0x5DA3D0 mechanic search] 0x62BBA0 nonzero returns; tired (0x62B3B0, "Too tired to fix rides") goes to the tail; +0x5D8 = the finder 0x5DAAD0 (on the HQ thing of the world word
	// +0x1EAB4E, 0x64E9A0) whatever follows; 0 goes to the tail; the patrol-area test 0x62C6B0 false goes to the tail silently; "Mechanic: found a ride that needs repairing"; the route 0x62EAC0 to
	// the ride's entrance word +0x46 (0x62EAC0 turns the word into the cell bytes with the point 0x80, 0x80 and calls the staff route 0x62EB10: StaffRoute) failing logs "couldn't reach broken ride" and goes to the tail; else the claim 0x5E3780; a ride neither requested
	// (0x5E3B70) nor upgrading (0x5E3B90) posts advice 0x43 (ParkEconomy.ThemeParkIncMechanicClaimAdvice; it draws nothing); a ride not in state 1 (0x5E3950), not
	// requested and below its line (0x5E28A0) gets the request 0x5E3400(1); then the setter 0x5DA150(0x15); the tail: the patrol destination 0x609F70 sets 1, none ("Couldn't even move to random
	// dest") sets 0
	private void SearchMechanic( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		var settings = economy.TpiStaff!.Settings;
		if ( Gate( economy, settings, member, turn ) )
			return;
		if ( !member.Tired( settings ) )
		{
			var found = FindRide( economy, member, turn );
			member.RideId = found;
			if ( found != 0 && economy.TryGetObject( found, out var ride ) && InMechanicArea( member )
				&& ObjectCells?.Invoke( found ) is { Entrance: { } entrance } && StaffRoute( economy, member, entrance, GuestWander.CentreByte, GuestWander.CentreByte ) )
			{
				economy.ClaimThemeParkInc( ride, member.ThingId, turn );
				if ( !ride.MaintenanceRequested && ParkEconomy.ObjectState( ride ) != RideObjectStates.Upgrading )
					economy.ThemeParkIncMechanicClaimAdvice( ride );
				if ( ParkEconomy.ObjectState( ride ) != RideObjectStates.BrokenDown && !ride.MaintenanceRequested && economy.BelowThemeParkIncRepairLine( ride ) )
					economy.RequestThemeParkIncMaintenance( ride );
				SetState( economy, member, ToRide, turn );
				return;
			}
		}
		SetState( economy, member, Patrol( economy, member ) ? Walking : Idle, turn );
	}

	/// <summary>The mechanic's patrol-area test (0x62C6B0): true for the whole map, the only area a placed member has (no patrol area is set in the park; a saved mechanic is refused with another).</summary>
	// [BIN:TPI-EXE:0x62C6B0 mechanic area] the corner words of +0x24C..+0x258 equal to 1 and 0x4000 (the cells (0, 0) and (127, 127)) answer true; any other rectangle runs 0x6256A0, which needs the
	// patrol-area tool (not ported; ThemeParkIncStaffMember.IsValidWorld refuses another rectangle for a mechanic)
	private static bool InMechanicArea( ThemeParkIncStaffMember member ) =>
		member.PatrolArea == (0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1)
			? true
			: throw new InvalidOperationException( "A Theme Park Inc mechanic's patrol area (0x6256A0) is not ported." );

	/// <summary>
	/// The ride finder (0x5DAAD0): the HQ's cursor moves one object on from where the previous search started and walks the object list (newest first) round once; an object broken down, called
	/// for (not a toilet, bin or plant), below its repair line or (for a mechanic trained to upgrade) upgrading, and claimed by nobody, is a candidate; the nearest by squared cell distance wins,
	/// a broken or called-for one before any other, the first met on a tie. 0 for none.
	/// </summary>
	// [BIN:TPI-EXE:0x5DAAD0 ride finder] the HQ word +0x14: 0 takes the list head (world +0x1EAB88, next +0x14); found in the list it takes that object's next (0 past the end), else the head;
	// start = it, 0 answers 0. Each object: the word moves to its next (the head past the end); a = his learnt ability +0x218 == 1 (0x62B7D0); the object passes on state 1 (0x5E3950), the
	// repair line 0x5E28A0, the request +0x78 with +0x42 & 0x41 and & 0x1000 clear (relief, bin, plant), or state 2 with a and the claimant 0 or his; and then the claimant 0x5E37F0 0 (each call may
	// lapse a claim); d = dx² + dy² of the cell bytes (+5, +7, as |Δ| of the cell words' columns and rows), strictly smaller replaces, in the urgent pair when the object is in state 1 or requested,
	// else in the other (unsigned, 0xFFFFFFFF to start); until the word is back at the start; the urgent one, else the other
	// [BIN:TPI-EXE:0x64F3E0 object list] the list is ParkEconomy.ThemeParkIncObjectList: newest first, with the attraction things that have no economy record kept as negated thing ids, which the
	// walk steps over as candidates of no kind (in the prebuilt Science park the obstacles, lake, gate and camera path and the fixed item 1710; an obstacle's replacement later; docs/reverse/TPI-STAFF.md)
	internal int FindRide( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		var list = economy.ThemeParkIncObjectList;
		var positions = new Dictionary<int, int>( list.Count );
		for ( var index = 0; index < list.Count; index++ )
			positions[list[index]] = index;
		var head = list.Count > 0 ? list[0] : 0;
		int Next( int id ) => positions.TryGetValue( id, out var index ) && index + 1 < list.Count ? list[index + 1] : 0;
		var cursor = economy.MechanicCursor;
		cursor = cursor == 0 ? head : positions.ContainsKey( cursor ) ? Next( cursor ) : head;
		economy.MechanicCursor = cursor;
		if ( cursor == 0 )
			return 0;
		var start = cursor;
		// [BIN:TPI-EXE:0x62B7D0 learnt ability] a = +0x218 == 1 (Ride Upgrading, learnt in the training room); no Theme Park Inc ride reaches state 2 (the economy sells no upgrade), so the upgrading
		// branch finds nothing even for a trained mechanic
		var canUpgrade = member.Ability == 1;
		var (x, y) = member.Navigation!.Cell;
		uint urgentBest = uint.MaxValue, otherBest = uint.MaxValue;
		int urgent = 0, other = 0;
		do
		{
			var current = cursor;
			cursor = Next( cursor );
			if ( cursor == 0 )
				cursor = head;
			economy.MechanicCursor = cursor;
			if ( !economy.TryGetObject( current, out var item ) )
				continue;
			var state = ParkEconomy.ObjectState( item );
			var info = economy.Catalog.TryGet( item.InfoId, out var known ) ? known : null;
			var candidate = state == RideObjectStates.BrokenDown || economy.BelowThemeParkIncRepairLine( item )
				|| item.MaintenanceRequested && info is not { ProvidesRelief: true } and not { HoldsLitter: true } and not { RequiresWatering: true }
				|| state == RideObjectStates.Upgrading && canUpgrade && (Claimant( economy, item, turn ) == 0 || Claimant( economy, item, turn ) == member.ThingId);
			if ( !candidate || Claimant( economy, item, turn ) != 0 || ObjectCells?.Invoke( current ) is not { } cells )
				continue;
			var dx = Math.Abs( cells.Thing.X - x );
			var dy = Math.Abs( cells.Thing.Y - y );
			var distance = unchecked( (uint)(dy * dy + dx * dx) );
			if ( state == RideObjectStates.BrokenDown || item.MaintenanceRequested )
			{
				if ( distance < urgentBest )
					(urgentBest, urgent) = (distance, current);
			}
			else if ( distance < otherBest )
				(otherBest, other) = (distance, current);
		}
		while ( cursor != start );
		return urgent != 0 ? urgent : other;
	}

	/// <summary>
	/// The object's claimant (0x5E37F0), 0 for none: a claim taken more than 100 turns ago lapses unless its claimant still targets the object, and the claimant then goes idle.
	/// </summary>
	// [BIN:TPI-EXE:0x5E37F0 claimant] +0x72 zero: none; the turn above +0x74 + 100 (unsigned) and the claimant's 0x62B280(the object) false (kind 4 and kind 0x14: +0x5D8 is the object; kind 5: its loo
	// or bin target +0x5DA, which the ported handyman never sets; other kinds false): "Ride: removing staff member from this ride, he's taken too long to fix it", the claimant's 0x628180(0) and +0x72 = 0,
	// +0x74 = 0
	internal int Claimant( ParkEconomy economy, ParkObjectState item, long turn )
	{
		if ( item.MechanicId == 0 )
			return 0;
		if ( unchecked( (uint)turn ) > unchecked( (uint)(item.ClaimTurn + 100) ) )
		{
			var claimant = economy.TpiStaff?.Members.FirstOrDefault( member => member.Navigation != null && member.ThingId == item.MechanicId );
			// [BIN:TPI-EXE:0x62B280 claim validator] kind 4 and 0x14 compare the word +0x5D8 with the object (0x62B2B3 jumps to the mechanic's compare for 0x14)
			var targets = claimant switch
			{
				{ Role: ThemeParkIncStaffRole.Mechanic } => claimant.RideId == item.Id,
				{ Role: ThemeParkIncStaffRole.Gardener } => claimant.PlantId == item.Id,
				_ => false
			};
			if ( !targets )
			{
				if ( claimant != null )
					SetState( economy, claimant, Idle, turn );
				item.MechanicId = 0;
				item.ClaimTurn = 0;
				return 0;
			}
		}
		return item.MechanicId;
	}

	/// <summary>The mechanic's own states in the setter (0x5DA150): 21 queues the patrol walk, 22 the repair with its job timer.</summary>
	// [BIN:TPI-EXE:0x5DA150 mechanic state setter] 0x15: template 0xF (0x5D0F90), state 0x15; 0x16: template 0x1A, D = the work duration 0x62E4C0, the ride table[+0x5D8]: in state 2 (0x5E3B90)
	// +0x5D4 = D × the type's word +0x2F8 + 72 × the level byte (Upgrades[level + 1].DurationOfUpgrade), else (100 - (u8)trunc(+0x54)) × D (32-bit) / 100 (signed, toward zero), state 0x16; neither
	// touches the speed bonus; the others go to the common 0x628180
	private void SetMechanicState( ParkEconomy economy, ThemeParkIncStaffMember member, int state )
	{
		if ( state == ToRide )
		{
			RequestTemplate( member, WalkTemplate );
			member.State = ToRide;
			return;
		}
		RequestTemplate( member, RepairTemplate );
		var settings = economy.TpiStaff!.Settings;
		var duration = settings.WorkDuration( ThemeParkIncStaffRole.Mechanic, member.Experience, member.Motivation, member.Salary );
		if ( !economy.TryGetObject( member.RideId, out var ride ) )
			throw new InvalidOperationException( $"Theme Park Inc mechanic {member.Id} starts work on object {member.RideId}, which is not in the park." );
		if ( ParkEconomy.ObjectState( ride ) == RideObjectStates.Upgrading )
			throw new InvalidOperationException( "A Theme Park Inc upgrade needs a mechanic trained in Ride Upgrading, which is not ported." );
		member.JobTimer = (uint)RepairTimer( duration, (float)ride.Repair );
		member.State = Repairing;
	}

	/// <summary>The mechanic's repair timer: (100 − the state of repair byte) × the work duration, a 32-bit product divided by 100 toward zero.</summary>
	// [BIN:TPI-EXE:0x5DA1DF repair timer] (u8)ftol(+0x54); ecx = 100 - byte; imul ebx (D); the magic 0x51EB851F, sar 5 and the sign bit: a signed division by 100
	public static int RepairTimer( int workDuration, float stateOfRepair ) => unchecked( (100 - RideBreakdownRules.GaugeByte( stateOfRepair )) * workDuration ) / 100;

	/// <summary>A cancelled maintenance request ends its claim (<c>0x5E3400(0)</c>): the claimant goes idle by the common setter <c>0x628180(0)</c>, keeping his target.</summary>
	internal static void ClaimantReleased( ThemeParkIncStaff staff, int thingId, long turn )
	{
		if ( staff.Members.FirstOrDefault( member => member.Navigation != null && member.ThingId == thingId ) is { } claimant )
			EnterIdle( claimant, turn );
	}

	/// <summary>
	/// The removal message (10) a mechanic (0x5D9DD0) or a gardener (0x5D2480) hears: when the removed object is his ride or his plant, he forgets it and goes idle. Called for every placed member
	/// when the economy removes an object.
	/// </summary>
	// [BIN:TPI-EXE:0x5D2480 gardener message] message 10 whose object word (+8) is his +0x5D8: "Gardener: plant I was watering was deleted", +0x5D8 = 0, template 3 in the state 0x20 (0x5D0F90), then
	// 0x628180(0); the base handler 0x6265C0 follows for every message (the gardener's constructor subscribed him, 0x436AC0(this, 10))
	// [BIN:TPI-EXE:0x5D9DD0 mechanic message] message 10 whose object word (+8) is his +0x5D8: "Mechanic: ride I was fixing was deleted", +0x5D8 = 0 and the setter 0x5DA150(0) (0x628180(0): the
	// idle stamp the turn after state 1, else 0, the speed bonus 0 and template 3); the constructor subscribed him to the message (0x436AC0(this, 10))
	internal static void ObjectRemoved( ThemeParkIncStaff staff, int instanceId, long turn )
	{
		foreach ( var member in staff.Members )
		{
			if ( member.Navigation == null )
				continue;
			if ( member.Role == ThemeParkIncStaffRole.Mechanic && member.RideId == instanceId )
			{
				member.RideId = 0;
				EnterIdle( member, turn );
			}
			else if ( member.Role == ThemeParkIncStaffRole.Gardener && member.PlantId == instanceId )
			{
				member.PlantId = 0;
				EnterIdle( member, turn );
			}
		}
	}
}
