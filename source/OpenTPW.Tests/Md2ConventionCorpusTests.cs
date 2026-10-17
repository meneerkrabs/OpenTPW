using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SVector3 = System.Numerics.Vector3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The evidence for the converter's handedness and texture-V conventions (docs/CONTENT-KIT.md, "Converter conventions"), from the original models. Needs OPENTPW_GAME_PATH.
/// <para>
/// The sign faces of the shipped objects carry the runtime-written texts <c>sign1</c> and <c>sign2</c>, whose pixels are an ordinary left-to-right, top-to-bottom bitmap.
/// Seen from the front of a face, in the engine's right-handed camera frame (x right, y away, z up; <see cref="ObjectAssets.ConvertAxes"/> of the model position, then the node matrix as
/// <see cref="ObjectPlacement.SwapAxes"/> gives it), the text must read left to right and upright. This checks, per triangle, that the texture's U axis runs to the viewer's right
/// and its V axis up, where the viewer stands on the side that Veldrid's <c>FrontFace.Clockwise</c> with back-face culling draws.
/// </para>
/// </summary>
[TestClass]
public class Md2ConventionCorpusTests
{
	[TestMethod]
	public void OriginalSignFacesReadLeftToRightAndUprightInTheEngineFrame()
	{
		var data = Md2CorpusTests.OriginalDataPath();
		int models = 0, meshes = 0, meshesWithAFrontFace = 0, triangles = 0, uRight = 0, vUp = 0, both = 0;
		foreach ( var wad in Directory.EnumerateFiles( data, "*.wad", SearchOption.AllDirectories ) )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (_, file) in Md2CorpusTests.Md2Members( archive.Root, "" ) )
			{
				ModelFile model;
				try
				{
					model = new ModelFile( new MemoryStream( file.GetData() ) );
				}
				catch ( NotSupportedException )
				{
					continue;
				}
				finally
				{
					file.Free();
				}
				if ( model.Kind != ModelFileKind.Geometry )
					continue;
				var any = false;
				foreach ( var mesh in model.Meshes.Where( mesh => mesh.NodeIndex >= 0 ) )
				{
					var world = Matrix4x4.Identity;
					for ( var node = mesh.NodeIndex; node >= 0; node = model.Nodes[node].ParentIndex )
						world *= model.Nodes[node].Transform;
					int meshFront = 0, meshOther = 0;
					for ( var face = 0; face < mesh.Indices.Length / 3; face++ )
					{
						var corners = Enumerable.Range( 0, 3 ).Select( corner => (int)mesh.Indices[face * 3 + corner] ).ToArray();
						if ( !corners.All( corner => mesh.Materials[(int)mesh.Vertices[corner].TextureIndex].Name is "sign1" or "sign2" ) )
							continue;
						var engine = corners.Select( corner =>
						{
							var position = SVector3.Transform( mesh.Vertices[corner].Position.GetSystemVector3(), world );
							return new SVector3( position.X, position.Z, position.Y );
						} ).ToArray();
						var uv = corners.Select( corner => mesh.TexCoords[corner] ).ToArray();
						var d1 = engine[1] - engine[0];
						var d2 = engine[2] - engine[0];
						float du1 = uv[1].X - uv[0].X, dv1 = uv[1].Y - uv[0].Y, du2 = uv[2].X - uv[0].X, dv2 = uv[2].Y - uv[0].Y;
						var determinant = du1 * dv2 - du2 * dv1;
						var away = SVector3.Cross( d1, d2 );
						var right = SVector3.Cross( away, SVector3.UnitZ );
						if ( MathF.Abs( determinant ) < 1e-6f || away.LengthSquared() < 1e-8f || right.LengthSquared() < 1e-6f )
							continue;
						away = SVector3.Normalize( away );
						right = SVector3.Normalize( right );
						var up = SVector3.Cross( right, away );
						var alongU = (d1 * dv2 - d2 * dv1) / determinant;
						var alongV = (d2 * du1 - d1 * du2) / determinant;
						var u = SVector3.Dot( alongU, right ) > 0;
						var v = SVector3.Dot( alongV, up ) > 0;
						triangles++;
						uRight += u ? 1 : 0;
						vUp += v ? 1 : 0;
						if ( u && v )
						{
							both++;
							meshFront++;
						}
						else
							meshOther++;
					}
					if ( meshFront + meshOther > 0 )
					{
						meshes++;
						any = true;
						meshesWithAFrontFace += meshFront > 0 ? 1 : 0;
					}
				}
				models += any ? 1 : 0;
			}
		}
		Console.WriteLine( $"sign faces: {models} models, {meshes} meshes ({meshesWithAFrontFace} with a face that reads correctly), {triangles} triangles: U to the right {uRight}, V up {vUp}, both {both}" );
		Assert.IsTrue( models > 100 && meshes > 100, "the corpus has 120 models with sign faces" );
		// every sign mesh is a front face plus (usually) a back-facing duplicate whose text reads mirrored from behind: nearly every mesh has a face that reads correctly,
		// and V is up on all but a handful of triangles. A mirrored or flipped convention would fail for every front face.
		Assert.IsTrue( meshesWithAFrontFace >= meshes * 0.97, $"{meshesWithAFrontFace} of {meshes} sign meshes have a correctly reading face" );
		Assert.IsTrue( vUp >= triangles * 0.98, $"V runs up on {vUp} of {triangles} sign triangles" );
		Assert.IsTrue( both >= triangles * 0.7, $"{both} of {triangles}" );
	}
}
