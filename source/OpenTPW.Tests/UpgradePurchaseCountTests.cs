using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The count of levels bought for a ride (<see cref="ParkObjectState.UpgradePurchases"/>, C4b-2): the identity of its pending purchase, which a guest's upgrade
/// cancellation names. The economy counts accepted buys, the save keeps the count optional, and the world hash leaves it out.
/// </summary>
[TestClass]
public class UpgradePurchaseCountTests
{
	private static (ParkEconomy Park, ParkObjectState Ride) Researched( int cash = 50000 )
	{
		var park = EconomyTestData.Park( initialCash: cash );
		var ride = park.RegisterExisting( 1100 );
		park.Research.AddResearcherPoints( 1000000 );
		Assert.IsTrue( park.Research.IsAvailable( 1100, 1 ) );
		return (park, ride);
	}

	private static ParkObjectState Restored( string json, ParkEconomy park, int id )
	{
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.IsTrue( restored.TryGetObject( id, out var ride ) );
		return ride;
	}

	[TestMethod]
	public void EachAcceptedBuyRaisesTheCountAndARefusedBuyOrACancelDoesNot()
	{
		var park = EconomyTestData.Park( initialCash: 50000 );
		var ride = park.RegisterExisting( 1100 );
		Assert.AreEqual( 0, ride.UpgradePurchases, "a new record counts no purchase" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotResearched, park.TryBuyUpgrade( ride.Id ) );
		Assert.AreEqual( 0, ride.UpgradePurchases, "a refused buy (not researched) does not count" );
		park.Research.AddResearcherPoints( 1000000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		Assert.AreEqual( 1, ride.UpgradePurchases );
		Assert.AreEqual( ParkEconomy.PurchaseResult.UpgradeInProgress, park.TryBuyUpgrade( ride.Id ) );
		Assert.AreEqual( 1, ride.UpgradePurchases, "a refused buy (one is pending) does not count" );
		Assert.IsTrue( park.TryCancelUpgrade( ride.Id ) );
		Assert.AreEqual( 1, ride.UpgradePurchases, "a cancel does not lower the count" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		Assert.AreEqual( 2, ride.UpgradePurchases, "the same level bought again is another purchase" );
		var (poor, other) = Researched( 400 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughMoney, poor.TryBuyUpgrade( other.Id ) );
		Assert.AreEqual( 0, other.UpgradePurchases, "a refused buy (the balance) does not count" );
		var instant = EconomyTestData.Park( mode: ParkGameMode.InstantAction );
		var shop = instant.RegisterExisting( 1100 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotAvailableInInstantAction, instant.TryBuyUpgrade( shop.Id ) );
		Assert.AreEqual( 0, shop.UpgradePurchases, "a refused buy (the game type) does not count" );
	}

	[TestMethod]
	public void ARidesPurchaseCountIsSavedAndLoadedAndAbsentWhileZero()
	{
		var (park, ride) = Researched();
		Assert.IsFalse( ParkSaveFile.Serialize( park ).Contains( "UpgradePurchases" ), "absent while 0" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		Assert.IsTrue( park.TryCancelUpgrade( ride.Id ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		var json = ParkSaveFile.Serialize( park );
		StringAssert.Contains( json, "\"UpgradePurchases\": 2," );
		var loaded = Restored( json, park, ride.Id );
		Assert.AreEqual( (2, 1), (loaded.UpgradePurchases, loaded.PendingLevel) );
	}

	[TestMethod]
	public void AnOlderSaveWithAPendingLevelLoadsWithNoPurchasesAndANegativeCountIsRefused()
	{
		var (park, ride) = Researched();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		var json = ParkSaveFile.Serialize( park );
		var older = json.Replace( "\"UpgradePurchases\": 1,", "" );
		Assert.AreNotEqual( json, older, "the field was written" );
		Assert.IsFalse( older.Contains( "UpgradePurchases" ) );
		var loaded = Restored( older, park, ride.Id );
		Assert.AreEqual( (0, 1), (loaded.UpgradePurchases, loaded.PendingLevel), "a save without the field loads with 0 and keeps its pending level" );
		var negative = json.Replace( "\"UpgradePurchases\": 1,", "\"UpgradePurchases\": -1," );
		Assert.AreNotEqual( json, negative );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( negative ) ) );
	}

	[TestMethod]
	public void TheWorldHashIgnoresTheCountOfLevelsBought()
	{
		var (park, ride) = Researched();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		Assert.AreEqual( 1, ride.UpgradePurchases );
		var counted = WorldStateHash.Compute( new WorldStateSources { Economy = park } );
		ride.UpgradePurchases = 7;
		Assert.AreEqual( counted, WorldStateHash.Compute( new WorldStateSources { Economy = park } ), "another count, the same world hash" );
		ride.UpgradePurchases = 0;
		Assert.AreEqual( counted, WorldStateHash.Compute( new WorldStateSources { Economy = park } ), "the count cleared, the same world hash" );
		ride.PendingLevel = 0;
		ride.UpgradeCostDue = false;
		Assert.AreNotEqual( counted, WorldStateHash.Compute( new WorldStateSources { Economy = park } ), "the hash still covers the rest of the record" );
	}
}
