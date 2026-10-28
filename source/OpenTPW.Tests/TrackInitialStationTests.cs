using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class TrackInitialStationTests
{
	private sealed class Grid : IParkGrid
	{
		public int Width => 64;
		public int Height => 64;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private static readonly TrackFamily[] Families = { TrackFamily.Kart, TrackFamily.Water };
	private static readonly int[] Rotations = { 0, 90, 180, 270 };
	private static (int X, int Y) Ahead( TrackStation s, int tiles ) =>
		(s.Exit.X + 2 * tiles * TrackHeading.Dx( s.Heading ), s.Exit.Y + 2 * tiles * TrackHeading.Dy( s.Heading ));

	[TestMethod]
	public void FreshStationHasFreeAdjacentOriginsAndOneInitialVertexInEveryRotation()
	{
		foreach ( var family in Families )
		foreach ( var rotation in Rotations )
		{
			var cells = new ParkCellMap( 64, 64 );
			var economy = EconomyTestData.Park( initialCash: 0 );
			var builder = new TrackBuilder( cells, family, () => economy, new Grid() );
			var station = TrackStation.FromPlacement( 30, 30, rotation );
			builder.PlaceInitialStation( station );
			Assert.AreEqual( 0L, economy.Balance );
			Assert.AreEqual( 16, cells.Tracks.Cells().Count() );
			foreach ( var at in station.Tiles.Concat( new[] { station.Exit, station.Entry } ) )
			{
				var origin = cells.Tracks.Get( at.X, at.Y );
				Assert.AreEqual( builder.OwnerOf( station ), origin.Owner );
				Assert.AreEqual( 0, origin.Overlap );
				Assert.AreEqual( at == station.Exit ? 0 : station.Heading, origin.Link );
				var mask = at == station.Exit ? TrackHeading.Opposite( station.Heading ) : at == station.Entry ? station.Heading : (byte)(station.Heading | TrackHeading.Opposite( station.Heading ));
				Assert.AreEqual( mask, origin.Mask );
				Assert.AreEqual( station.Tiles.Contains( at ) ? builder.StartType : builder.OriginType, origin.Type );
				foreach ( var child in TrackStation.TileCellsOf( at.X, at.Y ).Skip( 1 ) )
				{
					var record = cells.Tracks.Get( child.X, child.Y );
					Assert.AreEqual( TrackCellTypes.Extension, record.Type );
					Assert.AreEqual( cells.Tracks.IndexOf( at.X, at.Y ), record.Parent );
					Assert.AreEqual( origin.Owner, record.Owner );
				}
			}
			var tool = new CellBuildTool();
			new TrackEditor( builder, station ).EnterLineTool( tool );
			// [BIN:TPW-EXE:0x00530A87 collector stack] a walk that ends at the exit pushes it twice: the stack is kept as collected
			CollectionAssert.AreEqual( new[] { station.Exit, station.Exit }, tool.Vertices.ToArray() );
			Assert.IsTrue( tool.Undo(), "the zero-length line is taken back with the step (0, 0): the exit counts down and stays, nothing is refunded" );
			CollectionAssert.AreEqual( new[] { station.Exit }, tool.Vertices.ToArray(), "one point is left" );
			Assert.AreEqual( TrackHeading.Opposite( station.Heading ), cells.Tracks.Get( station.Exit.X, station.Exit.Y ).Mask );
			Assert.IsFalse( tool.Undo(), "the bottom point is no line" );
		}
	}

	[TestMethod]
	public void FirstLineChargesOnlyNewTilesAndReopenedUndoPreservesBothInitialEnds()
	{
		foreach ( var family in Families )
		foreach ( var rotation in Rotations )
		foreach ( var affordable in new[] { 0, 1, 2 } )
		{
			var cost = family == TrackFamily.Kart ? 400 : 500;
			var economy = EconomyTestData.Park( initialCash: affordable * cost );
			var ride = economy.RegisterExisting( 1100 );
			var cells = new ParkCellMap( 64, 64 );
			var builder = new TrackBuilder( cells, family, () => economy, new Grid() );
			var station = TrackStation.FromPlacement( 30, 30, rotation );
			builder.PlaceInitialStation( station );
			var cleared = 0;
			var editor = new TrackEditor( builder, station, new TrackEditHooks { TileCleared = _ => { cleared++; economy.RefundTrackTile( builder.Purchase, ride.Id ); } } );
			var line = new TrackLineWriter( builder, owner: builder.OwnerOf( station ), editor: editor ).Commit( station.Exit, Ahead( station, 2 ) );
			CollectionAssert.AreEqual( new[] { station.Exit }, line.Existing.ToArray() );
			Assert.AreEqual( affordable, line.Built.Count );
			Assert.AreEqual( (long)affordable * cost, line.Charged );
			Assert.AreEqual( 0L, economy.Balance );
			Assert.AreEqual( affordable == 2 ? CellBuildResult.Ok : CellBuildResult.NotEnoughMoney, line.StoppedBy );
			var tool = new CellBuildTool();
			editor.EnterLineTool( tool );
			while ( tool.Undo() ) { }
			Assert.AreEqual( affordable, cleared );
			Assert.AreEqual( 4, cells.Tracks.Cells().Count( x => x.Cell.IsOrigin ) );
			Assert.AreEqual( TrackHeading.Opposite( station.Heading ), cells.Tracks.Get( station.Exit.X, station.Exit.Y ).Mask );
			Assert.AreEqual( station.Heading, cells.Tracks.Get( station.Entry.X, station.Entry.Y ).Mask );
			Assert.IsFalse( editor.ClearCell( station.Exit, pass: false ) );
			Assert.IsFalse( editor.ClearCell( station.Entry, pass: false ) );
			Assert.AreEqual( affordable, cleared, "protected origins do not refund" );
			var before = economy.Balance;
			var refund = builder.TileCost * economy.ScrapPercent( ride ) / 100;
			Assert.AreEqual( 2, editor.Demolish().Count );
			var compensation = economy.ChargeTrackDemolition( builder.Purchase, ride.Id );
			Assert.AreEqual( before + 2 * refund - compensation, economy.Balance, "preserve separate refund and combined compensation rounding" );
		}
	}

	[TestMethod]
	public void ImmediateAndZeroLengthUndoKeepStationFacingArmsWithoutRefundingInitialOrigins()
	{
		foreach ( var family in Families )
		foreach ( var rotation in Rotations )
		{
			var cells = new ParkCellMap( 64, 64 );
			var builder = new TrackBuilder( cells, family, () => null, new Grid() );
			var station = TrackStation.FromPlacement( 30, 30, rotation );
			builder.PlaceInitialStation( station );
			var cleared = 0;
			var editor = new TrackEditor( builder, station, new TrackEditHooks { TileCleared = _ => cleared++ } );
			var writer = new TrackLineWriter( builder, owner: builder.OwnerOf( station ), editor: editor );
			var line = writer.Commit( station.Exit, Ahead( station, 1 ) );
			Assert.AreEqual( 1, cells.Tracks.Get( station.Exit.X, station.Exit.Y ).Overlap );
			writer.Undo( line );
			Assert.AreEqual( 1, cleared );
			foreach ( var at in new[] { station.Exit, station.Entry } )
				editor.UndoLine( new SegmentResult( at, Array.Empty<(int, int)>(), 0, CellBuildResult.Ok, false ) { Start = at } );
			Assert.AreEqual( 1, cleared );
			Assert.AreEqual( 4, cells.Tracks.Cells().Count( x => x.Cell.IsOrigin ) );
			Assert.AreEqual( TrackHeading.Opposite( station.Heading ), cells.Tracks.Get( station.Exit.X, station.Exit.Y ).Mask );
			Assert.AreEqual( station.Heading, cells.Tracks.Get( station.Entry.X, station.Entry.Y ).Mask );
		}
	}

	[TestMethod]
	public void FreePlacementDoesNotOverrideRefusedAdjacentCells()
	{
		foreach ( var family in Families )
		foreach ( var rotation in Rotations )
		{
			var cells = new ParkCellMap( 64, 64 );
			var economy = EconomyTestData.Park( initialCash: 10000 );
			var builder = new TrackBuilder( cells, family, () => economy, new Grid() );
			var station = TrackStation.FromPlacement( 30, 30, rotation );
			const int foreignOwner = 987;
			Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( station.Exit.X, station.Exit.Y, foreignOwner ) );
			var foreign = cells.Tracks.Get( station.Exit.X, station.Exit.Y );
			cells.SetType( station.Entry.X, station.Entry.Y, ParkCellType.Path );
			cells.SetFlags( station.Entry.X, station.Entry.Y, ParkCellFlags.NoModify );
			var balance = economy.Balance;
			builder.PlaceInitialStation( station );
			Assert.AreEqual( foreign, cells.Tracks.Get( station.Exit.X, station.Exit.Y ) );
			Assert.AreEqual( ParkCellType.Path, cells.TypeAt( station.Entry.X, station.Entry.Y ) );
			Assert.IsTrue( cells.Tracks.Get( station.Entry.X, station.Entry.Y ).IsEmpty );
			Assert.AreEqual( balance, economy.Balance );
			Assert.AreEqual( 3, cells.Tracks.Cells().Count( x => x.Cell.IsOrigin ), "station remains, but neither refused adjacent origin is overwritten" );
		}
	}

	[TestMethod]
	public void DemolitionConsumesBothVisitsOfACornerExitWithoutLeavingAnOrphan()
	{
		foreach ( var family in Families )
		foreach ( var rotation in Rotations )
		{
			var cells = new ParkCellMap( 64, 64 );
			var builder = new TrackBuilder( cells, family, () => null, new Grid() );
			var station = TrackStation.FromPlacement( 30, 30, rotation );
			builder.PlaceInitialStation( station );
			var dx = TrackHeading.Dy( station.Heading );
			var dy = -TrackHeading.Dx( station.Heading );
			var writer = new TrackLineWriter( builder, owner: builder.OwnerOf( station ) );
			Assert.IsTrue( writer.Commit( station.Exit, (station.Exit.X + 4 * dx, station.Exit.Y + 4 * dy) ).Completed );
			var cleared = new System.Collections.Generic.List<(int X, int Y)>();
			var editor = new TrackEditor( builder, station, new TrackEditHooks { TileCleared = at => cleared.Add( at ) } );
			editor.FindOpenEnd();
			Assert.AreEqual( 1, cells.Tracks.Get( station.Exit.X, station.Exit.Y ).Overlap );
			Assert.AreEqual( 4, editor.Demolish().Count );
			Assert.AreEqual( 4, cleared.Distinct().Count() );
			Assert.IsTrue( cleared.Contains( station.Exit ) && cleared.Contains( station.Entry ) );
			Assert.AreEqual( 2, cells.Tracks.Cells().Count( x => x.Cell.IsOrigin ) );
			Assert.AreEqual( 0, editor.Demolish().Count );
			Assert.AreEqual( 4, cleared.Count, "repeated demolition cannot refund twice" );
		}
	}

	[TestMethod]
	public void SaveRoundtripRetainsInitialCellsButLegacyStationStampDoesNotInventThem()
	{
		foreach ( var family in Families )
		foreach ( var rotation in Rotations )
		foreach ( var fresh in new[] { false, true } )
		{
			var cells = new ParkCellMap( 64, 64 );
			var builder = new TrackBuilder( cells, family, () => null, new Grid() );
			var station = TrackStation.FromPlacement( 30, 30, rotation );
			if ( fresh ) builder.PlaceInitialStation( station ); else builder.PlaceStation( station );
			if ( fresh ) builder.PlaceTile( station.Exit.X, station.Exit.Y, builder.OwnerOf( station ) );
			var save = TrackSave.Capture( cells, new BumpWorld(), Array.Empty<(int, BumpController)>() )!;
			var decoded = ParkResumeJson.Deserialize<TrackWorldSaveData>( ParkResumeJson.Serialize( save ) );
			var restored = new ParkCellMap( 64, 64 );
			TrackSave.RestoreCells( restored, decoded );
			Assert.AreEqual( fresh ? 16 : 8, restored.Tracks.Cells().Count() );
			foreach ( var item in cells.Tracks.Cells() )
				Assert.AreEqual( item.Cell, restored.Tracks.Get( item.X, item.Y ) );
			Assert.AreEqual( fresh ? 1 : 0, restored.Tracks.Get( station.Exit.X, station.Exit.Y ).Overlap );
		}
	}

	private static (TrackBuilder Builder, ParkCellMap Cells, TrackStation Station) Fresh( int rotation = 0, Func<int, int, bool>? occupied = null, ParkEconomy? economy = null )
	{
		var cells = new ParkCellMap( 64, 64 );
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => economy, new Grid(), occupied );
		return (builder, cells, TrackStation.FromPlacement( 30, 30, rotation ));
	}

	[TestMethod]
	public void TheStationLineAloneHasTheArmsOfTheLineAndTheExitAndEntryOpsGiveTheOthers()
	{
		foreach ( var rotation in Rotations )
		{
			var (builder, cells, station) = Fresh( rotation, occupied: ( x, y ) => false );
			// every cell of the exit tile is refused and the entry tile is free: the station keeps the arms of its own line plus the entry's hand-written one
			var blocked = TrackStation.TileCellsOf( station.Exit.X, station.Exit.Y ).ToHashSet();
			var refusing = new TrackBuilder( cells, TrackFamily.Kart, () => null, new Grid(), ( x, y ) => blocked.Contains( (x, y) ) );
			refusing.PlaceInitialStation( station );
			Assert.IsFalse( cells.Tracks.Get( station.Exit.X, station.Exit.Y ).IsOrigin, "the exit's type op was refused" );
			Assert.AreEqual( TrackHeading.Opposite( station.Heading ), cells.Tracks.Get( station.Second.X, station.Second.Y ).Mask, "no arm toward a tile that was not laid (0x539220 after a refused type op)" );
			Assert.AreEqual( (byte)(station.Heading | TrackHeading.Opposite( station.Heading )), cells.Tracks.Get( station.First.X, station.First.Y ).Mask, "the entry's arm back is written by hand" );
			Assert.AreEqual( station.Heading, cells.Tracks.Get( station.Entry.X, station.Entry.Y ).Mask );
			Assert.AreEqual( station.Heading, cells.Tracks.Get( station.Entry.X, station.Entry.Y ).Link );
		}
	}

	[TestMethod]
	public void ARefusedEntryStillGivesTheFirstStationTileItsArmTowardIt()
	{
		foreach ( var rotation in Rotations )
		{
			var (_, cells, station) = Fresh( rotation );
			var blocked = TrackStation.TileCellsOf( station.Entry.X, station.Entry.Y ).ToHashSet();
			var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new Grid(), ( x, y ) => blocked.Contains( (x, y) ) );
			builder.PlaceInitialStation( station );
			Assert.IsFalse( cells.Tracks.Get( station.Entry.X, station.Entry.Y ).IsOrigin );
			Assert.IsTrue( cells.Tracks.Get( station.Exit.X, station.Exit.Y ).IsOrigin );
			Assert.AreEqual( (byte)(station.Heading | TrackHeading.Opposite( station.Heading )), cells.Tracks.Get( station.First.X, station.First.Y ).Mask, "0x522700 on the cell two on runs whatever the type op answered (0x52572C)" );
			Assert.AreEqual( (byte)(station.Heading | TrackHeading.Opposite( station.Heading )), cells.Tracks.Get( station.Second.X, station.Second.Y ).Mask );
		}
	}

	[TestMethod]
	public void TheExitAndEntryAreFreeButAParkInDebtLaysNeither()
	{
		var broke = EconomyTestData.Park( initialCash: 0 );
		var (builder, cells, station) = Fresh( 0, economy: broke );
		builder.PlaceInitialStation( station );
		Assert.AreEqual( 16, cells.Tracks.Cells().Count(), "no cash needed" );
		Assert.AreEqual( 0L, broke.Balance, "and none spent" );
		var debt = EconomyTestData.Park( initialCash: 100 );
		debt.TrySpendCoasterTrack( 400, force: true );
		Assert.IsTrue( debt.Balance < 0 );
		var (indebted, debtCells, other) = Fresh( 0, economy: debt );
		indebted.PlaceInitialStation( other );
		Assert.IsFalse( debtCells.Tracks.Get( other.Exit.X, other.Exit.Y ).IsOrigin, "the type op's credit test refuses a free tile while the cash is below zero (0x538FF5)" );
		Assert.IsFalse( debtCells.Tracks.Get( other.Entry.X, other.Entry.Y ).IsOrigin );
		Assert.IsTrue( TrackCellTypes.IsStation( debtCells.Tracks.Get( other.First.X, other.First.Y ).Type ), "the station line itself is laid" );
	}

	[TestMethod]
	public void TheGiveWayHookSeesEveryCellOfTheExitAndEntryTiles()
	{
		var (builder, _, station) = Fresh( 90 );
		var seen = new List<(int X, int Y)>();
		builder.PlaceInitialStation( station, ( x, y ) => seen.Add( (x, y) ) );
		CollectionAssert.AreEquivalent( TrackStation.TileCellsOf( station.Exit.X, station.Exit.Y ).Concat( TrackStation.TileCellsOf( station.Entry.X, station.Entry.Y ) ).ToArray(), seen );
	}

	[TestMethod]
	public void AnotherRidesStationIsNeverDemolishedByAnExitTileOverIt()
	{
		var cells = new ParkCellMap( 64, 64 );
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new Grid() );
		var a = TrackStation.FromPlacement( 30, 30, 0 );
		builder.PlaceInitialStation( a );
		// ride B's exit tile (anchor + (4, 1)) lands on ride A's first station tile
		var b = TrackStation.FromPlacement( 26, 30, 0 );
		Assert.AreEqual( a.First, b.Exit );
		var seen = new List<(int X, int Y)>();
		builder.PlaceInitialStation( b, ( x, y ) => seen.Add( (x, y) ) );
		Assert.IsFalse( seen.Intersect( TrackStation.TileCellsOf( a.First.X, a.First.Y ) ).Any(), "op 0x87 asks nothing about a tile whose origin is a station" );
		Assert.IsTrue( TrackCellTypes.IsStation( cells.Tracks.Get( a.First.X, a.First.Y ).Type ), "ride A's station stays" );
		Assert.AreEqual( builder.OwnerOf( a ), cells.Tracks.Get( a.First.X, a.First.Y ).Owner );
	}
}
