namespace OpenTPW;

/// <summary>
/// Where guests pay. When set on <see cref="GuestSimulation.Payments"/> it is the single source of
/// the entrance fee and of every charge (the park economy, <see cref="GuestEconomyBridge"/>); the
/// simulation keeps no money totals of its own, only the guests' purses and the
/// <see cref="GuestSimulation.MoneySpent"/> event.
/// </summary>
public interface IGuestPayments
{
	/// <summary>Current entrance fee shown at the ticket booth.</summary>
	int AdmissionFee { get; }
	/// <summary>Charges admission. False when the park is closed or the guest cannot pay.</summary>
	bool TryPayAdmission( long guestMoney, out int feePaid );
	/// <summary>Charges a shop or sideshow visit (0 for rides, which only count the use).</summary>
	bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid );
}
