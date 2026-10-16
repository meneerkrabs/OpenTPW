using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A staff candidate's draws (tp.exe <c>0x00507600</c>): the sprite variant, one discarded draw, the grade, the name (drawn again, at most 15 times, while
/// a candidate or employee has it, <c>0x00507580</c>) and the timeout.
/// </summary>
[TestClass]
public class StaffCandidateDrawTests
{
	[TestMethod]
	public void TheInitialPoolDrawsInTheOriginalOrder()
	{
		var settings = EconomyTestData.Settings();
		var staff = new ParkStaff( settings );
		var random = new WorldRng( 0x1234 );
		var replay = new WorldRng( 0x1234 );
		staff.CreateInitialPool( random, 0 );
		var taken = new HashSet<(StaffType, int)>();
		var expected = new List<(StaffType Type, int Name, int Grade)>();
		foreach ( var role in settings.Roles )
		{
			for ( var count = 0; count < Math.Min( role.BeginningNumberInPool, Math.Max( role.MaximumInPool, role.MinimumInPool ) ); count++ )
			{
				replay.NextWord(); // the sprite variant, 0x541f70
				replay.NextWord(); // discarded, 0x50762d
				var raw = (role.AverageGrade + replay.Next( 3 ) - 1) & 0xFF;
				var grade = raw > BalanceSettings.GradeCount - 1 ? BalanceSettings.GradeCount - 1 : raw;
				int name, draws = 0;
				do
				{
					name = replay.Next( ParkStaff.NameTableSize );
					draws++;
				}
				while ( taken.Contains( (role.Type, name) ) && draws < ParkStaff.MaximumNameDraws );
				taken.Add( (role.Type, name) );
				if ( settings.StaffTimeoutTime >> 1 > 0 )
					replay.Next( settings.StaffTimeoutTime >> 1 );
				expected.Add( (role.Type, name, grade) );
			}
		}
		CollectionAssert.AreEqual( expected, staff.Candidates.Select( candidate => (candidate.Type, candidate.NameIndex, candidate.Grade) ).ToList() );
		Assert.AreEqual( replay.State, random.State, "no other draw" );
	}

	[TestMethod]
	public void ARoleNeverRepeatsANameWhileFifteenDrawsFindAFreeOne()
	{
		var park = EconomyTestData.Park( seed: 99 );
		for ( var month = 0; month < 6; month++ )
		{
			park.AdvanceMonths( 1 );
			var names = park.Staff.Candidates.Select( candidate => (candidate.Type, candidate.NameIndex) ).Concat( park.Staff.Members.Select( member => (member.Type, member.NameIndex) ) ).ToList();
			Assert.AreEqual( names.Count, names.Distinct().Count(), $"month {month}: {string.Join( ", ", names )}" );
			if ( park.Staff.Candidates.FirstOrDefault( candidate => park.Staff.CanHire( candidate.Type ) ) is { } hire )
				park.Hire( hire.Id );
		}
	}
}
