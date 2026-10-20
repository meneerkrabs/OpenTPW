using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The forecast's presentation (docs/AUDIO.md "Weather visuals"): the countdown line a pending forecast shows,
/// the messages posted when a forecast appears and applies, the transient changed/current lines, and the
/// ParkWeather seams the presentation reads (apply day, current day, band classification, the debug seam).
/// All of it is an OpenTPW extension (forecast-presentation): the original shows no forecast, and no original
/// forecast text exists to compare against.
/// </summary>
[TestClass]
public class WeatherForecastTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static readonly WeatherQualityBands Dry = new( false, false, false );
	private static readonly WeatherQualityBands Rain = new( false, true, false );
	private static readonly WeatherQualityBands Snow = new( true, false, false );
	private static readonly WeatherQualityBands Thunderstorms = new( false, true, true );

	private static WeatherForecastSample Sample(
		long day = 0, int? forecast = null, long applyDay = 0,
		WeatherQualityBands? forecastBands = null, WeatherQualityBands? currentBands = null ) =>
		new( day, forecast, applyDay, forecastBands ?? Dry, currentBands ?? Dry );

	[TestMethod]
	public void QuietWeatherStaysSilentOnAttach()
	{
		var state = new WeatherForecastState();
		state.Step( Sample(), 0.016f );
		Assert.IsNull( state.Banner, "a dry, quiet park shows nothing" );
		Assert.AreEqual( 0, state.TakeAnnouncements().Count );
	}

	[TestMethod]
	public void EnteringWeatheredParkSaysSoOnce()
	{
		var state = new WeatherForecastState();
		state.Step( Sample( currentBands: Rain ), 0.016f );
		Assert.AreEqual( "The weather: rain.", state.Banner );
		Assert.IsTrue( state.IsTransient );
		Assert.AreEqual( 0, state.TakeAnnouncements().Count, "the attach line is display only, not a message" );
		state.Step( Sample( currentBands: Rain ), 0.016f );
		Assert.AreEqual( "The weather: rain.", state.Banner, "the line keeps showing until its time is up" );
	}

	[TestMethod]
	public void TheTransientLineRetiresWithSimulationTimeAndFreezesWhenPaused()
	{
		var state = new WeatherForecastState();
		state.Step( Sample( currentBands: Rain ), 0f );
		for ( var step = 0; step < 100; step++ )
			state.Step( Sample( currentBands: Rain ), 0f );
		Assert.AreEqual( "The weather: rain.", state.Banner, "a paused park (delta 0) freezes the line mid-display" );
		for ( var step = 0; step < 11; step++ )
			state.Step( Sample( currentBands: Rain ), 0.5f );
		Assert.IsNotNull( state.Banner, "5.5 of 6 seconds have passed" );
		state.Step( Sample( currentBands: Rain ), 0.5f );
		Assert.IsNull( state.Banner, "the line is gone after ChangedLineSeconds of simulation time" );
	}

	[TestMethod]
	public void APendingForecastIsAnnouncedOnceAndCountsDown()
	{
		var state = new WeatherForecastState();
		state.Step( Sample( day: 3 ), 0.016f );
		state.Step( Sample( day: 3, forecast: 8, applyDay: 7, forecastBands: Thunderstorms ), 0.016f );
		var messages = state.TakeAnnouncements();
		CollectionAssert.AreEqual( new[] { "Weather forecast: thunderstorms in 4 days." }, messages.ToArray() );
		Assert.AreEqual( "Forecast: thunderstorms in 4 days.", state.Banner );
		Assert.IsFalse( state.IsTransient, "the countdown is the steady line, not the transient one" );
		// More steps inside the same forecast: no further message, the countdown follows the days.
		for ( var day = 4; day <= 6; day++ )
		{
			state.Step( Sample( day, forecast: 8, applyDay: 7, forecastBands: Thunderstorms ), 0.016f );
			Assert.AreEqual( 0, state.TakeAnnouncements().Count, "one message per forecast, not per step" );
			Assert.AreEqual( $"Forecast: thunderstorms {(day == 6 ? "tomorrow" : $"in {7 - day} days")}.", state.Banner );
		}
	}

	[TestMethod]
	public void TheForecastAppliesWithOneMessageAndAChangedLine()
	{
		var state = new WeatherForecastState();
		state.Step( Sample( day: 3, forecast: 30, applyDay: 7, forecastBands: Rain ), 0.016f );
		state.TakeAnnouncements();
		state.Step( Sample( day: 7, currentBands: Rain ), 0.016f );
		CollectionAssert.AreEqual( new[] { "The weather has changed: rain." }, state.TakeAnnouncements().ToArray() );
		Assert.AreEqual( "The weather has changed: rain.", state.Banner );
		Assert.IsTrue( state.IsTransient );
		state.Step( Sample( day: 7, currentBands: Rain ), WeatherForecastState.ChangedLineSeconds );
		Assert.IsNull( state.Banner, "after the flash the park is quiet until the next forecast" );
	}

	[TestMethod]
	public void AParkResumedMidWarningAnnouncesThePendingForecastOnce()
	{
		var state = new WeatherForecastState();
		// A resumed park first sees the forecast with one day left, as a restore mid-warning would.
		state.Step( Sample( day: 6, forecast: 8, applyDay: 7, forecastBands: Thunderstorms, currentBands: Rain ), 0.016f );
		var messages = state.TakeAnnouncements();
		CollectionAssert.AreEqual( new[] { "Weather forecast: thunderstorms tomorrow." }, messages.ToArray() );
		Assert.AreEqual( "The weather: rain.", state.Banner, "the transient attach line shows first (it wins over the countdown)" );
		state.Step( Sample( day: 6, forecast: 8, applyDay: 7, forecastBands: Thunderstorms, currentBands: Rain ), WeatherForecastState.ChangedLineSeconds );
		Assert.AreEqual( "Forecast: thunderstorms tomorrow.", state.Banner, "then the countdown takes over" );
		for ( var step = 0; step < 50; step++ )
			state.Step( Sample( day: 6, forecast: 8, applyDay: 7, forecastBands: Thunderstorms, currentBands: Rain ), 0.1f );
		Assert.AreEqual( 0, state.TakeAnnouncements().Count, "the resumed warning is not repeated while the day holds" );
	}

	[TestMethod]
	public void SteppingNeverSpams()
	{
		var state = new WeatherForecastState();
		var total = 0;
		// One full cycle observed step by step (rain applies on day 7), then a second forecast (snow on day 14): two posts and two changes in all.
		for ( var day = 0; day <= 20; day++ )
		{
			int? forecast = day is >= 3 and < 7 ? 30 : day is >= 10 and < 14 ? 60 : null;
			var bands = forecast == 30 ? Rain : forecast == 60 ? Snow : Dry;
			var current = day >= 14 ? Snow : day >= 7 ? Rain : Dry;
			state.Step( Sample( day, forecast, forecast != null ? forecast == 30 ? 7 : 14 : 0, bands, current ), 0.1f );
			total += state.TakeAnnouncements().Count;
		}
		Assert.AreEqual( 4, total, "one post and one change per forecast cycle, nothing per step" );
	}

	[TestMethod]
	public void AForecastOrApplyThatKeepsTheKindOfWeatherIsNoNews()
	{
		// The original rolls every cycle whatever the weather: a dry roll during dry weather shows and posts nothing, nor does its apply.
		var state = new WeatherForecastState();
		state.Step( Sample( day: 3 ), 0.016f );
		state.Step( Sample( day: 3, forecast: 88, applyDay: 7, forecastBands: Dry ), 0.016f );
		Assert.IsNull( state.Banner, "no countdown toward the weather there is already" );
		state.Step( Sample( day: 7 ), 0.016f );
		Assert.IsNull( state.Banner );
		Assert.AreEqual( 0, state.TakeAnnouncements().Count, "dry after dry is no news" );
		// The same for rain after rain, once the attach line is over.
		var wet = new WeatherForecastState();
		wet.Step( Sample( day: 3, currentBands: Rain ), WeatherForecastState.ChangedLineSeconds );
		wet.Step( Sample( day: 3, forecast: 30, applyDay: 7, forecastBands: Rain, currentBands: Rain ), 0.016f );
		Assert.IsNull( wet.Banner );
		wet.Step( Sample( day: 7, currentBands: Rain ), 0.016f );
		Assert.IsNull( wet.Banner );
		Assert.AreEqual( 0, wet.TakeAnnouncements().Count, "rain after rain is no news" );
		// An undecided snow-or-rain forecast during rain may still bring snow: it is announced.
		wet.Step( Sample( day: 10, forecast: 20, applyDay: 14, forecastBands: new WeatherQualityBands( true, true, false ), currentBands: Rain ), 0.016f );
		CollectionAssert.AreEqual( new[] { "Weather forecast: snow or rain in 4 days." }, wet.TakeAnnouncements().ToArray() );
	}

	[TestMethod]
	public void AChangeAndTheNextForecastInOneStepBothPost()
	{
		// With DaysBetweenChanges at most DaysOfWarning the apply and the next roll come in one update (tp.exe 0x512880 falls through from one to the other).
		var state = new WeatherForecastState();
		state.Step( Sample( day: 3, forecast: 30, applyDay: 7, forecastBands: Rain ), 0.016f );
		state.TakeAnnouncements();
		state.Step( Sample( day: 7, forecast: 60, applyDay: 11, forecastBands: Snow, currentBands: Rain ), 0.016f );
		CollectionAssert.AreEqual( new[] { "The weather has changed: rain.", "Weather forecast: snow in 4 days." }, state.TakeAnnouncements().ToArray() );
		Assert.AreEqual( "The weather has changed: rain.", state.Banner, "the changed line shows first" );
		state.Step( Sample( day: 7, forecast: 60, applyDay: 11, forecastBands: Snow, currentBands: Rain ), WeatherForecastState.ChangedLineSeconds );
		Assert.AreEqual( "Forecast: snow in 4 days.", state.Banner, "then the new countdown" );
	}

	[TestMethod]
	public void TheKindTextNamesTheUndecidedTieAndComposesThunder()
	{
		var english = new WeatherForecastState();
		Assert.AreEqual( "snow or rain", english.KindText( new WeatherQualityBands( true, true, false ) ) );
		Assert.AreEqual( "thunderstorms", english.KindText( new WeatherQualityBands( true, true, true ) ) );
		Assert.AreEqual( "thunderstorms", english.KindText( new WeatherQualityBands( false, true, true ) ) );
		Assert.AreEqual( "thunder and lightning", english.KindText( new WeatherQualityBands( false, false, true ) ) );
		Assert.AreEqual( "snow", english.KindText( new WeatherQualityBands( true, false, false ) ) );
		Assert.AreEqual( "rain", english.KindText( Rain ) );
		Assert.AreEqual( "dry weather", english.KindText( Dry ) );
		Assert.AreEqual( "today", english.DueText( 0 ) );
		Assert.AreEqual( "tomorrow", english.DueText( 1 ) );
		Assert.AreEqual( "in 5 days", english.DueText( 5 ) );
	}

	[TestMethod]
	public void TheLinesFollowTheSelectedLanguage()
	{
		var language = "Dutch";
		var state = new WeatherForecastState( () => language );
		state.Step( Sample( day: 3, forecast: 30, applyDay: 4, forecastBands: Rain, currentBands: Dry ), 0 );
		Assert.AreEqual( "Weerbericht: morgen regen.", state.Banner );
		CollectionAssert.AreEqual( new[] { "Weerbericht: morgen regen." }, state.TakeAnnouncements().ToArray() );
		language = "German";
		state.Step( Sample( day: 1, forecast: 30, applyDay: 4, forecastBands: Rain, currentBands: Dry ), 0 );
		Assert.AreEqual( "Vorhersage: in 3 Tagen Regen.", state.Banner, "the countdown is worded again on every step" );
		Assert.AreEqual( "torrt väder", new WeatherForecastState( () => "Swedish" ).KindText( Dry ) );
		Assert.AreEqual( "in 2 days", new WeatherForecastState( () => "Klingon" ).DueText( 2 ), "a language without a translation reads English" );
	}

	// ---- ParkWeather seams ----

	/// <summary>The jungle's weather bands with the shipped cycle (rain 0..40, lightning 0..15, no snow, four days of warning).</summary>
	private static SamSettings Jungle() => new( new[] { SamDocument.Parse(
		"WeatherEffects.QualityForSnowLow\t-1\n" +
		"WeatherEffects.QualityForSnowHigh\t-1\n" +
		"WeatherEffects.QualityForRainLow\t0\n" +
		"WeatherEffects.QualityForRainHigh\t40\n" +
		"WeatherEffects.QualityForLightningLow\t0\n" +
		"WeatherEffects.QualityForLightningHigh\t15\n" +
		"WeatherEffects.MaxRaindrops\t600\n" +
		"Weather.DaysOfWarning\t4\n" +
		"Weather.DaysBetweenChanges\t7\n" +
		"Weather.SpeedOfChange\t20\n", "test" ) } );

	[TestMethod]
	public void TheWeatherExposesTheScheduleThePresentationReads()
	{
		var weather = new ParkWeather( Jungle(), 4, 10, 10 );
		Assert.IsFalse( weather.Started );
		weather.AdvanceTo( 0 );
		Assert.IsTrue( weather.Started );
		Assert.AreEqual( 0, weather.CurrentDay );
		Assert.AreEqual( 4, weather.DaysOfWarning );
		Assert.IsNull( weather.Forecast );
		weather.Step( ParkCalendar.TickOfTurn( 1 ) );
		Assert.AreEqual( 0, weather.CurrentDay );
		Assert.IsNotNull( weather.Forecast, "[BIN:TPW-EXE:0x511f70] lastChanged is 0, so the first update rolls a forecast at once" );
		Assert.AreEqual( 4, weather.ForecastApplyDay, "the forecast applies the warning distance after its roll" );
		weather.Step( ParkCalendar.TickAtDay( 4 ) );
		Assert.AreEqual( 4, weather.CurrentDay );
		Assert.IsNotNull( weather.Forecast, "the timestamp is the roll's time plus four days, a turn after the start of day 4" );
		weather.Step( ParkCalendar.TickAtDay( 5 ) );
		Assert.AreEqual( 5, weather.CurrentDay );
		Assert.IsNull( weather.Forecast, "day 5 applies it" );
	}

	[TestMethod]
	public void TheBandClassificationMatchesTheConverter()
	{
		var weather = new ParkWeather( Jungle(), 1, 10, 10 );
		foreach ( var quality in new[] { 1, 8, 15, 16, 20, 40, 41, 78, 100 } )
		{
			var bands = weather.BandsOf( quality );
			Assert.AreEqual( bands.Rain, quality is >= 0 and <= 40, $"rain band of {quality}" );
			Assert.AreEqual( bands.Lightning, quality is >= 0 and <= 15, $"lightning band of {quality}" );
			Assert.IsFalse( bands.Snow, "the jungle ships no snow band" );
			// The converter's own flags follow the same bands (the tie cannot hold without a snow band).
			weather.ApplyQuality( quality );
			Assert.AreEqual( bands.Rain, weather.Rain, $"the applied rain of {quality}" );
			Assert.AreEqual( bands.Lightning, weather.Lightning, $"the applied lightning of {quality}" );
		}
		// Both precipitation bands at once classify as undecided; the applied weather breaks the tie.
		var both = new ParkWeather( new( new[] { SamDocument.Parse(
			"WeatherEffects.QualityForSnowLow\t0\nWeatherEffects.QualityForSnowHigh\t100\n" +
			"WeatherEffects.QualityForRainLow\t0\nWeatherEffects.QualityForRainHigh\t100\n" +
			"WeatherEffects.QualityForLightningLow\t-1\nWeatherEffects.QualityForLightningHigh\t-1\n" +
			"WeatherEffects.MaxRaindrops\t600\nWeather.SpeedOfChange\t20\n", "test" ) } ), 1, 10, 10 );
		Assert.AreEqual( new WeatherQualityBands( true, true, false ), both.BandsOf( 50 ) );
	}

	[TestMethod]
	public void TheDebugSeamPostsARealForecast()
	{
		var weather = new ParkWeather( Jungle(), 1, 10, 10 );
		Assert.ThrowsException<InvalidOperationException>( () => weather.DebugPostForecast( 10 ), "the seam needs a started weather" );
		weather.AdvanceTo( ParkCalendar.TickAtDay( 2 ) );
		var before = weather.Quality;
		weather.DebugPostForecast( 300 ); // out of range: clamped like a rolled quality
		Assert.AreEqual( 100, weather.Forecast );
		Assert.AreEqual( 2 + 4, weather.ForecastApplyDay, "the debug forecast applies the warning distance ahead" );
		Assert.AreEqual( before, weather.Quality, "and does not apply it at once" );
		weather.Step( ParkCalendar.TickAtDay( 5 ) );
		Assert.IsNotNull( weather.Forecast, "day 5 is still inside the warning window" );
		weather.Step( ParkCalendar.TickAtDay( 6 ) );
		Assert.IsNotNull( weather.Forecast, "the timestamp (four days after the posting turn) is a few turns past the start of day 6" );
		weather.Step( ParkCalendar.TickAtDay( 7 ) );
		Assert.IsNull( weather.Forecast );
		Assert.AreEqual( 100, weather.Quality, "the debug forecast applies on its day like a natural one" );
	}

	[TestMethod]
	public void ARestoredWeatherKeepsTheCurrentDayTheCountdownReads()
	{
		var weather = new ParkWeather( Jungle(), 4, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.AdvanceTo( ParkCalendar.TickAtDay( 2 ) ); // turn by turn: the first turn rolls the forecast (applies four days later)
		Assert.AreEqual( 2, weather.CurrentDay );
		Assert.IsNotNull( weather.Forecast, "the first update's forecast is pending on day 2" );
		var snapshot = weather.CaptureSnapshot();
		var restored = new ParkWeather( Jungle(), 4, 10, 10 );
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( 2, restored.CurrentDay, "the countdown's 'now' resumes at the saved day, not day 0" );
		Assert.AreEqual( 4, restored.ForecastApplyDay );
		// The first presentation step after restore reads the true remaining distance, before any turn steps; the current weather is taken of another
		// kind than the rolled forecast, so the forecast is news
		var state = new WeatherForecastState();
		var quality = restored.Forecast;
		Assert.IsNotNull( quality );
		var forecastBands = restored.BandsOf( quality.Value );
		var current = WeatherForecastState.KindKey( forecastBands ) == OpenTPW.UI.Original.OpenTpwText.WeatherDry ? Rain : Dry;
		state.Step( new WeatherForecastSample( restored.CurrentDay, quality, restored.ForecastApplyDay, forecastBands, current ), 0.016f );
		var expected = $"Weather forecast: {state.KindText( restored.BandsOf( quality.Value ) )} in 2 days.";
		CollectionAssert.AreEqual( new[] { expected }, state.TakeAnnouncements().ToArray(),
			"a park resumed mid-warning announces the true distance on its first frame" );
	}
}
