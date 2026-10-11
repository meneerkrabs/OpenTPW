namespace OpenTPW;

/// <summary>An actual employee actor, independent of the position of its assigned job.</summary>
public readonly record struct StaffWorldSnapshot( int Id, StaffType Type, float CellX, float CellY, bool Travelling, bool JobReachable );

/// <summary>
/// Host presentation policy, not recovered native staff movement: new employees enter at the supplied
/// gate path (otherwise the first path in row order), and move one connected cell per second towards
/// their existing assigned job. Idle employees stay where they are. This never changes job timers,
/// finance, research or staff states. Exact native sub-cell positions survive import and load;
/// off-path actors remain there rather than teleporting to a convenient job or path.
/// </summary>
public sealed class StaffWorldSimulation
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
			if ( member.CellX == null && member.CellY == null && spawn is { } cell )
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

	public IReadOnlyList<StaffWorldSnapshot> Snapshots => members().Where( member => member.CellX.HasValue && member.CellY.HasValue )
		.Select( member =>
		{
			var reachable = CanTravel( member, out var target, out var x, out var y );
			return new StaffWorldSnapshot( member.Id, member.Type, member.CellX!.Value, member.CellY!.Value,
				reachable && (x, y) != target, reachable );
		} ).ToArray();

	/// <summary>Called only by the fixed simulation clock, using its already speed-scaled delta.</summary>
	public void Tick( float seconds )
	{
		if ( !float.IsFinite( seconds ) || seconds < 0 ) throw new ArgumentOutOfRangeException( nameof( seconds ) );
		Synchronize();
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
}
