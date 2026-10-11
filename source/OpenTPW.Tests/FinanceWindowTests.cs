using System.Collections.Generic;
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
}
