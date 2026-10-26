using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// The queue models on a slope ([BIN:TPW-EXE:0x005229E0], [BIN:TPW-EXE:0x00469A80]): the instance height, the placement, the joints between neighbouring cells (data-free), and the shipped
/// models' nodes and faces and their drape on a landscape (with the original PC data, inconclusive without OPENTPW_GAME_PATH).
/// </summary>
[TestClass]
public class QueueModelDrapeTests
{
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init() => previousFileSystem = FileSystem;

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	/// <summary>A bumpy hillside (integer arithmetic only, so a pinned hash does not depend on a platform's trigonometry): every corner differs, and the cells vary their diagonal.</summary>
	private static ModelHeightfield Hillside( int cells = 12 )
	{
		var heights = new float[(cells + 1) * (cells + 1)];
		for ( var z = 0; z <= cells; z++ )
			for ( var x = 0; x <= cells; x++ )
				heights[z * (cells + 1) + x] = 1.8f * x + z + ((x * 37 + z * 91 + x * z * 13) % 9 - 4) * 0.75f;
		var words = new uint[cells * cells];
		for ( var index = 0; index < words.Length; index++ )
			words[index] = (index * 7 + index / cells) % 3 == 0 ? ModelHeightfield.AntiDiagonalFlag : 0;
		return new ModelHeightfield( cells, cells, 10, 10, 0, 0, Array.Empty<uint>(), heights, words );
	}

	[TestMethod]
	public void TheInstanceHeightIsHalfTheLowestAndHighestCorner()
	{
		var field = Hillside();
		for ( var (x, y) = (0, 0); y < 11; y += 3, x += 2 )
		{
			var corners = new[] { field.GetCornerHeight( x, y ), field.GetCornerHeight( x + 1, y ), field.GetCornerHeight( x, y + 1 ), field.GetCornerHeight( x + 1, y + 1 ) };
			Assert.AreEqual( (corners.Min() + corners.Max()) * 0.5f, QueueModelDrape.BaseHeight( field, x, y ) );
			Assert.AreNotEqual( corners.Average(), QueueModelDrape.BaseHeight( field, x, y ), "not the mean of the four corners" );
		}
	}

	[TestMethod]
	public void AVertexOnTheCellFloorLiesOnTheLandscapeAndTheModelHeightStacksOnIt()
	{
		var field = Hillside();
		foreach ( var yaw in new[] { 0, 90, 180, 270 } )
		{
			var (x, y) = (4, 5);
			var baseHeight = QueueModelDrape.BaseHeight( field, x, y );
			var placement = QueueModelDrape.Placement( field, Matrix4x4.Identity, x, y, yaw, baseHeight );
			var chain = ModelDrape.Chain( placement, Matrix4x4.Identity );
			for ( var step = 0; step <= 10; step += 2 )
			{
				var floor = QueueModelDrape.Vertex( field, chain, new Vector3( step, 0, 10 - step * 0.5f ), baseHeight );
				var landscape = LandscapeSampler.SampleDrape( field.CellCountX, field.CellCountZ, 10, 10, field.GetCornerHeight, field.SplitsAlongAntiDiagonal, floor.X, floor.Z );
				Assert.AreEqual( landscape, floor.Y, 1e-4f, $"yaw {yaw}: the floor stands on the drawn ground, no lift above it" );
				var post = QueueModelDrape.Vertex( field, chain, new Vector3( step, 3.5f, 10 - step * 0.5f ), baseHeight );
				Assert.AreEqual( floor.X, post.X );
				Assert.AreEqual( floor.Z, post.Z );
				Assert.AreEqual( floor.Y + 3.5f, post.Y, 1e-4f, "a post of model height 3.5 stands 3.5 above the ground under it" );
			}
			// the cell's footprint: the placed ground positions stay inside the cell (x, y) to (x + 1, y + 1) whatever the yaw
			var corners = new[] { (0f, 0f), (10f, 0f), (0f, 10f), (10f, 10f) }.Select( corner => QueueModelDrape.Vertex( field, chain, new Vector3( corner.Item1, 0, corner.Item2 ), baseHeight ) ).ToArray();
			Assert.IsTrue( corners.All( corner => corner.X >= x * 10 - 1e-4f && corner.X <= (x + 1) * 10 + 1e-4f && corner.Z >= y * 10 - 1e-4f && corner.Z <= (y + 1) * 10 + 1e-4f ), $"yaw {yaw}" );
		}
	}

	[TestMethod]
	public void NeighbouringCellsMeetOnTheirSharedEdgeAtEveryYaw()
	{
		var field = Hillside();
		var (checkedPoints, gaps) = (0, 0f);
		foreach ( var yawA in new[] { 0, 90, 180, 270 } )
		{
			foreach ( var yawB in new[] { 0, 90, 180, 270 } )
			{
				// cells (3, 4) and (4, 4) share the line x = 40, and have different instance heights on the slope
				var (heightA, heightB) = (QueueModelDrape.BaseHeight( field, 3, 4 ), QueueModelDrape.BaseHeight( field, 4, 4 ));
				Assert.AreNotEqual( heightA, heightB );
				var chainA = ModelDrape.Chain( QueueModelDrape.Placement( field, Matrix4x4.Identity, 3, 4, yawA, heightA ), Matrix4x4.Identity );
				var chainB = ModelDrape.Chain( QueueModelDrape.Placement( field, Matrix4x4.Identity, 4, 4, yawB, heightB ), Matrix4x4.Identity );
				var edgeA = EdgePoints( field, chainA, heightA, 40 );
				var edgeB = EdgePoints( field, chainB, heightB, 40 );
				Assert.IsTrue( edgeA.Count >= 3 && edgeB.Count >= 3, "each cell has local points on the shared line" );
				foreach ( var a in edgeA )
				{
					foreach ( var b in edgeB.Where( b => MathF.Abs( b.Z - a.Z ) < 1e-5f ) )
					{
						// the same ground point: the same height, up to the rounding of the two different instance heights
						gaps = MathF.Max( gaps, MathF.Abs( a.Y - b.Y ) );
						Assert.AreEqual( a.Y, b.Y, 2e-4f, $"yaw {yawA} and {yawB} at z {a.Z}" );
						checkedPoints++;
					}
				}
			}
		}
		Assert.IsTrue( checkedPoints >= 16, $"{checkedPoints} shared points, the largest gap {gaps}" );
	}

	/// <summary>The grid-frame positions of local floor points of a cell whose x lies on <paramref name="line"/>.</summary>
	private static List<Vector3> EdgePoints( ModelHeightfield field, Matrix4x4 chain, float baseHeight, float line )
	{
		var points = new List<Vector3>();
		for ( var step = 0; step <= 10; step += 2 )
		{
			foreach ( var local in new[] { new Vector3( 0, 0, step ), new Vector3( 10, 0, step ), new Vector3( step, 0, 0 ), new Vector3( step, 0, 10 ) } )
			{
				var placed = QueueModelDrape.Vertex( field, chain, local, baseHeight );
				if ( MathF.Abs( placed.X - line ) < 1e-5f )
					points.Add( placed );
			}
		}
		return points;
	}

	[TestMethod]
	public void TheYawTurnsTheModelAboutTheCellAndLeavesTheHeightsAlone()
	{
		var field = Hillside();
		var baseHeight = QueueModelDrape.BaseHeight( field, 2, 2 );
		var chains = new[] { 0, 90, 180, 270 }.Select( yaw => ModelDrape.Chain( QueueModelDrape.Placement( field, Matrix4x4.Identity, 2, 2, yaw, baseHeight ), Matrix4x4.Identity ) ).ToArray();
		// the model's corner (0, 0, 0) lands on a different corner of the cell for each yaw, at that corner's ground height
		var landings = chains.Select( chain => QueueModelDrape.Vertex( field, chain, Vector3.Zero, baseHeight ) ).ToArray();
		var expectedCorners = new[] { (20f, 20f), (20f, 30f), (30f, 30f), (30f, 20f) };
		for ( var index = 0; index < 4; index++ )
		{
			Assert.AreEqual( expectedCorners[index].Item1, landings[index].X, 1e-4f, $"yaw index {index}" );
			Assert.AreEqual( expectedCorners[index].Item2, landings[index].Z, 1e-4f, $"yaw index {index}" );
			Assert.AreEqual( field.GetCornerHeight( (int)expectedCorners[index].Item1 / 10, (int)expectedCorners[index].Item2 / 10 ), landings[index].Y, 1e-4f );
		}
	}

	[TestMethod]
	public void TheDepthCompareIsTheOriginalsLessEqualSoAFloorOnTheGroundShowsOverIt()
	{
		// [BIN:TPW-EXE:0x0057360F depth compare] the device set-up sets ZFUNC to LESSEQUAL once; a queue floor draped exactly onto the ground (no lift) is at the stored depth and must pass
		Assert.AreEqual( Veldrid.ComparisonKind.LessEqual, Material.DepthComparison( MaterialFlags.None ) );
		Assert.AreEqual( Veldrid.ComparisonKind.LessEqual, Material.DepthComparison( MaterialFlags.DisableDepthWrite ), "the depth test stays on without the write" );
		Assert.AreEqual( Veldrid.ComparisonKind.Always, Material.DepthComparison( MaterialFlags.DisableDepthTest | MaterialFlags.DisableDepthWrite ) );
	}

	// ---- the shipped models ------------------------------------------------------------------------

	private static IEnumerable<(string Theme, string Name, ModelFile Model)> ShippedQueueModels()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original queue archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space" } )
		{
			var path = Path.Combine( dataPath, "levels", theme, "queue.wad" );
			if ( !File.Exists( path ) )
				Assert.Inconclusive( $"The {theme} queue archive is missing." );
			using var archive = new WadArchive( path );
			foreach ( var name in QueueModels.Names.Distinct() )
				yield return (theme, name, new ModelFile( new MemoryStream( archive.GetFile( name + ".md2" ).GetData() ) ));
		}
	}

	[TestMethod]
	public void EveryQueueMeshNodeIsDrapedAndEveryFacePointsIntoTheFaceNormalBlock()
	{
		var (models, nodes, meshes, faces) = (0, 0, 0, 0);
		foreach ( var (theme, name, model) in ShippedQueueModels() )
		{
			models++;
			foreach ( var node in model.Nodes )
			{
				// [BIN:TPW-EXE:0x00461F10 node flags] the loader's drape flag ORs 0x20000 into every mesh node; the node flag 0x200 that would take a node out of the walk is never set by the files (types 0x1 and 0x401)
				Assert.AreEqual( 0u, node.Type & 0x200, $"{theme} {name} node {node.Name}" );
				nodes++;
			}
			foreach ( var mesh in model.Meshes )
			{
				meshes++;
				foreach ( var word in mesh.FaceNormalIndices )
				{
					// [BIN:TPW-EXE:0x00473A40 face normals] the refresh after the drape rewrites only the entry a face's first word points to; every queue face points into the face-normal block, so the stored corner normals light the draped model as they do the original
					var index = word & 0x7FFF;
					Assert.IsTrue( index >= mesh.CornerNormals.Length && index < mesh.CornerNormals.Length + mesh.FaceNormals.Length, $"{theme} {name} {mesh.Name}: face normal index {index}" );
					faces++;
				}
			}
		}
		Assert.AreEqual( "28 74 74 1536", $"{models} {nodes} {meshes} {faces}", "models (7 per theme), nodes, meshes and faces of the four themes' queue archives" );
	}

	[TestMethod]
	public void EveryQueueVertexStandsOnTheHillsideItsModelHeightAbove()
	{
		var field = Hillside( 14 );
		var (vertices, lifted) = (0, 0);
		using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		foreach ( var (theme, name, model) in ShippedQueueModels() )
		{
			foreach ( var (x, y, yaw) in new[] { (3, 4, 0), (5, 6, 90), (7, 8, 180), (9, 3, 270) } )
			{
				var baseHeight = QueueModelDrape.BaseHeight( field, x, y );
				var chains = QueueModelDrape.Chains( model, QueueModelDrape.Placement( field, model, x, y, yaw, baseHeight ) );
				var flatChains = QueueModelDrape.Chains( model, QueueModelDrape.Placement( field, model, x, y, yaw, 0 ) );
				foreach ( var mesh in model.Meshes )
				{
					foreach ( var vertex in mesh.Vertices )
					{
						var source = vertex.Position.GetSystemVector3();
						var placed = QueueModelDrape.Vertex( field, chains[mesh.NodeIndex], source, baseHeight );
						// the model's own height in the grid's frame (the chain's, without the instance height)
						var modelHeight = ModelDrape.Transform( flatChains[mesh.NodeIndex], source );
						var landscape = LandscapeSampler.SampleDrape( field.CellCountX, field.CellCountZ, 10, 10, field.GetCornerHeight, field.SplitsAlongAntiDiagonal, placed.X, placed.Z );
						Assert.AreEqual( landscape + modelHeight.Y, placed.Y, 2e-3f, $"{theme} {name} {mesh.Name} at ({x}, {y}) yaw {yaw}" );
						// on its cell, within a model's authored reach beyond the edge (up to 2.5 units), whatever the root node's own turn (the Space bends carry a quarter turn there)
						Assert.IsTrue( placed.X > x * 10 - 2.5f && placed.X < (x + 1) * 10 + 2.5f && placed.Z > y * 10 - 2.5f && placed.Z < (y + 1) * 10 + 2.5f, $"{theme} {name} {mesh.Name} at ({x}, {y}) yaw {yaw}: ({placed.X}, {placed.Z})" );
						Assert.AreEqual( modelHeight.X, placed.X, 1e-4f );
						Assert.AreEqual( modelHeight.Z, placed.Z, 1e-4f );
						vertices++;
						lifted += modelHeight.Y > 0.01f ? 1 : 0;
						hash.AppendData( BitConverter.GetBytes( placed.X ) );
						hash.AppendData( BitConverter.GetBytes( placed.Y ) );
						hash.AppendData( BitConverter.GetBytes( placed.Z ) );
					}
				}
			}
		}
		Assert.IsTrue( vertices > 10000 && lifted > 1000, $"{vertices} vertices, {lifted} above their floor" );
		// a regression pin of the draped positions (the models of the four themes at four cells and yaws on the integer hillside), not original data
		Assert.AreEqual( "9D67F8307D2913F590CB87067B2BA716F77F1617B8CB4E4B3F043AF04296546F", Convert.ToHexString( hash.GetHashAndReset() ) );
	}
}
