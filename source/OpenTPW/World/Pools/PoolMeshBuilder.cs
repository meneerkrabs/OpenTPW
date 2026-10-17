using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Generates the mesh of a pool complex from its cells (CPU only, no GPU objects): a basin dug into the ground (docs/POOLS.md). Every cell is cut into 4 x 4 tiles at the rim width (the outer
/// rows and columns are the rim, the middle two the water; <see cref="PoolEdges"/> says which tiles of a cell are coping from its 8-neighbour mask), and neighbouring cells share their tile
/// edges, so any shape joins watertight.
/// <list type="bullet">
/// <item>The <b>coping</b> is the deck at the rim: flat on the ground (the caller's ground function, in the game <see cref="LandscapeSampler"/>), lifted a little.</item>
/// <item>The <b>water</b> of each connected group of cells is one level surface, <see cref="PoolMeshOptions.WaterDrop"/> below the lowest ground of the group; its first texture coordinate is
/// how close the point is to the edge of the pool (1 at the wall, 0 from one cell away), for lighter water near the edge.</item>
/// <item>The <b>walls</b> go straight down from the inside of the coping to the <b>floor</b>, which is <see cref="PoolMeshOptions.Depth"/> below the water: an upper part to the water line and a lower
/// part below it whose first texture coordinate is the depth fraction (0 at the water line, 1 at the floor).</item>
/// <item>A short <b>skirt</b> closes the outside of the deck down below the ground.</item>
/// </list>
/// The terrain renderer leaves the ground out under the pool cells, so the basin shows (<see cref="PoolRenderHoles"/>).
/// </summary>
// [EXT:pools-render] OpenTPW's own pool geometry; the original game has no pools
public static class PoolMeshBuilder
{
	/// <summary>Tile columns and rows per cell: two rim strips and two water tiles.</summary>
	public const int Tiles = 4;
	private static readonly int[] TileClass = { 0, 1, 1, 2 };

	/// <summary>Builds the mesh of <paramref name="cells"/>.</summary>
	/// <param name="cells">The pool cells; duplicates are ignored. The order of the output follows row then column, so equal sets give equal meshes.</param>
	/// <param name="isPool">Whether a cell is a pool cell (the neighbour tests).</param>
	/// <param name="ground">The ground's engine height at a point given in cell units (cell (x, y) spans x to x + 1 and y to y + 1); continuous across cell borders.</param>
	/// <param name="origin">Engine position of grid corner (0, 0).</param>
	/// <param name="cellSize">Engine units per cell.</param>
	public static PoolMesh Build( IEnumerable<(int X, int Y)> cells, Func<int, int, bool> isPool, Func<float, float, float> ground, SVector2 origin, float cellSize, PoolMeshOptions? options = null )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( isPool );
		ArgumentNullException.ThrowIfNull( ground );
		options ??= PoolMeshOptions.Default;
		if ( !(options.RimWidth > 0 && options.RimWidth < 0.5f) )
			throw new ArgumentOutOfRangeException( nameof( options ), "The rim is a fraction of a cell between 0 and 0.5." );
		if ( !(options.Depth > 0) )
			throw new ArgumentOutOfRangeException( nameof( options ), "The pool has a depth." );
		if ( !(cellSize > 0) )
			throw new ArgumentOutOfRangeException( nameof( cellSize ) );
		var ordered = cells.Distinct().OrderBy( cell => cell.Y ).ThenBy( cell => cell.X ).ToArray();
		var mesh = PoolMesh.Empty;
		if ( ordered.Length == 0 )
			return mesh;
		var breaks = new[] { 0f, options.RimWidth, 0.5f, 1f - options.RimWidth, 1f };
		var levels = WaterLevels( ordered, ground, options );

		foreach ( var (x, y) in ordered )
		{
			var mask = PoolEdges.MaskAt( isPool, x, y );
			var level = levels[(x, y)];
			var floor = level - options.Depth;
			SVector3 Point( float u, float v, float height ) => new( origin.X + (x + u) * cellSize, origin.Y + (y + v) * cellSize, height );
			SVector3 Top( float u, float v ) => Point( u, v, ground( x + u, y + v ) + options.CopingLift );
			SVector3 Surface( float u, float v ) => Point( u, v, level );
			SVector3 Bottom( float u, float v ) => Point( u, v, floor );
			SVector3 Skirt( float u, float v ) => Point( u, v, ground( x + u, y + v ) - options.SkirtDepth );
			SVector2 World( SVector3 point ) => new( point.X, point.Y );
			var up = new SVector3( 0, 0, 1 );

			for ( var row = 0; row < Tiles; row++ )
			{
				for ( var column = 0; column < Tiles; column++ )
				{
					float u0 = breaks[column], u1 = breaks[column + 1], v0 = breaks[row], v1 = breaks[row + 1];
					if ( PoolEdges.IsCoping( mask, TileClass[column], TileClass[row] ) )
					{
						SVector3 a = Top( u0, v0 ), b = Top( u1, v0 ), c = Top( u1, v1 ), d = Top( u0, v1 );
						mesh.Coping.AddQuad( a, b, c, d, up, World( a ), World( b ), World( c ), World( d ) );
						continue;
					}
					SVector3 wa = Surface( u0, v0 ), wb = Surface( u1, v0 ), wc = Surface( u1, v1 ), wd = Surface( u0, v1 );
					SVector2 Edge( float u, float v ) => new( EdgeProximity( isPool, x + u, y + v ), 0 );
					mesh.Water.AddQuad( wa, wb, wc, wd, up, Edge( u0, v0 ), Edge( u1, v0 ), Edge( u1, v1 ), Edge( u0, v1 ) );
					var deep = new SVector2( 1, 0 );
					mesh.Floor.AddQuad( Bottom( u0, v0 ), Bottom( u1, v0 ), Bottom( u1, v1 ), Bottom( u0, v1 ), up, deep, deep, deep, deep );
				}
			}

			// the inner walls, between a coping tile and a water tile of this cell: an upper part from the coping down to the water line and a lower part from there to the floor
			// (tiles of the same class are alike, so walls lie on class borders only, and neighbouring cells agree on every tile along their shared border)
			for ( var row = 0; row < Tiles; row++ )
			{
				for ( var column = 0; column < Tiles; column++ )
				{
					var coping = PoolEdges.IsCoping( mask, TileClass[column], TileClass[row] );
					if ( column < Tiles - 1 && TileClass[column] != TileClass[column + 1] && coping != PoolEdges.IsCoping( mask, TileClass[column + 1], TileClass[row] ) )
					{
						var u = breaks[column + 1];
						AddInnerWall( mesh, Top( u, breaks[row] ), Top( u, breaks[row + 1] ), Surface( u, breaks[row] ), Surface( u, breaks[row + 1] ),
							Bottom( u, breaks[row] ), Bottom( u, breaks[row + 1] ), new SVector3( coping ? 1 : -1, 0, 0 ) );
					}
					if ( row < Tiles - 1 && TileClass[row] != TileClass[row + 1] && coping != PoolEdges.IsCoping( mask, TileClass[column], TileClass[row + 1] ) )
					{
						var v = breaks[row + 1];
						AddInnerWall( mesh, Top( breaks[column], v ), Top( breaks[column + 1], v ), Surface( breaks[column], v ), Surface( breaks[column + 1], v ),
							Bottom( breaks[column], v ), Bottom( breaks[column + 1], v ), new SVector3( 0, coping ? 1 : -1, 0 ) );
					}
				}
			}

			// the skirt: the outer side of a rim strip that lies on the edge of a cell with no pool neighbour, from the coping down below the ground
			for ( var index = 0; index < Tiles; index++ )
			{
				float a = breaks[index], b = breaks[index + 1];
				if ( (mask & PoolNeighbours.West) == 0 && PoolEdges.IsCoping( mask, 0, TileClass[index] ) )
					AddSkirt( mesh.UpperWalls, Top( 0, a ), Top( 0, b ), Skirt( 0, a ), Skirt( 0, b ), new SVector3( -1, 0, 0 ) );
				if ( (mask & PoolNeighbours.East) == 0 && PoolEdges.IsCoping( mask, 2, TileClass[index] ) )
					AddSkirt( mesh.UpperWalls, Top( 1, a ), Top( 1, b ), Skirt( 1, a ), Skirt( 1, b ), new SVector3( 1, 0, 0 ) );
				if ( (mask & PoolNeighbours.North) == 0 && PoolEdges.IsCoping( mask, TileClass[index], 0 ) )
					AddSkirt( mesh.UpperWalls, Top( a, 0 ), Top( b, 0 ), Skirt( a, 0 ), Skirt( b, 0 ), new SVector3( 0, -1, 0 ) );
				if ( (mask & PoolNeighbours.South) == 0 && PoolEdges.IsCoping( mask, TileClass[index], 2 ) )
					AddSkirt( mesh.UpperWalls, Top( a, 1 ), Top( b, 1 ), Skirt( a, 1 ), Skirt( b, 1 ), new SVector3( 0, 1, 0 ) );
			}
		}
		return mesh;
	}

	/// <summary>
	/// How close the point (<paramref name="cellX"/>, <paramref name="cellY"/>), in cell units, is to the edge of the pool: 1 on the edge, falling to 0 a cell from it. The edge is the border
	/// of the nearest cell that is not a pool cell, so the value is continuous across the pool and the same for every cell that shares the point.
	/// </summary>
	public static float EdgeProximity( Func<int, int, bool> isPool, float cellX, float cellY )
	{
		int cx = (int)MathF.Floor( cellX ), cy = (int)MathF.Floor( cellY );
		var nearest = 1f;
		for ( var dy = -1; dy <= 1; dy++ )
		{
			for ( var dx = -1; dx <= 1; dx++ )
			{
				if ( isPool( cx + dx, cy + dy ) )
					continue;
				// distance from the point to that cell's square
				float ox = cx + dx, oy = cy + dy;
				var gapX = MathF.Max( MathF.Max( ox - cellX, cellX - (ox + 1) ), 0 );
				var gapY = MathF.Max( MathF.Max( oy - cellY, cellY - (oy + 1) ), 0 );
				nearest = MathF.Min( nearest, MathF.Sqrt( gapX * gapX + gapY * gapY ) );
			}
		}
		return 1f - nearest;
	}

	/// <summary>
	/// The water level of every cell: one for each group of 4-connected pool cells, <see cref="PoolMeshOptions.WaterDrop"/> below the lowest ground at any corner of its cells, so a pool
	/// on a slope is a level basin whose deck follows the ground.
	/// </summary>
	public static Dictionary<(int X, int Y), float> WaterLevels( IReadOnlyList<(int X, int Y)> cells, Func<float, float, float> ground, PoolMeshOptions options )
	{
		var set = cells.ToHashSet();
		var levels = new Dictionary<(int X, int Y), float>();
		foreach ( var start in cells )
		{
			if ( levels.ContainsKey( start ) )
				continue;
			var group = new List<(int X, int Y)>();
			var pending = new Stack<(int X, int Y)>();
			var seen = new HashSet<(int X, int Y)> { start };
			pending.Push( start );
			var lowest = float.PositiveInfinity;
			while ( pending.Count > 0 )
			{
				var cell = pending.Pop();
				group.Add( cell );
				foreach ( var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) } )
					lowest = MathF.Min( lowest, ground( cell.X + dx, cell.Y + dy ) );
				foreach ( var step in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) } )
				{
					var next = (X: cell.X + step.Item1, Y: cell.Y + step.Item2);
					if ( set.Contains( next ) && seen.Add( next ) )
						pending.Push( next );
				}
			}
			foreach ( var cell in group )
				levels[cell] = lowest - options.WaterDrop;
		}
		return levels;
	}

	private static void AddInnerWall( PoolMesh mesh, SVector3 topA, SVector3 topB, SVector3 waterA, SVector3 waterB, SVector3 bottomA, SVector3 bottomB, SVector3 facing )
	{
		// above the water line: the coping's material, with the texture in world units along and up
		float AlongOf( SVector3 point ) => facing.X != 0 ? point.Y : point.X;
		mesh.UpperWalls.AddQuad( waterA, waterB, topB, topA, facing, new SVector2( AlongOf( waterA ), waterA.Z ), new SVector2( AlongOf( waterB ), waterB.Z ),
			new SVector2( AlongOf( topB ), topB.Z ), new SVector2( AlongOf( topA ), topA.Z ) );
		// below it: the first coordinate is the depth fraction, 0 at the water line and 1 at the floor
		mesh.LowerWalls.AddQuad( bottomA, bottomB, waterB, waterA, facing, new SVector2( 1, 0 ), new SVector2( 1, 0 ), new SVector2( 0, 0 ), new SVector2( 0, 0 ) );
	}

	private static void AddSkirt( PoolMeshPart part, SVector3 topA, SVector3 topB, SVector3 bottomA, SVector3 bottomB, SVector3 facing )
	{
		float AlongOf( SVector3 point ) => facing.X != 0 ? point.Y : point.X;
		part.AddQuad( bottomA, bottomB, topB, topA, facing, new SVector2( AlongOf( bottomA ), bottomA.Z ), new SVector2( AlongOf( bottomB ), bottomB.Z ),
			new SVector2( AlongOf( topB ), topB.Z ), new SVector2( AlongOf( topA ), topA.Z ) );
	}
}
