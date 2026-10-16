using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The bank's withdrawals flag, balance after the last debit and turn-entered-red stamp (tp.exe <c>0x004D01F0</c> debit,
/// <c>0x004D0190</c> credit, <c>0x004D0370</c> month, <c>0x004D0850</c> payoff).
/// </summary>
[TestClass]
public class BankWordsTests
{
	private static long Turn( ParkEconomy park ) => ParkCalendar.Turn( park.Tick );

	[TestMethod]
	public void OnlyTheDebitThatLeavesTheBlackStampsTheTurn()
	{
		var park = EconomyTestData.Park( initialCash: 1000 );
		park.Advance( ParkCalendar.TicksForHours( 30 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TrySpendCoasterTrack( 1500, force: true ) );
		var stamp = Turn( park );
		Assert.AreEqual( (-500L, stamp), (park.LastBalance, park.TurnEnteredRed) );

		park.Advance( ParkCalendar.TicksForHours( 30 ) );
		park.CreditCoasterTrack( 2000 );
		Assert.AreEqual( (1500L, -500L, stamp), (park.Balance, park.LastBalance, park.TurnEnteredRed), "a credit leaves both words" );
		Assert.AreEqual( 0, park.MonthsSinceEnteringRed(), "in the black" );

		park.Advance( ParkCalendar.TicksForHours( 30 ) );
		park.TrySpendCoasterTrack( 2000, force: true );
		Assert.AreEqual( (-500L, stamp), (park.LastBalance, park.TurnEnteredRed), "the last balance was already below 0: no new stamp" );

		park.CreditCoasterTrack( 1000 );
		park.TrySpendCoasterTrack( 100, force: true );
		Assert.AreEqual( 400L, park.LastBalance );
		park.Advance( ParkCalendar.TicksForHours( 30 ) );
		park.TrySpendCoasterTrack( 1000, force: true );
		Assert.AreEqual( (-600L, Turn( park )), (park.LastBalance, park.TurnEnteredRed), "from 0 or more into the red stamps again" );
	}

	[TestMethod]
	public void TheMonthsCountTheParkTimeSinceTheStampIn30DayMonths()
	{
		var park = EconomyTestData.Park( initialCash: 1000 );
		park.TrySpendCoasterTrack( 2000, force: true );
		var stamp = Turn( park );
		var turnsPerMonth = 30 * ParkCalendar.SecondsPerDay / (double)ParkCalendar.SecondsPerTurn;
		Assert.AreEqual( 691.2, turnsPerMonth, 1e-9, "15000 / 4 seconds a turn" );
		park.Advance( ParkCalendar.TickOfTurn( stamp + 691 ) - park.Tick );
		Assert.AreEqual( 0, park.MonthsSinceEnteringRed() );
		park.Advance( ParkCalendar.TickOfTurn( stamp + 692 ) - park.Tick );
		Assert.AreEqual( 1, park.MonthsSinceEnteringRed() );
		park.RestoreBankWords( true, 100, stamp );
		Assert.AreEqual( 0, park.MonthsSinceEnteringRed(), "the last balance must be below 0 too" );
	}

	[TestMethod]
	public void ASavedBatchCreditIsPaidIntoTheBalanceAndTheProfitAtTheNextMonthEndAndThenZeroed()
	{
		// 0x4D0370: the month adds the batch word to the balance, the money in and the year's profit, then zeroes it.
		var park = EconomyTestData.Park( initialCash: 5000 );
		var (balance, profit) = (park.Balance, park.Ledger.ProfitThisYear);
		park.RestoreBankWords( true, balance, 0, 750 );
		Assert.AreEqual( 750, park.BatchCredit );
		park.AdvanceMonths( 1 );
		Assert.AreEqual( 0, park.BatchCredit );
		Assert.AreEqual( balance + 750, park.Balance, "no staff or loans: the month changes nothing else" );
		Assert.AreEqual( profit + 750, park.Ledger.ProfitThisYear );
		Assert.AreEqual( 750, park.Ledger.History.Last().MoneyIn );
		park.AdvanceMonths( 1 );
		Assert.AreEqual( balance + 750, park.Balance, "paid once" );
		Assert.ThrowsException<System.ArgumentOutOfRangeException>( () => park.RestoreBankWords( true, balance, 0, -1 ) );
	}

	[TestMethod]
	public void ThePendingBatchCreditSurvivesAParkSave()
	{
		var park = EconomyTestData.Park( initialCash: 5000 );
		park.RestoreBankWords( true, park.Balance, 0, 321 );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );
		Assert.AreEqual( 321, restored.BatchCredit );
		Assert.IsFalse( ParkSaveFile.Serialize( EconomyTestData.Park() ).Contains( "BatchCredit" ), "absent while 0, so existing save text is unchanged" );
	}

	[TestMethod]
	public void WithWithdrawalsOffADebitChargesNothingAndAnInstalmentRaisesTheProfit()
	{
		var park = EconomyTestData.Park( initialCash: 300000 );
		var loan = park.TakeLoan( 0 );
		park.RestoreBankWords( false, park.Balance, 0 );
		var (balance, profit) = (park.Balance, park.Ledger.ProfitThisYear);
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TrySpendCoasterTrack( 5000, force: true ) );
		Assert.AreEqual( (balance, profit, balance), (park.Balance, park.Ledger.ProfitThisYear, park.LastBalance), "0x4d01f0 does nothing with +0x114 clear" );

		park.AdvanceMonths( 1 );
		Assert.AreEqual( 1, park.Loans.Single().MonthsRepaid, "the instalment still counts" );
		Assert.AreEqual( balance, park.Balance, "and is not deducted" );
		var interest = LoanMath.InstalmentInterest( loan.MonthlyRepayment, loan.Months, loan.OriginalAmount );
		Assert.AreEqual( profit + loan.MonthlyRepayment - interest, park.Ledger.ProfitThisYear, "the principal share is added back without the debit" );

		profit = park.Ledger.ProfitThisYear;
		var remaining = park.Loans.Single().RemainingBalance;
		Assert.IsTrue( park.TryRepayLoan( 0 ) );
		Assert.AreEqual( balance, park.Balance );
		Assert.AreEqual( profit + remaining - LoanMath.PayoffInterest( loan.MonthlyRepayment, loan.Months, loan.OriginalAmount ), park.Ledger.ProfitThisYear );
	}

	[TestMethod]
	public void APayoffComparesTheBalanceAsAnUnsignedWord()
	{
		var park = EconomyTestData.Park( initialCash: 1000 );
		park.TakeLoan( 0 );
		var remaining = park.Loans.Single().RemainingBalance;
		park.CreditCoasterTrack( remaining - 1 - park.Balance );
		Assert.IsFalse( park.TryRepayLoan( 0 ), "one short" );
		park.TrySpendCoasterTrack( park.Balance + 10, force: true );
		Assert.AreEqual( -10L, park.Balance );
		Assert.IsTrue( park.TryRepayLoan( 0 ), "a balance below 0 is a large unsigned word" );
		Assert.AreEqual( -10L - remaining, park.Balance );
	}

	[TestMethod]
	public void TheBankWordsAreSavedAndOlderSavesCountBackFromTheirMonthsInTheRed()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.AdvanceMonths( 5 );
		Assert.IsTrue( park.MonthsInRed >= 2, park.MonthsInRed.ToString() );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Capture( park ), park.Settings, park.Catalog );
		Assert.AreEqual( (park.WithdrawalsEnabled, park.LastBalance, park.TurnEnteredRed, park.MonthsInRed),
			(restored.WithdrawalsEnabled, restored.LastBalance, restored.TurnEnteredRed, restored.MonthsInRed) );

		var json = JsonNode.Parse( ParkSaveFile.Serialize( park ) )!.AsObject();
		json.Remove( "WithdrawalsEnabled" );
		json.Remove( "LastBalance" );
		json.Remove( "TurnEnteredRed" );
		var legacy = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ), park.Settings, park.Catalog );
		Assert.AreEqual( (true, park.Balance), (legacy.WithdrawalsEnabled, legacy.LastBalance) );
		Assert.AreEqual( park.MonthsInRed, legacy.MonthsSinceEnteringRed(), "the stamp is counted back by the saved months" );

		json["TurnEnteredRed"] = Turn( park ) + 1;
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
	}
}
