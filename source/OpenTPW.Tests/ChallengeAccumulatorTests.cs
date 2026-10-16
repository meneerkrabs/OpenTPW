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

	[TestMethod]
	public void ASavedDeclinedCountAtTheLimitSkipsTheSlotAndPlayNeverRaisesIt()
	{
		// 0x4d2a70 / 0x4d2b60 skip a slot whose declined count is not below DeclinesToForfeit (2); the decline 0x4d2ec0 never raises it
		var objectives = Objectives( 25, 50, 60 );
		Assert.AreEqual( 9, objectives.AdvanceDay( 540, _ => 0 ).Single().Index );
		for ( var offer = 0; offer < 4; offer++ )
		{
			objectives.Decline( 540 + offer );
			Assert.AreEqual( 0, objectives.Declines.Count );
			var next = objectives.AdvanceDay( 540 + offer + 300, _ => 0 ).Single();
			Assert.AreEqual( ParkEventKind.ChallengeOffered, next.Kind );
		}
		var restored = Objectives( 25, 50, 60 );
		restored.Restore( 540, 0, null, new System.Collections.Generic.Dictionary<int, int> { [9] = 2 }, System.Array.Empty<int>(), System.Array.Empty<GoldenTicketKind>() );
		Assert.AreEqual( 0, restored.AdvanceDay( 600, _ => 0 ).Count( item => item.Index == 9 ), "a saved count at the limit is skipped" );
	}

	[TestMethod]
	public void TheFirstOfferWaitsForAnOpenParkAndAfterwardsOnlyTheNextEventTimeCounts()
	{
		// 0x4d1e90: switch off: park open (0x4c9050) and the park's age at DaysUntilFirstChallenge; then the switch stays on
		var objectives = Objectives( 25, 50, 60 );
		Assert.IsFalse( objectives.SwitchedOn );
		Assert.AreEqual( 0, objectives.AdvanceDay( 540, _ => 0, parkOpen: false ).Count(), "day 540 with the park closed" );
		Assert.AreEqual( 0, objectives.AdvanceDay( 700, _ => 0, parkOpen: false ).Count() );
		Assert.IsFalse( objectives.SwitchedOn );
		Assert.AreEqual( 9, objectives.AdvanceDay( 701, _ => 0, parkOpen: true ).Single().Index, "the park opens: the offer comes at once" );
		Assert.IsTrue( objectives.SwitchedOn );
		objectives.Decline( 701 );
		Assert.AreEqual( ParkEventKind.ChallengeOffered, objectives.AdvanceDay( 701 + 270, _ => 0, parkOpen: false ).Single().Kind, "the next offer no longer needs an open park" );
	}

	[TestMethod]
	public void TheChallengeSwitchSurvivesAParkSaveAndOlderSavesInferIt()
	{
		var park = EconomyTestData.Park();
		park.OpenPark();
		park.AdvanceDays( 540 );
		Assert.IsTrue( park.Objectives.SwitchedOn );
		var text = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( text ) ), park.Settings, park.Catalog );
		Assert.IsTrue( restored.Objectives.SwitchedOn );
		var fresh = EconomyTestData.Park();
		Assert.IsFalse( ParkSaveFile.Serialize( fresh ).Contains( "SwitchedOn" ), "absent while off" );
		Assert.IsFalse( ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( fresh ) ) ), fresh.Settings, fresh.Catalog ).Objectives.SwitchedOn );
		var node = System.Text.Json.Nodes.JsonNode.Parse( text )!;
		Assert.IsTrue( node["Objectives"]!.AsObject().Remove( "SwitchedOn" ) );
		var legacy = node.ToJsonString();
		Assert.IsTrue( ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( legacy ) ), park.Settings, park.Catalog ).Objectives.SwitchedOn, "an older save past the first offer day counts as started" );
	}
}
