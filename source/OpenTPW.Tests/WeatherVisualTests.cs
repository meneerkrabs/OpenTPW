using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The weather's visual presentation (docs/AUDIO.md "Weather visuals"): the drop field that follows the
/// simulated current drops, the wrapping camera-follow volume, snow's slower sway, the bolt flash's lifetime
/// and jittered polyline, and the strikes the simulation reports for drawing.
/// </summary>
[TestClass]
public class WeatherVisualTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static WeatherVisualState Field( int drops, bool snow = false, Vector3? centre = null )
	{
		var state = new WeatherVisualState( 1 );
		state.Step( 0.016f, drops, snow, centre ?? Vector3.Zero );
		return state;
	}

	[TestMethod]
	public void TheDropCountFollowsTheSimulationAndStopsAtTheCap()
	{
		var state = new WeatherVisualState( 1 );
		state.Step( 0.016f, 300, snow: false, Vector3.Zero );
		Assert.AreEqual( 300, state.DropCount );
		state.Step( 0.016f, 0, snow: false, Vector3.Zero );
		Assert.AreEqual( 0, state.DropCount, "the weather drying up drops the field" );
		state.Step( 0.016f, WeatherVisualState.MaximumDrops + 500, snow: false, Vector3.Zero );
		Assert.AreEqual( WeatherVisualState.MaximumDrops, state.DropCount, "a level with more simulated drops than the visual cap draws the cap" );
	}

	[TestMethod]
	public void TheFieldIsDeterministicForASeed()
	{
		var first = new WeatherVisualState( 42 );
		var second = new WeatherVisualState( 42 );
		first.Step( 0.016f, 100, snow: false, Vector3.Zero );
		second.Step( 0.016f, 100, snow: false, Vector3.Zero );
		CollectionAssert.AreEqual( first.Drops.ToList(), second.Drops.ToList() );
		var other = new WeatherVisualState( 43 );
		other.Step( 0.016f, 100, snow: false, Vector3.Zero );
		Assert.AreNotEqual( first.Drops[0], other.Drops[0], "another seed draws another field" );
	}

	[TestMethod]
	public void DropsWrapInsideTheVolumeAroundTheMovingCentre()
	{
		var state = Field( WeatherVisualState.MaximumDrops );
		var centre = new Vector3( 0, 0, 0 );
		// Five seconds of falling in frame slices while the view target slides, as a panning camera would move it.
		for ( var slice = 0; slice < 300; slice++ )
		{
			centre = new Vector3( slice * 0.5f, -slice * 0.25f, 0 );
			state.Step( 1f / 60f, WeatherVisualState.MaximumDrops, snow: false, centre );
		}
		foreach ( var drop in state.Drops )
		{
			Assert.IsTrue( Math.Abs( drop.Position.X - centre.X ) <= WeatherVisualState.VolumeRadius + 0.001f, $"drop X {drop.Position.X} left the volume" );
			Assert.IsTrue( Math.Abs( drop.Position.Y - centre.Y ) <= WeatherVisualState.VolumeRadius + 0.001f, $"drop Y {drop.Position.Y} left the volume" );
			var bottom = centre.Z - WeatherVisualState.VolumeBottomOffset;
			Assert.IsTrue( drop.Position.Z >= bottom - 0.001f && drop.Position.Z <= bottom + WeatherVisualState.VolumeHeight + 0.001f, $"drop Z {drop.Position.Z} left the volume" );
		}
	}

	[TestMethod]
	public void APausedParkFreezesTheVisualsWhereTheyAre()
	{
		var state = Field( 200 );
		var before = state.Drops.ToList();
		state.Step( 0f, 200, snow: false, Vector3.Zero );
		CollectionAssert.AreEqual( before, state.Drops.ToList(), "a delta of zero moves nothing" );
		state.Step( -1f, 200, snow: false, Vector3.Zero );
		CollectionAssert.AreEqual( before, state.Drops.ToList(), "a negative delta moves nothing" );
	}

	[TestMethod]
	public void SnowFallsSlowerThanRainAndSwaysSideways()
	{
		var rain = Field( 1, snow: false );
		var snow = Field( 1, snow: true );
		var rainDrop = rain.Drops[0].Position;
		var snowDrop = snow.Drops[0].Position;
		rain.Step( 0.001f, 1, snow: false, Vector3.Zero );
		var phase = snow.Drops[0].Phase;
		var elapsed = snow.Elapsed;
		snow.Step( 0.001f, 1, snow: true, Vector3.Zero );
		var rainFall = rainDrop.Z - rain.Drops[0].Position.Z;
		var snowFall = snowDrop.Z - snow.Drops[0].Position.Z;
		Assert.AreEqual( WeatherVisualState.RainFallSpeed * 0.001f, rainFall, 1e-4f );
		Assert.AreEqual( WeatherVisualState.SnowFallSpeed * 0.001f, snowFall, 1e-4f );
		Assert.IsTrue( snowFall < rainFall, "snow drifts down slower than rain streaks" );
		var omega = 2f * MathF.PI / WeatherVisualState.SnowSwaySeconds;
		var expectedSway = MathF.Cos( phase + (elapsed + 0.001f) * omega ) * WeatherVisualState.SnowSwaySpeed * 0.001f;
		Assert.AreEqual( expectedSway, snow.Drops[0].Position.X - snowDrop.X, 1e-4f, "the flake sways sideways at its phase's speed" );
		Assert.AreEqual( 0f, rain.Drops[0].Position.X - rainDrop.X, 1e-4f, "rain falls straight down" );
	}

	[TestMethod]
	public void BoltFlashesFadeAfterTheirLifetimeAndOldOnesAreDropped()
	{
		var state = new WeatherVisualState( 1 );
		for ( var flash = 0; flash < WeatherVisualState.MaximumBoltFlashes + 2; flash++ )
			state.AddBoltFlash( new Vector3( flash, 300, flash ), new Vector3( flash, 0, flash ) );
		Assert.AreEqual( WeatherVisualState.MaximumBoltFlashes, state.Flashes.Count, "only the newest flashes are kept" );
		Assert.AreEqual( 2, state.Flashes[0].Strike, "the oldest two strikes were dropped" );
		Assert.AreEqual( 5, state.Flashes[^1].Strike, "the newest strike is last" );
		var newest = state.Flashes[^1];
		state.Step( WeatherVisualState.BoltFlashSeconds, 0, snow: false, Vector3.Zero );
		Assert.AreEqual( 0, state.Flashes.Count, "the flash lifetime expires" );
		Assert.AreEqual( new Vector3( 5, 300, 5 ), new Vector3( newest.Top.X, newest.Top.Y, newest.Top.Z ), "the kept flash was the newest strike's" );
	}

	[TestMethod]
	public void TheBoltPolylineIsDeterministicAndBounded()
	{
		// Engine coordinates: Z up, so the bolt's top is the high Z and the ground the low one.
		var state = new WeatherVisualState( 1 );
		state.AddBoltFlash( new Vector3( 30, 40, 60 ), new Vector3( 30, 40, 0 ) );
		state.AddBoltFlash( new Vector3( 30, 40, 60 ), new Vector3( 30, 40, 0 ) );
		var first = state.BoltPoints( state.Flashes[0] );
		var second = state.BoltPoints( state.Flashes[0] );
		var otherStrike = state.BoltPoints( state.Flashes[1] );
		CollectionAssert.AreEqual( first.ToList(), second.ToList(), "the same strike always draws the same bolt" );
		Assert.AreNotEqual( first[3], otherStrike[3], "another strike jitters differently" );
		Assert.AreEqual( WeatherVisualState.BoltPointCount, first.Length );
		Assert.AreEqual( new Vector3( 30, 40, 60 ), new Vector3( first[0].X, first[0].Y, first[0].Z ), "the polyline starts at the top" );
		Assert.AreEqual( new Vector3( 30, 40, 0 ), new Vector3( first[^1].X, first[^1].Y, first[^1].Z ), "the polyline ends at the ground" );
		for ( var index = 1; index < first.Length - 1; index++ )
		{
			Assert.IsTrue( first[index].Z > first[^1].Z && first[index].Z < first[0].Z, "the between points stay between the endpoints" );
			Assert.IsTrue( Math.Abs( first[index].X - 30 ) <= WeatherVisualState.BoltJitter && Math.Abs( first[index].Y - 40 ) <= WeatherVisualState.BoltJitter,
				"the jitter stays within its amplitude" );
		}
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
		"WeatherEffects.MaxRaindrops\t600\n", "test" ) } );

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
