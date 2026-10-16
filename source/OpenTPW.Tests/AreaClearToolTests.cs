using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The area clear tool (tp.exe mode 0x3A, the clear <c>0x52BDD0</c>): two clicks, objects wholly inside, path cells, queues staying.</summary>
[TestClass]
public class AreaClearToolTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 16;
		public int Height => 16;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) =>
			x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private sealed class Rig
	{
		public readonly GuestPathGrid Grid = new( 16, 16 );
		public readonly Dictionary<(int, int), string> Objects = new();
		public readonly List<string> Removed = new();
		public readonly ParkPathBuilder Paths;
		public readonly AreaClearTool Tool;

		public Rig()
		{
			Paths = new ParkPathBuilder( Grid.Cells, Grid, (ParkEconomy?)null, new FlatGrid(), ( x, y ) => Objects.ContainsKey( (x, y) ) );
			Tool = new AreaClearTool( ( x, y ) => Objects.TryGetValue( (x, y), out var name ) ? name : null, Remove, () => Grid.Cells, Paths.Remove );
		}

		public void Place( string name, int x0, int y0, int x1, int y1 )
		{
			for ( var y = y0; y <= y1; y++ )
				for ( var x = x0; x <= x1; x++ )
					Objects[(x, y)] = name;
		}

		private bool Remove( int x, int y )
		{
			var name = Objects[(x, y)];
			foreach ( var cell in Objects.Where( pair => pair.Value == name ).Select( pair => pair.Key ).ToArray() )
				Objects.Remove( cell );
			Removed.Add( name );
			return true;
		}
	}

	[TestMethod]
	public void TwoClicksClearTheRectangleAndEndTheTool()
	{
		var rig = new Rig();
		rig.Tool.Begin();
		Assert.AreEqual( AreaClearClick.CornerStored, rig.Tool.Click( 2, 2 ) );
		Assert.IsTrue( rig.Tool.IsActive );
		rig.Tool.Hover( 6, 5 );
		Assert.AreEqual( 2 * 5 + 2 * 2, rig.Tool.PreviewEdge().Count(), "the ghost is the rectangle's edge" );
		Assert.AreEqual( AreaClearClick.Cleared, rig.Tool.Click( 6, 5 ) );
		Assert.IsFalse( rig.Tool.IsActive );
		Assert.AreEqual( AreaClearClick.Ignored, rig.Tool.Click( 6, 5 ) );
	}

	[TestMethod]
	public void ObjectsWhollyInsideAndPathCellsGoQueuesAndStraddlingObjectsStay()
	{
		var rig = new Rig();
		rig.Paths.BuildSegment( (0, 8), (10, 8) );
		rig.Place( "inside", 3, 3, 4, 5 );
		rig.Place( "straddling", 7, 3, 9, 4 );
		rig.Grid.SetQueue( 5, 7, 0 );
		rig.Tool.Begin();
		rig.Tool.Click( 2, 2 );
		rig.Tool.Click( 8, 8 );
		CollectionAssert.AreEqual( new[] { "inside" }, rig.Removed );
		Assert.IsTrue( rig.Objects.ContainsKey( (7, 3) ), "an object running past the rectangle stays" );
		Assert.AreEqual( (1, 7), rig.Tool.LastCleared );
		Assert.AreEqual( ParkCellType.Queue, rig.Grid.Cells.TypeAt( 5, 7 ), "queue cells stay" );
		for ( var x = 2; x <= 8; x++ )
			Assert.AreEqual( ParkCellType.Empty, rig.Grid.Cells.TypeAt( x, 8 ) );
		Assert.AreEqual( ParkCellType.Path, rig.Grid.Cells.TypeAt( 1, 8 ) );
		Assert.AreEqual( 0, rig.Grid.Cells.LinksAt( 1, 8 ) & 0x04, "the cleared path is unlinked from the one outside" );
	}

	[TestMethod]
	public void AnObjectTouchingTheEdgeFromInsideGoes()
	{
		var rig = new Rig();
		rig.Place( "edge", 2, 2, 4, 4 );
		Assert.AreEqual( (1, 0), rig.Tool.Clear( (2, 2), (4, 4) ) );
	}

	[TestMethod]
	public void AFlaggedCellOnTheScanKeepsTheObject()
	{
		var rig = new Rig();
		rig.Place( "kept", 3, 3, 4, 4 );
		// Every cell of the object met starts its own four scans: one flagged cell beyond the object only fails the scans through it.
		rig.Grid.Cells.SetFlags( 5, 3, (ParkCellFlags)0x10 );
		Assert.AreEqual( (1, 0), rig.Tool.Clear( (2, 2), (8, 8) ), "the scans from row 4 pass" );
		rig.Place( "kept", 3, 3, 4, 4 );
		rig.Grid.Cells.SetFlags( 5, 3, ParkCellFlags.None );
		// Flagging two opposite cells of the object fails a scan from each of its cells.
		rig.Grid.Cells.SetFlags( 3, 3, (ParkCellFlags)0x10 );
		rig.Grid.Cells.SetFlags( 4, 4, (ParkCellFlags)0x10 );
		Assert.AreEqual( (0, 0), rig.Tool.Clear( (2, 2), (8, 8) ) );
	}

	[TestMethod]
	public void AFixedPathCellWithNeighboursStays()
	{
		var rig = new Rig();
		rig.Paths.BuildSegment( (0, 2), (4, 2) );
		rig.Grid.Cells.SetFlags( 3, 2, ParkCellFlags.NoModify );
		var cleared = rig.Tool.Clear( (2, 0), (3, 4) );
		Assert.AreEqual( ParkCellType.Empty, rig.Grid.Cells.TypeAt( 2, 2 ) );
		Assert.AreEqual( ParkCellType.Path, rig.Grid.Cells.TypeAt( 3, 2 ), "NoModify with a neighbour (4, 2) stays" );
		Assert.AreEqual( (0, 1), cleared );
	}
}
