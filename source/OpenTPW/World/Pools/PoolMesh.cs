using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// One vertex of a generated pool mesh, in engine space (X and Y on the ground, Z up, the axes <see cref="OriginalParkPlacement.ToEngine"/> gives).
/// [EXT:pools-render] OpenTPW's own pool rendering; the original game has no pools (docs/POOLS.md).
/// </summary>
public readonly record struct PoolVertex( SVector3 Position, SVector3 Normal, SVector2 Uv );

/// <summary>An indexed triangle list of one pool surface: the water, the coping on top of the rim, or the walls.</summary>
public sealed class PoolMeshPart
{
	public List<PoolVertex> Vertices { get; } = new();
	/// <summary>Triangle corners as indices into <see cref="Vertices"/>, three per triangle.</summary>
	public List<int> Indices { get; } = new();
	public int TriangleCount => Indices.Count / 3;

	/// <summary>Adds a quad whose corners run around it, flipping the order when the winding would face against <paramref name="normal"/>.</summary>
	internal void AddQuad( SVector3 a, SVector3 b, SVector3 c, SVector3 d, SVector3 normal, SVector2 uvA, SVector2 uvB, SVector2 uvC, SVector2 uvD )
	{
		var facing = SVector3.Dot( SVector3.Cross( b - a, c - b ), normal );
		var @base = Vertices.Count;
		if ( facing >= 0 )
		{
			Vertices.Add( new PoolVertex( a, normal, uvA ) );
			Vertices.Add( new PoolVertex( b, normal, uvB ) );
			Vertices.Add( new PoolVertex( c, normal, uvC ) );
			Vertices.Add( new PoolVertex( d, normal, uvD ) );
		}
		else
		{
			Vertices.Add( new PoolVertex( d, normal, uvD ) );
			Vertices.Add( new PoolVertex( c, normal, uvC ) );
			Vertices.Add( new PoolVertex( b, normal, uvB ) );
			Vertices.Add( new PoolVertex( a, normal, uvA ) );
		}
		Indices.AddRange( new[] { @base, @base + 1, @base + 2, @base, @base + 2, @base + 3 } );
	}
}

/// <summary>
/// The surfaces of a pool complex, each drawn with its own material (<see cref="PoolRenderer"/>): the water; the coping (the deck round the water); the upper walls (from the coping down to the
/// water line, and the skirt round the outside of the deck, in the coping's material); the lower walls (from the water line down to the floor) and the floor, drawn in the wall tiles' material
/// and shaded by the depth their first texture coordinate carries (0 at the water line, 1 at the floor).
/// </summary>
public sealed record PoolMesh( PoolMeshPart Water, PoolMeshPart Coping, PoolMeshPart UpperWalls, PoolMeshPart LowerWalls, PoolMeshPart Floor )
{
	/// <summary>Every part, in drawing order (opaque parts first, the water last).</summary>
	public IEnumerable<PoolMeshPart> Parts => new[] { LowerWalls, Floor, UpperWalls, Coping, Water };
	public bool IsEmpty => Parts.All( part => part.Vertices.Count == 0 );
	public int TriangleCount => Parts.Sum( part => part.TriangleCount );
	public static PoolMesh Empty => new( new PoolMeshPart(), new PoolMeshPart(), new PoolMeshPart(), new PoolMeshPart(), new PoolMeshPart() );
}

/// <summary>
/// The measures of a pool (docs/POOLS.md). Heights are engine units (a cell is 2 wide), the rim's width a fraction of one cell. The pool is dug into the ground in the drawing only: the terrain
/// renderer leaves the ground out under the pool cells (<see cref="PoolRenderHoles"/>) while the heightfield and the simulation keep their heights.
/// </summary>
/// <param name="RimWidth">Width of the coping band along a pool edge, as a fraction of a cell (0 to 0.5, exclusive): a quarter of a cell.</param>
/// <param name="WaterDrop">How far the water surface lies below the lowest ground of the pool's connected cells.</param>
/// <param name="Depth">How far the floor lies below the water surface.</param>
/// <param name="CopingLift">How far the coping stands above the ground at its own point (so the deck does not sink into the grass at its edge).</param>
/// <param name="SkirtDepth">How far the skirt round the outside of the deck reaches below the ground.</param>
// [EXT:pools-render] OpenTPW's own measures; the original game has no pools
public sealed record PoolMeshOptions( float RimWidth = 0.25f, float WaterDrop = 0.12f, float Depth = 1.75f, float CopingLift = 0.03f, float SkirtDepth = 0.2f )
{
	public static PoolMeshOptions Default { get; } = new();
}
