namespace OpenTPW;

/// <summary>
/// The restored advice extension's view of a running park (<see cref="IRestoredAdviceState"/>): the economy, and the level's guests and visitor bridges when there is a level. It only reads: nothing it
/// computes is stored in the park, a save or the simulation hash, and a fact that needs the level (queues, riders, exits, guests) is 0 without one.
/// </summary>
/// <remarks>
/// The three new trackings are here. <b>Security cover</b> (<see cref="SecurityCoverage"/>) joins the environment grid's security word (cameras and guards where they stand, <see cref="ParkEnvironment"/>, the
/// camera ticket's test <see cref="ParkEconomy.SecurityCoveragePercent"/>) with the patrol areas of the guards who have one, over the path and queue cells. <b>Blocked exits</b> (<see cref="ExitBlocked"/>) is a ride with a
/// queue whose exit's outside cell, laid as a path by the placement (<see cref="ObjectAccessCells.Lay"/>), is no path now. <b>Riders stuck</b> is the ride's state (<see cref="ParkEconomy.ObjectState"/>) with the guests aboard
/// (<see cref="RideVisitorBridge.RidersOnBoard"/>, <see cref="RideVisitorBridge.Riders"/>).
/// </remarks>
// [EXT:restored-advice-security] [EXT:restored-advice-exits] [EXT:restored-advice-stranded] OpenTPW's own tracking; tp.exe computes none of it (its callbacks are stubs, 0x005D60C0)
internal sealed class ParkRestoredAdviceState : IRestoredAdviceState
{
	private readonly ParkEconomy economy;
	private readonly Level? level;

	public ParkRestoredAdviceState( ParkEconomy economy, Level? level )
	{
		this.economy = economy ?? throw new ArgumentNullException( nameof( economy ) );
		this.level = level;
	}

	// [BIN:TPW-EXE:0x004F8800 park time] the clock divided by 30 days, as EconomyMoneyState reads it
	public int ParkMonths => (int)Math.Min( int.MaxValue, ParkCalendar.Seconds( economy.Tick ) / ParkEconomy.SecondsPerMonthInRed );
	public int HiredGuards => economy.Staff.OfType( StaffType.Guard ).Count();
	public int HiredMechanics => economy.Staff.OfType( StaffType.Mechanic ).Count();
	public bool MechanicsOnStrike => economy.RoleOnStrike( StaffType.Mechanic );
	public bool UpgradesPossible => economy.Features.Upgrades;

	/// <inheritdoc />
	public RestoredSecurityCoverage SecurityCoverage
	{
		get
		{
			var cameras = economy.Objects.Count( item => economy.Catalog.TryGet( item.InfoId, out var info ) && info.ProvidesSecurity );
			if ( economy.Litter.Cells is not { } cells )
				return new RestoredSecurityCoverage( 0, 0, cameras );
			var patrols = economy.Staff.OfType( StaffType.Guard ).Where( guard => guard.HasPatrolArea ).Select( guard => guard.PatrolArea ).ToArray();
			var environment = economy.Litter.Environment;
			int path = 0, covered = 0;
			for ( var y = 0; y < cells.Height; y++ )
			{
				for ( var x = 0; x < cells.Width; x++ )
				{
					if ( cells.TypeAt( x, y ) is not (ParkCellType.Path or ParkCellType.Queue) )
						continue;
					path++;
					if ( environment.At( x, y ).Security > 0 || patrols.Any( area => x >= area.X0 && x <= area.X1 && y >= area.Y0 && y <= area.Y1 ) )
						covered++;
				}
			}
			return new RestoredSecurityCoverage( path, covered, cameras );
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<RestoredRide> Rides
	{
		get
		{
			var bridges = new Dictionary<int, (RideVisitorBridge? Bridge, IEnumerable<ObjectAccessPoint>? Points)>();
			if ( level is { Objects: { } placed } )
				foreach ( var item in placed.Objects )
					if ( !item.IsDeleted && item.Runtime.IsAttraction && level.GetEconomyInstance( item ) is int instance )
						bridges[instance] = (item.Runtime.Visitors, item.AccessPoints);
			var rides = new List<RestoredRide>();
			foreach ( var item in economy.Objects )
			{
				if ( item.Kind != ParkObjectKind.Ride )
					continue;
				bridges.TryGetValue( item.Id, out var found );
				rides.Add( RideFacts( item, found.Bridge, found.Points, level?.Guests?.Grid ) );
			}
			return rides;
		}
	}

	/// <summary>
	/// One ride as the restored triggers see it: the economy's state (open, broken down, condemned) joined with the visitor bridge's queue, riders and exit when the level has the ride's bridge (<paramref name="bridge"/> null: none).
	/// </summary>
	internal static RestoredRide RideFacts( ParkObjectState item, RideVisitorBridge? bridge, IEnumerable<ObjectAccessPoint>? points, GuestPathGrid? grid )
	{
		var state = ParkEconomy.ObjectState( item );
		var broken = state == RideObjectStates.BrokenDown;
		var condemned = state == RideObjectStates.Condemned;
		return bridge == null
			? new RestoredRide( item.Id, item.IsOpen, broken, condemned, 0, 0, false )
			: new RestoredRide( item.Id, item.IsOpen, broken, condemned, Math.Max( bridge.RidersOnBoard, bridge.Riders.Count ), bridge.QueueLength, ExitBlocked( bridge, points ?? Array.Empty<ObjectAccessPoint>(), grid ) );
	}

	/// <summary>
	/// Whether a ride's exit has no path: the ride has a queue (the placement lays the exit's outside cell as a path only then, <see cref="ObjectAccessCells.Lay"/>), registered cells
	/// with the guests, and its last exit's outside cell is not a path cell now.
	/// </summary>
	internal static bool ExitBlocked( RideVisitorBridge bridge, IEnumerable<ObjectAccessPoint> points, GuestPathGrid? grid )
	{
		if ( grid == null || !bridge.HasCells || !bridge.HasQueue )
			return false;
		ObjectAccessPoint? exit = null;
		foreach ( var point in points )
			if ( point.Kind == ObjectCellKind.Exit )
				exit = point;
		return exit is { } leave && !grid.IsWalkable( leave.OutsideX, leave.OutsideY );
	}

	/// <inheritdoc />
	public int UpgradesAvailable => economy.Objects.Count( item => item.Kind == ParkObjectKind.Ride && item.PendingLevel <= item.Level && economy.Catalog.TryGet( item.InfoId, out var info )
		&& item.Level + 1 < info.Upgrades.Count && economy.Research.IsAvailable( item.InfoId, item.Level + 1 ) );

	public int UpgradesBought => economy.Objects.Where( item => item.Kind == ParkObjectKind.Ride ).Sum( item => Math.Max( item.Level, item.PendingLevel ) );

	public int Toilets => economy.Toilets().Count();

	private IEnumerable<Guest> GuestsInPark => level?.Guests?.Guests.Where( guest => guest.State is >= GuestState.DoingNothing and <= GuestState.ExitingRide ) ?? Enumerable.Empty<Guest>();

	// [BIN:TPW-EXE:0x004C9290 toilet need] guests whose toilet byte is strictly above the argument
	public int GuestsNeedingToilet( int above ) => GuestsInPark.Count( guest => (int)guest.Toilet > above );

	public int GuestsFarFromToilets( int above, int cells )
	{
		var toilets = economy.Toilets().Select( toilet => toilet.EffectCell ).Where( cell => cell != null ).Select( cell => cell!.Value ).ToArray();
		if ( toilets.Length == 0 )
			return 0;
		return GuestsInPark.Count( guest => (int)guest.Toilet > above && toilets.Min( toilet => Math.Abs( toilet.X - guest.CellX ) + Math.Abs( toilet.Y - guest.CellY ) ) >= cells );
	}

	/// <inheritdoc />
	public long RetailSalesTwoMonths => economy.Objects.Where( IsRetail ).Sum( item => item.CustomersThisMonth + item.CustomersLastMonth );

	// the shops of stock type 2, 3 and 4 (food, restaurant, drinks) are the food and drink shops of the advisor's price callbacks; the others sell goods (docs/reverse/ADVISOR-SCAN.md)
	private bool IsRetail( ParkObjectState item ) => item.Kind == ParkObjectKind.Shop && economy.Catalog.TryGet( item.InfoId, out var info ) && info.ShopType is not (2 or 3 or 4);

	/// <inheritdoc />
	public long VisitorsTwoMonths
	{
		get
		{
			var closed = economy.MonthlyAdmissions;
			var thisMonth = Math.Max( 0, economy.Counters[ParkCounters.Admissions] - (economy.DroppedAdmissions + closed.Sum()) );
			return thisMonth + (closed.Count > 0 ? closed[^1] : 0);
		}
	}

	/// <inheritdoc />
	public RestoredLand Land
	{
		get
		{
			if ( economy.Litter.Cells is not { } cells )
				return default;
			int owned = 0, free = 0;
			for ( var y = 0; y < cells.Height; y++ )
			{
				for ( var x = 0; x < cells.Width; x++ )
				{
					var type = cells.RawTypeAt( x, y );
					if ( type is ParkCellTypes.Water or ParkCellTypes.NoTerrain or ParkCellTypes.FixedWalkway || cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) )
						continue;
					owned++;
					if ( type == (byte)ParkCellType.Empty )
						free++;
				}
			}
			return new RestoredLand( owned, free );
		}
	}

	public long Record( ParkRecordKind kind ) =>
		economy.Counters.Values.Where( pair => pair.Key.StartsWith( $"record:{kind}:", StringComparison.Ordinal ) ).Select( pair => pair.Value ).DefaultIfEmpty( 0 ).Max();

	public long TicketThreshold( GoldenTicketKind kind )
	{
		var rules = economy.Settings.GoldenTickets;
		return kind switch
		{
			GoldenTicketKind.CoasterHeight => rules.CoasterHeight,
			GoldenTicketKind.GokartExcitement => rules.GokartExcitement,
			GoldenTicketKind.WaterLength => rules.WaterLength,
			GoldenTicketKind.ProfitYear => rules.ProfitYear,
			_ => 0,
		};
	}

	public bool HoldsTicket( GoldenTicketKind kind ) => economy.Objectives.GoldenTickets.Contains( kind );
	public long ProfitThisYear => economy.Ledger.ProfitThisYear;
	public long MonthlyWages => economy.Rules.Staff ? economy.Staff.TotalMonthlyWages : 0;
}
