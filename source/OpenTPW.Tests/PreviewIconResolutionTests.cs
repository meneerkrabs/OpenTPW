using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
public class PreviewIconResolutionTests
{
	private static ModelFile Quad() => new( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );

	[TestMethod]
	public void SharedMaterialIsResolvedOnceAndRetainsEveryTriangle()
	{
		var imageCalls = 0;
		var runtimeCalls = 0;
		var icon = new PreviewIcon( Quad(), name => { imageCalls++; return "image/" + name; },
			name => { runtimeCalls++; return null; } );
		Assert.AreEqual( 1, imageCalls );
		Assert.AreEqual( 1, runtimeCalls );
		Assert.AreEqual( 2, icon.TriangleCount );
		foreach ( var yaw in new[] { 0f, 0.5f, 1f } )
		{
			var batch = new UiBatch();
			icon.Draw( batch, new UiRect( 0, 0, 100, 100 ), yaw );
			Assert.AreEqual( 6, batch.Draws.Sum( draw => draw.Vertices.Count ) );
			Assert.IsTrue( batch.Draws.All( draw => draw.Texture.ImagePath == "image/tex_a" ) );
		}
		Assert.AreEqual( 1, imageCalls, "drawing a cached preview does not resolve materials again" );
	}

	[TestMethod]
	public void DistinctUsedMaterialsKeepTheirOwnTexturesAcrossRotations()
	{
		var model = Quad();
		var mesh = model.Meshes.Single();
		mesh.Materials = mesh.Materials.Append( new ModelFile.MaterialData { Name = "tex_b", TextureIndex = 0 } ).ToArray();
		var vertex = mesh.Vertices[mesh.Indices[3]];
		var uv = mesh.TexCoords[mesh.Indices[3]];
		vertex.TextureIndex = 1;
		mesh.Indices[3] = (uint)mesh.Vertices.Length;
		mesh.Vertices = mesh.Vertices.Append( vertex ).ToArray();
		mesh.TexCoords = mesh.TexCoords.Append( uv ).ToArray();
		var calls = new System.Collections.Generic.List<string>();
		var icon = new PreviewIcon( model, name => { calls.Add( name ); return "image/" + name; } );
		CollectionAssert.AreEquivalent( new[] { "tex_a", "tex_b" }, calls );
		foreach ( var yaw in new[] { 0f, 0.5f, 1f } )
		{
			var batch = new UiBatch();
			icon.Draw( batch, new UiRect( 0, 0, 100, 100 ), yaw );
			CollectionAssert.AreEquivalent( new[] { "image/tex_a", "image/tex_b" }, batch.Draws.Select( draw => draw.Texture.ImagePath ).ToArray() );
			Assert.IsTrue( batch.Draws.All( draw => draw.Vertices.Count == 3 ) );
		}
		Assert.AreEqual( 2, calls.Count );
	}

	[TestMethod]
	public void BothFacesAreDrawnAndTheBackFacesGoFirstSoTheyNeverPaintOverTheFrontFaces()
	{
		// The quad (a wall in the XY plane facing +Z) again with its two triangles wound the other way: the same corners, facing -Z. The original draws both faces
		// (D3DCULL_NONE, tp.exe 0x566957) and depth tests them; the painter-sorted preview draws the pair turned away from the viewer first and the pair facing it
		// over it, so a back face never covers a front face (the winter kiosk's split roof slopes) and a face seen from behind is still drawn. Turned less than a
		// quarter round, the first pair faces the viewer; turned more, the reversed pair does.
		var model = Quad();
		var mesh = model.Meshes.Single();
		var front = mesh.Indices.ToArray();
		var back = Enumerable.Range( 0, front.Length / 3 ).SelectMany( face => new[] { front[face * 3], front[face * 3 + 2], front[face * 3 + 1] } );
		mesh.Indices = front.Concat( back ).ToArray();
		var icon = new PreviewIcon( model, _ => "image/quad" );
		Assert.AreEqual( 4, icon.TriangleCount );
		foreach ( var (yaw, facing) in new[] { (0f, new[] { 0, 1 }), (0.5f, new[] { 0, 1 }), (1f, new[] { 0, 1 }), (2f, new[] { 2, 3 }), (3f, new[] { 2, 3 }) } )
		{
			var drawn = icon.ArrangeBuyPreview( new UiRect( 0, 0, 100, 100 ), yaw );
			Assert.AreEqual( 4, drawn.Count, $"yaw {yaw}: both faces are drawn" );
			CollectionAssert.AreEquivalent( facing, drawn.Skip( 2 ).Select( triangle => triangle.Index ).ToArray(), $"yaw {yaw}: the pair facing the viewer is drawn last" );
			Assert.IsTrue( drawn.Skip( 2 ).All( triangle => triangle.FacesViewer ) && drawn.Take( 2 ).All( triangle => !triangle.FacesViewer ), $"yaw {yaw}" );
			var batch = new UiBatch();
			icon.Draw( batch, new UiRect( 0, 0, 100, 100 ), yaw );
			Assert.AreEqual( 12, batch.Draws.Sum( draw => draw.Vertices.Count ), $"yaw {yaw}" );
		}
	}

	[TestMethod]
	public void RuntimeMaterialKeepsPriorityOverImageResolution()
	{
		var calls = 0;
		var texture = UiTexture.Image( "runtime/sign" );
		var icon = new PreviewIcon( Quad(), _ => { Assert.Fail( "Runtime texture must take priority." ); return null; },
			_ => { calls++; return texture; } );
		Assert.AreEqual( 1, calls );
		var batch = new UiBatch();
		icon.Draw( batch, new UiRect( 0, 0, 100, 100 ), 0 );
		Assert.AreSame( texture, batch.Draws.Single().Texture );
		Assert.AreEqual( 6, batch.Draws.Single().Vertices.Count );
	}

	[TestMethod]
	public void MissingMaterialIsResolvedOnceAndDrawsWithSolidFallback()
	{
		var calls = 0;
		var icon = new PreviewIcon( Quad(), _ => { calls++; return null; } );
		Assert.AreEqual( 1, calls );
		var batch = new UiBatch();
		icon.Draw( batch, new UiRect( 0, 0, 100, 100 ), 0 );
		Assert.AreSame( UiTexture.Solid, batch.Draws.Single().Texture );
	}

	[TestMethod]
	public void UnusedAndUntexturedMaterialsDoNotTriggerLoading()
	{
		var model = Quad();
		var mesh = model.Meshes.Single();
		mesh.Materials = mesh.Materials.Append( new ModelFile.MaterialData { Name = "sign1", TextureIndex = 0 } ).ToArray();
		mesh.Materials[0].TextureIndex = -1;
		var icon = new PreviewIcon( model, _ => { Assert.Fail( "Untextured or unused material must not load an image." ); return null; },
			_ => { Assert.Fail( "An unused sign must not be composed." ); return null; } );
		Assert.AreEqual( 2, icon.TriangleCount );
	}
}
