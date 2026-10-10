using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Asset-free tests of the path builder traced in docs/reverse/PATH-plan.md: LayLine and its snap, the per-cell
/// validation order, the per-cell charge, removal without refund, NoModify, the shared path/queue tool and the
/// cell map as the guests' source of truth.
/// </summary>
[TestClass]
public class PathBuilderTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private const int PathCost = 20; // EconomyTestData: Costs.PathCell 20

	/// <summary>A flat build grid where chosen cells fail the terrain rule.</summary>
	private sealed class TestGrid : IParkGrid
	{
		public readonly Dictionary<(int, int), OriginalPlacementResult> Terrain = new();
		public int Width { get; init; } = 16;
		public int Height { get; init; } = 16;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) =>
			x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : Terrain.TryGetValue( (x, y), out var result ) ? result : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private sealed class Rig
	{
		public readonly GuestPathGrid Grid = new( 16, 16 );
		public readonly TestGrid Terrain = new();
		public readonly HashSet<(int, int)> Objects = new();
		public readonly ParkEconomy? Economy;
		public readonly ParkPathBuilder Builder;

		public Rig( bool economy = true, int cash = 50000 )
		{
			Economy = economy ? EconomyTestData.Park( initialCash: cash ) : null;
			Builder = new ParkPathBuilder( Grid.Cells, Grid, Economy, Terrain, ( x, y ) => Objects.Contains( (x, y) ) );
		}

		public ParkCellMap Cells => Grid.Cells;
		public long OtherCosts => Economy!.Ledger.CurrentTotals.TryGetValue( LedgerCategory.OtherCosts, out var value ) ? value : 0;
	}

	// ---- LayLine and the snap -----------------------------------------------------------------------

	[TestMethod]
	public void SnapKeepsTheDominantAxisAndATieKeepsX()
	{
		Assert.AreEqual( (9, 2), ParkPathBuilder.SnapEnd( (2, 2), (9, 5) ), "|dx| > |dy| keeps X" );
		Assert.AreEqual( (2, 9), ParkPathBuilder.SnapEnd( (2, 2), (5, 9) ), "|dy| > |dx| keeps Y" );
		Assert.AreEqual( (6, 2), ParkPathBuilder.SnapEnd( (2, 2), (6, 6) ), "a tie keeps X" );
		Assert.AreEqual( (-1, 2), ParkPathBuilder.SnapEnd( (2, 2), (-1, 5) ), "a tie toward −X keeps X" );
		Assert.AreEqual( (2, 2), ParkPathBuilder.SnapEnd( (2, 2), (2, 2) ) );
	}

	[TestMethod]
	public void LayLineWalksOneAxisAndIgnoresTheOtherEndCoordinate()
	{
		CollectionAssert.AreEqual( new[] { (2, 3), (3, 3), (4, 3), (5, 3) }, ParkPathBuilder.LayLine( (2, 3), (5, 7 - 4) ).ToArray() );
		CollectionAssert.AreEqual( new[] { (2, 3), (3, 3), (4, 3), (5, 3) }, ParkPathBuilder.LayLine( (2, 3), (5, 4) ).ToArray(), "|dx| > |dy|: row y0, end y ignored" );
		CollectionAssert.AreEqual( new[] { (2, 3), (2, 2), (2, 1) }, ParkPathBuilder.LayLine( (2, 3), (3, 1) ).ToArray(), "|dy| ≥ |dx|: column x0, end x ignored" );
		CollectionAssert.AreEqual( new[] { (2, 3), (2, 4), (2, 5) }, ParkPathBuilder.LayLine( (2, 3), (4, 5) ).ToArray(), "LayLine's own test is strict: an unsnapped tie walks Y" );
		CollectionAssert.AreEqual( new[] { (5, 0), (4, 0), (3, 0) }, ParkPathBuilder.LayLine( (5, 0), (3, 0) ).ToArray() );
		CollectionAssert.AreEqual( new[] { (4, 4) }, ParkPathBuilder.LayLine( (4, 4), (4, 4) ).ToArray() );
	}

	[TestMethod]
	public void BuildSegmentSnapsATieAlongX()
	{
		var rig = new Rig();
		var result = rig.Builder.BuildSegment( (2, 2), (5, 5) );
		Assert.AreEqual( (5, 2), result.SnappedEnd );
		CollectionAssert.AreEqual( new[] { (2, 2), (3, 2), (4, 2), (5, 2) }, result.Built.ToArray() );
		Assert.AreEqual( ParkCellType.Empty, rig.Cells.TypeAt( 2, 3 ) );
	}

	// ---- Validation (PATH-plan §9.2, in order) ------------------------------------------------------

	[TestMethod]
	public void ValidationFollowsTheTracedOrder()
	{
		var rig = new Rig();
		var builder = rig.Builder;
		Assert.AreEqual( CellBuildResult.Ok, builder.CheckCell( 1, 1 ) );
		Assert.AreEqual( CellBuildResult.OutsideTerrain, builder.CheckCell( -1, 0 ) );
		Assert.AreEqual( CellBuildResult.OutsideTerrain, builder.CheckCell( 16, 0 ) );

		// 2. Unowned land (flag 0x40) is refused before anything else on the cell.
		rig.Cells.SetFlags( 1, 1, ParkCellFlags.Unowned );
		rig.Objects.Add( (1, 1) );
		rig.Terrain.Terrain[(1, 1)] = OriginalPlacementResult.Water;
		Assert.AreEqual( CellBuildResult.NotOwned, builder.CheckCell( 1, 1 ) );
		rig.Cells.SetFlags( 1, 1, ParkCellFlags.None );
		// 3. CanChangeCellType: any other original type (4 = ride, 9 = queue end, 21, …) is refused.
		foreach ( var type in new byte[] { 2, 4, 5, 7, 9, 10, 11, 16, 21, 24, 30 } )
		{
			rig.Cells.SetType( 2, 2, type );
			Assert.AreEqual( CellBuildResult.Occupied, builder.CheckCell( 2, 2 ), $"type {type}" );
		}
		rig.Cells.SetType( 2, 2, ParkCellType.Empty );
		// 4. Path over a queue cell (CanChangeCellType allows it, the validator refuses: PATH-008).
		rig.Grid.SetQueue( 3, 3, 0 );
		Assert.AreEqual( CellBuildResult.QueueCell, builder.CheckCell( 3, 3 ) );
		// 5. Object footprints and fixed items.
		Assert.AreEqual( CellBuildResult.Occupied, builder.CheckCell( 1, 1 ), "the object is checked before the terrain" );
		rig.Objects.Clear();
		// 6. The object terrain rule (water, entrance area, …); the save's own path cells stay buildable.
		Assert.AreEqual( CellBuildResult.Terrain, builder.CheckCell( 1, 1 ) );
		rig.Terrain.Terrain[(1, 1)] = OriginalPlacementResult.EntranceArea;
		Assert.AreEqual( CellBuildResult.Terrain, builder.CheckCell( 1, 1 ) );
		rig.Terrain.Terrain[(1, 1)] = OriginalPlacementResult.Path;
		Assert.AreEqual( CellBuildResult.Ok, builder.CheckCell( 1, 1 ), "a removed save path cell can be paved again" );
		// Path over path is allowed before the object and terrain checks (validator 0x84864).
		rig.Grid.SetPath( 4, 4, true );
		rig.Objects.Add( (4, 4) );
		Assert.AreEqual( CellBuildResult.Existing, builder.CheckCell( 4, 4 ) );
	}

	[TestMethod]
	public void MoneyTestIsBalanceMinusCostAtLeastZero()
	{
		var rig = new Rig( cash: 2 * PathCost );
		var result = rig.Builder.BuildSegment( (0, 0), (2, 0) );
		CollectionAssert.AreEqual( new[] { (0, 0), (1, 0) }, result.Built.ToArray(), "balance − cost = 0 is still affordable" );
		Assert.AreEqual( CellBuildResult.NotEnoughMoney, result.StoppedBy );
		Assert.AreEqual( (2, 0), result.StoppedAt );
		Assert.AreEqual( 0, rig.Economy!.Balance );
		Assert.AreEqual( ParkCellType.Empty, rig.Cells.TypeAt( 2, 0 ) );
		Assert.AreEqual( CellBuildResult.NotEnoughMoney, rig.Builder.CheckCell( 5, 5 ) );
		Assert.AreEqual( CellBuildResult.Existing, rig.Builder.CheckCell( 0, 0 ), "an existing path needs no money" );
	}

	// ---- Cost ---------------------------------------------------------------------------------------

	[TestMethod]
	public void EachNewCellIsChargedOnceWhenWrittenAndPathOverPathIsFree()
	{
		var rig = new Rig();
		var balance = rig.Economy!.Balance;
		var first = rig.Builder.BuildSegment( (1, 1), (4, 1) );
		Assert.IsTrue( first.Completed );
		Assert.AreEqual( 4, first.Built.Count );
		Assert.AreEqual( 4L * PathCost, first.Charged );
		Assert.AreEqual( balance - 4 * PathCost, rig.Economy.Balance );
		Assert.AreEqual( 4L * PathCost, rig.OtherCosts, "posted as other costs (PATH-010)" );

		// Over the same cells plus two new ones: only the new cells are charged; the old ones bump their counter.
		var second = rig.Builder.BuildSegment( (3, 1), (6, 1) );
		CollectionAssert.AreEqual( new[] { (5, 1), (6, 1) }, second.Built.ToArray() );
		CollectionAssert.AreEqual( new[] { (3, 1), (4, 1) }, second.Existing.ToArray() );
		Assert.AreEqual( 2L * PathCost, second.Charged );
		Assert.AreEqual( balance - 6 * PathCost, rig.Economy.Balance );
		Assert.AreEqual( (short)1, rig.Cells.PlacementCountAt( 3, 1 ) );
		Assert.AreEqual( (short)0, rig.Cells.PlacementCountAt( 5, 1 ) );
		Assert.IsTrue( second.EndedOnExisting == false );
		Assert.IsTrue( rig.Builder.BuildSegment( (8, 1), (6, 1) ).EndedOnExisting, "the last cell landed on a path" );
	}

	[TestMethod]
	public void ARefusedCellStopsTheLineAndEarlierCellsStayCharged()
	{
		var rig = new Rig();
		rig.Objects.Add( (4, 2) );
		var balance = rig.Economy!.Balance;
		Assert.IsFalse( rig.Builder.TryLayLine( (1, 2), (7, 2), out var result ) );
		CollectionAssert.AreEqual( new[] { (1, 2), (2, 2), (3, 2) }, result.Built.ToArray() );
		Assert.AreEqual( CellBuildResult.Occupied, result.StoppedBy );
		Assert.AreEqual( balance - 3 * PathCost, rig.Economy.Balance );
		Assert.AreEqual( ParkCellType.Empty, rig.Cells.TypeAt( 5, 2 ) );
	}

	[TestMethod]
	public void WithoutAnEconomyPathsAreFree()
	{
		var rig = new Rig( economy: false );
		var result = rig.Builder.BuildSegment( (0, 0), (0, 5) );
		Assert.AreEqual( 6, result.Built.Count );
		Assert.AreEqual( 0, result.Charged );
	}

	[TestMethod]
	public void RemovalRefundsNothing()
	{
		var rig = new Rig();
		rig.Builder.BuildSegment( (1, 1), (3, 1) );
		var balance = rig.Economy!.Balance;
		var costs = rig.OtherCosts;
		Assert.AreEqual( CellBuildResult.Ok, rig.Builder.Remove( 2, 1 ) );
		Assert.AreEqual( balance, rig.Economy.Balance, "ClearCell's path case calls no Earn" );
		Assert.AreEqual( costs, rig.OtherCosts );
		Assert.AreEqual( ParkCellType.Empty, rig.Cells.TypeAt( 2, 1 ) );
		Assert.AreEqual( 0, rig.Cells.LinksAt( 1, 1 ) & GuestPathGrid.LinkValue( 1 ), "the neighbour's link toward the removed cell is cleared" );
		Assert.AreEqual( 0, rig.Cells.LinksAt( 3, 1 ) & GuestPathGrid.LinkValue( 3 ) );
		Assert.AreEqual( CellBuildResult.NotPath, rig.Builder.Remove( 2, 1 ) );
		Assert.AreEqual( CellBuildResult.NotPath, rig.Builder.Remove( 9, 9 ) );
		// A path cell placed several times is still removed: the player's removal is forced (a = b = 0).
		rig.Builder.BuildSegment( (1, 1), (1, 1) );
		Assert.AreEqual( (short)1, rig.Cells.PlacementCountAt( 1, 1 ) );
		Assert.AreEqual( CellBuildResult.Ok, rig.Builder.Remove( 1, 1 ) );
		Assert.AreEqual( (short)0, rig.Cells.PlacementCountAt( 1, 1 ) );
	}

	[TestMethod]
	public void NoModifyCellsStayWhileTheyHaveNeighbours()
	{
		var rig = new Rig();
		rig.Grid.SetPath( 5, 5, true );
		rig.Grid.SetPath( 6, 5, true );
		rig.Cells.SetFlags( 5, 5, ParkCellFlags.NoModify );
		Assert.AreEqual( CellBuildResult.NoModify, rig.Builder.Remove( 5, 5 ) );
		Assert.AreEqual( ParkCellType.Path, rig.Cells.TypeAt( 5, 5 ) );
		// Building over a fixed path is a free same-type placement (SetCellType bumps the counter).
		var balance = rig.Economy!.Balance;
		Assert.AreEqual( CellBuildResult.Existing, rig.Builder.CheckCell( 5, 5 ) );
		Assert.AreEqual( 0, rig.Builder.BuildSegment( (5, 5), (5, 5) ).Charged );
		Assert.AreEqual( balance, rig.Economy.Balance );
		Assert.IsTrue( rig.Cells.FlagsAt( 5, 5 ).HasFlag( ParkCellFlags.NoModify ) );
		// Without a neighbour the flag is dropped ("Removing path cell with no neighbours but NOMODIFY set").
		Assert.AreEqual( CellBuildResult.Ok, rig.Builder.Remove( 6, 5 ) );
		Assert.AreEqual( CellBuildResult.Ok, rig.Builder.Remove( 5, 5 ) );
		Assert.AreEqual( ParkCellFlags.None, rig.Cells.FlagsAt( 5, 5 ) );
	}

	// ---- Links and the guests' walk graph ----------------------------------------------------------

	[TestMethod]
	public void NewCellsLinkToCardinalPathNeighboursAndGuestsWalkThem()
	{
		var rig = new Rig();
		rig.Grid.SetPath( 0, 3, true, 0 ); // an existing cell that links nothing
		var version = rig.Grid.Version;
		Assert.AreEqual( -1, rig.Grid.Distance( 0, 3, 4, 0 ) );
		rig.Builder.BuildSegment( (1, 3), (4, 3) );
		rig.Builder.BuildSegment( (4, 2), (4, 0) );
		Assert.AreNotEqual( version, rig.Grid.Version, "builder writes go straight to the map and move the walk graph's version" );
		Assert.AreEqual( 7, rig.Grid.Distance( 0, 3, 4, 0 ) );
		Assert.AreEqual( (byte)(GuestPathGrid.LinkValue( 1 ) | GuestPathGrid.LinkValue( 3 )), rig.Cells.LinksAt( 2, 3 ) );
		Assert.AreEqual( GuestPathGrid.LinkValue( 1 ), rig.Cells.LinksAt( 0, 3 ), "the old cell gains the link back" );
		Assert.AreEqual( (byte)(GuestPathGrid.LinkValue( 0 ) | GuestPathGrid.LinkValue( 3 )), rig.Cells.LinksAt( 4, 3 ), "a corner links −Y and −X" );
		Assert.AreEqual( 0, rig.Cells.LinksAt( 5, 3 ) );
		// A guest walks the built path.
		var simulation = new GuestSimulation( rig.Grid, GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 0, 3 );
		Assert.IsTrue( rig.Grid.TryStep( 0, 3, 4, 0, out var nx, out var ny ) && (nx, ny) == (1, 3) );
		for ( var tick = 0; tick < 60 * 30; tick++ )
			simulation.Tick( Tick );
		Assert.IsTrue( rig.Grid.IsWalkable( guest.Cell.X, guest.Cell.Y ), $"the guest stays on the path at {guest.Cell}" );
		Assert.IsTrue( guest.DistanceWalked > 0 );
		// Removing a cell cuts the route.
		rig.Builder.Remove( 4, 1 );
		Assert.AreEqual( -1, rig.Grid.Distance( 0, 3, 4, 0 ) );
	}

	[TestMethod]
	public void QueueCellsKeepWorkingOnTheSharedMap()
	{
		var rig = new Rig();
		rig.Grid.SetQueue( 6, 6, 0 );
		Assert.AreEqual( 1, rig.Grid.QueueCellCount );
		rig.Builder.BuildSegment( (5, 6), (7, 6) );
		Assert.AreEqual( ParkCellType.Path, rig.Cells.TypeAt( 5, 6 ) );
		Assert.AreEqual( ParkCellType.Queue, rig.Cells.TypeAt( 6, 6 ), "the path stops at the queue cell" );
		Assert.AreEqual( GuestPathGrid.LinkValue( 0 ), rig.Grid.GetQueueLink( 6, 6 ) );
		Assert.AreEqual( 0, rig.Cells.LinksAt( 5, 6 ) & GuestPathGrid.LinkValue( 1 ), "a path cell does not link to a queue cell" );
		Assert.AreEqual( 1, rig.Grid.WalkableCount );
	}

	[TestMethod]
	public void TheCellMapIsTheGuestsSourceOfTruth()
	{
		var cells = new ParkCellMap( 8, 8 );
		var grid = new GuestPathGrid( cells );
		Assert.AreSame( cells, grid.Cells );
		cells.SetType( 2, 2, ParkCellType.Path );
		cells.SetType( 3, 2, ParkCellType.Path );
		Assert.IsTrue( grid.IsWalkable( 2, 2 ) );
		Assert.AreEqual( 2, grid.WalkableCount );
		Assert.AreEqual( -1, grid.Distance( 2, 2, 3, 2 ), "no links yet" );
		cells.SetLinks( 2, 2, GuestPathGrid.LinkValue( 1 ) );
		Assert.AreEqual( 1, grid.Distance( 2, 2, 3, 2 ), "the cached field follows the map's version" );
		Assert.AreEqual( 0b0010, GuestPathGrid.FromCellLinks( GuestPathGrid.LinkValue( 1 ) ) );
		Assert.ThrowsException<ArgumentException>( () => new ParkPathBuilder( new ParkCellMap( 8, 8 ), grid, (ParkEconomy?)null, new TestGrid() ) );
	}

	/// <summary>
	/// The guests' run on the cross grid has the same raw hash before and after the cell map became the source of
	/// truth (value recorded on 025d410 + the research commit, before the flip).
	/// </summary>
	[TestMethod]
	public void OwnershipFlipKeepsGuestPathingIdentical()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		for ( var tick = 0; tick < 120 * 60; tick++ )
			simulation.Tick( Tick );
		Assert.AreEqual( 92, simulation.Admissions );
		Assert.AreEqual( 0x3622300C23D95AB8UL, simulation.ComputeStateHash(), $"0x{simulation.ComputeStateHash():X16}" );
	}

	[TestMethod]
	public void TheCanonicalHashCoversTheCellMap()
	{
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		var before = WorldStateHash.Compute( new WorldStateSources { Guests = guests } );
		var builder = new ParkPathBuilder( guests.Grid.Cells, guests.Grid, (ParkEconomy?)null, new TestGrid { Width = 20, Height = 10 } );
		builder.BuildSegment( (0, 0), (3, 0) );
		var built = WorldStateHash.Compute( new WorldStateSources { Guests = guests } );
		Assert.AreNotEqual( before, built );
		builder.BuildSegment( (0, 0), (0, 0) );
		Assert.AreNotEqual( built, WorldStateHash.Compute( new WorldStateSources { Guests = guests } ), "the placement counter is hashed" );
		var other = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		Assert.AreEqual( before, WorldStateHash.Compute( new WorldStateSources { Guests = other } ) );
	}

	// ---- The shared tool (PATH-plan §4.3, §9.4) -----------------------------------------------------

	[TestMethod]
	public void ToolPreviewsWithoutWritingAndRefusesALineItCannotPay()
	{
		var rig = new Rig( cash: 3 * PathCost );
		var tool = new CellBuildTool();
		tool.Enter( rig.Builder, (1, 1) );
		Assert.AreEqual( CellToolMode.Path, tool.Mode );
		var ghost = tool.Hover( (6, 3) )!;
		Assert.AreEqual( (6, 1), ghost.SnappedEnd );
		Assert.AreEqual( 6, ghost.Line.Count );
		Assert.AreEqual( 3, ghost.Built.Count, "the pending line cost reaches the balance after three cells" );
		Assert.AreEqual( CellBuildResult.NotEnoughMoney, ghost.StoppedBy );
		Assert.AreEqual( 3L * PathCost, ghost.Charged );
		Assert.AreEqual( 0, rig.Grid.WalkableCount, "the preview writes nothing" );
		Assert.AreEqual( 3L * PathCost, rig.Economy!.Balance );
		Assert.AreSame( ghost, tool.Ghost );
	}

	[TestMethod]
	public void ToolCommitsSegmentsAndContinuesFromTheEnd()
	{
		var rig = new Rig();
		var tool = new CellBuildTool();
		var ended = 0;
		tool.Ended += () => ended++;
		Assert.IsNull( tool.Click( (3, 3) ), "the tool is off" );
		tool.Enter( rig.Builder, (1, 1) );
		var first = tool.Click( (5, 2) )!;
		Assert.AreEqual( (5, 1), first.SnappedEnd );
		Assert.AreEqual( 5, first.Built.Count );
		Assert.AreEqual( (5, 1), tool.Start, "the end becomes the next start" );
		CollectionAssert.AreEqual( new[] { (1, 1), (5, 1) }, tool.Vertices.ToArray() );
		Assert.IsNull( tool.Ghost );
		var second = tool.Click( (4, 6) )!;
		Assert.AreEqual( (5, 6), second.SnappedEnd );
		CollectionAssert.AreEqual( new[] { (5, 1) }, second.Existing.ToArray(), "the joint is the previous end: free, counter bumped" );
		Assert.AreEqual( 5, second.Built.Count );
		Assert.AreEqual( CellToolMode.Path, tool.Mode );
		// Clicking the start again ends the tool ("click again to stop building").
		tool.Click( (5, 6) );
		Assert.IsFalse( tool.IsActive );
		Assert.IsNull( tool.Start );
		Assert.AreEqual( 0, tool.Vertices.Count );
		Assert.AreEqual( 1, ended );
		Assert.AreEqual( 10, rig.Grid.WalkableCount );
	}

	[TestMethod]
	public void ToolEndsOnARefusalOrOnAnExistingPath()
	{
		var rig = new Rig();
		rig.Objects.Add( (4, 1) );
		var tool = new CellBuildTool();
		tool.Enter( rig.Builder, (1, 1) );
		var refused = tool.Click( (8, 1) )!;
		Assert.AreEqual( CellBuildResult.Occupied, refused.StoppedBy );
		Assert.IsFalse( tool.IsActive, "a failed LayLine ends the tool" );
		Assert.AreEqual( 3, rig.Grid.WalkableCount, "cells before the refusal stay" );

		rig.Grid.SetPath( 1, 6, true );
		tool.Enter( rig.Builder, (1, 2) );
		var joined = tool.Click( (1, 6) )!;
		Assert.IsTrue( joined.EndedOnExisting );
		Assert.IsFalse( tool.IsActive, "ending on an existing path completes it (PATH-003)" );
		Assert.AreEqual( 5, rig.Grid.Distance( 1, 1, 1, 6 ), "the new line joins (1, 1) to the old path" );
	}

	[TestMethod]
	public void ASingleClickOnTheStartLaysOneCellAndEnds()
	{
		var rig = new Rig();
		var tool = new CellBuildTool();
		tool.Enter( rig.Builder, (2, 2) );
		var result = tool.Click( (2, 2) )!;
		CollectionAssert.AreEqual( new[] { (2, 2) }, result.Built.ToArray() );
		Assert.IsFalse( tool.IsActive );
		Assert.AreEqual( 1, rig.Grid.WalkableCount );
	}

	[TestMethod]
	public void UndoRemovesTheLastSegmentWithoutARefund()
	{
		var rig = new Rig();
		var tool = new CellBuildTool();
		tool.Enter( rig.Builder, (1, 1) );
		tool.Click( (4, 1) );
		tool.Click( (4, 4) );
		var balance = rig.Economy!.Balance;
		Assert.AreEqual( 7, rig.Grid.WalkableCount );
		Assert.AreEqual( (short)1, rig.Cells.PlacementCountAt( 4, 1 ) );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 4, rig.Grid.WalkableCount );
		Assert.AreEqual( (short)0, rig.Cells.PlacementCountAt( 4, 1 ), "the joint's counter bump is taken back" );
		Assert.AreEqual( 0, rig.Cells.LinksAt( 4, 1 ) & GuestPathGrid.LinkValue( 2 ) );
		Assert.AreEqual( balance, rig.Economy.Balance, "no refund (PATH-002)" );
		Assert.AreEqual( (4, 1), tool.Start );
		CollectionAssert.AreEqual( new[] { (1, 1), (4, 1) }, tool.Vertices.ToArray() );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 0, rig.Grid.WalkableCount );
		Assert.AreEqual( (1, 1), tool.Start );
		Assert.IsFalse( tool.Undo(), "nothing left to undo" );
		Assert.IsTrue( tool.IsActive );
		tool.Cancel();
		Assert.IsFalse( tool.IsActive );
		Assert.IsFalse( tool.Undo() );
	}

	[TestMethod]
	public void CancelWritesNothing()
	{
		var rig = new Rig();
		var tool = new CellBuildTool();
		tool.Enter( rig.Builder, (1, 1) );
		tool.Hover( (7, 1) );
		tool.Cancel();
		Assert.AreEqual( 0, rig.Grid.WalkableCount );
		Assert.IsNull( tool.Ghost );
		Assert.IsNull( tool.Hover( (3, 1) ), "no ghost while the tool is off" );
	}

	[TestMethod]
	public void TheVertexStackHoldsTenTwentyFourPoints()
	{
		var grid = new GuestPathGrid( 1024, 2 );
		var builder = new ParkPathBuilder( grid.Cells, grid, (ParkEconomy?)null, new TestGrid { Width = 1024, Height = 2 } );
		var tool = new CellBuildTool();
		tool.Enter( builder, (0, 0) );
		for ( var x = 1; x < CellBuildTool.VertexCapacity; x++ )
			tool.Click( (x, 0) );
		Assert.IsTrue( tool.IsActive );
		Assert.AreEqual( CellBuildTool.VertexCapacity, tool.Vertices.Count );
		// A full stack takes no more vertices: the line is still laid, then the tool ends.
		tool.Click( (1023, 1) );
		Assert.IsFalse( tool.IsActive );
		Assert.AreEqual( ParkCellType.Path, grid.Cells.TypeAt( 1023, 1 ) );
		Assert.AreEqual( 1025, grid.WalkableCount );
	}

	[TestMethod]
	public void HoverHelpPicksTheTracedLines()
	{
		var rig = new Rig();
		var tool = new CellBuildTool();
		rig.Grid.SetPath( 2, 2, true );
		rig.Grid.SetQueue( 3, 3, 0 );
		rig.Objects.Add( (4, 4) );
		Assert.AreEqual( 441, tool.HoverHelpId( rig.Builder, 1, 1 ), "empty owned cell: build path" );
		Assert.AreEqual( 442, tool.HoverHelpId( rig.Builder, 2, 2 ), "path: extend this path" );
		Assert.AreEqual( 444, tool.HoverHelpId( rig.Builder, 3, 3 ), "queue: edit this queue" );
		Assert.IsNull( tool.HoverHelpId( rig.Builder, 4, 4 ), "an object" );
		rig.Cells.SetFlags( 5, 5, ParkCellFlags.Unowned );
		Assert.IsNull( tool.HoverHelpId( rig.Builder, 5, 5 ), "land the park does not own" );
		Assert.IsNull( tool.HoverHelpId( null, 1, 1 ) );
		tool.Enter( rig.Builder, (1, 1) );
		Assert.AreEqual( 443, tool.HoverHelpId( rig.Builder, 4, 4 ), "inside the path tool" );
	}

	[TestMethod]
	public void TheQueueModeSharesTheTool()
	{
		var grid = new GuestPathGrid( 12, 12 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 0, true );
		var ride = new RideVisitorBridge( 1, "Queue ride", RideVisitorKind.Ride, 5, 55, 30 )
		{
			EntranceCell = (2, 0),
			ExitCell = (2, 0),
			QueueFrontCell = (2, 5),
			QueueEntranceDirection = 2,
			Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
		};
		var charged = 0;
		var writer = new QueueLineWriter( grid, ride, ( x, y ) => QueuePaths.CheckExtend( grid, ride, x, y ),
			( x, y ) =>
			{
				var result = QueuePaths.TryExtend( grid, ride, x, y );
				charged += result == QueueBuildResult.Ok ? 75 : 0;
				return result;
			}, ( x, y ) => QueuePaths.RemoveFrom( grid, ride, x, y ), () => 75 );
		var tool = new CellBuildTool();
		tool.Enter( writer, (2, 5) );
		Assert.AreEqual( CellToolMode.Queue, tool.Mode );
		Assert.AreEqual( 445, tool.HoverHelpId( null, 0, 0 ) );
		var ghost = tool.Hover( (2, 3) )!;
		Assert.AreEqual( 3, ghost.Built.Count );
		Assert.AreEqual( 0, grid.QueueCellCount );
		tool.Click( (3, 3) ); // snaps to (2, 3)
		CollectionAssert.AreEqual( new[] { (2, 5), (2, 4), (2, 3) }, ride.QueueCells.ToArray() );
		Assert.AreEqual( 3 * 75, charged );
		Assert.AreEqual( (2, 3), tool.Start );
		// Onto the path: the cell before the path is laid, the path cell refuses and the tool ends.
		var last = tool.Click( (2, 0) )!;
		CollectionAssert.AreEqual( new[] { (2, 2), (2, 1) }, last.Built.ToArray() );
		Assert.IsFalse( tool.IsActive );
		Assert.AreEqual( (2, 0), ride.JoinCell, "the queue reaches the path" );
		Assert.AreEqual( 5, ride.QueueSizeInCells );

		tool.Enter( writer, ride.QueueBackCell );
		tool.Click( (4, 1) );
		Assert.AreEqual( 7, ride.QueueSizeInCells );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 5, ride.QueueSizeInCells, "undo removes the segment's queue cells" );
	}

	[TestMethod]
	public void PathRegisterHasTheTenPlanUnknowns()
	{
		var names = PathApproximations.Entries.Select( entry => entry.Rule.Split( ':' )[0] ).ToArray();
		CollectionAssert.AreEqual( new[] { "PATH-ENTER", "PATH-UNDO", "PATH-ENDFLAG", "PATH-CONNECT", "PATH-REMOVE", "PATH-SLOPE", "PATH-LAND", "PATH-CODE8", "PATH-FREE", "PATH-LEDGER" }, names );
		CollectionAssert.AreEqual( Enumerable.Range( 1, 10 ).Select( index => $"PATH-{index:000}" ).ToArray(), PathApproximations.Entries.Select( entry => entry.Id ).ToArray() );
	}
}
