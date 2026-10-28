using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The instance mesh sampler (mode 4, tp.exe <c>0x451290</c> over the instance's <c>+0x2C</c>) on the shipped object models (TRACK-001): the mesh is the faces of the ground node the type loader's search
/// (<c>0x46A5A0</c> → <c>0x469A80</c>) chose, not every face of the model.
/// </summary>
[TestClass]
public class TrackGroundNodeSamplingTests
{
	private static readonly string[] Themes = { "jungle", "fantasy", "hallow", "space" };
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the object corpus." );
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

	private static int FaceCount( ModelFile model, int mesh ) =>
		Enumerable.Range( 0, model.Meshes[mesh].FaceNormalIndices.Length ).Count( face => face * 3 + 2 < model.Meshes[mesh].Indices.Length && (model.Meshes[mesh].FaceNormalIndices[face] & 0x8000) != 0 );

	[TestMethod]
	public void TheInstanceMeshOfAShippedObjectIsItsGroundNodesFacesAndNothingWithoutOne()
	{
		var reached = 0;
		var withNode = 0;
		foreach ( var theme in Themes )
		{
			var terrain = HoardingTerrain.Of( ObjectSkirtCorpusTests.FieldOf( theme ) );
			foreach ( var entry in ObjectSkirtCorpusTests.NonFixedTypes( theme ) )
			{
				var model = ObjectSkirtCorpusTests.ModelOf( entry );
				if ( TrackSurface.FromObject( model ).Mode != TrackSamplerMode.InstanceMesh )
					continue;
				reached++;
				var node = ObjectHoardingGeometry.SelectGroundNode( model, ObjectMapGrid.ShapeOf( entry.Settings ), terrain ).Node;
				var surface = TrackSurface.FromObject( model, groundNode: node );
				Assert.AreEqual( TrackSamplerMode.InstanceMesh, surface.Mode, $"{theme}/{entry.ArchiveName}" );
				if ( node < 0 )
				{
					Assert.AreEqual( 0, surface.TriangleCount, $"{theme}/{entry.ArchiveName}: no ground node, no mesh: the probe answers the instance's own height" );
					Assert.AreEqual( 0f, surface.HeightAt( 5, 5 )!.Value, $"{theme}/{entry.ArchiveName}" );
					continue;
				}
				withNode++;
				var mesh = model.Nodes[node].MeshIndex;
				Assert.AreEqual( FaceCount( model, mesh ), surface.TriangleCount, $"{theme}/{entry.ArchiveName}: the ground node's faces" );
				// the shipped object models carry no face with the collision bit 0x8000 (the sampler counts only those), so the probe of every one of them answers the instance's own height
				Assert.AreEqual( 0, surface.TriangleCount, $"{theme}/{entry.ArchiveName}" );
			}
		}
		Assert.IsTrue( reached > 100, "most shipped types are sampled in mode 4" );
		Assert.IsTrue( withNode > 100 );
	}
}
