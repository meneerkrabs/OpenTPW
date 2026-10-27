using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// What the price test at the stand point records (tp.exe 0x004FDE50, 0x004C74B0) and what a refusal counts on the ride (0x004E1670,
/// rolled each day by 0x004DD320): the park's perception statistics and the walk-away counts, and their save.
/// </summary>
[TestClass]
public class PriceRefusalRecordTests
{
	private static PriceTerms Shop( int price ) => new( price, 0, 0, false, 20, 40, 0, 0, 10, 0 );

	[TestMethod]
	public void AShopRecordsItsIngredientAndAppearanceCategories()
	{
		// willingness 49 at a price of 45: (49 + 5) × 10 − 45 × 10 = 90
		CollectionAssert.AreEqual( new[] { (2, 90), (6, 90) }, QueueJoinRules.PriceStatistics( Shop( 45 ), 49, 2, 1 ) );
		CollectionAssert.AreEqual( new[] { (4, 90), (7, 90) }, QueueJoinRules.PriceStatistics( Shop( 45 ), 49, 4, 2 ) );
		CollectionAssert.AreEqual( new[] { (1, 90) }, QueueJoinRules.PriceStatistics( Shop( 45 ), 49, 1, 0 ) );
		Assert.AreEqual( 0, QueueJoinRules.PriceStatistics( Shop( 45 ), 49, 0, 0 ).Length, "a shop without an ingredient or appearance records nothing" );
		Assert.AreEqual( 0, QueueJoinRules.PriceStatistics( Shop( 0 ), 49, 2, 1 ).Length, "a free visit returns before the statistics" );
	}

	[TestMethod]
	public void AValueIsCappedAtOneHundredAsAnUnsignedWord()
	{
		Assert.AreEqual( (2, 100), QueueJoinRules.PriceStatistics( Shop( 1 ), 49, 2, 0 ).Single(), "(49 + 5) × 10 − 10 = 530 is capped" );
		Assert.AreEqual( (2, 100), QueueJoinRules.PriceStatistics( Shop( 60 ), 49, 2, 0 ).Single(), "a price above the willingness wraps below zero, which is above 99 unsigned" );
		Assert.AreEqual( (2, 0), QueueJoinRules.PriceStatistics( Shop( 54 ), 49, 2, 0 ).Single() );
		Assert.AreEqual( (2, 90), QueueJoinRules.PriceStatistics( Shop( 49 ), 53, 2, 0 ).Single(), "the values come in tens" );
	}

	[TestMethod]
	public void ASideshowRecordsCategoryNine()
	{
		var terms = new PriceTerms( 65, 50, 70, true, 5, 40, 0, 0, 10, 0 );
		Assert.AreEqual( (9, 50), QueueJoinRules.PriceStatistics( terms, 65, 2, 1 ).Single(), "(65 + 5) × 10 − 650 = 50; the shop words are not read" );
	}

	[TestMethod]
	public void TheRingWrapsAfterFiftyValuesAndSetsItsFullFlag()
	{
		var statistics = new ParkPriceStatistics();
		for ( var value = 1; value <= 50; value++ )
			statistics.Record( 3, value );
		Assert.AreEqual( 50, statistics.Count( 3 ) );
		Assert.IsFalse( statistics.Capture().Single().Full, "the fiftieth value fills the last slot" );
		statistics.Record( 3, 51 );
		var ring = statistics.Capture().Single();
		Assert.AreEqual( (3, 0, true), (ring.Category, ring.Head, ring.Full) );
		Assert.AreEqual( 51, ring.Values[0] );
		CollectionAssert.AreEqual( Enumerable.Range( 2, 50 ).Select( value => (byte)value ).ToArray(), statistics.Values( 3 ).ToArray() );
		Assert.AreEqual( 0, statistics.Count( 4 ) );
	}

	[TestMethod]
	public void WalkAwaysCountAndRollIntoThirtyDays()
	{
		var economy = EconomyTestData.Park();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1203, out var shop ) );
		economy.RecordWalkAway( shop!.Id );
		economy.RecordWalkAway( shop.Id );
		Assert.AreEqual( (2L, 2L), (shop.WalkAways, shop.WalkAwaysToday) );
		for ( var day = 0; day < 31; day++ )
		{
			economy.AdvanceDays( 1 );
			economy.RecordWalkAway( shop.Id );
		}
		Assert.AreEqual( 33L, shop.WalkAways );
		Assert.AreEqual( 1L, shop.WalkAwaysToday );
		Assert.AreEqual( ParkEconomy.WalkAwayHistoryDays, shop.WalkAwayDays.Count, "the ring keeps 30 days" );
		Assert.IsTrue( shop.WalkAwayDays.All( day => day == 1 ), "the first day's 2 has left the ring" );
	}

	[TestMethod]
	public void WalkAwaysAndStatisticsSurviveASaveAndOlderSavesLoadWithout()
	{
		var economy = EconomyTestData.Park();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1203, out var shop ) );
		var plain = ParkSaveFile.Serialize( economy );
		StringAssert.DoesNotMatch( plain, new System.Text.RegularExpressions.Regex( "WalkAway|PriceStatistics" ), "nothing is written while nothing was recorded" );
		economy.RecordWalkAway( shop!.Id );
		economy.AdvanceDays( 1 );
		economy.RecordWalkAway( shop.Id );
		economy.PriceStatistics.Record( 9, 42 );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), EconomyTestData.Settings(), EconomyTestData.Catalog() );
		Assert.IsTrue( restored.TryGetObject( shop.Id, out var resumed ) );
		Assert.AreEqual( (2L, 1L), (resumed.WalkAways, resumed.WalkAwaysToday) );
		CollectionAssert.AreEqual( new[] { 1L, 0L }, resumed.WalkAwayDays, "the first day (ECON-063) took the first record, the day that passed the second entry" );
		CollectionAssert.AreEqual( new byte[] { 42 }, restored.PriceStatistics.Values( 9 ).ToArray() );
		var older = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( plain ) ), EconomyTestData.Settings(), EconomyTestData.Catalog() );
		Assert.IsTrue( older.TryGetObject( shop.Id, out var old ) );
		Assert.AreEqual( (0L, 0L, 0), (old.WalkAways, old.WalkAwaysToday, old.WalkAwayDays.Count) );
	}

	[TestMethod]
	public void AnInconsistentStatisticIsRejected()
	{
		var economy = EconomyTestData.Park();
		economy.PriceStatistics.Record( 1, 5 );
		var json = ParkSaveFile.Serialize( economy ).Replace( "\"Category\": 1", "\"Category\": 20" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ) );
	}

	[TestMethod]
	public void AGuestThatRefusesAtTheStandPointCountsAWalkAwayAndRecordsItsView()
	{
		var economy = EconomyTestData.Park();
		economy.Speed = GameSpeed.Fastest;
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 11 );
		var scripts = new RideScriptWorld( 5 );
		var payments = new GuestEconomyBridge( () => economy, guests );
		guests.Payments = payments;
		economy.GuestStatistics = payments;
		economy.RideOperations = payments;
		economy.OpenPark();
		// a salt shop far too dear for anyone: every guest called forward tests the price, records its view in category 2 and walks away
		var shop = new RideVisitorBridge( scripts.AllocateAttractionId(), "Drinks Shop", RideVisitorKind.Shop, 1, 70, 30, GuestNeeds.Thirst, price: 900 )
		{ EntranceCell = (10, 9), ExitCell = (10, 9), SpecialIngredient = 2, InitCostOfGoods = 20, ThirstEffect = 40 };
		var script = GuestTests.CreateShopScript( new VisitorRideScriptEffects( shop, UnimplementedRideScriptEffects.Instance ), scripts, seed: null );
		shop.Attach( script, () => true );
		guests.Register( shop );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1203, out var bought ) );
		payments.Link( shop.AttractionId, bought!.Id );
		for ( var tick = 0; tick < 20000 && bought.WalkAways == 0; tick++ )
		{
			guests.Tick( 1f / 60f );
			shop.HostStep();
			script.Advance( 1f / 60f );
			economy.Advance( (int)economy.Speed );
		}
		Assert.IsTrue( bought.WalkAways > 0, "a guest refused the price" );
		Assert.IsTrue( economy.PriceStatistics.Count( 2 ) >= bought.WalkAways, "every refusal recorded its view first" );
		Assert.AreEqual( 100, economy.PriceStatistics.Values( 2 )[0], "a price above the willingness records 100" );
		Assert.AreEqual( 0, bought.CustomersThisMonth );
	}
}
