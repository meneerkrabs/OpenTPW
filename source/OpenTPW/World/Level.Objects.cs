namespace OpenTPW;

/// <summary>Original objects of the level: catalog, placed objects, build/remove tools.</summary>
public partial class Level
{
	/// <summary>Placed original objects (imported or built) with their build rules.</summary>
	public ParkObjects Objects { get; private set; } = null!;
	/// <summary>Catalog entry the build tool places, or null.</summary>
	public ObjectCatalogEntry? BuildEntry { get; set; }
	/// <summary>Rotation in degrees (0/90/180/270) for the next built object.</summary>
	public int BuildRotation { get; private set; }
	/// <summary>Clicking removes the object under the cursor.</summary>
	public bool IsRemovingObjects { get; set; }

	private void SetupObjects()
	{
		var theme = OriginalPark?.LevelName ?? "jungle";
		IParkGrid grid = OriginalPark == null ? new SandboxParkGrid() : new OriginalParkGrid( OriginalPark );
		var easy = Park?.Economy.Settings.IsEasy ?? false;
		// Objects keep off built path and queue cells; imported path cells are refused by the terrain rule as before.
		Objects = new ParkObjects( ObjectCatalog.Load( theme, easy ), grid, Seed ) { IsReserved = ( x, y ) => IsReservedByPrototype( x, y ) || Guests?.Grid.IsQueue( x, y ) == true || Guests?.Grid.IsWalkable( x, y ) == true };
		if ( OriginalPark == null )
			return;
		var count = OriginalPark.Save != null ? Objects.ImportOriginal( OriginalPark.Save ) : Objects.AddDefaultFixedItems();
		Log.Trace( $"Original {theme} objects: {count} placed with their original models and scripts ({Objects.Catalog.Entries.Count} catalog entries)." );
	}

	/// <summary>Every placed object that guests may choose becomes a live attraction; new and removed objects follow.</summary>
	private void ConnectObjectsToGuests()
	{
		if ( Guests == null )
			return;
		foreach ( var item in Objects.Objects )
			RegisterObjectWithGuests( item );
		Objects.ObjectPlaced += RegisterObjectWithGuests;
		Objects.ObjectRemoved += item => Guests?.Unregister( item.Visitors );
	}

	/// <summary>
	/// The queue's front cell is the cell outside the object's entrance (shape '2'); guests reappear outside its
	/// exit (shape 'S'/'N'/'E', else the entrance). Until a queue path is built there (and while that cell is
	/// not a walkable path), the nearest walkable cell serves as a one-cell queue.
	/// </summary>
	private void RegisterObjectWithGuests( OriginalObject item )
	{
		if ( Guests != null )
			RegisterWithGuests( item.Runtime, item.AccessPoints, Guests );
	}

	/// <summary>
	/// Registers a placed object's visitor bridge with <paramref name="guests"/> the way the build flow does
	/// (also used by the headless M3 gate): attractions only, cells from <see cref="ResolveVisitorCells"/>.
	/// </summary>
	internal static bool RegisterWithGuests( OriginalObjectRuntime runtime, IEnumerable<ObjectAccessPoint> accessPoints, GuestSimulation guests )
	{
		if ( !runtime.IsAttraction || ResolveVisitorCells( accessPoints, guests.Grid ) is not { } cells )
			return false;
		runtime.Visitors.EntranceCell = cells.Entrance;
		runtime.Visitors.ExitCell = cells.Exit;
		runtime.Visitors.QueueFrontCell = cells.QueueFront;
		runtime.Visitors.QueueEntranceDirection = cells.QueueEntranceDirection;
		runtime.Visitors.HasCells = true;
		guests.Register( runtime.Visitors );
		return true;
	}

	/// <summary>
	/// Walkable entrance and exit cells for an object's access points, plus the entrance's outside cell (the queue's
	/// front cell) and the direction from it into the entrance; null when it has no entrance or the grid no path.
	/// </summary>
	internal static ((int X, int Y) Entrance, (int X, int Y) Exit, (int X, int Y) QueueFront, int QueueEntranceDirection)? ResolveVisitorCells( IEnumerable<ObjectAccessPoint> accessPoints, GuestPathGrid grid )
	{
		var points = accessPoints.ToArray();
		var entrance = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Entrance );
		if ( entrance == default )
			return null;
		var exit = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Exit );
		if ( exit == default )
			exit = entrance;
		var entranceCell = WalkableNear( grid, entrance.OutsideX, entrance.OutsideY );
		var exitCell = WalkableNear( grid, exit.OutsideX, exit.OutsideY );
		if ( entranceCell == null || exitCell == null )
			return null;
		return (entranceCell.Value, exitCell.Value, (entrance.OutsideX, entrance.OutsideY),
			GuestPathGrid.DirectionBetween( entrance.OutsideX, entrance.OutsideY, entrance.X, entrance.Y ));
	}

	/// <summary>The headless path builder over the level's cell map (original levels; null in the generic sandbox).</summary>
	public ParkPathBuilder? Paths { get; private set; }
	/// <summary>The path and queue build tool the park view drives (PATH-plan §9.4).</summary>
	public CellBuildTool CellTool { get; } = new();

	/// <summary>The attraction whose queue the build tool lays (set after building a ride with <c>Info.HasQueue</c>).</summary>
	public RideVisitorBridge? QueueToolRide
	{
		get => CellTool.Writer is QueueLineWriter queue ? queue.Ride : null;
		set
		{
			if ( value == null )
			{
				if ( CellTool.Mode == CellToolMode.Queue )
					CellTool.Cancel();
			}
			else
				EnterQueueTool( value );
		}
	}

	private void SetupPaths()
	{
		if ( Guests == null )
			return;
		Paths = new ParkPathBuilder( Guests.Grid.Cells, Guests.Grid, () => Park?.Economy, Objects.Grid, IsPathBlocked );
	}

	// Objects, fixed items (the gate and entrance included) and the sandbox Totem block paths like their original cell types do.
	private bool IsPathBlocked( int x, int y ) => Objects.FindAt( x, y ) != null || IsReservedByPrototype( x, y );

	/// <summary>Starts the path tool at (x, y) (a click in the park view on an empty owned cell or a path cell).</summary>
	// [BIN:STP-PPC:0x10139C64 park-view hover] empty cell: hover state 2, help 441 "build path"; path (type 1): state 1, help 442 "extend this path"; queue (type 3): help 444
	// [APPROX:PATH-001] a left click on an empty owned cell or on a path cell enters mode 1 with that cell as the start; the click handler that calls SetMode(1) is not traced and no menu button exists — evidence needed: the caller of SetMode 0x1007B320 with mode 1
	public bool EnterPathTool( int x, int y )
	{
		if ( IsReadOnlyVisit || Paths == null )
			return false;
		var check = Paths.CheckCell( x, y );
		if ( check is not (CellBuildResult.Ok or CellBuildResult.Existing) )
		{
			LastActionMessage = $"Cannot build a path here: {check}.";
			return false;
		}
		CellTool.Enter( Paths, (x, y) );
		return true;
	}

	/// <summary>Starts the queue tool for <paramref name="ride"/> at its entrance's outside cell (or the queue's back cell).</summary>
	// [BIN:STP-PPC:0x1007497C ride placement] a placed HasQueue ride enters tool mode 3, the queue tool (PATH-plan §8); it shares LayLine, the vertex stack and the preview/commit code with the path tool
	public bool EnterQueueTool( RideVisitorBridge ride )
	{
		if ( IsReadOnlyVisit || Guests == null || ride.QueueFrontCell is not { } front )
			return false;
		ride.RecomputeQueue( Guests.Grid );
		var start = Guests.Grid.IsQueue( front.X, front.Y ) ? ride.QueueBackCell : front;
		CellTool.Enter( new QueueLineWriter( Guests.Grid, ride, ( x, y ) => QueuePaths.CheckExtend( Guests.Grid, ride, x, y, IsQueueBlocked ),
			( x, y ) => BuildQueueCell( ride, x, y ), RemoveQueueCell, () => Park?.Economy.CellCost( CellPurchase.Queue ) ?? 0 ), start );
		return true;
	}

	/// <summary>
	/// Lays the snapped straight path from <paramref name="from"/> toward <paramref name="to"/> (headless API; the
	/// path tool commits through the same builder). Read-only visits and the generic sandbox lay nothing.
	/// </summary>
	public SegmentResult? BuildPath( (int X, int Y) from, (int X, int Y) to )
	{
		if ( IsReadOnlyVisit || Paths == null )
		{
			LastActionMessage = "Read-only visit: path changes are disabled.";
			return null;
		}
		var result = Paths.BuildSegment( from, to );
		LastActionMessage = result.Completed
			? $"Built {result.Built.Count} path cells for ${result.Charged}."
			: $"Cannot build a path here: {result.StoppedBy}.";
		return result;
	}

	/// <summary>Removes the path cell at (x, y) (forced, no refund; fixed InitialPath cells stay).</summary>
	public CellBuildResult RemovePathCell( int x, int y )
	{
		if ( IsReadOnlyVisit || Paths == null )
			return CellBuildResult.NotPath;
		var result = Paths.Remove( x, y );
		if ( result == CellBuildResult.Ok )
			LastActionMessage = "Removed a path cell.";
		return result;
	}

	/// <summary>
	/// Adds one queue cell for an attraction (headless API; the queue tool calls it per click). The park
	/// economy charges <c>Costs.QueueCell</c>; read-only visits and object/terrain-blocked cells are refused.
	/// </summary>
	public QueueBuildResult BuildQueueCell( RideVisitorBridge ride, int x, int y )
	{
		if ( IsReadOnlyVisit )
		{
			LastActionMessage = "Read-only visit: queue changes are disabled.";
			return QueueBuildResult.Refused;
		}
		if ( Guests == null )
			return QueueBuildResult.Refused;
		var result = BuildQueueCell( Guests.Grid, Park?.Economy, ride, x, y, IsQueueBlocked, out var message );
		LastActionMessage = message;
		return result;
	}

	/// <summary>
	/// The queue tool's per-cell build (also used by the headless M3 gate): recompute the queue,
	/// <see cref="QueuePaths.CheckExtend"/>, then <c>Costs.QueueCell</c> charged through <paramref name="economy"/>
	/// (none: free), then <see cref="QueuePaths.TryExtend(GuestPathGrid, RideVisitorBridge, int, int, Func{int, int, bool}?)"/>.
	/// </summary>
	internal static QueueBuildResult BuildQueueCell( GuestPathGrid grid, ParkEconomy? economy, RideVisitorBridge ride, int x, int y, Func<int, int, bool> isBlocked, out string message )
	{
		// Bring the queue up to date before charging: a grid edit since the last recompute would otherwise pass the
		// check here, be charged, and then be refused by TryExtend's own recompute.
		ride.RecomputeQueue( grid );
		var check = QueuePaths.CheckExtend( grid, ride, x, y, isBlocked );
		if ( check != QueueBuildResult.Ok )
		{
			message = $"Cannot build a queue here: {check}.";
			return check;
		}
		// [DATA:Standard.sam:Costs.QueueCell] charged per cell when written (ParkEconomy.TrySpendCell, PATH-plan §3.2)
		if ( economy != null && economy.TrySpendCell( CellPurchase.Queue ) != ParkEconomy.PurchaseResult.Ok )
		{
			message = "Cannot build a queue: not enough money.";
			return QueueBuildResult.Refused;
		}
		var result = QueuePaths.TryExtend( grid, ride, x, y, isBlocked );
		message = $"{ride.Name}: queue is now {ride.QueueSizeInCells} cells long.";
		return result;
	}

	/// <summary>
	/// Removes the queue cell at (x, y) and every cell behind it. Each removed cell refunds
	/// <c>Costs.QueueCell</c> × the ride's scrap percentage / 100 (<see cref="ParkEconomy.RefundQueueCell"/>).
	/// </summary>
	public int RemoveQueueCell( int x, int y )
	{
		if ( IsReadOnlyVisit || Guests == null )
			return 0;
		var ride = Guests.Grid.IsQueue( x, y ) ? Guests.Attractions.OfType<RideVisitorBridge>().FirstOrDefault( item => item.QueueCells.Contains( (x, y) ) ) : null;
		if ( ride == null )
			return 0;
		var removed = QueuePaths.RemoveFrom( Guests.Grid, ride, x, y );
		var owner = Objects.Objects.FirstOrDefault( item => item.Visitors == ride );
		if ( Park != null && owner != null && GetEconomyInstance( owner ) is int instance )
		{
			for ( var cell = 0; cell < removed; cell++ )
				Park.Economy.RefundQueueCell( instance );
		}
		return removed;
	}

	// [APPROX:QUEUE-012] queue cells need terrain the object build rule allows and no object footprint — evidence needed: the queue tool's placement validity in 0x10070B98..0x1008C7C0
	private bool IsQueueBlocked( int x, int y ) =>
		Objects.Grid.CheckTerrain( x, y ) != OriginalPlacementResult.Allowed || Objects.FindAt( x, y ) != null || IsReservedByPrototype( x, y );

	// [APPROX:RIDES-028] A non-walkable outside cell (queue area) is replaced by the nearest walkable path cell — evidence needed: original queue-path building/joining rules
	private static (int X, int Y)? WalkableNear( GuestPathGrid grid, int x, int y ) =>
		grid.IsWalkable( x, y ) ? (x, y) : FindRideEntrance( grid, x, y, x, y );

	public void RotateBuild() => BuildRotation = (BuildRotation + 90) % 360;

	/// <summary>Grid cell under the cursor (sandbox tiles or original cells).</summary>
	public bool TryGetGridCell( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize, out int x, out int y )
	{
		x = y = -1;
		if ( !ParkPlacement.TryGetGroundPoint( mousePosition, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, out var ground ) )
			return false;
		return TryGetGridCell( ground.X, ground.Y, out x, out y );
	}

	/// <summary>Screen position of cell (x, y)'s centre on the Z = 0 plane the cursor picks on (for build ghosts).</summary>
	public bool TryProjectCell( int x, int y, System.Numerics.Vector2 viewportSize, out System.Numerics.Vector2 screen )
	{
		var origin = Objects.Grid.Origin;
		var point = new System.Numerics.Vector4( origin.X + (x + 0.5f) * ObjectPlacement.CellSize, origin.Y + (y + 0.5f) * ObjectPlacement.CellSize, 0, 1 );
		var clip = System.Numerics.Vector4.Transform( point, Camera.ViewMatrix * Camera.ProjMatrix );
		screen = default;
		if ( clip.W <= 0 )
			return false;
		screen = new System.Numerics.Vector2( (clip.X / clip.W + 1) / 2 * viewportSize.X, (1 - clip.Y / clip.W) / 2 * viewportSize.Y );
		return float.IsFinite( screen.X ) && float.IsFinite( screen.Y );
	}

	public bool TryGetGridCell( float engineX, float engineY, out int x, out int y )
	{
		var origin = Objects.Grid.Origin;
		x = (int)MathF.Floor( (engineX - origin.X) / ObjectPlacement.CellSize );
		y = (int)MathF.Floor( (engineY - origin.Y) / ObjectPlacement.CellSize );
		return float.IsFinite( engineX ) && float.IsFinite( engineY ) && x >= 0 && y >= 0 && x < Objects.Grid.Width && y < Objects.Grid.Height;
	}

	/// <summary>
	/// Builds <paramref name="entry"/> with its footprint centred on cell (x, y). In original levels the park
	/// economy must allow the purchase (research, golden tickets, money; ParkEconomy.TryBuild) and charges
	/// <c>Upgrades[0].CostOfUpgrade</c>; the generic sandbox has no economy and builds for free.
	/// </summary>
	// [APPROX:RIDES-021] Built footprint is centred on the clicked cell; the cursor ray hits the Z = 0 plane — evidence needed: original build cursor behaviour
	public OriginalObject? PlaceObject( ObjectCatalogEntry entry, int x, int y, int rotation )
	{
		if ( IsReadOnlyVisit )
		{
			LastActionMessage = "Read-only visit: object changes are disabled.";
			return null;
		}
		var anchor = GetCentredAnchor( entry, x, y, rotation );
		var result = Objects.Check( entry, anchor.X, anchor.Y, rotation );
		if ( result != OriginalPlacementResult.Allowed )
		{
			LastActionMessage = $"Cannot build {entry.DisplayName} here: {result}.";
			return null;
		}
		ParkObjectState? bought = null;
		if ( Park != null )
		{
			var purchase = Park.Economy.TryBuild( entry.InfoId, out bought );
			if ( purchase != ParkEconomy.PurchaseResult.Ok )
			{
				LastActionMessage = $"Cannot build {entry.DisplayName}: {purchase}.";
				return null;
			}
		}
		var placed = Objects.TryPlace( entry, anchor.X, anchor.Y, rotation, out _ )!;
		if ( bought != null )
			LinkEconomy( placed, bought.Id );
		LastActionMessage = bought != null
			? $"Built {entry.DisplayName} for ${bought.TotalSpent}; it runs its original script."
			: $"Built {entry.DisplayName} (sandbox: no economy, not charged); it runs its original script.";
		return placed;
	}

	/// <summary>Anchor that centres the footprint on cell (x, y).</summary>
	public static (int X, int Y) GetCentredAnchor( ObjectCatalogEntry entry, int x, int y, int rotation )
	{
		var (minX, minY, maxX, maxY) = ObjectFootprint.GetBounds( entry.Shape, 0, 0, rotation );
		return ObjectFootprint.AnchorForBounds( entry.Shape, x - (maxX - minX) / 2, y - (maxY - minY) / 2, rotation );
	}

	/// <summary>Build rule for <paramref name="entry"/> centred on cell (x, y), without buying it.</summary>
	public OriginalPlacementResult CheckObject( ObjectCatalogEntry entry, int x, int y, int rotation )
	{
		var anchor = GetCentredAnchor( entry, x, y, rotation );
		return Objects.Check( entry, anchor.X, anchor.Y, rotation );
	}

	public bool RemoveObjectAt( int x, int y )
	{
		if ( IsReadOnlyVisit )
		{
			LastActionMessage = "Read-only visit: object changes are disabled.";
			return false;
		}
		var item = Objects.FindAt( x, y );
		if ( item == null )
			return false;
		LastActionMessage = $"Removed {item.Entry.DisplayName}.";
		Objects.Remove( item );
		return true;
	}

	private void HandleObjectClick( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize )
	{
		if ( IsReadOnlyVisit )
			return;
		if ( (BuildEntry == null && !IsRemovingObjects && !CellTool.IsActive) || !TryGetGridCell( mousePosition, viewportSize, out var x, out var y ) )
			return;
		if ( CellTool.IsActive && BuildEntry == null )
			CellTool.Click( (x, y) );
		else if ( IsRemovingObjects )
			RemoveAt( x, y );
		else
			PlaceObject( BuildEntry!, x, y, BuildRotation );
	}

	/// <summary>The remove tool: the object under the cursor, else a queue cell, else a path cell.</summary>
	public bool RemoveAt( int x, int y ) =>
		RemoveObjectAt( x, y ) || RemoveQueueCell( x, y ) > 0 || RemovePathCell( x, y ) == CellBuildResult.Ok;

	/// <summary>Cells covered by the prototype Totem (its 6×8-unit model centred on its position).</summary>
	private bool IsReservedByPrototype( int x, int y ) => PlacedRide != null && PrototypeCovers( PlacedRide.Position, x, y );

	// [EXT:developer-prototype] Sandbox Totem blocks cells whose centres lie within its 6×8-unit model box (no original counterpart)
	private bool PrototypeCovers( Vector3 position, int x, int y )
	{
		var origin = Objects.Grid.Origin;
		var centreX = origin.X + (x + 0.5f) * ObjectPlacement.CellSize;
		var centreY = origin.Y + (y + 0.5f) * ObjectPlacement.CellSize;
		return Math.Abs( centreX - position.X ) < 3 && Math.Abs( centreY - position.Y ) < 4;
	}

	private bool PrototypeOverlapsObjects( Vector3 position )
	{
		foreach ( var item in Objects.Objects )
		{
			if ( item.Cells.Any( cell => PrototypeCovers( position, cell.X, cell.Y ) ) )
				return true;
		}
		return false;
	}

	private readonly Dictionary<OriginalObject, int> economyInstances = new();
	private int? prototypeInstance;
	private bool prototypeBought;

	/// <summary>Economy instance id of a placed object, when the level has an economy.</summary>
	public int? GetEconomyInstance( OriginalObject item ) => economyInstances.TryGetValue( item, out var id ) ? id : null;

	/// <summary>
	/// Matches the imported objects to the uncharged economy records OriginalEconomyImport created (same
	/// Info.Id, in save order), links their guest payments, and sells objects when they are removed.
	/// </summary>
	private void ConnectObjectsToEconomy()
	{
		if ( Park == null )
			return;
		var free = Park.Economy.Objects.Where( state => state.Imported ).OrderBy( state => state.Id ).ToList();
		foreach ( var item in Objects.Objects )
		{
			var state = free.FirstOrDefault( candidate => candidate.InfoId == item.Entry.InfoId );
			if ( state == null && Park.Economy.Catalog.TryGet( item.Entry.InfoId, out _ ) )
				state = Park.Economy.RegisterExisting( item.Entry.InfoId );
			if ( state == null )
				continue;
			free.Remove( state );
			LinkEconomy( item, state.Id );
		}
		Objects.ObjectRemoved += item =>
		{
			Park.Guests?.Unlink( item.Visitors.AttractionId );
			if ( economyInstances.Remove( item, out var instance ) && Park.Economy.TryGetObject( instance, out _ ) )
			{
				var credit = Park.Economy.Sell( instance );
				LastActionMessage = $"Removed {item.Entry.DisplayName}; scrap value ${credit} credited.";
			}
		};
	}

	private void LinkEconomy( OriginalObject item, int instance )
	{
		economyInstances[item] = instance;
		Park?.Guests?.Link( item.Visitors.AttractionId, instance );
	}

	/// <summary>Mirrors each script's open state into the economy (closed shops/sideshows take no money).</summary>
	private void SyncObjectEconomy()
	{
		if ( Park == null )
			return;
		foreach ( var (item, instance) in economyInstances )
		{
			if ( Park.Economy.TryGetObject( instance, out var state ) )
				state.IsOpen = item.IsOpen;
		}
	}

	/// <summary>The prototype Totem is bought through the catalogue like any object (Info.Id 1110).</summary>
	private void LinkPrototypeToEconomy( PrototypeRide ride )
	{
		if ( Park == null || !Park.Economy.Catalog.TryGet( PrototypeRide.InfoId, out _ ) )
			return;
		var purchase = Park.Economy.TryBuild( PrototypeRide.InfoId, out var bought );
		prototypeBought = purchase == ParkEconomy.PurchaseResult.Ok;
		// [EXT:developer-prototype] When the economy refuses the Totem (e.g. Research.Group 4 not yet researched), the developer prototype is registered uncharged (no original counterpart)
		var state = prototypeBought ? bought! : Park.Economy.RegisterExisting( PrototypeRide.InfoId );
		prototypeInstance = state.Id;
		Park.Guests?.Link( ride.Visitors.AttractionId, state.Id );
		Log.Trace( prototypeBought ? $"Prototype Totem bought for ${state.TotalSpent}." : $"Prototype Totem registered uncharged ({purchase})." );
	}

	private void RemovePrototypeFromEconomy( PrototypeRide ride )
	{
		if ( Park == null || prototypeInstance is not int instance )
			return;
		Park.Guests?.Unlink( ride.Visitors.AttractionId );
		if ( Park.Economy.TryGetObject( instance, out _ ) )
		{
			if ( prototypeBought )
				Park.Economy.Sell( instance );
			else
				Park.Economy.Remove( instance );
		}
		prototypeInstance = null;
	}
}
