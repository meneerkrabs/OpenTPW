using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>RIDES-021: which node state bits the shipped object models carry at load (the cursor pick's model walk reads 0x10, 0x20, 0x200 and 0x8000, tp.exe 0x45d090). Inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass]
[DoNotParallelize]
public class ObjectPickNodeBitsCorpusTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;
	public TestContext TestContext { get; set; } = null!;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the node bit census." );
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

	[TestMethod]
	public void NoObjectModelNodeCarriesTheBitsThePickWalkPrunesBy()
	{
		var counts = new SortedDictionary<uint, int>();
		var models = 0; var rootMesh = 0; var rootFaces = 0;
		foreach ( var theme in new[] { "fantasy", "hallow", "jungle", "space" } )
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( x => x.ModelPath != null ) )
			{
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath! );
				models++; var r = model.RootNodeIndex; if ( r >= 0 && model.Nodes[r].MeshIndex >= 0 ) { rootMesh++; if ( model.Meshes[model.Nodes[r].MeshIndex].Indices.Length > 0 ) rootFaces++; }
				foreach ( var node in model.Nodes )
					for ( uint bit = 1; bit != 0; bit <<= 1 )
						if ( (node.Type & bit) != 0 )
							counts[bit] = counts.GetValueOrDefault( bit ) + 1;
			}
		TestContext.WriteLine( $"{models} models, root with mesh {rootMesh}, with faces {rootFaces}: " + string.Join( ", ", counts.Select( x => $"0x{x.Key:X}={x.Value}" ) ) );
		Assert.IsTrue( models >= 270, $"{models} object models" );
		CollectionAssert.AreEquivalent( new uint[] { 0x1, 0x8, 0x200, 0x400 }, counts.Keys.ToArray(), "only these node bits occur in the shipped object models: none of 0x10, 0x20 or 0x8000" );
		// most roots carry a mesh, so the normal refresh of the root node (0x473c30) reaches the faces the pick tests
		Assert.IsTrue( rootFaces >= 240, $"{rootFaces} roots with faces" );
	}
}
