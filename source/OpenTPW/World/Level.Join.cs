namespace OpenTPW;

public partial class Level
{
	/// <summary>[EXT:online-coop-snapshot-join] A join's session state while its build runs (<see cref="BeginJoinOverFrames"/>): the join restore mode; null otherwise.</summary>
	private CoopJoinState? joinRestore;

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The session state a join snapshot carries beyond the park's save (<see cref="CoopJoinState"/>), taken at the cut between two ticks: every player's carried
	/// catalogue entry and employee in hand, every seat's pending hire (C4c) and the stale on-pointer candidate (a load resets both), the coasters' being-edited
	/// marks, the session clock, the guests' seats' rights and the rides' claims. A held coaster drag refuses the capture, which waits for its release: the drag's edited pylon is no part of the
	/// save.
	/// </summary>
	internal CoopJoinState CaptureJoinState()
	{
		if ( coasterEdits is { IsEmpty: false } )
			throw new InvalidOperationException( "A coaster drag is held; the join capture waits for its release." );
		var staff = Park?.Economy.Staff;
		return new CoopJoinState(
			carriedEntries?.OrderBy( pair => pair.Key ).Select( pair => (pair.Key, pair.Value) ).ToArray() ?? Array.Empty<(int, string)>(),
			StaffHands?.All.ToArray() ?? Array.Empty<(int, int, int, int)>(),
			PendingHireRows(),
			staff?.StaleOnPointerId ?? 0,
			CoasterStations().Where( station => station.Ride.BeingEdited ).Select( station => station.AttractionId ).ToArray(),
			Objects?.ScriptWorld?.SessionTime?.Ticks,
			CoasterUnheldCells(),
			SeatRights.Select( pair => (pair.Key, pair.Value) ).ToArray(),
			RideClaims.Select( pair => (pair.Key, pair.Value) ).ToArray() );
	}

	/// <summary>[EXT:online-coop-permissions] The pending hires as a join state and the session digest carry them, seat by seat (C4c).</summary>
	internal (int Seat, int Candidate)[] PendingHireRows() =>
		Park?.Economy.Staff.PendingHires.Select( pair => (pair.Key, pair.Value) ).ToArray() ?? Array.Empty<(int, int)>();

	/// <summary>
	/// [EXT:online-coop-snapshot-join] Writes a join snapshot's session state (<see cref="CaptureJoinState"/>) into the park a guest built from its save, before its first tick: the save's load
	/// zeroed the pending hire and left the hands, the carried entries and the marks as the save has them. A coaster the state marks must be one of this park's, and a ride it claims one of
	/// its track rides or coasters; the staff state is checked whole (C4c): each seat once with one pending hire whose candidate the pool the save brought holds and no other seat reserves, each
	/// hand of a seat of the session holding an employee the park has, picked up, no other hand holds, from a cell of the park's grid, and a guest's seat with either a hand or a pending hire,
	/// not both (the host's seat may hold both). Anything else throws before any of the state applies.
	/// </summary>
	internal void ApplyJoinState( CoopJoinState state )
	{
		ArgumentNullException.ThrowIfNull( state );
		var stations = CoasterStations().ToArray();
		if ( state.CoastersBeingEdited.Except( stations.Select( station => station.AttractionId ) ).FirstOrDefault( -1 ) is var unknown and >= 0 )
			throw new InvalidDataException( $"The join state marks coaster {unknown}, which the park does not hold." );
		if ( state.CoasterUnheldCells.Select( cell => cell.Station ).Except( stations.Select( station => station.AttractionId ) ).FirstOrDefault( -1 ) is var unbound and >= 0 )
			throw new InvalidDataException( $"The join state holds marker cells of coaster {unbound}, which the park does not hold." );
		if ( state.StaffHands.Count > 0 && StaffHands == null )
			throw new InvalidDataException( "The join state holds employees in hand, which this park cannot hold." );
		var pool = Park?.Economy.Staff;
		if ( state.PendingHires.Count > 0 && pool == null )
			throw new InvalidDataException( "The join state holds a pending hire, which this park cannot hold." );
		RequireJoinStaffState( state, pool );
		// [EXT:online-coop-claims] a claimed ride must be one of this park's track rides (C3c-1) or coasters (C3c-2)
		var claims = state.RideClaims ?? Array.Empty<(int Ride, int Seat)>();
		if ( claims.Count > 0 )
		{
			var claimable = Objects.Objects.Where( item => !item.IsDeleted && IsClaimableRide( item ) ).Select( item => item.Runtime.AttractionId ).ToHashSet();
			if ( claims.Where( claim => !claimable.Contains( claim.Ride ) ).Select( claim => (int?)claim.Ride ).FirstOrDefault() is { } stray )
				throw new InvalidDataException( $"The join state claims attraction {stray}, which is no track ride or coaster of the park." );
		}
		carriedEntries = state.CarriedEntries.Count == 0 ? null : state.CarriedEntries.ToDictionary( item => item.Player, item => item.Entry );
		StaffHands?.Clear();
		foreach ( var (player, staff, x, y) in state.StaffHands )
			StaffHands!.Carry( player, staff, (x, y) );
		if ( Park?.Economy.Staff is { } economyStaff )
		{
			economyStaff.RestorePendingHires( state.PendingHires );
			economyStaff.StaleOnPointerId = state.StaleOnPointer;
		}
		var marked = state.CoastersBeingEdited.ToHashSet();
		foreach ( var (attraction, ride) in stations )
			ride.BeingEdited = marked.Contains( attraction );
		if ( Objects?.ScriptWorld is { } scripts )
			scripts.SessionTime = state.SessionClockTicks is { } ticks ? new DateTime( ticks, DateTimeKind.Unspecified ) : null;
		seatRights = state.SeatRights is { Count: > 0 } rights ? new SortedDictionary<int, CoopRights>( rights.ToDictionary( row => row.Seat, row => row.Rights ) ) : null;
		rideClaims = claims.Count > 0 ? new SortedDictionary<int, int>( claims.ToDictionary( claim => claim.Ride, claim => claim.Seat ) ) : null;
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The join state's staff rows checked whole against this park before any applies (C4c, <see cref="ApplyJoinState"/>): the pending hires and the hands each
	/// of a seat of the session at most once, a pending hire naming a candidate of the pool no other seat reserves, a hand holding an employee of the park, picked up, held by no other hand,
	/// from a cell of the park's grid, and a guest's seat holding one of a hand and a pending hire at most.
	/// </summary>
	private void RequireJoinStaffState( CoopJoinState state, ParkStaff? pool )
	{
		var hireSeats = new HashSet<int>();
		var reserved = new HashSet<int>();
		foreach ( var (seat, candidate) in state.PendingHires )
		{
			if ( seat < 0 || seat >= CoopJoinState.MaximumPlayerEntries || !hireSeats.Add( seat ) )
				throw new InvalidDataException( $"The join state holds a pending hire of seat {seat}, which is no seat of the session or holds two." );
			// a pending hire's candidate stays in the pool, which the save brought: a candidate it does not hold is no hire of this park
			if ( candidate <= 0 || pool == null || pool.Candidates.All( item => item.Id != candidate ) )
				throw new InvalidDataException( $"The join state's pending hire names candidate {candidate}, which the park's pool does not hold." );
			if ( !reserved.Add( candidate ) )
				throw new InvalidDataException( $"The join state's pending hires reserve candidate {candidate} twice." );
		}
		var handSeats = new HashSet<int>();
		var held = new HashSet<int>();
		foreach ( var (player, staff, x, y) in state.StaffHands )
		{
			if ( player < 0 || player >= CoopJoinState.MaximumPlayerEntries || !handSeats.Add( player ) )
				throw new InvalidDataException( $"The join state holds a hand of seat {player}, which is no seat of the session or holds two." );
			if ( !held.Add( staff ) )
				throw new InvalidDataException( $"The join state holds employee {staff} in two hands." );
			if ( pool?.Members.FirstOrDefault( member => member.Id == staff ) is not { State: StaffState.PickedUp } )
				throw new InvalidDataException( $"The join state holds employee {staff} in seat {player}'s hand, whom the park does not hold picked up." );
			if ( Guests?.Grid.Cells is not { } map || !map.InBounds( x, y ) )
				throw new InvalidDataException( $"The join state holds employee {staff} picked up from ({x}, {y}), outside the park's grid." );
			if ( player != ParkCommands.LocalPlayer && hireSeats.Contains( player ) )
				throw new InvalidDataException( $"The join state holds both an employee in hand and a pending hire for seat {player}, a guest's seat, which holds one at most." );
		}
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] Whether a player holds a coaster drag (a join capture waits for its release). It reads the drags without making their set, which the session digest
	/// would otherwise count on this machine alone.
	/// </summary>
	internal bool CoasterDragHeld => coasterEdits is { IsEmpty: false };

	/// <summary>Each coaster's marker cells its binding wants and does not hold, by the station's attraction id, in order (<see cref="CoasterCellMarkers.UnheldCells"/>).</summary>
	private (int Station, int X, int Y, bool Displaced)[] CoasterUnheldCells() => coasterBuilders?.Where( pair => !pair.Key.IsDeleted && pair.Value.Track.CellMarkers != null )
		.OrderBy( pair => pair.Key.Runtime.AttractionId )
		.SelectMany( pair => pair.Value.Track.CellMarkers!.UnheldCells().Select( cell => (pair.Key.Runtime.AttractionId, cell.X, cell.Y, cell.Displaced) ) )
		.ToArray() ?? Array.Empty<(int, int, int, bool)>();

	/// <summary>The placed coaster stations with their controllers, as the coaster save joins them (<see cref="CoasterSave.Capture(CoasterWorld, ParkObjects)"/>).</summary>
	private IEnumerable<(int AttractionId, CoasterRide Ride)> CoasterStations() => Objects?.Objects.Where( item => item.Runtime?.Coaster is not null )
		.Select( item => (item.Runtime.AttractionId, item.Runtime.Coaster!) ) ?? Enumerable.Empty<(int, CoasterRide)>();
}
