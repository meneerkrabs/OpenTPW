using System;
using System.Collections.Generic;
using System.Linq;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The pool mesh generation of the opentpw.pools extension on the CPU (docs/POOLS.md): the 8-neighbour tiling, seams, counts, normals and UV ranges.</summary>
[TestClass]
public class PoolMeshTests
{
	private const float CellSize = 2f;
	private static readonly Vector2 Origin = new( -10, -10 );

	private static PoolMesh Build( IEnumerable<(int X, int Y)> cells, Func<int, int, float>? height = null, PoolMeshOptions? options = null )
	{
		var set = cells.ToHashSet();
		var corner = height ?? ( ( _, _ ) => 1f );
		return PoolMeshBuilder.Build( set, ( x, y ) => set.Contains( (x, y) ), ( cellX, cellY ) => GroundAt( corner, cellX, cellY ), Origin, CellSize, options );
	}

	/// <summary>The mesh of one cell (5, 5) whose neighbours are the pool cells of <paramref name="mask"/>; only the centre cell is built.</summary>
	private static PoolMesh BuildForMask( PoolNeighbours mask )
	{
		var neighbours = new Dictionary<PoolNeighbours, (int X, int Y)>
		{
			[PoolNeighbours.North] = (5, 4), [PoolNeighbours.NorthEast] = (6, 4), [PoolNeighbours.East] = (6, 5), [PoolNeighbours.SouthEast] = (6, 6),
			[PoolNeighbours.South] = (5, 6), [PoolNeighbours.SouthWest] = (4, 6), [PoolNeighbours.West] = (4, 5), [PoolNeighbours.NorthWest] = (4, 4)
		};
		var pool = neighbours.Where( pair => (mask & pair.Key) != 0 ).Select( pair => pair.Value ).Append( (5, 5) ).ToHashSet();
		return PoolMeshBuilder.Build( new[] { (5, 5) }, ( x, y ) => pool.Contains( (x, y) ), ( _, _ ) => 1f, Origin, CellSize );
	}

	private static float XyArea( PoolMeshPart part )
	{
		var area = 0f;
		for ( var index = 0; index < part.Indices.Count; index += 3 )
		{
			var (a, b, c) = (part.Vertices[part.Indices[index]].Position, part.Vertices[part.Indices[index + 1]].Position, part.Vertices[part.Indices[index + 2]].Position);
			area += MathF.Abs( (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y) ) / 2;
		}
		return area;
	}

	[TestMethod]
	public void EveryNeighbourMaskTilesTheCellWithWaterAndCoping()
	{
		for ( var value = 0; value < 256; value++ )
		{
			var mask = (PoolNeighbours)value;
			var mesh = BuildForMask( mask );
			var coping = PoolEdges.CopingTileCount( mask );
			Assert.AreEqual( (9 - coping) * 4, mesh.Water.Vertices.Count, $"mask {value}: water vertices" );
			Assert.AreEqual( coping * 4, mesh.Coping.Vertices.Count, $"mask {value}: coping vertices" );
			Assert.AreEqual( (9 - coping) * 2, mesh.Water.TriangleCount );
			Assert.AreEqual( coping * 2, mesh.Coping.TriangleCount );
			// the two top surfaces together cover the cell exactly once
			Assert.AreEqual( CellSize * CellSize, XyArea( mesh.Water ) + XyArea( mesh.Coping ), 1e-4f, $"mask {value}: the tiles cover the cell" );
			Assert.IsTrue( PoolEdges.IsCoping( mask, 1, 1 ) == false );
		}
	}

	[TestMethod]
	public void TheNamedEdgeCasesHaveTheExpectedRim()
	{
		const PoolNeighbours all = PoolNeighbours.All;
		Assert.AreEqual( 0, PoolEdges.CopingTileCount( all ), "interior: no rim" );
		Assert.AreEqual( 1, PoolEdges.CopingTileCount( all & ~PoolNeighbours.NorthWest ), "inner corner: one corner tile" );
		Assert.IsTrue( PoolEdges.IsCoping( all & ~PoolNeighbours.NorthWest, 0, 0 ) );
		Assert.AreEqual( 3, PoolEdges.CopingTileCount( all & ~PoolNeighbours.North ), "straight edge: the strip and its two corners" );
		Assert.AreEqual( 5, PoolEdges.CopingTileCount( all & ~(PoolNeighbours.North | PoolNeighbours.NorthWest | PoolNeighbours.West) ), "outer corner" );
		Assert.AreEqual( 8, PoolEdges.CopingTileCount( PoolNeighbours.None ), "a lone cell is all rim round one pool tile" );
		var lone = BuildForMask( PoolNeighbours.None );
		Assert.AreEqual( 1, lone.Water.TriangleCount / 2 );
		// two cells meeting only at a corner: both keep their corner tile as coping
		Assert.IsTrue( PoolEdges.IsCoping( PoolNeighbours.SouthEast, 2, 2 ) );
	}

	[TestMethod]
	public void NormalsUvsAndIndicesAreValidForEveryPart()
	{
		var mesh = Build( new[] { (3, 3), (4, 3), (5, 3), (3, 4), (3, 5), (6, 6) } );
		foreach ( var part in new[] { mesh.Water, mesh.Coping, mesh.Walls } )
		{
			Assert.IsTrue( part.Vertices.Count > 0 );
			Assert.AreEqual( 0, part.Indices.Count % 3 );
			Assert.IsTrue( part.Indices.All( index => index >= 0 && index < part.Vertices.Count ) );
			foreach ( var vertex in part.Vertices )
			{
				Assert.AreEqual( 1f, vertex.Normal.Length(), 1e-5f, "unit normals" );
				Assert.IsTrue( vertex.Uv.X >= 0 && vertex.Uv.X <= 1 && vertex.Uv.Y >= 0 && vertex.Uv.Y <= 1, $"uv {vertex.Uv}" );
			}
			// every triangle winds counter-clockwise around its vertex normal (right-handed)
			for ( var index = 0; index < part.Indices.Count; index += 3 )
			{
				var (a, b, c) = (part.Vertices[part.Indices[index]], part.Vertices[part.Indices[index + 1]], part.Vertices[part.Indices[index + 2]]);
				Assert.IsTrue( Vector3.Dot( Vector3.Cross( b.Position - a.Position, c.Position - a.Position ), a.Normal ) > 0, "winding agrees with the normal" );
			}
		}
		Assert.IsTrue( mesh.Water.Vertices.All( vertex => vertex.Normal == new Vector3( 0, 0, 1 ) ) && mesh.Coping.Vertices.All( vertex => vertex.Normal == new Vector3( 0, 0, 1 ) ), "the top surfaces face up" );
		Assert.IsTrue( mesh.Walls.Vertices.All( vertex => vertex.Normal.Z == 0 ), "walls are vertical" );
	}

	/// <summary>Welds the three parts by position and returns how many triangles use each undirected edge.</summary>
	private static Dictionary<((long, long, long), (long, long, long)), int> EdgeUses( PoolMesh mesh )
	{
		static (long, long, long) Key( Vector3 position ) => ((long)MathF.Round( position.X * 10000 ), (long)MathF.Round( position.Y * 10000 ), (long)MathF.Round( position.Z * 10000 ));
		var uses = new Dictionary<((long, long, long), (long, long, long)), int>();
		foreach ( var part in new[] { mesh.Water, mesh.Coping, mesh.Walls } )
		{
			for ( var index = 0; index < part.Indices.Count; index += 3 )
			{
				var corners = new[] { Key( part.Vertices[part.Indices[index]].Position ), Key( part.Vertices[part.Indices[index + 1]].Position ), Key( part.Vertices[part.Indices[index + 2]].Position ) };
				for ( var corner = 0; corner < 3; corner++ )
				{
					var (first, second) = (corners[corner], corners[(corner + 1) % 3]);
					var edge = first.CompareTo( second ) <= 0 ? (first, second) : (second, first);
					uses[edge] = uses.GetValueOrDefault( edge ) + 1;
				}
			}
		}
		return uses;
	}

	private static float GroundAt( Func<int, int, float> corner, float cellX, float cellY )
	{
		int x = (int)MathF.Floor( cellX ), y = (int)MathF.Floor( cellY );
		float u = cellX - x, v = cellY - y;
		return corner( x, y ) * (1 - u) * (1 - v) + corner( x + 1, y ) * u * (1 - v) + corner( x, y + 1 ) * (1 - u) * v + corner( x + 1, y + 1 ) * u * v;
	}

	[TestMethod]
	public void SeamsBetweenCellsAreWatertightForAnyShapeOnFlatAndSlopedGround()
	{
		var shapes = new Dictionary<string, (int, int)[]>
		{
			["rectangle"] = Enumerable.Range(2, 4).SelectMany( x => Enumerable.Range( 2, 3 ).Select( y => (x, y) ) ).ToArray(),
			["L"] = new[] { (2, 2), (3, 2), (4, 2), (2, 3), (2, 4) },
			["ring"] = new[] { (2, 2), (3, 2), (4, 2), (2, 3), (4, 3), (2, 4), (3, 4), (4, 4) },
			["diagonal touch"] = new[] { (2, 2), (3, 3), (4, 4) },
			["lone"] = new[] { (3, 3) },
			["two blobs"] = new[] { (1, 1), (2, 1), (6, 6), (7, 6), (7, 7) }
		};
		Func<int, int, float>[] grounds = { ( _, _ ) => 1f, ( x, y ) => 1f + 0.05f * x - 0.04f * y + ((x * 7 + y * 3) % 5) * 0.01f };
		foreach ( var (name, cells) in shapes )
		{
			foreach ( var ground in grounds )
			{
				var mesh = Build( cells, ground );
				var uses = EdgeUses( mesh );
				// only cells that touch at a single corner (the diagonal case) pinch: their skirts meet along the one vertical edge at that corner
				Assert.IsFalse( uses.Any( pair => pair.Value > 2 && (name != "diagonal touch" || pair.Key.Item1.Item1 != pair.Key.Item2.Item1 || pair.Key.Item1.Item2 != pair.Key.Item2.Item2) ), $"{name}: no edge is shared by three triangles" );
				// the only open edges are the bottom edges of the skirt: they lie on the ground, along the outline of the pool
				var set = cells.ToHashSet();
				var open = uses.Where( pair => pair.Value == 1 ).Select( pair => pair.Key ).ToArray();
				var outline = cells.Sum( cell => new[] { (0, -1), (1, 0), (0, 1), (-1, 0) }.Count( step => !set.Contains( (cell.Item1 + step.Item1, cell.Item2 + step.Item2) ) ) );
				Assert.AreEqual( outline * 3, open.Length, $"{name}: three open skirt edges per outline side of a cell" );
				foreach ( var (first, second) in open )
				{
					foreach ( var end in new[] { first, second } )
					{
						var position = new Vector3( end.Item1 / 10000f, end.Item2 / 10000f, end.Item3 / 10000f );
						var (cellX, cellY) = ((position.X - Origin.X) / CellSize, (position.Y - Origin.Y) / CellSize);
						Assert.AreEqual( GroundAt( ground, cellX, cellY ), position.Z, 2e-4f, $"{name}: an open edge lies on the ground" );
					}
				}
				// every triangle edge inside the pool is used by exactly two triangles: closed apart from the skirt
				Assert.IsTrue( uses.Values.Count( count => count == 2 ) > 0 );
			}
		}
	}

	[TestMethod]
	public void WaterLiesAboveTheGroundAndCopingAboveTheWater()
	{
		// the terrain cannot be dug, so the pool is a raised basin: water over the ground, coping over the water
		var options = new PoolMeshOptions( 0.25f, 0.15f, 0.1f );
		Func<int, int, float> corner = ( x, y ) => 1f + 0.02f * x + 0.03f * y;
		var mesh = Build( new[] { (2, 2), (3, 2), (2, 3), (3, 3) }, corner, options );
		float GroundOf( PoolVertex vertex ) => GroundAt( corner, ( vertex.Position.X - Origin.X ) / CellSize, ( vertex.Position.Y - Origin.Y ) / CellSize );
		Assert.IsTrue( mesh.Water.Vertices.All( vertex => MathF.Abs( vertex.Position.Z - (GroundOf( vertex ) + 0.15f) ) < 1e-4f ), "the water is the ground plus the lift" );
		Assert.IsTrue( mesh.Coping.Vertices.All( vertex => MathF.Abs( vertex.Position.Z - (GroundOf( vertex ) + 0.25f) ) < 1e-4f ), "the coping stands above the water" );
		// the water is one surface: cells that share a corner agree on its height
		var byCorner = mesh.Water.Vertices.GroupBy( vertex => (MathF.Round( vertex.Position.X, 3 ), MathF.Round( vertex.Position.Y, 3 )) );
		Assert.IsTrue( byCorner.All( group => group.Select( vertex => MathF.Round( vertex.Position.Z, 4 ) ).Distinct().Count() == 1 ), "no step in the water between cells" );
		// the lift covers the twist of the drawn terrain: half the corner spread at the ground rule's limit
		Assert.IsTrue( options.WaterLift > PoolBuilder.MaximumCornerSpread / 2 );
		Assert.IsTrue( PoolMeshOptions.Default.WaterLift > PoolBuilder.MaximumCornerSpread / 2 );
	}

	[TestMethod]
	public void TheMeshFollowsTheGroundHeightAndTheOriginAndIsDeterministic()
	{
		var first = Build( new[] { (4, 4), (5, 4) } );
		var second = Build( new[] { (5, 4), (4, 4), (4, 4) } );
		CollectionAssert.AreEqual( first.Water.Vertices.ToArray(), second.Water.Vertices.ToArray(), "input order and duplicates do not matter" );
		CollectionAssert.AreEqual( first.Walls.Indices.ToArray(), second.Walls.Indices.ToArray() );
		var minimum = first.Coping.Vertices.Min( vertex => vertex.Position.X );
		Assert.AreEqual( Origin.X + 4 * CellSize, minimum, 1e-4f );
		Assert.IsTrue( PoolMesh.Empty.IsEmpty );
		Assert.IsTrue( Build( Array.Empty<(int, int)>() ).IsEmpty );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Build( new[] { (1, 1) }, options: new PoolMeshOptions( 0.5f ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Build( new[] { (1, 1) }, options: new PoolMeshOptions( 0f ) ) );
	}

	[TestMethod]
	public void TheGeneratedTexturesTileAndAreStable()
	{
		foreach ( var texture in new[] { PoolTextures.Coping(), PoolTextures.Wall(), PoolTextures.Water() } )
		{
			Assert.AreEqual( PoolTextures.Size * PoolTextures.Size * 4, texture.Length );
			Assert.IsTrue( Enumerable.Range( 0, PoolTextures.Size * PoolTextures.Size ).All( pixel => texture[pixel * 4 + 3] == 255 ) );
		}
		Assert.IsTrue( PoolTextures.Water().SequenceEqual( PoolTextures.Water() ), "deterministic" );
		// the water wraps: the row after the last is the first row's continuation, so the sine terms have whole periods (the seam is as smooth as any neighbour pair)
		var water = PoolTextures.Water();
		int Red( int x, int y ) => water[((y % PoolTextures.Size) * PoolTextures.Size + x % PoolTextures.Size) * 4];
		var seam = Enumerable.Range( 0, PoolTextures.Size ).Average( x => Math.Abs( Red( x, PoolTextures.Size - 1 ) - Red( x, PoolTextures.Size ) ) );
		var inside = Enumerable.Range( 0, PoolTextures.Size ).Average( x => Math.Abs( Red( x, 30 ) - Red( x, 31 ) ) );
		Assert.IsTrue( seam <= inside * 2 + 4, $"seam {seam} against interior {inside}" );
	}
}
