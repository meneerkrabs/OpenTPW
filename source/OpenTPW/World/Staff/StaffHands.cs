namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] What the staff commands act on (<see cref="StaffToolCommand"/>): the park's economy, its cells and the cells objects block, and the employee each player holds in
/// hand with the cell it was picked up from. The hands are the level's state, not a tool's: a pick-up a replay or a peer applies stays picked up (<see cref="StaffPlacement.Reconcile"/>),
/// and a cancel drops the employee back where its own pick-up found it.
/// </summary>
public sealed class StaffHands
{
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<ParkCellMap?> cells;
	private readonly Func<int, int, bool> occupied;
	private readonly Dictionary<int, (int Staff, int X, int Y)> carried = new();

	/// <param name="occupied">The cells besides what the cell types refuse that no employee may be put on (<see cref="StaffPlacementRules.IsValidDropCell"/>).</param>
	public StaffHands( Func<ParkEconomy?> economy, Func<ParkCellMap?> cells, Func<int, int, bool> occupied )
	{
		this.economy = economy ?? throw new ArgumentNullException( nameof( economy ) );
		this.cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
		this.occupied = occupied ?? throw new ArgumentNullException( nameof( occupied ) );
	}

	/// <summary>The park's economy, or null.</summary>
	public ParkEconomy? Economy => economy();
	/// <summary>The park's cell map, or null.</summary>
	public ParkCellMap? Cells => cells();

	/// <summary>Whether an employee may be put down on (<paramref name="x"/>, <paramref name="y"/>) now.</summary>
	public bool IsValidCell( int x, int y ) => cells() is { } map && StaffPlacementRules.IsValidDropCell( map, x, y, occupied );

	/// <summary>The employee <paramref name="player"/> holds and the cell it came from, or null.</summary>
	public (int Staff, int X, int Y)? Of( int player ) => carried.TryGetValue( player, out var hand ) ? hand : null;

	/// <summary>[EXT:online-coop-snapshot-join] Every hand, lowest player first: the employee and the cell it came from (a join snapshot carries them).</summary>
	internal IEnumerable<(int Player, int Staff, int X, int Y)> All => carried.OrderBy( pair => pair.Key ).Select( pair => (pair.Key, pair.Value.Staff, pair.Value.X, pair.Value.Y) );

	/// <summary>Whether a player holds employee <paramref name="staff"/>.</summary>
	public bool IsCarried( int staff ) => carried.Values.Any( hand => hand.Staff == staff );

	/// <summary>Puts employee <paramref name="staff"/>, picked up from <paramref name="origin"/>, in <paramref name="player"/>'s hand.</summary>
	internal void Carry( int player, int staff, (int X, int Y) origin ) => carried[player] = (staff, origin.X, origin.Y);

	/// <summary>Empties <paramref name="player"/>'s hand.</summary>
	internal void Empty( int player ) => carried.Remove( player );

	/// <summary>Empties every hand (a load replaced the economy, whose employees they held).</summary>
	public void Clear() => carried.Clear();

	/// <summary>[EXT:online-coop-lockstep] Every hand, lowest player first, for the session digest: later commands read them on every peer.</summary>
	internal void AddSessionState( StateHasher hash )
	{
		hash.Add( carried.Count );
		foreach ( var (player, hand) in carried.OrderBy( pair => pair.Key ) )
		{
			hash.Add( player );
			hash.Add( hand.Staff );
			hash.Add( hand.X );
			hash.Add( hand.Y );
		}
	}

	/// <summary>Applies a staff command to these hands directly, outside a level's command funnel (the tool's tests); any other command is refused.</summary>
	public ParkCommandResult Apply( ParkCommand command ) =>
		command is StaffToolCommand staff ? staff.Apply( this ) : ParkCommandResult.Rejected( $"{command.Kind} is no staff command." );
}
