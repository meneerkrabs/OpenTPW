using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The weather's own-format snapshot: a restored park continues the same weather — the draw stream, the
/// forecast schedule, the ramped drops and the lightning clocks — instead of re-rolling from the seed, and
/// invalid snapshots are rejected against the destination's settings before any mutation.
/// </summary>
[TestClass]
public class ParkWeatherSnapshotTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static SamSettings Keys( string text ) => new( new[] { SamDocument.Parse( text, "test" ) } );

	/// <summary>The jungle's weather bands with the shipped cycle (rain 0..40, lightning 0..15, no snow, 600 drops).</summary>
	internal static SamSettings Jungle() => Keys(
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

	private static void AssertSameWeather( ParkWeather expected, ParkWeather actual, string context )
	{
		Assert.AreEqual( expected.Quality, actual.Quality, context );
		Assert.AreEqual( expected.Snow, actual.Snow, context );
		Assert.AreEqual( expected.Rain, actual.Rain, context );
		Assert.AreEqual( expected.Lightning, actual.Lightning, context );
		Assert.AreEqual( expected.CurrentDrops, actual.CurrentDrops, context );
		Assert.AreEqual( expected.TargetDrops, actual.TargetDrops, context );
		Assert.AreEqual( expected.Forecast, actual.Forecast, context );
	}

	[TestMethod]
	public void RestoredWeatherContinuesTheSavedStreamAndSchedule()
	{
		var original = new ParkWeather( Jungle(), 4, 10, 10 );
		original.AdvanceTo( ParkCalendar.TickAtDay( 6 ) );
		var snapshot = original.CaptureSnapshot();
		Assert.IsTrue( snapshot.Started, "six days in, the weather has started" );
		Assert.IsTrue( snapshot.Quality is >= 1 and <= 100 );

		var restored = new ParkWeather( Jungle(), 4, 10, 10 );
		restored.RestoreSnapshot( snapshot );
		AssertSameWeather( original, restored, "the restore itself changes nothing" );

		original.AdvanceTo( ParkCalendar.TickAtDay( 13 ) );
		restored.AdvanceTo( ParkCalendar.TickAtDay( 13 ) );
		AssertSameWeather( original, restored, "the restored park follows the same weather" );
		var continued = original.CaptureSnapshot();
		var resumed = restored.CaptureSnapshot();
		Assert.AreEqual( continued.RandomState, resumed.RandomState, "the draw stream continues exactly" );
		Assert.AreEqual( continued.LastTurn, resumed.LastTurn );
		Assert.AreEqual( continued.LastChanged, resumed.LastChanged );
		Assert.AreEqual( continued.NextRollDay, resumed.NextRollDay );
		Assert.AreEqual( continued.Bolts, resumed.Bolts );
		Assert.AreEqual( continued.ThunderCountdown, resumed.ThunderCountdown );
		CollectionAssert.AreEqual( continued.PendingStrikes.ToArray(), resumed.PendingStrikes.ToArray(), "the due thunder matches" );

		// A seed-only restart is not a snapshot: replaying the park from its seed lands on a different point
		// of the stream (and re-runs the weather steps), so the contract requires the explicit state.
		var replayed = new ParkWeather( Jungle(), 4, 10, 10 );
		replayed.AdvanceTo( ParkCalendar.TickAtDay( 13 ) );
		Assert.AreNotEqual( continued.RandomState, replayed.CaptureSnapshot().RandomState );
	}

	[TestMethod]
	public void RestoredWeatherPlaysTheDueThunderOnce()
	{
		var weather = new ParkWeather( Jungle(), 4, 10, 10 );
		weather.AdvanceTo( 0 );
		var snapshot = weather.CaptureSnapshot() with
		{
			PendingStrikes = new[]
			{
				new ParkWeather.ThunderStrikeSnapshot( 12f, 300f, 34f ),
				new ParkWeather.ThunderStrikeSnapshot( 56f, 300f, 78f )
			}
		};
		var restored = new ParkWeather( Jungle(), 4, 10, 10 );
		restored.RestoreSnapshot( snapshot );
		var due = restored.TakeThunder();
		Assert.AreEqual( 2, due.Count );
		Assert.AreEqual( (12f, 300f, 34f), (due[0].X, due[0].Y, due[0].Z) );
		Assert.AreEqual( (56f, 300f, 78f), (due[1].X, due[1].Y, due[1].Z) );
		Assert.AreEqual( 0, restored.TakeThunder().Count, "the strikes are taken once, not duplicated" );
	}

	[TestMethod]
	public void WeatherSnapshotsRejectInvalidStateWithoutMutating()
	{
		var weather = new ParkWeather( Jungle(), 4, 10, 10 );
		weather.AdvanceTo( ParkCalendar.TickAtDay( 6 ) );
		weather.ApplyQuality( 8 ); // a lightning-band quality, so the lightning clocks are live
		weather.Step( 0 );
		var good = weather.CaptureSnapshot();
		foreach ( var bad in new[]
		{
			good with { Quality = 0 },
			good with { Quality = 101 },
			good with { Snow = true, Rain = true },
			good with { CurrentDrops = 601 },
			good with { TargetDrops = -1 },
			good with { LastTurn = -1 },
			good with { LastChanged = -2 },
			good with { NextRollDay = good.LastChanged - 1 },
			good with { PendingQuality = 101 },
			good with { PendingQuality = 0 },
			good with { PendingQuality = 42, ApplyDay = good.LastChanged - 1 },
			good with { LightningCountdown = -1 },
			good with { Bolts = -1 },
			good with { ThunderCountdown = -1 },
			good with { LastTop = new ParkWeather.ThunderStrikeSnapshot( float.NaN, 300f, 0f ) },
			good with { PendingStrikes = Enumerable.Range( 0, ParkWeather.ParkWeatherSnapshot.MaximumPendingStrikes + 1 ).Select( _ => new ParkWeather.ThunderStrikeSnapshot( 1f, 2f, 3f ) ).ToArray() }
		} )
		{
			var fresh = new ParkWeather( Jungle(), 4, 10, 10 );
			fresh.AdvanceTo( 0 );
			var before = fresh.CaptureSnapshot();
			Assert.ThrowsException<InvalidDataException>( () => fresh.ValidateSnapshot( bad ), $"quality {bad.Quality}, turn {bad.LastTurn}" );
			Assert.ThrowsException<InvalidDataException>( () => fresh.RestoreSnapshot( bad ) );
			var after = fresh.CaptureSnapshot();
			Assert.AreEqual( before.RandomState, after.RandomState, "a rejected restore does not touch the fresh weather" );
			Assert.AreEqual( before.Quality, after.Quality );
		}
		Assert.ThrowsException<InvalidDataException>( () => weather.ValidateSnapshot( good with { Started = false } ),
			"a not-started snapshot must be pristine" );
		Assert.ThrowsException<InvalidDataException>( () => weather.ValidateSnapshot( good with { Started = false, Quality = 0 } ),
			"pristine also means no live drops or clocks" );
	}

	[TestMethod]
	public void WeatherSnapshotsValidateAgainstTheDestinationSettings()
	{
		var weather = new ParkWeather( Jungle(), 4, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.ApplyQuality( 1 ); // target 588 drops, inside the jungle's 600 cap
		weather.Step( 0 );
		var snapshot = weather.CaptureSnapshot();
		Assert.AreEqual( 588, snapshot.TargetDrops );

		var drier = new ParkWeather( Keys(
			"WeatherEffects.MaxRaindrops\t300\n" +
			"Weather.DaysOfWarning\t4\nWeather.DaysBetweenChanges\t7\nWeather.SpeedOfChange\t20\n" ), 4, 10, 10 );
		Assert.ThrowsException<InvalidDataException>( () => drier.ValidateSnapshot( snapshot ), "the drops exceed the destination park's cap" );
		drier.AdvanceTo( 0 );
		Assert.AreEqual( 0, drier.CurrentDrops, "the rejected snapshot does not mutate" );
	}
}
