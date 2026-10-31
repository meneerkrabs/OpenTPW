using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The bumper rides with their real scripts (docs/TRACKS.md, "Bumper cars"): each theme's bumper script boards guests, runs a session and hands every
/// rider back through BUMP 2. Before the bumper controller existed the scripts' BUMP commands had nothing to answer them, so guests boarded and stayed
/// aboard for ever. Skipped without <c>OPENTPW_GAME_PATH</c>.
/// </summary>
[TestClass]
public class BumperCarAssetTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	[DataTestMethod]
	[DataRow( "jungle", "/levels/jungle/rides/bumper", -1 )]
	[DataRow( "hallow", "/levels/hallow/rides/bumper", -6 )]
	[DataRow( "space", "/levels/space/rides/bumper", -14 )]
	[DataRow( "fantasy", "/levels/fantasy/rides/bbugs", -11 )]
	public void BumperRideReleasesEveryRiderItBoards( string theme, string archive, int type )
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original data." );
		try
		{
			var entry = ObjectCatalog.Load( theme ).Entries.Single( item => item.ArchivePath == archive );
			Assert.AreEqual( type, entry.Settings.GetInt( "Bumper.BumperType" ) );
			var (runtime, guests, simulation) = BuildRide( theme, entry, 16 );
			Assert.IsNotNull( runtime.Bump, "a bumper ride has a controller" );
			Assert.IsTrue( runtime.Bump!.Template.IsBumper );
			var ride = runtime.Visitors;
			var cycles = 0;
			var wasRunning = false;
			var maxCars = 0;
			for ( var tick = 0; tick < 600 * FixedStepClock.TicksPerSecond; tick++ )
			{
				simulation.Tick( Tick );
				runtime.Simulate( Tick );
				runtime.Visitors.WalkUpdate(); // the frame's walk update (0x00557D80)
				maxCars = System.Math.Max( maxCars, runtime.Bump.VehicleCount );
				var running = runtime.GetVariable( RideVariables.VAR_RUNNING ) != 0;
				if ( wasRunning && !running )
					cycles++;
				wasRunning = running;
			}
			var using_ = guests.Count( guest => guest.State == GuestState.Using );
			System.Console.WriteLine( $"{theme}: boarded {ride.BoardedTotal} released {ride.ReleasedTotal} cycles {cycles} cars {maxCars} aboard {ride.RidersOnBoard} using {using_}" );
			Assert.IsTrue( ride.BoardedTotal >= 16, $"{theme}: the guests board ({ride.BoardedTotal})" );
			Assert.IsTrue( cycles >= 2, $"{theme}: the ride cycles board, run and release at least twice ({cycles})" );
			Assert.IsTrue( maxCars > 0, $"{theme}: the script made cars" );
			Assert.AreEqual( ride.BoardedTotal, ride.ReleasedTotal + ride.RidersOnBoard, $"{theme}: every boarded guest is released or still aboard" );
			Assert.IsTrue( ride.ReleasedTotal >= 16, $"{theme}: guests are released ({ride.ReleasedTotal})" );
			Assert.IsTrue( ride.RidersOnBoard <= runtime.Bump.RiderCount + runtime.Bump.Leaving.Count + runtime.Bump.Pending.Count, "no guest is aboard that the controller does not hold" );
		}
		finally { FileSystem = previous; }
	}

	[TestMethod]
	public void BumperRideEndsEachSessionAndReturnsToLoading()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original data." );
		try
		{
			var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( item => item.ArchivePath == "/levels/jungle/rides/bumper" );
			var (runtime, _, simulation) = BuildRide( "jungle", entry, 16 );
			var bump = runtime.Bump!;
			var sawRunning = false;
			var sawLoadingAfterRun = false;
			for ( var tick = 0; tick < 300 * FixedStepClock.TicksPerSecond; tick++ )
			{
				simulation.Tick( Tick );
				runtime.Simulate( Tick );
				runtime.Visitors.WalkUpdate(); // the frame's walk update (0x00557D80)
				if ( bump.Phase == BumpController.PhaseRunning )
					sawRunning = true;
				else if ( sawRunning && bump.Phase == BumpController.PhaseLoading )
					sawLoadingAfterRun = true;
				Assert.IsTrue( bump.RiderCount >= 0, "the rider count never goes negative" );
			}
			Assert.IsTrue( sawRunning, "a session runs (phase 2)" );
			Assert.IsTrue( sawLoadingAfterRun, "the ride returns to loading once every rider left (phase 1)" );
		}
		finally { FileSystem = previous; }
	}

	/// <summary>The ride in a small queue with <paramref name="count"/> guests heading for it (the setup of the stuck-rider probe).</summary>
	internal static (OriginalObjectRuntime Runtime, System.Collections.Generic.List<Guest> Guests, GuestSimulation Simulation) BuildRide( string theme, ObjectCatalogEntry entry, int count )
	{
		var park = OriginalPark.Load( theme );
		var grid = GuestPathGrid.FromOriginal( park.Map, park.Save! );
		var settings = GuestSettings.Load( theme );
		var site = Enumerable.Range( 0, grid.CountX * grid.CountY ).Select( index => (X: index % grid.CountX, Y: index / grid.CountX) )
			.Where( cell => grid.IsWalkable( cell.X, cell.Y ) )
			.SelectMany( cell => Enumerable.Range( 0, 4 ).Select( direction => (Path: cell, Direction: direction) ) )
			.First( candidate => Enumerable.Range( 1, 5 ).All( step =>
			{
				var (dx, dy) = GuestPathGrid.Directions[candidate.Direction];
				var (x, y) = (candidate.Path.X + dx * step, candidate.Path.Y + dy * step);
				return grid.InBounds( x, y ) && !grid.IsWalkable( x, y ) && GuestPathGrid.Directions.Count( d => grid.IsWalkable( x + d.DX, y + d.DY ) ) == (step == 1 ? 1 : 0);
			} ) );
		var (sdx, sdy) = GuestPathGrid.Directions[site.Direction];
		var queue = Enumerable.Range( 1, 4 ).Select( step => (X: site.Path.X + sdx * step, Y: site.Path.Y + sdy * step) ).Reverse().ToArray();
		var runtime = new OriginalObjectRuntime( entry, seed: 3, open: true );
		var ride = runtime.Visitors;
		ride.EntranceCell = site.Path;
		ride.ExitCell = site.Path;
		ride.QueueFrontCell = queue[0];
		ride.QueueEntranceDirection = site.Direction;
		var simulation = new GuestSimulation( grid, settings, 21 ) { ArrivalsEnabled = false };
		simulation.Register( ride );
		Assert.AreEqual( 4, QueuePaths.TryExtend( grid, ride, queue ), "the queue is built" );
		QueuePaths.TryConnect( grid, ride );
		GuestTests.LayStandCell( grid, ride );
		var guests = Enumerable.Range( 0, count ).Select( _ => simulation.SpawnInPark( site.Path.X, site.Path.Y ) ).ToList();
		foreach ( var guest in guests )
		{
			guest.AttractionId = ride.AttractionId;
			guest.State = GuestState.GoingToRide;
		}
		return (runtime, guests, simulation);
	}
}
