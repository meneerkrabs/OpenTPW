using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The clear op of the kart and water track tools on synthetic maps (docs/TRACKS.md, "The clear op"; TRACK-004, TRACK-007): every form of tp.exe <c>0x53B280</c> (the forced clear (0, 0),
/// the cut's pass (−1, −1) and the step of a replayed line (±2, 0) or (0, ±2)), its exit-tile tail and the replays of the demolition built from the corner stacks of <c>0x530830</c> and
/// <c>0x530B70</c>. The expected values are the original's rules read from the instructions, not recordings of the old model.
/// </summary>
[TestClass]
public class TrackClearOpTests
{
	private sealed class Grid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	/// <summary>The station of attraction cell (10, 9) at rotation 0: tiles (10, 10) and (12, 10), exit (14, 10), entry (8, 10), heading +X.</summary>
	private sealed class Rig
	{
		public readonly ParkCellMap Cells = new( 40, 40 );
		public readonly TrackBuilder Builder;
		public readonly TrackStation Station = TrackStation.FromPlacement( 10, 9, 0 );
		public readonly List<(int X, int Y)> Refunded = new();
		public readonly TrackEditor Editor;

		public Rig( TrackFamily family = TrackFamily.Kart )
		{
			Builder = new TrackBuilder( Cells, family, () => null, new Grid() );
			Builder.PlaceStation( Station );
			Editor = new TrackEditor( Builder, Station, new TrackEditHooks { TileCleared = at => Refunded.Add( at ) } );
		}

		public TrackCellLayer Tracks => Builder.Tracks;
		public int Owner => Builder.OwnerOf( Station );

		/// <summary>Writes a whole tile (origin and extension cells) with the given arms, link and overlap.</summary>
		public void Put( (int X, int Y) at, byte mask, byte link, short overlap )
		{
			foreach ( var (cx, cy) in TrackStation.TileCellsOf( at.X, at.Y ) )
			{
				var origin = (cx, cy) == at;
				Cells.SetType( cx, cy, origin ? Builder.OriginType : TrackCellTypes.Extension );
				Tracks.Set( cx, cy, new TrackCell
				{
					Type = origin ? Builder.OriginType : TrackCellTypes.Extension,
					Parent = origin ? -1 : Tracks.IndexOf( at.X, at.Y ),
					Owner = Owner,
					Mask = origin ? mask : (byte)0,
					Link = origin ? link : (byte)0,
					Overlap = origin ? overlap : (short)0
				} );
			}
		}

		public TrackCell At( (int X, int Y) at ) => Tracks.Get( at.X, at.Y );
	}

	[TestMethod]
	public void AForcedClearWritesMinusOneUnlessTheTileIsTheExit()
	{
		var rig = new Rig();
		rig.Put( (20, 20), 0x44, TrackHeading.PosX, 3 );
		Assert.IsTrue( rig.Editor.ClearCell( (20, 20), 0, 0 ), "(0, 0) forces the counter to −1 whatever it was (0x53B388), so the tile goes" );
		Assert.AreEqual( 0, rig.At( (20, 20) ).Type );
		CollectionAssert.AreEqual( new[] { (20, 20) }, rig.Refunded );

		var exit = rig.Station.Exit;
		rig.Put( exit, 0x40, TrackHeading.PosX, 2 );
		Assert.IsFalse( rig.Editor.ClearCell( exit, 0, 0 ) );
		Assert.AreEqual( 1, rig.At( exit ).Overlap, "the exit tile only counts down (0x53B393)" );
		Assert.IsFalse( rig.Editor.ClearCell( exit, 0, 0 ) );
		Assert.IsFalse( rig.Editor.ClearCell( exit, 0, 0 ), "below zero it is still spared outside a replay" );
		Assert.AreEqual( -1, rig.At( exit ).Overlap );
		Assert.IsTrue( rig.At( exit ).IsOrigin );
		Assert.IsTrue( rig.Editor.ClearCell( exit, 0, 0, replay: true ), "the demolition's flag clears it" );
	}

	[DataTestMethod]
	[DataRow( -2, 0, 0x04 )]
	[DataRow( 2, 0, 0x40 )]
	[DataRow( 0, -2, 0x10 )]
	[DataRow( 0, 2, 0x01 )]
	public void ALineStepTakesTheArmOppositeToItOutOfATileWithOneLinkBit( int dx, int dy, int armGone )
	{
		var rig = new Rig();
		rig.Put( (20, 20), 0x55, 0x01, 1 );
		Assert.IsFalse( rig.Editor.ClearCell( (20, 20), dx, dy ) );
		var after = rig.At( (20, 20) );
		Assert.AreEqual( (byte)(0x55 & ~armGone), after.Mask );
		Assert.AreEqual( 0, after.Overlap );
		Assert.AreEqual( (byte)0x01, after.Link, "a single link bit is not rewritten for a tile that was no corner" );
	}

	[TestMethod]
	public void ACornerLeftWithOneArmPointsItsLinkAwayFromIt()
	{
		var rig = new Rig();
		rig.Put( (20, 20), 0x05, 0x04, 1 );
		Assert.IsFalse( rig.Editor.ClearCell( (20, 20), -2, 0 ) );
		var after = rig.At( (20, 20) );
		Assert.AreEqual( (byte)0x01, after.Mask, "dx = −2 takes the +X arm (0x53B4D8)" );
		Assert.AreEqual( (byte)0x10, after.Link, "the link is the reverse of the arm left (0x53B538, 0x4D8C00, 0x5227E0)" );
	}

	[TestMethod]
	public void ALineStepSparesTheArmsOfTheExitAndEntryTilesButStillCountsDown()
	{
		var rig = new Rig();
		foreach (var at in new[] { rig.Station.Exit, rig.Station.Entry })
		{
			rig.Put( at, 0x55, 0x04, 2 );
			Assert.IsFalse( rig.Editor.ClearCell( at, -2, 0 ) );
			var after = rig.At( at );
			Assert.AreEqual( (byte)0x55, after.Mask, "0x53B3FA jumps to 0x53B54A: no arm is touched" );
			Assert.AreEqual( 1, after.Overlap );
		}
	}

	[DataTestMethod]
	[DataRow( 0, 2, 0x41, 0x40, 0x44 )]
	[DataRow( 0, -2, 0x14, 0x04, 0x44 )]
	[DataRow( 2, 0, 0x50, 0x10, 0x11 )]
	[DataRow( -2, 0, 0x05, 0x01, 0x11 )]
	public void ALineStepTakesItsBitOutOfALinkWithTwoBitsAndBothArmsOfItsAxis( int dx, int dy, int link, int expectedLink, int expectedMask )
	{
		var rig = new Rig();
		rig.Put( (20, 20), 0x55, (byte)link, 1 );
		Assert.IsFalse( rig.Editor.ClearCell( (20, 20), dx, dy ) );
		var after = rig.At( (20, 20) );
		Assert.AreEqual( (byte)expectedLink, after.Link );
		Assert.AreEqual( (byte)expectedMask, after.Mask );
		Assert.AreEqual( 0, after.Overlap );
	}

	[TestMethod]
	public void ASurvivingExitTileTakesBackTheArmTowardTheStationAndItsLink()
	{
		var rig = new Rig();
		var exit = rig.Station.Exit;
		// the cut's pass took the station-side arm: the tile keeps +Y only
		rig.Put( exit, 0x10, TrackHeading.PosY, 1 );
		Assert.IsFalse( rig.Editor.ClearCell( exit, 0, 0 ) );
		var after = rig.At( exit );
		Assert.AreEqual( (byte)0x50, after.Mask, "the second station tile is beside the exit on −X: its arm comes back (0x53B96D..0x53B980)" );
		Assert.AreEqual( (byte)0x04, after.Link, "with the flow link turned to the reverse of that side" );
	}

	[TestMethod]
	public void AnExitTileWithOneArmFacingTheStationTakesItsLinkFromIt()
	{
		var rig = new Rig();
		var exit = rig.Station.Exit;
		rig.Put( exit, 0x40, 0x11, 1 );
		rig.Editor.ClearCell( exit, 0, 0 );
		Assert.AreEqual( (byte)0x04, rig.At( exit ).Link, "a lone arm (0x522790 = 1): the link is cleared (0x5227F0) and set to the reverse of the arm that faces the station (0x53BA64)" );
		Assert.AreEqual( (byte)0x40, rig.At( exit ).Mask );
	}

	[TestMethod]
	public void TheExitTailDoesNotRunDuringAReplayOrOnOtherTiles()
	{
		var rig = new Rig();
		var exit = rig.Station.Exit;
		rig.Put( exit, 0x10, TrackHeading.PosY, 2 );
		rig.Editor.ClearCell( exit, 0, 0, replay: true );
		Assert.AreEqual( (byte)0x10, rig.At( exit ).Mask, "the flag 0x81B46C skips the tail (0x53B8E9)" );
		rig.Put( (20, 20), 0x10, TrackHeading.PosY, 2 );
		rig.Editor.ClearCell( (20, 20), 0, 0 );
		Assert.IsFalse( rig.At( (20, 20) ).IsOrigin );
	}

	[TestMethod]
	public void AClearedStationTileHasNoCellCostToRefund()
	{
		var rig = new Rig();
		Assert.IsTrue( rig.Editor.ClearCell( rig.Station.First, 0, 0 ), "a station tile is in the op's type set (0xD)" );
		Assert.AreEqual( 0, rig.Refunded.Count, "only types 0xB and 0x10 refund (0x53B5C6..0x53B627)" );
	}

	/// <summary>The loop of the other tests: the six laid tiles of a closed circuit, laid by the line tool.</summary>
	private static Rig Loop()
	{
		var rig = new Rig();
		var writer = new TrackLineWriter( rig.Builder, owner: rig.Owner );
		Assert.IsTrue( writer.Commit( (14, 10), (14, 12) ).Completed );
		Assert.IsTrue( writer.Commit( (14, 12), (8, 12) ).Completed );
		Assert.IsTrue( writer.Commit( (8, 12), (8, 10) ).Completed );
		return rig;
	}

	[TestMethod]
	public void TheReplayRunsTheLinesLatestFirstAndEachCornerTwice()
	{
		var rig = Loop();
		rig.Editor.Demolish();
		// the corner stack is exit, exit, (14, 12), (8, 12), entry, first station tile: the lines run first station tile → entry, entry → (8, 12), (8, 12) → (14, 12), (14, 12) → exit and exit → exit
		CollectionAssert.AreEqual( new[] { (8, 10), (8, 12), (10, 12), (12, 12), (14, 12), (14, 10) }, rig.Refunded );
		Assert.IsFalse( rig.Tracks.Get( 10, 10 ).IsOrigin, "the forward walk stands on the first station tile, so the first replayed line starts there" );
		Assert.IsTrue( rig.Tracks.Get( 12, 10 ).IsOrigin, "the second station tile is the ride removal's" );
	}

	[TestMethod]
	public void ADemolitionLeavesTheExitTilesStationFacingArmOnTheSecondStationTileGone()
	{
		var rig = Loop();
		rig.Editor.Demolish();
		Assert.AreEqual( (byte)0x40, rig.Tracks.Get( 12, 10 ).Mask, "the exit tile's clear took the second station tile's arm toward it" );
	}

	[TestMethod]
	public void AJunctionOnTheChainLosesOnePassAndKeepsTheBranchItCarries()
	{
		var rig = Loop();
		Assert.IsTrue( new TrackLineWriter( rig.Builder, owner: rig.Owner ).Commit( (12, 12), (12, 14) ).Completed );
		rig.Editor.Demolish();
		Assert.IsTrue( rig.Tracks.Get( 12, 12 ).IsOrigin, "the junction's counter is 1 (0x5228A0 from 0x530830) and one visit takes it to 0" );
		Assert.AreEqual( (byte)0x10, rig.Tracks.Get( 12, 12 ).Mask, "the step along X took both X arms (0x53B437)" );
		Assert.AreEqual( (byte)0x10, rig.Tracks.Get( 12, 12 ).Link, "and the opposite bit of the line's step out of the two-bit link (0x5227F0)" );
		Assert.IsTrue( rig.Tracks.Get( 12, 14 ).IsOrigin );
		Assert.IsFalse( rig.Refunded.Contains( (12, 12) ) );
	}

	[TestMethod]
	public void ADemolitionWalksTheEntrySideAgainstTheFlowAndStopsBeforeTheStation()
	{
		var rig = new Rig();
		var writer = new TrackLineWriter( rig.Builder, owner: rig.Owner );
		// a line that ends at the entry tile from the north: the walk from the entry goes up and stops at its end
		Assert.IsTrue( writer.Commit( (8, 14), (8, 10) ).Completed, "a line from (8, 14) down to the entry tile (8, 10)" );
		rig.Editor.Demolish();
		Assert.IsFalse( rig.Tracks.Get( 8, 14 ).IsOrigin );
		Assert.IsFalse( rig.Tracks.Get( 8, 12 ).IsOrigin );
		Assert.IsFalse( rig.Tracks.Get( 8, 10 ).IsOrigin );
		Assert.IsTrue( rig.Tracks.Get( 10, 10 ).IsOrigin && rig.Tracks.Get( 12, 10 ).IsOrigin, "the backward walk stops on the last laid tile and never steps onto the station" );
		Assert.AreEqual( 3, rig.Refunded.Count );
	}

	[TestMethod]
	public void ADemolitionOfAnEmptyStationClearsTheExitOnlyWhenItWasLaid()
	{
		var rig = new Rig();
		Assert.AreEqual( 0, rig.Editor.Demolish().Count );
		rig.Put( rig.Station.Exit, 0x40, 0, 0 );
		var cleared = rig.Editor.Demolish();
		CollectionAssert.AreEqual( new[] { rig.Station.Exit }, cleared.ToArray(), "the walk ends at once: exit and end are the same point, a zero-length line (0, 0) with the replay flag clears it" );
	}

	[TestMethod]
	public void TheLineStepRuleAlsoClearsABackspacedLineAndRepairsTheStation()
	{
		var rig = new Rig();
		var tool = new CellBuildTool();
		var editor = rig.Editor;
		var writer = new TrackLineWriter( rig.Builder, owner: rig.Owner, editor: editor );
		var segment = writer.Commit( (14, 10), (14, 14) );
		Assert.IsTrue( segment.Completed );
		writer.Undo( segment );
		Assert.IsFalse( rig.Tracks.Get( 14, 12 ).IsOrigin || rig.Tracks.Get( 14, 14 ).IsOrigin, "the new tiles of the line go" );
		Assert.IsTrue( rig.Tracks.Get( 14, 10 ).IsOrigin, "the exit stays (0x53B54A)" );
		Assert.AreEqual( (byte)0x40, rig.Tracks.Get( 14, 10 ).Mask, "only its arm toward the station is left" );
		Assert.IsNotNull( tool );
	}
}
