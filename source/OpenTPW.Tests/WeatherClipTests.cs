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
/// view modes that take the stronger rain parallax. The reference numbers were produced by an offline emulation of the original's own instructions over the same inputs (analysis scripts outside the repository; tp.exe is never launched).
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
	public void TheDepthSplitThresholdMatchesTheOriginalsOnTheFarArgument()
	{
		// [BIN:TPW-EXE:0x583220 depth split] the original's code run offline for D = 1000 (the eye views) and D = 400: the threshold [0x8BCC28] and the slope [0x8BCC34]
		Assert.AreEqual( 2.9294944f, WeatherProjection.SplitThreshold( 1000f ), 2e-3f );
		Assert.AreEqual( 1.1294838f, WeatherProjection.SplitThreshold( 400f ), 2e-3f );
		// the eye views' far argument is at least 100 (0x56EA90): the linear range then ends at a clip z of 0.23, the divided range holds nearly all of the 8-unit drop field
		Assert.AreEqual( 0.2295f, WeatherProjection.SplitThreshold( 100f ), 2e-3f );
		// a far argument so small that t stays at the near plane: the linear range ends where the scene's depth is the near plane's
		Assert.AreEqual( 0f, WeatherProjection.SplitThreshold( 20f ), 0.05f );
	}

	[TestMethod]
	public void TheOriginalsDepthValueSitsWhereASceneSurfaceOfTheEquivalentDistanceDoes()
	{
		var threshold = WeatherProjection.SplitThreshold( 1000f );
		var m10 = WeatherProjection.ParkSine / (1f - 0.1f / 1000f);
		// the linear range (clip z below the threshold, no divide by w): the surface at n + z / m10, whatever the w
		foreach ( var z in new[] { 0.04f, 0.5f, 2.04f, 2.9f } )
			Assert.AreEqual( 0.1f + z / m10, WeatherProjection.EquivalentDistance( z, z + 0.06f ), 1e-4f, $"z = {z}" );
		// above it, the divided range: n / (1 - (z / w) (1 - n / f)), and the two ranges meet at the distance t where the split sits
		var t = 0.1f + threshold / m10;
		var wt = WeatherProjection.ParkSine * t;
		Assert.AreEqual( t, WeatherProjection.EquivalentDistance( threshold + 1e-5f, wt ), 2e-3f, "continuous across the threshold" );
		Assert.AreEqual( t, WeatherProjection.EquivalentDistance( threshold - 1e-5f, wt ), 2e-3f );
		Assert.IsTrue( t > 4.2f && t < 4.3f, $"t = {t}: for the far argument 1000 (the first projection setup's) the linear range reaches 4.24 original units" );
		foreach ( var distance in new[] { 5f, 10f, 200f } )
		{
			var z = m10 * (distance - 0.1f);
			var w = WeatherProjection.ParkSine * distance;
			Assert.AreEqual( distance, WeatherProjection.EquivalentDistance( z, w ), distance * 1e-3f, $"v = {distance}" );
		}
		// the lobby has a split of its own, with the sine of its field of view (100 degrees): the same two ranges
		var lobbySine = MathF.Sin( 50f * MathF.PI / 180f );
		var lobbyM10 = lobbySine / (1f - 0.1f / 400f);
		Assert.AreEqual( 0.1f + 0.5f / lobbyM10, WeatherProjection.EquivalentDistance( 0.5f, 0.6f, 400f, lobbySine ), 1e-4f );
		Assert.AreNotEqual( WeatherProjection.SplitThreshold( 400f, lobbySine ), WeatherProjection.SplitThreshold( 400f ), 1e-4f, "the threshold follows the field of view's sine" );
		// the divided range alone (a flake in it): the depth value z / w sorts as a surface about twice as far as its depth
		var flake = 20f;
		var ratio = (flake + 0.1f) / (flake + 0.1501f);
		Assert.AreEqual( WeatherProjection.DividedDistance( ratio, 1f ), WeatherProjection.EquivalentDistance( flake + 0.1f, flake + 0.1501f ), 1e-3f );
		Assert.IsTrue( float.IsPositiveInfinity( WeatherProjection.EquivalentDistance( 5f, 4.9f ) ), "beyond the far plane" );
		// the compare is unsigned: a negative z takes the divided branch
		Assert.AreEqual( WeatherProjection.DividedDistance( -0.5f, 3f ), WeatherProjection.EquivalentDistance( -0.5f, 3f ), 1e-5f );
	}

	[TestMethod]
	public void SceneDepthFollowsTheProjectionAndNothingIsCutBeforeTheEye()
	{
		var projection = OriginalProjection.Create( 4f / 3f, farPlane: 200f );
		const float scale = OriginalParkPlacement.ModelScale;
		Assert.AreEqual( 0f, WeatherProjection.SceneDepth( projection, 0.1f ), 1e-5f, "0 at the scene's near plane, 0.1 engine units" );
		Assert.AreEqual( 1f, WeatherProjection.SceneDepth( projection, 200f ), 1e-4f, "1 at its far plane" );
		// a rain vertex right in front of the eye: z 0.09, w 0.15 sorts at 0.1 + 0.09 / 0.7 = 0.23 original units, nearer than the scene's near plane: the original draws it, in front of everything
		Assert.IsTrue( WeatherProjection.EquivalentDistance( 0.09f, 0.15f ) * scale < 0.1f );
		Assert.AreEqual( 0f, WeatherProjection.WriteDepth( 0.09f, 0.15f, projection, scale ) );
		Assert.IsTrue( WeatherProjection.WriteDepth( 0.7071f * 499.9f, 0.7071f * 500f, projection, scale ) is > 0 and < 1, "a drop 500 original units away" );
		Assert.AreEqual( 1f, WeatherProjection.WriteDepth( 5f, 4.9f, projection, scale ), "beyond the far plane" );
		Assert.AreEqual( 1f, WeatherProjection.WriteDepth( 100.02f, 100f, projection, scale ), "and no NaN at the pole of the mapping" );
	}

	[TestMethod]
	public void ARainStreakWhoseLeadingEndLiesBeyondTheFarPlaneIsClippedAtZEqualsW()
	{
		// looking steeply up: the streak's leading end has z = trailing depth + 0.04 and w = its own depth + 0.1 with the leading depth far less: z > w
		var output = new WeatherVertex[6];
		WeatherVisualState.WriteStreak( new NVector3( 0f, 0f, 1f ), new NVector3( 0f, 0f, -0.1f ), output );
		var triangle = new[] { output[3], output[4], output[5] }.Select( v => new WeatherRibbonVertex( new NVector4( v.Position, v.W ), v.TexCoords, new NVector2( v.Grey, v.Alpha ) ) ).ToArray();
		Assert.IsTrue( triangle.Any( v => v.Clip.Z > v.Clip.W ), "the leading vertices are beyond the far plane" );
		var clipped = new WeatherRibbonVertex[8];
		var count = WeatherProjection.ClipNear( triangle, clipped, -1f, 1f, 0f );
		Assert.IsTrue( count >= 3 );
		foreach ( var vertex in clipped.Take( count ) )
			Assert.IsTrue( vertex.Clip.Z <= vertex.Clip.W + 1e-5f );
		Assert.IsTrue( clipped.Take( count ).All( v => v.Tint.Y is >= 0.5f and <= 1f ), "the alpha interpolates between 0x80 and 0xFF" );
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
