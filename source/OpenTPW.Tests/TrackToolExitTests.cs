using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Which tile the original's exit globals (<c>0x7639E0/E4</c>) name for each tool (docs/TRACKS.md, "The clear op"): the ride's exit tile for the line tool, Backspace and the demolition, the ride's anchor
/// cell for the edit tool, and the repair's flag tail after Backspace's last line (tp.exe <c>0x52FC80</c>, <c>0x52FD94..0x52FE3D</c>).
/// </summary>
[TestClass]
public class TrackToolExitTests
{
	private sealed class Grid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private sealed class Rig
	{
		public readonly ParkCellMap Cells = new( 40, 40 );
		public readonly TrackBuilder Builder;
		public readonly TrackStation Station = TrackStation.FromPlacement( 10, 9, 0 );
		public readonly List<(int X, int Y)> Refunded = new();
		public readonly TrackEditor Editor;

		public Rig()
		{
			Builder = new TrackBuilder( Cells, TrackFamily.Kart, () => null, new Grid() );
			Builder.PlaceStation( Station );
			var writer = new TrackLineWriter( Builder, owner: Builder.OwnerOf( Station ) );
			Assert.IsTrue( writer.Commit( (14, 10), (14, 12) ).Completed );
			Assert.IsTrue( writer.Commit( (14, 12), (8, 12) ).Completed );
			Assert.IsTrue( writer.Commit( (8, 12), (8, 10) ).Completed );
			Assert.IsTrue( TrackCircuitWalker.WalkAndRefresh( Builder, Station ).IsValid );
			Editor = new TrackEditor( Builder, Station, new TrackEditHooks { TileCleared = at => Refunded.Add( at ) } );
		}

		public TrackCellLayer Tracks => Builder.Tracks;
	}

	[TestMethod]
	public void TheAnchorIsTheCellThePlacementWasGiven()
	{
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
			Assert.AreEqual( (30, 30), TrackStation.FromPlacement( 30, 30, rotation ).Anchor, $"rotation {rotation}" );
		Assert.AreEqual( (30, 30), TrackStation.FromPlacementLegacy( 30, 30, 90 ).Anchor, "a legacy layout finds its anchor too" );
	}

	[TestMethod]
	public void ACutEndingOnTheExitTileClearsAndRefundsIt()
	{
		var rig = new Rig();
		rig.Editor.UseLineToolExit();
		// the edit tool's cut runs with the anchor in the exit globals, whatever tool state the editor was left in
		var cut = TrackEditWriter.Cut( rig.Editor, (14, 10), (14, 12) );
		Assert.IsNotNull( cut );
		CollectionAssert.AreEqual( new[] { (14, 10), (14, 12) }, cut!.Cleared.ToArray() );
		CollectionAssert.AreEqual( new[] { (14, 10), (14, 12) }, rig.Refunded.ToArray(), "the exit tile refunds like any cleared tile" );
		Assert.IsFalse( rig.Tracks.Get( 14, 10 ).IsOrigin );
		Assert.AreEqual( rig.Station.Anchor, rig.Editor.ToolExit );
	}

	[TestMethod]
	public void BackspaceRunsWithTheExitTileAfterAnEditCut()
	{
		var rig = new Rig();
		rig.Editor.UseEditToolExit();
		var tool = new CellBuildTool();
		rig.Editor.EnterLineTool( tool );
		Assert.AreEqual( rig.Station.Exit, rig.Editor.ToolExit, "the line tool is the exit tile's again" );
	}

	[TestMethod]
	public void TheLastLinesRepairTakesTheExitCornersArmsToCellsThatAreNoStationAway()
	{
		var rig = new Rig();
		rig.Editor.UseLineToolExit();
		// the exit tile (14, 10) is a corner: the arm toward the station tile (12, 10) and the one toward the track at (14, 12)
		Assert.AreEqual( 0x50, rig.Tracks.Get( 14, 10 ).Mask );
		rig.Editor.RepairStation( last: false );
		Assert.AreEqual( 0x50, rig.Tracks.Get( 14, 10 ).Mask, "without the flag the repair leaves a good corner" );
		rig.Editor.RepairStation( last: true );
		var exit = rig.Tracks.Get( 14, 10 );
		Assert.AreEqual( 0x40, exit.Mask, "the arm toward the non-station tile went" );
		Assert.AreEqual( 0x04, exit.Link, "the arm toward the station tile sets the link to its reverse (0x52FE1E)" );
		Assert.AreEqual( 0x00, rig.Tracks.Get( 14, 12 ).Mask & 0x01, "and the neighbour lost its arm back" );
	}

	[TestMethod]
	public void TheToolMarksTheStacksFirstLineForTheRepairsFlag()
	{
		var rig = new Rig();
		var open = rig.Editor.FindOpenEnd();
		var corners = open.Corners;
		Assert.IsTrue( corners.Count > 2 );
		var marked = new List<bool>();
		var tool = new CellBuildTool();
		tool.Enter( new RecordingWriter( marked ), open.End, corners,
			corners.Skip( 1 ).Select( ( end, index ) => new SegmentResult( end, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) { Start = corners[index] } ).ToArray() );
		while ( tool.Undo() ) { }
		Assert.AreEqual( corners.Count - 1, marked.Count );
		Assert.AreEqual( 1, marked.Count( first => first ), "exactly the segment the stack holds first is marked" );
		Assert.IsTrue( marked[^1], "and it is the last one taken back" );
	}

	private sealed class RecordingWriter( List<bool> marked ) : ICellLineWriter
	{
		public CellToolMode Mode => CellToolMode.KartTrack;
		public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) => throw new NotSupportedException();
		public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end ) => throw new NotSupportedException();
		public void Undo( SegmentResult segment ) => marked.Add( segment.IsFirstLine );
	}
}
