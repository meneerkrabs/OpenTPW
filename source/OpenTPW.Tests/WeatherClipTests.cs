using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace OpenTPW.Tests;

/// <summary>
/// The weather's projection to the screen (docs/AUDIO.md "Weather visuals"): the lightning ribbon emitter of tp.exe (0x57DD10), the original's depth value in OpenTPW's scene depth, the near-plane clip and the
/// view modes that take the stronger rain parallax. The reference numbers were produced by running the original's own instructions in an offline emulator over the same inputs (static analysis; tp.exe is never launched).
/// </summary>
[TestClass]
public class WeatherClipTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	// The original's view-projection-screen matrix for a camera at the origin looking along +z: x' = 2 a cos(f/2) x, y' = -2 cos(f/2) y, z' = m10 z - m10 n, w' = sin(f/2) z (0x578BE0, 0x5781D0)
	private static NVector4 Original( NVector3 point, float aspect = 0.75f, float fieldOfView = 90f )
	{
		var c = MathF.Cos( fieldOfView * MathF.PI / 360f );
		var s = MathF.Sin( fieldOfView * MathF.PI / 360f );
		var m10 = s / (1f - WeatherProjection.OriginalNear / WeatherProjection.OriginalFar);
		return new NVector4( 2 * aspect * c * point.X, -2 * c * point.Y, m10 * point.Z - m10 * WeatherProjection.OriginalNear, s * point.Z );
	}

	private static NVector3 P( float x, float y, float z ) => new( x, y, z );

	private static readonly NVector3[] Trunk = { P( 0f, 0f, 3f ), P( 0.2f, 1f, 3.2f ), P( 0.5f, 2.1f, 3.6f ), P( 0.4f, 3f, 4f ), P( 1.1f, 4.2f, 4.4f ), P( 0.9f, 5f, 4.1f ) };
	private static readonly NVector3[] Branch = { P( 0.5f, 2.1f, 3.6f ), P( 1.5f, 2.6f, 3.9f ), P( 2.4f, 2.9f, 4.4f ), P( 3f, 3.3f, 5f ) };

	[TestMethod]
	public void TheRibbonEmitterMatchesTheOriginalsQuadsForATrunkABranchAndARepeatedPoint()
	{
		// the calls of one scripted walk: the trunk, a branch at depth 1 (reset, polyline, closing call), the trunk again with a repeated point, the closing call
		var calls = new (int Depth, bool Reset, NVector3? First, NVector3 Second)[]
		{
			(0, false, Trunk[1], Trunk[0]), (0, false, Trunk[2], Trunk[1]), (0, false, Trunk[3], Trunk[2]),
			(1, true, null, default),
			(1, false, Branch[1], Branch[0]), (1, false, Branch[2], Branch[1]), (1, false, Branch[3], Branch[2]), (1, false, null, Branch[3]),
			(0, false, Trunk[4], Trunk[3]), (0, false, Trunk[4], Trunk[4]), (0, false, Trunk[5], Trunk[4]), (0, false, null, Trunk[5]),
		};
		// [BIN:TPW-EXE:0x57DD10 ribbon quads] (depth, the four vertices x, y, z, w) from the original's code with the matrix above (aspect 0.75, field of view 90)
		var expected = new (int Depth, (float X, float Y, float Z, float W)[] Vertices)[]
		{
			(0, new[] { (-1.28562f, -0.19284f, 2.05081f, 2.12132f), (1.28562f, 0.19284f, 2.05081f, 2.12132f), (1.28562f, 0.19284f, 2.05081f, 2.12132f), (-1.28562f, -0.19284f, 2.05081f, 2.12132f) }),
			(0, new[] { (-1.28562f, -0.19284f, 2.05081f, 2.12132f), (1.28562f, 0.19284f, 2.05081f, 2.12132f), (1.4922f, -1.18746f, 2.19225f, 2.26274f), (-1.06794f, -1.64097f, 2.19225f, 2.26274f) }),
			(1, new[] { (0.16978f, -3.51068f, 2.47512f, 2.54558f), (0.89089f, -2.42902f, 2.47512f, 2.54558f), (0.89089f, -2.42902f, 2.47512f, 2.54558f), (0.16978f, -3.51068f, 2.47512f, 2.54558f) }),
			(1, new[] { (0.16978f, -3.51068f, 2.47512f, 2.54558f), (0.89089f, -2.42902f, 2.47512f, 2.54558f), (1.90439f, -3.1075f, 2.68727f, 2.75772f), (1.27759f, -4.24641f, 2.68727f, 2.75772f) }),
			(1, new[] { (1.27759f, -4.24641f, 2.68727f, 2.75772f), (1.90439f, -3.1075f, 2.68727f, 2.75772f), (3.61382f, -4.18109f, 3.46517f, 3.53553f), (2.75014f, -5.15272f, 3.46517f, 3.53553f) }),
			(0, new[] { (-1.06794f, -1.64097f, 2.19225f, 2.26274f), (1.4922f, -1.18746f, 2.19225f, 2.26274f), (1.82804f, -2.89279f, 2.47512f, 2.54558f), (-0.76738f, -3.04691f, 2.47512f, 2.54558f) }),
			(0, new[] { (-0.76738f, -3.04691f, 2.47512f, 2.54558f), (1.82804f, -2.89279f, 2.47512f, 2.54558f), (1.70669f, -4.02958f, 2.75799f, 2.82843f), (-0.85816f, -4.4557f, 2.75799f, 2.82843f) }),
			(0, new[] { (-0.85816f, -4.4557f, 2.75799f, 2.82843f), (1.70669f, -4.02958f, 2.75799f, 2.82843f), (2.23233f, -7.31064f, 2.82871f, 2.89914f), (-0.32314f, -6.83149f, 2.82871f, 2.89914f) }),
		};
		var ribbon = new WeatherBoltRibbon( point => Original( point ) );
		var quads = new List<(int Depth, WeatherRibbonQuad Quad)>();
		foreach ( var call in calls )
		{
			if ( call.Reset )
			{
				ribbon.ResetDepth( call.Depth );
				continue;
			}
			if ( ribbon.Emit( call.Depth, call.First, call.Second ) is { } quad )
				quads.Add( (call.Depth, quad) );
		}
		Assert.AreEqual( expected.Length, quads.Count, "one quad per call from the second of a polyline on, none for the first, the repeated point or a zero step" );
		for ( var index = 0; index < expected.Length; index++ )
		{
			Assert.AreEqual( expected[index].Depth, quads[index].Depth );
			var actual = new[] { quads[index].Quad.V0, quads[index].Quad.V1, quads[index].Quad.V2, quads[index].Quad.V3 };
			for ( var corner = 0; corner < 4; corner++ )
			{
				var (x, y, z, w) = expected[index].Vertices[corner];
				Assert.AreEqual( x, actual[corner].Clip.X, 3e-4f, $"quad {index} vertex {corner} x" );
				Assert.AreEqual( y, actual[corner].Clip.Y, 3e-4f, $"quad {index} vertex {corner} y" );
				Assert.AreEqual( z, actual[corner].Clip.Z, 3e-4f, $"quad {index} vertex {corner} z" );
				Assert.AreEqual( w, actual[corner].Clip.W, 3e-4f, $"quad {index} vertex {corner} w" );
			}
		}
		// the texture corners (u0, v0), (u1, v0), (u1, v1), (u0, v1) of the image's frame
		var first = quads[0].Quad;
		Assert.AreEqual( new NVector2( 0, 0 ), first.V0.TexCoords );
		Assert.AreEqual( new NVector2( 1, 0 ), first.V1.TexCoords );
		Assert.AreEqual( new NVector2( 1, 1 ), first.V2.TexCoords );
		Assert.AreEqual( new NVector2( 0, 1 ), first.V3.TexCoords );
	}

	[TestMethod]
	public void ARibbonsWidthIs1Point3OverTheDepthPlusOneInClipUnitsAndCornersKeepTheirPointsZAndW()
	{
		foreach ( var depth in new[] { 0, 1, 4 } )
		{
			var ribbon = new WeatherBoltRibbon( point => Original( point ) );
			ribbon.ResetDepth( depth );
			Assert.IsNull( ribbon.Emit( depth, Trunk[1], Trunk[0] ), "the first call emits nothing" );
			ribbon.Emit( depth, Trunk[2], Trunk[1] );
			var quad = ribbon.Emit( depth, Trunk[3], Trunk[2] )!.Value;
			// the end corners of the middle quad are the mitre at the previous point: the pair is 2 x 1.3 / (depth + 1) apart in clip xy and carries that point's z and w
			var width = (quad.V3.Clip - quad.V2.Clip);
			Assert.AreEqual( 2 * 1.3f / (depth + 1), new NVector2( width.X, width.Y ).Length(), 1e-3f, $"depth {depth}" );
			Assert.AreEqual( quad.V2.Clip.W, quad.V3.Clip.W );
			Assert.AreEqual( Original( Trunk[1] ).W, quad.V2.Clip.W, 1e-4f, "w of the point the corners belong to (the previous point: the call before the last)" );
		}
	}

	[TestMethod]
	public void ACallWithTwoEqualPointsDoesNothingAndAZeroStepStopsTheCall()
	{
		var ribbon = new WeatherBoltRibbon( point => Original( point ) );
		Assert.IsNull( ribbon.Emit( 0, Trunk[0], Trunk[0] ) );
		Assert.IsNull( ribbon.Emit( 0, Trunk[1], Trunk[0] ), "the repeated call did not count: this is the first call of the depth, which emits nothing" );
		Assert.IsNotNull( ribbon.Emit( 0, Trunk[2], Trunk[1] ), "the second call emits the first quad" );
		var stopped = new WeatherBoltRibbon( point => Original( point ) );
		stopped.Emit( 0, Trunk[1], Trunk[0] );
		Assert.IsNull( stopped.Emit( 0, Trunk[2], Trunk[0] ), "a step of zero length in clip xy (the second point is the previous point) stops the call" );
	}

	[TestMethod]
	public void TheBoltsEmitterCallsFollowTheWalkOrderFromTheGroundToTheSkyWithOneClosingCallPerPolyline()
	{
		var top = new NVector3( 120, 300, 80 );
		var bottom = new NVector3( 120, 0, 80 );
		foreach ( var seed in new ulong[] { 1, 7, 99, 12345 } )
		{
			var (segments, calls) = WeatherVisualState.Build( top, bottom, seed );
			Assert.AreEqual( segments.Count, calls.Count( call => call.First != null ), "every final segment is one emit call" );
			var trunk = calls.Where( call => call.Depth == 0 && !call.StartsBranch ).ToList();
			Assert.AreEqual( bottom, trunk[0].Second, "the polyline starts at the ground point (the first call's second point)" );
			Assert.IsNull( trunk[^1].First, "and the last call closes it" );
			Assert.AreEqual( top, trunk[^1].Second, "at the sky point" );
			Assert.AreEqual( calls.Count( call => call.StartsBranch ), calls.Count( call => call.First == null && !call.StartsBranch && call.Depth > 0 ), "every branch is closed once" );
			// a branch's polyline starts after its marker (a nested branch may start first)
			for ( var index = 0; index < calls.Count; index++ )
				if ( calls[index].StartsBranch )
				{
					Assert.IsTrue( index + 1 < calls.Count );
					Assert.IsTrue( calls[index + 1].Depth >= calls[index].Depth );
				}
		}
	}

	[TestMethod]
	public void TheWalkEmitsTheSameCallsAsTheOriginalsWalkOnTheSameSeed()
	{
		// [BIN:TPW-EXE:0x57E810 walk] the original's walk run offline on the stream state seed ^ (seed >> 32) | 1 with the sky point (120, 300, 80) and the ground point (120, 0, 80): the emitter calls it makes (the trunk's closing call comes from 0x57EC50 and is not among them)
		// seed, calls, closing calls, calls at a branch depth, the sum of every call's coordinates, deepest depth, the first call's second point x y z and the last call's second point
		var references = new (ulong Seed, int Calls, int Closings, int Deep, float Sum, int MaxDepth, NVector3 FirstSecond, NVector3 LastSecond)[]
		{
			(7, 433, 8, 183, 319937.35f, 1, P( 120f, 0f, 80f ), P( 120.46303f, 298.76328f, 81.68890f )),
			(99, 445, 9, 150, 303755.2f, 2, P( 120f, 0f, 80f ), P( 118.17064f, 298.83890f, 79.26804f )),
			(12345, 318, 3, 86, 256879.04f, 1, P( 120f, 0f, 80f ), P( 120.23022f, 298.75800f, 79.39822f )),
			(31337, 418, 10, 167, 236429.89f, 3, P( 118.64790f, 5.19788f, 80.95280f ), P( 120.82439f, 297.59088f, 80.40387f )),
		};
		foreach ( var reference in references )
		{
			var (_, calls) = WeatherVisualState.Build( new NVector3( 120, 300, 80 ), new NVector3( 120, 0, 80 ), reference.Seed );
			var emitted = calls.Where( call => !call.StartsBranch ).SkipLast( 1 ).ToList();
			Assert.AreEqual( reference.Calls, emitted.Count, $"seed {reference.Seed}: calls" );
			Assert.AreEqual( reference.Closings, emitted.Count( call => call.First == null ), $"seed {reference.Seed}: closing calls" );
			Assert.AreEqual( reference.Deep, emitted.Count( call => call.Depth > 0 ), $"seed {reference.Seed}: calls at a branch depth" );
			Assert.AreEqual( reference.MaxDepth, emitted.Max( call => call.Depth ), $"seed {reference.Seed}: deepest branch" );
			var sum = 0f;
			foreach ( var call in emitted )
			{
				if ( call.First is { } first )
					sum += first.X + first.Y + first.Z;
				sum += call.Second.X + call.Second.Y + call.Second.Z;
			}
			Assert.AreEqual( reference.Sum, sum, 2f, $"seed {reference.Seed}: the calls' coordinates" );
			Assert.IsTrue( NVector3.Distance( reference.FirstSecond, emitted[0].Second ) < 1e-2f, $"seed {reference.Seed}: the first call's second point {emitted[0].Second}" );
			Assert.IsTrue( NVector3.Distance( reference.LastSecond, emitted[^1].Second ) < 1e-2f, $"seed {reference.Seed}: the last call's second point {emitted[^1].Second}" );
			Assert.AreEqual( 0, emitted[^1].Depth );
		}
	}

	[TestMethod]
	public void TheOriginalsDepthValueSitsWhereASceneSurfaceOfTheEquivalentDistanceDoes()
	{
		// the original's depth-buffer value at view depth v (0x578BE0 with near 0.1, far 1000) and its inverse
		foreach ( var distance in new[] { 0.1f, 0.3f, 1f, 2.5f, 10f, 200f } )
		{
			var value = (distance - 0.1f) / (distance * (1f - 0.1f / 1000f));
			Assert.AreEqual( distance, WeatherProjection.EquivalentDistance( value ), distance * 1e-3f + 1e-4f, $"v = {distance}" );
		}
		// the flake's value (z + 0.1) / (z + 0.1501) and the rain's (z + 0.04) / (z + 0.1) of the original's weather vertices
		var flake = 2f;
		Assert.AreEqual( (flake + 0.1f) / (flake + 0.1501f), (WeatherProjection.EquivalentDistance( (flake + 0.1f) / (flake + 0.1501f) ) - 0.1f) / (WeatherProjection.EquivalentDistance( (flake + 0.1f) / (flake + 0.1501f) ) * 0.9999f), 1e-5f );
		Assert.IsTrue( WeatherProjection.EquivalentDistance( (flake + 0.1f) / (flake + 0.1501f) ) > 2 * flake, "a flake sorts as a surface about twice as far as the flake's depth, so a world surface nearer than that hides it" );
	}

	[TestMethod]
	public void SceneDepthFollowsTheProjectionAndTheNearVariantsDifferOnlyWhereTheSceneCameraWouldCut()
	{
		var projection = OriginalProjection.Create( 4f / 3f, farPlane: 200f );
		const float scale = OriginalParkPlacement.ModelScale;
		Assert.AreEqual( 0f, WeatherProjection.SceneDepth( projection, 0.1f ), 1e-5f, "0 at the scene's near plane, 0.1 engine units" );
		Assert.AreEqual( 1f, WeatherProjection.SceneDepth( projection, 200f ), 1e-4f, "1 at its far plane" );

		// a rain vertex right in front of the eye: its equivalent distance (0.5 original units is the scene near plane) is below the scene's near plane
		var near = WeatherProjection.WriteDepth( 0.4f, projection, scale, WeatherNearPlane.Scene );
		Assert.IsTrue( WeatherProjection.EquivalentDistance( 0.4f ) * scale < 0.1f && near < 0, "the scene's near plane cuts it (a negative depth the device clips)" );
		var original = WeatherProjection.WriteDepth( 0.4f, projection, scale, WeatherNearPlane.Original );
		Assert.AreEqual( 0f, original, "the original's draws it, in front of everything" );
		// farther away both agree
		var farValue = (1.5f - 0.1f) / (1.5f * 0.9999f);
		Assert.AreEqual( WeatherProjection.WriteDepth( farValue, projection, scale, WeatherNearPlane.Scene ), WeatherProjection.WriteDepth( farValue, projection, scale, WeatherNearPlane.Original ) );
		Assert.IsTrue( WeatherProjection.WriteDepth( farValue, projection, scale, WeatherNearPlane.Scene ) is > 0 and < 1 );
	}

	[TestMethod]
	public void TheNearClipCutsAQuadAtThePlaneAndInterpolatesItsTexture()
	{
		var quad = new[]
		{
			new WeatherRibbonVertex( new NVector4( -1, 0, 2, 4 ), new NVector2( 0, 0 ) ),
			new WeatherRibbonVertex( new NVector4( 1, 0, 2, 4 ), new NVector2( 1, 0 ) ),
			new WeatherRibbonVertex( new NVector4( 1, 0, -2, 1 ), new NVector2( 1, 1 ) ),
			new WeatherRibbonVertex( new NVector4( -1, 0, -2, 1 ), new NVector2( 0, 1 ) ),
		};
		var output = new WeatherRibbonVertex[8];
		var count = WeatherProjection.ClipNear( quad, output, 1f, 0f, 0f );
		Assert.AreEqual( 4, count, "the quad is cut by z >= 0 into a smaller quad" );
		foreach ( var vertex in output.Take( count ) )
			Assert.IsTrue( vertex.Clip.Z >= -1e-6f );
		var cut = output.Take( count ).Where( vertex => MathF.Abs( vertex.Clip.Z ) < 1e-6f ).ToList();
		Assert.AreEqual( 2, cut.Count );
		Assert.IsTrue( cut.All( vertex => MathF.Abs( vertex.TexCoords.Y - 0.5f ) < 1e-5f ), "the texture follows the cut (z runs 2 to -2: the plane is half way)" );
		Assert.AreEqual( 0, WeatherProjection.ClipNear( quad, output, 0f, 1f, -100f ), "everything in front of the plane's far side is cut" );
		Assert.AreEqual( 4, WeatherProjection.ClipNear( quad, output, 0f, 1f, 0f ), "a quad wholly inside keeps its four vertices" );
	}

	[TestMethod]
	public void TheStrongerParallaxFactorBelongsToTheCamcorderAndTheRideAndNoOtherView()
	{
		// the original's bits 0x2 (the terrain camcorder, OpenTPW's free walk) and 0x4 (the ride follow); bit 0x10 is never set by any instruction
		Assert.IsFalse( WeatherRenderer.IsEyeView( null ) );
		Assert.IsFalse( WeatherRenderer.IsEyeView( new CameraMode() ), "the plain overview camera" );
		Assert.IsFalse( WeatherRenderer.IsEyeView( new ParkCameraMode() ), "the park overview takes 0.1" );
		Assert.IsFalse( WeatherRenderer.IsEyeView( new LobbyCameraMode() ), "the lobby takes 0.1" );
		Assert.IsFalse( WeatherRenderer.IsEyeView( new GlobeCameraMode() ) );
		Assert.AreEqual( 0.3f, WeatherVisualState.EyeModeParallaxScale );
		Assert.AreEqual( 0.1f, WeatherVisualState.ParallaxScale );
	}
}
