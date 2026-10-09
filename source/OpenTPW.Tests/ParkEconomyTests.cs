using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
		Assert.AreEqual( new ParkDate( 1, 2, 1, 0 ), ParkCalendar.ToDate( ParkCalendar.TicksPerDay * 30L ) );
		Assert.AreEqual( new ParkDate( 2, 1, 1, 13 ), ParkCalendar.ToDate( ParkCalendar.TicksPerDay * 360L + ParkCalendar.TicksPerHour * 13 ) );
		Assert.AreEqual( new ParkDate( 1, 12, 30, 23 ), ParkCalendar.ToDate( ParkCalendar.TicksPerDay * 360L - 1 ) );
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
		Assert.AreEqual( 250, month.MoneyIn );
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
	public void LoanRepaymentsFollowTermsAndInterest()
	{
		Assert.AreEqual( 2777, LoanMath.MonthlyRepayment( 100000, 0, 36 ) );
		Assert.AreEqual( 750, LoanMath.MonthlyRepayment( 18000, 0, 24 ) );
		Assert.AreEqual( 100000, LoanMath.TotalPayable( 100000, 0, 36 ) );
		Assert.AreEqual( 3716, LoanMath.MonthlyRepayment( 100000, 20, 36 ) );
		var total = LoanMath.TotalPayable( 100000, 20, 36 );
		Assert.IsTrue( total > 133000 && total < 134000, total.ToString() );

		var park = EconomyTestData.Park( initialCash: 0 );
		var account = park.TakeLoan( 2 );
		Assert.AreEqual( 18000, park.Balance );
		Assert.AreEqual( LoanMath.MonthlyRepayment( 18000, 20, 24 ), account.MonthlyRepayment );
		Assert.IsFalse( park.AvailableLoans.Any( offer => offer.Index == 2 ) );
		Assert.ThrowsException<InvalidOperationException>( () => park.TakeLoan( 2 ) );
		park.AdvanceDays( 30 * 24 );
		Assert.AreEqual( 0, park.Loans.Count );
		Assert.AreEqual( 18000 - LoanMath.TotalPayable( 18000, 20, 24 ), park.Balance );
		Assert.AreEqual( LoanMath.TotalPayable( 18000, 20, 24 ), park.Ledger.History.Sum( month => month[LedgerCategory.LoanPayments] ) );
		Assert.AreEqual( 0, park.Ledger.History.Sum( month => month.MoneyIn ) );
		Assert.IsTrue( park.AvailableLoans.Any( offer => offer.Index == 2 ) );

		var early = EconomyTestData.Park( initialCash: 5000 );
		early.TakeLoan( 1 );
		early.AdvanceDays( 30 );
		var remaining = early.Loans[0].RemainingBalance;
		Assert.AreEqual( 50000 + LoanMath.MonthlyInterest( 50000, 20 ) - early.Loans[0].MonthlyRepayment, remaining );
		var before = early.Balance;
		Assert.IsTrue( early.TryRepayLoan( 1 ) );
		Assert.AreEqual( before - remaining, early.Balance );
		Assert.AreEqual( 0, early.Loans.Count );
		var instant = EconomyTestData.Park( mode: ParkGameMode.InstantAction );
		Assert.AreEqual( 0, instant.AvailableLoans.Count() );
	}

	[TestMethod]
	public void WagesArePaidAtMonthEndAndTrainingRaisesGrades()
	{
		var park = EconomyTestData.Park();
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		var wages = park.Settings.GetMonthlyWage( StaffType.Mechanic, mechanic.Grade ) + park.Settings.GetMonthlyWage( StaffType.Handyman, handyman.Grade );
		park.AdvanceDays( 29 );
		Assert.AreEqual( 50000, park.Balance );
		park.AdvanceDays( 1 );
		Assert.AreEqual( 50000 - wages, park.Balance );
		Assert.AreEqual( wages, park.Ledger.History[0][LedgerCategory.StaffCosts] );
		park.Fire( handyman.Id );
		Assert.AreEqual( 1, park.Staff.Members.Count );

		var grade = mechanic.Grade;
		var price = park.Settings[StaffType.Mechanic].PoundsPerTrainingPoint[grade];
		park.SetTrainingBudget( StaffType.Mechanic, price * 60L );
		var balance = park.Balance;
		park.AdvanceDays( 30 );
		Assert.AreEqual( 60, mechanic.TrainingPoints );
		Assert.AreEqual( balance - park.Settings.GetMonthlyWage( StaffType.Mechanic, grade ) - price * 60L, park.Balance );
		park.AdvanceDays( 30 );
		Assert.AreEqual( grade + 1, mechanic.Grade );
		Assert.AreEqual( 0, mechanic.TrainingPoints );
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
				park.Advance( ParkCalendar.TicksPerHour );
			EconomyTestData.HireBest( park, StaffType.Guard );
		}
		while ( !park.Staff.Candidates.Any( candidate => candidate.Type == StaffType.Guard ) )
			park.Advance( ParkCalendar.TicksPerHour );
		Assert.IsFalse( park.Staff.CanHire( StaffType.Guard ) );
		Assert.ThrowsException<InvalidOperationException>( () => EconomyTestData.HireBest( park, StaffType.Guard ) );
	}

	[TestMethod]
	public void SixMonthsInTheRedMakeTheParkBankrupt()
	{
		var park = EconomyTestData.Park( initialCash: 100 );
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		EconomyTestData.HireBest( park, StaffType.Researcher );
		park.OpenPark();
		park.AdvanceDays( 30 );
		Assert.IsTrue( park.Balance < 0 );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.InTheRed ) );
		Assert.AreEqual( 1, park.MonthsInRed );
		park.AdvanceDays( 30 * 4 );
		CollectionAssert.AreEqual( new long[] { 3, 5 }, events.Where( item => item.Kind == ParkEventKind.BankruptcyWarning ).Select( item => item.Amount ).ToArray() );
		Assert.IsFalse( park.IsBankrupt );
		park.AdvanceDays( 30 );
		Assert.IsTrue( park.IsBankrupt );
		Assert.IsFalse( park.IsParkOpen );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.Bankrupt ) );
		var tick = park.Tick;
		park.AdvanceDays( 30 );
		Assert.AreEqual( tick, park.Tick );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Bankrupt, park.TryBuild( 1100, out _ ) );

		var recovered = EconomyTestData.Park( initialCash: 100 );
		EconomyTestData.HireBest( recovered, StaffType.Researcher );
		recovered.AdvanceDays( 30 * 2 );
		recovered.TakeLoan( 0 );
		recovered.AdvanceDays( 30 );
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
		park.AdvanceDays( 30 );
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
		Assert.AreEqual( 250, park.ScrapValue( ride! ) );
		park.AdvanceDays( 360 );
		Assert.AreEqual( 150, park.ScrapValue( ride! ) );
		var gates = park.RegisterExisting( 1601 );
		Assert.IsTrue( gates.Imported );
		var balance = park.Balance;
		Assert.AreEqual( 150, park.Sell( ride!.Id ) );
		Assert.AreEqual( balance + 150, park.Balance );
		Assert.IsFalse( park.TryGetObject( ride.Id, out _ ) );
	}

	[TestMethod]
	public void RidesWearOutAndMechanicsRepairAndUpgradeThem()
	{
		var park = EconomyTestData.Park();
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		park.TryBuild( 1100, out var ride );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotResearched, park.TryBuyUpgrade( ride!.Id ) );
		park.AdvanceDays( 15 );
		Assert.AreEqual( 25, ride.StateOfRepair );
		park.AdvanceDays( 1 );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.RideWorn ) );
		park.AdvanceDays( 4 );
		Assert.IsTrue( ride.IsBrokenDown );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.RideBrokeDown ) );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		park.Advance( ParkCalendar.TicksPerHour );
		Assert.AreEqual( mechanic.Id, ride.MechanicId );
		park.Advance( park.Settings[StaffType.Mechanic].WorkDuration[mechanic.Grade] * (long)ParkCalendar.TicksPerHour );
		Assert.IsFalse( ride.IsBrokenDown );
		Assert.AreEqual( 100, ride.StateOfRepair );
		Assert.AreEqual( 0, mechanic.AssignedInstanceId );

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
		public int AverageHappiness => 0;
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
		park.AdvanceDays( 1 );
		var perHour = 60 * ParkEconomy.LitterScale / park.Settings[StaffType.Handyman].WorkDuration[handyman.Grade];
		Assert.AreEqual( Math.Max( 0, 50 * ParkEconomy.LitterScale - 24 * perHour ), park.LitterScaled );
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
		var daily = (long)park.Settings.ResearchAbility[researcher.Grade] * ParkResearch.PointScale;
		park.AdvanceDays( 1 );
		var minecart = park.Research.Items.Single( item => item.InfoId == 1180 );
		Assert.AreEqual( daily * 100 / 110, park.Research.GetProgress( minecart ), "ride effort 100 of 100 + upgrade 10" );
		Assert.AreEqual( daily * 10 / 110, park.Research.GetProgress( park.Research.Current( ResearchCategory.Upgrade )! ) );

		var days = 1;
		while ( !park.Research.IsAvailable( 1180 ) && days++ < 2000 )
			park.AdvanceDays( 1 );
		Assert.IsTrue( park.Research.IsAvailable( 1180 ) );
		Assert.AreEqual( (int)Math.Ceiling( 800.0 * ParkResearch.PointScale / (daily * 100 / 110) ), days );
		Assert.AreEqual( 1, events.Count( item => item.Kind == ParkEventKind.ItemResearched && item.InfoId == 1180 ) );
		Assert.IsTrue( park.Research.IsGroupOpen( ResearchCategory.Ride, 2 ) );
		Assert.AreEqual( 1106, park.Research.Current( ResearchCategory.Ride )!.InfoId );
		Assert.AreEqual( 1, park.Counters[ParkCounters.Researched( ResearchCategory.Ride )] );

		park.SetResearchEffort( ResearchCategory.Upgrade, 100 );
		while ( !park.Research.IsAllResearched && park.Tick < 3600L * ParkCalendar.TicksPerDay )
			park.AdvanceDays( 1 );
		Assert.IsTrue( park.Research.IsAllResearched );
		Assert.IsTrue( park.Research.IsAvailable( 1500 ), "add-on after its target ride" );

		var automatic = EconomyTestData.Park( mode: ParkGameMode.InstantAction );
		automatic.AdvanceDays( 1 );
		Assert.IsTrue( automatic.Research.GetProgress( automatic.Research.Current( ResearchCategory.Ride )! ) > 0, "Instant Action research is automatic" );
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
		research.AdvanceDay( 900L * ParkResearch.PointScale );
		Assert.IsTrue( research.IsAvailable( 1120 ) );
		Assert.AreEqual( 1130, research.Current( ResearchCategory.Ride )!.InfoId );
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
	public void GoldenTicketsAreCheckedEvery100TicksInFullSimulationOnly()
	{
		foreach ( var mode in new[] { ParkGameMode.FullSimulation, ParkGameMode.InstantAction } )
		{
			var park = EconomyTestData.Park( mode: mode );
			park.OpenPark();
			for ( var visitor = 0; visitor < 100; visitor++ )
				park.TryAdmitVisitor( 100, out _ );
			park.Advance( ParkEconomy.GoldenTicketCheckInterval - 1 );
			Assert.AreEqual( 0, park.Objectives.GoldenTickets.Count, $"{mode}: not checked before tick 100" );
			park.Advance( 1 );
			Assert.AreEqual( mode == ParkGameMode.FullSimulation ? 1 : 0, park.Objectives.GoldenTickets.Count, $"{mode}: checked at tick 100" );
		}
	}

	[TestMethod]
	public void GoldenTicketsAndKeys()
	{
		var park = EconomyTestData.Park();
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		park.OpenPark();
		for ( var visitor = 0; visitor < 100; visitor++ )
			park.TryAdmitVisitor( 100, out _ );
		park.ReportRecord( ParkRecordKind.CoasterHeight, 0, 110 );
		park.AdvanceDays( 30 );
		CollectionAssert.AreEquivalent( new[] { GoldenTicketKind.Visitors, GoldenTicketKind.CoasterHeight }, park.Objectives.GoldenTickets.ToArray() );
		Assert.AreEqual( 2, events.Count( item => item.Kind == ParkEventKind.GoldenTicketWon ) );
		Assert.AreEqual( 2, park.GoldenTicketsAvailable );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughGoldenTickets, park.TryBuild( 1112, out _ ) );
		park.ReportRecord( ParkRecordKind.WaterLength, 0, 60 );
		park.AdvanceDays( 30 );
		var earnedProgress = new PlayerProgress();
		earnedProgress.SetTickets( park.Settings.Theme, park.Objectives.GoldenTickets.Count );
		Assert.AreEqual( 3, earnedProgress.TotalTickets );
		Assert.AreEqual( PlayerProgress.StartingKeys + 1, earnedProgress.Keys );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1112, out _ ) );
		Assert.AreEqual( 0, park.GoldenTicketsAvailable );
		earnedProgress.SetTickets( park.Settings.Theme, park.Objectives.GoldenTickets.Count );
		Assert.AreEqual( PlayerProgress.StartingKeys + 1, earnedProgress.Keys, "Spending mystery-item tickets preserves earned keys." );

		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );
		Assert.AreEqual( 0, restored.GoldenTicketsAvailable );
		Assert.AreEqual( 3, restored.TicketsSpent );
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
	public void SaveLoadRejectsCorruptOrForeignFiles()
	{
		var park = EconomyTestData.Park();
		park.TryBuild( 1100, out _ );
		var json = ParkSaveFile.Serialize( park );
		void Rejects( string text ) => Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( text ) ) );
		Rejects( json[..(json.Length / 2)] );
		Rejects( json.Replace( "\"Version\": 1", "\"Version\": 2" ) );
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

	[TestMethod]
	public void SaveEconomyRecordsAreLocatedByStructure()
	{
		var payload = Enumerable.Repeat( (byte)0xCD, 4096 ).ToArray();
		void Int( int offset, int value ) => BitConverter.GetBytes( value ).CopyTo( payload, offset );
		var words = new[] { 25, 87987, 0, 1, 87787, 0, -12013 };
		for ( var index = 0; index < words.Length; index++ )
			Int( 1000 + index * 4, words[index] );
		var loans = new[] { (100000, 36), (50000, 36), (25000, 36), (18000, 24) };
		for ( var index = 0; index < loans.Length; index++ )
		{
			var offset = 1028 + index * 32;
			Array.Clear( payload, offset, 32 );
			Int( offset + 4, loans[index].Item1 );
			Int( offset + 12, loans[index].Item2 );
			Int( offset + 16, loans[index].Item1 / loans[index].Item2 );
			Int( offset + 28, index );
		}
		var challenges = new[] { (3, 60, 30, 0, 5000, 0, true), (18, 60, 1, 1180, 7500, 20, true), (20, 180, 200, 1180, 30000, 0, false) };
		for ( var index = 0; index < challenges.Length; index++ )
		{
			var offset = 2001 + index * 45;
			Array.Clear( payload, offset, 45 );
			var (type, time, value, target, prize, followup, independent) = challenges[index];
			Int( offset, type );
			Int( offset + 4, time );
			Int( offset + 8, value );
			Int( offset + 12, target );
			Int( offset + 20, prize );
			Int( offset + 24, followup );
			payload[offset + 42] = (byte)(independent ? 1 : 0);
		}
		var records = SaveEconomyRecords.Parse( payload );
		Assert.AreEqual( 4, records.Loans.Count );
		Assert.AreEqual( new SaveLoanRecord( 1060, 1, false, 50000, 0, 36, 1388, false, 0, 1 ), records.Loans[1] );
		Assert.AreEqual( new SaveBankRecord( 1000, 25, 87987, 0, true, 87787, 0, -12013 ), records.Bank );
		Assert.AreEqual( 3, records.Challenges.Count );
		Assert.AreEqual( new SaveChallengeRecord( 2046, 18, 60, 1, 1180, 0, 7500, 20, true ), records.Challenges[1] );
		Assert.IsFalse( records.Challenges[2].Independent );

		var duplicate = payload.ToArray();
		Array.Copy( payload, 2001, duplicate, 3000, 90 );
		Assert.ThrowsException<InvalidDataException>( () => SaveEconomyRecords.Parse( duplicate ) );
		var missing = payload.ToArray();
		Array.Fill( missing, (byte)0xCD, 1028, 128 );
		Assert.ThrowsException<InvalidDataException>( () => SaveEconomyRecords.Parse( missing ) );

		var settings = EconomyTestData.Settings( apr: 0 );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Apply( economy, records, new[] { 1100 }, new[] { 1601 } ), "4 save offers vs 3 settings offers" );
	}
}
