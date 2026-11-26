using System;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's navigator object (NV2, docs/reverse/TPI-NAVIGATOR.md): every member of the 177-byte record (<see cref="GuestNavigation.ToRecord"/>), the steering tail's local axes, the
/// set-up's subpath_dist slots, the whole progress history word and the single-rounded square root. The expected records are Game.exe's own: its constructor 0x63ED10, set-up 0x63E3A0 and
/// steering step 0x63DD20 ran three walks on the same cells in the dev-only x86 oracle at the game thread's control word 0x7F (<c>tools/ppc-analysis/oracle_x86/tpi_navigator_golden.py</c>),
/// and its serializer 0x63AD30 loaded and saved every record back unchanged. Only path_timestamp (+0x48) is left out: Game.exe's set-up takes the raised stamp counter, OpenTPW's the cell map's
/// edit count (the native counter is NV3).
/// </summary>
[TestClass]
public class ThemeParkIncNavigatorTests
{
	private static readonly (int X, int Y) Origin = (40, 40);
	/// <summary>'#' path (links 0x55), 'q' queue (links 0x55), '~' walkway 0x1E, 'O' walkway with the obstacle flag, 'H' type 8.</summary>
	private static readonly string[] Rows =
	[
		"################",
		"#....~O..#.....#",
		"#.##.#####.###.#",
		"#.#H.#...#.#...#",
		"#.#..q...#.#.~.#",
		"#.####.###.#.#.#",
		"#......#...#.#.#",
		"######.#.###.#.#",
		"#....#...#...#.#",
		"################",
	];
	/// <summary>'u' marks an unowned cell (flag 0x40).</summary>
	private static readonly string[] Unowned =
	[
		"................",
		"...u......u.....",
		"....u...........",
		"....u.......u...",
		"......u.........",
		"................",
		".u.....u........",
		"........u.......",
		"...........u....",
		"................",
	];

	/// <summary>(start x, y, velocity, mask, max speed, max force, target x, y, steps), as the golden script's.</summary>
	private static readonly (int X, int Y, int Vx, int Vy, int Mask, int Speed, int Force, int Tx, int Ty, int Steps)[] Walks =
	[
		((41 << 16) + 0x6000, (41 << 16) + 0x9000, 1200, -800, 0, 13107, 26214, (54 << 16) + 0x5000, (48 << 16) + 0xA000, 140),
		((46 << 16) + 0x8000, (46 << 16) + 0x4000, 0, 0, 1, 7733, 15466, (44 << 16) + 0x2000, (41 << 16) + 0x7000, 60),
		((44 << 16) + 0x3000, (44 << 16) + 0xC000, -3000, 2500, 0, 26214, 52428, (53 << 16) + 0x8000, (43 << 16) + 0x8000, 70),
	];

	private sealed record Golden( int Setup, string Results, string Digest, (int Step, string Record)[] Records );

	/// <summary>Game.exe's walks: the set-up's answer, the walk step's results, the SHA-256 of every record with path_timestamp zeroed, and the records after the set-up and every 20th step.</summary>
	private static readonly Golden[] Expected =
	[
	// walk 0: set-up 1
	new( 1, "11111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111", "0fed0bf00ad8db314ddc3420b66bce6d4280b8ab1154c6edb1c924f9560e6ca5",
	[
		(0, "00000000000000000000000000000000000000000000000000000000000000000000010066660000333300000000000000000000050000000000000000000000000000000000000c0000001b000050360000a03000010000000b00000000802b0000602900009029003333000000802a0000802d000000030000802d0000802d000000030000802d0000802a00000004000080310000802a0000000200008031000080280000000000b0040000e0fcffff"),
		(20, "00000000000000000000000000000000c7cf000051a30000af5cffffc7cf00000000010066660000333300000000000000000000050000000100000000a41a0000000000000000090000001b000050360000a03000010000000b00000000802b00f1962a00875f2d003333000000802a0000802d000000030000802d0000802d000000030000802d0000802a00000004000080310000802a000000020000803100008028000000000095280000e61f0000"),
		(40, "000000000000000000000000000000000a030000b4fcfeff4c0301000a0300000000010066660000333300000000000000000000050000000200000000f7330000000000000000060000001b000050360000a03000010000000b00000000802b00e97f2d00952b2c003333000000802a0000802d000000030000802d0000802d000000030000802d0000802a00000004000080310000802a00000002000080310000802800000000009b00000058ccffff"),
		(60, "00000000000000000000000000000000640301006903000097fcffff640301000000010066660000333300000000000000000000050000000300000000b54c0000000000000000020000001b000050360000a03000010000000b00000000802b002d0b3000fd7a2a003333000000802a0000802d000000030000802d0000802d000000030000802d0000802a00000004000080310000802a0000000200008031000080280000000000ad330000ae000000"),
		(80, "000000000000000000000000000000000503010078f4ffff880b0000050301000000010066660000333300000000000000000000050000000000000000dc0900000000000000001100000004000050360000a0300002000000060000008b1f1b00f96f3200bb772800333300000080370000802800000009000080370000803100000002000080350000803100000005000080350000802c00000001000080360000802c00000000009a330000b4fdffff"),
		(100, "000000000000000000000000000000005503010037020000c9fdffff5503010000000100666600003333000000000000000000000500000000000000000a3000000000000000001100000004000050360000a0300002000000060000008b1f1b008a783600b37d2800333300000080370000802800000009000080370000803100000002000080350000803100000005000080350000802c00000001000080360000802c0000000000aa33000071000000"),
		(120, "00000000000000000000000000000000b4fdffff05030100fbfcfeffb4fdffff0000010066660000333300000000000000000000050000000100000000425700000000000000000800000004000050360000a0300002000000060000008b1f1b002c8d3700b9a52b00333300000080370000802800000009000080370000803100000002000080350000803100000005000080350000802c00000001000080360000802c00000000008bffffff9a330000"),
		(140, "00000000000000000000000000000000b9fdffff55030100abfcfeffb9fdffff00000100666600003333000000000000000000000500000001000000007d7d00000000000000000800000004000050360000a0300002000000060000008b1f1b000b84370018ae2f00333300000080370000802800000009000080370000803100000002000080350000803100000005000080350000802c00000001000080360000802c00000000008cffffffaa330000"),
	] ),
	// walk 1: set-up 1
	new( 1, "111111111111111111111111111111111111111111111111111000000000", "cdd843c7884eb242f00c8d05ed2951d0e4fb619daa496adf784d214bb5bec1e3",
	[
		(0, "0000000000000000000000000000000000000000000000000000000000000000000001006a3c0000351e000000000000010000000300000000000000000000000000000000000005000000000000202c000070290001000000030000000060060000802e0000402e003333000000802d0000802d000000040000802d00008029000000010000802c0000802900000000000000000000000000000000000000000000000000000000000000000000000000"),
		(20, "00000000000000000000000000000000f7f7ffff77fafeff89050100f7f7ffff000001006a3c0000351e000000000000010000000300000001000000006467000000000000000001000000000000202c00007029000100000003000000006006005e952d0035422c003333000000802d0000802d000000040000802d00008029000000010000802c0000802900000000000000000000000000000000000000000000000000000000000fffffff5ae1ffff"),
		(40, "0000000000000000000000000000000044f8ffff2afefeffd601010044f8ffff000001006a3c0000351e00000000000001000000030000000100000000e0c8000000000000000001000000000000202c0000702900010000000300000000600600cc822d000bde29003333000000802d0000802d000000040000802d00008029000000010000802c00008029000000000000000000000000000000000000000000000000000000000018ffffffc9e1ffff"),
		(60, "0000000000000000000000000000000087fd0000e6daffff1a25000087fd0000000001006a3c0000351e0000000000000100000003000000020000000100000100ff020000000000000000000000202c0000702900010000000300000000600600a7662c00c76f29003333000000802d0000802d000000040000802d00008029000000010000802c00008029000000000000000000000000000000000000000000000000000000000029000000faffffff"),
	] ),
	// walk 2: set-up 0
	new( 0, "2222222222222222222222222222222222222222222222222222222222222222222222", "7be3ee9edcd30d32f8c3be7c8a2f19d44934375859fe66595094901445e373da",
	[
		(0, "000000000000000000000000000000000000000000000000000000000000000000000100cccc0000666600000100000000000000000000000000000001000000000000000000000000000000000080350000802b0000000000000000000000000000302c0000c02c003333000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000048f4ffffc4090000"),
		(20, "000000000000000000000000000000000000ffff00000000000000000000ffff00000100cccc000066660000010000000000000000000000000000000100000100ffff070000000000000000000080350000802b000000000000000000000000003e242c00bfc92c0033330000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffffffff00000000"),
		(40, "000000000000000000000000000000000000ffff00000000000000000000ffff00000100cccc000066660000010000000000000000000000000000000100000100ffffffff00000000000000000080350000802b000000000000000000000000002a242c00bfc92c0033330000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffffffff00000000"),
		(60, "000000000000000000000000000000000000ffff00000000000000000000ffff00000100cccc000066660000010000000000000000000000000000000100000100ffffffff00000000000000000080350000802b0000000000000000000000000016242c00bfc92c0033330000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffffffff00000000"),
		(70, "000000000000000000000000000000000000ffff00000000000000000000ffff00000100cccc000066660000010000000000000000000000000000000100000100ffffffff00000000000000000080350000802b000000000000000000000000000c242c00bfc92c0033330000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffffffff00000000"),
	] ),
	];

	private const int TimestampAt = 85;

	internal static ParkCellMap Map()
	{
		var map = new ParkCellMap( 128, 128 ) { EdgeRules = WalkEdgeRules.ThemeParkInc };
		for ( var y = 0; y < Rows.Length; y++ )
		{
			for ( var x = 0; x < Rows[y].Length; x++ )
			{
				var (cx, cy) = (Origin.X + x, Origin.Y + y);
				byte type = Rows[y][x] switch { '#' => 1, 'q' => 3, '~' or 'O' => 0x1E, 'H' => 8, _ => 0 };
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
		// the oracle's map has no stamped block and a counter of 0
		map.RestoreBlockStamps( 0, null );
		return map;
	}

	private static byte[] WithoutTimestamp( byte[] record )
	{
		var copy = (byte[])record.Clone();
		Array.Clear( copy, TimestampAt, 4 );
		return copy;
	}

	private static (bool Setup, string Results, byte[][] Records) Run( int index )
	{
		var walk = Walks[index];
		var map = Map();
		var navigation = new GuestNavigation { X = walk.X, Y = walk.Y, VelocityX = walk.Vx, VelocityY = walk.Vy, Mask = walk.Mask, MaxSpeed = walk.Speed, MaxForce = walk.Force };
		var setup = navigation.Navigate( map, walk.Tx, walk.Ty );
		var records = new byte[walk.Steps + 1][];
		records[0] = navigation.ToRecord().Raw;
		var results = new System.Text.StringBuilder();
		for ( var step = 1; step <= walk.Steps; step++ )
		{
			results.Append( (int)navigation.Step( navigation.MaxSpeed, navigation.MaxForce, map, static ( _, _ ) => false ) );
			records[step] = navigation.ToRecord().Raw;
		}
		return (setup, results.ToString(), records);
	}

	[TestMethod]
	public void EveryNavigatorMemberFollowsGameExeThroughWholeWalks()
	{
		for ( var index = 0; index < Walks.Length; index++ )
		{
			var (setup, results, records) = Run( index );
			var expected = Expected[index];
			Assert.AreEqual( expected.Setup == 1, setup, $"walk {index}: the set-up" );
			Assert.AreEqual( expected.Results, results, $"walk {index}: the walk step's results" );
			foreach ( var (step, record) in expected.Records )
				Assert.AreEqual( Convert.ToHexStringLower( WithoutTimestamp( Convert.FromHexString( record ) ) ), Convert.ToHexStringLower( WithoutTimestamp( records[step] ) ),
					$"walk {index}: the record after step {step}" );
			var digest = SHA256.HashData( records.SelectMany( WithoutTimestamp ).ToArray() );
			Assert.AreEqual( expected.Digest, Convert.ToHexStringLower( digest ), $"walk {index}: every record" );
		}
	}

	[TestMethod]
	public void EveryGameExeRecordLoadsAndSavesBackUnchanged()
	{
		foreach ( var golden in Expected )
		{
			foreach ( var (step, hex) in golden.Records )
			{
				var bytes = Convert.FromHexString( hex );
				Assert.IsNull( GuestNavigation.RecordProblem( SaveNavigation.Read( bytes ) ), $"step {step}" );
				var navigation = new GuestNavigation();
				navigation.LoadRecord( SaveNavigation.Read( bytes ) );
				CollectionAssert.AreEqual( bytes, navigation.ToRecord().Raw, $"step {step}: the load half then the save half" );
				CollectionAssert.AreEqual( bytes, SaveNavigation.Read( bytes ).Write(), $"step {step}: the shared layout" );
			}
		}
	}

	[TestMethod]
	public void ALoadedRecordContinuesTheWalk()
	{
		// the record after step 20 of the first walk, loaded into a fresh navigator, steps on to Game.exe's record after step 40
		var expected = Expected[0];
		var map = Map();
		var navigation = new GuestNavigation();
		navigation.LoadRecord( SaveNavigation.Read( Convert.FromHexString( expected.Records.Single( entry => entry.Step == 20 ).Record ) ) );
		for ( var step = 21; step <= 40; step++ )
			navigation.Step( navigation.MaxSpeed, navigation.MaxForce, map, static ( _, _ ) => false );
		Assert.AreEqual( Convert.ToHexStringLower( WithoutTimestamp( Convert.FromHexString( expected.Records.Single( entry => entry.Step == 40 ).Record ) ) ),
			Convert.ToHexStringLower( WithoutTimestamp( navigation.ToRecord().Raw ) ) );
	}

	[TestMethod]
	public void AFreshNavigatorHoldsTheConstructorsMembers()
	{
		// [BIN:TPI-EXE:0x0063ED10 navigator constructor] mass 0x10000, radius 0x3333, max force 0x6666, max speed 0x3333, finished 1; every other written member 0, the buffer slots zeroed by 0x63D820
		var record = new GuestNavigation().ToRecord();
		Assert.AreEqual( (0x10000, 0x3333, 0x6666, 0x3333, (byte)1), (record.Mass.Raw, record.Radius.Raw, record.MaxForce.Raw, record.MaxSpeed.Raw, record.PathFinished) );
		Assert.IsTrue( record.Subpaths.All( subpath => subpath.Buffer.X.Raw == 0 && subpath.Buffer.Y.Raw == 0 && subpath.Distance.Raw == 0 ) );
		Assert.AreEqual( (0, 0, 0, 0), (record.Force.X.Raw, record.Force.Y.Raw, record.FormationPos.X.Raw, record.FormationPos.Y.Raw) );
		Assert.AreEqual( SaveNavigation.Bytes, record.Raw.Length );
	}

	[TestMethod]
	public void TheSquareRootRoundsToASingleOnlyInThemeParkIncsWalk()
	{
		// Game.exe 0x5D2060 and 0x63E150 at the control word 0x7F (oracle tpi-exe-navigator-root). Theme Park World's walk keeps the exact root: X25 ran tp.exe at 0x27F, and its
		// 0x7F reading is a Theme Park World follow-up (docs/reverse/TPI-NAVIGATOR.md)
		Assert.AreEqual( (4999, 5000), (Fixed16.Root( 24999999, single: false ), Fixed16.Root( 24999999, single: true )) );
		Assert.AreEqual( (65535, 65536), (Fixed16.Root( 0xFFFFFFFF, single: false ), Fixed16.Root( 0xFFFFFFFF, single: true )) );
		Assert.AreEqual( (5517, 5518), (Fixed16.Length( -3781, -4019 ), Fixed16.Length( -3781, -4019, single: true )) );
		Assert.AreEqual( ((63000, 18060), (59850, 17157)), (Fixed16.Normalize( 3150, 903, single: false ), Fixed16.Normalize( 3150, 903, single: true )) );
		Assert.AreEqual( (0, 0), Fixed16.Normalize( 0, 0, single: true ), "a zero velocity gives zero axes" );
	}

	private static GuestSnapshotTests.Rig Rig( WalkEdgeRules rules )
	{
		var rig = new GuestSnapshotTests.Rig();
		rig.Grid.Cells.EdgeRules = rules;
		return rig;
	}

	/// <summary>
	/// A guest mid-walk with Theme Park Inc's native navigator survives the resume codec: its snapshot carries the navigator's record (the members its own fields do not), the restored
	/// park hashes alike (the native members join the digest) and continues identically. A Theme Park World park's save carries no record.
	/// </summary>
	[TestMethod]
	public void ANativeWalkSurvivesTheCodecAndContinues()
	{
		var park = Rig( WalkEdgeRules.ThemeParkInc );
		static bool MidWalk( Guest guest ) => guest.Navigation is { Original: true, Native: true, Arrived: false, Count: > 0 } && guest.IsMoving;
		var tick = GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.Guests.Any( MidWalk ), 0, 6000 );
		var walker = park.Simulation.Guests.FirstOrDefault( MidWalk );
		Assert.IsNotNull( walker, $"the harness reached a mid-walk guest (tick {tick})" );
		Assert.IsTrue( walker.Navigation.LocalXAxisX != 0 || walker.Navigation.LocalXAxisY != 0, "a moving walker has a local x axis" );

		var loaded = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( park.Simulation.CaptureSnapshot() ) );
		Assert.IsTrue( loaded.ThemeParkIncNativeWalk );
		var saved = loaded.Guests.Single( guest => guest.Id == walker.Id );
		CollectionAssert.AreEqual( walker.Navigation.ToRecord().Raw, saved.Navigation.NativeRecord, "the snapshot carries the walker's record" );

		var resumed = Rig( WalkEdgeRules.ThemeParkInc );
		// the layout snapshot carries the cell map's counter, which counts the simulation's calls here (NV3)
		resumed.Grid.Cells.RestoreBlockStamps( park.Grid.Cells.StampCounter, park.Grid.Cells.CaptureBlockStamps() );
		resumed.Simulation.RestoreSnapshot( loaded );
		foreach ( var attraction in loaded.Attractions )
			resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script[RideVariables.VAR_LETMEON] = attraction.LetMeOnWritten ? attraction.CalledGuest : 0;
		CollectionAssert.AreEqual( walker.Navigation.ToRecord().Raw, resumed.Simulation.Find( walker.Id )!.Navigation.ToRecord().Raw, "the restored record" );
		Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ) );
		var lastTick = tick + 300;
		for ( ; tick <= lastTick; tick++ )
		{
			park.Step();
			GuestSnapshotTests.Drive( park, tick );
			resumed.Step();
			GuestSnapshotTests.Drive( resumed, tick );
			Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ), $"the parks diverged at tick {tick}" );
		}

		var world = Rig( WalkEdgeRules.ThemeParkWorld );
		GuestSnapshotTests.RunUntil( world, rig => rig.Simulation.Guests.Any( guest => guest.Navigation is { Original: true, Count: > 0 } ), 0, 6000 );
		Assert.IsFalse( world.Simulation.Guests.Any( guest => guest.Navigation.Native ) );
		Assert.IsFalse( System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( world.Simulation.CaptureSnapshot() ) ).Contains( nameof( GuestNavigationSnapshot.NativeRecord ) ),
			"a Theme Park World walk writes no record: its save is unchanged" );
	}

	/// <summary>
	/// A native guest saved while its walk is a walk to its own point (SetDestination: standing, riding, placed on the grid; an explicit route, not the original's search) keeps its whole
	/// navigator: the save carries the record, and the restored park hashes alike through the guest's next original walk, whose set-up may fail and keep the old stamp and slots.
	/// </summary>
	[TestMethod]
	public void ANativeGuestSavedWhileStandingKeepsItsNavigator()
	{
		var park = Rig( WalkEdgeRules.ThemeParkInc );
		static bool MidWalk( Guest guest ) => guest.Navigation is { Native: true, Original: true, Arrived: false, Count: > 0 } && guest.IsMoving;
		var tick = GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.Guests.Any( MidWalk ), 0, 6000 );
		var stander = park.Simulation.Guests.FirstOrDefault( MidWalk );
		Assert.IsNotNull( stander, $"the harness reached a native walker (tick {tick})" );
		// the removal message of its target (here a bin's, tp.exe 0x4FB360): the walker stops where it walks (SetDestination to its own point, StayWhereItStands)
		stander.BinId = 0x7777;
		park.Simulation.BinRemoved( 0x7777 );
		Assert.IsTrue( stander.Navigation is { Native: true, Original: false }, "a native stand-still walk" );

		var loaded = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( park.Simulation.CaptureSnapshot() ) );
		var saved = loaded.Guests.Single( guest => guest.Id == stander.Id );
		Assert.IsNull( saved.Navigation.OriginalWalk, "the snapshot's own fields hold a route walk" );
		CollectionAssert.AreEqual( stander.Navigation.ToRecord().Raw, saved.Navigation.NativeRecord, "and the record the whole navigator" );

		var resumed = Rig( WalkEdgeRules.ThemeParkInc );
		// the layout snapshot carries the cell map's counter, which counts the simulation's calls here (NV3)
		resumed.Grid.Cells.RestoreBlockStamps( park.Grid.Cells.StampCounter, park.Grid.Cells.CaptureBlockStamps() );
		resumed.Simulation.RestoreSnapshot( loaded );
		foreach ( var attraction in loaded.Attractions )
			resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script[RideVariables.VAR_LETMEON] = attraction.LetMeOnWritten ? attraction.CalledGuest : 0;
		var restored = resumed.Simulation.Find( stander.Id )!;
		CollectionAssert.AreEqual( stander.Navigation.ToRecord().Raw, restored.Navigation.ToRecord().Raw, "every navigator member is back" );
		Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ) );
		var walkedAgain = false;
		// at least 300 ticks, and on until the guest has walked an original walk again (at most 2000)
		for ( long end = tick + 2000, least = tick + 300; tick <= end && (tick <= least || !walkedAgain); tick++ )
		{
			park.Step();
			GuestSnapshotTests.Drive( park, tick );
			resumed.Step();
			GuestSnapshotTests.Drive( resumed, tick );
			Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ), $"the parks diverged at tick {tick}" );
			walkedAgain |= park.Simulation.Find( stander.Id )?.Navigation.Original == true;
		}
		Assert.IsTrue( walkedAgain, "the guest set up an original walk again after the restore" );
	}

	[TestMethod]
	public void ANativeRecordThatDisagreesWithTheWalkIsRefused()
	{
		var park = Rig( WalkEdgeRules.ThemeParkInc );
		GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.Guests.Any( guest => guest.Navigation is { Native: true, Original: true, Count: > 0 } ), 0, 6000 );
		var snapshot = park.Simulation.CaptureSnapshot();
		var guest = snapshot.Guests.First( guest => guest.Navigation.NativeRecord != null );
		var record = (byte[])guest.Navigation.NativeRecord!.Clone();
		record[101]++; // pos.y (record bytes 101..104)
		var forged = snapshot with { Guests = snapshot.Guests.Select( entry => entry == guest ? entry with { Navigation = entry.Navigation with { NativeRecord = record } } : entry ).ToArray() };
		StringAssert.Contains( Assert.ThrowsException<System.IO.InvalidDataException>( () => Rig( WalkEdgeRules.ThemeParkInc ).Simulation.RestoreSnapshot( forged ) ).Message,
			"does not match the walk's own fields" );
		var world = Rig( WalkEdgeRules.ThemeParkWorld );
		StringAssert.Contains( Assert.ThrowsException<System.IO.InvalidDataException>( () => world.Simulation.RestoreSnapshot( snapshot with { ThemeParkIncNativeWalk = false } ) ).Message,
			"without Theme Park Inc's navigator rules" );
	}

	[TestMethod]
	public void ARecordTheWalkCannotHoldIsRefused()
	{
		var bytes = new GuestNavigation().ToRecord().Raw;
		var odd = (byte[])bytes.Clone();
		odd[109] = 1; // subpath_buffer[0].x: no cell centre
		StringAssert.Contains( GuestNavigation.RecordProblem( SaveNavigation.Read( odd ) ), "not a cell centre" );
		var flag = (byte[])bytes.Clone();
		flag[60] = 2; // path_finished
		StringAssert.Contains( GuestNavigation.RecordProblem( SaveNavigation.Read( flag ) ), "finished flag" );
		var counted = (byte[])bytes.Clone();
		counted[52] = 2; // path_buffer_count 2 with no slot written
		StringAssert.Contains( GuestNavigation.RecordProblem( SaveNavigation.Read( counted ) ), "waypoint slots are written for a buffer count of 2" );
		var index = (byte[])bytes.Clone();
		index[56] = 3; // path_count (the index) beyond the buffer count 0
		StringAssert.Contains( GuestNavigation.RecordProblem( SaveNavigation.Read( index ) ), "outside the buffer" );
	}
}
