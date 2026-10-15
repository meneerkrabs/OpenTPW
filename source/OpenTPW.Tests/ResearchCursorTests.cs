using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ResearchCursorTests
{
	private static EconomyObjectCatalog Catalog()
	{
		EconomyObjectInfo Ride( int id, int group, int research = 100 ) => new( id, $"Ride {id}", ParkObjectKind.Ride, ResearchCategory.Ride, group,
			new[] { new UpgradeLevelInfo( 0, 500, research, 1, 0, new[] { 50, 30, 20, 10 } ) }, null, null, null, 0, 0, 0, 0, 0, 25, $"rides/{id}.wad" );
		// 1100 costs no research, so it starts researched (tp.exe 0x5031a0); the others are lab projects
		return new EconomyObjectCatalog( new[] { Ride( 1100, 0, 0 ), Ride( 1120, 1 ), Ride( 1130, 1 ), Ride( 1140, 1 ), Ride( 1150, 2 ) } );
	}

	[TestMethod]
	public void SelectionWrapsTableOrderAndRetainsEachProjectsWorkWithoutCharge()
	{
		var park = new ParkEconomy( EconomyTestData.Settings(), Catalog(), ParkGameMode.FullSimulation, 7 );
		var research = park.Research;
		var cash = park.Balance;
		CollectionAssert.AreEqual( new[] { 1120, 1130, 1140 }, research.SelectableItems( ResearchCategory.Ride ).Select( item => item.InfoId ).ToArray() );
		var first = research.Current( ResearchCategory.Ride )!;
		research.AddResearcherPoints( 10 );
		var points = research.GetProgress( first );
		Assert.IsTrue( research.MoveCurrent( ResearchCategory.Ride, -1 ) );
		Assert.AreEqual( 1140, research.Current( ResearchCategory.Ride )!.InfoId );
		var last = research.Current( ResearchCategory.Ride )!;
		research.AddResearcherPoints( 10 );
		Assert.AreEqual( points, research.GetProgress( first ) );
		Assert.AreEqual( points, research.GetProgress( last ) );
		Assert.IsTrue( research.MoveCurrent( ResearchCategory.Ride, 1 ) );
		Assert.AreEqual( first, research.Current( ResearchCategory.Ride ) );
		Assert.AreEqual( points, research.GetProgress( first ) );
		Assert.IsFalse( research.MoveCurrent( ResearchCategory.Ride, 3 ) );
		Assert.IsFalse( research.MoveCurrent( ResearchCategory.Ride, 0 ) );
		Assert.AreEqual( cash, park.Balance );
	}

	[TestMethod]
	public void CompletionMovesToTheNextEligibleProjectWithWrapAndAllCompleteHasNoCursor()
	{
		// tp.exe 0x004AAEC0: a finished cursor project runs the forward picker 0x005034F0 from its place in the table, wrapping to the first.
		var research = new ParkResearch( EconomyTestData.Settings(), Catalog() );
		Assert.IsTrue( research.MoveCurrent( ResearchCategory.Ride, 1 ) );
		var done = research.AddResearcherPoints( 1000 );
		Assert.AreEqual( 1130, done.Single().InfoId );
		Assert.AreEqual( 1140, research.Current( ResearchCategory.Ride )!.InfoId, "the project after the completed one, not the first" );
		Assert.AreEqual( 1140, research.AddResearcherPoints( 1000 ).Single().InfoId );
		Assert.AreEqual( 1120, research.Current( ResearchCategory.Ride )!.InfoId, "after the last project the cursor wraps to the first" );
		Assert.IsFalse( research.SelectableItems( ResearchCategory.Ride ).Any( item => item.InfoId == 1130 ) );
		while ( !research.IsAllResearched )
			research.AddResearcherPoints( 1000 );
		Assert.IsNull( research.Current( ResearchCategory.Ride ) );
		Assert.AreEqual( 0, research.SelectableItems( ResearchCategory.Ride ).Count );
		Assert.IsFalse( research.MoveCurrent( ResearchCategory.Ride, 1 ) );
	}

	[TestMethod]
	public void ProjectsTakeTheObjectsOwnCategoryAndGroupAndLevelProjectsJoinTheUpgradeCategory()
	{
		// tp.exe 0x005031A0 / 0x00504580: the base project takes the object's Research.Category and Research.Group, an add-on's included; every object whose
		// own category is not 4 gets its paid levels as category-4, group-0 projects (a shop too, though no shipped shop has one).
		UpgradeLevelInfo Level( int level, int research ) => new( level, 100, research, 0, 0, new[] { 50 } );
		var catalog = new EconomyObjectCatalog( new[]
		{
			new EconomyObjectInfo( 1100, "Ride", ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { Level( 0, 0 ), Level( 1, 40 ) }, null, null, null, 0, 0, 0, 0, 0, 0, "rides/a.wad" ),
			new EconomyObjectInfo( 1200, "Shop", ParkObjectKind.Shop, ResearchCategory.Shop, 1, new[] { Level( 0, 30 ), Level( 1, 60 ) }, 10, 5, null, 1, 0, 0, 0, 0, 0, "shops/b.wad" ),
			new EconomyObjectInfo( 1500, "Add-on", ParkObjectKind.Upgrade, ResearchCategory.Upgrade, 2, new[] { Level( 0, 70 ) }, null, null, null, 0, 0, 0, 1100, 0, 0, "upgrades/c.wad" )
		} );
		var items = new ParkResearch( EconomyTestData.Settings(), catalog ).Items;
		Assert.AreEqual( (ResearchCategory.Shop, 1, 0), items.Where( item => item.InfoId == 1200 && item.Level == 0 ).Select( item => (item.Category, item.Group, item.RequiresInfoId) ).Single() );
		Assert.AreEqual( (ResearchCategory.Upgrade, 0, 1200), items.Where( item => item.InfoId == 1200 && item.Level == 1 ).Select( item => (item.Category, item.Group, item.RequiresInfoId) ).Single() );
		Assert.AreEqual( (ResearchCategory.Upgrade, 2, 1100), items.Where( item => item.InfoId == 1500 ).Select( item => (item.Category, item.Group, item.RequiresInfoId) ).Single(), "the add-on keeps its data's group" );
		Assert.AreEqual( (ResearchCategory.Upgrade, 0, 1100), items.Where( item => item.InfoId == 1100 && item.Level == 1 ).Select( item => (item.Category, item.Group, item.RequiresInfoId) ).Single() );
	}

	[TestMethod]
	public void UpgradeSelectionHonorsPrerequisitesAndCannotCycleSingleEligibleProject()
	{
		var research = EconomyTestData.Park().Research;
		var choices = research.SelectableItems( ResearchCategory.Upgrade );
		Assert.AreEqual( 1, choices.Count );
		Assert.AreEqual( (1100, 1), (choices[0].InfoId, choices[0].Level) );
		Assert.IsFalse( research.MoveCurrent( ResearchCategory.Upgrade, 1 ) );
		Assert.IsFalse( choices.Any( item => item.InfoId == 1500 || item.Level == 2 ) );
		Assert.IsFalse( research.MoveCurrent( ResearchCategory.Shop, -1 ) );
	}

	[TestMethod]
	public void SaveRoundTripKeepsChosenProjectAndProgressAndLegacySaveDefaultsToFirst()
	{
		var settings = EconomyTestData.Settings();
		var catalog = Catalog();
		var park = new ParkEconomy( settings, catalog, ParkGameMode.FullSimulation, 7 );
		park.Research.AddResearcherPoints( 10 );
		park.Research.MoveCurrent( ResearchCategory.Ride, 1 );
		park.Research.AddResearcherPoints( 20 );
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), settings, catalog );
		Assert.AreEqual( 1130, restored.Research.Current( ResearchCategory.Ride )!.InfoId );
		foreach ( var item in park.Research.Items )
			Assert.AreEqual( park.Research.GetProgress( item ), restored.Research.GetProgress( item ) );
		restored.Research.AddResearcherPoints( 10 );
		Assert.IsTrue( restored.Research.GetProgress( restored.Research.Current( ResearchCategory.Ride )! ) > park.Research.GetProgress( park.Research.Current( ResearchCategory.Ride )! ) );
		var legacy = JsonNode.Parse( json )!;
		legacy["Research"]!.AsObject().Remove( "Current" );
		var old = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( legacy.ToJsonString() ) ), settings, catalog );
		Assert.AreEqual( 1120, old.Research.Current( ResearchCategory.Ride )!.InfoId );
	}
	[TestMethod]
	public void ZeroWorkloadAndSelectedProjectSurviveSaveAndLegacyUsesSettingsDefault()
	{
		var settings = EconomyTestData.Settings();
		var catalog = Catalog();
		var park = new ParkEconomy( settings, catalog, ParkGameMode.FullSimulation, 7 );
		park.Research.MoveCurrent( ResearchCategory.Ride, 1 );
		park.Research.AddResearcherPoints( 10 );
		park.Research.SetWorkload( 0 );
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), settings, catalog );
		Assert.AreEqual( 0, restored.Research.Workload );
		Assert.AreEqual( 1130, restored.Research.Current( ResearchCategory.Ride )!.InfoId );
		var item = restored.Research.Current( ResearchCategory.Ride )!;
		var points = restored.Research.GetProgress( item );
		restored.Research.AddResearcherPoints( 100 );
		Assert.AreEqual( points, restored.Research.GetProgress( item ) );
		var legacy = JsonNode.Parse( json )!;
		legacy["Research"]!.AsObject().Remove( "Workload" );
		var old = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( legacy.ToJsonString() ) ), settings, catalog );
		Assert.AreEqual( settings.ResearchStartingWorkLoad, old.Research.Workload );
		Assert.AreEqual( 1130, old.Research.Current( ResearchCategory.Ride )!.InfoId );
	}

	[DataTestMethod]
	[DataRow( -1 )]
	[DataRow( 101 )]
	public void SavedWorkloadOutsidePercentageRangeIsRejected( int workload )
	{
		var json = JsonNode.Parse( ParkSaveFile.Serialize( EconomyTestData.Park() ) )!;
		json["Research"]!["Workload"] = workload;
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
	}

}
