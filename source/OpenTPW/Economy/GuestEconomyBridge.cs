namespace OpenTPW;

/// <summary>
/// Connects <see cref="GuestSimulation"/> to <see cref="ParkEconomy"/>: admission goes through
/// <see cref="ParkEconomy.TryAdmitVisitor"/> (the economy's entrance fee, <c>BankAccountInfo.InitialAdmissionFee</c>
/// until changed; the guests' walk to the booth reads the open/closed state through <see cref="ParkClosed"/>), shop and sideshow visits through <see cref="ParkEconomy.TryBuy"/>
/// and <see cref="ParkEconomy.PlaySideshow"/>, ride visits through <see cref="ParkEconomy.RecordRideUse"/>.
/// Attractions are matched to economy objects with <see cref="Link"/>; unlinked attractions are free.
/// It also supplies <see cref="IParkGuestStatistics"/> from the guests for rating, tickets and challenges, and
/// <see cref="IRideOperations"/> from the linked rides' scripts for the wear step.
/// </summary>
public sealed class GuestEconomyBridge : IGuestPayments, IParkGuestStatistics, IRideOperations
{
	private readonly Dictionary<int, int> instances = new();

	public GuestEconomyBridge( Func<ParkEconomy> economy, GuestSimulation guests )
	{
		Economy = economy ?? throw new ArgumentNullException( nameof( economy ) );
		Guests = guests ?? throw new ArgumentNullException( nameof( guests ) );
	}

	/// <summary>Resolves the current economy (a park load replaces the instance).</summary>
	public Func<ParkEconomy> Economy { get; }
	public GuestSimulation Guests { get; }

	public void Link( int attractionId, int economyInstanceId ) => instances[attractionId] = economyInstanceId;

	public void Unlink( int attractionId ) => instances.Remove( attractionId );

	public bool TryGetInstance( int attractionId, out int instanceId ) => instances.TryGetValue( attractionId, out instanceId );

	/// <summary>The running state, riders and speed of the ride linked to an economy object, read from its script.</summary>
	// [BIN:STP-PPC:0x100B5BE0 ride script variable] the wear step reads script variables 9 (VAR_RUNNING), 5 (VAR_ONRIDE), 2 and 3 and the script's speed (+0xC0, 0x100B5B40); 0x100B57D4 writes 4 (VAR_BREAKSTAT) and 8 (VAR_WORN)
	public bool TryGet( int instanceId, out RideOperation operation )
	{
		foreach ( var (attractionId, linked) in instances )
		{
			if ( linked != instanceId )
				continue;
			var attraction = Guests.Attractions.FirstOrDefault( candidate => candidate.AttractionId == attractionId );
			if ( attraction == null )
				break;
			operation = new RideOperation( attraction.IsRunning, attraction.RidersOnBoard, attraction.Speed );
			return true;
		}
		operation = default;
		return false;
	}

	public int AdmissionFee => Economy().EntranceFee;

	public bool ParkClosed => !Economy().IsParkOpen;

	public ParkLitter? Litter => Economy().Litter;

	public bool TryPayAdmission( long guestMoney, out int feePaid ) => Economy().TryAdmitVisitor( guestMoney, out feePaid );

	public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) => TryPayVisit( attraction, guestMoney, out pricePaid, out _ );

	public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid, out bool? won )
	{
		pricePaid = 0;
		won = null;
		if ( !instances.TryGetValue( attraction.AttractionId, out var instance ) )
			return true;
		var economy = Economy();
		switch ( attraction.Kind )
		{
			case RideVisitorKind.Shop:
				return economy.TryBuy( instance, guestMoney, out pricePaid );
			case RideVisitorKind.Sideshow:
				var played = economy.PlaySideshow( instance, guestMoney, out pricePaid, out var result );
				won = played ? result : null;
				return played;
			default:
				economy.RecordRideUse( instance );
				return true;
		}
	}

	public void ReliefUsed( IRideVisitorBridge attraction, int toiletByte )
	{
		if ( instances.TryGetValue( attraction.AttractionId, out var instance ) )
			Economy().UseRelief( instance, toiletByte );
	}

	public void VisitFinished( IRideVisitorBridge attraction )
	{
		if ( instances.TryGetValue( attraction.AttractionId, out var instance ) )
			Economy().RecordUseFinished( instance );
	}

	public void WalkedAway( IRideVisitorBridge attraction )
	{
		if ( instances.TryGetValue( attraction.AttractionId, out var instance ) )
			Economy().RecordWalkAway( instance );
	}

	public void PriceTested( int category, int value ) => Economy().PriceStatistics.Record( category, value );

	public int PeopleInPark => Guests.GetStatistics().InPark;

	// [BIN:STP-PPC:0x100C19E4 average happiness] 0.0 while the park is closed (0x100C3524), otherwise the single mean of each in-park guest's (u8)trunc(happiness), not the mean of the raw values (PPC-scenarios, golden tickets)
	public float AverageHappiness
	{
		get
		{
			if ( !Economy().IsParkOpen )
				return 0f;
			int count = 0, sum = 0;
			foreach ( var guest in Guests.Guests )
			{
				if ( !Guests.IsInPark( guest ) )
					continue;
				count++;
				sum += (byte)(int)guest.Happiness;
			}
			return count == 0 ? 0f : (float)sum / count;
		}
	}

	public int CountHappierThan( int happiness ) => Guests.Guests.Count( guest => Guests.IsInPark( guest ) && guest.Happiness >= happiness );

	/// <summary>The percentage of the in-park guests holding a balloon (challenge type 9).</summary>
	// [BIN:STP-PPC:0x100CE7B4 challenge types 9 and 10] percent = trunc(100 × in-park guests holding a balloon (guest +0x210, 0x100C3BEC) or wearing a costume (+0x24 == 2, 0x100C3CD0) / in-park guests), 0 without guests
	// [BIN:TPW-EXE:0x004D1746 challenge types 9 and 10] the same on the PC: the counts 0x004C9530 (+0x210 set) and 0x004C95D0 (+0x24 == 2) of the guests on a park cell (0x004FA990) times 100, divided (signed) by the in-park count 0x004C9130
	public int KidsWithBalloonsPercent => Percent( guest => guest.HasBalloon );

	/// <summary>The percentage of the in-park guests wearing a costume (challenge type 10).</summary>
	public int KidsWithCostumesPercent => Percent( guest => guest.InCostume );

	private int Percent( Func<Guest, bool> holds )
	{
		int inPark = 0, count = 0;
		foreach ( var guest in Guests.Guests )
		{
			if ( !Guests.IsOnParkCell( guest ) )
				continue;
			inPark++;
			if ( holds( guest ) )
				count++;
		}
		return inPark == 0 ? 0 : count * 100 / inPark;
	}
}
