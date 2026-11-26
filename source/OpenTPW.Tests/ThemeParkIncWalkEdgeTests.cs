using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's edge test <c>0x5D7B60</c> (<see cref="WalkEdgeRules.ThemeParkInc"/>, docs/reverse/TPI-NAVIGATOR.md): an obstacle's walkway cell is no walkway fast path, unowned land blocks
/// after the path and walkway rules, and type 8 blocks. The expected values are Game.exe's own: its edge test 0x5D7B60 and route search 0x640C50 ran on the same cells in the dev-only x86 oracle
/// (the targets <c>tpi-exe-walk-edge-test</c>, <c>tpi-exe-walk-commit</c> and <c>tpi-exe-walk-search</c>, docs/reverse/ORACLE.md). The Theme Park World rules on the same cells are tp.exe's (X25).
/// </summary>
[TestClass]
public class ThemeParkIncWalkEdgeTests
{
	private static readonly (int X, int Y) Origin = (20, 30);

	/// <summary>'.' empty, '#' path (links 0x55), 'q' queue (links 0x55), '~' walkway 0x1E, 'O' walkway with the obstacle flag, 'H' type 8, '4' type 4, 'W' type 7.</summary>
	private static readonly string[] Rows = ["#####~~.", "#q#O#H..", "#~#.#4#.", "########", ".OH~q#..", "..#..#W."];
	/// <summary>'u' marks an unowned cell (flag 0x40).</summary>
	private static readonly string[] Unowned = ["...u....", "..u..u..", ".u..u...", "......uu", "u..u.u..", ".uu....."];

	private static ParkCellMap Build( WalkEdgeRules rules )
	{
		var map = new ParkCellMap( 128, 128 ) { EdgeRules = rules };
		for ( var y = 0; y < Rows.Length; y++ )
		{
			for ( var x = 0; x < Rows[y].Length; x++ )
			{
				var (cx, cy) = (Origin.X + x, Origin.Y + y);
				byte type = Rows[y][x] switch { '#' => 1, 'q' => 3, '~' or 'O' => 0x1E, 'H' => 8, '4' => 4, 'W' => 7, _ => 0 };
				if ( type != 0 )
					map.SetType( cx, cy, type );
				if ( Rows[y][x] is '#' or 'q' )
					map.SetLinks( cx, cy, 0x55 );
				if ( Rows[y][x] == 'O' )
					map.SetObstacleFlag( cx, cy, true );
				if ( Unowned[y][x] == 'u' )
					map.SetFlags( cx, cy, ParkCellFlags.Unowned );
			}
		}
		return map;
	}

	/// <summary>Every edge of the cells from (−1, −1) to (8, 6) around the map, rows from the top, each row from the left, the four directions in turn: '1' blocked.</summary>
	private static string Edges( ParkCellMap map, int mask )
	{
		var result = new System.Text.StringBuilder();
		for ( var y = -1; y < 7; y++ )
			for ( var x = -1; x < 9; x++ )
				for ( var direction = 0; direction < 4; direction++ )
					result.Append( WalkEdges.Blocked( map, Origin.X + x, Origin.Y + y, direction, mask ) ? '1' : '0' );
		return result.ToString();
	}

	// Game.exe 0x5D7B60 on these cells (masks 0 and 1, the only ones a Theme Park Inc walk passes)
	private const string Mask0 = "00000000000000000000000000000000000000000000100110001000101010000010000000000000000000010100010000000101001000010000000000000001100001000000010110001101000000000000001100100010100000001000001011100000010000000111000001010110010000100000000000001100000011110000100001110000000100000000000010000000000000000000100000000000";
	private const string Mask1 = "00000000000000000000000000000000000000000000000000000000000000000010000000000000000000000100000000000100001000010000000000000000100000000000010010000001000000000000001000000010000000001000000000000000010000000111000001010110000000100000000000001100000010010000100001000000000100000000000010000000000000000000100000000000";
	// tp.exe's rules on the same cells: no obstacle, unowned or hole rule. These two come from walk_reading.py's statement of tp.exe's edge test (X25 verified it against tp.exe on random
	// maps), not from a run of tp.exe on this map
	private const string WorldMask0 = "00000000000000000000000000000000000000000000100110001000100010000000000000000000000000010000000000000100001000000000000000000001100001000000010100001101000000000000001100000010100000001000001011100000000000000000000001000010010000100000000000000000000011110000100001110000000100000000000000000000000000000000100000000000";
	private const string WorldMask1 = "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000000000000000000100000000000010000000001000000000000000000000000000000001000000000000000000000000000000001000010000000100000000000000000000000000000100001000000000100000000000000000000000000000000100000000000";

	[TestMethod]
	public void TheEdgeTestFollowsTheOriginalOnObstacleUnownedAndHoleCells()
	{
		var map = Build( WalkEdgeRules.ThemeParkInc );
		Assert.AreEqual( Mask0, Edges( map, 0 ), "mask 0" );
		Assert.AreEqual( Mask1, Edges( map, 1 ), "mask 1" );
	}

	[TestMethod]
	public void TheWorldRulesOnTheSameCellsAreUnchanged()
	{
		var map = Build( WalkEdgeRules.ThemeParkWorld );
		Assert.AreEqual( WorldMask0, Edges( map, 0 ), "mask 0" );
		Assert.AreEqual( WorldMask1, Edges( map, 1 ), "mask 1" );
		Assert.AreEqual( WalkEdgeRules.ThemeParkWorld, new ParkCellMap( 4, 4 ).EdgeRules, "a map walks with Theme Park World's rules unless its level sets Theme Park Inc's" );
	}

	[TestMethod]
	public void EachRuleInItsPlace()
	{
		var tpi = Build( WalkEdgeRules.ThemeParkInc );
		var world = Build( WalkEdgeRules.ThemeParkWorld );
		bool Both( int x, int y, int direction, int mask, bool tpiBlocked, bool worldBlocked ) =>
			WalkEdges.Blocked( tpi, Origin.X + x, Origin.Y + y, direction, mask ) == tpiBlocked && WalkEdges.Blocked( world, Origin.X + x, Origin.Y + y, direction, mask ) == worldBlocked;
		// [BIN:TPI-EXE:0x5D7C0E path target] an unowned path entered from a path is open (the fast path comes before the unowned rule): (2, 0) → (3, 0)
		Assert.IsTrue( Both( 2, 0, 1, 0, false, false ), "unowned path from a path" );
		// an unowned path entered from a queue is refused, where the world's rule reads the back link: (1, 1) → (2, 1)
		Assert.IsTrue( Both( 1, 1, 1, 0, true, false ), "unowned path from a queue" );
		// [BIN:TPI-EXE:0x5D7C55 walkway target] a walkway without the obstacle flag stays open from a path, even unowned: (0, 2) → (1, 2)
		Assert.IsTrue( Both( 0, 2, 1, 0, false, false ), "unowned walkway" );
		// the obstacle's walkway: from a path with mask 0 refused (R15), with mask 1 open (R14); from empty land open (R13)
		Assert.IsTrue( Both( 2, 1, 1, 0, true, false ), "obstacle walkway from a path, mask 0" );
		Assert.IsTrue( Both( 2, 1, 1, 1, false, false ), "obstacle walkway from a path, mask 1" );
		Assert.IsTrue( Both( 3, 2, 0, 0, false, false ), "obstacle walkway from empty land" );
		// [BIN:TPI-EXE:0x5D7D0D unowned target] empty unowned land (0, 4) from a path with mask 1, and from empty land, is refused
		Assert.IsTrue( Both( 0, 3, 2, 1, true, false ), "unowned land from a path, mask 1" );
		Assert.IsTrue( Both( 0, 5, 0, 0, true, false ), "unowned land from empty land" );
		// [BIN:TPI-EXE:0x67F780 blocking types] the owned hole (2, 4) from the walkway beside it
		Assert.IsTrue( Both( 3, 4, 3, 0, true, false ), "a hole" );
	}

	[TestMethod]
	public void TheRouteSearchGoesAroundWhatTheRulesClose()
	{
		// Game.exe 0x640C50 on the same cells: (mask, start, goal, waypoints or null for the failure 0x70000000)
		var cases = new (int Mask, (int X, int Y) Start, (int X, int Y) Goal, (int X, int Y)[]? Inc, (int X, int Y)[] World)[]
		{
			(0, (0, 3), (7, 0), [(2, 2), (2, 0), (7, 0)], [(4, 0), (7, 0)]),
			(1, (0, 3), (7, 5), [(3, 3), (3, 5), (7, 6), (7, 5)], [(3, 5), (7, 6), (7, 5)]),
			(0, (0, 0), (2, 5), [(1, 2), (3, 3), (3, 5), (2, 5)], [(2, 4), (2, 5)]),
			(1, (7, 3), (0, 5), [(3, 4), (2, 6), (0, 6), (0, 5)], [(0, 5)]),
			(0, (3, 0), (3, 5), [(2, 0), (2, 3), (3, 3), (3, 5)], [(3, 5)]),
			(1, (5, 5), (0, 4), null, [(0, 4)]),
		};
		var path = new List<(int X, int Y)>();
		(int X, int Y) At( (int X, int Y) cell ) => (Origin.X + cell.X, Origin.Y + cell.Y);
		foreach ( var (mask, start, goal, inc, world) in cases )
		{
			var what = $"{start} -> {goal}, mask {mask}";
			Assert.AreEqual( inc != null, WalkSearch.Find( Build( WalkEdgeRules.ThemeParkInc ), At( start ), At( goal ), mask, path ), what );
			if ( inc != null )
				CollectionAssert.AreEqual( inc.Select( At ).ToList(), path, $"{what}: {string.Join( " ", path )}" );
			Assert.IsTrue( WalkSearch.Find( Build( WalkEdgeRules.ThemeParkWorld ), At( start ), At( goal ), mask, path ), what );
			CollectionAssert.AreEqual( world.Select( At ).ToList(), path, $"{what}, the world's rules: {string.Join( " ", path )}" );
		}
	}

	[TestMethod]
	public void TheCommitRefusesADiagonalWhoseRoutesTheRulesClose()
	{
		var tpi = Build( WalkEdgeRules.ThemeParkInc );
		var world = Build( WalkEdgeRules.ThemeParkWorld );
		(int X, int Y) At( int x, int y ) => (Origin.X + x, Origin.Y + y);
		// from the hole's neighbour (6, 0) down-left to (5, 1), the hole: both L routes end on the hole
		Assert.IsFalse( WalkEdges.Commit( tpi, At( 6, 0 ), At( 5, 1 ), 0 ) );
		Assert.IsTrue( WalkEdges.Commit( world, At( 6, 0 ), At( 5, 1 ), 0 ) );
		// along the path row y = 3 into the unowned (6, 3) and (7, 3): open, the path fast path comes first
		Assert.IsTrue( WalkEdges.Commit( tpi, At( 5, 3 ), At( 6, 3 ), 0 ) );
		// from the path (6, 2) down-right to the unowned path (7, 3) through the empty (7, 2): open with mask 1
		Assert.IsTrue( WalkEdges.Commit( tpi, At( 6, 2 ), At( 7, 3 ), 1 ) );
	}

	[TestMethod]
	public void AWalkUnderTheIncRulesStaysOffUnownedLand()
	{
		// a path row y = 0 to x = 6 and empty land below; with mask 1 a walk to (6, 3) may cross the land, except the unowned column x = 3
		ParkCellMap Field( WalkEdgeRules rules )
		{
			var map = new ParkCellMap( 128, 128 ) { EdgeRules = rules };
			for ( var x = 0; x < 7; x++ )
				map.SetType( x, 0, ParkCellType.Path );
			for ( var y = 1; y < 6; y++ )
				map.SetFlags( 3, y, ParkCellFlags.Unowned );
			map.RestoreBlockStamps( 0, null );
			return map;
		}
		var visited = new Dictionary<WalkEdgeRules, HashSet<(int X, int Y)>>();
		foreach ( var rules in new[] { WalkEdgeRules.ThemeParkInc, WalkEdgeRules.ThemeParkWorld } )
		{
			var map = Field( rules );
			var navigation = new GuestNavigation { X = 0x8000, Y = 0x8000, Mask = 1 };
			Assert.IsTrue( navigation.Navigate( map, (6 << 16) + 0x8000, (3 << 16) + 0x8000 ), $"{rules}" );
			var cells = visited[rules] = new HashSet<(int X, int Y)>();
			for ( var step = 0; step < 2000 && navigation.Step( 7733, 15466, map, ( _, _ ) => true ) != WalkResult.Arrived; step++ )
				cells.Add( navigation.Cell );
			Assert.AreEqual( (6, 3), navigation.Cell, $"{rules}: arrived" );
		}
		Assert.IsFalse( visited[WalkEdgeRules.ThemeParkInc].Any( cell => cell.X == 3 && cell.Y > 0 ), "Theme Park Inc's walker crosses the unowned column only on the path" );
		Assert.IsTrue( visited[WalkEdgeRules.ThemeParkWorld].Any( cell => cell.X == 3 && cell.Y > 0 ), "Theme Park World's walker cuts across it" );
	}

	private static GuestSimulation Park( WalkEdgeRules rules )
	{
		var grid = GuestTests.CreateCross();
		grid.Cells.EdgeRules = rules;
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var cell = Enumerable.Range( 0, grid.CountY ).SelectMany( y => Enumerable.Range( 0, grid.CountX ).Select( x => (X: x, Y: y) ) ).First( c => grid.IsWalkable( c.X, c.Y ) );
		simulation.SpawnInPark( cell.X, cell.Y );
		return simulation;
	}

	private static GuestSimulationSnapshot SaveOf( GuestSimulation simulation ) =>
		ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( simulation.CaptureSnapshot() ) )!;

	[TestMethod]
	public void ASaveRecordsTheRulesAndARestoreRefusesTheOtherRules()
	{
		var walkSave = SaveOf( Park( WalkEdgeRules.ThemeParkInc ) );
		Assert.IsTrue( walkSave.ThemeParkIncNativeWalk, "the save records the navigator's rules" );
		StringAssert.Contains( Assert.ThrowsException<System.IO.InvalidDataException>( () => Park( WalkEdgeRules.ThemeParkWorld ).RestoreSnapshot( walkSave ) ).Message,
			"made with Theme Park Inc's navigator rules" );
		Park( WalkEdgeRules.ThemeParkInc ).RestoreSnapshot( walkSave );

		var plainSave = SaveOf( Park( WalkEdgeRules.ThemeParkWorld ) );
		Assert.IsFalse( plainSave.ThemeParkIncNativeWalk );
		Assert.IsFalse( System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( plainSave ) ).Contains( nameof( GuestSimulationSnapshot.ThemeParkIncNativeWalk ) ),
			"a Theme Park World save does not write the member: its bytes are unchanged" );
		// a Theme Park Inc save made before NV4 (the switch off) is refused by every Theme Park Inc park now
		StringAssert.Contains( Assert.ThrowsException<System.IO.InvalidDataException>( () => Park( WalkEdgeRules.ThemeParkInc ).RestoreSnapshot( plainSave ) ).Message,
			"made before Theme Park Inc's own navigator" );
		Park( WalkEdgeRules.ThemeParkWorld ).RestoreSnapshot( plainSave );
	}

	[TestMethod]
	public void AThemeParkWorldResumeFileCannotCarryTheSwitch()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		ParkResumeSaveFile.ValidateShape( snapshot );
		var marked = snapshot with { Guests = snapshot.Guests! with { ThemeParkIncNativeWalk = true } };
		StringAssert.Contains( Assert.ThrowsException<System.IO.InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( marked ) ).Message,
			"only a Theme Park Inc park walks with Theme Park Inc's navigator rules" );
	}

	/// <summary>A Theme Park World resume file whose layout counter counts the simulation's calls is refused (the lead's NV3 review: it is Theme Park Inc's navigator's alone).</summary>
	[TestMethod]
	public void AThemeParkWorldResumeFileCannotCarryASimulationCounter()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		var counted = snapshot with { Layout = snapshot.Layout! with { Cells = snapshot.Layout.Cells! with { SimulationStampCounter = true, StampCounter = 12 } } };
		StringAssert.Contains( Assert.ThrowsException<System.IO.InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( counted ) ).Message,
			"counts the simulation's calls in a park without Theme Park Inc's navigator" );
	}

	[TestMethod]
	public void AMapCopiedForAPredictionKeepsItsEdgeTest()
	{
		var map = Build( WalkEdgeRules.ThemeParkInc );
		Assert.AreEqual( WalkEdgeRules.ThemeParkInc, map.CopyForPrediction().EdgeRules );
		Assert.AreEqual( Mask0, Edges( map.CopyForPrediction(), 0 ) );
	}
}

/// <summary>A new park's edge test is Theme Park Inc's in Theme Park Inc and Theme Park World's elsewhere (<see cref="WalkEdges.RulesForNewPark"/>; the default since NV4).</summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncWalkRulesTests
{
	[TestMethod]
	public void ThemeParkIncParksWalkWithTheIncEdgeTestAndThemeParkWorldParksWithTheirOwn()
	{
		var edition = GameEditions.Current;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkInc );
			Assert.AreEqual( WalkEdgeRules.ThemeParkInc, WalkEdges.RulesForNewPark() );
			GameEditions.Select( GameEdition.ThemeParkWorld );
			Assert.AreEqual( WalkEdgeRules.ThemeParkWorld, WalkEdges.RulesForNewPark() );
		}
		finally
		{
			GameEditions.Select( edition );
		}
	}
}
