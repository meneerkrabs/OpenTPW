using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A lab's highest open groups are the saved ones (tp.exe group serializer 0x504760: the last byte of each group record, read back as it is; only the opener 0x503B80 raises it).
/// </summary>
[TestClass]
public class SavedResearchGroupTests
{
	private static ParkEconomy HigherGroupPark()
	{
		EconomyObjectInfo Feature( int id, int group ) => new( id, $"Feature {id}", ParkObjectKind.Feature, ResearchCategory.Feature, group,
			new[] { new UpgradeLevelInfo( 0, 100, 100, 0, 0, new[] { 50 } ) }, null, null, null, 0, 0, 0, 0, 0, 0, $"features/{id}.wad" );
		return new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( new[] { Feature( 1400, 0 ), Feature( 1410, 3 ) } ), ParkGameMode.FullSimulation, 7 );
	}

	[TestMethod]
	public void OwnSaveRestoresHigherGroupsBeforeValidatingTheSelectedProject()
	{
		var park = HigherGroupPark();
		Assert.AreEqual( 1, park.Research.HighestOpenGroup( ResearchCategory.Feature ), "the synthetic group-1 threshold is zero; group 2 still requires 80 percent completion" );
		Assert.IsFalse( park.Research.TrySelect( ResearchCategory.Feature, 1410, 0 ) );
		var groups = Enum.GetValues<ResearchCategory>().Select( park.Research.HighestOpenGroup ).ToArray();
		groups[(int)ResearchCategory.Feature] = 3;
		park.Research.RestoreHighestGroups( groups );
		Assert.IsTrue( park.Research.MoveCurrent( ResearchCategory.Feature, 1 ), "host selection can choose the newly opened group" );
		Assert.AreEqual( 1410, park.Research.Current( ResearchCategory.Feature )!.InfoId );

		var saved = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) );
		CollectionAssert.AreEqual( groups, saved.Research.HighestGroups );
		CollectionAssert.AreEqual( new[] { (int)ResearchCategory.Feature, 1410, 0 }, saved.Research.Current!.Single() );
		var restored = ParkSaveFile.Restore( saved, park.Settings, park.Catalog );
		CollectionAssert.AreEqual( groups, Enum.GetValues<ResearchCategory>().Select( restored.Research.HighestOpenGroup ).ToArray() );
		Assert.AreEqual( 1410, restored.Research.Current( ResearchCategory.Feature )!.InfoId );
		Assert.AreEqual( 0, restored.Research.Current( ResearchCategory.Feature )!.Level );
	}

	/// <summary>A lab loaded from a save keeps its saved current project as it is, offered by the picker now or not (SAVE-007); it ends when the project is finished or the player moves on.</summary>
	[TestMethod]
	public void ASavedCurrentProjectThePickerWouldNotOfferIsKeptAsSaved()
	{
		var park = HigherGroupPark();
		Assert.IsFalse( park.Research.TrySelect( ResearchCategory.Feature, 1410, 0 ), "the group is closed: the picker would not offer it" );
		Assert.AreEqual( 1400, park.Research.Current( ResearchCategory.Feature )!.InfoId );
		Assert.IsTrue( park.Research.SelectAsSaved( ResearchCategory.Feature, 1410, 0 ) );
		Assert.AreEqual( 1410, park.Research.Current( ResearchCategory.Feature )!.InfoId, "kept as the loader reads it" );
		Assert.IsFalse( park.Research.SelectAsSaved( ResearchCategory.Feature, 9999, 0 ), "a project the lab does not hold cannot be kept" );
		// it survives the park's own save
		var saved = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) );
		CollectionAssert.AreEqual( new[] { (int)ResearchCategory.Feature, 1410, 0 }, saved.Research.Current!.Single() );
		var restored = ParkSaveFile.Restore( saved, park.Settings, park.Catalog );
		Assert.AreEqual( 1410, restored.Research.Current( ResearchCategory.Feature )!.InfoId );
	}

	[DataTestMethod]
	[DataRow( 9999, 0, (int)ResearchCategory.Ride )]
	[DataRow( 1100, 0, (int)ResearchCategory.Ride )]
	public void SavedGroupsDoNotPermitUnknownCompletedOrPrerequisiteBlockedCursors( int id, int level, int category )
	{
		var park = EconomyTestData.Park();
		park.Research.RestoreHighestGroups( new[] { 7, 7, 7, 7, 7 } );
		Assert.IsFalse( park.Research.TrySelect( (ResearchCategory)category, id, level ) );
		var json = JsonNode.Parse( ParkSaveFile.Serialize( park ) )!;
		json["Research"]!["Current"] = new JsonArray( new JsonArray( category, id, level ) );
		var saved = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) );
		var error = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( saved, park.Settings, park.Catalog ) );
		Assert.AreEqual( $"Saved research cursor refers to an invalid project {id} level {level}.", error.Message );
	}

	[TestMethod]
	public void AbsentSavedGroupsDoNotOpenASelectedProjectsGroupAndItsCursorIsKept()
	{
		var park = HigherGroupPark();
		var json = JsonNode.Parse( ParkSaveFile.Serialize( park ) )!;
		Assert.IsNull( json["Research"]!["HighestGroups"] );
		json["Research"]!["Current"] = new JsonArray( new JsonArray( (int)ResearchCategory.Feature, 1410, 0 ) );
		var saved = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) );
		// the cursor is kept as saved (the loader runs no picker over it, SAVE-007) and opens no group
		var restored = ParkSaveFile.Restore( saved, park.Settings, park.Catalog );
		Assert.AreEqual( 1, restored.Research.HighestOpenGroup( ResearchCategory.Feature ) );
		Assert.AreEqual( 1410, restored.Research.Current( ResearchCategory.Feature )!.InfoId );
	}

	[TestMethod]
	public void SavedHighestGroupsReplaceTheRecomputedOnesAndAreKeptByOwnSaves()
	{
		var park = EconomyTestData.Park();
		var research = park.Research;
		Assert.IsNull( research.HighestGroupsToSave, "a new park's groups are the ones its researched projects give" );
		var recomputed = Enum.GetValues<ResearchCategory>().Select( research.HighestOpenGroup ).ToArray();
		var saved = recomputed.Select( group => Math.Min( ParkResearch.MaximumOpenGroup, group + 3 ) ).ToArray();
		if ( saved.SequenceEqual( recomputed ) )
			saved[0] = recomputed[0] == 0 ? 1 : 0;
		research.RestoreHighestGroups( saved );
		CollectionAssert.AreEqual( saved, Enum.GetValues<ResearchCategory>().Select( research.HighestOpenGroup ).ToArray() );
		Assert.IsTrue( research.IsGroupOpen( ResearchCategory.Ride, saved[0] ) );
		CollectionAssert.AreEqual( saved, research.HighestGroupsToSave!.ToArray() );

		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );
		CollectionAssert.AreEqual( saved, Enum.GetValues<ResearchCategory>().Select( restored.Research.HighestOpenGroup ).ToArray(), "a park save keeps the imported groups" );
	}

	[TestMethod]
	public void AParkSaveWithoutTheFieldRecomputesTheGroups()
	{
		var park = EconomyTestData.Park();
		var json = ParkSaveFile.Serialize( park );
		StringAssert.DoesNotMatch( json, new System.Text.RegularExpressions.Regex( "HighestGroups" ), "the field is written only where it differs" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.IsNull( restored.Research.HighestGroupsToSave );
	}

	[DataTestMethod]
	[DataRow( "[0,0,0,0,8]" )]
	[DataRow( "[0,0,0,0,-1]" )]
	[DataRow( "[0,0,0]" )]
	public void OwnSaveRejectsMalformedHighestGroups( string groups )
	{
		var json = JsonNode.Parse( ParkSaveFile.Serialize( HigherGroupPark() ) )!;
		json["Research"]!["HighestGroups"] = JsonNode.Parse( groups );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
	}

	[TestMethod]
	public void GroupsOutsideTheRangeAreRefused()
	{
		var research = EconomyTestData.Park().Research;
		Assert.ThrowsException<InvalidDataException>( () => research.RestoreHighestGroups( new[] { 1, 1, 1, 1, ParkResearch.MaximumOpenGroup + 1 } ) );
		Assert.ThrowsException<InvalidDataException>( () => research.RestoreHighestGroups( new[] { 1, 1, 1 } ) );
		Assert.ThrowsException<InvalidDataException>( () => research.RestoreHighestGroups( new[] { 1, 1, 1, 1, -1 } ) );
	}
}
