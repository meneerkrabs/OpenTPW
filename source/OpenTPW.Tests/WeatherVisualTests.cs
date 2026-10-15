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
			var trailing = (vertices[quad].Position + vertices[quad + 1].Position) / 2;
			var leading = (vertices[quad + 2].Position + vertices[quad + 4].Position) / 2;
			Assert.AreEqual( WeatherVisualState.StreakLength, (leading - trailing).Length(), 1e-4f, "the streak is 0.1 long" );
			Assert.AreEqual( -WeatherVisualState.StreakLength, leading.Y - trailing.Y, 1e-4f, "it points down" );
			Assert.AreEqual( 128f / 255f, vertices[quad].Alpha, 1e-6f, "trailing alpha 0x80" );
			Assert.AreEqual( 1f, vertices[quad + 2].Alpha, 1e-6f, "leading alpha 0xFF" );
			Assert.AreEqual( WeatherVisualState.StreakWidthRatio * WeatherVisualState.StreakLength, (vertices[quad].Position - vertices[quad + 1].Position).Length(), 1e-4f, "0.2 x length wide" );
			Assert.IsTrue( vertices[quad].Position.Z > 0 && vertices[quad + 2].Position.Z > 0, "only depth > 0 is drawn" );
			Assert.AreEqual( WeatherVisualState.RainGrey( vertices[quad].Position.Z ), vertices[quad].Grey, 1e-6f );
		}
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
		// [BIN:TPW-EXE:0x57f490] one triangle a flake: the apex at the top middle of the quadrant, the base at its lower corners
		for ( var flake = 0; flake < count; flake += 3 )
		{
			var (apex, right, left) = (vertices[flake], vertices[flake + 1], vertices[flake + 2]);
			Assert.AreEqual( 1f, apex.Grey, "full white" );
			Assert.AreEqual( 1f, apex.Alpha );
			Assert.AreEqual( 2 * WeatherVisualState.SnowHalfSize, (right.Position - left.Position).X, 1e-5f, "base 2h wide" );
			Assert.AreEqual( 2 * WeatherVisualState.SnowHalfSize, apex.Position.Y - left.Position.Y, 1e-5f, "2h tall" );
			Assert.AreEqual( (right.Position.X + left.Position.X) / 2, apex.Position.X, 1e-5f, "the apex above the base's middle" );
			var corner = left.TexCoords;
			Assert.IsTrue( corner is { X: 0f or 0.5f, Y: 0.5f or 1f }, "the lower left corner of an atlas quadrant" );
			Assert.AreEqual( new System.Numerics.Vector2( corner.X + 0.5f, corner.Y ), right.TexCoords );
			Assert.AreEqual( new System.Numerics.Vector2( corner.X + 0.25f, corner.Y - 0.5f ), apex.TexCoords );
			Assert.IsTrue( apex.Position.Z > 0 );
		}
		var first = vertices[0];
		state.Update( 0.016f, new Source { DropCount = 8, Snow = true }, Eye );
		var again = new WeatherVertex[WeatherVisualState.FieldSize * 6];
		state.WriteSnow( Eye, again );
		Assert.AreNotEqual( first.Position, again[0].Position, "a flake moves each frame" );
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
			var centre = new NVector3( vertices[quad * 3].Position.X, (vertices[quad * 3].Position.Y + vertices[quad * 3 + 1].Position.Y) / 2, vertices[quad * 3].Position.Z );
			var spread = 1f + offset.Z / WeatherVisualState.SnowSpreadDepth;
			Assert.AreEqual( offset.X * spread, centre.X, 0.03f, $"flake {index} x" );
			Assert.AreEqual( offset.Y * spread, centre.Y, 0.03f, $"flake {index} y" );
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
		Assert.AreEqual( top, first[0].A, "the trunk starts at the top" );
		Assert.IsTrue( first.Count > 64 && first.Count <= WeatherVisualState.MaximumBoltSegments, $"segments of 2 to 3 units over 300 units: {first.Count}" );
		var trunk = first.Where( segment => segment.Depth == 0 ).ToList();
		Assert.AreEqual( bottom, trunk[^1].B, "the trunk ends at the ground point" );
		for ( var index = 0; index + 1 < trunk.Count; index++ )
			Assert.AreEqual( trunk[index].B, trunk[index + 1].A, "the trunk is one connected path" );
		var lengths = trunk.Select( segment => (segment.B - segment.A).Length() ).ToList();
		Assert.IsTrue( lengths.Max() < 3.5f, $"a segment stops splitting under squared length 4..8: {lengths.Max()}" );
		Assert.IsTrue( first.All( segment => segment.Depth <= WeatherVisualState.MaximumBranchDepth ) );
	}

	[TestMethod]
	public void NoBoltPassesTheSegmentCapWhateverTheSeed()
	{
		var most = 0;
		for ( ulong seed = 1; seed <= 3000; seed++ )
		{
			var count = WeatherVisualState.BuildBolt( new NVector3( 0, 300, 0 ), new NVector3( 0, 0, 0 ), seed ).Count;
			Assert.IsTrue( count <= WeatherVisualState.MaximumBoltSegments, $"seed {seed} made {count} segments" );
			most = Math.Max( most, count );
		}
		Assert.IsTrue( most > 100, "ordinary bolts still reach their natural length" );
		var capped = WeatherVisualState.BuildBolt( new NVector3( 0, 300, 0 ), new NVector3( 0, 0, 0 ), 576 );
		Assert.IsTrue( capped.Count <= WeatherVisualState.MaximumBoltSegments );
		Assert.AreEqual( new NVector3( 0, 0, 0 ), capped.Where( segment => segment.Depth == 0 ).Last().B, "a capped bolt still reaches the ground" );
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
		Assert.AreEqual( 3f, state.Bolts[0].Segments[0].A.X, "the oldest three were replaced" );
		Assert.AreEqual( 0, source.Tops.Count, "the tops were taken" );
		state.Update( 0f, new Source(), Eye );
		Assert.AreEqual( WeatherVisualState.MaximumBolts, state.Bolts.Count, "a paused park keeps its bolts" );
		state.Update( 1.9f, new Source(), Eye );
		Assert.AreEqual( WeatherVisualState.MaximumBolts, state.Bolts.Count );
		state.Update( 0.2f, new Source(), Eye );
		Assert.AreEqual( 0, state.Bolts.Count, "the 2000 ms lifetime expires them" );
	}

	[TestMethod]
	public void TheBoltColourPulsesInHalfSecondCyclesBetween192And255()
	{
		var bolt = new WeatherVisualState().AddBolt( new NVector3( 0, 300, 0 ), 2f );
		var seen = new List<float>();
		for ( var step = 0; step <= 20; step++ )
		{
			bolt.Age = step * 0.025f;
			bolt.Flicker = 0;
			seen.Add( WeatherVisualState.BoltGrey( bolt ) );
		}
		Assert.IsTrue( seen.All( grey => grey >= 192f / 255f - 1e-6f && grey <= 1f ), "the base stays within 192..255" );
		Assert.IsTrue( seen.Max() - seen.Min() > 0.2f, "it pulses" );
		bolt.Age = 0.1f;
		bolt.Flicker = 0;
		var low = WeatherVisualState.BoltGrey( bolt );
		bolt.Age = 0.6f;
		Assert.AreEqual( low, WeatherVisualState.BoltGrey( bolt ), 1e-5f, "the cycle repeats every 500 ms" );
		bolt.Flicker = 63;
		Assert.IsTrue( WeatherVisualState.BoltGrey( bolt ) <= 1f, "the flicker is capped at 255" );
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
