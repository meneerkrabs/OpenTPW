using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>TRACK-010: the bridge ops of the second layer (docs/TRACKS.md "Bridges"): the map layer keeps its paths under a tile and the ops give the tile its flag word.</summary>
[TestClass]
public class TrackBridgeOpsTests
{
	private sealed class Grid : IParkGrid
	{
		public int Width => 32;
		public int Height => 32;
		public System.Numerics.Vector2 Origin => default;
		public float GetCellHeight( int x, int y ) => 0;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
	}

	private static (ParkCellMap Cells, TrackBuilder Builder) Rig( TrackFamily family = TrackFamily.Kart, params (int X, int Y)[] paths )
	{
		var cells = new ParkCellMap( 32, 32 );
		foreach ( var (x, y) in paths )
			cells.SetType( x, y, ParkCellType.Path );
		return (cells, new TrackBuilder( cells, family, () => null, new Grid() ));
	}

	[TestMethod]
	public void ALineKeepsThePathsUnderItsInnerTilesAndBridgesThem()
	{
		var (cells, builder) = Rig( TrackFamily.Kart, (14, 12), (15, 13) );
		var line = new TrackLineWriter( builder );
		Assert.AreEqual( CellBuildResult.Ok, line.Commit( (12, 12), (18, 12) ).StoppedBy );
		Assert.AreEqual( (byte)ParkCellType.Path, cells.PrimaryTypeAt( 14, 12 ), "the path stays in the map layer" );
		Assert.AreEqual( (byte)ParkCellType.Path, cells.PrimaryTypeAt( 15, 13 ) );
		Assert.AreEqual( TrackCell.FlagBridge, builder.Tracks.Get( 14, 12 ).Flags, "the straight over the path is a bridge" );
		Assert.AreEqual( 0, builder.Tracks.Get( 12, 12 ).Flags );
		Assert.AreEqual( 0, builder.Tracks.Get( 16, 12 ).Flags );
	}

	[TestMethod]
	public void TheLastTileOfALineClearsThePathsUnderIt()
	{
		var (cells, builder) = Rig( TrackFamily.Kart, (16, 12), (17, 13) );
		var line = new TrackLineWriter( builder );
		line.Commit( (12, 12), (16, 12) );
		Assert.AreEqual( (byte)0, cells.PrimaryTypeAt( 16, 12 ), "the end tile took the paths under it away" );
		Assert.AreEqual( (byte)0, cells.PrimaryTypeAt( 17, 13 ) );
		Assert.AreEqual( 0, builder.Tracks.Get( 16, 12 ).Flags & TrackCell.BridgeMask );
	}

	[TestMethod]
	public void ACornerOverAPathIsNoBridge()
	{
		var (cells, builder) = Rig( TrackFamily.Water, (14, 12) );
		var line = new TrackLineWriter( builder );
		line.Commit( (12, 12), (16, 12) );
		line.Commit( (16, 12), (16, 16) );
		var corner = builder.Tracks.Get( 16, 12 );
		Assert.AreEqual( 0x50, corner.Mask );
		Assert.AreEqual( 0, corner.Flags & TrackCell.BridgeMask );
		Assert.AreEqual( TrackCell.FlagBridge, builder.Tracks.Get( 14, 12 ).Flags );
	}

	[TestMethod]
	public void APathLaidOverATileMakesItABridgeAndClearingItLeavesTheBridge()
	{
		var (cells, builder) = Rig();
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 12, 12 ) );
		Assert.AreEqual( 0, builder.Tracks.Get( 12, 12 ).Flags );
		cells.SetType( 13, 13, ParkCellType.Path );
		Assert.AreEqual( TrackCell.FlagBridge, builder.Tracks.Get( 12, 12 ).Flags, "the path tool's op 0x85 (0x534115)" );
		cells.SetType( 13, 13, ParkCellType.Empty );
		Assert.AreEqual( TrackCell.FlagBridge, builder.Tracks.Get( 12, 12 ).Flags, "the cell remover runs no bridge op" );
		// the track tool's op 0x85 finds the four cells empty and takes the bridge state away
		TrackBridges.ApplyBase( cells, 12, 12 );
		Assert.AreEqual( 0, builder.Tracks.Get( 12, 12 ).Flags & TrackCell.BridgeMask );
	}

	[TestMethod]
	public void ABridgeStateTheOpsDoNotKnowSurvivesARestorePass()
	{
		var (_, builder) = Rig();
		builder.PlaceTile( 12, 12 );
		var tile = builder.Tracks.Get( 12, 12 );
		tile.Flags = TrackCell.FlagBridgeAbove;
		builder.Tracks.Set( 12, 12, tile );
		TrackBuilder.RefreshAll( builder.Tracks );
		Assert.AreEqual( TrackCell.FlagBridgeAbove, builder.Tracks.Get( 12, 12 ).Flags, "a restore only chooses the piece again" );
	}

	[TestMethod]
	public void TheFlagWordIsAssignedWholeByTheBaseOp()
	{
		var (_, builder) = Rig( TrackFamily.Kart, (12, 12) );
		builder.PlaceTile( 12, 12 );
		// placed as a line of one tile its paths went; lay one again and the add-on bit goes with the assignment (0x53A510)
		var tile = builder.Tracks.Get( 12, 12 );
		tile.Flags = TrackCell.FlagAddOn | TrackCell.FlagBridgeAbove;
		builder.Tracks.Set( 12, 12, tile );
		builder.Cells.SetType( 12, 12, ParkCellType.Path );
		TrackBridges.ApplyBase( builder.Cells, 12, 12 );
		Assert.AreEqual( TrackCell.FlagBridge, builder.Tracks.Get( 12, 12 ).Flags );
	}

	[TestMethod]
	public void APathLaidAgainOverAPathRunsThePathToolsOpOnEveryCell()
	{
		var (cells, builder) = Rig();
		builder.PlaceTile( 12, 12 );
		cells.SetType( 13, 13, ParkCellType.Path );
		Assert.AreEqual( TrackCell.FlagBridge, builder.Tracks.Get( 12, 12 ).Flags );
		// the tile's bridge bits go (the tile became straight after the base op, say) with the path still under it
		var tile = builder.Tracks.Get( 12, 12 );
		tile.Flags = 0;
		builder.Tracks.Set( 12, 12, tile );
		// [BIN:TPW-EXE:0x00534115 path tool bridge op] the op runs on every cell the tool lays, one that already holds a path included
		cells.SetType( 13, 13, ParkCellType.Path );
		Assert.AreEqual( TrackCell.FlagBridge, builder.Tracks.Get( 12, 12 ).Flags );
	}
}
