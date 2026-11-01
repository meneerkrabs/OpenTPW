namespace OpenTPW;

/// <summary>
/// The guards' state machine (tp.exe <c>0x004D6410</c>, docs/reverse/WANDER-plan.md §4): once per park turn each guard idles or patrols, spreading
/// his <c>RegionFX</c> row 3 (security) around him. A prank on a cell with security sends the free guard nearest the staff HQ after the prankster;
/// he catches it on its cell, carries it to the ticket booth and sets it down to leave the park.
/// </summary>
public sealed partial class StaffWorldSimulation
{
	/// <summary>A guard sets off after a prankster: cat_staff 136 at him.</summary>
	public const uint ChaseEvent = 136;
	/// <summary>A guard catches the prankster: cat_staff 137 at him.</summary>
	public const uint CatchEvent = 137;
	/// <summary>A guard sets the caught guest down: cat_kids 128 at the guest.</summary>
	public const uint SetDownEvent = 128;

	/// <summary>Whether a guard is carrying the guest with this id to the gate (state 18 with it as his target, tp.exe <c>0x004D6350</c>).</summary>
	public bool IsEscorting( int guestId ) =>
		members().Any( member => member.Type == StaffType.Guard && member.Job == StaffJob.Escorting && member.TargetGuestId == guestId );

	// [BIN:TPW-EXE:0x004D6410 guard update] 0 idle: a world draw, (draw & 15) == 0 plays cat_staff 167, waits while the turn is at most IdleDuration[grade] + the idle stamp; 1 patrol: a world draw for cat_staff 166, a walk step, the walk drain while walking; both then run the strike and rest gate 0x00506A40 and, on a turn whose low two bits are not both 0, the wander 0x004F9490 (state 1), else state 0; 16 the chase 0x004D6790; 18 the escort 0x004D6A50; 19 walks, then routes to the park entrance (0x004D8690, the turn's bit 0) and patrols (state 1) or idles; 2, 3 and the others are the generic staff handlers
	private void UpdateGuard( ParkEconomy economy, StaffMember member, long turn, int maxSpeed, int maxForce )
	{
		switch ( member.Job )
		{
			case StaffJob.Idle:
				Voice( economy, member, walking: false );
				if ( unchecked( (uint)turn ) <= unchecked( (uint)(economy.Settings.IdleDuration[member.Grade] + member.IdleStamp) ) )
					return;
				SearchGuard( economy, member, turn );
				return;
			case StaffJob.Patrol:
				Voice( economy, member, walking: true );
				if ( Walk( member, maxSpeed, maxForce ) == WalkResult.Walking )
				{
					StaffVitals.Walk( member );
					return;
				}
				SearchGuard( economy, member, turn );
				return;
			case StaffJob.Chasing:
				Chase( economy, member, turn, maxSpeed, maxForce );
				return;
			case StaffJob.Escorting:
				if ( Walk( member, maxSpeed, maxForce ) == WalkResult.Walking )
					return;
				// [BIN:TPW-EXE:0x004D6A50 escort] on arrival (or a broken walk) the guard queues template 0x18 (the release), the guest is set down on the guard's cell (0x004FEB50), +0x188 = 0, and the guard routes to the park entrance (0x004D8690, the turn's bit 0): state 19, else 0
				StaffSpriteEvents.GuardReleased( member );
				SetDown( economy, member );
				member.Navigation!.Mask = 0;
				SetJob( economy, member, RouteToGate( member, GateCell.Entrance, turn ) ? StaffJob.ReturningFromEscort : StaffJob.Idle, turn );
				return;
			case StaffJob.ReturningFromEscort:
				if ( Walk( member, maxSpeed, maxForce ) == WalkResult.Walking )
					return;
				SetJob( economy, member, RouteToGate( member, GateCell.Entrance, turn ) ? StaffJob.Patrol : StaffJob.Idle, turn );
				return;
		}
		SearchGuard( economy, member, turn );
	}

	/// <summary>The guard's search (tp.exe <c>0x004D63D0</c>, also the tail of states 0 and 1): the strike and rest gate, then a patrol on three turns in four, else idle.</summary>
	private void SearchGuard( ParkEconomy economy, StaffMember member, long turn )
	{
		if ( StrikeOrRest( economy, member, turn ) )
			return;
		SetJob( economy, member, (turn & 3) != 0 && Patrol( economy, member, Cell( member ) ) ? StaffJob.Patrol : StaffJob.Idle, turn );
	}

	/// <summary>
	/// A prank (the bus message the prankster sends, handled by the staff HQ, tp.exe <c>0x00508A30</c>): on a cell whose security word is at least 1,
	/// the free guard (idle or patrolling) nearest the staff HQ sets off after the prankster.
	/// </summary>
	// [BIN:TPW-EXE:0x00508A30 prank message] type 14: the prankster's cell's environment record (0x004D8410) word +6 (security) above 0, else nothing ("no security level"); the guard 0x005099C0: the guard list (game +0x1DA744, next at +0x210), states 0 and 1 (0x004D6330), the smallest dx² + dy² between the guard's cell and the receiver's (the staff HQ thing, always at cell (0, 0) in the shipped and played saves), strictly smaller replaces; none: "No guards available"; the chase starts in 0x004D6260
	// [BIN:TPW-EXE:0x0040FB10 message bus] a message posted is delivered at once, in the poster's call, to every thing subscribed to its type (0x0050B550 by thing type), so the prank reaches the staff HQ (and the advisor) as it is made
	public void ReportPrank( ParkEconomy economy, int guestId )
	{
		// a member hired since the last staff turn already stands with his row down (the constructors put it on their cell)
		Synchronize();
		foreach ( var member in economy.Staff.Members.Where( member => member.Navigation == null && ParkEconomy.EffectRow( member.Type ) != null && economy.DrivenByWorld( member ) ) )
			if ( Place( member ) )
				economy.MoveStaffEffect( member, Cell( member ) );
		if ( Guests?.CellOf( guestId ) is not { } cell || economy.Litter.Environment.At( cell.X, cell.Y ).Security < 1 )
			return;
		// a guard still walking back off the paths from a strike cannot route to the prankster on OpenTPW's grid, so he is not asked (ECON-022)
		// [BIN:TPW-EXE:0x004D5DE0 guard list] a hired guard is pushed at the head of the guard list (game +0x1DA744, next +0x210; 0x00519AA0), so the list runs newest first and the newest wins a tie of the choice
		var guards = economy.Staff.OfType( StaffType.Guard ).Where( member => member.Navigation is { OffGrid: false }
			&& member.State is not (StaffState.PickedUp or StaffState.OnStrike or StaffState.Resting) ).Reverse().ToList();
		if ( ChooseGuard( guards.Select( member => (member.Job, Cell( member )) ), StaffHqCell ) is not { } index )
			return;
		var guard = guards[index];
		// [BIN:TPW-EXE:0x004D6260 chase start] too tired (0x00506680): nothing; else cat_staff 136 at the guard, the target +0x212 = the guest, a route to its cell: state 16, else nothing (the target stays)
		if ( StaffVitals.IsBelowRestLevel( guard.Energy, economy.Settings.RestLevel ) )
			return;
		Sound( guard, ChaseEvent );
		guard.TargetGuestId = guestId;
		if ( Route( guard, cell, GuestWander.CentreByte, GuestWander.CentreByte ) )
			SetJob( economy, guard, StaffJob.Chasing, economy.Turn );
	}

	/// <summary>The staff HQ's cell: the thing sits at (0, 0) in every shipped and played save, and the guard choice measures from it.</summary>
	public static readonly (int X, int Y) StaffHqCell = (0, 0);

	/// <summary>
	/// The guard choice (tp.exe <c>0x005099C0</c>): among the guards in list order, those idle or patrolling, the one with the smallest squared distance
	/// from <paramref name="origin"/> (the staff HQ, not the prankster), the first on a tie; its index, or null when none is free.
	/// </summary>
	// [BIN:TPW-EXE:0x005099C0 guard choice] executed by the x86 oracle (ORACLE.md X24): the prankster's cell argument is never read; d = dx² + dy² from the receiver's cell bytes +5 and +7, unsigned, strictly smaller replaces, the best starting at 0xFFFFFFFF; states 0 and 1 only (0x004D6330)
	public static int? ChooseGuard( IEnumerable<(StaffJob Job, (int X, int Y) Cell)> guards, (int X, int Y) origin )
	{
		int? chosen = null;
		var best = uint.MaxValue;
		var index = 0;
		foreach ( var (job, cell) in guards )
		{
			if ( job is StaffJob.Idle or StaffJob.Patrol )
			{
				var dx = cell.X - origin.X;
				var dy = cell.Y - origin.Y;
				var distance = (uint)(dx * dx + dy * dy);
				if ( distance < best )
					(chosen, best) = (index, distance);
			}
			index++;
		}
		return chosen;
	}

	/// <summary>
	/// The chase (tp.exe <c>0x004D6790</c>): the timer counts down; the guard re-routes when he arrives where the prankster no longer is; on the
	/// prankster's cell he catches it if its state allows, and carries it towards the ticket booth.
	/// </summary>
	// [BIN:TPW-EXE:0x004D6790 chase] the work drain (0x00506760); no target or a timer of 0 abandons, else the timer +0x214 counts down; the walk step: arrived on another cell than the guest's re-routes to its cell (none: abandon), held (2) abandons, another cell waits; on its cell 0x004FEC10 (states 6–10) or abandon; caught: +0xC2 = 0, cat_staff 137, the guest held (0x004FEB10, state 17), a route to the ticket booth (0x004D8610, the turn's bit 0; a second try with +0x188 = 1): state 18, else the guest is set down on the guard's cell and he abandons; abandoning sets +0xC2 = 0, state 0 and clears the target
	private void Chase( ParkEconomy economy, StaffMember member, long turn, int maxSpeed, int maxForce )
	{
		StaffVitals.Work( member );
		if ( member.TargetGuestId == 0 || member.JobStamp == 0 || Guests?.CellOf( member.TargetGuestId ) is not { } target )
		{
			Abandon( economy, member, turn );
			return;
		}
		member.JobStamp--;
		var result = Walk( member, maxSpeed, maxForce );
		var cell = Cell( member );
		if ( result == WalkResult.Arrived && cell != target )
		{
			if ( !Route( member, target, GuestWander.CentreByte, GuestWander.CentreByte ) )
				Abandon( economy, member, turn );
			return;
		}
		if ( result == WalkResult.Stuck )
		{
			Abandon( economy, member, turn );
			return;
		}
		if ( cell != target )
			return;
		if ( !Guests.IsCatchable( member.TargetGuestId ) )
		{
			Abandon( economy, member, turn );
			return;
		}
		// [BIN:TPW-EXE:0x004D691B caught kid counted] the analyser's misbehaving-kids call (0x004C74F0 with a nonzero argument) runs right after the catch succeeds (0x004FEC10), before the sound and the hold: the month and lifetime words
		economy.RecordGuardChase( caught: true );
		member.SpeedBonus = 0;
		// [BIN:TPW-EXE:0x004D6790 caught a kid] right after the catch (0x004FEC10) the guard queues template 0x16 (the catch, which hands over to the hold), before the guest is held
		StaffSpriteEvents.GuardCaught( member );
		Sound( member, CatchEvent );
		Guests.Hold( member.TargetGuestId );
		if ( RouteToGate( member, GateCell.TicketBooth, turn ) )
		{
			SetJob( economy, member, StaffJob.Escorting, turn );
			return;
		}
		// the second try may leave the paths (+0x188 = 1, 0x004D6977)
		member.Navigation!.Mask = 1;
		if ( RouteToGate( member, GateCell.TicketBooth, turn ) )
		{
			SetJob( economy, member, StaffJob.Escorting, turn );
			return;
		}
		SetDown( economy, member );
		Abandon( economy, member, turn );
	}

	// [BIN:TPW-EXE:0x004D6A01 abandoned chase] every way out of the chase that gives it up (no target or timer, a failed route, the held guest, the failed catch, the failed second route) reaches this tail: "Guard: abandoned chase" (0x75A944), the analyser's misbehaving-kids call with 0 (0x004D6A1F: the month, lifetime and got-away words), the state back to idle (0x4D65D0 with 0, the target +0x212 cleared)
	private static void Abandon( ParkEconomy economy, StaffMember member, long turn )
	{
		economy.RecordGuardChase( caught: false );
		member.SpeedBonus = 0;
		SetJob( economy, member, StaffJob.Idle, turn );
	}

	/// <summary>
	/// The guard sets his guest down where he stands (tp.exe <c>0x004FEB50</c>): cat_kids 128 there, and the guest walks out of the park by the side
	/// a world draw's bit 0 picks, or is removed when that side is out of reach.
	/// </summary>
	private void SetDown( ParkEconomy economy, StaffMember member )
	{
		var cell = Cell( member );
		// [BIN:TPW-EXE:0x004FEBC1 set-down side] one world draw (0x00516330); its bit 0 is the gate cell 0x004D86D0 returns, 0 side A, 1 side B
		var side = (int)(Draw( economy ) & 1);
		Sound( member, SetDownEvent, kids: true, cell );
		Guests?.Release( member.TargetGuestId, cell, side );
	}

	private enum GateCell { TicketBooth, Entrance }

	/// <summary>
	/// A route to the ticket booth (tp.exe <c>0x004D8610</c>) or the park entrance (<c>0x004D8690</c>) of arrival lane A or B by the turn's bit 0
	/// (<c>FixedItemInfo.TicketBoothA/BPos</c>, <c>EntranceA/BPos</c>).
	/// </summary>
	// [DATA:levels/Standard.sam:FixedItemInfo.TicketBoothAPosX..EntranceBPosY] the gate cells at 0x0078559C.. (ticket booths) and 0x007855BC.. (entrances), cell id y × 128 + x + 1
	// [BIN:TPW-EXE:0x004D6954 booth route] the catch asks 0x004D8610 for the booth cell of the side the world turn's bit 0 names ([game +0x1DA70C] & 1, the same side for both tries) and requests a route to that one cell (0x004FA530, SetDest 0x00510100 on the cell map with the walk mask), first with the guard's mask, then with +0x188 = 1 (0x004D6977, 0x004D699C); the guard never routes to the entrance in its place, the entrance (0x004D8690) is only the way back after the escort (0x004D6AF0)
	private bool RouteToGate( StaffMember member, GateCell gate, long turn )
	{
		// the booth comes from the level's gate data, not from the lane that must also end on a walkable entrance: tp.exe reads its fixed table (0x004D8610) and always routes (a level without the booth cells has no such table and the guard cannot route)
		(int X, int Y)? target;
		if ( gate == GateCell.TicketBooth )
			target = Guests?.TicketBooth( (int)(turn & 1) );
		else
			target = Guests?.Lane( (int)(turn & 1) ) is { Length: >= 5 } lane ? lane[^1] : null;
		if ( target is not { } cell )
			return false;
		return Route( member, cell, GuestWander.CentreByte, GuestWander.CentreByte );
	}
}
