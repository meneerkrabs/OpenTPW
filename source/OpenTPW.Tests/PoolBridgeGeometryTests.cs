using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>CPU checks on the original Blender mesh shipped with the pool bridge; no original game files or GPU are required.</summary>
[TestClass]
public class PoolBridgeGeometryTests
{
	private static string Folder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var path = Path.Combine( directory.FullName, "content", "pools", "bridge" );
			if ( File.Exists( Path.Combine( path, "bridge.json" ) ) ) return path;
		}
		throw new FileNotFoundException( "The committed pool bridge asset was not found above the test binaries." );
	}

	private static JsonDocument Read() => JsonDocument.Parse( File.ReadAllText( Path.Combine( Folder(), "bridge.json" ) ) );
	private static float[][] Vertices( JsonElement part ) => part.GetProperty( "vertices" ).EnumerateArray().Select( v => v.EnumerateArray().Select( x => x.GetSingle() ).ToArray() ).ToArray();
	private static SVector3 Position( float[] v ) => new( v[0], v[1], v[2] );
	private static SVector3 Normal( float[] v ) => new( v[3], v[4], v[5] );

	[TestMethod]
	public void TheVersionedAssetContainsOnlyTheThreeOriginalTexturedBridgeParts()
	{
		using var json = Read();
		var root = json.RootElement;
		Assert.AreEqual( 1, root.GetProperty( "version" ).GetInt32() );
		Assert.AreEqual( 10, root.GetProperty( "unitsPerCell" ).GetInt32() );
		Assert.AreEqual( 20, root.GetProperty( "spanStart" ).GetInt32() );
		Assert.AreEqual( 50, root.GetProperty( "spanEnd" ).GetInt32() );
		Assert.AreEqual( 10, root.GetProperty( "deckHeight" ).GetInt32() );
		var parts = root.GetProperty( "parts" ).EnumerateArray().ToArray();
		CollectionAssert.AreEquivalent( new[] { "deck", "frame", "accent" }, parts.Select( p => p.GetProperty( "name" ).GetString() ).ToArray() );
		foreach ( var part in parts )
		{
			var name = part.GetProperty( "name" ).GetString();
			Assert.AreEqual( name + ".png", part.GetProperty( "texture" ).GetString() );
			var png = File.ReadAllBytes( Path.Combine( Folder(), name + ".png" ) );
			CollectionAssert.AreEqual( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take( 8 ).ToArray(), "PNG signature" );
			Assert.AreEqual( "IHDR", System.Text.Encoding.ASCII.GetString( png, 12, 4 ) );
			Assert.AreEqual( 32u, BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( 16, 4 ) ) );
			Assert.AreEqual( 32u, BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( 20, 4 ) ) );
			Assert.IsTrue( png.Length < 4096, "Small procedural textures keep the browser payload bounded." );
			var image = StbImageSharp.ImageResult.FromMemory( png, StbImageSharp.ColorComponents.RedGreenBlueAlpha );
			Assert.AreEqual( 32, image.Width );
			Assert.AreEqual( 32, image.Height );
			Assert.AreEqual( 32 * 32 * 4, image.Data.Length );
			Assert.IsTrue( Enumerable.Range( 0, 32 * 32 ).All( i => image.Data[i * 4 + 3] == 255 ), "Bridge surfaces are opaque." );
		}
	}

	[TestMethod]
	public void EveryVertexAndTriangleIsFiniteIndexedAndFacesItsUnitNormal()
	{
		using var json = Read();
		var triangles = 0;
		foreach ( var part in json.RootElement.GetProperty( "parts" ).EnumerateArray() )
		{
			var vertices = Vertices( part );
			Assert.IsTrue( vertices.Length > 0 );
			foreach ( var v in vertices )
			{
				Assert.AreEqual( 8, v.Length, "Position, normal and UV." );
				Assert.IsTrue( v.All( float.IsFinite ) );
				Assert.AreEqual( 1f, Normal( v ).Length(), 0.0001f );
			}
			var indices = part.GetProperty( "indices" ).EnumerateArray().Select( v => v.GetInt32() ).ToArray();
			Assert.IsTrue( indices.Length > 0 && indices.Length % 3 == 0 );
			Assert.IsTrue( indices.All( i => i >= 0 && i < vertices.Length ) );
			for ( var i = 0; i < indices.Length; i += 3 )
			{
				var a = vertices[indices[i]];
				var b = vertices[indices[i + 1]];
				var c = vertices[indices[i + 2]];
				Assert.IsTrue( SVector3.Dot( SVector3.Cross( Position( b ) - Position( a ), Position( c ) - Position( a ) ), Normal( a ) ) > 0,
					"A non-degenerate triangle must face outward (counter-clockwise)." );
			}
			triangles += indices.Length / 3;
		}
		Assert.IsTrue( triangles is > 100 and < 5000, "The complete bevelled bridge remains a small render mesh." );
	}

	[TestMethod]
	public void TheDeckAndApproachesFitSevenByOneCellsWithTheWalkSurfaceTenUnitsHigh()
	{
		using var json = Read();
		var parts = json.RootElement.GetProperty( "parts" ).EnumerateArray().ToArray();
		var all = parts.SelectMany( Vertices ).ToArray();
		Assert.IsTrue( all.All( v => v[0] >= 0 && v[0] <= 70 && v[1] >= 0 && v[1] <= 10 && v[2] >= 0 ) );
		Assert.AreEqual( 0f, all.Min( v => v[0] ), 0.0001f );
		Assert.AreEqual( 70f, all.Max( v => v[0] ), 0.0001f );
		Assert.AreEqual( 0f, all.Min( v => v[1] ), 0.0001f );
		Assert.AreEqual( 10f, all.Max( v => v[1] ), 0.0001f );
		var deck = Vertices( parts.Single( p => p.GetProperty( "name" ).GetString() == "deck" ) );
		Assert.AreEqual( 10f, deck.Max( v => v[2] ), 0.0001f );
		Assert.IsTrue( deck.Where( v => v[0] > 20 && v[0] < 50 ).All( v => v[2] >= 9.2f ), "The deck does not fill the swimming channel." );
		for ( var step = 0; step < 10; step++ )
		{
			var left = deck.Where( v => v[0] > step * 2 && v[0] < step * 2 + 2 ).ToArray();
			var right = deck.Where( v => v[0] > 68 - step * 2 && v[0] < 70 - step * 2 ).ToArray();
			Assert.AreEqual( step + 1f, left.Max( v => v[2] ), 0.0001f );
			Assert.AreEqual( step + 1f, right.Max( v => v[2] ), 0.0001f );
		}
	}

	private static List<SVector3> Clip( List<SVector3> polygon, float edge, int sign )
	{
		var result = new List<SVector3>();
		for ( var i = 0; i < polygon.Count; i++ )
		{
			var a = polygon[i];
			var b = polygon[(i + 1) % polygon.Count];
			var aInside = sign * (a.X - edge) >= 0;
			var bInside = sign * (b.X - edge) >= 0;
			if ( aInside ) result.Add( a );
			if ( aInside != bInside ) result.Add( SVector3.Lerp( a, b, (edge - a.X) / (b.X - a.X) ) );
		}
		return result;
	}

	[TestMethod]
	public void NoTrianglePlacesALowSupportAnywhereInsideTheThreeWaterCells()
	{
		using var json = Read();
		foreach ( var part in json.RootElement.GetProperty( "parts" ).EnumerateArray() )
		{
			var vertices = Vertices( part );
			var indices = part.GetProperty( "indices" ).EnumerateArray().Select( v => v.GetInt32() ).ToArray();
			for ( var i = 0; i < indices.Length; i += 3 )
			{
				var triangle = indices.Skip( i ).Take( 3 ).Select( index => Position( vertices[index] ) ).ToList();
				// Clip triangles, rather than testing vertices alone: a diagonal support could cross the water with both endpoints on land.
				var waterPart = Clip( Clip( triangle, 20.0001f, 1 ), 49.9999f, -1 );
				Assert.IsTrue( waterPart.All( v => v.Z >= 8.89f ), "The full swimming span has at least 8.89 model units of air above ground." );
				// Full water-anchored swim-sprite audit: maximum above-hotspot rectangle is 1.215340 engine units.
				// Conservatively make its entire camera-up extent vertical and add the maximum visible wave crest.
				var swimmerTop = -new PoolMeshOptions().WaterDrop + PoolWaves.MaxHeave + 1.215340f;
				Assert.IsTrue( waterPart.All( v => v.Z * OriginalParkPlacement.CellSize / 10f - swimmerTop >= 0.57765f ),
					"Maximum crest plus full swimmer sprite must retain the audited 0.57766 clearance (float tolerance 0.00001)." );
			}
		}
	}
	[TestMethod]
	public void TheRuntimeMeshFitsItsCellsAndPreservesWindingInAllFourDirections()
	{
		var asset = PoolBridgeAsset.Load( Path.Combine( Folder(), "bridge.json" ) );
		var origin = new System.Numerics.Vector2( -17, 9 );
		foreach ( var cellSize in new[] { 2f, 3.5f } )
			for ( var direction = 0; direction < 4; direction++ )
			{
				var bridge = new PoolBridge( 12, 14, direction, 1250 );
				var cells = Enumerable.Range( 0, PoolBridge.Length ).Select( bridge.Cell ).ToArray();
				var meshes = asset.Parts.Select( part => PoolBridgeMeshBuilder.Build( part, new[] { bridge }, origin, cellSize ) ).ToArray();
				var vertices = meshes.SelectMany( mesh => mesh.Vertices ).ToArray();
				Assert.AreEqual( origin.X + cells.Min( c => c.X ) * cellSize, vertices.Min( v => v.Position.X ), 0.0001f );
				Assert.AreEqual( origin.X + (cells.Max( c => c.X ) + 1) * cellSize, vertices.Max( v => v.Position.X ), 0.0001f );
				Assert.AreEqual( origin.Y + cells.Min( c => c.Y ) * cellSize, vertices.Min( v => v.Position.Y ), 0.0001f );
				Assert.AreEqual( origin.Y + (cells.Max( c => c.Y ) + 1) * cellSize, vertices.Max( v => v.Position.Y ), 0.0001f );
				Assert.AreEqual( bridge.Ground, vertices.Min( v => v.Position.Z ), 0.0001f );
				var deck = meshes[Array.FindIndex( asset.Parts, p => p.Name == "deck" )];
				Assert.AreEqual( bridge.Ground + asset.DeckHeight * cellSize / asset.UnitsPerCell, deck.Vertices.Max( v => v.Position.Z ), 0.0001f );
				for ( var partIndex = 0; partIndex < meshes.Length; partIndex++ )
				{
					var mesh = meshes[partIndex];
					var part = asset.Parts[partIndex];
					Assert.AreEqual( part.Vertices.Length, mesh.Vertices.Count );
					CollectionAssert.AreEqual( part.Indices, mesh.Indices.ToArray() );
					for ( var i = 0; i < mesh.Vertices.Count; i++ )
					{
						Assert.AreEqual( 1f, mesh.Vertices[i].Normal.Length(), 0.0001f );
						Assert.AreEqual( part.Vertices[i][6], mesh.Vertices[i].Uv.X, 0.0001f );
						Assert.AreEqual( 1 - part.Vertices[i][7], mesh.Vertices[i].Uv.Y, 0.0001f, "Blender bottom-up UVs become top-down PNG UVs." );
					}
					for ( var i = 0; i < mesh.Indices.Count; i += 3 )
					{
						var a = mesh.Vertices[mesh.Indices[i]];
						var b = mesh.Vertices[mesh.Indices[i + 1]];
						var c = mesh.Vertices[mesh.Indices[i + 2]];
						Assert.IsTrue( SVector3.Dot( SVector3.Cross( b.Position - a.Position, c.Position - a.Position ), a.Normal ) > 0 );
					}
				}
			}
	}

	[TestMethod]
	public void BatchingBridgesOffsetsIndicesAndAnEmptyWorldProducesNoGeometry()
	{
		var asset = PoolBridgeAsset.Load( Path.Combine( Folder(), "bridge.json" ) );
		foreach ( var part in asset.Parts )
		{
			var origin = System.Numerics.Vector2.Zero;
			var empty = PoolBridgeMeshBuilder.Build( part, Array.Empty<PoolBridge>(), origin );
			Assert.AreEqual( 0, empty.Vertices.Count );
			Assert.AreEqual( 0, empty.Indices.Count );
			var mesh = PoolBridgeMeshBuilder.Build( part, new[] { new PoolBridge( 8, 8, 0, 0 ), new PoolBridge( 20, 20, 2, 2000 ) }, origin );
			Assert.AreEqual( part.Vertices.Length * 2, mesh.Vertices.Count );
			Assert.AreEqual( part.Indices.Length * 2, mesh.Indices.Count );
			CollectionAssert.AreEqual( part.Indices.Select( i => i + part.Vertices.Length ).ToArray(), mesh.Indices.Skip( part.Indices.Length ).ToArray() );
		}
	}

	[TestMethod]
	public void WalkHeightMatchesEveryAuthoredStairTopInAllFourDirections()
	{
		var asset = PoolBridgeAsset.Load( Path.Combine( Folder(), "bridge.json" ) );
		var deck = asset.Parts.Single( p => p.Name == "deck" );
		for ( var direction = 0; direction < 4; direction++ )
		{
			var bridge = new PoolBridge( 12, 14, direction, 1250 );
			var (dx, dy) = GuestPathGrid.Directions[direction];
			foreach ( var along in Enumerable.Range( 0, 10 ).SelectMany( step => new[] { 1f + 2 * step, 69f - 2 * step } ).Concat( new[] { 25f, 35f, 45f } ) )
			{
				// Measure the committed tread's vertices, not another copy of HeightAt's formula.
				var surface = deck.Vertices.Where( v => MathF.Abs( v[0] - along ) < 1f ).Max( v => v[2] );
				var x = bridge.X + .5f + dx * (along / asset.UnitsPerCell - .5f);
				var y = bridge.Y + .5f + dy * (along / asset.UnitsPerCell - .5f);
				Assert.AreEqual( bridge.Ground + surface * OriginalParkPlacement.CellSize / asset.UnitsPerCell,
					bridge.HeightAt( x, y ), 0.0001f, $"Direction {direction}, model X {along}: feet on the actual tread." );
			}
		}
	}

}
