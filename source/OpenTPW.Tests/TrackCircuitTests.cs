using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Asset-free tests of the water track tool and the circuit walk (docs/TRACKS.md): tile writing and cost, the snapped line, connection masks,
/// path clearing, bridges, and the walk's verdicts for a closed loop, an open track and a tee entered along its stem.
/// </summary>
[TestClass]
public class TrackCircuitTests
{
	private const int WaterCost = 500; // EconomyTestData: Costs.WaterTrackCell 500

	private sealed class TestGrid : IParkGrid
	{
		public readonly Dictionary<(int, int), OriginalPlacementResult> Terrain = new();
		public int Width { get; init; } = 40;
		public int Height { get; init; } = 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) =>
			x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : Terrain.TryGetValue( (x, y), out var result ) ? result : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private sealed class Rig
	{
		public readonly ParkCellMap Cells = new( 40, 40 );
		public readonly TestGrid Terrain = new();
		public readonly HashSet<(int, int)> Objects = new();
		public readonly ParkEconomy? Economy;
		public readonly TrackBuilder Builder;
		public readonly TrackStation Station = TrackStation.FromPlacement( 10, 9, 0 );

		public Rig( bool economy = false, int cash = 100000 )
		{
			Economy = economy ? EconomyTestData.Park( initialCash: cash ) : null;
			Builder = new TrackBuilder( Cells, TrackFamily.Water, () => Economy, Terrain, ( x, y ) => Objects.Contains( (x, y) ) );
			Builder.PlaceStation( Station );
		}

		public TrackLineWriter Writer => new( Builder );

		/// <summary>The closed loop of eight tiles: station (10,10) (12,10), then (14,10) up to (14,12), left to (8,12), down to (8,10).</summary>
		public void LayLoop()
		{
			Assert.IsTrue( Writer.Commit( (14, 10), (14, 12) ).Completed );
			Assert.IsTrue( Writer.Commit( (14, 12), (8, 12) ).Completed );
			Assert.IsTrue( Writer.Commit( (8, 12), (8, 10) ).Completed );
		}

		public TrackCircuit Walk() => TrackCircuitWalker.Walk( Builder, Station );
	}

	// ---- The station and the line tool ---------------------------------------------------------------

	[TestMethod]
	public void StationTilesFollowTheRotationAndTheCircuitStartsAtTheFirst()
	{
		// the line of 0x52A1A0 from the attraction cell (10, 10): the tiles lie one row beside the entrance row, the circuit starts at the first
		var station = TrackStation.FromPlacement( 10, 10, 0 );
		Assert.AreEqual( ((10, 11), (12, 11), (14, 11), (8, 11)), (station.First, station.Second, station.Exit, station.Entry) );
		Assert.AreEqual( TrackHeading.PosX, station.Heading );
		var turned = TrackStation.FromPlacement( 10, 10, 90 );
		Assert.AreEqual( TrackHeading.NegY, turned.Heading );
		Assert.AreEqual( ((11, 9), (11, 7), (11, 5), (11, 11)), (turned.First, turned.Second, turned.Exit, turned.Entry) );
		var back = TrackStation.FromPlacement( 10, 10, 180 );
		Assert.AreEqual( ((9, 8), (7, 8), (5, 8), (11, 8)), (back.First, back.Second, back.Exit, back.Entry) );
		var up = TrackStation.FromPlacement( 10, 10, 270 );
		Assert.AreEqual( ((8, 10), (8, 12), (8, 14), (8, 8)), (up.First, up.Second, up.Exit, up.Entry) );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var item = TrackStation.FromPlacement( 20, 20, rotation );
			var footprint = ObjectFootprint.GetCells( new ObjectShape( new[] { "****", "****", "****", "****" } ), 20, 20, rotation ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
			foreach ( var tile in item.Tiles )
				foreach ( var cell in TrackStation.TileCellsOf( tile.X, tile.Y ) )
					Assert.IsTrue( footprint.Contains( cell ), $"rotation {rotation}: station cell {cell} lies in the 4 × 4 footprint" );
		}
	}

	[TestMethod]
	public void StationCellsAreTypedAndConnectedAlongTheFlow()
	{
		var rig = new Rig();
		var cells = rig.Cells;
		Assert.AreEqual( TrackCellTypes.WaterStart, cells.RawTypeAt( 10, 10 ) );
		Assert.AreEqual( TrackCellTypes.Extension, cells.RawTypeAt( 11, 11 ) );
		Assert.AreEqual( TrackCellTypes.WaterStart, cells.RawTypeAt( 12, 10 ) );
		Assert.AreEqual( 0x44, cells.Tracks.Get( 10, 10 ).Mask );
		Assert.AreEqual( 0x44, cells.Tracks.Get( 12, 10 ).Mask );
		Assert.AreEqual( cells.Tracks.IndexOf( 10, 10 ), cells.Tracks.Get( 11, 11 ).Parent, "an extension cell names its origin" );
		Assert.AreEqual( -1, cells.Tracks.Get( 10, 10 ).Parent );
		Assert.AreEqual( 8, cells.Tracks.Count );
	}

	[TestMethod]
	public void SnapKeepsTheDominantAxisInWholeTileSteps()
	{
		Assert.AreEqual( (14, 10), TrackLineWriter.SnapEnd( (10, 10), (15, 11) ), "an odd distance in the + direction rounds toward the start" );
		Assert.AreEqual( (16, 10), TrackLineWriter.SnapEnd( (10, 10), (16, 13) ) );
		Assert.AreEqual( (10, 14), TrackLineWriter.SnapEnd( (10, 10), (11, 15) ) );
		Assert.AreEqual( (10, 10), TrackLineWriter.SnapEnd( (10, 10), (11, 10) ), "one cell is less than a tile" );
		Assert.AreEqual( (6, 10), TrackLineWriter.SnapEnd( (10, 10), (7, 10) ), "an odd distance in the − direction rounds away from the start: start + 2 × floor(−3 / 2) ([BIN:TPW-EXE:0x53AAC0], TRACK-009)" );
		Assert.AreEqual( (8, 10), TrackLineWriter.SnapEnd( (10, 10), (9, 10) ), "one cell toward −X already makes a tile step" );
		Assert.AreEqual( (10, 8), TrackLineWriter.SnapEnd( (10, 10), (10, 9) ), "and toward −Y" );
		Assert.AreEqual( (10, 12), TrackLineWriter.SnapEnd( (10, 10), (13, 13) ), "a tie |dx| = |dy| goes to Y (the jle at 0x53AB02)" );
		Assert.AreEqual( (10, 12), TrackLineWriter.SnapEnd( (10, 10), (12, 12) ) );
		Assert.AreEqual( (10, 10), TrackLineWriter.SnapEnd( (10, 10), (10, 10) ), "no offset, no line" );
		Assert.AreEqual( (12, 10), TrackLineWriter.SnapEnd( (10, 10), (12, 11) ), "X only when |dx| > |dy|" );
		var committed = TrackLineWriter.SnapEnd( (10, 10), (7, 10) );
		Assert.AreEqual( committed, TrackLineWriter.SnapEnd( (10, 10), committed ), "a snapped end snaps to itself, so a preview and its commit agree" );
		CollectionAssert.AreEqual( new[] { (14, 10), (14, 12), (14, 14) }, TrackLineWriter.LayLine( (14, 10), (14, 14) ).ToArray() );
		CollectionAssert.AreEqual( new[] { (14, 10), (12, 10) }, TrackLineWriter.LayLine( (14, 10), (12, 10) ).ToArray() );
	}

	[TestMethod]
	public void ALineWritesTilesChargesPerTileAndConnectsThem()
	{
		var rig = new Rig( economy: true, cash: 10000 );
		var before = rig.Economy!.Balance;
		var result = rig.Writer.Commit( (14, 10), (14, 14) );
		Assert.IsTrue( result.Completed );
		CollectionAssert.AreEqual( new[] { (14, 10), (14, 12), (14, 14) }, result.Built.ToArray() );
		Assert.AreEqual( 3 * WaterCost, result.Charged );
		Assert.AreEqual( before - 3 * WaterCost, rig.Economy.Balance );
		var cells = rig.Cells;
		Assert.AreEqual( TrackCellTypes.WaterOrigin, cells.RawTypeAt( 14, 12 ) );
		Assert.AreEqual( TrackCellTypes.Extension, cells.RawTypeAt( 15, 13 ) );
		Assert.AreEqual( 0x50, cells.Tracks.Get( 14, 10 ).Mask, "the first tile joins the station's open end (−X) and the next tile (+Y)" );
		Assert.AreEqual( 0x11, cells.Tracks.Get( 14, 12 ).Mask );
		Assert.AreEqual( 0x01, cells.Tracks.Get( 14, 14 ).Mask, "the end of the line is a stub" );
		// the same line again exists: free, and it bumps the overlap counters
		var again = rig.Writer.Commit( (14, 10), (14, 14) );
		Assert.AreEqual( 0, again.Charged );
		Assert.AreEqual( 3, again.Existing.Count );
		Assert.AreEqual( 1, cells.Tracks.Get( 14, 12 ).Overlap );
		Assert.AreEqual( before - 3 * WaterCost, rig.Economy.Balance );
	}

	[TestMethod]
	public void APreviewWritesNothingAndTheLineStopsAtTheFirstRefusal()
	{
		var rig = new Rig( economy: true, cash: 2 * WaterCost );
		var ghost = rig.Writer.Preview( (14, 10), (14, 18) );
		Assert.AreEqual( CellBuildResult.NotEnoughMoney, ghost.StoppedBy );
		Assert.AreEqual( 2, ghost.Built.Count, "two tiles are affordable" );
		Assert.AreEqual( 0, rig.Cells.Tracks.Get( 14, 10 ).Type, "a preview writes nothing" );
		var commit = rig.Writer.Commit( (14, 10), (14, 18) );
		Assert.AreEqual( CellBuildResult.NotEnoughMoney, commit.StoppedBy );
		Assert.AreEqual( 2, commit.Built.Count );
		Assert.AreEqual( (14, 14), commit.StoppedAt );
		Assert.AreEqual( 0, rig.Economy!.Balance );
	}

	[TestMethod]
	public void TilesRefuseObjectsQueuesAndLandTheTerrainRuleForbids()
	{
		var rig = new Rig();
		rig.Objects.Add( (15, 15) );
		Assert.AreEqual( CellBuildResult.Occupied, rig.Builder.CheckTile( 14, 14 ), "an object on one of the four cells" );
		rig.Cells.SetType( 21, 21, ParkCellType.Queue );
		Assert.AreEqual( CellBuildResult.QueueCell, rig.Builder.CheckTile( 20, 20 ) );
		rig.Terrain.Terrain[(25, 25)] = OriginalPlacementResult.Blocked;
		Assert.AreEqual( CellBuildResult.Terrain, rig.Builder.CheckTile( 24, 24 ) );
		rig.Terrain.Terrain[(27, 27)] = OriginalPlacementResult.Water;
		Assert.AreEqual( CellBuildResult.Ok, rig.Builder.CheckTile( 26, 26 ), "water is allowed for a water track" );
		Assert.AreEqual( CellBuildResult.OutsideTerrain, rig.Builder.CheckTile( 39, 10 ), "the tile's far cells leave the map" );
		rig.Cells.SetFlags( 30, 30, ParkCellFlags.Unowned );
		Assert.AreEqual( CellBuildResult.NotOwned, rig.Builder.CheckTile( 30, 30 ) );
		var kart = new TrackBuilder( rig.Cells, TrackFamily.Kart, () => null, rig.Terrain );
		Assert.AreEqual( CellBuildResult.Terrain, kart.CheckTile( 26, 26 ), "water is not allowed for a kart track" );
	}

	[TestMethod]
	public void ATileOverPathCellsClearsThemAndUnlinksTheirNeighbours()
	{
		var rig = new Rig();
		var grid = new GuestPathGrid( rig.Cells );
		foreach ( var (x, y) in new[] { (20, 20), (21, 20), (22, 20) } )
			grid.SetPath( x, y, true );
		Assert.AreEqual( CellBuildResult.Ok, rig.Builder.PlaceTile( 20, 20 ) );
		Assert.AreEqual( ParkCellType.Path, rig.Cells.TypeAt( 22, 20 ), "the path cell beside the tile stays" );
		Assert.AreEqual( TrackCellTypes.WaterOrigin, rig.Cells.RawTypeAt( 20, 20 ) );
		Assert.AreEqual( TrackCellTypes.Extension, rig.Cells.RawTypeAt( 21, 20 ) );
		Assert.AreEqual( 0, rig.Cells.LinksAt( 22, 20 ) & GuestPathGrid.LinkValue( 3 ), "the neighbouring path cell lost its link toward the cleared cell" );
		// [BIN:TPW-EXE:0x005390C0 paths under the last tile] a tile laid alone is the line's last tile: it took the paths away, so it bridges nothing
		Assert.AreEqual( 0, rig.Cells.Tracks.Get( 20, 20 ).Flags, "the paths under the last tile are gone and the tile is no bridge" );
		// a fixed (NoModify) path cell cannot be cleared
		rig.Cells.SetFlags( 30, 30, ParkCellFlags.NoModify );
		rig.Cells.SetType( 30, 30, ParkCellType.Path );
		Assert.AreEqual( CellBuildResult.NoModify, rig.Builder.CheckTile( 30, 30 ) );
	}

	// ---- The circuit walk ----------------------------------------------------------------------------

	[TestMethod]
	public void AClosedLoopWalksValidWithZeroBasedSegmentsAndFlowLinks()
	{
		var rig = new Rig();
		rig.LayLoop();
		var circuit = rig.Walk();
		Assert.IsTrue( circuit.IsValid, circuit.Failure );
		Assert.AreEqual( 8, circuit.Steps.Count );
		CollectionAssert.AreEqual( new[] { (10, 10), (12, 10), (14, 10), (14, 12), (12, 12), (10, 12), (8, 12), (8, 10) }, circuit.Steps.Select( step => (step.X, step.Y) ).ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 8 ).ToArray(), circuit.Steps.Select( step => step.Segment ).ToArray() );
		CollectionAssert.AreEqual( new byte[] { 0x04, 0x04, 0x10, 0x40, 0x40, 0x40, 0x01, 0x04 }, circuit.Steps.Select( step => step.Exit ).ToArray(), "the exit heading of each tile" );
		var tracks = rig.Cells.Tracks;
		Assert.AreEqual( 3, tracks.Get( 14, 12 ).Segment, "the walk numbers its steps from 0 (0x52B295)" );
		Assert.AreEqual( TrackHeading.NegY, tracks.Get( 8, 12 ).Link, "a corner's link is its new heading" );
		Assert.AreEqual( 0x04, tracks.Get( 12, 10 ).Link );
		// the four corners draw the first corner mesh (this loop turns counter-clockwise) and the straights the straight; station tiles draw nothing
		foreach ( var corner in new[] { (14, 10), (14, 12), (8, 12), (8, 10) } )
			Assert.AreEqual( 3, tracks.Get( corner.Item1, corner.Item2 ).Piece, $"corner {corner}" );
	}

	/// <summary>TRACK-014: the walk rewrites the flow link only at corners, so a straight keeps the direction its line was drawn in, whichever way the circuit runs.</summary>
	[TestMethod]
	public void AStraightKeepsTheDirectionItsLineWasDrawnIn()
	{
		var rig = new Rig();
		// the left side is drawn upward from the station row while the circuit flows down it
		Assert.IsTrue( rig.Writer.Commit( (8, 10), (8, 14) ).Completed );
		Assert.IsTrue( rig.Writer.Commit( (8, 14), (14, 14) ).Completed );
		Assert.IsTrue( rig.Writer.Commit( (14, 14), (14, 10) ).Completed );
		var tracks = rig.Cells.Tracks;
		Assert.AreEqual( TrackHeading.PosY, tracks.Get( 8, 12 ).Link, "drawn toward +Y" );
		Assert.AreEqual( TrackHeading.NegY, tracks.Get( 14, 12 ).Link, "drawn toward −Y" );
		Assert.AreEqual( TrackHeading.PosX, tracks.Get( 10, 10 ).Link, "the station tiles take the heading of the rotation (0x524F1E)" );
		var circuit = rig.Walk();
		Assert.IsTrue( circuit.IsValid, circuit.Failure );
		Assert.AreEqual( TrackHeading.NegY, circuit.Steps.Single( step => (step.X, step.Y) == (8, 12) ).Exit, "the circuit flows down the left side" );
		Assert.AreEqual( TrackHeading.PosY, tracks.Get( 8, 12 ).Link, "the walk leaves a straight's link alone" );
		Assert.AreEqual( TrackHeading.PosY, tracks.Get( 14, 10 ).Link, "a corner takes its new heading" );
		Assert.AreEqual( TrackHeading.PosY, circuit.Steps.Single( step => (step.X, step.Y) == (14, 10) ).Exit );
	}

	/// <summary>TRACK-014: a non-corner keeps the heading without testing its arms, and the walk fails only on the original's conditions.</summary>
	[TestMethod]
	public void AStraightIsNotTestedForAnArmTowardTheFlow()
	{
		var rig = new Rig();
		rig.LayLoop();
		var tracks = rig.Cells.Tracks;
		// a stray tile of type 11 whose mask has two parallel arms across the flow: the original keeps the heading through it
		var side = tracks.Get( 14, 10 );
		Assert.AreEqual( 0x50, side.Mask );
		var straight = tracks.Get( 12, 12 );
		straight.Mask = 0x11;
		tracks.Set( 12, 12, straight );
		var circuit = rig.Walk();
		Assert.IsTrue( circuit.IsValid, "no arm toward the flow is tested on a straight: " + circuit.Failure );
		Assert.AreEqual( TrackHeading.NegX, circuit.Steps.Single( step => (step.X, step.Y) == (12, 12) ).Exit, "the heading is kept" );
	}

	[TestMethod]
	public void StraightsOnTheLoopTurnWithTheirFlow()
	{
		var rig = new Rig();
		rig.LayLoop();
		var circuit = TrackCircuitWalker.WalkAndRefresh( rig.Builder, rig.Station );
		Assert.IsTrue( circuit.IsValid );
		var straight = rig.Cells.Tracks.Get( 12, 12 );
		Assert.AreEqual( (4, 2, 270), (straight.Family, straight.Piece, straight.Rotation), "a straight along X with the flow toward −X turns 180° from its 90° base" );
	}

	[TestMethod]
	public void AnOpenTrackFailsAndLeavesTheCircuitInvalid()
	{
		var rig = new Rig();
		Assert.IsTrue( rig.Writer.Commit( (14, 10), (14, 12) ).Completed );
		Assert.IsTrue( rig.Writer.Commit( (14, 12), (8, 12) ).Completed );
		var open = rig.Walk();
		Assert.IsFalse( open.IsValid, "the loop is not closed: the last tile has a single arm" );
		StringAssert.Contains( open.Failure!, "fewer than two arms" );
		// no track at all: the station's second tile has no neighbour
		var bare = new Rig();
		var bareWalk = bare.Walk();
		Assert.IsFalse( bareWalk.IsValid );
		Assert.AreEqual( 2, bareWalk.Steps.Count, "the two station tiles are walked before the open end" );
		// closing the loop makes it valid, and removing a tile of a closed loop breaks it again
		rig.Writer.Commit( (8, 12), (8, 10) );
		Assert.IsTrue( rig.Walk().IsValid );
		Assert.IsTrue( rig.Builder.RemoveTile( 12, 12 ) );
		Assert.IsFalse( rig.Walk().IsValid );
		Assert.AreEqual( 0, rig.Cells.RawTypeAt( 12, 12 ), "a removed tile frees its four cells" );
	}

	[TestMethod]
	public void ATeeEnteredAlongItsStemIsRefused()
	{
		var rig = new Rig();
		rig.LayLoop();
		Assert.IsTrue( rig.Walk().IsValid );
		// a stub below the corner (14, 10) makes it a three-arm tile entered along the stem (+X) with nothing ahead
		Assert.IsTrue( rig.Writer.Commit( (14, 10), (14, 8) ).Completed );
		var tee = rig.Cells.Tracks.Get( 14, 10 );
		Assert.AreEqual( 0x51, tee.Mask );
		var refused = rig.Walk();
		Assert.IsFalse( refused.IsValid );
		StringAssert.Contains( refused.Failure!, "junction" );
		// the same tee entered along its bar continues straight ahead: a stub off the straight (12, 12) leaves the circuit valid
		var along = new Rig();
		along.LayLoop();
		Assert.IsTrue( along.Writer.Commit( (12, 12), (12, 14) ).Completed );
		Assert.AreEqual( 0x54, along.Cells.Tracks.Get( 12, 12 ).Mask );
		Assert.IsTrue( along.Walk().IsValid, "entering a tee along its bar goes straight on" );
		Assert.AreEqual( 11, along.Walk().Steps[4].Kind, "a tee builds a crossing-style section" );
	}

	[TestMethod]
	public void ACrossingWalkedTwiceDuplicatesNoSectionAndOrsItsLinks()
	{
		var rig = new Rig();
		// a figure eight: the station row crosses the loop at (14, 10)
		Assert.IsTrue( rig.Writer.Commit( (14, 10), (18, 10) ).Completed );
		Assert.IsTrue( rig.Writer.Commit( (18, 10), (18, 14) ).Completed );
		Assert.IsTrue( rig.Writer.Commit( (18, 14), (14, 14) ).Completed );
		Assert.IsTrue( rig.Writer.Commit( (14, 14), (14, 8) ).Completed );
		Assert.IsTrue( rig.Writer.Commit( (14, 8), (8, 8) ).Completed );
		Assert.IsTrue( rig.Writer.Commit( (8, 8), (8, 10) ).Completed );
		var crossing = rig.Cells.Tracks.Get( 14, 10 );
		Assert.AreEqual( 0x55, crossing.Mask, "the station line and the loop cross here" );
		var circuit = rig.Walk();
		Assert.IsTrue( circuit.IsValid, circuit.Failure );
		Assert.AreEqual( 2, circuit.Steps.Count( step => (step.X, step.Y) == (14, 10) ), "the crossing is walked twice" );
		var sections = TrackSections.Build( circuit.Steps, 257, Array.Empty<Buoy>() );
		Assert.AreEqual( circuit.Steps.Count - 1, sections.Sections.Count, "the duplicate crossing section is ignored" );
		// the second pass ORs its heading into the link: +X (0x04) then −Y (0x01)
		var piece = rig.Cells.Tracks.Get( 14, 10 );
		Assert.AreEqual( 0x05, piece.Link );
		Assert.AreEqual( (4, 4, 0), (piece.Family, piece.Piece, piece.Rotation), "no rule of the second crossing family covers link 0x05" );
	}

	// ---- Bridges -------------------------------------------------------------------------------------

	/// <summary>TRACK-010: the bridge test takes every tile with at most two arms and no corner, so a single-arm end a path is laid over is a bridge end (tp.exe 0x538AA4..0x538B05, the path tool's op 0x85 0x534115).</summary>
	[TestMethod]
	public void ASingleArmEndOverAPathIsABridge()
	{
		var lone = new Rig();
		Assert.IsTrue( lone.Writer.Commit( (14, 10), (16, 10) ).Completed );
		var paths = new GuestPathGrid( lone.Cells );
		foreach ( var cell in TrackStation.TileCellsOf( 16, 10 ) )
			paths.SetPath( cell.X, cell.Y, true );
		var end = lone.Cells.Tracks.Get( 16, 10 );
		Assert.AreEqual( 0x40, end.Mask, "a stub with one arm" );
		Assert.AreEqual( TrackCell.FlagBridge, end.Flags & TrackCell.BridgeMask );
		Assert.AreEqual( 1 + TrackPieceSelector.PieceBridgeStem, end.Piece, "the end piece in its bridge variant" );
		Assert.IsTrue( TrackBuilder.IsBridgeShape( 0x00 ) && TrackBuilder.IsBridgeShape( 0x10 ) && TrackBuilder.IsBridgeShape( 0x44 ) );
		Assert.IsFalse( TrackBuilder.IsBridgeShape( 0x14 ) || TrackBuilder.IsBridgeShape( 0x54 ), "corners and tees are no bridges" );
	}

	[TestMethod]
	public void StraightsOverPathsBecomeBridgeVariantsByTheirNeighbours()
	{
		// a longer loop: the top row (14, 12) .. (6, 12) holds the straights (12, 12), (10, 12) and (8, 12) between two corners;
		// their cells hold paths, which the original keeps under the track and bridges over (tp.exe 0x538A92: map type 1 or 3)
		var rig = new Rig();
		var paths = new GuestPathGrid( rig.Cells );
		foreach ( var tile in new[] { (12, 12), (10, 12), (8, 12) } )
			foreach ( var cell in TrackStation.TileCellsOf( tile.Item1, tile.Item2 ) )
				paths.SetPath( cell.X, cell.Y, true );
		// the walks round the ride (op 0x86) give the straights their flag words, so the lines belong to the station's ride
		var writer = new TrackLineWriter( rig.Builder, owner: rig.Builder.OwnerOf( rig.Station ) );
		Assert.IsTrue( writer.Commit( (14, 10), (14, 12) ).Completed );
		Assert.IsTrue( writer.Commit( (14, 12), (6, 12) ).Completed );
		Assert.IsTrue( writer.Commit( (6, 12), (6, 10) ).Completed );
		Assert.IsTrue( writer.Commit( (6, 10), (8, 10) ).Completed );
		var circuit = TrackCircuitWalker.WalkAndRefresh( rig.Builder, rig.Station );
		Assert.IsTrue( circuit.IsValid, circuit.Failure );
		var tracks = rig.Cells.Tracks;
		// the flow runs −X along the top row: the first straight has its bridged neighbour ahead, the middle one both, the last one behind
		Assert.AreEqual( TrackCell.FlagBridgeUp, tracks.Get( 12, 12 ).Flags & TrackCell.BridgeMask );
		Assert.AreEqual( TrackCell.FlagBridgeAbove, tracks.Get( 10, 12 ).Flags & TrackCell.BridgeMask );
		Assert.AreEqual( TrackCell.FlagBridgeDown, tracks.Get( 8, 12 ).Flags & TrackCell.BridgeMask );
		Assert.AreEqual( 14, tracks.Get( 12, 12 ).Piece, "H_U straight (2 + 12)" );
		Assert.AreEqual( 18, tracks.Get( 10, 12 ).Piece, "H_A" );
		Assert.AreEqual( 17, tracks.Get( 8, 12 ).Piece, "H_D straight (2 + 15)" );
		Assert.AreEqual( 270, tracks.Get( 12, 12 ).Rotation, "the bridge straight turns with the flow like a plain one" );
		Assert.AreEqual( 3, tracks.Get( 14, 12 ).Piece, "a corner is never a bridge" );
		// water under a straight is no bridge (inside the park water cells are map type 0, tp.exe 0x536548); a straight with no
		// bridged neighbour over a cleared path is the plain hump (piece 11)
		var lone = new Rig();
		foreach ( var cell in TrackStation.TileCellsOf( 24, 24 ) )
			lone.Terrain.Terrain[cell] = OriginalPlacementResult.Water;
		Assert.IsTrue( lone.Writer.Commit( (22, 24), (26, 24) ).Completed );
		Assert.AreEqual( 0, lone.Cells.Tracks.Get( 24, 24 ).Flags & TrackCell.BridgeMask, "a straight over water stays on the water" );
		var grid = new GuestPathGrid( lone.Cells );
		grid.SetPath( 30, 30, true );
		Assert.IsTrue( lone.Writer.Commit( (28, 30), (32, 30) ).Completed );
		Assert.AreEqual( 11, lone.Cells.Tracks.Get( 30, 30 ).Piece, "the middle tile replaced a path cell: a bridge straight (H)" );
		Assert.AreEqual( TrackCell.FlagBridge, lone.Cells.Tracks.Get( 30, 30 ).Flags & TrackCell.BridgeMask );
		Assert.AreEqual( 1, lone.Cells.Tracks.Get( 28, 30 ).Piece, "a stub end stays the plain end piece" );
	}

	// ---- Ownership (TRACK-021) -----------------------------------------------------------------------

	[TestMethod]
	public void TwoAdjacentRidesNeverConnectAndADemolitionRemovesOnlyItsOwnTiles()
	{
		var rig = new Rig();
		var other = TrackStation.FromPlacement( 10, 13, 0 );
		rig.Builder.PlaceStation( other );
		var (ownerA, ownerB) = (rig.Builder.OwnerOf( rig.Station ), rig.Builder.OwnerOf( other ));
		Assert.AreNotEqual( ownerA, ownerB );
		var writerA = new TrackLineWriter( rig.Builder, owner: ownerA );
		var writerB = new TrackLineWriter( rig.Builder, owner: ownerB );
		Assert.IsTrue( writerA.Commit( (14, 10), (14, 12) ).Completed );
		Assert.IsTrue( writerB.Commit( (14, 14), (14, 16) ).Completed );
		var tracks = rig.Cells.Tracks;
		Assert.AreEqual( 0, tracks.Get( 14, 12 ).Mask & TrackHeading.PosY, "ride A's last tile does not reach into ride B's tile two cells away" );
		Assert.AreEqual( 0, tracks.Get( 14, 14 ).Mask & TrackHeading.NegY, "ride B's first tile does not take ride A's tile as a neighbour" );
		Assert.IsFalse( rig.Builder.Link( (14, 12), (14, 14) ), "an explicit link between the two rides is refused" );
		// a line of ride A that runs onto a tile of ride B stops there
		var onto = writerA.Commit( (14, 12), (14, 14) );
		Assert.IsFalse( onto.Completed );
		Assert.AreEqual( CellBuildResult.Occupied, onto.StoppedBy );
		Assert.AreEqual( ownerB, tracks.Get( 14, 14 ).Owner );
		// removing ride A's tiles leaves ride B's and its station
		var removed = rig.Builder.RemoveOwned( ownerA );
		Assert.AreEqual( 2, removed );
		Assert.IsFalse( tracks.Get( 14, 10 ).IsOrigin );
		Assert.IsTrue( tracks.Get( 14, 14 ).IsOrigin && tracks.Get( 14, 16 ).IsOrigin );
		Assert.AreEqual( TrackHeading.PosY, tracks.Get( 14, 14 ).Mask & TrackHeading.PosY );
		Assert.IsTrue( tracks.Get( 10, 14 ).IsOrigin && TrackCellTypes.IsStation( tracks.Get( 10, 14 ).Type ) );
		Assert.AreEqual( 0, rig.Builder.RemoveOwned( 0 ), "tiles laid without a ride are nobody's" );
	}

	/// <summary>TRACK-007: each cleared water or kart tile refunds cost × scrap percentage / 100 and the demolition charges twice that once (tp.exe 0x53B5FE, 0x528157).</summary>
	[TestMethod]
	public void ADemolitionRefundsEveryTileAndChargesTwoTilesOnce()
	{
		var economy = EconomyTestData.Park( initialCash: 100000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1100, out var ride ) );
		var percent = economy.ScrapPercent( ride! );
		Assert.IsTrue( percent > 0 );
		var before = economy.Balance;
		var refund = economy.RefundTrackTile( CellPurchase.WaterTrack, ride!.Id );
		Assert.AreEqual( WaterCost * percent / 100, refund );
		Assert.AreEqual( before + refund, economy.Balance );
		var charge = economy.ChargeTrackDemolition( CellPurchase.WaterTrack, ride.Id );
		Assert.AreEqual( 2 * WaterCost * percent / 100, charge );
		Assert.AreEqual( before + refund - charge, economy.Balance );
		Assert.AreEqual( 0, economy.RefundTrackTile( CellPurchase.WaterTrack, 12345 ), "an unknown ride refunds nothing" );
		Assert.AreEqual( 0, economy.ChargeTrackDemolition( CellPurchase.WaterTrack, 12345 ) );
		var rig = new Rig();
		var owner = rig.Builder.OwnerOf( rig.Station );
		var writer = new TrackLineWriter( rig.Builder, owner: owner );
		Assert.IsTrue( writer.Commit( (14, 10), (14, 12) ).Completed );
		Assert.AreEqual( 2, rig.Builder.OwnedTileCount( owner ), "the station tiles refund nothing and are not counted" );
		Assert.AreEqual( 0, rig.Builder.OwnedTileCount( 0 ) );
	}

	[TestMethod]
	public void ARideWalksOnlyItsOwnLineEvenWhenAnotherRideTouchesIt()
	{
		var rig = new Rig();
		var other = TrackStation.FromPlacement( 10, 13, 0 );
		rig.Builder.PlaceStation( other );
		var writerA = new TrackLineWriter( rig.Builder, owner: rig.Builder.OwnerOf( rig.Station ) );
		var writerB = new TrackLineWriter( rig.Builder, owner: rig.Builder.OwnerOf( other ) );
		Assert.IsTrue( writerA.Commit( (14, 10), (14, 12) ).Completed );
		Assert.IsTrue( writerA.Commit( (14, 12), (8, 12) ).Completed );
		Assert.IsTrue( writerA.Commit( (8, 12), (8, 10) ).Completed );
		// ride B's line runs right beside ride A's top row; no arm joins them
		Assert.IsTrue( writerB.Commit( (14, 14), (8, 14) ).Completed );
		var circuit = rig.Walk();
		Assert.IsTrue( circuit.IsValid, circuit.Failure );
		Assert.AreEqual( 8, circuit.Steps.Count );
	}
}
