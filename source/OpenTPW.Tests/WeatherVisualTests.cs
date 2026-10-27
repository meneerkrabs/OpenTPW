using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The weather's visual presentation (docs/AUDIO.md "Weather visuals"): the original CRain's 1024-point drop
/// field, its scroll and wrap, the rain streak and snow flake geometry, the bolt shape, lifetime and colour,
/// and the strikes the simulation reports for drawing.
/// </summary>
[TestClass]
public class WeatherVisualTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private sealed class Source : IWeatherVisualSource
	{
		public int DropCount { get; set; }
		public bool Snow { get; set; }
		public float BoltLifetimeSeconds { get; set; } = 2f;
		public List<NVector3> Tops { get; } = new();
		public IReadOnlyList<WeatherStrike> TakeStrikes()
		{
			var taken = Tops.Select( WeatherStrike.Below ).ToList();
			Tops.Clear();
			return taken;
		}
	}

	private static readonly WeatherCamera Eye = WeatherCamera.Looking( NVector3.Zero );

	private static WeatherVisualState Field( int drops, bool snow = false )
	{
		var state = new WeatherVisualState();
		state.Update( 0.016f, new Source { DropCount = drops, Snow = snow }, Eye );
		return state;
	}

	private static int Rain( WeatherVisualState state, WeatherCamera camera, out WeatherVertex[] vertices )
	{
		vertices = new WeatherVertex[WeatherVisualState.FieldSize * 6];
		return state.WriteRain( camera, vertices );
	}

	[TestMethod]
	public void TheDrawnCountIsTheSourceCountCappedAtTheField()
	{
		var state = new WeatherVisualState();
		var source = new Source { DropCount = 300 };
		state.Update( 0.016f, source, Eye );
		Assert.AreEqual( 300, state.DropCount );
		source.DropCount = 0;
		state.Update( 0.016f, source, Eye );
		Assert.AreEqual( 0, state.DropCount, "the weather drying up draws nothing" );
		source.DropCount = WeatherVisualState.FieldSize + 500;
		state.Update( 0.016f, source, Eye );
		Assert.AreEqual( WeatherVisualState.FieldSize, state.DropCount, "the field holds 1024 points" );
	}

	[TestMethod]
	public void TheFieldIsFixedAndItsPointsLieInTheBox()
	{
		var first = Field( WeatherVisualState.FieldSize );
		var second = Field( WeatherVisualState.FieldSize );
		for ( var index = 0; index < WeatherVisualState.FieldSize; index++ )
		{
			var point = first.DropOffset( index );
			Assert.AreEqual( point, second.DropOffset( index ), "the same seed gives the same 1024 points" );
			foreach ( var axis in new[] { point.X, point.Y, point.Z } )
				Assert.IsTrue( axis >= -WeatherVisualState.BoxHalfExtent && axis < WeatherVisualState.BoxHalfExtent, $"point {index} left the box: {axis}" );
		}
	}

	[TestMethod]
	public void TheBoxScrollsDownAtTheRainAndSnowVelocitiesAndWraps()
	{
		foreach ( var (snow, velocity) in new[] { (false, WeatherVisualState.RainVelocity), (true, WeatherVisualState.SnowVelocity) } )
		{
			var state = Field( 8, snow );
			var before = state.DropOffset( 3 );
			state.Update( 0.1f, new Source { DropCount = 8, Snow = snow }, Eye );
			var after = state.DropOffset( 3 );
			Assert.AreEqual( before.X, after.X, 1e-4f, "straight down: no sideways drift" );
			Assert.AreEqual( before.Z, after.Z, 1e-4f );
			var fell = before.Y - after.Y;
			if ( fell < 0 )
				fell += 2 * WeatherVisualState.BoxHalfExtent;
			Assert.AreEqual( velocity * 0.1f, fell, 1e-3f, "the point fell velocity x dt" );
		}
		var wrapping = Field( 8 );
		for ( var step = 0; step < 100; step++ )
			wrapping.Update( 0.5f, new Source { DropCount = 8 }, Eye );
		for ( var index = 0; index < 8; index++ )
			Assert.IsTrue( Math.Abs( wrapping.DropOffset( index ).Y ) <= WeatherVisualState.BoxHalfExtent, "a long fall stays inside the box" );
	}

	[TestMethod]
	public void TheCameraMovementShiftsTheFieldByTheParallaxWeight()
	{
		var state = Field( 8 );
		var before = state.Offset;
		state.Update( 0f, new Source { DropCount = 8 }, WeatherCamera.Looking( new NVector3( 5, 0, 0 ) ) );
		var moved = state.Offset.X - before.X;
		if ( moved > WeatherVisualState.BoxHalfExtent )
			moved -= 2 * WeatherVisualState.BoxHalfExtent;
		Assert.AreEqual( -5 * WeatherVisualState.ParallaxScale, moved, 1e-4f, "the field follows 0.1 of the camera's movement (a zero dt leaves the fall alone)" );
	}

	[TestMethod]
	public void AnEyeModeCameraShiftsTheFieldByThreeTenthsOfItsMovement()
	{
		// [BIN:TPW-EXE:0x580003-0x58000B] the factor is 0.3 (0x3E99999A) while the camera-mode byte has bits 0x16
		var state = Field( 8 );
		var before = state.Offset;
		var camera = WeatherCamera.Looking( new NVector3( 5, 0, 0 ) ) with { EyeMode = true };
		state.Update( 0f, new Source { DropCount = 8 }, camera );
		var moved = state.Offset.X - before.X;
		if ( moved > WeatherVisualState.BoxHalfExtent )
			moved -= 2 * WeatherVisualState.BoxHalfExtent;
		Assert.AreEqual( -5 * WeatherVisualState.EyeModeParallaxScale, moved, 1e-3f );
	}

	[TestMethod]
	public void TheWrappedParallaxTermIsTruncatedToASixteenThousandthAndWrapsAtEight()
	{
		Assert.AreEqual( 0.25f, WeatherVisualState.QuantisedParallax( 0.25f ), 1e-7f );
		Assert.AreEqual( 0f, WeatherVisualState.QuantisedParallax( 0.00001f ), 1e-9f, "the integer step truncates below 1/16384" );
		Assert.AreEqual( 3.5f, WeatherVisualState.QuantisedParallax( -4.5f ), 1e-6f, "-4.5 wraps by 8 into [-4, 4)" );
		Assert.AreEqual( -4f, WeatherVisualState.QuantisedParallax( 4f ), 1e-6f, "4 is the wrap point" );
		Assert.AreEqual( 1.0f / 16384f, WeatherVisualState.QuantisedParallax( 1.0f / 16384f + 1e-6f ), 1e-9f );
	}

	[TestMethod]
	public void TheParallaxIsTheAbsoluteCameraPositionSoAReturnRestoresTheField()
	{
		// [BIN:TPW-EXE:0x57efd0] the drops move by -0.1 x the camera's position (wrapped), not by an accumulated delta
		var state = Field( 8 );
		var before = Enumerable.Range( 0, 8 ).Select( state.DropOffset ).ToList();
		state.Update( 0f, new Source { DropCount = 8 }, WeatherCamera.Looking( new NVector3( 37.3f, -12f, 5.5f ) ) );
		state.Update( 0f, new Source { DropCount = 8 }, WeatherCamera.Looking( new NVector3( -3f, 140f, 2f ) ) );
		state.Update( 0f, new Source { DropCount = 8 }, Eye );
		var after = Enumerable.Range( 0, 8 ).Select( state.DropOffset ).ToList();
		for ( var index = 0; index < 8; index++ )
			Assert.IsTrue( NVector3.Distance( before[index], after[index] ) < 1e-3f, $"drop {index}: {before[index]} vs {after[index]}" );
		var moved = Field( 8 );
		moved.Update( 0f, new Source { DropCount = 8 }, WeatherCamera.Looking( new NVector3( 0, 0, 30 ) ) );
		var shift = moved.Offset.Z - state.Offset.Z;
		Assert.AreEqual( -3f + 8f, shift < 0 ? shift + 8f : shift, 1e-4f, "z moved by -0.1 x 30, wrapped into the 8-unit box" );
	}

	[TestMethod]
	public void TheRenderStatesFollowTheOriginalStateWords()
	{
		// [BIN:TPW-EXE:0x567620] 0x200D52 (rain, bolts): source alpha over one, no depth writes; 0x556 (snow): alpha blend with depth writes
		Assert.AreEqual( MaterialFlags.DisableDepthWrite | MaterialFlags.DoubleSided | MaterialFlags.AdditiveBlend, WeatherRenderer.RainFlags );
		Assert.AreEqual( WeatherRenderer.RainFlags, WeatherRenderer.BoltFlags );
		Assert.AreEqual( MaterialFlags.DoubleSided, WeatherRenderer.SnowFlags );
		var shader = File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", "weather.shader" ) );
		StringAssert.Contains( shader, "if ( alpha < 16.0 / 255.0 )", "the alpha test at 0x10 of 255 (ALPHAFUNC greater-or-equal)" );
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !Directory.Exists( Path.Combine( directory.FullName, "content", "shaders" ) ) )
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException( "content/shaders" );
	}

	[TestMethod]
	public void FrameTimeFollowsTheSimulationScaleCappedAtHalfASecondAndZeroWhilePaused()
	{
		// [BIN:TPW-EXE:0x57ff60] dt = min(scaled ms, 500) on the rate-scaled clock 0x402d70
		Assert.AreEqual( 0.016f, WeatherVisualState.FrameSeconds( 0.016f, 1f ) );
		Assert.AreEqual( 0.032f, WeatherVisualState.FrameSeconds( 0.016f, 2f ), 1e-7f, "a faster game advances the field faster" );
		Assert.AreEqual( 0.004f, WeatherVisualState.FrameSeconds( 0.016f, 0.25f ), 1e-7f );
		Assert.AreEqual( 0.5f, WeatherVisualState.FrameSeconds( 3f, 1f ), "a stall moves the field by 500 ms at most" );
		Assert.AreEqual( 0.5f, WeatherVisualState.FrameSeconds( 0.3f, 2f ), "the cap applies to the scaled step" );
		Assert.AreEqual( 0f, WeatherVisualState.FrameSeconds( 0.016f, 0f ), "a paused park freezes the field" );
		Assert.AreEqual( 0f, WeatherVisualState.FrameSeconds( -1f, 1f ) );
		Assert.AreEqual( 0f, WeatherVisualState.FrameSeconds( float.NaN, 1f ) );
		Assert.AreEqual( 0f, WeatherVisualState.FrameSeconds( 0.016f, float.NaN ) );
		var state = Field( 200 );
		var before = Enumerable.Range( 0, 200 ).Select( state.DropOffset ).ToList();
		state.Update( 0f, new Source { DropCount = 200 }, Eye );
		CollectionAssert.AreEqual( before, Enumerable.Range( 0, 200 ).Select( state.DropOffset ).ToList(), "a zero step moves nothing" );
	}

	[TestMethod]
	public void TheRainGreyRampIsTheOriginalsAndRunsToZeroAtDepthSix()
	{
		Assert.AreEqual( 216f / 255f, WeatherVisualState.RainGrey( 0f ), 1e-6f, "216 at the eye" );
		Assert.AreEqual( 0f, WeatherVisualState.RainGrey( 6f ), 1e-6f );
		Assert.AreEqual( 0f, WeatherVisualState.RainGrey( 9f ), 1e-6f, "never negative" );
		Assert.AreEqual( 3 * (int)(12 * (6 - 2.5f)) / 255f, WeatherVisualState.RainGrey( 2.5f ), 1e-6f );
	}

	[TestMethod]
	public void AStreakRunsPointToPointPlusATenthWithAlphaRisingToTheLeadingEnd()
	{
		var state = Field( WeatherVisualState.FieldSize );
		var count = Rain( state, Eye, out var vertices );
		Assert.IsTrue( count > 0 && count % 6 == 0 );
		Assert.IsTrue( count <= WeatherVisualState.FieldSize * 6 );
		for ( var quad = 0; quad < count; quad += 6 )
		{
			// the fan of the original's four vertices: (V0, V1, V2) and (V0, V2, V3), the clip y running down the screen
			var (v0, v1, v2, v3) = (vertices[quad], vertices[quad + 1], vertices[quad + 2], vertices[quad + 5]);
			Assert.AreEqual( v0, vertices[quad + 3] );
			Assert.AreEqual( v2, vertices[quad + 4] );
			var trailing = (v0.Position + v1.Position) / 2;
			var leading = (v2.Position + v3.Position) / 2;
			Assert.AreEqual( WeatherVisualState.StreakLength, leading.Y - trailing.Y, 1e-4f, "the streak is 0.1 long and points down the screen" );
			Assert.AreEqual( 0f, leading.X - trailing.X, 1e-4f );
			Assert.AreEqual( 128f / 255f, v0.Alpha, 1e-6f, "trailing alpha 0x80" );
			Assert.AreEqual( 1f, v2.Alpha, 1e-6f, "leading alpha 0xFF" );
			Assert.AreEqual( 0.2f * WeatherVisualState.StreakLength, (v0.Position - v1.Position).Length(), 1e-4f, "0.2 x length wide" );
			var depth = v0.W - WeatherVisualState.RainW;
			Assert.IsTrue( depth > 0, "only a trailing end in front of the eye is drawn" );
			Assert.AreEqual( depth + WeatherVisualState.RainDepthBias, v0.Position.Z, 1e-5f, "clip z is the trailing depth + 0.04 on every vertex" );
			Assert.AreEqual( v0.Position.Z, v3.Position.Z );
			Assert.AreEqual( WeatherVisualState.RainGrey( depth ), v0.Grey, 1e-6f );
			Assert.AreEqual( v0.Grey, v2.Grey, "the leading end's colour is the trailing end's too" );
		}
	}

	[TestMethod]
	public void RainStreaksMatchTheOriginalsVertexSubmitForAnExplicitStreak()
	{
		// [BIN:TPW-EXE:0x57efd0 rain vertices] reference values run on the original's own code with an identity camera (static analysis only: tp.exe is never run): trailing end, fall vector, then the four vertices (x, y, z, w)
		var cases = new (NVector3 Trailing, NVector3 Fall, (float X, float Y, float Z, float W)[] Vertices)[]
		{
			(new( 0.5f, 0.3f, 2.0f ), new( 0.0f, -0.1f, 0.0f ), new[] { (0.51f, -0.3f, 2.04f, 2.1f), (0.49f, -0.3f, 2.04f, 2.1f), (0.49f, -0.2f, 2.04f, 2.1f), (0.51f, -0.2f, 2.04f, 2.1f) }),
			(new( -1.2f, 2.0f, 3.5f ), new( 0.02f, -0.09f, 0.03f ), new[] { (-1.191f, -2.002f, 3.54f, 3.6f), (-1.209f, -1.998f, 3.54f, 3.6f), (-1.189f, -1.908f, 3.54f, 3.63f), (-1.171f, -1.912f, 3.54f, 3.63f) }),
			(new( 0.1f, -0.7f, 0.4f ), new( 0.05f, -0.08f, -0.02f ), new[] { (0.108f, 0.695f, 0.44f, 0.5f), (0.092f, 0.705f, 0.44f, 0.5f), (0.142f, 0.785f, 0.44f, 0.48f), (0.158f, 0.775f, 0.44f, 0.48f) }),
			(new( 2.5f, 1.0f, 3.5f ), new( 0.0f, 0.0f, 0.1f ), new[] { (2.5f, -1.0f, 3.54f, 3.6f), (2.5f, -1.0f, 3.54f, 3.6f), (2.5f, -1.0f, 3.54f, 3.7f), (2.5f, -1.0f, 3.54f, 3.7f) }),
			(new( -0.3f, 0.2f, 0.05f ), new( 0.07f, -0.07f, 0.0f ), new[] { (-0.293f, -0.207f, 0.09f, 0.15f), (-0.307f, -0.193f, 0.09f, 0.15f), (-0.237f, -0.123f, 0.09f, 0.15f), (-0.223f, -0.137f, 0.09f, 0.15f) }),
		};
		foreach ( var (trailing, fall, expected) in cases )
		{
			var output = new WeatherVertex[6];
			Assert.AreEqual( 6, WeatherVisualState.WriteStreak( trailing, fall, output ) );
			var fan = new[] { output[0], output[1], output[2], output[5] };
			for ( var corner = 0; corner < 4; corner++ )
			{
				Assert.AreEqual( expected[corner].X, fan[corner].Position.X, 2e-5f, $"{trailing} vertex {corner} x" );
				Assert.AreEqual( expected[corner].Y, fan[corner].Position.Y, 2e-5f, $"{trailing} vertex {corner} y" );
				Assert.AreEqual( expected[corner].Z, fan[corner].Position.Z, 2e-5f, $"{trailing} vertex {corner} z" );
				Assert.AreEqual( expected[corner].W, fan[corner].W, 2e-5f, $"{trailing} vertex {corner} w" );
			}
		}
		Assert.AreEqual( 0, WeatherVisualState.WriteStreak( new NVector3( 0.1f, 0.1f, 0f ), new NVector3( 0, -0.1f, 0 ), new WeatherVertex[6] ), "a trailing end at or behind the eye is not drawn" );
		Assert.AreEqual( 6, WeatherVisualState.WriteStreak( new NVector3( 0.1f, 0.1f, 0.02f ), new NVector3( 0, 0, -0.1f ), new WeatherVertex[6] ), "the leading end is not tested: it may lie behind the eye" );
	}

	[TestMethod]
	public void OnlyDropsInFrontOfTheCameraAreDrawn()
	{
		var state = Field( WeatherVisualState.FieldSize );
		var forward = Rain( state, Eye, out _ );
		var backward = Rain( state, new WeatherCamera( NVector3.Zero, NVector3.UnitX, NVector3.UnitY, -NVector3.UnitZ ), out _ );
		Assert.IsTrue( forward > 0 && backward > 0 );
		Assert.IsTrue( forward < WeatherVisualState.FieldSize * 6 && backward < WeatherVisualState.FieldSize * 6, "about half the box is behind the eye plane" );
		Assert.IsTrue( Math.Abs( forward - backward ) < 1500, "the two halves roughly split the field" );
	}

	[TestMethod]
	public void SnowFlakesSpreadWithDepthMapToAnAtlasQuadrantAndJitterEachFrame()
	{
		var state = Field( 8, snow: true );
		var vertices = new WeatherVertex[WeatherVisualState.FieldSize * 6];
		var count = state.WriteSnow( Eye, vertices );
		Assert.IsTrue( count > 0 && count % 3 == 0 );
		// [BIN:TPW-EXE:0x57f490] one triangle a flake: the apex above the centre, the base at the lower corners, OpenTPW's upright v running down the original's
		for ( var flake = 0; flake < count; flake += 3 )
		{
			var (apex, right, left) = (vertices[flake], vertices[flake + 1], vertices[flake + 2]);
			Assert.AreEqual( 1f, apex.Grey, "full white" );
			Assert.AreEqual( 1f, apex.Alpha );
			var aspect = 0.75f;
			Assert.AreEqual( 2 * WeatherVisualState.SnowHalfSize * aspect, (right.Position - left.Position).X, 1e-5f, "base 2 h a wide in clip x: the display's height over its width keeps the flake square on the screen" );
			Assert.AreEqual( 2 * WeatherVisualState.SnowHalfSize, left.Position.Y - apex.Position.Y, 1e-5f, "2h tall in clip y, which runs down" );
			Assert.AreEqual( (right.Position.X + left.Position.X) / 2, apex.Position.X, 1e-5f, "the apex above the base's middle" );
			Assert.AreEqual( apex.W, right.W );
			Assert.AreEqual( apex.W - WeatherVisualState.SnowProjectionBias + WeatherVisualState.SnowDepthBias, apex.Position.Z, 1e-5f, "z is the depth + 0.1 and w the depth + 0.1501" );
			var corner = left.TexCoords;
			Assert.IsTrue( corner is { X: 0f or 0.5f, Y: 0f or 0.5f }, "the upper left corner of an atlas quadrant" );
			Assert.AreEqual( new System.Numerics.Vector2( corner.X + 0.5f, corner.Y ), right.TexCoords );
			Assert.AreEqual( new System.Numerics.Vector2( corner.X + 0.25f, corner.Y + 0.5f ), apex.TexCoords, "the apex at the quadrant's lower middle" );
		}
		var first = vertices[0];
		state.Update( 0.016f, new Source { DropCount = 8, Snow = true }, Eye );
		var again = new WeatherVertex[WeatherVisualState.FieldSize * 6];
		state.WriteSnow( Eye, again );
		Assert.AreNotEqual( first.Position, again[0].Position, "a flake moves each frame" );
	}

	[TestMethod]
	public void SnowFlakesMatchTheOriginalsVertexSubmitForAnExplicitPosition()
	{
		// [BIN:TPW-EXE:0x57f490 flake vertices] reference values run on the original's own code with an identity camera, the aspect word 0.75, flake index 0 (static analysis only: tp.exe is never run): the jittered camera-space position, then the three vertices (x, y, z, w)
		var cases = new (NVector3 Position, (float X, float Y, float Z, float W)[] Vertices)[]
		{
			(new( 1.001953f, 0.501953f, 2.001953f ), new[] { (1.252686f, -0.727564f, 2.101953f, 2.152053f), (1.327686f, -0.527564f, 2.101953f, 2.152053f), (1.177686f, -0.527564f, 2.101953f, 2.152053f) }),
			(new( -0.498047f, -0.248047f, 1.001953f ), new[] { (-0.560424f, 0.179113f, 1.101953f, 1.152053f), (-0.485424f, 0.379113f, 1.101953f, 1.152053f), (-0.635424f, 0.379113f, 1.101953f, 1.152053f) }),
			(new( 2.001953f, -1.498047f, 3.901953f ), new[] { (2.978394f, 2.128711f, 4.001953f, 4.052053f), (3.053394f, 2.32871f, 4.001953f, 4.052053f), (2.903394f, 2.32871f, 4.001953f, 4.052053f) }),
			(new( 0.201953f, 3.001953f, 0.501953f ), new[] { (0.214625f, -3.290308f, 0.601953f, 0.652053f), (0.289625f, -3.090308f, 0.601953f, 0.652053f), (0.139625f, -3.090308f, 0.601953f, 0.652053f) }),
		};
		foreach ( var (position, expected) in cases )
		{
			var output = new WeatherVertex[3];
			Assert.AreEqual( 3, WeatherVisualState.WriteFlake( position, 0, 0.75f, output ) );
			for ( var corner = 0; corner < 3; corner++ )
			{
				Assert.AreEqual( expected[corner].X, output[corner].Position.X, 2e-5f, $"{position} vertex {corner} x" );
				Assert.AreEqual( expected[corner].Y, output[corner].Position.Y, 2e-5f, $"{position} vertex {corner} y" );
				Assert.AreEqual( expected[corner].Z, output[corner].Position.Z, 2e-5f, $"{position} vertex {corner} z" );
				Assert.AreEqual( expected[corner].W, output[corner].W, 2e-5f, $"{position} vertex {corner} w" );
			}
		}
		// the base half-width is h x the aspect word: at 16:9 (0.5625) the base is narrower in clip x, the same on the screen
		var wide = new WeatherVertex[3];
		WeatherVisualState.WriteFlake( new NVector3( 0, 0, 1 ), 0, 0.5625f, wide );
		Assert.AreEqual( 2 * 0.1f * 0.5625f, wide[1].Position.X - wide[2].Position.X, 1e-6f );
		Assert.AreEqual( 0, WeatherVisualState.WriteFlake( new NVector3( 0, 0, 0 ), 0, 0.75f, wide ), "a flake at or behind the eye is not drawn" );
	}

	[TestMethod]
	public void SnowJitterIsDrawnInWholeStepsOf1Over2048EveryFrameEvenWhenPaused()
	{
		// [BIN:TPW-EXE:0x57f490] each draw adds ((rand & 31) - 16) / 2048 to every axis of every flake
		var state = Field( 64, snow: true );
		var seen = new HashSet<float>();
		var changed = false;
		for ( var frame = 0; frame < 20; frame++ )
		{
			var before = state.JitterOf( 0 );
			state.Update( 0f, new Source { DropCount = 64, Snow = true }, Eye );
			changed |= state.JitterOf( 0 ) != before;
			for ( var index = 0; index < 64; index++ )
				foreach ( var axis in new[] { state.JitterOf( index ).X, state.JitterOf( index ).Y, state.JitterOf( index ).Z } )
				{
					var steps = axis * 2048f;
					Assert.AreEqual( MathF.Round( steps ), steps, 1e-4f, "a whole number of 1/2048 steps" );
					Assert.IsTrue( steps >= -16 && steps <= 15, $"{steps} within -16..15" );
					seen.Add( steps );
				}
		}
		Assert.IsTrue( changed, "a paused frame still draws a new jitter" );
		Assert.IsTrue( seen.Contains( -16f ) && seen.Contains( 15f ) && seen.Count == 32, $"all 32 steps occur ({seen.Count})" );
	}

	[TestMethod]
	public void SnowFlakesAtGreaterDepthSitFurtherFromTheScreenCentre()
	{
		// With the identity camera a flake's camera-space point is its drop offset (plus under 1/128 of jitter),
		// and its x and y are scaled by 1 + depth / 8 before the quad is placed.
		var all = Field( WeatherVisualState.FieldSize, snow: true );
		// The longest prefix of drops that stays clear of the eye plane, so the jitter cannot change what is drawn.
		var prefix = 0;
		while ( prefix < WeatherVisualState.FieldSize && Math.Abs( all.DropOffset( prefix ).Z ) > 0.02f )
			prefix++;
		Assert.IsTrue( prefix >= 8, $"prefix {prefix}" );
		var state = Field( prefix, snow: true );
		var vertices = new WeatherVertex[WeatherVisualState.FieldSize * 6];
		var count = state.WriteSnow( Eye, vertices );
		var quad = 0;
		var compared = 0;
		for ( var index = 0; index < prefix; index++ )
		{
			var offset = state.DropOffset( index );
			if ( offset.Z <= 0 )
				continue;
			var centre = new NVector3( vertices[quad * 3].Position.X, -vertices[quad * 3].Position.Y - WeatherVisualState.SnowHalfSize, vertices[quad * 3].W );
			var spread = 1f + offset.Z / WeatherVisualState.SnowSpreadDepth;
			Assert.AreEqual( offset.X * spread, centre.X, 0.03f, $"flake {index} x" );
			Assert.AreEqual( offset.Y * spread, centre.Y, 0.03f, $"flake {index} y" );
			Assert.AreEqual( offset.Z + WeatherVisualState.SnowProjectionBias, centre.Z, 0.03f, $"flake {index} depth: the original's w is the depth + 0.1501" );
			quad++;
			compared++;
		}
		Assert.IsTrue( compared > 2 && compared * 3 == count );
	}

	[TestMethod]
	public void ABoltReachesFromItsTopToGroundLevelWithItsShapeFixedPerSeed()
	{
		var top = new NVector3( 120, 300, 80 );
		var bottom = new NVector3( 120, 0, 80 );
		var first = WeatherVisualState.BuildBolt( top, bottom, 7 );
		var second = WeatherVisualState.BuildBolt( top, bottom, 7 );
		CollectionAssert.AreEqual( first.ToList(), second.ToList(), "the shape is fixed per seed" );
		CollectionAssert.AreNotEqual( first.ToList(), WeatherVisualState.BuildBolt( top, bottom, 8 ).ToList() );
		Assert.IsTrue( first.Count > 64 && first.Count <= WeatherVisualState.MaximumBoltSegments, $"segments of about 2 to 3 units over 300 units: {first.Count}" );
		var trunk = first.Where( segment => segment.Depth == 0 ).ToList();
		// The trunk is one connected path: the sky and the ground point end one segment each, every point between two.
		var ends = trunk.SelectMany( segment => new[] { segment.A, segment.B } ).GroupBy( point => point ).ToDictionary( group => group.Key, group => group.Count() );
		Assert.AreEqual( 1, ends[top], "the trunk starts at the sky point" );
		Assert.AreEqual( 1, ends[bottom], "the trunk ends at the ground point" );
		Assert.AreEqual( 2, ends.Count( pair => pair.Value == 1 ), "no other loose end" );
		Assert.IsTrue( ends.Where( pair => pair.Key != top && pair.Key != bottom ).All( pair => pair.Value == 2 ) );
		// 0x57E810 tests each half against the threshold 4..8 (squared), so every final segment is shorter than sqrt(8).
		Assert.IsTrue( first.All( segment => (segment.B - segment.A).LengthSquared() < 8f ), "a final segment stops splitting under squared length 4..8" );
		Assert.IsTrue( first.All( segment => segment.Depth <= WeatherVisualState.MaximumBranchDepth ) );
	}

	[TestMethod]
	public void TheBoltStreamIsTheOriginalsRotateMultiplyRecurrence()
	{
		// 0x57E53C: state = rotl4( rotl3( state x 0x13D573 ) + 0xE0423 ), the whole state being the draw.
		var rng = new CRainRandom( 1 );
		Assert.AreEqual( 0x0ACAFBB0u, rng.Next() );
		Assert.AreEqual( 0x55204A65u, rng.Next() );
		Assert.AreEqual( 0x741A7200u, rng.Next() );
	}

	[TestMethod]
	public void ASplitsMidpointFollowsTheOriginalsDisplacement()
	{
		// Values from an independent evaluation of 0x57E4B0's formulas on the same stream: pick 0 (x axis, falling to y
		// where the segment has no x extent), and a pick of -2 (a negative state remainder takes the z-first case).
		var rng = new CRainRandom( 1 );
		var middle = WeatherVisualState.Midpoint( ref rng, new NVector3( 0, 300, 0 ), new NVector3( 10, 0, 5 ) );
		Assert.AreEqual( -29.7755f, middle.X, 1e-2f );
		Assert.AreEqual( 152.3203f, middle.Y, 1e-2f );
		Assert.AreEqual( 2.5387f, middle.Z, 1e-2f );
		rng = new CRainRandom( 12345 );
		middle = WeatherVisualState.Midpoint( ref rng, new NVector3( 100, 300, 50 ), new NVector3( 120, 0, 80 ) );
		Assert.AreEqual( 115.4654f, middle.X, 1e-2f );
		Assert.AreEqual( 157.4529f, middle.Y, 1e-2f );
		Assert.AreEqual( 3.9278f, middle.Z, 1e-2f );
	}

	[TestMethod]
	public void NoBoltPassesTheSegmentCapWhateverTheSeedAndOrdinaryBoltsRarelyHitIt()
	{
		var most = 0;
		var capped = 0;
		for ( ulong seed = 1; seed <= 3000; seed++ )
		{
			var count = WeatherVisualState.BuildBolt( new NVector3( 120, 300, 80 ), new NVector3( 100, 0, 60 ), seed ).Count;
			Assert.IsTrue( count <= WeatherVisualState.MaximumBoltSegments, $"seed {seed} made {count} segments" );
			if ( count == WeatherVisualState.MaximumBoltSegments )
				capped++;
			most = Math.Max( most, count );
		}
		Assert.IsTrue( most > 100, "ordinary bolts still reach their natural length" );
		Assert.IsTrue( capped == 0, $"{capped} of 3000 bolts hit OpenTPW's buffer bound" );
	}

	[TestMethod]
	public void TheWeatherImagesResolveInAnyLetterCaseThroughTheGameFileSystem()
	{
		var root = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"opentpw-weather-{Guid.NewGuid():N}" );
		try
		{
			var folder = System.IO.Path.Combine( root, "Generic", "Weather" );
			System.IO.Directory.CreateDirectory( folder );
			foreach ( var name in new[] { "Raindrop.tga", "SNOW.TGA", "lightning.tga" } )
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( folder, name ), new byte[] { 1, 2, 3 } );
			var files = new BaseFileSystem( root );
			Assert.AreEqual( 3, WeatherRenderer.ImagePaths.Length );
			foreach ( var path in WeatherRenderer.ImagePaths )
			{
				Assert.IsTrue( files.FileExists( path ), $"{path} is found" );
				CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, files.ReadAllBytes( path ), path );
			}
		}
		finally
		{
			System.IO.Directory.Delete( root, recursive: true );
		}
	}

	[TestMethod]
	public void BranchesAppearAndDeeperOnesAreNarrower()
	{
		var branched = Enumerable.Range( 1, 40 )
			.SelectMany( seed => WeatherVisualState.BuildBolt( new NVector3( 0, 300, 0 ), new NVector3( 0, 0, 0 ), (ulong)seed ) )
			.Where( segment => segment.Depth > 0 ).ToList();
		Assert.IsTrue( branched.Count > 0, "9/512 of the splits branch" );
		Assert.AreEqual( 1.3f, WeatherVisualState.BoltWidth( 0 ), 1e-6f );
		Assert.AreEqual( 0.65f, WeatherVisualState.BoltWidth( 1 ), 1e-6f );
		Assert.AreEqual( 1.3f / 5, WeatherVisualState.BoltWidth( 4 ), 1e-6f );
	}

	[TestMethod]
	public void BoltsLiveForTheirLifetimeUpTo16AndAreFrozenWhenTheStepIsZero()
	{
		var state = new WeatherVisualState();
		var source = new Source { BoltLifetimeSeconds = 2f };
		for ( var strike = 0; strike < WeatherVisualState.MaximumBolts + 3; strike++ )
			source.Tops.Add( new NVector3( strike, 300, strike ) );
		state.Update( 0f, source, Eye );
		Assert.AreEqual( WeatherVisualState.MaximumBolts, state.Bolts.Count, "at most 16 live bolts" );
		Assert.IsTrue( state.Bolts[0].Segments.Any( segment => segment.A == new NVector3( 3, 300, 3 ) || segment.B == new NVector3( 3, 300, 3 ) ), "the oldest three were replaced" );
		Assert.AreEqual( 0, source.Tops.Count, "the tops were taken" );
		state.Update( 0f, new Source(), Eye );
		Assert.AreEqual( WeatherVisualState.MaximumBolts, state.Bolts.Count, "a paused park keeps its bolts" );
		state.Update( 1.9f, new Source(), Eye );
		Assert.AreEqual( WeatherVisualState.MaximumBolts, state.Bolts.Count );
		state.Update( 0.2f, new Source(), Eye );
		Assert.AreEqual( 0, state.Bolts.Count, "the 2000 ms lifetime expires them" );
	}

	[TestMethod]
	public void TheBoltColourFollowsTheOriginalsRemainingTimeCurve()
	{
		var bolt = new WeatherVisualState().AddBolt( new NVector3( 0, 300, 0 ), 2f );
		bolt.Flicker = 0;
		// remaining 2000 ms: x = 0, 255 - trunc(0.9 x 1.11 x 64) = 192
		bolt.Age = 0f;
		Assert.AreEqual( 192f / 255f, WeatherVisualState.BoltGrey( bolt ), 1e-6f );
		// remaining 1750 ms: x = 0.5, 255 - trunc(0.4 x 71.04) = 227
		bolt.Age = 0.25f;
		Assert.AreEqual( 227f / 255f, WeatherVisualState.BoltGrey( bolt ), 1e-6f );
		// remaining 1550 ms: x = 0.1, 255 - trunc(0.8 x 71.04) = 199; the cycle repeats every 500 ms
		bolt.Age = 0.45f;
		var low = WeatherVisualState.BoltGrey( bolt );
		bolt.Age = 0.95f;
		Assert.AreEqual( low, WeatherVisualState.BoltGrey( bolt ), 1e-6f );
		// from x = 0.9 up the base is 192 + trunc((1 - x) x 640)
		bolt.Age = 0.04f;
		Assert.AreEqual( (192 + 51) / 255f, WeatherVisualState.BoltGrey( bolt ), 1e-6f, "x = 0.92: 192 + trunc(0.08 x 640)" );
	}

	[TestMethod]
	public void TheBoltFlickerWrapsAboveTwoFiftyFiveAsTheOriginalsOrIntoTheChannelsDoes()
	{
		var bolt = new WeatherVisualState().AddBolt( new NVector3( 0, 300, 0 ), 2f );
		bolt.Age = 0.25f;
		bolt.Flicker = 20;
		Assert.AreEqual( 247f / 255f, WeatherVisualState.BoltGrey( bolt ), 1e-6f, "227 + 20 still fits" );
		bolt.Flicker = 40;
		Assert.AreEqual( 11f / 255f, WeatherVisualState.BoltGrey( bolt ), 1e-6f, "227 + 40 = 267 keeps only its low byte: ((v | 0xFFFFFF00) << 8 | v) << 8 | v loses bit 8" );
	}

	// ---- the simulation side the renderer drains ----

	/// <summary>The jungle's weather band with lightning 0..15 and rain 0..40 (the shipped keys).</summary>
	private static SamSettings Jungle() => new( new[] { SamDocument.Parse(
		"WeatherEffects.QualityForSnowLow\t-1\n" +
		"WeatherEffects.QualityForSnowHigh\t-1\n" +
		"WeatherEffects.QualityForRainLow\t0\n" +
		"WeatherEffects.QualityForRainHigh\t40\n" +
		"WeatherEffects.QualityForLightningLow\t0\n" +
		"WeatherEffects.QualityForLightningHigh\t15\n" +
		"WeatherEffects.LightningTime\t2000\n" +
		"WeatherEffects.MaxRaindrops\t600\n", "test" ) } );

	[TestMethod]
	public void TheParkSourceReadsTheSimulatedDropsAndTheBoltLifetime()
	{
		var weather = new ParkWeather( Jungle(), 10, 10, 10 );
		var source = new ParkWeatherVisualSource( weather );
		Assert.AreEqual( 2f, source.BoltLifetimeSeconds, "LightningTime 2000 ms" );
		Assert.AreEqual( 600, weather.MaxRaindrops );
		Assert.AreEqual( 0, source.DropCount );
	}

	[TestMethod]
	public void StrikesReportTheirBoltTopsOnceAtHeight300()
	{
		var weather = new ParkWeather( Jungle(), 11, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.ApplyQuality( 5 );
		Assert.IsTrue( weather.Lightning );
		weather.Step( 0 );
		var tops = weather.TakeBoltTops();
		Assert.AreEqual( 1, tops.Count, "the strike reports its bolt top once" );
		Assert.AreEqual( ParkWeather.BoltTopHeight, tops[0].Y, "the bolt's top is the thunder's sky point (x, 300, z)" );
		Assert.AreEqual( 0, weather.TakeBoltTops().Count, "the second take draws nothing new" );
	}

	[TestMethod]
	public void AStrikesBoltRunsFromItsGroundPointToASkyPointOffsetByUpTo50()
	{
		// 0x512C50: the ground point is (x, 0, z) from the strike's own draws, the sky point (x + 100u - 50, 300, z + 100u' - 50).
		var weather = new ParkWeather( Jungle(), 11, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.ApplyQuality( 5 );
		weather.Step( 0 );
		var points = new List<(System.Numerics.Vector3 Top, System.Numerics.Vector3 Ground)>();
		weather.DrainStrikePoints( points );
		Assert.AreEqual( 1, points.Count );
		var (top, ground) = points[0];
		Assert.AreEqual( 0f, ground.Y, "the ground point is at y = 0" );
		Assert.AreEqual( ParkWeather.BoltTopHeight, top.Y );
		Assert.IsTrue( MathF.Abs( top.X - ground.X ) <= 50 && MathF.Abs( top.Z - ground.Z ) <= 50, "the sky point lies within 50 of the ground point on x and z" );
		Assert.IsTrue( ground.X >= 1 && ground.Z >= 1, "the ground point is clamped to at least 1" );
		var source = new ParkWeatherVisualSource( weather );
		Assert.AreEqual( 0, source.TakeStrikes().Count, "the strike was drained" );
	}

	[TestMethod]
	public void StrikesOutsideTheLightningBandReportNoBolt()
	{
		var weather = new ParkWeather( Jungle(), 12, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.ApplyQuality( 50 );
		Assert.IsFalse( weather.Lightning );
		for ( var step = 0; step < 5; step++ )
			weather.Step( 0 );
		Assert.AreEqual( 0, weather.TakeBoltTops().Count );
	}

	[TestMethod]
	public void ARestoredWeatherStartsWithNoPendingBoltTops()
	{
		var weather = new ParkWeather( Jungle(), 13, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.ApplyQuality( 5 );
		weather.Step( 0 );
		Assert.AreEqual( 1, weather.TakeBoltTops().Count );
		var snapshot = weather.CaptureSnapshot();
		var restored = new ParkWeather( Jungle(), 13, 10, 10 );
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( 0, restored.TakeBoltTops().Count, "the visual bolt queue is presentation-only and starts empty" );
	}

	[TestMethod]
	public void ACreatedWeatherAnnouncesItselfForTheRenderer()
	{
		var created = 0;
		ParkWeather.Created += Created;
		try
		{
			var weather = new ParkWeather( Jungle(), 14, 10, 10 );
			Assert.AreEqual( 1, created, "the weather announces itself exactly once, at construction" );
			weather.AdvanceTo( 0 );
			Assert.AreEqual( 1, created, "stepping does not announce it again" );
		}
		finally
		{
			ParkWeather.Created -= Created;
		}
		void Created( ParkWeather weather ) => created++;
	}
}
