using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The challenge accumulators of tp.exe: the shop-sale types (0x4e16b0) and the daily-average types 24, 25 and 27 (0x4d2860, 0x4d1660).
/// </summary>
[TestClass]
public class ChallengeAccumulatorTests
{
	private static ParkObjectives Objectives( int type, int targetValue, int targetTime, int staffType = 0, bool checkAtEndOnly = false ) =>
		new( Settings( type, targetValue, targetTime, staffType, checkAtEndOnly ), EconomyTestData.Catalog() );

	/// <summary>The test level's challenges with index 9 (of the given type) first in <c>ChallengesInThisLevel</c>.</summary>
	private static BalanceSettings Settings( int type, int targetValue, int targetTime, int staffType = 0, bool checkAtEndOnly = false ) => new(
		new SamSettings( new[] { SamDocument.Parse( EconomyTestData.StandardText().Replace( "ChallengesInThisLevel[0].ChallengeType 1\n", "ChallengesInThisLevel[0].ChallengeType 9\n" ), "Standard.sam" ) } ),
		new SamSettings( new[] { SamDocument.Parse( EconomyTestData.ChallengesText
			+ $"Challenges[9].Type {type}\nChallenges[9].FollowupType 0\nChallenges[9].TargetTime {targetTime}\nChallenges[9].TargetVal {targetValue}\nChallenges[9].TargetObj 0\nChallenges[9].TargetObj2 0\nChallenges[9].TargetStaffType {staffType}\nChallenges[9].Prize 1000\nChallenges[9].CheckAtEndOnly {(checkAtEndOnly ? 1 : 0)}\nChallenges[9].Independent 1\n", "Challenges.sam" ) } ),
		null, "jungle", false );

	private static ChallengeDefinition Definition( int type, int staffType = 0 ) => new( 0, type, 0, 60, 10, 0, 0, staffType, 1000, false, true );

	[TestMethod]
	public void AShopSaleCountsForTheTypeOfItsShopTypeAndSpecialIngredient()
	{
		// 0x4e16b0: ShopType 2 or 4 by the ingredient 1, 2, 3, 4 to the types 2, 1, 3, 4; ShopType 1 type 5; 3 type 8; 5 type 7; 6 type 6
		var park = EconomyTestData.Park();
		long Measure( int type ) => park.Objectives.Measure( Definition( type ), park.Counters, park.Staff, park.Objects, park.GuestStatistics );
		park.Counters.Add( ParkCounters.Sold( 2, 2 ), 3 );
		park.Counters.Add( ParkCounters.Sold( 4, 2 ), 4 );
		park.Counters.Add( ParkCounters.Sold( 4, 3 ), 5 );
		park.Counters.Add( ParkCounters.Sold( 2, 3 ), 1 );
		park.Counters.Add( ParkCounters.Sold( 4, 0 ), 100 );
		park.Counters.Add( ParkCounters.Sold( 2, 1 ), 7 );
		park.Counters.Add( ParkCounters.Sold( 4, 4 ), 2 );
		Assert.AreEqual( (7L, 7L, 6L, 2L), (Measure( 1 ), Measure( 2 ), Measure( 3 ), Measure( 4 )), "a drinks shop sale without an ingredient adds nothing" );
		park.Counters.Add( ParkCounters.Sold( 1, 0 ), 2 );
		park.Counters.Add( ParkCounters.Sold( 3, 0 ), 3 );
		park.Counters.Add( ParkCounters.Sold( 5, 0 ), 4 );
		park.Counters.Add( ParkCounters.Sold( 6, 0 ), 5 );
		Assert.AreEqual( (2L, 5L, 4L, 3L), (Measure( 5 ), Measure( 6 ), Measure( 7 ), Measure( 8 )) );
	}

	[TestMethod]
	public void TheToiletCleanlinessOfTheDailyMessageIs100MinusTheAverageDirtiness()
	{
		// 0x4ca160: the integer average of 100 - the gauge byte over the toilets, 0 without one; 0x4d2860 adds 100 minus it
		var park = EconomyTestData.Park();
		long Measure() => park.Objectives.Measure( Definition( 24 ), park.Counters, park.Staff, park.Objects, park.GuestStatistics );
		Assert.AreEqual( 100, Measure(), "no toilet: the average is 0, so the day adds 100" );
		park.RegisterExisting( 1402 ).Repair = 80;
		park.RegisterExisting( 1402 ).Repair = 55.9;
		Assert.AreEqual( 100 - (20 + 45) / 2, Measure(), "the gauge single is truncated: 55.9 is 55" );
	}

	[TestMethod]
	public void TheStaffHappinessAveragesTruncateTheSinglesAndUseTheChallengesStaffType()
	{
		var park = EconomyTestData.Park();
		long Measure( int type, int staffType = 0 ) => park.Objectives.Measure( Definition( type, staffType ), park.Counters, park.Staff, park.Objects, park.GuestStatistics );
		Assert.AreEqual( (0L, 0L), (Measure( 25 ), Measure( 27 )), "without staff" );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		handyman.Happiness = 71.9f;
		mechanic.Happiness = 30f;
		Assert.AreEqual( (71 + 30) / 2, Measure( 25 ), "all staff" );
		Assert.AreEqual( 71, Measure( 27, 0 ), "TargetStaffType 0 is the handymen" );
		Assert.AreEqual( 30, Measure( 27, 1 ), "TargetStaffType 1 is the mechanics" );
		Assert.AreEqual( 0, Measure( 27, 3 ), "no guards" );
	}

	[TestMethod]
	public void TheDailyAverageTypesAddUpTheDailyValuesAndCompareTheSumDividedByTargetTime()
	{
		// 0x4d2860 adds each day's value while the challenge is accepted; 0x4d1660 tests TargetVal <= sum / TargetTime
		var objectives = Objectives( 25, 50, 4 );
		Assert.AreEqual( 9, objectives.AdvanceDay( 540, _ => 0 ).Single().Index );
		objectives.Accept( 540, 123 );
		Assert.AreEqual( 0, objectives.Current!.Baseline, "the sum starts at 0 whatever is measured at the accept" );
		Assert.AreEqual( 0, objectives.AdvanceDay( 541, _ => 60 ).Count(), "60 / 4 = 15" );
		Assert.AreEqual( 60, objectives.Current!.Baseline );
		Assert.AreEqual( 0, objectives.AdvanceDay( 542, _ => 100 ).Count(), "160 / 4 = 40" );
		var result = objectives.AdvanceDay( 543, _ => 40 ).Single();
		Assert.AreEqual( (ParkEventKind.ChallengeCompleted, 9), (result.Kind, result.Index), "200 / 4 = 50 reaches TargetVal before the deadline" );
	}

	[TestMethod]
	public void ADailyAverageChallengeChecksTheSumAtTheDeadlineWhenCheckAtEndOnly()
	{
		var objectives = Objectives( 24, 20, 3, checkAtEndOnly: true );
		objectives.AdvanceDay( 540, _ => 0 ).ToList();
		objectives.Accept( 540, 0 );
		Assert.AreEqual( 0, objectives.AdvanceDay( 541, _ => 90 ).Count(), "270 / 3 = 90 would pass, but only the deadline counts" );
		Assert.AreEqual( 0, objectives.AdvanceDay( 542, _ => 0 ).Count() );
		var result = objectives.AdvanceDay( 543, _ => 0 ).Single();
		Assert.AreEqual( ParkEventKind.ChallengeCompleted, result.Kind, "90 / 3 = 30 at the deadline" );

		var failing = Objectives( 24, 40, 3, checkAtEndOnly: true );
		failing.AdvanceDay( 540, _ => 0 ).ToList();
		failing.Accept( 540, 0 );
		failing.AdvanceDay( 541, _ => 90 ).ToList();
		failing.AdvanceDay( 542, _ => 0 ).ToList();
		Assert.AreEqual( ParkEventKind.ChallengeFailed, failing.AdvanceDay( 543, _ => 0 ).Single().Kind, "90 / 3 = 30 < 40" );
	}

	[TestMethod]
	public void AnImportedSavedValueIsTheRunningSum()
	{
		// the original's system value (+0x3dc) of a running daily-average challenge is its sum; a count type keeps measuring against a baseline
		var park = new ParkEconomy( Settings( 25, 50, 60 ), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		park.RestoreOriginalChallenges( 600, 1, 9, 600, 1234, null, new System.Collections.Generic.Dictionary<int, int>(), System.Array.Empty<int>() );
		Assert.AreEqual( 1234, park.Objectives.Current!.Baseline );
	}
}
