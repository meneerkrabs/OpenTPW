using System;
using System.Numerics;
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
		public Vector2 Origin => default;
		public float GetCellHeight( int x, int y ) => 0;
		public OriginalPlacementResult CheckTerrain( int x, int y ) =>
			x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : Result;
	}

	private static QueueSegmentReducer Reducer( Terrain terrain, ParkCellMap cells ) =>
		new( cells, 17, ( x, y ) => !Level.QueueTerrainAllows( terrain, cells, x, y ),
			() => true, _ => true, () => 10, _ => { },
			terminalAllowed: ( x, y ) => terrain.CheckTerrain( x, y ) == OriginalPlacementResult.Path
				&& cells.RawTypeAt( x, y ) == (byte)ParkCellType.Path && !cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned )
				&& Level.QueueTerrainAllows( terrain, cells, x, y, terminalPath: true ) );

	[TestMethod]
	public void InitialPathTerrainDoesNotHideTheTerminalNoModifyExemption()
	{
		var terrain = new Terrain();
		var cells = new ParkCellMap( 8, 8 );
		cells.SetType( 2, 3, ParkCellType.Queue );
		cells.SetOwner( 2, 3, 17 );
		cells.SetType( 4, 3, ParkCellType.Path );
		cells.SetFlags( 4, 3, ParkCellFlags.NoModify );
		var reducer = Reducer( terrain, cells );
		var ride = new RideVisitorBridge( 1, "Synthetic queue", RideVisitorKind.Ride, 5, 55, 30 ) { OwnerWord = 17 };
		var writer = new QueueLineWriter( new GuestPathGrid( cells ), ride,
			( _, _, _ ) => throw new InvalidOperationException( "Unexpected legacy preview" ),
			( _, _ ) => throw new InvalidOperationException( "Unexpected legacy commit" ),
			( _, _ ) => 0, () => 10 ).WithNativeReducer( reducer );
		Assert.IsTrue( writer.CanUseNative( (2, 3), (4, 3) ) );
		Assert.IsTrue( writer.CanUseNative( (2, 3), (4, 4) ), "use the snapped endpoint" );
		var unchanged = new QueueSegmentReducer( cells, 17, ( x, y ) => !Level.QueueTerrainAllows( terrain, cells, x, y ), () => true, _ => true, () => 10, _ => { } );
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
		cells.SetFlags( 4, 3, ParkCellFlags.NoModify | ParkCellFlags.Unowned );
		Assert.AreEqual( CellBuildResult.Occupied, Reducer( terrain, cells ).Preview( (2, 3), (4, 3) ).StoppedBy );
		Assert.ThrowsException<InvalidOperationException>( () => Reducer( terrain, cells ).Commit( (2, 3), (4, 3) ) );
		Assert.AreEqual( ParkCellType.Empty, cells.TypeAt( 2, 3 ) );
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
}
