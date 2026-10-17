using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>The pool mesh generation of the opentpw.pools extension on the CPU (docs/POOLS.md): the 8-neighbour tiling, the walls and floor, seams, counts, normals and UV ranges.</summary>
[TestClass]
public class PoolMeshTests
{
	private const float CellSize = 2f;
	private static readonly Vector2 Origin = new( -10, -10 );
	private static readonly PoolMeshOptions Options = PoolMeshOptions.Default;
	private static readonly int[] TileClass = { 0, 1, 1, 2 };

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

	/// <summary>Fine tiles (of the 4 x 4 per cell) that are coping for a mask.</summary>
	private static int CopingFineTiles( PoolNeighbours mask ) =>
		Enumerable.Range( 0, 4 ).Sum( row => Enumerable.Range( 0, 4 ).Count( column => PoolEdges.IsCoping( mask, TileClass[column], TileClass[row] ) ) );

	[TestMethod]
	public void EveryNeighbourMaskTilesTheCellWithWaterAndCoping()
	{
		for ( var value = 0; value < 256; value++ )
		{
			var mask = (PoolNeighbours)value;
			var mesh = BuildForMask( mask );
			var coping = CopingFineTiles( mask );
			Assert.AreEqual( coping * 4, mesh.Coping.Vertices.Count, $"mask {value}: coping vertices" );
			Assert.AreEqual( coping * 2, mesh.Coping.TriangleCount );
			Assert.AreEqual( (16 - coping) * 4, mesh.Water.Vertices.Count, $"mask {value}: water vertices" );
			Assert.AreEqual( (16 - coping) * 4, mesh.Floor.Vertices.Count, $"mask {value}: the floor lies under every water tile" );
			// the two top surfaces together cover the cell exactly once, and the floor covers what the water covers
			Assert.AreEqual( CellSize * CellSize, XyArea( mesh.Water ) + XyArea( mesh.Coping ), 1e-4f, $"mask {value}: the tiles cover the cell" );
			Assert.AreEqual( XyArea( mesh.Water ), XyArea( mesh.Floor ), 1e-4f );
			Assert.IsFalse( PoolEdges.IsCoping( mask, 1, 1 ), "the middle is always water" );
			// 12 fine rim tiles when the cell has no pool neighbour at all, none inside a pool; a lone cell has a rim a quarter of a cell wide round a half-cell pool
			if ( mask == PoolNeighbours.None )
				Assert.AreEqual( 12, coping );
			if ( mask == PoolNeighbours.All )
				Assert.AreEqual( 0, coping );
		}
	}

	[TestMethod]
	public void TheNamedEdgeCasesHaveTheExpectedRimAndWalls()
	{
		const PoolNeighbours all = PoolNeighbours.All;
		Assert.AreEqual( 0, PoolEdges.CopingTileCount( all ), "interior: no rim" );
		Assert.AreEqual( 0, BuildForMask( all ).UpperWalls.Vertices.Count + BuildForMask( all ).LowerWalls.Vertices.Count, "an interior cell has no wall" );
		Assert.AreEqual( 1, PoolEdges.CopingTileCount( all & ~PoolNeighbours.NorthWest ), "inner corner: one corner tile" );
		Assert.IsTrue( PoolEdges.IsCoping( all & ~PoolNeighbours.NorthWest, 0, 0 ) );
		Assert.AreEqual( 3, PoolEdges.CopingTileCount( all & ~PoolNeighbours.North ), "straight edge: the strip and its two corners" );
		Assert.AreEqual( 5, PoolEdges.CopingTileCount( all & ~(PoolNeighbours.North | PoolNeighbours.NorthWest | PoolNeighbours.West) ), "outer corner" );
		Assert.AreEqual( 8, PoolEdges.CopingTileCount( PoolNeighbours.None ), "a lone cell is all rim round one pool tile" );
		// walls: a quad for each tile edge between a coping tile and a water tile, below the water line (lower) and above it (upper), and the skirt along the outline (four quads per side)
		var straight = BuildForMask( all & ~PoolNeighbours.North );
		Assert.AreEqual( 4, straight.LowerWalls.Vertices.Count / 4, "a straight edge: one lower wall quad for each of the four tiles along the cell" );
		Assert.AreEqual( 4 + 4, straight.UpperWalls.Vertices.Count / 4, "four upper wall quads and the four skirt quads of its outer side" );
		var inner = BuildForMask( all & ~PoolNeighbours.NorthWest );
		Assert.AreEqual( 2, inner.LowerWalls.Vertices.Count / 4, "an inner corner: the two sides of the corner tile" );
		Assert.AreEqual( 2, inner.UpperWalls.Vertices.Count / 4, "and no skirt: both neighbours are pool" );
		var lone = BuildForMask( PoolNeighbours.None );
		Assert.AreEqual( 8, lone.LowerWalls.Vertices.Count / 4, "a lone cell: the whole outline of its 2 x 2 water tiles" );
		Assert.AreEqual( 8 + 16, lone.UpperWalls.Vertices.Count / 4 );
	}

	[TestMethod]
	public void TheBasinIsDugToItsDepthBelowTheWaterAndTheWaterBelowTheDeck()
	{
		var mesh = Build( new[] { (3, 3), (4, 3), (3, 4), (4, 4) }, ( x, y ) => 1f + 0.05f * x );
		var water = mesh.Water.Vertices.Select( vertex => vertex.Position.Z ).Distinct().Select( z => MathF.Round( z, 4 ) ).ToArray();
		Assert.AreEqual( 1, water.Length, "one connected pool is one level surface" );
		var ground = 1f + 0.05f * 3;
		Assert.AreEqual( ground - Options.WaterDrop, water[0], 1e-4f, "the water lies a little below the lowest ground of the pool" );
		Assert.IsTrue( mesh.Floor.Vertices.All( vertex => MathF.Abs( vertex.Position.Z - (water[0] - Options.Depth) ) < 1e-4f ), "a flat floor the depth below the water" );
		Assert.IsTrue( mesh.Floor.Vertices.All( vertex => vertex.Normal.Z == 1 && vertex.Uv.X == 1 ), "the floor faces up and is fully deep" );
		Assert.IsTrue( Options.Depth is >= 1.5f and <= 2f, "about 1.5 to 2 units" );
		Assert.IsTrue( mesh.Coping.Vertices.All( vertex => vertex.Position.Z > water[0] ), "the deck is above the water" );
		Assert.IsTrue( mesh.Coping.Vertices.All( vertex => MathF.Abs( vertex.Position.Z - (GroundAt( ( x, y ) => 1f + 0.05f * x, ( vertex.Position.X - Origin.X ) / CellSize, ( vertex.Position.Y - Origin.Y ) / CellSize ) + Options.CopingLift) ) < 1e-4f ), "the deck lies on the ground" );
		// a lower wall runs from the water line (depth 0) to the floor (depth 1)
		Assert.IsTrue( mesh.LowerWalls.Vertices.All( vertex => vertex.Uv.X is 0f or 1f ) );
		foreach ( var vertex in mesh.LowerWalls.Vertices )
			Assert.AreEqual( vertex.Uv.X == 0 ? water[0] : water[0] - Options.Depth, vertex.Position.Z, 1e-4f );
		// the upper walls end at the water line inside; the skirt reaches below the ground outside
		Assert.IsTrue( mesh.UpperWalls.Vertices.Any( vertex => MathF.Abs( vertex.Position.Z - water[0] ) < 1e-4f ) );
	}

	[TestMethod]
	public void TheRimIsAQuarterOfACellWide()
	{
		Assert.AreEqual( 0.25f, Options.RimWidth );
		var mesh = Build( new[] { (3, 3), (4, 3), (3, 4), (4, 4) } );
		// the water stops a quarter of a cell inside the pool's outline: the nearest water vertex to the west edge is 0.25 * 2 away
		var west = mesh.Water.Vertices.Min( vertex => vertex.Position.X );
		Assert.AreEqual( Origin.X + 3 * CellSize + 0.25f * CellSize, west, 1e-4f );
		var outer = mesh.Coping.Vertices.Min( vertex => vertex.Position.X );
		Assert.AreEqual( Origin.X + 3 * CellSize, outer, 1e-4f );
	}

	[TestMethod]
	public void NormalsUvsAndIndicesAreValidForEveryPart()
	{
		var mesh = Build( new[] { (3, 3), (4, 3), (5, 3), (3, 4), (3, 5), (6, 6) } );
		foreach ( var part in mesh.Parts )
		{
			Assert.IsTrue( part.Vertices.Count > 0 );
			Assert.AreEqual( 0, part.Indices.Count % 3 );
			Assert.IsTrue( part.Indices.All( index => index >= 0 && index < part.Vertices.Count ) );
			foreach ( var vertex in part.Vertices )
			{
				Assert.AreEqual( 1f, vertex.Normal.Length(), 1e-5f, "unit normals" );
				Assert.IsTrue( float.IsFinite( vertex.Uv.X ) && float.IsFinite( vertex.Uv.Y ) );
			}
			// every triangle winds counter-clockwise around its vertex normal (right-handed)
			for ( var index = 0; index < part.Indices.Count; index += 3 )
			{
				var (a, b, c) = (part.Vertices[part.Indices[index]], part.Vertices[part.Indices[index + 1]], part.Vertices[part.Indices[index + 2]]);
				Assert.IsTrue( Vector3.Dot( Vector3.Cross( b.Position - a.Position, c.Position - a.Position ), a.Normal ) > 0, "winding agrees with the normal" );
			}
		}
		var up = new Vector3( 0, 0, 1 );
		Assert.IsTrue( mesh.Water.Vertices.All( vertex => vertex.Normal == up ) && mesh.Coping.Vertices.All( vertex => vertex.Normal == up ) && mesh.Floor.Vertices.All( vertex => vertex.Normal == up ), "the top surfaces face up" );
		Assert.IsTrue( mesh.UpperWalls.Vertices.Concat( mesh.LowerWalls.Vertices ).All( vertex => vertex.Normal.Z == 0 ), "walls are vertical" );
		// texture coordinates: the water carries the edge proximity, the lower parts the depth, both in 0 to 1
		Assert.IsTrue( mesh.Water.Vertices.All( vertex => vertex.Uv.X is >= 0 and <= 1 && vertex.Uv.Y == 0 ) );
		Assert.IsTrue( mesh.LowerWalls.Vertices.Concat( mesh.Floor.Vertices ).All( vertex => vertex.Uv.X is >= 0 and <= 1 ) );
		// the deck and the upper walls are textured in world units: the same texel size everywhere
		foreach ( var vertex in mesh.Coping.Vertices )
		{
			Assert.AreEqual( vertex.Position.X, vertex.Uv.X, 1e-5f );
			Assert.AreEqual( vertex.Position.Y, vertex.Uv.Y, 1e-5f );
		}
	}

	[TestMethod]
	public void TheWaterIsLighterTowardTheEdge()
	{
		var set = Enumerable.Range( 0, 5 ).SelectMany( x => Enumerable.Range( 0, 5 ).Select( y => (x, y) ) ).ToHashSet();
		bool IsPool( int x, int y ) => set.Contains( (x, y) );
		Assert.AreEqual( 1f, PoolMeshBuilder.EdgeProximity( IsPool, 0f, 2.5f ), 1e-6f, "on the outline" );
		Assert.AreEqual( 0.75f, PoolMeshBuilder.EdgeProximity( IsPool, 0.25f, 2.5f ), 1e-6f, "at the wall, a quarter cell in" );
		Assert.AreEqual( 0f, PoolMeshBuilder.EdgeProximity( IsPool, 2.5f, 2.5f ), 1e-6f, "the middle of a big pool is farther than a cell from the edge" );
		Assert.IsTrue( PoolMeshBuilder.EdgeProximity( IsPool, 0.5f, 2.5f ) > PoolMeshBuilder.EdgeProximity( IsPool, 1.0f, 2.5f ) );
		// the same point gives the same value whichever cell asks, and it is continuous across a cell border
		Assert.AreEqual( PoolMeshBuilder.EdgeProximity( IsPool, 1f - 1e-4f, 0.5f ), PoolMeshBuilder.EdgeProximity( IsPool, 1f + 1e-4f, 0.5f ), 1e-3f );
		var mesh = Build( set );
		var groups = mesh.Water.Vertices.GroupBy( vertex => (MathF.Round( vertex.Position.X, 3 ), MathF.Round( vertex.Position.Y, 3 )) );
		Assert.IsTrue( groups.All( group => group.Select( vertex => MathF.Round( vertex.Uv.X, 4 ) ).Distinct().Count() == 1 ), "cells agree on the value at a shared vertex" );
	}

	/// <summary>Welds the parts by position and returns how many triangles use each undirected edge.</summary>
	private static Dictionary<((long, long, long), (long, long, long)), int> EdgeUses( PoolMesh mesh )
	{
		static (long, long, long) Key( Vector3 position ) => ((long)MathF.Round( position.X * 10000 ), (long)MathF.Round( position.Y * 10000 ), (long)MathF.Round( position.Z * 10000 ));
		var uses = new Dictionary<((long, long, long), (long, long, long)), int>();
		foreach ( var part in mesh.Parts )
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
			["rectangle"] = Enumerable.Range( 2, 4 ).SelectMany( x => Enumerable.Range( 2, 3 ).Select( y => (x, y) ) ).ToArray(),
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
				// an edge is shared by two triangles, or by three along the water line (the water tile, the wall above it and the wall below it); only cells that touch at a single corner
				// (the diagonal case) pinch further, where their skirts meet along the one vertical edge at that corner
				foreach ( var (edge, count) in uses.Where( pair => pair.Value > 2 ) )
				{
					var horizontal = edge.Item1.Item3 == edge.Item2.Item3;
					var vertical = edge.Item1.Item1 == edge.Item2.Item1 && edge.Item1.Item2 == edge.Item2.Item2;
					Assert.IsTrue( (count == 3 && horizontal) || (count == 4 && vertical && name == "diagonal touch"), $"{name}: an edge shared by {count} triangles" );
				}
				// the only open edges are the bottom edges of the skirt: four per outline side of a cell, below the ground along the outline of the pool
				var set = cells.ToHashSet();
				var open = uses.Where( pair => pair.Value == 1 ).Select( pair => pair.Key ).ToArray();
				var outline = cells.Sum( cell => new[] { (0, -1), (1, 0), (0, 1), (-1, 0) }.Count( step => !set.Contains( (cell.Item1 + step.Item1, cell.Item2 + step.Item2) ) ) );
				Assert.AreEqual( outline * 4, open.Length, $"{name}: four open skirt edges per outline side of a cell" );
				foreach ( var (first, second) in open )
				{
					foreach ( var end in new[] { first, second } )
					{
						var position = new Vector3( end.Item1 / 10000f, end.Item2 / 10000f, end.Item3 / 10000f );
						var (cellX, cellY) = ((position.X - Origin.X) / CellSize, (position.Y - Origin.Y) / CellSize);
						Assert.AreEqual( GroundAt( ground, cellX, cellY ) - Options.SkirtDepth, position.Z, 2e-4f, $"{name}: an open edge lies below the ground" );
					}
				}
				Assert.IsTrue( uses.Values.Count( count => count == 2 ) > 0 );
			}
		}
	}

	[TestMethod]
	public void EachConnectedPoolHasItsOwnLevelAndGroupsFollowTheSlope()
	{
		// two pools two cells apart on a slope: each is level, the two differ
		var mesh = Build( new[] { (1, 1), (2, 1), (6, 1), (7, 1) }, ( x, y ) => 1f + 0.1f * x );
		var levels = mesh.Water.Vertices.Select( vertex => MathF.Round( vertex.Position.Z, 4 ) ).Distinct().OrderBy( z => z ).ToArray();
		CollectionAssert.AreEqual( new[] { MathF.Round( 1.1f - Options.WaterDrop, 4 ), MathF.Round( 1.6f - Options.WaterDrop, 4 ) }, levels );
		// cells that meet only at a corner are separate pools
		var diagonal = PoolMeshBuilder.WaterLevels( new[] { (2, 2), (3, 3) }, ( x, y ) => x, Options );
		Assert.AreNotEqual( diagonal[(2, 2)], diagonal[(3, 3)] );
	}

	[TestMethod]
	public void TheMeshIsDeterministicAndRefusesBadMeasures()
	{
		var first = Build( new[] { (4, 4), (5, 4) } );
		var second = Build( new[] { (5, 4), (4, 4), (4, 4) } );
		CollectionAssert.AreEqual( first.Water.Vertices.ToArray(), second.Water.Vertices.ToArray(), "input order and duplicates do not matter" );
		CollectionAssert.AreEqual( first.LowerWalls.Indices.ToArray(), second.LowerWalls.Indices.ToArray() );
		Assert.IsTrue( PoolMesh.Empty.IsEmpty );
		Assert.IsTrue( Build( Array.Empty<(int, int)>() ).IsEmpty );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Build( new[] { (1, 1) }, options: new PoolMeshOptions( RimWidth: 0.5f ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Build( new[] { (1, 1) }, options: new PoolMeshOptions( RimWidth: 0f ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Build( new[] { (1, 1) }, options: new PoolMeshOptions( Depth: 0f ) ) );
	}

	// ---- Textures and styles --------------------------------------------------------------------------

	[TestMethod]
	public void TheGeneratedTexturesTileAreStableAndDifferByStyle()
	{
		foreach ( var style in PoolStyles.All )
		{
			var coping = PoolTextures.Coping( style );
			var wall = PoolTextures.Wall( style );
			foreach ( var texture in new[] { coping, wall } )
			{
				Assert.AreEqual( PoolTextures.Size * PoolTextures.Size * 4, texture.Length );
				Assert.IsTrue( Enumerable.Range( 0, PoolTextures.Size * PoolTextures.Size ).All( pixel => texture[pixel * 4 + 3] == 255 ), style.Id );
			}
			Assert.IsTrue( coping.SequenceEqual( PoolTextures.Coping( style ) ), $"{style.Id}: deterministic" );
			// tile joints: the first row and column of every slab hold the joint colour
			var joint = style.CopingJoint;
			for ( var slab = 0; slab < PoolTextures.SlabsPerSide; slab++ )
				Assert.AreEqual( (joint.R, joint.G, joint.B), (coping[(slab * 16 * 64 + 5) * 4], coping[(slab * 16 * 64 + 5) * 4 + 1], coping[(slab * 16 * 64 + 5) * 4 + 2]), $"{style.Id}: joint row" );
			// the bevel: inside the joint, the top edge is lighter than the bottom edge of the same slab
			int Luma( int x, int y ) => coping[(y * 64 + x) * 4] + coping[(y * 64 + x) * 4 + 1] + coping[(y * 64 + x) * 4 + 2];
			Assert.IsTrue( Luma( 8, 1 ) > Luma( 8, 15 ), $"{style.Id}: a light top edge and a shadowed bottom edge" );
		}
		var copings = PoolStyles.All.Select( style => Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( PoolTextures.Coping( style ) ) ) ).ToArray();
		Assert.AreEqual( copings.Length, copings.Distinct().Count(), "every style has its own coping" );
		var water = PoolTextures.Water();
		Assert.IsTrue( water.SequenceEqual( PoolTextures.Water() ), "deterministic" );
		int Red( int x, int y ) => water[((y % PoolTextures.Size) * PoolTextures.Size + x % PoolTextures.Size) * 4];
		var seam = Enumerable.Range( 0, PoolTextures.Size ).Average( x => Math.Abs( Red( x, PoolTextures.Size - 1 ) - Red( x, PoolTextures.Size ) ) );
		var inside = Enumerable.Range( 0, PoolTextures.Size ).Average( x => Math.Abs( Red( x, 30 ) - Red( x, 31 ) ) );
		Assert.IsTrue( seam <= inside * 2 + 4, $"seam {seam} against interior {inside}" );
	}

	[TestMethod]
	public void EachThemeGetsItsOwnStyleAndOtherWorldsTheGenericOne()
	{
		Assert.AreSame( PoolStyles.Jungle, PoolStyles.ForTheme( "jungle" ) );
		Assert.AreSame( PoolStyles.Hallow, PoolStyles.ForTheme( "Hallow" ) );
		Assert.AreSame( PoolStyles.Space, PoolStyles.ForTheme( "SPACE" ) );
		Assert.AreSame( PoolStyles.Fantasy, PoolStyles.ForTheme( "fantasy" ) );
		Assert.AreSame( PoolStyles.Generic, PoolStyles.ForTheme( null ) );
		Assert.AreSame( PoolStyles.Generic, PoolStyles.ForTheme( "tpi-zone" ) );
		Assert.AreSame( PoolStyles.Generic, PoolStyles.ForTheme( "generic" ), "the generic row is the fallback, not a theme" );
		Assert.AreEqual( 5, PoolStyles.All.Count );
		Assert.AreEqual( PoolStyles.All.Count, PoolStyles.All.Select( style => style.Id ).Distinct().Count() );
		// the brief: jungle wood and lagoon green-blue, hallow dark and murky with a glow, space metal and bright cyan with a glow, fantasy pastel
		Assert.AreEqual( PoolCopingPattern.Planks, PoolStyles.Jungle.Coping );
		Assert.IsTrue( PoolStyles.Jungle.WaterShallow.G > PoolStyles.Jungle.WaterShallow.R && PoolStyles.Jungle.WaterShallow.B > PoolStyles.Jungle.WaterShallow.R );
		Assert.AreEqual( PoolCopingPattern.MossyStone, PoolStyles.Hallow.Coping );
		Assert.IsTrue( PoolStyles.Hallow.Glow > 0 && PoolStyles.Hallow.CopingBase.R < 100 && PoolStyles.Hallow.WaterDeep.B > PoolStyles.Hallow.WaterDeep.G );
		// purple, not green: blue and red both above green in the shallow water
		Assert.IsTrue( PoolStyles.Hallow.WaterShallow.B > PoolStyles.Hallow.WaterShallow.G && PoolStyles.Hallow.WaterShallow.R > PoolStyles.Hallow.WaterShallow.G );
		Assert.AreEqual( PoolCopingPattern.RivetedPanels, PoolStyles.Space.Coping );
		Assert.IsTrue( PoolStyles.Space.Glow > PoolStyles.Hallow.Glow && PoolStyles.Space.WaterShallow.G > 200 && PoolStyles.Space.WaterShallow.B > 200 && PoolStyles.Space.WaterShallow.R < 150 );
		Assert.AreEqual( PoolCopingPattern.CandyTiles, PoolStyles.Fantasy.Coping );
		Assert.IsTrue( PoolStyles.Fantasy.CopingBase.R > 200 && PoolStyles.Fantasy.WaterShallow.R > 200 && PoolStyles.Fantasy.WaterShallow.B > 200 );
		Assert.AreEqual( 0f, PoolStyles.Jungle.Glow );
	}
}
