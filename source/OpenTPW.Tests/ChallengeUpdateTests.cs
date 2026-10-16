using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The challenge system's update (tp.exe 0x004D1E90, the thing update of the system): every park turn it offers once the next event time has passed and checks a running
/// challenge on the turns whose low two bits are not 0, against an exact deadline (ECON-058).
/// </summary>
[TestClass]
public class ChallengeUpdateTests
{
	private const long Day = ParkCalendar.SecondsPerDay;

	private static ParkObjectives Fresh() => EconomyTestData.Park().Objectives;

	/// <summary>An objectives object with challenge 1 (sell 30 drinks within 60 days) offered at the first second it can be.</summary>
	private static (ParkObjectives Objectives, long Start) Offered()
	{
		var objectives = Fresh();
		var start = 540 * Day;
		Assert.AreEqual( ParkEventKind.ChallengeOffered, objectives.Update( start, 1, _ => 0 ).Single().Kind );
		return (objectives, start);
	}

	[TestMethod]
	public void TheSystemOffersOnTheFirstTurnAtOrAfterTheNextEventTimeNotAtADayEnd()
	{
		var objectives = Fresh();
		Assert.IsFalse( objectives.Update( 540 * Day - 1, 1, _ => 0 ).Any(), "the park is not old enough" );
		Assert.IsFalse( objectives.SwitchedOn );
		Assert.IsFalse( objectives.Update( 540 * Day + 3750, 1, _ => 0, parkOpen: false ).Any(), "the switch needs the park open (0x4c9050)" );
		var offer = objectives.Update( 540 * Day + 3750, 1, _ => 0 ).Single();
		Assert.AreEqual( ParkEventKind.ChallengeOffered, offer.Kind );
		Assert.IsTrue( objectives.SwitchedOn );
		Assert.AreEqual( 540, objectives.Current!.OfferedDay );
	}

	[TestMethod]
	public void AnAcceptedChallengeEndsAtItsExactSecond()
	{
		var (objectives, start) = Offered();
		objectives.AcceptAt( start + 1000, 0 );
		var deadline = start + 1000 + 60 * Day;
		Assert.AreEqual( deadline, objectives.Current!.EndSeconds );
		Assert.IsFalse( objectives.Update( deadline - 1, 1, _ => 0 ).Any(), "a turn before the deadline" );
		var lost = objectives.Update( deadline, 1, _ => 0 ).Single();
		Assert.AreEqual( ParkEventKind.ChallengeFailed, lost.Kind );
		Assert.AreEqual( deadline + 270 * Day, objectives.NextOfferSeconds, "the next offer waits DaysAfterCompletedChallenge from the loss" );
	}

	[TestMethod]
	public void ARunningChallengeIsCheckedOnEveryTurnExceptThoseDivisibleByFour()
	{
		var (objectives, start) = Offered();
		objectives.AcceptAt( start, 0 );
		// 30 drinks sold: the target is reached at once
		long Sold( ChallengeDefinition definition ) => 30;
		foreach ( var skipped in new long[] { 4, 8, 100 } )
			Assert.IsFalse( objectives.Update( start + 3750, skipped, Sold ).Any(), $"turn {skipped} only posts the progress message (0x4d3390)" );
		Assert.AreEqual( ParkEventKind.ChallengeCompleted, objectives.Update( start + 3750, 5, Sold ).Single( item => item.Kind == ParkEventKind.ChallengeCompleted ).Kind, "a pass wins on the first checked turn" );
		Assert.IsNull( objectives.Current );
		Assert.AreEqual( start + 3750 + 270 * Day, objectives.NextOfferSeconds );
	}

	[TestMethod]
	public void ARunningChallengeWarnsWhileFewerDaysAreLeftThanTheSettingAllows()
	{
		// [BIN:TPW-EXE:0x4d21fd challenge time warning] (whole days left x 100) / TargetTime below Challenges.ShortTimeLeftWarningAt (20 here), TargetTime 60 days
		var (objectives, start) = Offered();
		objectives.AcceptAt( start, 0 );
		var deadline = start + 60 * Day;
		var warned = new List<int>();
		objectives.TimeShort += warned.Add;
		objectives.Update( deadline - 12 * Day, 1, _ => 0 ).ToList();
		Assert.AreEqual( 0, warned.Count, "12 days left: 12 x 100 / 60 = 20 is not below 20" );
		objectives.Update( deadline - 12 * Day + 3750, 1, _ => 0 ).ToList();
		Assert.AreEqual( 1, warned.Count, "11 whole days left: 18 is below 20" );
		objectives.Update( deadline - 3 * Day, 4, _ => 0 ).ToList();
		Assert.AreEqual( 1, warned.Count, "turn 4 only posts the progress message" );
		objectives.Update( deadline - 3 * Day, 5, _ => 0 ).ToList();
		Assert.AreEqual( 2, warned.Count, "posted again on every checked turn while it holds" );
		objectives.Update( deadline - 3 * Day, 6, definition => 30 ).ToList();
		Assert.AreEqual( 2, warned.Count, "a challenge that passes wins instead" );
	}

	[TestMethod]
	public void AChallengeCheckedAtItsEndOnlyWinsOnlyAtTheDeadline()
	{
		var settings = new BalanceSettings(
			new SamSettings( new[] { SamDocument.Parse( EconomyTestData.StandardText().Replace( "ChallengesInThisLevel[0].ChallengeType 1\n", "ChallengesInThisLevel[0].ChallengeType 9\n" ), "Standard.sam" ) } ),
			new SamSettings( new[] { SamDocument.Parse( EconomyTestData.ChallengesText
				+ "Challenges[9].Type 3\nChallenges[9].FollowupType 0\nChallenges[9].TargetTime 60\nChallenges[9].TargetVal 30\nChallenges[9].TargetObj 0\nChallenges[9].TargetObj2 0\nChallenges[9].TargetStaffType 0\nChallenges[9].Prize 1000\nChallenges[9].CheckAtEndOnly 1\nChallenges[9].Independent 1\n", "Challenges.sam" ) } ),
			null, "jungle", false );
		var objectives = new ParkObjectives( settings, EconomyTestData.Catalog() );
		Assert.AreEqual( ParkEventKind.ChallengeOffered, objectives.Update( 540 * Day, 1, _ => 0 ).Single().Kind );
		objectives.AcceptAt( 540 * Day, 0 );
		Assert.IsFalse( objectives.Update( 541 * Day, 1, _ => 99 ).Any(), "a pass before the end does not win (0x4d1660 with the flag clear)" );
		Assert.AreEqual( ParkEventKind.ChallengeCompleted, objectives.Update( 600 * Day, 1, _ => 99 ).First().Kind );
	}

	[TestMethod]
	public void TheDayMessageAddsTheDailyAverageValueOnlyWhileRunning()
	{
		var (objectives, start) = Offered();
		objectives.AccumulateDaily( _ => 7 );
		Assert.AreEqual( 0, objectives.Current!.Baseline, "an offer that waits adds nothing" );
		var definition = objectives.CurrentDefinition! with { Type = 24 };
		Assert.IsTrue( ParkObjectives.IsDailyAverage( definition.Type ) );
	}

	[TestMethod]
	public void TheExactTimesSurviveASaveAndOlderSavesTakeDayStarts()
	{
		var park = EconomyTestData.Park();
		park.Objectives.Update( 540 * Day + 1234, 1, _ => 0 ).ToList();
		park.Objectives.AcceptAt( 540 * Day + 1234, 5 );
		var text = ParkSaveFile.Serialize( park );
		StringAssert.Contains( text, "\"DeadlineSeconds\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Capture( park ), park.Settings, park.Catalog );
		Assert.AreEqual( 600 * Day + 1234, restored.Objectives.Current!.EndSeconds );
		// an older save has neither field: the deadline is the start of its day, the next offer the start of its day
		park.Objectives.Restore( 700, 0, new ActiveChallenge( 1, 640, true, 640, 700, 0 ), new System.Collections.Generic.Dictionary<int, int>(), System.Array.Empty<int>(), System.Array.Empty<GoldenTicketKind>() );
		Assert.AreEqual( (700 * Day, 700 * Day), (park.Objectives.Current!.EndSeconds, park.Objectives.NextOfferSeconds) );
		StringAssert.DoesNotMatch( ParkSaveFile.Serialize( park ), new System.Text.RegularExpressions.Regex( "NextOfferSeconds|DeadlineSeconds" ) );
	}
}
