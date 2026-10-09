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

	/// <summary>Builds <paramref name="entry"/> with its footprint centred on cell (x, y).</summary>
	public OriginalObject? PlaceObject( ObjectCatalogEntry entry, int x, int y, int rotation )
	{
		var (minX, minY, maxX, maxY) = ObjectFootprint.GetBounds( entry.Shape, 0, 0, rotation );
		var anchor = ObjectFootprint.AnchorForBounds( entry.Shape, x - (maxX - minX) / 2, y - (maxY - minY) / 2, rotation );
		var placed = Objects.TryPlace( entry, anchor.X, anchor.Y, rotation, out var result );
		LastActionMessage = placed == null
			? $"Cannot build {entry.DisplayName} here: {result}."
			: $"Built {entry.DisplayName} (cost {entry.BuildCost}, not charged); it runs its original script.";
		return placed;
	}

	public bool RemoveObjectAt( int x, int y )
	{
		var item = Objects.FindAt( x, y );
		if ( item == null )
			return false;
		Objects.Remove( item );
		LastActionMessage = $"Removed {item.Entry.DisplayName}.";
		return true;
	}

	private void HandleObjectClick( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize )
	{
		if ( (BuildEntry == null && !IsRemovingObjects) || !TryGetGridCell( mousePosition, viewportSize, out var x, out var y ) )
			return;
		if ( IsRemovingObjects )
			RemoveObjectAt( x, y );
		else
			PlaceObject( BuildEntry!, x, y, BuildRotation );
	}

	/// <summary>Cells covered by the prototype Totem (its 6×8-unit model centred on its position).</summary>
	private bool IsReservedByPrototype( int x, int y ) => PlacedRide != null && PrototypeCovers( PlacedRide.Position, x, y );

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
}
