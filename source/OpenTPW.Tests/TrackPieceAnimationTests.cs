using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Asset-free tests of the track pieces' water clip (docs/TRACKS.md, "Piece animation"): the original's two piece walks (0x52A490 from the exit along the flow,
/// 0x52A700 from the entry against it), the per-tile clip state, and BUMP 17's stored word that makes a repeated value a no-op.
/// </summary>
[TestClass]
public class TrackPieceAnimationTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) =>
			x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private sealed class Rig
	{
		public readonly ParkCellMap Cells = new( 40, 40 );
		public readonly TrackBuilder Builder;
		public readonly TrackStation Station = TrackStation.FromPlacement( 10, 9, 0 );

		public Rig()
		{
			Builder = new TrackBuilder( Cells, TrackFamily.Water, () => null, new FlatGrid(), ( _, _ ) => false );
			Builder.PlaceStation( Station );
		}

		public TrackCellLayer Layer => Builder.Tracks;

		public void Lay( (int X, int Y) from, (int X, int Y) to ) => Assert.IsTrue( new TrackLineWriter( Builder ).Commit( from, to ).Completed );

		/// <summary>The closed loop of eight tiles: station (10,10) (12,10), then (14,10) up to (14,12), left to (8,12), down to (8,10).</summary>
		public TrackCircuit LayLoop()
		{
			Lay( (14, 10), (14, 12) );
			Lay( (14, 12), (8, 12) );
			Lay( (8, 12), (8, 10) );
			return TrackCircuitWalker.WalkAndRefresh( Builder, Station );
		}
	}

	[TestMethod]
	public void TheForwardWalkRunsFromTheExitAlongTheFlowAndStopsBeforeTheStation()
	{
		var rig = new Rig();
		Assert.IsTrue( rig.LayLoop().IsValid );
		var forward = TrackPieceWalk.Forward( rig.Layer, rig.Station ).ToList();
		CollectionAssert.AreEqual( new[] { (14, 10), (14, 12), (12, 12), (10, 12), (8, 12), (8, 10) }, forward, "every piece of the loop in flow order, the station tiles left out" );
		Assert.IsTrue( forward.All( tile => rig.Layer.Get( tile.Item1, tile.Item2 ).Piece != 0 ) );
	}

	[TestMethod]
	public void TheBackwardWalkRunsFromTheEntryAgainstTheFlow()
	{
		var rig = new Rig();
		rig.LayLoop();
		var backward = TrackPieceWalk.Backward( rig.Layer, rig.Station ).ToList();
		CollectionAssert.AreEqual( new[] { (8, 10), (8, 12), (10, 12), (12, 12), (14, 12), (14, 10) }, backward );
	}

	[TestMethod]
	public void AnOpenTrackEndsTheWalkAtItsLastTileAndTheEmptyEntryWalksNothing()
	{
		var rig = new Rig();
		rig.Lay( (14, 10), (14, 14) );
		Assert.IsFalse( TrackCircuitWalker.WalkAndRefresh( rig.Builder, rig.Station ).IsValid );
		// the corner turns the walk up; the end stub has no arm ahead, so the walk stops after it
		CollectionAssert.AreEqual( new[] { (14, 10), (14, 12), (14, 14) }, TrackPieceWalk.Forward( rig.Layer, rig.Station ).ToList() );
		Assert.AreEqual( 0, TrackPieceWalk.Backward( rig.Layer, rig.Station ).Count(), "no track before the station: the first cell is no track tile" );
	}

	[TestMethod]
	public void TheCornerTestIsTheFourTwoArmBends()
	{
		var corners = Enumerable.Range( 0, 0x56 ).Where( mask => (mask & ~0x55) == 0 && TrackPieceWalk.IsCorner( (byte)mask ) ).ToArray();
		CollectionAssert.AreEqual( new[] { 0x05, 0x14, 0x41, 0x50 }, corners );
	}

	[TestMethod]
	public void APlayRestartsATileAndAStopFreezesItWhereItIs()
	{
		var rig = new Rig();
		rig.LayLoop();
		var clips = new TrackPieceAnimations();
		clips.Advance( 2 );
		clips.Trigger( rig.Layer, 14, 10, play: true );
		Assert.AreEqual( 2, clips.At( rig.Layer, 14, 10 )!.Value.Start );
		clips.Advance( 1.5 );
		Assert.AreEqual( 1.5, clips.Elapsed( clips.At( rig.Layer, 14, 10 )!.Value ), 1e-9 );
		var version = clips.Version;
		clips.Trigger( rig.Layer, 14, 10, play: false );
		Assert.AreEqual( 3.5, clips.At( rig.Layer, 14, 10 )!.Value.Stop );
		clips.Advance( 4 );
		Assert.AreEqual( 1.5, clips.Elapsed( clips.At( rig.Layer, 14, 10 )!.Value ), 1e-9, "a stopped clip keeps its last frame" );
		Assert.IsTrue( clips.Version > version );
		// a second stop changes nothing; a stop on a tile that never played leaves its stored mesh
		version = clips.Version;
		clips.Trigger( rig.Layer, 14, 10, play: false );
		clips.Trigger( rig.Layer, 8, 12, play: false );
		Assert.AreEqual( version, clips.Version );
		Assert.IsNull( clips.At( rig.Layer, 8, 12 ) );
		// a new play restarts the clip from its first frame (flags 7: at once)
		clips.Trigger( rig.Layer, 14, 10, play: true );
		Assert.AreEqual( 0, clips.Elapsed( clips.At( rig.Layer, 14, 10 )!.Value ) );
	}

	[TestMethod]
	public void AReChosenPieceIsANewInstanceWithoutAClip()
	{
		var rig = new Rig();
		rig.Lay( (14, 10), (16, 10) );
		TrackCircuitWalker.WalkAndRefresh( rig.Builder, rig.Station );
		var clips = new TrackPieceAnimations();
		clips.Trigger( rig.Layer, 16, 10, play: true );
		Assert.IsNotNull( clips.At( rig.Layer, 16, 10 ) );
		var before = rig.Layer.Get( 16, 10 ).Piece;
		// extending the line turns the end stub into a straight: the tile shows its stored mesh until the next play
		rig.Lay( (16, 10), (18, 10) );
		Assert.AreNotEqual( before, rig.Layer.Get( 16, 10 ).Piece );
		Assert.IsNull( clips.At( rig.Layer, 16, 10 ) );
	}

	[TestMethod]
	public void ALaidAgainTileDoesNotBringItsOldClipBack()
	{
		var rig = new Rig();
		rig.Lay( (14, 10), (16, 10) );
		TrackCircuitWalker.WalkAndRefresh( rig.Builder, rig.Station );
		var clips = new TrackPieceAnimations();
		clips.Trigger( rig.Layer, 16, 10, play: true );
		var piece = rig.Layer.Get( 16, 10 ).Piece;
		Assert.IsTrue( rig.Builder.RemoveTile( 16, 10 ) );
		clips.Prune( rig.Layer );
		Assert.AreEqual( 0, clips.Count, "the demolished tile's clip is dropped" );
		rig.Lay( (14, 10), (16, 10) );
		Assert.AreEqual( piece, rig.Layer.Get( 16, 10 ).Piece );
		Assert.IsNull( clips.At( rig.Layer, 16, 10 ), "the same piece on the same cell is a new instance with no clip" );
	}

	[TestMethod]
	public void BumpSeventeenTriggersThePiecesOnlyWhenItsValueChanges()
	{
		var water = new BumpController( BumperTemplates.Get( -5 ), () => 0 );
		var raised = new List<bool>();
		water.WaterPiecesTriggered += raised.Add;
		Assert.AreEqual( -1, water.WaterCommand, "the script attach starts the word at −1" );
		water.Command( 17, 0 );
		water.Command( 17, 0 );
		water.Command( 17, 1 );
		water.Command( 17, 1 );
		water.Command( 17, 2 );
		water.Command( 17, 0 );
		CollectionAssert.AreEqual( new[] { true, false, false, true }, raised, "0 starts, any other value stops, a repeated value does nothing" );
		Assert.AreEqual( 0, water.WaterCommand );
		Assert.IsTrue( water.WaterRunning );
	}

	[TestMethod]
	public void ARestoredControllerTriggersThePiecesOnItsNextBumpSeventeen()
	{
		var water = new BumpController( BumperTemplates.Get( -5 ), () => 0 );
		water.Command( 17, 0 );
		var saved = TrackSave.CaptureController( water, 1 );
		var restored = new BumpController( BumperTemplates.Get( -5 ), () => 0 );
		restored.Command( 17, 0 );
		restored.Restore( saved );
		Assert.AreEqual( -1, restored.WaterCommand, "the load's script restore sets the word to −1 again" );
		var raised = new List<bool>();
		restored.WaterPiecesTriggered += raised.Add;
		restored.Command( 17, 0 );
		CollectionAssert.AreEqual( new[] { true }, raised );
	}

	[TestMethod]
	public void ThePlacementWalkStartsAValidTrackAndALineStopsIt()
	{
		Assert.AreEqual( (true, false), TrackPieceAnimations.ForWalk( TrackWalkCause.Placement, valid: true ) );
		Assert.AreEqual( (false, true), TrackPieceAnimations.ForWalk( TrackWalkCause.Placement, valid: false ) );
		Assert.AreEqual( (false, false), TrackPieceAnimations.ForWalk( TrackWalkCause.Line, valid: true ), "a closing line stops the pieces even on a valid circuit" );
		Assert.AreEqual( (false, true), TrackPieceAnimations.ForWalk( TrackWalkCause.Line, valid: false ) );
		Assert.IsNull( TrackPieceAnimations.ForWalk( TrackWalkCause.Restore, valid: true ), "a restore creates every piece without a clip" );
		var rig = new Rig();
		rig.LayLoop();
		var clips = new TrackPieceAnimations();
		clips.TriggerTrack( rig.Layer, rig.Station, play: true, backward: false );
		Assert.AreEqual( 6, clips.Count );
		clips.Advance( 1 );
		clips.TriggerTrack( rig.Layer, rig.Station, play: false, backward: false );
		Assert.IsTrue( TrackPieceWalk.Forward( rig.Layer, rig.Station ).All( tile => clips.At( rig.Layer, tile.X, tile.Y )!.Value.Stop == 1 ) );
	}
}
