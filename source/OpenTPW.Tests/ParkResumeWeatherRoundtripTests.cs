using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class ParkResumeWeatherRoundtripTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private sealed class Rig
	{
		private readonly ParkResumeSnapshot template;
		private readonly ParkObjects objects;
		private readonly WorldSeed seed;
		public readonly FixedStepClock Clock = new();
		public readonly GuestSimulation Guests;
		public ParkEconomy Economy;
		public ParkWeather? Weather;

		public Rig( ParkResumeSnapshot template, bool snow, ParkResumeSnapshot? saved = null )
		{
			this.template = template;
			seed = new WorldSeed( template.Seed );
			objects = new ParkObjects( null!, new SandboxParkGrid(), seed );
			var grid = saved == null ? new GuestPathGrid( 20, 10 ) : new GuestPathGrid( ParkLayoutSnapshot.RestoreCellMap( saved.Layout ) );
			if ( saved == null ) for ( var x = 0; x < 20; x++ ) grid.SetPath( x, 0, true );
			var guestSettings = GuestTests.CreateSettings();
			guestSettings.ExitLevelSeconds = 100000;
			guestSettings.ExitLevelVarSeconds = 0;
			Guests = new GuestSimulation( grid, guestSettings, 7 ) { ArrivalsEnabled = false };
			var weatherKeys = string.Join( "\n", Enumerable.Range( 0, 4 ).SelectMany( i => new[]
			{
				$"Seasons[{i}].AvgWeatherQuality 8", $"Seasons[{i}].NormalTolerance 0", $"Seasons[{i}].ChanceForExceptionalWeather 0"
			} ) ) + $"\nWeatherEffects.QualityForSnowLow {(snow ? 0 : -1)}\nWeatherEffects.QualityForSnowHigh {(snow ? 40 : -1)}\n"
				+ $"WeatherEffects.QualityForRainLow {(snow ? -1 : 0)}\nWeatherEffects.QualityForRainHigh {(snow ? -1 : 40)}\n"
				+ "WeatherEffects.QualityForLightningLow 0\nWeatherEffects.QualityForLightningHigh 15\nWeatherEffects.MaxRaindrops 64\n"
				+ "Weather.DaysOfWarning 1\nWeather.DaysBetweenChanges 2\nWeather.SpeedOfChange 3\n"
				+ "WeatherEffects.LightningCountdownSpeed 1\nWeatherEffects.LightningCountdownFrom 10\n"
				+ "WeatherEffects.ThunderStartsThisFarAway 8\nWeatherEffects.ThunderRandomiser 4\n";
			var settings = EconomyTestData.Settings( extra: weatherKeys );
			Economy = saved == null ? new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 )
				: ParkSaveFile.Restore( saved.Economy, settings, EconomyTestData.Catalog() );
			if ( saved == null )
			{
				Guests.SpawnInPark( 10, 0 );
				Weather = NewWeather();
			}
			else
			{
				objects.ScriptWorld.RestoreSnapshot( saved.Scripts, new Dictionary<int, RideVM>() );
				Guests.RestoreSnapshot( saved.Guests );
				Clock.RestoreSnapshot( saved.Clock );
				if ( saved.Weather != null ) { Weather = NewWeather(); Weather.RestoreSnapshot( saved.Weather ); }
			}
		}

		private ParkWeather NewWeather() => new( Economy.Settings.Standard, seed.WeatherStream, Guests.Grid.CountX, Guests.Grid.CountY );
		public int Frame( double elapsed ) => Clock.Advance( elapsed * GameSpeedScale.Of( Economy.Speed ), dt =>
		{
			Guests.Tick( dt );
			Economy.AdvanceFixedTick();
			Weather ??= NewWeather();
			Weather.AdvanceTo( Economy.Tick );
		} );
		public ParkResumeSnapshot Capture() => template with
		{
			Economy = ParkSaveFile.Capture( Economy, new WorldRandomState( seed.Value, Guests.RandomState, objects.ScriptWorld.RandomState, 42 ) ),
			Layout = ParkLayoutSnapshot.CaptureSnapshot( Guests.Grid.Cells, objects ),
			Guests = Guests.CaptureSnapshot(),
			Scripts = objects.ScriptWorld.CaptureSnapshot(),
			Runtimes = Array.Empty<ParkResumeRuntime>(),
			EconomyBindings = Array.Empty<ParkResumeEconomyBinding>(),
			Clock = Clock.CaptureSnapshot(),
			Weather = Weather?.CaptureSnapshot()
		};
	}

	private static void Same( Rig original, Rig restored, string stage ) => CollectionAssert.AreEqual(
		ParkResumeSaveFile.Serialize( original.Capture() ), ParkResumeSaveFile.Serialize( restored.Capture() ), stage );
	private static void Storm( Rig rig )
	{
		for ( var frame = 0; frame < 1000; frame++ )
		{
			rig.Frame( 0.1 );
			var weather = rig.Weather!.CaptureSnapshot();
			if ( weather.CurrentDrops > 0 && weather.PendingQuality != null && weather.PendingStrikes.Count > 0
				&& weather.LightningCountdown > 0 && weather.ThunderCountdown > 0 ) return;
		}
		Assert.Fail( "the bounded synthetic timeline must reach a live storm, forecast and due thunder" );
	}

	[TestMethod]
	public void UnstartedWeatherAndFractionalClockRestoreWithoutAnEarlyRoll()
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var original = new Rig( template, false );
		Assert.AreEqual( 0, original.Frame( 0.004 ) );
		var saved = original.Capture();
		Assert.IsFalse( saved.Weather!.Started );
		var loaded = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( saved ) );
		var restored = new Rig( template, false, loaded );
		Same( original, restored, "unstarted restore" );
		Assert.AreEqual( saved.Weather.RandomState, restored.Weather!.CaptureSnapshot().RandomState );
		Assert.AreEqual( 1, original.Frame( 0.014 ) ); Assert.AreEqual( 1, restored.Frame( 0.014 ) );
		Assert.IsTrue( restored.Weather.CaptureSnapshot().Started );
		Assert.AreEqual( 1L, restored.Clock.TickCount ); Assert.AreEqual( 1L, restored.Economy.Tick );
		Assert.AreEqual( 1L, restored.Guests.TickCount );
		Same( original, restored, "first natural roll after resume" );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void StormFileRoundtripPreservesPausedClockThunderAndDeterministicContinuation( bool snow )
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var original = new Rig( template, snow );
		Storm( original );
		Assert.AreEqual( snow, original.Weather!.Snow );
		Assert.AreEqual( !snow, original.Weather.Rain );
		original.Frame( 1 ); // Real catch-up limit: diagnostic dropped time is saved too.
		original.Frame( 0.004 );
		original.Economy.Speed = GameSpeed.Paused;
		var snapshot = original.Capture();
		Assert.IsTrue( snapshot.Weather!.CurrentDrops > 0 );
		Assert.IsTrue( snapshot.Weather.PendingStrikes.Count > 0, "the file carries actual due thunder" );
		Assert.IsTrue( snapshot.Clock.DroppedSeconds > 0 );
		Assert.IsTrue( snapshot.Clock.PendingSeconds > 0 );
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-weather-{Guid.NewGuid():N}.otpw" );
		try
		{
			ParkResumeSaveFile.Save( path, snapshot );
			var loaded = ParkResumeSaveFile.Load( path );
			var restored = new Rig( template, snow, loaded );
			Same( original, restored, "full file restore into fresh runtime modules" );
			Assert.AreEqual( snapshot.Weather!.CurrentDrops, restored.Weather!.CurrentDrops );
			Assert.AreEqual( snow, restored.Weather.Snow );
			Assert.AreEqual( !snow, restored.Weather.Rain );
			Assert.AreEqual( snapshot.Weather.PendingQuality, restored.Weather.Forecast );
			var pausedBefore = ParkResumeSaveFile.Serialize( original.Capture() );
			Assert.AreEqual( 0, original.Frame( 20 ) ); Assert.AreEqual( 0, restored.Frame( 20 ) );
			CollectionAssert.AreEqual( pausedBefore, ParkResumeSaveFile.Serialize( original.Capture() ), "pause preserves the pre-frame state, not just equality between both timelines" );
			Same( original, restored, "pause does not change any simulation counter" );
			var due = original.Weather.TakeThunder().ToArray();
			Assert.IsTrue( due.Length > 0, "the once-only comparison cannot pass on two empty queues" );
			CollectionAssert.AreEqual( due, restored.Weather!.TakeThunder().ToArray() );
			Assert.AreEqual( 0, restored.Weather.TakeThunder().Count );
			Same( original, restored, "pending thunder is consumed once" );
			for ( var frame = 0; frame < 300; frame++ )
			{
				var speed = frame % 5 == 0 ? GameSpeed.Paused : frame % 3 == 0 ? GameSpeed.Fast : GameSpeed.Normal;
				original.Economy.Speed = restored.Economy.Speed = speed;
				var elapsed = frame % 7 == 0 ? 0.013 : 0.05;
				Assert.AreEqual( original.Frame( elapsed ), restored.Frame( elapsed ) );
				Same( original, restored, $"frame {frame}" );
			}
			Assert.IsTrue( restored.Economy.Tick > snapshot.Economy.Tick );
			Assert.IsTrue( restored.Weather.CaptureSnapshot().LastTurn > snapshot.Weather!.LastTurn );
		}
		finally { File.Delete( path ); }
	}

	[TestMethod]
	public void MissingOptionalWeatherWaitsForTheFirstSimulationStep()
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var original = new Rig( template, false ); original.Weather = null;
		original.Frame( 0.004 );
		var json = JsonNode.Parse( ParkResumeSaveFile.Serialize( original.Capture() ) )!.AsObject();
		json.Remove( "Weather" );
		var loaded = ParkResumeSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) );
		var restored = new Rig( template, false, loaded );
		Assert.IsNull( restored.Weather );
		Assert.AreEqual( 0, restored.Frame( 0 ) ); Assert.IsNull( restored.Weather );
		original.Frame( 0.014 ); restored.Frame( 0.014 );
		Assert.IsNotNull( restored.Weather );
		Same( original, restored, "absent weather uses the existing lazy first-step initialization" );
	}

	[TestMethod]
	public void CodecAndDestinationWeatherChecksRefuseCorruptionBeforeWeatherMutation()
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var rig = new Rig( template, false ); Storm( rig );
		var saved = rig.Capture();
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Serialize( saved with
		{
			Weather = saved.Weather! with { PendingStrikes = null! }
		} ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Serialize( saved with
		{
			Weather = saved.Weather! with { LastTop = new ParkWeather.ThunderStrikeSnapshot( float.NaN, 300, 0 ) }
		} ) );
		var excessive = saved with { Weather = saved.Weather! with { CurrentDrops = 65 } };
		var loaded = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( excessive ) );
		var before = ParkResumeJson.Serialize( rig.Weather!.CaptureSnapshot() );
		Assert.ThrowsException<InvalidDataException>( () => rig.Weather.RestoreSnapshot( loaded.Weather! ) );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( rig.Weather.CaptureSnapshot() ), "destination settings reject without changing the live weather" );
	}
	[TestMethod]
	public void MalformedNestedWeatherJsonIsRefusedWithoutChangingLiveState()
	{
		var rig = new Rig( ParkResumeSaveFileTests.Envelope(), false );
		Storm( rig );
		var valid = ParkResumeSaveFile.Serialize( rig.Capture() );
		var before = ParkResumeJson.Serialize( rig.Weather!.CaptureSnapshot() );
		foreach ( var corruption in new[] { "missing strikes", "null strikes", "unknown member", "nonfinite coordinate", "overflow coordinate" } )
		{
			var json = JsonNode.Parse( valid )!.AsObject();
			var weather = json["Weather"]!.AsObject();
			switch ( corruption )
			{
				case "missing strikes": weather.Remove( "PendingStrikes" ); break;
				case "null strikes": weather["PendingStrikes"] = null; break;
				case "unknown member": weather["UnexpectedWeatherMember"] = 1; break;
				case "nonfinite coordinate": weather["LastTop"]!["X"] = "NaN"; break;
				case "overflow coordinate": weather["LastTop"]!["X"] = 1e100; break;
			}
			var bytes = Encoding.UTF8.GetBytes( json.ToJsonString() );
			Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( bytes ), corruption );
			CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( rig.Weather.CaptureSnapshot() ) );
		}
	}

}
