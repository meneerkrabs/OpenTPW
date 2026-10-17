namespace OpenTPW;

/// <summary>
/// A search for a valid route of a slide from a tower to its pool (docs/POOLS.md, "Water slides"): the shortest track the slide rules accept, found by iterative deepening over the cells beside the track's
/// end. It is a developer and test aid (the capture option <c>--debug-slide</c> and the tests use it); the player lays the track cell by cell. Deterministic: neighbours are tried straight on, then left, then right.
/// </summary>
// [EXT:debug-pool] smoke-only helper of the capture options; the original game has no pools
public static class PoolSlideRoutes
{
	/// <summary>The cells of the shortest valid slide from <paramref name="tower"/> with at most <paramref name="maxPieces"/> pieces, or null when there is none.</summary>
	public static IReadOnlyList<(int X, int Y)>? Find( PoolSlideTower tower, PoolSlideSite site, int maxPieces = 12 )
	{
		ArgumentNullException.ThrowIfNull( tower );
		ArgumentNullException.ThrowIfNull( site );
		if ( !FeatureGates.Pools || tower.PoolSideCell( site.Cells ) == null )
			return null;
		for ( var depth = 2; depth <= Math.Min( maxPieces, PoolSlide.MaximumPieces ); depth++ )
		{
			foreach ( var (x, y, heading) in tower.StartOptions() )
			{
				var path = new List<(int X, int Y)> { (x, y) };
				if ( Search( tower, site, path, heading, depth ) )
					return path;
			}
		}
		return null;
	}

	private static bool Search( PoolSlideTower tower, PoolSlideSite site, List<(int X, int Y)> path, int heading, int depth )
	{
		var cell = path[^1];
		if ( !site.Cells.InBounds( cell.X, cell.Y ) || site.Cells.FlagsAt( cell.X, cell.Y ).HasFlag( ParkCellFlags.Unowned ) )
			return false;
		var type = site.Cells.RawTypeAt( cell.X, cell.Y );
		if ( type == ParkCellTypes.PoolWater )
			return path.Count == depth && Valid( tower, site, path );
		if ( type != 0 || site.IsReserved( cell.X, cell.Y ) || path.Count == depth )
			return false;
		foreach ( var turn in new[] { 0, 3, 1 } )
		{
			var next = (heading + turn) & 3;
			var (dx, dy) = PoolSlide.Direction( next );
			var target = (X: cell.X + dx, Y: cell.Y + dy);
			if ( path.Contains( target ) || tower.Footprint.Contains( target ) )
				continue;
			path.Add( target );
			if ( Search( tower, site, path, next, depth ) )
				return true;
			path.RemoveAt( path.Count - 1 );
		}
		return false;
	}

	/// <summary>Whether the cells make a valid complete slide: the pieces follow from the headings, the last is the splash.</summary>
	private static bool Valid( PoolSlideTower tower, PoolSlideSite site, IReadOnlyList<(int X, int Y)> path )
	{
		var start = tower.StartOptions().First( option => option.X == path[0].X && option.Y == path[0].Y );
		var pieces = new List<PoolSlidePiece>();
		var heading = start.Heading;
		for ( var index = 0; index < path.Count - 1; index++ )
		{
			var step = (path[index + 1].X - path[index].X, path[index + 1].Y - path[index].Y);
			var next = Enumerable.Range( 0, 4 ).First( direction => PoolSlide.Direction( direction ) == step );
			pieces.Add( ((next - heading) & 3) switch { 0 => PoolSlidePiece.Straight, 1 => PoolSlidePiece.Right, _ => PoolSlidePiece.Left } );
			heading = next;
		}
		pieces.Add( PoolSlidePiece.Splash );
		var end = PoolSlideRules.EndHeightOf( path[^1].X, path[^1].Y, site );
		if ( end == null )
			return false;
		var slide = new PoolSlide( tower.X, tower.Y, tower.Profile, start.X, start.Y, start.Heading, pieces, PoolSlideRules.TopHeightOf( tower, site ), end.Value );
		return PoolSlideRules.Validate( slide, tower, site ) == PoolSlideResult.Ok;
	}
}
