using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ParkResumeLoanContinuationTests
{
	[TestMethod]
	public void WholeEnvelopePreservesPartlyPaidLoansThroughInstalmentsAndPayoff()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		var world = ParkSaveFile.ToWorldState( snapshot.Economy );
		var economy = EconomyTestData.Park();
		var instance = economy.RegisterExisting( 1100 );
		Assert.AreEqual( snapshot.EconomyBindings.Single().InstanceId, instance.Id );
		economy.TakeLoan( 2 );
		economy.TakeLoan( 0 );
		economy.AdvanceMonths( 5 );
		snapshot = snapshot with { Economy = ParkSaveFile.Capture( economy, world ) };

		// Go through the strict world codec, including its embedded economy and association checks.
		var bytes = ParkResumeSaveFile.Serialize( snapshot );
		var decoded = ParkResumeSaveFile.Deserialize( bytes );
		ParkResumeSaveFile.ValidateShape( decoded );
		CollectionAssert.AreEqual( bytes, ParkResumeSaveFile.Serialize( decoded ) );
		var restored = ParkSaveFile.Restore( decoded.Economy, economy.Settings, economy.Catalog );
		CollectionAssert.AreEqual( economy.Loans.ToArray(), restored.Loans.ToArray() );
		Assert.IsTrue( restored.Loans.All( loan => loan.MonthsRepaid == 5 ) );
		Assert.IsFalse( restored.AvailableLoans.Any( offer => offer.Index is 0 or 2 ) );
		Assert.AreEqual( 145245L, restored.Balance );
		Assert.AreEqual( -5115L, restored.Ledger.ProfitThisYear );

		void SameEnvelope( string stage ) => CollectionAssert.AreEqual(
			ParkResumeSaveFile.Serialize( snapshot with { Economy = ParkSaveFile.Capture( economy, world ) } ),
			ParkResumeSaveFile.Serialize( decoded with { Economy = ParkSaveFile.Capture( restored, world ) } ), stage );

		SameEnvelope( "immediately after restoration" );
		for ( var month = 6; month <= 13; month++ )
		{
			economy.AdvanceMonths( 1 );
			restored.AdvanceMonths( 1 );
			// The bought instalments are 3651 and 900; their yearly interest shares are 873 and 150.
			Assert.AreEqual( 168000L - 4551L * month, restored.Balance, $"month {month}'s cash" );
			Assert.AreEqual( 4551L, restored.Ledger.History[^1][LedgerCategory.LoanPayments], $"month {month}'s instalments" );
			Assert.AreEqual( -1023L * (month < 12 ? month : month - 12), restored.Ledger.ProfitThisYear,
				$"month {month}'s yearly profit, including the January reset" );
			Assert.AreEqual( 2, restored.Loans.Count );
			Assert.IsTrue( restored.Loans.All( loan => loan.MonthsRepaid == month ) );
			SameEnvelope( $"after month {month}" );
		}

		Assert.IsTrue( economy.TryRepayLoan( 0 ) );
		Assert.IsTrue( restored.TryRepayLoan( 0 ) );
		Assert.AreEqual( 24864L, restored.Balance, "early payoff withdraws 23 remaining instalments of 3651" );
		Assert.AreEqual( -32451L, restored.Ledger.ProfitThisYear, "early payoff charges the whole-term interest share of 31428" );
		Assert.AreEqual( 2, restored.Loans.Single().OfferIndex );
		Assert.IsTrue( restored.AvailableLoans.Any( offer => offer.Index == 0 ) );
		SameEnvelope( "after early payoff" );

		for ( var month = 14; month <= 24; month++ )
		{
			economy.AdvanceMonths( 1 );
			restored.AdvanceMonths( 1 );
			Assert.AreEqual( 24864L - 900L * (month - 13), restored.Balance, $"month {month}'s cash" );
			Assert.AreEqual( 900L + (month == 14 ? 83973L : 0), restored.Ledger.History[^1][LedgerCategory.LoanPayments],
				"the payoff and subsequent instalments reach the monthly ledger exactly once" );
			Assert.AreEqual( month == 24 ? 0L : -32451L - 150L * (month - 13), restored.Ledger.ProfitThisYear );
			Assert.AreEqual( month == 24 ? 0 : 1, restored.Loans.Count, "the last instalment removes the remaining loan" );
			SameEnvelope( $"after month {month}" );
		}

		Assert.IsTrue( restored.AvailableLoans.Any( offer => offer.Index == 2 ) );
		Assert.AreEqual( 153036L, restored.Ledger.History.Sum( month => month[LedgerCategory.LoanPayments] ),
			"both loans repay their exact whole-term instalment totals" );
		economy.AdvanceMonths( 1 );
		restored.AdvanceMonths( 1 );
		Assert.AreEqual( 14964L, restored.Balance );
		Assert.AreEqual( 0L, restored.Ledger.History[^1][LedgerCategory.LoanPayments], "a paid-off loan makes no further deduction" );
		SameEnvelope( "one month after both loans are repaid" );
	}
}
