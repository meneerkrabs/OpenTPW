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
					park.TryAdmitVisitor( 100, out _ );
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
		var payload = ReadEasymode();
		var records = SaveEconomyRecords.Parse( payload );
		Assert.AreEqual( 1411390, records.Loans[0].Offset );
		CollectionAssert.AreEqual( new[] { 100000, 50000, 25000, 10000, 18000, 30000, 80000, 65000 }, records.Loans.Select( loan => loan.Amount ).ToArray() );
		CollectionAssert.AreEqual( new[] { 2777, 1388, 694, 277, 750, 1000, 1666, 2166 }, records.Loans.Select( loan => loan.MonthlyRepayment ).ToArray() );
		CollectionAssert.AreEqual( new[] { false, false, false, true, false, false, false, false }, records.Loans.Select( loan => loan.Available ).ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 8 ).ToArray(), records.Loans.Select( loan => loan.LenderNameIndex ).ToArray() );
		Assert.IsTrue( records.Loans.All( loan => loan.AprPercent == 0 && !loan.Bought && loan.MonthsRepaid == 0 ) );
		Assert.AreEqual( 1410409, records.Challenges[0].Offset );
		CollectionAssert.AreEqual( new[] { 3, 9, 13, 15, 5, 18, 19, 20 }, records.Challenges.Select( challenge => challenge.Type ).ToArray() );
		Assert.AreEqual( new SaveBankRecord( 1411362, 25, 87987, 0, true, 87787, 0, -12013 ), records.Bank );

		var easy = new ParkEconomy( BalanceSettings.Load( "jungle", true ), EconomyObjectCatalog.Load( "jungle", true ), ParkGameMode.FullSimulation, 1 );
		var park = OriginalPark.Load( "jungle" );
		var import = OriginalEconomyImport.Apply( easy, park );
		Assert.AreEqual( 11, import.ImportedObjects );
		Assert.AreEqual( 3, import.ImportedFixedItems );
		Assert.AreEqual( 100000, easy.Balance, "balance import remains outside this record-decoding correction" );
		Assert.AreEqual( 4450, easy.Objects.Where( item => item.Kind != ParkObjectKind.FixedItem ).Sum( item => easy.Catalog.TryGet( item.InfoId, out var info ) ? info.PurchaseCost : 0 ) );
		Assert.IsTrue( easy.Objects.All( item => item.Imported ) );

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
		economy.AdvanceMonths( 1 );
		Assert.AreEqual( new ParkDate( 1, 2, 1, 0 ), economy.Date );
		Assert.AreEqual( 100000 - economy.Settings.GetMonthlyWage( StaffType.Mechanic, mechanic.Grade ), economy.Balance );
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
		Assert.IsFalse( full.Economy.SeedResearcherStandIn );
		CollectionAssert.AreEqual( new[] { "/levels/Standard.sam", "/levels/jungle/Standard.sam" }, full.Economy.Settings.Standard.Sources.ToArray() );

		var instant = ParkEconomyRuntime.ForOriginalLevel( OriginalPark.Load( "jungle" ), ParkStartKind.InstantAction );
		Assert.IsTrue( instant.Economy.Settings.IsEasy );
		Assert.AreEqual( 100000, instant.Economy.Balance );
		Assert.IsTrue( instant.Economy.Objects.Any( item => item.Imported ) );
		Assert.IsFalse( instant.Economy.AvailableLoans.Any() );
		Assert.IsTrue( instant.Economy.SeedResearcherStandIn, "the seed's undecoded researcher has an ECON-019 stand-in" );

		// Themes without Easy_Standard.sam still load in Instant Action, as in the original.
		var space = BalanceSettings.Load( "space", easy: true );
		CollectionAssert.AreEqual( new[] { "/levels/Standard.sam", "/levels/space/Standard.sam" }, space.Standard.Sources.ToArray() );
	}

	private static byte[] ReadEasymode()
	{
		using var stream = FileSystem.OpenRead( "/levels/jungle/Easymode.TPWI" );
		using var reader = new SaveReader( stream );
		return reader.ReadFile();
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
