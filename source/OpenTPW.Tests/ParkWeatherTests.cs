using System;
using System.Collections.Generic;
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
	public void TheConstructedQualityIsNeverConvertedAndTheFirstUpdateRollsAForecastAtOnce()
	{
		// [BIN:TPW-EXE:0x511f70] the constructor stores the season's roll as the quality without calling the converter 0x512f00, and lastChanged is 0
		var weather = new ParkWeather( Jungle(), 4, 10, 10 );
		weather.AdvanceTo( 0 );
		Assert.IsNull( weather.Forecast, "nothing is forecast before the first update" );
		Assert.IsTrue( weather.Quality is >= 1 and <= 100, "the quality is the season's roll" );
		Assert.IsFalse( weather.Rain || weather.Snow || weather.Lightning, "and has no effect: no weather was converted" );
		Assert.AreEqual( 0, weather.TargetDrops );
		Assert.AreEqual( 0, weather.CurrentDrops );
		var first = weather.Quality;
		weather.Step( ParkCalendar.TickOfTurn( 1 ) );
		Assert.IsNotNull( weather.Forecast, "the first update rolls a forecast at once" );
		Assert.AreEqual( first, weather.Quality, "and does not apply it yet" );
		Assert.IsFalse( weather.Rain || weather.Snow || weather.Lightning );
		var coming = weather.Forecast!.Value;
		weather.Step( ParkCalendar.TickAtDay( 4 ) );
		Assert.AreEqual( coming, weather.Forecast, "day 4 is still warning: the apply time is the roll's time plus four days" );
		weather.Step( ParkCalendar.TickAtDay( 5 ) );
		Assert.IsNull( weather.Forecast, "day 5 applies it" );
		Assert.AreEqual( coming, weather.Quality );
	}

	[TestMethod]
	public void ASaveHoldingDayIndicesIsMigratedToGameSecondsAndACurrentOneIsNot()
	{
		var weather = new ParkWeather( Jungle(), 4, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.AdvanceTo( ParkCalendar.TickAtDay( 5 ) );
		var seconds = weather.CaptureSnapshot();
		Assert.IsTrue( seconds.NextRollDay > 100, "a current snapshot holds game seconds" );
		// the same schedule as a pre-timestamp save wrote it: day indices (limit = day 5 + 4 + 7 + 1 = 17)
		var days = seconds with { LastChanged = 2, NextRollDay = 5, ApplyDay = 9, PendingQuality = 30 };
		var migrated = new ParkWeather( Jungle(), 4, 10, 10 ).MigrateSchedule( days );
		Assert.AreEqual( (2L * 86400, 5L * 86400, 9L * 86400), (migrated.LastChanged, migrated.NextRollDay, migrated.ApplyDay) );
		var restored = new ParkWeather( Jungle(), 4, 10, 10 );
		restored.RestoreSnapshot( days );
		Assert.AreEqual( 9, restored.ForecastApplyDay, "the forecast resumes on its day" );
		// a current snapshot is left alone, and so is one over the limit by a single day
		Assert.AreEqual( seconds, restored.MigrateSchedule( seconds ) );
		var over = seconds with { LastChanged = 0, NextRollDay = 18, ApplyDay = 0 };
		Assert.AreEqual( over, restored.MigrateSchedule( over ) );
		var atLimit = seconds with { LastChanged = 0, NextRollDay = 17, ApplyDay = 0 };
		Assert.AreEqual( 17L * 86400, restored.MigrateSchedule( atLimit ).NextRollDay );
		// a park under a day old cannot tell the two apart and keeps its values; an unstarted weather has nothing to migrate
		var young = seconds with { LastTurn = 10, LastChanged = 0, NextRollDay = 5, ApplyDay = 0 };
		Assert.AreEqual( young, restored.MigrateSchedule( young ) );
		var unstarted = new ParkWeather( Jungle(), 4, 10, 10 ).CaptureSnapshot();
		Assert.AreEqual( unstarted, restored.MigrateSchedule( unstarted ) );
	}

	[TestMethod]
	public void ARollIsDueAtItsTimestampNotAtADayBoundary()
	{
		// [BIN:TPW-EXE:0x51295c] roll when now >= lastChanged + max(0, DaysBetweenChanges - DaysOfWarning) days, on timestamps, not day indices
		var weather = new ParkWeather( Jungle(), 4, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.Step( ParkCalendar.TickOfTurn( 1 ) );
		var applyAt = ParkCalendar.SecondsPerTurn + 4 * ParkCalendar.SecondsPerDay;
		var applyTick = ParkCalendar.TickAtSeconds( applyAt );
		weather.Step( applyTick - 1 );
		Assert.IsNotNull( weather.Forecast, "one tick before the apply time" );
		weather.Step( applyTick );
		Assert.IsNull( weather.Forecast, "the apply, with a three day cycle before the next roll" );
		var appliedAt = ParkCalendar.Seconds( applyTick );
		var rollTick = ParkCalendar.TickAtSeconds( appliedAt + 3 * ParkCalendar.SecondsPerDay );
		var turnBefore = ParkCalendar.TickOfTurn( ParkCalendar.Turn( rollTick ) - 1 );
		weather.Step( turnBefore );
		Assert.IsNull( weather.Forecast, "the turn before lastChanged + 3 days: no roll yet, although it is the same day" );
		weather.Step( rollTick );
		Assert.IsNotNull( weather.Forecast, "the roll is due at lastChanged + 3 days" );
	}

	[TestMethod]
	public void AnApplyFallsThroughToTheRollTestInTheSameTick()
	{
		// DaysBetweenChanges (3) <= DaysOfWarning (4): the next roll is due at once, in the tick that applied the forecast
		var weather = new ParkWeather( Keys( "Weather.DaysOfWarning\t4\nWeather.DaysBetweenChanges\t3\n" ), 4, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.Step( ParkCalendar.TickOfTurn( 1 ) );
		var coming = weather.Forecast!.Value;
		var applyTick = ParkCalendar.TickAtSeconds( ParkCalendar.SecondsPerTurn + 4 * ParkCalendar.SecondsPerDay );
		weather.Step( applyTick );
		Assert.AreEqual( coming, weather.Quality, "the forecast applied" );
		Assert.IsNotNull( weather.Forecast, "and the same tick rolled the next forecast" );
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

	private static SamSettings Storm( int thunderStarts ) => Keys(
		"WeatherEffects.QualityForSnowLow\t-1\n" +
		"WeatherEffects.QualityForSnowHigh\t-1\n" +
		"WeatherEffects.QualityForRainLow\t0\n" +
		"WeatherEffects.QualityForRainHigh\t40\n" +
		"WeatherEffects.QualityForLightningLow\t0\n" +
		"WeatherEffects.QualityForLightningHigh\t15\n" +
		"WeatherEffects.LightningCountdownSpeed\t1\n" +
		"WeatherEffects.LightningCountdownFrom\t1\n" +
		$"WeatherEffects.ThunderStartsThisFarAway\t{thunderStarts}\n" +
		"WeatherEffects.ThunderRandomiser\t1\n" );

	[TestMethod]
	public void ACountdownOfOneRingsOnTheStrikeStepAndAComputedZeroNeverRings()
	{
		// 0x101033CC..0x10103528: a countdown of 0 - 1 strikes every step; the first strike arms max(0, 1 - 0) + 0 = 1, which
		// the step's own decrement takes to 0 (thunder); later strikes arm max(0, 1 - bolts) = 0, which never rings.
		var weather = new ParkWeather( Storm( 1 ), 5, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.ApplyQuality( 5 );
		var rang = new System.Collections.Generic.List<int>();
		for ( var step = 0; step < 6; step++ )
		{
			weather.Step( 0 );
			rang.Add( weather.TakeThunder().Count );
		}
		CollectionAssert.AreEqual( new[] { 1, 0, 0, 0, 0, 0 }, rang );

		var silent = new ParkWeather( Storm( 0 ), 5, 10, 10 );
		silent.AdvanceTo( 0 );
		silent.ApplyQuality( 5 );
		var total = 0;
		for ( var step = 0; step < 6; step++ )
		{
			silent.Step( 0 );
			total += silent.TakeThunder().Count;
		}
		Assert.AreEqual( 0, total, "a computed countdown of 0 never rings: the original's decrement takes it below 0" );
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

	[TestMethod]
	public void DecemberReadsTheSeasonArraysCountSlotAsItsAverage()
	{
		// tp.exe 0x512e10: December is season row 4, one past the array; its average is the array's count slot (0x7854f4),
		// which the .sam loader raises to the highest stored index + 1 (0x4017a0). Tolerance and chance are DaysOfWarning and
		// DaysBetweenChanges; zero for both makes the roll return the average itself.
		string Seasons( int count )
		{
			var text = "Weather.DaysOfWarning\t0\nWeather.DaysBetweenChanges\t0\n";
			for ( var i = 0; i < count; i++ )
				text += $"Seasons[{i}].AvgWeatherQuality\t{50 + i}\nSeasons[{i}].NormalTolerance\t0\nSeasons[{i}].ChanceForExceptionalWeather\t0\n";
			return text;
		}

		var december = ParkCalendar.TickAtMonth( 11 );
		Assert.AreEqual( 4, new ParkWeather( Keys( Seasons( 4 ) ), 1, 10, 10 ).Roll( december ), "four seasons stored (every shipped layer)" );
		Assert.AreEqual( 3, new ParkWeather( Keys( Seasons( 3 ) ), 1, 10, 10 ).Roll( december ), "the count follows the stored indices" );
		Assert.AreEqual( 52, new ParkWeather( Keys( Seasons( 4 ) ), 1, 10, 10 ).Roll( ParkCalendar.TickAtMonth( 6 ) ), "month 7 is season 2 (month / 3)" );
	}

	/// <summary>
	/// The wind (tp.exe weather constructor 0x511F70 and converter 0x512F00): 90 degrees and 0.5 at the start; every applied change sets the strength to (100 - quality) / 100 and turns the
	/// direction by -45 to +44 degrees inside 0 to 360, from a stream of its own, so the weather's own draws are unchanged.
	/// </summary>
	[TestMethod]
	public void EveryAppliedChangeTurnsTheWindAndLeavesTheWeathersOwnDraws()
	{
		var weather = new ParkWeather( Jungle(), 7, 10, 10 );
		Assert.AreEqual( (90f, 0.5f), (weather.WindDirection, weather.WindStrength) );
		var directions = new List<float>();
		for ( var change = 0; change < 200; change++ )
		{
			var before = weather.WindDirection;
			var stream = weather.CaptureSnapshot().RandomState;
			weather.ApplyQuality( 100 - change % 100 );
			Assert.AreEqual( Math.Clamp( (change % 100) * 0.01f, 0f, 1f ), weather.WindStrength, 1e-6f );
			Assert.IsTrue( weather.WindDirection is >= 0f and <= 360f );
			var turn = (weather.WindDirection - before + 540f) % 360f - 180f;
			Assert.IsTrue( turn is >= -45f and < 45f, $"turn {turn}" );
			directions.Add( weather.WindDirection );
			// the jungle has no snow band, so the converter itself draws nothing (no rain/snow tie): the weather's stream stands still
			Assert.AreEqual( stream, weather.CaptureSnapshot().RandomState, "the wind does not draw from the weather's stream" );
		}
		Assert.IsTrue( directions.Distinct().Count() > 50, "the direction wanders" );
	}

	[TestMethod]
	public void TheWindIsSavedAndASaveFromBeforeItStartsFromTheConstructorsWind()
	{
		var weather = new ParkWeather( Jungle(), 5, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.AdvanceTo( ParkCalendar.TickAtDay( 30 ) );
		weather.ApplyQuality( 30 );
		weather.ApplyQuality( 60 );
		var snapshot = weather.CaptureSnapshot();
		var restored = new ParkWeather( Jungle(), 5, 10, 10 );
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( (weather.WindDirection, weather.WindStrength), (restored.WindDirection, restored.WindStrength) );
		weather.ApplyQuality( 45 );
		restored.ApplyQuality( 45 );
		Assert.AreEqual( weather.WindDirection, restored.WindDirection, "the wind's stream continues" );
		var legacy = new ParkWeather( Jungle(), 5, 10, 10 );
		legacy.RestoreSnapshot( snapshot with { WindDirection = null, WindStrength = null, WindRandomState = null } );
		Assert.AreEqual( (90f, 0.5f), (legacy.WindDirection, legacy.WindStrength) );
		Assert.ThrowsException<InvalidDataException>( () => legacy.ValidateSnapshot( snapshot with { WindDirection = 400 } ) );
		Assert.ThrowsException<InvalidDataException>( () => legacy.ValidateSnapshot( snapshot with { WindStrength = -0.5f } ) );
		Assert.ThrowsException<InvalidDataException>( () => legacy.ValidateSnapshot( snapshot with { WindRandomState = null } ), "all three wind fields or none" );
	}
}
