namespace OpenTPW;

/// <summary>Deterministic SplitMix64 stream; independent of System.Random's implementation.</summary>
// [APPROX:DET-014] guests draw from their own SplitMix64 stream seeded from the world seed, not from the original's shared world LCG reseeded to each new guest's id — evidence needed: DET-I2 port of WorldRng (docs/reverse/DET-plan.md §2.3, §4.3)
public struct GuestRandom
{
	public ulong State;

	public GuestRandom( ulong seed ) => State = seed;

	public ulong NextULong()
	{
		var z = State += 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return z ^ (z >> 31);
	}

	/// <summary>Uniform integer in [0, maximumExclusive).</summary>
	public int Next( int maximumExclusive ) => maximumExclusive <= 0 ? 0 : (int)(NextULong() % (ulong)maximumExclusive);
	public int Next( int minimum, int maximumInclusive ) => minimum + Next( maximumInclusive - minimum + 1 );
	public float NextFloat() => (NextULong() >> 40) / (float)(1UL << 24);
}

/// <summary>Counts for overlays, logs and tests.</summary>
public readonly record struct GuestStatistics( int InPark, int Walking, int Queueing, int OnRides, int Arriving, int Leaving, float AverageHappiness, GuestThought CommonThought, long Admissions );

/// <summary>
/// Park visitors on the fixed simulation tick: arrival from the bus stops through the ticket booth
/// (.sam FixedItemInfo cells), path walking on <see cref="GuestPathGrid"/> flow fields, needs, attraction
/// choice with the .sam decision weights, queues and rides through <see cref="IRideVisitorBridge"/>, and
/// leaving. All randomness comes from one seeded <see cref="GuestRandom"/>; guests and attractions are
/// updated in id order, so the same seed and inputs give the same <see cref="ComputeStateHash"/>.
/// Rates and thresholds without an original source are labelled approximations in
/// <see cref="GuestSettings"/> and docs/GUESTS.md.
/// </summary>
public sealed class GuestSimulation : IRideVisitorHost
{
	public const float QueueSlotSpacing = 0.28f;
	public const float TicketBoothSeconds = 1.0f;
	public const float ExitRideSeconds = 1.0f;
	public const float WaitForBusSeconds = 3.0f;
	public const float SpawnSpacingSeconds = 0.4f;

	private readonly List<Guest> guests = new();
	private readonly Dictionary<int, Guest> byId = new();
	private readonly List<IRideVisitorBridge> attractions = new();
	private GuestRandom random;
	private int nextId = 1;
	private float arrivalTimer;
	private float spawnTimer;
	private int pendingArrivals;
	private int laneToggle;

	public GuestSimulation( GuestPathGrid grid, GuestSettings settings, ulong seed )
	{
		Grid = grid;
		Settings = settings;
		Seed = seed;
		random = new GuestRandom( seed );
	}

	public GuestPathGrid Grid { get; }
	public GuestSettings Settings { get; }
	public ulong Seed { get; }
	/// <summary>Current <see cref="GuestRandom"/> state (saved with the park; see <see cref="WorldRandomState"/>).</summary>
	public ulong RandomState => random.State;
	public IReadOnlyList<Guest> Guests => guests;
	public IReadOnlyList<IRideVisitorBridge> Attractions => attractions;
	public long TickCount { get; private set; }
	public double TimeSeconds { get; private set; }
	public bool ArrivalsEnabled { get; set; } = true;
	/// <summary>Where guests pay (the park economy). Null: the standalone <see cref="GuestSettings.AdmissionFee"/> and attraction prices apply and no money is booked anywhere.</summary>
	public IGuestPayments? Payments { get; set; }
	/// <summary>Entrance fee: the economy's when <see cref="Payments"/> is set, else <see cref="GuestSettings.AdmissionFee"/>.</summary>
	public int AdmissionFee => Payments?.AdmissionFee ?? Settings.AdmissionFee;
	/// <summary>Number of guests admitted (a visitor count; money is booked by <see cref="Payments"/>).</summary>
	public long Admissions { get; private set; }
	public int Departed { get; private set; }
	/// <summary>Raised when a guest pays (guest, amount, attraction id or 0 for admission).</summary>
	public event Action<Guest, int, int>? MoneySpent;

	/// <summary>Continues the guest stream from a saved state.</summary>
	public void RestoreRandomState( ulong state ) => random = new GuestRandom( state );

	public Guest? Find( int id ) => byId.TryGetValue( id, out var guest ) ? guest : null;

	public void Register( IRideVisitorBridge attraction )
	{
		if ( attractions.Contains( attraction ) )
			return;
		attraction.Host = this;
		attractions.Add( attraction );
		attractions.Sort( ( a, b ) => a.AttractionId.CompareTo( b.AttractionId ) );
	}

	public void Unregister( IRideVisitorBridge attraction )
	{
		if ( !attractions.Remove( attraction ) )
			return;
		foreach ( var guest in guests )
		{
			if ( guest.AttractionId == attraction.AttractionId && guest.State is GuestState.GoingToRide or GuestState.Queueing or GuestState.WaitingToBoard or GuestState.Using )
			{
				attraction.LeaveQueue( guest.Id );
				ReturnToPath( guest, guest.State == GuestState.Using ? attraction.ExitCell : (guest.CellX, guest.CellY) );
			}
		}
		if ( attraction.Host == this )
			attraction.Host = null;
	}

	private Guest Create()
	{
		var type = random.Next( Math.Max( 1, Settings.Types.Count ) );
		var guest = new Guest( nextId++, type );
		var peep = Settings.Types[Math.Min( type, Settings.Types.Count - 1 )];
		var variance = peep.StartingCash * Settings.StartingCashVarPercent / 100;
		guest.Money = Math.Max( 0, peep.StartingCash + random.Next( -variance, variance ) );
		guest.Happiness = Settings.StartingHappiness;
		guest.ExitLevel = Settings.ExitLevelSeconds + random.Next( -Settings.ExitLevelVarSeconds, Settings.ExitLevelVarSeconds );
		guest.OffsetX = (random.NextFloat() - 0.5f) * 0.5f;
		guest.OffsetY = (random.NextFloat() - 0.5f) * 0.5f;
		guest.DecisionTimer = random.NextFloat() * Settings.DecisionIntervalSeconds;
		guests.Add( guest );
		byId.Add( guest.Id, guest );
		return guest;
	}

	/// <summary>Spawns a guest at a bus stop; it crosses the road, pays at the ticket booth and enters.</summary>
	public Guest? SpawnArrival()
	{
		var lane = PickLane( laneToggle++ );
		if ( lane == null )
		{
			var start = FindSpawnCell();
			return start == null ? null : SpawnInPark( start.Value.X, start.Value.Y );
		}
		var guest = Create();
		guest.Lane = lane;
		guest.LaneIndex = 1;
		guest.LaneReversed = false;
		guest.X = lane[0].X + 0.5f + guest.OffsetX;
		guest.Y = lane[0].Y + 0.5f + guest.OffsetY;
		guest.State = GuestState.CrossingRoad;
		SetLaneWaypoint( guest );
		return guest;
	}

	/// <summary>Spawns a guest already inside the park on a walkable cell (tests, smoke, imported parks).</summary>
	public Guest SpawnInPark( int x, int y )
	{
		if ( !Grid.IsWalkable( x, y ) )
			throw new ArgumentException( $"Cell ({x}, {y}) is not a walkable path cell." );
		var guest = Create();
		PlaceOnGrid( guest, x, y );
		guest.State = GuestState.WalkingAround;
		guest.DecisionTimer = 0;
		return guest;
	}

	private (int X, int Y)[]? PickLane( int index )
	{
		var a = ValidLane( Settings.ArrivalLaneA );
		var b = ValidLane( Settings.ArrivalLaneB );
		if ( a != null && b != null )
			return (index & 1) == 0 ? a : b;
		return a ?? b;
	}

	private (int X, int Y)[]? ValidLane( (int X, int Y)[]? lane ) =>
		lane != null && lane.Length >= 2 && Grid.IsWalkable( lane[^1].X, lane[^1].Y ) ? lane : null;

	private (int X, int Y)? FindSpawnCell()
	{
		for ( var y = 0; y < Grid.CountY; y++ )
			for ( var x = 0; x < Grid.CountX; x++ )
				if ( Grid.IsWalkable( x, y ) )
					return (x, y);
		return null;
	}

	private void PlaceOnGrid( Guest guest, int x, int y )
	{
		guest.OnGrid = true;
		guest.Lane = null;
		guest.CellX = x;
		guest.CellY = y;
		guest.PreviousCellX = -1;
		guest.PreviousCellY = -1;
		guest.X = x + 0.5f + guest.OffsetX;
		guest.Y = y + 0.5f + guest.OffsetY;
		guest.HasWaypoint = false;
	}

	public void Tick( float deltaSeconds )
	{
		if ( !float.IsFinite( deltaSeconds ) || deltaSeconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( deltaSeconds ) );
		TickCount++;
		TimeSeconds += deltaSeconds;
		UpdateArrivals( deltaSeconds );
		for ( var index = 0; index < guests.Count; index++ )
			UpdateGuest( guests[index], deltaSeconds );
		for ( var index = guests.Count - 1; index >= 0; index-- )
		{
			if ( guests[index].State != GuestState.Gone )
				continue;
			byId.Remove( guests[index].Id );
			guests.RemoveAt( index );
			Departed++;
		}
	}

	private void UpdateArrivals( float deltaSeconds )
	{
		if ( !ArrivalsEnabled )
			return;
		arrivalTimer -= deltaSeconds;
		if ( arrivalTimer <= 0 )
		{
			arrivalTimer += Settings.ArrivalTimeBetween * Settings.ArrivalTimeUnitSeconds;
			pendingArrivals += Math.Max( Settings.ArrivalMinPeople, Settings.ArrivalFixedRate );
		}
		spawnTimer -= deltaSeconds;
		if ( pendingArrivals > 0 && spawnTimer <= 0 )
		{
			spawnTimer = SpawnSpacingSeconds;
			pendingArrivals--;
			if ( guests.Count < Settings.MaximumGuests )
				SpawnArrival();
		}
	}

	public bool IsInPark( Guest guest ) => InPark( guest );

	private bool InPark( Guest guest ) => guest.State is not (GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth
		or GuestState.AtTicketBooth or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.Gone);

	private void UpdateGuest( Guest guest, float dt )
	{
		guest.IsMoving = false;
		if ( InPark( guest ) )
			UpdateNeeds( guest, dt );
		switch ( guest.State )
		{
			case GuestState.CrossingRoad:
			case GuestState.CrossingRoadToPark:
			case GuestState.GoingToTicketBooth:
			case GuestState.EnteringPark:
			case GuestState.CrossingRoadHome:
				UpdateLane( guest, dt );
				break;
			case GuestState.LeavingPark:
				if ( guest.Lane != null )
					UpdateLane( guest, dt );
				else
					UpdateLeaving( guest, dt );
				break;
			case GuestState.AtTicketBooth:
				guest.StateTimer -= dt;
				if ( guest.StateTimer <= 0 )
					PayAdmission( guest );
				break;
			case GuestState.WaitingToGoHome:
				guest.StateTimer -= dt;
				if ( guest.StateTimer <= 0 )
					guest.State = GuestState.Gone;
				break;
			case GuestState.WalkingAround:
			case GuestState.DoingNothing:
				UpdateWalking( guest, dt );
				break;
			case GuestState.GoingToRide:
				UpdateGoingToRide( guest, dt );
				break;
			case GuestState.Queueing:
			case GuestState.WaitingToBoard:
			case GuestState.Boarding:
				UpdateQueueing( guest, dt );
				break;
			case GuestState.ExitingRide:
				guest.StateTimer -= dt;
				if ( guest.StateTimer <= 0 )
				{
					guest.State = GuestState.WalkingAround;
					guest.DecisionTimer = Settings.DecisionIntervalSeconds;
				}
				break;
		}
	}

	private void UpdateNeeds( Guest guest, float dt )
	{
		var riding = guest.State == GuestState.Using;
		guest.Hunger = Math.Min( 150, guest.Hunger + Settings.HungerPerSecond * dt );
		guest.Thirst = Math.Min( 150, guest.Thirst + Settings.ThirstPerSecond * dt );
		guest.Toilet = Math.Min( 150, guest.Toilet + Settings.ToiletPerSecond * dt );
		guest.Nausea = Math.Max( 0, guest.Nausea - Settings.NauseaDecayPerSecond * dt );
		if ( riding || guest.State is GuestState.Queueing or GuestState.WaitingToBoard or GuestState.DoingNothing )
			guest.Energy = Math.Min( 100, guest.Energy + Settings.EnergyRecoveryPerSecond * dt );
		else
			guest.Energy = Math.Max( 0, guest.Energy - Settings.EnergyDrainPerSecond * dt );
		if ( !riding )
		{
			guest.SecondsSinceRide += dt;
			if ( guest.State != GuestState.LeavingPark )
				guest.ExitLevel -= dt;
		}
	}

	private float Speed( Guest guest ) => Settings.WalkSpeedCellsPerSecond * (guest.Energy < 20 ? 0.7f : 1f);

	/// <summary>Moves towards the waypoint; true when reached.</summary>
	private bool Move( Guest guest, float dt )
	{
		var dx = guest.WaypointX - guest.X;
		var dy = guest.WaypointY - guest.Y;
		var distance = MathF.Sqrt( dx * dx + dy * dy );
		var step = Speed( guest ) * dt;
		if ( distance <= step || distance < 1e-4f )
		{
			guest.X = guest.WaypointX;
			guest.Y = guest.WaypointY;
			guest.DistanceWalked += distance;
			guest.IsMoving = distance > 1e-4f;
			guest.HasWaypoint = false;
			return true;
		}
		guest.HeadingX = dx / distance;
		guest.HeadingY = dy / distance;
		guest.X += guest.HeadingX * step;
		guest.Y += guest.HeadingY * step;
		guest.DistanceWalked += step;
		guest.IsMoving = true;
		return false;
	}

	private void SetCellWaypoint( Guest guest, int x, int y )
	{
		guest.PreviousCellX = guest.CellX;
		guest.PreviousCellY = guest.CellY;
		guest.CellX = x;
		guest.CellY = y;
		guest.WaypointX = x + 0.5f + guest.OffsetX;
		guest.WaypointY = y + 0.5f + guest.OffsetY;
		guest.HasWaypoint = true;
	}

	private void SetLaneWaypoint( Guest guest )
	{
		var cell = guest.Lane![guest.LaneIndex];
		guest.WaypointX = cell.X + 0.5f + guest.OffsetX;
		guest.WaypointY = cell.Y + 0.5f + guest.OffsetY;
		guest.HasWaypoint = true;
	}

	// Arrival lane: [bus stop, crossing (bus side), crossing (park side), ticket booth, entrance].
	private void UpdateLane( Guest guest, float dt )
	{
		if ( !Move( guest, dt ) )
			return;
		var lane = guest.Lane!;
		if ( !guest.LaneReversed )
		{
			if ( guest.LaneIndex == lane.Length - 1 )
			{
				PlaceOnGrid( guest, lane[^1].X, lane[^1].Y );
				guest.State = GuestState.WalkingAround;
				guest.DecisionTimer = 0;
				return;
			}
			if ( guest.LaneIndex == lane.Length - 2 && guest.State != GuestState.EnteringPark )
			{
				guest.State = GuestState.AtTicketBooth;
				guest.StateTimer = TicketBoothSeconds;
				return;
			}
			guest.LaneIndex++;
			guest.State = guest.LaneIndex switch
			{
				1 => GuestState.CrossingRoad,
				2 => GuestState.CrossingRoadToPark,
				_ when guest.LaneIndex == lane.Length - 1 => GuestState.EnteringPark,
				_ => GuestState.GoingToTicketBooth
			};
			SetLaneWaypoint( guest );
			return;
		}
		if ( guest.LaneIndex == 0 )
		{
			guest.State = GuestState.WaitingToGoHome;
			guest.StateTimer = WaitForBusSeconds;
			return;
		}
		guest.LaneIndex--;
		guest.State = guest.LaneIndex <= 1 ? GuestState.CrossingRoadHome : GuestState.LeavingPark;
		SetLaneWaypoint( guest );
	}

	private void PayAdmission( Guest guest )
	{
		int fee;
		if ( Payments != null ? !Payments.TryPayAdmission( guest.Money, out fee ) : guest.Money < (fee = AdmissionFee) )
		{
			// Closed park or too expensive: the guest turns back at the booth.
			guest.Thought = GuestThought.Angry;
			StartLeavingLane( guest, guest.Lane!, guest.LaneIndex );
			return;
		}
		guest.Money -= fee;
		Admissions++;
		if ( fee > 0 )
			MoneySpent?.Invoke( guest, fee, 0 );
		guest.LaneIndex = guest.Lane!.Length - 1;
		guest.State = GuestState.EnteringPark;
		SetLaneWaypoint( guest );
	}

	private void StartLeavingLane( Guest guest, (int X, int Y)[] lane, int fromIndex )
	{
		guest.OnGrid = false;
		guest.Lane = lane;
		guest.LaneReversed = true;
		guest.LaneIndex = Math.Max( 0, fromIndex - 1 );
		guest.State = guest.LaneIndex <= 1 ? GuestState.CrossingRoadHome : GuestState.LeavingPark;
		SetLaneWaypoint( guest );
	}

	private void UpdateWalking( Guest guest, float dt )
	{
		guest.DecisionTimer -= dt;
		if ( guest.HasWaypoint && !Move( guest, dt ) )
			return;
		if ( guest.DecisionTimer <= 0 )
		{
			guest.DecisionTimer = Settings.DecisionIntervalSeconds;
			UpdateMood( guest );
			if ( ShouldLeave( guest ) )
			{
				guest.State = GuestState.LeavingPark;
				return;
			}
			var choice = ChooseAttraction( guest );
			if ( choice != null )
			{
				guest.AttractionId = choice.AttractionId;
				guest.State = GuestState.GoingToRide;
				return;
			}
		}
		Wander( guest );
	}

	private void Wander( Guest guest )
	{
		var count = Grid.NeighbourCount( guest.CellX, guest.CellY );
		if ( count == 0 )
		{
			guest.State = GuestState.DoingNothing;
			return;
		}
		guest.State = GuestState.WalkingAround;
		Span<int> options = stackalloc int[4];
		var optionCount = 0;
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( !Grid.AreConnected( guest.CellX, guest.CellY, direction ) )
				continue;
			var nx = guest.CellX + GuestPathGrid.Directions[direction].DX;
			var ny = guest.CellY + GuestPathGrid.Directions[direction].DY;
			if ( count > 1 && nx == guest.PreviousCellX && ny == guest.PreviousCellY )
				continue;
			options[optionCount++] = direction;
		}
		var chosen = options[random.Next( optionCount )];
		SetCellWaypoint( guest, guest.CellX + GuestPathGrid.Directions[chosen].DX, guest.CellY + GuestPathGrid.Directions[chosen].DY );
	}

	private void UpdateMood( Guest guest )
	{
		var small = Settings.SmallHappinessChange;
		if ( guest.Hunger >= 100 || guest.Thirst >= 100 )
			guest.Happiness -= small;
		if ( guest.Toilet >= Settings.ToiletDesperate )
			guest.Happiness -= small;
		var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
		var bored = guest.SecondsSinceRide > type.BoredomThreshold;
		if ( bored )
			guest.Happiness -= small;
		guest.Happiness = Math.Clamp( guest.Happiness, 0, 100 );

		var hungry = guest.Hunger >= Settings.NeedThoughtLevel;
		var thirsty = guest.Thirst >= Settings.NeedThoughtLevel;
		guest.Thought = guest.Happiness <= 10 ? GuestThought.Angry
			: guest.Toilet >= Settings.ToiletDesperate * Settings.NeedThoughtLevel / 100 ? GuestThought.NeedToilet
			: hungry && thirsty ? GuestThought.HungryAndThirsty
			: hungry ? GuestThought.Hungry
			: thirsty ? GuestThought.Thirsty
			: bored ? GuestThought.Bored
			: guest.Happiness >= 90 ? GuestThought.SuperHappy
			: guest.Happiness >= 70 ? GuestThought.Happy
			: guest.Happiness >= 30 ? GuestThought.Okay
			: GuestThought.Unhappy;
	}

	private bool ShouldLeave( Guest guest ) => guest.ExitLevel <= 0 || guest.Happiness <= 10;

	/// <summary>
	/// Attraction score with the .sam PeepInfo.DecisionVar* weights. How the original combines them is unknown;
	/// the terms and scales below are an approximation (docs/GUESTS.md).
	/// </summary>
	public int ScoreAttraction( Guest guest, IRideVisitorBridge attraction, int distance )
	{
		var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
		var score = attraction.AttractionValue
			- Settings.DecisionDistanceWeight * distance
			- Settings.DecisionQueueWeight * attraction.QueueLength;
		if ( attraction.Kind == RideVisitorKind.Ride || attraction.Kind == RideVisitorKind.Sideshow )
			score += Settings.DecisionExcitementWeight * (25 - Math.Abs( type.PreferredExcitement - attraction.ExcitementLevel ));
		if ( attraction.Satisfies.HasFlag( GuestNeeds.Hunger ) )
			score += (int)(Settings.DecisionHungerWeight * guest.Hunger / 4);
		if ( attraction.Satisfies.HasFlag( GuestNeeds.Thirst ) )
			score += (int)(Settings.DecisionThirstWeight * guest.Thirst / 4);
		if ( attraction.Satisfies.HasFlag( GuestNeeds.Toilet ) )
			score += (int)(Settings.DecisionToiletWeight * guest.Toilet / 4);
		if ( attraction.Kind == RideVisitorKind.Ride )
			score -= (int)(Settings.DecisionIllnessWeight * guest.Nausea / 4);
		if ( attraction.AttractionId == guest.LastAttractionId && guest.SecondsSinceRide < type.BoredomThreshold )
			score -= attraction.AttractionValue;
		if ( attraction.Price > guest.Money )
			score = int.MinValue;
		return score;
	}

	private IRideVisitorBridge? ChooseAttraction( Guest guest )
	{
		IRideVisitorBridge? best = null;
		var bestScore = 0;
		foreach ( var attraction in attractions )
		{
			if ( !attraction.IsOpen )
				continue;
			var distance = Grid.Distance( guest.CellX, guest.CellY, attraction.EntranceCell.X, attraction.EntranceCell.Y );
			if ( distance < 0 )
				continue;
			var score = ScoreAttraction( guest, attraction, distance );
			if ( score == int.MinValue )
				continue;
			score += random.Next( 10 );
			if ( score > bestScore )
			{
				bestScore = score;
				best = attraction;
			}
		}
		return best;
	}

	private IRideVisitorBridge? FindAttraction( int id )
	{
		foreach ( var attraction in attractions )
			if ( attraction.AttractionId == id )
				return attraction;
		return null;
	}

	private void UpdateGoingToRide( Guest guest, float dt )
	{
		if ( guest.HasWaypoint && !Move( guest, dt ) )
			return;
		var attraction = FindAttraction( guest.AttractionId );
		if ( attraction == null || !attraction.IsOpen )
		{
			guest.AttractionId = 0;
			guest.State = GuestState.WalkingAround;
			return;
		}
		var (tx, ty) = attraction.EntranceCell;
		if ( guest.CellX == tx && guest.CellY == ty )
		{
			if ( attraction.TryJoinQueue( guest.Id ) )
			{
				guest.State = GuestState.Queueing;
				return;
			}
			guest.Thought = GuestThought.QueueTooLong;
			guest.Happiness = Math.Max( 0, guest.Happiness - Settings.SmallHappinessChange );
			guest.LastAttractionId = attraction.AttractionId;
			guest.AttractionId = 0;
			guest.State = GuestState.WalkingAround;
			guest.DecisionTimer = Settings.DecisionIntervalSeconds;
			return;
		}
		if ( Grid.TryStep( guest.CellX, guest.CellY, tx, ty, out var nx, out var ny ) )
			SetCellWaypoint( guest, nx, ny );
		else
		{
			guest.Thought = GuestThought.Confused;
			guest.AttractionId = 0;
			guest.State = GuestState.WalkingAround;
		}
	}

	private void UpdateQueueing( Guest guest, float dt )
	{
		var attraction = FindAttraction( guest.AttractionId );
		if ( attraction == null )
		{
			ReturnToPath( guest, (guest.CellX, guest.CellY) );
			return;
		}
		var slot = guest.State == GuestState.Queueing ? attraction.GetQueuePosition( guest.Id ) + 1 : 0;
		var (x, y) = QueueSlot( attraction, Math.Max( 0, slot ) );
		guest.WaypointX = x;
		guest.WaypointY = y;
		guest.HasWaypoint = true;
		Move( guest, dt );
	}

	/// <summary>Queue slot positions: slot 0 at the entrance cell centre, later slots back along the path.</summary>
	public (float X, float Y) QueueSlot( IRideVisitorBridge attraction, int slot )
	{
		var (ex, ey) = attraction.EntranceCell;
		var (bx, by) = (0, 1);
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( Grid.AreConnected( ex, ey, direction ) )
			{
				(bx, by) = GuestPathGrid.Directions[direction];
				break;
			}
		}
		var row = slot / 3;
		var column = slot % 3 - 1;
		return (ex + 0.5f + bx * row * QueueSlotSpacing - by * column * QueueSlotSpacing,
			ey + 0.5f + by * row * QueueSlotSpacing + bx * column * QueueSlotSpacing);
	}

	private void UpdateLeaving( Guest guest, float dt )
	{
		if ( guest.HasWaypoint && !Move( guest, dt ) )
			return;
		var lanes = new[] { ValidLane( Settings.ArrivalLaneA ), ValidLane( Settings.ArrivalLaneB ) };
		(int X, int Y)[]? bestLane = null;
		var bestDistance = int.MaxValue;
		foreach ( var lane in lanes )
		{
			if ( lane == null )
				continue;
			var distance = Grid.Distance( guest.CellX, guest.CellY, lane[^1].X, lane[^1].Y );
			if ( distance >= 0 && distance < bestDistance )
			{
				bestDistance = distance;
				bestLane = lane;
			}
		}
		if ( bestLane == null )
		{
			guest.State = GuestState.Gone; // no way out: the original would eject; documented simplification
			return;
		}
		if ( bestDistance == 0 )
		{
			StartLeavingLane( guest, bestLane, bestLane.Length - 1 );
			return;
		}
		if ( Grid.TryStep( guest.CellX, guest.CellY, bestLane[^1].X, bestLane[^1].Y, out var nx, out var ny ) )
			SetCellWaypoint( guest, nx, ny );
	}

	private void ReturnToPath( Guest guest, (int X, int Y) cell )
	{
		guest.AttractionId = 0;
		if ( !Grid.IsWalkable( cell.X, cell.Y ) )
			cell = Grid.FindNearestWalkable( cell.X, cell.Y ) ?? cell;
		PlaceOnGrid( guest, cell.X, cell.Y );
		guest.State = GuestState.WalkingAround;
		guest.DecisionTimer = Settings.DecisionIntervalSeconds;
	}

	void IRideVisitorHost.OnVisitorOffered( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is { } guest )
			guest.State = GuestState.WaitingToBoard;
	}

	void IRideVisitorHost.OnVisitorBoarded( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		guest.State = GuestState.Using;
		guest.AttractionId = ride.AttractionId;
		guest.HasWaypoint = false;
		var price = 0;
		if ( Payments != null )
			Payments.TryPayVisit( ride, guest.Money, out price );
		else if ( ride.Price > 0 && guest.Money >= ride.Price )
			price = ride.Price;
		if ( price > 0 )
		{
			guest.Money -= price;
			MoneySpent?.Invoke( guest, price, ride.AttractionId );
		}
	}

	void IRideVisitorHost.OnVisitorReleased( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		ReturnToPath( guest, ride.ExitCell );
		guest.State = GuestState.ExitingRide;
		guest.StateTimer = ExitRideSeconds;
		guest.LastAttractionId = ride.AttractionId;
		guest.SecondsSinceRide = 0;
		guest.RidesTaken++;
		ApplyVisit( guest, ride );
	}

	void IRideVisitorHost.OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId )
	{
		if ( Find( guestId ) is not { } guest )
			return;
		guest.Thought = GuestThought.Dissatisfied;
		ReturnToPath( guest, ride.EntranceCell );
	}

	/// <summary>
	/// Ride outcome from the .sam PeepInfo.PerfectRide/GoodRide/OKRide bands (±5/±15/±40 of the preferred
	/// excitement) and RideVomitDivisor/VomitCapacity; shops reset the needs they satisfy.
	/// </summary>
	private void ApplyVisit( Guest guest, IRideVisitorBridge ride )
	{
		if ( ride.Satisfies.HasFlag( GuestNeeds.Hunger ) ) guest.Hunger = 0;
		if ( ride.Satisfies.HasFlag( GuestNeeds.Thirst ) ) guest.Thirst = 0;
		if ( ride.Satisfies.HasFlag( GuestNeeds.Toilet ) ) guest.Toilet = 0;
		if ( ride.Kind is RideVisitorKind.Ride or RideVisitorKind.Sideshow )
		{
			var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
			var difference = Math.Abs( type.PreferredExcitement - ride.ExcitementLevel );
			var change = difference <= 5 ? Settings.PerfectRide
				: difference <= 15 ? Settings.GoodRide
				: difference <= 40 ? Settings.OkRide
				: -Settings.SmallHappinessChange;
			guest.Happiness = Math.Clamp( guest.Happiness + change, 0, 100 );
			guest.ExitLevel += Math.Max( 0, change );
			guest.Thought = change > 0 ? GuestThought.Pleased : GuestThought.Dissatisfied;
			guest.Nausea += ride.ExcitementLevel / (float)Settings.RideVomitDivisor;
			if ( guest.Nausea >= Settings.VomitCapacity )
			{
				guest.Nausea = 0;
				guest.Thought = GuestThought.FeelingSick;
				guest.Happiness = Math.Max( 0, guest.Happiness - Settings.MediumHappinessChange );
			}
		}
	}

	public GuestStatistics GetStatistics()
	{
		int inPark = 0, walking = 0, queueing = 0, onRides = 0, arriving = 0, leaving = 0;
		var happiness = 0f;
		Span<int> thoughts = stackalloc int[18];
		foreach ( var guest in guests )
		{
			if ( InPark( guest ) ) { inPark++; happiness += guest.Happiness; }
			switch ( guest.State )
			{
				case GuestState.WalkingAround or GuestState.GoingToRide or GuestState.DoingNothing or GuestState.ExitingRide: walking++; break;
				case GuestState.Queueing or GuestState.WaitingToBoard or GuestState.Boarding: queueing++; break;
				case GuestState.Using: onRides++; break;
				case GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth or GuestState.AtTicketBooth: arriving++; break;
				case GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome: leaving++; break;
			}
			if ( (int)guest.Thought is > 0 and < 18 )
				thoughts[(int)guest.Thought]++;
		}
		var common = 0;
		for ( var index = 1; index < thoughts.Length; index++ )
			if ( thoughts[index] > thoughts[common] )
				common = index;
		return new GuestStatistics( inPark, walking, queueing, onRides, arriving, leaving, inPark == 0 ? 0 : happiness / inPark, (GuestThought)common, Admissions );
	}

	/// <summary>Thing-table part of <see cref="WorldStateHash"/>: every field the next tick reads, in id order.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( TickCount );
		hash.Add( TimeSeconds );
		hash.Add( nextId );
		hash.Add( Admissions );
		hash.Add( Departed );
		hash.Add( arrivalTimer );
		hash.Add( spawnTimer );
		hash.Add( pendingArrivals );
		hash.Add( laneToggle );
		hash.Add( ArrivalsEnabled );
		hash.Add( guests.Count );
		foreach ( var guest in guests )
		{
			hash.Add( guest.Id );
			hash.Add( guest.Type );
			hash.Add( (int)guest.State );
			hash.Add( guest.X );
			hash.Add( guest.Y );
			hash.Add( guest.HeadingX );
			hash.Add( guest.HeadingY );
			hash.Add( guest.IsMoving );
			hash.Add( guest.DistanceWalked );
			hash.Add( guest.Hunger );
			hash.Add( guest.Thirst );
			hash.Add( guest.Toilet );
			hash.Add( guest.Energy );
			hash.Add( guest.Nausea );
			hash.Add( guest.Happiness );
			hash.Add( guest.Money );
			hash.Add( guest.ExitLevel );
			hash.Add( guest.SecondsSinceRide );
			hash.Add( guest.RidesTaken );
			hash.Add( (int)guest.Thought );
			hash.Add( guest.AttractionId );
			hash.Add( guest.LastAttractionId );
			hash.Add( guest.CellX );
			hash.Add( guest.CellY );
			hash.Add( guest.PreviousCellX );
			hash.Add( guest.PreviousCellY );
			hash.Add( guest.OnGrid );
			hash.Add( guest.OffsetX );
			hash.Add( guest.OffsetY );
			hash.Add( guest.WaypointX );
			hash.Add( guest.WaypointY );
			hash.Add( guest.HasWaypoint );
			hash.Add( guest.Lane?.Length ?? -1 );
			foreach ( var (x, y) in guest.Lane ?? [] )
				hash.Add( x * 4096L + y );
			hash.Add( guest.LaneIndex );
			hash.Add( guest.LaneReversed );
			hash.Add( guest.StateTimer );
			hash.Add( guest.DecisionTimer );
		}
		hash.Add( attractions.Count );
		foreach ( var attraction in attractions )
		{
			hash.Add( attraction.AttractionId );
			hash.Add( attraction.IsOpen );
			hash.Add( attraction.EntranceCell.X * 4096L + attraction.EntranceCell.Y );
			hash.Add( attraction.ExitCell.X * 4096L + attraction.ExitCell.Y );
			if ( attraction is RideVisitorBridge bridge )
				bridge.AddCanonicalState( hash );
			else
				hash.Add( attraction.QueueLength );
		}
	}

	/// <summary>FNV-1a over every guest's simulation state, the RNG and the counters.</summary>
	public ulong ComputeStateHash()
	{
		var hash = 14695981039346656037UL;
		void Add( long value )
		{
			for ( var shift = 0; shift < 64; shift += 8 )
			{
				hash ^= (byte)(value >> shift);
				hash *= 1099511628211UL;
			}
		}
		void AddFloat( float value ) => Add( BitConverter.SingleToInt32Bits( value ) );
		Add( TickCount );
		Add( (long)random.State );
		Add( nextId );
		Add( Admissions );
		Add( guests.Count );
		foreach ( var guest in guests )
		{
			Add( guest.Id );
			Add( guest.Type );
			Add( (int)guest.State );
			AddFloat( guest.X );
			AddFloat( guest.Y );
			AddFloat( guest.Hunger );
			AddFloat( guest.Thirst );
			AddFloat( guest.Toilet );
			AddFloat( guest.Energy );
			AddFloat( guest.Nausea );
			AddFloat( guest.Happiness );
			AddFloat( guest.ExitLevel );
			Add( guest.Money );
			Add( guest.AttractionId );
			Add( (int)guest.Thought );
			Add( guest.CellX * 4096 + guest.CellY );
		}
		return hash;
	}
}
