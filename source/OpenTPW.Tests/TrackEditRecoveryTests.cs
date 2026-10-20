using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Asset-free fixtures for recorded-corner Backspace replay. No original assets or executable bytes are part of the fixtures.</summary>
[TestClass]
public class TrackEditRecoveryTests
{
	private sealed class Grid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => x >= 0 && y >= 0 && x < Width && y < Height ? OriginalPlacementResult.Allowed : OriginalPlacementResult.OutsideTerrain;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private sealed class Rig
	{
		public readonly ParkCellMap Cells = new( 40, 40 );
		public readonly ParkEconomy Economy = EconomyTestData.Park( initialCash: 100000 );
		public readonly TrackStation Station = TrackStation.FromPlacement( 10, 9, 0 );
		public readonly TrackBuilder Builder;
		public readonly TrackEditor Editor;
		public readonly List<(int X, int Y)> Cleared = new();
		public readonly List<(int X, int Y)> AddOns = new();
		public readonly int Instance;

		public Rig( TrackFamily family = TrackFamily.Kart )
		{
			Instance = Economy.RegisterExisting( 1100 ).Id;
			Builder = new TrackBuilder( Cells, family, () => Economy, new Grid() );
			Builder.PlaceStation( Station );
			Editor = new TrackEditor( Builder, Station, new TrackEditHooks
			{
				TileCleared = at => { Cleared.Add( at ); Economy.RefundTrackTile( Builder.Purchase, Instance ); },
				RemoveAddOn = at => AddOns.Add( at )
			} );
		}

		public void Line( (int X, int Y) from, (int X, int Y) to ) =>
			Assert.IsTrue( new TrackLineWriter( Builder, owner: Builder.OwnerOf( Station ) ).Commit( from, to ).Completed );

		public CellBuildTool Enter()
		{
			var tool = new CellBuildTool();
			Editor.EnterLineTool( tool );
			return tool;
		}

		public ulong TrackHash()
		{
			var hash = new StateHasher();
			Cells.Tracks.AddCanonicalState( hash );
			return hash.Value;
		}
	}

	[DataTestMethod]
	[DataRow( TrackFamily.Kart )]
	[DataRow( TrackFamily.Water )]
	public void ExistingCornersUndoFromTheOpenEndAndRefundEveryRemovedTileOnce( TrackFamily family )
	{
		var rig = new Rig( family );
		rig.Line( (14, 10), (14, 14) );
		rig.Line( (14, 14), (10, 14) );
		var tool = rig.Enter();
		CollectionAssert.AreEqual( new[] { (14, 10), (14, 10), (14, 14), (10, 14) }, tool.Vertices.ToArray() );
		var before = rig.Economy.Balance;
		var refund = rig.Builder.TileCost * rig.Economy.ScrapPercent( rig.Economy.Objects.Single() ) / 100;
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( (14, 14), tool.Start );
		CollectionAssert.AreEqual( new[] { (10, 14), (12, 14) }, rig.Cleared.ToArray() );
		Assert.AreEqual( TrackHeading.NegY, rig.Cells.Tracks.Get( 14, 14 ).Mask );
		Assert.AreEqual( TrackHeading.PosY, rig.Cells.Tracks.Get( 14, 14 ).Link );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( rig.Station.Exit, tool.Start );
		Assert.IsTrue( tool.Undo(), "the collector's repeated first corner has its own final replay" );
		Assert.IsFalse( tool.Undo(), "the one-point station boundary cannot be popped" );
		Assert.AreEqual( 5, rig.Cleared.Count );
		Assert.AreEqual( 5, rig.Cleared.Distinct().Count() );
		Assert.AreEqual( before + 5 * refund, rig.Economy.Balance );
		Assert.AreEqual( 2, rig.Cells.Tracks.Cells().Count( cell => cell.Cell.IsOrigin ), "only station tiles remain" );
	}

	[TestMethod]
	public void NewLinesAndExistingLinesShareTheSameUndoAndRefundPath()
	{
		var rig = new Rig();
		rig.Line( (14, 10), (18, 10) );
		var tool = rig.Enter();
		var before = rig.Economy.Balance;
		var result = tool.Click( (18, 14) );
		Assert.IsTrue( result?.Completed == true );
		Assert.AreEqual( 2 * rig.Builder.TileCost, result!.Charged );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( (18, 10), tool.Start );
		Assert.AreEqual( before, rig.Economy.Balance, "a new unused ride's two tiles refund their complete price" );
		Assert.AreEqual( TrackHeading.NegX, rig.Cells.Tracks.Get( 18, 10 ).Mask );
		Assert.IsTrue( tool.Undo(), "then the old straight is replayed" );
		Assert.AreEqual( 5, rig.Cleared.Distinct().Count() );
		Assert.IsFalse( tool.Undo() );
	}

	[TestMethod]
	public void PreviewCancelAndReentryNeitherChargeNorRefundAndRebuildOnlyRemainingHistory()
	{
		var rig = new Rig();
		rig.Line( (14, 10), (14, 14) );
		rig.Line( (14, 14), (10, 14) );
		var tool = rig.Enter();
		Assert.IsTrue( tool.Undo() );
		var again = rig.Enter();
		var before = (rig.TrackHash(), rig.Economy.Balance, rig.Cleared.Count);
		again.Hover( (20, 14) );
		again.Cancel();
		Assert.AreEqual( before, (rig.TrackHash(), rig.Economy.Balance, rig.Cleared.Count) );
		var resumed = rig.Enter();
		Assert.AreEqual( (14, 14), resumed.Start );
		Assert.AreEqual( 2, resumed.SegmentCount );
		while ( resumed.Undo() ) { }
		Assert.AreEqual( 5, rig.Cleared.Count );
		Assert.AreEqual( 5, rig.Cleared.Distinct().Count(), "re-entry cannot refund already removed tiles" );
	}

	[TestMethod]
	public void AReplayedCrossingKeepsTheOtherFlowAndItsUnremovedTiles()
	{
		var rig = new Rig();
		rig.Line( (14, 10), (14, 16) );
		rig.Line( (12, 12), (16, 12) );
		var tool = rig.Enter();
		Assert.AreEqual( 0x55, rig.Cells.Tracks.Get( 14, 12 ).Mask );
		Assert.IsTrue( tool.Undo() );
		var crossing = rig.Cells.Tracks.Get( 14, 12 );
		Assert.IsTrue( crossing.IsOrigin );
		Assert.AreEqual( 0, crossing.Overlap );
		Assert.AreEqual( 0x44, crossing.Mask, "the reverse Y replay clears both Y arms when the link has two flow bits" );
		Assert.AreEqual( TrackHeading.PosX, crossing.Link, "only the line's forward flow bit is removed" );
		Assert.IsFalse( rig.Cleared.Contains( (14, 12) ) );
		Assert.IsTrue( rig.Cells.Tracks.Get( 12, 12 ).IsOrigin && rig.Cells.Tracks.Get( 16, 12 ).IsOrigin );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 3, rig.Cleared.Count );
	}

	[TestMethod]
	public void UndoRemovesAnAddOnOnlyWhenItsTileActuallyClears()
	{
		var rig = new Rig();
		rig.Line( (14, 10), (18, 10) );
		var tile = rig.Cells.Tracks.Get( 16, 10 );
		tile.Flags |= TrackCell.FlagAddOn;
		rig.Cells.Tracks.Set( 16, 10, tile );
		var tool = rig.Enter();
		Assert.IsTrue( tool.Undo() );
		CollectionAssert.AreEqual( new[] { (16, 10) }, rig.AddOns.ToArray() );
		Assert.IsFalse( tool.Undo() );
		Assert.IsFalse( rig.Enter().Undo() );
		Assert.AreEqual( 1, rig.AddOns.Count );
	}

	[TestMethod]
	public void AForeignTileAndTheStationCannotBeRemovedByAStaleReplay()
	{
		var rig = new Rig();
		var other = rig.Builder.OwnerOf( TrackStation.FromPlacement( 24, 20, 0 ) );
		new TrackLineWriter( rig.Builder, owner: other ).Commit( (20, 20), (24, 20) );
		rig.Editor.UndoLine( new SegmentResult( (24, 20), Array.Empty<(int, int)>(), 0, CellBuildResult.Ok, false ) { Start = (20, 20) } );
		rig.Editor.UndoLine( new SegmentResult( rig.Station.Second, Array.Empty<(int, int)>(), 0, CellBuildResult.Ok, false ) { Start = rig.Station.First } );
		// RepairStation may remove its already-dangling arm toward the empty entry; the station itself and foreign line remain intact.
		Assert.AreEqual( 5, rig.Cells.Tracks.Cells().Count( cell => cell.Cell.IsOrigin ) );
		Assert.AreEqual( other, rig.Cells.Tracks.Get( 22, 20 ).Owner );
		Assert.AreEqual( 0, rig.Cleared.Count );
		Assert.IsTrue( TrackCellTypes.IsStation( rig.Cells.Tracks.Get( rig.Station.First.X, rig.Station.First.Y ).Type ) );
	}

	[TestMethod]
	public void AReplayOutsideTheMapOrOffTheTileAxisDoesNotChangeTheTrack()
	{
		var rig = new Rig();
		rig.Line( (14, 10), (18, 10) );
		var before = (rig.TrackHash(), rig.Economy.Balance);
		foreach ( var end in new[] { (int.MaxValue, 10), (-2, 10), (18, 12), (17, 10) } )
			rig.Editor.UndoLine( new SegmentResult( end, Array.Empty<(int, int)>(), 0, CellBuildResult.Ok, false ) { Start = (14, 10) } );
		Assert.AreEqual( before, (rig.TrackHash(), rig.Economy.Balance) );
		Assert.AreEqual( 0, rig.Cleared.Count );
	}

	[TestMethod]
	public void SavedOpenTrackReconstructsTheSameUndoWithoutSavingToolHistory()
	{
		var rig = new Rig( TrackFamily.Water );
		rig.Line( (14, 10), (14, 14) );
		rig.Line( (14, 14), (10, 14) );
		var original = rig.Enter();
		Assert.IsTrue( original.Undo() );
		var saved = TrackSave.Capture( rig.Cells, new BumpWorld(), Array.Empty<(int, BumpController)>() )!;
		var decoded = ParkResumeJson.Deserialize<TrackWorldSaveData>( ParkResumeJson.Serialize( saved ) );
		var restored = new Rig( TrackFamily.Water );
		foreach ( var cell in saved.Cells ) restored.Cells.SetType( cell.X, cell.Y, cell.Type );
		TrackSave.RestoreCells( restored.Cells, decoded );
		var uninterrupted = rig.Enter();
		var resumed = restored.Enter();
		CollectionAssert.AreEqual( uninterrupted.Vertices.ToArray(), resumed.Vertices.ToArray() );
		while ( uninterrupted.Undo() ) Assert.IsTrue( resumed.Undo() );
		Assert.IsFalse( resumed.Undo() );
		Assert.AreEqual( rig.TrackHash(), restored.TrackHash() );
		Assert.AreEqual( 3, restored.Cleared.Count, "only the three saved tiles refund after loading" );
	}
}
