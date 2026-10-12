using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>Staff happiness, energy, rest and strikes (ECON-014, docs/reverse/ORACLE.md F22); the original's routines are executed by the oracle targets staff-*.</summary>
[TestClass]
public class StaffVitalsTests
{
	private const int StaffRoomId = 1411;

	private sealed class FixedGuests : IParkGuestStatistics
	{
		public FixedGuests( int people ) => PeopleInPark = people;
		public int PeopleInPark { get; }
		public float AverageHappiness => 50;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	private static StaffMember Member( int grade = 0, float energy = 100f, float happiness = 100f ) =>
		new() { Id = 1, Type = StaffType.Mechanic, NameIndex = 0, Grade = grade, Energy = energy, Happiness = happiness };

	/// <summary>A test park whose catalogue also has the Staff Room (the one jungle object with UsageInfo.ChillsYouOut, the original's rest area).</summary>
	private static ParkEconomy ParkWithStaffRoom( int initialCash = 5_000_000 )
	{
		var room = new EconomyObjectInfo( StaffRoomId, "Staff Room", ParkObjectKind.Feature, ResearchCategory.Feature, 0,
			new[] { new UpgradeLevelInfo( 0, 100, 0, 0, 0, new[] { 50, 30, 20, 10 } ) }, null, null, null, 0, 0, 0, 0, 0, 0, "features/staff.wad", ChillsYouOut: true );
		return new ParkEconomy( EconomyTestData.Settings( initialCash ), new EconomyObjectCatalog( EconomyTestData.Catalog().Objects.Append( room ) ), ParkGameMode.FullSimulation, 7 );
	}

	private static List<StaffMember> Mechanics( ParkEconomy park, int count, float energy, float happiness ) =>
		Enumerable.Range( 0, count ).Select( _ => park.Staff.AddImported( StaffType.Mechanic, 0, 0, happiness, StaffState.Patrolling, 0, energy ) ).ToList();

	[TestMethod]
	public void StaffStartWithFullEnergyAndHappiness()
	{
		var park = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( park, StaffType.Mechanic );
		Assert.AreEqual( 100f, member.Happiness );
		Assert.AreEqual( 100f, member.Energy );
	}

	[TestMethod]
	public void SettingsCarryTheRecuperationRatesRestLevelAndTheRestAreaHit()
	{
		var settings = EconomyTestData.Settings();
		CollectionAssert.AreEqual( new[] { 0.2f, 0.3f, 0.4f, 0.5f, 0.75f }, settings.RecuperationRate.ToArray() );
		CollectionAssert.AreEqual( new[] { 1f, 2f, 2f, 3f, 3f }, settings.HappinessRecuperationRate.ToArray() );
		Assert.AreEqual( (1, 2), (settings.RestLevel, settings.HappyHitCosNoRestArea) );
	}

	[TestMethod]
	public void MovingWorkingAndRestingStepsFollowTheOriginalFormulas()
	{
		// A grade 0 member (factor 6) walking: 0.012 x 6 energy and 0.005 x 6 happiness; working: 0.025 x 6 and 0.01 x 6.
		var walker = Member();
		StaffVitals.Walk( walker );
		Assert.AreEqual( 99.928f, walker.Energy, 1e-4f );
		Assert.AreEqual( 99.97f, walker.Happiness, 1e-4f );
		var worker = Member();
		StaffVitals.Work( worker );
		Assert.AreEqual( 99.85f, worker.Energy, 1e-4f );
		Assert.AreEqual( 99.94f, worker.Happiness, 1e-4f );
		// A grade 4 member (factor 2) loses a third of that.
		var expert = Member( grade: 4 );
		StaffVitals.Walk( expert );
		Assert.AreEqual( 99.976f, expert.Energy, 1e-4f );
		// Resting adds the rates, clamps at 100 and reports the end of the rest once the truncated energy byte is 100.
		var rester = Member( energy: 99.9f, happiness: 50f );
		Assert.IsTrue( StaffVitals.Rest( rester, 0.2f, 1f ) );
		Assert.AreEqual( (100f, 51f), (rester.Energy, rester.Happiness) );
		var tired = Member( energy: 10f, happiness: 99.5f );
		Assert.IsFalse( StaffVitals.Rest( tired, 0.2f, 1f ) );
		Assert.AreEqual( (10.2f, 100f), (tired.Energy, tired.Happiness) );
		// Every vital stays inside 0..100.
		var empty = Member( grade: 0, energy: 0.01f, happiness: 0.001f );
		StaffVitals.Work( empty );
		Assert.AreEqual( (0f, 0f), (empty.Energy, empty.Happiness) );
		var grown = Member( grade: 7 );
		StaffVitals.Walk( grown );
		Assert.AreEqual( (100f, 100f), (grown.Energy, grown.Happiness) );
	}

	[TestMethod]
	public void TheEnergyByteDecidesRestAndTheRestLevelTestsAreTheOriginalsOwn()
	{
		Assert.AreEqual( 255, StaffVitals.Byte( -1f ) );
		Assert.AreEqual( 0, StaffVitals.Byte( 256f ) );
		Assert.AreEqual( 0, StaffVitals.Byte( float.NaN ) );
		Assert.IsTrue( StaffVitals.IsBelowRestLevel( 0.9f, 1 ) );
		Assert.IsFalse( StaffVitals.IsBelowRestLevel( 1f, 1 ) );
		// 0x100F4900 seeks rest at the level itself: energy 1.5 truncates to 1.
		var tired = Member( energy: 1.5f, happiness: 50f );
		Assert.IsTrue( StaffVitals.SeekRest( tired, 1, 2, restAreaFound: true, () => 0 ) );
		Assert.IsFalse( StaffVitals.SeekRest( Member( energy: 2f ), 1, 2, restAreaFound: true, () => 0 ) );
		// Without an area, happiness falls only when the draw's low four bits are 0, and clamps at 0.
		Assert.IsFalse( StaffVitals.SeekRest( tired, 1, 2, restAreaFound: false, () => 16 ) );
		Assert.AreEqual( 48f, tired.Happiness );
		Assert.IsFalse( StaffVitals.SeekRest( tired, 1, 2, restAreaFound: false, () => 17 ) );
		Assert.AreEqual( 48f, tired.Happiness );
		var gloomy = Member( energy: 0f, happiness: 1f );
		StaffVitals.SeekRest( gloomy, 1, 2, restAreaFound: false, () => 0 );
		Assert.AreEqual( 0f, gloomy.Happiness );
	}

	[TestMethod]
	public void AWalkingMemberLosesEnergyEveryTurnAndAWorkingOneMore()
	{
		var park = EconomyTestData.Park();
		var walker = park.Staff.AddImported( StaffType.Handyman, 2, 0, 100f, StaffState.Patrolling, 0, 100f );
		var worker = park.Staff.AddImported( StaffType.Mechanic, 2, 0, 100f, StaffState.Working, 0, 100f );
		worker.BusyTicks = 100_000;
		EconomyTestData.AdvanceToTurn( park, 100 );
		// grade 2: factor 4: walking 0.048 and 0.02 per turn, working 0.1 and 0.04 per turn
		Assert.AreEqual( 100f - 100 * 0.048f, walker.Energy, 0.01f );
		Assert.AreEqual( 100f - 100 * 0.02f, walker.Happiness, 0.01f );
		Assert.AreEqual( 100f - 100 * 0.1f, worker.Energy, 0.01f );
		Assert.AreEqual( 100f - 100 * 0.04f, worker.Happiness, 0.01f );
	}

	[TestMethod]
	public void APromotionByTrainingRestoresHappinessButNotEnergy()
	{
		var park = EconomyTestData.Park();
		var member = park.Staff.AddImported( StaffType.Handyman, 0, 0, 40f, StaffState.Idle, 0, 30f );
		park.Staff.SetTrainingBudget( StaffType.Handyman, 100_000 );
		var before = member.Grade;
		park.AdvanceMonths( 1 );
		Assert.IsTrue( member.Grade > before, "the budget bought a grade" );
		Assert.AreEqual( 100f, member.Happiness );
		Assert.AreEqual( 30f, member.Energy, "idle: untouched by training" );
	}

	[TestMethod]
	public void AnIdleMemberDoesNotDrain()
	{
		var park = EconomyTestData.Park();
		var idle = park.Staff.AddImported( StaffType.Handyman, 0, 0, 100f, StaffState.Idle, 0, 100f );
		EconomyTestData.AdvanceToTurn( park, 50 );
		Assert.AreEqual( (100f, 100f), (idle.Energy, idle.Happiness) );
	}

	[TestMethod]
	public void ATiredMemberRestsAtAStaffRoomUntilItsEnergyIsBack()
	{
		var park = ParkWithStaffRoom();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( StaffRoomId, out _ ) );
		var tired = park.Staff.AddImported( StaffType.Mechanic, 0, 0, 60f, StaffState.Patrolling, 0, 0.5f );
		EconomyTestData.AdvanceToTurn( park, 1 );
		Assert.AreEqual( StaffState.Resting, tired.State );
		Assert.IsFalse( tired.IsAvailable, "a resting mechanic is not dispatched" );
		EconomyTestData.AdvanceToTurn( park, 100 );
		Assert.AreEqual( StaffState.Resting, tired.State );
		Assert.AreEqual( 0.43f + 99 * 0.2f, tired.Energy, 0.1f );
		// 99.6 energy short of full at 0.2 a turn: the rest ends about turn 499, and the member is walking again after that
		EconomyTestData.AdvanceToTurn( park, 520 );
		Assert.AreEqual( StaffState.Patrolling, tired.State );
		Assert.IsTrue( tired.Energy > 95f, $"back at work with nearly full energy, {tired.Energy}" );
		Assert.IsTrue( tired.IsAvailable );
		Assert.IsTrue( tired.Happiness > 99f, "happiness recovered at one a turn" );
	}

	[TestMethod]
	public void ATiredMemberWithoutAStaffRoomKeepsWorkingAndLosesHappiness()
	{
		var park = ParkWithStaffRoom();
		var tired = park.Staff.AddImported( StaffType.Mechanic, 0, 0, 100f, StaffState.Patrolling, 0, 0f );
		EconomyTestData.AdvanceToTurn( park, 2000 );
		Assert.AreEqual( StaffState.Patrolling, tired.State );
		Assert.AreEqual( 0f, tired.Energy );
		// one turn in sixteen costs 2: about 250 in 2000 turns, which empties the 100
		Assert.IsTrue( tired.Happiness < 30f, $"happiness {tired.Happiness}" );
	}

	[TestMethod]
	public void RestingResearchersDoNotResearchAndOnlyAStaffRoomChillsYouOut()
	{
		Assert.IsFalse( ParkResearch.CanResearch( new StaffMember { Id = 1, Type = StaffType.Researcher, NameIndex = 0, Grade = 0, State = StaffState.Resting } ) );
		Assert.IsTrue( ParkResearch.CanResearch( new StaffMember { Id = 1, Type = StaffType.Researcher, NameIndex = 0, Grade = 0, State = StaffState.Patrolling } ) );
		Assert.AreEqual( 0, EconomyTestData.Catalog().Objects.Count( item => item.ChillsYouOut ), "no object of the plain test catalogue is a rest area" );
	}

	private static List<(long Month, ParkEvent Event)> RunMonths( ParkEconomy park, int months )
	{
		var events = new List<(long, ParkEvent)>();
		var month = 0L;
		park.EventRaised += item =>
		{
			if ( item.Kind == ParkEventKind.StaffStrikeMessage )
				events.Add( (month, item) );
		};
		for ( ; month < months; month++ )
			park.AdvanceMonths( 1 );
		return events;
	}

	[TestMethod]
	public void FourTiredMechanicsInAnOldParkWarnThenStrikeAndAreBackAtTheNextMonthEnd()
	{
		var park = EconomyTestData.Park( initialCash: 5_000_000 );
		park.OpenPark();
		var mechanics = Mechanics( park, 4, energy: 0f, happiness: 100f );
		var messages = RunMonths( park, 31 );
		// A park younger than 24 thirty-day months is left alone; the first month end after that is the warning, the next the strike.
		Assert.IsTrue( messages.All( item => item.Event.InfoId == (int)StaffType.Mechanic ), "only the mechanics are unhappy" );
		var kinds = messages.Select( item => (StrikeMessageKind)item.Event.Amount ).ToArray();
		CollectionAssert.AreEqual( new[] { StrikeMessageKind.Warning, StrikeMessageKind.Strike, StrikeMessageKind.Strike, StrikeMessageKind.Strike }, kinds[..4],
			"warning, then a strike every second month end (the one in between only ends the strike)" );
		var warnedAt = messages[0].Month;
		Assert.IsTrue( warnedAt >= 23, $"warned at month end {warnedAt}: the park is 24 thirty-day months old at its 24th month end" );
		CollectionAssert.AreEqual( new[] { warnedAt + 1, warnedAt + 3, warnedAt + 5 }, messages.Skip( 1 ).Take( 3 ).Select( item => item.Month ).ToArray() );
		Assert.AreEqual( 4, park.Staff.Strikes.Level( StaffType.Mechanic ) );
		Assert.AreEqual( "TAG_SYSTEM 48", messages[1].Event.Detail, "\"Your mechanics have walked off the job... they're on strike!\"" );
		Assert.AreEqual( "TAG_SYSTEM 38", messages[0].Event.Detail );
	}

	[TestMethod]
	public void MechanicsOnStrikeStopWorkingUntilTheStrikeIsEnded()
	{
		var park = EconomyTestData.Park( initialCash: 5_000_000 );
		park.OpenPark();
		var mechanics = Mechanics( park, 4, energy: 0f, happiness: 100f );
		var seen = new List<StrikeMessageKind>();
		park.EventRaised += item =>
		{
			if ( item.Kind == ParkEventKind.StaffStrikeMessage )
				seen.Add( (StrikeMessageKind)item.Amount );
		};
		while ( !seen.Contains( StrikeMessageKind.Strike ) )
			park.AdvanceMonths( 1 );
		Assert.IsTrue( park.Staff.Strikes.IsActive( StaffType.Mechanic ) );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 2 );
		Assert.IsTrue( mechanics.All( member => member.State == StaffState.OnStrike ), "free mechanics walk out" );
		Assert.IsTrue( mechanics.All( member => !member.IsAvailable ) );
		// A broken ride gets no mechanic while they strike.
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1100, out var ride ) );
		ride!.IsBrokenDown = true;
		EconomyTestData.AdvanceToTurn( park, park.Turn + 100 );
		Assert.AreEqual( 0, ride.MechanicId );
		// The next month end ends the strike, and the mechanics are back at work on the next turn.
		park.AdvanceMonths( 1 );
		Assert.IsFalse( park.Staff.Strikes.IsActive( StaffType.Mechanic ) );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 2 );
		Assert.IsTrue( mechanics.All( member => member.State != StaffState.OnStrike ) );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 24 * 24 );
		Assert.IsFalse( ride.IsBrokenDown, "a mechanic repaired the ride once the strike was over" );
	}

	private static List<ParkObjectState> BrokenRides( ParkEconomy park, int count )
	{
		var rides = new List<ParkObjectState>();
		for ( var index = 0; index < count; index++ )
		{
			Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1100, out var ride ) );
			ride!.IsBrokenDown = true;
			// A worn-out ride: the repair takes the whole WorkDuration in park turns (WorkDuration x (100 - 0) / 100).
			ride.Repair = 0;
			rides.Add( ride );
		}
		return rides;
	}

	[TestMethod]
	public void AMechanicFinishesItsJobDuringAStrikeButTakesNoneOfTheBacklog()
	{
		var park = EconomyTestData.Park( initialCash: 5_000_000 );
		park.OpenPark();
		var mechanic = park.Staff.AddImported( StaffType.Mechanic, 0, 0, 100f, StaffState.Patrolling, 0, 50f );
		var rides = BrokenRides( park, 3 );
		EconomyTestData.AdvanceToTurn( park, 5 );
		var first = rides.Single( ride => ride.MechanicId == mechanic.Id );
		Assert.AreEqual( StaffState.Working, mechanic.State );
		// the mechanics' strike begins while the job is on
		park.Staff.Strikes.Restore( new[] { 0, 2, 0, 0, 0 }, new[] { false, true, false, false, false }, new uint[5] );
		EconomyTestData.AdvanceToTurn( park, 250 );
		Assert.IsFalse( first.IsBrokenDown, "the current job is finished" );
		Assert.AreEqual( StaffState.OnStrike, mechanic.State );
		Assert.IsTrue( rides.Where( ride => ride != first ).All( ride => ride.IsBrokenDown && ride.MechanicId == 0 ), "the backlog waits" );
	}

	[TestMethod]
	public void ATiredMechanicWithAStaffRoomRestsAfterItsJobInsteadOfTakingTheNext()
	{
		var park = ParkWithStaffRoom();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( StaffRoomId, out _ ) );
		var mechanic = park.Staff.AddImported( StaffType.Mechanic, 0, 0, 100f, StaffState.Working, 0, 0f );
		var rides = BrokenRides( park, 2 );
		mechanic.BusyTicks = 2; // park turns: the job ends on the second staff update
		mechanic.AssignedInstanceId = rides[0].Id;
		rides[0].MechanicId = mechanic.Id;
		EconomyTestData.AdvanceToTurn( park, 4 );
		Assert.IsFalse( rides[0].IsBrokenDown, "the job was finished" );
		Assert.AreEqual( StaffState.Resting, mechanic.State );
		Assert.AreEqual( 0, rides[1].MechanicId, "the next repair waits for a rested mechanic" );
		EconomyTestData.AdvanceToTurn( park, 40 );
		Assert.AreEqual( 0, rides[1].MechanicId );
	}

	[TestMethod]
	public void SellingTheOnlyStaffRoomEndsTheRest()
	{
		var park = ParkWithStaffRoom();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( StaffRoomId, out var room ) );
		var tired = park.Staff.AddImported( StaffType.Mechanic, 0, 0, 60f, StaffState.Patrolling, 0, 0.5f );
		EconomyTestData.AdvanceToTurn( park, 5 );
		Assert.AreEqual( StaffState.Resting, tired.State );
		park.Sell( room!.Id );
		EconomyTestData.AdvanceToTurn( park, 6 );
		Assert.AreEqual( StaffState.Patrolling, tired.State );
		Assert.IsTrue( tired.IsAvailable );
	}

	[TestMethod]
	public void StrikeMessagesReadTheirTagSystemText()
	{
		var table = Enumerable.Range( 0, 70 ).Select( index => $"text {index}" ).ToArray();
		var strike = new ParkEvent( 0, ParkEventKind.StaffStrikeMessage, 0, (int)StaffType.Mechanic, (int)StrikeMessageKind.Strike, "TAG_SYSTEM 48" );
		Assert.AreEqual( "text 48", OpenTPW.Hud.ParkHud.StrikeMessageText( strike, table ) );
		Assert.IsNull( OpenTPW.Hud.ParkHud.StrikeMessageText( strike, table.Take( 10 ).ToArray() ) );
		Assert.IsNull( OpenTPW.Hud.ParkHud.StrikeMessageText( strike with { Kind = ParkEventKind.DayEnded }, table ) );
	}

	[TestMethod]
	public void GuestsInTheParkOrAClosedParkSkipTheMonth()
	{
		foreach ( var scenario in new[] { "guests", "closed" } )
		{
			var park = EconomyTestData.Park( initialCash: 5_000_000 );
			if ( scenario == "guests" )
			{
				park.OpenPark();
				park.GuestStatistics = new FixedGuests( 3 );
			}
			Mechanics( park, 4, energy: 0f, happiness: 0f );
			Assert.AreEqual( 0, RunMonths( park, 30 ).Count, scenario );
			Assert.AreEqual( 0, park.Staff.Strikes.Level( StaffType.Mechanic ), scenario );
		}
	}

	[TestMethod]
	public void ContentStaffAndSmallCrewsNeverHaveAGrievance()
	{
		var park = EconomyTestData.Park( initialCash: 5_000_000 );
		park.OpenPark();
		Mechanics( park, 3, energy: 0f, happiness: 0f );
		Assert.AreEqual( 0, RunMonths( park, 30 ).Count, "three mechanics are never more than three" );
		Assert.IsFalse( StaffStrikeBoard.Grievance( StaffType.Guard, Enumerable.Repeat( new StaffVital( 100f, 100f ), 6 ).ToList() ) );
		Assert.IsTrue( StaffStrikeBoard.Grievance( StaffType.Guard, Enumerable.Repeat( new StaffVital( 100f, 14.9f ), 4 ).ToList() ) );
		Assert.IsFalse( StaffStrikeBoard.Grievance( StaffType.Guard, Enumerable.Repeat( new StaffVital( 15f, 100f ), 4 ).ToList() ), "an average of exactly 15 is fine" );
		Assert.IsTrue( StaffStrikeBoard.Grievance( StaffType.Guard, new[] { new StaffVital( 15f, 100f ), new StaffVital( 15f, 100f ), new StaffVital( 15f, 100f ), new StaffVital( 14f, 100f ) } ),
			"the sum 59 over 4 members is 14 (floor)" );
		Assert.IsTrue( StaffStrikeBoard.Grievance( StaffType.Guard, Enumerable.Repeat( new StaffVital( 100f, 100f ), 5 ).ToList(), forced: true ) );
	}

	[TestMethod]
	public void StrikeMessagesPointAtTheirTagSystemTexts()
	{
		Assert.AreEqual( 38, new StrikeMessage( StaffType.Mechanic, StrikeMessageKind.Warning ).TagSystemIndex );
		Assert.AreEqual( 39, new StrikeMessage( StaffType.Handyman, StrikeMessageKind.Warning ).TagSystemIndex );
		Assert.AreEqual( 40, new StrikeMessage( StaffType.Guard, StrikeMessageKind.Warning ).TagSystemIndex );
		Assert.AreEqual( 41, new StrikeMessage( StaffType.Entertainer, StrikeMessageKind.Warning ).TagSystemIndex );
		Assert.AreEqual( 42, new StrikeMessage( StaffType.Researcher, StrikeMessageKind.Warning ).TagSystemIndex );
		Assert.AreEqual( 49, new StrikeMessage( StaffType.Handyman, StrikeMessageKind.Strike ).TagSystemIndex );
		Assert.AreEqual( 57, new StrikeMessage( StaffType.Researcher, StrikeMessageKind.CalledOff ).TagSystemIndex );
		Assert.AreEqual( 62, new StrikeMessage( StaffType.Researcher, StrikeMessageKind.Over ).TagSystemIndex );
	}

	[TestMethod]
	public void VitalsAndStrikeLevelsSurviveASave()
	{
		var park = EconomyTestData.Park( initialCash: 5_000_000 );
		park.OpenPark();
		var mechanics = Mechanics( park, 4, energy: 0f, happiness: 12.5f );
		mechanics[1].Energy = 33.25f;
		RunMonths( park, 26 );
		Assert.AreNotEqual( 0, park.Staff.Strikes.Level( StaffType.Mechanic ) );
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		CollectionAssert.AreEqual( park.Staff.Members.Select( member => (member.Energy, member.Happiness, member.State) ).ToArray(),
			restored.Staff.Members.Select( member => (member.Energy, member.Happiness, member.State) ).ToArray() );
		foreach ( var role in System.Enum.GetValues<StaffType>() )
			Assert.AreEqual( (park.Staff.Strikes.Level( role ), park.Staff.Strikes.IsActive( role ), park.Staff.Strikes.LastConsidered( role )),
				(restored.Staff.Strikes.Level( role ), restored.Staff.Strikes.IsActive( role ), restored.Staff.Strikes.LastConsidered( role )) );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
	}

	[TestMethod]
	public void ASaveWithoutEnergyOrStrikesStillLoadsAsRestedStaff()
	{
		var park = EconomyTestData.Park();
		Mechanics( park, 1, energy: 100f, happiness: 100f );
		var node = System.Text.Json.Nodes.JsonNode.Parse( ParkSaveFile.Serialize( park ) )!.AsObject();
		var members = node["Staff"]!["Members"]!.AsArray();
		members[0]!.AsObject().Remove( "Energy" );
		node["Staff"]!.AsObject().Remove( "Strikes" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), park.Settings, park.Catalog );
		Assert.AreEqual( (100f, 100f), (restored.Staff.Members[0].Energy, restored.Staff.Members[0].Happiness) );
		Assert.AreEqual( 0, restored.Staff.Strikes.Level( StaffType.Mechanic ) );
	}
}
