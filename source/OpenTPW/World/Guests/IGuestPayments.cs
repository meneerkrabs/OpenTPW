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
	/// <summary>Books admission at the booth. False only when the park refuses it (bankrupt); the guest's purse is not read, and a closed park stops guests on their walk to the booth (<see cref="ParkClosed"/>).</summary>
	bool TryPayAdmission( out int feePaid );
	/// <summary>True while the park is closed (the world variable <c>mParkClosed</c>): every guest that gets to decide heads home (<see cref="GuestLifecycle"/>).</summary>
	bool ParkClosed => false;
	/// <summary>Charges a shop or sideshow visit (0 for rides, which only count the use).</summary>
	bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid );
	/// <summary>The same, with a sideshow's result (<paramref name="won"/>, null when the payer does not play sideshows out).</summary>
	bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid, out bool? won )
	{
		won = null;
		return TryPayVisit( attraction, guestMoney, out pricePaid );
	}
	/// <summary>A guest has left the attraction (the original's use counter, +412, rises on release).</summary>
	void VisitFinished( IRideVisitorBridge attraction ) { }
	/// <summary>A guest used a relief object with this toilet meter (as a byte): the toilet's gauge wears (tp.exe 0x004E2440).</summary>
	void ReliefUsed( IRideVisitorBridge attraction, int toiletByte ) { }
	/// <summary>A guest refused the attraction's price at its stand point and walked away (the ride's walk-away count, tp.exe <c>0x004E1670</c>).</summary>
	void WalkedAway( IRideVisitorBridge attraction ) { }
	/// <summary>The price test recorded a perception value in a park statistic category (tp.exe <c>0x004C74B0</c>).</summary>
	void PriceTested( int category, int value ) { }
	/// <summary>The park's litter by cell, where guests drop theirs (null: litter is not kept).</summary>
	ParkLitter? Litter => null;
}
