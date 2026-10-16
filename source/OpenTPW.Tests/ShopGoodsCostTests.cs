using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The cost of goods a shop sale books (tp.exe <c>0x004E1B40</c>, called by the use routine <c>0x004FE1E0</c>).</summary>
[TestClass]
public class ShopGoodsCostTests
{
	[TestMethod]
	public void TheBuiltShopBooksItsCostOfGoodsAsItStands()
	{
		Assert.AreEqual( 100, ShopGoodsCost.Of( 100, 50, 50, 2 ), "both bytes 50: no term" );
		Assert.AreEqual( 0, ShopGoodsCost.Of( 0, 255, 255, 1 ) );
	}

	[TestMethod]
	public void TheQualityByteScalesTheCostByFiveThousandthsPerPointClampedToHalf()
	{
		Assert.AreEqual( 125, ShopGoodsCost.Of( 100, 100, 50, 2 ), "+50 points: +25 %" );
		Assert.AreEqual( 75, ShopGoodsCost.Of( 100, 0, 50, 2 ), "-50 points: -25 %" );
		Assert.AreEqual( 150, ShopGoodsCost.Of( 100, 255, 50, 2 ), "+205 points clamp to +50 %" );
		Assert.AreEqual( 8, ShopGoodsCost.Of( 7, 100, 50, 2 ), "7 x 1.25 = 8.75 truncates toward zero" );
		Assert.AreEqual( 50, ShopGoodsCost.Of( 100, 0, 0, 4 ), "quality -25 % and ingredient -25 % (type 4) give 50 %" );
	}

	[TestMethod]
	public void TheIngredientTermHasItsSignFlippedForSpecialIngredients1And3()
	{
		Assert.AreEqual( 75, ShopGoodsCost.Of( 100, 50, 100, 1 ), "ingredient 1: more of it is cheaper" );
		Assert.AreEqual( 125, ShopGoodsCost.Of( 100, 50, 100, 2 ) );
		Assert.AreEqual( 75, ShopGoodsCost.Of( 100, 50, 100, 3 ) );
		Assert.AreEqual( 125, ShopGoodsCost.Of( 100, 50, 100, 4 ) );
		Assert.AreEqual( 100, ShopGoodsCost.Of( 100, 100, 100, 1 ), "+25 % and -25 %" );
	}

	[TestMethod]
	public void TheProductIsRoundedToASingleAtEveryStep()
	{
		// ECON-059: the x87 holds a 24-bit significand: 200 x 1.005 (quality 51) rounds up to 201 before the truncation (a double gives 200.99999998 -> 200)
		Assert.AreEqual( 201, ShopGoodsCost.Of( 200, 51, 50, 2 ) );
		Assert.AreEqual( 439, ShopGoodsCost.Of( 333, 77, 13, 1 ) );
		// a cost beyond 2^29 needs more than a double's 53 bits for the exact product: checked against an exact rational model
		Assert.AreEqual( 1005000000, ShopGoodsCost.Of( 1_000_000_007, 51, 50, 2 ) );
	}

	[TestMethod]
	public void ASaleBooksTheScaledCostEverywhere()
	{
		var park = EconomyTestData.Park();
		var shop = park.RegisterExisting( 1203 );
		park.SetPrices( shop.Id, 50, costOfGoods: 20 );
		park.SetShopSettings( shop.Id, qualityOfGoods: 100 );
		var info = EconomyTestData.Catalog().Objects.Single( item => item.InfoId == 1203 );
		var expected = ShopGoodsCost.Of( 20, 100, shop.IngredientAmount, info.SpecialIngredient );
		Assert.AreNotEqual( 20, expected, "the quality scales the cost" );
		var before = park.Balance;
		Assert.IsTrue( park.TryBuy( shop.Id, 1000, out _ ) );
		Assert.AreEqual( before + 50 - expected, park.Balance );
		Assert.AreEqual( expected, shop.CostsThisMonth );
		Assert.AreEqual( 50 - expected, shop.TotalProfit );
		Assert.AreEqual( 50 - expected, park.Counters[ParkCounters.ShopProfit] );
	}
}
