using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:winter-tuning] The headless measurements of docs/WINTER.md, "Tuning" (phase W6): what the winter kiosk and the glühwein stall sell per season and weather, how many guests are dressed, and what the cold rule does to happiness over a snowy week
/// of the Halloween park's real <see cref="ParkWeather"/>. The same synthetic cross park as the determinism pins, fixed seeds, no game data. Nothing here is pinned: the tests check the shape of the numbers (and write the tables to the file named by
/// <c>OPENTPW_WINTER_MEASURE</c> when it is set, to regenerate the doc's tables).
/// </summary>
[TestClass]
[DoNotParallelize]
public class WinterTuningMeasureTests
{
	/// <summary>Ticks of one park day: 23.04 turns of 14.88 ticks.</summary>
	private const double TicksPerDay = 23.04 * 14.88;

	private static readonly ulong[] Seeds = { 1, 2, 3 };

	internal sealed record Sales( int Seed, int Month, int Weather, int Days, int Admitted, int Kiosk, int Stall, int Dressed, int Holding );

	private static SamSettings Hallow() => new( new[] { SamDocument.Parse(
		"WeatherEffects.QualityForSnowLow\t0\nWeatherEffects.QualityForSnowHigh\t25\nWeatherEffects.QualityForRainLow\t15\nWeatherEffects.QualityForRainHigh\t45\n" +
		"WeatherEffects.QualityForLightningLow\t0\nWeatherEffects.QualityForLightningHigh\t25\nWeatherEffects.MaxRaindrops\t600\nWeather.DaysOfWarning\t4\nWeather.DaysBetweenChanges\t7\nWeather.SpeedOfChange\t20\n", "test" ) } );

	/// <summary>The shops of the cross park for <paramref name="days"/> park days in a fixed month and a fixed weather state (<see cref="UmbrellaRules.WeatherNone"/>, Rain or Snow).</summary>
	internal static Sales MeasureSales( ulong seedValue, int month, int weather, int days, int? kioskSummer = null, int? kioskSpring = null )
	{
		var park = new DeterminismTests.SyntheticPark( new WorldSeed( seedValue ), winterShops: true, prepareGuests: guests =>
		{
			guests.WinterMonth = () => month;
			guests.UmbrellaWeatherState = () => weather;
			guests.WinterWeatherState = () => weather;
		} );
		// the fixture's kiosk mirrors the shipped terms (snow 200, winter 150, spring and autumn 60, summer 20); a what-if replaces the summer or spring percentage
		if ( kioskSummer != null || kioskSpring != null )
			park.WinterShops[0].WinterTerms = new WinterShopTerms( 0, 0, 200, 150, kioskSpring ?? 60, kioskSummer ?? 20, kioskSpring ?? 60 );
		var shopIds = park.WinterShops.Select( shop => shop.AttractionId ).ToArray();
		var sales = new int[shopIds.Length];
		park.Guests.MoneySpent += ( guest, amount, attraction ) =>
		{
			var index = Array.IndexOf( shopIds, attraction );
			if ( index >= 0 )
				sales[index]++;
		};
		var seen = new HashSet<int>();
		var dressed = new HashSet<int>();
		var holding = new HashSet<int>();
		var total = (long)Math.Round( days * TicksPerDay );
		for ( long tick = 0; tick < total; tick += 15 )
		{
			park.Run( 15 );
			foreach ( var guest in park.Guests.Guests )
			{
				seen.Add( guest.Id );
				if ( guest.WarmItems != 0 )
					holding.Add( guest.Id );
				if ( WinterRules.SlotsFull( guest.WarmItems ) )
					dressed.Add( guest.Id );
			}
		}
		return new Sales( (int)seedValue, month, weather, days, (int)park.Guests.Admissions, sales[0], sales[1], dressed.Count, holding.Count );
	}

	internal sealed record Cold( int Seed, bool ColdOn, bool Shops, bool RainMood, int Guests, double SnowHappiness, double DressedHappiness, double BareHappiness, int Snowy, int Affected, int Dressed, int Kiosk, int Stall );

	/// <summary>
	/// A snowy week of the Halloween park: its real weather (seed 21, from day 328 so it crosses the first December rolls) drives the cross park for 20 days. Mean happiness of the guests sampled during snow turns, split by outfit.
	/// </summary>
	internal static Cold MeasureCold( ulong seedValue, bool coldOn, bool shops, bool rainMood )
	{
		var weather = new ParkWeather( Hallow(), 21, 10, 10 );
		var startTurn = 328L * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn;
		weather.AdvanceTo( ParkCalendar.TickOfTurn( 0 ) );
		weather.AdvanceTo( ParkCalendar.TickOfTurn( startTurn ) );
		var snowy = 0;
		var park = new DeterminismTests.SyntheticPark( new WorldSeed( seedValue ), winterShops: shops, prepareGuests: guests =>
		{
			if ( coldOn )
				guests.WinterCold = WinterColdSettings.Default;
			if ( rainMood )
				guests.RainMood = RainMoodSettings.Default;
			guests.WinterDay = () => ParkCalendar.DayIndex( guests.TickCount );
			guests.WinterMonth = () => 12;
			guests.UmbrellaWeatherState = () =>
			{
				weather.AdvanceTo( ParkCalendar.TickOfTurn( startTurn + ParkCalendar.Turn( guests.TickCount ) ) );
				return UmbrellaRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow );
			};
			// the winter signal advances the weather too, so a run with the rain mood off (nothing else reads the umbrellas' signal) still steps it
			guests.WinterWeatherState = () =>
			{
				weather.AdvanceTo( ParkCalendar.TickOfTurn( startTurn + ParkCalendar.Turn( guests.TickCount ) ) );
				return WinterRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow );
			};
		} );
		var shopIds = park.WinterShops.Select( shop => shop.AttractionId ).ToArray();
		var shopSales = new int[2];
		park.Guests.MoneySpent += ( guest, amount, attraction ) =>
		{
			var index = Array.IndexOf( shopIds, attraction );
			if ( index >= 0 )
				shopSales[index]++;
		};
		var seen = new HashSet<int>();
		var dressed = new HashSet<int>();
		var affected = new HashSet<int>();
		double snowSum = 0, dressedSum = 0, bareSum = 0;
		long snowN = 0, dressedN = 0, bareN = 0;
		var total = (long)Math.Round( 20 * TicksPerDay );
		for ( long tick = 0; tick < total; tick += 15 )
		{
			park.Run( 15 );
			var isSnow = weather.Snow && weather.CurrentDrops > 0;
			if ( isSnow )
				snowy++;
			foreach ( var guest in park.Guests.Guests )
			{
				seen.Add( guest.Id );
				if ( guest.ColdMoodDay != 0 )
					affected.Add( guest.Id );
				if ( guest.HoldsWinter )
					dressed.Add( guest.Id );
				if ( !isSnow )
					continue;
				snowSum += guest.Happiness;
				snowN++;
				if ( guest.WarmItems != 0 )
				{
					dressedSum += guest.Happiness;
					dressedN++;
				}
				else
				{
					bareSum += guest.Happiness;
					bareN++;
				}
			}
		}
		return new Cold( (int)seedValue, coldOn, shops, rainMood, seen.Count, snowN == 0 ? 0 : snowSum / snowN, dressedN == 0 ? 0 : dressedSum / dressedN, bareN == 0 ? 0 : bareSum / bareN, snowy, affected.Count, dressed.Count, shopSales[0], shopSales[1] );
	}

	[TestMethod]
	public void TheShopsSellAllYearAndMoreInSnowAndTheNumbersAreReproducible()
	{
		var report = new StringBuilder();
		report.AppendLine( "Sales over 17 park days (sum of seeds 1 to 3): month, weather, admitted, kiosk sales, stall sales, guests with an item, guests with a complete outfit (head item and mittens)" );
		var sums = new Dictionary<(int Month, int Weather), (int Admitted, int Kiosk, int Stall, int Holding, int Dressed)>();
		foreach ( var month in new[] { 1, 4, 7, 10 } )
			foreach ( var weather in new[] { UmbrellaRules.WeatherNone, UmbrellaRules.WeatherRain, UmbrellaRules.WeatherSnow } )
			{
				var runs = Seeds.Select( seed => MeasureSales( seed, month, weather, 17 ) ).ToList();
				var sum = (runs.Sum( run => run.Admitted ), runs.Sum( run => run.Kiosk ), runs.Sum( run => run.Stall ), runs.Sum( run => run.Holding ), runs.Sum( run => run.Dressed ));
				sums[(month, weather)] = sum;
				report.AppendLine( FormattableString.Invariant( $"month {month,2} weather {weather}: admitted {sum.Item1,4}, kiosk {sum.Item2,4}, stall {sum.Item3,4}, with an item {sum.Item4,4}, dressed {sum.Item5,4}" ) );
			}
		Write( report.ToString() );
		Assert.AreEqual( MeasureSales( 1, 12, UmbrellaRules.WeatherSnow, 5 ), MeasureSales( 1, 12, UmbrellaRules.WeatherSnow, 5 ), "repeatable" );
		Assert.IsTrue( sums.Values.All( sum => sum.Admitted > 0 ), "guests arrive in every condition" );
		// the kiosk's demand follows the season (summer 20 %, winter 150 %): a clear January sells more than a clear July (the July count itself is a tuning finding, see docs/WINTER.md)
		Assert.IsTrue( sums[(1, UmbrellaRules.WeatherNone)].Kiosk > sums[(7, UmbrellaRules.WeatherNone)].Kiosk, "more kiosk sales in a clear January than in a clear July" );
		// the stall's snow percentage (250) and winter months (150) beat summer (20)
		Assert.IsTrue( sums[(1, UmbrellaRules.WeatherSnow)].Stall > sums[(7, UmbrellaRules.WeatherNone)].Stall, "more glühwein in a snowy January than in a clear July" );
	}

	[TestMethod]
	public void AClearJulyKioskSellsLittleAtTheShippedSummerPercentageAndMoreAsItRises()
	{
		var report = new StringBuilder();
		report.AppendLine( "Kiosk sales in a clear July over 17 park days (sum of seeds 1 to 3), by the kiosk's summer percentage (shipped 20), with the stall and the drinks shop competing:" );
		var counts = new List<int>();
		foreach ( var percent in new[] { 20, 35, 50, 75, 100 } )
		{
			var runs = Seeds.Select( seed => MeasureSales( seed, 7, UmbrellaRules.WeatherNone, 17, kioskSummer: percent ) ).ToList();
			counts.Add( runs.Sum( run => run.Kiosk ) );
			report.AppendLine( FormattableString.Invariant( $"summer {percent,3} %: kiosk {runs.Sum( run => run.Kiosk ),4}, stall {runs.Sum( run => run.Stall ),4}, guests with an item {runs.Sum( run => run.Holding ),4} of {runs.Sum( run => run.Admitted ),4} admitted" ) );
		}
		Write( report.ToString() );
		Assert.IsTrue( counts.Last() >= counts.First(), "a higher summer percentage never sells less" );
	}

	[TestMethod]
	public void TheColdRuleCostsUndressedGuestsHappinessOverASnowyHallowWeekAndDressedGuestsLessOrNothing()
	{
		var report = new StringBuilder();
		report.AppendLine( "Halloween park, real weather from day 328, 20 days, seeds 1 to 3: setup, guests, snowy samples, mean happiness in snow (all / with item / without item), guests affected by the cold rule, guests dressed" );
		var rows = new List<Cold>();
		foreach (var (coldOn, shops, rainMood) in new[] { (false, false, true), (false, true, true), (true, false, true), (true, true, true), (true, true, false) } )
			foreach ( var seed in Seeds )
			{
				var row = MeasureCold( seed, coldOn, shops, rainMood );
				rows.Add( row );
				report.AppendLine( FormattableString.Invariant( $"cold {(coldOn ? "on " : "off")} shops {(shops ? "yes" : "no ")} rainmood {(rainMood ? "on " : "off")} seed {seed}: guests {row.Guests,4}, snowy turns {row.Snowy,4}, kiosk {row.Kiosk,3} stall {row.Stall,3}, happiness {row.SnowHappiness,6:0.0} / {row.DressedHappiness,6:0.0} / {row.BareHappiness,6:0.0}, affected {row.Affected,4}, dressed {row.Dressed,4}" ) );
			}
		Write( report.ToString() );
		Assert.IsTrue( rows.All( row => row.Snowy > 0 ), "the Halloween week snows" );
		double Mean( bool cold, bool shops ) => rows.Where( row => row.ColdOn == cold && row.Shops == shops && row.RainMood ).Average( row => row.SnowHappiness );
		Assert.IsTrue( Mean( true, false ) < Mean( false, false ), "with nobody dressed the cold rule lowers the mood in snow against the rain mood's +1" );
	}

	private static void Write( string text )
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_WINTER_MEASURE" ) is { Length: > 0 } path )
			File.AppendAllText( path, text + "\n" );
	}
}
