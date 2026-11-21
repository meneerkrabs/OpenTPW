using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's obstacles as things of the world (docs/reverse/TPI-MISSIONS.md §6): the placement's and the opening's cell writes, the prebuilt Science park's obstacle cells and things, and the
/// obstacle cells in a layout snapshot. The data tests need OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncObstacleTests
{
	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	internal static void UseTpiData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		ObjectCatalog.BonusDataRoot = null;
	}

	/// <summary>The prebuilt Science park's attraction things as the obstacle code's things: the saved chain, a placement taking the next id.</summary>
	internal sealed class PrebuiltThings( ThemeParkIncSaveWorld save ) : IThemeParkIncObstacleThings
	{
		public Dictionary<ushort, int> Live { get; } = save.Attractions.ToDictionary( attraction => (ushort)attraction.Id, attraction => attraction.InfoId );
		public List<int> Placed { get; } = new();
		public int? TypeOf( ushort thing ) => Live.TryGetValue( thing, out var type ) ? type : null;
		public void Place( int type )
		{
			Placed.Add( type );
			Live[(ushort)(Live.Keys.Max( id => id ) + 1)] = type;
		}
		public void Destroy( ushort thing ) => Assert.IsTrue( Live.Remove( thing ), $"thing {thing} is destroyed once" );
	}

	/// <summary>The prebuilt Science park as a headless world: its cells, its attraction things and the zone's obstacle types.</summary>
	internal sealed record Science( OriginalPark Park, ParkCellMap Cells, PrebuiltThings Things, IReadOnlyDictionary<int, ThemeParkIncObstacleType> Types );

	internal static Science LoadScience()
	{
		var park = OriginalPark.Load( "science" );
		return new Science( park, Level.OriginalCellMap( park, null!, null ), new PrebuiltThings( park.Save!.ThemeParkIncWorld! ), ThemeParkIncObstacleTypes.Load( FileSystem, "science" ) );
	}

	private static ThemeParkIncObstacleType Synthetic( string[] after, int x, int y, int gateTo = 0, bool delete = false, int replace = 0 ) =>
		new( 9000, "synthetic", null, ThemeParkIncMapGrid.Parse( after, "after" ), 0, 0, true, 0, false, gateTo, false, false, x, y, 0, 0, delete, replace, false );

	[TestMethod]
	public void TheOpeningWritesTheAfterShapeAndKeepsTheMapEdgeBlocked()
	{
		var cells = new ParkCellMap( 128, 128 );
		cells.SetType( 1, 0, 8 );
		cells.SetOwner( 1, 0, 2 );
		cells.SetObstacleFlag( 1, 0, true );
		cells.SetType( 2, 0, 5 );
		cells.SetObstacleFlag( 2, 0, true );
		cells.SetType( 1, 1, 8 );
		cells.SetOwner( 1, 1, 2 );
		cells.SetObstacleFlag( 1, 1, true );
		cells.SetType( 2, 1, 8 );
		cells.SetOwner( 2, 1, 2 );
		cells.SetObstacleFlag( 2, 1, true );
		// rows bottom first: row 0 is y = 0 (the map's edge), row 1 is y = 1
		var fate = ThemeParkIncObstacles.Open( cells, Synthetic( new[] { "%X", ".X" }, 1, 0 ) );
		Assert.AreEqual( ThemeParkIncObstacleFate.Stays, fate, "neither ReplaceWithID nor DeleteAfterUse: the thing stays" );
		Assert.AreEqual( (byte)5, cells.RawTypeAt( 1, 0 ), "an edge cell becomes type 5 after its value" );
		Assert.AreEqual( (ushort)0, cells.OwnerAt( 1, 0 ) );
		Assert.IsFalse( cells.ObstacleFlagAt( 1, 0 ) );
		Assert.AreEqual( (byte)5, cells.RawTypeAt( 2, 0 ) );
		Assert.AreEqual( (short)1, cells.PlacementCountAt( 2, 0 ), "the edge's second 5 over an X counts a placement (0x67CB70: the same non-zero type)" );
		Assert.AreEqual( (byte)8, cells.RawTypeAt( 1, 1 ), "a % cell keeps its type and owner" );
		Assert.AreEqual( (ushort)2, cells.OwnerAt( 1, 1 ) );
		Assert.IsFalse( cells.ObstacleFlagAt( 1, 1 ), "but loses the flag" );
		Assert.AreEqual( (byte)5, cells.RawTypeAt( 2, 1 ) );
		Assert.AreEqual( (ushort)0, cells.OwnerAt( 2, 1 ) );
		Assert.AreEqual( ThemeParkIncObstacleFate.Deleted, ThemeParkIncObstacles.Open( new ParkCellMap( 128, 128 ), Synthetic( new[] { "." }, 5, 5, delete: true ) ) );
		Assert.AreEqual( ThemeParkIncObstacleFate.Replaced, ThemeParkIncObstacles.Open( new ParkCellMap( 128, 128 ), Synthetic( new[] { "." }, 5, 5, delete: true, replace: 9001 ) ), "ReplaceWithID wins" );
		var gate = new ParkCellMap( 128, 128 );
		gate.SetType( 5, 5, 8 );
		gate.SetObstacleFlag( 5, 5, true );
		Assert.AreEqual( ThemeParkIncObstacleFate.Stays, ThemeParkIncObstacles.Open( gate, Synthetic( new[] { "." }, 5, 5, gateTo: 2 ) ) );
		Assert.IsTrue( gate.RawTypeAt( 5, 5 ) == 8 && gate.ObstacleFlagAt( 5, 5 ), "a gate keeps its cells and its flag" );
	}

	[TestMethod]
	public void ThePlacementRuleWritesThePrebuiltsSavedObstacleCells()
	{
		UseTpiData();
		var park = OriginalPark.Load( "science" );
		var types = ThemeParkIncObstacleTypes.Load( FileSystem, "science" );
		var placed = ParkCellMap.FromOriginal( park.Map, null, park.Heightfield );
		foreach ( var type in types.Values.Where( type => type.IsObstacle ) )
			ThemeParkIncObstacles.Place( placed, type );
		var saved = park.Save!.Cells;
		var flagged = 0;
		for ( var y = 0; y < 128; y++ )
		{
			for ( var x = 0; x < 128; x++ )
			{
				var cell = saved[x, y];
				Assert.AreEqual( cell.ThemeParkIncObstacleFlag != 0, placed.ObstacleFlagAt( x, y ), $"flag at ({x}, {y})" );
				if ( cell.ThemeParkIncObstacleFlag == 0 )
					continue;
				flagged++;
				Assert.AreEqual( 1u, cell.ThemeParkIncObstacleFlag );
				Assert.AreEqual( cell.MapType, (uint)placed.RawTypeAt( x, y ), $"type at ({x}, {y})" );
				Assert.AreEqual( cell.MapParentId, placed.OwnerAt( x, y ), $"owner at ({x}, {y})" );
			}
		}
		Assert.IsTrue( flagged > 500, $"{flagged} obstacle cells" );
		Assert.AreEqual( (ushort)(18 * 128 + 1 + 60), ThemeParkIncObstacles.AnchorWord( types[1702] ), "the bridge's anchor takes its map offset (1, 1)" );
		Assert.AreEqual( (ushort)(18 * 128 + 1 + 60), saved[60, 18].MapParentId );
	}

	[TestMethod]
	public void TheLevelTakesThePrebuiltsObstacleCellsAndThings()
	{
		UseTpiData();
		var science = LoadScience();
		Assert.AreEqual( (byte)8, science.Cells.RawTypeAt( 41, 48 ) );
		Assert.AreEqual( (ushort)(48 * 128 + 1 + 41), science.Cells.OwnerAt( 41, 48 ) );
		Assert.IsTrue( science.Cells.ObstacleFlagAt( 41, 48 ) );
		Assert.IsFalse( science.Cells.ObstacleFlagAt( 40, 48 ) );
		var ids = new Dictionary<int, int> { [1702] = 27, [1701] = 28, [1700] = 29, [1703] = 30, [1750] = 31, [1798] = 32 };
		var catalog = ObjectCatalog.Load( "science" );
		var save = science.Park.Save!;
		foreach ( var (type, thing) in ids )
		{
			var attraction = save.ThemeParkIncWorld!.Attractions.Single( attraction => attraction.InfoId == type );
			Assert.AreEqual( thing, attraction.Id );
			var entry = catalog.Find( type )!;
			Assert.IsTrue( entry.IsFixedItem && entry.Category == ObjectCategory.Obstacle && !entry.IsTool, $"{type} is imported as a fixed item" );
			Assert.IsNotNull( entry.ScriptPath, $"{type} runs its script" );
			var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), open: true );
			Assert.IsNotNull( runtime.Script );
			Assert.IsTrue( RideSeat.FindNode( runtime.Model ) >= 0, $"{type}'s model has camera seat 1" );
			// the import keeps the record's thing (a sentinel fixed item) or, for the lake without a record, the attraction itself
			Assert.IsTrue( type == 1703 ? save.UnrecordedThemeParkIncAttractions.Single().Id == thing : save.FixedItems.Any( record => record.InfoId == type && save.FindThemeParkIncAttraction( record )?.Id == thing ),
				$"{type}: the import finds thing {thing}" );
		}
		Assert.AreEqual( (ushort)29, science.Park.Save!.ThemeParkIncWorld!.Variables.CurrentObstacle, "the World variable names the 1700 gate's thing" );
		Assert.IsNull( science.Park.LakeModel, "the lake is the obstacle 1703, not terrain.wad's dlake.MD2" );
	}

	[TestMethod]
	public void TheScienceObstaclesOpenAsTheirTypesSay()
	{
		UseTpiData();
		var science = LoadScience();
		var gates = ThemeParkIncObstacles.OpenThing( science.Cells, science.Types, science.Things, 29 );
		Assert.AreEqual( ThemeParkIncObstacleFate.Stays, gates );
		Assert.IsTrue( science.Things.Live.ContainsKey( 29 ), "the opened 1700 gate stays" );
		// AfterShape "...." / "X..X" / "X..X" / "...." at (41, 48), rows bottom first
		Assert.AreEqual( (byte)0, science.Cells.RawTypeAt( 41, 48 ) );
		Assert.AreEqual( (byte)5, science.Cells.RawTypeAt( 41, 49 ) );
		Assert.AreEqual( (byte)0, science.Cells.RawTypeAt( 42, 49 ) );
		Assert.AreEqual( (ushort)0, science.Cells.OwnerAt( 41, 49 ) );
		Assert.IsFalse( Enumerable.Range( 41, 4 ).Any( x => Enumerable.Range( 48, 4 ).Any( y => science.Cells.ObstacleFlagAt( x, y ) ) ) );

		Assert.AreEqual( ThemeParkIncObstacleFate.Deleted, ThemeParkIncObstacles.OpenThing( science.Cells, science.Types, science.Things, 28 ) );
		Assert.IsFalse( science.Things.Live.ContainsKey( 28 ), "the fallen trees go" );

		var lakeBefore = science.Cells.RawTypeAt( 7, 19 );
		Assert.AreEqual( ThemeParkIncObstacleFate.Replaced, ThemeParkIncObstacles.OpenThing( science.Cells, science.Types, science.Things, 30 ) );
		CollectionAssert.AreEqual( new[] { 1704 }, science.Things.Placed, "the drained lake replaces the lake" );
		Assert.IsFalse( science.Things.Live.ContainsKey( 30 ) );
		var after = science.Types[1703].AfterShape!;
		// 1704's own Shape ("X" at (7, 19)) is not written: the cell is what the lake's AfterShape left there, % (untouched) at that place
		Assert.AreEqual( ThemeParkIncMapGrid.Untouched, after[7 - 6, 19 - 18].Value );
		Assert.AreEqual( lakeBefore, science.Cells.RawTypeAt( 7, 19 ) );
		Assert.IsFalse( science.Cells.ObstacleFlagAt( 7, 19 ), "every AfterShape cell loses the flag, % included" );

		var gateCells = Enumerable.Range( 0, 128 ).SelectMany( x => Enumerable.Range( 0, 128 ).Select( y => (x, y) ) ).Where( cell => science.Cells.OwnerAt( cell.x, cell.y ) == 13 * 128 + 1 + 50 ).ToList();
		Assert.AreEqual( ThemeParkIncObstacleFate.Stays, ThemeParkIncObstacles.OpenThing( science.Cells, science.Types, science.Things, 31 ) );
		Assert.IsTrue( gateCells.Count > 0 && gateCells.All( cell => science.Cells.ObstacleFlagAt( cell.x, cell.y ) ), "the 1750 gate (GateTo 2) keeps its footprint and flags" );
		Assert.ThrowsException<InvalidOperationException>( () => ThemeParkIncObstacles.OpenThing( science.Cells, science.Types, science.Things, 28 ), "a destroyed thing opens no more" );
	}

	[TestMethod]
	public void TheLayoutSnapshotKeepsTheObstacleCells()
	{
		UseTpiData();
		var science = LoadScience();
		var objects = new ParkObjects( ObjectCatalog.Load( "science" ), new OriginalParkGrid( science.Park ), WorldSeed.Default );
		var data = ParkLayoutSnapshot.CaptureSnapshot( science.Cells, objects );
		Assert.IsNotNull( data.Cells.TpiObstacles );
		ParkLayoutSnapshot.ValidateSnapshot( data, objects.Catalog );
		var restored = ParkLayoutSnapshot.RestoreCellMap( data );
		for ( var y = 0; y < 128; y++ )
			for ( var x = 0; x < 128; x++ )
				Assert.AreEqual( science.Cells.ObstacleFlagAt( x, y ), restored.ObstacleFlagAt( x, y ), $"({x}, {y})" );
		Assert.AreEqual( (byte)8, restored.RawTypeAt( 41, 48 ) );
		Assert.AreEqual( (ushort)(48 * 128 + 1 + 41), restored.OwnerAt( 41, 48 ) );
		var plain = new ParkCellMap( 128, 128 );
		Assert.IsNull( ParkLayoutSnapshot.CaptureSnapshot( plain, new ParkObjects( objects.Catalog, new SandboxParkGrid() ) ).Cells.TpiObstacles, "a map without obstacle cells writes none" );
	}
}
