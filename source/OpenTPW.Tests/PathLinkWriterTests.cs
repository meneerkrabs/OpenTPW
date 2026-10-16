using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The link writer <c>0x5348D0</c> and the clear's unlinking loops (<c>0x5367A0</c>) over the cell map's link byte: cardinal
/// and diagonal bits, the OR into existing bytes, the queue-corner exclusion, the clean-up beside queue links, the type-3
/// conversion, the type-9/10 links gated by the queue link, mode 3's predecessor link and the commit's two passes.
/// </summary>
[TestClass]
public class PathLinkWriterTests
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

	private static ParkPathBuilder Builder( ParkCellMap cells ) => new( cells, null, (ParkEconomy?)null, new FlatGrid() );

	private static void Type( ParkCellMap cells, int x, int y, byte type ) => cells.SetType( x, y, type );

	[TestMethod]
	public void ASquareOfPathGetsEveryDiagonalBothWays()
	{
		var cells = new ParkCellMap( 16, 16 );
		var builder = Builder( cells );
		builder.BuildSegment( (2, 2), (3, 2) );
		builder.BuildSegment( (2, 3), (3, 3) );
		// (2,2): +X 4, +Y 16, +X+Y 8; (3,2): −X 64, +Y 16, −X+Y 0x20; (2,3): −Y 1, +X 4, +X−Y 2; (3,3): −Y 1, −X 64, −X−Y 0x80.
		Assert.AreEqual( 0x1C, cells.LinksAt( 2, 2 ) );
		Assert.AreEqual( 0x70, cells.LinksAt( 3, 2 ) );
		Assert.AreEqual( 0x07, cells.LinksAt( 2, 3 ) );
		Assert.AreEqual( 0xC1, cells.LinksAt( 3, 3 ) );
	}

	[TestMethod]
	public void AStraightLineHasNoDiagonals()
	{
		var cells = new ParkCellMap( 16, 16 );
		Builder( cells ).BuildSegment( (2, 5), (6, 5) );
		Assert.AreEqual( 0x04, cells.LinksAt( 2, 5 ) );
		Assert.AreEqual( 0x44, cells.LinksAt( 4, 5 ) );
		Assert.AreEqual( 0x40, cells.LinksAt( 6, 5 ) );
	}

	[TestMethod]
	public void LinksAreOredIntoTheExistingBytes()
	{
		var cells = new ParkCellMap( 16, 16 );
		Type( cells, 4, 4, 1 );
		// A bit toward a cell that is not a path (as an imported InitialPath cell carries) stays.
		cells.SetLinks( 4, 4, 0x40 );
		Type( cells, 5, 4, 1 );
		PathLinkWriter.LinkPath( cells, 4, 4 );
		Assert.AreEqual( 0x44, cells.LinksAt( 4, 4 ), "the +X link is ORed in, the old −X bit stays" );
		Assert.AreEqual( 0x40, cells.LinksAt( 5, 4 ) );
	}

	[TestMethod]
	public void ANeighboursDiagonalSkipsAQueueCorner()
	{
		var cells = new ParkCellMap( 16, 16 );
		Type( cells, 2, 2, 1 );
		Type( cells, 3, 2, 3 );
		Type( cells, 3, 3, 1 );
		Type( cells, 2, 3, 1 );
		PathLinkWriter.LinkPath( cells, 2, 3 );
		// The −Y neighbour (2,2) would get +X+Y toward (3,3), but its +X cell (3,2) is a queue: 0x534987..0x5349CA.
		Assert.AreEqual( 0x10, cells.LinksAt( 2, 2 ) );
		Assert.AreEqual( 0x05, cells.LinksAt( 2, 3 ), "no +X−Y corner without a path at (3,2)" );
		Assert.AreEqual( 0x40, cells.LinksAt( 3, 3 ) );
	}

	[TestMethod]
	public void ALinkToAQueueClearsTheDiagonalsBesideIt()
	{
		var cells = new ParkCellMap( 16, 16 );
		Type( cells, 5, 4, 3 );
		Type( cells, 5, 5, 1 );
		cells.SetLinks( 5, 5, 0x01 | 0x02 | 0x80 | 0x08 );
		PathLinkWriter.LinkPath( cells, 5, 5 );
		Assert.AreEqual( 0x09, cells.LinksAt( 5, 5 ), "the −Y queue link clears +X−Y and −X−Y only (0x5351D8)" );
	}

	[TestMethod]
	public void AQueueCellBecomesAPathCellAndKeepsItsLinks()
	{
		var cells = new ParkCellMap( 16, 16 );
		Type( cells, 7, 7, 3 );
		cells.SetLinks( 7, 7, 0x01 );
		Type( cells, 7, 6, 3 );
		cells.SetLinks( 7, 6, 0x10 );
		var stamp = cells.StampCounter;
		var refreshed = new List<(int, int)>();
		var sounds = new List<(int, int)>();
		PathLinkWriter.LinkPath( cells, 7, 7, new LinkEffects( ( x, y ) => refreshed.Add( (x, y) ), ( x, y ) => sounds.Add( (x, y) ) ) );
		Assert.AreEqual( ParkCellType.Path, cells.TypeAt( 7, 7 ) );
		Assert.AreEqual( stamp + 1, cells.StampCounter, "the conversion stamps its block (0x534921)" );
		Assert.AreEqual( 0x01, cells.LinksAt( 7, 7 ), "the link to the queue stays" );
		CollectionAssert.AreEqual( new[] { (7, 6) }, refreshed, "the queue neighbour linking back is refreshed" );
		CollectionAssert.AreEqual( new[] { (7, 7) }, sounds, "and reported for cat_ui 139" );
	}

	[TestMethod]
	public void AQueueNeighbourIsReportedOnlyWhenItLinksBackAndTheFlagIsSet()
	{
		var cells = new ParkCellMap( 16, 16 );
		Type( cells, 3, 3, 3 );
		Type( cells, 4, 3, 1 );
		var sounds = 0;
		PathLinkWriter.LinkPath( cells, 4, 3, new LinkEffects( QueueNeighbour: ( _, _ ) => sounds++ ) );
		Assert.AreEqual( 0, sounds, "a queue cell without the +X bit does not count" );
		cells.SetLinks( 3, 3, 0x04 );
		PathLinkWriter.LinkPath( cells, 4, 3 );
		Assert.AreEqual( 0, sounds, "without the refresh flag nothing is reported" );
		PathLinkWriter.LinkPath( cells, 4, 3, new LinkEffects( QueueNeighbour: ( _, _ ) => sounds++ ) );
		Assert.AreEqual( 1, sounds );
		Assert.AreEqual( 0x00, cells.LinksAt( 4, 3 ), "mode 1 never links to a queue cell" );
	}

	[TestMethod]
	public void RideCellsLinkByTheirQueueLink()
	{
		var cells = new ParkCellMap( 16, 16 );
		Type( cells, 6, 6, 1 );
		// −Y: a type-10 cell links when its queue link points here (16); +X: a type-9 cell when its queue link points away (4).
		Type( cells, 6, 5, 10 );
		cells.SetQueueLink( 6, 5, 0x10 );
		Type( cells, 7, 6, 9 );
		cells.SetQueueLink( 7, 6, 0x04 );
		// +Y: a type-10 cell whose link points elsewhere does not.
		Type( cells, 6, 7, 10 );
		cells.SetQueueLink( 6, 7, 0x10 );
		PathLinkWriter.LinkPath( cells, 6, 6 );
		Assert.AreEqual( 0x05, cells.LinksAt( 6, 6 ) );
		Assert.AreEqual( 0x10, cells.LinksAt( 6, 5 ) );
		Assert.AreEqual( 0x40, cells.LinksAt( 7, 6 ) );
		Assert.AreEqual( 0x00, cells.LinksAt( 6, 7 ) );
	}

	[TestMethod]
	public void ClearingAPathCellUnlinksEveryBitBothWays()
	{
		var cells = new ParkCellMap( 16, 16 );
		var builder = Builder( cells );
		builder.BuildSegment( (2, 2), (3, 2) );
		builder.BuildSegment( (2, 3), (3, 3) );
		Assert.AreEqual( CellBuildResult.Ok, builder.Remove( 2, 2 ) );
		Assert.AreEqual( 0x70 & ~0x40, cells.LinksAt( 3, 2 ) );
		Assert.AreEqual( 0x07 & ~0x01, cells.LinksAt( 2, 3 ) );
		Assert.AreEqual( 0xC1 & ~0x80, cells.LinksAt( 3, 3 ), "the diagonal back bit goes too" );
	}

	[TestMethod]
	public void TheClearUnlinksOnlyAlongTheCellsOwnBits()
	{
		var cells = new ParkCellMap( 16, 16 );
		Type( cells, 5, 5, 1 );
		Type( cells, 6, 5, 1 );
		// (6,5) links back to (5,5), but (5,5) has no bit toward it: 0x5367A0 walks the cleared cell's own bits.
		cells.SetLinks( 6, 5, 0x40 );
		PathLinkWriter.Unlink( cells, 5, 5, cardinalOnly: false );
		Assert.AreEqual( 0x40, cells.LinksAt( 6, 5 ) );
	}

	[TestMethod]
	public void TheQueueStepLinksThePredecessorWhileItHasRoom()
	{
		var cells = new ParkCellMap( 16, 16 );
		Type( cells, 4, 4, 3 );
		cells.SetLinks( 4, 4, 0x40 );
		Type( cells, 5, 4, 3 );
		PathLinkWriter.LinkQueueStep( cells, 5, 4, 1, 0, 3, ( _, _ ) => true );
		Assert.AreEqual( 0x40, cells.LinksAt( 5, 4 ) );
		Assert.AreEqual( 0x44, cells.LinksAt( 4, 4 ) );
		// A predecessor with two bits already, or another owner's, is not linked.
		Type( cells, 6, 4, 3 );
		cells.SetLinks( 5, 4, 0x41 );
		PathLinkWriter.LinkQueueStep( cells, 6, 4, 1, 0, 3, ( _, _ ) => true );
		Assert.AreEqual( 0x00, cells.LinksAt( 6, 4 ) );
		cells.SetLinks( 5, 4, 0x40 );
		PathLinkWriter.LinkQueueStep( cells, 6, 4, 1, 0, 3, ( _, _ ) => false );
		Assert.AreEqual( 0x00, cells.LinksAt( 6, 4 ) );
	}

	[TestMethod]
	public void AQueueLineEndingOnAPathLinksItAndClearsThePathsDiagonals()
	{
		var cells = new ParkCellMap( 16, 16 );
		Type( cells, 8, 8, 3 );
		cells.SetLinks( 8, 8, 0x01 );
		Type( cells, 8, 9, 1 );
		cells.SetLinks( 8, 9, 0x04 | 0x02 | 0x80 );
		// The line steps +Y from the queue cell (8, 8) onto the path cell (8, 9): dy is 1, the predecessor is −Y.
		PathLinkWriter.LinkQueueStep( cells, 8, 9, 0, 1, 3, ( _, _ ) => true );
		Assert.AreEqual( 0x05, cells.LinksAt( 8, 9 ), "−Y linked, +X−Y and −X−Y cleared" );
		Assert.AreEqual( 0x11, cells.LinksAt( 8, 8 ) );
	}

	[TestMethod]
	public void TheCommitRefreshesEveryLineCellAfterTheLinkPass()
	{
		var cells = new ParkCellMap( 16, 16 );
		var refreshed = new List<(int, int)>();
		var builder = Builder( cells );
		builder.Effects = new LinkEffects( ( x, y ) => refreshed.Add( (x, y) ) );
		builder.BuildSegment( (2, 2), (4, 2) );
		// The link pass refreshes each path neighbour it links (−Y, +X, +Y, −X), then pass 0x81 every cell of the line.
		CollectionAssert.AreEqual( new[] { (3, 2), (4, 2), (2, 2), (3, 2), (2, 2), (3, 2), (4, 2) }, refreshed );
	}
}
