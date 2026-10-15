using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The original walk's cell rules and route search (<see cref="WalkEdges"/>, <see cref="WalkSearch"/>). The expected values are tp.exe's
/// own: its edge test 0x4D8750, commit 0x510510 and search 0x511420 run in the dev-only x86 oracle (docs/reverse/ORACLE.md section 7) on
/// the same cells; thousands of random maps agreed the same way while porting.
/// </summary>
[TestClass]
public class WalkSearchTests
{
	private static readonly (int X, int Y) Origin = (20, 30);

	/// <summary>Map rows: '.' empty, '#' path (1), 'q' queue (3), 'W' type 7, '4' type 4, '~' 0x1E, 'e' 9, 't' 10, 'x' 2.</summary>
	private static readonly Dictionary<string, string[]> Maps = new()
	{
		["spiral"] = ["############", "#..........#", "#.########.#", "#.#......#.#", "#.#.####.#.#", "#.#.#..#.#.#", "#.#.#.##.#.#", "#.#.#....#.#", "#.#.######.#", "#.#........#", "#.##########", "#..........."],
		["comb"] = ["#############", "#.#.#.#.#.#.#", "#.#.#.#.#.#.#", "#.#.#.#.#.#.#", "#############", "......#......", "......#######"],
		["plaza"] = ["##########", "##########", "###WW#####", "###WW#####", "##########", "#####4####", "##########"],
		["island"] = ["#######.....", "#.....#.....", "#.###.#.....", "#.#.#.#.....", "#.###.#.....", "#.....#.....", "#######....#"],
	};

	private static ParkCellMap Build( string name )
	{
		var map = new ParkCellMap( 128, 128 );
		var rows = Maps[name];
		for ( var y = 0; y < rows.Length; y++ )
			for ( var x = 0; x < rows[y].Length; x++ )
			{
				byte type = rows[y][x] switch { '#' => 1, 'q' => 3, 'W' => 7, '4' => 4, '~' => 0x1E, 'e' => 9, 't' => 10, 'x' => 2, _ => 0 };
				if ( type != 0 )
					map.SetType( Origin.X + x, Origin.Y + y, type );
			}
		return map;
	}

	private static (int X, int Y) At( (int X, int Y) cell ) => (Origin.X + cell.X, Origin.Y + cell.Y);

	[TestMethod]
	public void TheSearchFindsTheOriginalsWaypoints()
	{
		var cases = new (string Map, (int X, int Y) Start, (int X, int Y) Goal, int Mask, (int X, int Y)[]? Path)[]
		{
			("spiral", (0, 11), (5, 4), 0, [(0, 0), (11, 0), (11, 10), (2, 10), (2, 2), (9, 2), (9, 8), (4, 8), (4, 4), (5, 4)]),
			("spiral", (0, 0), (11, 9), 0, [(11, 0), (11, 9)]),
			("spiral", (11, 0), (6, 6), 0, [(11, 10), (2, 10), (2, 2), (9, 2), (9, 8), (4, 8), (4, 4), (7, 4), (7, 6), (6, 6)]),
			("comb", (2, 3), (10, 3), 0, [(2, 4), (10, 4), (10, 3)]),
			("comb", (0, 1), (12, 6), 0, [(0, 4), (6, 4), (6, 6), (12, 6)]),
			("comb", (6, 5), (0, 3), 1, [(0, 3)]),
			("plaza", (0, 0), (9, 6), 0, [(4, 6), (9, 6)]),
			("plaza", (4, 1), (4, 4), 0, [(5, 1), (5, 4), (4, 4)]),
			("plaza", (2, 6), (7, 0), 0, [(5, 4), (7, 0)]),
			("island", (0, 0), (3, 3), 0, null),
			("island", (0, 0), (11, 6), 0, null),
			("island", (0, 0), (11, 6), 1, [(11, 6)]),
			("island", (2, 2), (4, 4), 0, [(2, 4), (4, 4)]),
			("island", (6, 6), (3, 3), 1, [(3, 3)]),
		};
		var path = new List<(int X, int Y)>();
		foreach ( var (name, start, goal, mask, expected) in cases )
		{
			var found = WalkSearch.Find( Build( name ), At( start ), At( goal ), mask, path );
			var what = $"{name} {start} -> {goal}, mask {mask}";
			Assert.AreEqual( expected != null, found, what );
			if ( expected != null )
				CollectionAssert.AreEqual( expected.Select( At ).ToList(), path, $"{what}: {string.Join( " ", path )}" );
		}
	}

	private static ParkCellMap EdgeMap()
	{
		var map = new ParkCellMap( 128, 128 );
		foreach ( var (x, y, type) in new[] { (10, 10, 1), (11, 10, 1), (12, 10, 3), (13, 10, 3), (14, 10, 1), (12, 11, 4), (13, 11, 4), (10, 11, 30), (11, 11, 30), (10, 12, 9), (11, 12, 10), (14, 11, 7), (14, 12, 2), (12, 12, 5), (0, 0, 1), (127, 127, 1), (10, 9, 1) } )
			map.SetType( x, y, (byte)type );
		foreach ( var (x, y, links) in new[] { (12, 10, 64), (13, 10, 68), (14, 10, 64), (11, 10, 4) } )
			map.SetLinks( x, y, (byte)links );
		foreach ( var (x, y, link) in new[] { (10, 12, 16), (11, 12, 1) } )
			map.SetQueueLink( x, y, (byte)link );
		foreach ( var (x, y, flags) in new[] { (11, 11, 1024) } )
			map.SetFlags( x, y, (ParkCellFlags)flags );
		return map;
	}

	[TestMethod]
	public void TheEdgeTestFollowsTheOriginalForEveryCellTypeAndMask()
	{
		var map = EdgeMap();
		// (x, y, direction, mask, blocked) for the cells of the edge map in every direction and mask
		var expected = new (int X, int Y, int Direction, int Mask, bool Blocked)[]
		{
			(10, 10, 0, 0, false), (10, 10, 0, 1, false), (10, 10, 0, 2, false), (10, 10, 1, 0, false), (10, 10, 1, 1, false), (10, 10, 1, 2, false),
			(10, 10, 2, 0, false), (10, 10, 2, 1, false), (10, 10, 2, 2, true), (10, 10, 3, 0, true), (10, 10, 3, 1, false), (10, 10, 3, 2, false),
			(11, 10, 0, 0, true), (11, 10, 0, 1, false), (11, 10, 0, 2, false), (11, 10, 1, 0, false), (11, 10, 1, 1, false), (11, 10, 1, 2, false),
			(11, 10, 2, 0, false), (11, 10, 2, 1, false), (11, 10, 2, 2, false), (11, 10, 3, 0, false), (11, 10, 3, 1, false), (11, 10, 3, 2, false),
			(12, 10, 0, 0, true), (12, 10, 0, 1, true), (12, 10, 0, 2, true), (12, 10, 1, 0, false), (12, 10, 1, 1, false), (12, 10, 1, 2, false),
			(12, 10, 2, 0, true), (12, 10, 2, 1, true), (12, 10, 2, 2, true), (12, 10, 3, 0, false), (12, 10, 3, 1, false), (12, 10, 3, 2, false),
			(13, 10, 0, 0, true), (13, 10, 0, 1, true), (13, 10, 0, 2, true), (13, 10, 1, 0, false), (13, 10, 1, 1, false), (13, 10, 1, 2, false),
			(13, 10, 2, 0, true), (13, 10, 2, 1, true), (13, 10, 2, 2, true), (13, 10, 3, 0, true), (13, 10, 3, 1, true), (13, 10, 3, 2, true),
			(14, 10, 0, 0, true), (14, 10, 0, 1, false), (14, 10, 0, 2, false), (14, 10, 1, 0, true), (14, 10, 1, 1, false), (14, 10, 1, 2, false),
			(14, 10, 2, 0, true), (14, 10, 2, 1, true), (14, 10, 2, 2, true), (14, 10, 3, 0, false), (14, 10, 3, 1, false), (14, 10, 3, 2, false),
			(12, 11, 0, 0, true), (12, 11, 0, 1, true), (12, 11, 0, 2, true), (12, 11, 1, 0, false), (12, 11, 1, 1, false), (12, 11, 1, 2, false),
			(12, 11, 2, 0, true), (12, 11, 2, 1, true), (12, 11, 2, 2, true), (12, 11, 3, 0, false), (12, 11, 3, 1, false), (12, 11, 3, 2, false),
			(13, 11, 0, 0, true), (13, 11, 0, 1, true), (13, 11, 0, 2, true), (13, 11, 1, 0, true), (13, 11, 1, 1, true), (13, 11, 1, 2, true),
			(13, 11, 2, 0, false), (13, 11, 2, 1, false), (13, 11, 2, 2, false), (13, 11, 3, 0, false), (13, 11, 3, 1, false), (13, 11, 3, 2, false),
			(10, 11, 0, 0, false), (10, 11, 0, 1, false), (10, 11, 0, 2, false), (10, 11, 1, 0, false), (10, 11, 1, 1, false), (10, 11, 1, 2, false),
			(10, 11, 2, 0, true), (10, 11, 2, 1, true), (10, 11, 2, 2, true), (10, 11, 3, 0, false), (10, 11, 3, 1, false), (10, 11, 3, 2, false),
			(11, 11, 0, 0, false), (11, 11, 0, 1, false), (11, 11, 0, 2, false), (11, 11, 1, 0, true), (11, 11, 1, 1, true), (11, 11, 1, 2, true),
			(11, 11, 2, 0, false), (11, 11, 2, 1, false), (11, 11, 2, 2, false), (11, 11, 3, 0, false), (11, 11, 3, 1, false), (11, 11, 3, 2, false),
			(10, 12, 0, 0, false), (10, 12, 0, 1, false), (10, 12, 0, 2, true), (10, 12, 1, 0, true), (10, 12, 1, 1, true), (10, 12, 1, 2, true),
			(10, 12, 2, 0, true), (10, 12, 2, 1, true), (10, 12, 2, 2, true), (10, 12, 3, 0, true), (10, 12, 3, 1, true), (10, 12, 3, 2, true),
			(11, 12, 0, 0, false), (11, 12, 0, 1, false), (11, 12, 0, 2, false), (11, 12, 1, 0, true), (11, 12, 1, 1, true), (11, 12, 1, 2, true),
			(11, 12, 2, 0, false), (11, 12, 2, 1, false), (11, 12, 2, 2, false), (11, 12, 3, 0, true), (11, 12, 3, 1, true), (11, 12, 3, 2, true),
			(10, 9, 0, 0, true), (10, 9, 0, 1, false), (10, 9, 0, 2, false), (10, 9, 1, 0, true), (10, 9, 1, 1, false), (10, 9, 1, 2, false),
			(10, 9, 2, 0, false), (10, 9, 2, 1, false), (10, 9, 2, 2, false), (10, 9, 3, 0, true), (10, 9, 3, 1, false), (10, 9, 3, 2, false),
			(0, 0, 0, 0, true), (0, 0, 0, 1, true), (0, 0, 0, 2, true), (0, 0, 1, 0, true), (0, 0, 1, 1, false), (0, 0, 1, 2, false),
			(0, 0, 2, 0, true), (0, 0, 2, 1, false), (0, 0, 2, 2, false), (0, 0, 3, 0, true), (0, 0, 3, 1, true), (0, 0, 3, 2, true),
			(127, 127, 0, 0, true), (127, 127, 0, 1, false), (127, 127, 0, 2, false), (127, 127, 1, 0, true), (127, 127, 1, 1, true), (127, 127, 1, 2, true),
			(127, 127, 2, 0, true), (127, 127, 2, 1, true), (127, 127, 2, 2, true), (127, 127, 3, 0, true), (127, 127, 3, 1, false), (127, 127, 3, 2, false),
			(14, 11, 0, 0, false), (14, 11, 0, 1, false), (14, 11, 0, 2, false), (14, 11, 1, 0, false), (14, 11, 1, 1, false), (14, 11, 1, 2, false),
			(14, 11, 2, 0, true), (14, 11, 2, 1, true), (14, 11, 2, 2, true), (14, 11, 3, 0, true), (14, 11, 3, 1, true), (14, 11, 3, 2, true),
			(11, 13, 0, 0, false), (11, 13, 0, 1, false), (11, 13, 0, 2, false), (11, 13, 1, 0, false), (11, 13, 1, 1, false), (11, 13, 1, 2, false),
			(11, 13, 2, 0, false), (11, 13, 2, 1, false), (11, 13, 2, 2, false), (11, 13, 3, 0, false), (11, 13, 3, 1, false), (11, 13, 3, 2, false),
			(12, 12, 0, 0, true), (12, 12, 0, 1, true), (12, 12, 0, 2, true), (12, 12, 1, 0, false), (12, 12, 1, 1, false), (12, 12, 1, 2, false),
			(12, 12, 2, 0, false), (12, 12, 2, 1, false), (12, 12, 2, 2, false), (12, 12, 3, 0, false), (12, 12, 3, 1, false), (12, 12, 3, 2, false),
		};
		foreach ( var (x, y, direction, mask, blocked) in expected )
			Assert.AreEqual( blocked, WalkEdges.Blocked( map, x, y, direction, mask ), $"({x}, {y}) direction {direction} mask {mask}" );
	}

	[TestMethod]
	public void TheCommitTakesADiagonalThroughEitherLShapedRoute()
	{
		var map = EdgeMap();
		var expected = new ((int X, int Y) From, (int X, int Y) To, int Mask, bool Accepted)[]
		{
			((10, 10), (11, 9), 0, false),
			((10, 10), (11, 9), 1, true),
			((10, 10), (11, 11), 0, true),
			((10, 10), (11, 11), 1, true),
			((11, 10), (12, 11), 0, false),
			((11, 10), (12, 11), 1, false),
			((10, 10), (9, 9), 0, false),
			((10, 10), (9, 9), 1, true),
			((10, 10), (12, 10), 0, false),
			((10, 10), (12, 10), 1, false),
			((14, 10), (13, 11), 0, false),
			((14, 10), (13, 11), 1, false),
			((10, 9), (11, 10), 0, true),
			((10, 9), (11, 10), 1, true),
			((11, 10), (10, 11), 0, true),
			((11, 10), (10, 11), 1, true),
		};
		foreach ( var (from, to, mask, accepted) in expected )
			Assert.AreEqual( accepted, WalkEdges.Commit( map, from, to, mask ), $"{from} -> {to} mask {mask}" );
		Assert.IsFalse( WalkEdges.Commit( map, (10, 10), null, 0 ), "a step off the map is refused" );
	}

	[TestMethod]
	public void TheBlockStampsSurviveTheLayoutSnapshotAndOldLayoutsLoadUnstamped()
	{
		var cells = new ParkCellMap( 40, 40 );
		cells.SetType( 3, 3, ParkCellType.Path );
		cells.SetType( 35, 20, ParkCellType.Path );
		cells.SetType( 35, 20, ParkCellType.Path );
		Assert.AreEqual( 2, cells.StampCounter, "an unchanged type stamps nothing" );
		ParkLayoutSnapshot.CellData Record( int x, int y ) => new() { X = x, Y = y, Type = 1, Flags = 0, Placements = 0, Links = 0, QueueLink = 0 };
		var layout = new ParkLayoutSnapshot.Data
		{
			Cells = new ParkLayoutSnapshot.CellMapData
			{
				Width = 40, Height = 40, Version = cells.Version, Cells = [Record( 3, 3 ), Record( 35, 20 )],
				BlockStamps = cells.CaptureBlockStamps(), StampCounter = cells.StampCounter
			},
			Objects = [], NextSeed = 0, NextAttractionId = 0, ObjectScriptKey = 0
		};
		var restored = ParkLayoutSnapshot.RestoreCellMap( layout );
		Assert.AreEqual( (2, 1, 2, 0), (restored.StampCounter, restored.BlockStamp( 0, 0 ), restored.BlockStamp( 2, 1 ), restored.BlockStamp( 1, 1 )) );
		var old = layout with { Cells = layout.Cells with { BlockStamps = null, StampCounter = 0 } };
		var oldMap = ParkLayoutSnapshot.RestoreCellMap( old );
		Assert.AreEqual( (0, 0, 0), (oldMap.StampCounter, oldMap.BlockStamp( 0, 0 ), oldMap.BlockStamp( 2, 1 )), "a save from before the stamps restores every block unstamped" );
		Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( layout with { Cells = layout.Cells with { BlockStamps = [1, 2] } } ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( layout with { Cells = layout.Cells with { StampCounter = 1 } } ), "a stamp above the counter" );
	}
}
