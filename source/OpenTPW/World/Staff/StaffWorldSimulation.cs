namespace OpenTPW;

/// <summary>An actual employee actor, independent of the position of its assigned job.</summary>
public readonly record struct StaffWorldSnapshot( int Id, StaffType Type, float CellX, float CellY, bool Travelling, bool JobReachable );

/// <summary>
/// Host presentation policy, not recovered native staff movement: new employees enter at the supplied
/// gate path (otherwise the first path in row order), and move one connected cell per second towards
/// their existing assigned job. Idle employees stay where they are. Movement does not alter job timers,
/// finance, research or staff states. Exact native sub-cell positions survive import and load;
/// off-path actors remain there rather than teleporting to a convenient job or path.
/// </summary>
public sealed partial class StaffWorldSimulation : IMechanicJobWorld, IStaffWorld
{
	public const double SecondsPerCell = 1;
	private readonly GuestPathGrid grid;
	private readonly Func<IReadOnlyList<StaffMember>> members;
	private readonly Func<int, (int X, int Y)?> jobAccess;
	private readonly (int X, int Y)? gate;

	public StaffWorldSimulation( GuestPathGrid grid, Func<IReadOnlyList<StaffMember>> members,
		Func<int, (int X, int Y)?> jobAccess, (int X, int Y)? gate = null )
	{
		this.grid = grid;
		this.members = members;
		this.jobAccess = jobAccess;
		this.gate = gate;
		Synchronize();
	}

	/// <summary>Reflects hire/fire/load even while paused; no movement happens here.</summary>
	public void Synchronize()
	{
		var spawn = gate is { } entry && grid.IsWalkable( entry.X, entry.Y ) ? gate : FirstPath();
		foreach ( var member in members() )
			// a member in the player's hand without a position waits for the drop; the gate spawn is for the others (staff placement, docs/STAFF-PLACEMENT.md)
			if ( member.CellX == null && member.CellY == null && member.State != StaffState.PickedUp && spawn is { } cell )
			{
				member.CellX = cell.X + 0.5f;
				member.CellY = cell.Y + 0.5f;
			}
	}

	private (int X, int Y)? FirstPath()
	{
		for ( var y = 0; y < grid.CountY; y++ )
			for ( var x = 0; x < grid.CountX; x++ )
				if ( grid.IsWalkable( x, y ) ) return (x, y);
		return null;
	}

	private bool CanTravel( StaffMember member, out (int X, int Y) target, out int x, out int y )
	{
		target = default;
		x = member.CellX is { } px ? (int)MathF.Floor( px ) : -1;
		y = member.CellY is { } py ? (int)MathF.Floor( py ) : -1;
		if ( member.CellX == null || member.CellY == null || member.AssignedInstanceId == 0
			|| member.State is StaffState.PickedUp or StaffState.OnStrike or StaffState.Resting
			|| jobAccess( member.AssignedInstanceId ) is not { } access || !grid.InBounds( access.X, access.Y ) )
			return false;
		target = access;
		return grid.Distance( x, y, target.X, target.Y ) >= 0;
	}

	/// <summary>A positioned member must stand on a path cell to be given a job; an unpositioned one is placed first.</summary>
	public bool CanStartJobs( StaffMember member ) =>
		member.CellX is not { } x || member.CellY is not { } y || grid.IsWalkable( (int)MathF.Floor( x ), (int)MathF.Floor( y ) );

	/// <summary>Only this world's current employee at its reachable assigned access cell has arrived.</summary>
	public bool HasArrived( StaffMember member ) => members().Any( current => ReferenceEquals( current, member ) )
		&& CanTravel( member, out var target, out var x, out var y ) && (x, y) == target;

	public IReadOnlyList<StaffWorldSnapshot> Snapshots => members().Where( member => member.CellX.HasValue && member.CellY.HasValue )
		.Select( member =>
		{
			if ( member.Navigation is { } navigation )
				return new StaffWorldSnapshot( member.Id, member.Type, member.CellX!.Value, member.CellY!.Value,
					member.Job is StaffJob.Patrol or StaffJob.ToLitter or StaffJob.ToToilet or StaffJob.Chasing or StaffJob.Escorting or StaffJob.ReturningFromEscort
						or StaffJob.ToRestArea or StaffJob.ToStrike or StaffJob.ToRide && !navigation.Arrived, true );
			var reachable = CanTravel( member, out var target, out var x, out var y );
			return new StaffWorldSnapshot( member.Id, member.Type, member.CellX!.Value, member.CellY!.Value,
				reachable && (x, y) != target, reachable );
		} ).ToArray();

	/// <summary>Called only by the fixed simulation clock, using its already speed-scaled delta.</summary>
	public void Tick( float seconds )
	{
		if ( !float.IsFinite( seconds ) || seconds < 0 ) throw new ArgumentOutOfRangeException( nameof( seconds ) );
		Synchronize();
		ownTicks++;
		foreach ( var member in members() )
			UpdatePresentation( member );
		foreach ( var member in members() )
		{
			if ( seconds == 0 || !CanTravel( member, out var target, out var x, out var y ) || (x, y) == target ) continue;
			member.TravelSeconds += seconds;
			while ( member.TravelSeconds >= SecondsPerCell )
			{
				member.TravelSeconds -= SecondsPerCell;
				if ( !grid.TryStep( x, y, target.X, target.Y, out x, out y ) ) break;
				member.CellX = x + 0.5f;
				member.CellY = y + 0.5f;
				if ( (x, y) == target ) { member.TravelSeconds = 0; break; }
			}
		}
	}

	/// <summary>One staff actor's world position and movement phase for a park save.</summary>
	/// <param name="CellX">Cell-unit position, null while the actor has not been attached to a world.</param>
	/// <param name="TravelSeconds">Fraction of the next one-cell movement interval (<see cref="StaffMember.TravelSeconds"/>).</param>
	/// <param name="CellSerial">The entry number the actor's thing holds on its cell's chain (WALK-003, <see cref="StaffCellChain"/>); 0 in a save made before the staff were on the chains, which files them oldest.</param>
	public sealed record StaffActorSnapshot( int Id, float? CellX, float? CellY, double TravelSeconds, long CellSerial = 0 );

	/// <summary>
	/// The staff actors' positions for a park save: one record per employee, in hire order. Restoring writes
	/// the saved positions and movement phases back into the restored economy's employees, so travelling
	/// actors resume mid-interval instead of restarting the cell step; job assignment and states come from
	/// the economy's own save.
	/// </summary>
	public sealed record StaffWorldStateSnapshot( IReadOnlyList<StaffActorSnapshot> Actors );

	/// <summary>The actors' positions and movement phases for a park save, in member order.</summary>
	public StaffWorldStateSnapshot CaptureSnapshot() => new( members()
		.Select( member => new StaffActorSnapshot( member.Id, member.CellX, member.CellY, member.TravelSeconds, Chain.Of( member.Id )?.Serial ?? member.CellSerial ) )
		.ToArray() );

	/// <summary>
	/// Checks a snapshot against this world's employee list and grid without touching either: every actor
	/// must name one current employee exactly once, positions must be finite, paired and inside the grid
	/// (off-path positions are legal and stay where they are), and a travel fraction is one interval at most.
	/// </summary>
	public void ValidateSnapshot( StaffWorldStateSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( snapshot.Actors is null )
			throw new InvalidDataException( "Staff world snapshot is inconsistent: missing actor records." );
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"Staff world snapshot is inconsistent: {message}." );
		}
		var ids = new HashSet<int>();
		foreach ( var actor in snapshot.Actors )
		{
			Require( actor is not null, "an actor record is missing" );
			Require( ids.Add( actor.Id ), $"duplicate actor {actor.Id}" );
			Require( actor.CellX.HasValue == actor.CellY.HasValue, $"actor {actor.Id} has one position component only" );
			if ( actor.CellX is { } x && actor.CellY is { } y )
				Require( float.IsFinite( x ) && float.IsFinite( y ) && x >= 0 && x < grid.CountX && y >= 0 && y < grid.CountY,
					$"actor {actor.Id} stands outside the grid" );
			Require( double.IsFinite( actor.TravelSeconds ) && actor.TravelSeconds >= 0 && actor.TravelSeconds < SecondsPerCell,
				$"actor {actor.Id} travel fraction outside one interval" );
			Require( actor.CellSerial >= 0, $"actor {actor.Id} has a negative chain entry number" );
		}
		var current = members().Select( member => member.Id ).ToHashSet();
		Require( ids.SetEquals( current ), "the snapshot's actors must be exactly the current employees" );
	}

	/// <summary>
	/// Writes a validated snapshot's positions and movement phases into the matching employees. Call after
	/// the economy's staff are restored and this world is constructed: <see cref="Synchronize"/> has already
	/// spawned unpositioned employees at the gate, and the saved positions (including saved nulls) replace that.
	/// </summary>
	public void RestoreSnapshot( StaffWorldStateSnapshot snapshot )
	{
		ValidateSnapshot( snapshot );
		foreach ( var actor in snapshot.Actors )
		{
			var member = members().Single( item => item.Id == actor.Id );
			member.CellX = actor.CellX;
			member.CellY = actor.CellY;
			member.TravelSeconds = actor.TravelSeconds;
			member.CellSerial = actor.CellSerial;
		}
		// the members' things are filed again from the saved entry numbers; a save without them files the members oldest, first member head-most (the order the staff stood after the guests in)
		Chain.Clear();
		var legacy = 0;
		foreach ( var member in members() )
			if ( member.CellSerial == 0 )
				member.CellSerial = --legacy;
		SyncChain();
	}
}
