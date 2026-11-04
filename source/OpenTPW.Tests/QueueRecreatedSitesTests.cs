using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The sites that report the cells whose tile tp.exe re-creates (0x5365D0) to <see cref="ParkCellMap.Recreated"/>, each recording what it reports: the queue animations restart exactly those cells
/// (<see cref="QueueBirths"/>).
/// </summary>
[TestClass]
public class QueueRecreatedSitesTests
{
	[TestMethod]
	public void TheQueueLineCommitReportsTheLineFromItsAnchorAndNotTheParallelLane()
	{
		var map = new ParkCellMap( 12, 12 );
		// the anchor (5, 5) and a parallel lane (6, 5), (6, 6) of the same ride
		foreach ( var (x, y) in new[] { (5, 5), (6, 5), (6, 6) } )
		{
			map.SetType( x, y, ParkCellType.Queue );
			map.SetOwner( x, y, 17 );
		}
		var reported = new List<(int X, int Y)>();
		var reducer = new QueueSegmentReducer( map, 17, ( x, y ) => false, () => true, _ => true, () => 10, _ => { }, effects: new LinkEffects( ( x, y ) => reported.Add( (x, y) ) ) );
		reducer.Commit( (5, 5), (5, 7) );
		CollectionAssert.AreEqual( new[] { (5, 5), (5, 6), (5, 7) }, reported, "anchor to end inclusive, even for the anchor whose model did not change" );
	}

	[TestMethod]
	public void ThePathLinkWriterReportsAQueueNeighbourThatLinksBack()
	{
		var map = new ParkCellMap( 12, 12 );
		map.SetType( 5, 5, ParkCellType.Path );
		map.SetType( 5, 6, ParkCellType.Queue );
		map.SetLinks( 5, 6, 1 );
		map.SetType( 8, 8, ParkCellType.Queue );
		var reported = new List<(int X, int Y)>();
		PathLinkWriter.LinkPath( map, 5, 5, new LinkEffects( ( x, y ) => reported.Add( (x, y) ) ) );
		CollectionAssert.AreEqual( new[] { (5, 6) }, reported, "the queue end with the bit toward the new path cell, and no other queue cell" );
	}

	[TestMethod]
	public void ALinkWriterBesideAQueueCellWithoutABackLinkReportsNothing()
	{
		var map = new ParkCellMap( 12, 12 );
		map.SetType( 5, 5, ParkCellType.Path );
		map.SetType( 5, 6, ParkCellType.Queue );
		var reported = new List<(int X, int Y)>();
		PathLinkWriter.LinkPath( map, 5, 5, new LinkEffects( ( x, y ) => reported.Add( (x, y) ) ) );
		Assert.AreEqual( 0, reported.Count );
	}

	[TestMethod]
	public void TheClearReportsEachLinkedNeighbourOnly()
	{
		var map = new ParkCellMap( 12, 12 );
		foreach ( var (x, y) in new[] { (5, 5), (5, 6), (5, 7), (6, 6) } )
			map.SetType( x, y, ParkCellType.Queue );
		map.SetLinks( 5, 5, 16 ); map.SetLinks( 5, 6, 1 | 16 ); map.SetLinks( 5, 7, 1 );
		var reported = new List<(int X, int Y)>();
		map.Recreated = ( x, y ) => reported.Add( (x, y) );
		PathLinkWriter.Unlink( map, 5, 6, cardinalOnly: true, map.ClearEffects );
		CollectionAssert.AreEquivalent( new[] { (5, 5), (5, 7) }, reported, "the parallel (6, 6) is not linked" );
	}

	[TestMethod]
	public void TheFenceReportsItsPostBlocksAndNothingFarFromTheFence()
	{
		var golden = ParkFenceGolden.Cases[0];
		var cells = ParkFenceGoldenTests.MapOf( golden );
		var reported = new HashSet<(int X, int Y)>();
		cells.Recreated = ( x, y ) => reported.Add( (x, y) );
		var land = ParkLand.NewPark( cells, new ParkLandSetup( golden.PostA, golden.PostB, golden.Clicks ) )!;
		var layer = land.LayerFor( cells.Width, cells.Height );
		Assert.IsTrue( reported.Count > 0 );
		var posts = 0;
		for ( var y = 0; y < cells.Height - 1; y++ )
			for ( var x = 0; x < cells.Width - 1; x++ )
				if ( layer.IsPost( x, y ) )
				{
					posts++;
					foreach ( var cell in new[] { (x, y), (x + 1, y), (x, y + 1), (x + 1, y + 1) } )
						Assert.IsTrue( reported.Contains( cell ), $"the block of the post at ({x}, {y}) is re-created (op 0x81, 0x5385F0..0x53874B): {cell}" );
				}
		Assert.IsTrue( posts > 0 );
		Assert.IsTrue( reported.Count < cells.Width * cells.Height, "not every cell of the map" );
	}
}
