using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The price test when a guest is called forward (tp.exe 0x004FDE50, ORACLE.md X16). Each willingness below was produced by the
/// original routine in the x86 oracle (tpw-exe-price-test); at that price the guest pays, one more and it refuses, and a guest
/// with one less in money refuses too.
/// </summary>
[TestClass]
public class PriceTestTests
{
	private static PriceTerms Shop( int price, int initCostOfGoods = 20, int thirst = 40, int hunger = 0, int vomit = 0, int happiness = 10 ) =>
		new( price, 0, 0, false, initCostOfGoods, thirst, hunger, vomit, happiness, 0 );

	[DataTestMethod]
	[DataRow( 49u, 20, 40, 0, 0, 10, 60f, 80f, 10f, 0f )]
	[DataRow( 25u, 20, 40, 0, 0, 10, 0f, 0f, 10f, 0f )]
	[DataRow( 78u, 20, 40, 30, 0, 10, 100f, 100f, 100f, 0f )]
	[DataRow( 32u, 20, 40, 0, 50, 10, 60f, 80f, 10f, 90f )]
	[DataRow( 268u, 150, 40, 0, 0, -30, 37.9f, 80f, 10f, 0f )]
	[DataRow( 38u, 20, 40, -20, 0, 10, 60f, 55.5f, 99.9f, 0f )]
	public void ShopWillingnessMatchesTheOriginal( uint willingness, int initCost, int thirstEffect, int hungerEffect, int vomitEffect, int happinessEffect,
		float happiness, float thirst, float hunger, float illness )
	{
		PriceTerms At( int price ) => Shop( price, initCost, thirstEffect, hungerEffect, vomitEffect, happinessEffect );
		Assert.AreEqual( willingness, QueueJoinRules.WillingnessToPay( At( 1 ), happiness, thirst, hunger, illness ) );
		Assert.IsFalse( QueueJoinRules.TooExpensive( At( (int)willingness ), happiness, thirst, hunger, illness, 1000 ) );
		Assert.IsTrue( QueueJoinRules.TooExpensive( At( (int)willingness + 1 ), happiness, thirst, hunger, illness, 1000 ) );
		Assert.IsTrue( QueueJoinRules.TooExpensive( At( (int)willingness ), happiness, thirst, hunger, illness, willingness - 1 ) );
	}

	[TestMethod]
	public void ASideshowAddsTheExpectedPrize()
	{
		// cost of goods 50 at a win byte of 70 adds 35 before the per-type and happiness factors: willingness 65 where 5 base alone gives less
		var terms = new PriceTerms( 65, 50, 70, true, 5, 40, 0, 0, 10, 0 );
		Assert.AreEqual( 65u, QueueJoinRules.WillingnessToPay( terms, 60f, 80f, 10f, 0f ) );
		Assert.IsFalse( QueueJoinRules.TooExpensive( terms, 60f, 80f, 10f, 0f, 1000 ) );
		Assert.IsTrue( QueueJoinRules.TooExpensive( terms with { Price = 66 }, 60f, 80f, 10f, 0f, 1000 ) );
	}

	[TestMethod]
	public void AFreeVisitNeverFails() => Assert.IsFalse( QueueJoinRules.TooExpensive( Shop( 0 ), 0f, 0f, 0f, 100f, 0 ) );
}
