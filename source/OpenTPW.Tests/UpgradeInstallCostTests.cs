using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The upgrade's money (tp.exe <c>0x004AF480</c> → <c>0x004E0050</c>, <c>0x004DF8F0</c>): the buy handler only requests the upgrade, when the balance is
/// greater than the level's cost, and the mechanic's installation debits that cost, without a balance test.
/// </summary>
[TestClass]
public class UpgradeInstallCostTests
{
	private static (ParkEconomy Park, ParkObjectState Ride) Researched( int cash )
	{
		var park = EconomyTestData.Park( initialCash: cash );
		var ride = park.RegisterExisting( 1100 );
		park.Research.AddResearcherPoints( 1000000 );
		Assert.IsTrue( park.Research.IsAvailable( 1100, 1 ) );
		return (park, ride);
	}

	[TestMethod]
	public void TheRequestNeedsABalanceAboveTheCost()
	{
		var (equal, ride) = Researched( 400 );
		Assert.AreEqual( 400L, equal.Balance );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughMoney, equal.TryBuyUpgrade( ride.Id ), "0x004AF4EC: jle refuses a balance equal to the cost" );
		Assert.AreEqual( 0, ride.PendingLevel );
		var (above, other) = Researched( 401 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, above.TryBuyUpgrade( other.Id ) );
		Assert.AreEqual( 401L, above.Balance, "the request debits nothing" );
		Assert.AreEqual( 1, other.PendingLevel );
		Assert.IsTrue( other.UpgradeCostDue );
	}

	[TestMethod]
	public void TheInstallationDebitsTheCostWithoutABalanceTest()
	{
		var (park, ride) = Researched( 401 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var debits = new List<ParkEvent>();
		park.EventRaised += item => { if ( item.Kind == ParkEventKind.UpgradeCompleted ) debits.Add( item ); };
		for ( var day = 0; day < 20 && ride.Level == 0; day++ )
			park.AdvanceDays( 1 );
		Assert.AreEqual( 1, ride.Level );
		Assert.AreEqual( 1, debits.Count );
		Assert.AreEqual( 400L, ride.TotalSpent );
		Assert.IsFalse( ride.UpgradeCostDue );
		Assert.IsNotNull( mechanic );
	}

	[TestMethod]
	public void AnUpgradePaidAtPurchaseInAnOlderSaveIsNotChargedAgain()
	{
		var (park, ride) = Researched( 50000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		var json = ParkSaveFile.Serialize( park );
		Assert.IsTrue( json.Contains( "\"UpgradeCostDue\": true," ), "written while due" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.IsTrue( restored.TryGetObject( ride.Id, out var due ) && due.UpgradeCostDue, "a new save keeps the debit due" );

		var older = json.Replace( "\"UpgradeCostDue\": true,", "" );
		Assert.AreNotEqual( json, older );
		var paid = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( older ) ), park.Settings, park.Catalog );
		Assert.IsTrue( paid.TryGetObject( ride.Id, out var prepaid ) );
		Assert.AreEqual( 1, prepaid.PendingLevel );
		Assert.IsFalse( prepaid.UpgradeCostDue );
		EconomyTestData.HireBest( paid, StaffType.Mechanic );
		var spent = prepaid.TotalSpent;
		for ( var day = 0; day < 20 && prepaid.Level == 0; day++ )
			paid.AdvanceDays( 1 );
		Assert.AreEqual( 1, prepaid.Level );
		Assert.AreEqual( spent, prepaid.TotalSpent, "the older save paid at purchase" );
	}

	[TestMethod]
	public void ASaveWithACostDueButNoPendingUpgradeIsRejected()
	{
		var (park, ride) = Researched( 50000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		var json = ParkSaveFile.Serialize( park ).Replace( "\"PendingLevel\": 1", "\"PendingLevel\": 0" );
		Assert.IsTrue( json.Contains( "\"PendingLevel\": 0" ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ) );
	}
}
