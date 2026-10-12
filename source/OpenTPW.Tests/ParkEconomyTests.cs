using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenTPW.Tests;

[TestClass]
public class ParkEconomyTests
{
	[TestMethod]
	public void SamDocumentKeepsValueListsQuotesAndSkipsCommentsAndShapes()
	{
		var document = SamDocument.Parse(
			"#comment\r\nInfo.Id\t\t1100\r\nInfo.Name\t\"Belly Bounce\"\r\nInfo.Shape\r\n---\r\n*S*\r\n---\r\n"
			+ "PeepTypes[0].PreferredExcitement.StartingCash.BoredomThreshold\t80\t 300\t40\n"
			+ "Upgrades[0].CostOfUpgrade\t\t1000\tcash cost when buying this item\n"
			+ "PerGradeStaffConsts[0].BaseWage\t4\t\t\t// Grade 0\n"
			+ "ResearchCategories[0].Effort\t100\t\t#ride\n"
			+ "RidesBuildRides.Score -50 This is negative\n", "test.sam" );
		var settings = new SamSettings( new[] { document, SamDocument.Parse( "Upgrades[0].CostOfUpgrade 750\n", "Easy_test.sam" ) } );
		Assert.AreEqual( 1100, settings.GetInt( "Info.Id" ) );
		Assert.AreEqual( "Belly Bounce", settings.GetString( "info.name" ) );
		CollectionAssert.AreEqual( new[] { "80", "300", "40" }, settings.Get( "PeepTypes[0].PreferredExcitement.StartingCash.BoredomThreshold" ).Values.ToArray() );
		Assert.AreEqual( 750, settings.GetInt( "Upgrades[0].CostOfUpgrade" ) );
		Assert.AreEqual( "Easy_test.sam", settings.Get( "Upgrades[0].CostOfUpgrade" ).Source );
		Assert.AreEqual( 4, settings.GetInt( "PerGradeStaffConsts[0].BaseWage" ) );
		Assert.AreEqual( 100, settings.GetInt( "ResearchCategories[0].Effort" ) );
		Assert.AreEqual( -50, settings.GetInt( "RidesBuildRides.Score" ) );
		Assert.IsFalse( settings.Contains( "Info.Shape" ) );
		Assert.IsFalse( document.Entries.Any( entry => entry.Key.Contains( '*' ) ) );
		CollectionAssert.AreEqual( new[] { 0 }, settings.GetIndices( "Upgrades", "CostOfUpgrade" ).ToArray() );
		Assert.ThrowsException<KeyNotFoundException>( () => settings.GetInt( "BankAccountInfo.InitialCash" ) );
	}

	[TestMethod]
	public void BalanceSettingsReadEveryEconomyGroup()
	{
		var settings = EconomyTestData.Settings();
		Assert.AreEqual( 50000, settings.InitialCash );
		Assert.AreEqual( 20, settings.InitialAdmissionFee );
		Assert.AreEqual( 3, settings.Loans.Count );
		Assert.AreEqual( new LoanOffer( 2, 18000, 20, 24, 2 ), settings.Loans[2] );
		Assert.AreEqual( 6 * 30, settings.GetMonthlyWage( StaffType.Mechanic, 2 ) );
		Assert.AreEqual( 12 * 35, settings.GetMonthlyWage( StaffType.Researcher, 4 ) );
		Assert.AreEqual( 80, settings[StaffType.Mechanic].WorkDuration[0] );
		Assert.AreEqual( 10, settings[StaffType.Guard].MaximumInPark );
		CollectionAssert.AreEqual( new[] { 1, 7, 8 }, settings.ChallengesInThisLevel.ToArray() );
		Assert.AreEqual( 30000, settings.Challenges[8].Prize );
		Assert.IsFalse( settings.Challenges[8].Independent );
		Assert.AreEqual( 540, settings.ChallengeTiming.DaysUntilFirstChallenge );
		Assert.AreEqual( 1, settings.KeysToEnter );
		Assert.AreEqual( 2.0, settings.EntryFee.ExpensivePriceMultiplier );
		Assert.ThrowsException<InvalidDataException>( () => new BalanceSettings(
			new SamSettings( new[] { SamDocument.Parse( EconomyTestData.StandardText( extra: "ChallengesInThisLevel[3].ChallengeType 99\n" ), "Standard.sam" ) } ),
			new SamSettings( new[] { SamDocument.Parse( EconomyTestData.ChallengesText, "Challenges.sam" ) } ), null, "jungle", false ) );
	}

	[TestMethod]
	public void CalendarAndSpeedAdvanceInWholeTicks()
	{
		Assert.AreEqual( new ParkDate( 1, 1, 1, 0 ), ParkCalendar.ToDate( 0 ) );
		Assert.AreEqual( 15, ParkCalendar.TickOfTurn( 1 ), "a 248 ms turn is 14.88 ticks" );
		Assert.AreEqual( 3750, ParkCalendar.Seconds( ParkCalendar.TickOfTurn( 1 ) ), "one turn adds 15000 / 4 seconds" );
		Assert.AreEqual( 0, ParkCalendar.DayIndex( ParkCalendar.TickOfTurn( 23 ) ) );
		Assert.AreEqual( 1, ParkCalendar.DayIndex( ParkCalendar.TickOfTurn( 24 ) ), "a day is 23.04 turns" );
		Assert.AreEqual( new ParkDate( 1, 2, 1, 0 ), ParkCalendar.ToDate( ParkCalendar.TickAtDay( 31 ) ) );
		Assert.AreEqual( 60, ParkCalendar.DayIndex( ParkCalendar.TickAtMonth( 2 ) ), "February 2000 has 29 days" );
		Assert.AreEqual( new ParkDate( 1, 12, 31, 23 ), ParkCalendar.ToDate( ParkCalendar.TickAtDay( 366 ) - 1 ) );
		Assert.AreEqual( new ParkDate( 2, 1, 1, 0 ), ParkCalendar.ToDate( ParkCalendar.TickAtDay( 366 ) ) );
		Assert.AreEqual( 12, ParkCalendar.MonthIndex( ParkCalendar.TickAtDay( 366 ) ) );
		var park = EconomyTestData.Park();
		park.Speed = GameSpeed.Paused;
		park.AdvanceFixedTick();
		Assert.AreEqual( 0, park.Tick );
		park.Speed = GameSpeed.Fastest;
		park.AdvanceFixedTick();
		Assert.AreEqual( 4, park.Tick );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => park.Advance( -1 ) );
	}

	[TestMethod]
	public void LedgerPostsCategoriesAndClosesBoundedMonths()
	{
		var ledger = new ParkLedger( 1000 );
		ledger.Post( LedgerCategory.GateTakings, 200 );
		ledger.Post( LedgerCategory.ShopTakings, 50 );
		ledger.Post( LedgerCategory.StaffCosts, 300 );
		ledger.Post( LedgerCategory.OtherCosts, 25 );
		ledger.Post( LedgerCategory.LoanReceived, 10000 );
		ledger.Post( LedgerCategory.LoanPayments, 400 );
		Assert.AreEqual( 1000 + 200 + 50 - 300 - 25 + 10000 - 400, ledger.Balance );
		var month = ledger.CloseMonth( 1, 40, 1234 );
		Assert.AreEqual( 10250, month.MoneyIn, "loans received are money in" );
		Assert.AreEqual( 725, month.MoneyOut );
		Assert.AreEqual( -475, month.Profit );
		Assert.AreEqual( 1000, month.OpeningBalance );
		Assert.AreEqual( ledger.Balance, month.ClosingBalance );
		Assert.AreEqual( 0, ledger.CurrentTotals.Count );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => ledger.Post( LedgerCategory.OtherCosts, -1 ) );
		for ( var index = 2; index < 200; index++ )
		{
			ledger.Post( LedgerCategory.GateTakings, index );
			ledger.CloseMonth( index, 0, 0 );
		}
		Assert.AreEqual( ParkLedger.MaximumHistoryMonths, ledger.History.Count );
		Assert.AreEqual( 199 - ParkLedger.MaximumHistoryMonths, ledger.History[0].MonthIndex );
		var year = ledger.SummariseYear( 15 );
		Assert.IsNotNull( year );
		Assert.AreEqual( Enumerable.Range( 181, 12 ).Sum(), year!.MoneyIn );
	}

	[TestMethod]
	public void YearlyProfitRunsFromIncomeAndCostsAndResetsEachYear()
	{
		var park = EconomyTestData.Park( initialCash: 0 );
		park.OpenPark();
		park.TryAdmitVisitor( 100, out _ );
		var entry = park.Ledger.ProfitThisYear;
		Assert.IsTrue( entry > 0, "gate takings raise the year's profit" );
		var account = park.TakeLoan( 2 );
		Assert.AreEqual( entry, park.Ledger.ProfitThisYear, "a loan received is not profit" );
		park.AdvanceMonths( 1 );
		var interest = LoanMath.InstalmentInterest( account.MonthlyRepayment, account.Months, account.OriginalAmount );
		Assert.AreEqual( entry - interest, park.Ledger.ProfitThisYear, "an instalment costs only its interest share" );
		park.AdvanceMonths( 11 );
		Assert.AreEqual( 1, park.Date.Month );
		Assert.AreEqual( 0, park.Ledger.ProfitThisYear, "the month event (instalment) comes before the year event (reset)" );
		park.AdvanceMonths( 1 );
		Assert.AreEqual( -interest, park.Ledger.ProfitThisYear );
		Assert.AreEqual( 4294967268L / 36, LoanMath.InstalmentInterest( 2777, 36, 100000 ), "(M x N - P) wraps in 32 bits when M x N < P" );
	}

	[TestMethod]
	public void LoanRepaymentsFollowTheOriginalInstalment()
	{
		// ORACLE F12: M = trunc(P x pow(1 + APR/100, (N/12) x 0.5) / N); the five original PC saves store these eight values
		CollectionAssert.AreEqual( new long[] { 3651, 1825, 912, 365, 922, 1282, 2320, 2749 },
			new[] { (100000, 20, 36), (50000, 20, 36), (25000, 20, 36), (10000, 20, 36), (18000, 23, 24), (30000, 22, 30), (80000, 18, 48), (65000, 21, 30) }
				.Select( offer => LoanMath.MonthlyRepayment( offer.Item1, offer.Item2, offer.Item3 ) ).ToArray() );
		Assert.AreEqual( 2777, LoanMath.MonthlyRepayment( 100000, 0, 36 ) );
		Assert.AreEqual( 750, LoanMath.MonthlyRepayment( 18000, 0, 24 ) );
		Assert.AreEqual( 99972, LoanMath.TotalPayable( 100000, 0, 36 ), "at 0 % APR the original repays M x N, short of P by the division remainder" );
		Assert.AreEqual( 3651, LoanMath.MonthlyRepayment( 100000, 20, 36 ) );
		Assert.AreEqual( 131436, LoanMath.TotalPayable( 100000, 20, 36 ), "the total payable is M x N" );
		Assert.AreEqual( 4294967295L, LoanMath.MonthlyRepayment( long.MaxValue / 2, 20, 1 ), "the float to unsigned conversion saturates" );

		var park = EconomyTestData.Park( initialCash: 0 );
		var account = park.TakeLoan( 2 );
		Assert.AreEqual( 18000, park.Balance );
		Assert.AreEqual( LoanMath.MonthlyRepayment( 18000, 20, 24 ), account.MonthlyRepayment );
		Assert.IsFalse( park.AvailableLoans.Any( offer => offer.Index == 2 ) );
		Assert.ThrowsException<InvalidOperationException>( () => park.TakeLoan( 2 ) );
		park.AdvanceMonths( 1 );
		Assert.AreEqual( 18000 - account.MonthlyRepayment, park.Balance, "exactly M leaves the bank, with no interest on a balance" );
		Assert.AreEqual( 23, park.Loans.Single().MonthsRemaining );
		park.AdvanceMonths( 23 );
		Assert.AreEqual( 0, park.Loans.Count );
		Assert.AreEqual( 18000 - LoanMath.TotalPayable( 18000, 20, 24 ), park.Balance );
		Assert.AreEqual( LoanMath.TotalPayable( 18000, 20, 24 ), park.Ledger.History.Sum( month => month[LedgerCategory.LoanPayments] ) );
		Assert.AreEqual( 18000, park.Ledger.History.Sum( month => month.MoneyIn ), "the loan is money in" );
		Assert.IsTrue( park.AvailableLoans.Any( offer => offer.Index == 2 ) );

		var early = EconomyTestData.Park( initialCash: 30000 );
		early.TakeLoan( 1 );
		early.AdvanceMonths( 12 );
		var loan = early.Loans[0];
		Assert.AreEqual( 12, loan.MonthsRepaid );
		var remaining = loan.MonthlyRepayment * (loan.Months - 12);
		Assert.AreEqual( remaining, loan.RemainingBalance, "early repayment is M x the months left (0xccc78)" );
		var before = early.Balance;
		Assert.IsTrue( early.TryRepayLoan( 1 ) );
		Assert.AreEqual( before - remaining, early.Balance );
		Assert.AreEqual( 0, early.Loans.Count );
		var instant = EconomyTestData.Park( mode: ParkGameMode.InstantAction );
		Assert.AreEqual( 0, instant.AvailableLoans.Count() );
	}

	[TestMethod]
	public void EarlyPayoffChargesTheWholeTermShareToTheYearProfit()
	{
		// 0xCCC78: months repaid are cleared before they are read again, so a payoff after 12 of 36 months still lowers the
		// year profit by q x N = 873 x 36 = 31,428 (q = (3651 x 36 - 100,000) / 36), beside the 87,624 that leaves the balance.
		var park = EconomyTestData.Park( initialCash: 300000, apr: 20 );
		var loan = park.TakeLoan( 0 );
		Assert.AreEqual( 3651, loan.MonthlyRepayment );
		park.AdvanceMonths( 12 );
		var (balance, profit) = (park.Balance, park.Ledger.ProfitThisYear);
		Assert.IsTrue( park.TryRepayLoan( 0 ) );
		Assert.AreEqual( balance - 87624, park.Balance );
		Assert.AreEqual( profit - 31428, park.Ledger.ProfitThisYear );
		Assert.AreEqual( 31428, LoanMath.PayoffInterest( 3651, 36, 100000 ) );
	}

	[TestMethod]
	public void TheYearProfitWrapsAsASigned32BitWordOnEveryUpdate()
	{
		// Two 0 % jungle-easy loans take 119,304,646 a month each off the profit (the unsigned share of M x N < P). After nine
		// payments the word sits just above -2^31, and an ordinary $21 cost wraps it to +2,147,483,647 as the original's stw does.
		var ledger = new ParkLedger( 0 );
		for ( var month = 0; month < 9; month++ )
			ledger.ChargeLoanInterest( 238_609_292 );
		Assert.AreEqual( -2_147_483_628L, ledger.ProfitThisYear );
		ledger.Post( LedgerCategory.OtherCosts, 21 );
		Assert.AreEqual( 2_147_483_647L, ledger.ProfitThisYear );
		ledger.Post( LedgerCategory.GateTakings, 1 );
		Assert.AreEqual( -2_147_483_648L, ledger.ProfitThisYear, "income wraps the other way" );
	}

	[TestMethod]
	public void SeveralLoansArePaidInOfferOrder()
	{
		var park = EconomyTestData.Park( initialCash: 0 );
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		var second = park.TakeLoan( 2 );
		var first = park.TakeLoan( 0 );
		park.AdvanceMonths( 1 );
		CollectionAssert.AreEqual( new[] { 0, 2 }, events.Where( item => item.Kind == ParkEventKind.LoanPayment ).Select( item => item.InfoId ).ToArray() );
		Assert.AreEqual( first.MonthlyRepayment + second.MonthlyRepayment, park.Ledger.History[^1][LedgerCategory.LoanPayments] );
	}

	[TestMethod]
	public void WagesArePaidAtMonthEndAndTrainingRaisesGrades()
	{
		var park = EconomyTestData.Park();
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		var wages = park.Settings.GetMonthlyWage( StaffType.Mechanic, mechanic.Grade ) + park.Settings.GetMonthlyWage( StaffType.Handyman, handyman.Grade );
		park.Advance( ParkCalendar.TickAtMonth( 1 ) - 1 );
		Assert.AreEqual( 50000, park.Balance );
		park.Advance( 1 );
		Assert.AreEqual( 50000 - wages, park.Balance );
		Assert.AreEqual( wages, park.Ledger.History[0][LedgerCategory.StaffCosts] );
		park.Fire( handyman.Id );
		Assert.AreEqual( 1, park.Staff.Members.Count );

		var grade = mechanic.Grade;
		var price = park.Settings[StaffType.Mechanic].PoundsPerTrainingPoint[grade];
		park.SetTrainingBudget( StaffType.Mechanic, price * 60L );
		var balance = park.Balance;
		park.AdvanceMonths( 1 );
		Assert.AreEqual( 60, mechanic.TrainingPoints );
		Assert.AreEqual( balance - park.Settings.GetMonthlyWage( StaffType.Mechanic, grade ) - price * 60L, park.Balance );
		park.AdvanceMonths( 1 );
		Assert.AreEqual( grade + 1, mechanic.Grade );
		Assert.AreEqual( 20, mechanic.TrainingPoints, "60 + 60 points: the 20 above the grade boundary carry over" );
	}

	[TestMethod]
	public void StaffPoolHonoursParkMaximumAndCandidateTimeouts()
	{
		var park = EconomyTestData.Park();
		Assert.AreEqual( 15, park.Staff.Candidates.Count );
		var first = park.Staff.Candidates[0];
		Assert.IsTrue( first.ExpiresTick == ParkCalendar.SecondsToTicks( 120 ) );
		park.Advance( ParkCalendar.SecondsToTicks( 200 ) );
		Assert.IsFalse( park.Staff.Candidates.Any( candidate => candidate.Id == first.Id ) );
		foreach ( var type in Enum.GetValues<StaffType>() )
			Assert.IsTrue( park.Staff.Candidates.Count( candidate => candidate.Type == type ) >= 1 );
		for ( var hired = 0; hired < 10; hired++ )
		{
			while ( !park.Staff.Candidates.Any( candidate => candidate.Type == StaffType.Guard ) )
				park.Advance( ParkCalendar.TicksForHours( 1 ) );
			EconomyTestData.HireBest( park, StaffType.Guard );
		}
		Assert.IsFalse( park.Staff.CanHire( StaffType.Guard ) );
		park.Advance( ParkCalendar.SecondsToTicks( 400 ) );
		Assert.IsFalse( park.Staff.Candidates.Any( candidate => candidate.Type == StaffType.Guard ), "no new guards once the park maximum is employed" );
	}

	[TestMethod]
	public void StaffSkillIsTwentyTimesGradePlusTrainingPercentage()
	{
		StaffMember Member( int grade, int points ) => new() { Id = 1, Type = StaffType.Handyman, NameIndex = 0, Grade = grade, TrainingPoints = points };
		Assert.AreEqual( 0, ParkStaff.StaffSkill( Member( 0, 0 ) ) );
		Assert.AreEqual( 80, ParkStaff.StaffSkill( Member( 4, 0 ) ) );
		Assert.AreEqual( 27, ParkStaff.StaffSkill( Member( 1, 35 ) ) );
		Assert.AreEqual( 99, ParkStaff.StaffSkill( Member( 4, 99 ) ) );
	}

	[TestMethod]
	public void StaffPoolRefillFillsEachRolesShortfallUpToTheCap()
	{
		var park = EconomyTestData.Park();
		Assert.AreEqual( 15, park.Staff.Candidates.Count, "3 of each role at the start" );
		park.Advance( park.Staff.UpdateInterval + ParkCalendar.TicksForHours( 2 ) );
		Assert.AreEqual( 25, park.Staff.Candidates.Count, "the shortfall of 2 per role is 10, the per-update cap" );
		foreach ( var type in Enum.GetValues<StaffType>() )
			Assert.AreEqual( 5, park.Staff.Candidates.Count( candidate => candidate.Type == type ), $"{type} reaches Max {type}s" );
	}

	[TestMethod]
	public void SixMonthsInTheRedMakeTheParkBankrupt()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.OpenPark();
		park.AdvanceMonths( 1 );
		Assert.IsTrue( park.Balance < 0 );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.InTheRed ) );
		Assert.AreEqual( 1, park.MonthsInRed );
		park.AdvanceMonths( 4 );
		CollectionAssert.AreEqual( new long[] { 3, 5 }, events.Where( item => item.Kind == ParkEventKind.BankruptcyWarning ).Select( item => item.Amount ).ToArray() );
		Assert.IsFalse( park.IsBankrupt );
		park.AdvanceMonths( 1 );
		Assert.IsTrue( park.IsBankrupt );
		Assert.IsFalse( park.IsParkOpen );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.Bankrupt ) );
		var tick = park.Tick;
		park.AdvanceMonths( 1 );
		Assert.AreEqual( tick, park.Tick );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Bankrupt, park.TryBuild( 1100, out _ ) );

		var recovered = EconomyTestData.Park( initialCash: 100 );
		EconomyTestData.HireBest( recovered, StaffType.Researcher );
		recovered.AdvanceMonths( 2 );
		recovered.TakeLoan( 0 );
		recovered.AdvanceMonths( 1 );
		Assert.AreEqual( 0, recovered.MonthsInRed );
	}

	[TestMethod]
	public void GuestsPayAtTheGateShopsAndSideshows()
	{
		var park = EconomyTestData.Park();
		Assert.IsFalse( park.TryAdmitVisitor( 100, out _ ) );
		park.OpenPark();
		Assert.IsTrue( park.TryAdmitVisitor( 100, out var fee ) );
		Assert.AreEqual( 20, fee );
		Assert.IsFalse( park.TryAdmitVisitor( 19, out _ ) );
		park.SetEntranceFee( 35 );
		Assert.IsTrue( park.TryAdmitVisitor( 35, out fee ) );
		Assert.AreEqual( 55, park.Ledger.CurrentTotals[LedgerCategory.GateTakings] );
		Assert.AreEqual( 2, park.Counters[ParkCounters.Admissions] );

		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1203, out var shop ) );
		Assert.AreEqual( 50000 + 55 - 650, park.Balance );
		Assert.IsTrue( park.TryBuy( shop!.Id, 100, out var price ) );
		Assert.AreEqual( 30, price );
		Assert.AreEqual( 30, park.Ledger.CurrentTotals[LedgerCategory.ShopTakings] );
		Assert.AreEqual( 650 + 20, park.Ledger.CurrentTotals[LedgerCategory.OtherCosts] );
		Assert.AreEqual( 50, park.LitterScaled );
		Assert.AreEqual( 1, park.Counters[ParkCounters.Sold( 4, 3 )] );
		Assert.AreEqual( 10, park.Counters[ParkCounters.ShopProfit] );
		park.SetPrices( shop.Id, 45 );
		Assert.IsFalse( park.TryBuy( shop.Id, 40, out _ ) );

		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1303, out var sideshow ) );
		int Winners( ParkEconomy target, int sideshowId )
		{
			var winners = 0;
			for ( var play = 0; play < 200; play++ )
			{
				Assert.IsTrue( target.PlaySideshow( sideshowId, 100, out var paid, out var won ) );
				Assert.AreEqual( 20, paid );
				winners += won ? 1 : 0;
			}
			return winners;
		}
		var wins = Winners( park, sideshow!.Id );
		Assert.IsTrue( wins is > 30 and < 90, wins.ToString() );
		Assert.AreEqual( 200 * 20, park.Ledger.CurrentTotals[LedgerCategory.SideshowTakings] );
		Assert.AreEqual( 200 * 20 - wins * 50, park.Counters[ParkCounters.SideshowProfit] );
		var twin = EconomyTestData.Park();
		twin.OpenPark();
		twin.TryBuild( 1203, out _ );
		twin.TryBuild( 1303, out var twinSideshow );
		Assert.AreEqual( wins, Winners( twin, twinSideshow!.Id ), "same seed, same draws" );

		park.RecordRideUse( shop.Id );
		park.AdvanceMonths( 1 );
		Assert.AreEqual( 1 + 1, shop.CustomersLastMonth );
		Assert.AreEqual( 30, shop.TakingsLastMonth );
		Assert.AreEqual( 0, shop.CustomersThisMonth );
	}

	[TestMethod]
	public void BuildingNeedsResearchMoneyAndGoldenTickets()
	{
		var park = EconomyTestData.Park( initialCash: 1000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotResearched, park.TryBuild( 1180, out _ ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotBuyable, park.TryBuild( 1601, out _ ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.UnknownObject, park.TryBuild( 9999, out _ ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughGoldenTickets, park.TryBuild( 1112, out _ ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughMoney, park.TryBuild( 1303, out _ ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1100, out var ride ) );
		Assert.AreEqual( 500, park.Balance );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyCells( CellPurchase.Path, 10 ) );
		Assert.AreEqual( 300, park.Balance );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughMoney, park.TryBuyCells( CellPurchase.Land, 4 ) );
		Assert.AreEqual( 500, park.ScrapValue( ride! ), "nobody used it in its first 30 days: the whole base price" );
		park.RecordRideUse( ride!.Id ); park.RecordUseFinished( ride!.Id );
		Assert.AreEqual( 250, park.ScrapValue( ride ) );
		park.AdvanceDays( 365 );
		Assert.AreEqual( 150, park.ScrapValue( ride ) );
		var gates = park.RegisterExisting( 1601 );
		Assert.IsTrue( gates.Imported );
		var balance = park.Balance;
		Assert.AreEqual( 150, park.Sell( ride!.Id ) );
		Assert.AreEqual( balance + 150, park.Balance );
		Assert.IsFalse( park.TryGetObject( ride.Id, out _ ) );
	}

	/// <summary>A ride with the jungle Rides.sam rows: cost 1000 / 1000 / 2000 and a better scrap row per upgrade level.</summary>
	private static ParkEconomy ScrapPark( int firstYearOfBase = 50 ) => new( EconomyTestData.Settings(), new EconomyObjectCatalog( new[]
	{
		new EconomyObjectInfo( 1100, "Belly Bounce", ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[]
		{
			new UpgradeLevelInfo( 0, 1000, 0, 5, 0, new[] { firstYearOfBase, 30, 20, 10 } ),
			new UpgradeLevelInfo( 1, 1000, 250, 3, 2, new[] { 60, 40, 30, 15 } ),
			new UpgradeLevelInfo( 2, 2000, 300, 2, 4, new[] { 65, 45, 35, 20 } )
		}, null, null, null, 0, 0, 0, 0, 0, 25, "rides/bouncy.wad", 100, 5 )
	} ), ParkGameMode.FullSimulation, 7 );

	[TestMethod]
	public void ScrapValueIsTheBasePriceTimesTheCurrentLevelsAgePercentage()
	{
		// ORACLE.md F21 (0xE25E8 / 0xE2424): Upgrades[0].CostOfUpgrade only, whatever was spent on upgrades, and the row of the
		// current level. A ride at level 2 that guests have used, in years 1, 2, 3 and from year 4 on.
		var park = ScrapPark();
		var ride = park.RegisterExisting( 1100 );
		ride.Level = 2;
		ride.TotalSpent = 4000;
		park.RecordRideUse( ride.Id ); park.RecordUseFinished( ride.Id );
		Assert.AreEqual( 650, park.ScrapValue( ride ), "65 % of the base price 1000, not of the 4000 spent" );
		park.AdvanceDays( 365 );
		Assert.AreEqual( 450, park.ScrapValue( ride ) );
		park.AdvanceDays( 365 );
		Assert.AreEqual( 350, park.ScrapValue( ride ), "a two-year-old upgraded ride: 35 % of 1000 (the old rule gave 4000 x 35 % = 1400)" );
		park.AdvanceDays( 365 );
		Assert.AreEqual( 200, park.ScrapValue( ride ) );
		park.AdvanceDays( 365 * 5 );
		Assert.AreEqual( 200, park.ScrapValue( ride ), "year 4 onwards uses year 4" );
		ride.Level = 0;
		Assert.AreEqual( 100, park.ScrapValue( ride ) );
	}

	[TestMethod]
	public void AnObjectNobodyUsedIsRefundedInFullForItsFirstThirtyDays()
	{
		var park = ScrapPark();
		var ride = park.RegisterExisting( 1100 );
		ride.Level = 2;
		Assert.AreEqual( 1000, park.ScrapValue( ride ), "unused and new: 100 % of the base price whatever the level" );
		// A turn is 3750 s: 30 days are 691.2 turns.
		EconomyTestData.AdvanceToTurn( park, 691 );
		Assert.AreEqual( 1000, park.ScrapValue( ride ), "29.99 days" );
		EconomyTestData.AdvanceToTurn( park, 692 );
		Assert.AreEqual( 650, park.ScrapValue( ride ), "30.01 days: the level 2 row, year 1" );
	}

	[TestMethod]
	public void TheUseCountsWhenTheGuestLeavesAndTheProductWrapsLikeTheOriginal()
	{
		var park = ScrapPark();
		var ride = park.RegisterExisting( 1100 );
		park.RecordRideUse( ride.Id );
		Assert.AreEqual( 1000, park.ScrapValue( ride ), "a guest aboard: the use word +412 rises on release, so it still counts as unused" );
		park.RecordUseFinished( ride.Id );
		Assert.AreEqual( 500, park.ScrapValue( ride ) );

		var big = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( new[]
		{
			new EconomyObjectInfo( 1100, "Big", ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { new UpgradeLevelInfo( 0, 50_000_000, 0, 0, 0, new[] { 50, 30, 20, 10 } ) }, null, null, null, 0, 0, 0, 0, 0, 0, "x.wad" )
		} ), ParkGameMode.FullSimulation, 7 );
		Assert.AreEqual( 7_050_327, big.ScrapValue( big.RegisterExisting( 1100 ) ), "50,000,000 x 100 wraps to 32 bits before the unsigned division by 100" );
	}

	[TestMethod]
	public void AnImportedUsedOrOldObjectNeverGetsTheUnusedRefund()
	{
		var park = ScrapPark();
		EconomyTestData.AdvanceToTurn( park, 5000 );
		var used = park.RegisterExisting( 1100, builtTick: ParkCalendar.TickOfTurn( 4990 ), timesUsed: 3 );
		Assert.AreEqual( 500, park.ScrapValue( used ), "customers in the save: the ordinary year-1 row" );
		var old = park.RegisterExisting( 1100, builtTick: ParkCalendar.TickOfTurn( 1 ), timesUsed: 0 );
		Assert.AreEqual( ParkCalendar.TickOfTurn( 1 ), old.BuiltTick );
		Assert.AreEqual( 500, park.ScrapValue( old ), "unused but older than 30 days" );
		Assert.AreEqual( 1000, park.ScrapValue( park.RegisterExisting( 1100 ) ), "a new object without saved history is new and unused" );
	}

	[TestMethod]
	public void TheFirstThirtyDaysRefundNeedsNoUseAndAPositiveBaseFirstYearPercentage()
	{
		var park = ScrapPark();
		var ride = park.RegisterExisting( 1100 );
		park.RecordRideUse( ride.Id ); park.RecordUseFinished( ride.Id );
		Assert.AreEqual( 500, park.ScrapValue( ride ), "a use ends the full refund" );
		var shop = ScrapPark( firstYearOfBase: 0 );
		var unused = shop.RegisterExisting( 1100 );
		Assert.AreEqual( 0, shop.ScrapValue( unused ), "the base level's first-year percentage is 0, so there is no 100 %" );
		unused.Level = 1;
		Assert.AreEqual( 600, shop.ScrapValue( unused ), "the test reads level 0's row, so level 1 gets its own first-year 60 %" );
	}

	[TestMethod]
	public void RidesWearOutAndMechanicsRepairAndUpgradeThem()
	{
		var park = EconomyTestData.Park();
		var rides = new EconomyTestData.RideOperations();
		park.RideOperations = rides;
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		park.TryBuild( 1100, out var ride );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotResearched, park.TryBuyUpgrade( ride!.Id ) );
		// Five riders at speed 50 under red line 60 and capacity red line 2: s = 0.55, c = 1.0, wear = 1.55 × 0.5 × 5.
		rides.Set( ride.Id, new RideOperation( true, 5, 50 ) );
		const double step = 3.875;
		EconomyTestData.AdvanceToTurn( park, ParkEconomy.WearInterval - 1 );
		Assert.AreEqual( 100, ride.Repair, "no wear before the 64th turn" );
		EconomyTestData.AdvanceToTurn( park, ParkEconomy.WearInterval );
		Assert.AreEqual( 100 - step, ride.Repair, 1e-3 );
		EconomyTestData.AdvanceToTurn( park, 19 * ParkEconomy.WearInterval );
		Assert.AreEqual( 0, events.Count( item => item.Kind == ParkEventKind.RideWorn ) );
		EconomyTestData.AdvanceToTurn( park, 20 * ParkEconomy.WearInterval );
		Assert.AreEqual( 22, ride.StateOfRepair );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.RideWorn ) );
		EconomyTestData.AdvanceToTurn( park, 26 * ParkEconomy.WearInterval - ParkEconomy.RideUpdateInterval );
		Assert.IsFalse( ride.IsBrokenDown, "state of repair 3.125 still works" );
		EconomyTestData.AdvanceToTurn( park, 26 * ParkEconomy.WearInterval );
		Assert.IsTrue( ride.IsBrokenDown );
		Assert.AreEqual( 0, ride.Repair );
		Assert.AreEqual( 100 - 26 * 0.02 * step - ParkEconomy.BreakdownLifeLoss, ride.LifeGauge, 1e-3, "wear and the breakdown cost life" );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.RideBrokeDown ) );
		EconomyTestData.AdvanceToTurn( park, 27 * ParkEconomy.WearInterval );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.RideBrokeDown ), "a broken ride does not break down again" );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		park.Advance( ParkCalendar.TicksForHours( 2 ) );
		Assert.AreEqual( mechanic.Id, ride.MechanicId );
		var life = ride.LifeGauge;
		// ORACLE F19: the repair of a ride at zero repair takes WorkDuration park turns (100 % of it) and one update more to complete it.
		var work = park.Settings[StaffType.Mechanic].WorkDuration[mechanic.Grade];
		var startTurn = park.Turn;
		Assert.IsTrue( mechanic.BusyTicks is > 0 && mechanic.BusyTicks <= work + 1 && mechanic.BusyTicks > work + 1 - 3, "dispatched within the last turns" );
		var remaining = mechanic.BusyTicks;
		EconomyTestData.AdvanceToTurn( park, startTurn + remaining - 1 );
		Assert.IsTrue( ride.IsBrokenDown, "still being repaired one turn before the timer ends" );
		EconomyTestData.AdvanceToTurn( park, startTurn + remaining );
		Assert.IsFalse( ride.IsBrokenDown );
		Assert.AreEqual( 100, ride.Repair );
		Assert.AreEqual( life, ride.LifeGauge, "a repair does not restore the life gauge, and a ride under repair does not wear" );
		Assert.AreEqual( 0, mechanic.AssignedInstanceId );
		rides.Set( ride.Id, new RideOperation( false, 0, 50 ) );

		var researcher = EconomyTestData.HireBest( park, StaffType.Researcher );
		park.SetResearchEffort( ResearchCategory.Ride, 0 );
		while ( !park.Research.IsAvailable( 1100, 1 ) )
			park.AdvanceDays( 1 );
		var before = park.Balance;
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		Assert.AreEqual( before - 400, park.Balance );
		Assert.AreEqual( ParkEconomy.PurchaseResult.UpgradeInProgress, park.TryBuyUpgrade( ride.Id ) );
		park.AdvanceDays( 10 );
		Assert.AreEqual( 1, ride.Level );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.UpgradeCompleted ) );
		Assert.IsNotNull( researcher );
		var instant = EconomyTestData.Park( mode: ParkGameMode.InstantAction );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotAvailableInInstantAction, instant.TryBuyUpgrade( instant.RegisterExisting( 1100 ).Id ) );
	}

	[TestMethod]
	public void RepairTimerScalesWithTheMissingRepairAndMultipliesUpgrades()
	{
		// ORACLE F19 (0xd9c0c): WorkDuration x (100 - truncated state of repair) / 100, or WorkDuration x DurationOfUpgrade for an upgrade.
		Assert.AreEqual( 48, ParkEconomy.RepairTimer( 80, 40.0, null ), "a ride at 40 % repair, grade 0" );
		Assert.AreEqual( 48, ParkEconomy.RepairTimer( 80, 40.9, null ), "the gauge is truncated" );
		Assert.AreEqual( 80, ParkEconomy.RepairTimer( 80, 0.0, null ) );
		Assert.AreEqual( 0, ParkEconomy.RepairTimer( 80, 100.0, null ), "a fully repaired ride needs no time" );
		Assert.AreEqual( 12, ParkEconomy.RepairTimer( 20, 40.0, null ), "grade 4" );
		Assert.AreEqual( 0, ParkEconomy.RepairTimer( 80, 99.0, null ), "0.8 rounds down" );
		Assert.AreEqual( 160, ParkEconomy.RepairTimer( 80, 40.0, 2 ), "an upgrade ignores the state of repair" );
		Assert.AreEqual( 0, ParkEconomy.RepairTimer( 80, 40.0, 0 ), "no minimum of one" );
		Assert.AreEqual( 320, ParkEconomy.RepairTimer( 80, 40.0, 4 ) );
		Assert.AreEqual( 0L, ParkEconomy.RepairTimer( 80, 101.0, null ), "-0.8 truncates toward zero" );
		Assert.AreEqual( 4294967216L, ParkEconomy.RepairTimer( 80, 200.0, null ), "above 100 the factor goes negative and the unsigned word wraps (-80)" );
	}

	[TestMethod]
	public void RepairTimerIsTheLowByteOfTheTruncatedGauge()
	{
		Assert.AreEqual( 80L, ParkEconomy.RepairTimer( 80, 256.0, null ), "256 wraps to a zero byte: a full repair is due" );
		Assert.AreEqual( 80L, ParkEconomy.RepairTimer( 80, -256.0, null ), "-256 is the word 0xFFFFFF00" );
	}

	[TestMethod]
	public void UpgradesAreQueuedWithoutMechanicsAndWaitForOne()
	{
		// STP-PPC 0x10166F1C: buying an upgrade checks money only; a mechanic installs it later.
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var ride );
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.SetResearchEffort( ResearchCategory.Ride, 0 );
		while ( !park.Research.IsAvailable( 1100, 1 ) )
			park.AdvanceDays( 1 );
		Assert.IsFalse( park.Staff.OfType( StaffType.Mechanic ).Any() );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride!.Id ) );
		park.AdvanceDays( 10 );
		Assert.AreEqual( 0, ride.Level, "nobody installs it without a mechanic" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.UpgradeInProgress, park.TryBuyUpgrade( ride.Id ) );
		EconomyTestData.HireBest( park, StaffType.Mechanic );
		park.AdvanceDays( 10 );
		Assert.AreEqual( 1, ride.Level );
	}

	private sealed class FixedGuests( int inPark ) : IParkGuestStatistics
	{
		public int PeopleInPark => inPark;
		public float AverageHappiness => 0;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	[TestMethod]
	public void ParkRatingSumsCappedCountsOfGuestsAttractionsAndStaff()
	{
		var park = EconomyTestData.Park( initialCash: 1000000 );
		Assert.AreEqual( 0, park.ParkRating );
		park.GuestStatistics = new FixedGuests( 749 );
		Assert.AreEqual( 14, park.ParkRating, "749 × 20 / 1000" );
		park.GuestStatistics = new FixedGuests( 5000 );
		Assert.AreEqual( 20, park.ParkRating, "guests are capped at 1000" );
		park.GuestStatistics = NoGuestStatistics.Instance;

		park.TryBuild( 1100, out var ride );
		Assert.AreEqual( 1, park.ParkRating, "one ride × 3 / 2" );
		ride!.IsOpen = false;
		Assert.AreEqual( 1, park.ParkRating, "closed attractions count too" );
		park.TryBuild( 1203, out _ );
		park.TryBuild( 1303, out _ );
		park.TryBuild( 1402, out _ );
		Assert.AreEqual( 1 + 2 + 2 + 1, park.ParkRating );
		ride.Level = 2;
		Assert.AreEqual( 7, park.ParkRating, "a ride at upgrade level 2 adds one" );
		for ( var i = 0; i < 20; i++ )
			park.TryBuild( 1203, out _ );
		Assert.AreEqual( 7 - 2 + 10, park.ParkRating, "shops are capped at 10" );

		for ( var i = 0; i < 6; i++ )
		{
			var candidate = park.Staff.Candidates.FirstOrDefault( item => item.Type == StaffType.Handyman );
			if ( candidate != null )
				park.Hire( candidate.Id );
		}
		var handymen = park.Staff.Members.Count( member => member.Type == StaffType.Handyman );
		Assert.AreEqual( 15 + Math.Min( handymen, 4 ), park.ParkRating, "each staff type is capped at 4" );
	}

	[TestMethod]
	public void CandidateGradesSpreadAroundTheAverageAndWrapBelowZero()
	{
		var grades = new HashSet<int>();
		var park = EconomyTestData.Park();
		for ( var step = 0; step < 40; step++ )
		{
			foreach ( var candidate in park.Staff.Candidates )
				grades.Add( candidate.Grade );
			park.Advance( park.Staff.UpdateInterval + ParkCalendar.TicksForHours( 2 ) );
		}
		CollectionAssert.AreEquivalent( new[] { 0, 1, 2 }, grades.ToArray(), "average 1: grades 0, 1 and 2 only, no 'great' grade" );
		var zeroAverage = new ParkEconomy( EconomyTestData.Settings( extra: "StaffPoolInfo.AvgGradeOfHandymen 0\n" ), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		var handymen = new HashSet<int>();
		for ( var step = 0; step < 40; step++ )
		{
			foreach ( var candidate in zeroAverage.Staff.Candidates.Where( candidate => candidate.Type == StaffType.Handyman ) )
				handymen.Add( candidate.Grade );
			zeroAverage.Advance( zeroAverage.Staff.UpdateInterval + ParkCalendar.TicksForHours( 2 ) );
		}
		CollectionAssert.AreEquivalent( new[] { 0, 1, 4 }, handymen.ToArray(), "average 0: a raw -1 wraps to 255 and is stored as grade 4" );
	}

	[TestMethod]
	public void HandymenCleanLitter()
	{
		var park = EconomyTestData.Park();
		park.OpenPark();
		park.TryBuild( 1203, out var shop );
		for ( var sale = 0; sale < 100; sale++ )
			park.TryBuy( shop!.Id, 100, out _ );
		Assert.AreEqual( 50, park.LitterItems );
		var rating = park.ParkRating;
		park.AdvanceDays( 1 );
		Assert.AreEqual( 50, park.LitterItems );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		var hourBefore = ParkCalendar.HourIndex( park.Tick );
		park.AdvanceDays( 1 );
		var hours = ParkCalendar.HourIndex( park.Tick ) - hourBefore;
		Assert.IsTrue( hours is 23 or 24, hours.ToString() );
		var perHour = 60 * ParkEconomy.LitterScale / park.Settings[StaffType.Handyman].WorkDuration[handyman.Grade];
		Assert.AreEqual( Math.Max( 0, 50 * ParkEconomy.LitterScale - hours * perHour ), park.LitterScaled, "one cleaning step per park-clock hour" );
		Assert.IsTrue( park.ParkRating >= rating );
	}

	[TestMethod]
	public void ResearchProgressesByAbilityEffortAndGroups()
	{
		var park = EconomyTestData.Park();
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		Assert.IsTrue( park.Research.IsAvailable( 1100 ) );
		Assert.IsTrue( park.Research.IsAvailable( 1203 ) );
		Assert.IsFalse( park.Research.IsAvailable( 1180 ) );
		Assert.IsFalse( park.Research.IsAvailable( 1100, 1 ) );
		Assert.IsTrue( park.Research.IsGroupOpen( ResearchCategory.Ride, 1 ) );
		Assert.IsFalse( park.Research.IsGroupOpen( ResearchCategory.Ride, 2 ) );
		Assert.AreEqual( 1180, park.Research.Current( ResearchCategory.Ride )!.InfoId );
		park.AdvanceDays( 3 );
		Assert.AreEqual( 0, park.Research.GetProgress( park.Research.Current( ResearchCategory.Ride )! ), "no researchers, no progress" );

		var researcher = EconomyTestData.HireBest( park, StaffType.Researcher );
		var ability = park.Settings.ResearchAbility[researcher.Grade];
		long Share( int effort, int total ) => (long)Math.Round( (float)((double)((float)ability * ((float)effort / (float)total) * 85f) / 100.0) * (double)ParkResearch.PointScale );
		void NextResearchTurn() => park.Advance( ParkCalendar.TickOfTurn( (park.Turn / ParkResearch.TurnsPerResearch + 1) * ParkResearch.TurnsPerResearch ) - park.Tick );
		NextResearchTurn();
		var minecart = park.Research.Items.Single( item => item.InfoId == 1180 );
		Assert.AreEqual( Share( 100, 110 ), park.Research.GetProgress( minecart ), "ability x ride effort 100 of 100 + upgrade 10 x work load 85 / 100" );
		Assert.AreEqual( Share( 10, 110 ), park.Research.GetProgress( park.Research.Current( ResearchCategory.Upgrade )! ) );
		park.Advance( 1 );
		Assert.AreEqual( Share( 100, 110 ), park.Research.GetProgress( minecart ), "nothing between research turns" );

		var ticks = 1;
		while ( !park.Research.IsAvailable( 1180 ) && ticks++ < 2000 )
			NextResearchTurn();
		Assert.IsTrue( park.Research.IsAvailable( 1180 ) );
		Assert.AreEqual( (int)Math.Ceiling( 800.0 * ParkResearch.PointScale / Share( 100, 110 ) ), ticks );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.ItemResearched && item.InfoId == 1180 ) );
		Assert.IsTrue( park.Research.IsGroupOpen( ResearchCategory.Ride, 2 ) );
		Assert.AreEqual( 1106, park.Research.Current( ResearchCategory.Ride )!.InfoId );
		Assert.AreEqual( 1, park.Counters[ParkCounters.Researched( ResearchCategory.Ride )] );

		park.SetResearchEffort( ResearchCategory.Upgrade, 100 );
		while ( !park.Research.IsAllResearched && park.Tick < ParkCalendar.TickAtDay( 3600 ) )
			park.AdvanceDays( 1 );
		Assert.IsTrue( park.Research.IsAllResearched );
		Assert.IsTrue( park.Research.IsAvailable( 1500 ), "add-on after its target ride" );

		var instant = EconomyTestData.Park( mode: ParkGameMode.InstantAction );
		instant.Advance( ParkCalendar.TickOfTurn( ParkResearch.TurnsPerResearch ) );
		Assert.AreEqual( 0, instant.Research.GetProgress( instant.Research.Current( ResearchCategory.Ride )! ), "no staffless Instant Action research" );
		EconomyTestData.HireBest( instant, StaffType.Researcher );
		instant.Advance( ParkCalendar.TickOfTurn( ParkResearch.TurnsPerResearch ) );
		Assert.IsTrue( instant.Research.GetProgress( instant.Research.Current( ResearchCategory.Ride )! ) > 0, "Instant Action researchers research" );
	}

	[TestMethod]
	public void ResearchGroupsOpenOnTheShareResearchedOfAllOpenGroups()
	{
		var scrap = new[] { 50, 30, 20, 10 };
		EconomyObjectInfo Ride( int id, int group, int research ) => new( id, $"Ride {id}", ParkObjectKind.Ride, ResearchCategory.Ride, group, new[] { new UpgradeLevelInfo( 0, 500, research, 1, 0, scrap ) }, null, null, null, 0, 0, 0, 0, 0, 25, $"rides/r{id}.wad" );
		var catalog = new EconomyObjectCatalog( new[] { Ride( 1100, 0, 0 ), Ride( 1101, 0, 0 ), Ride( 1102, 0, 0 ), Ride( 1103, 0, 0 ), Ride( 1110, 1, 100 ), Ride( 1111, 1, 100 ), Ride( 1120, 2, 100 ) } );
		var research = new ParkResearch( EconomyTestData.Settings(), catalog );
		Assert.AreEqual( 1, research.HighestOpenGroup( ResearchCategory.Ride ), "groups <= 1: 4 of 6 researched (66 %) is below ResearchTech[2] = 80" );
		while ( !research.IsAvailable( 1110 ) )
			research.AddResearcherPoints( 100 );
		Assert.IsFalse( research.IsAvailable( 1111 ) );
		Assert.AreEqual( 2, research.HighestOpenGroup( ResearchCategory.Ride ), "5 of 6 (83 %) opens group 2 although group 1 is half done" );
	}

	[TestMethod]
	public void ResearchFollowsTableOrderNotCost()
	{
		var scrap = new[] { 50, 30, 20, 10 };
		var catalog = new EconomyObjectCatalog( new[]
		{
			new EconomyObjectInfo( 1100, "Belly Bounce", ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { new UpgradeLevelInfo( 0, 500, 0, 5, 0, scrap ) }, null, null, null, 0, 0, 0, 0, 0, 25, "rides/bouncy.wad" ),
			new EconomyObjectInfo( 1120, "Expensive", ParkObjectKind.Ride, ResearchCategory.Ride, 1, new[] { new UpgradeLevelInfo( 0, 5000, 900, 1, 0, scrap ) }, null, null, null, 0, 0, 0, 0, 0, 25, "rides/a.wad" ),
			new EconomyObjectInfo( 1130, "Cheap", ParkObjectKind.Ride, ResearchCategory.Ride, 1, new[] { new UpgradeLevelInfo( 0, 500, 100, 1, 0, scrap ) }, null, null, null, 0, 0, 0, 0, 0, 25, "rides/b.wad" )
		} );
		var research = new ParkResearch( EconomyTestData.Settings(), catalog );
		Assert.AreEqual( 1120, research.Current( ResearchCategory.Ride )!.InfoId, "the first open item in table order, although it costs more" );
		for ( var step = 0; step < 100 && !research.IsAvailable( 1120 ); step++ )
			research.AddResearcherPoints( 100 );
		Assert.IsTrue( research.IsAvailable( 1120 ) );
		Assert.AreEqual( 1130, research.Current( ResearchCategory.Ride )!.InfoId );
	}

	[TestMethod]
	public void BuildChallengesFollowTheBinaryHandlerTable()
	{
		var park = EconomyTestData.Park();
		ChallengeDefinition Challenge( int type, int value, int target, int target2 = 0 ) => new( 0, type, 0, 60, value, target, target2, 0, 1000, false, true );
		bool Reached( ChallengeDefinition definition ) => park.Objectives.Measure( definition, park.Counters, park.Staff, park.Objects, park.GuestStatistics ) >= ParkObjectives.Target( definition );
		var upgrade = Challenge( 28, 2, 1100 );
		Assert.IsFalse( Reached( upgrade ), "no ride: level -1" );
		var ride = park.RegisterExisting( 1100 );
		Assert.IsFalse( Reached( Challenge( 30, 0, 1180 ) ) );
		Assert.IsTrue( Reached( Challenge( 30, 0, 1100 ) ), "an existing ride counts; TargetVal is not read" );
		Assert.IsTrue( Reached( Challenge( 31, 5, 1100, 1180 ) ), "type 31 shares the type 30 handler: only TargetObj" );
		Assert.IsTrue( Reached( Challenge( 32, 0, 1100 ) ) );
		Assert.IsFalse( Reached( Challenge( 33, 0, 1100, 1180 ) ), "type 33 needs both objects" );
		park.RegisterExisting( 1180 );
		Assert.IsTrue( Reached( Challenge( 33, 0, 1100, 1180 ) ) );
		Assert.IsFalse( ParkObjectives.IsCountSinceAcceptance( 30 ) || ParkObjectives.IsCountSinceAcceptance( 33 ), "built counts are absolute" );
		ride.Level = 1;
		Assert.IsFalse( Reached( upgrade ), "level byte 1 < TargetVal 2" );
		ride.Level = 2;
		Assert.IsTrue( Reached( upgrade ), "the third level (byte 2) reaches TargetVal 2" );
	}

	[TestMethod]
	public void ChallengesAreOfferedCompletedFollowedUpAndForfeited()
	{
		var park = EconomyTestData.Park();
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.AdvanceDays( 539 );
		Assert.IsNull( park.Objectives.Current );
		park.AdvanceDays( 1 );
		Assert.AreEqual( 1, park.Objectives.Current!.DefinitionIndex );
		park.DeclineChallenge();
		Assert.AreEqual( 540 + 270, park.Objectives.NextOfferDay );
		park.AdvanceDays( 270 );
		Assert.AreEqual( 7, park.Objectives.Current!.DefinitionIndex );
		park.AcceptChallenge();
		Assert.IsTrue( park.Research.IsAvailable( 1180 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1180, out var minecart ) );
		var balance = park.Balance;
		park.AdvanceDays( 1 );
		Assert.AreEqual( balance + 7500, park.Balance );
		Assert.AreEqual( 7, events.Single( item => item.Kind == ParkEventKind.ChallengeCompleted ).InfoId );
		Assert.AreEqual( 8, park.Objectives.Current!.DefinitionIndex, "follow-up type 20 for the same ride" );
		park.AcceptChallenge();
		park.RecordRideUse( minecart!.Id );
		park.AdvanceDays( 1 );
		Assert.AreEqual( 8, park.Objectives.Current!.DefinitionIndex );
		park.RecordRideUse( minecart.Id );
		park.AdvanceDays( 1 );
		Assert.IsNull( park.Objectives.Current );
		Assert.AreEqual( 37500, park.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherIncome ) + park.Ledger.History.Sum( month => month[LedgerCategory.OtherIncome] ) );
		park.AdvanceDays( 270 );
		Assert.AreEqual( 1, park.Objectives.Current!.DefinitionIndex );
		park.DeclineChallenge();
		Assert.IsTrue( park.Objectives.Finished.Contains( 1 ), "second decline forfeits" );
		park.AdvanceDays( 600 );
		Assert.IsNull( park.Objectives.Current );

		var failing = EconomyTestData.Park();
		failing.AdvanceDays( 540 );
		failing.AcceptChallenge();
		failing.AdvanceDays( 60 );
		Assert.IsNull( failing.Objectives.Current );
		Assert.IsTrue( failing.Objectives.Finished.Contains( 1 ) );
	}

	[TestMethod]
	public void GoldenTicketsAreCheckedEvery100TurnsInFullSimulationOnly()
	{
		foreach ( var mode in new[] { ParkGameMode.FullSimulation, ParkGameMode.InstantAction } )
		{
			var park = EconomyTestData.Park( mode: mode );
			park.OpenPark();
			for ( var visitor = 0; visitor < 101; visitor++ )
				park.TryAdmitVisitor( 100, out _ );
			park.Advance( ParkCalendar.TickOfTurn( ParkEconomy.GoldenTicketCheckInterval ) - 1 );
			Assert.AreEqual( 0, park.Objectives.GoldenTickets.Count, $"{mode}: not checked before turn 100" );
			park.Advance( 1 );
			Assert.AreEqual( mode == ParkGameMode.FullSimulation ? 1 : 0, park.Objectives.GoldenTickets.Count, $"{mode}: checked at turn 100" );
		}
	}

	// ORACLE F23 (ECON-039): the local ticket check 0xd31d0 awards ticket 4 when the bank's year profit word is signed GREATER than
	// GoldenTicketLocal.ProfitYear. The 0.9 multiplier of 0x13fdc is the advisor's near-ticket hint, not part of the award.
	[TestMethod]
	public void TheProfitTicketNeedsTheYearProfitStrictlyAboveProfitYear()
	{
		var threshold = EconomyTestData.Park().Settings.GoldenTickets.ProfitYear;
		Assert.AreEqual( 15000, threshold );
		bool Won( long profit )
		{
			var park = EconomyTestData.Park();
			return park.Objectives.CheckGoldenTickets( park.Counters, park.GuestStatistics, park.Research, profit, Array.Empty<long>() ).Contains( GoldenTicketKind.ProfitYear );
		}
		Assert.IsFalse( Won( 0 ) );
		Assert.IsFalse( Won( -20000 ) );
		Assert.IsFalse( Won( (long)(threshold * 0.9) + 1 ), "no 0.9 factor: the advisor hint is not the award" );
		Assert.IsFalse( Won( threshold - 1 ) );
		Assert.IsFalse( Won( threshold ), "equal is not enough (signed greater-than at 0xd3400)" );
		Assert.IsTrue( Won( threshold + 1 ) );
		Assert.IsTrue( Won( int.MaxValue ) );
	}

	// ORACLE F23: every ticket arm of 0xd31d0 and 0xd34c0 is a signed strictly-greater compare. Each case checks the limit (not won) and
	// the limit plus one (won) against the shipped jungle thresholds of the test Standard.sam.
	private sealed class StatGuests( int people, float average ) : IParkGuestStatistics
	{
		public int PeopleInPark => people;
		public float AverageHappiness => average;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	private static bool TicketWon( GoldenTicketKind kind, int visitors = 0, int people = 0, float average = 0, long profit = 0, long recentVisitors = 0,
		ParkRecordKind? record = null, long recordValue = 0 )
	{
		var park = EconomyTestData.Park();
		park.Counters.Add( ParkCounters.Admissions, visitors );
		if ( record is { } kindOfRecord )
			park.Counters.Max( ParkCounters.Record( kindOfRecord, 0 ), recordValue );
		// six closed months; the last carries the recent visitors (RecentVisitorMonths 6)
		var months = new long[] { 0, 0, 0, 0, 0, recentVisitors };
		return park.Objectives.CheckGoldenTickets( park.Counters, new StatGuests( people, average ), park.Research, profit, months ).Contains( kind );
	}

	[TestMethod]
	public void EveryGoldenTicketNeedsItsStatisticStrictlyAboveTheLimit()
	{
		Assert.IsFalse( TicketWon( GoldenTicketKind.Visitors, visitors: 100 ) );
		Assert.IsTrue( TicketWon( GoldenTicketKind.Visitors, visitors: 101 ) );
		Assert.IsFalse( TicketWon( GoldenTicketKind.PeopleInPark, people: 200 ) );
		Assert.IsTrue( TicketWon( GoldenTicketKind.PeopleInPark, people: 201 ) );
		// happiness: the single average above 75 and more than 150 guests; the fraction counts (75.5 > 75), nobody is filtered by happiness
		Assert.IsFalse( TicketWon( GoldenTicketKind.Happiness, people: 151, average: 75f ) );
		Assert.IsTrue( TicketWon( GoldenTicketKind.Happiness, people: 151, average: 75.5f ) );
		Assert.IsFalse( TicketWon( GoldenTicketKind.Happiness, people: 150, average: 90f ) );
		Assert.IsTrue( TicketWon( GoldenTicketKind.Happiness, people: 151, average: 75.01f ) );
		Assert.IsFalse( TicketWon( GoldenTicketKind.Happiness, people: 151, average: float.NaN ) );
		Assert.IsFalse( TicketWon( GoldenTicketKind.RecentVisitors, recentVisitors: 350 ) );
		Assert.IsTrue( TicketWon( GoldenTicketKind.RecentVisitors, recentVisitors: 351 ) );
		Assert.IsFalse( TicketWon( GoldenTicketKind.CoasterHeight, record: ParkRecordKind.CoasterHeight, recordValue: 105 ) );
		Assert.IsTrue( TicketWon( GoldenTicketKind.CoasterHeight, record: ParkRecordKind.CoasterHeight, recordValue: 106 ) );
		Assert.IsFalse( TicketWon( GoldenTicketKind.GokartExcitement, record: ParkRecordKind.GokartExcitement, recordValue: 90 ) );
		Assert.IsTrue( TicketWon( GoldenTicketKind.GokartExcitement, record: ParkRecordKind.GokartExcitement, recordValue: 91 ) );
		Assert.IsFalse( TicketWon( GoldenTicketKind.WaterLength, record: ParkRecordKind.WaterLength, recordValue: 50 ) );
		Assert.IsTrue( TicketWon( GoldenTicketKind.WaterLength, record: ParkRecordKind.WaterLength, recordValue: 51 ) );
		Assert.IsFalse( TicketWon( GoldenTicketKind.BigPark, record: ParkRecordKind.CellsOwned, recordValue: 3000 ) );
		Assert.IsTrue( TicketWon( GoldenTicketKind.BigPark, record: ParkRecordKind.CellsOwned, recordValue: 3001 ) );
	}

	[TestMethod]
	public void GoldenTicketsAndKeys()
	{
		var park = EconomyTestData.Park();
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		park.OpenPark();
		for ( var visitor = 0; visitor < 101; visitor++ )
			park.TryAdmitVisitor( 100, out _ );
		park.ReportRecord( ParkRecordKind.CoasterHeight, 0, 110 );
		park.AdvanceMonths( 1 );
		CollectionAssert.AreEquivalent( new[] { GoldenTicketKind.Visitors, GoldenTicketKind.CoasterHeight }, park.Objectives.GoldenTickets.ToArray() );
		Assert.AreEqual( 2, events.Count( item => item.Kind == ParkEventKind.GoldenTicketWon ) );
		Assert.AreEqual( 2, park.GoldenTicketsAvailable );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughGoldenTickets, park.TryBuild( 1112, out _ ) );
		park.ReportRecord( ParkRecordKind.WaterLength, 0, 60 );
		park.AdvanceMonths( 1 );
		var earnedProgress = new PlayerProgress();
		earnedProgress.SetTickets( park.Settings.Theme, park.Objectives.GoldenTickets.Count );
		Assert.AreEqual( 3, earnedProgress.TotalTickets );
		Assert.AreEqual( PlayerProgress.StartingKeys + 1, earnedProgress.Keys );
		var cash = park.Balance;
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1112, out _ ) );
		Assert.AreEqual( 0, park.GoldenTicketsAvailable );
		Assert.AreEqual( cash, park.Balance, "a ticket purchase costs no cash" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1112, out _ ), "a second copy takes the cash path" );
		Assert.AreEqual( cash - 2500, park.Balance );
		Assert.AreEqual( 0, park.GoldenTicketsAvailable );
		earnedProgress.SetTickets( park.Settings.Theme, park.Objectives.GoldenTickets.Count );
		Assert.AreEqual( PlayerProgress.StartingKeys + 1, earnedProgress.Keys, "Spending mystery-item tickets preserves earned keys." );

		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );
		Assert.AreEqual( 0, restored.GoldenTicketsAvailable );
		Assert.AreEqual( 3, restored.TicketsSpent );
		CollectionAssert.AreEqual( new[] { 1112 }, restored.TicketItems.ToArray() );
		var restoredProgress = new PlayerProgress();
		restoredProgress.SetTickets( restored.Settings.Theme, restored.Objectives.GoldenTickets.Count );
		Assert.AreEqual( earnedProgress.TotalTickets, restoredProgress.TotalTickets );
		Assert.AreEqual( earnedProgress.Keys, restoredProgress.Keys );

		var progress = new PlayerProgress();
		Assert.AreEqual( 1, progress.Keys );
		Assert.IsTrue( progress.CanEnter( 1 ) );
		Assert.IsFalse( progress.CanEnter( 3 ) );
		progress.SetTickets( "jungle", 8 );
		progress.SetTickets( "jungle", 3 );
		Assert.AreEqual( 3, progress.Keys );
		Assert.IsTrue( progress.CanEnter( 3 ) );
	}

	[DataTestMethod]
	[DataRow( 0, 0 )]
	[DataRow( 2, 0 )]
	[DataRow( 3, 1 )]
	[DataRow( 5, 1 )]
	[DataRow( 6, 2 )]
	public void EveryThirdEarnedTicketAwardsAKey( int tickets, int earnedKeys )
	{
		var progress = new PlayerProgress();
		progress.SetTickets( "jungle", Math.Min( 2, tickets ) );
		progress.SetTickets( "hallow", Math.Max( 0, tickets - 2 ) );
		Assert.AreEqual( tickets, progress.TotalTickets );
		Assert.AreEqual( earnedKeys, progress.Keys - PlayerProgress.StartingKeys );
	}

	private static void Play( ParkEconomy park, int day )
	{
		park.OpenPark();
		foreach ( var item in park.Objects.ToList() )
		{
			for ( var guest = 0; guest < 3 + day % 4; guest++ )
			{
				if ( item.Kind == ParkObjectKind.Shop )
					park.TryBuy( item.Id, 500, out _ );
				else if ( item.Kind == ParkObjectKind.Sideshow )
					park.PlaySideshow( item.Id, 500, out _, out _ );
				else if ( item.Kind == ParkObjectKind.Ride )
					park.RecordRideUse( item.Id );
			}
		}
		for ( var visitor = 0; visitor < 5 + day % 7; visitor++ )
			park.TryAdmitVisitor( 300, out _ );
		if ( park.Objectives.Current is { Accepted: false } )
			park.AcceptChallenge();
		park.AdvanceDays( 1 );
	}

	private static ParkEconomy BusyPark()
	{
		var park = EconomyTestData.Park( seed: 99 );
		foreach ( var type in Enum.GetValues<StaffType>() )
			EconomyTestData.HireBest( park, type );
		park.SetTrainingBudget( StaffType.Handyman, 300 );
		park.TryBuild( 1100, out _ );
		park.TryBuild( 1203, out _ );
		park.TryBuild( 1303, out _ );
		park.RegisterExisting( 1601 );
		park.TakeLoan( 1 );
		park.ReportRecord( ParkRecordKind.CellsOwned, 0, 1234 );
		for ( var day = 0; day < 830; day++ )
			Play( park, day );
		park.Advance( 37 );
		return park;
	}

	[TestMethod]
	public void SavesWrittenWithTheOldLoanBalanceStillLoad()
	{
		// Version 2 saves from the interest-on-balance model hold the old remaining balance and an annuity instalment. The
		// months left already say how far a loan is repaid, so the balance is dropped and M x the months left replaces it.
		var park = EconomyTestData.Park( initialCash: 5000 );
		park.TakeLoan( 1 );
		park.AdvanceMonths( 5 );
		var json = JsonNode.Parse( ParkSaveFile.Serialize( park ) )!;
		var loan = json["Loans"]![0]!.AsObject();
		Assert.AreEqual( 31, (int)loan["MonthsRemaining"]! );
		Assert.AreEqual( park.Loans[0].RemainingBalance, (long)loan["RemainingBalance"]!, "written as M x the months left" );
		loan["MonthlyRepayment"] = 1858;
		loan["RemainingBalance"] = 43210;
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ), park.Settings, park.Catalog );
		var account = restored.Loans.Single();
		Assert.AreEqual( (5, 31, 1858L, 1858L * 31), (account.MonthsRepaid, account.MonthsRemaining, account.MonthlyRepayment, account.RemainingBalance) );
		loan.Remove( "RemainingBalance" );
		Assert.AreEqual( account, ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ), park.Settings, park.Catalog ).Loans.Single() );
		loan["MonthsRemaining"] = 0;
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
	}

	[TestMethod]
	public void SaveRoundTripRestoresTheCompleteState()
	{
		var park = BusyPark();
		Assert.IsTrue( park.Ledger.History.Count > 12 && park.Loans.Count == 1 && park.Staff.Members.Count == 5 && park.Research.Completed.Count > 0 );
		Assert.IsNotNull( park.Objectives.Current );
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
		Assert.AreEqual( park.Balance, restored.Balance );
		Assert.AreEqual( park.Date, restored.Date );
		for ( var day = 830; day < 900; day++ )
		{
			Play( park, day );
			Play( restored, day );
		}
		Assert.AreEqual( ParkSaveFile.Serialize( park ), ParkSaveFile.Serialize( restored ), "identical continuation after load" );

		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-park-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			var path = Path.Combine( directory, "park.json" );
			ParkSaveFile.Save( path, park );
			ParkSaveFile.Save( path, park );
			Assert.AreEqual( 1, Directory.GetFiles( directory ).Length, "no temporary files remain" );
			var loaded = ParkSaveFile.Load( path, ( theme, easy ) => (park.Settings, park.Catalog) );
			Assert.AreEqual( ParkSaveFile.Serialize( park ), ParkSaveFile.Serialize( loaded ) );
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Save( Path.Combine( directory, "park.TPWS" ), park ) );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void WearAmountFollowsTheOriginalFormula()
	{
		var catalog = EconomyTestData.Catalog();
		Assert.IsTrue( catalog.TryGet( 1100, out var bouncy ) );
		var level = bouncy.Upgrades[0];
		Assert.AreEqual( 3.875f, ParkEconomy.WearAmount( bouncy, level, new RideOperation( true, 5, 50 ) ), 1e-5f, "under both red lines" );
		Assert.AreEqual( (1.0f + 0.28f) * 0.5f * 5, ParkEconomy.WearAmount( bouncy, level, new RideOperation( true, 1, 100 ) ), 1e-5f, "speed over the red line: (0.9 + 1.1) × 0.5; one rider: 0.18 + 0.1" );
		Assert.AreEqual( ParkEconomy.WearAmount( bouncy, level, new RideOperation( true, 5, 50 ) ), ParkEconomy.WearAmount( bouncy, level, new RideOperation( true, 9, 50 ) ), "riders are clamped to UsageInfo.MaxCapacity" );
		Assert.AreEqual( (0.55f + 0.1f) * 0.5f * 5, ParkEconomy.WearAmount( bouncy with { MaxCapacity = 0 }, level, new RideOperation( true, 5, 50 ) ), 1e-5f, "no maximum capacity: rider term 0.1" );
		var noCapacityRedLine = level with { RedLineCapacity = 0 };
		Assert.AreEqual( (0.55f + (0.9f * 0.4f + 1.1f) * 0.5f) * 0.5f * 5, ParkEconomy.WearAmount( bouncy, noCapacityRedLine, new RideOperation( true, 2, 50 ) ), 1e-5f, "RedLineCapacity <= riders / 10" );
	}

	[TestMethod]
	public void RidesDoNotWearWithoutARunningScriptOrRiders()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var noScript );
		park.TryBuild( 1100, out var stopped );
		park.TryBuild( 1100, out var empty );
		var rides = new EconomyTestData.RideOperations();
		rides.Set( stopped!.Id, new RideOperation( false, 5, 50 ) );
		rides.Set( empty!.Id, new RideOperation( true, 0, 50 ) );
		park.RideOperations = rides;
		EconomyTestData.AdvanceToTurn( park, 10 * ParkEconomy.WearInterval );
		Assert.AreEqual( 100, noScript!.Repair, "no linked ride script: the ride does not run" );
		Assert.AreEqual( 100, stopped.Repair, "VAR_RUNNING 0" );
		Assert.AreEqual( 100, empty.Repair, "VAR_ONRIDE 0" );
	}

	[TestMethod]
	public void AClosedRideInGoodRepairNeitherWearsNorBreaksDown()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var ride );
		ride!.IsOpen = false;
		var rides = new EconomyTestData.RideOperations();
		rides.Set( ride.Id, new RideOperation( false, 0, 50 ) );
		park.RideOperations = rides;
		EconomyTestData.AdvanceToTurn( park, 10 * ParkEconomy.WearInterval );
		Assert.AreEqual( (100.0, 100.0, false), (ride.Repair, ride.LifeGauge, ride.IsBrokenDown) );
	}

	[TestMethod]
	public void AnEmptyLifeGaugeBreaksARideEvenWhenRepaired()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var ride );
		ride!.LifeGauge = 0.5;
		EconomyTestData.AdvanceToTurn( park, ParkEconomy.RideUpdateInterval - 1 );
		Assert.IsFalse( ride.IsBrokenDown );
		EconomyTestData.AdvanceToTurn( park, ParkEconomy.RideUpdateInterval );
		Assert.IsTrue( ride.IsBrokenDown, "the breakdown check runs every 8 turns" );
		Assert.AreEqual( 100, ride.Repair );
		Assert.AreEqual( 0.5, ride.LifeGauge, "the life gauge only pays 5 when the state of repair causes the breakdown" );
	}

	[TestMethod]
	public void SavesKeepTheExactRepairAndLifeGauge()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var ride );
		ride!.Repair = 42.625;
		ride.LifeGauge = 97.5;
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.IsTrue( restored.TryGetObject( ride.Id, out var copy ) );
		Assert.AreEqual( (42.625, 97.5), (copy.Repair, copy.LifeGauge) );
		var older = json.Replace( "\"Repair\": 42.625,", "" ).Replace( "\"LifeGauge\": 97.5,", "" );
		Assert.AreNotEqual( json, older, "the test removes both fields" );
		var fromOlder = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( older ) ), park.Settings, park.Catalog );
		Assert.IsTrue( fromOlder.TryGetObject( ride.Id, out var old ) );
		Assert.AreEqual( (42.0, 100.0), (old.Repair, old.LifeGauge), "older saves use StateOfRepair and a full life gauge" );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.Replace( "\"Repair\": 42.625", "\"Repair\": 50.5" ) ) ) );
	}

	[TestMethod]
	public void SavesWithTheOldJobTimeFieldAreConvertedToTurns()
	{
		// ORACLE F19: mechanic job time is counted in park turns (BusyTurns). An older save has BusyTicks in tenths of a park hour
		// (360 park seconds each); a turn is 3750 park seconds, so the old value converts rounded up.
		var park = EconomyTestData.Park();
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		mechanic.BusyTicks = 7;
		var text = ParkSaveFile.Serialize( park );
		Assert.IsTrue( text.Contains( "\"BusyTurns\": 7" ) && !text.Contains( "\"BusyTicks\"" ), "new saves write BusyTurns only" );
		Assert.AreEqual( 7L, ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( text ) ), park.Settings, park.Catalog ).Staff.Members.Single( member => member.Id == mechanic.Id ).BusyTicks );

		long Loaded( string field ) => ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( text.Replace( "\"BusyTurns\": 7", field ) ) ), park.Settings, park.Catalog )
			.Staff.Members.Single( member => member.Id == mechanic.Id ).BusyTicks;
		Assert.AreEqual( 0L, Loaded( "\"BusyTicks\": 0" ), "no job" );
		Assert.AreEqual( 1L, Loaded( "\"BusyTicks\": 10" ), "one park hour left is one turn" );
		Assert.AreEqual( 77L, Loaded( "\"BusyTicks\": 800" ), "800 tenths = 80 h = 76.8 turns, rounded up" );
		Assert.AreEqual( 1L, Loaded( "\"BusyTicks\": 1" ), "a started job never rounds down to none" );
		Assert.ThrowsException<InvalidDataException>( () => Loaded( "\"BusyTicks\": -1" ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( text.Replace( "\"BusyTurns\": 7", "\"BusyTurns\": 7, \"BusyTicks\": 7" ) ) ) );
	}

	[TestMethod]
	public void SaveLoadRejectsCorruptOrForeignFiles()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out _ );
		var json = ParkSaveFile.Serialize( park );
		void Rejects( string text ) => Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( text ) ) );
		Rejects( json[..(json.Length / 2)] );
		Rejects( json.Replace( $"\"Version\": {ParkSaveFile.CurrentVersion}", "\"Version\": 1" ) );
		Rejects( json.Replace( "\"opentpw-park\"", "\"other\"" ) );
		Rejects( json.Replace( "\"Tick\": 0,", "\"Tick\": 0, \"Extra\": 1," ) );
		Rejects( json.Replace( "\"EntranceFee\": 20,", "" ) );
		Rejects( json.Replace( "\"StateOfRepair\": 100", "\"StateOfRepair\": 101" ) );
		Rejects( json.Replace( "\"Speed\": \"Normal\"", "\"Speed\": \"Warp\"" ) );
		Rejects( "[]" );
		var data = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) );
		var easy = new BalanceSettings( park.Settings.Standard, null, null, "jungle", true );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( data, easy, park.Catalog ) );
		var unknownObject = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.Replace( "\"InfoId\": 1100", "\"InfoId\": 4242" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( unknownObject, park.Settings, park.Catalog ) );
	}
}
