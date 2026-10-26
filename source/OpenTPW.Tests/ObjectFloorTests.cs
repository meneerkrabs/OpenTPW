using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The floor of a placed object: the edge conform (tp.exe <c>0x00469940</c> → <c>0x00469A80</c>, both texturing modes) and the skirt's slot flags and texture coordinates on the render parts
/// (<c>0x00581790</c>, <c>0x00467310</c>); docs/OBJECTS.md, "Floors".
/// </summary>
[TestClass]
[DoNotParallelize]
public class ObjectFloorCorpusTests
{
	private static readonly string[] Themes = { "jungle", "fantasy", "hallow", "space" };
	private static readonly int[] Rotations = { 0, 90, 180, 270 };
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the floor corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	/// <summary>The matrix of a node in the instance's tree: its own, then each ancestor's down to the root, then the root placement (0x004702D0 along the path).</summary>
	private static float[] InstanceMatrix( ModelFile model, int node, N.Matrix4x4 root )
	{
		var path = new List<int>();
		for ( var current = node; current >= 0; current = model.Nodes[current].ParentIndex )
			path.Insert( 0, current );
		var matrix = ObjectFloorMath.ToArray( root );
		foreach ( var step in path )
			matrix = ObjectFloorMath.Chain( ObjectFloorMath.ToArray( model.Nodes[step].Transform ), matrix );
		return matrix;
	}

	private static float Slope( int x, int y ) => 3f * x + 2f * y + 1f;

	private static ObjectPlacement PlacementAt( int x, int y, int rotation, float baseModelHeight ) =>
		new( x, y, rotation, N.Vector2.Zero, baseModelHeight * ObjectPlacement.ModelScale );

	[TestMethod]
	public void EveryListedVertexOfEveryTypeLandsOnATerrainCornerAtEveryRotation()
	{
		var processedTotal = 0;
		foreach ( var theme in Themes )
		{
			var field = ObjectSkirtCorpusTests.FieldOf( theme );
			var terrain = HoardingTerrain.Of( field );
			foreach ( var entry in ObjectSkirtCorpusTests.NonFixedTypes( theme ) )
			{
				var model = ObjectSkirtCorpusTests.ModelOf( entry );
				var skirt = ObjectSkirt.Build( entry.Settings, model, terrain )!;
				foreach ( var rotation in Rotations )
				{
					var root = ObjectGroundConform.RootPlacement( PlacementAt( 40, 40, rotation, 12f ), terrain );
					var result = ObjectGroundConform.Apply( model, skirt, terrain, Slope, root );
					var name = $"{theme}/{entry.ArchiveName}@{rotation}";
					Assert.AreEqual( skirt.ConformList.Length, result.Processed.Count, $"{name}: a placement inside the map consumes the whole list" );
					foreach ( var (meshIndex, vertex) in result.Processed )
					{
						var node = model.Meshes[meshIndex].NodeIndex;
						var matrix = InstanceMatrix( model, node, root );
						var world = ObjectFloorMath.TransformPoint( matrix, result.Positions[meshIndex][vertex] );
						var before = ObjectFloorMath.TransformPoint( matrix, model.Meshes[meshIndex].Positions[vertex].GetSystemVector3() );
						var (cx, cz) = ((int)MathF.Round( world.X / 10f ), (int)MathF.Round( world.Z / 10f ));
						// the grid line is taken within 0.01 of a cell (0.1 units) of the vertex placed in the world; a vertex the type load counted by its model-space position (a coordinate below 0 counts as
						// the line) can lie farther off in the world and then keeps its coordinate, as in the original
						Assert.AreEqual( Math.Abs( before.X - MathF.Round( before.X / 10f ) * 10f ) < 0.09f ? cx * 10f : before.X, world.X, 1e-2f, $"{name}: x" );
						Assert.AreEqual( Math.Abs( before.Z - MathF.Round( before.Z / 10f ) * 10f ) < 0.09f ? cz * 10f : before.Z, world.Z, 1e-2f, $"{name}: z" );
						Assert.AreEqual( Slope( cx, cz ), world.Y, 1e-2f, $"{name}: height of the corner" );
						Assert.IsTrue( Math.Abs( world.X - cx * 10f ) < 0.2f && Math.Abs( world.Z - cz * 10f ) < 0.2f, $"{name}: the corner is the nearest" );
					}
					processedTotal += result.Processed.Count;
				}
			}
		}
		Assert.IsTrue( processedTotal > 1000, "the corpus has listed vertices" );
	}

	[TestMethod]
	public void FlatGroundAtTheBaseHeightMovesAVertexByNoMoreThanTheSnap()
	{
		var field = ObjectSkirtCorpusTests.FieldOf( "jungle" );
		var terrain = HoardingTerrain.Of( field );
		foreach ( var entry in ObjectSkirtCorpusTests.NonFixedTypes( "jungle" ) )
		{
			var model = ObjectSkirtCorpusTests.ModelOf( entry );
			var skirt = ObjectSkirt.Build( entry.Settings, model, terrain )!;
			foreach ( var rotation in Rotations )
			{
				var result = ObjectGroundConform.Apply( model, skirt, terrain, ( _, _ ) => 12f, ObjectGroundConform.RootPlacement( PlacementAt( 40, 40, rotation, 12f ), terrain ) );
				foreach ( var (meshIndex, vertex) in result.Processed )
				{
					var stored = model.Meshes[meshIndex].Positions[vertex].GetSystemVector3();
					// at most the snap onto the line (0.1 units) and the height onto the corner (0.01)
					Assert.IsTrue( N.Vector3.Distance( stored, result.Positions[meshIndex][vertex] ) < 0.15f, $"{entry.ArchiveName}@{rotation}: vertex {vertex} of mesh {meshIndex}" );
				}
			}
		}
	}

	[TestMethod]
	public void OnlyAFewGroundNodesAreAnimatedAndTheirVertexTracksOverwriteTheConform()
	{
		var withGround = 0;
		var movedNodes = 0;
		var vertexTracks = new List<string>();
		foreach ( var theme in Themes )
		{
			var terrain = HoardingTerrain.Of( ObjectSkirtCorpusTests.FieldOf( theme ) );
			foreach ( var entry in ObjectSkirtCorpusTests.NonFixedTypes( theme ) )
			{
				var model = ObjectSkirtCorpusTests.ModelOf( entry );
				var scan = ObjectHoardingGeometry.ScanGround( model, ObjectMapGrid.ShapeOf( entry.Settings ), terrain );
				var listed = Enumerable.Range( 0, scan.ListedNodes.Length ).Where( node => scan.ListedNodes[node] ).ToArray();
				if ( listed.Length == 0 )
					continue;
				withGround++;
				var animatedNodes = ObjectRenderParts.FindAnimatedNodes( entry, model );
				var animatedMeshes = ObjectRenderParts.FindVertexAnimatedMeshes( entry, model );
				// a clip may move the node as a whole (space gokarts' floor): the conformed vertices then move with it, as the original's instance vertices do; a vertex track would overwrite them
				foreach ( var node in listed.Where( node => animatedMeshes[model.Nodes[node].MeshIndex] ) )
					vertexTracks.Add( $"{theme}/{entry.ArchiveName}" );
				movedNodes += listed.Count( node => animatedNodes[node] );
			}
		}
		Assert.IsTrue( withGround > 200 );
		Assert.IsTrue( movedNodes < 10, $"{movedNodes} listed nodes are moved by a clip" );
		// a vertex track writes the instance's positions with its own samples; the instance (OriginalObject.DrawnMesh) keeps the conformed copy as the base of every pose without samples
		Assert.AreEqual( "space/gokarts,space/spawheel,space/station,space/wateride", string.Join( ",", vertexTracks ) );
	}

	[TestMethod]
	public void OnlyAFewUvAnimatedMeshesUseASlotWhoseWrapBitsTheSkirtPassClears()
	{
		// a scrolling texture track wants a wrapping slot; the coordinate pass makes a slot clamp when all its faces lie on the floor in an edge cell (0x00467310), in the original too
		var tracks = 0;
		var clamped = new SortedSet<string>();
		foreach ( var theme in Themes )
		{
			var terrain = HoardingTerrain.Of( ObjectSkirtCorpusTests.FieldOf( theme ) );
			foreach ( var entry in ObjectSkirtCorpusTests.NonFixedTypes( theme ) )
			{
				var model = ObjectSkirtCorpusTests.ModelOf( entry );
				var skirt = ObjectSkirt.Build( entry.Settings, model, terrain )!;
				foreach ( var file in entry.Animations )
				{
					var clip = ObjectAssets.LoadModel( entry.FileSystem, file.Path ).Clip;
					foreach ( var track in clip?.Tracks.Where( track => track.UvAnimation != null ) ?? Enumerable.Empty<ModelAnimationTrack>() )
					{
						tracks++;
						var mesh = model.Meshes.FirstOrDefault( candidate => candidate.NodeIndex == track.NodeIndex );
						if ( mesh == null )
							continue;
						foreach ( var material in mesh.Materials.Where( material => material.TextureIndex >= 0 ) )
							if ( skirt.ClampedSlots[material.TextureIndex] )
								clamped.Add( $"{theme}/{entry.ArchiveName}" );
					}
				}
			}
		}
		Assert.IsTrue( tracks > 0, "the corpus has UV tracks" );
		Assert.AreEqual( "space/gokarts,space/wateride", string.Join( ",", clamped ), "the original clamps these two floors' tracks too (the slot words are the type's)" );
	}

	[TestMethod]
	public void TheConformWorksOnACopyAndLeavesTheTypesModelAlone()
	{
		var entry = ObjectSkirtCorpusTests.NonFixedTypes( "jungle" ).Single( candidate => candidate.ArchiveName == "bouncy" );
		var model = ObjectSkirtCorpusTests.ModelOf( entry );
		var terrain = HoardingTerrain.Of( ObjectSkirtCorpusTests.FieldOf( "jungle" ) );
		var skirt = ObjectSkirt.Build( entry.Settings, model, terrain )!;
		var before = model.Meshes.Select( mesh => mesh.Positions.ToArray() ).ToArray();
		var result = ObjectGroundConform.Apply( model, skirt, terrain, Slope, ObjectGroundConform.RootPlacement( PlacementAt( 40, 40, 90, 12f ), terrain ) );
		Assert.IsTrue( result.Positions.Count > 0 );
		for ( var mesh = 0; mesh < before.Length; mesh++ )
			CollectionAssert.AreEqual( before[mesh], model.Meshes[mesh].Positions, $"mesh {mesh} of the shared model" );
		var moved = result.Positions.First();
		Assert.AreEqual( model.Meshes[moved.Key].Positions.Length, moved.Value.Length );
	}

	[TestMethod]
	public void AFootprintBeyondTheLastTerrainCornerEndsTheWalkAndTheRestKeepsItsPositions()
	{
		var entry = ObjectSkirtCorpusTests.NonFixedTypes( "jungle" ).Single( candidate => candidate.ArchiveName == "bouncy" );
		var model = ObjectSkirtCorpusTests.ModelOf( entry );
		var field = ObjectSkirtCorpusTests.FieldOf( "jungle" );
		var terrain = HoardingTerrain.Of( field );
		var skirt = ObjectSkirt.Build( entry.Settings, model, terrain )!;
		// the 3 x 4 floor placed so that its far side lies past the terrain's last corner: the walk of its node ends at the first listed vertex whose corner is past it
		var partial = 0;
		var edgePlacements = new List<(int X, int Y, int Rotation)>();
		foreach ( var rotation in Rotations )
			for ( var offset = 1; offset <= 5; offset++ )
			{
				edgePlacements.Add( (field.CellCountX - offset, 40, rotation) );
				edgePlacements.Add( (40, field.CellCountZ - offset, rotation) );
			}
		foreach ( var (anchorX, anchorY, rotation) in edgePlacements )
		{
			var result = ObjectGroundConform.Apply( model, skirt, terrain, ( x, y ) => Slope( Math.Min( x, field.CellCountX ), Math.Min( y, field.CellCountZ ) ), ObjectGroundConform.RootPlacement( PlacementAt( anchorX, anchorY, rotation, 12f ), terrain ) );
			Assert.IsTrue( result.Processed.Count <= skirt.ConformList.Length );
			if ( result.Processed.Count == skirt.ConformList.Length )
				continue;
			// everything after the vertex that ended the node keeps its stored position
			var taken = result.Processed.Select( pair => pair.Vertex ).ToHashSet();
			foreach ( var (mesh, positions) in result.Positions )
				for ( var vertex = 0; vertex < positions.Length; vertex++ )
					if ( !taken.Contains( vertex ) )
						Assert.AreEqual( model.Meshes[mesh].Positions[vertex].GetSystemVector3(), positions[vertex], $"anchor {anchorX},{anchorY}/{rotation}: vertex {vertex}" );
			partial++;
		}
		Assert.IsTrue( partial > 0, "a footprint beyond the last terrain corner ends the node's walk (no valid placement does: the cell 96 of a 96-cell map is not a cell)" );
	}

	private static (ObjectCatalogEntry Entry, ModelFile Model, ObjectFloor Floor, IReadOnlyList<ObjectRenderPart> Parts) BellyBounce( bool withFloor = true )
	{
		var entry = ObjectSkirtCorpusTests.NonFixedTypes( "jungle" ).Single( candidate => candidate.ArchiveName == "bouncy" );
		var model = ObjectSkirtCorpusTests.ModelOf( entry );
		var field = ObjectSkirtCorpusTests.FieldOf( "jungle" );
		var floor = ObjectFloor.Create( entry, model, PlacementAt( 40, 40, 0, 12f ), field )!;
		return (entry, model, floor, ObjectRenderParts.Build( entry, model, entry.Animations, withFloor ? floor : null ));
	}

	private static TextureAlphaType AlphaOf( ObjectCatalogEntry entry, string name )
	{
		var resolved = ObjectAssets.ResolveTexture( entry, name );
		Assert.IsNotNull( resolved, name );
		using var stream = resolved.Value.FileSystem.OpenRead( resolved.Value.Path );
		return TextureFile.ReadAlphaType( stream );
	}

	/// <summary>The translucent groups of a part, split as the model does (a slot the skirt made opaque is not translucent), as the names of their slots.</summary>
	private static IEnumerable<string> TranslucentSlotNames( ObjectCatalogEntry entry, ObjectRenderPart part )
	{
		var types = part.Textures.Select( name => AlphaOf( entry, name ) ).ToArray();
		var vertices = part.Vertices.ToArray();
		WorldTranslucency.ForceOpaqueSlots( types, vertices );
		return WorldTranslucency.SplitFaces( vertices, part.Indices, slot => WorldTranslucency.IsTranslucent( types[slot] ) ).Translucent.Select( group => part.Textures[group.Slot] );
	}

	[TestMethod]
	public void BellyBouncesSkirtSlotsAreOpaqueAndLeaveNoTranslucentGroupWhereTheyWereOne()
	{
		var (entry, model, floor, parts) = BellyBounce();
		var skirtNames = floor.Skirt.Cells.Select( cell => model.Textures[cell.Entry.Slot].FrameNames[0] ).Select( name => Path.GetFileNameWithoutExtension( name ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		Assert.IsTrue( skirtNames.Contains( "m_grass1" ), "the grass fringe art is a skirt slot" );
		var translucent = parts.SelectMany( part => TranslucentSlotNames( entry, part ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		Assert.IsFalse( translucent.Overlaps( skirtNames ), $"translucent groups left: {string.Join( ",", translucent.Intersect( skirtNames, StringComparer.OrdinalIgnoreCase ) )}" );
		// without the floor the fringe art is translucent: the check above is not vacuous
		var before = BellyBounce( withFloor: false ).Parts.SelectMany( part => TranslucentSlotNames( entry, part ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		Assert.IsTrue( before.Overlaps( skirtNames ), "the unmodified model draws its fringe art translucent" );
	}

	[TestMethod]
	public void TheGroundMeshCarriesTheOpaqueAndClampFlagsOnTheSkirtSlotsAndNormalisedCoordinates()
	{
		var (_, model, floor, parts) = BellyBounce();
		var skirt = floor.Skirt;
		var flagged = parts.SelectMany( part => part.Vertices ).Count( vertex => (vertex.MatFlags & WorldTranslucency.OpaqueSlotFlag) != 0 );
		Assert.IsTrue( flagged > 0 );
		foreach ( var part in parts )
			foreach ( var vertex in part.Vertices.Where( vertex => (vertex.MatFlags & WorldTranslucency.OpaqueSlotFlag) != 0 ) )
				Assert.AreEqual( TextureSlotAddressing.ClampFlag, vertex.MatFlags & TextureSlotAddressing.ClampFlag, "a skirt slot clamps (its wrap bits are cleared)" );
		var ground = model.Meshes[skirt.GroundMesh];
		Assert.AreEqual( ground.TexCoords.Length, skirt.TexCoords!.Length );
		// the groups whose wrap bits the pass cleared lie in the edge cells, and their coordinates are inside 0..1 (the original logs a coordinate outside there)
		for ( var corner = 0; corner < ground.Vertices.Length; corner++ )
		{
			var slot = ground.Materials[(int)ground.Vertices[corner].TextureIndex].TextureIndex;
			if ( slot < 0 || !skirt.ClampedSlots[slot] )
				continue;
			var uv = skirt.TexCoords[corner];
			Assert.IsTrue( uv.X >= -1e-4f && uv.X <= 1 + 1e-4f && uv.Y >= -1e-4f && uv.Y <= 1 + 1e-4f, $"corner {corner}: ({uv.X}, {uv.Y})" );
		}
		// a mesh the skirt does not touch is the model's own
		var other = model.Meshes.Select( ( mesh, index ) => (mesh, index) ).FirstOrDefault( pair => pair.index != skirt.GroundMesh && !floor.Changes( pair.index, pair.mesh ) );
		if ( other.mesh != null )
			Assert.AreSame( other.mesh, floor.Apply( other.index, other.mesh ) );
	}

	[TestMethod]
	public void TheSlotFlagsOfTheModelStayAsReadAndOnlyTheInstancesCopyCarriesTheOpaqueBit()
	{
		var (_, model, floor, _) = BellyBounce();
		var ground = model.Meshes[floor.Skirt.GroundMesh];
		Assert.IsTrue( ground.Materials.All( material => (material.Flags & TextureSlotAddressing.OpaqueSlotBit) == 0 ) );
		var copy = floor.Apply( floor.Skirt.GroundMesh, ground );
		Assert.AreNotSame( ground, copy );
		Assert.IsTrue( copy.Materials.Any( material => (material.Flags & TextureSlotAddressing.OpaqueSlotBit) != 0 ) );
		CollectionAssert.AreEqual( ground.Indices, copy.Indices );
	}
}
