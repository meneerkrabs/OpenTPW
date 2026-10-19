using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Wintry Decembers (docs/WINTER.md, "Wintry Decembers"; <c>opentpw.winter-weather</c>, off by default): the thunderstorms of a December weather roll become snow in a park with no snow band of its own (jungle, fantasy). Headless
/// weather over the jungle's shipped bands, no original game data; the Halloween park and a park with no rain band are checked for being untouched.
/// </summary>
// [EXT:winter-weather] OpenTPW's own option; tp.exe's converter (0x512f00) reads only the quality and its three bands, with no calendar input, so the month is the option's own addition
[TestClass]
public class WinterWeatherTests
{
	private static SamSettings Keys( string text ) => new( new[] { SamDocument.Parse( text, "test" ) } );

	internal static SamSettings Hallow() => Keys(
		"WeatherEffects.QualityForSnowLow\t0\nWeatherEffects.QualityForSnowHigh\t25\nWeatherEffects.QualityForRainLow\t15\nWeatherEffects.QualityForRainHigh\t45\n" +
		"WeatherEffects.QualityForLightningLow\t0\nWeatherEffects.QualityForLightningHigh\t25\nWeatherEffects.MaxRaindrops\t600\nWeather.DaysOfWarning\t4\nWeather.DaysBetweenChanges\t7\nWeather.SpeedOfChange\t20\n" );

	internal static SamSettings Space() => Keys(
		"WeatherEffects.QualityForSnowLow\t-1\nWeatherEffects.QualityForSnowHigh\t-1\nWeatherEffects.QualityForRainLow\t-1\nWeatherEffects.QualityForRainHigh\t-1\n" +
		"WeatherEffects.QualityForLightningLow\t0\nWeatherEffects.QualityForLightningHigh\t15\nWeatherEffects.MaxRaindrops\t600\nWeather.DaysOfWarning\t4\nWeather.DaysBetweenChanges\t7\nWeather.SpeedOfChange\t20\n" );

	// ---- The converter ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void AWintryDecemberRollIsSnowWithoutLightningAndWithTheStormsDrops()
	{
		var storm = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 3, 10, 10 );
		storm.ApplyQuality( 5, wintryDecember: true );
		Assert.IsFalse( storm.Snow, "the option is off: the converter does not know the month" );
		Assert.IsTrue( storm.Rain && storm.Lightning );
		var wintry = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 3, 10, 10, wintryDecembers: true );
		wintry.ApplyQuality( 5, wintryDecember: true );
		Assert.IsTrue( wintry.Snow, "a December roll in the rain band snows" );
		Assert.IsFalse( wintry.Rain, "snow and rain never hold together" );
		Assert.IsFalse( wintry.Lightning, "the thunderstorm became a snowfall: no lightning" );
		Assert.AreEqual( storm.TargetDrops, wintry.TargetDrops, "the drops are the rain band's, so the arrival factor is the storm's" );
		Assert.AreEqual( 5, wintry.Quality );
		// a December quality outside the rain band stays dry
		wintry.ApplyQuality( 78, wintryDecember: true );
		Assert.IsFalse( wintry.Snow || wintry.Rain || wintry.Lightning );
		Assert.AreEqual( 0, wintry.TargetDrops );
	}

	[TestMethod]
	public void OtherMonthsAndAnOptionThatIsOffKeepTheStorm()
	{
		var wintry = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 3, 10, 10, wintryDecembers: true );
		wintry.ApplyQuality( 5 );
		Assert.IsTrue( wintry.Rain && wintry.Lightning && !wintry.Snow, "not a December roll: the original's storm" );
		var off = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 3, 10, 10 );
		off.ApplyQuality( 5, wintryDecember: true );
		Assert.IsTrue( off.Rain && off.Lightning && !off.Snow, "option off: the original's storm even in December" );
	}

	[TestMethod]
	public void TheHalloweenParkAndAParkWithNoRainBandAreUntouched()
	{
		var plainHallow = new ParkWeather( Hallow(), 4, 10, 10 );
		var wintryHallow = new ParkWeather( Hallow(), 4, 10, 10, wintryDecembers: true );
		for ( var quality = 1; quality <= 100; quality++ )
		{
			plainHallow.ApplyQuality( quality );
			wintryHallow.ApplyQuality( quality, wintryDecember: true );
			Assert.AreEqual( (plainHallow.Snow, plainHallow.Rain, plainHallow.Lightning, plainHallow.TargetDrops), (wintryHallow.Snow, wintryHallow.Rain, wintryHallow.Lightning, wintryHallow.TargetDrops), $"hallow quality {quality}: it snows already, the option changes nothing" );
		}
		var space = new ParkWeather( Space(), 5, 10, 10, wintryDecembers: true );
		for ( var quality = 1; quality <= 100; quality++ )
		{
			space.ApplyQuality( quality, wintryDecember: true );
			Assert.IsFalse( space.Snow || space.Rain, $"space quality {quality}: no precipitation, December or not" );
			Assert.AreEqual( quality <= 15, space.Lightning, $"space quality {quality}: dry lightning as before" );
		}
	}

	[TestMethod]
	public void TheUmbrellasAndTheRainMoodReadSnowInsteadOfAStorm()
	{
		var storm = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 3, 10, 10 );
		storm.ApplyQuality( 5 );
		Assert.AreEqual( UmbrellaRules.WeatherStorm, UmbrellaRules.WeatherStateOf( storm.Rain, storm.Lightning, storm.Snow ), "the original's December: a thunderstorm" );
		var wintry = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 3, 10, 10, wintryDecembers: true );
		wintry.ApplyQuality( 5, wintryDecember: true );
		Assert.AreEqual( UmbrellaRules.WeatherSnow, UmbrellaRules.WeatherStateOf( wintry.Rain, wintry.Lightning, wintry.Snow ), "the wintry December: the shared weather map reads snow, which the umbrella rules, the rain mood (kid snow +1) and the winter shops see" );
		var deltas = RainMoodSettings.Default.Kid.Happiness;
		Assert.AreEqual( (-1f, 1f), (deltas.Of( UmbrellaRules.WeatherStorm ), deltas.Of( UmbrellaRules.WeatherSnow )), "and the rain mood's happiness row is -1 in a storm and +1 in snow" );
	}

	[TestMethod]
	public void AHalloweenSnowfallIsSnowForTheWinterShopsAndAStormForTheUmbrellasAndTheRainMood()
	{
		// Halloween's snow band (0..25) is its lightning band, so every snowfall there has lightning too
		var hallow = new ParkWeather( Hallow(), 4, 10, 10, wintryDecembers: true );
		hallow.ApplyQuality( 10, wintryDecember: true );
		Assert.IsTrue( hallow.Snow && hallow.Lightning, "a Halloween snowfall flashes lightning" );
		Assert.AreEqual( UmbrellaRules.WeatherStorm, UmbrellaRules.WeatherStateOf( hallow.Rain, hallow.Lightning, hallow.Snow ), "the umbrellas and the rain mood keep reading a storm (no existing pin moves)" );
		Assert.AreEqual( UmbrellaRules.WeatherSnow, WinterRules.WeatherStateOf( hallow.Rain, hallow.Lightning, hallow.Snow ), "the winter shops read the snow, so the kiosk's 200 and the stall's 250 apply" );
		// and the wintry December's snow (no lightning) is the same signal in both maps
		var jungle = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 3, 10, 10, wintryDecembers: true );
		jungle.ApplyQuality( 5, wintryDecember: true );
		Assert.AreEqual( UmbrellaRules.WeatherStateOf( jungle.Rain, jungle.Lightning, jungle.Snow ), WinterRules.WeatherStateOf( jungle.Rain, jungle.Lightning, jungle.Snow ) );
	}

	// ---- The forecast ----------------------------------------------------------------------------------------------

	/// <summary>A started jungle weather at <paramref name="day"/> (0-based from the calendar's epoch) with a forecast of <paramref name="quality"/> posted at once.</summary>
	private static ParkWeather ForecastAt( SamSettings settings, bool wintry, int day, int quality )
	{
		var weather = new ParkWeather( settings, 21, 10, 10, wintry );
		weather.AdvanceTo( ParkCalendar.TickOfTurn( (long)day * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn ) );
		weather.DebugPostForecast( quality );
		return weather;
	}

	[TestMethod]
	public void TheForecastOfAWintryDecemberRollSaysSnowAndTheApplyAgrees()
	{
		var thunderstorms = new WeatherQualityBands( false, true, true );
		var snow = new WeatherQualityBands( true, false, false );
		var wintry = ForecastAt( ParkWeatherSnapshotTests.Jungle(), true, FirstDecemberDay + 3, 5 );
		Assert.AreEqual( snow, wintry.ForecastBands, "the option is on and the roll is in December: the forecast does not say thunderstorms" );
		Assert.AreEqual( thunderstorms, wintry.BandsOf( 5 ), "the plain bands are what the converter reads before the option" );
		Assert.AreEqual( snow, ForecastAt( ParkWeatherSnapshotTests.Jungle(), true, FirstDecemberDay + 3, 30 ).ForecastBands, "plain rain (no lightning) becomes snow as well" );
		Assert.AreEqual( default( WeatherQualityBands ), ForecastAt( ParkWeatherSnapshotTests.Jungle(), true, FirstDecemberDay + 3, 78 ).ForecastBands, "a dry quality stays dry" );
		Assert.AreEqual( thunderstorms, ForecastAt( ParkWeatherSnapshotTests.Jungle(), false, FirstDecemberDay + 3, 5 ).ForecastBands, "the option off: the original's thunderstorms" );
		Assert.AreEqual( thunderstorms, ForecastAt( ParkWeatherSnapshotTests.Jungle(), true, FirstDecemberDay - 20, 5 ).ForecastBands, "a November roll: thunderstorms" );
		Assert.AreEqual( default( WeatherQualityBands ), new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 21, 10, 10, true ).ForecastBands, "no forecast pending: no bands" );
		// the park whose own snow band holds qualities is untouched by the option
		var hallow = ForecastAt( Hallow(), true, FirstDecemberDay + 3, 20 );
		Assert.AreEqual( hallow.BandsOf( 20 ), hallow.ForecastBands );
		// what the forecast announced is what the converter then makes of it
		var applied = ForecastAt( ParkWeatherSnapshotTests.Jungle(), true, FirstDecemberDay + 3, 5 );
		var announced = applied.ForecastBands;
		for ( var turn = (long)(FirstDecemberDay + 3) * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn + 1; applied.Forecast != null; turn++ )
			applied.AdvanceTo( ParkCalendar.TickOfTurn( turn ) );
		Assert.AreEqual( announced, new WeatherQualityBands( applied.Snow, applied.Rain, applied.Lightning ), "the snow it announced is the snow that fell" );
	}

	// ---- The calendar ----------------------------------------------------------------------------------------------

	/// <summary>One weather run: the state of every park turn over <paramref name="days"/> days (quality, flags, drops) hashed in order, and the days (0-based, from 2000-01-01) on which it snowed.</summary>
	internal static (ulong Hash, SortedSet<int> SnowDays, SortedSet<int> RainDays) Run( SamSettings settings, ulong seed, bool wintry, int days, bool alwaysWinter = false )
	{
		var weather = new ParkWeather( settings, seed, 10, 10, wintry, alwaysWinter );
		var hash = 14695981039346656037UL;
		var snow = new SortedSet<int>();
		var rain = new SortedSet<int>();
		void Add( long value ) => hash = (hash ^ (ulong)value) * 1099511628211UL;
		var lastTurn = (long)days * 24 * 3600 / ParkCalendar.SecondsPerTurn;
		for ( var turn = 0L; turn <= lastTurn; turn++ )
		{
			weather.AdvanceTo( ParkCalendar.TickOfTurn( turn ) );
			Add( weather.Quality );
			Add( (weather.Snow ? 1 : 0) | (weather.Rain ? 2 : 0) | (weather.Lightning ? 4 : 0) );
			Add( weather.CurrentDrops );
			var day = (int)(turn * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay);
			if ( weather.Snow && weather.CurrentDrops > 0 )
				snow.Add( day );
			if ( weather.Rain && weather.CurrentDrops > 0 )
				rain.Add( day );
		}
		return (hash, snow, rain);
	}

	/// <summary>The weather's first 400 days (to the end of the first February: the first December starts on day 335).</summary>
	internal const int RunDays = 400;
	internal const int FirstDecemberDay = 335;

	[TestMethod]
	public void TheJungleNeverSnowsWithTheOptionOffAndSnowsOnlyFromDecemberRollsWithItOn()
	{
		var off = Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: false, RunDays );
		Assert.AreEqual( 0, off.SnowDays.Count, "the original's jungle never snows (snow band -1..-1)" );
		Assert.IsTrue( off.RainDays.Count > 20, $"{off.RainDays.Count} rainy days" );
		var on = Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: true, RunDays );
		Assert.IsTrue( on.SnowDays.Count >= 5, $"{on.SnowDays.Count} snowy days with the option on" );
		// the roll that makes a December forecast is made on a December day and applied four days later, so no snow before the first December's roll has applied
		Assert.IsTrue( on.SnowDays.Min >= FirstDecemberDay + 4, $"the first snowy day is {on.SnowDays.Min}, December starts on day {FirstDecemberDay}" );
		// until the first snow the two runs are one weather: the option draws nothing and changes nothing before it
		Assert.IsTrue( off.RainDays.Count( day => day < FirstDecemberDay ) > 5 );
		CollectionAssert.AreEqual( off.RainDays.Where( day => day < FirstDecemberDay ).ToArray(), on.RainDays.Where( day => day < FirstDecemberDay ).ToArray(), "the rain before December is the same weather" );
		// the same option and seed give the same run (the pin below)
		Assert.AreEqual( on.Hash, Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: true, RunDays ).Hash );
		Assert.AreNotEqual( off.Hash, on.Hash );
	}

	[TestMethod]
	public void HallowAndSpaceRunTheSameWeatherWithTheOptionOnOrOff()
	{
		foreach ( var (name, settings) in new (string, SamSettings)[] { ("hallow", Hallow()), ("space", Space()) } )
		{
			var off = Run( settings, 22, wintry: false, RunDays );
			var on = Run( settings, 22, wintry: true, RunDays );
			Assert.AreEqual( off.Hash, on.Hash, $"{name}: the option changes nothing here (the Halloween park snows already, space has no precipitation)" );
		}
	}

	/// <summary>[EXT:winter-weather] The pinned hash of the jungle's first 400 days under seed 21 with the option off: the original's weather (docs/DETERMINISM.md, "Wintry Decembers").</summary>
	internal const ulong JungleSeed21OffHash = 0x58F5D2429FDCADAAUL;
	/// <summary>[EXT:winter-weather] The pinned hash of the same run with the option on (docs/DETERMINISM.md, "Wintry Decembers").</summary>
	internal const ulong JungleSeed21WintryHash = 0x2DC4898B7594C4FAUL;

	[TestMethod]
	public void TheJungleWeatherOfAFixedSeedIsPinnedWithTheOptionOffAndOn()
	{
		var off = Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: false, RunDays );
		var on = Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: true, RunDays );
		Assert.AreEqual( JungleSeed21OffHash, off.Hash, $"off: 0x{off.Hash:X16} ({off.RainDays.Count} rainy days)" );
		Assert.AreEqual( JungleSeed21WintryHash, on.Hash, $"on: 0x{on.Hash:X16} ({on.SnowDays.Count} snowy days, first on day {on.SnowDays.Min})" );
	}

	// ---- Resume --------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void AWintryWeatherSavedMidSnowContinuesIdenticallyAfterAResume()
	{
		var reference = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 21, 10, 10, wintryDecembers: true );
		var turns = (long)RunDays * 24 * 3600 / ParkCalendar.SecondsPerTurn;
		long saveTurn = -1;
		for ( var turn = 0L; turn <= turns && saveTurn < 0; turn++ )
		{
			reference.AdvanceTo( ParkCalendar.TickOfTurn( turn ) );
			if ( reference.Snow && reference.CurrentDrops > 100 )
				saveTurn = turn + 10;
		}
		Assert.IsTrue( saveTurn > 0, "the run snows" );
		reference.AdvanceTo( ParkCalendar.TickOfTurn( saveTurn ) );
		Assert.IsTrue( reference.Snow, "saved while it snows" );
		var snapshot = reference.CaptureSnapshot();
		var resumed = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 21, 10, 10, wintryDecembers: true );
		resumed.RestoreSnapshot( snapshot );
		for ( var turn = saveTurn + 1; turn <= turns; turn++ )
		{
			var tick = ParkCalendar.TickOfTurn( turn );
			reference.AdvanceTo( tick );
			resumed.AdvanceTo( tick );
			Assert.AreEqual( (reference.Quality, reference.Snow, reference.Rain, reference.Lightning, reference.CurrentDrops), (resumed.Quality, resumed.Snow, resumed.Rain, resumed.Lightning, resumed.CurrentDrops), $"turn {turn}" );
		}
		Assert.AreEqual( System.Text.Json.JsonSerializer.Serialize( reference.CaptureSnapshot() ), System.Text.Json.JsonSerializer.Serialize( resumed.CaptureSnapshot() ) );
	}
}
