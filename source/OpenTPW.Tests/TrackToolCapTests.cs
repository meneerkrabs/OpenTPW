using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The vertex stack of the kart and water track tools (TRACK-018): the PC pusher <c>0x52FBF0</c> stores a point while the count is at most 0x400, so the stack holds 0x401 = 1025 points, the bound
/// of the corner collectors <c>0x530830</c> and <c>0x530B70</c> as well; the path and queue tools keep their 1024.
/// </summary>
[TestClass]
public class TrackToolCapTests
{
	private sealed class Grid( int width, int height ) : IParkGrid
	{
		public int Width { get; } = width;
		public int Height { get; } = height;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	[TestMethod]
	public void TheCollectorsBoundIsTheToolsStack()
	{
		Assert.AreEqual( 0x401, TrackEditor.CornerCapacity );
		Assert.AreEqual( 1025, ((ICellLineWriter)new TrackLineWriter( new TrackBuilder( new ParkCellMap( 8, 8 ), TrackFamily.Kart, () => null, new Grid( 8, 8 ) ) )).VertexCapacity );
		var walk = new GuestPathGrid( 8, 8 );
		Assert.AreEqual( CellBuildTool.VertexCapacity, ((ICellLineWriter)new ParkPathBuilder( walk.Cells, walk, (ParkEconomy?)null, new Grid( 8, 8 ) )).VertexCapacity,
			"the path tool keeps the PPC stack's 1024" );
	}

	[DataTestMethod]
	[DataRow( TrackFamily.Kart )]
	[DataRow( TrackFamily.Water )]
	public void AFullTrackToolStackTakesNoMorePointsButStillLaysTheLine( TrackFamily family )
	{
		const int Width = 1024;
		var cells = new ParkCellMap( Width, 8 );
		var builder = new TrackBuilder( cells, family, () => null, new Grid( Width, 8 ) );
		var tool = new CellBuildTool();
		tool.Enter( new TrackLineWriter( builder ), (0, 0) );
		// along the first tile row to its end, one row down, back along the second row, one row down: 1024 clicks, one point each
		var clicks = Enumerable.Range( 1, 511 ).Select( index => (2 * index, 0) )
			.Append( (1022, 2) ).Concat( Enumerable.Range( 0, 511 ).Select( index => (1020 - 2 * index, 2) ) ).Append( (0, 4) ).ToArray();
		Assert.AreEqual( 1024, clicks.Length );
		foreach ( var click in clicks )
			Assert.IsNotNull( tool.Click( click ), $"click {click}" );
		Assert.AreEqual( 1025, tool.Vertices.Count, "the start and 1024 clicks fill the stack to its 0x401 points" );
		Assert.IsNotNull( tool.Click( (2, 4) ) );
		Assert.AreEqual( 1025, tool.Vertices.Count, "the push answers 0 and the stack is unchanged, the line is laid all the same" );
		Assert.IsTrue( builder.IsTile( 2, 4 ) );
		Assert.IsTrue( tool.IsActive, "the tool goes on from the new end: the original ignores the push's answer (0x5252D9)" );
		Assert.AreEqual( (2, 4), tool.Start );
		Assert.AreEqual( 1024, tool.SegmentCount, "no segment is recorded for the line the stack could not take" );
		Assert.IsNotNull( tool.Click( (4, 4) ) );
		Assert.IsTrue( builder.IsTile( 4, 4 ) );
		Assert.IsTrue( tool.Undo(), "Backspace takes back the last line the stack holds" );
		Assert.AreEqual( 1024, tool.Vertices.Count );
	}

	[TestMethod]
	public void TheLineToolTakesAllTheCornersTheCollectorRecorded()
	{
		var cells = new ParkCellMap( 40, 40 );
		var station = TrackStation.FromPlacement( 10, 9, 0 );
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new Grid( 40, 40 ) );
		builder.PlaceStation( station );
		var editor = new TrackEditor( builder, station );
		// a full collector stack: 0x401 recorded corners that do not end with the open end, which the walk did not push for want of room
		var corners = Enumerable.Range( 0, TrackEditor.CornerCapacity ).Select( index => (index % 2 == 0 ? 14 : 16, 10) ).ToList();
		var tool = new CellBuildTool();
		editor.EnterLineTool( tool, walked: new TrackOpenEnd( (18, 10), corners, false ) );
		Assert.AreEqual( TrackEditor.CornerCapacity, tool.Vertices.Count, "the stack is the collector's, as the original keeps it" );
		Assert.AreEqual( (18, 10), tool.Start, "and the tool starts at the end all the same (0x81EDE4/E8)" );
		Assert.AreEqual( TrackEditor.CornerCapacity - 1, tool.SegmentCount );
	}
}
