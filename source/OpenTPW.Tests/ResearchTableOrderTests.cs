using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The lab's table is the object definitions in the original's load order (tp.exe 0x005031A0 over 0x00412E60 / 0x00412E70: Rides, Features, Upgrades, Shops, Sideshow, each
/// directory by archive name), not in Info.Id order (ECON-017).
/// </summary>
[TestClass]
public class ResearchTableOrderTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	[TestMethod]
	public void ANewParksTableFollowsTheScanOrderNotTheInfoIdOrder()
	{
		var park = EconomyTestData.Park();
		var kinds = park.Research.Items.Where( item => item.Level == 0 ).Select( item => park.Catalog.TryGet( item.InfoId, out var info ) ? info.Kind : ParkObjectKind.FixedItem ).ToList();
		var ranks = kinds.Select( kind => kind switch { ParkObjectKind.Ride => 0, ParkObjectKind.Feature => 1, ParkObjectKind.Upgrade => 2, ParkObjectKind.Shop => 3, _ => 4 } ).ToList();
		CollectionAssert.AreEqual( ranks.OrderBy( rank => rank ).ToList(), ranks, "rides, features, upgrades, shops, sideshows" );
	}

	[TestMethod]
	public void TheShippedJungleLabHoldsTheProjectsOfANewParkInTheSavedOrder()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		var original = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			var level = OriginalPark.Load( "jungle" );
			var saved = level.Save!.ResearchLab!.Projects.Select( project => (int)BitConverter.ToUInt16( project, 0 ) ).TakeWhile( id => id != 0xCDCD ).ToList();
			var runtime = ParkEconomyRuntime.ForOriginalLevel( level, ParkStartKind.InstantAction );
			var fresh = new ParkResearch( runtime.Economy.Settings, runtime.Economy.Catalog );
			var table = fresh.Items.Where( item => item.Level == 0 && item.Cost > 0 ).Select( item => item.InfoId ).ToList();
			// the saved lab is the shipped Easymode game's: its projects are the new park's base projects with a research cost, in the same order
			// the shipped Easymode game does not offer every object (the coasters and a few more are no projects of its lab), so the saved projects are a subsequence of the new park's table
			CollectionAssert.AreEqual( saved.Take( 24 ).ToList(), table.Where( saved.Contains ).ToList(), "the saved lab's order is the scan order" );
		}
		finally
		{
			FileSystem = original;
		}
	}

	[TestMethod]
	public void AnAddOnWhoseTargetHasATicketCostNeedsTheTargetBoughtWithTickets()
	{
		// tp.exe 0x005034F0: a category-4 project is eligible when its target is researched and, with a golden-ticket cost above 0 (+0xC4), the profile's ticket set holds the target (0x004D4B70)
		var catalog = new EconomyObjectCatalog( EconomyTestData.Catalog().Objects.Select( info => info.InfoId == 1180 ? info with { GoldenTicketCost = 500 } : info ) );
		var park = new ParkEconomy( EconomyTestData.Settings(), catalog, ParkGameMode.FullSimulation, 7 );
		park.Research.Restore( new[] { (1180, 0) }, Array.Empty<((int, int), long)>(), new[] { 100, 0, 0, 0, 0 }, null );
		Assert.IsNull( park.Research.SelectableItems( ResearchCategory.Upgrade ).FirstOrDefault( item => item.InfoId == 1500 ), "the target is researched but not bought with tickets" );
		park.Research.OwnsTicketItem = _ => true;
		Assert.IsNotNull( park.Research.SelectableItems( ResearchCategory.Upgrade ).FirstOrDefault( item => item.InfoId == 1500 ), "the profile owns it" );
		var plain = EconomyTestData.Park();
		plain.Research.Restore( new[] { (1180, 0) }, Array.Empty<((int, int), long)>(), new[] { 100, 0, 0, 0, 0 }, null );
		Assert.IsNotNull( plain.Research.SelectableItems( ResearchCategory.Upgrade ).FirstOrDefault( item => item.InfoId == 1500 ), "a target without a ticket cost needs only the research" );
	}

	private static UpgradeLevelInfo Level( int level, long cost, int research ) => new( level, cost, research, 0, 0, new[] { 0, 0, 0, 0 } );

	/// <summary>Two rides with levels (2001, 2002) and an add-on (1500) over the test catalogue; both rides cost research for every level.</summary>
	private static ParkEconomy ParkWithTwoUpgradableRides()
	{
		var objects = EconomyTestData.Catalog().Objects.Where( info => info.InfoId == 1500 ).Concat( new[]
		{
			new EconomyObjectInfo( 2001, "Ride A", ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { Level( 0, 100, 100 ), Level( 1, 100, 50 ), Level( 2, 100, 60 ) }, null, null, null, 0, 0, 0, 0, 0, 10, "rides/a.wad" ),
			new EconomyObjectInfo( 2002, "Ride B", ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { Level( 0, 100, 100 ), Level( 1, 100, 50 ), Level( 2, 100, 60 ) }, null, null, null, 0, 0, 0, 0, 0, 10, "rides/b.wad" )
		} );
		return new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( objects ), ParkGameMode.FullSimulation, 7 );
	}

	private static void Complete( ParkResearch research, int infoId, int level )
	{
		var item = research.Items.Single( entry => entry.InfoId == infoId && entry.Level == level );
		Assert.IsTrue( research.TrySelect( item.Category, infoId, level ), $"{infoId} level {level} is selectable" );
		for ( var step = 0; step < 1000 && !research.Completed.Contains( (infoId, level) ); step++ )
			research.AddResearcherPoints( 100 );
		Assert.IsTrue( research.Completed.Contains( (infoId, level) ) );
	}

	[TestMethod]
	public void ALevelProjectJoinsTheEndOfTheCategoryFourTableWhenTheLevelBelowCompletes()
	{
		// tp.exe 0x00504580 from the completion handler 0x00503D40: the next level's project is appended at the end, so the order is the order of the completions
		var research = ParkWithTwoUpgradableRides().Research;
		CollectionAssert.AreEqual( new[] { (1500, 0) }, research.UpgradeTable.ToArray(), "no level project before its base is researched" );
		Complete( research, 2002, 0 );
		Complete( research, 2001, 0 );
		CollectionAssert.AreEqual( new[] { (1500, 0), (2002, 1), (2001, 1) }, research.UpgradeTable.ToArray(), "ride B finished first, so its level 1 comes first, though A is first in the scan" );
		CollectionAssert.AreEqual( new[] { (1500, 0), (2002, 1), (2001, 1) }, research.SelectableItems( ResearchCategory.Upgrade ).Select( item => (item.InfoId, item.Level) ).ToArray(), "the picker walks the table in this order" );
		Complete( research, 2002, 1 );
		CollectionAssert.AreEqual( new[] { (1500, 0), (2002, 1), (2001, 1), (2002, 2) }, research.UpgradeTable.ToArray() );
	}

	[TestMethod]
	public void TheTableOrderIsSavedOnlyOnceTheCompletionsMadeItDifferAndSurvivesARestore()
	{
		var park = ParkWithTwoUpgradableRides();
		Assert.IsNull( park.Research.UpgradeTableToSave, "a new park's table is what a restore rebuilds" );
		Complete( park.Research, 2001, 0 );
		Complete( park.Research, 2002, 0 );
		Assert.IsNull( park.Research.UpgradeTableToSave, "completions in the scan order give the rebuilt order" );
		var other = ParkWithTwoUpgradableRides();
		Complete( other.Research, 2002, 0 );
		Complete( other.Research, 2001, 0 );
		Assert.IsNotNull( other.Research.UpgradeTableToSave );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Capture( other ), other.Settings, other.Catalog );
		CollectionAssert.AreEqual( other.Research.UpgradeTable.ToArray(), restored.Research.UpgradeTable.ToArray() );
		// an older save has no order: the level projects follow the scan order of the completed projects
		var rebuilt = ParkWithTwoUpgradableRides();
		rebuilt.Research.Restore( new[] { (2002, 0), (2001, 0) }, Array.Empty<((int, int), long)>(), new[] { 100, 0, 0, 0, 0 } );
		CollectionAssert.AreEqual( new[] { (1500, 0), (2001, 1), (2002, 1) }, rebuilt.Research.UpgradeTable.ToArray() );
	}

	[TestMethod]
	public void ARideResearchedAtStartHasItsFirstLevelProjectBeforeTheAddOns()
	{
		// the walk of the fill 0x005031A0 appends the level project of a free base at the base's turn, and the rides come before the upgrades
		var research = EconomyTestData.Park().Research;
		CollectionAssert.AreEqual( new[] { (1100, 1), (1500, 0) }, research.UpgradeTable.ToArray() );
	}
}
