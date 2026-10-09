namespace OpenTPW;

/// <summary>
/// Connects <see cref="GuestSimulation"/> to <see cref="ParkEconomy"/>: admission goes through
/// <see cref="ParkEconomy.TryAdmitVisitor"/> (the economy's entrance fee, <c>BankAccountInfo.InitialAdmissionFee</c>
/// until changed, and its open/closed state), shop and sideshow visits through <see cref="ParkEconomy.TryBuy"/>
/// and <see cref="ParkEconomy.PlaySideshow"/>, ride visits through <see cref="ParkEconomy.RecordRideUse"/>.
/// Attractions are matched to economy objects with <see cref="Link"/>; unlinked attractions are free.
/// It also supplies <see cref="IParkGuestStatistics"/> from the guests for rating, tickets and challenges.
/// </summary>
public sealed class GuestEconomyBridge : IGuestPayments, IParkGuestStatistics
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
	// [APPROX:ECON-044] balloon/costume percentages are 0 (guests carry no items yet) — evidence needed: guests slice item state
	public int KidsWithBalloonsPercent => 0;

	public int KidsWithCostumesPercent => 0;
}
