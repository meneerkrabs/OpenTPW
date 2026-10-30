namespace OpenTPW;

public partial class Level
{
	/// <summary>[EXT:pools-slides] The park's water slides: towers placed beside a pool and the tracks built from them (docs/POOLS.md, "Water slides"); null on a read-only visit and before the paths are set up.</summary>
	public PoolSlideWorld? PoolSlides { get; private set; }

	/// <summary>[EXT:pools-render] Draws the park's slides; created with the pool renderer.</summary>
	public PoolSlideRenderer? PoolSlideRenderer { get; private set; }

	private Dictionary<int, PoolSlideTower>? slideTowerStore;
	private Dictionary<int, PoolSlideTower> slideTowers => slideTowerStore ??= new();
	private int slideWaterStamp = -1;
	private Dictionary<(int X, int Y), float>? slideWaterLevelStore;
	private Dictionary<(int X, int Y), float> slideWaterLevels { get => slideWaterLevelStore ??= new(); set => slideWaterLevelStore = value; }

	/// <summary>
	/// [EXT:pools-slides] Creates the slide world over the level's cell map and follows the placed slide towers: a tower placed registers its slide, a tower removed takes its slide with it and ends the rides
	/// on it. Called once the paths are set up.
	/// </summary>
	private void SetupPoolSlides()
	{
		if ( Guests == null || IsReadOnlyVisit )
			return;
		PoolSlides = new PoolSlideWorld( SlideSite, id => slideTowers.GetValueOrDefault( id ), () => Park?.Economy );
		Guests.PoolSlides = PoolSlides;
		if ( PoolTool != null )
			PoolTool.Slides = PoolSlides;
		foreach ( var item in Objects.Objects )
			RegisterSlideTower( item );
		Objects.ObjectPlaced += RegisterSlideTower;
		Objects.ObjectRemoved += UnregisterSlideTower;
	}

	/// <summary>[EXT:pools-slides] Starts the slide mode of the pool tool for the tower's slide (after ending the other tools); false when the extension is off or the slide is already finished.</summary>
	internal bool EnterSlideTool( OriginalObject tower )
	{
		if ( PoolTool == null || PoolSlides == null || !PoolSlideTowers.IsSlideTower( tower.Entry ) )
			return false;
		EndBuildTools();
		return PoolTool.BeginSlide( tower.Placement.X, tower.Placement.Y );
	}

	/// <summary>[EXT:pools-slides] The first placed slide tower whose slide is not finished (no track yet, or an open end), in placement order, or null.</summary>
	internal OriginalObject? NextUnfinishedSlideTower() => PoolSlides == null ? null : Objects.Objects.FirstOrDefault( item => !item.IsDeleted && PoolSlideTowers.IsSlideTower( item.Entry )
		&& slideTowers.ContainsKey( PoolSlide.KeyOf( item.Placement.X, item.Placement.Y ) ) && PoolSlides.Find( PoolSlide.KeyOf( item.Placement.X, item.Placement.Y ) ) is not { IsComplete: true } );

	private void RegisterSlideTower( OriginalObject item )
	{
		if ( !FeatureGates.Pools || item.IsDeleted || !PoolSlideTowers.IsSlideTower( item.Entry ) )
			return;
		var (x, y, rotation) = (item.Placement.X, item.Placement.Y, item.Placement.Rotation);
		slideTowers[PoolSlide.KeyOf( x, y )] = new PoolSlideTower( x, y, PoolSlideTowers.Footprint( item.Entry.Shape, x, y, rotation ),
			PoolSlideTowers.PoolSideCandidates( item.Entry.Shape, x, y, rotation ), PoolSlideTowers.ProfileOf( item.Entry ), PoolSlideTowers.CapacityOf( item.Entry ), item.ThingId );
		// what the world remembers of its slides' usability depends on the towers
		PoolSlides?.Touch();
	}

	private void UnregisterSlideTower( OriginalObject item )
	{
		if ( !PoolSlideTowers.IsSlideTower( item.Entry ) )
			return;
		var id = PoolSlide.KeyOf( item.Placement.X, item.Placement.Y );
		if ( !slideTowers.Remove( id ) )
			return;
		// the riders and the queue on it end their session at once, then the track goes (and is refunded)
		Guests?.AbortPoolActivitiesOf( item.ThingId );
		PoolSlides?.Remove( item.Placement.X, item.Placement.Y );
		// [EXT:online-coop-undo] its pieces went with it: no step of its slide can be taken back any more, whatever tower is built on its anchor next (C5)
		UndoJournal?.TowerRemoved( item.Placement.X, item.Placement.Y );
		PoolSlides?.Touch();
	}

	/// <summary>The park as the slide rules see it: the cell map, the ground the pool deck follows, the water levels of the pools and the cells other things hold.</summary>
	private PoolSlideSite? slideSiteStore;

	/// <summary>
	/// The park as the slide rules see it. One instance for the level's life, so asking for it (the activity does, per swimmer per turn) allocates nothing: its functions read the live cell map, and the water
	/// levels are rebuilt on the first question after a pool cell was written.
	/// </summary>
	private PoolSlideSite? SlideSite()
	{
		if ( Guests == null )
			return null;
		return slideSiteStore ??= new PoolSlideSite
		{
			Cells = Guests.Grid.Cells,
			Ground = SlideGround,
			WaterLevel = ( x, y ) =>
			{
				var cells = Guests!.Grid.Cells;
				if ( slideWaterStamp != cells.PoolWrites )
				{
					slideWaterStamp = cells.PoolWrites;
					slideWaterLevels = PoolMeshBuilder.WaterLevels( PoolRenderer.PoolCells( cells ), SlideGround, PoolMeshOptions.Default );
				}
				return slideWaterLevels.TryGetValue( (x, y), out var level ) ? level : null;
			},
			IsReserved = ( x, y ) => PoolBridges?.Reserves( x, y ) == true || IsReservedByPrototype( x, y )
		};
	}

	private float SlideGround( float cellX, float cellY )
	{
		var terrain = Objects.Grid;
		return terrain.GetTerrainHeight( Math.Min( cellX * LandscapeSampler.CellUnits, terrain.Width * LandscapeSampler.CellUnits - 0.001f ),
			Math.Min( cellY * LandscapeSampler.CellUnits, terrain.Height * LandscapeSampler.CellUnits - 0.001f ) );
	}

	/// <summary>
	/// [EXT:debug-pool] Smoke-only (<c>--debug-slide</c>): builds the theme's slide tower of the profile through the level's own placement (charged; it needs pool water at its front) at the first anchor and
	/// rotation within four cells of <paramref name="pool"/> that the rules allow and from which a valid slide into the pool exists, then lays that slide piece by piece through the slide world. Null when
	/// the extension is off, the theme lists no such tower or nothing fits.
	/// </summary>
	internal OriginalObject? BuildSlideBeside( IReadOnlyList<(int X, int Y)> pool, PoolSlideProfile profile )
	{
		var entry = Objects.Catalog.Entries.FirstOrDefault( item => PoolSlideTowers.IsSlideTower( item ) && PoolSlideTowers.ProfileOf( item ) == profile );
		if ( entry == null || pool.Count == 0 || PoolSlides == null || SlideSite() is not { } site )
			return null;
		var (minX, maxX, minY, maxY) = (pool.Min( cell => cell.X ) - 4, pool.Max( cell => cell.X ) + 4, pool.Min( cell => cell.Y ) - 4, pool.Max( cell => cell.Y ) + 4);
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
			for ( var y = minY; y <= maxY; y++ )
				for ( var x = minX; x <= maxX; x++ )
				{
					if ( CheckObject( entry, x, y, rotation ) != OriginalPlacementResult.Allowed )
						continue;
					var anchor = GetCentredAnchor( entry, x, y, rotation );
					var tower = new PoolSlideTower( anchor.X, anchor.Y, PoolSlideTowers.Footprint( entry.Shape, anchor.X, anchor.Y, rotation ),
						PoolSlideTowers.PoolSideCandidates( entry.Shape, anchor.X, anchor.Y, rotation ), profile, PoolSlideTowers.CapacityOf( entry ) );
					if ( PoolSlideRoutes.Find( tower, SlideSite()! ) is not { } route || PlaceObject( entry, x, y, rotation ) is not { } placed )
						continue;
					foreach ( var (cellX, cellY) in route )
						if ( PoolSlides.AddCell( placed.Placement.X, placed.Placement.Y, cellX, cellY ) != PoolSlideResult.Ok )
							break;
					return placed;
				}
		return null;
	}

	/// <summary>
	/// [EXT:pools-slides] Why a pool cell cannot be erased because of a slide: it is the splash cell of a complete slide or a pool-side cell of a slide's tower (always: a built slide keeps its water, as a changing
	/// room keeps its access cells), or a guest is on the slide's track or stairs in it. Null when it can go.
	/// </summary>
	private PoolBuildResult? SlideEraseBlock( int x, int y )
	{
		if ( PoolSlides == null || Guests == null )
			return null;
		if ( PoolSlides.NeedsWater( x, y ) )
			return PoolBuildResult.SlideWater;
		foreach ( var slide in PoolSlides.Slides )
		{
			if ( PoolSlides.TowerOf( slide ) is not { } tower )
				continue;
			if ( slide.LandingCell == (x, y) && PoolSlideActivity.HasRiders( Guests, tower.ThingId ) )
				return PoolBuildResult.SlideRiders;
			if ( tower.PoolSideCell( Guests.Grid.Cells ) == (x, y) && PoolSlideActivity.HasClimbers( Guests, tower.ThingId ) )
				return PoolBuildResult.SlideRiders;
		}
		return null;
	}
}
