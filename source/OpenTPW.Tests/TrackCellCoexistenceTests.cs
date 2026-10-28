using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Path and queue cells over straight or stub kart and water track tiles (tp.exe 0x535670 reads the second layer's record apart from the cell: only a fence post, a corner or a junction refuses),
/// and the type byte's two views of such a cell (<see cref="ParkCellMap.PrimaryTypeAt"/>).
/// </summary>
[TestClass]
public class TrackCellCoexistenceTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 24;
		public int Height => 24;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	/// <summary>A kart line of three tiles along +X from (4, 8): the middle tile is a straight, the ends are single-arm stubs.</summary>
	private static (ParkCellMap Cells, TrackBuilder Builder, GuestPathGrid Grid, ParkPathBuilder Paths) Rig( bool corner = false )
	{
		var cells = new ParkCellMap( 24, 24 );
		var grid = new GuestPathGrid( cells );
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new FlatGrid() );
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 4, 8 ) );
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 6, 8 ) );
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 8, 8 ) );
		Assert.IsTrue( builder.Link( (4, 8), (6, 8) ) );
		Assert.IsTrue( builder.Link( (6, 8), (8, 8) ) );
		if ( corner )
		{
			Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 6, 10 ) );
			Assert.IsTrue( builder.Link( (6, 8), (6, 10) ) );
		}
		return (cells, builder, grid, new ParkPathBuilder( cells, grid, (ParkEconomy?)null, new FlatGrid() ));
	}

	[TestMethod]
	public void ATileShowsItsTypeInTheByteWhileTheCellIsOtherwiseEmptyAndAPathLaidOverItReplacesThatView()
	{
		var (cells, builder, _, paths) = Rig();
		Assert.AreEqual( TrackCellTypes.KartOrigin, cells.RawTypeAt( 6, 8 ) );
		Assert.AreEqual( (byte)0, cells.PrimaryTypeAt( 6, 8 ), "the cell's own type stays what the ground was" );
		Assert.AreEqual( CellBuildResult.Ok, paths.CheckCell( 6, 8 ), "a straight tile does not refuse a path" );
		var result = paths.BuildSegment( (6, 8), (6, 8) );
		Assert.IsTrue( result.Completed, result.StoppedBy.ToString() );
		Assert.AreEqual( (byte)1, cells.RawTypeAt( 6, 8 ), "the path is the cell's type" );
		Assert.AreEqual( (byte)1, cells.PrimaryTypeAt( 6, 8 ) );
		Assert.AreEqual( TrackCellTypes.KartOrigin, cells.Tracks.Get( 6, 8 ).Type, "the tile lies beneath it" );
		Assert.IsTrue( cells.Tracks.Get( 6, 8 ).IsOrigin );
		// the path comes off again: the byte shows the tile as before
		Assert.AreEqual( CellBuildResult.Ok, paths.Remove( 6, 8 ) );
		Assert.AreEqual( TrackCellTypes.KartOrigin, cells.RawTypeAt( 6, 8 ) );
		Assert.AreEqual( (byte)0, cells.PrimaryTypeAt( 6, 8 ) );
		Assert.IsNotNull( builder.OriginOf( 6, 8 ) );
	}

	[TestMethod]
	public void APathLineCrossesAStraightTileOnItsExtensionCellsToo()
	{
		var (cells, _, _, paths) = Rig();
		// (7, 9) is the +X +Y extension cell of the middle tile (6, 8): a line along x = 7 crosses it and the origin row's extension cell (7, 8)
		var result = paths.BuildSegment( (7, 6), (7, 11) );
		Assert.IsTrue( result.Completed, result.StoppedBy.ToString() );
		foreach ( var y in new[] { 8, 9 } )
		{
			Assert.AreEqual( (byte)1, cells.PrimaryTypeAt( 7, y ) );
			Assert.AreEqual( TrackCellTypes.Extension, cells.Tracks.Get( 7, y ).Type );
		}
		Assert.AreEqual( (byte)1, cells.PrimaryTypeAt( 7, 7 ) );
	}

	[TestMethod]
	public void AStubTileTakesAPathButACornerOrAJunctionDoesNot()
	{
		var (_, _, _, stubPaths) = Rig();
		Assert.AreEqual( CellBuildResult.Ok, stubPaths.CheckCell( 4, 8 ), "an end tile with one arm is a stub" );
		var (_, _, _, cornerPaths) = Rig( corner: true );
		foreach ( var cell in new[] { (6, 8), (7, 8), (6, 9), (7, 9) } )
			Assert.AreEqual( CellBuildResult.Occupied, cornerPaths.CheckCell( cell.Item1, cell.Item2 ), $"the three-arm tile refuses its cell {cell}" );
		Assert.AreEqual( CellBuildResult.Ok, cornerPaths.CheckCell( 4, 8 ), "the stub beside it still takes one" );
	}

	[TestMethod]
	public void ARemovedTileLeavesThePathOverItAndAnEmptyCellOtherwise()
	{
		var (cells, builder, _, paths) = Rig();
		Assert.IsTrue( paths.BuildSegment( (7, 7), (7, 9) ).Completed );
		builder.EraseTile( 6, 8 );
		Assert.IsTrue( cells.Tracks.Get( 6, 8 ).IsEmpty );
		Assert.AreEqual( (byte)0, cells.RawTypeAt( 6, 8 ), "the cell the path did not cover is empty" );
		foreach ( var y in new[] { 7, 8, 9 } )
			Assert.AreEqual( (byte)1, cells.RawTypeAt( 7, y ), "the path stays: the record clear touches the record only" );
	}

	[TestMethod]
	public void AnObjectIsRefusedOverAPathThatLiesOverATile()
	{
		var (cells, _, _, paths) = Rig();
		Assert.IsTrue( paths.BuildSegment( (7, 8), (7, 9) ).Completed );
		Assert.AreEqual( ObjectPathCell.FixedPath, Level.ObjectPathAt( cells, 7, 8 ) );
		Assert.AreEqual( ObjectPathCell.None, Level.ObjectPathAt( cells, 12, 12 ) );
	}

	[TestMethod]
	public void AQueueLineCrossesAStraightTileAndTheUndoLeavesTheTileView()
	{
		var (cells, _, _, _) = Rig();
		const ushort owner = 17;
		cells.SetType( 7, 5, ParkCellType.Queue );
		cells.SetOwner( 7, 5, owner );
		var reducer = new QueueSegmentReducer( cells, owner, ( _, _ ) => false, () => true, _ => true, () => 10, _ => { } );
		var line = ParkPathBuilder.LayLine( (7, 5), (7, 10) );
		Assert.IsTrue( reducer.Supports( (7, 5), (7, 10) ) );
		var result = reducer.Commit( (7, 5), (7, 10) );
		Assert.AreEqual( CellBuildResult.Ok, result.StoppedBy );
		foreach ( var (x, y) in line )
		{
			Assert.AreEqual( (byte)3, cells.PrimaryTypeAt( x, y ), $"({x}, {y})" );
			Assert.AreEqual( owner, cells.OwnerAt( x, y ) );
		}
		Assert.AreEqual( TrackCellTypes.Extension, cells.Tracks.Get( 7, 8 ).Type, "the tile beneath is still there" );
		reducer.Undo( result.QueueReceipt! );
		Assert.AreEqual( (byte)0, cells.PrimaryTypeAt( 7, 8 ) );
		Assert.AreEqual( TrackCellTypes.Extension, cells.RawTypeAt( 7, 8 ), "the undo shows the tile again" );
	}

	[TestMethod]
	public void TheReducerRefusesACornerTileAndItsExtensions()
	{
		var (cells, _, _, _) = Rig( corner: true );
		const ushort owner = 17;
		cells.SetType( 7, 5, ParkCellType.Queue );
		cells.SetOwner( 7, 5, owner );
		var reducer = new QueueSegmentReducer( cells, owner, ( _, _ ) => false, () => true, _ => true, () => 10, _ => { } );
		Assert.IsFalse( reducer.Supports( (7, 5), (7, 10) ), "the line crosses the three-arm tile's extension cells" );
	}

	[TestMethod]
	public void APathOverATileSurvivesTheLayoutAndTrackSaveRoundTrip()
	{
		var (cells, _, _, paths) = Rig();
		Assert.IsTrue( paths.BuildSegment( (7, 7), (7, 9) ).Completed );
		var data = ParkLayoutSnapshot.CaptureSnapshot( cells, new ParkObjects( null!, new SandboxParkGrid() ) );
		var restored = ParkLayoutSnapshot.RestoreCellMap( ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( ParkResumeJson.Serialize( data ) ) );
		var tracks = TrackSave.Capture( cells, new BumpWorld(), Array.Empty<(int, BumpController)>() )!;
		TrackSave.RestoreCells( restored, ParkResumeJson.Deserialize<TrackWorldSaveData>( ParkResumeJson.Serialize( tracks ) ) );
		for ( var y = 0; y < 24; y++ )
		{
			for ( var x = 0; x < 24; x++ )
			{
				Assert.AreEqual( cells.RawTypeAt( x, y ), restored.RawTypeAt( x, y ), $"({x}, {y})" );
				Assert.AreEqual( cells.PrimaryTypeAt( x, y ), restored.PrimaryTypeAt( x, y ), $"({x}, {y})" );
				Assert.AreEqual( cells.Tracks.Get( x, y ).Type, restored.Tracks.Get( x, y ).Type, $"({x}, {y})" );
			}
		}
		var (hashA, hashB) = (new StateHasher(), new StateHasher());
		cells.AddCanonicalState( hashA );
		restored.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.Value, hashB.Value );
	}

	[TestMethod]
	public void AQueuePassResidueOnACellShowingATileSurvivesTheSnapshotAndAnObjectTypeDoesNot()
	{
		var (cells, _, _, _) = Rig();
		// the queue pass's metadata on an empty cell that shows a tile (the type pass failed on its cash): the tile is no cell type
		cells.ApplyQueuePassMetadata( 7, 8, 64, 17 );
		Assert.IsTrue( cells.HasQueuePassResidue( 7, 8 ) );
		var data = ParkLayoutSnapshot.CaptureSnapshot( cells, new ParkObjects( null!, new SandboxParkGrid() ) );
		var restored = ParkLayoutSnapshot.RestoreCellMap( ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( ParkResumeJson.Serialize( data ) ) );
		Assert.AreEqual( (ushort)17, restored.OwnerAt( 7, 8 ) );
		Assert.AreEqual( (byte)64, restored.QueueLinkAt( 7, 8 ) );
		Assert.IsTrue( restored.HasQueuePassResidue( 7, 8 ) );
		var object4 = new ParkCellMap( 24, 24 );
		object4.SetType( 3, 3, ParkCellTypes.Object );
		Assert.ThrowsException<InvalidOperationException>( () => object4.ApplyQueuePassMetadata( 3, 3, 64, 17 ) );
	}
}
