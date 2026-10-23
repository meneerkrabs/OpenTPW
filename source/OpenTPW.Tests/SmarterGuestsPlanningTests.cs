using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Phase 5 of the smarter-guests plan (docs/SMARTER-GUESTS.md): planning needs. A service whose need will cross its level before the guest is done with the ride it would take is scored as if the need had grown
/// (<see cref="SmarterGuestsRules.Planned"/>), a service on the guest's way gets one detour step, and shortly after a meal a ride that would make the guest sick scores lower. The measured effect is in the page's tables; these tests
/// pin each rule's pure decision, the bookkeeping of the meal time (kept, saved, restored, validated) and what the host fills in for the policy.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SmarterGuestsPlanningTests
{
	private static readonly GuestSettings Settings = GuestTests.CreateSettings();
	private static readonly SmarterGuestDecisions Smarter = new( SmarterRuleSet.Phase5 );
	private static readonly SmarterGuestDecisions Phase4 = new( SmarterRuleSet.None );

	/// <summary>A guest of thing id 8 (a multiple of 4: the only kind whose meters ever grow) and thing id 7 (whose meters never do).</summary>
	private const int Grower = 8, Steady = 7;

	private static GuestView Guest( int id = Grower, float thirst = 10, float hunger = 10, float toilet = 10, float illness = 0, long meal = -1, long plan = -1 ) =>
		new( id, 11, 0, 60, thirst, hunger, toilet, illness, 500, 3, 3, Array.AsReadOnly( new int[4] ), Array.AsReadOnly( new int[4] ), meal, plan );

	private static AttractionView Shop( int thirstEffect = 0, int hungerEffect = 0, bool relief = false, bool onTheWay = false ) =>
		new( 5, 1, RideVisitorKind.Shop, true, (3, 4), (3, 5), 0, 0, 4, 20, 0, 0, thirstEffect, hungerEffect, relief, 30, false, 0, 0, true, false, false, 0, 0, 0, 12 * 256, 12 * 256, onTheWay );

	private static AttractionView Ride( int excitement = 70, int id = 6 ) =>
		new( id, 2, RideVisitorKind.Ride, true, (3, 4), (3, 5), 0, 0, 4, 20, excitement, 1, 0, 0, false, 30, false, 0, 0, true, false, false, 0, 0, 0, 30 * 256, 30 * 256 );

	private static DecisionContext Context( long turn = 800 ) => new( Settings, turn, false, 3 );

	// ---- The needs block's growth, projected ------------------------------------------------------

	[TestMethod]
	public void OnlyAGuestWhoseIdIsAMultipleOfFourGrowsAndTheStepsAreTheMultiplesOfSixteen()
	{
		Assert.AreEqual( 0, NeedsUpdate.GrowthSteps( Steady, 0, 10_000 ), "an id that is not a multiple of 4 never grows" );
		Assert.AreEqual( 0, NeedsUpdate.GrowthSteps( 6, 0, 10_000 ) );
		Assert.AreEqual( 1, NeedsUpdate.GrowthSteps( Grower, 0, 16 ), "turn 16 is the first multiple" );
		Assert.AreEqual( 0, NeedsUpdate.GrowthSteps( Grower, 0, 15 ) );
		Assert.AreEqual( 0, NeedsUpdate.GrowthSteps( Grower, 16, 15 ), "the turn already past does not count again" );
		Assert.AreEqual( 1, NeedsUpdate.GrowthSteps( Grower, 16, 16 ) );
		Assert.AreEqual( 11, NeedsUpdate.GrowthSteps( Grower, 800, 180 ), "the planning's longest look ahead" );
		Assert.AreEqual( 0, NeedsUpdate.GrowthSteps( Grower, 800, 0 ) );
		Assert.AreEqual( 0, NeedsUpdate.GrowthSteps( Grower, 800, -5 ) );
		Assert.AreEqual( 3, NeedsUpdate.GrowthSteps( Grower, -20, 40 ), "turns -16, 0 and 16 are multiples of 16 (floor division before the park starts)" );
		Assert.AreEqual( 1, NeedsUpdate.GrowthSteps( Grower, -1, 2 ) );
	}

	[TestMethod]
	public void TheProjectionIsWhatTheNeedsBlockDoesTurnByTurn()
	{
		// the projection is checked against the original's own update (NeedsUpdate.Apply), not against its description
		foreach ( var id in new[] { 7, 8, 9, 12, 100 } )
		{
			foreach (var (from, turns) in new[] { (0L, 40L), (13L, 100L), (800L, 180L), (1000L, 16L), (1015L, 2L) })
			{
				var meters = new GuestMeters( 50, 31, 42, 23, 5 );
				var current = meters;
				for ( var turn = from + 1; turn <= from + turns; turn++ )
					current = NeedsUpdate.Apply( current, 100, (uint)turn, id ).Meters;
				var view = Guest( id, thirst: meters.Thirst, hunger: meters.Hunger, toilet: meters.Toilet );
				var (thirst, hunger, toilet) = SmarterGuestsRules.ProjectedMeters( view, from, turns );
				Assert.AreEqual( (current.Thirst, current.Hunger, current.Toilet), (thirst, hunger, toilet), $"id {id}, turns {from}+{turns}" );
			}
		}
	}

	[TestMethod]
	public void TheLevelsAreTheOnesTheOriginalActsOn()
	{
		Assert.AreEqual( 80, SmarterGuestsRules.ToiletSeekLevel, "a queueing guest leaves for a ride above this toilet byte" );
		Assert.AreEqual( 100, SmarterGuestsRules.FullNeedLevel, "the byte at which the needs block charges happiness and the thoughts start" );
		Assert.IsTrue( SmarterGuestsRules.CrossesFullNeed( 99.9f, 100 ) );
		Assert.IsFalse( SmarterGuestsRules.CrossesFullNeed( 99.9f, 99.99f ), "a byte of 99 is not 100" );
		Assert.IsFalse( SmarterGuestsRules.CrossesFullNeed( 100, 100 ), "already there: the original's own score has it" );
		Assert.IsTrue( SmarterGuestsRules.CrossesToiletLevel( 80.9f, 81 ) );
		Assert.IsFalse( SmarterGuestsRules.CrossesToiletLevel( 81, 90 ), "already above" );
		Assert.IsFalse( SmarterGuestsRules.CrossesToiletLevel( 70, 80.9f ), "a byte of 80 is not above 80" );
		Assert.AreEqual( 180, SmarterGuestsRules.PlanningCapTurns, "the give-up patience plus a default ride cycle" );
		Assert.AreEqual( 0, SmarterGuestsRules.PlanningTurns( -1 ) );
		Assert.AreEqual( 1, SmarterGuestsRules.PlanningTurns( 1 ), "rounded up to whole turns" );
		Assert.AreEqual( 60, SmarterGuestsRules.PlanningTurns( 60 * 256 ) );
		Assert.AreEqual( 180, SmarterGuestsRules.PlanningTurns( 100_000 * 256 ), "capped" );
	}

	[TestMethod]
	public void AGuestWithNoMeterNearALevelIsNeverScannedFor()
	{
		Assert.IsFalse( SmarterGuestsRules.MayCross( Guest( Steady, hunger: 99 ), 800 ), "an id that never grows has no meter that will cross" );
		Assert.IsFalse( SmarterGuestsRules.MayCross( Guest( Grower, hunger: 60, thirst: 60, toilet: 30 ), 800 ) );
		Assert.IsTrue( SmarterGuestsRules.MayCross( Guest( Grower, hunger: 90 ), 800 ), "11 steps of 2 from 90 pass 100" );
		Assert.IsTrue( SmarterGuestsRules.MayCross( Guest( Grower, thirst: 90 ), 800 ) );
		Assert.IsTrue( SmarterGuestsRules.MayCross( Guest( Grower, toilet: 72 ), 800 ), "11 steps of 1 from 72 pass 80" );
		Assert.IsFalse( SmarterGuestsRules.MayCross( Guest( Grower, toilet: 60 ), 800 ) );
		Assert.IsFalse( SmarterGuestsRules.MayCross( Guest( Grower, hunger: 100 ), 800 ), "already full" );
	}

	// ---- Rule 1: plan -------------------------------------------------------------------------------

	[TestMethod]
	public void ANeedThatWillCrossItsLevelIsReadAsGrownForTheServiceThatAnswersItAndNothingElse()
	{
		var guest = Guest( hunger: 94, thirst: 94, toilet: 77, plan: 60 * 256 );
		var food = Shop( hungerEffect: 25 );
		var seen = SmarterGuestsRules.Planned( guest, food, 800, planning: true, detour: false );
		Assert.IsTrue( seen.Hunger > guest.Hunger, "60 turns are 3 or 4 steps of 2: 94 passes 100 only with 3 steps and a bit; the projection is what the needs block gives" );
		Assert.AreEqual( SmarterGuestsRules.ProjectedMeters( guest, 800, 60 ).Hunger, seen.Hunger );
		Assert.AreEqual( guest.Thirst, seen.Thirst, "a meter the shop does not serve is the guest's own" );
		Assert.AreEqual( guest.Toilet, seen.Toilet );
		var toilet = SmarterGuestsRules.Planned( guest with { Toilet = 76, PlanTurns256 = 100 * 256 }, Shop( relief: true ), 800, true, false );
		Assert.IsTrue( toilet.Toilet > 76, "a relief object reads the projected toilet" );
		Assert.AreEqual( guest.Hunger, SmarterGuestsRules.Planned( guest, Ride(), 800, true, true ).Hunger, "a ride is scored on the guest as it is" );
		Assert.AreEqual( guest, SmarterGuestsRules.Planned( guest, Ride(), 800, true, true ) );
		var unplanned = guest with { PlanTurns256 = -1 };
		Assert.AreEqual( unplanned, SmarterGuestsRules.Planned( unplanned, food, 800, true, false ), "no plan: the guest as it is" );
		Assert.AreEqual( guest, SmarterGuestsRules.Planned( guest, food, 800, planning: false, detour: false ), "the rule switched off" );
	}

	[TestMethod]
	public void ANeedThatDoesNotCrossAnythingIsLeftAlone()
	{
		var farFromFull = Guest( hunger: 40, plan: 120 * 256 );
		Assert.AreEqual( farFromFull, SmarterGuestsRules.Planned( farFromFull, Shop( hungerEffect: 25 ), 800, true, false ), "40 plus 14 steps of 2 is still far from 100" );
		var steady = Guest( Steady, hunger: 99.5f, plan: 100 * 256 );
		Assert.AreEqual( steady, SmarterGuestsRules.Planned( steady, Shop( hungerEffect: 25 ), 800, true, false ), "a guest that never grows has nothing to cross" );
		var full = Guest( hunger: 100, plan: 100 * 256 );
		Assert.AreEqual( full, SmarterGuestsRules.Planned( full, Shop( hungerEffect: 25 ), 800, true, false ), "already full: the original's own score has the need" );
	}

	[TestMethod]
	public void APlannedServiceScoresAtLeastAsMuchAndTheScoreIsTheOriginalsOverTheProjectedGuest()
	{
		var guest = Guest( hunger: 92, plan: 80 * 256 );
		var food = Shop( hungerEffect: 25 );
		var planned = Smarter.ScoreAttraction( Context(), guest, food );
		var without = Smarter.ScoreAttraction( Context(), guest with { PlanTurns256 = -1 }, food );
		Assert.IsTrue( planned >= without, $"{planned} against {without}" );
		Assert.IsTrue( planned > without, "92 plus 5 steps of 2 reaches 100: the need counts as full, a higher match than 92" );
		var projected = SmarterGuestsRules.ProjectedMeters( guest, 800, 80 );
		Assert.AreEqual( Phase4.ScoreAttraction( Context(), guest with { Hunger = projected.Hunger, PlanTurns256 = -1 }, food ), planned, "the same score the guest would give the shop with the grown meter" );
		Assert.AreEqual( without, Phase4.ScoreAttraction( Context(), guest, food ), "with the rule off the score is the previous version's" );
		var onlyDetour = new SmarterGuestDecisions( SmarterRuleSet.Detour | SmarterRuleSet.MealTime );
		Assert.AreEqual( without, onlyDetour.ScoreAttraction( Context(), guest, food ), "planning switched off alone" );
		Assert.AreEqual( Phase4.ScoreAttraction( Context(), guest, Ride() ), Smarter.ScoreAttraction( Context(), guest, Ride() ), "a ride's score does not read the plan" );
	}

	// ---- Rule 2: detour -----------------------------------------------------------------------------

	[TestMethod]
	public void AServiceOnTheWayIsReadOneStepHigherAtMostToOneHundred()
	{
		var guest = Guest( thirst: 55, hunger: 95, toilet: 40 );
		var drink = Shop( thirstEffect: 40, onTheWay: true );
		var seen = SmarterGuestsRules.Planned( guest, drink, 800, planning: false, detour: true );
		Assert.AreEqual( 65f, seen.Thirst );
		Assert.AreEqual( guest.Hunger, seen.Hunger, "only the need the shop serves" );
		var food = SmarterGuestsRules.Planned( guest, Shop( hungerEffect: 25, onTheWay: true ), 800, false, true );
		Assert.AreEqual( 100f, food.Hunger, "a meter stays inside 0..100" );
		Assert.AreEqual( 45f, SmarterGuestsRules.Planned( guest, Shop( relief: true, onTheWay: true ), 800, false, true ).Toilet, "one step of the relief curve, which is indexed in fives" );
		Assert.AreEqual( guest, SmarterGuestsRules.Planned( guest, Shop( thirstEffect: 40 ), 800, false, true ), "not on the way: no step" );
		Assert.AreEqual( guest, SmarterGuestsRules.Planned( guest, drink, 800, planning: false, detour: false ), "the rule switched off" );
		Assert.AreEqual( guest, SmarterGuestsRules.Planned( guest, Ride() with { OnTheWay = true }, 800, true, true ), "a ride on the way is a ride" );
	}

	[TestMethod]
	public void TheDetourIsSmallItAddsAtMostAFewScorePointsToAnOrdinaryShopAndNeverLowersOne()
	{
		var most = 0L;
		foreach ( var effect in new[] { 5, 15, 25, 40, 100 } )
		{
			for ( var need = 0; need <= 100; need += 5 )
			{
				var guest = Guest( thirst: need, hunger: need, toilet: need );
				foreach ( var shop in new[] { Shop( thirstEffect: effect ), Shop( hungerEffect: effect ), Shop( relief: true ) } )
				{
					var plain = Smarter.ScoreAttraction( Context(), guest, shop );
					var nudged = Smarter.ScoreAttraction( Context(), guest, shop with { OnTheWay = true } );
					Assert.IsTrue( nudged >= plain, $"effect {effect}, need {need}: {nudged} against {plain}" );
					most = Math.Max( most, (long)nudged - plain );
				}
			}
		}
		Assert.IsTrue( most <= 5, $"the largest gain over the table is {most} points" );
		Assert.IsTrue( most >= 1, "and the rule does something where a need is high" );
		var none = Guest( thirst: 0, hunger: 0, toilet: 0 );
		Assert.AreEqual( Phase4.ScoreAttraction( Context(), none, Shop( thirstEffect: 40 ) ), Smarter.ScoreAttraction( Context(), none, Shop( thirstEffect: 40, onTheWay: true ) ), "a guest with no need is not tempted" );
	}

	// ---- Rule 3: meal time --------------------------------------------------------------------------

	[TestMethod]
	public void TheMealWindowIsTheTimeAMealTakesToGrowBack()
	{
		// the shipped Fries Shop takes 20 off hunger; the needs block puts 2 back every 16 turns
		Assert.AreEqual( 160, SmarterGuestsRules.MealWindowTurns );
		Assert.AreEqual( 20, 160 / 16 * (int)NeedsUpdate.ThirstHungerGrowth, "a window of 160 turns is 20 hunger points of growth" );
		Assert.IsTrue( SmarterGuestsRules.InMealWindow( 959, 800 ) );
		Assert.IsFalse( SmarterGuestsRules.InMealWindow( 960, 800 ), "160 turns after is out" );
		Assert.IsTrue( SmarterGuestsRules.InMealWindow( 800, 800 ), "the turn of the meal itself" );
		Assert.IsFalse( SmarterGuestsRules.InMealWindow( 800, -1 ), "no meal" );
		Assert.IsFalse( SmarterGuestsRules.InMealWindow( 799, 800 ), "a meal in the future is no meal" );
	}

	[TestMethod]
	public void TheRidesIllnessGainIsTheOriginalsFormulaOverTheGuestsOwnStomach()
	{
		foreach ( var hunger in new float[] { 0, 10, 19.9f, 20, 55, 81, 100 } )
		{
			foreach ( var excitement in new[] { 0, 20, 45, 70, 95, 100 } )
			{
				var (_, illness) = RideOutcome.Apply( 50, hunger, 7, 40, excitement, 20, 10, 5, Settings.RideVomitDivisor );
				var view = Guest( hunger: hunger );
				var expected = excitement == 0 ? 0 : illness - 7;
				Assert.AreEqual( expected, SmarterGuestsRules.RideIllnessGain( Context(), view, Ride( excitement ) ), $"hunger {hunger}, excitement {excitement}" );
			}
		}
		Assert.AreEqual( 36, SmarterGuestsRules.RideIllnessGain( Context(), Guest( hunger: 10 ), Ride( 95 ) ), "a thrill ride on a full stomach: 9 x 4" );
		Assert.AreEqual( 0, SmarterGuestsRules.RideIllnessGain( Context(), Guest( hunger: 85 ), Ride( 95 ) ), "a hungry guest is not made sick" );
	}

	[TestMethod]
	public void ARideIsTooMuchAfterAMealOnlyInTheWindowAndOnlyWhenItWouldAddAQuarterOfTheMeter()
	{
		var fed = Guest( hunger: 10, meal: 800 );
		Assert.IsTrue( SmarterGuestsRules.TooMuchAfterMeal( Context( 900 ), fed, Ride( 95 ) ), "36 is at least 25" );
		Assert.IsTrue( SmarterGuestsRules.TooMuchAfterMeal( Context( 900 ), fed, Ride( 70 ) ), "7 x 4 = 28" );
		Assert.IsFalse( SmarterGuestsRules.TooMuchAfterMeal( Context( 900 ), fed, Ride( 60 ) ), "6 x 4 = 24 is under the limit" );
		Assert.IsFalse( SmarterGuestsRules.TooMuchAfterMeal( Context( 960 ), fed, Ride( 95 ) ), "outside the window" );
		Assert.IsFalse( SmarterGuestsRules.TooMuchAfterMeal( Context( 900 ), fed with { MealTurn = -1 }, Ride( 95 ) ), "no meal on record" );
		Assert.IsFalse( SmarterGuestsRules.TooMuchAfterMeal( Context( 900 ), fed with { Hunger = 60 }, Ride( 95 ) ), "a stomach that is half empty again: 9 x 2 = 18" );
		Assert.IsFalse( SmarterGuestsRules.TooMuchAfterMeal( Context( 900 ), fed, Shop( hungerEffect: 25 ) ), "only a ride makes a guest sick" );
	}

	[TestMethod]
	public void LoweringAScoreBringsItHalfwayToTheChoiceFloorKeepsTheOrderAndNeverRaisesAnything()
	{
		Assert.AreEqual( 25u, SmarterGuestsRules.LowerAfterMeal( 40 ) );
		Assert.AreEqual( 15u, SmarterGuestsRules.LowerAfterMeal( 20 ) );
		Assert.AreEqual( 10u, SmarterGuestsRules.LowerAfterMeal( 10 ) );
		Assert.AreEqual( 0u, SmarterGuestsRules.LowerAfterMeal( 0 ) );
		Assert.AreEqual( 9u, SmarterGuestsRules.LowerAfterMeal( 9 ), "below the floor there was nothing to keep" );
		for ( uint score = 0; score < 400; score++ )
		{
			var lowered = SmarterGuestsRules.LowerAfterMeal( score );
			Assert.IsTrue( lowered <= score, $"{score} -> {lowered}" );
			if ( score >= AttractionScore.CandidateFloor )
				Assert.IsTrue( lowered >= AttractionScore.CandidateFloor, $"{score} -> {lowered}: a candidate stays a candidate" );
			if ( score > 0 )
				Assert.IsTrue( lowered >= SmarterGuestsRules.LowerAfterMeal( score - 1 ), "monotone" );
		}
		// two rides that scored 40 and 50 still rank in that order after the lowering (no tie at the floor)
		Assert.IsTrue( SmarterGuestsRules.LowerAfterMeal( 50 ) > SmarterGuestsRules.LowerAfterMeal( 40 ) );
	}

	[TestMethod]
	public void ARecentMealLowersTheThrillRideAndLeavesTheCalmOneAndTheShopsAlone()
	{
		var fed = Guest( hunger: 10, meal: 800 );
		var thrill = Ride( 95 );
		var calm = Ride( 20, 7 );
		var phase4 = Phase4.ScoreAttraction( Context( 900 ), fed, thrill );
		Assert.IsTrue( phase4 >= 20, $"the ride scores {phase4}" );
		Assert.AreEqual( SmarterGuestsRules.LowerAfterMeal( phase4 ), Smarter.ScoreAttraction( Context( 900 ), fed, thrill ) );
		Assert.IsTrue( Smarter.ScoreAttraction( Context( 900 ), fed, thrill ) < phase4 );
		Assert.AreEqual( Phase4.ScoreAttraction( Context( 900 ), fed, calm ), Smarter.ScoreAttraction( Context( 900 ), fed, calm ), "20 / 10 x 4 = 8 is no trouble" );
		Assert.AreEqual( phase4, Smarter.ScoreAttraction( Context( 1000 ), fed, thrill ), "after the window" );
		Assert.AreEqual( phase4, Smarter.ScoreAttraction( Context( 900 ), fed with { MealTurn = -1 }, thrill ), "no meal" );
		Assert.AreEqual( phase4, new SmarterGuestDecisions( SmarterRuleSet.Planning | SmarterRuleSet.Detour ).ScoreAttraction( Context( 900 ), fed, thrill ), "the rule switched off" );
		var food = Shop( hungerEffect: 25 );
		Assert.AreEqual( Phase4.ScoreAttraction( Context( 900 ), fed, food ), Smarter.ScoreAttraction( Context( 900 ), fed, food ) );
	}

	[TestMethod]
	public void WithoutAMealAPlanOrAStopOnTheWayThePoliciesAreTheSameAndTheRulesAreSwitchable()
	{
		Assert.IsTrue( Smarter.UsesPlanning );
		Assert.IsTrue( new SmarterGuestDecisions( SmarterRuleSet.Detour ).UsesPlanning );
		Assert.IsFalse( Phase4.UsesPlanning, "no rule: the previous version's policy, which asks the host for nothing" );
		Assert.IsFalse( ((IGuestDecisions)OriginalGuestDecisions.Instance).UsesPlanning );
		Assert.AreEqual( SmarterRuleSet.Phase5, SmarterRuleSet.Planning | SmarterRuleSet.Detour | SmarterRuleSet.MealTime );
		foreach ( var guest in new[] { Guest(), Guest( Steady, hunger: 80, toilet: 70 ), Guest( hunger: 99, thirst: 99 ) } )
			foreach ( var attraction in new[] { Ride( 95 ), Ride( 20, 7 ), Shop( thirstEffect: 40 ), Shop( hungerEffect: 25 ), Shop( relief: true ) } )
				Assert.AreEqual( Phase4.ScoreAttraction( Context(), guest, attraction ), Smarter.ScoreAttraction( Context(), guest, attraction ), $"{guest.Id} {attraction.Kind} {attraction.HungerEffect}" );
	}

	// ---- The meal times -----------------------------------------------------------------------------

	[TestMethod]
	public void TheMealTimesAreKeptInBirthIdOrderReplacedForgottenAndNeverForAGuestWithoutAnId()
	{
		var meals = new MealTimes();
		Assert.AreEqual( -1, meals.Of( 5 ) );
		meals.Note( 9, 100 );
		meals.Note( 3, 50 );
		meals.Note( 6, 70 );
		meals.Note( 0, 99 );
		CollectionAssert.AreEqual( new[] { 3UL, 6UL, 9UL }, meals.Entries.Select( entry => entry.BirthId ).ToArray(), "birth-id order, and no entry for id 0" );
		Assert.AreEqual( 70, meals.Of( 6 ) );
		meals.Note( 6, 300 );
		Assert.AreEqual( 300, meals.Of( 6 ), "a later meal replaces the earlier one" );
		Assert.AreEqual( 3, meals.Count );
		Assert.AreEqual( -1, meals.Of( 0 ) );
		meals.Forget( 3 );
		meals.Forget( 77 );
		meals.Forget( 0 );
		CollectionAssert.AreEqual( new[] { new MealTime( 6, 300 ), new MealTime( 9, 100 ) }, meals.Entries.ToArray() );
		meals.Clear();
		Assert.AreEqual( 0, meals.Count );
	}

	[TestMethod]
	public void TheMealTimesJoinTheWorldHashOnlyWhileTheyHoldAnEntry()
	{
		var park = SmarterPark( 1, 3000, SmarterRuleSet.Phase5 );
		var seed = new WorldSeed( 1 );
		ulong Hash() => WorldStateHash.Compute( new WorldStateSources { Seed = seed, Guests = park.Guests } );
		park.Guests.SmarterGuests!.Meals.Clear();
		var plain = Hash();
		var guest = park.Guests.Guests.First( g => g.BirthId != 0 );
		park.Guests.SmarterGuests.Meals.Note( guest.BirthId, 100 );
		var with = Hash();
		Assert.AreNotEqual( plain, with, "a meal is part of the hash" );
		park.Guests.SmarterGuests.Meals.Note( guest.BirthId, 101 );
		Assert.AreNotEqual( with, Hash(), "so is its turn" );
		park.Guests.SmarterGuests.Meals.Forget( guest.BirthId );
		Assert.AreEqual( plain, Hash(), "an empty store hashes as 1.3.0 did" );
	}

	// ---- Persistence --------------------------------------------------------------------------------

	private static ParkScenario SmarterPark( ulong seed, long ticks, SmarterRuleSet rules, bool food = false )
	{
		var park = new ParkScenario( new WorldSeed( seed ), new ParkScenarioOptions { FoodShop = food }, new SmarterGuestDecisions( rules ) );
		park.Run( ticks );
		return park;
	}

	[TestMethod]
	public void TheMealTimesRoundTripThroughTheSectionAndRestoreIntoAFreshInstance()
	{
		var park = SmarterPark( 1, 5000, SmarterRuleSet.Phase5 );
		var guests = park.Guests.Guests.Where( guest => guest.BirthId != 0 ).OrderBy( guest => guest.BirthId ).Take( 2 ).ToArray();
		Assert.AreEqual( 2, guests.Length );
		park.Guests.SmarterGuests!.Meals.Note( guests[1].BirthId, park.Guests.ParkTurn - 10 );
		park.Guests.SmarterGuests.Meals.Note( guests[0].BirthId, park.Guests.ParkTurn - 40 );
		var section = SmarterGuestsSave.Capture( park.Guests )!;
		Assert.AreEqual( 2, section.Meals!.Count );
		var again = ParkResumeJson.Deserialize<SmarterGuestsSaveData>( ParkResumeJson.Serialize( section ) );
		CollectionAssert.AreEqual( section.Meals.ToArray(), again.Meals!.ToArray() );
		Assert.IsNull( SmarterGuestsSave.SectionProblem( again, park.Guests.Guests.Select( guest => guest.Id ), park.Guests.Attractions.Select( attraction => attraction.AttractionId ) ) );

		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var fresh = SmarterGuestsTests.Fresh( park );
		SmarterGuestsSave.Restore( fresh.Guests, again );
		CollectionAssert.AreEqual( park.Guests.SmarterGuests.Meals.Entries.ToArray(), fresh.Guests.SmarterGuests!.Meals.Entries.ToArray() );
		Assert.AreEqual( park.Guests.ParkTurn - 40, fresh.Guests.SmarterGuests.Meals.Of( guests[0].BirthId ) );
		Assert.AreEqual( System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( section ) ), System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( SmarterGuestsSave.Capture( fresh.Guests )! ) ), "capturing the restored park gives the same section" );
	}

	[TestMethod]
	public void ASectionWithoutMealsHasNoMemberSoTheSectionOfAnOlderParkIsAsItWas()
	{
		var park = SmarterPark( 1, 3000, SmarterRuleSet.None );
		var section = SmarterGuestsSave.Capture( park.Guests )!;
		Assert.IsNull( section.Meals );
		Assert.IsFalse( System.Text.Json.Nodes.JsonNode.Parse( System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( section ) ) )!.AsObject().ContainsKey( "Meals" ) );
	}

	[TestMethod]
	public void BadMealDataIsRefused()
	{
		var park = SmarterPark( 1, 5000, SmarterRuleSet.Phase5 );
		var ids = park.Guests.Guests.Select( guest => guest.Id ).ToArray();
		var attractions = park.Guests.Attractions.Select( attraction => attraction.AttractionId ).ToArray();
		var section = SmarterGuestsSave.Capture( park.Guests )!;
		var births = section.Births.Select( birth => birth.BirthId ).OrderBy( id => id ).ToArray();
		Assert.IsTrue( births.Length >= 3 );
		string? Problem( params MealTime[] meals ) => SmarterGuestsSave.SectionProblem( section with { Meals = meals }, ids, attractions );
		Assert.IsNull( Problem( new MealTime( births[0], 5 ), new MealTime( births[2], 0 ) ) );
		StringAssert.Contains( Problem( new MealTime( births[^1] + 100, 5 ) )!, "whom the section does not hold" );
		StringAssert.Contains( Problem( new MealTime( births[1], 5 ), new MealTime( births[0], 5 ) )!, "ascending" );
		StringAssert.Contains( Problem( new MealTime( births[1], 5 ), new MealTime( births[1], 6 ) )!, "ascending", "a guest twice" );
		StringAssert.Contains( Problem( new MealTime( births[0], -1 ) )!, "below 0" );
		StringAssert.Contains( Problem()!, "empty meal list" );
		// extension state needs the extension recorded: a save that only carries script mods holds the identity half and nothing else
		var record = new[] { new ParkModRecord( "opentpw.smarter-guests", "1.6.0", ModSet.NoFilesSha256 ) };
		var withMeals = section with { Meals = [new MealTime( births[0], 5 )] };
		Assert.IsNull( SmarterGuestsSave.ShapeProblem( withMeals, ids, record, false, attractions ) );
		StringAssert.Contains( SmarterGuestsSave.ShapeProblem( withMeals, ids, null, true, attractions )!, "does not record" );
		StringAssert.Contains( SmarterGuestsSave.ShapeProblem( section with { Stalls = [new PriceStall( births[0], 1 )] }, ids, null, true, attractions )!, "does not record" );
		Assert.IsNull( SmarterGuestsSave.ShapeProblem( section with { Meals = null, Stalls = null, Throughputs = null }, ids, null, true, attractions ) );
		// a meal after the latest turn the park's guests can be on is not a state the park can have been in (their turns' phase may put the latest one past the tick count's turn)
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var fresh = SmarterGuestsTests.Fresh( park );
		var future = section with { Meals = [new MealTime( births[0], GuestSimulation.LatestParkTurn( fresh.Guests.TickCount ) + 1 )] };
		var refused = Assert.ThrowsException<InvalidDataException>( () => SmarterGuestsSave.Restore( fresh.Guests, future ) );
		StringAssert.Contains( refused.Message, "after the park's turn" );
		Assert.AreEqual( 0, fresh.Guests.SmarterGuests!.Meals.Count, "nothing was half-restored" );
		// a meal on that latest turn restores: the guests stamp their park turn, which the phase can put there
		SmarterGuestsSave.Restore( fresh.Guests, section with { Meals = [new MealTime( births[0], GuestSimulation.LatestParkTurn( fresh.Guests.TickCount ) )] } );
		Assert.AreEqual( 1, fresh.Guests.SmarterGuests!.Meals.Count, "the meal on the latest turn is restored" );
	}

	[TestMethod]
	public void TheCycleIsTheTimePerGuestTimesTheCapacityCappedAtTheNominalCycle()
	{
		Assert.AreEqual( 12 * 256 * 3, SmarterGuestsRules.CycleTurns256Of( 12 * 256, 3 ) );
		Assert.AreEqual( 60 * 256, SmarterGuestsRules.CycleTurns256Of( 12 * 256, 20 ), "a ride of capacity 20 at 12 turns a guest is a stalled or half-empty one: the nominal cycle" );
		Assert.AreEqual( 60 * 256, SmarterGuestsRules.CycleTurns256Of( 1_000_000, 1 ) );
		Assert.AreEqual( 64, SmarterGuestsRules.CycleTurns256Of( 64, 0 ), "a capacity below 1 reads as 1" );
	}

	[TestMethod]
	public void ThePriceRuleAndTheScoreAgreeOnWhichStandsThePriceEmptied()
	{
		// a dear drink the grower cannot afford: the real guest's willingness decides the refusal, the planned guest's score decides whether the guest would have chosen it
		var guest = Guest( thirst: 96, plan: 100 * 256 );
		var dear = Shop( thirstEffect: 40 ) with { Price = 500, Willingness = 100 };
		Assert.AreEqual( 0u, Smarter.ScoreAttraction( Context(), guest, dear ), "the price zeroes it" );
		var seen = SmarterGuestsRules.Planned( guest, dear, 800, true, false );
		Assert.AreEqual( OriginalGuestDecisions.Instance.ScoreAttraction( Context(), seen, dear ) >= AttractionScore.CandidateFloor, Smarter.PriceTurnedAway( Context(), guest, dear ), "judged on the guest the score sees" );
		Assert.IsFalse( Smarter.PriceTurnedAway( Context(), guest, Shop( thirstEffect: 40 ) ), "an unpriced shop is never turned away" );
	}

	// ---- The host -----------------------------------------------------------------------------------

	/// <summary>Records what the policy is given: the guests' meal turns and plans, and which candidates the host calls a stop on the way.</summary>
	private sealed class Spy( SmarterRuleSet rules ) : SmarterGuestsTests.DelegatingDecisions( new SmarterGuestDecisions( rules ) )
	{
		public readonly List<(int GuestId, long Meal, long Plan, bool Service, bool OnTheWay)> Views = new();
		public readonly List<AttractionView> Rides = new();
		public GuestView LastGuest;
		public override uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction )
		{
			Views.Add( (guest.Id, guest.MealTurn, guest.PlanTurns256, SmarterGuestsRules.ServesNeed( attraction ), attraction.OnTheWay) );
			LastGuest = guest;
			if ( attraction.Kind == RideVisitorKind.Ride )
				Rides.Add( attraction );
			return base.ScoreAttraction( context, guest, attraction );
		}
	}

	[TestMethod]
	public void TheHostFillsInTheMealTurnOnlyForAPolicyThatPlansAndItIsTheTurnTheGuestAte()
	{
		var spy = new Spy( SmarterRuleSet.Phase5 );
		var park = new ParkScenario( new WorldSeed( 2 ), new ParkScenarioOptions { FoodShop = true }, spy );
		park.Run( 20_000 );
		Assert.IsTrue( spy.Views.Any( view => view.Meal >= 0 ), "guests ate and the policy was told" );
		Assert.IsTrue( spy.Views.All( view => view.Meal <= park.Guests.ParkTurn ), "never a turn in the future" );
		var silent = new Spy( SmarterRuleSet.None );
		var quiet = new ParkScenario( new WorldSeed( 2 ), new ParkScenarioOptions { FoodShop = true }, silent );
		quiet.Run( 20_000 );
		Assert.IsTrue( silent.Views.Count > 100 && silent.Views.All( view => view.Meal == -1 && view.Plan == -1 && !view.OnTheWay ), "a policy that does not plan is given the defaults, and the host keeps no meals for it" );
		Assert.AreEqual( 0, quiet.Guests.SmarterGuests!.Meals.Count );
	}

	[TestMethod]
	public void MealsAreNotedAtTheFoodShopRememberedByBirthIdAndForgottenWhenTheGuestLeaves()
	{
		var park = SmarterPark( 2, 20_000, SmarterRuleSet.Phase5, food: true );
		var meals = park.Guests.SmarterGuests!.Meals;
		var inPark = park.Guests.Guests.Where( guest => guest.BirthId != 0 ).Select( guest => guest.BirthId ).ToHashSet();
		Assert.IsTrue( meals.Count > 0, "the food shop fed somebody who is still here" );
		Assert.IsTrue( meals.Entries.All( entry => inPark.Contains( entry.BirthId ) ), "a guest who left has no meal on record" );
		Assert.IsTrue( meals.Entries.All( entry => entry.Turn is >= 0 && entry.Turn <= park.Guests.ParkTurn ) );
		var food = park.Attractions.Single( attraction => attraction.Role == "shop-food" ).Bridge;
		Assert.IsTrue( food.HungerEffect > 0 && food.VomitEffect > 0 );
		var thirstShops = park.Attractions.Where( attraction => attraction.Role is "shop-cheap" or "shop-dear" ).Select( attraction => attraction.Bridge ).ToArray();
		Assert.IsTrue( thirstShops.All( shop => shop.HungerEffect == 0 ), "a drink is no meal" );
	}

	[TestMethod]
	public void ThePlanOfAGrowerNearALevelIsTheQuickestRidesWaitPlusItsCycleAndOthersAreNotScanned()
	{
		var spy = new Spy( SmarterRuleSet.Phase5 );
		var park = new ParkScenario( new WorldSeed( 3 ), decisions: spy );
		park.Run( 6000 );
		var growers = park.Guests.Guests.Where( guest => (guest.Id & 3) == 0 && guest.State is GuestState.DoingNothing or GuestState.WalkingAround ).ToArray();
		var steady = park.Guests.Guests.Where( guest => (guest.Id & 3) != 0 ).ToArray();
		Assert.IsTrue( growers.Length > 0 && steady.Length > 0 );
		spy.Views.Clear();
		foreach ( var guest in steady )
		{
			guest.Hunger = 99;
			guest.Toilet = 79;
			park.Guests.PeekChoice( guest );
		}
		Assert.IsTrue( spy.Views.Count > 0 && spy.Views.All( view => view.Plan == -1 ), "a guest that never grows is not scanned for, however full" );
		// a grower that has more than one ride it could choose (which guest that is depends on the run's history)
		bool Choosable( AttractionView ride ) => ride.IsOpen && !ride.IsBrokenDown && ride.QueueLength < ride.QueueLimit && Admitted( park, spy.LastGuest, ride ) && SmarterGuestsRules.IsUnusedAdmissibleRide( Context( park.Guests.ParkTurn ), spy.LastGuest, ride );
		Guest? grower = null;
		for ( var run = 0; run < 40 && grower == null; run++ )
		{
			if ( run > 0 )
				park.Run( 250 );
			foreach ( var candidate in park.Guests.Guests.Where( guest => (guest.Id & 3) == 0 && guest.State is GuestState.DoingNothing or GuestState.WalkingAround ).ToArray() )
			{
				spy.Views.Clear();
				spy.Rides.Clear();
				candidate.Hunger = 96;
				park.Guests.PeekChoice( candidate );
				if ( spy.Rides.Count( Choosable ) >= 2 )
				{
					grower = candidate;
					break;
				}
			}
		}
		Assert.IsNotNull( grower, "a grower with two rides it could choose" );
		Assert.IsTrue( spy.Views.Count > 0 && spy.Views.All( view => view.GuestId == grower.Id && view.Plan >= 0 ), "a grower close to full gets a plan" );
		// the join test's excitement gate, which the host applies to the rides it considers
		static bool Admitted( ParkScenario park, GuestView guest, AttractionView ride )
		{
			var bridge = park.Guests.Attractions.Single( attraction => attraction.AttractionId == ride.Id );
			return QueueJoinRules.Refusal( bridge.StaticExcitementLevel, bridge.JoinDifference( park.Settings.Types[Math.Min( guest.Type, park.Settings.Types.Count - 1 )].PreferredExcitement ) ) == null;
		}
		// the plan is the quickest choosable ride's wait plus its (capped) cycle, recomputed here from the views the policy was given
		long Expected( Spy seen ) => seen.Rides.Where( ride => ride.IsOpen && !ride.IsBrokenDown && ride.QueueLength < ride.QueueLimit && Admitted( park, seen.LastGuest, ride ) && SmarterGuestsRules.IsUnusedAdmissibleRide( Context( park.Guests.ParkTurn ), seen.LastGuest, ride ) )
			.Min( ride => SmarterGuestsRules.ExpectedWait256( ride )!.Value + ride.CycleTurns256 );
		Assert.IsTrue( spy.Rides.All( ride => ride.CycleTurns256 <= RideThroughputs.DefaultCycleTurns * 256 && ride.CycleTurns256 > 0 ), "the cycle is capped at the nominal one" );
		var plan = spy.Views[0].Plan;
		Assert.AreEqual( Expected( spy ), plan );
		// a ride the guest has used or was just turned away from is not a ride it could choose: the plan then comes from the others
		var quickest = spy.Rides.Where( ride => ride.IsOpen && !ride.IsBrokenDown && ride.QueueLength < ride.QueueLimit && Admitted( park, spy.LastGuest, ride ) && SmarterGuestsRules.IsUnusedAdmissibleRide( Context( park.Guests.ParkTurn ), spy.LastGuest, ride ) )
			.OrderBy( ride => SmarterGuestsRules.ExpectedWait256( ride )!.Value + ride.CycleTurns256 ).First();
		var others = spy.Rides.Count( ride => ride.Id != quickest.Id && ride.IsOpen && !ride.IsBrokenDown && ride.QueueLength < ride.QueueLimit && Admitted( park, spy.LastGuest, ride ) && SmarterGuestsRules.IsUnusedAdmissibleRide( Context( park.Guests.ParkTurn ), spy.LastGuest, ride ) );
		Assert.IsTrue( others > 0, "another ride remains to plan from" );
		foreach ( var history in new[] { grower.UsedHistory, grower.TemporaryHistory } )
		{
			Array.Clear( grower.UsedHistory );
			Array.Clear( grower.TemporaryHistory );
			history[1] = quickest.Id;
			spy.Views.Clear();
			spy.Rides.Clear();
			park.Guests.PeekChoice( grower );
			Assert.AreEqual( Expected( spy ), spy.Views[0].Plan, "planned from the rides left" );
			Assert.IsTrue( spy.Views[0].Plan >= plan, "never quicker than with the ride in reach" );
		}
		Array.Clear( grower.UsedHistory );
		Array.Clear( grower.TemporaryHistory );
		// the minor decision plans from the ride the guest is walking to
		var target = park.Guests.Attractions.First( ride => ride.Kind == RideVisitorKind.Ride && ride.JoinCell != null );
		var viewOfTarget = park.Guests.ViewOf( target, grower );
		var withPlan = park.Guests.WithPlanHorizon( park.Guests.ViewOf( grower ), grower, target );
		Assert.AreEqual( SmarterGuestsRules.ExpectedWait256( viewOfTarget )!.Value + viewOfTarget.CycleTurns256, withPlan.PlanTurns256 );
		grower.Hunger = 20;
		Assert.AreEqual( -1, park.Guests.WithPlanHorizon( park.Guests.ViewOf( grower ), grower, target ).PlanTurns256, "no meter near a level: no plan" );
		grower.Hunger = 96;
		spy.Views.Clear();
		grower.Hunger = 20;
		park.Guests.PeekChoice( grower );
		Assert.IsTrue( spy.Views.Count > 0 && spy.Views.All( view => view.Plan == -1 ), "a grower far from every level is not scanned for" );
	}

	[TestMethod]
	public void OnlyTheMinorDecisionCallsACandidateAStopOnTheWay()
	{
		var spy = new Spy( SmarterRuleSet.Phase5 );
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: spy );
		park.Run( 12_000 );
		Assert.IsTrue( spy.Views.Any( view => view.OnTheWay ), "the minor decision's window offered a candidate" );
		spy.Views.Clear();
		foreach ( var guest in park.Guests.Guests.ToArray() )
			park.Guests.PeekChoice( guest );
		Assert.IsTrue( spy.Views.Count > 0 && spy.Views.All( view => !view.OnTheWay ), "the major decision's candidates are not on the way to anything" );
		var plain = new Spy( SmarterRuleSet.Planning | SmarterRuleSet.MealTime );
		var other = new ParkScenario( new WorldSeed( 1 ), decisions: plain );
		other.Run( 12_000 );
		Assert.IsTrue( plain.Views.Any( view => view.OnTheWay ), "the host marks the window for any policy that plans; the policy decides what it means (detour off here)" );
	}

	// ---- The extension as a whole -------------------------------------------------------------------

	[TestMethod]
	public void WithThePhaseFiveRulesOffTheScenarioIsVersion130sRunAndWithThemOnItIsTheSameWhereNoRuleActs()
	{
		// the pin of 1.3.0 (GuestMeasurementTests.ScenarioSmarterFixedRunHash): the scenario has no food shop and nobody near a level at a decision, so the rules never act in it
		var off = new ParkScenario( new WorldSeed( 1 ), decisions: new SmarterGuestDecisions( SmarterRuleSet.None ) );
		off.Run( 12_000 );
		var on = new ParkScenario( new WorldSeed( 1 ), decisions: new SmarterGuestDecisions( SmarterRuleSet.Phase5 ) );
		on.Run( 12_000 );
		Assert.AreEqual( off.Hash, on.Hash );
		Assert.AreEqual( WorldStateHash.Compute( new WorldStateSources { Seed = off.Seed, Guests = off.Guests } ), WorldStateHash.Compute( new WorldStateSources { Seed = on.Seed, Guests = on.Guests } ) );
		Assert.AreEqual( off.Guests.RandomState, on.Guests.RandomState );
	}

	[TestMethod]
	public void WithAFoodShopTheMealRuleChangesTheRunAndEachRuleOnItsOwnIsADeterministicReplay()
	{
		ulong Run( SmarterRuleSet rules )
		{
			var park = SmarterPark( 4, 30_000, rules, food: true );
			return park.Hash;
		}
		Assert.AreEqual( Run( SmarterRuleSet.MealTime ), Run( SmarterRuleSet.MealTime ), "replay" );
		Assert.AreEqual( Run( SmarterRuleSet.Phase5 ), Run( SmarterRuleSet.Phase5 ), "replay" );
		Assert.AreNotEqual( Run( SmarterRuleSet.None ), Run( SmarterRuleSet.MealTime ), "after a meal the thrill ride scores lower, so somebody rides something else" );
	}

	[TestMethod]
	public void TheReportCanSwitchThePhaseFiveRulesOffAndSaysSo()
	{
		CollectionAssert.AreEqual( new[] { SmarterRuleSet.None }, new[] { GuestReport.ParseRules( "none" ) } );
		Assert.AreEqual( SmarterRuleSet.All, GuestReport.ParseRules( "all" ) );
		Assert.AreEqual( SmarterRuleSet.Planning | SmarterRuleSet.MealTime, GuestReport.ParseRules( "planning, meal" ) );
		Assert.ThrowsException<ArgumentException>( () => GuestReport.ParseRules( "everything" ) );
		var options = new GuestReportOptions
		{
			Seeds = new ulong[] { 4 }, WarmupDays = 20, MeasuredDays = 60, Performance = false, Scenario = new ParkScenarioOptions { FoodShop = true },
			Extensions = GuestReport.ResolveExtensions( new[] { "smarter-guests" } )
		};
		ulong Reported( GuestReportOptions run ) => GuestReport.Run( run ).Scenarios.Single( scenario => scenario.Name == GuestReport.ScenarioName ).Seeds.Single().StateHash;
		ulong Explicit( SmarterRuleSet rules )
		{
			var park = new ParkScenario( new WorldSeed( 4 ), options.Scenario, new SmarterGuestDecisions( rules ) );
			park.Run( GuestReport.TicksForDays( 80 ) );
			return park.Hash;
		}
		var none = GuestReport.ToJsonObject( GuestReport.Run( options with { Rules = SmarterRuleSet.None } ) );
		var all = GuestReport.ToJsonObject( GuestReport.Run( options ) );
		Assert.IsFalse( all.ContainsKey( "rules" ), "the shipped extension's report says nothing extra" );
		Assert.AreEqual( "None", (string)none["rules"]! );
		// the run really used the rules it says: its end state is an explicit run's, and not the other's
		Assert.AreEqual( Explicit( SmarterRuleSet.None ), Reported( options with { Rules = SmarterRuleSet.None } ), "the report's run with the rules off is the explicit run with them off" );
		Assert.AreEqual( Explicit( SmarterRuleSet.All ), Reported( options ), "and the default is all three" );
		Assert.AreNotEqual( Explicit( SmarterRuleSet.None ), Explicit( SmarterRuleSet.All ), "which differ in this scenario" );
		Assert.ThrowsException<ArgumentException>( () => GuestReport.Run( options with { Rules = SmarterRuleSet.None, Mods = ["x"] } ) );
		StringAssert.Contains( GuestReportCompare.Compare( all, none, [] ).Notes, "smarter-guests rules: baseline All, candidate None" );
	}
}
