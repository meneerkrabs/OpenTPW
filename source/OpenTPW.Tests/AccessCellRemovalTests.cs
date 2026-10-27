using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The cells a placement lays outside an entrance and an exit are NoModify, which tp.exe's cell clear keeps (0x5367D4); the level's remove tools take them nevertheless
/// (EXT:removable-access-cells), and a queue cell may stand free of the queue (QUEUE-014). Headless: the cell map, the queue and path builders and the level's re-resolution
/// of a ride's entrance, which is what <c>Level.RemoveAt</c> runs; the native smoke test drives the level itself.
/// </summary>
[TestClass]
public class AccessCellRemovalTests
{
	private const int RideId = 7;

	/// <summary>A ride "*2S" placed at (4, 8): its entrance outside cell (5, 7) is the queue front, its exit outside cell (6, 9) a path stub; a spine of path cells runs along y = 0.</summary>
	private sealed class Site
	{
		public readonly GuestPathGrid Grid = new( 24, 24 );
		public readonly ushort Owner = ParkCellMap.CellWord( 4, 8 );
		public readonly List<ObjectAccessPoint> Points;
		public readonly RideVisitorBridge Ride;

		public Site( bool laid = true )
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 0, true );
			// the map start's initial path stays fixed
			Grid.Cells.SetFlags( 3, 0, ParkCellFlags.NoModify );
			var shape = new ObjectShape( new[] { "*2S" } );
			var footprint = ObjectFootprint.GetCells( shape, 4, 8, 0 ).ToList();
			Points = ObjectFootprint.GetAccessPoints( shape, 4, 8, 0 ).ToList();
			Grid.Cells.WriteFootprint( footprint, Owner, 0 );
			if ( laid )
				ObjectAccessCells.Lay( Grid.Cells, Points, Owner, hasQueue: true );
			Ride = new RideVisitorBridge( RideId, "Queue ride", RideVisitorKind.Ride, 5, 55, 30 )
			{
				OwnerWord = Owner,
				Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
			};
			Resolve();
			Ride.QueueFrontCell = Front;
			Ride.QueueEntranceDirection = GuestPathGrid.DirectionBetween( Front.X, Front.Y, Entrance.X, Entrance.Y );
			Ride.HasCells = true;
			Ride.RecomputeQueue( Grid );
		}

		public ObjectAccessPoint Entrance => Points.Single( point => point.Kind == ObjectCellKind.Entrance );
		public ObjectAccessPoint ExitPoint => Points.Single( point => point.Kind == ObjectCellKind.Exit );
		public (int X, int Y) Front => (Entrance.OutsideX, Entrance.OutsideY);
		public (int X, int Y) ExitStub => (ExitPoint.OutsideX, ExitPoint.OutsideY);

		public void Resolve()
		{
			var cells = Level.ResolveVisitorCells( Points, Grid )!.Value;
			Ride.EntranceCell = cells.Entrance;
			Ride.ExitCell = cells.Exit;
		}

		/// <summary>What <c>Level.RemoveAt</c> does after it took cells a placement laid.</summary>
		public void Refresh( params (int X, int Y)[] removed ) =>
			Level.RefreshVisitorCells( new[] { ((IEnumerable<ObjectAccessPoint>)Points, Ride) }, Grid, removed.ToHashSet() );
	}

	/// <summary>
	/// A ride's entrance and exit stand where its placement resolved them until a cell they use is removed; a path laid nearer later moves neither. A save keeps them as they stand, so
	/// a restore, whose registration resolves them on the restored grid, puts them back where they were (a shared park's join restores the host's park this way, and its check compares them).
	/// </summary>
	[TestMethod]
	public void ASaveKeepsTheEntranceAndExitAPathLaidNearerSinceDidNotMove()
	{
		static void LayNearer( Site site )
		{
			site.Grid.SetPath( 5, 5, true );
			site.Grid.SetPath( 6, 10, true );
		}
		// nothing laid the ride's access cells, so its entrance and exit fell back to the nearest path cells on the spine
		var site = new Site( laid: false );
		var fallback = (site.Ride.EntranceCell, site.Ride.ExitCell);
		Assert.AreEqual( ((5, 0), (6, 0)), fallback );
		LayNearer( site );
		Assert.AreEqual( fallback, (site.Ride.EntranceCell, site.Ride.ExitCell), "a path laid nearer moves neither" );
		var saved = site.Ride.CaptureSnapshot();
		Assert.AreEqual( fallback, (saved.EntranceCell!.Value, saved.ExitCell!.Value) );

		// the restore registers the ride on the restored grid, which resolves the nearer cells, then takes the saved ones
		var restored = new Site( laid: false );
		LayNearer( restored );
		restored.Resolve();
		Assert.AreEqual( ((5, 5), (6, 10)), (restored.Ride.EntranceCell, restored.Ride.ExitCell), "a fresh resolution finds the nearer paths" );
		restored.Ride.RestoreSnapshot( saved );
		Assert.AreEqual( fallback, (restored.Ride.EntranceCell, restored.Ride.ExitCell) );

		// a save from before keeps the cells the restore resolved, and the two cells go together
		var older = new Site( laid: false );
		LayNearer( older );
		older.Resolve();
		older.Ride.RestoreSnapshot( saved with { EntranceCell = null, ExitCell = null } );
		Assert.AreEqual( ((5, 5), (6, 10)), (older.Ride.EntranceCell, older.Ride.ExitCell) );
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( saved with { ExitCell = null } ).Any( error => error.Contains( "together" ) ) );
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( saved with { EntranceCell = (-1, 0) } ).Any( error => error.Contains( "outside" ) ) );
	}

	[TestMethod]
	public void TheLaidQueueFrontGoesWithTheQueueBehindItAndTheRideFallsBackToThePath()
	{
		var site = new Site();
		var (grid, ride) = (site.Grid, site.Ride);
		Assert.IsTrue( grid.IsQueue( site.Front.X, site.Front.Y ) );
		Assert.IsTrue( grid.Cells.FlagsAt( site.Front.X, site.Front.Y ).HasFlag( ParkCellFlags.NoModify ), "the placement lays the front NoModify" );
		Assert.AreEqual( site.Front, ride.EntranceCell, "the entrance is the laid queue front" );
		for ( var y = 6; y >= 1; y-- )
			Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 5, y ) );
		QueuePaths.TryConnect( grid, ride );
		Assert.AreEqual( 7, ride.QueueSizeInCells );
		Assert.IsNotNull( ride.JoinCell, "the queue reaches the spine" );

		// the original's cell clear keeps a NoModify cell; the level opts in to taking it
		Assert.AreEqual( 0, QueuePaths.RemoveFrom( grid, ride, site.Front.X, site.Front.Y ), "without the opt-in the front stays" );
		var removed = QueuePaths.RemoveFrom( grid, ride, site.Front.X, site.Front.Y, removeFixed: true );
		Assert.AreEqual( 7, removed, "the front goes with every cell behind it" );
		for ( var y = 7; y >= 1; y-- )
		{
			Assert.AreEqual( ParkCellType.Empty, grid.Cells.TypeAt( 5, y ) );
			Assert.AreEqual( ParkCellFlags.None, grid.Cells.FlagsAt( 5, y ), "no NoModify flag stays behind" );
			Assert.AreEqual( 0, grid.Cells.OwnerAt( 5, y ) );
		}
		Assert.AreEqual( 0, grid.Cells.LinksAt( 5, 8 ) & GuestPathGrid.LinkValue( 0 ), "the entrance cell's link toward the outside cell is undone" );

		// the entrance resolution falls back to the nearest walkable path, as for a ride nothing laid cells for
		site.Refresh( site.Front );
		Assert.IsTrue( grid.IsWalkable( ride.EntranceCell.X, ride.EntranceCell.Y ), $"the entrance falls back to a path cell, {ride.EntranceCell}" );
		Assert.AreNotEqual( site.Front, ride.EntranceCell );
		CollectionAssert.AreEqual( new[] { ride.EntranceCell }, ride.QueueCells.ToArray(), "the ride keeps a one-cell queue" );
		Assert.IsNotNull( ride.JoinCell, "guests can still join the ride" );

		// the queue can be laid again from the same front, and a demolition clears what is left
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, site.Front.X, site.Front.Y ), "the front can be laid again" );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 5, 6 ) );
		Assert.AreEqual( site.Owner, grid.Cells.OwnerAt( 5, 6 ) );
		Assert.AreEqual( 2, ride.QueueSizeInCells );
		ObjectAccessCells.ClearQueue( grid.Cells, site.Owner );
		Assert.IsFalse( grid.IsQueue( 5, 7 ) || grid.IsQueue( 5, 6 ), "the demolition clears the new queue as well" );
		Assert.AreEqual( ParkCellFlags.None, grid.Cells.FlagsAt( 5, 7 ) );
	}

	[TestMethod]
	public void TheLaidExitPathGoesAndAPathCanBeLaidThereAgain()
	{
		var site = new Site();
		var grid = site.Grid;
		var (x, y) = site.ExitStub;
		Assert.AreEqual( (byte)ParkCellType.Path, grid.Cells.RawTypeAt( x, y ) );
		Assert.IsTrue( grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.NoModify ) );
		// a path cell beside the stub, linked to it, gives the stub a neighbour: the original's forced clear then keeps it
		var paths = new ParkPathBuilder( grid.Cells, grid, () => null, new SandboxParkGrid() );
		Assert.AreEqual( CellBuildResult.Ok, paths.CheckCell( x + 1, y ) );
		grid.SetPath( x + 1, y, true );
		PathLinkWriter.LinkPath( grid.Cells, x, y );
		Assert.AreEqual( CellBuildResult.NoModify, paths.Remove( x, y ), "the forced clear keeps a NoModify cell with a neighbour" );
		Assert.AreEqual( CellBuildResult.Ok, paths.RemoveFixed( x, y ), "the level's opt-in takes it" );
		Assert.AreEqual( ParkCellType.Empty, grid.Cells.TypeAt( x, y ) );
		Assert.AreEqual( ParkCellFlags.None, grid.Cells.FlagsAt( x, y ) );
		Assert.AreEqual( 0, grid.Cells.LinksAt( x + 1, y ) & GuestPathGrid.LinkValue( 3 ), "the neighbour's link toward the removed cell is undone" );
		site.Refresh( (x, y) );
		Assert.IsTrue( grid.IsWalkable( site.Ride.ExitCell.X, site.Ride.ExitCell.Y ), $"the exit falls back to a path cell, {site.Ride.ExitCell}" );
		// a path can be laid on the removed cell again
		Assert.AreEqual( CellBuildResult.Ok, paths.CheckCell( x, y ) );
		Assert.IsTrue( paths.BuildSegment( (x + 1, y), (x, y) ).Completed );
		Assert.AreEqual( (byte)ParkCellType.Path, grid.Cells.RawTypeAt( x, y ) );
		// a lone fixed cell with no neighbour goes with the plain tool too (the original's own exception)
		Assert.AreEqual( CellBuildResult.NoModify, paths.Remove( 3, 0 ), "the map start's initial path keeps its flag" );
	}

	[TestMethod]
	public void OnlyTheCellsOutsideAnEntranceOrExitCountAsLaidByAPlacement()
	{
		var site = new Site();
		Assert.IsTrue( ObjectAccessCells.IsOutsideCell( site.Points, site.Front.X, site.Front.Y ) );
		Assert.IsTrue( ObjectAccessCells.IsOutsideCell( site.Points, site.ExitStub.X, site.ExitStub.Y ) );
		Assert.IsFalse( ObjectAccessCells.IsOutsideCell( site.Points, 3, 0 ), "the map start's NoModify path is not an access cell" );
		Assert.IsFalse( ObjectAccessCells.IsOutsideCell( site.Points, site.Entrance.X, site.Entrance.Y ) );
	}

	[TestMethod]
	public void AGuestQueuedBehindTheRemovedFrontIsNotStuck()
	{
		var rig = new QueueTests.Rig();
		rig.HoldAdmission();
		var ride = rig.Ride;
		var owner = ParkCellMap.CellWord( 2, 6 );
		ride.OwnerWord = owner;
		foreach ( var cell in ride.QueueCells )
			rig.Grid.Cells.SetOwner( cell.X, cell.Y, owner );
		rig.Grid.Cells.SetFlags( 2, 5, ParkCellFlags.NoModify );
		var points = new[] { new ObjectAccessPoint( ObjectCellKind.Entrance, 2, 6, 2, 5 ) };
		rig.KeepNeedsEmpty = true;
		var guests = Enumerable.Range( 0, 3 ).Select( _ => rig.Send() ).ToList();
		var reached = false;
		rig.RunTurns( 150, () => reached |= guests.Any( guest => guest.IsInQueue || guest.State is GuestState.Using or GuestState.ExitingRide ) );
		Assert.IsTrue( reached, $"guests reached the ride before the edit: {string.Join( ", ", guests.Select( guest => guest.State ) )}" );

		Assert.IsTrue( guests.Any( guest => guest.IsInQueue ), "guests stand in the queue when the front goes" );
		Assert.AreEqual( 5, QueuePaths.RemoveFrom( rig.Grid, ride, 2, 5, removeFixed: true ) );
		Level.RefreshVisitorCells( new[] { ((IEnumerable<ObjectAccessPoint>)points, ride) }, rig.Grid, new HashSet<(int X, int Y)> { (2, 5), (2, 4), (2, 3), (2, 2), (2, 1) } );
		var emptied = new HashSet<(int X, int Y)> { (2, 5), (2, 4), (2, 3), (2, 2), (2, 1) };
		// the original's clear writes no guest: the queued guests keep their place until the ride's re-evaluation (the next turns' sync) dismisses them where they stand
		Assert.IsTrue( guests.Any( guest => guest.IsInQueue ), "the guests are still queued after the clear" );
		var late = rig.Send();
		rig.Open = true;
		rig.Script[RideVariables.VAR_RUNNING] = 0;
		rig.RunTurns( 1500 );
		Assert.IsTrue( rig.Grid.IsWalkable( ride.EntranceCell.X, ride.EntranceCell.Y ), "the ride's entrance is on the path" );
		Assert.AreNotEqual( GuestState.GoingToRide, late.State, "a guest sent after the edit joined, rode or gave up; it is not walking for ever" );
		foreach ( var guest in guests.Append( late ) )
		{
			Assert.IsFalse( guest.Navigation.Stuck, $"guest {guest.Id} (state {guest.State}) is not stuck" );
			// the original's clear writes no guest, so a guest that keeps its place in the queue (within the room) stands on the emptied cell until its own state moves it
			if ( guest.OnGrid && !guest.Navigation.OffGrid && guest.State is GuestState.GoingToRide or GuestState.WalkingAround or GuestState.DoingNothing )
				Assert.IsTrue( rig.Grid.IsWalkable( guest.CellX, guest.CellY ) || rig.Grid.IsQueue( guest.CellX, guest.CellY ), $"guest {guest.Id} (state {guest.State}) stands on {(guest.CellX, guest.CellY)}, neither path nor queue" );
		}
	}

	[TestMethod]
	public void AFreeStandingQueueCellIsTheRidesAndNoWalkerOrChainTouchesIt()
	{
		var rig = new QueueTests.Rig( queueCells: 3 );
		var (grid, ride) = (rig.Grid, rig.Ride);
		var owner = ParkCellMap.CellWord( 2, 6 );
		ride.OwnerWord = owner;
		var chain = ride.QueueCells.ToArray();
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.CheckExtend( grid, ride, 8, 8 ), "no rule asks the cell to touch the back cell" );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 8, 8 ) );
		Assert.IsTrue( grid.IsQueue( 8, 8 ) );
		Assert.AreEqual( owner, grid.Cells.OwnerAt( 8, 8 ), "the ride owns it through its owner word" );
		Assert.AreEqual( 0, grid.GetQueueLink( 8, 8 ), "a free-standing cell has no predecessor" );
		Assert.AreEqual( 0, grid.Cells.LinksAt( 8, 8 ) );
		CollectionAssert.AreEqual( chain, ride.QueueCells.ToArray(), "the queue is the chain from its front" );
		Assert.AreEqual( ride, Level.QueueRideAt( grid, new[] { ride }, 8, 8 ), "the owner word finds the ride for the remove tool" );

		// a second free-standing cell beside it, and one touching the queue's back cell without a link toward it, stay out of the chain too
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 9, 8 ) );
		Assert.AreEqual( 3, ride.QueueSizeInCells );
		rig.Send();
		rig.RunTurns( 300 );

		// the remove tool takes one cell, and not the queue
		Assert.AreEqual( 1, QueuePaths.RemoveFrom( grid, ride, 8, 8 ) );
		Assert.AreEqual( ParkCellType.Empty, grid.Cells.TypeAt( 8, 8 ) );
		Assert.AreEqual( 0, grid.Cells.OwnerAt( 8, 8 ) );
		Assert.IsTrue( grid.IsQueue( 9, 8 ), "the other free cell stays" );
		CollectionAssert.AreEqual( chain, ride.QueueCells.ToArray() );
		Assert.AreEqual( 0, QueuePaths.RemoveFrom( grid, new RideVisitorBridge( RideId + 1, "Other", RideVisitorKind.Ride, 5, 55, 30 ) { OwnerWord = 99, Parameters = ride.Parameters }, 9, 8 ), "another ride's remove tool leaves it" );
	}

	[TestMethod]
	public void AFreeStandingQueueCellSurvivesASaveAndALoad()
	{
		var grid = new GuestPathGrid( 16, 16 );
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "jungle", Array.Empty<ObjectCatalogEntry>() }, null )!;
		var objects = new ParkObjects( catalog, new SandboxParkGrid() );
		var owner = ParkCellMap.CellWord( 3, 3 );
		grid.SetQueue( 6, 6, -1 );
		grid.Cells.SetOwner( 6, 6, owner );
		var snapshot = ParkLayoutSnapshot.CaptureSnapshot( grid.Cells, objects );
		var restored = ParkLayoutSnapshot.RestoreCellMap( snapshot );
		Assert.AreEqual( (byte)ParkCellType.Queue, restored.RawTypeAt( 6, 6 ) );
		Assert.AreEqual( owner, restored.OwnerAt( 6, 6 ) );
		Assert.AreEqual( 0, restored.QueueLinkAt( 6, 6 ) );
	}
}
