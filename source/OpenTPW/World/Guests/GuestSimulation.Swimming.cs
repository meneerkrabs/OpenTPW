namespace OpenTPW;

// The swim session of the pools extension (docs/POOLS.md, "Swimming"): a guest released from a changing room swims in the pool complex beside it, swims back, changes and steps out.
// Everything here is gated by the changing room: a park without one never reaches any of it, draws nothing from the world generator and hashes nothing extra.
public sealed partial class GuestSimulation
{
	/// <summary>The water regions by pool-side cell, rebuilt when a pool cell is written (<see cref="ParkCellMap.PoolWrites"/>).</summary>
	private readonly Dictionary<(int X, int Y), (int Stamp, PoolSwimRegion? Region)> swimRegions = new();
	private readonly List<(int X, int Y)> swimOthers = new();

	/// <summary>The park turns a swimmer may take to reach the steps after its swim: after them it is put into the changing room where it is (a guard against a goal nobody can reach).</summary>
	public const int ClimbOutLimitTurns = 160;

	/// <summary>
	/// [EXT:pools-swim] The pool-side cell of a changing room: the first of its back cells that holds pool water now, or null when none does (the pool beside it was erased).
	/// </summary>
	// [EXT:pools-swim] OpenTPW's own swim session; the original game has no pools
	public (int X, int Y)? PoolAccessOf( IRideVisitorBridge ride )
	{
		foreach ( var cell in ride.PoolAccessCandidates )
			if ( Grid.InBounds( cell.X, cell.Y ) && Grid.Cells.RawTypeAt( cell.X, cell.Y ) == ParkCellTypes.PoolWater )
				return cell;
		return null;
	}

	/// <summary>[EXT:pools-swim] The water a changing room's swimmers share: the pool cells connected to its pool-side cell; null when it has no pool beside it.</summary>
	public PoolSwimRegion? SwimRegionOf( IRideVisitorBridge ride )
	{
		if ( !ride.IsChangingRoom || PoolAccessOf( ride ) is not { } access )
			return null;
		var stamp = Grid.Cells.PoolWrites;
		if ( swimRegions.TryGetValue( access, out var cached ) && cached.Stamp == stamp )
			return cached.Region;
		var region = PoolSwimRegion.Build( Grid.Cells, access );
		swimRegions[access] = (stamp, region);
		return region;
	}

	/// <summary>[EXT:pools-swim] Whether the guest is aboard a changing room, changing into swimwear (hidden; the room's script holds it for the changing time).</summary>
	public bool IsChangingIn( Guest guest ) => guest.State == GuestState.Using && FindAttraction( guest.AttractionId ) is { IsChangingRoom: true };

	/// <summary>
	/// [EXT:pools-swim] The guests in a swim session of the changing room with this attraction id, for erase guards and tests: swimming, climbing out or changing back.
	/// </summary>
	public IEnumerable<Guest> SwimmersOf( int attractionId ) => guests.Where( guest => guest.InSwimSession && guest.AttractionId == attractionId );

	/// <summary>[EXT:pools-swim] How many bodies (swimmers, floaters, guests under a jet: <see cref="IsWaterBody"/>) are in the water of <paramref name="region"/>, whichever changing room they came from.</summary>
	private int SwimmerCountIn( PoolSwimRegion region )
	{
		var count = 0;
		foreach ( var guest in guests )
			if ( IsWaterBody( guest, region ) )
				count++;
		return count;
	}

	/// <summary>
	/// [EXT:pools-swim] Whether a guest is a body in the water of the region now: swimming or climbing out there, or in a state-25 activity whose <see cref="IPoolActivity.InWater"/> says so (a river floater, a
	/// guest under a jet, a landing). These are the obstacles free swimmers keep their distance from (docs/POOLS.md, "Activities").
	/// </summary>
	internal bool IsWaterBody( Guest guest, PoolSwimRegion region )
	{
		if ( !region.Contains( guest.CellX, guest.CellY ) )
			return false;
		return guest.IsSwimming || (guest.State == GuestState.PoolActivity && PoolActivityOf( guest )?.InWater( this, guest ) == true);
	}

	// A cell-bucketed lookup of the guests that are, or may become within the tick, bodies in the water: states 22, 23 and 25, in buckets of 2 x 2 cells by their cell at the start of the tick (rebuilt once
	// per tick on the first query, in guest list order, so the order is deterministic). A body moves less than a cell a turn, so a query that reads the 3 x 3 buckets around its point finds every body within
	// 1.2 cells; the live state and position are tested at the query.
	private readonly Dictionary<(int X, int Y), List<Guest>> waterBuckets = new();
	private readonly Stack<List<Guest>> waterBucketPool = new();
	private readonly List<Guest> waterNear = new();
	private long waterBucketTick = -1;

	private void EnsureWaterBuckets()
	{
		if ( waterBucketTick == TickCount )
			return;
		waterBucketTick = TickCount;
		foreach ( var list in waterBuckets.Values )
		{
			list.Clear();
			waterBucketPool.Push( list );
		}
		waterBuckets.Clear();
		foreach ( var guest in guests )
			if ( guest.State is GuestState.Swimming or GuestState.ClimbingOut or GuestState.PoolActivity )
				AddToWaterBuckets( guest );
	}

	private void AddToWaterBuckets( Guest guest )
	{
		var key = (guest.CellX >> 1, guest.CellY >> 1);
		if ( !waterBuckets.TryGetValue( key, out var list ) )
			waterBuckets[key] = list = waterBucketPool.Count > 0 ? waterBucketPool.Pop() : new List<Guest>();
		list.Add( guest );
	}

	/// <summary>
	/// [EXT:pools-swim] Collects into <paramref name="into"/> the bodies in the water of the region (<see cref="IsWaterBody"/>) other than <paramref name="except"/> whose point is within <paramref name="radius"/> (16.16 cells, at most
	/// 1.2 cells) of (<paramref name="x"/>, <paramref name="y"/>) in both axes. Read-only, no random numbers.
	/// </summary>
	internal void GatherWaterBodies( PoolSwimRegion region, Guest? except, int x, int y, int radius, List<Guest> into )
	{
		into.Clear();
		EnsureWaterBuckets();
		int bx = (x >> 16) >> 1, by = (y >> 16) >> 1;
		for ( var dy = -1; dy <= 1; dy++ )
			for ( var dx = -1; dx <= 1; dx++ )
				if ( waterBuckets.TryGetValue( (bx + dx, by + dy), out var list ) )
					foreach ( var other in list )
						if ( other != except && Math.Abs( other.Navigation.X - x ) < radius && Math.Abs( other.Navigation.Y - y ) < radius && IsWaterBody( other, region ) )
							into.Add( other );
	}

	/// <summary>[EXT:pools-swim] The bodies in the water within <paramref name="radius"/> (in both axes) of a point, other than <paramref name="except"/>. The list is valid until the next query.</summary>
	internal IReadOnlyList<Guest> WaterBodiesNear( PoolSwimRegion region, Guest? except, int x, int y, int radius )
	{
		GatherWaterBodies( region, except, x, y, radius, waterNear );
		return waterNear;
	}

	/// <summary>[EXT:pools-swim] The points of the bodies near a point, for <see cref="PoolSwimming.Step"/>: within <see cref="PoolSwimSettings.AvoidRadius"/> in both axes. The span is valid until the next call.</summary>
	internal ReadOnlySpan<(int X, int Y)> WaterBodyPoints( PoolSwimRegion region, Guest? except, int x, int y )
	{
		GatherWaterBodies( region, except, x, y, PoolSwimSettings.AvoidRadius, waterNear );
		swimOthers.Clear();
		foreach ( var other in waterNear )
			swimOthers.Add( (other.Navigation.X, other.Navigation.Y) );
		return System.Runtime.InteropServices.CollectionsMarshal.AsSpan( swimOthers );
	}

	/// <summary>[EXT:pools-swim] Whether another body is in the water within <paramref name="radius"/> (16.16 cells, at most 1 cell) of the point: an activity waits for its landing zone to be free by it.</summary>
	public bool IsWaterBodyNear( PoolSwimRegion region, Guest? except, int x, int y, int radius )
	{
		GatherWaterBodies( region, except, x, y, radius, waterNear );
		foreach ( var other in waterNear )
			if ( Fixed16.Length( other.Navigation.X - x, other.Navigation.Y - y ) < radius )
				return true;
		return false;
	}

	/// <summary>[EXT:pools-swim] Whether a swimmer's position lies in the cell (the pool tool does not erase water under swimmers).</summary>
	public bool IsSwimmerIn( int x, int y ) => guests.Any( guest => (guest.IsSwimming || guest.InPoolActivity) && guest.CellX == x && guest.CellY == y );

	/// <summary>
	/// A guest was released from a changing room: it steps into the water at the pool-side cell and swims. False when it cannot (not a changing room, the room is being demolished or has stopped,
	/// the park is closed, no pool beside it, or the water is full, <see cref="PoolSwimRegion.Capacity"/>): the guest then walks on like any other released guest. One draw of the world generator seeds the swimmer's own random stream; nothing else of the session touches the world generator.
	/// </summary>
	private bool BeginSwim( Guest guest, IRideVisitorBridge ride )
	{
		// a room that is being demolished (or whose script stopped) releases its guests the way any attraction does: they step out on its exit, no swim starts
		if ( !ride.IsChangingRoom || ride.IsStopped || Payments?.ParkClosed == true || SwimRegionOf( ride ) is not { } region )
			return false;
		// a full pool takes nobody more: the guest leaves the room the normal way, as from a ride (no draw is taken)
		if ( SwimmerCountIn( region ) >= region.Capacity )
			return false;
		var stream = random.NextWord();
		var swimmer = stream == 0 ? 1u : stream;
		guest.SwimTurns = PoolSwimSettings.MinimumTurns + (int)(PoolSwimming.Next( ref swimmer ) % PoolSwimSettings.ExtraTurns);
		var pick = (int)(PoolSwimming.Next( ref swimmer ) % 10);
		guest.SwimStroke = pick < 5 ? 0 : pick < 8 ? 1 : 2;
		guest.SwimHeading = (int)(PoolSwimming.Next( ref swimmer ) % PoolSwimSettings.Headings);
		guest.SwimRandom = swimmer;
		guest.SwimHasGoal = false;
		// every guest enters a little off the cell's centre, by its id, so those who step in together do not start on one point
		var (x, y) = PoolSwimRegion.Centre( region.Access );
		x += (((guest.Id * 37) & 15) - 8) * 800;
		y += (((guest.Id * 59) & 15) - 8) * 800;
		if ( !region.IsWater( x, y ) )
			(x, y) = PoolSwimRegion.Centre( region.Access );
		var navigation = guest.Navigation;
		(navigation.X, navigation.Y) = (x, y);
		navigation.SetDestination( x, y, [] );
		guest.AttractionId = ride.AttractionId;
		SetSwimCell( guest );
		guest.HasWaypoint = false;
		guest.IsMoving = false;
		guest.State = GuestState.Swimming;
		(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (guest.X, guest.Y, guest.DistanceWalked, TickCount);
		UpdatePresentation( guest );
		SetSwimHeading( guest );
		// a body of this tick for the swimmers after it
		if ( waterBucketTick == TickCount )
			AddToWaterBuckets( guest );
		// [EXT:pools-activities] a shower comes before the water
		if ( poolActivities.Count > 0 )
			OfferActivitiesAtStart( guest, region );
		return true;
	}

	/// <summary>
	/// [EXT:debug-pool] Smoke-only (<c>--debug-swimmers</c>, docs/POOLS.md): puts <paramref name="count"/> new guests into the water of a changing room at once, as if they had just changed, so a capture shows swimmers
	/// without waiting for guests to choose the room. Returns how many are swimming.
	/// </summary>
	internal int DebugStartSwimmers( IRideVisitorBridge ride, int count )
	{
		var started = 0;
		for ( var index = 0; index < count; index++ )
		{
			var guest = SpawnInPark( ride.ExitCell.X, ride.ExitCell.Y );
			guest.AttractionId = ride.AttractionId;
			if ( BeginSwim( guest, ride ) )
			{
				// each starts at a point of the water of its own, drawn from its own stream, so a capture shows them spread over the pool
				var random = guest.SwimRandom;
				var (x, y) = PoolSwimming.PickGoal( SwimRegionOf( ride )!, ref random );
				(guest.Navigation.X, guest.Navigation.Y, guest.SwimRandom) = (x, y, random);
				SetSwimCell( guest );
				(guest.StepFromX, guest.StepFromY) = (guest.X, guest.Y);
				UpdatePresentation( guest );
				started++;
			}
			else
				guest.AttractionId = 0;
		}
		return started;
	}

	/// <summary>The cell a swimmer is in; swimmers stay out of the cell lists walkers separate by.</summary>
	private void SetSwimCell( Guest guest )
	{
		var cell = guest.Navigation.Cell;
		guest.OnGrid = true;
		guest.Navigation.OffGrid = false;
		(guest.PreviousCellX, guest.PreviousCellY) = (-1, -1);
		(guest.CellX, guest.CellY) = cell;
		LeaveCellLists( guest );
	}

	/// <summary>Takes the guest out of the walkers' cell lists (a swimmer is in the water, a guest changing is indoors).</summary>
	private void LeaveCellLists( Guest guest )
	{
		if ( guest.OccupiedCell is { } previous && occupants.TryGetValue( previous, out var old ) )
			old.Remove( guest );
		guest.OccupiedCell = null;
	}

	private static void SetSwimHeading( Guest guest )
	{
		var (x, y) = PoolSwimming.Direction( guest.SwimHeading );
		guest.HeadingX = x / (float)Fixed16.One;
		guest.HeadingY = y / (float)Fixed16.One;
	}

	private static void ClearSwim( Guest guest )
	{
		guest.SwimHeading = 0;
		guest.SwimTurns = 0;
		guest.SwimStroke = 0;
		guest.SwimGoalX = guest.SwimGoalY = 0;
		guest.SwimHasGoal = false;
		guest.SwimRandom = 0;
	}

	/// <summary>State 22: one swimming stroke per park turn until the swim is over, the park closes or the pool goes.</summary>
	private void UpdateSwimming( Guest guest )
	{
		var ride = FindAttraction( guest.AttractionId );
		if ( ride == null || SwimRegionOf( ride ) is not { } region )
		{
			EndSwimAtOnce( guest, ride );
			return;
		}
		if ( !turnStarted )
			return;
		// [EXT:pools-swim] a closed park sends the swimmers out of the water (the guests' own leaving test runs once they are back on the path)
		if ( --guest.SwimTurns <= 0 || Payments?.ParkClosed == true )
		{
			guest.State = GuestState.ClimbingOut;
			guest.SwimTurns = ClimbOutLimitTurns;
			(guest.SwimGoalX, guest.SwimGoalY) = PoolSwimRegion.Centre( region.Access );
			guest.SwimHasGoal = true;
			return;
		}
		// [EXT:pools-activities] a swimmer may take an offer of a registered activity (none draws nothing)
		if ( TryOfferActivity( guest, region ) )
			return;
		// [EXT:pools-activities] the ambient effects of the registered activities (a wave, a jet) act on a swimmer that took no offer
		foreach ( var activity in poolActivities )
			if ( activity is IPoolAmbient ambient )
				ambient.Ambient( this, guest, region );
		// [EXT:pools-waves] and the currents of the water (the waves) carry it after its stroke
		var (pushX, pushY) = PoolCurrent( guest, region );
		SwimStep( guest, region, ignoreOthers: false, pushX, pushY );
	}

	/// <summary>State 23: swimming to the pool-side cell; the crowd at the steps is ignored within <see cref="PoolSwimSettings.CrowdedRadius"/> of it. Then the guest changes back.</summary>
	private void UpdateClimbingOut( Guest guest )
	{
		var ride = FindAttraction( guest.AttractionId );
		if ( ride == null || SwimRegionOf( ride ) is not { } region )
		{
			EndSwimAtOnce( guest, ride );
			return;
		}
		if ( !turnStarted )
			return;
		var steps = PoolSwimRegion.Centre( region.Access );
		var distance = Fixed16.Octile( steps.X - guest.Navigation.X, steps.Y - guest.Navigation.Y );
		if ( distance < PoolSwimSettings.GoalRadius || --guest.SwimTurns <= 0 )
		{
			var navigation = guest.Navigation;
			(navigation.X, navigation.Y) = steps;
			navigation.SetDestination( steps.X, steps.Y, [] );
			SetSwimCell( guest );
			(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (guest.X, guest.Y, guest.DistanceWalked, TickCount);
			UpdatePresentation( guest );
			guest.IsMoving = false;
			// the swim is paid in happiness when the guest is back in the changing room
			guest.Happiness = MeterMath.Add( guest.Happiness, PoolSwimSettings.HappinessGain );
			guest.SwimTurns = PoolSwimSettings.ChangeOutTurns;
			guest.SwimHasGoal = false;
			guest.State = GuestState.ChangingOut;
			return;
		}
		(guest.SwimGoalX, guest.SwimGoalY, guest.SwimHasGoal) = (steps.X, steps.Y, true);
		SwimStep( guest, region, ignoreOthers: distance < PoolSwimSettings.CrowdedRadius );
	}

	/// <summary>State 24: hidden in the changing room for a few turns, then out on the room's path-side exit like a guest leaving a ride.</summary>
	private void UpdateChangingOut( Guest guest )
	{
		if ( !turnStarted || --guest.SwimTurns > 0 )
			return;
		EndSwimAtOnce( guest, FindAttraction( guest.AttractionId ) );
		if ( guest.State == GuestState.DoingNothing )
		{
			guest.State = GuestState.ExitingRide;
			guest.StateTimer = ExitRideSeconds;
		}
	}

	/// <summary>
	/// Ends the session on the path: the guest is set on the changing room's path-side exit (or the nearest path when the room is gone) and decides again. Used when the session ends,
	/// when the pool beside the room is gone and when the room is demolished (<see cref="Unregister"/>).
	/// </summary>
	private void EndSwimAtOnce( Guest guest, IRideVisitorBridge? ride )
	{
		// [EXT:pools-activities] an activity in progress lets go of what it holds first
		if ( guest.State == GuestState.PoolActivity )
		{
			PoolActivityById( guest.PoolActivityId )?.Abort( this, guest );
			ClearPoolActivity( guest );
		}
		ClearSwim( guest );
		guest.IsMoving = false;
		// leave the swim state first: the cell lists (Occupy) leave a swimmer out, and the guest is a walker again from the cell it is placed on
		guest.State = GuestState.DoingNothing;
		ReturnToPath( guest, ride?.ExitCell ?? (guest.CellX, guest.CellY) );
		// a guest with balloon life gets its balloon back on stepping out, as it does when it leaves a ride (the swim put it away, like a ride does)
		if ( guest.BalloonLife > 0 )
			CreateBalloon( guest );
	}

	/// <summary>
	/// [EXT:pools-waves] The push of the water's currents (the registered <see cref="IPoolCurrent"/>s: the waves) on a body in the water of <paramref name="region"/> this turn, in 16.16 cells; zero for none.
	/// Integers only; the free swimmers' step and the lazy river's floaters add it.
	/// </summary>
	internal (int X, int Y) PoolCurrent( Guest guest, PoolSwimRegion region )
	{
		var (x, y) = (0, 0);
		foreach ( var activity in poolActivities )
			if ( activity is IPoolCurrent current )
			{
				var push = current.Current( this, guest, region );
				(x, y) = (x + push.X, y + push.Y);
			}
		return (x, y);
	}

	/// <summary>One steering step of a swimmer; the others are the swimmers of the same changing room. A push (16.16 cells, the waves) then carries it where there is room.</summary>
	private void SwimStep( Guest guest, PoolSwimRegion region, bool ignoreOthers, int pushX = 0, int pushY = 0 )
	{
		var navigation = guest.Navigation;
		// the water may have changed since the last turn (a cell erased beside the swimmer): a swimmer outside it is put back at the middle of its cell, or at the steps
		if ( !region.IsWater( navigation.X, navigation.Y ) )
		{
			var cell = region.Contains( navigation.X >> 16, navigation.Y >> 16 ) ? (navigation.X >> 16, navigation.Y >> 16) : region.Access;
			(navigation.X, navigation.Y) = PoolSwimRegion.Centre( cell );
		}
		// every body in the same water, whichever changing room it came from (two rooms may share one pool), and every guest of an activity that is in the water
		var others = ignoreOthers ? ReadOnlySpan<(int X, int Y)>.Empty : WaterBodyPoints( region, guest, navigation.X, navigation.Y );
		var body = new PoolSwimBody
		{
			X = navigation.X, Y = navigation.Y, Heading = guest.SwimHeading, GoalX = guest.SwimGoalX, GoalY = guest.SwimGoalY, HasGoal = guest.SwimHasGoal, Random = guest.SwimRandom
		};
		var (fromX, fromY, fromDistance) = (guest.X, guest.Y, guest.DistanceWalked);
		var moved = PoolSwimming.Step( region, ref body, others, guest.BaseSpeed * PoolSwimSettings.SpeedPerBaseSpeed, ignoreOthers );
		// the drift carries the swimmer but is no stroke: a swimmer only carried does not count distance (its stroke animation stays where it was)
		PoolSwimming.Drift( region, ref body, pushX, pushY, others );
		(navigation.X, navigation.Y) = (body.X, body.Y);
		(guest.SwimHeading, guest.SwimGoalX, guest.SwimGoalY, guest.SwimHasGoal, guest.SwimRandom) = (body.Heading, body.GoalX, body.GoalY, body.HasGoal, body.Random);
		SetSwimCell( guest );
		SetSwimHeading( guest );
		(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (fromX, fromY, fromDistance, TickCount);
		if ( moved )
		{
			var dx = guest.X - fromX;
			var dy = guest.Y - fromY;
			guest.DistanceWalked += MathF.Sqrt( dx * dx + dy * dy );
		}
	}
}

// [EXT:pools-activities] The host of pool activities (docs/POOLS.md, "Activities"): the registry, the offer cadence and the state-25 turn. An empty registry, or a swimmer that is offered nothing, draws nothing and hashes nothing.
public sealed partial class GuestSimulation
{
	private readonly List<IPoolActivity> poolActivities = new();
	private readonly List<(IPoolActivity Activity, PoolActivityOffer Offer)> poolOffers = new();

	/// <summary>The registered pool activities in id order.</summary>
	public IReadOnlyList<IPoolActivity> PoolActivities => poolActivities;

	/// <summary>Registers an activity (replacing one with the same id), keeping the registry in id order so offers are evaluated the same way in every run.</summary>
	public void RegisterPoolActivity( IPoolActivity activity )
	{
		poolActivities.RemoveAll( other => other.Id == activity.Id );
		poolActivities.Add( activity );
		poolActivities.Sort( ( a, b ) => string.CompareOrdinal( a.Id, b.Id ) );
	}

	/// <summary>Empties the registry (guests still in an activity then leave the water on their next turn).</summary>
	internal void ClearPoolActivities() => poolActivities.Clear();

	/// <summary>The placed pool extras (<see cref="PoolExtras"/>), supplied by the level; null in a simulation without objects.</summary>
	public Func<IReadOnlyList<PoolExtraSite>>? PoolExtraSites { get; set; }

	/// <summary>The changing room a guest in a swim session started at, or null.</summary>
	internal IRideVisitorBridge? SwimRoomOf( Guest guest ) => FindAttraction( guest.AttractionId );

	/// <summary>The registered activity with this id, or null.</summary>
	public IPoolActivity? PoolActivityById( string? id ) => id == null ? null : poolActivities.FirstOrDefault( activity => activity.Id == id );

	/// <summary>The activity a guest is doing, or null.</summary>
	public IPoolActivity? PoolActivityOf( Guest guest ) => guest.State == GuestState.PoolActivity ? PoolActivityById( guest.PoolActivityId ) : null;

	/// <summary>
	/// Sets the guest at a point of the water or of the pool side (16.16 cells) at once, as a pool activity needs when the guest steps onto a ladder or lands in the water.
	/// The guest is drawn there from this turn on, not interpolated from where it was.
	/// </summary>
	public void PlacePoolGuest( Guest guest, int x, int y )
	{
		var navigation = guest.Navigation;
		(navigation.X, navigation.Y) = (x, y);
		navigation.SetDestination( x, y, [] );
		SetSwimCell( guest );
		(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (guest.X, guest.Y, guest.DistanceWalked, TickCount);
		guest.IsMoving = false;
		UpdatePresentation( guest );
	}

	/// <summary>Moves the guest to a point (16.16 cells) as one step of this turn: it is drawn walking from where it was and faces the way it went.</summary>
	public void MovePoolGuest( Guest guest, int x, int y )
	{
		var navigation = guest.Navigation;
		var (fromX, fromY, fromDistance) = (guest.X, guest.Y, guest.DistanceWalked);
		(navigation.X, navigation.Y) = (x, y);
		SetSwimCell( guest );
		(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (fromX, fromY, fromDistance, TickCount);
		var dx = guest.X - fromX;
		var dy = guest.Y - fromY;
		var length = MathF.Sqrt( dx * dx + dy * dy );
		guest.IsMoving = length > 0;
		if ( length > 0 )
		{
			guest.HeadingX = dx / length;
			guest.HeadingY = dy / length;
			guest.DistanceWalked += length;
		}
	}

	/// <summary>
	/// [EXT:debug-pool] Smoke-only (<c>--debug-extras</c>) and tests: puts a swimmer into the activity at once if the activity has an offer for it (so a capture shows the activity without waiting for the cadence).
	/// </summary>
	internal bool DebugStartActivity( Guest guest, string id )
	{
		if ( !guest.IsSwimming || PoolActivityById( id ) is not { } activity || SwimRoomOf( guest ) is not { } room || SwimRegionOf( room ) is not { } region || activity.Offer( this, guest, region ) is not { } offer )
			return false;
		var stream = guest.SwimRandom;
		var started = BeginPoolActivity( guest, activity, offer, ref stream );
		guest.SwimRandom = stream;
		return started;
	}

	/// <summary>
	/// The engine height of a swimmer's feet: <see cref="PoolFeetHeights.Default"/> (the water surface on a pool cell, else the terrain) unless the guest's activity says otherwise
	/// (<see cref="IPoolActivity.FeetHeight"/>). Presentation only.
	/// </summary>
	public float PoolFeetHeight( Guest guest, PoolFeetHeights heights ) =>
		guest.State == GuestState.PoolActivity && PoolActivityOf( guest )?.FeetHeight( this, guest, heights ) is { } height ? height : heights.Default;

	/// <summary>Aborts the activity of every guest doing one for this target (the thing id of the object that provides it, which was erased or demolished; 0 aborts nothing); each ends its session at once, as a changing room's swimmers do.</summary>
	public void AbortPoolActivitiesOf( int targetId )
	{
		if ( targetId == 0 )
			return;
		foreach ( var guest in guests )
			if ( guest.State == GuestState.PoolActivity && guest.PoolActivityTarget == targetId )
				EndSwimAtOnce( guest, FindAttraction( guest.AttractionId ) );
	}

	/// <summary>
	/// Starts the activity for the guest, already in the swim session. The swimmer's own stream (<c>stream</c>) is passed to the activity. False when it declines: the guest is back as it was.
	/// </summary>
	private bool BeginPoolActivity( Guest guest, IPoolActivity activity, PoolActivityOffer offer, ref uint stream )
	{
		var previous = guest.State;
		guest.State = GuestState.PoolActivity;
		guest.PoolActivityId = activity.Id;
		guest.PoolActivityTarget = offer.TargetId;
		guest.PoolActivityPhase = 0;
		guest.PoolActivityTurns = 0;
		(guest.PoolActivityX, guest.PoolActivityY) = PoolSwimRegion.Centre( (offer.X, offer.Y) );
		if ( activity.Start( this, guest, offer, ref stream ) )
		{
			poolActivityVersion++;
			return true;
		}
		guest.State = previous;
		ClearPoolActivity( guest );
		return false;
	}

	/// <summary>Counts every change of who does which activity at which target and seat (a start, an end, a restore); <see cref="PoolOccupancy"/> is rebuilt only when it moved.</summary>
	private int poolActivityVersion;
	private int poolOccupancyVersion = -1;
	private readonly HashSet<(string Id, int Target)> poolTargetsTaken = new();
	private readonly HashSet<(string Id, int Target, int X, int Y)> poolSeatsTaken = new();

	/// <summary>
	/// Whether a guest does the activity at the target (when <paramref name="seat"/> is given, on that seat cell). Exact at every moment: it is rebuilt from the guests whenever one starts or ends an activity.
	/// A seat is the cell of <see cref="Guest.PoolActivityX"/>, <see cref="Guest.PoolActivityY"/> as <see cref="IPoolActivity.Start"/> left it: an activity that books seats keeps that point for the whole activity.
	/// </summary>
	public bool IsPoolActivityTaken( string id, int target, (int X, int Y)? seat = null )
	{
		if ( poolOccupancyVersion != poolActivityVersion )
		{
			poolOccupancyVersion = poolActivityVersion;
			poolTargetsTaken.Clear();
			poolSeatsTaken.Clear();
			foreach ( var guest in guests )
				if ( guest.State == GuestState.PoolActivity && guest.PoolActivityId is { } activity )
				{
					poolTargetsTaken.Add( (activity, guest.PoolActivityTarget) );
					poolSeatsTaken.Add( (activity, guest.PoolActivityTarget, guest.PoolActivityX >> 16, guest.PoolActivityY >> 16) );
				}
		}
		return seat is { } cell ? poolSeatsTaken.Contains( (id, target, cell.X, cell.Y) ) : poolTargetsTaken.Contains( (id, target) );
	}

	private void ClearPoolActivity( Guest guest )
	{
		poolActivityVersion++;
		guest.PoolActivityId = null;
		guest.PoolActivityTarget = guest.PoolActivityPhase = guest.PoolActivityTurns = 0;
		guest.PoolActivityX = guest.PoolActivityY = 0;
	}

	/// <summary>The activities with a start chance (a shower) get their draw as the swim starts, in registry order; the first taken ends the offers. No draw when none has an offer.</summary>
	private void OfferActivitiesAtStart( Guest guest, PoolSwimRegion region )
	{
		var stream = guest.SwimRandom;
		foreach ( var activity in poolActivities )
		{
			if ( activity.StartOfferPercent <= 0 || activity.Offer( this, guest, region ) is not { } offer )
				continue;
			var taken = PoolSwimming.Next( ref stream ) % 100 < activity.StartOfferPercent;
			if ( taken && BeginPoolActivity( guest, activity, offer, ref stream ) )
				break;
		}
		guest.SwimRandom = stream;
	}

	/// <summary>
	/// The offer cadence of a swimming guest: while some activity offers something, one draw of the swimmer's own stream each turn decides whether it takes an offer
	/// (on average one every <see cref="PoolActivitySettings.OfferIntervalTurns"/> turns), and a second draw picks which. True when an activity was started.
	/// </summary>
	private bool TryOfferActivity( Guest guest, PoolSwimRegion region )
	{
		if ( poolActivities.Count == 0 || guest.SwimTurns < PoolActivitySettings.OfferMinimumTurns )
			return false;
		// first a cheap look whether any offer exists (stopping at the first); only then the take-draw, and only when it is taken the whole ordered list for the pick-draw
		var any = false;
		foreach ( var activity in poolActivities )
			if ( activity.StartOfferPercent <= 0 && activity.Offer( this, guest, region ) != null )
			{
				any = true;
				break;
			}
		if ( !any )
			return false;
		var stream = guest.SwimRandom;
		var started = false;
		if ( PoolSwimming.Next( ref stream ) % PoolActivitySettings.OfferIntervalTurns == 0 )
		{
			poolOffers.Clear();
			foreach ( var activity in poolActivities )
				if ( activity.StartOfferPercent <= 0 && activity.Offer( this, guest, region ) is { } offer )
					poolOffers.Add( (activity, offer) );
			var (picked, pickedOffer) = poolOffers[(int)(PoolSwimming.Next( ref stream ) % (uint)poolOffers.Count)];
			started = BeginPoolActivity( guest, picked, pickedOffer, ref stream );
		}
		guest.SwimRandom = stream;
		return started;
	}

	/// <summary>State 25: one turn of the guest's activity; it goes on, puts the guest back in the water, or ends the session.</summary>
	private void UpdatePoolActivity( Guest guest )
	{
		var ride = FindAttraction( guest.AttractionId );
		var activity = PoolActivityById( guest.PoolActivityId );
		if ( ride == null || activity == null || SwimRegionOf( ride ) is not { } region )
		{
			EndSwimAtOnce( guest, ride );
			return;
		}
		// a closed park lets go of the activity and sends the guest out like a swimmer: back in the water, to the steps, then changing back
		if ( Payments?.ParkClosed == true )
		{
			activity.Abort( this, guest );
			PutInWater( guest, region );
			ClearPoolActivity( guest );
			BeginClimbOut( guest, region );
			return;
		}
		if ( !turnStarted )
			return;
		var stream = guest.SwimRandom;
		var step = activity.Update( this, guest, ref stream );
		guest.SwimRandom = stream;
		switch ( step )
		{
			case PoolActivityStep.Continue:
				return;
			case PoolActivityStep.BackInWater:
				PutInWater( guest, region );
				ClearPoolActivity( guest );
				guest.SwimHasGoal = false;
				guest.State = GuestState.Swimming;
				return;
			default:
				PutInWater( guest, region );
				ClearPoolActivity( guest );
				BeginClimbOut( guest, region );
				return;
		}
	}

	private void BeginClimbOut( Guest guest, PoolSwimRegion region )
	{
		guest.State = GuestState.ClimbingOut;
		guest.SwimTurns = ClimbOutLimitTurns;
		(guest.SwimGoalX, guest.SwimGoalY) = PoolSwimRegion.Centre( region.Access );
		guest.SwimHasGoal = true;
	}

	/// <summary>
	/// An activity must leave the guest in the water; a guest it left elsewhere is set at the middle of its own cell when that is water of the region (as a swimmer outside the water is put back), else at the
	/// pool-side cell, rather than swim on dry land.
	/// </summary>
	private void PutInWater( Guest guest, PoolSwimRegion region )
	{
		if ( region.IsWater( guest.Navigation.X, guest.Navigation.Y ) )
			return;
		var own = PoolSwimRegion.Centre( guest.Navigation.Cell );
		var (x, y) = region.Contains( guest.Navigation.Cell.X, guest.Navigation.Cell.Y ) && region.IsWater( own.X, own.Y ) ? own : PoolSwimRegion.Centre( region.Access );
		PlacePoolGuest( guest, x, y );
	}
}
