using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Supported two-flow crossings reached by demolition, using real line placement and the saved track layer.</summary>
[TestClass]
public class TrackDemolitionFlowTests
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
		public readonly TrackStation Station = TrackStation.FromPlacement( 10, 9, 0 );
		public readonly TrackBuilder Builder;
		public readonly TrackEditor Editor;
		public readonly List<(int X, int Y)> ClearedCallbacks = new();
		public readonly List<(int X, int Y)> AddOns = new();
		public Rig( TrackFamily family )
		{
			Builder = new( Cells, family, () => null, new Grid() );
			Builder.PlaceStation( Station );
			Editor = new( Builder, Station, new TrackEditHooks { TileCleared = at => ClearedCallbacks.Add( at ), RemoveAddOn = at => AddOns.Add( at ) } );
		}
		public void Line( (int X, int Y) from, (int X, int Y) to ) =>
			Assert.IsTrue( new TrackLineWriter( Builder, owner: Builder.OwnerOf( Station ) ).Commit( from, to ).Completed );
		public void Cross( int x, bool reverseHorizontal, bool reverseVertical = false )
		{
			Line( reverseVertical ? (x, 16) : (x, 10), reverseVertical ? (x, 10) : (x, 16) );
			Line( reverseHorizontal ? (x + 2, 12) : (x - 2, 12), reverseHorizontal ? (x - 2, 12) : (x + 2, 12) );
			Assert.AreEqual( 0x55, Cells.Tracks.Get( x, 12 ).Mask );
		}
	}

	[DataTestMethod]
	[DataRow( TrackFamily.Kart, 14, false )]
	[DataRow( TrackFamily.Kart, 14, true )]
	[DataRow( TrackFamily.Kart, 8, false )]
	[DataRow( TrackFamily.Kart, 8, true )]
	[DataRow( TrackFamily.Water, 14, false )]
	[DataRow( TrackFamily.Water, 14, true )]
	[DataRow( TrackFamily.Water, 8, false )]
	[DataRow( TrackFamily.Water, 8, true )]
	public void EitherCollectorPreservesTheOtherCrossingFlowAndItsSavedRotation( TrackFamily family, int x, bool reverseHorizontal )
	{
		var rig = new Rig( family );
		rig.Cross( x, reverseHorizontal );
		var flow = reverseHorizontal ? TrackHeading.NegX : TrackHeading.PosX;
		Assert.AreEqual( (byte)(flow | TrackHeading.PosY), rig.Cells.Tracks.Get( x, 12 ).Link );
		foreach ( var y in new[] { 12, 14 } )
		{
			var cell = rig.Cells.Tracks.Get( x, y );
			rig.Cells.Tracks.Set( x, y, cell with { Flags = TrackCell.FlagAddOn } );
		}
		rig.Line( (24, 24), (26, 24) ); // A detached piece is never part of either replay.
		// Count the per-clear refund hook; this fixture has no economy and does not assert bank amounts.
		var cleared = rig.Editor.Demolish();
		var survivor = rig.Cells.Tracks.Get( x, 12 );
		Assert.AreEqual( 0x44, survivor.Mask );
		Assert.AreEqual( flow, survivor.Link );
		Assert.AreEqual( 0, survivor.Overlap );
		Assert.AreEqual( reverseHorizontal ? 270 : 90, survivor.Rotation );
		Assert.AreEqual( 3, cleared.Count );
		Assert.AreEqual( 3, rig.ClearedCallbacks.Distinct().Count() );
		Assert.IsFalse( rig.ClearedCallbacks.Contains( (x, 12) ) );
		CollectionAssert.AreEqual( new[] { (x, 14) }, rig.AddOns.ToArray() );
		Assert.IsTrue( rig.Cells.Tracks.Get( x - 2, 12 ).IsOrigin && rig.Cells.Tracks.Get( x + 2, 12 ).IsOrigin );
		Assert.IsTrue( rig.Cells.Tracks.Get( 24, 24 ).IsOrigin );
		var saved = TrackSave.Capture( rig.Cells, new BumpWorld(), Array.Empty<(int, BumpController)>() )!;
		var decoded = ParkResumeJson.Deserialize<TrackWorldSaveData>( ParkResumeJson.Serialize( saved ) );
		var restored = new ParkCellMap( 40, 40 );
		foreach ( var cell in decoded.Cells ) restored.SetType( cell.X, cell.Y, cell.Type );
		TrackSave.RestoreCells( restored, decoded );
		Assert.AreEqual( survivor, restored.Tracks.Get( x, 12 ) );
		Assert.AreEqual( 0, rig.Editor.Demolish().Count, "repeating demolition cannot call TileCleared for the unreachable survivor" );
		Assert.AreEqual( 3, rig.ClearedCallbacks.Count );
	}

	[DataTestMethod]
	[DataRow( 14 )]
	[DataRow( 8 )]
	public void ContraryLaidFlowRemainsOutsideTheSupportedCorrection( int x )
	{
		var rig = new Rig( TrackFamily.Kart );
		rig.Cross( x, reverseHorizontal: true, reverseVertical: true );
		var before = rig.Cells.Tracks.Get( x, 12 ).Link;
		Assert.AreEqual( (byte)(TrackHeading.NegX | TrackHeading.NegY), before );
		rig.Editor.Demolish();
		Assert.AreEqual( before, rig.Cells.Tracks.Get( x, 12 ).Link, "do not infer laid flow from backward traversal or normalize every saved link" );
	}

	/// <summary>
	/// The line step of the clear op (0x53B280 with (0, ±2): the replay of the line from (14, 16) back to the exit runs along −Y, dy = −2) takes the opposite bit, +Y (0x10), out of a link with two or
	/// more bits, whatever the other bits are, and both arms of the axis out of the mask; the original does not look at which combinations a line can write.
	/// </summary>
	[DataTestMethod]
	[DataRow( 0x55, 0x45 )]
	[DataRow( 0x11, 0x01 )]
	[DataRow( 0x94, 0x84 )]
	public void TheLineStepTakesItsOppositeBitOutOfAnyLinkWithTwoOrMoreBits( int link, int expected )
	{
		var rig = new Rig( TrackFamily.Water );
		rig.Cross( 14, reverseHorizontal: false );
		var cell = rig.Cells.Tracks.Get( 14, 12 );
		rig.Cells.Tracks.Set( 14, 12, cell with { Link = (byte)link } );
		rig.Editor.Demolish();
		var survivor = rig.Cells.Tracks.Get( 14, 12 );
		Assert.AreEqual( (byte)expected, survivor.Link );
		Assert.AreEqual( 0x44, survivor.Mask, "the vertical axis goes, the horizontal one stays" );
		Assert.AreEqual( 0, survivor.Overlap, "the crossing's one pass of this chain" );
	}
}
