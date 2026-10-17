namespace OpenTPW;

// The host side of the decision layer (docs/SMARTER-GUESTS.md, "A pure decision layer"): the views the simulation hands to its IGuestDecisions and the one hook that is new.
public sealed partial class GuestSimulation
{
	/// <summary>What every decision may read besides its own views: the settings, the park turn and the weather.</summary>
	private DecisionContext ContextNow() => new( Settings, ParkTurn, WeatherDropsFalling?.Invoke() ?? false, SmarterGuests?.ParkSeed ?? Seed, Grid.CountX, Grid.CountY );

	/// <summary>The error for a policy result the host cannot act on: it names the policy, and nothing of the result is used.</summary>
	private InvalidOperationException PolicyError( string what ) => new( $"The guest decision policy {decisions.GetType().Name} {what}." );

	/// <summary>A copy of the fields of <paramref name="guest"/> the decisions read; the histories are views of the guest's arrays, unchanged while a decision runs.</summary>
	private static GuestView ViewOf( Guest guest ) => new( guest.Id, guest.BirthId, guest.Type, guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea, guest.Money,
		guest.CellX, guest.CellY, Array.AsReadOnly( guest.UsedHistory ), Array.AsReadOnly( guest.TemporaryHistory ) );

	/// <summary>
	/// <paramref name="attraction"/> as <paramref name="guest"/> sees it: every value the original score reads, taken now. An attraction without a join cell carries identity and flags only
	/// (the original scores it 0 without reading anything else).
	/// </summary>
	private AttractionView ViewOf( IRideVisitorBridge attraction, Guest guest )
	{
		if ( attraction.JoinCell == null )
			return new AttractionView( attraction.AttractionId, attraction.TypeId, attraction.Kind, false, default, attraction.EntranceCell, 0, attraction.QueueLength, attraction.QueueSizeInCells,
				attraction.QueueLimit, 0, 0, 0, 0, attraction.ProvidesRelief, 0, false, 0, 0, attraction.IsOpen, attraction.IsBrokenDown, false );
		var sameType = guest.UsedHistory[0] != 0 && FindAttraction( guest.UsedHistory[0] ) is { } last && last.TypeId == attraction.TypeId;
		var back = attraction.QueueCells[^1];
		var modifier = Litter?.Environment.At( back.X, back.Y ).Attraction ?? 0;
		var view = new AttractionView( attraction.AttractionId, attraction.TypeId, attraction.Kind, true, back, attraction.EntranceCell, modifier, attraction.QueueLength, attraction.QueueSizeInCells,
			attraction.QueueLimit, attraction.LiveExcitement, attraction.StaticExcitementLevel, attraction.ThirstEffect, attraction.HungerEffect, attraction.ProvidesRelief, attraction.AgeDays,
			attraction.IsIndoors, attraction.GoldenTicketCost, attraction.UpgradeCost, attraction.IsOpen, attraction.IsBrokenDown, sameType );
		if ( decisions.UsesValueForMoney )
			view = WithPrice( view, attraction, guest );
		return decisions.UsesSpreading ? WithSpreading( view, attraction, guest ) : view;
	}

	/// <summary>
	/// <paramref name="view"/> with what the spreading rules read (docs/SMARTER-GUESTS.md, phase 3): the guests, other than <paramref name="guest"/>, walking to the attraction (their target, not yet in its queue) and the
	/// attraction's observed time per guest (<see cref="RideThroughputs"/>, the cold start without a measurement). Pure: it records and changes nothing.
	/// </summary>
	// [EXT:smarter-guests-spread] host-computed facts for the policy; the throughput bookkeeping is the host's (NoteBoarding, ObserveThroughputs)
	private AttractionView WithSpreading( AttractionView view, IRideVisitorBridge attraction, Guest guest )
	{
		if ( SmarterGuests is not { } smarter )
			return view;
		var reservations = ReservationsOf( attraction.AttractionId, guest );
		var interval = smarter.Throughputs.IntervalFor( attraction.AttractionId, attraction.Kind, attraction.LiveCapacity, attraction.Speed, ParkTurn, attraction.QueueLength );
		return view with { Reservations = reservations, TurnsPerGuest256 = interval };
	}

	/// <summary>The walkers per attraction, counted once at the start of a decision (<see cref="BeginReservations"/>); null outside one.</summary>
	private Dictionary<int, int>? reservationCounts;

	/// <summary>
	/// Counts, once, the guests walking to each attraction, for the candidates of the decision that follows (<see cref="EndReservations"/> ends it). The candidate loops change no guest's state or target, so the count is
	/// exactly the one a scan per candidate would give, without the scan (one pass over the guests per decision instead of one per candidate view).
	/// </summary>
	private void BeginReservations()
	{
		if ( !decisions.UsesSpreading || reservationCounts != null )
			return;
		reservationCounts = new Dictionary<int, int>();
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
	/// <paramref name="view"/> with what the value-for-money rule reads (docs/SMARTER-GUESTS.md, phase 2): for a shop or sideshow with a price, the price, the guest's willingness to pay right now (the stand-point
	/// test's word, <see cref="QueueJoinRules.WillingnessToPay"/>, computed here without recording a statistic: <see cref="TooExpensive"/> stays the only place that does)  Rides and free stands carry nothing.
	/// </summary>
	// [EXT:smarter-guests-value] host-computed facts for the policy; the stand-point test and its statistics are unchanged
	private AttractionView WithPrice( AttractionView view, IRideVisitorBridge attraction, Guest guest )
	{
		if ( attraction.Kind is not (RideVisitorKind.Shop or RideVisitorKind.Sideshow) )
			return view;
		var terms = PriceTermsOf( attraction );
		if ( terms.Price == 0 )
			return view;
		var willingness = QueueJoinRules.WillingnessToPay( terms, guest.Happiness, guest.Thirst, guest.Hunger, guest.Nausea );
		return view with { Price = terms.Price, Willingness = willingness };
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
	/// The queue patience hook (docs/SMARTER-GUESTS.md): runs after the existing leave tests of a waiting guest, on a turn the guest stays, never while it is called forward or in an
	/// interlude. True when the policy lets the guest leave; the host then dismisses it with the policy's thought. The original policy always stays, so this changes nothing and takes no draw.
	/// </summary>
	private bool QueuePatienceLeaves( Guest guest, IRideVisitorBridge attraction )
	{
		var decision = decisions.DecideQueuePatience( ContextNow(), ViewOf( guest ), new QueuePatienceView( ViewOf( attraction, guest ), guest.QueueJoinTurn, guest.QueuePosition, attraction.QueueLength ) );
		if ( !decision.Leave )
			return false;
		DismissFromQueue( guest, attraction, decision.Thought, QueueEndReason.Impatient );
		return true;
	}
}
