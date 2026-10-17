using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The original walk's cell rules and route search (<see cref="WalkEdges"/>, <see cref="WalkSearch"/>) and the steering of a walk of the
/// original kind (<see cref="GuestNavigation.Navigate"/>). The expected values are tp.exe's own: its edge test 0x4D8750, commit 0x510510,
/// search 0x511420, avoid_walls 0x50E4A0 and steering step 0x50F3B0 run in the dev-only x86 oracle (docs/reverse/ORACLE.md section 7.19,
/// finding X25) on the same cells; thousands of random maps, walks and edits agreed the same way while porting.
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
	public void AvoidWallsPushesFromTheEdgeOfThePositionsCellAndFromCutCorners()
	{
		var edges = new (string Map, int X, int Y, int VelocityX, int VelocityY, (int X, int Y) Force)[]
		{
			("plaza", 1501792, 2157152, 3000, 2000, (-21142, 0)),
			("plaza", 1503792, 2127152, 4000, 0, (-27142, 0)),
			("plaza", 1643400, 2091616, -3000, 3000, (0, 0)),
			("plaza", 1501792, 2231224, 3000, -3000, (0, 0)),
			("plaza", 1641400, 2231224, -2000, -2500, (0, 0)),
			("comb", 1406256, 2231224, 0, -4000, (0, 28214)),
			("comb", 1444792, 2231224, -3000, -3000, (0, 0)),
			("comb", 1503792, 2231224, 3000, -3000, (0, 0)),
			("plaza", 1671168, 2233224, 0, -6000, (0, 0)),
			("plaza", 1605632, 2288224, 2000, 6000, (0, 0)),
			("plaza", 1763936, 2288224, 5000, 5000, (0, 0)),
		};
		foreach ( var (name, x, y, vx, vy, force) in edges )
			Assert.AreEqual( force, GuestNavigation.AvoidWallsOriginal( Build( name ), x, y, vx, vy, 0, GuestNavigation.Radius ), $"{name} ({x}, {y}) v ({vx}, {vy})" );
		// A queue cell on the diagonal cuts the corner its two path neighbours share: the push comes from the ray test 0x50CC20.
		var q1 = new ParkCellMap( 128, 128 );
		foreach ( var (x, y, type) in new[] { (40, 40, 1), (41, 40, 1), (40, 41, 1), (41, 41, 3), (39, 40, 1), (40, 39, 1), (39, 39, 1), (41, 39, 1), (39, 41, 1) } )
			q1.SetType( x, y, (byte)type );
		var q4 = new ParkCellMap( 128, 128 );
		foreach ( var (x, y, type) in new[] { (40, 40, 1), (41, 40, 1), (40, 41, 1), (41, 41, 1), (39, 40, 1), (40, 39, 1), (39, 39, 3), (41, 39, 3), (39, 41, 3) } )
			q4.SetType( x, y, (byte)type );
		var corners = new (ParkCellMap Map, int X, int Y, int VelocityX, int VelocityY, (int X, int Y) Force)[]
		{
			(q1, 2684543, 2676933, 25193, 20239, (20200, -24215)),
			(q1, 2682776, 2677973, 16493, 2822, (7880, -40075)),
			(q1, 2674006, 2668392, 11905, 20560, (-7410, 6285)),
			(q1, 2671497, 2675516, 14560, 23789, (-29195, 21455)),
			(q1, 2668453, 2685033, 20498, 22729, (-45070, 43010)),
			(q1, 2680040, 2668828, 20367, 12729, (33565, -48090)),
			(q4, 2680059, 2638169, 17848, -11319, (30200, 42555)),
			(q4, 2686960, 2629960, 9015, -13454, (20320, 12150)),
			(q4, 2665893, 2626426, 25406, -105, (380, 24490)),
			(q4, 2644470, 2627050, -19483, -20512, (45925, -44830)),
			(q4, 2682004, 2627866, 16818, -19290, (2140, 1155)),
			(q4, 2646150, 2668984, -24516, 23601, (13115, 16350)),
		};
		foreach ( var (map, x, y, vx, vy, force) in corners )
			Assert.AreEqual( force, GuestNavigation.AvoidWallsOriginal( map, x, y, vx, vy, 0, GuestNavigation.Radius ), $"corner ({x}, {y}) v ({vx}, {vy})" );
	}

	[TestMethod]
	public void AWalkOfTheOriginalKindStepsAsTheOriginal()
	{
		var map = Build( "spiral" );
		var navigation = new GuestNavigation { X = (Origin.X << 16) + 0x8000, Y = ((Origin.Y + 11) << 16) + 0x8000 };
		Assert.IsTrue( navigation.Navigate( map, ((Origin.X + 5) << 16) + 0x4000, ((Origin.Y + 4) << 16) + 0x6000 ) );
		Assert.AreEqual( 10, navigation.PathCount, "waypoints" );
		Assert.AreEqual( 4718592, navigation.Total, "route length +0xA0" );
		Assert.AreEqual( 2490368, navigation.Ahead, "buffered length +0xA4" );
		Assert.AreEqual( 1507328, navigation.Beyond, "length beyond the buffer +0xA8" );
		// (step, x, y, velocity x, velocity y, waypoint index, progress, history)
		var checkpoints = new (int Step, int X, int Y, int VelocityX, int VelocityY, int Index, int Progress, int History)[]
		{
			(0, 1343488, 2712011, 0, -7733, 0, 108, 0),
			(1, 1343488, 2704224, 0, -7787, 0, 216, 0),
			(2, 1343488, 2696409, 0, -7815, 0, 325, 0),
			(5, 1343488, 2672913, 0, -7836, 0, 651, 0),
			(20, 1343488, 2555296, 0, -7843, 0, 2284, 0),
			(50, 1343488, 2320019, 0, -7844, 0, 5552, 0),
			(100, 1434163, 2008989, 7837, -131, 1, 11202, 0),
			(150, 1826150, 2002629, 7843, -126, 1, 16690, 0),
			(200, 2059305, 2168300, 81, 7840, 2, 22344, 0),
			(300, 1745814, 2648991, -7844, 150, 3, 33516, 0),
			(400, 1503091, 2143116, 7755, -1031, 0, 1474, 0),
			(500, 1932766, 2503354, 212, 7766, 1, 36407, 0),
			(576, 1641567, 2258657, 6969, -3424, 4, 64209, 0),
			(577, 1648355, 2255480, 6788, -3177, 4, 65536, 0),
			(578, 1651749, 2253891, 3394, -1589, 4, 65536, 1),
			(899, 1655138, 2251987, 0, -1, 4, 65536, 32767),
		};
		var next = 0;
		for ( var step = 0; step < 900; step++ )
		{
			var result = navigation.Step( 7733, 15466, map, ( _, _ ) => true );
			Assert.AreEqual( step >= 577 ? WalkResult.Arrived : WalkResult.Walking, result, $"step {step}" );
			if ( next < checkpoints.Length && checkpoints[next].Step == step )
			{
				var c = checkpoints[next++];
				Assert.AreEqual( (c.X, c.Y, c.VelocityX, c.VelocityY, c.Index, c.Progress, c.History),
					(navigation.X, navigation.Y, navigation.VelocityX, navigation.VelocityY, navigation.Index, navigation.Progress, navigation.History), $"step {step}" );
			}
		}
	}

	/// <summary>A path row at y = 0 from x = 0 to <paramref name="length"/> − 1 in a 128 × 128 map, with its block stamps cleared.</summary>
	private static ParkCellMap Row( int length )
	{
		var map = new ParkCellMap( 128, 128 );
		for ( var x = 0; x < length; x++ )
			map.SetType( x, 0, ParkCellType.Path );
		map.RestoreBlockStamps( 0, null );
		return map;
	}

	private static GuestNavigation WalkOn( ParkCellMap map, int fromX, int toX )
	{
		var navigation = new GuestNavigation { X = (fromX << 16) + 0x8000, Y = 0x8000 };
		Assert.IsTrue( navigation.Navigate( map, (toX << 16) + 0x8000, 0x8000 ) );
		return navigation;
	}

	[TestMethod]
	public void AFailedSearchLeavesTheWalkArrivedAndStuck()
	{
		var map = Row( 6 );
		map.SetType( 20, 0, ParkCellType.Path );
		var navigation = new GuestNavigation { X = 0x18000, Y = 0x8000, VelocityX = 3000 };
		Assert.IsFalse( navigation.Navigate( map, (20 << 16) + 0x8000, 0x8000 ), "the path cell at x = 20 is cut off by empty cells" );
		Assert.IsTrue( navigation.Original && navigation.Stuck && navigation.Arrived );
		Assert.AreEqual( (0, 0, 0, 0), (navigation.PathCount, navigation.Count, navigation.Total, navigation.History) );
		Assert.AreEqual( WalkResult.Stuck, navigation.Step( 7733, 15466, map, ( _, _ ) => true ), "the walk step reports 2" );
		Assert.AreEqual( 1500, navigation.VelocityX, "follow_path brakes (−velocity) once arrived, weighted 0.5" );
		Assert.IsTrue( navigation.Navigate( map, 0x48000, 0x8000 ), "a new destination clears the stuck flag" );
		Assert.IsFalse( navigation.Stuck );
	}

	[TestMethod]
	public void AnEditInTheWalkersBlockMakesItSearchAgain()
	{
		var map = Row( 12 );
		var navigation = WalkOn( map, 1, 10 );
		for ( var step = 0; step < 5; step++ )
			navigation.Step( 7733, 15466, map, ( _, _ ) => true );
		var stamp = navigation.Stamp;
		map.SetType( 40, 3, ParkCellType.Path );
		navigation.Step( 7733, 15466, map, ( _, _ ) => true );
		Assert.AreEqual( 0, navigation.Reroutes, "an edit in another 16 x 16 block changes nothing" );
		Assert.AreEqual( stamp, navigation.Stamp );
		map.SetType( 5, 1, ParkCellType.Path );
		navigation.Step( 7733, 15466, map, ( _, _ ) => true );
		Assert.AreEqual( 1, navigation.Reroutes, "the stamp of the walker's block is above the walk's: a search in mode 1" );
		Assert.AreEqual( map.StampCounter, navigation.Stamp );
		Assert.AreEqual( navigation.Cell, navigation.Buffer[0], "mode 1 puts the walker's cell first" );
		map.SetLinks( 7, 0, 0 );
		navigation.Step( 7733, 15466, map, ( _, _ ) => true );
		Assert.AreEqual( 1, navigation.Reroutes, "a link write stamps nothing" );
		map.SetType( 10, 0, ParkCellType.Empty );
		Assert.AreEqual( WalkResult.Stuck, navigation.Step( 7733, 15466, map, ( _, _ ) => true ), "the target cell is gone: the search fails" );
		Assert.IsTrue( navigation.Arrived && navigation.Stuck );
	}

	[TestMethod]
	public void ProgressIsTheShareOfTheWholeRouteWalked()
	{
		var map = Row( 40 );
		var navigation = WalkOn( map, 0, 30 );
		// One waypoint (the goal cell 30): +0xA0 is the octile length from the start position to its centre.
		Assert.AreEqual( (1, 30 << 16, 0, 0), (navigation.PathCount, navigation.Total, navigation.Ahead, navigation.Beyond) );
		Assert.AreEqual( 0, navigation.ProgressValue() );
		navigation.X += 15 << 16;
		Assert.AreEqual( Fixed16.One / 2, navigation.ProgressValue(), "half the route" );
		navigation.X -= 20 << 16;
		Assert.AreEqual( Fixed16.One - (35 << 16) * (long)Fixed16.One / (30 << 16), navigation.ProgressValue(), "behind the start the progress is negative" );
		navigation.Arrived = true;
		Assert.AreEqual( Fixed16.One, navigation.ProgressValue() );
	}

	[TestMethod]
	public void TheBlockStampsSurviveTheLayoutSnapshotAndOldLayoutsLoadUnstamped()
	{
		var cells = new ParkCellMap( 40, 40 );
		cells.SetType( 3, 3, ParkCellType.Path );
		cells.SetType( 35, 20, ParkCellType.Path );
		cells.SetType( 35, 20, ParkCellType.Path );
		Assert.AreEqual( 2, cells.StampCounter, "an unchanged type stamps nothing" );
		var cleared = new ParkCellMap( 40, 40 );
		cleared.SetType( 20, 20, ParkCellType.Empty );
		Assert.AreEqual( (1, 1), (cleared.StampCounter, cleared.BlockStamp( 1, 1 )), "clearing an empty cell stamps its block (0x5346EC skips the same-type return for 0)" );
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

	[TestMethod]
	public void GuestsWalkTheOriginalWalkAndOldSavesRestoreTheirRouteWalks()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		for ( var tick = 0; tick < 60 * 60 && !simulation.Guests.Any( guest => guest.State == GuestState.WalkingAround && guest.HasWaypoint ); tick++ )
			simulation.Tick( GuestSnapshotTests.Tick );
		var walker = simulation.Guests.First( guest => guest.State == GuestState.WalkingAround && guest.HasWaypoint );
		Assert.IsTrue( walker.Navigation.Original, "a wander walk is a walk of the original kind" );
		Assert.AreEqual( 0, walker.Navigation.Mask, "guests walk the park with mask 0" );
		var snapshot = simulation.CaptureSnapshot();
		var saved = snapshot.Guests.Single( guest => guest.Id == walker.Id ).Navigation;
		Assert.IsNotNull( saved.OriginalWalk );
		Assert.AreEqual( GuestNavigation.BufferedWaypoints, saved.Waypoints!.Length );
		// A save from before: no original walk fields; its walks restore as route walks along their saved cells.
		var root = JsonNode.Parse( ParkResumeJson.Serialize( snapshot ) )!.AsObject();
		foreach ( var guest in root["Guests"]!.AsArray() )
		{
			var navigation = guest![nameof( GuestSnapshot.Navigation )]!.AsObject();
			navigation.Remove( nameof( GuestNavigationSnapshot.OriginalWalk ) );
			navigation.Remove( nameof( GuestNavigationSnapshot.Waypoints ) );
			var route = navigation[nameof( GuestNavigationSnapshot.Route )]!.AsArray();
			navigation[nameof( GuestNavigationSnapshot.Count )] = Math.Min( GuestNavigation.BufferedWaypoints, route.Count );
			navigation[nameof( GuestNavigationSnapshot.Index )] = 0;
			navigation[nameof( GuestNavigationSnapshot.Cursor )] = 0;
		}
		var old = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( Encoding.UTF8.GetBytes( root.ToJsonString() ) );
		var resumed = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		Assert.IsTrue( resumed.ValidateSnapshot( old ).IsValid, string.Join( "; ", resumed.ValidateSnapshot( old ).Errors ) );
		resumed.RestoreSnapshot( old );
		var restored = resumed.Find( walker.Id )!;
		Assert.IsFalse( restored.Navigation.Original, "the old walk continues along its cells" );
		for ( var tick = 0; tick < 30 * 60; tick++ )
			resumed.Tick( GuestSnapshotTests.Tick );
		Assert.IsTrue( resumed.Guests.Any( guest => guest.Navigation.Original ), "the next walks are of the original kind" );
	}

	[TestMethod]
	public void AGuardsSetDownGivesTheWalkMaskOneTheRecallClearsItAndSavesKeepIt()
	{
		var settings = GuestTests.CreateSettings();
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 5 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 4, 5 );
		IGuardGuests guards = simulation;
		guards.Hold( guest.Id );
		Assert.IsTrue( guards.Release( guest.Id, (4, 5) ) );
		Assert.AreEqual( (GuestState.LeavingPark, 1), (guest.State, guest.Navigation.Mask), "tp.exe 0x4FEBAB: +0x188 = 1 before the walk out" );
		var snapshot = simulation.CaptureSnapshot();
		var resumed = new GuestSimulation( GuestTests.CreateCross(), settings, 5 ) { ArrivalsEnabled = false };
		resumed.RestoreSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) ) );
		Assert.AreEqual( 1, resumed.Find( guest.Id )!.Navigation.Mask, "the mask is saved with the walk" );
		// A guest with an exit counter above 0 and some happiness in an open park is recalled (0x500AB9): +0x188 = 0, state 6.
		guest.ExitLevel = 1000;
		guest.Happiness = 50;
		for ( var tick = 0; tick < 60 && guest.State == GuestState.LeavingPark; tick++ )
			simulation.Tick( GuestSnapshotTests.Tick );
		Assert.AreNotEqual( GuestState.LeavingPark, guest.State );
		Assert.AreEqual( 0, guest.Navigation.Mask );
	}

	[TestMethod]
	public void SavesWithAProgressAboveOneOrBufferedWaypointsOffTheRouteAreRejected()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		var snapshot = simulation.CaptureSnapshot();
		for ( var tick = 0; tick < 60 * 60 && !snapshot.Guests.Any( guest => guest.Navigation.OriginalWalk != null ); tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			if ( tick % 5 == 0 )
				snapshot = simulation.CaptureSnapshot();
		}
		snapshot = simulation.CaptureSnapshot();
		Assert.IsTrue( simulation.ValidateSnapshot( snapshot ).IsValid );
		GuestSimulationSnapshot With( Func<GuestNavigationSnapshot, GuestNavigationSnapshot?> change ) => snapshot with
		{
			Guests = snapshot.Guests.Select( guest => change( guest.Navigation ) is { } changed ? guest with { Navigation = changed } : guest ).ToArray()
		};
		var tooFar = simulation.ValidateSnapshot( With( navigation => navigation.OriginalWalk is { } walk
			? navigation with { OriginalWalk = walk with { Progress = Fixed16.One + 1 } } : null ) );
		Assert.IsTrue( tooFar.Errors.Any( error => error.Contains( "progress" ) && error.Contains( "above ONE" ) ), string.Join( "; ", tooFar.Errors ) );
		// A route walk (an older save's walk) must buffer its route from the cursor: one guest's walk is made an old route walk here.
		var oldWalker = snapshot.Guests.First( guest => guest.Navigation.OriginalWalk != null ).Id;
		GuestSimulationSnapshot AsRouteWalk( (int X, int Y)[] waypoints ) => snapshot with
		{
			Guests = snapshot.Guests.Select( guest => guest.Id == oldWalker ? guest with
			{
				Navigation = guest.Navigation with { OriginalWalk = null, Route = [(10, 5), (11, 5)], Cursor = 0, Count = 2, Index = 0, Waypoints = waypoints }
			} : guest ).ToArray()
		};
		var mismatch = simulation.ValidateSnapshot( AsRouteWalk( [(10, 5), (12, 5), (0, 0), (0, 0), (0, 0)] ) );
		Assert.IsTrue( mismatch.Errors.Any( error => error.Contains( "do not match its route" ) ), string.Join( "; ", mismatch.Errors ) );
		Assert.IsFalse( simulation.ValidateSnapshot( AsRouteWalk( [(10, 5), (11, 5), (0, 0), (0, 0), (0, 0)] ) ).Errors.Any( error => error.Contains( "do not match its route" ) ) );
	}
}
