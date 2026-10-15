namespace OpenTPW;

/// <summary>A toilet for the handymen's search: its economy object id, its cell and the path cell in front of it (thing <c>+0x36</c>).</summary>
public readonly record struct ToiletSite( int Id, (int X, int Y) Cell, (int X, int Y) Access );

/// <summary>
/// A sound the staff state machines played this turn, at a position in cells: a walking or idle voice (<paramref name="Event"/> 0: the role's
/// cat_staff pair), or a cat_staff event of its own (a performance's end, a guard's chase and catch), or with <paramref name="Kids"/> a cat_kids event.
/// </summary>
public readonly record struct StaffVoice( StaffType Type, bool Walking, float CellX, float CellY, uint Event = 0, bool Kids = false );

/// <summary>
/// The handymen's state machine and walk (tp.exe <c>0x004D73C0</c>, docs/reverse/WANDER-plan.md §4): once per park turn each handyman
/// updates its speed, idles, patrols, walks to litter or a dirty toilet and sweeps or cleans it, timing the work from its arrival.
/// </summary>
public sealed partial class StaffWorldSimulation
{
	/// <summary>The handymen's toilets (relief objects with their cells); null: none.</summary>
	public Func<IReadOnlyList<ToiletSite>>? Toilets { get; set; }

	private readonly List<StaffVoice> voices = new();
	private const int MaxQueuedVoices = 64;

	/// <summary>The staff sounds played since the last call (the walking and idle voices and the work sounds of the members this world drives).</summary>
	public IReadOnlyList<StaffVoice> TakeVoices()
	{
		var taken = voices.ToArray();
		voices.Clear();
		return taken;
	}

	/// <summary>Whether this world runs the handymen, entertainers and guards (their voices then come from <see cref="TakeVoices"/>).</summary>
	public bool DrivesStaff => true;

	/// <summary>The guests a guard catches and an entertainer performs for; null: none (guards chase nobody, entertainers find no audience).</summary>
	public IGuardGuests? Guests { get; set; }

	// [BIN:TPW-EXE:0x004D73C0 handyman update] once per park turn after the pre-update 0x004D7060 (the speed 0x004FA870 and, while idle, a litter scan every IdleDuration turns): 0 idle until the turn passes +0x200 + IdleDuration[grade] (0x00785330 + 16 × grade); 1 patrol: one walk step, the walk drain while walking (0x005066A0), the job search on arrival or stuck; 8 to litter (0x004D7580); 9 sweep (0x004D76A0); 10 to a toilet (0x004D7790); 11 clean until the turn passes +0x214 + WorkDuration[grade] (0x004DFD80, 0x004DF390); every other state runs the generic staff handler
	// [APPROX:ECON-022] all staff in a level walk and work as the original's state machines (WANDER-plan §4, StaffWorldSimulation), with the shared walks to a rest area and to the strike area, but they step without the guests' separation, a striker walks out along arrival lane A (OpenTPW's path grid ends at the park entrance) and is not sent after a prankster until he is back on the paths, a rest ends when its rest area is sold, a resting member stays on the map (no staff model is drawn yet), a toilet claim ends when the walk to it fails instead of lapsing after 100 turns, a toilet's, bin's or rest area's access cell is its join or entrance cell or first path neighbour, a mechanic's finder scans the objects from the first (the original starts one further on each search) and his repair does not wait for the ride's fix animation, a mechanic's claim also ends when his walk fails or the ride no longer needs him, an entertainer's performance has no animation and the guests do not turn to watch, the guard list's order is the hire order, a prank reaches the guards at once, a picked-up entertainer's or guard's row stays on the cell he was lifted from, and a guard carries a caught guest to the park entrance when the ticket booth is no path cell; a headless economy without a world keeps its own handyman dispatch: the first unclaimed littered cell in the finder's order, park-wide, else the dirtiest toilet, timed from the dispatch — evidence needed: none (read, tp.exe 0x004D73C0 / 0x004D7100, 0x004D4810 / 0x004D46D0, 0x004D6410 / 0x00508A30 / 0x004D6790, 0x005029F0, 0x004DA490 / 0x004DA5B0, 0x00506A40 / 0x00505FE0 / 0x005061D0 / 0x00506300)
	// [BIN:TPW-EXE:0x0050B360 staff update] each staff thing in list order: the type's pre-update, then its state handler (4 mechanic 0x00505490 / 0x004DA490, 5 handyman 0x004D7060 / 0x004D73C0, 6 entertainer 0x004D4660 / 0x004D4810, 7 guard 0x004D6360 / 0x004D6410, 8 researcher 0x00502960 / 0x005029F0)
	public void UpdateStaff( ParkEconomy economy, long turn )
	{
		// the voices wait for the audio until it takes them; a run that never takes them keeps only the latest
		if ( voices.Count > MaxQueuedVoices )
			voices.RemoveRange( 0, voices.Count - MaxQueuedVoices );
		Synchronize();
		foreach ( var member in economy.Staff.Members.Where( economy.DrivenByWorld ).ToList() )
		{
			var placed = member.Navigation == null;
			if ( !Place( member ) )
				continue;
			// the entertainers' and guards' pre-update moves their RegionFX row with them (the constructor puts it on the first cell)
			economy.MoveStaffEffect( member, Cell( member ) );
			// [BIN:TPW-EXE:0x00505490 staff pre-update] on the member's phase ((id & 3) == (turn & 3)) the thought object's update 0x0050BE40 takes an old bubble away
			if ( member.BubbleThought != 0 && NeedsUpdate.IsPhase( (uint)turn, member.Id ) && (ulong)(member.BubbleTurn + GuestThoughts.BubbleTurns) < (ulong)turn )
				member.BubbleThought = 0;
			switch ( member.State )
			{
				case StaffState.PickedUp:
					// a guard picked up while escorting sets the guest down where he stands (0x004D65D0 state 7)
					if ( member.Job == StaffJob.Escorting && member.TargetGuestId != 0 )
					{
						SetDown( economy, member );
						member.TargetGuestId = 0;
					}
					member.Job = StaffJob.PickedUp;
					continue;
				// a strike or rest the economy started without a walk (an older save): the member strikes or rests where he stands
				case StaffState.OnStrike when member.Job is not (StaffJob.ToStrike or StaffJob.Striking):
					member.Job = StaffJob.Striking;
					break;
				case StaffState.Resting when member.Job is not (StaffJob.ToRestArea or StaffJob.Resting):
					member.Job = StaffJob.Resting;
					break;
			}
			if ( placed && member.Type is StaffType.Handyman or StaffType.Mechanic )
			{
				// a timed job of the headless dispatch (an older save) ends unfinished; a new handyman searches at once (the constructor
				// 0x004D6B60 runs the job search), and so does a mechanic placed with a dispatched job
				var dispatched = member.BusyTicks > 0;
				DropDispatchedJob( economy, member );
				if ( member.Type == StaffType.Handyman )
					Search( economy, member, turn );
				else if ( dispatched )
					SearchMechanic( economy, member, turn );
			}
			else if ( member.Job == StaffJob.PickedUp )
			{
				// set down by the player (0x00505EA0 sets state 0): the type's search runs in the same update
				SetJob( economy, member, StaffJob.Idle, turn );
				SearchByType( economy, member, turn );
			}
			else
			{
				// the pre-update's speed step (0x004FA870 through 0x00505490, shared with the guests)
				(member.SmoothedSpeed, _, var maxSpeed, var maxForce) = GuestSpeed.Update( member.SmoothedSpeed, member.BaseSpeed, member.SpeedBonus, 0 );
				if ( member.Job is StaffJob.ToRestArea or StaffJob.Resting or StaffJob.ToStrike or StaffJob.Striking )
					UpdateRestOrStrike( economy, member, turn, maxSpeed, maxForce );
				else
				{
					switch ( member.Type )
					{
						case StaffType.Handyman: UpdateHandyman( economy, member, turn, maxSpeed, maxForce ); break;
						case StaffType.Entertainer: UpdateEntertainer( economy, member, turn, maxSpeed, maxForce ); break;
						case StaffType.Guard: UpdateGuard( economy, member, turn, maxSpeed, maxForce ); break;
						case StaffType.Researcher: UpdateResearcher( economy, member, turn, maxSpeed, maxForce ); break;
						case StaffType.Mechanic: UpdateMechanic( economy, member, turn, maxSpeed, maxForce ); break;
					}
				}
			}
			var navigation = member.Navigation!;
			member.CellX = navigation.X / (float)Fixed16.One;
			member.CellY = navigation.Y / (float)Fixed16.One;
		}
		CountRestingStaff( economy );
	}

	/// <summary>The type's search, run when a member is set down or ends a rest (0x00506D10 dispatches by type: 5 0x004D7100, 6 0x004D46D0, 7 0x004D63D0, 8 0x00502C70).</summary>
	private void SearchByType( ParkEconomy economy, StaffMember member, long turn )
	{
		switch ( member.Type )
		{
			case StaffType.Handyman: Search( economy, member, turn ); break;
			case StaffType.Entertainer: SearchEntertainer( economy, member, turn ); break;
			case StaffType.Guard: SearchGuard( economy, member, turn ); break;
			case StaffType.Researcher: SearchResearcher( economy, member, turn ); break;
			case StaffType.Mechanic: SearchMechanic( economy, member, turn ); break;
		}
	}

	/// <summary>Releases a job the headless dispatch timed (its claim on litter or a toilet), so the economy's timer cannot finish it later.</summary>
	private static void DropDispatchedJob( ParkEconomy economy, StaffMember member )
	{
		if ( member.BusyTicks == 0 )
			return;
		if ( member.LitterCell is { } cell && economy.Litter.CollectorAt( cell.X, cell.Y ) == member.Id )
			economy.Litter.Claim( cell.X, cell.Y, 0, 0 );
		if ( member.AssignedInstanceId != 0 && economy.TryGetObject( member.AssignedInstanceId, out var toilet ) && toilet.MechanicId == member.Id )
			toilet.MechanicId = 0;
		member.LitterCell = null;
		member.AssignedInstanceId = 0;
		member.BusyTicks = 0;
		member.AwaitingJobArrival = false;
	}

	/// <summary>Gives a positioned member its steering state; false while it has no position.</summary>
	private bool Place( StaffMember member )
	{
		if ( member.Navigation != null )
			return true;
		if ( member.CellX is not { } x || member.CellY is not { } y )
			return false;
		var navigation = new GuestNavigation { X = (int)(x * Fixed16.One), Y = (int)(y * Fixed16.One) };
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		member.Navigation = navigation;
		member.BaseSpeed = member.Grade < 3 ? GuestSpeed.BaseSpeeds[0] : GuestSpeed.BaseSpeeds[2];
		return true;
	}

	private void UpdateHandyman( ParkEconomy economy, StaffMember member, long turn, int maxSpeed, int maxForce )
	{
		// the rest of the pre-update 0x004D7060: an idle handyman scans for litter every IdleDuration turns
		var cell = Cell( member );
		var settings = economy.Settings;
		if ( member.Job == StaffJob.Idle && unchecked( (uint)turn ) % (uint)Math.Max( 1, settings.IdleDuration[member.Grade] ) == 0
			&& FindLitter( economy.Litter, cell, member.Id, DetectionRange( economy, member ) ) != null )
			// the search runs in the pre-update; the state handler then runs the state it chose in the same update
			Search( economy, member, turn );
		var work = settings[StaffType.Handyman].WorkDuration[member.Grade];
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
			case StaffJob.ToLitter:
				if ( member.LitterCell is not { } target || economy.Litter.CollectorAt( target.X, target.Y ) != member.Id )
				{
					member.LitterCell = null;
					break;
				}
				switch ( Walk( member, maxSpeed, maxForce ) )
				{
					case WalkResult.Walking:
						return;
					case WalkResult.Arrived when economy.Litter.TypeAt( target.X, target.Y ) != LitterType.None:
						// 0x004FA930 puts him on the destination point; the sweep's time starts now
						var navigation = member.Navigation!;
						(navigation.X, navigation.Y) = (navigation.TargetX, navigation.TargetY);
						SetJob( economy, member, StaffJob.Sweeping, turn );
						return;
				}
				member.LitterCell = null;
				break;
			case StaffJob.Sweeping:
				StaffVitals.Work( member );
				if ( unchecked( (uint)(work + member.JobStamp) ) >= unchecked( (uint)turn ) )
					return;
				// the sweep removes the litter of the cell he stands on
				economy.Litter.Remove( cell.X, cell.Y );
				member.LitterCell = null;
				break;
			case StaffJob.ToToilet:
				switch ( Walk( member, maxSpeed, maxForce ) )
				{
					case WalkResult.Walking:
						return;
					case WalkResult.Arrived when Toilets?.Invoke().FirstOrDefault( site => site.Id == member.ToiletId ) is { } site && site.Access == cell
						&& economy.TryGetObject( member.ToiletId, out var toilet ) && toilet.MechanicId == member.Id:
						SetJob( economy, member, StaffJob.Cleaning, turn );
						return;
				}
				ReleaseToilet( economy, member );
				SetJob( economy, member, StaffJob.Idle, turn );
				return;
			case StaffJob.Cleaning:
				StaffVitals.Work( member );
				if ( unchecked( (uint)turn ) <= unchecked( (uint)(work + member.JobStamp) ) )
					return;
				economy.FinishToiletClean( member );
				SetJob( economy, member, StaffJob.Idle, turn );
				return;
		}
		Search( economy, member, turn );
	}

	/// <summary>
	/// The handyman's job search (tp.exe <c>0x004D7100</c>): the strike and rest gate, then (unless too tired) the nearest litter within
	/// DetectionRange, else the nearest dirty toilet in reach, else a patrol destination, else idle.
	/// </summary>
	// [BIN:TPW-EXE:0x004D7100 handyman job search] 0x00506A40 (strike, rest; a comfortable member's base speed from its energy, 0x0075C7F8) and 0x00506680 (energy byte below RestLevel: no jobs); the litter finder 0x004C8ED0 within DetectionRange (0x007853F0 + 12 × grade); a find reseeds the generator with the cell id for the walk's sub-cell point (0x004D71CA, 13 × clamp(r & 0x7F, 1, 9)), and a route claims the cell (0x004D9600) and sets state 8, no route skips to the patrol; without litter the toilet search 0x004D7880 sets state 10; then 0x004F9490 (state 1) or state 0
	private void Search( ParkEconomy economy, StaffMember member, long turn )
	{
		var settings = economy.Settings;
		if ( StrikeOrRest( economy, member, turn ) )
			return;
		var cell = Cell( member );
		if ( !StaffVitals.IsBelowRestLevel( member.Energy, settings.RestLevel ) )
		{
			if ( FindLitter( economy.Litter, cell, member.Id, DetectionRange( economy, member ) ) is { } litter )
			{
				member.LitterCell = litter;
				var (bx, by) = ParkLitter.SweepSpot( litter.X, litter.Y );
				if ( Route( member, litter, bx, by ) )
				{
					economy.Litter.Claim( litter.X, litter.Y, member.Id, turn );
					SetJob( economy, member, StaffJob.ToLitter, turn );
					return;
				}
				member.LitterCell = null;
			}
			else if ( FindToilet( economy, member, cell ) is { } toilet )
			{
				member.ToiletId = toilet.Id;
				if ( economy.TryGetObject( toilet.Id, out var state ) )
					state.MechanicId = member.Id;
				Route( member, toilet.Access, GuestWander.CentreByte, GuestWander.CentreByte );
				SetJob( economy, member, StaffJob.ToToilet, turn );
				return;
			}
		}
		SetJob( economy, member, Patrol( economy, member, cell ) ? StaffJob.Patrol : StaffJob.Idle, turn );
	}

	/// <summary>
	/// The litter finder (tp.exe <c>0x004C8ED0</c>): the cells within <paramref name="range"/> (a square, clipped to the map) that hold litter
	/// claimed by nobody or by <paramref name="collector"/>, scanned x outer and y inner; the nearest by Manhattan distance, the first on a tie.
	/// </summary>
	// [BIN:TPW-EXE:0x004C8ED0 litter finder] asserts a range below 6; collects the cells with +0x2C nonzero and +0x30 equal to 0 or the handyman's id, then keeps the first with the smallest |dx| + |dy| (strictly smaller replaces)
	public static (int X, int Y)? FindLitter( ParkLitter litter, (int X, int Y) cell, int collector, int range )
	{
		(int X, int Y)? best = null;
		var bestDistance = int.MaxValue;
		for ( var x = Math.Max( 0, cell.X - range ); x <= Math.Min( litter.Width - 1, cell.X + range ); x++ )
		{
			for ( var y = Math.Max( 0, cell.Y - range ); y <= Math.Min( litter.Height - 1, cell.Y + range ); y++ )
			{
				if ( litter.TypeAt( x, y ) == LitterType.None || litter.CollectorAt( x, y ) is not 0 && litter.CollectorAt( x, y ) != collector )
					continue;
				var distance = Math.Abs( x - cell.X ) + Math.Abs( y - cell.Y );
				if ( distance < bestDistance )
					(best, bestDistance) = ((x, y), distance);
			}
		}
		return best;
	}

	/// <summary>
	/// The toilet search (tp.exe <c>0x004D7880</c>): the relief objects in list order that nobody else has claimed, by squared distance from the
	/// handyman's cell to the object's; a dirty one (gauge byte below 25) within DetectionRange² is taken whatever the best so far, when a route
	/// reaches its access cell, so the last such one wins.
	/// </summary>
	// [BIN:TPW-EXE:0x004D7880 toilet search] candidates with flag +0x32 bit 0 whose claim (0x004E0220) is free or his; d = dx² + dy²; accept when (mRequestedService (0x004E03D0) and d below the best) or (the gauge byte below 25 (0x004E0390) and d below DetectionRange²), and the route test 0x004FA530 to +0x36 passes; the best becomes d; the winner is claimed (0x004E01F0)
	private ToiletSite? FindToilet( ParkEconomy economy, StaffMember member, (int X, int Y) cell )
	{
		if ( Toilets?.Invoke() is not { Count: > 0 } sites )
			return null;
		var range = DetectionRange( economy, member );
		ToiletSite? found = null;
		var best = uint.MaxValue;
		foreach ( var toilet in economy.Toilets() )
		{
			if ( toilet.MechanicId != 0 && toilet.MechanicId != member.Id || sites.FirstOrDefault( item => item.Id == toilet.Id ) is not { Id: > 0 } site )
				continue;
			var dx = site.Cell.X - cell.X;
			var dy = site.Cell.Y - cell.Y;
			var distance = (uint)(dx * dx + dy * dy);
			if ( !(toilet.MaintenanceRequested && distance < best || ParkEconomy.IsDirtyToilet( toilet ) && distance < (uint)(range * range)) )
				continue;
			if ( grid.FindPath( cell.X, cell.Y, site.Access.X, site.Access.Y ).Count > 0 )
				(found, best) = (site, distance);
		}
		return found;
	}

	private static void ReleaseToilet( ParkEconomy economy, StaffMember member )
	{
		if ( economy.TryGetObject( member.ToiletId, out var toilet ) && toilet.MechanicId == member.Id )
			toilet.MechanicId = 0;
		member.ToiletId = 0;
	}

	/// <summary>
	/// A patrol destination (<see cref="GuestWander"/>, the staff's 0x004F9490), kept inside the member's patrol rectangle: a member outside it walks to a
	/// random reachable cell inside it, and one whose every step leaves it does the same. True when a destination is set.
	/// </summary>
	// [BIN:TPW-EXE:0x004F9490 staff patrol area] for staff (types 4..8) a cell outside the rectangle (0x00506ED0: +0x20A and +0x20C are its corners' cell ids, inclusive) first tries 0x00506F30; when that fails ("I can't reach my patrol area", message 0x16) the wander runs without the rectangle; inside it, the candidates outside it are removed, and when none is left 0x00506F30 runs ("I'm inside my patrol area but can't get anywhere else within it" when it fails)
	private bool Patrol( ParkEconomy economy, StaffMember member, (int X, int Y) cell )
	{
		var area = member.PatrolArea;
		bool InArea( int x, int y ) => x >= area.X0 && x <= area.X1 && y >= area.Y0 && y <= area.Y1;
		var keepToArea = true;
		if ( !InArea( cell.X, cell.Y ) )
		{
			if ( EnterPatrolArea( economy, member ) )
				return true;
			keepToArea = false;
		}
		if ( GuestWander.Pick( grid, cell, () => Draw( economy ), keepToArea ? InArea : null, out var areaBlocked ) is { } destination )
			return Route( member, destination.Cell, destination.ByteX, destination.ByteY );
		return areaBlocked && EnterPatrolArea( economy, member );
	}

	/// <summary>
	/// A walk to a random cell of the patrol rectangle (tp.exe <c>0x00506F30</c>): up to 30 tries of two world draws (x, y) for a path cell a route reaches.
	/// </summary>
	// [BIN:TPW-EXE:0x00506F30 into the patrol area] x = draw % (x1 − x0 + 1) + x0, y = draw % (y1 − y0 + 1) + y0; a path cell (0x004D8300, 0x00536310) that the route 0x004FA530 reaches ends the search; after 30 tries "Could not find or reach a destination" and message 0x16
	private bool EnterPatrolArea( ParkEconomy economy, StaffMember member )
	{
		var area = member.PatrolArea;
		for ( var tries = 0; tries < 30; tries++ )
		{
			var x = (int)(Draw( economy ) % (uint)(area.X1 - area.X0 + 1)) + area.X0;
			var y = (int)(Draw( economy ) % (uint)(area.Y1 - area.Y0 + 1)) + area.Y0;
			if ( grid.IsWalkable( x, y ) && Route( member, (x, y), GuestWander.CentreByte, GuestWander.CentreByte ) )
				return true;
		}
		Think( member, StaffThoughts.NoDestination, economy.Turn );
		return false;
	}

	/// <summary>The member thinks <paramref name="thought"/> (tp.exe <c>0x0050BE80</c> on its thought object): stored, with a bubble when the last one is old enough.</summary>
	private static void Think( StaffMember member, int thought, long turn )
	{
		member.Thought = thought;
		member.BubbleThought = 0;
		if ( !GuestThoughts.TakesBubble( thought, turn, member.BubbleTurn ) )
			return;
		member.BubbleThought = thought;
		member.BubbleTurn = turn;
	}

	/// <summary>Sets the member's walk to a point of <paramref name="cell"/> along the shortest route; false when none reaches it.</summary>
	private bool Route( StaffMember member, (int X, int Y) cell, int byteX, int byteY )
	{
		var navigation = member.Navigation!;
		var start = navigation.Cell;
		var route = grid.FindPath( start.X, start.Y, cell.X, cell.Y );
		if ( route.Count == 0 )
			return false;
		var (x, y) = GuestNavigation.BytePoint( cell, byteX, byteY );
		navigation.SetDestination( x, y, route );
		return true;
	}

	private enum WalkResult { Arrived, Walking, Stuck }

	/// <summary>
	/// One walk step (tp.exe <c>0x004FA2A0</c>): arrived, walking, or stuck when the route ahead lost a path cell. A walk with an off-grid leg goes on
	/// past its path end along the leg's points; it arrives at the last one.
	/// </summary>
	private WalkResult Walk( StaffMember member, int maxSpeed, int maxForce )
	{
		var navigation = member.Navigation!;
		if ( !navigation.Arrived )
		{
			if ( !navigation.OffGrid )
			{
				foreach ( var (x, y) in navigation.Route )
				{
					if ( !grid.IsWalkable( x, y ) )
					{
						navigation.SetDestination( navigation.X, navigation.Y, [] );
						member.OffGridLeg.Clear();
						return WalkResult.Stuck;
					}
				}
			}
			if ( !navigation.Step( maxSpeed, maxForce, navigation.OffGrid ? static ( _, _ ) => true : navigation.IsOnWalk ) )
				return WalkResult.Walking;
		}
		if ( member.OffGridLeg.Count > 0 )
		{
			NextLeg( member );
			return WalkResult.Walking;
		}
		// back on a path cell, the member walks the grid again
		if ( navigation.OffGrid && grid.IsWalkable( navigation.Cell.X, navigation.Cell.Y ) )
			navigation.OffGrid = false;
		return WalkResult.Arrived;
	}

	/// <summary>
	/// The roles' state setters (tp.exe <c>0x004D7330</c> handyman, <c>0x004D65D0</c> guard, and the staff setter <c>0x005054D0</c>): the walks to a
	/// job and a chase take the speed bonus, a sweep, a clean or a performance stamps its start, a chase sets its timer, and idling stamps the turn
	/// when it follows a patrol (else 0, so the next update searches).
	/// </summary>
	// [BIN:TPW-EXE:0x004D7330 handyman state setter] 8 and 10: +0xC2 = 25 (0x0075C7F2); 9 and 11: +0x214 = the turn; others through 0x005054D0, whose state 0 sets +0x200 to the turn after state 1 and to 0 otherwise and +0xC2 to 0 (0x0075C7F0)
	// [BIN:TPW-EXE:0x004D65D0 guard state setter] 0: the target +0x212 = 0, then 0x005054D0(0); 16: +0xC2 = 25 (0x0075C7F2) and the chase timer +0x214 = GuardConstsPerGrade[grade].WorkDuration (0x00785498 + 8 × grade); 18, 19 and the others: +0xC2 = 0 (0x0075C7F0); 7 from 18 sets the guest down first
	private static void SetJob( ParkEconomy economy, StaffMember member, StaffJob job, long turn )
	{
		switch ( job )
		{
			case StaffJob.ToLitter or StaffJob.ToToilet:
				member.SpeedBonus = GuestSpeed.ToiletBonus;
				break;
			case StaffJob.Sweeping or StaffJob.Cleaning or StaffJob.Performing or StaffJob.Researching:
				member.JobStamp = turn;
				break;
			case StaffJob.Idle:
				member.IdleStamp = member.Job == StaffJob.Patrol ? turn : 0;
				member.SpeedBonus = 0;
				if ( member.Type == StaffType.Guard )
					member.TargetGuestId = 0;
				break;
			case StaffJob.Chasing:
				member.SpeedBonus = GuestSpeed.ToiletBonus;
				member.JobStamp = economy.Settings[StaffType.Guard].WorkDuration[member.Grade];
				break;
			case StaffJob.Patrol or StaffJob.Escorting or StaffJob.ReturningFromEscort when member.Type == StaffType.Guard:
				member.SpeedBonus = 0;
				break;
			// the shared setter 0x005054D0: 4 (to the strike) takes the bonus 25 (0x0075C7F2), 5 (striking) clears it and the heading
			case StaffJob.ToStrike:
				member.SpeedBonus = GuestSpeed.ToiletBonus;
				break;
			case StaffJob.Striking:
				member.SpeedBonus = 0;
				break;
			// states 3 (resting) and 7 (picked up) take the thought bubble away (0x0050C0D0)
			case StaffJob.Resting or StaffJob.PickedUp:
				member.BubbleThought = 0;
				break;
		}
		member.Job = job;
		member.State = ParkStaff.DisplayState( (uint)job );
	}

	/// <summary>
	/// The walking or idle voice: a world draw whose low four bits are 0 plays the role's cat_staff pair (handyman 160/161, entertainer 164/165,
	/// guard 166/167; tp.exe <c>0x004D73C0</c>, <c>0x004D4810</c>, <c>0x004D6410</c>, states 1 and 0).
	/// </summary>
	private void Voice( ParkEconomy economy, StaffMember member, bool walking )
	{
		if ( (Draw( economy ) & 0xF) == 0 )
			voices.Add( new StaffVoice( member.Type, walking, member.CellX ?? 0, member.CellY ?? 0 ) );
	}

	/// <summary>A cat_staff event of the member's own at its position (or, with <paramref name="kids"/>, a cat_kids event at <paramref name="cell"/>).</summary>
	private void Sound( StaffMember member, uint eventId, bool kids = false, (int X, int Y)? cell = null )
	{
		var (x, y) = cell is { } at ? (at.X + 0.5f, at.Y + 0.5f) : (member.Navigation!.X / (float)Fixed16.One, member.Navigation.Y / (float)Fixed16.One);
		voices.Add( new StaffVoice( member.Type, false, x, y, eventId, kids ) );
	}

	/// <summary>One draw of the world generator (tp.exe <c>0x00516330</c>; OpenTPW's economy stream, DET-014).</summary>
	private static uint Draw( ParkEconomy economy ) => (uint)(economy.Random.NextUInt64() >> 33);

	private static (int X, int Y) Cell( StaffMember member ) => member.Navigation!.Cell;

	private static int DetectionRange( ParkEconomy economy, StaffMember member ) =>
		Math.Min( 5, economy.Settings.HandymanDetectionRange[member.Grade] );
}
