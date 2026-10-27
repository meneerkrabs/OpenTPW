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
	public void AFeatureIsAFixedItemByItsUiTypeAsTheBuyMenuFillerReadsIt()
	{
		// tp.exe 0x4aaf70 lists a definition only in the tab equal to Info.WhichUIType (0-3) and never reads Research.Category
		EconomyObjectInfo Feature( string keys ) => EconomyObjectCatalog.FromSettings(
			new SamSettings( new[] { SamDocument.Parse( "Info.Id 1650\nUpgrades[0].CostOfUpgrade 100\n" + keys, "test.sam" ) } ), ParkObjectKind.Feature, "features/test.wad" );
		Assert.AreEqual( ParkObjectKind.FixedItem, Feature( "Info.WhichUIType 4\nResearch.Category 3\n" ).Kind );
		Assert.AreEqual( ParkObjectKind.Feature, Feature( "Info.WhichUIType 3\nResearch.Category 0\n" ).Kind );
		Assert.AreEqual( ParkObjectKind.Feature, Feature( "Research.Category 0\n" ).Kind, "no UI type key" );
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
		// A faster speed is the clock scale of the whole simulation (Level scales the elapsed time), not extra economy ticks per fixed tick.
		park.Speed = GameSpeed.Fastest;
		park.AdvanceFixedTick();
		Assert.AreEqual( 1, park.Tick );
		Assert.AreEqual( (0f, 1f, 2f, 4f), (GameSpeedScale.Of( GameSpeed.Paused ), GameSpeedScale.Of( GameSpeed.Normal ), GameSpeedScale.Of( GameSpeed.Fast ), GameSpeedScale.Of( GameSpeed.Fastest )) );
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
		park.TryAdmitVisitor( out _ );
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
	public void TheCreditTestComparesTheAmountWithTheNetWorthLessTheBoughtInstalments()
	{
		// [BIN:TPW-EXE:0x4d06b0 credit test] amount <= net worth (read with age 20) - sum((months - repaid) x monthly) over the bought offers
		var park = EconomyTestData.Park( initialCash: 0 );
		Assert.IsTrue( park.Settings.Loans.All( offer => park.IsLoanAvailable( offer.Index ) == offer.Amount < 10001 ), "the constructor 0x4cf7c0 stores amount < 10,001" );
		park.Litter.Cells = new ParkCellMap( 128, 128 );
		park.RefreshLoanOffers();
		var worth = park.NetWorth( 20 );
		Assert.AreEqual( 32768, worth );
		CollectionAssert.AreEqual( park.Settings.Loans.Where( offer => offer.Amount <= worth ).Select( offer => offer.Index ).ToArray(), park.AvailableLoans.Select( offer => offer.Index ).ToArray() );
		var bought = park.TakeLoan( 2 );
		Assert.IsFalse( park.AvailableLoans.Any( offer => offer.Index == 2 ), "a bought offer is not listed" );
		park.RefreshLoanOffers();
		var owed = bought.Months * (int)bought.MonthlyRepayment;
		CollectionAssert.AreEqual( park.Settings.Loans.Where( offer => offer.Index != 2 && offer.Amount <= worth - owed ).Select( offer => offer.Index ).ToArray(),
			park.AvailableLoans.Select( offer => offer.Index ).ToArray(), "the bought loan's instalments still owed come off the net worth" );
		// [BIN:TPW-EXE:0x4d0750 take loan] taking needs only the bought flag clear
		var large = park.Settings.Loans.First( offer => !park.IsLoanAvailable( offer.Index ) && offer.Index != 2 );
		Assert.AreEqual( large.Amount, park.TakeLoan( large.Index ).OriginalAmount, "an offer that failed the test can still be taken" );
		Assert.ThrowsException<InvalidOperationException>( () => park.TakeLoan( large.Index ), "but not twice" );
	}

	[TestMethod]
	public void TheStoredCreditTestsAreSavedAndAnOlderSaveStartsWithTheConstructors()
	{
		var park = EconomyTestData.Park();
		park.Litter.Cells = new ParkCellMap( 128, 128 );
		park.RefreshLoanOffers();
		var data = ParkSaveFile.Capture( park );
		CollectionAssert.AreEqual( park.AvailableLoans.Select( offer => offer.Index ).ToArray(), data.AvailableLoanOffers!.ToArray() );
		var restored = ParkSaveFile.Restore( data, park.Settings, park.Catalog );
		CollectionAssert.AreEqual( park.AvailableLoans.Select( offer => offer.Index ).ToArray(), restored.AvailableLoans.Select( offer => offer.Index ).ToArray() );
		typeof( ParkSaveFile.Data ).GetProperty( nameof( ParkSaveFile.Data.AvailableLoanOffers ) )!.SetValue( data, null ); // a save written before the field
		var older = ParkSaveFile.Restore( data, park.Settings, park.Catalog );
		CollectionAssert.AreEqual( park.Settings.Loans.Where( offer => offer.Amount < 10001 ).Select( offer => offer.Index ).ToArray(), older.AvailableLoans.Select( offer => offer.Index ).ToArray() );
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
		// [BIN:TPW-EXE:0x4d0370 / 0x49fa00] the last instalment clears the bought flag; the Available tab lists the offer again when a list build's credit test passes
		Assert.IsFalse( park.AvailableLoans.Any( offer => offer.Index == 2 ), "the 18,000 offer's stored test is the constructor's amount < 10,001" );
		park.RefreshLoanOffers();
		Assert.IsFalse( park.AvailableLoans.Any( offer => offer.Index == 2 ), "a headless park is worth nothing" );
		park.Litter.Cells = new ParkCellMap( 128, 128 );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 4 * 21 );
		park.RefreshLoanOffers();
		Assert.IsTrue( park.AvailableLoans.Any( offer => offer.Index == 2 ), "16,384 owned empty cells are worth 32,768" );

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
		// tp.exe 0x00505A10: the training is money out but not a staff cost (the staff costs row +0x1F7F0 takes only the wages)
		Assert.AreEqual( park.Settings.GetMonthlyWage( StaffType.Mechanic, grade ) + park.Settings.GetMonthlyWage( StaffType.Handyman, handyman.Grade ),
			park.Ledger.History[^1][LedgerCategory.StaffCosts], "the mechanic's wage and the dismissed handyman's month" );
		Assert.AreEqual( price * 60L, park.Ledger.History[^1][LedgerCategory.OtherCosts] );
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
		// StaffTimeoutTime 120 + R % 60 units of four turns, leaving on the first turn past them (tp.exe 0x507600, 0x5084f0)
		Assert.IsTrue( park.Staff.Candidates.All( candidate => candidate.ExpiresTick >= ParkCalendar.TickOfTurn( ParkStaff.TurnsPerPoolUnit * 120 + 1 )
			&& candidate.ExpiresTick <= ParkCalendar.TickOfTurn( ParkStaff.TurnsPerPoolUnit * 179 + 1 ) ) );
		Assert.IsTrue( park.Staff.Candidates.Select( candidate => candidate.ExpiresTick ).Distinct().Count() > 1, "each candidate draws its own timeout" );
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
	public void SixThirtyDayMonthsInTheRedMakeTheParkBankrupt()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.OpenPark();
		park.AdvanceMonths( 1 );
		Assert.IsTrue( park.Balance < 0 );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.InTheRed ) );
		var stamp = park.TurnEnteredRed;
		Assert.AreEqual( park.Balance, park.LastBalance );
		Assert.AreEqual( 0, park.MonthsInRed, "the wages took the park into the red at this month end (0x4d0370 counts from the stamp)" );
		var counts = new List<int>();
		while ( !park.IsBankrupt && counts.Count < 12 )
		{
			park.AdvanceMonths( 1 );
			counts.Add( park.MonthsInRed );
			Assert.AreEqual( stamp, park.TurnEnteredRed, "debits in the red keep the stamp" );
			Assert.AreEqual( (ParkCalendar.Turn( park.Tick ) - stamp) * ParkCalendar.SecondsPerTurn / (30 * ParkCalendar.SecondsPerDay), park.MonthsInRed, "whole 30-day months since the stamp" );
		}
		Assert.IsTrue( park.IsBankrupt, string.Join( ", ", counts ) );
		Assert.AreEqual( 6, counts[^1], "more than 5" );
		Assert.IsTrue( counts.Take( counts.Count - 1 ).All( count => count <= 5 ) );
		Assert.AreEqual( 0, events.Count( item => item.Kind == ParkEventKind.BankruptcyWarning ), "the bank's month compares the count only with 5 (0x4d0370): no warnings" );
		Assert.IsTrue( park.IsParkOpen, "the lost park keeps its park-closed word (0x5168f0)" );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.Bankrupt ) );
		var tick = park.Tick;
		var months = events.Count( item => item.Kind == ParkEventKind.MonthEnded );
		park.AdvanceMonths( 2 );
		Assert.IsTrue( park.Tick > tick, "the turn counter and the calendar go on in world state 4" );
		Assert.AreEqual( months + 2, events.Count( item => item.Kind == ParkEventKind.MonthEnded ), "the bank's month goes on" );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.Bankrupt ), "the handler acts once: the world is already in state 4 (0x4d02d0)" );
		Assert.IsTrue( park.MonthsInRed > 5 );
		Assert.AreEqual( 3, events.Count( item => item.Kind == ParkEventKind.BankruptcyMessage ), "the bank posts message 2 at every month end while the condition holds (0x4d0370)" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Bankrupt, park.TryBuild( 1100, out _ ) );

		var recovered = EconomyTestData.Park( initialCash: 100 );
		EconomyTestData.HireBest( recovered, StaffType.Researcher );
		recovered.AdvanceMonths( 2 );
		recovered.TakeLoan( 0 );
		recovered.AdvanceMonths( 1 );
		Assert.AreEqual( 0, recovered.MonthsInRed );
	}

	[TestMethod]
	public void ALostParkStopsItsThingsButKeepsTheCalendarAndTheBank()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		var researcher = EconomyTestData.HireBest( park, StaffType.Researcher );
		var ride = park.RegisterExisting( 1100 );
		park.OpenPark();
		while ( !park.IsBankrupt )
			park.AdvanceMonths( 1 );
		var energy = researcher.Energy;
		var repair = ride.Repair;
		var researched = park.Research.ProgressEntries.Sum( entry => entry.Points );
		var wages = events.Count( item => item.Kind == ParkEventKind.WagesPaid );
		park.AdvanceMonths( 1 );
		Assert.AreEqual( energy, researcher.Energy, "the staff's thing update stops" );
		Assert.AreEqual( researched, park.Research.ProgressEntries.Sum( entry => entry.Points ), "research comes from the researchers' thing update" );
		Assert.AreEqual( repair, ride.Repair, "the ride update stops" );
		Assert.AreEqual( wages + 1, events.Count( item => item.Kind == ParkEventKind.WagesPaid ), "the staff manager's month goes on" );
	}

	[TestMethod]
	public void AWinningSequenceKeepsTheParkFromBankruptcy()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		EconomyTestData.HireBest( park, StaffType.Researcher );
		var winning = true;
		park.WinningSequenceActive = () => winning;
		park.AdvanceMonths( 1 );
		while ( park.MonthsInRed < ParkEconomy.MonthsInRedForBankruptcy )
			park.AdvanceMonths( 1 );
		park.AdvanceMonths( 1 );
		Assert.IsFalse( park.IsBankrupt, "0x516c00: the handler skips the message while the winning sequence's thing exists" );
		winning = false;
		park.AdvanceMonths( 1 );
		Assert.IsTrue( park.IsBankrupt, "the next month's message type 2 is acted on" );
	}

	[TestMethod]
	public void GuestsPayAtTheGateShopsAndSideshows()
	{
		var park = EconomyTestData.Park();
		park.OpenPark();
		Assert.IsTrue( park.TryAdmitVisitor( out var fee ) );
		Assert.AreEqual( 20, fee );
		park.SetEntranceFee( 35 );
		Assert.IsTrue( park.TryAdmitVisitor( out fee ) );
		Assert.AreEqual( 55, park.Ledger.CurrentTotals[LedgerCategory.GateTakings] );
		Assert.AreEqual( 2, park.Counters[ParkCounters.Admissions] );

		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1203, out var shop ) );
		Assert.AreEqual( 50000 + 55 - 650, park.Balance );
		Assert.IsTrue( park.TryBuy( shop!.Id, 100, out var price ) );
		Assert.AreEqual( 30, price );
		Assert.AreEqual( 30, park.Ledger.CurrentTotals[LedgerCategory.ShopTakings] );
		Assert.AreEqual( 650 + 20, park.Ledger.CurrentTotals[LedgerCategory.OtherCosts] );
		Assert.AreEqual( 0, park.LitterItems, "a sale fills the buyer's litter meter, not the park (tp.exe 0x004FE1E0)" );
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
	public void MechanicsSkipACondemnedRideAndRepairABrokenRideBeforeItsUpgrade()
	{
		// ORACLE F19: the arrival 0xd9c0c starts a job only for state 1 or 2 or a call, so an uncalled condemned ride (state 4, life gauge 0) gets no mechanic.
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var condemned );
		park.TryBuild( 1100, out var broken );
		condemned!.LifeGauge = 0;
		condemned.IsBrokenDown = true;
		broken!.IsBrokenDown = true;
		broken.Repair = 50;
		broken.PendingLevel = 1;
		Assert.AreEqual( RideObjectStates.Condemned, ParkEconomy.ObjectState( condemned ) );
		Assert.AreEqual( RideObjectStates.BrokenDown, ParkEconomy.ObjectState( broken ), "a breakdown outranks a purchased upgrade" );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var second = EconomyTestData.HireBest( park, StaffType.Mechanic );
		park.Advance( ParkCalendar.TicksForHours( 2 ) );
		Assert.AreEqual( 0, condemned.MechanicId, "the condemned ride is no job" );
		Assert.IsTrue( broken.MechanicId == mechanic.Id || broken.MechanicId == second.Id );
		var work = park.Settings[StaffType.Mechanic].WorkDuration[mechanic.Grade];
		EconomyTestData.AdvanceToTurn( park, park.Turn + work + 2 );
		Assert.IsFalse( broken.IsBrokenDown, "the breakdown is repaired first" );
		Assert.AreEqual( 0, broken.Level, "the repair does not install the upgrade" );
		Assert.AreEqual( 1, broken.PendingLevel );
		Assert.AreEqual( 0, condemned.MechanicId );
	}

	[TestMethod]
	public void ACalledCondemnedRideIsRepaired()
	{
		// tp.exe 0x4da740 (0x4da7d5): the arrival starts the work for state 1, state 2 or the call +0x64, whatever else the state is, and the finder
		// 0x4daa90 selects a called ride in any state; the repair 0x4df8f0 then sets the gauge to 100 and the state to 0.
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out var condemned );
		condemned!.LifeGauge = 0;
		condemned.IsBrokenDown = true;
		condemned.Repair = 40;
		Assert.AreEqual( RideObjectStates.Condemned, ParkEconomy.ObjectState( condemned ) );
		Assert.IsTrue( park.RequestMaintenance( condemned.Id, true ) );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var repaired = 0;
		park.EventRaised += item => { if ( item.Kind == ParkEventKind.RideRepaired && item.InstanceId == condemned.Id ) repaired++; };
		park.Advance( ParkCalendar.TicksForHours( 2 ) );
		Assert.AreEqual( mechanic.Id, condemned.MechanicId, "the call makes the condemned ride a job" );
		var work = park.Settings[StaffType.Mechanic].WorkDuration[mechanic.Grade];
		EconomyTestData.AdvanceToTurn( park, park.Turn + work + 2 );
		Assert.AreEqual( 1, repaired );
		Assert.AreEqual( 0, condemned.MechanicId );
		Assert.IsFalse( condemned.MaintenanceRequested, "the repair clears the call (+0x64 = 0)" );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 100 );
		Assert.AreEqual( (0, 1), (condemned.MechanicId, repaired), "without a new call the ride, whatever its gauges, gets no second job" );
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
		var spent = ride.TotalSpent;
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuyUpgrade( ride.Id ) );
		Assert.AreEqual( before, park.Balance, "tp.exe 0x4af480 requests the upgrade without a debit" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.UpgradeInProgress, park.TryBuyUpgrade( ride.Id ) );
		park.AdvanceDays( 10 );
		Assert.AreEqual( 1, ride.Level );
		Assert.AreEqual( spent + 400, ride.TotalSpent, "the installation debits the level's cost (0x4df8f0)" );
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
	public void HandymenSweepLitterCellByCell()
	{
		var park = EconomyTestData.Park();
		park.OpenPark();
		foreach ( var (x, y) in new[] { (5, 2), (3, 7), (3, 1) } )
			Assert.IsTrue( park.Litter.Drop( x, y, (LitterType)1 ) );
		Assert.IsFalse( park.Litter.Drop( 3, 7, LitterType.Stinkbomb ), "a cell holds one item" );
		Assert.AreEqual( 3, park.LitterItems );
		var rating = park.ParkRating;
		park.AdvanceDays( 1 );
		Assert.AreEqual( 3, park.LitterItems, "nothing but a handyman removes litter" );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		var work = park.Settings[StaffType.Handyman].WorkDuration[handyman.Grade];
		AdvanceTurns( park, 1 );
		Assert.AreEqual( (3, 1), handyman.LitterCell, "the first littered cell in the finder's order, x outer and y inner" );
		Assert.AreEqual( handyman.Id, park.Litter.CollectorAt( 3, 1 ) );
		Assert.AreEqual( StaffState.Working, handyman.State );
		AdvanceTurns( park, work );
		Assert.AreEqual( 3, park.LitterItems, "the sweep lasts WorkDuration turns and the update that ends it" );
		AdvanceTurns( park, 1 );
		Assert.AreEqual( 2, park.LitterItems );
		Assert.AreEqual( LitterType.None, park.Litter.TypeAt( 3, 1 ) );
		AdvanceTurns( park, 3 * (work + 2) );
		Assert.AreEqual( 0, park.LitterItems );
		Assert.IsTrue( park.ParkRating >= rating );
	}

	private static void AdvanceTurns( ParkEconomy park, int turns ) =>
		park.Advance( ParkCalendar.TickOfTurn( park.Turn + turns ) - park.Tick );

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

	/// <summary>Objectives over <see cref="EconomyTestData.ChallengesText"/> plus a second type-20 follow-up (index 2, another ride) listed before index 8.</summary>
	private static ParkObjectives TwoFollowupObjectives() => new( new BalanceSettings(
		new SamSettings( new[] { SamDocument.Parse( EconomyTestData.StandardText().Replace( "ChallengesInThisLevel[0].ChallengeType 1\nChallengesInThisLevel[1].ChallengeType 7\nChallengesInThisLevel[2].ChallengeType 8\n",
			"ChallengesInThisLevel[0].ChallengeType 7\nChallengesInThisLevel[1].ChallengeType 2\nChallengesInThisLevel[2].ChallengeType 8\n" ), "Standard.sam" ) } ),
		new SamSettings( new[] { SamDocument.Parse( EconomyTestData.ChallengesText
			+ "Challenges[2].Type 20\nChallenges[2].FollowupType 0\nChallenges[2].TargetTime 90\nChallenges[2].TargetVal 5\nChallenges[2].TargetObj 1150\nChallenges[2].TargetObj2 0\nChallenges[2].TargetStaffType 0\nChallenges[2].Prize 1000\nChallenges[2].CheckAtEndOnly 0\nChallenges[2].Independent 0\n", "Challenges.sam" ) } ),
		null, "jungle", false ), EconomyTestData.Catalog() );

	[TestMethod]
	public void TheFollowupIsTheFirstOpenSlotOfItsTypeWhateverItsObject()
	{
		// tp.exe 0x4d2b60 compares only the type, scanning the slots from the first: index 2 (another ride) comes before index 8 (the same ride)
		var objectives = TwoFollowupObjectives();
		Assert.AreEqual( 7, objectives.AdvanceDay( 540, _ => 0 ).Single().Index );
		objectives.Accept( 540, 0 );
		var results = objectives.AdvanceDay( 541, definition => definition.Type == 18 ? 1 : 0 ).ToList();
		Assert.AreEqual( ParkEventKind.ChallengeCompleted, results[0].Kind );
		Assert.AreEqual( (ParkEventKind.ChallengeOffered, 2), (results[1].Kind, results[1].Index) );
	}

	[TestMethod]
	public void ChallengeType18ComparesTheBestCoastersCountedSectionsWithoutABaseline()
	{
		// tp.exe 0x4d1873: TargetVal <= the unsigned maximum of the counted sections (+0x108) over the coasters of TargetObj (0x4cc810), 0 without one
		var park = EconomyTestData.Park();
		var definition = park.Settings.Challenges[7] with { TargetValue = 12 };
		long Measure() => park.Objectives.Measure( definition, park.Counters, park.Staff, park.Objects, park.GuestStatistics, park.CoasterCountedSections );
		Assert.AreEqual( 0, Measure(), "no coaster host" );
		var sections = new Dictionary<int, long> { [1180] = 12, [1185] = 30 };
		park.CoasterCountedSections = infoId => infoId == 0 ? sections.Values.Max() : sections.GetValueOrDefault( infoId );
		Assert.AreEqual( 12, Measure() );
		Assert.IsFalse( ParkObjectives.IsCountSinceAcceptance( 18 ), "no baseline: an existing coaster counts at once" );
		Assert.AreEqual( 12, ParkObjectives.Target( definition ) );
		Assert.AreEqual( 0, ParkObjectives.Target( definition with { TargetValue = 0 } ), "TargetVal 0 is not raised, so it passes with no coaster" );
	}

	[TestMethod]
	public void ChallengeTypes14And19ReadTheCoastersAnd22CountsRideUsesLike20()
	{
		// tp.exe 0x4d1660: 14 is TargetVal <= 0x4cc810 for any coaster, 19 is 0x4cc810(TargetObj) > 2; the ride-use event 0x501db0 adds to 17, 20 and 22 alike
		var park = EconomyTestData.Park();
		var twenty = park.Settings.Challenges[8];
		park.CoasterCountedSections = infoId => infoId switch { 0 => 9, 1180 => 3, _ => 0 };
		long Measure( ChallengeDefinition definition ) => park.Objectives.Measure( definition, park.Counters, park.Staff, park.Objects, park.GuestStatistics, park.CoasterCountedSections );
		Assert.AreEqual( 9, Measure( twenty with { Type = 14, TargetObject = 1180 } ), "type 14 reads any coaster" );
		var nineteen = twenty with { Type = 19, TargetValue = 50 };
		Assert.AreEqual( 3, Measure( nineteen ) );
		Assert.AreEqual( 3, ParkObjectives.Target( nineteen ), "more than 2, whatever TargetVal" );
		Assert.IsTrue( ParkObjectives.IsSupportedType( 14 ) && ParkObjectives.IsSupportedType( 22 ) && !ParkObjectives.IsSupportedType( 23 ) && !ParkObjectives.IsSupportedType( 26 ) );
		Assert.IsTrue( ParkObjectives.IsCountSinceAcceptance( 22 ) );
		park.Counters.Add( ParkCounters.RideUses( 1180 ), 4 );
		Assert.AreEqual( Measure( twenty ), Measure( twenty with { Type = 22 } ) );
	}

	[TestMethod]
	public void DecliningAFollowupPaysHalfItsPrize()
	{
		// tp.exe 0x4d2ec0: a declined challenge that is not independent credits prize / 2 (0x4d0190); an independent one pays nothing
		var park = EconomyTestData.Park(); park.OpenPark();
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.AdvanceDays( 540 );
		var balance = park.Balance;
		park.DeclineChallenge();
		Assert.AreEqual( balance, park.Balance, "challenge 1 is independent" );
		park.AdvanceDays( 270 );
		park.AcceptChallenge();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1180, out _ ) );
		park.CoasterCountedSections = infoId => infoId is 0 or 1180 ? 1 : 0;
		park.AdvanceDays( 1 );
		Assert.AreEqual( 8, park.Objectives.Current!.DefinitionIndex );
		balance = park.Balance;
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		park.DeclineChallenge();
		Assert.AreEqual( balance + 15000, park.Balance );
		Assert.AreEqual( 15000, events.Single( item => item.Kind == ParkEventKind.ChallengeDeclined ).Amount );
	}

	/// <summary>Advances whole park turns (the challenge system's times are exact park seconds, not day starts).</summary>
	private static void Turns( ParkEconomy park, int turns ) => park.Advance( ParkCalendar.TickOfTurn( ParkCalendar.Turn( park.Tick ) + turns ) - park.Tick );

	[TestMethod]
	public void ChallengesAreOfferedCompletedAndFollowedUp()
	{
		var park = EconomyTestData.Park(); park.OpenPark();
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
		// type 18 (tp.exe 0x4d1873) reads the most counted sections of the coasters 1180, which the level supplies
		park.CoasterCountedSections = infoId => infoId is 0 or 1180 ? 1 : 0;
		var balance = park.Balance;
		park.AdvanceDays( 1 );
		Assert.AreEqual( balance, park.Balance, "a part with a follow-up pays no prize (0x4d2fb0)" );
		Assert.AreEqual( (7, 0L), events.Single( item => item.Kind == ParkEventKind.ChallengeCompleted ) is var won ? (won.InfoId, won.Amount) : default );
		Assert.AreEqual( 8, park.Objectives.Current!.DefinitionIndex, "follow-up type 20 for the same ride" );
		park.AcceptChallenge();
		park.RecordRideUse( minecart!.Id );
		park.AdvanceDays( 1 );
		Assert.AreEqual( 8, park.Objectives.Current!.DefinitionIndex );
		park.RecordRideUse( minecart.Id );
		park.AdvanceDays( 1 );
		Assert.IsNull( park.Objectives.Current );
		Assert.AreEqual( 30000, park.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherIncome ) + park.Ledger.History.Sum( month => month[LedgerCategory.OtherIncome] ), "only the chain's last part pays" );
		park.AdvanceDays( 270 );
		Assert.AreEqual( 1, park.Objectives.Current!.DefinitionIndex );
		park.DeclineChallenge();
		Assert.IsFalse( park.Objectives.Finished.Contains( 1 ), "a second decline does not forfeit: the original's decline never raises the count (0x4d2ec0)" );
		CollectionAssert.AreEquivalent( new[] { 8 }, park.Objectives.Finished.ToArray(), "0x4d2fb0 marked the follow-up 8 won, not the chain head 7" );
		park.AdvanceDays( 600 );
		Assert.AreEqual( 7, park.Objectives.Current!.DefinitionIndex, "the won chain head stays open and is offered again" );

		var failing = EconomyTestData.Park(); failing.OpenPark();
		failing.AdvanceDays( 540 );
		failing.AcceptChallenge();
		var failed = new List<ParkEvent>();
		failing.EventRaised += failed.Add;
		failing.AdvanceDays( 60 );
		// the deadline is the accept's turn second plus 60 days, tested on every turn (0x4d1e90): the day's first tick is a turn short of it
		Turns( failing, 2 );
		Assert.IsNull( failing.Objectives.Current );
		Assert.AreEqual( 1, failed.Single( item => item.Kind == ParkEventKind.ChallengeFailed ).InfoId );
		Assert.IsFalse( failing.Objectives.Finished.Contains( 1 ), "a lost challenge is not completed (0x4d3140)" );
		Assert.AreEqual( 600 + 270, failing.Objectives.NextOfferDay );
		failing.AdvanceDays( 270 );
		Turns( failing, 2 );
		Assert.IsNotNull( failing.Objectives.Current, "the next offer comes 270 days later" );
		for ( var offer = 0; offer < 20 && failing.Objectives.Current?.DefinitionIndex != 1; offer++ )
		{
			failing.DeclineChallenge();
			failing.AdvanceDays( 270 );
			Turns( failing, 2 );
		}
		Assert.AreEqual( 1, failing.Objectives.Current?.DefinitionIndex, "the lost challenge is offered again" );
	}

	[TestMethod]
	public void GoldenTicketsAreCheckedEvery100TurnsInFullSimulationOnly()
	{
		foreach ( var mode in new[] { ParkGameMode.FullSimulation, ParkGameMode.InstantAction } )
		{
			var park = EconomyTestData.Park( mode: mode );
			park.OpenPark();
			for ( var visitor = 0; visitor < 101; visitor++ )
				park.TryAdmitVisitor( out _ );
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

	[TestMethod]
	public void RecentVisitorsCountTheOpenMonthAndNeedTheCloseBefore()
	{
		// tp.exe 0x004C94D0 / 0x004D4BC0: lifetime visitors minus the snapshot at the close RecentVisitorMonths (6) back, so the visitors
		// of the open month count; the seventh close back must exist, and the park clock must be more than 6 exact 30-day months old.
		bool Won( long[] months, long openMonth, long dropped = 0, long days = 400 )
		{
			var park = EconomyTestData.Park();
			park.Counters.Add( ParkCounters.Admissions, dropped + months.Sum() + openMonth );
			return park.Objectives.CheckGoldenTickets( park.Counters, new StatGuests( 0, 0 ), park.Research, 0, months, dropped, days * ParkCalendar.SecondsPerDay )
				.Contains( GoldenTicketKind.RecentVisitors );
		}
		var seven = new long[] { 1000, 0, 0, 0, 0, 0, 300 };
		Assert.IsFalse( Won( seven, openMonth: 50 ), "300 + 50 is not above 350" );
		Assert.IsTrue( Won( seven, openMonth: 51 ), "the open month counts" );
		Assert.IsFalse( Won( new long[] { 0, 0, 0, 0, 0, 400 }, openMonth: 0 ), "six closes: the snapshot six closes back does not exist" );
		Assert.IsTrue( Won( seven, openMonth: 51, dropped: 5000 ), "dropped history is part of every snapshot" );
		Assert.IsFalse( Won( seven, openMonth: 51, days: 6 * 30 + 29 ), "6 whole 30-day months is not more than 6" );
		Assert.IsTrue( Won( seven, openMonth: 51, days: 7 * 30 ) );
	}

	private static bool TicketWon( GoldenTicketKind kind, int visitors = 0, int people = 0, float average = 0, long profit = 0, long recentVisitors = 0,
		ParkRecordKind? record = null, long recordValue = 0, long? coveredCells = null )
	{
		var park = EconomyTestData.Park();
		park.Counters.Add( ParkCounters.Admissions, visitors + recentVisitors );
		if ( record is { } kindOfRecord )
			park.Counters.Max( ParkCounters.Record( kindOfRecord, 0 ), recordValue );
		// seven closed months (RecentVisitorMonths 6 needs the seventh close back); the recent visitors came after the first close
		var months = new long[] { visitors, 0, 0, 0, 0, 0, recentVisitors };
		return park.Objectives.CheckGoldenTickets( park.Counters, new StatGuests( people, average ), park.Research, profit, months, coveredCells: coveredCells ).Contains( kind );
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
		// the big park (0x4d4e50): the covered cells (0x4cd7f0) above MinCellsCovered (2000), not MinCellsOwned
		Assert.IsFalse( TicketWon( GoldenTicketKind.BigPark, coveredCells: 2000 ) );
		Assert.IsTrue( TicketWon( GoldenTicketKind.BigPark, coveredCells: 2001 ) );
		Assert.IsFalse( TicketWon( GoldenTicketKind.BigPark, record: ParkRecordKind.CellsOwned, recordValue: 3001 ), "the owned-cells record is not read" );
		Assert.IsFalse( TicketWon( GoldenTicketKind.BigPark ), "no cell map" );
	}

	[DataTestMethod]
	[DataRow( 0L, ParkGameMode.FullSimulation, true )]
	[DataRow( 1L, ParkGameMode.FullSimulation, false )]
	[DataRow( 99L, ParkGameMode.FullSimulation, false )]
	[DataRow( 100L, ParkGameMode.FullSimulation, true )]
	[DataRow( 101L, ParkGameMode.FullSimulation, false )]
	[DataRow( 8433L, ParkGameMode.FullSimulation, false )]
	[DataRow( 648000L, ParkGameMode.FullSimulation, true )]
	[DataRow( 100L, ParkGameMode.InstantAction, false )]
	[DataRow( 0L, ParkGameMode.InstantAction, false )]
	public void TheGoldenTicketCheckRunsEvery100thTurnInFullSimulation( long turn, ParkGameMode mode, bool runs )
	{
		// [BIN:STP-PPC:0x100D67F0 world update] turn % 100 == 0 (unsigned) calls the dispatcher 0x100D2F1C, which checks only for game type 0; about 4.3 park days (25 s at normal speed) apart (oracle F31)
		Assert.AreEqual( runs, ParkEconomy.RunsGoldenTicketCheck( turn, mode ) );
	}

	[TestMethod]
	public void TheGoldenTicketCheckSeesTheStateAfterTheCalendarUpdateOfItsTurn()
	{
		// The world update runs the calendar update (and with it the bank's year reset, a message handled at once) before the ticket check.
		// Turn 648,000 is the first turn of 2077 and a multiple of 100: the year's profit of 20,000 (above the jungle's 15,000) was reset
		// by the year message, so the check of that turn finds none; the check at turn 100 of a running year finds it.
		ParkEconomy Prepared( long startTurn )
		{
			var park = EconomyTestData.Park( initialCash: 100_000_000 );
			park.StartAtOriginalTurn( startTurn );
			park.ImportOriginalBank( 100_000_000, 10, 20000, Array.Empty<LoanAccount>() );
			return park;
		}
		var running = Prepared( 99 );
		running.Advance( ParkCalendar.TickOfTurn( 100 ) - running.Tick );
		CollectionAssert.AreEqual( new[] { GoldenTicketKind.ProfitYear }, running.Objectives.GoldenTickets.ToArray(), "a check in the middle of a year sees the profit" );

		var yearChange = Prepared( 647_999 );
		yearChange.Advance( ParkCalendar.TickOfTurn( 648_000 ) - yearChange.Tick );
		Assert.AreEqual( 648_000, yearChange.Turn );
		Assert.AreEqual( 0, yearChange.Ledger.ProfitThisYear, "the year message reset the profit" );
		Assert.AreEqual( 0, yearChange.Objectives.GoldenTickets.Count, "the ticket check of the year's first turn runs after the reset" );
	}

	[TestMethod]
	public void GoldenTicketsAndKeys()
	{
		var park = EconomyTestData.Park();
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		park.OpenPark();
		for ( var visitor = 0; visitor < 101; visitor++ )
			park.TryAdmitVisitor( out _ );
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
			park.TryAdmitVisitor( out _ );
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
