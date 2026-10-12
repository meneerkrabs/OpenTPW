using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park's weather (the weather thing's quality, its bands, the drops ramp, the forecast's seven-day cycle and the
/// lightning and thunder countdowns). The synthetic tests set the keys directly; the original-data tests read the shipped
/// <c>Standard.sam</c> layers.
/// </summary>
[TestClass]
public class ParkWeatherTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static SamSettings Keys( string text ) =>
		new( new[] { SamDocument.Parse( text, "test" ) } );

	/// <summary>The jungle's weather bands with the shipped cycle (rain 0..40, lightning 0..15, no snow).</summary>
	private static SamSettings Jungle() => Keys(
		"WeatherEffects.QualityForSnowLow\t-1\n" +
		"WeatherEffects.QualityForSnowHigh\t-1\n" +
		"WeatherEffects.QualityForRainLow\t0\n" +
		"WeatherEffects.QualityForRainHigh\t40\n" +
		"WeatherEffects.QualityForLightningLow\t0\n" +
		"WeatherEffects.QualityForLightningHigh\t15\n" +
		"WeatherEffects.MaxRaindrops\t600\n" +
		"Weather.DaysOfWarning\t4\n" +
		"Weather.DaysBetweenChanges\t7\n" +
		"Weather.SpeedOfChange\t20\n" );

	[TestMethod]
	public void SnowHasDropsToo()
	{
		// 0x103a90..0x103afc: the snow band computes its drops target like the rain band, from its own range; the ramp sends the
		// drop-based level in any band (0x103394..0x1033a4).
		var keys = Keys( "WeatherEffects.QualityForSnowLow\t60\nWeatherEffects.QualityForSnowHigh\t100\nWeatherEffects.QualityForRainLow\t-1\nWeatherEffects.QualityForRainHigh\t-1\n" +
			"WeatherEffects.MaxRaindrops\t600\nWeather.SpeedOfChange\t20\n" );
		var weather = new ParkWeather( keys, 1, 10, 10 );
		weather.ApplyQuality( 70 );
		Assert.IsTrue( weather.Snow && !weather.Rain );
		Assert.AreEqual( 450, weather.TargetDrops, "intensity 25: (100 − 25) × 600 / 100" );
		weather.Step( 0 );
		Assert.AreEqual( 20, weather.CurrentDrops, "snow drops ramp like rain's (the word the arrival factor reads)" );
		Assert.AreEqual( 20 * 100 / 600, weather.RainLevel, "the level follows the drops in any band" );
		weather.ApplyQuality( 30 );
		Assert.IsFalse( weather.Snow || weather.Rain );
		Assert.AreEqual( 0, weather.TargetDrops );
	}

	[TestMethod]
	public void TheConverterSetsTheRainTargetFromTheQualityBand()
	{
		var weather = new ParkWeather( Jungle(), 1, 10, 10 );
		weather.ApplyQuality( 1 );
		Assert.IsTrue( weather.Rain );
		Assert.AreEqual( 588, weather.TargetDrops, "(100 − 2) × 600 / 100" );
		weather.ApplyQuality( 20 );
		Assert.AreEqual( 300, weather.TargetDrops, "intensity 50 gives half the cap" );
		weather.ApplyQuality( 40 );
		Assert.AreEqual( 0, weather.TargetDrops, "the top of the band is dry" );
		weather.ApplyQuality( 78 );
		Assert.IsFalse( weather.Rain, "jungle's shipped quality 78 is dry" );
		Assert.IsFalse( weather.Lightning );
		Assert.AreEqual( 0, weather.TargetDrops );
	}

	[TestMethod]
	public void TheLightningBandIsAStrictRangeOfTheQuality()
	{
		var weather = new ParkWeather( Jungle(), 2, 10, 10 );
		weather.ApplyQuality( 15 );
		Assert.IsTrue( weather.Lightning, "15 is in the 0..15 band" );
		weather.ApplyQuality( 16 );
		Assert.IsFalse( weather.Lightning );
	}

	[TestMethod]
	public void TheDropsRampInStepsAndSettleOneStepShort()
	{
		var weather = new ParkWeather( Jungle(), 3, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.ApplyQuality( 1 );
		// 588 with steps of 20: the ramp moves 29 times to 580 and stops, since 8 is less than one step.
		for ( var step = 0; step < 40; step++ )
			weather.Step( 0 );
		Assert.AreEqual( 580, weather.CurrentDrops );
		Assert.AreEqual( 580 * 100 / 600, weather.RainLevel, "the rain's level is the drops over the cap" );
	}

	[TestMethod]
	public void TheForecastComesThreeDaysInAndAppliesAfterFourMoreWithASevenDayCycle()
	{
		var weather = new ParkWeather( Jungle(), 4, 10, 10 );
		weather.AdvanceTo( 0 );
		Assert.IsNull( weather.Forecast, "nothing is forecast on the first day" );
		var first = weather.Quality;
		weather.Step( ParkCalendar.TickAtDay( 2 ) );
		Assert.IsNull( weather.Forecast, "day 2 is before the roll" );
		weather.Step( ParkCalendar.TickAtDay( 3 ) );
		Assert.IsNotNull( weather.Forecast, "day 3 rolls the next quality" );
		Assert.AreEqual( first, weather.Quality, "and does not apply it yet" );
		var coming = weather.Forecast!.Value;
		weather.Step( ParkCalendar.TickAtDay( 6 ) );
		Assert.IsNotNull( weather.Forecast, "day 6 is still warning" );
		weather.Step( ParkCalendar.TickAtDay( 7 ) );
		Assert.IsNull( weather.Forecast, "day 7 applies it" );
		Assert.AreEqual( coming, weather.Quality );
	}

	[TestMethod]
	public void EachStrikeRingsThunderAtItsTopAfterTheCountdown()
	{
		var weather = new ParkWeather( Jungle(), 5, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.ApplyQuality( 5 );
		Assert.IsTrue( weather.Lightning );
		var rang = 0;
		for ( var step = 0; step < 13; step++ )
		{
			weather.Step( 0 );
			rang += weather.TakeThunder().Count;
		}
		Assert.AreEqual( 1, rang, "the first strike's thunder comes ten or eleven weather steps later" );
	}

	// ---- original data (OPENTPW_GAME_PATH) ----

	private static string? DataDirectory()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			return null;
		return Directory.EnumerateDirectories( path ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path;
	}

	private static SamSettings Shipped( BaseFileSystem fileSystem, string theme ) => new(
		BalanceSettings.GetStandardLayers( theme, easy: false )
			.Where( path => fileSystem.FileExists( path ) )
			.Select( path => SamDocument.Parse( fileSystem.ReadAllText( path ), path ) ) );

	[TestMethod]
	public void TheShippedWeatherKeysAreTheOnesTheSimReads()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var fileSystem = new BaseFileSystem( data );
		var jungle = new ParkWeather( Shipped( fileSystem, "jungle" ), 6, 10, 10 );
		jungle.ApplyQuality( 78 );
		Assert.IsFalse( jungle.Rain, "jungle's quality 78 is dry" );
		jungle.ApplyQuality( 1 );
		Assert.AreEqual( 588, jungle.TargetDrops, "the global MaxRaindrops of 600 reaches the target" );
		Assert.IsTrue( Shipped( fileSystem, "jungle" ).Contains( "Weather.DaysBetweenChanges" ), "the cycle is the shipped one" );
	}

	[TestMethod]
	public void HallowSnowsAndRainsInItsOverlapAndSpaceNeverRains()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var fileSystem = new BaseFileSystem( data );
		// Hallow's snow band 0..25 and rain band 15..45 overlap at 15..25: a coin decides, so one of snow and rain holds, never both.
		var hallow = new ParkWeather( Shipped( fileSystem, "hallow" ), 7, 10, 10 );
		for ( var quality = 15; quality <= 25; quality++ )
		{
			hallow.ApplyQuality( quality );
			Assert.IsFalse( hallow.Snow && hallow.Rain, $"quality {quality}" );
			Assert.IsTrue( hallow.Snow || hallow.Rain, $"quality {quality} is snow or rain" );
		}
		var space = new ParkWeather( Shipped( fileSystem, "space" ), 8, 10, 10 );
		for ( var quality = 1; quality <= 100; quality++ )
		{
			space.ApplyQuality( quality );
			Assert.IsFalse( space.Rain, $"space quality {quality}" );
		}
	}
}
