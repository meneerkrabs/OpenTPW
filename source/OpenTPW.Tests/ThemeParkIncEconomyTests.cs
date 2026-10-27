using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The Theme Park Inc money economy (docs/THEME-PARK-INC.md, "Economy") on synthetic data: the edition-scoped settings, the capability split, the
/// rules that differ from Theme Park World (running costs, the scrap value's condemned branch, no Buy Land, no research gate, the arrival
/// group) and the shared rules that must stay as they are (loans, bankruptcy, the calendar). The corpus run is in <see cref="ThemeParkIncPlayTests"/>.
/// </summary>
[TestClass]
public class ThemeParkIncEconomyTests
{
	private const string TpiStandard =
		"BankAccountInfo.InitialCash\t50000\nBankAccountInfo.InitialAdmissionFee\t25\n"
		+ "LoanInfo[0].LoanAmount\t10000\nLoanInfo[0].APRInPercent\t20\nLoanInfo[0].RepaymentPeriodInMonths\t36\nLoanInfo[0].Lendername\t0\n"
		+ "LoanInfo[1].LoanAmount\t50000\nLoanInfo[1].APRInPercent\t18\nLoanInfo[1].RepaymentPeriodInMonths\t24\nLoanInfo[1].Lendername\t1\n"
		+ "Costs.QueueCell 25\nCosts.PathCell 10\nCosts.KartTrackCell 40\nCosts.WaterTrackCell 50\nCosts.MapCell 75\n"
		+ "PeepInfo.ExcitementToCostDivisor 6 Scaling factor\nPeepInfo.MinimumEntryFee 20\nPeepInfo.CheapPriceMultiplier 0.75\nPeepInfo.AveragePriceMultiplier 1.25\nPeepInfo.ExpensivePriceMultiplier 2.0\n";

	private static BalanceSettings Settings( string text = TpiStandard ) =>
		BalanceSettings.ForThemeParkInc( new SamSettings( new[] { SamDocument.Parse( text, "Standard.sam" ) } ), "science" );

	private static EconomyObjectInfo Ride( int id, long cost, int running, int research = 0, int level1Running = 0 )
	{
		var scrap = new[] { 50, 30, 20, 10 };
		var levels = new List<UpgradeLevelInfo> { new( 0, cost, research, 0, 0, scrap, RunningCost: running ) };
		if ( level1Running > 0 )
			levels.Add( new( 1, cost / 2, 0, 0, 0, scrap, RunningCost: level1Running ) );
		return new( id, $"Ride {id}", ParkObjectKind.Ride, ResearchCategory.Ride, 7, levels, null, null, null, 0, 0, 0, 0, 0, 0, "/x" );
	}

	private static ParkEconomy Economy( params EconomyObjectInfo[] objects ) =>
		new( Settings(), new EconomyObjectCatalog( objects ), ParkGameMode.FullSimulation, 7, EconomyRules.ThemeParkIncMoney );

	/// <summary>A Theme Park World economy over the same objects (the shared rules and the rules that must not change for it).</summary>
	private static ParkEconomy TpwEconomy( params EconomyObjectInfo[] objects ) =>
		new( EconomyTestData.Settings(), new EconomyObjectCatalog( objects ), ParkGameMode.FullSimulation, 7 );

	// ---- the capability split and the edition-scoped settings ------------------------------------

	[TestMethod]
	public void TheCapabilitiesSplitTheEconomyAndThemeParkWorldKeepsAllOfIt()
	{
		var tpw = EditionCapabilities.ForThemeParkWorld();
		Assert.IsTrue( tpw.Money && tpw.Staff && tpw.Research && tpw.Challenges && tpw.Economy, "Theme Park World runs every part" );
		var tpi = EditionCapabilities.ForThemeParkIncSlice();
		Assert.IsTrue( tpi.Money, "Theme Park Inc runs the money" );
		Assert.IsFalse( tpi.Staff || tpi.Research || tpi.Challenges || tpi.Economy, "and nothing of the rest" );
		Assert.IsFalse( EconomyRules.For( tpw ).ThemeParkInc, "the Theme Park World rules are the full ones" );
		Assert.AreEqual( EconomyRules.Full with { ThemeParkInc = false }, EconomyRules.For( tpw ) );
	}

	[TestMethod]
	public void TheSettingsHoldOnlyThemeParkIncKeysAndFillNoThemeParkWorldValues()
	{
		var settings = Settings();
		Assert.AreEqual( GameEdition.ThemeParkInc, settings.Edition );
		Assert.AreEqual( GameEdition.ThemeParkWorld, EconomyTestData.Settings().Edition );
		Assert.AreEqual( 50000L, settings.InitialCash );
		Assert.AreEqual( 25, settings.InitialAdmissionFee );
		Assert.AreEqual( new CellCosts( 25, 10, 40, 50, 75 ), settings.Costs );
		Assert.AreEqual( 6, settings.EntryFee.ExcitementToCostDivisor );
		Assert.AreEqual( 2, settings.Loans.Count );
		Assert.AreEqual( 0, settings.Roles.Count + settings.BaseWage.Count + settings.ResearchEffort.Count + settings.ResearchAbility.Count + settings.ChallengesInThisLevel.Count, "no staff, research or challenge member is filled" );
		Assert.AreEqual( 0, settings.Challenges.Count );
		Assert.IsFalse( settings.CanSpendTickets || settings.CanEarnTickets );
		// a money key that is missing fails the load, as for Theme Park World; nothing falls back to a Theme Park World number
		Assert.ThrowsException<KeyNotFoundException>( () => Settings( TpiStandard.Replace( "Costs.MapCell 75\n", "" ) ) );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => Settings( TpiStandard.Replace( "Costs.PathCell 10", "Costs.PathCell -1" ) ) );
	}

	[TestMethod]
	public void TheCatalogueReadsTheThemeParkIncObjectKeysAndTheWorldFilesKeepZero()
	{
		const string Object = "Info.Id 1101\nInfo.Name Test\nInfo.ExpectedPrice 25\nInfo.WorkingLife 7\nUpgrades[0].CostOfUpgrade 1000\nUpgrades[0].RunningCost 60\nUpgrades[0].CashPerBrainPoint 3\n"
			+ "Upgrades[1].CostOfUpgrade 250\nUpgrades[1].RunningCost 40\nUpgrades[1].CashPerBrainPoint 4\nUpgrades[0].ScrapValueYear1 50\n";
		var info = EconomyObjectCatalog.FromSettings( new SamSettings( new[] { SamDocument.Parse( Object, "ride.sam" ) } ), ParkObjectKind.Ride, "/x" );
		Assert.AreEqual( (25, 7), (info.ExpectedPrice, info.WorkingLife) );
		Assert.AreEqual( (60, 3), (info.Upgrades[0].RunningCost, info.Upgrades[0].CashPerBrainPoint) );
		Assert.AreEqual( (40, 4), (info.Upgrades[1].RunningCost, info.Upgrades[1].CashPerBrainPoint) );
		var world = EconomyObjectCatalog.FromSettings( new SamSettings( new[] { SamDocument.Parse( "Info.Id 1102\nUpgrades[0].CostOfUpgrade 500\n", "ride.sam" ) } ), ParkObjectKind.Ride, "/x" );
		Assert.AreEqual( (0, 0, 0), (world.ExpectedPrice, world.WorkingLife, world.Upgrades[0].RunningCost), "Theme Park World's files carry none of the keys" );
	}

	// ---- the money: shared rules ------------------------------------------------------------------

	[TestMethod]
	public void TheBankStartsWithCashAndFeeAndTheCalendarAdvances()
	{
		var economy = Economy( Ride( 1, 1000, 0 ) );
		Assert.AreEqual( 50000L, economy.Balance );
		Assert.AreEqual( 25, economy.EntranceFee );
		Assert.AreEqual( 0, economy.Staff.Candidates.Count, "no staff pool is drawn" );
		Assert.AreEqual( new DateTime( 2000, 1, 1 ), ParkCalendar.DateTimeAt( economy.Tick ) );
		economy.AdvanceDays( 40 );
		Assert.AreEqual( new DateTime( 2000, 2, 10 ), ParkCalendar.DateTimeAt( economy.Tick ).Date, "15000 game seconds per real second: the park clock runs on" );
		economy.AdvanceMonths( 11 );
		Assert.AreEqual( 2001, ParkCalendar.DateTimeAt( economy.Tick ).Year );
	}

	[TestMethod]
	public void BuildingPathsQueuesAndTracksChargeTheCellCosts()
	{
		var economy = Economy( Ride( 1, 1000, 0 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1, out var state ) );
		Assert.AreEqual( 49000L, economy.Balance, "Upgrades[0].CostOfUpgrade" );
		Assert.AreEqual( 1000L, state!.TotalSpent );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TrySpendCell( CellPurchase.Path ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TrySpendCell( CellPurchase.Queue ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuyCells( CellPurchase.KartTrack, 2 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuyCells( CellPurchase.WaterTrack, 1 ) );
		Assert.AreEqual( 49000L - 10 - 25 - 80 - 50, economy.Balance );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughMoney, Economy( Ride( 2, 60000, 0 ) ).CheckBuild( 2 ), "a price above the balance is refused" );
	}

	[TestMethod]
	public void LoansRepayMonthlyAsInThemeParkWorld()
	{
		var tpi = Economy( Ride( 1, 1000, 0 ) );
		var offer = tpi.Settings.Loans[0];
		var account = tpi.TakeLoan( offer.Index );
		Assert.AreEqual( 60000L, tpi.Balance );
		tpi.AdvanceMonths( 1 );
		Assert.AreEqual( 60000L - account.MonthlyRepayment, tpi.Balance, "one instalment" );
		Assert.AreEqual( account, LoanMath.Open( offer ), "the same offer arithmetic" );
		Assert.IsTrue( tpi.TryRepayLoan( offer.Index ) );
		Assert.AreEqual( 0, tpi.Loans.Count );
	}

	[TestMethod]
	public void SixMonthsInTheRedBankruptThePark()
	{
		// an object whose running cost outruns the cash: 50000 lasts two months, then the 30-day months in the red count to six
		var economy = Economy( Ride( 1, 0, 20000 ) );
		economy.RegisterExisting( 1 );
		var events = new List<ParkEventKind>();
		economy.EventRaised += item => events.Add( item.Kind );
		economy.AdvanceMonths( 3 );
		Assert.IsTrue( economy.Balance < 0 );
		Assert.IsFalse( economy.IsBankrupt );
		economy.AdvanceMonths( 9 );
		Assert.IsTrue( economy.IsBankrupt );
		CollectionAssert.Contains( events, ParkEventKind.Bankrupt );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Bankrupt, economy.CheckBuild( 1 ) );
		Assert.AreEqual( 0, economy.Settings.Roles.Count, "and the staff-less park never needed a pool" );
	}

	[TestMethod]
	public void TheEntryFeeJudgementUsesTheSettingsDivisor()
	{
		var tpi = Settings().EntryFee;
		var guests = new GuestSettings { ExcitementToCostDivisor = tpi.ExcitementToCostDivisor, MinimumEntryFee = tpi.MinimumEntryFee };
		Assert.AreEqual( 20u + 600 / 6, BoothVerdict.ParkValue( 600, 0, guests ), "divisor 6 where Theme Park World's data holds 4" );
		Assert.AreEqual( 20u + 600 / 8, BoothVerdict.ParkValue( 600, 2, guests ), "the draw adds up to half the divisor: 600 / (6 + 2 % 4)" );
		Assert.AreEqual( 20u + 600 / 4, BoothVerdict.ParkValue( 600, 0, new GuestSettings { ExcitementToCostDivisor = EconomyTestData.Settings().EntryFee.ExcitementToCostDivisor, MinimumEntryFee = 20 } ) );
	}

	// ---- the money: Theme Park Inc's own rules ----------------------------------------------------

	[TestMethod]
	public void RunningCostsAreChargedMonthlyAtTheCurrentLevelAndNotWhileBrokenOrCondemned()
	{
		var economy = Economy( Ride( 1, 1000, 60, level1Running: 40 ), Ride( 2, 500, 15 ) );
		economy.TryBuild( 1, out var first );
		economy.TryBuild( 2, out var second );
		var start = economy.Balance;
		economy.AdvanceMonths( 2 );
		Assert.AreEqual( start - 2 * (60 + 15), economy.Balance, "each object pays its Upgrades[0].RunningCost every month" );
		Assert.AreEqual( 60L, first!.CostsLastMonth, "the cost enters the object's statistic" );
		Assert.AreEqual( -120L, first.TotalProfit, "two months of running costs are in the object's total profit" );
		Assert.AreEqual( -30L, second!.TotalProfit );
		Assert.IsTrue( first.ProfitLast30Days <= -60 && second.ProfitLast30Days <= -15, "and in the 30-day profit through the cost ring" );
		first.Level = 1;
		Assert.AreEqual( 40L, economy.RunningCost( first ), "the current level's value" );
		first.IsBrokenDown = true;
		Assert.AreEqual( 0L, economy.RunningCost( first ), "ride state 1 costs nothing" );
		first.LifeGauge = 0;
		Assert.AreEqual( 0L, economy.RunningCost( first ), "and neither does state 4" );
		var before = economy.Balance;
		economy.AdvanceMonths( 1 );
		Assert.AreEqual( before - 15, economy.Balance, "only the other object pays" );
		Assert.AreEqual( 15L, economy.RunningCost( second! ) );
	}

	[TestMethod]
	public void AParkWithoutStaffWearsNoToiletAndDropsNoLitter()
	{
		var toilet = Ride( 1, 100, 5 ) with { ProvidesRelief = true };
		var economy = Economy( toilet );
		economy.TryBuild( 1, out var state );
		for ( var month = 0; month < 24; month++ )
		{
			for ( var use = 0; use < 400; use++ )
				Assert.IsFalse( economy.UseRelief( state!.Id, 255 ), "a use never turns the toilet dirty" );
			economy.AdvanceMonths( 1 );
		}
		Assert.AreEqual( RideRepair.FullGauge, state!.Repair, "after two years of heavy use the gauge is full: nobody could clean it, so nothing wears it" );
		Assert.IsFalse( ParkEconomy.IsDirtyToilet( state ) );
		var wearing = TpwEconomy( toilet );
		wearing.TryBuild( 1, out var worn );
		for ( var use = 0; use < 400; use++ )
			wearing.UseRelief( worn!.Id, 255 );
		Assert.IsTrue( ParkEconomy.IsDirtyToilet( worn! ), "Theme Park World's rule still wears the toilet" );
	}

	[TestMethod]
	public void ThemeParkWorldRulesChargeNoRunningCost()
	{
		var economy = TpwEconomy( Ride( 1, 1000, 60 ) );
		economy.TryBuild( 1, out var state );
		Assert.AreEqual( 0L, economy.RunningCost( state! ) );
		var before = economy.Balance;
		economy.AdvanceMonths( 2 );
		Assert.AreEqual( before, economy.Balance, "nothing but wages, which an empty staff does not owe" );
	}

	[TestMethod]
	public void TheScrapValueRefundsByAgeAndRefusesACondemnedObjectOnlyForThemeParkInc()
	{
		var tpi = Economy( Ride( 1, 1000, 0 ) );
		tpi.TryBuild( 1, out var fresh );
		Assert.AreEqual( 100, tpi.ScrapPercent( fresh! ), "unused within 30 days refunds everything, as in Theme Park World" );
		Assert.AreEqual( 1000L, tpi.ScrapValue( fresh! ) );
		tpi.AdvanceDays( 40 );
		Assert.AreEqual( 50, tpi.ScrapPercent( fresh! ), "then the year's percentage" );
		tpi.AdvanceDays( 400 );
		Assert.AreEqual( 30, tpi.ScrapPercent( fresh! ), "year 1" );
		fresh!.IsBrokenDown = true;
		Assert.AreEqual( 30, tpi.ScrapPercent( fresh ), "broken down (state 1) still refunds" );
		fresh.LifeGauge = 0;
		fresh.StateWord.Set( RideObjectStates.Condemned );
		Assert.AreEqual( 0, tpi.ScrapPercent( fresh ), "condemned (state 4) refunds nothing" );
		Assert.AreEqual( 0L, tpi.ScrapValue( fresh ) );
		var before = tpi.Balance;
		Assert.AreEqual( 0L, tpi.Sell( fresh.Id ) );
		Assert.AreEqual( before, tpi.Balance );

		var tpw = TpwEconomy( Ride( 1, 1000, 0 ) );
		tpw.TryBuild( 1, out var same );
		same!.IsBrokenDown = true;
		same.LifeGauge = 0;
		Assert.AreEqual( 100, tpw.ScrapPercent( same ), "Theme Park World has no state-4 branch: its rule is unchanged" );
		Assert.AreEqual( 1000L, tpw.Sell( same.Id ) );
	}

	[TestMethod]
	public void ThemeParkIncHasNoBuyLandNoUpgradePurchaseNoStaffAndNoResearchGate()
	{
		var tpi = Economy( Ride( 1, 1000, 0, research: 500, level1Running: 10 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NoLandForSale, tpi.TryBuyCells( CellPurchase.Land, 1 ) );
		Assert.ThrowsException<InvalidOperationException>( () => tpi.ChargeLand( 75, 1 ) );
		Assert.ThrowsException<InvalidOperationException>( () => tpi.Hire( 1 ) );
		Assert.ThrowsException<InvalidOperationException>( () => tpi.SetResearchEffort( ResearchCategory.Ride, 10 ) );
		Assert.ThrowsException<InvalidOperationException>( () => tpi.AcceptChallenge() );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, tpi.TryBuild( 1, out var state ), "an unresearched object is on offer when research is refused" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NeedsStaff, tpi.TryBuyUpgrade( state!.Id ), "an upgrade is installed by a mechanic" );
		Assert.AreEqual( 50000L - 1000, tpi.Balance, "none of them charged anything" );

		var tpw = TpwEconomy( Ride( 1, 1000, 0, research: 500 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotResearched, tpw.CheckBuild( 1 ), "Theme Park World still gates the build on research" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, tpw.TryBuyCells( CellPurchase.Land, 1 ) );
	}

	[TestMethod]
	public void ARunWithoutStaffDoesNotWearOrBreakDownTheRides()
	{
		var economy = Economy( Ride( 1, 1000, 0 ) );
		economy.TryBuild( 1, out var state );
		economy.AdvanceMonths( 3 );
		Assert.IsFalse( state!.IsBrokenDown, "no mechanic could repair a breakdown, so none happens" );
		Assert.AreEqual( 100.0, state.Repair, "and nothing wears" );
	}

	// ---- the arrival rule ---------------------------------------------------------------------

	[TestMethod]
	public void TheArrivalGroupFollowsThemeParkIncsRoutineWhenTheSettingsHoldItsWeatherPercentages()
	{
		var settings = GuestTests.CreateSettings();
		settings.ArrivalNewParkBonus = 20;
		settings.ArrivalPointsPerVisitor = 25;
		settings.ArrivalMinPeople = 1;
		settings.ArrivalWeatherPercentages = new[] { 90, 80, 75, 120 };
		var points = 500;
		var weather = 0;
		var halted = false;
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 1 ) { ParkPoints = () => points, ArrivalsHalted = () => halted, ArrivalWeatherState = () => weather };
		Assert.AreEqual( 20, simulation.ArrivalGroupSize(), "100 percent x 0.01f x (20 + 500) / 25 = 20.8 truncated; Theme Park World's 1.2 would give 24" );
		weather = 1;
		Assert.AreEqual( 24, simulation.ArrivalGroupSize(), "state 1 is the heatwave, 120 percent (ORACLE.md X7; the jump table's first entry loads 0xA02250)" );
		weather = 2;
		Assert.AreEqual( 18, simulation.ArrivalGroupSize(), "state 2 rain, 90 percent" );
		weather = 3;
		Assert.AreEqual( 16, simulation.ArrivalGroupSize(), "state 3 storm, 80 percent" );
		weather = 4;
		Assert.AreEqual( 15, simulation.ArrivalGroupSize(), "state 4 snow, 75 percent" );
		weather = 9;
		Assert.AreEqual( 20, simulation.ArrivalGroupSize(), "any other state takes the plain 100.0" );
		weather = 0;
		settings.ArrivalPointsPerVisitor = 1;
		points = 80;
		Assert.AreEqual( 99, simulation.ArrivalGroupSize(), "0.01 is a single: (20 + 80) x 100 x 0.01f is 99.99999776 and truncates to 99" );
		points = -20;
		settings.ArrivalMinPeople = 3;
		Assert.AreEqual( 3, simulation.ArrivalGroupSize(), "raised to MinPeople" );
		halted = true;
		Assert.AreEqual( 0, simulation.ArrivalGroupSize(), "none while the park is lost" );
	}

	[TestMethod]
	public void TheWeatherPercentagesComeFromTheBalanceFileAndAreAbsentFromThemeParkWorlds()
	{
		var values = new Dictionary<string, string[]>();
		GuestSettings.ParseSam( "Arrival.RainPercentageModifier 90\nArrival.StormPercentageModifier 80\nArrival.SnowPercentageModifier 75\nArrival.HeatwavePercentageModifier 120\n", values );
		CollectionAssert.AreEqual( new[] { 90, 80, 75, 120 }, GuestSettings.FromValues( values ).ArrivalWeatherPercentages );
		values.Remove( "Arrival.SnowPercentageModifier" );
		Assert.IsNull( GuestSettings.FromValues( values ).ArrivalWeatherPercentages, "a file without all four keeps the Theme Park World rule" );
		Assert.IsNull( GuestTests.CreateSettings().ArrivalWeatherPercentages );
	}
}
