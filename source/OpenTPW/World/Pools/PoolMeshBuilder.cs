using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Generates the mesh of a pool complex from its cells (CPU only, no GPU objects): a water surface, the coping on the rim and the walls between them
/// (<see cref="PoolEdges"/> for the tiling). Every cell is cut into 3 x 3 tiles at <see cref="PoolMeshOptions.RimWidth"/>; neighbouring cells share their tile
/// edges, so any shape joins watertight. Heights follow the ground (the caller's ground function: in the game <see cref="LandscapeSampler"/>, the height the terrain is drawn at). The terrain
/// cannot be dug, so the pool is a raised basin: at each cell corner the water lies <see cref="PoolMeshOptions.WaterLift"/> above the ground and the coping <see cref="PoolMeshOptions.CopingRise"/>
/// above the water; the levels are interpolated across a cell, and the ground is continuous across cells, so the surfaces are too. A skirt closes the outer side of the rim down to the ground.
/// (docs/POOLS.md)
/// </summary>
// [EXT:pools-render] OpenTPW's own pool geometry; the original game has no pools
public static class PoolMeshBuilder
{
	/// <summary>
	/// Builds the mesh of <paramref name="cells"/>.
	/// </summary>
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
		if ( !(cellSize > 0) )
			throw new ArgumentOutOfRangeException( nameof( cellSize ) );
		var ordered = cells.Distinct().OrderBy( cell => cell.Y ).ThenBy( cell => cell.X ).ToArray();
		var mesh = PoolMesh.Empty;
		if ( ordered.Length == 0 )
			return mesh;
		var breaks = new[] { 0f, options.RimWidth, 1f - options.RimWidth, 1f };
		float Level( int cornerX, int cornerY ) => ground( cornerX, cornerY ) + options.WaterLift;

		foreach ( var (x, y) in ordered )
		{
			var mask = PoolEdges.MaskAt( isPool, x, y );
			var (w00, w10, w01, w11) = (Level( x, y ), Level( x + 1, y ), Level( x, y + 1 ), Level( x + 1, y + 1 ));
			float Ground( float u, float v ) => ground( x + u, y + v );
			float Water( float u, float v ) => w00 * (1 - u) * (1 - v) + w10 * u * (1 - v) + w01 * (1 - u) * v + w11 * u * v;
			SVector3 Point( float u, float v, float height ) => new( origin.X + (x + u) * cellSize, origin.Y + (y + v) * cellSize, height );
			SVector3 Top( float u, float v ) => Point( u, v, Water( u, v ) + options.CopingRise );
			SVector3 Surface( float u, float v ) => Point( u, v, Water( u, v ) );
			SVector3 Floor( float u, float v ) => Point( u, v, Ground( u, v ) );
			var up = new SVector3( 0, 0, 1 );

			for ( var row = 0; row < 3; row++ )
			{
				for ( var column = 0; column < 3; column++ )
				{
					float u0 = breaks[column], u1 = breaks[column + 1], v0 = breaks[row], v1 = breaks[row + 1];
					var coping = PoolEdges.IsCoping( mask, column, row );
					if ( coping )
						mesh.Coping.AddQuad( Top( u0, v0 ), Top( u1, v0 ), Top( u1, v1 ), Top( u0, v1 ), up,
							new SVector2( u0, v0 ), new SVector2( u1, v0 ), new SVector2( u1, v1 ), new SVector2( u0, v1 ) );
					else
						mesh.Water.AddQuad( Surface( u0, v0 ), Surface( u1, v0 ), Surface( u1, v1 ), Surface( u0, v1 ), up,
							new SVector2( u0, v0 ), new SVector2( u1, v0 ), new SVector2( u1, v1 ), new SVector2( u0, v1 ) );
				}
			}

			// the walls between a coping tile and a water tile of this cell (tiles of neighbouring cells always agree, so none cross a cell edge)
			for ( var row = 0; row < 3; row++ )
			{
				for ( var column = 0; column < 3; column++ )
				{
					var coping = PoolEdges.IsCoping( mask, column, row );
					if ( column < 2 && coping != PoolEdges.IsCoping( mask, column + 1, row ) )
					{
						// the shared edge is the vertical line u = breaks[column + 1]; the wall faces the water tile
						var u = breaks[column + 1];
						var facing = coping ? new SVector3( 1, 0, 0 ) : new SVector3( -1, 0, 0 );
						AddWall( mesh.Walls, Top( u, breaks[row] ), Top( u, breaks[row + 1] ), Surface( u, breaks[row] ), Surface( u, breaks[row + 1] ), facing,
							breaks[row], breaks[row + 1] );
					}
					if ( row < 2 && coping != PoolEdges.IsCoping( mask, column, row + 1 ) )
					{
						var v = breaks[row + 1];
						var facing = coping ? new SVector3( 0, 1, 0 ) : new SVector3( 0, -1, 0 );
						AddWall( mesh.Walls, Top( breaks[column], v ), Top( breaks[column + 1], v ), Surface( breaks[column], v ), Surface( breaks[column + 1], v ), facing,
							breaks[column], breaks[column + 1] );
					}
				}
			}

			// the skirt: the outer side of a rim strip that lies on the edge of a cell with no pool neighbour, from the coping down to the ground
			for ( var index = 0; index < 3; index++ )
			{
				float a = breaks[index], b = breaks[index + 1];
				if ( (mask & PoolNeighbours.West) == 0 && PoolEdges.IsCoping( mask, 0, index ) )
					AddWall( mesh.Walls, Top( 0, a ), Top( 0, b ), Floor( 0, a ), Floor( 0, b ), new SVector3( -1, 0, 0 ), a, b );
				if ( (mask & PoolNeighbours.East) == 0 && PoolEdges.IsCoping( mask, 2, index ) )
					AddWall( mesh.Walls, Top( 1, a ), Top( 1, b ), Floor( 1, a ), Floor( 1, b ), new SVector3( 1, 0, 0 ), a, b );
				if ( (mask & PoolNeighbours.North) == 0 && PoolEdges.IsCoping( mask, index, 0 ) )
					AddWall( mesh.Walls, Top( a, 0 ), Top( b, 0 ), Floor( a, 0 ), Floor( b, 0 ), new SVector3( 0, -1, 0 ), a, b );
				if ( (mask & PoolNeighbours.South) == 0 && PoolEdges.IsCoping( mask, index, 2 ) )
					AddWall( mesh.Walls, Top( a, 1 ), Top( b, 1 ), Floor( a, 1 ), Floor( b, 1 ), new SVector3( 0, 1, 0 ), a, b );
			}
		}
		return mesh;
	}

	/// <summary>
	/// One vertical quad between two points at the top and the same two at the bottom; <paramref name="facing"/> is its horizontal normal. The texture runs along the quad
	/// from <paramref name="s0"/> to <paramref name="s1"/> (cell fractions) and from the bottom (0) to the top (1).
	/// </summary>
	private static void AddWall( PoolMeshPart part, SVector3 topA, SVector3 topB, SVector3 bottomA, SVector3 bottomB, SVector3 facing, float s0, float s1 ) =>
		part.AddQuad( bottomA, bottomB, topB, topA, facing, new SVector2( s0, 0 ), new SVector2( s1, 0 ), new SVector2( s1, 1 ), new SVector2( s0, 1 ) );
}
