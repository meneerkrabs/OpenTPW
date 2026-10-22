using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A ride's entrance and exit across a resume (docs/PATHS.md): nothing laid its access cells, so the placement resolved them to the nearest walkable cells, and a path laid nearer since
/// moved neither. The resume's registration resolves them on the restored grid, which finds the nearer paths; the resume puts the saved cells back before it finds the queues from them
/// (<see cref="Level.RestoreResumeRoutingGeometry"/>), so the resumed guests' state is the saved park's and stays so. A shop has no queue and a queue ride none laid: both queue at the
/// entrance cell itself.
/// </summary>
[TestClass]
public class RideEntranceResumeTests
{
	private const float Tick = GuestSnapshotTests.Tick;

	/// <summary>A ride "*2S" placed at (4, 8) beside a spine of path along y = 0, with no access cells laid; <paramref name="nearerFirst"/> lays the nearer paths before the registration (a restored grid).</summary>
	private sealed class Site
	{
		public readonly GuestPathGrid Grid = new( 24, 24 );
		public readonly GuestSimulation Simulation;
		public readonly RideVisitorBridge Ride;

		public Site( bool hasQueue, bool nearerFirst )
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 0, true );
			var shape = new ObjectShape( new[] { "*2S" } );
			var owner = ParkCellMap.CellWord( 4, 8 );
			var points = ObjectFootprint.GetAccessPoints( shape, 4, 8, 0 ).ToList();
			Grid.Cells.WriteFootprint( ObjectFootprint.GetCells( shape, 4, 8, 0 ).ToList(), owner, 0 );
			if ( nearerFirst )
				LayNearer();
			Simulation = new GuestSimulation( Grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
			Ride = new RideVisitorBridge( 7, hasQueue ? "Queue ride" : "Shop", hasQueue ? RideVisitorKind.Ride : RideVisitorKind.Shop, 5, 55, 30 )
			{
				OwnerWord = owner,
				Parameters = new QueueParameters( hasQueue, false, false, 130, 60, 60, 5, 30 )
			};
			// what Level.RegisterWithGuests does
			var cells = Level.ResolveVisitorCells( points, Grid )!.Value;
			Ride.EntranceCell = cells.Entrance;
			Ride.ExitCell = cells.Exit;
			Ride.QueueFrontCell = cells.QueueFront;
			Ride.QueueEntranceDirection = cells.QueueEntranceDirection;
			Ride.HasCells = true;
			Simulation.Register( Ride );
		}

		public void LayNearer()
		{
			Grid.SetPath( 5, 5, true );
			Grid.SetPath( 6, 10, true );
		}

		public (int X, int Y) Entrance => Ride.EntranceCell;
		public (int X, int Y) Exit => Ride.ExitCell;

		public ulong Hash()
		{
			var hash = new StateHasher();
			Simulation.AddCanonicalState( hash );
			return hash.Value;
		}
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void AResumeKeepsTheEntranceAndExitAPathLaidNearerSinceDidNotMove( bool hasQueue )
	{
		var live = new Site( hasQueue, nearerFirst: false );
		Assert.AreEqual( ((5, 0), (6, 0)), (live.Entrance, live.Exit), "the placement fell back to the spine" );
		live.LayNearer();
		// the guests' next tick sees the edit, so the save's cache is the map's version and the resume compares the queues it finds
		live.Simulation.Tick( Tick );
		Assert.AreEqual( ((5, 0), (6, 0)), (live.Entrance, live.Exit), "a path laid nearer moves neither cell" );
		var saved = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( live.Simulation.CaptureSnapshot() ) );
		Assert.AreEqual( live.Grid.Version, saved.SeenGridVersion );

		// without the saved cells the queue the restored grid finds is not the saved one, and the resume refuses the park's own save
		var unaided = new Site( hasQueue, nearerFirst: true );
		Assert.AreEqual( live.Grid.Version, unaided.Grid.Version );
		Assert.AreEqual( ((5, 5), (6, 10)), (unaided.Entrance, unaided.Exit), "the registration on the restored grid resolves the nearer paths" );
		Assert.ThrowsException<InvalidDataException>( () => Level.ValidateResumeQueueGeometry( saved, unaided.Simulation ) );

		var resumed = new Site( hasQueue, nearerFirst: true );
		Level.RestoreResumeRoutingGeometry( saved, null, resumed.Simulation );
		resumed.Simulation.RestoreSnapshot( saved );
		Assert.AreEqual( ((5, 0), (6, 0)), (resumed.Entrance, resumed.Exit) );
		Assert.AreEqual( live.Hash(), resumed.Hash(), "the resumed park is the saved one" );
		for ( var tick = 0; tick < 120; tick++ )
		{
			live.Simulation.Tick( Tick );
			resumed.Simulation.Tick( Tick );
		}
		Assert.AreEqual( live.Hash(), resumed.Hash(), "and goes on as it" );
	}
}
