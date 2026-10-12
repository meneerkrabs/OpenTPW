using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The guests' needs and starting values (docs/reverse/ORACLE.md F25). Expected values are results of the original routines executed in the PowerPC oracle
/// (<c>guest-needs-turn</c>, <c>guest-constructor</c>, <c>shop-sale-litter</c>), which compares this code with them over 529, 517 and 526 inputs.
/// </summary>
[TestClass]
public class NeedsUpdateTests
{
	private static GuestMeters Meters( float happiness = 50, float thirst = 0, float hunger = 0, float toilet = 0, float illness = 0 ) =>
		new( happiness, thirst, hunger, toilet, illness );

	[TestMethod]
	public void TheNeedsBlockRunsOnlyWhenTheTurnAndTheIdAgreeInTheirLowTwoBits()
	{
		Assert.IsTrue( NeedsUpdate.IsPhase( 0, 4 ) );
		Assert.IsTrue( NeedsUpdate.IsPhase( 1001, 5 ) );
		Assert.IsFalse( NeedsUpdate.IsPhase( 0, 5 ) );
		Assert.IsTrue( NeedsUpdate.IsPhase( uint.MaxValue, 3 ), "the turn is an unsigned word" );
		var off = NeedsUpdate.Apply( Meters( thirst: 10, hunger: 10, toilet: 10 ), 120, 0, 5 );
		Assert.AreEqual( Meters( thirst: 10, hunger: 10, toilet: 10 ), off.Meters );
		Assert.AreEqual( 120, off.ExitLevel, "off phase nothing changes, not even the exit counter" );
		Assert.IsNull( off.ToiletSpeedTerm, "and +194 is left alone" );
	}

	[TestMethod]
	public void OnItsPhaseAGuestCountsDownAndOnAMultipleOfSixteenItsNeedsGrow()
	{
		// the oracle's first edge: id 4, turn 0, needs 10: thirst and hunger +2, toilet +1, the counter 119
		var grown = NeedsUpdate.Apply( Meters( thirst: 10, hunger: 10, toilet: 10 ), 120, 0, 4 );
		Assert.AreEqual( Meters( thirst: 12, hunger: 12, toilet: 11 ), grown.Meters );
		Assert.AreEqual( 119, grown.ExitLevel );
		Assert.AreEqual( 0, grown.ToiletSpeedTerm );
		// turn 4 is a phase turn for id 4 but not a multiple of 16: only the counter moves
		var plain = NeedsUpdate.Apply( Meters( thirst: 10, hunger: 10, toilet: 10 ), 120, 4, 4 );
		Assert.AreEqual( Meters( thirst: 10, hunger: 10, toilet: 10 ), plain.Meters );
		Assert.AreEqual( 119, plain.ExitLevel );
		// only a guest whose id is a multiple of four ever meets a multiple of 16 on its phase
		foreach ( var id in new[] { 1, 2, 3, 5, 6, 7 } )
			Assert.IsTrue( Enumerable.Range( 0, 64 ).Select( turn => (uint)(16 * turn) ).All( turn => !NeedsUpdate.IsPhase( turn, id ) ), $"id {id}" );
		// the counter keeps counting below 0
		Assert.AreEqual( -1, NeedsUpdate.Apply( Meters(), 0, 4, 4 ).ExitLevel );
		Assert.AreEqual( int.MaxValue, NeedsUpdate.Apply( Meters(), int.MinValue, 4, 4 ).ExitLevel, "a signed word wraps" );
	}

	[TestMethod]
	public void ANeedAtExactlyOneHundredCostsHappinessEachPhase()
	{
		// 98 + 2 = 100: hunger and thirst reach a byte of 100 (and toilet 99 + 1): three penalties of 1 each
		var full = NeedsUpdate.Apply( Meters( thirst: 98, hunger: 98, toilet: 99 ), 120, 16, 8 );
		Assert.AreEqual( 47f, full.Meters.Happiness );
		Assert.AreEqual( 100f, full.Meters.Hunger );
		// 99 + 2 = 101 clamps to 100: the clamp makes a byte of exactly 100 the common case
		Assert.AreEqual( 47f, NeedsUpdate.Apply( Meters( thirst: 99, hunger: 99, toilet: 99.5f ), 120, 16, 8 ).Meters.Happiness );
		// four needs at 100 cost 4; a need at 100.9 still truncates to 100, 101 does not
		Assert.AreEqual( 46f, NeedsUpdate.Apply( Meters( thirst: 100, hunger: 100, toilet: 100, illness: 100 ), 120, 4, 4 ).Meters.Happiness );
		Assert.AreEqual( 48f, NeedsUpdate.Apply( Meters( thirst: 100.9f, hunger: 100, toilet: 80 ), 120, 4, 4 ).Meters.Happiness, "hunger 100 and thirst 100.9 (a byte of 100) cost one each; toilet 80 none" );
	}

	[TestMethod]
	public void TheCellWordsMoveHappinessIllnessAndHunger()
	{
		var moved = NeedsUpdate.Apply( Meters( happiness: 10, illness: 90, hunger: 20 ), 120, 4, 4, cellHappiness: -100, cellIllness: 100, cellHunger: -100 );
		Assert.AreEqual( Meters( happiness: 0, illness: 100, hunger: 0 ), moved.Meters, "each clamps to 0..100" );
		var some = NeedsUpdate.Apply( Meters(), 120, 4, 4, 5, -5, 3 );
		Assert.AreEqual( Meters( happiness: 55, illness: 0, hunger: 3 ), some.Meters );
	}

	[TestMethod]
	public void TheToiletSpeedTermFollowsTheToiletByteAboveEighty()
	{
		Assert.AreEqual( 0, NeedsUpdate.Apply( Meters( toilet: 80.9f ), 120, 4, 4 ).ToiletSpeedTerm );
		Assert.AreEqual( 25, NeedsUpdate.Apply( Meters( toilet: 81 ), 120, 4, 4 ).ToiletSpeedTerm );
	}

	[TestMethod]
	public void EveryTwentiethTurnShiftsAZeroIntoTheTemporaryHistory()
	{
		var history = new[] { 7, 8, 9, 10 };
		NeedsUpdate.DecayHistory( history, 21 );
		CollectionAssert.AreEqual( new[] { 7, 8, 9, 10 }, history );
		NeedsUpdate.DecayHistory( history, 20 );
		CollectionAssert.AreEqual( new[] { 0, 7, 8, 9 }, history );
		NeedsUpdate.DecayHistory( history, 40 );
		NeedsUpdate.DecayHistory( history, 60 );
		NeedsUpdate.DecayHistory( history, 80 );
		CollectionAssert.AreEqual( new[] { 0, 0, 0, 0 }, history, "a refusal is forgotten after four such turns" );
	}

	/// <summary>The world generator the Python side draws from (<c>0x105328</c>): the state's successor, returned as its absolute value.</summary>
	private static Func<uint> WorldDraws( uint state ) => () =>
	{
		state = unchecked( state * 1664525u + 1013904223u );
		return state == 0x80000000 ? state : (uint)Math.Abs( (int)state );
	};

	[TestMethod]
	public void AGuestStartsWithTheOriginalConstructorsValues()
	{
		var types = new[] { (80, 300, 40), (65, 500, 30), (50, 600, 40), (35, 700, 35), (65, 750, 30), (80, 600, 40), (45, 400, 30), (80, 500, 40) }
			.Select( row => new GuestType( row.Item1, row.Item2, row.Item3 ) ).ToList();
		// the oracle's first constructor input: world state 0, ExitLevel 120 ± 42, StartingCashVarPc 15
		var start = GuestStart.FromDraws( types, 120, 42, 15, WorldDraws( 0 ) );
		Assert.AreEqual( new GuestStartValues( 2, 528, 50f, 12f, 47f, 4f, 0f, 0f, 109 ), start );
		Assert.AreEqual( GuestStart.StartingHappiness, start.Happiness );
	}

	[TestMethod]
	public void StartingValuesStayInTheirDomains()
	{
		var types = new[] { new GuestType( 80, 300, 40 ), new GuestType( 50, 600, 40 ) };
		var draw = WorldDraws( 12345 );
		for ( var count = 0; count < 2000; count++ )
		{
			var start = GuestStart.FromDraws( types, 120, 60, 15, draw );
			Assert.IsTrue( start.Thirst is >= 0 and < 50 && start.Hunger is >= 0 and < 50 && start.Toilet is >= 0 and < 30 );
			Assert.IsTrue( start.ExitLevel is >= 120 - 60 and <= 120 + 60 - 1, $"{start.ExitLevel}" );
			var cash = types[start.Type].StartingCash;
			Assert.IsTrue( start.Money >= cash * 85 / 100 && start.Money <= cash * 115 / 100, $"{start.Money} of {cash}" );
			Assert.AreEqual( 0f, start.Illness );
		}
	}

	[TestMethod]
	public void ANewGuestInTheSimulationStartsLikeTheOriginalsAndCountsDownOncePerFourTurns()
	{
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 120;
		settings.ExitLevelVarSeconds = 60;
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 21 ) { ArrivalsEnabled = false };
		var guests = Enumerable.Range( 0, 40 ).Select( _ => simulation.SpawnInPark( 6, 5 ) ).ToList();
		Assert.IsTrue( guests.All( guest => guest.Happiness == 50f && guest.Nausea == 0f ) );
		Assert.IsTrue( guests.Select( guest => guest.Thirst ).Distinct().Count() > 10, "drawn per guest, not all 0" );
		Assert.IsTrue( guests.All( guest => guest.Thirst is >= 0 and < 50 && guest.Hunger is >= 0 and < 50 && guest.Toilet is >= 0 and < 30 ) );
		Assert.IsTrue( guests.All( guest => guest.ExitLevel is >= 60 and <= 179 ) );
		var guest = guests[0];
		var before = guest.ExitLevel;
		var start = simulation.ParkTurn;
		while ( simulation.ParkTurn < start + 40 )
			simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
		Assert.AreEqual( before - 10, guest.ExitLevel, "40 park turns hold ten needs phases of any one guest" );
		// 40 turns are 9.92 s: the counter's unit is 0.992 s
		Assert.AreEqual( 9.92, 40 * ParkCalendar.TurnMilliseconds / 1000.0, 1e-9 );
	}

	[TestMethod]
	public void IllnessDoesNotDecayOnItsOwn()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 6, 5 );
		guest.Nausea = 50;
		for ( var tick = 0; tick < 60 * 60; tick++ )
			simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
		Assert.AreEqual( 50f, guest.Nausea, "the original lowers illness only through environment words and effects" );
	}

	[TestMethod]
	public void AUseEffectsSubtractsNeedsAndAddsIllnessAndHappinessWithTheOriginalsClamps()
	{
		// the oracle's shop-sale-litter first input: thirst 40, hunger 40, vomit 10, happiness 50 with effects 20, 30, 5, 10
		var result = UseEffects.Apply( new GuestMeters( 50, 40, 40, 0, 10 ), 20, 30, 5, 10 );
		Assert.AreEqual( new GuestMeters( 60, 20, 10, 0, 15 ), result );
		var clamped = UseEffects.Apply( new GuestMeters( 95, 10, 5, 0, 99 ), 50, 50, 50, 50 );
		Assert.AreEqual( new GuestMeters( 100, 0, 0, 0, 100 ), clamped );
		var negative = UseEffects.Apply( new GuestMeters( 50, 10, 10, 0, 10 ), -30, -3, 0, 0 );
		Assert.AreEqual( new GuestMeters( 50, 40, 13, 0, 10 ), negative, "a negative effect raises the need" );
	}
}
