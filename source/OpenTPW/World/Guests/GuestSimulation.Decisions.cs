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
		return decisions.UsesValueForMoney ? WithPrice( view, attraction, guest ) : view;
	}

	/// <summary>
	/// <paramref name="view"/> with what the value-for-money rule reads (docs/SMARTER-GUESTS.md, phase 2): for a shop or sideshow with a price, the price, the guest's willingness to pay right now (the stand-point
	/// test's word, <see cref="QueueJoinRules.WillingnessToPay"/>, computed here without recording a statistic: <see cref="TooExpensive"/> stays the only place that does) and whether the guest remembers a refusal
	/// there. Rides and free stands carry nothing.
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
		var refused = SmarterGuests is { } smarter && smarter.Memory.Refused( guest.BirthId, attraction.AttractionId, ParkTurn );
		return view with { Price = terms.Price, Willingness = willingness, RecentlyRefused = refused };
	}

	/// <summary>The attractions that are open and not broken and have a join cell: the park's variety as the smarter booth counts it. Zero without asking (the original ignores it).</summary>
	private int OpenAttractionCount()
	{
		if ( !decisions.UsesValueForMoney )
			return 0;
		var count = 0;
		foreach ( var attraction in attractions )
			if ( attraction.JoinCell != null && attraction.IsOpen && !attraction.IsBrokenDown )
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
