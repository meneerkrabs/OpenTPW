using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A sale or play at price 0 (tp.exe <c>0x004FE1A0</c>): the price step is skipped whole (no takings, sale count or till), while the cost of goods,
/// a won prize and the customer count are still booked.
/// </summary>
[TestClass]
public class FreeSaleTests
{
	[TestMethod]
	public void AFreeSaleBooksNoTakingsAndCountsNoSale()
	{
		var park = EconomyTestData.Park();
		var shop = park.RegisterExisting( 1203 );
		park.SetPrices( shop.Id, 0, 20 );
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		var takings = park.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.ShopTakings );
		var costs = park.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherCosts );
		var balance = park.Balance;
		Assert.IsTrue( park.TryBuy( shop.Id, 0, out var paid ) );
		Assert.AreEqual( 0, paid );
		Assert.AreEqual( takings, park.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.ShopTakings ) );
		Assert.AreEqual( costs + 20, park.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherCosts ), "the goods are still charged (0x004FE1E0)" );
		Assert.AreEqual( balance - 20, park.Balance );
		Assert.AreEqual( 0L, park.Counters.Values.Where( pair => pair.Key.StartsWith( "sold:" ) ).Sum( pair => pair.Value ), "no sale is counted (0x004E16B0 is skipped)" );
		Assert.AreEqual( -20L, park.Counters[ParkCounters.ShopProfit], "only the goods reach accumulator 12 (0x004E1920)" );
		Assert.AreEqual( 1, shop.CustomersThisMonth, "the customer count does not depend on the price (0x004E1690)" );
		Assert.AreEqual( 0, shop.TakingsThisMonth );
		Assert.IsFalse( events.Any( item => item.Kind == ParkEventKind.GuestPaid ) );
	}

	[TestMethod]
	public void APaidSaleStillBooksTheTakings()
	{
		var park = EconomyTestData.Park();
		var shop = park.RegisterExisting( 1203 );
		park.SetPrices( shop.Id, 30, 20 );
		Assert.IsTrue( park.TryBuy( shop.Id, 100, out var paid ) );
		Assert.AreEqual( 30, paid );
		Assert.AreEqual( 30L, park.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.ShopTakings ) );
		Assert.AreEqual( 1L, park.Counters.Values.Where( pair => pair.Key.StartsWith( "sold:" ) ).Sum( pair => pair.Value ) );
		Assert.AreEqual( 10L, park.Counters[ParkCounters.ShopProfit] );
	}

	[TestMethod]
	public void AFreePlayBooksNoTakingsButPaysThePrize()
	{
		var park = EconomyTestData.Park();
		var sideshow = park.RegisterExisting( 1303 );
		park.SetPrices( sideshow.Id, 0, 50, 0 );
		var balance = park.Balance;
		Assert.IsTrue( park.PlaySideshow( sideshow.Id, 0, out var paid, out var won ) );
		Assert.AreEqual( 0, paid );
		Assert.IsTrue( won, "a chance of losing of 0 always wins" );
		Assert.AreEqual( 0L, park.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.SideshowTakings ) );
		Assert.AreEqual( balance - 50, park.Balance );
		Assert.AreEqual( -50L, park.Counters[ParkCounters.SideshowProfit] );
		Assert.AreEqual( 1, sideshow.CustomersThisMonth );
	}
}
