using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ChallengePresentationStateTests
{
	private const long Day = ParkCalendar.SecondsPerDay;
	private const long Start = 540 * Day;

	private static ChallengeDefinition Definition( int type, int followup = 0, bool endOnly = false, int targetObject = 0 ) =>
		new( 0, type, followup, 10, 1000, targetObject, 1100, 0, 700, endOnly, true );

	private static ParkEconomy Park( params ChallengeDefinition[] definitions )
	{
		var settings = EconomyTestData.Settings();
		settings.AdoptChallenges( definitions );
		return new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
	}

	private static void OfferAndAccept( ParkEconomy park )
	{
		park.Objectives.Update( Start, 1, _ => 0 ).ToList();
		park.Objectives.AcceptAt( Start, 0 );
	}

	[TestMethod]
	public void FourthTurnsCopyStoredStateWithoutCheckingEvenAtOrPastTheDeadline()
	{
		var park = Park( Definition( 11 ) );
		var messages = new List<ChallengePresentation>();
		park.Objectives.PresentationPosted += messages.Add;
		Assert.IsFalse( park.Objectives.Update( Start - 1, 4, _ => throw new InvalidOperationException() ).Any() );
		park.Objectives.Update( Start, 1, _ => 0 ).ToList();
		Assert.AreEqual( ChallengePresentationKind.Offer, messages.Single().Kind );
		Assert.AreEqual( 0, park.Objectives.Current!.RawValue );
		park.Objectives.Update( Start + Day, 4, _ => throw new InvalidOperationException() ).ToList();
		Assert.AreEqual( 1, messages.Count, "an unanswered offer produces no progress" );
		park.Objectives.AcceptAt( Start, 0 );
		park.Counters.Add( ParkCounters.Admissions, 3 );
		Assert.AreEqual( 3, park.Objectives.Current!.RawValue, "events update the snapshot before any checked turn" );
		var deadline = park.Objectives.Current.EndSeconds;
		foreach ( var (seconds, days) in new[] { (Start, 10), (deadline, 0), (deadline + Day - 1, 0), (deadline + Day, -1) } )
		{
			Assert.IsFalse( park.Objectives.Update( seconds, 4, _ => throw new InvalidOperationException(), guestCensus: () => throw new InvalidOperationException() ).Any() );
			Assert.AreEqual( (ChallengePresentationKind.Progress, 3, days), (messages[^1].Kind, messages[^1].RawValue, messages[^1].DaysLeft) );
		}
		Assert.IsTrue( park.Objectives.Current.Accepted );
		Assert.AreEqual( ParkEventKind.ChallengeFailed, park.Objectives.Update( deadline + Day, 5, _ => 3 ).Single().Kind );
		Assert.AreEqual( ChallengePresentationKind.Failure, messages[^1].Kind );
		Assert.AreEqual( 3, messages[^1].RawValue );
	}

	[TestMethod]
	public void CountEventsRespectTargetAndCarriedObjectFiltersAndDoNotDoubleCountRides()
	{
		var park = Park( Definition( 3 ) );
		OfferAndAccept( park );
		park.Counters.Add( ParkCounters.Sold( 2, 3 ), 2 );
		park.Counters.Add( ParkCounters.Sold( 4, 3 ), 4 );
		park.Counters.Add( ParkCounters.Sold( 2, 2 ), 50 );
		Assert.AreEqual( 6, park.Objectives.Current!.RawValue );
		park.Objectives.RestoreCarried( 42, null );
		park.Counters.Add( ParkCounters.Sold( 2, 3 ), 9 );
		Assert.AreEqual( 6, park.Objectives.Current.RawValue );

		var mismatched = Park( Definition( 3, targetObject: 1180 ) );
		OfferAndAccept( mismatched );
		mismatched.Counters.Add( ParkCounters.Sold( 2, 3 ), 9 );
		Assert.AreEqual( 0, mismatched.Objectives.Current!.RawValue );

		foreach ( var type in new[] { 17, 20, 22 } )
		{
			var rides = Park( Definition( type, targetObject: 1180 ) );
			var carried = rides.RegisterExisting( 1180 );
			var other = rides.RegisterExisting( 1180 );
			OfferAndAccept( rides );
			rides.Objectives.RestoreCarried( carried.Id, null );
			rides.Counters.Add( ParkCounters.RideUses( 1180 ), 3 );
			rides.Counters.Add( ParkCounters.RideUsesOfThing( other.Id ), 2 );
			rides.Counters.Add( ParkCounters.RideUsesOfThing( carried.Id ), 1 );
			Assert.AreEqual( 1, rides.Objectives.Current!.RawValue, $"type {type}: only the matching instance counter" );
			rides.Objectives.AccumulateCounter( ParkCounters.RideUsesOfThing( carried.Id ), 8, _ => 1100 );
			Assert.AreEqual( 1, rides.Objectives.Current.RawValue, "carried object alone does not override TargetObject" );
		}
	}

	[TestMethod]
	public void EveryCountTypeReceivesOnlyItsEligibleCounter()
	{
		var cases = new (int Type, string Key)[]
		{
			(1, ParkCounters.Sold( 4, 2 )), (2, ParkCounters.Sold( 2, 1 )), (3, ParkCounters.Sold( 4, 3 )), (4, ParkCounters.Sold( 2, 4 )),
			(5, ParkCounters.Sold( 1, 0 )), (6, ParkCounters.Sold( 6, 0 )), (7, ParkCounters.Sold( 5, 0 )), (8, ParkCounters.Sold( 3, 0 )),
			(11, ParkCounters.Admissions), (12, ParkCounters.ShopProfit), (13, ParkCounters.SideshowProfit), (16, ParkCounters.Researched( ResearchCategory.Ride )),
			(17, ParkCounters.RideUses( 0 )), (20, ParkCounters.RideUses( 0 )), (22, ParkCounters.RideUses( 0 ))
		};
		foreach ( var (type, key) in cases )
		{
			var park = Park( Definition( type ) );
			OfferAndAccept( park );
			park.Counters.Add( "unrelated", 999 );
			park.Counters.Add( key, -7 );
			Assert.AreEqual( -7, park.Objectives.Current!.RawValue, $"type {type} retains signed raw accumulation" );
		}
	}

	[TestMethod]
	public void DailySumsRemainRawAndFailureDoesNotUseTheDividedScoringValue()
	{
		foreach ( var type in new[] { 24, 25, 27 } )
		{
			var park = Park( Definition( type, endOnly: true ) );
			OfferAndAccept( park );
			var messages = new List<ChallengePresentation>();
			park.Objectives.PresentationPosted += messages.Add;
			park.Objectives.AccumulateDaily( _ => 60 );
			park.Objectives.AccumulateDaily( _ => 30 );
			park.Objectives.Update( Start + Day, 4, _ => throw new InvalidOperationException() ).ToList();
			Assert.AreEqual( 90, messages.Single().RawValue );
			var failed = park.Objectives.Update( Start + 10 * Day, 5, _ => throw new InvalidOperationException() ).Single();
			Assert.AreEqual( 9L, failed.Amount, "the existing gameplay event keeps its divided value" );
			Assert.AreEqual( 90, messages[^1].RawValue, "the failure presentation keeps the sum" );
		}
	}

	[TestMethod]
	public void PercentagesAndSkillUpdateOnlyOnAllowedCheckedTurnsAndPositiveCensusRetainsSnapshots()
	{
		foreach ( var type in new[] { 9, 10, 29 } )
		foreach ( var endOnly in new[] { false, true } )
		{
			var park = Park( Definition( type, endOnly: endOnly ) );
			OfferAndAccept( park );
			park.Objectives.Update( Start + Day, 3, _ => 37, guestCensus: () => 2 ).ToList();
			Assert.AreEqual( endOnly ? 0 : 37, park.Objectives.Current!.RawValue );
			park.Objectives.Update( Start + Day, 4, _ => throw new InvalidOperationException(), guestCensus: () => throw new InvalidOperationException() ).ToList();
			park.Objectives.Update( Start + Day, 5, _ => 81, guestCensus: () => 0 ).ToList();
			Assert.AreEqual( endOnly ? 0 : type == 29 ? 81 : 37, park.Objectives.Current.RawValue );
			park.Objectives.Update( Start + Day, 6, _ => 92, guestCensus: () => -1 ).ToList();
			Assert.AreEqual( endOnly ? 0 : type == 29 ? 92 : 37, park.Objectives.Current.RawValue );
			var messages = new List<ChallengePresentation>();
			park.Objectives.PresentationPosted += messages.Add;
			park.Objectives.Update( Start + 10 * Day, 7, _ => 53, guestCensus: () => 1 ).ToList();
			Assert.AreEqual( (ChallengePresentationKind.Failure, 53), (messages.Single().Kind, messages.Single().RawValue), "deadline check is allowed" );
		}
	}

	[TestMethod]
	public void EveryImportedTypeRetainsArbitraryRawStateThroughFourthTurnsAndSaveRoundTrips()
	{
		for ( var type = 1; type <= 33; type++ )
		{
			var park = Park( Definition( type ) );
			var index = park.Settings.ChallengesInThisLevel[0];
			park.RestoreOriginalChallenges( 550, 0, index, 550, -1234, null, new Dictionary<int, int>(), Array.Empty<int>() );
			Assert.AreEqual( -1234, park.Objectives.Current!.RawValue, $"import type {type}" );
			var text = ParkSaveFile.Serialize( park );
			var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( text ) ), park.Settings, park.Catalog );
			var messages = new List<ChallengePresentation>();
			restored.Objectives.PresentationPosted += messages.Add;
			restored.Objectives.Update( Start + Day, 4, _ => throw new InvalidOperationException() ).ToList();
			Assert.AreEqual( -1234, messages.Single().RawValue, $"saved type {type}" );
			if ( !ParkObjectives.IsCountSinceAcceptance( type ) && !ParkObjectives.IsDailyAverage( type ) && type is not (9 or 10 or 29) )
			{
				restored.Objectives.Update( Start + Day, 3, _ => -1 ).ToList();
				Assert.AreEqual( -1234, restored.Objectives.Current!.RawValue, "absolute checks never overwrite imported display state" );
			}
		}
	}

	[TestMethod]
	public void OldSavesInferOnlyCountsAndDailySumsWithoutFreshAbsoluteSnapshots()
	{
		foreach ( var type in new[] { 11, 24, 9, 10, 14, 29, 33 } )
		{
			var park = Park( Definition( type ) );
			OfferAndAccept( park );
			park.Counters.Add( ParkCounters.Admissions, 12 );
			park.Objectives.AccumulateDaily( _ => 21 );
			var node = JsonNode.Parse( ParkSaveFile.Serialize( park ) )!;
			Assert.IsTrue( node["Objectives"]!["Current"]!.AsObject().Remove( "RawValue" ) );
			var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), park.Settings, park.Catalog );
			Assert.AreEqual( type == 11 ? 12 : type == 24 ? 21 : 0, restored.Objectives.Current!.RawValue );
		}
	}

	[TestMethod]
	public void FollowupPresentationUsesThePostOfferSlotWithoutChangingGenericEventOrder()
	{
		foreach ( var nextFollowup in new[] { 0, 13 } )
		{
			var park = Park( Definition( 11, followup: 12 ), Definition( 12, followup: nextFollowup ) );
			OfferAndAccept( park );
			park.Counters.Add( ParkCounters.Admissions, 1000 );
			var messages = new List<ChallengePresentation>();
			park.Objectives.PresentationPosted += messages.Add;
			var events = park.Objectives.Update( Start + Day, 3, _ => 1000 ).ToArray();
			CollectionAssert.AreEqual( new[] { ParkEventKind.ChallengeCompleted, ParkEventKind.ChallengeOffered }, events.Select( item => item.Kind ).ToArray() );
			Assert.AreEqual( 0L, events[0].Amount );
			Assert.AreEqual( ChallengePresentationKind.Offer, messages[0].Kind );
			Assert.AreEqual( 12, messages[0].Type );
			Assert.AreEqual( 0, messages[0].RawValue, "a follow-up offer resets raw state" );
			Assert.AreEqual( nextFollowup == 0 ? 2 : 1, messages.Count );
			if ( nextFollowup == 0 )
				Assert.AreEqual( (ChallengePresentationKind.Success, 12, 700), (messages[1].Kind, messages[1].Type, messages[1].Prize) );
			Assert.AreEqual( 11, park.Settings.Challenges[events[0].Index].Type, "the generic completion still describes the old slot" );
			park.Objectives.AcceptAt( Start + Day, 0 );
			park.Counters.Add( ParkCounters.ShopProfit, 6 );
			Assert.AreEqual( 0, messages[0].RawValue, "captured payloads are immutable" );
		}
	}

	[TestMethod]
	public void MissingOrForfeitedFollowupsDoNotInventOldSlotSuccessMessages()
	{
		foreach ( var forfeited in new[] { false, true } )
		{
			var park = Park( forfeited ? new[] { Definition( 11, followup: 12 ), Definition( 12 ) } : new[] { Definition( 11, followup: 12 ) } );
			if ( forfeited )
				park.Objectives.Restore( 540, 0, null, new Dictionary<int, int> { [park.Settings.ChallengesInThisLevel[1]] = park.Settings.ChallengeTiming.DeclinesToForfeit }, Array.Empty<int>(), Array.Empty<GoldenTicketKind>() );
			OfferAndAccept( park );
			var messages = new List<ChallengePresentation>();
			park.Objectives.PresentationPosted += messages.Add;
			Assert.AreEqual( ParkEventKind.ChallengeCompleted, park.Objectives.Update( Start + Day, 3, _ => 1000 ).Single().Kind );
			Assert.AreEqual( 0, messages.Count );
		}
	}
	[TestMethod]
	public void RuntimeForwarderImmediatelyFollowsEconomyReplacementAndDetachesOldObjectives()
	{
		var park = Park( Definition( 11 ) );
		OfferAndAccept( park );
		park.Counters.Add( ParkCounters.Admissions, 4 );
		var runtime = ParkEconomyRuntime.ForResume( park, ParkStartKind.FullSimulation );
		var messages = new List<ChallengePresentation>();
		runtime.ChallengePresentationPosted += messages.Add;
		var path = Path.Combine( Path.GetTempPath(), "opentpw-challenge-" + Guid.NewGuid().ToString( "N" ) + ".json" );
		try
		{
			runtime.Save( path );
			runtime.Load( path );
			park.Objectives.Update( Start + Day, 4, _ => throw new InvalidOperationException() ).ToList();
			Assert.AreEqual( 0, messages.Count, "the previous economy has detached" );
			runtime.Economy.Objectives.Update( Start + Day, 4, _ => throw new InvalidOperationException() ).ToList();
			Assert.AreEqual( (ChallengePresentationKind.Progress, 4), (messages.Single().Kind, messages.Single().RawValue), "no HUD frame is needed to bind the replacement" );
		}
		finally { File.Delete( path ); }
	}

	[TestMethod]
	public void DisplaySnapshotIsSavedButDoesNotChangeTheCanonicalGameplayHash()
	{
		var park = Park( Definition( 11 ), Definition( 12 ) );
		OfferAndAccept( park );
		park.Counters.Add( ParkCounters.Admissions, 4 );
		var original = ParkSaveFile.Serialize( park );
		var node = JsonNode.Parse( original )!;
		node["Objectives"]!["Current"]!["RawValue"] = -51;
		var changed = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), park.Settings, park.Catalog );
		Assert.AreNotEqual( original, ParkSaveFile.Serialize( changed ), "the distinct display snapshot is persisted" );
		var expectedHash = WorldStateHash.Compute( new WorldStateSources { Economy = park } );
		Assert.AreEqual( expectedHash, WorldStateHash.Compute( new WorldStateSources { Economy = changed } ), "presentation does not move the simulation digest" );
		node["Objectives"]!["Current"]!["Baseline"] = 1;
		var scoringChanged = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), park.Settings, park.Catalog );
		Assert.AreNotEqual( expectedHash, WorldStateHash.Compute( new WorldStateSources { Economy = scoringChanged } ), "real scoring state remains in the digest" );
		node["Objectives"]!["Current"]!["Baseline"] = 0;
		node["Objectives"]!["Current"]!["DefinitionIndex"] = park.Settings.ChallengesInThisLevel[1];
		var definitionChanged = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), park.Settings, park.Catalog );
		Assert.AreNotEqual( expectedHash, WorldStateHash.Compute( new WorldStateSources { Economy = definitionChanged } ), "the active definition remains in the digest" );
	}

	[TestMethod]
	public void CounterSnapshotsWrapAt32BitsAndLegacyCarriedMismatchesDefaultToZero()
	{
		var wrapped = Park( Definition( 11 ) );
		OfferAndAccept( wrapped );
		wrapped.Counters.Add( ParkCounters.Admissions, int.MaxValue );
		wrapped.Counters.Add( ParkCounters.Admissions, 1 );
		Assert.AreEqual( int.MinValue, wrapped.Objectives.Current!.RawValue );
		Assert.AreEqual( (long)int.MaxValue + 1, wrapped.Counters[ParkCounters.Admissions], "existing long scoring counters are unchanged" );
		foreach ( var type in new[] { 11, 17 } )
		{
			var park = Park( Definition( type, targetObject: type == 17 ? 1180 : 0 ) );
			var wrongThing = park.RegisterExisting( 1100 );
			OfferAndAccept( park );
			park.Objectives.RestoreCarried( wrongThing.Id, null );
			park.Counters.Add( ParkCounters.Admissions, 9 );
			park.Counters.Add( ParkCounters.RideUsesOfThing( wrongThing.Id ), 9 );
			var node = JsonNode.Parse( ParkSaveFile.Serialize( park ) )!;
			node["Objectives"]!["Current"]!.AsObject().Remove( "RawValue" );
			var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), park.Settings, park.Catalog );
			Assert.AreEqual( 0, restored.Objectives.Current!.RawValue, "legacy hydration must not infer ineligible carried-object events" );
		}
	}

}
