using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The boat's four-probe height over a track surface (docs/TRACKS.md), asset-free.</summary>
[TestClass]
public class TrackSurfaceTests
{
	private static TrackSurface.Triangle Triangle( float x0, float y0, float z0, float x1, float y1, float z1, float x2, float y2, float z2 ) =>
		new( new System.Numerics.Vector3( x0, y0, z0 ), new System.Numerics.Vector3( x1, y1, z1 ), new System.Numerics.Vector3( x2, y2, z2 ) );

	[TestMethod]
	public void HeightIsTheHighestTriangleUnderThePoint()
	{
		var surface = new TrackSurface( new[]
		{
			Triangle( 0, 0, 3, 20, 0, 3, 0, 20, 3 ),
			Triangle( 20, 0, 3, 20, 20, 3, 0, 20, 3 ),
			Triangle( 5, 5, 8, 15, 5, 8, 5, 15, 8 )
		} );
		Assert.AreEqual( 3f, surface.HeightAt( 18, 18 )!.Value, 1e-4f, "the flat water plane" );
		Assert.AreEqual( 8f, surface.HeightAt( 6, 6 )!.Value, 1e-4f, "the higher triangle wins where two overlap" );
		Assert.IsNull( surface.HeightAt( 25, 5 ), "outside every triangle" );
		Assert.IsNull( new TrackSurface( Array.Empty<TrackSurface.Triangle>() ).HeightAt( 1, 1 ) );
		// a sloping face interpolates: z rises 0.1 per unit of x
		var slope = new TrackSurface( new[] { Triangle( 0, 0, 0, 20, 0, 2, 0, 20, 0 ), Triangle( 20, 0, 2, 20, 20, 2, 0, 20, 0 ) } );
		Assert.AreEqual( 1f, slope.HeightAt( 10, 3 )!.Value, 1e-3f );
	}

	[TestMethod]
	public void PoseIsTheProbeMeanWithPitchAndRollFromTheDifferences()
	{
		// level water: height 3, no tilt, whatever the heading
		var level = TrackSurface.Pose( 10, 10, 77, 3.33f, ( x, y ) => 3 );
		Assert.AreEqual( (3f, 0f, 0f), (level.Height, level.Pitch, level.Roll) );
		// a surface rising 0.5 per unit toward +Y: a boat heading +Y (0) pitches up by atan(front − back over 2R), and has no roll
		var rise = TrackSurface.Pose( 10, 10, 0, 2, ( x, y ) => 0.5f * y );
		Assert.AreEqual( 5f, rise.Height, 1e-4f, "the mean of the four probes" );
		Assert.AreEqual( MathF.Atan2( 2f, 4f ), rise.Pitch, 1e-4f, "front 6, back 4 over the probe distance 2R = 4" );
		Assert.AreEqual( 0f, rise.Roll, 1e-4f );
		// the same surface seen from a boat heading +X (128): the rise is across it, so it rolls and does not pitch
		var across = TrackSurface.Pose( 10, 10, 128, 2, ( x, y ) => 0.5f * y );
		Assert.AreEqual( 0f, across.Pitch, 1e-4f );
		Assert.AreEqual( MathF.Atan2( -2f, 4f ), across.Roll, 1e-4f, "heading +X puts +Y on the left, so the right side is lower" );
		// heading −Y (256) reverses the pitch
		Assert.AreEqual( -rise.Pitch, TrackSurface.Pose( 10, 10, 256, 2, ( x, y ) => 0.5f * y ).Pitch, 1e-3f );
	}

	/// <summary>Authored MD2 input, not a test-only TrackSurface constructor or original game asset.</summary>
	private static TrackSurface ObjectSurface( bool records, params TrackSurface.Triangle[] triangles )
	{
		var builder = new ModelBuilder();
		var node = builder.AddMesh( "surface" );
		// The authoring API requires the head bit; bit 2 selects this synthetic surface record.
		if ( records )
			node.AddHead( 1, type: 0x82 );
		var material = node.Mesh!.AddMaterial( null );
		foreach ( var triangle in triangles )
		{
			int Vertex( System.Numerics.Vector3 p ) => material.AddVertex(
				new System.Numerics.Vector3( p.X, p.Z, p.Y ), System.Numerics.Vector3.UnitY, System.Numerics.Vector2.Zero );
			material.AddTriangle( Vertex( triangle.A ), Vertex( triangle.B ), Vertex( triangle.C ) );
		}
		var model = builder.Build();
		foreach ( var mesh in model.Meshes )
			for ( var face = 0; face < mesh.FaceNormalIndices.Length; face++ )
				mesh.FaceNormalIndices[face] |= 0x8000;
		// the synthetic model's only node is the type loader's ground node (the mode-4 mesh, 0x46A5A0)
		var surface = TrackSurface.FromObject( model, groundNode: records ? -1 : 0 );
		Assert.AreEqual( records ? TrackSamplerMode.SurfaceRecords : TrackSamplerMode.InstanceMesh, surface.Mode );
		Assert.AreEqual( triangles.Length, surface.TriangleCount );
		return surface;
	}

	[TestMethod]
	public void ObjectSurfaceRejectsReversedWindingThroughTheModelAxisConversion()
	{
		var front = Triangle( 0, 0, 3, 0, 8, 3, 8, 0, 3 );
		var back = new TrackSurface.Triangle( front.A, front.C, front.B );
		Assert.AreEqual( 3f, ObjectSurface( true, front ).HeightAt( 2, 2 )!.Value );
		Assert.IsNull( ObjectSurface( true, back ).HeightAt( 2, 2 ) );
		Assert.AreEqual( 3f, new TrackSurface( new[] { back } ).HeightAt( 2, 2 )!.Value,
			"The general triangle-list helper remains two-sided." );
	}

	[TestMethod]
	public void ObjectSurfaceIncludesExactEdgesButNotTheFormerBarycentricTolerance()
	{
		var surface = ObjectSurface( true, Triangle( 0, 0, 3, 0, 8, 3, 8, 0, 3 ) );
		foreach ( var point in new[] { (0f, 0f), (0f, 4f), (4f, 0f), (4f, 4f), (8f, 0f), (0f, 8f) } )
			Assert.AreEqual( 3f, surface.HeightAt( point.Item1, point.Item2 )!.Value );
		const float outside = 1f / 4096;
		Assert.IsNull( surface.HeightAt( -outside, 2 ) );
		Assert.IsNull( surface.HeightAt( 2, -outside ) );
		Assert.IsNull( surface.HeightAt( 4 + outside, 4 ) );
	}

	[TestMethod]
	public void HigherBackFaceDoesNotHideTheLowerAcceptedObjectFace()
	{
		var back = Triangle( 0, 0, 9, 8, 0, 9, 0, 8, 9 );
		var front = Triangle( 0, 0, 3, 0, 8, 3, 8, 0, 3 );
		Assert.AreEqual( 3f, ObjectSurface( true, back, front ).HeightAt( 2, 2 )!.Value );
		Assert.AreEqual( 3f, ObjectSurface( true, front, back ).HeightAt( 2, 2 )!.Value );
		Assert.AreEqual( 3f, ObjectSurface( false, back, front ).HeightAt( 2, 2 )!.Value,
			"The instance-mesh first-hit route must skip rejected back faces too." );
		Assert.AreEqual( 0f, ObjectSurface( false, back ).HeightAt( 2, 2 )!.Value,
			"No accepted instance face retains the existing base-height fallback." );
	}

	[TestMethod]
	public void DirectedObjectFacesKeepSlopeInterpolationAndTranslation()
	{
		var surface = ObjectSurface( true, Triangle( 16, -8, 2, 16, 0, 2, 24, -8, 10 ) );
		Assert.AreEqual( 4f, surface.HeightAt( 18, -6 )!.Value );
		Assert.IsNull( surface.HeightAt( 15, -6 ) );
	}
}
