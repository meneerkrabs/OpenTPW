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
	/// <summary>Engine height of the ground at grid corner (x, y), the original's terrain vertex; a grid without corner heights gives the height of the cell there.</summary>
	float GetCornerHeight( int x, int y ) => GetCellHeight( x, y );
	/// <summary>Engine height of the ground under a point of the grid in model units (10 per cell from corner (0, 0)), 0 off the map: the original's landscape sampler (0x4527F0); defaults to the cell's mean.</summary>
	float GetTerrainHeight( float x, float y ) => GetCellHeight( Math.Clamp( (int)MathF.Floor( x / 10 ), 0, Math.Max( Width - 1, 0 ) ), Math.Clamp( (int)MathF.Floor( y / 10 ), 0, Math.Max( Height - 1, 0 ) ) );
	/// <summary>Engine height of the ground a draped mesh vertex takes at a point of the grid in model units: the landscape triangle always split by the cell's diagonal (0x469A80, <see cref="LandscapeSampler.SampleDrape"/>); defaults to <see cref="GetTerrainHeight"/>.</summary>
	float GetDrapeHeight( float x, float y ) => GetTerrainHeight( x, y );
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

	/// <summary>
	/// The level's live cell map, once it has one. Its types and flags then decide path, queue and object cells (the host's reservation), so the saved path and occupancy bits
	/// are no longer consulted: they describe the park as it was saved, not as the player has built on it since.
	/// </summary>
	public Func<ParkCellMap?>? LiveCells { get; set; }

	public OriginalPlacementResult CheckTerrain( int x, int y )
	{
		var result = OriginalParkPlacement.Check( park.Map, park.Heightfield, null, x, y, x, y );
		if ( result != OriginalPlacementResult.Allowed || park.Save == null || LiveCells?.Invoke() != null )
			return result;
		var cell = park.Save.Cells[x, y];
		if ( cell.IsPath )
			return OriginalPlacementResult.Path;
		if ( cell.IsOccupied && !importedObjectCells.Contains( (x, y) ) )
			return OriginalPlacementResult.Occupied;
		return OriginalPlacementResult.Allowed;
	}

	public float GetCellHeight( int x, int y ) => OriginalParkPlacement.GetCellCenter( park.Heightfield, x, y ).Z;

	public float GetCornerHeight( int x, int y )
	{
		var field = park.Heightfield;
		x = Math.Clamp( x, 0, field.CellCountX );
		y = Math.Clamp( y, 0, field.CellCountZ );
		return OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( x * field.CellSizeX, field.GetCornerHeight( x, y ), y * field.CellSizeZ ) ).Z;
	}

	public float GetTerrainHeight( float x, float y )
	{
		var field = park.Heightfield;
		return LandscapeSampler.Sample( field.CellCountX, field.CellCountZ, field.CellSizeX, field.CellSizeZ, ( cx, cz ) => field.GetCornerHeight( cx, cz ), ( cx, cz ) => field.SplitsAlongAntiDiagonal( cx, cz ), x, y )
			* OriginalParkPlacement.ModelScale;
	}

	public float GetDrapeHeight( float x, float y )
	{
		var field = park.Heightfield;
		return LandscapeSampler.SampleDrape( field.CellCountX, field.CellCountZ, field.CellSizeX, field.CellSizeZ, ( cx, cz ) => field.GetCornerHeight( cx, cz ), ( cx, cz ) => field.SplitsAlongAntiDiagonal( cx, cz ), x, y )
			* OriginalParkPlacement.ModelScale;
	}
}

/// <summary>How a host's live cell holds a path for the object build rule (<see cref="ParkObjects.PathAt"/>).</summary>
public enum ObjectPathCell
{
	/// <summary>No path cell.</summary>
	None,
	/// <summary>A path cell an object may stand on; the placement clears it.</summary>
	Path,
	/// <summary>A path cell with the NoModify flag (the map start's initial path), which refuses an object.</summary>
	FixedPath
}

/// <summary>What an allowed placement changes besides its own footprint: the objects it demolishes and the path cells it clears.</summary>
public sealed record ObjectPlacementPlan( IReadOnlyList<OriginalObject> Overwritten, IReadOnlyList<(int X, int Y)> ClearedPaths )
{
	public static ObjectPlacementPlan None { get; } = new( Array.Empty<OriginalObject>(), Array.Empty<(int X, int Y)>() );

	/// <summary>True when the placement demolishes no object and clears no path (the smokes' and demos' site searches keep their own layout).</summary>
	public bool KeepsTheRest => Overwritten.Count == 0 && ClearedPaths.Count == 0;
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
	/// <summary>
	/// The park's thing pool (<see cref="ThingPool"/>): each placed object takes a slot as its thing id, and a removed object's slot is freed at the turn's end. Null: objects have no thing id
	/// (a grid of its own, such as the headless gates').
	/// </summary>
	public ThingPool? Things { get; set; }
	public RideScriptWorld ScriptWorld { get; }
	public IReadOnlyList<OriginalObject> Objects => objects;

	/// <summary>Counts every addition to and removal from <see cref="Objects"/>, so a cache of the placed objects (the evening lamps) rebuilds only when the set changes.</summary>
	public int Version { get; private set; }
	/// <summary>Extra blocked cells owned by the host (the sandbox Totem prototype).</summary>
	public Func<int, int, bool>? IsReserved { get; set; }
	/// <summary>Cells the park does not own (the level's cell map, flag 0x40); null owns every cell.</summary>
	public Func<int, int, bool>? IsUnowned { get; set; }
	/// <summary>
	/// The host's live path cells (the level's cell map): null where it has none, when the terrain rule's path cells (the save's and the map
	/// start's) refuse as before. A <see cref="ObjectPathCell.Path"/> under a footprint is cleared by <see cref="ClearPath"/> on commit.
	/// </summary>
	public Func<int, int, ObjectPathCell?>? PathAt { get; set; }
	/// <summary>Clears a path cell under a new footprint (the level: ParkPathBuilder.Remove, no refund); called by <see cref="TryPlace"/> before the object is added.</summary>
	public Action<int, int>? ClearPath { get; set; }
	/// <summary>The path tool's rule for an access cell's outside cell, objects aside (the level: ParkPathBuilder.CheckPathRule); without it, or where it answers null, the terrain and the reserved cells decide.</summary>
	public Func<int, int, bool?>? PathRuleAllows { get; set; }
	/// <summary>
	/// The rule for the '+' and '#' cells of an add-on (<see cref="TrackBuilder.CanStandAddOn"/> for the ride it is for): such a cell lies on its ride's track instead of free ground, so the
	/// occupancy rules do not apply to it and this one does, besides a cell another object already holds (the ride's own station tiles). Null: they are ordinary object cells.
	/// </summary>
	public Func<int, int, bool>? AddOnCellRule { get; set; }
	/// <summary>Land and secondary-layer gate for the beyond-endpoint cell, before its object-mode rule.</summary>
	public Func<int, int, bool>? TrackEndCellRule { get; set; }
	/// <summary>
	/// [EXT:pools-changing-room] An extra placement rule of the host, asked last with the entry, anchor and rotation: a refusal it answers is the placement's result, null has no opinion
	/// (the level: a changing room needs pool water beside it, <see cref="PoolChangingRooms.PlacementRule"/>).
	/// </summary>
	public Func<ObjectCatalogEntry, int, int, int, OriginalPlacementResult?>? PlacementRule { get; set; }
	/// <summary>
	/// The landscape words and the procedural ground the level's placed objects put their skirt on while the procedural terrain textures are on (<see cref="FloorBaseContext"/>); null from the host
	/// when the textures are off or the level has none, and a park without terrain has no such host.
	/// </summary>
	internal Func<FloorBaseContext?>? FloorBase { get; set; }
	// objects registered since the last footprint refresh (a park load creates every instance before it refreshes the cells); refreshNow is set while a live placement registers
	private readonly List<OriginalObject> pendingRefresh = new();
	private bool refreshNow;

	/// <summary>
	/// The cell refresh of the objects a load placed (tp.exe <c>0x415140</c> → <c>0x4D83A0</c> refreshes all 128 × 128 cells after the instances of <c>0x4647A0</c> exist, so every skirt cell was still open to
	/// a neighbour's composite scan when the instances were made): each footprint cell takes the hole word. A live placement does this itself, at once.
	/// </summary>
	internal void CommitFootprintRefresh()
	{
		if ( pendingRefresh.Count == 0 )
			return;
		if ( FloorBase?.Invoke() is { } context )
		{
			foreach ( var item in pendingRefresh.Where( objects.Contains ) )
				item.RefreshFootprint( context.Words );
			context.ReleaseArt();
		}
		pendingRefresh.Clear();
	}
	public event Action<OriginalObject>? ObjectPlaced;
	public event Action<OriginalObject>? ObjectRemoved;

	public OriginalObject? FindAt( int x, int y ) => occupied.TryGetValue( (x, y), out var item ) ? item : null;

	public bool IsOccupied( int x, int y ) => occupied.ContainsKey( (x, y) ) || IsReserved?.Invoke( x, y ) == true;

	/// <summary>Build rule for a catalog object anchored at (x, y) with a rotation.</summary>
	public OriginalPlacementResult Check( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation ) =>
		Check( entry, anchorX, anchorY, rotation, out _ );

	/// <summary>
	/// <see cref="Check(ObjectCatalogEntry, int, int, int)"/> with what the commit changes besides the new footprint (<see cref="ObjectPlacementPlan"/>):
	/// an object whose <see cref="ObjectCatalogEntry.OverwritePriority"/> is below the new one's gives way (a ride over a bush) and is
	/// demolished, a path cell under the footprint is cleared, and any other overlap refuses. The outside cells of the entrances and exits
	/// must pass the rules the original lays its access cells with.
	/// </summary>
	// [APPROX:RIDES-018] Build rules = footprint inside grid + the original's map-start cell types (holes refused, OriginalParkPlacement) + save paths/objects + overlap by overwrite priority + plain path cells cleared + owned land + the access cells' rules; no slope test, as in tp.exe's validator 0x535670; Level.PlaceObject enforces economy purchases — evidence needed: the second-layer link tests 0x53AE00 and 0x53AE90 (a corner or junction on the cell's second layer, 0x535BD7), and which types carry +0x288 = 1, whose Bumper.WhichTrackType turns the access-cell option off at the build tool's enter (0x46D605; OpenTPW follows Info.HasQueue alone)
	// [BIN:TPW-EXE:0x5357c7 placement validator] after its early returns for a few tool modes the validator 0x535670 refuses a cell with flag 0x40 for whatever it places, paths, queues and objects alike
	// [BIN:TPW-EXE:0x535D66 overwrite priority] a cell of type 4, 9 or 10 is allowed when 0x818C10, the placed type's +0x54 (Info.OverwritePriority, sam schema 0x745020; set by the hover 0x5234D0), is greater than 0x818C14, the +0x54 of the type of the object owning the cell (0x53570E: the thing at the cell's owner index +0x10, class 3); otherwise the cell is refused and the sticky refusal 0x816D50 set
	public OriginalPlacementResult Check( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, out ObjectPlacementPlan plan )
	{
		var overwritten = new List<OriginalObject>();
		var paths = new List<(int X, int Y)>();
		plan = ObjectPlacementPlan.None;
		var result = CheckFootprint( entry, anchorX, anchorY, rotation, overwritten, paths );
		if ( result == OriginalPlacementResult.Allowed && IsUnowned is { } unowned )
		{
			foreach ( var (x, y, _) in ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation ) )
			{
				if ( unowned( x, y ) )
					return OriginalPlacementResult.NotOwned;
			}
		}
		if ( result == OriginalPlacementResult.Allowed )
			result = CheckAccessCells( entry, anchorX, anchorY, rotation, overwritten );
		if ( result == OriginalPlacementResult.Allowed && PlacementRule?.Invoke( entry, anchorX, anchorY, rotation ) is { } refused )
			result = refused;
		if ( result == OriginalPlacementResult.Allowed )
			plan = new ObjectPlacementPlan( overwritten, paths );
		return result;
	}

	/// <summary>Whether a path (<see cref="PathOverwritePriority"/>) or queue (<see cref="QueueOverwritePriority"/>) tool may lay its cell over the object at (x, y), demolishing it: its overwrite priority is below the tool's.</summary>
	// [BIN:TPW-EXE:0x524571 tool priority] the hover 0x5234D0 sets 0x818C10 to 2 for the path tool (tool 1) and 3 for the queue tool (tool 3), as the click 0x524960 does at 0x527200 and 0x527218; the validator 0x535670 compares it with the owner's +0x54 for a type 4, 9 or 10 cell before any path or queue rule (0x535D66), so a path or queue may cover a feature of priority 0 or 1 but no ride, shop or sideshow (5)
	public bool GivesWay( int x, int y, int toolPriority ) => FindAt( x, y ) is { } item && item.Entry.OverwritePriority < toolPriority;

	/// <summary>The overwrite priority the path tool places with (tp.exe <c>0x818C10</c> = 2).</summary>
	public const int PathOverwritePriority = 2;
	/// <summary>The overwrite priority the queue tool places with (tp.exe <c>0x818C10</c> = 3).</summary>
	public const int QueueOverwritePriority = 3;

	/// <summary>
	/// <see cref="Check(ObjectCatalogEntry, int, int, int)"/> for a site that demolishes nothing and clears no path: an overwrite or a cleared path
	/// counts as occupied (fixtures and demo builders keep their own layout).
	/// </summary>
	public OriginalPlacementResult CheckFree( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation )
	{
		var result = Check( entry, anchorX, anchorY, rotation, out var plan );
		return result == OriginalPlacementResult.Allowed && !plan.KeepsTheRest ? OriginalPlacementResult.Occupied : result;
	}

	private OriginalPlacementResult CheckFootprint( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, List<OriginalObject> overwritten, List<(int X, int Y)> paths )
	{
		if ( !ObjectFootprint.IsValidRotation( rotation ) )
			throw new ArgumentOutOfRangeException( nameof( rotation ) );
		if ( entry.IsTool )
			return OriginalPlacementResult.Blocked;
		foreach ( var (x, y, cell) in ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation ) )
		{
			// a '+' or '#' cell lies on its ride's track: the add-on rule judges it instead of the occupancy rules
			if ( AddOnCellRule != null && cell.CellType is ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash )
			{
				if ( x < 0 || y < 0 || x >= Grid.Width || y >= Grid.Height )
					return OriginalPlacementResult.OutsideTerrain;
				var terrain = Grid.CheckTerrain( x, y );
				if ( terrain != OriginalPlacementResult.Allowed )
					return terrain;
				if ( !AddOnCellRule( x, y ) )
					return OriginalPlacementResult.Blocked;
				// [EXT:addon-not-on-station] the original's add-on test counts the station tiles as track (types 13 and 18, 0x0053AD30) and would let an add-on take the ride's own cells, which its sale then leaves empty under the station; OpenTPW refuses a cell another object holds
				if ( FindAt( x, y ) != null )
					return OriginalPlacementResult.Occupied;
				continue;
			}
			var result = CheckObjectCell( entry, x, y, overwritten );
			if ( result != OriginalPlacementResult.Allowed )
				return result;
			if ( PathAt?.Invoke( x, y ) == ObjectPathCell.Path && !paths.Contains( (x, y) ) )
				paths.Add( (x, y) );
		}
		// [BIN:TPW-EXE:0x005295A8 track end beyond cell] a track end (type 23: '<', '>', 'D') needs the cell beyond it in its rotated direction (0x4D97E0 answers none off the map or for a symbol without a direction), and the placement is refused when there is none
		foreach ( var (x, y, cell) in ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation ) )
		{
			if ( cell.CellType != ObjectShapeSymbols.TypeTrackEnd )
				continue;
			var direction = GuestPathGrid.LinkDirection( ObjectFootprint.RotateDirectionByte( cell.Direction, rotation ) );
			if ( direction < 0 ) return OriginalPlacementResult.OutsideTerrain;
			var bx = x + GuestPathGrid.Directions[direction].DX;
			var by = y + GuestPathGrid.Directions[direction].DY;
			if ( bx < 0 || by < 0 || bx >= Grid.Width || by >= Grid.Height )
				return OriginalPlacementResult.OutsideTerrain;
			// [BIN:TPW-EXE:0x5295D7,0x5295D9] station endpoints use object mode4, not the node tool's same-owner marker exemption.
			if ( TrackEndCellRule?.Invoke( bx, by ) == false ) return OriginalPlacementResult.Occupied;
			var beyond = CheckObjectCell( entry, bx, by, overwritten );
			if ( beyond != OriginalPlacementResult.Allowed ) return beyond;
			if ( PathAt?.Invoke( bx, by ) == ObjectPathCell.Path && !paths.Contains( (bx, by) ) ) paths.Add( (bx, by) );
		}
		return OriginalPlacementResult.Allowed;
	}

	/// <summary>
	/// The validator's rule for one cell of type 4 (a footprint cell, or an entrance's outside cell): terrain, a fixed (NoModify) path refused and a
	/// plain one allowed, an object by overwrite priority, the host's reserved cells.
	/// </summary>
	// [BIN:TPW-EXE:0x5356B5 NoModify] a cell with flag 0x20 (the map start's initial path, 0x536490) refuses type 4, 9 and 10 before any other test
	// [BIN:TPW-EXE:0x535D21 object over path] type 4 over a type-1 cell returns 0 (allowed); the commit's cell op 0x87 clears the path with ClearCell 0x5367A0(0, 0) when 0x818C14 is below 0x818C10 (0x534549, the build tool being neither path nor queue), and the footprint write clears every cell with ClearCell(0, 0) anyway before typing it (0x52910C), so the path goes whatever the priorities; the path case of ClearCell spends nothing (ParkPathBuilder.Remove)
	private OriginalPlacementResult CheckObjectCell( ObjectCatalogEntry entry, int x, int y, List<OriginalObject> overwritten )
	{
		if ( x < 0 || y < 0 || x >= Grid.Width || y >= Grid.Height )
			return OriginalPlacementResult.OutsideTerrain;
		var path = PathAt?.Invoke( x, y );
		if ( path == ObjectPathCell.FixedPath )
			return OriginalPlacementResult.Path;
		var terrain = Grid.CheckTerrain( x, y );
		// With a live cell map the terrain rule's path cells (the save's and the map start's) are the map's to judge: a path still there was tested above.
		if ( terrain == OriginalPlacementResult.Path && path != null )
			terrain = OriginalPlacementResult.Allowed;
		if ( terrain != OriginalPlacementResult.Allowed )
			return terrain;
		// the host's reservation (queue and track cells, the Totem) refuses whatever owns the cell
		if ( IsReserved?.Invoke( x, y ) == true )
			return OriginalPlacementResult.Occupied;
		return CheckOwner( entry, x, y, overwritten ) ?? OriginalPlacementResult.Allowed;
	}

	/// <summary>The object at (x, y) against the placed type's overwrite priority: null without one, Occupied when it does not give way.</summary>
	private OriginalPlacementResult? CheckOwner( ObjectCatalogEntry entry, int x, int y, List<OriginalObject> overwritten )
	{
		if ( !occupied.TryGetValue( (x, y), out var item ) )
			return null;
		if ( item.Entry.OverwritePriority >= entry.OverwritePriority )
			return OriginalPlacementResult.Occupied;
		if ( !overwritten.Contains( item ) )
			overwritten.Add( item );
		return OriginalPlacementResult.Allowed;
	}

	/// <summary>
	/// The outside cells of the entrances and exits, by the rules the original lays its access cells with: a type with a queue
	/// (<see cref="ObjectCatalogEntry.HasQueue"/>) has its entrance's outside cell checked as an object cell (it becomes the queue's first cell)
	/// and its exit's by the path rule (<see cref="PathRuleAllows"/>); any other type has its entrance's checked by the path rule and its exit's
	/// not at all. An object there of a lower priority gives way. Refusals are <see cref="OriginalPlacementResult.AccessBlocked"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x529744 access cells] the placement refuses an entrance's outside cell off the map (0x4D8300); with 0x52FAB0 true the check pass validates it as type 4 (op 0x204, 0x5297A4; the commit lays a queue cell there, 0x529801), else as a path (op 0x201, 0x5298B9; the commit lays a path); only with 0x52FAB0 true does it test the exit's outside cell, off the map refused (0x5299E2), as a path (op 0x201, 0x529A2F). The commit's op 0x87 on either cell (0x529801, 0x529A61, 0x5298EE) demolishes a lower-priority owner
	// [BIN:TPW-EXE:0x46D644 queue option] 0x52FAB0 is option 0x81B0CC while 0x8187F8 is clear; entering the build tool (tool 4 or 0x3B, 0x46D5A0) sets 0x81B0CC to the type's Info.HasQueue (+0x40) and 0x8187F8 through 0x52F880 to its Bumper.WhichTrackType (+0x9C) when the type's +0x288 is 1, else 0
	private OriginalPlacementResult CheckAccessCells( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, List<OriginalObject> overwritten )
	{
		foreach ( var point in ObjectFootprint.GetAccessPoints( entry.Shape, anchorX, anchorY, rotation ) )
		{
			if ( point.Kind == ObjectCellKind.Exit && !entry.HasQueue )
				continue;
			var (x, y) = (point.OutsideX, point.OutsideY);
			if ( x < 0 || y < 0 || x >= Grid.Width || y >= Grid.Height )
				return OriginalPlacementResult.OutsideTerrain;
			var result = point.Kind == ObjectCellKind.Entrance && entry.HasQueue ? CheckObjectCell( entry, x, y, overwritten ) : CheckPathRule( entry, x, y, overwritten );
			if ( result == OriginalPlacementResult.Allowed && IsUnowned?.Invoke( x, y ) == true )
				result = OriginalPlacementResult.NotOwned;
			if ( result != OriginalPlacementResult.Allowed )
				return OriginalPlacementResult.AccessBlocked;
		}
		return OriginalPlacementResult.Allowed;
	}

	/// <summary>The validator's path rule for an access cell: an object by the placed type's priority, else the host's path rule (without one: the terrain and the reserved cells).</summary>
	private OriginalPlacementResult CheckPathRule( ObjectCatalogEntry entry, int x, int y, List<OriginalObject> overwritten )
	{
		if ( CheckOwner( entry, x, y, overwritten ) is { } owner )
			return owner;
		if ( PathRuleAllows?.Invoke( x, y ) is { } allowed )
			return allowed ? OriginalPlacementResult.Allowed : OriginalPlacementResult.Occupied;
		var terrain = Grid.CheckTerrain( x, y );
		if ( terrain is not (OriginalPlacementResult.Allowed or OriginalPlacementResult.Path) )
			return terrain;
		return IsReserved?.Invoke( x, y ) == true ? OriginalPlacementResult.Occupied : OriginalPlacementResult.Allowed;
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
	// [BIN:TPW-EXE:0x52910C footprint clear] each footprint cell then gets ClearCell 0x5367A0(0, 0) before its type is written (op 4 / 9 / 10, then the link op 0x81)
	public OriginalObject? TryPlace( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, out OriginalPlacementResult result, bool open = true, Action<OriginalObject>? registered = null )
	{
		result = Check( entry, anchorX, anchorY, rotation, out var plan );
		if ( result != OriginalPlacementResult.Allowed )
			return null;
		// the new object is built (models, script) before anything is demolished or cleared, so a failed build leaves the park as it was;
		// like Add, its script draws from the world generator (DET-013)
		var item = new OriginalObject( entry, ComputePlacement( entry, anchorX, anchorY, rotation ), ScriptWorld, nextSeed++ ^ scriptKey, open, attractionId: null, seedScript: false );
		foreach ( var victim in plan.Overwritten )
			Remove( victim );
		foreach ( var (x, y) in plan.ClearedPaths )
			ClearPath?.Invoke( x, y );
		// a live placement refreshes its cells at once; the cells of earlier loads were refreshed already
		CommitFootprintRefresh();
		refreshNow = true;
		try
		{
			return Register( item, registered: registered );
		}
		finally
		{
			refreshNow = false;
		}
	}

	/// <summary>Places an object without build rules (imported original records and fixed items).</summary>
	public OriginalObject Add( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, bool open = true ) =>
		// the placement ordinal stays the object's creation seed (identity and save bookkeeping); its script draws from the world generator like the original's (DET-013)
		Register( new OriginalObject( entry, ComputePlacement( entry, anchorX, anchorY, rotation ), ScriptWorld, nextSeed++ ^ scriptKey, open, attractionId: null, seedScript: false ) );

	/// <summary>
	/// Places a saved object back (<see cref="ParkLayoutSnapshot"/> restore): the placement rules of <see cref="Add"/>
	/// with the record's own creation seed instead of the allocator's next value, and its saved attraction id instead
	/// of a fresh allocation — the object's runtime adopts the id, so guests, scripts and the economy stay linked to
	/// the exact identity they were saved with, in constant time (ids of removed objects are never replayed). Like
	/// Add it applies no build rules and charges nothing; the seed allocator itself is restored separately with
	/// <see cref="RestoreSeedAllocator"/> so objects placed after the load continue the saved seed sequence.
	/// </summary>
	internal OriginalObject AddRestored( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, bool open, int seed, int attractionId, bool legacyBaseHeight = false, int thingId = 0 ) =>
		Register( new OriginalObject( entry, ComputePlacement( entry, anchorX, anchorY, rotation, legacyBaseHeight ), ScriptWorld, seed, open, attractionId, seedScript: false ), thingId );

	// [DATA:Easymode.TPWI:SYSG X/Y/rotation] [DATA:<fixed item>.MD2:park-space coordinates]
	// [DATA:PreBuilt/prebuilt.TPWS:SYSG X/Y/rotation] TPI fixed models use local coordinates and their saved anchor.
	internal ObjectPlacement ComputePlacement( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, bool legacyBaseHeight = false ) =>
		entry.IsFixedItem && !GameEditions.IsThemeParkInc
			? new ObjectPlacement( 0, 0, 0, Grid.Origin, 0 )
			: new ObjectPlacement( anchorX, anchorY, rotation, Grid.Origin,
				entry.IsFixedItem ? 0 : legacyBaseHeight ? GetLegacyBaseHeight( entry, anchorX, anchorY, rotation ) : GetBaseHeight( entry, anchorX, anchorY, rotation ),
				legacyBaseHeight && !entry.IsFixedItem );

	/// <param name="item">The object to register.</param>
	/// <param name="thingId">A thing id the object keeps (a save's, adopted from the pool); 0 takes the next slot of the pool.</param>
	// [BIN:TPW-EXE:0x004DB090 object constructor] the attraction's constructor takes its thing id through the base constructor 0x50AFE0 and the pool's allocator (0x516270), the same pool as every other kind of thing
	/// <param name="registered">Called once the object is in the park, before <see cref="ObjectPlaced"/> runs its handlers: a caller that must know the object even when a handler throws (the build flow's economy binding) takes it here.</param>
	private OriginalObject Register( OriginalObject item, int thingId = 0, Action<OriginalObject>? registered = null )
	{
		if ( Things is { } pool )
		{
			if ( thingId == 0 )
				item.ThingId = pool.Allocate( SaveThingModel.Attraction ) is var taken and not 0 ? taken : throw new InvalidOperationException( $"The thing pool is full ({ThingPool.Capacity} things); {item.Entry.DisplayName} cannot be built." );
			else if ( pool.IsUsed( thingId ) ? pool.ModelOf( thingId ) != SaveThingModel.Attraction || objects.Any( other => other.ThingId == thingId ) : !pool.Occupy( thingId, SaveThingModel.Attraction ) )
				throw new InvalidDataException( $"The thing id {thingId} of {item.Entry.DisplayName} is not a free attraction slot of the thing pool." );
			else
				item.ThingId = thingId;
		}
		objects.Add( item );
		Version++;
		foreach ( var (x, y, _) in item.Cells )
			occupied[(x, y)] = item;
		// [BIN:TPW-EXE:0x0046334C skirt base] the instance's creation (0x463060) writes the skirt words and makes the base composites once its placement is done, after the demolished objects' cells were restored
		if ( FloorBase?.Invoke() is { } floorBase )
		{
			item.PlaceFloorBase( floorBase.Words, floorBase.Ground, floorBase.Terrain );
			if ( refreshNow )
			{
				item.RefreshFootprint( floorBase.Words );
				floorBase.ReleaseArt();
			}
			else
				pendingRefresh.Add( item );
		}
		registered?.Invoke( item );
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
	/// Base height of an object: the midpoint of the lowest and the highest terrain vertex under its footprint. The vertices are the four corners of every shape cell whose cell type has a
	/// non-zero low nibble (so <c>#</c>, type 16, adds none), at the anchor and rotation. The original does not flatten the ground under the ride: the floor meets the terrain through the skirt and the edge conform (<see cref="ObjectGroundConform"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x00466D70 base height] the creator 0x463060 (flags 0x32f, bit 8, shape pointer def+0x18) calls 0x466D70, which samples the four corners of each shape cell (entries 8 bytes, low nibble of the first byte non-zero, 0x469930) through 0x454050 and returns (lowest + highest vertex height) x 0.5 (0x466E60..0x466F70); the vertex of a corner is ftol(lerp + 0.1) of the unrotated shape position turned about the anchor, which is exactly ObjectFootprint.ToGrid of the corner (box origin 0x466F90)
	// [BIN:TPW-EXE:0x00467030 footprint box] the placement 0x467030 (creation flag 2) only places the instance and stores the footprint box in +0xC0/+0xC4 (read by the hoarding conform and the terrain composite); it blends no ground flat, and 0x57ACE0 is the composite texture of the floor art over the procedural ground, not a deformation. The instance words +0xF0/+0xF4 are the shape's width and height (0x4629D0: shape words 0 and 1, params 10/11 are 0 for objects). tp.exe's own 0x466D70 run in the x86 oracle (tpw-exe-base-height) returns this rule's result for every synthetic footprint and heightfield tried (rotations 0 to 270, random shapes and heights)
	public float GetBaseHeight( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation ) => GetBaseHeight( entry.Shape, Grid, anchorX, anchorY, rotation );

	/// <summary><see cref="GetBaseHeight(ObjectCatalogEntry,int,int,int)"/> for a shape on a grid.</summary>
	internal static float GetBaseHeight( ObjectShape shape, IParkGrid grid, int anchorX, int anchorY, int rotation )
	{
		var lowest = float.PositiveInfinity;
		var highest = float.NegativeInfinity;
		foreach ( var cell in shape.Cells.Where( cell => (cell.CellType & 0xF) != 0 ) )
		{
			for ( var corner = 0; corner < 4; corner++ )
			{
				var point = ObjectFootprint.ToGrid( anchorX, anchorY, rotation, new System.Numerics.Vector2( cell.U + (corner & 1), cell.V + (corner >> 1) ) );
				var height = grid.GetCornerHeight( (int)MathF.Round( point.X ), (int)MathF.Round( point.Y ) );
				lowest = Math.Min( lowest, height );
				highest = Math.Max( highest, height );
			}
		}
		return float.IsInfinity( lowest ) ? 0 : (lowest + highest) * 0.5f;
	}

	/// <summary>The earlier rule, the mean ground height of the footprint's cells: only a park-resume save made before the vertex rule keeps it (<see cref="ParkLayoutSnapshot"/>).</summary>
	internal float GetLegacyBaseHeight( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation )
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
			Version++;
		}
		finally
		{
			removing.Remove( item );
		}
		foreach ( var key in occupied.Where( pair => pair.Value == item ).Select( pair => pair.Key ).ToArray() )
			occupied.Remove( key );
		item.Delete();
		// [BIN:TPW-EXE:0x00527EE0 demolition] the demolition clears every footprint cell (0x5367A0, then the refresh 0x5365D0 of the type 0 cell: the backup word returns, 0x46DF50 mode 0x37); a level that clears its park does not bother
		pendingRefresh.Remove( item );
		if ( !clearing && FloorBase?.Invoke() is { } cleared )
			foreach ( var (x, y) in item.FloorWordCells )
				cleared.Words.Restore( x, y );
		// [BIN:TPW-EXE:0x00527EE0 object demolition] the demolition asks for the thing's destruction (0x0050B780 → 0x00516310), which the turn's end carries out; a park that clears itself frees the slots at once (0x00515FB0)
		if ( Things is { } pool && item.ThingId != 0 )
		{
			if ( clearing )
				pool.Release( item.ThingId );
			else
				pool.QueueDestroy( item.ThingId );
		}
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
	/// One fixed tick of the original's substep scheduler (tp.exe main loop <c>0x0054F4BF</c>, <see cref="SimulationSubstep"/>): in a substep that is due the bump module ticks (0x54F55F), then the script pass runs every
	/// script newest first on its own cadence (0x54F56B, 0x5516B0); the objects' own ticks follow, the tour cars among them on even substeps (0x54F5D7). A paused park runs none of it.
	/// </summary>
	internal void Simulate( double deltaSeconds, SimulationSubstep substep, OriginalObject? prototype = null )
	{
		ScriptWorld.Bumpers.HostDriven = true;
		if ( substep.Due )
		{
			ScriptWorld.Bumpers.StepSubstep();
			ScriptWorld.RunPass( SimulationSubstep.Milliseconds );
		}
		if ( substep.Running )
			ScriptWorld.Bumpers.PresentAt( substep.SecondsSinceModuleStep );
		prototype?.Simulate( deltaSeconds, substep );
		foreach ( var item in objects )
			item.Simulate( deltaSeconds, substep );
	}

	/// <summary>
	/// Replaces the Easymode markers with the real objects: every SYSG placed record at its saved anchor and
	/// rotation (unresolved non-square rotations use the catalog shape), and the fixed items in park space.
	/// Imported objects are opened (the saved open word is not imported, SAVE-006). Each object takes the hoarding state, node states and animation channels of its RideSystem record (<see cref="OriginalObjectRuntime.RestoreSavedScene"/>); what could not be restored is listed in <see cref="SavedSceneGaps"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x004DB712 built ride closes] a built object opens in the constructor (open word +0x68 = 1, 0x4DB3B9) and closes again when its type has Info.HasQueue (Level.BuiltObjectStartsOpen); an imported object is not built: its open word comes from the save in the original (mCanLoad), which this port does not import and opens every imported object instead
	// [APPROX:SAVE-006] an imported object's saved open word and its sign lines other than the park gate's (0x75B5EC) are not taken from the save (the staff claims, 0x004E01F0, are imported as saved)
	// [EXT:original-engine-state-dumps] its raw links (mesh instance, script and track handles, flags, footprint, entrance and exit cells, upgrade parent, balloon sprite) are not taken from the save either: the placement and the queue rebuild them in OpenTPW's own engines (user-approved 2026-10-16)
	public int ImportOriginal( OriginalParkImport save ) => ImportOriginalSteps( save ).LastOrDefault().Placed;

	private readonly List<string> savedSceneGaps = new();

	/// <summary>What <see cref="ImportOriginal"/> could not restore of the saved scene records' node states and animation controls, one line each.</summary>
	public IReadOnlyList<string> SavedSceneGaps => savedSceneGaps;

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
			// [APPROX:RIDES-040] TPI fixed scene records at (0, 0) use their local model origin; the thing records of the six that have an archive carry the fixed-item default position (128, 128) and angle 0 (the five without an archive carry real positions, but nothing to draw), so no transform field is left unread in the save — evidence needed: how the original places a local-coordinate fixed model at its anchor (a thing's position bytes are decoded as (cell + fraction / 256) x 10 only by the camera-target setters 0x503490 and 0x5A1100 of the no-CD Theme Park Inc exe, no model placement reads them there)
			// the object keeps its saved thing id when the save's attraction thing is the one record that matches (the pool holds the saved chain)
			var thingId = Things != null && save.FindAttraction( record ) is { } thing && Things.IsUsed( thing.Id ) && Things.ModelOf( thing.Id ) == SaveThingModel.Attraction
				&& objects.All( other => other.ThingId != thing.Id ) ? thing.Id : 0;
			var imported = Register( new OriginalObject( entry, ComputePlacement( entry, record.X, record.Y, record.Rotation ), ScriptWorld, nextSeed++ ^ scriptKey, true, attractionId: null, seedScript: false ), thingId );
			// [BIN:TPW-EXE:0x004647A0 hoarding load] the RideSystem record's flag word +0x1C bits 0..6 and its +0x22 dword are the thing's hoarding flags and level (docs/OBJECTS.md, "Hoarding")
			if ( save.RideSystem?.Records.FirstOrDefault( scene => scene.Offset == record.Offset ) is { } scene )
			{
				imported.Runtime.Hoarding.Restore( ObjectHoardingState.UnpackSavedFlags( scene.InstanceFlags ), BitConverter.UInt32BitsToSingle( scene.InstanceDword ) );
				// [BIN:TPW-EXE:0x004647A0 scene record load] the same record's node states and animation time controls (OriginalObjectRuntime.RestoreSavedScene) on the save's game clock
				foreach ( var gap in imported.Runtime.RestoreSavedScene( scene, save.RuntimeBlocks?.ClockWords[0] ) )
				{
					var line = $"{entry.DisplayName} (RideSystem slot {scene.Slot}): {gap}.";
					savedSceneGaps.Add( line );
					Log.Trace( $"Original scene import: {line}" );
				}
			}
			count++;
			yield return (count, (index + 1) / (float)records.Count);
		}
		yield return (count, 1);
	}

	/// <summary>Gives the imported objects' scripts the ids, list order and pass counter of an original save's RSSE block (<see cref="SavedScriptImport"/>, DET-013); returns the report lines.</summary>
	public IReadOnlyList<string> ImportOriginalScripts( SaveRsseBlock rsse, uint? clockWord = null ) =>
		SavedScriptImport.Apply( ScriptWorld, rsse, objects.Where( item => item.Script != null ).Select( item => new SavedScriptImport.Candidate( item.ThingId, item.Entry, item.Script!, item.Runtime.Visitors ) ), clockWord );

	/// <summary>
	/// For levels without an original save: the fixed items the original creates at world setup, Gates and Lights, in park space.
	/// The arrival vehicles (bus, seaplane, Ferry) are placed when the arrival update first sends them (<see cref="ArrivalVehicles"/>);
	/// a Theme Park Inc world still gets the Bus here.
	/// </summary>
	// [BIN:TPW-EXE:0x005156A0 world setup fixed items] world setup creates the definitions named "gates" (wide literal 0x762448) and "lights" (0x762454) through 0x4db090 (case-insensitive compare 0x67b140); a missing definition logs "Could not find the gates feature" or the LIGHTSNAME message; a loaded save then restores the handles (writer 0x517076)
	// [BIN:TPW-EXE:0x0051A2F0 vehicle on demand] the bus, seaplane and Ferry are created by the vehicle dispatch the first time each is sent (0x004DB090 at 0x0051A57E), never at setup
	// End is not a setup object either: ParkFinale creates it only on a new research completion that starts the finale (0x503D40 -> 0x516B00, after advice 0xA7: Instant Action, and the Full Simulation when the player's records show every park complete, 0x5B0C40), and its dismissal destroys it (0x48A970 -> 0x516C10); see docs/reverse/INSTANT-ACTION-FINALE.md.
	public int AddDefaultFixedItems() => AddDefaultFixedItemSteps().LastOrDefault().Placed;

	/// <summary><see cref="AddDefaultFixedItems"/> one object at a time, as <see cref="ImportOriginalSteps"/>.</summary>
	internal IEnumerable<(int Placed, float Share)> AddDefaultFixedItemSteps()
	{
		// [EXT:tpi-bus-from-setup] a Theme Park Inc world without a prebuilt save gets its Bus at setup where the Theme Park World arrival vehicles are placed on first dispatch: no original reading of how Theme Park Inc places its arrival vehicle is available, and a bus standing from the start keeps the viewer's park alive
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
