namespace OpenTPW;

/// <summary>A park's build grid: cell size <see cref="ObjectPlacement.CellSize"/>, origin, heights and terrain rules.</summary>
public interface IParkGrid
{
	int Width { get; }
	int Height { get; }
	/// <summary>Engine position of grid corner (0, 0).</summary>
	System.Numerics.Vector2 Origin { get; }
	/// <summary>Terrain rule for one cell, ignoring objects (those are checked by <see cref="ParkObjects"/>).</summary>
	OriginalPlacementResult CheckTerrain( int x, int y );
	/// <summary>Engine height of the ground at a cell (mean of its corners).</summary>
	float GetCellHeight( int x, int y );
}

/// <summary>The generic sandbox: the 32×32 tile park of <see cref="ParkPlacement"/>, flat at height 0.</summary>
public sealed class SandboxParkGrid : IParkGrid
{
	public int Width => ParkPlacement.TileCount;
	public int Height => ParkPlacement.TileCount;
	public System.Numerics.Vector2 Origin => new( ParkPlacement.MinimumCoordinate, ParkPlacement.MinimumCoordinate );
	public OriginalPlacementResult CheckTerrain( int x, int y ) =>
		x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : OriginalPlacementResult.Allowed;
	public float GetCellHeight( int x, int y ) => 0;
}

/// <summary>
/// An imported original level: MAP flags and heightfield holes (docs/MAP.md), plus the Easymode save's path
/// cells and occupied non-object cells (queues). Object footprints are not taken from the save here: the
/// imported objects are real <see cref="OriginalObject"/>s, so removing one frees its cells.
/// </summary>
public sealed class OriginalParkGrid : IParkGrid
{
	private readonly OriginalPark park;
	private readonly HashSet<(int, int)> importedObjectCells = new();

	public OriginalParkGrid( OriginalPark park )
	{
		this.park = park;
		if ( park.Save != null )
		{
			foreach ( var item in park.Save.PlacedObjects )
			{
				for ( var x = item.MinX; x <= item.MaxX; x++ )
				{
					for ( var y = item.MinY; y <= item.MaxY; y++ )
						importedObjectCells.Add( (x, y) );
				}
			}
		}
	}

	public int Width => Math.Min( park.Heightfield.CellCountX, park.Map.CellCountX );
	public int Height => Math.Min( park.Heightfield.CellCountZ, park.Map.CellCountY );
	public System.Numerics.Vector2 Origin => -OriginalParkPlacement.GetOrigin( park.Heightfield );

	public OriginalPlacementResult CheckTerrain( int x, int y )
	{
		var result = OriginalParkPlacement.Check( park.Map, park.Heightfield, null, x, y, x, y );
		if ( result != OriginalPlacementResult.Allowed || park.Save == null )
			return result;
		var cell = park.Save.Cells[x, y];
		if ( cell.IsPath )
			return OriginalPlacementResult.Path;
		if ( cell.IsOccupied && !importedObjectCells.Contains( (x, y) ) )
			return OriginalPlacementResult.Occupied;
		return OriginalPlacementResult.Allowed;
	}

	public float GetCellHeight( int x, int y ) => OriginalParkPlacement.GetCellCenter( park.Heightfield, x, y ).Z;
}

/// <summary>
/// The placed original objects of a level and their build rules: footprints from the catalog shape and
/// rotation (<see cref="ObjectFootprint"/>), terrain rules from the <see cref="IParkGrid"/>, no overlap.
/// All scripts share one <see cref="RideScriptWorld"/> (bus.RSE finds the traffic lights by name).
/// This low-level grid does not charge; Level.PlaceObject routes gameplay purchases through the economy.
/// </summary>
public sealed class ParkObjects
{
	private readonly List<OriginalObject> objects = new();
	private readonly Dictionary<(int, int), OriginalObject> occupied = new();
	private int nextSeed = 1;

	public ParkObjects( ObjectCatalog catalog, IParkGrid grid )
	{
		Catalog = catalog;
		Grid = grid;
	}

	public ObjectCatalog Catalog { get; }
	public IParkGrid Grid { get; }
	public RideScriptWorld ScriptWorld { get; } = new();
	public IReadOnlyList<OriginalObject> Objects => objects;
	/// <summary>Extra blocked cells owned by the host (the sandbox Totem prototype).</summary>
	public Func<int, int, bool>? IsReserved { get; set; }
	public event Action<OriginalObject>? ObjectPlaced;
	public event Action<OriginalObject>? ObjectRemoved;

	public OriginalObject? FindAt( int x, int y ) => occupied.TryGetValue( (x, y), out var item ) ? item : null;

	public bool IsOccupied( int x, int y ) => occupied.ContainsKey( (x, y) ) || IsReserved?.Invoke( x, y ) == true;

	/// <summary>Build rule for a catalog object anchored at (x, y) with a rotation.</summary>
	// [APPROX:RIDES-018] Build rules = footprint inside grid + MAP/save terrain rules + no overlap; no slope, path or land rule; Level.PlaceObject enforces economy purchases — evidence needed: original build checks (binary/captures)
	public OriginalPlacementResult Check( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation )
	{
		if ( !ObjectFootprint.IsValidRotation( rotation ) )
			throw new ArgumentOutOfRangeException( nameof( rotation ) );
		if ( entry.IsTool )
			return OriginalPlacementResult.Blocked;
		foreach ( var (x, y, _) in ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation ) )
		{
			if ( x < 0 || y < 0 || x >= Grid.Width || y >= Grid.Height )
				return OriginalPlacementResult.OutsideTerrain;
			var terrain = Grid.CheckTerrain( x, y );
			if ( terrain != OriginalPlacementResult.Allowed )
				return terrain;
			if ( IsOccupied( x, y ) )
				return OriginalPlacementResult.Occupied;
		}
		return OriginalPlacementResult.Allowed;
	}

	/// <summary>Places an object when the build rules allow it.</summary>
	public OriginalObject? TryPlace( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, out OriginalPlacementResult result, bool open = true )
	{
		result = Check( entry, anchorX, anchorY, rotation );
		return result == OriginalPlacementResult.Allowed ? Add( entry, anchorX, anchorY, rotation, open ) : null;
	}

	/// <summary>Places an object without build rules (imported original records and fixed items).</summary>
	public OriginalObject Add( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, bool open = true )
	{
		// [DATA:Easymode.TPWI:SYSG X/Y/rotation] [DATA:<fixed item>.MD2:park-space coordinates]
		var placement = entry.IsFixedItem
			? new ObjectPlacement( 0, 0, 0, Grid.Origin, 0 )
			: new ObjectPlacement( anchorX, anchorY, rotation, Grid.Origin, GetBaseHeight( entry, anchorX, anchorY, rotation ) );
		var item = new OriginalObject( entry, placement, ScriptWorld, nextSeed++, open );
		objects.Add( item );
		foreach ( var (x, y, _) in item.Cells )
			occupied[(x, y)] = item;
		ObjectPlaced?.Invoke( item );
		return item;
	}

	/// <summary>
	/// Base height: the mean ground height under the footprint. Rides flatten their ground in the original
	/// (<c>Info.DontDeformBase</c>, per-archive <c>.hmp</c> files not decoded), so this is an approximation.
	/// </summary>
	// [APPROX:RIDES-014] Object base height = mean ground height of its footprint cells (.hmp ground deformation not decoded) — evidence needed: .hmp format, captures on slopes
	public float GetBaseHeight( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation )
	{
		var heights = ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation )
			.Where( cell => cell.X >= 0 && cell.Y >= 0 && cell.X < Grid.Width && cell.Y < Grid.Height )
			.Select( cell => Grid.GetCellHeight( cell.X, cell.Y ) ).ToArray();
		return heights.Length == 0 ? 0 : heights.Average();
	}

	public bool Remove( OriginalObject item )
	{
		if ( !objects.Remove( item ) )
			return false;
		foreach ( var key in occupied.Where( pair => pair.Value == item ).Select( pair => pair.Key ).ToArray() )
			occupied.Remove( key );
		item.Delete();
		ObjectRemoved?.Invoke( item );
		return true;
	}

	public void Clear()
	{
		foreach ( var item in objects.ToArray() )
			Remove( item );
	}

	public void Simulate( double deltaSeconds )
	{
		foreach ( var item in objects )
			item.Simulate( deltaSeconds );
	}

	/// <summary>
	/// Replaces the Easymode markers with the real objects: every SYSG placed record at its saved anchor and
	/// rotation (unresolved non-square rotations use the catalog shape), and the fixed items in park space.
	/// Imported objects are opened: the save's ride state is not decoded.
	/// </summary>
	// [APPROX:RIDES-015] Imported objects start open (saved ride state not decoded); built objects open too — evidence needed: TPWS ride-state fields, original build behaviour
	public int ImportOriginal( OriginalParkImport save )
	{
		var count = 0;
		foreach ( var record in save.PlacedObjects.Select( item => item.Record ).Concat( save.UnresolvedObjects ).Concat( save.FixedItems ) )
		{
			var entry = Catalog.Find( record.InfoId );
			if ( entry == null || entry.IsTool )
				continue;
			Add( entry, record.X, record.Y, record.Rotation );
			count++;
		}
		return count;
	}

	/// <summary>
	/// For levels without an original save: the three fixed items the Easymode save records (Gates, Lights,
	/// Bus), in park space. Which fixed items the original spawns, and when (seaplane, ferry, end), is not known.
	/// </summary>
	// [APPROX:RIDES-019] Levels without a save get Gates, Lights and Bus (the fixed items Easymode records) — evidence needed: original fixed-item spawning per level
	public int AddDefaultFixedItems()
	{
		var count = 0;
		foreach ( var entry in Catalog.Entries.Where( entry => entry.IsFixedItem && entry.SettingsName is "Gates" or "Lights" or "Bus" ) )
		{
			Add( entry, 0, 0, 0 );
			count++;
		}
		return count;
	}
}
