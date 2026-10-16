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
		long OtherCosts() => park.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherCosts );
		var (balance, costs) = (park.Balance, OtherCosts());
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		Assert.AreEqual( (balance, costs), (park.Balance, OtherCosts()), "the request (0x4af480) debits nothing" );
		// drain the balance below the cost after the request: the installation does not test it
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TrySpendCoasterTrack( park.Balance - 100, force: true ) );
		Assert.AreEqual( 100L, park.Balance );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		long? delta = null, costDelta = null;
		for ( var turn = 0; turn < 2000 && ride.Level == 0; turn++ )
		{
			(balance, costs) = (park.Balance, OtherCosts());
			EconomyTestData.AdvanceToTurn( park, park.Turn + 1 );
			if ( ride.Level == 1 )
				(delta, costDelta) = (balance - park.Balance, OtherCosts() - costs);
		}
		Assert.AreEqual( 1, ride.Level, "installed with a balance below the cost" );
		Assert.AreEqual( 400L, delta, "the installation debits exactly Upgrades[1].CostOfUpgrade (0x4df8f0 → 0x4d01f0)" );
		Assert.AreEqual( 400L, costDelta );
		Assert.IsTrue( park.Balance < 0, $"the balance goes negative ({park.Balance})" );
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
