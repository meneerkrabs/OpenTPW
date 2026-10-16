using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>Finance graph values keep cash flow and balance categories distinct.</summary>
[TestClass]
public class FinanceWindowTests
{
	[TestMethod]
	public void GraphSeriesUseTheTrackedMonthValues()
	{
		var totals = new Dictionary<LedgerCategory, long>
		{
			[LedgerCategory.GateTakings] = 100,
			[LedgerCategory.ShopTakings] = 20,
			[LedgerCategory.SideshowTakings] = 30,
			[LedgerCategory.OtherIncome] = 10,
			[LedgerCategory.LoanReceived] = 500,
			[LedgerCategory.StaffCosts] = 40,
			[LedgerCategory.OtherCosts] = 50,
			[LedgerCategory.LoanPayments] = 60
		};
		var month = new LedgerMonth( 0, totals, 1000, 1510, 70, 2500 );
		Assert.AreEqual( 1510L, ParkHud.FinanceValue( month, FinanceSeries.BankBalance ) );
		Assert.AreEqual( 2500L, ParkHud.FinanceValue( month, FinanceSeries.ParkValue ) );
		Assert.AreEqual( 660L, ParkHud.FinanceValue( month, FinanceSeries.MoneyIn ), "money in includes the loan credit" );
		Assert.AreEqual( 150L, ParkHud.FinanceValue( month, FinanceSeries.MoneyOut ), "money out includes loan repayment" );
		Assert.AreEqual( 100L, ParkHud.FinanceValue( month, FinanceSeries.GateTakings ) );
		Assert.AreEqual( 20L, ParkHud.FinanceValue( month, FinanceSeries.ShopTakings ) );
		Assert.AreEqual( 30L, ParkHud.FinanceValue( month, FinanceSeries.SideshowTakings ) );
		Assert.AreEqual( 40L, ParkHud.FinanceValue( month, FinanceSeries.StaffCosts ) );
	}

	[TestMethod]
	public void TheSeriesToggleFollowsTheOriginalsSelectionMasks()
	{
		var visible = new HashSet<FinanceSeries> { FinanceSeries.BankBalance };
		ParkHud.ApplyFinanceToggle( visible, FinanceSeries.ParkValue );
		CollectionAssert.AreEquivalent( new[] { FinanceSeries.BankBalance, FinanceSeries.ParkValue }, visible.ToArray(), "bank balance and park value can show together (mask 0xfc clears only bits 2-7)" );
		ParkHud.ApplyFinanceToggle( visible, FinanceSeries.GateTakings );
		CollectionAssert.AreEquivalent( new[] { FinanceSeries.GateTakings }, visible.ToArray(), "a cash-flow series clears the pair (mask 0x03)" );
		ParkHud.ApplyFinanceToggle( visible, FinanceSeries.MoneyIn );
		ParkHud.ApplyFinanceToggle( visible, FinanceSeries.StaffCosts );
		Assert.AreEqual( 3, visible.Count, "the cash-flow series are independent of each other" );
		ParkHud.ApplyFinanceToggle( visible, FinanceSeries.MoneyIn );
		CollectionAssert.AreEquivalent( new[] { FinanceSeries.GateTakings, FinanceSeries.StaffCosts }, visible.ToArray(), "deselecting clears just that series" );
		ParkHud.ApplyFinanceToggle( visible, FinanceSeries.BankBalance );
		CollectionAssert.AreEquivalent( new[] { FinanceSeries.BankBalance }, visible.ToArray(), "the bank balance clears the cash-flow series" );
	}
}
