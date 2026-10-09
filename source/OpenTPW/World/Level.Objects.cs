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
		Objects = new ParkObjects( ObjectCatalog.Load( theme ), grid ) { IsReserved = IsReservedByPrototype };
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
	/// Guests queue at the walkable cell outside the object's entrance (shape '2') and reappear outside its exit
	/// (shape 'S'/'N'/'E', else the entrance). Where that outside cell is not a walkable path (the Belly Bounce
	/// queue cells are not path cells), the nearest walkable cell to it is used.
	/// </summary>
	private void RegisterObjectWithGuests( OriginalObject item )
	{
		if ( Guests == null || !item.Runtime.IsAttraction )
			return;
		var points = item.AccessPoints.ToArray();
		var entrance = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Entrance );
		if ( entrance == default )
			return;
		var exit = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Exit );
		if ( exit == default )
			exit = entrance;
		var entranceCell = WalkableNear( entrance.OutsideX, entrance.OutsideY );
		var exitCell = WalkableNear( exit.OutsideX, exit.OutsideY );
		if ( entranceCell == null || exitCell == null )
			return;
		item.Visitors.EntranceCell = entranceCell.Value;
		item.Visitors.ExitCell = exitCell.Value;
		item.Visitors.HasCells = true;
		Guests.Register( item.Visitors );
	}

	// [APPROX:RIDES-028] A non-walkable outside cell (queue area) is replaced by the nearest walkable path cell — evidence needed: original queue-path building/joining rules
	private (int X, int Y)? WalkableNear( int x, int y ) =>
		Guests!.Grid.IsWalkable( x, y ) ? (x, y) : FindRideEntrance( Guests.Grid, x, y, x, y );

	public void RotateBuild() => BuildRotation = (BuildRotation + 90) % 360;

	/// <summary>Grid cell under the cursor (sandbox tiles or original cells).</summary>
	public bool TryGetGridCell( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize, out int x, out int y )
	{
		x = y = -1;
		if ( !ParkPlacement.TryGetGroundPoint( mousePosition, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, out var ground ) )
			return false;
		return TryGetGridCell( ground.X, ground.Y, out x, out y );
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
		if ( (BuildEntry == null && !IsRemovingObjects) || !TryGetGridCell( mousePosition, viewportSize, out var x, out var y ) )
			return;
		if ( IsRemovingObjects )
			RemoveObjectAt( x, y );
		else
			PlaceObject( BuildEntry!, x, y, BuildRotation );
	}

	/// <summary>Cells covered by the prototype Totem (its 6×8-unit model centred on its position).</summary>
	private bool IsReservedByPrototype( int x, int y ) => PlacedRide != null && PrototypeCovers( PlacedRide.Position, x, y );

	// [APPROX:RIDES-020] Sandbox Totem blocks cells whose centres lie within its 6×8-unit model box — evidence needed: none for gameplay (prototype only; to be replaced by anchor placement)
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
		// [APPROX:RIDES-029] When the economy refuses the Totem (e.g. Research.Group 4 not yet researched), the developer prototype is registered uncharged — evidence needed: none for gameplay (developer tool; players build through PlaceObject)
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
