using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>What a staff member costs (docs/reverse/ORACLE.md F28): hiring is free in the Mac binary, a month's wage is paid at the month's end and a dismissal costs one more.</summary>
[TestClass]
public class StaffChargeTests
{
	[TestMethod]
	public void HiringCostsNothing()
	{
		var park = EconomyTestData.Park();
		var candidate = park.Staff.Candidates.First();
		var before = park.Balance;
		park.Hire( candidate.Id );
		Assert.AreEqual( before, park.Balance, "no routine of the original's hire path debits the bank" );
		Assert.AreEqual( 0, park.Ledger.CurrentMonth( park.ParkRating, park.ParkValue )[LedgerCategory.StaffCosts] );
	}

	[TestMethod]
	public void DismissingAnEmployeeCostsOneMonthlyWage()
	{
		var park = EconomyTestData.Park();
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var wage = park.Settings.GetMonthlyWage( StaffType.Mechanic, mechanic.Grade );
		Assert.IsTrue( wage > 0 );
		var balance = park.Balance;
		var profit = park.Ledger.ProfitThisYear;
		park.Fire( mechanic.Id );
		Assert.AreEqual( balance - wage, park.Balance, "0xf33ec debits the wage the employee would have been paid" );
		Assert.AreEqual( profit - wage, park.Ledger.ProfitThisYear );
		Assert.AreEqual( wage, park.Ledger.CurrentMonth( park.ParkRating, park.ParkValue )[LedgerCategory.StaffCosts], "booked as a staff cost" );
		var fired = events.Single( item => item.Kind == ParkEventKind.StaffFired );
		Assert.AreEqual( wage, fired.Amount );
		Assert.AreEqual( 0, park.Staff.Members.Count );
		// The dismissed employee is not paid at the month's end any more.
		park.AdvanceMonths( 1 );
		Assert.AreEqual( balance - wage, park.Balance, "no wage at the month's end" );
	}

	[TestMethod]
	public void TheMonthlyWageIsA32BitProduct()
	{
		// 0xf46bc multiplies the two words with mullw: 65536 x 65536 wraps to 0.
		var text = EconomyTestData.StandardText()
			.Replace( "PerGradeStaffConsts[0].BaseWage\t4", "PerGradeStaffConsts[0].BaseWage\t65536" )
			.Replace( "PerTypeStaffConsts[0].PayMultiplier\t10", "PerTypeStaffConsts[0].PayMultiplier\t65536" );
		var settings = new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( text, "Standard.sam" ) } ), null, null, "jungle", false );
		Assert.AreEqual( 0, settings.GetMonthlyWage( StaffType.Handyman, 0 ) );
		Assert.AreEqual( 5L * 10, EconomyTestData.Settings().GetMonthlyWage( StaffType.Handyman, 1 ) );
	}
}
