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
	internal sealed class Rig
	{
		public readonly GuestPathGrid Grid;
		public readonly RideVisitorBridge Ride;
		public readonly RideVM Script;
		public readonly GuestSimulation Simulation;
		public bool Open = true;

		public Rig( int queueCells = 5, QueueParameters? parameters = null, GuestNeeds satisfies = GuestNeeds.None, int capacity = 5, bool takesGuests = false, int excitement = 55, Action<GuestSettings>? configure = null, RideVisitorKind? kind = null, int gridSize = 12 )
		{
			Grid = new( gridSize, gridSize );
			for ( var x = 0; x < 10; x++ )
				Grid.SetPath( x, 0, true );
			Ride = new RideVisitorBridge( RideId, "Queue ride", kind ?? (satisfies == GuestNeeds.None ? RideVisitorKind.Ride : RideVisitorKind.Facility), capacity, excitement, 30, satisfies )
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
				// The queue line ends on the path beside the back (code 8), which connects the queue.
				QueuePaths.TryConnect( Grid, Ride );
			// the ride's entrance cell as a placement lays it (type 9, the link byte and the queue link toward the front queue cell, ObjectAccessCells.Lay): the called guest's search walks into it
			if ( queueCells > 0 )
			{
				Grid.Cells.SetType( 2, 6, ParkCellTypes.Entrance );
				Grid.Cells.SetLinks( 2, 6, GuestPathGrid.LinkValue( 0 ) );
				Grid.Cells.SetQueueLink( 2, 6, GuestPathGrid.LinkValue( 0 ) );
			}
			var settings = GuestTests.CreateSettings();
			configure?.Invoke( settings );
			Simulation = new GuestSimulation( Grid, settings, 7 ) { ArrivalsEnabled = false };
			Simulation.Register( Ride );
			Script.Advance( Tick );
		}

		/// <summary>
		/// The guests in the order they stand in the queue. Every guest walks the whole way to the back cell on its own (WALK-004), pushed apart by its neighbours, so the
		/// order in which a batch joins is not the order it was sent in.
		/// </summary>
		public static List<Guest> InQueueOrder( IEnumerable<Guest> guests ) => guests.OrderBy( guest => guest.QueuePosition < 0 ? int.MaxValue : guest.QueuePosition ).ToList();

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

		/// <summary>
		/// Keeps every guest's thirst, hunger and toilet at 0 after each tick, so that the needs (which grow on the needs phase and cost happiness at 100,
		/// ORACLE.md F25) never end a long wait: for tests about the queue rules over thousands of turns.
		/// </summary>
		public bool KeepNeedsEmpty { get; set; }

		public void Step()
		{
			Simulation.Tick( Tick );
			if ( KeepNeedsEmpty )
			{
				foreach ( var guest in Simulation.Guests )
					guest.Hunger = guest.Thirst = guest.Toilet = 0;
			}
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
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 2, 5 ) );
		Assert.AreEqual( GuestPathGrid.LinkValue( 2 ), grid.GetQueueLink( 2, 5 ), "the front cell links toward the ride entrance" );
		Assert.IsNull( ride.JoinCell, "a queue that touches no path is not connected" );
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
		Assert.IsNull( ride.JoinCell, "a back cell touching a path is not connected to it" );
		// The link bytes the queue line's link pass writes: the front toward the entrance, each cell and the one before it both ways.
		Assert.AreEqual( 0x10 | 0x01, grid.Cells.LinksAt( 2, 5 ) );
		Assert.AreEqual( 0x10 | 0x04, grid.Cells.LinksAt( 2, 4 ) );
		Assert.AreEqual( 0x10, grid.Cells.LinksAt( 3, 1 ), "the back links only to the cell before it" );
		Assert.AreEqual( 0x00, grid.Cells.LinksAt( 1, 4 ), "the stray queue cell is linked to nothing" );
		// A queue line ending on the path (code 8) links the path and the back cell: the queue is connected.
		Assert.AreEqual( QueueBuildResult.Connected, QueuePaths.TryExtend( grid, ride, 3, 0 ) );
		Assert.AreEqual( 0x11, grid.Cells.LinksAt( 3, 1 ) );
		Assert.AreEqual( 0x10, grid.Cells.LinksAt( 3, 0 ) & 0x10 );
		Assert.AreEqual( 6, ride.QueueSizeInCells, "connecting lays nothing" );
		Assert.AreEqual( (3, 0), ride.JoinCell, "guests step onto the back cell from the path" );
		Assert.AreEqual( 24, ride.QueueRoom );
		Assert.AreEqual( 21, ride.MaximumQueueLength, "min(4 × cells, the limit trunc(130 × 5 / 30) = 21)" );
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
		Assert.IsTrue( Can( 1, 3 ), "a queue goes over a path on every cell of a line ..." );
		Assert.IsFalse( Can( 1, 3, last: true ), "... except its last cell (data:0x84b2c set by the line tool)" );
		Assert.IsFalse( Can( 5, 3 ) || Can( 4, 1 ) );
	}

	[TestMethod]
	public void TheCellMoneyTestIs32BitAndANegativeCostFailsTheBalanceLoad()
	{
		// SetCellType 0x82bc8: balance − cost as a wrapping 32-bit difference, then the sign bit.
		var low = EconomyTestData.CellCostPark( int.MinValue, 20, 1 );
		Assert.IsTrue( low.CanSpendCell( CellPurchase.Queue ), "−2^31 − 1 wraps to 2^31 − 1" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, low.TrySpendCell( CellPurchase.Queue ) );

		// tp.exe's balance parser 0x00401D7C refuses a negative Costs.* value, which fails the balance load.
		Assert.ThrowsException<InvalidDataException>( () => EconomyTestData.CellCostPark( 100, 20, -5 ) );
		Assert.ThrowsException<InvalidDataException>( () => EconomyTestData.CellCostPark( 100, -1, 75 ) );
	}

	[TestMethod]
	public void TheQueuePreviewUsesTheValidatorAndTheCommitOnlySetCellType()
	{
		// The validator 0x84550: the 32-bit pending line cost is refused when greater than the balance; zero passes.
		Assert.IsFalse( EconomyTestData.CellCostPark( int.MinValue, 20, 1 ).CanPreviewCells( CellPurchase.Queue, 1 ) );
		Assert.IsTrue( EconomyTestData.CellCostPark( -5, 20, 0 ).CanPreviewCells( CellPurchase.Queue, 4 ), "a zero sum passes in the red" );
		var park = EconomyTestData.CellCostPark( 150, 20, 75 );
		Assert.IsTrue( park.CanPreviewCells( CellPurchase.Queue, 2 ) );
		Assert.IsFalse( park.CanPreviewCells( CellPurchase.Queue, 3 ), "225 > 150" );
		Assert.IsTrue( park.CanPreviewLine( unchecked( 0x1_0000_0000L ) ), "the sum is 32-bit: 2^32 wraps to 0" );

		// Balance −2^31 with cost 1: the preview refuses the cell, the commit (SetCellType's test only) lays and charges it.
		var rig = new Rig( queueCells: 0 );
		var low = EconomyTestData.CellCostPark( int.MinValue, 20, 1 );
		Assert.AreEqual( QueueBuildResult.Refused, Level.CheckQueueCell( rig.Grid, low, rig.Ride, 2, 5, Array.Empty<(int X, int Y)>(), ( _, _ ) => false ) );
		Assert.AreEqual( QueueBuildResult.Ok, Level.BuildQueueCell( rig.Grid, low, rig.Ride, 2, 5, ( _, _ ) => false, out _ ) );
		Assert.AreEqual( 1, rig.Grid.QueueCellCount );
		Assert.AreEqual( (long)int.MinValue - 1, low.Balance );
	}

	[TestMethod]
	public void UnbounceNeedsAPassedDeadlineInsideTheFirst200MsOfEachSecond()
	{
		// UNBOUNCE 0xae020 on the oracle's edge inputs: (visitor, deadline, start) slots in index order.
		static int Take( double now, params (int Guest, double Until, double Start)[] slots ) => RideVisitorBridge.TakeUnbounced( slots.ToList(), now, force: false );
		Assert.AreEqual( 0, Take( 31000, (7, 31000, 1000) ), "deadline < now is strict" );
		Assert.AreEqual( 7, Take( 31001, (7, 31000, 1000) ) );
		Assert.AreEqual( 7, Take( 31199, (7, 31000, 1000) ), "(30199 mod 1000) / 200 = 0" );
		Assert.AreEqual( 0, Take( 31200, (7, 31000, 1000) ), "(30200 mod 1000) / 200 = 1: outside the 200 ms window" );
		Assert.AreEqual( 0, Take( 31950, (7, 31000, 1000) ) );
		Assert.AreEqual( 7, Take( 32000, (7, 31000, 1000) ), "the next second opens the window again" );
		Assert.AreEqual( 5, Take( 1000, (5, 900, 1150) ), "the C remainder keeps the sign: −150 / 200 = 0" );
		Assert.AreEqual( 0, Take( 1000, (5, 900, 1250) ), "−250 / 200 = −1" );
		Assert.AreEqual( 9, Take( 50, (0, 0, 0), (9, 40, 49), (8, 10, 50) ), "a free slot is skipped" );
		Assert.AreEqual( 9, Take( 1049, (9, 40, 49), (8, 10, 49) ), "index order, not deadline order" );
		var slots = new List<(int Guest, double Until, double Start)> { (0, 0, 0), (4, 1e12, 0), (6, 1e12, 0) };
		Assert.AreEqual( 4, RideVisitorBridge.TakeUnbounced( slots, 0, force: true ), "FORCEUNBOUNCE takes the first occupied slot of the window (inside it: time 0 - start 0 = 0)" );
	}

	[TestMethod]
	public void ForceUnbounceDropsTheDeadlineTestButKeepsTheWindow()
	{
		// FORCEUNBOUNCE 0x00555842 is UNBOUNCE 0x005557A3 without the deadline compare (0x00555811): the 200 ms window of each second since the bounce (0x005558A4..0x005558B7) still gates it
		static int Force( double now, params (int Guest, double Until, double Start)[] slots ) => RideVisitorBridge.TakeUnbounced( slots.ToList(), now, force: true );
		Assert.AreEqual( 7, Force( 1100, (7, 1e12, 1000) ), "a deadline far ahead does not matter inside the window" );
		Assert.AreEqual( 0, Force( 1200, (7, 1e12, 1000) ), "(200 mod 1000) / 200 = 1: outside the window" );
		Assert.AreEqual( 0, Force( 1900, (7, 0, 1000) ), "a passed deadline does not open the window" );
		Assert.AreEqual( 7, Force( 2000, (7, 1e12, 1000) ), "the next second opens it again" );
		Assert.AreEqual( 8, Force( 1300, (7, 1e12, 1000), (8, 1e12, 1300) ), "index order among the entries inside their windows: entry 7 is outside its window, entry 8 inside its own" );
	}

	[TestMethod]
	public void BounceFillsTheFirstFreeSlotOfTheScriptsTable()
	{
		// BOUNCE 0xadf40: the first slot with visitor 0 of the header's bounce-size table; a full table stores nothing.
		var slots = new List<(int Guest, double Until, double Start)>();
		Assert.IsTrue( RideVisitorBridge.TakeBounceSlot( slots, 3, 1, 5000, 0 ) );
		Assert.IsTrue( RideVisitorBridge.TakeBounceSlot( slots, 3, 2, 9000, 100 ) );
		Assert.IsTrue( RideVisitorBridge.TakeBounceSlot( slots, 3, 3, 5300, 300 ) );
		Assert.IsFalse( RideVisitorBridge.TakeBounceSlot( slots, 3, 4, 9999, 400 ), "every slot is taken" );
		Assert.AreEqual( 3, slots.Count );
		// Slot 1 is still held; slot 0 (start 0) and slot 2 (start 300) are past their deadlines. At 6000 only slot 0
		// is inside its window, at 6300 slot 2 is.
		Assert.AreEqual( 1, RideVisitorBridge.TakeUnbounced( slots, 6000, force: false ) );
		Assert.AreEqual( 3, RideVisitorBridge.TakeUnbounced( slots, 6300, force: false ) );
		CollectionAssert.AreEqual( new[] { 0, 2 }, slots.Select( slot => slot.Guest ).ToArray(), "slot 0 stays a free hole; the free end is trimmed" );
		Assert.IsTrue( RideVisitorBridge.TakeBounceSlot( slots, 3, 5, 12000, 7000 ) );
		CollectionAssert.AreEqual( new[] { 5, 2 }, slots.Select( slot => slot.Guest ).ToArray(), "the new visitor takes the hole, not the end" );
		// Release order follows the slot index: 5 (slot 0) leaves before 2 (slot 1) when both are due.
		Assert.AreEqual( 5, RideVisitorBridge.TakeUnbounced( slots, 13000, force: false ) );
		Assert.AreEqual( 2, RideVisitorBridge.TakeUnbounced( slots, 13100, force: false ) );
		Assert.AreEqual( 0, slots.Count );
		Assert.IsFalse( RideVisitorBridge.TakeBounceSlot( slots, 0, 6, 1, 0 ), "a script without bounce slots bounces nobody" );
	}

	[TestMethod]
	public void TheBridgeHashCoversEveryFieldOfABounceSlot()
	{
		static ulong Hash( params (int Guest, double Until, double Start)[] slots )
		{
			var bridge = new RideVisitorBridge( RideId, "Hash ride", RideVisitorKind.Ride, 5, 55, 30 );
			var field = (List<(int Guest, double Until, double Start)>)typeof( RideVisitorBridge )
				.GetField( "bouncing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!.GetValue( bridge )!;
			field.AddRange( slots );
			var hash = new StateHasher();
			bridge.AddCanonicalState( hash );
			return hash.Value;
		}
		var baseline = Hash( (3, 5000, 1000) );
		Assert.AreNotEqual( baseline, Hash( (4, 5000, 1000) ), "visitor" );
		Assert.AreNotEqual( baseline, Hash( (3, 5001, 1000) ), "deadline" );
		Assert.AreNotEqual( baseline, Hash( (3, 5000, 1001) ), "start (the UNBOUNCE window's origin)" );
		Assert.AreNotEqual( Hash( (0, 0, 0), (3, 5000, 1000) ), baseline, "a free slot before an occupied one" );
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
		Assert.AreEqual( 100, park.ScrapPercent( ride! ), "a ride nobody used, in its first 30 days (0xE2424: 100)" );
		Assert.AreEqual( 75, park.RefundQueueCell( ride!.Id ) );
		Assert.AreEqual( before + 75, park.Balance );
		park.RecordRideUse( ride.Id ); park.RecordUseFinished( ride.Id );
		Assert.AreEqual( 50, park.ScrapPercent( ride ), "year-0 scrap percentage once the ride has been used" );
		Assert.AreEqual( 75 * 50 / 100, park.RefundQueueCell( ride.Id ) );
		Assert.AreEqual( before + 75 + 37, park.Balance );
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
		// A long queue: the original tests no cell count (QUEUE-013 retired), so 30 cells are laid; only the back-of-queue walk's 1,000 bound ends a queue.
		var grid = new GuestPathGrid( 40, 40 );
		var ride = new RideVisitorBridge( 11, "Long", RideVisitorKind.Ride, 1, 0, 10 ) { EntranceCell = (0, 0), QueueFrontCell = (0, 1), QueueEntranceDirection = 0, Parameters = rig.Ride.Parameters };
		Assert.AreEqual( 30, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 1, 30 ).Select( y => (0, y) ) ) );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.CheckExtend( grid, ride, 0, 31 ), "no 25-cell cap" );
		Assert.AreEqual( 30, ride.QueueSizeInCells );
		Assert.AreEqual( 21, ride.MaximumQueueLength, "room 100, but the HasQueue limit trunc(130 × 5 / 30) = 21 decides" );
	}

	// ---- Joining ---------------------------------------------------------------------------------

	[TestMethod]
	public void QueueLimitFollowsTheTracedFormula()
	{
		static uint Limit( bool hasQueue, float qwtc, int speed, int initSpeed, int capacity, int duration ) =>
			RideVisitorBridge.ComputeQueueLimit( new QueueParameters( hasQueue, false, false, qwtc, speed, initSpeed, capacity, duration ) );
		Assert.AreEqual( 100u, Limit( false, 130, 60, 60, 5, 30 ), "no Info.HasQueue (shops, toilets, sideshows): 100, the QueueWaitTimeConstant is unused" );
		Assert.AreEqual( 21u, Limit( true, 130, 60, 60, 5, 30 ), "Belly Bounce: trunc(130 × (1 × 5) / 30) = trunc(21.67)" );
		Assert.AreEqual( 10u, Limit( true, 130, 30, 60, 5, 30 ), "G = SPEED / InitSpeed = 0.5: trunc(10.83)" );
		Assert.AreEqual( 20u, Limit( true, 30, 0, 60, 2, 3 ), "SPEED 0 gives G = 1 (Rides.sam level 0: QWTC 30, CAP 2, DUR 3)" );
		Assert.AreEqual( 4u, Limit( true, 10, 60, 60, 1, 30 ), "floor of 4" );
		Assert.AreEqual( 4u, Limit( true, 0, 0, 0, 0, 0 ), "a NaN quotient becomes 4" );
		Assert.AreEqual( uint.MaxValue, Limit( true, 30, 0, 60, 2, 0 ), "DUR 0 divides to +infinity, which saturates at 2^32 − 1; the room still bounds the queue" );
		Assert.AreEqual( 3_000_000_000u, Limit( true, 3e9f, 0, 60, 1, 1 ), "the limit is unsigned: quotients up to 2^32 − 1 survive" );
		Assert.AreEqual( 465_288_096u, Limit( true, 130, unchecked( (int)0x80000000 ), 100, 5, 30 ), "SPEED is an unsigned word: 2^31 / 100 × 5 × 130 / 30" );
		Assert.AreEqual( 12u, Limit( true, 3, 65, 90, 6, 1 ), "single precision: 3 × (65/90 × 6) / 1 = 12.999999f (13 in double)" );
	}

	[TestMethod]
	public void TheExcitementRefusalGatesOnTheLevelByteAtFortyFive()
	{
		// The original's 0xe9a84 difference is the ride minus the guest's preference (QUEUE-004, ORACLE.md F16): below it is "not exciting enough" (thought 12, Bored), above "too exciting" (thought 15, Scared).
		Assert.IsNull( QueueJoinRules.Refusal( 60, 44 ), "|44| passes" );
		Assert.IsNull( QueueJoinRules.Refusal( 60, -44 ) );
		Assert.AreEqual( QueueJoinResult.NotExcitingEnough, QueueJoinRules.Refusal( 60, -45 ), "the ride is 45 below the preference" );
		Assert.AreEqual( QueueJoinResult.TooExciting, QueueJoinRules.Refusal( 60, 45 ), "45 above" );
		Assert.IsNull( QueueJoinRules.Refusal( 0, 90 ), "an ExcitementLevel of 0 is not gated" );
		Assert.IsNull( QueueJoinRules.Refusal( 256, 90 ), "only the low byte of the word counts" );
		Assert.AreEqual( QueueJoinResult.TooExciting, QueueJoinRules.Refusal( 1, 100 ), "the relief difference 100 is too exciting" );
		// The gate follows the data, not the kind: a shop with a level is gated, a ride without one is not.
		foreach ( var (kind, level, gated) in new[] { (RideVisitorKind.Shop, 60, true), (RideVisitorKind.Facility, 60, true), (RideVisitorKind.Ride, 60, true), (RideVisitorKind.Ride, 0, false), (RideVisitorKind.Sideshow, 0, false) } )
		{
			var bridge = new RideVisitorBridge( 1, "Gate", kind, 1, level, 10 ) { Parameters = new QueueParameters( true, false, false, 130, 60, 60, 1, 30 ) };
			Assert.AreEqual( gated, bridge.ExcitementRefusal( 0 ) is not null, $"{kind} with level {level}, a guest preferring 0" );
		}
	}

	[TestMethod]
	public void JoiningChecksRoomThenExcitementThenTheLimit()
	{
		// One queue cell: room 4.
		var rig = new Rig( queueCells: 1 );
		var ride = rig.Ride;
		// The rig's ride has excitement 55 at its design speed and duration; the join takes the guest's preferred excitement.
		Assert.AreEqual( QueueJoinResult.NotExcitingEnough, ride.JoinQueue( 1, 100 ), "|difference| ≥ 45, ride below the guest's preference" );
		Assert.AreEqual( QueueJoinResult.TooExciting, ride.JoinQueue( 1, 10 ) );
		Assert.AreEqual( QueueJoinResult.Joined, ride.JoinQueue( 1, 99 ) );
		Assert.AreEqual( QueueJoinResult.AlreadyQueued, ride.JoinQueue( 1 ) );
		Assert.IsTrue( new[] { 2, 3, 4 }.All( ride.TryJoinQueue ) );
		Assert.AreEqual( QueueJoinResult.NoRoom, ride.JoinQueue( 5, 100 ), "the room check comes before the excitement gate" );

		// HasQueue with QWTC 10, CAP 1, DUR 30: two cells give room 8, but the QWTC limit is 4 (the floor).
		var shop = new Rig( queueCells: 0, parameters: new QueueParameters( true, false, false, 10, 60, 60, 1, 30 ) );
		shop.Grid.SetQueue( 2, 5, 2 );
		shop.Grid.SetQueue( 2, 4, 2 );
		shop.Ride.RecomputeQueue( shop.Grid );
		Assert.AreEqual( (8, 4u, 4), (shop.Ride.QueueRoom, shop.Ride.QueueLimit, shop.Ride.MaximumQueueLength) );
		Assert.IsTrue( new[] { 1, 2, 3, 4 }.All( shop.Ride.TryJoinQueue ) );
		Assert.AreEqual( QueueJoinResult.QueueTooLong, shop.Ride.JoinQueue( 5 ) );

		// HasQueue with QWTC 600 and 26 cells (laid directly on the grid): room 104, limit trunc(600 × 5 / 30) = 100.
		var grid = new GuestPathGrid( 30, 30 );
		for ( var y = 1; y <= 26; y++ )
			grid.SetQueue( 1, y, 0 );
		var big = new RideVisitorBridge( 4, "Big", RideVisitorKind.Ride, 5, 50, 10 ) { EntranceCell = (1, 0), QueueFrontCell = (1, 1), QueueEntranceDirection = 0, Parameters = rig.Ride.Parameters with { QueueWaitTimeConstant = 600 } };
		big.Attach( IdleScript( UnimplementedRideScriptEffects.Instance ), () => true );
		big.RecomputeQueue( grid );
		Assert.AreEqual( 26, big.QueueSizeInCells );
		Assert.AreEqual( (104, 100u, 100), (big.QueueRoom, big.QueueLimit, big.MaximumQueueLength) );
		Assert.IsTrue( Enumerable.Range( 1, 100 ).All( big.TryJoinQueue ) );
		Assert.AreEqual( QueueJoinResult.QueueTooLong, big.JoinQueue( 101 ) );
		Assert.AreEqual( 99, big.GetQueuePosition( 100 ) );
	}

	[TestMethod]
	public void AFullQueueRoomTurnsAGuestAwayWithoutAThoughtOrAHappinessLoss()
	{
		// tp.exe 0x004FFC4F: the room test fails, so event 0x15 and state 6 only (GUEST-004); the queue-too-long refusal thinks 16 and pushes the
		// history, and neither changes happiness (0x004FFBC0 calls 0x004FEA70 only for a stuck walk or a closed park).
		var rig = new Rig { KeepNeedsEmpty = true };
		Assert.IsTrue( Enumerable.Range( 100, rig.Ride.QueueRoom ).All( rig.Ride.TryJoinQueue ), "the room is full" );
		var guest = rig.Send();
		for ( var turns = 0; guest.State == GuestState.GoingToRide && turns < 60; turns++ )
			rig.RunTurns( 1 );   // the guest first walks to the back cell (WALK-004)
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( GuestThought.None, guest.Thought, "no thought" );
		Assert.AreEqual( 60f, guest.Happiness, "no happiness change" );
		Assert.IsTrue( guest.TemporaryHistory.All( id => id == 0 ), "no history push" );
		Assert.AreEqual( RideId, guest.AttractionId, "a full-room refusal keeps the target until the next major choice" );

		var shop = new Rig( parameters: new QueueParameters( true, false, false, 10, 60, 60, 1, 30 ) ) { KeepNeedsEmpty = true };
		Assert.IsTrue( Enumerable.Range( 100, (int)shop.Ride.QueueLimit ).All( shop.Ride.TryJoinQueue ), "the queue is at its limit" );
		Assert.IsTrue( shop.Ride.QueueLength < shop.Ride.QueueRoom, "with room to spare" );
		var turned = shop.Send();
		for ( var turns = 0; turned.State == GuestState.GoingToRide && turns < 60; turns++ )
			shop.RunTurns( 1 );
		Assert.AreEqual( GuestState.DoingNothing, turned.State );
		Assert.AreEqual( GuestThought.QueueTooLong, turned.Thought, "thought 16" );
		Assert.AreEqual( 60f, turned.Happiness, "no happiness change" );
		Assert.AreEqual( RideId, turned.TemporaryHistory[0], "the history push" );
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
		guests = Rig.InQueueOrder( guests );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ), string.Join( ", ", guests.Select( guest => guest.State ) ) );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 6 ).ToArray(), guests.Select( guest => guest.QueuePosition ).ToArray() );
		// Positions 0..3 stand in the front cell, 4 and 5 in the second cell; everyone walked in over the back cells.
		// (a guest standing a hair past the cell edge of its slot, which is 255/256 deep, is counted in the neighbouring cell: the slots below are asserted exactly)
		CollectionAssert.AreEqual( new[] { (2, 5), (2, 5), (2, 5), (2, 5) }, guests.Take( 4 ).Select( guest => guest.Cell ).ToArray() );
		Assert.IsTrue( guests.Skip( 4 ).All( guest => guest.Cell is (2, 4) or (2, 5) ), string.Join( " ", guests.Skip( 4 ).Select( guest => guest.Cell ) ) );
		Assert.IsTrue( visited.SetEquals( new[] { (2, 5), (2, 4), (2, 3), (2, 2), (2, 1) } ), string.Join( " ", visited ) );
		Assert.IsTrue( guests.All( guest => rig.Grid.IsQueue( guest.Cell.X, guest.Cell.Y ) ) );
		// Depth 0/63/127/191 from the front edge (toward the ride at +Y), a small sideways offset, in bytes of 1/256 cell
		// (0xe68cc; WALK-I, was /255 with the guest snapped onto the point). The traced steering arrives within 0.32 cell
		// (octile) of the slot and the arrival step still moves, so a guest stands within 0.32 cell + one step of it.
		var expectedY = new[] { 5f + 255 / 256f, 5f + 192 / 256f, 5f + 128 / 256f, 5f + 64 / 256f, 4f + 255 / 256f, 4f + 192 / 256f };
		for ( var index = 0; index < guests.Count; index++ )
		{
			var guest = guests[index];
			Assert.AreEqual( expectedY[index], guest.QueueTargetY, 0f, $"position {index}" );
			Assert.IsTrue( Math.Abs( guest.QueueTargetX - 2.5f ) <= 14 / 256f, $"lateral offset {guest.QueueTargetX}" );
			var distance = MathF.Sqrt( (guest.X - guest.QueueTargetX) * (guest.X - guest.QueueTargetX) + (guest.Y - guest.QueueTargetY) * (guest.Y - guest.QueueTargetY) );
			Assert.IsTrue( distance < (GuestNavigation.ArrivalRadius + guest.MaxSpeed) / (float)Fixed16.One, $"position {index} stands {distance} cell from its slot" );
		}
		Assert.AreEqual( 6, rig.Ride.QueueLength );
		Assert.AreEqual( 6, rig.Simulation.GetStatistics().Queueing );
	}

	/// <summary>The leaving test only reads the closing word for guests that decide (state 6) and for guests walking to a ride (state 10); the park door
	/// closes the rides (<c>ParkGate.DoorMoved</c>, tests set <c>Rig.Open</c>), whose update dismisses the queue head one guest per turn (GUEST-011).</summary>
	internal sealed class ClosedPayments : IGuestPayments
	{
		public bool Closed;
		public int AdmissionFee => 0;
		public bool ParkClosed => Closed;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
	}

	private sealed class ReliefPayments : IGuestPayments
	{
		public readonly List<int> Relief = new();
		public int AdmissionFee => 0;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
		public void ReliefUsed( IRideVisitorBridge attraction, int toiletByte ) => Relief.Add( toiletByte );
	}

	[TestMethod]
	public void AReliefVisitWearsTheToiletAndClearsHighIllness()
	{
		// tp.exe 0x004FE1E0: the toilet wears by the guest's toilet meter as a byte, the meter goes to 0, an illness byte above 90 to 0
		var rig = new Rig( satisfies: GuestNeeds.Toilet, takesGuests: true, capacity: 1 );
		var payments = new ReliefPayments();
		rig.Simulation.Payments = payments;
		var guest = rig.Send();
		var deadline = rig.Simulation.ParkTurn + 400;
		while ( payments.Relief.Count == 0 && rig.Simulation.ParkTurn < deadline )
		{
			guest.Toilet = 77.9f;
			guest.Nausea = 95.5f;
			rig.Step();
		}
		CollectionAssert.AreEqual( new[] { 77 }, payments.Relief );
		Assert.AreEqual( 0f, guest.Toilet );
		Assert.AreEqual( 0f, guest.Nausea );
	}

	private static bool GoingHome( Guest guest ) => guest.State is GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.Gone;

	[TestMethod]
	public void ClosingTheParkDismissesTheQueueHeadOfTheClosedRideOnePerTurnAndDropsGoingToRideReservations()
	{
		// tp.exe 0x00519EF0 closes the rides (0x004DF300: open word 0); the closed ride's update runs the admission 0x004E1220, which only runs the head step
		// 0x004E0450 (unlink, dismissal 0x005012F0: the medium drop, state 6) once per update, and state 6 sends the guest home when the park is closed
		var rig = new Rig();
		rig.HoldAdmission();
		var queued = Enumerable.Range( 0, 3 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		queued = Rig.InQueueOrder( queued );
		Assert.IsTrue( queued.All( guest => guest.State == GuestState.Queueing ), string.Join( ", ", queued.Select( guest => guest.State ) ) );
		Assert.AreEqual( 3, rig.Ride.QueueLength );
		var happiness = queued.Select( guest => guest.Happiness ).ToList();

		var payments = new ClosedPayments();
		rig.Simulation.Payments = payments;
		payments.Closed = true;
		rig.RunTurns( 1 );
		Assert.AreEqual( 3, rig.Ride.QueueLength, "closing the park alone pulls nobody out: the rides are still open" );
		Assert.IsTrue( queued.All( guest => guest.State == GuestState.Queueing ) );

		rig.Open = false;   // the park door's close walk
		for ( var gone = 1; gone <= 3; gone++ )
		{
			rig.RunTurns( 1 );
			Assert.AreEqual( 3 - gone, rig.Ride.QueueLength, $"{gone} turns after the ride closed: one head per update" );
			Assert.IsTrue( queued.Take( gone ).All( guest => !guest.IsInQueue ), "the head leaves first" );
			Assert.IsTrue( queued.Skip( gone ).All( guest => guest.State == GuestState.Queueing ), "the others keep their places" );
		}
		Assert.AreEqual( 3, rig.Ride.Dismissed );
		rig.RunTurns( 60 );
		Assert.IsTrue( queued.All( GoingHome ), string.Join( ", ", queued.Select( guest => guest.State ) ) );
		var big = rig.Simulation.Settings.BigHappinessChange;
		var medium = rig.Simulation.Settings.MediumHappinessChange;
		Assert.IsTrue( queued.Select( ( guest, index ) => (guest, index) ).All( pair => pair.guest.Happiness <= Math.Max( 0, happiness[pair.index] - medium - big ) + 0.01f ),
			"the dismissal's medium drop, then the leaving gate's big drop" );

		// A guest still on its way drops the reservation (state 10, 0x004FFEB9) instead of walking up to a ride that will not take it.
		var walker = rig.Send();
		walker.Happiness = 90;
		Assert.AreEqual( GuestState.GoingToRide, walker.State );
		rig.RunTurns( 1 );
		Assert.AreEqual( 0, walker.AttractionId, "the reservation is dropped" );
		Assert.IsTrue( walker.State is GuestState.DoingNothing || GoingHome( walker ), $"{walker.State}" );
		Assert.IsTrue( walker.Happiness <= 100 - big + 0.01f, $"the big drop at 0x004FEA70(2) (the needs phase may have added up to the cap first): {walker.Happiness}" );
		rig.RunTurns( 5 );
		Assert.IsTrue( GoingHome( walker ), $"state 6 sends it home: {walker.State}" );
	}

	/// <summary>
	/// A guest that was called and walks to the stand point (Boarding) is still the queue head when the ride closes (the close 0x004DF300 withdraws the call, +0x6C = 0): the closed
	/// ride's head step dismisses it like any other head, or it would stand at a ride that no longer takes anyone.
	/// </summary>
	[TestMethod]
	public void ClosingTheRideDismissesAGuestCalledToBoardAsTheQueueHead()
	{
		// QWTC 600 with one guest per take: the first guest is called early and walks to the stand point (Boarding)
		// over several turns, so the closing turn catches it mid-walk, before the script can take it.
		var rig = new Rig( takesGuests: true, capacity: 1, parameters: new QueueParameters( true, false, false, 600, 60, 60, 1, 30 ) );
		var guests = Enumerable.Range( 0, 2 ).Select( _ => rig.Send() ).ToList();
		Guest? boarding = null;
		var deadline = rig.Simulation.ParkTurn + 400;
		while ( boarding == null && rig.Simulation.ParkTurn < deadline )
		{
			rig.Step();
			boarding = guests.FirstOrDefault( guest => guest.State == GuestState.Boarding );
		}
		Assert.IsNotNull( boarding, "a called guest was on its way to the stand point" );
		Assert.AreEqual( 0, rig.Ride.GetQueuePosition( boarding.Id ), "the boarding guest is still held in the queue list" );

		var payments = new ClosedPayments();
		rig.Simulation.Payments = payments;
		payments.Closed = true;
		rig.Open = false;
		rig.RunTurns( 2 );
		Assert.IsFalse( boarding.IsInQueue, $"{boarding.State}" );
		Assert.AreEqual( -1, rig.Ride.GetQueuePosition( boarding.Id ), "the closed ride dismissed it from the queue" );
		Assert.AreNotEqual( boarding.Id, rig.Ride.CalledGuest, "the call was withdrawn" );
		rig.RunTurns( 60 );
		Assert.IsTrue( GoingHome( boarding ), $"state 6 sends it home: {boarding.State}" );
	}

	/// <summary>
	/// Review WV-13: a guest standing on the join cell keeps the last queue cell it stood on in QueueCellIndex (here cell
	/// 1, as after cutting a corner into a folded queue and drifting back). A redirect must start its route at the join
	/// cell and go over the back cell; a route from the stale index skips cells that are walls for the walk, and the
	/// guest never arrives.
	/// </summary>
	[TestMethod]
	public void ARedirectFromTheJoinCellStartsBehindTheBackCell()
	{
		var rig = new Rig();
		rig.HoldAdmission();
		var guest = rig.Send();
		rig.RunTurns( 60 );
		Assert.AreEqual( GuestState.Queueing, guest.State );
		Assert.AreEqual( 0, guest.QueuePosition );
		// Put it on the join cell (2, 0) with a stale cell index and a recorded position the list no longer has.
		var join = rig.Ride.JoinCell!.Value;
		(guest.Navigation.X, guest.Navigation.Y) = GuestNavigation.BytePoint( join, 127, 191 );
		(guest.Navigation.VelocityX, guest.Navigation.VelocityY) = (0, 0);
		(guest.CellX, guest.CellY) = join;
		guest.QueueCellIndex = 1;
		guest.QueuePosition = 3;
		guest.QueueMoveDelay = 0;
		var start = rig.Simulation.ParkTurn;
		rig.RunTurns( 2 );
		Assert.AreEqual( GuestState.MovingUpQueue, guest.State, "the position changed: walk to slot 0" );
		var route = guest.Navigation.Route;
		CollectionAssert.AreEqual( new[] { join, (2, 1), (2, 2), (2, 3), (2, 4), (2, 5) }, route, string.Join( " ", route ) );
		rig.RunTurns( 100, () => Assert.IsTrue( guest.State is GuestState.MovingUpQueue or GuestState.Queueing, $"{guest.State}" ) );
		Assert.AreEqual( GuestState.Queueing, guest.State, "arrived at slot 0" );
		Assert.AreEqual( (2, 5), guest.Cell );
		Assert.IsTrue( rig.Simulation.ParkTurn - start <= 102 );
	}

	[TestMethod]
	public void QueueSlotsFollowTheTracedDepthBytes()
	{
		CollectionAssert.AreEqual( new[] { (0, 0), (0, 63), (0, 127), (0, 191), (1, 0), (1, 63), (2, 127) },
			new[] { 0, 1, 2, 3, 4, 5, 10 }.Select( position => GuestSimulation.QueueSlot( position, 3 ) ).ToArray() );
		Assert.AreEqual( (0, 191), GuestSimulation.QueueSlot( 7, 1 ), "beyond the last cell the walk stops at the terminator" );
	}

	[TestMethod]
	public void AGuestWaitingItsMoveUpDelayStillRunsTheNeedsWindow()
	{
		// tp.exe 0x005002EB: the delay decrement falls through to the needs window (0x005002F2), so an unhappy guest leaves meanwhile
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		guests = Rig.InQueueOrder( guests );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ) );
		var waiting = guests[5];
		Assert.IsTrue( waiting.QueueMoveDelay > 1 );
		rig.Ride.LeaveQueue( guests[0].Id );
		waiting.Happiness = 0;
		rig.RunTurns( 2 );
		Assert.IsFalse( waiting.IsInQueue, "the unhappy guest left while waiting its delay" );
	}

	[TestMethod]
	public void GuestsMoveUpAfterTruncOneTwoTimesPositionTurnsWhenTheGapIsSmall()
	{
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		guests = Rig.InQueueOrder( guests );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ) );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 6 }, guests.Select( guest => guest.QueueMoveDelay ).ToArray(), "trunc(1.2 × position)" );

		// The front guest leaves: everyone's gap is 1, so each waits out its delay before walking.
		rig.Ride.LeaveQueue( guests[0].Id );
		var removedTurn = rig.Simulation.ParkTurn;
		var moved = new Dictionary<int, long>();
		rig.RunTurns( 1 );
		Assert.IsFalse( guests[0].IsInQueue, "the removed guest left the queue" );
		rig.RunTurns( 11, () =>
		{
			// it may choose the ride again and rejoin at the back within these turns
			foreach ( var guest in guests.Skip( 1 ).Where( guest => guest.State == GuestState.MovingUpQueue ) )
				moved.TryAdd( guest.Id, rig.Simulation.ParkTurn - removedTurn );
		} );
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
		var rig = new Rig { KeepNeedsEmpty = true };
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
		Assert.IsTrue( calm.Concat( happy ).All( guest => rig.Simulation.QueueWaitTurns( guest ) >= 1980 ) );
	}

	[TestMethod]
	public void AQueueingGuestWhoseCounterPassesZeroNeverLeavesByIt()
	{
		// ORACLE.md F26: the counter is tested for equality in state 6 only; a guest standing in the queue while it reads 0 keeps a negative counter.
		var rig = new Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		var guest = rig.Send( 60 );
		guest.ExitLevel = 3;
		rig.RunTurns( 400, () => { } );
		Assert.IsTrue( guest.IsInQueue, "still in the queue" );
		Assert.IsTrue( guest.ExitLevel < 0, $"the counter ran on below 0: {guest.ExitLevel}" );
	}

	// ---- Leaving ---------------------------------------------------------------------------------

	[TestMethod]
	public void UnhappyGuestsLeaveOutsideTheThirtyTurnWindow()
	{
		var rig = new Rig();
		rig.HoldAdmission();
		var guest = rig.Send( 5 );
		guest.BaseSpeed = 140; // the fastest walker reaches its queue position well inside the window, so the window alone decides
		long leftAt = -1;
		var leftCell = (-1, -1);
		var thought = GuestThought.None;
		var leftHappiness = -1f;
		rig.RunTurns( 40, () =>
		{
			if ( leftAt >= 0 || guest.IsInQueue || guest.State == GuestState.GoingToRide )
				return;
			leftAt = rig.Simulation.ParkTurn;
			leftCell = guest.Cell;
			thought = guest.Thought;
			leftHappiness = guest.Happiness;
		} );
		Assert.AreEqual( GuestThought.Unhappy, thought );
		Assert.AreEqual( GuestSimulation.NeedsWindowTurns + 1, leftAt, "turn − +520 must exceed 30" );
		Assert.AreEqual( 0f, leftHappiness, "the dismissal's medium step (0x4fea70(1)) clamps at 0" );
		// [BIN:TPW-EXE:0x005004AA queue exit position] the exit writes no position: the guest stands on the queue cell it was on (QUEUE-010)
		Assert.IsTrue( rig.Grid.IsQueue( leftCell.Item1, leftCell.Item2 ), $"it stays on its queue cell: {leftCell}" );
	}

	[TestMethod]
	public void EveryStateElevenExitGoesThroughTheDismissal()
	{
		// tp.exe 0x5004aa: the state-11 exits set their thought, then unlink and dismiss (0x5012f0), which lowers happiness by the medium step (0x4fea70(1)).
		foreach ( var exit in new[] { GuestThought.Unhappy, GuestThought.NeedToilet, GuestThought.Bored, GuestThought.QueueTooLong, GuestThought.None } )
		{
			var rig = new Rig();
			rig.HoldAdmission();
			var guests = Enumerable.Range( 0, exit == GuestThought.QueueTooLong ? 6 : 1 ).Select( _ => rig.Send( exit == GuestThought.Unhappy ? 15 : 60 ) ).ToList();
			var guest = guests[^1];
			guest.BaseSpeed = 140;
			rig.KeepNeedsEmpty = exit != GuestThought.NeedToilet;
			rig.RunTurns( exit == GuestThought.QueueTooLong ? 120 : 20 );
			guest = Rig.InQueueOrder( guests )[^1];   // the last guest to join has the highest place
			Assert.IsTrue( guest.IsInQueue, $"{exit}: queued before the exit" );
			// A smaller QueueWaitTimeConstant lowers the limit to its floor of 4, below the last guest's position 5 (QueueLimitFollowsTheTracedFormula).
			if ( exit == GuestThought.QueueTooLong )
				rig.Ride.Parameters = new QueueParameters( true, false, false, 1, 60, 60, 5, 30 );
			var medium = rig.Simulation.Settings.MediumHappinessChange;
			var before = guest.Happiness;
			var thought = (GuestThought?)null;
			var after = 0f;
			rig.RunTurns( 200, () =>
			{
				if ( thought != null )
					return;
				switch ( exit )
				{
					case GuestThought.Unhappy: guest.Happiness = Math.Min( guest.Happiness, 9 ); break;
					case GuestThought.NeedToilet: guest.Toilet = 90; break;
					case GuestThought.Bored: guest.InterludeTurn = rig.Simulation.ParkTurn; break;
					case GuestThought.QueueTooLong: break;
					case GuestThought.None: rig.Ride.LeaveQueue( guest.Id ); break;   // "Problem with a queue": a guest missing from the list
				}
				if ( guest.IsInQueue )
				{
					before = guest.Happiness;
					return;
				}
				thought = guest.Thought;
				after = guest.Happiness;
			} );
			Assert.AreEqual( exit, thought ?? GuestThought.Confused, $"{exit}: the exit's own thought" );
			Assert.AreEqual( Math.Max( 0, before - medium ), after, 0.01f, $"{exit}: one medium step in the dismissal" );
		}
	}

	[TestMethod]
	public void QueueFacingChangesOnlyInsideTheWindowBySpreadAngles()
	{
		// tp.exe 0x004FFFF0: beyond 30 turns from the last interlude every needs path returns, so a guest that keeps queueing never
		// turns; inside the window a draw mod 10 = 0 turns the heading by (second draw mod 800) − 400 of 2048.
		static int Turned( (float X, float Y) before, (float X, float Y) after ) =>
			(int)MathF.Round( MathF.Atan2( before.X * after.Y - before.Y * after.X, before.X * after.X + before.Y * after.Y ) * 2048 / MathF.Tau );
		var rig = new Rig();
		rig.HoldAdmission();
		var guest = rig.Send();
		guest.BaseSpeed = 140;
		rig.RunTurns( 31 );
		Assert.AreEqual( GuestState.Queueing, guest.State );
		var outside = new List<int>();
		var heading = (guest.HeadingX, guest.HeadingY);
		rig.RunTurns( 60, () =>
		{
			if ( guest.State == GuestState.Queueing && !guest.InQueueInterlude )
				outside.Add( Turned( heading, (guest.HeadingX, guest.HeadingY) ) );
			heading = (guest.HeadingX, guest.HeadingY);
		} );
		Assert.IsTrue( outside.Count > 0 && outside.All( turned => turned == 0 ), string.Join( " ", outside ) );

		var inside = new List<int>();
		rig.RunTurns( 600, () =>
		{
			var turned = Turned( heading, (guest.HeadingX, guest.HeadingY) );
			if ( turned != 0 )
				inside.Add( turned );
			heading = (guest.HeadingX, guest.HeadingY);
			guest.InterludeTurn = rig.Simulation.ParkTurn;
			guest.QueueStandingSinceTurn = rig.Simulation.ParkTurn;
		} );
		Assert.AreEqual( GuestState.Queueing, guest.State );
		Assert.IsTrue( inside.Count is >= 30 and <= 100, $"about one turn in ten: {inside.Count}" );
		Assert.IsTrue( inside.All( turned => turned is >= -400 and <= 399 ), string.Join( " ", inside ) );
		Assert.IsTrue( inside.Any( turned => Math.Abs( turned ) < 300 ), "the size is spread, not a fixed ±400" );
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
			// tp.exe 0x5003aa: the thought comes before the relief test, so a guest queueing for a toilet thinks it too
			Assert.AreEqual( GuestThought.NeedToilet, guest.Thought );
		}
	}

	[TestMethod]
	public void ParkPointsNeedRealQueueCellsWithRoom()
	{
		// tp.exe 0x004DD920 (ORACLE.md X15): queue cells from the entrance and a list shorter than 4 × cells.
		var queued = new Rig();
		queued.HoldAdmission();
		Assert.IsTrue( QueueJoinRules.QueueCountsForParkPoints( queued.Ride, queued.Grid ) );
		var guests = Enumerable.Range( 0, 10 ).Select( _ => queued.Send() ).ToList();
		queued.RunTurns( 120 );
		Assert.AreEqual( 10, queued.Ride.QueueLength );
		Assert.IsTrue( QueueJoinRules.QueueCountsForParkPoints( queued.Ride, queued.Grid ), "10 guests in 5 cells" );
		// Shortened to two cells, the eight guests that stay fill them.
		QueuePaths.RemoveFrom( queued.Grid, queued.Ride, 2, 3 );
		queued.RunTurns( 1 );
		Assert.AreEqual( 8, queued.Ride.QueueLength );
		Assert.IsFalse( QueueJoinRules.QueueCountsForParkPoints( queued.Ride, queued.Grid ), "8 guests fill 2 cells" );
		// Without queue cells OpenTPW still lets guests in from the path cell at the entrance; the original has no back cell there.
		var bare = new Rig( queueCells: 0 );
		Assert.IsTrue( bare.Ride.QueueCells.Count > 0, "the one-cell fallback" );
		Assert.IsFalse( QueueJoinRules.QueueCountsForParkPoints( bare.Ride, bare.Grid ) );
	}

	[TestMethod]
	public void AQueueEditChangesNothingForTheGuestsThatStay()
	{
		// tp.exe 0x00501390 only dismisses a guest whose place is beyond the new room (and not waiting to be taken); the others are left to their own state 11,
		// which re-routes a guest whose place changed (0x00501160) and does nothing for one whose place did not
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		guests = Rig.InQueueOrder( guests );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ), string.Join( ", ", guests.Select( guest => guest.State ) ) );
		var before = guests.Select( guest => (guest.QueuePosition, guest.Cell, guest.Navigation.X, guest.Navigation.Y) ).ToList();
		// five cells cut to two: room 8, the six guests stay
		QueuePaths.RemoveFrom( rig.Grid, rig.Ride, 2, 3 );
		rig.Step();
		Assert.AreEqual( 2, rig.Ride.QueueSizeInCells );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ), "nobody is sent walking by the edit: " + string.Join( ", ", guests.Select( guest => guest.State ) ) );
		CollectionAssert.AreEqual( before, guests.Select( guest => (guest.QueuePosition, guest.Cell, guest.Navigation.X, guest.Navigation.Y) ).ToList(), "places and positions are untouched" );
		// the guests beyond the new room leave, the called guest and the others stay
		QueuePaths.RemoveFrom( rig.Grid, rig.Ride, 2, 4 );
		rig.Step();
		Assert.AreEqual( 1, rig.Ride.QueueSizeInCells );
		Assert.AreEqual( 4, rig.Ride.QueueLength );
		Assert.IsTrue( guests.Take( 4 ).All( guest => guest.IsInQueue && guest.State == GuestState.Queueing ) );
		Assert.IsTrue( guests.Skip( 4 ).All( guest => !guest.IsInQueue ) );
	}

	[TestMethod]
	public void QueueSlotsFollowTheOriginalAxisPerLink()
	{
		// tp.exe 0x004DE840: the rig's queue runs from (2, 5) back to (2, 1), its front towards +Y (link 16), so a place sits at
		// (lateral, 255 − depth) with depths 0, 63, 127, 191; the fourth place takes the axis of the cell behind it, and the back
		// cell's fourth place the direction from its path cell (2, 0), here +Y as well.
		var rig = new Rig();
		const byte lateral = 120;
		(int, int, int) Slot( int position ) => GuestSimulation.QueueSlotPoint( rig.Ride, position, lateral );
		Assert.AreEqual( (0, GuestNavigation.BytePoint( (2, 5), 120, 255 ).X, GuestNavigation.BytePoint( (2, 5), 120, 255 ).Y), Slot( 0 ) );
		Assert.AreEqual( (0, GuestNavigation.BytePoint( (2, 5), 120, 192 ).X, GuestNavigation.BytePoint( (2, 5), 120, 192 ).Y), Slot( 1 ) );
		Assert.AreEqual( (0, GuestNavigation.BytePoint( (2, 5), 120, 128 ).X, GuestNavigation.BytePoint( (2, 5), 120, 128 ).Y), Slot( 2 ) );
		Assert.AreEqual( (0, GuestNavigation.BytePoint( (2, 5), 120, 64 ).X, GuestNavigation.BytePoint( (2, 5), 120, 64 ).Y), Slot( 3 ) );
		Assert.AreEqual( (4, GuestNavigation.BytePoint( (2, 1), 120, 64 ).X, GuestNavigation.BytePoint( (2, 1), 120, 64 ).Y), Slot( 19 ) );
	}

	[TestMethod]
	public void QueueEditsSendGuestsAway()
	{
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 10 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		guests = Rig.InQueueOrder( guests );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ) );
		// Shorten the queue to two cells: room 8, positions 8 and 9 stood on the removed third cell.
		Assert.AreEqual( 3, QueuePaths.RemoveFrom( rig.Grid, rig.Ride, 2, 3 ) );
		rig.RunTurns( 1 );
		Assert.IsTrue( guests.Take( 8 ).All( guest => guest.IsInQueue ) );
		Assert.IsTrue( guests.Skip( 8 ).All( guest => !guest.IsInQueue ) );
		// one guest in three (id % 3 == 0) leaves angry, the others think nothing new (tp.exe 0x00501496)
		Assert.IsTrue( guests.Skip( 8 ).All( guest => guest.Thought == (guest.Id % 3 == 0 ? GuestThought.Angry : GuestThought.None) ),
			string.Join( ", ", guests.Skip( 8 ).Select( guest => $"{guest.Id}: {guest.Thought}" ) ) );
		Assert.AreEqual( 8, rig.Ride.QueueLength );
		Assert.IsNull( rig.Ride.JoinCell, "the shortened queue no longer touches the path" );

	}

	[TestMethod]
	public void ABrokenRideDismissesItsQueueHeadFirstOneGuestPerTurn()
	{
		// ORACLE.md F15: the ride update of a broken ride (states 1 and 4, 0xe0aec) removes the head guest (0xdcebc) and sends it away
		// (0xee804) on every turn; 0xdfe34 is a toilet's wear gauge and plays no part. No thought is set, happiness drops by the medium change.
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		guests = Rig.InQueueOrder( guests );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ) );
		var before = guests.Select( guest => (guest.Happiness, guest.Thought) ).ToList();
		var medium = rig.Simulation.Settings.MediumHappinessChange;

		var placesBefore = guests.Select( guest => (guest.QueuePosition, guest.Cell) ).ToList();
		// Read on the dismissal turn: from the next turn on a dismissed guest decides again (state 6), which may change both.
		var atDismissal = new (float Happiness, GuestThought Thought)[guests.Count];
		rig.Script[RideVariables.VAR_BROKEN] = 1;
		for ( var gone = 1; gone <= guests.Count; gone++ )
		{
			rig.RunTurns( 1 );
			atDismissal[gone - 1] = (guests[gone - 1].Happiness, guests[gone - 1].Thought);
			for ( var index = gone; index < guests.Count; index++ )
				Assert.AreEqual( placesBefore[index], (guests[index].QueuePosition, guests[index].Cell), $"guest {index} keeps its physical place while the ride is broken (0xed4b0)" );
			Assert.AreEqual( guests.Count - gone, rig.Ride.QueueLength, $"{gone} turns after the breakdown" );
			Assert.IsTrue( guests.Take( gone ).All( guest => !guest.IsInQueue ), "the head leaves first" );
			Assert.IsTrue( guests.Skip( gone ).All( guest => guest.IsInQueue ), "the others keep their place and wait" );
		}
		Assert.AreEqual( guests.Count, rig.Ride.Dismissed );
		for ( var index = 0; index < guests.Count; index++ )
		{
			Assert.IsTrue( guests[index].State is GuestState.DoingNothing or GuestState.WalkingAround, $"guest {index} decides or wanders again: {guests[index].State}" );
			Assert.AreEqual( before[index].Thought, atDismissal[index].Thought, "dismissal sets no thought, in particular not 'feeling sick'" );
			Assert.AreEqual( before[index].Happiness - medium, atDismissal[index].Happiness, 0.01f, "0xeb6f4 with selector 1 (PeepInfo.MediumHappinessChange)" );
		}
	}

	[TestMethod]
	public void ACondemnedRideDismissesItsHeadButItsGuestsWalkOn()
	{
		// tp.exe 0x004E14E0 gives state 4 when the life gauge truncates to a zero byte, state 1 otherwise; both dismiss the head each update (0x004E0E00), but the guest's
		// move-up holds its walk only in state 1 (0x004E0370 at 0x00500523), so behind a condemned ride's dismissed head the guests keep moving up
		var movedUp = new Dictionary<double, int>();
		foreach ( var life in new[] { 40.0, 0.0 } )
		{
			var rig = new Rig();
			rig.HoldAdmission();
			var guests = Enumerable.Range( 0, 8 ).Select( _ => rig.Send() ).ToList();
			rig.RunTurns( 120 );
			guests = Rig.InQueueOrder( guests );
			Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ), string.Join( ", ", guests.Select( guest => guest.State ) ) );
			rig.Ride.LifeGauge = life;
			var places = guests.Select( guest => guest.QueuePosition ).ToList();
			rig.Script[RideVariables.VAR_BROKEN] = 1;
			rig.RunTurns( 4 );
			Assert.IsTrue( rig.Ride.IsBrokenDown, "state 1 or 4 refuses new guests alike" );
			Assert.AreEqual( life == 0, rig.Ride.IsCondemned );
			Assert.AreEqual( life != 0, rig.Ride.HoldsQueuedGuests );
			Assert.AreEqual( 4, rig.Ride.Dismissed, "both states dismiss one head per update" );
			movedUp[life] = guests.Skip( 4 ).Where( ( guest, index ) => guest.QueuePosition != places[index + 4] ).Count();
		}
		Assert.AreEqual( 0, movedUp[40.0], "state 1 holds the guests behind the head where they stand" );
		Assert.IsTrue( movedUp[0.0] > 0, "state 4 does not hold them" );
	}

	[TestMethod]
	public void TheLifeGaugeAtTheRideUpdateDecidesStateFourAndSurvivesASave()
	{
		var rig = new Rig();
		var ride = rig.Ride;
		ride.LifeGauge = 0.99;   // truncates to a zero byte
		rig.Script[RideVariables.VAR_BROKEN] = 1;
		rig.RunTurns( 2 );
		Assert.IsTrue( ride.IsCondemned && !ride.HoldsQueuedGuests && ride.IsBrokenDown );
		// the gauge is read when the update enters the state, not afterwards
		ride.LifeGauge = 80;
		rig.RunTurns( 2 );
		Assert.IsTrue( ride.IsCondemned );
		var snapshot = ride.CaptureSnapshot();
		Assert.AreEqual( true, snapshot.Condemned );
		Assert.AreEqual( 0, RideVisitorBridge.ValidateSnapshot( snapshot ).Count );
		RideVisitorBridge Restored( RideVisitorBridgeSnapshot saved )
		{
			var other = new Rig();
			other.Script[RideVariables.VAR_RIDECLOSED] = 1;
			other.Ride.RestoreSnapshot( saved );
			return other.Ride;
		}
		Assert.IsTrue( Restored( snapshot ).IsCondemned );
		// an older save holds no such field: the restored ride is in state 1
		var third = Restored( snapshot with { Condemned = null } );
		Assert.IsTrue( third.InBrokenState && third.IsBrokenDown && !third.IsCondemned && third.HoldsQueuedGuests );
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( snapshot with { BrokenDown = false } ).Any( error => error.Contains( "condemned" ) ), "state 4 needs the broken-down state" );
		var one = new Rig();
		one.Ride.LifeGauge = 50;
		one.Script[RideVariables.VAR_BROKEN] = 1;
		one.RunTurns( 2 );
		Assert.IsTrue( one.Ride.IsBrokenDown && !one.Ride.IsCondemned );
		// leaving the state (opened again) forgets it
		rig.Script[RideVariables.VAR_BROKEN] = 0;
		rig.Script[RideVariables.VAR_RIDECLOSED] = 0;
		rig.RunTurns( 2 );
		Assert.IsFalse( ride.IsBrokenDown || ride.IsCondemned );
	}

	[TestMethod]
	public void ABrokenRideDrainsEvenWhenItsScriptClosedIt()
	{
		// SetState(1) writes VAR_RIDECLOSED = 1 itself; the queue still drains head first instead of emptying at once.
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		guests = Rig.InQueueOrder( guests );
		rig.Open = false;
		rig.Script[RideVariables.VAR_BROKEN] = 1;
		rig.RunTurns( 1 );
		// tp.exe 0x4e14e0: the closed ride's admission runs the head step (0x4e1220 → 0x4e0450), then SetState(1) runs it again (0x4e0e60).
		Assert.AreEqual( 4, rig.Ride.QueueLength, "two heads dismissed on the breakdown turn" );
		Assert.IsTrue( guests.Skip( 2 ).All( guest => guest.IsInQueue ) );
		rig.RunTurns( 1 );
		Assert.AreEqual( 3, rig.Ride.QueueLength, "then one per turn (state 1)" );
	}

	[TestMethod]
	public void AClosedRideSendsItsQueueHeadAwayOnePerTurnAndStillTakesJoiners()
	{
		// tp.exe 0x4e1220: with the open word +0x68 at 0 the admission only runs the head step 0x4e0450 (unlink 0x4ddd20, dismissal 0x5012f0):
		// one head per park turn, no thought, the medium happiness drop; the others keep their places. The join (0x4ffbc0) reads no open word.
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		guests = Rig.InQueueOrder( guests );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ) );
		var before = guests.Select( guest => (guest.Happiness, guest.Thought) ).ToList();
		var medium = rig.Simulation.Settings.MediumHappinessChange;
		rig.Open = false;
		for ( var gone = 1; gone <= guests.Count; gone++ )
		{
			rig.RunTurns( 1 );
			Assert.AreEqual( guests.Count - gone, rig.Ride.QueueLength, $"{gone} turns after closing" );
			Assert.IsTrue( guests.Take( gone ).All( guest => !guest.IsInQueue ), "the head leaves first" );
			Assert.IsTrue( guests.Skip( gone ).All( guest => guest.IsInQueue ), "the others wait" );
			var head = guests[gone - 1];
			Assert.AreEqual( before[gone - 1].Thought, head.Thought, "the dismissal sets no thought" );
			Assert.AreEqual( before[gone - 1].Happiness - medium, head.Happiness, 0.01f, "0x4fea70(1) in the dismissal" );
		}
		Assert.AreEqual( guests.Count, rig.Ride.Dismissed );
		Assert.IsFalse( rig.Ride.IsOpen, "the choice still skips a closed ride" );
		Assert.IsTrue( rig.Ride.AcceptsJoins );

		// A guest already walking to the ride when it closed joins and is sent away from the head.
		var late = rig.Send();
		rig.RunTurns( 30 );   // it first walks to the back cell (WALK-004)
		Assert.AreEqual( guests.Count + 1, rig.Ride.Dismissed, "the joiner was dismissed from the head" );
		Assert.IsFalse( late.IsInQueue );
	}

	[TestMethod]
	public void QueuedGuestsLeaveAngryWhenTheTrackRefusesGuests()
	{
		// tp.exe 0x500612: at its position, after the limit test, a guest queueing for a coaster failing 0x441970 (or a track-type-1 ride whose
		// track word is 0) thinks 13 and leaves through 0x5004aa. The admission 0x4e1220 does not call 0x441970 (see the next test), and the join
		// 0x4ffbc0 does not either, so a guest walking to the refusing coaster still joins.
		var rig = new Rig();
		rig.HoldAdmission();
		var takes = true;
		rig.Ride.Attach( rig.Script, () => rig.Open, () => takes );
		var guests = Enumerable.Range( 0, 3 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		guests = Rig.InQueueOrder( guests );
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ) );
		takes = false;
		Assert.IsTrue( rig.Ride.TrackRefusesGuests );
		Assert.IsTrue( rig.Ride.AcceptsJoins, "the join reads neither the coaster predicate nor the open word" );
		var thoughts = new GuestThought?[guests.Count];
		// The head leaves on the next update; the guests behind walk up first (their position changed) and leave once standing at it.
		rig.RunTurns( 60, () =>
		{
			for ( var index = 0; index < guests.Count; index++ )
				if ( thoughts[index] == null && !guests[index].IsInQueue )
					thoughts[index] = guests[index].Thought;
		} );
		CollectionAssert.AreEqual( Enumerable.Repeat<GuestThought?>( GuestThought.Angry, guests.Count ).ToArray(), thoughts );
		Assert.AreEqual( 0, rig.Ride.Dismissed, "each guest leaves on its own state-11 update, not from the ride's head step" );
		Assert.AreEqual( QueueJoinResult.Joined, rig.Ride.JoinQueue( 99 ), "a guest that was already walking to the refusing coaster joins (0x4ffbc0 reads no coaster test)" );
	}

	[TestMethod]
	public void AnOpenRideWhoseCoasterRefusesGuestsStillCallsItsHeadForward()
	{
		// tp.exe 0x4e1220 reads the open word, LETMEON, capacity, running, the pending visitor and the queue head: it never calls the coaster predicate 0x441970,
		// so an open ride whose coaster refuses guests calls its head forward like any other, and the guests' own state 11 sends the others away (thought 13)
		foreach ( var refuses in new[] { false, true } )
		{
			var (ride, script, host) = CreateAdmission();
			ride.Attach( script, () => true, () => !refuses, () => refuses );
			Assert.AreEqual( refuses, ride.TrackRefusesGuests );
			Assert.AreEqual( QueueJoinResult.Joined, ride.JoinQueue( 1 ), "the join does not read the coaster predicate" );
			host.AtFront.Add( 1 );
			Assert.AreEqual( 1, NextTurn( ride, host ).CalledGuest, $"refuses {refuses}: the head is called" );
			// a refusing ride that is also broken enters state 1 after the admission's own step, as any other
			script[RideVariables.VAR_BROKEN] = 1;
			Assert.AreEqual( QueueJoinResult.Joined, ride.JoinQueue( 2 ) );
			NextTurn( ride, host );
			Assert.IsTrue( ride.IsBrokenDown );
		}
	}

	[TestMethod]
	public void OperatingValuesAreDetectedExplicitly()
	{
		foreach ( var parameters in new[] { QueueParameters.Default, QueueParameters.Default with { RunsContinuously = true }, QueueParameters.Default with { HasQueue = true, Capacity = 4 } } )
			Assert.AreEqual( 60, new RideVisitorBridge( 1, "Synthetic", RideVisitorKind.Ride, 1, 60, 10 ) { Parameters = parameters }.LiveExcitement, "no operating values: the static level" );
		Assert.AreEqual( 0, new RideVisitorBridge( 1, "Zero", RideVisitorKind.Ride, 1, 60, 10 ) { Parameters = QueueParameters.Default with { Duration = 30, InitDuration = 30 } }.LiveExcitement & 0xFF, "a zero design speed with a duration is the original's 0 / 0" );
	}

	[TestMethod]
	public void ARepairedRideKeepsItsQueue()
	{
		var rig = new Rig();
		rig.HoldAdmission();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		guests = Rig.InQueueOrder( guests );
		rig.Script[RideVariables.VAR_BROKEN] = 1;
		rig.RunTurns( 2 );
		rig.Script[RideVariables.VAR_BROKEN] = 0;
		rig.RunTurns( 20 );
		var dismissed = rig.Ride.Dismissed;
		Assert.IsTrue( dismissed >= 1, "the breakdown dismissed the head" );
		Assert.IsTrue( rig.Ride.QueueLength >= guests.Count - dismissed, "the guests that were not dismissed stayed" );
		Assert.IsTrue( guests.Skip( dismissed ).All( guest => guest.IsInQueue ) );
	}

	[TestMethod]
	public void OnlyAWornOutReliefObjectSendsItsQueueAwayFeelingSick()
	{
		// ORACLE.md F15: 0xdfe34 is "UsageInfo.ProvidesRelief and the wear gauge (state of repair), as a byte, is below 25"; state 11 step 3 turns it into thought 14.
		foreach ( var (relief, gauge, leaves) in new[] { (true, 100.0, false), (true, 25.0, false), (true, 24.999, true), (true, 0.0, true), (false, 0.0, false) } )
		{
			var rig = new Rig( satisfies: relief ? GuestNeeds.Toilet : GuestNeeds.None );
			rig.HoldAdmission();
			var guest = rig.Send();
			rig.RunTurns( 60 );
			Assert.IsTrue( guest.IsInQueue );
			rig.Ride.WearGauge = gauge;
			var thought = GuestThought.None;
			rig.RunTurns( 5, () =>
			{
				if ( thought == GuestThought.None && guest.State == GuestState.DoingNothing )
					thought = guest.Thought;
			} );
			Assert.AreEqual( leaves, !guest.IsInQueue, $"relief {relief}, gauge {gauge}" );
			if ( leaves )
				Assert.AreEqual( GuestThought.FeelingSick, thought );
		}
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
		Assert.AreEqual( (true, 1, false, 0, true), (check.ConditionsHold, check.HeadGuest, check.HeadAtFront, check.CalledGuest, check.HeadNotReady), "the head is not standing at position 0 yet" );
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
		Assert.AreEqual( (0L, 1L), (ride.HeadNotReadyStreak, ride.MaximumHeadNotReadyStreak), "one evaluation with the head not ready, then calls" );

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
		// QWTC 600: the limit trunc(600 × 1 / 30) = 20 lets all six guests queue (130 would give 4).
		var rig = new Rig( takesGuests: true, capacity: 1, parameters: new QueueParameters( true, false, false, 600, 60, 60, 1, 30 ) );
		var waits = new List<long>();
		rig.Simulation.QueueWaitCompleted += ( _, _, turns ) => waits.Add( turns );
		var notReady = 0;
		rig.Ride.AdmissionChecked += ( _, check ) => notReady += check.HeadNotReady ? 1 : 0;
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
		// The guests walk up to position 0 and the called guest walks to the stand point: both progress counters move and stay bounded.
		Assert.IsTrue( rig.Ride.MaximumHeadNotReadyStreak is > 0 and < 100 && rig.Ride.MaximumHeadNotReadyStreak <= notReady, $"head-not-ready streak {rig.Ride.MaximumHeadNotReadyStreak}, evaluations {notReady}" );
		Assert.IsTrue( rig.Ride.MaximumCalledAgeTurns is > 0 and < 100, $"called-not-boarded age {rig.Ride.MaximumCalledAgeTurns}" );
	}

	[TestMethod]
	public void AdmissionCountersGrowWhileTheHeadIsNotReadyAndWhileTheCalledGuestHasNotBoarded()
	{
		var (ride, script, host) = CreateAdmission();
		Assert.IsTrue( ride.TryJoinQueue( 1 ) && ride.TryJoinQueue( 2 ) );
		for ( var turn = 1; turn <= 5; turn++ )
		{
			Assert.IsTrue( NextTurn( ride, host ).HeadNotReady );
			Assert.AreEqual( (long)turn, ride.HeadNotReadyStreak, "one per evaluation with the gates held and the head not at position 0" );
		}
		Assert.AreEqual( 5L, ride.MaximumHeadNotReadyStreak );

		host.AtFront.Add( 1 );
		var calledAt = host.Turn + 1;
		Assert.AreEqual( 1, NextTurn( ride, host ).CalledGuest );
		Assert.AreEqual( (0L, 5L, 0L), (ride.HeadNotReadyStreak, ride.MaximumHeadNotReadyStreak, ride.CalledAgeTurns), "calling the head ends the streak" );
		for ( var age = 1; age <= 3; age++ )
		{
			NextTurn( ride, host );
			Assert.AreEqual( (long)age, ride.CalledAgeTurns, "park turns since the call without boarding" );
		}
		Assert.AreEqual( 0L, ride.HeadNotReadyStreak, "a pending visitor fails the gates, so it is no head-not-ready evaluation" );

		Assert.IsTrue( ride.PresentForBoarding( 1 ) );
		script[RideVariables.VAR_LETMEON] = 0; // the script takes the guest
		var check = NextTurn( ride, host );
		Assert.AreEqual( 4, host.Turn - calledAt, "boarded on the fourth turn after the call" );
		CollectionAssert.Contains( host.Events, "board 1" );
		Assert.AreEqual( (0L, 3L), (ride.CalledAgeTurns, ride.MaximumCalledAgeTurns), "boarding ends the age; the maximum stays" );
		Assert.IsTrue( check.HeadNotReady, "guest 2 is the new head and is not standing yet" );
		Assert.AreEqual( (1L, 5L), (ride.HeadNotReadyStreak, ride.MaximumHeadNotReadyStreak), "a new streak; the maximum stays" );
	}

	[TestMethod]
	public void TheBreakdownsStateChangeMakesTheModelCallsOfItsSet()
	{
		// tp.exe 0x004E14E0: "Setting state BROKEN" calls 0x00454550(2) and "Setting state CONDEMNED" 0x00454550(4), then SetState's close calls 0x00454550(1)
		foreach ( var (gauge, set) in new[] { (100.0, ObjectHoardingState.BreakdownMode), (0.0, ObjectHoardingState.CondemnMode) } )
		{
			var (ride, script, host) = CreateAdmission();
			var calls = new List<int>();
			ride.ModelState = calls.Add;
			ride.LifeGauge = gauge;
			script[RideVariables.VAR_BROKEN] = 1;
			NextTurn( ride, host );
			CollectionAssert.AreEqual( new[] { set, ObjectHoardingState.CloseMode }, calls, $"gauge {gauge}" );
			NextTurn( ride, host );
			Assert.AreEqual( 2, calls.Count, "the broken state makes no further model call" );
		}
	}

	[TestMethod]
	public void BrokenRidesAreNotChosenJoinedOrAdmitted()
	{
		// Admission: the ride update runs the admission on the turn it notices the breakdown (0xe1864 calls 0xe1404 before it reads VAR_BROKEN),
		// then SetState(1) dismisses the head it has just called, zeroes the pending visitor and writes VAR_RIDECLOSED (ORACLE.md F27).
		var (ride, script, host) = CreateAdmission();
		Assert.IsTrue( ride.TryJoinQueue( 1 ) );
		host.AtFront.Add( 1 );
		script[RideVariables.VAR_BROKEN] = 1;
		var checks = 0;
		ride.AdmissionChecked += ( _, _ ) => checks++;
		NextTurn( ride, host );
		Assert.AreEqual( (1, 0), (checks, ride.CalledGuest), "one admission evaluation on the breakdown turn, and no pending visitor after it" );
		Assert.AreEqual( 1, script[RideVariables.VAR_RIDECLOSED], "SetState(1) writes VAR_RIDECLOSED = 1" );
		CollectionAssert.AreEqual( new[] { "call 1", "away 1" }, host.Events, "the head was called, then dismissed by the state change (F15, F27)" );
		// Joining: the join reads neither the ride state nor the open word (tp.exe 0x4ffbc0); the drain sends the joiner away from the head.
		Assert.AreEqual( QueueJoinResult.Joined, ride.JoinQueue( 2 ) );
		var checksAfterBreakdown = checks;
		NextTurn( ride, host );
		CollectionAssert.AreEqual( new[] { "call 1", "away 1", "away 2" }, host.Events, "state 1 dismisses the joiner on the next turn" );
		NextTurn( ride, host );
		Assert.AreEqual( checksAfterBreakdown, checks, "no admission evaluation on the turns after it: the ride is in state 1" );
		// Repaired and opened again (the callers of SetState(0)): the head is called again.
		script[RideVariables.VAR_BROKEN] = 0;
		script[RideVariables.VAR_RIDECLOSED] = 0;
		Assert.IsTrue( ride.TryJoinQueue( 1 ) );
		host.AtFront.Add( 1 );
		Assert.AreEqual( 1, NextTurn( ride, host ).CalledGuest, "repaired: the head is called again" );
		Assert.AreEqual( QueueJoinResult.Joined, ride.JoinQueue( 2 ) );

		// Choosing: walking guests skip a broken attraction (the counts-test, tp.exe 0x4dd920). The first major decision waits until turn 31 (state 6: T > +0x1FC + 30).
		foreach ( var broken in new[] { false, true } )
		{
			var rig = new Rig( excitement: 25 );
			rig.Script[RideVariables.VAR_BROKEN] = broken ? 1 : 0;
			var guests = Enumerable.Range( 0, 10 ).Select( _ => rig.Simulation.SpawnInPark( 2, 0 ) ).ToList();
			rig.RunTurns( 2 );
			Assert.AreEqual( broken, rig.Ride.IsBrokenDown, "the ride update enters state 1 on the turn it reads VAR_BROKEN" );
			var chose = 0;
			rig.RunTurns( 60, () => chose += guests.Count( guest => guest.AttractionId == RideId ) );
			Assert.AreEqual( broken, chose == 0, $"broken {broken}: {chose} guest-ticks heading for the ride" );
		}
	}

	[TestMethod]
	public void FullRidesAreNotChosen()
	{
		// tp.exe 0x4dd920: the choice's counts-test needs the queue list count below 4 × cells, so a full queue is never chosen
		// (the join's own room test 0x4dda20 stays for a queue that fills while the guest walks).
		foreach ( var full in new[] { false, true } )
		{
			var rig = new Rig( excitement: 25 );
			rig.HoldAdmission();
			Assert.AreEqual( 20, rig.Ride.QueueRoom, "five cells" );
			for ( var id = 9001; id < 9001 + (full ? 20 : 0); id++ )
				Assert.AreEqual( QueueJoinResult.Joined, rig.Ride.JoinQueue( id ) );
			var guests = Enumerable.Range( 0, 10 ).Select( _ => rig.Simulation.SpawnInPark( 2, 0 ) ).ToList();
			var chose = 0;
			rig.RunTurns( 60, () => chose += guests.Count( guest => guest.AttractionId == RideId ) );
			Assert.AreEqual( full, chose == 0, $"full {full}: {chose} guest-ticks heading for the ride" );
		}
	}

	[TestMethod]
	public void StateOneOutlastsVarBrokenUntilTheRideIsOpenedAgain()
	{
		// tp.exe 0x4e0e00: only state 0 reads VAR_BROKEN; states 1, 2 and 4 dismiss the head every turn, so a script that clears
		// VAR_BROKEN before the repair does not end the drain, and the choice's counts-test (0x4dd920) reads the state, not VAR_BROKEN.
		var (ride, script, host) = CreateAdmission();
		script[RideVariables.VAR_BROKEN] = 1;
		Assert.IsFalse( ride.IsBrokenDown, "state 0 until the ride update reads VAR_BROKEN" );
		Assert.AreEqual( QueueJoinResult.Joined, ride.JoinQueue( 1 ), "a ride whose update has not run yet still takes guests" );
		Assert.IsTrue( ride.TryJoinQueue( 2 ) );
		Assert.IsTrue( ride.TryJoinQueue( 3 ) );
		NextTurn( ride, host );
		Assert.IsTrue( ride.IsBrokenDown );
		script[RideVariables.VAR_BROKEN] = 0;
		NextTurn( ride, host );
		Assert.IsTrue( ride.IsBrokenDown, "VAR_BROKEN cleared, the ride stays in state 1" );
		Assert.AreEqual( 1, ride.Queue.Count(), "one head dismissed per turn: 1 on the breakdown turn, 2 on the next" );
		script[RideVariables.VAR_RIDECLOSED] = 0;
		Assert.IsFalse( ride.IsBrokenDown, "opened again: SetState(0)" );
		Assert.AreEqual( QueueJoinResult.Joined, ride.JoinQueue( 4 ) );
	}

	[TestMethod]
	public void RemovingARideClearsTheQueueStateOfEveryGuest()
	{
		var rig = new Rig();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		// The idle script never takes the called guest: the head waits to be taken (state 14), the others queue.
		rig.RunTurns( 120 );
		guests = Rig.InQueueOrder( guests );
		Assert.AreEqual( GuestState.WaitingToBoard, guests[0].State );
		Assert.IsTrue( guests.Skip( 1 ).All( guest => guest.State == GuestState.Queueing ) );
		Assert.IsTrue( guests.All( guest => guest.QueuePosition >= 0 && guest.QueueJoinTurn >= 0 ) );

		rig.Simulation.Unregister( rig.Ride );
		rig.Ride.ReleaseAll();
		rig.RunTurns( 2 );
		foreach ( var guest in guests )
		{
			Assert.IsFalse( guest.IsInQueue, $"guest {guest.Id}: {guest.State}" );
			Assert.AreEqual( (-1, -1L, false, false, 0), (guest.QueuePosition, guest.QueueJoinTurn, guest.QueueCalled, guest.InQueueInterlude, guest.AttractionId), $"guest {guest.Id}" );
		}

		// A guest left waiting for an attraction that is no longer registered (the orphaned state-14 path).
		var orphan = rig.Simulation.SpawnInPark( 2, 0 );
		orphan.AttractionId = 99;
		orphan.State = GuestState.WaitingToBoard;
		orphan.QueuePosition = 0;
		orphan.QueueJoinTurn = rig.Simulation.ParkTurn;
		orphan.QueueCalled = true;
		rig.Step();
		Assert.AreEqual( (GuestState.DoingNothing, -1, -1L, false), (orphan.State, orphan.QueuePosition, orphan.QueueJoinTurn, orphan.QueueCalled) );
	}

	[TestMethod]
	public void NeedsWindowComparesTruncatedHappinessAndToilet()
	{
		// trunc(happiness) > 80: 80.9 does not start an interlude, 81 does.
		foreach ( var (happiness, idles) in new[] { (80.9f, false), (81f, true) } )
		{
			var rig = new Rig();
			rig.HoldAdmission();
			var guest = rig.Send( happiness );
			var interlude = false;
			rig.RunTurns( 40, () => interlude |= guest.InQueueInterlude );
			Assert.AreEqual( idles, interlude, $"happiness {happiness}" );
			Assert.IsTrue( guest.IsInQueue );
		}
		// trunc(toilet) > 80: 80.9 stays, 81 leaves.
		foreach ( var (toilet, leaves) in new[] { (80.9f, false), (81f, true) } )
		{
			var rig = new Rig();
			rig.HoldAdmission();
			var guest = rig.Send();
			guest.Toilet = toilet;
			rig.RunTurns( 40 );
			Assert.AreEqual( leaves, !guest.IsInQueue, $"toilet {toilet}" );
		}
		// trunc(happiness) < 10: 9.9 leaves; 10.5 idles (10..19).
		foreach ( var (happiness, leaves) in new[] { (9.9f, true), (10.5f, false) } )
		{
			var rig = new Rig();
			rig.HoldAdmission();
			var guest = rig.Send( happiness );
			rig.RunTurns( 40 );
			Assert.AreEqual( leaves, !guest.IsInQueue, $"happiness {happiness}" );
		}
	}

	[TestMethod]
	public void AStaleQueueIsRecomputedBeforeACellIsCharged()
	{
		var rig = new Rig();
		// A path laid over the back cell (2, 1) shortens the queue; nothing has recomputed it yet.
		rig.Grid.SetPath( 2, 1, true );
		Assert.AreEqual( (2, 1), rig.Ride.QueueBackCell, "stale back cell" );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.CheckExtend( rig.Grid, rig.Ride, 3, 1 ), "the cell is valid on its own, whatever the queue's back" );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( rig.Grid, rig.Ride, 3, 1 ), "laying recomputes first: (3, 1) no longer touches the back, so it stands free" );
		Assert.AreEqual( 0, rig.Grid.GetQueueLink( 3, 1 ), "a free-standing cell has no queue link" );
		Assert.AreEqual( (2, 2), rig.Ride.QueueBackCell, "the recompute shortened the queue and the free cell joined nothing" );

		// The level recomputes before it checks and charges, wherever the queue tool's body lives.
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !Directory.Exists( Path.Combine( root.FullName, "source", "OpenTPW", "World" ) ) )
			root = root.Parent;
		if ( root == null )
			Assert.Inconclusive( "Repository sources are not next to the test binaries." );
		var charge = Directory.EnumerateFiles( Path.Combine( root!.FullName, "source", "OpenTPW" ), "*.cs", SearchOption.AllDirectories )
			.Select( File.ReadAllText ).Single( text => text.Contains( "TrySpendCell( CellPurchase.Queue )" ) );
		var spend = charge.IndexOf( "TrySpendCell( CellPurchase.Queue )", StringComparison.Ordinal );
		// BuildQueueCell checks through CheckQueueCell, the queue tool preview's validator, which calls CheckExtend.
		var checkAt = charge.LastIndexOf( "CheckQueueCell(", spend, StringComparison.Ordinal );
		var recompute = charge.LastIndexOf( ".RecomputeQueue(", spend, StringComparison.Ordinal );
		Assert.IsTrue( checkAt >= 0 && recompute >= 0 && recompute < checkAt, "RecomputeQueue precedes the check and the charge" );
		var validator = charge.IndexOf( "public QueueBuildResult CheckQueueCell(", StringComparison.Ordinal );
		Assert.IsTrue( validator >= 0 && charge.IndexOf( "QueuePaths.CheckExtend(", validator, StringComparison.Ordinal ) > validator, "CheckQueueCell runs CheckExtend" );
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

	[TestMethod]
	public void AWaterRideWithABrokenCircuitIsSkippedButKeepsItsQueue()
	{
		// tp.exe 0x004DD920 skips track types 1 and 2 whose track word is 0; the queue's own test 0x00500612 sends guests away only for a coaster or a kart track
		RideVisitorBridge Bridge( Func<bool>? refuses )
		{
			var bridge = new RideVisitorBridge( 7, "Water ride", RideVisitorKind.Ride, 5, 55, 30 );
			bridge.Attach( GuestSnapshotTests.IdleScript( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ) ), () => true, () => false, refuses );
			return bridge;
		}
		var water = Bridge( () => false );
		Assert.IsFalse( water.IsOpen, "the choice skips it" );
		Assert.IsFalse( water.TrackRefusesGuests, "its queued guests stay" );
		var kart = Bridge( null );
		Assert.IsFalse( kart.IsOpen );
		Assert.IsTrue( kart.TrackRefusesGuests, "without a separate queue test the choice's refusal sends the queue away" );
	}
}
