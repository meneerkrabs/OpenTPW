namespace OpenTPW;

/// <summary>What the staff world asks of the guests: a guard's prankster (where it is, whether it can be caught, held and set down) and an entertainer's audience.</summary>
public interface IGuardGuests
{
	/// <summary>The guest's cell, null when it is gone.</summary>
	(int X, int Y)? CellOf( int guestId );
	/// <summary>Whether the guest's state lets a guard catch it.</summary>
	bool IsCatchable( int guestId );
	/// <summary>A guard caught the guest: it is carried, out of sight.</summary>
	void Hold( int guestId );
	/// <summary>
	/// The guard sets the guest down on a cell: true when it walks out of the park, false when it is removed. <paramref name="side"/> is the way out
	/// the set-down drew (0: lane A, 1: lane B); null leaves by the nearer reachable lane.
	/// </summary>
	bool Release( int guestId, (int X, int Y) cell, int? side = null );
	/// <summary>Whether a guest stands on the map within a square of the given radius.</summary>
	bool AnyGuestWithin( (int X, int Y) cell, int radius );
	/// <summary>The walks on a cell's thing chain that separation pushes away from, head first: the guests (on a ride or carried too) and the staff interleaved by the time each entered the cell.</summary>
	IEnumerable<GuestNavigation> WalkersOn( int x, int y ) => [];
	/// <summary>Arrival lane A (0) or B (1), bus stop first and park entrance last; null when the level has none.</summary>
	(int X, int Y)[]? Lane( int side );
}
