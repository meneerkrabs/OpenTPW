using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The loans window's summary rows (tp.exe 0x49fb30).</summary>
[TestClass]
public class LoansSummaryTests
{
	[TestMethod]
	public void ANewParkShowsZeroUntilAMonthCloses()
	{
		// the history rings are empty until the first month message, and the reader then shows 0
		var park = EconomyTestData.Park();
		Assert.AreEqual( (0L, 0L, 0L, 0L), ParkHud.LoanSummaryOf( park ) );
	}

	[TestMethod]
	public void TheSummaryUsesTheLastClosedMonthAndTheOutstandingInstalments()
	{
		var park = EconomyTestData.Park();
		var loan = park.TakeLoan( 0 );
		park.AdvanceMonths( 1 );
		var second = park.TakeLoan( 2 );
		var last = park.Ledger.History[^1];
		Assert.IsTrue( last.MoneyIn > 0 );
		var instalments = park.Loans.Sum( account => account.MonthlyRepayment );
		Assert.AreEqual( loan.MonthlyRepayment + second.MonthlyRepayment, instalments, "every outstanding loan, including one taken this month" );
		Assert.AreEqual( (last.MoneyIn, last.MoneyOut - instalments, instalments, last.MoneyIn - last.MoneyOut), ParkHud.LoanSummaryOf( park ) );
		// the current month's postings do not enter it
		var before = ParkHud.LoanSummaryOf( park );
		park.Ledger.Post( LedgerCategory.OtherIncome, 1234 );
		Assert.AreEqual( before, ParkHud.LoanSummaryOf( park ) );
	}
}
