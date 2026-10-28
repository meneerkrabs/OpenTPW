namespace OpenTPW;

/// <summary>The kinds of water attraction objects of the pools extension (docs/POOLS.md, "Water attractions").</summary>
// [EXT:pools-attractions] OpenTPW's own objects; the original game has no pools
public enum PoolAttractionKind
{
	/// <summary>Not a water attraction.</summary>
	None,
	/// <summary>A rope swing on the pool edge: the guest climbs, swings, lets go and splashes into the pool.</summary>
	RopeSwing,
	/// <summary>A splash cannon or water jets on the deck: sprays the water in front of it; swimmers can stand under a jet.</summary>
	Jets,
	/// <summary>A wave machine on the pool edge: its pool's surface runs waves while it is on.</summary>
	WaveMachine,
	/// <summary>A spa pool beside the pool: one tile with one seat, or the large one (2 x 2 tiles) with four.</summary>
	Spa
}

/// <summary>
/// One placed water attraction as the swim session sees it: the object, where it stands and the pool cell beside it where the swimmers meet it. Made by <see cref="PoolAttractionSites"/>
/// from the placed objects, never stored.
/// </summary>
/// <param name="ObjectId">The object's thing id (<c>OriginalObject.ThingId</c>), what a guest saves as its activity target.</param>
/// <param name="Kind">What the object is.</param>
/// <param name="Cells">The footprint cells.</param>
/// <param name="Access">The first water-side cell that holds pool water: where swimmers leave the water for the attraction or land from it.</param>
// [EXT:pools-attractions] OpenTPW's own objects; the original game has no pools
public sealed record PoolAttractionSite( int ObjectId, PoolAttractionKind Kind, IReadOnlyList<(int X, int Y)> Cells, (int X, int Y) Access )
{
	/// <summary>The centre of the footprint as a 16.16 point.</summary>
	public (int X, int Y) CentreFixed
	{
		get
		{
			long x = 0, y = 0;
			foreach ( var (cx, cy) in Cells )
			{
				x += cx;
				y += cy;
			}
			return ((int)((x * Fixed16.One / Cells.Count) + Fixed16.One / 2), (int)((y * Fixed16.One / Cells.Count) + Fixed16.One / 2));
		}
	}
}

/// <summary>
/// The water attraction objects of the pools extension (docs/POOLS.md, "Water attractions"): data-only objects whose <c>.sam</c> says <c>Pools.Attraction ropeswing</c> (or <c>jets</c>,
/// <c>wavemachine</c>, <c>spa</c>) and <c>Pools.WaterSide N</c> (the compass side of the unrotated shape that faces the pool: N is the shape's top row, E, S, W the others, <c>*</c> any side).
/// Like a changing room's back, the water side of a placed attraction is the cells just outside the footprint on that side (rotated with the object); a water attraction may only be placed
/// with at least one of them holding pool water. Guests never walk to these objects from the park: they reach them from the water (docs/POOLS.md).
/// </summary>
// [EXT:pools-attractions] OpenTPW's own objects and placement rule; the original game has no pools
public static class PoolAttractions
{
	/// <summary>The .sam key that names the kind of a water attraction (<c>ropeswing</c>, <c>jets</c>, <c>wavemachine</c>, <c>spa</c>).</summary>
	public const string KindKey = "Pools.Attraction";
	/// <summary>The .sam key for the shape side that faces the pool: N, E, S, W or *.</summary>
	public const string WaterSideKey = "Pools.WaterSide";

	/// <summary>The kind of an object, or <see cref="PoolAttractionKind.None"/> when it is none or the key's value is not known.</summary>
	public static PoolAttractionKind KindOf( ObjectCatalogEntry entry )
	{
		ArgumentNullException.ThrowIfNull( entry );
		return ParseKind( entry.Settings[KindKey] );
	}

	/// <summary>Parses a .sam kind value (case ignored).</summary>
	public static PoolAttractionKind ParseKind( string? value ) => value?.Trim().ToLowerInvariant() switch
	{
		"ropeswing" => PoolAttractionKind.RopeSwing,
		"jets" => PoolAttractionKind.Jets,
		"wavemachine" => PoolAttractionKind.WaveMachine,
		"spa" => PoolAttractionKind.Spa,
		_ => PoolAttractionKind.None
	};

	/// <summary>The unrotated shape directions (u, v) of a <c>Pools.WaterSide</c> value; defaults to north (the shape's top row, the cabin's back) for a missing or unknown one.</summary>
	public static IReadOnlyList<(int U, int V)> WaterSides( ObjectCatalogEntry entry )
	{
		ArgumentNullException.ThrowIfNull( entry );
		var value = entry.Settings[WaterSideKey]?.Trim().ToUpperInvariant() ?? "N";
		return value switch
		{
			"*" or "ANY" => new[] { (0, -1), (1, 0), (0, 1), (-1, 0) },
			"E" => new[] { (1, 0) },
			"S" => new[] { (0, 1) },
			"W" => new[] { (-1, 0) },
			_ => new[] { (0, -1) }
		};
	}

	/// <summary>
	/// The water-side candidates of an attraction anchored at (<paramref name="anchorX"/>, <paramref name="anchorY"/>) with the rotation: for every footprint cell and every water side, the neighbour in that
	/// (rotated) direction when it is not part of the footprint, in footprint order, each cell once.
	/// </summary>
	public static IReadOnlyList<(int X, int Y)> AccessCandidates( ObjectShape shape, IReadOnlyList<(int U, int V)> sides, int anchorX, int anchorY, int rotation )
	{
		ArgumentNullException.ThrowIfNull( shape );
		ArgumentNullException.ThrowIfNull( sides );
		var footprint = ObjectFootprint.GetCells( shape, anchorX, anchorY, rotation ).Select( cell => (cell.X, cell.Y) ).ToList();
		var inside = footprint.ToHashSet();
		var candidates = new List<(int X, int Y)>();
		foreach ( var (x, y) in footprint )
			foreach ( var (du, dv) in sides )
			{
				var (dx, dy) = ObjectFootprint.RotateDirection( rotation, du, dv );
				var next = (X: x + dx, Y: y + dy);
				if ( !inside.Contains( next ) && !candidates.Contains( next ) )
					candidates.Add( next );
			}
		return candidates;
	}

	/// <summary>The water-side candidates of a catalog entry placed at the anchor.</summary>
	public static IReadOnlyList<(int X, int Y)> AccessCandidates( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation ) =>
		AccessCandidates( entry.Shape, WaterSides( entry ), anchorX, anchorY, rotation );

	/// <summary>The placement rule: a water attraction needs pool water on its water side. Null (no opinion) for any other object and while the extension is off.</summary>
	public static OriginalPlacementResult? PlacementRule( ObjectCatalogEntry entry, ParkCellMap? cells, int anchorX, int anchorY, int rotation )
	{
		if ( !FeatureGates.Pools || cells == null || KindOf( entry ) == PoolAttractionKind.None )
			return null;
		foreach ( var (x, y) in AccessCandidates( entry, anchorX, anchorY, rotation ) )
			if ( cells.InBounds( x, y ) && cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater )
				return null;
		return OriginalPlacementResult.NeedsPoolWater;
	}
}

/// <summary>
/// The placed water attractions of a park, derived on demand from the placed objects and the cell map (nothing is stored or saved: the objects and the pool cells are the truth). Level owns one and
/// calls <see cref="Invalidate"/> when an object is placed or removed or a pool cell is written; the activities read it through <see cref="All"/>.
/// </summary>
// [EXT:pools-attractions] OpenTPW's own objects; the original game has no pools
public sealed class PoolAttractionSites
{
	private readonly Func<IEnumerable<OriginalObject>> objects;
	private readonly Func<ParkCellMap?> cells;
	private readonly Func<IReadOnlyList<PoolAttractionSite>>? fixedSites;
	private IReadOnlyList<PoolAttractionSite>? cached;
	private int cachedWrites = -1;

	public PoolAttractionSites( Func<IEnumerable<OriginalObject>> objects, Func<ParkCellMap?> cells )
	{
		this.objects = objects ?? throw new ArgumentNullException( nameof( objects ) );
		this.cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
	}

	/// <summary>Sites given directly (tests and the headless sandbox have no placed objects): the list is read on every call.</summary>
	internal PoolAttractionSites( Func<IReadOnlyList<PoolAttractionSite>> sites )
	{
		fixedSites = sites ?? throw new ArgumentNullException( nameof( sites ) );
		objects = static () => Array.Empty<OriginalObject>();
		cells = static () => null;
	}

	/// <summary>Forgets the derived sites (an object was placed or removed).</summary>
	public void Invalidate()
	{
		cached = null;
		Version++;
	}

	/// <summary>Counts every time the derived sites may have changed (an object placed or removed); caches built from the sites key on it with <see cref="ParkCellMap.PoolWrites"/>.</summary>
	public int Version { get; private set; }

	/// <summary>Every placed water attraction whose water side holds pool water now, in placement order. Rebuilt when an object changed or a pool cell was written.</summary>
	public IReadOnlyList<PoolAttractionSite> All
	{
		get
		{
			if ( fixedSites != null )
				return fixedSites();
			var map = cells();
			if ( map == null || !FeatureGates.Pools )
				return Array.Empty<PoolAttractionSite>();
			if ( cached != null && cachedWrites == map.PoolWrites )
				return cached;
			var sites = Derive( map );
			cachedWrites = map.PoolWrites;
			Version++;
			return cached = sites;
		}
	}

	/// <summary>
	/// [EXT:online-coop-undo] The sites as <paramref name="map"/> holds their water, uncached: a take-back's prediction reads its copy of the cells (C5-2). Sites given directly are those.
	/// </summary>
	internal IReadOnlyList<PoolAttractionSite> SitesOn( ParkCellMap map ) => fixedSites != null ? fixedSites() : !FeatureGates.Pools ? Array.Empty<PoolAttractionSite>() : Derive( map );

	/// <summary>Every placed water attraction whose water side holds pool water on <paramref name="map"/>, in placement order, its access cell the first candidate with water.</summary>
	private List<PoolAttractionSite> Derive( ParkCellMap map )
	{
		var sites = new List<PoolAttractionSite>();
		foreach ( var item in objects() )
		{
			var kind = item.IsDeleted ? PoolAttractionKind.None : PoolAttractions.KindOf( item.Entry );
			if ( kind == PoolAttractionKind.None || item.ThingId == 0 )
				continue;     // an object without a thing slot (no park pool) cannot be named by a guest's activity target
			var placement = item.Placement;
			foreach ( var (x, y) in PoolAttractions.AccessCandidates( item.Entry, placement.X, placement.Y, placement.Rotation ) )
				if ( map.InBounds( x, y ) && map.RawTypeAt( x, y ) == ParkCellTypes.PoolWater )
				{
					sites.Add( new PoolAttractionSite( item.ThingId, kind, item.Cells.Select( cell => (cell.X, cell.Y) ).ToArray(), (x, y) ) );
					break;
				}
		}
		return sites;
	}

	/// <summary>The site of the object with this attraction id, or null when it is gone or has no water on its water side.</summary>
	public PoolAttractionSite? Find( int objectId ) => All.FirstOrDefault( site => site.ObjectId == objectId );

	/// <summary>The sites of a kind whose access cell lies in the swim region, in placement order.</summary>
	public IEnumerable<PoolAttractionSite> In( PoolSwimRegion region, PoolAttractionKind kind ) =>
		All.Where( site => site.Kind == kind && region.Contains( site.Access.X, site.Access.Y ) );

	/// <summary>The nearest site of a kind in the region to a point (16.16) that <paramref name="accepts"/> (ties by lower id), without allocating: the activities call it for every swimmer every turn.</summary>
	public PoolAttractionSite? NearestIn( PoolSwimRegion region, PoolAttractionKind kind, int x, int y, Func<PoolAttractionSite, bool> accepts )
	{
		PoolAttractionSite? best = null;
		var bestDistance = int.MaxValue;
		var all = All;
		for ( var i = 0; i < all.Count; i++ )
		{
			var site = all[i];
			if ( site.Kind != kind || !region.Contains( site.Access.X, site.Access.Y ) || !accepts( site ) )
				continue;
			var distance = Fixed16.Octile( x - (site.Access.X * Fixed16.One + Fixed16.One / 2), y - (site.Access.Y * Fixed16.One + Fixed16.One / 2) );
			if ( distance < bestDistance || (distance == bestDistance && best != null && site.ObjectId < best.ObjectId) )
				(best, bestDistance) = (site, distance);
		}
		return best;
	}

	/// <summary>
	/// Whether the pool cell (x, y) is the water side of a water attraction: erasing it would leave the attraction without its pool, so the pool tool refuses it
	/// (<see cref="PoolBuildResult.AttractionAccess"/>), as it does for a changing room. Reads the objects, not the cached sites: the cell must be refused whichever cell of the water side holds the water.
	/// </summary>
	public bool IsAccessCell( int x, int y ) => IsAccessCell( x, y, cells() );

	/// <summary>
	/// <see cref="IsAccessCell(int, int)"/> read on <paramref name="map"/>: [EXT:online-coop-undo] a take-back's prediction asks it of its copy of the cells, so a water cell its walk erased earlier
	/// counts for the later ones (C5-2).
	/// </summary>
	internal bool IsAccessCell( int x, int y, ParkCellMap? map )
	{
		foreach ( var item in objects() )
		{
			if ( item.IsDeleted || PoolAttractions.KindOf( item.Entry ) == PoolAttractionKind.None )
				continue;
			var placement = item.Placement;
			var candidates = PoolAttractions.AccessCandidates( item.Entry, placement.X, placement.Y, placement.Rotation );
			if ( !candidates.Contains( (x, y) ) )
				continue;
			// the object only needs one water cell: a cell is protected when it is the last water cell of the object's water side
			var others = candidates.Count( candidate => candidate != (x, y) && map != null && map.InBounds( candidate.X, candidate.Y ) && map.RawTypeAt( candidate.X, candidate.Y ) == ParkCellTypes.PoolWater );
			if ( others == 0 )
				return true;
		}
		return false;
	}
}
