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
