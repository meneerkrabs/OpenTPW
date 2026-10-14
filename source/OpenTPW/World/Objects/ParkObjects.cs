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
	private readonly int scriptKey;

	/// <param name="seed">The park's world seed: object script seeds and the script world's stream derive from it.</param>
	/// <param name="civilClock">The civil time the objects' scripts read (YEAR to SEC); null is the host clock, as in the game.</param>
	public ParkObjects( ObjectCatalog catalog, IParkGrid grid, WorldSeed? seed = null, Func<DateTime>? civilClock = null )
	{
		Catalog = catalog;
		Grid = grid;
		var world = seed ?? WorldSeed.Default;
		ScriptWorld = new RideScriptWorld( world.ScriptStream, civilClock );
		scriptKey = world.ObjectScriptKey;
	}

	public ObjectCatalog Catalog { get; }
	public IParkGrid Grid { get; }
	public RideScriptWorld ScriptWorld { get; }
	public IReadOnlyList<OriginalObject> Objects => objects;
	/// <summary>Extra blocked cells owned by the host (the sandbox Totem prototype).</summary>
	public Func<int, int, bool>? IsReserved { get; set; }
	public event Action<OriginalObject>? ObjectPlaced;
	public event Action<OriginalObject>? ObjectRemoved;

	public OriginalObject? FindAt( int x, int y ) => occupied.TryGetValue( (x, y), out var item ) ? item : null;

	public bool IsOccupied( int x, int y ) => occupied.ContainsKey( (x, y) ) || IsReserved?.Invoke( x, y ) == true;

	/// <summary>Build rule for a catalog object anchored at (x, y) with a rotation.</summary>
	// [APPROX:RIDES-018] Build rules = footprint inside grid + MAP/save terrain rules + no overlap; no slope, path or land rule; Level.PlaceObject enforces economy purchases — evidence needed: original build checks (binary/captures)
	public OriginalPlacementResult Check( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation ) =>
		Check( Grid, IsOccupied, entry, anchorX, anchorY, rotation );

	/// <summary>The same build rule for any grid and occupancy (the headless M3 gate has no <see cref="OriginalObject"/>s).</summary>
	internal static OriginalPlacementResult Check( IParkGrid grid, Func<int, int, bool> isOccupied, ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation )
	{
		if ( !ObjectFootprint.IsValidRotation( rotation ) )
			throw new ArgumentOutOfRangeException( nameof( rotation ) );
		if ( entry.IsTool )
			return OriginalPlacementResult.Blocked;
		foreach ( var (x, y, _) in ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation ) )
		{
			if ( x < 0 || y < 0 || x >= grid.Width || y >= grid.Height )
				return OriginalPlacementResult.OutsideTerrain;
			var terrain = grid.CheckTerrain( x, y );
			if ( terrain != OriginalPlacementResult.Allowed )
				return terrain;
			if ( isOccupied( x, y ) )
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
	public OriginalObject Add( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, bool open = true ) =>
		Register( new OriginalObject( entry, ComputePlacement( entry, anchorX, anchorY, rotation ), ScriptWorld, nextSeed++ ^ scriptKey, open ) );

	/// <summary>
	/// Places a saved object back (<see cref="ParkLayoutSnapshot"/> restore): the placement rules of <see cref="Add"/>
	/// with the record's own creation seed instead of the allocator's next value, and its saved attraction id instead
	/// of a fresh allocation — the object's runtime adopts the id, so guests, scripts and the economy stay linked to
	/// the exact identity they were saved with, in constant time (ids of removed objects are never replayed). Like
	/// Add it applies no build rules and charges nothing; the seed allocator itself is restored separately with
	/// <see cref="RestoreSeedAllocator"/> so objects placed after the load continue the saved seed sequence.
	/// </summary>
	internal OriginalObject AddRestored( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, bool open, int seed, int attractionId ) =>
		Register( new OriginalObject( entry, ComputePlacement( entry, anchorX, anchorY, rotation ), ScriptWorld, seed, open, attractionId ) );

	// [DATA:Easymode.TPWI:SYSG X/Y/rotation] [DATA:<fixed item>.MD2:park-space coordinates]
	// [DATA:PreBuilt/prebuilt.TPWS:SYSG X/Y/rotation] TPI fixed models use local coordinates and their saved anchor.
	private ObjectPlacement ComputePlacement( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation ) =>
		entry.IsFixedItem && !GameEditions.IsThemeParkInc
			? new ObjectPlacement( 0, 0, 0, Grid.Origin, 0 )
			: new ObjectPlacement( anchorX, anchorY, rotation, Grid.Origin,
				entry.IsFixedItem ? 0 : GetBaseHeight( entry, anchorX, anchorY, rotation ) );

	private OriginalObject Register( OriginalObject item )
	{
		objects.Add( item );
		foreach ( var (x, y, _) in item.Cells )
			occupied[(x, y)] = item;
		ObjectPlaced?.Invoke( item );
		return item;
	}

	/// <summary>The next seed <see cref="Add"/> draws (one past every placement ever made; removals do not rewind it).</summary>
	internal int NextPlacementSeed => nextSeed;
	/// <summary>The world seed's object-script key the placement seeds are XORed with (<see cref="WorldSeed.ObjectScriptKey"/>).</summary>
	internal int ObjectScriptKey => scriptKey;
	/// <summary>
	/// The script world's next attraction id. Like the seed allocator it never rewinds, so a park that removed objects
	/// has holes in both — <see cref="ParkLayoutSnapshot"/> captures and reproduces them with explicit ids, not by
	/// renumbering the survivors.
	/// </summary>
	internal int NextAttractionId => ScriptWorld.NextAttractionId;

	/// <summary>Restores the placement-seed allocator of a saved park (validation checked every saved seed against it).</summary>
	internal void RestoreSeedAllocator( int value ) => nextSeed = value >= 1 ? value : throw new ArgumentOutOfRangeException( nameof( value ) );

	/// <summary>
	/// Restores the script world's attraction-id frontier to a saved park's next id in constant time: the frontier
	/// adopts it, so the ids of objects the park removed are skipped without replaying their allocation. Requires a
	/// value the allocator could still allocate from (a fresh world starts at 1).
	/// </summary>
	internal void RestoreAttractionIdAllocator( int value )
	{
		if ( value < 1 || value >= int.MaxValue )
			throw new ArgumentOutOfRangeException( nameof( value ) );
		ScriptWorld.AdoptAttractionId( value - 1 );
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
	public int ImportOriginal( OriginalParkImport save ) => ImportOriginalSteps( save ).LastOrDefault().Placed;

	/// <summary><see cref="ImportOriginal"/> one object at a time: after each, the objects placed so far and the share of the records done.</summary>
	internal IEnumerable<(int Placed, float Share)> ImportOriginalSteps( OriginalParkImport save )
	{
		save.RequireSupportedCoasterRuntime();
		var records = save.PlacedObjects.Select( item => item.Record ).Concat( save.UnresolvedObjects ).Concat( save.FixedItems ).ToList();
		var count = 0;
		for ( var index = 0; index < records.Count; index++ )
		{
			var record = records[index];
			var entry = Catalog.Find( record.InfoId );
			if ( entry == null || entry.IsTool )
				continue;
			// [APPROX:RIDES-040] TPI fixed scene records at (0, 0) use their local model origin; their remaining transform fields are not decoded.
			Add( entry, record.X, record.Y, record.Rotation );
			count++;
			yield return (count, (index + 1) / (float)records.Count);
		}
		yield return (count, 1);
	}

	/// <summary>
	/// For levels without an original save: the three fixed items the Easymode save records (Gates, Lights,
	/// Bus), in park space. Which fixed items the original spawns, and when (seaplane, ferry, end), is not known.
	/// </summary>
	// [APPROX:RIDES-019] Levels without a save get Gates, Lights and Bus (the fixed items Easymode records) — evidence needed: original fixed-item spawning per level
	public int AddDefaultFixedItems() => AddDefaultFixedItemSteps().LastOrDefault().Placed;

	/// <summary><see cref="AddDefaultFixedItems"/> one object at a time, as <see cref="ImportOriginalSteps"/>.</summary>
	internal IEnumerable<(int Placed, float Share)> AddDefaultFixedItemSteps()
	{
		var entries = Catalog.Entries.Where( entry => entry.IsFixedItem && entry.SettingsName is "Gates" or "Lights" or "Bus" ).ToList();
		var count = 0;
		foreach ( var entry in entries )
		{
			Add( entry, 0, 0, 0 );
			count++;
			yield return (count, count / (float)entries.Count);
		}
		yield return (count, 1);
	}
}
