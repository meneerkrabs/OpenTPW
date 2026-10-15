using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The month-end training split of the original (0x100FC1C4 / 0x100F3588, ORACLE.md F17, ECON-013). The synthetic settings price
/// training at 5, 8, 12, 15 and 0 pounds per point for grades 0 to 4 in every role.
/// </summary>
[TestClass]
public class StaffTrainingTests
{
	private static ParkStaff Staff( out System.Func<StaffType, int, int, StaffMember> add )
	{
		var staff = new ParkStaff( EconomyTestData.Settings() );
		add = ( type, grade, points ) => staff.AddImported( type, grade, points, 100, StaffState.Idle, 0 );
		return staff;
	}

	[TestMethod]
	public void TheBudgetIsDividedOverEveryEmployeeOfTheRoleIncludingTheTopGrade()
	{
		var staff = Staff( out var add );
		var handymen = new[] { add( StaffType.Handyman, 0, 0 ), add( StaffType.Handyman, 0, 0 ), add( StaffType.Handyman, 4, 0 ) };
		var mechanics = new[] { add( StaffType.Mechanic, 1, 0 ), add( StaffType.Mechanic, 1, 0 ) };
		staff.SetTrainingBudget( StaffType.Handyman, 1000 );
		staff.SetTrainingBudget( StaffType.Mechanic, 1000 );
		var spent = staff.Train();
		// 1000 / 3 = 333 for each handyman: 333 / 5 = 66 points, the top-grade one is not trained and its share stays unspent.
		CollectionAssert.AreEqual( new[] { 66, 66, 0 }, handymen.Select( member => member.TrainingPoints ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 0, 4 }, handymen.Select( member => member.Grade ).ToArray() );
		// 1000 / 2 = 500 for each mechanic: 500 / 8 = 62 points; the whole share is debited, not 62 x 8 = 496.
		CollectionAssert.AreEqual( new[] { 62, 62 }, mechanics.Select( member => member.TrainingPoints ).ToArray() );
		Assert.AreEqual( 333 * 2 + 500 * 2, spent );
	}

	[TestMethod]
	public void AShareBelowThePriceBuysNothingAndIsStillDebited()
	{
		var staff = Staff( out var add );
		var handyman = add( StaffType.Handyman, 0, 7 );
		staff.SetTrainingBudget( StaffType.Handyman, 4 );
		Assert.AreEqual( 4, staff.Train() );
		Assert.AreEqual( (0, 7), (handyman.Grade, handyman.TrainingPoints) );
	}

	[TestMethod]
	public void APromotionKeepsThePointsAboveTheGradeBoundary()
	{
		var staff = Staff( out var add );
		var nearly = add( StaffType.Handyman, 0, 99 );
		staff.SetTrainingBudget( StaffType.Handyman, 300 );
		Assert.AreEqual( 300, staff.Train() );
		Assert.AreEqual( (1, 59), (nearly.Grade, nearly.TrainingPoints) ); // 99 + 60 = 159: one grade and 59 points

		var far = add( StaffType.Mechanic, 0, 0 );
		staff.SetTrainingBudget( StaffType.Handyman, 0 );
		staff.SetTrainingBudget( StaffType.Mechanic, 5000 );
		Assert.AreEqual( 5000, staff.Train() ); // 5000 / 8 = 625 points, at most 100 are given, the whole share is spent
		Assert.AreEqual( (1, 0), (far.Grade, far.TrainingPoints) );
	}

	[TestMethod]
	public void TheTopGradeIsNeverTrainedOrCharged()
	{
		var staff = Staff( out var add );
		var top = add( StaffType.Researcher, 4, 0 );
		staff.SetTrainingBudget( StaffType.Researcher, 1000 );
		Assert.AreEqual( 0, staff.Train() );
		Assert.AreEqual( (4, 0), (top.Grade, top.TrainingPoints) );
	}

	[TestMethod]
	public void ARoleWithoutBudgetOrEmployeesSpendsNothing()
	{
		var staff = Staff( out var add );
		var guard = add( StaffType.Guard, 0, 10 );
		Assert.AreEqual( 0, staff.Train() );
		staff.SetTrainingBudget( StaffType.Entertainer, 900 ); // nobody to train
		Assert.AreEqual( 0, staff.Train() );
		Assert.AreEqual( (0, 10), (guard.Grade, guard.TrainingPoints) );
	}
}
