using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The runs of a mesh's materials that <see cref="ObjectAssets.ConvertMeshParts"/> makes parts of (<see cref="ObjectAssets.PartMaterialRanges"/>), the names a co-op join's type steps
/// read ahead: synthetic meshes for what the game data does not show, a mesh split at 16 materials whose runs are used by some faces only, and meshes the parts skip.
/// </summary>
[TestClass]
public class ObjectPartMaterialRangesTests
{
	/// <summary>A mesh of one triangle per entry of <paramref name="faces"/>, each with its three corners on that material, out of <paramref name="materials"/> named m0, m1, ...</summary>
	private static ModelFile.Mesh Mesh( int materials, params int[] faces )
	{
		var vertices = faces.SelectMany( material => Enumerable.Repeat( new ModelFile.Vertex { TextureIndex = (uint)material }, 3 ) ).ToArray();
		return new ModelFile.Mesh
		{
			Name = "synthetic",
			Vertices = vertices,
			Indices = Enumerable.Range( 0, vertices.Length ).Select( index => (uint)index ).ToArray(),
			Normals = new Vector3[vertices.Length],
			TexCoords = new Vector2[vertices.Length],
			Materials = Enumerable.Range( 0, materials ).Select( index => new ModelFile.MaterialData { Name = $"m{index}", TextureIndex = index } ).ToArray()
		};
	}

	private static string[] RangeNames( ModelFile.Mesh mesh ) =>
		ObjectAssets.PartMaterialRanges( mesh ).SelectMany( range => mesh.Materials.Skip( range.First ).Take( range.Count ) ).Select( material => material.Name ).ToArray();

	private static string[] PartNames( ModelFile.Mesh mesh ) => ObjectAssets.ConvertMeshParts( mesh ).SelectMany( part => part.Textures ).ToArray();

	[TestMethod]
	public void TheRunsAreThoseOfTheConvertedParts()
	{
		foreach ( var (mesh, expected, why) in new[]
		{
			(Mesh( 3, 0, 2 ), "m0 m1 m2", "a mesh of at most 16 materials is one part with them all, used or not"),
			(Mesh( 16, 15 ), string.Join( " ", Enumerable.Range( 0, 16 ).Select( index => $"m{index}" ) ), "16 materials are still one part"),
			(Mesh( 20, 17 ), "m16 m17 m18 m19", "a split mesh keeps the runs some face uses wholly: here the second only"),
			(Mesh( 40, 0, 35 ), string.Join( " ", Enumerable.Range( 0, 16 ).Concat( Enumerable.Range( 32, 8 ) ).Select( index => $"m{index}" ) ), "the first and the last run"),
			(Mesh( 20 ), "", "no faces: no part"),
			(Mesh( 0, 0 ), "", "no materials: no part")
		} )
		{
			Assert.AreEqual( expected, string.Join( " ", RangeNames( mesh ) ), why );
			if ( mesh.Materials.Length > 0 && mesh.Vertices.Length > 0 )
				CollectionAssert.AreEqual( PartNames( mesh ), RangeNames( mesh ), $"{why}: the converted parts' names" );
		}
	}

	[TestMethod]
	public void AFaceAcrossTwoRunsBelongsToNeither()
	{
		// one face with corners on materials 3 and 18 (two runs of a split mesh): no run has a whole face, so the mesh makes no part
		var mesh = Mesh( 20, 3 );
		mesh.Vertices[1] = new ModelFile.Vertex { TextureIndex = 18 };
		Assert.AreEqual( 0, ObjectAssets.PartMaterialRanges( mesh ).Count() );
		Assert.AreEqual( 0, ObjectAssets.ConvertMeshParts( mesh ).Count );
	}
}
