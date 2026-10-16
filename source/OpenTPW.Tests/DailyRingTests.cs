using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// An attraction's six daily rings (tp.exe constructor 0x004DB090, day message 0x004DD320, count 0x00495D40, read 0x00495CF0, sum 0x00495C20, profit 0x004E1C70):
/// their roll, their sums and their save (ECON-057).
/// </summary>
[TestClass]
public class DailyRingTests
{
	[TestMethod]
	public void TheVisitAverageTakesTheFirstValueAndHalvesLaterOnesTowardZero()
	{
		var ring = new DailyRing();
		ring.Average( 9 );
		Assert.AreEqual( 9, ring.Today, "a running value of 0 takes the value (0x004E1E00)" );
		ring.Average( 4 );
		Assert.AreEqual( 6, ring.Today, "(9 + 4) / 2, truncated" );
		ring.Today = -3;
		ring.Average( 0 );
		Assert.AreEqual( -1, ring.Today, "the sign correction rounds toward zero: (-3 + 0) / 2" );
		ring.Today = 0;
		ring.Average( 0 );
		ring.Average( 8 );
		Assert.AreEqual( 8, ring.Today, "a first value of 0 leaves the next one to be taken whole" );
	}

	[TestMethod]
	public void TheSatisfactionIsFiftyPlusTheTruncatedMeanOfTheSignedEntries()
	{
		var ring = new DailyRing();
		Assert.AreEqual( 50, ring.Satisfaction, "no entries give 0x32 (0x004E1E30)" );
		foreach ( var change in new[] { 9, -4, 0 } )
		{
			ring.Today = change;
			ring.Roll();
		}
		Assert.AreEqual( 51, ring.Satisfaction, "the mean 5 / 3 = 1.67 truncates to 1" );
		var negative = new DailyRing();
		foreach ( var change in new[] { -9, 3, 0 } )
		{
			negative.Today = change;
			negative.Roll();
		}
		Assert.AreEqual( 48, negative.Satisfaction, "-6 / 3 = -2" );
	}

	[TestMethod]
	public void ARingStartsEmptyAndTakesOneDayPerRoll()
	{
		var ring = new DailyRing();
		Assert.AreEqual( (-1, false, 0), (ring.Head, ring.Wrapped, ring.Count), "the constructor's head -1, size 30 and cleared flag" );
		Assert.IsFalse( ring.TryGet( 0, out _ ) );
		ring.Today = 5;
		ring.Roll();
		Assert.AreEqual( (0, 1, 0), (ring.Head, ring.Count, ring.Today) );
		ring.Today = 7;
		ring.Roll();
		Assert.IsTrue( ring.TryGet( 0, out var newest ) );
		Assert.IsTrue( ring.TryGet( 1, out var older ) );
		Assert.AreEqual( (7, 5), (newest, older) );
		Assert.IsFalse( ring.TryGet( 2, out _ ), "past the days the ring holds" );
		Assert.AreEqual( 12L, ring.Total );
		Assert.AreEqual( 7L, ring.Sum( 1 ), "a shorter sum takes the newest days" );
		Assert.AreEqual( 12L, ring.Sum( 29 ), "a longer sum is cut to the count" );
	}

	[TestMethod]
	public void TheThirtyFirstDayWrapsAndTheOldestDayLeaves()
	{
		var ring = new DailyRing();
		for ( var day = 1; day <= 31; day++ )
		{
			ring.Today = day;
			ring.Roll();
		}
		Assert.AreEqual( (0, true, 30), (ring.Head, ring.Wrapped, ring.Count), "the head wrapped to slot 0 and set the full flag" );
		Assert.AreEqual( Enumerable.Range( 2, 30 ).Sum(), (int)ring.Total, "day 1 has left the window" );
		CollectionAssert.AreEqual( Enumerable.Range( 2, 30 ).ToArray(), ring.Entries.ToArray(), "oldest first" );
		ring.TryGet( 0, out var newest );
		ring.TryGet( 29, out var oldest );
		Assert.AreEqual( (31, 2), (newest, oldest) );
	}

	[TestMethod]
	public void EntriesAreAddedAsUnsignedWordsExceptTheSatisfactionRingsSignedSum()
	{
		var ring = new DailyRing { Today = -2 };
		ring.Roll();
		Assert.AreEqual( uint.MaxValue - 1L, ring.Total, "the sum adds a dword with a zero high word (0x00495C20)" );
		Assert.AreEqual( -2L, ring.SignedTotal, "the satisfaction gauge adds its entries as integers (0x004E1E30)" );
	}

	[TestMethod]
	public void ARestoredRingKeepsItsHeadAndRejectsAnInconsistentOne()
	{
		var ring = new DailyRing();
		for ( var day = 1; day <= 34; day++ )
		{
			ring.Today = day;
			ring.Roll();
		}
		ring.Today = 9;
		var again = DailyRing.FromState( ring.Head, ring.Wrapped, ring.Today, ring.Entries );
		CollectionAssert.AreEqual( ring.Entries.ToArray(), again.Entries.ToArray() );
		Assert.AreEqual( (ring.Head, ring.Wrapped, ring.Today), (again.Head, again.Wrapped, again.Today) );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => DailyRing.FromState( 3, false, 0, new[] { 1, 2 } ) );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => DailyRing.FromState( 30, false, 0, new int[31] ) );
	}

	[TestMethod]
	public void AShopsSalesRollEachDayAndTheWindowSumsThemNotACalendarMonth()
	{
		var economy = EconomyTestData.Park();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1203, out var shop ) );
		economy.SetPrices( shop!.Id, 10, 0, 0 );
		shop.IsOpen = true;
		Assert.IsTrue( economy.TryBuy( shop.Id, 100, out _ ) );
		Assert.IsTrue( economy.TryBuy( shop.Id, 100, out _ ) );
		Assert.AreEqual( 0L, shop.CustomersLast30Days, "today's sales are not in a ring until the day message" );
		Assert.AreEqual( 2, shop.CustomersRing.Today );
		economy.AdvanceDays( 1 );
		Assert.AreEqual( (2L, 20L), (shop.CustomersLast30Days, shop.TakingsLast30Days) );
		Assert.AreEqual( shop.TakingsLast30Days - shop.CostsRing.Total, shop.ProfitLast30Days );
		for ( var day = 0; day < 30; day++ )
			economy.AdvanceDays( 1 );
		Assert.AreEqual( 0L, shop.CustomersLast30Days, "the first day has left the 30-day window" );
	}

	[TestMethod]
	public void ASideshowPlayFeedsTheCustomerTakingsCostsAndWinnersRings()
	{
		var economy = EconomyTestData.Park();
		var sideshow = economy.RegisterExisting( 1303 );
		economy.SetPrices( sideshow.Id, 20, 50, 0 );
		Assert.IsTrue( economy.PlaySideshow( sideshow.Id, 100, out _, out var won ) );
		Assert.IsTrue( won, "a chance of losing of 0 always wins" );
		Assert.AreEqual( (1, 20, 50, 1), (sideshow.CustomersRing.Today, sideshow.TakingsRing.Today, sideshow.CostsRing.Today, sideshow.WinnersRing.Today) );
		economy.AdvanceDays( 1 );
		Assert.AreEqual( (1L, 20L, 1L, -30L), (sideshow.CustomersLast30Days, sideshow.TakingsLast30Days, sideshow.WinnersLast30Days, sideshow.ProfitLast30Days) );
		Assert.AreEqual( (0, 0, 0, 0), (sideshow.CustomersRing.Today, sideshow.TakingsRing.Today, sideshow.CostsRing.Today, sideshow.WinnersRing.Today), "the day message cleared the running values" );
	}

	[TestMethod]
	public void ARideUseCountsInTheCustomersRing()
	{
		var economy = EconomyTestData.Park();
		var ride = economy.RegisterExisting( 1100 );
		economy.RecordRideUse( ride.Id );
		economy.RecordRideUse( ride.Id );
		economy.AdvanceDays( 1 );
		Assert.AreEqual( 2L, ride.CustomersLast30Days );
	}

	[TestMethod]
	public void TheRingsSurviveASaveAndAnOlderSaveSeedsThemFromLastMonth()
	{
		var economy = EconomyTestData.Park();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1203, out var shop ) );
		economy.SetPrices( shop!.Id, 10, 0, 0 );
		shop.IsOpen = true;
		Assert.IsTrue( economy.TryBuy( shop.Id, 100, out _ ) );
		economy.AdvanceDays( 1 );
		Assert.IsTrue( economy.TryBuy( shop.Id, 100, out _ ) );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Capture( economy ), economy.Settings, economy.Catalog );
		var loaded = restored.Objects.Single( item => item.InfoId == 1203 );
		Assert.AreEqual( (1L, 10L, 1), (loaded.CustomersLast30Days, loaded.TakingsLast30Days, loaded.CustomersRing.Today) );
		// an older save has no rings: the first four take last month's totals as one day
		var text = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( text, "\"Rings\"" );
	}

	[TestMethod]
	public void ASaveWithoutRingsSeedsTheFirstFourFromLastMonth()
	{
		var economy = EconomyTestData.Park();
		var shop = economy.RegisterExisting( 1203 );
		shop.CustomersLastMonth = 7;
		shop.TakingsLastMonth = 90;
		shop.CostsLastMonth = 30;
		shop.WinnersLastMonth = 2;
		var data = ParkSaveFile.Capture( economy );
		Assert.IsNull( data.Objects.Single().Rings, "new rings are not written" );
		var restored = ParkSaveFile.Restore( data, economy.Settings, economy.Catalog ).Objects.Single();
		Assert.AreEqual( (7L, 90L, 60L, 2L), (restored.CustomersLast30Days, restored.TakingsLast30Days, restored.ProfitLast30Days, restored.WinnersLast30Days) );
		Assert.AreEqual( 0L, restored.SatisfactionRing.Total );
	}
}
