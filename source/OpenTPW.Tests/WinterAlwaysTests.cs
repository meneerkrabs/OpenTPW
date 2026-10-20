using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// Always winter (docs/WINTER.md, "Always winter"; <c>opentpw.winter-always</c>, off by default): every weather roll of the year uses December's season row and its rain bands are snow (the wintry Decembers' conversion, all year), and the
/// winter shops sell at their winter demand all year. Headless weather over the shipped bands and a headless guest simulation, no original game data.
/// </summary>
// [EXT:winter-always] OpenTPW's own option, user-approved 2026-10-19 ("Winterweer hele jaar"); tp.exe's roll (0x512e10) takes its season row from the calendar month (0x4f8770)
[TestClass]
public class WinterAlwaysTests
{
	private static IOpenTpwExtension Option => OfficialExtensions.WinterAlways;

	private static ModSet With( params (IOpenTpwExtension Extension, bool Enabled)[] choices ) =>
		ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = choices.ToDictionary( choice => choice.Extension.Id, choice => choice.Enabled ) }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	/// <summary>The first turn of park day <paramref name="day"/> (0-based from 2000-01-01).</summary>
	private static long TurnOfDay( int day ) => (long)day * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn;

	/// <summary>Day 160 is 9 June 2000; day 340 is 6 December.</summary>
	private const int JuneDay = 160, DecemberDay = 340;

	// ---- The registration ------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheOptionIsOffByDefaultAlwaysRecordedWhileOnAndOnlyInAThemeParkWorldRun()
	{
		Assert.AreEqual( "opentpw.winter-always", Option.Id );
		Assert.AreEqual( "1.0.0", Option.Version.ToString() );
		Assert.IsTrue( !Option.DefaultEnabled && Option.AffectsSimulation && Option.PlayerSelectable, "off by default, a simulation rule, selectable (the user's decision of 2026-10-19)" );
		Assert.IsFalse( ParkResumeMods.UsedOnlyExtensionIds.Contains( Option.Id ), "it changes every park's weather and shops, so a save always records it while it is on" );
		var all = OfficialExtensions.All.Select( extension => extension.Id ).ToList();
		Assert.AreEqual( all.IndexOf( OfficialExtensions.WinterCold.Id ) + 1, all.IndexOf( Option.Id ), "listed with the other winter rows, after the cold mood" );
		Assert.IsTrue( all.IndexOf( Option.Id ) < all.IndexOf( OfficialExtensions.RestoredAdvice.Id ) );
		using ( OpenTpwExtensions.Use( ModSet.Defaults( OfficialExtensions.All ) ) )
			Assert.IsFalse( FeatureGates.WinterAlways, "off by default" );
		var on = With( (Option, true) );
		using ( OpenTpwExtensions.Use( on ) )
		{
			Assert.IsTrue( FeatureGates.WinterAlways );
			Assert.IsFalse( FeatureGates.WinterWeather, "the option implies the wintry Decembers in the weather; it does not switch their own row on" );
			var recorded = ParkResumeMods.Capture( on, ParkResumeMods.InUse( new ParkCellMap( 20, 10 ) ) );
			CollectionAssert.AreEqual( new[] { Option.Id }, recorded!.Select( record => record.Id ).ToArray(), "recorded in a park that has no winter shop at all" );
		}
		var edition = GameEditions.Current;
		try
		{
			using var scope = OpenTpwExtensions.Use( on );
			GameEditions.Select( GameEdition.ThemeParkInc );
			Assert.IsFalse( FeatureGates.WinterAlways, "a Theme Park Inc run has no such weather" );
			Assert.IsFalse( ParkResumeMods.RecordableMods( OpenTpwExtensions.Active ).Any( mod => mod.Id == Option.Id ), "and the option leaves no trace in what it records" );
		}
		finally
		{
			GameEditions.Select( edition );
		}
	}

	[TestMethod]
	public void ASaveMadeWithTheOptionNeedsItAndASaveMadeWithoutItRefusesItOldSavesLoadUnchanged()
	{
		var off = ModSet.Defaults( OfficialExtensions.All );
		var on = With( (Option, true) );
		var savedOn = ParkResumeMods.Capture( on, ParkResumeMods.InUse( new ParkCellMap( 20, 10 ) ) );
		var savedOff = ParkResumeMods.Capture( off, ParkResumeMods.InUse( new ParkCellMap( 20, 10 ) ) );
		Assert.IsNull( savedOff, "with the option off a park records nothing for it, so an older save and a new one are the same bytes" );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( savedOn, on ).Count, "a save made with the option loads with it on" );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( savedOff, off ).Count, "an old save loads with the option off, as before" );
		var needs = ParkResumeMods.Unmet( savedOn, off );
		Assert.IsTrue( needs.Any( reason => reason.Contains( Option.Id ) ), "a save made with the option is refused with it off: " + string.Join( "; ", needs ) );
		var without = ParkResumeMods.Unmet( savedOff, on );
		Assert.IsTrue( without.Any( reason => reason.Contains( Option.Id ) ), "a save made without the option is refused with it on: " + string.Join( "; ", without ) );
		// both winter options on: both recorded
		var both = With( (OfficialExtensions.WinterWeather, true), (Option, true) );
		var savedBoth = ParkResumeMods.Capture( both, ParkResumeMods.InUse( new ParkCellMap( 20, 10 ) ) );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( savedBoth, both ).Count );
		CollectionAssert.AreEquivalent( new[] { OfficialExtensions.WinterWeather.Id, Option.Id }, savedBoth!.Select( record => record.Id ).ToArray() );
		Assert.IsTrue( ParkResumeMods.Unmet( savedBoth, on ).Count > 0, "and a save with both needs both" );
	}

	[TestMethod]
	public void TheOptionsRowHasTextInEveryLanguageMainSupports()
	{
		foreach ( var key in new[] { OpenTpwText.ExtensionWinterAlwaysName, OpenTpwText.ExtensionWinterAlwaysDescription } )
			foreach ( var language in SupplementaryStrings.SupportedLanguages )
			{
				var text = SupplementaryStrings.Get( key, language );
				Assert.IsFalse( string.IsNullOrWhiteSpace( text ), $"{key} in {language}" );
				if ( language != "English" )
					Assert.AreNotEqual( SupplementaryStrings.Get( key, "English" ), text, $"{key} is translated in {language}" );
			}
		Assert.AreEqual( "Always winter", SupplementaryStrings.Get( OpenTpwText.ExtensionWinterAlwaysName, "English" ) );
	}

	// ---- The roll and the converter --------------------------------------------------------------------------------

	[TestMethod]
	public void EveryMonthRollsDecembersRowInAParkThatCanSnowAndSpaceKeepsItsSeasons()
	{
		foreach ( var (name, settings, holds) in new (string, SamSettings, bool)[] { ("jungle", ParkWeatherSnapshotTests.Jungle(), true), ("hallow", WinterWeatherTests.Hallow(), true), ("space", WinterWeatherTests.Space(), false) } )
			for ( var month = 0; month < 12; month++ )
				for ( ulong seed = 1; seed <= 40; seed++ )
				{
					var day = month * 30 + 10;
					var always = new ParkWeather( settings, seed, 10, 10, alwaysWinter: true );
					var december = new ParkWeather( settings, seed, 10, 10 );
					var plain = new ParkWeather( settings, seed, 10, 10 );
					var quality = always.Roll( ParkCalendar.TickOfTurn( TurnOfDay( day ) ) );
					var expected = holds ? december.Roll( ParkCalendar.TickOfTurn( TurnOfDay( DecemberDay ) ) ) : plain.Roll( ParkCalendar.TickOfTurn( TurnOfDay( day ) ) );
					Assert.AreEqual( expected, quality, $"{name}, day {day}, seed {seed}: {(holds ? "December's row" : "its own season")}" );
				}
	}

	[TestMethod]
	public void ARainBandRollOfAnyMonthIsSnowWithoutLightningAndTheTwoOptionsDoNotConvertTwice()
	{
		var storm = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 3, 10, 10 );
		storm.ApplyQuality( 5 );
		foreach ( var (wintryDecembers, label) in new[] { (false, "always winter alone"), (true, "both options") } )
		{
			var always = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 3, 10, 10, wintryDecembers, alwaysWinter: true );
			always.ApplyQuality( 5, wintryDecember: true );
			Assert.IsTrue( always.Snow && !always.Rain && !always.Lightning, $"{label}: snow without lightning" );
			Assert.AreEqual( storm.TargetDrops, always.TargetDrops, $"{label}: the storm's drops, as in a wintry December" );
			always.ApplyQuality( 78, wintryDecember: true );
			Assert.IsFalse( always.Snow || always.Rain || always.Lightning, $"{label}: a dry quality stays dry" );
		}
	}

	[TestMethod]
	public void TheJungleSnowsAllYearAndNeverRains()
	{
		var off = WinterWeatherTests.Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: false, WinterWeatherTests.RunDays );
		var on = WinterWeatherTests.Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: false, WinterWeatherTests.RunDays, alwaysWinter: true );
		Assert.AreEqual( 0, off.SnowDays.Count );
		Assert.AreEqual( 0, on.RainDays.Count, "every rain band is snow" );
		Assert.IsTrue( on.SnowDays.Count > WinterWeatherTests.RunDays / 2, $"{on.SnowDays.Count} snowy days of {WinterWeatherTests.RunDays}" );
		Assert.IsTrue( on.SnowDays.Count( day => day is >= 152 and < 244 ) > 45, "June to August snow too" );
		var both = WinterWeatherTests.Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: true, WinterWeatherTests.RunDays, alwaysWinter: true );
		Assert.AreEqual( on.Hash, both.Hash, "with the wintry Decembers on as well nothing changes: no double effect" );
	}

	[TestMethod]
	public void HalloweenSnowsAllYearAndSpaceRunsTheSameWeatherOnOrOff()
	{
		var hallow = WinterWeatherTests.Run( WinterWeatherTests.Hallow(), 22, wintry: false, WinterWeatherTests.RunDays, alwaysWinter: true );
		Assert.IsTrue( hallow.SnowDays.Count( day => day is >= 152 and < 244 ) > 45, $"Halloween snows in the summer: {hallow.SnowDays.Count} snowy days" );
		Assert.AreEqual( hallow.Hash, WinterWeatherTests.Run( WinterWeatherTests.Hallow(), 22, wintry: true, WinterWeatherTests.RunDays, alwaysWinter: true ).Hash, "no double effect" );
		var space = WinterWeatherTests.Run( WinterWeatherTests.Space(), 22, wintry: false, WinterWeatherTests.RunDays );
		Assert.AreEqual( space.Hash, WinterWeatherTests.Run( WinterWeatherTests.Space(), 22, wintry: false, WinterWeatherTests.RunDays, alwaysWinter: true ).Hash, "space has no rain or snow band: its weather is left alone" );
	}

	/// <summary>[EXT:winter-always] The pinned hash of the jungle's first 400 days under seed 21 with the Always winter option on (docs/DETERMINISM.md, "Always winter").</summary>
	internal const ulong JungleSeed21AlwaysWinterHash = 0x71A3DF8635ACF9B9UL;

	[TestMethod]
	public void TheJungleWeatherOfAFixedSeedIsPinnedWithTheOptionOn()
	{
		var on = WinterWeatherTests.Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: false, WinterWeatherTests.RunDays, alwaysWinter: true );
		Assert.AreEqual( WinterWeatherTests.JungleSeed21OffHash, WinterWeatherTests.Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: false, WinterWeatherTests.RunDays, alwaysWinter: false ).Hash, "off: the original's weather pin" );
		Assert.AreEqual( WinterWeatherTests.JungleSeed21WintryHash, WinterWeatherTests.Run( ParkWeatherSnapshotTests.Jungle(), 21, wintry: true, WinterWeatherTests.RunDays, alwaysWinter: false ).Hash, "off: the wintry Decembers pin" );
		Assert.AreEqual( JungleSeed21AlwaysWinterHash, on.Hash, $"on: 0x{on.Hash:X16} ({on.SnowDays.Count} snowy days, first on day {on.SnowDays.Min})" );
	}

	// ---- The forecast ----------------------------------------------------------------------------------------------

	[TestMethod]
	public void AJuneForecastSaysSnowAndTheApplyAgrees()
	{
		var snow = new WeatherQualityBands( true, false, false );
		var thunderstorms = new WeatherQualityBands( false, true, true );
		ParkWeather At( bool alwaysWinter, int quality )
		{
			var weather = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 21, 10, 10, alwaysWinter: alwaysWinter );
			weather.AdvanceTo( ParkCalendar.TickOfTurn( TurnOfDay( JuneDay ) ) );
			weather.DebugPostForecast( quality );
			return weather;
		}
		Assert.AreEqual( snow, At( true, 5 ).ForecastBands, "June, option on: snow, not thunderstorms" );
		Assert.AreEqual( snow, At( true, 30 ).ForecastBands, "plain rain becomes snow as well" );
		Assert.AreEqual( default( WeatherQualityBands ), At( true, 78 ).ForecastBands, "a dry quality stays dry" );
		Assert.AreEqual( thunderstorms, At( false, 5 ).ForecastBands, "option off: the original's thunderstorms" );
		var applied = At( true, 5 );
		var announced = applied.ForecastBands;
		for ( var turn = TurnOfDay( JuneDay ) + 1; applied.Forecast != null; turn++ )
			applied.AdvanceTo( ParkCalendar.TickOfTurn( turn ) );
		Assert.AreEqual( announced, new WeatherQualityBands( applied.Snow, applied.Rain, applied.Lightning ), "the snow it announced is the snow that fell" );
	}

	[TestMethod]
	public void AHalloweenWinterSaysSnowNotThunderstorms()
	{
		var snow = new WeatherQualityBands( true, false, false );
		var snowWithLightning = new WeatherQualityBands( true, false, true );
		ParkWeather At( bool alwaysWinter, int quality )
		{
			var weather = new ParkWeather( WinterWeatherTests.Hallow(), 21, 10, 10, alwaysWinter: alwaysWinter );
			weather.AdvanceTo( ParkCalendar.TickOfTurn( TurnOfDay( JuneDay ) ) );
			weather.DebugPostForecast( quality );
			return weather;
		}
		Assert.AreEqual( snowWithLightning, At( false, 5 ).ForecastBands, "option off: Halloween's own bands, read as thunderstorms" );
		Assert.AreEqual( OpenTpwText.WeatherThunderstorms, WeatherForecastState.KindKey( At( false, 5 ).ForecastBands ) );
		var applied = At( true, 5 );
		Assert.AreEqual( snow, applied.ForecastBands, "option on: the forecast says snow" );
		Assert.AreEqual( OpenTpwText.WeatherSnow, WeatherForecastState.KindKey( applied.ForecastBands ) );
		for ( var turn = TurnOfDay( JuneDay ) + 1; applied.Forecast != null; turn++ )
			applied.AdvanceTo( ParkCalendar.TickOfTurn( turn ) );
		Assert.IsTrue( applied.Snow && applied.Lightning, "the applied weather keeps its flags: snow with lightning" );
		Assert.AreEqual( snow, applied.CurrentBands, "the current weather reads as snow as well" );
	}

	[TestMethod]
	public void SpaceKeepsItsMonthByMonthWinterShopDemand()
	{
		Assert.IsTrue( ParkWeather.CanSnowFromTheWinterRow( ParkWeatherSnapshotTests.Jungle() ), "jungle" );
		Assert.IsTrue( ParkWeather.CanSnowFromTheWinterRow( WinterWeatherTests.Hallow() ), "Halloween" );
		Assert.IsFalse( ParkWeather.CanSnowFromTheWinterRow( WinterWeatherTests.Space() ), "space: the shops keep their month's demand" );
	}

	// ---- Resume ----------------------------------------------------------------------------------------------------

	[TestMethod]
	public void AnAlwaysWinterWeatherSavedMidSnowInJulyContinuesIdenticallyAfterAResume()
	{
		var reference = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 21, 10, 10, alwaysWinter: true );
		var start = TurnOfDay( 183 );
		long saveTurn = -1;
		for ( var turn = 0L; turn <= start + TurnOfDay( 60 ) && saveTurn < 0; turn++ )
		{
			reference.AdvanceTo( ParkCalendar.TickOfTurn( turn ) );
			if ( turn >= start && reference.Snow && reference.CurrentDrops > 100 )
				saveTurn = turn + 10;
		}
		Assert.IsTrue( saveTurn > 0, "it snows in July" );
		reference.AdvanceTo( ParkCalendar.TickOfTurn( saveTurn ) );
		Assert.IsTrue( reference.Snow, "saved while it snows" );
		var resumed = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 21, 10, 10, alwaysWinter: true );
		resumed.RestoreSnapshot( reference.CaptureSnapshot() );
		for ( var turn = saveTurn + 1; turn <= saveTurn + TurnOfDay( 60 ); turn++ )
		{
			var tick = ParkCalendar.TickOfTurn( turn );
			reference.AdvanceTo( tick );
			resumed.AdvanceTo( tick );
			Assert.AreEqual( (reference.Quality, reference.Snow, reference.Rain, reference.Lightning, reference.CurrentDrops), (resumed.Quality, resumed.Snow, resumed.Rain, resumed.Lightning, resumed.CurrentDrops), $"turn {turn}" );
		}
	}

	// ---- The shops -------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheShopsSellAtTheirWinterDemandInEveryMonth()
	{
		Assert.AreEqual( 6, WinterRules.DemandMonthOf( 6, alwaysWinter: false ) );
		Assert.AreEqual( WinterSeason.Winter, WinterRules.SeasonOf( WinterRules.DemandMonthOf( 6, alwaysWinter: true ) ) );
		var kioskTerms = new WinterShopTerms( 0, 0, 200, 150, 60, 20, 60 );
		var stallTerms = new WinterShopTerms( 130, 130, 250, 150, 60, 20, 60 );
		foreach ( var (shop, label) in new[]
		{
			(new RideVisitorBridge( 1, "Winter Kiosk", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = WinterRules.KioskAppearanceEffect, WinterTerms = kioskTerms, EntranceCell = (10, 9), ExitCell = (10, 9) }, "kiosk"),
			(new RideVisitorBridge( 2, "Gluehwein Stall", RideVisitorKind.Shop, 1, 60, 30, GuestNeeds.Thirst ) { WinterTerms = stallTerms, SpecialIngredient = 3, EntranceCell = (10, 9), ExitCell = (10, 9) }, "stall")
		} )
		{
			var settings = GuestTests.CreateSettings();
			settings.ExitLevelSeconds = 10_000;
			settings.Types = new() { new GuestType( 60, 300, 1000 ) };
			var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 5 ) { ArrivalsEnabled = false };
			var weather = UmbrellaRules.WeatherNone;
			var month = 12;
			simulation.UmbrellaWeatherState = () => weather;
			simulation.WinterMonth = () => month;
			simulation.Register( shop );
			var guest = simulation.SpawnInPark( shop.JoinCell!.Value.X, shop.JoinCell!.Value.Y );
			foreach ( var state in new[] { UmbrellaRules.WeatherNone, UmbrellaRules.WeatherRain, UmbrellaRules.WeatherSnow } )
			{
				weather = state;
				month = 12;
				simulation.WinterAlways = false;
				var winter = simulation.ScoreAttraction( guest, shop );
				for ( month = 1; month <= 12; month++ )
				{
					simulation.WinterAlways = true;
					Assert.AreEqual( winter, simulation.ScoreAttraction( guest, shop ), $"{label}, weather {state}, month {month}: December's score" );
				}
				month = 7;
				simulation.WinterAlways = false;
				Assert.AreNotEqual( winter, simulation.ScoreAttraction( guest, shop ), $"{label}, weather {state}: off, July sells at the summer demand" );
			}
		}
	}
}
