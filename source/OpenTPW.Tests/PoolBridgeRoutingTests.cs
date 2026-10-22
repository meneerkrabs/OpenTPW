using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Elevated bridge connectivity stays separate from the ground and water map.</summary>
[TestClass]
public class PoolBridgeRoutingTests
{
	[TestMethod]
	public void WithoutBridgesGroundLinksAndWanderDrawsStayUnchanged()
	{
		var grid = new GuestPathGrid( 10, 1 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 0, true );
		var values = new Queue<uint>( new uint[] { 0, 1, 72, 0 } );
		var picked = GuestWander.Pick( grid, (5, 0), () => values.Dequeue() );
		Assert.AreEqual( new GuestWander.Destination( (6, 0), 72, GuestWander.MinimumByte ), picked );
		Assert.AreEqual( 0, values.Count, "The original draw sequence must be consumed exactly." );
		Assert.AreEqual( 10, grid.WalkableCount );
		for ( var x = 0; x < 10; x++ )
			Assert.AreEqual( grid.Cells.LinksAt( x, 0 ), grid.LinksAt( x, 0 ) );
	}

	[TestMethod]
	public void WithoutBridgesEveryWalkMaskStillRejectsPoolWater()
	{
		var map = new ParkCellMap( 4, 3 );
		map.SetType( 1, 1, ParkCellType.Path );
		map.SetType( 2, 1, ParkCellType.PoolWater );
		map.SetLinks( 1, 1, 0x55 );
		var grid = new GuestPathGrid( map );
		Assert.IsFalse( grid.IsWalkable( 2, 1 ) );
		Assert.IsFalse( grid.AreConnected( 1, 1, 1 ) );
		for ( var mask = 0; mask <= 2; mask++ )
		{
			Assert.IsTrue( WalkEdges.Blocked( map, 1, 1, 1, mask ) );
			Assert.IsTrue( WalkEdges.Blocked( map, 2, 0, 2, mask ), "An off-path source cannot enter water either." );
		}
	}
	private static (GuestPathGrid Grid, PoolBridgeWorld World, PoolBridge Bridge) Fixture( int direction, bool pathApproaches = false )
	{
		var cells = new ParkCellMap( 20, 20 );
		var bridge = new PoolBridge( 9, 9, direction, 0 );
		for ( var i = -1; i <= PoolBridge.Length; i++ )
		{
			var (x, y) = bridge.Cell( i );
			cells.SetType( x, y, i is >= 2 and <= 4 ? ParkCellType.PoolWater
				: i is -1 or PoolBridge.Length || pathApproaches ? ParkCellType.Path : ParkCellType.Empty );
		}
		return (new GuestPathGrid( cells ), new PoolBridgeWorld( cells ), bridge);
	}

	[TestMethod]
	public void BothAxesConnectOnlyThroughTheirEndsWithoutChangingWater()
	{
		foreach ( var direction in new[] { 0, 1, 2, 3 } )
		{
			var (grid, world, bridge) = Fixture( direction );
			var start = bridge.Cell( -1 );
			var end = bridge.Cell( PoolBridge.Length );
			Assert.AreEqual( -1, grid.Distance( start.X, start.Y, end.X, end.Y ) );
			var version = grid.Version;
			Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( bridge.X, bridge.Y, direction ) );
			Assert.IsTrue( grid.Version > version );
			Assert.AreEqual( PoolBridge.Length + 2, grid.WalkableCount );
			Assert.AreEqual( PoolBridge.Length + 1, grid.Distance( start.X, start.Y, end.X, end.Y ) );
			for ( var i = -1; i < PoolBridge.Length; i++ )
			{
				var cell = bridge.Cell( i );
				var next = bridge.Cell( i + 1 );
				Assert.IsTrue( grid.AreConnected( cell.X, cell.Y, direction ) );
				Assert.IsTrue( grid.AreConnected( next.X, next.Y, (direction + 2) & 3 ) );
				Assert.AreNotEqual( 0, grid.LinksAt( cell.X, cell.Y ) & GuestPathGrid.LinkValue( direction ) );
				for ( var mask = 0; mask <= 2; mask++ )
				{
					Assert.IsFalse( WalkEdges.Blocked( grid.Cells, cell.X, cell.Y, direction, mask ) );
					Assert.IsFalse( WalkEdges.Blocked( grid.Cells, next.X, next.Y, (direction + 2) & 3, mask ) );
				}
			}
			for ( var i = 0; i < PoolBridge.Length; i++ )
			{
				var cell = bridge.Cell( i );
				Assert.AreEqual( i is >= 2 and <= 4 ? ParkCellType.PoolWater : ParkCellType.Empty, grid.Cells.TypeAt( cell.X, cell.Y ) );
				Assert.AreEqual( (byte)0, grid.Cells.LinksAt( cell.X, cell.Y ), "Routing must not write ground links." );
			}
			var route = new List<(int X, int Y)>();
			for ( var mask = 0; mask <= 2; mask++ )
			{
				Assert.IsTrue( WalkSearch.Find( grid.Cells, start, end, mask, route ) );
				Assert.AreEqual( end, route[route.Count - 1] );
			}
			Assert.AreEqual( PoolBridgeResult.Ok, world.RemoveAt( bridge.X, bridge.Y ) );
			Assert.AreEqual( -1, grid.Distance( start.X, start.Y, end.X, end.Y ) );
			Assert.AreEqual( 2, grid.WalkableCount );
		}
	}

	[TestMethod]
	public void SidePathsAndPermissiveMasksCannotEnterOrLeaveTheDeck()
	{
		foreach ( var direction in new[] { 1, 2 } )
		{
			var (grid, world, bridge) = Fixture( direction, pathApproaches: true );
			Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( bridge.X, bridge.Y, direction ) );
			for ( var i = 0; i < PoolBridge.Length; i++ )
			{
				var cell = bridge.Cell( i );
				grid.Cells.SetLinks( cell.X, cell.Y, 0x55 );
				foreach ( var side in new[] { (direction + 1) & 3, (direction + 3) & 3 } )
				{
					var (dx, dy) = GuestPathGrid.Directions[side];
					var adjacent = (X: cell.X + dx, Y: cell.Y + dy);
					foreach ( var type in new[] { ParkCellType.Empty, ParkCellType.Path, ParkCellType.PoolWater } )
					{
						grid.Cells.SetType( adjacent.X, adjacent.Y, type );
						grid.Cells.SetLinks( adjacent.X, adjacent.Y, 0x55 );
						grid.Cells.SetFlags( adjacent.X, adjacent.Y, (ParkCellFlags)0x400 );
						Assert.IsFalse( grid.AreConnected( cell.X, cell.Y, side ) );
						Assert.IsFalse( grid.AreConnected( adjacent.X, adjacent.Y, (side + 2) & 3 ) );
						for ( var mask = 0; mask <= 2; mask++ )
						{
							Assert.IsTrue( WalkEdges.Blocked( grid.Cells, cell.X, cell.Y, side, mask ) );
							Assert.IsTrue( WalkEdges.Blocked( grid.Cells, adjacent.X, adjacent.Y, (side + 2) & 3, mask ) );
						}
					}
				}
				Assert.AreEqual( 2, GuestWander.OriginalCandidates( grid, cell ) );
				var values = new Queue<uint>( new uint[] { 0, 0, 20, 30 } );
				var picked = GuestWander.Pick( grid, cell, () => values.Dequeue() );
				Assert.IsNotNull( picked );
				Assert.IsTrue( picked.Value.Cell == bridge.Cell( i - 1 ) || picked.Value.Cell == bridge.Cell( i + 1 ) );
				Assert.AreEqual( 0, values.Count );
			}
			Assert.AreEqual( 9, grid.WalkableCount, "Path approaches must not be counted twice." );
		}
	}

	[TestMethod]
	public void WanderCanEnterAnEmptyApproachFromAnUnlinkedOuterPath()
	{
		var (grid, world, bridge) = Fixture( 1 );
		Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( bridge.X, bridge.Y, bridge.Direction ) );
		var values = new Queue<uint>( new uint[] { 0, 20, 30 } );
		var picked = GuestWander.Pick( grid, bridge.Cell( -1 ), () => values.Dequeue() );
		Assert.AreEqual( new GuestWander.Destination( bridge.Cell( 0 ), 20, 30 ), picked );
		Assert.AreEqual( 0, values.Count, "A sole effective bridge link requires no candidate draw." );
	}

	[TestMethod]
	public void RestoringTheBridgeLayerRefreshesCachedFlowsWithoutChangingTheSavedMapVersion()
	{
		var (grid, world, bridge) = Fixture( 1 );
		var start = bridge.Cell( -1 );
		var end = bridge.Cell( PoolBridge.Length );
		Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( bridge.X, bridge.Y, bridge.Direction ) );
		var saved = world.CaptureSnapshot();
		var version = grid.Version;
		Assert.AreEqual( 8, grid.Distance( start.X, start.Y, end.X, end.Y ) );
		world.RestoreSnapshot( null );
		Assert.AreEqual( version, grid.Version );
		Assert.AreEqual( -1, grid.Distance( start.X, start.Y, end.X, end.Y ) );
		world.RestoreSnapshot( saved );
		Assert.AreEqual( version, grid.Version );
		Assert.AreEqual( 8, grid.Distance( start.X, start.Y, end.X, end.Y ) );
	}

	[TestMethod]
	public void BridgeWithoutEndPathsIsAnIslandUntilAPathIsLaidAndClosesAgainWhenItGoes()
	{
		foreach ( var direction in new[] { 0, 1, 2, 3 } )
		{
			var (grid, world, bridge) = Fixture( direction );
			var start = bridge.Cell( -1 );
			var end = bridge.Cell( PoolBridge.Length );
			grid.Cells.SetType( start.X, start.Y, ParkCellType.Empty );
			grid.Cells.SetType( end.X, end.Y, ParkCellType.Empty );
			Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( bridge.X, bridge.Y, direction ), "built with no path at either end" );
			var inside = bridge.Cell( 0 );
			var far = bridge.Cell( PoolBridge.Length - 1 );
			Assert.AreEqual( 0, grid.Distance( inside.X, inside.Y, inside.X, inside.Y ) );
			Assert.AreEqual( PoolBridge.Length - 1, grid.Distance( inside.X, inside.Y, far.X, far.Y ), "the deck links end to end by itself" );
			for ( var mask = 0; mask <= 2; mask++ )
				Assert.IsTrue( WalkEdges.Blocked( grid.Cells, start.X, start.Y, direction, mask ), "nothing leads from an open end onto the bridge" );
			// a path laid at the first end opens that entrance only
			grid.Cells.SetType( start.X, start.Y, ParkCellType.Path );
			Assert.AreEqual( PoolBridge.Length, grid.Distance( start.X, start.Y, far.X, far.Y ) );
			Assert.IsFalse( grid.AreConnected( far.X, far.Y, direction ), "the far end stays closed" );
			Assert.IsTrue( WalkEdges.Blocked( grid.Cells, far.X, far.Y, direction, 0 ) );
			for ( var mask = 0; mask <= 2; mask++ )
				Assert.IsFalse( WalkEdges.Blocked( grid.Cells, start.X, start.Y, direction, mask ), "the path opens the first entrance" );
			// and one at the second end completes the crossing, with every walk mask
			grid.Cells.SetType( end.X, end.Y, ParkCellType.Path );
			Assert.AreEqual( PoolBridge.Length + 1, grid.Distance( start.X, start.Y, end.X, end.Y ) );
			for ( var mask = 0; mask <= 2; mask++ )
				Assert.IsFalse( WalkEdges.Blocked( grid.Cells, far.X, far.Y, direction, mask ) );
			// the path removed again closes that entrance
			grid.Cells.SetType( start.X, start.Y, ParkCellType.Empty );
			Assert.IsFalse( grid.AreConnected( start.X, start.Y, direction ) );
			Assert.AreEqual( -1, grid.Distance( start.X, start.Y, end.X, end.Y ) );
		}
	}
}
