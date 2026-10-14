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
}
