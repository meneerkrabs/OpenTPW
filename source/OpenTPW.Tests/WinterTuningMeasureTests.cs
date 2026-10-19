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

	internal sealed record Sales( int Seed, int Month, int Weather, int Days, int Admitted, int Kiosk, int Stall, int Dressed, int Holding, int KioskRefused = 0, int StallRefused = 0 );

	/// <summary>The words the price test reads of a winter shop (<see cref="PriceTerms"/>): its price, cost of goods and effects, and the per-type word <c>UsageInfo.RipOffOK</c>.</summary>
	internal sealed record ShopPrice( int Price, int InitCostOfGoods, int ThirstEffect, int HungerEffect, int VomitEffect, int HappinessEffect, int RipOffOk );

	/// <summary>
	/// <c>UsageInfo.RipOffOK</c> of every theme's <c>shops/Shops.sam</c>, the category default each shop inherits, the winter shops included (their own .sam does not set it; <see cref="ObjectCatalog"/> layers it under them, and
	/// <c>WinterPriceMeasureTests</c> checks the merged entry against the game's data). Without it the willingness to pay is about half, and the kiosk's price 25 would be above it for every guest.
	/// </summary>
	internal const int ShopsSamRipOffOk = 100;

	/// <summary>The shipped price terms of a winter shop (<c>kiosk</c> or <c>gluehwein</c>): the extension's own .sam under <c>content/winter</c>, with <see cref="ShopsSamRipOffOk"/> where the .sam leaves the key to the category.</summary>
	internal static ShopPrice ShippedPrice( string shop )
	{
		var path = Path.Combine( WinterContentTests.Folder, "objects", "jungle", shop, shop == "kiosk" ? "Kiosk.sam" : "Gluehwein.sam" );
		var s = new ObjectSettings( new[] { new ObjectSettingsFile( File.ReadAllText( path ), path ) } );
		return new ShopPrice( s.GetInt( "UsageInfo.InitPricePerUse" ), s.GetInt( "UsageInfo.InitCostOfGoods" ), s.GetInt( "UsageInfo.ThirstEffect" ), s.GetInt( "UsageInfo.HungerEffect" ), s.GetInt( "UsageInfo.VomitEffect" ),
			s.GetInt( "UsageInfo.HappinessEffect" ), s.Has( "UsageInfo.RipOffOK" ) ? s.GetInt( "UsageInfo.RipOffOK" ) : ShopsSamRipOffOk );
	}

	/// <summary>The kiosk's and the stall's shipped price terms, in the fixture's order.</summary>
	internal static IReadOnlyList<ShopPrice> ShippedPrices() => new[] { ShippedPrice( "kiosk" ), ShippedPrice( "gluehwein" ) };

	/// <summary>
	/// The shops of the cross park for <paramref name="days"/> park days in a fixed month and a fixed weather state (<see cref="UmbrellaRules.WeatherNone"/>, Rain or Snow). The winter shops carry their shipped price terms
	/// (<see cref="ShippedPrices"/>, or <paramref name="prices"/> for a what-if), so a guest whose willingness or purse is below the price is refused at the counter as in the game; <paramref name="freeVisits"/> runs them without price terms
	/// (the price test reads a price of 0 and the shops have no effects: the measurements before the price terms were added, docs/WINTER.md, "Price").
	/// </summary>
	internal static Sales MeasureSales( ulong seedValue, int month, int weather, int days, int? kioskSummer = null, int? kioskSpring = null, bool kioskReturn = true, IReadOnlyList<ShopPrice>? prices = null, bool freeVisits = false )
	{
		var refusals = new RefusalCounter();
		var park = new DeterminismTests.SyntheticPark( new WorldSeed( seedValue ), refusals, winterShops: true, winterPrices: freeVisits ? null : prices ?? ShippedPrices(), prepareGuests: guests =>
		{
			guests.WinterMonth = () => month;
			guests.UmbrellaWeatherState = () => weather;
			guests.WinterWeatherState = () => weather;
			guests.WinterKioskReturnEnabled = kioskReturn;
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
		return new Sales( (int)seedValue, month, weather, days, (int)park.Guests.Admissions, sales[0], sales[1], dressed.Count, holding.Count,
			refusals.Price.GetValueOrDefault( shopIds[0] ), refusals.Price.GetValueOrDefault( shopIds[1] ) );
	}

	/// <summary>Counts the price refusals at the stand point per attraction (a passive observer: it changes nothing).</summary>
	internal sealed class RefusalCounter : IGuestObserver
	{
		public Dictionary<int, int> Price { get; } = new();
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns )
		{
			if ( reason == QueueEndReason.Price )
				Price[attractionId] = Price.GetValueOrDefault( attractionId ) + 1;
		}
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
	}

	internal sealed record Cold( int Seed, bool ColdOn, bool Shops, bool RainMood, int Guests, double SnowHappiness, double DressedHappiness, double BareHappiness, int Snowy, int Affected, int HoldingWinter, int Kiosk, int Stall );

	/// <summary>
	/// A snowy week of the Halloween park: its real weather (seed 21, from day 328 so it crosses the first December rolls) drives the cross park for 20 days. Mean happiness of the guests sampled during snow turns, split by outfit.
	/// </summary>
	internal static Cold MeasureCold( ulong seedValue, bool coldOn, bool shops, bool rainMood )
	{
		var weather = new ParkWeather( WinterWeatherTests.Hallow(), 21, 10, 10 );
		var startTurn = 328L * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn;
		weather.AdvanceTo( ParkCalendar.TickOfTurn( 0 ) );
		weather.AdvanceTo( ParkCalendar.TickOfTurn( startTurn ) );
		var snowy = 0;
		var park = new DeterminismTests.SyntheticPark( new WorldSeed( seedValue ), winterShops: shops, winterPrices: shops ? ShippedPrices() : null, prepareGuests: guests =>
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
		var holdingWinter = new HashSet<int>();
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
					holdingWinter.Add( guest.Id );
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
		return new Cold( (int)seedValue, coldOn, shops, rainMood, seen.Count, snowN == 0 ? 0 : snowSum / snowN, dressedN == 0 ? 0 : dressedSum / dressedN, bareN == 0 ? 0 : bareSum / bareN, snowy, affected.Count, holdingWinter.Count, shopSales[0], shopSales[1] );
	}

	[TestMethod]
	public void TheShopsSellAllYearAndMoreInSnowAndTheNumbersAreReproducible()
	{
		var report = new StringBuilder();
		report.AppendLine( "Sales over 17 park days (sum of seeds 1 to 3), shipped prices: month, weather, admitted, kiosk sales, stall sales, guests with an item, guests with a complete outfit (head item and mittens), price refusals" );
		var sums = new Dictionary<(int Month, int Weather), (int Admitted, int Kiosk, int Stall, int Holding, int Dressed)>();
		foreach ( var month in new[] { 1, 4, 7, 10 } )
			foreach ( var weather in new[] { UmbrellaRules.WeatherNone, UmbrellaRules.WeatherRain, UmbrellaRules.WeatherSnow } )
			{
				var runs = Seeds.Select( seed => MeasureSales( seed, month, weather, 17 ) ).ToList();
				var sum = (runs.Sum( run => run.Admitted ), runs.Sum( run => run.Kiosk ), runs.Sum( run => run.Stall ), runs.Sum( run => run.Holding ), runs.Sum( run => run.Dressed ));
				sums[(month, weather)] = sum;
				report.AppendLine( FormattableString.Invariant( $"month {month,2} weather {weather}: admitted {sum.Item1,4}, kiosk {sum.Item2,4}, stall {sum.Item3,4}, with an item {sum.Item4,4}, dressed {sum.Item5,4}, price refusals kiosk {runs.Sum( run => run.KioskRefused ),3} stall {runs.Sum( run => run.StallRefused ),3}" ) );
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
	public void TheShippedPricesPassForNearlyEveryGuestAndWithoutTheCategoryWordTheKioskWouldSellNothing()
	{
		// [EXT:winter-tuning] the price test (tp.exe 0x004FDE50) with the shops' shipped terms: the willingness rises with happiness, and Shops.sam's RipOffOK 100 doubles it
		var kiosk = ShippedPrice( "kiosk" );
		var stall = ShippedPrice( "gluehwein" );
		static PriceTerms Terms( ShopPrice price, int ripOffOk ) => new( price.Price, price.InitCostOfGoods, 100, false, price.InitCostOfGoods, price.ThirstEffect, price.HungerEffect, price.VomitEffect, price.HappinessEffect, ripOffOk );
		var report = new StringBuilder();
		report.AppendLine( FormattableString.Invariant( $"Willingness to pay at thirst, hunger and illness 0, by happiness (kiosk price {kiosk.Price}, stall price {stall.Price}): RipOffOK {ShopsSamRipOffOk} (shipped) / RipOffOK 0" ) );
		foreach ( var happiness in new[] { 0, 5, 10, 25, 50, 75, 100 } )
			report.AppendLine( FormattableString.Invariant( $"happiness {happiness,3}: kiosk {QueueJoinRules.WillingnessToPay( Terms( kiosk, ShopsSamRipOffOk ), happiness, 0, 0, 0 ),3} / {QueueJoinRules.WillingnessToPay( Terms( kiosk, 0 ), happiness, 0, 0, 0 ),3}, stall {QueueJoinRules.WillingnessToPay( Terms( stall, ShopsSamRipOffOk ), happiness, 0, 0, 0 ),3} / {QueueJoinRules.WillingnessToPay( Terms( stall, 0 ), happiness, 0, 0, 0 ),3}" ) );
		Assert.AreEqual( ShopsSamRipOffOk, kiosk.RipOffOk, "the kiosk's .sam leaves RipOffOK to the category" );
		for ( var happiness = 0; happiness <= 100; happiness++ )
		{
			Assert.AreEqual( happiness < 5, QueueJoinRules.TooExpensive( Terms( kiosk, ShopsSamRipOffOk ), happiness, 0, 0, 0, 1000 ), $"the kiosk at happiness {happiness}: refused only below 5" );
			Assert.IsFalse( QueueJoinRules.TooExpensive( Terms( stall, ShopsSamRipOffOk ), happiness, 0, 0, 0, 1000 ), $"the stall at happiness {happiness}: never refused on willingness" );
			Assert.IsTrue( QueueJoinRules.TooExpensive( Terms( kiosk, 0 ), happiness, 0, 0, 0, 1000 ), $"without the category word the kiosk's willingness (at most 22) is below its price at happiness {happiness}" );
		}
		// a snowy January in the cross park: the shipped prices against free visits and against the terms without the category word (the reviewer's 0 sales)
		var bare = new[] { kiosk with { RipOffOk = 0 }, stall with { RipOffOk = 0 } };
		var priced = Seeds.Select( seed => MeasureSales( seed, 1, UmbrellaRules.WeatherSnow, 17 ) ).ToList();
		var free = Seeds.Select( seed => MeasureSales( seed, 1, UmbrellaRules.WeatherSnow, 17, freeVisits: true ) ).ToList();
		var noWord = Seeds.Select( seed => MeasureSales( seed, 1, UmbrellaRules.WeatherSnow, 17, prices: bare ) ).ToList();
		report.AppendLine( "Snowy January over 17 park days, seeds 1 to 3: admitted, kiosk sales / price refusals, stall sales / price refusals, guests with an item" );
		foreach ( var (name, runs) in new[] { ("shipped prices", priced), ("free visits", free), ("RipOffOK 0", noWord) } )
			report.AppendLine( FormattableString.Invariant( $"{name}: admitted {runs.Sum( run => run.Admitted ),4}, kiosk {runs.Sum( run => run.Kiosk ),4} / {runs.Sum( run => run.KioskRefused ),4}, stall {runs.Sum( run => run.Stall ),4} / {runs.Sum( run => run.StallRefused ),4}, with an item {runs.Sum( run => run.Holding ),4}" ) );
		Write( report.ToString() );
		Assert.IsTrue( priced.Sum( run => run.Kiosk ) > 0 && priced.Sum( run => run.Stall ) > 0, "both shops sell at their shipped prices" );
		Assert.IsTrue( priced.Sum( run => run.KioskRefused ) * 10 <= priced.Sum( run => run.Kiosk ), "at most one price refusal in ten kiosk sales" );
		Assert.IsTrue( priced.Sum( run => run.StallRefused ) * 10 <= priced.Sum( run => run.Stall ), "at most one price refusal in ten stall sales" );
		Assert.AreEqual( 0, free.Sum( run => run.KioskRefused + run.StallRefused ), "a free visit is never refused" );
		Assert.AreEqual( 0, noWord.Sum( run => run.Kiosk ), "without the category word nobody pays the kiosk's 25" );
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
	public void InASnowyJanuaryGuestsNowCompleteTheirOutfitBecauseTheKioskDoesNotCountAsRecentlyUsed()
	{
		// [EXT:winter-kiosk-return] user-approved 2026-10-19 ("Laten terugkomen"): the before is the game's own history rules (nobody bought a second item), the after is the shipped rule
		var report = new StringBuilder();
		report.AppendLine( "Snowy January over 17 park days, seeds 1 to 3: kiosk returns off (before) and on (after): admitted, kiosk sales, guests with an item, guests with a complete outfit" );
		var before = Seeds.Select( seed => MeasureSales( seed, 1, UmbrellaRules.WeatherSnow, 17, kioskReturn: false ) ).ToList();
		var after = Seeds.Select( seed => MeasureSales( seed, 1, UmbrellaRules.WeatherSnow, 17 ) ).ToList();
		foreach ( var (name, runs) in new[] { ("before", before), ("after", after) } )
			report.AppendLine( FormattableString.Invariant( $"{name}: admitted {runs.Sum( run => run.Admitted ),4}, kiosk {runs.Sum( run => run.Kiosk ),4}, with an item {runs.Sum( run => run.Holding ),4}, complete outfit {runs.Sum( run => run.Dressed ),4}" ) );
		Write( report.ToString() );
		Assert.AreEqual( 0, before.Sum( run => run.Dressed ), "the game's history rules: nobody came back for the second item" );
		Assert.IsTrue( after.Sum( run => run.Dressed ) > 0, "now full outfits occur" );
		Assert.IsTrue( after.Sum( run => run.Dressed ) * 20 >= after.Sum( run => run.Admitted ), "at least one guest in twenty is fully dressed" );
		// a sale beyond a guest's first is a second item: the guests who come back take kiosk turns first-time buyers would have had, so the total can stay level while the second items rise
		Assert.IsTrue( after.Sum( run => run.Kiosk - run.Holding ) > before.Sum( run => run.Kiosk - run.Holding ), "the kiosk sells the second items" );
		Assert.IsTrue( after.Sum( run => run.Dressed ) <= after.Sum( run => run.Holding ), "a complete outfit is an item" );
	}

	[TestMethod]
	public void TheColdRuleCostsUndressedGuestsHappinessOverASnowyHallowWeekAndDressedGuestsLessOrNothing()
	{
		var report = new StringBuilder();
		report.AppendLine( "Halloween park, real weather from day 328, 20 days, seeds 1 to 3: setup, guests, snowy samples, mean happiness in snow (all / with item / without item), guests affected by the cold rule, guests holding any winter state (an item or a glühwein warmth)" );
		var rows = new List<Cold>();
		foreach (var (coldOn, shops, rainMood) in new[] { (false, false, true), (false, true, true), (true, false, true), (true, true, true), (true, true, false) } )
			foreach ( var seed in Seeds )
			{
				var row = MeasureCold( seed, coldOn, shops, rainMood );
				rows.Add( row );
				report.AppendLine( FormattableString.Invariant( $"cold {(coldOn ? "on " : "off")} shops {(shops ? "yes" : "no ")} rainmood {(rainMood ? "on " : "off")} seed {seed}: guests {row.Guests,4}, snowy turns {row.Snowy,4}, kiosk {row.Kiosk,3} stall {row.Stall,3}, happiness {row.SnowHappiness,6:0.0} / {row.DressedHappiness,6:0.0} / {row.BareHappiness,6:0.0}, affected {row.Affected,4}, holding winter {row.HoldingWinter,4}" ) );
			}
		Write( report.ToString() );
		Assert.IsTrue( rows.All( row => row.Snowy > 0 ), "the Halloween week snows" );
		double Mean( bool cold, bool shops ) => rows.Where( row => row.ColdOn == cold && row.Shops == shops && row.RainMood ).Average( row => row.SnowHappiness );
		Assert.IsTrue( Mean( true, false ) < Mean( false, false ), "with nobody dressed the cold rule lowers the mood in snow against the rain mood's +1" );
		// the cold rule's cost per outfit, over the shop parks: the guests with an item lose less than the guests without one (or nothing)
		double Split( bool cold, Func<Cold, double> pick ) => rows.Where( row => row.ColdOn == cold && row.Shops && row.RainMood ).Average( pick );
		var dressedCost = Split( false, row => row.DressedHappiness ) - Split( true, row => row.DressedHappiness );
		var bareCost = Split( false, row => row.BareHappiness ) - Split( true, row => row.BareHappiness );
		Assert.IsTrue( bareCost > 0, $"the cold rule costs the guests without an item happiness ({bareCost:0.00})" );
		Assert.IsTrue( dressedCost < bareCost, $"and the guests with an item less ({dressedCost:0.00} against {bareCost:0.00})" );
	}

	private static void Write( string text )
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_WINTER_MEASURE" ) is { Length: > 0 } path )
			File.AppendAllText( path, text + "\n" );
	}
}
