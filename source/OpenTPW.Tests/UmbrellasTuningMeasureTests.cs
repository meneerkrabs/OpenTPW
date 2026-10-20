using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:umbrellas-tuning] The headless measurements of docs/UMBRELLAS.md, "Tuning" (phase U5): a park with a brolly shop through a rainy week, with Theme Park Inc's kid score of 75 % and with 100 %, the rain mood rule on and off.
/// The same synthetic cross park as the determinism pins (fixed seed, a drinks shop, the brolly shop at (10, 1)); four clear days let guests arrive, then seven days of rain, then three clear days. Presentation-free:
/// nothing here is pinned, the tests check the shape of the numbers (and write them to the file named by <c>OPENTPW_UMBRELLAS_MEASURE</c> when it is set, to regenerate the doc's table).
/// </summary>
[TestClass]
[DoNotParallelize]
public class UmbrellasTuningMeasureTests
{
	/// <summary>Ticks of one park day: 23.04 turns of 14.88 ticks.</summary>
	private const double TicksPerDay = 23.04 * 14.88;

	internal sealed record Measurement( int Seed, int BalloonEvery, bool Shop, int KidScore, bool RainMood, int Admitted, int Buyers, long Balance, double RainHappiness, double BuyerHappiness, double OthersHappiness, int BalloonsHeld, int BalloonsReleased, int DressedBuyers );

	/// <param name="balloonEvery">Every n-th guest to arrive holds a balloon of life 120 (0: nobody does).</param>
	internal static Measurement Measure( ulong seedValue, bool shop, int kidScore, bool rainMood, int balloonEvery = 2, int clearDays = 4, int rainDays = 7, int afterDays = 3 )
	{
		var rainStart = (long)Math.Round( clearDays * TicksPerDay );
		var rainEnd = (long)Math.Round( (clearDays + rainDays) * TicksPerDay );
		var total = (long)Math.Round( (clearDays + rainDays + afterDays) * TicksPerDay );
		var park = new DeterminismTests.SyntheticPark( new WorldSeed( seedValue ), brollyShop: shop, brollyKidScore: kidScore, prepareGuests: guests =>
		{
			if ( rainMood )
				guests.RainMood = RainMoodSettings.Default;
			guests.UmbrellaWeatherState = () => guests.TickCount >= rainStart && guests.TickCount < rainEnd ? UmbrellaRules.WeatherRain : UmbrellaRules.WeatherNone;
		} );
		var seen = new HashSet<int>();
		var buyers = new HashSet<int>();
		var held = 0;
		var lifeBefore = new Dictionary<int, int>();
		var lostEarly = new HashSet<int>();
		var dressed = new HashSet<int>();
		double rainSum = 0;
		long rainSamples = 0, buyerSamples = 0, otherSamples = 0;
		double buyerSum = 0, otherSum = 0;
		for ( long tick = 0; tick < total; tick += 15 )
		{
			park.Run( 15 );
			foreach ( var guest in park.Guests.Guests )
			{
				// a held balloon for every n-th guest to arrive, as --debug-guest-looks dresses them (life 120 of 255: a fresh balloon cuts the shop's score to nothing, see the balloon factor), so the balloon trade has something to trade
				if ( seen.Add( guest.Id ) && balloonEvery > 0 && seen.Count % balloonEvery == 0 )
				{
					guest.HasBalloon = true;
					guest.BalloonLife = 120;
					held++;
					dressed.Add( guest.Id );
				}
				if ( guest.UmbrellaState != 0 )
					buyers.Add( guest.Id );
				// a balloon that was gone while it still had life (it only falls by one a phase): let go, by a shop entry or an opening umbrella
				if ( lifeBefore.TryGetValue( guest.Id, out var before ) && before > 20 && !guest.HasBalloon )
					lostEarly.Add( guest.Id );
				lifeBefore[guest.Id] = guest.HasBalloon ? guest.BalloonLife : 0;
				if ( park.Guests.TickCount >= rainStart && park.Guests.TickCount < rainEnd )
				{
					rainSum += guest.Happiness;
					rainSamples++;
					if ( buyers.Contains( guest.Id ) ) { buyerSum += guest.Happiness; buyerSamples++; }
					else { otherSum += guest.Happiness; otherSamples++; }
				}
			}
		}
		return new Measurement( (int)(seedValue & 0xFF), balloonEvery, shop, kidScore, rainMood, (int)park.Guests.Admissions, buyers.Count, park.Economy.Balance,
			rainSamples == 0 ? 0 : rainSum / rainSamples, buyerSamples == 0 ? 0 : buyerSum / buyerSamples, otherSamples == 0 ? 0 : otherSum / otherSamples, held, buyers.Count( lostEarly.Contains ), buyers.Count( dressed.Contains ) );
	}

	[TestMethod]
	public void ARainyWeekWithABrollyShopSellsUmbrellasAndWithoutOneSellsNone()
	{
		var with = Measure( WorldSeed.DefaultValue, shop: true, kidScore: 100, rainMood: false );
		var without = Measure( WorldSeed.DefaultValue, shop: false, kidScore: 100, rainMood: false );
		Assert.IsTrue( with.Buyers >= 5, $"{with.Buyers} buyers of {with.Admitted} admitted" );
		Assert.AreEqual( 0, without.Buyers );
	}

	[TestMethod]
	public void TheKidScoreOnlyMattersThroughBalloonHoldersAndSellsFewerUmbrellasAt75Percent()
	{
		var seeds = new[] { WorldSeed.DefaultValue, WorldSeed.DefaultValue ^ 0x11, WorldSeed.DefaultValue ^ 0x22, WorldSeed.DefaultValue ^ 0x33 };
		int buyers75 = 0, buyers100 = 0, released75 = 0, released100 = 0;
		foreach ( var seed in seeds )
		{
			// with nobody holding a balloon the shop's score never decides a purchase, so the two values play out the same
			var plain75 = Measure( seed, shop: true, kidScore: 75, rainMood: false, balloonEvery: 0 );
			var plain100 = Measure( seed, shop: true, kidScore: 100, rainMood: false, balloonEvery: 0 );
			Assert.AreEqual( (plain100.Buyers, plain100.Balance, plain100.RainHappiness), (plain75.Buyers, plain75.Balance, plain75.RainHappiness), $"seed {seed:X}: no balloons, so the same run" );

			var seventyFive = Measure( seed, shop: true, kidScore: 75, rainMood: false );
			var hundred = Measure( seed, shop: true, kidScore: 100, rainMood: false );
			Assert.IsTrue( seventyFive.Buyers <= hundred.Buyers, $"seed {seed:X}: 75 %: {seventyFive.Buyers}, 100 %: {hundred.Buyers}" );
			buyers75 += seventyFive.Buyers;
			buyers100 += hundred.Buyers;
			released75 += seventyFive.BalloonsReleased;
			released100 += hundred.BalloonsReleased;
		}
		// measured over these four seeds: 50 against 55 buyers, 16 against 28 balloons let go; the extra sales at 100 % are balloon holders trading a balloon in
		Assert.IsTrue( buyers75 < buyers100, $"75 %: {buyers75} buyers, 100 %: {buyers100}" );
		Assert.IsTrue( released75 < released100, $"balloons let go, 75 %: {released75}, 100 %: {released100}" );
	}

	[TestMethod]
	public void MeasureTheTuningMatrix()
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_UMBRELLAS_MEASURE" ) is not { Length: > 0 } path )
			return;
		var seeds = new[] { WorldSeed.DefaultValue, WorldSeed.DefaultValue ^ 0x11, WorldSeed.DefaultValue ^ 0x22, WorldSeed.DefaultValue ^ 0x33 };
		var lines = new List<string> { "balloonEvery seed shop kid% mood admitted buyers balance rainHappy buyerHappy otherHappy balloonsHeld balloonsReleased dressedBuyers" };
		foreach ( var balloons in new[] { 2, 0 } )
			foreach ( var seed in seeds )
				foreach ( var (shop, kid) in new[] { (false, 100), (true, 75), (true, 100) } )
					foreach ( var mood in new[] { false, true } )
					{
						var m = Measure( seed, shop, kid, mood, balloons );
						lines.Add( string.Create( CultureInfo.InvariantCulture, $"{balloons} {m.Seed:X2} {(shop ? "shop" : "none")} {kid} {(mood ? "on" : "off")} {m.Admitted} {m.Buyers} {m.Balance} {m.RainHappiness:F2} {m.BuyerHappiness:F2} {m.OthersHappiness:F2} {m.BalloonsHeld} {m.BalloonsReleased} {m.DressedBuyers}" ) );
					}
		File.WriteAllLines( path, lines );
	}
}
