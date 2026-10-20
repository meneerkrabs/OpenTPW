using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The patrol area tool (tp.exe mouse mode 0x36, <c>0x00524960</c>) and the employee's area (<c>0x00506E50</c>, <c>0x00506E90</c>).</summary>
[TestClass]
public class PatrolAreaToolTests
{
	[TestMethod]
	public void TwoClicksSetTheAreaBetweenTheCornersAndEndTheTool()
	{
		var park = EconomyTestData.Park();
		var guard = EconomyTestData.HireBest( park, StaffType.Guard );
		var level = CommandTestLevel.Create( park );
		var tool = new PatrolAreaTool( () => park, command => level.Commands.Issue( command ) );
		Assert.IsTrue( tool.Begin( guard ) );
		Assert.IsTrue( tool.IsActive );
		Assert.IsTrue( tool.Click( 20, 4 ) );
		Assert.AreEqual( (20, 4), tool.FirstCorner );
		Assert.IsFalse( guard.HasPatrolArea, "the first click only stores a corner" );
		tool.Hover( 12, 9 );
		Assert.AreEqual( 2 * 9 + 2 * 6 - 4, tool.PreviewEdge().Count(), "the preview is the rectangle's edge" );
		Assert.IsTrue( tool.Click( 12, 9 ) );
		Assert.AreEqual( (12, 4, 20, 9), guard.PatrolArea, "the smaller x and y make the first corner" );
		Assert.IsFalse( tool.IsActive, "the second click ends the tool" );
		guard.ClearPatrolArea();
		Assert.AreEqual( StaffMember.WholeMap, guard.PatrolArea );
	}

	[TestMethod]
	public void ADrawnAreaIsSavedForAMemberWithoutAPosition()
	{
		var park = EconomyTestData.Park();
		var guard = EconomyTestData.HireBest( park, StaffType.Guard );
		guard.SetPatrolArea( (2, 3), (9, 8) );
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.AreEqual( (2, 3, 9, 8), restored.Staff.Members.Single( member => member.Id == guard.Id ).PatrolArea );
	}

	[TestMethod]
	public void CancelAndADismissedMemberLeaveTheAreaAlone()
	{
		var park = EconomyTestData.Park();
		var guard = EconomyTestData.HireBest( park, StaffType.Guard );
		var level = CommandTestLevel.Create( park );
		var tool = new PatrolAreaTool( () => park, command => level.Commands.Issue( command ) );
		tool.Begin( guard );
		tool.Click( 3, 3 );
		tool.Cancel();
		Assert.IsFalse( tool.IsActive );
		Assert.IsFalse( tool.Click( 8, 8 ) );
		Assert.IsFalse( guard.HasPatrolArea );
		tool.Begin( guard );
		tool.Click( 3, 3 );
		park.Fire( guard.Id );
		Assert.IsFalse( tool.Click( 8, 8 ), "the member is gone" );
		Assert.IsFalse( tool.IsActive );
		Assert.IsFalse( tool.Begin( guard ) );
		Assert.IsFalse( new PatrolAreaTool( () => park, command => level.Commands.Issue( command ) ).Click( 1, 1 ), "not running" );
	}
}
