using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>Economy against the original settings, objects and the Easymode save (inconclusive without OPENTPW_GAME_PATH).</summary>
[TestClass]
[DoNotParallelize]
public class ParkEconomyOriginalDataTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestMethod]
	public void ThemeSettingsLayerStandardThemeAndEasyFiles()
	{
		UseOriginalData();
		var normal = BalanceSettings.Load( "jungle", easy: false );
		Assert.AreEqual( 50000, normal.InitialCash );
		Assert.AreEqual( 20, normal.InitialAdmissionFee );
		Assert.AreEqual( 8, normal.Loans.Count );
		Assert.AreEqual( new LoanOffer( 0, 100000, 20, 36, 0 ), normal.Loans[0] );
		Assert.AreEqual( 3, normal.Loans[1].LenderNameIndex, "jungle Standard.sam renames lender 1" );
		Assert.AreEqual( 11, normal.Loans[6].LenderNameIndex );
		Assert.AreEqual( 2, normal[StaffType.Mechanic].AverageGrade, "theme overrides AvgGradeOfMechanics 1 -> 2" );
		Assert.AreEqual( 6 * 30, normal.GetMonthlyWage( StaffType.Mechanic, 2 ) );
		CollectionAssert.AreEqual( new[] { 1, 15, 6, 18, 12, 7, 8, 9 }, normal.ChallengesInThisLevel.ToArray() );
		Assert.AreEqual( 35, normal.Challenges.Count );
		Assert.AreEqual( 1, normal.KeysToEnter );
		Assert.AreEqual( 100, normal.Costs.MapCell );

		var easy = BalanceSettings.Load( "jungle", easy: true );
		Assert.IsTrue( BalanceSettings.HasEasyLayer( "jungle" ) );
		Assert.AreEqual( 100000, easy.InitialCash );
		Assert.IsTrue( easy.Loans.All( offer => offer.AprPercent == 0 ) );
		Assert.AreEqual( 5 * 23, easy.GetMonthlyWage( StaffType.Mechanic, 2 ) );
		Assert.AreEqual( 10, easy.Costs.MapCell );
		Assert.AreEqual( 9, easy.ResearchAbility[1] );
		Assert.AreEqual( 2.5, easy.EntryFee.ExpensivePriceMultiplier );

		var keys = Themes.ToDictionary( theme => theme, theme => BalanceSettings.Load( theme, false ).KeysToEnter );
		CollectionAssert.AreEqual( new[] { 1, 1, 3, 5 }, Themes.Select( theme => keys[theme] ).ToArray() );
		foreach ( var theme in Themes.Skip( 1 ) )
			Assert.IsFalse( BalanceSettings.HasEasyLayer( theme ) );
	}

	/// <summary>
	/// Every theme's staff room is not choosable (Features.sam Info.IsChoosable 0), so its record holds state 3 (0x4DB4FB); a standing staff room is no closed one
	/// (0x4E0440 reads the open word only), and the advisor's StaffroomClosed message 263, whose only response plays the z_error placeholder, waits for one the player closed (issue #829).
	/// </summary>
	[TestMethod]
	public void EveryThemesStaffRoomIsAnOpenRestAreaForTheAdvisor()
	{
		UseOriginalData();
		foreach ( var theme in Themes )
		{
			var park = ParkEconomy.CreateForTheme( theme, easy: false );
			var room = park.Catalog.Objects.Single( info => info.ChillsYouOut );
			Assert.IsFalse( room.IsChoosable, $"{theme}: {room.Name}" );
			var record = park.RegisterExisting( room.InfoId );
			var state = new EconomyParkState( park );
			Assert.AreEqual( (1, 0), (state.StaffRooms, state.ClosedStaffRooms), theme );
			record.IsOpen = false;
			Assert.AreEqual( 1, state.ClosedStaffRooms, theme );
		}
	}

	// ORACLE F23 (ECON-039): every shipped theme's GoldenTicketLocal.ProfitYear is the strict signed threshold of the award, with no factor.
	[TestMethod]
	public void TheShippedProfitTicketThresholdsAreStrictAndUnscaled()
	{
		UseOriginalData();
		foreach ( var theme in Themes )
		{
			var settings = BalanceSettings.Load( theme, easy: false );
			var threshold = settings.GoldenTickets.ProfitYear;
			Assert.IsTrue( threshold > 0, theme );
			bool Won( long profit )
			{
				var park = new ParkEconomy( settings, EconomyObjectCatalog.Load( theme, easy: false ), ParkGameMode.FullSimulation, 1 );
				return park.Objectives.CheckGoldenTickets( park.Counters, park.GuestStatistics, park.Research, profit, Array.Empty<long>() ).Contains( GoldenTicketKind.ProfitYear );
			}
			Assert.IsFalse( Won( threshold ), theme );
			Assert.IsTrue( Won( threshold + 1 ), theme );
			Assert.IsFalse( Won( threshold * 9L / 10 + 1 ), $"{theme}: 0.9 x ProfitYear is the advisor hint, not the award" );
		}
		Assert.AreEqual( 15000, BalanceSettings.Load( "jungle", false ).GoldenTickets.ProfitYear );
		Assert.AreEqual( 30000, BalanceSettings.Load( "space", false ).GoldenTickets.ProfitYear );
	}

	/// <summary>The objects that spread a <c>RegionFX</c> row (GUEST-006): a small and a super toilet, a security camera per theme, and the Hallow and Space shows.</summary>
	[TestMethod]
	public void ToiletsCamerasAndShowsCarryTheirRegionFlags()
	{
		UseOriginalData();
		foreach ( var (theme, toilets, camera, fireworks) in new[] { ("jungle", new[] { 1402, 1419 }, 1413, 0), ("hallow", new[] { 2404, 2406 }, 2403, 2415),
			("space", new[] { 3402, 3403 }, 3413, 3416), ("fantasy", new[] { 4405, 4406 }, 4414, 0) } )
		{
			var catalog = EconomyObjectCatalog.Load( theme, easy: false );
			CollectionAssert.AreEquivalent( toilets, catalog.Objects.Where( info => info.ProvidesRelief ).Select( info => info.InfoId ).ToArray(), theme );
			CollectionAssert.AreEqual( new[] { camera }, catalog.Objects.Where( info => info.ProvidesSecurity ).Select( info => info.InfoId ).ToArray(), theme );
			CollectionAssert.AreEqual( fireworks == 0 ? new int[0] : new[] { fireworks }, catalog.Objects.Where( info => info.IsFireworks ).Select( info => info.InfoId ).ToArray(), theme );
		}
	}

	[TestMethod]
	public void ObjectCatalogMergesCategoryObjectAndEasyFiles()
	{
		UseOriginalData();
		var catalog = EconomyObjectCatalog.Load( "jungle", easy: false );
		var bouncy = catalog[1100];
		Assert.AreEqual( "Belly Bounce", bouncy.Name );
		Assert.AreEqual( ParkObjectKind.Ride, bouncy.Kind );
		CollectionAssert.AreEqual( new long[] { 500, 400, 500 }, bouncy.Upgrades.Select( level => level.CostOfUpgrade ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 250, 300 }, bouncy.Upgrades.Select( level => level.CostOfResearch ).ToArray() );
		Assert.AreEqual( 5, bouncy.Upgrades[0].WearRate, "category Rides.sam default" );
		CollectionAssert.AreEqual( new[] { 50, 30, 20, 10 }, bouncy.Upgrades[0].ScrapValuePercentByYear.ToArray() );
		var drinks = catalog[1203];
		Assert.AreEqual( (30, 20, 4, 3, 50), (drinks.PricePerUse, drinks.CostOfGoods, drinks.ShopType, drinks.SpecialIngredient, drinks.LitterEffect) );
		Assert.AreEqual( 1, drinks.Upgrades.Count );
		var spray = catalog[1303];
		Assert.AreEqual( (20, 50, 75), (spray.PricePerUse, spray.CostOfGoods, spray.ChanceOfLosingPercent) );
		Assert.AreEqual( ParkObjectKind.FixedItem, catalog[1601].Kind );
		// tp.exe's buy-menu filler 0x4aaf70 never lists WhichUIType 4: per theme the six features-directory fixed items (bus, gates, seaplane, lights, ferry, end)
		foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space" } )
			Assert.AreEqual( 6, EconomyObjectCatalog.Load( theme, easy: false ).Objects.Count( info => info.Kind == ParkObjectKind.FixedItem && info.WhichUIType == 4 ), theme );
		Assert.AreEqual( ParkObjectKind.LandTool, catalog[101].Kind );
		Assert.AreEqual( 1150, catalog[1500].AddOnTargetId, "Dino Karts Jump upgrades the Dino Karts" );
		Assert.AreEqual( 10000, catalog[1185].PurchaseCost, "coaster archive: the .sam with Info.Id, not coaster.sam" );
		Assert.AreEqual( 3, catalog[1112].GoldenTicketCost );
		var easy = EconomyObjectCatalog.Load( "jungle", easy: true );
		CollectionAssert.AreEqual( new[] { 3, 2, 1 }, easy[1100].Upgrades.Select( level => level.WearRate ).ToArray(), "Easy_Bouncy.sam" );
		CollectionAssert.AreEqual( new[] { 0, 0, 0 }, easy[1100].Upgrades.Select( level => level.CostOfResearch ).ToArray() );
		foreach ( var theme in Themes )
		{
			var themed = EconomyObjectCatalog.Load( theme, false );
			Assert.IsTrue( themed.Objects.Count > 50, theme );
			Assert.IsTrue( themed.Objects.Where( info => info.IsBuyable ).All( info => info.Upgrades.Count > 0 ), theme );
			Assert.IsTrue( themed.Objects.Where( info => info.Kind == ParkObjectKind.Shop ).All( info => info.PricePerUse > 0 && info.CostOfGoods > 0 && info.ShopType > 0 ), theme );
		}
	}

	[TestMethod]
	public void ShippedResearchDataGivesTheOriginalsUpgradeCategory()
	{
		// tp.exe 0x005031A0 / 0x00504580: projects take the object's own Research.Category and Research.Group, and level projects come from objects whose own
		// category is not 4. On the shipped data that is the old kind rule: every add-on declares category 4 and group 0 and names a ride, no other object is in
		// category 4, and only rides have levels above 0.
		UseOriginalData();
		foreach ( var theme in Themes )
		{
			foreach ( var easy in BalanceSettings.HasEasyLayer( theme ) ? new[] { false, true } : new[] { false } )
			{
				var catalog = EconomyObjectCatalog.Load( theme, easy );
				var buyable = catalog.Objects.Where( info => info.IsBuyable ).ToList();
				Assert.IsTrue( buyable.Count( info => info.Kind == ParkObjectKind.Upgrade ) > 0, theme );
				foreach ( var info in buyable )
				{
					Assert.AreEqual( info.Kind == ParkObjectKind.Upgrade, info.ResearchCategory == ResearchCategory.Upgrade, $"{theme} {info.InfoId}" );
					if ( info.Kind == ParkObjectKind.Upgrade )
						Assert.IsTrue( info.ResearchGroup == 0 && catalog.TryGet( info.AddOnTargetId, out var target ) && target.Kind == ParkObjectKind.Ride, $"{theme} {info.InfoId}" );
					if ( info.Kind != ParkObjectKind.Ride )
						Assert.IsTrue( info.Upgrades.Count <= 1, $"{theme} {info.InfoId}" );
				}
			}
		}
	}

	[TestMethod]
	public void ShippedScrapRowsGiveTheOriginalsRefunds()
	{
		// ORACLE.md F21: the refund is Upgrades[0].CostOfUpgrade times the current level's ScrapValueYearN percentage, or the whole
		// base price for an unused object in its first 30 days, on every shipped object.
		UseOriginalData();
		var checkedObjects = 0;
		var upgraded = 0;
		var noFullRefund = new System.Collections.Generic.List<string>();
		foreach ( var theme in Themes )
		{
			var park = ParkEconomy.CreateForTheme( theme, easy: false );
			foreach ( var info in park.Catalog.Objects.Where( info => info.IsBuyable ) )
			{
				Assert.IsTrue( info.Upgrades.All( level => level.ScrapValuePercentByYear.Count == 4 ), $"{theme} {info.Name}: four scrap years per level" );
				var item = park.RegisterExisting( info.InfoId );
				var firstYear = info.Upgrades[0].ScrapValuePercentByYear[0];
				if ( firstYear <= 0 )
					noFullRefund.Add( $"{theme} {info.Name}" );
				Assert.AreEqual( firstYear > 0 ? info.PurchaseCost : info.PurchaseCost * firstYear / 100, park.ScrapValue( item ), $"{theme} {info.Name}: unused and new" );
				park.RecordRideUse( item.Id ); park.RecordUseFinished( item.Id );
				Assert.AreEqual( info.PurchaseCost * firstYear / 100, park.ScrapValue( item ), $"{theme} {info.Name}: used, year 1" );
				item.Level = info.Upgrades.Count - 1;
				Assert.AreEqual( info.PurchaseCost * info.Upgrades[^1].ScrapValuePercentByYear[0] / 100, park.ScrapValue( item ), $"{theme} {info.Name}: top level, year 1" );
				checkedObjects++;
				if ( info.Upgrades.Count > 1 )
					upgraded++;
			}
		}
		Console.WriteLine( $"Scrap refunds checked on {checkedObjects} shipped objects, {upgraded} with upgrade levels; no first-30-days refund (level 0 year 1 is 0) for: {string.Join( ", ", noFullRefund )}." );
		Assert.IsTrue( checkedObjects > 100 );
	}

	[TestMethod]
	public void EveryThemeCanResearchEverythingAndChallengesResolve()
	{
		UseOriginalData();
		foreach ( var theme in Themes )
		{
			var park = ParkEconomy.CreateForTheme( theme, easy: false );
			Assert.IsTrue( park.Research.Items.Count > 20, theme );
			for ( var hire = 0; hire < 3; hire++ )
			{
				while ( !park.Staff.Candidates.Any( candidate => candidate.Type == StaffType.Researcher ) )
					park.Advance( ParkCalendar.TicksForHours( 1 ) );
				EconomyTestData.HireBest( park, StaffType.Researcher );
			}
			park.OpenPark();
			while ( !park.Research.IsAllResearched && !park.IsBankrupt && park.Date.Year <= 40 )
			{
				for ( var visitor = 0; visitor < 1000; visitor++ )
					park.TryAdmitVisitor( out _ );
				park.AdvanceMonths( 1 );
			}
			Assert.IsTrue( park.Research.IsAllResearched, $"{theme}: all research done by year {park.Date.Year}" );
			Console.WriteLine( $"{theme}: {park.Research.Items.Count} research items done in year {park.Date.Year} with three researchers, balance {park.Balance}." );
			foreach ( var index in park.Settings.ChallengesInThisLevel )
			{
				var definition = park.Settings.Challenges[index];
				if ( definition.TargetObject != 0 )
					Assert.IsTrue( park.Catalog.TryGet( definition.TargetObject, out _ ), $"{theme}: challenge {index} target {definition.TargetObject}" );
			}
		}
	}

	[TestMethod]
	public void EasymodeSaveTablesMatchTheEasyBalanceFile()
	{
		UseOriginalData();
		var park = OriginalPark.Load( "jungle" );
		var records = park.Save!.Economy;
		Assert.AreEqual( park.Save.World.Variables.BankAccount, records.BankAccount.Id, "the bank is the thing mBankAccount names" );
		CollectionAssert.AreEqual( new[] { 100000, 50000, 25000, 10000, 18000, 30000, 80000, 65000 }, records.Loans.Select( loan => loan.Amount ).ToArray() );
		CollectionAssert.AreEqual( new[] { 2777, 1388, 694, 277, 750, 1000, 1666, 2166 }, records.Loans.Select( loan => loan.MonthlyRepayment ).ToArray() );
		CollectionAssert.AreEqual( new[] { false, false, false, true, false, false, false, false }, records.Loans.Select( loan => loan.Available ).ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 8 ).ToArray(), records.Loans.Select( loan => loan.LenderNameIndex ).ToArray() );
		Assert.IsTrue( records.Loans.All( loan => loan.AprPercent == 0 && !loan.Bought && loan.MonthsRepaid == 0 ) );
		CollectionAssert.AreEqual( new[] { 3, 9, 13, 15, 5, 18, 19, 20 }, records.Challenges.Select( challenge => challenge.Type ).ToArray() );
		Assert.AreEqual( (25, 87987, 0, true, 87787, 0u, -12013), (records.Bank.AdmissionFee, records.Bank.Balance, records.Bank.BatchBalance, records.Bank.WithdrawalsEnabled,
			records.Bank.LastBalance, records.Bank.TurnEnteredRed, records.Bank.ProfitThisYear) );

		var easy = new ParkEconomy( BalanceSettings.Load( "jungle", true ), EconomyObjectCatalog.Load( "jungle", true ), ParkGameMode.FullSimulation, 1 );
		var import = OriginalEconomyImport.Apply( easy, park );
		Assert.AreEqual( 11, import.ImportedObjects );
		Assert.AreEqual( 3, import.ImportedFixedItems );
		Assert.AreEqual( 87987, easy.Balance, "the bank balance is imported" );
		Assert.AreEqual( (25, -12013L), (easy.EntranceFee, easy.Ledger.ProfitThisYear) );
		Assert.AreEqual( 755, easy.Turn, "mGameTick is the park turn" );
		Assert.AreEqual( (15L, 604L), (easy.Counters[ParkCounters.FirstPurchaseTurn( 1402 )], easy.Counters[ParkCounters.FirstPurchaseTurn( 1600 )]), "the first purchases come back from the save's object-control table (0x4d3aa0 +0x1c), not the import turn" );
		Assert.AreEqual( new ParkDate( 1, 2, 2, 18 ), easy.Date, "the calendar's month and day caches agree with turn 755" );
		Assert.IsTrue( import.Evidence.Any( line => line.Contains( "agrees with that date" ) ) );
		Assert.AreEqual( 4450, easy.Objects.Where( item => item.Kind != ParkObjectKind.FixedItem ).Sum( item => easy.Catalog.TryGet( item.InfoId, out var info ) ? info.PurchaseCost : 0 ) );
		Assert.IsTrue( easy.Objects.All( item => item.Imported && item.Repair == 100 && item.LifeGauge == 100 && item.Level == 0 ) );
		foreach ( var item in easy.Objects.Where( item => item.Kind is ParkObjectKind.Shop or ParkObjectKind.Sideshow ) )
			Assert.AreEqual( park.Save.Attractions.Single( attraction => attraction.InfoId == item.InfoId ).PricePerUse, item.Price, $"Info.Id {item.InfoId} price" );
		CollectionAssert.AreEquivalent( new[] { StaffType.Mechanic, StaffType.Handyman, StaffType.Entertainer, StaffType.Guard, StaffType.Researcher }, easy.Staff.Members.Select( member => member.Type ).ToArray() );
		var researcher = easy.Staff.OfType( StaffType.Researcher ).Single();
		Assert.AreEqual( (2, 97, StaffState.Patrolling), (researcher.Grade, researcher.Happiness, researcher.State) );
		Assert.IsTrue( researcher.HiredTick > 0 && researcher.HiredTick <= easy.Tick );

		var normal = new ParkEconomy( BalanceSettings.Load( "jungle", false ), EconomyObjectCatalog.Load( "jungle", false ), ParkGameMode.FullSimulation, 1 );
		Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Apply( normal, park ), "20 % APR repayments do not match the save: it was made with Easy_Standard.sam" );
	}

	[TestMethod]
	public void ImportedJunglePaysWagesAndProgressesDates()
	{
		UseOriginalData();
		var economy = ParkEconomy.CreateForTheme( "jungle", easy: true );
		OriginalEconomyImport.Apply( economy, OriginalPark.Load( "jungle" ) );
		var repaired = 0;
		economy.EventRaised += item => repaired += item.Kind == ParkEventKind.RideRepaired ? 1 : 0;
		var mechanic = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		var bouncy = economy.Objects.Single( item => item.InfoId == 1100 );
		Assert.IsTrue( economy.Catalog.TryGet( 1100, out var info ) );
		Assert.AreEqual( 3, info.Upgrades[0].WearRate, "Easy_Bouncy.sam wear rate" );
		Assert.AreEqual( (100, 10), (info.MaxSpeed, info.MaxCapacity), "UsageInfo: Rides.sam MaxSpeed, Bouncy.sam MaxCapacity" );
		// The Belly Bounce runs full at speed 50 for the whole month; nothing else runs.
		var rides = new EconomyTestData.RideOperations();
		var operation = new RideOperation( true, info.MaxCapacity, 50 );
		rides.Set( bouncy.Id, operation );
		economy.RideOperations = rides;
		var firstTurn = economy.Turn;
		var balance = economy.Balance;
		var wages = economy.Staff.TotalMonthlyWages;
		Assert.IsTrue( economy.Staff.Members.Contains( mechanic ) && wages >= economy.Settings.GetMonthlyWage( StaffType.Mechanic, mechanic.Grade ) );
		economy.AdvanceMonths( 1 );
		Assert.AreEqual( new ParkDate( 1, 3, 1, 0 ), economy.Date, "the imported park starts on 2 February" );
		Assert.AreEqual( balance - wages, economy.Balance, "the imported and the hired staff are paid" );
		var steps = economy.Turn / ParkEconomy.WearInterval - firstTurn / ParkEconomy.WearInterval;
		Assert.IsTrue( steps > 0 );
		Assert.AreEqual( 100 - steps * ParkEconomy.WearAmount( info, info.Upgrades[0], operation ), bouncy.Repair, 1e-3, "one original wear step per 64 turns" );
		Assert.AreEqual( 0, repaired, "a month of use leaves the Belly Bounce above the worn threshold" );
		Assert.IsTrue( bouncy.StateOfRepair > ParkEconomy.WornStateOfRepair );
	}

	[TestMethod]
	public void GameModeSelectsBalanceLayersAndEasymodePark()
	{
		// STP-PPC 0x1013781C/0x1010474C/0x10137600: Instant Action (game type 2) loads the Easy_
		// layers and the Easymode park; Full Simulation loads neither.
		UseOriginalData();
		Log ??= new();
		var full = ParkEconomyRuntime.ForOriginalLevel( OriginalPark.Load( "jungle", readShippedSave: false ), ParkStartKind.FullSimulation );
		Assert.IsFalse( full.Economy.Settings.IsEasy );
		Assert.AreEqual( ParkGameMode.FullSimulation, full.Economy.Mode );
		Assert.AreEqual( 0, full.Economy.Objects.Count( item => item.Imported ) );
		Assert.AreEqual( 0, full.Economy.Staff.Members.Count );
		CollectionAssert.AreEqual( new[] { "/levels/Standard.sam", "/levels/jungle/Standard.sam" }, full.Economy.Settings.Standard.Sources.ToArray() );

		var instant = ParkEconomyRuntime.ForOriginalLevel( OriginalPark.Load( "jungle" ), ParkStartKind.InstantAction );
		Assert.IsTrue( instant.Economy.Settings.IsEasy );
		Assert.AreEqual( 87987, instant.Economy.Balance, "the seed's bank balance" );
		Assert.IsTrue( instant.Economy.Objects.Any( item => item.Imported ) );
		Assert.IsFalse( instant.Economy.AvailableLoans.Any() );
		Assert.AreEqual( 1, instant.Economy.Staff.OfType( StaffType.Researcher ).Count(), "the seed's researcher is imported" );

		// Themes without Easy_Standard.sam still load in Instant Action, as in the original.
		var space = BalanceSettings.Load( "space", easy: true );
		CollectionAssert.AreEqual( new[] { "/levels/Standard.sam", "/levels/space/Standard.sam" }, space.Standard.Sources.ToArray() );
	}

	[TestMethod]
	public void RepairTimersFollowTheShippedMechanicAndUpgradeData()
	{
		UseOriginalData();
		var catalog = EconomyObjectCatalog.Load( "jungle", easy: false );
		var work = BalanceSettings.Load( "jungle", easy: false )[StaffType.Mechanic].WorkDuration;
		var durations = catalog[1100].Upgrades.Select( level => level.DurationOfUpgrade ).ToArray();
		CollectionAssert.AreEqual( new[] { 80, 60, 40, 30, 20 }, work.ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 2, 4 }, durations, "Upgrades[level + 1].DurationOfUpgrade of the Belly Bounce" );
		// ORACLE F19: a ride at 40 % repair needs 60 % of the grade's WorkDuration in park turns, whatever its level; an upgrade needs
		// WorkDuration x the DurationOfUpgrade of the level it installs.
		CollectionAssert.AreEqual( new long[] { 48, 36, 24, 18, 12 }, work.Select( duration => ParkEconomy.RepairTimer( duration, 40.0, null ) ).ToArray() );
		CollectionAssert.AreEqual( new long[] { 160, 120, 80, 60, 40 }, work.Select( duration => ParkEconomy.RepairTimer( duration, 40.0, durations[1] ) ).ToArray() );
		CollectionAssert.AreEqual( new long[] { 320, 240, 160, 120, 80 }, work.Select( duration => ParkEconomy.RepairTimer( duration, 40.0, durations[2] ) ).ToArray() );
	}

	private void UseOriginalData()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}
}
