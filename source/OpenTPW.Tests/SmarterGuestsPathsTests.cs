using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Phase 6 of the smarter-guests plan (docs/SMARTER-GUESTS.md): clean and quiet paths. The wander goal is weighted by the litter and the crowd on the cells of the original's own walk, and a guest standing in
/// litter or in a crowd complains with a small, rate-limited happiness cost. The measured effect is in the page's tables; these tests pin the pure rule, what the host hands to it and does with its verdict, the
/// original's walk and draws (unchanged), and the complaint times (kept, hashed, saved, restored, validated).
/// </summary>
[TestClass]
[DoNotParallelize]
public class SmarterGuestsPathsTests
{
	private static readonly GuestSettings Settings = GuestTests.CreateSettings();

	private static GuestView Guest( ulong birth = 7, long complaint = -1 ) =>
		new( 8, birth, 0, 60, 10, 10, 10, 0, 500, 3, 3, Array.AsReadOnly( new int[4] ), Array.AsReadOnly( new int[4] ), -1, -1, complaint );

	private static DecisionContext Context( long turn = 1000 ) => new( Settings, turn, false, 3 );

	private static PathCellView Cell( int litter = 0, int crowd = 1, int x = 0 ) => new( x, 1, litter, crowd );

	private static PathDecision Decide( PathCellView here, PathCellView[] walk, GuestView? guest = null, long turn = 1000, bool litter = true, bool crowd = true ) =>
		SmarterPathComfort.Decide( Context( turn ), guest ?? Guest(), new WanderPathView( here, walk ), litter, crowd );

	// ---- The constants and the pure rule ---------------------------------------------------------

	[TestMethod]
	public void TheConstantsAreDerivedFromTheOriginalsNumbersAndFromEachOther()
	{
		Assert.AreEqual( 2, SmarterPathComfort.LitterComplaintUnits, "0.2 of nine cells is 1.8" );
		Assert.IsTrue( GuestThoughts.MessyShare * 9 > 1 && GuestThoughts.MessyShare * 9 <= SmarterPathComfort.LitterComplaintUnits );
		Assert.AreEqual( RideVisitorBridge.PositionsPerCell + 1, SmarterPathComfort.CrowdComplaintGuests, "one more than a queue cell holds" );
		// a cell at either complaint level costs the same in the weighting, and the switch margin is that cost
		Assert.AreEqual( SmarterPathComfort.LitterComplaintUnits * SmarterPathComfort.LitterWeight, SmarterPathComfort.CrowdComplaintGuests * SmarterPathComfort.CrowdWeight );
		Assert.AreEqual( SmarterPathComfort.CrowdWeight, SmarterPathComfort.SwitchMargin, "one more guest in the goal's own cell" );
		Assert.IsTrue( SmarterPathComfort.NearWeight < SmarterPathComfort.CrowdWeight, "a guest beside a cell weighs less than one in it" );
		Assert.AreEqual( 0.1f, SmarterPathComfort.HappinessCost( Settings ), "a fiftieth of the shipped small step of 5" );
		Assert.AreEqual( 0f, SmarterPathComfort.HappinessCost( new GuestSettings { SmallHappinessChange = -3 } ), "a negative step is not turned into a gain" );
		Assert.AreEqual( 480, SmarterPathComfort.ComplaintCooldownTurns, "a whole visit (the exit counter)" );
	}

	[TestMethod]
	public void LitterIsCountedPerKindVomitAndStinkbombsDouble()
	{
		Assert.AreEqual( 0, SmarterPathComfort.LitterUnitsOf( LitterType.None ) );
		foreach ( var kind in new[] { (LitterType)1, (LitterType)5, LitterType.Dodgy } )
			Assert.AreEqual( 1, SmarterPathComfort.LitterUnitsOf( kind ) );
		Assert.AreEqual( 2, SmarterPathComfort.LitterUnitsOf( LitterType.Vomit ) );
		Assert.AreEqual( 2, SmarterPathComfort.LitterUnitsOf( LitterType.Stinkbomb ) );
	}

	[TestMethod]
	public void ACleanQuietWalkKeepsTheGoalAndComplainsOfNothing()
	{
		Assert.AreEqual( PathDecision.Keep, Decide( Cell(), [Cell( x: 1 ), Cell( x: 2 ), Cell( x: 3 )] ) );
		Assert.AreEqual( PathDecision.Keep, Decide( Cell( crowd: 4, litter: 1 ), [Cell( crowd: 4, litter: 1 )] ), "just under both complaint levels, and nothing to move to" );
	}

	[TestMethod]
	public void TheGoalMovesToTheCheapestCellOnlyWhenItIsAComplaintsWorthCheaper()
	{
		// the goal is the walk's last cell; two littered units around it cost 10, a clean cell 0
		var dirty = Cell( litter: 2, x: 3 );
		Assert.AreEqual( 1, Decide( Cell(), [Cell( x: 1, litter: 1 ), Cell( x: 2 ), dirty] ).GoalIndex, "the cheapest cell wins" );
		Assert.AreEqual( 1, Decide( Cell(), [Cell( x: 1 ), Cell( x: 2 ), dirty] ).GoalIndex, "ties between cheaper cells go to the later one, nearer the goal" );
		Assert.AreEqual( -1, Decide( Cell(), [Cell( x: 1 ), Cell( x: 3 ) with { NearCrowd = 1 }] ).GoalIndex, "one guest beside the goal (1) is less than the margin (2): the goal stays" );
		Assert.AreEqual( 0, Decide( Cell(), [Cell( x: 1 ), Cell( x: 3 ) with { NearCrowd = 2 }] ).GoalIndex, "two beside it reach the margin: the goal moves" );
		Assert.AreEqual( 0, Decide( Cell(), [Cell( x: 1 ), Cell( x: 3, crowd: 2 )] ).GoalIndex, "one more guest in the goal cell reaches the margin" );
		Assert.AreEqual( 0, Decide( Cell(), [Cell( x: 1, crowd: 0 ), Cell( crowd: 6, x: 3 )] ).GoalIndex, "a crowd of six costs 12 against 0" );
		Assert.AreEqual( -1, Decide( Cell(), [Cell( crowd: 6 ), Cell( crowd: 6, x: 3 )] ).GoalIndex, "an equally bad walk stays as it is" );
		Assert.AreEqual( -1, Decide( Cell(), [dirty] ).GoalIndex, "a walk of one cell has nowhere else to go" );
		Assert.AreEqual( -1, Decide( Cell(), [] ).GoalIndex );
	}

	[TestMethod]
	public void EachRuleCountsOnlyItsOwnKindOfCost()
	{
		var walk = new[] { Cell( x: 1, crowd: 0 ), Cell( litter: 4, crowd: 6, x: 3 ) };
		Assert.AreEqual( 0, Decide( Cell(), walk ).GoalIndex );
		Assert.AreEqual( 0, Decide( Cell(), walk, litter: true, crowd: false ).GoalIndex );
		Assert.AreEqual( 0, Decide( Cell(), walk, litter: false, crowd: true ).GoalIndex );
		Assert.AreEqual( -1, Decide( Cell(), walk, litter: false, crowd: false ).GoalIndex );
		var here = Cell( litter: 3, crowd: 6 );
		Assert.AreEqual( PathComplaint.Litter, Decide( here, walk ).Complaint, "litter first" );
		Assert.AreEqual( PathComplaint.Crowd, Decide( here, walk, litter: false ).Complaint );
		Assert.AreEqual( PathComplaint.Litter, Decide( here, walk, crowd: false ).Complaint );
		Assert.AreEqual( PathComplaint.None, Decide( here, walk, litter: false, crowd: false ).Complaint );
	}

	[TestMethod]
	public void AComplaintCarriesTheOriginalsThoughtASmallCostAndWaitsOutItsCooldown()
	{
		var littered = Decide( Cell( litter: 2 ), [Cell()] );
		Assert.AreEqual( new PathDecision( -1, PathComplaint.Litter, GuestThought.TooMessy, 0.1f ), littered, "the original's own 'Too messy'" );
		var crowded = Decide( Cell( crowd: 5 ), [Cell()] );
		Assert.AreEqual( new PathDecision( -1, PathComplaint.Crowd, GuestThought.Dissatisfied, 0.1f ), crowded );
		Assert.AreEqual( PathComplaint.None, Decide( Cell( litter: 1, crowd: 4 ), [Cell()] ).Complaint, "one unit and four guests are under the levels" );
		var turn = 1000L;
		var last = turn - SmarterPathComfort.ComplaintCooldownTurns;
		Assert.AreEqual( PathComplaint.Litter, Decide( Cell( litter: 2 ), [Cell()], Guest( complaint: last ), turn ).Complaint, "the cooldown has just passed" );
		Assert.AreEqual( PathComplaint.None, Decide( Cell( litter: 2 ), [Cell()], Guest( complaint: last + 1 ), turn ).Complaint, "one turn short" );
		Assert.AreEqual( PathComplaint.None, Decide( Cell( litter: 2 ), [Cell()], Guest( complaint: turn ), turn ).Complaint );
		Assert.AreEqual( PathComplaint.Litter, Decide( Cell( litter: 2 ), [Cell()], Guest( complaint: -1 ) ).Complaint, "no earlier complaint" );
		Assert.AreEqual( PathComplaint.None, Decide( Cell( litter: 9, crowd: 9 ), [Cell()], Guest( birth: 0 ) ).Complaint, "a guest without a birth id cannot have its cooldown kept, so it does not complain" );
		// the goal move does not wait for the cooldown
		Assert.AreEqual( 0, Decide( Cell( litter: 2 ), [Cell(), Cell( litter: 3, x: 2 )], Guest( complaint: turn ), turn ).GoalIndex );
	}

	// ---- The original's walk, unchanged ----------------------------------------------------------

	private static Func<uint> Draws( params uint[] words )
	{
		var index = 0;
		return () => words[index++ % words.Length];
	}

	[TestMethod]
	public void ThePickReportsTheCellsOfTheWalkAndTakesTheSameDrawsAndGivesTheSameGoal()
	{
		var grid = new ParkScenario( new WorldSeed( 1 ) ).Grid;
		var random = new WorldRng( 99 );
		for ( var attempt = 0; attempt < 200; attempt++ )
		{
			var start = (X: 2 + attempt % 30, Y: ParkScenario.StreetRow);
			var seed = (uint)attempt * 2654435761u;
			var plainCount = 0;
			var plain = GuestWander.Pick( grid, start, () => { plainCount++; return unchecked( seed = seed * 1664525u + 1013904223u ) >> 3; } );
			seed = (uint)attempt * 2654435761u;
			var trailCount = 0;
			Span<(int X, int Y)> trail = stackalloc (int X, int Y)[GuestWander.TrailCapacity];
			var count = 0;
			var withTrail = GuestWander.Pick( grid, start, () => { count++; return unchecked( seed = seed * 1664525u + 1013904223u ) >> 3; }, null, out _, trail, out trailCount );
			Assert.AreEqual( plain, withTrail, $"start {start}: the same goal" );
			Assert.AreEqual( plainCount, count, $"start {start}: the same number of draws" );
			Assert.IsNotNull( plain );
			Assert.IsTrue( trailCount >= 1 && trailCount <= GuestWander.TrailCapacity );
			Assert.AreEqual( plain!.Value.Cell, trail[trailCount - 1], "the goal is the walk's last cell" );
			var previous = start;
			for ( var index = 0; index < trailCount; index++ )
			{
				Assert.AreEqual( 1, Math.Abs( trail[index].X - previous.X ) + Math.Abs( trail[index].Y - previous.Y ), "each step is to a neighbouring cell" );
				previous = trail[index];
			}
		}
		_ = random;
	}

	[TestMethod]
	public void ALongWalkReportsItsLastCellsAndAShortTrailBufferStillEndsAtTheGoal()
	{
		var grid = new ParkScenario( new WorldSeed( 1 ) ).Grid;
		var start = (X: 10, Y: ParkScenario.StreetRow);
		// the step count is the first draw % 5 (up to five steps); a buffer of two keeps the last two of them
		Span<(int X, int Y)> all = stackalloc (int X, int Y)[GuestWander.TrailCapacity];
		var plain = GuestWander.Pick( grid, start, Draws( 4, 1, 1, 1, 1, 1, 0x40, 0x40 ), null, out _, all, out var allCount );
		Assert.AreEqual( 5, allCount, "a draw of 4 walks five steps" );
		Span<(int X, int Y)> two = stackalloc (int X, int Y)[2];
		var again = GuestWander.Pick( grid, start, Draws( 4, 1, 1, 1, 1, 1, 0x40, 0x40 ), null, out _, two, out var twoCount );
		Assert.AreEqual( plain, again );
		Assert.AreEqual( 2, twoCount );
		Assert.AreEqual( all[3], two[0] );
		Assert.AreEqual( all[4], two[1] );
		Assert.AreEqual( plain!.Value.Cell, two[1] );
	}

	// ---- The host --------------------------------------------------------------------------------

	[TestMethod]
	public void OnlyThePolicyThatAsksGetsThePathViewsAndTheShippedSetAsksForThem()
	{
		Assert.IsFalse( ((IGuestDecisions)OriginalGuestDecisions.Instance).UsesPathComfort );
		Assert.IsFalse( new SmarterGuestDecisions( SmarterRuleSet.Phase5 ).UsesPathComfort );
		Assert.IsFalse( new SmarterGuestDecisions( SmarterRuleSet.None ).UsesPathComfort );
		Assert.IsTrue( new SmarterGuestDecisions().UsesPathComfort );
		Assert.IsTrue( new SmarterGuestDecisions( SmarterRuleSet.Litter ).UsesPathComfort );
		Assert.IsTrue( new SmarterGuestDecisions( SmarterRuleSet.Crowding ).UsesPathComfort );
		Assert.AreEqual( SmarterRuleSet.Litter | SmarterRuleSet.Crowding, SmarterRuleSet.Paths );
		Assert.AreEqual( SmarterRuleSet.Phase5 | SmarterRuleSet.Paths, SmarterRuleSet.Phase6 );
		Assert.AreEqual( SmarterRuleSet.Phase6 | SmarterRuleSet.Bins, SmarterRuleSet.All );
		Assert.AreEqual( PathDecision.Keep, ((IGuestDecisions)OriginalGuestDecisions.Instance).ChoosePath( Context(), Guest(), new WanderPathView( Cell(), [Cell()] ) ) );
	}

	/// <summary>Asks for the path views and answers with a fixed verdict; records what the host handed over.</summary>
	private sealed class PathSpy( Func<WanderPathView, GuestView, PathDecision> answer ) : SmarterGuestsTests.DelegatingDecisions( new SmarterGuestDecisions( SmarterRuleSet.Phase5 ) )
	{
		public readonly List<(PathCellView Here, PathCellView[] Walk, long ComplaintTurn)> Seen = [];
		public readonly List<GuestWander.Destination> Proposals = [];
		public Action<GuestView, GuestWander.Destination>? OnGoal;
		public override bool UsesPathComfort => true;
		public override PathDecision ChoosePath( in DecisionContext context, in GuestView guest, in WanderPathView path )
		{
			Seen.Add( (path.Here, path.Walk.ToArray(), guest.ComplaintTurn) );
			return answer( path, guest );
		}
		public override GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal )
		{
			Proposals.Add( proposal );
			OnGoal?.Invoke( guest, proposal );
			return base.ChooseWanderGoal( context, guest, proposal );
		}
	}

	private static ParkScenario Run( IGuestDecisions? decisions, long ticks = 9000, ParkScenarioOptions? options = null )
	{
		var park = new ParkScenario( new WorldSeed( 1 ), options, decisions );
		park.Run( ticks );
		return park;
	}

	[TestMethod]
	public void APolicyThatKeepsEveryGoalAndComplainsOfNothingLeavesTheRunAsItWasWithNoExtraDraw()
	{
		var plain = Run( new SmarterGuestDecisions( SmarterRuleSet.Phase5 ) );
		var spy = new PathSpy( ( _, _ ) => PathDecision.Keep );
		var spied = Run( spy );
		Assert.IsTrue( spy.Seen.Count > 50, $"{spy.Seen.Count} wanders were offered" );
		Assert.AreEqual( plain.Hash, spied.Hash, "the guests" );
		Assert.AreEqual( plain.Guests.RandomState, spied.Guests.RandomState, "the draws, in the same order" );
		Assert.AreEqual( WorldStateHash.Compute( new WorldStateSources { Seed = plain.Seed, Guests = plain.Guests } ), WorldStateHash.Compute( new WorldStateSources { Seed = spied.Seed, Guests = spied.Guests } ), "and the extension's state" );
	}

	[TestMethod]
	public void TheHostHandsOverTheWalkWithTheGuestsOwnCellAndTheGoalLast()
	{
		var spy = new PathSpy( ( _, _ ) => PathDecision.Keep );
		var park = Run( spy );
		Assert.IsTrue( spy.Seen.All( item => item.Walk.Length is >= 1 and <= GuestWander.TrailCapacity + 4 ) );
		Assert.AreEqual( spy.Seen.Count, spy.Proposals.Count, "one proposal for every offered walk" );
		for ( var index = 0; index < spy.Seen.Count; index++ )
			Assert.AreEqual( spy.Proposals[index].Cell, (spy.Seen[index].Walk[^1].X, spy.Seen[index].Walk[^1].Y), "the original's goal is the walk's last cell" );
		Assert.IsTrue( spy.Seen.All( item => item.ComplaintTurn == -1 ), "nobody has complained" );
		Assert.IsTrue( spy.Seen.Any( item => item.Walk.Any( cell => cell.Crowd > 1 ) ) || spy.Seen.Any( item => item.Here.Crowd > 1 ), "guests share cells in this run" );
		Assert.IsTrue( spy.Seen.All( item => item.Walk.All( cell => cell.LitterUnits == 0 ) ), "no litter map in the default scenario: no cell is read" );
		_ = park;
	}

	[TestMethod]
	public void TheCrowdOfACellIsTheGuestsStandingInIt()
	{
		var checkedCells = 0;
		ParkScenario? scenario = null;
		var spy = new PathSpy( ( path, guest ) =>
		{
			foreach ( var cell in path.Walk.ToArray().Append( path.Here ) )
			{
				var live = scenario!.Guests.Guests.Count( other => other.Id != guest.Id && other.State != GuestState.Gone && other.CellX == cell.X && other.CellY == cell.Y );
				Assert.AreEqual( live, cell.Crowd, $"cell ({cell.X}, {cell.Y})" );
				var around = scenario.Guests.Guests.Count( other => other.Id != guest.Id && other.State != GuestState.Gone && Math.Abs( other.Navigation.Cell.X - cell.X ) <= 1 && Math.Abs( other.Navigation.Cell.Y - cell.Y ) <= 1 ) - scenario.Guests.Guests.Count( other => other.Id != guest.Id && other.State != GuestState.Gone && other.Navigation.Cell == (cell.X, cell.Y) );
				Assert.AreEqual( around, cell.NearCrowd, $"the eight cells around ({cell.X}, {cell.Y})" );
				checkedCells++;
			}
			return PathDecision.Keep;
		} );
		scenario = new ParkScenario( new WorldSeed( 1 ), decisions: spy );
		scenario.Run( 9000 );
		Assert.IsTrue( checkedCells > 100 );
	}

	[TestMethod]
	public void TheCandidatesAreTheWalkTheLinkedCellsBesideItsGoalAndTheGoalLastAllPathCells()
	{
		var spy = new PathSpy( ( _, _ ) => PathDecision.Keep );
		var park = Run( spy );
		var beside = 0;
		for ( var index = 0; index < spy.Seen.Count; index++ )
		{
			var walk = spy.Seen[index].Walk;
			Assert.IsTrue( walk.All( cell => park.Grid.IsWalkable( cell.X, cell.Y ) ), "every candidate is a path cell" );
			var goal = walk[^1];
			beside += walk.Count( cell => Math.Abs( cell.X - goal.X ) + Math.Abs( cell.Y - goal.Y ) == 1 );
			Assert.IsTrue( walk.Length <= GuestWander.TrailCapacity + 4 );
		}
		Assert.IsTrue( beside > 20, $"{beside} candidates beside a goal" );
	}

	[TestMethod]
	public void ALitterMapIsReadAroundTheCellsAndCountedByKind()
	{
		var options = new ParkScenarioOptions { Litter = true, FoodShop = true, ArrivalTimeBetween = 10, HandymanTurns = 100_000, LitterEffect = 25 };
		var seenUnits = new List<int>();
		ParkScenario? scenario = null;
		var spy = new PathSpy( ( path, guest ) =>
		{
			var litter = scenario!.Guests.Litter!;
			foreach ( var cell in path.Walk.ToArray().Append( path.Here ) )
			{
				var expected = 0;
				for ( var y = cell.Y - 1; y <= cell.Y + 1; y++ )
					for ( var x = cell.X - 1; x <= cell.X + 1; x++ )
						expected += litter.TypeAt( x, y ) switch { LitterType.None => 0, LitterType.Vomit or LitterType.Stinkbomb => 2, _ => 1 };
				Assert.AreEqual( expected, cell.LitterUnits, $"cell ({cell.X}, {cell.Y})" );
				seenUnits.Add( cell.LitterUnits );
			}
			return PathDecision.Keep;
		} );
		scenario = new ParkScenario( new WorldSeed( 1 ), options, spy );
		scenario.Run( GuestReport.TicksForDays( 80 ) );
		Assert.IsTrue( scenario.Guests.Litter!.Count > 3, "guests vomit in the food-shop scenario" );
		Assert.IsTrue( seenUnits.Any( units => units >= SmarterPathComfort.LitterComplaintUnits ), "a walk went past enough litter to complain of" );
	}

	[TestMethod]
	public void AComplaintCostsHappinessThinksTheThoughtNotesTheTurnAndPassesTheCooldownBack()
	{
		var seenComplaints = new List<long>();
		ParkScenario? scenario = null;
		var happinessBefore = new Dictionary<int, float>();
		var spy = new PathSpy( ( path, guest ) =>
		{
			seenComplaints.Add( guest.ComplaintTurn );
			happinessBefore[guest.Id] = guest.Happiness;
			return new PathDecision( -1, PathComplaint.Crowd, GuestThought.Dissatisfied, 3 );
		} );
		scenario = new ParkScenario( new WorldSeed( 1 ), decisions: spy );
		var counts = new Dictionary<PathComplaint, int>();
		scenario.Guests.Observer = new ComplaintCounter( counts );
		scenario.Run( 6000 );
		Assert.IsTrue( counts.GetValueOrDefault( PathComplaint.Crowd ) > 20, $"{counts.GetValueOrDefault( PathComplaint.Crowd )} complaints" );
		var smarter = scenario.Guests.SmarterGuests!;
		Assert.IsTrue( smarter.Complaints.Count > 5, "turns are noted by birth id" );
		Assert.IsTrue( smarter.Complaints.Entries.All( entry => entry.Turn >= 0 && entry.Turn <= scenario.Guests.ParkTurn ) );
		Assert.IsTrue( seenComplaints.Any( turn => turn >= 0 ), "a guest that complained is shown its last complaint on its next wander" );
		Assert.IsTrue( scenario.Guests.Guests.Any( guest => guest.Thought == GuestThought.Dissatisfied ) );
	}

	private sealed class ComplaintCounter( Dictionary<PathComplaint, int> counts ) : IGuestObserver
	{
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) { }
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
		public void PathComplained( Guest guest, PathComplaint complaint ) => counts[complaint] = counts.GetValueOrDefault( complaint ) + 1;
	}

	/// <summary>A guest of a running scenario on a path cell with a linked path cell beside it, and that neighbour.</summary>
	private static (Guest Guest, (int X, int Y) Beside) GuestOnAStreet( ParkScenario park )
	{
		foreach ( var guest in park.Guests.Guests )
		{
			if ( !park.Grid.IsWalkable( guest.CellX, guest.CellY ) )
				continue;
			foreach ( var next in new[] { (guest.CellX + 1, guest.CellY), (guest.CellX - 1, guest.CellY) } )
				if ( park.Grid.IsWalkable( next.Item1, next.Item2 ) && park.Grid.AreConnected( guest.CellX, guest.CellY, GuestPathGrid.DirectionBetween( guest.CellX, guest.CellY, next.Item1, next.Item2 ) ) )
					return (guest, next);
		}
		throw new AssertFailedException( "no guest on a street" );
	}

	[TestMethod]
	public void AGoalMoveNamesACandidateAndKeepsTheOriginalsSubCellBytesAndTheOriginalGoalWhenKept()
	{
		var spy = new PathSpy( ( path, guest ) => path.Walk.Length > 1 ? new PathDecision( 0 ) : PathDecision.Keep );
		var park = Run( spy, 3000 );
		var (guest, beside) = GuestOnAStreet( park );
		var far = (X: guest.CellX < 20 ? 30 : 5, Y: guest.CellY);
		var proposal = new GuestWander.Destination( far, 77, 99 );
		var moved = park.Guests.WeighPath( guest, proposal, [beside, far] );
		Assert.AreEqual( beside, moved.Cell, "the candidate the policy named" );
		Assert.AreEqual( (77, 99), (moved.ByteX, moved.ByteY), "the original's sub-cell bytes" );
		var kept = new ParkScenario( new WorldSeed( 1 ), decisions: new PathSpy( ( _, _ ) => PathDecision.Keep ) );
		kept.Run( 3000 );
		var (other, otherBeside) = GuestOnAStreet( kept );
		Assert.AreEqual( proposal with { }, kept.Guests.WeighPath( other, proposal, [otherBeside, far] ), "a kept goal is the proposal itself" );
	}

	[TestMethod]
	public void NoCandidateAndNoMoveIsEverTheGuestsOwnCellAndOnlyPathCellsAreOffered()
	{
		var spy = new PathSpy( ( path, guest ) => path.Walk.Length > 1 ? new PathDecision( 0 ) : PathDecision.Keep );
		var park = Run( spy, 3000 );
		var (guest, beside) = GuestOnAStreet( park );
		var start = (X: guest.CellX, Y: guest.CellY);
		// a walk that passes back through the start cell, with the goal next to it: neither the start nor a non-path cell is offered
		var offGrid = (X: 0, Y: 11);
		Assert.IsFalse( park.Grid.IsWalkable( offGrid.X, offGrid.Y ) );
		spy.Seen.Clear();
		_ = park.Guests.WeighPath( guest, new GuestWander.Destination( beside, 64, 64 ), [start, offGrid, beside, start, beside] );
		var walk = spy.Seen.Single().Walk;
		Assert.IsTrue( walk.SkipLast( 1 ).All( cell => (cell.X, cell.Y) != start && park.Grid.IsWalkable( cell.X, cell.Y ) ), "no start cell and no off-path cell among the candidates" );
		Assert.AreEqual( beside, (walk[^1].X, walk[^1].Y), "the goal stays last" );
		// over a whole run no moved goal is the start cell
		var starts = new List<((int X, int Y) Start, (int X, int Y) Goal)>();
		var recording = new PathSpy( ( path, g ) => path.Walk.Length > 1 ? new PathDecision( 0 ) : PathDecision.Keep );
		recording.OnGoal = ( g, proposal ) => starts.Add( ((g.CellX, g.CellY), proposal.Cell) );
		var run = Run( recording, 9000 );
		var movedGoals = 0;
		for ( var index = 0; index < recording.Seen.Count; index++ )
		{
			var seen = recording.Seen[index];
			if ( seen.Walk.Length > 1 )
			{
				movedGoals++;
				Assert.AreNotEqual( (seen.Here.X, seen.Here.Y), starts[index].Goal, "a moved goal is never the cell the guest stands on" );
			}
		}
		Assert.IsTrue( movedGoals > 50, $"{movedGoals} moves" );
		_ = run;
	}

	[TestMethod]
	public void AVerdictTheHostCannotActOnIsRefusedNamingThePolicy()
	{
		void Refused( Func<PathDecision> verdict, string text )
		{
			var spy = new PathSpy( ( _, _ ) => verdict() );
			var thrown = Assert.ThrowsException<InvalidOperationException>( () => Run( spy, 9000 ) );
			StringAssert.Contains( thrown.Message, nameof( PathSpy ) );
			StringAssert.Contains( thrown.Message, text );
		}
		Refused( () => new PathDecision( 99 ), "walk cell 99" );
		Refused( () => new PathDecision( -2 ), "walk cell -2" );
		Refused( () => new PathDecision( -1, PathComplaint.Litter, GuestThought.None, 1 ), "complaint" );
		Refused( () => new PathDecision( -1, PathComplaint.Litter, GuestThought.TooMessy, -1 ), "happiness cost -1" );
		Refused( () => new PathDecision( -1, PathComplaint.Litter, GuestThought.TooMessy, 1000 ), "happiness cost 1000" );
		Refused( () => new PathDecision( -1, PathComplaint.Litter, GuestThought.TooMessy, float.NaN ), "happiness cost NaN" );
		Refused( () => new PathDecision( -1, PathComplaint.None, GuestThought.TooMessy, 0 ), "without a complaint" );
		Refused( () => new PathDecision( -1, (PathComplaint)9, GuestThought.TooMessy, 1 ), "complaint" );
	}

	[TestMethod]
	public void TheShippedRulesComplainAndMoveGoalsInACrowdedLitteredParkAndReplayTheSame()
	{
		var options = new ParkScenarioOptions { Litter = true, FoodShop = true, ArrivalTimeBetween = 10, HandymanTurns = 400, LitterEffect = 25 };
		ulong Run( SmarterRuleSet rules, out Dictionary<PathComplaint, int> counts, out int moved )
		{
			var park = new ParkScenario( new WorldSeed( 3 ), options, new SmarterGuestDecisions( rules ) );
			var counter = new ComplaintCounterWithMoves();
			park.Guests.Observer = counter;
			park.Run( GuestReport.TicksForDays( 90 ) );
			counts = counter.Counts;
			moved = counter.Moved;
			return park.Hash;
		}
		var all = Run( SmarterRuleSet.All, out var counts, out var moved );
		Assert.IsTrue( counts.GetValueOrDefault( PathComplaint.Litter ) > 0, "a guest complained of litter" );
		Assert.IsTrue( counts.GetValueOrDefault( PathComplaint.Crowd ) > 0, "and of a crowd" );
		Assert.IsTrue( moved > 0, "and a goal moved" );
		Assert.AreEqual( all, Run( SmarterRuleSet.All, out _, out _ ), "replay" );
		var phase5 = Run( SmarterRuleSet.Phase5, out var quiet, out var quietMoved );
		Assert.AreEqual( 0, quiet.Values.Sum() + quietMoved, "no complaint and no move without the path rules" );
		Assert.AreNotEqual( phase5, all );
		var litterOnly = Run( SmarterRuleSet.Litter, out var litterCounts, out _ );
		Assert.AreEqual( 0, litterCounts.GetValueOrDefault( PathComplaint.Crowd ) );
		Assert.IsTrue( litterCounts.GetValueOrDefault( PathComplaint.Litter ) > 0 );
		Run( SmarterRuleSet.Crowding, out var crowdCounts, out _ );
		Assert.AreEqual( 0, crowdCounts.GetValueOrDefault( PathComplaint.Litter ) );
		Assert.IsTrue( crowdCounts.GetValueOrDefault( PathComplaint.Crowd ) > 0 );
		_ = litterOnly;
	}

	private sealed class ComplaintCounterWithMoves : IGuestObserver
	{
		public readonly Dictionary<PathComplaint, int> Counts = new();
		public int Moved;
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) { }
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
		public void PathComplained( Guest guest, PathComplaint complaint ) => Counts[complaint] = Counts.GetValueOrDefault( complaint ) + 1;
		public void WanderGoalMoved( Guest guest ) => Moved++;
	}

	[TestMethod]
	public void TheScenarioWithoutTheLitterOptionIsTheBaselinesAndWithItGuestsLitterTheParkAndAHandymanSweepsIt()
	{
		var plain = new ParkScenario( new WorldSeed( 2 ) );
		Assert.IsNull( plain.Guests.Litter );
		var littered = new ParkScenario( new WorldSeed( 2 ), new ParkScenarioOptions { Litter = true, FoodShop = true, LitterEffect = 40, HandymanTurns = 100_000, ArrivalTimeBetween = 10 } );
		littered.Run( GuestReport.TicksForDays( 80 ) );
		Assert.IsTrue( littered.Guests.Litter!.Count > 5, $"{littered.Guests.Litter.Count} littered cells" );
		var types = littered.Guests.Litter.Littered().Select( cell => littered.Guests.Litter.TypeAt( cell.X, cell.Y ) ).Distinct().ToArray();
		Assert.IsTrue( types.Contains( LitterType.Vomit ) && types.Any( type => type is >= (LitterType)1 and <= (LitterType)5 ), $"vomit and ordinary litter: {string.Join( ", ", types )}" );
		var swept = new ParkScenario( new WorldSeed( 2 ), new ParkScenarioOptions { Litter = true, FoodShop = true, LitterEffect = 40, HandymanTurns = 8, ArrivalTimeBetween = 10 } );
		swept.Run( GuestReport.TicksForDays( 80 ) );
		Assert.IsTrue( swept.Guests.Litter!.Count < littered.Guests.Litter.Count, "a sweep every 8 turns keeps the park cleaner than none" );
		Assert.AreEqual( new ParkScenario( new WorldSeed( 2 ) ).Hash, plain.Hash );
	}

	// ---- Persistence ------------------------------------------------------------------------------

	[TestMethod]
	public void TheComplaintTimesAreKeptInBirthIdOrderReplacedAndForgottenAndJoinTheHashOnlyWhileTheyHoldAnEntry()
	{
		var park = Run( new SmarterGuestDecisions( SmarterRuleSet.Phase5 ), 3000 );
		var smarter = park.Guests.SmarterGuests!;
		smarter.Complaints.Clear();
		ulong Hash() => WorldStateHash.Compute( new WorldStateSources { Seed = park.Seed, Guests = park.Guests } );
		var empty = Hash();
		var guest = park.Guests.Guests.First( g => g.BirthId != 0 );
		smarter.Complaints.Note( guest.BirthId, 100 );
		var with = Hash();
		Assert.AreNotEqual( empty, with );
		smarter.Complaints.Note( guest.BirthId, 101 );
		Assert.AreNotEqual( with, Hash(), "so is its turn" );
		smarter.Meals.Note( guest.BirthId, 101 );
		smarter.Complaints.Clear();
		var meal = Hash();
		smarter.Meals.Clear();
		smarter.Complaints.Note( guest.BirthId, 101 );
		Assert.AreNotEqual( meal, Hash(), "a complaint on a turn is not a meal on it (the marker differs)" );
		smarter.Complaints.Forget( guest.BirthId );
		Assert.AreEqual( empty, Hash(), "an empty store hashes as 1.4.0 did" );
	}

	private static ParkScenario Complained( out SmarterGuestsSaveData section )
	{
		var park = Run( new SmarterGuestDecisions(), 9000 );
		var guests = park.Guests.Guests.Where( guest => guest.BirthId != 0 ).OrderBy( guest => guest.BirthId ).Take( 2 ).ToArray();
		var complaints = park.Guests.SmarterGuests!.Complaints;
		complaints.Note( guests[1].BirthId, park.Guests.ParkTurn - 10 );
		complaints.Note( guests[0].BirthId, park.Guests.ParkTurn - 40 );
		section = SmarterGuestsSave.Capture( park.Guests )!;
		return park;
	}

	[TestMethod]
	public void TheComplaintTimesRoundTripThroughTheSectionAndRestoreIntoAFreshInstance()
	{
		var park = Complained( out var section );
		Assert.IsTrue( section.Complaints!.Count >= 2 );
		var again = ParkResumeJson.Deserialize<SmarterGuestsSaveData>( ParkResumeJson.Serialize( section ) );
		CollectionAssert.AreEqual( section.Complaints.ToArray(), again.Complaints!.ToArray() );
		Assert.IsNull( SmarterGuestsSave.SectionProblem( again, park.Guests.Guests.Select( guest => guest.Id ), park.Guests.Attractions.Select( attraction => attraction.AttractionId ) ) );
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var fresh = SmarterGuestsTests.Fresh( park );
		SmarterGuestsSave.Restore( fresh.Guests, again );
		CollectionAssert.AreEqual( park.Guests.SmarterGuests!.Complaints.Entries.ToArray(), fresh.Guests.SmarterGuests!.Complaints.Entries.ToArray() );
		Assert.AreEqual( Encoding.UTF8.GetString( ParkResumeJson.Serialize( section ) ), Encoding.UTF8.GetString( ParkResumeJson.Serialize( SmarterGuestsSave.Capture( fresh.Guests )! ) ), "capturing the restored park gives the same section" );
		Assert.AreEqual( WorldStateHash.Compute( new WorldStateSources { Seed = park.Seed, Guests = park.Guests } ), WorldStateHash.Compute( new WorldStateSources { Seed = fresh.Seed, Guests = fresh.Guests } ), "the restored park hashes like the saved one" );
	}

	[TestMethod]
	public void ARestoredParkSeesTheSameCrowdsAndLitterAsTheOneThatWasSaved()
	{
		var options = new ParkScenarioOptions { Litter = true, FoodShop = true, ArrivalTimeBetween = 10, HandymanTurns = 100_000, LitterEffect = 25 };
		var park = new ParkScenario( new WorldSeed( 1 ), options, new SmarterGuestDecisions() );
		park.Run( GuestReport.TicksForDays( 60 ) );
		Assert.IsTrue( park.Guests.Litter!.Count > 0 );
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var fresh = new ParkScenario( park.Seed, options, new SmarterGuestDecisions() );
		fresh.Things.RestoreSnapshot( park.Things.CaptureSnapshot() );
		fresh.Guests.RestoreSnapshot( park.Guests.CaptureSnapshot() );
		fresh.Guests.Litter!.Restore( park.Guests.Litter.Snapshot() );
		var crowded = 0;
		for ( var x = 0; x < ParkScenario.StreetLength; x++ )
			for ( var y = 0; y < 12; y++ )
			{
				var (live, restored) = (park.Guests.PathCellOf( (x, y) ), fresh.Guests.PathCellOf( (x, y) ));
				Assert.AreEqual( live, restored, $"cell ({x}, {y})" );
				crowded += live.Crowd > 0 ? 1 : 0;
			}
		Assert.IsTrue( crowded > 0, "the saved park has shared cells to compare" );
	}

	[TestMethod]
	public void BadComplaintDataIsRefused()
	{
		var park = Complained( out var section );
		var ids = park.Guests.Guests.Select( guest => guest.Id ).ToArray();
		var attractions = park.Guests.Attractions.Select( attraction => attraction.AttractionId ).ToArray();
		var births = section.Births.Select( birth => birth.BirthId ).OrderBy( id => id ).ToArray();
		string? Problem( params MealTime[] complaints ) => SmarterGuestsSave.SectionProblem( section with { Complaints = complaints }, ids, attractions );
		Assert.IsNull( Problem( new MealTime( births[0], 5 ), new MealTime( births[2], 0 ) ) );
		StringAssert.Contains( Problem( new MealTime( births[^1] + 100, 5 ) )!, "whom the section does not hold" );
		StringAssert.Contains( Problem( new MealTime( births[1], 5 ), new MealTime( births[0], 5 ) )!, "ascending" );
		StringAssert.Contains( Problem( new MealTime( births[0], -1 ) )!, "below 0" );
		StringAssert.Contains( Problem()!, "empty complaint list" );
		var record = new[] { new ParkModRecord( "opentpw.smarter-guests", "1.6.0", ModSet.NoFilesSha256 ) };
		var withComplaint = section with { Complaints = [new MealTime( births[0], 5 )] };
		Assert.IsNull( SmarterGuestsSave.ShapeProblem( withComplaint, ids, record, false, attractions ) );
		StringAssert.Contains( SmarterGuestsSave.ShapeProblem( withComplaint, ids, null, true, attractions )!, "does not record" );
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var fresh = SmarterGuestsTests.Fresh( park );
		// after the latest turn the park's guests can be on (their turns' phase may put it one past the tick count's turn)
		var future = section with { Complaints = [new MealTime( births[0], GuestSimulation.LatestParkTurn( fresh.Guests.TickCount ) + 1 )] };
		var refused = Assert.ThrowsException<InvalidDataException>( () => SmarterGuestsSave.Restore( fresh.Guests, future ) );
		StringAssert.Contains( refused.Message, "complaint" );
		Assert.AreEqual( 0, fresh.Guests.SmarterGuests!.Complaints.Count, "nothing was half-restored" );
	}

	[TestMethod]
	public void ASectionWithoutComplaintsHasNoMemberAndAGuestThatLeavesIsForgotten()
	{
		var park = Run( new SmarterGuestDecisions( SmarterRuleSet.Phase5 ), 4000 );
		var json = Encoding.UTF8.GetString( ParkResumeJson.Serialize( SmarterGuestsSave.Capture( park.Guests )! ) );
		Assert.IsFalse( json.Contains( "Complaints" ), "a section from before and a park nobody complained in are as they were" );
		var complaining = Run( new SmarterGuestDecisions(), GuestReport.TicksForDays( 60 ), new ParkScenarioOptions { ArrivalTimeBetween = 10 } );
		Assert.IsTrue( complaining.Guests.Departed > 0 );
		var alive = complaining.Guests.Guests.Select( guest => guest.BirthId ).ToHashSet();
		Assert.IsTrue( complaining.Guests.SmarterGuests!.Complaints.Entries.All( entry => alive.Contains( entry.BirthId ) ), "the store holds only guests in the park" );
	}

	// ---- The report -------------------------------------------------------------------------------

	[TestMethod]
	public void TheReportParsesTheNewRuleNamesAndMeasuresThePathMetrics()
	{
		Assert.AreEqual( SmarterRuleSet.Phase5, GuestReport.ParseRules( "phase5" ) );
		Assert.AreEqual( SmarterRuleSet.Paths, GuestReport.ParseRules( "paths" ) );
		Assert.AreEqual( SmarterRuleSet.Litter | SmarterRuleSet.MealTime, GuestReport.ParseRules( "litter, meal" ) );
		Assert.AreEqual( SmarterRuleSet.Crowding, GuestReport.ParseRules( "crowding" ) );
		var park = new ParkScenario( new WorldSeed( 1 ), new ParkScenarioOptions { Litter = true, FoodShop = true, ArrivalTimeBetween = 10 }, new SmarterGuestDecisions() );
		var metrics = new GuestMetrics();
		metrics.Attach( park.Guests );
		park.Run( GuestReport.TicksForDays( 10 ) );
		metrics.BeginWindow();
		park.Run( GuestReport.TicksForDays( 40 ) );
		var snapshot = metrics.EndWindow();
		var paths = snapshot.Paths!;
		Assert.IsTrue( paths.GuestTicks > 0 && paths.CrowdTicks > 0 && paths.LitterCellSum > 0 );
		Assert.IsTrue( paths.LitteredGuestTicks <= paths.LitterNearGuestTicks + paths.LitteredGuestTicks && paths.LitteredGuestTicks <= paths.GuestTicks );
		Assert.IsTrue( paths.CrowdedGuestTicks <= paths.PathGuestTicks && paths.PathGuestTicks <= paths.GuestTicks );
		var names = snapshot.Headline().Select( item => item.Name ).ToArray();
		foreach ( var name in new[] { "path.littered.timeShare", "path.litterNear.timeShare", "path.litterEncounters.perAdmission", "path.crowd.guestsPerOccupiedCell", "path.crowded.timeShare", "path.complaints.litter.perAdmission", "path.complaints.crowd.perAdmission", "path.goalsMoved.perAdmission", "path.litter.meanCells" } )
			CollectionAssert.Contains( names, name );
		Assert.AreEqual( -1, GuestReportCompare.Direction( "path.littered.timeShare" ) );
		Assert.AreEqual( 0, GuestReportCompare.Direction( "path.complaints.crowd.perAdmission" ), "a complaint is the rule's output, not a quality to push down" );
	}
}
