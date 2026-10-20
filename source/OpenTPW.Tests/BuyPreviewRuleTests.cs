using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>The buy window's turning preview (tp.exe 0x4689f0 fit, 0x468e50 turn and tilt).</summary>
[TestClass]
public class BuyPreviewRuleTests
{
	[TestMethod]
	public void TheTurnAndTiltAreTheOriginals()
	{
		Assert.AreEqual( 0.4f, PreviewIcon.BuyTurnRate, "0.0004 rad a millisecond" );
		Assert.AreEqual( MathF.PI / 4, PreviewIcon.BuyTilt );
	}

	[TestMethod]
	public void TheFitTakesTheSmallerOfTheFootprintAndHeightTerms()
	{
		// a 4 x 4 footprint (half-diagonal 2.83) and 3 high: the width term 360 / 2 / 2.83 = 63.6, the height term 360 / |(4, 3, 4)| = 57.1
		var min = new NVector3( -2, 0, -2 );
		var max = new NVector3( 2, 3, 2 );
		Assert.AreEqual( 360f / MathF.Sqrt( 41 ), PreviewIcon.BuyScale( 360, 360, min, max ), 1e-3 );
		// a wide frame leaves the height term; a flat model in a narrow frame takes the width term
		Assert.AreEqual( 360f / 2 / MathF.Sqrt( 8 ), PreviewIcon.BuyScale( 360, 1000, min, new NVector3( 2, 0.1f, 2 ) ), 1e-3 );
	}

	/// <summary>
	/// The preview's corners against the original's steps done as it does them: the root frame's matrix scaled by the fit (0x46fad0), turned in place by the
	/// row operations of 0x46f420 and 0x46f240, the offsets added and the columns scaled (0x468e50), then the instance's view diag(1, -1, 1, 1) (0x57a260) and
	/// the viewport (0x579690 mode 2), on the full 2048 x 1536 canvas, with the frame passed in normalized units as 0x486410 passes it.
	/// </summary>
	[TestMethod]
	public void TheCornersLandWhereTheOriginalsMatrixViewAndViewportPutThem()
	{
		// the test quad's mesh hangs off a root node with a translation, so the root's unscaled translation is covered too
		var model = new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
		var icon = new PreviewIcon( model, _ => null );
		var mesh = model.Meshes.Single();
		var root = model.Nodes.Select( ( node, index ) => (node, index) ).Single( pair => pair.node.ParentIndex < 0 ).node.Transform;
		var (min, max) = icon.Bounds;
		var frame = new UiRect( 437, 202, 360, 363 );
		// 0x486410: left x, bottom y, width, height in normalized units
		float left = (frame.X - 1024) / 1024f, bottom = (768 - frame.Bottom) / 768f, width = frame.Width / 1024f, height = frame.Height / 768f;
		// 0x4689f0: the fit and the kept extents
		float dx = max.X - min.X, dz = max.Z - min.Z;
		var halfDiagonal = MathF.Sqrt( dz * 0.5f * dz * 0.5f + dx * 0.5f * dx * 0.5f );
		var reach = (max - new NVector3( (max.X + min.X) * 0.5f, 0, (max.Z + min.Z) * 0.5f )).Length();
		var corner = (max - new NVector3( min.X, 0, min.Z )).Length();
		var fit = Math.Min( width * 0.6666667f / halfDiagonal, height / corner );
		var share = reach / corner / (reach / corner + halfDiagonal / corner);
		var baseMatrix = new float[16];
		for ( var index = 0; index < 16; index++ )
			baseMatrix[index] = root[index / 4, index % 4];
		for ( var index = 0; index < 11; index++ )
			if ( index % 4 != 3 )
				baseMatrix[index] *= fit;
		float hw = fit * dx * 0.5f, hd = fit * dz * 0.5f, reached = fit * reach;
		foreach ( var yaw in new[] { 0f, 0.7f, 2f, 4.4f } )
		{
			var m = (float[])baseMatrix.Clone();
			float sin = OriginalSineTable.Sin( yaw ), cos = OriginalSineTable.Cos( yaw );
			for ( var row = 0; row < 16; row += 4 )
			{
				// 0x46f420( m, cos, sin )
				var x = m[row];
				m[row] = m[row + 2] * sin + x * cos;
				m[row + 2] = m[row + 2] * cos - x * sin;
			}
			float c = OriginalSineTable.Sin( 0.7853982f ), s = OriginalSineTable.Sin( -0.7853982f );
			for ( var row = 0; row < 16; row += 4 )
			{
				// 0x46f240( m, c, s )
				var y = m[row + 1];
				m[row + 1] = y * c - m[row + 2] * s;
				m[row + 2] = m[row + 2] * c + y * s;
			}
			m[12] = (left - ((sin * -hd + cos * -hw) * -0.75f + width * -0.5f)) + m[12];
			m[13] = (((bottom - height * -0.5f) - share * reached * 0.35f) - (cos * -hd - -hw * sin) * -0.7f) + m[13];
			foreach ( var index in new[] { 2, 6, 10, 14 } )
				m[index] *= 0.1f;
			foreach ( var index in new[] { 0, 4, 8 } )
				m[index] *= 0.75f;
			var arranged = icon.ArrangeBuyPreview( frame, yaw, 1 );
			var world = OriginalTerrain.GetWorldMatrix( model, mesh.NodeIndex );
			NMatrix4x4.Invert( root, out var fromRoot );
			for ( var face = 0; face < arranged.Triangles.Count; face++ )
			{
				var triangle = arranged.Triangles[face];
				foreach ( var (vertex, actual) in new[] { ((int)mesh.Indices[face * 3], triangle.A), ((int)mesh.Indices[face * 3 + 1], triangle.B), ((int)mesh.Indices[face * 3 + 2], triangle.C) } )
				{
					// the corner in the root frame's space, then through the frame's matrix, the view and the viewport
					var local = NVector3.Transform( NVector3.Transform( mesh.Vertices[vertex].Position.GetSystemVector3(), world ), fromRoot );
					var x = local.X * m[0] + local.Y * m[4] + local.Z * m[8] + m[12];
					var y = -(local.X * m[1] + local.Y * m[5] + local.Z * m[9] + m[13]);
					var z = (local.X * m[2] + local.Y * m[6] + local.Z * m[10] + m[14]) * 0.1f;
					Assert.AreEqual( x * 1024 + 1024, actual.X, 0.05, $"yaw {yaw}: x" );
					Assert.AreEqual( y * 768 + 768, actual.Y, 0.05, $"yaw {yaw}: y" );
					Assert.AreEqual( z, actual.Z, 1e-5, $"yaw {yaw}: depth" );
				}
			}
		}
	}

	[TestMethod]
	public void TheBuyClipIsAnimationFiveVariantZeroOfThePreviewModel()
	{
		// the letter table's id 5 is m: <model>m1 before the unnumbered <model>m
		Assert.AreEqual( "/r/Ptotemm1.md2", PreviewIcon.BuyClipMember( "/r/Ptotem.md2", new[] { "/r/Ptotem.md2", "/r/Ptotemm.md2", "/r/Ptotemm1.md2", "/r/Ptotemc1.md2" } )?.Path );
		Assert.AreEqual( "/r/Ptotemm.md2", PreviewIcon.BuyClipMember( "/r/Ptotem.md2", new[] { "/r/Ptotem.md2", "/r/Ptotemm.md2" } )?.Path );
		Assert.IsNull( PreviewIcon.BuyClipMember( "/r/Ptotem.md2", new[] { "/r/Ptotem.md2", "/r/Ptotemm2.md2", "/r/totemm1.md2" } ) );
	}
}
