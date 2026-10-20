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
		var level = CommandTestLevel.Create( economy );
		var visit = CommandTestLevel.Create( economy, readOnly: true );
		Assert.IsTrue( ParkHud.TrySetEntryPrice( level, 35 ) );
		economy.OpenPark();
		Assert.IsTrue( economy.TryAdmitVisitor( out var paid ) );
		Assert.AreEqual( 35, paid );
		Assert.AreEqual( 35L, economy.Ledger.CurrentMonth( 0, 0 )[LedgerCategory.GateTakings] );
		Assert.IsFalse( ParkHud.TrySetEntryPrice( visit, 10 ) );
		Assert.IsFalse( ParkHud.TrySetEntryPrice( level, -1 ) );
		Assert.AreEqual( 35, economy.EntranceFee );
	}

	[TestMethod]
	public void TrainingBudgetActionSetsActualMonthlyAllocationAndPreservesCash()
	{
		var economy = Economy();
		var level = CommandTestLevel.Create( economy );
		var visit = CommandTestLevel.Create( economy, readOnly: true );
		var cash = economy.Balance;
		Assert.IsTrue( ParkHud.TrySetStaffTrainingBudget( level, StaffType.Mechanic, 120 ) );
		Assert.AreEqual( 120L, economy.Staff.GetTrainingBudget( StaffType.Mechanic ) );
		Assert.AreEqual( cash, economy.Balance, "allocation is spent by the existing monthly training model" );
		Assert.IsFalse( ParkHud.TrySetStaffTrainingBudget( visit, StaffType.Mechanic, 999 ) );
		Assert.IsFalse( ParkHud.TrySetStaffTrainingBudget( level, StaffType.Mechanic, -1 ) );
		Assert.AreEqual( 120L, economy.Staff.GetTrainingBudget( StaffType.Mechanic ) );
	}

	[TestMethod]
	public void WorkloadActionClampsAndRespectsReadOnlyAndInstantAction()
	{
		var economy = Economy();
		var level = CommandTestLevel.Create( economy );
		var visit = CommandTestLevel.Create( economy, readOnly: true );
		Assert.IsTrue( ParkHud.TrySetResearchWorkload( level, 300 ) );
		Assert.AreEqual( 100, economy.Research.Workload );
		Assert.IsFalse( ParkHud.TrySetResearchWorkload( visit, 5 ) );
		Assert.AreEqual( 100, economy.Research.Workload );
		Assert.IsTrue( ParkHud.TrySetResearchWorkload( level, -20 ) );
		Assert.AreEqual( 0, economy.Research.Workload );
		Assert.IsFalse( ParkHud.TrySetResearchWorkload( CommandTestLevel.Create( Economy( ParkGameMode.InstantAction ) ), 5 ) );
	}

	[TestMethod]
	public void ResearchCursorActionChangesTheTargetAndPreservesReadOnlyAndModeGuards()
	{
		var economy = new ParkEconomy( EconomyTestData.Settings( extra: "ResearchTech[1].PercentageForThisTech 0\nResearchTech[2].PercentageForThisTech 0\n" ), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		Assert.IsTrue( economy.Research.SelectableItems( ResearchCategory.Ride ).Count > 1 );
		var level = CommandTestLevel.Create( economy );
		var visit = CommandTestLevel.Create( economy, readOnly: true );
		var initial = economy.Research.Current( ResearchCategory.Ride );
		Assert.IsTrue( ParkHud.TryMoveResearchCurrent( level, ResearchCategory.Ride, 1 ) );
		var selected = economy.Research.Current( ResearchCategory.Ride );
		Assert.AreNotEqual( initial, selected );
		economy.Research.AddResearcherPoints( 10 );
		Assert.IsTrue( economy.Research.GetProgress( selected! ) > 0, "the selected target receives research points" );
		Assert.AreEqual( 0L, economy.Research.GetProgress( initial! ) );
		Assert.IsFalse( ParkHud.TryMoveResearchCurrent( visit, ResearchCategory.Ride, -1 ) );
		Assert.AreEqual( selected, economy.Research.Current( ResearchCategory.Ride ) );
		Assert.IsTrue( ParkHud.TryMoveResearchCurrent( level, ResearchCategory.Ride, -1 ) );
		Assert.AreEqual( initial, economy.Research.Current( ResearchCategory.Ride ) );
		Assert.IsFalse( ParkHud.TryMoveResearchCurrent( CommandTestLevel.Create( Economy( ParkGameMode.InstantAction ) ), ResearchCategory.Ride, 1 ) );
	}
}
