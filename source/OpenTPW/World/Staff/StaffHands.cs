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
	private readonly Func<StaffWorldSimulation?>? world;
	private readonly Dictionary<int, (int Staff, int X, int Y)> carried = new();

	/// <param name="occupied">The cells besides what the cell types refuse that no employee may be put on (<see cref="StaffPlacementRules.IsValidDropCell"/>).</param>
	/// <param name="world">The level's staff world, whose role searches a new employee's constructor runs (<see cref="PlaceStaffCommand"/>); null: a headless economy, whose members search on their first update.</param>
	public StaffHands( Func<ParkEconomy?> economy, Func<ParkCellMap?> cells, Func<int, int, bool> occupied, Func<StaffWorldSimulation?>? world = null )
	{
		this.world = world;
		this.economy = economy ?? throw new ArgumentNullException( nameof( economy ) );
		this.cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
		this.occupied = occupied ?? throw new ArgumentNullException( nameof( occupied ) );
	}

	/// <summary>The park's economy, or null.</summary>
	public ParkEconomy? Economy => economy();
	/// <summary>The park's cell map, or null.</summary>
	public ParkCellMap? Cells => cells();
	/// <summary>The level's staff world, or null.</summary>
	internal StaffWorldSimulation? StaffWorld => world?.Invoke();

	/// <summary>Whether an employee may be put down on (<paramref name="x"/>, <paramref name="y"/>) now.</summary>
	public bool IsValidCell( int x, int y ) => cells() is { } map && StaffPlacementRules.IsValidDropCell( map, x, y, occupied );

	/// <summary>The employee <paramref name="player"/> holds and the cell it came from, or null.</summary>
	public (int Staff, int X, int Y)? Of( int player ) => carried.TryGetValue( player, out var hand ) ? hand : null;

	/// <summary>[EXT:online-coop-snapshot-join] Every hand, lowest player first: the employee and the cell it came from (a join snapshot carries them).</summary>
	internal IEnumerable<(int Player, int Staff, int X, int Y)> All => carried.OrderBy( pair => pair.Key ).Select( pair => (pair.Key, pair.Value.Staff, pair.Value.X, pair.Value.Y) );

	/// <summary>Whether a player holds employee <paramref name="staff"/>.</summary>
	public bool IsCarried( int staff ) => carried.Values.Any( hand => hand.Staff == staff );

	/// <summary>[EXT:online-coop-commands] The player who holds employee <paramref name="staff"/>, or null (C4c: another seat's employee in hand is not fired).</summary>
	public int? HolderOf( int staff )
	{
		foreach ( var (player, hand) in carried )
			if ( hand.Staff == staff )
				return player;
		return null;
	}

	/// <summary>
	/// [EXT:online-coop-commands] Drops <paramref name="player"/>'s employee back on the cell it was picked up from, as the drop on that cell does (<see cref="DropStaffCommand"/>): the hand
	/// empties and the employee, if it is still employed, leaves its jobs and stands there idle (C4c: also a seat's release, its staff right taken back, and a guest's hire or pick-up that
	/// replaces what its hand held). Returns the employee put down; null without a hand or when the employee is no longer employed.
	/// </summary>
	internal StaffMember? ReturnToOrigin( int player )
	{
		if ( Of( player ) is not { } hand )
			return null;
		Empty( player );
		// an employee dismissed while in hand leaves nothing to put down
		if ( economy() is not { } park || park.Staff.Members.FirstOrDefault( item => item.Id == hand.Staff ) is not { } member )
			return null;
		StaffPlacement.ReleaseAssignments( park, member );
		StaffPlacement.PutDown( member, hand.X, hand.Y );
		return member;
	}

	/// <summary>Puts employee <paramref name="staff"/>, picked up from <paramref name="origin"/>, in <paramref name="player"/>'s hand.</summary>
	internal void Carry( int player, int staff, (int X, int Y) origin ) => carried[player] = (staff, origin.X, origin.Y);

	/// <summary>Empties <paramref name="player"/>'s hand.</summary>
	internal void Empty( int player ) => carried.Remove( player );

	/// <summary>
	/// [EXT:online-coop-commands] Puts the employees in the hands <paramref name="which"/> selects down where they were picked up, as their drop back would, for a park save's captures, until
	/// the returned scope ends (C4c): both captures that hold an employee's walk (the economy's members and the staff world's actors) read them dropped, and the scope then gives each
	/// member back every field the put-down wrote (its off-grid leg the list it had), also when a capture throws. The hands stay as they are. Null when no such hand holds an employee.
	/// </summary>
	internal IDisposable? PutDownForSave( Func<int, bool> which )
	{
		if ( economy() is not { } park )
			return null;
		var walks = new List<HeldWalk>();
		try
		{
			foreach ( var (player, hand) in carried.OrderBy( pair => pair.Key ) )
			{
				if ( !which( player ) || park.Staff.Members.FirstOrDefault( item => item.Id == hand.Staff ) is not { } member )
					continue;
				walks.Add( HeldWalk.Of( member ) );
				// the put-down's clear empties a list of its own: the member gets its own back
				member.OffGridLeg = new();
				StaffPlacement.PutDown( member, hand.X, hand.Y );
			}
		}
		catch
		{
			foreach ( var walk in walks )
				walk.Restore();
			throw;
		}
		return walks.Count == 0 ? null : new HeldWalks( walks );
	}

	/// <summary>The fields of a held employee's walk that a put-down writes (<see cref="StaffPlacement.PutDown"/>), to give back after a save's captures.</summary>
	private sealed record HeldWalk( StaffMember Member, float? CellX, float? CellY, double TravelSeconds, GuestNavigation? Navigation, StaffState State, int SpeedBonus, StaffJob Job,
		long IdleStamp, List<(int X, int Y, int ByteX, int ByteY)> OffGridLeg )
	{
		public static HeldWalk Of( StaffMember member ) => new( member, member.CellX, member.CellY, member.TravelSeconds, member.Navigation, member.State, member.SpeedBonus, member.Job,
			member.IdleStamp, member.OffGridLeg );

		public void Restore()
		{
			Member.CellX = CellX;
			Member.CellY = CellY;
			Member.TravelSeconds = TravelSeconds;
			Member.Navigation = Navigation;
			Member.State = State;
			Member.SpeedBonus = SpeedBonus;
			Member.Job = Job;
			Member.IdleStamp = IdleStamp;
			Member.OffGridLeg = OffGridLeg;
		}
	}

	private sealed class HeldWalks( List<HeldWalk> walks ) : IDisposable
	{
		public void Dispose()
		{
			foreach ( var walk in walks )
				walk.Restore();
			walks.Clear();
		}
	}

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
