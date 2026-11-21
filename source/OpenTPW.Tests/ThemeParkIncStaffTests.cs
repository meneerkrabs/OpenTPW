using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's staff settings and formulas (docs/reverse/TPI-STAFF.md). The formulas are checked against the executable by
/// <c>tools/ppc-analysis/oracle_x86/tpi_staff_formula_oracle.py</c>; these tests pin the C# port on the retail global <c>Standard.sam</c>
/// when <c>OPENTPW_TPI_PATH</c> names the data, and on synthetic values otherwise.
/// </summary>
[TestClass]
public class ThemeParkIncStaffTests
{
	private static ThemeParkIncStaffSettings RetailSettings()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		var file = Directory.EnumerateFiles( Path.Combine( data, "levels" ) ).First( name => string.Equals( Path.GetFileName( name ), "Standard.sam", StringComparison.OrdinalIgnoreCase ) );
		return new ThemeParkIncStaffSettings( new SamSettings( new[] { SamDocument.Parse( File.ReadAllText( file ), file ) } ) );
	}

	[TestMethod]
	public void TheInterpolationHelperClampsAndRoundsTowardZero()
	{
		// [BIN:TPI-EXE:0x62C900 staff lerp] cases the oracle runs natively
		Assert.AreEqual( 7, ThemeParkIncStaffSettings.Lerp( 11, 0, 10, 3, 7 ), "above the range: b" );
		Assert.AreEqual( 3, ThemeParkIncStaffSettings.Lerp( -1, 0, 10, 3, 7 ), "below the range: a" );
		Assert.AreEqual( 5, ThemeParkIncStaffSettings.Lerp( 5, 0, 10, 3, 7 ), "half way" );
		Assert.AreEqual( -2, ThemeParkIncStaffSettings.Lerp( 1, 0, 3, -3, 0 ), "toward zero" );
		Assert.ThrowsException<DivideByZeroException>( () => ThemeParkIncStaffSettings.Lerp( 4, 4, 4, 1, 2 ), "an empty range divides by zero, as the executable's idiv does" );
	}

	[TestMethod]
	public void TheRetailStaffSettingsAreTheExecutablesKeys()
	{
		var settings = RetailSettings();
		Assert.AreEqual( 100000, settings.MaxExperience );
		Assert.AreEqual( 25, settings.MotivationFactor );
		Assert.AreEqual( 12, settings.MinMonthsForStrike );
		Assert.AreEqual( 0.2f, settings.MinRecuperationRate );
		Assert.IsTrue( settings.Agencies.Count >= 3 && settings.Agencies[0].RequiredPromotionLevel == 0 && settings.Agencies[1].RequiredPromotionLevel == 1 );
		// [BIN:TPI-EXE:0x631C12 gardener pool keys] the gardener takes the handyman's pool keys, not MaxGardeners (6) or MinGardenersInPool
		var handyman = settings.Roles[ThemeParkIncStaffRole.Handyman];
		var gardener = settings.Roles[ThemeParkIncStaffRole.Gardener];
		Assert.AreEqual( handyman.MaxInPool, gardener.MaxInPool );
		Assert.AreEqual( handyman.MinInPool, gardener.MinInPool );
		Assert.AreEqual( handyman.BeginningNumber, gardener.BeginningNumber );
		Assert.AreEqual( handyman.AverageExperience, gardener.AverageExperience );
		Assert.AreEqual( handyman.ExperiencePerMonth, gardener.ExperiencePerMonth );
		Assert.AreEqual( 30, gardener.MaxInPark, "MaxGardenersInPark is the gardener's own" );
		// [BIN:TPI-EXE:0x62E4C0 work duration pairs] the researcher's pair is its research ability
		Assert.AreEqual( (2, 6), (settings.Roles[ThemeParkIncStaffRole.Researcher].MinWorkDuration, settings.Roles[ThemeParkIncStaffRole.Researcher].MaxWorkDuration) );
		Assert.AreEqual( 1, settings.AvailableAgencies( 0 ), "a new career reaches the first agency only" );
		Assert.AreEqual( 2, settings.AvailableAgencies( 1 ) );
	}

	[DataTestMethod]
	// [BIN:TPI-EXE:0x62CD30 expected wage] retail MaxExperience 100000: the native routine's answers (oracle)
	[DataRow( 20000, 80, 10 )]
	[DataRow( 18000, 250, 40 )]
	[DataRow( 100000, 300, 300 )]
	[DataRow( 99999, 300, 280 )]
	[DataRow( -1, 80, 10 )]
	public void TheExpectedWageFollowsExperience( int experience, int top, int expected )
	{
		Assert.AreEqual( expected, RetailSettings().ExpectedWage( experience, top ) );
	}

	[DataTestMethod]
	// [BIN:TPI-EXE:0x62E4C0 work duration] retail pairs and MotivationFactor 25: the native routine's answers (oracle)
	[DataRow( ThemeParkIncStaffRole.Mechanic, 20000, 100f, 80, 69 )]
	[DataRow( ThemeParkIncStaffRole.Mechanic, 5000, 100f, 9, 200 )]
	[DataRow( ThemeParkIncStaffRole.Handyman, 50000, 60f, 120, 23 )]
	[DataRow( ThemeParkIncStaffRole.Handyman, 0, 100f, 10, 40 )]
	[DataRow( ThemeParkIncStaffRole.Entertainer, 50000, 60f, 120, 41 )]
	[DataRow( ThemeParkIncStaffRole.Entertainer, 5000, 100f, 9, 1 )]
	[DataRow( ThemeParkIncStaffRole.Guard, 99999, 100f, 200, 74 )]
	[DataRow( ThemeParkIncStaffRole.Researcher, 50000, 60f, 120, 3 )]
	[DataRow( ThemeParkIncStaffRole.Gardener, 20000, 100f, 80, 25 )]
	[DataRow( ThemeParkIncStaffRole.Gardener, 0, 100f, 10, 30 )]
	public void TheWorkDurationFollowsExperienceAndMotivation( ThemeParkIncStaffRole role, int experience, float motivation, int salary, int expected )
	{
		Assert.AreEqual( expected, RetailSettings().WorkDuration( role, experience, motivation, salary ) );
	}

	[DataTestMethod]
	// [BIN:TPI-EXE:0x6302C0 candidate] the retail global Standard.sam, a new career (rank 0, one agency) and the world generator at state 12345: the values the
	// oracle's port of the native constructor gives for the same words (tpi_staff_candidate_oracle.py)
	[DataRow( ThemeParkIncStaffRole.Handyman, 29090, 22 )]
	[DataRow( ThemeParkIncStaffRole.Mechanic, 26890, 66 )]
	public void ACandidateDrawsItsAgencySpriteExperienceNameAndTimeoutInTheExecutablesOrder( ThemeParkIncStaffRole role, int experience, int wage )
	{
		var world = new WorldRng( 12345 );
		var candidate = ThemeParkIncStaffCandidates.Generate( RetailSettings(), role, world, 0, 7, 1000, ( _, _ ) => false );
		Assert.AreEqual( 0, candidate.Agency, "a new career reaches only the first agency" );
		Assert.AreEqual( 339536374u, candidate.SpriteDraw, "the sprite takes the second word (the first went to the agency, [EXT:tpi-staff-crt-rand])" );
		Assert.AreEqual( experience, candidate.Experience );
		Assert.AreEqual( wage, candidate.Wage );
		Assert.AreEqual( 31, candidate.NameIndex );
		Assert.AreEqual( (1000L, 128), (candidate.Stamp, candidate.Timeout), "the turn and StaffTimeoutTime 120 plus R % 60" );
		Assert.AreEqual( 7, candidate.Id );
	}

	[TestMethod]
	public void TheWageReviewMovesMotivationAndHappinessAndSetsTheDemandDay()
	{
		// [BIN:TPI-EXE:0x62CDA0 wage review] retail values: UnHappyWageVal 5, HappyWageVal 2, LowestWageHappiness 20 and agency 0 (MotivationGain/LossModifier 6,
		// InitialWageModifier 10); a handyman of 20000 experience expects 10 + 10 (the oracle checks the routine natively)
		var settings = RetailSettings();
		var member = new ThemeParkIncStaffMember( 1, ThemeParkIncStaffRole.Handyman, 3, 20000, 15, 0, 0 );
		var world = new WorldRng( 12345 );
		member.ReviewWage( settings, world );
		Assert.AreEqual( 83f, member.Motivation, "x = 5 × 7 = 35 costs 17 motivation" );
		Assert.AreEqual( 91.25f, member.Happiness, "and 35 × 0.25 happiness" );
		Assert.AreEqual( 5, member.WageShortfall );
		Assert.AreEqual( 0, member.WageDemandDay, "motivation above 20 demands nothing" );
		Assert.AreEqual( 12345u, world.State, "and draws nothing" );
		member.Salary = 20;
		member.ReviewWage( settings, world );
		Assert.AreEqual( (97f, 100f, 0), (member.Motivation, member.Happiness, member.WageShortfall), "a fair salary raises both by 2 × 7, happiness up to 100" );
		member.Salary = 15;
		member.Motivation = 30f;
		member.ReviewWage( settings, world );
		Assert.AreEqual( 13f, member.Motivation );
		Assert.AreEqual( 19, member.WageDemandDay, "motivation at most 20 sets a demand day, R % 27 + 1 ([EXT:tpi-staff-crt-rand])" );
	}

	[TestMethod]
	public void TheDaysWorkingShareGivesTheExperienceGainAndTheCountersStartAgain()
	{
		// [BIN:TPI-EXE:0x629650 experience gain] [BIN:TPI-EXE:0x62D1A0 day reset] retail: handyman ExperiencePerMonth 3, agency 0 ExperienceGainModifier 30
		var settings = RetailSettings();
		var member = new ThemeParkIncStaffMember( 1, ThemeParkIncStaffRole.Handyman, 3, 20000, 15, 0, 0 );
		for ( var tick = 0; tick < 3; tick++ )
			member.CountTick( ThemeParkIncStaffMember.DisplayState.Working );
		member.CountTick( ThemeParkIncStaffMember.DisplayState.Idle );
		member.CountTick( ThemeParkIncStaffMember.DisplayState.PickedUp );
		Assert.AreEqual( 0u, member.DayTicks( ThemeParkIncStaffMember.DisplayState.PickedUp ), "picked up counts nothing" );
		member.EndDay( settings );
		Assert.AreEqual( 20024, member.Experience, "share 0.75: trunc(3 × 0.75) = 2, then trunc(30 × 0.75 + 2) = 24" );
		Assert.AreEqual( 0u, member.DayTicks( ThemeParkIncStaffMember.DisplayState.Working ), "the counters start again" );
		member.EndDay( settings );
		Assert.AreEqual( 20024, member.Experience, "an empty day gains nothing" );
		member.Experience = 99990;
		member.CountTick( ThemeParkIncStaffMember.DisplayState.Researching );
		member.EndDay( settings );
		Assert.AreEqual( 99999, member.Experience, "experience stops one below MaxExperience" );
	}

	[TestMethod]
	public void AStepCostsLessWithExperienceAndRestRecoversUntilEnergyIsFull()
	{
		// [BIN:TPI-EXE:0x62B3E0 staff step] [BIN:TPI-EXE:0x62A1C0 rest] [BIN:TPI-EXE:0x62B3B0 tired] values from the oracle's native-checked mirror; retail
		// Min/MaxRecuperationRate 0.2/0.75, Min/MaxHappinessRecuperationRate 1/3, agency 0 modifiers 10, RestLevel 1
		var settings = RetailSettings();
		var member = new ThemeParkIncStaffMember( 1, ThemeParkIncStaffRole.Handyman, 3, 20000, 15, 0, 0 );
		member.Step( settings );
		Assert.AreEqual( (99.92f, 99.96f), (member.Energy, member.Happiness), "d = 0.8: 0.08 energy and 0.04 happiness" );
		member.Experience = 0;
		member.Energy = 0.05f;
		member.Step( settings );
		Assert.AreEqual( 0f, member.Energy, "energy stops at 0" );
		Assert.IsTrue( member.Tired( settings ), "an energy byte below RestLevel 1" );
		member.Experience = 20000;
		(member.Energy, member.Happiness, member.Motivation) = (50f, 50f, 50f);
		Assert.IsFalse( member.Rest( settings ) );
		Assert.AreEqual( (50.44f, 52.2f, 52.2f), (member.Energy, member.Happiness, member.Motivation), "e = (0.2 + 0.2) × 1.1, h = 2 × 1.1" );
		Assert.IsFalse( member.Tired( settings ) );
		member.Experience = 99999;
		(member.Energy, member.Happiness, member.Motivation) = (99.5f, 99f, 99f);
		Assert.IsTrue( member.Rest( settings ), "the rest ends when the energy reaches 100" );
		Assert.AreEqual( (100f, 100f, 100f), (member.Energy, member.Happiness, member.Motivation) );
	}

	[TestMethod]
	public void ASalaryChangeIsClampedToTheMinimumWageAndReviewedOnlyWhenItChanges()
	{
		// [BIN:TPI-EXE:0x62D160 salary setter] retail MinWage 1
		var settings = RetailSettings();
		var member = new ThemeParkIncStaffMember( 1, ThemeParkIncStaffRole.Handyman, 3, 20000, 15, 0, 0 );
		var world = new WorldRng( 12345 );
		member.SetSalary( settings, 15, world );
		Assert.AreEqual( 100f, member.Motivation, "an unchanged salary is not reviewed" );
		member.SetSalary( settings, -4, world );
		Assert.AreEqual( 1, member.Salary, "at least MinWage" );
		Assert.AreEqual( 83f, member.Motivation, "the changed salary is reviewed: short of the expected 20" );
		Assert.AreEqual( 19, member.WageShortfall );
	}

	[TestMethod]
	public void PaydayDebitsEachSalaryThenReviewsItAndADemandDayAsksOncePerThirtyDays()
	{
		// [BIN:TPI-EXE:0x6265DE staff payday] [BIN:TPI-EXE:0x628CA0 staff day] [BIN:TPI-EXE:0x436E97 message order]
		var staff = RetailStaff();
		staff.CreateOpeningPool( new WorldRng( 99 ), 0, 0 );
		var hired = new ThemeParkIncStaffMember[3];
		for ( var slot = 0; slot < 3; slot++ )
		{
			staff.BeginHire( slot );
			hired[slot] = staff.EndHire( slot, placed: true )!;
		}
		var paid = new System.Collections.Generic.List<(int Id, int Salary)>();
		staff.PayMonth( ( member, salary ) => paid.Add( (member.Id, salary) ), new WorldRng( 1 ) );
		CollectionAssert.AreEqual( hired.Select( member => (member.Id, member.Salary) ).ToArray(), paid.ToArray(), "in ascending handle order" );

		Assert.AreEqual( 100u, ThemeParkIncStaff.GameDay( 2304 ) );
		Assert.AreEqual( 3, ThemeParkIncStaff.ParkAgeMonths( 2304 ) );
		hired[0].WageDemandDay = 5;
		hired[0].WageShortfall = 7;
		hired[1].WageDemandDay = 5;
		hired[1].WageShortfall = 9;
		Assert.AreEqual( 0, staff.EndDay( 2304, 4 ).Count, "only on the demand day of the month" );
		var demand = staff.EndDay( 2304, 5 ).Single();
		Assert.AreEqual( (hired[0], 0xB, 7), (demand.Member, demand.Type, demand.Shortfall), "one demand per day: the next member waits thirty days" );
		Assert.AreEqual( (0, 0), (hired[0].WageDemandDay, hired[0].WageShortfall), "the demand clears its day and shortfall" );
		Assert.AreEqual( 100u, staff.LastWageDemandDay );
		Assert.AreEqual( 0, staff.EndDay( 2304 + 23 * 29, 5 ).Count, "29 days later" );
		foreach ( var member in hired )
			member.Energy = 20.9f;
		demand = staff.EndDay( 2996, 5 ).Single();
		Assert.AreEqual( 130u, ThemeParkIncStaff.GameDay( 2996 ) );
		Assert.AreEqual( (hired[1], 0xA, 9), (demand.Member, demand.Type, demand.Shortfall), "a grieved role in a park younger than MinMonthsForStrike" );
	}

	private static ThemeParkIncStaff RetailStaff() => new( RetailSettings(), ( role, index ) => $"{role} {index}" );

	[TestMethod]
	public void TheOpeningPoolTakesEachRolesBeginningNumberInTheExecutablesOrder()
	{
		// [BIN:TPI-EXE:0x631F30 opening pool] handyman, mechanic, gardener (BeginningNumberOfHandymen), entertainer, guard, researcher: 5, 5, 5, 5, 5 and 4 in the retail data
		var staff = RetailStaff();
		staff.CreateOpeningPool( new WorldRng( 99 ), 7, 0 );
		var roles = staff.Slots.Where( slot => slot != null ).Select( slot => slot!.Role ).ToArray();
		CollectionAssert.AreEqual( Enumerable.Repeat( ThemeParkIncStaffRole.Handyman, 5 ).Concat( Enumerable.Repeat( ThemeParkIncStaffRole.Mechanic, 5 ) )
			.Concat( Enumerable.Repeat( ThemeParkIncStaffRole.Gardener, 5 ) ).Concat( Enumerable.Repeat( ThemeParkIncStaffRole.Entertainer, 5 ) )
			.Concat( Enumerable.Repeat( ThemeParkIncStaffRole.Guard, 5 ) ).Concat( Enumerable.Repeat( ThemeParkIncStaffRole.Researcher, 4 ) ).ToArray(), roles );
		Assert.AreEqual( 7, staff.PoolStamp );
		Assert.AreEqual( 29, staff.Candidates.Select( candidate => $"{candidate.Role} {candidate.NameIndex}" ).Distinct().Count(), "no name text twice while 15 draws find a free one" );
		var state = new WorldRng( 99 );
		staff.CreateOpeningPool( state, 9, 0 );
		Assert.AreEqual( 29, staff.Candidates.Count(), "a second opening pool adds nothing" );
		Assert.AreEqual( 9, staff.PoolStamp, "but the caller still stamps the pool" );
	}

	[TestMethod]
	public void ThePoolRefillsAfterFourTimesTheUpdateIntervalAndCandidatesExpireAfterFourTimesTheirTimeout()
	{
		// [BIN:TPI-EXE:0x631B40 pool update] refill only when more than 4 × TimeBetweenStaffUpdates (90) turns have passed; the shortfalls are handyman 1, mechanic 0,
		// entertainer 1, guard -1, researcher 2, gardener 1 (against MaxHandymen), and the cap is the three free slots
		var staff = RetailStaff();
		var world = new WorldRng( 99 );
		staff.CreateOpeningPool( world, 0, 0 );
		var state = world.State;
		staff.UpdatePool( world, 360, 0 );
		Assert.AreEqual( 29, staff.Candidates.Count() );
		Assert.AreEqual( state, world.State, "no refill and no draw at exactly 4 × 90 turns" );
		staff.UpdatePool( world, 361, 0 );
		Assert.AreEqual( 32, staff.Candidates.Count(), "the refill fills the three free slots" );
		Assert.AreEqual( 361, staff.PoolStamp );
		var shortest = staff.Candidates.Min( candidate => candidate.Timeout );
		var expiring = staff.Candidates.Count( candidate => candidate.Stamp == 0 && candidate.Timeout == shortest );
		staff.SelectedSlot = -1;
		Assert.IsTrue( expiring > 0 && 4 * shortest + 1 - 361 <= 360, "the refill waits while the shortest timeouts run out" );
		staff.UpdatePool( world, 4 * shortest, 0 );
		Assert.AreEqual( 32, staff.Candidates.Count(), "not yet expired at exactly 4 × the timeout" );
		staff.UpdatePool( world, 4 * shortest + 1, 0 );
		Assert.AreEqual( 32 - expiring, staff.Candidates.Count(), "the shortest timeouts expire one turn later" );
	}

	[TestMethod]
	public void AHireConsumesItsCandidateAndACancelledOneReturnsToThePool()
	{
		// [BIN:TPI-EXE:0x630D10 hire] [BIN:TPI-EXE:0x6319E0 placement end]
		var staff = RetailStaff();
		staff.CreateOpeningPool( new WorldRng( 99 ), 0, 0 );
		var candidate = staff.Slots[0]!;
		Assert.IsTrue( staff.BeginHire( 0 ) );
		Assert.IsTrue( staff.IsPlacing( 0 ) );
		Assert.IsNull( staff.EndHire( 0, placed: false ), "a cancelled placement makes no member" );
		Assert.AreSame( candidate, staff.Slots[0], "and returns the candidate to the pool" );
		staff.BeginHire( 0 );
		var member = staff.EndHire( 0, placed: true )!;
		Assert.IsNull( staff.Slots[0], "a placed hire leaves the pool" );
		Assert.AreEqual( (candidate.Role, candidate.Experience, candidate.Wage, candidate.Agency), (member.Role, member.Experience, member.Salary, member.Agency) );
		Assert.AreEqual( (100f, 100f, 100f), (member.Happiness, member.Motivation, member.Energy), "the meters start full" );
		CollectionAssert.Contains( staff.Members.ToArray(), member );
	}

	[TestMethod]
	public void AGrievedRoleWarnsThenStrikesEveryOtherMonthAndSettlesWhenTheGrievanceGoes()
	{
		// [BIN:TPI-EXE:0x632C20 strike month] [BIN:TPI-EXE:0x6330B0 strike consideration] [BIN:TPI-EXE:0x633740 grievance] retail: MinMonthsForStrike 12,
		// MinStaffOfTypeForStrike 3, EnergyPercentToStrike 20
		var staff = RetailStaff();
		staff.CreateOpeningPool( new WorldRng( 99 ), 0, 0 );
		for ( var slot = 0; slot < 3; slot++ )
		{
			staff.BeginHire( slot );
			staff.EndHire( slot, placed: true )!.Energy = 20.9f;
		}
		Assert.AreEqual( 4, staff.Grievance( ThemeParkIncStaffRole.Handyman ), "three handymen whose energy bytes average 20" );
		Assert.AreEqual( 0, staff.Grievance( ThemeParkIncStaffRole.Mechanic ), "fewer than three mechanics" );
		var young = ThemeParkIncStaff.ParkAgeMonths( 8294 );
		Assert.AreEqual( 11, young );
		Assert.AreEqual( 0, staff.ConsiderStrikes( 8294 ).Count, "a park younger than 12 months has no strikes" );
		var turn = 8295L;
		Assert.AreEqual( 12, ThemeParkIncStaff.ParkAgeMonths( turn ) );
		var expected = new (int Kind, int Reason, int Level, bool Active)[]
		{
			(2, 4, 1, false), // the warning carries the grievance
			(2, 0, 2, true), // one week
			(5, 0, 2, false), // the next month's message ends it
			(2, 0, 3, true), // two weeks
			(5, 0, 3, false),
			(2, 0, 4, true), // a month
			(5, 0, 4, false),
		};
		foreach ( var step in expected )
		{
			var messages = staff.ConsiderStrikes( turn++ );
			Assert.AreEqual( new ThemeParkIncStaff.StrikeMessage( ThemeParkIncStaffRole.Handyman, step.Kind, step.Reason ), messages.Single() );
			var record = staff.Strikes[(int)ThemeParkIncStaffRole.Handyman];
			Assert.AreEqual( (step.Level, step.Active), (record.Level, record.Active) );
		}
		Assert.AreEqual( 0, staff.ConsiderStrikes( turn++ ).Count, "the fourth level strikes again as the second, without a message" );
		Assert.AreEqual( (2, true), (staff.Strikes[0].Level, staff.Strikes[0].Active) );
		Assert.AreEqual( 0, staff.ConsiderStrikes( turn - 1 ).Count, "a role is considered once per turn" );
		staff.ConsiderStrikes( turn++ );
		foreach ( var member in staff.Members )
			member.Energy = 100f;
		Assert.AreEqual( new ThemeParkIncStaff.StrikeMessage( ThemeParkIncStaffRole.Handyman, 3, 0 ), staff.ConsiderStrikes( turn++ ).Single(), "the grievance gone" );
		Assert.AreEqual( (0, false), (staff.Strikes[0].Level, staff.Strikes[0].Active) );
	}
}
