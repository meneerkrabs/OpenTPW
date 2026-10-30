using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic QUEUE-003 vectors from static tp.exe 0x4DE040, not an original-game execution oracle.</summary>
[TestClass]
public class TypedQueueFrontTests
{
	private static (GuestPathGrid Grid, RideVisitorBridge Ride) Rig( byte links, bool typed = true )
	{
		var grid = new GuestPathGrid( 12, 12 );
		grid.SetPath( 1, 1, true );
		grid.Cells.SetType( 5, 5, ParkCellTypes.Entrance );
		grid.Cells.SetLinks( 5, 5, links );
		grid.Cells.SetQueueLink( 5, 5, 0x04 );
		var ride = new RideVisitorBridge( 1, "Synthetic queue", RideVisitorKind.Ride, 5, 55, 30 )
		{
			EntranceCell = (1, 1), ExitCell = (1, 1), QueueFrontCell = (1, 1), QueueEntranceDirection = 2, HasCells = true,
			QueueEntryResolver = typed ? () => (5, 5) : null
		};
		return (grid, ride);
	}

	[DataTestMethod]
	[DataRow( 1, 5, 4, 2 )]
	[DataRow( 16, 5, 6, 0 )]
	[DataRow( 64, 4, 5, 1 )]
	[DataRow( 4, 6, 5, 3 )]
	[DataRow( 85, 5, 4, 2 )]
	[DataRow( 84, 5, 6, 0 )]
	[DataRow( 68, 4, 5, 1 )]
	public void EntryLinksSelectFrontInNativePriorityDespiteStaleCacheAndDifferentQueueDirection( int links, int x, int y, int direction )
	{
		var (grid, ride) = Rig( (byte)links );
		ride.RecomputeQueue( grid );
		Assert.AreEqual( (x, y), ride.QueueFrontCell!.Value );
		Assert.AreEqual( (x, y), ride.QueueBackCell );
		Assert.AreEqual( direction, ride.QueueEntranceDirection );
		Assert.AreEqual( 1, ride.QueueSizeInCells, "an empty selected neighbour is not replaced by the nearest path" );
		Assert.IsNull( ride.JoinCell );
	}

	[TestMethod]
	public void SelectedNeighbourNeedsNoOwnerOrReciprocalLinkOrQueueType()
	{
		var (grid, ride) = Rig( 1 );
		grid.Cells.SetType( 5, 4, ParkCellTypes.Object );
		grid.Cells.SetOwner( 5, 4, 321 );
		ride.RecomputeQueue( grid );
		Assert.AreEqual( (5, 4), ride.QueueBackCell );
		Assert.AreEqual( (byte)0, grid.Cells.LinksAt( 5, 4 ) );
	}

	[TestMethod]
	public void LinkEditsRefreshGeometryAndDirectionWithoutChangingPositiveSnapshotContract()
	{
		var (grid, ride) = Rig( 1 );
		ride.RecomputeQueue( grid );
		var edits = ride.QueueEditCount;
		grid.Cells.SetLinks( 5, 5, 64 );
		Assert.IsTrue( ride.RecomputeQueue( grid ) );
		Assert.AreEqual( edits + 1, ride.QueueEditCount );
		Assert.AreEqual( (4, 5), ride.QueueBackCell );
		Assert.AreEqual( 1, ride.QueueEntranceDirection );
		var saved = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( ride.CaptureSnapshot() ) );
		Assert.AreEqual( 0, RideVisitorBridge.ValidateSnapshot( saved ).Count );
		var restored = Rig( 64 );
		restored.Ride.RecomputeQueue( restored.Grid ); // level rebinds the provider before snapshot restoration
		restored.Ride.RestoreSnapshot( saved );
		Assert.IsFalse( restored.Ride.RecomputeQueue( restored.Grid ) );
		CollectionAssert.AreEqual( ride.QueueCells.ToArray(), restored.Ride.QueueCells.ToArray() );
		Assert.AreEqual( ride.QueueFrontCell, restored.Ride.QueueFrontCell );
		Assert.AreEqual( ride.QueueEntranceDirection, restored.Ride.QueueEntranceDirection );
	}

	[TestMethod]
	public void ReplayedLinkEditsMatchLiveAndReboundSnapshotCanonicalState()
	{
		var live = Rig( 1 ); var replay = Rig( 1 );
		foreach ( byte links in new byte[] { 1, 16, 68, 4, 85 } )
		{
			live.Grid.Cells.SetLinks( 5, 5, links ); replay.Grid.Cells.SetLinks( 5, 5, links );
			live.Ride.RecomputeQueue( live.Grid ); replay.Ride.RecomputeQueue( replay.Grid );
			var a = new StateHasher(); var b = new StateHasher();
			live.Ride.AddCanonicalState( a ); replay.Ride.AddCanonicalState( b );
			Assert.AreEqual( a.Value, b.Value, "same synthetic link inputs, same canonical queue state" );
			var saved = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( live.Ride.CaptureSnapshot() ) );
			var rebound = Rig( links );
			rebound.Ride.RecomputeQueue( rebound.Grid ); rebound.Ride.RestoreSnapshot( saved );
			var c = new StateHasher(); rebound.Ride.AddCanonicalState( c );
			Assert.AreEqual( a.Value, c.Value, "provider rebound from level topology, not serialized" );
		}
	}

	[TestMethod]
	public void SaveBeforeQueueSyncRestoresOldCachesThenTheNextRecomputeMatches()
	{
		var live = Rig( 1 ); live.Ride.RecomputeQueue( live.Grid );
		live.Grid.Cells.SetLinks( 5, 5, 64 ); // the map changed, but SyncQueues has not observed it yet
		var saved = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( live.Ride.CaptureSnapshot() ) );
		var restored = Rig( 64 ); restored.Ride.RecomputeQueue( restored.Grid );
		Assert.AreNotEqual( live.Ride.QueueFrontCell, restored.Ride.QueueFrontCell );
		restored.Ride.RestoreSnapshot( saved );
		var before = new StateHasher(); var after = new StateHasher();
		live.Ride.AddCanonicalState( before ); restored.Ride.AddCanonicalState( after );
		Assert.AreEqual( before.Value, after.Value, "restore must not combine the new front with the old saved queue" );
		live.Ride.RecomputeQueue( live.Grid ); restored.Ride.RecomputeQueue( restored.Grid );
		before = new StateHasher(); after = new StateHasher();
		live.Ride.AddCanonicalState( before ); restored.Ride.AddCanonicalState( after );
		Assert.AreEqual( before.Value, after.Value );
	}

	[TestMethod]
	public void FullSimulationSaveBeforeSyncRestoresSeenCountersAndNextTickExactly()
	{
		var live = Rig( 1 );
		var simulation = new GuestSimulation( live.Grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		simulation.Register( live.Ride ); simulation.Tick( 1f / 30 );
		live.Grid.Cells.SetLinks( 5, 5, 64 );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( simulation.CaptureSnapshot() ) );
		var restored = Rig( 1 ); restored.Grid.Cells.SetLinks( 5, 5, 64 );
		var target = new GuestSimulation( restored.Grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		target.Register( restored.Ride );
		Level.ValidateResumeQueueGeometry( snapshot, target ); // stale map-version cache is allowed, not normalized
		target.RestoreSnapshot( snapshot );
		void SameState()
		{
			var a = new StateHasher(); var b = new StateHasher();
			simulation.AddCanonicalState( a ); target.AddCanonicalState( b );
			Assert.AreEqual( a.Value, b.Value );
			Assert.AreEqual( simulation.CaptureSnapshot().SeenGridVersion, target.CaptureSnapshot().SeenGridVersion );
		}
		SameState();
		simulation.Tick( 1f / 30 ); target.Tick( 1f / 30 );
		SameState();
		Assert.AreEqual( (4, 5), restored.Ride.QueueFrontCell!.Value );
	}

	[TestMethod]
	public void LegacySnapshotUsesOriginalShapeCacheUntilNormalRefresh()
	{
		var legacy = Rig( 1, typed: false ); legacy.Ride.RecomputeQueue( legacy.Grid );
		var saved = legacy.Ride.CaptureSnapshot();
		Assert.IsNull( saved.TypedQueueFront ); Assert.IsNull( saved.TypedQueueDirection );
		var restored = Rig( 64 ); restored.Ride.RecomputeQueue( restored.Grid );
		restored.Ride.RestoreSnapshot( saved );
		Assert.AreEqual( (1, 1), restored.Ride.QueueFrontCell!.Value );
		Assert.AreEqual( 2, restored.Ride.QueueEntranceDirection );
		var a = new StateHasher(); var b = new StateHasher();
		legacy.Ride.AddCanonicalState( a ); restored.Ride.AddCanonicalState( b );
		Assert.AreEqual( a.Value, b.Value );
		restored.Ride.RecomputeQueue( restored.Grid );
		Assert.AreEqual( (4, 5), restored.Ride.QueueFrontCell!.Value );
	}

	[TestMethod]
	public void LegacyCurrentEpochValidatesOriginalGeometryWithoutMutatingTypedTarget()
	{
		var legacy = Rig( 64, typed: false );
		var source = new GuestSimulation( legacy.Grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		source.Register( legacy.Ride ); source.Tick( 1f / 30 );
		var saved = source.CaptureSnapshot();
		var restored = Rig( 64 );
		var target = new GuestSimulation( restored.Grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		target.Register( restored.Ride );
		Assert.AreEqual( restored.Grid.Version, saved.SeenGridVersion );
		Assert.AreNotEqual( legacy.Ride.QueueFrontCell, restored.Ride.QueueFrontCell );
		var before = new StateHasher(); target.AddCanonicalState( before );
		Level.ValidateResumeQueueGeometry( saved, target );
		var after = new StateHasher(); target.AddCanonicalState( after );
		Assert.AreEqual( before.Value, after.Value, "validation never rewrites live caches" );
		var altered = saved with { Attractions = new[] { saved.Attractions[0] with { QueueCells = new[] { (7, 7) } } } };
		Assert.ThrowsException<System.IO.InvalidDataException>( () => Level.ValidateResumeQueueGeometry( altered, target ) );
		target.RestoreSnapshot( saved );
		var a = new StateHasher(); var b = new StateHasher(); source.AddCanonicalState( a ); target.AddCanonicalState( b );
		Assert.AreEqual( a.Value, b.Value );
	}

	[TestMethod]
	public void TypedCachePairRejectsPartialOrInvalidValuesWithoutRelaxingQueueValidation()
	{
		var rig = Rig( 1 ); rig.Ride.RecomputeQueue( rig.Grid ); var saved = rig.Ride.CaptureSnapshot();
		foreach ( var invalid in new[] {
			saved with { TypedQueueFront = null }, saved with { TypedQueueDirection = null },
			saved with { TypedQueueDirection = 4 }, saved with { TypedQueueFront = (-1, 0) },
			saved with { QueueCells = Array.Empty<(int X, int Y)>() } } )
			Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( invalid ).Count > 0 );
		var simulation = new GuestSimulation( rig.Grid, GuestTests.CreateSettings(), 7 );
		simulation.Register( rig.Ride );
		var snapshot = simulation.CaptureSnapshot();
		var bad = snapshot with { Attractions = new[] { saved with { TypedQueueFront = (99, 99) } } };
		Assert.IsFalse( simulation.ValidateSnapshot( bad ).IsValid );
	}

	[TestMethod]
	public void AnUntypedWorldKeepsTheHostOneCellQueueAndALinklessEntryHasNoQueueAtAll()
	{
		var untyped = Rig( 1, typed: false );
		untyped.Ride.RecomputeQueue( untyped.Grid );
		Assert.AreEqual( (1, 1), untyped.Ride.QueueBackCell );
		Assert.AreEqual( 1, untyped.Ride.QueueSizeInCells );
		Assert.AreEqual( ((int, int)?)(1, 1), untyped.Ride.BackCell, "an untyped world has no entry to read: the host's one-cell queue stays" );
		Assert.IsFalse( untyped.Ride.IsZeroFront );
		// [BIN:TPW-EXE:0x004DE040 zero front] an entry without a cardinal link bit (none, or diagonals only) gives back cell 0 and size 0 (QUEUE-003's guest side); the host keeps one cell of geometry for the called guest's stand cell and the queue tool
		foreach ( var rig in new[] { Rig( 0 ), Rig( 2 ) } )
		{
			rig.Ride.RecomputeQueue( rig.Grid );
			Assert.IsTrue( rig.Ride.IsZeroFront );
			Assert.IsNull( rig.Ride.BackCell, "the back-of-queue cell is 0" );
			Assert.AreEqual( 0, rig.Ride.QueueSizeInCells, "+0x40 is 0" );
			Assert.IsNull( rig.Ride.JoinCell );
			Assert.AreEqual( 0, rig.Ride.QueueRoom, "no joiner finds room (0x4DDA20)" );
			Assert.AreEqual( (1, 1), rig.Ride.QueueBackCell, "the queue tool's geometry keeps its fallback cell" );
		}
	}

	[TestMethod]
	public void CellZeroZeroIsNotTheNativeNullCellWord()
	{
		var (grid, ride) = Rig( 1 );
		ride.QueueEntryResolver = () => (0, 1);
		grid.Cells.SetLinks( 0, 1, 1 );
		ride.RecomputeQueue( grid );
		Assert.AreEqual( (0, 0), ride.QueueBackCell );
	}

	[DataTestMethod]
	[DataRow( 0 )] [DataRow( 2 )] [DataRow( 0xAA )]
	public void AnEntryWithoutACardinalBitHasANativeZeroFront( int links )
	{
		// tp.exe 0x4DE040 writes the cell word 0 when none of the bits 1, 16, 64 and 4 is set (diagonal bits do not count); 0x4DE4A0 then reports the queue ride unconnected (a back of 0 fails its first test), which the opening guard and the object window read
		var (grid, ride) = Rig( (byte)links );
		ride.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		Assert.IsTrue( ride.ReadNativeFront( grid, out var front, out var direction ) );
		Assert.IsNull( front );
		Assert.AreEqual( -1, direction );
		Assert.IsFalse( ParkGate.QueueConnected( grid, ride ), "a ride with no front is unconnected" );
	}

	[TestMethod]
	public void ANativeFrontNeedsAReconstructableEntry()
	{
		var (grid, ride) = Rig( 1, typed: false );
		Assert.IsFalse( ride.ReadNativeFront( grid, out _, out _ ), "an untyped world has no entry to read" );
		var (typedGrid, typed) = Rig( 1 );
		typed.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		Assert.IsTrue( typed.ReadNativeFront( typedGrid, out var front, out var direction ) );
		Assert.AreEqual( ((int, int)?)(5, 4), front );
		Assert.AreEqual( 0, direction );
		Assert.IsTrue( ParkGate.QueueConnected( typedGrid, typed ), "a front that holds no queue cell keeps the permissive answer" );
	}

	[TestMethod]
	public void TheReevaluationRaisesTheCounterAndStampsNoBlockForAZeroFront()
	{
		// tp.exe 0x4DE1F0: the counter 0x7CDB98 rises (0x4D8C50) and 0x4D8C60 stamps the back cell's block only for a nonzero cell word; a zero front has back 0
		var (grid, ride) = Rig( 0 );
		ride.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		var counter = grid.Cells.StampCounter;
		var block = grid.Cells.BlockStamp( 0, 0 );
		ride.NotifyQueueEdited( grid );
		Assert.AreEqual( counter + 1, grid.Cells.StampCounter );
		Assert.AreEqual( block, grid.Cells.BlockStamp( 0, 0 ), "no block was stamped" );
		var (stampedGrid, stamped) = Rig( 1 );
		stamped.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		stamped.RecomputeQueue( stampedGrid );
		stamped.NotifyQueueEdited( stampedGrid );
		Assert.AreEqual( stampedGrid.Cells.StampCounter, stampedGrid.Cells.BlockStamp( 0, 0 ), "a front's block takes the new counter" );
		var before = stampedGrid.Cells.StampCounter;
		stamped.NotifyQueueEdited( stampedGrid, stampBack: false );
		Assert.AreEqual( before + 1, stampedGrid.Cells.StampCounter );
		Assert.AreEqual( before, stampedGrid.Cells.BlockStamp( 0, 0 ), "the entrance-clear evaluation stamps no block" );
	}
}
