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

/// <summary>A placed tower as the slide rules see it: its anchor (the slide's key), its footprint, its pool-side candidates, its profile and its capacity.</summary>
public sealed record PoolSlideTower( int X, int Y, IReadOnlyList<(int X, int Y)> Footprint, IReadOnlyList<(int X, int Y)> PoolSide, PoolSlideProfile Profile, int Capacity )
{
	/// <summary>The id of the slide this tower carries (<see cref="PoolSlide.KeyOf"/>).</summary>
	public int Id => PoolSlide.KeyOf( X, Y );

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
