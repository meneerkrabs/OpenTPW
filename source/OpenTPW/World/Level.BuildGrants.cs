namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// [EXT:online-coop-permissions] Each guest seat's rights (<see cref="CoopRights"/>), in seat order, from <see cref="SetSeatRightsCommand"/> and <see cref="SetBuildGrantCommand"/> alike (one
	/// reducer, <see cref="SetSeatRights"/>); null until the first. A seat without a right has no row. Session state: the session digest covers it, a join snapshot carries it, no world hash or
	/// save holds it.
	/// </summary>
	private SortedDictionary<int, CoopRights>? seatRights;

	/// <summary>[EXT:online-coop-permissions] Every guest seat's rights, in seat order (empty without a session or a right).</summary>
	internal IReadOnlyDictionary<int, CoopRights> SeatRights => (IReadOnlyDictionary<int, CoopRights>?)seatRights ?? EmptySeatRights;

	private static readonly IReadOnlyDictionary<int, CoopRights> EmptySeatRights = new SortedDictionary<int, CoopRights>();

	/// <summary>[EXT:online-coop-permissions] The rights of <paramref name="seat"/>; none for a seat without a row.</summary>
	internal CoopRights RightsOf( int seat ) => seatRights != null && seatRights.TryGetValue( seat, out var rights ) ? rights : CoopRights.None;

	/// <summary>[EXT:online-coop-permissions] The seats granted building, in order (empty without a session or a grant).</summary>
	internal IReadOnlyCollection<int> BuildGrants => SeatRights.Where( pair => pair.Value.HasFlag( CoopRights.Build ) ).Select( pair => pair.Key ).ToArray();

	/// <summary>[EXT:online-coop-permissions] Whether the guest's <paramref name="seat"/> may build.</summary>
	internal bool HasBuildGrant( int seat ) => RightsOf( seat ).HasFlag( CoopRights.Build );

	/// <summary>[EXT:online-coop-permissions] Takes every seat's rights back: rights are their session's, and the park's next session starts without any.</summary>
	internal void ClearSeatRights() => seatRights = null;

	/// <summary>[EXT:online-coop-permissions] Grants <paramref name="seat"/> building, or takes it back (<see cref="SetBuildGrantCommand"/>): the building bit of the seat's rights alone.</summary>
	internal void SetBuildGrant( int seat, bool granted ) =>
		SetSeatRights( seat, granted ? RightsOf( seat ) | CoopRights.Build : RightsOf( seat ) & ~CoopRights.Build );

	/// <summary>
	/// [EXT:online-coop-permissions] Sets the rights of <paramref name="seat"/> (C4a), the one reducer of <see cref="SetSeatRightsCommand"/> and <see cref="SetBuildGrantCommand"/>: a seat
	/// without a right has no row. A right taken back ends what it held, in the same record: building's claims ([EXT:online-coop-claims], with a coaster's tool state, C3c-1b and C3c-2), the
	/// staff's pending hire and employee in hand (C4c, <see cref="ReleaseStaffOf"/>).
	/// </summary>
	internal void SetSeatRights( int seat, CoopRights rights )
	{
		var taken = RightsOf( seat ) & ~rights;
		if ( rights == CoopRights.None )
		{
			if ( seatRights != null && seatRights.Remove( seat ) && seatRights.Count == 0 )
				seatRights = null;
		}
		else
			(seatRights ??= new SortedDictionary<int, CoopRights>())[seat] = rights;
		if ( taken.HasFlag( CoopRights.Build ) )
			ReleaseClaimsOf( seat );
		if ( taken.HasFlag( CoopRights.Staff ) )
			ReleaseStaffOf( seat );
	}

	/// <summary>
	/// [EXT:online-coop-permissions] The guest seats that hold anything in the park, in order (C4a): a right, a carried type (C3b-2), a claim (C3c-1) or staff state (C4c), which their release
	/// frees (<see cref="ReleaseSeatCommand"/>).
	/// </summary>
	internal IEnumerable<int> HoldingGuestSeats() =>
		SeatRights.Keys.Concat( CarryingGuestSeats() ).Concat( ClaimingSeats().Where( seat => seat > 0 ) ).Concat( StaffHoldingGuestSeats() ).Distinct().Order().ToArray();

	/// <summary>[EXT:online-coop-permissions] Whether the guest seat <paramref name="seat"/> holds anything its release frees (<see cref="HoldingGuestSeats"/>).</summary>
	internal bool SeatHoldsAnything( int seat ) => seat > 0 && (RightsOf( seat ) != CoopRights.None || CarriesEntry( seat ) || ClaimedRideOf( seat ) != null || HoldsStaff( seat ));

	/// <summary>[EXT:online-coop-permissions] Takes every right of <paramref name="seat"/> back (a departed seat's release, <see cref="ReleaseSeatCommand"/>). True when it held one.</summary>
	internal bool ReleaseSeatRights( int seat )
	{
		if ( RightsOf( seat ) == CoopRights.None )
			return false;
		SetSeatRights( seat, CoopRights.None );
		return true;
	}
}
