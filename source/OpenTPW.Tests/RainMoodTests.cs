using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The rain mood extension (docs/UMBRELLAS.md, phase U4): Theme Park Inc's weather rule for happiness, thirst and hunger, applied once per qualifying day, in the pure rules and in a headless guest simulation.
/// </summary>
// [BIN:TPI-EXE:0x611050 weather rule] the behaviour under test is the Theme Park Inc trace (docs/UMBRELLAS.md); the guests are Theme Park World's
[TestClass]
public class RainMoodTests
{
	private const double TurnSeconds = ParkCalendar.TurnMilliseconds / 1000.0;
	private static readonly RainMoodSettings Settings = RainMoodSettings.Default;

	private sealed class Rig
	{
		public readonly GuestPathGrid Grid = new( 16, 16 );
		public readonly GuestSimulation Simulation;
		public int Weather;

		public Rig( bool rainMood, ulong seed = 5 )
		{
			var guestSettings = GuestTests.CreateSettings();
			for ( var x = 0; x < 12; x++ )
				Grid.SetPath( x, 5, true );
			guestSettings.ExitLevelSeconds = 10_000;
			Simulation = new GuestSimulation( Grid, guestSettings, seed ) { ArrivalsEnabled = false };
			Simulation.UmbrellaWeatherState = () => Weather;
			if ( rainMood )
				Simulation.RainMood = Settings;
		}

		public void RunTurns( int turns )
		{
			var until = Simulation.ParkTurn + turns;
			for ( var tick = 0; Simulation.ParkTurn < until; tick++ )
			{
				Assert.IsTrue( tick < 60 * 60, "the turns pass" );
				Simulation.Tick( GuestSnapshotTests.Tick );
			}
		}
	}

	/// <summary>A guest of <paramref name="days"/> game days and a little (the age is set directly; a day is 23.04 park turns).</summary>
	private static Guest Aged( Rig rig, double days )
	{
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.TimeInParkSeconds = days * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn * TurnSeconds;
		guest.Happiness = 70;
		guest.Thirst = 40;
		guest.Hunger = 40;
		return guest;
	}

	// ---- The rules --------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheShippedKidRowGivesRainStormAndSnowTheirDeltas()
	{
		Assert.AreEqual( (69f, 40f, 40f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherRain, false, Settings ), "rain: -1 happiness" );
		Assert.AreEqual( (69f, 40f, 40f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherStorm, false, Settings ), "storm: -1 happiness" );
		Assert.AreEqual( (71f, 39f, 41f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherSnow, false, Settings ), "snow: +1 happiness, -1 thirst, +1 hunger" );
		Assert.AreEqual( (70f, 40f, 40f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherNone, false, Settings ), "no weather: nothing" );
	}

	[TestMethod]
	public void AnOpenUmbrellaAddsTheModifierToANegativeHappinessDeltaOnly()
	{
		Assert.AreEqual( (70f, 40f, 40f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherRain, true, Settings ), "rain -1 + 1: no loss" );
		Assert.AreEqual( (70f, 40f, 40f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherStorm, true, Settings ) );
		Assert.AreEqual( (71f, 39f, 41f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherSnow, true, Settings ), "a gain is left alone, and so are thirst and hunger" );
		var harsh = Settings with { Kid = Settings.Kid with { Happiness = Settings.Kid.Happiness with { Rain = -3f } }, UmbrellaModifier = 1 };
		Assert.AreEqual( 68f, RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherRain, true, harsh ).Happiness, "-3 + 1 = -2" );
		Assert.AreEqual( 67f, RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherRain, false, harsh ).Happiness );
	}

	[TestMethod]
	public void TheMetersAreClampedToZeroAndOneHundredAsFloats()
	{
		Assert.AreEqual( 0f, RainMoodRules.Apply( 0.5f, 0, 100, UmbrellaRules.WeatherRain, false, Settings ).Happiness, "0.5 - 1 clamps to 0" );
		Assert.AreEqual( (100f, 0f, 100f), RainMoodRules.Apply( 99.5f, 0.5f, 99.5f, UmbrellaRules.WeatherSnow, false, Settings ), "99.5 + 1 clamps to 100, thirst 0.5 - 1 to 0, hunger 99.5 + 1 to 100" );
		Assert.AreEqual( 69.5f, RainMoodRules.Apply( 70.5f, 0, 0, UmbrellaRules.WeatherRain, false, Settings ).Happiness, "fractions stay" );
	}

	[TestMethod]
	public void TheHeatWaveRowNeverAppliesInThemeParkWorld()
	{
		Assert.IsFalse( RainMoodRules.Applies( 0, UmbrellaRules.WeatherHeatWave, 0 ), "a heat wave is not a weather the rule runs for here (TPW has none)" );
		Assert.IsFalse( RainMoodRules.Applies( 0, UmbrellaRules.WeatherNone, 0 ) );
		Assert.AreEqual( 1f, Settings.Kid.Happiness.HeatWave, "the row still carries TPI's heat wave number" );
	}

	[TestMethod]
	public void TheRuleAppliesOnceOnTheFirstDayThenOnceOnEveryFourthDay()
	{
		static long Age( double days ) => (long)(days * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn);
		var rain = UmbrellaRules.WeatherRain;
		Assert.IsTrue( RainMoodRules.Applies( 0, rain, 0 ), "the first update of a visit" );
		Assert.IsFalse( RainMoodRules.Applies( 8, rain, RainMoodRules.DayOf( 0 ) + 1 ), "already applied on day 0" );
		Assert.AreEqual( 0L, RainMoodRules.DayOf( Age( 0.99 ) ), "the whole first day is day 0" );
		for ( var day = 0; day < 12; day++ )
		{
			var age = Age( day + 0.5 );
			Assert.AreEqual( day, RainMoodRules.DayOf( age ), $"day {day}" );
			Assert.AreEqual( day % 4 == 0, UmbrellaRules.DayGateOpen( age ), $"day {day} gate" );
			Assert.AreEqual( day % 4 == 0, RainMoodRules.Applies( age, rain, 0 ), $"day {day} applies" );
		}
	}

	// ---- In the simulation ------------------------------------------------------------------------------------------

	[TestMethod]
	public void AGuestLosesOneHappinessOnItsFirstRainyDayWhateverItsNumberOfUpdates()
	{
		var withRule = new Rig( true );
		var without = new Rig( false );
		var a = Aged( withRule, 0.1 );
		var b = Aged( without, 0.1 );
		withRule.Weather = without.Weather = UmbrellaRules.WeatherRain;
		withRule.RunTurns( 16 );
		without.RunTurns( 16 );
		Assert.AreEqual( b.Happiness - 1f, a.Happiness, 1e-4, "one update on the guest's phase in 16 turns would be four: once per day, not per update" );
		Assert.AreEqual( (b.Thirst, b.Hunger), (a.Thirst, a.Hunger), "rain changes no thirst or hunger in the kid row" );
		Assert.AreEqual( 1L, a.RainMoodDay, "the rule has run on day 0" );
	}

	[TestMethod]
	public void TheRuleRunsAgainOnTheNextQualifyingDayButNotBetween()
	{
		var rig = new Rig( true );
		var guest = Aged( rig, 0.5 );
		rig.Weather = UmbrellaRules.WeatherRain;
		rig.RunTurns( 8 );
		Assert.AreEqual( 1L, guest.RainMoodDay );
		guest.TimeInParkSeconds = 1.5 * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn * TurnSeconds;
		rig.RunTurns( 8 );
		Assert.AreEqual( 1L, guest.RainMoodDay, "day 1 does not qualify" );
		guest.TimeInParkSeconds = 4.2 * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn * TurnSeconds;
		rig.RunTurns( 8 );
		Assert.AreEqual( 5L, guest.RainMoodDay, "day 4 qualifies again" );
	}

	[TestMethod]
	public void RainThatStartsLaterInADayStillAppliesOnceThatDay()
	{
		var rig = new Rig( true );
		var guest = Aged( rig, 0.2 );
		rig.RunTurns( 12 );
		Assert.AreEqual( 0L, guest.RainMoodDay, "clear weather: nothing applied" );
		rig.Weather = UmbrellaRules.WeatherSnow;
		rig.RunTurns( 8 );
		Assert.AreEqual( 1L, guest.RainMoodDay, "the snow arrives, and the day's one application happens then" );
	}

	[TestMethod]
	public void AnOpenUmbrellaCancelsTheLossAndAClosedOneDoesNot()
	{
		float Loss( int umbrellaState )
		{
			var rain = new Rig( true );
			var dry = new Rig( false );
			var a = Aged( rain, 0.1 );
			var b = Aged( dry, 0.1 );
			a.UmbrellaState = b.UmbrellaState = umbrellaState;
			rain.Weather = dry.Weather = UmbrellaRules.WeatherRain;
			// the weather of the umbrella rule would move the pending states; only the mood step is compared, so the same rig runs both
			rain.RunTurns( 4 );
			dry.RunTurns( 4 );
			return b.Happiness - a.Happiness;
		}
		Assert.AreEqual( 0f, Loss( UmbrellaRules.StateOpen ), 1e-4, "open: the umbrella's 1 cancels the rain's -1" );
		Assert.AreEqual( 1f, Loss( UmbrellaRules.StateNone ), 1e-4, "no umbrella" );
		Assert.AreEqual( 1f, Loss( UmbrellaRules.StateClosed ), 1e-4, "a closed umbrella protects nothing" );
	}

	[TestMethod]
	public void OnlyAnUmbrellaThatWasAlreadyOpenWhenTheStepBeganSoftensTheLoss()
	{
		// [BIN:TPI-EXE:0x611334] the switch on the state: state 4 is the modifier (0x6111BE), state 1 sets 4 (0x6111E3) and a due 3 calls 0x402d8d; none of those reaches the modifier in the same call
		float Loss( int umbrellaState )
		{
			var rain = new Rig( true );
			var dry = new Rig( false );
			var a = Aged( rain, 0.1 );
			var b = Aged( dry, 0.1 );
			rain.Simulation.SetUmbrellaStateForTest( a, umbrellaState );
			dry.Simulation.SetUmbrellaStateForTest( b, umbrellaState );
			rain.Weather = dry.Weather = UmbrellaRules.WeatherRain;
			rain.RunTurns( 4 );
			dry.RunTurns( 4 );
			Assert.AreEqual( b.UmbrellaState, a.UmbrellaState, $"the umbrella step is the same with the mood rule on, from state {umbrellaState}" );
			return b.Happiness - a.Happiness;
		}
		Assert.AreEqual( 0f, Loss( UmbrellaRules.StateOpen ), 1e-4, "already open: no loss" );
		Assert.AreEqual( 1f, Loss( UmbrellaRules.StateClosing ), 1e-4, "state 1 becomes 4 in the umbrella step, after the modifier's case: the guest still loses 1" );
		Assert.AreEqual( 1f, Loss( UmbrellaRules.StateOpening ), 1e-4, "a pending open that is due opens in the step, and the guest still loses 1" );
		Assert.AreEqual( 1f, Loss( UmbrellaRules.StateClosed ), 1e-4, "closed: loses 1" );
	}

	[TestMethod]
	public void AnImportedGuestWithoutATimeInTheParkCountsItsRainMoodAgeFromTheLoad()
	{
		// [EXT:rain-mood-imported-age] user-approved 2026-10-18 ("Tellen vanaf het laden")
		var rig = new Rig( true );
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		(guest.Happiness, guest.Thirst, guest.Hunger) = (70f, 40f, 40f);
		guest.TimeInParkSeconds = null;
		guest.RainMoodAgeStartTurn = rig.Simulation.ParkTurn;
		rig.Weather = UmbrellaRules.WeatherRain;
		rig.RunTurns( 8 );
		Assert.AreEqual( 1L, guest.RainMoodDay, "the load day counts as day 0 and applies once" );
		var before = guest.Happiness;
		rig.RunTurns( 40 );
		Assert.AreEqual( 1L, guest.RainMoodDay, "days 1 to 3 after the load do not qualify" );
		Assert.AreEqual( before, guest.Happiness, 1e-3, "and cost nothing, the umbrella-less guest's needs step aside" );
		rig.RunTurns( 60 );
		Assert.AreEqual( 5L, guest.RainMoodDay, "four days after the load the rule applies again, as it does to any guest" );
		guest.RainMoodAgeStartTurn = null;
		Assert.AreEqual( 0L, rig.Simulation.RainMoodAgeTurnsOf( guest ), "a guest with neither a time nor a stamp counts as new" );
	}

	[TestMethod]
	public void TheLoadTurnOfAnImportedGuestIsRecordedOnlyWithTheExtensionOnAndRoundTripsThroughASave()
	{
		static Guest Import( bool rainMood, uint arrivalDate = 9000 )
		{
			var rig = new Rig( rainMood );
			var body = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields { Position = (6 * 256 + 128, 5 * 256 + 128), State = 7, PersonType = 0, ArrivalDate = arrivalDate } );
			var payload = new byte[8 + body.Length];
			body.CopyTo( payload, 8 );
			var saved = SaveGuest.Read( payload, new SaveThing( 30, SaveThingModel.Guest, 0, 0, 8, body.Length ) );
			rig.Simulation.ImportOriginal( new[] { saved }, 755, _ => 0, _ => 0 );
			return rig.Simulation.Guests.Single();
		}
		var on = Import( true );
		Assert.IsNull( on.TimeInParkSeconds, "a stamp after the save's own turn is no date: no known time in the park" );
		Assert.AreEqual( 0L, on.RainMoodAgeStartTurn, "the park turn of the load" );
		Assert.IsNull( Import( false ).RainMoodAgeStartTurn, "extension off: nothing recorded, saves keep their bytes" );
		Assert.IsNull( Import( true, 100 ).RainMoodAgeStartTurn, "a guest with a known arrival counts from it, as before" );
		var rig = new Rig( true );
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.TimeInParkSeconds = null;
		guest.RainMoodAgeStartTurn = 12;
		var snapshot = rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid );
		StringAssert.Contains( JsonSerializer.Serialize( snapshot.Guests.Single() ), "\"RainMoodAgeStartTurn\":12" );
		Assert.IsFalse( JsonSerializer.Serialize( snapshot.Guests.Single() with { RainMoodAgeStartTurn = null } ).Contains( "RainMoodAgeStartTurn", StringComparison.Ordinal ), "a guest without it has no such field" );
		var restored = new Rig( true );
		restored.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( 12L, restored.Simulation.Guests.Single().RainMoodAgeStartTurn );
		Assert.AreEqual( JsonSerializer.Serialize( snapshot ), JsonSerializer.Serialize( restored.Simulation.CaptureSnapshot() ) );
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( snapshot with { Guests = snapshot.Guests.Select( g => g with { RainMoodAgeStartTurn = -1 } ).ToArray() } ).IsValid, "negative" );
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( snapshot with { Guests = snapshot.Guests.Select( g => g with { TimeInParkSeconds = 5 } ).ToArray() } ).IsValid, "a stamp with a known time is refused" );
		ulong Hash( long? start )
		{
			var other = new Rig( true, 9 );
			var one = other.Simulation.SpawnInPark( 6, 5 );
			one.TimeInParkSeconds = null;
			one.RainMoodAgeStartTurn = start;
			return other.Simulation.ComputeStateHash();
		}
		Assert.AreNotEqual( Hash( null ), Hash( 0 ), "a set stamp counts" );
		Assert.AreNotEqual( Hash( 0 ), Hash( 4 ) );
	}

	[TestMethod]
	public void TheLevelLeavesTheRuleOffByDefaultAndHandsTheShippedRowsWhileTheGateIsOn()
	{
		Assert.IsNull( Level.RainMoodFor( FeatureGates.RainMood ), "the extension is off in a default run: the guests never run the rule" );
		Assert.IsNull( Level.RainMoodFor( false ) );
		Assert.AreEqual( RainMoodSettings.Default, Level.RainMoodFor( true ) );
	}

	[TestMethod]
	public void GuestsInsideAnAttractionAreAffectedToo()
	{
		var rule = new Rig( true );
		var plain = new Rig( false );
		var ride = new RideVisitorBridge( 2, "Shop", RideVisitorKind.Shop, 1, 60, 30 ) { ExitCell = (6, 5) };
		var a = Aged( rule, 0.1 );
		var b = Aged( plain, 0.1 );
		rule.Weather = plain.Weather = UmbrellaRules.WeatherRain;
		((IRideVisitorHost)rule.Simulation).OnVisitorBoarded( ride, a.Id );
		((IRideVisitorHost)plain.Simulation).OnVisitorBoarded( ride, b.Id );
		Assert.AreEqual( GuestState.Using, a.State );
		rule.RunTurns( 8 );
		plain.RunTurns( 8 );
		Assert.AreEqual( GuestState.Using, a.State, "still inside" );
		Assert.AreEqual( b.Happiness - 1f, a.Happiness, 1e-4, "a rider is touched like any guest (the original rule runs for every guest, wherever it is)" );
	}

	[TestMethod]
	public void WithTheRuleOffTheWeatherChangesNothingAboutAGuestWithoutAnUmbrella()
	{
		ulong Hash( int weather )
		{
			var rig = new Rig( false, 9 );
			Aged( rig, 0.1 );
			rig.Weather = weather;
			rig.RunTurns( 40 );
			return rig.Simulation.ComputeStateHash();
		}
		Assert.AreEqual( Hash( UmbrellaRules.WeatherNone ), Hash( UmbrellaRules.WeatherRain ), "rain mood off: the state hash is the same in rain and in clear weather" );
		Assert.AreEqual( Hash( UmbrellaRules.WeatherNone ), Hash( UmbrellaRules.WeatherSnow ) );
	}

	[TestMethod]
	public void TheUmbrellaStatesStillMoveWithTheRuleOffAndWithItOn()
	{
		foreach ( var rule in new[] { false, true } )
		{
			var rig = new Rig( rule );
			var guest = Aged( rig, 0.1 );
			guest.UmbrellaState = UmbrellaRules.StateClosed;
			rig.Weather = UmbrellaRules.WeatherRain;
			rig.RunTurns( 24 );
			Assert.AreEqual( UmbrellaRules.StateOpen, guest.UmbrellaState, $"the umbrella opens in the rain on a qualifying day, rain mood {rule}" );
		}
	}

	// ---- The saved state --------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheRainMoodDayRoundTripsIsValidatedAndJoinsTheHashOnlyOnceSet()
	{
		var rig = new Rig( true );
		var fresh = rig.Simulation.SpawnInPark( 6, 5 );
		var applied = rig.Simulation.SpawnInPark( 7, 5 );
		applied.RainMoodDay = 5;
		var snapshot = rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid );
		Assert.IsFalse( JsonSerializer.Serialize( snapshot.Guests.Single( guest => guest.Id == fresh.Id ) ).Contains( "RainMoodDay", StringComparison.Ordinal ), "a guest the rule has not touched has no such field: older bytes stay" );
		StringAssert.Contains( JsonSerializer.Serialize( snapshot.Guests.Single( guest => guest.Id == applied.Id ) ), "\"RainMoodDay\":5" );
		var restored = new Rig( true );
		restored.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( 5L, restored.Simulation.Guests.Single( guest => guest.Id == applied.Id ).RainMoodDay );
		Assert.AreEqual( JsonSerializer.Serialize( snapshot ), JsonSerializer.Serialize( restored.Simulation.CaptureSnapshot() ) );
		var bad = snapshot with { Guests = snapshot.Guests.Select( guest => guest with { RainMoodDay = -1 } ).ToArray() };
		Assert.IsFalse( restored.Simulation.ValidateSnapshot( bad ).IsValid, "a negative day must not validate" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => restored.Simulation.RestoreSnapshot( bad ) );
		ulong Hash( long day )
		{
			var other = new Rig( true, 9 );
			other.Simulation.SpawnInPark( 6, 5 ).RainMoodDay = day;
			return other.Simulation.ComputeStateHash();
		}
		Assert.AreNotEqual( Hash( 0 ), Hash( 1 ), "a set day counts" );
		Assert.AreNotEqual( Hash( 1 ), Hash( 2 ) );
		var older = System.Text.RegularExpressions.Regex.Replace( JsonSerializer.Serialize( snapshot.Guests.Single( guest => guest.Id == applied.Id ) ), "\"RainMoodDay\":\\d+,?", "" ).Replace( ",}", "}" );
		Assert.AreEqual( 0L, JsonSerializer.Deserialize<GuestSnapshot>( older )!.RainMoodDay, "an older save loads as never applied" );
	}

	[TestMethod]
	public void AParkSavedMidRainContinuesIdenticallyAfterAResume()
	{
		static void Weather( Rig rig ) => rig.Simulation.UmbrellaWeatherState = () => rig.Simulation.TickCount < 900 ? UmbrellaRules.WeatherRain : rig.Simulation.TickCount < 1500 ? UmbrellaRules.WeatherNone : UmbrellaRules.WeatherSnow;
		var park = new Rig( true, 11 );
		Weather( park );
		for ( var index = 0; index < 8; index++ )
		{
			var guest = park.Simulation.SpawnInPark( 4, 5 );
			guest.TimeInParkSeconds = (index * 12.0) * TurnSeconds;
			if ( index % 3 == 0 )
				(guest.UmbrellaState, guest.UmbrellaType) = (UmbrellaRules.StateOpen, index % 3);
		}
		for ( var tick = 0; tick < 300; tick++ )
			park.Simulation.Tick( GuestSnapshotTests.Tick );
		Assert.IsTrue( park.Simulation.Guests.Any( guest => guest.RainMoodDay != 0 ), "the rule ran before the snapshot" );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.IsTrue( park.Simulation.ValidateSnapshot( snapshot ).IsValid );
		var resumed = new Rig( true, 11 );
		Weather( resumed );
		resumed.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( park.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash() );
		for ( var tick = 0; tick < 2400; tick++ )
		{
			park.Simulation.Tick( GuestSnapshotTests.Tick );
			resumed.Simulation.Tick( GuestSnapshotTests.Tick );
			if ( tick % 40 == 0 )
				Assert.AreEqual( park.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash(), $"the parks diverged at tick {tick}" );
		}
		Assert.AreEqual( JsonSerializer.Serialize( park.Simulation.CaptureSnapshot() ), JsonSerializer.Serialize( resumed.Simulation.CaptureSnapshot() ) );
	}

	// ---- The extension ----------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheExtensionIsOffByDefaultAlwaysRecordedWhileOnAndNotInTheThemeParkIncRun()
	{
		var extension = OfficialExtensions.RainMood;
		Assert.AreEqual( "opentpw.rain-mood", extension.Id );
		Assert.IsFalse( extension.DefaultEnabled );
		Assert.IsTrue( extension.AffectsSimulation );
		Assert.IsTrue( extension.PlayerSelectable );
		Assert.IsFalse( ParkResumeMods.UsedOnlyExtensionIds.Contains( extension.Id ), "recorded in every park while on (it changes every park with weather)" );
		Assert.IsFalse( FeatureGates.RainMood, "off in a default run" );
		ModSet Set( bool enabled, GameEdition edition ) => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
			new ModSettings { Enabled = new Dictionary<string, bool> { [extension.Id] = enabled } }, null, edition, OfficialExtensions.All );
		using ( OpenTpwExtensions.Use( Set( true, GameEdition.ThemeParkWorld ) ) )
		{
			Assert.IsTrue( FeatureGates.RainMood );
			var recorded = ParkResumeMods.Capture( OpenTpwExtensions.Active, ParkResumeMods.InUse( null, false, false ) );
			CollectionAssert.Contains( recorded.Select( mod => mod.Id ).ToArray(), extension.Id, "recorded although no pool and no umbrella is in the park" );
			var edition = GameEditions.Current;
			try
			{
				GameEditions.Select( GameEdition.ThemeParkInc );
				Assert.IsFalse( FeatureGates.RainMood, "the extension is not offered in a Theme Park Inc run (OpenTPW does not implement the rule there)" );
			}
			finally
			{
				GameEditions.Select( edition );
			}
			// [EXT:rain-mood-extension] a Theme Park Inc run with the extension switched on has the gate off, so it records nothing and refuses nothing for it
			try
			{
				GameEditions.Select( GameEdition.ThemeParkInc );
				Assert.IsFalse( ( ParkResumeMods.Capture( OpenTpwExtensions.Active, ParkResumeMods.InUse( null, false, false ) ) ?? Array.Empty<ParkModRecord>() ).Any( mod => mod.Id == extension.Id ), "not recorded while the edition gate is off" );
				Assert.IsNull( ParkSharing.ModsRefusal(), "and no export refusal on its account" );
				Assert.AreEqual( 0, ParkResumeMods.Unmet( null, OpenTpwExtensions.Active ).Count, "nor an unmet requirement" );
			}
			finally
			{
				GameEditions.Select( edition );
			}
			Assert.IsNull( ParkSharing.ModsRefusal(), "in a Theme Park World run the extension is an official one: the export records it and refuses nothing" );
			Assert.AreEqual( extension.Id, ParkSharing.ExtensionUses( (Level?)null )!.Single( use => use.Id == extension.Id ).Id );
		}
		using ( OpenTpwExtensions.Use( Set( false, GameEdition.ThemeParkWorld ) ) )
			Assert.IsFalse( ( ParkResumeMods.Capture( OpenTpwExtensions.Active, ParkResumeMods.InUse( null, false, false ) ) ?? Array.Empty<ParkModRecord>() ).Any( mod => mod.Id == extension.Id ) );
	}
}
