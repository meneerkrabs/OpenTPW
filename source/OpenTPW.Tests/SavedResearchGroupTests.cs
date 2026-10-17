using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A lab's highest open groups are the saved ones (tp.exe group serializer 0x504760: the last byte of each group record, read back as it is; only the opener 0x503B80 raises it), SAVE-007.
/// </summary>
[TestClass]
public class SavedResearchGroupTests
{
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

	[TestMethod]
	public void GroupsOutsideTheRangeAreRefused()
	{
		var research = EconomyTestData.Park().Research;
		Assert.ThrowsException<InvalidDataException>( () => research.RestoreHighestGroups( new[] { 1, 1, 1, 1, ParkResearch.MaximumOpenGroup + 1 } ) );
		Assert.ThrowsException<InvalidDataException>( () => research.RestoreHighestGroups( new[] { 1, 1, 1 } ) );
		Assert.ThrowsException<InvalidDataException>( () => research.RestoreHighestGroups( new[] { 1, 1, 1, 1, -1 } ) );
	}
}
