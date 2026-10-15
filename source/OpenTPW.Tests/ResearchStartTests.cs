using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>What the lab starts with (tp.exe <c>0x005031A0</c>): an object without research cost is researched, whatever its group; every other one is a project.</summary>
[TestClass]
public class ResearchStartTests
{
	private static EconomyObjectInfo Feature( int id, int group, int research ) => new( id, $"Feature {id}", ParkObjectKind.Feature, ResearchCategory.Feature, group,
		new[] { new UpgradeLevelInfo( 0, 100, research, 0, 0, new[] { 50 } ) }, null, null, null, 0, 0, 0, 0, 0, 10, $"features/{id}.wad" );

	[TestMethod]
	public void AGroupZeroObjectWithAResearchCostIsAProject()
	{
		// Jungle's Wooden Log: group 0, CostOfResearch 300
		var research = new ParkResearch( EconomyTestData.Settings(), new EconomyObjectCatalog( new[] { Feature( 1424, 0, 300 ), Feature( 1400, 0, 0 ) } ) );
		Assert.IsFalse( research.IsAvailable( 1424 ), "0x5031a0 marks only cost-0 types researched" );
		Assert.IsTrue( research.IsAvailable( 1400 ) );
		Assert.AreEqual( 1424, research.Current( ResearchCategory.Feature )?.InfoId, "the group-0 project is open at once" );
		var done = research.AddResearcherPoints( 1000 );
		Assert.IsTrue( done.Any( item => item.InfoId == 1424 ) );
		Assert.IsTrue( research.IsAvailable( 1424 ) );
	}

	[TestMethod]
	public void AnObjectWithoutResearchCostStartsResearchedInAClosedGroup()
	{
		var research = new ParkResearch( EconomyTestData.Settings(), new EconomyObjectCatalog( new[] { Feature( 1400, 0, 200 ), Feature( 1410, 3, 0 ) } ) );
		Assert.IsFalse( research.IsGroupOpen( ResearchCategory.Feature, 3 ) );
		Assert.IsTrue( research.IsAvailable( 1410 ), "no group test at the start (0x5031a0)" );
	}
}
