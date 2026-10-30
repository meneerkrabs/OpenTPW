using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Theme Park Inc's weather spells (Game.exe 0x641F60, 0x642770, 0x642A80) over synthetic settings with the shipped science and global numbers.</summary>
// [BIN:TPI-EXE:0x642770 spell roll] the machine under test follows the executable; the roll's numbers come from the economy's world stream ([EXT:tpi-weather-spell-seed])
[TestClass]
public class ThemeParkIncWeatherTests
{
	private static readonly ThemeParkIncWeatherSettings Science = new(
		new[] { 0, 0, 0, 25, 0, 30, 0, 0, 20, 0, 0, 0, 0, 0, 15, 0 }, new[] { 4, 0, 0 }, 3, (40, 95), (95, 100), (0, 20), (0, 40), (0, 15) );

	/// <summary>Runs the months from January of the calendar's first year, a draw of <paramref name="roll"/> for the kind and 0 for each quality.</summary>
	private static List<(int Month, int Year, int State)> Run( ThemeParkIncWeatherSettings settings, int months, Func<int, int>? roll = null )
	{
		var weather = new ThemeParkIncWeatherSpells( settings );
		var log = new List<(int, int, int)>();
		for ( var index = 0; index < months; index++ )
		{
			weather.OnMonth( index % 12 + 1, index / 12, bound => bound == 100 ? (roll?.Invoke( index ) ?? 99) : 0 );
			log.Add( (index % 12 + 1, index / 12, weather.State) );
		}
		return log;
	}

	[TestMethod]
	public void TheFirstMonthRollsNormalWeatherForTheRestOfTheQuarterAndTheNextMonthStartsIt()
	{
		var weather = new ThemeParkIncWeatherSpells( Science );
		weather.OnMonth( 1, 0, bound => 0 );
		Assert.IsTrue( weather.NextPending );
		Assert.AreEqual( new WeatherSpell( 2, 0, 40, 2 ), weather.Next, "January: q 2, position 2: normal weather for 2 months from February, quality from the normal range" );
		Assert.AreEqual( 0, weather.State );
		weather.OnMonth( 2, 0, bound => 0 );
		Assert.IsFalse( weather.NextPending );
		Assert.AreEqual( weather.Active, new WeatherSpell( 2, 0, 40, 2 ) );
	}

	[TestMethod]
	public void FreakWeatherIsRolledOnlyInTheThirdMonthsOfAYearFromTheYearsSettingOn()
	{
		// the months that roll are the ones the previous spell ends in: February, May, August and November in the steady cycle
		var rolled = new List<(int Month, int Year)>();
		var weather = new ThemeParkIncWeatherSpells( Science );
		for ( var index = 0; index < 60; index++ )
		{
			var before = weather.Next;
			var pending = weather.NextPending;
			weather.OnMonth( index % 12 + 1, index / 12, bound => bound == 100 ? 0 : 0 );
			if ( weather.NextPending && (!pending || weather.Next != before) )
				rolled.Add( (index % 12 + 1, index / 12) );
		}
		CollectionAssert.AreEqual( new[] { 2, 5, 8, 11 }, rolled.Where( item => item.Year >= 2 ).Select( item => item.Month ).Distinct().ToArray(), "after the first years only the third months roll" );
		var early = Run( Science, 36, index => 0 );
		Assert.IsTrue( early.Where( item => item.Year < 3 ).All( item => item.State == 0 ), "before the third year the rolls are normal weather" );
		Assert.AreEqual( "002221113331112221113331", string.Concat( Run( Science, 60, index => 0 ).Where( item => item.Year >= 3 ).Select( item => item.State ) ), "from year 3 on, a fixed draw of 0 gives these states per month (the spells follow the quarter rows)" );
	}

	[TestMethod]
	public void TheRowFollowsTheMonthAndNovemberReadsTheWordsAfterTheTable()
	{
		// February (q 3) row 1: rain 30 %; May row 2: heatwave 20 %; August row 3: storm 15 %; November row 4: the count 4 as a heatwave chance, two zeros, then the years setting 3 as a snow chance
		Assert.AreEqual( 2, RolledKind( 2, 0 ), "row 1: 0 < 30 is rain" );
		Assert.AreEqual( 1, RolledKind( 5, 0 ), "row 2: 0 < 20 is a heatwave" );
		Assert.AreEqual( 3, RolledKind( 8, 0 ), "row 3: 0 < 15 is a storm" );
		Assert.AreEqual( 1, RolledKind( 11, 0 ), "row 4: 0 < 4 is a heatwave" );
		Assert.AreEqual( 4, RolledKind( 11, 4 ), "row 4: 4 is not below the heatwave's 4, then 0 < 3 is snow" );
		Assert.AreEqual( 0, RolledKind( 11, 7 ), "row 4: 7 - 4 = 3 is not below the snow chance 3" );
		Assert.AreEqual( 0, RolledKind( 2, 30 ), "row 1: 30 is not below 30 (rain), nor the other chances: normal weather" );
	}

	private static int RolledKind( int month, int roll )
	{
		var weather = new ThemeParkIncWeatherSpells( Science );
		// the running spell's last month has passed: the first message rolls
		weather.OnMonth( month, 5, bound => bound == 100 ? roll : 0 );
		return weather.Next.Kind;
	}

	[TestMethod]
	public void AFreakSpellLastsThreeMonthsAndTheQualityComesFromItsRange()
	{
		var weather = new ThemeParkIncWeatherSpells( Science );
		weather.OnMonth( 2, 5, bound => bound == 100 ? 0 : 7 );
		Assert.AreEqual( new WeatherSpell( 3, 2, 7, 3 ), weather.Next, "rain from March for 3 months, quality 0 + a draw below 40 - 0" );
		weather.OnMonth( 3, 5, bound => 0 );
		Assert.AreEqual( 2, weather.State );
		weather.OnMonth( 4, 5, bound => 0 );
		weather.OnMonth( 5, 5, bound => 0 );
		Assert.AreEqual( 2, weather.State, "the spell's last month is 5: the roll that ends it comes then" );
		Assert.IsTrue( weather.NextPending, "and the next spell is waiting for June" );
		weather.OnMonth( 6, 5, bound => 0 );
		Assert.AreNotEqual( 2, weather.State, "June starts the next spell" );
	}

	[TestMethod]
	public void TheEconomyStepsTheWeatherOnEachMonthAndTheArrivalGroupReadsItsState()
	{
		const string Standard = "BankAccountInfo.InitialCash\t50000\nBankAccountInfo.InitialAdmissionFee\t25\n"
			+ "LoanInfo[0].LoanAmount\t10000\nLoanInfo[0].APRInPercent\t20\nLoanInfo[0].RepaymentPeriodInMonths\t36\nLoanInfo[0].Lendername\t0\n"
			+ "Costs.QueueCell 25\nCosts.PathCell 10\nCosts.KartTrackCell 40\nCosts.WaterTrackCell 50\nCosts.MapCell 75\n"
			+ "PeepInfo.ExcitementToCostDivisor 6 s\nPeepInfo.MinimumEntryFee 20\nPeepInfo.CheapPriceMultiplier 0.75\nPeepInfo.AveragePriceMultiplier 1.25\nPeepInfo.ExpensivePriceMultiplier 2.0\n"
			+ "Seasons[0].PercentageChanceForHeatwave 100\nSeasons[0].PercentageChanceForRain 0\nSeasons[0].PercentageChanceForStorm 0\nSeasons[0].PercentageChanceForSnow 0\n"
			+ "Seasons[1].PercentageChanceForHeatwave 100\nSeasons[1].PercentageChanceForRain 0\nSeasons[1].PercentageChanceForStorm 0\nSeasons[1].PercentageChanceForSnow 0\n"
			+ "Seasons[2].PercentageChanceForHeatwave 100\nSeasons[2].PercentageChanceForRain 0\nSeasons[2].PercentageChanceForStorm 0\nSeasons[2].PercentageChanceForSnow 0\n"
			+ "Seasons[3].PercentageChanceForHeatwave 100\nSeasons[3].PercentageChanceForRain 0\nSeasons[3].PercentageChanceForStorm 0\nSeasons[3].PercentageChanceForSnow 0\n"
			+ "Weather.YearsBeforeFirstFreakWeather 0\nWeatherEffects.QualityForNormalLow 40\nWeatherEffects.QualityForNormalHigh 95\nWeatherEffects.QualityForHeatWaveLow 95\nWeatherEffects.QualityForHeatWaveHigh 100\n";
		var settings = BalanceSettings.ForThemeParkInc( new SamSettings( new[] { SamDocument.Parse( Standard, "Standard.sam" ) } ), "science" );
		Assert.IsNotNull( settings.Weather );
		var economy = new ParkEconomy( settings, new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7, EconomyRules.ThemeParkIncMoney );
		Assert.IsNotNull( economy.TpiWeather );
		Assert.AreEqual( 0, economy.TpiWeatherState );
		var seen = new HashSet<int>();
		for ( var month = 0; month < 30; month++ )
		{
			economy.AdvanceMonths( 1 );
			seen.Add( economy.TpiWeatherState );
		}
		CollectionAssert.IsSubsetOf( seen.ToArray(), new[] { 0, 1 }, "every freak roll is a heatwave with these chances" );
		Assert.IsTrue( seen.Contains( 1 ), "and a spell of them runs" );
		var tpw = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7 );
		Assert.IsNull( tpw.TpiWeather, "Theme Park World has no such weather" );
	}
}
