namespace OpenTPW;

// The host side of the decision layer (docs/SMARTER-GUESTS.md, "A pure decision layer"): the views the simulation hands to its IGuestDecisions and the one hook that is new.
public sealed partial class GuestSimulation
{
	/// <summary>What every decision may read besides its own views: the settings, the park turn and the weather.</summary>
	private DecisionContext ContextNow() => new( Settings, ParkTurn, WeatherDropsFalling?.Invoke() ?? false, SmarterGuests?.ParkSeed ?? Seed, Grid.CountX, Grid.CountY );

	/// <summary>The error for a policy result the host cannot act on: it names the policy, and nothing of the result is used.</summary>
	private InvalidOperationException PolicyError( string what ) => new( $"The guest decision policy {decisions.GetType().Name} {what}." );

	/// <summary>A copy of the fields of <paramref name="guest"/> the decisions read; the histories are views of the guest's arrays, unchanged while a decision runs.</summary>
	internal GuestView ViewOf( Guest guest ) => new( guest.Id, guest.BirthId, guest.Type, guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea, guest.Money,
		guest.CellX, guest.CellY, guest.UsedHistoryView, guest.TemporaryHistoryView,
		// [EXT:smarter-guests-planning] the guest's last meal comes from the extension's state, for a policy that plans
		decisions.UsesPlanning && SmarterGuests is { } smarter ? smarter.Meals.Of( guest.BirthId ) : -1, -1,
		// [EXT:smarter-guests-paths] the guest's last complaint about its path, for a policy that weighs the path's comfort
		decisions.UsesPathComfort && SmarterGuests is { } paths ? paths.Complaints.Of( guest.BirthId ) : -1 );

	/// <summary>The score's eligibility only: preserve connected fallbacks, and opt in to a disconnected back only when it is a real raw queue cell.</summary>
	// [BIN:TPW-EXE:0x004DE130 typed back] the back-of-queue walk starts at the front cell the entry's links name (0x004DE040) whatever the cell holds, so an empty-ground front is a one-cell back that passes 0x004DD920 and is scored; the route request then fails (a mask-0 walk cannot enter a type-0 cell) and the walk is stuck (state 10: "stuck on the way")
	// [BIN:TPW-EXE:0x004FCB84 back cell test] the choice skips a ride whose back-of-queue cell (0x004DE110) is 0: a ride without a front (QUEUE-003, a zero front) is never scored, chosen or joined
	private bool HasScoreBackCell( IRideVisitorBridge attraction ) => attraction.BackCell is { } back && (attraction.JoinCell != null
		|| decisions.ScoresDisconnectedQueues && (attraction.HasTypedBack || Grid.Cells.RawTypeAt( back.X, back.Y ) == (byte)ParkCellType.Queue));

	/// <summary>
	/// <paramref name="attraction"/> as <paramref name="guest"/> sees it: every value the score reads, taken now. An opted-in disconnected typed back carries real facts while
	/// <see cref="AttractionView.HasJoinCell"/> remains false. An unsupported back, or a policy without that capability, retains the identity-and-flags-only fallback.
	/// </summary>
	internal AttractionView ViewOf( IRideVisitorBridge attraction, Guest guest, bool reservations = true )
	{
		if ( !HasScoreBackCell( attraction ) )
			return new AttractionView( attraction.AttractionId, attraction.TypeId, attraction.Kind, false, default, attraction.EntranceCell, 0, attraction.QueueLength, attraction.QueueSizeInCells,
				attraction.QueueLimit, 0, 0, 0, 0, attraction.ProvidesRelief, 0, false, 0, 0, attraction.IsOpen, attraction.IsBrokenDown, false );
		var sameType = guest.UsedHistory[0] != 0 && FindAttraction( guest.UsedHistory[0] ) is { } last && last.TypeId == attraction.TypeId;
		var back = attraction.BackCell!.Value;
		var modifier = Litter?.Environment.At( back.X, back.Y ).Attraction ?? 0;
		// [EXT:smarter-guests-value] the price and the guest's willingness for a shop or sideshow with a price, for a policy that weighs prices
		var (price, willingness) = decisions.UsesValueForMoney ? PriceFacts( attraction, guest ) : (0, 0u);
		// [EXT:smarter-guests-spread] the walkers (none in a queue hook's view), the observed time per guest and the cycle, for a policy that spreads guests
		int walkers = 0;
		long interval = 0, cycle = 0;
		if ( decisions.UsesSpreading && SmarterGuests is { } smarter )
		{
			// the queue hooks' views carry no walkers: outside a decision counting them is a scan of every guest, and neither hook reads them (the smarter policy zeroes them in the admission)
			walkers = reservations ? ReservationsOf( attraction.AttractionId, guest ) : 0;
			var capacity = attraction.LiveCapacity;
			interval = smarter.Throughputs.IntervalFor( attraction.AttractionId, attraction.Kind, capacity, attraction.Speed, ParkTurn, attraction.QueueLength );
			cycle = SmarterGuestsRules.CycleTurns256Of( interval, capacity );
		}
		// built once, whatever the policy asks for: the view is a large struct and a copy per rule is a cost per candidate per decision
		return new AttractionView( attraction.AttractionId, attraction.TypeId, attraction.Kind, attraction.JoinCell != null, back, attraction.EntranceCell, modifier, attraction.QueueLength, attraction.QueueSizeInCells,
			attraction.QueueLimit, attraction.LiveExcitement, attraction.StaticExcitementLevel, attraction.ThirstEffect, attraction.HungerEffect, attraction.ProvidesRelief, attraction.AgeDays,
			attraction.IsIndoors, attraction.GoldenTicketCost, attraction.UpgradeCost, attraction.IsOpen, attraction.IsBrokenDown, sameType, price, willingness, walkers, interval, cycle );
	}

	/// <summary>The walkers per attraction, counted once at the start of a decision (<see cref="BeginReservations"/>); null outside one.</summary>
	private Dictionary<int, int>? reservationCounts;

	/// <summary>The dictionary behind <see cref="reservationCounts"/>, kept between decisions (a decision does not nest in another, so one is enough).</summary>
	private Dictionary<int, int>? reservationScratch;

	/// <summary>
	/// Counts, once, the guests walking to each attraction, for the candidates of the decision that follows (<see cref="EndReservations"/> ends it). The candidate loops change no guest's state or target, so the count is
	/// exactly the one a scan per candidate would give, without the scan (one pass over the guests per decision instead of one per candidate view).
	/// </summary>
	private void BeginReservations()
	{
		if ( !decisions.UsesSpreading || reservationCounts != null )
			return;
		reservationCounts = reservationScratch ??= new Dictionary<int, int>();
		reservationCounts.Clear();
		foreach ( var other in guests )
			if ( other.State == GuestState.GoingToRide )
				reservationCounts[other.AttractionId] = reservationCounts.GetValueOrDefault( other.AttractionId ) + 1;
	}

	private void EndReservations() => reservationCounts = null;

	/// <summary>The guests other than <paramref name="guest"/> walking to the attraction: from the decision's count, else a scan of the guests.</summary>
	private int ReservationsOf( int attractionId, Guest guest )
	{
		var self = guest.State == GuestState.GoingToRide && guest.AttractionId == attractionId ? 1 : 0;
		if ( reservationCounts != null )
			return reservationCounts.GetValueOrDefault( attractionId ) - self;
		var count = 0;
		foreach ( var other in guests )
			if ( other.State == GuestState.GoingToRide && other.AttractionId == attractionId && !ReferenceEquals( other, guest ) )
				count++;
		return count;
	}

	/// <summary>A guest boarded <paramref name="ride"/>: folds the gap since its previous boarding into its throughput estimate (<see cref="RideThroughputs.Boarded"/>).</summary>
	// [EXT:smarter-guests-spread] the ride bridge reports every boarding, in the host's deterministic order, so the estimate is a function of the simulation's own events
	private void NoteBoarding( IRideVisitorBridge ride ) =>
		SmarterGuests?.Throughputs.Boarded( ride.AttractionId, ride.Kind, ride.LiveCapacity, ride.Speed, ParkTurn, ride.QueueLength );

	/// <summary>
	/// The turn-start look at the attractions: demand follows the queue (an open, working attraction with a queue gets an entry and a silence clock; a closed, broken or empty one loses its demand flag) and an
	/// upgrade (a changed live capacity or speed) restarts the estimate (<see cref="RideThroughputs.Observe"/>).
	/// </summary>
	private void ObserveThroughputs()
	{
		if ( SmarterGuests is not { } smarter )
			return;
		foreach ( var attraction in attractions )
			smarter.Throughputs.Observe( attraction.AttractionId, attraction.Kind, attraction.LiveCapacity, attraction.Speed, attraction.IsOpen && !attraction.IsBrokenDown, attraction.QueueLength, ParkTurn );
	}

	/// <summary>
	/// What the value-for-money rule reads of <paramref name="attraction"/> (docs/SMARTER-GUESTS.md, phase 2): for a shop or sideshow with a price, the price and the guest's willingness to pay right now (the stand-point
	/// test's word, <see cref="QueueJoinRules.WillingnessToPay"/>, computed here without recording a statistic: <see cref="TooExpensive"/> stays the only place that does). Rides and free stands carry nothing.
	/// </summary>
	// [EXT:smarter-guests-value] host-computed facts for the policy; the stand-point test and its statistics are unchanged
	private static (int Price, uint Willingness) PriceFacts( IRideVisitorBridge attraction, Guest guest )
	{
		if ( attraction.Kind is not (RideVisitorKind.Shop or RideVisitorKind.Sideshow) )
			return (0, 0);
		var terms = PriceTermsOf( attraction );
		if ( terms.Price == 0 )
			return (0, 0);
		return (terms.Price, QueueJoinRules.WillingnessToPay( terms, guest.Happiness, guest.Thirst, guest.Hunger, guest.Nausea ));
	}

	/// <summary>
	/// The rides and sideshows that are open, not broken and have a join cell: the park's variety as the smarter booth counts it (shops and toilets are services, not draws). Zero without asking (the original
	/// ignores it), and zero for a park with none open, which the booth rule reads as its least favourable 85 percent.
	/// </summary>
	private int OpenAttractionCount()
	{
		if ( !decisions.UsesValueForMoney )
			return 0;
		var count = 0;
		foreach ( var attraction in attractions )
			if ( attraction.Kind is RideVisitorKind.Ride or RideVisitorKind.Sideshow && attraction.JoinCell != null && attraction.IsOpen && !attraction.IsBrokenDown )
				count++;
		return count;
	}

	/// <summary>
	/// <paramref name="view"/> with the commitment the planning rule looks ahead over (docs/SMARTER-GUESTS.md, phase 5): when a meter of the guest is near a level the rule plans for
	/// (<see cref="SmarterGuestsRules.MayCross"/>), the turns before it could next serve a need if it committed to a ride now: the ride it is walking to (<paramref name="target"/>, in the minor decision), else the
	/// quickest ride it could choose. A guest with no meter that close pays nothing, which is nearly every guest. Pure: it records and changes nothing.
	/// </summary>
	// [EXT:smarter-guests-planning] the expected wait is the phase 3 estimate (queue ahead plus walkers over the observed throughput), the ride time its cycle
	internal GuestView WithPlanHorizon( GuestView view, Guest guest, IRideVisitorBridge? target )
	{
		if ( !decisions.UsesPlanHorizon || !decisions.UsesSpreading || !SmarterGuestsRules.MayCross( view, ParkTurn ) )
			return view;
		var turns = target != null ? CommitmentTurns256( ViewOf( target, guest ) ) : BestCommitment256( guest );
		return turns < 0 ? view : view with { PlanTurns256 = turns };
	}

	/// <summary>The turns a guest is busy with a ride from now: the expected wait of the queue (and the walkers) plus one cycle, in 1/256 turn; -1 without an estimate or for anything but a ride.</summary>
	private static long CommitmentTurns256( in AttractionView ride ) =>
		ride.Kind == RideVisitorKind.Ride && SmarterGuestsRules.ExpectedWait256( ride ) is { } wait ? wait + ride.CycleTurns256 : -1;

	/// <summary>
	/// The shortest <see cref="CommitmentTurns256"/> over the rides <paramref name="guest"/> could choose now (open, working, with room, its excitement admitted, and not one it has used or been turned away from lately:
	/// <see cref="SmarterGuestsRules.IsUnusedAdmissibleRide"/>), or -1 when there is none. The least, so the planning only acts when even the quickest ride leaves a need to cross. Pure.
	/// </summary>
	private long BestCommitment256( Guest guest )
	{
		var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
		var (context, guestView) = (ContextNow(), ViewOf( guest ));
		var counted = reservationCounts == null;
		BeginReservations();
		try
		{
			var best = -1L;
			foreach ( var other in attractions )
			{
				if ( !CouldJoinRide( other, type ) )
					continue;
				var view = ViewOf( other, guest );
				if ( SmarterGuestsRules.IsUnusedAdmissibleRide( context, guestView, view ) && CommitmentTurns256( view ) is var turns and >= 0 && (best < 0 || turns < best) )
					best = turns;
			}
			return best;
		}
		finally
		{
			if ( counted )
				EndReservations();
		}
	}

	/// <summary>A ride a guest of this type could join now: it has a join cell, is open and working, has room, and its excitement is admitted.</summary>
	private static bool CouldJoinRide( IRideVisitorBridge other, GuestType type ) =>
		other.Kind == RideVisitorKind.Ride && other.JoinCell != null && other.IsOpen && !other.IsBrokenDown && other.QueueLength < other.QueueLimit
		&& QueueJoinRules.Refusal( other.StaticExcitementLevel, other.JoinDifference( type.PreferredExcitement ) ) == null;

	/// <summary>
	/// The queue patience hook (docs/SMARTER-GUESTS.md): runs after the existing leave tests of a waiting guest, on a turn the guest stays, never while it is called forward or in an
	/// interlude. True when the policy lets the guest leave; the host then dismisses it with the policy's thought. The original policy always stays, so this changes nothing and takes no draw.
	/// </summary>
	private bool QueuePatienceLeaves( Guest guest, IRideVisitorBridge attraction, int position )
	{
		var context = ContextNow();
		// a guest that has not waited long enough for the policy to answer anything but the original's "stay" is not asked, and no view is built for it (nearly every queued guest on nearly every turn)
		var quiet = decisions.QueuePatienceQuietTurns( context, guest.BirthId );
		if ( quiet >= 0 && guest.QueueJoinTurn >= 0 && ParkTurn - guest.QueueJoinTurn < quiet )
			return false;
		var view = ViewOf( guest );
		// the alternatives are scanned only for a guest that is not at the front and has waited long enough for the policy's own give-up test to start
		var look = decisions.AlternativeLookAfterTurns( context, view );
		var alternative = look >= 0 && position > 0 && guest.QueueJoinTurn >= 0 && ParkTurn - guest.QueueJoinTurn >= look ? BestAlternativeWait256( guest, attraction.AttractionId ) : -1;
		var decision = decisions.DecideQueuePatience( context, view, new QueuePatienceView( ViewOf( attraction, guest, reservations: false ), guest.QueueJoinTurn, position, attraction.QueueLength, alternative ) );
		if ( !decision.Leave )
			return false;
		DismissFromQueue( guest, attraction, decision.Thought, QueueEndReason.Impatient );
		return true;
	}

	/// <summary>
	/// The shortest expected wait, in 1/256 park turn, that <paramref name="guest"/> would have at another ride it could actually choose and join: open, working, with room, its excitement word and join difference admitted
	/// and, as the choice would have it, not in its used or temporary history, not of the type it rode last and of an excitement it likes (<see cref="SmarterGuestsRules.IsUnusedAdmissibleRide"/>). The same estimate the spreading
	/// rules use. -1 when there is no such ride or no estimate. Pure: it records and changes nothing.
	/// </summary>
	// [EXT:smarter-guests-patience] a guest gives up on a queue only for a ride it could join with a clearly shorter wait; a park with one ride has nowhere else to go
	private long BestAlternativeWait256( Guest guest, int excludeAttractionId )
	{
		if ( SmarterGuests == null )
			return -1;
		var type = Settings.Types[Math.Min( guest.Type, Settings.Types.Count - 1 )];
		var (context, guestView) = (ContextNow(), ViewOf( guest ));
		var counted = reservationCounts == null;
		BeginReservations();
		try
		{
			var best = -1L;
			foreach ( var other in attractions )
			{
				if ( other.AttractionId == excludeAttractionId || !CouldJoinRide( other, type ) )
					continue;
				var view = ViewOf( other, guest );
				if ( SmarterGuestsRules.IsUnusedAdmissibleRide( context, guestView, view ) && SmarterGuestsRules.ExpectedWait256( view ) is { } wait && (best < 0 || wait < best) )
					best = wait;
			}
			return best;
		}
		finally
		{
			if ( counted )
				EndReservations();
		}
	}

	/// <summary>
	/// The comfort of the wander's goal (docs/SMARTER-GUESTS.md, phase 6): hands the policy the cell the guest stands on and the cells the original's random walk visited (the goal last), each with the litter around it and
	/// the guests standing in it, and performs the verdict: the goal moves to the named cell of the walk (the goal's own sub-cell bytes stay) and a complaint is thought, taken off the guest's happiness and noted for the
	/// cooldown. Reads the litter map and the cell lists only; the neighbourhoods are the nine cells around the walk's at most <see cref="GuestWander.TrailCapacity"/> + 1 cells.
	/// </summary>
	// [EXT:smarter-guests-paths] no draw is taken here and the original's route request is made afterwards, for whatever goal results
	internal GuestWander.Destination WeighPath( Guest guest, GuestWander.Destination proposal, ReadOnlySpan<(int X, int Y)> trail )
	{
		// the candidates: the cells of the original's walk, then the linked path cells beside its goal that the walk did not visit, then the goal itself (always last)
		Span<(int X, int Y)> cells = stackalloc (int X, int Y)[trail.Length + 4];
		var count = 0;
		var start = (X: guest.CellX, Y: guest.CellY);
		// never the guest's own cell (the walk may pass back through it; staying put is not a goal), and only path cells (a walk that starts on a queue cell can enter queue cells); the original's goal stays whatever it is
		for ( var index = 0; index < trail.Length - 1; index++ )
			if ( trail[index] != start && Grid.IsWalkable( trail[index].X, trail[index].Y ) )
				cells[count++] = trail[index];
		var goalCell = trail[^1];
		{
			var links = Grid.LinksAt( goalCell.X, goalCell.Y );
			for ( var direction = 0; direction < 4; direction++ )
			{
				var (dx, dy) = GuestPathGrid.Directions[direction];
				var next = (X: goalCell.X + dx, Y: goalCell.Y + dy);
				if ( (links & GuestPathGrid.LinkValue( direction )) == 0 || next == start || !Grid.IsWalkable( next.X, next.Y ) || cells[..count].Contains( next ) )
					continue;
				if ( Grid.Cells.PoolBridges is { } bridges && (bridges.Covers( goalCell.X, goalCell.Y ) || bridges.Covers( next.X, next.Y ))
					&& !bridges.Connects( goalCell.X, goalCell.Y, direction ) )
					continue;
				cells[count++] = next;
			}
		}
		cells[count++] = goalCell;
		Span<PathCellView> walk = stackalloc PathCellView[count];
		for ( var index = 0; index < count; index++ )
		{
			// the walk often steps back over a cell it has been on: that cell's view is the same
			var seen = cells[..index].IndexOf( cells[index] );
			walk[index] = seen >= 0 ? walk[seen] : PathCellOf( cells[index], guest );
		}
		var decision = decisions.ChoosePath( ContextNow(), ViewOf( guest ), new WanderPathView( PathCellOf( start, guest ), walk ) );
		if ( decision.GoalIndex < -1 || decision.GoalIndex >= walk.Length )
			throw PolicyError( $"returned the walk cell {decision.GoalIndex} of {walk.Length}" );
		if ( decision.Complaint != PathComplaint.None )
		{
			if ( !Enum.IsDefined( decision.Complaint ) || !Enum.IsDefined( decision.Thought ) || decision.Thought == GuestThought.None || !float.IsFinite( decision.HappinessCost ) || decision.HappinessCost < 0 || decision.HappinessCost > Settings.BigHappinessChange )
				throw PolicyError( $"returned the complaint {decision.Complaint} with thought {(int)decision.Thought} and happiness cost {decision.HappinessCost}" );
			Observer?.PathComplained( guest, decision.Complaint );
			Think( guest, decision.Thought );
			guest.Happiness = MeterMath.Add( guest.Happiness, -decision.HappinessCost );
			SmarterGuests?.Complaints.Note( guest.BirthId, ParkTurn );
		}
		else if ( decision.Thought != GuestThought.None || decision.HappinessCost != 0 )
		{
			throw PolicyError( $"returned thought {(int)decision.Thought} and happiness cost {decision.HappinessCost} without a complaint" );
		}
		if ( decision.GoalIndex is not (>= 0 and var goal) || goal == walk.Length - 1 )
			return proposal;
		Observer?.WanderGoalMoved( guest );
		return proposal with { Cell = cells[goal] };
	}

	/// <summary>A path cell as the comfort rule sees it: the litter in the cell and the eight around it (zero without any litter in the park: no cell is read) and the guests in the cell.</summary>
	internal PathCellView PathCellOf( (int X, int Y) cell, Guest? asking = null )
	{
		var units = 0;
		if ( Litter is { Count: > 0 } litter )
		{
			for ( var y = cell.Y - 1; y <= cell.Y + 1; y++ )
				for ( var x = cell.X - 1; x <= cell.X + 1; x++ )
					units += SmarterPathComfort.LitterUnitsOf( litter.TypeAt( x, y ) );
		}
		var near = 0;
		for ( var y = cell.Y - 1; y <= cell.Y + 1; y++ )
				for ( var x = cell.X - 1; x <= cell.X + 1; x++ )
					if ( (x != cell.X || y != cell.Y) && occupants.TryGetValue( (x, y), out var around ) )
						near += around.Count;
		var here = occupants.TryGetValue( cell, out var list ) ? list.Count : 0;
		// the asking guest is not a crowd to itself: it stands in one of these cells and is taken out of the count
		if ( asking?.OccupiedCell is { } mine && Math.Abs( mine.X - cell.X ) <= 1 && Math.Abs( mine.Y - cell.Y ) <= 1 )
		{
			if ( mine == cell )
				here--;
			else
				near--;
		}
		return new PathCellView( cell.X, cell.Y, units, Math.Max( 0, here ), Math.Max( 0, near ) );
	}
}
