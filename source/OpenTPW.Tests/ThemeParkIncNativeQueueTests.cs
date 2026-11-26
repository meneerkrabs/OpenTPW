using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's native guest states, phase P3a (docs/reverse/TPI-GUESTS.md): the attraction score 0x6101E0, the major decision 0x610080, the walk to a ride (state 13), the queue (state 14), the queue
/// step (state 15), the queue functions and the bridged boarding of state 16. The rig is QueueTests' ride with its five queue cells up the column x = 2 and its back cell (2, 1) beside the path y = 0,
/// with the switch on and the ride offered as the attraction thing 40.
/// </summary>
[TestClass]
public sealed class ThemeParkIncNativeQueueTests
{
	private const ushort Thing = 40;

	/// <summary>The park as the native guests read it: one attraction, the rig's ride.</summary>
	private sealed class World( RideVisitorBridge ride, ThemeParkIncScoreTerms terms ) : IThemeParkIncGuestWorld
	{
		public ThemeParkIncScoreTerms Terms = terms;
		public IReadOnlyList<ThemeParkIncThingView> Bins() => [];
		public IReadOnlyList<ThemeParkIncThingView> Shows() => [];
		public IReadOnlyList<ThemeParkIncEntertainerView> Entertainers() => [];
		public ThemeParkIncThingView? Thing( ushort thingId ) => null;
		public void RecordCustomer( ushort thingId ) { }
		public IReadOnlyList<ThemeParkIncAttractionView> Attractions() => [new ThemeParkIncAttractionView( ThemeParkIncNativeQueueTests.Thing, (2, 6), ride, Terms )];
	}

	/// <summary>Score words that keep every generation's score (100 percent) and no weather change.</summary>
	private static readonly ThemeParkIncScoreTerms Plain = new( 0, 0, false, 0, 0, 0, 0, 100, 100, 100 );

	private static (QueueTests.Rig Rig, World World) Rig( ThemeParkIncScoreTerms? terms = null, int queueCells = 5, bool takesGuests = false, QueueParameters? parameters = null )
	{
		var rig = new QueueTests.Rig( queueCells: queueCells, takesGuests: takesGuests, parameters: parameters, configure: settings =>
		{
			settings.TpiGuestStart = new( Enumerable.Range( 0, 24 ).Select( i => new ThemeParkIncGuestType( 55, 300 + 20 * i, 40 + i, i / 8 ) ).ToArray(), 400, 30, 15, 70, 20, 10, 10 );
			settings.TpiNative = ThemeParkIncNativeGuestTests.NativeSettings() with
			{
				DecisionVariables = new( 30, 5, 0 ), DecisionWeights = DecisionWeights.Shipped
			};
		} );
		var world = new World( rig.Ride, terms ?? Plain );
		rig.Simulation.ThemeParkIncNativeGuests = true;
		rig.Simulation.ThemeParkIncWorld = world;
		return (rig, world);
	}

	/// <summary>A native guest at (x, y), happy, with no needs, prank code 0 and a long stay.</summary>
	private static (Guest Guest, ThemeParkIncGuestRecord Tpi) Native( QueueTests.Rig rig, int x = 2, int y = 0 )
	{
		var guest = rig.Simulation.SpawnInPark( x, y );
		(guest.Happiness, guest.ExitLevel, guest.TpiPrankCode) = (50, 100000, 0);
		(guest.Hunger, guest.Thirst, guest.Toilet, guest.Nausea, guest.Litter) = (0, 0, 0, 0, 0);
		return (guest, guest.Tpi!);
	}

	/// <summary>Sends the native guest walking to the rig's ride (the major decision's choice and state 13).</summary>
	private static void SendToRide( QueueTests.Rig rig, Guest guest, ThemeParkIncGuestRecord tpi )
	{
		rig.Simulation.ThemeParkIncMajorDecision( guest, tpi, rig.Simulation.Settings.TpiNative! );
		Assert.AreEqual( Thing, tpi.Target, "the major decision chooses the only ride" );
		rig.Simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToMajor );
	}

	/// <summary>Runs park turns until <paramref name="done"/> holds, keeping the needs empty; false after <paramref name="turns"/> turns.</summary>
	private static bool RunUntil( QueueTests.Rig rig, Func<bool> done, int turns = 400 )
	{
		for ( var turn = 0; turn < turns; turn++ )
		{
			if ( done() )
				return true;
			rig.StepTurn();
			foreach ( var guest in rig.Simulation.Guests )
				(guest.Hunger, guest.Thirst, guest.Toilet, guest.Nausea, guest.Litter) = (0, 0, 0, 0, 0);
		}
		return done();
	}

	// ---- The attraction score ----------------------------------------------------------------------------------------------------

	private static ThemeParkIncAttractionScoreInput ScoreInput() => new( false, false, false, Plain, 0, 0, false, 0, 10, 10, 11, 12, 0, 3, 2, 60, true, 90, 50f, 30f, 85f, 10f, 35, 10, false, 100,
		0, 0, 0, 100, 40, [0, 0, 0, 0], [0, 0, 0, 0] );

	/// <summary>Cases the oracle's reading of 0x6101E0 scores (tools/ppc-analysis/oracle_x86/tpi_guest_states_oracle.py, port_score, which agrees with the executable over 5000 random cases).</summary>
	[TestMethod]
	public void TheScoreMatchesTheOraclesReading()
	{
		var weights = DecisionWeights.Shipped;
		var variables = new DecisionVariables( 30, 5, 0 );
		uint Score( ThemeParkIncAttractionScoreInput input ) => ThemeParkIncAttractionScore.Compute( input, weights, variables );
		var input = ScoreInput();
		Assert.AreEqual( 21u, Score( input ) );
		Assert.AreEqual( 32u, Score( input with { Relief = true } ), "the toilet and illness matches count for a relief thing (+0x42 bit 1)" );
		Assert.AreEqual( 105u, Score( input with { AgeDays = 10 } ), "a ride younger than DecisionVariable1 days scores DecisionVariable2 times" );
		Assert.AreEqual( 29u, Score( input with { GoldenTicketCost = 3 } ), "_ftol(((3 + 1) × 0.1 + 1.0) × 21) at 24 bits" );
		Assert.AreEqual( 25u, Score( input with { UpgradeCost = 6000 } ), "_ftol((6000 / 3000 × 0.1 + 1.0) × 21)" );
		Assert.AreEqual( 23u, Score( input with { Weather = 2, Terms = Plain with { RainPercentage = 150, ParentPercentage = 75 }, Generation = 1 } ), "21 × 150 / 100, then × 75 / 100" );
		Assert.AreEqual( 6u, Score( input with { Weather = 2, Terms = Plain with { AppearanceEffect = 3 }, HoldsBalloon = true, BalloonCountdown = 100, DiscardBalloonPercentage = 50 } ),
			"an umbrella shop with a balloon: f = 100 − 100 × 100 / 255 = 61, × 50 / 100 = 30, score × 30 / 100" );
		Assert.AreEqual( 0u, Score( input with { UsedHistory = [0, 40, 0, 0], TemporaryHistory = [40, 0, 0, 40] } ), "21 / 4, then / 5 and / 2" );
		Assert.AreEqual( 7u, Score( input with { BackX = 30, BackY = 30 } ), "out of the queue's reach, the distance match far" );
		Assert.AreEqual( 0u, Score( input with { Terms = Plain with { GenderSpecific = 2 } } ), "a type for women only (GenderSpecific 2) refuses a male bank" );
		Assert.AreEqual( 0u, Score( input with { Special = true } ), "a special ride awaiting its activation (0x6242F0, 0x6101FD) scores 0" );
		Assert.AreEqual( 0u, Score( input with { Terms = Plain with { AppearanceEffect = 3 } } ), "an umbrella shop scores 0 without rain, storm or snow" );
		Assert.AreEqual( 0u, Score( input with { Terms = Plain with { AppearanceEffect = 1 }, UmbrellaState = UmbrellaRules.StateOpen } ), "a balloon shop scores 0 under an open umbrella" );
	}

	[TestMethod]
	public void TheX87ProductRoundsOnce()
	{
		Assert.AreEqual( 0.4f, ThemeParkIncX87Single.Multiply( 4, 0.1 ) );
		Assert.AreEqual( (float)(3 * 0.1), ThemeParkIncX87Single.Multiply( 3, 0.1 ) );
		Assert.AreEqual( -16777217f, ThemeParkIncX87Single.Multiply( -16777217, 1.0 ), "the exact product rounds to nearest even" );
		Assert.AreEqual( 0, ThemeParkIncX87Single.Truncate( 0.99 ) );
		Assert.AreEqual( -1, ThemeParkIncX87Single.Truncate( -1.5 ) );
	}

	[TestMethod]
	public void TheQueueLimitFollowsTheDataFormula()
	{
		Assert.AreEqual( RideVisitorBridge.NoQueueLimit, GuestSimulation.ThemeParkIncQueueLimit( QueueParameters.Default ), "no Info.HasQueue: 100" );
		Assert.AreEqual( 216u, GuestSimulation.ThemeParkIncQueueLimit( new QueueParameters( true, false, false, 130, 60, 60, 5, 3 ) ), "130 × 5 × 1 / 3" );
		Assert.AreEqual( 4u, GuestSimulation.ThemeParkIncQueueLimit( new QueueParameters( true, false, false, 1, 0, 60, 1, 30 ) ), "at least 4" );
		Assert.AreEqual( 433u, GuestSimulation.ThemeParkIncQueueLimit( new QueueParameters( true, false, false, 130, 120, 60, 5, 3 ) ), "the speed over InitSpeed scales it" );
		Assert.AreEqual( 0u, GuestSimulation.ThemeParkIncQueueLimit( new QueueParameters( true, false, false, 130, 60, 60, 5, 0 ) ), "a duration of 0: infinity, which _ftol makes 0" );
		Assert.AreEqual( 4u, GuestSimulation.ThemeParkIncQueueLimit( new QueueParameters( true, false, false, 130, 60, 60, 0, 0 ) ), "0 / 0: NaN fails the test against 4.0 and becomes 4" );
	}

	[TestMethod]
	public void StatesThirteenToSixteenRunInThisBuild()
	{
		foreach ( var state in new[] { 13, 14, 15, 16 } )
			Assert.IsTrue( ThemeParkIncGuestStates.RunsInThisBuild( state ), $"state {state}" );
		foreach ( var state in new[] { 10, 11, 12, 17, 18, 19, 20, 21 } )
			Assert.IsFalse( ThemeParkIncGuestStates.RunsInThisBuild( state ), $"state {state}" );
	}

	// ---- The ride choice, the walk and the queue -----------------------------------------------------------------------------------

	[TestMethod]
	public void AGuestChoosesTheRideJoinsTheQueueBoardsAndIsIdleAfterwards()
	{
		var (rig, _) = Rig( takesGuests: true );
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		var seen = new HashSet<int>();
		var boarded = false;
		Assert.IsTrue( RunUntil( rig, () =>
		{
			seen.Add( tpi.State );
			boarded |= guest.State == GuestState.Using;
			return seen.Contains( ThemeParkIncGuestStates.BeingLoaded ) && tpi.State == ThemeParkIncGuestStates.Idle;
		}, 2000 ), $"the guest rides and comes back idle (states {string.Join( ", ", seen )})" );
		CollectionAssert.IsSubsetOf( new[] { 13, 14, 15, 16 }, seen.ToArray(), "the walk, the queue step, the queue and the bridged boarding" );
		Assert.IsTrue( boarded && rig.Ride.BoardedTotal == 1, "the bridged boarding rides through the bridge" );
		Assert.AreEqual( (0, 0, 0), (tpi.Target, guest.AttractionId, rig.Ride.QueueLength), "back idle, no target and out of the list" );
		CollectionAssert.AreEqual( new[] { (int)Thing, 0, 0, 0 }, guest.UsedHistory,
			"the use routine 0x6118D0 pushes the thing id, not Theme Park World's attraction id, into +0x20A" );
	}

	[TestMethod]
	public void TheQueueStepAndTheQueueKeepThePlaceAndTheLinks()
	{
		var (rig, _) = Rig();
		rig.HoldAdmission();
		var first = Native( rig );
		var second = Native( rig, 3, 0 );
		SendToRide( rig, first.Guest, first.Tpi );
		SendToRide( rig, second.Guest, second.Tpi );
		Assert.IsTrue( RunUntil( rig, () => first.Tpi.State == ThemeParkIncGuestStates.StandingInQueue && second.Tpi.State == ThemeParkIncGuestStates.StandingInQueue ),
			"both stand in the queue" );
		var queue = rig.Ride.Queue;
		var head = queue[0] == first.Guest.Id ? first : second;
		var tail = queue[0] == first.Guest.Id ? second : first;
		Assert.AreEqual( (0, 1), (head.Tpi.Byte21B, tail.Tpi.Byte21B), "+0x21B is the place in the list (0x619530)" );
		Assert.AreEqual( (tail.Guest.Id, 0), (head.Tpi.QueueLink, head.Tpi.QueueSlot), "+0x2CC is the next guest, +0x2CE the previous" );
		Assert.AreEqual( (0, head.Guest.Id), (tail.Tpi.QueueLink, tail.Tpi.QueueSlot) );
		Assert.AreEqual( 2, rig.Simulation.TpiQueuePosition( rig.Ride, 0 ), "0x5E0280 with id 0 is the length" );
		Assert.AreEqual( GuestState.Queueing, head.Guest.State, "the projection of 14" );
	}

	[TestMethod]
	public void AQueueTooLongRefusesWithThoughtSixteenAndTheHistory()
	{
		var (rig, _) = Rig( parameters: new QueueParameters( true, false, false, 1, 0, 60, 1, 30 ) );
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 5 ).Select( index => Native( rig, 3 + index % 3, 0 ) ).ToList();
		foreach ( var (guest, tpi) in guests.Take( 4 ) )
			SendToRide( rig, guest, tpi );
		Assert.IsTrue( RunUntil( rig, () => rig.Ride.QueueLength == 4 ), "the limit's four" );
		var (late, lateTpi) = guests[4];
		SendToRide( rig, late, lateTpi );
		Assert.IsTrue( RunUntil( rig, () => lateTpi.State == ThemeParkIncGuestStates.Idle ), "the late guest refuses at the back" );
		Assert.AreEqual( GuestThought.QueueTooLong, late.Thought );
		Assert.AreEqual( (int)Thing, late.TemporaryHistory[0], "0x611D20 pushes the ride into the temporary history" );
		Assert.AreEqual( 0, lateTpi.Target );
	}

	[TestMethod]
	public void ABoringRideRefusesWithThoughtTwelve()
	{
		var (rig, _) = Rig();
		var (guest, tpi) = Native( rig );
		tpi.PreferredExcitement = 200;
		SendToRide( rig, guest, tpi );
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.Idle ) );
		Assert.AreEqual( GuestThought.Bored, guest.Thought, "0x611410 below −45: not exciting enough, thought 12" );
		Assert.AreEqual( (0, 0), (tpi.Target, tpi.Word224), "+0x204 and +0x224 cleared" );
		Assert.AreEqual( 0, rig.Ride.QueueLength );
	}

	/// <summary>Runs until the guest stands in the queue, then sets its fields for one state-14 update on the next turn and returns the generator's state before it.</summary>
	private static uint StandThenOneUpdate( QueueTests.Rig rig, Guest guest, ThemeParkIncGuestRecord tpi, Action set )
	{
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.StandingInQueue ), "the guest stands" );
		while ( !rig.Simulation.NextTickStartsTurn || ((rig.Simulation.ParkTurn + 1) & 3) == (guest.Id & 3) )
			rig.Step();
		set();
		var before = rig.Simulation.World.State;
		rig.Step();
		return before;
	}

	[TestMethod]
	public void TheHeadTurnsByTwoDrawsOnceInTenTurns()
	{
		var (rig, _) = Rig();
		rig.HoldAdmission();
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		uint second = 0;
		var heading = 0;
		var before = StandThenOneUpdate( rig, guest, tpi, () =>
		{
			heading = tpi.Heading = 0x7F0;
			tpi.SpotTurn = (int)rig.Simulation.ParkTurn;
			tpi.Word224 = (int)rig.Simulation.ParkTurn;
			// a generator state whose first word is 0 modulo 10
			for ( uint seed = 1; ; seed++ )
			{
				var replica = new WorldRng( seed );
				if ( replica.NextWord() % 10 != 0 )
					continue;
				second = replica.NextWord();
				rig.Simulation.World.Reseed( seed );
				break;
			}
		} );
		var expected = heading + (int)(second % 800) - 400;
		expected = expected < 0 ? expected + 0x800 : expected >= 0x800 ? expected - 0x800 : expected;
		Assert.AreEqual( expected, tpi.Heading, "+0x2C + R2 % 800 − 400, wrapped (0x617D0C)" );
		var replicaAfter = new WorldRng( before );
		replicaAfter.NextWord();
		replicaAfter.NextWord();
		Assert.AreEqual( replicaAfter.State, rig.Simulation.World.State, "two world draws, nothing else" );
	}

	[TestMethod]
	public void UnhappyGuestsLeaveWithThoughtElevenAndTheWalkAwayCount()
	{
		var (rig, _) = Rig();
		rig.HoldAdmission();
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		StandThenOneUpdate( rig, guest, tpi, () =>
		{
			guest.Happiness = 5;
			tpi.SpotTurn = (int)rig.Simulation.ParkTurn - 100;
		} );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, tpi.State );
		Assert.AreEqual( GuestThought.Unhappy, guest.Thought );
		Assert.AreEqual( 0f, guest.Happiness, "the dismissal's MediumHappinessChange (0x619700(0)), clamped" );
		Assert.AreEqual( 0, rig.Ride.QueueLength );
	}

	[TestMethod]
	public void BoredGuestsLeaveAfterAHundredTurns()
	{
		var (rig, _) = Rig();
		rig.HoldAdmission();
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		StandThenOneUpdate( rig, guest, tpi, () =>
		{
			tpi.SpotTurn = (int)rig.Simulation.ParkTurn;
			tpi.Word224 = (int)rig.Simulation.ParkTurn - 101;
		} );
		Assert.AreEqual( (ThemeParkIncGuestStates.Idle, GuestThought.Bored), (tpi.State, guest.Thought) );
	}

	[TestMethod]
	public void TheUmbrellaShopsQueueReactsToTheWeatherWithOneDraw()
	{
		var (rig, _) = Rig( terms: Plain with { AppearanceEffect = 3 } );
		rig.HoldAdmission();
		var weather = UmbrellaRules.WeatherRain;
		rig.Simulation.ArrivalWeatherState = () => weather;
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		var before = StandThenOneUpdate( rig, guest, tpi, () =>
		{
			weather = UmbrellaRules.WeatherNone;
			tpi.SpotTurn = (int)rig.Simulation.ParkTurn;
			tpi.Word224 = (int)rig.Simulation.ParkTurn;
		} );
		var replica = new WorldRng( before );
		var expected = (int)(replica.NextWord() % 3) + (int)((uint)rig.Simulation.ParkTurn >> 2);
		Assert.AreEqual( (1, expected), (tpi.Byte28C, tpi.Word290), "0x61795E: +0x290 = rand() % SlowestWeatherChangeResponse + (turn >> 2), a world draw ([EXT:tpi-staff-crt-rand])" );
		weather = UmbrellaRules.WeatherHeatWave;
		var happiness = guest.Happiness;
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.Idle ), "a heat wave sends the guest away at once" );
		Assert.AreEqual( happiness, guest.Happiness, "quietly: 0x619700(1) takes no happiness step" );
	}

	[TestMethod]
	public void TheIdleDecisionChoosesTheRide()
	{
		var (rig, _) = Rig();
		var (guest, tpi) = Native( rig );
		var seen = false;
		RunUntil( rig, () => seen |= tpi.State is ThemeParkIncGuestStates.WalkingToMajor or ThemeParkIncGuestStates.TakingQueueStep or ThemeParkIncGuestStates.StandingInQueue, 300 );
		Assert.IsTrue( seen, "state 6's decision on A & 3 of 0 or 2 runs the major decision (0x610080) and walks to the ride (state 13)" );
	}

	/// <summary>The economy's special-ride state for the test: the ride awaits its activation while <see cref="Pending"/> holds.</summary>
	private sealed class SpecialRidePayments : IGuestPayments
	{
		public bool Pending = true;
		public int AdmissionFee => 0;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
		public bool AwaitsActivation( IRideVisitorBridge attraction ) => Pending;
	}

	/// <summary>
	/// A special ride awaiting its activation (<c>0x6242F0</c> does not free it) scores 0 in the native score (<c>0x6101FD</c>), so no native guest chooses it, idle or walking, and the Theme Park World
	/// rules' HostScore refuses it from the same economy state; once activated it is chosen.
	/// </summary>
	[TestMethod]
	public void ANativeGuestNeverChoosesASpecialRideAwaitingActivation()
	{
		var (rig, _) = Rig();
		var payments = new SpecialRidePayments();
		rig.Simulation.Payments = payments;
		var (guest, tpi) = Native( rig );
		rig.Simulation.ThemeParkIncMajorDecision( guest, tpi, rig.Simulation.Settings.TpiNative! );
		Assert.AreEqual( 0, tpi.Target, "the major decision refuses the pending special ride" );
		Assert.AreEqual( 0u, rig.Simulation.HostScore( guest, rig.Ride, 100 ), "the Theme Park World rules' score refuses it too (the same economy state)" );
		var chose = false;
		RunUntil( rig, () =>
		{
			chose |= tpi.Target != 0 || tpi.State is ThemeParkIncGuestStates.WalkingToMajor or ThemeParkIncGuestStates.TakingQueueStep or ThemeParkIncGuestStates.StandingInQueue;
			return false;
		}, 300 );
		Assert.IsFalse( chose, "300 turns of idle decisions never choose it" );
		Assert.AreEqual( 0, rig.Ride.QueueLength );
		payments.Pending = false;
		rig.Simulation.ThemeParkIncMajorDecision( guest, tpi, rig.Simulation.Settings.TpiNative! );
		Assert.AreEqual( Thing, tpi.Target, "once activated it is chosen" );
	}

	[TestMethod]
	public void ARemovedRideSendsItsQueueAway()
	{
		var (rig, _) = Rig();
		rig.HoldAdmission();
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.StandingInQueue ) );
		var happiness = guest.Happiness;
		rig.Simulation.ThemeParkIncObjectRemoved( Thing );
		Assert.AreEqual( (ThemeParkIncGuestStates.Idle, 0, 0), (tpi.State, tpi.Target, rig.Ride.QueueLength), "0x60D120: the dismissal, the target cleared, idle" );
		Assert.AreEqual( MeterMath.Add( MeterMath.Add( happiness, -15 ), -5 ), guest.Happiness, "MediumHappinessChange (0x619700(0)), then SmallHappinessChange" );
	}

	[TestMethod]
	public void AQueuedGuestRoundTripsThroughTheSave()
	{
		var (rig, _) = Rig();
		rig.HoldAdmission();
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.StandingInQueue ) );
		var snapshot = rig.Simulation.CaptureSnapshot();
		Assert.AreEqual( "", string.Join( "; ", rig.Simulation.ValidateSnapshot( snapshot ).Errors ), "a guest in state 14 is restorable" );
		var block = snapshot.Guests.Single( saved => saved.Id == guest.Id ).Tpi!;
		Assert.AreEqual( (ThemeParkIncGuestStates.StandingInQueue, (int)Thing, 0), (block.State, block.Target, block.Byte21B) );
		var (copy, _) = Rig();
		copy.HoldAdmission();
		copy.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( rig.Simulation.ComputeStateHash(), copy.Simulation.ComputeStateHash(), "the restored park hashes the same" );
		for ( var turn = 0; turn < 40; turn++ )
		{
			rig.StepTurn();
			copy.StepTurn();
		}
		Assert.AreEqual( rig.Simulation.ComputeStateHash(), copy.Simulation.ComputeStateHash(), "and runs the same" );
	}

	/// <summary>
	/// A guest of the bridged boarding saved mid-ride (state 16 with Theme Park World's Using) restores and finishes the ride the same way: idle, out of the list and with the thing in its used history
	/// (TPI-GUESTS.md, "Phases after P2": phase P3b maps such a save to state 20).
	/// </summary>
	[TestMethod]
	public void AGuestMidRideRoundTripsThroughTheSave()
	{
		var (rig, _) = Rig( takesGuests: true );
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.BeingLoaded && guest.State == GuestState.Using, 2000 ), "the guest rides" );
		// through the save file's codec, with the ride script's own state (its heap holds the rider)
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		Assert.AreEqual( "", string.Join( "; ", rig.Simulation.ValidateSnapshot( snapshot ).Errors ), "a guest mid-ride in the bridged state 16 is restorable" );
		var saved = snapshot.Guests.Single( item => item.Id == guest.Id );
		Assert.AreEqual( (ThemeParkIncGuestStates.BeingLoaded, GuestState.Using), (saved.Tpi!.State, saved.State) );
		var script = ParkResumeJson.Deserialize<RideVMSnapshot>( ParkResumeJson.Serialize( rig.Script.CaptureSnapshot() ) );
		var (copy, _) = Rig( takesGuests: true );
		copy.Simulation.RestoreSnapshot( snapshot );
		script.ValidateSnapshot();
		copy.Script.RestoreSnapshot( script );
		Assert.AreEqual( rig.Simulation.ComputeStateHash(), copy.Simulation.ComputeStateHash(), "the restored park hashes the same" );
		var restored = copy.Simulation.Guests.Single( item => item.Id == guest.Id );
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.Idle, 2000 ) );
		Assert.IsTrue( RunUntil( copy, () => restored.Tpi!.State == ThemeParkIncGuestStates.Idle, 2000 ), "the restored guest finishes the ride" );
		Assert.AreEqual( rig.Simulation.ComputeStateHash(), copy.Simulation.ComputeStateHash(), "and finishes the ride the same" );
		CollectionAssert.AreEqual( new[] { (int)Thing, 0, 0, 0 }, restored.UsedHistory, "the restored guest's use pushes the thing id" );
	}
}
