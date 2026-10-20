using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class PoolBridgeSimulationTests
{
	[TestMethod]
	public void AGuestWalksAcrossTheDeckWhileTheSameCellsRemainSwimmable()
	{
		var rig = new PoolsSwimSessionTests.Rig();
		// North/south bridge: land y=1,2 and 6,7, water y=3..5, outer paths y=0,8.
		for ( var y = 6; y <= 7; y++ )
			rig.Cells.SetType( 8, y, ParkCellType.Empty );
		rig.Park.Grid.SetPath( 8, 8, true );
		var world = new PoolBridgeWorld( rig.Cells );
		Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( 8, 1, 2 ) );
		var swimmer = rig.Send();
		rig.Until( () => swimmer.State == GuestState.Swimming, 600 );
		// Start at the changing room's real water entry, then swim across the bridge axis under ordinary movement.
		Assert.IsTrue( swimmer.Navigation.Cell.X < 8 );
		var swimTarget = PoolSwimRegion.Centre( (9, 4) );
		var swimmerWentUnder = false;
		var swimmerReachedOtherSide = false;

		var walker = rig.Simulation.SpawnInPark( 8, 8 );
		walker.State = GuestState.WalkingAround;
		var target = GuestNavigation.BytePoint( (8, 0), GuestWander.CentreByte, GuestWander.CentreByte );
		Assert.IsTrue( walker.Navigation.Navigate( rig.Cells, target.X, target.Y ) );
		walker.HasWaypoint = true;
		var seen = new HashSet<(int X, int Y)>();
		var walkerReachedOtherSide = false;
		for ( var tick = 0; tick < 12000 && (!walkerReachedOtherSide || !swimmerReachedOtherSide); tick++ )
		{
			swimmer.SwimTurns = 200;
			if ( !swimmerReachedOtherSide )
			{
				swimmer.SwimGoalX = swimTarget.X;
				swimmer.SwimGoalY = swimTarget.Y;
				swimmer.SwimHasGoal = true;
			}
			rig.Park.Step();
			if ( swimmer.Navigation.Cell.X == 8 )
			{
				swimmerWentUnder = true;
				Assert.IsTrue( world.Covers( swimmer.Navigation.Cell.X, swimmer.Navigation.Cell.Y ) );
				Assert.IsTrue( rig.Simulation.IsWaterBody( swimmer, rig.Region ) );
				Assert.IsFalse( rig.Simulation.IsOnParkCell( swimmer ), "A moving swimmer under the bridge remains on the water layer." );
			}
			if ( swimmer.Navigation.Cell.X >= 9 ) swimmerReachedOtherSide = true;
			seen.Add( walker.Navigation.Cell );
			if ( walker.Navigation.Cell == (8, 0) ) walkerReachedOtherSide = true;
			Assert.IsFalse( walker.Navigation.Stuck );
			Assert.IsFalse( rig.Simulation.IsWaterBody( walker, rig.Region ), "Deck walkers never enter swimmer separation buckets." );
			if ( world.Covers( walker.CellX, walker.CellY ) )
			{
				Assert.IsTrue( rig.Simulation.IsOnParkCell( walker ) );
				Assert.AreEqual( 8, walker.CellX, "The walker stays on the bridge's axis." );
			}
			Assert.IsTrue( rig.Region.IsWater( swimmer.Navigation.X, swimmer.Navigation.Y ) );
		}
		Assert.IsTrue( swimmerWentUnder && swimmerReachedOtherSide, "The swimmer must actually cross beneath the bridge, without teleporting." );
		Assert.IsTrue( walkerReachedOtherSide, "The full simulation reaches the far end." );
		for ( var y = 1; y <= 7; y++ )
			Assert.IsTrue( seen.Contains( (8, y) ), $"The walker crosses deck cell (8,{y})." );
		for ( var y = 3; y <= 5; y++ )
			Assert.AreEqual( ParkCellType.PoolWater, rig.Cells.TypeAt( 8, y ) );
	}

	[TestMethod]
	public void QueuesCannotAttachToCoveredApproachesOrKeepAStaleJoinThere()
	{
		var rig = new QueueTests.Rig( queueCells: 0, gridSize: 20 );
		var cells = rig.Grid.Cells;
		rig.Ride.QueueFrontCell = (4, 4);
		rig.Ride.QueueEntranceDirection = 0;
		rig.Grid.SetQueue( 4, 4, 0 );
		for ( var x = 6; x <= 8; x++ )
			cells.SetType( x, 5, ParkCellType.PoolWater );
		rig.Grid.SetPath( 3, 5, true );
		rig.Grid.SetPath( 11, 5, true );
		var world = new PoolBridgeWorld( cells );
		Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( 4, 5, 1 ) );
		Assert.IsFalse( QueuePaths.Connect( rig.Grid, rig.Ride, (4, 4), (4, 5) ) );
		Assert.IsFalse( QueuePaths.TryConnect( rig.Grid, rig.Ride ) );
		Assert.AreEqual( QueueBuildResult.Blocked, QueuePaths.TryExtend( rig.Grid, rig.Ride, 4, 5 ) );
		cells.SetLinks( 4, 4, GuestPathGrid.LinkValue( 2 ) );
		rig.Ride.RecomputeQueue( rig.Grid );
		Assert.IsNull( rig.Ride.JoinCell, "An old ground link must not attach a queue sideways to the deck." );
		Assert.AreEqual( ParkCellType.Empty, cells.TypeAt( 4, 5 ) );
		Assert.AreEqual( (byte)0, cells.LinksAt( 4, 5 ) );
	}
}
