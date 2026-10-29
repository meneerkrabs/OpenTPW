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

	/// <summary>The lobby recolours the whole grid each frame: an invalidated staging builds the clouds again with the new colour.</summary>
	[TestMethod]
	public void AnInvalidatedStagingTakesTheNewGridColour()
	{
		var cloudPositions = Enumerable.Range( 0, Sky.GridSize * Sky.GridSize ).Select( index => Sky.ToEngine( Sky.CloudPosition( index / Sky.GridSize, index % Sky.GridSize ) ) ).ToArray();
		var cylinderPositions = Enumerable.Range( 0, Sky.CylinderColumns * Sky.CylinderRings ).Select( index => Sky.ToEngine( Sky.CylinderPosition( index / Sky.CylinderRings, index % Sky.CylinderRings ) ) ).ToArray();
		var colours = Enumerable.Repeat( 0xFF102030u, Sky.GridSize * Sky.GridSize ).ToArray();
		var staging = new Sky.Staging( cloudPositions, colours, Sky.CloudTriangles().ToArray(), cylinderPositions, Sky.CylinderTriangles().ToArray() );
		var offsets = new float[Sky.MaximumLayers];
		var before = staging.Update( 1, offsets, offsets, out _ )[0].Normal;
		Array.Fill( colours, 0xFF405060u );
		Assert.AreEqual( before, staging.Update( 1, offsets, offsets, out _ )[0].Normal, "the staged vertices keep the old tint until invalidated" );
		staging.Invalidate();
		var after = staging.Update( 1, offsets, offsets, out _ )[0].Normal;
		Assert.AreNotEqual( before, after );
		Assert.AreEqual( 0x40, (int)after.X );
	}

	// ---- the sky objects (the moons, UI-107) ----

	/// <summary>The view that turns engine axes (Z up) into the original's (Y up): the clip matrix the original's identity camera would have.</summary>
	private static readonly System.Numerics.Matrix4x4 SwapYZ = new( 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1 );

	private static System.Numerics.Vector2[] NdcOf( SVector3[] corners, System.Numerics.Matrix4x4 clipMatrix ) =>
		corners.Select( corner => System.Numerics.Vector4.Transform( new System.Numerics.Vector4( corner, 1f ), clipMatrix ) ).Select( clip => new System.Numerics.Vector2( clip.X / clip.W, clip.Y / clip.W ) ).ToArray();

	[TestMethod]
	public void AMoonQuadIsTheOriginalsClipSpaceSquareTurnedByTheRoll()
	{
		// goldens from tp.exe's own 0x5860D0 and 0x591430 run in the oracle on the identity clip matrix with the aspect word 1 (docs/reverse/ORACLE.md): the hallow moon (2, 11, 15), size 5, centre (2, 11), half size 2.5;
		// the corner offsets from the centre, top left, top right, bottom right, bottom left, for the rolls 0, 0.125, 0.25 and -0.125 turns
		const float d = 3.5355339f;
		var golden = new (float Roll, (float X, float Y)[] Offsets)[]
		{
			(0f, new[] { (-2.5f, 2.5f), (2.5f, 2.5f), (2.5f, -2.5f), (-2.5f, -2.5f) }),
			(0.125f, new[] { (0f, d), (d, 0f), (0f, -d), (-d, 0f) }),
			(0.25f, new[] { (2.5f, 2.5f), (2.5f, -2.5f), (-2.5f, -2.5f), (-2.5f, 2.5f) }),
			(-0.125f, new[] { (-d, 0f), (0f, d), (d, 0f), (0f, -d) }),
		};
		foreach ( var (roll, offsets) in golden )
		{
			Assert.IsTrue( Sky.TryMoonQuad( new SVector3( 2f, 11f, 15f ), 5f, SwapYZ, System.Numerics.Matrix4x4.Identity, roll, out var corners ) );
			var ndc = NdcOf( corners, SwapYZ );
			for ( var index = 0; index < 4; index++ )
			{
				Assert.AreEqual( 2f + offsets[index].X, ndc[index].X, 2e-3f, $"roll {roll} corner {index} x" );
				Assert.AreEqual( 11f + offsets[index].Y, ndc[index].Y, 2e-3f, $"roll {roll} corner {index} y" );
			}
		}
	}

	[TestMethod]
	public void AMoonQuadIsASquareOnTheScreenWhoseSizeFallsWithTheDepth()
	{
		var projection = OriginalProjection.Create( 16f / 9f );
		// the camera looks straight at the hallow moon from the origin
		var direction = Sky.ToEngine( new SVector3( 2f, 11f, 15f ) );
		var view = System.Numerics.Matrix4x4.CreateLookTo( SVector3.Zero, SVector3.Normalize( direction ), SVector3.UnitZ );
		Assert.IsTrue( Sky.TryMoonQuad( new SVector3( 2f, 11f, 15f ), 5f, view, projection, 0f, out var corners ) );
		var ndc = NdcOf( corners, view * projection );
		var depth = SVector3.Dot( direction, SVector3.Normalize( direction ) );
		var half = 2.5f / depth;
		Assert.AreEqual( 0f, (ndc[0].X + ndc[2].X) / 2f, 1e-3f, "a point on the view axis is the screen's centre" );
		Assert.AreEqual( 0f, (ndc[0].Y + ndc[2].Y) / 2f, 1e-3f );
		Assert.AreEqual( 2f * half, ndc[0].Y - ndc[3].Y, 2e-3f, "the half height is the half size over the clip depth w" );
		// the width is the height times height over width: a square in pixels
		Assert.AreEqual( 2f * half * 9f / 16f, ndc[1].X - ndc[0].X, 2e-3f );
		// the same direction farther away or behind
		Assert.IsFalse( Sky.TryMoonQuad( new SVector3( 2f, 11f, 15f ), 5f, System.Numerics.Matrix4x4.CreateLookTo( SVector3.Zero, -SVector3.Normalize( direction ), SVector3.UnitZ ), projection, 0f, out _ ), "behind the camera" );
	}

	[TestMethod]
	public void TheMoonKeepsItsPictureAtAnyDepth()
	{
		var projection = OriginalProjection.Create( 4f / 3f );
		var view = System.Numerics.Matrix4x4.CreateLookTo( SVector3.Zero, new SVector3( 0.3f, 1f, 0.2f ), SVector3.UnitZ );
		var position = new SVector3( 2f, 11f, 15f );
		Assert.IsTrue( Sky.TryMoonQuad( position, 5f, view, projection, 0f, out var near ) );
		var scaled = position * 3f;
		var ndcNear = NdcOf( near, view * projection );
		// a point three times as far has a third of the clip offset ratio: its quad of the same size is a third as large
		Assert.IsTrue( Sky.TryMoonQuad( scaled, 5f, view, projection, 0f, out var far ) );
		var ndcFar = NdcOf( far, view * projection );
		Assert.AreEqual( (ndcNear[1].X - ndcNear[0].X) / 3f, ndcFar[1].X - ndcFar[0].X, 2e-3f );
		Assert.AreEqual( (ndcNear[0].X + ndcNear[2].X) / 2f, (ndcFar[0].X + ndcFar[2].X) / 2f, 1e-3f, "the same direction is the same screen position" );
	}

	[DataTestMethod]
	[DataRow( 0f, 1f, 0f )]
	[DataRow( 0.5f, 0.8660254f, -0.083333336f )]
	[DataRow( -0.5f, 0.8660254f, 0.083333336f )]
	[DataRow( 0f, 0f, 0f )]
	public void TheCameraRollIsTheAngleOfItsUpVectorFromTheWorlds( float right, float up, float turns )
	{
		Assert.AreEqual( turns, Sky.CameraRoll( right, up ), 1e-5f );
	}

	[TestMethod]
	public void TheSpriteRotationReadsTheQuantisedSineTable()
	{
		Assert.AreEqual( (1f, 0f), Sky.RollRotation( 0f ) );
		var (cosine, sine) = Sky.RollRotation( 0.125f );
		Assert.AreEqual( 0.70710677f, cosine, 1e-6f );
		Assert.AreEqual( 0.70710677f, sine, 1e-6f );
		// 256 x roll is truncated: 0.0039 of a turn is one entry, anything below is none
		Assert.AreEqual( Sky.RollRotation( 0f ), Sky.RollRotation( 0.003f ) );
		Assert.AreNotEqual( Sky.RollRotation( 0f ), Sky.RollRotation( 0.004f ) );
		Assert.AreEqual( -0.70710677f, Sky.RollRotation( -0.125f ).Sin, 1e-6f );
	}

	[TestMethod]
	public void SkyObjectsComeFromTheLevelsStandardSamWithTheSharedFileAsDefault()
	{
		var files = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>
		{
			["/levels/Standard.sam"] = new() { ["NumSkyObjects.n"] = "0", ["SkyObjects[0].Size"] = "3.0", ["SkyObjects[0].FrontFlag"] = "0" },
			["/levels/space/Standard.sam"] = new()
			{
				["NumSkyObjects.n"] = "3",
				["SkyObjects[0].X"] = "1.0", ["SkyObjects[0].Y"] = "12.0", ["SkyObjects[0].Z"] = "13.0", ["SkyObjects[0].FrontFlag"] = "1", ["SkyObjects[0].Filename"] = "moon.tga",
				["SkyObjects[1].X"] = "15.0", ["SkyObjects[1].Y"] = "13.0", ["SkyObjects[1].Z"] = "1.0", ["SkyObjects[1].FrontFlag"] = "1", ["SkyObjects[1].Filename"] = "moon2.tga",
				["SkyObjects[2].Filename"] = "",
			},
		};
		Func<string, bool> exists = files.ContainsKey;
		Func<string, string, string?> read = ( path, key ) => files[path].TryGetValue( key, out var value ) ? value : null;
		var space = Sky.ReadObjects( "space", exists, read );
		Assert.AreEqual( 2, space.Count, "an object without a file is not added" );
		Assert.AreEqual( new Sky.SkyObject( new SVector3( 1f, 12f, 13f ), 3f, true, "moon.tga" ), space[0], "the size is the shared file's: a key the level lacks takes it" );
		Assert.AreEqual( new SVector3( 15f, 13f, 1f ), space[1].Position );
		Assert.AreEqual( 0f, space[1].Size, "the shared file defines index 0 only" );
		Assert.AreEqual( 0, Sky.ReadObjects( "jungle", exists, read ).Count, "the shared file holds none" );
		Assert.AreEqual( 0, Sky.ReadObjects( "jungle", _ => false, read ).Count );
		// the back objects: a zero FrontFlag
		files["/levels/hallow/Standard.sam"] = new() { ["NumSkyObjects.n"] = "1", ["SkyObjects[0].Filename"] = "moon.tga", ["SkyObjects[0].X"] = "2.0", ["SkyObjects[0].Y"] = "11.0", ["SkyObjects[0].Z"] = "15.0", ["SkyObjects[0].Size"] = "5.0", ["SkyObjects[0].FrontFlag"] = "0" };
		Assert.IsFalse( Sky.ReadObjects( "hallow", exists, read )[0].Front );
		// at most sixteen
		files["/levels/many/Standard.sam"] = Enumerable.Range( 0, 20 ).SelectMany( index => new[] { ($"SkyObjects[{index}].Filename", "m.tga") } ).Append( ("NumSkyObjects.n", "20") ).ToDictionary( pair => pair.Item1, pair => pair.Item2 );
		Assert.AreEqual( Sky.MaximumObjects, Sky.ReadObjects( "many", exists, read ).Count );
	}

	[TestMethod]
	public void SkyDetailOneIsTheOnlyValueThatDrawsTheSkyObjects()
	{
		var detail = new OriginalDetailSettings();
		Assert.IsFalse( GraphicsPresets.ToRenderQuality( detail with { SkyDetail = 0 }, 16, 1 ).SkyObjects );
		Assert.IsTrue( GraphicsPresets.ToRenderQuality( detail with { SkyDetail = 1 }, 16, 1 ).SkyObjects );
		Assert.IsFalse( RenderQuality.Legacy.SkyObjects );
	}
}
