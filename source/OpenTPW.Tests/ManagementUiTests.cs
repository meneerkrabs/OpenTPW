using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
public class ManagementUiTests
{
	private static ParkEconomy Economy( ParkGameMode mode = ParkGameMode.FullSimulation ) => new( EconomyTestData.Settings(), EconomyTestData.Catalog(), mode, 7 );

	[TestMethod]
	public void EntryPriceActionChangesActualAdmissionCharge()
	{
		var economy = Economy();
		Assert.IsTrue( ParkHud.TrySetEntryPrice( economy, false, 35 ) );
		economy.OpenPark();
		Assert.IsTrue( economy.TryAdmitVisitor( 100, out var paid ) );
		Assert.AreEqual( 35, paid );
		Assert.AreEqual( 35L, economy.Ledger.CurrentMonth( 0, 0 )[LedgerCategory.GateTakings] );
		Assert.IsFalse( ParkHud.TrySetEntryPrice( economy, true, 10 ) );
		Assert.IsFalse( ParkHud.TrySetEntryPrice( economy, false, -1 ) );
		Assert.AreEqual( 35, economy.EntranceFee );
	}

	[TestMethod]
	public void TrainingBudgetActionSetsActualMonthlyAllocationAndPreservesCash()
	{
		var economy = Economy();
		var cash = economy.Balance;
		Assert.IsTrue( ParkHud.TrySetStaffTrainingBudget( economy, false, StaffType.Mechanic, 120 ) );
		Assert.AreEqual( 120L, economy.Staff.GetTrainingBudget( StaffType.Mechanic ) );
		Assert.AreEqual( cash, economy.Balance, "allocation is spent by the existing monthly training model" );
		Assert.IsFalse( ParkHud.TrySetStaffTrainingBudget( economy, true, StaffType.Mechanic, 999 ) );
		Assert.IsFalse( ParkHud.TrySetStaffTrainingBudget( economy, false, StaffType.Mechanic, -1 ) );
		Assert.AreEqual( 120L, economy.Staff.GetTrainingBudget( StaffType.Mechanic ) );
	}

	[TestMethod]
	public void WorkloadActionClampsAndRespectsReadOnlyAndInstantAction()
	{
		var economy = Economy();
		Assert.IsTrue( ParkHud.TrySetResearchWorkload( economy, false, 300 ) );
		Assert.AreEqual( 100, economy.Research.Workload );
		Assert.IsFalse( ParkHud.TrySetResearchWorkload( economy, true, 5 ) );
		Assert.AreEqual( 100, economy.Research.Workload );
		Assert.IsTrue( ParkHud.TrySetResearchWorkload( economy, false, -20 ) );
		Assert.AreEqual( 0, economy.Research.Workload );
		Assert.IsFalse( ParkHud.TrySetResearchWorkload( Economy( ParkGameMode.InstantAction ), false, 5 ) );
	}

	[TestMethod]
	public void ResearchCursorActionChangesTheTargetAndPreservesReadOnlyAndModeGuards()
	{
		var economy = new ParkEconomy( EconomyTestData.Settings( extra: "ResearchTech[1].PercentageForThisTech 0\nResearchTech[2].PercentageForThisTech 0\n" ), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		Assert.IsTrue( economy.Research.SelectableItems( ResearchCategory.Ride ).Count > 1 );
		var initial = economy.Research.Current( ResearchCategory.Ride );
		Assert.IsTrue( ParkHud.TryMoveResearchCurrent( economy, false, ResearchCategory.Ride, 1 ) );
		var selected = economy.Research.Current( ResearchCategory.Ride );
		Assert.AreNotEqual( initial, selected );
		economy.Research.AddResearcherPoints( 10 );
		Assert.IsTrue( economy.Research.GetProgress( selected! ) > 0, "the selected target receives research points" );
		Assert.AreEqual( 0L, economy.Research.GetProgress( initial! ) );
		Assert.IsFalse( ParkHud.TryMoveResearchCurrent( economy, true, ResearchCategory.Ride, -1 ) );
		Assert.AreEqual( selected, economy.Research.Current( ResearchCategory.Ride ) );
		Assert.IsTrue( ParkHud.TryMoveResearchCurrent( economy, false, ResearchCategory.Ride, -1 ) );
		Assert.AreEqual( initial, economy.Research.Current( ResearchCategory.Ride ) );
		Assert.IsFalse( ParkHud.TryMoveResearchCurrent( Economy( ParkGameMode.InstantAction ), false, ResearchCategory.Ride, 1 ) );
	}
}
