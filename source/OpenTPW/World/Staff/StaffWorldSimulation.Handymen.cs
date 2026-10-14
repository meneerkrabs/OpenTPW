namespace OpenTPW;

/// <summary>A toilet for the handymen's search: its economy object id, its cell and the path cell in front of it (thing <c>+0x36</c>).</summary>
public readonly record struct ToiletSite( int Id, (int X, int Y) Cell, (int X, int Y) Access );

/// <summary>A staff voice the state machine drew this turn (cat_staff walking or idle, at the member's position in cells).</summary>
public readonly record struct StaffVoice( StaffType Type, bool Walking, float CellX, float CellY );

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

	/// <summary>The staff voices drawn since the last call (the handymen's walking and idle voices).</summary>
	public IReadOnlyList<StaffVoice> TakeVoices()
	{
		var taken = voices.ToArray();
		voices.Clear();
		return taken;
	}

	/// <summary>Whether this world runs the handymen (their voices then come from <see cref="TakeVoices"/>).</summary>
	public bool DrivesHandymen => true;

	// [BIN:TPW-EXE:0x004D73C0 handyman update] once per park turn after the pre-update 0x004D7060 (the speed 0x004FA870 and, while idle, a litter scan every IdleDuration turns): 0 idle until the turn passes +0x200 + IdleDuration[grade] (0x00785330 + 16 × grade); 1 patrol: one walk step, the walk drain while walking (0x005066A0), the job search on arrival or stuck; 8 to litter (0x004D7580); 9 sweep (0x004D76A0); 10 to a toilet (0x004D7790); 11 clean until the turn passes +0x214 + WorkDuration[grade] (0x004DFD80, 0x004DF390); every other state runs the generic staff handler
	// [APPROX:ECON-022] handymen in a level walk and search as the original (WANDER-plan §4, StaffWorldSimulation), but their rest and strike are the economy's without the walks to the rest or strike area, a toilet claim ends when the walk to it fails instead of lapsing after 100 turns, the player's maintenance request (mRequestedService) and patrol rectangle have no control, a handyman steps without the guests' separation, and a toilet's access cell is its join or entrance cell; a headless economy without a world keeps its own dispatch: the first unclaimed littered cell in the finder's order, park-wide, else the dirtiest toilet, timed from the dispatch — evidence needed: none (read, tp.exe 0x004D73C0 / 0x004D7100); the rest and strike walks and the controls are their own items
	public void UpdateHandymen( ParkEconomy economy, long turn )
	{
		// the voices wait for the audio until it takes them; a run that never takes them keeps only the latest
		if ( voices.Count > MaxQueuedVoices )
			voices.RemoveRange( 0, voices.Count - MaxQueuedVoices );
		Synchronize();
		foreach ( var member in economy.Staff.OfType( StaffType.Handyman ).ToList() )
		{
			var placed = member.Navigation == null;
			if ( !Place( member ) )
				continue;
			switch ( member.State )
			{
				case StaffState.PickedUp:
					member.Job = HandymanJob.PickedUp;
					continue;
				case StaffState.OnStrike:
					member.Job = HandymanJob.Striking;
					continue;
				case StaffState.Resting:
					member.Job = HandymanJob.Resting;
					continue;
			}
			if ( placed )
			{
				// a timed job of the headless dispatch (an older save) ends unfinished; a new handyman searches at once (the constructor
				// 0x004D6B60 runs the job search)
				DropDispatchedJob( economy, member );
				Search( economy, member, turn );
			}
			else if ( member.Job is HandymanJob.PickedUp or HandymanJob.Striking or HandymanJob.Resting or HandymanJob.ToRestArea or HandymanJob.ToStrike )
			{
				// Back from a rest or a strike: the original's rest end (0x00506D10) sets state 0 and runs the job search in the same update.
				SetJob( economy, member, HandymanJob.Idle, turn );
				Search( economy, member, turn );
			}
			else
				UpdateHandyman( economy, member, turn );
			var navigation = member.Navigation!;
			member.CellX = navigation.X / (float)Fixed16.One;
			member.CellY = navigation.Y / (float)Fixed16.One;
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

	private void UpdateHandyman( ParkEconomy economy, StaffMember member, long turn )
	{
		// the pre-update 0x004D7060: the movement routine's speed step (0x004FA870, shared with the guests), then an idle handyman scans for litter every IdleDuration turns
		(member.SmoothedSpeed, _, var maxSpeed, var maxForce) = GuestSpeed.Update( member.SmoothedSpeed, member.BaseSpeed, member.SpeedBonus, 0 );
		var cell = Cell( member );
		var settings = economy.Settings;
		if ( member.Job == HandymanJob.Idle && unchecked( (uint)turn ) % (uint)Math.Max( 1, settings.IdleDuration[member.Grade] ) == 0
			&& FindLitter( economy.Litter, cell, member.Id, DetectionRange( economy, member ) ) != null )
			// the search runs in the pre-update; the state handler then runs the state it chose in the same update
			Search( economy, member, turn );
		var work = settings[StaffType.Handyman].WorkDuration[member.Grade];
		switch ( member.Job )
		{
			case HandymanJob.Idle:
				Voice( economy, member, walking: false );
				if ( unchecked( (uint)turn ) <= unchecked( (uint)(settings.IdleDuration[member.Grade] + member.IdleStamp) ) )
					return;
				break;
			case HandymanJob.Patrol:
				Voice( economy, member, walking: true );
				if ( Walk( member, maxSpeed, maxForce ) == WalkResult.Walking )
				{
					StaffVitals.Walk( member );
					return;
				}
				break;
			case HandymanJob.ToLitter:
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
						SetJob( economy, member, HandymanJob.Sweeping, turn );
						return;
				}
				member.LitterCell = null;
				break;
			case HandymanJob.Sweeping:
				StaffVitals.Work( member );
				if ( unchecked( (uint)(work + member.JobStamp) ) >= unchecked( (uint)turn ) )
					return;
				// the sweep removes the litter of the cell he stands on
				economy.Litter.Remove( cell.X, cell.Y );
				member.LitterCell = null;
				break;
			case HandymanJob.ToToilet:
				switch ( Walk( member, maxSpeed, maxForce ) )
				{
					case WalkResult.Walking:
						return;
					case WalkResult.Arrived when Toilets?.Invoke().FirstOrDefault( site => site.Id == member.ToiletId ) is { } site && site.Access == cell
						&& economy.TryGetObject( member.ToiletId, out var toilet ) && toilet.MechanicId == member.Id:
						SetJob( economy, member, HandymanJob.Cleaning, turn );
						return;
				}
				ReleaseToilet( economy, member );
				SetJob( economy, member, HandymanJob.Idle, turn );
				return;
			case HandymanJob.Cleaning:
				StaffVitals.Work( member );
				if ( unchecked( (uint)turn ) <= unchecked( (uint)(work + member.JobStamp) ) )
					return;
				economy.FinishToiletClean( member );
				SetJob( economy, member, HandymanJob.Idle, turn );
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
		var energy = StaffVitals.Byte( member.Energy );
		if ( energy > settings.RestLevel )
			member.BaseSpeed = energy < 100 ? GuestSpeed.BaseSpeeds[energy / 20] : GuestSpeed.BaseSpeeds[^1];
		if ( economy.HandymanStrikeOrRest( member ) )
		{
			member.Job = member.State == StaffState.OnStrike ? HandymanJob.Striking : HandymanJob.Resting;
			return;
		}
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
					SetJob( economy, member, HandymanJob.ToLitter, turn );
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
				SetJob( economy, member, HandymanJob.ToToilet, turn );
				return;
			}
		}
		SetJob( economy, member, Patrol( economy, member, cell ) ? HandymanJob.Patrol : HandymanJob.Idle, turn );
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
		foreach ( var toilet in economy.Toilets() )
		{
			if ( toilet.MechanicId != 0 && toilet.MechanicId != member.Id || sites.FirstOrDefault( item => item.Id == toilet.Id ) is not { Id: > 0 } site )
				continue;
			var dx = site.Cell.X - cell.X;
			var dy = site.Cell.Y - cell.Y;
			if ( !ParkEconomy.IsDirtyToilet( toilet ) || dx * dx + dy * dy >= range * range )
				continue;
			if ( grid.FindPath( cell.X, cell.Y, site.Access.X, site.Access.Y ).Count > 0 )
				found = site;
		}
		return found;
	}

	private static void ReleaseToilet( ParkEconomy economy, StaffMember member )
	{
		if ( economy.TryGetObject( member.ToiletId, out var toilet ) && toilet.MechanicId == member.Id )
			toilet.MechanicId = 0;
		member.ToiletId = 0;
	}

	/// <summary>A patrol destination (<see cref="GuestWander"/>, the staff's 0x004F9490): true when one is set.</summary>
	private bool Patrol( ParkEconomy economy, StaffMember member, (int X, int Y) cell )
	{
		var area = member.PatrolArea;
		if ( GuestWander.Pick( grid, cell, () => (uint)(economy.Random.NextUInt64() >> 33),
			( x, y ) => x >= area.X0 && x <= area.X1 && y >= area.Y0 && y <= area.Y1 ) is not { } destination )
			return false;
		return Route( member, destination.Cell, destination.ByteX, destination.ByteY );
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

	/// <summary>One walk step (tp.exe <c>0x004FA2A0</c>): arrived, walking, or stuck when the route ahead lost a path cell.</summary>
	private WalkResult Walk( StaffMember member, int maxSpeed, int maxForce )
	{
		var navigation = member.Navigation!;
		if ( navigation.Arrived )
			return WalkResult.Arrived;
		foreach ( var (x, y) in navigation.Route )
		{
			if ( !grid.IsWalkable( x, y ) )
			{
				navigation.SetDestination( navigation.X, navigation.Y, [] );
				return WalkResult.Stuck;
			}
		}
		return navigation.Step( maxSpeed, maxForce, navigation.IsOnWalk ) ? WalkResult.Arrived : WalkResult.Walking;
	}

	/// <summary>
	/// The handyman's state setter (tp.exe <c>0x004D7330</c> and the staff setter <c>0x005054D0</c>): the walks to a job take the speed bonus,
	/// a sweep or a clean stamps its start, and idling stamps the turn when it follows a patrol (else 0, so the next update searches).
	/// </summary>
	// [BIN:TPW-EXE:0x004D7330 handyman state setter] 8 and 10: +0xC2 = 25 (0x0075C7F2); 9 and 11: +0x214 = the turn; others through 0x005054D0, whose state 0 sets +0x200 to the turn after state 1 and to 0 otherwise and +0xC2 to 0 (0x0075C7F0)
	private static void SetJob( ParkEconomy economy, StaffMember member, HandymanJob job, long turn )
	{
		switch ( job )
		{
			case HandymanJob.ToLitter or HandymanJob.ToToilet:
				member.SpeedBonus = GuestSpeed.ToiletBonus;
				break;
			case HandymanJob.Sweeping or HandymanJob.Cleaning:
				member.JobStamp = turn;
				break;
			case HandymanJob.Idle:
				member.IdleStamp = member.Job == HandymanJob.Patrol ? turn : 0;
				member.SpeedBonus = 0;
				break;
		}
		member.Job = job;
		// the display state (0x005064F0)
		member.State = job switch
		{
			HandymanJob.Idle => StaffState.Idle,
			HandymanJob.Patrol => StaffState.Patrolling,
			_ => StaffState.Working
		};
	}

	/// <summary>The walking or idle voice: a world draw whose low four bits are 0 plays cat_staff 160 or 161 (tp.exe <c>0x004D73C0</c>, states 1 and 0).</summary>
	private void Voice( ParkEconomy economy, StaffMember member, bool walking )
	{
		if ( (economy.Random.NextUInt64() >> 33 & 0xF) == 0 )
			voices.Add( new StaffVoice( member.Type, walking, member.CellX ?? 0, member.CellY ?? 0 ) );
	}

	private static (int X, int Y) Cell( StaffMember member ) => member.Navigation!.Cell;

	private static int DetectionRange( ParkEconomy economy, StaffMember member ) =>
		Math.Min( 5, economy.Settings.HandymanDetectionRange[member.Grade] );
}
