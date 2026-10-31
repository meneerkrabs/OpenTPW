using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic host-terrain/reducer integration; no original media or executable playback.</summary>
[TestClass]
public class QueueTerrainValidationTests
{
	private sealed class Terrain : IParkGrid
	{
		public OriginalPlacementResult Result = OriginalPlacementResult.Path;
		public int Width => 8;
		public int Height => 8;
		public System.Numerics.Vector2 Origin => default;
		public float GetCellHeight( int x, int y ) => 0;
		public OriginalPlacementResult CheckTerrain( int x, int y ) =>
			x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : Result;
	}

	private static QueueSegmentReducer Reducer( Terrain terrain, ParkCellMap cells ) =>
		new( cells, 17, ( x, y ) => !Level.QueueCellRulesAllow( terrain, cells, x, y ),
			() => true, _ => true, () => 10, _ => { },
			terminalAllowed: ( x, y ) => terrain.CheckTerrain( x, y ) == OriginalPlacementResult.Path
				&& cells.RawTypeAt( x, y ) == (byte)ParkCellType.Path
				&& Level.QueueCellRulesAllow( terrain, cells, x, y, terminalPath: true ) );

	[TestMethod]
	public void InitialPathTerrainDoesNotHideTheTerminalNoModifyExemption()
	{
		var terrain = new Terrain();
		var cells = new ParkCellMap( 8, 8 );
		cells.SetType( 2, 3, ParkCellType.Queue );
		cells.SetOwner( 2, 3, 17 );
		cells.SetType( 4, 3, ParkCellType.Path );
		cells.SetFlags( 4, 3, ParkCellFlags.NoModify );
		Assert.IsFalse( Level.QueueCellRulesAllow( terrain, cells, 4, 3 ), "the host refuses a nonterminal NoModify path" );
		Assert.IsTrue( Level.QueueCellRulesAllow( terrain, cells, 4, 3, terminalPath: true ), "the host must pass terminal state to the native validator" );
		var reducer = Reducer( terrain, cells );
		var ride = new RideVisitorBridge( 1, "Synthetic queue", RideVisitorKind.Ride, 5, 55, 30 ) { OwnerWord = 17 };
		var writer = new QueueLineWriter( new GuestPathGrid( cells ), ride,
			( _, _, _ ) => throw new InvalidOperationException( "Unexpected legacy preview" ),
			( _, _ ) => throw new InvalidOperationException( "Unexpected legacy commit" ),
			( _, _ ) => 0, () => 10 ).WithNativeReducer( reducer );
		Assert.IsTrue( writer.CanUseNative( (2, 3), (4, 3) ) );
		Assert.IsTrue( writer.CanUseNative( (2, 3), (4, 4) ), "use the snapped endpoint" );
		var unchanged = new QueueSegmentReducer( cells, 17, ( x, y ) => !Level.QueueCellRulesAllow( terrain, cells, x, y ), () => true, _ => true, () => 10, _ => { } );
		Assert.IsFalse( unchanged.SupportsAdmission( (2, 3), (4, 3) ), "unrelated callers receive no exception" );
		Assert.IsTrue( reducer.Supports( (2, 3), (4, 3) ) );
		var preview = writer.Preview( (2, 3), (4, 4) );
		Assert.AreEqual( CellBuildResult.Ok, preview.StoppedBy );
		Assert.IsTrue( preview.LinkPointer );
		Assert.AreEqual( ParkCellType.Queue, cells.TypeAt( 2, 3 ), "preview must not write" );
		Assert.AreEqual( ParkCellFlags.NoModify, cells.FlagsAt( 4, 3 ) );
		var commit = writer.Commit( (2, 3), (4, 3) );
		Assert.AreEqual( CellBuildResult.Ok, commit.StoppedBy );
		Assert.AreEqual( 1, commit.Built.Count );
		Assert.AreEqual( ParkCellType.Path, cells.TypeAt( 4, 3 ) );
		Assert.AreEqual( ParkCellFlags.NoModify, cells.FlagsAt( 4, 3 ) );
	}

	[TestMethod]
	public void DelegatingPathTerrainPreservesInteriorAndOwnershipRefusals()
	{
		var terrain = new Terrain();
		var cells = new ParkCellMap( 8, 8 );
		cells.SetType( 4, 3, ParkCellType.Path );
		cells.SetFlags( 4, 3, ParkCellFlags.NoModify );
		Assert.IsFalse( Reducer( terrain, cells ).Supports( (2, 3), (5, 3) ) );
		Assert.AreEqual( CellBuildResult.Occupied, Reducer( terrain, cells ).Preview( (2, 3), (5, 3) ).StoppedBy );
		Assert.ThrowsException<InvalidOperationException>( () => Reducer( terrain, cells ).Commit( (2, 3), (5, 3) ) );
		Assert.AreEqual( ParkCellFlags.NoModify, cells.FlagsAt( 4, 3 ) );
		Assert.IsFalse( Reducer( terrain, cells ).SupportsUndo( (2, 3), (4, 3) ), "terminal exception must not expand undo" );
		Assert.ThrowsException<InvalidOperationException>( () => Reducer( terrain, cells ).Undo( new QueueSegmentReceipt( (2, 3), (4, 3) ) ),
			"direct undo must use the same narrow admission as SupportsUndo" );
		Assert.AreEqual( ParkCellType.Path, cells.TypeAt( 4, 3 ) );
		Assert.AreEqual( ParkCellFlags.NoModify, cells.FlagsAt( 4, 3 ) );
		cells.SetFlags( 4, 3, ParkCellFlags.NoModify | ParkCellFlags.Unowned );
		Assert.AreEqual( CellBuildResult.Occupied, Reducer( terrain, cells ).Preview( (2, 3), (4, 3) ).StoppedBy );
		Assert.ThrowsException<InvalidOperationException>( () => Reducer( terrain, cells ).Commit( (2, 3), (4, 3) ) );
		Assert.AreEqual( ParkCellType.Empty, cells.TypeAt( 2, 3 ) );
	}

	[TestMethod]
	public void TerminalNoModifyExemptionDoesNotAdmitAFenceExtension()
	{
		var terrain = new Terrain();
		var cells = new ParkCellMap( 8, 8 );
		cells.SetType( 5, 3, ParkCellType.Path );
		cells.SetFlags( 5, 3, ParkCellFlags.NoModify );
		cells.SetLand( new ParkLand( (2, 2), (0, 2), new (int X, int Y)[] { (2, 2), (4, 2), (4, 4), (2, 4), (0, 4), (0, 2) } ) );
		Assert.AreEqual( FenceLayer.PostType, QueueCellValidator.StructureTypeAt( cells, 5, 3 ), "the terminal path is on a post's extension" );
		Assert.IsTrue( Level.QueueCellRulesAllow( terrain, cells, 7, 3 ) && Level.QueueCellRulesAllow( terrain, cells, 6, 3 ), "only the terminal cell is blocked" );
		Assert.IsFalse( Level.QueueCellRulesAllow( terrain, cells, 5, 3, terminalPath: true ), "the fence refusal still precedes the terminal-path exemption" );
		Assert.IsFalse( Reducer( terrain, cells ).SupportsAdmission( (7, 3), (5, 3) ) );
	}

	[TestMethod]
	public void TerrainHolesAndMissingLiveMapStillRefuse()
	{
		var terrain = new Terrain();
		var cells = new ParkCellMap( 8, 8 );
		Assert.IsFalse( Level.QueueTerrainAllows( terrain, null, 2, 3 ) );
		Assert.IsFalse( Level.QueueTerrainAllows( terrain, cells, -1, 3 ) );
		Assert.IsFalse( Level.QueueTerrainAllows( terrain, new ParkCellMap( 2, 2 ), 2, 3 ) );
		Assert.IsTrue( Level.QueueTerrainAllows( terrain, cells, 2, 3 ), "a cleared initial path remains buildable" );
		foreach ( var reason in new[] { OriginalPlacementResult.Water, OriginalPlacementResult.FixedWalkway, OriginalPlacementResult.NoTerrainSurface, OriginalPlacementResult.Occupied } )
		{
			terrain.Result = reason;
			Assert.IsFalse( Level.QueueTerrainAllows( terrain, cells, 2, 3 ), reason.ToString() );
		}
		terrain.Result = OriginalPlacementResult.Allowed;
		Assert.IsTrue( Level.QueueTerrainAllows( terrain, null, 2, 3 ), "plain allowed terrain does not require a live map" );
	}

	[TestMethod]
	public void ATypedWorldAsksTheValidatorAloneAndReadsNoTerrain()
	{
		// [BIN:TPW-EXE:0x00535670 no terrain read] the validator reads the cell's own type, flags and owner: the terrain the host grid reports (water, a walkway, no surface) does not refuse a cell the cell map accepts
		var terrain = new Terrain { Result = OriginalPlacementResult.Water };
		var cells = new ParkCellMap( 8, 8 );
		Assert.IsFalse( Level.QueueCellRulesAllow( terrain, cells, 2, 3 ), "a world that types no footprints keeps OpenTPW's terrain rule" );
		Assert.IsTrue( Level.QueueCellRulesAllow( terrain, cells, 2, 3, terrainRule: false ), "a typed world's cell is the validator's alone" );
		cells.SetFlags( 2, 3, ParkCellFlags.Unowned );
		Assert.IsFalse( Level.QueueCellRulesAllow( terrain, cells, 2, 3, terrainRule: false ), "the validator's own refusal (flag 0x40) still holds" );
	}
}
