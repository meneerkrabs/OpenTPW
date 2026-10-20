using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The cold rule (<c>opentpw.winter-cold</c>, docs/WINTER.md, phase W5): in snow a guest loses happiness once per qualifying day unless it is dressed warmly, in the pure rules, in a headless guest simulation, in saves and in the
/// extension's registration. Nothing of it is Theme Park World's behaviour, so every rule under test is the extension's own.
/// </summary>
// [EXT:winter-cold] OpenTPW's own rule; the day gate and the once-per-day rule are the rain mood's ([BIN:TPI-EXE:0x611050] day gate)
[TestClass]
public class WinterColdTests
{
	private const double TurnSeconds = ParkCalendar.TurnMilliseconds / 1000.0;
	private static readonly WinterColdSettings Settings = WinterColdSettings.Default;
	private const int Snow = UmbrellaRules.WeatherSnow;

	private static SamSettings HalloweenBands() => new( new[] { SamDocument.Parse(
		"WeatherEffects.QualityForSnowLow\t0\nWeatherEffects.QualityForSnowHigh\t25\nWeatherEffects.QualityForRainLow\t15\nWeatherEffects.QualityForRainHigh\t45\n" +
		"WeatherEffects.QualityForLightningLow\t0\nWeatherEffects.QualityForLightningHigh\t25\nWeatherEffects.MaxRaindrops\t600\nWeather.DaysOfWarning\t4\nWeather.DaysBetweenChanges\t7\nWeather.SpeedOfChange\t20\n", "test" ) } );

	private sealed class Rig
	{
		public readonly GuestPathGrid Grid = new( 16, 16 );
		public readonly GuestSimulation Simulation;
		public int Weather;
		public long Day;

		public Rig( bool cold, bool rainMood = false, ulong seed = 5 )
		{
			var guestSettings = GuestTests.CreateSettings();
			for ( var x = 0; x < 12; x++ )
				Grid.SetPath( x, 5, true );
			guestSettings.ExitLevelSeconds = 100_000;
			Simulation = new GuestSimulation( Grid, guestSettings, seed ) { ArrivalsEnabled = false };
			Simulation.UmbrellaWeatherState = () => Weather;
			Simulation.WinterDay = () => Day;
			if ( cold )
				Simulation.WinterCold = Settings;
			if ( rainMood )
				Simulation.RainMood = RainMoodSettings.Default;
		}

		/// <summary>Real Halloween snow: a <see cref="ParkWeather"/> of the Halloween bands rolled to quality 10, where it snows and flashes lightning at once. The umbrellas' map (and so the rain mood) reads a storm, the winter signal reads snow.</summary>
		public void HalloweenSnow()
		{
			var weather = new ParkWeather( HalloweenBands(), 4, 10, 10 );
			weather.ApplyQuality( 10 );
			Assert.IsTrue( weather.Snow && weather.Lightning, "Halloween snow comes with lightning" );
			Simulation.UmbrellaWeatherState = () => UmbrellaRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow );
			Simulation.WinterWeatherState = () => WinterRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow );
			Assert.AreEqual( UmbrellaRules.WeatherStorm, Simulation.UmbrellaWeatherState() );
			Assert.AreEqual( Snow, Simulation.WinterWeatherState() );
		}

		public void RunTurns( long turns )
		{
			var until = Simulation.ParkTurn + turns;
			for ( var tick = 0; Simulation.ParkTurn < until; tick++ )
			{
				Assert.IsTrue( tick < 60 * 600, "the turns pass" );
				Simulation.Tick( GuestSnapshotTests.Tick );
			}
		}
	}

	private static Guest Aged( Rig rig, double days, int items = 0 )
	{
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.TimeInParkSeconds = days * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn * TurnSeconds;
		guest.Happiness = 70;
		guest.Thirst = 40;
		guest.Hunger = 40;
		rig.Simulation.SetWarmItemsForTest( guest, items );
		return guest;
	}

	// ---- The pure rules ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheDeltaIsMinusTwoAndEachProtectionGivesOneBackCappedAtZero()
	{
		Assert.AreEqual( -2, WinterColdRules.Delta( 0, false, Settings ), "undressed: -2" );
		Assert.AreEqual( -1, WinterColdRules.Delta( WinterRules.Hat, false, Settings ), "a hat: +1" );
		Assert.AreEqual( -1, WinterColdRules.Delta( WinterRules.Earmuffs, false, Settings ), "earmuffs count like a hat" );
		Assert.AreEqual( -1, WinterColdRules.Delta( WinterRules.Mittens, false, Settings ), "mittens: +1" );
		Assert.AreEqual( -1, WinterColdRules.Delta( 0, true, Settings ), "a recent glühwein: +1" );
		Assert.AreEqual( 0, WinterColdRules.Delta( WinterRules.Hat | WinterRules.Mittens, false, Settings ), "head item and mittens: back to 0" );
		Assert.AreEqual( 0, WinterColdRules.Delta( WinterRules.Earmuffs, true, Settings ), "a head item and a glühwein: back to 0" );
		Assert.AreEqual( 0, WinterColdRules.Delta( WinterRules.AllItems & ~WinterRules.Earmuffs, true, Settings ), "all three: +1 more is capped at 0, never a cheer" );
	}

	[TestMethod]
	public void TheMetersMoveOnlyByTheDeltaAndStayInRange()
	{
		Assert.AreEqual( 68f, WinterColdRules.Apply( 70, 0, false, Settings ) );
		Assert.AreEqual( 70f, WinterColdRules.Apply( 70, WinterRules.Hat | WinterRules.Mittens, false, Settings ) );
		Assert.AreEqual( 0f, WinterColdRules.Apply( 1.5f, 0, false, Settings ), "clamped at 0" );
	}

	[TestMethod]
	public void TheRuleRunsInSnowOnlyAndOnTheRainMoodsDays()
	{
		static long Age( double days ) => (long)(days * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn);
		Assert.IsTrue( WinterColdRules.Applies( 0, Snow, 0 ), "the first update of a visit" );
		Assert.IsFalse( WinterColdRules.Applies( 0, UmbrellaRules.WeatherRain, 0 ), "rain is the rain mood's, not the cold's" );
		Assert.IsFalse( WinterColdRules.Applies( 0, UmbrellaRules.WeatherStorm, 0 ) );
		Assert.IsFalse( WinterColdRules.Applies( 0, UmbrellaRules.WeatherNone, 0 ), "no weather, no cold" );
		Assert.IsFalse( WinterColdRules.Applies( 8, Snow, RainMoodRules.DayOf( 0 ) + 1 ), "already applied on day 0" );
		for ( var day = 0; day < 12; day++ )
		{
			var age = Age( day + 0.5 );
			Assert.AreEqual( day % 4 == 0, WinterColdRules.Applies( age, Snow, 0 ), $"day {day}" );
			Assert.AreEqual( RainMoodRules.Applies( age, Snow, 0 ), WinterColdRules.Applies( age, Snow, 0 ), $"day {day}: the same gate as the rain mood in snow" );
		}
	}

	[TestMethod]
	public void AGluehweinWarmsForTheRestOfItsDayAndTheNextDays()
	{
		Assert.AreEqual( 0L + 1 + 1, WinterColdRules.WarmUntilDayOf( 0, Settings ) );
		Assert.IsTrue( WinterColdRules.IsWarmed( WinterColdRules.WarmUntilDayOf( 5, Settings ), 5 ), "the day of the drink" );
		Assert.IsTrue( WinterColdRules.IsWarmed( WinterColdRules.WarmUntilDayOf( 5, Settings ), 6 ), "the next day" );
		Assert.IsFalse( WinterColdRules.IsWarmed( WinterColdRules.WarmUntilDayOf( 5, Settings ), 7 ), "two days later it is over" );
		Assert.IsFalse( WinterColdRules.IsWarmed( 0, 0 ), "no drink, never warmed" );
		Assert.AreEqual( 8L, WinterColdRules.WarmUntilDayOf( 5, Settings with { WarmEffectDays = 2 } ) );
	}

	[TestMethod]
	public void TheRainMoodsSnowHappinessIsLeftOutOnlyWhenColdReplacesIt()
	{
		var rain = RainMoodSettings.Default;
		Assert.AreEqual( (71f, 39f, 41f), RainMoodRules.Apply( 70, 40, 40, Snow, false, rain ), "alone: snow +1" );
		Assert.AreEqual( (70f, 39f, 41f), RainMoodRules.Apply( 70, 40, 40, Snow, false, rain, coldReplacesHappiness: true ), "replaced: happiness 0, thirst -1 and hunger +1 stay" );
		Assert.AreEqual( (69f, 40f, 40f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherRain, false, rain ), "rain is untouched" );
		Assert.AreEqual( (69f, 40f, 40f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherStorm, false, rain ), "a storm alone is untouched" );
		// [EXT:winter-cold-replaces-snow-cheer] "Kou vervangt storm-humeur": the caller says the winter signal reads snow although the map reads a storm (Halloween); the storm row's happiness -1 is left out, its thirst and hunger (0) stay
		Assert.AreEqual( (70f, 40f, 40f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherStorm, false, rain, coldReplacesHappiness: true ), "Halloween snow: the storm's -1 is replaced too" );
		var storm = rain with { Kid = rain.Kid with { Thirst = rain.Kid.Thirst with { Storm = -3f }, Hunger = rain.Kid.Hunger with { Storm = 2f } } };
		Assert.AreEqual( (70f, 37f, 42f), RainMoodRules.Apply( 70, 40, 40, UmbrellaRules.WeatherStorm, false, storm, coldReplacesHappiness: true ), "the storm row's thirst and hunger effects stay" );
	}

	// ---- In the simulation ------------------------------------------------------------------------------------------

	[TestMethod]
	public void AnUndressedGuestLosesTwoHappinessOnItsFirstSnowyDayWhateverItsNumberOfUpdates()
	{
		var withRule = new Rig( true );
		var without = new Rig( false );
		var a = Aged( withRule, 0.1 );
		var b = Aged( without, 0.1 );
		withRule.Weather = without.Weather = Snow;
		withRule.RunTurns( 16 );
		without.RunTurns( 16 );
		Assert.AreEqual( b.Happiness - 2f, a.Happiness, 1e-4, "once per day, not per update" );
		Assert.AreEqual( 1L, a.ColdMoodDay, "the rule has run on day 0" );
		Assert.AreEqual( (b.Thirst, b.Hunger), (a.Thirst, a.Hunger), "the cold rule moves happiness only" );
	}

	[TestMethod]
	public void HalloweenSnowWithLightningIsSnowForTheColdRuleButAStormForTheUmbrellasMap()
	{
		// Halloween's snow band is its lightning band: the umbrellas' map reads a storm there, the winter shops' signal reads the snow, and the cold rule follows the shops
		Assert.AreEqual( UmbrellaRules.WeatherStorm, UmbrellaRules.WeatherStateOf( rain: false, lightning: true, snow: true ) );
		Assert.AreEqual( Snow, WinterRules.WeatherStateOf( rain: false, lightning: true, snow: true ) );
		var cold = new Rig( cold: true );
		cold.Simulation.UmbrellaWeatherState = () => UmbrellaRules.WeatherStorm;
		cold.Simulation.WinterWeatherState = () => Snow;
		var without = new Rig( false );
		without.Simulation.UmbrellaWeatherState = () => UmbrellaRules.WeatherStorm;
		var coldGuest = Aged( cold, 0.1 );
		var plainGuest = Aged( without, 0.1 );
		cold.RunTurns( 16 );
		without.RunTurns( 16 );
		Assert.AreEqual( plainGuest.Happiness - 2f, coldGuest.Happiness, 1e-4, "an undressed guest cools in Halloween snow although the umbrella map says storm" );
		Assert.AreEqual( 1L, coldGuest.ColdMoodDay );
		var elsewhere = new Rig( cold: true );
		elsewhere.Simulation.UmbrellaWeatherState = () => UmbrellaRules.WeatherStorm;
		elsewhere.Simulation.WinterWeatherState = () => UmbrellaRules.WeatherStorm;
		var stormGuest = Aged( elsewhere, 0.1 );
		elsewhere.RunTurns( 16 );
		Assert.AreEqual( 0L, stormGuest.ColdMoodDay, "a real storm is not cold" );
	}

	[TestMethod]
	public void AGuestInRainOrWithoutWeatherIsLeftAlone()
	{
		foreach ( var weather in new[] { UmbrellaRules.WeatherNone, UmbrellaRules.WeatherRain, UmbrellaRules.WeatherStorm } )
		{
			var withRule = new Rig( true );
			var without = new Rig( false );
			var a = Aged( withRule, 0.1 );
			var b = Aged( without, 0.1 );
			withRule.Weather = without.Weather = weather;
			withRule.RunTurns( 16 );
			without.RunTurns( 16 );
			Assert.AreEqual( b.Happiness, a.Happiness, 1e-4, $"weather {weather}" );
			Assert.AreEqual( 0L, a.ColdMoodDay );
		}
	}

	[TestMethod]
	public void WarmItemsAndAGluehweinSoftenTheColdInTheSimulation()
	{
		var results = new List<float>();
		foreach ( var (items, warmed) in new[] { (0, false), (WinterRules.Hat, false), (WinterRules.Hat | WinterRules.Mittens, false), (WinterRules.Mittens, true), (WinterRules.Earmuffs | WinterRules.Mittens, true) } )
		{
			var rig = new Rig( true );
			var guest = Aged( rig, 0.1, items );
			if ( warmed )
				guest.WarmUntilDay = 5;
			rig.Day = 3;
			rig.Weather = Snow;
			var before = guest.Happiness;
			rig.RunTurns( 16 );
			results.Add( guest.Happiness - before );
		}
		var plain = new Rig( false );
		var control = Aged( plain, 0.1 );
		plain.Weather = Snow;
		plain.RunTurns( 16 );
		var natural = control.Happiness - 70f;
		CollectionAssert.AreEqual( new[] { -2f, -1f, 0f, 0f, 0f }, results.Select( delta => MathF.Round( delta - natural, 3 ) ).ToArray(), "0, 1, 2 and 3 protections (the needs step's own change removed)" );
	}

	[TestMethod]
	public void AGluehweinWarmthLapsesWhenTheParkDayPassesIt()
	{
		var rig = new Rig( true );
		var guest = Aged( rig, 0.1 );
		guest.WarmUntilDay = 4;
		rig.Day = 4;
		rig.Weather = Snow;
		var plain = new Rig( false );
		var control = Aged( plain, 0.1 );
		plain.Weather = Snow;
		rig.RunTurns( 16 );
		plain.RunTurns( 16 );
		Assert.AreEqual( control.Happiness - 2f, guest.Happiness, 1e-4, "on the day it lapses (exclusive) the guest is cold again" );
	}

	[TestMethod]
	public void ColdReplacesTheRainMoodsSnowCheerWhenBothAreOnAndNeitherInterferesAlone()
	{
		static float Happiness( bool cold, bool rainMood, int items = 0 )
		{
			var rig = new Rig( cold, rainMood );
			var guest = Aged( rig, 0.1, items );
			rig.Weather = Snow;
			rig.RunTurns( 16 );
			return guest.Happiness;
		}
		var baseline = Happiness( false, false );
		Assert.AreEqual( baseline + 1f, Happiness( false, true ), 1e-4, "rain mood alone: snow cheers" );
		Assert.AreEqual( baseline - 2f, Happiness( true, false ), 1e-4, "cold alone: -2" );
		Assert.AreEqual( baseline - 2f, Happiness( true, true ), 1e-4, "both: the cold rule replaces the +1, an undressed guest nets -2 (not -1)" );
		Assert.AreEqual( baseline, Happiness( true, true, WinterRules.Hat | WinterRules.Mittens ), 1e-4, "both, dressed: 0, not the rain mood's +1" );
	}

	[TestMethod]
	public void InHalloweenSnowColdReplacesTheStormMoodAndTheStormsThirstAndHungerStay()
	{
		// [EXT:winter-cold-replaces-snow-cheer] "Kou vervangt storm-humeur": real Halloween ParkWeather snow (snow and lightning at once). With cold on only the cold rule's happiness counts: -2 undressed, 0 dressed; the storm row's thirst and hunger stay
		var storm = RainMoodSettings.Default with { Kid = RainMoodSettings.Default.Kid with { Thirst = RainMoodSettings.Default.Kid.Thirst with { Storm = -3f }, Hunger = RainMoodSettings.Default.Kid.Hunger with { Storm = 2f } } };
		Guest Run( bool cold, bool rainMood, int items = 0 )
		{
			var rig = new Rig( cold, rainMood );
			if ( rainMood )
				rig.Simulation.RainMood = storm;
			var guest = Aged( rig, 0.1, items );
			rig.HalloweenSnow();
			rig.RunTurns( 16 );
			return guest;
		}
		var baseline = Run( false, false );
		var stormMood = Run( false, true );
		Assert.AreEqual( baseline.Happiness - 1f, stormMood.Happiness, 1e-4, "rain mood alone: Halloween's snow is a storm, -1" );
		Assert.AreEqual( baseline.Thirst - 3f, stormMood.Thirst, 1e-4 );
		Assert.AreEqual( baseline.Hunger + 2f, stormMood.Hunger, 1e-4 );
		var undressed = Run( true, true );
		Assert.AreEqual( baseline.Happiness - 2f, undressed.Happiness, 1e-4, "cold + rain mood, undressed: -2 (the cold rule only, not -3)" );
		Assert.AreEqual( baseline.Happiness, Run( true, true, WinterRules.Hat | WinterRules.Mittens ).Happiness, 1e-4, "cold + rain mood, dressed: 0 (not the storm's -1)" );
		Assert.AreEqual( baseline.Thirst - 3f, undressed.Thirst, 1e-4, "the storm row's thirst still applies" );
		Assert.AreEqual( baseline.Hunger + 2f, undressed.Hunger, 1e-4, "the storm row's hunger still applies" );
		Assert.AreEqual( Run( true, false ).Happiness, undressed.Happiness, 1e-4, "the rain mood adds nothing to happiness there" );
	}

	[TestMethod]
	public void WithColdOffHalloweenSnowStaysTheRainMoodsStorm()
	{
		// the decision is scoped to cold being on: off, the rain mood reads the storm exactly as before, whatever the winter signal says
		var rig = new Rig( cold: false, rainMood: true );
		var guest = Aged( rig, 0.1 );
		rig.HalloweenSnow();
		var plain = new Rig( false );
		var control = Aged( plain, 0.1 );
		plain.HalloweenSnow();
		rig.RunTurns( 16 );
		plain.RunTurns( 16 );
		Assert.AreEqual( control.Happiness - 1f, guest.Happiness, 1e-4 );
	}

	[TestMethod]
	public void ColdAndTheRainMoodKeepTheirOwnDayMarks()
	{
		var rig = new Rig( true, rainMood: true );
		var guest = Aged( rig, 0.1 );
		rig.Weather = Snow;
		rig.RunTurns( 16 );
		Assert.AreEqual( 1L, guest.RainMoodDay );
		Assert.AreEqual( 1L, guest.ColdMoodDay );
		var thirst = guest.Thirst;
		rig.RunTurns( 16 );
		Assert.AreEqual( 1L, guest.ColdMoodDay, "once per qualifying day" );
		Assert.IsTrue( guest.Thirst <= thirst + 0.01f || guest.Thirst >= thirst, "thirst keeps following the needs step" );
	}

	[TestMethod]
	public void AnImportedGuestWithoutATimeInTheParkCountsItsColdAgeFromTheLoadToo()
	{
		// the same age rule the rain mood uses (RainMoodAgeTurnsOf); the import marks the guest while either rule is on
		var rig = new Rig( true );
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.TimeInParkSeconds = null;
		typeof( Guest ).GetProperty( nameof( Guest.RainMoodAgeStartTurn ) )!.SetValue( guest, 10L );
		Assert.AreEqual( Math.Max( 0, rig.Simulation.ParkTurn - 10 ), rig.Simulation.RainMoodAgeTurnsOf( guest ) );
	}

	[TestMethod]
	public void AGluehweinStallWarmsOnlyWithTheRuleOnAndWritesNoDrawOrState()
	{
		static (Rig Rig, Guest Guest) Visit( bool cold )
		{
			var rig = new Rig( cold, seed: 9 );
			var stall = new RideVisitorBridge( 2, "Gluehwein Stall", RideVisitorKind.Shop, 1, 60, 30, GuestNeeds.Thirst ) { WinterTerms = new WinterShopTerms( 130, 130, 250, 150, 60, 20, 60 ), SpecialIngredient = 3, ExitCell = (6, 5) };
			var kiosk = new RideVisitorBridge( 3, "Kiosk", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = WinterRules.KioskAppearanceEffect, WinterTerms = new WinterShopTerms( 0, 0, 200, 100, 0, 0, 0 ), ExitCell = (6, 5) };
			var guest = rig.Simulation.SpawnInPark( 6, 5 );
			rig.Day = 7;
			((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( stall, guest.Id );
			((IRideVisitorHost)rig.Simulation).OnVisitorReleased( stall, guest.Id );
			return (rig, guest);
		}
		var (offRig, off) = Visit( false );
		Assert.AreEqual( 0L, off.WarmUntilDay, "with the rule off a glühwein changes nothing but the shop's own effects" );
		Assert.IsFalse( off.HoldsWinter );
		var (onRig, on) = Visit( true );
		Assert.AreEqual( WinterColdRules.WarmUntilDayOf( 7, Settings ), on.WarmUntilDay );
		Assert.IsTrue( on.HoldsWinter, "the park then uses the winter extension (a save records it)" );
		Assert.AreEqual( 0, on.WarmItems, "no item, only warmth" );
		Assert.AreEqual( off.Happiness, on.Happiness, 1e-4, "the drink's own effects are the same" );
		Assert.AreEqual( offRig.Simulation.Guests.Count(), onRig.Simulation.Guests.Count() );
	}

	[TestMethod]
	public void ASecondGluehweinExtendsNeverShortensTheWindow()
	{
		var rig = new Rig( true );
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.WarmUntilDay = 50;
		var stall = new RideVisitorBridge( 2, "Gluehwein Stall", RideVisitorKind.Shop, 1, 60, 30, GuestNeeds.Thirst ) { WinterTerms = new WinterShopTerms( 130, 130, 250, 150, 60, 20, 60 ), SpecialIngredient = 3, ExitCell = (6, 5) };
		rig.Day = 2;
		((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( stall, guest.Id );
		((IRideVisitorHost)rig.Simulation).OnVisitorReleased( stall, guest.Id );
		Assert.AreEqual( 50L, guest.WarmUntilDay );
	}

	// ---- The measurement (docs/WINTER.md, "Cold mood, measured") ---------------------------------------------------

	/// <summary>
	/// A headless kid over a snowy week (7 game days, 162 park turns of snow) with 0, 1, 2 and 3 protections, with the cold rule alone and with the rain mood on too: the happiness lost to the rules, the needs step's own
	/// change taken out. The numbers of docs/WINTER.md come from this test; it fails if they drift. Two skies: synthetic snow (the umbrellas' map and the winter signal both say snow) and real Halloween <see cref="ParkWeather"/> snow
	/// (snow with lightning: the map says storm, the winter signal says snow).
	/// </summary>
	[TestMethod]
	public void ASnowyWeekCostsAKidWhatTheDocumentationSays()
	{
		var turns = (long)Math.Round( 7 * ParkCalendar.SecondsPerDay / (double)ParkCalendar.SecondsPerTurn );
		var cases = new (string Name, int Items, bool Warmed)[]
		{
			("none", 0, false), ("head item", WinterRules.Hat, false), ("head item + mittens", WinterRules.Hat | WinterRules.Mittens, false), ("head item + mittens + glühwein", WinterRules.Hat | WinterRules.Mittens, true)
		};
		foreach ( var halloween in new[] { false, true } )
		{
			var table = new List<string>();
			var cold = new List<float>();
			var both = new List<float>();
			var rainOnly = new List<float>();
			foreach ( var (name, items, warmed) in cases )
			{
				float Week( bool coldOn, bool rainMoodOn )
				{
					var rig = new Rig( coldOn, rainMoodOn, seed: 3 );
					var guest = Aged( rig, 0, items );
					guest.TimeInParkSeconds = 0;
					guest.Happiness = 90;
					if ( warmed )
						guest.WarmUntilDay = 1000;
					if ( halloween )
						rig.HalloweenSnow();
					else
						rig.Weather = Snow;
					rig.RunTurns( turns );
					return guest.Happiness;
				}
				var plain = Week( false, false );
				cold.Add( Week( true, false ) - plain );
				both.Add( Week( true, true ) - plain );
				rainOnly.Add( Week( false, true ) - plain );
				table.Add( $"{(halloween ? "Halloween" : "synthetic")} {name}: cold {cold[^1]:0.#}, cold + rain mood {both[^1]:0.#}, rain mood alone {rainOnly[^1]:0.#}" );
			}
			Console.WriteLine( string.Join( "\n", table ) );
			// a snowy week holds two applications (days 0 and 4 of the visit): -4, -2, 0, 0 for the cold rule
			CollectionAssert.AreEqual( new[] { -4f, -2f, 0f, 0f }, cold.Select( value => MathF.Round( value, 2 ) ).ToArray(), string.Join( "; ", table ) );
			CollectionAssert.AreEqual( cold.Select( value => MathF.Round( value, 2 ) ).ToArray(), both.Select( value => MathF.Round( value, 2 ) ).ToArray(), "with the rain mood on too its happiness in snow is replaced (snow +1, Halloween's storm -1), so the rain mood adds nothing to happiness" );
			// the rain mood alone: synthetic snow cheers +1 per application, Halloween's snow is a storm and costs -1 per application, dressed or not
			var expected = halloween ? -2f : 2f;
			CollectionAssert.AreEqual( new[] { expected, expected, expected, expected }, rainOnly.Select( value => MathF.Round( value, 2 ) ).ToArray(), string.Join( "; ", table ) );
		}
	}

	// ---- Saves, resume, registration --------------------------------------------------------------------------------

	[TestMethod]
	public void TheColdDayMarkIsSavedOnlyOnceSetValidatedAndHashedOnlyWhenSet()
	{
		var rig = new Rig( true );
		var guest = Aged( rig, 0.1 );
		var emptyHash = rig.Simulation.ComputeStateHash();
		Assert.IsFalse( JsonSerializer.Serialize( rig.Simulation.CaptureSnapshot() ).Contains( "ColdMoodDay" ), "not written while 0 (older saves and parks without the rule keep their bytes)" );
		rig.Weather = Snow;
		rig.RunTurns( 16 );
		Assert.AreNotEqual( 0L, guest.ColdMoodDay );
		var snapshot = rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( JsonSerializer.Serialize( snapshot ).Contains( "ColdMoodDay" ) );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid );
		Assert.AreNotEqual( emptyHash, rig.Simulation.ComputeStateHash() );
		var bad = JsonSerializer.Deserialize<GuestSimulationSnapshot>( JsonSerializer.Serialize( snapshot ).Replace( "\"ColdMoodDay\":1", "\"ColdMoodDay\":-3" ) )!;
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( bad ).IsValid, "a negative day is refused" );
		var old = JsonSerializer.Deserialize<GuestSimulationSnapshot>( JsonSerializer.Serialize( snapshot ).Replace( "\"ColdMoodDay\":1,", "" ) )!;
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( old ).IsValid, "a save without the field loads" );
	}

	[TestMethod]
	public void AParkSavedMidSnowContinuesIdenticallyAfterAResume()
	{
		static void Sky( Rig rig ) => rig.Simulation.UmbrellaWeatherState = () => rig.Simulation.TickCount < 900 ? Snow : rig.Simulation.TickCount < 1500 ? UmbrellaRules.WeatherNone : Snow;
		static void Clock( Rig rig ) => rig.Simulation.WinterDay = () => ParkCalendar.DayIndex( rig.Simulation.TickCount );
		var park = new Rig( true, rainMood: true, seed: 11 );
		Sky( park );
		Clock( park );
		var items = new[] { 0, 1, 2, 4, 5, 6, 0, 2 };
		var guests = Enumerable.Range( 0, 8 ).Select( _ => park.Simulation.SpawnInPark( 4, 5 ) ).ToArray();
		for ( var index = 0; index < guests.Length; index++ )
		{
			park.Simulation.SetWarmItemsForTest( guests[index], items[index] );
			guests[index].TimeInParkSeconds = (96 + index) * TurnSeconds;
			if ( index % 3 == 0 )
				guests[index].WarmUntilDay = 40;
		}
		for ( var tick = 0; tick < 300; tick++ )
			park.Simulation.Tick( GuestSnapshotTests.Tick );
		Assert.IsTrue( park.Simulation.Guests.Any( guest => guest.ColdMoodDay != 0 ), "the rule ran before the snapshot" );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.IsTrue( park.Simulation.ValidateSnapshot( snapshot ).IsValid );
		var resumed = new Rig( true, rainMood: true, seed: 11 );
		Sky( resumed );
		Clock( resumed );
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

	[TestMethod]
	public void ARunWithTheRuleOffIsTheSameWhateverTheSnowAndDaySources()
	{
		string Run( bool sources )
		{
			var rig = new Rig( false, seed: 13 );
			if ( !sources )
			{
				rig.Simulation.WinterDay = null;
				rig.Simulation.UmbrellaWeatherState = null;
			}
			else
				rig.Weather = Snow;
			var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Simulation.SpawnInPark( 6, 5 ) ).ToArray();
			for ( var tick = 0; tick < 1500; tick++ )
				rig.Simulation.Tick( GuestSnapshotTests.Tick );
			return $"{rig.Simulation.ComputeStateHash():X16}|{guests.Sum( guest => guest.ColdMoodDay )}";
		}
		Assert.AreEqual( Run( false ), Run( true ), "the cold hooks read nothing while the rule is off" );
	}

	[TestMethod]
	public void TheColdExtensionIsRegisteredOffByDefaultAlwaysRecordedWhileOnAndOnlyInAThemeParkWorldRun()
	{
		var option = OfficialExtensions.WinterCold;
		Assert.AreEqual( "opentpw.winter-cold", option.Id );
		Assert.AreEqual( "1.0.0", option.Version.ToString() );
		Assert.IsTrue( !option.DefaultEnabled && option.AffectsSimulation && option.PlayerSelectable, "off by default, a simulation rule, selectable (decision of October 18, 2026)" );
		Assert.IsFalse( ParkResumeMods.UsedOnlyExtensionIds.Contains( option.Id ), "always recorded while on" );
		var all = OfficialExtensions.All.Select( extension => extension.Id ).ToList();
		Assert.AreEqual( all.IndexOf( OfficialExtensions.WinterWeather.Id ) + 1, all.IndexOf( option.Id ), "listed after the wintry Decembers" );
		Assert.IsTrue( all.IndexOf( option.Id ) < all.IndexOf( OfficialExtensions.RestoredAdvice.Id ) );
		using ( OpenTpwExtensions.Use( ModSet.Defaults( OfficialExtensions.All ) ) )
			Assert.IsFalse( FeatureGates.WinterCold );
		var on = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool> { [option.Id] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		using ( OpenTpwExtensions.Use( on ) )
		{
			Assert.IsTrue( FeatureGates.WinterCold );
			Assert.IsNotNull( Level.WinterColdFor( FeatureGates.WinterCold ) );
			var recorded = ParkResumeMods.Capture( on, ParkResumeMods.InUse( new ParkCellMap( 20, 10 ) ) );
			CollectionAssert.AreEqual( new[] { option.Id }, recorded!.Select( record => record.Id ).ToArray(), "recorded in a park that has no winter shop" );
		}
		Assert.IsNull( Level.WinterColdFor( false ), "the level hands the guests no rule while the gate is off" );
		var edition = GameEditions.Current;
		try
		{
			using var scope = OpenTpwExtensions.Use( on );
			GameEditions.Select( GameEdition.ThemeParkInc );
			Assert.IsFalse( FeatureGates.WinterCold, "a Theme Park Inc run has no such weather" );
			Assert.IsFalse( ParkResumeMods.RecordableMods( OpenTpwExtensions.Active ).Any( mod => mod.Id == option.Id ), "and the option leaves no trace in what it records" );
		}
		finally
		{
			GameEditions.Select( edition );
		}
	}

	[TestMethod]
	public void TheOptionsRowHasTextInEveryLanguageMainSupports()
	{
		foreach ( var key in new[] { OpenTpwText.ExtensionWinterColdName, OpenTpwText.ExtensionWinterColdDescription } )
			foreach ( var language in SupplementaryStrings.SupportedLanguages )
			{
				var text = SupplementaryStrings.Get( key, language );
				Assert.IsFalse( string.IsNullOrWhiteSpace( text ), $"{key} in {language}" );
				if ( language != "English" )
					Assert.AreNotEqual( SupplementaryStrings.Get( key, "English" ), text, $"{key} is translated in {language}" );
			}
	}
}
