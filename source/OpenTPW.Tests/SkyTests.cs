using Veldrid;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>The sky's layout and scroll (docs/UI.md, UI-107), from the numbers tp.exe's sky object builds (0x584EF0, 0x585720, 0x5863C0, 0x585F10).</summary>
[TestClass]
public class SkyTests
{
	[TestMethod]
	public void CloudGridHangsFromTheCentreHeightAndSagsAtAFifthPerUnit()
	{
		var centre = Sky.CloudPosition( 7, 7 );
		Assert.AreEqual( Sky.CentreHeight, centre.Y + MathF.Sqrt( centre.X * centre.X + centre.Z * centre.Z ) * Sky.Sag, 1e-3f );
		var corner = Sky.CloudPosition( 0, 0 );
		Assert.AreEqual( (-1200f, -1200f), (corner.X, corner.Z) );
		Assert.AreEqual( 300f - MathF.Sqrt( 2 ) * 1200f * 0.2f, corner.Y, 1e-3f );
		Assert.AreEqual( 1200f, Sky.CloudPosition( 15, 15 ).X, 1e-3f );
	}

	[TestMethod]
	public void CylinderRingsRunFromTheTopDownThroughTheHorizon()
	{
		Assert.AreEqual( 720f, Sky.CylinderRadius );
		Assert.AreEqual( 156f, Sky.CylinderTop, 1e-3f );
		CollectionAssert.AreEqual( new[] { 156f, 78f, 0f, -156f }, Enumerable.Range( 0, 4 ).Select( ring => Sky.CylinderPosition( 0, ring ).Y ).ToArray() );
		// the radii of the four rings, and the first and last columns closing the circle
		CollectionAssert.AreEqual( new[] { 720f, 705.6f, 691.2f, 604.8f }, Enumerable.Range( 0, 4 ).Select( ring => MathF.Round( Sky.CylinderPosition( 0, ring ).X, 2 ) ).ToArray() );
		var last = Sky.CylinderPosition( 17, 0 );
		Assert.AreEqual( 720f, last.X, 1e-2f );
		Assert.AreEqual( 0f, last.Z, 1e-2f );
		var half = Sky.CylinderPosition( 8, 0 );
		Assert.AreEqual( -720f, half.X, 1e-2f );
		Assert.AreEqual( half.X, Sky.CylinderPosition( 9, 0 ).X, 1e-3f, "the half circles share their seam" );
	}

	[TestMethod]
	public void CylinderCoordinatesStackTheTwoHazeGradients()
	{
		CollectionAssert.AreEqual( new[] { 0.99f, 0.84f, 0.67f, 0.51f }, Enumerable.Range( 0, 4 ).Select( ring => MathF.Round( Sky.CylinderTexCoord( 3, ring ).Y, 2 ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0.49f, 0.34f, 0.17f, 0.01f }, Enumerable.Range( 0, 4 ).Select( ring => MathF.Round( Sky.CylinderTexCoord( 12, ring ).Y, 2 ) ).ToArray() );
		Assert.AreEqual( (0f, 1f, 0f, 1f), (Sky.CylinderTexCoord( 0, 0 ).X, Sky.CylinderTexCoord( 8, 0 ).X, Sky.CylinderTexCoord( 9, 0 ).X, Sky.CylinderTexCoord( 17, 0 ).X) );
	}

	[TestMethod]
	public void TriangleListsCoverEveryCellAndSegment()
	{
		var cloud = Sky.CloudTriangles().ToList();
		Assert.AreEqual( 15 * 15 * 2, cloud.Count, "every cloud cell stays above the bottom ring (the lowest grid corner is at -39)" );
		Assert.IsTrue( cloud.All( t => new[] { t.V0, t.V1, t.V2 }.All( v => v is >= 0 and < 256 ) ) );
		var cylinder = Sky.CylinderTriangles().ToList();
		Assert.AreEqual( 16 * 3 * 2, cylinder.Count );
		Assert.IsTrue( cylinder.All( t => new[] { t.V0, t.V1, t.V2 }.All( v => v is >= 0 and < 72 ) ) );
		CollectionAssert.AreEqual( new[] { 0, 5, 4 }, new[] { cylinder[0].V0, cylinder[0].V1, cylinder[0].V2 } );
	}

	[TestMethod]
	public void LayersStepScrollAndFadeLikeTheOriginals()
	{
		CollectionAssert.AreEqual( new[] { 207, 151, 95, 39 }, Enumerable.Range( 0, 4 ).Select( layer => (int)Sky.LayerAlpha( layer ) ).ToArray() );
		Assert.AreEqual( MathF.PI * 7 / 112f, Sky.LayerStep( 0 ), 1e-6f );
		Assert.AreEqual( MathF.PI * 4 / 112f, Sky.LayerStep( 3 ), 1e-6f );
		Assert.AreEqual( (0.0008f, 0.00009f), (Sky.LayerVelocityU( 0 ), Sky.LayerVelocityV( 0 )) );
		Assert.AreEqual( (0.0023f, 0f), (Sky.LayerVelocityU( 3 ), MathF.Round( Sky.LayerVelocityV( 3 ), 6 )) );
		// the four layers mirror the texture in turn
		Assert.AreEqual( (-2f, 3f), (Sky.LayerTexCoord( 0, 0, 0, 2f, 3f ).X, Sky.LayerTexCoord( 0, 0, 0, 2f, 3f ).Y) );
		Assert.AreEqual( (-2f, -3f), (Sky.LayerTexCoord( 1, 0, 0, 2f, 3f ).X, Sky.LayerTexCoord( 1, 0, 0, 2f, 3f ).Y) );
		Assert.AreEqual( (2f, -3f), (Sky.LayerTexCoord( 2, 0, 0, 2f, 3f ).X, Sky.LayerTexCoord( 2, 0, 0, 2f, 3f ).Y) );
		Assert.AreEqual( (2f, 3f), (Sky.LayerTexCoord( 3, 0, 0, 2f, 3f ).X, Sky.LayerTexCoord( 3, 0, 0, 2f, 3f ).Y) );
		// the row drives u and the column v, with the layer's step
		var step = Sky.LayerStep( 3 );
		Assert.AreEqual( (5 * step, 2 * step), (Sky.LayerTexCoord( 3, 2, 5, 0, 0 ).X, Sky.LayerTexCoord( 3, 2, 5, 0, 0 ).Y) );
	}

	[TestMethod]
	public void ScrollMovesByFrameTimeAndWrapsIntoUnitRange()
	{
		var (u, v) = Sky.Scroll( 0, 0f, 0f, 1f / 60f );
		Assert.AreEqual( 0.0008f * 25f / 60f, u, 1e-7f );
		Assert.AreEqual( 0.00009f * 25f / 60f, v, 1e-7f );
		Assert.AreEqual( (-0.5f, 0.25f), Sky.Scroll( 0, -0.5f, 0.25f, 0f ), "no time, no motion" );
		Assert.AreEqual( (0.125f, 0.5f), Sky.Scroll( 0, 0.125f, 0.5f, 1.5f ), "a frame of a second or more holds the sky still" );
		Assert.AreEqual( (0.125f, 0.5f), Sky.Scroll( 0, 0.125f, 0.5f, float.NaN ) );
		var wrapped = Sky.Scroll( 3, 0.9999f, 0f, 1f );
		Assert.IsTrue( wrapped.U is > -1f and < 1f );
		Assert.AreEqual( 0.9999f + 0.0023f * 25f - 1f, wrapped.U, 1e-4f );
	}

	[TestMethod]
	public void ColourGridSamplesTheCentreOfEachCellOfSkyRgb()
	{
		// a 32 x 32 image whose red channel is the x coordinate and whose green channel is the y coordinate
		var rgba = new byte[32 * 32 * 4];
		for ( var y = 0; y < 32; y++ )
			for ( var x = 0; x < 32; x++ )
				(rgba[(y * 32 + x) * 4], rgba[(y * 32 + x) * 4 + 1], rgba[(y * 32 + x) * 4 + 2]) = ((byte)x, (byte)y, 7);
		var grid = Sky.SampleColourGrid( rgba, 32, 32 );
		// cell (x = 3, y = 5) samples pixel (1 + 3 * 2, 1 + 5 * 2) counted from the bottom row (the file's first), at index x * 16 + y
		Assert.AreEqual( 0xFF000000u | 7u << 16 | (uint)(31 - 11) << 8 | 7u, grid[3 * 16 + 5] );
		Assert.IsTrue( grid.All( colour => colour >> 24 == 0xFF ) );
	}

	[TestMethod]
	public void FogColourReadsTheDirect3DColourOfTheSetting()
	{
		Assert.AreEqual( new RgbaByte( 0x48, 0xD8, 0xF8, 255 ), Sky.FogColour( 4774136 ), "jungle and fantasy" );
		Assert.AreEqual( new RgbaByte( 0x11, 0x22, 0x44, 255 ), Sky.FogColour( 1122884 ), "hallow's night" );
		Assert.AreEqual( new RgbaByte( 0xF8, 0x84, 0x30, 255 ), Sky.FogColour( 16286768 ), "space" );
		var files = new System.Collections.Generic.Dictionary<string, string> { ["/levels/hallow/Standard.sam"] = "1122884", ["/levels/Standard.sam"] = "4774136" };
		Func<string, bool> exists = files.ContainsKey;
		Func<string, string?> read = path => files[path];
		Assert.AreEqual( new RgbaByte( 0x11, 0x22, 0x44, 255 ), Sky.FogColour( "hallow", exists, read ), "the level's own file wins" );
		Assert.AreEqual( new RgbaByte( 0x48, 0xD8, 0xF8, 255 ), Sky.FogColour( "other", exists, read ), "the shared file otherwise" );
		Assert.AreEqual( new RgbaByte( 0x44, 0xDD, 0xFF, 255 ), Sky.FogColour( "other", _ => false, read ), "the front end's fog colour when no file has one" );
	}

	[TestMethod]
	public void SkyQualityZeroIsTheOnlyValueThatDrawsNoSky()
	{
		var detail = new OriginalDetailSettings();
		foreach ( var (setting, layers) in new[] { (0, 0), (1, 1), (2, 2), (4, 4), (3, 4), (9, 4) } )
			Assert.AreEqual( layers, GraphicsPresets.ToRenderQuality( detail with { SkyQuality = setting }, 16, 1 ).SkyLayers, $"SKYQUALITY {setting}" );
		Assert.AreEqual( 4, RenderQuality.Legacy.SkyLayers );
	}

	/// <summary>The staged vertices of a frame equal a full rebuild at the same scroll, for every layer count and across layer-count changes.</summary>
	[TestMethod]
	public void StagedVerticesEqualAFullRebuildForEveryLayerCount()
	{
		var cloudPositions = Enumerable.Range( 0, Sky.GridSize * Sky.GridSize ).Select( index => Sky.ToEngine( Sky.CloudPosition( index / Sky.GridSize, index % Sky.GridSize ) ) ).ToArray();
		var cylinderPositions = Enumerable.Range( 0, Sky.CylinderColumns * Sky.CylinderRings ).Select( index => Sky.ToEngine( Sky.CylinderPosition( index / Sky.CylinderRings, index % Sky.CylinderRings ) ) ).ToArray();
		var colours = Enumerable.Range( 0, Sky.GridSize * Sky.GridSize ).Select( index => 0xFF000000u | (uint)index * 0x010203u ).ToArray();
		var cloudTriangles = Sky.CloudTriangles().ToArray();
		var cylinderTriangles = Sky.CylinderTriangles().ToArray();
		var staging = new Sky.Staging( cloudPositions, colours, cloudTriangles, cylinderPositions, cylinderTriangles );
		var random = new Random( 7 );
		foreach ( var layers in new[] { 1, 2, 4, 2, 4, 1, 4 } )
		{
			for ( var frame = 0; frame < 3; frame++ )
			{
				var offsetU = Enumerable.Range( 0, Sky.MaximumLayers ).Select( _ => (float)(random.NextDouble() * 2 - 1) ).ToArray();
				var offsetV = Enumerable.Range( 0, Sky.MaximumLayers ).Select( _ => (float)(random.NextDouble() * 2 - 1) ).ToArray();
				var staged = staging.Update( layers, offsetU, offsetV, out var clouds );
				var expected = Sky.BuildVertices( layers, cloudPositions, colours, cloudTriangles, cylinderPositions, cylinderTriangles, offsetU, offsetV, out var expectedClouds );
				Assert.AreEqual( expectedClouds, clouds, $"{layers} layers" );
				Assert.AreEqual( layers * cloudTriangles.Length * 3, clouds );
				CollectionAssert.AreEqual( System.Runtime.InteropServices.MemoryMarshal.AsBytes( expected.AsSpan() ).ToArray(),
					System.Runtime.InteropServices.MemoryMarshal.AsBytes( staged.AsSpan() ).ToArray(), $"{layers} layers, frame {frame}" );
			}
		}
	}
}
