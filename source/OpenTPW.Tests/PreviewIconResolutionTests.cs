using System;
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

	/// <summary>
	/// The object window's preview of <paramref name="icon"/> at <paramref name="yaw"/> from the quad's front: one depth-tested image, drawn as one quad of the
	/// batch. The quad faces +Z and the preview looks from the -Z side, so it is turned half round.
	/// </summary>
	private static UiDepthImage Drawn( PreviewIcon icon, float yaw )
	{
		var batch = new UiBatch();
		icon.Draw( batch, new UiRect( 0, 0, 100, 100 ), yaw + MathF.PI );
		var image = batch.DepthImages.Single();
		Assert.AreSame( image, batch.Draws.Single().Texture.DepthImage );
		Assert.AreEqual( 6, batch.Draws.Single().Vertices.Count );
		return image;
	}

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
			// the object window's view sees the quad's front at these turns; its image holds both triangles with the one texture
			var image = Drawn( icon, yaw );
			Assert.AreEqual( 6, image.Draws.Sum( draw => draw.Vertices.Count ) );
			Assert.IsTrue( image.Draws.All( draw => draw.Texture.ImagePath == "image/tex_a" ) );
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
			var image = Drawn( icon, yaw );
			CollectionAssert.AreEquivalent( new[] { "image/tex_a", "image/tex_b" }, image.Draws.Select( draw => draw.Texture.ImagePath ).ToArray() );
			Assert.IsTrue( image.Draws.All( draw => draw.Vertices.Count == 3 ) );
		}
		Assert.AreEqual( 2, calls.Count );
	}

	[TestMethod]
	public void TheFaceTestKeepsTheTrianglesThatRunClockwiseOnTheScreenAndTheImageHoldsOnlyThose()
	{
		// The quad (a wall in the XY plane facing +Z) again with its two triangles wound the other way: the same corners, facing -Z. The original keeps a
		// triangle only when its corners run clockwise on the screen (tp.exe 0x581320 in the primitive builder 0x580530); the preview's camera looks from
		// the -Z side (0x468e50 tilts by -pi/4), so unturned the reversed pair is kept, and turned half round the first pair is.
		var model = Quad();
		var mesh = model.Meshes.Single();
		var front = mesh.Indices.ToArray();
		var back = Enumerable.Range( 0, front.Length / 3 ).SelectMany( face => new[] { front[face * 3], front[face * 3 + 2], front[face * 3 + 1] } );
		mesh.Indices = front.Concat( back ).ToArray();
		var icon = new PreviewIcon( model, _ => "image/quad" );
		Assert.AreEqual( 4, icon.TriangleCount );
		foreach ( var (yaw, kept) in new[] { (0f, new[] { 2, 3 }), (0.5f, new[] { 2, 3 }), (1f, new[] { 2, 3 }), (2.5f, new[] { 0, 1 }), (3f, new[] { 0, 1 }), (MathF.PI, new[] { 0, 1 }) } )
		{
			var arranged = icon.ArrangeBuyPreview( new UiRect( 0, 0, 100, 100 ), yaw, 0.5f );
			Assert.AreEqual( 4, arranged.Triangles.Count, $"yaw {yaw}" );
			CollectionAssert.AreEquivalent( kept, arranged.Triangles.Where( triangle => triangle.FacesViewer ).Select( triangle => triangle.Index ).ToArray(), $"yaw {yaw}" );
			foreach ( var triangle in arranged.Triangles )
			{
				var clockwise = (triangle.B.X - triangle.A.X) * (triangle.C.Y - triangle.A.Y) - (triangle.C.X - triangle.A.X) * (triangle.B.Y - triangle.A.Y) > 0;
				Assert.AreEqual( clockwise, triangle.FacesViewer, $"yaw {yaw}, triangle {triangle.Index}" );
			}
			var batch = new UiBatch();
			icon.DrawBuyPreview( batch, new UiRect( 0, 0, 100, 100 ), yaw, 0.5f );
			Assert.AreEqual( 2, batch.DepthImages.Single().TriangleCount, $"yaw {yaw}: the image holds the kept pair only" );
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
		var image = Drawn( icon, 0 );
		Assert.AreSame( texture, image.Draws.Single().Texture );
		Assert.AreEqual( 6, image.Draws.Single().Vertices.Count );
	}

	[TestMethod]
	public void MissingMaterialIsResolvedOnceAndDrawsWithSolidFallback()
	{
		var calls = 0;
		var icon = new PreviewIcon( Quad(), _ => { calls++; return null; } );
		Assert.AreEqual( 1, calls );
		Assert.AreSame( UiTexture.Solid, Drawn( icon, 0 ).Draws.Single().Texture );
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

	[TestMethod]
	public void TheImageKeepsTheDepthOrderInsideTheDepthRangeAndSitsOnWholePixels()
	{
		var icon = new PreviewIcon( Quad(), _ => "image/quad" );
		var arranged = icon.ArrangeBuyPreview( new UiRect( 10.3f, 20.6f, 100, 100 ), MathF.PI, 0.5f );
		var image = icon.ToDepthImage( arranged );
		Assert.AreEqual( MathF.Floor( image.Rect.X ), image.Rect.X );
		Assert.AreEqual( MathF.Floor( image.Rect.Y ), image.Rect.Y );
		Assert.AreEqual( (image.Width, image.Height), ((int)image.Rect.Width, (int)image.Rect.Height) );
		var kept = arranged.Triangles.Where( triangle => triangle.FacesViewer ).ToArray();
		var corners = kept.SelectMany( triangle => new[] { triangle.A, triangle.B, triangle.C } ).ToArray();
		var vertices = image.Draws.SelectMany( draw => draw.Vertices ).ToArray();
		Assert.AreEqual( corners.Length, vertices.Length );
		for ( var index = 0; index < corners.Length; index++ )
		{
			Assert.AreEqual( corners[index].X - image.Rect.X, vertices[index].Position.X, 1e-4 );
			Assert.AreEqual( corners[index].Y - image.Rect.Y, vertices[index].Position.Y, 1e-4 );
			Assert.IsTrue( vertices[index].Position.Z is > 0 and < 1 );
			for ( var other = 0; other < corners.Length; other++ )
				if ( corners[index].Z < corners[other].Z )
					Assert.IsTrue( vertices[index].Position.Z < vertices[other].Position.Z, "the depth order is kept" );
		}
		Assert.IsTrue( vertices.All( vertex => vertex.Position.X >= 0 && vertex.Position.X <= image.Width && vertex.Position.Y >= 0 && vertex.Position.Y <= image.Height ) );
	}
}
