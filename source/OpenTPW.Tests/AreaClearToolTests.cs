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
		public readonly Dictionary<ushort, string> Objects = new();
		public readonly List<string> Removed = new();
		public readonly ParkPathBuilder Paths;
		public readonly AreaClearTool Tool;

		public Rig()
		{
			Paths = new ParkPathBuilder( Grid.Cells, Grid, (ParkEconomy?)null, new FlatGrid(), ( x, y ) => Grid.Cells.RawTypeAt( x, y ) == ParkCellTypes.Object );
			Tool = new AreaClearTool( ( a, b ) => ParkCommandResult.Ok( Clear( a, b ) ) );
		}

		/// <summary>An object of the cells x0..x1, y0..y1: type 4 with the owner word of its first cell, as the placement writes it.</summary>
		public void Place( string name, int x0, int y0, int x1, int y1 )
		{
			var owner = ParkCellMap.CellWord( x0, y0 );
			Objects[owner] = name;
			for ( var y = y0; y <= y1; y++ )
			{
				for ( var x = x0; x <= x1; x++ )
				{
					Grid.Cells.SetType( x, y, ParkCellTypes.Object );
					Grid.Cells.SetOwner( x, y, owner );
				}
			}
		}

		/// <summary>The rectangle's clear (<see cref="AreaClear.Run"/>) with this rig's removals, as the level's <see cref="ClearAreaCommand"/> runs it.</summary>
		public (int Objects, int Paths) Clear( (int X, int Y) a, (int X, int Y) b ) => AreaClear.Run( Grid.Cells, a, b, Remove, Paths.Remove );

		private bool Remove( int x, int y )
		{
			var owner = Grid.Cells.OwnerAt( x, y );
			for ( var cy = 0; cy < Grid.Cells.Height; cy++ )
				for ( var cx = 0; cx < Grid.Cells.Width; cx++ )
					if ( Grid.Cells.OwnerAt( cx, cy ) == owner )
						Grid.Cells.SetType( cx, cy, ParkCellType.Empty );
			Removed.Add( Objects[owner] );
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
		Assert.AreEqual( ParkCellTypes.Object, rig.Grid.Cells.RawTypeAt( 7, 3 ), "an object running past the rectangle stays" );
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
		Assert.AreEqual( (1, 0), rig.Clear( (2, 2), (4, 4) ) );
	}

	[TestMethod]
	public void AFlaggedCellOnTheScanKeepsTheObject()
	{
		var rig = new Rig();
		rig.Place( "kept", 3, 3, 4, 4 );
		// Every cell of the object met starts its own four scans: one flagged cell beyond the object only fails the scans through it.
		rig.Grid.Cells.SetFlags( 5, 3, (ParkCellFlags)0x10 );
		Assert.AreEqual( (1, 0), rig.Clear( (2, 2), (8, 8) ), "the scans from row 4 pass" );
		rig.Place( "kept", 3, 3, 4, 4 );
		rig.Grid.Cells.SetFlags( 5, 3, ParkCellFlags.None );
		// Flagging two opposite cells of the object fails a scan from each of its cells.
		rig.Grid.Cells.SetFlags( 3, 3, (ParkCellFlags)0x10 );
		rig.Grid.Cells.SetFlags( 4, 4, (ParkCellFlags)0x10 );
		Assert.AreEqual( (0, 0), rig.Clear( (2, 2), (8, 8) ) );
	}

	[TestMethod]
	public void AFixedPathCellWithNeighboursStays()
	{
		var rig = new Rig();
		rig.Paths.BuildSegment( (0, 2), (4, 2) );
		rig.Grid.Cells.SetFlags( 3, 2, ParkCellFlags.NoModify );
		var cleared = rig.Clear( (2, 0), (3, 4) );
		Assert.AreEqual( ParkCellType.Empty, rig.Grid.Cells.TypeAt( 2, 2 ) );
		Assert.AreEqual( ParkCellType.Path, rig.Grid.Cells.TypeAt( 3, 2 ), "NoModify with a neighbour (4, 2) stays" );
		Assert.AreEqual( (0, 1), cleared );
	}

	[TestMethod]
	public void TheScansFollowTheOwnerWordAndTheObjectTypeNotTheNeighbouringCells()
	{
		var rig = new Rig();
		// Two objects side by side: each is wholly inside the rectangle that holds only one of them, because a scan stops at a cell of another owner
		// (and is tested one cell beyond it, which still lies in the rectangle's reach).
		rig.Place( "left", 2, 2, 3, 3 );
		rig.Place( "right", 4, 2, 5, 3 );
		Assert.AreEqual( (1, 0), rig.Clear( (2, 2), (3, 3) ) );
		CollectionAssert.AreEqual( new[] { "left" }, rig.Removed );
		// A queue cell of the object's own owner word is not an object cell: it ends the run instead of extending it.
		rig.Place( "tall", 8, 8, 9, 9 );
		rig.Grid.Cells.SetType( 10, 8, ParkCellType.Queue );
		rig.Grid.Cells.SetOwner( 10, 8, ParkCellMap.CellWord( 8, 8 ) );
		rig.Grid.Cells.SetType( 11, 8, ParkCellTypes.Object );
		rig.Grid.Cells.SetOwner( 11, 8, ParkCellMap.CellWord( 8, 8 ) );
		Assert.AreEqual( (1, 0), rig.Clear( (7, 7), (10, 10) ), "the run to the right ends at the queue cell, one cell past the rectangle's edge at most" );
	}

	[TestMethod]
	public void AnAddOnFlagFromThePlacementKeepsTheObjectItBelongsTo()
	{
		// [BIN:TPW-EXE:0x0052950D '+' footprint cell] the placement sets the flag word 0x10 on the cell of a '+' shape cell, which keeps the area clear's scans from taking the object
		var rig = new Rig();
		var shape = new ObjectShape( new[] { "*++*", "*++*" } );
		var footprint = ObjectFootprint.GetCells( shape, 4, 4, 0 ).ToList();
		rig.Grid.Cells.WriteFootprint( footprint, ParkCellMap.CellWord( 4, 4 ) );
		rig.Objects[ParkCellMap.CellWord( 4, 4 )] = "add-on";
		Assert.IsTrue( footprint.Where( cell => cell.Cell.Symbol == '+' ).All( cell => rig.Grid.Cells.FlagsAt( cell.X, cell.Y ) == ParkCellFlags.AddOn ) );
		Assert.IsTrue( footprint.Where( cell => cell.Cell.Symbol == '*' ).All( cell => rig.Grid.Cells.FlagsAt( cell.X, cell.Y ) == ParkCellFlags.None ) );
		Assert.AreEqual( (0, 0), rig.Clear( (3, 3), (8, 6) ), "the scans along the rows meet the flagged cells" );
		Assert.IsTrue( rig.Removed.Count == 0 );
		rig.Grid.Cells.ClearFootprint( footprint );
		Assert.IsTrue( footprint.All( cell => rig.Grid.Cells.FlagsAt( cell.X, cell.Y ) == ParkCellFlags.None && rig.Grid.Cells.RawTypeAt( cell.X, cell.Y ) == 0 ), "removing the object takes the flag off again" );
	}

	[TestMethod]
	public void AParkWhoseObjectsDoNotTypeTheirCellsFollowsTheObjectOnTheCell()
	{
		var rig = new Rig();
		var objects = new Dictionary<(int, int), string>();
		for ( var y = 3; y <= 4; y++ )
			for ( var x = 3; x <= 4; x++ )
				objects[(x, y)] = "inc";
		var removed = new List<string>();
		Assert.AreEqual( (1, 0), AreaClear.Run( rig.Grid.Cells, (2, 2), (6, 6), ( x, y ) =>
		{
			var name = objects[(x, y)];
			removed.Add( name );
			foreach ( var cell in objects.Where( pair => pair.Value == name ).Select( pair => pair.Key ).ToArray() )
				objects.Remove( cell );
			return true;
		}, rig.Paths.Remove, ( x, y ) => objects.TryGetValue( (x, y), out var name ) ? name : null ) );
		CollectionAssert.AreEqual( new[] { "inc" }, removed );
	}
}
