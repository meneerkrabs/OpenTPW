using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The guest's decision state (6) and wander walk (7), docs/reverse/WANDER-plan.md: the state-6 order of tp.exe <c>0x004FEC90</c>
/// (<see cref="GuestDecision"/>), the wander destination <c>0x004F9490</c> (<see cref="GuestWander"/>) on synthetic cells, and the
/// activity (state 8) in the simulation and its save.
/// </summary>
[TestClass]
public class WanderTests
{
	private static Func<uint> Draws( params uint[] values )
	{
		var queue = new Queue<uint>( values );
		return () => queue.Count > 0 ? queue.Dequeue() : throw new AssertFailedException( "drew more than scripted" );
	}

	private static GuestPathGrid Row( int length )
	{
		var grid = new GuestPathGrid( 12, 12 );
		for ( var x = 0; x < length; x++ )
			grid.SetPath( x, 0, true );
		return grid;
	}

	[TestMethod]
	public void OneStepTakesTheFirstCandidateFromTheDrawnIndexAndClampsTheSubCellBytes()
	{
		// At (5, 0) the candidates in order +Y, +X, −Y, −X are [none, (6, 0), none, (4, 0)]: index 0 is empty, so +X.
		var draw = Draws( 0, 0, 200, 2 );
		Assert.AreEqual( new GuestWander.Destination( (6, 0), 72, GuestWander.MinimumByte ), GuestWander.Pick( Row( 10 ), (5, 0), draw ) );
		Assert.ThrowsException<AssertFailedException>( () => draw(), "four draws: the step count, one index, two bytes" );
		Assert.AreEqual( new GuestWander.Destination( (4, 0), GuestWander.MaximumByte, 64 ), GuestWander.Pick( Row( 10 ), (5, 0), Draws( 5, 3, 127, 64 ) ),
			"5 % 5 is one step; index 3 is −X; 127 clamps to 123" );
	}

	[TestMethod]
	public void LaterStepsNeverTurnBackWhileThereIsAnotherWay()
	{
		// Two steps: −X to (4, 0), then the draw points at +X (index 1), which is the way back, so the next candidate in order, −X again.
		Assert.AreEqual( new GuestWander.Destination( (3, 0), 10, 20 ), GuestWander.Pick( Row( 10 ), (5, 0), Draws( 1, 3, 1, 10, 20 ) ) );
	}

	[TestMethod]
	public void ADeadEndTurnsBackWithoutADrawAndALastStepOntoTheStartAddsOne()
	{
		// (0, 0) – (1, 0): every cell has one candidate, so no index is drawn. Two steps would end on the start, so a third is taken.
		var draw = Draws( 1, 50, 60 );
		Assert.AreEqual( new GuestWander.Destination( (1, 0), 50, 60 ), GuestWander.Pick( Row( 2 ), (0, 0), draw ) );
		Assert.ThrowsException<AssertFailedException>( () => draw() );
		// One step ends on (1, 0), not on the start.
		Assert.AreEqual( (1, 0), GuestWander.Pick( Row( 2 ), (0, 0), Draws( 0, 50, 60 ) )!.Value.Cell );
	}

	[TestMethod]
	public void ACrossingDrawsAnIndexForEveryStep()
	{
		var grid = new GuestPathGrid( 12, 12 );
		for ( var i = 0; i < 9; i++ )
		{
			grid.SetPath( i, 4, true );
			grid.SetPath( 4, i, true );
		}
		// From (4, 4) all four ways are open: index 0 is +Y, to (4, 5). There the candidates are +Y and −Y; the drawn index 2 (−Y) is
		// the way back and index 3 (−X) is empty, so the search wraps to index 0, +Y again.
		Assert.AreEqual( new GuestWander.Destination( (4, 6), 5, 5 ), GuestWander.Pick( grid, (4, 4), Draws( 1, 0, 2, 0, 0 ) ) );
		// From (4, 4) index 1 is +X; three steps east.
		Assert.AreEqual( (7, 4), GuestWander.Pick( grid, (4, 4), Draws( 2, 1, 0, 0, 0, 0 ) )!.Value.Cell );
	}

	[TestMethod]
	public void AnUnlinkedPathCellGivesItsOwnCentreAndAStrandedOneNone()
	{
		var grid = new GuestPathGrid( 12, 12 );
		grid.SetPath( 0, 0, true, connections: 0 );
		Assert.AreEqual( new GuestWander.Destination( (0, 0), 0x80, 0x80 ), GuestWander.Pick( grid, (0, 0), Draws( 3 ) ) );
		Assert.IsNull( GuestWander.Pick( grid, (5, 5), Draws( 3 ) ), "no path cell" );
		// Links that lead to no path cell (here a queue cell) leave no candidate: stranded (x86 oracle X19).
		grid.SetPath( 0, 0, true, connections: 0b0010 );
		grid.SetQueue( 1, 0, 1 );
		Assert.IsNull( GuestWander.Pick( grid, (0, 0), Draws( 3 ) ) );
	}

	[TestMethod]
	public void AStepFollowsTheCurrentCellsOwnLinks()
	{
		// (1, 0) links only to +X, although (0, 0) links back to it: the one candidate is (2, 0), taken without a draw.
		var grid = Row( 3 );
		grid.SetPath( 1, 0, true, connections: 0b0010 );
		Assert.AreEqual( new GuestWander.Destination( (2, 0), 7, 9 ), GuestWander.Pick( grid, (1, 0), Draws( 0, 7, 9 ) ) );
	}

	private static GuestDecision.Input Input( uint turn = 500, uint activity = 450, uint decision = 400, float happiness = 50, float illness = 0, int exit = 50, bool closed = false, uint draw = 2 ) =>
		new( turn, activity, decision, happiness, illness, exit, closed, draw );

	private static (GuestDecision.Outcome Outcome, string Calls) Decide( GuestDecision.Input input, bool wayOut = true, bool ride = true, bool wander = true )
	{
		var calls = "";
		var outcome = GuestDecision.Decide( input, () => { calls += "L"; return wayOut; }, () => { calls += "C"; return ride; }, () => { calls += "W"; return wander; } );
		return (outcome, calls);
	}

	[TestMethod]
	public void StateSixRunsItsTestsInTheOriginalOrder()
	{
		// Cheering comes first, more than 100 turns after the last activity, with a happiness byte above 80.
		Assert.AreEqual( (GuestDecision.Outcome.Cheer, ""), Decide( Input( turn: 551, activity: 450, happiness: 81, illness: 100, draw: 0 ) ) );
		Assert.AreEqual( (GuestDecision.Outcome.Vomit, ""), Decide( Input( turn: 550, activity: 450, happiness: 81, illness: 100, draw: 0 ) ), "100 turns is not more than 100" );
		Assert.AreEqual( (GuestDecision.Outcome.Cheer, ""), Decide( Input( turn: 5, activity: 450, happiness: 90 ) ), "the turn difference is unsigned" );
		Assert.AreEqual( (GuestDecision.Outcome.None, ""), Decide( Input( turn: 600, activity: 450, happiness: 80.99f ) ), "a byte of 80 does not cheer" );
		// Vomiting: an illness byte of exactly 100 and R % 3 == 0.
		Assert.AreEqual( (GuestDecision.Outcome.Vomit, ""), Decide( Input( illness: 100, draw: 3 ) ) );
		Assert.AreEqual( (GuestDecision.Outcome.None, ""), Decide( Input( illness: 99.99f, draw: 5 ) ) );
		// The leaving gate: it tries a way out; without one the tests go on.
		Assert.AreEqual( (GuestDecision.Outcome.Leave, "L"), Decide( Input( exit: 0, draw: 1 ) ) );
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "LW"), Decide( Input( exit: 0, draw: 1 ), wayOut: false ) );
		Assert.AreEqual( (GuestDecision.Outcome.Leave, "L"), Decide( Input( happiness: 0.99f, closed: false, draw: 2 ) ) );
		Assert.AreEqual( (GuestDecision.Outcome.Leave, "L"), Decide( Input( closed: true, draw: 2 ) ) );
		Assert.AreEqual( (GuestDecision.Outcome.None, ""), Decide( Input( exit: -1, draw: 2 ) ), "the counter compares for equality" );
	}

	[TestMethod]
	public void TheFinalChoiceFollowsTheDrawModuloThreeAndTheThirtyTurnStamp()
	{
		Assert.AreEqual( (GuestDecision.Outcome.GoToRide, "C"), Decide( Input( turn: 431, decision: 400, draw: 3 ) ) );
		Assert.AreEqual( (GuestDecision.Outcome.CantDecide, "C"), Decide( Input( turn: 431, decision: 400, draw: 3 ), ride: false ) );
		Assert.AreEqual( (GuestDecision.Outcome.None, ""), Decide( Input( turn: 430, decision: 400, draw: 3 ) ), "30 turns after the stamp is not yet later than it" );
		Assert.AreEqual( (GuestDecision.Outcome.None, ""), Decide( Input( turn: 10, decision: uint.MaxValue - 5, draw: 0 ) ), "the stamp plus 30 wraps, as in the original" );
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "W"), Decide( Input( draw: 4 ) ) );
		Assert.AreEqual( (GuestDecision.Outcome.WanderFailed, "W"), Decide( Input( draw: 4 ), wander: false ) );
		Assert.AreEqual( (GuestDecision.Outcome.None, ""), Decide( Input( draw: 5 ) ) );
	}

	[TestMethod]
	public void ACantDecidePauseLastsTenTurnsCostsTheSmallChangeAndStampsTheTurn()
	{
		var grid = Row( 10 );
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		var simulation = new GuestSimulation( grid, settings, 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 4, 0 );
		guest.Happiness = 60;
		long? pausedAt = null;
		var happinessBefore = guest.Happiness;
		for ( var tick = 0; tick < 300 * 60 && pausedAt == null; tick++ )
		{
			happinessBefore = guest.Happiness;
			simulation.Tick( GuestSnapshotTests.Tick );
			if ( guest.Activity == GuestActivity.CantDecide )
				pausedAt = simulation.ParkTurn;
		}
		Assert.IsNotNull( pausedAt, "a park without attractions leaves every major decision undecided" );
		Assert.IsTrue( pausedAt > GuestDecision.MajorDecisionGapTurns, "the first major decision waits for turn 31" );
		Assert.AreEqual( pausedAt, guest.QueueStandingSinceTurn, "the decision stamp +0x1FC" );
		Assert.AreEqual( pausedAt, guest.InterludeTurn, "the activity stamp +0x208" );
		Assert.AreEqual( happinessBefore - settings.SmallHappinessChange, guest.Happiness, 0.0001f );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		while ( true )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			if ( simulation.ParkTurn > pausedAt + GuestDecision.ActivityTurns )
				break;
			Assert.AreEqual( GuestActivity.CantDecide, guest.Activity, $"turn {simulation.ParkTurn}" );
		}
		Assert.AreEqual( GuestActivity.None, guest.Activity, "state 8 ends once the turn is above the stamp + 10" );
	}

	[TestMethod]
	public void WanderingGuestsWalkSeveralCellsAndDecideBetweenWalks()
	{
		var grid = Row( 12 );
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		settings.SmallHappinessChange = 0;   // a park without attractions would otherwise empty the guest's happiness and send it home
		var simulation = new GuestSimulation( grid, settings, 11 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 6, 0 );
		var walks = new List<int>();
		var from = guest.Cell;
		var previous = guest.State;
		for ( var tick = 0; tick < 200 * 60; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			if ( previous == GuestState.DoingNothing && guest.State == GuestState.WalkingAround )
				from = ((int)guest.X, (int)guest.Y);
			if ( previous == GuestState.WalkingAround && guest.State == GuestState.DoingNothing )
				walks.Add( Math.Abs( guest.Cell.X - from.X ) );
			previous = guest.State;
			Assert.IsTrue( Math.Abs( guest.Cell.X - guest.Navigation.Cell.X ) <= 1, $"the guest's cell {guest.Cell} follows its walk ({guest.Navigation.Cell})" );
			Assert.IsTrue( guest.State is GuestState.DoingNothing or GuestState.WalkingAround, $"{guest.State}" );
		}
		Assert.IsTrue( walks.Count > 5, $"{walks.Count} walks" );
		Assert.IsTrue( walks.Max() > 1, "a walk covers up to five cells, not one" );
	}

	[TestMethod]
	public void AWalkThatLosesARouteCellEndsWhereTheGuestStands()
	{
		var grid = Row( 12 );
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		settings.SmallHappinessChange = 0;   // a park without attractions would otherwise empty the guest's happiness and send it home
		var simulation = new GuestSimulation( grid, settings, 11 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 6, 0 );
		for ( var tick = 0; tick < 200 * 60 && !(guest.State == GuestState.WalkingAround && guest.Navigation.Route.Length >= 3); tick++ )
			simulation.Tick( GuestSnapshotTests.Tick );
		Assert.IsTrue( guest.HasWaypoint && guest.Navigation.Route.Length >= 3, "a walk of at least two cells" );
		var destination = guest.Navigation.Route[^1];
		grid.SetPath( destination.X, destination.Y, false );
		simulation.Tick( GuestSnapshotTests.Tick );
		Assert.IsFalse( guest.HasWaypoint, "the walk ended" );
		Assert.IsTrue( grid.IsWalkable( guest.Cell.X, guest.Cell.Y ), $"the guest stays on the path at {guest.Cell}" );
		for ( var tick = 0; tick < 60 * 60; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			Assert.IsTrue( guest.State is GuestState.DoingNothing or GuestState.WalkingAround );
		}
	}

	[TestMethod]
	public void AParkWithoutArrivalLanesStillLetsItsGuestsGo()
	{
		var settings = GuestTests.CreateSettings();
		settings.ArrivalLaneA = null;
		settings.ArrivalLaneB = null;
		var simulation = new GuestSimulation( Row( 6 ), settings, 2 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 2, 0 );
		guest.Happiness = 0.5f;
		for ( var tick = 0; tick < 10 * 60 && simulation.Find( guest.Id ) != null; tick++ )
			simulation.Tick( GuestSnapshotTests.Tick );
		Assert.IsNull( simulation.Find( guest.Id ), "with no gate modelled the leaving guest vanishes, as before" );
	}

	[TestMethod]
	public void TheActivityIsSavedAndASchemaTwoSaveRestoresWithoutOne()
	{
		var park = new GuestSnapshotTests.Rig();
		GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.Guests.Any( guest => guest.Activity != GuestActivity.None ), 0, 40_000 );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.AreEqual( 3, snapshot.SchemaVersion );
		Assert.IsTrue( snapshot.Guests.Any( guest => guest.Activity != GuestActivity.None ) );
		Assert.IsTrue( snapshot.Guests.All( guest => guest.DecisionTimer == null ), "schema 3 writes no decision timer" );
		var loaded = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) );
		var resumed = new GuestSnapshotTests.Rig();
		resumed.Simulation.RestoreSnapshot( loaded );
		CollectionAssert.AreEqual( park.Simulation.Guests.Select( guest => guest.Activity ).ToList(), resumed.Simulation.Guests.Select( guest => guest.Activity ).ToList() );
		Assert.AreEqual( park.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash() );

		// A schema-2 save carries the old decision timer and no activity: it restores, the timer is dropped.
		var schemaTwo = snapshot with
		{
			SchemaVersion = 2,
			Guests = snapshot.Guests.Select( guest => guest with { DecisionTimer = 1.25f, Activity = GuestActivity.None } ).ToArray()
		};
		var fromTwo = new GuestSnapshotTests.Rig();
		fromTwo.Simulation.RestoreSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( schemaTwo ) ) );
		Assert.IsTrue( fromTwo.Simulation.Guests.All( guest => guest.Activity == GuestActivity.None ) );

		// An activity outside state 6 or an unknown one is rejected.
		var walking = snapshot.Guests.First( guest => guest.State != GuestState.DoingNothing );
		var outside = snapshot with { Guests = snapshot.Guests.Select( guest => guest.Id == walking.Id ? guest with { Activity = GuestActivity.Cheering } : guest ).ToArray() };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => new GuestSnapshotTests.Rig().Simulation.RestoreSnapshot( outside ) ).Message, "outside state 6" );
		var unknown = snapshot with { Guests = snapshot.Guests.Select( guest => guest with { Activity = (GuestActivity)3 } ).ToArray() };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => new GuestSnapshotTests.Rig().Simulation.RestoreSnapshot( unknown ) ).Message, "not a state-6 activity" );
	}
}
