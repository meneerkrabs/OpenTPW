using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The object a won challenge carries into its follow-up (tp.exe: the check 0x004D1660 writes the best object's thing id into the slot's +0x2E, the win 0x004D2FB0
/// copies it to the system's mActualThing +0x3E0 and into the follow-up's slot, the accumulator 0x004D27A0 filters on it; ECON-055).
/// </summary>
[TestClass]
public class ChallengeCarriedObjectTests
{
	/// <summary>The test level's challenges 7 (type 18, follow-up type 20) and 8 (type 20) in that order.</summary>
	private static ParkObjectives Chain() => new( new BalanceSettings(
		new SamSettings( new[] { SamDocument.Parse( EconomyTestData.StandardText().Replace( "ChallengesInThisLevel[0].ChallengeType 1\nChallengesInThisLevel[1].ChallengeType 7\nChallengesInThisLevel[2].ChallengeType 8\n",
			"ChallengesInThisLevel[0].ChallengeType 7\nChallengesInThisLevel[1].ChallengeType 8\n" ), "Standard.sam" ) } ),
		new SamSettings( new[] { SamDocument.Parse( EconomyTestData.ChallengesText, "Challenges.sam" ) } ), null, "jungle", false ), EconomyTestData.Catalog() );

	private static long Uses( ParkObjectives objectives, ParkCounters counters ) =>
		objectives.Measure( objectives.CurrentDefinition!, counters, new ParkStaff( EconomyTestData.Settings() ), System.Array.Empty<ParkObjectState>(), new EmptyGuests() );

	private sealed class EmptyGuests : IParkGuestStatistics
	{
		public int PeopleInPark => 0;
		public float AverageHappiness => 0;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	private static ParkObjectives AcceptedFollowup( int bestCoaster )
	{
		var objectives = Chain();
		Assert.AreEqual( 7, objectives.AdvanceDay( 540, _ => 0 ).Single().Index );
		objectives.Accept( 540, 0 );
		var results = objectives.AdvanceDay( 541, definition => definition.Type == 18 ? 1 : 0, true, _ => bestCoaster ).ToList();
		Assert.AreEqual( (ParkEventKind.ChallengeCompleted, ParkEventKind.ChallengeOffered), (results[0].Kind, results[1].Kind) );
		return objectives;
	}

	[TestMethod]
	public void APassingCoasterCheckCarriesTheBestObjectIntoTheFollowUp()
	{
		var objectives = AcceptedFollowup( 42 );
		Assert.AreEqual( 42, objectives.ActualThing, "the win copies the slot's +0x2e to mActualThing (0x004D30B1)" );
		Assert.AreEqual( 42, objectives.CarriedThings[8], "the offered follow-up's slot takes it over (0x004D30C2)" );
		Assert.AreEqual( 42, objectives.CarriedThings[7] );
		Assert.AreEqual( 8, objectives.Current!.DefinitionIndex );
	}

	[TestMethod]
	public void WhileTheObjectIsCarriedARideUseCountsOnlyThatObject()
	{
		var objectives = AcceptedFollowup( 42 );
		var counters = new ParkCounters();
		counters.Add( ParkCounters.RideUses( 1180 ), 5 );
		counters.Add( ParkCounters.RideUsesOfThing( 7 ), 5 );
		Assert.AreEqual( 0, Uses( objectives, counters ), "uses of every other object of the type do not count (0x004D27A0)" );
		counters.Add( ParkCounters.RideUsesOfThing( 42 ), 3 );
		Assert.AreEqual( 3, Uses( objectives, counters ) );
	}

	[TestMethod]
	public void WithoutACarriedObjectEveryRideOfTheTypeCounts()
	{
		var objectives = Chain();
		objectives.AdvanceDay( 540, _ => 0 ).ToList();
		objectives.Accept( 540, 0 );
		objectives.AdvanceDay( 541, definition => definition.Type == 18 ? 1 : 0 ).ToList();
		Assert.AreEqual( 0, objectives.ActualThing, "no best object was reported" );
		var counters = new ParkCounters();
		counters.Add( ParkCounters.RideUses( 1180 ), 5 );
		Assert.AreEqual( 5, Uses( objectives, counters ) );
	}

	[TestMethod]
	public void ADeclineAndAWinWithoutAFollowUpClearTheCarriedObject()
	{
		var declined = AcceptedFollowup( 42 );
		declined.Decline( 542 );
		Assert.AreEqual( 0, declined.ActualThing, "the decline clears +0x3e0 (0x004D2F38)" );
		var won = AcceptedFollowup( 42 );
		won.Accept( 541, 0 );
		var counters = new ParkCounters();
		counters.Add( ParkCounters.RideUsesOfThing( 42 ), 2 );
		var result = won.AdvanceDay( 542, definition => definition.Type == 20 ? 2 : 0 ).ToList();
		Assert.AreEqual( ParkEventKind.ChallengeCompleted, result[0].Kind );
		Assert.AreEqual( 0, won.ActualThing, "a win without a follow-up type clears it (0x004D30D9)" );
	}

	[TestMethod]
	public void TheCarriedObjectRefusesTheObjectlessEventsOfAnAcceptedChallenge()
	{
		Assert.IsTrue( new[] { 1, 2, 3, 4, 5, 6, 7, 8, 12, 13, 24, 25, 27 }.All( ParkObjectives.TakesObjectlessEvents ), "sales, profits and the daily messages push shorts of 0" );
		Assert.IsFalse( new[] { 17, 20, 22, 9, 10, 14, 18, 19, 21 }.Any( ParkObjectives.TakesObjectlessEvents ) );
	}

	[TestMethod]
	public void TheCarriedObjectSurvivesASaveAndOlderSavesRestoreNone()
	{
		var park = EconomyTestData.Park();
		var ride = park.RegisterExisting( 1180 );
		park.Objectives.Restore( 10, 0, null, new System.Collections.Generic.Dictionary<int, int>(), System.Array.Empty<int>(), System.Array.Empty<GoldenTicketKind>(), true, ride.Id,
			new System.Collections.Generic.Dictionary<int, int> { [8] = ride.Id } );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Capture( park ), park.Settings, park.Catalog );
		Assert.AreEqual( (ride.Id, ride.Id), (restored.Objectives.ActualThing, restored.Objectives.CarriedThings[8]) );
		var text = ParkSaveFile.Serialize( park );
		StringAssert.Contains( text, "\"ActualThing\"" );
		park.Objectives.Restore( 10, 0, null, new System.Collections.Generic.Dictionary<int, int>(), System.Array.Empty<int>(), System.Array.Empty<GoldenTicketKind>() );
		StringAssert.DoesNotMatch( ParkSaveFile.Serialize( park ), new System.Text.RegularExpressions.Regex( "ActualThing|CarriedThings" ) );
	}

	[TestMethod]
	public void ARideUseRaisesTheObjectsOwnCountAndTheEconomyNamesTheBestKart()
	{
		var park = EconomyTestData.Park();
		var ride = park.RegisterExisting( 1180 );
		park.RecordRideUse( ride.Id );
		park.RecordRideUse( ride.Id );
		Assert.AreEqual( (2L, 2L), (park.Counters[ParkCounters.RideUses( 1180 )], park.Counters[ParkCounters.RideUsesOfThing( ride.Id )]) );
		var first = park.RegisterExisting( 1106 );
		var second = park.RegisterExisting( 1106 );
		park.ReportRecord( ParkRecordKind.KartCrossroads, first.Id, 4 );
		park.ReportRecord( ParkRecordKind.KartCrossroads, second.Id, 4 );
		park.ReportRecord( ParkRecordKind.KartCrossroads, second.Id, 3 );
		var kart = park.Settings.Challenges[7] with { Type = 15, TargetObject = 1106 };
		var best = typeof( ParkEconomy ).GetMethod( "BestObjectOf", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!;
		Assert.AreEqual( first.Id, best.Invoke( park, new object[] { kart } ), "the first of equal records keeps the title (strictly greater replaces, 0x004CC4B0)" );
	}

	[TestMethod]
	public void AnOriginalSavesCarriedObjectIsImportedAsTheObjectsEconomyInstance()
	{
		// the slots' +0x2e words (offset 43 of a 45-byte slot) and mActualThing (the last two bytes) are thing ids of the saved park: the Belly Bounce is thing 12
		var settings = OriginalSaveImportTests.Settings();
		var body = OriginalSaveImportTests.Challenges( settings );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( body.AsSpan( 8 + 1 * SaveChallengeSystem.SlotBytes + 43 ), 12 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( body.AsSpan( body.Length - 2 ), 12 );
		var save = OriginalSaveImportTests.Park( settings, bellyBounce: SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields { PositionX = 384, PositionY = 384, Price = 30, Repair = 80, LifeGauge = 90 } ), challenges: body );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var import = OriginalEconomyImport.Apply( economy, save );
		var ride = economy.Objects.Single( item => item.InfoId == 1100 );
		Assert.AreEqual( ride.Id, economy.Objectives.ActualThing );
		Assert.AreEqual( ride.Id, economy.Objectives.CarriedThings[settings.ChallengesInThisLevel[1]] );
		Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( "Challenges imported:", System.StringComparison.Ordinal ) && line.Contains( "carried object thing 12", System.StringComparison.Ordinal ) ) );
	}
}
