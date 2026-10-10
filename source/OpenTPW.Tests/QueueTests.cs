using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// Asset-free tests of the queue model traced in docs/reverse/QUEUE-plan.md: queue cells and their links,
/// joining limits, standing and moving up on park turns, admission gates, leave rules and positions.
/// </summary>
[TestClass]
public class QueueTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private const int RideId = 1;

	/// <summary>A script that only yields; tests drive its variables directly.</summary>
	private static RideVM IdleScript( IRideScriptEffects effects ) => new( new RideScriptFile( new MemoryStream(
		new Asm().Label( "loop" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" ).Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Effects = effects, Seed = 1 } );

	/// <summary>
	/// Path row y = 0 (x 0..9). The ride's entrance cell is (2, 6); its outside cell (2, 5) is the queue's
	/// front, and the queue runs up the column x = 2 to the back cell (2, 1), which touches the path at (2, 0).
	/// </summary>
	private sealed class Rig
	{
		public readonly GuestPathGrid Grid = new( 12, 12 );
		public readonly RideVisitorBridge Ride;
		public readonly RideVM Script;
		public readonly GuestSimulation Simulation;
		public bool Open = true;

		public Rig( int queueCells = 5, QueueParameters? parameters = null, GuestNeeds satisfies = GuestNeeds.None, int capacity = 5, bool takesGuests = false, int excitement = 55, Action<GuestSettings>? configure = null )
		{
			for ( var x = 0; x < 10; x++ )
				Grid.SetPath( x, 0, true );
			Ride = new RideVisitorBridge( RideId, "Queue ride", satisfies == GuestNeeds.None ? RideVisitorKind.Ride : RideVisitorKind.Facility, capacity, excitement, 30, satisfies )
			{
				EntranceCell = (2, 0),
				ExitCell = (2, 0),
				QueueFrontCell = (2, 5),
				QueueEntranceDirection = 2, // +Y, toward the ride's entrance cell (2, 6)
				Parameters = parameters ?? new QueueParameters( true, false, false, 130, 60, 60, capacity, 30 )
			};
			var effects = new VisitorRideScriptEffects( Ride, UnimplementedRideScriptEffects.Instance );
			Script = takesGuests ? GuestTests.CreateShopScript( effects ) : IdleScript( effects );
			Ride.Attach( Script, () => Open );
			if ( queueCells > 0 )
				Assert.AreEqual( queueCells, QueuePaths.TryExtend( Grid, Ride, Enumerable.Range( 0, queueCells ).Select( index => (2, 5 - index) ) ) );
			var settings = GuestTests.CreateSettings();
			settings.ToiletPerSecond = 0;
			configure?.Invoke( settings );
			Simulation = new GuestSimulation( Grid, settings, 7 ) { ArrivalsEnabled = false };
			Simulation.Register( Ride );
			Script.Advance( Tick );
		}

		/// <summary>Stops admission: the ride runs and does not run continuously.</summary>
		public void HoldAdmission() => Script[RideVariables.VAR_RUNNING] = 1;

		public Guest Send( float happiness = 60 )
		{
			var guest = Simulation.SpawnInPark( 2, 0 );
			guest.Happiness = happiness;
			guest.AttractionId = RideId;
			guest.State = GuestState.GoingToRide;
			return guest;
		}

		public void Step()
		{
			Simulation.Tick( Tick );
			Ride.HostStep();
			Script.Advance( Tick );
		}

		/// <summary>Runs until the park turn has advanced by <paramref name="turns"/>.</summary>
		public void RunTurns( int turns, Action? afterTick = null )
		{
			var target = Simulation.ParkTurn + turns;
			while ( Simulation.ParkTurn < target )
			{
				Step();
				afterTick?.Invoke();
			}
		}
	}

	// ---- Queue cells -------------------------------------------------------------------------------

	[TestMethod]
	public void QueueCellsLinkBackTowardTheEntranceAndRecomputeAfterEdits()
	{
		var rig = new Rig( queueCells: 0 );
		var ride = rig.Ride;
		var grid = rig.Grid;
		CollectionAssert.AreEqual( new[] { (2, 0) }, ride.QueueCells.ToArray(), "no queue cells yet: the path fallback is a one-cell queue" );
		Assert.AreEqual( QueueBuildResult.NotAtQueueEnd, QueuePaths.TryExtend( grid, ride, 2, 4 ), "the first cell is the entrance's outside cell" );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 2, 5 ) );
		Assert.AreEqual( GuestPathGrid.LinkValue( 2 ), grid.GetQueueLink( 2, 5 ), "the front cell links toward the ride entrance" );
		Assert.IsNull( ride.JoinCell, "a queue that touches no path is not connected" );
		Assert.AreEqual( QueueBuildResult.NotAtQueueEnd, QueuePaths.TryExtend( grid, ride, 3, 3 ) );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 2, 4 ) );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 3, 4 ) );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 3, 3 ) );
		Assert.AreEqual( 16, grid.GetQueueLink( 2, 4 ), "+Y toward (2, 5)" );
		Assert.AreEqual( 64, grid.GetQueueLink( 3, 4 ), "−X toward (2, 4)" );
		Assert.AreEqual( 16, grid.GetQueueLink( 3, 3 ), "+Y toward (3, 4)" );
		Assert.AreEqual( 1, GuestPathGrid.LinkValue( 0 ) );
		Assert.AreEqual( 4, GuestPathGrid.LinkValue( 1 ) );
		Assert.AreEqual( 3, GuestPathGrid.LinkDirection( 64 ) );
		// A queue cell next to the chain whose link does not point back at it is not part of the queue.
		grid.SetQueue( 1, 4, 0 );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 3, 2 ) );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 3, 1 ) );
		CollectionAssert.AreEqual( new[] { (2, 5), (2, 4), (3, 4), (3, 3), (3, 2), (3, 1) }, ride.QueueCells.ToArray() );
		Assert.AreEqual( (3, 1), ride.QueueBackCell );
		Assert.AreEqual( 6, ride.QueueSizeInCells );
		Assert.AreEqual( (3, 0), ride.JoinCell, "guests step onto the back cell from the path" );
		Assert.AreEqual( 24, ride.QueueRoom );
		Assert.AreEqual( 24, ride.MaximumQueueLength, "min(4 × cells, 100)" );
		Assert.IsFalse( grid.IsWalkable( 3, 3 ), "queue cells are not path cells" );
		Assert.AreEqual( -1, grid.Distance( 3, 0, 3, 3 ) );

		var edits = ride.QueueEditCount;
		Assert.IsFalse( ride.RecomputeQueue( grid ), "an unchanged grid is no queue edit" );
		Assert.AreEqual( 3, QueuePaths.RemoveFrom( grid, ride, 3, 3 ), "removing a cell removes every cell behind it" );
		Assert.AreEqual( edits + 1, ride.QueueEditCount );
		Assert.AreEqual( 3, ride.QueueSizeInCells );
		Assert.IsNull( ride.JoinCell );
		Assert.IsTrue( grid.IsQueue( 1, 4 ), "unrelated queue cells are untouched" );
	}

	[TestMethod]
	public void QueueAndPathCellsShareOneParkCellMap()
	{
		var grid = new GuestPathGrid( 4, 3 );
		grid.SetPath( 1, 0, true, 0b0010 ); // links +X
		Assert.AreEqual( ParkCellType.Path, grid.Cells.TypeAt( 1, 0 ) );
		Assert.AreEqual( (byte)SavePathConnections.PositiveX, grid.Cells.LinksAt( 1, 0 ), "the original 1/4/16/64 link bits" );
		grid.SetQueue( 1, 1, 0 );
		Assert.AreEqual( (ParkCellType.Queue, (byte)1, (byte)0), (grid.Cells.TypeAt( 1, 1 ), grid.Cells.QueueLinkAt( 1, 1 ), grid.Cells.LinksAt( 1, 1 )) );
		Assert.IsTrue( grid.IsQueue( 1, 1 ) && !grid.IsWalkable( 1, 1 ) );
		grid.ClearQueue( 1, 1 );
		Assert.AreEqual( (ParkCellType.Empty, (byte)0), (grid.Cells.TypeAt( 1, 1 ), grid.Cells.QueueLinkAt( 1, 1 )) );
		grid.SetPath( 1, 0, false );
		Assert.AreEqual( ParkCellType.Empty, grid.Cells.TypeAt( 1, 0 ) );
		Assert.AreEqual( 0, grid.QueueCellCount );
	}

	[TestMethod]
	public void CanChangeCellTypeFollowsTheTracedRules()
	{
		static bool Can( int old, int type, bool last = false ) => ParkCellMap.CanChangeCellType( (byte)old, (byte)type, last );
		Assert.IsTrue( Can( 9, 0 ), "clearing" );
		Assert.IsFalse( Can( 4, 4 ) );
		Assert.IsTrue( Can( 21, 21 ) );
		Assert.IsTrue( Can( 3, 1 ), "path over queue" );
		Assert.IsTrue( Can( 0, 3 ) && Can( 3, 3 ) && Can( 1, 1 ) );
		Assert.IsTrue( Can( 1, 4 ) );
		Assert.IsFalse( Can( 1, 3 ), "a queue goes over a path only with its last cell" );
		Assert.IsTrue( Can( 1, 3, last: true ) );
		Assert.IsFalse( Can( 5, 3 ) || Can( 4, 1 ) );
	}

	[TestMethod]
	public void QueueCellsAreChargedPerCellAndRefundedAtTheRideScrapPercentage()
	{
		var economy = EconomyTestData.Park( initialCash: 100 );
		var start = economy.Balance;
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TrySpendCell( CellPurchase.Queue ) );
		Assert.AreEqual( start - 75, economy.Balance, "Costs.QueueCell when written" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughMoney, economy.TrySpendCell( CellPurchase.Queue ), "balance 25 − 75 < 0" );
		Assert.AreEqual( start - 75, economy.Balance );

		var park = EconomyTestData.Park();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1100, out var ride ) );
		var before = park.Balance;
		Assert.AreEqual( 50, park.ScrapPercent( ride! ), "year-0 scrap percentage" );
		Assert.AreEqual( 75 * 50 / 100, park.RefundQueueCell( ride!.Id ) );
		Assert.AreEqual( before + 37, park.Balance );
		Assert.AreEqual( 0, park.RefundQueueCell( 99999 ), "no ride, no refund" );
	}

	[TestMethod]
	public void QueueBuildRulesRefuseOtherCells()
	{
		var rig = new Rig( queueCells: 0 );
		Assert.AreEqual( QueueBuildResult.OutOfBounds, QueuePaths.CheckExtend( rig.Grid, rig.Ride, 20, 20 ) );
		Assert.AreEqual( QueueBuildResult.Blocked, QueuePaths.CheckExtend( rig.Grid, rig.Ride, 2, 5, ( x, y ) => x == 2 && y == 5 ) );
		var shop = new RideVisitorBridge( 9, "Shop", RideVisitorKind.Shop, 1, 0, 10 ) { EntranceCell = (5, 0), QueueFrontCell = (5, 1), QueueEntranceDirection = 2 };
		Assert.AreEqual( QueueBuildResult.NoQueue, QueuePaths.CheckExtend( rig.Grid, shop, 5, 1 ), "objects without Info.HasQueue use the virtual queue" );
		var onPath = new RideVisitorBridge( 10, "On path", RideVisitorKind.Ride, 1, 0, 10 ) { EntranceCell = (5, 0), QueueFrontCell = (5, 0), QueueEntranceDirection = 0, Parameters = rig.Ride.Parameters };
		Assert.AreEqual( QueueBuildResult.NoFrontCell, QueuePaths.CheckExtend( rig.Grid, onPath, 5, 0 ) );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( rig.Grid, rig.Ride, 2, 5 ) );
		Assert.AreEqual( QueueBuildResult.Blocked, QueuePaths.CheckExtend( rig.Grid, rig.Ride, 2, 5 ), "already a queue cell" );
		// A long queue: the 26th cell is refused.
		var grid = new GuestPathGrid( 40, 40 );
		var ride = new RideVisitorBridge( 11, "Long", RideVisitorKind.Ride, 1, 0, 10 ) { EntranceCell = (0, 0), QueueFrontCell = (0, 1), QueueEntranceDirection = 0, Parameters = rig.Ride.Parameters };
		Assert.AreEqual( QueuePaths.MaximumCells, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 1, 30 ).Select( y => (0, y) ) ) );
		Assert.AreEqual( QueueBuildResult.TooLong, QueuePaths.CheckExtend( grid, ride, 0, 26 ) );
		Assert.AreEqual( 25, ride.QueueSizeInCells );
		Assert.AreEqual( 100, ride.MaximumQueueLength, "25 cells reach the HasQueue limit" );
	}

	// ---- Joining ---------------------------------------------------------------------------------

	[TestMethod]
	public void QueueLimitFollowsTheTracedFormula()
	{
		static int Limit( bool hasQueue, float qwtc, int speed, int initSpeed, int capacity, int duration ) =>
			RideVisitorBridge.ComputeQueueLimit( new QueueParameters( hasQueue, false, false, qwtc, speed, initSpeed, capacity, duration ) );
		Assert.AreEqual( 100, Limit( true, 130, 60, 60, 5, 30 ), "HasQueue rides: 100, the QueueWaitTimeConstant is unused" );
		Assert.AreEqual( 21, Limit( false, 130, 60, 60, 5, 30 ), "trunc(130 × (1 × 5) / 30) = trunc(21.67)" );
		Assert.AreEqual( 10, Limit( false, 130, 30, 60, 5, 30 ), "G = SPEED / InitSpeed = 0.5: trunc(10.83)" );
		Assert.AreEqual( 20, Limit( false, 30, 0, 60, 2, 3 ), "SPEED 0 gives G = 1 (Rides.sam level 0: QWTC 30, CAP 2, DUR 3)" );
		Assert.AreEqual( 4, Limit( false, 10, 60, 60, 1, 30 ), "floor of 4" );
		Assert.AreEqual( 4, Limit( false, 0, 0, 0, 0, 0 ), "a NaN quotient becomes 4" );
		Assert.AreEqual( int.MaxValue, Limit( false, 30, 0, 60, 2, 0 ), "DUR 0 divides to +infinity; the room still bounds the queue" );
		Assert.AreEqual( 12, Limit( false, 3, 65, 90, 6, 1 ), "single precision: 3 × (65/90 × 6) / 1 = 12.999999f (13 in double)" );
	}

	[TestMethod]
	public void JoiningChecksRoomThenExcitementThenTheLimit()
	{
		// One queue cell: room 4.
		var rig = new Rig( queueCells: 1 );
		var ride = rig.Ride;
		Assert.AreEqual( QueueJoinResult.NotExcitingEnough, ride.JoinQueue( 1, 45 ), "|difference| ≥ 45, ride below the guest's preference" );
		Assert.AreEqual( QueueJoinResult.TooExciting, ride.JoinQueue( 1, -45 ) );
		Assert.AreEqual( QueueJoinResult.Joined, ride.JoinQueue( 1, 44 ) );
		Assert.AreEqual( QueueJoinResult.AlreadyQueued, ride.JoinQueue( 1, 0 ) );
		Assert.IsTrue( new[] { 2, 3, 4 }.All( ride.TryJoinQueue ) );
		Assert.AreEqual( QueueJoinResult.NoRoom, ride.JoinQueue( 5, 90 ), "the room check comes before the excitement gate" );

		// No HasQueue: two cells give room 8, but the QWTC limit is 4 (the floor).
		var shop = new Rig( queueCells: 0, parameters: new QueueParameters( false, false, false, 10, 60, 60, 1, 30 ) );
		shop.Grid.SetQueue( 2, 5, 2 );
		shop.Grid.SetQueue( 2, 4, 2 );
		shop.Ride.RecomputeQueue( shop.Grid );
		Assert.AreEqual( (8, 4, 4), (shop.Ride.QueueRoom, shop.Ride.QueueLimit, shop.Ride.MaximumQueueLength) );
		Assert.IsTrue( new[] { 1, 2, 3, 4 }.All( shop.Ride.TryJoinQueue ) );
		Assert.AreEqual( QueueJoinResult.QueueTooLong, shop.Ride.JoinQueue( 5, 0 ) );

		// HasQueue with 26 cells (laid directly on the grid): room 104, limit 100.
		var grid = new GuestPathGrid( 30, 30 );
		for ( var y = 1; y <= 26; y++ )
			grid.SetQueue( 1, y, 0 );
		var big = new RideVisitorBridge( 4, "Big", RideVisitorKind.Ride, 5, 50, 10 ) { EntranceCell = (1, 0), QueueFrontCell = (1, 1), QueueEntranceDirection = 0, Parameters = rig.Ride.Parameters };
		big.Attach( IdleScript( UnimplementedRideScriptEffects.Instance ), () => true );
		big.RecomputeQueue( grid );
		Assert.AreEqual( 26, big.QueueSizeInCells );
		Assert.AreEqual( (104, 100, 100), (big.QueueRoom, big.QueueLimit, big.MaximumQueueLength) );
		Assert.IsTrue( Enumerable.Range( 1, 100 ).All( big.TryJoinQueue ) );
		Assert.AreEqual( QueueJoinResult.QueueTooLong, big.JoinQueue( 101, 0 ) );
		Assert.AreEqual( 99, big.GetQueuePosition( 100 ) );
	}

	// ---- Standing and moving up ------------------------------------------------------------------

	[TestMethod]
	public void GuestsWalkTheQueueCellsToFourPositionsPerCell()
	{
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		var visited = new HashSet<(int, int)>();
		rig.RunTurns( 120, () => visited.UnionWith( guests.Where( guest => guest.IsInQueue && rig.Grid.IsQueue( guest.Cell.X, guest.Cell.Y ) ).Select( guest => guest.Cell ) ) );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ), string.Join( ", ", guests.Select( guest => guest.State ) ) );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 6 ).ToArray(), guests.Select( guest => guest.QueuePosition ).ToArray() );
		// Positions 0..3 stand in the front cell, 4 and 5 in the second cell; everyone walked in over the back cells.
		CollectionAssert.AreEqual( new[] { (2, 5), (2, 5), (2, 5), (2, 5), (2, 4), (2, 4) }, guests.Select( guest => guest.Cell ).ToArray() );
		Assert.IsTrue( visited.SetEquals( new[] { (2, 5), (2, 4), (2, 3), (2, 2), (2, 1) } ), string.Join( " ", visited ) );
		Assert.IsTrue( guests.All( guest => rig.Grid.IsQueue( guest.Cell.X, guest.Cell.Y ) ) );
		// Depth 0/63/127/191 from the front edge (toward the ride at +Y), a small sideways offset.
		var expectedY = new[] { 6f - 0 / 255f, 6f - 63 / 255f, 6f - 127 / 255f, 6f - 191 / 255f, 5f, 5f - 63 / 255f };
		for ( var index = 0; index < guests.Count; index++ )
		{
			Assert.AreEqual( expectedY[index], guests[index].Y, 1e-4, $"position {index}" );
			Assert.IsTrue( Math.Abs( guests[index].X - 2.5f ) <= 14 / 255f + 1e-4f, $"lateral offset {guests[index].X}" );
		}
		Assert.AreEqual( 6, rig.Ride.QueueLength );
		Assert.AreEqual( 6, rig.Simulation.GetStatistics().Queueing );
	}

	[TestMethod]
	public void QueueSlotsFollowTheTracedDepthBytes()
	{
		CollectionAssert.AreEqual( new[] { (0, 0), (0, 63), (0, 127), (0, 191), (1, 0), (1, 63), (2, 127) },
			new[] { 0, 1, 2, 3, 4, 5, 10 }.Select( position => GuestSimulation.QueueSlot( position, 3 ) ).ToArray() );
		Assert.AreEqual( (0, 191), GuestSimulation.QueueSlot( 7, 1 ), "beyond the last cell the walk stops at the terminator" );
	}

	[TestMethod]
	public void GuestsMoveUpAfterTruncOneTwoTimesPositionTurnsWhenTheGapIsSmall()
	{
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ) );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 6 }, guests.Select( guest => guest.QueueMoveDelay ).ToArray(), "trunc(1.2 × position)" );

		// The front guest leaves: everyone's gap is 1, so each waits out its delay before walking.
		rig.Ride.LeaveQueue( guests[0].Id );
		var removedTurn = rig.Simulation.ParkTurn;
		var moved = new Dictionary<int, long>();
		rig.RunTurns( 12, () =>
		{
			foreach ( var guest in guests.Skip( 1 ).Where( guest => guest.State == GuestState.MovingUpQueue ) )
				moved.TryAdd( guest.Id, rig.Simulation.ParkTurn - removedTurn );
		} );
		Assert.IsFalse( guests[0].IsInQueue, "the removed guest left the queue" );
		CollectionAssert.AreEqual( new long[] { 2, 3, 4, 5, 7 }, guests.Skip( 1 ).Select( guest => moved[guest.Id] ).ToArray(),
			"first seen at the next turn, then 1, 2, 3, 4 and 6 turns of delay" );
		rig.RunTurns( 20 );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4 }, guests.Skip( 1 ).Select( guest => guest.QueuePosition ).ToArray() );

		// A gap of 3 moves at once.
		foreach ( var guest in guests.Skip( 1 ).Take( 3 ) )
			rig.Ride.LeaveQueue( guest.Id );
		removedTurn = rig.Simulation.ParkTurn;
		moved.Clear();
		rig.RunTurns( 4, () =>
		{
			foreach ( var guest in guests.Skip( 4 ).Where( guest => guest.State == GuestState.MovingUpQueue ) )
				moved.TryAdd( guest.Id, rig.Simulation.ParkTurn - removedTurn );
		} );
		CollectionAssert.AreEqual( new long[] { 1, 1 }, guests.Skip( 4 ).Select( guest => moved[guest.Id] ).ToArray() );
	}

	[TestMethod]
	public void StandingGuestsNeverLeaveForBoredom()
	{
		var rig = new Rig( configure: settings => settings.HungerPerSecond = settings.ThirstPerSecond = 0 );
		rig.HoldAdmission();
		var calm = Enumerable.Range( 0, 5 ).Select( _ => rig.Send( 60 ) ).ToList();
		var happy = Enumerable.Range( 0, 5 ).Select( _ => rig.Send( 95 ) ).ToList();
		var interludes = 0;
		rig.RunTurns( 2000, () => interludes += happy.Count( guest => guest.InQueueInterlude ) > 0 ? 1 : 0 );
		Assert.IsTrue( calm.Concat( happy ).All( guest => guest.IsInQueue ), "no time-based exit (QUEUE-plan §5.3)" );
		Assert.AreEqual( 10, rig.Ride.QueueLength );
		Assert.IsTrue( calm.All( guest => rig.Simulation.ParkTurn - guest.QueueStandingSinceTurn > GuestSimulation.BoredomTurns ),
			"standing far longer than the dead +508 + 100 test" );
		Assert.IsTrue( interludes > 0, "happy guests idle in interludes and return to the queue" );
		Assert.IsTrue( calm.Concat( happy ).All( guest => rig.Simulation.QueueWaitTurns( guest ) >= 1990 ) );
	}

	// ---- Leaving ---------------------------------------------------------------------------------

	[TestMethod]
	public void UnhappyGuestsLeaveOutsideTheThirtyTurnWindow()
	{
		var rig = new Rig();
		rig.HoldAdmission();
		var guest = rig.Send( 5 );
		long leftAt = -1;
		var leftCell = (-1, -1);
		var thought = GuestThought.None;
		rig.RunTurns( 40, () =>
		{
			if ( leftAt >= 0 || guest.IsInQueue || guest.State == GuestState.GoingToRide )
				return;
			leftAt = rig.Simulation.ParkTurn;
			leftCell = guest.Cell;
			thought = guest.Thought;
		} );
		Assert.AreEqual( GuestThought.Unhappy, thought );
		Assert.AreEqual( GuestSimulation.NeedsWindowTurns + 1, leftAt, "turn − +520 must exceed 30" );
		Assert.AreEqual( (2, 0), leftCell, "back on the path at the join cell" );
	}

	[TestMethod]
	public void GuestsNeedingTheToiletLeaveUnlessQueueingForOne()
	{
		foreach ( var relief in new[] { false, true } )
		{
			var rig = new Rig( satisfies: relief ? GuestNeeds.Toilet : GuestNeeds.None );
			rig.HoldAdmission();
			var guest = rig.Send();
			guest.Toilet = 100;
			var left = GuestThought.None;
			rig.RunTurns( 40, () =>
			{
				if ( left == GuestThought.None && guest.State == GuestState.WalkingAround )
					left = guest.Thought;
			} );
			Assert.AreEqual( relief ? GuestThought.None : GuestThought.NeedToilet, left, relief ? "ProvidesRelief keeps the guest" : "toilet > 80 leaves" );
			Assert.AreEqual( relief, guest.IsInQueue );
		}
	}

	[TestMethod]
	public void RideFailureAndQueueEditsSendGuestsAway()
	{
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 10 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ) );
		// Shorten the queue to two cells: room 8, positions 8 and 9 stood on the removed third cell.
		Assert.AreEqual( 3, QueuePaths.RemoveFrom( rig.Grid, rig.Ride, 2, 3 ) );
		rig.RunTurns( 1 );
		Assert.IsTrue( guests.Take( 8 ).All( guest => guest.IsInQueue ) );
		Assert.IsTrue( guests.Skip( 8 ).All( guest => !guest.IsInQueue && guest.Thought == GuestThought.QueueTooLong ) );
		Assert.AreEqual( 8, rig.Ride.QueueLength );
		Assert.IsNull( rig.Ride.JoinCell, "the shortened queue no longer touches the path" );

		rig.Script[RideVariables.VAR_BROKEN] = 1;
		rig.RunTurns( 2 );
		Assert.IsTrue( guests.All( guest => !guest.IsInQueue ), "a ride failure empties the queue" );
		Assert.AreEqual( 0, rig.Ride.QueueLength );
	}

	// ---- Admission -------------------------------------------------------------------------------

	private sealed class TurnHost : IRideVisitorHost
	{
		public long Turn;
		public readonly HashSet<int> AtFront = new();
		public readonly List<string> Events = new();
		public long ParkTurn => Turn;
		public bool WalksToBoard => true;
		public bool IsStandingAtFront( IRideVisitorBridge ride, int guestId ) => AtFront.Contains( guestId );
		public void OnVisitorOffered( IRideVisitorBridge ride, int guestId ) => Events.Add( $"call {guestId}" );
		public void OnVisitorBoarded( IRideVisitorBridge ride, int guestId ) => Events.Add( $"board {guestId}" );
		public void OnVisitorReleased( IRideVisitorBridge ride, int guestId ) => Events.Add( $"release {guestId}" );
		public void OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId ) => Events.Add( $"away {guestId}" );
	}

	private static (RideVisitorBridge Ride, RideVM Script, TurnHost Host) CreateAdmission( QueueParameters? parameters = null )
	{
		var ride = new RideVisitorBridge( 3, "Admission", RideVisitorKind.Ride, 2, 50, 10 ) { Parameters = parameters ?? QueueParameters.Default };
		var script = IdleScript( new VisitorRideScriptEffects( ride, UnimplementedRideScriptEffects.Instance ) );
		ride.Attach( script, () => true );
		script.Advance( Tick );
		var host = new TurnHost();
		ride.Host = host;
		return (ride, script, host);
	}

	private static AdmissionCheck NextTurn( RideVisitorBridge ride, TurnHost host )
	{
		host.Turn++;
		ride.HostStep();
		return ride.LastAdmissionCheck;
	}

	[TestMethod]
	public void AdmissionCallsTheFrontGuestOnlyWhenTheOriginalGatesHold()
	{
		var (ride, script, host) = CreateAdmission();
		var checks = 0;
		ride.AdmissionChecked += ( _, _ ) => checks++;
		Assert.IsTrue( ride.TryJoinQueue( 1 ) && ride.TryJoinQueue( 2 ) );

		var check = NextTurn( ride, host );
		Assert.AreEqual( (true, 1, false, 0, false), (check.ConditionsHold, check.HeadGuest, check.HeadAtFront, check.CalledGuest, check.Stalled), "the head is not standing at position 0 yet" );
		ride.HostStep();
		Assert.AreEqual( 1, checks, "one evaluation per park turn" );

		host.AtFront.Add( 1 );
		check = NextTurn( ride, host );
		Assert.AreEqual( 1, check.CalledGuest );
		Assert.AreEqual( 1, ride.CalledGuest );
		Assert.AreEqual( 0, script[RideVariables.VAR_LETMEON], "the called guest still has to walk to the stand point" );
		CollectionAssert.AreEqual( new[] { 1, 2 }, ride.Queue.ToArray(), "called guests stay queued" );
		Assert.IsFalse( NextTurn( ride, host ).ConditionsHold, "one pending visitor at a time" );

		Assert.IsFalse( ride.PresentForBoarding( 2 ) );
		Assert.IsTrue( ride.PresentForBoarding( 1 ) );
		Assert.AreEqual( 1, script[RideVariables.VAR_LETMEON] );
		NextTurn( ride, host );
		CollectionAssert.AreEqual( new[] { 1, 2 }, ride.Queue.ToArray(), "still queued until the script consumes VAR_LETMEON" );
		script[RideVariables.VAR_LETMEON] = 0;
		script[RideVariables.VAR_ONRIDE] = 1;
		host.AtFront.Add( 2 );
		check = NextTurn( ride, host );
		CollectionAssert.AreEqual( new[] { "call 1", "board 1", "call 2" }, host.Events );
		Assert.AreEqual( 2, check.CalledGuest, "ONRIDE 1 < CAPACITY 2" );
		Assert.AreEqual( 0, ride.StalledAdmissionChecks );

		// Capacity gate.
		ride.PresentForBoarding( 2 );
		script[RideVariables.VAR_LETMEON] = 0;
		script[RideVariables.VAR_ONRIDE] = 2;
		Assert.IsTrue( ride.TryJoinQueue( 3 ) );
		host.AtFront.Add( 3 );
		Assert.IsFalse( NextTurn( ride, host ).ConditionsHold, "ONRIDE 2 = CAPACITY 2" );
		script[RideVariables.VAR_CAPACITY] = 3;
		Assert.AreEqual( 3, NextTurn( ride, host ).CalledGuest, "VAR_CAPACITY, when set, is the capacity" );
	}

	[TestMethod]
	public void AdmissionWaitsForTheRideUnlessItRunsContinuously()
	{
		foreach ( var (parameters, expected) in new[] { (QueueParameters.Default, 0), (QueueParameters.Default with { RunsContinuously = true }, 1) } )
		{
			var (ride, script, host) = CreateAdmission( parameters );
			ride.TryJoinQueue( 1 );
			host.AtFront.Add( 1 );
			script[RideVariables.VAR_RUNNING] = 1;
			Assert.AreEqual( expected, NextTurn( ride, host ).CalledGuest );
		}
		foreach ( var (parameters, expected) in new[] { (QueueParameters.Default, 0), (QueueParameters.Default with { SkipsCapacityTest = true }, 1) } )
		{
			var (ride, script, host) = CreateAdmission( parameters );
			ride.TryJoinQueue( 1 );
			host.AtFront.Add( 1 );
			script[RideVariables.VAR_ONRIDE] = 5;
			Assert.AreEqual( expected, NextTurn( ride, host ).CalledGuest, "track types 2/3 skip the capacity test" );
		}
		{
			var (ride, script, host) = CreateAdmission();
			ride.TryJoinQueue( 1 );
			host.AtFront.Add( 1 );
			script[RideVariables.VAR_LETMEON] = 99;
			Assert.IsFalse( NextTurn( ride, host ).ConditionsHold, "VAR_LETMEON must be 0" );
		}
	}

	[TestMethod]
	public void QueuedGuestsAreCalledWalkToTheStandPointAndBoard()
	{
		var rig = new Rig( takesGuests: true, capacity: 1 );
		var waits = new List<long>();
		rig.Simulation.QueueWaitCompleted += ( _, _, turns ) => waits.Add( turns );
		var stalled = 0;
		rig.Ride.AdmissionChecked += ( _, check ) => stalled += check.Stalled ? 1 : 0;
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		var sawBoarding = false;
		var maximumQueue = 0;
		rig.RunTurns( 400, () =>
		{
			sawBoarding |= guests.Any( guest => guest.State == GuestState.Boarding );
			maximumQueue = Math.Max( maximumQueue, rig.Ride.QueueLength );
			// Called guests stay in the list until the script has taken them.
			if ( rig.Ride.CalledGuest != 0 )
				Assert.AreEqual( 0, rig.Ride.GetQueuePosition( rig.Ride.CalledGuest ) );
		} );
		Assert.IsTrue( sawBoarding, "the called guest walks to the stand point (state 13)" );
		Assert.AreEqual( 6, maximumQueue );
		Assert.IsTrue( guests.All( guest => guest.RidesTaken >= 1 ), string.Join( ", ", guests.Select( guest => $"{guest.State} {guest.RidesTaken}" ) ) );
		Assert.IsTrue( waits.Count >= 6 );
		var first = waits.Take( 6 ).ToArray();
		Assert.IsTrue( first.Zip( first.Skip( 1 ) ).All( pair => pair.First < pair.Second ), "first come, first served: " + string.Join( ", ", first ) );
		Assert.IsTrue( guests.All( guest => guest.LastQueueWaitTurns > 0 ), "every guest has a completed wait in park turns" );
		Assert.AreEqual( 0, stalled );
		Assert.AreEqual( 0, rig.Ride.StalledAdmissionChecks );
	}

	[TestMethod]
	public void EveryQueueApproximationHasOneCodeTagAndDocumentation()
	{
		var ids = QueueApproximations.Entries.Select( entry => entry.Id ).ToList();
		Assert.AreEqual( ids.Count, ids.Distinct().Count() );
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !File.Exists( Path.Combine( root.FullName, "docs", "GUESTS.md" ) ) )
			root = root.Parent;
		if ( root == null )
			Assert.Inconclusive( "Repository sources are not next to the test binaries." );
		var tags = Directory.EnumerateFiles( Path.Combine( root!.FullName, "source", "OpenTPW" ), "*.cs", SearchOption.AllDirectories )
			.Where( path => !path.Contains( $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}" ) )
			.SelectMany( path => Regex.Matches( File.ReadAllText( path ), @"\[APPROX:(QUEUE-\d+)\]" ).Select( match => match.Groups[1].Value ) ).ToList();
		CollectionAssert.AreEquivalent( ids, tags, "one code tag per registered approximation" );
		var docs = File.ReadAllText( Path.Combine( root.FullName, "docs", "GUESTS.md" ) );
		foreach ( var id in ids )
			Assert.IsTrue( docs.Contains( $"| {id} |" ), $"{id} is documented" );
	}
}
