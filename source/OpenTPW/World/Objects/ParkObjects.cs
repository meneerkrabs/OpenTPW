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
	/// <summary>Cells the park does not own (the level's cell map, flag 0x40); null owns every cell.</summary>
	public Func<int, int, bool>? IsUnowned { get; set; }
	public event Action<OriginalObject>? ObjectPlaced;
	public event Action<OriginalObject>? ObjectRemoved;

	public OriginalObject? FindAt( int x, int y ) => occupied.TryGetValue( (x, y), out var item ) ? item : null;

	public bool IsOccupied( int x, int y ) => occupied.ContainsKey( (x, y) ) || IsReserved?.Invoke( x, y ) == true;

	/// <summary>Build rule for a catalog object anchored at (x, y) with a rotation.</summary>
	public OriginalPlacementResult Check( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation ) =>
		Check( entry, anchorX, anchorY, rotation, out _ );

	/// <summary>
	/// <see cref="Check(ObjectCatalogEntry, int, int, int)"/> with the objects the placement would overwrite: an object whose
	/// <see cref="ObjectCatalogEntry.OverwritePriority"/> is below the new one's gives way (a ride over a bush), any other overlap refuses.
	/// </summary>
	// [APPROX:RIDES-018] Build rules = footprint inside grid + the original's map-start cell types (holes refused, OriginalParkPlacement) + save paths/objects + overlap by overwrite priority + owned land; no slope test, as in tp.exe's validator 0x535670; Level.PlaceObject enforces economy purchases — evidence needed: the placement's own path cells (an object may stand on a path cell that is not NoModify, 0x535D21, the commit clearing it, 0x534549), the entrance and exit outside cells (0x5298B9, 0x529A2F: the path rule 0x201 there) and the overwrite of objects by the path and queue tools (0x535D66 with 0x818C10 = 2 and 3)
	// [BIN:TPW-EXE:0x5357c7 placement validator] after its early returns for a few tool modes the validator 0x535670 refuses a cell with flag 0x40 for whatever it places, paths, queues and objects alike
	// [BIN:TPW-EXE:0x535D66 overwrite priority] a cell of type 4, 9 or 10 is allowed when 0x818C10, the placed type's +0x54 (Info.OverwritePriority, sam schema 0x745020; set by the hover 0x5234D0), is greater than 0x818C14, the +0x54 of the type of the object owning the cell (0x53570E: the thing at the cell's owner index +0x10, class 3); otherwise the cell is refused and the sticky refusal 0x816D50 set
	public OriginalPlacementResult Check( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, out IReadOnlyList<OriginalObject> overwritten )
	{
		var giving = new List<OriginalObject>();
		overwritten = giving;
		bool Blocks( int x, int y )
		{
			if ( occupied.TryGetValue( (x, y), out var item ) )
			{
				if ( item.Entry.OverwritePriority >= entry.OverwritePriority )
					return true;
				if ( !giving.Contains( item ) )
					giving.Add( item );
				return false;
			}
			return IsReserved?.Invoke( x, y ) == true;
		}
		var result = Check( Grid, Blocks, entry, anchorX, anchorY, rotation );
		if ( result == OriginalPlacementResult.Allowed && IsUnowned is { } unowned )
		{
			foreach ( var (x, y, _) in ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation ) )
			{
				if ( unowned( x, y ) )
				{
					result = OriginalPlacementResult.NotOwned;
					break;
				}
			}
		}
		if ( result != OriginalPlacementResult.Allowed )
			giving.Clear();
		return result;
	}

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
	// [BIN:TPW-EXE:0x5344E5 overwrite] the commit runs cell op 0x87 on every footprint cell first (0x528D2C, 0x5290FA): with 0x818C14 below 0x818C10 an object cell's owner is demolished whole (0x527EE0, which credits its scrap value), before the new footprint is written
	public OriginalObject? TryPlace( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, out OriginalPlacementResult result, bool open = true )
	{
		result = Check( entry, anchorX, anchorY, rotation, out var overwritten );
		if ( result != OriginalPlacementResult.Allowed )
			return null;
		foreach ( var item in overwritten )
			Remove( item );
		return Add( entry, anchorX, anchorY, rotation, open );
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

	private bool clearing;
	private readonly HashSet<OriginalObject> removing = new();

	/// <summary>
	/// Raised for an object that is about to be removed, while its script and footprint are still there: the one seam every removal (remove tool, sale from a window, scripted
	/// removal) passes through, except <see cref="Clear"/>. A handler that removes the same object again is ignored; one that throws aborts the removal.
	/// </summary>
	// [BIN:TPW-EXE:0x4dd0a0 object removal] the removal passes flag 0x2 to the kill 0x559060 (which starts the DestroyParticleEffect) unless a park loads or clears (DAT_00785a2c)
	public event Action<OriginalObject>? ObjectRemoving;

	public bool Remove( OriginalObject item )
	{
		if ( !objects.Contains( item ) || !removing.Add( item ) )
			return false;
		try
		{
			if ( !clearing )
				ObjectRemoving?.Invoke( item );
			if ( !objects.Remove( item ) )
				return false;
		}
		finally
		{
			removing.Remove( item );
		}
		foreach ( var key in occupied.Where( pair => pair.Value == item ).Select( pair => pair.Key ).ToArray() )
			occupied.Remove( key );
		item.Delete();
		ObjectRemoved?.Invoke( item );
		return true;
	}

	public void Clear()
	{
		clearing = true;
		try
		{
			foreach ( var item in objects.ToArray() )
				Remove( item );
		}
		finally
		{
			clearing = false;
		}
	}

	public void Simulate( double deltaSeconds )
	{
		// [BIN:TPW-EXE:0x0054F55F world substep] the bump module ticks once per substep before every ride's script step (the RSSE tick 0x5516B0), whichever rides exist
		ScriptWorld.Bumpers.HostDriven = true;
		ScriptWorld.Bumpers.AdvanceModule( deltaSeconds );
		foreach ( var item in objects )
			item.Simulate( deltaSeconds );
	}

	/// <summary>
	/// Replaces the Easymode markers with the real objects: every SYSG placed record at its saved anchor and
	/// rotation (unresolved non-square rotations use the catalog shape), and the fixed items in park space.
	/// Imported objects are opened: the save's ride state is not decoded.
	/// </summary>
	// [APPROX:RIDES-015] Imported objects start open (saved ride state not decoded); built objects open too, while the original's object constructor 0x4DB090 sets the open word +0x68 = 1 (0x4DB3B9) and then, for a type with Info.HasQueue (def+0x40, flag 8 at 0x4DB420), logs "Object %d Closing", clears the open word and writes VAR_RIDECLOSED = 1 (0x4DB712–0x4DB779), so a built ride with a queue starts closed — evidence needed: the TPWS ride-state fields (save loader 0x415270)
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
	/// For levels without an original save: the fixed items the original creates at world setup, Gates and Lights, in park space.
	/// The arrival vehicles (bus, seaplane, Ferry) are placed when the arrival update first sends them (<see cref="ArrivalVehicles"/>);
	/// a Theme Park Inc world still gets the Bus here.
	/// </summary>
	// [BIN:TPW-EXE:0x005156A0 world setup fixed items] world setup creates the definitions named "gates" (wide literal 0x762448) and "lights" (0x762454) through 0x4db090 (case-insensitive compare 0x67b140); a missing definition logs "Could not find the gates feature" or the LIGHTSNAME message; a loaded save then restores the handles (writer 0x517076)
	// [BIN:TPW-EXE:0x0051A2F0 vehicle on demand] the bus, seaplane and Ferry are created by the vehicle dispatch the first time each is sent (0x004DB090 at 0x0051A57E), never at setup
	// [APPROX:RIDES-019] End (0x762464) is not created: the original creates it through 0x516b00, called from the message handler 0x503d40 (message type 0x11, from 0x50b550), not at setup; Theme Park Inc worlds without a prebuilt save keep a Bus from setup — evidence needed: what sends the End message, and how Theme Park Inc places its arrival vehicle
	public int AddDefaultFixedItems() => AddDefaultFixedItemSteps().LastOrDefault().Placed;

	/// <summary><see cref="AddDefaultFixedItems"/> one object at a time, as <see cref="ImportOriginalSteps"/>.</summary>
	internal IEnumerable<(int Placed, float Share)> AddDefaultFixedItemSteps()
	{
		var entries = Catalog.Entries.Where( entry => entry.IsFixedItem && (entry.SettingsName is "Gates" or "Lights" || GameEditions.IsThemeParkInc && entry.SettingsName == "Bus") ).ToList();
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
