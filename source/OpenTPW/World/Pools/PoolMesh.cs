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

/// <summary>The three surfaces of a pool complex, each drawn with its own material (<see cref="PoolRenderer"/>).</summary>
public sealed record PoolMesh( PoolMeshPart Water, PoolMeshPart Coping, PoolMeshPart Walls )
{
	public bool IsEmpty => Water.Vertices.Count == 0 && Coping.Vertices.Count == 0 && Walls.Vertices.Count == 0;
	public static PoolMesh Empty => new( new PoolMeshPart(), new PoolMeshPart(), new PoolMeshPart() );
}

/// <summary>
/// The measures of a pool's rim and water (docs/POOLS.md). Heights are engine units (a cell is 2 wide), the rim's width a fraction of one cell. The terrain cannot be dug, so a pool is a
/// raised basin: the water lies <see cref="WaterLift"/> above the ground it stands on, and the coping <see cref="CopingRise"/> above the water.
/// </summary>
/// <param name="RimWidth">Width of the coping band along a pool edge, as a fraction of a cell (0 to 0.5, exclusive).</param>
/// <param name="WaterLift">How far the water surface lies above the ground at each cell corner. It must exceed the most the drawn ground can rise above the corner-to-corner surface inside a cell
/// (half the corner spread, 0.125 at <see cref="PoolBuilder.MaximumCornerSpread"/>), or the land would show through the water.</param>
/// <param name="CopingRise">How far the coping stands above the water.</param>
// [EXT:pools-render] OpenTPW's own measures; the original game has no pools
public sealed record PoolMeshOptions( float RimWidth = 0.2f, float WaterLift = 0.15f, float CopingRise = 0.1f )
{
	public static PoolMeshOptions Default { get; } = new();
}
