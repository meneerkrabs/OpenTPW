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

	/// <summary>[EXT:pools-swim] How many swimmers (swimming or climbing out) are in the water of <paramref name="region"/>, whichever changing room they came from.</summary>
	private int SwimmerCountIn( PoolSwimRegion region )
	{
		var count = 0;
		foreach ( var guest in guests )
			if ( guest.IsSwimming && region.Contains( guest.CellX, guest.CellY ) )
				count++;
		return count;
	}

	/// <summary>[EXT:pools-swim] Whether a swimmer's position lies in the cell (the pool tool does not erase water under swimmers).</summary>
	public bool IsSwimmerIn( int x, int y ) => guests.Any( guest => guest.IsSwimming && guest.CellX == x && guest.CellY == y );

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
		SwimStep( guest, region, ignoreOthers: false );
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
		ClearSwim( guest );
		guest.IsMoving = false;
		// leave the swim state first: the cell lists (Occupy) leave a swimmer out, and the guest is a walker again from the cell it is placed on
		guest.State = GuestState.DoingNothing;
		ReturnToPath( guest, ride?.ExitCell ?? (guest.CellX, guest.CellY) );
		// a guest with balloon life gets its balloon back on stepping out, as it does when it leaves a ride (the swim put it away, like a ride does)
		if ( guest.BalloonLife > 0 )
			guest.HasBalloon = true;
	}

	/// <summary>One steering step of a swimmer; the others are the swimmers of the same changing room.</summary>
	private void SwimStep( Guest guest, PoolSwimRegion region, bool ignoreOthers )
	{
		var navigation = guest.Navigation;
		// the water may have changed since the last turn (a cell erased beside the swimmer): a swimmer outside it is put back at the middle of its cell, or at the steps
		if ( !region.IsWater( navigation.X, navigation.Y ) )
		{
			var cell = region.Contains( navigation.X >> 16, navigation.Y >> 16 ) ? (navigation.X >> 16, navigation.Y >> 16) : region.Access;
			(navigation.X, navigation.Y) = PoolSwimRegion.Centre( cell );
		}
		swimOthers.Clear();
		if ( !ignoreOthers )
			foreach ( var other in guests )
				// every swimmer in the same water, whichever changing room it came from (two rooms may share one pool)
				if ( other != guest && other.IsSwimming && region.Contains( other.CellX, other.CellY ) )
					swimOthers.Add( (other.Navigation.X, other.Navigation.Y) );
		var body = new PoolSwimBody
		{
			X = navigation.X, Y = navigation.Y, Heading = guest.SwimHeading, GoalX = guest.SwimGoalX, GoalY = guest.SwimGoalY, HasGoal = guest.SwimHasGoal, Random = guest.SwimRandom
		};
		var (fromX, fromY, fromDistance) = (guest.X, guest.Y, guest.DistanceWalked);
		var moved = PoolSwimming.Step( region, ref body, System.Runtime.InteropServices.CollectionsMarshal.AsSpan( swimOthers ), guest.BaseSpeed * PoolSwimSettings.SpeedPerBaseSpeed, ignoreOthers );
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
