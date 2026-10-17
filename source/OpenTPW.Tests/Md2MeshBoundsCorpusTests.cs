using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The stored mesh boxes the original's per-mesh view test reads (tp.exe 0x57A530 → 0x561A30: the mesh record's +0x78 box, padded by half its extent on every side)
/// against the vertices of the mesh they bound. Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
public class Md2MeshBoundsCorpusTests
{
	[TestMethod]
	public void StoredMeshBoxesBracketTheirVerticesInTheOriginalCorpus()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		int meshes = 0, bracketed = 0;
		var outside = new List<string>();
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (name, file) in Md2CorpusTests.Md2Members( archive.Root, "" ) )
			{
				var data = file.GetData();
				file.Free();
				ModelFile model;
				try
				{
					model = new ModelFile( new MemoryStream( data ) );
				}
				catch ( NotSupportedException )
				{
					continue;
				}
				if ( model.Kind == ModelFileKind.Animation )
					continue;
				foreach ( var mesh in model.Meshes.Where( mesh => mesh.Vertices.Length > 0 ) )
				{
					meshes++;
					var lower = mesh.BoundsMin;
					var upper = mesh.BoundsMax;
					var inside = mesh.Vertices.All( vertex =>
					{
						var position = vertex.Position.GetSystemVector3();
						return position.X >= lower.X - 1e-3f && position.Y >= lower.Y - 1e-3f && position.Z >= lower.Z - 1e-3f
							&& position.X <= upper.X + 1e-3f && position.Y <= upper.Y + 1e-3f && position.Z <= upper.Z + 1e-3f;
					} );
					if ( inside )
						bracketed++;
					else
						outside.Add( $"{Path.GetRelativePath( dataPath, wad )}/{name}: node {mesh.NodeIndex}" );
				}
			}
		}
		Console.WriteLine( $"MD2 mesh boxes: {bracketed} of {meshes} meshes have every vertex inside the stored box" );
		Assert.AreEqual( 0, outside.Count, string.Join( Environment.NewLine, outside.Take( 20 ) ) );
	}
}
