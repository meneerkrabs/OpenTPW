namespace OpenTPW;

/// <summary>
/// Connects <see cref="GuestSimulation"/> to <see cref="ParkEconomy"/>: admission goes through
/// <see cref="ParkEconomy.TryAdmitVisitor"/> (the economy's entrance fee, <c>BankAccountInfo.InitialAdmissionFee</c>
/// until changed, and its open/closed state), shop and sideshow visits through <see cref="ParkEconomy.TryBuy"/>
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

	public bool TryPayAdmission( long guestMoney, out int feePaid ) => Economy().TryAdmitVisitor( guestMoney, out feePaid );

	public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid )
	{
		pricePaid = 0;
		if ( !instances.TryGetValue( attraction.AttractionId, out var instance ) )
			return true;
		var economy = Economy();
		switch ( attraction.Kind )
		{
			case RideVisitorKind.Shop:
				return economy.TryBuy( instance, guestMoney, out pricePaid );
			case RideVisitorKind.Sideshow:
				return economy.PlaySideshow( instance, guestMoney, out pricePaid, out _ );
			default:
				economy.RecordRideUse( instance );
				return true;
		}
	}

	public int PeopleInPark => Guests.GetStatistics().InPark;

	public int AverageHappiness => (int)Guests.GetStatistics().AverageHappiness;

	public int CountHappierThan( int happiness ) => Guests.Guests.Count( guest => Guests.IsInPark( guest ) && guest.Happiness >= happiness );

	/// <summary>Guests carry no balloons or costumes yet.</summary>
	// [BIN:STP-PPC:0x100CE7B4 challenge types 9 and 10] percent = trunc(100 × in-park guests holding a balloon (guest +0x210, 0x100C3BEC) or wearing a costume (+0x24 == 2, 0x100C3CD0) / in-park guests), 0 without guests
	// [APPROX:ECON-044] guests never hold a balloon or wear a costume, so both percentages are 0; the binary gives them when a guest uses a balloon or costume shop (0x100EAAF8) — evidence needed: balloon lifetime (+0x214) and costume state rules
	public int KidsWithBalloonsPercent => 0;

	public int KidsWithCostumesPercent => 0;
}
