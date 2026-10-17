namespace OpenTPW;

public partial class Level
{
	/// <summary>[EXT:pools-slides] The park's water slides: towers placed beside a pool and the tracks built from them (docs/POOLS.md, "Water slides"); null on a read-only visit and before the paths are set up.</summary>
	public PoolSlideWorld? PoolSlides { get; private set; }

	private readonly Dictionary<int, PoolSlideTower> slideTowers = new();
	private int slideWaterStamp = -1;
	private Dictionary<(int X, int Y), float> slideWaterLevels = new();

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
		foreach ( var item in Objects.Objects )
			RegisterSlideTower( item );
		Objects.ObjectPlaced += RegisterSlideTower;
		Objects.ObjectRemoved += UnregisterSlideTower;
	}

	private void RegisterSlideTower( OriginalObject item )
	{
		if ( !FeatureGates.Pools || item.IsDeleted || !PoolSlideTowers.IsSlideTower( item.Entry ) )
			return;
		var (x, y, rotation) = (item.Placement.X, item.Placement.Y, item.Placement.Rotation);
		slideTowers[PoolSlide.KeyOf( x, y )] = new PoolSlideTower( x, y, PoolSlideTowers.Footprint( item.Entry.Shape, x, y, rotation ),
			PoolSlideTowers.PoolSideCandidates( item.Entry.Shape, x, y, rotation ), PoolSlideTowers.ProfileOf( item.Entry ), PoolSlideTowers.CapacityOf( item.Entry ) );
	}

	private void UnregisterSlideTower( OriginalObject item )
	{
		if ( !PoolSlideTowers.IsSlideTower( item.Entry ) )
			return;
		var id = PoolSlide.KeyOf( item.Placement.X, item.Placement.Y );
		if ( !slideTowers.Remove( id ) )
			return;
		// the riders and the queue on it end their session at once, then the track goes (and is refunded)
		Guests?.AbortPoolActivitiesOf( id );
		PoolSlides?.Remove( item.Placement.X, item.Placement.Y );
	}

	/// <summary>The park as the slide rules see it: the cell map, the ground the pool deck follows, the water levels of the pools and the cells other things hold.</summary>
	private PoolSlideSite? SlideSite()
	{
		if ( Guests == null )
			return null;
		var cells = Guests.Grid.Cells;
		var terrain = Objects.Grid;
		float Ground( float cellX, float cellY ) => terrain.GetTerrainHeight( Math.Min( cellX * LandscapeSampler.CellUnits, terrain.Width * LandscapeSampler.CellUnits - 0.001f ),
			Math.Min( cellY * LandscapeSampler.CellUnits, terrain.Height * LandscapeSampler.CellUnits - 0.001f ) );
		if ( slideWaterStamp != cells.PoolWrites )
		{
			slideWaterStamp = cells.PoolWrites;
			slideWaterLevels = PoolMeshBuilder.WaterLevels( PoolRenderer.PoolCells( cells ), Ground, PoolMeshOptions.Default );
		}
		return new PoolSlideSite
		{
			Cells = cells,
			Ground = Ground,
			WaterLevel = ( x, y ) => slideWaterLevels.TryGetValue( (x, y), out var level ) ? level : null,
			IsReserved = ( x, y ) => IsReservedByPrototype( x, y )
		};
	}

	/// <summary>[EXT:pools-slides] Why a pool cell cannot be erased because of a slide: it is the splash cell of a slide with riders on it, or the foot of a tower's stairs with guests on them. Null when it can go.</summary>
	private PoolBuildResult? SlideEraseBlock( int x, int y )
	{
		if ( PoolSlides == null || Guests == null )
			return null;
		foreach ( var slide in PoolSlides.Slides )
		{
			if ( slide.LandingCell == (x, y) && PoolSlideActivity.HasRiders( Guests, slide.Id ) )
				return PoolBuildResult.SlideRiders;
			if ( PoolSlides.TowerOf( slide ) is { } tower && tower.PoolSideCell( Guests.Grid.Cells ) == (x, y) && PoolSlideActivity.HasClimbers( Guests, slide.Id ) )
				return PoolBuildResult.SlideRiders;
		}
		return null;
	}
}
