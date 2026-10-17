namespace OpenTPW;

/// <summary>
/// The tower of a water slide (docs/POOLS.md, "Water slides"): an object whose .sam says <c>Pools.SlideTower 1</c>, shipped with the pools extension (<see cref="PoolContent"/>). It stands on land
/// and its stairs face the pool: the cells beyond its front face, which is the local -V side of the shape at rotation 0, are its pool-side cells, and a tower may only be placed with at least one
/// of them holding pool water. Swimmers leave the water there to climb the stairs, and the slide's track leaves the tower from any of its other sides.
/// </summary>
// [EXT:pools-slides] OpenTPW's own object and placement rule; the original game has no pools
public static class PoolSlideTowers
{
	/// <summary>The .sam key that marks an object as a slide tower.</summary>
	public const string SettingKey = "Pools.SlideTower";
	/// <summary>The .sam key for the cross-section of the slide: 0 an open flume, 1 a closed tube.</summary>
	public const string ProfileKey = "Pools.SlideProfile";
	/// <summary>The .sam key for how many riders wait in the tower (default <see cref="DefaultCapacity"/>).</summary>
	public const string CapacityKey = "Pools.SlideCapacity";
	/// <summary>Riders waiting in a tower unless the .sam says otherwise.</summary>
	public const int DefaultCapacity = 4;
	/// <summary>The height of the tower's top above the highest ground of its footprint, engine units: the track starts there.</summary>
	public const float TopHeight = 5f;

	/// <summary>Whether the catalog entry is a slide tower.</summary>
	public static bool IsSlideTower( ObjectCatalogEntry entry ) => entry.Settings.GetBool( SettingKey );

	/// <summary>The profile the tower's slide is built with.</summary>
	public static PoolSlideProfile ProfileOf( ObjectCatalogEntry entry ) =>
		entry.Settings.GetInt( ProfileKey, 0 ) == 1 ? PoolSlideProfile.Tube : PoolSlideProfile.Flume;

	/// <summary>How many riders wait in the tower at once.</summary>
	public static int CapacityOf( ObjectCatalogEntry entry ) => Math.Clamp( entry.Settings.GetInt( CapacityKey, DefaultCapacity ), 1, 16 );

	/// <summary>
	/// The pool-side candidates of a tower anchored at (<paramref name="anchorX"/>, <paramref name="anchorY"/>) with the rotation: for every footprint cell whose neighbour beyond the front face is not
	/// part of the footprint, that neighbour, in footprint order.
	/// </summary>
	public static IReadOnlyList<(int X, int Y)> PoolSideCandidates( ObjectShape shape, int anchorX, int anchorY, int rotation )
	{
		ArgumentNullException.ThrowIfNull( shape );
		var (fx, fy) = ObjectFootprint.RotateDirection( rotation, 0, -1 );
		var footprint = Footprint( shape, anchorX, anchorY, rotation );
		var candidates = new List<(int X, int Y)>();
		foreach ( var cell in footprint )
		{
			var next = (X: cell.X + fx, Y: cell.Y + fy);
			if ( !footprint.Contains( next ) && !candidates.Contains( next ) )
				candidates.Add( next );
		}
		return candidates;
	}

	/// <summary>The cells of the tower's footprint.</summary>
	public static IReadOnlyList<(int X, int Y)> Footprint( ObjectShape shape, int anchorX, int anchorY, int rotation ) =>
		ObjectFootprint.GetCells( shape, anchorX, anchorY, rotation ).Select( cell => (cell.X, cell.Y) ).ToList();

	/// <summary>Whether a tower placed here has pool water beside the footprint on a side that is not its front (the player has turned it the wrong way, for the refusal's wording).</summary>
	public static bool WaterBesideOtherSide( ObjectShape shape, ParkCellMap cells, int anchorX, int anchorY, int rotation )
	{
		var footprint = Footprint( shape, anchorX, anchorY, rotation );
		var front = PoolSideCandidates( shape, anchorX, anchorY, rotation );
		foreach ( var (x, y) in footprint )
			for ( var heading = 0; heading < 4; heading++ )
			{
				var (dx, dy) = PoolSlide.Direction( heading );
				var next = (X: x + dx, Y: y + dy);
				if ( !footprint.Contains( next ) && !front.Contains( next ) && cells.InBounds( next.X, next.Y ) && cells.RawTypeAt( next.X, next.Y ) == ParkCellTypes.PoolWater )
					return true;
			}
		return false;
	}

	/// <summary>The placement rule: a slide tower needs pool water beside its front. Null (no opinion) for any other object and while the extension is off.</summary>
	public static OriginalPlacementResult? PlacementRule( ObjectCatalogEntry entry, ParkCellMap? cells, int anchorX, int anchorY, int rotation )
	{
		if ( !FeatureGates.Pools || cells == null || !IsSlideTower( entry ) )
			return null;
		foreach ( var (x, y) in PoolSideCandidates( entry.Shape, anchorX, anchorY, rotation ) )
			if ( cells.InBounds( x, y ) && cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater )
				return null;
		return OriginalPlacementResult.NeedsPool;
	}
}

/// <summary>
/// A placed tower as the slide rules see it: its anchor (the slide's key), its footprint, its pool-side candidates, its profile, its capacity and its thing id (the <see cref="Guest.PoolActivityTarget"/> of the guests on its slide,
/// the id of the object that provides the activity).
/// </summary>
public sealed record PoolSlideTower( int X, int Y, IReadOnlyList<(int X, int Y)> Footprint, IReadOnlyList<(int X, int Y)> PoolSide, PoolSlideProfile Profile, int Capacity, int ThingId = 0 )
{
	/// <summary>The id of the slide this tower carries (<see cref="PoolSlide.KeyOf"/>).</summary>
	public int Id => PoolSlide.KeyOf( X, Y );

	/// <summary>The rotation the tower was placed with (0, 90, 180 or 270), read back from where its pool-side cells lie: the front is the local -V side.</summary>
	public int Rotation
	{
		get
		{
			if ( PoolSide.Count == 0 )
				return 0;
			foreach ( var rotation in new[] { 0, 90, 180, 270 } )
			{
				var (fx, fy) = ObjectFootprint.RotateDirection( rotation, 0, -1 );
				foreach ( var cell in Footprint )
					if ( PoolSide.Contains( (cell.X + fx, cell.Y + fy) ) )
						return rotation;
			}
			return 0;
		}
	}

	/// <summary>
	/// A point of the tower's model (<see cref="PoolSlideTowerModel"/>, tenths of a model unit, X right and Y towards the stairs' side at rotation 0) as a place in the park (16.16 cells): the model's 2 x 2 cells
	/// lie on the footprint, its +X along the local +U and its +Y along the front (-V), turned with the tower.
	/// </summary>
	public (int X, int Y) ModelPoint( int tenthsX, int tenthsY )
	{
		long sumX = 0, sumY = 0;
		foreach ( var (x, y) in Footprint )
		{
			var centre = PoolSwimRegion.Centre( (x, y) );
			sumX += centre.X;
			sumY += centre.Y;
		}
		if ( Footprint.Count == 0 )
			return (0, 0);
		var rotation = Rotation;
		var (sx, sy) = ObjectFootprint.RotateDirection( rotation, 1, 0 );
		var (fx, fy) = ObjectFootprint.RotateDirection( rotation, 0, -1 );
		// a model unit is a tenth of a cell: tenths of units are hundredths of a cell
		var across = (long)(tenthsX - PoolSlideTowerModel.Size * 5) * Fixed16.One / 100;
		var along = (long)(tenthsY - PoolSlideTowerModel.Size * 5) * Fixed16.One / 100;
		return ((int)(sumX / Footprint.Count + across * sx + along * fx), (int)(sumY / Footprint.Count + across * sy + along * fy));
	}

	/// <summary>The first pool-side candidate that holds pool water now, or null.</summary>
	public (int X, int Y)? PoolSideCell( ParkCellMap cells )
	{
		foreach ( var cell in PoolSide )
			if ( cells.InBounds( cell.X, cell.Y ) && cells.RawTypeAt( cell.X, cell.Y ) == ParkCellTypes.PoolWater )
				return cell;
		return null;
	}

	/// <summary>The cells next to the footprint where the track may start, with the heading it leaves the tower in: every outside neighbour except the pool-side ones, in footprint order.</summary>
	public IEnumerable<(int X, int Y, int Heading)> StartOptions()
	{
		var seen = new HashSet<(int X, int Y)>();
		foreach ( var cell in Footprint )
			for ( var heading = 0; heading < 4; heading++ )
			{
				var (dx, dy) = PoolSlide.Direction( heading );
				var next = (X: cell.X + dx, Y: cell.Y + dy);
				if ( !Footprint.Contains( next ) && !PoolSide.Contains( next ) && seen.Add( next ) )
					yield return (next.X, next.Y, heading);
			}
	}
}

/// <summary>
/// The geometry of the tower model that the riders' places follow (tools/blender/opentpw_slide_tower.py, which is the source of the numbers): the model is 20 x 20 units for the 2 x 2 cells, its stairs a
/// flight of twelve steps along the left edge (X 0 to 7) from the ground at the front edge (Y = 20) up to the platform at Y = 8, the platform an L of slabs whose top is the track's height, the rails and
/// the corner posts round it. All values are tenths of a model unit (<see cref="PoolSlideTower.ModelPoint"/>).
/// </summary>
// [EXT:pools-slides] OpenTPW's own model; the original game has no pools
public static class PoolSlideTowerModel
{
	/// <summary>The model's side, model units (two cells of ten).</summary>
	public const int Size = 20;
	/// <summary>The middle of the flight, across.</summary>
	public const int StairsX = 35;
	/// <summary>The foot of the flight: the ground at the front edge.</summary>
	public const int StairsFootY = 200;
	/// <summary>The top of the flight, where its last tread meets the platform.</summary>
	public const int StairsTopY = 85;
	/// <summary>Where a climber steps onto the deck beside the top of the flight (the deck is the slab behind the head of the stairs).</summary>
	public const int DeckEntry = 60;
	/// <summary>How far the stairs' foot is above the ground the tower stands on: the platform's height above the highest ground, engine units.</summary>
	public const float StairsRise = PoolSlideTowers.TopHeight;

	/// <summary>
	/// The places on the deck where the riders wait, in order of the line (tenths of a model unit): clear of the flight's opening (X under 7, Y over 8), the corner posts and the rails.
	/// Four of them; a longer line stacks up beside them.
	/// </summary>
	public static readonly (int X, int Y)[] Spots = { (125, 130), (125, 45), (45, 45), (160, 90) };

	/// <summary>The place on the deck of the guest at this rank in the line, nudged towards the middle of the deck for every four ranks ahead of it so a long line does not share a spot.</summary>
	public static (int X, int Y) Spot( int rank )
	{
		var (x, y) = Spots[rank & 3];
		var ring = rank / 4;
		return (x + Math.Sign( 100 - x ) * ring * 12, y + Math.Sign( 100 - y ) * ring * 12);
	}
}
